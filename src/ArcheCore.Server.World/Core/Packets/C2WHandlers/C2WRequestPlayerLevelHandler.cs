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
    
    
    public C2WRequestPlayerLevelHandler(PlayerManager playerManager)
    {
        this.playerManager = playerManager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WRequestPlayerLevelPacket>(
            reader.GetRemainingBytes());

        int level = playerManager.GetLevel(peer);
        if (level == -1) return;

        // No need to go to persistence — level is already in memory
        playerManager.EnqueueAction(() =>
            W2CPlayerLevelResponsePacketSender.Send(peer, level));
    }
    
    
}