using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// One-shot tool: bakes SurfaceSampleData for each dinosaur FBX in Assets/dinosaurs/,
/// creates URP surface materials, then outputs fully wired prefabs to Assets/pinnedMeshes/.
///
/// Run via: Tools > Scatter > Setup Dinosaur Pinned Meshes
/// Safe to re-run — GenerateUniqueAssetPath prevents overwrites.
/// </summary>
public static class DinosaurPinnedMeshSetup
{
    private const string DinoFolder   = "Assets/dinosaurs";
    private const string ScatterData  = "Assets/ScatterData";
    private const string PinnedMeshes = "Assets/pinnedMeshes";
    private const string PinFbxPath   = "Assets/pins/Pin.fbx";
    private const string PinMatPath   = "Assets/pins/pinMaterial.mat";

    private const int   PoolSize      = 20000;
    private const int   ScatterCount  = 2000;
    private const int   BakeSeed      = 12345;
    // Target pin height as fraction of surface mesh diagonal.
    // 0.08 → pins are ~8% of the mesh's longest extent (err on the large/visible side).
    private const float PinFractionMin = 0.06f;
    private const float PinFractionMax = 0.10f;
    private const float NormalOffsetFraction = 0.005f;

    [MenuItem("Tools/Scatter/Setup Dinosaur Pinned Meshes")]
    public static void Run()
    {
        // ── Validate pin assets ──────────────────────────────────────────
        Mesh pinMesh = LoadFirstMesh(PinFbxPath);
        if (pinMesh == null)
        {
            Debug.LogError($"[DinosaurPinnedMeshSetup] No mesh found in {PinFbxPath}. Aborting.");
            return;
        }

        Material pinMat = AssetDatabase.LoadAssetAtPath<Material>(PinMatPath);
        if (pinMat == null)
        {
            Debug.LogError($"[DinosaurPinnedMeshSetup] Material not found at {PinMatPath}. Aborting.");
            return;
        }

        // ── Find dinosaur FBXs ───────────────────────────────────────────
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { DinoFolder });
        if (guids.Length == 0)
        {
            Debug.LogError($"[DinosaurPinnedMeshSetup] No model assets found in {DinoFolder}. Aborting.");
            return;
        }

        // ── Pass 1: reimport — enable Read/Write + fix normal maps ───────
        // Must happen outside StartAssetEditing so SaveAndReimport works.
        foreach (string guid in guids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            EnableReadWrite(fbxPath);

            string dir      = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string meshBase = GetMeshBase(fbxPath);
            SetNormalMapType($"{dir}/{meshBase}_Normal.png");
        }

        // ── Pass 2: create assets ────────────────────────────────────────
        EnsureFolder(ScatterData);
        EnsureFolder(PinnedMeshes);

