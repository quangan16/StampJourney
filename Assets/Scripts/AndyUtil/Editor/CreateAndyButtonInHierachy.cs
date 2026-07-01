// using UnityEditor;
// using UnityEngine;
// using UnityEngine.UI;

// public class CreateAndyButtonInHierachy
// {
//     [MenuItem("GameObject/UI /AndyButton", false, 10)]
//     public static void CreateButton(MenuCommand menuCommand)
//     {
//         // Find or create Canvas
//         Canvas canvas = Object.FindObjectOfType<Canvas>();
//         if (canvas == null)
//         {
//             GameObject canvasGO = new GameObject("Canvas");
//             canvas = canvasGO.AddComponent<Canvas>();
//             canvas.renderMode = RenderMode.ScreenSpaceOverlay;
//             canvasGO.AddComponent<CanvasScaler>();
//             canvasGO.AddComponent<GraphicRaycaster>();
//         }

//         // Create Button
//         GameObject buttonGO = new GameObject("AndyButton");
//         RectTransform rect = buttonGO.AddComponent<RectTransform>();
//         buttonGO.AddComponent<CanvasRenderer>();

//         Image image = buttonGO.AddComponent<Image>();
//         image.color = Color.cyan;

//         Button button = buttonGO.AddComponent<AndyButton>();

//         // Parent to canvas
//         buttonGO.transform.SetParent(canvas.transform, false);

//         // Create Text
//         GameObject textGO = new GameObject("Text");
//         textGO.transform.SetParent(buttonGO.transform, false);

//         RectTransform textRect = textGO.AddComponent<RectTransform>();
//         textRect.anchorMin = Vector2.zero;
//         textRect.anchorMax = Vector2.one;
//         textRect.offsetMin = Vector2.zero;
//         textRect.offsetMax = Vector2.zero;

//         Text text = textGO.AddComponent<Text>();
//         text.text = "";
//         text.alignment = TextAnchor.MiddleCenter;
//         text.color = Color.black;
//         text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

//         // Set default size
//         rect.sizeDelta = new Vector2(160, 40);

//         // Register undo
//         Undo.RegisterCreatedObjectUndo(buttonGO, "Create My Button");

//         // Select the new button
//         Selection.activeGameObject = buttonGO;
//     }

//     private const string PREFAB_PATH = "Assets/Prefabs/UI/MyButton.prefab";

//     [MenuItem("GameObject/UI/My Button (Prefab)", false, 11)]
//     public static void CreateButtonFromPrefab(MenuCommand menuCommand)
//     {
//         // Load prefab
//         GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
//         if (prefab == null)
//         {
//             Debug.LogError("Prefab not found at path: " + PREFAB_PATH);
//             return;
//         }

//         // Find or create Canvas
//         Canvas canvas = Object.FindObjectOfType<Canvas>();
//         if (canvas == null)
//         {
//             GameObject canvasGO = new GameObject("Canvas");
//             canvas = canvasGO.AddComponent<Canvas>();
//             canvas.renderMode = RenderMode.ScreenSpaceOverlay;
//             canvasGO.AddComponent<CanvasScaler>();
//             canvasGO.AddComponent<GraphicRaycaster>();
//         }

//         // Instantiate prefab (keeps prefab connection)
//         GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

//         // Parent to Canvas or selected object
//         Transform parent = Selection.activeTransform != null
//             ? Selection.activeTransform
//             : canvas.transform;

//         instance.transform.SetParent(parent, false);

//         // Register Undo
//         Undo.RegisterCreatedObjectUndo(instance, "Create My Button (Prefab)");

//         // Select new object
//         Selection.activeGameObject = instance;
//     }


// }