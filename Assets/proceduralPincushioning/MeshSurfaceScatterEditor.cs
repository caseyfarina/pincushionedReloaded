#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MeshSurfaceScatter))]
[CanEditMultipleObjects]
public class MeshSurfaceScatterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rescatter (New Seed)", GUILayout.Height(30)))
        {
            foreach (var t in targets)
                ((MeshSurfaceScatter)t).RandomizeAndScatter();
        }
        if (GUILayout.Button("Rescatter (Same Seed)", GUILayout.Height(30)))
        {
            foreach (var t in targets)
                ((MeshSurfaceScatter)t).ForceRescatter();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Quick Count", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        int[] presets = { 50, 100, 500, 1000, 2500, 5000 };
        foreach (int p in presets)
        {
            if (GUILayout.Button(p.ToString(), EditorStyles.miniButton))
            {
                foreach (var t in targets)
                    ((MeshSurfaceScatter)t).SetCount(p);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Open Batch Processor", EditorStyles.miniButton))
        {
            EditorWindow.GetWindow<BatchScatterProcessorWindow>("Batch Scatter Processor");
        }
    }
}

#endif
