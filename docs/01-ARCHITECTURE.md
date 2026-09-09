# ArcheCore Architecture

## The four processes

ArcheCore is not one server — it's four independent processes that only
know about each other through sockets:

```
┌─────────────────┐        LiteNetLib (UDP)         ┌──────────────────────┐
│  Unity Client    │ <-----------------------------> │   WorldServer (C#)   │
│  ArcheCore.Client│        port 7777, key "MMO"      │ ArcheCore.Server.    │
└────────┬─────────┘                                 │       World          │
         │                                            └──────────┬───────────┘
         │ HTTP                                                  │ raw TCP,
         │ (session tokens,                                      │ length-prefixed
         │  gamedata.db)                                         │ MessagePack
         v                                                        v
┌──────────────────┐                                ┌──────────────────────┐
│    AuthServer     │                                │  PersistenceServer   │
│ (not in this repo  │                                │  (Bun/TypeScript)    │
│  snapshot)         │                                │ ArcheCore.Server.    │
└────────────────────┘                                │     Persistence      │
                                                        └──────────────────────┘
```

- **Unity Client** (`ArcheCore.Client`) — HDRP Unity project. Talks to the
  WorldServer over LiteNetLib for everything gameplay-related, and to the
  AuthServer over plain HTTP for login and for downloading `gamedata.db`.
- **WorldServer** (`ArcheCore.Server.World`) — a .NET `IHostedService`
  (`WorldServer.cs`) driven by a generic host (`Program.cs` /
  `ServerBootstrap.cs`). Owns the game simulation: sessions, spatial
  awareness, NPCs, Lua scripting, and the authoritative tick loop. Never
  touches account data directly — it asks the PersistenceServer for it.
- **PersistenceServer** (`ArcheCore.Server.Persistence`) — a small Bun/Node
  process (`src/index.ts`) that owns `persistence.db` (character rows) and
  speaks a second, separate protocol to the WorldServer over a raw TCP
  socket on port `7778`.
- **AuthServer** — referenced by config (`WorldServerConfig.AuthServerUrl`,
  default `http://127.0.0.1:3000`) and by the client
  (`GameDataBootstrap.AuthServerUrl`), but its own source wasn't part of
  this snapshot. From the outside it exposes at least `POST
  /validate-session` (checked with an `x-internal-secret` header that must
  match `WorldServerConfig.InternalSecret`) and a `gamedata.db`
  download/version-check endpoint.

## Why two separate opcode systems

The client-facing protocol (`Opcodes`, LiteNetLib, ports 7777) and the
internal world-to-persistence protocol (`PServerOpcodes` /
`ProtocolPersistence`, raw TCP, port 7778) are completely separate, on
purpose: the client should never be able to address the persistence layer
directly, and the persistence layer doesn't need LiteNetLib's
unreliable/sequenced delivery semantics — it's a handful of
request/response calls (save, load, list, create) over a boring reliable
TCP connection. See
[02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md) for the wire
format of each.

## Tech stack per component

| Component | Language / runtime | Key libraries |
|---|---|---|
| WorldServer | C# / .NET (generic host) | LiteNetLib 2.1.4, MessagePack-CSharp, EF Core (Sqlite provider), Microsoft.Data.Sqlite, MoonSharp (Lua), NLog |
| PersistenceServer | TypeScript / Bun | `bun:sqlite`, `@msgpack/msgpack`, raw `net` module (no framework) |
| Unity Client | C# / Unity (HDRP) | LiteNetLib 2.1.4, MessagePack, sqlite-net-pcl + SQLitePCLRaw (for the local `gamedata.db`), Newtonsoft.Json, Sirenix Odin (inspector), TextMesh Pro |
| ArcheCore.Network | C# class library | Shared by both WorldServer and Client — this is where `Opcodes`, packet classes, and both dispatcher implementations live |

## Directory map (server side)

