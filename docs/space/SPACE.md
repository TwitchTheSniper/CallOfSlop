# The space layer

Call of Slop is heading for several planetoids, in one star system and across systems. The space layer is what places
them: where every body is at any time, the frames they turn in, and (later) ships flying between them. It follows
Karl's architecture doc (six layers, five coordinate tiers), built in C# on what the game already has: no Flecs, Jolt or
CBT (Karl, 2026-11-03). The plan and the phases are in the project file `planets/SPACE-PLAN.md`; the code is in
`Source/Space/` (namespace `CallOfSlop.Space`); the "Space frames" self-test section checks it (`Source/App/SelfTestSpace.cs`).

**Phase 1 (this):** positions, the clock, the frame tree, Kepler rails and system files. Nothing in a match uses it yet,
and nothing goes over the wire.

## Positions: `SpacePos`

A position in a system is three `long`s of 0.1 mm (`SpacePos.Unit`), in the ecliptic frame. The step is the same
everywhere, so a point 6,000 AU out is as exact as one at the star, and the difference of two positions is an exact
integer subtraction before it becomes metres. Reach: ±9.2 x 10^14 m (about 6,000 AU; the doc's 9 x 10^15 m is at 1 mm
steps). Bodies and frames must stay within `SafeReach` (a quarter of that) so sums can't overflow; files are refused past it.

Everything frame-local (an orbit's offset from its parent, a point in a body's frame, velocities) is a `Vec3d` (doubles,
metres). `QuatD` is a double quaternion; `Rotate` turns a frame's own axes into its parent's.

## Time: `SpaceTime` and `SpaceClock`

`SpaceTime` is a `long` count of nanoseconds since the system's epoch (the doc's BubbleTime), good for 292 years. A 60 Hz
tick isn't a whole number of nanoseconds, so `SpaceTime.TickNs(n)` is the exact floor of n / 60 s: 60 ticks are one
second exactly, and a century of ticks has no remainder. `SpaceClock` is stepped once a tick with a whole-number warp
(0 pauses, up to 100,000x); its time is always "time at the last warp change + `TickNs(ticks x warp)`", so it never drifts.

Periodic things (orbits, spins) take whole periods off in integer nanoseconds first (`KeplerOrbit.PhaseSeconds`), so a
planet 30 years on is where its phase says to well under a millimetre.

## Frames

| Kind (`FrameKind`) | Doc tier | Origin | Axes |
| --- | --- | --- | --- |
| `Ecliptic` | 2, ECL | the star | z the ecliptic's north, x zero longitude (right-handed) |
| `Orbit` | 3, ORB | the body's parent | x towards periapsis, z the orbit's normal |
| `Inertial` | 4, CCE | the body | the ecliptic's (doesn't turn): attitudes are stored here |
| `BodyFixed` | 4 | the body | z its spin axis (leaning by the tilt), turning with the ground |
| bubble (`StarSystem.BubbleOn`) | 5, BUB | a point on a body's ground | the game's: +Y up, -Z north, +X east |

`StarSystem.PoseOf(frame, time)` gives a `FramePose`: origin (`SpacePos`), axes (`QuatD`), the origin's velocity and the
frame's spin, all in the ecliptic frame. `ToEcliptic` / `FromEcliptic` move points in and out; `To(other, local)` moves a
point from one frame to another without rounding to the 0.1 mm grid; `VelocityToEcliptic` adds the frame's motion and
spin (w x r). The bubble at latitude 90 matches a planetoid map (`Source/Planets`): the map's centre, (0, -radius, 0) in
game coordinates, is the body's centre, and game +Y is the spin axis. No query allocates.

The galaxy tier (placing whole systems) comes with several systems (phase 6).

## Orbits on rails: `KeplerOrbit`

Closed orbits (eccentricity 0 to below 1) round a parent's GM: semi-major axis, eccentricity, inclination, longitude of
the ascending node, argument of periapsis and mean anomaly at the epoch. `StateAt(t)` solves Kepler's equation (Newton to
1e-15) and gives the position and velocity relative to the parent; `FromState` makes the orbit through a position and
velocity (what ships will use in phase 2). Bodies on rails are exact functions of time and never drift.

## System files: `Systems/*.json`

```json
{
  "name": "Dev System",
  "bodies": [
    { "name": "Slopstar", "gm": 1.32712440018e20, "radius": 6.957e8, "rotation": { "hours": 609.12, "tilt": 7.25 } },
    { "name": "Planetoid", "parent": "Slopstar", "radius": 10000, "gravity": 18, "map": "Planetoid",
      "orbit": { "au": 1.0, "e": 0.0167, "i": 0, "lan": 0, "argp": 102.9, "m0": 100.5 },
      "rotation": { "hours": 24, "tilt": 23.4 } }
  ]
}
```

| Key | Meaning |
| --- | --- |
| `name`, `description` | The system's name (default: the file name) and a note. |
| `bodies` | The star first (no `parent`, no `orbit`), then every body after its parent. |
| body `name` | Unique in the system. |
| `parent` | The body it orbits (listed before it). |
| `radius` | Ground radius, metres. |
| `gm` or `gravity` | GM (m^3/s^2), or the surface gravity (m/s^2, GM = g R^2). Exactly one. |
| `map` | The map it's played on (a planetoid map's name). Not used yet. |
| `orbit` | `a` (m), `km` or `au` (one of them); `e` (0 to below 1), `i` (0 to 180), `lan`, `argp`, `m0` (degrees, default 0). |
| `rotation` | `period` (s), `hours` or `locked: true` (turns once per orbit); `tilt` (0 to 180 degrees), `angle` at the epoch. |

Refused with a reason: unknown keys, a parent not listed before, an orbit that dips into its parent, that goes past
half the parent's Hill radius (the parent couldn't hold it), siblings whose orbits overlap, a body heavier than its
parent, anything past the reach, more than 8 parents deep.

`Systems/Dev.json` is the test system: a Sun-like star, the 10 km Planetoid (the Dev map, same radius and gravity: the
self-test checks they match) with a 1 km moonlet at 60 km, and Redrock, a 4 km planetoid at 1.5 AU.

## Command line

- `--space-sim [FILE] [--days N] [--every H]`: prints a system (default `Systems/Dev.json`) and where each body is every
  H hours (from its parent, from the star, and where the sun is overhead on each spinning body). Output goes to
  `callofslop.log`.
- `--check-system FILE`: does this file load? Exit code 0 or 1, the reason in the log.

## Next phases

2. Ships as massless test particles in the bodies' gravity: a symplectic step while coasting, an adaptive one for burns
   and close passes, force hooks (gravity tech, anomalies), a trajectory predictor for a map view.
3. The bubble in play: the Planetoid map placed in the Dev system (sun and other bodies where their orbits put them),
   with a floating origin. Needs constructs to fall towards a planetoid's centre (the planet plan's step 2).
4. Far bodies drawn in the sky, reversed-Z / logarithmic depth, day and night from the spin.
5. Summed gravity sources (mascons, tetrahedra) for stitched and irregular bodies, gravity-tech parts.
6. Many systems: the galaxy tier, low-attention simulation of far ships and bases, data patching for mods.
