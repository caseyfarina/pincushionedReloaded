using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scan Folder button for BackdropLibrary, plus the triangle guard.
///
/// Scans the imported prefab hierarchy rather than loose Mesh sub-assets,
/// because a model is only complete with all of its pieces: several of these
/// files hold two submeshes, and one holds two objects. Walking the hierarchy is
/// also what recovers each piece's transform relative to the model root - loose
/// Mesh assets carry no placement, so parts authored apart would stack at the
/// origin.
/// </summary>
[CustomEditor(typeof(BackdropLibrary))]
public class BackdropLibraryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var lib = (BackdropLibrary)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Scan Folder", GUILayout.Height(28)))
            Scan(lib);

        if (lib.models.Count > 0)
        {
            int parts = 0, multi = 0;
            long tris = 0;
            foreach (var m in lib.models)
            {
                if (m == null) continue;
                parts += m.parts.Count;
                if (m.parts.Count > 1) multi++;
                tris += m.triangles;
            }

            EditorGUILayout.HelpBox(
                $"{lib.models.Count} models, {parts} parts ({multi} multi-part), {tris:n0} triangles total.\n" +
                $"Each part is a separate instanced draw, so parts is what the draw-call count scales with.",
                MessageType.None);
        }
    }

    private static void Scan(BackdropLibrary lib)
    {
        if (!AssetDatabase.IsValidFolder(lib.scanFolder))
        {
            Debug.LogError($"[BackdropLibrary] Not a folder: {lib.scanFolder}", lib);
            return;
        }

        var found = new List<BackdropModel>();
        var heavy = new List<string>();

        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { lib.scanFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) continue;

            EnsureReadable(path);
            var model = BuildModel(root, lib.normalizeScale, lib.sphereThreshold);
            if (model == null) continue;

            found.Add(model);
            if (model.triangles > lib.triangleWarnThreshold)
                heavy.Add($"{model.name} ({model.triangles:n0} tris)");
        }

        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(lib, "Scan Backdrop Models");
        lib.models = found;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        int multiPart = 0, spheres = 0;
        foreach (var m in found)
        {
            if (m.parts.Count > 1) multiPart++;
            foreach (var part in m.parts) if (part.isSphere) spheres++;
        }

        Debug.Log($"[BackdropLibrary] Found {found.Count} models in {lib.scanFolder}, " +
                  $"{multiPart} of them multi-part, {spheres} spherical part(s) detected.", lib);

        if (heavy.Count > 0)
            Debug.LogWarning(
                $"[BackdropLibrary] {heavy.Count} model(s) exceed {lib.triangleWarnThreshold:n0} triangles " +
                $"and will be multiplied by instances x split-screen cells, then doubled again by the " +
                $"outline pass: {string.Join(", ", heavy)}.", lib);
    }

    /// <summary>
    /// Sphere detection needs vertex positions, and FBX import leaves meshes
    /// non-readable by default. Same switch the artifact importer flips for the
    /// scatter bake; these models are small enough that the memory cost is
    /// irrelevant.
    /// </summary>
    private static void EnsureReadable(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null || importer.isReadable) return;

        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// How close a submesh is to a sphere, as the product of two measures:
    /// how uniform its vertex distances from the centroid are, and how closely
    /// each vertex normal points along its own radius.
    ///
    /// The second measure is what makes this work. Distance uniformity alone
    /// calls a cube a sphere - all eight corners sit at the same radius - and
    /// that is exactly what it did on the first pass here. A cube's corner
    /// normals run along its faces, so they sit about 0.58 against the radial
    /// direction where a sphere's sit at 1.
    ///
    /// Measured rather than assumed from submesh order, because order is an
    /// authoring accident: the sphere is submesh 1 throughout this set and need
    /// not be in the next batch.
    /// </summary>
    private static float Sphericity(Mesh mesh, int subMesh)
    {
        if (!mesh.isReadable) return 0f;

        var verts = mesh.vertices;
        var normals = mesh.normals;
        var tris = mesh.GetTriangles(subMesh);
        if (tris.Length == 0 || verts.Length == 0) return 0f;

        // Unique vertices of this submesh only - a model's submeshes share one
        // vertex buffer, so using all of them would measure the whole model.
        var used = new HashSet<int>();
        for (int i = 0; i < tris.Length; i++) used.Add(tris[i]);
        if (used.Count < 12) return 0f;

        var centre = Vector3.zero;
        foreach (int i in used) centre += verts[i];
        centre /= used.Count;

        float mean = 0f;
        foreach (int i in used) mean += Vector3.Distance(verts[i], centre);
        mean /= used.Count;
        if (mean < 1e-5f) return 0f;

        float variance = 0f, radial = 0f;
        bool haveNormals = normals != null && normals.Length == verts.Length;

        foreach (int i in used)
        {
            Vector3 offset = verts[i] - centre;

            float d = offset.magnitude - mean;
            variance += d * d;

            if (haveNormals && offset.sqrMagnitude > 1e-10f)
                radial += Mathf.Clamp01(Vector3.Dot(normals[i].normalized, offset.normalized));
        }

        variance /= used.Count;
        float uniformity = Mathf.Clamp01(1f - Mathf.Sqrt(variance) / mean);
        float alignment = haveNormals ? radial / used.Count : 1f;

        return uniformity * alignment;
    }

    private static BackdropModel BuildModel(GameObject root, bool normalize, float sphereThreshold)
    {
        var model = new BackdropModel { name = root.name };
        var rootT = root.transform;

        var combined = new Bounds();
        bool haveBounds = false;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;

            // Relative to the model root, so a part authored away from the origin
            // keeps its offset when the model is instanced.
            Matrix4x4 local = rootT.worldToLocalMatrix * mf.transform.localToWorldMatrix;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                float round = Sphericity(mesh, sub);
                model.parts.Add(new BackdropPart
                {
                    mesh = mesh,
                    subMesh = sub,
                    localPosition = local.GetColumn(3),
                    localRotation = local.rotation,
                    localScale = local.lossyScale,
                    sphericity = round,
                    isSphere = round >= sphereThreshold,
                });
                model.triangles += (int)(mesh.GetIndexCount(sub) / 3);
            }

            var b = mesh.bounds;
            b.center = local.MultiplyPoint3x4(b.center);
            b.size = Vector3.Scale(b.size, local.lossyScale);

            if (!haveBounds) { combined = b; haveBounds = true; }
            else combined.Encapsulate(b);
        }

        if (model.parts.Count == 0) return null;

        // Fit into a one-unit box. Largest dimension rather than radius, because
        // a slab and a cube of the same radius read as wildly different sizes.
        float longest = haveBounds
            ? Mathf.Max(combined.size.x, Mathf.Max(combined.size.y, combined.size.z))
            : 1f;
        model.normalizeScale = normalize && longest > 1e-4f ? 1f / longest : 1f;

        return model;
    }
}
