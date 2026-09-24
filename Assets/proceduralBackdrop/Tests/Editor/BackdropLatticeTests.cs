using NUnit.Framework;
using UnityEngine;

public class BackdropLatticeTests
{
    private static BackdropParameters P(int count, BackdropDomain d, bool solid = false)
    {
        var p = BackdropParameters.Default;
        p.spawnCount = count;
        p.domain = d;
        p.solidFill = solid;
        p.domainSize = new Vector3(60f, 30f, 60f);
        return p;
    }

    [Test]
    public void Rand01_IsDeterministicAndInRange()
    {
        for (int i = 0; i < 500; i++)
        {
            float a = BackdropLattice.Rand01(i, 7u, 0u);
            float b = BackdropLattice.Rand01(i, 7u, 0u);
            Assert.AreEqual(a, b, $"id {i} is not deterministic");
            Assert.GreaterOrEqual(a, 0f, $"id {i}");
            Assert.LessOrEqual(a, 1f, $"id {i}");
        }
    }

    [Test]
    public void Rand01_DiffersAcrossSeeds()
    {
        int same = 0;
        for (int i = 0; i < 200; i++)
            if (Mathf.Approximately(BackdropLattice.Rand01(i, 1u, 0u),
                                    BackdropLattice.Rand01(i, 2u, 0u))) same++;

        Assert.Less(same, 5, "two seeds produce near-identical streams; the hash is not mixing the seed");
    }

    [Test]
    public void Position_StaysInsideTheDomainForEveryShape()
    {
        var size = new Vector3(60f, 30f, 60f);
        foreach (BackdropDomain d in System.Enum.GetValues(typeof(BackdropDomain)))
        {
            for (int i = 0; i < 400; i++)
            {
                var v = BackdropLattice.Position(i, 400, d, size, false);
                Assert.LessOrEqual(Mathf.Abs(v.x), size.x * 0.5f + 1e-3f, $"{d} id {i} x");
                Assert.LessOrEqual(Mathf.Abs(v.y), size.y * 0.5f + 1e-3f, $"{d} id {i} y");
                Assert.LessOrEqual(Mathf.Abs(v.z), size.z * 0.5f + 1e-3f, $"{d} id {i} z");
            }
        }
    }

    [Test]
    public void Position_IsFlatForThePlane()
    {
        for (int i = 0; i < 200; i++)
            Assert.AreEqual(0f,
                BackdropLattice.Position(i, 200, BackdropDomain.Plane, new Vector3(60f, 30f, 60f), false).z,
                1e-5f, $"id {i} is off the plane");
    }

    [Test]
    public void Position_HemisphereStaysOnTheHalfFacingTheCamera()
    {
        // The pole runs along +Z so the dome faces the viewer. A point behind
        // the equator would sit behind the camera once the rig is placed.
        for (int i = 0; i < 300; i++)
            Assert.GreaterOrEqual(
                BackdropLattice.Position(i, 300, BackdropDomain.Hemisphere, Vector3.one * 40f, false).z,
                -1e-4f, $"id {i} dipped behind the equator");
    }

    [Test]
    public void Position_HemisphereDoesNotClusterAtThePole()
    {
        // A naive spherical sample piles points at the pole. Split the cap into
        // two equal-height bands and assert neither starves.
        int count = 600, upper = 0;
        for (int i = 0; i < count; i++)
            if (BackdropLattice.Position(i, count, BackdropDomain.Hemisphere, Vector3.one * 2f, false).z > 0.5f)
                upper++;

        float frac = (float)upper / count;
        Assert.Greater(frac, 0.2f, "too few points near the pole");
        Assert.Less(frac, 0.8f, "points are clustering at the pole");
    }

    [Test]
    public void Position_HollowCubePutsEveryInstanceOnTheShell()
    {
        var size = new Vector3(40f, 40f, 40f);
        for (int i = 0; i < 500; i++)
        {
            var v = BackdropLattice.Position(i, 500, BackdropDomain.Cube, size, false);
            float m = Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
            Assert.AreEqual(20f, m, 1e-3f, $"id {i} is not on the shell");
        }
    }

    [Test]
    public void Position_SolidCubeFillsTheInterior()
    {
        var size = new Vector3(40f, 40f, 40f);
        int interior = 0;
        for (int i = 0; i < 500; i++)
        {
            var v = BackdropLattice.Position(i, 500, BackdropDomain.Cube, size, true);
            float m = Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
            if (m < 19.9f) interior++;
        }
        Assert.Greater(interior, 0, "solid fill produced no interior instances");
    }

    [Test]
    public void Position_NoTwoInstancesShareAPoint()
    {
        // Overlapping lattice points read as a missing instance, and an indexing
        // slip in the cube or grid arithmetic shows up here and nowhere else.
        var seen = new System.Collections.Generic.HashSet<Vector3>();
        int dupes = 0;
        for (int i = 0; i < 300; i++)
            if (!seen.Add(BackdropLattice.Position(i, 300, BackdropDomain.Cube, Vector3.one * 30f, true)))
                dupes++;

        Assert.AreEqual(0, dupes, $"{dupes} instances share a lattice point");
    }

