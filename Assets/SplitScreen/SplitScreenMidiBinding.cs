using UnityEngine;
using MidiFighter64;

namespace Pincushioned.SplitScreen
{
    /// <summary>
    /// Binds the split-screen rig to the Midi Fighter 64 and the MIDI Mix.
    ///
    /// Three separate reroll actions, so a pad can trigger exactly as much change
    /// as you want mid-set:
    ///
    ///   Reroll All        — new cell arrangement AND new camera angles
    ///   Reroll Layout     — new cell arrangement, same angles
    ///   Reroll Cameras    — new camera angles, same cell arrangement
    ///
    /// Point all three at the same pad if you want one button that changes
    /// everything; leave the ones you don't want at row 0 to disable them.
    ///
    /// <b>Pad conflict:</b> MidiFighterInteriorSpawner responds to every pad that
    /// isn't reserved, so a pad bound here will also toggle an interior object
    /// unless you add it to that component's Extra Reserved Pads list. The
    /// Inspector warns when that is the case.
    /// </summary>
    public class SplitScreenMidiBinding : MonoBehaviour
    {
        [System.Serializable]
        public struct PadBinding
        {
            [Tooltip("MF64 row, 1 = top. 0 disables this binding.")]
            [Range(0, 8)] public int row;

            [Tooltip("MF64 column, 1 = left.")]
            [Range(0, 8)] public int col;

            public bool Enabled => row >= 1 && row <= 8 && col >= 1 && col <= 8;
            public bool Matches(GridButton b) => Enabled && b.row == row && b.col == col;
        }

        [Header("Midi Fighter 64 pads")]
        [Tooltip("Rerolls the cell layout AND the camera angles.")]
        [SerializeField] PadBinding _rerollAll = new PadBinding { row = 8, col = 3 };

        [Tooltip("Rerolls the cell layout only.")]
        [SerializeField] PadBinding _rerollLayout = new PadBinding { row = 0, col = 0 };

        [Tooltip("Rerolls the camera angles only.")]
        [SerializeField] PadBinding _rerollCameras = new PadBinding { row = 0, col = 0 };

        [Tooltip("Re-runs the point-of-interest raycast, choosing a new subject.")]
        [SerializeField] PadBinding _reevaluatePoi = new PadBinding { row = 0, col = 0 };

        [Header("Split-screen show/hide + reconfigure")]
        [Tooltip("Toggles the split screen on and off. The arrangement is remembered, " +
                 "so switching back on returns the identical layout and angles. " +
                 "This pad's LED shows the current state.")]
        [SerializeField] PadBinding _toggleSplitScreen = new PadBinding { row = 8, col = 4 };

        [Tooltip("Reconfigure with a random cell count in the WIDE range - busier.")]
        [SerializeField] PadBinding _randomizeWide = new PadBinding { row = 8, col = 5 };

        [Tooltip("Reconfigure with a random cell count in the TIGHT range - calmer.")]
        [SerializeField] PadBinding _randomizeTight = new PadBinding { row = 8, col = 6 };

        [Tooltip("Inclusive cell range for the WIDE reconfigure pad.")]
        [SerializeField] Vector2Int _wideRange = new Vector2Int(4, 9);

        [Tooltip("Inclusive cell range for the TIGHT reconfigure pad.")]
        [SerializeField] Vector2Int _tightRange = new Vector2Int(2, 5);

        [Header("LED feedback")]
        [Tooltip("Light the toggle pad to show the split screen's state on the hardware.")]
        [SerializeField] bool _driveToggleLed = true;

        [Tooltip("Colour when the split screen is ON.")]
        [SerializeField] MidiFighterLEDColor _ledOn = MidiFighterLEDColor.BrightBlue;

        [Tooltip("Colour when the split screen is OFF. DarkGrey reads as 'armed but " +
                 "idle'; Off leaves the pad dark.")]
        [SerializeField] MidiFighterLEDColor _ledOff = MidiFighterLEDColor.DarkGrey;

        [Tooltip("Briefly flash the reconfigure pads when they fire, as confirmation.")]
        [SerializeField] bool _flashReconfigureLeds = true;

        [SerializeField] MidiFighterLEDColor _ledFlash = MidiFighterLEDColor.BrightPink;

        [Tooltip("Seconds the reconfigure flash stays lit.")]
        [SerializeField] float _flashSeconds = 0.12f;

        [Header("MIDI Mix knob → cell count")]
        [Tooltip("Off leaves the cell count on whatever the Inspector says.")]
        [SerializeField] bool _knobDrivesSubdivisions = false;

