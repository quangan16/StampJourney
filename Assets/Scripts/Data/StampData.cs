using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Data
{
    /// <summary>
    /// Defines a postage stamp type.
    /// Each stamp has a grid size (cols × rows), a full image, and a recognition color.
    /// Piece sprites are sliced from fullImage at editor time.
    /// </summary>
    [Serializable]
    public class StampData
    {
        #region Identity

        [BoxGroup("Identity")]
        [LabelText("Stamp ID")]
        public int stampId;

        [BoxGroup("Identity")]
        [LabelText("Stamp Name")]
        public string stampName = "New Stamp";

        [BoxGroup("Identity")]
        [LabelText("Border Color")]
        [ColorPalette]
        public Color stampColor = Color.white;

        #endregion

        #region Layout

        [BoxGroup("Layout")]
        [LabelText("Columns")]
        [Range(1, 5)]
        public int cols = 2;

        [BoxGroup("Layout")]
        [LabelText("Rows")]
        [Range(1, 5)]
        public int rows = 2;

        #endregion

        #region Visuals

        [BoxGroup("Visuals")]
        [LabelText("Full Stamp Image")]
        [PreviewField(100)]
        [Required]
        public Sprite fullImage;

        [BoxGroup("Visuals")]
        [LabelText("Piece Sprites (col-major: [col + row*cols])")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [InfoBox("Must have exactly cols × rows sprites. Index = col + row * cols.")]
        public Sprite[] pieceSprites;

        #endregion

        #region Computed

        /// <summary>Total piece count = cols × rows.</summary>
        public int TotalPieces => cols * rows;

        /// <summary>Gets the sprite for the piece at (col, row) within this stamp.</summary>
        public Sprite GetPieceSprite(int col, int row)
        {
            int index = col + row * cols;
            if (pieceSprites == null || index < 0 || index >= pieceSprites.Length)
            {
                return CreateRuntimePiece(col, row);
            }
            return pieceSprites[index] != null ? pieceSprites[index] : CreateRuntimePiece(col, row);
        }

        /// <summary>
        /// Creates a slice when authored piece sprites are not present. This lets the level
        /// authoring tool work directly from a single source image (including Single-mode
        /// sprite imports) without requiring designers to manually slice every image first.
        /// </summary>
        private Sprite CreateRuntimePiece(int col, int row)
        {
            if (fullImage == null || col < 0 || col >= cols || row < 0 || row >= rows)
                return fullImage;

            // The level designer allows rows/columns to change after a stamp has been used.
            // Discard stale runtime slices rather than indexing the old cache with new dimensions.
            if (pieceSprites == null || pieceSprites.Length != TotalPieces)
                pieceSprites = new Sprite[TotalPieces];
            int index = col + row * cols;
            if (pieceSprites[index] != null) return pieceSprites[index];

            Rect source = fullImage.rect;
            float width = source.width / cols;
            float height = source.height / rows;
            // Sprite rects use bottom-left origin; game rows use top-left origin.
            Rect pieceRect = new Rect(source.x + col * width, source.y + (rows - 1 - row) * height, width, height);
            pieceSprites[index] = Sprite.Create(fullImage.texture, pieceRect, new Vector2(.5f, .5f), fullImage.pixelsPerUnit);
            return pieceSprites[index];
        }

        #endregion

#if UNITY_EDITOR
        #region Editor Tools

        [BoxGroup("Visuals")]
        [Button("Auto-Slice Pieces from Full Image")]
        [InfoBox("Slices fullImage into cols×rows sprites and assigns them to pieceSprites.")]
        private void AutoSlicePieces()
        {
            if (fullImage == null) { Debug.LogError("fullImage is null!"); return; }

            pieceSprites = new Sprite[TotalPieces];
            var tex = fullImage.texture;
            Rect sourceRect = fullImage.rect;
            float w = sourceRect.width / (float)cols;
            float h = sourceRect.height / (float)rows;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = sourceRect.x + c * w;
                    float y = sourceRect.y + (rows - 1 - r) * h; // Unity UV is bottom-up
                    var rect = new Rect(x, y, w, h);
                    var pivot = new Vector2(0.5f, 0.5f);
                    pieceSprites[c + r * cols] = Sprite.Create(tex, rect, pivot,
                        fullImage.pixelsPerUnit);
                }
            }

            Debug.Log($"[StampData] Sliced {TotalPieces} pieces for '{stampName}'.");
        }

        #endregion
#endif
    }
}
