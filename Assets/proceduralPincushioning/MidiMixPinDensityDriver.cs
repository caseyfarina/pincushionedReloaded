using UnityEngine;
using MidiFighter64;

/// <summary>
/// MIDI Mix Row 2, Ch 1 knob → pin density ceiling.
///
/// The knob no longer writes the scatter count directly. It sets
/// <see cref="PinDensityController.Ceiling01"/>, which the controller multiplies
/// by the live audio envelope before applying a single rate-limited count to
/// every MeshSurfaceScatter.
///
/// With no AudioPinDensityDriver in the scene the envelope stays at 1, so the
/// knob behaves exactly as it did before: full sweep over the controller's
/// minCount–maxCount range. The count range itself now lives on
/// PinDensityController, so both drivers agree on it.
///
/// Place this on the Pincushioned Rig object in the scene.
/// </summary>
public class MidiMixPinDensityDriver : MonoBehaviour
{
    [Tooltip("MIDI Mix channel to listen on (1-8).")]
    [Range(1, 8)]
    public int channel = 1;

    [Tooltip("MIDI Mix knob row to listen on (1-3).")]
    [Range(1, 3)]
    public int row = 2;

    // MidiMixRouter events are static — the -= is mandatory, not optional.
    private void OnEnable()  => MidiMixRouter.OnKnob += HandleKnob;
    private void OnDisable() => MidiMixRouter.OnKnob -= HandleKnob;

    private void HandleKnob(int ch, int r, float value)
    {
        if (ch != channel || r != row) return;
        PinDensityController.Ceiling01 = value;
    }
}
