using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using MidiFighter64;
using DG.Tweening;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// MF64 column 8 → vertical camera movement between floors.
    /// Row 1 (top) selects the highest floor, row 8 (bottom) selects floor 0.
    ///
    /// <b>Floors do not have to be uniform.</b> Each floor's position comes from its
    /// own <see cref="FloorVolume"/> — its bounds, or an explicit camera anchor — so
    /// rooms can be different heights and sizes and the camera follows. The old
    /// <c>index * floorHeight</c> arithmetic is only a fallback for scenes with no
    /// FloorVolumes at all.
    /// </summary>
    public class FloorCameraController : MonoBehaviour
    {
        [Header("Travel")]
        [Tooltip("Seconds to move between floors.")]
        [Range(0f, 3f)]
        [SerializeField] float _duration = 0.6f;

        [Tooltip("Easing curve for the floor move.")]
        [SerializeField] Ease _ease = Ease.InOutCubic;

        [Tooltip("Move the camera on all three axes toward the floor's camera " +
                 "position. Off tweens height only, keeping X/Z fixed — the original " +
                 "behaviour, and the right choice when every room shares a footprint.")]
        [SerializeField] bool _moveAllAxes = false;

        [Header("Fallback (no FloorVolumes in scene)")]
        [Tooltip("Vertical distance between floors when deriving positions arithmetically.")]
        [SerializeField] float _floorHeight = 8f;

        [Tooltip("Camera height above a floor's base Y when deriving arithmetically.")]
        [SerializeField] float _cameraYOffset = 7.7f;

        [Tooltip("Floor count used only when no FloorVolumes are present.")]
        [Range(1, 8)]
        [SerializeField] int _fallbackFloorCount = 8;

        [Header("Wiring")]
        [Tooltip("Virtual camera moved between floors. Leave empty to create one at " +
                 "runtime from Main Camera's transform (not authorable — assign a " +
                 "scene camera to tune framing directly).")]
        [SerializeField] CinemachineCamera _floorVcam;

        [Tooltip("Priority given to a runtime-created vcam. Ignored when Floor Vcam is assigned.")]
        [SerializeField] int _fallbackPriority = 10;

        Tweener _activeTween;

        // floorIndex -> volume, built once and kept sorted by index.
        readonly SortedDictionary<int, FloorVolume> _volumes = new SortedDictionary<int, FloorVolume>();

        /// <summary>Current floor index. Updated on every MF64 col-8 press.</summary>
        public static int CurrentFloor { get; private set; }

        /// <summary>The floor being travelled to; equals CurrentFloor when idle.</summary>
        public static int DestinationFloor { get; private set; }

        /// <summary>True while a floor move is in flight.</summary>
        public static bool IsMoving { get; private set; }

        /// <summary>Raised as a move begins: (from, to). Fires before the tween starts
        /// so listeners can make the destination visible in time.</summary>
        public static event Action<int, int> OnFloorChangeStarted;

        /// <summary>Raised when a move completes: (floor).</summary>
        public static event Action<int> OnFloorChangeCompleted;

        /// <summary>Number of floors, from the FloorVolumes present.</summary>
        public int FloorCount => _volumes.Count > 0 ? _volumes.Count : _fallbackFloorCount;

        /// <summary>Highest floor index in the scene.</summary>
        public int MaxFloorIndex
        {
            get
            {
                int max = -1;
                foreach (var kv in _volumes) if (kv.Key > max) max = kv.Key;
                return max >= 0 ? max : _fallbackFloorCount - 1;
            }
        }

        void Awake()
        {
            CacheVolumes();
            EnsureVcam();
            CurrentFloor = DestinationFloor = NearestFloorTo(
                _floorVcam != null ? _floorVcam.transform.position : Vector3.zero);
        }

        /// <summary>
        /// Re-scan for FloorVolumes. Includes inactive objects deliberately: the
        /// visibility system disables off-screen floors, and a disabled floor must
        /// still be a valid navigation target.
        /// </summary>
        public void CacheVolumes()
        {
            _volumes.Clear();
            foreach (var v in UnityEngine.Object.FindObjectsByType<FloorVolume>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (v == null) continue;
                if (!_volumes.ContainsKey(v.floorIndex)) _volumes[v.floorIndex] = v;
                else Debug.LogWarning($"[FloorCameraController] Duplicate floorIndex " +
                                      $"{v.floorIndex} on '{v.name}' — ignoring.");
            }
        }

        void EnsureVcam()
        {
            if (_floorVcam != null) return;

            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[FloorCameraController] No Floor Vcam assigned and no " +
                                 "Main Camera to derive one from.");
                return;
            }

            if (mainCam.GetComponent<CinemachineBrain>() == null)
            {
                var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.Cut, 0f);
            }

            var go = new GameObject("FloorVcam");
            go.transform.SetPositionAndRotation(
                mainCam.transform.position, mainCam.transform.rotation);

            _floorVcam          = go.AddComponent<CinemachineCamera>();
            _floorVcam.Priority = _fallbackPriority;
        }

        /// <summary>Camera position for a floor. Uses the floor's own volume when
        /// present, otherwise falls back to uniform arithmetic.</summary>
        public Vector3 CameraPositionFor(int floor)
        {
            if (_volumes.TryGetValue(floor, out var v) && v != null)
                return v.CameraPosition;

            Vector3 basis = _floorVcam != null ? _floorVcam.transform.position : Vector3.zero;
            return new Vector3(basis.x, floor * _floorHeight + _cameraYOffset, basis.z);
        }

        /// <summary>Floor whose camera position is nearest a world point.</summary>
        public int NearestFloorTo(Vector3 world)
        {
            if (_volumes.Count == 0)
                return Mathf.Clamp(Mathf.RoundToInt((world.y - _cameraYOffset) / _floorHeight),
                                   0, _fallbackFloorCount - 1);

            int best = 0; float bestDist = float.MaxValue;
            foreach (var kv in _volumes)
            {
                float d = Mathf.Abs(kv.Value.CameraPosition.y - world.y);
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.col != 8 || !isNoteOn || _floorVcam == null) return;

            // Row 1 (top) = highest floor. With unequal floors the mapping is by
            // index, so row 8 is always floor 0 regardless of physical heights.
            GoToFloor(MaxFloorIndex - (btn.row - 1));
        }

        /// <summary>Move to a floor. Public so other rigs or the Inspector can drive it.</summary>
        public void GoToFloor(int floor)
        {
            if (_floorVcam == null) return;

            floor = ClampToExistingFloor(floor);
            int from = CurrentFloor;

            DestinationFloor = floor;
            IsMoving         = true;
            OnFloorChangeStarted?.Invoke(from, floor);

            Vector3 target = CameraPositionFor(floor);
            _activeTween?.Kill();

            _activeTween = (_moveAllAxes
                    ? _floorVcam.transform.DOMove(target, _duration)
                    : _floorVcam.transform.DOMoveY(target.y, _duration))
                .SetEase(_ease)
                .OnComplete(() =>
                {
                    CurrentFloor = floor;
                    IsMoving     = false;
                    OnFloorChangeCompleted?.Invoke(floor);
                });

            // Update immediately as well, so anything reading CurrentFloor mid-move
            // sees the floor being entered rather than the one being left.
            CurrentFloor = floor;
        }

        int ClampToExistingFloor(int floor)
        {
            if (_volumes.Count == 0)
                return Mathf.Clamp(floor, 0, _fallbackFloorCount - 1);
            if (_volumes.ContainsKey(floor)) return floor;

            int best = 0; int bestDist = int.MaxValue;
            foreach (var kv in _volumes)
            {
                int d = Mathf.Abs(kv.Key - floor);
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_volumes.Count == 0) CacheVolumes();

            foreach (var kv in _volumes)
            {
                if (kv.Value == null) continue;
                Gizmos.color = kv.Key == CurrentFloor ? Color.cyan : new Color(1f, 1f, 1f, 0.35f);
                Vector3 p = kv.Value.CameraPosition;
                Gizmos.DrawWireCube(p, new Vector3(2f, 0.05f, 2f));
                UnityEditor.Handles.Label(p + Vector3.right, $"F{kv.Key}");
            }
        }
#endif
    }
}
