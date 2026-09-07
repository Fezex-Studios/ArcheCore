using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.Interact)]
    public class C2WInteractHandler : IPacketHandler
    {
        private readonly PlayerManager playerManager;
        private readonly InteractionRegistry interactions;

        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();

        public C2WInteractHandler(
            PlayerManager playerManager,
            InteractionRegistry interactions)
        {
            this.playerManager = playerManager;
            this.interactions = interactions;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            Logger.Info($"[Interact] C2WInteractHandler RECEIVED packet from {peer.Address}");

            C2WInteractPacket packet =
                MessagePackSerializer
                    .Deserialize<C2WInteractPacket>(
                        reader.GetRemainingBytes());

            Logger.Info(
                $"[Interact] TargetNetworkId={packet.TargetNetworkId}");

            if (!playerManager.TryGetNetworkId(peer, out int playerId))
            {
                Logger.Warn(
                    $"[Interact] FAILED: Could not resolve player NetworkId for {peer.Address}");
                return;
            }

            Logger.Info(
                $"[Interact] PlayerNetworkId={playerId}");

            if (!interactions.TryGet(packet.TargetNetworkId, out var target))
            {
                Logger.Warn(
                    $"[Interact] FAILED: NetworkId {packet.TargetNetworkId} not found in InteractionRegistry");

                W2CInteractDeniedPacketSender.Send(
                    peer,
                    "That's no longer there.");

                return;
            }

            Logger.Info(
                $"[Interact] Target found: TemplateId={target.TemplateId}, " +
                $"Kind={target.Kind}, " +
                $"Position={target.Position}, " +
                $"Range={target.InteractRange}");

            if (!playerManager.TryGetPosition(playerId, out Vector3 playerPos))
            {
                Logger.Warn(
                    $"[Interact] FAILED: Could not get player position for NetworkId={playerId}");
                return;
            }

            Logger.Info(
                $"[Interact] Player position={playerPos}");

            float distance = Vector3.Distance(playerPos, target.Position);

            Logger.Info(
                $"[Interact] Distance={distance:F2}, Allowed={target.InteractRange:F2}");

            if (distance > target.InteractRange)
            {
                Logger.Warn(
                    $"[Interact] DENIED: Too far away");

                W2CInteractDeniedPacketSender.Send(
                    peer,
                    "Too far away.");

                return;
            }

            var luaPlayer = playerManager.CreateLuaPlayer(peer);

            if (luaPlayer == null)
            {
                Logger.Warn(
                    $"[Interact] FAILED: CreateLuaPlayer returned null");
                return;
            }

            Logger.Info(
                $"[Interact] FIRING OnInteract: TemplateId={target.TemplateId}, Kind={target.Kind}");

            playerManager.FireInteractEvent(luaPlayer, target);

            Logger.Info(
                $"[Interact] OnInteract finished");

        }
    }
}