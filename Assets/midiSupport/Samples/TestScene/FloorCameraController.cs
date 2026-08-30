using UnityEngine;
using Unity.Cinemachine;
using MidiFighter64;
using DG.Tweening;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// MF64 column 8 → vertical camera movement between floors.
    /// Row 1 (top) selects the top floor, row 8 (bottom) selects floor 0.
    ///
    /// Every framing number is authored in the Inspector — floor spacing, camera
    /// height, travel time and easing are all live-tunable while the scene is open.
    /// </summary>
    public class FloorCameraController : MonoBehaviour
    {
        [Header("Building layout")]
        [Tooltip("Vertical distance between floors, in world units.")]
        [SerializeField] float _floorHeight = 8f;

        [Tooltip("Camera height above a floor's base Y.")]
        [SerializeField] float _cameraYOffset = 7.7f;

        [Tooltip("Number of floors. MF64 column 8 maps row 1 → top floor, row N → floor 0.")]
        [Range(1, 8)]
        [SerializeField] int _floorCount = 8;

        [Header("Travel")]
        [Tooltip("Seconds to move between floors.")]
        [Range(0f, 3f)]
        [SerializeField] float _duration = 0.6f;

        [Tooltip("Easing curve for the floor move.")]
        [SerializeField] Ease _ease = Ease.InOutCubic;

        [Header("Wiring")]
        [Tooltip("Virtual camera moved between floors. Leave empty to create one at " +
                 "runtime from Main Camera's transform (not authorable — assign a " +
                 "scene camera to tune framing directly).")]
        [SerializeField] CinemachineCamera _floorVcam;

        [Tooltip("Priority given to a runtime-created vcam. Ignored when Floor Vcam is assigned.")]
        [SerializeField] int _fallbackPriority = 10;

        Tweener _activeTween;

        /// <summary>Current floor index (0-based). Updated on every MF64 col-8 press.</summary>
        public static int CurrentFloor { get; private set; }

        /// <summary>World Y the camera sits at for a given floor.</summary>
        public float FloorToY(int floor) => floor * _floorHeight + _cameraYOffset;

        void Awake()
        {
            if (_floorVcam == null)
            {
                var mainCam = Camera.main;
                if (mainCam == null)
                {
                    Debug.LogWarning("[FloorCameraController] No Floor Vcam assigned and no " +
                                     "Main Camera to derive one from.");
                    return;
                }

                // Fallback only — assign _floorVcam in the scene to make this authorable.
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

            // Derive the starting floor from wherever the vcam already sits.
            CurrentFloor = Mathf.Clamp(
                Mathf.RoundToInt((_floorVcam.transform.position.y - _cameraYOffset) / _floorHeight),
                0, _floorCount - 1);
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (btn.col != 8 || !isNoteOn || _floorVcam == null) return;
            GoToFloor(_floorCount - btn.row);
        }

        /// <summary>Move to a floor. Public so other rigs (or the Inspector) can drive it.</summary>
        public void GoToFloor(int floor)
        {
            if (_floorVcam == null) return;

            floor        = Mathf.Clamp(floor, 0, _floorCount - 1);
            CurrentFloor = floor;

            _activeTween?.Kill();
            _activeTween = _floorVcam.transform
                .DOMoveY(FloorToY(floor), _duration)
                .SetEase(_ease);
        }

#if UNITY_EDITOR
        // Draw the floor stops in the Scene view so the spacing is visible while tuning.
        void OnDrawGizmosSelected()
        {
            Vector3 origin = _floorVcam != null ? _floorVcam.transform.position
                           : (Camera.main != null ? Camera.main.transform.position : transform.position);

            for (int i = 0; i < _floorCount; i++)
            {
                Gizmos.color = i == CurrentFloor ? Color.cyan : new Color(1f, 1f, 1f, 0.35f);
                var p = new Vector3(origin.x, FloorToY(i), origin.z);
                Gizmos.DrawWireCube(p, new Vector3(2f, 0.05f, 2f));
            }
        }
#endif
    }
}
