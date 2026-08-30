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

        [Header("MIDI Mix knob → subdivisions")]
        [Tooltip("Off leaves subdivisions on whatever the Inspector says.")]
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

            _rig.SetSubdivisions(Mathf.RoundToInt(Mathf.Lerp(
                QuadtreeLayout.MinSubdivisions, QuadtreeLayout.MaxSubdivisions, value)));
        }

        /// <summary>Pads this component listens to, for conflict checks.</summary>
        public void GetBoundPads(System.Collections.Generic.List<Vector2Int> into)
        {
            if (into == null) return;
            foreach (var b in new[] { _rerollAll, _rerollLayout, _rerollCameras, _reevaluatePoi })
                if (b.Enabled) into.Add(new Vector2Int(b.row, b.col));
        }
    }
}
