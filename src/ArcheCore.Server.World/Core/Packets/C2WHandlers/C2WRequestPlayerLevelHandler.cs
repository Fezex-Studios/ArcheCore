using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W;

public class C2WRequestPlayerLevelHandler: IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager playerManager;
    private readonly PersistenceClient persistence;
    
    
    public C2WRequestPlayerLevelHandler(PlayerManager playerManager, PersistenceClient persistence)
    {
        this.playerManager = playerManager;
        this.persistence = persistence;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WRequestPlayerLevelPacket>(reader.GetRemainingBytes());

        long characterId = playerManager.GetCharacterId(peer); // add this getter, mirrors GetLevel
        if (characterId == -1) return;

        _ = FetchAndReply(peer, characterId);
    }

    private async Task FetchAndReply(NetPeer peer, long characterId)
    {
        var character = await persistence.W2PCharacter.Load(characterId);

        playerManager.EnqueueAction(() =>
            W2CPlayerLevelResponsePacketSender.Send(peer, character.Level));
    }
    
}