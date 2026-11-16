# One frame per construct, spawning into motion, leaving with momentum (notes)

The user's request of 2026-11-16, in three parts. Branch `claude/construct-frame`. Code: `Source/Constructs/ConstructRide.cs`,
`ConstructRideMove.cs` (contraption space), `ConstructRideSpawn.cs` (new), the check `--ride-check` (`Source/DevTools/RideCheck.cs`).

## What was found

`--ride-check [BLUEPRINT,...] [--speed V] [--turn DEG/S] [--map NAME]` builds each ship on the Proving Ground, lifts it 30 m
and drives it round a circle (10 m/s, 10 deg/s by default), sets a player down on standing spots found on its parts, walks
them four ways and jumps, through the real step (`World.Simulate` + `ConstructWorld.BlockPlayer`), and prints every tick
their frame changed or lapsed; then a lift on a moving deck, and a plate made inside each ship.

- **The welded-piece idea did not hold.** Karl's ships (Rust Bucket, Zero Day, Halberd, Magpie) are socket-built: 0 freeform
  welds; their joints (8 on the Zero Day, 14 on the Halberd) are doors, ramps and the like. In contraption space a freeform
  weld was already one piece with its parent (`MoveSeg`). Walking their interiors at 10 m/s on a turn gave no frame-less tick
  and no segment switch before the change either.
- **What did lapse:** the frame reached only 0.5 m round the bounds (2.5 m above). Riders on an outer deck, a hull top or
  near the edge of an open bay left it on a step or a short hop, and the hull, moving on, then met them in the world's
  collision (the plain push-out of `BlockPlayer`), which shoves a player sideways out of a moving box. Over a turn that was
  the jump "outside any segment".
- **Segments switched frames.** Standing on a joint's leaf (a lift deck, a lowered ramp, a turntable) made it the rider's
  frame (`RideSeg`); stepping between it and the body re-joined. The watcher's copy of a rider (`Game.SampleSnapshots`) and
  the client's own drawing only interpolate between samples in the same `RideBody`/`RideSeg`, so each switch was a drawn
  pop. Bots' plain ride also took every freeform piece as a frame of its own.
- **Leaving lost momentum.** Off a moving frame a player had 45 ticks of weak air steering, then the ground's air control
  (3/s) braked them to a stop in about a second: out of the back of a cargo plane they fell and stopped.
- **Spawns:** f4c955791 covered the menu's spawns, drops, `Give` and held ghosts, only when the player was riding. Every
  other path (machines' outputs, paste, placing a loose part, wrecks without a parent velocity, anything made where nobody
  stands) made bodies at rest in the world.

## What changed

1. **One frame per construct.** A player in contraption space always rides the body's frame (`RideSeg` 0). A moving segment
   under the feet only carries them in it: `MoveState.RideOn` (sent in the own-player snapshot, protocol 325), moved each
   tick by that segment's motion at the feet (`ConstructRide.CarrierVel`, the velocity half a tick on, so a turntable carries
   them round, not out), and `RideVel` is relative to it while they stand on it. Stepping on or off it changes `RideOn`
   and their relative velocity in the same tick (no frame switch, no re-join). The frame reaches 2 m round the bounds and
   3 m above (`BoundsPad`, `BoundsAbove`, `ConstructRide.InFrame`), with no tick cap inside (the bots' 180-tick cap went too).
   Saves from before (`Rejoin` with a segment) are carried into the body's frame. Bots' plain ride rides the `MoveSeg`.
2. **Spawn into motion.** `ConstructWorld.Create` marks each new body `Fresh`; before the physics steps
   (`ConstructRide.SpawnIntoMotion`, server) a fresh body that its path gave no motion takes the motion of the moving
   construct whose bounds (0.3 m round, 1 m above) hold its centre of mass: the velocity there and the turn
   (`ConstructRide.IntoMotion`, `FrameAt`). `ConstructEdits.InheritRide` goes through the same helper. Paths that set a
   motion themselves keep it (wrecks and machine outputs take the parent's point velocity; a part cut free now takes the
   turn too). A player set down in a bunk takes the bunk's surface velocity (`SpawnVel`).
3. **Leaving keeps momentum.** Off a frame moving faster than 6 m/s (`FlingSpeed`) a player keeps its velocity until they
   land (`NoclipMove.Flung`: air steering 0.1/s); slower, the weak steering for 45 ticks as before (`ConstructRide.FreeFor`,
   also for crash throws and seat exits). The momentum is the surface's at the feet, a lift's included
   (`RiderSurfaceVel`). Grenades thrown aboard take the frame's motion (`SandboxRideCarry`); a seated player's velocity is
   the seat's surface velocity.

## Status

- [x] part 1, part 2, part 3 as above; `--ride-check` passes (no frame-less tick inside a frame; the lift's rider within
  0.5 cm of its top at 0.6 m/s on a ship at 10 m/s; plates made inside each ship match its velocity and turn).
- [ ] not tried in the real window or over the network (see the branch's report).
