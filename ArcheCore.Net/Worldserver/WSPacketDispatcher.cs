using System;
using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using LiteNetLib;

namespace ArcheCore.Net.Worldserver
{
    public class PacketDispatcher
    {
        private readonly Dictionary<
            Opcodes,
            IPacketHandler> handlers =
            new();

        public void Register(
            Opcodes packet,
            IPacketHandler handler)
        {
            handlers[packet] =
                handler;
        }

        public void Handle(
            Opcodes packet,
            NetPeer peer,
            NetPacketReader reader)
        {
            if (handlers.TryGetValue(
                    packet,
                    out var handler))
            {
                handler.Handle(
                    peer,
                    reader);
            }
            else
            {
                Console.WriteLine(
                    $"Unhandled packet: {packet}");
            }
        }
    }
}