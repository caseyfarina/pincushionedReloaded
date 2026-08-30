#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Batch tool that processes a folder of mesh assets:
///   1. Bakes a SurfaceSampleData asset for each mesh (position + normal + UV)
///   2. Creates a prefab with MeshFilter, MeshRenderer, and MeshSurfaceScatter
///      already wired up with the baked sample data
///
/// Access via Tools > Scatter > Batch Process Folder
/// </summary>
public class BatchScatterProcessorWindow : EditorWindow
{
    // ── Source ───────────────────────────────────────────────────────────
    private DefaultAsset sourceFolder;
    private bool includeSubfolders = true;

    // ── Bake Settings ───────────────────────────────────────────────────
    private int poolSize = 20000;
    private int bakeSeed = 12345;

    // ── Default Pins (optional) ─────────────────────────────────────────
    private bool assignDefaultPins = false;
    private List<DefaultPinEntry> defaultPins = new List<DefaultPinEntry>();

    // ── Default Scatter Settings ────────────────────────────────────────
    private int defaultScatterCount = 500;
    private Vector2 defaultScaleRange = new Vector2(0.8f, 1.2f);
    private float defaultNormalOffset = 0f;
    private bool defaultRandomYaw = true;

    // ── Default Material for target surface ─────────────────────────────
    private Material defaultSurfaceMaterial;

    // ── Output ──────────────────────────────────────────────────────────
    private string outputFolder = "Assets/ScatterData";
    private string prefabFolder = "Assets/ScatterPrefabs";

    // ── Internal ────────────────────────────────────────────────────────
    private Vector2 scrollPos;
    private List<ProcessResult> lastResults = new List<ProcessResult>();

    [System.Serializable]
    private class DefaultPinEntry
    {
        public Mesh mesh;
        public Material material;
        public float weight = 1f;
    }

    private struct ProcessResult
    {
        public string meshName;
        public bool success;
        public string message;
    }

    [MenuItem("Tools/Scatter/Batch Process Folder")]
    public static void ShowWindow()
    {
        var window = GetWindow<BatchScatterProcessorWindow>("Batch Scatter Processor");
        window.minSize = new Vector2(450, 600);
    }

