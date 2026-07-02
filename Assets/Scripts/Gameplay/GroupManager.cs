using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

namespace StampJourney.Core
{
    /// <summary>
    /// Quản lý tất cả CardGroup trên board.
    /// Rebuild groups sau mỗi swap/gravity; cung cấp API cho group swap.
    /// </summary>
    public class GroupManager : SerializedMonoBehaviour
    {
        private Gameboard _board;
        [ShowInInspector, ReadOnly]
        private readonly Dictionary<int, CardGroup> _groups = new();

        // ---- Public API ----

        public void Init(Gameboard board)
        {
            _board = board;
            _groups.Clear();
        }

        /// <summary>
        /// Quét toàn bộ board, tạo lại tất cả groups từ đầu.
        /// Dùng Union-Find approach: duyệt từ trái→phải, trên→dưới,
        /// merge với neighbor phía trên và bên trái.
        /// Sau khi detect xong, tạo parent object cho mỗi group.
        /// </summary>
        public void RebuildGroups()
        {
            // Lấy danh sách groups hiện tại làm oldGroups
            var oldGroups = _groups.Values.ToList();
            _groups.Clear();

            // 1. Clear Group reference trên tất cả tiles
            for (int r = 0; r < _board.Rows; r++)
            {
                for (int c = 0; c < _board.Cols; c++)
                {
                    var tile = _board.GetTile(c, r);
                    if (tile != null) tile.Group = null;
                }
            }

            // 2. Duyệt grid (Union-Find)
            for (int r = 0; r < _board.Rows; r++)
            {
                for (int c = 0; c < _board.Cols; c++)
                {
                    var tile = _board.GetTile(c, r);
                    if (tile == null) continue;

                    TryMerge(tile, _board.GetTile(c, r - 1));
                    TryMerge(tile, _board.GetTile(c - 1, r));
                }
            }

            // 3. Clean up: loại bỏ groups <= 1 member
            var toRemove = _groups.Where(kvp => kvp.Value.Count <= 1).Select(kvp => kvp.Key).ToList();
            foreach (var id in toRemove)
            {
                _groups[id].Disband();
                _groups.Remove(id);
            }

            // 4. Đối chiếu old vs new
            var finalNewGroups = new List<CardGroup>();

            foreach (var newGroup in _groups.Values)
            {
                // Tìm oldGroup khớp hoàn toàn: cùng số lượng member, cùng bounds, cùng members
                var matchedOld = oldGroups.FirstOrDefault(old =>
                    old.Count == newGroup.Count &&
                    old.MinCol == newGroup.MinCol &&
                    old.MinRow == newGroup.MinRow &&
                    !newGroup.Members.Except(old.Members).Any());

                if (matchedOld != null && matchedOld.GroupTransform != null)
                {
                    // Tái sử dụng parent (nhóm không hề thay đổi)
                    newGroup.GroupTransform = matchedOld.GroupTransform;
                    oldGroups.Remove(matchedOld);
                }
                else
                {
                    finalNewGroups.Add(newGroup);
                }
            }

            // 5. Destroy parent của những oldGroups bị phá vỡ / di chuyển
            foreach (var oldGroup in oldGroups)
            {
                if (oldGroup.GroupTransform != null)
                {
                    var children = new List<Transform>();
                    for (int i = 0; i < oldGroup.GroupTransform.childCount; i++)
                        children.Add(oldGroup.GroupTransform.GetChild(i));

                    foreach (var child in children)
                        child.SetParent(_board.transform, true);

                    Object.Destroy(oldGroup.GroupTransform.gameObject);
                    oldGroup.GroupTransform = null;
                }
            }

            // 6. Tạo parent mới cho các groups vừa được tạo ra hoặc thay đổi
            foreach (var group in finalNewGroups)
            {
                CreateGroupParent(group);
            }

            Debug.Log($"[GroupManager] RebuildGroups — Total: {_groups.Count} | Rebuilt: {finalNewGroups.Count} | Kept: {_groups.Count - finalNewGroups.Count}");
        }

