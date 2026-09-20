using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The connector meshes a cable can be tipped with. Scanned from a folder at
/// author time into a serialized array, because AssetDatabase does not exist in
/// a player build.
///
/// An empty library is a supported state: cables render headless rather than
/// throwing, so the system is usable before any plug has been modelled.
/// </summary>
public class CableLibrary : MonoBehaviour
{
    [Tooltip("Folder scanned by the Scan button. Every mesh found becomes a selectable connector.")]
    public string folder = "Assets/proceduralCables/Meshes/Connectors";

    [Tooltip("Meshes above this triangle count are skipped - a connector is a prop, not a scan.")]
    public int maxTriangles = 20000;

    [SerializeField] private Mesh[] meshes = new Mesh[0];

    public Mesh[] Meshes => meshes ?? (meshes = new Mesh[0]);
    public int Count => Meshes.Length;

    /// <summary>Null-safe lookup. Index -1 means "no library", and is expected.</summary>
    public Mesh Get(int index) =>
        (index < 0 || index >= Count) ? null : meshes[index];

#if UNITY_EDITOR
    /// <summary>Editor-only. Rebuilds the serialized array from the folder.</summary>
    public void Scan()
    {
        var found = new System.Collections.Generic.List<Mesh>();
        int skipped = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(obj is Mesh m)) continue;
                if (m.triangles.Length / 3 > maxTriangles) { skipped++; continue; }
                found.Add(m);
            }
        }

        meshes = found.ToArray();
        EditorUtility.SetDirty(this);
        Debug.Log($"CableLibrary: {meshes.Length} connector(s) in {folder}"
                + (skipped > 0 ? $", {skipped} skipped over {maxTriangles} triangles" : ""));
    }
#endif
}
