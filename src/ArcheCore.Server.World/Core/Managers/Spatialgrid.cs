using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Spatial index for interest management.
    ///
    /// THE GRID IS A BROAD PHASE, NOT THE ANSWER.
    ///
    /// It used to be both, and that was the bug. Interest was decided by
    /// cell membership alone: you saw the 3x3 block of cells around the
    /// one you were standing in, and that block is anchored to the GRID,
    /// not to you. At a 50-unit cell size that meant view distance swung
    /// between 25 and 125 units depending on where inside the cell you
    /// happened to stand, and it snapped to a whole new box every time you
    /// crossed a boundary.
    ///
    /// Now the grid's only job is to cheaply answer "what MIGHT be close"
    /// (broad phase). InterestManager filters that candidate list by real
    /// distance (narrow phase), so visibility is a circle centred on the
    /// player and identical everywhere.
    ///
    /// For that to work the grid has to store positions, not just cell
    /// membership. It gets them for free - Update already received the
    /// position, it was just throwing it away after computing the cell.
    ///
    /// COVERAGE RULE. A query for radius R must scan a ring of
    /// ceil(R / cellSize) cells, or the square of cells won't contain the
    /// circle and entities genuinely within R get missed near the corners.
    /// RingFor does that calculation; never hardcode a ring radius.
    ///
    /// NOT THREAD SAFE. One grid belongs to one thread. NPC AI runs inside
    /// the main tick rather than on its own thread for exactly this reason.
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly float _invCellSize;

        private readonly Dictionary<(int, int), List<int>> _cells = new();
        private readonly Dictionary<int, (int, int)> _entityCell = new();

        /// <summary>
        /// Last known position of every entity in the grid. This is what
        /// makes the narrow phase possible - without it the only thing this
        /// class could report was "same cell-ish", which is where the
        /// variable view distance came from.
        /// </summary>
        private readonly Dictionary<int, Vector3> _entityPosition = new(capacity: 32_768);

        public SpatialGrid(float cellSize = 50f)
        {
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
        }

        public float CellSize => _cellSize;

        /// <summary>
        /// How many cells out a query must reach to fully contain a circle
        /// of the given radius. Always at least 1.
        /// </summary>
        public int RingFor(float radius) =>
            Math.Max(1, (int)MathF.Ceiling(radius * _invCellSize));

        public (int, int) CellOf(Vector3 position) => (
            (int)MathF.Floor(position.X * _invCellSize),
            (int)MathF.Floor(position.Z * _invCellSize));

        public bool TryGetPosition(int id, out Vector3 position) =>
            _entityPosition.TryGetValue(id, out position);

        /// <summary>
        /// Moves an entity. Returns TRUE only if it changed cells.
        ///
        /// The position is recorded either way. A same-cell move still
        /// matters now - two entities can drift apart inside one cell far
        /// enough to leave each other's radius - so the caller can no
        /// longer treat a false return as "nothing can have changed". See
        /// InterestManager's movement threshold.
        /// </summary>
        public bool Update(int id, Vector3 position)
        {
            _entityPosition[id] = position;

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
            _entityPosition.Remove(id);

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

        // ── Broad phase only: cell membership, no distance check ─────────
        //
        // Still the right tool for coarse proximity questions where an
        // exact boundary doesn't matter - spawner activation ("is any
        // player roughly near this spawner?") and chat shout range are
        // both fine at cell granularity. NOT the right tool for deciding
        // what a client renders; that's what the variable view distance
        // bug was.

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

        // ── Broad phase + narrow phase ───────────────────────────────────

        /// <summary>
        /// Everything within <paramref name="radius"/> world units of
        /// <paramref name="origin"/>, excluding <paramref name="excludeId"/>.
        /// The ring is sized from the radius, so the scanned square always
        /// contains the circle.
        ///
        /// Roughly 35% of gathered candidates survive the distance test
        /// when cellSize is close to radius. That's the normal ratio for a
        /// square-covers-circle broad phase, not waste. A smaller cellSize
        /// improves the ratio at the cost of more dictionary lookups per
        /// query.
        /// </summary>
        public void GetWithinRadius(
            Vector3 origin,
            float radius,
            List<int> results,
            int excludeId = -1)
        {
            results.Clear();

            var radiusSq = radius * radius;
            var ring     = RingFor(radius);
            var center   = CellOf(origin);

            for (int dx = -ring; dx <= ring; dx++)
            for (int dz = -ring; dz <= ring; dz++)
            {
                if (!_cells.TryGetValue((center.Item1 + dx, center.Item2 + dz), out var list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var id = list[i];

                    if (id == excludeId)
                        continue;

                    if (!_entityPosition.TryGetValue(id, out var position))
                        continue;

                    if (Vector3.DistanceSquared(origin, position) <= radiusSq)
                        results.Add(id);
                }
            }
        }

        /// <summary>
        /// True when two entities are within the given distance of each
        /// other. The despawn half of the hysteresis check asks about one
        /// specific pair rather than a whole neighbourhood, so it uses this
        /// instead of a second radius query.
        /// </summary>
        public bool WithinRadius(int a, int b, float radius)
        {
            if (!_entityPosition.TryGetValue(a, out var pa)) return false;
            if (!_entityPosition.TryGetValue(b, out var pb)) return false;

            return Vector3.DistanceSquared(pa, pb) <= radius * radius;
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