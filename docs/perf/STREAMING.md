# Asset and level streaming for very large maps: a study

Branch `study/streaming`, from `main` e1503a7. A study and a proposal, not a rewrite: what the map pipeline does
today, what it costs on the biggest maps we have, how that scales to the ten prop-heavy Space maps that are coming
(and bigger), and a staged plan, cheapest first.

Measurements were taken headlessly in the Linux agent container (shared with seven other agents; it is roughly three
to five times slower than the user's desktop and has no GPU), with a scratch self-test section that was removed
afterwards. The numbers are relative: what matters is the shape of the curves. The stress maps come from
`tools/perf/stress_grid.py` (kept on the branch).

## 1. How it works today

Nothing is streamed. A map is a whole: every map file is parsed at start-up, and when a map is played, every
renderer builds its complete GPU data for that map once, keeps it until the map changes, and culls per frame from
that whole. The server simulation (collision, nav graph) is whole too, which is what keeps it deterministic.

### 1.1 Map lifecycle

| Stage | Where | When | Notes |
| --- | --- | --- | --- |
| Discover and parse every map file | `Maps.All` -> `LoadAll` -> `LoadFolder` (`Source/Maps/Maps.cs:462`, `:518`, `:548-631`) | First touch of `Maps.All` (start-up, before the lobby), and again on every "Refresh maps" (`Maps.Reload`, `:483`) | All 98 files are read, built (every op executed) and validated (`:622`) serially on the main thread. `Reload` also re-serialises every map (`MapFile.ToJson`, `:500`) to find what changed. |
| Build one map | `MapFile.Load` (`Source/Maps/MapFile.cs:300-449`) | Per file | JSON -> ops invoked by reflection (`:423`) -> foliage planted (`:430-434`) -> movers, zombies, objectives -> `FinishDestruction` (`:446`) -> `FinishProps` (Clutter, `:447`). `HalfX`/`HalfZ` are clamped to 5..500 m (`:324-325`): the largest possible map is 1,000 x 1,000 m. |
| Props | `Map.PlaceProp` (`Source/Props/MapProps.cs:46-76`), `Clutter` (`:88-102`, at most 2,000 per op, `:97`), `FinishProps` (`:236-250`) | Load | A solid prop adds 1-6 collider boxes to `Solid` (`AddProp`, `:195-221`; `ColliderBoxes`, `:130-193`), flagged so they are not drawn (`IsPropCollider`). Collision, bullets, the nav graph and the fingerprint see props as boxes. |
| Validate | `Map.Validate` (`Maps.cs:382-418`) | Load, every map | Moves spawns and waypoints out of geometry with `IsBlocked`, a scan of every box (`:320-338`). |
| Fingerprint | `Map.ContentHash` (`MapFile.cs:80-181`) | On demand: Welcome packet, nav cache key, thumbnails | Walks every box and spawn. The nav code caches it per map object (`BotNav.cs:108-112`). |
| Physics world | `new PhysicsWorld(map)` (`Source/Physics/PhysicsWorld.cs:84-90`), BVH `Bvh.cs` | Lazily on the first collision query after `World.SetMap` (`Source/Game/World.cs:136`), on the game thread | One BVH over every box, OBB, mesh collider and barrier piece. Deterministic by construction. Broken props switch boxes off in place (`UpdateBox`); restores re-read them (`RefreshBoxes`). The old `CollisionGrid` (4 m columns, `Source/Game/CollisionGrid.cs`) is kept only for the parity test. |
| Destruction graph | `MapDestruction.StartGraph` (`Source/Maps/MapDestruction.cs:166`) | `World.SetMap` (`World.cs:102`), on a worker thread | Support links between chunks. Rebuilt per session; patches are reset on restore. |
| Nav graph | `NavGraph.Pump` (`Source/Game/BotNav.cs:123-192`), `BuildSteps` (`:225-435`) | While bots play on a new map, 3 ms per server tick (`BotMove.cs:9`), on the game thread against an as-built copy of the physics world (`PhysicsWorld.AsBuilt`) | Grid spacing `clamp(sqrt(area/1000), 3, 7)` m (`:238`): one column of probes per cell (`:243-252`), edges within 1.5 spacings, mover walks of risky links (`BotNavWalk.cs`), CSR arrays. Cached per (name, fingerprint) for the process (`:77`); never evicted. Destruction patches (`BotNavPatch.cs`) are applied on top and undone next match. |
| Render data | one `Rebuild` per renderer when `map != _map` | First frame the map is drawn (lobby background, thumbnail, or the match) | See 1.2. All on the main thread except the decal bake and the decal atlas paint. |

The host is a listen server: there is no headless or dedicated mode (`CallOfSlop host` goes through the window and
`RunLoop`, `Source/App/Program.cs:145-149`), so the host always holds the render data as well as the simulation. The
only headless path is `--selftest`, which never touches GL (renderers are built lazily on first `Draw`).

Maps ship with the build. Joining sends only the map name and fingerprint (`Game.cs:1552`); a client without the file,
or with a different fingerprint, is refused (`MapVote.cs:381-389`). Nothing map-sized ever crosses the wire: the
join-time state is the broken prop list (2 bytes per broken prop, `GameProps.cs:318-348`), doors and windows, and
broken destructible chunks.

The scene graph (`Source/Scene`) is not involved: it holds rigs (guns, arms, players, movers), not map geometry
(`Source/Scene/README.md`).

### 1.2 Renderers

Each row: what it builds, how big, how it draws, how it culls, what scales with what.

| Renderer | Built from | CPU/GPU data | Draw and cull | Scales with |
| --- | --- | --- | --- | --- |
| **Map boxes** (`DrawArena`, `Program.cs:1285-1300`, box loop `:1289-1291`; `Renderer.Draw`, `Source/Render/Renderer.cs:389-412`) | `Map.Solid` where `IsDrawnBox` (`MapEdges.cs:48`): not prop colliders, building shells, hidden colliders, Zombies doors or the boundary floor | None: the unit cube, four uniform uploads and one `glDrawArrays` per box every frame | **One draw call per box, no culling at all.** | Linearly with drawn-one-by-one boxes; the map authors keep that under ~600 on the shipped big maps (tree trunks are among them, `Foliage.cs:390`). |
| **Buildings** (`Source/Render/BuildingRenderer.cs`) | `BuildingLooks` per shell box | Static instance buffer, 64 B per box (`BuildStatic`, `:232-250`); a small dynamic buffer for doors and glass | One instanced run per building, frustum-culled per building (`:157-181`), visible neighbours merged. Destroyed chunks are rewritten in place (`RefreshDestroyed`). | Instances with shell boxes (25k on Silo, 40k on Paris = 2.5 MB); draw calls with buildings in view. |
| **Props** (`PropRenderer.cs`, `PropBatches.cs`, `PropMeshes.cs`) | The whole prop library once (`UploadMeshes`, `PropRenderer.cs:865-899`: 393k vertices, 196k triangles, 20.7 MB, one VBO/IBO); per map a static instance buffer of 32 B per prop sorted by type then 24 m chunk in snake order (`PropBatches.Build`, `:39-121`) | 32 B per prop; chunk table | Per frame, for every type, every chunk: nearest-distance test against the type's fade distance (small cosmetic props only; solid props never fade), frustum test, runs merged into one `glDrawElementsInstanced` (`DrawChunks`, `:590-627`). Broken props are scale 0 in the shader; `ApplyBroken` rewrites the buffer when `PropBreakVersion` changes (`:426-433`). Animated props get a second pass within their freeze distance. | CPU per frame with the number of chunks (types x occupied cells), not props; GPU with visible instances x their triangles, with no LOD and no far cut for solid props. |
| **Meshes and models** (`Source/Render/Mesh/MeshRenderer.cs`) | `MeshPlacements` (cap 20,000, `MapMeshes.cs:32`) into one `MeshPool` | Per-part bounding spheres and slots; a streamed instance buffer (64 B per visible part) orphaned and uploaded every frame (`:240-250`) | Per frame, **every part** is sphere-vs-frustum tested and given a LOD from its screen size (`:188-212`), bucketed by (mesh, LOD), one instanced draw per bucket. | CPU per frame linearly with parts (3,747 on Stratos Station: fine; 75k would be 1-2 ms and 5 MB uploaded per frame). |
| **Decals** (`DecalRenderer.cs`, `DecalBaker.cs`, `MapDecals.cs`, `DecalAtlas.cs`) | Baked on a worker thread the first time the map is drawn (`DecalMeshIfReady`, `MapDecals.cs:167-190`): a private `PhysicsWorld` over the as-built boxes (`DecalBaker.cs:76-96`), one static vertex buffer of 60 B per vertex, sorted by blend then 24 m chunk (`:58`), capped at 400k vertices (`:61`); a 128 px RGBA8 layer per decal in one array texture (`DecalAtlas.cs:19`, at most 2,048 layers, `:27`), global layers reused, the map's pack layers painted on a worker (`DecalRenderer.cs:395-430`) | 1.7-5.3 MB per map here; atlas 64 KB per layer plus mipmaps | Chunks past their fade distance or outside the frustum are skipped, neighbours merged (`:276-290`); decals on a broken or open box are hidden through a 1-bit-per-box "alive" texture (1,024 boxes per row). Impacts are a separate fixed ring. | Bake time with decals x surface complexity (0.008 ms each on a flat yard, 0.5 ms each on Paris); GPU memory with placed vertices; per-map cap 6,000 decals (`MapDecals.cs:34`), 1,024 different pack decals per map (`DecalPacks.cs:84`). |
| **Foliage** (`FoliageRenderer.cs`, `FoliageBatches.cs`, `Source/Maps/Foliage.cs`) | Planted at load (`PlantFoliage`), cap 400k plants (`Foliage.cs:181`); per map and quality a static instance buffer, 32 B per plant, by type then 16 m chunk (`FoliageBatches.cs:21`) | 1-1.6 MB on the forest maps | Chunk frustum and fade tests like props (`FoliageRenderer.cs:265`), per-plant dither fade and near/far mesh cross-fade in the vertex shader. Trunks are solid boxes drawn one by one. | Planting time with plant count (0.3-0.6 s on the forest maps); per frame with chunks. |
| **Terrain** (`TerrainRenderer.cs`) | One vertex buffer for the grid, an index buffer laid out in 32 x 32-cell chunks (`:8-18`) | ~40 B per sample (120k samples on London = 5 MB) | Frustum-culled per chunk, neighbours merged. | Memory with samples (area / cell^2); one build per map on the main thread. |
| **Textures** (`Textures.cs`) | Generated once at start-up: 64 x 64 texels x `Material.Count` layers (`:25-31`) | Tiny | One array texture bound for every box-like shader. | Not with maps at all. |
| **Sky** (`Source/Render/Sky`) | Per map: a colour or a panorama (2048 x 1024 on Stratos) | A few MB | n/a | With distinct skies in use (one at a time). |
| **Concealment** (`Source/Props/MapConcealment.cs`) | Concealing props' bounds into 8 m cells on first bot sight query | Small | n/a (bots' line of sight) | With concealing props. |
| **Thumbnails** (`MapThumbnails.cs:122-175`) | The map select screen queues one map per frame; each render uses separate renderer instances, so each **rebuilds every render structure of that map** (prop batches, mesh buckets, foliage batches, terrain mesh), draws, then `glFinish` | A 384 x 216 texture per map seen | One per frame while the screen scrolls. | A frame hitch per map proportional to its whole build cost. |

