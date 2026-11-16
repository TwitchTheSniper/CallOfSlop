# The rust bucket (a walkable ship)

Karl (2026-11-14): "a start in a space ship. You have a rust bucket, it has a full interior including small bedroom,
bathroom, hydroponics, engine room, cockpit, storage room." The ship of the rust bucket start (docs/story/STARTS.md "A ship
start"). Generator: `Source/PartKit/Generators/ShipParts.cs` (`--gen-parts ship_parts`), which writes
`Constructs/Parts/Ship/*.json` and `Constructs/Blueprints/RustBucket.json`. Change the generator, never its output.

## The hull

The second hull (Karl, 2026-11-15: the first, a 10 by 17 m lozenge, was a bad design; the Zero Day frigate is the model).
What the Zero Day does that the first hull didn't, kept scrappy here: a long lofted hull whose profile changes along its
length, a line along every edge, armour that varies panel by panel, and things on the outside that do a job.

- **Lofted.** Five points a side (belly edge, chine, beam at the deck line, upper chine, spine edge) through seven rings,
  each point with its own z so a facet can rake: a blunt bridge front over the cargo ramp, a hexagonal section
  amidships with a tall spine (7.3 m), and a tail behind the rooms that tucks in to the heat exchanger. About 21.5 m long,
  14 m across the radiator wings, 11.6 m to the mast's tip, 113 t.
- **Lines.** Chine rails along the chine, the beam (the heavy rub strake) and the upper chine and spine edges; three hoop
  frames (shoulder, midship, haunch) stand proud right round it.
- **Plates.** `Plates` deals each facet's panels (about 2.7 m) one of a rust bucket's repairs: a dark slab, a stepped
  double slab, a mismatched green or ochre patch with rust weeping from it, two half slabs at different heights, or bare
  tan paint with a seam, a rust streak and a primer scuff. The pattern changes band to band and side to side.
- **On the outside.** Radiator wings on the eaves (they glow along the edge), a battered half-height container strapped to
  the spine, a deck crane folded onto a crutch, a mast with a dish and a beacon, a grating walkway and a hatch on the spine,
  two raked fins on the tail, fuel lines on brackets from the saddle tanks aft to the pods, a sealed side hatch, searchlights
  on the nose's cheeks, running lights, tail lights. The stern pods have intake cowls, cooling rings and a fairing.
- **The bridge front** (Karl, 2026-11-15: the first chisel nose had to go). A blunt, raked face like a tug's: armour and a
  heavy bumper with hazard chevrons and two headlamps low down, a band of windows at eye height with mullions, and a dark visor
  jutting out over them from the brow.
