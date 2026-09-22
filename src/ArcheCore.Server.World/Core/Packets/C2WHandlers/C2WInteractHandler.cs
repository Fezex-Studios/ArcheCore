using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Entities;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W
{
    /// <summary>
    /// The player pressed interact on something. This handler owns the
    /// checks every interaction shares - the target exists, the player is
    /// in range - then routes on IInteractable.Kind:
    ///
    ///   HarvestNode -> HarvestManager.TryBeginHarvest
    ///   Npc         -> ShopManager.TryOpen if it's a merchant,
    ///                  then Lua OnInteract either way (dialogue, quests)
    ///
    /// Harvest nodes deliberately do NOT fire OnInteract: existing scripts
    /// branch on template id alone, and a node template id colliding with
    /// an NPC template id would make a rock say the guard's line. Nodes
    /// fire their own OnHarvest instead.
    ///
    /// Per-step logging is at Debug now that the flow is proven - set NLog
    /// to Debug to see it again.
    /// </summary>
    [PacketOpcode(Opcodes.Interact)]
    public class C2WInteractHandler : IPacketHandler
    {
        private readonly PlayerManager _playerManager;
        private readonly InteractionRegistry _interactions;
        private readonly HarvestManager _harvest;
        private readonly ShopManager _shops;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public C2WInteractHandler(
            PlayerManager playerManager,
            InteractionRegistry interactions,
            HarvestManager harvest,
            ShopManager shops)
        {
            _playerManager = playerManager;
            _interactions = interactions;
            _harvest = harvest;
            _shops = shops;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<C2WInteractPacket>(reader.GetRemainingBytes());

            if (!_playerManager.TryGetNetworkId(peer, out int playerId))
            {
                Logger.Warn($"[Interact] Could not resolve player NetworkId for {peer.Address}");
                return;
            }

            if (!_interactions.TryGet(packet.TargetNetworkId, out var target))
            {
                Logger.Debug($"[Interact] Target {packet.TargetNetworkId} not in InteractionRegistry");
                W2CInteractDeniedPacketSender.Send(peer, "That's no longer there.");
                return;
            }

            if (!_playerManager.TryGetPosition(playerId, out Vector3 playerPos))
            {
                Logger.Warn($"[Interact] No position for player {playerId}");
                return;
            }

            float distance = Vector3.Distance(playerPos, target.Position);
            Logger.Debug($"[Interact] Player {playerId} -> {target.Kind} template {target.TemplateId}, distance {distance:F2}/{target.InteractRange:F2}");

            if (distance > target.InteractRange)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
                return;
            }

            // ── Harvest nodes ──
            if (target.Kind == InteractableKind.HarvestNode)
            {
                if (_harvest.TryGetNode(packet.TargetNetworkId, out var node))
                    _harvest.TryBeginHarvest(peer, playerId, node);
                return;
            }

            // Doing anything else stops a harvest in progress.
            _harvest.CancelFor(playerId, "You stop gathering.");

            // ── NPCs ──
            if (target is NpcEntity npc)
                _shops.TryOpen(peer, npc);

            var luaPlayer = _playerManager.CreateLuaPlayer(peer);
            if (luaPlayer == null)
            {
                Logger.Warn("[Interact] CreateLuaPlayer returned null");
                return;
            }

            _playerManager.FireInteractEvent(luaPlayer, target);
        }
    }
}