    [Test]
    public void Fill_WritesExactlySpawnCountEntries()
    {
        var p = P(250, BackdropDomain.Cube);
        var m = new Matrix4x4[BackdropParameters.MaxSpawnCount];
        var f = new float[BackdropParameters.MaxSpawnCount];

        Assert.AreEqual(250, BackdropLattice.Fill(p, 0f, -1000f, m, f));
    }

    [Test]
    public void Fill_NeverOverrunsTheBuffer()
    {
        var p = P(BackdropParameters.MaxSpawnCount, BackdropDomain.Cube);
        var m = new Matrix4x4[64];
        var f = new float[64];

        Assert.AreEqual(64, BackdropLattice.Fill(p, 0f, -1000f, m, f),
            "Fill must clamp to the buffer it was handed, not to spawnCount");
    }

    [Test]
    public void Fill_IsReproducibleForAGivenSeed()
    {
        var p = P(120, BackdropDomain.Hemisphere);
        var m1 = new Matrix4x4[200]; var f1 = new float[200];
        var m2 = new Matrix4x4[200]; var f2 = new float[200];

        BackdropLattice.Fill(p, 3.5f, -1000f, m1, f1);
        BackdropLattice.Fill(p, 3.5f, -1000f, m2, f2);

        for (int i = 0; i < 120; i++)
            Assert.AreEqual(m1[i], m2[i], $"instance {i} is not reproducible");
    }

    [Test]
    public void Fill_ChangingTheSeedMovesTheField()
    {
        var a = P(120, BackdropDomain.Cube);
        var b = a; b.layoutSeed = a.layoutSeed + 1;
        a.offsetJitter = b.offsetJitter = Vector3.one;   // jitter is what the seed drives

        var m1 = new Matrix4x4[200]; var f1 = new float[200];
        var m2 = new Matrix4x4[200]; var f2 = new float[200];
        BackdropLattice.Fill(a, 0f, -1000f, m1, f1);
        BackdropLattice.Fill(b, 0f, -1000f, m2, f2);

        int moved = 0;
        for (int i = 0; i < 120; i++) if (m1[i] != m2[i]) moved++;
        Assert.Greater(moved, 100, "a new layout seed barely changed the field");
    }

    [Test]
    public void Fill_WaveReturnsToTheRestPoseRatherThanDrifting()
    {
        // The wave must be an absolute offset from the lattice point. An
        // accumulating add looks fine for a few seconds and then walks the whole
        // field away, which is only visible long after you stop looking.
        var p = P(50, BackdropDomain.Plane);
        p.waveAmplitude = 3f;
        p.waveFrequency = 0.25f;
        p.wavePhaseSpread = 0f;
        p.offsetJitter = Vector3.zero;
        p.spinRateRange = Vector2.zero;

        var m0 = new Matrix4x4[64]; var f0 = new float[64];
        var m1 = new Matrix4x4[64]; var f1 = new float[64];

        // 1/0.25 = 4 seconds is one full cycle; 100 cycles later it must match.
        BackdropLattice.Fill(p, 0f, -1000f, m0, f0);
        BackdropLattice.Fill(p, 400f, -1000f, m1, f1);

        for (int i = 0; i < 50; i++)
            Assert.AreEqual(m0[i].GetColumn(3).y, m1[i].GetColumn(3).y, 1e-2f,
                $"instance {i} drifted after 100 wave cycles");
    }

    [Test]
    public void Fill_FlashDecaysAndIsZeroBeforeItStarts()
    {
        var p = P(10, BackdropDomain.Plane);
        p.flashIntensity = 10f;
        p.flashDecay = 4f;
        p.flashRipple = 0f;

        var m = new Matrix4x4[16]; var f = new float[16];

        BackdropLattice.Fill(p, 5f, 10f, m, f);          // flash is in the future
        Assert.AreEqual(0f, f[0], 1e-6f, "flash fired before its timestamp");

        BackdropLattice.Fill(p, 10f, 10f, m, f);         // at the timestamp
        float atStart = f[0];
        Assert.Greater(atStart, 0f);

        BackdropLattice.Fill(p, 11f, 10f, m, f);         // one second later
        Assert.Less(f[0], atStart, "flash did not decay");
    }

    [Test]
    public void Fill_FlashRippleStaggersAcrossTheField()
    {
        var p = P(100, BackdropDomain.Plane);
        p.flashIntensity = 10f;
        p.flashRipple = 1f;

        var m = new Matrix4x4[128]; var f = new float[128];
        BackdropLattice.Fill(p, 10f, 10f, m, f);

        Assert.Greater(f[0], 0f, "the first instance should already be lit");
        Assert.AreEqual(0f, f[99], 1e-6f, "the last instance should not have fired yet");
    }

    [Test]
    public void LocalBounds_ContainsEveryInstanceItClaimsTo()
    {
        var p = P(300, BackdropDomain.Cube);
        p.offsetJitter = Vector3.one * 2f;
        p.waveAmplitude = 1.5f;

        var b = BackdropLattice.LocalBounds(p);
        var m = new Matrix4x4[512]; var f = new float[512];
        int n = BackdropLattice.Fill(p, 1.234f, -1000f, m, f);

        for (int i = 0; i < n; i++)
            Assert.IsTrue(b.Contains(m[i].GetColumn(3)),
                $"instance {i} at {m[i].GetColumn(3)} sits outside the culling bounds {b}");
    }

