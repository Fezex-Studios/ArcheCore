# ArcheCore.Movement — architecture and migration

A movement core that runs **identically on client and server**, with the
engine kept behind one interface. Written from first principles — no AAEmu
in it.

## Why it's shaped this way

Three properties you'll want later, each of which is painful to retrofit
and nearly free to design in now:

| Want | Requires |
|---|---|
| Server-authoritative movement | Movement code the server can run |
| Client prediction + reconciliation | Fixed timestep, pure step function, no hidden state |
| Ships, carts, lifts | Positions expressed in a parent's reference frame |

Everything below follows from those.

## Layout

```
ArcheCore.Movement/            ← netstandard2.1, NO UnityEngine reference
  MovementProfile.cs           tunables as data (player, mount, deer, ship)
  MoveInput.cs                 one tick of intent + sequence number
  MoveState.cs                 everything that persists between ticks
  ICollisionWorld.cs           the single engine seam
  CharacterMotor.cs            the simulation
  ReferenceFrame.cs            parent-space maths for vehicles

Assets/ArcheCore.Client/Scripts/Movement/
  UnityCollisionWorld.cs       ICollisionWorld over PhysX
  LocalCharacterMotor.cs       fixed-step driver + render interpolation
```

The **absence of a UnityEngine reference** in the core project is
load-bearing. It's what forces engine dependencies out through
`ICollisionWorld` instead of quietly creeping in. Don't add one "just for
Mathf".

## The three rules

1. **Fixed timestep.** `Step()` takes a constant `dt`. Never
   `Time.deltaTime`. Variable dt means two machines running the same inputs
   get different results, which doesn't make reconciliation inaccurate — it
   makes it impossible.
2. **No state outside `MoveState`.** Reconciliation replays inputs from an
   older state; a private field in the motor wouldn't rewind with it, so the
   replay would diverge for reasons invisible in the diff. Coyote time and
   jump buffering live in `MoveState` for exactly this reason.
3. **No engine calls in the core.** Everything about the world arrives
   through `ICollisionWorld`.

## What's implemented

- Grounded and airborne locomotion, acceleration and friction
- Slope-aware movement, slope limit, walkable-surface classification
- Collide-and-slide with **substepping**, so no sweep is longer than half a
  radius — an anti-tunnelling *guarantee*, not a mitigation
- Step-up over low obstacles, with clean failure
- Ground snap so descending slopes don't produce a series of hops
- Coyote time and jump buffering
- Overlap resolution for characters that start inside geometry
- Swimming with a wade threshold and buoyancy
- Gliding and scripted displacement as working placeholders
- `ReferenceFrame` maths for vehicle-relative movement

## What isn't, and what each one needs

**Client prediction + reconciliation.** The structure is ready — inputs
carry `Sequence`, the motor is pure, `MoveState` is a copyable struct — but
there's nothing to reconcile against until the server runs the motor.
Order: server-side `ICollisionWorld` → server steps inputs → server echoes
`(sequence, state)` → client keeps an input history and replays on
mismatch.

**Server `ICollisionWorld`.** This is the next real piece, and it's the
terrain heightmap you already raised. Start with height sampling only:
`CapsuleCast` downward becomes a heightmap lookup, horizontal casts return
no hit. That alone gives correct spawn heights, real ground validation, and
NPCs that aren't buried. Mesh geometry and a BVH come later.

**Vehicle `ICollisionWorld`.** For ships, implement the interface over a
hull's local colliders and hand the motor that instead of the world one.
The motor needs no changes — that's what the interface bought.

## Consistency beats fidelity

The client and server collision worlds don't have to be perfect; they have
to **agree**. Where they disagree the client predicts one thing, the server
another, and the player gets corrected. A server heightmap 10cm off from
the client's visual mesh produces constant small corrections on slopes.
Derive both from the same source data rather than tuning them to match.

## On determinism — don't chase it

You do **not** need bit-exact float reproduction across machines. Reconcile
with a tolerance: if the server's result is within a few centimetres of the
client's, accept the client's. Chasing true determinism (fixed-point maths,
identical instruction ordering) costs months and buys nothing an MMO needs.

## Migration

This is additive — nothing in the current build breaks by adding these
files. Suggested order:

1. **Add the project.** Reference it from the world server; drop the source
   (or a built DLL) into Unity.
2. **Test in isolation.** Empty scene, a capsule with
   `LocalCharacterMotor`, some terrain and stairs. Wire `InputSource` to
   your input code. Confirm walking, jumping, slopes, stairs, and that you
   *cannot* fall through terrain at any speed — that's the property the
   substepping guarantees, so test it deliberately: fall from very high.
3. **Swap the local player.** Replace `PlayerController`'s movement half
   with `LocalCharacterMotor`; keep its networking half, reading
   `motor.State` instead of `transform.position`. Remote players and the
   interpolator are untouched.
4. **Then** server-side `ICollisionWorld`, then authority, then prediction.

Do **not** run this alongside `CharacterController` on the same object. Two
systems writing one transform is what put remote players under the terrain.

## Profiles, not controllers

A mount, a deer and a cart differ in `MovementProfile` numbers, not in code.
Anything that differs in *behaviour* differs by `LocomotionMode`. If you
find yourself writing a second motor, that's the signal a mode is missing.

This also fixes validation properly: because both sides run the same motor
with the same profile, "is this legal" stops being a guess about plausible
speeds and becomes "does the client's result match what I computed". A mount
doesn't need anyone to remember to raise a speed cap — it has its own
profile and the server already knows it.
