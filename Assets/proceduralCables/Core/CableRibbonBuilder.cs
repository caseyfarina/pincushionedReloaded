using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a cable's node positions into vertex and index data, appending into
/// shared lists so every live cable lands in one mesh and one draw.
///
/// Positions are emitted un-offset and the ribbon is widened in the vertex
/// shader. That is not an optimisation - this project renders up to 32
/// split-screen cameras, and a CPU-billboarded ribbon is correct for exactly
/// one of them and collapses to an invisible sliver in the rest.
/// </summary>
public static class CableRibbonBuilder
{
    private const float Eps = 1e-10f;

    public static int VertexCount(int nodes) => nodes < 2 ? 0 : nodes * 2;
    public static int IndexCount(int nodes) => nodes < 2 ? 0 : (nodes - 1) * 6;

    /// <summary>
    /// Append one cable. emission is the cable's contact flash, written flat
    /// across both its vertices. uvTiling is repeats per world unit, so u is
    /// proportional to the cable's actual length rather than to t - otherwise a
    /// short cable and a long one get the same repeat count and the texel
    /// density mismatch gives the ribbon away.
    /// </summary>
    public static void Append(
        Vector3[] nodes, int nodeCount, Color color, Color emission, float uvTiling,
        List<Vector3> positions, List<Vector3> tangents, List<Vector2> uvs,
        List<Color> colors, List<Vector4> emissions, List<int> indices)
    {
        if (nodes == null || nodeCount < 2) return;

        int baseVertex = positions.Count;
        float travelled = 0f;
        Vector3 lastTangent = Vector3.forward;

        for (int i = 0; i < nodeCount; i++)
        {
            Vector3 node = nodes[i];

            // Central difference along the interior, one-sided at the ends.
            Vector3 delta = i == 0 ? nodes[1] - nodes[0]
                          : i == nodeCount - 1 ? nodes[i] - nodes[i - 1]
                          : nodes[i + 1] - nodes[i - 1];

            // Coincident nodes give a zero-length delta. Unguarded this
            // normalises to NaN, and Unity silently drops any mesh containing
            // one rather than reporting it.
            Vector3 tangent = delta.sqrMagnitude > Eps ? delta.normalized : lastTangent;
            lastTangent = tangent;

            if (i > 0) travelled += Vector3.Distance(nodes[i - 1], node);
            float u = travelled * uvTiling;

            positions.Add(node); positions.Add(node);
            tangents.Add(tangent); tangents.Add(tangent);
            uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, 1f));
            colors.Add(color); colors.Add(color);

            // Emission rides its own channel: vertex colour's alpha is already
            // the per-cable width multiplier, and packing a flash into the rgb
            // would distort a bright cable's own colour when it clipped.
            var e = new Vector4(emission.r, emission.g, emission.b, 0f);
            emissions.Add(e); emissions.Add(e);
        }

        for (int i = 0; i < nodeCount - 1; i++)
        {
            int a = baseVertex + i * 2;
            int b = a + 2;

            indices.Add(a); indices.Add(a + 1); indices.Add(b);
            indices.Add(a + 1); indices.Add(b + 1); indices.Add(b);
        }
    }
}
