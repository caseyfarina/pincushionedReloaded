// Assets/Editor/FloorSetup.cs  — run once via MCP execute_script
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

public class FloorSetup
{
    // 2-3 pinned-mesh prefabs per floor (floor index 0-7)
    static readonly string[][] PrefabsPerFloor =
    {
        /* 0 */ new[] { "Assets/pinnedMeshes/Triceratops_horridus-150k_Pinned.prefab",
                        "Assets/pinnedMeshes/sarcophagus_of_duaenre_Pinned.prefab",
                        "Assets/pinnedMeshes/colossal_foot_Pinned.prefab" },
        /* 1 */ new[] { "Assets/pinnedMeshes/Allosaurus_fragilis_maxilla-150k_Pinned.prefab",
                        "Assets/pinnedMeshes/pyramidion_of_amenemhat_iii_Pinned.prefab" },
        /* 2 */ new[] { "Assets/pinnedMeshes/amun_ram_with_pharaoh_taharqa_statue_-_ashmolean_Pinned.prefab",
                        "Assets/pinnedMeshes/thutmose_iv_and_mother_tiaa_Pinned.prefab",
                        "Assets/pinnedMeshes/standing_statue_of_khamerernebty_ii_Pinned.prefab" },
        /* 3 */ new[] { "Assets/pinnedMeshes/lintel_of_the_scribe_djeserka_wife_wadjrenpet_Pinned.prefab",
                        "Assets/pinnedMeshes/false_door_of_medunefer_Pinned.prefab" },
        /* 4 */ new[] { "Assets/pinnedMeshes/statue_of_ptolemy_iv_or_v_Pinned.prefab",
                        "Assets/pinnedMeshes/reserve_head_portrait_head_of_kanefer_Pinned.prefab",
                        "Assets/pinnedMeshes/head_of_king_menkaure_back_obscured_Pinned.prefab" },
        /* 5 */ new[] { "Assets/pinnedMeshes/the_shabako_stone_Pinned.prefab",
                        "Assets/pinnedMeshes/standing_statue_of_khamerernebty_ii_Pinned.prefab" },
        /* 6 */ new[] { "Assets/pinnedMeshes/Stegosaurus_stenops_tailspike-150k_Pinned.prefab",
                        "Assets/pinnedMeshes/amun_ram_with_pharaoh_taharqa_statue_-_ashmolean_Pinned.prefab" },
        /* 7 */ new[] { "Assets/pinnedMeshes/Allosaurus_fragilis_maxilla-150k_Pinned 1.prefab",
                        "Assets/pinnedMeshes/false_door_of_medunefer_Pinned.prefab",
                        "Assets/pinnedMeshes/the_shabako_stone_Pinned.prefab" },
    };

    // Local XZ scatter positions per floor slot (Y stays 0, relative to floor root)
    static readonly Vector3[][] PosPerFloor =
    {
        /* 0 */ new[] { new Vector3(-5f,0,-5f), new Vector3(5f,0,3f),  new Vector3(0f,0,-7f) },
        /* 1 */ new[] { new Vector3(-4f,0,4f),  new Vector3(6f,0,-4f) },
        /* 2 */ new[] { new Vector3(-3f,0,-6f), new Vector3(4f,0,2f),  new Vector3(-7f,0,3f) },
        /* 3 */ new[] { new Vector3(5f,0,5f),   new Vector3(-5f,0,1f) },
        /* 4 */ new[] { new Vector3(-6f,0,-3f), new Vector3(3f,0,6f),  new Vector3(7f,0,-2f) },
        /* 5 */ new[] { new Vector3(-4f,0,3f),  new Vector3(6f,0,-5f) },
        /* 6 */ new[] { new Vector3(2f,0,-4f),  new Vector3(-5f,0,5f) },
        /* 7 */ new[] { new Vector3(-3f,0,-3f), new Vector3(5f,0,4f),  new Vector3(0f,0,7f) },
    };

