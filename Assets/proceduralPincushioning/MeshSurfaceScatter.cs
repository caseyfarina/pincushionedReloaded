using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// High-performance mesh surface scatterer using precomputed sample pools and
/// density-weighted selection. Renders via ScatterRendererFeature — zero GameObjects,
/// pure GPU instancing issued from inside URP's Render Graph pipeline.
///
/// Workflow:
///   1. Bake samples via Tools > Scatter > Batch Process Folder
///   2. Assign the resulting SurfaceSampleData asset to this component
///   3. Configure pin variants and density mode
///   4. Add ScatterRendererFeature to your URP Renderer asset (one-time setup)
///
/// Unity 6.3 URP compatible (Render Graph + Compatibility Mode).
/// </summary>
[ExecuteAlways]
public class MeshSurfaceScatter : MonoBehaviour
{
    // ── Precomputed Data ────────────────────────────────────────────────
    [Header("Precomputed Surface Data")]
    [Tooltip("Baked sample pool from the editor tool. Assign one per target mesh.")]
    [SerializeField] private SurfaceSampleData sampleData;

    [Tooltip("Transform of the target surface (for local-to-world). Uses this transform if null.")]
    [SerializeField] private Transform surfaceTransform;

    // ── Pin Meshes ──────────────────────────────────────────────────────
    [Header("Pin Meshes")]
    [SerializeField] private PinVariant[] pinVariants;

    // ── Scatter Parameters ──────────────────────────────────────────────
    [Header("Scatter Settings")]
    [Range(0, 10000)]
    [SerializeField] private int scatterCount = 500;

    [Tooltip("Random seed. Controls selection order and variant assignment.")]
    [SerializeField] private int seed = 42;

    [Header("Pin Transform")]
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [Tooltip("Offset along surface normal.")]
    [SerializeField] private float normalOffset = 0.0f;

    [SerializeField] private bool randomYawRotation = true;

    [Range(0f, 45f)]
    [SerializeField] private float maxTiltAngle = 0f;

    // ── Density ─────────────────────────────────────────────────────────
    [Header("Density")]
    [SerializeField] private ScatterDensity density = new ScatterDensity();

    [Tooltip("How much density weight influences pin scale. " +
             "0 = no effect, 1 = full range from scaleRange.x (zero density) to scaleRange.y (full density).")]
    [Range(0f, 1f)]
    [SerializeField] private float scaleByDensity = 0f;

    // ── Runtime Controls ────────────────────────────────────────────────
    [Header("Runtime")]
    [SerializeField] private bool rescatter = false;

    // ── Rendering ───────────────────────────────────────────────────────
    [Header("Rendering")]
    [SerializeField] private ShadowCastingMode castShadows = ShadowCastingMode.On;
    [SerializeField] private bool receiveShadows = true;
    [SerializeField] private uint renderingLayerMask = 1;

    // ═════════════════════════════════════════════════════════════════════
    // Internal state
    // ═════════════════════════════════════════════════════════════════════
    private int _lastScatterCount = -1;
    private int _lastSeed = -1;

    private List<List<Matrix4x4[]>> _variantBatches;

    // Cached weight and selection arrays — reused across Scatter() calls
    // to reduce GC pressure when rescattering frequently.
    private float[] _weights;
    private float[] _cumWeights;

    // Transform tracking — pins follow surface movement without re-scattering.
    // _variantBatches stores local-space matrices; RenderBatches() applies
    // the current localToWorldMatrix each frame.
    private Matrix4x4[] _renderBuffer = new Matrix4x4[1023];

    // ─────────────────────────────────────────────────────────────────────
    [System.Serializable]
    public struct PinVariant
    {
        public Mesh mesh;
        public Material material;
        [Min(0.01f)]
        public float weight;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ═════════════════════════════════════════════════════════════════════

    private void OnEnable()  => _variantBatches = null; // scatter on first Update
    private void OnDisable() => _variantBatches = null;

    private void Update()
    {
        if (_variantBatches == null ||
            rescatter ||
            _lastScatterCount != scatterCount ||
            _lastSeed != seed)
        {
            Scatter();
            rescatter = false;
        }

        RenderBatches();
    }

