using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One drawable piece of a connector: a mesh, which of its submeshes to draw,
/// the material that submesh was authored with, and where the piece sits
/// relative to the connector's root.
///
/// The submesh index is part of the identity because an instanced draw renders
/// exactly one submesh. A plug modelled as a rubber boot plus a metal barrel is
/// two parts, not one - submit only part zero and the barrel never appears.
/// The backdrop hit this same bug; see its CLAUDE.md.
/// </summary>
[System.Serializable]
public struct CablePart
{
    /// <summary>The source object's name, so a part with its own behaviour - the port's indicator light - can be found without relying on its index.</summary>
    public string name;

    public Mesh mesh;
    public int subMesh;
    public Material material;

    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;

    public Matrix4x4 Local => Matrix4x4.TRS(
        localPosition,
        localRotation.x == 0f && localRotation.y == 0f && localRotation.z == 0f && localRotation.w == 0f
            ? Quaternion.identity
            : localRotation,
        localScale == Vector3.zero ? Vector3.one : localScale);
}

/// <summary>One connector - an XLR, a quarter-inch plug - drawn as a unit.</summary>
[System.Serializable]
public class CableConnector
{
    public string name;
    public List<CablePart> parts = new List<CablePart>();

    /// <summary>
    /// How far this plug sits INTO the port along the port axis, in the
    /// target's local units. The port is universal; plug types differ only in
    /// how deep they sink, so this is the one number calibration produces.
    ///
    /// Measured in the calibration scene rather than guessed, because it is a
    /// property of the modelled geometry - where the barrel ends and the boot
    /// begins - and no formula recovers it from a bounding box.
    /// </summary>
    public float seatOffset;

    public bool IsUsable
    {
        get
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].mesh != null) return true;
            return false;
        }
    }
}

/// <summary>
/// The connectors a cable can be tipped with. Scanned from a folder at author
/// time into a serialized list, because AssetDatabase does not exist in a
/// player build.
///
/// An empty library is a supported state: cables render headless rather than
/// throwing, so the system is usable before any plug has been modelled.
///
/// A ScriptableObject rather than a component, because the calibrated seat
/// depths have to be the same numbers in the calibration scene, the explorer
/// and eventually the show scene. Per-scene component data would mean
/// calibrating in one place and the rig never seeing it - the same reason
/// BackdropLibrary is an asset.
/// </summary>
[CreateAssetMenu(menuName = "Performance/Cable Library", fileName = "CableLibrary")]
public class CableLibrary : ScriptableObject
{
    [Tooltip("Folder scanned by the Scan button. Every model found becomes one selectable connector.")]
    public string folder = "Assets/proceduralCables/Meshes/Connectors";

    [Tooltip("Models above this triangle count are skipped - a connector is a prop, not a scan.")]
    public int maxTriangles = 20000;

    [Tooltip("The model every plug seats into. One universal port - plug types differ only in how far they sink into it.")]
    public GameObject portModel;

    [SerializeField] private List<CableConnector> connectors = new List<CableConnector>();
    [SerializeField] private CableConnector port = new CableConnector();

    /// <summary>The universal port, or null when none has been assigned or scanned.</summary>
    public CableConnector Port => (port != null && port.IsUsable) ? port : null;

    public List<CableConnector> Connectors => connectors ?? (connectors = new List<CableConnector>());
    public int Count => Connectors.Count;

    /// <summary>Null-safe lookup. Index -1 means "no library", and is expected.</summary>
    public CableConnector Get(int index) =>
        (index < 0 || index >= Count) ? null : connectors[index];

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only. Rebuilds the connector list from the folder.
    ///
    /// Scans the imported prefab hierarchy rather than loose Mesh sub-assets, so
    /// a multi-object FBX stays one connector instead of becoming several, and
    /// each piece keeps the material and offset it was authored with.
    /// </summary>
    public void Scan()
    {
        var found = new List<CableConnector>();
        var instanced = new List<string>();
        int skipped = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) continue;

            // The port lives in this folder too but is not a plug, so it must
            // not become a choosable connector.
            if (portModel != null && root == portModel) continue;

            var connector = BuildConnector(root, instanced, out int tris);
            if (connector == null) continue;
            if (tris > maxTriangles) { skipped++; continue; }

            // Keep any depth already calibrated for this plug type - a rescan
            // after adding a connector must not wipe the others' calibration.
            connector.seatOffset = SeatOffsetFor(connector.name);
            found.Add(connector);
        }

        connectors = found;

        port = portModel != null
            ? BuildConnector(portModel, instanced, out _) ?? new CableConnector()
            : new CableConnector();

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        var sb = new System.Text.StringBuilder();
        foreach (var c in found) sb.Append(c.name).Append(" (").Append(c.parts.Count)
                                   .Append(" parts, seat ").Append(c.seatOffset.ToString("F3")).Append(")  ");

        Debug.Log($"CableLibrary: {found.Count} plug(s) in {folder} - {sb}"
                + $"port: {(Port != null ? port.name : "NONE")}. "
                + (skipped > 0 ? $"{skipped} skipped over {maxTriangles} triangles. " : "")
                + (instanced.Count > 0 ? $"Enabled GPU Instancing on: {string.Join(", ", instanced)}" : ""));
    }

    /// <summary>Depth already calibrated for a plug of this name, or 0 if new.</summary>
    private float SeatOffsetFor(string name)
    {
        if (connectors == null) return 0f;
        foreach (var c in connectors)
            if (c != null && c.name == name) return c.seatOffset;
        return 0f;
    }

    /// <summary>
    /// Every (mesh, submesh) under a model, with the material and offset it was
    /// authored with. Shared by plugs and the port so they can never drift apart
    /// in how they are read.
    /// </summary>
    private CableConnector BuildConnector(GameObject root, List<string> instanced, out int triangles)
    {
        triangles = 0;
        if (root == null) return null;

        var connector = new CableConnector { name = root.name };
        var rootT = root.transform;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;

            var mr = mf.GetComponent<MeshRenderer>();
            var authored = mr != null ? mr.sharedMaterials : null;

            // Relative to the model root, so a piece modelled away from the
            // origin keeps its offset when the model is instanced.
            Matrix4x4 local = rootT.worldToLocalMatrix * mf.transform.localToWorldMatrix;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material mat = authored != null && sub < authored.Length ? authored[sub] : null;

                // RenderMeshInstanced refuses a material with no instancing
                // variant, and imported materials never have it set. This is an
                // authoring action, so fix it here rather than making the artist
                // find the checkbox on every plug.
                if (mat != null && !mat.enableInstancing)
                {
                    mat.enableInstancing = true;
                    EditorUtility.SetDirty(mat);
                    instanced?.Add(mat.name);
                }

                connector.parts.Add(new CablePart
                {
                    name = mf.gameObject.name,
                    mesh = mesh,
                    subMesh = sub,
                    material = mat,
                    localPosition = local.GetColumn(3),
                    localRotation = local.rotation,
                    localScale = local.lossyScale,
                });
                triangles += (int)(mesh.GetIndexCount(sub) / 3);
            }
        }

        return connector.IsUsable ? connector : null;
    }
#endif
}
