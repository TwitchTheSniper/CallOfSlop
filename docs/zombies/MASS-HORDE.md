# Mass horde (10,000-20,000 zombies)

The developer approved a plan (2026-10-27, board item `dev-mass-horde-mh01`) for hordes of tens of thousands of
zombies in three tiers. This page covers the **spike**: tiers 1 and 2 on a dev map, to prove the server tick budget
before the rest is built. The rest of the plan (the mass tier, combat against the mass, the network, rendering, crowd
physics, audio) waits for the developer's go-ahead.

## The tiers in the spike

- **Individuals** (tier 1): ordinary zombies (`Zombie`, pooled), with full AI, collision, hit boxes, animation and
  network state, exactly as on every other map. There are at most 300 at once (`CrowdTuning.IndividualCap`, also
  capped by the host's horde cap).
- **Crowd** (tier 2): `HordeCrowd` (`Source/Game/Horde/HordeCrowd.cs`). Each zombie is a particle in flat arrays
  (structure of arrays: position, velocity, heading, speed, separation push, next think tick, held flag), with no
  object per zombie.
  - **Flow field.** One field over the floor plan (2 m cells), a Dijkstra from every standing player's cell at once.
    It is rebuilt every 30 ticks, or 10 ticks after a player moves to another cell. A particle follows the field only
    when the way round is longer than the straight line could be on the grid; otherwise it walks straight at the
    nearest player. This stops the crowd filing along the grid's eight directions.
  - **Thinks.** A particle reads its heading every 8-16 ticks (spread per particle). It walks every tick, slides along
    blocked cells, and is pushed apart from its neighbours through a counting-sort grid (0.7 m cells). Half the
    particles work out their push each tick, alternating. Individuals push particles twice as hard.
  - **No hit boxes.** Nothing can shoot a particle. The front is always individuals.
- **Mass** (tier 3, not in the spike): per-cell counts and mean velocities, for the interior of a blob.

### Promotion and demotion

`Source/Game/ZombiesCrowd.cs` does this, and only on a map whose zombies section has `"crowd": N`.

- **Seeding.** At a round's start, all N zombies are seeded as particles over the field, at least 24 m from the
  middle of the start area. The seed comes from the run's seed.
- **Promotion.** Each tick, the 6 particles nearest a player within 22 m become full zombies. A promoted zombie starts
  in Chase at the particle's position and heading. Promotion stops while the individual cap is full; particles inside
  22 m then hold (drawn orange).
- **Demotion.** A healthy individual goes back to the crowd if it is in plain Chase and has been more than 34 m from
  every player for 2 s. It keeps its position and velocity, and its speed comes from its id. At most 4 are demoted a
  tick. A hurt, burning, slowed, stunned or limb-lost zombie stays an individual.
- **Conservation.** The counts always add up: crowd + alive + killed = the round's total. Every kill path counts
  (shots, melee, mines, the nuke power-up). `ZRemaining` includes the crowd, and the round ends when the crowd and the
  horde are both empty.

The crowd round replaces normal spawning: nothing comes out of the map's spawners, and the player-count rescale is off.

### Gating

On every map without `"crowd"`, `CrowdTick` never runs. `Crowd.Count` is 0, so the round-end condition, `ZRemaining`
and the alive cap are unchanged, and no seeded digest changes. The spike changes nothing on the wire.

## Mass Horde Test

`Maps/Dev/MassHordeTest.json` is written by the map kit generator `Source/MapKit/Generators/MassHordeTest.cs`.

- **Layout.** A 160 x 160 m field with `"crowd": 10000`. There is a low plaza in the middle with the hill and the box.
  Four long walls at 30 m have gaps; there is an L of crates in each quarter and a ring of pillars at 62 m.
- **Debug drawing** (host only, because the spike sends no crowd state). Crowd particles are drawn yellow, or orange
  while held back by the cap. Every individual has a red marker over its head.
- **Pictures.** `--crowd-shots [count]` runs an offline game on the map and saves `smoke_crowd_<view>_<s>s.bmp` (from
  above and from near the player, at 0, 3, 8 and 16 s). In the container, run it under
  `xvfb-run -a -s "-screen 0 1280x720x24"`.

## Measurements

The "Mass horde" self-test section runs crowds of 5,000, 10,000 and 20,000 for 4 s each: 1 s of warm-up, then 3 s
measured. Every count must add up on every tick. At 10,000 and below, the Windows worst tick must stay under 8 ms.
Steady-state allocation must be zero. The CI run's notice has the numbers. These results come from the manual CI run
on 2026-10-28 (the Windows runner, thread CPU cycles):

| Crowd | Server tick CPU mean | Worst | Crowd tick alone | Flow field build | Network sketch per client | Crowd arrays |
| --- | --- | --- | --- | --- | --- | --- |
| 5,000 | 0.72 ms | 3.09 ms | 0.63 ms | 2.4 ms | 8.7 KB/s | 0.8 MB |
| 10,000 | 1.13 ms | 3.11 ms | 1.08 ms | 1.5 ms | 15.9 KB/s | 1.0 MB |
| 20,000 | 2.34 ms | 3.70 ms | 1.98 ms | 1.0 ms | 27.3 KB/s | 1.6 MB |

The server tick includes the 300 individuals' full AI, which costs about 1.2 ms on its own ("Zombie hordes": 300
zombies, 1.15 ms). The worst tick is the one that rebuilds the flow field (1-2.4 ms in one go). Managed memory for the
whole game with the crowd is 6-7 MB.

The **network sketch** is not wired up. It counts particles per 4 m cell (40 x 40 cells on this map) every snapshot
and charges 2 bytes for each cell whose count changed, plus 4 bytes of header. A full keyframe is about 4.5 KB.
