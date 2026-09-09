# Lua Scripting System

ArcheCore embeds [MoonSharp](https://www.moonsharp.org/) to run gameplay
scripts server-side, using an **Eluna/AzerothCore-style register-once,
fire-by-id model** rather than "run a script file when an event happens."
This is a deliberate design choice, called out directly in the
`LuaEngine` doc comment — worth understanding before you add new hooks.

## The model, in one sentence

Every `.lua` file under `Lua/Server/` is loaded and *executed* exactly once
at boot; what that execution typically does is call
`Server:RegisterPlayerEvent(eventId, function(...) ... end)` to hang a
closure off an event id, and from then on the C# side calls
`LuaEngine.FireEvent(evt, args...)` to invoke every closure registered for
that event — no file path, no re-parsing, no `DoFile` on the hot path.

## The moving pieces

| Class | File | Role |
|---|---|---|
| `LuaEngine` | `Core/Lua/LuaEngine.cs` | Owns the MoonSharp `Script`, loads all scripts at boot, holds the `PlayerEvent -> List<Closure>` hook table, exposes `FireEvent` |
| `PlayerEvent` | `Core/Lua/Playerevent.cs` | The enum of hookable event ids |
| `LuaServerBinding` | `Core/Lua/Bindings/LuaServerBinding.cs` | The global `Server` table scripts see — logging, time, and `RegisterPlayerEvent` itself |
| `LuaPlayer` | `Core/Lua/Bindings/LuaPlayer.cs` | The object passed into most hooks as `player` — a safe, narrow view over a real `NetPeer` |

## `PlayerEvent` — the current hook table

```csharp
// Core/Lua/Playerevent.cs
public enum PlayerEvent
{
    OnConnect    = 1,
    OnDisconnect = 2,
    OnLevelUp    = 3,
    OnChat       = 4,
    OnDeath      = 5,
    OnInteract   = 6,
}
```

Only `OnConnect` and `OnInteract` are actually fired anywhere in the C#
code today (`PlayerSpawnManager.HandlePlayerConnected` and
`PlayerManager.FireInteractEvent` respectively) — `OnDisconnect`,
`OnLevelUp`, `OnChat`, and `OnDeath` are reserved slots with no `FireEvent`
call wired up yet. Adding the C# side of one of those is a one-line
`_luaEngine.FireEvent(PlayerEvent.OnChat, luaPlayer, message)` call at the
right point in the relevant handler/manager — the Lua side needs nothing
extra, `RegisterPlayerEvent(4, ...)` already works today, it just never
fires.

## Boot-time loading

```csharp
// LuaEngine.LoadAllScripts — called once from PlayerManager.InitializeScripts()
public void LoadAllScripts(string directory)
{
    string[] files = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories);
    foreach (string path in files)
    {
        try
        {
            script.DoFile(path);
            Logger.Info($"[LuaEngine] Loaded script: {path}");
        }
        catch (ScriptRuntimeException e) { Logger.Error(...); }
        catch (SyntaxErrorException e)   { Logger.Error(...); }
    }
}
```

`PlayerManager.Lua` resolves the directory as
`AppContext.BaseDirectory/Lua/Server` — i.e. it's read relative to the
built executable, not the source tree, so scripts need to be copied to the
output directory (check the `.csproj` for a `CopyToOutputDirectory` item,
or copy them manually) for a fresh build to pick them up. A syntax error or
runtime error in one script is logged and **does not stop the others from
loading** — each file is wrapped in its own try/catch.

## Registering a hook, from Lua

```lua
-- on_player_connect.lua
local function OnPlayerConnect(player)
    Server:Log("Player connected: AccountId=" .. player.AccountId)
    player:SendAnnouncementMessage("Welcome to ArcheCore!")
end

Server:RegisterPlayerEvent(1, OnPlayerConnect) -- 1 = PlayerEvent.OnConnect
```

`Server:RegisterPlayerEvent` is a real C# method on `LuaServerBinding`:

```csharp
// LuaServerBinding.cs
public void RegisterPlayerEvent(int eventId, Closure handler)
{
    Engine.RegisterHook((PlayerEvent)eventId, handler);
}
```

which appends to `LuaEngine`'s hook table:

```csharp
internal void RegisterHook(PlayerEvent evt, Closure handler)
{
    if (!hooks.TryGetValue(evt, out List<Closure> list))
    {
        list = new List<Closure>();
        hooks[evt] = list;
    }
    list.Add(handler);
}
```

Multiple scripts can register for the same event — `FireEvent` calls every
registered closure in registration order, snapshot-iterating so a handler
that registers/unregisters mid-call doesn't corrupt the loop:

```csharp
public void FireEvent(PlayerEvent evt, params object[] args)
{
    if (!hooks.TryGetValue(evt, out List<Closure> list) || list.Count == 0) return;

    for (int i = 0; i < list.Count; i++)
    {
        try { script.Call(list[i], args); }
        catch (ScriptRuntimeException e) { Logger.Error($"[LuaEngine] Error in {evt} hook: {e.DecoratedMessage}"); }
    }
}
```

One failing hook doesn't stop the others from running for the same event —
each `Call` is individually try/caught.

## The `Server` global — full API today

Exposed via `LuaServerBinding`, `[MoonSharpUserData]`:

| Lua call | C# signature | Notes |
|---|---|---|
| `Server:Log(message)` | `void Log(string message)` | Writes to NLog at Info level, prefixed `[Lua]` |
| `Server:GetTime()` | `string GetTime()` | Returns `HH:mm:ss` of server local time |
| `Server:RegisterPlayerEvent(eventId, handler)` | `void RegisterPlayerEvent(int eventId, Closure handler)` | Boot-time only in practice — see above |

## The `LuaPlayer` object — full API today

Passed as the first argument to `OnConnect` and `OnInteract` hooks, backed
by a real `NetPeer` but with no direct network access exposed to the
script:

```csharp
// Core/Lua/Bindings/LuaPlayer.cs
[MoonSharpUserData]
public class LuaPlayer
{
    public int NetworkId { get; }
    public int AccountId { get; }

    public void SendAnnouncementMessage(string message);   // W2CAnnouncementPacket to this player only
    public void Kick(string reason);                        // peer.Disconnect() — reason is currently unused/unsent
    public void SendDialogue(string speakerName, string text);   // W2CInteractDialoguePacket
    public void SendLootMessage(string itemName);                 // W2CInteractLootPacket — UI message only, grants nothing
}
```

Two things worth knowing before you script against this:

- **`SendLootMessage` is explicitly a placeholder** — the doc comment says
  so directly: "this only sends a client-side message, it does not grant
  an item." There's no inventory system yet. If you're building the shop
  system, this is one of the first things it will need to replace with a
  real grant-item call.
- **`Kick(reason)` doesn't currently send `reason` anywhere** — it just
  calls `peer.Disconnect()`, which uses LiteNetLib's default disconnect
  path. If you want the client to display *why* it was kicked, that needs
  a packet sent before the disconnect, not a parameter threaded through to
  it.

## Worked example: the NPC guard script

This is the real `OnInteract` script shipped with the project, and it
doubles as the reference for how `targetTemplateId`/`targetKind` args work:

```lua
-- npc_guard_example.lua
-- PlayerEvent.OnInteract = 6
-- Handler signature: function(player, targetTemplateId, targetKind)

Server:RegisterPlayerEvent(6, function(player, targetTemplateId, targetKind)

    if targetTemplateId == 1 then
        player:SendDialogue("Guard", "Halt! State your business, traveler.")
        return
    end

    if targetTemplateId == 2 then
        -- Placeholder until inventory exists - see W2CInteractLootPacket.
        player:SendLootMessage("Rusty Sword")
        return
    end

    -- Unhandled template id - say nothing rather than guess.
end)
```

The C# call site that fires this (`PlayerManager.FireInteractEvent`, called
from `C2WInteractHandler` after range/existence checks pass):

