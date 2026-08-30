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

    // ── inputs ───────────────────────────────────────────────────────────

    static float _ceiling01  = 1f;
    static float _envelope01 = 1f;
    static bool  _dirty      = true;

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

    // ── lifecycle ────────────────────────────────────────────────────────

    MeshSurfaceScatter[] _scatters;
    int   _appliedCount = -1;
    float _nextApplyTime;

    void OnEnable()
    {
        // Statics survive "Enter Play Mode without domain reload", so reset them
        // here rather than relying on field initializers.
        _ceiling01  = 1f;
        _envelope01 = 1f;
        _dirty      = true;
    }

    void Start() => Rescan();

    /// <summary>Re-find the scatter instances. Call after spawning new ones.</summary>
    public void Rescan()
    {
        _scatters = Object.FindObjectsByType<MeshSurfaceScatter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (_scatters.Length == 0)
            Debug.LogWarning("[PinDensityController] No MeshSurfaceScatter instances found.");
    }

    void Update()
    {
        if (!_dirty || _scatters == null) return;
        if (Time.unscaledTime < _nextApplyTime) return;

        int target = Mathf.RoundToInt(Mathf.Lerp(minCount, maxCount, _ceiling01 * _envelope01));

        // First apply always goes through; after that, honour the deadband.
        if (_appliedCount >= 0 && Mathf.Abs(target - _appliedCount) < countDeadband)
        {
            _dirty = false;
            return;
        }

        foreach (var scatter in _scatters)
            if (scatter != null) scatter.SetCount(target);

        _appliedCount  = target;
        _dirty         = false;
        _nextApplyTime = Time.unscaledTime +
                         (maxUpdatesPerSecond > 0f ? 1f / maxUpdatesPerSecond : 0f);
    }
}
