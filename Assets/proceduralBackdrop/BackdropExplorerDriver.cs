using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The parameter-space search rig. Space finds a region, M refines inside it,
/// the arrow keys recover the one you lost, S keeps it.
///
/// Lives in the exploration scene, not the performance rig — during a set the
/// backdrop is played with the four pads, not searched.
///
/// Keys:
///   Space        full randomise within BackdropRanges
///   M            mutate current by Mutation Strength
///   Left/Right   walk the history ring
///   S            save current into the preset book under Save Name
///   F1-F9        recall preset 1-9
///
/// Recall is on the function keys, not the digits, because
/// KeyboardBackdropDriver owns 1-4 as the documented performance mapping and
/// both drivers sit on the same GameObject in the exploration scene — on the
/// digits, one press would recall a preset and then reroll on top of it.
/// </summary>
[RequireComponent(typeof(BackdropInstrument))]
public class BackdropExplorerDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Tooltip("The box the randomiser samples inside. Tighten it as the search converges.")]
    [SerializeField] private BackdropRanges ranges;

    [Tooltip("Where S writes. Presets survive Play mode because it is an asset, not scene state.")]
    [SerializeField] private BackdropPresetBook presetBook;

    [Tooltip("Seed for the search itself. Same seed replays the same sequence of randomisations.")]
    [SerializeField] private int searchSeed = 1;

    [Range(0f, 1f)]
    [Tooltip("How far M moves. 0.1 refines; above 0.5 discrete parameters start flipping too.")]
    [SerializeField] private float mutationStrength = 0.1f;

    [SerializeField] private int historyCapacity = 20;
    [SerializeField] private string saveName = "untitled";
    [SerializeField] private bool showOverlay = true;

    private System.Random rng;
    private HistoryRing<BackdropParameters> history;
    private string lastAction = "-";

    /// <summary>
    /// The instrument revision this driver has already accounted for. Anything
    /// higher means someone else wrote — a performance pad, the inspector — and
    /// that edit has to enter the history or walking back would restore a state
    /// that silently omits it.
    /// </summary>
    private int lastSeenRevision;

    private void Reset() => instrument = GetComponent<BackdropInstrument>();

    private void Awake()
    {
        if (instrument == null) instrument = GetComponent<BackdropInstrument>();
        rng = new System.Random(searchSeed);
        history = new HistoryRing<BackdropParameters>(historyCapacity);
        history.Push(instrument.Current);
        lastSeenRevision = instrument.Revision;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || instrument == null) return;

        // Reconcile before reading any key, so an edit made by another driver
        // last frame is already in the history if this frame walks it.
        if (instrument.Revision != lastSeenRevision)
        {
            history.Push(instrument.Current);
            lastSeenRevision = instrument.Revision;
            lastAction = "external edit";
        }

        if (kb.spaceKey.wasPressedThisFrame) DoRandomize();
        if (kb.mKey.wasPressedThisFrame)     DoMutate();
        if (kb.leftArrowKey.wasPressedThisFrame)  StepHistory(back: true);
        if (kb.rightArrowKey.wasPressedThisFrame) StepHistory(back: false);
        if (kb.sKey.wasPressedThisFrame)     DoSave();

        for (int i = 0; i < 9; i++)
            if (kb[Key.F1 + i].wasPressedThisFrame) DoRecall(i);
    }

    private void DoRandomize()
    {
        if (ranges == null) { lastAction = "no ranges asset"; return; }
        var p = BackdropRandomizer.Randomize(ranges, instrument.Current, rng);
        Commit(p, "randomize");
    }

    private void DoMutate()
    {
        if (ranges == null) { lastAction = "no ranges asset"; return; }
        var p = BackdropRandomizer.Mutate(ranges, instrument.Current, mutationStrength, rng);
        Commit(p, $"mutate {mutationStrength:0.00}");
    }

    private void StepHistory(bool back)
    {
        bool ok = back ? history.TryBack(out var p) : history.TryForward(out p);
        if (!ok) { lastAction = back ? "at oldest" : "at newest"; return; }

        // Applied without pushing, or walking history would itself write history.
        // The revision is still recorded, or the reconciler above would read this
        // driver's own ApplyAndRelayout as an external edit and push it anyway.
        instrument.ApplyAndRelayout(p);
        lastSeenRevision = instrument.Revision;
        lastAction = back ? "back" : "forward";
    }

    private void DoSave()
    {
        if (presetBook == null) { lastAction = "no preset book"; return; }

        presetBook.Save(saveName, instrument.Current);
        lastAction = $"saved '{saveName}'";

#if UNITY_EDITOR
        // Without this the preset is lost when Play mode exits, which is exactly
        // when it matters — the whole point is to keep what the search found.
        UnityEditor.EditorUtility.SetDirty(presetBook);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }

    private void DoRecall(int index)
    {
        if (presetBook == null || !presetBook.TryGet(index, out var p))
        {
            lastAction = $"no preset {index + 1}";
            return;
        }
        Commit(p, $"recall {index + 1}");
    }

    private void Commit(BackdropParameters p, string action)
    {
        instrument.ApplyAndRelayout(p);
        history.Push(instrument.Current);   // Current is the clamped version.
        lastSeenRevision = instrument.Revision;
        lastAction = action;
    }

    private void OnGUI()
    {
        if (!showOverlay || instrument == null) return;

        var p = instrument.Current;

        GUI.Box(new Rect(10, 270, 340, 176), GUIContent.none);
        GUILayout.BeginArea(new Rect(20, 278, 320, 164));
        GUILayout.Label($"Last: {lastAction}    History: {history.Count}");
        GUILayout.Label($"{p.spawnCount} on {p.domain}{(p.solidFill ? " solid" : "")}   {p.shading}");
        GUILayout.Label($"size {p.domainSize.x:0}x{p.domainSize.y:0}x{p.domainSize.z:0}   scale {p.scaleRange.x:0.00}-{p.scaleRange.y:0.00}");
        GUILayout.Label($"bias {p.scaleAxisBias.x:0.0},{p.scaleAxisBias.y:0.0},{p.scaleAxisBias.z:0.0}   spin +/-{p.spinRateRange.y:0}");
        GUILayout.Label($"wave amp {p.waveAmplitude:0.00} freq {p.waveFrequency:0.00} spread {p.wavePhaseSpread:0.00}");
        GUILayout.Label("Space randomize   M mutate   <- -> history");
        GUILayout.Label($"S save as '{saveName}'   F1-9 recall");
        GUILayout.EndArea();
    }
}