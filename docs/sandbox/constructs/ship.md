# The rust bucket (a walkable ship)

Karl (2026-11-14): "a start in a space ship. You have a rust bucket, it has a full interior including small bedroom,
bathroom, hydroponics, engine room, cockpit, storage room." The ship of the rust bucket start (docs/story/STARTS.md "A ship
start"). Generator: `Source/PartKit/Generators/ShipParts.cs` (`--gen-parts ship_parts`), which writes
`Constructs/Parts/Ship/*.json` and `Constructs/Blueprints/RustBucket.json`. Change the generator, never its output.

## The hull

About 10 by 17.5 m, two decks, a faceted hull: a wedge nose whose canopy glass rakes back over the cockpit, the chin under
it the cargo ramp (an event door that swings down to the ground), sides that flare out below the lower deck line and lean
in above the upper one, cut corners at the stern, a radiator spine on the roof. Karl's steer: less boxy, corners, a nose
that makes sense; the chonky engine pods stay.

It is three big parts, one a storey (Keel, Deck, Roof), each holding its floor, its partitions, its share of the hull's
facets (`"shape": "hull"`, drawn only, any angle) and the hidden boxes that stand for them, so the whole ship is a few
merged meshes and about 70 steps. The generator works in ship metres (x from the port side, y from the lower floor's
underside, z from the bow back) and moves them by half a cell; every big part has its origin at the body's.

## The rooms

Upper deck (living): the cockpit across the bow (two Seats, a Chart Table, the dashboard under the glass), a corridor aft
with the bedroom and bathroom either side, then hydroponics (four Grow Racks), the stair hall and the galley. Lower deck
(working): the hold behind the ramp (shelving, a Tool Rack, a Locker, two Requisition Crates) and the engine room aft.
A Steel Wide Stair 3 m joins them. Furniture is the shipped parts on sockets the big parts carry where each goes; the new
fittings are the Ship Toilet, Ship Basin, Shower Stall, Grow Rack 2 m and Ship Engine Block.

`--check-building "Rust Bucket"` passes: up the lowered ramp, through every door, up the stair, every seat and store in
reach. The walk check opens event doors that swing about a level axis (the ramp: the only way in) before it walks.

## Flying

- The stern pods are fuel tanks (2,500 l each) carrying a **Rust Bucket Drive** on their tails (600 kN, pushes forward)
  and a **Rust Bucket Lift Jet** under each (800 kN, down). Two **saddle tanks** on the forward flanks (1,000 l each) carry
  two more lift jets, so the lifters stand round the centre of mass and the hover hold keeps it level.
- The pilot's Seat (port) drives through a Driving Module on the cockpit floor, wired as the Orbiter's. Shift climbs and
  holds, W flies forward, A and D turn. There is no reverse thrust: to slow, turn round and burn.
- In orbit it is a ship like any other (docs/space/SPACE.md); to come down, turn it round, burn until the orbit's low
  point is under the sky's edge, and the hover catches it on the way in.

## Not yet

- The kit stays aboard as the crates and racks; the start kit isn't placed (`"kit": []`).
- People standing in it count as not aboard for landing on another world (seated only), as SPACE.md says.
