// LASP's Lasp.Runtime asmdef is Editor + desktop standalone only, so guard the
// whole file — Assembly-CSharp compiles for every target and would otherwise
// fail to find the Lasp namespace on an unsupported platform.
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX

using UnityEngine;
using Lasp;

/// <summary>
/// Live audio input → pin CONFIGURATION change, not pin count.
///
/// Writes <see cref="PinDensityController.Agitation01"/>, which the controller
/// integrates into the scatter noise field's drift velocity. The MIDI knob still
/// owns how many pins exist; this owns how much that set churns.
///
/// Why not drive count: mapping amplitude to density makes loud passages add
/// mass, which reads as the model swelling rather than reacting. Driving the
/// noise field instead keeps the composition's weight fixed and lets the music
/// reorganise WHICH of the baked samples are selected — the pins stay still
/// through quiet passages and migrate across the surface on loud ones.
///
/// REQUIRES each target scatter's Density Mode to be Noise. In Uniform mode
/// noiseOffset is never read, so this does nothing at all. Switching to Noise
/// also makes the pins cluster rather than spread evenly — that is a real
/// aesthetic change, not a side effect to ignore.
///
/// Can run alongside <see cref="AudioPinDensityDriver"/>, but usually should not:
/// that one drives count from the same signal, which is the behaviour this
/// replaces. Disable it, or leave its envelope pinned at 1.
///
/// Place on the Pincushioned Rig object in the scene.
/// </summary>
public class AudioScatterAgitationDriver : MonoBehaviour
{
    [Header("Input device")]
    [Tooltip("Leave empty to use the system default input device. Device IDs are " +
             "machine-specific and must be reconfigured on another machine — get one " +
             "from an AudioLevelTracker's Select button in the Inspector, or via " +
             "Lasp.AudioSystem.InputDevices at runtime.\n\n" +
             "Default here is the Yeti Nano on this rig.")]
    [SerializeField] string _deviceID = "{0.0.1.00000000}.{2b2da3c9-87f2-4af9-8a70-9cb998e37118}";

    [Header("Signal")]
    [Tooltip("Which part of the spectrum drives agitation. LowPass = kick/bass " +
             "(configuration turns over on the beat), BandPass = mids, " +
             "HighPass = hats/air (fine constant shimmer), Bypass = full range.")]
    [SerializeField] FilterType _band = FilterType.LowPass;

    [Tooltip("Auto-track the noise floor so the signal stays usable as room level " +
             "changes. Turn off to set gain by hand.")]
    [SerializeField] bool _autoGain = true;

    [Tooltip("Manual gain in dB. Only used when Auto Gain is off.")]
    [SerializeField] float _gain = 6f;

    [Tooltip("Decibel range mapped onto 0–1. Smaller = more twitchy.")]
    [SerializeField] float _dynamicRange = 24f;

    [Header("Shaping")]
    [Tooltip("Below this level the field is treated as completely still. The whole " +
             "point of this driver is that quiet passages hold their composition, " +
             "so a small gate here is what buys you genuine stillness rather than " +
             "a slow constant crawl.")]
    [Range(0f, 0.5f)]
    [SerializeField] float _gate = 0.08f;

    [Tooltip("Power curve on the gated signal. >1 makes only loud events move the " +
             "pins (punchier, more selective); <1 makes everything move a little.")]
    [Range(0.25f, 4f)]
    [SerializeField] float _contrast = 2f;

    [Tooltip("Smooth the signal's fall so agitation eases out after a hit instead " +
             "of stopping dead. This is what turns a transient into a gesture.")]
    [SerializeField] bool _smoothFall = true;

    [Tooltip("How quickly agitation falls once the sound stops.")]
    [SerializeField] float _fallSpeed = 4f;

    AudioLevelTracker _tracker;

    /// <summary>Current agitation, 0–1, after gate and contrast. For debug/UI.</summary>
    public float Agitation { get; private set; }

    void Awake()
    {
        _tracker = gameObject.AddComponent<AudioLevelTracker>();

        if (string.IsNullOrEmpty(_deviceID))
            _tracker.useDefaultDevice = true;
        else
            _tracker.deviceID = _deviceID;

        _tracker.filterType   = _band;
        _tracker.autoGain     = _autoGain;
        _tracker.gain         = _gain;
        _tracker.dynamicRange = _dynamicRange;
        _tracker.smoothFall   = _smoothFall;
        _tracker.fallSpeed    = _fallSpeed;
    }

    void Update()
    {
        if (_tracker == null) return;

        float level = _tracker.normalizedLevel;

        // Gate first, then rescale, so the surviving range still reaches 1.
        // Gating without rescaling would quietly cap agitation below full.
        if (level <= _gate)
        {
            level = 0f;
        }
        else
        {
            level = (level - _gate) / Mathf.Max(1e-4f, 1f - _gate);
            if (!Mathf.Approximately(_contrast, 1f))
                level = Mathf.Pow(level, _contrast);
        }

        Agitation = level;
        PinDensityController.Agitation01 = Agitation;
    }

    // Freeze the configuration if audio is switched off mid-set, rather than
    // leaving the last agitation value latched and the field drifting forever.
    void OnDisable()
    {
        Agitation = 0f;
        PinDensityController.Agitation01 = 0f;
    }
}

#endif