        /// <summary>
        /// Tạo parent GameObject tại tâm bounding box của group.
        /// Reparent tất cả CardView vào parent đó.
        /// </summary>
        private void CreateGroupParent(CardGroup group)
        {
            if (group.Count < 2) return;

            // Tính world center từ bounding box
            Vector2 minPos = _board.GetWorldPosition(group.MinCol, group.MaxRow); // bottom-left
            Vector2 maxPos = _board.GetWorldPosition(group.MaxCol, group.MinRow); // top-right
            Vector2 center = (minPos + maxPos) / 2f;

            // Tạo parent object
            var parentGO = new GameObject($"Group_{group.GroupId}_{group.Stamp.stampName}");
            parentGO.transform.SetParent(_board.transform, false);
            parentGO.transform.position = center;

            // Reparent tất cả member views vào parent
            foreach (var member in group.Members)
            {
                var view = _board.cardFactory.GetView(member.TileId);
                if (view != null)
                {
                    view.transform.SetParent(parentGO.transform, true); // worldPositionStays = true
                }
            }

            group.GroupTransform = parentGO.transform;
        }



        /// <summary>
        /// Trả tất cả card views về board root và destroy parent object.
        /// KHÔNG disband group data — chỉ dọn dẹp hierarchy.
        /// Gọi trước khi animate tiles riêng lẻ (e.g. sau group swap).
        /// </summary>
        public void UnparentGroupCards(CardGroup group)
        {
            if (group?.GroupTransform == null) return;

            // Dừng mọi animation đang chạy trên parent trước khi destroy để tránh lỗi DOTween
            group.GroupTransform.DOKill();

            // Đưa parent về mặc định để các children không bị lưu lại transform sai lệch

            group.GroupTransform.localScale = Vector3.one;
            group.GroupTransform.rotation = Quaternion.identity;

            var children = new List<Transform>();
            for (int i = 0; i < group.GroupTransform.childCount; i++)
                children.Add(group.GroupTransform.GetChild(i));

            foreach (var child in children)
            {
                child.SetParent(_board.transform, true);
                // Reset luôn transform của tile cho chắc chắn
                child.localScale = Vector3.one;
                child.rotation = Quaternion.identity;
            }

            Object.Destroy(group.GroupTransform.gameObject);
            group.GroupTransform = null;
        }

        /// <summary>
        /// Swap cả group theo (deltaCol, deltaRow).
        /// Trả về true nếu swap thành công.
        /// </summary>
        public bool TrySwapGroup(CardGroup group, int deltaCol, int deltaRow)
        {
            if (group == null || (deltaCol == 0 && deltaRow == 0)) return false;

            // 1. Tính vị trí mới cho tất cả members
            var members = group.Members.ToList();
            var newPositions = new List<(CardModel member, int newCol, int newRow)>();

            foreach (var member in members)
            {
                int newCol = member.BoardCol + deltaCol;
                int newRow = member.BoardRow + deltaRow;

                // Kiểm tra bounds
                if (!_board.IsInBounds(newCol, newRow))
                    return false;

                newPositions.Add((member, newCol, newRow));
            }

            // 2. Thu thập tiles bị displaced (ở vị trí đích nhưng không thuộc group này)
            var memberSet = new HashSet<int>(members.Select(m => m.TileId));
            var displaced = new List<(CardModel tile, int origCol, int origRow)>();

            foreach (var (member, newCol, newRow) in newPositions)
            {
                var targetTile = _board.GetTile(newCol, newRow);
                if (targetTile != null && !memberSet.Contains(targetTile.TileId))
                {
                    displaced.Add((targetTile, targetTile.BoardCol, targetTile.BoardRow));
                }
            }

            // 3. Xoá tất cả group members khỏi grid
            foreach (var member in members)
            {
                _board.SetTile(member.BoardCol, member.BoardRow, null);
            }

            // 4. Xoá displaced tiles khỏi grid
            foreach (var (tile, origCol, origRow) in displaced)
            {
                _board.SetTile(origCol, origRow, null);
            }

            // 5. Đặt group members vào vị trí mới
            foreach (var (member, newCol, newRow) in newPositions)
            {
                _board.SetTile(newCol, newRow, member);
            }

            // 6. Đặt displaced tiles vào vị trí cũ của group (best-effort)
            PlaceDisplacedTiles(displaced, members, deltaCol, deltaRow);

            return true;
        }

