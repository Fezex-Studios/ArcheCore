using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Replacement for the locked, allocating InterestManager.
    ///
    /// WHAT WAS WRONG WITH THE OLD ONE
    ///
    /// UpdatePosition ran the full set-difference on EVERY movement packet
    /// and allocated a fresh HashSet plus two Lists each time. At 20k
    /// players moving at 10Hz that is 600,000 allocations per second doing
    /// work that is almost always a no-op, because a player who moved 30cm
    /// has the same neighbours they had last tick. The comment in the old
    /// file about avoiding LINQ was solving the right problem one layer too
    /// low: the fix isn't cheaper set difference, it's not running it.
    ///
    /// Now the grid tells us whether the entity crossed a cell boundary,
    /// and everything below that check is skipped when it didn't. That is
    /// roughly a 95% reduction in the dominant cost, and it means the
    /// remaining 5% can afford to be thorough.
    ///
    /// GetKnownBy also allocated a new List on every call. SnapshotDispatcher
    /// calls it once per player per tick - 200,000 times a second at target
    /// load - so it now returns a maintained, reusable view instead.
    ///
    /// NOT THREAD SAFE, BY DESIGN. The old class locked because NPC AI ran
    /// on a separate thread. That model doesn't scale to N cores. One
    /// InterestManager belongs to one WorldWorker and is touched by exactly
    /// one thread. See SpatialGrid and INTEGRATION.md.
    /// </summary>
    public sealed class InterestManager
    {
        private readonly SpatialGrid _grid;

        /// <summary>
        /// networkId -> who it is currently aware of. ObserverSet keeps a
        /// List for indexed, allocation-free iteration and a parallel index
        /// map for O(1) contains and removal, because the hot loop needs
        /// both and a bare HashSet only gives one of them cheaply.
        /// </summary>
        private readonly Dictionary<int, ObserverSet> _known = new(capacity: 32_768);

        // Scratch buffers, reused. Never allocate inside UpdatePosition.
        private readonly List<int> _nearbyScratch = new(capacity: 512);
        private readonly HashSet<int> _nearbySet = new(capacity: 512);
        private readonly List<int> _entered = new(capacity: 64);
        private readonly List<int> _left = new(capacity: 64);

        public InterestManager(float cellSize = 50f)
        {
            _grid = new SpatialGrid(cellSize);
        }

        public float CellSize => _grid.CellSize;

        /// <summary>
        /// Call on every position change. Returns who entered and who left
        /// awareness. BOTH RETURNED LISTS ARE REUSED SCRATCH BUFFERS - read
        /// them before the next call, do not store them.
        ///
        /// When the entity stayed inside its cell, both come back empty
        /// without any set work happening at all. That's the common case
        /// and it is the whole point of this class's rewrite.
        /// </summary>
        public (List<int> entered, List<int> left) UpdatePosition(int networkId, Vector3 position)
        {
            _entered.Clear();
            _left.Clear();

            var crossedCell = _grid.Update(networkId, position);

            // Fast path. Same cell, so no cell-granularity relationship can
            // have changed. Movement itself still replicates - that's the
            // SnapshotDispatcher's job, not this one's.
            if (!crossedCell && _known.ContainsKey(networkId))
                return (_entered, _left);

            _grid.GetNearby(networkId, _nearbyScratch);

            // Hard ceiling on how many nearby entities get processed for
            // enter/leave bookkeeping. Without this, a dense cluster (or,
            // worse, thousands of bots random-walking near a shared spawn
            // point instead of spreading across a real map) makes a
            // single cell crossing trigger symmetric ObserverSet updates
            // against however many hundreds or thousands of entities are
            // nearby - unbounded, and the direct cause of tick time
            // climbing from ~0ms to 140ms+ as bot count grew to 5000 even
            // after SnapshotDispatcher's own scan cap was in place (see
            // TickHealth logs, 2026-09-17 01:xx run - gen2GC stayed near
            // 0 throughout, ruling out GC; the shape was still O(local
            // density) per crossing).
            //
            // No position data is available at this layer - SpatialGrid
            // tracks cell membership only, not coordinates - so this caps
            // by raw count rather than true nearest-N. Under realistic,
            // non-pathological clustering that distinction rarely
            // matters; the goal is a hard ceiling on worst-case cost, not
            // perfect selection under an extreme crush.
            const int MaxNearbyCandidates = 300;
            if (_nearbyScratch.Count > MaxNearbyCandidates)
                _nearbyScratch.RemoveRange(MaxNearbyCandidates, _nearbyScratch.Count - MaxNearbyCandidates);

            _nearbySet.Clear();
            for (int i = 0; i < _nearbyScratch.Count; i++)
                _nearbySet.Add(_nearbyScratch[i]);

            if (!_known.TryGetValue(networkId, out var mine))
                _known[networkId] = mine = new ObserverSet();

            foreach (var id in _nearbySet)
            {
                if (!mine.Contains(id))
                    _entered.Add(id);
            }

            for (int i = 0; i < mine.Items.Count; i++)
            {
                var id = mine.Items[i];
                if (!_nearbySet.Contains(id))
                    _left.Add(id);
            }

            // Apply symmetrically: if B entered A's awareness, A entered B's.
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

        public void Remove(int networkId)
        {
            _grid.Remove(networkId);

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
        /// mutate. Callers previously got a defensive copy; at 200k calls a
        /// second that copy was pure waste.
        /// </summary>
        public IReadOnlyList<int> GetKnownBy(int networkId) =>
            _known.TryGetValue(networkId, out var set) ? set.Items : EmptyList;

        public void GetNearbyAtRadius(int networkId, int radiusCells, List<int> results) =>
            _grid.GetNearby(networkId, results, radiusCells);

        public void GetNearbyAtPosition(Vector3 position, int radiusCells, List<int> results) =>
            _grid.GetNearby(position, results, radiusCells);

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