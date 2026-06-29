// Server/Scripting/Bindings/LuaServerBinding.cs

using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using NLog;


namespace ArcheCore.Server.World.Lua.Scripting.Bindings
{
    [MoonSharpUserData]
    public class LuaServerBinding
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        // Set by LuaEngine after construction — kept internal so only the
        // engine wires it up, scripts just call RegisterPlayerEvent.
        internal LuaEngine Engine;

        public void Log(string message)
        {
            Logger.Info($"[Lua] {message}");
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