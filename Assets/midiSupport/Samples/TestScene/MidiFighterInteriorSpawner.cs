using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Spawns one interior prefab instance per Midi Fighter 64 pad, scattered in
    /// 3D space. Pressing a pad toggles that instance on/off.
    ///
    /// The layout is <b>seeded</b>: the same seed always produces the same
    /// arrangement, so a composition you like is reproducible instead of being
    /// lost when you leave Play mode. Change Seed (or press Reroll Layout in the
    /// Inspector) to explore; keep the seed to keep the look.
    /// </summary>
    public class MidiFighterInteriorSpawner : MonoBehaviour
    {
        const int BUTTON_COUNT = 64;   // fixed by the MF64 grid, not an art parameter

        [Header("Layout")]
        [Tooltip("Half-extent of the scatter area on X and Z, in world units.")]
        [SerializeField] float _spread = 25f;

        [Tooltip("Y position for spawned instances.")]
        [SerializeField] float _groundY = 0f;

        [Tooltip("Random seed for positions and rotations. The same seed always " +
                 "rebuilds the identical arrangement.")]
        [SerializeField] int _seed = 12345;

        [Tooltip("Snap rotation to 90° steps. Off gives free rotation.")]
        [SerializeField] bool _quantizeRotation = true;

        [Tooltip("Uniform scale applied to every instance.")]
        [SerializeField] float _scale = 1f;

        [Header("Wiring")]
        [Tooltip("Parent for spawned instances. Leave empty to create a root object.")]
        [SerializeField] Transform _root;

        [Tooltip("Pads reserved by other systems and skipped here. Column 8 is floor " +
                 "navigation; row 8 cols 1-2 drive the close-up camera.")]
        [SerializeField] bool _reserveNavigationPads = true;

        readonly GameObject[] _instances = new GameObject[BUTTON_COUNT];

        void Start() => Build();

        /// <summary>Destroy and rebuild the whole arrangement from the current seed.</summary>
        public void Build()
        {
            var refs = Resources.Load<MidiFighterInteriorRefs>(MidiFighterInteriorRefs.ResourceName);
            if (refs == null || refs.prefabs == null || refs.prefabs.Length == 0)
            {
                Debug.LogWarning("[MidiFighterInteriorSpawner] MidiFighterInteriorRefs not found in Resources.");
                return;
            }

            Clear();

            if (_root == null)
                _root = new GameObject("Interior Objects").transform;

            // Seeded generator — deliberately NOT UnityEngine.Random, which is a
            // global shared stream that anything else in the scene can perturb.
            var rng   = new System.Random(_seed);
            int count = refs.prefabs.Length;

            for (int i = 0; i < BUTTON_COUNT; i++)
            {
                var prefab = refs.prefabs[i % count];
                if (prefab == null) continue;

                var go = Instantiate(prefab, _root);
                go.name = $"{prefab.name}_{i}";

                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * _spread;
                float z = (float)(rng.NextDouble() * 2.0 - 1.0) * _spread;
                go.transform.position = new Vector3(x, _groundY, z);

                float yaw = _quantizeRotation
                    ? rng.Next(0, 4) * 90f
                    : (float)(rng.NextDouble() * 360.0);
                go.transform.rotation   = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = Vector3.one * _scale;

                go.SetActive(false);
                _instances[i] = go;
            }
        }

        /// <summary>Destroy every spawned instance.</summary>
        public void Clear()
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i] == null) continue;
                if (Application.isPlaying) Destroy(_instances[i]);
                else DestroyImmediate(_instances[i]);
                _instances[i] = null;
            }
        }

        /// <summary>Pick a new random seed and rebuild.</summary>
        public void RerollLayout()
        {
            _seed = new System.Random().Next(int.MinValue, int.MaxValue);
            Build();
        }

        void OnEnable()  => MidiGridRouter.OnGridButton += HandleButton;
        void OnDisable() => MidiGridRouter.OnGridButton -= HandleButton;

        void HandleButton(GridButton btn, bool isNoteOn)
        {
            if (_reserveNavigationPads)
            {
                if (btn.col == 8) return;                    // floor navigation
                if (btn.row == 8 && btn.col <= 2) return;    // close-up camera
            }

            int idx = btn.linearIndex;
            if (idx < 0 || idx >= BUTTON_COUNT) return;

            var go = _instances[idx];
            if (go != null) go.SetActive(isNoteOn);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(0f, _groundY, 0f),
                                new Vector3(_spread * 2f, 0.05f, _spread * 2f));
        }
#endif
    }
}
