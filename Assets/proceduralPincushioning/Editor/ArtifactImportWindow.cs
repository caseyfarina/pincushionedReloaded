#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Takes a scan2unity output folder and produces finished, pinnable artifacts.
///
/// This closes steps 3-6 of the ingestion chain, which previously had no tooling
/// and were done by hand (see the root CLAUDE.md). For each model it:
///
///   3. copies the FBX + URP maps into Assets/Artifacts/{stem}/
///   4. fixes the four Unity import defaults, all of which are wrong here
///   5. builds {stem}_Surface.mat (URP/Lit) and wires every map it finds
///   6. bakes SurfaceSampleData and emits {stem}_Pinned.prefab, pin variants
///      and house scatter settings already applied
///
/// Access via Tools > Scatter > Import Processed Artifacts.
/// </summary>
public class ArtifactImportWindow : EditorWindow
{
    // -- Source -----------------------------------------------------------
    [SerializeField] private string sourceFolder = "";

    // -- Bake -------------------------------------------------------------
    [SerializeField] private int poolSize = 20000;
    [SerializeField] private int bakeSeed = 12345;
    [SerializeField] private int scatterCount = 2000;

    // -- Steps ------------------------------------------------------------
    [SerializeField] private bool copyAssets = true;
    [SerializeField] private bool fixImportSettings = true;
    [SerializeField] private bool buildMaterials = true;
    [SerializeField] private bool bakeAndPin = true;
    [SerializeField] private bool rebuildPinDebug = true;

    private Vector2 scroll;
    private readonly List<string> log = new List<string>();

    private const string ArtifactsRoot = "Assets/Artifacts";
    private const string ScatterDataRoot = "Assets/ScatterData";
    private const string PinnedRoot = "Assets/pinnedMeshes";
    private const string PinsRoot = "Assets/pins";

    // Textures the pipeline emits, by URP suffix.
    private static readonly string[] MapSuffixes =
        { "_BaseColor.png", "_Normal.png", "_MetallicSmoothness.png", "_Occlusion.png" };

    [MenuItem("Tools/Scatter/Import Processed Artifacts")]
    public static void ShowWindow() =>
        GetWindow<ArtifactImportWindow>("Import Artifacts").minSize = new Vector2(460, 420);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "A scan2unity output folder (e.g. 3DObjectProcessing/unity_assets). " +
            "It lives OUTSIDE Assets/, so pick it with Browse.", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            sourceFolder = EditorGUILayout.TextField("Pipeline Output", sourceFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFolderPanel("scan2unity output folder", sourceFolder, "");
                if (!string.IsNullOrEmpty(p)) sourceFolder = p;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Steps", EditorStyles.boldLabel);
        copyAssets = EditorGUILayout.Toggle("3. Copy into Assets", copyAssets);
        fixImportSettings = EditorGUILayout.Toggle("4. Fix import settings", fixImportSettings);
        buildMaterials = EditorGUILayout.Toggle("5. Build materials", buildMaterials);
        bakeAndPin = EditorGUILayout.Toggle("6. Bake + make _Pinned", bakeAndPin);
        rebuildPinDebug = EditorGUILayout.Toggle("   Rebuild PinDebug grid", rebuildPinDebug);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
        poolSize = EditorGUILayout.IntSlider("Pool Size", poolSize, 1000, 60000);
        bakeSeed = EditorGUILayout.IntField("Bake Seed", bakeSeed);
        scatterCount = EditorGUILayout.IntSlider("Scatter Count", scatterCount, 0, 10000);

        EditorGUILayout.Space();
        bool bad = string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder);
        using (new EditorGUI.DisabledScope(bad))
        {
            if (GUILayout.Button("Import Artifacts", GUILayout.Height(32))) Run();
        }

