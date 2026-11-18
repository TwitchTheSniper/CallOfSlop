# Spacewalks: the space suit and the Manned Manoeuvring Unit

Three parts (part kit generator `Source/PartKit/Generators/EvaParts.cs`, files in `Constructs/Parts/Eva`, inventory Flight > Pilot gear) and a breath rule. Code: `Source/Game/GameSandboxEva.cs` (rules, use, dock, HUD), `Source/Physics/PilotGear.cs` (`MmuStep`), `Source/Render/EvaRender.cs` (the pack on the back), the space suit uniform in `Cosmetics/uniforms.json` (`"space_suit"`).

## Breath in vacuum (Karl, 2026-11-17)

- Vacuum is past the space bubble's edge (`GravityFrames.Point.OrbitFrom`) or where `ConstructAir.Density` is under `Eva.VacuumBelow` (0.05). Ships count as vacuum too, until pressurised rooms exist.
- Without a suit a player has `Eva.BreathSeconds` (20 s) of breath, then takes `Eva.ChokeDamage` a second and dies "suffocated". Breath comes back in air.
- A suit holds `Eva.SuitSeconds` (30 min) of air, refilled in air or aboard a construct, seated or standing (`Eva.Fed`: the ship's air line, until pressurised rooms exist).
- A person who comes into play out in vacuum (a spawn, a respawn, a saved world rejoined in orbit: within `Eva.IssueTicks` of `SpawnTick`) is given a full suit, so the ship starts don't choke anyone.
- Robot shells and NPC bodies (bots) don't breathe (`Eva.Breathes`). Noclip doesn't either.
- The client runs the same `Eva.Step` for its HUD line; the server's is the one that hurts.

## The parts

- **Space Suit Locker** (`"eva": "locker"`): F puts the suit on or takes it off. Refused in a robot shell and while wearing an MMU. The suit is `Player.Eva & Eva.Suit`, sent with the roster (after the uniform: protocol 336), drawn as the `space_suit` uniform (`Player.DrawnUniform`) whatever uniform is chosen. Its helmet (Karl, 2026-11-18) is a hollow cube round the head, about 3 cm clear of the ears, nose and hair, with a glass visor in front (a `"glass"` hat part: docs/sandbox/UNIFORMS.md), so the face shows through it.
- **MMU Dock** (`"eva": "dock"`): a seat with an `mmu` socket on its upright and a `Fuel in` pipe port on its base edge. Sitting needs a suit (or a shell). Seated, the docked pack fills from the body's fuel (`ReachesFuel`/`UseFuel`, `Eva.MmuLitres` per fill) in Survival, at once in Creative; its tank is stored in the pack part's `CPart.Rounds` (out of `Eva.MmuFull`), so it is saved and synced like a magazine. The eject key (L) detaches: the pack part is removed, `Player.Eva |= Eva.Mmu`, the player stands with a small push forward. Walking up to an empty dock wearing the pack and pressing F docks it again.
- **Manned Manoeuvring Unit** (`"eva": "mmu"`): the pack, original but after NASA's: a white frame, side tanks, four corner thruster quads, two arms forward to hand controllers. Its latch fits the suit's back plate.

## Flying

`PilotGear.Mmu` (gear bit 8) runs `MmuStep` in place of the jetpack: forward/back along the look, strafe right/left, jump up and crouch down along the look's up, `PilotGear.MmuAccel` (2.5 m/s2) per axis, one tick of fuel per thrusting tick. Thrusting shows as `Canopy = Burning` to others, who see cold-gas puffs off the quads. No thrust while grounded, riding or mantling. `Player.MmuFuel` mirrors the tank so a teleport (which resets `Sim`) keeps it. Dying or leaving the game with the pack drops it as a loose part holding its fuel. Detaching in space leaves the ship's ride frame (`ConstructRide.Leave`), so the pack fires at once.

No canopy in vacuum (2026-11-17): where `Eva.Vacuum` holds, `PilotGear.Step` never opens the parachute or the glider (the jump key; an ejection seat's delayed chute waits, held, and opens once they fall into air), and one already open stows; the jetpack still burns. The HUD's open hint is hidden there too.

## On the ships (2026-11-18)

Each ship's generator places the gear by its way out; `EvaParts.FitDock` hangs an MMU Dock with a pack on it and, given a tank, a pipe from the tank's Fuel out into the dock's Fuel in (`Blueprint.Pipes`, routed along the walls as conduit).

- **Zero Day** (`TorpedoFrigateParts.cs`): three Space Suit Lockers in the suit bay, one in the airlock, and the MMU Dock on the airlock's aft bulkhead by the boarding shaft, fed from Zero Day Keel 4.
- **Magpie** (`MinerParts.cs`): two lockers and the dock on the cargo bay's port wall by the stern ramp, a few metres from the nacelles' service hatches; fed from the keel.
- **Rust Bucket** (`ShipParts.cs`): the dock and a locker on the hold's starboard wall by the bow ramp, a second locker to port; fed from the starboard saddle tank.
- **Dray** (`HaulerParts.cs`): a locker in the suit room, the dock and a locker in the airlock either side of the boarding shaft; no fuel line, as the Dray has no tankage yet.
- **Shuttle** (`TorpedoFrigateShuttle.cs`): its two cabin lockers are Space Suit Lockers; no room for a dock. The **Limpet** has none: its cabin is 1.98 m tall, under the locker.

A docked pack starts empty and fills in 15 s from the ship's fuel (at once in Creative).

## Not yet

- No brake or attitude hold; the seated pose in the dock is the plain sitting pose.
- The suit isn't kept in a world save (a player rejoining in vacuum is given a fresh one).
