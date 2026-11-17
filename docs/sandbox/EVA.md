# Spacewalks: the space suit and the Manned Manoeuvring Unit

Three parts (part kit generator `Source/PartKit/Generators/EvaParts.cs`, files in `Constructs/Parts/Eva`, inventory Flight > Pilot gear) and a breath rule. Code: `Source/Game/GameSandboxEva.cs` (rules, use, dock, HUD), `Source/Physics/PilotGear.cs` (`MmuStep`), `Source/Render/EvaRender.cs` (the pack on the back), the space suit uniform in `Cosmetics/uniforms.json` (`"space_suit"`).

## Breath in vacuum (Karl, 2026-11-17)

- Vacuum is past the space bubble's edge (`GravityFrames.Point.OrbitFrom`) or where `ConstructAir.Density` is under `Eva.VacuumBelow` (0.05). Ships count as vacuum too, until pressurised rooms exist.
- Without a suit a player has `Eva.BreathSeconds` (20 s) of breath, then takes `Eva.ChokeDamage` a second and dies "suffocated". Breath comes back in air.
- A suit holds `Eva.SuitSeconds` (30 min) of air, refilled in air or seated in an MMU Dock.
- Robot shells and NPC bodies (bots) don't breathe (`Eva.Breathes`). Noclip doesn't either.
- The client runs the same `Eva.Step` for its HUD line; the server's is the one that hurts.

## The parts

- **Space Suit Locker** (`"eva": "locker"`): F puts the suit on or takes it off. Refused in a robot shell and while wearing an MMU. The suit is `Player.Eva & Eva.Suit`, sent with the roster (after the uniform: protocol 335), drawn as the `space_suit` uniform (`Player.DrawnUniform`) whatever uniform is chosen.
- **MMU Dock** (`"eva": "dock"`): a seat with an `mmu` socket on its upright and a `Fuel in` pipe port on its base edge. Sitting needs a suit (or a shell). Seated, the docked pack fills from the body's fuel (`ReachesFuel`/`UseFuel`, `Eva.MmuLitres` per fill) in Survival, at once in Creative; its tank is stored in the pack part's `CPart.Rounds` (out of `Eva.MmuFull`), so it is saved and synced like a magazine. The eject key (L) detaches: the pack part is removed, `Player.Eva |= Eva.Mmu`, the player stands with a small push forward. Walking up to an empty dock wearing the pack and pressing F docks it again.
- **Manned Manoeuvring Unit** (`"eva": "mmu"`): the pack, original but after NASA's: a white frame, side tanks, four corner thruster quads, two arms forward to hand controllers. Its latch fits the suit's back plate.

## Flying

`PilotGear.Mmu` (gear bit 8) runs `MmuStep` in place of the jetpack: forward/back along the look, strafe right/left, jump up and crouch down along the look's up, `PilotGear.MmuAccel` (2.5 m/s2) per axis, one tick of fuel per thrusting tick. Thrusting shows as `Canopy = Burning` to others, who see cold-gas puffs off the quads. No thrust while grounded, riding or mantling. `Player.MmuFuel` mirrors the tank so a teleport (which resets `Sim`) keeps it. Dying with the pack drops it as a loose part holding its fuel.

## Not yet

- No ship blueprint carries a dock or locker yet.
- No brake or attitude hold; the seated pose in the dock is the plain sitting pose.
- The suit and the worn pack aren't kept in a world save (the player comes back unsuited).
