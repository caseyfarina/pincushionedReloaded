#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Area-weighted surface sampling, extracted so that every tool that produces a
/// <see cref="SurfaceSampleData"/> asset uses the SAME math.
///
/// This matters more than it looks: pin scale is judged by comparing artifacts
/// side by side, so two code paths that sample differently would make models
/// silently incomparable. <see cref="BatchScatterProcessorWindow"/> and
/// <see cref="ArtifactImportWindow"/> both call this.
/// </summary>
public static class SurfaceSampleBaker
{
    /// <summary>
    /// Bake a pool of surface samples in the mesh's LOCAL space.
    /// Returns null when the mesh has no triangles.
    /// </summary>
    /// <param name="assetPath">
    /// Where to write the .asset. When <paramref name="overwrite"/> is false a
    /// unique path is generated instead — which is how "_SampleData 1"
    /// duplicates appear. Importing tools should pass true.
    /// </param>
    public static SurfaceSampleData Bake(
        Mesh mesh, int poolSize, int seed, string assetPath, bool overwrite)
    {
        if (mesh == null) return null;

        Vector3[] verts = mesh.vertices;
        Vector3[] meshNormals = mesh.normals;
        int[] tris = mesh.triangles;
        int triCount = tris.Length / 3;
        if (triCount == 0) return null;

        Vector2[] meshUVs = mesh.uv;
        bool hasUVs = meshUVs != null && meshUVs.Length == verts.Length;

        // Cumulative area table (local space) so sampling is area-weighted:
        // large triangles must receive proportionally more pins.
        float[] cumArea = new float[triCount];
        float running = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = verts[tris[i * 3]];
            Vector3 b = verts[tris[i * 3 + 1]];
            Vector3 c = verts[tris[i * 3 + 2]];
            running += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumArea[i] = running;
        }
        float totalArea = running;

        Vector3[] positions = new Vector3[poolSize];
        Vector3[] normals = new Vector3[poolSize];
        Vector2[] uvs = hasUVs ? new Vector2[poolSize] : null;

        // System.Random, never UnityEngine.Random: the latter is a global
        // shared stream anything else can perturb, so bakes would not reproduce.
        System.Random rng = new System.Random(seed);

        for (int s = 0; s < poolSize; s++)
        {
            float r = (float)(rng.NextDouble() * totalArea);
            int triIdx = System.Array.BinarySearch(cumArea, r);
            if (triIdx < 0) triIdx = ~triIdx;
            triIdx = Mathf.Clamp(triIdx, 0, triCount - 1);

            int i0 = tris[triIdx * 3];
            int i1 = tris[triIdx * 3 + 1];
            int i2 = tris[triIdx * 3 + 2];

            // Uniform barycentric point in the triangle (fold the far half back).
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            float w = 1f - u - v;

            positions[s] = verts[i0] * w + verts[i1] * u + verts[i2] * v;
            normals[s] = (meshNormals[i0] * w + meshNormals[i1] * u + meshNormals[i2] * v).normalized;
            if (hasUVs) uvs[s] = meshUVs[i0] * w + meshUVs[i1] * u + meshUVs[i2] * v;
        }

        SurfaceSampleData data = ScriptableObject.CreateInstance<SurfaceSampleData>();
        data.sourceMesh = mesh;
        data.totalSurfaceArea = totalArea;
        data.sampleCount = poolSize;
        data.hasUVs = hasUVs;
        data.positions = positions;
        data.normals = normals;
        data.uvs = uvs ?? new Vector2[0];

        if (overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SurfaceSampleData>(assetPath);
            if (existing != null)
            {
                // Update IN PLACE rather than delete-and-recreate. Two reasons:
                //
                // 1. It preserves the asset's GUID, so every existing reference
                //    (prefabs, the main scene) keeps working. Deleting would
                //    silently null them.
                // 2. A DeleteAsset followed by CreateAsset in the same frame
                //    yields an asset whose GUID is not yet registered, so
                //    anything serialising a reference to it in that same frame
                //    writes null. That produced _Pinned prefabs with no sample
                //    data.
                EditorUtility.CopySerialized(data, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(data);   // the temporary we just built
                return existing;
            }
        }
        else
        {
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        }

        AssetDatabase.CreateAsset(data, assetPath);
        return data;
    }
}
#endif
