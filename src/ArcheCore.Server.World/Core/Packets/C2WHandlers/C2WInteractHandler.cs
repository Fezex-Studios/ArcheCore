using System.Numerics;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Core.Interaction;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W
{
    public class C2WInteractHandler : IPacketHandler
    {
        private readonly PlayerManager playerManager;
        private readonly InteractionRegistry interactions;

        public C2WInteractHandler(
            PlayerManager playerManager,
            InteractionRegistry interactions)
        {
            this.playerManager = playerManager;
            this.interactions = interactions;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            C2WInteractPacket packet =
                MessagePackSerializer
                    .Deserialize<C2WInteractPacket>(
                        reader.GetRemainingBytes());

            if (!playerManager.TryGetNetworkId(peer, out int playerId))
                return;

            // Target may have despawned/been looted between the client's
            // raycast and this packet arriving - that's normal, not an error.
            if (!interactions.TryGet(packet.TargetNetworkId, out var target))
            {
                W2CInteractDeniedPacketSender.Send(peer, "That's no longer there.");
                return;
            }

            if (!playerManager.TryGetPosition(playerId, out Vector3 playerPos))
                return;

            float distance = Vector3.Distance(playerPos, target.Position);

            if (distance > target.InteractRange)
            {
                W2CInteractDeniedPacketSender.Send(peer, "Too far away.");
                return;
            }
            
            var luaPlayer = playerManager.CreateLuaPlayer(peer);

            if (luaPlayer == null)
                return;

            // What actually happens (dialogue, loot, quest turn-in) is
            // entirely up to whatever Lua script registered OnInteract -
            // this handler doesn't know or care which one.
            playerManager.FireInteractEvent(luaPlayer, target);
        }
    }
}