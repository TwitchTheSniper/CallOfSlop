# Terrain digging (decision 16)

Blasts leave craters in the ground and Drill Modules dig holes; later, shovels dig tunnels and caves. This is the first
slice: craters from explosions and drills' holes in Sandbox, networked.

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
- **Not yet.** Bot nav graphs, foliage, baked decals and baked lighting still see the map as built.

## Rules

- **Who digs.** In Sandbox, with the lobby's Destruction setting on, on a map with terrain
  (`Game.TerrainDigLive`), a blast digs a crater. That covers grenades, launchers, kill streaks and explosive props
  (`SandboxBlast`), and construct guns' explosive rounds and cook-offs (`BlastTargets`). The crater's radius is 0.4 of the
  blast's (0.75 to 6 m), and its depth is 0.55 of its radius.
- **Drills.** A working Drill Module takes scoops out under its bit (`DrillShaft`, `ConstructDrillShaft.cs`; docs/sandbox/
  CONSTRUCTS.md "Gathering"): at most 6 ops a hole, 1.5 m deep under the surface as built, at most one op a gather step in
  the world, and only up to half of `MaxOps`. Each scoop keeps R(k)^2 = R(k-1)^2 - step^2, so it always meets the one
  before it and leaves no shelf on open ground (a steep slope may keep a thin lip: overhangs are allowed, as below).
- **Network.** The server applies an op and broadcasts it (`Msg.TerrainDig`, 15 bytes per op, protocol 108). A joiner
  gets the match's whole log. The ops use no trigonometry, so every peer ends with the same ground. Only ops that
  changed something are logged, up to `MaxOps`.
- **Restore.** `Map.RestoreProps` (every match) fills the ground in again.
- **Overhangs stay.** A second blast lower in a crater cuts under the first one's rim and leaves a lip; digging through
  is the point, so it stays. A gap too low to stand in is a wall for a standing player (crouch to go in): the classic
  mover checks the head over the whole footprint (`TerrainHeadroom.Over`) when walking, jumping and climbing, and
  `PhysicsWorld.OverlapBody` (room to stand, mantle targets) does too. Two blasts in one spot can leave a pit with walls
  taller than a mantle (1.7 m). "Terrain digging" (`DigCraterEscape`) checks no head ever goes into the ground with
  either mover. A drill's hole on open ground has no overhang, a player climbs out of it every way, and stacked on craters
  no head goes into the ground (`DigShaftEscape`).

## Next slices

1. A shovel and a hand drill (scoops, `DigKind.Scoop`) to make trenches, ramps and tunnels.
2. Nav patches around craters, foliage removed over dug ground, ground materials (rock digs slower), and digging
   yielding resources.
3. Artillery.
