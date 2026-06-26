using System.Collections.Generic;
using System.IO;
using ArcheCore.WorldServer.Lua.Scripting.Bindings;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using UnityEngine;

namespace ArcheCore.WorldServer.Lua.Scripting
{
    /// <summary>
    /// Eluna-style script engine: scripts are loaded once at boot and
    /// register themselves into event hooks via Server:RegisterPlayerEvent.
    /// Gameplay code never references a .lua file path again after boot —
    /// it just calls FireEvent(eventId, args), same as AzerothCore/Eluna's
    /// RegisterPlayerEvent + hook dispatch model.
    /// </summary>
    public class LuaEngine
    {
        private readonly Script script;
        private readonly LuaServerBinding serverBinding;

        private readonly Dictionary<PlayerEvent, List<Closure>> hooks = new();

        public LuaEngine()
        {
            script = new Script();

            // Override Unity's default loader with a filesystem loader
            script.Options.ScriptLoader = new FileSystemScriptLoader
            {
                ModulePaths = new string[] { "?", "?.lua" }
            };

            serverBinding = new LuaServerBinding { Engine = this };
            RegisterBindings();
        }

        private void RegisterBindings()
        {
            UserData.RegisterType<LuaServerBinding>();
            UserData.RegisterType<LuaPlayer>();

            script.Globals["Server"] = UserData.Create(serverBinding);
        }

        /// <summary>
        /// Boot-time only. Loads and runs every .lua file in the given
        /// directory exactly once so each can call Server:RegisterPlayerEvent
        /// to hook itself in. Should be called once from ServerBootstrap,
        /// never from a per-player code path.
        /// </summary>
        public void LoadAllScripts(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Debug.LogWarning($"[LuaEngine] Script directory not found: {directory}");
                return;
            }

            string[] files = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories);

            foreach (string path in files)
            {
                try
                {
                    script.DoFile(path);
                    WorldLogger.Info($"[LuaEngine] Loaded script: {path}");
                }
                catch (ScriptRuntimeException e)
                {
                    Debug.LogError($"[LuaEngine] Runtime error loading {path}: {e.DecoratedMessage}");
                }
                catch (SyntaxErrorException e)
                {
                    Debug.LogError($"[LuaEngine] Syntax error in {path}: {e.DecoratedMessage}");
                }
            }

            WorldLogger.Info($"[LuaEngine] Loaded {files.Length} script(s) from {directory}");
        }

        /// <summary>
        /// Called by LuaServerBinding when a script calls
        /// Server:RegisterPlayerEvent(eventId, handler) during LoadAllScripts.
        /// </summary>
        internal void RegisterHook(PlayerEvent evt, Closure handler)
        {
            if (!hooks.TryGetValue(evt, out List<Closure> list))
            {
                list = new List<Closure>();
                hooks[evt] = list;
            }

            list.Add(handler);
        }

        /// <summary>
        /// Fires all hooks registered for an event. This replaces RunFile()
        /// on the player-connect/disconnect/etc hot paths — no file path,
        /// no parsing, just calling already-resolved Lua functions.
        /// </summary>
        public void FireEvent(PlayerEvent evt, params object[] args)
        {
            if (!hooks.TryGetValue(evt, out List<Closure> list) || list.Count == 0)
                return;

            // Snapshot-iterate in case a handler registers/unregisters during the call
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    script.Call(list[i], args);
                }
                catch (ScriptRuntimeException e)
                {
                    Debug.LogError($"[LuaEngine] Error in {evt} hook: {e.DecoratedMessage}");
                }
            }
        }

        /// <summary>
        /// Calls a global function by name. Kept for non-hook utility scripts
        /// (e.g. admin commands) that aren't part of the event system.
        /// </summary>
        public void CallFunction(
            string functionName,
            params object[] args)
        {
            DynValue fn =
                script.Globals.Get(functionName);

            if (fn.Type != DataType.Function)
            {
                Debug.LogWarning(
                    $"[LuaEngine] Function not found: {functionName}");
                return;
            }

            try
            {
                script.Call(fn, args);
            }
            catch (ScriptRuntimeException e)
            {
                Debug.LogError(
                    $"[LuaEngine] Error calling {functionName}: {e.DecoratedMessage}");
            }
        }
    }
}