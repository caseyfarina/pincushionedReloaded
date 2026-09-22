using UnityEngine;

/// <summary>
/// Where cables plug in: a grid of ports laid out in the target's local space.
///
/// Local space is the whole point. Every port is an offset from the target
/// transform, so moving, rotating or scaling that one transform carries the
/// entire bay with it - there is no second thing to keep in sync, and no world
/// coordinates baked into a cable at the moment it was fired.
///
/// The grid sits in the target's XY plane because plugs seat along its Z, so
/// the target's blue axis is the direction cables plug in along.
/// </summary>
public static class CablePatchBay
{
    public static int PortCount(int columns, int rows) =>
        (columns <= 0 || rows <= 0) ? 0 : columns * rows;

    /// <summary>
    /// A port's position in the target's local space. Index runs across each
    /// row before wrapping to the next, and row 0 is the top.
    ///
    /// Centred on the origin so the target sits in the middle of its bay -
    /// anchoring at a corner would make rotating the target swing the bay
    /// around a hinge instead of turning it in place.
    /// </summary>
    public static Vector3 PortLocal(int index, int columns, int rows, float columnSpacing, float rowSpacing)
    {
        if (columns <= 0 || rows <= 0) return Vector3.zero;

        index = Mathf.Clamp(index, 0, columns * rows - 1);
        int col = index % columns;
        int row = index / columns;

        return new Vector3(
            (col - (columns - 1) * 0.5f) * columnSpacing,
            ((rows - 1) * 0.5f - row) * rowSpacing,
            0f);
    }

    /// <summary>
    /// Choose a port for a new cable, preferring one nothing is plugged into.
    ///
    /// Free ports are filled before any port is doubled up, so the bay reads as
    /// filling rather than piling. Once every port is taken - which happens when
    /// cables are fired faster than they retire - it shares one instead of
    /// refusing the cable, because dropping a shot mid-performance is worse than
    /// two plugs in one socket.
    ///
    /// The pick is seeded rather than lowest-index-first, or an emptying bay
    /// would refill from the top-left corner every time.
    /// </summary>
    public static int PickPort(int id, uint seed, int portCount, bool[] occupied)
    {
        if (portCount <= 0) return -1;

        int free = 0;
        if (occupied != null)
            for (int i = 0; i < portCount && i < occupied.Length; i++)
                if (!occupied[i]) free++;

        if (occupied == null || free == 0)
            return Mathf.Min(portCount - 1, Mathf.FloorToInt(CableCurve.Rand01(id, seed, 41u) * portCount));

        // Walk to the nth free port, so every free port is equally likely
        // without rejection-sampling against a nearly full bay.
        int nth = Mathf.Min(free - 1, Mathf.FloorToInt(CableCurve.Rand01(id, seed, 42u) * free));
        for (int i = 0; i < portCount && i < occupied.Length; i++)
        {
            if (occupied[i]) continue;
            if (nth == 0) return i;
            nth--;
        }

        return 0;
    }
}
