# Jolt physics, scene graph and mesh pipeline: recommendation

Status: final recommendation from the spike on branch `spike/jolt-scene`. Nothing here is merged into the game.
Background, architecture and data formats are in [jolt-scene-mesh.md](jolt-scene-mesh.md); this page is the
decision. All numbers come from the `Jolt spike` CI workflow (`.github/workflows/spike.yml`, `spike/JoltSpike`),
run on three GitHub runners: windows-latest, windows-2022 and ubuntu-latest (2 vCPU AMD EPYC 7763 / 9V45 / 9V74).

## 1. Verdict

**Go for the design. No-go for merging Jolt with today's package. Start the parts that don't need Jolt now.**

| Part | Verdict | Why |
|---|---|---|
| `CharacterVirtual` player movement | **Go, once a working package exists** | Matches today's movement in 16 of 16 behaviour checks. Bit-identical across runs, rebuilt worlds, different Windows CPUs and 8/60/240-tick rewinds from snapshot state only. Cheap: 32 players cost 0.06-0.10 ms per tick on proper shapes. |
| Jolt world queries (bullets, line of sight, mantle, grenades) | **Blocked** | `Ahwoo.BRUTAL.Jolt 2026.10.2` (the only version on nuget.org) ships natives with **no ray or shape query at all**, on any route: `NarrowPhaseQuery_*`, `TransformedShape_Cast*`, `BroadPhaseQuery_Cast*`, `CollisionDispatch_*` and `CharacterVirtual_CheckCollision*` are missing from both `jolt.dll` and `libjolt.so`. |
| Jolt terrain and mesh colliders | **Blocked** (a slow workaround exists) | The heightfield, mesh and convex hull constructors can't be fed data (`SetHeightSamples`, `SetTriangleVertices`, `SetIndexedTriangles`, `SetPoints` are missing). The triangle-compound workaround works but costs 6-12x more per character. |
| C# BVH world query (`BvhWorldQuery`) | **Go now** | Same answers as today's ray loop on 1000 of 1000 rays, 3-25x faster (see 2.4). It's the fallback if Jolt never works and the test oracle if it does. |
| Scene graph | **Go now** | No dependency on physics. Parenting and named attach points are proven in the spike. |
| Mesh pipeline (primitives + glTF) | **Go now** | `Brutal.Render.Mesh` primitives and `Brutal.Gltf` loading work in the published packages and feed both rendering and colliders. |

## 2. What was measured

### 2.1 Determinism (what prediction and reconciliation need)

| Test | Result |
|---|---|
| Same world, 8 bots x 600 ticks of recorded random input, every state of every bot hashed | identical |
| Same, 32 bots x 600 ticks | identical |
| Client and server each build the map (separately built world) | identical |
| World built in a shuffled order (different body IDs) | identical |
| Rewind 8 / 60 / 240 ticks to a **snapshot-only** state (position, velocity, stance) + `RefreshContacts`, replay | **bit-identical** |
| windows-latest (EPYC 9V45) vs windows-2022 (EPYC 7763): different CPU generations | **identical**, all keys |
| Separate CI runs on fresh runners (Jolt spike runs 39396724142 and 39397105088) | identical Windows hashes (`81fca6a7e89876e4`, `13dd08762cc17d0f`) |
| Windows vs Linux | **different**: the Linux native is built with SSE4.1/4.2, the Windows one with SSE2 only, and neither with `JPH_CROSS_PLATFORM_DETERMINISTIC` |

Every Windows player runs the same `jolt.dll` with no CPU-dependent code paths, so Windows clients and a Windows
server agree bit for bit. A Linux server or client would need natives built for cross-platform determinism.

### 2.2 Movement behaviour (16/16 checks pass with the tuned settings)

Walk and sprint distances equal the game formula (5.620 m in 1 s). 0.4 m stairs climb, a single 0.55 m step climbs,
a 0.70 m step blocks. A 30° ramp holds still and walks up, a 50° ramp slides and can't be climbed. Jump apex 0.950 m
(game: 0.95). Crouch under a 1.4 m ceiling, standing up refused by `SetShape(maxPenetration 0)`, stands up once clear,
prone and back. Carried by a moving platform (3.31 m vs 3.33 m) and a spinning one.

