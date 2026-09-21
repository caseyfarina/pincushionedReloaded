using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// How the cable instrument is played without hardware. A dumb driver: it reads
/// keys and calls the instrument, and holds no state of its own.
///
/// X fires, R rerolls the seed, C clears.
/// </summary>
public class KeyboardCableDriver : MonoBehaviour
{
    public CableInstrument instrument;

    private void Reset() => instrument = GetComponent<CableInstrument>();

    private void Update()
    {
        if (instrument == null) return;

        if (Pressed_X()) instrument.Fire();
        if (Pressed_R()) instrument.RerollSeed();
        if (Pressed_C()) instrument.Clear();
    }

#if ENABLE_INPUT_SYSTEM
    private static bool Pressed_X() => Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame;
    private static bool Pressed_R() => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
    private static bool Pressed_C() => Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
    private static bool Pressed_X() => Input.GetKeyDown(KeyCode.X);
    private static bool Pressed_R() => Input.GetKeyDown(KeyCode.R);
    private static bool Pressed_C() => Input.GetKeyDown(KeyCode.C);
#endif
}
