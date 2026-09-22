using NUnit.Framework;
using UnityEngine;

public class CablePatchBayTests
{
    private const float Eps = 1e-4f;

    [Test]
    public void PortCount_IsColumnsTimesRows()
    {
        Assert.AreEqual(30, CablePatchBay.PortCount(10, 3));
        Assert.AreEqual(0, CablePatchBay.PortCount(0, 3), "no columns is no bay, not a crash");
        Assert.AreEqual(0, CablePatchBay.PortCount(10, -2));
    }

    [Test]
    public void PortLocal_CentersTheGridOnTheTargetOrigin()
    {
        // So the target transform sits in the middle of the bay rather than at
        // a corner - otherwise rotating the target swings the bay around a hinge.
        var sum = Vector3.zero;
        int n = CablePatchBay.PortCount(10, 3);
        for (int i = 0; i < n; i++) sum += CablePatchBay.PortLocal(i, 10, 3, 1.5f, 1.2f);

        Assert.Less((sum / n).magnitude, Eps, "the grid is not centred on the origin");
    }

    [Test]
    public void PortLocal_LiesInTheTargetsXYPlane()
    {
        // Plugs seat along the target's Z, so the bay face must be its XY plane.
        for (int i = 0; i < 30; i++)
            Assert.AreEqual(0f, CablePatchBay.PortLocal(i, 10, 3, 1.5f, 1.2f).z, Eps, $"port {i}");
    }

    [Test]
    public void PortLocal_SpacesColumnsAndRowsEvenly()
    {
        var p0 = CablePatchBay.PortLocal(0, 10, 3, 1.5f, 1.2f);
        var p1 = CablePatchBay.PortLocal(1, 10, 3, 1.5f, 1.2f);
        Assert.AreEqual(1.5f, p1.x - p0.x, Eps, "column spacing wrong");
        Assert.AreEqual(0f, p1.y - p0.y, Eps, "index 1 should be the next column, not the next row");

        var pNextRow = CablePatchBay.PortLocal(10, 10, 3, 1.5f, 1.2f);
        Assert.AreEqual(1.2f, p0.y - pNextRow.y, Eps, "row spacing wrong, or rows do not run downward");
        Assert.AreEqual(p0.x, pNextRow.x, Eps, "a new row should start back at the first column");
    }

    [Test]
    public void PortLocal_FillsAcrossColumnsBeforeWrapping()
    {
        var last = CablePatchBay.PortLocal(9, 10, 3, 1.5f, 1.2f);
        var first = CablePatchBay.PortLocal(0, 10, 3, 1.5f, 1.2f);
        Assert.AreEqual(9 * 1.5f, last.x - first.x, Eps);
        Assert.AreEqual(first.y, last.y, Eps, "the first ten ports should be one row");
    }

    [Test]
    public void PickPort_TakesAFreePortWhenOneExists()
    {
        var occupied = new bool[30];
        for (int i = 0; i < 30; i++) occupied[i] = true;
        occupied[17] = false;

        Assert.AreEqual(17, CablePatchBay.PickPort(0, 1u, 30, occupied), "the one free port was not chosen");
    }

    [Test]
    public void PickPort_NeverDoublesUpWhileAnyPortIsFree()
    {
        // The rule the performer will notice: cables fill the bay before they
        // start stacking.
        var occupied = new bool[30];
        for (int id = 0; id < 30; id++)
        {
            int port = CablePatchBay.PickPort(id, 7u, 30, occupied);
            Assert.IsFalse(occupied[port], $"id {id} landed on an occupied port with free ports remaining");
            occupied[port] = true;
        }

        foreach (var o in occupied) Assert.IsTrue(o, "30 cables did not fill 30 ports exactly");
    }

    [Test]
    public void PickPort_SharesAPortOnceTheBayIsFull()
    {
        var occupied = new bool[30];
        for (int i = 0; i < 30; i++) occupied[i] = true;

        for (int id = 0; id < 20; id++)
        {
            int port = CablePatchBay.PickPort(id, 3u, 30, occupied);
            Assert.GreaterOrEqual(port, 0, "a full bay must still accept a cable, not reject it");
            Assert.Less(port, 30);
        }
    }

    [Test]
    public void PickPort_SpreadsAcrossTheFreePorts()
    {
        // Always taking the lowest free index would pile every cable into the
        // top-left corner of the bay.
        var occupied = new bool[30];
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int id = 0; id < 12; id++) seen.Add(CablePatchBay.PickPort(id, 5u, 30, occupied));

        Assert.Greater(seen.Count, 6, "picks clustered instead of spreading over an empty bay");
    }

    [Test]
    public void PickPort_IsDeterministicForTheSameIdAndSeed()
    {
        var occupied = new bool[30];
        Assert.AreEqual(CablePatchBay.PickPort(9, 4u, 30, occupied),
                        CablePatchBay.PickPort(9, 4u, 30, occupied));
    }

    [Test]
    public void PickPort_ReturnsMinusOneWhenThereIsNoBay()
    {
        Assert.AreEqual(-1, CablePatchBay.PickPort(0, 1u, 0, new bool[0]),
            "no ports should be a sentinel, not an index");
    }
}
