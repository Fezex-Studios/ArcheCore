# Adding a new packet

This is the one document describing how a packet gets from client to
server and back in ArcheCore, and exactly what you need to do to add a
new one. Before the auto-registration system (see below), this required
editing 3-4 separate files by hand with no compiler check that you did
all of them — that's what caused both the "chat isn't working" bug
(client dispatcher registration never added) and the NullReferenceException
bug (wrong InterestManager instance passed to a handler) in one afternoon.
Neither can happen anymore, structurally, if you follow this doc.

**If you already know the pattern**, jump to the [Quick Reference](#quick-reference)
below. If you're new to it, read from the top — the two worked examples
after it walk through a real simple case and a real complicated one.

## Contents

- [Quick Reference](#quick-reference)
- [Prefix glossary](#prefix-glossary)
- [The big picture](#the-big-picture)
- [Opcode numbering rules](#opcode-numbering-rules)
- [Adding a client → server (C2W) packet](#adding-a-client--server-c2w-packet)
- [Adding a server → client (W2C) packet](#adding-a-server--client-w2c-packet)
- [Verifying a new packet works](#verifying-a-new-packet-works)
- [Worked Example 1: Hello World](#worked-example-1-hello-world-client--world-server--client)
- [Worked Example 2: through the Persistence Server](#worked-example-2-client--world-server--persistence-server--world-server--client)
- [Edge cases you'll hit](#edge-cases-youll-hit)
- [Adding a new UI element wired to a packet](#adding-a-new-ui-element-wired-to-a-packet)
- [Where things live](#where-things-live-quick-index)

---

## Quick reference

**Adding a C2W packet (client → world server):**
1. Add an explicit-numbered value to `Opcodes` (`ArcheCore.Network/Shared/Opcodes.cs`)
2. Add a `[MessagePackObject(true)]` packet class in `Shared/Packets/C2W/`
3. Add a static sender in `ArcheCore.Client/Networking/C2WSenders/`
4. Add an `IPacketHandler` in `ArcheCore.Server.World/Core/Packets/C2WHandlers/`, tagged `[PacketOpcode(...)]`
5. If the handler needs a dependency not already in `ServiceContainer`, add one line to `WorldServer.RegisterPackets`
6. Done — `AutoRegister` finds the handler. No dispatcher edits, no registration list.

**Adding a W2C packet (world server → client):** mirror image — packet class in `Shared/Packets/W2C/`, sender in `ArcheCore.Server.World/Core/Packets/W2CSenders/`, `IClientPacketHandler` in `ArcheCore.Client/Networking/W2CHandlers/` tagged `[PacketOpcode(...)]`. Client `AutoRegister` only resolves **parameterless** constructors today — a handler needing a dependency needs an extension to `ClientPacketDispatcher.AutoRegister` first.

**Adding anything that crosses into the Persistence Server:** none of the above auto-registration applies. See [Worked Example 2](#worked-example-2-client--world-server--persistence-server--world-server--client) and [edge case 4](#edge-cases-youll-hit) — you're hand-editing enums and registration lists in two languages, and it's easy to miss one.

**Before you ship any new packet:** run through [Verifying a new packet works](#verifying-a-new-packet-works).

---

## Prefix glossary

| Prefix / name | Means | Direction | Where the enum lives |
|---|---|---|---|
| `C2W` | Client → World Server | client sends, world server handles | `Opcodes` enum |
| `W2C` | World Server → Client | world server sends, client handles | `Opcodes` enum |
| `W2P` | World Server → Persistence Server | world server sends, Node handles | `PServerOpcodes` (C#) / `ProtocolPersistence` (TS) |
| `P2W` | Persistence Server → World Server | Node sends, world server handles | `PServerOpcodes` (C#) / `ProtocolPersistence` (TS) |
| `Opcodes` | The client ↔ world-server opcode enum | — | `ArcheCore.Network/Shared/Opcodes.cs` |
| `PServerOpcodes` | The world-server ↔ persistence opcode enum, **C# copy** | — | `ArcheCore.Network/Shared/PServerOpcodes.cs` |
| `ProtocolPersistence` | The world-server ↔ persistence opcode enum, **canonical TS copy** | — | `ArcheCore.Server.Persistence/src/Shared/protocol.persistence.ts` |

`PServerOpcodes` and `ProtocolPersistence` must always have identical names mapped to identical numbers — see [edge case 5](#edge-cases-youll-hit). There's also a stale, unused `Shared/Opcodes.ts` on the TS side — see the warning in Worked Example 2, step 2.

---

## The big picture

```
Client                                  Server
------                                  ------
YourFeature.cs
  -> C2WYourPacketSender.Send(peer,...)
        |
        v  (LiteNetLib, opcode-prefixed)
                                         WorldServer.OnNetworkReceive
                                           -> PacketDispatcher.Handle
                                                -> C2WYourHandler.Handle(peer, reader)
                                                     -> does the thing, may send a reply

                                         W2CYourReplySender.Send(peer, ...)
        |
        v
ClientPacketDispatcher.Handle
  -> W2CYourHandler.Handle(reader)
       -> updates local game state / UI
```

Every packet has an `Opcodes` enum value (shared between client and
server — `ArcheCore.Network/Shared/Opcodes.cs`), a MessagePack packet
class (the wire payload), a sender (writes bytes to a peer), and a
handler (reads bytes and does something). That's the whole shape, both
directions.

There is a **second, separate** dispatcher/opcode system between the
World Server and the Persistence Server (`PServerOpcodes`,
`PersistenceDispatcher`, `PersistenceClient`) — same idea, different
transport (raw TCP instead of LiteNetLib) and, importantly, **not**
covered by the C# auto-registration system, and the Persistence Server
itself is a separate Node/TypeScript process with no attribute system at
all. Worked Example 2 below walks through that path end to end, and the
Edge Cases section calls out exactly what's different about it.

---

## Opcode numbering rules

These apply to **every** opcode enum in the project (`Opcodes`,
`PServerOpcodes`, `ProtocolPersistence`) and are worth internalizing
before you touch any of them:

1. **Always assign an explicit number**, never let the enum
   auto-increment (`ChatMessage = 22`, not just `ChatMessage,`). Both
   worked examples below follow this. If you insert a new value in the
   middle of an implicitly-numbered enum, every value after it silently
   shifts — which changes what's actually on the wire without changing
   any code that looks correct.
2. **Append new values at the end** of the enum. Don't renumber
   existing values to "keep things tidy."
3. **Never reuse a retired number.** If a packet is deleted, retire its
   number with it (a `// 26 retired - was OldFeaturePacket` comment is
   fine) rather than letting a future packet silently take it over.
4. **For anything crossing the Persistence Server boundary**, the C#
   (`PServerOpcodes`) and TypeScript (`ProtocolPersistence`) enums must
   get the same name and number **in the same commit** — see
   [edge case 5](#edge-cases-youll-hit).

## Adding a client → server (C2W) packet

1. **Add the opcode.** Add a new value to the `Opcodes` enum
   (`ArcheCore.Network/Shared/Opcodes.cs`). Shared by both projects — one
   edit, both sides see it. Follow the [numbering rules](#opcode-numbering-rules)
   above.
2. **Add the packet class.** A small MessagePack-serializable class in
   `ArcheCore.Network/Shared/Packets/C2W/` with the fields the client
   needs to send. Follow the existing convention:

   ```csharp
   using MessagePack;

   namespace ArcheCore.Network.Shared.Packets.C2W
   {
       [MessagePackObject(true)]
       public class C2WYourPacket
       {
           public string SomeField;
       }
   }
   ```

3. **Add the sender**, in the client project
   (`ArcheCore.Client/Networking/C2WSenders/`) — a static method that
   writes the opcode + serialized packet to a `NetPeer` via
   `ClientPacketSender.SendPacket(...)`.
4. **Add the handler**, in
   `ArcheCore.Server.World/Core/Packets/C2WHandlers/`, implementing
   `IPacketHandler`:

   ```csharp
   using ArcheCore.Network.Shared;
   using ArcheCore.Library.Net.Worldserver;
   using ArcheCore.Network.Worldserver;

   namespace ArcheCore.Server.World.Networking.C2W
   {
       [PacketOpcode(Opcodes.YourNewOpcode)]
       public class C2WYourHandler : IPacketHandler
       {
           private readonly PlayerManager _playerManager;

           public C2WYourHandler(PlayerManager playerManager)
           {
               _playerManager = playerManager;
           }

           public void Handle(NetPeer peer, NetPacketReader reader)
           {
               var packet = MessagePackSerializer
                   .Deserialize<C2WYourPacket>(reader.GetRemainingBytes());

               // ... do the thing
           }
       }
   }
   ```

   The constructor can ask for any type already registered in
   `WorldServer.RegisterPackets` (currently: `PlayerManager`,
   `PersistenceClient`, `InterestManager`, `ReplicationManager`,
   `AuthService`, `InteractionRegistry`). If you need something that
   isn't registered yet, add one line to `RegisterPackets` — see below.

   > If this handler needs to `await` anything (a Persistence Server
   > call, for instance) before replying, read
   > [edge cases 6 and 7](#edge-cases-youll-hit) now, not after you've
   > written the bug.

5. **That's it.** You do **not** need to touch `WorldServer.cs`,
   `PacketDispatcher`, or any registration list. `WorldServer.RegisterPackets`
   calls `_packetDispatcher.AutoRegister(...)`, which scans the assembly at
   startup for every `IPacketHandler` and reads its `[PacketOpcode]`
   attribute. A handler that exists but was forgotten used to fail
   silently (the exact chat bug); now it either gets picked up
   automatically, or — if you forgot the attribute entirely — prints a
   clear `WARNING: ... has no [PacketOpcode] attribute` at startup instead
   of silently doing nothing at runtime.

### If your handler needs a new dependency

If your handler's constructor needs something not already registered,
add one line in `WorldServer.RegisterPackets`:

```csharp
services.Register(_yourNewManager);
```

`ServiceContainer` guarantees there is exactly one instance of any
registered type available to every handler — this is what makes it
structurally impossible to accidentally wire a second, empty instance of
a manager into a handler (the root cause of the InterestManager
NullReferenceException bug).

## Adding a server → client (W2C) packet

Mirror image of the above:

1. **Add the opcode** (same enum as above, if not already added).
2. **Add the packet class** in `ArcheCore.Network/Shared/Packets/W2C/`.
3. **Add the sender**, server-side, in
   `ArcheCore.Server.World/Core/Packets/W2CSenders/` — writes the opcode +
   payload to one or more peers via `WorldserverPacketSender.SendPacket(...)`.
4. **Add the handler**, client-side, in
   `ArcheCore.Client/Networking/W2CHandlers/`, implementing
   `IClientPacketHandler`:

   ```csharp
   using ArcheCore.Network.Shared;
   using ArcheCore.Library.Net.Worldserver;
   using ArcheCore.Network.Client;

   namespace ArcheCore.Client.Networking.W2C
   {
       [PacketOpcode(Opcodes.YourNewOpcode)]
       public class W2CYourHandler : IClientPacketHandler
       {
           public void Handle(NetPacketReader reader)
           {
               var packet = MessagePackSerializer
                   .Deserialize<W2CYourPacket>(reader.GetRemainingBytes());

               // ... update local game state / UI
           }
       }
   }
   ```

5. **That's it.** `ClientNetwork.RegisterHandlers` calls
   `dispatcher.AutoRegister(typeof(ClientNetwork).Assembly)`, which finds
   it the same way the server does. All client handlers today are
   parameterless; if yours needs a dependency, give it a constructor —
   `AutoRegister` on the client resolves parameterless constructors only
   right now, so a parameterized client handler needs a small extension to
   `ClientPacketDispatcher.AutoRegister` (mirroring the server's
   `ServiceContainer` approach) before it'll work.

---

## Verifying a new packet works

Before you consider a new packet done, whichever direction:

1. **Start the world server and check the startup log for
   `WARNING: ... has no [PacketOpcode] attribute`.** If your handler
   shows up there, the attribute is missing or malformed — fix it before
   testing anything else.
2. **Trigger the packet from the client** (a debug keybind is the
   fastest way, same as the Hello World example) and confirm the
   server-side log line you added actually appears.
3. **Confirm the reply lands client-side** — a `Debug.Log` in the
   handler is enough for a first pass.
4. **If the packet touches the Persistence Server**, also check the
   Node console for `Unknown Opcode N` — that specific message means
   one of the three manual steps in [edge case 4](#edge-cases-youll-hit)
   was missed (enum value, handler, or `RegisterHandler(...)` line).
5. **If two clients are involved** (anything broadcasting, like chat or
   position), test with two actual client instances, not one — several
   of the existing bugs this doc exists to prevent only show up with
   more than one connected peer.

---

## Worked Example 1: Hello World (Client → World Server → Client)

Goal: the client sends "Hello", the world server logs it and sends a
reply back, the client logs the reply. This is the smallest possible
round trip and reuses a packet you already have — `Opcodes.W2CTestPacket`
/ `W2CTestPacketSender` / `W2CTestPacketHandler` already exist in the
project and already do the "server → client" half. All that's missing is
the "client → server" half that triggers it.

**1. Add the opcode** (`ArcheCore.Network/Shared/Opcodes.cs`):

```csharp
public enum Opcodes : ushort
{
    // ...existing values...
    ChatMessage = 22,
    C2WHelloWorld = 23,   // NEW
}
```

**2. Add the packet class** (new file,
`ArcheCore.Network/Shared/Packets/C2W/C2WHelloWorldPacket.cs`):

```csharp
using MessagePack;

namespace ArcheCore.Network.Shared.Packets.C2W
{
    [MessagePackObject(true)]
    public class C2WHelloWorldPacket
    {
        public string Message;
    }
}
```

**3. Add the client sender** (new file,
`ArcheCore.Client/Networking/C2WSenders/C2WHelloWorldPacketSender.cs`):

```csharp
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WHelloWorldPacketSender
    {
        public static void Send(NetPeer peer, string message)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WHelloWorld,
                new C2WHelloWorldPacket { Message = message });
        }
    }
}
```

Call it from anywhere you already have `ClientNetwork.Instance.ServerPeer`
— e.g. a debug key bind:

```csharp
C2WHelloWorldPacketSender.Send(
    ClientNetwork.Instance.ServerPeer, "Hello from the client!");
```

**4. Add the server handler** (new file,
`ArcheCore.Server.World/Core/Packets/C2WHandlers/C2WHelloWorldHandler.cs`)
— this is the only genuinely new logic; it replies using the
**already-existing** `W2CTestPacketSender`:

```csharp
using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using NLog;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.C2WHelloWorld)]
    public class C2WHelloWorldHandler : IPacketHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<C2WHelloWorldPacket>(reader.GetRemainingBytes());

            Logger.Info($"[HelloWorld] Client said: {packet.Message}");

            // Reusing the test packet you already have for the reply -
            // no new W2C plumbing needed for this example.
            W2CTestPacketSender.Send(peer, $"World Server says: hello back!");
        }
    }
}
```

No constructor, no dependencies — this handler needs nothing from
`ServiceContainer`, which is fine; `AutoRegister` calls
`Activator.CreateInstance(type, args)` with a zero-length `args` array
when the constructor has no parameters, same as any other handler (see
[edge case 8](#edge-cases-youll-hit)).

**5. Client already has the handler** — `W2CTestPacketHandler` is already
tagged `[PacketOpcode(Opcodes.W2CTestPacket)]` and already logs the
message via `Debug.Log`. Nothing to add on that side.

That's the whole example: 1 enum value, 1 packet class, 1 sender, 1
handler — three of those four pieces were genuinely new, and the fourth
(the reply) reused code you already had. This is the shape almost every
simple request/response packet will take. Run it through the
[verification checklist](#verifying-a-new-packet-works) before you
consider it done.

---

## Worked Example 2: Client → World Server → Persistence Server → World Server → Client

Goal: the client asks the world server to "ping" the persistence server;
the persistence server replies; the world server forwards that reply
back to the originating client. This is the shape you'll actually use
for anything that needs a database round trip before the client can be
told the result (inventory purchases, guild lookups, leaderboard
queries, etc.) — it's deliberately more involved than Example 1 because
it exercises every edge case in the next section.

### The chain

```
Client                  World Server                 Persistence Server (Node)
------                  ------------                 --------------------------
C2WPingPersistence
  -> sent to WS
                         C2WPingPersistenceHandler
                           -> PersistenceClient
                                .W2PTestPing.Ping(msg)
                                   |
                                   v  (TCP, PServerOpcodes.W2PTestPing)
                                                      W2PTestPingHandler
                                                        -> does the thing
                                                        -> SendPacket back
                                   ^  (TCP, PServerOpcodes.P2WTestPingResponse)
                                   |
                         P2WTestPingResponseHandler
                           -> persistenceClient.ResolvePing(...)
                           -> resumes the awaited Task in the C2W handler
                         playerManager.EnqueueAction(() =>
                           W2CPingPersistenceResponseSender.Send(peer, reply))
  <- sent to client
W2CPingPersistenceResponseHandler
  -> logs / updates UI
```

### 1. World Server ↔ Client opcodes (`ArcheCore.Network/Shared/Opcodes.cs`)

```csharp
public enum Opcodes : ushort
{
    // ...existing values, including C2WHelloWorld = 23 from Example 1...
    C2WPingPersistence         = 24,   // NEW
    W2CPingPersistenceResponse = 25,   // NEW
}
```

### 2. World Server ↔ Persistence Server opcodes — **two places, kept in sync by hand**

C# side (`ArcheCore.Network/Shared/PServerOpcodes.cs`):

```csharp
namespace ArcheCore.Network.PersistenceServer
{
    public enum PServerOpcodes : ushort
    {
        // ...existing values...
        P2WCharacterListResponse = 9,
        W2PTestPing              = 10,  // NEW
        P2WTestPingResponse      = 11,  // NEW
    }
}
```

TypeScript side (`ArcheCore.Server.Persistence/src/Shared/protocol.persistence.ts`)
— **this is the canonical one the Node process actually imports**:

```typescript
export enum ProtocolPersistence
{
    // ...existing values...
    P2WCharacterListResponse = 9,
    W2PTestPing = 10,          // NEW
    P2WTestPingResponse = 11,  // NEW
}
```

⚠️ There is a second file, `Shared/Opcodes.ts`, with the same enum name
but stale values (it's missing `HelloWorld` and everything after it).
`RegisterHandlers.ts` imports from `protocol.persistence.ts`, not
`Opcodes.ts` — so `Opcodes.ts` is dead weight today, but it's a landmine
if anyone new starts importing from it out of habit. Worth deleting or
merging in its own small cleanup pass; not required for this example to
work, just flagged so you don't lose an hour to it later.

### 3. Packet classes (both projects, both directions)

C# request/response (`ArcheCore.Network/Shared/Packets/W2P/W2PTestPingRequest.cs`
and `.../P2W/P2WTestPingResponse.cs`):

```csharp
using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.W2P
{
    [MessagePackObject(true)]
    public class W2PTestPingRequest
    {
        // Guid as a string on the wire - see "Edge Cases" for why.
        public string RequestId;
        public string Message;
    }
}
```

```csharp
using MessagePack;

namespace ArcheCore.Network.Shared.Packets.PersistenceServer.P2W
{
    [MessagePackObject(true)]
    public class P2WTestPingResponse
    {
        public string RequestId;
        public string Reply;
    }
}
```

TypeScript mirrors (`Shared/Packets/Requests/W2PTestPingRequest.ts` and
`Shared/Packets/Response/P2WTestPingResponse.ts`):

```typescript
export interface W2PTestPingRequest {
    RequestId: string;
    Message: string;
}
```

```typescript
export interface P2WTestPingResponse {
    RequestId: string;
    Reply: string;
}
```

### 4. World Server → Persistence Server sender + correlation

`PersistenceClient` already tracks in-flight requests for
character load/create/list, keyed by `AccountId` (one request per
account makes sense for those). A ping isn't tied to an account, so it's
keyed by a fresh `Guid` instead (see [edge case 2](#edge-cases-youll-hit))
— add this alongside the existing
`pendingLoads` / `pendingCreates` / `pendingLists` dictionaries in
`PersistenceClient.cs`:

```csharp
internal readonly ConcurrentDictionary<string, TaskCompletionSource<P2WTestPingResponse>>
    pendingPings = new();

public W2PTestPingSender W2PTestPing { get; private set; }
```

...instantiate it in `Start()` next to the other senders:

```csharp
W2PTestPing = new W2PTestPingSender(this);
```

...and add a resolve method next to `ResolveLoad` / `ResolveCreate`:

```csharp
public void ResolvePing(P2WTestPingResponse response)
{
    if (pendingPings.TryRemove(response.RequestId, out var tcs))
        tcs.SetResult(response);
    else
        Logger.Warn($"[PersistenceClient] No pending ping for RequestId={response.RequestId}");
}
```

New sender (`.../Networking/W2PSenders/W2PTestPingSender.cs`), following
the same `TaskCompletionSource` pattern as `W2PCharacterSender.Load`:

```csharp
using System;
using System.Threading.Tasks;
using ArcheCore.Network.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer.W2P;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Senders
{
    public class W2PTestPingSender
    {
        private readonly PersistenceClient _client;

        public W2PTestPingSender(PersistenceClient client) => _client = client;

        public async Task<string> Ping(string message)
        {
            var requestId = Guid.NewGuid().ToString();

            var tcs = new TaskCompletionSource<P2WTestPingResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.pendingPings[requestId] = tcs;

            await _client.Send(
                PServerOpcodes.W2PTestPing,
                new W2PTestPingRequest { RequestId = requestId, Message = message });

            var response = await tcs.Task;
            return response.Reply;
        }
    }
}
```

### 5. World Server's P2W response handler

New file, `.../Networking/P2WHandlers/P2WTestPingResponseHandler.cs`
(follows the exact shape of `P2WCharacterLoadHandler`):

```csharp
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using MessagePack;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.PersistenceServer.Networking.P2W
{
    public class P2WTestPingResponseHandler : IPersistencePacketHandler
    {
        private readonly PersistenceClient _persistenceClient;

        public P2WTestPingResponseHandler(PersistenceClient persistenceClient)
        {
            _persistenceClient = persistenceClient;
        }

        public void Handle(PersistencePacket persistencePacket)
        {
            var response = MessagePackSerializer
                .Deserialize<P2WTestPingResponse>(persistencePacket.Payload);

            _persistenceClient.ResolvePing(response);
        }
    }
}
```

Register it in `PersistenceClient.RegisterHandlers()` — **this part is
manual, on purpose; see [edge case 4](#edge-cases-youll-hit)**:

```csharp
dispatcher.Register(
    PServerOpcodes.P2WTestPingResponse,
    new P2WTestPingResponseHandler(this));
```

### 6. Persistence Server (Node) handler

New file, `Networking/W2P/W2PTestPingHandler.ts`:

```typescript
import net from "net";
import { decode } from "@msgpack/msgpack";
import { SendPacket } from "../lib/PacketSender";
import { ProtocolPersistence } from "../../Shared/protocol.persistence";
import { W2PTestPingRequest } from "../../Shared/Packets/Requests/W2PTestPingRequest";
import { P2WTestPingResponse } from "../../Shared/Packets/Response/P2WTestPingResponse";

export async function W2PTestPingHandler(
    socket: net.Socket,
    payload: Uint8Array)
{
    const request = decode(payload) as W2PTestPingRequest;

    console.log(`[Persistence] Ping received: ${request.Message}`);

    const response: P2WTestPingResponse = {
        RequestId: request.RequestId,
        Reply: `Persistence Server says: got "${request.Message}"`
    };

    SendPacket(socket, ProtocolPersistence.P2WTestPingResponse, response);
}
```

Register it in `Networking/RegisterHandlers.ts` — **also manual, also
not auto-discovered; see [edge case 4](#edge-cases-youll-hit)**:

```typescript
import { W2PTestPingHandler } from "./W2P/W2PTestPingHandler";
// ...
RegisterHandler(ProtocolPersistence.W2PTestPing, W2PTestPingHandler);
```

### 7. World Server's C2W handler — ties it all together

New file, `.../C2WHandlers/C2WPingPersistenceHandler.cs`:

```csharp
using System.Threading.Tasks;
using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using ArcheCore.Server.World.Managers;
using ArcheCore.Server.World.Networking.W2C;
using LiteNetLib;
using MessagePack;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.Server.World.Networking.C2W
{
    [PacketOpcode(Opcodes.C2WPingPersistence)]
    public class C2WPingPersistenceHandler : IPacketHandler
    {
        private readonly PlayerManager _playerManager;
        private readonly PersistenceClient _persistence;

        public C2WPingPersistenceHandler(PlayerManager playerManager, PersistenceClient persistence)
        {
            _playerManager = playerManager;
            _persistence = persistence;
        }

        public void Handle(NetPeer peer, NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<C2WPingPersistencePacket>(reader.GetRemainingBytes());

            // Capture the NetworkId now, on the tick thread, before the
            // await below suspends this method - see edge cases 6 and 7.
            if (!_playerManager.TryGetNetworkId(peer, out int networkId))
                return;

            _ = PingAndReply(networkId, packet.Message);
        }

        private async Task PingAndReply(int networkId, string message)
        {
            string reply = await _persistence.W2PTestPing.Ping(message);

            // We're resuming on a TCP receive thread here, not the game
            // tick thread - every touch of shared state (including just
            // looking the peer back up) goes through EnqueueAction.
            _playerManager.EnqueueAction(() =>
            {
                if (!_playerManager.TryGetPeer(networkId, out var peer))
                    return; // player disconnected while we were waiting

                W2CPingPersistenceResponseSender.Send(peer, reply);
            });
        }
    }
}
```

New packet class (`Shared/Packets/C2W/C2WPingPersistencePacket.cs`):

```csharp
[MessagePackObject(true)]
public class C2WPingPersistencePacket
{
    public string Message;
}
```

New W2C sender (`.../W2CSenders/W2CPingPersistenceResponseSender.cs`):

```csharp
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.W2C;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Server.World.Networking.W2C
{
    public static class W2CPingPersistenceResponseSender
    {
        public static void Send(NetPeer peer, string reply)
        {
            WorldserverPacketSender.SendPacket(
                peer,
                Opcodes.W2CPingPersistenceResponse,
                new W2CPingPersistenceResponsePacket { Reply = reply });
        }
    }
}
```

New W2C packet class (`Shared/Packets/W2C/W2CPingPersistenceResponsePacket.cs`):

```csharp
[MessagePackObject(true)]
public class W2CPingPersistenceResponsePacket
{
    public string Reply;
}
```

### 8. Client sender + handler

`.../C2WSenders/C2WPingPersistencePacketSender.cs`:

```csharp
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WPingPersistencePacketSender
    {
        public static void Send(NetPeer peer, string message)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WPingPersistence,
                new C2WPingPersistencePacket { Message = message });
        }
    }
}
```

`.../W2CHandlers/W2CPingPersistenceResponseHandler.cs`:

```csharp
using ArcheCore.Network.Shared;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    [PacketOpcode(Opcodes.W2CPingPersistenceResponse)]
    public class W2CPingPersistenceResponseHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<W2CPingPersistenceResponsePacket>(reader.GetRemainingBytes());

            Debug.Log(packet.Reply);
        }
    }
}
```

That's the full chain: 2 new `Opcodes` values, 2 new `PServerOpcodes` /
`ProtocolPersistence` values (kept in sync by hand across a C# enum and
a TS enum), 4 new packet classes across two languages, a
`Guid`-correlated request/response pair through `PersistenceClient`, one
Node handler with manual registration, and a C# handler that has to
marshal its continuation back through `EnqueueAction`. Every one of
those steps is a real edge case — see below. Run it through the
[verification checklist](#verifying-a-new-packet-works), specifically
step 4 (checking the Node console).

---

## Edge cases you'll hit

These are the ones both worked examples above were built to surface.
Keep this list in mind any time a new packet isn't a simple
one-hop request/response.

**1. Fire-and-forget vs. request/response.**
`W2PHelloWorldSender.Send` and `W2CMOTDPacketSender.Send` are
fire-and-forget — nothing waits for a reply, so no correlation is
needed. The moment you need *this specific caller* to get *this specific
reply* (character load, the ping example above), you need a correlation
key and a `TaskCompletionSource`. Don't add the plumbing for requests
that don't need it — it's pure overhead for a one-way notification.

**2. Picking the correlation key.**
The existing character load/create/list requests are keyed by
`AccountId`, which works because there's only ever one in-flight request
per account for those operations. That assumption doesn't hold for
something like the ping example, or anything a player could trigger
multiple times in quick succession (spamming a "check price" button) —
use a fresh `Guid` per request instead, generated where the request
starts and echoed back unchanged in the response.

**3. `Guid` doesn't cross the C#/TypeScript boundary as a `Guid`.**
MessagePack for C# will happily serialize a `System.Guid`, but
`@msgpack/msgpack` on the Node side has no matching extension type for
it out of the box, and you don't want to debug that mismatch at 1am. Use
`string` (from `Guid.ToString()` / parsed back with `Guid.Parse(...)`)
in every packet class that crosses that specific boundary. Everywhere
that stays purely on the C# side (the `pendingPings` dictionary key, for
example) can use whichever type is convenient — `string` is fine there
too, and avoids a needless `Guid.Parse` round trip.

**4. The Persistence Server is not auto-registered — three manual edits, in two languages.**
`PacketDispatcher.AutoRegister` and `ClientPacketDispatcher.AutoRegister`
only scan C# assemblies for `[PacketOpcode]`. The Node process has none
of that: adding a persistence-side packet means manually adding the enum
value to `protocol.persistence.ts`, writing the handler, and adding one
`RegisterHandler(...)` line in `RegisterHandlers.ts` — miss any of the
three and you get `Unknown Opcode N` printed to the Node console with no
further clue. If this becomes a recurring pain point, the same
reflection trick doesn't port to TypeScript directly, but a lint rule or
a startup assertion ("every enum value has a registered handler") would
catch the same class of mistake.

**5. Two opcode enums that must stay numerically identical, by hand, forever.**
`PServerOpcodes` (C#) and `ProtocolPersistence` (TypeScript) are two
independently-typed enums that must have the same names mapped to the
same numbers. Nothing enforces this except discipline (see the
[opcode numbering rules](#opcode-numbering-rules) above) — a mismatch
means messages get silently misrouted to the wrong handler (or to no
handler, printing `Unknown Opcode`) with no compiler or runtime error
pointing at the actual cause. **Always add the same value, with the same
number, to both files in the same commit.** The already-existing drift
between `Shared/Opcodes.ts` and `Shared/protocol.persistence.ts`
(flagged in the worked example above) is exactly what this looks like
once it happens — worth a quick audit of every value in both files while
you're in there.

**6. Thread marshaling on the way back from an `await`.**
`C2WHandlers` run synchronously on the LiteNetLib receive path inside
the game tick. The moment you `await` a `PersistenceClient` call, your
continuation resumes on whatever thread the TCP `ReceiveLoop` completed
on — **not** the tick thread. Every existing handler that does this
(`C2WAuthenticateHandler`, `C2WCreateCharacterHandler`,
`C2WSelectCharacterHandler`) wraps everything after the `await` in
`playerManager.EnqueueAction(() => { ... })`, which queues the closure to
run on the next tick instead of running it immediately on the TCP
thread. Skipping this is a race condition waiting to happen, not a bug
you'll see in local testing with one client connected.

**7. The player may be gone by the time the reply comes back.**
Between sending the persistence request and getting the response, the
player can disconnect. Capture the `NetworkId` before the `await` (not
the `NetPeer` reference), and re-resolve the peer via
`playerManager.TryGetPeer(networkId, out peer)` *inside* the enqueued
action, bailing out silently if it's no longer there — see step 7 of the
ping example. Holding onto the original `NetPeer` and sending to it
regardless is usually harmless with LiteNetLib (it no-ops on a
disconnected peer) but resolving fresh is one line and removes any doubt.

**8. Handlers with zero constructor parameters still go through
`Activator.CreateInstance`.**
`AutoRegister` builds every handler via reflection, including ones with
no dependencies (Example 1's `C2WHelloWorldHandler`) — `ctor.GetParameters()`
just returns an empty array and `Activator.CreateInstance(type, args)`
works the same as `new HandlerType()`. Nothing special to do for a
dependency-free handler; it's the same code path.

## Adding a new UI element wired to a packet

The pattern is the same one used by chat, character list, and the
interaction dialogue/loot popups:

1. Build the UI (a prefab + a MonoBehaviour that shows/hides it and
   exposes plain C# methods like `ShowDialogue(string text)` — no
   networking code in this class).
2. In the matching `W2CYourHandler.Handle`, deserialize the packet and
   call into that UI class directly (see `W2CInteractDialogueHandler` or
   `W2CChatMessageHandler` for the existing pattern — a static
   `Instance`/event on the UI class that the handler calls or invokes).
3. If the UI needs to send something back (a dialogue choice, a chat
   message), that's a new C2W packet — follow the [C2W steps](#adding-a-client--server-c2w-packet)
   above, fired from a button's `OnClick` / an input field's submit
   handler.

Anything that broadcasts to *other* players (chat, position, someone
else's dialogue state) should go through `ReplicationManager` /
`InterestManager` rather than being sent ad hoc — see `C2WChatHandler`
for the reference implementation of "who should receive this."

## Where things live (quick index)

| What | Where |
|---|---|
| Opcodes enum (client ↔ world server) | `ArcheCore.Network/Shared/Opcodes.cs` |
| PServerOpcodes enum (world server ↔ persistence, C# side) | `ArcheCore.Network/Shared/PServerOpcodes.cs` |
| ProtocolPersistence enum (world server ↔ persistence, TS side — the canonical one) | `ArcheCore.Server.Persistence/src/Shared/protocol.persistence.ts` |
| C2W / W2C packet classes | `ArcheCore.Network/Shared/Packets/{C2W,W2C}/` |
| W2P / P2W packet classes (C#) | `ArcheCore.Network/Shared/Packets/PersistenceServer/{W2P,P2W}/` |
| W2P / P2W packet interfaces (TS) | `ArcheCore.Server.Persistence/src/Shared/Packets/{Requests,Response}/` |
| `[PacketOpcode]` attribute | `ArcheCore.Network/Shared/PacketOpcodeAttribute.cs` |
| Server packet handlers (C2W) | `ArcheCore.Server.World/Core/Packets/C2WHandlers/` |
| Server → client senders (W2C) | `ArcheCore.Server.World/Core/Packets/W2CSenders/` |
| Server dispatcher + auto-registration | `ArcheCore.Network/Utils/Worldserver/WSPacketDispatcher.cs` |
| `ServiceContainer` (handler dependency resolution) | `ArcheCore.Server.World/Core/Services/ServiceContainer.cs` |
| Client packet handlers (W2C) | `ArcheCore.Client/Networking/W2CHandlers/` |
| Client → server senders (C2W) | `ArcheCore.Client/Networking/C2WSenders/` |
| Client dispatcher + auto-registration | `ArcheCore.Network/Utils/Client/ClientPacketDispatcher.cs` |
| World server ↔ persistence client, correlation dictionaries | `ArcheCore.Server.World/Core/PersistenceServer/.../PersistenceClient.cs` |
| World server's P2W handlers | `.../PersistenceServer/.../Networking/P2WHandlers/` |
| World server's W2P senders | `.../PersistenceServer/.../Networking/W2PSenders/` |
| Persistence server (Node) opcode dispatch | `ArcheCore.Server.Persistence/src/Networking/lib/PacketDispatcher.ts` |
| Persistence server (Node) handlers + registration | `ArcheCore.Server.Persistence/src/Networking/W2P/` + `RegisterHandlers.ts` |
| Player state (session, spawn, movement, saving) | `ArcheCore.Server.World/Core/Managers/` — `SessionManager`, `PlayerSpawnManager`, `PlayerMovementBroadcaster`, `CharacterPersistence`, coordinated by the thin `PlayerManager` facade |