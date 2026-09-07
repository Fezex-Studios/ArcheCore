using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using LiteNetLib;



namespace ArcheCore.Network.Client
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

        /// <summary>
        /// Scans <paramref name="assembly"/> for every non-abstract
        /// IClientPacketHandler, reads its [PacketOpcode] attribute, and
        /// constructs + registers it. Every client handler today is
        /// parameterless, so this is simpler than the server-side version
        /// in PacketDispatcher (no ServiceContainer needed) - if a client
        /// handler ever needs a dependency, give it a constructor and this
        /// will still resolve it via Activator.CreateInstance with no args,
        /// which will throw clearly at startup rather than silently.
        /// </summary>
        public void AutoRegister(Assembly assembly)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => typeof(IClientPacketHandler).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var type in handlerTypes)
            {
                var attr = type.GetCustomAttribute<PacketOpcodeAttribute>();

                if (attr == null)
                {
                    Console.WriteLine(
                        $"[ClientPacketDispatcher] WARNING: {type.Name} implements " +
                        "IClientPacketHandler but has no [PacketOpcode] attribute - skipped.");
                    continue;
                }

                Register(attr.Opcode, (IClientPacketHandler)Activator.CreateInstance(type));
            }
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
            else
            {
                // Previously this branch didn't exist, so an unhandled
                // packet on the client vanished with zero log - worse than
                // the server side, which at least prints "Unhandled packet".
                Console.WriteLine($"[ClientPacketDispatcher] Unhandled packet: {packet}");
            }
        }
    }
}