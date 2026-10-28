# Mass horde (10,000-20,000 zombies)

The developer approved a plan (2026-10-27, board item `dev-mass-horde-mh01`) for hordes of tens of thousands of
zombies in three tiers. This page is the design and the numbers. It runs only on a map whose zombies section says
`"crowd": N` (the dev map Mass Horde Test); on every other map none of it runs, and no seeded digest changes.

## The tiers

- **Individuals** (tier 1): ordinary zombies (`Zombie`, pooled), with full AI, collision, hit boxes, animation and
  network state, exactly as on every other map. At most 300 at once (`individualCap` in the rules file's `crowd`
  section, also capped by the host's horde cap). They are the front: only individuals attack.
- **Crowd** (tier 2): `HordeCrowd` (`Source/Game/Horde/HordeCrowd.cs`). Each zombie is a particle in flat arrays
  (structure of arrays: position, velocity, heading, speed, separation push, next think tick, held flag, health), with
  no object per zombie.
  - **Flow field.** One field over the floor plan (2 m cells), a Dijkstra from every standing player's cell at once,
    built over several ticks (about 1,600 cells a tick) into a second buffer and swapped in when done. It is started
    again every 30 ticks, or 10 ticks after a player moves to another cell. A particle follows the field only when
    the way round is longer than the straight line could be on the grid; otherwise it walks straight at the nearest
    player, so the crowd doesn't file along the grid's eight directions.
  - **Thinks.** A particle reads its heading every 8-16 ticks (spread per particle). It walks every tick, slides along
    blocked cells, and is pushed apart from its neighbours through a counting-sort grid (0.7 m cells). Half the
    particles work out their push each tick, alternating. Individuals push particles twice as hard.
  - **Hit boxes.** A crowd zombie is a capsule for shots (`RayHit` walks the separation grid along the ray) and a
    point for blasts (`Within`). No animation or AI state.
- **Mass** (tier 3): `HordeCrowd.Mass.cs`. Far from every player there are no particles at all: per cell of the
  network grid (4 m) a count and a mean velocity. Every tick each cell sends a share of its count one cell downstream
  along the flow field (`count * speed * dt / cell`, accumulated as a flux), held back where the next cell is full
  (`massCapacity`, 40). Particles further than `massRange + massHysteresis` (60 m) from everyone merge into their
  cell (unhurt ones only, at most `massPerTick` a tick); cells within `massRange` (50 m) let theirs out again as
  particles at random open spots. Zombie types (another branch) will need a count per type per cell: the four places
  a count moves are listed in the class comment.

### Promotion and demotion

`Source/Game/ZombiesCrowd.cs`.

- **Seeding.** At a round's start, all N zombies are seeded over the field, at least 24 m from the middle of the
  start area: as particles up to 60 m out, straight into the mass beyond. The seed comes from the run's seed.
- **Promotion.** Each tick, the 6 particles nearest a player within 22 m become full zombies, in Chase at the
  particle's position, heading and health. Promotion stops while the individual cap is full; particles inside 22 m
  then hold.
- **Demotion.** A healthy individual goes back to the crowd if it is in plain Chase and has been more than 34 m from
  every player for 2 s. It keeps its position and velocity, and its speed comes from its id. At most 4 a tick. A
  hurt, burning, slowed, stunned or limb-lost zombie stays an individual.
- **Conservation.** The counts always add up: particles + mass + alive + killed = the round's total. Every kill path
  counts (shots, melee, mines, the nuke power-up, shots and blasts into the crowd, blasts into the mass).
  `ZRemaining` includes the crowd, and the round ends when the crowd and the horde are both empty.

The crowd round replaces normal spawning: nothing comes out of the map's spawners, and the player-count rescale is off.

## Combat

- **Shots.** A ray that hits no individual goes on into the crowd: the nearest crowd capsule on it takes the gun's
  damage (head above 1.5 m, with the head multiplier). The damage is applied per trigger pull (`CrowdShotApply`), so a
  shotgun's pellets add up on one zombie. A kill is credited exactly as an individual's (kills, headshots, frenzy,
  points, drop and power-up rolls).
- **Blasts.** Every particle in the radius takes the blast's damage falling off with distance, as individuals do;
  the dead leave the crowd by count. In the mass, each covered cell loses the share the blast covers (at least one
  when the cell's middle is inside the radius) if the damage there would kill one. Each dead one is a kill for the
  thrower, with at most 4 drop and power-up rolls per blast.

## Network

`Msg.ZCrowd` (protocol 71; `Source/Game/ZombiesCrowdNet.cs`). Every `netEvery` (6) ticks the server counts the crowd
(particles and mass together) per 4 m cell (count and mean heading as a byte round the compass) and sends each client
the cells that changed since what that client was last sent, reliable ordered; cells further than 60 m from the
client only every third time. A new crowd round or a new client gets a keyframe. Individuals go in `Msg.ZEnts` as ever.

The client keeps a picture of the cells (`CrowdPicture`) and fills each cell itself, the same way on every client:
zombie k of cell c stands at a hashed spot, drifting along the cell's heading and wrapping round inside it.

## Rendering

`Source/Render/CrowdRig.cs`, `CrowdInstances.cs`, `Renderer.Crowd.cs`. OpenGL 3.3 features only (no compute shaders,
no indirect draws: Mac).

- **Baked rigs.** The box zombie's walk cycle (`ZombieAnim`) is recorded once at 16 phases into a float texture: a
  row per frame, four texels per box (the transform's three axis rows with the colour in w, then the translation with
  the draw mode). Three levels of detail: near (20 boxes: the full body with hands, feet and eyes, to 35 m), mid (10
  boxes, one per limb, to 80 m) and far (3 boxes, standing, beyond).
- **Instances.** Each frame the CPU sorts every crowd zombie in view into the three levels: the host from its
  particles (a sphere test each), a client from its picture (a sphere test per cell, then at most `drawCap` (28)
  zombies a cell: the top layer of a dense blob). Eight floats an instance: position, yaw, metres walked (the walk
  cycle's frame, so a zombie's legs match the ground it covers), brightness, debug tint and held.
- **Draws.** One instanced draw per level. The vertex shader reads the box's transform for the frame with
  `texelFetch` and places it; the fragment shader is the box shader's own (sun, sky, baked lighting, fog).
- **Readability.** Crowd zombies use the individuals' palette and gait, so the front and the mass read as one horde;
  the dev map's `--crowd-tiers` tint (yellow crowd, orange held, darker mass) is a debug view, off by default.

## Crowd physics: the crowd as ground

`Source/Game/Horde/CrowdSurface.cs`, `ZombiesCrowdSurface.cs`. The crowd's per-cell counts (the same ones the network
sends; the server counts them every 6 ticks offline too, a client has its picture) make a height surface: a cell packed
at `surfacePacked` (2 a square metre, 0.7 m apart) is `surfaceHeight` (1.3 m) high, a thinner one lower in
proportion, bilinear between cell centres, no ground under `surfaceMin` (0.3 m). After each tick of a player's
movement (server, prediction and replay, from `ZBlockPlayer` after the zombie bodies):

- a player within `surfaceStep` (0.45 m) of the top stands on it (feet on the top, grounded, so they can walk over a
  packed horde and jump off it), slid down its slope at `surfacePush` (3 m/s per unit of slope) towards thinner crowd;
- a player deep inside is pushed down the slope twice as hard and lifted up onto it at `surfaceLift` (0.8 m/s), so
  nobody is buried in the mass;
- a push never goes into a wall (`ZBlockTryPush`), and nothing happens on another floor or in the air above.

The Cathedral's bell siege (`ZombiesClimb.cs`) keeps its own pyramid of individuals: the surface only needs counts, so
a map could stack density against a wall the same way later.

## Audio

`Source/Audio/CrowdRoar.cs`, `Audio.Crowd.cs`, `Sfx.CrowdRoar` (SynthZombie.cs). The crowd's cells are grouped into
clusters of 3 x 3 (12 m); every quarter second the four clusters that matter most to the listener (zombies over
distance squared) each get a looping roar at the cluster's centre of mass, as loud as its density (full at
`roarFull` (240) zombies, fewer by the square root, none under 12). A voice keeps its cluster while the cluster is
still there and eases to its spot and loudness, so the roar never jumps. The loop is 4 s of 28 low throats groaning
over each other at random over a rumble (two variants, so two clusters never sound in step), on the zombie voices
row. Individuals keep their own voices (`ZombieSounds`, 8 vocal and 8 step voices).

