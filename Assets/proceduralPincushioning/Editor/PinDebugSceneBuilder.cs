#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pincushioned.Debugging;

namespace Pincushioned.Debugging.EditorTools
{
    /// <summary>
    /// Builds a standalone scene containing every pinned mesh in a grid, for tuning
    /// and verifying pin behaviour away from the main scene.
    ///
    /// Tools &gt; Pincushioning &gt; Build Pin Debug Scene
    /// </summary>
    public static class PinDebugSceneBuilder
    {
        const string ScenePath  = "Assets/Scenes/PinDebug.unity";
        const string PrefabsDir = "Assets/pinnedMeshes";

        [MenuItem("Tools/Pincushioning/Build Pin Debug Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Neutral grey so pin colours read honestly rather than against black.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);

            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var rigGo = new GameObject("Pin Debug Rig");
            var rig = rigGo.AddComponent<PinDebugRig>();
            rig.prefabs = LoadPinnedPrefabs();
            rig.RebuildGrid();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            cam.nearClipPlane = 0.01f;
            FrameCameraOnRig(cam, rigGo);

            if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[PinDebug] Built {ScenePath} with {rig.prefabs.Count} models.\n" +
                      "No MIDI rig here deliberately — with controllers connected, a script " +
                      "recompile crashes the editor via RtMidi, so pin work belongs in a " +
                      "scene without one.");
            Selection.activeGameObject = rigGo;
        }

        static List<GameObject> LoadPinnedPrefabs()
        {
            var list = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                // Only prefabs that actually scatter — skip anything else in the folder.
                if (go.GetComponent<MeshSurfaceScatter>() == null) continue;
                list.Add(go);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        static void FrameCameraOnRig(Camera cam, GameObject rig)
        {
            var pinRig = rig.GetComponent<PinDebugRig>();
            Bounds b = pinRig != null ? pinRig.GridBounds
                                      : new Bounds(rig.transform.position, Vector3.one * 10f);

            // Frame the grid's WIDTH against the camera's horizontal FOV. Using the
            // bounds' diagonal pulls the camera much too far back on a wide, flat
            // grid, which is what made the first build unreadable.
            float halfV = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * cam.aspect);
            float distW = (b.extents.x + 0.5f) / Mathf.Max(0.05f, Mathf.Tan(halfH));
            float distD = (b.extents.z + 0.5f) / Mathf.Max(0.05f, Mathf.Tan(halfV));
            float dist  = Mathf.Max(distW, distD) * 0.85f;

            cam.transform.position = b.center + new Vector3(0f, dist * 0.55f, -dist);
            cam.transform.LookAt(b.center);
            cam.farClipPlane = Mathf.Max(1000f, dist * 4f);
        }
    }

    [CustomEditor(typeof(PinDebugRig))]
    public class PinDebugRigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rig = (PinDebugRig)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "Tune pins here, not in the main scene. Comparing models side by side " +
                "is the only reliable way to judge scale — a setting that looks right " +
                "on one artifact can be invisible on another.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Grid", GUILayout.Height(28))) rig.RebuildGrid();
                if (GUILayout.Button("Apply Settings", GUILayout.Height(28))) rig.ApplySettings();
                if (GUILayout.Button("Reroll", GUILayout.Height(28))) rig.RerollAll();
            }

            if (GUILayout.Button("Log Size Report"))
                Debug.Log("[PinDebug]\n" + rig.BuildReport());

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"models in grid: {rig.Scatters.Count}");
        }
    }
}
#endif
