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

    private static float Frac(float v) => v - Mathf.Floor(v);

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
    /// <summary>
    /// Overload kept for the domains that need no extra parameters. Region
    /// shapes route through the <see cref="BackdropParameters"/> overload.
    /// </summary>
    public static Vector3 Position(int id, int count, BackdropDomain domain, Vector3 size, bool solidFill)
        => Position(id, count, domain, size, solidFill, BackdropParameters.Default);

    public static Vector3 Position(int id, int count, BackdropDomain domain, Vector3 size, bool solidFill,
                                   in BackdropParameters p)
    {
        count = Mathf.Max(count, 1);
        float t = (float)id / count;

        switch (domain)
        {
            case BackdropDomain.Arch:
            {
                // Annulus in the screen plane. Radius goes through a square root
                // so area is covered evenly - lerping the radius directly piles
                // instances up at the inner rim, where the circumference is
                // smallest.
                float rin = Mathf.Clamp01(p.archInnerRadius);
                float r = Mathf.Sqrt(Mathf.Lerp(rin * rin, 1f, t));
                float theta = id * 2.399963229728653f;

                // Depth is stratified by the golden ratio conjugate rather than
                // randomly, so the shell stays evenly filled at any count.
                float z = (Frac(id * 0.6180339887f) - 0.5f) * size.z;

                return new Vector3(Mathf.Cos(theta) * r * size.x * 0.5f,
                                   Mathf.Sin(theta) * r * size.y * 0.5f,
                                   z);
            }

            case BackdropDomain.Colonnade:
            {
                int cols = Mathf.Clamp(p.colonnadeCount, 1, 24);
                int perCol = Mathf.Max(1, Mathf.CeilToInt((float)count / cols));

                int col = Mathf.Min(id / perCol, cols - 1);
                int k = id - col * perCol;

                // Each band is its own little box, filled by the same even-spacing
                // rule the cube uses, so a band reads as a slab rather than a line.
                float slotW = size.x / cols;
                float bandW = slotW * Mathf.Clamp(p.colonnadeWidth, 0.05f, 1f);
                var bandSize = new Vector3(bandW, size.y, size.z);

                Vector3 n3 = AxisCounts(perCol, bandSize);
                int nx = (int)n3.x, ny = (int)n3.y, nz = (int)n3.z;

                int ix = k % nx;
                int iy = (k / nx) % ny;
                int iz = (k / (nx * ny)) % nz;

                var local = new Vector3(
                    nx > 1 ? ((float)ix / (nx - 1) - 0.5f) * bandW : 0f,
                    ny > 1 ? ((float)iy / (ny - 1) - 0.5f) * size.y : 0f,
                    nz > 1 ? ((float)iz / (nz - 1) - 0.5f) * size.z : 0f);

                float centre = (col + 0.5f) / cols - 0.5f;
                local.x += centre * size.x;
                return local;
            }

            case BackdropDomain.Skyline:
            {
                // A ground grid whose heights come from a noise field. Instances
                // are lifted rather than stacked, so with the Y scale bias up
                // they read as towers of differing height without the count
                // having to vary per column.
                Vector2 n2 = SheetCounts(count, size.x, Mathf.Max(size.z, 0.001f));
                int nx = (int)n2.x, nz = (int)n2.y;

                int ix = id % nx;
                int iz = (id / nx) % nz;

                float u = nx > 1 ? (float)ix / (nx - 1) - 0.5f : 0f;
                float w = nz > 1 ? (float)iz / (nz - 1) - 0.5f : 0f;

                float h = Noise3(u * size.x * p.skylineRoughness, 0f,
                                 w * size.z * p.skylineRoughness, p.layoutSeed);
                float rise = h * Mathf.Clamp01(p.skylineHeight);

                // Measured from the floor up, or the ceiling down when inverted.
                float y = p.skylineInverted
                    ? size.y * 0.5f - rise * size.y
                    : -size.y * 0.5f + rise * size.y;

                return new Vector3(u * size.x, y, w * size.z);
            }

            case BackdropDomain.Corridor:
            {
                // The cube shell without its front and back faces, so the frame
                // is bounded left, right, top and bottom while the centre stays
                // open all the way through. Every edge becomes a perspective line.
                float perim = 2f * (size.x + size.y);
                Vector2 n2 = SheetCounts(count, perim, Mathf.Max(size.z, 0.001f));
                int np = Mathf.Max((int)n2.x, 4), nz = (int)n2.y;

                int ip = id % np;
                int iz = (id / np) % nz;

                float d = (float)ip / np * perim;   // walk the rectangle
                float hx = size.x * 0.5f, hy = size.y * 0.5f;

                Vector3 xy;
                if (d < size.x)                          xy = new Vector3(-hx + d, -hy, 0f);
                else if (d < size.x + size.y)            xy = new Vector3(hx, -hy + (d - size.x), 0f);
                else if (d < 2f * size.x + size.y)       xy = new Vector3(hx - (d - size.x - size.y), hy, 0f);
                else                                     xy = new Vector3(-hx, hy - (d - 2f * size.x - size.y), 0f);

                xy.z = nz > 1 ? ((float)iz / (nz - 1) - 0.5f) * size.z : 0f;
                return xy;
            }
        }

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
                //
                // The pole runs along +Z, not +Y, so all three domains share one
                // convention: XY is the screen plane and Z is depth away from the
                // camera. With the rig facing the camera that points the dome at
                // the viewer and fills the frame, where a +Y pole would present
                // the dome edge-on and waste most of the width.
                float z = 1f - t;
                float r = Mathf.Sqrt(Mathf.Clamp01(1f - z * z));
                float theta = id * 2.399963229728653f;
                return Vector3.Scale(
                    new Vector3(Mathf.Cos(theta) * r, Mathf.Sin(theta) * r, z),
                    size * 0.5f);
            }

            default:
            {
                // Resolution per axis is proportional to that axis's length, so
                // spacing is even in world units. A shared N would put instances
                // 20 apart across a wide field and 2 apart through a shallow one
                // - which is what a camera-fitted domain always produces, since
                // width tracks the frame while depth stays authored.
                Vector3 n3 = AxisCounts(count, size);
                int nx = (int)n3.x, ny = (int)n3.y, nz = (int)n3.z;

                int ix = id % nx;
                int iy = (id / nx) % ny;
                int iz = (id / (nx * ny)) % nz;

                var u = new Vector3(
                    nx > 1 ? (float)ix / (nx - 1) - 0.5f : 0f,
                    ny > 1 ? (float)iy / (ny - 1) - 0.5f : 0f,
                    nz > 1 ? (float)iz / (nz - 1) - 0.5f : 0f);

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
    /// <summary>
    /// Whether the lattice slot at <paramref name="id"/> carries an instance.
    ///
    /// Evaluated per slot against a fixed lattice, so lowering occupancy removes
    /// instances without moving the ones that stay - the same property the pin
    /// scatter relies on, where changing the count adds and removes from a
    /// stable ordering rather than reshuffling.
    /// </summary>
    public static bool IsOccupied(int id, int slotCount, in BackdropParameters p)
    {
        float occ = p.occupancy * DriftFactor(id, slotCount, p);

        if (occ >= 1f) return true;
        if (occ <= 0f) return false;

        if (p.occupancyNoiseScale <= 0f)
            return Rand01(id, p.layoutSeed, 977u) < occ;

        // Clustered: threshold a noise field sampled at the slot's own position,
        // so gaps group into voids and instances into clumps. Even speckle reads
        // as damage; clusters read as structure.
        Vector3 at = Position(id, slotCount, p.domain, p.domainSize, p.solidFill, p) * p.occupancyNoiseScale;
        float n = Noise3(at.x, at.y, at.z, p.layoutSeed);

        // Nudged by a per-slot value so the threshold edge is ragged rather than
        // a clean contour, which otherwise reads as a machined cut.
        n = n * 0.85f + Rand01(id, p.layoutSeed, 613u) * 0.15f;
        return n < occ;
    }

    /// <summary>
    /// Density multiplier for one slot under the drift gradient: 1 at the near
    /// end of driftDirection, falling to (1 - driftAmount) at the far end.
    ///
    /// A gradient rather than a cut, because a hard edge draws a line across the
    /// composition while a fade weights it. The projection is normalised against
    /// the domain so the falloff spans the field whatever its size.
    /// </summary>
    public static float DriftFactor(int id, int slotCount, in BackdropParameters p)
    {
        if (p.driftAmount <= 0f || p.driftDirection.sqrMagnitude < 1e-6f) return 1f;

        Vector3 pos = Position(id, slotCount, p.domain, p.domainSize, p.solidFill, p);
        Vector3 half = p.domainSize * 0.5f;

        // Normalised into -1..1 per axis first, so an ultrawide field does not
        // make the horizontal gradient shallower than the vertical one.
        var unit = new Vector3(
            half.x > 1e-4f ? pos.x / half.x : 0f,
            half.y > 1e-4f ? pos.y / half.y : 0f,
            half.z > 1e-4f ? pos.z / half.z : 0f);

        float along = Vector3.Dot(unit, p.driftDirection.normalized);
        return Mathf.Lerp(1f, 1f - p.driftAmount, Mathf.Clamp01(along * 0.5f + 0.5f));
    }

    /// <summary>
    /// Base orientation for an instance from the shape it sits on, mapping local
    /// +Y to the outward direction. +Y because a Y scale bias is the common case
    /// - towers - so aligned towers point out of the surface rather than lying
    /// along it.
    ///
    /// Only the curved and shelled domains have an orientation worth deriving.
    /// The flat and box-like ones return identity, which reads as axis-aligned.
    /// </summary>
    public static Quaternion ShapeAlignment(int id, int count, in BackdropParameters p)
    {
        Vector3 pos = Position(id, count, p.domain, p.domainSize, p.solidFill, p);
        Vector3 outward;

        switch (p.domain)
        {
            case BackdropDomain.Arch:
                // Radial in the screen plane: spokes around the opening.
                outward = new Vector3(pos.x, pos.y, 0f);
                break;

            case BackdropDomain.Hemisphere:
                outward = pos;
                break;

            case BackdropDomain.Cube:
            case BackdropDomain.Corridor:
            {
                // Whichever face this instance is nearest to.
                Vector3 half = p.domainSize * 0.5f;
                float dx = half.x > 1e-4f ? Mathf.Abs(pos.x) / half.x : 0f;
                float dy = half.y > 1e-4f ? Mathf.Abs(pos.y) / half.y : 0f;
                float dz = half.z > 1e-4f ? Mathf.Abs(pos.z) / half.z : 0f;

                if (dx >= dy && dx >= dz)      outward = new Vector3(Mathf.Sign(pos.x), 0f, 0f);
                else if (dy >= dx && dy >= dz) outward = new Vector3(0f, Mathf.Sign(pos.y), 0f);
                else                           outward = new Vector3(0f, 0f, Mathf.Sign(pos.z));
                break;
            }

            default:
                return Quaternion.identity;
        }

        if (outward.sqrMagnitude < 1e-8f) return Quaternion.identity;
        return Quaternion.FromToRotation(Vector3.up, outward.normalized);
    }

    /// <summary>
    /// Fills <paramref name="dst"/> with a seeded selection of distinct model
    /// indices, and returns how many were written.
    ///
    /// Partial Fisher-Yates over a scratch permutation: distinct by construction,
    /// where rejection sampling would stall as the subset approaches the library
    /// size. Caller-owned buffers so this can run on a parameter change without
    /// allocating.
    /// </summary>
    public static int BuildModelSubset(int[] dst, int[] scratch, int modelCount, int subsetSize, uint seed)
    {
        if (modelCount <= 0 || dst == null || dst.Length == 0) return 0;

        int want = Mathf.Clamp(subsetSize, 1, Mathf.Min(modelCount, dst.Length));

        int n = Mathf.Min(modelCount, scratch.Length);
        for (int i = 0; i < n; i++) scratch[i] = i;

        for (int i = 0; i < want; i++)
        {
            // Draw from the untouched tail, then swap the pick into place.
            int j = i + (int)(Rand01(i, seed, 811u) * (n - i));
            if (j >= n) j = n - 1;
            (scratch[i], scratch[j]) = (scratch[j], scratch[i]);
            dst[i] = scratch[i];
        }

        return want;
    }

    /// <summary>
    /// Which position in the word an instance takes.
    ///
    /// On a colonnade the letter advances per column rather than per instance,
    /// so each band is one letter and the word reads across the frame - which is
    /// the arrangement worth having. Every other domain advances per instance,
    /// which tiles the word through the field.
    /// </summary>
    public static int WordIndexForInstance(int id, int count, in BackdropParameters p, int wordLength)
    {
        if (wordLength <= 0) return 0;

        if (p.domain == BackdropDomain.Colonnade)
        {
            int cols = Mathf.Clamp(p.colonnadeCount, 1, 24);
            int perCol = Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(count, 1) / cols));
            return (id / perCol) % wordLength;
        }

        return id % wordLength;
    }

    /// <summary>
    /// Which model an instance draws, as an index into <paramref name="subset"/>.
    ///
    /// Its own hash stream, so the assignment holds still while position, scale
    /// and rotation are dialled - a letter that jumped to a different glyph every
    /// time the scale moved would be unusable.
    /// </summary>
    public static int ModelForInstance(int id, uint seed, int[] subset, int subsetCount)
    {
        if (subset == null || subsetCount <= 0) return 0;
        if (subsetCount == 1) return subset[0];

        int k = (int)(Rand01(id, seed, 929u) * subsetCount);
        if (k >= subsetCount) k = subsetCount - 1;
        return subset[k];
    }

    /// <summary>
    /// Scale multiplier for one instance: 1 for most, or accentRatio raised to a
    /// step for the accented minority.
    ///
    /// A field drawn from one continuous size range has no hierarchy - every
    /// instance reads as the same object at a different distance, and the eye
    /// has nothing to measure against. Promoting a minority to a discrete size
    /// class gives it landmarks. At the default 1.618 each class stands to the
    /// next as consecutive Fibonacci terms do, which is why the accents read as
    /// belonging to the field rather than as a handful of oversized strays.
    ///
    /// Drawn from its own hash stream, so the accents stay put when the base
    /// scale range is dialled.
    /// </summary>
    public static float AccentMultiplier(int id, in BackdropParameters p)
    {
        if (p.accentFraction <= 0f) return 1f;

        float roll = Rand01(id, p.layoutSeed, 401u);
        if (roll >= p.accentFraction) return 1f;

        // Higher steps are progressively rarer, so the series thins as it climbs
        // the way a real skyline does - many mid-rise, a few towers, one spire.
        int steps = 1;
        for (int k = 1; k < p.accentSteps; k++)
        {
            if (Rand01(id, p.layoutSeed, (uint)(431 + k * 7)) < 0.35f) steps++;
            else break;
        }

        return Mathf.Pow(p.accentRatio, steps);
    }

    /// <summary>
    /// 0-1 value noise in three dimensions, built from Unity's 2D Perlin across
    /// three planes. Cheap, deterministic, and adequate for drift - this is a
    /// displacement field, not a texture.
    /// </summary>
    public static float Noise3(float x, float y, float z, uint seed)
    {
        float o = (seed % 1000u) * 0.137f;
        float a = Mathf.PerlinNoise(x + o, y + o);
        float b = Mathf.PerlinNoise(y + o + 31.416f, z + o);
        float c = Mathf.PerlinNoise(z + o + 78.233f, x + o);
        return (a + b + c) / 3f;
    }

    public static int Fill(in BackdropParameters p, float time, float flashTime,
                           Matrix4x4[] matrices, float[] flash)
    {
        int count = Mathf.Clamp(p.spawnCount, 0, Mathf.Min(matrices.Length, flash.Length));
        if (count == 0) return 0;

        Vector3 waveAxis = p.waveAxis.sqrMagnitude < 1e-6f ? Vector3.up : p.waveAxis.normalized;
        Vector3 spinAxis = p.spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : p.spinAxis.normalized;

        float waveW = p.waveFrequency * 2f * Mathf.PI;
        float decay = Mathf.Max(p.flashDecay, 0.01f);

        // `write` trails `i` once slots start being skipped: the lattice is
        // indexed by slot so positions never shift, while the buffers stay
        // densely packed for the instanced draw.
        int write = 0;

        for (int i = 0; i < count; i++)
        {
            if (!IsOccupied(i, count, p)) continue;

            float idNorm = (float)i / count;

            Vector3 pos = Position(i, count, p.domain, p.domainSize, p.solidFill, p);
            Vector3 jitter = Rand3(i, p.layoutSeed, 11u) * 2f - Vector3.one;
            pos += Vector3.Scale(jitter, p.offsetJitter);

            // Absolute offset from the lattice point, never an accumulating add -
            // accumulating walks every instance off its lattice point permanently.
            if (p.motion == BackdropMotion.Noise)
            {
                // Sampled at the instance's own position, so neighbours move
                // together and the field drifts as a body rather than each
                // instance bobbing on its own clock.
                float t = time * p.waveFrequency;
                Vector3 at = pos * p.noiseScale;
                var n = new Vector3(
                    Noise3(at.x + t, at.y, at.z, p.layoutSeed) - 0.5f,
                    Noise3(at.x, at.y + t, at.z, p.layoutSeed + 17u) - 0.5f,
                    Noise3(at.x, at.y, at.z + t, p.layoutSeed + 41u) - 0.5f);
                pos += n * (2f * p.waveAmplitude);
            }
            else
            {
                float phase = idNorm * p.wavePhaseSpread * 2f * Mathf.PI;
                pos += waveAxis * (Mathf.Sin(time * waveW + phase) * p.waveAmplitude);
            }

            float s = Mathf.Lerp(p.scaleRange.x, p.scaleRange.y, Rand01(i, p.layoutSeed, 31u));
            s *= AccentMultiplier(i, p);
            Vector3 scale = p.scaleAxisBias * s;

            Vector3 rot = Vector3.Scale(
                Rand3(i, p.layoutSeed, 53u) * 2f - Vector3.one,
                p.rotationJitter);

            float rate = Mathf.Lerp(p.spinRateRange.x, p.spinRateRange.y, Rand01(i, p.layoutSeed, 71u));

            // Shape alignment is the base orientation, jitter perturbs it, spin
            // rides on top. Jitter applied before alignment would rotate the
            // instance out of the surface it is supposed to be sitting on.
            Quaternion align = p.alignToShape ? ShapeAlignment(i, count, p) : Quaternion.identity;
            Quaternion q = Quaternion.AngleAxis(rate * time, spinAxis) * align * Quaternion.Euler(rot);

            matrices[write] = Matrix4x4.TRS(pos, q, scale);

            // flashTime is a timestamp, not a level. The idNorm term staggers the
            // onset so the flash sweeps the field instead of blinking flat. It is
            // keyed to the slot, not the write index, so the sweep keeps its
            // direction and speed however many slots are skipped.
            float since = time - flashTime - idNorm * p.flashRipple;
            flash[write] = since < 0f ? 0f : Mathf.Exp(-since * decay) * p.flashIntensity;

            write++;
        }

        return write;
    }

    /// <summary>
    /// Resolution for a two-dimensional sheet: counts proportional to the two
    /// extents, with the product landing at or just above <paramref name="count"/>.
    ///
    /// Separate from AxisCounts because that one divides a volume, and feeding
    /// it a degenerate third axis collapses the second to 1 - which silently
    /// flattens a sheet into a single row.
    /// </summary>
    public static Vector2 SheetCounts(int count, float extentA, float extentB)
    {
        count = Mathf.Max(count, 1);
        float a = Mathf.Max(extentA, 1e-4f);
        float b = Mathf.Max(extentB, 1e-4f);

        int na = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count * a / b)));
        int nb = Mathf.Max(1, Mathf.CeilToInt((float)count / na));

        while ((long)na * nb < count) { if (a / na >= b / nb) na++; else nb++; }
        return new Vector2(na, nb);
    }

    /// <summary>
    /// Per-axis lattice resolution for a box, proportional to each axis's length
    /// so world-space spacing is even, with the product landing near
    /// <paramref name="count"/>.
    ///
    /// Returned as a Vector3 of whole numbers rather than three out parameters
    /// purely so this stays easy to assert in a test.
    /// </summary>
    public static Vector3 AxisCounts(int count, Vector3 size)
    {
        count = Mathf.Max(count, 1);

        // A zero-length axis still needs one plane of instances, not zero.
        float sx = Mathf.Max(size.x, 1e-3f);
        float sy = Mathf.Max(size.y, 1e-3f);
        float sz = Mathf.Max(size.z, 1e-3f);

        // Spacing s that would put `count` instances in the volume at even
        // density; the per-axis resolution is then that axis divided by s.
        float spacing = Mathf.Pow(sx * sy * sz / count, 1f / 3f);
        if (spacing < 1e-5f) spacing = 1e-5f;

        int nx = Mathf.Max(1, Mathf.RoundToInt(sx / spacing));
        int ny = Mathf.Max(1, Mathf.RoundToInt(sy / spacing));
        int nz = Mathf.Max(1, Mathf.RoundToInt(sz / spacing));

        // Rounding can leave the product short of count, which would make the
        // index wrap and stack instances on top of each other. Grow the longest
        // axis until there is room for every one.
        while ((long)nx * ny * nz < count)
        {
            if (sx / nx >= sy / ny && sx / nx >= sz / nz) nx++;
            else if (sy / ny >= sz / nz) ny++;
            else nz++;
        }

        return new Vector3(nx, ny, nz);
    }

    /// <summary>
    /// The width and height that exactly fill a camera's frame at a given
    /// distance, before margin. Perspective only - for an orthographic camera
    /// the frame does not grow with distance, so height is just twice the size.
    ///
    /// Kept here rather than in the MonoBehaviour so the framing can be asserted
    /// in tests: "a 32:9 camera produces a field 3.55x wider than it is tall" is
    /// a fact worth pinning down, not something to re-check by eye every time
    /// the aspect changes.
    /// </summary>
    public static Vector2 FrameSizeAt(float verticalFovDeg, float aspect, float distance, bool orthographic, float orthoSize)
    {
        if (orthographic)
        {
            float h = Mathf.Max(orthoSize, 0.001f) * 2f;
            return new Vector2(h * Mathf.Max(aspect, 0.001f), h);
        }

        float halfH = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(verticalFovDeg, 1f, 179f) * 0.5f) * Mathf.Max(distance, 0.001f);
        return new Vector2(halfH * 2f * Mathf.Max(aspect, 0.001f), halfH * 2f);
    }

    /// <summary>
    /// Returns <paramref name="p"/> with domainSize X and Y replaced by the
    /// camera's frame at fitDistance, times fitMargin. Z is left alone - depth is
    /// an authored quality, not something the frame can imply.
    ///
    /// The fit is measured at the far face rather than the near one. A
    /// perspective frustum widens with distance, so fitting at the near face
    /// leaves the back of the field short of the frame edge and the corners
    /// visibly empty.
    /// </summary>
    public static BackdropParameters FitToFrame(BackdropParameters p, float verticalFovDeg, float aspect,
                                                bool orthographic, float orthoSize)
    {
        if (!p.fitToCamera) return p;

        float far = p.fitDistance + Mathf.Max(p.domainSize.z, 0f);
        var frame = FrameSizeAt(verticalFovDeg, aspect, far, orthographic, orthoSize);

        p.domainSize = new Vector3(
            frame.x * p.fitMargin.x,
            frame.y * p.fitMargin.y,
            p.domainSize.z);

        return p;
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
        // Accented instances are the largest thing in the field, so the bounds
        // have to assume one sits on an edge - otherwise the biggest objects are
        // exactly the ones that pop out at a glancing angle.
        float accent = p.accentFraction > 0f ? Mathf.Pow(Mathf.Max(p.accentRatio, 1f), p.accentSteps) : 1f;

        float reach = p.offsetJitter.magnitude
                    + Mathf.Abs(p.waveAmplitude)
                    + Mathf.Max(p.scaleRange.y, 0f) * accent * Mathf.Max(p.scaleAxisBias.x,
                          Mathf.Max(p.scaleAxisBias.y, p.scaleAxisBias.z));

        return new Bounds(Vector3.zero, p.domainSize + Vector3.one * (reach * 2f));
    }
}
