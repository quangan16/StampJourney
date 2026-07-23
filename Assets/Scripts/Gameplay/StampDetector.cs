using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Manages all CardGroups on the board and detects completed topics.
    /// Rebuilds groups after each swap/gravity; provides API for group swap.
    /// A completed topic = its four authored item cards arranged as a 2x2 square.
    /// </summary>
    public class StampDetector : SerializedMonoBehaviour
    {
        private Gameboard _board;

        [BoxGroup("Merge Jelly")]
        [LabelText("Enable Group Jelly")]
        public bool enableGroupJelly = true;

        [BoxGroup("Merge Jelly")]
        [ShowIf("enableGroupJelly")]
        [Range(0.03f, 0.2f)]
        public float groupJellyStrength = 0.1f;

        [BoxGroup("Merge Jelly")]
        [ShowIf("enableGroupJelly")]
        [MinValue(0.15f)]
        public float groupJellyDuration = 0.48f;

        [ShowInInspector, ReadOnly]
        private readonly Dictionary<int, CardGroup> _groups = new();
        private readonly Dictionary<int, string> _lastGroupLayouts = new();

        private bool _hasCompletedInitialRebuild;

        #region Public API

        public void Init(Gameboard board)
        {
            _board = board;
            _groups.Clear();
            _lastGroupLayouts.Clear();
            _hasCompletedInitialRebuild = false;
        }

        public CardGroup GetGroup(int groupId) =>
            _groups.TryGetValue(groupId, out var g) ? g : null;

        public IReadOnlyDictionary<int, CardGroup> AllGroups => _groups;

        #endregion

        #region Group Management

        /// <summary>
        /// Scans the entire board and rebuilds all groups from scratch.
        /// Uses a flood-fill approach: scans left→right, top→bottom,
        /// merging matching neighbors.
        /// After detection, creates parent objects for each group.
        /// </summary>
        public void RebuildGroups()
        {
            var oldGroups = _groups.Values.ToList();
            var newlyFormedGroupIds = new HashSet<int>();
            _groups.Clear();

            // 1. Clear Group references on all tiles
            ClearAllGroupReferences();

            // 2. Flood-fill to find new logical groups
            var newLogicalGroups = FindLogicalGroups();

            // 3. Match old groups to new, reuse where possible
            ReconcileGroups(newLogicalGroups, oldGroups, newlyFormedGroupIds);

            // 4. Destroy orphaned old groups
            DestroyOrphanedGroups(oldGroups);

            // 5. Update or create parent transforms for all groups. Compare member positions
            // relative to the group's own bounds so internal changes replay jelly, while moving
            // the whole unchanged group to another board position does not.
            var currentGroupLayouts = new Dictionary<int, string>();
            foreach (var group in _groups.Values)
            {
                string currentLayout = BuildGroupLayoutSignature(group);
                bool layoutChanged =
                    _lastGroupLayouts.TryGetValue(group.GroupId, out string previousLayout) &&
                    previousLayout != currentLayout;
                currentGroupLayouts[group.GroupId] = currentLayout;

                UpdateOrCreateGroupParent(group);

                bool shouldPlayJelly =
                    newlyFormedGroupIds.Contains(group.GroupId) || layoutChanged;
                if (_hasCompletedInitialRebuild && shouldPlayJelly)
                    PlayGroupMergeJelly(group);
            }

            _lastGroupLayouts.Clear();
            foreach (var pair in currentGroupLayouts)
                _lastGroupLayouts[pair.Key] = pair.Value;

            UpdateAllLinkStateColors(_hasCompletedInitialRebuild);

            _hasCompletedInitialRebuild = true;

            Debug.Log($"[StampDetector] RebuildGroups — Total: {_groups.Count}");
        }

        public void UnparentGroupCards(CardGroup group)
        {
            if (group == null || group.gameObject == null) return;

            group.transform.DOKill();
            group.transform.localScale = Vector3.one;
            group.transform.rotation = Quaternion.identity;

            DetachChildren(group.transform);
        }

        #endregion

        #region Group Swap

        public bool TrySwapGroup(CardGroup group, int deltaCol, int deltaRow)
        {
            if (group == null || (deltaCol == 0 && deltaRow == 0)) return false;

            // 1. Calculate new positions for all members
            var members = group.Members.ToList();
            var newPositions = new List<(CardModel member, int newCol, int newRow)>();

            foreach (var member in members)
            {
                int newCol = member.BoardCol + deltaCol;
                int newRow = member.BoardRow + deltaRow;

                if (!_board.IsInBounds(newCol, newRow))
                    return false;

                newPositions.Add((member, newCol, newRow));
            }

            // 2. Collect displaced tiles (at target positions but not in this group)
            var memberSet = new HashSet<int>(members.Select(m => m.TileId));
            var displaced = new List<(CardModel tile, int origCol, int origRow)>();

            foreach (var (member, newCol, newRow) in newPositions)
            {
                var targetTile = _board.GetCard(newCol, newRow);
                if (targetTile != null && !memberSet.Contains(targetTile.TileId))
                {
                    displaced.Add((targetTile, targetTile.BoardCol, targetTile.BoardRow));
                }
            }

            // 3. Remove all group members from grid
            foreach (var member in members)
                _board.SetTile(member.BoardCol, member.BoardRow, null);

            // 4. Remove displaced tiles from grid
            foreach (var (tile, origCol, origRow) in displaced)
                _board.SetTile(origCol, origRow, null);

            // 5. Place group members at new positions
            foreach (var (member, newCol, newRow) in newPositions)
                _board.SetTile(newCol, newRow, member);

            // 6. Place displaced tiles into vacated positions (best-effort)
            PlaceDisplacedTiles(displaced, members, deltaCol, deltaRow);

            return true;
        }

        #endregion

        #region Stamp Detection

        /// <summary>
        /// Scans the entire grid and returns groups that form completed topics.
        /// Each result element is a CardGroup of cards forming one completed topic.
        /// </summary>
        public List<CardGroup> FindCompletedStamps(Tile[,] tiles, int boardCols, int boardRows)
        {
            var results = new List<CardGroup>();
            foreach (var group in _groups.Values)
            {
                if (group.IsTopicComplete)
                    results.Add(group);
            }

            return results;
        }

        #endregion

        #region Private — Group Rebuild Helpers

        private void ClearAllGroupReferences()
        {
            for (int r = 0; r < _board.Rows; r++)
                for (int c = 0; c < _board.Cols; c++)
                {
                    var card = _board.GetCard(c, r);
                    if (card != null) card.Group = null;
                }
        }

        private static string BuildGroupLayoutSignature(CardGroup group)
        {
            int originCol = group.Members.Min(member => member.BoardCol);
            int originRow = group.Members.Min(member => member.BoardRow);

            return string.Join(
                "|",
                group.Members
                    .OrderBy(member => member.TileId)
                    .Select(member =>
                        $"{member.TileId}:{member.BoardCol - originCol}:{member.BoardRow - originRow}"));
        }

        private List<List<CardModel>> FindLogicalGroups()
        {
            var result = new List<List<CardModel>>();
            var visited = new HashSet<CardModel>();

            for (int r = 0; r < _board.Rows; r++)
            {
                for (int c = 0; c < _board.Cols; c++)
                {
                    var card = _board.GetCard(c, r);
                    if (card == null || card.IsIced || visited.Contains(card)) continue;

                    var groupMembers = new List<CardModel>();
                    var queue = new Queue<CardModel>();
                    groupMembers.Add(card);
                    queue.Enqueue(card);
                    visited.Add(card);

                    while (queue.Count > 0)
                    {
                        var curr = queue.Dequeue();

                        var neighbors = new[]
                        {
                            _board.GetCard(curr.BoardCol, curr.BoardRow - 1),
                            _board.GetCard(curr.BoardCol, curr.BoardRow + 1),
                            _board.GetCard(curr.BoardCol - 1, curr.BoardRow),
                            _board.GetCard(curr.BoardCol + 1, curr.BoardRow)
                        };

                        foreach (var n in neighbors)
                        {
                            if (n != null &&
                                !visited.Contains(n) &&
                                CardGroup.CanFormTopicSquareGroup(groupMembers, n))
                            {
                                groupMembers.Add(n);
                                visited.Add(n);
                                queue.Enqueue(n);
                            }
                        }
                    }

                    if (groupMembers.Count > 1)
                        result.Add(groupMembers);
                }
            }

            return result;
        }

        private void ReconcileGroups(
            List<List<CardModel>> newLogicalGroups,
            List<CardGroup> oldGroups,
            HashSet<int> newlyFormedGroupIds)
        {
            foreach (var logicalGroup in newLogicalGroups)
            {
                var matchedOld = oldGroups.FirstOrDefault(old =>
                    old.Count == logicalGroup.Count &&
                    !logicalGroup.Except(old.Members).Any());

                if (matchedOld != null)
                {
                    // Reuse existing group (no ID churn)
                    _groups[matchedOld.GroupId] = matchedOld;
                    oldGroups.Remove(matchedOld);

                    foreach (var member in logicalGroup)
                        member.Group = matchedOld;

                    matchedOld.RecalculateBounds();
                }
                else
                {
                    // Create a new group
                    var go = new GameObject();
                    var newGroup = go.AddComponent<CardGroup>();
                    newGroup.Init(logicalGroup[0].Topic);
                    foreach (var member in logicalGroup)
                        newGroup.Add(member);

                    _groups[newGroup.GroupId] = newGroup;
                    newlyFormedGroupIds.Add(newGroup.GroupId);
                }
            }
        }

        private void DestroyOrphanedGroups(List<CardGroup> oldGroups)
        {
            foreach (var oldGroup in oldGroups)
            {
                if (oldGroup == null || oldGroup.gameObject == null) continue;

                oldGroup.transform.DOKill();
                oldGroup.transform.rotation = Quaternion.identity;

                DetachChildren(oldGroup.transform);
                Destroy(oldGroup.gameObject);
            }
        }

        #endregion

        #region Private — Group Parenting

        private void UpdateOrCreateGroupParent(CardGroup group)
        {
            if (group.Count < 2) return;

            // Calculate world center from bounding box
            Vector2 minPos = _board.GetWorldPosition(group.MinCol, group.MaxRow); // bottom-left
            Vector2 maxPos = _board.GetWorldPosition(group.MaxCol, group.MinRow); // top-right
            Vector2 center = (minPos + maxPos) / 2f;

            group.transform.SetParent(_board.MainboardHolder, false);
            group.transform.DOKill();

            // Temporarily unparent children so moving the group doesn't shift them
            DetachChildren(group.transform);

            group.transform.position = center;
            group.transform.localScale = Vector3.one;
            group.transform.rotation = Quaternion.identity;

            group.gameObject.name = $"Group_{group.GroupId}_{group.Topic.TopicName}";

            // Group parents are movement-only transforms. A parent SortingGroup would flatten
            // every card and bridge into one render unit, allowing a bridge to cover cards in
            // another group. Remove legacy runtime components left by an earlier rebuild.
            if (group.gameObject.TryGetComponent<UnityEngine.Rendering.SortingGroup>(out var legacySortingGroup))
            {
                legacySortingGroup.enabled = false;
                Destroy(legacySortingGroup);
            }

            // Reparent all member views under the group
            foreach (var member in group.Members)
            {
                var view = _board.cardFactory.GetView(member.TileId);
                if (view != null)
                {
                    view.transform.SetParent(group.transform, true);
                    view.transform.localScale = Vector3.one;
                    view.transform.localRotation = Quaternion.identity;
                    view.SetSortingOrder(view.baseSortingOrder);
                }
            }
        }

        /// <summary>
        /// Squashes and stretches a newly connected group like soft pudding.
        /// The dominant axis follows the group's shape and each rebound loses energy.
        /// </summary>
        private void PlayGroupMergeJelly(CardGroup group)
        {
            if (!enableGroupJelly || group == null || group.GroupTransform == null) return;

            Transform target = group.GroupTransform;
            float strength = Mathf.Clamp(groupJellyStrength, 0.03f, 0.2f);
            float duration = Mathf.Max(0.15f, groupJellyDuration);
            bool horizontalGroup = group.Width >= group.Height;

            Vector3 firstSquash = horizontalGroup
                ? new Vector3(1f + strength, 1f - strength * 0.72f, 1f)
                : new Vector3(1f - strength * 0.72f, 1f + strength, 1f);
            Vector3 firstRebound = horizontalGroup
                ? new Vector3(1f - strength * 0.55f, 1f + strength * 0.48f, 1f)
                : new Vector3(1f + strength * 0.48f, 1f - strength * 0.55f, 1f);
            Vector3 secondRebound = horizontalGroup
                ? new Vector3(1f + strength * 0.24f, 1f - strength * 0.18f, 1f)
                : new Vector3(1f - strength * 0.18f, 1f + strength * 0.24f, 1f);

            target.DOKill(false);
            target.localScale = Vector3.one;

            DOTween.Sequence()
                .SetTarget(target)
                .Append(target.DOScale(firstSquash, duration * 0.22f).SetEase(Ease.OutQuad))
                .Append(target.DOScale(firstRebound, duration * 0.28f).SetEase(Ease.InOutSine))
                .Append(target.DOScale(secondRebound, duration * 0.23f).SetEase(Ease.InOutSine))
                .Append(target.DOScale(Vector3.one, duration * 0.27f).SetEase(Ease.OutSine))
                .OnKill(() =>
                {
                    if (target != null) target.localScale = Vector3.one;
                })
                .OnComplete(() =>
                {
                    if (target != null) target.localScale = Vector3.one;
                });
        }

        /// <summary>
        /// Applies blue/green/orange/purple state colors to every board card. Standalone cards
        /// count as one, while connected cards use their current logical group size.
        /// </summary>
        private void UpdateAllLinkStateColors(bool animate)
        {
            for (int row = 0; row < _board.Rows; row++)
            {
                for (int col = 0; col < _board.Cols; col++)
                {
                    CardModel model = _board.GetCard(col, row);
                    if (model == null) continue;

                    CardView view = _board.cardFactory.GetView(model.TileId);
                    if (view == null) continue;

                    view.SetLinkedItemCount(model.Group?.Count ?? 1, animate);
                }
            }
        }

        /// <summary>
        /// Detaches all children from a transform, reparenting them to the board.
        /// Preserves world position and resets scale/rotation.
        /// </summary>
        private void DetachChildren(Transform parent)
        {
            var children = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
                children.Add(parent.GetChild(i));

            foreach (var child in children)
            {
                child.SetParent(_board.MainboardHolder, true);
                child.localScale = Vector3.one;
                child.localRotation = Quaternion.identity;
            }
        }

        #endregion

        #region Private — Displaced Tile Placement

        private void PlaceDisplacedTiles(
            List<(CardModel tile, int origCol, int origRow)> displaced,
            List<CardModel> groupMembers,
            int deltaCol, int deltaRow)
        {
            var oldGroupPositions = new HashSet<(int, int)>();
            foreach (var member in groupMembers)
            {
                int oldCol = member.BoardCol - deltaCol;
                int oldRow = member.BoardRow - deltaRow;
                oldGroupPositions.Add((oldCol, oldRow));
            }

            foreach (var (tile, origCol, origRow) in displaced)
            {
                int targetCol = origCol - deltaCol;
                int targetRow = origRow - deltaRow;

                if (_board.IsInBounds(targetCol, targetRow) && _board.GetCard(targetCol, targetRow) == null)
                {
                    _board.SetTile(targetCol, targetRow, tile);
                }
                else
                {
                    bool placed = false;
                    foreach (var (oc, or) in oldGroupPositions.ToList())
                    {
                        int moveCol = oc - origCol;
                        int moveRow = or - origRow;
                        if (_board.GetCard(oc, or) == null &&
                            tile.AllowsPlayerMove(moveCol, moveRow))
                        {
                            _board.SetTile(oc, or, tile);
                            oldGroupPositions.Remove((oc, or));
                            placed = true;
                            break;
                        }
                    }

                    if (!placed)
                        Debug.LogWarning($"[StampDetector] Could not place displaced tile {tile}");
                }
            }
        }

        #endregion

        #region Private — Stamp Matching

        #endregion
    }
}