        [Range(1, 8)] [SerializeField] int _subdivisionChannel = 2;
        [Range(1, 3)] [SerializeField] int _subdivisionRow     = 2;

        SplitScreenCameraRig  _rig;
        PointOfInterestFinder _poi;

        void Awake()
        {
            _rig = GetComponent<SplitScreenCameraRig>();
            _poi = GetComponent<PointOfInterestFinder>();

            if (_rig == null)
                Debug.LogWarning("[SplitScreenMidiBinding] No SplitScreenCameraRig on this object.");
        }

        // Router events are static: every += needs its matching -=, or a destroyed
        // object keeps receiving MIDI.
        void OnEnable()
        {
            MidiGridRouter.OnGridButton += HandlePad;
            if (_knobDrivesSubdivisions) MidiMixRouter.OnKnob += HandleKnob;
        }

        void OnDisable()
        {
            MidiGridRouter.OnGridButton -= HandlePad;
            MidiMixRouter.OnKnob        -= HandleKnob;
        }

        void HandlePad(GridButton btn, bool isDown)
        {
            if (!isDown || _rig == null) return;   // act on press, ignore release

            if (_rerollAll.Matches(btn))     _rig.RerollAll();
            if (_rerollLayout.Matches(btn))  _rig.RerollLayout();
            if (_rerollCameras.Matches(btn)) _rig.ResetCameraPositions();

            if (_toggleSplitScreen.Matches(btn))
            {
                _rig.ToggleActive();
                RefreshToggleLed();
            }

            if (_randomizeWide.Matches(btn))
            {
                _rig.RandomizeCells(_wideRange.x, _wideRange.y);
                FlashPad(_randomizeWide);
            }

            if (_randomizeTight.Matches(btn))
            {
                _rig.RandomizeCells(_tightRange.x, _tightRange.y);
                FlashPad(_randomizeTight);
            }

            if (_reevaluatePoi.Matches(btn) && _poi != null)
            {
                _poi.RefreshRendererCache();
                _poi.Evaluate();
            }
        }

        void HandleKnob(int channel, int row, float value)
        {
            if (_rig == null) return;
            if (channel != _subdivisionChannel || row != _subdivisionRow) return;

            _rig.SetCellCount(Mathf.RoundToInt(Mathf.Lerp(
                MosaicLayout.MinCells, MosaicLayout.MaxCells, value)));
        }

        // ── LED feedback ─────────────────────────────────────────────────────

        System.Collections.IEnumerator Start()
        {
            // MidiFighterOutput.ClearOnStart blanks all 64 pads in its own Start,
            // so setting the LED here would be wiped. Wait for the output to exist
            // and for that clear to have happened, then assert our state.
            for (int i = 0; i < 120 && MidiFighterOutput.Instance == null; i++)
                yield return null;
            yield return null;
            RefreshToggleLed();
        }

        /// <summary>Drive the toggle pad's LED to match the rig's current state.</summary>
        public void RefreshToggleLed()
        {
            if (!_driveToggleLed || !_toggleSplitScreen.Enabled) return;
            if (_rig == null || MidiFighterOutput.Instance == null) return;

            SetPadColor(_toggleSplitScreen, _rig.IsActive ? _ledOn : _ledOff);
        }

        void FlashPad(PadBinding pad)
        {
            if (!_flashReconfigureLeds || !pad.Enabled) return;
            if (MidiFighterOutput.Instance == null) return;
            StartCoroutine(FlashRoutine(pad));
        }

        System.Collections.IEnumerator FlashRoutine(PadBinding pad)
        {
            SetPadColor(pad, _ledFlash);
            yield return new WaitForSeconds(Mathf.Max(0.02f, _flashSeconds));
            SetPadColor(pad, MidiFighterLEDColor.Off);
        }

        static void SetPadColor(PadBinding pad, MidiFighterLEDColor color)
        {
            // Never compute MF64 notes by hand — the grid is two 4-column halves
            // and the naive 36 + row*8 + col is wrong for the right-hand half.
            int note = MidiFighter64InputMap.ToNote(pad.row, pad.col);
            if (note >= 0) MidiFighterOutput.Instance.SetLED(note, color);
        }

        /// <summary>Pads this component listens to, for conflict checks.</summary>
        public void GetBoundPads(System.Collections.Generic.List<Vector2Int> into)
        {
            if (into == null) return;
            foreach (var b in new[] { _rerollAll, _rerollLayout, _rerollCameras,
                                      _reevaluatePoi, _toggleSplitScreen,
                                      _randomizeWide, _randomizeTight })
                if (b.Enabled) into.Add(new Vector2Int(b.row, b.col));
        }
    }
}
