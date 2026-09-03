using UnityEngine;
using MidiFighter64.Samples;

namespace Pincushioned.SplitScreen
{
    /// <summary>
    /// Keeps the split-screen rig pointed at the floor you are actually on.
    ///
    /// Without this the sub-cameras stay aimed at the previous floor's point of
    /// interest. Once <see cref="FloorVisibilityController"/> disables that floor,
    /// every cell renders empty black — the cameras are staring into a room that is
    /// switched off. Symptom looks like the split screen is broken; the cause is
    /// stale aim.
    ///
    /// Deliberately a separate adapter rather than a reference inside the rig: the
    /// split-screen system stays generic and knows nothing about floors, and the
    /// dependency points one way only.
    /// </summary>
    [RequireComponent(typeof(PointOfInterestFinder))]
    public class SplitScreenFloorSync : MonoBehaviour
    {
        [Tooltip("Re-run the point-of-interest raycast when a floor move completes.")]
        [SerializeField] bool _reevaluatePoi = true;

        [Tooltip("Also re-place the cameras, giving each floor a fresh set of angles. " +
                 "Off keeps the same angles and simply re-aims them.")]
        [SerializeField] bool _rerollCameras = true;

        [Tooltip("Re-scan renderers before re-evaluating. Needed because the newly " +
                 "enabled floor's renderers did not exist in the cache taken while it " +
                 "was disabled.")]
        [SerializeField] bool _refreshRendererCache = true;

        PointOfInterestFinder _poi;
        SplitScreenCameraRig  _rig;

        void Awake()
        {
            _poi = GetComponent<PointOfInterestFinder>();
            _rig = GetComponent<SplitScreenCameraRig>();
        }

        void OnEnable()  => FloorCameraController.OnFloorChangeCompleted += HandleFloorChanged;
        void OnDisable() => FloorCameraController.OnFloorChangeCompleted -= HandleFloorChanged;

        void HandleFloorChanged(int floor)
        {
            if (_poi == null) return;

            if (_refreshRendererCache) _poi.RefreshRendererCache();
            if (_reevaluatePoi)        _poi.Evaluate();
            if (_rerollCameras && _rig != null) _rig.ResetCameraPositions();
        }
    }
}
