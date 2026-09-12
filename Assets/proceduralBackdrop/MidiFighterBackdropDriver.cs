using UnityEngine;
using MidiFighter64;

/// <summary>
/// Midi Fighter 64 pads -> BackdropInstrument. The only component in this
/// system that knows MIDI exists; the instrument itself is input-agnostic.
///
/// Pad bindings are serialized data rather than a hardcoded note function, so
/// the claimed pads are visible in the Inspector and auditable against
/// MidiFighterInteriorSpawner, which claims every pad it is not explicitly
/// told to skip.
///
/// All four pads default to row 0 / col 0, which means unbound. The main
/// scene's pad map is being reworked; assign these when that lands.
/// </summary>
public class MidiFighterBackdropDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Header("Performance pads (row 0 / col 0 = unbound)")]
    [SerializeField] private Vector2Int newLayoutPad   = Vector2Int.zero;
    [SerializeField] private Vector2Int swapShadingPad = Vector2Int.zero;
    [SerializeField] private Vector2Int flashPad       = Vector2Int.zero;
    [SerializeField] private Vector2Int nextMeshPad    = Vector2Int.zero;

    [Header("LED feedback")]
    [Tooltip("Optional. Leave empty to skip LED output entirely.")]
    [SerializeField] private MidiFighterOutput output;
    [SerializeField] private MidiFighterLEDColor idleColor    = MidiFighterLEDColor.DarkBlue;
    [SerializeField] private MidiFighterLEDColor pressedColor = MidiFighterLEDColor.BrightPink;

    private void Reset() => instrument = GetComponent<BackdropInstrument>();
    private void Awake() { if (instrument == null) instrument = GetComponent<BackdropInstrument>(); }

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

        if (Matches(b, newLayoutPad))   { instrument.RerollLayout();  SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, swapShadingPad)) { instrument.CycleShading();  SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, flashPad))       { instrument.Flash();         SetLed(b.row, b.col, pressedColor); return; }
        if (Matches(b, nextMeshPad))    { instrument.CycleMesh();     SetLed(b.row, b.col, pressedColor); return; }
    }

    private void HandleRelease(GridButton b)
    {
        if (Matches(b, newLayoutPad) || Matches(b, swapShadingPad) ||
            Matches(b, flashPad)     || Matches(b, nextMeshPad))
            SetLed(b.row, b.col, idleColor);
    }

    private static bool Matches(GridButton b, Vector2Int pad)
        => pad.x >= 1 && pad.y >= 1 && b.row == pad.x && b.col == pad.y;

    private void PaintIdleLeds()
    {
        if (output == null) return;
        PaintOne(newLayoutPad);
        PaintOne(swapShadingPad);
        PaintOne(flashPad);
        PaintOne(nextMeshPad);
    }

    private void PaintOne(Vector2Int pad)
    {
        if (pad.x < 1 || pad.y < 1) return;
        SetLed(pad.x, pad.y, idleColor);
    }

    private void SetLed(int row, int col, MidiFighterLEDColor color)
    {
        if (output == null) return;
        output.SetLED(MidiFighter64InputMap.ToNote(row, col), color);
    }
}