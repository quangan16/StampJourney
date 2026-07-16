using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Data
{
    /// <summary>
    /// Defines one collectible topic. Every sprite is a complete item picture; cards are
    /// grouped by topic ID. A topic authors exactly four complete item pictures; those
    /// four cards must be arranged as a 2x2 square to complete the topic.
    ///
    /// The class name is retained so existing LevelData assets keep their serialized topic
    /// references while the project transitions to complete item pictures.
    /// </summary>
    [Serializable]
    public class StampData
    {
        public const int RequiredItemCount = 4;

        #region Identity

        [BoxGroup("Identity")]
        [LabelText("Topic ID")]
        public int stampId;

        [BoxGroup("Identity")]
        [LabelText("Topic Name")]
        public string stampName = "New Topic";

        [BoxGroup("Identity")]
        [LabelText("Topic Color")]
        [ColorPalette]
        public Color stampColor = Color.white;

        #endregion

        #region Items

        [BoxGroup("Items")]
        [LabelText("Authored Item Pictures")]
        [ListDrawerSettings(ShowIndexLabels = true)]
        [InfoBox("Add exactly four complete item pictures. In gameplay they complete the topic when arranged as a 2x2 square.")]
        public Sprite[] itemSprites = Array.Empty<Sprite>();

        #endregion

        #region Computed

        public int TopicId => stampId;
        public string TopicName => stampName;
        public Color TopicColor => stampColor;

        /// <summary>Number of complete, non-null item pictures authored for this topic.</summary>
        public int TotalItems => itemSprites?.Count(sprite => sprite != null) ?? 0;

        public bool HasRequiredItemCount => TotalItems == RequiredItemCount;

        public Sprite GetItemSprite(int itemIndex)
        {
            if (itemIndex < 0) return null;

            if (itemSprites != null)
            {
                int authoredIndex = 0;
                foreach (Sprite sprite in itemSprites)
                {
                    if (sprite == null) continue;
                    if (authoredIndex == itemIndex) return sprite;
                    authoredIndex++;
                }
            }

            return null;
        }

        public bool IsValidItemIndex(int itemIndex) => itemIndex >= 0 && itemIndex < TotalItems;

        public bool HasCompleteItemSet(IEnumerable<int> itemIndices)
        {
            if (!HasRequiredItemCount || itemIndices == null) return false;
            return itemIndices.Where(IsValidItemIndex).Distinct().Count() == RequiredItemCount;
        }

        #endregion
    }
}
