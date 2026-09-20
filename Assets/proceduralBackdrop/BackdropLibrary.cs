using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One drawable piece of a model: a mesh, which of its submeshes to draw, and
/// where that piece sits relative to the model root.
///
/// A submesh index is part of the identity because most of these shapes carry
/// two, and an instanced draw renders exactly one submesh - so a mesh with two
/// is two parts, not one. That was the bug: only submesh 0 ever appeared.
///
/// The transform is stored decomposed rather than as a Matrix4x4 because Unity
/// serializes TRS components reliably and they are legible in the inspector.
/// </summary>
[System.Serializable]
public struct BackdropPart
{
    public Mesh mesh;
    public int subMesh;

    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;

    [Tooltip("Set on scan when this part's vertices are all about the same distance from its centre. These shapes pair a body with a sphere, and the two want different materials - so the distinction has to survive into the renderer.")]
    public bool isSphere;

    [Tooltip("0 is a flat or boxy part, 1 is a perfect sphere. Kept so the scan's decision can be second-guessed without re-deriving it.")]
    public float sphericity;

    public Matrix4x4 Local => Matrix4x4.TRS(
        localPosition,
        localRotation.x == 0f && localRotation.y == 0f && localRotation.z == 0f && localRotation.w == 0f
            ? Quaternion.identity
            : localRotation,
        localScale == Vector3.zero ? Vector3.one : localScale);
}

/// <summary>
/// One source file's worth of geometry, drawn as a unit. Several of these shapes
/// are multi-part: either several objects in the FBX, or one object with several
/// submeshes. Every part has to be submitted or the shape appears incomplete.
/// </summary>
[System.Serializable]
public class BackdropModel
{
    public string name;
    public List<BackdropPart> parts = new List<BackdropPart>();

    /// <summary>
    /// Uniform scale that fits this model into a one-unit box.
    ///
    /// Source files agree on nothing, and without this the scale range and axis
    /// bias would mean something different for every model - swapping mesh would
    /// change the size of the field rather than the shape of its instances. The
    /// same reason every artifact in this project is normalised to radius 1 at
    /// intake.
    /// </summary>
    public float normalizeScale = 1f;

    public int triangles;

    public bool IsUsable
    {
        get
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].mesh != null) return true;
            return false;
        }
    }
}

/// <summary>
/// The model set the backdrop cycles through. A ScriptableObject rather than a
/// runtime folder scan because AssetDatabase is editor-only and does not exist
/// in a build - the same reason Samples/Resources exists in this project.
/// Populate it with the Scan Folder button in the inspector.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Library", fileName = "BackdropLibrary")]
public class BackdropLibrary : ScriptableObject
{
    [Tooltip("Project-relative folder the Scan Folder button reads, e.g. Assets/proceduralBackdrop/Meshes")]
    public string scanFolder = "Assets/proceduralBackdrop/Meshes";

    [Tooltip("Warn on scan when a model exceeds this triangle count. A backdrop model is multiplied by instances x split-screen cells, and again by the outline pass.")]
    public int triangleWarnThreshold = 4000;

    [Tooltip("Normalise every model into a one-unit box on scan, so swapping mesh changes the shape rather than the size of the field.")]
    public bool normalizeScale = true;

    [Tooltip("How round a part must be to be treated as a sphere and take the sphere material. 1 is a perfect sphere; the bodies in this set sit well below 0.8.")]
    [Range(0.5f, 1f)] public float sphereThreshold = 0.86f;

    public List<BackdropModel> models = new List<BackdropModel>();

    public int Count => models.Count;

    /// <summary>Index-safe, null-safe fetch. Out of range returns null rather than throwing, because the index comes from a pad press.</summary>
    public BackdropModel Get(int index)
    {
        if (index < 0 || index >= models.Count) return null;
        var m = models[index];
        return m != null && m.IsUsable ? m : null;
    }
}