    [Test]
    public void FrameSizeAt_MatchesTheCameraAspect()
    {
        // 32:9 super-ultrawide. The whole point of fitting is that the field
        // tracks the frame, so the ratio is the thing worth pinning down.
        var f = BackdropLattice.FrameSizeAt(60f, 32f / 9f, 30f, false, 0f);
        Assert.AreEqual(32f / 9f, f.x / f.y, 1e-4f);

        var wide = BackdropLattice.FrameSizeAt(60f, 21f / 9f, 30f, false, 0f);
        Assert.Less(wide.x, f.x, "a narrower aspect must produce a narrower field");
        Assert.AreEqual(wide.y, f.y, 1e-4f, "aspect must not change the height");
    }

    [Test]
    public void FrameSizeAt_GrowsWithDistanceUnderPerspective()
    {
        var near = BackdropLattice.FrameSizeAt(60f, 16f / 9f, 10f, false, 0f);
        var far  = BackdropLattice.FrameSizeAt(60f, 16f / 9f, 40f, false, 0f);
        Assert.AreEqual(4f, far.y / near.y, 1e-3f, "perspective frame should scale linearly with distance");
    }

    [Test]
    public void FrameSizeAt_IsFlatUnderOrthographic()
    {
        var near = BackdropLattice.FrameSizeAt(60f, 16f / 9f, 10f, true, 12f);
        var far  = BackdropLattice.FrameSizeAt(60f, 16f / 9f, 90f, true, 12f);
        Assert.AreEqual(near.y, far.y, 1e-4f, "an orthographic frame must not grow with distance");
        Assert.AreEqual(24f, near.y, 1e-4f);
    }

    [Test]
    public void FitToFrame_SizesXAndYButLeavesDepthAlone()
    {
        var p = BackdropParameters.Default;
        p.fitToCamera = true;
        p.fitMargin = Vector2.one;
        p.fitDistance = 20f;
        p.domainSize = new Vector3(999f, 999f, 45f);

        var fitted = BackdropLattice.FitToFrame(p, 60f, 32f / 9f, false, 0f);

        Assert.AreEqual(45f, fitted.domainSize.z, 1e-4f, "depth is authored, not implied by the frame");
        Assert.AreNotEqual(999f, fitted.domainSize.x);
        Assert.AreEqual(32f / 9f, fitted.domainSize.x / fitted.domainSize.y, 1e-3f);
    }

    [Test]
    public void FitToFrame_MeasuresAtTheFarFaceSoTheBackIsNotShort()
    {
        // A perspective frustum widens with distance. Fitting at the near face
        // would leave the back of the field inside the frame, with visibly empty
        // corners - this asserts the far face is what gets measured.
        var p = BackdropParameters.Default;
        p.fitToCamera = true;
        p.fitMargin = Vector2.one;
        p.fitDistance = 20f;
        p.domainSize = new Vector3(1f, 1f, 30f);

        var fitted = BackdropLattice.FitToFrame(p, 60f, 16f / 9f, false, 0f);
        var atNear = BackdropLattice.FrameSizeAt(60f, 16f / 9f, 20f, false, 0f);

        Assert.Greater(fitted.domainSize.y, atNear.y + 1e-3f);
    }

    [Test]
    public void FitToFrame_AppliesMargin()
    {
        var p = BackdropParameters.Default;
        p.fitToCamera = true;
        p.fitDistance = 20f;
        p.domainSize = new Vector3(1f, 1f, 0f);

        p.fitMargin = Vector2.one;
        var exact = BackdropLattice.FitToFrame(p, 60f, 16f / 9f, false, 0f).domainSize;

        p.fitMargin = new Vector2(2f, 1.5f);
        var bled = BackdropLattice.FitToFrame(p, 60f, 16f / 9f, false, 0f).domainSize;

        Assert.AreEqual(exact.x * 2f, bled.x, 1e-3f);
        Assert.AreEqual(exact.y * 1.5f, bled.y, 1e-3f);
    }

    [Test]
    public void FitToFrame_IsAPassThroughWhenDisabled()
    {
        var p = BackdropParameters.Default;
        p.fitToCamera = false;
        p.domainSize = new Vector3(11f, 22f, 33f);

        Assert.AreEqual(new Vector3(11f, 22f, 33f),
            BackdropLattice.FitToFrame(p, 60f, 32f / 9f, false, 0f).domainSize);
    }

    private static BackdropParameters Region(BackdropDomain d, int count)
    {
        var p = BackdropParameters.Default;
        p.domain = d;
        p.spawnCount = count;
        p.domainSize = new Vector3(160f, 60f, 20f);
        p.offsetJitter = Vector3.zero;
        p.waveAmplitude = 0f;
        p.spinRateRange = Vector2.zero;
        p.accentFraction = 0f;
        p.occupancy = 1f;
        return p;
    }