    // ═════════════════════════════════════════════════════════════════════
    // GUI
    // ═════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Batch Scatter Processor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Processes all meshes in a folder:\n" +
            "  1. Bakes SurfaceSampleData assets (position + normal + UV)\n" +
            "  2. Creates prefabs with MeshSurfaceScatter wired up\n\n" +
            "Supported formats: FBX, OBJ, GLTF, Blend (any Unity-importable mesh).",
            MessageType.Info);

        // ── Source Folder ────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

        sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Mesh Folder", sourceFolder, typeof(DefaultAsset), false);

        includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", includeSubfolders);

        if (sourceFolder != null)
        {
            string path = AssetDatabase.GetAssetPath(sourceFolder);
            int meshCount = CountMeshesInFolder(path);
            EditorGUILayout.HelpBox($"Found {meshCount} mesh asset(s) in '{path}'", MessageType.None);
        }

        // ── Bake Settings ────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);

        poolSize = EditorGUILayout.IntSlider("Pool Size (per mesh)", poolSize, 1000, 100000);
        bakeSeed = EditorGUILayout.IntField("Bake Seed", bakeSeed);

        EditorGUILayout.HelpBox(
            $"Memory per mesh: ~{(poolSize * 32) / 1024}KB  |  " +
            $"(position 12B + normal 12B + uv 8B) x {poolSize}",
            MessageType.None);

        // ── Default Scatter Settings ─────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Default Scatter Settings", EditorStyles.boldLabel);

        defaultScatterCount = EditorGUILayout.IntSlider("Scatter Count", defaultScatterCount, 0, 10000);
        defaultScaleRange = EditorGUILayout.Vector2Field("Scale Range", defaultScaleRange);
        defaultNormalOffset = EditorGUILayout.FloatField("Normal Offset", defaultNormalOffset);
        defaultRandomYaw = EditorGUILayout.Toggle("Random Yaw Rotation", defaultRandomYaw);

        // ── Surface Material ─────────────────────────────────────────────
        EditorGUILayout.Space(4);
        defaultSurfaceMaterial = (Material)EditorGUILayout.ObjectField(
            "Surface Material", defaultSurfaceMaterial, typeof(Material), false);
        EditorGUILayout.HelpBox(
            "Optional. If set, overrides all prefabs. If empty, uses the material embedded in each source asset (FBX/GLTF). Falls back to Unity default if none found.",
            MessageType.None);

        // ── Default Pins (optional) ──────────────────────────────────────
        EditorGUILayout.Space(8);
        assignDefaultPins = EditorGUILayout.Foldout(assignDefaultPins, "Default Pin Variants (optional)", true, EditorStyles.foldoutHeader);

        if (assignDefaultPins)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "If configured, these pin variants will be assigned to every generated prefab. " +
                "Leave empty to set pins manually per-prefab later.",
                MessageType.None);

            for (int i = 0; i < defaultPins.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                defaultPins[i].mesh = (Mesh)EditorGUILayout.ObjectField(
                    "Mesh", defaultPins[i].mesh, typeof(Mesh), false);
                defaultPins[i].material = (Material)EditorGUILayout.ObjectField(
                    "Material", defaultPins[i].material, typeof(Material), false);
                defaultPins[i].weight = EditorGUILayout.FloatField("Weight", defaultPins[i].weight);

                EditorGUILayout.EndVertical();

                if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(60)))
                {
                    defaultPins.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            if (GUILayout.Button("+ Add Pin Variant"))
            {
                defaultPins.Add(new DefaultPinEntry());
            }

            EditorGUI.indentLevel--;
        }

        // ── Output Paths ─────────────────────────────────────────────────
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

        outputFolder = EditorGUILayout.TextField("Sample Data Folder", outputFolder);
        prefabFolder = EditorGUILayout.TextField("Prefab Folder", prefabFolder);

        // ── Process Button ───────────────────────────────────────────────
        EditorGUILayout.Space(12);
        GUI.enabled = sourceFolder != null;

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        if (GUILayout.Button("Process All Meshes", buttonStyle, GUILayout.Height(40)))
        {
            ProcessFolder();
        }
        GUI.enabled = true;

        // ── Results ──────────────────────────────────────────────────────
        if (lastResults.Count > 0)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Last Run Results", EditorStyles.boldLabel);

            int successes = lastResults.Count(r => r.success);
            int failures = lastResults.Count - successes;
            EditorGUILayout.LabelField($"{successes} succeeded, {failures} failed");

            foreach (var result in lastResults)
            {
                MessageType type = result.success ? MessageType.Info : MessageType.Error;
                EditorGUILayout.HelpBox($"{result.meshName}: {result.message}", type);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Processing
    // ═════════════════════════════════════════════════════════════════════

    private int CountMeshesInFolder(string folderPath)
    {
        return FindMeshesInFolder(folderPath).Count;
    }

    private List<(string assetPath, Mesh mesh)> FindMeshesInFolder(string folderPath)
    {
        var results = new List<(string, Mesh)>();

        SearchOption searchOpt = includeSubfolders
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        string[] extensions = { "*.fbx", "*.obj", "*.gltf", "*.glb", "*.blend", "*.asset" };

        var allFiles = new HashSet<string>();
        foreach (string ext in extensions)
        {
            string fullFolderPath = Path.GetFullPath(folderPath);
            if (Directory.Exists(fullFolderPath))
            {
                foreach (string file in Directory.GetFiles(fullFolderPath, ext, searchOpt))
                {
                    string normalized = file.Replace('\\', '/');
                    string dataPath = Application.dataPath.Replace('\\', '/');
                    string relativePath = "Assets" + normalized.Substring(dataPath.Length);
                    allFiles.Add(relativePath);
                }
            }
        }

        foreach (string assetPath in allFiles)
        {
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (var asset in subAssets)
            {
                if (asset is Mesh mesh && mesh.vertexCount > 0 && mesh.triangles.Length > 0)
                {
                    results.Add((assetPath, mesh));
                }
            }
        }

        return results;
    }

    private void ProcessFolder()
    {
        lastResults.Clear();

        string folderPath = AssetDatabase.GetAssetPath(sourceFolder);
        var meshEntries = FindMeshesInFolder(folderPath);

        if (meshEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("No Meshes Found",
                $"No mesh assets found in '{folderPath}'.\n\n" +
                "Supported formats: FBX, OBJ, GLTF, GLB, Blend, Asset.",
                "OK");
            return;
        }

        EnsureDirectoryExists(outputFolder);
        EnsureDirectoryExists(prefabFolder);

        int processed = 0;
        int total = meshEntries.Count;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var (assetPath, mesh) in meshEntries)
            {
                processed++;
                string meshName = SanitizeFileName(mesh.name);
                string displayName = $"{Path.GetFileNameWithoutExtension(assetPath)}/{mesh.name}";

                EditorUtility.DisplayProgressBar(
                    "Batch Scatter Processing",
                    $"[{processed}/{total}] {displayName}",
                    (float)processed / total);

                try
                {
                    if (!mesh.isReadable)
                    {
                        lastResults.Add(new ProcessResult
                        {
                            meshName = displayName,
                            success = false,
                            message = "Mesh is not readable. Enable Read/Write in import settings."
                        });
                        continue;
                    }

                    SurfaceSampleData sampleData = BakeSamples(mesh, meshName);
                    if (sampleData == null)
                    {
                        lastResults.Add(new ProcessResult
                        {
                            meshName = displayName,
                            success = false,
                            message = "Failed to bake sample data (no triangles?)."
                        });
                        continue;
                    }

                    string prefabPath = CreatePrefab(mesh, sampleData, meshName, assetPath);

                    string uvNote = sampleData.hasUVs ? "with UVs" : "no UVs (texture density unavailable)";
                    lastResults.Add(new ProcessResult
                    {
                        meshName = displayName,
                        success = true,
                        message = $"OK — {poolSize} samples baked ({uvNote}), prefab at {prefabPath}"
                    });
                }
                catch (System.Exception e)
                {
                    lastResults.Add(new ProcessResult
                    {
                        meshName = displayName,
                        success = false,
                        message = $"Exception: {e.Message}"
                    });
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        int successes = lastResults.Count(r => r.success);
        EditorUtility.DisplayDialog("Batch Processing Complete",
            $"Processed {total} meshes.\n" +
            $"  Succeeded: {successes}\n" +
            $"  Failed: {total - successes}\n\n" +
            $"Sample data: {outputFolder}\n" +
            $"Prefabs: {prefabFolder}",
            "OK");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Bake Samples (position + normal + UV)
    // ═════════════════════════════════════════════════════════════════════

    private SurfaceSampleData BakeSamples(Mesh mesh, string meshName)
    {
        Vector3[] verts = mesh.vertices;
        Vector3[] meshNormals = mesh.normals;
        int[] tris = mesh.triangles;
        int triCount = tris.Length / 3;

        if (triCount == 0) return null;

        // UV data (may not exist on all meshes)
        Vector2[] meshUVs = mesh.uv;
        bool hasUVs = meshUVs != null && meshUVs.Length == verts.Length;

        // Cumulative area table (local space)
        float[] cumArea = new float[triCount];
        float running = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = verts[tris[i * 3]];
            Vector3 b = verts[tris[i * 3 + 1]];
            Vector3 c = verts[tris[i * 3 + 2]];
            running += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumArea[i] = running;
        }
        float totalArea = running;

        // Sample
        Vector3[] positions = new Vector3[poolSize];
        Vector3[] normals = new Vector3[poolSize];
        Vector2[] uvs = hasUVs ? new Vector2[poolSize] : null;
        System.Random rng = new System.Random(bakeSeed);

        for (int s = 0; s < poolSize; s++)
        {
            float r = (float)(rng.NextDouble() * totalArea);
            int triIdx = System.Array.BinarySearch(cumArea, r);
            if (triIdx < 0) triIdx = ~triIdx;
            triIdx = Mathf.Clamp(triIdx, 0, triCount - 1);

            int i0 = tris[triIdx * 3];
            int i1 = tris[triIdx * 3 + 1];
            int i2 = tris[triIdx * 3 + 2];

            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            float w = 1f - u - v;

            positions[s] = verts[i0] * w + verts[i1] * u + verts[i2] * v;
            normals[s] = (meshNormals[i0] * w + meshNormals[i1] * u + meshNormals[i2] * v).normalized;

            if (hasUVs)
                uvs[s] = meshUVs[i0] * w + meshUVs[i1] * u + meshUVs[i2] * v;
        }

        // Create and save ScriptableObject
        SurfaceSampleData data = ScriptableObject.CreateInstance<SurfaceSampleData>();
        data.sourceMesh = mesh;
        data.totalSurfaceArea = totalArea;
        data.sampleCount = poolSize;
        data.hasUVs = hasUVs;
        data.positions = positions;
        data.normals = normals;
        data.uvs = uvs ?? new Vector2[0];

        string assetPath = $"{outputFolder}/{meshName}_SampleData.asset";
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        AssetDatabase.CreateAsset(data, assetPath);

        return data;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Create Prefab
    // ═════════════════════════════════════════════════════════════════════

    private string CreatePrefab(Mesh mesh, SurfaceSampleData sampleData, string meshName, string sourcePath = null)
    {
        GameObject go = new GameObject($"{meshName}_Scatter");

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (defaultSurfaceMaterial != null)
        {
            mr.sharedMaterial = defaultSurfaceMaterial;
        }
        else if (sourcePath != null)
        {
            // Collect candidate materials: sub-assets of the FBX + all .mat files in the same folder
            var candidates = new List<Material>();

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
            {
                if (asset is Material mat) candidates.Add(mat);
            }

            string folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string matPath = AssetDatabase.GUIDToAssetPath(guid);
                // Only immediate folder, not sub-folders of sub-folders
                if (Path.GetDirectoryName(matPath).Replace('\\', '/') == folder)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat != null && !candidates.Contains(mat))
                        candidates.Add(mat);
                }
            }

            Material bestMat = null;
            int bestTexCount = -1;
            foreach (var mat in candidates)
            {
                int texCount = CountAssignedTextures(mat);
                if (texCount > bestTexCount)
                {
                    bestTexCount = texCount;
                    bestMat = mat;
                }
            }
            if (bestMat != null)
                mr.sharedMaterial = bestMat;
        }

        MeshSurfaceScatter scatter = go.AddComponent<MeshSurfaceScatter>();

        SerializedObject so = new SerializedObject(scatter);

        so.FindProperty("sampleData").objectReferenceValue = sampleData;
        so.FindProperty("scatterCount").intValue = defaultScatterCount;
        so.FindProperty("scaleRange").vector2Value = defaultScaleRange;
        so.FindProperty("normalOffset").floatValue = defaultNormalOffset;
        so.FindProperty("randomYawRotation").boolValue = defaultRandomYaw;

        // Assign pin variants: use manual list if configured, otherwise auto-load defaults
        var pinEntries = GetPinEntries();
        if (pinEntries.Count > 0)
        {
            SerializedProperty pinArray = so.FindProperty("pinVariants");
            pinArray.arraySize = pinEntries.Count;

            for (int i = 0; i < pinEntries.Count; i++)
            {
                SerializedProperty element = pinArray.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("mesh").objectReferenceValue = pinEntries[i].mesh;
                element.FindPropertyRelative("material").objectReferenceValue = pinEntries[i].material;
                element.FindPropertyRelative("weight").floatValue = pinEntries[i].weight;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        string prefabPath = $"{prefabFolder}/{meshName}_Scatter.prefab";
        prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);

        DestroyImmediate(go);

        return prefabPath;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Utilities
    // ═════════════════════════════════════════════════════════════════════

    private List<DefaultPinEntry> GetPinEntries()
    {
        // Use manually configured pins if the foldout is open and has entries
        if (assignDefaultPins && defaultPins.Count > 0)
            return defaultPins;

        // Otherwise auto-load from Assets/pins/
        var entries = new List<DefaultPinEntry>();
        string pinFbxPath = "Assets/pins/Pin.fbx";

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

        if (pinMesh == null) return entries;

        string[] matNames = {
            "pinMaterial_Black", "pinMaterial_Blue", "pinMaterial_Green",
            "pinMaterial_Red", "pinMaterial_White", "pinMaterial_Yellow"
        };

        foreach (string name in matNames)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"Assets/pins/{name}.mat");
            if (mat != null)
                entries.Add(new DefaultPinEntry { mesh = pinMesh, material = mat, weight = 1f });
        }

        return entries;
    }

    private static int CountAssignedTextures(Material mat)
    {
        int count = 0;
        foreach (string prop in mat.GetTexturePropertyNames())
        {
            if (mat.GetTexture(prop) != null)
                count++;
        }
        return count;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        name = name.Replace(' ', '_');
        return name;
    }

    private static void EnsureDirectoryExists(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;

        string[] parts = assetPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

#endif