The lobby background does the same rebuild on the real renderers: `RenderLobbyBackground` calls `World.SetMap` on the
previewed map (`Program.cs:1261-1262`), and with "random from pack" it switches maps every 12 seconds (`:1260`), which
also makes the decal bake and the destruction graph start for each of them.

### 1.3 Runtime changes the renderers already track

These are the hooks any chunking or streaming has to keep:

- Props breaking or respawning: `Map.PropBreakVersion` (`MapProps.cs:532`) -> `PropBatches.ApplyBroken` and one
  `glBufferSubData`; `World.SolidChanged` -> physics box off, `DecalEvents` hides decals on it.
- Building doors, windows, destructible chunks: `BuildingStateVersion`, `MapDestruction.Version` -> dynamic buffer or
  run rewrite; nav patches (`BotNavPatch.cs`, `GameDestructionNav.cs`) on top of the cached graph; all undone by
  `RestoreProps` / `RestoreBuildings` / `RestoreDestruction` at the next match.
- Map reload: `Maps.Reload` swaps the list; a map in play keeps its object, so every renderer's `map != _map` check
  rebuilds only when the played map object changes. Nav graphs are keyed by (name, fingerprint), so an edited map gets
  a new graph and an unchanged one keeps its cache.

## 2. Measurements

Headless, Linux container, Release build. Load = `MapFile.Load` of the file text (parse, every op, foliage, decal
requests, destruction prep, Clutter). All times are main-thread CPU unless noted.

