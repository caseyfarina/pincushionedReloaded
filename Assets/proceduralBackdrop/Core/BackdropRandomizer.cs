using UnityEngine;

/// <summary>
/// Turns a BackdropRanges box into concrete BackdropParameters. Pure, seeded
/// and free of Unity runtime dependencies, which makes it the one part of the
/// backdrop system with a real automated test surface — everything downstream
/// is a GPU shader judged by eye.
///
/// Randomize resamples the whole space; Mutate perturbs an existing point by a
/// fraction of each range's span. Both honour locks. The pair is the point of
/// the exploration rig: Randomize finds a region, Mutate refines inside it.
/// </summary>
public static class BackdropRandomizer
{
    /// <summary>Full resample. Locked parameters come through from basis untouched.</summary>
    public static BackdropParameters Randomize(
        BackdropRanges r, BackdropParameters basis, System.Random rng)
    {
        return Sample(r, basis, rng, strength: 1f, mutate: false).Clamped();
    }

    /// <summary>
    /// Perturbs basis by `strength` of each range's span. strength 0 returns
    /// basis unchanged; strength 1 moves up to a full span in either direction
    /// but is still anchored on basis, so it is not the same as Randomize.
    /// </summary>
    public static BackdropParameters Mutate(
        BackdropRanges r, BackdropParameters basis, float strength, System.Random rng)
    {
        return Sample(r, basis, rng, Mathf.Max(0f, strength), mutate: true).Clamped();
    }

    private static BackdropParameters Sample(
        BackdropRanges r, BackdropParameters basis, System.Random rng, float strength, bool mutate)
    {
        var p = basis;
        if (r == null) return p;

        // Draw order is fixed and never conditional on a lock, so locking one
        // parameter does not shift the values every later parameter receives.
        // Without this, ticking a lock would silently change the whole result.

        float spawn        = Draw(r.spawnCount, basis.spawnCount, rng, strength, mutate, r);
        float sizeX        = Draw(r.domainSizeX, basis.domainSize.x, rng, strength, mutate, r);
        float sizeY        = Draw(r.domainSizeY, basis.domainSize.y, rng, strength, mutate, r);
        float sizeZ        = Draw(r.domainSizeZ, basis.domainSize.z, rng, strength, mutate, r);
        float sMin         = Draw(r.scaleMin, basis.scaleRange.x, rng, strength, mutate, r);
        float sMax         = Draw(r.scaleMax, basis.scaleRange.y, rng, strength, mutate, r);
        // One magnitude, not three. Which axis it lands on is decided below.
        float biasMag      = Draw(r.scaleBias, MaxComponent(basis.scaleAxisBias), rng, strength, mutate, r);
        float occ          = Draw(r.occupancy,          basis.occupancy,          rng, strength, mutate, r);
        float occNoise     = Draw(r.occupancyNoiseScale, basis.occupancyNoiseScale, rng, strength, mutate, r);
        float accFrac      = Draw(r.accentFraction,     basis.accentFraction,     rng, strength, mutate, r);
        float accRatio     = Draw(r.accentRatio,        basis.accentRatio,        rng, strength, mutate, r);
        float nScale       = Draw(r.noiseScale,         basis.noiseScale,         rng, strength, mutate, r);
        float jitter       = Draw(r.offsetJitter, basis.offsetJitter.x, rng, strength, mutate, r);
        float rotJitter    = Draw(r.rotationJitter, MaxComponent(basis.rotationJitter), rng, strength, mutate, r);
        float spin         = Draw(r.spinRate, Mathf.Abs(basis.spinRateRange.y), rng, strength, mutate, r);
        float waveAmp      = Draw(r.waveAmplitude, basis.waveAmplitude, rng, strength, mutate, r);
        float waveFreq     = Draw(r.waveFrequency, basis.waveFrequency, rng, strength, mutate, r);
        float wavePhase    = Draw(r.wavePhaseSpread, basis.wavePhaseSpread, rng, strength, mutate, r);
        float fDecay       = Draw(r.flashDecay, basis.flashDecay, rng, strength, mutate, r);
        float fIntensity   = Draw(r.flashIntensity, basis.flashIntensity, rng, strength, mutate, r);
        float fRipple      = Draw(r.flashRipple, basis.flashRipple, rng, strength, mutate, r);
        float emissionSat  = Draw(r.emissionSaturation, 0.5f, rng, strength, mutate, r);

        // Discrete draws come last, again always consuming the same number of
        // rng values whether or not they are enabled.
        // Proportion and orientation are each one decision for the whole field.
        // Drawn unconditionally, like every other roll, so the draw count stays
        // independent of the outcome.
        float proportionRoll = (float)rng.NextDouble();
        float axisRoll       = (float)rng.NextDouble();
        float alignRoll      = (float)rng.NextDouble();

        int   motionRoll   = rng.Next(2);
        int   domainRoll   = rng.Next(3);
        int   shadingRoll  = rng.Next(3);
        bool  solidRoll    = rng.Next(2) == 0;
        float hueRoll      = (float)rng.NextDouble();

        // Discrete parameters honour holdChance too. Without it the domain and
        // shading flip on every single roll, which is the loudest change on
        // screen and drowns out whatever the continuous parameters just did.
        float holdD = (float)rng.NextDouble();
        float holdS = (float)rng.NextDouble();
        float holdF = (float)rng.NextDouble();
        float holdH = (float)rng.NextDouble();
        float holdM = (float)rng.NextDouble();
        float hold  = r.holdChance;
        uint  seedRoll     = unchecked((uint)rng.Next(1, int.MaxValue));

        p.spawnCount     = Mathf.RoundToInt(spawn);
        p.domainSize     = new Vector3(sizeX, sizeY, sizeZ);
        p.scaleRange     = new Vector2(Mathf.Min(sMin, sMax), Mathf.Max(sMin, sMax));
        p.offsetJitter   = new Vector3(jitter, jitter, jitter);

        // Proportion: uniform, or one axis stretched. Y is weighted heaviest
        // because standing forms read as architecture where a stretched X or Z
        // reads as debris - but all three stay reachable.
        if (mutate)
        {
            // Mutation scales whatever proportion is already there rather than
            // re-picking the axis, which would be a jump out of the region.
            float cur = MaxComponent(basis.scaleAxisBias);
            p.scaleAxisBias = cur > 1e-4f
                ? basis.scaleAxisBias * (biasMag / cur)
                : Vector3.one;
        }
        else if (proportionRoll < r.uniformProportionChance)
        {
            p.scaleAxisBias = Vector3.one;
        }
        else
        {
            p.scaleAxisBias = axisRoll < 0.55f ? new Vector3(1f, biasMag, 1f)
                            : axisRoll < 0.80f ? new Vector3(biasMag, 1f, 1f)
                                               : new Vector3(1f, 1f, biasMag);
        }

        // Orientation: aligned to the shape, or tumbled on all three axes. The
        // in-between - one axis jittered, the others not - is what made every
        // roll read the same, so it is no longer reachable by chance.
        if (!mutate)
        {
            bool aligned = alignRoll < r.alignChance;
            p.alignToShape = aligned;
            p.rotationJitter = aligned
                ? Vector3.zero
                : new Vector3(rotJitter, rotJitter, rotJitter);
        }
        else
        {
            p.rotationJitter = basis.rotationJitter.sqrMagnitude > 1e-6f
                ? new Vector3(rotJitter, rotJitter, rotJitter)
                : Vector3.zero;
        }
        p.spinRateRange  = new Vector2(-spin, spin);
        p.occupancy           = occ;
        p.occupancyNoiseScale = occNoise;
        p.accentFraction      = accFrac;
        p.accentRatio         = accRatio;
        p.noiseScale          = nScale;
        p.waveAmplitude  = waveAmp;
        p.waveFrequency  = waveFreq;
        p.wavePhaseSpread = wavePhase;
        p.flashDecay     = fDecay;
        p.flashIntensity = fIntensity;
        p.flashRipple    = fRipple;

        // A fresh arrangement is part of resampling the space: without it two
        // Space presses that land on the same count and domain produce the
        // identical field, which reads as a dead button. Mutation is the
        // opposite case — it refines a look, so the layout is held.
        if (!mutate) p.layoutSeed = seedRoll;

        // Discrete parameters are not mutated at low strength — flipping the
        // domain shape is a jump out of the region, not a refinement of it.
        bool allowDiscrete = !mutate || strength >= 0.5f;

        if (r.randomiseMotion && allowDiscrete && holdM >= hold)
            p.motion = (BackdropMotion)motionRoll;

        if (r.randomiseDomain && allowDiscrete && holdD >= hold)
            p.domain = (BackdropDomain)domainRoll;

        if (r.randomiseShading && allowDiscrete && holdS >= hold)
            p.shading = (BackdropShadingMode)shadingRoll;

        if (r.randomiseSolidFill && allowDiscrete && holdF >= hold)
            p.solidFill = solidRoll;

        if (r.randomiseEmissionHue && allowDiscrete && holdH >= hold)
            p.emissionColor = Color.HSVToRGB(hueRoll, Mathf.Clamp01(emissionSat), 1f);

        return p;
    }

