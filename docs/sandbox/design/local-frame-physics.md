# Physics aboard a moving ship: a local frame (analysis)

The user's request of 2026-11-19, with two clips (`physics-aboard-moving.mp4`, `physics-aboard-moving-2.mp4`): "While aboard a
vehicle moving at speed, physics are just acting strangely ... Is there any way we could try implementing a local physics
simulation? ... when a player enters a vehicle, they're technically in a stationary thing somewhere hidden in the world, while a
representation is moving around in the world with its physics." And later: "Maybe some way that all objects on a moving ship are
all local motion. So its positions are all stored locally in relation to the ship instead of having to constantly recalculate
positions in relation to the world." This note is the analysis asked for before any change. Nothing here has been built.

## 1. What the clips show

- **Where.** A ship interior: white wall panels, blue skirting, yellow trim, grate floor, a pink-lit room beyond (Zero Day style
  corridors). Clip 2's pause menu reads "Single player - paused", so it is the **host**: no network between the sim and the
  picture. Neither clip shows the ship's speed, or whether it is in the bubble or out in space.
- **The player is steady.** The view does not shake; walls, doors and the floor stay still on screen. The player's own frame
  (contraption space) is working.
- **Clip 1 (2.6 s, Hands tool, nothing held).** A loose dark plate with a yellow edge strip hangs near the ceiling and jumps
  from frame to frame by up to about a metre and some tens of degrees, back and forth, never settling. It floats: no weight
  pulls it to the deck.
- **Clip 2 (7.8 s, physics gun).** A dark box is held in the beam. The beam bends far off the crosshair (the piece lags well
  behind the aim), and the box jumps about and flips between frames. Later, let go, a plate is seen near the ceiling again,
  jittering as in clip 1. Seats, doors and crew do not appear in either clip.

So what misbehaves is **loose bodies and the held piece**: things stepped by the construct physics, not the player.

## 2. How riding works today

**Players: already ship-local.** Contraption space (`ConstructRideMove.cs`, protocol 130; one frame per construct since protocol
325, `construct-frame-notes.md`): with `MoveState.RideUp` set the mover runs in the construct's rest frame. `RideLocal` and
`RideVel` are the truth; `Position`/`Velocity` are copies for world readers. The hull's boxes come from its joints alone
(`SegPose`), never its world pose, "so 200 m/s, a banking turn and an inverted hull all walk like still, level ground". The
frame reaches 2 m round the bounds and 3 m above (`ConstructRide.InFrame`). The look is kept in the frame
(`GameSandboxRide.RideLookSync`), shots from a moving deck use the server's exact eye (`RideShot`), the host aims at the drawn
pose (`RideDrawnPose`/`RideTickPose` swap the rider's body, its carrier, its cargo, `SpaceRide` bodies and the held piece to
their drawn poses for the aim, then back). Noclip boarding sets a flyer down inside a hull (protocol 368). This is the user's
"all local motion" idea, done for players. It is why the player is steady in the clips.