### 2.1 Start-up

`Maps.LoadFolder` over the 98 shipped maps: **24-29 s** in the container (three runs), **169 MB** of managed memory
kept for the whole list (281,560 boxes, 70,804 props, 832,622 plants). The six World Tour maps and Stratos Station
are 11 of those seconds. Every launch and every "Refresh maps" pays this. The prop mesh library (696 defs, 20.7 MB of
vertex data) builds in 190-250 ms the first time props are drawn, plus 60-100 ms per pack-local library on a map
change (Silo: 104 ms).

### 2.2 Per map

| Map | Size m | Boxes (one by one / prop colliders / shell / hidden) | Props / meshes / plants / decals | Load ms | Validate ms | BVH ms | Destr. graph ms (worker) | Decal bake ms (worker) | GPU KB (bldg / props / foliage / decals) | Nav: spacing, nodes, edges, build ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Stratos Station | 136 x 252 | 7,522 (0 / 361 / 1,848 / 5,313) | 267 / 3,747 / 0 / 317 | 208 | 11 | 15 | - | 214 | 105 / 8 / 0 / 2,479 | 5.9, 663, 4,286, 686 |
| Silo | 247 x 173 | 28,378 (203 / 2,859 / 25,316 / 0) | 2,071 / 1,587 / 43,374 / 1,034 | 1,536 | 103 | 87 | 331 | 254 | 1,570 / 64 / 1,015 / 4,421 | 6.5, 2,508, 25,909, 3,608 |
| Slopview Mall | 154 x 141 | 5,612 (31 / 1,122 / 4,459 / 0) | 1,088 / 0 / 6,138 / 399 | 299 | 83 | 17 | 76 | 91 | 271 / 34 / 137 / 4,476 | 4.7, 1,700, 16,293, 973 |
| Paris Quarter | 156 x 104 | 43,248 (251 / 2,327 / 40,670 / 0) | 2,802 / 1,116 / 2,632 / 672 | 1,714 | 630 | 170 | 720 | 329 | 2,496 / 87 / 57 / 2,022 | 4.0, 2,584, 15,530, 1,954 |
| Vuorikoski | 237 x 177 | 32,127 (477 / 2,310 / 29,340 / 0) | 5,657 / 2,938 / 55,773 / 946 | 1,807 | 284 | 103 | 317 | 228 | 1,801 / 176 / 1,312 / 5,048 | 6.5, 2,138, 18,793, 2,582 |
| London | 150 x 103 | 31,338 (83 / 1,697 / 29,558 / 0) | 3,661 / 95 / 8,040 / 700 | 1,365 | 1,470 | 123 | 395 | 257 | 1,803 / 114 / 179 / 5,321 | 3.9, 1,879, 10,262, 1,062 |
| Powder Keg | 200 x 139 | 2,417 (241 / 2,175 / 0 / 0) | 4,923 / 341 / 34,890 / 0 | 343 | 6 | 4 | - | 0 | 0 / 153 / 779 / 0 | 5.3, 1,438, 18,158, 336 |
| Timberline | 191 x 137 | 2,184 (411 / 1,773 / 0 / 0) | 4,290 / 1,038 / 51,968 / 0 | 386 | 8 | 3 | - | 0 | 0 / 134 / 1,191 / 0 | 5.1, 1,265, 13,800, 716 |
| Old Town | 187 x 130 | 1,955 (317 / 1,637 / 0 / 0) | 4,904 / 711 / 43,765 / 0 | 406 | 5 | 3 | - | 0 | 0 / 153 / 992 / 0 | 4.9, 1,406, 15,321, 294 |
| Sprawl | 221 x 204 | 1,857 (590 / 1,267 / 0 / 0) | 5,048 / 455 / 68,407 / 0 | 361 | 7 | 3 | - | 0 | 0 / 157 / 1,590 / 0 | 6.7, 1,132, 17,150, 764 |
| Blackmoor Asylum | 70 x 75 | 253 (95 / 134 / 0 / 0) | 126 / 63 / 5,342 / 230 | 22 | 1 | 0.4 | - | 5 | 0 / 3 / 118 / 652 | 3.0, 547, 3,450, 63 |
| Building Yard | 140 x 112 | 6,528 (52 / 867 / 5,609 / 0) | 1,157 / 0 / 15,778 / 0 | 83 | 21 | 15 | - | 0 | 322 / 36 / 347 / 0 | 4.0, 1,854, 14,219, 542 |
| Stress Grid 2k | 200 x 200 | 5,011 (405 / 4,606 / 0 / 0) | 2,000 / 0 / 0 / 1,000 | 145 | 13 | 9 | - | 7 | 0 / 62 / 0 / 354 | 6.3, 1,437, 11,489, 787 |
| **Stress Grid 10k** | 400 x 400 | 15,074 (1,605 / 13,469 / 0 / 0) | 10,000 / 0 / 0 / 5,000 | **1,371** | 129 | 38 | - | 38 | 0 / 312 / 0 / 1,772 | 7.0, 4,754, 44,690, **3,925** |
| **Stress Grid 50k** | 800 x 800 | 70,487 (6,405 / 64,082 / 0 / 0) | 50,000 / 0 / 0 / 5,000 | **31,596** | **2,583** | 207 | - | 129 | 0 / 1,562 / 0 / 1,763 | 7.0, 18,910, 171,858, **20,748** |

