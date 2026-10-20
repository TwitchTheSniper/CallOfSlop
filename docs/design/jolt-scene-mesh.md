# Jolt physics, scene graph and mesh pipeline: design and spike results

Status: design reference, backed by a working spike (`spike/JoltSpike`, CI workflow `.github/workflows/spike.yml`).
Branch `spike/jolt-scene`. Nothing here changes the game yet. **The decision, port plan, risks and the questions for
the user are in [jolt-recommendation.md](jolt-recommendation.md)**; where the two differ, the recommendation is newer.

## 0. TL;DR

| Question | Answer (measured) |
|---|---|
| Does Jolt's `CharacterVirtual` reproduce our movement? | **Yes, 16/16 checks.** Walk/sprint distances match the game formula exactly; stairs, a single 0.55 m step (blocks 0.70), 30° ramps, 50° slides, jump apex (0.950 m vs the game's 0.95), crouch under a ceiling with the stand-up refused, prone, moving and spinning platforms all work. The step needed tuning: minimum forward step 0.3, step-up 0.5 (see 4.3). |
| Is it deterministic enough for prediction and reconciliation? | **Yes, on Windows.** Two runs in one process, a separately built world and a world built in shuffled order all give bit-identical hashes. Rewinding 8, 60 or 240 ticks from snapshot-only state (position, velocity, stance) and replaying gives **bit-identical** results. Two different Windows runner images on different CPU generations (EPYC 7763 and 9V45) give the same hash, also for 32 characters and across separate CI runs. **Linux gives a different hash**: its native library is compiled differently. |
| Is it fast enough? | **Yes.** 32 characters on box geometry cost 57-97 µs per tick (about 2-3 µs each). On the triangle-compound fallback terrain (see below) it's 6-12x slower: 400-790 µs per tick for 32 characters, 3-5.4 ms to replay 240 ticks for one. Both are small against the 16.7 ms tick. |
| Can Jolt do our ray casts? | **Not with the published package.** `Ahwoo.BRUTAL.Jolt 2026.10.2` (still the only version on nuget.org) ships natives with no ray or shape query on **any** route: `NarrowPhaseQuery_*`, `TransformedShape_Cast*`, `BroadPhaseQuery_Cast*`, `CollisionDispatch_*`, `CharacterVirtual_CheckCollision*` are missing from both `jolt.dll` and `libjolt.so`. The heightfield, mesh and convex hull data setters and `CharacterVirtual.SaveState/RestoreState` are missing too: 1,691 of the 4,087 entry points the managed binding calls have no native export. A C# BVH answers the same rays 3-25x faster than today's loop. |
| Does glTF loading work? | **Yes.** `Brutal.Gltf` loads a .glb with a node hierarchy, and positions/indices go straight into Jolt colliders. The first load costs about 180 ms because of JIT and JSON warm-up. |
| Recommendation | See [jolt-recommendation.md](jolt-recommendation.md): go for the design, no-go for merging Jolt with today's package; start the BVH query, scene graph and mesh pipeline now. |

## 1. What the spike is

`spike/JoltSpike` is a standalone .NET 10 console app that references `Ahwoo.BRUTAL.Jolt 2026.10.2`,
`Ahwoo.BRUTAL.Gltf 2026.10.2` and `Ahwoo.BRUTAL.Render.Mesh 2026.10.1` (the only Jolt version on nuget.org).
It runs six sections as separate processes, so a native crash only takes out one section:

| Section | What it tests |
|---|---|
| a | World setup: 600 random boxes, a 256x256 terrain, cylinder, convex hull, triangle mesh, kinematic platforms. Standing on terrain. Ray casts. Binding audit. |
| b | CharacterVirtual movement: walk, sprint, stairs, single steps, ramps, jump, crouch/stand headroom, prone, moving and rotating platforms, mantle probe, step-height sweep. |
| c | Determinism: 8 bots × 600 ticks of recorded random input (hash of every state, every tick), reconciliation replays, separately built and shuffled worlds. Movement performance. |
| d | Ray-cast and world-build performance. |
| e | Lag compensation: historic capsules by hand vs a Jolt hitbox layer. |
| f | glTF load, scene graph, ProcGenMesh → colliders. |

The workflow runs on `windows-latest`, `windows-2022` and `ubuntu-latest`. A compare job diffs the
determinism hashes, and everything is reported as annotations.

The spike's movement code (`Sim.cs`, `Mover`) is the game's `World.Simulate` rules (same constants, same
acceleration blend, same jump integration) with Jolt doing the collision. Inputs and terrain use no CRT
trig (`DMath`), so the cross-OS comparison isolates Jolt itself.

## 2. Measured results

Machines: GitHub runners, 2 vCPU (AMD EPYC 7763 / 9V45, Intel Xeon 8573C depending on the run). Times are
best or median of several reps after warm-up. Treat them as relative: a gaming PC is 1.5-3x faster per core.

### 2.1 Movement behaviour (section b, all three OSes identical)

