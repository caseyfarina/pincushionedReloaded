using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The mesh set the backdrop cycles through. A ScriptableObject rather than a
/// runtime folder scan because AssetDatabase is editor-only and does not exist
/// in a build — the same reason Samples/Resources exists in this project.
/// Populate it with the Scan Folder button in the inspector.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Backdrop Library", fileName = "BackdropLibrary")]
public class BackdropLibrary : ScriptableObject
{
    [Tooltip("Project-relative folder the Scan Folder button reads, e.g. Assets/proceduralBackdrop/Meshes")]
    public string scanFolder = "Assets/proceduralBackdrop/Meshes";

    [Tooltip("Warn on scan when a mesh exceeds this triangle count. A backdrop instance is multiplied by instances x split-screen cells.")]
    public int triangleWarnThreshold = 3000;

    public List<Mesh> meshes = new List<Mesh>();

    public int Count => meshes.Count;

    /// <summary>Index-safe, null-safe fetch. Out-of-range returns null rather than throwing, because index comes from a pad press.</summary>
    public Mesh Get(int index)
    {
        if (index < 0 || index >= meshes.Count) return null;
        return meshes[index];
    }
}