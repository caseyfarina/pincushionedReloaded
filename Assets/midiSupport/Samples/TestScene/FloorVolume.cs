using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Attached to each floor root. Defines the floor's spatial bounds (via a
    /// trigger BoxCollider) and holds a reference to the DOF Volume GameObject
    /// used by CloseUpCameraController when this floor is active.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FloorVolume : MonoBehaviour
    {
        [Tooltip("0-indexed floor number (floor 0 = Y 0, floor 7 = Y 56)")]
        public int floorIndex;

        [Tooltip("Child GameObject containing the Volume component with DOF override")]
        public GameObject dofVolumeObject;

        BoxCollider _collider;

        void Awake() => _collider = GetComponent<BoxCollider>();

        /// <summary>World-space bounds of this floor's room volume.</summary>
        public Bounds WorldBounds
        {
            get
            {
                if (_collider == null) _collider = GetComponent<BoxCollider>();
                return new Bounds(
                    transform.TransformPoint(_collider.center),
                    Vector3.Scale(_collider.size, transform.lossyScale));
            }
        }
    }
}
