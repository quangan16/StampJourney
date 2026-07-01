using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class PrefabBaseSO : SerializedScriptableObject
{
    public Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    public Dictionary<PrefabType, GameObject> dynamicPrefabs = new Dictionary<PrefabType, GameObject>();

    public GameObject Get(string prefabName)
    {
        return prefabs.TryGetValue(prefabName, out var prefab) ? prefab : null;
    }

    public GameObject Get(PrefabType type)
    {
        return dynamicPrefabs.TryGetValue(type, out var prefab) ? prefab : null;
    }

    public enum PrefabType
    {
        BlockBase,
        GridCell,
    }
}