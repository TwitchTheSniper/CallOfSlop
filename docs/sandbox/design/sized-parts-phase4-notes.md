# Sized gun and engine parts: phase 4 notes (2026-11-13)

Branch `claude/sized-parts-4-ccd7ea`. The Showcase press traced, phase 3's answers (a receiver's mass), and
the first coherent part of phase 4 of `sized-gun-engine-parts.md` ("The engine reads scale"). **Protocol 298** (the engine's formula data
changed: `ModularEngine.json` /2; nothing new on the wire).

## 1. Why the Showcase press "made no barrel"

**It did.** Traced on a copy of the Production Showcase with a Barrel Die in its Press: `feed Press 1 Extrusion round 10x10x70 steel` is
taken, and in the same step the press (creative: no stroke time) makes `Pressed Part (barrel.plain round 10x10x70 steel)` and sends it out
of its **Piece out**, which the Showcase wires to a 7.4 m conveyor into a Conveyor Join, then a second join and 2.5 m on to the Long Piece
Rack: about 11 m at 0.5 m/s, some 22 s. Phase 3 looked at the bay and at loose bodies near the press within 300 window frames (about 1.3 s
of simulation: the window runs several frames a tick) and saw neither. Followed for 6,000 frames, the rider went Press, join, join and
reached the last conveyor before the rack. Start, power, the die's class and the order state were all fine (`Ready`, powered, no orders).

What changed for the player: nothing said where a made part went. Now
- the press's die screen's first line reads `Press -> conveyor` when a Piece out conveyor leads away;
- its look line's state adds `what it makes leaves on its Piece out conveyor` (the look line's cache key knows the conveyor).

Two window-tool steps for the hand tests (`Program.WindowShot.cs`, host only): `fitpart NAME | PART[#n] SOCKET` fits the nearest loose
one-part body of NAME onto that socket by the build rules (`ConstructBuild.Place` and `Attach`, its "back" socket first) and removes the
loose body; `plate DIST` stands a Plate Steel 1 m (armour 20) up, pinned, DIST m out along the spawned body's first gun, and `plate` again
prints its health, the gun's loaded rounds, its box and its trigger wire.

## 2. Phase 3's answers: the generated receivers stay catalogue; a pressed receiver weighs the merge's mass

The generated receivers were built as Receiver die patterns before the user's answers came; the answer was "No, catalogue only", so that
commit is reverted: the Receiver die keeps its three patterns, and with it went the scale band (0.75 to 6, which only the 15 cm generated
presets needed) and a like-body z-fight settle (only the Kingpin Revolver at 2x needed it). `Pressing.json` is as phase 3 left it
(`--gen-parts press_parts --check` clean; `sweep receiver` 27 blanks, 0 problems).

**Mass** (answer 1): a pressed receiver now weighs the catalogue file's mass times s to the merge's `partMass` power (2), with no bay 2
mass: `receiver.rifle solid 40x40x80` 56 kg (was 82), `solid 15x15x30` 7.88 kg, `receiver.heavy solid 50x50x100` 128 kg. Health, armour
and everything else stay the blank's. The drawn scale stays round half up (answer 2).

## 3. Phase 4: the engine reads scale (first part)

