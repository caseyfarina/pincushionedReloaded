#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Pincushioned.SplitScreen.EditorTools
{
    /// <summary>
    /// Inspector for the split-screen rig, with a preview of the quadtree layout
    /// so cell arrangements can be judged without entering Play mode.
    /// </summary>
    [CustomEditor(typeof(SplitScreenCameraRig))]
    public class SplitScreenCameraRigEditor : Editor
    {
        readonly List<Rect> _preview = new List<Rect>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rig = (SplitScreenCameraRig)target;
            var so  = serializedObject;

            int   subdivisions = so.FindProperty("_subdivisions").intValue;
            int   seed         = so.FindProperty("_layoutSeed").intValue;
            var   strategy     = (QuadtreeSplitStrategy)so.FindProperty("_splitStrategy").enumValueIndex;
            float border       = so.FindProperty("_borderThickness").floatValue;
            Color borderColor  = so.FindProperty("_borderColor").colorValue;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Layout preview", EditorStyles.boldLabel);

            QuadtreeLayout.Build(subdivisions, strategy, seed, _preview);

            int cells = _preview.Count;
            int subCams = so.FindProperty("_mainCameraTakesLargestCell").boolValue
                ? Mathf.Max(0, cells - 1) : cells;

            EditorGUILayout.HelpBox(
                $"{cells} cells  ({subCams} sub-cameras + " +
                (subCams == cells ? "no main" : "the main camera") + ")\n" +
                "Each cell is a full scene render. Cost is dominated by per-camera " +
                "culling and post-processing, which do not shrink with cell size.",
                cells > 16 ? MessageType.Warning : MessageType.Info);

            DrawPreview(_preview, border, borderColor);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild", GUILayout.Height(26)))
                    rig.Rebuild();

                if (GUILayout.Button("Reroll Layout", GUILayout.Height(26)))
                {
                    Undo.RecordObject(rig, "Reroll Split-Screen Layout");
                    rig.RerollLayout();
                    EditorUtility.SetDirty(rig);
                }

                if (GUILayout.Button("Reset Camera Positions", GUILayout.Height(26)))
                {
                    Undo.RecordObject(rig, "Reset Split-Screen Cameras");
                    rig.ResetCameraPositions();
                    EditorUtility.SetDirty(rig);
                }
            }

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "Cameras are created at runtime. Enter Play mode to see the rig; " +
                    "the preview above reflects the layout either way.",
                    MessageType.None);
        }

        static void DrawPreview(List<Rect> cells, float border, Color borderColor)
        {
            // 16:9 preview box matching viewport space (origin bottom-left).
            Rect box = GUILayoutUtility.GetRect(10f, 190f);
            float w = Mathf.Min(box.width, 320f);
            float h = w * 9f / 16f;
            box = new Rect(box.x + (box.width - w) * 0.5f, box.y, w, h);

            EditorGUI.DrawRect(box, borderColor);

            float bx = border / 1920f;
            float by = border / 1080f;

            foreach (var c in cells)
            {
                Rect r = QuadtreeLayout.Inset(c, bx, by);
                // Flip Y: viewport is bottom-left, IMGUI is top-left.
                var draw = new Rect(
                    box.x + r.x * box.width,
                    box.y + (1f - r.y - r.height) * box.height,
                    r.width  * box.width,
                    r.height * box.height);
                EditorGUI.DrawRect(draw, new Color(0.22f, 0.25f, 0.30f));
            }

            GUILayoutUtility.GetRect(10f, 4f);
        }
    }

    [CustomEditor(typeof(PointOfInterestFinder))]
    public class PointOfInterestFinderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var finder = (PointOfInterestFinder)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Point of interest", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "This scene's artifacts have no colliders, so the renderer-bounds " +
                "fallback is what actually finds them. The pins are GPU-instanced " +
                "and can never be hit.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Evaluate Now", GUILayout.Height(24)))
                    finder.Evaluate();
                if (GUILayout.Button("Refresh Renderer Cache"))
                    finder.RefreshRendererCache();
            }

            if (Application.isPlaying && finder.HasPoint)
            {
                EditorGUILayout.LabelField("Resolved", finder.Point.ToString("F2"));
                EditorGUILayout.LabelField("Target",
                    finder.Target != null ? finder.Target.name : "<none — using default distance>");
                Repaint();
            }
        }
    }
}
#endif
