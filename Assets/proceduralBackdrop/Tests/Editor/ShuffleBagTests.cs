using System.Collections.Generic;
using NUnit.Framework;

public class ShuffleBagTests
{
    [Test]
    public void Next_NeverRepeatsTheSameValueTwiceInARow()
    {
        var bag = new ShuffleBag(3, seed: 12345);

        int previous = bag.Next();
        for (int i = 0; i < 500; i++)
        {
            int next = bag.Next();
            Assert.AreNotEqual(previous, next,
                $"repeat at draw {i}: a repeated value is a pad press that does nothing visible");
            previous = next;
        }
    }

    [Test]
    public void Next_VisitsEveryValueWithinTwoPasses()
    {
        var bag = new ShuffleBag(5, seed: 7);
        var seen = new HashSet<int>();

        for (int i = 0; i < 10; i++) seen.Add(bag.Next());

        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, seen);
    }

    [Test]
    public void Next_IsReproducibleForAGivenSeed()
    {
        var a = new ShuffleBag(6, seed: 99);
        var b = new ShuffleBag(6, seed: 99);

        for (int i = 0; i < 50; i++)
            Assert.AreEqual(a.Next(), b.Next(), $"divergence at draw {i}");
    }

    [Test]
    public void Next_ReturnsZeroForASingleEntry()
    {
        var bag = new ShuffleBag(1, seed: 1);
        Assert.AreEqual(0, bag.Next());
        Assert.AreEqual(0, bag.Next());
    }

    [Test]
    public void Next_ReturnsMinusOneWhenEmpty()
    {
        var bag = new ShuffleBag(0, seed: 1);
        Assert.AreEqual(-1, bag.Next());
    }

    [Test]
    public void Resize_KeepsDrawingValidIndices()
    {
        var bag = new ShuffleBag(3, seed: 4);
        bag.Next();
        bag.Resize(8);

        for (int i = 0; i < 40; i++)
        {
            int v = bag.Next();
            Assert.GreaterOrEqual(v, 0);
            Assert.Less(v, 8);
        }
    }
}