using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Attached to each floor root. This is the <b>source of truth</b> for where a
    /// floor is and how big it is, so floors do not have to be uniform: make a room
    /// taller, wider, or move it, and the camera and visibility systems follow
    /// automatically. Nothing derives a floor's position from
    /// <c>index * uniformHeight</c> any more.
    ///
    /// Defines the floor's spatial bounds (via a trigger BoxCollider) and holds the
    /// DOF Volume used by <see cref="CloseUpCameraController"/> when active.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FloorVolume : MonoBehaviour
    {
        [Tooltip("0-indexed floor number. Order is by index, not by height, so " +
                 "floors of different sizes can sit at any Y.")]
        public int floorIndex;

        [Tooltip("Child GameObject containing the Volume component with DOF override")]
        public GameObject dofVolumeObject;

        [Header("Camera framing")]
        [Tooltip("Optional. When set, the floor camera moves to this transform's " +
                 "position exactly — full control for a room that needs a different " +
                 "distance or angle, not just a different height. Leave empty to " +
                 "derive the height from this floor's bounds.")]
        public Transform cameraAnchor;

        [Tooltip("Height above this floor's bounds centre for the camera, used only " +
                 "when Camera Anchor is empty. Lets a taller room pull the camera up " +
                 "without hand-placing an anchor.")]
        public float cameraHeightOffset = 3.7f;

        BoxCollider _collider;

        BoxCollider Collider
        {
            get
            {
                if (_collider == null) _collider = GetComponent<BoxCollider>();
                return _collider;
            }
        }

        void Awake() => _collider = GetComponent<BoxCollider>();

        /// <summary>World-space bounds of this floor's room volume.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var c = Collider;
                if (c == null) return new Bounds(transform.position, Vector3.one);
                return new Bounds(
                    transform.TransformPoint(c.center),
                    Vector3.Scale(c.size, transform.lossyScale));
            }
        }

        /// <summary>
        /// Where the floor camera sits for this floor. Uses <see cref="cameraAnchor"/>
        /// when assigned; otherwise the bounds centre raised by
        /// <see cref="cameraHeightOffset"/>. Because it reads the live bounds, resizing
        /// the room's collider re-frames the camera with no code change.
        /// </summary>
        public Vector3 CameraPosition
        {
            get
            {
                if (cameraAnchor != null) return cameraAnchor.position;
                Bounds b = WorldBounds;
                return new Vector3(b.center.x, b.center.y + cameraHeightOffset, b.center.z);
            }
        }

        /// <summary>True when a world point falls inside this floor's volume.</summary>
        public bool Contains(Vector3 worldPoint) => WorldBounds.Contains(worldPoint);

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Bounds b = WorldBounds;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(b.center, b.size);

            Gizmos.color = Color.cyan;
            Vector3 cam = CameraPosition;
            Gizmos.DrawWireSphere(cam, 0.4f);
            Gizmos.DrawLine(cam, b.center);
        }
#endif
    }
}