        if (log.Count == 0) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var line in log) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndScrollView();
    }

    // =====================================================================

    private void Run()
    {
        log.Clear();
        var stems = Directory.GetDirectories(sourceFolder)
                             .Select(Path.GetFileName)
                             .Where(n => File.Exists(Path.Combine(sourceFolder, n, n + ".fbx")))
                             .OrderBy(n => n).ToList();

        if (stems.Count == 0)
        {
            log.Add("No matching {stem}/{stem}.fbx folders found.");
            return;
        }

        int ok = 0;
        try
        {
            if (copyAssets)
            {
                for (int i = 0; i < stems.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Import Artifacts",
                        "Copying " + stems[i], (float)i / stems.Count);
                    CopyOne(stems[i]);
                }
                AssetDatabase.Refresh();   // importers must exist before step 4
            }

            for (int i = 0; i < stems.Count; i++)
            {
                string stem = stems[i];
                EditorUtility.DisplayProgressBar("Import Artifacts",
                    "Processing " + stem, (float)i / stems.Count);

                if (fixImportSettings) FixImports(stem);
                Material mat = buildMaterials ? BuildMaterial(stem) : LoadMaterial(stem);
                if (bakeAndPin && !BakeAndPin(stem, mat)) continue;
                ok++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (rebuildPinDebug) RebuildPinDebug();

        log.Insert(0, ok + "/" + stems.Count + " artifacts imported");
    }

    /// <summary>Copy the FBX and any URP maps the pipeline produced.</summary>
    private void CopyOne(string stem)
    {
        string src = Path.Combine(sourceFolder, stem);
        string dst = Path.Combine(ArtifactsRoot, stem);
        Directory.CreateDirectory(dst);

        File.Copy(Path.Combine(src, stem + ".fbx"), Path.Combine(dst, stem + ".fbx"), true);

        foreach (var suffix in MapSuffixes)
        {
            string f = Path.Combine(src, stem + suffix);
            if (!File.Exists(f)) continue;
            File.Copy(f, Path.Combine(dst, stem + suffix), true);

            // Guard against the Stage 6 regression: Blender's placeholder is a
            // ~18 KB grey checker that silently replaced real 4-5 MB albedo.
            long bytes = new FileInfo(f).Length;
            if (suffix == "_BaseColor.png" && bytes < 50000)
            {
                log.Add("WARNING " + stem + ": BaseColor is only " + (bytes / 1024) +
                        " KB, likely a placeholder checkerboard rather than the real scan texture.");
            }
        }
    }

    /// <summary>All four Unity defaults are wrong for this project.</summary>
    private void FixImports(string stem)
    {
        string fbx = ArtifactsRoot + "/" + stem + "/" + stem + ".fbx";
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi != null && !mi.isReadable)
        {
            mi.isReadable = true;          // the bake reads verts/normals/UVs
            mi.SaveAndReimport();
        }

        foreach (var suffix in MapSuffixes)
        {
            string p = ArtifactsRoot + "/" + stem + "/" + stem + suffix;
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool changed = false;

            if (suffix == "_Normal.png")
            {
                // Pipeline emits OpenGL-convention normals.
                if (ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
                if (!ti.flipGreenChannel) { ti.flipGreenChannel = true; changed = true; }
            }
            else if (suffix != "_BaseColor.png")
            {
                // Metallic/Smoothness and Occlusion are DATA, not colour.
                if (ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            }

            if (changed) ti.SaveAndReimport();
        }
    }

    private static string MaterialPath(string stem)
    {
        return ArtifactsRoot + "/" + stem + "/" + stem + "_Surface.mat";
    }

    private static Material LoadMaterial(string stem)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(stem));
    }

    /// <summary>Create or refresh the URP/Lit surface material, wiring every map found.</summary>
    private Material BuildMaterial(string stem)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            log.Add("ERROR " + stem + ": URP/Lit shader not found.");
            return null;
        }

        string path = MaterialPath(stem);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = lit;

        Texture2D baseColor = LoadMap(stem, "_BaseColor.png");
        if (baseColor != null)
        {
            mat.SetTexture("_BaseMap", baseColor);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseColor);
        }

        Texture2D normal = LoadMap(stem, "_Normal.png");
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }

        Texture2D ms = LoadMap(stem, "_MetallicSmoothness.png");
        if (ms != null)
        {
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f);
        }

        Texture2D occ = LoadMap(stem, "_Occlusion.png");
        if (occ != null)
        {
            mat.SetTexture("_OcclusionMap", occ);
            mat.EnableKeyword("_OCCLUSIONMAP");
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Texture2D LoadMap(string stem, string suffix)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(
            ArtifactsRoot + "/" + stem + "/" + stem + suffix);
    }

    /// <summary>Bake the sample pool and emit a ready-to-use _Pinned prefab.</summary>
    private bool BakeAndPin(string stem, Material surface)
    {
        string fbx = ArtifactsRoot + "/" + stem + "/" + stem + ".fbx";
        Mesh mesh = LargestMesh(fbx);
        if (mesh == null) { log.Add("SKIP " + stem + ": no mesh in FBX."); return false; }
        if (!mesh.isReadable) { log.Add("SKIP " + stem + ": mesh not readable."); return false; }

        Directory.CreateDirectory(ScatterDataRoot);
        var data = SurfaceSampleBaker.Bake(
            mesh, poolSize, bakeSeed,
            ScatterDataRoot + "/" + stem + "_SampleData.asset", true);

        if (data == null) { log.Add("SKIP " + stem + ": bake failed, no triangles."); return false; }

        Directory.CreateDirectory(PinnedRoot);
        string prefabPath = PinnedRoot + "/" + stem + "_Pinned.prefab";

        // Build the shell, save it, and only THEN configure the component on the
        // saved asset.
        //
        // Configuring a temporary scene GameObject and saving it looks simpler
        // and mostly works, but the object reference to the freshly baked
        // SurfaceSampleData does not survive: the SaveAndReimport() calls in
        // FixImports() disturb the AssetDatabase enough that the pending
        // SerializedObject value is dropped, leaving a _Pinned prefab whose
        // sampleData is null while every value-type field looks correct.
        // Editing the persisted asset avoids that entirely.
        var go = new GameObject(stem + "_Pinned");
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = surface;
            go.AddComponent<MeshSurfaceScatter>();
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        }
        finally { DestroyImmediate(go); }

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (asset == null) { log.Add("SKIP " + stem + ": prefab did not save."); return false; }

        var scatter = asset.GetComponent<MeshSurfaceScatter>();
        var so = new SerializedObject(scatter);
        so.FindProperty("sampleData").objectReferenceValue = data;
        so.FindProperty("scatterCount").intValue = scatterCount;
        // The house look already IS the field-default set; write it anyway so
        // the prefab states it explicitly rather than inheriting silently.
        so.FindProperty("scaleMode").enumValueIndex = 0;               // RelativeToModel
        so.FindProperty("relativeScaleRange").vector2Value = new Vector2(0.03f, 0.20f);
        so.FindProperty("scaleBias").floatValue = 3f;
        so.FindProperty("maxTiltAngle").floatValue = 12f;
        ApplyPinVariants(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        if (scatter.SampleData == null)
            log.Add("WARNING " + stem + ": sample data did not attach to the prefab.");

        if (!data.hasUVs) log.Add("NOTE " + stem + ": no UVs, Texture density mode unavailable.");
        log.Add("OK   " + stem + ": " + poolSize + " samples, " +
                (surface != null ? "material wired" : "NO MATERIAL"));

        return true;
    }

    /// <summary>Pin.fbx mesh x every pinMaterial_*.mat, equal weight.</summary>
    private void ApplyPinVariants(SerializedObject so)
    {
        Mesh pin = null;
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(PinsRoot + "/Pin.fbx"))
        {
            var m = o as Mesh;
            if (m != null) { pin = m; break; }
        }

        var mats = AssetDatabase.FindAssets("t:Material", new[] { PinsRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p).StartsWith("pinMaterial_"))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(m => m != null)
            .ToList();

        var pv = so.FindProperty("pinVariants");
        if (pin == null || mats.Count == 0)
        {
            log.Add("WARNING: no Pin.fbx mesh or no pinMaterial_* assets found, pins will not render.");
            pv.arraySize = 0;
            return;
        }

        pv.arraySize = mats.Count;
        for (int i = 0; i < mats.Count; i++)
        {
            var e = pv.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("mesh").objectReferenceValue = pin;
            e.FindPropertyRelative("material").objectReferenceValue = mats[i];
            e.FindPropertyRelative("weight").floatValue = 1f;
        }
    }

    /// <summary>Largest mesh in the FBX. Scans are single-mesh, but be safe.</summary>
    private static Mesh LargestMesh(string assetPath)
    {
        Mesh best = null;
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
        {
            var m = o as Mesh;
            if (m != null && (best == null || m.vertexCount > best.vertexCount)) best = m;
        }
        return best != null ? best : AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    /// <summary>
    /// Repopulate and rebuild the debug grid. RebuildGrid() early-returns on an
    /// empty list and does NOT self-populate, so the list is filled here first.
    /// </summary>
    private void RebuildPinDebug()
    {
        var rig = Object.FindFirstObjectByType<Pincushioned.Debugging.PinDebugRig>();
        if (rig == null)
        {
            log.Add("NOTE: PinDebug rig not in the open scene, grid not rebuilt.");
            return;
        }

        rig.prefabs.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PinnedRoot }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null && go.GetComponent<MeshSurfaceScatter>() != null) rig.prefabs.Add(go);
        }
        rig.prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        rig.RebuildGrid();
        EditorUtility.SetDirty(rig);
        log.Add("PinDebug grid rebuilt: " + rig.prefabs.Count + " models.");
    }
}
#endif
