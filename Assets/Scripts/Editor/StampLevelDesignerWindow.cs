using System;
using System.Collections.Generic;
using System.Linq;
using StampJourney.Core;
using StampJourney.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StampJourney.EditorTools
{
    /// <summary>
    /// Scene-backed visual authoring tool for a Stamp Journey level.
    /// Uses direct slot-to-slot dragging: dragged cards are moved (or swapped), never copied.
    /// </summary>
    public sealed class StampLevelDesignerWindow : EditorWindow
    {
        private IList<LevelData> cachedEditorLevelData;
        private int _currentLevelID;

        private const float CardWidth = 100;
        private const float CardHeight = 100f;
        private const float CardGap = 10f;
        private const float PaletteItemSize = 64f;
        private const float PaletteCardWidth = 286f;
        private const string DragTitle = "Stamp card";
        private const string LevelDataFolder = "Assets/LevelData";
        private const string LevelDataAddressableGroup = "LevelData";
        private const string LevelDataAddressableLabel = "level_data";
        private static readonly System.Random LayoutRandom = new();

        private static readonly Color HeaderColor = new(0.10f, 0.17f, 0.28f);
        private static readonly Color QueueColor = new(0.17f, 0.41f, 0.65f);
        private static readonly Color BoardColor = new(0.21f, 0.63f, 0.43f);
        private static readonly Color AccentColor = new(0.16f, 0.55f, 0.95f);
        private static readonly Color EmptySlotColor = new(0.14f, 0.16f, 0.20f, 0.12f);

        private enum SlotKind { Board, Queue }

        private readonly struct LayoutSlot : IEquatable<LayoutSlot>
        {
            public readonly SlotKind Kind;
            public readonly int Column;
            /// <summary>Board row for board slots; drop order for queue slots.</summary>
            public readonly int Index;

            public LayoutSlot(SlotKind kind, int column, int index)
            {
                Kind = kind;
                Column = column;
                Index = index;
            }

            public bool Equals(LayoutSlot other) => Kind == other.Kind && Column == other.Column && Index == other.Index;
            public override bool Equals(object obj) => obj is LayoutSlot other && Equals(other);
            public override int GetHashCode() => ((int)Kind * 486187739) ^ (Column * 16777619) ^ Index;
        }

        private sealed class DragState
        {
            public CardPlacement Card;
            public LayoutSlot? Source;
            public LayoutSlot? Hover;
            public bool IsActive => Card != null;

            public void Begin(CardPlacement card, LayoutSlot? source)
            {
                Card = card?.Clone();
                Source = source;
                Hover = null;
            }

            public void Clear()
            {
                Card = null;
                Source = null;
                Hover = null;
            }
        }

        private readonly struct PaletteHit
        {
            public readonly Rect Rect;
            public readonly CardPlacement Card;

            public PaletteHit(Rect rect, CardPlacement card) { Rect = rect; Card = card; }
        }

        private GameManager _gameManager;
        private string _topicFolder = "Assets/Arts/Topics";
        private string[] _topicTypes = Array.Empty<string>();
        private int _selectedTopicIndex = 0;
        private string[] _splitTypes = Array.Empty<string>();
        private int _selectedSplitIndex = 0;
        private Sprite[] _topicSprites = Array.Empty<Sprite>();
        private Vector2 _windowScroll;
        private Vector2 _libraryScroll;
        private readonly Dictionary<LayoutSlot, Rect> _slotRects = new();
        private readonly List<PaletteHit> _paletteHits = new();
        private readonly DragState _drag = new();
        private string _validationMessage = "Choose a level, then build its board and queues.";

        [MenuItem("Tools/Stamp Journey/Level Designer %g")]
        public static void Open()
        {
            var window = GetWindow<StampLevelDesignerWindow>("Stamp Level Designer");
            window.minSize = new Vector2(860f, 680f);
            window.Show();
        }

        private async void OnEnable()
        {
            _gameManager = FindAnyObjectByType<GameManager>();
            cachedEditorLevelData = await _gameManager.LevelSystem.LoadAllLevelDataAsync();
            RefreshTopicSprites();
            EditorApplication.update += RepaintWhileDragging;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhileDragging;
            _drag.Clear();
        }

        private void RepaintWhileDragging()
        {
            if (_drag.IsActive) Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            if (!TryGetConfig(out LevelData config)) return;

            EnsureCollections(config);
            _paletteHits.Clear();
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);

            EditorGUILayout.BeginHorizontal();

            // First Column
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawLayoutCanvas(config);
            EditorGUILayout.EndVertical();

            GUILayout.Space(16);

            // Second Column
            EditorGUILayout.BeginVertical();
            DrawLevelSettings(config);
            DrawStampLibrary(config);
            DrawFooter(config);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Slot rectangles and Event.mousePosition are both relative to the scroll content here.
            // Keep drag hit-testing inside this scope (as ToolChangeBoxOrder does).
            HandlePointerInput(config, Event.current);

            if (_drag.IsActive && Event.current.type == EventType.Repaint)
                DrawFloatingCard(Event.current.mousePosition);

            EditorGUILayout.EndScrollView();
        }

        #region Header and settings

        private void DrawHeader()
        {
            Rect headerRect = GUILayoutUtility.GetRect(1, 58, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, HeaderColor);
            GUI.Label(new Rect(headerRect.x + 18, headerRect.y + 8, 330, 22), "STAMP JOURNEY", TitleStyle(18, Color.white));
            GUI.Label(new Rect(headerRect.x + 18, headerRect.y + 31, 390, 18), "VISUAL LEVEL DESIGNER", LabelStyle(10, new Color(0.66f, 0.78f, 0.96f), FontStyle.Bold));

            float right = headerRect.xMax - 12;
            if (DrawHeaderButton(new Rect(right - 96, headerRect.y + 15, 96, 28), "Save Scene", AccentColor)) SaveScene();
            if (DrawHeaderButton(new Rect(right - 190, headerRect.y + 15, 84, 28), "Refresh", QueueColor)) RefreshTopicSprites();
        }

        private bool TryGetConfig(out LevelData config)
        {
            config = null;
            EditorGUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            _gameManager.LevelSystem = (LevelSystem)EditorGUILayout.ObjectField("Level System", _gameManager.LevelSystem, typeof(LevelSystem), true);
            if (EditorGUI.EndChangeCheck()) cachedEditorLevelData = null;

            if (_gameManager.LevelSystem == null)
            {
                EditorGUILayout.HelpBox("Assign the scene's LevelSystem. Its serialized level entries are the source of truth for this tool.", MessageType.Info);
                return false;
            }


            EditorGUILayout.BeginHorizontal();
            _currentLevelID = EditorGUILayout.IntField("Level ID", _currentLevelID);

            LevelData level = _currentLevelID > 0 ? FindLevelData(_currentLevelID) : null;
            if (_currentLevelID > 0)
            {
                string actionLabel = level == null ? "Create Level" : "Save Level";
                if (GUILayout.Button(actionLabel, GUILayout.Width(110f)))
                {
                    if (level == null)
                        level = CreateLevelData(_currentLevelID);
                    else
                        SaveLevelData(level);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_currentLevelID <= 0)
            {
                EditorGUILayout.HelpBox("Enter a Level ID of 1 or higher.", MessageType.Info);
                return false;
            }

            if (level == null)
            {
                EditorGUILayout.HelpBox($"Level {_currentLevelID} has not been created. Press Create Level to confirm.", MessageType.Info);
                return false;
            }

            config = level;
            return true;
        }

        private LevelData FindLevelData(int levelID)
        {
            LevelData level = cachedEditorLevelData?.FirstOrDefault(candidate => candidate != null && candidate.levelID == levelID);
            if (level != null) return level;

            string assetPath = $"{LevelDataFolder}/Level_{levelID}.asset";
            level = AssetDatabase.LoadAssetAtPath<LevelData>(assetPath);
            if (level == null) return null;

            cachedEditorLevelData ??= new List<LevelData>();
            if (!cachedEditorLevelData.Contains(level))
                cachedEditorLevelData.Add(level);

            return level;
        }

        private LevelData CreateLevelData(int levelID)
        {
            LevelData existingLevel = FindLevelData(levelID);
            if (existingLevel != null) return existingLevel;

            string assetPath = $"{LevelDataFolder}/Level_{levelID}.asset";
            EnsureAssetFolder(LevelDataFolder);

            LevelData level = CreateInstance<LevelData>();
            level.name = $"Level_{levelID}";
            level.levelID = levelID;
            AssetDatabase.CreateAsset(level, assetPath);
            Undo.RegisterCreatedObjectUndo(level, $"Create Level {levelID} Data");

            cachedEditorLevelData ??= new List<LevelData>();
            cachedEditorLevelData.Add(level);

            SaveLevelData(level);
            _validationMessage = $"Created LevelData for level {levelID}.";
            return level;
        }

        private void SaveLevelData(LevelData level)
        {
            if (level == null) return;

            string assetPath = AssetDatabase.GetAssetPath(level);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError($"[Stamp Level Designer] Level {level.levelID} is not a saved asset.", level);
                return;
            }
            string levelLabel = "level_data";
            EditorUtility.SetDirty(level);
            AndyUtil.Addressable.MarkAssetAsAddressable(
                assetPath,
                LevelDataAddressableGroup,
                $"Level_{level.levelID}",
                levelLabel);

            MarkDirty();
            AssetDatabase.SaveAssets();
            _validationMessage = $"Saved LevelData for level {level.levelID}.";
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parentFolder = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string folderName = System.IO.Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder))
                EnsureAssetFolder(parentFolder);

            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        private void DrawLevelSettings(LevelData config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("LEVEL SETUP", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Level ID", config.levelID.ToString());
            config.boardCols = EditorGUILayout.IntSlider("Board columns", config.boardCols, 2, 8);
            config.boardRows = EditorGUILayout.IntSlider("Board rows", config.boardRows, 2, 10);
            config.maxMoves = EditorGUILayout.IntField("Move limit (-1 = unlimited)", config.maxMoves);
            config.timeLimitSeconds = EditorGUILayout.FloatField("Time limit (sec, 0 = off)", config.timeLimitSeconds);
            if (config.timeLimitSeconds < 0f)
                config.timeLimitSeconds = 0f;
            config.useAuthoredLayout = EditorGUILayout.Toggle(
                new GUIContent(
                    "Use authored layout",
                    "Enabled: use this tool's exact board and queue layout. Disabled: runtime generates the layout from the configured topics."),
                config.useAuthoredLayout);
            if (EditorGUI.EndChangeCheck())
            {
                TrimInvalidSlots(config);
                EditorUtility.SetDirty(config);
                MarkDirty();
            }
            EditorGUILayout.HelpBox(
                config.useAuthoredLayout
                    ? "This level will use the exact board and queue below. Drag cards to move or swap them. Right-click any card to remove it."
                    : "This level will ignore the board and queue below at runtime. Gameplay will generate one four-item set from each configured topic.",
                MessageType.None);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        #endregion

        #region Topic item palette

        private void DrawStampLibrary(LevelData config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("TOPIC IMAGE LIBRARY", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _topicFolder = EditorGUILayout.TextField("Topic folder", _topicFolder);
            if (GUILayout.Button("Scan folder", GUILayout.Width(100))) RefreshTopicSprites();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Use one folder per topic with exactly four complete item sprites. Match the four cards into a 2x2 square.", EditorStyles.wordWrappedMiniLabel);

            if (_topicTypes != null && _topicTypes.Length > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();


                float oldLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 84;


                EditorGUI.BeginChangeCheck();
                _selectedTopicIndex = EditorGUILayout.Popup("Filter Topic", _selectedTopicIndex, _topicTypes);


                if (_splitTypes != null && _splitTypes.Length > 0)
                {
                    GUILayout.Space(10);
                    EditorGUIUtility.labelWidth = 64;
                    _selectedSplitIndex = EditorGUILayout.Popup("Filter Split", _selectedSplitIndex, _splitTypes);
                }


                if (EditorGUI.EndChangeCheck()) RefreshTopicSprites();


                EditorGUIUtility.labelWidth = oldLabelWidth;
                EditorGUILayout.EndHorizontal();
            }

            _libraryScroll = EditorGUILayout.BeginScrollView(_libraryScroll, GUILayout.Height(104));
            EditorGUILayout.BeginHorizontal();
            foreach (Sprite sprite in _topicSprites)
                DrawTopicImageButton(config, sprite);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            if (_selectedTopicIndex > 0 && _topicSprites.Length > 0 &&
                GUILayout.Button($"Add all visible items to {_topicTypes[_selectedTopicIndex]}", GUILayout.Height(24)))
            {
                foreach (Sprite sprite in _topicSprites)
                    AddItemToLevel(config, sprite, false);
                MarkDirty();
            }

            config.stamps ??= Array.Empty<StampData>();
            if (config.stamps.Length == 0)
            {
                EditorGUILayout.HelpBox("Click an image above, or add the filtered folder, to create a topic and its authored items.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("TOPIC ITEM PALETTE", EditorStyles.boldLabel);
                int itemsPerRow = 6;
                StampData[] stamps = config.stamps.ToArray();
                int numRows = Mathf.CeilToInt((float)stamps.Length / itemsPerRow);

                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < itemsPerRow; col++)
                {
                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    bool hasItem = false;
                    for (int row = 0; row < numRows; row++)
                    {
                        int index = row * itemsPerRow + col;
                        if (index < stamps.Length)
                        {
                            DrawStampPalette(config, stamps[index]);
                            EditorGUILayout.Space(4);
                            hasItem = true;
                        }
                    }
                    if (!hasItem) GUILayout.Label("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
        }

        private void DrawTopicImageButton(LevelData config, Sprite sprite)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(92));
            bool isAlreadyAdded = IsSpriteAlreadyInPalette(config, sprite);
            string tooltip = isAlreadyAdded
                ? $"{sprite.name} is already an authored topic item."
                : $"Add complete item {sprite.name}";

            // AssetPreview is generated asynchronously and can temporarily return null during
            // repaints. Drawing from sprite.rect keeps sub-sprite thumbnails stable on every frame.
            Rect thumbnailRect = GUILayoutUtility.GetRect(86, 62, GUILayout.Width(86), GUILayout.Height(62));
            bool clicked = GUI.Button(thumbnailRect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            GUI.Box(thumbnailRect, GUIContent.none);
            DrawSprite(thumbnailRect, sprite, 1f, 3f);
            EditorGUIUtility.AddCursorRect(thumbnailRect, MouseCursor.Link);
            if (clicked)
                AddItemToLevel(config, sprite);

            EditorGUILayout.LabelField(isAlreadyAdded ? "Already added" : sprite.name, EditorStyles.miniLabel, GUILayout.Width(86));
            EditorGUILayout.EndVertical();
        }

        private void DrawStampPalette(LevelData config, StampData stamp)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(PaletteCardWidth));
            const int itemsPerRow = 4;
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(stamp.TotalItems / (float)itemsPerRow));
            float itemAreaHeight = rowCount * PaletteItemSize;
            Rect itemArea = GUILayoutUtility.GetRect(PaletteCardWidth - 12f, itemAreaHeight, GUILayout.Height(itemAreaHeight));
            EditorGUI.DrawRect(itemArea, new Color(0f, 0f, 0f, .08f));

            for (int itemIndex = 0; itemIndex < stamp.TotalItems; itemIndex++)
            {
                int column = itemIndex % itemsPerRow;
                int row = itemIndex / itemsPerRow;
                Rect itemRect = new(
                    itemArea.x + column * PaletteItemSize,
                    itemArea.y + row * PaletteItemSize,
                    PaletteItemSize,
                    PaletteItemSize);
                CardPlacement item = new() { stamp = stamp, itemIndex = itemIndex };
                DrawPiece(itemRect, item, 1f, 3f);
                DrawOutline(itemRect, new Color(.22f, .55f, .92f, .75f), 1f);
                GUI.Label(
                    itemRect,
                    new GUIContent(string.Empty, $"{stamp.GetItemSprite(itemIndex)?.name}\nRight-click to remove from {stamp.stampName}."),
                    GUIStyle.none);
                _paletteHits.Add(new PaletteHit(itemRect, item));
                EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Link);
            }

            EditorGUILayout.Space(3);


            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 40;


            EditorGUI.BeginChangeCheck();
            stamp.stampName = EditorGUILayout.TextField("Name", stamp.stampName);
            stamp.stampId = Mathf.Max(1, EditorGUILayout.IntField("ID", stamp.stampId));
            stamp.stampColor = EditorGUILayout.ColorField("Color", stamp.stampColor);
            EditorGUILayout.LabelField($"{stamp.TotalItems} authored items", EditorStyles.miniLabel);


            if (EditorGUI.EndChangeCheck()) MarkDirty();


            EditorGUIUtility.labelWidth = oldLabelWidth;


            EditorGUILayout.Space(2);
            if (GUILayout.Button("Remove topic", GUILayout.Height(22)))
            {
                RemoveStamp(config, stamp);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Layout canvas

        private void DrawLayoutCanvas(LevelData config)
        {
            _slotRects.Clear();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("AUTHORED LEVEL LAYOUT", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Queue cards are stacked above their column. The NEXT row (closest to the board) drops first.", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add queue row", GUILayout.Width(150), GUILayout.Height(24)))
            {
                Undo.RecordObject(config, "Add authored queue row");
                config.authoredQueueRows = GetQueueRowCount(config) + 1;
                EditorUtility.SetDirty(config);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);

            int queueRows = GetQueueRowCount(config);
            for (int order = queueRows - 1; order >= 0; order--)
            {
                DrawSectionLabel(order == 0 ? "NEXT DROP" : $"QUEUE LAYER {order + 1}", QueueColor);
                DrawSlotRow(config, SlotKind.Queue, order);
            }

            DrawSectionLabel("BOARD", BoardColor);
            for (int row = 0; row < config.boardRows; row++)
                DrawSlotRow(config, SlotKind.Board, row);

            EditorGUILayout.EndVertical();
        }

        private void DrawSectionLabel(string text, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(1, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
            GUI.Label(new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 16), text, LabelStyle(10, Color.white, FontStyle.Bold));
        }

        private void DrawSlotRow(LevelData config, SlotKind kind, int index)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            for (int column = 0; column < config.boardCols; column++)
            {
                LayoutSlot slot = new(kind, column, index);
                Rect rect = GUILayoutUtility.GetRect(CardWidth, CardHeight, GUILayout.Width(CardWidth), GUILayout.Height(CardHeight));
                _slotRects[slot] = rect;
                DrawSlot(config, slot, rect);
                GUILayout.Space(CardGap);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private void DrawSlot(LevelData config, LayoutSlot slot, Rect rect)
        {
            bool isSource = _drag.IsActive && _drag.Source.HasValue && _drag.Source.Value.Equals(slot);
            bool isHover = _drag.IsActive && _drag.Hover.HasValue && _drag.Hover.Value.Equals(slot);
            CardPlacement card = GetCard(config, slot);

            if (isSource) DrawPlaceholder(rect, slot);
            else if (card == null) DrawEmptySlot(rect, slot);
            else DrawCard(rect, card, GetSlotBadge(slot), false);

            if (isHover) DrawOutline(rect, AccentColor, 3f);
            EditorGUIUtility.AddCursorRect(rect, card != null ? MouseCursor.Pan : MouseCursor.Link);
        }

        private void DrawCard(Rect rect, CardPlacement card, string badge, bool floating)
        {
            Rect shadow = new(rect.x + (floating ? 5f : 2f), rect.y + (floating ? 5f : 2f), rect.width, rect.height);
            EditorGUI.DrawRect(shadow, new Color(0f, 0f, 0f, floating ? .3f : .12f));
            EditorGUI.DrawRect(rect, new Color(.96f, .97f, .99f));
            DrawOutline(rect, card.stamp.stampColor.a > 0 ? card.stamp.stampColor : new Color(.8f, .8f, .8f), 2f);

            Rect imageRect = new(rect.x + 7, rect.y + 10, rect.width - 14, rect.width - 14);
            DrawPiece(imageRect, card, 1f, 0f);
            DrawGreenBadge(new Rect(rect.x - 7, rect.y - 7, 25, 25), badge);

            Rect caption = new(rect.x + 6, rect.yMax - 25, rect.width - 12, 20);
            GUI.Label(caption, card.stamp.stampName, LabelStyle(10, new Color(.12f, .16f, .24f), FontStyle.Bold));
            GUI.Label(new Rect(rect.x + 6, rect.yMax - 12, rect.width - 12, 11), $"Item {card.itemIndex + 1}/{card.stamp.TotalItems}", LabelStyle(8, new Color(.34f, .39f, .47f)));
        }

        private void DrawEmptySlot(Rect rect, LayoutSlot slot)
        {
            EditorGUI.DrawRect(rect, EmptySlotColor);
            DrawDashedOutline(rect, slot.Kind == SlotKind.Queue ? QueueColor : BoardColor);
            string label = slot.Kind == SlotKind.Queue ? (slot.Index == 0 ? "NEXT" : $"Q{slot.Index + 1}") : $"{slot.Column + 1},{slot.Index + 1}";
            GUI.Label(rect, label, LabelStyle(11, new Color(.42f, .47f, .55f), FontStyle.Bold));
        }

        private void DrawPlaceholder(Rect rect, LayoutSlot slot)
        {
            EditorGUI.DrawRect(rect, new Color(AccentColor.r, AccentColor.g, AccentColor.b, .10f));
            DrawDashedOutline(rect, AccentColor);
            GUI.Label(rect, "MOVING", LabelStyle(10, AccentColor, FontStyle.Bold));
        }

        private void DrawFloatingCard(Vector2 mousePosition)
        {
            Rect rect = new(mousePosition.x - CardWidth * .5f, mousePosition.y - CardHeight * .5f, CardWidth, CardHeight);
            DrawCard(rect, _drag.Card, _drag.Source.HasValue ? GetSlotBadge(_drag.Source.Value) : "NEW", true);
        }

        private static void DrawPiece(Rect rect, CardPlacement card, float alpha, float padding)
        {
            Sprite sprite = card?.stamp?.GetItemSprite(card.itemIndex);
            DrawSprite(rect, sprite, alpha, padding);
        }

        private static void DrawSprite(Rect rect, Sprite sprite, float alpha, float padding)
        {
            if (sprite == null || sprite.texture == null) return;
            Rect source = sprite.rect;
            Rect uv = new(
                source.x / sprite.texture.width,
                source.y / sprite.texture.height,
                source.width / sprite.texture.width,
                source.height / sprite.texture.height);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2f, rect.height - padding * 2f), sprite.texture, uv);
            GUI.color = old;
        }

        private static void DrawOutline(Rect rect, Color color, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(rect, Color.clear, color);
            if (width > 1f)
            {
                for (int i = 1; i < Mathf.RoundToInt(width); i++)
                    Handles.DrawSolidRectangleWithOutline(new Rect(rect.x + i, rect.y + i, rect.width - i * 2, rect.height - i * 2), Color.clear, color);
            }
            Handles.EndGUI();
        }

        private static void DrawDashedOutline(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            Color old = Handles.color;
            Handles.color = color;
            Handles.DrawDottedLine(new Vector3(rect.x, rect.y), new Vector3(rect.xMax, rect.y), 3f);
            Handles.DrawDottedLine(new Vector3(rect.xMax, rect.y), new Vector3(rect.xMax, rect.yMax), 3f);
            Handles.DrawDottedLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.x, rect.yMax), 3f);
            Handles.DrawDottedLine(new Vector3(rect.x, rect.yMax), new Vector3(rect.x, rect.y), 3f);
            Handles.color = old;
            Handles.EndGUI();
        }

        private static void DrawGreenBadge(Rect rect, string text)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                Handles.color = new Color(.18f, .68f, .37f);
                Handles.DrawSolidDisc(rect.center, Vector3.forward, rect.width * .5f);
                Handles.EndGUI();
            }
            GUI.Label(rect, text, LabelStyle(10, Color.white, FontStyle.Bold));
        }

        #endregion

        #region Direct pointer drag-and-drop

        private void HandlePointerInput(LevelData config, Event input)
        {
            switch (input.type)
            {
                case EventType.MouseDown when input.button == 1:
                    if (TryFindPalettePiece(input.mousePosition, out CardPlacement paletteItem))
                    {
                        RemoveItemFromTopic(config, paletteItem.stamp, paletteItem.itemIndex);
                        _drag.Clear();
                        input.Use();
                        Repaint();
                    }
                    else if (TryFindSlot(input.mousePosition, out LayoutSlot removeSlot) && GetCard(config, removeSlot) != null)
                    {
                        ClearSlot(config, removeSlot);
                        MarkDirty();
                        input.Use();
                    }
                    break;

                case EventType.MouseDown when input.button == 0:
                    if (TryFindPalettePiece(input.mousePosition, out CardPlacement paletteCard))
                    {
                        _drag.Begin(paletteCard, null);
                        input.Use();
                    }
                    else if (TryFindSlot(input.mousePosition, out LayoutSlot sourceSlot) && GetCard(config, sourceSlot) is CardPlacement sourceCard)
                    {
                        _drag.Begin(sourceCard, sourceSlot);
                        input.Use();
                    }
                    break;

                case EventType.MouseDrag when _drag.IsActive:
                    _drag.Hover = TryFindSlot(input.mousePosition, out LayoutSlot hoverSlot) ? hoverSlot : null;
                    input.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when input.button == 0 && _drag.IsActive:
                    if (TryFindSlot(input.mousePosition, out LayoutSlot targetSlot))
                        CommitDrop(config, targetSlot);
                    _drag.Clear();
                    input.Use();
                    Repaint();
                    break;
            }
        }

        private void CommitDrop(LevelData config, LayoutSlot target)
        {
            if (!_drag.IsActive || (_drag.Source.HasValue && _drag.Source.Value.Equals(target))) return;
            CardPlacement targetCard = GetCard(config, target);

            if (_drag.Source.HasValue)
            {
                LayoutSlot source = _drag.Source.Value;
                ClearSlot(config, source);
                SetSlot(config, target, _drag.Card);
                if (targetCard != null) SetSlot(config, source, targetCard);
            }
            else
            {
                SetSlot(config, target, _drag.Card);
            }

            EditorUtility.SetDirty(config);
            MarkDirty();
        }

        private bool TryFindSlot(Vector2 mousePosition, out LayoutSlot slot)
        {
            foreach ((LayoutSlot key, Rect rect) in _slotRects)
            {
                if (rect.Contains(mousePosition)) { slot = key; return true; }
            }
            slot = default;
            return false;
        }

        private bool TryFindPalettePiece(Vector2 mousePosition, out CardPlacement card)
        {
            foreach (PaletteHit hit in _paletteHits)
            {
                if (hit.Rect.Contains(mousePosition)) { card = hit.Card; return true; }
            }
            card = null;
            return false;
        }

        #endregion

        #region Data access and mutations

        private static void EnsureCollections(LevelData config)
        {
            config.boardLayout ??= new List<CardPlacement>();
            config.queueLayout ??= new List<QueueCardPlacement>();
            config.stamps ??= Array.Empty<StampData>();
        }

        private static CardPlacement GetCard(LevelData config, LayoutSlot slot)
        {
            return slot.Kind == SlotKind.Board
                ? config.boardLayout.Find(card => card != null && card.column == slot.Column && card.row == slot.Index)
                : config.queueLayout.Find(card => card != null && card.column == slot.Column && card.order == slot.Index);
        }

        private static void ClearSlot(LevelData config, LayoutSlot slot)
        {
            if (slot.Kind == SlotKind.Board)
                config.boardLayout.RemoveAll(card => card != null && card.column == slot.Column && card.row == slot.Index);
            else
                config.queueLayout.RemoveAll(card => card != null && card.column == slot.Column && card.order == slot.Index);
        }

        private static void SetSlot(LevelData config, LayoutSlot slot, CardPlacement card)
        {
            ClearSlot(config, slot);
            if (card == null) return;
            if (slot.Kind == SlotKind.Board)
            {
                CardPlacement placed = card.Clone();
                placed.column = slot.Column;
                placed.row = slot.Index;
                config.boardLayout.Add(placed);
            }
            else
            {
                config.authoredQueueRows = Mathf.Max(config.authoredQueueRows, slot.Index + 1);
                config.queueLayout.Add(new QueueCardPlacement
                {
                    stamp = card.stamp,
                    itemIndex = card.itemIndex,
                    column = slot.Column,
                    row = -1,
                    order = slot.Index
                });
            }
        }

        private static int GetQueueRowCount(LevelData config)
        {
            int highest = config.queueLayout.Count == 0 ? 0 : config.queueLayout.Where(card => card != null).DefaultIfEmpty().Max(card => card?.order ?? 0);
            return Mathf.Max(1, config.authoredQueueRows, highest + 1);
        }

        private static string GetSlotBadge(LayoutSlot slot) => slot.Kind == SlotKind.Queue ? $"Q{slot.Index + 1}" : $"{slot.Column + 1}:{slot.Index + 1}";

        private void AddItemToLevel(LevelData config, Sprite sprite, bool showFeedback = true)
        {
            config.stamps ??= Array.Empty<StampData>();
            if (IsSpriteAlreadyInPalette(config, sprite))
            {
                if (showFeedback)
                    _validationMessage = $"{sprite.name} is already an authored item.";
                return;
            }

            string topicName = GetTopicName(sprite);
            StampData stamp = config.stamps.FirstOrDefault(topic => topic != null &&
                string.Equals(topic.stampName, topicName, StringComparison.OrdinalIgnoreCase));
            if (stamp == null)
            {
                stamp = new StampData
                {
                    stampId = GetNextStampId(config.stamps),
                    stampName = topicName,
                    stampColor = Color.white,
                    itemSprites = Array.Empty<Sprite>()
                };
                config.stamps = config.stamps.Append(stamp).ToArray();
            }

            stamp.itemSprites = (stamp.itemSprites ?? Array.Empty<Sprite>()).Append(sprite).ToArray();
            if (showFeedback)
                _validationMessage = $"Added {sprite.name} to topic {stamp.stampName}.";
            MarkDirty();
        }

        private string GetTopicName(Sprite sprite)
        {
            string assetPath = AssetDatabase.GetAssetPath(sprite).Replace('\\', '/');
            string root = _topicFolder.TrimEnd('/') + "/";
            if (assetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string relative = assetPath.Substring(root.Length);
                int separator = relative.IndexOf('/');
                if (separator > 0) return relative.Substring(0, separator);
            }

            string directory = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            return string.IsNullOrWhiteSpace(directory) ? "Uncategorized" : System.IO.Path.GetFileName(directory);
        }

        private static int GetNextStampId(IEnumerable<StampData> stamps)
        {
            HashSet<int> usedIds = stamps.Where(stamp => stamp != null).Select(stamp => stamp.stampId).ToHashSet();
            int nextId = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
            while (usedIds.Contains(nextId)) nextId++;
            return nextId;
        }

        private static bool IsSpriteAlreadyInPalette(LevelData config, Sprite sprite)
        {
            return config?.stamps?.Any(stamp => stamp?.itemSprites?.Contains(sprite) == true) == true;
        }

        private void RemoveItemFromTopic(LevelData config, StampData topic, int itemIndex)
        {
            if (config == null || topic == null || !topic.IsValidItemIndex(itemIndex)) return;

            Sprite removedSprite = topic.GetItemSprite(itemIndex);
            int serializedIndex = -1;
            int authoredIndex = 0;
            for (int i = 0; i < topic.itemSprites.Length; i++)
            {
                if (topic.itemSprites[i] == null) continue;
                if (authoredIndex++ != itemIndex) continue;
                serializedIndex = i;
                break;
            }
            if (serializedIndex < 0) return;

            Undo.RecordObject(config, "Remove topic item");
            topic.itemSprites = topic.itemSprites
                .Where((sprite, index) => index != serializedIndex)
                .ToArray();

            int topicId = topic.stampId;
            RemoveAndReindexTopicCards(config.boardLayout, topic, topicId, itemIndex);
            RemoveAndReindexTopicCards(config.queueLayout, topic, topicId, itemIndex);

            EditorUtility.SetDirty(config);
            MarkDirty();
            _validationMessage = $"Removed {removedSprite?.name ?? $"item {itemIndex + 1}"} from {topic.stampName}. Add a replacement, then Auto-arrange or repair the authored layout.";
        }

        private static void RemoveAndReindexTopicCards<T>(
            List<T> cards,
            StampData canonicalTopic,
            int topicId,
            int removedItemIndex) where T : CardPlacement
        {
            if (cards == null) return;

            cards.RemoveAll(card =>
                card?.stamp != null &&
                card.stamp.stampId == topicId &&
                card.itemIndex == removedItemIndex);

            foreach (T card in cards)
            {
                if (card?.stamp == null || card.stamp.stampId != topicId) continue;
                card.stamp = canonicalTopic;
                if (card.itemIndex > removedItemIndex)
                    card.itemIndex--;
            }
        }

        private void RemoveStamp(LevelData config, StampData stamp)
        {
            int topicId = stamp.stampId;
            config.stamps = config.stamps.Where(current => current != stamp).ToArray();
            config.boardLayout.RemoveAll(card => card?.stamp?.stampId == topicId);
            config.queueLayout.RemoveAll(card => card?.stamp?.stampId == topicId);
            EditorUtility.SetDirty(config);
            MarkDirty();
        }

        private static void TrimInvalidSlots(LevelData config)
        {
            config.boardLayout.RemoveAll(card => card == null || card.column < 0 || card.column >= config.boardCols || card.row < 0 || card.row >= config.boardRows);
            config.queueLayout.RemoveAll(card => card == null || card.column < 0 || card.column >= config.boardCols || card.order < 0);
            config.authoredQueueRows = Mathf.Max(config.authoredQueueRows, GetQueueRowCount(config));
        }

        #endregion

        #region Generation, validation, and persistence

        private void DrawFooter(LevelData config)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (DrawActionButton("Auto-arrange solvable", BoardColor)) GenerateGuaranteedSolvableLayout(config);
            if (DrawActionButton("Validate layout", QueueColor)) Validate(config);
            if (DrawActionButton("Clear layout", new Color(.78f, .30f, .30f)))
            {
                config.boardLayout.Clear();
                config.queueLayout.Clear();
                config.authoredQueueRows = 1;
                EditorUtility.SetDirty(config);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(_validationMessage, _validationMessage.StartsWith("Solvable") ? MessageType.Info : MessageType.None);
        }

        private void GenerateGuaranteedSolvableLayout(LevelData config)
        {
            EnsureCollections(config);
            Undo.RecordObject(config, "Generate solvable stamp layout");
            ReassignContinuousTopicIds(config.stamps);
            if (!TryBuildGuaranteedLayout(config, out List<CardPlacement> board, out List<QueueCardPlacement> queue, out string error))
            {
                _validationMessage = error;
                EditorUtility.SetDirty(config);
                return;
            }

            config.boardLayout = board;
            config.queueLayout = queue;
            config.authoredQueueRows = Mathf.Max(1, Mathf.CeilToInt(queue.Count / (float)config.boardCols));
            EditorUtility.SetDirty(config);
            Validate(config);
            MarkDirty();
        }

        private static void ReassignContinuousTopicIds(IEnumerable<StampData> topics)
        {
            int nextId = 1;
            foreach (StampData topic in topics.Where(topic => topic != null))
                topic.stampId = nextId++;
        }

        private static bool TryBuildGuaranteedLayout(
            LevelData config,
            out List<CardPlacement> boardLayout,
            out List<QueueCardPlacement> queueLayout,
            out string error)
        {
            boardLayout = null;
            queueLayout = null;
            error = string.Empty;

            if (config.stamps == null || config.stamps.Length == 0)
            {
                error = "Add at least one topic before generating.";
                return false;
            }

            List<StampData> eligible = config.stamps.Where(stamp => stamp != null).ToList();
            if (eligible.Count == 0)
            {
                error = "Add at least one valid topic before generating.";
                return false;
            }

            StampData invalidTopic = eligible.FirstOrDefault(topic => !topic.HasRequiredItemCount);
            if (invalidTopic != null)
            {
                error = $"{invalidTopic.stampName} has {invalidTopic.TotalItems} item pictures. Every topic must have exactly {StampData.RequiredItemCount}.";
                return false;
            }

            int boardCardCount = config.boardCols * config.boardRows;

            IGrouping<int, StampData> duplicateIdGroup = eligible.GroupBy(stamp => stamp.stampId).FirstOrDefault(group => group.Count() > 1);
            if (duplicateIdGroup != null)
            {
                error = $"Topic ID {duplicateIdGroup.Key} is duplicated. Runtime matching requires every topic ID to be unique.";
                return false;
            }

            List<List<CardPlacement>> completeSets = BuildPaletteSetsForBottomPackedQueue(eligible, boardCardCount);
            int totalCardCount = completeSets.Sum(set => set.Count);
            int queueCardCount = totalCardCount - boardCardCount;
            if (queueCardCount < 0)
            {
                error = $"The level has {totalCardCount} authored items but needs at least {boardCardCount} to fill the board. Add more items; the generator does not duplicate authored pictures.";
                return false;
            }

            for (int attempt = 0; attempt < 512; attempt++)
            {
                List<List<CardPlacement>> shuffledSets = completeSets.ToList();
                Shuffle(shuffledSets);

                List<CardPlacement> boardPieces = new(boardCardCount);
                List<CardPlacement> remainingPieces = new(totalCardCount);
                foreach (List<CardPlacement> set in shuffledSets)
                {
                    if (boardPieces.Count + set.Count <= boardCardCount)
                        boardPieces.AddRange(set);
                    else
                        remainingPieces.AddRange(set);
                }

                Shuffle(remainingPieces);
                int partialBoardCount = boardCardCount - boardPieces.Count;
                boardPieces.AddRange(remainingPieces.Take(partialBoardCount));
                List<CardPlacement> queuePieces = remainingPieces.Skip(partialBoardCount).ToList();
                Shuffle(boardPieces);
                Shuffle(queuePieces);

                List<CardPlacement> candidateBoard = CreateBoardLayout(boardPieces, config.boardCols, config.boardRows);
                List<QueueCardPlacement> candidateQueue = CreateBottomPackedQueueLayout(queuePieces, config.boardCols);
                if (ContainsCompletedStamp(candidateBoard, config.boardCols, config.boardRows))
                    continue;

                if (!CanSolveLayout(candidateBoard, candidateQueue, config.boardCols, config.boardRows, out _))
                    continue;

                boardLayout = candidateBoard;
                queueLayout = candidateQueue;
                return true;
            }

            error = "Could not find a solvable bottom-packed queue after 512 attempts. Try adding more four-item topics or increasing the board.";
            return false;
        }

        private static List<List<CardPlacement>> BuildPaletteSetsForBottomPackedQueue(
            IReadOnlyList<StampData> stamps,
            int boardCardCount)
        {
            List<List<CardPlacement>> result = new();
            // Each authored item is unique in a level; do not silently duplicate pictures.
            foreach (StampData topic in stamps)
                result.Add(BuildCompleteStampSet(topic));

            return result;
        }

        private static List<CardPlacement> CreateBoardLayout(
            IReadOnlyList<CardPlacement> items,
            int boardCols,
            int boardRows)
        {
            List<CardPlacement> result = new(boardCols * boardRows);
            int index = 0;
            for (int row = 0; row < boardRows; row++)
                for (int col = 0; col < boardCols; col++)
                    result.Add(CopyPlacement(items[index++], col, row));
            return result;
        }

        private static List<QueueCardPlacement> CreateBottomPackedQueueLayout(
            IReadOnlyList<CardPlacement> items,
            int boardCols)
        {
            List<QueueCardPlacement> result = new(items.Count);
            int index = 0;
            int queueRows = Mathf.CeilToInt(items.Count / (float)boardCols);
            for (int order = 0; order < queueRows && index < items.Count; order++)
            {
                for (int col = 0; col < boardCols && index < items.Count; col++)
                {
                    CardPlacement item = items[index++];
                    result.Add(new QueueCardPlacement
                    {
                        stamp = item.stamp,
                        itemIndex = item.itemIndex,
                        column = col,
                        row = -1,
                        order = order
                    });
                }
            }

            return result;
        }

        private static List<CardPlacement> BuildCompleteStampSet(StampData stamp)
        {
            List<CardPlacement> result = new(stamp.TotalItems);
            AddCompleteStampSet(result, stamp);
            return result;
        }

        private static CardPlacement CopyPlacement(CardPlacement source, int column, int row)
        {
            return new CardPlacement
            {
                stamp = source.stamp,
                itemIndex = source.itemIndex,
                column = column,
                row = row
            };
        }

        private static bool ContainsCompletedStamp(IReadOnlyList<CardPlacement> boardLayout, int boardCols, int boardRows)
        {
            CardPlacement[,] board = new CardPlacement[boardCols, boardRows];
            foreach (CardPlacement card in boardLayout)
                board[card.column, card.row] = card;

            for (int row = 0; row < boardRows - 1; row++)
            {
                for (int col = 0; col < boardCols - 1; col++)
                {
                    CardPlacement topLeft = board[col, row];
                    CardPlacement topRight = board[col + 1, row];
                    CardPlacement bottomLeft = board[col, row + 1];
                    CardPlacement bottomRight = board[col + 1, row + 1];
                    if (topLeft == null || topRight == null || bottomLeft == null || bottomRight == null) continue;

                    int topicId = topLeft.stamp.stampId;
                    if (topRight.stamp.stampId != topicId ||
                        bottomLeft.stamp.stampId != topicId ||
                        bottomRight.stamp.stampId != topicId) continue;

                    if (topLeft.stamp.HasCompleteItemSet(new[]
                        {
                            topLeft.itemIndex, topRight.itemIndex,
                            bottomLeft.itemIndex, bottomRight.itemIndex
                        }))
                        return true;
                }
            }

            return false;
        }

        private static void AddCompleteStampSet(ICollection<CardPlacement> result, StampData stamp)
        {
            for (int itemIndex = 0; itemIndex < stamp.TotalItems; itemIndex++)
                result.Add(new CardPlacement { stamp = stamp, itemIndex = itemIndex });
        }

        private static void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = LayoutRandom.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        private sealed class SolverStamp
        {
            public int Id;
            public int ItemCount;
            public int Offset;
        }

        private static bool CanSolveLayout(
            IReadOnlyList<CardPlacement> boardLayout,
            IReadOnlyList<QueueCardPlacement> queueLayout,
            int boardCols,
            int boardRows,
            out int clearedStampCount)
        {
            clearedStampCount = 0;
            List<CardPlacement> allCards = boardLayout.Cast<CardPlacement>().Concat(queueLayout).Where(card => card != null).ToList();
            List<SolverStamp> stamps = new();
            Dictionary<int, SolverStamp> stampById = new();
            int itemSlotCount = 0;

            foreach (IGrouping<int, CardPlacement> group in allCards.GroupBy(card => card.stamp.stampId))
            {
                StampData stamp = group.First().stamp;
                SolverStamp solverStamp = new()
                {
                    Id = stamp.stampId,
                    ItemCount = stamp.TotalItems,
                    Offset = itemSlotCount
                };
                itemSlotCount += solverStamp.ItemCount;
                stamps.Add(solverStamp);
                stampById.Add(solverStamp.Id, solverStamp);
            }

            int[] boardCounts = new int[itemSlotCount];
            foreach (CardPlacement card in boardLayout)
            {
                SolverStamp stamp = stampById[card.stamp.stampId];
                boardCounts[stamp.Offset + card.itemIndex]++;
            }

            int[][] queues = new int[boardCols][];
            for (int column = 0; column < boardCols; column++)
            {
                queues[column] = queueLayout
                    .Where(card => card.column == column)
                    .OrderBy(card => card.order)
                    .Select(card =>
                    {
                        SolverStamp stamp = stampById[card.stamp.stampId];
                        return stamp.Offset + card.itemIndex;
                    })
                    .ToArray();
            }

            int[] queueIndices = new int[boardCols];
            HashSet<string> failedStates = new();
            int visitedStates = 0;
            return SearchSolvableState(
                boardCounts,
                queueIndices,
                queues,
                stamps,
                boardCols,
                boardRows,
                failedStates,
                ref visitedStates,
                0,
                out clearedStampCount);
        }

        private static bool SearchSolvableState(
            int[] boardCounts,
            int[] queueIndices,
            int[][] queues,
            IReadOnlyList<SolverStamp> stamps,
            int boardCols,
            int boardRows,
            ISet<string> failedStates,
            ref int visitedStates,
            int depth,
            out int clearedStampCount)
        {
            clearedStampCount = depth;
            if (++visitedStates > 250000) return false;

            bool boardEmpty = boardCounts.All(count => count == 0);
            bool queuesEmpty = Enumerable.Range(0, boardCols).All(column => queueIndices[column] >= queues[column].Length);
            if (boardEmpty && queuesEmpty) return true;

            string stateKey = string.Join(",", boardCounts) + "|" + string.Join(",", queueIndices);
            if (failedStates.Contains(stateKey)) return false;

            foreach (SolverStamp stamp in stamps.OrderByDescending(candidate => candidate.ItemCount))
            {
                bool completeSetAvailable = true;
                for (int item = 0; item < stamp.ItemCount; item++)
                {
                    if (boardCounts[stamp.Offset + item] > 0) continue;
                    completeSetAvailable = false;
                    break;
                }
                if (!completeSetAvailable) continue;

                int[] clearedBoardCounts = (int[])boardCounts.Clone();
                for (int item = 0; item < stamp.ItemCount; item++)
                    clearedBoardCounts[stamp.Offset + item]--;

                int remainingQueueCards = Enumerable.Range(0, boardCols)
                    .Sum(column => queues[column].Length - queueIndices[column]);
                int releaseCount = Mathf.Min(stamp.ItemCount, remainingQueueCards);
                foreach (int[] releasesByColumn in BuildQueueReleaseOptions(
                             releaseCount, queueIndices, queues, boardCols, boardRows))
                {
                    int[] nextBoardCounts = (int[])clearedBoardCounts.Clone();
                    int[] nextQueueIndices = (int[])queueIndices.Clone();
                    for (int column = 0; column < boardCols; column++)
                    {
                        for (int released = 0; released < releasesByColumn[column]; released++)
                        {
                            int itemSlot = queues[column][nextQueueIndices[column]++];
                            nextBoardCounts[itemSlot]++;
                        }
                    }

                    if (SearchSolvableState(
                            nextBoardCounts,
                            nextQueueIndices,
                            queues,
                            stamps,
                            boardCols,
                            boardRows,
                            failedStates,
                            ref visitedStates,
                            depth + 1,
                            out clearedStampCount))
                        return true;
                }
            }

            failedStates.Add(stateKey);
            return false;
        }

        private static IReadOnlyList<int[]> BuildQueueReleaseOptions(
            int releaseCount,
            IReadOnlyList<int> queueIndices,
            IReadOnlyList<int[]> queues,
            int boardCols,
            int boardRows)
        {
            var results = new List<int[]>();
            var current = new int[boardCols];
            BuildQueueReleaseOptionsRecursive(0, releaseCount, queueIndices, queues, boardRows, current, results);
            return results;
        }

        private static void BuildQueueReleaseOptionsRecursive(
            int column,
            int remaining,
            IReadOnlyList<int> queueIndices,
            IReadOnlyList<int[]> queues,
            int boardRows,
            int[] current,
            ICollection<int[]> results)
        {
            if (column == current.Length)
            {
                if (remaining == 0) results.Add((int[])current.Clone());
                return;
            }

            int available = queues[column].Length - queueIndices[column];
            int maxRelease = Mathf.Min(remaining, Mathf.Min(boardRows, available));
            for (int count = maxRelease; count >= 0; count--)
            {
                current[column] = count;
                BuildQueueReleaseOptionsRecursive(column + 1, remaining - count, queueIndices, queues, boardRows, current, results);
            }
            current[column] = 0;
        }

        private void Validate(LevelData config)
        {
            EnsureCollections(config);
            List<CardPlacement> allCards = config.boardLayout.Cast<CardPlacement>().Concat(config.queueLayout).Where(card => card != null).ToList();
            if (allCards.Count == 0) { _validationMessage = "Layout is empty."; return; }

            int authoredBoardSlots = config.boardLayout
                .Where(card => card != null && card.column >= 0 && card.column < config.boardCols && card.row >= 0 && card.row < config.boardRows)
                .Select(card => (card.column, card.row))
                .Distinct()
                .Count();
            if (authoredBoardSlots != config.boardCols * config.boardRows)
            {
                _validationMessage = "Not ready: fill every board slot. The runtime fills starting board cards before it begins the authored queues.";
                return;
            }

            CardPlacement invalid = allCards.FirstOrDefault(card => !card.IsValid);
            if (invalid != null) { _validationMessage = "Invalid card: its topic was removed or its authored item index no longer exists."; return; }

            IGrouping<int, StampData> duplicateTopicId = config.stamps
                .Where(topic => topic != null)
                .GroupBy(topic => topic.stampId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateTopicId != null)
            {
                _validationMessage = $"Not solvable: topic ID {duplicateTopicId.Key} is assigned more than once.";
                return;
            }

            foreach (StampData topic in config.stamps.Where(topic => topic != null))
            {
                if (!topic.HasRequiredItemCount)
                {
                    _validationMessage = $"Not ready: {topic.stampName} must have exactly {StampData.RequiredItemCount} authored item pictures; it currently has {topic.TotalItems}.";
                    return;
                }

                List<CardPlacement> topicCards = allCards
                    .Where(card => card.stamp.stampId == topic.stampId)
                    .ToList();
                bool hasEveryItemExactlyOnce = topicCards.Count == topic.TotalItems &&
                    topicCards.Select(card => card.itemIndex).Distinct().Count() == topic.TotalItems;
                if (!hasEveryItemExactlyOnce)
                {
                    _validationMessage = $"Not solvable: place each of the {topic.TotalItems} authored items from {topic.stampName} exactly once.";
                    return;
                }
            }

            if (config.queueLayout.Count > 0)
            {
                List<QueueCardPlacement> queueCards = config.queueLayout.Where(card => card != null).ToList();
                int highestOrder = queueCards.Max(card => card.order);
                bool isBottomPacked = highestOrder >= 0;
                for (int order = 0; order <= highestOrder && isBottomPacked; order++)
                {
                    List<QueueCardPlacement> row = queueCards.Where(card => card.order == order).ToList();
                    int expectedCount = order < highestOrder ? config.boardCols : row.Count;
                    isBottomPacked = row.Count > 0 && row.Count == expectedCount &&
                                     row.Count <= config.boardCols &&
                                     row.Select(card => card.column).Distinct().Count() == row.Count;
                }

                if (!isBottomPacked)
                {
                    _validationMessage = "Not ready: fill each lower queue row before placing cards in the row above it. Only the highest row may be partial.";
                    return;
                }
            }

            if (!CanSolveLayout(config.boardLayout, config.queueLayout, config.boardCols, config.boardRows, out int clearCount))
            {
                _validationMessage = "Not solvable: queue simulation reaches a state where a topic cannot bring all four items onto the board.";
                return;
            }

            string limitWarning = config.maxMoves > 0 || config.HasTimeLimit
                ? " Move/time limits are not included in this structural guarantee."
                : string.Empty;
            _validationMessage = $"Solvable: every topic has four items, the queue is packed bottom-up, and the simulated clear path completes {clearCount} topics. Players complete each topic by making a 2x2 square.{limitWarning}";
        }

        /// <summary>
        /// Rebuilds the topic and split filter options from the configured folder, then loads
        /// every Sprite asset matching the active filters into the editor image library.
        /// Expected hierarchy: Topic Root / Topic / optional Split / sprite assets.
        /// </summary>
        private void RefreshTopicSprites()
        {
            // Stop early when the configured root is missing. Clearing all cached values also
            // prevents the window from continuing to display sprites from the previous folder.
            if (!AssetDatabase.IsValidFolder(_topicFolder))
            {
                _topicTypes = Array.Empty<string>();
                _splitTypes = Array.Empty<string>();
                _topicSprites = Array.Empty<Sprite>();
                return;
            }

            // Every direct child of the root is treated as a topic. The leading "All" entry
            // represents the root itself, which makes AssetDatabase search every topic below it.
            string[] subfolders = AssetDatabase.GetSubFolders(_topicFolder);
            List<string> typeNames = new List<string> { "All" };
            typeNames.AddRange(subfolders.Select(f => System.IO.Path.GetFileName(f)));
            _topicTypes = typeNames.ToArray();

            // Folder contents may have changed since the last refresh, so keep the selected
            // popup index inside the newly rebuilt topic list.
            if (_selectedTopicIndex < 0 || _selectedTopicIndex >= _topicTypes.Length)
                _selectedTopicIndex = 0;

            // Topic index 0 means search the complete root. Other popup entries map to the
            // direct subfolders array, hence the -1 offset for the synthetic "All" entry.
            string searchFolder = _selectedTopicIndex == 0 ? _topicFolder : subfolders[_selectedTopicIndex - 1];

            // Build the second-level split filter. When all topics are selected, collect split
            // folders from every topic; otherwise only inspect the selected topic's children.
            string[] splitFolders = _selectedTopicIndex == 0
                ? subfolders.SelectMany(f => AssetDatabase.GetSubFolders(f)).ToArray()
                : AssetDatabase.GetSubFolders(searchFolder);

            // Different topics may use the same split name, so show each name only once in the
            // popup while retaining all matching folder paths for the eventual asset search.
            string[] splitNamesDistinct = splitFolders.Select(f => System.IO.Path.GetFileName(f)).Distinct().ToArray();
            List<string> splitNamesList = new List<string> { "All" };
            splitNamesList.AddRange(splitNamesDistinct);
            _splitTypes = splitNamesList.ToArray();

            // Reset a stale split selection if the chosen topic has a different set of splits.
            if (_selectedSplitIndex < 0 || _selectedSplitIndex >= _splitTypes.Length)
                _selectedSplitIndex = 0;

            // With no split filter, search the selected topic/root recursively. With a split
            // selected, search every folder whose final path segment has that split name.
            string[] finalSearchFolders = _selectedSplitIndex == 0
                ? new[] { searchFolder }
                : splitFolders.Where(f => System.IO.Path.GetFileName(f) == _splitTypes[_selectedSplitIndex]).ToArray();

            // A selected split may not exist under the current topic after folders are changed.
            if (finalSearchFolders.Length == 0)
            {
                _topicSprites = Array.Empty<Sprite>();
            }
            else
            {
                // FindAssets returns GUIDs. Convert them to unique asset paths, then load every
                // Sprite at each path so multi-sprite texture assets are included as well.
                _topicSprites = AssetDatabase.FindAssets("t:Sprite", finalSearchFolders)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                    .ToArray();
            }

            // Refresh the visible library immediately after rebuilding its cached sprite list.
            Repaint();
        }

        private void MarkDirty()
        {
            if (_gameManager.LevelSystem == null) return;
            EditorUtility.SetDirty(_gameManager.LevelSystem);
            if (_gameManager.LevelSystem.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(_gameManager.LevelSystem.gameObject.scene);
        }

        private void SaveScene()
        {
            if (_gameManager.LevelSystem?.gameObject.scene.IsValid() == true)
                EditorSceneManager.SaveScene(_gameManager.LevelSystem.gameObject.scene);
        }

        #endregion

        #region Styling helpers

        private static bool DrawHeaderButton(Rect rect, string text, Color color)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUI.Button(rect, text);
            GUI.backgroundColor = old;
            return clicked;
        }

        private static bool DrawActionButton(string text, Color color)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(text, GUILayout.Height(30));
            GUI.backgroundColor = old;
            return clicked;
        }

        private static GUIStyle LabelStyle(int size, Color color, FontStyle style = FontStyle.Normal) => new(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = style,
            normal = { textColor = color },
            clipping = TextClipping.Clip
        };

        private static GUIStyle TitleStyle(int size, Color color) => new(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = FontStyle.Bold,
            normal = { textColor = color }
        };

        #endregion
    }
}
