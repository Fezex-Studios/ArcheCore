using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Decides who is aware of whom.
    ///
    /// WHAT CHANGED: INTEREST IS A CIRCLE NOW, NOT A BOX
    ///
    /// Interest used to be pure cell membership - you were aware of
    /// everything in the 3x3 block of cells around your own. That block is
    /// anchored to the GRID, not to you, so your view distance depended on
    /// where inside a cell you were standing: 75 units in every direction
    /// from the middle, but 25 one way and 125 the other from a corner.
    /// Players saw each other at wildly different ranges depending on
    /// nothing they could perceive, and everything re-evaluated in a jump
    /// whenever someone crossed a boundary.
    ///
    /// The grid is now a broad phase only. It cheaply answers "what might
    /// be close", and this class filters that by real distance. Visibility
    /// is a circle of SpawnRadius centred on the entity, identical
    /// everywhere on the map.
    ///
    /// WHY TWO RADII
    ///
    /// Cell granularity was accidentally providing hysteresis: you had to
    /// cross an entire boundary before anything changed. An exact distance
    /// test removes that, and a player standing at exactly the edge would
    /// spawn and despawn on alternating ticks - a strobing entity on every
    /// nearby client and a reliable spawn/despawn packet every tick for
    /// each of them.
    ///
    /// So awareness STARTS at SpawnRadius and ENDS at DespawnRadius, and
    /// the gap between them is the dead band. Something already known stays
    /// known until it is properly gone.
    ///
    /// WHY A MOVEMENT THRESHOLD AND NOT JUST CELL CROSSINGS
    ///
    /// The old fast path skipped everything when an entity stayed in its
    /// cell, which was exactly correct when interest WAS cell membership:
    /// no cell change meant no possible relationship change. With distance
    /// that reasoning no longer holds - two entities can drift apart inside
    /// one 50-unit cell by enough to leave each other's radius.
    ///
    /// Recompute is therefore triggered by EITHER a cell crossing OR having
    /// moved RecomputeThreshold units since the last recompute. The
    /// threshold is half the hysteresis gap on purpose: two entities each
    /// drifting just under the threshold can close or open the distance
    /// between them by just under the full gap, which is exactly the band
    /// the dead zone absorbs. Widen the gap if you widen the threshold, or
    /// entities can slip through the band unnoticed.
    ///
    /// ONLY PLAYERS OBSERVE (audit M1)
    ///
    /// NPCs, harvest nodes and corpses are OBSERVED, never observers. An
    /// awareness pair is only ever recorded when at least one side is a
    /// player: two mobs, or a mob and an ore node, never track each other.
    /// That used to be O(n^2) per dense area (a camp of 30 mobs beside 50
    /// nodes = 80 ids per mob, rebuilt as they wandered) for sets every
    /// consumer then skipped with IsNpcId. A non-player's narrow phase now
    /// queries a second, player-only grid, so its cost scales with the
    /// players near it - usually zero.
    ///
    /// NOT THREAD SAFE, BY DESIGN. One InterestManager belongs to one
    /// thread. NPC AI runs inside the main tick for this reason.
    /// </summary>
    public sealed class InterestManager
    {
        private readonly SpatialGrid _grid;

        /// <summary>Players only - what a non-player's narrow phase searches.</summary>
        private readonly SpatialGrid _playerGrid;

        /// <summary>True for ids that observe (players). Everything else is only observed.</summary>
        private readonly System.Func<int, bool> _isObserver;

        // ── Tuning ───────────────────────────────────────────────────────

        /// <summary>
        /// How close something must come to enter awareness. This is the
        /// real view distance and the number to hand the client so its
        /// culling matches the server's.
        /// </summary>
        public float SpawnRadius { get; }

        /// <summary>
        /// How far something must get to leave awareness. Must be larger
        /// than SpawnRadius; the difference is the dead band that stops
        /// edge flicker.
        /// </summary>
        public float DespawnRadius { get; }

        /// <summary>
        /// How far an entity may move before its interest set is
        /// recomputed even without crossing a cell.
        /// </summary>
        public float RecomputeThreshold { get; }

        /// <summary>
        /// networkId -> who it is currently aware of. ObserverSet keeps a
        /// List for indexed, allocation-free iteration plus a parallel
        /// index map for O(1) contains and removal, because the hot loop
        /// needs both and a bare HashSet only gives one cheaply.
        /// </summary>
        private readonly Dictionary<int, ObserverSet> _known = new(capacity: 32_768);

        /// <summary>
        /// Where each entity was when its interest set was last recomputed.
        /// Drives the movement threshold above.
        /// </summary>
        private readonly Dictionary<int, Vector3> _lastRecomputeAt = new(capacity: 32_768);

        // Scratch buffers, reused. Never allocate inside UpdatePosition.
        private readonly List<int> _nearbyScratch = new(capacity: 512);
        private readonly HashSet<int> _nearbySet = new(capacity: 512);
        private readonly List<int> _entered = new(capacity: 64);
        private readonly List<int> _left = new(capacity: 64);

        /// <param name="cellSize">
        /// Broad-phase granularity only - it no longer has anything to do
        /// with view distance. Closest to optimal is roughly DespawnRadius:
        /// the ring stays at 1 and the square is the tightest square that
        /// can contain the circle. Smaller cells mean more dictionary
        /// lookups but fewer wasted distance checks.
        /// </param>
        public InterestManager(
            float cellSize = 50f,
            float spawnRadius = 75f,
            float despawnRadius = 85f,
            float recomputeThreshold = 5f,
            System.Func<int, bool> isObserver = null)
        {
            _grid = new SpatialGrid(cellSize);
            _playerGrid = new SpatialGrid(cellSize);
            _isObserver = isObserver ?? (id => !SpawnManager.IsNpcId(id));

            SpawnRadius        = spawnRadius;
            DespawnRadius      = despawnRadius;
            RecomputeThreshold = recomputeThreshold;
        }

        public float CellSize => _grid.CellSize;

        /// <summary>
        /// Call on every position change. Returns who entered and who left
        /// awareness. BOTH RETURNED LISTS ARE REUSED SCRATCH BUFFERS - read
        /// them before the next call, never store them.
        /// </summary>
        public (List<int> entered, List<int> left) UpdatePosition(int networkId, Vector3 position)
        {
            _entered.Clear();
            _left.Clear();

            bool observer = _isObserver(networkId);

            var crossedCell = _grid.Update(networkId, position);
            if (observer)
                _playerGrid.Update(networkId, position);

            var isKnown     = _known.ContainsKey(networkId);

            // Fast path. Still in the same cell AND hasn't drifted far
            // enough for any relationship to have crossed the dead band.
            // This is the common case and it's why the rest can afford to
            // be thorough.
            if (!crossedCell && isKnown && !MovedEnough(networkId, position))
                return (_entered, _left);

            _lastRecomputeAt[networkId] = position;

            // ── Narrow phase ────────────────────────────────────────────
            // Everything genuinely within SpawnRadius. Note this is a
            // circle of world units, not a square of cells.
            // A player sees everything; anything else only needs to know
            // which PLAYERS can see it (see class doc, "only players observe").
            (observer ? _grid : _playerGrid)
                .GetWithinRadius(position, SpawnRadius, _nearbyScratch, excludeId: networkId);

            _nearbySet.Clear();
            for (int i = 0; i < _nearbyScratch.Count; i++)
                _nearbySet.Add(_nearbyScratch[i]);

            if (!_known.TryGetValue(networkId, out var mine))
                _known[networkId] = mine = new ObserverSet();

            // Entering: inside SpawnRadius and not already known.
            foreach (var id in _nearbySet)
            {
                if (!mine.Contains(id))
                    _entered.Add(id);
            }

            // Leaving: known, but now beyond DespawnRadius. The asymmetry
            // with the check above IS the dead band - something between the
            // two radii is in neither list and simply stays as it was.
            for (int i = 0; i < mine.Items.Count; i++)
            {
                var id = mine.Items[i];

                if (_nearbySet.Contains(id))
                    continue;

                if (!_grid.WithinRadius(networkId, id, DespawnRadius))
                    _left.Add(id);
            }

            // Apply symmetrically: if B entered A's awareness, A entered
            // B's. Distance is symmetric, so this stays consistent even
            // though the two entities recompute at different moments.
            for (int i = 0; i < _entered.Count; i++)
            {
                var other = _entered[i];
                mine.Add(other);

                if (!_known.TryGetValue(other, out var theirs))
                    _known[other] = theirs = new ObserverSet();

                theirs.Add(networkId);
            }

            for (int i = 0; i < _left.Count; i++)
            {
                var other = _left[i];
                mine.Remove(other);

                if (_known.TryGetValue(other, out var theirs))
                    theirs.Remove(networkId);
            }

            return (_entered, _left);
        }

        private bool MovedEnough(int networkId, Vector3 position)
        {
            if (!_lastRecomputeAt.TryGetValue(networkId, out var last))
                return true;

            return Vector3.DistanceSquared(position, last)
                   > RecomputeThreshold * RecomputeThreshold;
        }

        public void Remove(int networkId)
        {
            _grid.Remove(networkId);
            _playerGrid.Remove(networkId);
            _lastRecomputeAt.Remove(networkId);

            if (_known.TryGetValue(networkId, out var mine))
            {
                for (int i = 0; i < mine.Items.Count; i++)
                {
                    if (_known.TryGetValue(mine.Items[i], out var theirs))
                        theirs.Remove(networkId);
                }
            }

            _known.Remove(networkId);
        }

        private static readonly List<int> EmptyList = new();

        /// <summary>
        /// Who this entity is aware of. Returns a LIVE, REUSED view - safe
        /// to read within the tick, never safe to cache across ticks or
        /// mutate.
        /// </summary>
        public IReadOnlyList<int> GetKnownBy(int networkId) =>
            _known.TryGetValue(networkId, out var set) ? set.Items : EmptyList;

        public bool TryGetPosition(int networkId, out Vector3 position) =>
            _grid.TryGetPosition(networkId, out position);

        // ── Coarse proximity helpers ─────────────────────────────────────
        //
        // Cell-granularity, no distance filter. Deliberately left as they
        // were: spawner activation and chat shout range don't need an exact
        // boundary, and making them exact would cost distance checks for no
        // perceptible gain. If you ever want an exact one, use
        // GetWithinRadiusOf below instead of widening these.

        public void GetNearbyAtRadius(int networkId, int radiusCells, List<int> results) =>
            _grid.GetNearby(networkId, results, radiusCells);

        public void GetNearbyAtPosition(Vector3 position, int radiusCells, List<int> results) =>
            _grid.GetNearby(position, results, radiusCells);

        /// <summary>
        /// Exact-distance neighbourhood query for callers that want one -
        /// an AoE spell, a proximity trigger, a "who heard that" check that
        /// should not depend on grid alignment.
        /// </summary>
        public void GetWithinRadiusOf(
            Vector3 origin, float radius, List<int> results, int excludeId = -1) =>
            _grid.GetWithinRadius(origin, radius, results, excludeId);

        /// <summary>
        /// List plus index map. Indexed iteration for the replication loop,
        /// O(1) contains and swap-back removal for the interest loop.
        /// Removal does not preserve order, which nothing here depends on.
        /// </summary>
        private sealed class ObserverSet
        {
            public readonly List<int> Items = new(capacity: 32);
            private readonly Dictionary<int, int> _index = new(capacity: 32);

            public bool Contains(int id) => _index.ContainsKey(id);

            public void Add(int id)
            {
                if (_index.ContainsKey(id)) return;
                _index[id] = Items.Count;
                Items.Add(id);
            }

            public void Remove(int id)
            {
                if (!_index.TryGetValue(id, out var i)) return;

                var last = Items.Count - 1;
                var moved = Items[last];

                Items[i] = moved;
                Items.RemoveAt(last);
                _index.Remove(id);

                if (moved != id)
                    _index[moved] = i;
            }
        }
    }
}