using NUnit.Framework;
using UnityEngine;

public class BackdropRandomizerTests
{
    private static BackdropRanges Ranges() => BackdropRanges.CreateDefault();

    [Test]
    public void Randomize_IsReproducibleForAGivenSeed()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var a = BackdropRandomizer.Randomize(r, basis, new System.Random(4242));
        var b = BackdropRandomizer.Randomize(r, basis, new System.Random(4242));

        Assert.AreEqual(a.spawnCount, b.spawnCount);
        Assert.AreEqual(a.domain, b.domain);
        Assert.AreEqual(a.domainSize, b.domainSize);
        Assert.AreEqual(a.scaleRange, b.scaleRange);
        Assert.AreEqual(a.waveFrequency, b.waveFrequency, 1e-6f);
        Assert.AreEqual(a.shading, b.shading);
    }

    [Test]
    public void Randomize_DiffersAcrossSeeds()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var a = BackdropRandomizer.Randomize(r, basis, new System.Random(1));
        var b = BackdropRandomizer.Randomize(r, basis, new System.Random(2));

        Assert.AreNotEqual(a.waveFrequency, b.waveFrequency);
    }

    [Test]
    public void Randomize_RespectsSpawnCountRange()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 200);

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.GreaterOrEqual(p.spawnCount, 50, $"seed {seed}");
            Assert.LessOrEqual(p.spawnCount, 200, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_NeverExceedsTheHardCeiling()
    {
        var r = Ranges();
        // An operator could plausibly type this into the inspector.
        r.spawnCount = new RandomRange(50000, 90000);

        for (int seed = 0; seed < 50; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.LessOrEqual(p.spawnCount, BackdropParameters.MaxSpawnCount, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_LeavesLockedParametersAtTheBasisValue()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 200, locked: true);
        r.waveAmplitude = new RandomRange(0f, 5f, locked: true);

        var basis = BackdropParameters.Default;
        basis.spawnCount = 777;
        basis.waveAmplitude = 1.25f;

        for (int seed = 0; seed < 50; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, basis, new System.Random(seed));
            Assert.AreEqual(777, p.spawnCount, $"seed {seed}");
            Assert.AreEqual(1.25f, p.waveAmplitude, 1e-6f, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_AlwaysProducesAnOrderedScaleRange()
    {
        var r = Ranges();
        // Deliberately overlapping min/max ranges: the sampler can draw min > max.
        r.scaleMin = new RandomRange(0.5f, 3f);
        r.scaleMax = new RandomRange(0.5f, 3f);

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.LessOrEqual(p.scaleRange.x, p.scaleRange.y, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_NeverProducesAnInvisibleField()
    {
        var r = Ranges();

        for (int seed = 0; seed < 200; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.Greater(p.spawnCount, 0, $"seed {seed}: zero instances renders nothing");
            Assert.Greater(p.scaleRange.y, 0f, $"seed {seed}: zero max scale renders nothing");
            Assert.Greater(p.domainSize.x, 0f, $"seed {seed}");
        }
    }

    [Test]
    public void Mutate_AtZeroStrengthReturnsTheBasisUnchanged()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        var m = BackdropRandomizer.Mutate(r, basis, 0f, new System.Random(9));

        Assert.AreEqual(basis.spawnCount, m.spawnCount);
        Assert.AreEqual(basis.waveAmplitude, m.waveAmplitude, 1e-5f);
        Assert.AreEqual(basis.waveFrequency, m.waveFrequency, 1e-5f);
        Assert.AreEqual(basis.domain, m.domain);
    }

    [Test]
    public void Mutate_MovesLessThanAFullRandomize()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;
        basis.waveAmplitude = 1.5f;

        float mutated = 0f, randomised = 0f;
        for (int seed = 0; seed < 100; seed++)
        {
            mutated += Mathf.Abs(
                BackdropRandomizer.Mutate(r, basis, 0.1f, new System.Random(seed)).waveAmplitude
                - basis.waveAmplitude);
            randomised += Mathf.Abs(
                BackdropRandomizer.Randomize(r, basis, new System.Random(seed)).waveAmplitude
                - basis.waveAmplitude);
        }

        Assert.Less(mutated, randomised,
            "mutation is meant to refine inside a region, not resample the space");
    }

    [Test]
    public void Mutate_StaysInsideTheRange()
    {
        var r = Ranges();
        r.waveAmplitude = new RandomRange(0f, 3f);

        var basis = BackdropParameters.Default;
        basis.waveAmplitude = 2.95f;   // right against the ceiling

        for (int seed = 0; seed < 200; seed++)
        {
            var m = BackdropRandomizer.Mutate(r, basis, 0.5f, new System.Random(seed));
            Assert.GreaterOrEqual(m.waveAmplitude, 0f, $"seed {seed}");
            Assert.LessOrEqual(m.waveAmplitude, 3f, $"seed {seed}");
        }
    }

    [Test]
    public void Mutate_RespectsLocks()
    {
        var r = Ranges();
        r.spawnCount = new RandomRange(50, 600, locked: true);

        var basis = BackdropParameters.Default;
        basis.spawnCount = 321;

        for (int seed = 0; seed < 50; seed++)
            Assert.AreEqual(321,
                BackdropRandomizer.Mutate(r, basis, 0.9f, new System.Random(seed)).spawnCount,
                $"seed {seed}");
    }
}