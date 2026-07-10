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
    /// Manages all CardGroups on the board and detects completed stamps.
    /// Rebuilds groups after each swap/gravity; provides API for group swap.
    /// A completed stamp = n×m adjacent cells, each matching the correct stamp and piece position.
    /// </summary>
    public class StampDetector : SerializedMonoBehaviour
    {
        private Gameboard _board;

        [ShowInInspector, ReadOnly]
        private readonly Dictionary<int, CardGroup> _groups = new();

        #region Public API

        public void Init(Gameboard board)
        {
            _board = board;
            _groups.Clear();
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
            _groups.Clear();

            // 1. Clear Group references on all tiles
            ClearAllGroupReferences();

            // 2. Flood-fill to find new logical groups
            var newLogicalGroups = FindLogicalGroups();

            // 3. Match old groups to new, reuse where possible
            ReconcileGroups(newLogicalGroups, oldGroups);

            // 4. Destroy orphaned old groups
            DestroyOrphanedGroups(oldGroups);

            // 5. Update or create parent transforms for all groups
            foreach (var group in _groups.Values)
                UpdateOrCreateGroupParent(group);

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
        /// Scans the entire grid and returns groups that form completed stamps.
        /// Each result element is a CardGroup of tiles forming one completed stamp.
        /// </summary>
        public List<CardGroup> FindCompletedStamps(Tile[,] tiles, int boardCols, int boardRows)
        {
            var results = new List<CardGroup>();
            bool[,] used = new bool[boardCols, boardRows];

            // Shortcut: check existing groups that are already complete
            foreach (var group in _groups.Values)
            {
                if (group.IsStampComplete)
                {
                    results.Add(group);
                    foreach (var t in group.Members)
                        used[t.BoardCol, t.BoardRow] = true;
                }
            }

            // Grid scan for tiles not yet in any group
            for (int r = 0; r < boardRows; r++)
            {
                for (int c = 0; c < boardCols; c++)
                {
                    if (used[c, r]) continue;

                    var tileModel = tiles[c, r];
                    var tile = tileModel?.Card;
                    if (tile == null) continue;

                    // Only consider tiles that are the top-left corner of a stamp
                    if (tile.PieceCol != 0 || tile.PieceRow != 0) continue;

                    var stamp = tile.Stamp;
                    var match = TryMatchStamp(tiles, boardCols, boardRows, c, r, stamp, used);
                    if (match != null)
                    {
                        var go = new GameObject();
                        var newGroup = go.AddComponent<CardGroup>();
                        newGroup.Init(stamp);
                        foreach (var t in match)
                            newGroup.Add(t);

                        results.Add(newGroup);
                        foreach (var t in match)
                            used[t.BoardCol, t.BoardRow] = true;
                    }
                }
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

        private List<List<CardModel>> FindLogicalGroups()
        {
            var result = new List<List<CardModel>>();
            var visited = new HashSet<CardModel>();

            for (int r = 0; r < _board.Rows; r++)
            {
                for (int c = 0; c < _board.Cols; c++)
                {
                    var card = _board.GetCard(c, r);
                    if (card == null || visited.Contains(card)) continue;

                    var groupMembers = new List<CardModel>();
                    var queue = new Queue<CardModel>();
                    queue.Enqueue(card);
                    visited.Add(card);

                    while (queue.Count > 0)
                    {
                        var curr = queue.Dequeue();
                        groupMembers.Add(curr);

                        var neighbors = new[]
                        {
                            _board.GetCard(curr.BoardCol, curr.BoardRow - 1),
                            _board.GetCard(curr.BoardCol, curr.BoardRow + 1),
                            _board.GetCard(curr.BoardCol - 1, curr.BoardRow),
                            _board.GetCard(curr.BoardCol + 1, curr.BoardRow)
                        };

                        foreach (var n in neighbors)
                        {
                            if (n != null && !visited.Contains(n) && CardGroup.AreMatchingNeighbors(curr, n))
                            {
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

        private void ReconcileGroups(List<List<CardModel>> newLogicalGroups, List<CardGroup> oldGroups)
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
                    newGroup.Init(logicalGroup[0].Stamp);
                    foreach (var member in logicalGroup)
                        newGroup.Add(member);

                    _groups[newGroup.GroupId] = newGroup;
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

            group.transform.SetParent(_board.transform, false);
            group.transform.DOKill();

            // Temporarily unparent children so moving the group doesn't shift them
            DetachChildren(group.transform);

            group.transform.position = center;
            group.transform.localScale = Vector3.one;
            group.transform.rotation = Quaternion.identity;

            group.gameObject.name = $"Group_{group.GroupId}_{group.Stamp.stampName}";

            // Ensure SortingGroup exists
            var sortingGroup = group.gameObject.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (sortingGroup == null)
            {
                sortingGroup = group.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
                sortingGroup.sortingOrder = 10;
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
                child.SetParent(_board.transform, true);
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
                    foreach (var (oc, or) in oldGroupPositions)
                    {
                        if (_board.GetCard(oc, or) == null)
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

        /// <summary>
        /// Tries to match a stamp starting from the top-left anchor (anchorCol, anchorRow).
        /// Returns the list of matching tiles, or null if no match.
        /// </summary>
        private List<CardModel> TryMatchStamp(
            Tile[,] tiles, int boardCols, int boardRows,
            int anchorCol, int anchorRow, StampData stamp, bool[,] used)
        {
            int sc = stamp.cols;
            int sr = stamp.rows;

            if (anchorCol + sc > boardCols) return null;
            if (anchorRow + sr > boardRows) return null;

            var group = new List<CardModel>(sc * sr);

            for (int pr = 0; pr < sr; pr++)
            {
                for (int pc = 0; pc < sc; pc++)
                {
                    int bc = anchorCol + pc;
                    int br = anchorRow + pr;

                    if (used[bc, br]) return null;

                    var tModel = tiles[bc, br];
                    var t = tModel?.Card;
                    if (t == null) return null;
                    if (t.Stamp.stampId != stamp.stampId) return null;
                    if (t.PieceCol != pc) return null;
                    if (t.PieceRow != pr) return null;

                    group.Add(t);
                }
            }

            return group;
        }

        #endregion
    }
}
