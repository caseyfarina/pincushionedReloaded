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
    /// <summary>Which flash colour this cable strikes with. Drawn separately from colorIndex so the flash does not echo the cable.</summary>
    public int flashColorIndex;
    /// <summary>Multiple of CableParameters.thickness this cable draws at.</summary>
    public float widthScale;
    /// <summary>Index into CableLibrary, or -1 when no connectors are imported.</summary>
    public int meshIndex;

    /// <summary>Which patch bay port this cable is plugged into, or -1 for none.</summary>
    public int portIndex;

    /// <summary>
    /// The landing point in the TARGET's local space. landing is re-resolved
    /// from this every frame, which is what lets one transform carry the whole
    /// bay: move or rotate the target and every plugged cable follows, rather
    /// than staying at the world point it was fired at.
    /// </summary>
    public Vector3 landingLocal;

    /// <summary>
    /// World direction the plug travels as it seats - the target's Z, refreshed
    /// each frame by the instrument. A world vector rather than a Transform,
    /// because Core must stay free of Unity object types to stay testable.
    /// </summary>
    public Vector3 insertAxis;

    /// <summary>
    /// Where this cable left from, in the EMITTER's local space. source stays
    /// the frozen launch point - a cable in flight travels from where it was
    /// fired - while this is re-resolved to keep the trailing end attached to
    /// an emitter that has since moved.
    /// </summary>
    public Vector3 sourceLocal;

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
        int flashes = (p.flashColors != null && p.flashColors.Length > 0) ? p.flashColors.Length : 0;

        return new CableShot
        {
            seed = CableCurve.Hash((uint)id * 0x9e3779b9u + s),
            source = source,
            landing = landing,
            age = 0f,
            flightDuration = Mathf.Max(MinFlight, Vector3.Distance(source, landing) / speed),
            colorIndex = Mathf.FloorToInt(CableCurve.Rand01(id, s, 21u) * colors) % colors,
            widthScale = Mathf.Lerp(1f, Mathf.Max(1f, p.thicknessVariation), CableCurve.Rand01(id, s, 23u)),
            flashColorIndex = flashes > 0
                ? Mathf.FloorToInt(CableCurve.Rand01(id, s, 24u) * flashes) % flashes
                : 0,
            meshIndex = meshCount > 0
                ? Mathf.FloorToInt(CableCurve.Rand01(id, s, 22u) * meshCount) % meshCount
                : -1,
        };
    }

    /// <summary>Seconds since this cable landed. Negative while it is still in flight.</summary>
    public float SettleAge => age - flightDuration;

    /// <summary>1 the instant it is fired, 0 once it has landed. Never negative.</summary>
    public float Flight01 => 1f - Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));

    /// <summary>
    /// Where the far end of the cable is right now. Flight and settle are the
    /// same expression - the cable has "arrived" simply because this stops
    /// moving, which is why there is no handoff and no pop at impact.
    /// </summary>
    public Vector3 HeadAnchor(in CableParameters p) => AnchorAtFraction(Progress, p);

    /// <summary>How far through its flight this cable is, 0 to 1. Clamps once it has landed.</summary>
    public float Progress => Mathf.Clamp01(age / Mathf.Max(MinFlight, flightDuration));

    /// <summary>Where the head is, or was, at a given fraction of its flight.</summary>
    private Vector3 AnchorAtFraction(float x, in CableParameters p)
    {
        x = Mathf.Clamp01(x);

        float k = p.insertion01;
        if (k > 0f && p.insertionDepth > 0f)
        {
            k = Mathf.Clamp(k, 1e-4f, 0.5f);
            Vector3 standoff = landing - SeatAxis * p.insertionDepth;

            // The last stretch is a straight push along the port axis, so the
            // plug enters nose-first instead of sliding in sideways off the end
            // of its curve.
            if (x >= 1f - k)
                return Vector3.Lerp(standoff, landing, (x - (1f - k)) / k);

            // The approach is the ordinary curve, re-aimed to finish lined up
            // in front of the port. Detour's lobes are zero at the end of the
            // approach, so it arrives on the standoff exactly and there is no
            // seam at the handover.
            return Curved(x / (1f - k), source, standoff, p);
        }

        return Curved(x, source, landing, p);
    }

    private Vector3 Curved(float x, Vector3 from, Vector3 to, in CableParameters p)
    {
        Vector3 straight = Vector3.Lerp(from, to, CableCurve.Ease(x, p.deceleration));
        if (p.pathCurl <= 0f) return straight;
        return straight + Detour(x, p.pathCurl);
    }

    /// <summary>The seating direction, falling back to the chord if none was supplied.</summary>
    private Vector3 SeatAxis
    {
        get
        {
            if (insertAxis.sqrMagnitude > Eps) return insertAxis.normalized;
            Vector3 chord = landing - source;   // the swerve plane, from the shot's overall run
            return chord.sqrMagnitude > Eps ? chord.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// A point along the cable's body, t running 0 at the source to 1 at the head.
    ///
    /// The body is the head's own history: the point at t is where the head was
    /// when it was t of the way through the flight it has completed so far. So
    /// the cable lies along the route it actually travelled instead of snapping
    /// taut behind the head, and a landed cable keeps that route forever rather
    /// than collapsing onto the chord.
    ///
    /// This needs no history buffer because the head's path is a pure function
    /// of its flight fraction - the trail is simply that function, resampled.
    /// </summary>
    public Vector3 PathPoint(float t, in CableParameters p) =>
        AnchorAtFraction(Progress * Mathf.Clamp01(t), p);

    /// <summary>
    /// How far off the straight line the head is at normalised time x.
    ///
    /// Two lobes across the flight - one half-cycle and one full - on two axes
    /// perpendicular to the chord, which gives a swerving arc rather than a
    /// single bulge. Both lobes are sine terms that are exactly zero at x=0 and
    /// x=1, so however large the curl, the cable still leaves its source and
    /// arrives at its plug precisely. That is the same windowing trick that
    /// welds the cable's ends, for the same reason.
    /// </summary>
    private Vector3 Detour(float x, float curl)
    {
        Vector3 chord = landing - source;
        Vector3 dir = chord.sqrMagnitude > Eps ? chord.normalized : Vector3.forward;

        // A seeded arbitrary vector, made perpendicular to the chord. Picking a
        // fixed world axis instead would make every cable in a horizontal run
        // swerve through the same plane.
        Vector3 rough = new Vector3(
            CableCurve.Rand01((int)seed, seed, 31u) * 2f - 1f,
            CableCurve.Rand01((int)seed, seed, 32u) * 2f - 1f,
            CableCurve.Rand01((int)seed, seed, 33u) * 2f - 1f);

        Vector3 perpA = Vector3.ProjectOnPlane(rough, dir);
        if (perpA.sqrMagnitude <= Eps) perpA = Vector3.ProjectOnPlane(Vector3.up, dir);
        if (perpA.sqrMagnitude <= Eps) perpA = Vector3.ProjectOnPlane(Vector3.right, dir);
        perpA = perpA.normalized;

        Vector3 perpB = Vector3.Cross(dir, perpA);

        // Three sine lobes, each exactly zero at x=0 and x=1, so however wide
        // the swerve the cable still leaves its source and reaches its plug.
        // The third lobe is what turns a single bulge into a serpentine route.
        float lobe1 = Mathf.Sin(Mathf.PI * x);
        float lobe2 = Mathf.Sin(2f * Mathf.PI * x);
        float lobe3 = Mathf.Sin(3f * Mathf.PI * x);

        // Signed so the swerve is not always the same handedness.
        float swing = CableCurve.Rand01((int)seed, seed, 34u) * 2f - 1f;
        float twist = CableCurve.Rand01((int)seed, seed, 35u) * 2f - 1f;

        return (perpA * (lobe1 + lobe3 * twist * 0.7f)
              + perpB * (lobe2 * swing + lobe3 * 0.5f)) * curl;
    }

    /// <summary>
    /// Whether this cable has outlived its settled life and should retire.
    ///
    /// Measured from landing, not from firing: the lifetime the performer sets
    /// means "how long it hangs there", and a distant target must not have its
    /// hang time eaten by a longer flight.
    ///
    /// Zero or less means unlimited, which is the default - so adding a
    /// lifetime cannot silently start retiring cables in existing scenes.
    /// </summary>
    public bool IsExpired(float lifetime) =>
        lifetime > 0f && age > flightDuration + lifetime;

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
