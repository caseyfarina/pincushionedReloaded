using UnityEngine;

/// <summary>Which shape the instances are distributed evenly across.</summary>
/// <summary>
/// Which shape the instances are distributed evenly across.
///
/// The first three fill the frame; the rest occupy a region of it and leave the
/// remainder clear, so the backdrop divides the composition rather than backing
/// all of it. New entries are appended, never inserted - the values are
/// serialized in scenes and presets.
/// </summary>
public enum BackdropDomain
{
    Plane = 0,
    Hemisphere = 1,
    Cube = 2,

    /// <summary>Annulus in the screen plane. The void in the middle is where a performer goes.</summary>
    Arch = 3,
    /// <summary>Rhythmic vertical bands with gaps between them.</summary>
    Colonnade = 4,
    /// <summary>A ground grid whose heights come from a noise field - a city or a ridgeline.</summary>
    Skyline = 5,
    /// <summary>The cube shell minus its front and back faces, so every edge is a perspective line.</summary>
    Corridor = 6,
}

/// <summary>
/// Which shading model BackdropShading.shadergraph branches to. Three
/// genuinely different models rather than three parameter sets, which is why
/// they are branched inside one graph rather than being three Materials:
/// a VFX mesh output binds its material at author time and cannot be
/// reassigned at runtime.
/// </summary>
public enum BackdropShadingMode { Toon = 0, FresnelToon = 1, Lit = 2 }

