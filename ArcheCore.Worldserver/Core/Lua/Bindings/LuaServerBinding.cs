// Server/Scripting/Bindings/LuaServerBinding.cs
using MoonSharp.Interpreter;
using UnityEngine;

namespace ArcheCore.WorldServer.Lua.Scripting.Bindings
{
    [MoonSharpUserData]
    public class LuaServerBinding
    {
        // Set by LuaEngine after construction — kept internal so only the
        // engine wires it up, scripts just call RegisterPlayerEvent.
        internal LuaEngine Engine;

        public void Log(string message)
        {
            Debug.Log($"[Lua] {message}");
        }

        public string GetTime()
        {
            return System.DateTime.Now
                .ToString("HH:mm:ss");
        }

        /// <summary>
        /// Called from Lua at script-load time, e.g.:
        ///   Server:RegisterPlayerEvent(1, function(player) ... end)
        /// Event IDs match ArcheCore.WorldServer.Lua.Scripting.PlayerEvent.
        /// </summary>
        public void RegisterPlayerEvent(int eventId, Closure handler)
        {
            Engine.RegisterHook((PlayerEvent)eventId, handler);
        }
    }
}