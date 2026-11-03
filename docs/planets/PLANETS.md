# Planetoids

A planetoid is a whole world kilometres across: ground all the way round, gravity towards its centre and a horizon
in every direction. The first one is the Dev map **Planetoid** (`Maps/Dev/Planetoid.json`, 10 km radius). The code is in
`Source/Planets/`; the "Planetoid" self-test section checks it (`Source/App/SelfTestPlanetoid.cs`).

This is step 1 of the plan (one planetoid you can walk on). Still to come: Sandbox on it (constructs still assume gravity
points down -Y), gravity play (islands and moonlets with their own pull), several planetoids, and travel between them.

## The shape: a cube-sphere

- Six faces of a cube (+X, -X, +Y, -Y, +Z, -Z), each a grid of `N` x `N` cells, pushed out onto the sphere. The grid
  is **equiangular** (`Planetoid.Warp`: tan of the face coordinate), so cells are nearly square everywhere (about
  1.92 m on the 10 km map, `N` = 8192).
- `N` is a power of two, so every grid coordinate is exact in floating point and a vertex on a face's edge is
  **bit for bit** the neighbouring face's (same direction, same height). "Planetoid" checks every seam.
- Each face's grid axes satisfy v x u = outward normal, the same handedness as the flat terrain's x and z, so the drawing
  reuses the flat terrain's index list and the collision its winding.
- A vertex's height is a function of its direction alone (`Planetoid.HeightAt`), worked out in doubles and rounded to
  1/1024 m, so any part of the world can be made on its own and every peer gets the same numbers.

## The map file

The top-level `"planetoid"` object (MapFile.cs calls `Planetoid.Parse`):

| Key | Default | Meaning |
| --- | --- | --- |
| `radius` | 10000 | Radius of the ground at height 0 (500 to 16000 m). |
| `cell` | 2 | About this many metres per cell; rounded so a face has a power of two of them. |
| `strength` | 11 | Gravity at the surface (m/s2). The Dev map uses 18, the game's usual. |
| `base` | 0 | Height before any feature. |
| `surface` | | Colours and slope rules, exactly as a flat terrain's `surface`. |
| `features` | | Applied in order (below). |

The centre is straight under the world origin (`(0, -radius, 0)`), so the +Y pole (latitude 90) is at the origin plus
its height: that's where spawns and play start, and where float precision is best. The map gets point gravity
towards the centre unless it gives its own `"gravity"`; `halfX`/`halfZ` (up to 12600) set the top of the sky as on any
planet map. Surface ops (`SurfaceSpawn`, `SurfaceBlock`, `SurfaceMesh`...) place things on the planetoid's ground by
latitude and longitude.

Features (lat/lon in degrees: latitude 90 is the +Y pole, longitude 0 faces +Z and 90 faces +X):

| Feature | Keys | What it does |
| --- | --- | --- |
| `noise` | amplitude, scale, octaves = 4, persistence = 0.5, lacunarity = 2, seed = 1, ridged | 3D fBm value noise over the sphere (scale in metres): seamless everywhere. |
| `mountain` | lat, lon, radius, height, seed, ridges, erosion, warp | The flat terrain's eroded mountain, laid on the ground round its centre. |
| `flatten` | lat, lon, radius, height, edge = 20 | Flat at `height` within `radius`, blending back over `edge`. Pick a height near the natural ground there, or the edge is a cliff. |
| `crater` | lat, lon, radius, depth, rim = depth / 4 | A bowl with a raised rim out to twice the radius. |

Features placed by lat/lon work in their own local ground coordinates (azimuthal equidistant: true distance and bearing
from the centre), so a mountain looks the same wherever it is. `Planetoid.WorkOutBounds` gives the lowest and highest the
features can make the ground (from their own limits, never a sampling), which rays and the drawing rely on.

## Collision

The planetoid isn't a heightfield over x and z, so it isn't `Map.Terrain`. `PhysicsWorld.Planet` answers for it in
every query, with `ShapeKind.Terrain` ids (footsteps and impacts treat it as dirt):

- **Triangles** (`Planetoid.Gather`): two per cell, split along the cell's (0,0)-(1,1) diagonal like the drawing, front
  faces out. The capsule mover (`SweptMover`, always used on point-gravity maps) and capsule/sphere casts get them through
  `PhysicsWorld.Gather`.