Built:
- `ConstructPartDef.PieceScale` (0 on every file piece: read as its class's row, `EngineRules.ScaleOf`; phase 5 sets it from a blank).
- `EngineRules.At(scale)`: kW, idle, peak and limiter rpm by a straight line in log-log between the `sizes` rows, the end segments past
  them; **at a knot it returns the row itself**. `KwAt`, `NearestSize`. The loader now refuses sizes whose scales don't rise.
- `ModularEngine.json` format `/2` (`/1` still loads): `starterFrom` 8. An engine needs a starter when its crank's scale reaches it
  (`ConstructEngine.NeedsStarter`), the same answer as the old per-size flags for every file engine.
- `ModEngine.Scale`: the crank's, its **largest section's**; `Size` is the class nearest it. Apply reads rpm from `At(Scale)`.
- **Anything bolts on.** `MixedSizes` is never raised. Engine piece to engine piece mates across socket classes in `ConstructBuild.Place`
  and `SnapBuild.CanMate` (`ConstructBuild.EngineMate`); every other part keeps the class rule. A flywheel of any size is the output.
- Each cylinder gives `KwAt(its own scale) x PiecePower x share x fuel x boost`. A bank's fuel feed is `min(1, KwAt(fuel) / KwAt(the
  bank's largest cylinder))`, a booster's gain `1 + (power - 1) x` the same ratio (its own multiplier exactly when the ratio is whole).
- The look line (and readout) names the first starved bank: `bank 1 starved: the Small Carburettor feeds 5% of its cylinders`, or
  `bank 1: the Small Turbo is small for its cylinders (40% of its boost)`, after any fault (`StarvedText`).
- Sounds and the drawn shake read `e.Scale`; the Engine Kit's read-back takes rpm from `At(e.Scale)`.
- Self-test "Modular engine": the three cases that pinned "mixed sizes: use one size" now pin the new rule (a small cylinder on a medium
  crank gives I1 40 kW; a Medium cylinder with a Small Carburettor on a small crank reads I1 40 kW and the starved text).

Not built (left for the next part of phase 4 or for phase 5): **stroke** (`ModEngine.Stroke`, rpm by the cylinders' weighted `r`). Every
file cylinder has r = 1, and only a pressed cylinder (phase 5) can differ, so nothing a player builds today would show it.

## What was tried, and seen

All in throwaway runs (`--save-dir` to a temp folder; test blueprints in `bin/` only, removed after).

- Showcase copy with a Barrel Die: the barrel was made at once and rode the conveyors as above; host and a **joined client** both showed
  `Press -> conveyor` and the client saw the barrel riding `Press#256 -> Conveyor Join#261`.
- A barrel pressed on a real press (a Press with a Barrel Die beside a Rifle Receiver, no Piece out): it dropped loose in front, `fitpart`
  put it on the receiver's front socket: `fires, reach 360 m, spread 0.8325 deg, damage 26`. Fired through its Switch with real F at a
  plate 8 m out: plate 160 to 130 to 58 to gone, 6 a hit (26 less armour 20). The first try used an Armour Plate (armour 40): no damage,
  as the rule says.
- A pressed `receiver.rifle solid 40x40x80` with a pressed `barrel.plain round 20x20x100`: `damage 104, every 12 ticks, 4 rounds a shot,
  magazine drawn 2x`; the plate (160) was gone within two shots (run before the mass change, which moves no gun number).
- Engines, one Small crank and a Small Flywheel: Small Cylinder and Small Carburettor `I1 40 kW`; a Medium Cylinder and a Medium
  Carburettor `I1 800 kW` (rpm from the Small row: `At(1)` is the row); a Medium Cylinder and a Small Carburettor `I1 40 kW ... bank 1
  starved: the Small Carburettor feeds 5% of its cylinders`, the same on a joined client.
- `--gen-engine` for the five ENGINEKIT.md sample rows: the 300 kW tier 1 and 5 and the 20 MW rows match the doc; the 900 N m tier 3
  (B8 480 kW) and 2000 kW tier 4 (R28 2038 kW) rows differ from it, **identically on the code before this phase** (checked by building the
  previous commit), so the doc's table is stale, not this change. `--gen-engine-parts --check`: clean.
- `--press-part presets` 0 problems, `sweep receiver` 0 problems (after the revert). Self-test sections by name: Modular engine (after the update), Construct
  weapons, Construct merged receivers, Sandbox weapon parts, Press stock: 0 failures.
- `--check-construct-parts`: exit 0. `--gen-parts all --check`: every generator 0 out of date, 0 stale. `--gen-vehicles`: 238 vehicles, all
  identical to the committed files.

Not tried: `--speed-test` on two shipped vehicles (their engines are one class each, every ratio 1, so their numbers can't move; not
measured); a bigger engine with a Medium crank and a turbo of another size (the boost text); a Large-scale mix and its starter; a
picture of the mixed engine (the Medium cylinder hides the Small crank and flywheel, which is what anything-bolts-on looks like).

## Questions for the user (each with a recommendation; they can be asked together)

1. **Mixed sockets on engines.** Pieces of other sizes now mate through their sockets whatever the class (a Medium cylinder's base on a
   Small crank's side). Nothing else changed the socket class rule.
   - A (recommended): engine pieces only, as built.
   - B: also let them auto-link to neighbours of another class (today only the placed socket links across classes).
2. **The ENGINEKIT.md sample table** is stale (two rows) on main already.
   - A (recommended): re-record it in phase 6's docs pass.
   - B: re-record it now in a docs-only commit.

## What is left of phase 4

- Stroke: `ModEngine.Stroke`, the weighted `r`, rpm divided by it (only matters with phase 5's pressed cylinders).
- `--speed-test` on two shipped vehicles against the doc's figures.
