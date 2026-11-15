# The Magpie mining ship (the first Survival ship to build)

Karl (2026-11-15, Design Roadmap Review): the first big Survival goal is a mining ship, paired with a small refinery
station or a refinery on the ground, not a refinery ship. This is the ship only: its build cost and the refinery are the
production system's (Twitch), and nothing here changes machines, recipes or ship building. Karl's steer: build it the
way the Zero Day frigate is built, not the Halberd or the rust bucket. That means one lofted hull, decks running the
length, a dozen big parts each drawing its own band of the hull, interesting geometry (facets, a layered hull, the
working parts on the outside) and a lived-in inside.

Generator: `Source/PartKit/Generators/MinerParts.cs` (`--gen-parts miner_parts`), which writes
`Constructs/Parts/Magpie/*.json` and `Constructs/Blueprints/MagpieMiningShip.json`. Change the generator, never its
output.

## The shape

A blunt, faceted hull about 35 m long, 17 m across the nacelles and 11 m tall, in mining yellow over worn grey, about
132 t. The hull is lofted through stations (a five-point half profile blended along z). Every big part draws its own
band of skin over the rooms it holds, and `MustFit` throws if a room pokes through. Along the outside:

- **Ore hoppers** (L and R): faceted sponsons along the drill bay's flanks. They have ribs, a sight glass, a top hatch,
  a discharge spout, and a lift jet on a shelf.
- **Engine nacelles** (L and R): fuel pods on raked pylons aft. Each has a main drive at its tail and a lift jet under it.
- **The spine**: an ore derrick (pedestal, a boom laid forward, a crutch and a ram), a ground-radar dish, radiators and
  antennae.
- **The bow**: a light bar, a chin ground scanner, and a retro jet in each cheek.
- **The stern**: a hood over the boarding porch, with a radiator grille.

## The decks

Two decks, both walkable (`--check-building "Magpie Mining Ship"`: 148 m2 a standing person can reach, every door
landed). From bow to stern:

| Deck | Part | What is in it |
| --- | --- | --- |
| lower, fore | Magpie Workshop Deck | bench, tool rack, lockers, desk, crates, shelving, the ladder up to the hall |
| lower, middle | Magpie Drill Bay | the drill well (railed), the gantry with four drills on rams, windows into both hoppers, the drill lever and its sign |
| lower, aft | Magpie Engine Deck | power plant, batteries, board, pipes, the aft ladder, the suit room and airlock, the outer door onto the porch |
| upper, fore | Magpie Bridge Deck | sealed bridge (screens, no glass), two flight couches, the flight computer, the hall, two cabins with crash couches |
| upper, aft | Magpie Mess Deck | head, sick bay, mess and galley, grow racks, the ladder down to engineering |

The keel holds the main fuel tank (6,000 l) and has the drill well cut through it. Boarding is by the ladder from the
ground up to the porch at the stern.

## Fittings

Karl (2026-11-15) said the stock stove, table and lockers looked like a house kitchen, "not on a space ship". The Magpie
now carries its own fittings, made in the same generator. Each keeps the size and sockets of the stock part it replaces,
is bolted down (a plinth, a deck flange or a wall rail) and is painted in the ship's colours:

- **Magpie Galley**: a sealed cook module with induction plates, a fiddle rail, a latched oven and a vented hood.
- **Magpie Brew Unit**: a pressure urn on a wall rail.
- **Magpie Mess Table**: a lipped top over lockers on one pedestal, with tie-down rails.
- **Magpie Locker**: a vented, latched kit locker with a helmet on top.
- **Magpie Fold Desk**: a wall-bracketed top with a screen on its backboard.
- **Magpie Bunk**: a framed bunk with restraint straps, a curtain rail and a reading lamp.
- **Magpie Med Bed**: a bed on a lift ram, with a monitor and a lamp arm.
- **Magpie Med Locker**: a sealed case with a green cross.
- **Magpie Cargo Rack**: netted shelves on wall uprights.

The rust bucket is getting generic "Ship Galley Range" and "Ship Mess Table" parts in ShipParts.cs (PR #218). Once those
merge, the Magpie can switch to them if a shared set suits it.

## Mining

- Four tier 1 **Drill Modules** (iron, stone, sand) hang on four **Piston 3x3 Long** rams under the gantry, in pairs
  across the well. A later tier fits the same socket (`MinerParts.DrillPart`).
- One lever in the drill bay drives every ram (2 m stroke, slow) and flips the sign between "DRILLS UP" and
  "DRILLS DOWN". Fully down, the bit tips sit about 10 cm above the ground the legs stand on, inside a drill's 25 cm
  reach.
- Drills 0 and 2 chute into the port hold and drills 1 and 3 into the starboard hold. Each hold is a **Storage Volume**
  sized to fill its hopper's inside, about 30 m3 (some 12,000 units of one bulk material).
- A refinery and the trip to it are the production system's.

## Flight

The pilot's Flight Couch is wired to the **Magpie Flight Computer**, a flight stabilizer with a 10 MN m gyro. It flies on
Rotorcraft keys, as the Halberd and the Zero Day do: Shift climbs, W leans forward, A and D turn.

- Four **Magpie Lift Jets** (1.2 MN each) stand round the centre of mass, under the hoppers and the nacelles.
- Two **Magpie Drives** (900 kN each) push forward from the nacelles' tails.
- Two **Magpie Retro Jets** (500 kN each) in the bow stop it over a claim.

Each thruster burns from the tank it hangs on: hoppers 1,500 l each, nacelles 2,500 l each, bow 1,200 l.

To check it, run `--flight-check Constructs/Blueprints/MagpieMiningShip.json`. This is the aircraft kit's flight check
on any blueprint file. It reports a climb of 9.3 m in 2 s, a hover drift of 0.01 m, a tilt of 0.1 degrees, and lift of
2.0x its weight.

## How it is welded

Following the Halberd, the 13 structure parts share the body's origin and are welded with `Connect`. Wherever two parts'
solid or hidden boxes touch, the generator adds a facing socket pair, and `PlaceOrder` lists them from the keel outward,
so a part that touches nothing throws. The blueprint places each part with `Put` at zero, and the game welds the rest.
`--check-vehicle` finds no coplanar faces between the parts; the "No z-fighting" section finds none inside them.

## Not yet

- Mining hasn't been tried in a world: no drill has been run against real ground from the ship.
- No build cost and no recipes: they are left to the production system.
- The holds have no way to unload except their chute ports. The hopper spouts are only looks.
