using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Row 8, Col 1 — hold to cut to a close-up camera inside the current room.
    ///               Release to cut back to the floor camera.
    /// Row 8, Col 2 — press to reposition the close-up camera near a random
    ///               object in the current room, found via Physics.OverlapBox
    ///               on the floor's FloorVolume trigger collider.
    ///               DOF focus distance is updated to match the target.
    /// </summary>
    public class CloseUpCameraController : MonoBehaviour
    {
        [Header("Shot framing")]
        [Tooltip("Closest the camera sits to the target, added to the target's radius.")]
        [SerializeField] float _distMin = 1.5f;

        [Tooltip("Furthest the camera sits from the target, added to the target's radius.")]
        [SerializeField] float _distMax = 4.5f;

        [Tooltip("Vertical offset spread, as a fraction of the target's radius.")]
        [Range(0f, 1f)]
        [SerializeField] float _heightJitter = 0.4f;

        [Tooltip("Smallest radius used for tiny objects, so the camera never sits inside them.")]
        [SerializeField] float _minRadius = 0.3f;

        [Header("Repeatability")]
        [Tooltip("Seed for target choice and orbit placement. Same seed = same shots " +
                 "in the same order, so a run you like can be replayed.")]
        [SerializeField] int _seed = 2024;

        [Header("Wiring")]
        [Tooltip("Close-up virtual camera. Leave empty to create one at runtime from " +
                 "Main Camera (not authorable — assign a scene camera to tune it).")]
        [SerializeField] CinemachineCamera _closeUpVcam;

        [Tooltip("Priority for the close-up vcam. Must beat the floor vcam's priority.")]
        [SerializeField] int _priority = 20;

        System.Random _rng;
        FloorVolume[]     _floorVolumes;

        // Active-session state
        Volume       _activeDOFVolume;
        DepthOfField _activeDOF;
        FloorVolume  _activeFloorVolume;

        void Awake()
        {
            _rng = new System.Random(_seed);

            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[CloseUpCameraController] No Main Camera found.");
                return;
            }

            if (_closeUpVcam == null)
            {
                var go = new GameObject("CloseUpVcam");
                go.transform.SetPositionAndRotation(
                    mainCam.transform.position,
                    mainCam.transform.rotation);
                _closeUpVcam = go.AddComponent<CinemachineCamera>();
            }

            _closeUpVcam.Priority = _priority;   // must beat FloorVcam when active
            _closeUpVcam.gameObject.SetActive(false);
        }

        void Start()
        {
            // Cache after all scene objects are initialised
            _floorVolumes = Object.FindObjectsByType<FloorVolume>(FindObjectsSortMode.None);
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.row != 8 || _closeUpVcam == null) return;

            if (btn.col == 1)
                SetCloseUpActive(isNoteOn);
            else if (btn.col == 2 && isNoteOn)
                RandomizePosition();
        }

        // ── activation ───────────────────────────────────────────────────────

        void SetCloseUpActive(bool active)
        {
            if (active)
            {
                _activeFloorVolume = GetCurrentFloorVolume();

                if (_activeFloorVolume != null && _activeFloorVolume.dofVolumeObject != null)
                {
                    var vol = _activeFloorVolume.dofVolumeObject.GetComponent<Volume>();
                    if (vol != null)
                    {
                        // Clone profile so we never mutate the saved asset
                        vol.profile      = Object.Instantiate(vol.sharedProfile);
                        _activeDOFVolume = vol;
                        _activeDOFVolume.enabled = true;
                        vol.profile.TryGet(out _activeDOF);
                    }
                }

                _closeUpVcam.gameObject.SetActive(true);
            }
            else
            {
                _closeUpVcam.gameObject.SetActive(false);

                if (_activeDOFVolume != null)
                {
                    _activeDOFVolume.enabled = false;
                    // Restore shared profile reference so the clone can be GC'd
                    _activeDOFVolume.profile = _activeDOFVolume.sharedProfile;
                    _activeDOFVolume         = null;
                }

                _activeDOF         = null;
                _activeFloorVolume = null;
            }
        }

        // ── repositioning ────────────────────────────────────────────────────

        void RandomizePosition()
        {
            if (_closeUpVcam == null) return;

            var floorVol = _activeFloorVolume ?? GetCurrentFloorVolume();
            if (floorVol == null)
            {
                Debug.LogWarning("[CloseUpCameraController] No FloorVolume for current floor.");
                return;
            }

            Bounds roomBounds = floorVol.WorldBounds;

            // Primary: Physics.OverlapBox — fast broadphase query
            var hits       = Physics.OverlapBox(roomBounds.center, roomBounds.extents);
            var candidates = new System.Collections.Generic.List<Renderer>(16);

            foreach (var hit in hits)
            {
                if (hit.gameObject == _closeUpVcam.gameObject) continue;
                // Prefer the renderer on the hit GO; fall back to first child renderer
                var r = hit.GetComponent<Renderer>()
                     ?? hit.GetComponentInChildren<Renderer>();
                if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                    candidates.Add(r);
            }

            // Fallback: Y-band scan (catches objects without colliders)
            if (candidates.Count == 0)
            {
                var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                foreach (var r in allRenderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.gameObject == _closeUpVcam.gameObject) continue;
                    float y = r.bounds.center.y;
                    if (y >= roomBounds.min.y && y <= roomBounds.max.y)
                        candidates.Add(r);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning("[CloseUpCameraController] No objects found in floor volume.");
                return;
            }

            PlaceCameraOnTarget(candidates[_rng.Next(0, candidates.Count)]);
        }

        void PlaceCameraOnTarget(Renderer target)
        {
            Bounds  bounds = target.bounds;
            Vector3 center = bounds.center;
            float   radius = Mathf.Max(bounds.extents.magnitude, _minRadius);

            float angle  = (float)(_rng.NextDouble() * 360.0) * Mathf.Deg2Rad;
            float dist   = radius + Mathf.Lerp(_distMin, _distMax, (float)_rng.NextDouble());
            float jitter = radius * _heightJitter;
            float height = Mathf.Lerp(-jitter, jitter, (float)_rng.NextDouble());

            Vector3 pos = center + new Vector3(
                Mathf.Cos(angle) * dist,
                height,
                Mathf.Sin(angle) * dist);

            _closeUpVcam.transform.position = pos;
            _closeUpVcam.transform.LookAt(center);

            // Update DOF focus distance to match actual camera-to-target distance
            if (_activeDOF != null)
                _activeDOF.focusDistance.Override(Vector3.Distance(pos, center));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        FloorVolume GetCurrentFloorVolume()
        {
            int current = FloorCameraController.CurrentFloor;
            if (_floorVolumes != null)
                foreach (var fv in _floorVolumes)
                    if (fv.floorIndex == current) return fv;
            return null;
        }
    }
}
