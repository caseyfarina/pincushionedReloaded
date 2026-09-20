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
}
