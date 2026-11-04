# The space layer

Call of Slop is heading for several planetoids, in one star system and across systems. The space layer is what places
them: where every body is at any time, the frames they turn in, and (later) ships flying between them. It follows
Karl's architecture doc (six layers, five coordinate tiers), built in C# on what the game already has: no Flecs, Jolt or
CBT (Karl, 2026-11-03). The plan and the phases are in the project file `planets/SPACE-PLAN.md`; the code is in
`Source/Space/` (namespace `CallOfSlop.Space`); the "Space frames" and "Space ships" self-test sections check it (`Source/App/SelfTestSpace.cs`, `SelfTestSpaceShips.cs`).

**Phase 1:** positions, the clock, the frame tree, Kepler rails and system files. **Phase 2:** ships flying in a system's
gravity (below, "Ships"). **Phase 3:** the bubble: a map a body names is played in its system, with the real sun and moon
in its sky, and the clock and ships go over the wire (below, "The bubble"; the `"Space bubble"` self-test,
`Source/App/SelfTestSpaceBubble.cs`). **Phase 4:** seeing space: the other bodies and ships drawn in the sky (below,
"Seeing space"; the `"Space sky"` self-test, `Source/App/SelfTestSpaceSky.cs`). **Leaving the bubble:** a built ship flies out of a planetoid's sky into orbit and back
(below; the `"Space launch"` self-test, `Source/App/SelfTestSpaceLaunch.cs`). **Phase 6:** many systems: the galaxy
(at most ten systems placed in light-years), the Jump Drive and parked ships (docs/space/GALAXY.md; the `"Galaxy"`
self-test).

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
spin (w x r). The bubble at latitude 90 matches a planetoid map (`Source/Planets`): the map's centre, the game's origin,
is the body's centre, and game +Y is the spin axis. No query allocates.

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
| `map` | The map it's played on (a planetoid map's name): that map is played in this system ("The bubble"). |
| `orbit` | `a` (m), `km` or `au` (one of them); `e` (0 to below 1), `i` (0 to 180), `lan`, `argp`, `m0` (degrees, default 0). |
| `rotation` | `period` (s), `hours` or `locked: true` (turns once per orbit); `tilt` (0 to 180 degrees), `angle` at the epoch. |
| `color` | How the body looks from afar: `[r, g, b]`, each 0 to 1 (default: a rock tone picked from its name). |
| `lumps` | A lumpy body's gravity: `{ "sources": [...] }`, a map's summed sources in the body's own frame, their masses shares of its GM (docs/space/GRAVITY.md "Lumpy bodies"). Not the star. |

Top level, `"start": { "days": N }` (or `seconds`, `hours`; 0 to 100 years) is where a new game's space clock starts
(default the epoch). The Dev system starts on day 252, the Planetoid's northern midsummer, so its polar spawn has the sun
23 degrees up all day; started at the epoch it was in polar night.

Refused with a reason: unknown keys, a parent not listed before, an orbit that dips into its parent, that goes past
half the parent's Hill radius (the parent couldn't hold it), siblings whose orbits overlap, a body heavier than its
parent, anything past the reach, more than 8 parents deep.

`Systems/Dev.json` is the test system: a Sun-like star, the 10 km Planetoid (the Dev map, same radius and gravity: the
self-test checks they match) with a 1 km moonlet at 60 km and Bluestone, a 3 km sister planetoid 1,500 km out, and
Redrock, a 4 km planetoid at 1.5 AU. Distances are "mixed" (Karl, 2026-11-03): moons and sister planetoids close (short
hops by ship, discs in the sky), other planets far (AU, needing warp). They are only numbers here, so easy to change.

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

## The bubble (phase 3): `SpaceBubble`, `Game.Space`

A map that a body names (`"map"`) is played as that body's ground: the game World is the body-fixed frame at a point on
it (`SpaceBubble`: by default its axes are those at latitude 90 and its origin is the body's centre, as the planetoid
maps have it: game +Y is the spin axis). The ground never moves under the 60 Hz
physics; the sky does. `StarSystems` loads every `Systems/*.json` once and finds a map's system and body
(`ForMap`), with a hash of each file.

