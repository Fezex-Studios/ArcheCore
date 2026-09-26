using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Managers
{
    /// <summary>
    /// Pets. Roadmap O: an NPC that follows its owner instead of wandering.
    ///
    /// A pet IS a normal NPC - same spawn path, same network id range, same
    /// visibility - with one difference: NpcAiManager gives it an AI state
    /// with an OwnerPlayerId, so it follows rather than wanders and never
    /// picks a fight. Nothing new was needed for it to be seen, walked or
    /// despawned; it reuses the split built for orcs.
    ///
    /// One pet per player. Using the item again dismisses it, and it's
    /// dismissed on death and disconnect - a pet with no owner would stand
    /// in a field forever, since no spawner owns it.
    ///
    /// Whether a pet can be attacked is data: its NpcTemplate's MaxHealth,
    /// which is 0 (untouchable) unless a row says otherwise.
    /// </summary>
    public class PetManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly PlayerManager _players;
        private readonly SpawnManager _spawnManager;
        private readonly NpcAiManager _npcAi;
        private readonly InterestManager _interest;

        public PetManager(PlayerManager players, SpawnManager spawnManager, NpcAiManager npcAi, InterestManager interest)
        {
            _players = players;
            _spawnManager = spawnManager;
            _npcAi = npcAi;
            _interest = interest;
        }

        /// <summary>Using a pet item: summon it, or dismiss the one that's out.</summary>
        public bool Toggle(NetPeer peer, PlayerSession session, int npcTemplateId)
        {
            if (session.Mount.PetNetworkId != 0)
            {
                Dismiss(session, "Your companion returns.");
                if (peer != null) W2CInteractDeniedPacketSender.Send(peer, "Your companion returns.");
                return true;
            }

            if (session.Combat.IsDead || session.NetworkId is not int ownerId)
                return false;

            if (!_spawnManager.TryGetTemplate(npcTemplateId, out var template))
            {
                Logger.Warn("[Pets] Item tried to summon unknown NPC template {Id}", npcTemplateId);
                return false;
            }

            var pet = _spawnManager.SpawnStandalone(template, session.Position);
            if (pet == null)
                return false;

            // Into the AI as a follower rather than a wanderer. RegisterPet
            // also puts it in the interest grid and tells everyone standing
            // nearby.
            //
            // Do NOT call _interest.UpdatePosition here first: the broadcast
            // relies on a brand new entity's "entered" list being the people
            // discovering it, and registering it early empties that list -
            // which left the owner (standing still) unable to see their own
            // pet until they moved, while everyone else saw it as soon as
            // THEY moved.
            _npcAi.RegisterPet(pet, ownerId);

            session.Mount.PetNetworkId = pet.NetworkId;

            Logger.Info("[Pets] Account {Account} summoned '{Pet}' ({Id})", session.AccountId, pet.Name, pet.NetworkId);
            return true;
        }

        /// <summary>
        /// Send the pet away. Safe when there isn't one, so death, logout and
        /// re-summon can all just call it.
        /// </summary>
        public void Dismiss(PlayerSession session, string reason = null)
        {
            if (session == null || session.Mount.PetNetworkId == 0)
                return;

            int petId = session.Mount.PetNetworkId;
            session.Mount.PetNetworkId = 0;

            // Out of the AI and out of everyone's view (UnregisterPet does
            // both), then out of the spawner's registry.
            _npcAi.UnregisterPet(petId);
            _spawnManager.DespawnStandalone(petId);
        }

        /// <summary>Called when the owner dies or disconnects.</summary>
        public void DismissFor(int playerNetworkId)
        {
            if (_players.TryGetPeer(playerNetworkId, out var peer) &&
                _players.TryGetSession(peer, out var session))
                Dismiss(session);
        }
    }
}