## Mass Horde Test

`Maps/Dev/MassHordeTest.json` is written by the map kit generator `Source/MapKit/Generators/MassHordeTest.cs`.

- **Layout.** A 160 x 160 m field with `"crowd": 10000`. There is a low plaza in the middle with the hill and the box.
  Four long walls at 30 m have gaps; there is an L of crates in each quarter and a ring of pillars at 62 m.
- **Debug views.** `--crowd-tiers` tints the tiers (yellow crowd, orange held, darker yellow mass); every individual
  has a red marker over its head. `--crowd-shots [count]` runs an offline game on the map and saves
  `smoke_crowd_<view>_<s>s.bmp` (from above and from near the player, at 0, 3, 8 and 16 s). In the container, run it
  under `xvfb-run -a -s "-screen 0 1280x720x24"`.

## Rules

Everything above is tuned in the `crowd` section of `Parts/Tables/ZombiesRules.json` (`CrowdTuning`): the think,
promote, demote, speed, separation, flow, net, mass, draw, surface and roar keys, each with its range checked at load.

## Measurements

The "Mass horde" self-test section runs crowds of 5,000, 10,000 and 20,000 for 4 s each: 1 s of warm-up, then 3 s
measured. Every count must add up on every tick. On Windows the worst tick must stay under 8 ms at every count and a
frame's instance preparation under 4 ms of CPU. Steady-state allocation must be zero (the tick, the network write, the
instance preparation, standing on the crowd, the roar). It also checks the network picture against what was sent,
promotion and demotion when the player walks off, a seeded replay, the baked rigs, shots and blasts into the crowd and
the mass, the surface (lift, stand, slide, client agreement) and the roar. The CI run's notice has the numbers.

