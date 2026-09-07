using System;
using System.Collections.Generic;

namespace ArcheCore.Server.World.Core.Services
{
    /// <summary>
    /// Not a general-purpose DI container - just enough to build packet
    /// handlers by reflection with zero chance of two different instances
    /// of the same type existing. That guarantee is what permanently fixes
    /// the class of bug where a handler was accidentally constructed with
    /// its own private/empty manager instance instead of the shared one
    /// (e.g. a second, empty InterestManager instead of the real one).
    ///
    /// Every long-lived singleton the world server owns (PlayerManager,
    /// InterestManager, ReplicationManager, etc.) is registered here once
    /// in WorldServer.RegisterPackets, and PacketDispatcher.AutoRegister
    /// resolves handler constructor parameters from this same table - so
    /// there is exactly one place any dependency can come from.
    /// </summary>
    public class ServiceContainer
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Register<T>(T instance)
        {
            _services[typeof(T)] = instance;
        }

        public object Resolve(Type type)
        {
            if (_services.TryGetValue(type, out var instance))
                return instance;

            throw new InvalidOperationException(
                $"[ServiceContainer] No instance registered for {type.Name}. " +
                "Add a services.Register(...) call for it in WorldServer.RegisterPackets.");
        }
    }
}