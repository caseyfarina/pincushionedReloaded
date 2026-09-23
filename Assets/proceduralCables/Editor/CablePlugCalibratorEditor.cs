using UnityEditor;
using UnityEngine;

/// <summary>
/// Capture button for the calibrator. Writing the depth is a deliberate action
/// rather than a live binding, so nudging the plug while looking at it cannot
/// silently overwrite a depth you already settled on.
/// </summary>
[CustomEditor(typeof(CablePlugCalibrator))]
public class CablePlugCalibratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var cal = (CablePlugCalibrator)target;
        EditorGUILayout.Space();

        if (cal.library == null)
        {
            EditorGUILayout.HelpBox("Assign a CableLibrary to calibrate.", MessageType.Info);
            return;
        }

        if (cal.library.Port == null)
            EditorGUILayout.HelpBox("No port in the library. Assign Port Model on the CableLibrary and press Scan.",
                MessageType.Warning);

        var connector = cal.Current;
        if (connector == null)
        {
            EditorGUILayout.HelpBox($"No plug at index {cal.connectorIndex}. The library has {cal.library.Count}.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Calibrating", connector.name);
        EditorGUILayout.LabelField("Stored depth", connector.seatOffset.ToString("F4"));
        EditorGUILayout.LabelField("Measured depth", cal.MeasuredDepth.ToString("F4"));

        using (new EditorGUI.DisabledScope(cal.plug == null))
        {
            if (GUILayout.Button($"Capture depth for {connector.name}", GUILayout.Height(26)))
            {
                Undo.RecordObject(cal.library, "Calibrate Plug Depth");
                connector.seatOffset = cal.MeasuredDepth;
                EditorUtility.SetDirty(cal.library);
                Debug.Log($"Calibrated {connector.name}: seat depth {connector.seatOffset:F4}", cal.library);
            }
        }

        if (GUILayout.Button("Move plug to stored depth"))
        {
            Undo.RecordObject(cal.plug, "Move Plug To Stored Depth");
            var p = cal.plug.localPosition;
            cal.plug.localPosition = new Vector3(p.x, p.y, connector.seatOffset);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("All plugs", EditorStyles.boldLabel);
        for (int i = 0; i < cal.library.Count; i++)
        {
            var c = cal.library.Get(i);
            if (c == null) continue;
            EditorGUILayout.LabelField($"  [{i}] {c.name}", c.seatOffset.ToString("F4"));
        }
    }
}
