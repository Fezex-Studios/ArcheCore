using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;

namespace ArcheCore.Server.World.Managers;

public class DemoManager
{
    public void OnPlayerJoin(NetPeer peer)
    {
        W2CTestPacketSender.Send(peer,"TESTPACKETSENDER FROM DEMOMANAGER");
    }
}