        /// <summary>
        /// Lấy group theo ID.
        /// </summary>
        public CardGroup GetGroup(int groupId) =>
            _groups.TryGetValue(groupId, out var g) ? g : null;

        /// <summary>
        /// Tất cả groups hiện tại.
        /// </summary>
        public IReadOnlyDictionary<int, CardGroup> AllGroups => _groups;

        // ---- Internal ----

        /// <summary>
        /// Thử merge tile vào group của neighbor (nếu hợp lệ).
        /// Trả về true nếu merge thành công.
        /// </summary>
        private bool TryMerge(CardModel tile, CardModel neighbor)
        {
            if (tile == null || neighbor == null) return false;
            if (!CardGroup.AreMatchingNeighbors(tile, neighbor)) return false;

            // Cả hai đã thuộc cùng group → skip
            if (tile.Group != null && tile.Group == neighbor.Group) return true;

            if (tile.Group != null && neighbor.Group != null)
            {
                // Cả hai đều có group → merge 2 group
                var keepGroup = tile.Group;
                var absorbGroup = neighbor.Group;
                keepGroup.Absorb(absorbGroup);
                _groups.Remove(absorbGroup.GroupId);
                return true;
            }

            if (neighbor.Group != null)
            {
                // Neighbor có group, tile chưa → thêm tile vào group neighbor
                neighbor.Group.Add(tile);
                return true;
            }

            if (tile.Group != null)
            {
                // Tile có group, neighbor chưa → thêm neighbor vào group tile
                tile.Group.Add(neighbor);
                return true;
            }

            // Cả hai chưa có group → tạo group mới
            var newGroup = new CardGroup(tile.Stamp);
            newGroup.Add(neighbor);
            newGroup.Add(tile);
            _groups[newGroup.GroupId] = newGroup;
            return true;
        }

        /// <summary>
        /// Đặt các tile bị displaced vào vị trí cũ của group members.
        /// Logic: displaced tile tại (origCol, origRow) → đẩy ngược về (origCol - deltaCol, origRow - deltaRow).
        /// </summary>
        private void PlaceDisplacedTiles(
            List<(CardModel tile, int origCol, int origRow)> displaced,
            List<CardModel> groupMembers,
            int deltaCol, int deltaRow)
        {
            // Collect vị trí cũ của group (trước khi move)
            var oldGroupPositions = new HashSet<(int, int)>();
            foreach (var member in groupMembers)
            {
                int oldCol = member.BoardCol - deltaCol;
                int oldRow = member.BoardRow - deltaRow;
                oldGroupPositions.Add((oldCol, oldRow));
            }

            foreach (var (tile, origCol, origRow) in displaced)
            {
                // Vị trí ngược: displaced tile được đẩy về phía ngược delta
                int targetCol = origCol - deltaCol;
                int targetRow = origRow - deltaRow;

                // Ưu tiên vị trí ngược delta, fallback sang vị trí cũ của group
                if (_board.IsInBounds(targetCol, targetRow) && _board.GetTile(targetCol, targetRow) == null)
                {
                    _board.SetTile(targetCol, targetRow, tile);
                }
                else
                {
                    // Fallback: tìm ô trống trong vùng cũ của group
                    bool placed = false;
                    foreach (var (oc, or) in oldGroupPositions)
                    {
                        if (_board.GetTile(oc, or) == null)
                        {
                            _board.SetTile(oc, or, tile);
                            oldGroupPositions.Remove((oc, or));
                            placed = true;
                            break;
                        }
                    }

                    if (!placed)
                    {
                        Debug.LogWarning($"[GroupManager] Could not place displaced tile {tile}");
                    }
                }
            }
        }
    }
}
