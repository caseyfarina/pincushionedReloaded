using System.Collections.Generic;
using UnityEngine;
using MidiFighter64;

/// <summary>
/// Midi Fighter 64 pads -> PoseInstrument. The only component in this system
/// that knows MIDI exists; PoseInstrument itself is input-agnostic.
///
/// Pad bindings are serialized data, not a hardcoded note-to-index function,
/// so the reserved region is visible in the Inspector and auditable against
/// MidiFighterInteriorSpawner, which claims every pad it is not explicitly
/// told to skip.
///
/// PROTOTYPE SCOPE: the main scene is untouched. When this moves over, the
/// same row/col pairs bound here must be added to MidiFighterInteriorSpawner
/// Extra Reserved Pads on the Pincushioned Rig prefab, or every pose pad will
/// also toggle an interior object.
/// </summary>
public class MidiFighterPoseDriver : MonoBehaviour
{
    [System.Serializable]
    public struct PadBinding
    {
        [Tooltip("MF64 row, 1-8, 1 = top.")]   [Range(1, 8)] public int row;
        [Tooltip("MF64 column, 1-8, 1 = left.")] [Range(1, 8)] public int col;
        [Tooltip("Index into the instrument PoseBank.")] public int poseIndex;
    }

    [SerializeField] private PoseInstrument instrument;

    [Header("Pose pads")]
    [Tooltip("Prototype default: row 1, columns 1-7. Column 8 stays with floor navigation.")]
    [SerializeField]
    private List<PadBinding> padBindings = new List<PadBinding>
    {
        new PadBinding { row = 1, col = 1, poseIndex = 0 },
        new PadBinding { row = 1, col = 2, poseIndex = 1 },
        new PadBinding { row = 1, col = 3, poseIndex = 2 },
        new PadBinding { row = 1, col = 4, poseIndex = 3 },
    };

    [Header("Transport pads (row 0 / col 0 = unbound)")]
    [SerializeField] private Vector2Int playModeTogglePad = Vector2Int.zero;
    [SerializeField] private Vector2Int freezeAllPad      = Vector2Int.zero;
    [SerializeField] private Vector2Int resumeAllPad      = Vector2Int.zero;

    [Header("LED feedback")]
    [Tooltip("Optional. Leave empty to skip LED output entirely.")]
    [SerializeField] private MidiFighterOutput output;
    [SerializeField] private MidiFighterLEDColor idleColor    = MidiFighterLEDColor.DarkBlue;
    [SerializeField] private MidiFighterLEDColor pressedColor = MidiFighterLEDColor.BrightPink;
    [SerializeField] private MidiFighterLEDColor playModeColor = MidiFighterLEDColor.White;

    private void Reset() => instrument = GetComponent<PoseInstrument>();

    // MidiFighterButtonRouter events are static - the -= is mandatory, not optional.
    private void OnEnable()
    {
        MidiFighterButtonRouter.OnButtonPress   += HandlePress;
        MidiFighterButtonRouter.OnButtonRelease += HandleRelease;
        PaintIdleLeds();
    }

    private void OnDisable()
    {
        MidiFighterButtonRouter.OnButtonPress   -= HandlePress;
        MidiFighterButtonRouter.OnButtonRelease -= HandleRelease;
    }

    private void HandlePress(GridButton b, float velocity)
    {
        if (instrument == null) return;

        if (Matches(b, playModeTogglePad)) { instrument.TogglePlayMode(); PaintIdleLeds(); return; }
        if (Matches(b, freezeAllPad))      { instrument.FreezeAllNow();  return; }
        if (Matches(b, resumeAllPad))      { instrument.ResumeAllNow();  return; }

        for (int i = 0; i < padBindings.Count; i++)
        {
            var p = padBindings[i];
            if (p.row != b.row || p.col != b.col) continue;
            instrument.Trigger(p.poseIndex);
            SetLed(b.row, b.col, pressedColor);
            return;
        }
    }

    private void HandleRelease(GridButton b)
    {
        for (int i = 0; i < padBindings.Count; i++)
            if (padBindings[i].row == b.row && padBindings[i].col == b.col)
            {
                SetLed(b.row, b.col, idleColor);
                return;
            }
    }

    private static bool Matches(GridButton b, Vector2Int pad)
        => pad.x >= 1 && pad.y >= 1 && b.row == pad.x && b.col == pad.y;

    private void PaintIdleLeds()
    {
        if (output == null) return;
        for (int i = 0; i < padBindings.Count; i++)
            SetLed(padBindings[i].row, padBindings[i].col, idleColor);

        if (playModeTogglePad.x >= 1 && playModeTogglePad.y >= 1)
            SetLed(playModeTogglePad.x, playModeTogglePad.y,
                   instrument != null && instrument.PlayModeOn ? playModeColor : idleColor);
    }

    private void SetLed(int row, int col, MidiFighterLEDColor color)
    {
        if (output == null) return;
        output.SetLED(MidiFighter64InputMap.ToNote(row, col), color);
    }
}
