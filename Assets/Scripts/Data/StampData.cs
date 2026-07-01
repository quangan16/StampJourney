using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Data
{
    /// <summary>
    /// ScriptableObject định nghĩa một loại tem bưu chính.
    /// Mỗi tem có kích thước (cols × rows), hình ảnh gốc và màu nhận dạng.
    /// Các sprite mảnh ghép (pieces) được slice từ fullImage tại runtime.
    /// </summary>
    public class StampData
    {
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

        [BoxGroup("Layout")]
        [LabelText("Columns")]
        [Range(1, 5)]
        public int cols = 2;

        [BoxGroup("Layout")]
        [LabelText("Rows")]
        [Range(1, 5)]
        public int rows = 2;

        [BoxGroup("Visuals")]
        [LabelText("Full Stamp Image")]
        [PreviewField(100)]
        [Required]
        public Sprite fullImage;

        [BoxGroup("Visuals")]
        [LabelText("Piece Sprites (col-major: [col + row*cols])")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [InfoBox("Phải có đủ cols × rows sprites. Index = col + row * cols.")]
        public Sprite[] pieceSprites;

        // ---- Computed ----

        /// <summary>Tổng số mảnh = cols × rows.</summary>
        public int TotalPieces => cols * rows;

        /// <summary>Lấy sprite của mảnh tại (col, row) trong stamp.</summary>
        public Sprite GetPieceSprite(int col, int row)
        {
            int index = col + row * cols;
            if (pieceSprites == null || index < 0 || index >= pieceSprites.Length)
            {
                Debug.LogWarning($"[StampData] {stampName}: pieceSprites[{index}] out of range.");
                return fullImage;
            }
            return pieceSprites[index];
        }

#if UNITY_EDITOR
        [BoxGroup("Visuals")]
        [Button("Auto-Slice Pieces from Full Image")]
        [InfoBox("Slice fullImage thành cols×rows sprites và gán vào pieceSprites.")]
        private void AutoSlicePieces()
        {
            if (fullImage == null) { Debug.LogError("fullImage is null!"); return; }
            pieceSprites = new Sprite[TotalPieces];
            var tex = fullImage.texture;
            float w = tex.width / (float)cols;
            float h = tex.height / (float)rows;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = c * w;
                    float y = (rows - 1 - r) * h; // Unity UV bottom-up
                    var rect = new Rect(x, y, w, h);
                    var pivot = new Vector2(0.5f, 0.5f);
                    pieceSprites[c + r * cols] = Sprite.Create(tex, rect, pivot,
                        fullImage.pixelsPerUnit);
                }
            }
            Debug.Log($"[StampData] Sliced {TotalPieces} pieces for '{stampName}'.");
        }
#endif
    }
}