Other per-map numbers: fingerprint 0.3-13 ms (Silo 8-13 ms); `ToJson` 5-110 ms (Stratos 109 ms, 1.3 MB: every map
pays this on each "Refresh maps"); `PropBatches.Build` 1-17 ms; `BuildStatic` 0.2-7 ms; `FoliageBatches.Build` 1-27
ms; the old collision grid 0.2-7 ms; prop chunk counts 86 (Blackmoor) to 1,667 (Vuorikoski) and 28,488 on the 50k
stress map.

Where the load time goes (per-op timing, scratch instrumentation of `MapFile.Load:423`):

| Map | Top costs |
| --- | --- |
| Silo | `PlaceProp` x624 = **1,006 ms**; foliage 317 ms; `AutoWaypoints` 51 ms; `PlaceBuilding` x26 = 36 ms; Clutter 22 ms |
| Paris Quarter | `PlaceProp` x343 = **1,004 ms**; foliage 281 ms; `PlaceBuilding` x18 = 142 ms; `AutoWaypoints` 48 ms |
| London | `PlaceProp` x537 = **845 ms**; foliage 270 ms; buildings 55 ms; Clutter 54 ms; then **Validate 1,470-1,574 ms** |
| Vuorikoski | `PlaceProp` x477 = 742 ms; foliage 586 ms; Clutter 185 ms; `AutoWaypoints` 98 ms |
| Stress 10k / 50k | Clutter (`FinishProps`) **1,160 ms / 31,518 ms**; everything else under 200 ms |
| Stratos Station | `Mesh` x3,747 = 9 ms, `Collider` x5,313 = 3 ms: the JSON parse and reflection dominate its 208 ms |

Three quadratic paths explain nearly all of it:

1. **`PlaceProp` builds a fresh `BoxSpace` over every box for each prop** (`MapProps.cs:64`, only when the prop
   ground-snaps, which most do): cost = props x boxes. 624 props x 28k boxes on Silo. Clutter already keeps one
   `BoxSpace` and adds to it (`:241`, `:214`), so the fix is a cached space on the map, invalidated by non-prop ops.
