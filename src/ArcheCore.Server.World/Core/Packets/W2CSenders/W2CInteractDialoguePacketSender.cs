using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CInteractDialoguePacketSender
{
    public static void Send(NetPeer peer, string speakerName, string text)
    {
        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.W2CInteractDialogue,
            new W2CInteractDialoguePacket
            {
                SpeakerName = speakerName,
                Text = text
            });
    }
}