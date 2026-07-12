using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    // NOTE: This is intentionally a message-only placeholder. There is no
    // inventory system yet, so the server can't actually hand the player
    // an item instance. This just lets Lua scripts say "you found X" and
    // gives the client something real to hook up once inventory exists.
    [MessagePackObject(true)]
    public class W2CInteractLootPacket
    {
        public string ItemName;
    }
}