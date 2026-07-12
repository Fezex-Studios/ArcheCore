using MessagePack;

namespace ArcheCore.Network.Shared.Packets.W2C
{
    [MessagePackObject(true)]
    public class W2CInteractDialoguePacket
    {
        public string SpeakerName;
        public string Text;
    }
}