    public static void Execute()
    {
        // Ensure DOF profile folder
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/FloorDOF"))
            AssetDatabase.CreateFolder("Assets/Scenes", "FloorDOF");

        // ── 1. Prepare Floor1 as a clean template ─────────────────────────
        var floor1GO = GameObject.Find("Floor1");
        if (floor1GO == null) { Debug.LogError("[FloorSetup] Floor1 not found."); return; }

        // Remove mammoth scatter
        var mammoth = floor1GO.transform.Find("mammoth_Scatter");
        if (mammoth != null) Object.DestroyImmediate(mammoth.gameObject);

        // BoxCollider — room trigger volume (generous room footprint, 8 units tall)
        var col = floor1GO.GetComponent<BoxCollider>();
        if (col == null) col = floor1GO.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center    = new Vector3(3f, 4f, -3f);
        col.size      = new Vector3(22f, 8f, 22f);

        // FloorVolume component
        var fv = floor1GO.GetComponent<MidiFighter64.Samples.FloorVolume>();
        if (fv == null) fv = floor1GO.AddComponent<MidiFighter64.Samples.FloorVolume>();
        fv.floorIndex = 0;

        // DOF Volume child (disabled by default; CloseUpCameraController enables it)
        var dofGO = BuildDOFVolume(floor1GO, 0);
        fv.dofVolumeObject = dofGO;

        floor1GO.tag = "Floor";

        // ── 2. Duplicate to floors 2-8 before adding any prefabs ──────────
        GameObject[] floorGOs = new GameObject[8];
        floorGOs[0] = floor1GO;

        for (int i = 1; i < 8; i++)
        {
            string floorName = $"Floor{i + 1}";

            // Destroy stale duplicate if it already exists
            var stale = GameObject.Find(floorName);
            if (stale != null) Object.DestroyImmediate(stale);

            var dup = Object.Instantiate(floor1GO);
            dup.name = floorName;
            dup.transform.position = new Vector3(0f, i * 8f, 0f);
            dup.tag = "Floor";

            // Update FloorVolume index
            var dupFV = dup.GetComponent<MidiFighter64.Samples.FloorVolume>();
            dupFV.floorIndex = i;

            // Replace shared DOF profile with a per-floor one
            var oldDof = dup.transform.Find("DOF_Volume");
            if (oldDof != null) Object.DestroyImmediate(oldDof.gameObject);
            var newDof = BuildDOFVolume(dup, i);
            dupFV.dofVolumeObject = newDof;

            floorGOs[i] = dup;
        }

        // ── 3. Populate every floor with its pinned-mesh prefabs ──────────
        for (int f = 0; f < 8; f++)
            PopulateFloor(floorGOs[f], f);

        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FloorSetup] 8 floors created, populated, and scene saved.");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    static GameObject BuildDOFVolume(GameObject floorGO, int floorIndex)
    {
        var go = new GameObject("DOF_Volume");
        go.transform.SetParent(floorGO.transform, false);
        go.transform.localPosition = Vector3.zero;

        var vol      = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.weight   = 1f;
        vol.enabled  = false;   // activated by CloseUpCameraController

        var profile  = ScriptableObject.CreateInstance<VolumeProfile>();
        var dof      = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(5f);
        dof.focalLength.Override(50f);
        dof.aperture.Override(5.6f);

        string path  = $"Assets/Scenes/FloorDOF/Floor{floorIndex}_DOF.asset";
        // Overwrite if re-running
        var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(profile, path);
        vol.profile  = profile;

        return go;
    }

    static void PopulateFloor(GameObject floorGO, int floorIndex)
    {
        var paths = PrefabsPerFloor[floorIndex];
        var poses = PosPerFloor[floorIndex];

        for (int i = 0; i < paths.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (prefab == null)
            {
                Debug.LogWarning($"[FloorSetup] Prefab not found: {paths[i]}");
                continue;
            }
            var inst = PrefabUtility.InstantiatePrefab(prefab, floorGO.transform) as GameObject;
            inst.transform.localPosition = poses[i];
            inst.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
    }
}