```
ArcheCore.Network/
  Shared/
    Opcodes.cs                  # client <-> world opcode enum
    PServerOpcodes.cs           # world <-> persistence opcode enum (C# copy)
    PacketOpcodeAttribute.cs    # [PacketOpcode(...)] used by both dispatchers
    Packets/{C2W,W2C,W2P,P2W}/  # wire payload classes (MessagePack)
  Utils/
    Worldserver/                # PacketDispatcher (server), IPacketHandler
    Client/                     # ClientPacketDispatcher, IClientPacketHandler
    Persistenceserver/          # PacketDispatcher/Sender used ON the C# side
                                 # for talking to the Node persistence server

ArcheCore.Server.World/
  Core/
    WorldServer.cs               # IHostedService: boot sequence + tick loop
    Managers/                    # SessionManager, PlayerSpawnManager,
                                  # PlayerMovementBroadcaster,
                                  # CharacterPersistence, PlayerManager
                                  # (facade), InterestManager, SpatialGrid,
                                  # ReplicationManager, InteractionRegistry,
                                  # QuestManager, DemoManager
    Interaction/                 # IInteractable, InteractionConstants
    Lua/                         # LuaEngine, PlayerEvent, LuaPlayer,
                                  # LuaServerBinding
    Packets/
      C2WHandlers/                # server-side handlers, [PacketOpcode(...)]
      W2CSenders/                 # static senders, one per outbound packet
    PersistenceServer/
      .../PersistenceClient.cs    # the TCP client to the Node process
      .../Networking/P2WHandlers/ # handles replies from persistence
      .../Networking/W2PSenders/  # sends requests to persistence
    Services/
      ServiceContainer.cs         # minimal DI used only for handler ctor args
      Authservice/                # calls the external AuthServer
  GameData/
    Items/, Npcs/, Quests/, Shop/, World/Spawners/  # POCOs + spawner logic
  Migrations/                    # EF Core migrations — the ONLY schema-management
                                  # system now; SQL/patches/ has been retired, see
                                  # 03-DATABASE-AND-PERSISTENCE.md and 09-KNOWN-GAPS-AND-NEXT-STEPS.md
  Lua/Server/                    # *.lua files loaded once at boot
  Docs/ADDING_PACKETSv2.md       # the packet-adding bible, read it

ArcheCore.Server.Persistence/
  src/
    index.ts                     # raw net.Server, length-prefixed framing
    Database/Database.ts         # bun:sqlite, owns persistence.db
    Networking/
      lib/PacketDispatcher.ts    # opcode -> handler map (Node side)
      RegisterHandlers.ts        # the ONE place Node handlers get wired up
      W2P/                       # request handlers (from World)
      P2W/                       # response senders (to World)
    Shared/
      protocol.persistence.ts    # canonical opcode enum (TS side)
      Opcodes.ts                 # STALE, unused, drifted duplicate — don't
                                  # edit this one, see 02-NETWORKING doc
      Packets/{Requests,Response}/
```

## Directory map (client side, Unity)

```
Assets/ArcheCore.Client/
  Networking/
    ClientNetwork.cs             # MonoBehaviour, owns the LiteNetLib client,
                                  # manually registers every W2C handler
    ClientPacketDispatcher.cs    # (lives in ArcheCore.Network actually —
                                  # shared with server) has an unused
                                  # AutoRegister path, see 07-CLIENT doc
    C2WSenders/                  # static Send(...) methods, mirror server
    W2CHandlers/                 # IClientPacketHandler implementations
  GameData/
    GameDataBootstrap.cs         # downloads/version-checks gamedata.db
    GameDataCrypto.cs            # decrypts it to a temp file
    GameDataDatabase.cs          # opens the decrypted sqlite-net connection
    ItemRecord.cs / ItemRepository.cs
  Gameplay/
    PlayerRegistry.cs, MMOCamera.cs, FollowCamera.cs
  UI/
    LoginScreenManager.cs, ServerSelectUI.cs,
    CharacterCreateUI.cs, CharacterSelectUI.cs, ChatUI.cs, ...
  World/
    dev_world.unity, main_world.unity, server_select.unity
```

## Boot sequence (WorldServer)

`WorldServer.StartAsync` (`Core/WorldServer.cs`) runs, in order:

1. `WorldDataDbContext.Database.MigrateAsync()` — applies any pending EF
   Core migrations against `Data/worldserver.db`. This replaced the old
   SQL patch runner — see
   [03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md) and
   [09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md).
   Seeding actual rows (NPC templates, spawners, etc.) is a separate
   concern handled by whatever tooling the project uses for that — not
   part of this boot step.
2. `PersistenceClient.Start()` — opens the TCP connection to the Node
   process on `WorldServerConfig.PersistencePort` (7778) and sends a
   `W2PConnectRequest` + a `HelloWorld` packet.
3. Managers are constructed in dependency order: `ReplicationManager` →
   `InteractionRegistry` → `SpawnManager` → `PlayerManager` (which itself
   builds `SessionManager`, `CharacterPersistence`, `PlayerMovementBroadcaster`,
   `PlayerSpawnManager`, and loads all Lua scripts).
3. `QuestManager.LoadFromDatabase()`.
4. `PacketDispatcher.AutoRegister` scans the assembly for every
   `[PacketOpcode]`-tagged `IPacketHandler` and wires it up via
   `ServiceContainer` (see [02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md)).
5. LiteNetLib `NetManager` starts listening on `NetworkConfig.Port` (7777).
6. `SpawnManager.SpawnInitialObjects()` — populates the world from the DB.
7. `DemoService.RunService()`.
8. The tick loop starts (`RunTickLoopAsync`), running at
   `WorldServerConfig.TickRate` (20 Hz by default), each tick draining
   `PlayerManager`'s pending-action queue and polling LiteNetLib events.

Everything past step 4 depends on step 4 having succeeded — if a handler
class exists but has no `[PacketOpcode]` attribute, `AutoRegister` logs a
warning and moves on rather than failing startup, so a silently-dead packet
handler is a real possibility worth checking for after adding one (see
[09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md)).
