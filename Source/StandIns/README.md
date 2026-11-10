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
- **Story window (F4), People tab**: the "Craft" switch turns them off; "Call craft" brings one of each kind in near you
  for ten seconds, to see them without waiting for a trip. Log lines start with `[standin]`.

## How it works

The host decides each craft's script (when it starts, how long it takes to come in, stand and go, where it lands, which
way it travels) and sends it once in `Msg.StandIn` (protocol 233), again every 3 s for anyone who joined; every PC poses
it from the server tick (`Game.MoverRenderTick`), so nothing streams while it flies.

| File | What |
| --- | --- |
| `StandInCraft.cs` | the kinds, the box shapes, the paths (`StandInPath.Pose`) |
| `GameStandIns.cs` | the host's side (arrivals, pickups, couriers, the test), the wire |
| `StandInRender.cs` | drawing, in the opaque pass beside the map's movers |

## Taking them out

Delete this folder, then every line marked `STAND-IN` (`git grep -n "STAND-IN"`):

- `Source/Net/Net.cs`: `Msg.StandIn` (leave the slot if newer messages come after it, or bump the protocol)
- `Source/Net/ServerBrowser.cs`: the protocol note
- `Source/Game/Game.cs`: the `Msg.StandIn` case
- `Source/Game/GameStory.cs`: `StandInsForget()` and `StandInsTick(s, now)`
- `Source/Game/GameStoryBodies.cs`: `gone = StandInMayBoard(a.Entity)` back to `gone = true`
- `Source/Game/GameStoryJobs.cs`: `StandInCourierCalled` and the two `StandInCourierDone`
- `Source/App/Program.cs`: `StandInRender.Draw`
- `Source/UI/UiStoryPeople.cs`: the "Craft" switch and the "Call craft" button
- `docs/story/STORY.md`: the "Stand-in craft" paragraph

When the real ships and vehicles can carry people, the hooks above are where they plug in: the same moments (a body
about to appear at a road's end, a body walking out, a crate sealed) want a real craft instead.