    private void OnValidate()
    {
        // Re-scatter when inspector values change, but only if already initialized.
        if (_variantBatches != null)
            Scatter();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Registry — called every Update() since the registry clears each frame
    // ═════════════════════════════════════════════════════════════════════

    private void RenderBatches()
    {
        if (_variantBatches == null) return;

        Transform xform = surfaceTransform != null ? surfaceTransform : transform;
        Matrix4x4 localToWorld = xform.localToWorldMatrix;
        Bounds bounds = new Bounds(xform.position, Vector3.one * 1000f);

        for (int v = 0; v < pinVariants.Length; v++)
        {
            if (pinVariants[v].mesh == null || pinVariants[v].material == null) continue;
            if (_variantBatches[v].Count == 0) continue;

            var rp = new RenderParams(pinVariants[v].material)
            {
                shadowCastingMode  = castShadows,
                receiveShadows     = receiveShadows,
                worldBounds        = bounds,
                layer              = gameObject.layer,
                renderingLayerMask = renderingLayerMask,
            };

            for (int b = 0; b < _variantBatches[v].Count; b++)
            {
                var localBatch = _variantBatches[v][b];
                int count = localBatch.Length;

                for (int i = 0; i < count; i++)
                    _renderBuffer[i] = localToWorld * localBatch[i];

                Graphics.RenderMeshInstanced(rp, pinVariants[v].mesh, 0, _renderBuffer, count);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Core: Density-Weighted Scatter
    // ═════════════════════════════════════════════════════════════════════

    public void Scatter()
    {
        if (sampleData == null || sampleData.positions == null ||
            pinVariants == null || pinVariants.Length == 0)
            return;

        _lastScatterCount = scatterCount;
        _lastSeed = seed;

        int poolSize = sampleData.sampleCount;
        Transform xform = surfaceTransform != null ? surfaceTransform : transform;
        Matrix4x4 localToWorld = xform.localToWorldMatrix;

        // ── 1. Transform all samples to world space and evaluate density ──
        if (_weights == null || _weights.Length != poolSize)
        {
            _weights = new float[poolSize];
            _cumWeights = new float[poolSize];
        }

        bool isUniform = density.mode == ScatterDensity.DensityMode.Uniform;

        if (!isUniform)
        {
            Vector3[] worldPositions = new Vector3[poolSize];
            Vector2[] uvs = sampleData.hasUVs ? sampleData.uvs : new Vector2[poolSize];

            for (int i = 0; i < poolSize; i++)
                worldPositions[i] = localToWorld.MultiplyPoint3x4(sampleData.positions[i]);

            density.EvaluateAll(worldPositions, uvs, _weights, poolSize);

            float running = 0f;
            for (int i = 0; i < poolSize; i++)
            {
                running += _weights[i];
                _cumWeights[i] = running;
            }

            if (running <= 0f)
            {
                _variantBatches = null;
                return;
            }
        }

        // ── 2. Select samples ───────────────────────────────────────────
        System.Random rng = new System.Random(seed);
        int actualCount = Mathf.Min(scatterCount, poolSize);

        float[] variantCumWeights = new float[pinVariants.Length];
        float totalVariantWeight = 0f;
        for (int i = 0; i < pinVariants.Length; i++)
        {
            totalVariantWeight += pinVariants[i].weight;
            variantCumWeights[i] = totalVariantWeight;
        }

        var variantLists = new List<List<Matrix4x4>>(pinVariants.Length);
        for (int i = 0; i < pinVariants.Length; i++)
            variantLists.Add(new List<Matrix4x4>());

        if (isUniform)
        {
            int[] indices = BuildShuffledIndices(poolSize, rng);
            for (int i = 0; i < actualCount; i++)
                BuildInstance(indices[i], 1f, rng,
                    variantCumWeights, totalVariantWeight, variantLists);
        }
        else
        {
            float totalWeight = _cumWeights[poolSize - 1];
            var selected = new HashSet<int>();
            int maxAttempts = actualCount * 4;
            int attempts = 0, placed = 0;

            while (placed < actualCount && attempts < maxAttempts)
            {
                attempts++;
                float r = (float)(rng.NextDouble() * totalWeight);
                int idx = System.Array.BinarySearch(_cumWeights, 0, poolSize, r);
                if (idx < 0) idx = ~idx;
                idx = Mathf.Clamp(idx, 0, poolSize - 1);

                if (_weights[idx] <= 0f) continue;
                if (!selected.Add(idx)) continue;

                BuildInstance(idx, _weights[idx], rng,
                    variantCumWeights, totalVariantWeight, variantLists);
                placed++;
            }
        }

        // ── 3. Batch into 1023-element arrays ───────────────────────────
        const int batchSize = 1023;
        _variantBatches = new List<List<Matrix4x4[]>>(pinVariants.Length);

        for (int v = 0; v < pinVariants.Length; v++)
        {
            var batches = new List<Matrix4x4[]>();
            var matrices = variantLists[v];

            for (int offset = 0; offset < matrices.Count; offset += batchSize)
            {
                int count = Mathf.Min(batchSize, matrices.Count - offset);
                Matrix4x4[] batch = new Matrix4x4[count];
                matrices.CopyTo(offset, batch, 0, count);
                batches.Add(batch);
            }

            _variantBatches.Add(batches);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Instance Builder
    // ═════════════════════════════════════════════════════════════════════

    private void BuildInstance(
        int sampleIdx,
        float densityWeight,
        System.Random rng,
        float[] variantCumWeights,
        float totalVariantWeight,
        List<List<Matrix4x4>> variantLists)
    {
        Vector3 localPos = sampleData.positions[sampleIdx];
        Vector3 localNormal = sampleData.normals[sampleIdx].normalized;
        localPos += localNormal * normalOffset;

        float vr = (float)(rng.NextDouble() * totalVariantWeight);
        int variantIdx = System.Array.BinarySearch(variantCumWeights, vr);
        if (variantIdx < 0) variantIdx = ~variantIdx;
        variantIdx = Mathf.Clamp(variantIdx, 0, pinVariants.Length - 1);

        Quaternion rot = Quaternion.FromToRotation(Vector3.up, localNormal);

        if (randomYawRotation)
            rot *= Quaternion.AngleAxis((float)(rng.NextDouble() * 360.0), Vector3.up);

        if (maxTiltAngle > 0f)
        {
            Vector3 tiltAxis = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0), 0f,
                (float)(rng.NextDouble() * 2.0 - 1.0)).normalized;
            rot *= Quaternion.AngleAxis((float)(rng.NextDouble() * maxTiltAngle), tiltAxis);
        }

        float baseScale    = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());
        float densityScale = Mathf.Lerp(scaleRange.x, scaleRange.y, densityWeight);
        float finalScale   = Mathf.Lerp(baseScale, densityScale, scaleByDensity);

        variantLists[variantIdx].Add(Matrix4x4.TRS(localPos, rot, Vector3.one * finalScale));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Fisher-Yates Shuffle
    // ═════════════════════════════════════════════════════════════════════

    private static int[] BuildShuffledIndices(int count, System.Random rng)
    {
        int[] indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        return indices;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Public API
    // ═════════════════════════════════════════════════════════════════════

    public void SetCount(int count)         { scatterCount = Mathf.Clamp(count, 0, 10000); Scatter(); }
    public void RandomizeAndScatter()       { seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue); Scatter(); }
    public void SetSeed(int newSeed)        { seed = newSeed; Scatter(); }
    public void SetSampleData(SurfaceSampleData newData) { sampleData = newData; _weights = null; _cumWeights = null; Scatter(); }
    public void SetNormalOffset(float v)    { normalOffset = v; Scatter(); }
    public void SetMaxTiltAngle(float v)    { maxTiltAngle = Mathf.Clamp(v, 0f, 45f); Scatter(); }
    public void SetScaleByDensity(float v)  { scaleByDensity = Mathf.Clamp01(v); Scatter(); }
    public void SetScaleMin(float v)        { scaleRange.x = v; Scatter(); }
    public void SetScaleMax(float v)        { scaleRange.y = v; Scatter(); }
    public ScatterDensity Density           => density;
    public void ForceRescatter()            => Scatter();
}
