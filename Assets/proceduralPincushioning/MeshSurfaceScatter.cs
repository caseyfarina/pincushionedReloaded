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
/// <summary>How pin size is decided.</summary>
public enum PinScaleMode
{
    /// <summary>Pin size is a fraction of the pinned model's bounding-sphere
    /// radius, normalised by each pin mesh's own size. Scale-invariant: the same
    /// settings read identically on models of wildly different size.</summary>
    RelativeToModel = 0,

    /// <summary>Raw multipliers on the pin mesh. Legacy; every model needs its own
    /// hand-tuned numbers.</summary>
    Absolute = 1,
}

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
    [Tooltip("Relative (default) sizes pins as a fraction of the pinned model's " +
             "bounding-sphere radius, so a pin looks the same on a small skull and a " +
             "huge sarcophagus with no retuning. Absolute uses Scale Range as raw " +
             "multipliers - the legacy behaviour, which needed per-model hand-tuning.")]
    [SerializeField] private PinScaleMode scaleMode = PinScaleMode.RelativeToModel;

    [Tooltip("Pin size as a fraction of the model's bounding-sphere radius. " +
             "0.03-0.08 means every pin is 3%-8% of the object's radius.")]
    [SerializeField] private Vector2 relativeScaleRange = new Vector2(0.03f, 0.08f);

    [Tooltip("Raw scale multipliers. Used only in Absolute mode.")]
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

    // Per-variant multiplier turning "fraction of the model's radius" into a scale
    // for that specific pin mesh. Recomputed each Scatter(); pin meshes differ in
    // size, so a single global factor would make some variants wrong.
    private float[] _variantUnitScale;

    // Local-space bounds of the placed instances, computed once per Scatter().
    // RenderBatches transforms this to world space each frame. Without it the
    // render bounds were a hardcoded 1000-unit cube, which meant Unity could
    // never frustum-cull a scatter instance — every floor in the building
    // submitted its pins every frame, visible or not.
    private Bounds _localBounds = new Bounds(Vector3.zero, Vector3.one);
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
        Bounds bounds = TransformBounds(localToWorld, _localBounds);

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
        RecomputeVariantUnitScales();
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

        RecomputeLocalBounds(variantLists);
    }

    /// <summary>
    /// Radius of the pinned model's bounding sphere, in the surface's local space.
    /// Taken from the baked sample data's source mesh when available so it matches
    /// exactly what was sampled; falls back to the MeshFilter, then to the spread
    /// of the baked sample positions themselves.
    /// </summary>
    public float ModelRadius
    {
        get
        {
            if (sampleData != null && sampleData.sourceMesh != null)
                return sampleData.sourceMesh.bounds.extents.magnitude;

            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return mf.sharedMesh.bounds.extents.magnitude;

            // Last resort: derive it from the baked samples.
            if (sampleData != null && sampleData.positions != null && sampleData.positions.Length > 0)
            {
                var min = sampleData.positions[0];
                var max = min;
                for (int i = 1; i < sampleData.positions.Length; i++)
                {
                    min = Vector3.Min(min, sampleData.positions[i]);
                    max = Vector3.Max(max, sampleData.positions[i]);
                }
                return ((max - min) * 0.5f).magnitude;
            }
            return 1f;
        }
    }

    /// <summary>
    /// Build the per-variant multipliers that turn "fraction of the model's radius"
    /// into a scale for each pin mesh.
    ///
    /// Pin meshes are authored at arbitrary sizes, so the fraction has to be divided
    /// by each mesh's own radius. Without that, two variants asked for the same
    /// fraction would come out visibly different sizes.
    /// </summary>
    private void RecomputeVariantUnitScales()
    {
        if (pinVariants == null) { _variantUnitScale = null; return; }

        if (_variantUnitScale == null || _variantUnitScale.Length != pinVariants.Length)
            _variantUnitScale = new float[pinVariants.Length];

        float modelRadius = ModelRadius;

        for (int v = 0; v < pinVariants.Length; v++)
        {
            var mesh = pinVariants[v].mesh;
            float pinRadius = mesh != null ? mesh.bounds.extents.magnitude : 0f;

            // A degenerate pin mesh would divide by ~zero and produce astronomically
            // large instances, so fall back to an unscaled multiplier instead.
            _variantUnitScale[v] = pinRadius > 1e-6f ? modelRadius / pinRadius : 1f;
        }
    }

    /// <summary>Largest scale any instance can reach, for bounds padding.</summary>
    private float MaxEffectiveScale
    {
        get
        {
            if (scaleMode == PinScaleMode.Absolute)
                return Mathf.Max(Mathf.Abs(scaleRange.x), Mathf.Abs(scaleRange.y));

            float frac = Mathf.Max(Mathf.Abs(relativeScaleRange.x), Mathf.Abs(relativeScaleRange.y));
            float unit = 1f;
            if (_variantUnitScale != null)
                for (int i = 0; i < _variantUnitScale.Length; i++)
                    unit = Mathf.Max(unit, _variantUnitScale[i]);
            return frac * unit;
        }
    }

    /// <summary>
    /// Tight local-space bounds around every placed instance, padded by the
    /// largest pin's reach so a pin straddling the edge is never clipped.
    /// </summary>
    private void RecomputeLocalBounds(List<List<Matrix4x4>> variantLists)
    {
        bool any = false;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int v = 0; v < variantLists.Count; v++)
        {
            var list = variantLists[v];
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 p = list[i].GetColumn(3);   // translation
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }

        if (!any)
        {
            _localBounds = new Bounds(Vector3.zero, Vector3.zero);
            return;
        }

        // Pad by the largest pin mesh extent at the largest scale. Instance
        // positions are pin origins, so without this a tall pin near the edge
        // would be culled while still on screen.
        float pinReach = 0f;
        for (int v = 0; v < pinVariants.Length; v++)
            if (pinVariants[v].mesh != null)
                pinReach = Mathf.Max(pinReach, pinVariants[v].mesh.bounds.extents.magnitude);

        float pad = pinReach * MaxEffectiveScale + Mathf.Abs(normalOffset);

        var b = new Bounds((min + max) * 0.5f, max - min);
        b.Expand(pad * 2f);
        _localBounds = b;
    }

    /// <summary>Axis-aligned world bounds of a local bounds under a transform.</summary>
    private static Bounds TransformBounds(Matrix4x4 m, Bounds local)
    {
        Vector3 c = m.MultiplyPoint3x4(local.center);
        Vector3 e = local.extents;
        // Sum the absolute contribution of each basis vector — the standard AABB
        // transform. Cheaper and tighter than transforming all eight corners.
        Vector3 ax = m.GetColumn(0) * e.x;
        Vector3 ay = m.GetColumn(1) * e.y;
        Vector3 az = m.GetColumn(2) * e.z;
        Vector3 ext = new Vector3(
            Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
            Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
            Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        return new Bounds(c, ext * 2f);
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

        Vector2 range = scaleMode == PinScaleMode.RelativeToModel ? relativeScaleRange : scaleRange;

        float baseScale    = Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());
        float densityScale = Mathf.Lerp(range.x, range.y, densityWeight);
        float finalScale   = Mathf.Lerp(baseScale, densityScale, scaleByDensity);

        // In relative mode the value so far is a fraction of the model's radius;
        // convert it to a multiplier for THIS pin mesh.
        if (scaleMode == PinScaleMode.RelativeToModel &&
            _variantUnitScale != null && variantIdx < _variantUnitScale.Length)
            finalScale *= _variantUnitScale[variantIdx];

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
