using UnityEngine;

namespace Pincushioned.SplitScreen
{
    /// <summary>When the point of interest is re-evaluated.</summary>
    public enum PoiUpdateMode
    {
        /// <summary>Only when something calls <see cref="PointOfInterestFinder.Evaluate"/>.
        /// Best for performance: the sub-cameras hold a stable composition.</summary>
        Manual = 0,

        /// <summary>On a fixed interval.</summary>
        Interval = 1,

        /// <summary>Every frame. The POI — and every sub-camera aim — then chases
        /// the main camera continuously, which reads as twitchy.</summary>
        EveryFrame = 2,
    }

    /// <summary>
    /// Resolves the point every split-screen camera looks at: cast a ray from the
    /// main camera and take the first visible thing it hits.
    ///
    /// Two-stage resolution, because <b>this scene's artifacts have no colliders</b>
    /// — the pinned Smithsonian scans carry only MeshFilter + MeshRenderer, so a
    /// physics raycast sails straight past them and hits a wall instead:
    ///
    ///   1. <see cref="Physics.Raycast"/> — precise, respects layers, ignores
    ///      triggers (the per-floor FloorVolume boxes are triggers and would
    ///      otherwise give an invisible POI floating in the middle of a room).
    ///   2. Renderer-bounds fallback — ray vs. AABB over cached renderers. Coarser,
    ///      but it is what actually finds the sculptures.
    ///   3. A point at <see cref="_defaultDistance"/> along the ray, so the rig
    ///      always has something to aim at.
    ///
    /// Note the pins themselves can never be hit: they are drawn with
    /// Graphics.RenderMeshInstanced and have no GameObjects at all.
    /// </summary>
    public class PointOfInterestFinder : MonoBehaviour
    {
        [Header("Ray")]
        [Tooltip("Camera the ray is cast from. Empty = Camera.main.")]
        [SerializeField] Camera _sourceCamera;

        [Tooltip("Viewport point the ray passes through. (0.5, 0.5) is screen centre.")]
        [SerializeField] Vector2 _viewportPoint = new Vector2(0.5f, 0.5f);

        [Tooltip("Maximum ray length.")]
        [SerializeField] float _maxDistance = 500f;

        [Tooltip("Layers the ray can hit.")]
        [SerializeField] LayerMask _layerMask = ~0;

        [Header("Resolution")]
        [Tooltip("Fall back to ray-vs-renderer-bounds when the physics raycast misses. " +
                 "Required in this scene — the artifacts have no colliders.")]
        [SerializeField] bool _useRendererFallback = true;

        [Tooltip("Ignore renderers smaller than this, so dust and tiny props don't " +
                 "become the subject.")]
        [SerializeField] float _minRendererSize = 0.05f;

        [Tooltip("Ignore renderers LARGER than this. Structural geometry — the room " +
                 "shell, floor slabs — is what a raw raycast hits first, giving a POI " +
                 "on a wall instead of on a sculpture. 0 disables the limit.")]
        [SerializeField] float _maxRendererSize = 30f;

        [Tooltip("Resolve against renderer bounds BEFORE trying physics. On, because " +
                 "in this scene the room shell has a collider and the artifacts do " +
                 "not, so physics-first always wins with the wrong answer.")]
        [SerializeField] bool _preferRenderers = true;

        [Tooltip("Where to put the POI when nothing is hit at all.")]
        [SerializeField] float _defaultDistance = 20f;

        [Header("Update")]
        [SerializeField] PoiUpdateMode _updateMode = PoiUpdateMode.Manual;

        [Tooltip("Seconds between re-evaluations in Interval mode.")]
        [SerializeField] float _interval = 1f;

        [Tooltip("Seconds to ease to a newly found POI. 0 snaps.")]
        [SerializeField] float _smoothTime = 0.25f;

        // ── state ────────────────────────────────────────────────────────────

        /// <summary>Current (smoothed) point of interest in world space.</summary>
        public Vector3 Point { get; private set; }

        /// <summary>The renderer or collider the POI resolved to. May be null.</summary>
        public Transform Target { get; private set; }

        /// <summary>True once a POI has been resolved at least once.</summary>
        public bool HasPoint { get; private set; }

