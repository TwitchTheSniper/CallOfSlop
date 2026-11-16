# The Zero Day torpedo frigate (the IT Department's warship)

Karl (2026-11-15): a state-of-the-art faction torpedo frigate, sleeker and bigger than the Halberd, with countermeasures,
a torpedo bay on each side, a railgun and a drive the width of the ship; faceted corners and stepped armour like his
reference picture (the MORN 251 cutaway), decks running the length of the ship, and doors and fittings you'd expect on a
spaceship. The faction is the IT Department, known for powerful, state-of-the-art ships: pale skin, dark armour slabs,
link-blue light strips, server racks aboard. It is an enemy ship, but it builds and previews like any other.

Generator: `Source/PartKit/Generators/TorpedoFrigateParts.cs` (`--gen-parts torpedo_frigate_parts`), which writes
`Constructs/Parts/ZeroDay/*.json` and `Constructs/Blueprints/ZeroDayFrigate.json`. Change the generator, never its output.
Built the way the Halberd is (frigate.md): big parts that share the body's origin, each holding a deck's floor, walls,
hull band and hidden boxes, with furniture on their sockets. About 53 m long, 17 m across the sponsons, 15 m tall on its
legs, 188 parts, 211 t.

## The hull

A lofted hull of six facets (keel, four sides, spine) through stations from the chisel bow (z -3.5) to the stern (46.5). Neighbouring facets' ends are staggered (`FacetStagger`) so no two plates share a plane where they cross.
Each half profile is five points; `At(z)` blends them, and `SkinHalf(z, y)` gives the inner half width at a height, so
`MustFit` refuses a room that would poke through the skin. On the facets go bevelled armour slabs (`Plates`), chine rails
(`Chine`) and raked shoulder blocks (`Shoulder`). Voids (`Void`) are hidden boxes out to the skin, their ends kept 4 mm
apart from the next band's.

## The decks

Two decks run fore and aft (floors at 0.25 and 3.5 m, ship frame), each in a fore part and an aft part split at z 25:

| Part | What is in it |
| --- | --- |
| Zero Day Lower Deck Fore | the server room (Server Racks), the armoury, the fore end of the torpedo room with a bay door each side, the fire buttons; the ladder up to ops |
| Zero Day Lower Deck Aft | the rest of the torpedo room, the airlock over the boarding shaft (starboard), the suit bay (port), engineering (reactor, board, batteries) and the ladder up to the mess |
| Zero Day Upper Deck Fore | the sealed bridge (no glass: a view screen over the dashboard, camera screens each side; two Flight Couches, the flight computer), ops (the forward gun station, a plot table, the flares button), cabin A and the captain's cabin |
| Zero Day Upper Deck Aft | cabin B, the head, the med bay, the mess with the aft gun station |

Around them: the Keel (14,000 l of fuel, legs, lift jets, the boarding shaft), the Bow (armoured over the bridge, retro jets), the Spine
(roof, the railgun's trough, a dorsal block, mast, antennae, flare launchers), the Stern (9,000 l, fins) and the Drive
Array (four bells in one collar the width of the stern, 9 MN).

## Weapons and countermeasures

- Two torpedo sponsons (Zero Day Torpedo Bay L/R), each a room off the torpedo room through a bulkhead door, with a
  six-round drum Ordnance Rack loaded with designed torpedoes (radar seeker, HE warhead) that leave through the launch
  port in the sponson's front face. The fire buttons are on the torpedo room's walls.
- The Zero Day Railgun along the spine, fixed forward and aimed by the ship (the pilot fires it).
- Four Zero Day PDCs on barbettes on the beam, run from the two gun stations (ops and the mess).
- Four Zero Day Countermeasure Launchers (flare guns) on the spine, all fired by the flares button in ops.

## Getting aboard

The boarding ladder climbs from the ground up the belly shaft into the airlock; its inner door is aft of the shaft so
both landings are clear. `--check-building "Zero Day Frigate"` passes (327 m2 a standing person can reach and leave,
from the ground up the boarding ladder and the two ladders between the decks). The legs leave 2.5 m under the keel so
people can walk beneath it to the shaft.

## Flames

A thruster with no `"flame"` gets one from its force (`ConstructParts`: radius `sqrt(F) * 0.002 + 0.05`), which at frigate forces is far wider than
the nozzle: 12 m across from the middle of the drive. So each Zero Day thruster names its own: one per drive bell (1.15 m, 9 m long) and one
from each jet's mouth (0.4 m times its scale).

## The crew (an enemy that flies and fights by itself)

Karl's roadmap review (2026-11-15), step 5: one enemy ship that flies and fights by itself. Any craft with a Flight Stabilizer
can have one: the stabilizer's **AI crew** setting (Crew group: Off, Fights strangers, Fights everyone). The blueprint
**Zero Day Frigate (crewed)** is the Zero Day with it set to Fights everyone, so from the parts menu it is an enemy at once,
its spawner included. Code: `Source/Constructs/ConstructShipAi.cs` (the crew), `Source/Game/GameSandboxShipAi.cs` (which
bodies have one, who each is after; server only).

- **It works the ship's own seats**, as players would, so nothing new goes over the wire and a ship needs no special parts.
  The pilot's seat (the stabilizer's) flies it: the crew turns the stabilizer's Mouse aim on and looks at the target, so the
  ship turns to face it; Shift and Ctrl hold it a few metres over the target (never under 35 m over the ground or into a hill
  ahead); W and S hold a closing speed that falls to nothing at 220 m, inside its guns' 300 m; in that band Q and E slide it
  one way and then the other every few seconds. The gunners' seats look where their rounds meet the target (led for its
  speed) and fire when a gun bears within 4 degrees (2.5 for a fixed gun such as the railgun) and nothing of its own hull is
  in the way. The Buttons wired to Ordnance Racks launch one torpedo every 8 s, taking turns, from 350 m to 2 km with the
  target within 15 degrees of the nose, at a ship or a car only (a seeker wants something that moves); the Button wired to
  the flare launchers goes when a missile steers for it inside a kilometre.
- **Targets.** The nearest living player within 2.5 km: the body they sit or ride in, else themselves. Fights strangers
  leaves out the ship owner's faction (a nobody's ship fights everyone). Anyone aboard it is not a target.
- **With nothing to fight** it holds where it is, and after half a minute comes down where it is.
- **Boarding.** A player in the pilot's seat has the helm: the crew stands down and Mouse aim goes back as it was. A player
  in a gunner's seat has that seat. Setting it Off stands the crew down for good.
- **Not yet:** saving a crew's state (it is the setting, so a loaded ship has its crew again), flying in space past the sky's
  edge (a ship there is a SpaceFlight particle; the crew stops), the radar lock (PR #211: racks launch on what their
  seekers see), defending anything, fleets or factions.
- **The check:** `--ship-ai-check [SHIP] [--target NAME] [--range M] [--seconds S] [--still]` (`Source/VehicleKit/ShipAiCheck.cs`)
  flies a crew against a target ship on the flight check's flat floor and prints a line every two seconds. On 2026-11-15 the
  Zero Day against a second Zero Day 1200 m off (sliding about at 40 m): it lifted off, turned onto it in 6 s, launched its
  first torpedo at 1180 m, closed at 23 m/s (its stabilizer's top speed) to 236 m, fired 1204 rounds until its ammunition was
  gone and took the target to 98% of its health.
