using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W;

public class C2WRequestPlayerLevelHandler: IPacketHandler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly PlayerManager playermanager;
    
    
    public C2WRequestPlayerLevelHandler(PlayerManager playermanager)
    {
        this.playermanager = playermanager;
    }

    public void Handle(NetPeer peer, NetPacketReader reader)
    {
        MessagePackSerializer.Deserialize<C2WRequestPlayerLevelPacket>(reader.GetRemainingBytes());
        
        playermanager.EnqueueAction(() =>
        {
            int level = playermanager.GetLevel(peer);

            if (level == -1)
            {
                Logger.Warn($"[C2WRequestPlayerLevelHandler] No player found for peer {peer.Address}");
                return;
            }
            W2CPlayerLevelResponsePacketSender.Send(peer,level);
            Logger.Info($"[C2WRequestPlayerLevelHandler] {level}");
            
        });
        
    }
}