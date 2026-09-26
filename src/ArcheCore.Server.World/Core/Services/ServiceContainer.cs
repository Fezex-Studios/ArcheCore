using System;
using System.Collections.Generic;

namespace ArcheCore.Server.World.Core.Services
{
    /// <summary>
    /// Every long-lived singleton the world server owns, registered once by
    /// WorldServer. Two jobs:
    ///
    ///   - Packet handlers are built by reflection from it
    ///     (PacketDispatcher.AutoRegister), so a handler can never be handed
    ///     a second, empty copy of a manager.
    ///   - Managers find each other through it in IInitializable.Initialize
    ///     (two-phase start-up), instead of through static Current handles.
    ///
    /// Not a general DI container: one instance per type, registered by hand.
    /// </summary>
    public class ServiceContainer
    {
        private readonly Dictionary<Type, object> _services = new();
        private readonly List<object> _inOrder = new();

        public void Register<T>(T instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance), $"[ServiceContainer] Registering a null {typeof(T).Name}.");

            _services[typeof(T)] = instance;

            if (!_inOrder.Contains(instance))
                _inOrder.Add(instance);
        }

        public object Resolve(Type type)
        {
            if (_services.TryGetValue(type, out var instance))
                return instance;

            throw new InvalidOperationException(
                $"[ServiceContainer] No instance registered for {type.Name}. " +
                "Add a services.Register(...) call for it in WorldServer.");
        }

        public T Get<T>() => (T)Resolve(typeof(T));

        public bool TryGet<T>(out T instance)
        {
            if (_services.TryGetValue(typeof(T), out var o))
            {
                instance = (T)o;
                return true;
            }

            instance = default;
            return false;
        }

        /// <summary>
        /// Phase 2 of start-up: Initialize every registered IInitializable,
        /// in registration order, each exactly once.
        /// </summary>
        public void InitializeAll()
        {
            foreach (var instance in _inOrder)
                if (instance is IInitializable initializable)
                    initializable.Initialize(this);
        }
    }
}