    private static Vector3[] Positions(BackdropParameters p)
    {
        var m = new Matrix4x4[p.spawnCount + 8];
        var f = new float[p.spawnCount + 8];
        int n = BackdropLattice.Fill(p, 0f, -1000f, m, f);
        var v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = m[i].GetColumn(3);
        return v;
    }

    [Test]
    public void Arch_LeavesTheMiddleEmpty()
    {
        // The whole point of the shape: a performer stands in the hole.
        var p = Region(BackdropDomain.Arch, 600);
        p.archInnerRadius = 0.5f;

        foreach (var v in Positions(p))
        {
            float r = Mathf.Sqrt(
                (v.x / (p.domainSize.x * 0.5f)) * (v.x / (p.domainSize.x * 0.5f)) +
                (v.y / (p.domainSize.y * 0.5f)) * (v.y / (p.domainSize.y * 0.5f)));
            Assert.GreaterOrEqual(r, 0.5f - 1e-3f, $"instance at {v} is inside the arch void");
            Assert.LessOrEqual(r, 1f + 1e-3f, $"instance at {v} is outside the outer rim");
        }
    }

    [Test]
    public void Arch_AtZeroInnerRadiusFillsTheDisc()
    {
        var p = Region(BackdropDomain.Arch, 400);
        p.archInnerRadius = 0f;

        float nearest = float.MaxValue;
        foreach (var v in Positions(p))
            nearest = Mathf.Min(nearest, new Vector2(v.x / (p.domainSize.x * 0.5f), v.y / (p.domainSize.y * 0.5f)).magnitude);

        Assert.Less(nearest, 0.15f, "a zero inner radius should reach the centre");
    }

    [Test]
    public void Colonnade_ProducesTheRequestedNumberOfBandsWithGaps()
    {
        var p = Region(BackdropDomain.Colonnade, 600);
        p.colonnadeCount = 6;
        p.colonnadeWidth = 0.4f;

        var xs = new System.Collections.Generic.List<float>();
        foreach (var v in Positions(p)) xs.Add(v.x);
        xs.Sort();

        // Count the gaps: a jump much larger than typical spacing is a band edge.
        float slot = p.domainSize.x / p.colonnadeCount;
        int gaps = 0;
        for (int i = 1; i < xs.Count; i++)
            if (xs[i] - xs[i - 1] > slot * 0.3f) gaps++;

        Assert.AreEqual(p.colonnadeCount - 1, gaps,
            $"expected {p.colonnadeCount - 1} gaps between {p.colonnadeCount} bands, found {gaps}");
    }

    [Test]
    public void Colonnade_FullWidthClosesTheGaps()
    {
        var p = Region(BackdropDomain.Colonnade, 600);
        p.colonnadeCount = 6;
        p.colonnadeWidth = 1f;

        var xs = new System.Collections.Generic.List<float>();
        foreach (var v in Positions(p)) xs.Add(v.x);
        xs.Sort();

        float slot = p.domainSize.x / p.colonnadeCount;
        int gaps = 0;
        for (int i = 1; i < xs.Count; i++)
            if (xs[i] - xs[i - 1] > slot * 0.6f) gaps++;

        Assert.AreEqual(0, gaps, "width 1 should read as a solid wall, not bands");
    }

