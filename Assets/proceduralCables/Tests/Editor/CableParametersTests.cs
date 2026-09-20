using NUnit.Framework;
using UnityEngine;

public class CableParametersTests
{
    [Test]
    public void Default_HasSixColors()
    {
        var p = CableParameters.Default;
        Assert.IsNotNull(p.colors);
        Assert.AreEqual(6, p.colors.Length);
    }

    [Test]
    public void Default_HasUsableResolutionAndCap()
    {
        var p = CableParameters.Default;
        Assert.GreaterOrEqual(p.nodesPerCable, 2, "a cable needs at least two nodes to form one segment");
        Assert.Greater(p.cableCap, 0);
        Assert.Greater(p.cableSpeed, 0f, "flight duration is distance / cableSpeed and must not divide by zero");
    }

    [Test]
    public void Default_ColorsAreDistinct()
    {
        var p = CableParameters.Default;
        for (int i = 0; i < p.colors.Length; i++)
            for (int j = i + 1; j < p.colors.Length; j++)
                Assert.AreNotEqual(p.colors[i], p.colors[j], $"colors {i} and {j} are identical");
    }
}
