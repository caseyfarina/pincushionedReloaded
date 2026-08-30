using UnityEditor;
using UnityEngine;

/// <summary>
/// Automatically enables Read/Write on any mesh asset imported from
/// the scan2unity pipeline output or scatter data folders.
/// This is required by the BatchScatterProcessorWindow to bake surface samples.
///
/// Triggers on every import — no manual steps needed.
/// Matching folders (case-insensitive):
///   - unity_assets   (scan2unity FBX output)
///   - ScatterData    (baked SurfaceSampleData folder)
///   - ScatterPrefabs (optional, meshes referenced in prefabs)
/// </summary>
public class ScanImportPostprocessor : AssetPostprocessor
{
    private static readonly string[] WatchedFolders =
    {
        "unity_assets",
        "scatterdata",
        "scatterprefabs",
    };

    void OnPreprocessModel()
    {
        string lowerPath = assetPath.ToLowerInvariant();

        foreach (string folder in WatchedFolders)
        {
            if (lowerPath.Contains(folder))
            {
                var importer = assetImporter as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    Debug.Log($"[ScanImport] Read/Write enabled: {assetPath}");
                }
                return;
            }
        }
    }
}
