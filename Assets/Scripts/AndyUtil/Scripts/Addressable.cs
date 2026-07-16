#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace AndyUtil
{
    public static class Addressable
    {
        public static void MarkAssetAsAddressable(string assetPath, string groupName = null, string address = null, string label = null)
        {
            // Load the Addressables settings
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                global::AndyUtil.Logger.LogError("AddressableAssetSettings not found!");
                return;
            }

            // Get GUID for the asset path

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                global::AndyUtil.Logger.LogError($"Asset at path '{assetPath}' not found or no GUID.");
                return;
            }

            // Choose target group (if null, use default)
            AddressableAssetGroup targetGroup = null;
            if (!string.IsNullOrEmpty(groupName))
                targetGroup = settings.FindGroup(groupName);
            if (targetGroup == null)
                targetGroup = settings.DefaultGroup;

            // Create or move entry
            var entry = settings.CreateOrMoveEntry(guid, targetGroup, readOnly: false, postEvent: true);
            if (entry == null)
            {
                global::AndyUtil.Logger.LogError("Failed to create/move entry for addressables.");
                return;
            }

            // You can set address name explicitly
            if (address == null)
            {
                string assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                entry.address = assetName;
            }
            else
            {
                entry.address = address;
            }


            // Optionally assign labels, etc
            if (!string.IsNullOrEmpty(label))
            {
                entry.SetLabel(label, true);
            }


            // Save settings

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            AssetDatabase.SaveAssets();
            global::AndyUtil.Logger.Log($"Marked '{assetPath}' as Addressable (address: {entry.address}) in group '{targetGroup.Name}'.");
        }
    }
}




#endif