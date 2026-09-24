using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CableProbeTests
{
    [Test]
    public void Direction_IsAlwaysUnitLength()
    {
        for (int i = 0; i < 300; i++)
            Assert.AreEqual(1f, CableProbe.Direction(i, 0, 1u, 0.3f).magnitude, 1e-3f, $"id {i}");
    }

    [Test]
    public void Direction_StaysUnderTheUpwardLimit()
    {
        // A room has little worth patching into overhead, so rays should not be
        // spent up there.
        for (int i = 0; i < 400; i++)
            Assert.LessOrEqual(CableProbe.Direction(i, 0, 1u, 0.3f).y, 0.3f + 1e-4f, $"id {i}");
    }

    [Test]
    public void Direction_MostlyLooksDownAndSideways()
    {
        int low = 0, n = 500;
        for (int i = 0; i < n; i++)
            if (CableProbe.Direction(i, 0, 1u, 0.3f).y < 0f) low++;

        Assert.Greater(low / (float)n, 0.6f, "not enough rays aimed at the floor and walls");
    }

    [Test]
    public void Direction_SpreadsAroundRatherThanFavouringOneSide()
    {
        int posX = 0, n = 500;
        for (int i = 0; i < n; i++)
            if (CableProbe.Direction(i, 0, 1u, 0.3f).x > 0f) posX++;

        float frac = posX / (float)n;
        Assert.Greater(frac, 0.4f, $"lopsided ({frac:F2})");
        Assert.Less(frac, 0.6f, $"lopsided ({frac:F2})");
    }

    [Test]
    public void Direction_IsDeterministicButDiffersPerAttempt()
    {
        Assert.AreEqual(CableProbe.Direction(5, 2, 3u, 0.3f), CableProbe.Direction(5, 2, 3u, 0.3f));
        Assert.AreNotEqual(CableProbe.Direction(5, 2, 3u, 0.3f), CableProbe.Direction(5, 3, 3u, 0.3f),
            "a retry probes the same direction again");
    }

    [Test]
    public void Accepts_RejectsSurfacesBeyondTheSearchRadius()
    {
        var src = Vector3.zero;
        Assert.IsFalse(CableProbe.Accepts(src, new Vector3(0f, 0f, 12f), -Vector3.forward, Vector3.forward, 10f, 0.3f),
            "plugged into something out of reach");
        Assert.IsTrue(CableProbe.Accepts(src, new Vector3(0f, 0f, 8f), -Vector3.forward, Vector3.forward, 10f, 0.3f));
    }

    [Test]
    public void Accepts_RejectsGrazingHits()
    {
        // A surface almost edge-on to the probe would seat a plug flat against
        // it, which reads as clipping rather than plugging in.
        var src = Vector3.zero;
        var point = new Vector3(0f, 0f, 5f);

        Assert.IsFalse(CableProbe.Accepts(src, point, Vector3.up, Vector3.forward, 10f, 0.3f), "took an edge-on surface");
        Assert.IsTrue(CableProbe.Accepts(src, point, -Vector3.forward, Vector3.forward, 10f, 0.3f), "refused a face-on surface");
    }

    [Test]
    public void Accepts_SurvivesDegenerateNormalsAndDirections()
    {
        var src = Vector3.zero;
        Assert.IsFalse(CableProbe.Accepts(src, Vector3.forward, Vector3.zero, Vector3.forward, 10f, 0.3f));
        Assert.IsFalse(CableProbe.Accepts(src, Vector3.forward, Vector3.up, Vector3.zero, 10f, 0.3f));
    }

    [Test]
    public void IsClearOf_KeepsPortsApart()
    {
        var taken = new List<Vector3> { Vector3.zero, new Vector3(5f, 0f, 0f) };

        Assert.IsFalse(CableProbe.IsClearOf(new Vector3(0.5f, 0f, 0f), taken, 2f), "stacked onto an existing port");
        Assert.IsTrue(CableProbe.IsClearOf(new Vector3(2.5f, 0f, 0f), taken, 2f));
        Assert.IsTrue(CableProbe.IsClearOf(new Vector3(0.5f, 0f, 0f), taken, 0f), "spacing of zero should not reject anything");
        Assert.IsTrue(CableProbe.IsClearOf(Vector3.zero, null, 2f), "an empty bay should accept the first port");
    }
}
