using UnityEngine;

/// <summary>Where a cable is fired from, relative to the emitter transform.</summary>
public enum CableOriginShape
{
    /// <summary>Every cable leaves from the emitter itself.</summary>
    Point,
    /// <summary>A columns x rows grid in the emitter's XY plane - a patch bay firing into a patch bay.</summary>
    Grid,
    /// <summary>Anywhere inside a disc in the emitter's XY plane.</summary>
    Disc,
    /// <summary>Anywhere on the surface of a sphere around the emitter.</summary>
    Sphere,
}

/// <summary>
/// Spreads cable origins into a shape around the emitter.
///
/// Offsets are in the emitter's local space, so moving, rotating or scaling
/// that one transform carries the whole field - the same arrangement the patch
/// bay uses for its ports, rather than a second mechanism that drifts from it.
/// </summary>
public static class CableOriginField
{
    /// <summary>A cable's origin, as an offset from the emitter transform.</summary>
    public static Vector3 OriginLocal(int id, uint seed, in CableParameters p)
    {
        switch (p.originShape)
        {
            case CableOriginShape.Grid:
            {
                int cells = CablePatchBay.PortCount(p.originColumns, p.originRows);
                if (cells <= 0) return Vector3.zero;

                // Reuses the bay's own layout, so an origin grid and a port grid
                // are the same arithmetic and cannot drift apart.
                int cell = Mathf.Min(cells - 1, Mathf.FloorToInt(CableCurve.Rand01(id, seed, 51u) * cells));
                return CablePatchBay.PortLocal(cell, p.originColumns, p.originRows,
                                               p.originColumnSpacing, p.originRowSpacing);
            }

            case CableOriginShape.Disc:
            {
                float r = Mathf.Max(0f, p.originRadius);
                float a = CableCurve.Rand01(id, seed, 52u) * Mathf.PI * 2f;

                // sqrt is what makes this uniform by area. Sampling the radius
                // directly piles half the cables into the inner quarter.
                float d = Mathf.Sqrt(Mathf.Clamp01(CableCurve.Rand01(id, seed, 53u))) * r;
                return new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0f);
            }

            case CableOriginShape.Sphere:
            {
                float r = Mathf.Max(0f, p.originRadius);

                // Height uniform, not the polar angle - the same correction the
                // split-screen rig needs, or cables crowd the poles.
                float y = CableCurve.Rand01(id, seed, 54u) * 2f - 1f;
                float a = CableCurve.Rand01(id, seed, 55u) * Mathf.PI * 2f;
                float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));

                return new Vector3(ring * Mathf.Cos(a), y, ring * Mathf.Sin(a)) * r;
            }

            default:
                return Vector3.zero;
        }
    }
}
