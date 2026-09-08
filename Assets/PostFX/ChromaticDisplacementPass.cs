using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// Pass structure:
///
///   1. DownsampleLevels  full-res read -> 1/N res, luminance + levels
///   2. Blur H / Blur V   at 1/N res, repeated per iteration
///   3. Displace          full-res, gradient + spectral sweep   <-- dominates
///   4. CopyBack          full-res
///
/// Measured 2026-09-07: at one camera the whole chain is +0.02 ms GPU and
/// exactly +5 SetPass calls, i.e. inside the noise floor. Applied to EVERY
/// split-screen sub-camera at 32 cells it cost +9.26 ms GPU / +16.72 ms CPU,
/// because each sub-camera allocates full-size targets regardless of how small
/// its viewport rect is. Hence the main-camera-only default on the feature.
///
/// A texture cannot be read and written in the same pass, which is why the
/// displace pass writes to a temp and the last pass copies it back.
/// </summary>
public class ChromaticDisplacementPass : ScriptableRenderPass
{
    const int PassDownsampleLevels = 0;
    const int PassBlurH            = 1;
    const int PassBlurV            = 2;
    const int PassDisplace         = 3;
    const int PassCopyBack         = 4;

    static readonly int InBlackId     = Shader.PropertyToID("_InBlack");
    static readonly int InWhiteId     = Shader.PropertyToID("_InWhite");
    static readonly int GammaId       = Shader.PropertyToID("_Gamma");
    static readonly int OutBlackId    = Shader.PropertyToID("_OutBlack");
    static readonly int OutWhiteId    = Shader.PropertyToID("_OutWhite");
    static readonly int BlurTexelId   = Shader.PropertyToID("_BlurTexel");
    static readonly int StrengthId    = Shader.PropertyToID("_Strength");
    static readonly int SpreadId      = Shader.PropertyToID("_Spread");
    static readonly int HeightGammaId = Shader.PropertyToID("_HeightGamma");
    static readonly int IntensityId   = Shader.PropertyToID("_Intensity");
    static readonly int SamplesId     = Shader.PropertyToID("_Samples");
    static readonly int TintAmountId  = Shader.PropertyToID("_TintAmount");
    static readonly int TintAId       = Shader.PropertyToID("_TintA");
    static readonly int TintBId       = Shader.PropertyToID("_TintB");
    static readonly int AspectFixId   = Shader.PropertyToID("_AspectFix");
    static readonly int ViewHeightId  = Shader.PropertyToID("_ViewHeight");
    static readonly int HeightTexId   = Shader.PropertyToID("_HeightTex");

    class DisplaceData
    {
        public TextureHandle source;
        public TextureHandle height;
        public Material      material;
    }

    readonly Material _material;
    ChromaticDisplacementFeature.Settings _settings;

    public ChromaticDisplacementPass(Material material,
                                     ChromaticDisplacementFeature.Settings settings)
    {
        _material = material;
        _settings = settings;
        requiresIntermediateTexture = true;   // we must be able to READ camera colour
    }

