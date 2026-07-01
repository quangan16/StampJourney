//
// using System;
// using System.Collections;
// using System.Reflection;
// using Newtonsoft.Json;
// using Sirenix.Serialization;
// using UnityEngine;
//
// public class CloneUtil<T> where T : class
// {
//     public static T ShallowCopy<T>(T obj) where T : class
//     {
//         if (obj == null) return null;
//
//
//         if (obj is UnityEngine.Object unityObj)
//         {
//             return UnityEngine.Object.Instantiate(unityObj) as T;
//         }
//
//         System.Reflection.MethodInfo cloneMethod = typeof(object).GetMethod("MemberwiseClone",
//             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
//
//         return (T)cloneMethod.Invoke(obj, null);
//     }
//
//     public static T DeepCopy<T>(T original)
//     {
//         if (original == null) return default;
//
//         // 1) Pure UnityEngine.Object? → just return the same reference
//         if (original is UnityEngine.Object)
//             return (T)(object)original;
//
//         // 2) Value type or string → return directly
//         if (typeof(T).IsValueType || original is string)
//             return original;
//         if (original is IList originalList)
//         {
//             var listType = original.GetType();
//             var clonedList = (IList)Activator.CreateInstance(listType);
//
//             foreach (var item in originalList)
//             {
//                 var clonedItem = DeepCopyDynamic(item);
//                 clonedList.Add(clonedItem);
//             }
//
//             return (T)clonedList;
//         }
//         // 3) Otherwise → deep copy using Odin binary serialization
//         var bytes = SerializationUtility.SerializeValue(original, DataFormat.Binary);
//         var clone = SerializationUtility.DeserializeValue<T>(bytes, DataFormat.Binary);
//
//         // 4) Fix UnityEngine.Object fields: copy references directly
//         CopyUnityReferences(original, clone);
//
//         return clone;
//     }
//
//     private static object DeepCopyDynamic(object original)
//     {
//         if (original == null) return null;
//
//         // UnityEngine.Object → keep reference
//         if (original is UnityEngine.Object)
//             return original;
//
//         // Strings / value types → return directly
//         var type = original.GetType();
//         if (type.IsValueType || original is string)
//             return original;
//
//         // Generic deep copy
//         var method = typeof(CloneUtil<>).GetMethod(nameof(DeepCopy),
//             BindingFlags.Public | BindingFlags.Static);
//
//         var generic = method.MakeGenericMethod(type);
//         return generic.Invoke(null, new[] { original });
//     }
//
//     private static void CopyUnityReferences(object original, object clone)
//     {
//         if (original == null || clone == null) return;
//
//         var type = original.GetType();
//         var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//
//         foreach (var field in fields)
//         {
//             var value = field.GetValue(original);
//
//             if (value is UnityEngine.Object unityObj)
//             {
//                 // Copy Unity reference directly (not serialized in Odin)
//                 field.SetValue(clone, unityObj);
//             }
//             else if (value != null && !field.FieldType.IsValueType && !(value is string))
//             {
//                 // Recursively check sub-objects
//                 var subClone = field.GetValue(clone);
//                 CopyUnityReferences(value, subClone);
//             }
//         }
//     }
//
//
//
//     // public static T DeepCopy(T @object)
//     // {
//     //     var settings = new JsonSerializerSettings
//     //     {
//     //         TypeNameHandling = TypeNameHandling.All,
//     //         Formatting = Formatting.None,
//     //         ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
//     //         ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
//     //         {
//     //             IgnoreSerializableAttribute = true
//     //         },
//     //         // This ignores Unity's calculated properties like normalized
//     //         MetadataPropertyHandling = MetadataPropertyHandling.Ignore
//     //     };
//     //     settings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
//     //     {
//     //         NamingStrategy = new Newtonsoft.Json.Serialization.DefaultNamingStrategy()
//     //     };
//     //     var serializedData = Newtonsoft.Json.JsonConvert.SerializeObject(@object);
//     //     return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(serializedData,settings);
//     // }
// }
//
// public class Vector2Converter : JsonConverter<Vector2>
// {
//     public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
//     {
//         writer.WriteStartObject();
//         writer.WritePropertyName("x");
//         writer.WriteValue(value.x);
//         writer.WritePropertyName("y");
//         writer.WriteValue(value.y);
//         writer.WriteEndObject();
//     }
//
//     public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
//     {
//         float x = 0, y = 0;
//         while (reader.Read())
//         {
//             if (reader.TokenType == JsonToken.PropertyName)
//             {
//                 var propName = reader.Value.ToString();
//                 reader.Read();
//                 switch (propName)
//                 {
//                     case "x": x = Convert.ToSingle(reader.Value); break;
//                     case "y": y = Convert.ToSingle(reader.Value); break;
//                 }
//             }
//             else if (reader.TokenType == JsonToken.EndObject)
//             {
//                 break;
//             }
//         }
//         return new Vector2(x, y);
//     }
// }

