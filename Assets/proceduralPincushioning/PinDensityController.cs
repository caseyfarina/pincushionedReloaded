using UnityEngine;

/// <summary>
/// Single owner of pin scatter count across every active MeshSurfaceScatter.
///
/// Two independent inputs multiply:
///   Ceiling01  — the performer's hand (MIDI Mix Ch 1, Row 2 knob) sets intensity.
///   Envelope01 — the live audio envelope drives motion within that intensity.
///
/// Both default to 1, so either driver is optional: with no audio driver present
/// the knob alone controls count exactly as it did before, and with no MIDI the
/// audio sweeps the full range.
///
/// Why a single owner: MeshSurfaceScatter.SetCount() runs a full Scatter(),
/// which rebuilds every instance matrix and allocates a Matrix4x4[] per batch.
/// Two components calling it independently would rescatter twice per frame and
/// fight over the value. This funnels both inputs into one rate-limited write.
///
/// Place this on the Pincushioned Rig object in the scene. Its count range and
/// cost limits are authored in the Inspector, not in code.
/// </summary>
public class PinDensityController : MonoBehaviour
{
    [Header("Count range")]
    [Tooltip("Pin count when the combined signal is 0.")]
    public int minCount = 0;

    [Tooltip("Pin count when the combined signal is 1.")]
    public int maxCount = 2000;

    [Header("Cost control")]
    [Tooltip("Maximum rescatters per second. Scatter() is not free — it rebuilds " +
             "every instance matrix and allocates. 0 disables the limit.")]
    public float maxUpdatesPerSecond = 30f;

    [Tooltip("Ignore changes smaller than this many pins. Stops audio jitter from " +
             "triggering a rescatter that is not visible anyway.")]
    public int countDeadband = 8;

    [Tooltip("Seconds between re-finding the scatter set. Floors are enabled and " +
             "disabled at runtime, so a Start-only scan goes stale. 0 disables.")]
    public float rescanInterval = 0.5f;

    [Header("Audio agitation")]
    [Tooltip("Let audio drive the noise field's drift instead of the pin count. " +
             "Loud passages reconfigure WHICH pins are visible; the count stays " +
             "whatever the MIDI knob asked for. " +
             "REQUIRES each scatter's Density Mode to be Noise - noiseOffset does " +
             "nothing in Uniform mode, so pins will also cluster rather than spread.")]
    public bool agitationEnabled = false;

    [Tooltip("World-space direction the noise field travels. Any non-zero vector; " +
             "it is normalised. An axis-aligned value makes the motion read as a " +
             "single sweep, a skewed one as tumbling.")]
    public Vector3 driftDirection = new Vector3(1f, 0.35f, 0.7f);

    [Tooltip("Noise-field units travelled per second at full amplitude. Higher = " +
             "the visible configuration turns over faster on loud passages.")]
    public float driftSpeed = 2f;

    [Tooltip("Ignore drift smaller than this. The counterpart of Count Deadband: " +
             "stops inaudible signal from forcing a rescatter every frame.")]
    public float driftEpsilon = 0.01f;

    // ── inputs ───────────────────────────────────────────────────────────

    static float _ceiling01   = 1f;
    static float _envelope01  = 1f;
    static float _agitation01 = 0f;
    static bool  _dirty       = true;

    /// <summary>Performer intensity, 0–1. Written by MidiMixPinDensityDriver.</summary>
    public static float Ceiling01
    {
        get => _ceiling01;
        set { _ceiling01 = Mathf.Clamp01(value); _dirty = true; }
    }

    /// <summary>Live audio envelope, 0–1. Written by AudioPinDensityDriver.</summary>
    public static float Envelope01
    {
        get => _envelope01;
        set { _envelope01 = Mathf.Clamp01(value); _dirty = true; }
    }

    /// <summary>
    /// Live audio agitation, 0–1. Written by AudioScatterAgitationDriver.
    ///
    /// Deliberately does NOT set _dirty: agitation acts by integrating drift over
    /// time, and the drift check in Update decides when a rescatter is warranted.
    /// Marking dirty here would rescatter on every audio callback.
    /// </summary>
    public static float Agitation01
    {
        get => _agitation01;
        set { _agitation01 = Mathf.Clamp01(value); }
    }

