using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Replacement for the locked SpatialGrid.
    ///
    /// TWO CHANGES, both required before this scales past a few thousand
    /// entities.
    ///
    /// 1. THE LOCK IS GONE. The old grid guarded every read and write with
    ///    a single global lock, which was correct for the old shape (main
    ///    tick thread plus one NPC AI thread) but is a hard ceiling: once
    ///    you have N worker threads, that one lock IS the server and adding
    ///    cores makes things worse, not better. The replacement rule is
    ///    ownership instead of locking - each WorldWorker owns one grid
    ///    covering one region, and exactly one thread ever touches it.
    ///    Cross-region visibility is handled by border ghosting (a worker
    ///    publishes a read-only snapshot of its edge cells once per tick
    ///    and neighbours consume it), never by two threads sharing a grid.
    ///
    ///    IMPORTANT: this means NpcAiManager can no longer run on its own
    ///    thread poking the same grid. NPC AI becomes a system inside the
    ///    worker's tick. See INTEGRATION.md.
    ///
    /// 2. UPDATE REPORTS CELL TRANSITIONS. The old grid silently returned
    ///    when an entity stayed in its cell, which meant the caller
    ///    (InterestManager) had no way to know it could skip the expensive
    ///    part. Now Update returns true only when the entity actually
    ///    crossed a cell boundary. At a 50-unit cell size and normal run
    ///    speed a player crosses a boundary every few seconds, so this
    ///    turns a per-movement-packet AOI recompute into a per-few-seconds
    ///    one - roughly a 95% cut in the dominant cost.
    ///
    /// Query results are written into a caller-supplied List so the hot
    /// path allocates nothing. The caller owns the buffer and is expected
    /// to reuse one.
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly float _invCellSize;

        private readonly Dictionary<(int, int), List<int>> _cells = new();
        private readonly Dictionary<int, (int, int)> _entityCell = new();

        public SpatialGrid(float cellSize = 50f)
        {
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
        }

        public float CellSize => _cellSize;

        public (int, int) CellOf(Vector3 position) => (
            (int)MathF.Floor(position.X * _invCellSize),
            (int)MathF.Floor(position.Z * _invCellSize));

        /// <summary>
        /// Moves an entity. Returns TRUE only if it changed cells - which
        /// is the caller's cue that interest sets need recomputing. A false
        /// return means the entity moved within its cell and no spatial
        /// relationship can possibly have changed at cell granularity.
        /// </summary>
        public bool Update(int id, Vector3 position)
        {
            var newCell = CellOf(position);

            if (_entityCell.TryGetValue(id, out var oldCell))
            {
                if (oldCell == newCell)
                    return false;

                if (_cells.TryGetValue(oldCell, out var oldList))
                {
                    oldList.Remove(id);
                    if (oldList.Count == 0)
                        _cells.Remove(oldCell);
                }
            }

            _entityCell[id] = newCell;

            if (!_cells.TryGetValue(newCell, out var list))
                _cells[newCell] = list = new List<int>(capacity: 8);

            list.Add(id);
            return true;
        }

        public void Remove(int id)
        {
            if (!_entityCell.TryGetValue(id, out var cell))
                return;

            if (_cells.TryGetValue(cell, out var list))
            {
                list.Remove(id);
                if (list.Count == 0)
                    _cells.Remove(cell);
            }

            _entityCell.Remove(id);
        }

        public bool TryGetCell(int id, out (int, int) cell) =>
            _entityCell.TryGetValue(id, out cell);

        /// <summary>
        /// Fills <paramref name="results"/> with every entity in the
        /// radiusCells neighbourhood around this entity, excluding itself.
        /// Clears the list first. Allocates nothing if the caller reuses
        /// the buffer.
        /// </summary>
        public void GetNearby(int id, List<int> results, int radiusCells = 1)
        {
            results.Clear();

            if (!_entityCell.TryGetValue(id, out var center))
                return;

            GatherInto(center, radiusCells, results);
            results.Remove(id);
        }

        public void GetNearby(Vector3 position, List<int> results, int radiusCells = 1)
        {
            results.Clear();
            GatherInto(CellOf(position), radiusCells, results);
        }

        private void GatherInto((int, int) center, int radiusCells, List<int> results)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                if (_cells.TryGetValue((center.Item1 + dx, center.Item2 + dz), out var list))
                    results.AddRange(list);
            }
        }
    }
}
