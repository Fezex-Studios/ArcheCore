using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using LiteNetLib;

namespace ArcheCore.Network.Worldserver
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

        /// <summary>
        /// Scans <paramref name="assembly"/> for every non-abstract
        /// IPacketHandler, reads its [PacketOpcode] attribute, and
        /// constructs + registers it by resolving each constructor
        /// parameter through <paramref name="resolve"/>. Replaces the old
        /// hand-written list of "new XHandler(...)" calls in
        /// WorldServer.RegisterPackets - a handler that exists but was
        /// never wired in used to fail silently; now it can only fail
        /// loudly (missing attribute) or not compile (missing dependency).
        ///
        /// Requires every handler to have exactly one public constructor -
        /// that's true for all handlers in this project today, so this is
        /// enforced with .Single(), which throws immediately (at startup,
        /// not at runtime when the packet finally arrives) if that ever
        /// stops being the case.
        /// </summary>
        public void AutoRegister(Func<Type, object> resolve, Assembly assembly)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => typeof(IPacketHandler).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var type in handlerTypes)
            {
                var attr = type.GetCustomAttribute<PacketOpcodeAttribute>();

                if (attr == null)
                {
                    Console.WriteLine(
                        $"[PacketDispatcher] WARNING: {type.Name} implements IPacketHandler " +
                        "but has no [PacketOpcode] attribute - skipped.");
                    continue;
                }

                var ctor = type.GetConstructors().Single();
                var args = ctor.GetParameters()
                    .Select(p => resolve(p.ParameterType))
                    .ToArray();

                Register(attr.Opcode, (IPacketHandler)Activator.CreateInstance(type, args));
            }
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