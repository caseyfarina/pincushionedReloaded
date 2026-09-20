using UnityEngine;

/// <summary>
/// Where every point on a cable sits. A pure closed-form function of
/// (t, seed, time) with no Unity object references and nothing integrated frame
/// to frame, so a cable can be asserted in tests rather than judged by eye, and
/// a rebuild reproduces a composition exactly.
///
/// There is no solver. The project wants the appearance of gravity, not
/// accurate rope physics - see the design doc for why a Verlet chain was
/// considered and dropped.
/// </summary>
public static class CableCurve
{
    /// <summary>
    /// Integer hash, matching BackdropLattice.Hash. Deterministic across
    /// machines and Unity versions, unlike UnityEngine.Random.
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

    /// <summary>
    /// Forces noise to zero at t=0 and t=1, welding the cable to its source and
    /// its plug while leaving the middle free to wander.
    ///
    /// This is the single most important function in the system. Without it the
    /// ends jitter, the cable detaches from the connector, and the illusion
    /// collapses immediately.
    /// </summary>
    public static float Window(float t) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));

    /// <summary>One coherent noise channel in [-1,1], offset per stream so the three axes differ.</summary>
    private static float Noise1(float x, uint seed, uint stream)
    {
        float off = Rand01(0, seed, stream) * 997f;
        return Mathf.PerlinNoise(x + off, off) * 2f - 1f;
    }

    /// <summary>
    /// A point on the cable. t runs 0 at anchorA to 1 at anchorB.
    ///
    /// Three terms: the chord, a parabolic droop toward world down, and noise
    /// windowed to zero at both ends. 4t(1-t) is zero at the anchors and 1 at
    /// the midpoint - a true catenary differs by a fraction of a percent at
    /// realistic droop and costs a cosh.
    /// </summary>
    public static Vector3 Position(
        float t, Vector3 anchorA, Vector3 anchorB,
        float slack, float noiseAmp, float noiseScale,
        float drift, uint seed)
    {
        t = Mathf.Clamp01(t);

        Vector3 chord = Vector3.Lerp(anchorA, anchorB, t);

        // Sag is toward world down, not perpendicular to the chord: a cable
        // droops toward the floor however its ends are oriented, and a
        // perpendicular sag would swing a near-vertical cable sideways.
        float sag = slack * 4f * t * (1f - t);

        float u = t * noiseScale + drift;
        Vector3 n = new Vector3(
            Noise1(u, seed, 1u),
            Noise1(u, seed, 2u),
            Noise1(u, seed, 3u));

        return chord + Vector3.down * sag + n * (noiseAmp * Window(t));
    }
}
