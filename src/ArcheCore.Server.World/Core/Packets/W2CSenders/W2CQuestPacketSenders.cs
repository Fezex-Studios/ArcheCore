using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CQuestCatalogPacketSender
    {
        public static void Send(NetPeer peer, QuestDefinitionData[] quests) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CQuestCatalog, new W2CQuestCatalogPacket { Quests = quests });
    }

    public static class W2CQuestLogPacketSender
    {
        public static void Send(NetPeer peer, QuestProgressData[] quests) =>
            WorldserverPacketSender.SendPacket(peer, Opcodes.W2CQuestLog, new W2CQuestLogPacket { Quests = quests });
    }

    public static class W2CQuestUpdatePacketSender
    {
        /// <summary>message null = a silent progress tick (the log updates, nothing is said).</summary>
        public static void Send(NetPeer peer, QuestProgressData quest, string message, bool removed) =>
            WorldserverPacketSender.SendPacket(
                peer, Opcodes.W2CQuestUpdate,
                new W2CQuestUpdatePacket { Quest = quest, Message = message ?? string.Empty, Removed = removed });
    }

    public static class W2CQuestOffersPacketSender
    {
        public static void Send(NetPeer peer, int npcNetworkId, string npcName, int[] available, int[] completable, int[] inProgress) =>
            WorldserverPacketSender.SendPacket(
                peer, Opcodes.W2CQuestOffers,
                new W2CQuestOffersPacket
                {
                    NpcNetworkId = npcNetworkId,
                    NpcName      = npcName,
                    Available    = available,
                    Completable  = completable,
                    InProgress   = inProgress
                });
    }
}
