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

    [Tooltip("Name of the material this submesh was authored with in Blender. This is what decides whether the part takes the accent material, because it is a statement of intent rather than a guess about geometry.")]
    public string materialName;

    [Tooltip("Set on scan when materialName matches one of the library's accent names.")]
    public bool isSphere;

    [Tooltip("How round the part measures, 0 to 1. Informational only - it disagreed with the authored material on 7 of 13 models, because a squashed or half-buried sphere is still the sphere.")]
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

    [Tooltip("A part whose authored material name contains any of these takes the sphere material. Case-insensitive substring match, so BackdropSphere matches 'sphere'.")]
    public List<string> accentMaterialNames = new List<string> { "sphere", "accent" };

    [Tooltip("Fallback only, for parts whose material name says nothing. Roundness is a poor proxy for intent - it misses squashed and half-buried spheres - so the name wins wherever there is one.")]
    [Range(0.5f, 1f)] public float sphereThreshold = 0.9f;

    [Tooltip("Rotation applied to every model in this library before it is instanced. The backdrop faces the camera, which means looking at a model's -Z side - fine for a symmetric shape, and mirrored for anything with handedness. Letters need (0, 180, 0) to read the right way round.")]
    public Vector3 modelRotationEuler;

    [Tooltip("Spelled out by the Word selection mode. Characters with no matching model are skipped, so a word can be changed without re-authoring the set.")]
    public string word = "pincushioned";

    public List<BackdropModel> models = new List<BackdropModel>();

    public int Count => models.Count;

    /// <summary>
    /// The model whose name ends in the given letter, or -1. Matches on the last
    /// letter of the name so letter_C, C and Glyph_C all resolve, without
    /// depending on a prefix convention.
    /// </summary>
    public int IndexOfLetter(char c)
    {
        char want = char.ToUpperInvariant(c);
        for (int i = 0; i < models.Count; i++)
        {
            var m = models[i];
            if (m == null || string.IsNullOrEmpty(m.name)) continue;

            for (int k = m.name.Length - 1; k >= 0; k--)
            {
                if (!char.IsLetter(m.name[k])) continue;
                if (char.ToUpperInvariant(m.name[k]) == want) return i;
                break;   // only the final letter counts
            }
        }
        return -1;
    }

    /// <summary>
    /// Resolves <see cref="word"/> into model indices, writing one entry per
    /// character that has a model and returning how many were written. Letters
    /// with no model are dropped rather than substituted, so a missing glyph
    /// shortens the word instead of spelling it wrong.
    /// </summary>
    public int BuildWord(int[] dst)
    {
        if (dst == null || string.IsNullOrEmpty(word)) return 0;

        int n = 0;
        for (int i = 0; i < word.Length && n < dst.Length; i++)
        {
            if (!char.IsLetter(word[i])) continue;
            int idx = IndexOfLetter(word[i]);
            if (idx >= 0) dst[n++] = idx;
        }
        return n;
    }

    /// <summary>Index-safe, null-safe fetch. Out of range returns null rather than throwing, because the index comes from a pad press.</summary>
    public BackdropModel Get(int index)
    {
        if (index < 0 || index >= models.Count) return null;
        var m = models[index];
        return m != null && m.IsUsable ? m : null;
    }
}
