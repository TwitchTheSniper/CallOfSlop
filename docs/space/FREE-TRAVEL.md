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
- Game methods that change the map, the mode or the gravity setting, or start, stop or respawn (`SetMapHere`, `Mode`,
  `LobbySetMap`, `LobbySetMode`, `LobbyStart`, `ReturnToLobbyNow`, `GravityPercent`, `RequestRespawn`) enter their
  own world themselves, since the lobby room and the self-tests call them between frames.
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

## Step 2: a planetoid on the traveller's game (`Source/Game/GameTravel.cs`, `Source/App/Program.Travel.cs`)

Off unless the host starts with `--free-travel` (`Game.FreeTravel`); step 5 makes it the default.

When a ship in space comes within the sky of another planetoid and the only person aboard is a client, the host
doesn't move the whole game (`SpaceLandingStep` calls `TravelAlone` rather than the group landing):

1. The host packs the ship and what it carries as a world file (`WorldCopy`, as saved ships are), with the traveller's
   record and the space clock, plus the planetoid's kept save from `Worlds/<world>/planets/<map>` if there is one. It
   sends both to that client (`Msg.Travel`: a begin and 48 KB chunks, reliable and ordered), takes the ship out of its
   own constructs and space, and marks the traveller `Player.AwayOn` (not alive, never respawned, ignored by the
   "everyone aboard" landing rule).
2. The client opens the planetoid as an offline server Game of its own, the keeper (`Game.OpenKeeper`, `Keep`), with
   the game connected to the host kept open behind it (`Program._link`, still updated every frame). The keeper lands
   the ship as the group landing would (`WorldLandApply` with `LandingPlan.All` and `.Travel`: every body of the file,
   the traveller's seat, no ships left in space, no save written at the end), and the traveller spawns in their seat.
3. Once the ship has been a body in that sky (`TravelKeeping.Down`) and flies out of it again, the keeper packs the
   ship and the whole planetoid (`PendingReturn`) and the client sends them through the link. The host keeps the
   planetoid in the world's `planets/` folder, reads the ship back into its space where it left the sky
   (`WorldReadShip`, shared with the landing), and the traveller sits in it again.

Known limits of step 2: the link's own sounds still play while travelling; the host itself can't travel alone (its game
is the space); other ships in space are copies on the keeper, moved on their orbits, not live.

## Step 3: meeting on a kept planetoid (`Source/Game/GameTravelMeet.cs`, `Source/Net/RelayTransport.cs`)

The host knows who keeps which planetoid (`KeeperOf`). A ship with one client aboard coming into a kept planetoid's
sky visits the keeper rather than waiting at the edge:

1. The host packs the ship as for step 2 (`TravelPackShip`) and sends it to the keeper's link (`Msg.Travel` purpose
   Arrive) with where it is relative to the planetoid, takes it out of its own world (`TravelAway`), and pairs the
   visitor with the keeper.
2. The keeper reads the ship into its world, puts its ship in its own space a little outside the sky, holds it there,
   and adds the visitor's record with their seat in it. Then it tells the host (Arrived), and the host tells the
   visitor's link (Visit).
3. The visitor's PC opens a client Game that joins the keeper's game over a `RelayTransport`. Every packet goes up
   the visitor's own link to the host as `Msg.Relay`, and the host passes it down the keeper's link, and back the same
   way. The host passes packets only between paired players. Nobody needs a port open or a hole punched, because each
   PC already reaches the host. The cost is the trip through the host, and the host carries that planetoid's traffic.
   The keeper's game hosts over a `RelayTransport` of its own from the start (the step 2 keeper too).
4. The visitor's record on the keeper puts them back in their seat as they spawn (`WorldReseat`). The keeper then lets
   the ship go with the speed it came in at, and it flies into the sky.
5. When that ship has come down and flies out of the sky again, the keeper packs it with its riders' records, sends it
   to the host (Leave), removes it, and closes the riders' connections. The host puts it in its own space and reseats
   the riders (`TravelShipHome`, shared with step 2's return), then tells their links (Home). Their visit games close.

Ships read in from another game are posed where their space ship is at once (`TravelPoseNow`). Without that, the next
physics step sees them where they were on the other map and they fall out of the world.

While visitors are there, the keeper's own ship can't take the planetoid home. It flies on in the keeper's space until
they have gone; handing over is step 4. If the keeper's PC leaves the host, its visitors come back to the host at a
spawn (`MeetGone`), and the planetoid is lost back to the last copy the host kept.

`--travel-check [LAG_MS]` (default 60, with 2% loss) checks steps 2 and 3 together. A host and two clients run in one
process over the loopback transport, in a throwaway save folder, each client running `Game.TravelStep` as
`Program.Travel.cs` does:
- The keeper's Orbiter goes into Bluestone's sky, Bluestone opens on the keeper's game, and the ship lands.
- The visitor's Orbiter goes into the same sky. The visitor joins the keeper's game through the host, sits in their
  ship, and lands.
- The visitor flies out and must be back in their ship in the host's space.
- The keeper flies out and must be back too, with Bluestone kept.

`[travel-check]` lines go to `callofslop.log`; the exit code is the number of problems.

## Next steps

4. Keeper hand-overs: a keeper leaving or dropping hands the planetoid to someone still on it, or saves it to the host
   and closes it.
5. Switch on free travel: drop the "everyone aboard" rule; keep the group rule as a setting.
6. Polish: who is where in the Tab list, a radio line when someone lands elsewhere, a load screen.
