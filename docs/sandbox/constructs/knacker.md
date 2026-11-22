# The Knacker breaker and the compact landing gear

The Salvagers' breaker (Karl, 2026-11-21: "New design using a different style please. This one can be for salvage"; kept in the game the same day, "first remove a little bit of the beading"). The third ship drawn on the ship kit (ship-kit.md) and the first in the Salvagers' look from their mood board: a frame for holding someone else's ship.

- Generator: `Source/PartKit/Generators/KnackerParts.cs` (`--gen-parts knacker_parts`); parts in `Constructs/Parts/Knacker` (53, blueprint-only by Categories.json's Ship name rule), blueprint `Constructs/Blueprints/KnackerBreaker.json` ("Knacker Breaker"), 148 parts, about 102 t.
- Shape: two open truss jaws reach 18 m forward of a wide plated root, fendered clamp pads inside and chevron claws at the tips; the bridge looks down between them. Behind it the sorting floor (a Recycler and a row of Materials Bins), then a narrower crew pod (four bunks; the utility room with the belly hatch and the boarding ladder). Four standard containers ride on open racks along the flanks, and two engines sit in lime-strapped cradles on the stern.
- Flying: six Knacker Lift Jets and two drives, 480 kN each (under the works' 500 kN, so no works). Fuel 14000 in the keel, 6000 in the stern. It stands on six Ram Legs.
- Look: bare galvanised zinc patchwork with heat-tinted cut seams, dark mill-scale frames, fender black, claim lime where hands go and material flows, the odd panel in another faction's paint. Hoop ribs only at three stations (ZR2, the root's end and the pod's end); the other three went with "remove a little bit of the beading".
- Checks run: `--check-vehicle` (0 coplanar faces among 2856 boxes), `--flight-check` (flies; lift 1.75x), `--ladder-check` (up and down ok), `--check-building` (ok, 85 m2), `--selftest-only "Assembling, No z-fighting, Compound collision"`.
- Not built: the jaws don't move or hold a wreck yet (they are look and frame), and the Knacker is in no Salvager route.

## Compact landing gear

`Source/PartKit/Generators/LandingGearParts.cs` (`--gen-parts landing_gear`; parts in `Constructs/Parts/LandingGear`): the leg pods' working leg without the pod, a shallow mount welded under a keel, a leg on a hinge and a sprung foot. Three sets: the Tuck Leg (a slim leg folding aft flat into a flush bay), the Skid Leg (a raked leg on a skid runner, folding forward) and the Ram Leg (a hydraulic ram in an open cradle on a broad square pad). Each has a test blueprint (`TuckLegGear`, `SkidLegGear`, `RamLegGear`) and a deploy sequence in `tools/preview/anim/`. A ship hangs a set with `LandingGearParts.HangGear` and stands at `StandingOf(name)` below its keel.
