#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Pincushioned.SplitScreen.EditorTools
{
    /// <summary>
    /// Inspector for the MIDI binding. Warns when a bound pad is also live for the
    /// interior spawner, which is easy to miss until it fires mid-set.
    /// </summary>
    [CustomEditor(typeof(SplitScreenMidiBinding))]
    public class SplitScreenMidiBindingEditor : Editor
    {
        readonly List<Vector2Int> _pads = new List<Vector2Int>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var binding = (SplitScreenMidiBinding)target;
            _pads.Clear();
            binding.GetBoundPads(_pads);

            EditorGUILayout.Space(8);

            if (_pads.Count == 0)
            {
                EditorGUILayout.HelpBox("No pads bound. Set a row and column (both 1-8) " +
                                        "to enable an action.", MessageType.Info);
                return;
            }

            var spawner = Object.FindFirstObjectByType<MidiFighter64.Samples.MidiFighterInteriorSpawner>();
            if (spawner == null)
            {
                EditorGUILayout.HelpBox("No MidiFighterInteriorSpawner in the scene, so " +
                                        "no pad conflicts are possible.", MessageType.None);
                return;
            }

            var so = new SerializedObject(spawner);
            var reserveNav = so.FindProperty("_reserveNavigationPads").boolValue;
            var extra = so.FindProperty("_extraReservedPads");

            var reserved = new HashSet<Vector2Int>();
            for (int i = 0; i < extra.arraySize; i++)
            {
                var v = extra.GetArrayElementAtIndex(i).vector2IntValue;
                reserved.Add(v);
            }

            var clashes = new List<Vector2Int>();
            foreach (var p in _pads)
            {
                bool navReserved = reserveNav && (p.y == 8 || (p.x == 8 && p.y <= 2));
                if (!navReserved && !reserved.Contains(p)) clashes.Add(p);
            }

            if (clashes.Count == 0)
            {
                EditorGUILayout.HelpBox("All bound pads are reserved on the interior " +
                                        "spawner. No conflict.", MessageType.Info);
                return;
            }

            var names = string.Join(", ", clashes.ConvertAll(p => $"R{p.x}C{p.y}"));
            EditorGUILayout.HelpBox(
                $"Pad conflict: {names} will ALSO toggle an interior object, because " +
                "MidiFighterInteriorSpawner responds to every unreserved pad.",
                MessageType.Warning);

            if (GUILayout.Button("Reserve these pads on the spawner", GUILayout.Height(26)))
            {
                Undo.RecordObject(spawner, "Reserve Split-Screen Pads");
                foreach (var p in clashes)
                {
                    extra.arraySize++;
                    extra.GetArrayElementAtIndex(extra.arraySize - 1).vector2IntValue = p;
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(spawner);
            }
        }
    }
}
#endif
