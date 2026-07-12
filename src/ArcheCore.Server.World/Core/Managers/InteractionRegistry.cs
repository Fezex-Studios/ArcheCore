using System.Collections.Generic;
using ArcheCore.Server.World.Core.Interaction;

namespace ArcheCore.Server.World.Managers
{
    // Thin lookup table, same shape as your other managers. Entities
    // register themselves here when spawned (see NpcSpawner) and
    // unregister when despawned. C2WInteractHandler is the only reader.
    public class InteractionRegistry
    {
        private readonly Dictionary<int, IInteractable> _byNetworkId = new();

        public void Register(int networkId, IInteractable interactable)
        {
            _byNetworkId[networkId] = interactable;
        }

        public void Unregister(int networkId)
        {
            _byNetworkId.Remove(networkId);
        }

        public bool TryGet(int networkId, out IInteractable interactable)
        {
            return _byNetworkId.TryGetValue(networkId, out interactable);
        }
    }
}