    // ── lifecycle ────────────────────────────────────────────────────────

    MeshSurfaceScatter[] _scatters;
    int     _appliedCount = -1;
    float   _nextApplyTime;
    int     _activeCount  = -1;
    float   _nextRescanTime;
    Vector3 _driftOffset;
    Vector3 _appliedDrift;

    Vector3 DriftAxis =>
        driftDirection.sqrMagnitude > 1e-6f ? driftDirection.normalized : Vector3.right;

    void OnEnable()
    {
        // Statics survive "Enter Play Mode without domain reload", so reset them
        // here rather than relying on field initializers.
        _ceiling01   = 1f;
        _envelope01  = 1f;
        _agitation01 = 0f;
        _dirty       = true;
    }

    void Start() => Rescan();

    /// <summary>
    /// Re-find the scatter instances.
    ///
    /// MUST use FindObjectsInactive.Include. FloorVisibilityController disables
    /// every floor except the one you are standing on, so an Exclude scan at
    /// Start only ever sees the startup floor -- and both the knob and the audio
    /// silently stop affecting every other floor in the building. That reads as
    /// "the audio reactivity broke", when in fact it never left floor one.
    ///
    /// Inactive scatters are collected but skipped at apply time, so a floor that
    /// becomes visible later is already in the set and gets written on the next
    /// apply rather than keeping its authored count.
    /// </summary>
    public void Rescan()
    {
        _scatters = Object.FindObjectsByType<MeshSurfaceScatter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int active = 0;
        foreach (var s in _scatters)
            if (s != null && s.isActiveAndEnabled) active++;

        // A floor that just appeared brings scatters this controller has never
        // written to, so push the next apply past the deadband.
        if (active != _activeCount)
        {
            _activeCount  = active;
            _appliedCount = -1;
            _dirty        = true;
        }

        if (_scatters.Length == 0)
            Debug.LogWarning("[PinDensityController] No MeshSurfaceScatter instances found.");
    }

    void Update()
    {
        // Floors are enabled and disabled at runtime, so the scatter set is
        // re-checked on a slow timer rather than trusted from Start alone.
        if (rescanInterval > 0f && Time.unscaledTime >= _nextRescanTime)
        {
            _nextRescanTime = Time.unscaledTime + rescanInterval;
            Rescan();
        }

        // Agitation integrates over time: amplitude sets the SPEED the noise
        // field travels, not a position. Quiet passages barely advance it, so the
        // same samples keep winning selection and the pins sit still; loud ones
        // sweep it across the mesh and the visible configuration turns over.
        if (agitationEnabled && _agitation01 > 0f)
            _driftOffset += DriftAxis * (_agitation01 * driftSpeed * Time.deltaTime);

        if (_scatters == null) return;

        int target = Mathf.RoundToInt(Mathf.Lerp(minCount, maxCount, _ceiling01 * _envelope01));

        bool countSettled = _appliedCount >= 0 &&
                            Mathf.Abs(target - _appliedCount) < countDeadband;
        bool driftSettled = !agitationEnabled ||
                            (_driftOffset - _appliedDrift).sqrMagnitude < driftEpsilon * driftEpsilon;

        if (countSettled && driftSettled) { _dirty = false; return; }
        if (Time.unscaledTime < _nextApplyTime) return;

        foreach (var scatter in _scatters)
        {
            // Writing to a disabled floor would rebuild matrices that OnDisable
            // throws away. It stays in _scatters and is written when it returns.
            if (scatter == null || !scatter.isActiveAndEnabled) continue;

            if (agitationEnabled) scatter.Density.noiseOffset = _driftOffset;

            // SetCount() runs a full Scatter(), which also picks up the drift we
            // just wrote -- so count and configuration cost ONE rescatter, not two.
            scatter.SetCount(target);
        }

        _appliedCount  = target;
        _appliedDrift  = _driftOffset;
        _dirty         = false;
        _nextApplyTime = Time.unscaledTime +
                         (maxUpdatesPerSecond > 0f ? 1f / maxUpdatesPerSecond : 0f);
    }
}
