# Formers take work orders; catalogue wings become former presets (2026-11-13)

Branch `claude/formers-work-orders-9c8387`, Windows PC. No wire change (protocol stays 286). Guide: `production.md` "Work orders" and
"A former's orders".

## What the formers accepted before

- **Wheel Former**: the Extruder's whole set (queue by key, Queue/Cancel/Clear, Line Console bill, `ConstructCrafting` lists a wheel at its first size).
- **Fan Former, Airframe Former**: nothing. `WorkOrders.TakesOrders` refused them ("takes no orders yet"), their screens had only a Recipe button,
  and a blueprint bill line naming a fan or wing piece was refused by `ConstructBill.ParseLine`.
- **Catalogue wings and rotors** already had **no Assembler recipe**: the part kit's `Assembling.cs` skips the `Wings` folder and every part of
  role `rotor` (`SkipFolders`, the `rotor` test in the long tail). So there was nothing to move off the Assembler, no marker rows, and no saved
  Assembler can name them. In survival they came only from wrecks; `--bill` listed them as "no recipe".

## Step 1: what the Fan and Airframe Formers take now

Shared code in `WorkOrders.cs` (no copy): `TakesOrders` is every Extruder-family machine; `FormerRefusal` is their side.

- **An order** is a prop by its key (`Extrusion fan0.straight.3 0x220x0 steel`: kind, style, blades, diameter) or a wing by its key
  (`Extrusion wing-aileron 50x0x200 steel`: kind, chord, span), or a catalogue part that is a preset (below).
- **Refusals**: the other former's shape ("the Fan Former forms props and rotors: a Wing ... is an Airframe Former's order"), a material its
  table hasn't, a size past or under the machine (`FormerPresets.Fits`), off the 5 cm step, a full queue, a bin fed another material, a part
  that is no preset ("no former's preset makes the Rudder") or an Assembler's.
- **The machine** (`ConstructExtruder.Work`, `GrowWhole`): the head order's key replaces the settings (kind, blades and style, or kind and
  chord), the piece grows to the order's diameter or span and parts off; a piece on the die that isn't the head's is cut off as it is; an
  empty queue waits. `ForHead` takes the wanted key, so a prop matches on kind/style/blades and a wing on kind and chord.
- **At the machine**: the formers' part files (part kit `extrusion_parts`) now have the status lamp, an 8-row queue, Queue, Cancel, Clear,
  Recipe, Up, Down and an Order count. Queue makes one order of its settings in the bin's material.