    /// <summary>
    /// The dominant component of a vector parameter. Proportion and rotation are
    /// single decisions for the whole field, so when mutating from an existing
    /// value there is one magnitude to carry forward, not three.
    /// </summary>
    private static float MaxComponent(Vector3 v)
        => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    /// <summary>
    /// One scalar. A locked range returns the basis value but still consumes an
    /// rng draw, so locking a parameter changes only that parameter rather than
    /// reshuffling everything sampled after it.
    /// </summary>
    private static float Draw(
        RandomRange range, float basis, System.Random rng, float strength, bool mutate,
        BackdropRanges r)
    {
        // All three draws happen before any branch, for the same reason the lock
        // check sits below them: the number of rng values consumed per parameter
        // must not depend on the outcome, or one parameter's result would shift
        // every parameter drawn after it.
        float roll    = (float)rng.NextDouble();
        float modeRoll = (float)rng.NextDouble();
        float endRoll  = (float)rng.NextDouble();

        if (range.locked) return basis;

        float lo = Mathf.Min(range.min, range.max);
        float hi = Mathf.Max(range.min, range.max);

        if (mutate)
        {
            // Mutation refines a look in place. Snapping to an end would be a
            // jump out of the region, which is what Randomize is for.
            float delta = (roll * 2f - 1f) * strength * range.Span;
            return Mathf.Clamp(basis + delta, lo, hi);
        }

        float hold = r != null ? r.holdChance : 0f;
        float extreme = r != null ? r.extremeChance : 0f;
        float maxBias = r != null ? r.maxVsMin : 0.5f;

        // Hold: leave it where it is, so consecutive rolls stay related.
        // Clamped, because the range is the declared space and Randomize must
        // never hand back a value outside it - a held value that drifted out of
        // range via a MIDI knob would otherwise survive every future roll.
        if (modeRoll < hold) return Mathf.Clamp(basis, lo, hi);

        // End: commit to a limit. This is what uniform sampling almost never
        // does and what gives a roll its character.
        if (modeRoll < hold + extreme) return endRoll < maxBias ? hi : lo;

        return Mathf.Lerp(lo, hi, roll);
    }
}