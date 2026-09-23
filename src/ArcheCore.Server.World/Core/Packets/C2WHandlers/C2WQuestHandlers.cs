using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Server.World.Networking.C2W;

/// <summary>Accept a quest. QuestManager re-checks giver, range, level and prerequisite.</summary>
[PacketOpcode(Opcodes.C2WQuestAccept)]
public class C2WQuestAcceptHandler : IPacketHandler
{
    private readonly QuestManager _quests;

    public C2WQuestAcceptHandler(QuestManager quests)
    {
        _quests = quests;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WQuestAcceptPacket>(reader.GetRemainingBytes());
        _quests.TryAccept(peer, packet.NpcNetworkId, packet.QuestId);
    }
}

/// <summary>Hand a quest in. QuestManager re-checks the objectives and takes the items.</summary>
[PacketOpcode(Opcodes.C2WQuestComplete)]
public class C2WQuestCompleteHandler : IPacketHandler
{
    private readonly QuestManager _quests;

    public C2WQuestCompleteHandler(QuestManager quests)
    {
        _quests = quests;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WQuestCompletePacket>(reader.GetRemainingBytes());
        _quests.TryComplete(peer, packet.NpcNetworkId, packet.QuestId);
    }
}

/// <summary>Drop a quest from the log. No NPC needed.</summary>
[PacketOpcode(Opcodes.C2WQuestAbandon)]
public class C2WQuestAbandonHandler : IPacketHandler
{
    private readonly QuestManager _quests;

    public C2WQuestAbandonHandler(QuestManager quests)
    {
        _quests = quests;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        var packet = MessagePackSerializer.Deserialize<C2WQuestAbandonPacket>(reader.GetRemainingBytes());
        _quests.TryAbandon(peer, packet.QuestId);
    }
}
