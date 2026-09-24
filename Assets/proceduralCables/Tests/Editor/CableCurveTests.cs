using NUnit.Framework;
using UnityEngine;

public class CableCurveTests
{
    private static readonly Vector3 A = new Vector3(-4f, 2f, 1f);
    private static readonly Vector3 B = new Vector3(6f, 3f, -2f);

    // Mathf.Sin(Mathf.PI) is 8.7e-8, not 0, so the far anchor lands within
    // float epsilon rather than exactly. Anything larger than this is a bug.
    private const float Eps = 1e-4f;

    [Test]
    public void Window_IsZeroAtBothEnds()
    {
        Assert.AreEqual(0f, CableCurve.Window(0f), Eps);
        Assert.AreEqual(0f, CableCurve.Window(1f), Eps);
        Assert.AreEqual(1f, CableCurve.Window(0.5f), Eps);
    }

    [Test]
    public void Position_EndsStayPinned_ForAnySeedTimeAndNoise()
    {
        // The load-bearing assertion. If noise leaks past the window, every
        // cable detaches from its source and its plug.
        for (uint seed = 0; seed < 25; seed++)
        {
            for (float time = 0f; time < 6f; time += 1.3f)
            {
                var a = CableCurve.Position(0f, A, B, 5f, 10f, 3f, time, seed);
                var b = CableCurve.Position(1f, A, B, 5f, 10f, 3f, time, seed);
                Assert.Less(Vector3.Distance(a, A), Eps, $"near end drifted: seed {seed} t {time}");
                Assert.Less(Vector3.Distance(b, B), Eps, $"far end drifted: seed {seed} t {time}");
            }
        }
    }

    [Test]
    public void Position_IsDeterministicForTheSameInputs()
    {
        var x = CableCurve.Position(0.37f, A, B, 2f, 1f, 3f, 4.2f, 9u);
        var y = CableCurve.Position(0.37f, A, B, 2f, 1f, 3f, 4.2f, 9u);
        Assert.AreEqual(x, y);
    }

    [Test]
    public void Position_DiffersAcrossSeeds()
    {
        int same = 0;
        for (int i = 0; i < 200; i++)
        {
            float t = (i + 1) / 202f;
            var x = CableCurve.Position(t, A, B, 2f, 1f, 3f, 0f, 1u);
            var y = CableCurve.Position(t, A, B, 2f, 1f, 3f, 0f, 2u);
            if (Vector3.Distance(x, y) < 1e-5f) same++;
        }
        Assert.Less(same, 20, "two seeds produced near-identical cables");
    }

    [Test]
    public void Position_DriftMovesTheCableButNotItsEnds()
    {
        var mid0 = CableCurve.Position(0.5f, A, B, 0f, 1f, 3f, 0f, 5u);
        var mid1 = CableCurve.Position(0.5f, A, B, 0f, 1f, 3f, 9f, 5u);
        Assert.Greater(Vector3.Distance(mid0, mid1), 1e-3f, "drift did not move the middle");

        var end0 = CableCurve.Position(1f, A, B, 0f, 1f, 3f, 0f, 5u);
        var end1 = CableCurve.Position(1f, A, B, 0f, 1f, 3f, 9f, 5u);
        Assert.Less(Vector3.Distance(end0, end1), Eps, "drift moved the far end");
    }

    [Test]
    public void Position_SagsTowardWorldDownAtTheMidpoint()
    {
        // No noise, so sag is the only term acting on the midpoint.
        var chord = Vector3.Lerp(A, B, 0.5f);
        var mid = CableCurve.Position(0.5f, A, B, 3f, 0f, 3f, 0f, 1u);
        Assert.AreEqual(chord.y - 3f, mid.y, Eps, "midpoint should hang exactly slack below the chord");
        Assert.AreEqual(chord.x, mid.x, Eps, "sag must not move the cable sideways");
        Assert.AreEqual(chord.z, mid.z, Eps, "sag must not move the cable in depth");
    }

    [Test]
    public void Position_SagIsSymmetricAndPeaksInTheMiddle()
    {
        float DropAt(float t)
        {
            var chord = Vector3.Lerp(A, B, t);
            return chord.y - CableCurve.Position(t, A, B, 3f, 0f, 3f, 0f, 1u).y;
        }
        Assert.AreEqual(DropAt(0.25f), DropAt(0.75f), Eps, "sag is not symmetric");
        Assert.Greater(DropAt(0.5f), DropAt(0.25f), "sag does not peak in the middle");
        Assert.Greater(DropAt(0.25f), DropAt(0.05f), "sag is not monotonic on the way up");
    }

    [Test]
    public void Ease_IsLinearAtZeroDeceleration()
    {
        for (float x = 0f; x <= 1f; x += 0.1f)
            Assert.AreEqual(x, CableCurve.Ease(x, 0f), Eps, $"x {x}");
    }

    [Test]
    public void Ease_SpansZeroToOneAndIsMonotonic()
    {
        foreach (float d in new[] { 0f, 1.5f, 6f })
        {
            Assert.AreEqual(0f, CableCurve.Ease(0f, d), Eps, $"deceleration {d}");
            Assert.AreEqual(1f, CableCurve.Ease(1f, d), Eps, $"deceleration {d}");

            float prev = -1f;
            for (float x = 0f; x <= 1f; x += 0.05f)
            {
                float v = CableCurve.Ease(x, d);
                Assert.GreaterOrEqual(v, prev, $"not monotonic at x {x}, deceleration {d}");
                prev = v;
            }
        }
    }

