using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class RenameFloors
{
    public static void Execute()
    {
        // Rename in reverse order to avoid name collisions (Floor8→Floor7 before Floor7→Floor6 etc.)
        for (int i = 7; i >= 0; i--)
        {
            var go = GameObject.Find($"Floor{i + 1}");
            if (go == null) { Debug.LogWarning($"Floor{i + 1} not found, skipping."); continue; }
            go.name = $"Floor{i}";
            Debug.Log($"Renamed Floor{i + 1} → Floor{i}");
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RenameFloors] Done.");
    }
}
