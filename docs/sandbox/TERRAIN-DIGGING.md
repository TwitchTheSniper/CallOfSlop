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
  ragdolls take their cave-aware paths. A column's highest top slopes (see "Smooth tops"): height, normal, solidity and
  ray hits all read that slope. Lower tops and undersides are level. Between columns whose changes differ a lot there is
  still a wall: a ledge up to `World.StepHeight` is a step, and anything higher is a wall.
- **Drawing.** Only the faces where solid meets air are drawn (`TerrainEdits.CellFaces`). Where a dug column meets
  untouched ground, its wall follows that ground's sloping surface so the seam is closed. The terrain renderer turns dug
  cells into degenerate triangles in place (a chunk's index range never changes length). The faces go into one small
  mesh per drawing block of 8 x 8 cells (`DigBlocks`, `TerrainRendererDug.cs`); see "What it costs".
- **Smoothed rims (drawing only).** The renderer asks for `CellFaces(..., smooth: true)` (`TerrainEditsSmooth.cs`):
  marching squares on the columns. Where four columns meet and only one (or only three) of them is solid over a step at
  least `MinSmoothStep` (1 m, `GroundTops.Near`) tall, the lone column's quarter loses its corner triangle (or the open one's gains one),
  legs half a column. A staircase outline becomes a line through its steps' middles, so a hole or a floating island
  reads as round. Only those rim columns take the slower path (the Streamed Terrain island: 410k triangles against
  368k as plain blocks); shorter steps (the island's 0.5 m terraces) and corners touching untouched ground keep their
  squares. Rim columns keep level tops (the cut and filled corners are drawn on them). Collision, rays and physics
  stay the square columns there (at most 0.18 m off at a corner). "Terrain digging" and
  "Terrain streaming" check every smoothed face against the shape it draws (`DrawnSolid`).
- **Physics bodies.** `PhysicsWorld.AddTerrain` gives a dug cell's faces as two-sided triangles.
- **Added ground.** A fill adds an interval to its columns (`TerrainEdits.Add`, on undug cells too: the cell turns into
  columns first). Filled ground can stand over the map's highest sample, so anything asking "can this reach the ground?"
  asks `TerrainData.HighestGround`, not `MaxHeight`.
- **Not yet.** Bot nav graphs, foliage, baked decals and baked lighting still see the map as built. A tree on dug ground
  stays where it was, floating over a hole or sunk in a heap, and can still be felled.

## Smooth tops

The ground is still columns of spans; only the open top of each column's highest span stops being a level square
(`GroundTops.cs`, `TerrainEditsTops.cs`, `PlanetEditsTops.cs`). Nothing is stored, sent or saved for it: tops are worked
out from the spans, the same on every peer.

- **Corner heights.** At every corner where four columns meet, each dug column's top gets a height: the ground as built
  at the corner plus a weighted mean of how far the columns there have been dug or filled (top now less top as built).
  A column whose change is within `Near` (4 m) of this one's weighs fully, so they share the height: one sloping
  surface. At `Far` (8 m) apart it weighs nothing: a wall (a deep shaft, a cutting's side). In between the wall
  shrinks. `Near` takes in the steepest step a blast leaves (the side of a crater made by several blasts, the rim of a
  very big one): at 1 m and 2 m those sides were bars and blocks. Only columns whose
  top spans stand side by side count (not a ledge over another's floor). Untouched ground at the corner pulls the top
  to its own surface, so a dug cell meets the map's ground with no wall and ground nobody changed is exactly the map's.
- **A column's top** is two triangles between its four corners where its middle lies on their diagonal (ground as
  built, a level pad, an even slope), else a fan of four round its middle. The middle is the span's top, held within
  25 cm of the mean of its corners (`GroundTops.Middle`): the ground is where the spans say at a column's middle, so a
  drill's hole is as deep and a heap as big as before, but one column left standing in a crater (or bored down alone)
  is a low bump and not a spike, and a column on a steep side is a plain ramp and not a tooth.
- **A dug border makes its neighbours dug cells.** When a dig takes ground from a column on its cell's border, the
  cells beside it become dug cells too, still the ground as built (`TouchBeside`). The hole's side then runs out across
  the border as a slope; held to the untouched cell's edge it drew every hole's outline in 2 m squares.
- **Walls** between columns are drawn from the two tops' edges, whatever is left between them.
- **Filled ground stays walkable.** Where a fill's step was one a player could walk up (0.55 m), its top slopes at
  most 40 degrees and the rest is a riser of at most 13 cm (`GroundTops.Ease`). Dug slopes are left unbroken, so a
  bowl's side reads as one surface; a side too steep to walk slides a player, as steep terrain does.
- **Shading.** Corners that share a height share a normal and a colour (grass blends into dirt), so a bowl shades
  round. A cut shallower than 0.6 m fades from the surface's own colour (`TerrainEdits.ShadeFade`), so a hole's edge
  has no hard line round it. Dug ground draws with the dirt texture for its rock layer too: the map's rock layer has
  slab lines, which ran across every crater. Colours are kept per dug cell and collision never asks for them.
- **Kept per dug cell** (five floats a column plus the as-built corners), made on first use and forgotten when the cell
  or one next to it changes (`Bump`). The arrays grow with the columns, so a query never allocates.
- **Level still:** floating islands' columns and the columns along a smoothed rim on flat maps. On a planetoid a tall
  rim's outline is still the column grid's staircase seen from above.

## What it costs

`--bench-dig [--map NAME]` digs a work site with the drill's own ops on the Planetoid and on Streamed Terrain and
prints what each op, a mesh rebuild, a collision gather, a height query and a ray cost (lines `[dig bench]` in
`callofslop.log`; run it with `DOTNET_TieredCompilation=0`, and twice, because the cloud box is noisy). Measured
2026-11-10 against the code before smooth tops:

| | Planetoid before | after | Streamed Terrain before | after |
| --- | --- | --- | --- | --- |
| Mesh made again after a change | 1.9 ms a chunk (worst 4.4) | 0.26 ms a block (worst 0.5) | 4.6 ms (worst 8) | 0.5 ms (worst 1.4) |
| Collision gather, a player's box | 43 us | 29 us | 11.5 us | 7.8 us |
| Collision gather, a vehicle's box | 140 us | 90 us | 73 us | 50 us |
| Ray down onto the site | 104 us | 35-40 us | 4.3 us | 7 us |
| Height under a point | 128 ns | 146 ns | 28 ns | 50 ns |
| A drill's scoop (the op itself) | 3.6 us | 3.6 us | 1.5 us | 1.6 us |
| A 30 m crater | 3.2 ms | 3.2 ms | 0.51 ms | 0.57 ms |

- **Drawing blocks.** A change used to make its whole 32 x 32-cell chunk's mesh again, at most three chunks a frame:
  a scoop on a worked site cost the whole site. Now the faces are kept in blocks of 8 x 8 cells (`DigBlocks`), only
  the blocks a change touched are made again, and a frame spends at most 2 ms on it (`PlanetRenderer.DugRebuildMs`,
  always at least one block). On flat maps a chunk's surface is cut out once all its blocks are made.
- **Collision** has fewer shapes to gather: a slope is two or four triangles where it was a top and a wall.
- **Planetoid rays** next to dug ground stepped 5 cm at a time anywhere under the height of the tallest fill on the
  planetoid. Dug columns are met exactly along a step (`SegmentHit`), so the step is now bounded by the ground as
  built alone and is at least 25 cm, as it is away from dug ground.
- **Dearer:** a height query and a flat map's ray over dug ground, by the slope's interpolation.

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
- **Craters are open upwards; a drill's overhangs stay.** A crater takes its flattened ball and everything up to a
  radius over the blast, and a roof left over that which is thinner than the radius falls in with it
  (`TerrainEdits.CraterTop`). So a second blast lower in a crater leaves no lip hanging over the new hole (those lips
  were the blocks and spikes round a crater made by several blasts); a roof thicker than the blast's radius stands. A
  drill's scoop is a plain ball: tunnels and overhangs are dug with it, and they stay. A gap too low to stand in is a wall for a standing player (crouch to go in): the classic
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
- **What it places.** The wheel's outer ring round the tool holds the materials, Stone, **Dirt** (the default) and Sand (a green
  dot on the pick), then the modes: **Dig** (the default, in the middle of the arc: the ring's marker), Flatten, Clean holes
  and Fill slope ("Shaping" below). When none of the pick is carried it uses the first of dirt, stone, sand that is. The HUD line
  shows the mode, the material and what is carried.
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

## Shaping

The drill's other three modes (protocol 215; `GroundShape.cs`, `TerrainEditsShape.cs`, `PlanetEditsShape.cs`, the input in
`Program.SandboxSculpt.cs`). Each is one op in the log, so it syncs, replays to joiners and saves like a scoop. The table's
`shapeRadius` (1.5 m) and `cleanRadius` (2 m) size them; a yellow outline shows what the next one touches.

- **Flatten** (`DigKind.GradeDirt`, `GradeStone`, `GradeSand` with its two ends at one point). LMB held levels a pad of
  `shapeRadius` round the crosshair to the height of the ground where the stroke began; RMB to the height of your feet. A
  sweep lays pads 0.66 radii apart. Ground over the level is cut away up to `GroundShape.Headroom` (4 m) above it and carried
  off as digging gives it; the gap under it is filled with the picked material, at most `GroundShape.Depth` (12 m) deep. What
  the same op cuts counts towards what it fills, so levelling a bump into a dip pays for itself.
- **Fill slope** (the same op with two ends). LMB on one end, LMB on the other: a ramp twice `shapeRadius` wide, its top the
  straight line between the two ends' heights, cut and filled as Flatten. At most `GroundShape.MaxLength` (32 m) long. RMB
  lets go of the first end, as does changing mode or tool. On a planetoid a height is a distance from the centre and the
  ramp's top runs between the ends' altitudes.
- **Clean holes** (`DigKind.SmoothDirt`, `SmoothStone`, `SmoothSand`). LMB held fills pits and ragged dig marks in a disc of
  `cleanRadius`: the surface under the crosshair is closed (the most of its neighbours' heights within `GroundShape.Kernel`,
  1.25 m, then the least of those), so a pit up to about 2.5 m across comes up to the ground round it and a slope, a bump or a
  wide dip stays as it is. It only fills, at most `GroundShape.SmoothRise` (3 m) a go.

The server checks reach (to the nearest point of a ramp), the 16 m over the surface rule, what is carried and burial (`FillBuries`
through each op's own fill) exactly as for a fill, and is quiet when a Flatten or Clean holes spot needs nothing (a sweep goes over
ground already done). A grade carries its far end after the op on the wire (`Msg.TerrainDig`, `CActKind.Sculpt`) and in a world
save's ground section (`GNDS`), so an older build can't read the ground of a world saved with a grade in it. A plan of every column's top
is made once for the ground's version (the measure, the burial checks and the op share it): a Clean holes op is about 0.5 ms, a
32 m diagonal ramp about 0.1 ms.

## Next slices

1. Nav patches around craters, foliage removed over dug ground, rock that digs slower.
2. Artillery.
