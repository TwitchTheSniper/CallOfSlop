# The Hatchet fighter

The Raiders' wedge fighter (Karl, 2026-11-21, from a picture): a flat crimson and cream wedge with grey panels and dark frame ribs, a long raked canopy, twin cannon turrets on the wing tips, rocket pods under the wings, canted tail fins and a rear engine block. The Raiders get it because their brief is speed and hitting first: two guns and rockets on a light hull, no cargo.

- Generator: `Source/PartKit/Generators/WedgeFighterParts.cs` (`--gen-parts wedge_fighter_parts`); parts in `Constructs/Parts/Hatchet`, blueprint `Constructs/Blueprints/HatchetFighter.json` ("Hatchet Fighter"). The pieces are blueprint-only (Categories.json's Ship name rule).
- Size about 14 m across the wings and 14 m long, about 18 t, smaller than the Zero Day. Lofted hull (five-point stations), welded pieces: Belly, Hull Port and Starboard, Nose, Canopy, Roof, Bulkhead, Stern and two Wings.
- Crew of two: the pilot couch forward under the canopy (flies, and fires the two Rocket Pod Mk2 through a Turret Module), the gunner couch behind at a Gunnery Console, aiming both wing-tip Hatchet Twin Cannons through one floor Turret Module (a seat aims one module; each gun is about 6.6 m of wire from it).
- Drives: two Hatchet Drives (300 kN each, a Drive Core XS behind each on the stern's mount), four Hatchet Lift Jets, RCS on the wing pods, nose and stern, four leg pods (size S). Fuel in the belly and both wings.
- Way in: a belly hatch with a boarding ladder, on the cabin's port switch. Cabin: suit lockers, Life Support, a Ship Battery XS and the Hatchet Power Cell.
- Checks run: `--check-vehicle` (0 coplanar faces, 0 slivers, works 100%, both cannons on the gunner's look; its wheel lines are ground checks), `--flight-check` (flies, hover drift 0.48 m, lift 2.30x), `--ladder-check`, `--selftest-only "No z-fighting, Compound collision"`.
- Not built: Assembling recipes, a Raiders fleet entry (Crew/Controllers.json), crew AI tried in it.
