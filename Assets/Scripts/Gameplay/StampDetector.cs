using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sirenix.OdinInspector;
using StampJourney.Card;
using StampJourney.Core;
using StampJourney.Data;
using UnityEngine;

namespace StampJourney.Gameplay
{
    /// <summary>
    /// Quản lý tất cả CardGroup trên board và phát hiện các tem hoàn chỉnh.
    /// Rebuild groups sau mỗi swap/gravity; cung cấp API cho group swap.
    /// Một tem hoàn chỉnh = n×m ô liền kề, mỗi ô thuộc đúng stamp và đúng piece position.
    /// </summary>
    public class StampDetector : SerializedMonoBehaviour
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

        // ==========================================
        // GROUP MANAGEMENT LOGIC
        // ==========================================

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
                    var card = _board.GetCard(c, r);
                    if (card != null) card.Group = null;
                }
            }

            // 2. Duyệt grid (Union-Find)
            for (int r = 0; r < _board.Rows; r++)
            {
                for (int c = 0; c < _board.Cols; c++)
                {
                    var card = _board.GetCard(c, r);
                    if (card == null) continue;

                    TryMerge(card, _board.GetCard(c, r - 1));
                    TryMerge(card, _board.GetCard(c - 1, r));
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
            foreach (var newGroup in _groups.Values)
            {
                // Tìm oldGroup khớp hoàn toàn (chỉ cần khớp members, không cần khớp bounds vì bounds có thể đổi do gravity/swap)
                var matchedOld = oldGroups.FirstOrDefault(old =>
                    old.Count == newGroup.Count &&
                    !newGroup.Members.Except(old.Members).Any());

                if (matchedOld != null && matchedOld.GroupTransform != null)
                {
                    // Tái sử dụng parent
                    newGroup.GroupTransform = matchedOld.GroupTransform;
                    oldGroups.Remove(matchedOld);
                }
            }

            // 5. Destroy parent của những oldGroups bị phá vỡ
            foreach (var oldGroup in oldGroups)
            {
                if (oldGroup.GroupTransform != null)
                {
                    // LỖI GLITCH ROTATION: Tương tự lúc reparent, nếu group cũ đang bị nghiêng/scale
                    // do animation thả chuột mà bị phá vỡ (do merge), các child sẽ giữ nguyên
                    // độ nghiêng đó khi unparent. Cần reset parent về chuẩn trước khi unparent.
                    oldGroup.GroupTransform.DOKill();
                    oldGroup.GroupTransform.localScale = Vector3.one;
                    oldGroup.GroupTransform.rotation = Quaternion.identity;

                    var children = new List<Transform>();
                    for (int i = 0; i < oldGroup.GroupTransform.childCount; i++)
                        children.Add(oldGroup.GroupTransform.GetChild(i));

                    foreach (var child in children)
                        child.SetParent(_board.transform, true);

                    Destroy(oldGroup.GroupTransform.gameObject);
                    oldGroup.GroupTransform = null;
                }
            }

            // 6. Cập nhật hoặc tạo parent mới cho tất cả các groups
            foreach (var group in _groups.Values)
            {
                UpdateOrCreateGroupParent(group);
            }

            Debug.Log($"[StampDetector] RebuildGroups — Total: {_groups.Count}");
        }

        private void UpdateOrCreateGroupParent(CardGroup group)
        {
            if (group.Count < 2) return;

            // Tính world center từ bounding box
            Vector2 minPos = _board.GetWorldPosition(group.MinCol, group.MaxRow); // bottom-left
            Vector2 maxPos = _board.GetWorldPosition(group.MaxCol, group.MinRow); // top-right
            Vector2 center = (minPos + maxPos) / 2f;

            if (group.GroupTransform == null)
            {
                // Tạo parent object mới
                var parentGO = new GameObject($"Group_{group.GroupId}_{group.Stamp.stampName}");
                parentGO.transform.SetParent(_board.transform, false);
                parentGO.transform.position = center;


                var sortingGroup = parentGO.AddComponent<UnityEngine.Rendering.SortingGroup>();
                sortingGroup.sortingOrder = 10; // Default base order for tiles
                group.GroupTransform = parentGO.transform;
            }
            else
            {
                // Cập nhật lại center cho parent cũ (nếu group bị di chuyển bởi gravity/swap)
                
                // Sửa lỗi glitch local position: Phải reset scale và rotation của parent về mặc định 
                // TRƯỚC KHI reparent. Nếu parent đang bị scale/rotate bởi DOTween (vd: lift/tilt), 
                // hàm SetParent(true) sẽ tính sai localPosition và làm lệch thẻ bài vĩnh viễn.
                group.GroupTransform.DOKill();
                group.GroupTransform.localScale = Vector3.one;
                group.GroupTransform.rotation = Quaternion.identity;

                // Phải tạm thời unparent các children để chúng không bị di chuyển theo khi parent nhảy về center mới
                var children = new List<Transform>();
                for (int i = 0; i < group.GroupTransform.childCount; i++)
                    children.Add(group.GroupTransform.GetChild(i));

                foreach (var child in children)
                    child.SetParent(_board.transform, true);

                group.GroupTransform.position = center;
                group.GroupTransform.name = $"Group_{group.GroupId}_{group.Stamp.stampName}"; // Cập nhật tên
            }

            // Reparent tất cả member views vào parent
            foreach (var member in group.Members)
            {
                var view = _board.cardFactory.GetView(member.TileId);
                if (view != null)
                {
                    view.transform.SetParent(group.GroupTransform, true); // worldPositionStays = true
                }
            }
        }

        public void UnparentGroupCards(CardGroup group)
        {
            if (group?.GroupTransform == null) return;

            // Dừng mọi animation đang chạy trên parent trước khi destroy/unparent để tránh lỗi DOTween
            group.GroupTransform.DOKill();

            group.GroupTransform.localScale = Vector3.one;
            group.GroupTransform.rotation = Quaternion.identity;

            var children = new List<Transform>();
            for (int i = 0; i < group.GroupTransform.childCount; i++)
                children.Add(group.GroupTransform.GetChild(i));

            foreach (var child in children)
            {
                child.SetParent(_board.transform, true);
                child.localScale = Vector3.one;
                child.rotation = Quaternion.identity;
            }

            // KHÔNG destroy GroupTransform ở đây nữa. Để RebuildGroups tái sử dụng nó.
        }

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
                var targetTile = _board.GetCard(newCol, newRow);
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

        public CardGroup GetGroup(int groupId) =>
            _groups.TryGetValue(groupId, out var g) ? g : null;

        public IReadOnlyDictionary<int, CardGroup> AllGroups => _groups;

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
                    {
                        Debug.LogWarning($"[StampDetector] Could not place displaced tile {tile}");
                    }
                }
            }
        }

        // ==========================================
        // STAMP DETECTION LOGIC
        // ==========================================

        /// <summary>
        /// Quét toàn bộ grid, trả về danh sách các nhóm tile tạo thành tem hoàn chỉnh.
        /// Mỗi phần tử trong kết quả là danh sách tiles của 1 tem hoàn chỉnh.
        /// </summary>
        public List<List<CardModel>> FindCompletedStamps(Tile[,] tiles, int boardCols, int boardRows)
        {
            var results = new List<List<CardModel>>();
            // Theo dõi các ô đã nằm trong match để không đếm trùng
            bool[,] used = new bool[boardCols, boardRows];

            // ---- Shortcut: kiểm tra group hoàn chỉnh trước ----
            // Nếu 1 group có đủ members == stamp.TotalPieces → stamp hoàn chỉnh
            foreach (var group in _groups.Values)
            {
                if (group.IsStampComplete)
                {
                    var match = new List<CardModel>(group.Members);
                    results.Add(match);
                    foreach (var t in match)
                        used[t.BoardCol, t.BoardRow] = true;
                }
            }

            // ---- Grid scan bình thường (cho trường hợp tile chưa thuộc group) ----
            for (int r = 0; r < boardRows; r++)
                for (int c = 0; c < boardCols; c++)
                {
                    if (used[c, r]) continue;

                    var tileModel = tiles[c, r];
                    var tile = tileModel?.Card;
                    if (tile == null) continue;

                    // Chỉ xét tile là góc trên-trái của stamp (pieceCol==0, pieceRow==0)
                    if (tile.PieceCol != 0 || tile.PieceRow != 0) continue;

                    var stamp = tile.Stamp;
                    var match = TryMatchStamp(tiles, boardCols, boardRows, c, r, stamp, used);
                    if (match != null)
                    {
                        results.Add(match);
                        // Đánh dấu used
                        foreach (var t in match)
                            used[t.BoardCol, t.BoardRow] = true;
                    }
                }

            return results;
        }

        /// <summary>
        /// Thử khớp stamp bắt đầu từ góc trên-trái (anchorCol, anchorRow).
        /// Trả về danh sách tiles nếu khớp, null nếu không.
        /// </summary>
        private List<CardModel> TryMatchStamp(
            Tile[,] tiles, int boardCols, int boardRows,
            int anchorCol, int anchorRow, StampData stamp, bool[,] used)
        {
            int sc = stamp.cols;
            int sr = stamp.rows;

            // Kiểm tra bounds
            if (anchorCol + sc > boardCols) return null;
            if (anchorRow + sr > boardRows) return null;

            var group = new List<CardModel>(sc * sr);

            for (int pr = 0; pr < sr; pr++)
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

            return group;
        }
    }
}
