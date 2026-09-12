using UnityEngine;

/// <summary>Which shape the instances are distributed evenly across.</summary>
public enum BackdropDomain { Plane = 0, Hemisphere = 1, Cube = 2 }

/// <summary>
/// Which shading model BackdropShading.shadergraph branches to. Three
/// genuinely different models rather than three parameter sets, which is why
/// they are branched inside one graph rather than being three Materials:
/// a VFX mesh output binds its material at author time and cannot be
/// reassigned at runtime.
/// </summary>
public enum BackdropShadingMode { Toon = 0, FresnelToon = 1, Lit = 2 }

/// <summary>
/// Every value the backdrop has. One struct, because it is simultaneously the
/// inspector surface, the unit of preset save/recall, the thing the randomiser
/// produces, and the entry in the undo history. Anything that is not in here
/// cannot be saved as a preset or randomised, so new parameters belong here
/// rather than as loose fields on the instrument.
///
/// Mirrors the exposed properties of Backdrop.vfx one-for-one. If you add a
/// field, add the matching exposed property in the graph and push it in
/// BackdropInstrument.Apply.
/// </summary>
[System.Serializable]
public struct BackdropParameters
{
    /// <summary>
    /// Hard ceiling on instances. Expected operating scale is hundreds; this
    /// exists so a runaway randomiser cannot hand the graph tens of thousands.
    /// Raising it is a deliberate act followed by a build measurement.
    /// </summary>
    public const int MaxSpawnCount = 4000;

    [Header("Layout")]
    public uint layoutSeed;
    [Min(0)] public int spawnCount;
    public BackdropDomain domain;
    public Vector3 domainSize;
    [Tooltip("Cube domain only: fill the volume rather than the shell.")]
    public bool solidFill;

    [Header("Per-instance variation")]
    [Tooltip("Uniform scale multiplier, min to max.")]
    public Vector2 scaleRange;
    [Tooltip("Per-axis multiplier applied after the uniform scale. (1,1,1) = untouched.")]
    public Vector3 scaleAxisBias;
    [Tooltip("Random displacement off the lattice point, metres per axis.")]
    public Vector3 offsetJitter;
    [Tooltip("Random rotation, degrees per axis.")]
    public Vector3 rotationJitter;

    [Header("Animation")]
    [Tooltip("Degrees per second, min to max. Negative reverses.")]
    public Vector2 spinRateRange;
    public Vector3 spinAxis;
    public Vector3 waveAxis;
    public float waveAmplitude;
    public float waveFrequency;
    [Tooltip("0 = every instance waves in unison, 1 = phases spread over a full cycle.")]
    [Range(0f, 1f)] public float wavePhaseSpread;

    [Header("Look")]
    public BackdropShadingMode shading;
    public Color emissionColor;
    [Tooltip("Flash decay rate. Higher = shorter flash.")]
    [Min(0.01f)] public float flashDecay;
    [Min(0f)] public float flashIntensity;
    [Tooltip("Seconds of delay per unit of normalised instance ID, so the flash ripples rather than blinking flat.")]
    [Min(0f)] public float flashRipple;

    public static BackdropParameters Default => new BackdropParameters
    {
        layoutSeed      = 1u,
        spawnCount      = 300,
        domain          = BackdropDomain.Cube,
        domainSize      = new Vector3(60f, 30f, 60f),
        solidFill       = false,

        scaleRange      = new Vector2(0.6f, 1.8f),
        scaleAxisBias   = new Vector3(1f, 3f, 1f),
        offsetJitter    = new Vector3(0.5f, 0.5f, 0.5f),
        rotationJitter  = new Vector3(0f, 180f, 0f),

        spinRateRange   = new Vector2(-8f, 8f),
        spinAxis        = Vector3.up,
        waveAxis        = Vector3.up,
        waveAmplitude   = 0.75f,
        waveFrequency   = 0.15f,
        wavePhaseSpread = 1f,

        shading         = BackdropShadingMode.Toon,
        emissionColor   = Color.white,
        flashDecay      = 4f,
        flashIntensity  = 8f,
        flashRipple     = 0.3f,
    };

    /// <summary>
    /// Returns a copy with every value forced into a range the graph can render.
    /// Called on every write path, so neither the inspector, the randomiser, nor
    /// a hand-edited preset asset can push the graph somewhere it cannot recover
    /// from. Inverted min/max pairs are ordered rather than rejected, because an
    /// inverted range is a plausible authoring slip with an obvious intent.
    /// </summary>
    public BackdropParameters Clamped()
    {
        var c = this;

        c.spawnCount = Mathf.Clamp(c.spawnCount, 0, MaxSpawnCount);

        c.domainSize = new Vector3(
            Mathf.Max(0.01f, c.domainSize.x),
            Mathf.Max(0.01f, c.domainSize.y),
            Mathf.Max(0.01f, c.domainSize.z));

        c.scaleRange    = Ordered(c.scaleRange, 0f);
        c.spinRateRange = Ordered(c.spinRateRange, float.NegativeInfinity);

        c.offsetJitter   = Abs(c.offsetJitter);
        c.rotationJitter = Abs(c.rotationJitter);

        c.spinAxis = c.spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.spinAxis.normalized;
        c.waveAxis = c.waveAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.waveAxis.normalized;

        c.waveFrequency   = Mathf.Max(0f, c.waveFrequency);
        c.wavePhaseSpread = Mathf.Clamp01(c.wavePhaseSpread);

        c.flashDecay     = Mathf.Max(0.01f, c.flashDecay);
        c.flashIntensity = Mathf.Max(0f, c.flashIntensity);
        c.flashRipple    = Mathf.Max(0f, c.flashRipple);

        return c;
    }

    private static Vector2 Ordered(Vector2 v, float floor)
    {
        float a = Mathf.Max(floor, v.x);
        float b = Mathf.Max(floor, v.y);
        return a <= b ? new Vector2(a, b) : new Vector2(b, a);
    }

    private static Vector3 Abs(Vector3 v)
        => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}