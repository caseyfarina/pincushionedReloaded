#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace MidiFighter64.Samples.EditorTools
{
    /// <summary>
    /// Inspector buttons for the seeded layout systems, so an arrangement can be
    /// explored and kept without editing code or entering Play mode blind.
    /// </summary>
    [CustomEditor(typeof(MidiFighterInteriorSpawner))]
    [CanEditMultipleObjects]
    public class MidiFighterInteriorSpawnerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "The arrangement is seeded: the same Seed always rebuilds the same " +
                "layout. Reroll to explore, then keep the seed you like.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild From Seed", GUILayout.Height(28)))
                    foreach (var t in targets)
                        ((MidiFighterInteriorSpawner)t).Build();

                if (GUILayout.Button("Reroll Layout", GUILayout.Height(28)))
                    foreach (var t in targets)
                    {
                        Undo.RecordObject(t, "Reroll Interior Layout");
                        ((MidiFighterInteriorSpawner)t).RerollLayout();
                        EditorUtility.SetDirty(t);
                    }
            }

            if (GUILayout.Button("Clear Instances"))
                foreach (var t in targets)
                    ((MidiFighterInteriorSpawner)t).Clear();
        }
    }

    [CustomEditor(typeof(FloorCameraController))]
    public class FloorCameraControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (FloorCameraController)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Jump To Floor", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to preview floor moves.",
                                        MessageType.None);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
                for (int i = 7; i >= 0; i--)
                    if (GUILayout.Button(i.ToString(), GUILayout.Height(24)))
                        controller.GoToFloor(i);
        }
    }
}
#endif
