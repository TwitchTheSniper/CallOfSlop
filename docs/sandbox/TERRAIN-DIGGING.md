# Terrain digging (decision 16)

Blasts leave craters in the ground, Drill Modules dig holes, and the ground drill digs the ground into carried dirt, stone
and sand and puts it back ([Sculpting](#sculpting)), all in Sandbox and networked.

## The idea: layered stacks over the heightfield

The approach comes from Peytavie et al., "Arches: a Framework for Modeling Complex Terrains" (2009), building on Benes and
Forsbach (2001). Untouched ground stays the map's heightfield (with its holes and caves) and costs nothing. The first dig
in a terrain cell turns that cell into columns about 0.5 m wide (`TerrainEdits.Sub` per cell side). Each column is a short
list of solid spans (bottom, top), read off the heightfield at the column's middle. The lowest span reaches down for
ever, and bedrock is 24 m under the map's lowest ground. A dig takes an interval out of every column it covers. Craters,
overhangs, tunnels and caves above caves are all just spans.

- **Queries.** `TerrainData.Sample`, `GroundBelow`, `CeilingAbove`, `SolidAt`, `NormalBelow` and the ray cast answer
  from a dug column first. `HeightfieldCollision.HasVoids` is true once anything is dug, so movement, grenades and
  ragdolls take their cave-aware paths. Dug tops are flat (normal up), so dug ground reads as blocks: a ledge up to
  `World.StepHeight` is a step, and anything higher is a wall.
- **Drawing.** Only the faces where solid meets air are drawn (`TerrainEdits.CellFaces`). Where a dug column meets
  untouched ground, its wall follows that ground's sloping surface so the seam is closed. The terrain renderer turns dug
  cells into degenerate triangles in place (a chunk's index range never changes length). It also builds a small block
  mesh per dug chunk, at most 3 chunks a frame (`TerrainRendererDug.cs`).
- **Smoothed rims (drawing only).** The renderer asks for `CellFaces(..., smooth: true)` (`TerrainEditsSmooth.cs`):
  marching squares on the columns. Where four columns meet and only one (or only three) of them is solid over a step at
  least `MinSmoothStep` (0.75 m) tall, the lone column's quarter loses its corner triangle (or the open one's gains one),
  legs half a column. A staircase outline becomes a line through its steps' middles, so a hole or a floating island
  reads as round. Only those rim columns take the slower path (the Streamed Terrain island: 410k triangles against
  368k as plain blocks); shorter steps (the island's 0.5 m terraces) and corners touching untouched ground keep their
  squares. Collision, rays and physics stay the square columns (at most 0.18 m off at a corner). "Terrain digging" and
  "Terrain streaming" check every smoothed face against the shape it draws (`DrawnSolid`).
- **Physics bodies.** `PhysicsWorld.AddTerrain` gives a dug cell's faces as two-sided triangles.
- **Added ground.** A fill adds an interval to its columns (`TerrainEdits.Add`, on undug cells too: the cell turns into
  columns first). Filled ground can stand over the map's highest sample, so anything asking "can this reach the ground?"
  asks `TerrainData.HighestGround`, not `MaxHeight`.
- **Not yet.** Bot nav graphs, foliage, baked decals and baked lighting still see the map as built. A tree on dug ground
  stays where it was, floating over a hole or sunk in a heap, and can still be felled.

## Big craters

A massive blast's crater (docs/sandbox/CONSTRUCTS.md, "Massive blasts"; up to 120 m) is not dug in one op: 120 m is
about 180,000 columns, tens of milliseconds on the server and on every client at once. The server (`Game.MegaDigTick`)
waits until the shock front is past it, then digs it a band of the footprint's rows a tick: op k of n (`DigOp.Slice`,
`Slices`) takes rows `cj0 + rows * k / n` to `cj0 + rows * (k + 1) / n - 1` of the same ball, on flat ground and on each face
of a planetoid, so every peer digs the same columns in the same order. A band is at most 3,500 columns (about 0.5 ms;
the first time a map digs its columns, up to 4 ms, making them). On the wire a sliced op's kind byte has 0x80 set and two
bytes follow, its slice and slices (protocol 195); it is logged and sent like any op, so a joiner replays the bands.

## Planetoids

A planetoid digs too, with radial columns instead of vertical ones (`Source/Planets/PlanetEdits.cs`, docs/planets/PLANETS.md "Digging"). Game code reaches either ground through `IDigGround` (`Game.DigGround`).

## Rules

- **Who digs.** In Sandbox, with the lobby's Destruction setting on, on a map with terrain
  (`Game.TerrainDigLive`), a blast digs a crater. That covers grenades, launchers, kill streaks and explosive props
  (`SandboxBlast`), and construct guns' explosive rounds and cook-offs (`BlastTargets`). The crater's radius is 0.4 of the
  blast's (0.75 to 6 m), and its depth is 0.55 of its radius. A bomb's own crater goes to 60 m, a massive blast's to 120 m
  ([Big craters](#big-craters)).
- **Drills.** A working Drill Module takes scoops out under its bit (`DrillShaft`, `ConstructDrillShaft.cs`; docs/sandbox/
  CONSTRUCTS.md "Gathering"): at most 6 ops a hole, 1.5 m deep under the surface as built, at most one op a gather step in
  the world, and only up to half of `MaxOps`. Each scoop keeps R(k)^2 = R(k-1)^2 - step^2, so it always meets the one
  before it and leaves no shelf on open ground (a steep slope may keep a thin lip: overhangs are allowed, as below).
- **Scoops.** A bucket on an arm takes bites when it is really pushed in, and pours dirt, stone and sand back as fill (docs/sandbox/CONSTRUCTS.md "Scoops"). Same ops, same log, stopping at `scoops.logShare`.
- **Network.** The server applies an op and broadcasts it (`Msg.TerrainDig`, 15 bytes per op, protocol 108). A joiner
  gets the match's whole log. The ops use no trigonometry, so every peer ends with the same ground. Only ops that
  changed something are logged, up to `MaxOps` (65,536).
- **Restore.** `Map.RestoreProps` (every match) fills the ground in again.
- **Overhangs stay.** A second blast lower in a crater cuts under the first one's rim and leaves a lip; digging through
  is the point, so it stays. A gap too low to stand in is a wall for a standing player (crouch to go in): the classic
  mover checks the head over the whole footprint (`TerrainHeadroom.Over`) when walking, jumping and climbing, and
  `PhysicsWorld.OverlapBody` (room to stand, mantle targets) does too. Two blasts in one spot can leave a pit with walls
  taller than a mantle (1.7 m). "Terrain digging" (`DigCraterEscape`) checks no head ever goes into the ground with
  either mover. A drill's hole on open ground has no overhang, a player climbs out of it every way, and stacked on craters
  no head goes into the ground (`DigShaftEscape`).

## Sculpting

The ground drill (tool wheel; `ConstructTool.GroundDrill`, wire byte 7; held model `Constructs/Tools/HandDrill.json`) digs and
fills with the crosshair. The numbers are in `Constructs/Digging.json` (`DiggingTable`, ConstructDigging.cs): 5 m reach, a
ball of 0.8 m radius, one every 0.3 s while held, 10 units a cubic metre both ways.

- **LMB digs** a `DigKind.Scoop` at the aimed ground through `Game.Dig`. Bedrock holds; the drill module's 1.5 m limit doesn't
  apply. The tool stays on while held and bores deeper at the rate; digging has nothing of its own to act on, and it stops
  when the ground leaves its reach. What comes out is measured before the op (`TerrainEdits.Measure`), column by column:
  the surface's material through the table's `ground` (grass and dirt give dirt, rock stone, sand sand), everything more than 3 m
  (`stoneDepth`) under the surface as built stone, and ground a fill laid gives back what it was made of. Fractions of a
  unit are kept per player on the server (`SandboxInventory.AddBulk`), so cutting a stroke up doesn't change what it gives.
- **RMB fills** a `DigKind.FillDirt`, `FillStone` or `FillSand` at the aimed ground, for what it measures to add, with a
  column's gap under the ball closed when no taller than the radius (so a fill on a slope has no air under it). A stroke
  never acts on what it laid: a new spot must clear every spot of the stroke by 1.1 radii across the ground
  (`SculptStroke`), so a stationary hold places once, and a sweep lays a line. Refused, with the reason on the HUD: it would
  bury a player or a construct (`Game.FillBuries`, column by column: only new ground inside a body's box counts), not enough carried, more than 16 m over the surface as built. Creative: free.
- **What it places.** The wheel's outer ring round the tool: Stone, **Dirt** (the default, in the middle of the arc), Sand.
  When none of the pick is carried it uses the first of dirt, stone, sand that is. The HUD line shows the choice and what is carried.
- **A fill draws as what it was made of**, in the map's own dirt, rock or sand colour. A column remembers one fill, the last,
  from the lowest ground it added up (`_fillKind`, `_fillLo`): a stone fill with dirt laid over it digs and draws as dirt.
  Per-span materials would be exact and cost a list per column.
- **The server's rate.** A bucket of 3 actions refilling at the repeat time (`Game.SculptBurst`): a client that asks
  faster is ignored, one that keeps the rate survives bunched packets.

**The log.** Sculpting makes far more ops than blasts, so `MaxOps` went from 16,384 to 65,536, and the tool stops at 75% of
that (`logShare`, 49,152 ops) with a HUD reason; drills stop at half (32,768), so blasts always have room. At 3.3 ops a
second that is 4.1 hours of one player's held button, about an hour of four players'. A log that full is 720 KB to a joiner
(64 ops a packet) and replays in about 100 ms (2 us an op, "Terrain sculpting" prints it). A compact state of columns
was rejected: a column is about 8 bytes and a dug cell up to 64 columns, so the log is the smaller of the two until a spot has
been worked over many times. The log is empty again when the map restores. Nothing is saved yet.

Overhangs and pits follow the rules above. A pit can be dug too deep to climb out of; dirt carried builds a way out.
Constructs and loose bodies near a change wake (`AfterDig`). Zombies and props are not checked by a fill. "Terrain sculpting"
and "Terrain sculpting network" (`Source/App/SelfTestSculpt.cs`) cover all of this.

## Next slices

1. Nav patches around craters, foliage removed over dug ground, rock that digs slower.
2. Artillery.