```csharp
public void FireInteractEvent(LuaPlayer player, IInteractable target)
{
    _luaEngine.FireEvent(
        PlayerEvent.OnInteract,
        player,
        target.TemplateId,
        (int)target.Kind);
}
```

`targetKind` arrives in Lua as a plain integer (`InteractableKind` cast to
`int`), currently only ever `1` (`Npc`) — see
[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md) for `IInteractable` and
where new kinds (`Lootable`, `QuestObject`, `ResourceNode`) would slot in.

## Adding a new hookable event, end to end

1. Add a value to `PlayerEvent` in `Core/Lua/Playerevent.cs`. Existing
   values are stable ids referenced from `.lua` files by number
   (`Server:RegisterPlayerEvent(6, ...)`), so append, don't renumber — same
   rule as the packet opcode enums in
   [02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md).
2. Call `_luaEngine.FireEvent(PlayerEvent.YourEvent, args...)` from
   wherever the triggering C# code lives. If it needs a `LuaPlayer`, get
   one via `playerManager.CreateLuaPlayer(peer)`.
3. If scripts need a new capability on `player` (say, `player:OpenShop()`),
   add the method to `LuaPlayer` itself — it's a thin wrapper, so this is
   usually a one-method addition that calls an existing sender or manager.
4. Drop a `.lua` file in `Lua/Server/` that calls
   `Server:RegisterPlayerEvent(yourNewId, function(...) ... end)`. No
   registration list to update anywhere else — `LoadAllScripts` picks up
   any `.lua` file in the directory automatically.

## Things to watch for

- **No hot-reload.** Scripts load once at boot (`PlayerManager.InitializeScripts`,
  called once from `WorldServer.StartAsync`). Changing a `.lua` file
  requires a server restart to take effect.
- **No per-script isolation.** All scripts share one MoonSharp `Script`
  instance and one global table — a script that clobbers a global used by
  another script will affect both. Fine at the current script count; worth
  reconsidering (per-file sandboxing, or at least a naming convention) once
  there are more than a handful of files.
- **`CallFunction(name, args)` exists but is separate from the hook
  system** — it looks up and calls a *global function by name*, for
  "utility scripts" like admin commands, not part of the
  register/fire-by-id flow. Don't reach for it for anything that should be
  a `PlayerEvent` hook.
