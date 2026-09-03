using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace Pincushioned.Diagnostics
{
    /// <summary>
    /// Samples Unity's profiler counters over a window of frames and prints a
    /// breakdown, so frame cost can be attributed without reading the Profiler
    /// window by eye.
    ///
    /// Resolves metrics by <b>name</b> through <see cref="ProfilerRecorderHandle"/>
    /// rather than by (category, name) pairs — category names vary between Unity
    /// versions, and guessing wrong yields an invalid recorder that reports zero.
    /// That reads as "this costs nothing" when it actually means "this never
    /// attached", so unresolved metrics are listed explicitly instead.
    ///
    /// Runs in players too — deliberately, because the answer in the editor is not
    /// the answer in a build. In a player the report goes to the player log.
    /// </summary>
    public class ProfilerProbe : MonoBehaviour
    {
        [Tooltip("Frames to sample before reporting. 120 is ~2s at 60fps and smooths " +
                 "out one-off spikes.")]
        [SerializeField] int _sampleFrames = 120;

        [Tooltip("Start a new sampling window as soon as one finishes.")]
        [SerializeField] bool _loop = true;

        [Tooltip("Write each report to the console / player log. In a build this is " +
                 "the only way to read it.")]
        [SerializeField] bool _logReport = true;

        [Tooltip("Skip this many frames before the first window, so startup hitches " +
                 "and shader warmup do not pollute the average.")]
        [SerializeField] int _warmupFrames = 180;

        int _warmup;

        [Tooltip("Metric names to sample. Anything that fails to resolve is reported " +
                 "as unresolved rather than silently reading zero.")]
        [SerializeField]
        string[] _metrics =
        {
            "Main Thread",
            "Render Thread",
            "CPU Total Frame Time",
            "GPU Frame Time",
            "PlayerLoop",
            "BehaviourUpdate",
            "Camera.Render",
            "RenderPipelineManager.DoRenderLoop_Internal",
            "Shadows.Draw",
            "Culling",
            "Draw Calls Count",
            "SetPass Calls Count",
            "Batches Count",
            "Triangles Count",
            "Shadow Casters Count",
            "GC Allocated In Frame",
        };

        readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        readonly List<string>           _names     = new List<string>();
        readonly List<bool>             _isTiming  = new List<bool>();
        readonly List<string>           _missing   = new List<string>();

        int _frames;

        /// <summary>Latest report. Empty until the first window completes.</summary>
        public string Report { get; private set; } = "";

        /// <summary>True while a sampling window is open.</summary>
        public bool Sampling { get; private set; }

        void OnEnable()  => Begin();
        void OnDisable() => Stop();

        /// <summary>Start (or restart) a sampling window.</summary>
        public void Begin()
        {
            Stop();

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            var byName = new Dictionary<string, ProfilerRecorderHandle>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (!string.IsNullOrEmpty(d.Name) && !byName.ContainsKey(d.Name))
                    byName[d.Name] = h;
            }

            _missing.Clear();
            foreach (var name in _metrics)
            {
                if (!byName.TryGetValue(name, out var handle)) { _missing.Add(name); continue; }

                var desc = ProfilerRecorderHandle.GetDescription(handle);
                // StartNew takes (category, name); only the constructor accepts a
                // resolved handle, so build it directly and start it explicitly.
                var rec = new ProfilerRecorder(handle, Mathf.Max(1, _sampleFrames),
                                               ProfilerRecorderOptions.Default);
                if (!rec.Valid) { _missing.Add(name + " (invalid)"); rec.Dispose(); continue; }
                rec.Start();

                _recorders.Add(rec);
                _names.Add(name);
                _isTiming.Add(desc.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds);
            }

            _frames  = 0;
            Sampling = true;
        }

        /// <summary>Stop sampling and release the recorders.</summary>
        public void Stop()
        {
            for (int i = 0; i < _recorders.Count; i++)
                if (_recorders[i].Valid) _recorders[i].Dispose();
            _recorders.Clear();
            _names.Clear();
            _isTiming.Clear();
            Sampling = false;
        }

        void Update()
        {
            if (_warmup < _warmupFrames) { _warmup++; _frames = 0; return; }
            if (!Sampling) return;
            if (++_frames < _sampleFrames) return;

            Report   = BuildReport();
            Sampling = false;
            if (_logReport) Debug.Log(Report);
            if (_loop) Begin();
        }

        string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ProfilerProbe — mean over " + _frames + " frames ===");

            for (int i = 0; i < _recorders.Count; i++)
            {
                var rec = _recorders[i];
                if (!rec.Valid) continue;

                var samples = rec.ToArray();
                if (samples == null || samples.Length == 0)
                {
                    sb.AppendLine(_names[i].PadRight(46) + " (no samples)");
                    continue;
                }

                double total = 0d;
                for (int s = 0; s < samples.Length; s++) total += samples[s].Value;
                double mean = total / samples.Length;

                sb.AppendLine(_isTiming[i]
                    ? _names[i].PadRight(46) + (mean * 1e-6).ToString("F2").PadLeft(10) + " ms"
                    : _names[i].PadRight(46) + mean.ToString("N0").PadLeft(10));
            }

            if (_missing.Count > 0)
                sb.AppendLine("\nunresolved: " + string.Join(", ", _missing));

            return sb.ToString();
        }
    }
}
