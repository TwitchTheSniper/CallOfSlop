# Co-op (PvE): design

Status: phase 1 (foundation + one mission, **Silo Breach** on World Tour: Silo). This document is the plan for the
whole feature; the "Phases" section at the end says what is built and what comes next.

A small group of players (1 to 4, up to 6 with bots) plays a **mission**: an ordered list of **stages**, each with one
**objective** (kill things, blow things up, hold a point, get somewhere, carry something). Enemies are not players: they
are a cheap server-side entity, many more of them than the 32 player slots allow, fed in continuously by a **spawn
director** that keeps an "alive budget" topped up out of the players' sight and paces the fight in waves.

## 1. Mode structure

```
Mission (Maps/<Pack>/Missions/<Name>.json)
  start spawns                     where the squad deploys (checkpoint 0)
  enemies { budget, perPlayer, max, mix }
  stages[]                         played in order
    type: eliminate | destroy | defend | reach | collect
    text                           the objective line on the HUD
    focus / at / targets / items   where it happens (markers)
    checkpoint[]                   respawn points while this stage runs (default: the previous stage's)
    spawnZones[]                   where the director may spawn (default: nav nodes in a ring round the focus)
    director { budget, intensity, waves }   per-stage overrides
```

- The game mode is **Co-op** (`GameMode.Coop` = 7, module `Source/Game/Modes/Coop/CoopModule.cs`). It is offered in the
  lobby only for maps that have at least one mission (`GameModeModule.Fits`), like Zombies needs a Zombies map.