**Frozen cargo and docked craft: ship-local too.** A body in a frozen cargo volume or caught by a dock is carried kinematically
(`ConstructCargo.Follow`: its pose in the carrier's frame, sent on the wire that way; no physics of its own).

**Loose bodies: world space.** Everything else aboard (a dropped part, a crate, the plate in the clips, the held piece) is an
ordinary dynamic body of `ConstructPhysics`, stepped in world coordinates:

- *In the bubble* the ship is a dynamic body too. Both move at the ship's speed and meet in the world's contact solver. They are
  given the ship's motion when they spawn (`ConstructRide.SpawnIntoMotion`, `SpeedKept`), and nothing after.
- *In space* the ship is posed by its flight after the step and sleeps in the physics. A loose body inside gets `SpaceRide`
  (`GameSpaceLaunch.SpaceRideFind`/`SpaceCarry`): it is stepped in the world at the ship's speed against the hull at its
  start-of-step pose, then shifted by the ship's move less its own (`own = com - SpaceRideWorld - SpaceRideVel * dt`), turned by
  the ship's turn, and given the ship's point velocity plus its relative one. It is weightless (`ConstructPhysics` skips its
  gravity: "the ship's flight carries the pull"). `PosedBack` keeps the other contacts honest.
- *The physics gun* (`ConstructGrab.Follow`) springs the piece towards the beam point on top of the deck's velocity and spin
  (`GrabFrameVel`, `GrabFrameSpin`); a joined holder predicts it in the deck's frame (pg02, `GameSandboxPhysGunSim.cs`), but the
  server's body is still a world body. Near another construct its pull is capped to `NearStep` = 0.2 m a tick.
- *Drawing* on the host lerps each body between `PrevPos` and `Pos`; a client interpolates world snapshots per body.

**Float precision.** World positions are `float`; on a planetoid map the origin is the planet's centre, so a deck is a radius
or more from it (Glacier 8 km: about 1 mm per float step; 16 km: 2 mm; parked at `SpaceFarDistance` 200 km: 16 mm). The space
flight itself is in doubles (`Vec3d`, `SpaceBubble`). Players are immune (their truth is local and small); a world-space loose
body is not.

### Failure classes, and which fit the clips

| Class | What happens | Fits the clips? |
| --- | --- | --- |
| **World-frame damping and limits** | `Integrate` damps every loose body's world velocity (`Vel *= 0.999` a tick in air, `AngVel *= 0.985`) and caps its speed at `MaxSpeed` 60 m/s unless `SpeedKept`. At 100 m/s in air that is 6 m/s2 of false drag towards the stern, a hard speed ceiling once the ship speeds up past what the body was given, and in a turning ship a false spin relative to the deck. The walls then shove the body every tick. | **Likely, in the bubble.** It explains the held piece lagging and the beam bending (the spring fights the drag) and bodies grinding on a wall. |
| **Sleep measured in the world** | `Sleep` asks for a world speed under 0.14 m/s. A body resting on a moving deck never sleeps: it is re-solved every tick, jittering in its contacts, and costs a full body's step. | **Likely.** Nothing in the clips ever settles. |
| **Contacts against a fast body** | The resting contact is solved at world speeds; any mismatch between the ship's step and the body's (the ship's own contacts, thrust, a turn) shows as a kick. In space the body integrates a full tick's travel (3.5 m at 208 m/s) past a hull that does not move in the step before `SpaceCarry` takes it back. | **Likely in space**, where the clips' weightless floating points. |
| **Two-pose blending** | Host draws a body by its own lerp, the ship by its own; a frame-carried body whose `PrevPos` and `Pos` were not set by the same carry (spawned, released, carried for one tick, jumped by `SpaceCarry`) is drawn a tick's travel off for a frame. At 200 m/s a tick's travel is 3.3 m. | **Possible**: matches metre jumps that come and go between frames. |
| **Precision** | Differences of large coordinates (`SpaceCarry`'s `own`) lose 1-2 mm a tick: up to 0.1 m/s of false relative velocity, a creep and fizz, not metre jumps. | Contributes; not the main cause. |
| **Prediction mismatch** | A client's world snapshots of loose bodies against its own deck frame. | Not in these clips (host). It will matter in multiplayer. |
| **Weightless in space** | `SpaceRide` bodies get no gravity while players get deck gravity (`RideUp`). | **Yes**: the plate floats at the ceiling. This is a rule, not a bug, but probably not what the user expects. |

So the user's instinct is right: the strangeness comes from the loose bodies being stepped in the world at the ship's speed, so
every small rule that reads a world velocity (drag, limits, sleep, contacts, blending) acts on the ship's motion as if it were
the body's own. It is not "too much maths at once"; it is maths in the wrong frame.

## 3. The two proposals

### A. A hidden stationary copy ("interior instance")

How others do it:
- **Star Citizen** (physics grids / local zones): each ship is its own physics grid; things inside are parented to it and
  simulated in its coordinates; crossing the grid's boundary reparents them. No hidden copy: the grid *is* the ship's frame.
- **Space Engineers**: grids are rigid bodies in a Havok world with double-precision clusters; loose items and players aboard a
  moving grid are world bodies, and it has the same class of bugs (items sliding, "Clang") at speed.
- **Stormworks**: physics in world space with limited speeds; items aboard fast vehicles are known to drift and fall through.
- **KSP**: floating origin (the world re-based on the active vessel) and "Krakensbane" (the velocity frame re-based so the active
  vessel is near rest); the fix for precision and big velocities, at the scale of one vessel.
- **Sea of Thieves**: the player and loose objects aboard are parented to the ship and simulated in its frame; the ship's
  motion is applied as a transform.

None of them keeps a hidden stationary duplicate; the ones that work keep the inside **in the ship's coordinates** and transform
at the boundary. A hidden copy is the same maths with extra cost here: a second copy of every hull collider, its edits, damage,
doors and joints mirrored each tick; two places for every body to be (teleports on entering and leaving, things half in and half
out of an open ramp); rays and weapons crossing the hull traced in two worlds; the space step's "nothing posed meets anything"
rules twice over; the far layer and saves holding two poses. Nothing it buys is not bought by B.

### B. Everything aboard stores its motion in the ship's frame (the user's second idea)

Every body aboard (loose items, the held piece; players already; crew bots next) keeps its pose and velocity in the ship's rest
frame as its truth, as contraption space does for players. Its world pose is derived only to draw it, to trace rays and shots
that cross the hull, to meet things outside the ship, and when it leaves.

Mapped onto this code:
- **Frame membership.** A body whose centre is inside a moving construct's frame (`ConstructRide.InFrame`, the same pads the
  players use; the smallest when frames nest, as `SpaceShipAround` does) gets `FrameBody`, `FrameLocal`, `FrameRot`,
  `FrameVel`, `FrameSpin`. It replaces `SpaceRide`, and generalises it to the bubble.
- **The step.** A frame body is integrated with its relative velocity; damping, speed limits and sleep read the relative
  velocity; gravity is the deck's (`RideUp`, like players) unless decided otherwise; its contacts with its own ship are solved in
  the ship's rest frame against `SegPose` boxes, where the hull is still (the player mover already gathers these). Its
  contacts with other bodies aboard are solved there too. After the ship's step (or the flight's pose in space) its world pose is
  set from the ship's, as `ConstructCargo.Follow` does. A body asleep in a frame costs nothing while the ship flies.
- **Boundary.** Leaving the frame (a hatch, an open deck, thrown out) gives it the world velocity at that point plus its own
  (`RiderSurfaceVel`, `FlingSpeed` already exist); entering takes the reverse. Contacts with things outside the ship (the
  ground through an open bay, another ship) are solved in the world as now, with both bodies' world poses: rare, and at their
  true relative speed.
- **Physics gun.** The server's spring runs in the frame (the client's pg02 prediction already does): `GrabFrameVel` goes away.
- **Net.** Loose bodies in a frame are sent as carrier id plus local pose, as frozen cargo already is; clients draw them from the
  ship's drawn pose, so they cannot drift from the deck on screen. A protocol bump.
