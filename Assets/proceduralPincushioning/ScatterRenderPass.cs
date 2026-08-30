using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

/// <summary>
/// Render pass that issues DrawMeshInstanced calls from inside URP's Render Graph.
/// Uses AddUnsafePass and inherits render targets + lighting state from URP's
/// opaque rendering setup. Shadows are handled separately via
/// Graphics.RenderMeshInstanced(ShadowsOnly) in MeshSurfaceScatter.
/// </summary>
public class ScatterRenderPass : ScriptableRenderPass
{
    private class PassData
    {
        internal List<ScatterInstanceRegistry.ScatterBatchGroup> groups;
        internal TextureHandle colorTarget;
        internal TextureHandle depthTarget;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
    {
        var groups = ScatterInstanceRegistry.GetGroups();
        if (groups.Count == 0) return;

        using (var builder = renderGraph.AddUnsafePass<PassData>(
            "Scatter Instanced Rendering", out var passData))
        {
            UniversalResourceData resourceData = frameContext.Get<UniversalResourceData>();

            passData.groups = new List<ScatterInstanceRegistry.ScatterBatchGroup>(groups);
            passData.colorTarget = resourceData.activeColorTexture;
            passData.depthTarget = resourceData.activeDepthTexture;

            builder.UseTexture(passData.colorTarget, AccessFlags.ReadWrite);
            builder.UseTexture(passData.depthTarget, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext ctx) =>
            {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                // Bind render targets but preserve URP's lighting/shader state
                cmd.SetRenderTarget(data.colorTarget, data.depthTarget);

                for (int g = 0; g < data.groups.Count; g++)
                {
                    var group = data.groups[g];
                    if (group.mesh == null || group.material == null) continue;

                    for (int b = 0; b < group.batches.Count; b++)
                    {
                        cmd.DrawMeshInstanced(
                            group.mesh,
                            0,
                            group.material,
                            0,               // pass 0 = ForwardLit only
                            group.batches[b]
                        );
                    }
                }
            });
        }
    }

    [System.Obsolete]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var groups = ScatterInstanceRegistry.GetGroups();
        if (groups.Count == 0) return;

        CommandBuffer cmd = CommandBufferPool.Get("Scatter Instanced Rendering");
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            if (group.mesh == null || group.material == null) continue;
            for (int b = 0; b < group.batches.Count; b++)
                cmd.DrawMeshInstanced(group.mesh, 0, group.material, -1, group.batches[b]);
        }
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
