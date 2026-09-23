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

    [Test]
    public void IsExpired_IsFalseWhileStillFlying()
    {
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.age = s.flightDuration * 0.5f;
        Assert.IsFalse(s.IsExpired(2f), "a cable in flight has not begun its settled life yet");
    }

    [Test]
    public void IsExpired_CountsFromLandingNotFromFiring()
    {
        // The lifetime the user sets is "seconds after hitting the target".
        // Measuring from firing would cut a slow cable short mid-flight.
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        s.age = s.flightDuration + 1.9f;
        Assert.IsFalse(s.IsExpired(2f), "retired early - lifetime is being measured from firing");

        s.age = s.flightDuration + 2.1f;
        Assert.IsTrue(s.IsExpired(2f), "outlived its lifetime and was not retired");
    }

    [Test]
    public void IsExpired_ZeroLifetimeMeansNeverRetire()
    {
        // The default, so adding this parameter cannot change existing scenes.
        var p = CableParameters.Default;
        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.age = 10000f;
        Assert.IsFalse(s.IsExpired(0f), "zero must mean unlimited, not instant death");
        Assert.IsFalse(s.IsExpired(-5f), "a negative lifetime must not retire everything instantly");
    }

    // ---- per-cable thickness ----

    [Test]
    public void Create_WidthScaleIsOneWhenVariationIsOne()
    {
        // The default, so adding variation cannot resize cables in existing scenes.
        var p = CableParameters.Default;
        p.thicknessVariation = 1f;
        for (int id = 0; id < 50; id++)
            Assert.AreEqual(1f, CableShot.Create(id, Src, Tgt, p, 1).widthScale, 1e-4f, $"id {id}");
    }

    [Test]
    public void Create_WidthScaleStaysInsideOneToVariation()
    {
        var p = CableParameters.Default;
        p.thicknessVariation = 5f;
        for (int id = 0; id < 300; id++)
        {
            float w = CableShot.Create(id, Src, Tgt, p, 1).widthScale;
            Assert.GreaterOrEqual(w, 1f - 1e-4f, $"id {id} thinner than the base thickness");
            Assert.LessOrEqual(w, 5f + 1e-4f, $"id {id} fatter than the configured maximum");
        }
    }

    [Test]
    public void Create_WidthScaleActuallyVariesAndSpansTheRange()
    {
        var p = CableParameters.Default;
        p.thicknessVariation = 5f;

        float min = float.MaxValue, max = float.MinValue;
        for (int id = 0; id < 300; id++)
        {
            float w = CableShot.Create(id, Src, Tgt, p, 1).widthScale;
            min = Mathf.Min(min, w); max = Mathf.Max(max, w);
        }
        Assert.Less(min, 1.6f, "no thin cables were produced");
        Assert.Greater(max, 4.4f, "no fat cables were produced");
    }

    // ---- circuitous flight ----

    [Test]
    public void HeadAnchor_FliesStraightWhenCurlIsZero()
    {
        var p = CableParameters.Default;
        p.pathCurl = 0f;
        p.deceleration = 0f;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        for (float x = 0.1f; x < 1f; x += 0.1f)
        {
            s.age = s.flightDuration * x;
            var straight = Vector3.Lerp(s.source, s.landing, x);
            Assert.Less(Vector3.Distance(s.HeadAnchor(p), straight), 1e-3f, $"x {x} bent with no curl");
        }
    }

    [Test]
    public void HeadAnchor_LeavesTheStraightLineWhenCurled()
    {
        var p = CableParameters.Default;
        p.pathCurl = 6f;
        var s = CableShot.Create(0, Src, Tgt, p, 1);

        float worst = 0f;
        for (float x = 0.1f; x < 1f; x += 0.05f)
        {
            s.age = s.flightDuration * x;
            var straight = Vector3.Lerp(s.source, s.landing, x);
            worst = Mathf.Max(worst, Vector3.Distance(s.HeadAnchor(p), straight));
        }
        Assert.Greater(worst, 1f, "the route never meaningfully left the straight line");
    }

    [Test]
    public void HeadAnchor_StillHitsTheLandingPointExactlyWhenCurled()
    {
        // The whole point of windowing the detour. A curl that does not close
        // means every cable misses its plug.
        var p = CableParameters.Default;
        p.pathCurl = 12f;

        for (int id = 0; id < 40; id++)
        {
            var s = CableShot.Create(id, Src, Tgt, p, 1);

            s.age = 0f;
            Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.source), 1e-3f, $"id {id} did not start at the source");

            s.age = s.flightDuration;
            Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-3f, $"id {id} missed its landing point");

            s.age = s.flightDuration * 5f;
            Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-3f, $"id {id} drifted after landing");
        }
    }

    [Test]
    public void HeadAnchor_CurlsDifferentlyPerCable()
    {
        var p = CableParameters.Default;
        p.pathCurl = 8f;

        var a = CableShot.Create(1, Src, Tgt, p, 1);
        var b = CableShot.Create(2, Src, Tgt, p, 1);
        a.age = a.flightDuration * 0.5f;
        b.age = b.flightDuration * 0.5f;

        // Compare detours from each cable's own straight line, so a different
        // landing point alone cannot make this pass.
        var da = a.HeadAnchor(p) - Vector3.Lerp(a.source, a.landing, 0.5f);
        var db = b.HeadAnchor(p) - Vector3.Lerp(b.source, b.landing, 0.5f);
        Assert.Greater(Vector3.Distance(da, db), 0.5f, "two cables took the same detour");
    }

    // ---- the cable follows the head's path, rather than snapping taut behind it ----

    [Test]
    public void PathPoint_StartsAtTheSourceAndEndsAtTheHead()
    {
        var p = CableParameters.Default;
        p.pathCurl = 10f;
        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.age = s.flightDuration * 0.6f;

        Assert.Less(Vector3.Distance(s.PathPoint(0f, p), s.source), 1e-3f, "the tail left the source");
        Assert.Less(Vector3.Distance(s.PathPoint(1f, p), s.HeadAnchor(p)), 1e-3f, "the near end is not at the head");
    }

    [Test]
    public void PathPoint_TracesWhereTheHeadActuallyWent()
    {
        // The whole claim: the body is the head's own history, so sampling the
        // spine at t must equal where the head was when its flight was t.
        var p = CableParameters.Default;
        p.pathCurl = 10f;

        var landed = CableShot.Create(3, Src, Tgt, p, 1);
        landed.age = landed.flightDuration * 3f;   // long since arrived

        for (float t = 0f; t <= 1f; t += 0.1f)
        {
            var probe = CableShot.Create(3, Src, Tgt, p, 1);
            probe.age = probe.flightDuration * t;
            Assert.Less(Vector3.Distance(landed.PathPoint(t, p), probe.HeadAnchor(p)), 1e-3f,
                $"spine at t {t} is not where the head was");
        }
    }

    [Test]
    public void PathPoint_KeepsTheWindingRouteAfterLanding()
    {
        // The bug this fixes: a landed cable used to collapse onto the straight
        // source-to-landing chord, throwing away the route it flew.
        var p = CableParameters.Default;
        p.pathCurl = 10f;
        var s = CableShot.Create(5, Src, Tgt, p, 1);
        s.age = s.flightDuration * 10f;

        float worst = 0f;
        for (float t = 0.1f; t < 1f; t += 0.05f)
        {
            var chord = Vector3.Lerp(s.source, s.landing, t);
            worst = Mathf.Max(worst, Vector3.Distance(s.PathPoint(t, p), chord));
        }
        Assert.Greater(worst, 1f, "a landed cable snapped back to a straight line");
    }

    [Test]
    public void PathPoint_IsStraightWhenThereIsNoCurl()
    {
        var p = CableParameters.Default;
        p.pathCurl = 0f;
        p.deceleration = 0f;
        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.age = s.flightDuration * 5f;

        for (float t = 0f; t <= 1f; t += 0.1f)
        {
            var chord = Vector3.Lerp(s.source, s.landing, t);
            Assert.Less(Vector3.Distance(s.PathPoint(t, p), chord), 1e-3f, $"t {t} bent with no curl");
        }
    }

    [Test]
    public void PathPoint_GrowsOutOfTheSourceWhileFlying()
    {
        // Early in flight the whole body must be short and near the source,
        // not already stretched to the target.
        var p = CableParameters.Default;
        p.pathCurl = 10f;
        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.age = s.flightDuration * 0.1f;

        float reach = 0f;
        for (float t = 0f; t <= 1f; t += 0.1f)
            reach = Mathf.Max(reach, Vector3.Distance(s.PathPoint(t, p), s.source));

        Assert.Less(reach, Vector3.Distance(s.source, s.landing) * 0.6f,
            "the cable was already most of the way to the target at 10% of its flight");
    }

    // ---- the last stretch is a straight push along the port axis ----

    private static CableShot Inserting(out CableParameters p, float depth = 2f, float k = 0.05f)
    {
        p = CableParameters.Default;
        p.pathCurl = 6f;
        p.insertionDepth = depth;
        p.insertion01 = k;

        var s = CableShot.Create(0, Src, Tgt, p, 1);
        s.insertAxis = Vector3.forward;
        return s;
    }

    [Test]
    public void HeadAnchor_StillLandsExactlyOnTheLandingPointWhenInserting()
    {
        var s = Inserting(out var p);
        s.age = s.flightDuration;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-3f, "insertion overshot or undershot the plug");

        s.age = s.flightDuration * 6f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), s.landing), 1e-3f, "a seated plug drifted");
    }

    [Test]
    public void HeadAnchor_ApproachEndsAtTheStandoffPoint()
    {
        var s = Inserting(out var p, depth: 2f, k: 0.05f);
        s.age = s.flightDuration * 0.95f;

        var standoff = s.landing - Vector3.forward * 2f;
        Assert.Less(Vector3.Distance(s.HeadAnchor(p), standoff), 1e-3f,
            "the approach did not arrive lined up in front of the port");
    }

    [Test]
    public void HeadAnchor_InsertionRunsStraightAlongThePortAxis()
    {
        // The whole point: no sideways slide over the last stretch.
        var s = Inserting(out var p, depth: 2f, k: 0.05f);

        for (float x = 0.95f; x <= 1f; x += 0.005f)
        {
            s.age = s.flightDuration * x;
            Vector3 toLanding = s.landing - s.HeadAnchor(p);
            if (toLanding.sqrMagnitude < 1e-8f) continue;

            float off = Vector3.Cross(toLanding.normalized, Vector3.forward).magnitude;
            Assert.Less(off, 1e-3f, $"x {x} was off the port axis");
        }
    }

    [Test]
    public void HeadAnchor_InsertionOnlyEverMovesTowardThePlug()
    {
        var s = Inserting(out var p);
        float prev = float.MaxValue;
        for (float x = 0.95f; x <= 1f; x += 0.005f)
        {
            s.age = s.flightDuration * x;
            float d = Vector3.Distance(s.HeadAnchor(p), s.landing);
            Assert.LessOrEqual(d, prev + 1e-4f, $"the plug backed out at x {x}");
            prev = d;
        }
    }

    [Test]
    public void HeadAnchor_DoesNotJumpAtTheHandover()
    {
        // A discontinuity here would read as the plug teleporting into the socket.
        var s = Inserting(out var p);

        s.age = s.flightDuration * 0.9490f;
        var before = s.HeadAnchor(p);
        s.age = s.flightDuration * 0.9510f;
        var after = s.HeadAnchor(p);

        Assert.Less(Vector3.Distance(before, after), 0.25f, "the head jumped as insertion began");
    }

    [Test]
    public void HeadAnchor_DepthAloneDoesNotEngageInsertion()
    {
        // Both knobs are required, so a depth left in the parameters cannot
        // silently reshape flights that never asked for an insertion.
        var withDepth = CableParameters.Default;
        withDepth.pathCurl = 6f;
        withDepth.insertion01 = 0f;
        withDepth.insertionDepth = 2f;

        var bare = withDepth;
        bare.insertionDepth = 0f;

        var a = CableShot.Create(0, Src, Tgt, withDepth, 1); a.insertAxis = Vector3.forward;
        var b = CableShot.Create(0, Src, Tgt, bare, 1);      b.insertAxis = Vector3.forward;

        for (float x = 0f; x <= 1f; x += 0.1f)
        {
            a.age = a.flightDuration * x;
            b.age = b.flightDuration * x;
            Assert.Less(Vector3.Distance(a.HeadAnchor(withDepth), b.HeadAnchor(bare)), 1e-5f, $"x {x}");
        }
    }

}
