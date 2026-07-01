using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class SpriteBaseSO : SerializedScriptableObject
{
    public Dictionary<string, SpriteBaseSO> sprites = new Dictionary<string, SpriteBaseSO>();
    public Dictionary<SpriteType, SpriteBaseSO> dynamicSprites = new Dictionary<SpriteType, SpriteBaseSO>();

    public SpriteBaseSO Get(string spriteName)
    {
        return sprites.TryGetValue(spriteName, out var sprite) ? sprite : null;
    }

    public SpriteBaseSO Get(SpriteType type)
    {
        return dynamicSprites.TryGetValue(type, out var sprite) ? sprite : null;
    }

    public enum SpriteType
    {
    }
}
