using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using NLog;

namespace ArcheCore.Server.World.Networking.W2C;

public static class W2CInteractDialoguePacketSender

{
    private static readonly Logger Logger =
        LogManager.GetCurrentClassLogger();
    public static void Send(NetPeer peer, string speakerName, string text)
    {
        
        Logger.Info(
            $"[W2CInteractDialogue] Sending: Speaker={speakerName}, Text={text}");

        WorldserverPacketSender.SendPacket(
            peer,
            Opcodes.W2CInteractDialogue,
            new W2CInteractDialoguePacket
            {
                SpeakerName = speakerName,
                Text = text
            });

        Logger.Info("[W2CInteractDialogue] SendPacket returned");
    }
}