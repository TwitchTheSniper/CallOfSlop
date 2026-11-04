# Free travel between planetoids

Goal: anyone gets in their ship and flies wherever they like, landing on any planetoid with a map, while their friends
stay where they are. Today's landing (docs/space/SPACE.md "Landing on another planetoid") moves the whole group at
once, because a game process holds one map.

Plan of record: https://claude.ai/artifact/VXLFgEeTi5bWvz4jZVFaXX. Route 2 (Karl, 2026-11-04): each planetoid is run by
the game of someone standing on it (its keeper); the host keeps space and the saves. If route 2 causes problems, the
fallback is route 1: the host runs every occupied planetoid. No time warp, so every world shares one clock.

## The shape: one Game per planetoid

A `Game` already owns its constructs, players, Sandbox state, space bubble and net peer. So a planetoid is a Game:

- Route 2: the keeper's process runs the planetoid's server Game; others there join it; the main host keeps space ships
  and the saves.
- Route 1: the host's process runs one server Game per occupied planetoid.

Either way a process must be able to run several Games on different maps. That was blocked by process-wide statics
(`World.Map`, `World.Physics`, gravity), which is what step 1 untangles.

## Step 1: a world per Game (`Source/Game/WorldContext.cs`)

`WorldContext` is one world's share of the process-wide simulation state. `Game.Place` is its own, made in the
constructor (`WorldContext.Fork`: a copy of the entered world, with its own gravity hooks and gravity parts) and
entered (`World.Enter`) before every tick, every packet (`Game.Update`) and its `Dispose`.

The roughly 500 call sites that read `World.Map`, `World.Physics`, `GravityFrames` and the rest are unchanged: the
statics always hold the entered world, and `World.Enter` swaps them. With one Game in a process (every game so far) it
never swaps, so nothing changes; `WorldContext.Swaps` counts the swaps.

Swapped per world:

| State | Where |
| --- | --- |
| the map, its collision world | `World.Map`, `World._physics` |
| movement code, gravity setting and scale | `World.Mover`, `GravitySetting`, `GravityScale` |
| the map's gravity field, generator volumes, force hooks | `GravityFrames` (`Saved`, `Save`, `Load`, `Copy`) |
| gathered gravity wells and generators | `ConstructGravity` (`State`, `Swap`: the arrays trade places, nothing is copied) |
| mover clock | `MoverPhysics.Clock` |
| the riders' and the last physics step's bodies | `ConstructRide.Bodies`, `ConstructPhysics.LastWorld` |

Not swapped, because they are keyed by map or are per-call scratch: posed mover slots and sweep pillars, the legacy
grid and penetration caches (rebuilt if a map changes under them), terrain layer rings, the construct physics' scratch
arrays (Games tick one after another, never at once). Contact warm starts already live in each `ConstructWorld`.

Two Games on the same map (a host and its own client, as the self-tests run them) each have a collision world; a broken
or restored prop reaches all of them (`WorldContext.SolidOnOthers`).

### Rules for new code

- State that belongs to one world (a map, its bodies, its gravity) and lives in a static must be swapped: add it to
  `WorldContext` (`World.SaveTo` and `LoadFrom`), or keep it in the Game or the map instead.
- Code that runs outside `Game.Update` for a particular Game (a tool, a test, a dev command) calls
  `World.Enter(game.Place)` first.
- `World.Enter` is main-thread only. A worker thread that reads `World.Map` (map destruction's support graph, nav
  builds) should take the map it was started with, not read the static, once two Games share a process.
- Client visuals are shared: `World.SetMap` clears ragdolls and gibs for the whole process, so only the Game being
  drawn should change map.

### Checking it

`--two-planetoids [SECONDS]` (default 8): a Buggy is thrown at the pole of the Planetoid and of Bluestone while the
keeper sprints, first each world alone, then both ticking turn about in one process. Every tick of the turn-about run
must match the lone run exactly (buggy and player), and each world must see its own map, gravity and collision world
while entered. Output in `callofslop.log` (`[two-planetoids]` lines); the exit code is the number of problems. With the
swap switched off the check fails (World.Map is the other world's; the buggy ends 66 m off).

## Next steps

2. Players on different maps: each player record carries the planetoid it is on; snapshots send each client only its
   own planetoid's bodies and players, plus ships in space for everyone.
3. A planetoid keeper: a client runs a planetoid's server Game on its own machine.
4. Hand-overs: a ship landing opens its planetoid on the lander's game (from the kept save, as the group landing does);
   a keeper leaving hands the planetoid to someone still on it, or saves it to the host and closes it.
5. Switch on free travel: drop the "everyone aboard" rule; keep the group rule as a setting.
6. Polish: who is where in the Tab list, a radio line when someone lands elsewhere, a load screen.
