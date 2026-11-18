# Spacewalks: the space suit and the Manned Manoeuvring Unit

Three parts (part kit generator `Source/PartKit/Generators/EvaParts.cs`, files in `Constructs/Parts/Eva`, inventory Flight > Pilot gear) and a breath rule. Code: `Source/Game/GameSandboxEva.cs` (rules, use, dock, HUD), `Source/Physics/PilotGear.cs` (`MmuStep`), `Source/Render/EvaRender.cs` (the pack on the back), the space suit uniform in `Cosmetics/uniforms.json` (`"space_suit"`).

## Breath in vacuum (Karl, 2026-11-17)

- Vacuum is past the space bubble's edge (`GravityFrames.Point.OrbitFrom`) or where `ConstructAir.Density` is under `Eva.VacuumBelow` (0.05). A ship counts as vacuum unless its life support is pressurising it ("Ship air" below).
- Without a suit a player has `Eva.BreathSeconds` (20 s) of breath, then takes `Eva.ChokeDamage` a second and dies "suffocated". Breath comes back in air.
- A suit holds `Eva.SuitSeconds` (30 min) of air, refilled in a planet's air or aboard a pressurised ship (from the ship's oxygen). A Space Suit Locker fills the suit it hands out the same way; with neither, the suit comes as it was hung up.
- A person who comes into play out in vacuum (a spawn, a respawn, a saved world rejoined in orbit: within `Eva.IssueTicks` of `SpawnTick`) is given a full suit, so the ship starts don't choke anyone.
- Robot shells and NPC bodies (bots) don't breathe (`Eva.Breathes`). Noclip doesn't either.
- The client runs the same `Eva.Step` for its HUD line; the server's is the one that hurts.

## Ship air (Karl, 2026-11-18)

A ship's air is limited and runs out sooner the more people are aboard. Code: `Source/Constructs/ShipAir.cs` (the rules and numbers), `Source/Game/GameShipAir.cs` (the server's count and drain, the F reading), parts in `EvaParts.cs`.

