using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keys 1-4 -> the four backdrop performance functions, plus D to walk the
/// domain shape. How the backdrop is played without the MF64 attached.
///
/// This project is Input System package only (activeInputHandler = 1), so the
/// legacy UnityEngine.Input class throws at runtime. Everything goes through
/// Keyboard.current.
///
/// Keys:
///   1  new layout      2  swap shading
///   3  flash           4  next mesh
///   D  next domain (plane / hemisphere / cube)
/// </summary>
[RequireComponent(typeof(BackdropInstrument))]
public class KeyboardBackdropDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Tooltip("Draw a small on-screen legend and live state readout.")]
    [SerializeField] private bool showOverlay = true;

    /// <summary>
    /// Derived, not typed as 3: a fourth domain shape added to the enum would
    /// otherwise never be reachable from the D key and nothing would say so.
    /// Cached because Enum.GetValues allocates and this runs on every press.
    /// </summary>
    private static readonly int DomainCount =
        System.Enum.GetValues(typeof(BackdropDomain)).Length;

    private void Reset() => instrument = GetComponent<BackdropInstrument>();
    private void Awake() { if (instrument == null) instrument = GetComponent<BackdropInstrument>(); }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || instrument == null) return;

        if (kb.digit1Key.wasPressedThisFrame) instrument.RerollLayout();
        if (kb.digit2Key.wasPressedThisFrame) instrument.CycleShading();
        if (kb.digit3Key.wasPressedThisFrame) instrument.Flash();
        if (kb.digit4Key.wasPressedThisFrame) instrument.CycleMesh();

        if (kb.dKey.wasPressedThisFrame) NextDomain();
    }

    private void NextDomain()
    {
        var p = instrument.Current;
        p.domain = (BackdropDomain)(((int)p.domain + 1) % DomainCount);
        instrument.ApplyAndRelayout(p);
    }

    private void OnGUI()
    {
        if (!showOverlay || instrument == null) return;

        var p = instrument.Current;
        int meshes = instrument.Library != null ? instrument.Library.Count : 0;

        GUI.Box(new Rect(10, 150, 320, 112), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 158, 300, 100));
        GUILayout.Label($"Backdrop: {p.spawnCount} on {p.domain}   seed {p.layoutSeed}");
        GUILayout.Label($"Shading: {p.shading}   Mesh {instrument.MeshIndex + 1}/{meshes}");
        GUILayout.Label("1 layout   2 shading   3 flash");
        GUILayout.Label("4 mesh     D domain");
        GUILayout.EndArea();
    }
}