- **Saves.** The local pose and frame id are saved with the body (carried cargo already does this); old saves load as world
  poses and join a frame on the first tick.
- **Space.** `SpaceCarry` and the `PosedBack` exemption go: a frame body never meets its posed ship in world terms.
- **Far layer.** Far ships carry frame bodies with no step at all (asleep in the frame); a woken body steps at the near rate.
- **Crew.** Crew bots use the plain ride (`ConstructRide.cs`), not contraption space. Moving them onto contraption space is the
  same change for people as already made for players; it can wait until crews show the same faults.

What it fixes: every class in the table but prediction mismatch, which it makes far smaller (local snapshots). Players already
show it works here.

What it costs and risks:
- **Medium-large:** a frame branch in `ConstructPhysics` (integration, contacts, sleep), the gun's spring, the net and save
  formats, the spawn and leave paths, and the space step's carry. Every reader of a loose body's `Pos`/`Vel` (rays, explosions,
  the gatherer, chutes, magnets, recycling, the nudge tool) must still see the world pose: it is kept current as a copy, as
  players' `Position` is.
- **Felt motion.** In a frame nothing feels the ship's acceleration unless it is added on purpose. That is how players move today
  (the jolt limit aside) and is a design decision (below).
- **Nested frames** (a car in a hangar of a carrier): the smallest frame wins; frozen cargo already rides its carrier. A body in
  a car that is itself loose in a ship is two levels: the car's frame, the car in the ship's.
- **Boundary jitter** at a frame's edge (a crate on a ramp lip): hysteresis, as the far layer and the bubble edge use.
- **Things between a ship and the world** (a crate pinned between the hull and a rock): solved in the world as now; acceptable.