    public void Setup(ChromaticDisplacementFeature.Settings settings) => _settings = settings;

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer) return;

        var source = resourceData.activeColorTexture;
        if (!source.IsValid()) return;

        var vol = VolumeManager.instance.stack.GetComponent<ChromaticDisplacementVolume>();
        if (vol == null || !vol.IsActive()) return;      // costs nothing when dialled out

        var srcDesc = source.GetDescriptor(renderGraph);
        float aspect = (float)srcDesc.width / Mathf.Max(1, srcDesc.height);

        // ── parameters ───────────────────────────────────────────────────
        _material.SetFloat(InBlackId,     vol.inputBlack.value);
        _material.SetFloat(InWhiteId,     vol.inputWhite.value);
        _material.SetFloat(GammaId,       vol.gamma.value);
        _material.SetFloat(OutBlackId,    vol.outputBlack.value);
        _material.SetFloat(OutWhiteId,    vol.outputWhite.value);
        _material.SetFloat(StrengthId,    vol.strength.value);
        _material.SetFloat(SpreadId,      vol.spread.value);
        _material.SetFloat(HeightGammaId, vol.heightGamma.value);
        _material.SetFloat(IntensityId,   vol.intensity.value);
        _material.SetInt(SamplesId,       vol.spectralSamples.value);
        _material.SetFloat(TintAmountId,  vol.tintAmount.value);
        _material.SetColor(TintAId,       vol.tintA.value);
        _material.SetColor(TintBId,       vol.tintB.value);
        _material.SetFloat(ViewHeightId,  vol.viewHeightField.value ? 1f : 0f);
        // Equal pixel motion on both axes: a UV step in x covers more pixels than
        // the same step in y on a wide viewport.
        _material.SetVector(AspectFixId, new Vector4(1f / aspect, 1f, 0f, 0f));

        // ── low-res displacement source ──────────────────────────────────
        int div = Mathf.Max(1, _settings.downsample);
        var lowDesc = srcDesc;
        lowDesc.width  = Mathf.Max(1, srcDesc.width  / div);
        lowDesc.height = Mathf.Max(1, srcDesc.height / div);
        lowDesc.depthBufferBits = 0;
        lowDesc.clearBuffer = false;
        lowDesc.name = "ChromaticDisp_HeightA";
        var a = renderGraph.CreateTexture(lowDesc);
        lowDesc.name = "ChromaticDisp_HeightB";
        var b = renderGraph.CreateTexture(lowDesc);

        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
            source, a, _material, PassDownsampleLevels), "ChromaticDisp Downsample+Levels");

        // Tap spacing is ONE low-res texel, always. Scaling the spacing with the
        // radius is the obvious approach and it is wrong: once the taps are far
        // apart you see nine discrete ghosts of the image instead of a blur.
        // Large radii come from ITERATIONS (variance adds, so the effective
        // radius grows with sqrt(iterations)) and from the downsample factor.
        float lowTexelY = 1f / Mathf.Max(1, lowDesc.height);
        float lowTexelX = 1f / Mathf.Max(1, lowDesc.width);

        // One 9-tap pass at 1-texel spacing reaches about 4 texels.
        float perPass = 4f * lowTexelY;
        float desired = Mathf.Max(vol.blurRadius.value, 1e-5f);
        int iterations = Mathf.Clamp(
            Mathf.RoundToInt((desired / perPass) * (desired / perPass)), 1, 8);

        // Set ONCE: it is the same for every blur pass, so deferred execution
        // against the shared material is safe here.
        _material.SetVector(BlurTexelId, new Vector4(lowTexelX, lowTexelY, 0f, 0f));

        for (int it = 0; it < iterations; it++)
        {
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                a, b, _material, PassBlurH), "ChromaticDisp BlurH");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                b, a, _material, PassBlurV), "ChromaticDisp BlurV");
        }

        // ── full-res displace, then copy back ────────────────────────────
        var tmpDesc = srcDesc;
        tmpDesc.depthBufferBits = 0;
        tmpDesc.clearBuffer = false;
        tmpDesc.name = "ChromaticDisp_Temp";
        var temp = renderGraph.CreateTexture(tmpDesc);

        // Two inputs (camera colour + height) cannot be expressed with
        // BlitMaterialParameters, so this one is an explicit raster pass.
        using (var builder = renderGraph.AddRasterRenderPass<DisplaceData>(
                   "ChromaticDisp Displace", out var data))
        {
            data.source = source;
            data.height = a;
            data.material = _material;

            builder.UseTexture(source);
            builder.UseTexture(a);
            builder.SetRenderAttachment(temp, 0);
            builder.AllowPassCulling(false);
            // SetGlobalTexture is global state; a raster pass must opt in.
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (DisplaceData d, RasterGraphContext ctx) =>
            {
                ctx.cmd.SetGlobalTexture(HeightTexId, d.height);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f),
                                    d.material, PassDisplace);
            });
        }

        renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
            temp, source, _material, PassCopyBack), "ChromaticDisp CopyBack");
    }
}
