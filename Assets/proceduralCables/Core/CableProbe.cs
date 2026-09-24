using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths behind finding ports on real surfaces: which way to probe, and
/// whether what was hit is worth plugging into.
///
/// Separated from the raycasting so the parts that decide can be tested. The
/// raycast itself needs a physics scene and is left to the MonoBehaviour.
/// </summary>
public static class CableProbe
{
    /// <summary>
    /// A direction to probe in, biased toward the room rather than the ceiling.
    ///
    /// Height is sampled between straight down and upLimit, so most rays sweep
    /// the floor and walls. A room has few surfaces above head height worth
    /// patching into, and an unbiased sphere wastes most of its rays up there.
    ///
    /// Sampling height uniformly rather than the polar angle is what keeps the
    /// remaining directions even - the same correction the origin sphere and
    /// the split-screen rig both need.
    /// </summary>
    public static Vector3 Direction(int id, int attempt, uint seed, float upLimit)
    {
        upLimit = Mathf.Clamp(upLimit, -1f, 1f);

        // Attempt folded into the stream, so retrying a failed probe looks
        // somewhere new rather than re-casting the same ray.
        int k = id * 97 + attempt;

        float y = Mathf.Lerp(-1f, upLimit, CableCurve.Rand01(k, seed, 61u));
        float a = CableCurve.Rand01(k, seed, 62u) * Mathf.PI * 2f;
        float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));

        return new Vector3(ring * Mathf.Cos(a), y, ring * Mathf.Sin(a));
    }

    /// <summary>
    /// Whether a surface hit is worth plugging into.
    ///
    /// The facing test rejects grazing hits, where a plug would sit almost flat
    /// against the surface and read as clipping through it rather than seated
    /// in it.
    /// </summary>
    public static bool Accepts(
        Vector3 source, Vector3 point, Vector3 surfaceNormal, Vector3 rayDir,
        float searchRadius, float minFacing)
    {
        if (Vector3.Distance(source, point) > searchRadius) return false;

        if (surfaceNormal.sqrMagnitude < 1e-10f || rayDir.sqrMagnitude < 1e-10f) return false;

        return Vector3.Dot(surfaceNormal.normalized, -rayDir.normalized) >= minFacing;
    }

    /// <summary>
    /// Whether a candidate port is far enough from the ports already found.
    /// Without this, probes cluster onto whatever large flat surface is nearest
    /// and the ports pile into one patch of floor.
    /// </summary>
    public static bool IsClearOf(Vector3 candidate, IReadOnlyList<Vector3> existing, float minSpacing)
    {
        if (existing == null || minSpacing <= 0f) return true;

        float sqr = minSpacing * minSpacing;
        for (int i = 0; i < existing.Count; i++)
            if ((existing[i] - candidate).sqrMagnitude < sqr) return false;

        return true;
    }
}
