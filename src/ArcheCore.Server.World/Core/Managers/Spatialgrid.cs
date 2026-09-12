using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Buckets entities into cells on the XZ plane so "who's near this position"
    /// is a handful of dictionary lookups instead of scanning every entity.
    ///
    /// Thread-safe: guarded by a single lock. This matters now that NPC AI
    /// runs on its own background thread (NpcAiManager) while the main tick
    /// thread is simultaneously updating player positions through the same
    /// grid - without locking, that's two threads mutating the same
    /// Dictionary at once. All query methods materialize their results
    /// eagerly (into a List) before returning, rather than yielding lazily,
    /// so the lock actually covers the whole read instead of being released
    /// before the caller finishes enumerating.
    /// </summary>
    public class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly object _lock = new();

        private readonly Dictionary<(int, int), HashSet<int>> _cells = new();
        private readonly Dictionary<int, (int, int)> _entityCell = new();

        public SpatialGrid(float cellSize = 50f)
        {
            _cellSize = cellSize;
        }

        public float CellSize => _cellSize;

        private (int, int) CellOf(Vector3 position)
        {
            return (
                (int)MathF.Floor(position.X / _cellSize),
                (int)MathF.Floor(position.Z / _cellSize));
        }

        public void Update(int id, Vector3 position)
        {
            lock (_lock)
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
        }

        public void Remove(int id)
        {
            lock (_lock)
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
        }

        /// <summary>
        /// Every other entity sharing this entity's cell or an adjacent one.
        /// radiusCells=1 means a 3x3 neighborhood - with the default 50-unit
        /// cell size that's up to ~150 units of awareness range.
        /// </summary>
        public List<int> GetNearby(int id, int radiusCells = 1)
        {
            lock (_lock)
            {
                if (!_entityCell.TryGetValue(id, out var center))
                    return new List<int>();

                var result = GetNearbyLocked(center, radiusCells);
                result.Remove(id);
                return result;
            }
        }

        /// <summary>
        /// Same neighborhood query, but anchored on an arbitrary world
        /// position instead of an entity that's already registered in the
        /// grid. Used by spawner activation checks - "is any player near
        /// this spawn point" - where the spawn point itself isn't (and for
        /// dormant spawners, shouldn't be) an entity in the grid.
        /// </summary>
        public List<int> GetNearby(Vector3 position, int radiusCells = 1)
        {
            lock (_lock)
            {
                return GetNearbyLocked(CellOf(position), radiusCells);
            }
        }

        // Caller must already hold _lock.
        private List<int> GetNearbyLocked((int, int) center, int radiusCells)
        {
            var result = new List<int>();

            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                var key = (center.Item1 + dx, center.Item2 + dz);

                if (!_cells.TryGetValue(key, out var set))
                    continue;

                result.AddRange(set);
            }

            return result;
        }
    }
}
