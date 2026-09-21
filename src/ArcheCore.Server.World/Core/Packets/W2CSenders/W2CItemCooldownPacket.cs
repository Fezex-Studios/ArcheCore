using LiteNetLib;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;

namespace ArcheCore.Server.World.Networking.W2C
{
    /// <summary>See W2CItemCooldownPacket. Sent from PlayerManager.TryUseItem
    /// only after a use has been accepted.</summary>
    public static class W2CItemCooldownPacketSender
    {
        public static void Send(NetPeer peer, int cooldownGroup, int[] itemTemplateIds, int durationMs)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CItemCooldown,
                new W2CItemCooldownPacket
                {
                    CooldownGroup   = cooldownGroup,
                    ItemTemplateIds = itemTemplateIds,
                    DurationMs      = durationMs
                });
        }
    }
}