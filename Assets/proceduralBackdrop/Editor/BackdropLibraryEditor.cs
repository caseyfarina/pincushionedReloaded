using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scan Folder button for BackdropLibrary, plus the triangle-count guard.
///
/// The guard is not cosmetic. This project's artifact scans are 50k+ triangles
/// and dropping one into the backdrop folder multiplies it by instances x
/// split-screen cells. The resulting stall reads as "VFX Graph is slow" rather
/// than as an authoring mistake, so the tool names it at import time.
/// </summary>
[CustomEditor(typeof(BackdropLibrary))]
public class BackdropLibraryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var lib = (BackdropLibrary)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Folder", GUILayout.Height(28)))
            Scan(lib);

        if (lib.meshes.Count > 0)
            EditorGUILayout.HelpBox(
                $"{lib.meshes.Count} meshes, {TotalTriangles(lib):n0} triangles total.",
                MessageType.None);
    }

    private static void Scan(BackdropLibrary lib)
    {
        if (!AssetDatabase.IsValidFolder(lib.scanFolder))
        {
            Debug.LogError($"[BackdropLibrary] Not a folder: {lib.scanFolder}", lib);
            return;
        }

        var found = new List<Mesh>();
        var heavy = new List<string>();

        var guids = AssetDatabase.FindAssets("t:Mesh", new[] { lib.scanFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(obj is Mesh m)) continue;
                if (found.Contains(m)) continue;

                found.Add(m);

                int tris = m.triangles.Length / 3;
                if (tris > lib.triangleWarnThreshold)
                    heavy.Add($"{m.name} ({tris:n0} tris)");
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(lib, "Scan Backdrop Meshes");
        lib.meshes = found;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BackdropLibrary] Found {found.Count} meshes in {lib.scanFolder}.", lib);

        if (heavy.Count > 0)
            Debug.LogWarning(
                $"[BackdropLibrary] {heavy.Count} mesh(es) exceed {lib.triangleWarnThreshold:n0} " +
                $"triangles and will be multiplied by instances x split-screen cells: " +
                $"{string.Join(", ", heavy)}. Decimate them or drop them from the folder.",
                lib);
    }

    private static long TotalTriangles(BackdropLibrary lib)
    {
        long total = 0;
        foreach (var m in lib.meshes)
            if (m != null) total += m.triangles.Length / 3;
        return total;
    }
}