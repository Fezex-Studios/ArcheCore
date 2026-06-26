using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using LiteNetLib;



namespace ArcheCore.Net.Client
{
    public class ClientPacketDispatcher
    {
        private readonly Dictionary<
                Opcodes,
                IClientPacketHandler>
            handlers = new();

        public void Register(
            Opcodes packet,
            IClientPacketHandler handler)
        {
            handlers[packet] =
                handler;
        }

        public void Handle(
            Opcodes packet,
            NetPacketReader reader)
        {
            if(handlers.TryGetValue(
                   packet,
                   out var handler))
            {
                handler.Handle(
                    reader);
            }
        }
    }
}