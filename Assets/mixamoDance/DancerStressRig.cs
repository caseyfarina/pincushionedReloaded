using System.Collections;
using UnityEngine;
using Pincushioned.Diagnostics;

/// <summary>
/// Throwaway measurement harness for the PoseInstrument scaling sweep.
/// Spawns N dancers, optionally unbinds the hat from skinning, optionally adds
/// extra cameras to stand in for split-screen cells, waits for one ProfilerProbe
/// window, prints a labelled report and quits.
///
/// One config per process, driven by command line, so no state bleeds between
/// runs:
///   Dancer.exe -dancers 12 -unbindhat -cameras 8
///
/// Delete this once the sweep is done. It is not part of the instrument.
/// </summary>
public class DancerStressRig : MonoBehaviour
{
    [SerializeField] private GameObject dancerPrefab;
    [SerializeField] private ProfilerProbe probe;

    [Header("Editor defaults (command line overrides these in a build)")]
    [SerializeField] private int  dancers    = 2;
    [SerializeField] private bool unbindHat  = false;
    [SerializeField] private int  extraCameras = 0;
    [SerializeField] private bool quitWhenDone = true;

    [Tooltip("Seconds between automatic pose triggers per dancer, so the sweep " +
             "measures animation actually running rather than a static pose.")]
    [SerializeField] private float triggerInterval = 0.8f;

    private string label;

    private void Awake()
    {
        // A standalone player stops ticking when it loses focus, and these runs
        // are launched from a script that never gives it focus. Without this the
        // probe window never closes and the process hangs.
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;   // vsync would floor every config at 60fps
        Application.targetFrameRate = -1;

        ParseCommandLine();
        label = $"dancers={dancers} hat={(unbindHat ? "UNBOUND" : "skinned")} cameras={1 + extraCameras}";

        SpawnDancers();
        SpawnCameras();
        Debug.Log($"===STRESS CONFIG=== {label}");

        StartCoroutine(ReportWhenReady());
    }

    private void ParseCommandLine()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-dancers":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var d)) dancers = d;
                    break;
                case "-cameras":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var c)) extraCameras = c;
                    break;
                case "-unbindhat":
                    unbindHat = true;
                    break;
            }
        }
    }

    private void SpawnDancers()
    {
        if (dancerPrefab == null) { Debug.LogError("STRESS: no dancer prefab"); return; }

        int cols = Mathf.CeilToInt(Mathf.Sqrt(dancers));
        for (int i = 0; i < dancers; i++)
        {
            int x = i % cols, z = i / cols;
            var go = Instantiate(dancerPrefab,
                                 new Vector3((x - (cols - 1) * 0.5f) * 1.6f, 0f, z * 1.6f),
                                 Quaternion.identity);
            go.name = $"Dancer_{i}";

            var kb = go.GetComponent<KeyboardPoseDriver>();
            if (kb != null) kb.enabled = false;          // one overlay is plenty, and it is not the subject

            if (unbindHat) UnbindHat(go);

            var inst = go.GetComponent<PoseInstrument>();
            if (inst != null)
            {
                inst.SetPlayMode(true);                  // clips running = the expensive case
                StartCoroutine(AutoTrigger(inst, i));
            }
        }
    }

    /// <summary>
    /// Replaces a single-bone SkinnedMeshRenderer with a static MeshRenderer
    /// parented to that bone. The hat is rigid; skinning it every frame buys
    /// nothing. Placement uses the inverse bindpose so it lands where the
    /// skinned version did.
    /// </summary>
    private static void UnbindHat(GameObject root)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null || smr.bones.Length != 1) continue;

            var bone = smr.bones[0];
            var mesh = smr.sharedMesh;
            var mats = smr.sharedMaterials;

            var go = new GameObject(smr.name + "_Static");
            go.transform.SetParent(bone, false);

            var m = bone.localToWorldMatrix * mesh.bindposes[0].inverse;
            go.transform.position   = m.GetColumn(3);
            go.transform.rotation   = m.rotation;
            go.transform.localScale = m.lossyScale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;

            smr.enabled = false;
        }
    }

    private void SpawnCameras()
    {
        var main = Camera.main;
        if (main == null || extraCameras <= 0) return;

        // Stand-ins for split-screen cells: each is a full scene render, tiled so
        // they all cover part of the screen the way the mosaic rig does.
        int cols = Mathf.CeilToInt(Mathf.Sqrt(extraCameras + 1));
        int rows = Mathf.CeilToInt((extraCameras + 1f) / cols);
        float w = 1f / cols, h = 1f / rows;

        main.rect = new Rect(0f, 0f, w, h);
        for (int i = 1; i <= extraCameras; i++)
        {
            var go = new GameObject($"Cell_{i}");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            go.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            go.transform.RotateAround(Vector3.zero, Vector3.up, i * (360f / (extraCameras + 1)));
            cam.rect = new Rect((i % cols) * w, (i / cols) * h, w, h);
        }
    }

    private IEnumerator AutoTrigger(PoseInstrument inst, int seed)
    {
        var rng = new System.Random(seed * 7919 + 13);
        yield return new WaitForSeconds((float)rng.NextDouble() * triggerInterval);
        while (true)
        {
            if (inst.PoseCount > 0) inst.Trigger(rng.Next(inst.PoseCount));
            yield return new WaitForSeconds(triggerInterval);
        }
    }

    private IEnumerator ReportWhenReady()
    {
        if (probe == null) probe = FindFirstObjectByType<ProfilerProbe>();
        if (probe == null) { Debug.LogError("STRESS: no ProfilerProbe"); yield break; }

        float timeout = Time.realtimeSinceStartup + 60f;
        while (string.IsNullOrEmpty(probe.Report) && Time.realtimeSinceStartup < timeout)
            yield return null;

        Debug.Log($"===STRESS BEGIN=== {label}\n{probe.Report}\n===STRESS END===");
        yield return null;

        if (quitWhenDone) Application.Quit();
    }
}
