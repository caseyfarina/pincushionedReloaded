using UnityEngine;

/// <summary>
/// Stores a precomputed pool of surface sample points for a specific mesh.
/// Generated offline via the Batch Scatter Processor editor tool.
///
/// Data is stored in the source mesh's LOCAL space. The runtime scatter
/// component applies the surface transform's localToWorldMatrix.
///
/// Memory cost per sample: ~32 bytes (position 12 + normal 12 + uv 8).
/// </summary>
[CreateAssetMenu(fileName = "SurfaceSampleData", menuName = "Scatter/Surface Sample Data")]
public class SurfaceSampleData : ScriptableObject
{
    [Tooltip("The source mesh this data was baked from.")]
    public Mesh sourceMesh;

    [Tooltip("Total surface area in local space.")]
    public float totalSurfaceArea;

    [Tooltip("Number of baked samples in the pool.")]
    public int sampleCount;

    [Tooltip("Whether UV data was available on the source mesh at bake time.")]
    public bool hasUVs;

    [HideInInspector] public Vector3[] positions;
    [HideInInspector] public Vector3[] normals;
    [HideInInspector] public Vector2[] uvs;

    public void GetSample(int index, out Vector3 position, out Vector3 normal)
    {
        position = positions[index];
        normal = normals[index];
    }

    public void GetSample(int index, out Vector3 position, out Vector3 normal, out Vector2 uv)
    {
        position = positions[index];
        normal = normals[index];
        uv = hasUVs ? uvs[index] : Vector2.zero;
    }
}
