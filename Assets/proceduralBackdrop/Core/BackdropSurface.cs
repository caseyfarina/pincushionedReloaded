using UnityEngine;

/// <summary>
/// Points sampled evenly over a mesh's surface, with the normal at each one.
///
/// Sampling is area-weighted, so density does not pile up wherever the source
/// mesh happens to be finely tessellated - the same requirement the pin scatter
/// has, and the reason a naive per-triangle or per-vertex distribution is wrong.
///
/// Built once and cached, because the CDF scan is O(triangles) and the surface
/// does not change per frame. Caller-owned arrays so the rebuild allocates only
/// when the count grows.
/// </summary>
public class BackdropSurface
{
    public Vector3[] positions = new Vector3[0];
    public Vector3[] normals = new Vector3[0];
    public int count;
    public Bounds bounds;

    private Mesh builtFrom;
    private int builtCount = -1;
    private uint builtSeed = uint.MaxValue;

    public bool IsValid => count > 0;

    /// <summary>
    /// Returns true when the cache already matches the request, so the caller
    /// can skip the rebuild.
    /// </summary>
    public bool Matches(Mesh mesh, int wanted, uint seed)
        => builtFrom == mesh && builtCount == wanted && builtSeed == seed && count > 0;

    /// <summary>
    /// Scatters <paramref name="wanted"/> points over the mesh. Returns false if
    /// the mesh cannot be read, which is the usual failure: FBX import leaves
    /// meshes non-readable, and vertex data is unreachable from script until
    /// that is switched on.
    /// </summary>
    public bool Rebuild(Mesh mesh, int wanted, uint seed)
    {
        count = 0;
        builtFrom = mesh;
        builtCount = wanted;
        builtSeed = seed;

        if (mesh == null || wanted <= 0) return false;
        if (!mesh.isReadable) return false;

        var verts = mesh.vertices;
        var tris = mesh.triangles;
        var norms = mesh.normals;
        int triCount = tris.Length / 3;
        if (triCount == 0 || verts.Length == 0) return false;

        bool haveNormals = norms != null && norms.Length == verts.Length;

        // Cumulative triangle area, so a point can be placed by binary search
        // against a uniform draw - which is what makes the distribution even
        // rather than per-triangle.
        var cdf = new float[triCount];
        float total = 0f;
        for (int t = 0; t < triCount; t++)
        {
            Vector3 a = verts[tris[t * 3]];
            Vector3 b = verts[tris[t * 3 + 1]];
            Vector3 c = verts[tris[t * 3 + 2]];
            total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cdf[t] = total;
        }
        if (total <= 1e-8f) return false;

        if (positions.Length < wanted)
        {
            positions = new Vector3[wanted];
            normals = new Vector3[wanted];
        }

        for (int i = 0; i < wanted; i++)
        {
            float pick = BackdropLattice.Rand01(i, seed, 1301u) * total;

            int lo = 0, hi = triCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cdf[mid] < pick) lo = mid + 1; else hi = mid;
            }

            int i0 = tris[lo * 3], i1 = tris[lo * 3 + 1], i2 = tris[lo * 3 + 2];
            Vector3 a = verts[i0], b = verts[i1], c = verts[i2];

            // Uniform barycentric point in the triangle. The fold keeps it
            // inside; without it the samples bunch along one edge.
            float u = BackdropLattice.Rand01(i, seed, 1307u);
            float v = BackdropLattice.Rand01(i, seed, 1313u);
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }

            positions[i] = a + (b - a) * u + (c - a) * v;
            normals[i] = haveNormals
                ? (norms[i0] * (1f - u - v) + norms[i1] * u + norms[i2] * v).normalized
                : Vector3.Cross(b - a, c - a).normalized;
        }

        count = wanted;
        bounds = mesh.bounds;
        return true;
    }

    public void Invalidate()
    {
        count = 0;
        builtFrom = null;
        builtCount = -1;
        builtSeed = uint.MaxValue;
    }
}
