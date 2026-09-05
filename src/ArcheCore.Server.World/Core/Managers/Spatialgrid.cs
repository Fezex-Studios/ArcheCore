using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Buckets entities into cells on the XZ plane so "who's near this position"
    /// is a handful of dictionary lookups instead of scanning every entity.
    /// Not thread-safe - call from the same thread that owns PlayerManager's state
    /// (matches how player sessions are already accessed, via peer.Tag).
    /// </summary>
    public class SpatialGrid
    {
        private readonly float _cellSize;

        private readonly Dictionary<(int, int), HashSet<int>> _cells = new();
        private readonly Dictionary<int, (int, int)> _entityCell = new();

        public SpatialGrid(float cellSize = 50f)
        {
            _cellSize = cellSize;
        }

        private (int, int) CellOf(Vector3 position)
        {
            return (
                (int)MathF.Floor(position.X / _cellSize),
                (int)MathF.Floor(position.Z / _cellSize));
        }

        public void Update(int id, Vector3 position)
        {
            var newCell = CellOf(position);

            if (_entityCell.TryGetValue(id, out var oldCell))
            {
                if (oldCell == newCell)
                    return;

                if (_cells.TryGetValue(oldCell, out var oldSet))
                {
                    oldSet.Remove(id);
                    if (oldSet.Count == 0)
                        _cells.Remove(oldCell);
                }
            }

            _entityCell[id] = newCell;

            if (!_cells.TryGetValue(newCell, out var set))
                _cells[newCell] = set = new HashSet<int>();

            set.Add(id);
        }

        public void Remove(int id)
        {
            if (!_entityCell.TryGetValue(id, out var cell))
                return;

            if (_cells.TryGetValue(cell, out var set))
            {
                set.Remove(id);
                if (set.Count == 0)
                    _cells.Remove(cell);
            }

            _entityCell.Remove(id);
        }

        /// <summary>
        /// Every other entity sharing this entity's cell or an adjacent one.
        /// radiusCells=1 means a 3x3 neighborhood - with the default 50-unit
        /// cell size that's up to ~150 units of awareness range.
        /// </summary>
        public IEnumerable<int> GetNearby(int id, int radiusCells = 1)
        {
            if (!_entityCell.TryGetValue(id, out var center))
                yield break;

            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                var key = (center.Item1 + dx, center.Item2 + dz);

                if (!_cells.TryGetValue(key, out var set))
                    continue;

                foreach (var otherId in set)
                {
                    if (otherId != id)
                        yield return otherId;
                }
            }
        }
    }
}