**Step height needed tuning.** With Jolt's default 0.02 m minimum forward step, `WalkStairs` lands the capsule's
round bottom on the step's edge, which counts as too steep, so nothing above 0.4 m climbs (only 4 of 139 attempts
succeed). Sweep of single steps 0.30-0.70 m:

| Settings | Climbs |
|---|---|
| Jolt default (min forward 0.02, step-up 0.6), native or C# | 0.30 0.40 |
| **min forward 0.3, step-up 0.5 (native `ExtendedUpdate`)** | **0.30-0.60, blocks 0.70** |
| min forward 0.3, step-up 0.6 | everything, including 0.70 (too much) |
| min forward 0.3, step-up 0.45 / 0.4 | up to 0.55 / up to 0.50 |
| own C# step (sweep up, forward, snap down) at 0.55 or 0.6 | 0.30-0.60, blocks 0.70 |

The capsule's round bottom rolls over a few extra centimetres, so the effective step is about 0.6 m, against today's
exact 0.55 m. Maps only use 0.4 m stairs. The native `ExtendedUpdate` with the tuned settings works, so no custom step
code is needed.

### 2.3 Cost per tick (60 Hz, 16.7 ms budget)

| Measurement | windows-latest | windows-2022 | ubuntu |
|---|---|---|---|
| **32 characters, proper shapes (box ground), per tick** mean / p99 | **68 / 98 µs** (2.1 µs each) | 97 / 172 µs | 57 / 78 µs |
| 32 characters on the triangle-compound terrain workaround, mean / p99 | 591 / 1010 µs (18.5 µs each) | 790 / 1529 µs | 403 / 647 µs |
| 8 characters, box ground / triangle terrain, mean | 12 / 155 µs | 18 / 264 µs | 12 / 103 µs |
| Client reconcile: restore + replay 240 ticks, 1 character (triangle terrain) | 5.3 ms (22 µs per tick) | 5.4 ms | 3.0 ms |
| Stance shape swap | 4-9 µs | | |
| `CharacterVirtual` create | 8-15 µs | | |

A full 32-player server spends well under 1% of its tick on movement with real shapes, and under 10% even on the
workaround terrain. Typical reconciles replay 6-15 ticks (0.1-0.3 ms on the workaround, about 0.03 ms on real shapes).

### 2.4 Ray casts

| Per ray, 1000 random rays up to 200 m | windows-latest | windows-2022 | ubuntu |
|---|---|---|---|
| Today's `RaycastWorld` loop, 609 boxes | 13.1 µs | 16.7 µs | 12.0 µs |
| **C# BVH**, 609 boxes (449 nodes, built in 3-6 ms) | **1.9 µs** | 4.0 µs | 2.0 µs |
| Today's loop, 5009 boxes | 18.4 µs | 31.4 µs | 16.7 µs |
| **C# BVH**, 5009 boxes | **0.7 µs** | 1.0 µs | 1.2 µs |
| Jolt `CastRay` | not available in the package | | |
| Lag compensation: 8 historic hitboxes by hand, per shot | 1.0 µs | 2.4 µs | 1.1 µs |

