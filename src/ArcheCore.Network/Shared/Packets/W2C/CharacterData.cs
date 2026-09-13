using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    // Slow-changing, session-scoped character info — level, name, and
    // whatever gets added later (title, class, etc). Deliberately NOT
    // where fast-changing combat stats like current HP belong once those
    // exist — those change too often to justify resending Name/Level
    // alongside them every time. This is "who is this character," not
    // "what's happening to them right now."
    [MessagePackObject(true)]
    public class CharacterData
    {
        public int Level;
        public string Name;
    }
}
