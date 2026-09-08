using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Volume parameters for the Red Giant style chromatic displacement.
///
/// The image displaces itself: its own luminance is shaped by a levels stage,
/// blurred, and then read as a HEIGHT FIELD. Pixels slide along the gradient of
/// that field, and the sample offset is swept across a spectrum to separate the
/// colours.
///
/// The gradient part is what makes it look like refraction rather than a smear,
/// and it has a consequence worth knowing while tuning: displacement is
/// strongest at the EDGES of bright regions, not in their middles. A large
/// uniformly bright area is flat, so it barely moves; its rim moves most.
/// </summary>
[Serializable]
[VolumeComponentMenu("Post-processing/Chromatic Displacement")]
[SupportedOnRenderPipeline(typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset))]
public class ChromaticDisplacementVolume : VolumeComponent, IPostProcessComponent
{
    [Header("Master")]
    [Tooltip("Overall effect amount. At 0 the passes are skipped entirely, so it " +
             "costs nothing when dialled out.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [Header("Displacement")]
    [Tooltip("How far pixels slide along the luminance gradient, in UV units. " +
             "Aspect-corrected, so it stays consistent on any viewport shape.")]
    public ClampedFloatParameter strength = new ClampedFloatParameter(0.02f, 0f, 0.25f);

    [Tooltip("Slope shaping applied AFTER the blur. This is the most expressive " +
             "single dial: the gradient is what drives displacement, so changing " +
             "the slope changes the character rather than just the amount. " +
             ">1 concentrates motion at bright rims, <1 spreads it out.")]
    public ClampedFloatParameter heightGamma = new ClampedFloatParameter(1f, 0.2f, 4f);

    [Header("Chromatic separation")]
    [Tooltip("How far apart the colour samples are spread along the displacement " +
             "vector. This is the rainbow width.")]
    public ClampedFloatParameter spread = new ClampedFloatParameter(0.35f, 0f, 1f);

    [Tooltip("Samples across the spectrum. 3 is cheap RGB fringing; 8-12 gives a " +
             "true rainbow. Cost is linear in this number and it is the most " +
             "expensive parameter in the effect.")]
    public ClampedIntParameter spectralSamples = new ClampedIntParameter(8, 3, 16);

    [Tooltip("Push the spectrum away from a neutral rainbow toward the two tint " +
             "colours below - the equivalent of Red Giant's Chroma Tint.")]
    public ClampedFloatParameter tintAmount = new ClampedFloatParameter(0f, 0f, 1f);

    public ColorParameter tintA = new ColorParameter(new Color(0.1f, 0.5f, 1f), true, false, true);
    public ColorParameter tintB = new ColorParameter(new Color(1f, 0.85f, 0.2f), true, false, true);

    [Header("Source blur")]
    [Tooltip("Blur radius as a fraction of SCREEN HEIGHT, not pixels. Resolution " +
             "independent on purpose: split-screen cells are different sizes, and a " +
             "pixel radius would give every cell a different look.")]
    public ClampedFloatParameter blurRadius = new ClampedFloatParameter(0.01f, 0f, 0.08f);

    [Tooltip("Extra blur iterations. Each doubles the effective radius for very " +
             "little cost, because it runs at reduced resolution.")]
    public ClampedIntParameter blurIterations = new ClampedIntParameter(2, 1, 4);

    [Header("Levels (on the displacement source)")]
    [Tooltip("Input black point. Luminance at or below this becomes 0.")]
    public ClampedFloatParameter inputBlack = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Input white point. Luminance at or above this becomes 1.")]
    public ClampedFloatParameter inputWhite = new ClampedFloatParameter(1f, 0f, 1f);

    [Tooltip("Midtone gamma, Substance-style: >1 lifts midtones.")]
    public ClampedFloatParameter gamma = new ClampedFloatParameter(1f, 0.1f, 5f);

    [Tooltip("Output black point.")]
    public ClampedFloatParameter outputBlack = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Output white point.")]
    public ClampedFloatParameter outputWhite = new ClampedFloatParameter(1f, 0f, 1f);

    [Header("Debug")]
    [Tooltip("Show the blurred, levelled height field instead of the displaced " +
             "image. The fastest way to tune levels and blur -- you are looking " +
             "directly at what drives the distortion.")]
    public BoolParameter viewHeightField = new BoolParameter(false);

    public bool IsActive() => intensity.value > 0f && strength.value > 0f;

    // Required by IPostProcessComponent in older URP versions; harmless here.
    public bool IsTileCompatible() => false;
}
