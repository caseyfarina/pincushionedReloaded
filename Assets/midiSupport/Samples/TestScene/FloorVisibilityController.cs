using System.Collections.Generic;
using UnityEngine;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Keeps only the floors you can actually see switched on.
    ///
    /// Measured on the 8-floor scene: leaving all floors active cost <b>125 ms CPU
    /// per frame</b>; isolating a single floor dropped it to <b>37 ms</b> — a 3.4x
    /// speedup, with draw calls falling 7,661 → 906 and triangles 113.8M → 12.3M.
    /// The cost is not geometry sitting in memory, it is 20 scatter instances
    /// submitting pins and 40 lights fighting for the shadow atlas every frame for
    /// rooms nobody is looking at.
    ///
    /// <b>Why SetActive rather than unloading scenes:</b> a whole-GameObject disable
    /// stops Update, rendering, lights and shadows in one call, costs nothing at
    /// steady state, and is instant. Async scene loading would introduce
    /// unpredictable hitches into a 0.6 s camera move during a live set, and memory
    /// was never the constraint.
    ///
    /// Floors need not be uniform: this works off <see cref="FloorVolume"/> indices,
    /// never off heights or spacing.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after FloorCameraController.Awake caches volumes
    public class FloorVisibilityController : MonoBehaviour
    {
        [Tooltip("Master switch. Off leaves every floor active — useful when " +
                 "authoring, or to A/B the cost.")]
        [SerializeField] bool _cullFloors = true;

        [Tooltip("Extra floors to keep active either side of the current one. 0 is " +
                 "cheapest. Raise to 1 if you can see into neighbouring rooms, or if " +
                 "a taller room's geometry pokes into the floor above.")]
        [Range(0, 3)]
        [SerializeField] int _neighbourRange = 0;

        [Tooltip("Keep the floor being left visible until the move finishes. On, " +
                 "because the camera travels through the gap between floors and the " +
                 "departing room would otherwise vanish mid-shot.")]
        [SerializeField] bool _keepSourceDuringMove = true;

        [Tooltip("Never disable these floor indices — e.g. a floor holding shared " +
                 "geometry or a light rig other floors depend on.")]
        [SerializeField] int[] _alwaysActive = new int[0];

        readonly Dictionary<int, FloorVolume> _floors = new Dictionary<int, FloorVolume>();
        readonly HashSet<int> _wanted = new HashSet<int>();

        int _pendingSource = -1;

        void OnEnable()
        {
            FloorCameraController.OnFloorChangeStarted   += HandleMoveStarted;
            FloorCameraController.OnFloorChangeCompleted += HandleMoveCompleted;
        }

        void OnDisable()
        {
            FloorCameraController.OnFloorChangeStarted   -= HandleMoveStarted;
            FloorCameraController.OnFloorChangeCompleted -= HandleMoveCompleted;
            SetAllActive(true);   // never leave the scene half-disabled
        }

        void Start()
        {
            CacheFloors();
            Apply(FloorCameraController.CurrentFloor, -1);
        }

        /// <summary>Re-scan for floors. Call after adding or removing one.</summary>
        public void CacheFloors()
        {
            _floors.Clear();
            foreach (var v in Object.FindObjectsByType<FloorVolume>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (v != null) _floors[v.floorIndex] = v;
        }

        void HandleMoveStarted(int from, int to)
        {
            _pendingSource = _keepSourceDuringMove ? from : -1;
            Apply(to, _pendingSource);
        }

        void HandleMoveCompleted(int floor)
        {
            _pendingSource = -1;
            Apply(floor, -1);
        }

        /// <summary>
        /// Switch on the target floor, its neighbours, any always-active floors and
        /// optionally the floor being left; switch everything else off.
        /// </summary>
        public void Apply(int targetFloor, int alsoKeep)
        {
            if (_floors.Count == 0) CacheFloors();

            if (!_cullFloors) { SetAllActive(true); return; }

            _wanted.Clear();
            _wanted.Add(targetFloor);
            if (alsoKeep >= 0) _wanted.Add(alsoKeep);

            // Neighbours are by INDEX, not by distance in world space — floors of
            // different heights are still adjacent if their indices are.
            for (int d = 1; d <= _neighbourRange; d++)
            {
                _wanted.Add(targetFloor - d);
                _wanted.Add(targetFloor + d);
                if (alsoKeep >= 0) { _wanted.Add(alsoKeep - d); _wanted.Add(alsoKeep + d); }
            }

            for (int i = 0; i < _alwaysActive.Length; i++) _wanted.Add(_alwaysActive[i]);

            foreach (var kv in _floors)
            {
                if (kv.Value == null) continue;
                bool want = _wanted.Contains(kv.Key);
                var go = kv.Value.gameObject;
                if (go.activeSelf != want) go.SetActive(want);   // only touch on change
            }
        }

        void SetAllActive(bool active)
        {
            foreach (var kv in _floors)
                if (kv.Value != null && kv.Value.gameObject.activeSelf != active)
                    kv.Value.gameObject.SetActive(active);
        }

        /// <summary>Floors currently switched on — for diagnostics.</summary>
        public int ActiveFloorCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _floors)
                    if (kv.Value != null && kv.Value.gameObject.activeSelf) n++;
                return n;
            }
        }
    }
}
