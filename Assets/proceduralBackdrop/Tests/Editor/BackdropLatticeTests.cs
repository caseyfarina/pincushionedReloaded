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
    public void Position_HemisphereStaysOnTheUpperHalf()
    {
        for (int i = 0; i < 300; i++)
            Assert.GreaterOrEqual(
                BackdropLattice.Position(i, 300, BackdropDomain.Hemisphere, Vector3.one * 40f, false).y,
                -1e-4f, $"id {i} dipped below the equator");
    }

    [Test]
    public void Position_HemisphereDoesNotClusterAtThePole()
    {
        // A naive spherical sample piles points at the pole. Split the cap into
        // two equal-height bands and assert neither starves.
        int count = 600, upper = 0;
        for (int i = 0; i < count; i++)
            if (BackdropLattice.Position(i, count, BackdropDomain.Hemisphere, Vector3.one * 2f, false).y > 0.5f)
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
}
