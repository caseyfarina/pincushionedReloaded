using UnityEngine;

/// <summary>
/// Every value a cable is drawn from, in one struct. This is the Inspector
/// surface, the unit a preset would store, and the input a test constructs -
/// keeping them the same type is what stops the three drifting apart.
/// </summary>
[System.Serializable]
public struct CableParameters
{
    [Tooltip("Stream seed. Every cable derives its own sub-seed from this.")]
    public uint seed;

    [Header("Flight")]
    [Tooltip("World units per second. Flight duration is distance / cableSpeed, so a distant target takes proportionally longer.")]
    public float cableSpeed;
    [Tooltip("Easing of the head along its path. 0 is linear; higher decelerates harder into the target. Not drag - nothing is integrated.")]
    public float deceleration;
    [Tooltip("Extra cable noise while in flight, on top of cableNoise. Makes a flying cable whip; does not bend its route - that is pathCurl.")]
    public float pathNoise;
    [Tooltip("How far the flight detours off the straight line, in world units. 0 flies direct. The detour closes to zero at both ends, so the cable still lands exactly on its target.")]
    public float pathCurl;

    [Header("Shape")]
    [Tooltip("Droop toward world down at the cable's midpoint, in world units.")]
    public float slack;
    [Tooltip("Amplitude of the noise along the cable's length. Windowed to zero at both ends.")]
    public float cableNoise;
    [Tooltip("Feature size of that noise. Higher is busier.")]
    public float noiseScale;
    [Tooltip("How fast the idle sway breathes. Never reaches zero, so a settled nest keeps moving.")]
    public float driftSpeed;

    [Header("Impact")]
    [Tooltip("How fast the landing shiver dies away.")]
    public float shiverDecay;
    [Tooltip("Frequency of the landing shiver.")]
    public float shiverFreq;

    [Header("Look")]
    [Tooltip("Ribbon half-width in world units. The base width - each cable picks its own multiple of it.")]
    public float thickness;
    [Tooltip("Largest multiple of thickness a cable may draw. 1 makes every cable the same width; 5 means the fattest is five times the thinnest.")]
    public float thicknessVariation;
    [Tooltip("Scale applied to the connector mesh.")]
    public float headScale;
    [Tooltip("Palette. Each cable seeds its own pick.")]
    public Color[] colors;

    [Header("Patch bay")]
    [Tooltip("Ports across the bay.")]
    public int patchColumns;
    [Tooltip("Ports down the bay.")]
    public int patchRows;
    [Tooltip("Distance between ports across, in the target's local units.")]
    public float columnSpacing;
    [Tooltip("Distance between ports down, in the target's local units.")]
    public float rowSpacing;

    [Header("Targeting")]
    [Tooltip("Landing points scatter on a sphere of this radius around the target Transform.")]
    public float targetScatterRadius;

    [Header("Lifetime")]
    [Tooltip("Seconds a cable hangs after it lands before retiring. 0 means it never retires and only the cap removes it.")]
    public float lifetime;

    [Header("Budget")]
    [Tooltip("Nodes per cable. Two gives a straight segment; higher resolves the sag and noise.")]
    public int nodesPerCable;
    [Tooltip("Live cables. At the cap the oldest retires by easing its thickness to zero.")]
    public int cableCap;

    public static CableParameters Default => new CableParameters
    {
        seed = 1u,
        cableSpeed = 18f,
        deceleration = 1.5f,
        pathNoise = 1.2f,
        pathCurl = 0f,
        slack = 1.5f,
        cableNoise = 0.35f,
        noiseScale = 2.5f,
        driftSpeed = 0.25f,
        shiverDecay = 3.5f,
        shiverFreq = 14f,
        thickness = 0.05f,
        thicknessVariation = 1f,
        headScale = 1f,
        patchColumns = 10,
        patchRows = 3,
        columnSpacing = 1.5f,
        rowSpacing = 1.2f,
        targetScatterRadius = 1.5f,
        lifetime = 0f,
        nodesPerCable = 24,
        cableCap = 64,
        colors = new[]
        {
            new Color(0.90f, 0.15f, 0.15f), // red
            new Color(0.95f, 0.55f, 0.10f), // orange
            new Color(0.95f, 0.85f, 0.15f), // yellow
            new Color(0.20f, 0.70f, 0.30f), // green
            new Color(0.15f, 0.40f, 0.85f), // blue
            new Color(0.55f, 0.25f, 0.75f), // violet
        },
    };
}