- **Life Support Unit** (`"air": {"kind": "unit", "holds": 240}`): a wall cabinet that keeps the air. Aboard a body whose unit works (not wrecked) and that has oxygen left, the body is pressurised: anyone aboard (seated, standing or floating in it) breathes with no suit, and a suit refills from it. It holds 4 person-hours of its own. Without a working unit the ship is vacuum, whatever its tanks hold.
- **Oxygen Tank** (`"kind": "tank", "holds": 720`): two bottles on a wall frame, 12 person-hours more. The unit takes from the tanks first and its own reserve last.
- **Use.** Everyone aboard who breathes uses `ShipAir.PerPersonMinute` (1 person-minute a minute): players (not robot shells, not noclip) and the body's crew records that aren't dead, prisoners included (surrendered members still aboard). Counted once a second by the server (`Construct.AirAboard`); a suit filling aboard costs 1 person-minute per minute of suit air (a full suit: 30). The oxygen is kept in each air part's `CPart.Rounds` in person-minutes, so it is saved with the world and synced like a magazine.
- **Running out.** Below 10% everyone breathing it is told; at nothing the ship is vacuum again: suits run on their own 30 minutes, the rest have their 20 s of breath. Crew bodies don't choke (NPCs don't breathe yet), they only use it.
- **Refill.** A working unit in breathable outside air (a planet's) draws it in at `ShipAir.RefillPerSecond` (12 person-minutes a second: a tank in a minute) and nothing aboard is used. Creative keeps every body's air full.
- **Docked craft.** A body with no life support of its own that is carried or gripped by one that has (the Shuttle in its cradle, the Limpet on a hull) breathes its carrier's (`ShipAir.AirBody`).
- **Spawned full, built empty.** Air parts in a blueprint the game spawns come full; a Building Station's build (`ConstructWorld.PlayerBuild`) and a part placed by hand come empty in Survival.
- **The time is real time.** The command view's faster time doesn't drain it (space time isn't world ticks).
- **HUD.** Aboard a ship with a unit, a line: `Ship air 74%, 3 h 20 min for 9 aboard` (orange under a quarter, red when gone). A joined client counts the people it sees aboard, so its time can read longer than the host's when crew have no bodies near. F on a unit or a tank gives its own share and the ship's line.

| Ship | Fitted | Air (person-hours) | Lasts |
| --- | --- | --- | --- |
| Zero Day | unit and two tanks, engineering's port wall | 28 | 28 h alone; about 2 h 50 min with the crewed one's 10 |
| Magpie | unit and a tank, engine deck port wall | 16 | 16 h alone |
| Rust Bucket | unit and a tank, the hold's port wall between the shelves | 16 | 16 h alone |
| Dray | unit and a tank, the mess's port wall | 16 | 16 h alone |
| Shuttle, Limpet | none: they breathe the Zero Day's while docked or gripped | - | - |

Not yet: holes (a breach, a cut) don't vent the air; no power draw (the Dray has no power yet); NPC crews don't suffocate.

## The parts

- **Space Suit Locker** (`"eva": "locker"`): F puts the suit on or takes it off. Refused in a robot shell and while wearing an MMU. The suit is `Player.Eva & Eva.Suit`, sent with the roster (after the uniform: protocol 336), drawn as the `space_suit` uniform (`Player.DrawnUniform`) whatever uniform is chosen. Its helmet (Karl, 2026-11-18) is a hollow cube round the head, about 3 cm clear of the ears, nose and hair, with a glass visor in front (a `"glass"` hat part: docs/sandbox/UNIFORMS.md), so the face shows through it.
- **MMU Dock** (`"eva": "dock"`): a seat with an `mmu` socket on its upright and a `Fuel in` pipe port on its base edge. Sitting needs a suit (or a shell). Seated, the docked pack fills from the body's fuel (`ReachesFuel`/`UseFuel`, `Eva.MmuLitres` per fill) in Survival, at once in Creative; its tank is stored in the pack part's `CPart.Rounds` (out of `Eva.MmuFull`), so it is saved and synced like a magazine. The eject key (L) detaches: the pack part is removed, `Player.Eva |= Eva.Mmu`, the player stands with a small push forward. Walking up to an empty dock wearing the pack and pressing F docks it again.
- **Manned Manoeuvring Unit** (`"eva": "mmu"`): the pack, original but after NASA's: a white frame, side tanks, four corner thruster quads, two arms forward to hand controllers. Its latch fits the suit's back plate.

## Flying

`PilotGear.Mmu` (gear bit 8) runs `MmuStep` in place of the jetpack. Karl, 2026-11-18: in vacuum the pack is a vehicle.

- **Attitude.** In vacuum and not seated, the mouse turns and pitches the spacewalker about their own axes (over the top and round, no pitch stop) and the roll keys (Q and E, a seat's `SeatRollLeft/Right`, which take their keys in the MMU as in a seat) roll them (`Game.MmuRollRate`, 1.6 rad/s). The client keeps the attitude in the world (`GameSandboxMmu.cs`) and measures the look's yaw and pitch back from it, so aim and tools follow the camera, whose up is the attitude's; the roll goes to the server with each command (`InputCmd.Roll`, an i16 trailer on the input packet, protocol 358) and `PilotGear.MmuAxes` rebuilds the same axes on both peers.
- **Thrust.** Forward/back along the look, strafe right/left, jump up and crouch down along the rolled up, `PilotGear.MmuAccel` (2.5 m/s2) per axis. The tank is `Eva.MmuFull` (3600) units, a unit `PilotGear.MmuBurnEvery` (20) thrusting ticks: twenty minutes of thrust (Karl: x20). The ticks into the next unit are counted in `MoveState.GearDelay` (sent with the move); the first version counted by `cmd.Seq`, which is 0 on the host's own player, so single player burned a unit a tick, a minute of thrust in all. A full fill takes `Eva.MmuLitres` (120 L) of the ship's fuel, still in 15 s. Thrusting shows as `Canopy = Burning` to others, who see cold-gas puffs off the quads.
- **No walking.** In vacuum (`PilotGear.MmuFree`, `World.SimulateMmu`) the walking movers get none of the move keys: no walking, crouching or jumping. Inside a ship they float in its frame (`ConstructRide.MmuFloat`: no deck gravity, no footing's friction, the pack's push turned into the frame), so the ship's speed never reaches them; out through a hatch they leave the frame with its motion as from any jump, and back inside its bounds coasting it catches them again. The wearer sits strapped in, knees bent forward a little like an upright barber's chair (`LegAnimator.PackLegs`: hips where standing puts them, ankles at `PackAnkle`; no gait, no planted feet, so nothing drags behind in flight), the boots on the pack's **foot pedestals** (struts down behind to a plate under each boot, toe loops and heel cups: `EvaRender.Pedestals`, placed from `PackAnkle`, drawn only when worn). In air (a planet) the pack is only a weak thruster on someone who walks, since 2.5 m/s2 can't lift anyone off a world.
- `Player.MmuFuel` mirrors the tank so a teleport (which resets `Sim`) keeps it. Dying or leaving the game with the pack drops it as a loose part holding its fuel. Detaching at a dock floats them forward off it at 0.6 m/s in the ship's frame. **Taking it off anywhere** (Karl, 2026-11-18): the eject key (L) out of a seat, while worn, sends `CActKind.MmuOff` (protocol 364; the recent-parts wheel on the same key gives way while the pack is worn); `Game.SandboxMmuOff` sets the pack loose 0.75 m behind their back with their world motion (a ship's frame and all) and its fuel, and they are a plain spacewalker: on a deck they walk, out in space they drift. To fly it again, pick it up and fit it on a dock.

No canopy in vacuum (2026-11-17): where `Eva.Vacuum` holds, `PilotGear.Step` never opens the parachute or the glider (the jump key; an ejection seat's delayed chute waits, held, and opens once they fall into air), and one already open stows; the jetpack still burns. The HUD's open hint is hidden there too.

## On the ships (2026-11-18)

Each ship's generator places the gear by its way out; `EvaParts.FitDock` hangs an MMU Dock with a pack on it and, given a tank, a pipe from the tank's Fuel out into the dock's Fuel in (`Blueprint.Pipes`, routed along the walls as conduit).

- **Zero Day** (`TorpedoFrigateParts.cs`): three Space Suit Lockers in the suit bay, one in the airlock, and the MMU Dock on the airlock's aft bulkhead, fed from Zero Day Keel 4. The airlock's belly hatch (2 m by 1.6 m, railed on three sides, open towards the dock) is the MMU's way out; its boarding ladder was removed (Karl, 2026-11-18: the old 1.25 m shaft ran half under the sloped plate beside the keel's flat, so it was sealed). On the ground there is no ladder up from it any more.
- **Magpie** (`MinerParts.cs`): two lockers and the dock on the cargo bay's port wall by the stern ramp, a few metres from the nacelles' service hatches; fed from the keel.
- **Rust Bucket** (`ShipParts.cs`): the dock and a locker on the hold's starboard wall by the bow ramp, a second locker to port; fed from the starboard saddle tank.
- **Dray** (`HaulerParts.cs`): a locker in the suit room, the dock and a locker in the airlock either side of the boarding shaft; no fuel line, as the Dray has no tankage yet. Its shaft moved 0.6 m inboard (2026-11-18) to stay inside the keel's flat, whose sloped plate had sealed its outboard edge.
- **Shuttle** (`TorpedoFrigateShuttle.cs`): its two cabin lockers are Space Suit Lockers; no room for a dock. The **Limpet** has none: its cabin is 1.98 m tall, under the locker.

A docked pack starts empty and fills in 15 s from the ship's fuel (at once in Creative).

## Not yet

- No brake or attitude hold; the seated pose in the dock is the plain sitting pose.
- Others see a spacewalker's body by its yaw only (the pitch and roll are the wearer's own view and thrust).
- The suit isn't kept in a world save (a player rejoining in vacuum is given a fresh one).
