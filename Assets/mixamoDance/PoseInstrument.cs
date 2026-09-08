using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays a bank of humanoid poses as an instrument: each Trigger() adds the
/// clip on a fresh mixer input and blends it in over a short ease-out, while
/// older layers freeze at their live weight and fade underneath.
///
/// Knows nothing about MIDI. Input arrives through the public control surface
/// below, driven by separate components (KeyboardPoseDriver,
/// MidiFighterPoseDriver) - the same split PinDensityController uses.
///
/// WEIGHT NORMALIZATION IS LOAD-BEARING. The mixer does not auto-normalize,
/// and un-normalized humanoid weights pull the pose toward rest. At trigger
/// time every live layer weight is captured as its baseline; thereafter the
/// newest layer rides the curve at f and the rest scale by (1 - f), so the sum
/// stays at whatever it was before - 1. Do not "simplify" the baseline fade.
///
/// The one exception is the very first trigger of a session, where the
/// baselines sum to 0 and the total is only f for the length of one blend.
/// primePoseIndex exists to snap past that so the rig never shows a T-pose.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PoseInstrument : MonoBehaviour
{
    [Header("Pose Source")]
    [Tooltip("The bank of poses this instrument can play. Required.")]
    [SerializeField] private PoseBank poseBank;

    [Header("Blend (global defaults)")]
    [Tooltip("Seconds to blend a newly triggered pose in. 0 = hard cut.")]
    [SerializeField, Min(0f)] private float blendTime = 0.06f;
    [SerializeField] private AnimationCurve blendCurve = QuadraticEaseOut();

    [Header("Play / Freeze")]
    [Tooltip("OFF = tween to a (random) frozen frame and rest. ON = tween in and keep playing. " +
             "Captured at trigger time: the mode you were in when the pad fired governs that layer for its life.")]
    [SerializeField] private bool playMode = false;
    [Tooltip("Speed for layers in Play mode. Live-syncs to playing layers, so a knob bends " +
             "the tempo of the current motion. Negative = reverse.")]
    [SerializeField] private float playbackRate = 1f;
    [Tooltip("Freeze mode: pick a new random frame on every press, so hammering one pad " +
             "cycles frozen poses out of a single clip.")]
    [SerializeField] private bool newRandomFrameEachPress = true;
    [Tooltip("Freeze frame used when random is off (0..1 of clip length).")]
    [SerializeField, Range(0f, 1f)] private float freezeNormalizedTime = 0f;
    [Tooltip("Seed for the freeze-frame stream. Its own System.Random, never UnityEngine.Random, " +
             "so nothing else in the scene can perturb it and a set is reproducible.")]
    [SerializeField] private int randomSeed = 12345;

    [Header("Advanced")]
    [SerializeField] private bool  applyFootIK = false;
    [Tooltip("How many layers may overlap mid-blend before the mixer grows. Not the bank size.")]
    [SerializeField, Min(2)] private int initialMixerInputs = 16;
    [Tooltip("Ceiling on mixer growth, so a long set cannot ratchet capacity upward forever.")]
    [SerializeField, Min(2)] private int maxMixerInputs = 64;
    [SerializeField] private float cullThreshold = 0.001f;
    [Tooltip("Pose held at Start so the rig never flashes a T-pose. -1 to disable.")]
    [SerializeField] private int primePoseIndex = 0;

    // ---------------- trigger-time settings ----------------

    /// <summary>
    /// Everything a press resolves once and never revisits. Immutable so a
    /// layer rate override cannot be lost by a later mode change - the bug
    /// you get from tracking play/rate/rateLocked as separate mutable fields.
    /// </summary>
    private readonly struct TriggerSettings
    {
        public readonly bool  Play;
        public readonly float Rate;
        public readonly bool  RateLocked;   // uses its own rate; the global knob will not touch it
        public readonly float BlendTime;

        public TriggerSettings(bool play, float rate, bool rateLocked, float blendTime)
        {
            Play = play; Rate = rate; RateLocked = rateLocked; BlendTime = blendTime;
        }
    }

    private class Layer
    {
        public AnimationClipPlayable Playable;
        public int   Input;
        public float Baseline;   // weight captured when it stopped being newest
        public bool  Active;     // newest layer, ramping toward 1
        public bool  Playing;    // advancing vs frozen; FreezeAllNow/ResumeAllNow move this
        public TriggerSettings Settings;
    }

    // ---------------- runtime ----------------
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private Animator animator;

    private readonly List<Layer> layers = new List<Layer>();
    private readonly Stack<int>  freeInputs = new Stack<int>();
    private System.Random rng;
    private int   inputCapacity;
    private float elapsed;
    private float activeBlendTime;

    public int   PoseCount        => poseBank != null ? poseBank.Count : 0;
    public bool  PlayModeOn       => playMode;
    public float PlaybackRateNow  => playbackRate;
    public int   ActiveLayerCount => layers.Count;

    /// <summary>Clip time of the newest layer, in seconds. Debug/overlay only; -1 when idle.</summary>
    public double NewestLayerTime =>
        layers.Count > 0 ? layers[layers.Count - 1].Playable.GetTime() : -1d;

    // ---------------- setup ----------------
    private void Awake()
    {
        animator = GetComponent<Animator>();
        rng = new System.Random(randomSeed);

        graph = PlayableGraph.Create($"{name}-PoseInstrument");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        inputCapacity = Mathf.Max(2, initialMixerInputs);
        mixer = AnimationMixerPlayable.Create(graph, inputCapacity);
        for (int i = inputCapacity - 1; i >= 0; i--) freeInputs.Push(i);

        var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
    }

    private void Start()
    {
        if (poseBank == null)
        {
            Debug.LogWarning($"{name}: PoseInstrument has no PoseBank assigned - nothing to play.", this);
            return;
        }

        if (primePoseIndex >= 0 && primePoseIndex < poseBank.Count)
        {
            Trigger(primePoseIndex);
            if (layers.Count > 0)
                mixer.SetInputWeight(layers[layers.Count - 1].Input, 1f);  // snap on: no startup fade from rest
            elapsed = activeBlendTime;
        }
    }

    // ---------------- control surface ----------------

    public void Trigger(int index)
    {
        if (poseBank == null) return;
        if (!poseBank.TryGet(index, out var e)) return;

        // Re-baseline off live weights, so an interruption mid-blend is pop-free.
        foreach (var l in layers) { l.Baseline = mixer.GetInputWeight(l.Input); l.Active = false; }

        int slot = AcquireInput();
        if (slot < 0) return;

        var cp = AnimationClipPlayable.Create(graph, e.clip);
        cp.SetApplyFootIK(applyFootIK);

        var settings = Resolve(e);
        if (settings.Play)
        {
            cp.SetSpeed(settings.Rate);
            cp.SetTime(0d);
        }
        else
        {
            cp.SetSpeed(0d);
            float nt = newRandomFrameEachPress ? (float)rng.NextDouble() : freezeNormalizedTime;
            cp.SetTime(nt * e.clip.length);
        }

        graph.Connect(cp, 0, mixer, slot);
        mixer.SetInputWeight(slot, 0f);

        layers.Add(new Layer {
            Playable = cp, Input = slot, Baseline = 0f, Active = true,
            Playing = settings.Play, Settings = settings
        });

        activeBlendTime = settings.BlendTime;
        elapsed = 0f;
    }

    public void TriggerByName(string poseName)
    {
        if (poseBank == null) return;
        int i = poseBank.IndexOf(poseName);
        if (i >= 0) Trigger(i);
    }

    public void SetPlayMode(bool on)     => playMode = on;
    public void TogglePlayMode()         => playMode = !playMode;
    public void SetPlaybackRate(float r) => playbackRate = r;      // applied to live layers in Update
    public void SetBlendTime(float t)    => blendTime = Mathf.Max(0f, t);

    /// <summary>Lock every currently-playing layer at its live frame.</summary>
    public void FreezeAllNow()
    {
        foreach (var l in layers) { l.Playing = false; l.Playable.SetSpeed(0d); }
    }

    /// <summary>Resume everything at its resolved rate.</summary>
    public void ResumeAllNow()
    {
        foreach (var l in layers) { l.Playing = true; l.Playable.SetSpeed(RateFor(l)); }
    }

    // ---------------- per-frame ----------------
    private void Update()
    {
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            if (l.Playing) l.Playable.SetSpeed(RateFor(l));   // live rate sync
        }

        if (layers.Count == 0) return;

        elapsed += Time.deltaTime;
        float t = activeBlendTime <= 0f ? 1f : Mathf.Clamp01(elapsed / activeBlendTime);
        float f = blendCurve.Evaluate(t);

        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var l = layers[i];
            float w = l.Active ? f : l.Baseline * (1f - f);
            mixer.SetInputWeight(l.Input, w);

            if (!l.Active && w <= cullThreshold) Release(i);
        }
    }

    // ---------------- internals ----------------

    private double RateFor(Layer l) => l.Settings.RateLocked ? l.Settings.Rate : playbackRate;

    private TriggerSettings Resolve(PoseEntry e)
    {
        bool play = e.playOverride switch
        {
            PlayOverride.ForcePlay   => true,
            PlayOverride.ForceFreeze => false,
            _                        => playMode
        };
        float rate  = e.usePlaybackRateOverride ? e.playbackRateOverride : playbackRate;
        float blend = e.useBlendTimeOverride ? Mathf.Max(0f, e.blendTimeOverride) : blendTime;
        return new TriggerSettings(play, rate, e.usePlaybackRateOverride, blend);
    }

    private int AcquireInput()
    {
        if (freeInputs.Count == 0) ForceCullWeakest();
        if (freeInputs.Count == 0) GrowMixer();
        return freeInputs.Count > 0 ? freeInputs.Pop() : -1;
    }

    private void Release(int layerIndex)
    {
        var l = layers[layerIndex];
        mixer.DisconnectInput(l.Input);
        l.Playable.Destroy();
        freeInputs.Push(l.Input);
        layers.RemoveAt(layerIndex);
    }

    private void ForceCullWeakest()
    {
        int worst = -1;
        float worstW = float.MaxValue;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].Active) continue;                     // never cull the newest
            float w = mixer.GetInputWeight(layers[i].Input);
            if (w < worstW) { worstW = w; worst = i; }
        }
        if (worst >= 0) Release(worst);
    }

    private void GrowMixer()
    {
        if (inputCapacity >= maxMixerInputs) return;
        int old = inputCapacity;
        inputCapacity = Mathf.Min(maxMixerInputs, inputCapacity * 2);
        mixer.SetInputCount(inputCapacity);                     // preserves existing inputs
        for (int i = inputCapacity - 1; i >= old; i--) freeInputs.Push(i);
    }

    // h(t) = 1 - (1-t)^2 - monotonic, fast start, no overshoot.
    private static AnimationCurve QuadraticEaseOut() =>
        new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

    private void Reset()
    {
        blendTime               = 0.06f;
        blendCurve              = QuadraticEaseOut();
        playMode                = false;
        playbackRate            = 1f;
        newRandomFrameEachPress = true;
        freezeNormalizedTime    = 0f;
        randomSeed              = 12345;
        applyFootIK             = false;
        initialMixerInputs      = 16;
        maxMixerInputs          = 64;
        cullThreshold           = 0.001f;
        primePoseIndex          = 0;
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
