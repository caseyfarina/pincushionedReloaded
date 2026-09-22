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

        // Any single parameter can legitimately agree across two seeds now that
        // a roll can snap to an end or hold its value, so compare the whole
        // configuration rather than one float.
        bool identical = a.spawnCount == b.spawnCount
                      && a.domainSize == b.domainSize
                      && a.scaleRange == b.scaleRange
                      && Mathf.Approximately(a.waveFrequency, b.waveFrequency)
                      && Mathf.Approximately(a.waveAmplitude, b.waveAmplitude);
        Assert.IsFalse(identical, "two different seeds produced the same configuration");
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
    public void Randomize_DrawsANewLayoutSeed_ButMutateHoldsIt()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;

        // Randomize resamples the space, and the arrangement is part of it:
        // without a fresh seed two presses landing on the same count and domain
        // give the identical field, which reads as a dead button.
        var rng = new System.Random(77);
        var first = BackdropRandomizer.Randomize(r, basis, rng);
        var second = BackdropRandomizer.Randomize(r, first, rng);

        Assert.AreNotEqual(basis.layoutSeed, first.layoutSeed);
        Assert.AreNotEqual(first.layoutSeed, second.layoutSeed);

        // Mutate refines a look in place, so the arrangement is held.
        for (int seed = 0; seed < 50; seed++)
            Assert.AreEqual(first.layoutSeed,
                BackdropRandomizer.Mutate(r, first, 0.9f, new System.Random(seed)).layoutSeed,
                $"seed {seed}");
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

    [Test]
    public void Randomize_LeavesProportionsUndistorted()
    {
        // The library holds designed shapes, not generic boxes. Stretching an
        // axis turns an ellipse into an egg and a cross into a crucifix, so the
        // randomiser must never touch the bias - size varies through the uniform
        // scale range instead.
        var r = Ranges();

        for (int seed = 0; seed < 400; seed++)
        {
            var b = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).scaleAxisBias;
            Assert.AreEqual(Vector3.one, b, $"seed {seed} distorted the proportions to {b}");
        }
    }

    [Test]
    public void Randomize_StillVariesSizeThroughTheScaleRange()
    {
        // Pinning the bias must not leave every roll the same size.
        var r = Ranges();

        float lo = float.MaxValue, hi = float.MinValue;
        for (int seed = 0; seed < 300; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            lo = Mathf.Min(lo, p.scaleRange.y);
            hi = Mathf.Max(hi, p.scaleRange.y);
        }

        Assert.Greater(hi - lo, 5f, $"scale barely varied across 300 rolls: {lo:0.0} to {hi:0.0}");
    }

    [Test]
    public void Mutate_AlsoLeavesProportionsAlone()
    {
        var r = Ranges();
        var basis = BackdropParameters.Default;
        basis.scaleAxisBias = Vector3.one;

        for (int seed = 0; seed < 60; seed++)
            Assert.AreEqual(Vector3.one,
                BackdropRandomizer.Mutate(r, basis, 0.4f, new System.Random(seed)).scaleAxisBias,
                $"seed {seed}");
    }

    [Test]
    public void Randomize_RotationIsEitherAlignedOrTumbledOnAllThreeAxes()
    {
        // The in-between - one axis jittered, the others not - is what made
        // every roll read alike, and should no longer be reachable by chance.
        var r = Ranges();

        for (int seed = 0; seed < 300; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));

            if (p.alignToShape)
            {
                Assert.AreEqual(Vector3.zero, p.rotationJitter, $"seed {seed} aligned but still jittered");
            }
            else
            {
                Assert.AreEqual(p.rotationJitter.x, p.rotationJitter.y, 1e-4f, $"seed {seed} x/y differ");
                Assert.AreEqual(p.rotationJitter.y, p.rotationJitter.z, 1e-4f, $"seed {seed} y/z differ");
            }
        }
    }

    [Test]
    public void Randomize_ReachesBothAlignedAndTumbled()
    {
        var r = Ranges();
        int aligned = 0;
        for (int seed = 0; seed < 400; seed++)
            if (BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).alignToShape)
                aligned++;

        Assert.Greater(aligned, 40, "almost never aligned");
        Assert.Less(aligned, 360, "almost always aligned");
    }

    [Test]
    public void Randomize_LandsOnRangeEndsOftenEnoughToMatter()
    {
        // The point of the whole change. Uniform sampling across twenty
        // parameters regresses to mid-range, so every roll reads alike; ends are
        // what give a roll character, and they have to actually turn up.
        var r = Ranges();
        r.extremeChance = 0.4f;
        r.holdChance = 0f;
        r.waveAmplitude = new RandomRange(0f, 5f);

        int ends = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            float v = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).waveAmplitude;
            if (Mathf.Approximately(v, 0f) || Mathf.Approximately(v, 5f)) ends++;
        }

        float frac = ends / 400f;
        Assert.Greater(frac, 0.25f, $"only {frac:P0} of rolls hit an end; extremes are still too rare");
        Assert.Less(frac, 0.6f, $"{frac:P0} of rolls hit an end; the middle of the range has been abandoned");
    }

    [Test]
    public void Randomize_WithNoExtremeChanceIsPurelyUniform()
    {
        var r = Ranges();
        r.extremeChance = 0f;
        r.holdChance = 0f;
        r.waveAmplitude = new RandomRange(0f, 5f);

        int ends = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            float v = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).waveAmplitude;
            if (Mathf.Approximately(v, 0f) || Mathf.Approximately(v, 5f)) ends++;
        }
        Assert.LessOrEqual(ends, 2, "extremeChance 0 should essentially never land exactly on an end");
    }

    [Test]
    public void Randomize_MaxVsMinBiasesWhichEnd()
    {
        var r = Ranges();
        r.extremeChance = 1f;
        r.holdChance = 0f;
        r.waveAmplitude = new RandomRange(0f, 5f);

        r.maxVsMin = 1f;
        for (int seed = 0; seed < 60; seed++)
            Assert.AreEqual(5f,
                BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).waveAmplitude,
                1e-4f, $"seed {seed} should have taken the maximum");

        r.maxVsMin = 0f;
        for (int seed = 0; seed < 60; seed++)
            Assert.AreEqual(0f,
                BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed)).waveAmplitude,
                1e-4f, $"seed {seed} should have taken the minimum");
    }

    [Test]
    public void Randomize_HoldKeepsParametersFromMoving()
    {
        // Without this, consecutive rolls share nothing and a look can only be
        // stumbled on rather than developed.
        var r = Ranges();
        r.holdChance = 1f;

        var basis = BackdropParameters.Default;
        basis.waveAmplitude = 1.234f;
        basis.spawnCount = 456;

        for (int seed = 0; seed < 40; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, basis, new System.Random(seed));
            Assert.AreEqual(1.234f, p.waveAmplitude, 1e-4f, $"seed {seed}");
            Assert.AreEqual(456, p.spawnCount, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_HoldAppliesToDiscreteParametersToo()
    {
        // Domain and shading are the loudest changes on screen; flipping them on
        // every roll drowns out whatever the continuous parameters just did.
        var r = Ranges();
        r.holdChance = 1f;

        var basis = BackdropParameters.Default;
        basis.domain = BackdropDomain.Hemisphere;
        basis.shading = BackdropShadingMode.Lit;

        for (int seed = 0; seed < 40; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, basis, new System.Random(seed));
            Assert.AreEqual(BackdropDomain.Hemisphere, p.domain, $"seed {seed}");
            Assert.AreEqual(BackdropShadingMode.Lit, p.shading, $"seed {seed}");
        }
    }

    [Test]
    public void Randomize_LockingOneParameterDoesNotDisturbAnother()
    {
        // The draw-order invariant, now that each parameter consumes three rng
        // values rather than one. If a locked range skipped its draws, ticking
        // one lock would silently change everything sampled after it.
        var a = Ranges();
        var b = Ranges();
        b.spawnCount = new RandomRange(a.spawnCount.min, a.spawnCount.max, locked: true);

        for (int seed = 0; seed < 60; seed++)
        {
            var ra = BackdropRandomizer.Randomize(a, BackdropParameters.Default, new System.Random(seed));
            var rb = BackdropRandomizer.Randomize(b, BackdropParameters.Default, new System.Random(seed));

            Assert.AreEqual(ra.waveAmplitude, rb.waveAmplitude, 1e-5f, $"seed {seed} waveAmplitude");
            Assert.AreEqual(ra.flashDecay, rb.flashDecay, 1e-5f, $"seed {seed} flashDecay");
            Assert.AreEqual(ra.domain, rb.domain, $"seed {seed} domain");
        }
    }

    [Test]
    public void Randomize_StillNeverProducesAnInvisibleFieldWithExtremesOn()
    {
        var r = Ranges();
        r.extremeChance = 1f;

        for (int seed = 0; seed < 300; seed++)
        {
            var p = BackdropRandomizer.Randomize(r, BackdropParameters.Default, new System.Random(seed));
            Assert.Greater(p.spawnCount, 0, $"seed {seed}");
            Assert.Greater(p.scaleRange.y, 0f, $"seed {seed}");
            Assert.LessOrEqual(p.spawnCount, BackdropParameters.MaxSpawnCount, $"seed {seed}");
        }
    }
}