| Check | Result |
|---|---|
| Walk 1 s (6 m/s) | 5.620 m, game formula 5.620 m ✔ |
| Sprint 1 s (9 m/s) | 8.801 m ✔ |
| 0.4 m stairs | Reaches the top (y = 1.600) ✔ |
| 0.70 m step | Blocks ✔ |
| Single 0.55 m step | ✔ with min forward step 0.3 and step-up 0.5 (Jolt's default 0.02 m forward step blocks everything above 0.4 m; see 4.3) |
| Stand still on a 30° ramp | Drift 0.000 m in 2 s, OnGround ✔ |
| Walk up a 30° ramp | ✔ |
| 50° ramp | OnSteepGround, slides 4.3 m in 2 s, can't be walked up ✔ |
| Jump apex | 0.950 m (game 0.95), lands ✔ |
| Crouch under a 1.4 m ceiling | Stays crouched when crouch is released; `SetShape(maxPenetration 0)` refuses to stand; stands up automatically once clear ✔ |
| Prone and back | ✔ |
| Moving platform (2 m/s) | Carried 3.314 m in 100 ticks (platform moved 3.333 m) ✔ |
| Spinning platform (1 rad/s) | Radius 2.72 (started at 2.5), turned 1.457 rad (platform 1.5) ✔ |
| Mantle ledge probe | **Blocked**: needs `CastShape`/`CheckCollision`, which have no native export (reported as a warning, not a failure) |
| Stance shape swap | 4-9 µs |

### 2.2 Determinism (section c)

| Test | Windows | Linux |
|---|---|---|
| Same world, run twice (8 bots × 600 ticks, every state hashed) | identical | identical |
| Separately built world (client and server each load the map) | identical | identical |
| World built in shuffled order (different body IDs) | identical | identical |
| Rewind 8 / 60 / 240 ticks from **snapshot state only** (pos, vel, stance) + `RefreshContacts`, replay | **bit-identical** | **bit-identical** |
| Rewind from full Jolt character state | not testable (`SaveState`/`RestoreState` missing natively) | same |
| 32 bots x 600 ticks, two separately built worlds | identical | identical |
| windows-latest vs windows-2022 (EPYC 9V45 vs 7763) | **IDENTICAL** (all keys) | |
| Separate CI runs | identical hashes run to run | identical |
| windows-latest vs ubuntu-latest | | **different** (all hashes) |

Why Linux differs: the Jolt build strings are
`Windows: Single precision x86 64-bit with instructions: SSE2 (FP Exceptions) ...` and
`Linux: ... SSE2 SSE4.1 SSE4.2 ...`. Different instruction sets, a different compiler, and no
`JPH_CROSS_PLATFORM_DETERMINISTIC` in either build. Windows clients all run the same `jolt.dll` with
SSE2 only, so there are no CPU-dependent code paths, and Windows players agree with each other. A Linux
host or client would need a build with cross-platform determinism. Even without it, the mismatch would
only cause small server corrections (reconciliation always restarts from server state), not desyncs.

The snapshot-only result matters most: the game's snapshot already carries exactly position, velocity,
grounded and stance. Jolt's internal contact cache doesn't need to be sent, because `RefreshContacts()`
rebuilds it identically.

### 2.3 Performance

Latest numbers (Jolt spike run 39396724142). Sections c and d of each OS job print them.

| Measurement | windows-latest | windows-2022 | ubuntu-latest |
|---|---|---|---|
| 32 chars per tick, box ground (mean / p99) | 68 / 98 µs | 97 / 172 µs | 57 / 78 µs |
| 32 chars per tick, triangle-compound terrain (mean / p99) | 591 / 1010 µs | 790 / 1529 µs | 403 / 647 µs |
| 8 chars per tick, box ground / triangle terrain (mean) | 12 / 155 µs | 18 / 264 µs | 12 / 103 µs |
| Client reconcile: restore + replay 240 ticks, 1 char (triangle terrain) | 5.3 ms | 5.4 ms | 3.0 ms |
| 8 chars x 240 ticks (server-side worst case, triangle terrain) | 31 ms | 37 ms | 20 ms |
| CharacterVirtual create | 13 µs | 15 µs | 8 µs |
| Ray, today's C# loop, 609 / 5009 boxes | 13.1 / 18.4 µs | 16.7 / 31.4 µs | 12.0 / 16.7 µs |
| Ray, C# BVH (`Bvh.cs`), 609 / 5009 boxes | 1.9 / 0.7 µs | 4.0 / 1.0 µs | 2.0 / 1.2 µs |
| Ray via Jolt | **unavailable** | | |
| World build: 600 boxes + 256² terrain (fallback) + shapes | 138 ms | 178 ms | 93 ms |
| 5000 boxes as separate bodies (+ terrain) / as one StaticCompoundShape | 142 / 54 ms | 194 / 70 ms | 85 / 38 ms |
| 20k-triangle "mesh" (triangle-compound fallback) | 14 ms | 13 ms | 11 ms |
| 1024² heightfield | **unavailable** | | |
| Lag comp, 8 historic capsules by hand, per shot | 1.0 µs | 2.4 µs | 1.1 µs |
| glTF load + read (first, cold) | 131 ms | 164 ms | 86 ms |
| Scene graph, 10k nodes: walk clean / 1 prop dirty / root dirty | 160 / 240 µs / 7.2 ms | 175 / 331 µs / 4.1 ms | 111 / 181 µs / 5.5 ms |

What the numbers say:

* **CharacterVirtual is cheap when the geometry is good:** about 2-3 µs per character per tick, so 32 players
  cost under 0.1 ms. The 6-12x penalty comes entirely from the stop-gap terrain built from 130k individual
  `TriangleShape`s. A real
  `HeightFieldShape` (or `MeshShape`) is expected to cost about the same as boxes.
* **Reconciliation is affordable:** typical replays are 6-15 ticks (100-250 ms RTT), so 15-350 µs
  even on the fallback terrain. The 240-tick worst case needs a cap (see 4.4).
* **Today's ray cast is O(boxes):** 12-17 µs per ray at 609 boxes, 17-31 µs at 5009. The spike's C# BVH gives the
  same answer on 1000 of 1000 rays at 0.7-4 µs, which matters for shotguns, bots' line-of-sight checks and bigger maps.
* **Static compounds build 2.5x faster** than separate bodies. Use one compound per map chunk for static props.
* **The scene graph must not walk everything every frame:** walking 10k class nodes costs 110-175 µs and recomputing
  them all costs 4-7 ms. Static nodes get flattened at load time (see 3.1).

### 2.4 The binding problem (blocker)

The managed `Brutal.Jolt.dll` in 2026.10.2 is generated from a newer header set than the native
`jolt.dll`/`libjolt.so` in the same package. The spike's audit step (`nm` on the native library vs
the entry-point strings in the managed DLL) counts **1,691 of 4,087** managed entry points with no
native export. It's worse than "missing": overloads are numbered (`_Create`, `_Create_2`, ...) and
the native side has fewer of them, so **some managed calls silently bind to a different native
overload**:

