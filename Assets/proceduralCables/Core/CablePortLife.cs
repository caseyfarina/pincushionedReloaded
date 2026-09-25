using UnityEngine;

/// <summary>
/// How big a port is right now: it pops into being when discovered, holds while
/// anything needs it, and shrinks away once its last cable has gone.
///
/// A closed-form function of the port's age rather than a tween, because ports
/// are not GameObjects - they are matrices submitted to an instanced draw, so
/// there is nothing for a tween to own. The growth curve is DOTween's OutBack
/// evaluated directly, so the motion is the one that easing gives, overshoot
/// and all, without a tween per port.
/// </summary>
public static class CablePortLife
{
    /// <summary>
    /// DOTween's OutBack easing, constants and all. Overshoots past 1 near the
    /// end and settles back, which is what gives the pop.
    /// </summary>
    public static float OutBack(float t)
    {
        t = Mathf.Clamp01(t);

        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;

        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>Smooth both ways, for the shrink - a port leaving should not overshoot into nothing.</summary>
    public static float SmoothIn(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Scale for a port, 0 when it should not be drawn at all.
    ///
    /// age is how long since it was discovered. sinceFreed is how long since its
    /// last cable left, or negative while something is still plugged in - a port
    /// in use never starts shrinking, however old it is.
    /// </summary>
    public static float Scale01(float age, float sinceFreed, float linger, float grow, float shrink)
    {
        if (age < 0f) return 0f;

        // Dead: its linger ran out.
        if (sinceFreed >= 0f && sinceFreed >= linger) return 0f;

        float rising = grow > 0f ? OutBack(age / grow) : 1f;

        if (sinceFreed < 0f) return Mathf.Max(0f, rising);

        float remaining = linger - sinceFreed;
        float falling = shrink > 0f ? SmoothIn(remaining / shrink) : 1f;

        // A port freed before it finished growing takes whichever is smaller,
        // so a brief port shrinks from wherever it got to rather than jumping
        // up to full size on its way out.
        return Mathf.Max(0f, Mathf.Min(rising, falling));
    }

    /// <summary>Whether a port has outlived its linger and its slot can be reused.</summary>
    public static bool IsDead(float sinceFreed, float linger) =>
        sinceFreed >= 0f && sinceFreed >= linger;
}
