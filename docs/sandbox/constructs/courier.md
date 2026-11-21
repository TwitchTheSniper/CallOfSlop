# The Satchel courier

The Traders' small fast courier (Karl, 2026-11-20): two standard containers ("Standard Container 2m", 2.5 x 2.5 x 6.5 m) side by side in the middle of the ship, in a bay open to the sky, so a crane on a dock lifts each one straight out by its four top corner castings. Faction look follows the Packet Spine Freighter (freighter.md); it is lower and quicker: a short cab forward, the bay amidships, engineering aft and two big drive nacelles on swept pylons.

- Generator: `Source/PartKit/Generators/CourierParts.cs` (`--gen-parts courier_parts`), started from the Packet's FreighterParts.cs; parts in `Constructs/Parts/Satchel`, blueprint `Constructs/Blueprints/SatchelCourier.json` ("Satchel Courier"). The pieces are blueprint-only (Categories.json's Ship name rule).
- Size about 9 m across the hull (17 m over the nacelles) and 33 m long, 121 t, two Drive Nozzle M (5 MN each) with Drive Core M and Fuel Pump M in the nacelles, four lift jets, four legs, RCS fore and aft.
- Cab: cockpit at the bow; port a bunk room (two bunks) and the head; starboard a galley and the airlock (two suit lockers, Life Support and an Oxygen Tank). A full-width aft wall with a centre door gives onto a cross passage.
- Bay: a side passage each side outside the containers, the bay floor between, eight twist-lock pads (`lock{L|R}{0..3}`, medium sockets) with footprint lines; each container hangs on `lock{L|R}0` by its `foot.0`, its hatches face the passages. Containers are blueprint steps, not cargo, so the game spawns them in place.
- Engineering: open to both passages; power plant (port), batteries (starboard), power board and tools, MMU dock, suit and Pressure Lockers on the aft wall.
- Checks run: `--check-vehicle` (0 coplanar faces, 0 slivers; its wheel and drive lines are ground-vehicle checks), `--flight-check` (flies; hover tilt 0.2, lift 2.65x), `--ladder-check "Satchel Courier"`.
- Recipes: one Assembling row per piece, at the end of Assembling.cs' Tail (Karl, 2026-11-21: "Add to build").
- Not built: a dock crane that lifts the containers, the courier in the Traders' routes (Crew/Stations.json).