- **Rays** (`Planetoid.Raycast`): clip to the shell between the lowest and highest ground, march by half the height
  above the ground (the features' smooth height while more than 1 m up, so a long ray makes no cached tiles), then halve
  down onto the triangles. Within 3 mm of the triangles in the self-test.
- **Vertices** are cached in 32 x 32-cell tiles (17 KB, 1,024 kept, the least recently used dropped). A tile takes about
  1 ms (2 ms on a mountain); a player walking makes a few a second.
- Overlap checks (`OverlapBody`, `PointInside`, `OverlapBox`, `OverlapCapsule`) also refuse anything under the ground
  (`Planetoid.Altitude`), and `World.ClampToSky` puts a player back on top as a safety net.

## Loose things

What lies about falls towards the centre and lands on the ground anywhere on the planetoid ("Planetoid loose items",
`Source/App/SelfTestPlanetLoose.cs`, checks the pole, a face seam, a cube corner, the equator and a crater):

- **Loose construct bodies** (a part dropped from the hands or the backpack, a part cut free, a wreck): gravity per body
  from `GravityFrames.GravityAt` at its centre of mass, and their solid pieces meet the planetoid's triangles
  (`ConstructPhysics.PlanetGround`, the flat map's ground contacts with the planet's `Gather`; a big body asks leaf by
  leaf). A body is out of the world 30 m under the lowest ground (`ConstructWorld.FellOut`, `PhysicsWorld.Underworld`),
  never at Y -30: the far side of the planet is kilometres below Y 0. A part dropped or placed with a yaw stands upright in
  the frame where it lands (`ConstructBuild.PlaceNew`, `ConstructEdits.Drop`, `DropPack`).
- **Ragdolls** gather the ground's triangles with their shapes, and are broken only 500 m under the ground.
- **Grenades** bounce off the ground (their curved step's rays include the terrain layer).
- Gibs and prop debris already found their floor with a ray along "up".

## Drawing

`PlanetRenderer` (drawn from `TerrainRenderer.Draw` when the map has a planetoid) is the streamed terrain's CDLOD per
face: a quadtree per face (the top level is the whole face, 8 levels on the 10 km map), 65 x 65-texel pages made on a
worker thread, one instanced draw a frame, morphing between levels and skirts along "up".

- A page stores each vertex's **position relative to the page's origin** (the node's centre on the sphere), so the
  shader adds the origin and nothing far away loses precision. Normals come from the 3D positions, colours from the flat
  terrain's surface rules with the slope measured against "up".
- Nodes wholly on the far side of the planet are skipped; nodes not yet made are bounded by nine directions at the
  lowest and highest ground plus the arc's sagitta.
- **Sky and light follow the local up.** `SkyRenderer` turns the sky's rays, sun and moon into the frame at the camera,
  and drops the sky's horizon to the planet's limb (`PlanetRenderer.HorizonDip`): from 700 m up the limb is 21 degrees
  below level. The ground shader corrects the ambient light (sky above "up", not world +Y) and colours its haze with the
  same local sky. The sun stays where the map puts it in the world, so the far side of the planet is in night.
- Far plane: to the horizon from the highest ground and on to ground as high behind it (`Map.PlanetoidFarPlane`, 26 km
  on the Dev map); fog runs from 300 m to 90% of it.

## Digging

Sandbox with Destruction on digs a planetoid the way it digs a flat map (docs/sandbox/TERRAIN-DIGGING.md): blasts leave
craters, the ground drill digs and fills, and the same `DigOp`s go over the wire (`Msg.TerrainDig`, unchanged).
`Source/Planets/PlanetEdits.cs` holds it, behind the same `IDigGround` interface as `TerrainEdits`, so the game's digging
code doesn't care which ground it has.

- **Radial columns.** A touched cell splits into 4 x 4 columns (0.48 m). The equiangular grid's lines are great circles,
  so every column wall is a plane through the centre and a column is a narrow cone along "up". Its spans are kept as
  altitudes above the radius, carved and filled with the flat map's span maths (`TerrainEdits.SubtractSpans/AddSpans`).
  Column directions are rounded to 2^-30 so every peer gets the same numbers.
- **Seams and corners.** Grid points on a face's edge are bit-identical between neighbouring faces, so cells meet
  without gaps; an op's footprint is worked out per face from its box's corners, and a side face looks its neighbour up
  across a seam (`Neighbour`) by direction. Craters across one, two and three faces come out round.
- **Collision.** `Altitude`, `GroundUnder` and `NormalUnder` read the spans; `Gather` gives touched cells' column faces
  instead of their triangles. Near dug cells the raycast steps by the height over the ground as built, tests each step
  against the columns exactly (`SegmentHit`) and checks untouched cells against their triangles.
- **Drawing.** Level-0 pages with a dug cell get a mask (a texture array on unit 8) that drops those cells and their
  skirts; each touched 32 x 32 chunk gets its own block mesh (`PlanetRendererDug.cs`), at most 3 rebuilt a frame.
- `RestoreProps` clears the digging like a flat map's. "Planetoid digging" tests a crater at the pole, on a seam and at a
  cube corner (depth, roundness, 400 rays each against brute force, walks, fill and burying, a copy's digest, drawing,
  host + client + late joiner, the ground drill).

## In a star system

The Dev system (`Systems/Dev.json`) names this map, so it is played as that system's Planetoid (docs/space/SPACE.md, "The
bubble"): the sun is the star's real direction for the space time, the planetoid turns once a day and leans 23.4
degrees, and the moonlet crosses the sky. The polar spawn is in midsummer at the start (the sun circles 23 degrees up);
walk south for day and night.

## Not yet


- Constructs (step 2): loose bodies fall and rest (above), but vehicles, wheels, suspension, thrusters, the physics gun,
  build ghosts and joints still assume Y is up, and construct drills don't dig a planetoid yet.
- Map boxes more than 500 m below Y 0 count as "parked" (switched off) in `PhysicsWorld`, and rockets leave the world
  below Y -30 or above Y 400: harmless on the Dev map (its blocks are at the pole) but a trap for anything placed far
  from it.
- Dug rims are blocky (no smoothing) and bots don't see digging.
- Bots have no nav graph on a planetoid (they steer straight); props, foliage, weather and buildings are flat-map things.
- Other renderers (boxes, props, players) still take their ambient light from world +Y, so far from the pole things
  lit only by the sky look a little off.
- The gravity frame's "north" has no direction at the two points the axis (-Z) passes through, on the equator.
