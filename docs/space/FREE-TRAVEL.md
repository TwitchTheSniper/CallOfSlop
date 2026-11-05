# Free travel between planetoids

Goal: anyone gets in their ship and flies wherever they like, landing on any planetoid with a map, while their friends
stay where they are. The group landing (docs/space/SPACE.md "Landing on another planetoid") moves the whole game at
once, because a Game holds one map.

Route 1 (Karl, 2026-11-05): the host's process runs every planetoid someone is on, one server Game each. This replaced
route 2 (2026-11-04: the planetoid ran on the game of a player standing on it, with hand-overs and copies), to keep
clear of desyncs and hand-overs. No time warp, so every world shares one clock.

## The shape: one Game per planetoid

A `Game` already owns its constructs, players, Sandbox state, space bubble and net peer, so a planetoid is a Game. The
host's main game holds space, its own planetoid, the saves, bots and the story's people; every other planetoid with
someone on it is a Game beside it in the same process (`Game.Places`). Players only ever connect to the host, on its
one port: a planetoid's traffic rides each traveller's own connection, tagged with the planetoid's number.

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

## Route 1: the host runs every planetoid

Code: `Source/Game/GameTravel.cs` (the host's main game), `GameTravelPlace.cs` (a planetoid's game on the host),
`GameTravelVisit.cs` (a traveller's PC), `Source/Net/RelayTransport.cs`, `Source/App/Program.Travel.cs`.

### Landing

When a ship in space comes within the sky of another planetoid and everyone aboard is a client, the host's landing
step (`SpaceLandingStep`) calls `TravelToPlace` rather than the group landing. After its own ticks (`UpdatePlaces`, at
the end of `Game.Update`):

1. The host packs the ship and what it carries as a world file (`TravelPackShip`, as saved ships are), with everyone's
   records and the space clock, takes it out of its own constructs and space, and marks the riders `Player.AwayOn` (not
   alive, never respawned, ignored by the "everyone aboard" landing rule).
2. If the planetoid isn't running yet, the host opens it (`OpenPlace`): a server Game on that map, with a silent
   `Audio`, its own `WorldContext`, and a `RelayTransport.OnHost` as its net. It loads as a landing does
   (`KeepOpen`, `WorldLandApply` with `LandingPlan.All`, `.Travel`, `.KeepPlaces`): the planetoid from the world's
   kept copy in `Worlds/<world>/planets/<map>` if there is one, then the ship and its riders' records. If it is already
   running, its game takes the ship on its next tick (`MeetArriveApply`), a little outside its sky.
3. The ship waits in that game's space at the edge of the sky until its riders sit in it (`KeepHoldOthers`,
   `VisitorsStep`). The planetoid's game tells the host as soon as it is ready (`PlaceVisit`), and the host sends each
   rider `Msg.Travel` purpose Visit (world, map, the planetoid's number).
4. The rider's PC opens a client game for it (`Game.OpenVisitor`) that joins over `RelayTransport.Joining`. Its
   packets go up the rider's connection as `Msg.Relay` with the planetoid's number, and the host's main game hands them
   straight to that planetoid's transport (`OnRelayHost`); its answers come down the same way (`PlaceRelaySend`). The
   host passes packets only between a traveller and the planetoid it sent them to (`_travelOn`).
5. The rider's record puts them back in their seat as they spawn (`WorldReseat`). Once everyone aboard sits in it, the
   ship flies on into the sky with the speed it came in at.

The planetoid's game ticks after the host's own, in its own world (`TickPlaces`; each Game enters its `Place` in
`Update`), on the same clock. Its host-side player (id 0) is never in play (`AwayOn = "space"`, no record), and it
doesn't advertise itself or land ships elsewhere.

### Leaving

When a ship that has come down flies out of the sky again, the planetoid's game packs it with its riders' records,
hands it to the host's main game (`MeetLeaveSend`, in memory), removes it and closes the riders' connections. The host
reads it back into its own space where it left the sky (`PlaceLeaveApply`, `TravelShipHome`), reseats the riders, and
sends them `Msg.Travel` purpose Home; their visit games close and the link plays again.

Ships read in from another game are posed where their space ship is at once (`TravelPoseNow`). Without that, the next
physics step sees them where they were on the other map and they fall out of the world.

### Saving and closing

A planetoid's game writes a copy of itself into the world's `planets/<map>` folder every `KeepSaveEvery` seconds (120)
and as it closes (`PlaceSave`). With nobody on it (no connections, no ship waiting for its riders) for
`PlaceEmptyClose` seconds (30; Karl, 2026-11-05), it is saved and closed, so machines there stop until someone comes
back. When the host closes, every planetoid is saved and closed first (`ClosePlaces`, in `Game.Dispose`).

### Who travels this way

Free travel is on by default. The host's "Travel together" box (lobby room, Match tab, Sandbox) brings back the group
landing (`travel_together=1` in settings; `Game.FreeTravel` off; `--free-travel` forces it on).

A ship with the host or a bot aboard still lands the old way, with everyone aboard. While a planetoid is running beside
the host's game, such a ship waits at its edge.

### Checking it

`--travel-check [LAG_MS]` (default 60, with 2% loss): a host and three clients (Ann, Bob, Cat) in one process over the
loopback transport, in a throwaway save folder, each client running `Game.TravelStep` as `Program.Travel.cs` does:
- All three Orbiters go into Bluestone's sky. Bluestone must open once on the host, and each rider must join it, sit
  in their ship and land.
- Cat flies out and must be back in their ship in the host's space; Cat's Tab list must say Bob is on Bluestone, and
  must hear Bob's chat line.
- Ann flies out with Bob still there: Bob's visit must go on, Bob where he was.
- Bob flies out: Bluestone, empty, must be saved into the world's folder and closed.
- Bob and Cat fly in together in one ship: Bluestone opens again, both sit in the one ship, land, and come home in it.

`[travel-check]` lines go to `callofslop.log`; the exit code is the number of problems.

## Polish (kept from route 2)

- Who is where: the roster carries every player away on another planetoid (`WriteRosterAway`), and the Tab list's
  status says "on Bluestone" (`ScoreboardModel.Status`).
- A radio line: the host's chat says who landed where and who left (`TravelRadio`).
- A load screen: while a traveller's game joins a planetoid, the centre line says "Landing on Bluestone..." until they
  are in their seat (`Game.TravelBanner`).

## Chat, scoreboard and sounds while away

While travelling the chat box is the host's (`Program.Voice.cs` draws the link's chat and sends what you type through
it), so everyone in the world reads and answers you; the host doesn't flag a traveller's lines as a dead player's. The
scoreboard lists the link's players (`Ui.RosterGame`), the whole world with "on X" for those away. The link plays no
sounds while you are away: its `Audio` is swapped for one never started (`Program.TravelSides`), and given back at home.

## Still open

- The host itself travels only with everyone on its map aboard (its game is space and its own planetoid). Next step if
  wanted: the host's own PC joins its own planetoid's game the way a client does.
- Ships in space are live only on the host's main game; a planetoid's game has copies moved on their orbits (it could
  read the main game's ships directly, since they share a process).
- A traveller who drops while on a planetoid comes back to the host at a spawn when they rejoin; what they had there
  stays in their record on that planetoid. The same goes for everyone away when the host closes.
- Voice goes through the planetoid's own game, so only those on the same planetoid hear you; and lines said aloud by
  the planetoid's own people show over their heads but not in the chat box (which is the host's).
- Each running planetoid's tick counts toward the host's 8 ms budget; not yet measured with several busy planetoids.
- Opening a planetoid on the host clears the host's own ragdolls and gibs once (`World.SetMap` clears the process's
  client visuals).
