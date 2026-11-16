---
paths:
  - "Source/Physics/**"
  - "Source/Constructs/ConstructPhysics*.cs"
  - "Source/Constructs/ConstructCollider.cs"
  - "Source/Constructs/ConstructNarrow.cs"
  - "Source/Constructs/ConstructBroadphase.cs"
  - "Source/Constructs/ConvexHull.cs"
  - "Source/Constructs/ConstructGravity.cs"
  - "Source/Constructs/ConstructRide*.cs"
  - "Source/Constructs/ConstructFlight*.cs"
  - "Source/Constructs/ConstructAero.cs"
  - "Source/Constructs/ConstructThrust.cs"
  - "Source/Constructs/ConstructDeck.cs"
  - "Source/Game/GameSandboxRide.cs"
  - "Source/Game/GameSandboxPhysGun.cs"
  - "Source/App/Program.SandboxFlight.cs"
  - "Source/App/Program.PhysicsGun.cs"
  - "docs/space/GRAVITY.md"
  - "docs/sandbox/constructs/flight.md"
---

# Physics, riding and flight: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- For a solver instability, print each body's position, tilt and speed by tick before changing a number, change one thing at a time, and read anything a contact needs from the tick's velocities before the first impulse.
- When a physics change breaks a scene, first check whether the new result is what the shape should do; a scene puts a body where a real shape would move or fall.
- A visual change of a part's size also changes its collider, player blocking, ray hit, outline and ghost checks: solid shape goes through `Construct.SolidCount/SolidBox/ShapeBounds`.
- A "don't move into X" check refuses only moves that make a NEW overlap and skips what the body is joined to.
- Per-tick work against the world is bounded per body whatever its size (2026-11-10: a Titan Walker lying on Caledon made 5,300 ground queries a tick); measure with the biggest blueprint lying on a slope, not standing on the flat.
- A run-time def made per growth step sets `ColliderBoxes`, not a hull (2026-11-11: a growing wheel built a 6 ms convex hull every 5 cm).
- A position fix that undoes a move along up goes back along that same up (`GravityFrames.UpAt`), never along a world axis (2026-11-12: players standing still on a tilted planet floor drifted 1-10 cm/s); try a change to standing with a player left still for 6 s on a tilted planet floor.
- **Riding.** Every way of leaving a moving thing is a ride-frame path (`ConstructRide.End`, or off with its surface velocity); state that decides how a replay runs arrives in the same message as the move.
- A world-side check on a construct is judged as the frame's mover would (any face up, a capsule on an edge); entry tests drop onto edges, beams and turned hulls, size tests use the biggest builds (`RideTall`).
- "What is under the crosshair" on a moving thing uses the picture just drawn (`PanelRender.DrawnSpot`).
- A view that replaces another has its own remembered mode and is tried with the other modes on.
- In flight, along the ground is `Along(v, up)` and up is `BodyUp`; try flight changes away from the world's +Y side.
- 2026-11-11: after the survival physics gun went to 5,000 kg, "physics gun feel" failed: its 450 kg "heavy" piece was no longer limited by the beam. A check that reads a limit takes its amounts from the constant (`SurvivalMaxMass * 0.3f`), not a number picked against the old value.
- 2026-11-13: a watcher's copy of a rider stepped back 5.4 cm: past their newest snapshot a remote player was carried on at the snapshot's velocity, but a rider who has stepped off a moving seat slows in the air. Anything drawn ahead of its newest network sample uses the motion the samples show (`Game.SampleSnapshots`).
- 2026-11-14: no nav link on a planetoid ever climbed a stair: `World.CanWalkCurved` stepped its samples with `World.Slerp(ua, ub, f)`, and for two ups a few metres apart the dot rounds to 1 in floats, so Slerp returned the first up and every sample was taken over the link's start. Interpolate nearby directions with a normalised lerp, never an acos of their dot; `World.Slerp` still does this (GravityFrames' smoothing, ConstructThrust).
- 2026-11-14: a rider standing in a ship in orbit was drawn with the legs torn away: the leg animator kept planted feet and the ground's height in world coordinates, and the construct moved 300 m/s under them. Anything a rider's drawing keeps from frame to frame lives in the rider's frame (`Player.RenderRideFeet`), never the world's.
- 2026-11-16: an ordered ship on Ship keys sat on the ground burning fuel, and a hovering one flew about its point at full speed: the Ship pattern stopped dead whenever the autopilot was on (it never read the height or the point it held), and on a planetoid the autopilot measured the way to its point whole, height included, so a craft 1 km over its point was "1 km away". A new key pattern is tried with the autopilot on in each mode, and a distance over curved ground is taken at the craft's own height (`ConstructFlight` "The way it goes").
