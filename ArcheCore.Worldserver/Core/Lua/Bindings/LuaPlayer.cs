using ArcheCore.WorldServer.Managers;
using ArcheCore.WorldServer.Networking.W2C;
using LiteNetLib;
using MoonSharp.Interpreter;

namespace ArcheCore.WorldServer.Lua.Scripting.Bindings
{
    [MoonSharpUserData]
    public class LuaPlayer
    {
        private readonly NetPeer            peer;
        private readonly ReplicationManager _replication;

        public int NetworkId { get; }
        public int AccountId { get; }

        public LuaPlayer(
            NetPeer peer,
            int networkId,
            int accountId,
            ReplicationManager replication)
        {
            this.peer    = peer;
            NetworkId    = networkId;
            AccountId    = accountId;
            _replication = replication;
        }

        public void SendAnnouncementMessage(string message)
        {
            W2CAnnouncementPacketSender.Send(_replication, new[] { peer }, message);
        }

        public void Kick(string reason)
        {
            peer.Disconnect();
        }
    }
}