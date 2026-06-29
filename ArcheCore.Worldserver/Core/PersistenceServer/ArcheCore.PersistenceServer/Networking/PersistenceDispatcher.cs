using System.Collections.Generic;
using ArcheCore.Net.PersistenceServer;
using ArcheCore.Net.Shared.Packets.PersistenceServer;

namespace ArcheCore.WorldServer.PersistenceServer.Networking
{
    public class PersistenceDispatcher
    {
        private readonly Dictionary<
                PServerOpcodes,
                IPersistencePacketHandler>
            handlers = new();

        public void Register(
            PServerOpcodes opcode,
            IPersistencePacketHandler handler)
        {
            handlers[opcode] =
                handler;
        }

        public void Handle(PersistencePacket persistencePacket)
        {
            PServerOpcodes opcode =
                (PServerOpcodes)
                persistencePacket.Opcode;

            if(handlers.TryGetValue(
                   opcode,
                   out var handler))
            {
                handler.Handle(persistencePacket);
            }
        }
    }
}