## 4. Smaller fixes

1. **Relative damping, limits and sleep** (small): in `Integrate` and `Sleep`, measure a loose body's speed against the moving
   construct whose frame holds it (the frame lookup `SpawnIntoMotion` already does), not the world. Removes the false drag, the
   speed ceiling, the false spin and the never-sleeping; a sleeping body would then be carried by the ship like cargo until
   something wakes it. Most of the bubble symptoms, at little risk.
2. **Carried sleep** (small, rides on 1): a body asleep on a moving deck is moved with the deck each tick (as `Follow` does)
   instead of stepped.
3. **Fix blending at carry jumps** (small): set `PrevPos`/`PrevRot` from the ship's previous pose whenever a carry places a body,
   so host drawing cannot pop a tick's travel.
4. **Per-ship origin or doubles** (medium): unnecessary for players and for B; worth it only if measurements show creep after B.
5. **Contacts in the moving body's frame** (medium): solving the pair's contacts in the ship's frame while keeping world truth.
   It is most of B's work without its gains in sleep, net and drawing.

## 5. Recommendation

Take **B, ship-local truth**, in phases, with fix 1 first because it is cheap, may fix most of what the clips show, and is kept
by B anyway (B's frame step reads relative velocity by construction). Do not build the hidden copy.

| Phase | Work | Size |
| --- | --- | --- |
| 0. Measure | Extend `--ride-check` with a loose-body mode (a dev check, not a self-test section): a crate resting on a deck, one held, one thrown, at 0, 60, 120 and 200 m/s with a 10 deg/s turn, in the bubble and posed out of it. Report per second: drift relative to the deck, the largest drawn jump in the deck's frame, ticks asleep, and the step's CPU. Run `--ride-check` and `--ride-check --held` as they stand for the baseline. | half a session |
| 1. Relative damping, limits, sleep; blending at carries (fixes 1-3) | `ConstructPhysics.Integrate`/`Sleep`, `SpaceCarry`, the carry paths. No wire change. | one session |
| 2. Frame bodies (B) | `FrameBody` and local truth for loose bodies, the frame step and contacts, the gun's spring in the frame, net (protocol bump) and saves, `SpaceRide` folded in. | one builder, a whole feature (several sessions) |
| 3. Crew in contraption space | Only if crews show the faults. | one session |

Measure after each phase with the same loose-body mode, `--ride-check`, `--ride-check --held`, a joined client under xvfb
(the window tool's `bringon`, `dropmotion`, `me`, `loose`, `aimat`), and the 8 ms tick budget with a ship carrying many loose
bodies (the F9 profiler on the host; `--bench` on the PC if drawing changes). Phase 2 should show zero drift and no drawn jump in
the deck's frame at any speed, and loose bodies asleep aboard a flying ship.

## Questions for the user

1. **Felt motion.** Should loose items slide when the ship speeds up, brakes or turns (real inertia), feel none (the inside is a
   still room, as players are today), or a share of it (like the MMU's inertia dampeners)? A crash should still throw things.
2. **Gravity in space.** Loose items aboard a ship in space float today while players stand. Should they fall to the deck (the
   ship's up, like players), or float unless a gravity generator holds them?
3. **May phase 0 add a loose-body mode to `--ride-check`?** It is a dev command, not a self-test section.
4. **Order.** Phase 1 first and judge the result, or go straight on to phase 2?

## The user's answers (2026-11-19)

These override the text above where they differ.

1. **Acceleration felt by loose items: fully.** Items aboard slide and tumble when the ship brakes, turns or is hit: in the ship's frame they feel the frame's acceleration (and its turning: the ship's angular acceleration, centrifugal and Coriolis as the maths gives) as a real force, so a steady cruise feels like rest and a hard manoeuvre throws things. Players keep today's feel unless the user says otherwise.
2. **Gravity aboard in space: follow the player.** Loose items get gravity aboard exactly when and where players do (deck gravity, gravity generators); weightless where a player would float.
3. **Plan: straight to the full fix** (phases 0 to 2 in one builder): measure first with a loose-item mode in `--ride-check` (a dev check, not a self-test), then ship-local truth for everything aboard (option B), with the quick-fix items folded in.
