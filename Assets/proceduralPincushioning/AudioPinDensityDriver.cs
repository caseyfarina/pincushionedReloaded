// LASP's Lasp.Runtime asmdef is Editor + desktop standalone only, so guard the
// whole file — Assembly-CSharp compiles for every target and would otherwise
// fail to find the Lasp namespace on an unsupported platform.
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX

using UnityEngine;
using Lasp;

/// <summary>
/// Live audio input → pin density envelope.
///
/// Drives <see cref="PinDensityController.Envelope01"/>, which multiplies with the
/// MIDI Mix knob's ceiling. The knob decides how intense the pins get; the music
/// decides how they move within that.
///
/// Uses LASP (jp.keijiro.lasp) for low-latency capture from a physical input
/// device. Attaches its own AudioLevelTracker and configures it in Awake — the
/// tracker resolves its device stream lazily on first Update, so configuring it
/// immediately after AddComponent is safe.
///
/// Place this on the Pincushioned Rig object in the scene. Device, band, and
/// envelope shaping are all authored in the Inspector.
/// </summary>
public class AudioPinDensityDriver : MonoBehaviour
{
    [Header("Input device")]
    [Tooltip("Leave empty to use the system default input device. Device IDs are " +
             "machine-specific and must be reconfigured on another machine — get one " +
             "from an AudioLevelTracker's Select button in the Inspector, or via " +
             "Lasp.AudioSystem.InputDevices at runtime.\n\n" +
             "Default here is the Yeti Nano on this rig.")]
    [SerializeField] string _deviceID = "{0.0.1.00000000}.{2b2da3c9-87f2-4af9-8a70-9cb998e37118}";

    [Header("Envelope")]
    [Tooltip("Which part of the spectrum drives density. LowPass = kick/bass, " +
             "BandPass = mids, HighPass = hats/air, Bypass = full range.")]
    [SerializeField] FilterType _band = FilterType.LowPass;

    [Tooltip("Auto-track the noise floor so the envelope stays usable as room " +
             "level changes. Turn off to set gain by hand.")]
    [SerializeField] bool _autoGain = true;

    [Tooltip("Manual gain in dB. Only used when Auto Gain is off.")]
    [SerializeField] float _gain = 6f;

    [Tooltip("Decibel range mapped onto 0–1. Smaller = more twitchy.")]
    [SerializeField] float _dynamicRange = 24f;

    [Tooltip("Smooth the envelope's fall so pins ease out instead of snapping.")]
    [SerializeField] bool _smoothFall = true;

    [Tooltip("How fast the envelope falls when Smooth Fall is on.")]
    [SerializeField] float _fallSpeed = 2f;

    [Header("Response")]
    [Tooltip("Envelope value at silence. Above 0 keeps some pins alive through " +
             "quiet passages instead of emptying the scene.")]
    [Range(0f, 1f)]
    [SerializeField] float _floor = 0.15f;

    [Tooltip("Shapes the response curve. 1 = linear, >1 = punchier (peaks stand " +
             "out), <1 = flatter (quiet detail lifts).")]
    [Range(0.25f, 4f)]
    [SerializeField] float _contrast = 1f;

    AudioLevelTracker _tracker;

    /// <summary>Current envelope, 0–1, after floor and contrast. For debug/UI.</summary>
    public float Envelope { get; private set; } = 1f;

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
        if (!Mathf.Approximately(_contrast, 1f))
            level = Mathf.Pow(level, _contrast);

        Envelope = Mathf.Lerp(_floor, 1f, level);
        PinDensityController.Envelope01 = Envelope;
    }

    // Hand density back to the knob if audio is switched off mid-set, rather
    // than leaving the last envelope value latched in.
    void OnDisable()
    {
        Envelope = 1f;
        PinDensityController.Envelope01 = 1f;
    }
}

#endif
