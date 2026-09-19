using UnityEngine;

/// <summary>
/// Where every backdrop instance sits, and how it moves. Pure static math over
/// (instanceId, seed, time) with no Unity object references, so the distribution
/// can be asserted in tests rather than judged by eye - which is the whole reason
/// this lives in C# instead of inside a node graph.
///
/// Nothing here integrates frame to frame. Position, rotation and flash are all
/// closed-form functions of the instance index and the clock, which is what lets
/// the renderer stay stateless and a rebuild reproduce a composition exactly.
/// </summary>
public static class BackdropLattice
{
    /// <summary>
    /// Integer hash. Deterministic across machines and Unity versions, unlike
    /// UnityEngine.Random, and unlike a float-noise hash it does not drift with
    /// the platform's floating point behaviour.
    /// </summary>
    public static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }
    }

    public static float Rand01(int id, uint seed, uint stream)
    {
        unchecked
        {
            uint h = Hash((uint)id * 0x9e3779b9u + seed * 0x85ebca6bu + stream * 0xc2b2ae35u);
            return h / 4294967295f;
        }
    }

    public static Vector3 Rand3(int id, uint seed, uint stream) => new Vector3(
        Rand01(id, seed, stream),
        Rand01(id, seed, stream + 101u),
        Rand01(id, seed, stream + 202u));

    /// <summary>
    /// The lattice point for one instance, before jitter. Even by construction
    /// rather than by random sampling, so density never piles up at a pole or an
    /// edge the way rejection sampling does.
    /// </summary>
    public static Vector3 Position(int id, int count, BackdropDomain domain, Vector3 size, bool solidFill)
    {
        count = Mathf.Max(count, 1);
        float t = (float)id / count;

        switch (domain)
        {
            case BackdropDomain.Plane:
            {
                // N x M grid, with N chosen so cells stay near-square on the
                // requested aspect instead of stretching with it.
                float aspect = Mathf.Max(size.x, 0.001f) / Mathf.Max(size.y, 0.001f);
                int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count * aspect)));
                int m = Mathf.Max(1, Mathf.CeilToInt((float)count / n));
                int col = id % n;
                int row = id / n;
                float u = n > 1 ? (float)col / (n - 1) - 0.5f : 0f;
                float v = m > 1 ? (float)row / (m - 1) - 0.5f : 0f;
                return new Vector3(u * size.x, v * size.y, 0f);
            }

            case BackdropDomain.Hemisphere:
            {
                // Fibonacci spherical cap. The golden angle keeps successive
                // points maximally separated, so there is no pole cluster - the
                // same reason SplitScreenCameraRig samples a cap this way.
                float y = 1f - t;
                float r = Mathf.Sqrt(Mathf.Clamp01(1f - y * y));
                float theta = id * 2.399963229728653f;
                return Vector3.Scale(
                    new Vector3(Mathf.Cos(theta) * r, y, Mathf.Sin(theta) * r),
                    size * 0.5f);
            }

            default:
            {
                // N x N x N lattice, hollow unless asked otherwise: the interior
                // of a solid cube is invisible from outside and would spend most
                // of the instance budget on nothing.
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Pow(Mathf.Max(count, 1), 1f / 3f)));
                int ix = id % n;
                int iy = (id / n) % n;
                int iz = (id / (n * n)) % n;

                var u = new Vector3(
                    (float)ix / (n - 1) - 0.5f,
                    (float)iy / (n - 1) - 0.5f,
                    (float)iz / (n - 1) - 0.5f);

                if (!solidFill)
                {
                    // Snap the dominant axis out to the shell rather than culling
                    // interior instances, so every instance the user asked for is
                    // visible instead of silently hidden inside.
                    float ax = Mathf.Abs(u.x), ay = Mathf.Abs(u.y), az = Mathf.Abs(u.z);
                    if (ax >= ay && ax >= az)      u.x = Mathf.Sign(u.x == 0f ? 1f : u.x) * 0.5f;
                    else if (ay >= ax && ay >= az) u.y = Mathf.Sign(u.y == 0f ? 1f : u.y) * 0.5f;
                    else                           u.z = Mathf.Sign(u.z == 0f ? 1f : u.z) * 0.5f;
                }

                return Vector3.Scale(u, size);
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="matrices"/> and <paramref name="flash"/> for the
    /// first p.spawnCount entries, at the given time. Returns how many were
    /// written, which is the count the renderer should submit.
    ///
    /// Buffers are caller-owned so the renderer can keep them across frames; this
    /// runs every frame and must not allocate.
    /// </summary>
    public static int Fill(in BackdropParameters p, float time, float flashTime,
                           Matrix4x4[] matrices, float[] flash)
    {
        int count = Mathf.Clamp(p.spawnCount, 0, Mathf.Min(matrices.Length, flash.Length));
        if (count == 0) return 0;

        Vector3 waveAxis = p.waveAxis.sqrMagnitude < 1e-6f ? Vector3.up : p.waveAxis.normalized;
        Vector3 spinAxis = p.spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : p.spinAxis.normalized;

        float waveW = p.waveFrequency * 2f * Mathf.PI;
        float decay = Mathf.Max(p.flashDecay, 0.01f);

        for (int i = 0; i < count; i++)
        {
            float idNorm = (float)i / count;

            Vector3 pos = Position(i, count, p.domain, p.domainSize, p.solidFill);
            Vector3 jitter = Rand3(i, p.layoutSeed, 11u) * 2f - Vector3.one;
            pos += Vector3.Scale(jitter, p.offsetJitter);

            // Absolute offset from the lattice point, never an accumulating add -
            // accumulating walks every instance off its lattice point permanently.
            float phase = idNorm * p.wavePhaseSpread * 2f * Mathf.PI;
            pos += waveAxis * (Mathf.Sin(time * waveW + phase) * p.waveAmplitude);

            float s = Mathf.Lerp(p.scaleRange.x, p.scaleRange.y, Rand01(i, p.layoutSeed, 31u));
            Vector3 scale = p.scaleAxisBias * s;

            Vector3 rot = Vector3.Scale(
                Rand3(i, p.layoutSeed, 53u) * 2f - Vector3.one,
                p.rotationJitter);

            float rate = Mathf.Lerp(p.spinRateRange.x, p.spinRateRange.y, Rand01(i, p.layoutSeed, 71u));
            Quaternion q = Quaternion.AngleAxis(rate * time, spinAxis) * Quaternion.Euler(rot);

            matrices[i] = Matrix4x4.TRS(pos, q, scale);

            // flashTime is a timestamp, not a level. The idNorm term staggers the
            // onset so the flash sweeps the field instead of blinking flat.
            float since = time - flashTime - idNorm * p.flashRipple;
            flash[i] = since < 0f ? 0f : Mathf.Exp(-since * decay) * p.flashIntensity;
        }

        return count;
    }

    /// <summary>
    /// Local-space bounds covering the domain plus the largest reach any instance
    /// can add. Instanced draws are culled against this one volume, so a bounds
    /// that is too small pops the whole field out at glancing angles - and one
    /// that is too large can never be culled at all, which is what cost this
    /// project 3.6x in MeshSurfaceScatter.
    /// </summary>
    public static Bounds LocalBounds(in BackdropParameters p)
    {
        float reach = p.offsetJitter.magnitude
                    + Mathf.Abs(p.waveAmplitude)
                    + Mathf.Max(p.scaleRange.y, 0f) * Mathf.Max(p.scaleAxisBias.x,
                          Mathf.Max(p.scaleAxisBias.y, p.scaleAxisBias.z));

        return new Bounds(Vector3.zero, p.domainSize + Vector3.one * (reach * 2f));
    }
}
