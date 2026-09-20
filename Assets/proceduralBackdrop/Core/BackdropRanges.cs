using UnityEngine;

/// <summary>
/// One randomisable scalar: the interval it is sampled from, plus a lock.
/// Locked parameters are passed through from the base value untouched, which
/// is what lets a search converge — settle the scale range, lock it, and
/// randomise only what is still open.
/// </summary>
[System.Serializable]
public struct RandomRange
{
    public bool locked;
    public float min;
    public float max;

    public RandomRange(float min, float max, bool locked = false)
    {
        this.min = min; this.max = max; this.locked = locked;
    }

    public float Span => Mathf.Abs(max - min);
}

/// <summary>
/// The box BackdropRandomizer samples inside. Separate from BackdropPresetBook
/// on purpose: the book holds points in the space, this defines the space.
///
/// spawnCount and domainSize ranges are also the performance rail — the
/// expected operating scale is hundreds of instances, and an unbounded
/// randomiser would happily hand the graph tens of thousands across eight
/// split-screen cells.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Ranges", fileName = "BackdropRanges")]
public class BackdropRanges : ScriptableObject
{
    [Header("Randomise character")]

    [Tooltip("Chance a parameter lands exactly on an end of its range rather than somewhere inside it. " +
             "Uniform sampling across ~20 parameters almost never puts several of them near an extreme, " +
             "so every roll comes out mid-range and the results all read alike. Snapping to the ends is " +
             "what produces a field that is decisively squat, or tall, or still.")]
    [Range(0f, 1f)] public float extremeChance = 0.4f;

    [Tooltip("Of the rolls that land on an end, how often it is the maximum. Below 0.5 favours sparse, small, slow; above favours dense, large, fast.")]
    [Range(0f, 1f)] public float maxVsMin = 0.5f;

    [Tooltip("Chance a parameter is left at its current value instead of being redrawn. " +
             "With every parameter redrawn at once, consecutive rolls have nothing in common and a look " +
             "can never be developed - only stumbled on. Holding a few keeps each roll recognisably " +
             "related to the last.")]
    [Range(0f, 1f)] public float holdChance = 0.15f;

    [Header("Layout")]
    public bool randomiseDomain = true;
    public RandomRange spawnCount = new RandomRange(250, 1200);
    public RandomRange occupancy = new RandomRange(0.35f, 1f);
    public RandomRange occupancyNoiseScale = new RandomRange(0f, 0.12f);
    [Tooltip("Ignored while fitToCamera is on - X and Y come from the frame.")]
    public RandomRange domainSizeX = new RandomRange(30f, 90f);
    [Tooltip("Ignored while fitToCamera is on - X and Y come from the frame.")]
    public RandomRange domainSizeY = new RandomRange(15f, 60f);
    [Tooltip("Depth, away from the camera. Authored even when fitting, since the frame cannot imply it.")]
    public RandomRange domainSizeZ = new RandomRange(8f, 60f);
    public bool randomiseSolidFill = true;

    [Header("Per-instance variation")]
    public RandomRange scaleMin = new RandomRange(0.2f, 1.2f);
    public RandomRange scaleMax = new RandomRange(1.0f, 4.0f);
    [Tooltip("How far the dominant axis is stretched when a roll decides the field is not uniform. One range, not three: the axis is chosen separately, so this is the magnitude of whatever proportion was picked.")]
    public RandomRange scaleBias = new RandomRange(1.5f, 8f);

    [Tooltip("Chance a roll leaves the proportions uniform. The rest of the time one axis is picked and stretched, so the field comes out decisively towers, or slabs, or fins - rather than the mush three independent draws produce.")]
    [Range(0f, 1f)] public float uniformProportionChance = 0.3f;

    [Tooltip("Chance a roll aligns instances to the shape instead of rotating them freely. Two clear outcomes - ordered or tumbled - rather than a partial rotation on each axis that reads as neither.")]
    [Range(0f, 1f)] public float alignChance = 0.45f;
    public RandomRange accentFraction = new RandomRange(0f, 0.3f);
    public RandomRange accentRatio = new RandomRange(1.3f, 2.2f);
    public RandomRange offsetJitter = new RandomRange(0f, 3f);
    public RandomRange rotationJitter = new RandomRange(0f, 180f);

    [Header("Animation")]
    public RandomRange spinRate = new RandomRange(0f, 45f);
    public RandomRange waveAmplitude = new RandomRange(0f, 3f);
    public RandomRange waveFrequency = new RandomRange(0.02f, 0.6f);
    public RandomRange wavePhaseSpread = new RandomRange(0f, 1f);
    public RandomRange noiseScale = new RandomRange(0.01f, 0.25f);
    [Tooltip("Sine or Noise motion. Off pins whichever mode is already set.")]
    public bool randomiseMotion = true;

    [Header("Look")]
    public bool randomiseShading = true;
    public RandomRange flashDecay = new RandomRange(1.5f, 10f);
    public RandomRange flashIntensity = new RandomRange(2f, 20f);
    public RandomRange flashRipple = new RandomRange(0f, 0.8f);
    public bool randomiseEmissionHue = true;
    public RandomRange emissionSaturation = new RandomRange(0f, 0.8f);

    public static BackdropRanges CreateDefault() => CreateInstance<BackdropRanges>();
}