The BVH returned the same distance as today's loop on all 1000 rays. Today's loop grows linearly with box count; the
BVH barely grows. Lag compensation should stay outside physics (C# against historic hitboxes, as today).

### 2.5 Assets and scene graph

A .glb with a node hierarchy loads through `Brutal.Gltf`, vertices and indices are read through buffer views, the
`muzzle` attach point is found by name, and a child node lands at the right world position after its parent turns.
A `ProcGenMesh` cylinder (147 vertices, 288 indices) becomes a Jolt collider. A cold first glTF load takes 90-165 ms
(JIT and JSON warm-up). Walking 10k scene nodes costs 110-175 µs even when nothing moved and 4-7 ms when everything
did, so the scene graph must update only dirty subtrees and bake static nodes at load.

## 3. Port plan (ordered, each step merges on its own with CI green)

Steps 1-5 don't need Jolt and can start now. Steps 6-8 wait for a package that passes the canary in step 1.

1. **Jolt canary (0.5 day).** A `Jolt` section in `SelfTest` that calls every API the port needs (shape settings,
   heightfield with samples, mesh shape with triangles, `CastRay`, `CastShape`, `CharacterVirtual.ExtendedUpdate`,
   `SetShape`, `RefreshContacts`) and fails on `EntryPointNotFoundException` or a zeroed result, plus a golden
   determinism hash. It goes in behind a package reference only when step 6 starts; until then `spike.yml` is the canary.
2. **`IWorldQuery` + `BvhWorldQuery` (3-4 days).** One interface: `Raycast`, `SphereCast`, `OverlapCapsule` (and the
   `Blocked` box test). The BVH covers map boxes; terrain keeps going through the existing `ITerrainCollision`. Route
   `World.RaycastWorld`/`RaycastBoxes`, `Blocked`/`BlockedByBoxes`, `TryMantle`'s box scan, `CanWalkStraight`,
   `Throwables` collision, `Melee` and bot line of sight through it. A/B self-test against the old loops on random rays
   on all maps. No gameplay change; bullets and bots get cheaper.
3. **Movement seam (1 day).** `World.Simulate` calls an `IMover` (`ClassicMover` = today's `Move`, unchanged). Pure
   refactor: identical self-test results, same `MoveState`, same snapshots.
4. **Scene graph core (3-4 days).** New `Source/Scene`: `SceneNode` (local TRS, parent, children, cached world
   matrix, dirty-subtree updates), `Scene` (roots, name index, static baking), components (`MeshRenderer`, `Collider`,
   `AttachPoint`, `Mover`, markers). The map loader builds nodes from v1 boxes and terrain. The renderer and physics
   still read `Map.Solid`, so nothing visible changes.
5. **Mesh pipeline, render side (6-8 days).** `MeshPool` (shared VBO/IBO arenas), primitives (unit cube, cylinder,
   sphere, wedge, capsule) cached by parameters, glTF import via `Brutal.Gltf`, an instanced lit shader using the
   lighting block shared with foliage and terrain, static batches per 32 m chunk with frustum culling, and render stats
   in the log. `Program.DrawArena`'s per-box loop is replaced. Acceptance: smoke-test screenshots match today's, fps
   no lower on the user's PC.
6. **Jolt world, gated (3-4 days).** When a BRUTAL release passes step 1: add the package, build static bodies from
   the scene's colliders (boxes as one `StaticCompoundShape` per chunk, terrain as one `HeightFieldShape` from
   `TerrainData.Heights`), kinematic movers posed as a pure function of the tick, and `JoltWorldQuery` as a second
   `IWorldQuery` that the self-test compares with the BVH.
7. **`JoltMover` behind a server setting (6-8 days).** `physics = classic | jolt`, chosen by the server and sent in
   the Welcome. Same `MoveState` in and out; see section 4 for the rules. Self-tests: recorded-input A/B on every map
   (trajectories close on flat ground, same stair tops, no fall-through), replay bit-exact, 8 bots x 1 min with nobody
   stuck. Then playtests.
8. **Switch the default, delete the old code (1-2 days).** Remove `Move`, `MoveAxis` and the box branches from
   `World.cs`. The BVH stays as the fallback query and test oracle.

After step 5 (render) and step 2 (queries), and in parallel with 6-8:

9. **Map JSON v2 (6-8 days).** `"format": "call-of-slop-map/2"`, a node tree with `mesh` / `model` / `prefab` / `op`
   per node, `collider`, `static`, `mover`, plus the existing terrain and foliage sections. v1 files keep loading
   unchanged (their ops produce boxes under an `ops` node). `--check-map` validates v2; `Maps/README.md` gets a v2
   section. Details: design doc section 5.1.
10. **Weapon and character hierarchies (6-10 days).** Guns as data (glTF or primitive children) with `muzzle`,
    `optic_rail`, `underbarrel`, `mag`, `grip_*` sockets; attachments snap onto sockets; arms by two-bone IK.
    Details: design doc sections 5.2 and 5.3.

### What replaces `World.cs` physics

| Today | After the port |
|---|---|
| `Move` / `MoveAxis` (box-by-box axis sweep, step-up) | `JoltMover`: `CharacterVirtual.ExtendedUpdate` with step-up 0.5, min forward 0.3, stick-to-floor 0.5 |
| `Blocked` (stand-up headroom) | `CharacterVirtual.SetShape(stance, maxPenetration 0)` refuses the swap |
| `TryMantle` box scan | `IWorldQuery.SphereCast` down in front + `OverlapCapsule` at the target; the 10-28-tick glide stays |
| `RaycastWorld` / `RaycastBoxes` | `IWorldQuery.Raycast` (BVH now, Jolt later) |
| `ClampToArena` | invisible perimeter boxes (or keep the clamp after the update) |
| `Hitbox` / `IsHeadshot` | unchanged C# boxes against the history buffer |
| `MaxSlopeDeg` 42° | `CharacterVirtualSettings.SetMaxSlopeAngle(42°)` |

## 4. Prediction and rewind with Jolt

1. The server owns one `CharacterVirtual` per player. A client owns only its local player's; remote players stay
   interpolated snapshots as today.
2. Snapshots don't change: position, velocity, grounded, stance, mantle ticks and target, boost ticks, movement perk.
   Jolt's contact cache is never sent.
3. Reconcile: `SetShape(stance)`, `SetPosition`, `SetLinearVelocity`, `RefreshContacts()`, then replay the pending
   inputs through the same step. Measured bit-identical for 8, 60 and 240 ticks.
4. Everything that can move a player must be a pure function of the server tick (lifts, doors, platforms: pose them
   for tick `t` before stepping tick `t`). Pushable or physics-driven objects stay out of the predicted world:
   cosmetic debris lives on a client-only layer that characters don't collide with.
5. Movement math in C# around Jolt avoids `MathF.Sin/Cos/Exp` on per-tick values (precomputed constants, as the
   spike's `DMath` does). Today every client is Windows, so this only matters for a future Linux server.
6. Cap replays: over 60 pending inputs, snap to the server state (the game already snaps on corrections over 2 m).
7. The version handshake also compares a hash of the Jolt native, so a client with a different `jolt.dll` can't join.

## 5. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| BRUTAL never ships matching Jolt natives | real (it's the state today) | Steps 2-5, 9, 10 don't need Jolt. If it never comes, generalise `ClassicMover` to oriented boxes and triangles over the BVH: no Jolt, same determinism story as today. |
| A BRUTAL update changes compiler flags and old and new clients disagree | medium | Native hash in the handshake; golden hash in the self-test. |
| Feel changes: effective step ~0.6 m instead of 0.55, capsule edges instead of box edges, slope sliding | medium | A/B flag in step 7, playtests, tune `CharacterPadding` and stick-to-floor. |
| Terrain cost on the workaround (6-12x per character) | only if we ship before a working heightfield | Don't ship step 7 on terrain maps until `HeightFieldShape` works; box maps are fine today. |
| Scene-graph per-frame cost | low | Dirty-subtree updates and static baking (a full walk of 10k nodes is 4-7 ms). |
| Linux server or clients | low (not a target) | Needs cross-platform-deterministic natives. |
| Mesh pipeline changes the look | medium | Screenshot parity in the smoke test before the old path is removed. |

## 6. Decisions needed from you

1. **Start the Jolt-independent work now?** The C# BVH query, the scene graph and the mesh pipeline (steps 2-5).
   *Recommended: yes.*
2. **How do we get working Jolt natives?** Report the mismatch to BRUTAL and wait, or build our own Jolt natives?
   Building our own is several days of C++ build work, and we'd own it. *Recommended: report it and wait; don't block
   on it.*
3. **If Jolt stays broken, fall back to our own C# collision?** Extend today's movement to oriented boxes and triangles
   over the BVH, and set a date to decide. *Recommended: yes, decide after steps 2-5 land.*
4. **Step height: accept about 0.6 m** (climbs 0.55, blocks 0.70) instead of exactly 0.55 m? *Recommended: yes.*
5. **Linux dedicated server ever?** If yes, cross-platform determinism becomes a requirement for the natives.
   *Recommended: no, Windows only.*
6. **Keep players passing through each other and physics props cosmetic-only**, as today? *Recommended: yes for both;
   anything else makes prediction much harder.*
