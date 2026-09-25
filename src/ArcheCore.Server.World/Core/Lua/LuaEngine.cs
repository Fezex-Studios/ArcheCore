using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ArcheCore.Server.World.Lua.Scripting.Bindings;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using NLog;

namespace ArcheCore.Server.World.Lua.Scripting
{
    /// <summary>
    /// Eluna-style script engine: scripts are loaded once at boot and
    /// register themselves into event hooks via Server:RegisterPlayerEvent.
    /// Gameplay code never references a .lua file path again after boot —
    /// it just calls FireEvent(eventId, args), same as AzerothCore/Eluna's
    /// RegisterPlayerEvent + hook dispatch model.
    ///
    /// SANDBOX (audit M7). Scripts get Preset_SoftSandbox: strings, tables,
    /// maths, coroutines, os.time/clock - and NOT io, os.execute/remove,
    /// load/loadstring/dofile/require. A script can't touch the disk or
    /// load code from a string.
    ///
    /// INSTRUCTION BUDGET. Every call into Lua runs as a coroutine with
    /// MoonSharp's AutoYieldCounter set, so a script that runs longer than
    /// its budget is stopped instead of freezing the tick thread for
    /// everyone. A hook that blows its budget MaxOverruns times is switched
    /// off (logged) - a `while true do end` costs one budget, not the shard.
    /// </summary>
    public class LuaEngine
    {
        private readonly Script script;
        private readonly LuaServerBinding serverBinding;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<PlayerEvent, List<Closure>> hooks = new();

        /// <summary>
        /// VM instructions one hook call may run. MoonSharp does very roughly
        /// 20-50M/s, so this is a few milliseconds - hundreds of times what a
        /// normal hook needs, and a small fraction of a 50ms tick.
        /// </summary>
        public const int HookInstructionBudget = 200_000;

        /// <summary>Budget for running a script file's top level at boot.</summary>
        public const int LoadInstructionBudget = 5_000_000;

        /// <summary>Overruns before a hook is switched off.</summary>
        public const int MaxOverruns = 3;

        private readonly Dictionary<Closure, int> _overruns = new();

        public LuaEngine()
        {
            script = new Script(CoreModules.Preset_SoftSandbox);

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
                Logger.Warn($"[LuaEngine] Script directory not found: {directory}");
                return;
            }

            string[] files = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories);

            foreach (string path in files)
            {
                try
                {
                    // Parse, then run the top level under a budget - a
                    // script's own body can loop forever too.
                    DynValue chunk = script.LoadFile(path);
                    if (RunGuarded(chunk, LoadInstructionBudget, path, System.Array.Empty<object>()) == GuardResult.Overran)
                        Logger.Error($"[LuaEngine] {path} ran past {LoadInstructionBudget:N0} instructions while loading - stopped.");
                    else
                        Logger.Info($"[LuaEngine] Loaded script: {path}");
                }
                catch (ScriptRuntimeException e)
                {
                    Logger.Error($"[LuaEngine] Runtime error loading {path}: {e.DecoratedMessage}");
                }
                catch (SyntaxErrorException e)
                {
                    Logger.Error($"[LuaEngine] Syntax error in {path}: {e.DecoratedMessage}");
                }
            }

           Logger.Info($"[LuaEngine] Loaded {files.Length} script(s) from {directory}");
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
        /// <summary>
        /// Raised for EVERY fired event, before the Lua hooks run, whether or
        /// not any script is listening.
        ///
        /// This is how server systems (QuestManager) hear about kills,
        /// harvests and conversations without a second event system beside
        /// this one: everything that already calls FireEvent keeps calling
        /// it, and both Lua and C# listen to the same call.
        ///
        /// Fired on whatever thread called FireEvent - today always the tick
        /// thread - so listeners must be as cheap as a Lua hook.
        /// </summary>
        public event Action<PlayerEvent, object[]> EventFired;

        public void FireEvent(PlayerEvent evt, params object[] args)
        {
            // Before the early return below: a C# listener must hear the
            // event even on a server with no scripts loaded at all.
            try
            {
                EventFired?.Invoke(evt, args);
            }
            catch (Exception e)
            {
                Logger.Error($"[LuaEngine] C# listener for {evt} threw: {e.Message}");
            }

            if (!hooks.TryGetValue(evt, out List<Closure> list) || list.Count == 0)
                return;

            // Snapshot-iterate in case a handler registers/unregisters during the call
            for (int i = 0; i < list.Count; i++)
            {
                var hook = list[i];

                try
                {
                    if (RunGuarded(DynValue.NewClosure(hook), HookInstructionBudget, evt.ToString(), args) != GuardResult.Overran)
                        continue;

                    int overruns = _overruns.TryGetValue(hook, out var n) ? n + 1 : 1;
                    _overruns[hook] = overruns;

                    if (overruns >= MaxOverruns)
                    {
                        list.RemoveAt(i--);
                        _overruns.Remove(hook);
                        Logger.Error($"[LuaEngine] A {evt} hook ran past {HookInstructionBudget:N0} instructions " +
                                     $"{overruns} times - SWITCHED OFF until the server restarts. Fix the script.");
                    }
                    else
                    {
                        Logger.Error($"[LuaEngine] A {evt} hook ran past {HookInstructionBudget:N0} instructions - stopped " +
                                     $"({overruns}/{MaxOverruns} before it's switched off).");
                    }
                }
                catch (ScriptRuntimeException e)
                {
                    Logger.Error($"[LuaEngine] Error in {evt} hook: {e.DecoratedMessage}");
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
                Logger.Warn(
                    $"[LuaEngine] Function not found: {functionName}");
                return;
            }

            try
            {
                if (RunGuarded(fn, HookInstructionBudget, functionName, args) == GuardResult.Overran)
                    Logger.Error($"[LuaEngine] {functionName} ran past {HookInstructionBudget:N0} instructions - stopped.");
            }
            catch (ScriptRuntimeException e)
            {
                Logger.Error(
                    $"[LuaEngine] Error calling {functionName}: {e.DecoratedMessage}");
            }
        }

        private enum GuardResult { Finished, Overran }

        /// <summary>
        /// Call a Lua function with an instruction budget. Runs it as a
        /// coroutine with AutoYieldCounter set: MoonSharp then forces a yield
        /// after that many instructions, and a forced yield here means "over
        /// budget" - the coroutine is simply dropped, never resumed.
        /// A script that yields on purpose inside a hook is treated the same
        /// (hooks must run to completion).
        /// </summary>
        private GuardResult RunGuarded(DynValue function, int budget, string what, object[] args)
        {
            var coroutine = script.CreateCoroutine(function).Coroutine;
            coroutine.AutoYieldCounter = budget;

            var dynArgs = new DynValue[args?.Length ?? 0];
            for (int i = 0; i < dynArgs.Length; i++)
                dynArgs[i] = DynValue.FromObject(script, args[i]);

            coroutine.Resume(dynArgs);

            return coroutine.State == CoroutineState.Dead ? GuardResult.Finished : GuardResult.Overran;
        }
    }
}