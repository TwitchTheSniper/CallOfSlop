# Stand-in craft (placeholder)

Placeholder ships and vehicles so the story can be watched while the real vehicle and ship system is being redone
(Karl, 2026-11-10). They are **pictures only**: a dozen coloured boxes on a scripted path, hazard yellow and black so
nobody takes them for the real thing. No collision, no physics, no construct, nothing saved, and nothing in the vehicle
kit or the construct code is touched.

## What they do

- **A person arriving by road** (Story bodies, docs/story/STORY.md "Bodies"): a craft sets down 7 m outside the road's
  end just as the person's body appears there, stands 8 s while they step out, and goes. A road of 5 story minutes or
  more is a **shuttle** that comes down out of the sky and leaves with a jump flash; a shorter one a **hover hauler**
  that runs along the ground (planetoids too: it follows the ground under it).
- **A person setting off**: a craft comes for them when they start to walk out. Their body waits at the road's end until
  the craft has landed (at most 30 s), then boards (the body goes) and the craft leaves.
- **A faction courier** (docs/story/STORY.md "Jobs"): when a Requisition Crate is sealed, a small **courier flyer** sets
  down 6 m beside it just as the courier comes, and leaves once the crate is collected, sent back over the paperwork,
  short again or the job dropped.
- Two craft due at one spot stand side by side (14 m apart).
- **A craft from another world** (the shuttle, the traffic's visitors and flyovers) warps in: a white flash where it
  breaks into the air, a streak that snaps back into a ship, two shock rings spreading across its path, and a boom
  heard as late as it is far (343 m/s, like thunder; nothing past 2.5 km). The jump away booms too, softer.

## Traffic (the world's ambient life)

`GameTraffic.cs`, data in `Story/Traffic/Traffic.json` (a bad file is refused with a reason and there is no traffic). It
runs in the story's survival world, round the penal colony, whenever "Craft" and "Traffic" are on (F4, People tab; the
"Traffic" tooltip lists every run, where it is and why it travels).

- **Stations**: the colony's gate and the quarry are real. The rest are placeholders, set out by bearing and distance
  from the gate (turned off anything built there): a plinth with hazard corners, a mast, a hut and crates, in their
  owner's colour. Each stands for a place that isn't built yet (the crusher, the stores, the pump house, customs, the
  port, the relay). A pad is a craft of kind `Pad` whose script the host renews, so it needs no message of its own.
- **Rounds**: a craft that goes round its stops for ever, standing a while at each: the ore run (quarry to crusher), the
  ration round, the water bowser, the fence patrol (haulers, along the ground) and the paperwork flight (a courier, in
  hops). A leg is a script with `FlagRoute` (`From` to `Land`); the next leg reuses the craft's id, so every PC sees
  one craft going on. A leg's ground is kept (`StandInPath.RouteTrack`), so a lap costs its ground once.
- **Visits**: a faction's ship from its home lands at a station, stands while it trades and jumps away: the clan's
  trader (girders and whisky for ore), an IT service call, a Call Centre warranty rep, the Ministry's prison transport.
- **Flyovers**: a freighter or shuttle crosses the sky high over a player, bound somewhere else.
- **Why**: every run has a "why" (the window) and radio lines; the traffic channel says one about every seven minutes
  in all (`radioEvery`), as a [RADIO] chat line.
- **Liveries**: `"liveries"` gives each faction a colour, worn on the craft's stripe boxes and the pads' flags.
- Nothing is traded yet: no money, no stock, no story events. When places and vehicles are real, a run's stops and
  reason are what a real driver and a real cargo would take over.
- `--traffic-check [seconds]` (headless, about 100 s): a host and a client over the real transport; prints the
  stations, every leg the client heard, how far haulers ran from the ground and what the host's look costs
  (0.1 us a call, four calls a second, no allocation). At most 48 craft and pads at once (`StandInMax`).
- **Story window (F4), People tab**: the "Craft" switch turns them off; "Call craft" brings one of each kind in near you
  for ten seconds, to see them without waiting for a trip. Log lines start with `[standin]`.

## How it works

The host decides each craft's script (when it starts, how long it takes to come in, stand and go, where it lands, which
way it travels) and sends it once in `Msg.StandIn` (protocol 233; a leg's start and the owner's colour since 239), again every 3 s for anyone who joined; every PC poses
it from the server tick (`Game.MoverRenderTick`), so nothing streams while it flies.

| File | What |
| --- | --- |
| `StandInCraft.cs` | the kinds, the box shapes, the paths (`StandInPath.Pose`) |
| `GameStandIns.cs` | the host's side (arrivals, pickups, couriers, the test), the wire |
| `GameTraffic.cs` | the ambient traffic (stations, rounds, visits, flyovers, the radio) and its data's loader |
| `TrafficCheck.cs` | `--traffic-check` |
| `StandInRender.cs` | drawing, in the opaque pass beside the map's movers |

## Taking them out

Delete this folder, then every line marked `STAND-IN` (`git grep -n "STAND-IN"`):

- `Source/Net/Net.cs`: `Msg.StandIn` (leave the slot if newer messages come after it, or bump the protocol)
- `Source/Net/ServerBrowser.cs`: the protocol note
- `Source/Game/Game.cs`: the `Msg.StandIn` case
- `Source/Game/GameStory.cs`: `StandInsForget()` and `StandInsTick(s, now)`
- `Source/Game/GameStoryBodies.cs`: `gone = StandInMayBoard(a.Entity)` back to `gone = true`
- `Source/Game/GameStoryJobs.cs`: `StandInCourierCalled` and the two `StandInCourierDone`
- `Source/App/Program.cs`: `StandInRender.Draw` and `--traffic-check`
- `Story/Traffic/`: the traffic's data
- `Source/UI/UiStoryPeople.cs`: the "Craft" and "Traffic" switches and the "Call craft" button
- `docs/story/STORY.md`: the "Stand-in craft" paragraph
- `Source/Game/GameQuarry.cs`: the quarry duty's crew bus (`StandInFor.Duty`, the ride steps) and `Source/App/Program.Quarry.cs`
  (the ride's camera): a real crew bus that carries players replaces both (docs/story/DUTIES.md "The crew bus")

When the real ships and vehicles can carry people, the hooks above are where they plug in: the same moments (a body
about to appear at a road's end, a body walking out, a crate sealed) want a real craft instead.