    [Test]
    public void Ease_DeceleratesMeaningTheHeadIsAlreadyPastHalfwayAtHalfTime()
    {
        Assert.Greater(CableCurve.Ease(0.5f, 3f), 0.5f,
            "a decelerating flight covers more than half the distance in the first half of the time");
    }

    [Test]
    public void Shiver_StartsAtZeroAndDecaysAway()
    {
        Assert.AreEqual(0f, CableCurve.Shiver(0f, 3.5f, 14f), Eps, "no shiver before any time has passed");
        Assert.Greater(Mathf.Abs(CableCurve.Shiver(0.1f, 3.5f, 14f)), 1e-3f, "shiver never started");
        Assert.Less(Mathf.Abs(CableCurve.Shiver(8f, 3.5f, 14f)), 1e-3f, "shiver never died away");
    }

    [Test]
    public void Shiver_IsFiniteForPathologicalInputs()
    {
        foreach (float decay in new[] { 0f, 1e6f })
            foreach (float freq in new[] { 0f, 1e6f })
            {
                float v = CableCurve.Shiver(2f, decay, freq);
                Assert.IsFalse(float.IsNaN(v), $"NaN at decay {decay} freq {freq}");
                Assert.IsFalse(float.IsInfinity(v), $"Inf at decay {decay} freq {freq}");
            }
    }

    [Test]
    public void Offset_IsZeroAtBothEnds()
    {
        // The sag-and-noise decoration, separated from the spine so the spine
        // can be a traced path instead of a chord.
        Assert.Less(CableCurve.Offset(0f, 5f, 10f, 3f, 2f, 7u).magnitude, Eps);
        Assert.Less(CableCurve.Offset(1f, 5f, 10f, 3f, 2f, 7u).magnitude, Eps);
    }

    [Test]
    public void Offset_SagsStraightDownAndPeaksInTheMiddle()
    {
        var mid = CableCurve.Offset(0.5f, 3f, 0f, 3f, 0f, 1u);
        Assert.AreEqual(-3f, mid.y, Eps);
        Assert.AreEqual(0f, mid.x, Eps);
        Assert.AreEqual(0f, mid.z, Eps);
        Assert.Greater(-CableCurve.Offset(0.5f, 3f, 0f, 3f, 0f, 1u).y,
                       -CableCurve.Offset(0.25f, 3f, 0f, 3f, 0f, 1u).y);
    }

    [Test]
    public void Position_IsStillTheChordPlusTheOffset()
    {
        // Guards the refactor: Position must keep meaning exactly what it did.
        for (float t = 0f; t <= 1f; t += 0.125f)
        {
            var expected = Vector3.Lerp(A, B, t) + CableCurve.Offset(t, 2f, 1f, 3f, 0.5f, 4u);
            Assert.Less(Vector3.Distance(CableCurve.Position(t, A, B, 2f, 1f, 3f, 0.5f, 4u), expected), 1e-5f, $"t {t}");
        }
    }

    [Test]
    public void DecorationFade_IsOneUntilInsertionBeginsThenReachesZero()
    {
        // Sag and noise have to be gone before the straight push starts, or the
        // "straight" insertion visibly wobbles.
        Assert.AreEqual(1f, CableCurve.DecorationFade(0.5f, 0.05f), Eps);
        Assert.AreEqual(1f, CableCurve.DecorationFade(0.95f, 0.05f), Eps, "faded early");
        Assert.AreEqual(0f, CableCurve.DecorationFade(1f, 0.05f), Eps, "still decorating at the plug");
        Assert.Less(CableCurve.DecorationFade(0.99f, 0.05f), 0.5f, "did not fade across the insertion window");
    }

    [Test]
    public void DecorationFade_IsOneEverywhereWhenInsertionIsOff()
    {
        for (float t = 0f; t <= 1f; t += 0.25f)
            Assert.AreEqual(1f, CableCurve.DecorationFade(t, 0f), Eps, $"t {t}");
    }

    [Test]
    public void Flash01_IsDarkUntilTheMomentOfContact()
    {
        Assert.AreEqual(0f, CableCurve.Flash01(-1f, 6f), Eps, "lit before it had landed");
        Assert.AreEqual(1f, CableCurve.Flash01(0f, 6f), Eps, "no flash at the instant of contact");
    }

    [Test]
    public void Flash01_DecaysAwayToNothing()
    {
        Assert.Less(CableCurve.Flash01(2f, 6f), 0.01f, "still lit long after landing");
        Assert.Greater(CableCurve.Flash01(0.05f, 6f), 0.5f, "died out too fast to be seen");
    }

    [Test]
    public void Flash01_FallsMonotonically()
    {
        float prev = 2f;
        for (float t = 0f; t < 3f; t += 0.05f)
        {
            float v = CableCurve.Flash01(t, 6f);
            Assert.LessOrEqual(v, prev + 1e-5f, $"brightened again at {t}");
            prev = v;
        }
    }

    [Test]
    public void Flash01_StaysInRangeForPathologicalDecay()
    {
        foreach (float decay in new[] { 0f, -5f, 1e6f })
            foreach (float t in new[] { 0f, 0.5f, 50f })
            {
                float v = CableCurve.Flash01(t, decay);
                Assert.IsFalse(float.IsNaN(v), $"NaN at decay {decay} t {t}");
                Assert.GreaterOrEqual(v, 0f, $"negative at decay {decay} t {t}");
                Assert.LessOrEqual(v, 1f, $"over one at decay {decay} t {t}");
            }
    }
}
