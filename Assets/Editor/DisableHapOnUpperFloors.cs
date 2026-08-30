// Assets/Editor/DisableHapOnUpperFloors.cs
// Removes hapPlayer and hapPlane from ALL floors (will set up properly later).
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class DisableHapOnUpperFloors
{
    public static void Execute()
    {
        for (int i = 0; i < 8; i++)
        {
            var floor = GameObject.Find($"Floor{i}");
            if (floor == null) { Debug.LogWarning($"Floor{i} not found."); continue; }

            var toDelete = new List<GameObject>();
            foreach (Transform child in floor.transform)
            {
                if (child.name.ToLower().StartsWith("hap"))
                    toDelete.Add(child.gameObject);
            }

            foreach (var go in toDelete)
            {
                Debug.Log($"Removing {go.name} from Floor{i}");
                Object.DestroyImmediate(go);
            }
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RemoveHap] Done — all hap objects removed, scene saved.");
    }
}
