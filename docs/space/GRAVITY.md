# Gravity quirks (space phase 5)

Phase 5 of the space plan (`planets/SPACE-ARCHITECTURE.md` in the project files, section 5): gravity as a **sum of
sources**, gravity-tech parts, and a stitched pair of worlds. Code: `Source/Physics/GravitySources.cs`,
`Source/Physics/GravityShapes.cs`, `Source/Constructs/ConstructGravity.cs`; tests: "Gravity sources" and "Gravity parts".

## Sources and the summed field

A `GravityModel` holds `GravitySource`s, each a pure function of position in doubles, with its total pull (`Mu`, GM in
m^3/s^2), centre of mass and a bounding radius (`Bound`):

| Source | What it is | Levels |
| --- | --- | --- |
| `PointSource` | a round body: a point mass outside its radius, a uniform ball inside | exact (as cheap as a point) |
| `MasconSource` | point masses through a body, softened (Plummer) so the field stays finite inside | one point far away, else every mascon |
| `TetraSource` | a body of tetrahedra, each with its own density | one point far away, each tetrahedron a point at medium range, the exact closed form close in |
| `UniformSource` | a constant pull | |

`TetraSource` uses the closed form for uniform polyhedra (Werner and Scheeres 1997): an edge term `-E r L` and a face
term `n (n . r) omega` summed over the boundary of each density, exact inside and outside (hollow worlds, the neck of a
stitched pair). Faces shared by two tetrahedra of the same density cancel when it is built, so a fine mesh costs only
its surface. A source is one point beyond `FarFactor` (default 20) of its bounding radii, and a tetrahedral body uses
its tetrahedra as mascons when the query is `MidFactor` (10) of its largest tetrahedron's radius outside its bound.
"Gravity sources" checks every level within 1% of the exact field.

`SummedGravity` wraps a model as the game's `IGravityField`: up is against the pull, and where the pull nearly
vanishes (the middle of a neck) it is away from the strongest source; height is the distance from the strongest
source's centre. It remembers its last query (the mover asks for gravity, up and height at one spot in a row).

### In a map file

```json
"gravity": { "type": "sum", "axis": [0, 0, -1], "far": 20, "sources": [
  { "type": "point", "name": "West", "center": [-80, 0, 0], "radius": 60, "surface": 9.81 },
  { "type": "mascons", "points": [[60, 0, 0], [70, 5, 0, 2]], "soft": 3, "mu": 20000 },
  { "type": "tetra", "vertices": [[0, 0, 0], [10, 0, 0], [0, 10, 0], [0, 0, 10]], "tetras": [[0, 1, 2, 3], [0, 1, 2, 3, 2.5]], "mu": 500 },
  { "type": "uniform", "direction": [1, 0, 0], "strength": 0.2 } ] }
```

A body's pull is `"mu"` or `"surface"` gravity at `"radius"` metres from its centre of mass; a mascon's fourth number
and a tetrahedron's fifth are relative weights or densities. A `"sum"` can also be a zone's field or a zones map's
default. Bad files are refused with a reason (flat tetrahedra, bad indices, unknown keys, only uniform sources).
`GravityShapes` builds boxes, balls, hollow shells and necks as tetrahedral meshes in C#.

## Force hooks

- **In the bubble:** `IGravityHook`s added with `GravityFrames.AddHook` pull on top of the map's field in
  `GravityFrames.GravityAt`, the swept mover (when airborne) and constructs. They change the pull, not "up". Changing the
  map clears them.
- **Gravity boxes:** `GravityFrames.UseVolumes(IGravityVolumes)` puts boxes with their own gravity over the map's field.
  While any are in use, `GravityFrames.Field` is a `VolumeField` wrapper (the map's own field stays `GravityFrames.Base`),
  so players' frames, the mover and everything curved see them; crossing into a box keeps the world look, like a zone.
- **In the ship integrator:** an `ISpaceForce` adds to every ship; an `IShipForce` also knows which ship it acts on.
  `BodyGravityForce` gives a body on rails its lumps (a `GravityModel` in its body-fixed frame, less the point mass the
  integrator already has).

## Gravity parts

Both are logic parts (`Constructs/Parts/Gravity/`), switched with F or a data signal, and need power. They run on the
parts' outputs (which clients hear) and the constructs' poses, so host and clients agree with nothing new on the wire.
`ConstructGravity.Refresh` gathers the working ones once a tick: the host at the end of `ConstructWorld.Step`, a client
after it blends the poses. A part never pulls its own construct (`ConstructGravity.GravityFor`).

- **Gravity Well:** pulls everything near it, its strength the pull 4 m away (softened closer in, fading to nothing at
  its range); a negative signal pushes. On a ship in space it pulls the other ships (`ConstructGravity.SpaceWells`, an
  `IShipForce` on `SpaceFlight`'s ships), never its own.
- **Gravity Generator:** the box it fills (configured like a cargo volume's, outlined in build mode, up to 24 m a side)
  gets its own down, the part's -Y, at its strength. Players walk on whatever face the builder points it at; loose bodies
  in the box fall that way.

## Twin Worlds (Dev map)

`Maps/Dev/TwinWorlds.json`, written by `tools/space/make_twin_worlds.py` (fix the script, not the file): two 60 m
worlds 40 m apart, joined by a 14 m neck. Gravity is two `point` sources and a `tetra` neck three times their density
(so the neck feels at most about 10 degrees steep: lighter, the worlds' pull along it made it a 35 degree ridge). The
self-test walks from the west world's top over the neck to the east world's, finding ground under every metre.
