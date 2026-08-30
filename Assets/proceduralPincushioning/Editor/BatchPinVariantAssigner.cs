#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class BatchPinVariantAssigner
{
    [MenuItem("Tools/Scatter/Assign Pin Color Variants to All Prefabs")]
    static void AssignVariants()
    {
        string materialFolder = "Assets/pins";
        string pinFbxPath = "Assets/pins/Pin.fbx";

        string[] prefabFolders = new[]
        {
            "Assets/pinnedMeshes",
            "Assets/ScatterPrefabs",
        };

        string[] materialNames = new[]
        {
            "pinMaterial_Black",
            "pinMaterial_Blue",
            "pinMaterial_Green",
            "pinMaterial_Red",
            "pinMaterial_White",
            "pinMaterial_Yellow",
        };

        // Load pin mesh from FBX
        Mesh pinMesh = null;
        var fbxAssets = AssetDatabase.LoadAllAssetsAtPath(pinFbxPath);
        foreach (var asset in fbxAssets)
        {
            if (asset is Mesh m && m.vertexCount > 0)
            {
                pinMesh = m;
                break;
            }
        }

        if (pinMesh == null)
        {
            Debug.LogError($"No mesh found in {pinFbxPath}");
            return;
        }

        // Load materials
        Material[] mats = new Material[materialNames.Length];
        for (int i = 0; i < materialNames.Length; i++)
        {
            string path = $"{materialFolder}/{materialNames[i]}.mat";
            mats[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mats[i] == null)
            {
                Debug.LogError($"Material not found: {path}");
                return;
            }
        }

        // Gather prefabs from all folders
        var allGuids = new HashSet<string>();
        foreach (string folder in prefabFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                allGuids.Add(guid);
        }

        int updated = 0;

        foreach (string guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = PrefabUtility.LoadPrefabContents(path);

            var scatter = prefab.GetComponent<MeshSurfaceScatter>();
            if (scatter == null)
            {
                PrefabUtility.UnloadPrefabContents(prefab);
                continue;
            }

            var so = new SerializedObject(scatter);
            var variantsProp = so.FindProperty("pinVariants");

            variantsProp.arraySize = mats.Length;
            for (int i = 0; i < mats.Length; i++)
            {
                var elem = variantsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("mesh").objectReferenceValue = pinMesh;
                elem.FindPropertyRelative("material").objectReferenceValue = mats[i];
                elem.FindPropertyRelative("weight").floatValue = 1f;
            }

            so.ApplyModifiedProperties();
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            PrefabUtility.UnloadPrefabContents(prefab);
            updated++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Pin variants assigned to {updated} prefabs using mesh '{pinMesh.name}'.");
    }
}
#endif
