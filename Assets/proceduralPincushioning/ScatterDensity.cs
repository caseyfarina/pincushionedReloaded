using UnityEngine;
using System;

/// <summary>
/// Evaluates per-sample density weights for the scatter system.
/// Three modes: UV-mapped texture, world-space procedural noise, point attractors.
/// All modes resolve to a single float weight per sample point.
///
/// The weight serves dual purpose:
///   1. Selection probability bias (higher weight = more likely to be placed)
///   2. Scale modulation (weight multiplied into pin scale)
/// </summary>
[Serializable]
public class ScatterDensity
{
    public enum DensityMode
    {
        Uniform,
        Texture,
        Noise,
        Attractor
    }

    [Tooltip("Density evaluation mode.")]
    public DensityMode mode = DensityMode.Uniform;

    // ── Texture Mode ────────────────────────────────────────────────────
    [Tooltip("Grayscale density map. White = high density, black = zero.")]
    public Texture2D densityTexture;

    [Tooltip("UV channel index from the baked sample data (0 = primary UVs).")]
    public int uvChannel = 0;

    // ── Noise Mode ──────────────────────────────────────────────────────
    [Tooltip("Scale of the noise pattern. Smaller = larger blobs.")]
    [Range(0.01f, 50f)]
    public float noiseScale = 5f;

    [Tooltip("World-space offset for the noise pattern. Animate this to shift clusters.")]
    public Vector3 noiseOffset = Vector3.zero;

    [Tooltip("Number of octaves for fractal noise. More = finer detail.")]
    [Range(1, 6)]
    public int noiseOctaves = 3;

    [Tooltip("How much each octave contributes relative to the previous.")]
    [Range(0f, 1f)]
    public float noisePersistence = 0.5f;

    // ── Attractor Mode ──────────────────────────────────────────────────
    [Tooltip("Transforms that attract scattered instances. Closer = higher density.")]
    public Transform[] attractors;

    [Tooltip("Radius of influence per attractor in world units.")]
    [Min(0.01f)]
    public float attractorRadius = 2f;

    [Tooltip("Falloff curve. 1 = linear, 2 = quadratic (tighter clusters), 0.5 = sqrt (softer).")]
    [Range(0.1f, 5f)]
    public float attractorFalloff = 2f;

    [Tooltip("Whether areas outside all attractor radii get zero weight or a minimum floor.")]
    [Range(0f, 1f)]
    public float attractorFloor = 0.05f;

    // ── Global ──────────────────────────────────────────────────────────
    [Tooltip("Contrast curve applied after density evaluation. >1 sharpens peaks, <1 flattens.")]
    [Range(0.1f, 5f)]
    public float contrast = 1f;

    [Tooltip("Density values below this threshold are clamped to zero (culled).")]
    [Range(0f, 1f)]
    public float cutoff = 0f;

    [Tooltip("Invert the density map. High becomes low, low becomes high.")]
    public bool invert = false;

    // ═════════════════════════════════════════════════════════════════════
    // Evaluation
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluate density weight for a single sample point.
    /// Returns a value in [0, 1] after contrast/cutoff/invert processing.
    /// </summary>
    /// <param name="worldPos">World-space position of the sample.</param>
    /// <param name="uv">UV coordinate of the sample (used in Texture mode).</param>
    public float Evaluate(Vector3 worldPos, Vector2 uv)
    {
        float raw;

        switch (mode)
        {
            case DensityMode.Texture:
                raw = EvaluateTexture(uv);
                break;
            case DensityMode.Noise:
                raw = EvaluateNoise(worldPos);
                break;
            case DensityMode.Attractor:
                raw = EvaluateAttractor(worldPos);
                break;
            default: // Uniform
                return 1f;
        }

        // Post-processing chain
        if (invert) raw = 1f - raw;
        raw = Mathf.Pow(Mathf.Clamp01(raw), contrast);
        if (raw < cutoff) raw = 0f;

        return raw;
    }