Manual CI run on 2026-10-28 (the Windows runner; thread CPU cycles for the tick, thread CPU for the rest):

| Crowd | Server tick CPU mean | Worst | Crowd tick alone | ZCrowd per client | Host prep | Client prep |
| --- | --- | --- | --- | --- | --- | --- |
| 5,000 | 0.52 ms | 1.08 ms | 0.55 ms | 7.7 KB/s | 0.45 ms | 0.36 ms |
| 10,000 | 0.84 ms | 1.45 ms | 0.65 ms | 11.8 KB/s | 0.63 ms | 0.58 ms |
| 20,000 | 1.12 ms | 1.72 ms | 0.90 ms | 14.8 KB/s | 1.17 ms | 1.14 ms |

The server tick includes the 300 individuals' full AI (about 1.2 ms on its own in "Zombie hordes"). With the mass
tier on, most of a 20,000 crowd is counts, which is why 20,000 costs little more than 10,000. Managed memory for the
whole game with the crowd is 7 MB; the crowd's arrays are 0.9-1.8 MB. The spike's Windows figures (before the mass
tier: every zombie a particle) were 2.34 ms mean / 3.70 ms worst at 20,000.

## Horde Stress

Board item `dev-horde-stress-map-hs01` (2026-10-28). `Maps/Dev/HordeStress.json`, written by the map kit generator
`Source/MapKit/Generators/HordeStress.cs`; the round logic is `Source/Game/ZombiesCrowdStress.cs`, the lobby settings
`Source/UI/UiLobbyStress.cs`.

### The map

A 460 x 400 m valley on terrain (2 m samples, 40 m margin), mirror-symmetric about z = 0, `order` 988 (last in the Dev
pack). x runs across the valley:

