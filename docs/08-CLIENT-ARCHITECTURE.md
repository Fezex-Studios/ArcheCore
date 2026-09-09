# Client Architecture (Unity)

## Scene flow

Three scenes under `Assets/ArcheCore.Client/World/`:

- `server_select.unity` — where `GameDataBootstrap` runs (downloads/opens
  `gamedata.db`) and the player picks a server (`ServerSelectUI.cs`).
- `main_world.unity` — the actual gameplay scene, `dev_world.unity` its
  smaller sibling for local iteration.

`ClientNetwork` (`Networking/ClientNetwork.cs`) is a `DontDestroyOnLoad`
singleton created once and carried across scene loads:

```csharp
private void Awake()
{
    if (Instance != null) { Destroy(gameObject); return; }
    Instance = this;
    DontDestroyOnLoad(gameObject);
    ReadCommandLineToken();
}
```

## Session token sources

Three ways the client can end up with a token, checked in order at
startup:

```csharp
private void ReadCommandLineToken()
{
#if UNITY_EDITOR
    if (!string.IsNullOrEmpty(editorToken))          // 1. Inspector field, editor-only
    {
        SessionManager.Token = editorToken;
        return;
    }
#endif
    string[] args = System.Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++)
        if (args[i] == "-token" && i + 1 < args.Length)   // 2. -token CLI arg
            SessionManager.Token = args[i + 1];
}
```

The third source — the normal path for an actual player — is whatever the
login screen (`LoginScreenManager.cs`) sets after a successful AuthServer
call, before `ClientNetwork.Connect(ip)` is ever invoked. The `-token` CLI
path exists for launching multiple test clients (e.g. from the Tauri
launcher mentioned in project notes) without going through the login UI
each time.

## Connecting

```csharp
public void Connect(string ip)
{
    if (client != null) { client.Stop(); client = null; }   // tear down a leftover client first
    ServerPeer = null;
    client = new NetManager(this);
    if (!client.Start()) { Debug.LogError("..."); return; }  // local port already in use
    client.Connect(ip, 7777, "MMO");
}
```

The `client.Stop()` guard at the top exists specifically for the Unity
Editor's Stop/Play cycle — without it, hitting Play twice without a clean
shutdown in between throws trying to bind the same local UDP port twice.

## The dispatch gotcha

This is worth its own heading because it's the single easiest mistake to
make when extending the client, and nothing catches it at compile time.

`ArcheCore.Network`'s `ClientPacketDispatcher` has a working
`AutoRegister(Assembly)` method — reflection-scans for
`IClientPacketHandler` + `[PacketOpcode]`, exactly like the server-side
`PacketDispatcher.AutoRegister` described in
[02-NETWORKING-AND-PACKETS.md](02-NETWORKING-AND-PACKETS.md). **Nothing on
the client calls it.** `ClientNetwork.RegisterHandlers()` instead builds
the table by hand, one line per opcode:

```csharp
private void RegisterHandlers()
{
    dispatcher.Register(Opcodes.MOTD,           new W2CMOTDHandler());
    dispatcher.Register(Opcodes.SpawnPlayer,    new W2CSpawnPlayerHandler());
    dispatcher.Register(Opcodes.PlayerPosition, new W2CPlayerPositionHandler());
    dispatcher.Register(Opcodes.PlayerLeave,    new W2CPlayerLeaveHandler());
    dispatcher.Register(Opcodes.Announcement,   new W2CAnnouncementHandler());
    dispatcher.Register(Opcodes.SpawnNpc,       new W2CSpawnNpcHandler());
    dispatcher.Register(Opcodes.W2CTestPacket,  new W2CTestPacketHandler());
    dispatcher.Register(Opcodes.PlayerLevelResponse, new W2CPlayerlevelResponseHandler());
    dispatcher.Register(Opcodes.W2CCharacterList, new W2CCharacterListHandler());
    dispatcher.Register(Opcodes.W2CInteractDialogue, new W2CInteractDialogueHandler());
    dispatcher.Register(Opcodes.W2CInteractLoot,      new W2CInteractLootHandler());
    dispatcher.Register(Opcodes.W2CInteractDenied,    new W2CInteractDeniedHandler());
    dispatcher.Register(Opcodes.ChatMessage,          new W2CChatMessageHandler());
}
```

