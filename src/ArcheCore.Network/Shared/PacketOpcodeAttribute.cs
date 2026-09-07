using System;
using ArcheCore.Library.Net.Worldserver;

namespace ArcheCore.Network.Shared
{
    /// <summary>
    /// Tags a packet handler with the opcode it responds to. Both the
    /// server and client dispatchers scan for this attribute at startup
    /// (see PacketDispatcher.AutoRegister / ClientPacketDispatcher.AutoRegister)
    /// instead of relying on someone remembering to write a Register(...)
    /// call by hand.
    ///
    /// This is the fix for the class of bug where a handler exists but was
    /// never wired into the dispatcher: the attribute lives right on the
    /// class it describes, so there's nothing separate to forget.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class PacketOpcodeAttribute : Attribute
    {
        public Opcodes Opcode { get; }

        public PacketOpcodeAttribute(Opcodes opcode)
        {
            Opcode = opcode;
        }
    }
}