        /// <summary>Raised when the POI resolves to a different object.</summary>
        public event System.Action<Vector3> OnPointChanged;

        Vector3 _targetPoint;
        Vector3 _velocity;
        float   _nextEval;
        Renderer[] _rendererCache;

        Camera SourceCamera => _sourceCamera != null ? _sourceCamera : Camera.main;

        void OnEnable()
        {
            RefreshRendererCache();
            Evaluate();
        }

        void Update()
        {
            if (_updateMode == PoiUpdateMode.EveryFrame ||
                (_updateMode == PoiUpdateMode.Interval && Time.time >= _nextEval))
                Evaluate();

            // Ease toward the resolved point regardless of update mode, so a manual
            // re-evaluation still glides rather than snapping.
            Point = _smoothTime > 0f
                ? Vector3.SmoothDamp(Point, _targetPoint, ref _velocity, _smoothTime)
                : _targetPoint;
        }

        /// <summary>Re-cache the renderer list. Call after spawning scene content.</summary>
        public void RefreshRendererCache()
        {
            _rendererCache = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        /// <summary>Re-run the ray and resolve a new point of interest.</summary>
        public void Evaluate()
        {
            _nextEval = Time.time + Mathf.Max(0.01f, _interval);

            var cam = SourceCamera;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(_viewportPoint.x, _viewportPoint.y, 0f));

            Vector3 hitPoint;
            Transform hitTarget;

            bool resolved = _preferRenderers
                ? (TryRenderers(ray, out hitPoint, out hitTarget) ||
                   TryPhysics(ray, out hitPoint, out hitTarget))
                : (TryPhysics(ray, out hitPoint, out hitTarget) ||
                   (_useRendererFallback && TryRenderers(ray, out hitPoint, out hitTarget)));

            if (!resolved)
            {
                hitPoint  = ray.origin + ray.direction * _defaultDistance;
                hitTarget = null;
            }

            bool changed = hitTarget != Target || !HasPoint;

            _targetPoint = hitPoint;
            Target       = hitTarget;

            if (!HasPoint)
            {
                Point    = hitPoint;   // no easing on the very first resolve
                HasPoint = true;
            }

            if (changed) OnPointChanged?.Invoke(_targetPoint);
        }

        bool TryPhysics(Ray ray, out Vector3 point, out Transform target)
        {
            point  = default;
            target = null;

            // Triggers are excluded deliberately: FloorVolume uses trigger boxes
            // for its per-floor bounds and would win almost every cast.
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance,
                                 _layerMask, QueryTriggerInteraction.Ignore))
                return false;

            point  = hit.point;
            target = hit.transform;
            return true;
        }

        bool TryRenderers(Ray ray, out Vector3 point, out Transform target)
        {
            point  = default;
            target = null;

            if (_rendererCache == null) RefreshRendererCache();

            float best = float.MaxValue;

            for (int i = 0; i < _rendererCache.Length; i++)
            {
                var r = _rendererCache[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                // Particles are explicitly not valid subjects.
                if (r is ParticleSystemRenderer) continue;

                // Don't let the rig aim at its own gizmo/helper geometry.
                if (r.transform.IsChildOf(transform)) continue;

                if ((_layerMask.value & (1 << r.gameObject.layer)) == 0) continue;

                Bounds b = r.bounds;
                float size = b.size.magnitude;
                if (size < _minRendererSize) continue;
                if (_maxRendererSize > 0f && size > _maxRendererSize) continue;

                if (b.IntersectRay(ray, out float dist) && dist < best && dist <= _maxDistance)
                {
                    best   = dist;
                    point  = ray.GetPoint(dist);
                    target = r.transform;
                }
            }

            return target != null;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var cam = SourceCamera;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(_viewportPoint.x, _viewportPoint.y, 0f));

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawLine(ray.origin, HasPoint ? Point : ray.origin + ray.direction * _defaultDistance);

            if (!HasPoint) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Point, 0.35f);
            Gizmos.DrawLine(Point + Vector3.up * 0.6f, Point - Vector3.up * 0.6f);

            if (Target != null)
            {
                var r = Target.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.25f);
                    Gizmos.DrawWireCube(r.bounds.center, r.bounds.size);
                }
            }
        }
#endif
    }
}