None of the `W2CHandlers` classes carry a `[PacketOpcode]` attribute, so
even if something started calling `AutoRegister` today, it would register
nothing — the attributes would need to be added to every handler class
first. **Until then, every new W2C packet needs a manual line here**, and
forgetting it produces no compile error and no obvious symptom beyond a
runtime log line:

```
[ClientPacketDispatcher] Unhandled packet: W2CShopOpen
```

printed once per received packet of that type, easy to miss in a busy
console. See
[09-KNOWN-GAPS-AND-NEXT-STEPS.md](09-KNOWN-GAPS-AND-NEXT-STEPS.md) for the
concrete fix (add the attributes, flip this method to call `AutoRegister`).

## Receiving packets

```csharp
public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
{
    Opcodes packet = (Opcodes)reader.GetUShort();
    dispatcher.Handle(packet, reader);
    reader.Recycle();
}
```

Mirrors the server's `WorldServer.OnNetworkReceive` exactly — same opcode-
prefix framing, since both ends share the LiteNetLib connection and the
`Opcodes` enum from `ArcheCore.Network`.

## Local reference data: `gamedata.db`

Covered in depth in
[03-DATABASE-AND-PERSISTENCE.md](03-DATABASE-AND-PERSISTENCE.md#gamedatadb--client-local-read-only-encrypted).
The short version for client-side code: never open `gamedata.db` yourself
— call `GameDataDatabase.Connection` (only valid after
`GameDataBootstrap` has run in `server_select.unity` and set
`GameDataDatabase.IsReady`), and go through `ItemRepository` /
`ItemRecord` for item lookups rather than writing raw SQL against the
connection in gameplay code, so the encryption/decryption lifecycle stays
in one place.

## UI layer

Plain `MonoBehaviour`s, no framework — `LoginScreenManager`,
`ServerSelectUI`, `CharacterCreateUI`, `CharacterSelectUI`, `ChatUI`,
`HudMessageDisplay`. The pattern used throughout (see
`W2CInteractDialogueHandler`'s note on itself) is: a `W2CHandler` decodes
the packet and calls a static method or raises a static event on the
relevant UI class (`PlayerUIEvents.RaiseCharacterListReceived(...)`,
`HudMessageDisplay.QueueOrShow(...)`) — the UI class itself has zero
networking knowledge. Follow this same shape for new UI: keep the
`W2CHandler` as the only thing that knows a packet was involved.

## Sending packets

Every `C2WSender` is a `static class` with a single `Send(...)` method,
calling `ClientPacketSender.SendPacket` (the client-side twin of
`WorldserverPacketSender`, both in `ArcheCore.Network`):

```csharp
// C2WInteractPacketSender.cs — the shape every C2W sender follows
public static class C2WInteractPacketSender
{
    public static void Send(NetPeer peer, int targetNetworkId)
    {
        if (peer == null) return;
        ClientPacketSender.SendPacket(peer, Opcodes.Interact,
            new C2WInteractPacket { TargetNetworkId = targetNetworkId },
            DeliveryMethod.ReliableOrdered);
    }
}
```

Note the explicit `DeliveryMethod.ReliableOrdered` here — worth specifying
explicitly (rather than relying on whatever LiteNetLib's default is) for
anything where a dropped packet would be a real gameplay bug, same
reasoning as the reliable-vs-unreliable split in
`PlayerMovementBroadcaster` covered in
[05-GAMEPLAY-SYSTEMS.md](05-GAMEPLAY-SYSTEMS.md).
