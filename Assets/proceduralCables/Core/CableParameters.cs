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
    [Tooltip("Wander in the head's flight path. Separate from cableNoise: this curves the route, that animates the cable once it arrives.")]
    public float pathNoise;

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
    [Tooltip("Ribbon half-width in world units.")]
    public float thickness;
    [Tooltip("Scale applied to the connector mesh.")]
    public float headScale;
    [Tooltip("Palette. Each cable seeds its own pick.")]
    public Color[] colors;

    [Header("Targeting")]
    [Tooltip("Landing points scatter on a sphere of this radius around the target Transform.")]
    public float targetScatterRadius;

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
        slack = 1.5f,
        cableNoise = 0.35f,
        noiseScale = 2.5f,
        driftSpeed = 0.25f,
        shiverDecay = 3.5f,
        shiverFreq = 14f,
        thickness = 0.05f,
        headScale = 1f,
        targetScatterRadius = 1.5f,
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
