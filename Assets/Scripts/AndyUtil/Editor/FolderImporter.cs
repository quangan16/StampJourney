using UnityEditor;
using UnityEngine;

public class FolderImporter : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
#if IS_USING_ANDY_UTILS
        foreach (var path in importedAssets)
        {
            if (path.StartsWith("Assets/BigFolder/"))
            {
                Debug.Log("Importing: " + path);
                // process asset here
            }
        }
#else
        // Block or ignore imports
#endif
    }
}