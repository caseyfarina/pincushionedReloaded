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
    [Header("Layout")]
    public bool randomiseDomain = true;
    public RandomRange spawnCount = new RandomRange(250, 1200);
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
    public RandomRange scaleBiasX = new RandomRange(0.5f, 2f);
    public RandomRange scaleBiasY = new RandomRange(0.5f, 8f);
    public RandomRange scaleBiasZ = new RandomRange(0.5f, 2f);
    public RandomRange offsetJitter = new RandomRange(0f, 3f);
    public RandomRange rotationJitter = new RandomRange(0f, 180f);

    [Header("Animation")]
    public RandomRange spinRate = new RandomRange(0f, 45f);
    public RandomRange waveAmplitude = new RandomRange(0f, 3f);
    public RandomRange waveFrequency = new RandomRange(0.02f, 0.6f);
    public RandomRange wavePhaseSpread = new RandomRange(0f, 1f);

    [Header("Look")]
    public bool randomiseShading = true;
    public RandomRange flashDecay = new RandomRange(1.5f, 10f);
    public RandomRange flashIntensity = new RandomRange(2f, 20f);
    public RandomRange flashRipple = new RandomRange(0f, 0.8f);
    public bool randomiseEmissionHue = true;
    public RandomRange emissionSaturation = new RandomRange(0f, 0.8f);

    public static BackdropRanges CreateDefault() => CreateInstance<BackdropRanges>();
}