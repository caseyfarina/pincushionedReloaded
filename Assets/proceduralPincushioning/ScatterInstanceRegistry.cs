using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Static per-frame registry that MeshSurfaceScatter instances write batch data into
/// each Update(), and ScatterRenderPass reads from inside the Render Graph pipeline.
///
/// Cleared at the start of each frame by ScatterRendererFeature before render passes run.
/// </summary>
public static class ScatterInstanceRegistry
{
    public struct ScatterBatchGroup
    {
        public Mesh                  mesh;
        public Material              material;
        public List<Matrix4x4[]>     batches;       // each array is max 1023 matrices
        public ShadowCastingMode     castShadows;
        public bool                  receiveShadows;
        public uint                  renderingLayerMask;
        public int                   layer;
        public Bounds                worldBounds;
    }

    private static readonly List<ScatterBatchGroup> _groups = new List<ScatterBatchGroup>();
    private static int _lastClearedFrame = -1;

    /// <summary>
    /// Called by each MeshSurfaceScatter instance at the top of RegisterBatches().
    /// Clears the list once per frame (on the first registration call), so by the
    /// time the render pass runs the registry contains exactly this frame's data.
    /// </summary>
    public static void EnsureClearedForFrame()
    {
        int frame = UnityEngine.Time.frameCount;
        if (frame != _lastClearedFrame)
        {
            _groups.Clear();
            _lastClearedFrame = frame;
        }
    }

    /// <summary>Called by each MeshSurfaceScatter instance every Update().</summary>
    public static void Register(ScatterBatchGroup group) => _groups.Add(group);

    /// <summary>Called by ScatterRenderPass inside the render pipeline.</summary>
    public static IReadOnlyList<ScatterBatchGroup> GetGroups() => _groups;
}
