using System;
using UnityEngine;

[Serializable]
public record SerializedKeyValuePair<T_Key, T_Value>
{
    [SerializeField] private T_Key _key;
    [SerializeField] private T_Value _value;

    public T_Key Key => _key;
    public T_Value Value => _value;
}