    [Test]
    public void Skyline_VariesHeightAndStaysOnItsSideOfTheField()
    {
        var p = Region(BackdropDomain.Skyline, 500);
        p.skylineHeight = 0.7f;
        p.skylineInverted = false;

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in Positions(p)) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }

        float half = p.domainSize.y * 0.5f;
        Assert.GreaterOrEqual(lo, -half - 1e-3f);
        Assert.LessOrEqual(hi, -half + 0.7f * p.domainSize.y + 1e-3f,
            "skyline rose past its height limit");
        Assert.Greater(hi - lo, p.domainSize.y * 0.1f, "heights barely varied; the field reads flat");

        // Depth too. A sheet collapsed to a single row still varies in height,
        // so the height check alone would not have caught it.
        float zlo = float.MaxValue, zhi = float.MinValue;
        foreach (var v in Positions(p)) { zlo = Mathf.Min(zlo, v.z); zhi = Mathf.Max(zhi, v.z); }
        Assert.Greater(zhi - zlo, p.domainSize.z * 0.5f, "skyline collapsed into one depth plane");
    }

    [Test]
    public void SheetCounts_SplitsProportionallyAndLeavesRoom()
    {
        foreach (int count in new[] { 1, 17, 500, 4000 })
        foreach (var ab in new[] { (440f, 20f), (160f, 20f), (10f, 10f), (5f, 900f) })
        {
            var n = BackdropLattice.SheetCounts(count, ab.Item1, ab.Item2);
            Assert.GreaterOrEqual(n.x * n.y, count, $"{count} over {ab}");
            Assert.GreaterOrEqual(n.x, 1f);
            Assert.GreaterOrEqual(n.y, 1f);
        }

        // Proportional: the longer extent carries more.
        var wide = BackdropLattice.SheetCounts(600, 440f, 20f);
        Assert.Greater(wide.x, wide.y, "the longer extent should carry more instances");
    }

    [Test]
    public void Skyline_InvertedHangsFromTheTop()
    {
        var p = Region(BackdropDomain.Skyline, 400);
        p.skylineHeight = 0.6f;

        p.skylineInverted = false;
        float upMean = 0f; var up = Positions(p);
        foreach (var v in up) upMean += v.y;
        upMean /= up.Length;

        p.skylineInverted = true;
        float downMean = 0f; var down = Positions(p);
        foreach (var v in down) downMean += v.y;
        downMean /= down.Length;

        Assert.Greater(downMean, upMean, "inverting should move the mass to the top of the frame");
    }

    [Test]
    public void Corridor_IsHollowThroughTheMiddle()
    {
        // Every instance must sit on one of the four side faces, leaving the
        // centre open all the way through.
        var p = Region(BackdropDomain.Corridor, 600);
        float hx = p.domainSize.x * 0.5f, hy = p.domainSize.y * 0.5f;

        foreach (var v in Positions(p))
        {
            bool onSide = Mathf.Abs(Mathf.Abs(v.x) - hx) < 1e-2f
                       || Mathf.Abs(Mathf.Abs(v.y) - hy) < 1e-2f;
            Assert.IsTrue(onSide, $"instance at {v} is not on a corridor wall");
        }
    }

    [Test]
    public void Corridor_UsesItsFullDepth()
    {
        var p = Region(BackdropDomain.Corridor, 600);

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in Positions(p)) { lo = Mathf.Min(lo, v.z); hi = Mathf.Max(hi, v.z); }

        Assert.Greater(hi - lo, p.domainSize.z * 0.5f,
            "the corridor should run into depth, not sit in one plane");
    }

    [Test]
    public void Drift_ThinsOneEndAndLeavesTheOther()
    {
        var p = Region(BackdropDomain.Plane, 900);
        p.occupancy = 1f;
        p.driftDirection = Vector3.right;
        p.driftAmount = 1f;

        int left = 0, right = 0;
        var m = new Matrix4x4[1024]; var f = new float[1024];
        int n = BackdropLattice.Fill(p, 0f, -1000f, m, f);
        for (int i = 0; i < n; i++)
        {
            if (((Vector3)m[i].GetColumn(3)).x < 0f) left++; else right++;
        }

        Assert.Greater(left, right * 2,
            $"drift should weight the near end: {left} left vs {right} right");
        Assert.Greater(right, 0, "a full fade should still leave a few at the far end, not a hard cut");
    }

    [Test]
    public void Drift_AtZeroAmountChangesNothing()
    {
        var p = Region(BackdropDomain.Plane, 400);
        p.occupancy = 0.6f;
        p.driftDirection = Vector3.right;

        p.driftAmount = 0f;
        int without = Positions(p).Length;

        p.driftDirection = Vector3.zero;
        int none = Positions(p).Length;

        Assert.AreEqual(none, without, "zero drift amount must behave as no drift at all");
    }

    [Test]
    public void EveryDomain_KeepsInstancesInsideTheCullingBounds()
    {
        // The region shapes are new; bounds that do not contain them would pop
        // the whole field out at a glancing angle.
        foreach (BackdropDomain d in System.Enum.GetValues(typeof(BackdropDomain)))
        {
            var p = Region(d, 300);
            p.offsetJitter = Vector3.one;
            p.waveAmplitude = 1f;
            p.accentFraction = 0.2f;

            var b = BackdropLattice.LocalBounds(p);
            foreach (var v in Positions(p))
                Assert.IsTrue(b.Contains(v), $"{d}: instance at {v} outside bounds {b}");
        }
    }

    [Test]
    public void WordIndex_AdvancesPerInstanceOnMostDomains()
    {
        var p = BackdropParameters.Default;
        p.domain = BackdropDomain.Plane;

        for (int i = 0; i < 40; i++)
            Assert.AreEqual(i % 12, BackdropLattice.WordIndexForInstance(i, 100, p, 12), $"instance {i}");
    }

    [Test]
    public void WordIndex_AdvancesPerColumnOnAColonnade()
    {
        // One letter per band is the arrangement worth having: the word reads
        // across the frame instead of tiling through it.
        var p = BackdropParameters.Default;
        p.domain = BackdropDomain.Colonnade;
        p.colonnadeCount = 6;

        const int count = 60;          // 10 instances per column
        int perCol = Mathf.CeilToInt((float)count / 6);

        for (int col = 0; col < 6; col++)
        {
            int expected = col % 12;
            for (int k = 0; k < perCol; k++)
            {
                int id = col * perCol + k;
                if (id >= count) break;
                Assert.AreEqual(expected, BackdropLattice.WordIndexForInstance(id, count, p, 12),
                    $"column {col} instance {k} should share the column's letter");
            }
        }
    }

    [Test]
    public void WordIndex_WrapsRatherThanRunningOff()
    {
        var p = BackdropParameters.Default;
        p.domain = BackdropDomain.Plane;

        for (int i = 0; i < 500; i++)
        {
            int w = BackdropLattice.WordIndexForInstance(i, 500, p, 12);
            Assert.GreaterOrEqual(w, 0, $"instance {i}");
            Assert.Less(w, 12, $"instance {i}");
        }
    }

    [Test]
    public void WordIndex_HandlesAnEmptyWord()
    {
        var p = BackdropParameters.Default;
        Assert.AreEqual(0, BackdropLattice.WordIndexForInstance(7, 100, p, 0));
    }

    [Test]
    public void ModelSubset_PicksDistinctIndicesInRange()
    {
        // Repeats would waste a slot and silently reduce the variety asked for.
        var dst = new int[32];
        var scratch = new int[64];

        foreach (int libCount in new[] { 1, 3, 10, 16 })
        foreach (int want in new[] { 1, 2, 5, 10, 32 })
        for (uint seed = 1; seed < 20; seed++)
        {
            int n = BackdropLattice.BuildModelSubset(dst, scratch, libCount, want, seed);

            Assert.AreEqual(Mathf.Min(want, libCount), n, $"lib {libCount} want {want}");

            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                Assert.GreaterOrEqual(dst[i], 0);
                Assert.Less(dst[i], libCount, $"lib {libCount} seed {seed}");
                Assert.IsTrue(seen.Add(dst[i]), $"index {dst[i]} repeated (lib {libCount} want {want} seed {seed})");
            }
        }
    }

    [Test]
    public void ModelSubset_CoversTheWholeLibraryWhenAskedForAllOfIt()
    {
        var dst = new int[32];
        var scratch = new int[64];

        int n = BackdropLattice.BuildModelSubset(dst, scratch, 10, 10, 7u);
        Assert.AreEqual(10, n);

        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < n; i++) seen.Add(dst[i]);
        Assert.AreEqual(10, seen.Count, "asking for the whole library should return every index once");
    }

    [Test]
    public void ModelSubset_IsReproducibleAndSeedDependent()
    {
        var a = new int[32]; var b = new int[32]; var scratch = new int[64];

        BackdropLattice.BuildModelSubset(a, scratch, 16, 5, 3u);
        BackdropLattice.BuildModelSubset(b, scratch, 16, 5, 3u);
        for (int i = 0; i < 5; i++) Assert.AreEqual(a[i], b[i], $"slot {i} not reproducible");

        BackdropLattice.BuildModelSubset(b, scratch, 16, 5, 4u);
        bool same = true;
        for (int i = 0; i < 5; i++) if (a[i] != b[i]) same = false;
        Assert.IsFalse(same, "two seeds produced the same subset");
    }

    [Test]
    public void ModelForInstance_SpreadsAcrossTheSubset()
    {
        // A field that leaned on one letter would read as a mistake rather than
        // as a word.
        var subset = new int[] { 2, 5, 7, 9 };
        var hits = new int[4];

        for (int i = 0; i < 800; i++)
        {
            int m = BackdropLattice.ModelForInstance(i, 11u, subset, 4);
            int slot = System.Array.IndexOf(subset, m);
            Assert.GreaterOrEqual(slot, 0, $"instance {i} got {m}, which is not in the subset");
            hits[slot]++;
        }

        foreach (int h in hits)
            Assert.Greater(h, 800 / 4 / 3, $"a model was starved: {hits[0]},{hits[1]},{hits[2]},{hits[3]}");
    }

    [Test]
    public void ModelForInstance_HoldsStillAcrossUnrelatedParameters()
    {
        // Its own hash stream, so dialling scale or position does not reshuffle
        // which glyph each instance shows.
        var subset = new int[] { 0, 1, 2 };
        for (int i = 0; i < 200; i++)
            Assert.AreEqual(
                BackdropLattice.ModelForInstance(i, 5u, subset, 3),
                BackdropLattice.ModelForInstance(i, 5u, subset, 3),
                $"instance {i} is not stable");
    }

    [Test]
    public void ModelForInstance_SingleSubsetAlwaysReturnsThatModel()
    {
        var subset = new int[] { 6 };
        for (int i = 0; i < 100; i++)
            Assert.AreEqual(6, BackdropLattice.ModelForInstance(i, 2u, subset, 1), $"instance {i}");
    }

    [Test]
    public void Occupancy_ThinsTheFieldWithoutMovingWhatRemains()
    {
        // The property that makes occupancy usable as a performance control: it
        // filters a fixed lattice rather than rebuilding a smaller one, so
        // pulling it down removes instances instead of rearranging everything.
        var full = P(400, BackdropDomain.Cube);
        full.occupancy = 1f;
        full.offsetJitter = Vector3.zero;
        full.waveAmplitude = 0f;
        full.spinRateRange = Vector2.zero;
        full.accentFraction = 0f;

        var thin = full;
        thin.occupancy = 0.5f;

        var mf = new Matrix4x4[512]; var ff = new float[512];
        var mt = new Matrix4x4[512]; var ft = new float[512];
        int nf = BackdropLattice.Fill(full, 0f, -1000f, mf, ff);
        int nt = BackdropLattice.Fill(thin, 0f, -1000f, mt, ft);

        Assert.Less(nt, nf, "occupancy 0.5 should render fewer instances");

        // Every surviving position must still appear in the full field.
        var fullPositions = new System.Collections.Generic.HashSet<Vector3>();
        for (int i = 0; i < nf; i++) fullPositions.Add(mf[i].GetColumn(3));
        for (int i = 0; i < nt; i++)
            Assert.IsTrue(fullPositions.Contains(mt[i].GetColumn(3)),
                $"thinned instance {i} sits somewhere the full field never had one");
    }

    [Test]
    public void Occupancy_RoughlyMatchesTheRequestedFraction()
    {
        var p = P(1000, BackdropDomain.Cube);
        var m = new Matrix4x4[1200]; var f = new float[1200];

        foreach (float want in new[] { 0.25f, 0.5f, 0.75f })
        {
            p.occupancy = want;
            int n = BackdropLattice.Fill(p, 0f, -1000f, m, f);
            float got = n / 1000f;
            Assert.AreEqual(want, got, 0.08f, $"asked for {want:P0}, rendered {got:P0}");
        }
    }

    [Test]
    public void Occupancy_AtZeroAndOneAreTheLimits()
    {
        var p = P(200, BackdropDomain.Plane);
        var m = new Matrix4x4[256]; var f = new float[256];

        p.occupancy = 0f;
        Assert.AreEqual(0, BackdropLattice.Fill(p, 0f, -1000f, m, f));

        p.occupancy = 1f;
        Assert.AreEqual(200, BackdropLattice.Fill(p, 0f, -1000f, m, f));
    }

    [Test]
    public void Occupancy_NoiseScaleClustersTheGaps()
    {
        // Clustered gaps read as structure; evenly scattered ones read as
        // damage. Neighbouring slots should agree more often under noise than
        // under a pure per-slot roll.
        var even = P(600, BackdropDomain.Plane);
        even.occupancy = 0.5f;
        even.occupancyNoiseScale = 0f;

        var clustered = even;
        clustered.occupancyNoiseScale = 0.08f;

        int agreeEven = 0, agreeClustered = 0;
        for (int i = 0; i < 599; i++)
        {
            if (BackdropLattice.IsOccupied(i, 600, even) == BackdropLattice.IsOccupied(i + 1, 600, even)) agreeEven++;
            if (BackdropLattice.IsOccupied(i, 600, clustered) == BackdropLattice.IsOccupied(i + 1, 600, clustered)) agreeClustered++;
        }

        Assert.Greater(agreeClustered, agreeEven,
            $"noise-scaled occupancy should cluster: neighbours agreed {agreeClustered} vs {agreeEven}");
    }

    [Test]
    public void Accent_PromotesRoughlyTheRequestedFraction()
    {
        var p = BackdropParameters.Default;
        p.accentFraction = 0.1f;
        p.accentRatio = 1.618034f;
        p.accentSteps = 1;

        int accented = 0;
        for (int i = 0; i < 2000; i++)
            if (BackdropLattice.AccentMultiplier(i, p) > 1.0001f) accented++;

        Assert.AreEqual(0.1f, accented / 2000f, 0.03f);
    }

    [Test]
    public void Accent_UsesExactRatioSteps()
    {
        // The accents must land on discrete classes, not a smear - that is what
        // separates a size hierarchy from a wider random range.
        var p = BackdropParameters.Default;
        p.accentFraction = 1f;
        p.accentRatio = 1.618034f;
        p.accentSteps = 3;

        for (int i = 0; i < 300; i++)
        {
            float m = BackdropLattice.AccentMultiplier(i, p);
            float steps = Mathf.Log(m) / Mathf.Log(1.618034f);
            Assert.AreEqual(Mathf.Round(steps), steps, 1e-3f,
                $"instance {i} multiplier {m} is not a whole power of the ratio");
            Assert.LessOrEqual(Mathf.Round(steps), 3f);
        }
    }

    [Test]
    public void Accent_HigherStepsAreRarer()
    {
        // A skyline has many mid-rise and few towers; a flat distribution across
        // classes would read as noise rather than hierarchy.
        var p = BackdropParameters.Default;
        p.accentFraction = 1f;
        p.accentRatio = 2f;
        p.accentSteps = 3;

        int one = 0, two = 0, three = 0;
        for (int i = 0; i < 3000; i++)
        {
            int steps = Mathf.RoundToInt(Mathf.Log(BackdropLattice.AccentMultiplier(i, p)) / Mathf.Log(2f));
            if (steps == 1) one++; else if (steps == 2) two++; else if (steps >= 3) three++;
        }

        Assert.Greater(one, two, "one-step accents should outnumber two-step");
        Assert.Greater(two, three, "two-step accents should outnumber three-step");
    }

    [Test]
    public void Accent_ZeroFractionLeavesEveryInstanceAlone()
    {
        var p = BackdropParameters.Default;
        p.accentFraction = 0f;
        for (int i = 0; i < 500; i++)
            Assert.AreEqual(1f, BackdropLattice.AccentMultiplier(i, p), 1e-6f, $"instance {i}");
    }

    [Test]
    public void LocalBounds_StillContainsAccentedInstances()
    {
        // Accents are the largest objects in the field, so they are exactly the
        // ones that pop out at a glancing angle if the bounds ignore them.
        var p = P(300, BackdropDomain.Cube);
        p.accentFraction = 0.5f;
        p.accentRatio = 2f;
        p.accentSteps = 3;
        p.offsetJitter = Vector3.one * 2f;

        var b = BackdropLattice.LocalBounds(p);
        var m = new Matrix4x4[512]; var f = new float[512];
        int n = BackdropLattice.Fill(p, 0.5f, -1000f, m, f);

        for (int i = 0; i < n; i++)
            Assert.IsTrue(b.Contains(m[i].GetColumn(3)), $"instance {i} outside bounds {b}");
    }

    [Test]
    public void NoiseMotion_MovesNeighboursTogetherAndStaysBounded()
    {
        var p = P(200, BackdropDomain.Plane);
        p.motion = BackdropMotion.Noise;
        p.waveAmplitude = 3f;
        p.waveFrequency = 0.3f;
        p.noiseScale = 0.05f;
        p.offsetJitter = Vector3.zero;
        p.spinRateRange = Vector2.zero;
        p.occupancy = 1f;
        p.accentFraction = 0f;

        var rest = p; rest.waveAmplitude = 0f;
        var mr = new Matrix4x4[256]; var fr = new float[256];
        BackdropLattice.Fill(rest, 0f, -1000f, mr, fr);

        var m = new Matrix4x4[256]; var f = new float[256];
        for (float t = 0f; t < 20f; t += 2.5f)
        {
            int n = BackdropLattice.Fill(p, t, -1000f, m, f);
            for (int i = 0; i < n; i++)
            {
                float d = Vector3.Distance(m[i].GetColumn(3), mr[i].GetColumn(3));
                Assert.LessOrEqual(d, 3f * Mathf.Sqrt(3f) + 1e-2f,
                    $"instance {i} at t={t} strayed {d} from rest, beyond the amplitude");
            }
        }
    }

    [Test]
    public void NoiseMotion_ActuallyDiffersFromSine()
    {
        var sine = P(120, BackdropDomain.Plane);
        sine.waveAmplitude = 2f;
        sine.offsetJitter = Vector3.zero;
        sine.spinRateRange = Vector2.zero;
        sine.accentFraction = 0f;
        sine.motion = BackdropMotion.Sine;

        var noise = sine;
        noise.motion = BackdropMotion.Noise;

        var ms = new Matrix4x4[128]; var fs = new float[128];
        var mn = new Matrix4x4[128]; var fn = new float[128];
        BackdropLattice.Fill(sine, 3.7f, -1000f, ms, fs);
        BackdropLattice.Fill(noise, 3.7f, -1000f, mn, fn);

        int differ = 0;
        for (int i = 0; i < 120; i++)
            if (Vector3.Distance(ms[i].GetColumn(3), mn[i].GetColumn(3)) > 1e-3f) differ++;

        Assert.Greater(differ, 100, "noise motion should not resemble the sine it replaces");
    }

    [Test]
    public void AxisCounts_GivesEvenWorldSpacingOnAnUnevenBox()
    {
        // The case a camera-fitted domain always produces: very wide, medium
        // tall, shallow. A shared per-axis N would space these 20 apart across
        // and 2 apart through.
        var size = new Vector3(240f, 90f, 20f);
        var n = BackdropLattice.AxisCounts(700, size);

        float sx = size.x / n.x, sy = size.y / n.y, sz = size.z / n.z;
        Assert.AreEqual(sx, sy, sx * 0.35f, $"x and y spacing diverge: {sx} vs {sy}");
        Assert.AreEqual(sx, sz, sx * 0.35f, $"x and z spacing diverge: {sx} vs {sz}");
    }

    [Test]
    public void AxisCounts_HasRoomForEveryInstance()
    {
        // Short of count, the index wraps and instances stack invisibly.
        foreach (var size in new[]
        {
            new Vector3(240f, 90f, 20f), new Vector3(10f, 10f, 10f),
            new Vector3(300f, 5f, 5f),   new Vector3(1f, 200f, 1f),
        })
        foreach (int count in new[] { 1, 7, 300, 700, 4000 })
        {
            var n = BackdropLattice.AxisCounts(count, size);
            Assert.GreaterOrEqual(n.x * n.y * n.z, count, $"{size} / {count}");
        }
    }

    [Test]
    public void AxisCounts_PutsMoreResolutionOnTheLongerAxis()
    {
        var n = BackdropLattice.AxisCounts(500, new Vector3(200f, 50f, 25f));
        Assert.Greater(n.x, n.y, "the widest axis should carry the most instances");
        Assert.Greater(n.y, n.z);
    }

    [Test]
    public void Position_WideCubeStillPutsEveryInstanceOnTheShell()
    {
        var size = new Vector3(240f, 90f, 20f);
        for (int i = 0; i < 700; i++)
        {
            var v = BackdropLattice.Position(i, 700, BackdropDomain.Cube, size, false);
            float onShell = Mathf.Min(
                Mathf.Abs(Mathf.Abs(v.x) - size.x * 0.5f),
                Mathf.Min(Mathf.Abs(Mathf.Abs(v.y) - size.y * 0.5f),
                          Mathf.Abs(Mathf.Abs(v.z) - size.z * 0.5f)));
            Assert.Less(onShell, 1e-2f, $"id {i} at {v} is not on any face");
        }
    }
}