        AssetDatabase.StartAssetEditing();
        try
        {
            int total     = guids.Length;
            int processed = 0;

            foreach (string guid in guids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                processed++;
                EditorUtility.DisplayProgressBar(
                    "Dinosaur Pinned Mesh Setup",
                    $"[{processed}/{total}] {Path.GetFileNameWithoutExtension(fbxPath)}",
                    (float)processed / total);

                ProcessDino(fbxPath, pinMesh, pinMat);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[DinosaurPinnedMeshSetup] Done. Prefabs in {PinnedMeshes}.");
        EditorUtility.DisplayDialog("Done",
            $"Dinosaur pinned mesh prefabs created in {PinnedMeshes}.\n\n" +
            "Adjust scaleRange on each MeshSurfaceScatter component to match mesh scale.",
            "OK");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Per-Mesh Processing
    // ═════════════════════════════════════════════════════════════════════

    private static void ProcessDino(string fbxPath, Mesh pinMesh, Material pinMat)
    {
        Mesh mesh = LoadFirstMesh(fbxPath);
        if (mesh == null)
        {
            Debug.LogWarning($"[DinosaurPinnedMeshSetup] No readable mesh in {fbxPath}. Skipping.");
            return;
        }

        string dir      = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
        string meshBase = GetMeshBase(fbxPath);

        // ── Surface material ─────────────────────────────────────────────
        Texture2D bcTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{meshBase}_BaseColor.png");
        Texture2D nTex  = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{meshBase}_Normal.png");

        Material surfMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        surfMat.name = $"{meshBase}_Surface";
        if (bcTex != null)
        {
            surfMat.SetTexture("_BaseMap", bcTex);
            surfMat.SetColor("_BaseColor", Color.white);
        }
        if (nTex != null)
        {
            surfMat.SetTexture("_BumpMap", nTex);
            surfMat.SetFloat("_BumpScale", 1f);
            surfMat.EnableKeyword("_NORMALMAP");
        }
        surfMat.SetFloat("_Smoothness", 0.2f);

        string matPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{meshBase}_Surface.mat");
        AssetDatabase.CreateAsset(surfMat, matPath);

        // ── Bounds-relative pin scale ────────────────────────────────────
        // We want the final rendered pin to be ~6-10% of the surface mesh's
        // diagonal. Since the pin mesh has its own size, we compute:
        //   scale = (surfaceDiagonal * fraction) / pinDiagonal
        // This makes results consistent regardless of pin or surface mesh units.
        float surfaceDiag = mesh.bounds.size.magnitude;
        float pinDiag     = Mathf.Max(pinMesh.bounds.size.magnitude, 0.001f);

        float targetMinHeight = surfaceDiag * PinFractionMin;
        float targetMaxHeight = surfaceDiag * PinFractionMax;
        Vector2 scaleRange    = new Vector2(targetMinHeight / pinDiag, targetMaxHeight / pinDiag);
        float normalOffset    = surfaceDiag * NormalOffsetFraction;

        Debug.Log($"[DinosaurPinnedMeshSetup] {meshBase} — surface diagonal: {surfaceDiag:F3}, " +
                  $"pin diagonal: {pinDiag:F3} → scaleRange [{scaleRange.x:F3}, {scaleRange.y:F3}], " +
                  $"normalOffset {normalOffset:F4}");

        // ── Bake surface samples ─────────────────────────────────────────
        SurfaceSampleData sampleData = BakeSamples(mesh);
        string sdPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{ScatterData}/{SanitizeName(mesh.name)}_SampleData.asset");
        AssetDatabase.CreateAsset(sampleData, sdPath);

        // ── Prefab ───────────────────────────────────────────────────────
        GameObject go = new GameObject($"{meshBase}_Pinned");

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = surfMat;

        MeshSurfaceScatter scatter = go.AddComponent<MeshSurfaceScatter>();
        SerializedObject so = new SerializedObject(scatter);

        so.FindProperty("sampleData").objectReferenceValue = sampleData;
        so.FindProperty("scatterCount").intValue           = ScatterCount;
        so.FindProperty("scaleRange").vector2Value         = scaleRange;
        so.FindProperty("normalOffset").floatValue         = normalOffset;
        so.FindProperty("randomYawRotation").boolValue     = true;

        SerializedProperty pinArray = so.FindProperty("pinVariants");
        pinArray.arraySize = 1;
        SerializedProperty elem = pinArray.GetArrayElementAtIndex(0);
        elem.FindPropertyRelative("mesh").objectReferenceValue     = pinMesh;
        elem.FindPropertyRelative("material").objectReferenceValue = pinMat;
        elem.FindPropertyRelative("weight").floatValue             = 1f;

        so.ApplyModifiedPropertiesWithoutUndo();

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{PinnedMeshes}/{meshBase}_Pinned.prefab");
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);

        Debug.Log($"[DinosaurPinnedMeshSetup] Created {prefabPath}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Sample Baking (mirrors BatchScatterProcessorWindow logic)
    // ═════════════════════════════════════════════════════════════════════

    private static SurfaceSampleData BakeSamples(Mesh mesh)
    {
        Vector3[] verts       = mesh.vertices;
        Vector3[] meshNormals = mesh.normals;
        int[]     tris        = mesh.triangles;
        int       triCount    = tris.Length / 3;

        Vector2[] meshUVs = mesh.uv;
        bool      hasUVs  = meshUVs != null && meshUVs.Length == verts.Length;

        // Cumulative area table (local space)
        float[] cumArea = new float[triCount];
        float   running = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = verts[tris[i * 3]];
            Vector3 b = verts[tris[i * 3 + 1]];
            Vector3 c = verts[tris[i * 3 + 2]];
            running    += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumArea[i]  = running;
        }
        float totalArea = running;

        Vector3[]     positions = new Vector3[PoolSize];
        Vector3[]     normals   = new Vector3[PoolSize];
        Vector2[]     uvs       = hasUVs ? new Vector2[PoolSize] : null;
        System.Random rng       = new System.Random(BakeSeed);

        for (int s = 0; s < PoolSize; s++)
        {
            float r      = (float)(rng.NextDouble() * totalArea);
            int   triIdx = System.Array.BinarySearch(cumArea, r);
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
            normals[s]   = (meshNormals[i0] * w + meshNormals[i1] * u + meshNormals[i2] * v).normalized;

            if (hasUVs)
                uvs[s] = meshUVs[i0] * w + meshUVs[i1] * u + meshUVs[i2] * v;
        }

        SurfaceSampleData data = ScriptableObject.CreateInstance<SurfaceSampleData>();
        data.sourceMesh       = mesh;
        data.totalSurfaceArea = totalArea;
        data.sampleCount      = PoolSize;
        data.hasUVs           = hasUVs;
        data.positions        = positions;
        data.normals          = normals;
        data.uvs              = uvs ?? new Vector2[0];

        return data;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    private static void EnableReadWrite(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null || importer.isReadable) return;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    private static void SetNormalMapType(string texPath)
    {
        var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti == null || ti.textureType == TextureImporterType.NormalMap) return;
        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
    }

    private static Mesh LoadFirstMesh(string assetPath)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            if (asset is Mesh m && m.vertexCount > 0)
                return m;
        return null;
    }

    /// <summary>
    /// Strips "_LOD0" suffix from the FBX filename to get the shared base name
    /// used by the accompanying texture files.
    /// </summary>
    private static string GetMeshBase(string fbxPath)
    {
        string name = Path.GetFileNameWithoutExtension(fbxPath);
        if (name.EndsWith("_LOD0")) name = name.Substring(0, name.Length - 5);
        return name;
    }

    private static string SanitizeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        string[] parts   = assetPath.Split('/');
        string   current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
