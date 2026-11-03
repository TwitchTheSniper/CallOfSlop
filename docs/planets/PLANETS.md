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

## Not yet

- Sandbox and constructs (step 2): `ConstructPhysics` uses a straight-down gravity; digging works on a flat heightfield.
- Bots have no nav graph on a planetoid (they steer straight); props, foliage, weather and buildings are flat-map things.
- Other renderers (boxes, props, players) still take their ambient light from world +Y, so far from the pole things
  lit only by the sky look a little off.
- The gravity frame's "north" has no direction at the two points the axis (-Z) passes through, on the equator.
