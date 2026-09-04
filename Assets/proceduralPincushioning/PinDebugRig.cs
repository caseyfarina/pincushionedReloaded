using System.Collections.Generic;
using UnityEngine;

namespace Pincushioned.Debugging
{
    /// <summary>
    /// Lays every pinned mesh out in a grid and drives their scatter settings
    /// together, so pin behaviour can be tuned and verified in isolation before it
    /// reaches the main scene.
    ///
    /// Why this exists:
    /// <list type="bullet">
    /// <item>Pin scale is only meaningfully judged by <b>comparing</b> models of
    /// different size side by side. A setting that looks right on one artifact can
    /// be invisible on another — which is exactly how the scale regression got
    /// through.</item>
    /// <item>The main scene carries the MIDI rig, and with controllers connected a
    /// script recompile crashes the editor (RtMidi throws during assembly reload).
    /// This scene has no MIDI, so iterating here is safe.</item>
    /// <item>The main scene is 780 KB and slow to open; this one is trivial.</item>
    /// </list>
    ///
    /// Every model is spaced by its own bounding radius, so unequal sizes lay out
    /// without overlapping.
    /// </summary>
    [ExecuteAlways]
    public class PinDebugRig : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Prefabs to lay out. Populated from Assets/pinnedMeshes by the " +
                 "Rebuild Grid button if left empty.")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Header("Grid")]
        [Tooltip("Columns before wrapping to the next row.")]
        [Range(1, 10)]
        public int columns = 5;

        [Tooltip("Gap between models, as a multiple of each model's own radius. " +
                 "Spacing is per-model so large and small artifacts both sit clear.")]
        [Range(1.5f, 8f)]
        public float spacingFactor = 2.6f;

        [Tooltip("Normalise every model to the same on-screen size. ON by default: " +
                 "it is the only way to judge PIN size independently of MODEL size, " +
                 "and in Relative mode all pins should then look identical. It also " +
                 "stops one oversized model (the mammoth is 5x the artifacts) from " +
                 "inflating every grid cell and shrinking everything else.")]
        public bool normalizeModelSize = true;

        [Tooltip("Target radius when Normalize Model Size is on.")]
        public float normalizedRadius = 1f;

        [Header("Scatter settings applied to every model")]
        [Range(0, 5000)]
        public int scatterCount = 400;

        public PinScaleMode scaleMode = PinScaleMode.RelativeToModel;

        [Tooltip("Pin size as a fraction of each model's bounding-sphere radius.")]
        public Vector2 relativeScaleRange = new Vector2(0.03f, 0.20f);

        [Tooltip("1 = uniform. Higher eases out toward the minimum: most pins small, " +
                 "a few long.")]
        [Range(1f, 8f)]
        public float scaleBias = 3f;

        [Tooltip("Raw multipliers, used only in Absolute mode.")]
        public Vector2 absoluteScaleRange = new Vector2(0.8f, 1.2f);

        public float normalOffset = 0f;

        [Range(0f, 45f)]
        public float maxTiltAngle = 12f;

        readonly List<MeshSurfaceScatter> _scatters = new List<MeshSurfaceScatter>();

        /// <summary>Scatter components currently under this rig.</summary>
        public IReadOnlyList<MeshSurfaceScatter> Scatters => _scatters;

        /// <summary>Destroy and re-create the grid from <see cref="prefabs"/>.</summary>
        public void RebuildGrid()
        {
            ClearGrid();
            if (prefabs == null || prefabs.Count == 0) return;

            // A uniform cell sized to the LARGEST model. Per-model spacing packs
            // tighter but makes the grid ragged, and a ragged grid is much harder to
            // scan when the whole point is comparing pins across models.
            float maxRadius = 0.0001f;
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                var sc = prefab.GetComponent<MeshSurfaceScatter>();
                float r = sc != null ? sc.ModelRadius : 1f;
                if (normalizeModelSize) r = normalizedRadius;
                maxRadius = Mathf.Max(maxRadius, r);
            }

            float cell = maxRadius * spacingFactor;
            int cols = Mathf.Max(1, columns);

            int i = 0;
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;

                var go = Instantiate(prefab, transform);
                go.name = prefab.name;

                var scatter = go.GetComponent<MeshSurfaceScatter>();
                float radius = scatter != null ? scatter.ModelRadius : 1f;
                if (radius <= 0.0001f) radius = 1f;

                if (normalizeModelSize)
                    go.transform.localScale = Vector3.one * (normalizedRadius / radius);

                int col = i % cols;
                int row = i / cols;
                go.transform.localPosition = new Vector3(col * cell, 0f, -row * cell);

                if (scatter != null) _scatters.Add(scatter);
                i++;
            }

            ApplySettings();
        }

        /// <summary>World-space bounds of the whole grid, for camera framing.</summary>
        public Bounds GridBounds
        {
            get
            {
                var rs = GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return new Bounds(transform.position, Vector3.one);
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                return b;
            }
        }

        /// <summary>Models whose pins cannot render - no variant, or a missing mesh
        /// or material. These show as magenta or as nothing at all.</summary>
        public string BuildProblemReport()
        {
            CollectScatters();
            var sb = new System.Text.StringBuilder();
            foreach (var s in _scatters)
            {
                if (s == null) continue;
                var so = new List<string>();
                var variants = s.PinVariantSummary;
                if (variants.total == 0)          so.Add("no pin variants");
                if (variants.missingMesh > 0)     so.Add(variants.missingMesh + " missing mesh");
                if (variants.missingMaterial > 0) so.Add(variants.missingMaterial + " missing material");
                if (s.SampleData == null)         so.Add("no sample data");
                if (so.Count > 0) sb.AppendLine($"  {s.name,-46} {string.Join(", ", so)}");
            }
            return sb.Length == 0 ? "  (none - every model can render pins)" : sb.ToString();
        }

        /// <summary>Push the rig's settings onto every model and rescatter.</summary>
        public void ApplySettings()
        {
            CollectScatters();

            foreach (var s in _scatters)
            {
                if (s == null) continue;
                var so = new UnityEngine.Object[] { s };  // keep reference alive

                s.SetScaleMode(scaleMode);
                s.SetRelativeScaleRange(relativeScaleRange);
                s.SetScaleBias(scaleBias);
                s.SetAbsoluteScaleRange(absoluteScaleRange);
                s.SetNormalOffset(normalOffset);
                s.SetMaxTiltAngle(maxTiltAngle);
                s.SetCount(scatterCount);      // triggers the rescatter
            }
        }

        /// <summary>Give every model a fresh seed and rescatter.</summary>
        public void RerollAll()
        {
            CollectScatters();
            foreach (var s in _scatters)
                if (s != null) s.RandomizeAndScatter();
        }

        /// <summary>Remove every instantiated model.</summary>
        public void ClearGrid()
        {
            _scatters.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        void CollectScatters()
        {
            _scatters.Clear();
            GetComponentsInChildren(true, _scatters);
        }

        /// <summary>Per-model diagnostic lines: radius and resulting pin size.</summary>
        public string BuildReport()
        {
            CollectScatters();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{"model",-42}{"radius",9}{"pin min",10}{"pin max",10}");
            foreach (var s in _scatters)
            {
                if (s == null) continue;
                float r = s.ModelRadius;
                Vector2 world = s.EffectiveWorldPinSize;
                sb.AppendLine($"{s.name,-42}{r,9:F3}{world.x,10:F4}{world.y,10:F4}");
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            CollectScatters();
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.4f);
            foreach (var s in _scatters)
            {
                if (s == null) continue;
                float r = s.ModelRadius * s.transform.lossyScale.x;
                Gizmos.DrawWireSphere(s.transform.position, r);
            }
        }
#endif
    }
}
