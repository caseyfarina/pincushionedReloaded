using NUnit.Framework;
using UnityEngine;

public class CableShotTests
{
    private static readonly Vector3 Src = new Vector3(0f, 1f, 0f);
    private static readonly Vector3 Tgt = new Vector3(20f, 4f, 5f);

    [Test]
    public void Create_ScattersLandingWithinTheRadius()
    {
        var p = CableParameters.Default;
        p.targetScatterRadius = 2f;

        for (int id = 0; id < 200; id++)
        {
            var s = CableShot.Create(id, Src, Tgt, p, 3);
            Assert.LessOrEqual(Vector3.Distance(s.landing, Tgt), 2f + 1e-4f, $"id {id} landed outside the radius");
        }
    }

    [Test]
    public void Create_ScattersRatherThanStackingOnTheTarget()
    {
        var p = CableParameters.Default;
        p.targetScatterRadius = 2f;

        var a = CableShot.Create(1, Src, Tgt, p, 3);
        var b = CableShot.Create(2, Src, Tgt, p, 3);
        Assert.Greater(Vector3.Distance(a.landing, b.landing), 1e-3f, "two shots landed on the same point");
    }

    [Test]
    public void Create_IsDeterministicForTheSameId()
    {
        var p = CableParameters.Default;
        var a = CableShot.Create(7, Src, Tgt, p, 3);
        var b = CableShot.Create(7, Src, Tgt, p, 3);
        Assert.AreEqual(a.landing, b.landing);
        Assert.AreEqual(a.colorIndex, b.colorIndex);
        Assert.AreEqual(a.meshIndex, b.meshIndex);
    }

    [Test]
    public void Create_PicksIndicesInRange()
    {
        var p = CableParameters.Default;
        for (int id = 0; id < 300; id++)
        {
            var s = CableShot.Create(id, Src, Tgt, p, 3);
            Assert.GreaterOrEqual(s.colorIndex, 0);
            Assert.Less(s.colorIndex, p.colors.Length);
            Assert.GreaterOrEqual(s.meshIndex, 0);
            Assert.Less(s.meshIndex, 3);
        }
    }

    [Test]
    public void Create_SurvivesAnEmptyMeshLibrary()
    {
        // CableLibrary degrades to an empty set when no connectors are imported.
        // The ribbon must still render headless rather than throwing.
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 0);
        Assert.AreEqual(-1, s.meshIndex, "an empty library should yield a sentinel, not an index");
    }

    [Test]
    public void Create_SetsFlightDurationFromDistanceOverSpeed()
    {
        var p = CableParameters.Default;
        p.cableSpeed = 10f;
        p.targetScatterRadius = 0f;

        var s = CableShot.Create(0, Vector3.zero, new Vector3(50f, 0f, 0f), p, 1);
        Assert.AreEqual(5f, s.flightDuration, 1e-3f, "a distant target should take proportionally longer");
    }

    [Test]
    public void Flight01_GoesFromOneToZeroAndStaysThere()
    {
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        s.age = 0f;
        Assert.AreEqual(1f, s.Flight01, 1e-4f);

        s.age = s.flightDuration * 2f;
        Assert.AreEqual(0f, s.Flight01, 1e-4f);

        s.age = s.flightDuration * 100f;
        Assert.AreEqual(0f, s.Flight01, 1e-4f, "Flight01 must clamp, not go negative");
    }

    [Test]
    public void HeadAnchor_ReachesTheLandingPointAndStops()
    {
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        s.age = 0f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.source), 1e-4f, "the head should start at the source");

        s.age = s.flightDuration;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-4f, "the head should arrive at the landing point");

        s.age = s.flightDuration * 10f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-4f, "a landed head must not drift past its plug");
    }

    [Test]
    public void HeadDirection_IsFiniteEvenWithNoTravelAndNoTangent()
    {
        // The NaN this exists to prevent: at the arrival instant travel is zero,
        // and normalising a zero vector yields NaN that propagates into the
        // head's rotation and blanks the mesh.
        var d = CableShot.HeadDirection(Vector3.zero, Vector3.zero, 0f, Vector3.forward);
        Assert.IsFalse(float.IsNaN(d.x) || float.IsNaN(d.y) || float.IsNaN(d.z), "NaN direction");
        Assert.AreEqual(1f, d.magnitude, 1e-4f, "direction must stay unit length");
    }

    [Test]
    public void HeadDirection_FollowsTravelWhileFlying()
    {
        var d = CableShot.HeadDirection(new Vector3(3f, 0f, 0f), Vector3.up, 1f, Vector3.forward);
        Assert.Less(Vector3.Distance(d, Vector3.right), 1e-3f, "a flying plug should point where it is going");
    }

    [Test]
    public void HeadDirection_SettlesOntoTheCableTangent()
    {
        var d = CableShot.HeadDirection(Vector3.zero, new Vector3(0f, 5f, 0f), 0f, Vector3.forward);
        Assert.Less(Vector3.Distance(d, Vector3.up), 1e-3f, "a landed plug should seat along the cable, not freeze mid-flight");
    }

    [Test]
    public void HeadDirection_IsAlwaysUnitLengthAcrossTheBlend()
    {
        for (float f = 0f; f <= 1f; f += 0.05f)
        {
            var d = CableShot.HeadDirection(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), f, Vector3.forward);
            Assert.AreEqual(1f, d.magnitude, 1e-3f, $"flight01 {f}");
        }
    }
}