| API | What happens |
|---|---|
| `NarrowPhaseQuery.CastRay/CastRayClosest/CastShape/CollideShape/...` | `EntryPointNotFoundException`. **No Jolt queries at all.** Only `NarrowPhaseQuery_Init` exists. The other routes are missing too: `TransformedShape_CastRay/CastShape/CollideShape/CollidePoint`, `BroadPhaseQuery_CastRay/CollidePoint`, every `CollisionDispatch_*` query and `Shape_CollidePoint` (audit step, both natives). |
| `HeightFieldShapeSettings.SetHeightSamples`, `MeshShapeSettings.SetTriangleVertices/SetIndexedTriangles`, `ConvexHullShapeSettings.SetPoints` | missing, so the default-constructed settings can't be filled in either |
| `CharacterVirtual.SaveState/RestoreState/CheckCollision*`, `CharacterBaseSettings.SetSupportingVolume` | missing |
| `ConvexHullShapeSettings.Create(points)` | binds to the native **default constructor**: "Need at least 3 points" |
| `MeshShapeSettings.Create(triangles)` | default constructor: "Need triangles"; the indexed overload is missing |
| `HeightFieldShapeSettings.Create(samples, ...)` | default constructor: "Sample count too low"; `Create()` and the sample setters are missing |
| `IndexedTriangle.Create(i1, i2, i3, ...)`, `Triangle.Create(v1, v2, v3)` | return **zeroed** structs (degenerate triangles that collide with nothing, silently) |
| `BoxShape/CapsuleShape/CylinderShape/SphereShape/RotatedTranslatedShape.Create(params)` | missing (`_Create_3`); the `XxxShapeSettings.Create(params).CreateShape()` route works |
| Works: PhysicsSystem, BodyInterface (create, set pose and velocity), CharacterVirtual (`ExtendedUpdate`, `SetShape`, `RefreshContacts`, ground state), `StaticCompoundShapeSettings` + `TriangleShapeSettings`, shape settings for box/capsule/cylinder/sphere/rotated-translated, `Jolt.RayCapsule` and the other ray-primitive helpers | |

The spike works around all of this (`Phys.cs`: settings-based constructors, C#-built triangle
structs, terrain/mesh/hull as `StaticCompoundShape`s of `TriangleShapeSettings`, with a drop-a-player
probe to prove they collide). **These are workarounds, not a foundation.** Before any game code depends
on Jolt we need a BRUTAL release whose natives match its managed binding, and a binding smoke test in our self-test
(section 7, P0).

## 3. Architecture

### 3.1 Scene graph

```
SceneNode
  Name, Tag (interned string / enum), Flags: Static | Hidden | NoCollide
  Parent, Children (List<SceneNode>, usually small)
  LocalPosition (Vector3), LocalRotation (Quaternion), LocalScale (Vector3)
  World (Matrix4x4, cached), WorldVersion (int)
  Components: small inline list (MeshRenderer, Collider, AttachPoint, Light, FoliageMarker, Mover, ...)
```

**Transform updates.** Setting a local TRS marks the node dirty and pushes it onto the scene's
`dirtyRoots` list, unless an ancestor is already dirty. `Scene.UpdateTransforms()` processes only
`dirtyRoots`: it recomputes each subtree once, bumps `WorldVersion`, and tells interested components
(MeshRenderer updates its instance slot, Collider updates its Jolt kinematic body). No full-tree walk per frame:
the spike's whole-tree walk of 10k nodes costs 174 µs even when nothing moved, and 2.2 ms when everything did.

**Static vs dynamic.**
* `Static` nodes (map geometry, props, foliage) get their world matrices computed once at load. Their
  MeshRenderers are baked into static batches (3.2) and their Colliders into static Jolt bodies (one
  `StaticCompoundShape` per 32 m chunk). After load they're *never visited again*; the nodes can even be dropped
  and only the name/tag index kept.
* Dynamic nodes are players, viewmodels, weapons, doors, platforms and effects: a few hundred at most. Updating
  them through `dirtyRoots` costs microseconds.

**Lookup.** Every prefab instance gets a `Dictionary<string, SceneNode>` of its named descendants, so
"Muzzle", "grip_left" and "optic_rail" are O(1). The spike's prototype (`Scene.cs`) proves the parenting
math: the muzzle is found by name, a suppressor attached to it, and the whole gun rotated, with the child landing at
exactly the expected world position.

**Components.**

