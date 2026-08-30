using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that enqueues ScatterRenderPass to draw all registered
/// scatter batches from inside the Render Graph pipeline.
///
/// Setup: add this feature to your URP Renderer asset once
/// (Renderer asset inspector → Add Renderer Feature → Scatter Renderer Feature).
/// All MeshSurfaceScatter instances in all scenes render through it automatically.
/// </summary>
public class ScatterRendererFeature : ScriptableRendererFeature
{
    private ScatterRenderPass _pass;

    public override void Create()
    {
        _pass = new ScatterRenderPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
    }
}