/// <summary>How instances move between their lattice point and wherever they are now.</summary>
public enum BackdropMotion
{
    /// <summary>One shared sine, phase-spread across the field. Reads as a wave passing through.</summary>
    Sine = 0,
    /// <summary>A 3D noise field sampled at each instance's position. Reads as drift or turbulence.</summary>
    Noise = 1,
}

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

    [Header("Camera fit")]
    [Tooltip("Size the field to the camera's view instead of to absolute units, and sit it in front of the camera facing back. A backdrop's job is to fill the frame, and the frame is what changes when the aspect or the lens does.")]
    public bool fitToCamera;

    [Tooltip("How far past the frame edge the field extends. 1 = exactly fills, above 1 bleeds off-screen so no edge is visible when the camera moves.")]
    public Vector2 fitMargin;

    [Tooltip("Distance from the camera to the near face of the field. Depth runs away from the camera from there, so the backdrop never swallows the subject.")]
    [Min(0.1f)] public float fitDistance;

    [Tooltip("Shifts the field within the frame, in fractions of frame width and height. Every domain is otherwise centred on the camera axis and can only divide the frame symmetrically; this is what puts a colonnade in the right third and leaves the left clear.")]
    public Vector2 frameOffset;

    [Header("Region shapes")]
    [Tooltip("Arch only: the hole, as a fraction of the outer radius. 0 fills the disc; 0.5 leaves half the width open.")]
    [Range(0f, 0.95f)] public float archInnerRadius;

    [Tooltip("Colonnade only: how many bands across the frame.")]
    [Range(1, 24)] public int colonnadeCount;

    [Tooltip("Colonnade only: how much of each band's slot is filled. 1 closes the gaps into a solid wall.")]
    [Range(0.05f, 1f)] public float colonnadeWidth;

    [Tooltip("Skyline only: how much of the domain height the tallest points reach.")]
    [Range(0f, 1f)] public float skylineHeight;

    [Tooltip("Skyline only: spatial frequency of the height field. Low is rolling hills, high is a jagged city.")]
    [Min(0.001f)] public float skylineRoughness;

    [Tooltip("Skyline only: flips the field to hang from the top instead of standing on the ground.")]
    public bool skylineInverted;
    [Tooltip("Cube domain only: fill the volume rather than the shell.")]
    public bool solidFill;

    [Header("Occupancy")]
    [Tooltip("Fraction of lattice slots that actually get an instance. Below 1 opens gaps. A fully populated lattice always reads as a grid; holes are what make it read as structure. Thinning filters a fixed lattice rather than rebuilding a smaller one, so lowering this removes instances without moving the ones that remain.")]
    [Range(0f, 1f)] public float occupancy;

    [Tooltip("Direction the density falls off across the field. Zero disables the gradient.")]
    public Vector3 driftDirection;

    [Tooltip("How completely the density fades along driftDirection. 1 empties the far end entirely; the field dissolves rather than stopping, so it weights the frame without drawing a line across it.")]
    [Range(0f, 1f)] public float driftAmount;

    [Tooltip("0 scatters the gaps evenly. Above 0 clusters them through a noise field, so the holes group into voids and the instances into clumps - far more building-like than even speckle. Higher values make smaller clusters.")]
    [Min(0f)] public float occupancyNoiseScale;

    [Header("Per-instance variation")]
    [Tooltip("Uniform scale multiplier, min to max.")]
    public Vector2 scaleRange;
    [Tooltip("Per-axis multiplier applied after the uniform scale. (1,1,1) = untouched.")]
    public Vector3 scaleAxisBias;

    [Tooltip("Fraction of instances promoted to a larger size class. A field drawn from one continuous size range has no hierarchy - everything reads as the same object at different distances. A minority at a fixed ratio above the rest gives the eye landmarks to read the field against.")]
    [Range(0f, 1f)] public float accentFraction;

    [Tooltip("What an accented instance is multiplied by. 1.618 is the golden ratio, so each size class relates to the next the way a Fibonacci term does; 2 or 3 give a blockier, more deliberate hierarchy.")]
    [Min(0.01f)] public float accentRatio;

    [Tooltip("How many ratio steps an accent can climb. 1 gives two size classes, 2 gives three, and so on - a short Fibonacci series across the field rather than a single jump.")]
    [Range(1, 4)] public int accentSteps;
    [Tooltip("Random displacement off the lattice point, metres per axis.")]
    public Vector3 offsetJitter;
    [Tooltip("Random rotation, degrees per axis.")]
    public Vector3 rotationJitter;

    [Tooltip("Orients each instance to the shape it sits on instead of to the world: radially on the arch, outward on the dome, along the face normal on the cube and corridor. The box-like shapes have no meaningful orientation of their own, so it reads as axis-aligned there. Rotation jitter still applies on top.")]
    public bool alignToShape;

    [Header("Animation")]
    [Tooltip("Degrees per second, min to max. Negative reverses.")]
    public Vector2 spinRateRange;
    public Vector3 spinAxis;
    public BackdropMotion motion;
    [Tooltip("Sine mode only: the direction instances travel. Noise mode displaces in all three axes.")]
    public Vector3 waveAxis;
    public float waveAmplitude;
    [Tooltip("Sine mode: cycles per second. Noise mode: how fast the field drifts.")]
    public float waveFrequency;
    [Tooltip("Noise mode only: spatial frequency of the field. Low values move whole regions together; high values make neighbours move independently.")]
    [Min(0.001f)] public float noiseScale;
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
        spawnCount      = 700,
        domain          = BackdropDomain.Cube,
        domainSize      = new Vector3(60f, 30f, 22f),
        solidFill       = false,

        occupancy           = 1f,
        occupancyNoiseScale = 0f,

        fitToCamera     = true,
        fitMargin       = new Vector2(1.1f, 1.1f),
        fitDistance     = 26f,
        frameOffset     = Vector2.zero,

        archInnerRadius  = 0.4f,
        colonnadeCount   = 6,
        colonnadeWidth   = 0.45f,
        skylineHeight    = 0.7f,
        skylineRoughness = 0.35f,
        skylineInverted  = false,

        driftDirection   = Vector3.zero,
        driftAmount      = 0f,

        scaleRange      = new Vector2(0.7f, 2.4f),
        scaleAxisBias   = new Vector3(1f, 3f, 1f),
        accentFraction  = 0.1f,
        accentRatio     = 1.618034f,
        accentSteps     = 1,
        offsetJitter    = new Vector3(0.5f, 0.5f, 0.5f),
        rotationJitter  = new Vector3(0f, 180f, 0f),
        alignToShape    = false,

        spinRateRange   = new Vector2(-8f, 8f),
        spinAxis        = Vector3.up,
        motion          = BackdropMotion.Sine,
        waveAxis        = Vector3.up,
        waveAmplitude   = 0.75f,
        waveFrequency   = 0.15f,
        noiseScale      = 0.05f,
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

        // A margin at or below zero collapses the field to nothing, which reads
        // as the backdrop having broken rather than as a parameter being wrong.
        c.fitMargin = new Vector2(
            Mathf.Clamp(c.fitMargin.x, 0.05f, 8f),
            Mathf.Clamp(c.fitMargin.y, 0.05f, 8f));
        c.fitDistance = Mathf.Max(0.1f, c.fitDistance);

        c.scaleRange    = Ordered(c.scaleRange, 0f);
        c.spinRateRange = Ordered(c.spinRateRange, float.NegativeInfinity);

        c.offsetJitter   = Abs(c.offsetJitter);
        c.rotationJitter = Abs(c.rotationJitter);

        c.spinAxis = c.spinAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.spinAxis.normalized;
        c.waveAxis = c.waveAxis.sqrMagnitude < 1e-6f ? Vector3.up : c.waveAxis.normalized;

        c.waveFrequency   = Mathf.Max(0f, c.waveFrequency);
        c.noiseScale      = Mathf.Max(0.001f, c.noiseScale);

        c.accentFraction = Mathf.Clamp01(c.accentFraction);
        c.accentRatio    = Mathf.Max(0.01f, c.accentRatio);
        c.accentSteps    = Mathf.Clamp(c.accentSteps, 1, 4);

        c.archInnerRadius  = Mathf.Clamp(c.archInnerRadius, 0f, 0.95f);
        c.colonnadeCount   = Mathf.Clamp(c.colonnadeCount, 1, 24);
        c.colonnadeWidth   = Mathf.Clamp(c.colonnadeWidth, 0.05f, 1f);
        c.skylineHeight    = Mathf.Clamp01(c.skylineHeight);
        c.skylineRoughness = Mathf.Max(0.001f, c.skylineRoughness);
        c.driftAmount      = Mathf.Clamp01(c.driftAmount);

        c.occupancy           = Mathf.Clamp01(c.occupancy);
        c.occupancyNoiseScale = Mathf.Max(0f, c.occupancyNoiseScale);
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