| Component | Purpose |
|---|---|
| `MeshRenderer` | mesh asset id, material id, tint, layer (world / viewmodel / transparent), cast-shadow flag |
| `Collider` | shape description (box/sphere/capsule/cylinder/convex/mesh/heightfield/compound-from-children), physics layer, static/kinematic, optional `surface` (for footsteps and bullet impacts) |
| `AttachPoint` | named socket with a local frame (weapons, characters, props) |
| `Light` | point/spot light for the forward lighting UBO (future) |
| `FoliageMarker` | an instanced foliage set, density, bounds (feeds the foliage system) |
| `Mover` | kinematic animation that is a pure function of the server tick (doors, lifts, platforms) |
| `Spawn`, `Waypoint`, `Volume` | gameplay markers (today's `Spawns`, `Waypoints`, capture zones) |

### 3.2 Mesh pipeline

**Assets.** `MeshAsset` = one vertex format (`pos f32x3, normal snorm 10:10:10:2 or f32x3, uv f32x2`, optional
tangent), u16/u32 indices, submeshes (index range + material slot), local AABB. Sources:

* **Primitives:** `Brutal.Render.Mesh` gives plane, cube, sphere, torus, disk, cone and cylinder (verified: a
  16-segment cylinder is 147 vertices and 288 indices, and became a working Jolt collider). We write the missing ones
  in C#: capsule, wedge, arch, rounded box (small, deterministic generators in `Primitives.cs`).
  Primitives are cached by parameters (`cylinder r=0.5 h=2 seg=16`), so 200 identical pillars share one mesh.
* **glTF/.glb** via `Brutal.Gltf`: nodes become SceneNodes, meshes become MeshAssets, materials become our materials, and
  images go through `Brutal.Stb` (PNG/JPG). The published package lacks the `GetAccessorBufferSpan` helper
  the source tree has, so read accessors via buffer view + offsets (as the spike does) and handle `byteStride`,
  normalized ints and missing normals (generate them). Sparse accessors and Draco aren't supported; reject
  those with a clear error.
* `Brutal.MeshOptimizer` (optional, import time only): vertex-cache and overdraw optimisation, plus
  simplification for LODs.

**GPU buffers.** A `MeshPool` owns a few large VBO/IBO arenas (for example 32 MB each, growable) and
sub-allocates. A mesh is `(baseVertex, firstIndex, indexCount)`. One VAO per vertex format. Draws use
`glDrawElementsInstancedBaseVertex` (GL 3.2, inside our 4.1 target), so switching meshes never rebinds buffers.

**Instancing and batching.**
* Per-instance data lives in one streaming buffer: `mat3x4 world` (48 B) + `tint rgba8` + `material layer u16`
  + `flags u16`, attached with `glVertexAttribDivisor(…, 1)`.
* **Static geometry** is grouped per 32 m chunk × (mesh, material). Each group is an instance range,
  written once at load. With frustum culling per chunk AABB, the draw list is "visible chunks × groups". For
  today's 600-box maps: **one unit-cube mesh, all boxes as instances, about 1-10 draw calls instead of 600**.
* **Dynamic objects** (players, guns, effects) append instances to a per-frame list. It's sorted by
  (pipeline, mesh, material) and uploaded with `glBufferSubData` into a ring buffer (orphaning, no sync stalls).
* Transparent objects (glass, smoke) go in a separate back-to-front list. Foliage keeps its own instanced path
  from the foliage work, sharing the MeshPool and the lighting UBO.

**Boxes become a primitive.** v1 maps keep producing `Box` records. The loader turns each into a
SceneNode with `MeshRenderer(UnitCube, material, tint=color)`, scaled by the half extents, and a box `Collider`. The
renderer no longer knows what a "box" is. `Program.DrawArena`'s per-box loop is replaced by the static
batches, with identical pixels as the acceptance test (smoke-test screenshot diff).

**Materials.** A `Material` is a mapping mode (world-planar like today, triplanar for rotated/scaled
primitives, or mesh UVs for glTF), a texture-array layer (the existing procedural array from `Textures.cs`; imported
textures are resized into extra layers of a second array, for example 512², or a dedicated texture when
larger), a tint and flags (unlit, alpha-test, double-sided). The material index goes in the instance data, so a whole
chunk is one draw even with many colours.

**Shaders.** One "lit" program with a few `#define` variants (planar/triplanar/UV, alpha-test,
foliage-wind, viewmodel). Lighting comes from a **shared `Lighting` UBO (std140, binding 0)** that the
foliage branch's `Lighting.cs` fills once per frame: sun direction/colour, hemisphere ambient
(sky/ground), fog colour/range, time (for wind). Every program (world, foliage, viewmodel) includes the same
GLSL snippet, so a map's lighting looks identical on every object type. The current shader's lambert +
rim + fog is the starting point. Shadows (one cascade + PCF) are a later, optional quality setting.

**Culling and stats.** Chunk AABBs and dynamic instance AABBs are tested against the frustum on the CPU. Log
`[render] draws=.. instances=.. tris=.. gpu_ms=..` once a second via `GL_TIME_ELAPSED` queries, so the lead
can verify the 240 fps budget on the real PC.

### 3.3 Physics

**World setup** (as in `Phys.cs`):

| Object layer | Broadphase | Collides with | Used for |
|---|---|---|---|
| `Static` | static | Moving, Debris | map geometry, terrain heightfield (one per chunk set), static props |
| `Moving` | moving | Static, Moving | kinematic movers (doors, platforms), server-side physics props, grenades if they become bodies |
| `Hitbox` | query-only | nothing | reserved; lag compensation stays outside Jolt (3.3.5) |
| `Debris` | moving | Static | cosmetic client-only rigid bodies (shell casings, gibs, falling rubble) |

Characters are `CharacterVirtual` with no inner body, so bullets and other characters don't see them
(matching today: players don't collide with each other, and hitboxes are separate).

**Colliders from data.** Each Collider maps to one Jolt shape:

| Data | Jolt shape |
|---|---|
| box, sphere, capsule, cylinder | `Box/Sphere/Capsule/CylinderShapeSettings` (these settings constructors work in 2026.10.2) |
| rotated or scaled primitive | parent transform; non-uniform scale is baked into the box extents, otherwise `ScaledShape` |
| convex hull (props, rocks) | `ConvexHullShapeSettings(points)`, generated from the render mesh or a `_col` mesh in the glTF |
| triangle mesh (complex static props, terrain overhangs) | `MeshShapeSettings` |
| terrain | `HeightFieldShapeSettings` (the terrain branch's heightmap; 1 m spacing, block size 4, 8 bits per sample) |
| a static prop made of parts | children merged into one `StaticCompoundShape` per prop, or per chunk for many props |

Until fixed natives arrive, the spike's fallbacks apply: hull, mesh and terrain all become a
`StaticCompoundShape` of `TriangleShapeSettings` (correct collisions, about 10x the per-tick cost) or thin
oriented boxes.

**CharacterVirtual settings, mapped from today's constants:**

| Game (World.cs) | Jolt |
|---|---|
| `PlayerRadius 0.4`, `HeightOf(Stand/Crouch/Prone) 1.8 / 1.2 / 0.6` | Stand/crouch: capsules r 0.4, offset up so the feet sit at the origin. Prone: a 0.6 m cylinder of r 0.4 (a lying capsule would have to turn with the view and snag when turning; the long prone body stays a hitbox only). Swap with `SetShape(shape, maxPenetration: 0)` when standing up (Jolt refuses the swap if there's no headroom, replacing `Blocked()`), or `float.MaxValue` when going down. |
| `StepHeight 0.55` | `ExtendedUpdateSettings.WalkStairsStepUp = 0.5` with `WalkStairsMinStepForward = 0.3` (measured: climbs up to 0.60, blocks 0.70; see 4.3). `StickToFloorStepDown = -0.5`, `StepForwardTest 0.15`, `CosAngleForwardContact cos 75°`. The native `ExtendedUpdate` works with these. |
| `MaxSlopeDeg 42` (terrain branch) | `MaxSlopeAngle 42°` (the spike used 45°). Don't call `CancelVelocityTowardsSteepSlopes` on the wish velocity before the update: it removes the push into a step face that `WalkStairs` needs; `ExtendedUpdate` handles steep slopes itself. |
| `WalkSpeed 6, SprintSpeed 9`, stance speed factors, the 14/3 accel blend | unchanged C# (applied to velocity relative to `GetGroundVelocity()`, so platforms carry you) |
| `JumpSpeed 6, Gravity 18` | unchanged C#: on ground `vy = ground.vy (+ 6 − 18·dt on jump)`, in air `vy −= 18·dt`. No gravity while grounded, so nobody creeps down slopes. |
| `MantleReach 1.7`, `TryMantle` | ledge probe = sphere cast down in front + capsule overlap at the target. **Needs Jolt queries**; until then, use the C# `WorldQuery` (3.3.4). The 10-28-tick mantle glide stays as is (memoryless, already in snapshots). |
| `CharacterPadding` | 0.02; `PredictiveContactDistance 0.1`; `PenetrationRecoverySpeed 1`; back faces collide; enhanced internal edge removal on |
| `ClampToArena` | keep, applied after the update, or replace with invisible perimeter boxes (preferred: no special case) |

**Prediction and reconciliation with Jolt.** Measured to be bit-exact (2.2), with these rules:
1. One `CharacterVirtual` per simulated player: the server has all of them, the client has its local player only.
   Remote players stay interpolated snapshots, as today.
2. A snapshot carries `pos, vel, stance, mantleTicks, mantleTo, boostTicks, movePerk`, exactly as now. Jolt's
   contacts aren't sent.
3. To reconcile: `SetShape(stance)`, `SetPosition`, `SetLinearVelocity`, `RefreshContacts()`, then replay pending inputs
   through the same `Step()`. Kinematic movers must be posed **as a pure function of the tick** before every tick
   (the spike's `SetMovers(tick)`), and the client knows the server tick of each replayed input.
4. Anything that affects movement and isn't a function of tick + inputs (pushable props, physics doors)
   must not be in the predicted world. Those go on the `Debris` layer (cosmetic) or become server-only Moving bodies
   that characters only see through snapshots.
5. All movement math in C# avoids `MathF.Sin/Cos/Exp` on values that differ per OS (constants are precomputed). Today's
   `World.Simulate` uses them, which is fine while every client is Windows.
6. Cap the replay length: above 60 pending inputs (1 s), snap to the server state instead of replaying (the game
   already snaps above 2 m of correction).

**Lag compensation.** Keep hitboxes **out of Jolt** and test rays against historic capsules by
hand (option A). It costs 1.0-2.4 µs per shot for 8 players including interpolation (measured), never touches
physics state (no broadphase churn, safe for fractional ticks and future threading), and our C# ray-capsule test
matches `Jolt.RayCapsule` on 999 of 1000 random rays. The world ray (where the bullet stops) comes from `WorldQuery`.
The port keeps today's `World.Hitbox` boxes unchanged; capsule hitboxes (body capsule + head sphere for stand/crouch,
a horizontal capsule along the yaw + head sphere for prone) are an optional later change.
`Player.History` keeps storing feet/stance/yaw. Option B (kinematic hitbox bodies moved per shot) couldn't be
timed (no queries); it would also mutate the broadphase per shot, so it isn't recommended either way.

**WorldQuery.** One interface for every "what does this ray/shape hit" question:
```csharp
interface IWorldQuery {
    bool Raycast(Vector3 o, Vector3 dir, float max, out WorldHit hit, QueryMask mask);   // bullets, LOS, explosions
    bool SphereCast(Vector3 o, Vector3 dir, float r, float max, out WorldHit hit);       // grenades, ledge probe
    bool OverlapCapsule(Vector3 a, Vector3 b, float r, QueryMask mask);                  // mantle fit, spawn checks
}
```
Two implementations: `BvhWorldQuery` (C#: a BVH over the map's collider primitives, boxes/OBBs/triangles/heightfield
cells; can ship now and replaces the O(n) loops) and `JoltWorldQuery` (`NarrowPhaseQuery`, once natives exist).
The self-test compares them on random rays. This also gives a clean fallback if Jolt never becomes usable.

**Grenades and throwables.** Keep the custom integrator in `Throwables.Step` (server-authoritative, not
predicted, already tuned), but replace its box collision with `WorldQuery.SphereCast`. Jolt rigid bodies would
add nothing gameplay needs and lose the tuned feel. Cosmetic debris can use real Jolt dynamic bodies on the
`Debris` layer (client only; `PhysicsSystem.Update` is exported).

**Bots.** The nav graph (today `AutoWaypoints`) gets **edges validated by walking them**: at map load, drop a
`CharacterVirtual` at each node, steer it along each candidate edge for up to `distance / speed + 1 s`, and keep the
edge if it arrives. This works with the current package (no queries needed) and automatically respects
stairs, slopes and gaps, the same as the players' movement. Jump/mantle links get the same test with the jump or
mantle input. Cost about 2 µs per tick per walk on real colliders (about 1-2 s per large map on one thread); cache the
result by map file hash. Line-of-sight checks use `WorldQuery.Raycast`.

## 4. Decisions inside the design

### 4.1 Static world: bodies vs compounds
Static compounds build 2.5x faster than separate bodies (5000 boxes: 89 vs 227 ms on Windows). Use one
`StaticCompoundShape` per 32 m chunk, which matches the render chunks, so culling and physics share the
same partition. Separate bodies only for kinematic movers.

### 4.2 Terrain
`HeightFieldShape`, one per map (Jolt's heightfield is already internally blocked and very cheap), built from the
terrain branch's heightmap. Holes (tunnels) use Jolt's no-collision sample value plus a `MeshShape` for the
tunnel. Until fixed natives exist, the fallback is a triangle compound per 16x16 cells (measured to work, but about
10x the character cost).

### 4.3 Step height
Jolt's `WalkStairs` moves the character forward by at least `WalkStairsMinStepForward` after stepping up. With the
default 0.02 m the capsule's round bottom lands on the step's edge, the contact there is steeper than the max slope,
and the step is undone: only 4 of 139 attempts succeed, and nothing above 0.4 m climbs (0.4 m steps are climbed by the
round bottom sliding over the edge, without `WalkStairs`). Section b's sweep (single steps of 0.30-0.70 m):

| Settings | Climbs |
|---|---|
| default (min forward 0.02, step-up 0.6), native or C# `ExtendedUpdate` | 0.30 0.40 |
| **min forward 0.3, step-up 0.5** (native `ExtendedUpdate`) | **0.30-0.60, blocks 0.70** (chosen) |
| min forward 0.3, step-up 0.6 | all, including 0.70 |
| min forward 0.3, step-up 0.45 / 0.40 | up to 0.55 / up to 0.50 |
| own C# step (sweep up, sweep forward 0.25, snap down, undo unless higher and on walkable ground), 0.55 or 0.6 | 0.30-0.60, blocks 0.70 |

The capsule rolls over the last few centimetres, so the effective step is about 0.6 m against today's exact 0.55 m.
The spike's own step (`Mover.OwnStepUp`) is kept as a fallback; it needed a walkable-ground check after landing, or
players "stepped" up the 50° ramp.

### 4.4 Reconciliation cap
Replays above 60 ticks snap instead (see 3.3, rule 6). On good colliders 60 ticks cost about 0.12 ms.

### 4.5 Linux
Not a target for clients. If a Linux dedicated server is ever wanted, BRUTAL must ship natives built with
`JPH_CROSS_PLATFORM_DETERMINISTIC` (and the same flags on Windows), or that server must accept a constant
trickle of tiny corrections.

## 5. Data formats

### 5.1 Map JSON v2

`"format": "call-of-slop-map/2"`. v1 files keep loading unchanged: the v1 `ops` produce boxes as today, and
each box becomes a static node with a unit-cube mesh and a box collider. A v2 file may still contain `ops`
(they run first and land under a node named `ops`), so existing building helpers keep working.

```json
{
  "format": "call-of-slop-map/2",
  "name": "Quarry", "halfX": 120, "halfZ": 120, "sky": "#8c9eb8",
  "terrain": { "heightmap": "quarry_height.png", "size": 256, "spacing": 1, "heightScale": 18,
               "layers": [ { "texture": "grass", "splat": "quarry_splat.png#r" }, { "texture": "rock", "splat": "#g" } ] },
  "foliage": [ { "mesh": "models/grass_tuft.glb", "density": "quarry_splat.png#r", "perM2": 4, "maxDist": 60 } ],
  "ops": [ { "op": "RingSpawns", "count": 8, "frac": 0.8 } ],
  "prefabs": { "crateStack": { "children": [
      { "mesh": "box", "pos": [0, 0.5, 0], "scale": [1, 1, 1], "material": "wood" },
      { "mesh": "box", "pos": [0.1, 1.5, 0], "rot": [15, 0, 0], "material": "wood" } ] } },
  "objects": [
    { "name": "Hangar", "pos": [20, 0, -10], "rot": [90, 0, 0], "static": true, "children": [
        { "op": "Building", "w": 20, "d": 14, "stories": 1, "doors": "N" },
        { "prefab": "crateStack", "pos": [4, 0, 3] },
        { "model": "models/forklift.glb", "pos": [-5, 0, 2], "collider": { "type": "convex" } },
        { "mesh": "cylinder", "radius": 0.3, "height": 4, "pos": [0, 2, 0], "material": "steel", "collider": "auto" } ] },
    { "name": "Lift", "mesh": "box", "scale": [3, 0.3, 3], "pos": [0, 0.15, 30],
      "mover": { "type": "pingpong", "to": [0, 6, 30], "period": 8 } }
  ]
}
```

Rules:
* `pos` in metres, `rot` as yaw/pitch/roll in degrees (or `"quat": [x, y, z, w]`), `scale` per axis. Every node
  is relative to its parent.
* A node has at most one of: `mesh` (a primitive name plus its parameters), `model` (a .glb path; its node tree becomes
  children), `prefab` (a name from `prefabs` or a file `prefabs/x.json`), or `op` (a v1 builder op run in the node's
  local frame).
* `collider`: `"auto"` (primitives use their own shape; models use a `_col` mesh if present, else a convex hull per mesh),
  `"none"`, or `{ "type": "box|sphere|capsule|cylinder|convex|mesh", ... }` with explicit sizes.
* `static` defaults to true, so the node is baked. `mover` makes it kinematic (pure function of the tick; types:
  pingpong, loop, rotate).
* `--check-map` gains v2 validation and prints node, mesh and collider counts. `Maps/README.md` gets a v2 section.

### 5.2 Weapons as hierarchies

`WeaponFile` keeps its stats and gains a `model` block (or a .glb), replacing `GunModel`'s hard-coded box
silhouettes:

```json
"model": {
  "file": "models/ar_mk2.glb",
  "nodes": {
    "Receiver": { "attach": { "muzzle": [0, 0.02, -0.62], "optic_rail": [0, 0.07, -0.1],
                               "underbarrel": [0, -0.04, -0.4], "mag": [0, -0.06, -0.05],
                               "stock": [0, 0.01, 0.18], "grip_right": [0, -0.08, 0.05], "grip_left": [0, -0.05, -0.35] } } }
}
```
Hierarchy: `WeaponRoot (grip origin, −Z to muzzle) → Receiver → Barrel → muzzle (AttachPoint)`, with
`optic_rail`, `underbarrel`, `mag`, `stock` and `grip_*` sockets. An attachment is a small model with a `mount` node
that is snapped onto its socket, and it may expose new sockets. A suppressor exposes a new `muzzle` further out,
so the muzzle flash and tracer origin follow automatically. ADS alignment uses the optic's `sight` node instead of
`GunModel.SightHeight`. A boxes-only gun can still be described with primitive children, so the current guns can be
migrated one by one.

### 5.3 Characters

`CharacterRoot (feet) → Pelvis → Spine → Chest → {Neck → Head, ShoulderL → UpperArmL → ForearmL → HandL,
ShoulderR → UpperArmR → ForearmR → HandR}`. The weapon root attaches to `HandR` at the weapon's `grip_right`
socket. `HandL` is driven by two-bone IK to the weapon's `grip_left`. Stance changes move the pelvis and chest
(a procedural pose, no skeletal animation needed yet). First person uses the same arm and weapon subtree under a
`ViewmodelRoot` parented to the camera, drawn in its own depth range (viewmodel layer). Hitboxes come from the
pose (Chest/Head nodes), so the drawn body and the damage shapes agree.

## 6. Migration plan

**Superseded by the ordered port plan in [jolt-recommendation.md](jolt-recommendation.md), section 3.** Kept for the
effort and file estimates. Each phase keeps the game playable and CI green.

| Phase | What | Touches | Effort | Risk | Depends on |
|---|---|---|---|---|---|
| **P0** | Report the binding mismatch to BRUTAL; add a `Jolt` section to `SelfTest` that calls each API we use (shape settings create, CharacterVirtual update, a ray cast) and fails on `EntryPointNotFoundException` or a zeroed result. Keep `spike.yml` as the determinism and perf canary. | SelfTest.cs, csproj | 0.5 d | low | — |
| **P1** | Mesh pipeline + scene graph, **render only**: MeshPool, instanced lit shader, Lighting UBO shared with foliage, boxes as unit-cube instances in static chunk batches, frustum culling, render stats, smoke-test screenshot parity. Physics untouched. | Source/Render/*, Program.DrawArena, new Source/Scene/* | 6-8 d | medium (visual parity, GL state) | — (parallel with P0, P2) |
| **P2** | `IWorldQuery` + `BvhWorldQuery`; route `RaycastWorld`, `Blocked`, `TryMantle`'s box scan, grenade collision and bot LOS through it. A/B self-test: new == old on random rays over all 24 maps. Faster bullets as a side effect. | World.cs, Throwables.cs, Bots.cs | 3-4 d | low | — |
| **P2b** | `JoltWorldQuery` once a BRUTAL release passes P0's self-test. | new file | 1-2 d | low | fixed natives |
| **P3** | `JoltMover` behind a setting (`physics = classic or jolt`, chosen by the server and sent in the Welcome). Same `MoveState` in and out, mantle via WorldQuery, stance via `SetShape`. Self-tests: recorded-input A/B on every map (trajectories within 5 cm on flat ground, both reach the same stair tops, no fall-through), prediction replay bit-exact, 8 bots × 1 min with no stuck players. | World.cs (Simulate dispatch), Game.cs (Reconcile unchanged in shape), new Source/Game/JoltMover.cs | 6-8 d | **high** (feel, edge cases) | P2, fixed natives for terrain/mesh (boxes work today) |
| **P4** | Switch the default to Jolt after playtests; delete `Move/MoveAxis/OverlapsXZ` and the box branches. | World.cs | 1-2 d | medium | P3 + playtest |
| **P5** | Map v2: loader, prefabs, glTF models, collider generation, terrain/foliage sections, `--check-map` v2, README. v1 maps unchanged. | Source/Maps/*, Maps/README.md | 6-8 d | medium | P1 (render), P2 (queries), terrain branch |
| **P6** | Weapon and character hierarchies: GunModel → data, attach points, IK arms, viewmodel layer, hitboxes from the pose. | Source/Weapons/*, Program (viewmodel), Game (hitboxes) | 6-10 d | medium | P1 |

Parallel tracks: {P0, P1, P2} start now. P5 and P6 follow P1. P3 waits on P2 and the natives. P2b drops in
whenever BRUTAL ships. Critical path without the natives: P2 → P3 can run on box-only maps, but terrain maps need
either fixed natives or the 10x-slower triangle-compound fallback.

## 7. Risks and fallbacks

| Risk | Likelihood | Mitigation / fallback |
|---|---|---|
| **BRUTAL.Jolt natives stay mismatched** | already happening | P0 self-test catches it on every upgrade. Meanwhile, `BvhWorldQuery` covers all queries, and the fallback compounds cover terrain/mesh colliders. If it's never fixed: keep custom movement, generalised from AABBs to OBBs/triangles over the same BVH (the "own queries" path). |
| A BRUTAL update changes compiler flags and breaks bit-exactness between old and new clients | medium | Clients and server already must run the same build (version handshake). Add a golden determinism hash to the self-test so a changed native library is noticed in CI. |
| Windows machines with different CPUs disagree | low | Measured identical across runner images (Intel and AMD hosts in different runs); the Windows native is SSE2-only (no runtime CPU dispatch). Keep the golden hash test. |
| Linux host/client | low (not a target) | Needs cross-platform-deterministic natives; otherwise accept tiny corrections. |
| Performance on triangle-heavy maps | medium until heightfield/mesh exist | Real HeightField/MeshShape; replay cap; per-chunk static compounds. |
| Feel changes (step height, slope behaviour, edge snagging) | medium | Step sweep (4.3); A/B flag with playtests; tune `CharacterPadding`, `StickToFloor`. |
| CharacterVirtual can't be made deterministic enough | low (measured bit-exact) | Keep custom movement in C# and use Jolt (or the BVH) only for shape queries: sweep the capsule, slide, step. Same determinism story as today. |
| glTF assets heavy or slow to load | low | Load once at startup (about 180 ms cold, mostly JIT/JSON); MeshOptimizer at import; optional binary cache later. |

## 8. Open questions for the user

**Superseded by the short decision list in [jolt-recommendation.md](jolt-recommendation.md), section 6.** The step
height (question 3) is answered by the sweep in 4.3.

1. **Wait for a fixed BRUTAL.Jolt, or build the C# `BvhWorldQuery` now?** *Recommendation: build it now (P2).* It speeds
   up bullets today, unblocks mantle/grenades/bots, and stays as the fallback and test oracle.
2. **Can someone get BRUTAL to republish Jolt with matching natives** (and ideally `JPH_CROSS_PLATFORM_DETERMINISTIC`)? That's
   the single biggest unblocker. The audit list is reproducible with the `Jolt binding vs native exports` step.
3. **Step height:** keep exactly 0.55 m (may need a Jolt tweak) or accept about 0.5 m? *Recommendation: decide after the sweep
   numbers (4.3); maps only use 0.4 m stairs.*
4. **Prone collision shape:** an upright 0.6 m cylinder (recommended, simple and turn-safe) or a lying capsule that turns
   with the view (more realistic, can snag)?
5. **Player-player collision:** stay without it (as today, recommended) or add `CharacterVsCharacterCollision`?
6. **Physics props:** cosmetic only (recommended), or gameplay-relevant physics objects (much harder with prediction)?
7. **Assets:** load .glb at runtime (recommended for modding, same as JSON maps) or pre-bake to a custom binary format?
8. **Linux dedicated server:** needed at all? It decides whether cross-platform determinism is a requirement.

## Appendix A: how to rerun the spike

Push to any `spike/**` branch, or run the `Jolt spike` workflow by hand. Read the results:

```
R=$(gh run list -R TwitchTheSniper/CallOfSlop -w "Jolt spike" -L 1 --json databaseId -q '.[0].databaseId')
for J in $(gh run view $R -R TwitchTheSniper/CallOfSlop --json jobs -q '.jobs[].databaseId'); do
  gh api "repos/TwitchTheSniper/CallOfSlop/check-runs/$J/annotations?per_page=100" -q '.[] | .title+" | "+.message'
done
```

Locally: `dotnet run --project spike/JoltSpike -c Release -- <a|b|c|d|e|f>` (needs win-x64 or linux-x64).

## Appendix B: spike file map

| File | Contents |
|---|---|
| `Phys.cs` | PhysicsSystem + layers + filters, shape constructors with fallbacks, triangle-floor probe, ray cast with fallback, deterministic math, PRNG, hasher |
| `TestWorld.cs` | the test map (terrain, fixtures lanes, platforms as pure functions of the tick, random boxes) |
| `Sim.cs` | game movement constants, stance shapes, `Mover` (CharacterVirtual + game rules), snapshot restore, mantle probe |
| `TestsWorldAndMovement.cs` | sections a and b |
| `TestsDeterminism.cs` | section c |
| `TestsPerfAndLagComp.cs` | sections d and e (incl. C# ray-capsule) |
| `TestsAssets.cs` | section f (GLB writer, glTF → scene graph → colliders, ProcGenMesh) |
| `Scene.cs` | the scene graph node prototype |
| `Bvh.cs` | the C# AABB BVH (the `BvhWorldQuery` prototype), checked against today's ray loop |
| `Report.cs` | annotations, results files, timing |
