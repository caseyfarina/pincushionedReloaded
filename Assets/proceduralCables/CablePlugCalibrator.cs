using UnityEngine;

/// <summary>
/// Measures how far each plug type sinks into the universal port.
///
/// The port is drawn at this object's origin facing its +Z, matching the rig,
/// where ports lie in the target's XY plane and plugs seat along its Z. Drag
/// the Plug child along that axis until it looks right, then capture - the
/// seat depth is simply the plug's local Z, so what you see in the scene is
/// literally the number that gets stored.
///
/// It exists because the depth is a property of the modelled geometry - where
/// the barrel ends and the boot begins - which no bounding box recovers, and
/// which differs per plug type even though the port is universal. Adding a
/// fourth plug later means calibrating one more number here, not re-deriving
/// anything.
/// </summary>
[ExecuteAlways]
public class CablePlugCalibrator : MonoBehaviour
{
    [Tooltip("The library being calibrated. Captured depths are written into its connectors.")]
    public CableLibrary library;

    [Tooltip("Which plug type is being calibrated - an index into the library's connectors.")]
    public int connectorIndex;

    [Tooltip("Drag this along the port's Z axis until the plug looks correctly seated. Its local Z is the depth.")]
    public Transform plug;

    [Tooltip("Scale applied to both meshes, matching the rig's Head Scale so calibration is done at performance size.")]
    public float headScale = 1f;

    [Tooltip("Local rotation applied to the plug mesh before it is aimed, matching CableInstrument.")]
    public Vector3 headOrientationEuler = new Vector3(90f, 0f, 0f);

    /// <summary>The depth that would be captured right now.</summary>
    public float MeasuredDepth => plug != null ? plug.localPosition.z : 0f;

    public CableConnector Current =>
        library != null ? library.Get(connectorIndex) : null;

    private void Update()
    {
        if (library == null) return;

        DrawConnector(library.Port, Matrix4x4.TRS(
            transform.position, transform.rotation, Vector3.one * headScale));

        var plugConnector = Current;
        if (plugConnector == null || plug == null) return;

        // Aimed exactly as CableInstrument aims a landed plug: along the port's
        // Z, with the same local orientation offset. Calibrating against a
        // differently-oriented preview would bake in an error.
        DrawConnector(plugConnector, Matrix4x4.TRS(
            plug.position,
            transform.rotation * Quaternion.Euler(headOrientationEuler),
            Vector3.one * headScale));
    }

    private static void DrawConnector(CableConnector connector, Matrix4x4 trs)
    {
        if (connector == null) return;

        foreach (var part in connector.parts)
        {
            if (part.mesh == null || part.material == null) continue;
            Graphics.RenderMesh(new RenderParams(part.material), part.mesh, part.subMesh, trs * part.Local);
        }
    }

    private void OnDrawGizmos()
    {
        // The seating axis, so it is obvious which way "in" is.
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawLine(transform.position - transform.forward * 2f,
                        transform.position + transform.forward * 2f);

        if (plug == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(transform.position, plug.position);
        Gizmos.DrawWireSphere(plug.position, 0.05f);
    }
}