- **The Overlook** (x = -175): a raised bench 30 x 130 m at 17 m on the west rise, gentle sides (the horde comes up
  them), low kerbs along its valley edge. The players' spawns, the hill and the box are here.
- **The valley floor** (x = 0): 12 m down, sandy, with the **rally point** (a yellow post) on a level patch.
- **The far slope** rising to a 24 m ridge at x = 185, with three **staging pads** at x = 150, z = -125, 0, 125 (red
  posts), kept apart by two low knolls. Nothing in between: the whole horde is in view from the Overlook.

### A round

The zombies section's `"stress"` (Maps/README.md, docs/zombies/ZOMBIES-MODE.md "Map format"):

1. **Staging.** The round's crowd (the host's **Horde size**, else the map's `"crowd"`, 2,500) is seeded in the mass
   tier, split evenly between the pads: each a round blob of cells packed to the mass capacity (40 a 4 m cell), so
   100,000 makes three blobs of about 90 m. The crowd is **held** (`HordeCrowd.Hold`: particles don't walk, the mass
   doesn't flow) for the countdown (10 s).
2. **Descent.** The crowd's **goal** (`HordeCrowd.SetGoal`) is the rally point: the flow field leads there instead of
   to the players (promotion, merging into the mass and release still go by the players). The hordes pour down the
   slope in streams.
3. **Merge.** Every half second a flood fill over the mass grid finds the blob of touching cells at the rally point.
   When it holds `"gather"` (75%) of the crowd and the crowd's mean distance to the rally point has stopped falling for
   10 s (packed, the tails queued in), the hordes have merged; after `"gatherSeconds"` (300 s) they go anyway.
4. **Advance.** The goal is cleared: the field leads to the players, and the merged horde comes up the west slope;
   near the players they become full zombies as on every crowd map (promoted crowd zombies are walkers).
5. **Endless** (lobby): another wave of the same size on the pads every `"waveSeconds"` (60 s), heading straight for
   the players, while the live horde stays under 200,000 (`Game.StressMax`).

At the crowd's 1.5 m/s the descent and merge take about two minutes and the advance another two.

### The crowd on terrain

The crowd tier was one floor height. On a terrain map now (a flat map keeps its one floor):

- Each flow cell is walkable where a body stands on the ground at its middle; a step to a neighbouring cell more than
  1.1 m up or down per metre (`HordeCrowd.MaxSlope`, about 48 degrees) is a wall for the field and for a particle's
  walk (`CanMove`).
- A crowd zombie stands on the ground under it (`GroundAt`): promotion, shots (the capsule from the ground), blasts,
  mass blasts, kill credit, the instances drawn (host and client: the client samples its own copy of the terrain, so
  `Msg.ZCrowd` is unchanged), the crowd surface (it rises from the ground there) and the roar.

### Changes to the mass horde found on the way

- **Packing.** Particles had no density limit: pressure from behind squeezed hundreds into a 4 m cell near the player
  (a cell's count saturated at 255 on the network, so 15,000 of a 100,000 crowd vanished from the clients' picture).
  Now a particle doesn't step into a separation cell (0.7 m) already holding 3 (`PackedCell`, about 6 a square metre),
  and the mass doesn't let a zombie out where it would land in one.
- **Sideways spill.** The mass moved each cell's share one cell along the field's heading and held it back when that
  cell was full, so a jam made single-file queues along the grid's eight directions. Now a full downstream cell sends
  the flow to the cell 45 degrees either side if that one is further down the field: the crowd spreads round a jam and
  packs like a fluid.
- **The separation grid** is sorted only over the window round the particles and individuals each tick (its size was
  the whole map's: on a 460 x 400 m map 375,000 cells a tick), and may cover up to 4,096 cells a side.
- **The host drew nothing** while the whole crowd was in the mass tier (it only drew from the particles when there were
  some); now it draws the mass cells too.
- **`Msg.ZCrowd`'s gaps** are 15 bits; a grid with more than 32,768 unchanged cells between two changes would have
  overflowed. The server now writes a filler entry (what the client already has, 32,768 cells on). No wire change.

### Limits (what holds at 50,000-100,000)

| What | Limit | At 100,000 on Horde Stress |
| --- | --- | --- |
| A stress round's size | 200,000 (`Game.StressMax`; presets to 100,000, custom to 200,000) | |
| A map's `"crowd"` | 32,000 (`ZombiesMap.MaxCrowd`; the stress size isn't bound by it) | |
| Particles at once | 40,000 (`HordeCrowd.ParticleCap`): the rest stays in the mass | 27,000-34,000 near the Overlook |
| Mass and network grid | 255 x 255 cells of 4 m: maps up to 1,020 m a side | 115 x 100 cells |
| Flow field | 1,024 x 1,024 cells of 2 m: 2,048 m | 230 x 200 = 46,000 cells, a new field every ~1.5 s |
| Separation grid | 4,096 cells of 0.7 m a side (2,867 m), windowed | |
| A mass cell | 40 zombies (`massCapacity`): 100,000 needs 2,500 cells (40,000 m2) | the merged blob fills the valley floor |
| Count a network cell | 255 (a byte); packing keeps it under ~140 | 40 at most in the mass, 0 cells saturated |
| One `Msg.ZCrowd` | 16 KB (`ZCrowdMaxBytes`; the rest next time) | keyframe 7.5 KB; 6-12 KB per 3 sends (~20-40 KB/s per client) |
| Instances drawn | no hard limit (arrays and GL buffers double); a mass cell draws its top 28 (`drawCap`) | 70,000-74,000 a frame (prep 2-7 ms CPU on the cloud box) |
| Individuals | 300 (`individualCap`) and the host's horde cap | 300 |
| Memory | the crowd's arrays | 6.9 MB |

Cloud box (2 shared cores, Mesa software GL, `--stress-shots 100000` at 3x speed): the zombies tick averages 0.5 ms
while the hordes descend and 1.6-1.9 ms while 27,000-34,000 particles and 300 individuals press on the Overlook; single
ticks reach 9-12 ms on that box (the first tick, about 9 ms, is the JIT). The Windows figures are the developer's to
take: the F9 window and the `[stress]` summary.

### Measurement

- **F9 window** (Counters): the tiers (individuals, crowd particles and how many are near, mass, killed of the round's
  total), the crowd's tick (mean and worst over the last second), the flow field's worst step, the crowd's bytes per
  client a second, the instances drawn per level of detail and the CPU to prepare them, and on a stress round its
  phase, wave and how much of the crowd is in the rally blob. The corner overlay has a one-line version.
- **Log summary** (`[stress] summary` and one line per size bracket, at a round's end, a new round, the run's or the
  session's end): per bracket of the live horde (under 1,000, 1,000+, 2,500+, ... 100,000+) the server tick's average
  and worst (wall clock), fps and the worst frame and frames under 30 fps, the peak particles, mass, individuals and
  instances drawn, and KB/s per client (all traffic, and the crowd's share). With endless waves the brackets show
  where the PC started to suffer.
- **Pictures**: `--stress-shots [size] [--stress-speed N]` (default 10,000 at 3x) runs an offline round and saves
  `smoke_stress_<above|eye|plan>_<staging|descending|merged|advancing>.bmp`, logging the counts, the zombies tick, the
  instances and what one client on the Overlook would be sent. In the container: `xvfb-run -a -s "-screen 0
  1280x720x24"`.

### Self-test

Section **"Horde stress"** (`Source/App/SelfTestHordeStress.cs`, about a second): 600 zombies at 6x speed with a 2 s
countdown: 200 on each pad, held through the countdown, descended and merged at the rally point within a minute,
advancing on the player on the Overlook (mean distance 190 m to 70 m) and promoted there standing on the terrain, the
counts adding up every tick, no allocation over half a second of the descent, and an endless wave adding another 300.
The map's own checks (loads, the Overlook and rally point probes, nav, symmetry, a bot match) are its test block in
"Map contract". Nothing big runs in the self-test.

