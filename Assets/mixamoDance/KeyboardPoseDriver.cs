using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Number keys 1-9 -> PoseInstrument.Trigger, plus keys for the global toggles.
/// This is how the prototype gets played without the MIDI hardware attached,
/// which matters because nothing in this project has run against the MF64 yet.
///
/// This project is Input System package only (activeInputHandler = 1), so the
/// legacy UnityEngine.Input class throws at runtime. Everything here goes
/// through Keyboard.current.
///
/// Keys:
///   1-9      trigger pose 0-8
///   Space    toggle Play / Freeze
///   F        FreezeAllNow
///   R        ResumeAllNow
///   Up/Down  playback rate +/- 0.25 (crosses zero into reverse)
///   0        reset playback rate to 1
/// </summary>
[RequireComponent(typeof(PoseInstrument))]
public class KeyboardPoseDriver : MonoBehaviour
{
    [SerializeField] private PoseInstrument instrument;

    [Tooltip("Draw a small on-screen legend and live state readout.")]
    [SerializeField] private bool showOverlay = true;

    [SerializeField, Min(0.01f)] private float rateStep = 0.25f;
    [SerializeField] private float rateMin = -3f;
    [SerializeField] private float rateMax =  3f;

    private float rate = 1f;

    private void Reset()      => instrument = GetComponent<PoseInstrument>();
    private void Awake()      { if (instrument == null) instrument = GetComponent<PoseInstrument>(); }

    private static readonly Key[] NumberKeys = {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || instrument == null) return;

        for (int i = 0; i < NumberKeys.Length; i++)
            if (kb[NumberKeys[i]].wasPressedThisFrame) instrument.Trigger(i);

        if (kb.spaceKey.wasPressedThisFrame) instrument.TogglePlayMode();
        if (kb.fKey.wasPressedThisFrame)     instrument.FreezeAllNow();
        if (kb.rKey.wasPressedThisFrame)     instrument.ResumeAllNow();

        if (kb.upArrowKey.wasPressedThisFrame)   SetRate(rate + rateStep);
        if (kb.downArrowKey.wasPressedThisFrame) SetRate(rate - rateStep);
        if (kb.digit0Key.wasPressedThisFrame)    SetRate(1f);
    }

    private void SetRate(float r)
    {
        rate = Mathf.Clamp(r, rateMin, rateMax);
        instrument.SetPlaybackRate(rate);
    }

    private void OnGUI()
    {
        if (!showOverlay || instrument == null) return;

        GUI.Box(new Rect(10, 10, 320, 128), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 18, 300, 116));
        GUILayout.Label($"Poses: {instrument.PoseCount}   Layers: {instrument.ActiveLayerCount}");
        GUILayout.Label($"Mode: {(instrument.PlayModeOn ? "PLAY" : "FREEZE")}   Rate: {instrument.PlaybackRateNow:0.00}");
        GUILayout.Label("1-9 trigger   Space play/freeze");
        GUILayout.Label("F freeze all   R resume all");
        GUILayout.Label("Up/Down rate   0 rate = 1");
        GUILayout.EndArea();
    }
}
