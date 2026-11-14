---
paths:
  - "Source/Maps/Terrain*.cs"
  - "Source/Maps/Dig*.cs"
  - "Source/Maps/Ground*.cs"
  - "Source/Render/TerrainRenderer*.cs"
  - "Source/Planets/**"
  - "Source/Game/GameTerrain*.cs"
  - "Source/Physics/TerrainHeadroom.cs"
  - "Source/Constructs/ConstructDigging.cs"
  - "Source/Constructs/ConstructScoops.cs"
  - "Source/Constructs/ConstructDrill*.cs"
  - "docs/sandbox/TERRAIN-DIGGING.md"
  - "docs/perf/STREAMING.md"
  - "docs/planets/**"
---

# Ground, digging and planetoids: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- **Dug ground** (docs/sandbox/TERRAIN-DIGGING.md). Blasts dig craters (`TerrainEdits.cs`, runtime only, cleared by `RestoreProps`). Every terrain query goes through `TerrainData`, which answers from the dug columns first: code that reads `Heights` or `Voids` directly doesn't see digging. Overhangs are allowed, so movement checks headroom over the whole footprint (`TerrainHeadroom.Over`).
- **Streamed terrain** (docs/perf/STREAMING.md section 8). A terrain with `"stream": true` has empty `Heights`/`MaterialIds`: tiles are made on demand (`TerrainStream.cs`). Read ground through `TerrainData.Height`, `Material` and `CellCorners`, never the arrays, and never loop over the whole grid. Bots get no nav graph on it yet.
- Stack blasts when trying digging. A nav graph finished over dug ground is flagged and dropped when the ground goes back (`NavGraph.BuiltOnDugGround`, `ForgetDug`).
- 2026-11-10: after the ordnance cook-offs a broken bomb went off as a landed shell, and the Terrain streaming check counted prop blasts only. A change that moves an effect to a new path updates the checks that count the old one (`ShellsLanded`).
- 2026-11-10: a Titan Walker lying on Caledon made Sandbox ticks 15 ms: `PlanetGround` asked every leaf of a big group for its own patch of ground (5,300 ground queries a tick). Per-tick work against the world is bounded per body whatever its size: the lowest pieces kept from tick to tick plus a slice of the rest, a shared triangle budget (`PickGroundLeaves`, `MaxGroundTris`); measure with the biggest blueprint lying on a slope, not standing on the flat.
- 2026-11-10: the smooth tops (PR #91) were judged on single craters and drill holes; in play, blasts on the floor of earlier craters left overhanging lips, sides steeper than 2 m a column stayed bars, and a hole's side stopped square at the untouched cell beside it. Judge dug ground on a dozen blasts in one place and on a very big crater with small ones inside, from the ground and from above (`TerrainEdits.CraterTop`, `TouchBeside`, `GroundTops.Near`).
- 2026-11-11: after PR #96 a hole dug with the hand drill was still blocks and dark faces: a player drills at an angle from beside the hole, so every scoop undercuts the far side, and the checks dug straight down. Try a tool the way a player holds it (from an eye beside the hole, aiming into it), not with ops placed from above (`TerrainEdits.MinRoof`).
- 2026-11-12: players stood still on the colony's floors on Glacier drifted 1-10 cm/s: the swept mover moves along the planet's up, tilted a little from +Y away from the pole, and `BlockPlayer` put the feet back on the floor straight up +Y. A position fix that undoes a move along up goes back along that same up (`GravityFrames.UpAt`), never along a world axis; try a change to standing with a player left still for 6 s on a tilted planet floor.
- 2026-11-14: a hosted (net) world on Glacier had no caves, metro or graded benches: the match reset dug them (`ResetSandbox`: `SandboxCaves`, `CityMetroDig`) and then `OnMatchStarted` called `Map.RestoreProps`, which clears dug ground; offline the order hid it. Ground made at a world's start is cleared before it is made (`Map.ClearDugGround` in the fresh-world block), never after, and the server sends its log again after `MatchStart` (which clears a client's); try a start dig in a `--net host` window, not only offline (`cave N` reads it).
- 2026-11-14: a blueprint asked for in a cave came down on the world's top 26 m over the player: `ConstructWorld.Ground(q, near)` cast down from 60 m over the ground, which meets the cave's roof first. Under a roof in dug ground the ray starts under the roof over the point; try a spawn, a ghost and a `bring` inside a cave (`cave 1`, `spawnbp`).
