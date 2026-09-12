using NUnit.Framework;
using UnityEngine;

public class BackdropParametersTests
{
    [Test]
    public void Default_ProducesAVisibleField()
    {
        var p = BackdropParameters.Default;

        Assert.Greater(p.spawnCount, 0, "a default backdrop with no instances renders nothing");
        Assert.LessOrEqual(p.spawnCount, BackdropParameters.MaxSpawnCount);
        Assert.Greater(p.domainSize.x, 0f);
        Assert.Greater(p.domainSize.y, 0f);
        Assert.Greater(p.domainSize.z, 0f);
        Assert.Greater(p.scaleRange.y, 0f, "max scale of 0 makes every instance invisible");
        Assert.LessOrEqual(p.scaleRange.x, p.scaleRange.y);
    }

    [Test]
    public void Clamped_PullsSpawnCountUnderTheCeiling()
    {
        var p = BackdropParameters.Default;
        p.spawnCount = BackdropParameters.MaxSpawnCount + 5000;

        Assert.AreEqual(BackdropParameters.MaxSpawnCount, p.Clamped().spawnCount);
    }

    [Test]
    public void Clamped_OrdersAnInvertedScaleRange()
    {
        var p = BackdropParameters.Default;
        p.scaleRange = new Vector2(3f, 1f);

        var c = p.Clamped();

        Assert.AreEqual(1f, c.scaleRange.x, 1e-5f);
        Assert.AreEqual(3f, c.scaleRange.y, 1e-5f);
    }

    [Test]
    public void Clamped_OrdersAnInvertedSpinRange()
    {
        var p = BackdropParameters.Default;
        p.spinRateRange = new Vector2(90f, -90f);

        var c = p.Clamped();

        Assert.AreEqual(-90f, c.spinRateRange.x, 1e-5f);
        Assert.AreEqual(90f, c.spinRateRange.y, 1e-5f);
    }

    [Test]
    public void Clamped_RejectsNegativeSpawnCount()
    {
        var p = BackdropParameters.Default;
        p.spawnCount = -10;

        Assert.AreEqual(0, p.Clamped().spawnCount);
    }
}