    /// <summary>
    /// Batch-evaluate weights for all samples. Populates the provided array.
    /// More efficient than per-sample calls when texture reads are involved.
    /// </summary>
    public void EvaluateAll(
        Vector3[] worldPositions,
        Vector2[] uvs,
        float[] weightsOut,
        int count)
    {
        if (mode == DensityMode.Uniform)
        {
            for (int i = 0; i < count; i++)
                weightsOut[i] = 1f;
            return;
        }

        // For texture mode, grab pixel data once to avoid per-pixel GetPixelBilinear overhead
        Color[] texPixels = null;
        int texW = 0, texH = 0;
        if (mode == DensityMode.Texture && densityTexture != null)
        {
            texPixels = densityTexture.GetPixels();
            texW = densityTexture.width;
            texH = densityTexture.height;
        }

        for (int i = 0; i < count; i++)
        {
            float raw;

            switch (mode)
            {
                case DensityMode.Texture:
                    raw = texPixels != null
                        ? SampleTexturePixels(texPixels, texW, texH, uvs[i])
                        : 0f;
                    break;
                case DensityMode.Noise:
                    raw = EvaluateNoise(worldPositions[i]);
                    break;
                case DensityMode.Attractor:
                    raw = EvaluateAttractor(worldPositions[i]);
                    break;
                default:
                    raw = 1f;
                    break;
            }

            if (invert) raw = 1f - raw;
            raw = Mathf.Pow(Mathf.Clamp01(raw), contrast);
            if (raw < cutoff) raw = 0f;

            weightsOut[i] = raw;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Mode Implementations
    // ═════════════════════════════════════════════════════════════════════

    private float EvaluateTexture(Vector2 uv)
    {
        if (densityTexture == null) return 1f;
        Color c = densityTexture.GetPixelBilinear(uv.x, uv.y);
        return c.grayscale;
    }

    private static float SampleTexturePixels(Color[] pixels, int w, int h, Vector2 uv)
    {
        // Bilinear sample from raw pixel array
        float fx = uv.x * (w - 1);
        float fy = uv.y * (h - 1);

        int x0 = Mathf.Clamp((int)fx, 0, w - 1);
        int y0 = Mathf.Clamp((int)fy, 0, h - 1);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        float c00 = pixels[y0 * w + x0].grayscale;
        float c10 = pixels[y0 * w + x1].grayscale;
        float c01 = pixels[y1 * w + x0].grayscale;
        float c11 = pixels[y1 * w + x1].grayscale;

        return Mathf.Lerp(
            Mathf.Lerp(c00, c10, tx),
            Mathf.Lerp(c01, c11, tx),
            ty);
    }

    private float EvaluateNoise(Vector3 worldPos)
    {
        Vector3 p = (worldPos + noiseOffset) * noiseScale;

        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxAmplitude = 0f;

        for (int o = 0; o < noiseOctaves; o++)
        {
            // Unity's Mathf.PerlinNoise is 2D; use XZ and XY planes combined
            // for pseudo-3D noise on arbitrary surfaces
            float n = (
                Mathf.PerlinNoise(p.x * frequency, p.z * frequency) +
                Mathf.PerlinNoise(p.x * frequency + 31.7f, p.y * frequency + 47.3f) +
                Mathf.PerlinNoise(p.y * frequency + 73.1f, p.z * frequency + 97.9f)
            ) / 3f;

            value += n * amplitude;
            maxAmplitude += amplitude;

            amplitude *= noisePersistence;
            frequency *= 2f;
        }

        return Mathf.Clamp01(value / maxAmplitude);
    }

    private float EvaluateAttractor(Vector3 worldPos)
    {
        if (attractors == null || attractors.Length == 0) return attractorFloor;

        float maxInfluence = 0f;

        for (int i = 0; i < attractors.Length; i++)
        {
            if (attractors[i] == null) continue;

            float dist = Vector3.Distance(worldPos, attractors[i].position);
            if (dist >= attractorRadius) continue;

            float t = 1f - (dist / attractorRadius);
            float influence = Mathf.Pow(t, attractorFalloff);
            maxInfluence = Mathf.Max(maxInfluence, influence);
        }

        return Mathf.Max(maxInfluence, attractorFloor);
    }
}