- **Bill**: a blueprint's bill line may name a fan or wing piece, or a preset part (`{"step": n, "part": "Wing Segment", "count": 1}`);
  `ConstructBill.ToConsole` puts preset parts on the console; Send deals them to a former (the kept-line reason prefers a former's).
- **Screens**: `WorkOrders.Row` shows `0/1 Wing 1x1.5 steel` and `0/2 Propeller 2.2m 3 blades steel`.

## Step 2: the presets

`Constructs/Extrusion.json` `"presets"`: `{"part": NAME, "piece": KEY}`, read by `ExtrusionTable.Presets`, looked up by
`FormerPresets.KeyOf(part)`. An order for that catalogue part at a former grows the key's piece, paid by volume like any piece, and at the
cut **the catalogue part itself** comes off in its place (`ConstructExtruder.CutAs`): on a Piece out conveyor as a made part
(`ConstructConveyor.PutMade`), else dropped where the piece lay (a wing turned so its span lies along the piece's). Blueprints keep naming
the same part, so nothing that flies changes.

`--former-presets` compares every catalogue wing, control surface and rotor with the key a former would cut for it (area, cl0, stall, cd0,
slope, induced drag, control throw, effect and sign within 0.5 %; mass within 1 %; a rotor's radius, power, rpm, motor, blades, top speed,
spool, duct, heli), and fails when the presets list differs from the parts that agree. Now: **12 presets, 0 problems.**

| Preset (Airframe Former key) | Smallest former |
| --- | --- |
| Wing Segment (`wing-wing 150x0x200 steel`), Short Wing Segment (`150x0x100`), Swept Wing L and R (`swept-left/right 150x0x200`) | Airframe Former (tier 1) |
| The Medium four (`600x0x800`, `600x0x400`) | Medium Airframe Former (tier 2) |
| The Large four (`2400x0x3200`, `2400x0x1600`) | Large Airframe Former |

### What stayed, and why (the difference)

| Parts | Difference from the former's piece |
| --- | --- |
| Aileron, Elevator, Flap, Rudder, Tail Fin, Tail Plane (Small) | flight numbers the same; mass 8-13 % lower (catalogue 8 kg/m2 vs the skin formula: Aileron 8 vs 9 kg) |
| The same six, Medium and Large | flight numbers the same; tail fins and tail planes 8 % lighter, control surfaces 29-35 % heavier (Medium Aileron 256 vs 166 kg) |
| Narrow Wing Segment (all sizes) | aspect 8 vs the wing row's 7: slope 5.03 vs 4.89, induced 0.050 vs 0.057 |
| Fabric Wing Segment (all sizes) | cl0 0.30 vs 0.25, stall 14 vs 15, cd0 0.030 vs 0.029, aspect 6 vs 7 |
| Every Capital surface | chord or span past `Extrusion.MaxChordCm` (32 m) or the Capital former; most don't build as a key |
| All 12 rotors | rated kW and rpm 3-55 % off, mass 0.16-2.8x, spool and top speed differ; Tail Rotor is shaft-driven where the tail kind has a motor; Medium Tail Rotor 700 vs 251 kW |

These stay as they were: no recipe, found on wrecks; the formers' own run-time pieces of the same kinds stand in for them.

### A survival player's first aircraft

Nothing became impossible (nothing had a recipe to lose). What a first plane needs is unchanged and now orderable: a **tier 1 Assembler**
builds an **Airframe Former** and a **Fan Former** (104 steel, 21 rubber each). The Airframe Former cuts the catalogue Wing Segment by
order, and its own ailerons, elevator, rudder and fin as pieces; the Fan Former makes the prop as a piece. Medium presets need a tier 2
Medium Airframe Former.

## Tried in a throwaway window run

`--window-shot` on the Proving Ground at `--spawn-at 140,0,-100`, a scratch bench (Foundation x2, Airframe Former set to Wing 1.0 x 1.5,
Battery, Line Console with a bill line `"part": "Wing Segment"`; the blueprint was deleted afterwards), `fill` 400 steel:

- Bill, Send, Line, Start: the former's screen read `1 order, 1 to make / >0/1 Wing Segment`, Forming.
- Queue on the former: `2 orders / >0/1 Wing Segment / 0/1 Wing 1x1.5 steel`.
- After 1,800 frames: loose near it **Wing Segment x1** (the catalogue part); after 1,500 more **Extrusion wing-wing 100x0x150 steel x1**,
  the screen `Waiting for orders`, stock 400 -> 384 steel.
- The preset part is the catalogue def itself, so "compared with its twin" is the `--former-presets` row: area 3 m2, cl0 0.25, stall 15,
  cd0 0.018, aspect 7, 27 kg on both sides.

Checks: `--check-construct-parts` exit 0; `--gen-parts all --check` all up to date; `--gen-vehicles` 238 identical; `--former-presets` 0
problems; self-test by name **Wheel orders, Display screens, Extruder, Assembling**: 0 failures. No section added.

## Not verified

- A **Fan Former** order in a window (only the Airframe Former was run); the code path is the same `GrowWhole` with the diameter.
- A preset riding a **Piece out conveyor** (`PutMade`): not run.
- Survival timing and cost (the run was creative); host and client; a save and load with a former's queue (orders are saved by the
  existing code, the key for a preset part order is derived again from the part).
- The **Crafting Terminal** and **chained orders** don't know formers yet: see "For the coordinator".

## For the coordinator (other sessions' files)

1. `Source/Constructs/ConstructCrafting.cs` (logistics), `MachineMakes`: after the Press branch, a former makes a preset part:
   ```csharp
   if (ConstructBill.PresetOf(ConstructParts.Get(part)) is not { } spec)
   {
       if (FormerPresets.KeyOf(part) is { } fk)
           for (var i = 0; i < net.MachineN; i++)
               if (c.DefOf(net.Machines[i]) is var fd && WorkOrders.IsWholeFormer(fd) && (fk.Shape == ExShape.Fan) == fd.IsFanFormer && FormerPresets.Fits(fd, fk) == null)
                   return fd.Name;
       return null;
   }
   ```
   and in the per-machine piece listing (`if (md.IsWheelFormer) Piece(...)`, about line 471), before it: for a Fan or Airframe Former,
   list each preset whose key `WorkOrders.Refusal` accepts as a catalogue row (as a press lists its die's presets), and skip the wheel
   piece; `FirstMaterial` should use `FindFan`/`FindWing` for those formers.
2. `Source/Constructs/MachineScreens.cs` (logistics), `Extruder`, the piece line under orders: a whole former's piece prints as
   `Wheel 1.50 x 0.00 m`. Use `d.IsFanFormer ? "Prop " + H across : d.IsAirframeFormer ? "Span " + L` before the `wheel` branch.

## Questions for the user

1. **Mass tolerance for presets.** Small control surfaces, fins and tail planes match in every flight number but weigh 8-13 % less than
   the skin formula's piece. (a) Keep strict (1 %): 12 presets. (b) Accept 15 %: 10 more presets (the six Small ones, and the Medium and
   Large tail fins and tail planes). (c) Retune the catalogue masses or the skin formula so they agree. **Recommended: (c) for the Small ones,
   via the part kit's `WingParts`** (the vehicle styles' owner should agree first, since a plane's weight changes slightly).
2. **Rotors.** No catalogue rotor matches a Fan Former key (power per disc, rpm, mass). (a) Leave them wreck-only; the Fan Former's own props
   fly. (b) Retune `fanKinds` so the catalogue props are on the curve. (c) Accept them as presets with the catalogue numbers. **Recommended:
   (a)**, since retuning either side changes how shipped aircraft fly.
3. **Narrow and Fabric wing segments.** (a) Leave them. (b) Add `airframes` rows "narrow wing" (aspect 8) and "fabric wing" (cl0 0.3,
   stall 14, aspect 6) so they become presets. **Recommended: (b)**: two data rows, no change to any part.
