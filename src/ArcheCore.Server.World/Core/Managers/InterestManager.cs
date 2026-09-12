using System.Collections.Generic;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Wraps SpatialGrid with the bookkeeping PlayerManager actually needs:
    /// "who currently knows about entity X" (for routing position/leave packets),
    /// and "what changed since last update" (for knowing who needs a fresh
    /// spawn packet vs a despawn packet).
    ///
    /// Shared by both players and NPCs (see SpawnManager.NpcIdBase for how
    /// they avoid id collisions in the same grid) and thread-safe, since
    /// NPC AI now updates NPC positions from a background thread
    /// (NpcAiManager) concurrently with the main thread updating player
    /// positions.
    /// </summary>
    public class InterestManager
    {
        private readonly SpatialGrid _grid;
        private readonly object _lock = new();

        // networkId -> set of other networkIds it's currently aware of.
        private readonly Dictionary<int, HashSet<int>> _known = new();

        public InterestManager(float cellSize = 50f)
        {
            _grid = new SpatialGrid(cellSize);
        }

        public float CellSize => _grid.CellSize;

        /// <summary>
        /// Call whenever an entity's position changes (including on spawn).
        /// Returns who newly entered and who newly left this entity's awareness -
        /// entered need a spawn packet, left need a despawn/leave packet.
        /// Keeps the relationship symmetric: if B enters A's awareness, A is
        /// also added to B's awareness, and vice versa on exit.
        ///
        /// Manual set-difference instead of LINQ's .Except().ToList() - this
        /// runs once per player movement tick AND once per active NPC every
        /// 300ms, so at scale the allocations from two LINQ enumerator
        /// chains per call add up to real GC pressure. Same result, just
        /// without the iterator/enumerable overhead.
        /// </summary>
        public (List<int> entered, List<int> left) UpdatePosition(int networkId, Vector3 position)
        {
            _grid.Update(networkId, position);

            lock (_lock)
            {
                var nearby = new HashSet<int>(_grid.GetNearby(networkId));

                _known.TryGetValue(networkId, out var previouslyKnown);
                previouslyKnown ??= new HashSet<int>();

                var entered = new List<int>();
                var left = new List<int>();

                foreach (var id in nearby)
                {
                    if (!previouslyKnown.Contains(id))
                        entered.Add(id);
                }

                foreach (var id in previouslyKnown)
                {
                    if (!nearby.Contains(id))
                        left.Add(id);
                }

                _known[networkId] = nearby;

                foreach (var other in entered)
                {
                    if (!_known.TryGetValue(other, out var otherSet))
                        _known[other] = otherSet = new HashSet<int>();

                    otherSet.Add(networkId);
                }

                foreach (var other in left)
                {
                    if (_known.TryGetValue(other, out var otherSet))
                        otherSet.Remove(networkId);
                }

                return (entered, left);
            }
        }

        public void Remove(int networkId)
        {
            _grid.Remove(networkId);

            lock (_lock)
            {
                if (_known.TryGetValue(networkId, out var aware))
                {
                    foreach (var other in aware)
                    {
                        if (_known.TryGetValue(other, out var otherSet))
                            otherSet.Remove(networkId);
                    }
                }

                _known.Remove(networkId);
            }
        }

        /// <summary>Every networkId currently aware of this entity - i.e. who to route position/leave packets to.</summary>
        public List<int> GetKnownBy(int networkId)
        {
            lock (_lock)
            {
                return _known.TryGetValue(networkId, out var set)
                    ? new List<int>(set)
                    : new List<int>();
            }
        }

        public List<int> GetNearbyAtRadius(int networkId, int radiusCells)
        {
            return _grid.GetNearby(networkId, radiusCells);
        }

        /// <summary>
        /// Who's registered near an arbitrary world position - not an
        /// existing entity's position. Used by spawner activation, where
        /// the question is "is a player near this spawn point" and the
        /// spawn point itself has no networkId of its own.
        /// </summary>
        public List<int> GetNearbyAtPosition(Vector3 position, int radiusCells)
        {
            return _grid.GetNearby(position, radiusCells);
        }
    }
}