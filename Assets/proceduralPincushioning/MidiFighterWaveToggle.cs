using UnityEngine;
using MidiFighter64;

/// <summary>
/// MIDI Fighter 64 pad → turn the audio-driven pin wave on and off.
///
/// The two performance controls are deliberately independent:
///
///   MIDI Mix knob (MidiMixPinDensityDriver) → HOW MANY pins exist.
///   This pad                                → whether the music REARRANGES them.
///
/// They compose because a scatter's selection is a pure function of
/// (seed, count, noise offset). Changing the count with the offset frozen adds
/// or removes pins from a stable ordering rather than reshuffling, so raising
/// density never disturbs a composition you liked -- verified by capture: 400
/// pins, up to 900, back to 400 returns a pixel-identical frame.
///
/// OFF freezes rather than resets. PinDensityController stops integrating drift,
/// leaving noiseOffset exactly where it was, so the pins you are looking at stay
/// put. Turning it back on resumes from that same offset, so there is no jump.
///
/// PAD CONFLICT: MidiFighterInteriorSpawner claims every pad that is not
/// reserved. Whatever pad you bind here must also be added to that component's
/// Extra Reserved Pads, or pressing it will toggle an interior object too.
/// Row 0 / Col 0 means unassigned, matching the split-screen bindings.
///
/// Place this on the Pincushioned Rig object in the scene.
/// </summary>
public class MidiFighterWaveToggle : MonoBehaviour
{
    public enum PadBehaviour
    {
        /// <summary>Press flips the state and it stays. Good for long sections.</summary>
        Toggle,
        /// <summary>Wave runs only while the pad is held. Good for fills and stabs.</summary>
        Momentary,
    }

    [Header("Pad")]
    [Tooltip("MF64 row, 1-8, counting from the TOP. 0 = unassigned.")]
    [Range(0, 8)]
    public int row = 0;

    [Tooltip("MF64 column, 1-8, counting from the LEFT. 0 = unassigned.")]
    [Range(0, 8)]
    public int col = 0;

    [Tooltip("Toggle: press flips and holds. Momentary: wave runs only while held.")]
    public PadBehaviour behaviour = PadBehaviour.Toggle;

    [Header("State")]
    [Tooltip("Live wave state. Mirrors PinDensityController.agitationEnabled, and " +
             "is safe to flip by hand here while testing without MIDI hardware.")]
    public bool waveOn = true;

    PinDensityController _controller;

    void Awake() => _controller = GetComponent<PinDensityController>();

    // MidiGridRouter events are static — the -= is mandatory, not optional, or a
    // destroyed object keeps receiving MIDI.
    void OnEnable()
    {
        MidiGridRouter.OnGridButton += HandleButton;
        Apply(waveOn);
    }

    void OnDisable()
    {
        MidiGridRouter.OnGridButton -= HandleButton;
    }

    void HandleButton(GridButton btn, bool isNoteOn)
    {
        if (row <= 0 || col <= 0) return;              // unassigned
        if (btn.row != row || btn.col != col) return;

        if (behaviour == PadBehaviour.Momentary)
        {
            Apply(isNoteOn);
        }
        else if (isNoteOn)                              // ignore note-off on a toggle
        {
            Apply(!waveOn);
        }
    }

    /// <summary>Set the wave state. Public so a UI or another binding can call it.</summary>
    public void Apply(bool on)
    {
        waveOn = on;

        if (_controller == null) _controller = GetComponent<PinDensityController>();
        if (_controller == null)
        {
            Debug.LogWarning("[MidiFighterWaveToggle] No PinDensityController on this object.");
            return;
        }

        // Freezes in place: the controller simply stops advancing the drift, so
        // the current noiseOffset -- and therefore the current pin arrangement --
        // is held until the wave is switched back on.
        _controller.agitationEnabled = on;
    }

    // Keeps the Inspector checkbox live while tuning in Play mode.
    void OnValidate()
    {
        if (Application.isPlaying) Apply(waveOn);
    }
}
