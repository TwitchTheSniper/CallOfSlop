# The space layer

Call of Slop is heading for several planetoids, in one star system and across systems. The space layer is what places
them: where every body is at any time, the frames they turn in, and (later) ships flying between them. It follows
Karl's architecture doc (six layers, five coordinate tiers), built in C# on what the game already has: no Flecs, Jolt or
CBT (Karl, 2026-11-03). The plan and the phases are in the project file `planets/SPACE-PLAN.md`; the code is in
`Source/Space/` (namespace `CallOfSlop.Space`); the "Space frames" and "Space ships" self-test sections check it (`Source/App/SelfTestSpace.cs`, `SelfTestSpaceShips.cs`).

**Phase 1:** positions, the clock, the frame tree, Kepler rails and system files. **Phase 2:** ships flying in a system's
gravity (below, "Ships"). Nothing in a match uses either yet, and nothing goes over the wire.

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

## Ships (phase 2): `SpaceShips`

Ships, wrecks and thrown things outside a planetoid's bubble are **massless test particles** in the summed gravity of the
bodies on rails, plus thrust and force hooks. `SpaceShips` keeps them as arrays by index and is stepped on the host to
the space clock's time (`Advance(t)`); `SpaceFlight` is the clock, the warp rule and the ships stepped together once a tick.
Nothing allocates per step (the self-test measures it).

**Primary and hand-over.** Each ship is held relative to its *primary*: the smallest body whose sphere of influence
(Laplace, a (m/M)^0.4) it is in, as a double offset and velocity in that body's inertial frame. That frame moves and
accelerates with the body along its rails, so the ship feels the other bodies' pull less the primary's own rail
acceleration (the tide), which is tiny near a body. Crossing a sphere of influence (into a child's, or 2% past the
primary's own) hands it over: the offset changes by an exact fixed-point difference, the velocity by the bodies' rail
velocities. The model is exactly the doc's: point masses on rails, a ship pulled by all of them. (A body on rails doesn't
wobble round its moons, so near the Planetoid the moonlet's pull isn't cancelled by the Planetoid moving too; it's a
few 10^-4 m/s^2.)

**Three steps**, picked per ship each tick (`ShipMode`):

| Mode | When | How |
| --- | --- | --- |
| `Coast` | No thrust, others' pull under 10% of the primary's | Wisdom-Holman kick-drift-kick: the drift is an exact two-body step round the primary (`UniversalKepler`: universal variables, any conic, Laguerre-Conway), the kicks are everything else. Symplectic: energy error stays bounded over thousands of orbits. Steps are at most 1/48 of the orbit and of each perturber's time scale (its own orbit time at that distance, and distance over closing speed), rounded down to a power of two so they rarely change size |
| `Adaptive` | Thrust, `ShipFlags.ForceAdaptive`, or others' pull over 10% of the primary's (back to `Coast` under 5%) | Dormand-Prince 5(4) with error control (0.1 mm and 10^-7 m/s a step by default); bodies inside a short chunk come from a cubic through its ends instead of a Kepler solve per stage |
| `Rail` | No thrust and others' pull under 10^-6 of the primary's (off again over 4 x 10^-6) | Patched conic: the ship's own two-body orbit from the moment it went on, exact at any warp (whole periods come off in integer ns). A sphere of influence crossed takes it off; a burn takes it off |

Ships in a span longer than a few steps are moved in chunks of at most 8 steps each, so step sizes are looked at again
before a ship gets much closer to anything. **Force hooks** (`ISpaceForce`, `SpaceShips.Forces`) add an acceleration to
every ship in every step (gravity tech, anomalies, repulsors later); `PointMassForce` is the simplest. A hook must be a
pure function of position, velocity and time (the predictor calls it at future times), and stops a ship going on rails
when it matters.

**Warp.** `SpaceShips.MaxWarp` is the fastest warp at which every ship still takes its full steps within a per-tick
budget (64 coasting substeps or 16 adaptive steps a tick); ships on rails don't limit it. The warp rule
(`SpaceWarpRule.Effective`) is the architecture's **provisional** proposal, not yet confirmed by Karl: only the host sets
the warp; above 1x only while nobody is busy in a bubble (`SpaceFlight.AnyoneBusy`, set by the game from phase 3); never
past `MaxWarp`; the host may always pause (0).

**Predictor.** `Predict(ship, every, ecl, frame, inFrame)` writes where a coasting ship will be every `every` seconds
(the map view's path), in the ecliptic frame and, if wanted, in any frame (a body's inertial frame for the path round it,
its body-fixed frame for the track over its ground). It uses the same steps on a private copy, with its own step sizes,
so it parts from the real flight by the coasting step's error: about a metre after 2 hours in a low orbit the moonlet
tugs, far less in a quiet orbit, nothing on rails. No allocation after the first call.

**Multiplayer.** The host steps ships; clients don't. A `ShipState` (time, primary, offset, velocity: 58 bytes,
`Write`/`Read`) is what a host would send. A client's `ShipTrack` keeps the last four and samples any time by carrying
the two either side along their two-body orbits to that time and blending them, so the path stays on the orbit even
when one packet covers minutes of warped time (0.1 mm from the host at 1x, about 0.3 m at 1000x in the moonlet-tugged
orbit). The host's steps are deterministic: the same inputs give the same bits. Nothing is on the wire yet; the
protocol bump comes with the bubble (phase 3).

**Budget.** 200 ships and 32 bodies take about 0.5 ms a tick at 1x (enforced under 1 ms on Windows, logged elsewhere),
allocating nothing.

## Command line

- `--space-sim [FILE] [--days N] [--every H]`: prints a system (default `Systems/Dev.json`) and where each body is every
  H hours (from its parent, from the star, and where the sun is overhead on each spinning body). Output goes to
  `callofslop.log`.
- `--check-system FILE`: does this file load? Exit code 0 or 1, the reason in the log.
- `--space-ships [FILE] [--hours H] [--warp W]`: demo ships in a system (default Dev): a circular orbit round every body
  but the star and one leaving the first planetoid, flown H hours at W x warp through `SpaceFlight` (default 6 h at 100x);
  prints each ship's primary, step and distance every hour, then the cost per tick and the hand-overs.

## Next phases

2. Done (above, "Ships"). Still to come with it: a debug map view drawing the predictor's path, and ships' attitudes.
3. The bubble in play: the Planetoid map placed in the Dev system (sun and other bodies where their orbits put them),
   with a floating origin. Needs constructs to fall towards a planetoid's centre (the planet plan's step 2).
4. Far bodies drawn in the sky, reversed-Z / logarithmic depth, day and night from the spin.
5. Summed gravity sources (mascons, tetrahedra) for stitched and irregular bodies, gravity-tech parts.
6. Many systems: the galaxy tier, low-attention simulation of far ships and bases, data patching for mods.
