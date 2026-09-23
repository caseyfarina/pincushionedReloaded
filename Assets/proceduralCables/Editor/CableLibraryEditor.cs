using UnityEditor;
using UnityEngine;

/// <summary>Scan button for the library asset, matching BackdropLibrary's inspector.</summary>
[CustomEditor(typeof(CableLibrary))]
public class CableLibraryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var lib = (CableLibrary)target;
        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Folder", GUILayout.Height(28)))
        {
            Undo.RecordObject(lib, "Scan Cable Library");
            lib.Scan();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Plugs: {lib.Count}", EditorStyles.boldLabel);
        for (int i = 0; i < lib.Count; i++)
        {
            var c = lib.Get(i);
            if (c == null) continue;
            EditorGUILayout.LabelField($"  [{i}] {c.name}", $"{c.parts.Count} parts, seat {c.seatOffset:F4}");
        }

        EditorGUILayout.LabelField("Port", lib.Port != null ? $"{lib.Port.name} ({lib.Port.parts.Count} parts)" : "NONE");
    }
}