2. **Clutter tests each attempt against every prop placed so far** (`:445-447`, the `ctx.Placed` list) and every
   spawn, waypoint, path, hill, flag and mover (`:456-473`); attempts run to 15 x count (`:333`). Cost = attempts x
   placed: 150k x 5k on the 10k map, 750k x 25k on the 50k map. A cell grid over `Placed` (like `BoxSpace`) makes it
   linear with the same answers (the attempt order and random draws do not change).
3. **`Validate` scans every box per spawn and waypoint** (`Maps.cs:331`, `:371` via `BadSpot`, `MapTerrain.cs:85`):
   1,312 waypoints x 43k boxes on Paris, 6,240 x 70k on the 800 m stress map. The same `BoxSpace` answers it.

The nav graph is linear in nodes (3.9 s for 4,754 nodes, 20.7 s for 18,910, mantle-heavy because the stress walls
are low), and nodes grow with area once the spacing hits its 7 m cap (`BotNav.cs:238`), which happens at about 49,000
m2 (220 x 220 m). The mover walks are a third to a half of it. Note the self-test cap of 3,000 ms of work per map
(`SelfTest.cs:825`): Silo is at 3.6 s here (it passes on the faster CI runner; this box is slow) and the 400 x 400
stress map is at 3.9 s. In a real match the build is sliced at 3 ms per tick (`BotMove.cs:9`), so 3.6 s of work is
1,200 ticks = 20 s of play before bots can plan; 21 s of work would be two minutes.

### 2.3 Frame cost (estimated)

There is no GPU here. The box loop's own CPU is small (0.1 ms for 1,605 boxes, 0.43 ms for 6,405, before the driver),
but each box is four uniform uploads and a draw call, which cost 3-10 us each in a GL driver, so:

| Drawn one by one | Driver CPU per frame (est.) |
| --- | --- |
| 600 (Sprawl, the most on a shipped map) | 1.8-6 ms |
| 1,605 (stress 10k) | 5-16 ms |
| 6,405 (stress 50k) | 19-64 ms: 15-50 fps on its own |

For scale, the user's PC runs the Cluttered maps at about 1,700 fps with 150-600 one-by-one boxes
(`Maps/Cluttered/DESIGN.md:184`, `:260`, `:476`), i.e. under 0.6 ms a frame for everything. Everything else already
scales with what is in view: props and foliage are O(chunks) per frame on the CPU (28,488 chunks x ~15 ns = 0.4 ms on
the 50k map; 1,667 chunks on Vuorikoski = 0.03 ms), buildings O(buildings), decals O(chunks), terrain O(chunks),
meshes O(parts) (0.05-0.1 ms for 3,747 parts).

### 2.4 Extrapolation to 5x and 20x props and decals, 4x area

Taking Silo and Stratos Station as the models for the Space maps (buildings plus meshes plus a few thousand props),
and the measured slopes:

| Quantity | Today (Silo) | 5x props and decals (10k props, 5k decals) | 20x (40k props, 20k decals) | 4x area (500 x 350 m) |
| --- | --- | --- | --- | --- |
| Load (as is) | 1.5 s | ~8-10 s (`PlaceProp` x props x boxes, Clutter x N^2) | **1-2 minutes** | +0.5-1 s (foliage, waypoints) |
| Load (after stage 0) | ~0.5 s | ~1.5 s | ~5 s | linear |
| Start-up with 10 such maps | +15 s here (+4-6 s on a desktop) | +80-100 s | minutes | - |
| Validate | 0.1 s | 0.5 s | 2-3 s | x4 (waypoints x boxes) |
| BVH build (first query, game thread) | 87 ms | ~250 ms (70k boxes) | ~1 s (280k boxes): a visible stall | linear in boxes |
| Nav build work | 3.6 s | 4-5 s (more mantle checks) | 6-8 s | **~15 s** (4x nodes): 80 s of play at 3 ms/tick |
| Prop instance buffer | 64 KB | 320 KB | 1.3 MB | - |
| Prop chunks (per-frame loop) | 1,105 | ~5,000 | ~20,000 (0.3 ms) | x4 |
| Decal bake (worker) | 0.25 s | 1-2 s | 5-10 s; **cap is 6,000 per map** | - |
| Decal GPU | 4.4 MB | 20 MB | 80 MB (400k-vertex cap bites at ~45k wrapped decals) | - |
| Building shell | 1.6 MB | - | - | 6 MB |
| Draw calls, one by one | 203 | depends on authoring | depends on authoring | must stay under ~1,000 |
| Managed memory per map | ~10 MB | ~25 MB | ~80 MB | x3-4 |
| Prop index on the wire (`U16`, `GameProps.cs:323`) | fine | fine | fine up to 65,535 props | - |
| Thumbnail hitch | ~50 ms | ~150 ms | ~500 ms per map scrolled | - |

GPU memory is not the problem: the biggest map here holds under 12 MB of static buffers, and the shared prop library
is 21 MB. Load time, the nav graph and unbatched boxes are.

## 3. Bottlenecks and risks, ranked