- **The host** (`Source/Game/GameSpace.cs`) makes the map's `SpaceSession` when a map loads (the space time carries on
  when the next map is in the same system, else it starts at the file's `"start"`), and steps its `SpaceFlight` once a
  tick: the clock under the warp rule, then the ships.
- **The sky** (`Source/Render/Sky/SpaceSky.cs`): each frame the map's sky takes the star's real direction
  (`SpaceBubble.SunDirection`, from the camera) and draws every other body and the nearest ships (phase 4, "Seeing
  space" below; the moonlet is about 1.9 degrees across from the pole). Light and fog follow the same sun, so a planetoid
  has day and night as it turns (24 h on the Planetoid at 1x), and seasons. The map's own sun and moon come back when the
  game leaves the bubble. Everything is a pure function of the system and the clock, so each peer works it out itself.
- **The wire** (`Msg.Space`, protocol 125): `Bubble` (reliable, to each joiner and on a map change: map, system file and
  hash, body, the clock), `Clock` (tick, time, warp: once a second unreliable, at once and reliable on a warp change) and
  `Ships` (10 Hz, up to 18 ships a packet from a cursor that takes turns, each its id and `ShipState`). A client runs its
  own clock between the host's and keeps a `ShipTrack` per ship id (`SpaceShips.Ids`: ids stay when indices shift); a
  ship not heard of for 3 s is gone. A client whose system file differs from the host's (by hash) gets no bubble and
  says so in the log.
- **Warp.** The host asks for a warp (`Game.RequestSpaceWarp`, or F7 > Space); `SpaceWarpRule.Effective` is still the
  provisional rule. "Busy" (`Game.SpaceAnyoneBusy`) is only a ship in space burning its engines (its thrust needs real-time
  steps). Life in the bubble (walking, driving, building, anything awake) doesn't count: the bubble's physics runs at 60 Hz
  whatever the space clock does, so it never stops the orbits (Karl, 2026-11-04: until then anyone moving dropped the
  warp to 1x, and the sky and orbits seemed to stop).
- **The Space window** (F7, the Space button): the system and body, the space time and warp, the sun's elevation where you
  stand, the moon, the ships; the host's warp buttons.

Not in phase 3 yet:
- **Moving the origin (floating origin): not done, by choice** (Karl, 2026-11-04). Shifting the World would move every
  player, construct, projectile, ragdoll, effect and cached tile on one tick on every peer, and with friends spread
  over a planetoid one origin can't suit everyone. Instead the origin is the planetoid's centre (PLANETS.md "Origin"),
  so the whole ground is about a radius out; a planetoid much past 16 km radius would need double precision positions.
- **Leaving a bubble**: done since, below ("Leaving the bubble").

## Leaving the bubble: `Game/GameSpaceLaunch.cs`

Travel is by built ships (Karl, 2026-11-03). On a planetoid map played in a star system, a free construct whose centre of
mass climbs past the **sky's edge** (the map's half size from the planet's centre, where players' sky already ends:
`World.ClampToSky`; 12.6 km on the Planetoid, so 2.6 km above its radius) becomes a ship of the host's `SpaceFlight`: a
test particle round the Planetoid in the gravity of every body on rails, coasting on the symplectic step or on rails, at
any warp. Its state is the construct's, through the bubble's frame (the ground's spin added).

- **The construct stays as the ship's picture.** `Construct.SpaceShip` (the ship's id) puts its physics to sleep; every
  tick the host poses it from the ship (place, speed, a spin for clients), so its poses reach clients the usual way, its
  riders stay seated (they can't get out in space) and see the planetoid turn below. Nothing new goes over the wire.
- **Flying it in space.** Its own thrusters push the ship (`ConstructPhysics.SpaceThrust`: a wire sets one, else W and S
  work those facing the seat's way, Shift those that push up; fuel and power are used as in the air), through the centre
  of mass. A and D turn it about the local up, and it is kept level over the ground below (ships have no attitude of their
  own yet). The driver's line shows the height, speed and the orbit's lowest and highest points (worked out from the
  construct's pose on every peer).
- **Coming back.** 200 m under the edge it is a physics body again, with the ship's speed, the ship goes, and the hover
  hold catches it at the height it came in at.
- **Warp.** A driver who only coasts isn't busy, so the host's warp runs in orbit; a burn holds it at 1x.
- **Far from the middle (render origin).** A ship 150 km up shook badly (Karl, 2026-11-04): with float positions out
  there (1.6 cm steps) and a camera at the same distance, the box shader's view-projection cancelled two numbers of 150 km
  each, and parts jumped tens of pixels a frame against each other. Now a camera more than 4 km from the World's origin
  draws boxes relative to a render origin (`Renderer.SetCameraAt`, `Program.RenderOrigin`): seated, the construct's drawn
  position (`Game.LocalSeatAnchor`, `ConstructRender.SeatPoseFrom`), else the eye. ConstructRender builds each body's frame
  from its position less the origin (`Renderer.Relative`), so the ship and the camera on it agree to the bit at any
  distance; other world draws (players, effects) are moved to the origin in `Renderer.Draw` (sub-centimetre out there).
  `ViewProj`, `View` and `CameraPosition` stay in world coordinates for the other renderers (ground, sky, foliage), whose
  far geometry loses nothing that shows. The World itself still doesn't move (see "Moving the origin" above).
- **Clouds from above.** The sky draws its clouds as flat layers overhead along each pixel's true direction (not the
  planetoid's dipped horizon, which lifts nearly every ray high up and smeared the layer over the whole view), and on a
  planetoid they fade out as the camera climbs through the layer's altitude (`SkyRenderer.CloudsAt`).
- **Flying up there** (planetoid maps only, nothing changes elsewhere): the hover hold measures its height from the
  planet's centre and pushes along the local up (`HoldHeight`, `HoldUp`), so a flyer holds its height over the ball; holding
  Shift or Ctrl climbs or sinks faster the longer it is held (to 120 m/s), never asking more than half the lifters' spare
  push; a level hold, its integral and a counter to the side jets' twist keep a small flyer level. The air thins with
  height (`AirAt`, e every 1.2 km): the flight drag, the damping and the speed limit (60 m/s over the air's thickness, up
  to 2 km/s) follow it, so a flyer can reach orbital speed (about 400 m/s at the edge) once it is up.
- **The Orbiter** (`Constructs/Blueprints/Orbiter.json`): the test ship. Two seats (the driver's, linked to the Driving
  Module, is the last) on fifteen fuel tanks, four Small Jets under it to lift and land on, one low on each side to push it on.

## Landing on another planetoid (`GameSpaceLanding.cs`)

A body with a `"map"` in its system file can be landed on (Dev's Bluestone has `Maps/Dev/Bluestone.json`, a 3 km
planetoid). The rule is **the group travels together** (Karl, 2026-11-04): the game holds one planetoid at a time, so a
ship enters another only with every player (not bots) seated in it, and with no couplings, cross wires, hoses, world links
or connectors to other bodies.

- **The edge.** A ship in space that comes within `LandEdge` of another mapped body (the map's half size, at least its
  radius plus 500 m) lands if it can. If it can't, it is held at that distance with its inward speed taken off, and the
  people in it are told why every 3 s ("Landing on Bluestone: everyone travels together: waiting for Alpha to sit aboard").
- **The hand-over** (`ServerTickLanding`, on the tick after the ship crosses): the world is saved; everything on the
  planetoid being left, except the ship and what it carries, is kept in the world's `planets/<map>` folder as a world save
  of its own; the map changes (no vote, no lobby) and `WorldLandApply` loads the new planetoid's kept file if there is one
  (a first visit has none), then the ship's bodies and stores from the main save under fresh ids, then the people, who sit
  where they sat. The ship keeps its place round the new body and is in its bubble at the edge; the driver flies it down.
- Coming back works the same way: the builds left behind are where they were.

Not yet: several bubbles (each person on the planetoid they choose, Karl's later option C); a landing between systems
(only bodies in the bubble's system); standing riders count as not aboard.

Not yet: ships far from the bubble still live in the World's floats (drawn steady via the render origin, but their pose
steps by 6 cm at 1,000 km, so very far out the whole ship moves in small jumps until the World's origin can move or the
ship lands in another body's bubble); seen from above, clouds just fade (no cloud deck under you); players standing (not seated) on a ship when it leaves stay in the bubble's sky. The bubble's gravity stays the
surface's all the way to the edge; space's falls off with distance.

## Seeing space (phase 4): `SpaceSkyView`

A bubble's sky shows, besides the sun, every other body of the system and the nearest ships (`SpaceSkyView`, filled each
frame by `SpaceSky.Update`, drawn by the sky shader in `SkyRenderer`):

- **Bodies** (up to 8, the biggest-looking): every body but the star and the bubble's own, at its real direction and
  angular size from the camera, in its file `color`. Each is a sphere lit from its own side (the way from *it* to the
  star, so a far planet's phase is right too), with a rock pattern on its ground that turns with its day (the shader gets
  each body's body-fixed axes: a locked moon keeps one face to you, Bluestone turns every 9 h). Farthest first, so a
  nearer body covers a farther one, and drawn over the sun's disc, so a moon in front of the sun eclipses it. A body
  behind the star's disc isn't drawn. A body smaller than a pixel is a point of light: brightness from its phase and
  size, never fainter than a dim star so other worlds can be found at night (Redrock at 0.5 AU is a point; Bluestone
  a few pixels; the moonlet a disc).
- **Eclipses.** `SpaceSkyView.SunVisible` is how much of the star's disc a point sees past every other body (flat-sky
  disc overlap, `DiscCover`), so the moonlet goes dark when it passes through the Planetoid's shadow (once an orbit).
- **Ships** (up to 16, the nearest): points of light while the sun is on them (dark in a shadow), a little brighter
  when lit from behind you; hidden behind a far body's disc. The host reads its own ships, a client samples its
  `ShipTrack`s at the frame's space time.
- **In the air.** A disc is drawn as the sky's own colour plus its light, so a new moon by day is hard to see and the
  lit crescent shows as a pale shape, like the real moon; at night it covers the stars.
- **True directions.** On a planetoid the sky's gradient is bent down to the ground's limb (`uDip`); the sun, the moon
  and everything above are drawn at their true directions, so what you see matches the light and the aim.
- **The far layer.** Everything is drawn by the sky's full-screen pass at the far plane, behind the world, so far bodies
  need no depth range of their own. Only image skies (cube maps, panoramas) don't draw bodies yet.
- **Cost.** At most 8 bodies and 16 ships a sky pixel (a dot product each, a few texture fetches inside a disc); no
  allocation (the "Space sky" section checks 600 frames with 20 ships).

**Depth range: not changed (yet).** The plan put reversed-Z / log depth here. Far bodies don't need it (they're in the
sky layer), and the game draws into the default framebuffer with a 24-bit fixed-point depth buffer, where reversed-Z
gains nothing (it needs a float depth buffer, so an off-screen target for the whole scene), and a log depth write in
every shader turns off early depth tests, which costs fill rate everywhere. On the Planetoid (near 0.05 m, far up to
40 km) the depth step is about 1 m at 1 km and 30 m at 5 km; that only shows as flicker where two far surfaces nearly
touch. If it shows in play, the cheapest fix is drawing far ground in its own depth slice (a second near/far pass), and
it gets its own branch.

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
3. Done (above, "The bubble", and "Leaving the bubble"), apart from moving the World's origin. Travel is decided now (Karl,
   2026-11-03): players build ships that fly, so leaving a bubble means a built ship becoming a phase 2 ship. Time warp
   with friends is shelved: the host-only provisional rule stays.
4. Done (above, "Seeing space"), apart from the depth range (see there). Lighting from local up for props and
   players, and drawing bodies in image skies, are still to do.
5. Done: gravity as a sum of sources, the Gravity Well and Gravity Generator parts and the Twin Worlds Dev map
   (docs/space/GRAVITY.md). Lumpy bodies in `Systems/*.json` are done too (`"lumps"`, GRAVITY.md "Lumpy
   bodies"). Still to come: a big stitched pair on the planetoid ground (Twin Worlds is Pocket Planet sized).
6. Many systems. Started: the galaxy tier, the Jump Drive and parked ships (docs/space/GALAXY.md). Still to come:
   factions and ship and station templates, low-attention simulation of far ships and bases, data patching for mods.
