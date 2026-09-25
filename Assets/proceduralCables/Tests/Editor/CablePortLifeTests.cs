using NUnit.Framework;
using UnityEngine;

public class CablePortLifeTests
{
    private const float Eps = 1e-4f;

    // linger 1s, grow 0.25s, shrink 0.25s
    private static float S(float age, float sinceFreed) =>
        CablePortLife.Scale01(age, sinceFreed, 1f, 0.25f, 0.25f);

    [Test]
    public void OutBack_RunsFromZeroToOne()
    {
        Assert.AreEqual(0f, CablePortLife.OutBack(0f), Eps);
        Assert.AreEqual(1f, CablePortLife.OutBack(1f), Eps);
    }

    [Test]
    public void OutBack_OvershootsPastOne()
    {
        // The overshoot is the whole point - it is what makes a port pop rather
        // than fade up. Without it this is just a lerp.
        float peak = 0f;
        for (float t = 0f; t <= 1f; t += 0.01f) peak = Mathf.Max(peak, CablePortLife.OutBack(t));
        Assert.Greater(peak, 1.05f, "no overshoot, so no pop");
    }

    [Test]
    public void Scale01_StartsAtNothing()
    {
        Assert.AreEqual(0f, S(0f, -1f), Eps, "a port appeared at full size");
    }

    [Test]
    public void Scale01_ReachesFullSizeAfterGrowing()
    {
        Assert.AreEqual(1f, S(0.25f, -1f), Eps);
        Assert.AreEqual(1f, S(5f, -1f), Eps, "an old but occupied port should be full size");
    }

    [Test]
    public void Scale01_HoldsWhileSomethingIsPluggedIn()
    {
        // sinceFreed negative means still in use, however long it has existed.
        for (float age = 0.25f; age < 30f; age += 3f)
            Assert.AreEqual(1f, S(age, -1f), Eps, $"age {age}");
    }

    [Test]
    public void Scale01_ShrinksAwayOnceItsCableHasGone()
    {
        Assert.AreEqual(1f, S(10f, 0.1f), Eps, "started shrinking before the linger was up");
        Assert.Less(S(10f, 0.9f), 0.5f, "had not begun shrinking near the end of its linger");
        Assert.AreEqual(0f, S(10f, 1f), Eps, "still drawn after its linger expired");
        Assert.AreEqual(0f, S(10f, 4f), Eps, "still drawn long after");
    }

    [Test]
    public void Scale01_NeverGoesNegative()
    {
        for (float freed = 0f; freed < 2f; freed += 0.05f)
            Assert.GreaterOrEqual(S(10f, freed), 0f, $"freed {freed}");
    }

    [Test]
    public void Scale01_APortFreedWhileStillGrowingDoesNotJumpToFullSize()
    {
        // Fired at and abandoned almost immediately: it should leave from
        // roughly where it got to, not snap up to full size on the way out.
        float young = CablePortLife.Scale01(0.05f, 0.95f, 1f, 0.25f, 0.25f);
        Assert.Less(young, 0.6f, "a barely-grown port snapped to full size as it died");
    }

    [Test]
    public void IsDead_OnlyOnceTheLingerHasRunOut()
    {
        Assert.IsFalse(CablePortLife.IsDead(-1f, 1f), "a port in use is not dead");
        Assert.IsFalse(CablePortLife.IsDead(0.5f, 1f), "died during its linger");
        Assert.IsTrue(CablePortLife.IsDead(1f, 1f));
        Assert.IsTrue(CablePortLife.IsDead(9f, 1f));
    }
}
