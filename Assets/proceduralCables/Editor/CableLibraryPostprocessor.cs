using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-scans a CableLibrary whenever a model it draws from is reimported.
///
/// The library bakes each part's offset and scale at scan time. Changing an
/// FBX's Scale Factor moves its children relative to the root, so those baked
/// offsets silently stop matching the rescaled mesh - the housing resizes and
/// its port and light stay where they were, floating clear of it. Nothing
/// errors; the model simply comes apart.
///
/// Re-scanning on import is the fix, because the alternative is remembering to
/// press a button after every scale tweak, and forgetting it looks like a
/// broken mesh rather than stale data.
/// </summary>
public class CableLibraryPostprocessor : AssetPostprocessor
{
    private static bool _rescanning;

    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        // Scan dirties and saves the library, which re-enters this callback.
        if (_rescanning || imported == null || imported.Length == 0) return;

        var libs = AssetDatabase.FindAssets("t:CableLibrary");
        if (libs.Length == 0) return;

        try
        {
            _rescanning = true;

            foreach (string guid in libs)
            {
                var lib = AssetDatabase.LoadAssetAtPath<CableLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (lib == null || !Touches(lib, imported)) continue;

                lib.Scan();
                Debug.Log($"CableLibrary '{lib.name}' rescanned: a model it uses was reimported.", lib);
            }
        }
        finally
        {
            _rescanning = false;
        }
    }

    /// <summary>Whether any imported path is a model this library reads.</summary>
    private static bool Touches(CableLibrary lib, string[] imported)
    {
        string portPath = lib.portModel != null ? AssetDatabase.GetAssetPath(lib.portModel) : null;
        string folder = string.IsNullOrEmpty(lib.folder) ? null : lib.folder.TrimEnd('/') + "/";

        foreach (string path in imported)
        {
            if (!string.IsNullOrEmpty(portPath) && path == portPath) return true;

            // Only models matter - a material or texture reimport does not move
            // any part, and rescanning on those would be constant churn.
            if (folder != null && path.StartsWith(folder, System.StringComparison.OrdinalIgnoreCase)
                && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return true;
        }

        return false;
    }
}