- **Glass.** The bridge's window band (no mullion in it), windows in the nose's cheeks either side of it, a raked windscreen
  over most of the nose's top and a band of side windows either side of the cockpit, framed; the rest is plate. Karl
  (2026-11-16): the pilot looked out through a slot with a post in front of the crosshair, so the front band lost its
  mullions, the cheeks and the windscreen opened up, and the dashboard came down (0.7 m, the yokes short stubs off the
  seats' lines).

The colours: tan paint, dark plate and frames, green and ochre patches, hazard ochre and primer red as accents.

## Breaking up

A hit takes a panel, not a deck (the ship-design skill, 1b). Each storey is drawn whole (`Keel()`, `DeckPart()`, `Roof()`),
each box tagged with the piece it belongs to (`With(tag, ...)`; `"~hull"` lets `Where` place it by where it stands), then
`Split` deals them out: the **Keel**, **Deck** and **Roof** (floors, ceiling, the belly and roof voids, what stands in the
rooms); eight hull runs (**Lower/Upper Port/Stbd Fore/Aft**, either side of the midship frame), the **Lower/Upper Stern**,
the **Chin** (round the ramp), the **Canopy** (the nose, windscreen and side windows), the **Tail**; the three **Frames**
(the ribs and the walls' slices at them: 30,000 health, so the ship loses its skin before its bones); and eight
**Partitions**. `Connect` gives every two pieces whose solid boxes touch a facing socket pair; the game welds the rest.
Tried: shooting off the starboard fore hull runs dropped the lamps, switches, rack, locker, basin and toilet on them and
nothing else. `--check-vehicle` finds no coplanar faces between the parts.

The rooms are a box standing inside the hull (Karl, 2026-11-16: "the interior rooms don't have to cover the whole of the
external shell", as the Zero Day's don't). Aft of the cockpit they have their own painted walls with a kick strip, 3.75 m
from the middle downstairs (`Lw`) and 3.5 m up (`Uw`), and the floors stop under them (the upper one over the hold's
walls), so there is a gap of 1 to 1.8 m out to the skin. At each hoop frame the frame's rib crosses the gap; elsewhere
hidden voids fill it, so a round still stops at the skin. The cockpit keeps the whole nose (4.73 m, its canopy for walls),
and the hold's front is a straight bay as wide as the ramp lets it be (`BayHalf`; no floor or walls stepping in with the nose), with a flat plate each side at its aft end out to the hold's walls and the skin
(`BoxWalled`, `HoldWallFront`). The nose, the roof, the belly and the tail are filled with hidden voids. The pods and saddle tanks hang from small mounts on the keel just inside the skin.

## The rooms

The first hull's, in the narrower room box: the fittings on the side walls moved in with them (the galley's range aft by
the coffee station, the stair hall's opening inside the starboard wall, the Engineer's Panel and the Pipe Bank inboard), the
galley's wake point too. Upper deck (living): the cockpit
across the bow (two Worn Flight Couches: a camper's old captain's chairs with the springs coming through; a Chart Table, the dashboard under the windscreen), a corridor aft with the bedroom and bathroom
either side (its walls gloss white), then hydroponics (four Grow Racks), the stair hall and the galley (stove, table,
Medicine Cabinet, Galley Coffee Station); pipes run along the corridor under the ceiling. Lower deck (working): the hold
behind the ramp (shelving, a Tool Rack, a Locker, two Requisition Crates) and the engine room aft (the Ship Engine Block,
the Engineer's Panel, a Power Board, a Pipe Bank, a Battery). A Steel Wide Stair 3 m joins them. Walls are painted plate.

`--check-building "Rust Bucket"` passes: up the lowered ramp, through every door, up the stair, every seat and store in
reach.

## Flying

- The stern pods are fuel tanks (2,500 l each) carrying a **Rust Bucket Drive** on their tails (600 kN, pushes forward)
  and a **Rust Bucket Lift Jet** under each (800 kN, down). Two **saddle tanks** on the forward flanks (1,000 l each) carry
  two more lift jets, so the lifters stand round the centre of mass and the hover hold keeps it level.
- Every drive and lift jet is a **pod** (ship works, docs/sandbox/design/ship-works.md; Karl's answer 7: "the Rust Bucket
  pods are nacelles"): `works.service: "outside"`, its works in its own cowl, so it needs nothing inside the hull but is
  serviced only at the part, on a space walk or landed. The drives swing a 12 degree gimbal. Each pod has a grab rail along
  its top and a tread-plate step under its outboard flank to reach it by.
- **RCS Thrusters** (80 kN) stand on each tank's top, bottom and outboard face, and a **Heavy RCS Thruster** (250 kN)
  on each saddle's front, pushing back (`ShipParts.Rcs`, on pads by `RcsMount`).
- The pilot's Seat (port) is wired to a small Flight Stabilizer on the deck's module socket, on Ship keys with mouse aim
  (docs/sandbox/constructs/flight.md "Ship keys"): the nose follows the look, W/S forward and back, A/D slide, Q/E roll,
  Shift/Ctrl up and down; let go, it stops and holds. `--flight-check`: climbs, holds within 0.25 m, nose 65 degrees down
  on the look and back level.
- In orbit it is a ship like any other (docs/space/SPACE.md), and the stabilizer flies it there too.

## Breakdowns

In Survival every crewed player ship breaks down now and then (Source/Game/GameSandboxBreakdowns.cs; the numbers are
Constructs/Breakdowns.json; Karl, 2026-11-16). A ship is a body with a thruster over ShipWorks.json's `jetMost` (500 kN) or
a Flight Stabilizer, not a wreck, and with no AI crew fighting for it. Each has its own clock (on its `Construct`), which runs
only while somebody alive is aboard: the first breakdown about 4 crewed minutes after it is first crewed, then every 8 to
14. The server walks the players, not the bodies, so the cost is bounded by the players. A part picked at random creaks
and groans for 90 s, louder and closer together, and the people aboard that ship are told which one ("the aft starboard
lift jet", "the drive core"). It picks among a hull drive's works pieces (Drive Core, Fuel Pump, Coolant Exchanger: the
nozzle itself is not picked) and the thrusters with no works inside (pods and whole jets).

The attach tool's Fire at the part sees to it for nothing in that time. A works piece stands inside the hull, so the
Engineer's Panel in the engine room can see to its warning too. A pod or a jet hangs outside: the panel only reports it
(which part, where), and it is seen to at the part, on a space walk or landed (the rust bucket's drives and lift jets are
pods: ship works, docs/sandbox/design/ship-works.md 7). Left alone it fails with a bang and stops working (Broken, its
health kept: that is how a breakdown is told from a part shot to pieces, in a save too) until the attach tool repairs it
at the part for 3 metal. No new one starts on a ship while two of its parts are broken down. A works piece shot to pieces
is mended where it stands too, paid by its mass (ShipWorks.json's `mendPerTonne`). The panel's name is read by the code
(BreakdownPanel): keep one in the engine room, reachable standing. The clocks aren't saved: a loaded ship starts its wait
again.

## Not yet

- The kit stays aboard as the crates and racks; the start kit isn't placed (`"kit": []`).
- People standing in it count as not aboard for landing on another world (seated only), as SPACE.md says.
- The deck cargo and the crane are looks only: the container holds nothing and the crane doesn't lift.
