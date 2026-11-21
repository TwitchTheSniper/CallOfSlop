# The Dibber lander

The farmers' vertical lander (Karl, 2026-11-21: a picture of a squat tapered lander on four jointed legs, "Can we add this lander to
the space faction?"; he picked the farmers on the card). An original small craft that drops straight down from a ship in orbit to a
field and climbs back, about 8.6 m across the pads and 5.6 m tall, far under the Zero Day. Generator
`Source/PartKit/Generators/DibberParts.cs` (`--gen-parts dibber_parts`); blueprint `Constructs/Blueprints/DibberLander.json`
("Dibber Lander"); parts in `Constructs/Parts/Dibber` (unlisted, Categories.json's Ship name rule).

## Shape and look

- An eight-sided capsule on end: the skin's apothem widens from 2.3 m at the belly to 2.75 m at the shoulder (0.6 m up), tapers to
  2.05 m at the roof's edge (5.2 m) and caps over to the roof. Facet k faces 45k degrees from +x towards +z (0 starboard, 2 aft,
  4 port, 6 fore); forward is -z.
- The farmers' dress, lighter than the Haywain's: cream pressed-steel panels, an orange band round the upper hull, graphite panels
  on the diagonal faces, a dark chassis, hazard yellow on the feet, a glass cab low on the nose, an antenna with a red tip.
- Four fixed legs at the diagonals: hip on the belly's chamfer, knee out and up, a strut down to a broad pad; the pads' soles are
  1.5 m under the belly (`Ground`).

## Inside

Two decks in a separate room shell (an upright octagon inside the tapering skin: apothem 2.2 m below, 1.75 m above).

- **Lower deck** (floor `Ft` 0.3 m): two Flight Couches at x +-0.5 facing the cab, the flight computer between them ahead, two suit
  lockers on the port wall, three lamps and a lights switch (starboard wall). The belly hatch is aft of centre (x +-0.55, z 1.25 to
  2.1): it slides 1.5 m down a shaft through the belly to the ground, with the boarding ladder standing on it (hung turned half round
  at its fore edge, so its top lands forward on the deck). A switch on the aft wall works it (starts shut).
- **Upper deck** (floor 3.1 m): reached by the Dibber Deck Ladder at x 1.02 through a hole at the starboard side (turned RY270, so
  its top lands inboard). The 150 kW Dibber Power Plant, two batteries, a Life Support Unit (port wall) and an Oxygen Tank (aft
  wall), two lamps and a switch; the rest is floor for crates.
- Passes `--check-building "Dibber Lander"` (both decks reached) and `--ladder-check "Dibber Lander"` (both ladders up and down).

## Flying it

- One Dibber Belly Jet (440 kN) under the middle of the belly and a Dibber Trim Jet (80 kN) under each leg's thigh: the flight
  computer tips the craft on the trims. Lift 2.6x its weight (about 16 t). RCS pads on the four lower flat faces (fore pad over the
  cab, the others level with the centre of mass), three jets each, and four RCS up the roof.
- Every piece that carries a jet is a fuel tank (a jet burns from the tank it is on): 1800 l in the belly, 75 l in each lower
  quarter, leg and the roof.
- Ship keys, mouse aim off (a lander flies upright over its belly jet; with aim on, it nosed down and never came back level in the
  flight check's look test).
- The flight computer's gyro is light (2 MN m): `ConstructFlight.Solve` drops any turn row under a twentieth of the strongest, and a
  gyro over 20x the jets' pitch and roll levers (170 kN m) switched those off, so it leaned 8 degrees for good in the hover.
- `--flight-check Constructs/Blueprints/DibberLander.json`: flies, hover tilt 0, forward 14 m.

## Not done

- No Assembling recipes for its pieces yet (to add once Karl is happy with the look, as the Satchel's were).
- Not in any start or faction fleet yet.