- **One side**: every person and bot is on the player side (`CoOp`: no friendly fire, friendly colours, Blue). Bots on
  the player side are optional co-op bots (the lobby's bot count), and the mode's player cap is 6 (`MaxPlayers`).
- **Server-authoritative**: the server runs the stage machine, the director, every enemy and every hit. Clients get the
  objective state in the mode-state tail (10 Hz) and the enemies in their own message (20 Hz).
- **No time limit, no score limit, no map vote**: the match ends when the mission is complete (victory) or failed (out of
  retries on Hard/Insane); then it restarts on the same map.

### Checkpoints and respawning

| Rule | Value |
|---|---|
| Death | You are out for `respawnSeconds` (Easy 6, Normal 10, Hard 15, Insane 20), then respawn at the current checkpoint. |
| Checkpoint | Each stage start is a checkpoint: its `checkpoint` spawns (or the last stage's, or the mission start). |
| Wipe | Nobody on the player side alive (after someone was) = **wipe**: every enemy is removed, the stage restarts from its checkpoint (stage progress back to zero; earlier stages stay done), everyone respawns there after a 4 s "regrouping" pause. |
| Retries | Unlimited on Easy/Normal; Hard 5 and Insane 3 wipes per mission, then "Mission failed" ends the match. |
| Joining mid-mission | You deploy at the current checkpoint. |

### Difficulty and player count

Difficulty is the lobby's **Bot difficulty** (Easy / Normal / Hard / Insane): one knob the host already has, applied to
the enemies (the player-side bots keep using it for their own skill too).

| | Easy | Normal | Hard | Insane |
|---|---|---|---|---|
| alive budget multiplier | 0.7 | 1.0 | 1.2 | 1.4 |
| enemy damage (x weapon damage) | 0.30 | 0.45 | 0.60 | 0.75 |
| aim spread multiplier | 1.6 | 1.2 | 1.0 | 0.85 |
| reaction before first shot (s) | 0.9 | 0.65 | 0.5 | 0.35 |
| grenades | rare | yes | yes | often |
| respawn (s) | 6 | 10 | 15 | 20 |

Alive budget = `(budget + perPlayer x (players - 1)) x difficulty`, capped at the mission's `max` and the engine's
`Game.PveMaxAlive` (96). The Silo mission: 26 + 9 per player, max 80, so 1 player Normal 26, 4 players Normal 53,
4 players Insane 74.

## 2. Objective types

| Type | Completes when | Can fail by | Data |
|---|---|---|---|
| **eliminate** | `count` enemies killed during the stage, or the named target(s) dead | (timeLimit) | `count`, optional `target {arch, name, at, health}` (spawned at the start, a boss with a marker) |
| **destroy** | every target destroyed | (timeLimit) | `targets[] {name, at, radius}`, `plantSeconds`, `fuseSeconds`. Stand at a target to plant (progress bar; interrupted if you leave); the fuse ticks (beeping), the charge blows: an explosion that breaks props, building doors and destructible structures round it (BlastProps / BlastBuildings / BlastStructures) and kills enemies in reach. |
| **defend** | `seconds` of holding pass | the **loss meter** fills: it rises while enemies stand in the zone and no player does (`loseSeconds` to fill), drains otherwise | `at`, `radius`, `seconds`, `loseSeconds` |
| **reach** (extract) | every living person (bots if no people) is inside the zone at once | (timeLimit) | `at`, `radius` |
| **collect** | `count` items delivered (or just picked up when there's no `deliver`) | (timeLimit) | `items[] {name, at}`, optional `deliver {at, radius}`. Walk over an item to pick it up; a carrier who dies drops it where they fell. |

Any stage can have `timeLimit` (s): running out fails the stage (a checkpoint restart like a wipe). Every stage can
override the director (budget, intensity, the enemy mix, spawn zones).

**Escort** (a moving defend zone) and **interact-key** plants are later phases: phase 1 plants by standing at the
target (no new input message).

## 3. Enemies

### Archetypes (Source/Game/Pve/PveArchetypes.cs)

| Archetype | Weapon (existing weapon defs) | Health | Behaviour |
|---|---|---|---|
| **Rifleman** | Vanguard AR | 100 | the backbone: advances by cover, fires bursts at mid range |
| **Rusher** | Breacher Pump | 90 | sprints straight at the nearest player, fires point blank, never takes cover |
| **Sniper** | Needle Bolt-Action | 80 | holds the furthest cover with a view, slow accurate shots; a laser glint (later) |
| **Heavy** | Anvil LMG | 220 | slow, long suppressive bursts, crouches to fire |
| **Grenadier** | Hornet SMG | 100 | throws frag grenades on a short cooldown at players who stay put |
| **Officer** | Marksman M14 | 140 | stays behind the line; while alive, enemies within 15 m shoot 20% tighter and react faster (buff), a marker over him |

Later: shield bearers, RPG troopers (the launcher defs), dogs, vehicles (a tank/APC as a special entity, a mounted gun
technical), helicopter drop-offs.

### Entity tech: why not players, why not zombies as-is

- **Not players**: a player slot is ~46 bytes of snapshot per player per client at 30 Hz, 128 history entries, lag
  compensation, bot brains with nav planning, perception and squads: costly, and capped at 32 by the id byte and
  `maxPlayers`. Missions want 40 to 80 enemies alive.
- **The Zombies entity path, generalised**: zombies are already a cheap server entity with their own replication
  (`Msg.ZEnts`, 12 bytes each, every snapshot, to everybody), shared player physics, nav A* with a per-tick search
  budget, lag-compensated hit tests, and hooks in the shot, melee and blast code. PvE enemies reuse that *pattern*
  and the same hooks (the shot, melee and blast functions get a second "entity set" next to the horde), but are their
  own class (`PveEnemy`) because they need guns, cover, grenades and interest management, and zombies need windows,
  boards and doors. Both stay in their own files; nothing in Zombies changes.

`PveEnemy` (Source/Game/Pve/PveEnemy.cs) is pooled (`Game.PveMaxAlive` = 96 preallocated, arrays inside reused), so
steady-state ticking allocates nothing.

### AI (server; Source/Game/Pve/GamePveAi.cs)

States: **Advance** (follow a nav path to the director's attack point), **Engage** (a player in sight: stop or strafe,
fire), **Cover** (move to a cover node, crouch, peek and fire, reload there), **Rush** (rushers), **Dead**.

- **Perception**: a staggered think at 10 Hz for enemies near players, 3 Hz mid range, 1 Hz far (LOD). A think picks the
  nearest visible player (one ray per candidate, chest height, at most 4 candidates), remembers where it last saw them
  and is alerted by being shot (the shooter becomes its target).
- **Movement**: near enemies (within 90 m of a player) step through the shared player physics (`World.Simulate`: stairs,
  slopes, mantles); far enemies move **kinematically** along their nav path (lerp between nodes, no collision) at their
  speed: invisible to players, a fraction of the cost.
- **Pathing**: the bots' nav graph and `NavSearch` A*, `PveSearchesPerTick` = 2 per tick for everyone (the rest keep
  their path or steer straight), paths re-planned every 1.5-3 s or when the goal moves.
- **Cover**: a cover node is a nav node within 8 m with low `NavGraph.Openness` (close to walls) that still sees the
  target from standing; crouched there the enemy is mostly hidden. Picked at most every 2 s per enemy.
- **Shooting** (lag-comp free: the server shoots at where players are now, which is what a server-side shooter sees):
  after the reaction delay, bursts at the weapon's fire rate with its pellets; each round is a ray in a cone of
  `archetype spread x difficulty x (moving target 1.35) x (first 0.6 s on a target 1.8)` round the target's chest, tested
  against the world (`QueryFilter.Shots`) and every living player's hitbox; damage is the weapon's damage with its range
  falloff x the difficulty multiplier (headshots x1.5, not the weapon's head multiplier). Enemies run their magazine
  and reload.
- **Grenades**: grenadiers (and others rarely) throw a real frag (`Projectiles`, `Throwables.AimPitch` for the arc) at a
  player who stays put 10-28 m away, with a per-enemy cooldown and a global one (one enemy grenade per 2.5 s), so they
  flush players out of cover. The blast hurts players, never other enemies.
- **Unstuck**: an enemy that barely moved for 2 s while trying to walks off sideways and re-plans; stuck for 10 s out of
  sight, it is recycled.

### Spawn director (Source/Game/Pve/SpawnDirector.cs)

The director is a small, pure class (no Game, no map), so it is easy to test:

- **Alive budget**: never more enemies alive than the budget (counted after every spawn; the self-test checks it).
- **Pacing (like Left 4 Dead's AI director)**: phases **BuildUp -> Peak -> Relax**. Build-up: the spawn rate ramps up
  and the alive target climbs from 40% to 100% of the budget. Peak: the target stays at 100% for a while. Relax: no new
  spawns until the alive count falls to 30% (or a timer), then build-up again. **Stress** (damage the players took
  recently, kills close to them) cuts a peak short when the squad is overwhelmed, so waves feel hard but survivable.
  Defend stages use an escalating wave number: each wave's peak is longer and its budget share higher.
- **Squads**: spawns come in groups of 2-6 (by intensity) at one spawn point, one group per 1.5-4 s, so enemies arrive
  as waves of squads instead of a fog of singles.
- **Where**: nav nodes on the main walkable area (not trapped, not switched off by a collapse), inside the stage's spawn
  zones (or 25-70 m round the focus), at least 22 m from every player and **out of sight** of every player (two rays
  from each player's eye, to chest and feet height, both blocked), preferring nodes ahead of the squad (closer to the
  objective than the players are). Candidates are sampled (24 per attempt), so the cost is bounded.
- **Recycling**: every second, an enemy more than 110 m from every player and unseen for 8 s, or stuck out of sight for
  10 s, is removed and returned to the pool; the director refills its slot near the action. Stage changes recycle
  enemies that are far behind the squad.
- **Totals**: eliminate stages spawn a little more than their kill count; defend stages spawn without a total until the
  timer ends; reach stages spawn light harassment.

## 4. Network and performance budget

| What | Budget | Design |
|---|---|---|
| Enemies replicated | 80 alive | `Msg.PEnts`, per client, 20 Hz |
| Bytes per enemy | 13 | id 2, position 6 (3 cm / 1.6 cm), yaw 1, pitch 1, state + archetype 1, health 1, flags + shot counter 1 |
| Interest management | nearest 56 within 150 m of that client's player (or camera) | within 45 m every packet; 45-150 m every other packet; dead ones for 1.5 s; never more than one datagram (header 8 + 56 x 13 = 736 bytes) |
| Bandwidth per client | **< 12 KB/s** worst case, ~6 KB/s typical | 80 enemies, 4 players: measured by the self-test ("Co-op") |
| Objective state | ~24 bytes at 10 Hz | the mode-state tail (`CoopModule.WriteState`) |
| Events | rare, reliable | `Msg.PEvent`: a feed line, a banner (STAGE 2: ...), a charge planted/blew, XP for a kill / an objective |
| Server CPU | **< 3 ms per tick** with 80 enemies (60 Hz), < 40 us per enemy per tick | think LOD, kinematic far movement, a shared A* budget, pooled entities, no LINQ or allocation in the tick |
| Client CPU/GPU | the enemy draw ~0.3 ms | box rig like the zombies but LOD: full rig with gun within 35 m (at most 24), 5-box figure to 150 m, frustum-culled; later instanced |

Measured by the self-test on the CI runner (phase 1): 80 alive (40 fighting near the squad, 40 far) cost 0.1-0.5 ms per
server tick (2-7 us per enemy), the nearest client gets 40 of them in a 526-byte packet, 7.4 KB/s per client at 20 Hz; the
co-op tick allocates nothing in steady state (the shared player physics allocates a `TerrainLayer` per terrain query on
cave maps such as Silo, for players as well as enemies: an engine fix for later).

Clients interpolate enemies from the last two updates (like zombies), drop one not updated for 1.2 s, and draw a tracer
and a gunshot for each new shot in the counter (direction from yaw and pitch, the end from a ray in their own world).

## 5. Data format

A mission is a JSON file in a map pack's `Missions` folder (`Maps/World/Missions/SiloBreach.json`). Missions are kept
next to the maps (copied with them, `Maps\**` in the csproj) but out of the map loader (like `Props` and `Decals`), and
reference their map by name, so a map file never changes for a mission and a map can have several. Positions are
`[x, z]` (on the ground there: the terrain, or a floor box just above it) or `[x, y, z]` (the floor at or just under
`y`: rooms in caves under the terrain). The self-test checks every point of every mission stands on the bots' nav graph.

```jsonc
{
  "format": "call-of-slop-mission/1",
  "name": "Silo Breach",
  "map": "Silo",
  "description": "...",
  "enemies": { "budget": 26, "perPlayer": 9, "max": 80, "mix": { "rifleman": 5, "rusher": 2, "heavy": 1 } },
  "start": [[-92, -6], [-92, 0], [-92, 6]],
  "respawnSeconds": 10,
  "stages": [
    { "type": "eliminate", "text": "Breach the perimeter", "count": 24, "focus": [-58, 0],
      "spawnZones": [{ "at": [-50, -20], "radius": 18 }] },
    { "type": "destroy", "text": "Plant charges on the transformers", "targets": [{ "name": "A", "at": [-44, -4, 3] }],
      "plantSeconds": 3, "fuseSeconds": 6 },
    { "type": "defend", "text": "Hold launch control", "at": [0, -4, -46], "radius": 9, "seconds": 180, "loseSeconds": 25 },
    { "type": "reach", "text": "Extract at the silo pad", "at": [-8, -24], "radius": 6 }
  ]
}
```

Unknown keys are errors (like map files), every number is range-checked, and the self-test loads every mission.

## 6. Progression

Co-op XP (account and weapon XP, `Progress.AwardCoop`): an enemy kill 20 (+10 headshot) with 40% of it as weapon XP for
the gun, an objective stage 150 for everyone alive or not, the mission 1,000 (on top of the normal match XP). Kills are
sent to the killer in `Msg.PEvent` (the killer's own client awards the XP, like player kills). Enemy kills count on the
scoreboard as kills; deaths as deaths.

## 7. Phases

1. **Foundation (this branch)**: the mode, the mission format and loader, the five objective types, checkpoints and
   wipes, the director, six archetypes with guns/cover/grenades, replication with interest management, HUD (objective,
   progress, timers, markers), co-op bots, XP, **Silo Breach**, tests.
2. **Feel**: interact-key plants and defuses, enemy voice barks (callouts), hit reactions and death animations through
   the ragdolls, a sniper glint, suppression on players, instanced enemy drawing, a mission select in the lobby
   (several missions per map), mission progress saved (unlocks, best times).
3. **More missions**: Bocage (hedgerow advance, destroy the guns, defend the crossroads), Vuorikoski (defend the dam,
   extract by the bridge), London; escort objectives; boss fights (named targets with phases).
4. **Vehicles and set pieces**: enemy APCs and helicopters dropping squads, mounted guns, destruction-driven objectives
   (bring a building down), scripted events (doors that blow, alarms that change the director).
