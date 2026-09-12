using NUnit.Framework;

public class HistoryRingTests
{
    [Test]
    public void TryBack_WalksToThePreviousEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        Assert.IsTrue(ring.TryBack(out int a));
        Assert.AreEqual(2, a);
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(1, b);
    }

    [Test]
    public void TryBack_StopsAtTheOldestEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2);

        Assert.IsTrue(ring.TryBack(out _));
        Assert.IsFalse(ring.TryBack(out _), "walking past the oldest entry must not wrap");
    }

    [Test]
    public void TryForward_ReturnsToTheNewerEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        ring.TryBack(out _);
        ring.TryBack(out _);

        Assert.IsTrue(ring.TryForward(out int f));
        Assert.AreEqual(2, f);
    }

    [Test]
    public void TryForward_StopsAtTheNewestEntry()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2);

        Assert.IsFalse(ring.TryForward(out _));
    }

    [Test]
    public void Push_AfterWalkingBackTruncatesTheRedoBranch()
    {
        var ring = new HistoryRing<int>(20);
        ring.Push(1); ring.Push(2); ring.Push(3);

        ring.TryBack(out _);       // now at 2
        ring.Push(99);             // new branch from 2

        Assert.IsFalse(ring.TryForward(out _), "3 was on the abandoned branch");
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(2, b);
    }

    [Test]
    public void Push_DropsTheOldestWhenAtCapacity()
    {
        var ring = new HistoryRing<int>(3);
        ring.Push(1); ring.Push(2); ring.Push(3); ring.Push(4);

        Assert.AreEqual(3, ring.Count);

        Assert.IsTrue(ring.TryBack(out int a));
        Assert.AreEqual(3, a);
        Assert.IsTrue(ring.TryBack(out int b));
        Assert.AreEqual(2, b);
        Assert.IsFalse(ring.TryBack(out _), "1 was evicted");
    }

    [Test]
    public void TryBack_OnAnEmptyRingReportsFailure()
    {
        var ring = new HistoryRing<int>(5);
        Assert.IsFalse(ring.TryBack(out _));
    }
}