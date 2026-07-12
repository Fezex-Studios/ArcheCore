using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Wraps SpatialGrid with the bookkeeping PlayerManager actually needs:
    /// "who currently knows about entity X" (for routing position/leave packets),
    /// and "what changed since last update" (for knowing who needs a fresh
    /// spawn packet vs a despawn packet).
    /// </summary>
    public class InterestManager
    {
        private readonly SpatialGrid _grid;

        // networkId -> set of other networkIds it's currently aware of.
        private readonly Dictionary<int, HashSet<int>> _known = new();

        public InterestManager(float cellSize = 50f)
        {
            _grid = new SpatialGrid(cellSize);
        }

        /// <summary>
        /// Call whenever an entity's position changes (including on spawn).
        /// Returns who newly entered and who newly left this entity's awareness -
        /// entered need a spawn packet, left need a despawn/leave packet.
        /// Keeps the relationship symmetric: if B enters A's awareness, A is
        /// also added to B's awareness, and vice versa on exit.
        /// </summary>
        public (List<int> entered, List<int> left) UpdatePosition(int networkId, Vector3 position)
        {
            _grid.Update(networkId, position);

            var nearby = new HashSet<int>(_grid.GetNearby(networkId));

            _known.TryGetValue(networkId, out var previouslyKnown);
            previouslyKnown ??= new HashSet<int>();

            var entered = nearby.Except(previouslyKnown).ToList();
            var left = previouslyKnown.Except(nearby).ToList();

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

        public void Remove(int networkId)
        {
            _grid.Remove(networkId);

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

        /// <summary>Every networkId currently aware of this entity - i.e. who to route position/leave packets to.</summary>
        public IEnumerable<int> GetKnownBy(int networkId)
        {
            return _known.TryGetValue(networkId, out var set)
                ? set
                : Enumerable.Empty<int>();
        }
    }
}