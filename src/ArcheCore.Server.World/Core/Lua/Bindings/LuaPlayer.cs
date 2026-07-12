using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MoonSharp.Interpreter;

namespace ArcheCore.Server.World.Lua.Scripting.Bindings
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

        // --- Interaction system ---

        // Called from an OnInteract Lua hook, e.g.:
        //   player:SendDialogue("Guard", "Halt! State your business.")
        public void SendDialogue(string speakerName, string text)
        {
            W2CInteractDialoguePacketSender.Send(peer, speakerName, text);
        }

        // Placeholder until a real inventory system exists - this only
        // sends a client-side message, it does not grant an item.
        //   player:SendLootMessage("Rusty Sword")
        public void SendLootMessage(string itemName)
        {
            W2CInteractLootPacketSender.Send(peer, itemName);
        }
    }
}