1. **Load time, and that every map is loaded at start-up.** 24-29 s for 98 maps in the container; ten more Silo-sized
   Space maps add 15-20 s here (4-6 s on a desktop) to every launch and every "Refresh maps". Three quadratic paths
   (`PlaceProp`'s per-op `BoxSpace`, Clutter's `Placed` scan, `Validate`'s box scan) make prop-heavy maps cost
   seconds each and 50k-prop maps cost half a minute.
2. **Nav graph build time, on the game thread, 3 ms per tick.** Linear in nodes but nodes grow with area past 220 x
   220 m; 4x area means ~15 s of work and over a minute of play with bots steering straight. The 3,000 ms self-test
   cap is already brushed by Silo. Determinism is fine (it builds against an as-built copy and is a pure function of
   the map); the risk is time and the fact that the cache lives only in memory.
3. **Boxes drawn one by one with no culling** (`DrawArena`): the only path that is neither instanced nor culled. Map
   authors have worked around it (0 such boxes on Stratos, 83 on London), but a big hand-built map or one with many
   tree trunks pays 3-10 us per box per frame, and nothing warns them except the design notes.
4. **Main-thread rebuilds on map change**: prop batches, building instances, foliage batches, terrain mesh, mesh
   buckets, plus `PropMeshes.Build` for a pack library (60-100 ms) and the first-query BVH build (87-170 ms today, ~1 s
   at 20x). The lobby's 12-second pack tour and the thumbnail queue repeat this per map. Each is a hitch; none is a
   tick budget problem yet, but the BVH one lands inside the first server tick.
5. **Decal limits**: 6,000 decals per map, 400k baked vertices, 1,024 pack decals per map, 2,048 atlas layers. A
   20x map of Paris-style wrapped decals hits the vertex cap first. The bake is already threaded.
6. **Mesh renderer per-part culling**: O(parts) CPU and a 64 B per visible part upload every frame; the 20,000
   placement cap keeps it under ~1 ms, but 20x Stratos would want it raised.
7. **Memory**: 169 MB managed for all maps today; a 20x map is ~80 MB on its own and every loaded map stays resident
   for the process. Not critical, but it compounds with eager loading.
8. **Thumbnails and the map select screen**: one full rebuild per map per frame while scrolling; proportional to the
   map's whole render-build cost.
9. **Snapshots and fingerprints**: snapshots are player state and do not grow with the map; the fingerprint is O(boxes)
   (13 ms on Silo) and is recomputed in a few places (`Maps.Reload` compares `ToJson`, 100 ms on Stratos); the prop
   index is a `U16` (65,535 props per map). Low risk until maps pass ~60k props.
10. **`--check-map`** loads one map standalone (2.9 s for the 10k stress map including library loads): fine, but it
    reports nothing about cost. **Client join time** is unaffected by map size (maps ship with the build), except that
    the joining client builds its own render data and BVH for the map on arrival.
11. **Windows-only timing checks**: tick work, GC pauses and stuck spots are only measured on Windows
    (`SelfTestTiming.cs:28`), so a nav or BVH stall that only shows in the tick budget needs a `[windows ci]` run.
12. **Physics broadphase at query time** is a BVH and scales as log(boxes); not a risk. The per-query costs in the
    "Physics" section stay flat.

## 4. Proposal: staged, cheapest first

Guiding rules: the server simulation stays whole and deterministic (one `Solid` list, one BVH, one nav graph built
from the as-built world); only render data and loading get chunked or deferred; every stage keeps the fingerprints,
the seeded digests and the pack asset hashes unchanged unless stated; and nothing adds per-frame allocations.

### Stage 0: fix the quadratic load paths (2-4 hours, do first)

- `MapProps.cs:64`: keep one `BoxSpace` per map while loading (field, invalidated whenever `Solid` grows from a
  non-prop op, or kept in sync by `Block` adding to it), as Clutter already does. Same answers, since `SupportTop`
  is a pure query over the same boxes.
- `MapProps.cs:445-447`: file `ctx.Placed` into 4 m cells (reuse `BoxSpace` with a y range, or a tiny 2D grid); the
  spawn/waypoint/path lists (`:456-463`) into the same grid. The attempt sequence and the random draws are untouched,
  so every map places the same props; the "Props", "Cluttered pack" and "Pack assets" sections prove it.
- `Maps.cs:320-338`, `:366-379`: `IsBlocked` and `BlockedButForDoors` through the cached `BoxSpace` (same box test,
  any order: the result is a bool).
- Expected: Silo 1.5 s -> ~0.5 s, Paris 1.7 s -> ~0.6 s, London load+validate 2.9 s -> ~0.6 s, the 50k stress map
  34 s -> ~1 s. Risk: low; the self-test round trips and hash pins catch any change.
- Files: `Source/Props/MapProps.cs`, `Source/Maps/Maps.cs`, `Source/Maps/MapTerrain.cs`.

### Stage 1: load maps in parallel, build shared libraries on a worker (2-4 hours)

- `Maps.LoadFolder` (`Maps.cs:548-631`): parse and build the files of a pack with `Parallel.ForEach`, collect in file
  order, then assign ids and validate in that order. Each map is built alone from its own text; the shared inputs
  (`Props`, `Decals`, `Buildings` libraries, `MaterialDestruction`) are read-only once loaded, so force them before the
  loop. Console lines from ops (moved waypoints, Clutter shortfalls) interleave; buffer them per map. `MapFile` keeps
  static caches (`Builders`) that are read-only. Expected: start-up load / core count (4-8x on a desktop).
- Build the global prop mesh library (`PropMeshes.Build`, 190-250 ms) and the per-pack libraries on a worker at
  start-up or when a pack is first previewed, so the first prop draw never pays it.
- Risk: any hidden static mutable state in an op (worth a grep for `static` fields written during `Load`; the
  recording in `Map.Op` is per instance). Determinism is unaffected: the build of one map never depends on another.
- Files: `Source/Maps/Maps.cs`, `Source/Render/PropRenderer.cs`, `Source/Props/PropMeshes.cs`.

### Stage 2: batch the one-by-one boxes (4-6 hours)

- A `BoxBatches` like `BuildingRenderer.BuildStatic`: one static instance buffer (64 B per drawn box: centre, half,
  colour, material layer), sorted by 24 m chunk in snake order, frustum-culled per chunk with runs merged, drawn by the
  building shader (it is the box shader with instancing, same lighting and texturing, so pixels match). `DrawArena`
  calls it instead of the loop at `Program.cs:1289-1291`; `Renderer.DrawBox` stays for everything dynamic.
- Runtime changes: the one-by-one set is static (props, doors, windows, destruction chunks and Zombies doors are
  already elsewhere), so the only rebuild trigger is the map object changing; `Map.Materials` is cached per map.
- Expected: 600 draws -> ~10, 6,405 -> ~50; the driver cost disappears from large maps and map authors stop having
  to avoid boxes. Risk: a tilt (planet maps, `Renderer.StandAt`) must still apply: on `PointGravity` maps keep the
  old loop or pass the tilt as an instance matrix. The smoke screenshots should be compared before and after.
- Files: new `Source/Render/BoxRenderer.cs` (or extend `BuildingRenderer`), `Source/App/Program.cs`,
  `Source/Render/MapThumbnails.cs:306`.

### Stage 3: nav graph: adaptive budget, disk cache, then parallel columns (1 + 4-6 + 10-16 hours)

- (a) Budget: `BotNavBuildMs = 3` is a constant; make the pump take what is left of the 8 ms tick budget when the
  tick was light (pre-match lobby, few players), e.g. `min(6, 8 - tickWorkSoFar)`. Slices are deterministic by
  design (`BotNav.cs:144-147`), so the budget only changes when the graph is ready. 1 hour.
- (b) Disk cache: write the finished graph (arrays, `SpareNodeCount` spares included) to the save folder keyed by map
  name + fingerprint + a nav format version, load it in milliseconds next time. The graph is a pure function of the
  map, so a cached one is the one the build would produce; "Nav determinism" stays the guard, and a version bump
  invalidates all. Needs the save-file rules (`SavePaths.SaveFiles`, `tools/package.ps1`, `MIGRATING.md`,
  `.gitignore`). 4-6 hours. Expected: the first match on a map pays the build once per install, not per launch.
- (c) Parallel build: columns (`AddColumn`, `:243-252`) are independent and edges are local (within 1.5 spacings), so
  the node pass and the link pass can run per cell on workers against the as-built `PhysicsWorld` (read-only after
  `EnsureTree`), with results merged in cell order so arrays come out identical. The mover walks (`BotNavWalk.cs`)
  use the mover on `World.Physics` through a thread-static override (`UseNavBuildPhysics` is a static field today:
  make it `[ThreadStatic]` or pass the world explicitly). 10-16 hours; the riskiest stage for determinism, so it
  comes last and is proven by "Nav determinism" on every map plus the seeded digests.
- Files: `Source/Game/BotMove.cs`, `Source/Game/BotNav.cs`, `Source/Game/BotNavWalk.cs`, `Source/Game/World.cs`,
  `Source/App/SavePaths.cs`, `tools/package.ps1`, `tools/MIGRATING.md`.

### Stage 4: build render data off the main thread, upload lazily (4-6 hours)

- `PropBatches.Build`, `FoliageBatches.Build`, `BuildingRenderer.BuildStatic`, the terrain mesh arrays and the mesh
  bucket tables have no GL in them (the self-tests already use them headlessly). Start them on a worker from
  `World.SetMap` (next to the decal bake and the destruction graph), upload when ready, draw the previous map's data
  or nothing meanwhile (the decal renderer already does exactly this, `DecalRenderer.cs:206`).
- Build the BVH there too (`World.Physics` is lazy; start it in `SetMap` and `Wait` on first query), so the first tick
  never pays 100-1,000 ms.
- The lobby tour and thumbnails then stop hitching; each thumbnail waits for its data over a few frames instead.
- Determinism: untouched (render-only, and the BVH build is a pure function). Risk: the `map != _map` rebuild checks
  become "data for this map ready?"; `ApplyBroken` and friends must run against the uploaded set only.
- Files: `Source/Game/World.cs`, `Source/Render/PropRenderer.cs`, `FoliageRenderer.cs`, `BuildingRenderer.cs`,
  `TerrainRenderer.cs`, `Mesh/MeshRenderer.cs`, `MapThumbnails.cs`.

### Stage 5: distance cut and LOD for solid props, far cut for decals (6-10 hours)

- Solid props never fade (`PropDef.FadeDistance` is 0 for them) because they are cover, but at 150 m a crate is a
  few pixels. Give every def a `LodDistance` (default from its size) past which the chunk draws a baked LOD1 mesh: the
  prop's collider boxes as plain boxes (already known) or the first N parts. `PropMeshes` gets a second range per def;
  `DrawChunks` splits runs by near distance as it already does for animated props (`:461-468`), so no sort change.
  Beyond a far distance (map-dependent, e.g. fog end) skip solid chunks of small props entirely; big props (vehicles,
  planes) keep drawing.
- Decals: chunks already have a fade distance; add a per-map cap on visible decal chunks or lower the fade for small
  decals.
- Expected: GPU triangles per frame bounded by what is near; CPU unchanged. Risk: pops at the LOD distance (use the
  existing dither cross-fade), and solid props are also bots' concealment, which is unaffected (that is gameplay data).
- Files: `Source/Props/PropDef.cs`, `PropFile.cs`, `PropMeshes.cs`, `Source/Render/PropRenderer.cs`, `Props/README.md`.

### Stage 6: level streaming of render data by cell (15-25 hours; not needed yet)

- Only if maps go past roughly 1,000 x 1,000 m with hundreds of thousands of props, i.e. when the whole map's render
  data stops fitting comfortably (we are at 12 MB now; a 20x map is ~100 MB). Per 96 m cell: its own prop, foliage,
  box and decal buffers built on a worker and uploaded when a player (or the camera) comes within N cells, freed when
  far; the server keeps its whole `Solid`, BVH and nav graph, so nothing in the simulation changes. Destruction and
  breaks go through the same version hooks per cell. The map file format needs nothing; `HalfX` would need its 500 m
  clamp lifted (`MapFile.cs:324`), which changes `ContentHash` inputs only for maps that use it.
- Risks: two-level culling, more draw calls at cell edges, hitches on upload (spread over frames), and the per-cell
  "alive" and broken bookkeeping. Defer.

### Stage 7: asset streaming (2-3 hours now; more only if textures grow)

- Today's assets are small: a 21 MB prop library uploaded once, a 64 x 64 material array, a decal atlas of 64 KB per
  layer, one sky panorama per map. Do: build libraries on a worker (stage 1), cache the per-pack prop meshes so a map
  change inside a pack costs nothing, and keep at most two skies resident. If maps ever carry many large PNG decals or
  model textures, add a budgeted LRU for atlas layers and `MeshTextures`; not before.

### Stage 8: tooling (3-5 hours)

- A `"stress": true` (or `"devOnly": true`) map flag that the all-maps self-test loops skip (nav build, edge walks,
  collision parity, round trips, digests), so `tools/perf/stress_grid.py`'s output can live in `Maps/Dev` with order
  999 without pushing any section past its budget. Until then the generator writes to a scratch path.
- A "Big maps" self-test section with budgets per unit, run on the five largest shipped maps and the stress map: load
  ms per 1,000 props (target under 50 after stage 0), validate ms per 1,000 waypoints, BVH ms per 10k boxes, nav work
  ms per 1,000 nodes, one-by-one boxes (fail over 1,000 until stage 2, over 0 after it), decal and prop GPU KB, and the
  per-frame chunk loop CPU. Print the per-op load breakdown as this study did, so a new map's cost is visible in CI.
- `--check-map` prints the same breakdown and the draw-call count, so map makers see cost before the game does.
- Files: `Source/App/SelfTest.cs` (+ one new section file), `Source/Maps/Maps.cs:675-707`, `Source/Maps/MapFile.cs`.

### Recommended order and what fits in one sitting

Order: **0 -> 1 -> 8 (section) -> 2 -> 3a/3b -> 4 -> 5 -> 7 -> 3c -> 6**. Stages 0, 1 and the self-test section are
what a single agent can do in one sitting (about 8-10 hours, no protocol or save-format change, every check stays
green): they remove the quadratic load, cut start-up by the core count, and leave a budget in CI so the Space maps
cannot regress it. Stage 2 is the next sitting and the one that lets map authors stop counting boxes. Stage 3b is
the one that most changes how a big map feels (bots ready at once on a known map), and it is the first to touch the
save folder rules.

## 5. Open questions for the user

1. How big is "bigger": is 500 x 500 m with 20-50k props the target, or does the 1,000 x 1,000 m clamp need lifting?
2. Is a lazily loaded map list acceptable (names, sizes and player limits read from the header, the rest on first
   use), or must `Maps.All` stay fully built at start-up? Parallel loading needs no such change.
3. May the nav graph be cached on disk in the save folder (a new save file: package list, `MIGRATING.md`, `.gitignore`)?
4. Should the stress map ship in `Maps/Dev` behind a `"stress": true` flag the all-maps checks skip, or stay a tool?
5. Is the 6,000-decal and 20,000-mesh per-map cap allowed to rise for the Space maps, and to what?
6. Can one `[windows ci]` run be spent on the Space maps once they land, so the tick and GC numbers are real?
7. Do the Space maps use `Clutter` heavily (stage 0's Clutter fix matters most) or hand-placed `Prop` ops (the
   `PlaceProp` fix matters most)? Both are in stage 0 either way.
