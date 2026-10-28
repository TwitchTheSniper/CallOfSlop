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
