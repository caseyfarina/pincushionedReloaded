using UnityEngine;

/// <summary>
/// One cable. Everything here is either fixed at creation or derived from age,
/// so a shot carries no simulation state and can be recreated exactly from its
/// id and the parameters it was fired with.
/// </summary>
[System.Serializable]
public struct CableShot
{
    public uint seed;
    public Vector3 source;
    public Vector3 landing;
    public float age;
    public float flightDuration;
    public int colorIndex;
    /// <summary>Index into CableLibrary, or -1 when no connectors are imported.</summary>
    public int meshIndex;

    private const float MinFlight = 1e-3f;
    private const float Eps = 1e-10f;

    /// <summary>
    /// Fire a cable. The landing point is sampled once, seeded, on a sphere
    /// around the target, so a nest converges on one place without every cable
    /// ending identically.
    /// </summary>
    public static CableShot Create(int id, Vector3 source, Vector3 target, in CableParameters p, int meshCount)
    {
        uint s = p.seed;

        // Even distribution in the ball: the cube root is what stops points
        // bunching at the centre, the same correction a spherical cap needs.
        float u1 = CableCurve.Rand01(id, s, 11u);
        float u2 = CableCurve.Rand01(id, s, 12u);
        float u3 = CableCurve.Rand01(id, s, 13u);

        float z = u1 * 2f - 1f;
        float theta = u2 * Mathf.PI * 2f;
        float r = Mathf.Pow(Mathf.Clamp01(u3), 1f / 3f) * Mathf.Max(0f, p.targetScatterRadius);
        float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

        Vector3 offset = new Vector3(ring * Mathf.Cos(theta), ring * Mathf.Sin(theta), z) * r;
        Vector3 landing = target + offset;

        float speed = Mathf.Max(1e-3f, p.cableSpeed);
        int colors = (p.colors != null && p.colors.Length > 0) ? p.colors.Length : 1;

        return new CableShot
        {
            seed = CableCurve.Hash((uint)id * 0x9e3779b9u + s),
            source = source,
            landing = landing,
            age = 0f,
            flightDuration = Mathf.Max(MinFlight, Vector3.Distance(source, landing) / speed),
            colorIndex = Mathf.FloorToInt(CableCurve.Rand01(id, s, 21u) * colors) % colors,
            meshIndex = meshCount > 0
                ? Mathf.FloorToInt(CableCurve.Rand01(id, s, 22u) * meshCount) % meshCount
                : -1,
        };
    }

    /// <summary>1 the instant it is fired, 0 once it has landed. Never negative.</summary>
    public float Flight01 => 1f - Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));

    /// <summary>
    /// Where the far end of the cable is right now. Flight and settle are the
    /// same expression - the cable has "arrived" simply because this stops
    /// moving, which is why there is no handoff and no pop at impact.
    /// </summary>
    public Vector3 HeadAnchor(in CableParameters p)
    {
        float x = Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));
        return Vector3.Lerp(source, landing, CableCurve.Ease(x, p.deceleration));
    }

    /// <summary>
    /// Which way the plug points. While flying it aims along travel; as speed
    /// decays it blends onto the cable's end tangent, which is what makes it
    /// read as seated in a socket rather than frozen mid-flight.
    ///
    /// Every normalise here is guarded. At the arrival instant travel is exactly
    /// zero, and an unguarded normalize yields NaN that propagates into the
    /// head's rotation and blanks the mesh.
    /// </summary>
    public static Vector3 HeadDirection(Vector3 travel, Vector3 endTangent, float flight01, Vector3 fallback)
    {
        Vector3 safeFallback = fallback.sqrMagnitude > Eps ? fallback.normalized : Vector3.forward;
        Vector3 dir = travel.sqrMagnitude > Eps ? travel.normalized : safeFallback;
        Vector3 tan = endTangent.sqrMagnitude > Eps ? endTangent.normalized : dir;

        Vector3 blended = Vector3.Slerp(tan, dir, Mathf.Clamp01(flight01));
        return blended.sqrMagnitude > Eps ? blended.normalized : safeFallback;
    }
}
