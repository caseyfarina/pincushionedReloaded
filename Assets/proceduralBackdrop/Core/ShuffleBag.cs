using System;
using System.Collections.Generic;

/// <summary>
/// Draws indices in a shuffled order that visits every value once per pass and
/// never repeats across a pass boundary.
///
/// This exists because uniform random is the wrong feel for a performance pad.
/// With three shading modes, Random.Range repeats about a third of the time,
/// and a press that produces no visible change reads as a dead button. A
/// shuffle bag guarantees motion on every press without feeling sequential.
///
/// System.Random with an explicit seed, per this project's convention:
/// UnityEngine.Random is a global shared stream anything else can perturb, so
/// compositions built on it are not reproducible.
/// </summary>
public class ShuffleBag
{
    private readonly List<int> order = new List<int>();
    private System.Random rng;
    private int count;
    private int cursor;
    private int lastDrawn = -1;

    public int Count => count;

    public ShuffleBag(int count, int seed)
    {
        rng = new System.Random(seed);
        Resize(count);
    }

    /// <summary>Changes the value range, e.g. when the mesh library is rescanned.</summary>
    public void Resize(int newCount)
    {
        count = Math.Max(0, newCount);
        Reshuffle();
    }

    public int Next()
    {
        if (count == 0) return -1;
        if (count == 1) return 0;

        if (cursor >= order.Count) Reshuffle();

        int value = order[cursor++];
        lastDrawn = value;
        return value;
    }

    private void Reshuffle()
    {
        order.Clear();
        for (int i = 0; i < count; i++) order.Add(i);

        // Fisher-Yates.
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // Guard the pass boundary: without this, a pass ending in 2 followed by
        // a pass starting with 2 produces exactly the dead press this class
        // exists to prevent.
        if (count > 1 && order.Count > 1 && order[0] == lastDrawn)
            (order[0], order[order.Count - 1]) = (order[order.Count - 1], order[0]);

        cursor = 0;
    }
}