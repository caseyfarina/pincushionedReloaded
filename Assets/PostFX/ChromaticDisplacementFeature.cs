using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// Red Giant style chromatic displacement for URP 17 / Unity 6.
///
/// The image displaces itself: its luminance is shaped by a levels stage,
/// blurred heavily, and read as a HEIGHT FIELD. Pixels slide along the gradient
/// of that field while the sample offset sweeps across a spectrum, which is
/// what separates the colours.
///
/// Parameters live on ChromaticDisplacementVolume so the effect can be blended
/// by the Volume framework like any other post process. At intensity 0 the pass
/// returns immediately and costs nothing.
///
/// URP 17 / Unity 6 note: this uses the Render Graph API (RecordRenderGraph).
/// The pre-Unity-6 Execute()/OnCameraSetup() path is obsolete and silently
/// does nothing, which is the usual reason custom post effects "don't work".
/// </summary>
[DisallowMultipleRendererFeature("Chromatic Displacement")]
public class ChromaticDisplacementFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Where in the frame the effect runs. BeforeRenderingPostProcessing " +
                 "gives pre-tonemap HDR, so bright things displace much harder -- " +
                 "closer to the Red Giant look. After gives bloom-fed displacement.")]
        public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("Displacement source resolution divisor. The source is only ever " +
                 "read as a GRADIENT of a heavily blurred field, so it is inherently " +
                 "low-frequency: 4 or 8 costs almost nothing and looks the same.")]
        [Range(1, 8)] public int downsample = 4;

        [Tooltip("Run on every camera, including split-screen sub-cameras. " +
                 "Leave OFF. Measured 2026-09-07: main-camera-only is free (+0.02 ms " +
                 "GPU), but ON at 32 cells cost +9.26 ms GPU / +16.72 ms CPU, because " +
                 "each sub-camera allocates full-size render targets regardless of how " +
                 "small its viewport rect is.")]
        public bool allCameras = false;
    }

    public Settings settings = new Settings();

    [Tooltip("Assign Hidden/PostFX/ChromaticDisplacement. Referenced explicitly " +
             "so the shader survives build stripping -- a Shader.Find at runtime is " +
             "the classic 'works in editor, pink in build' failure.")]
    public Shader shader;

    Material _material;
    ChromaticDisplacementPass _pass;

    public override void Create()
    {
        if (shader != null && _material == null)
            _material = CoreUtils.CreateEngineMaterial(shader);

        _pass = new ChromaticDisplacementPass(_material, settings)
        {
            renderPassEvent = settings.injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null) return;

        var cam = renderingData.cameraData.camera;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (!settings.allCameras && cam.CompareTag("MainCamera") == false) return;

        // Reading the camera colour requires an intermediate texture; without this
        // URP may render straight to the backbuffer and the source is unreadable.
        renderer.EnqueuePass(_pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (_pass != null) _pass.Setup(settings);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}
