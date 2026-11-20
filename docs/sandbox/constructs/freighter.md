# The Packet spine freighter

The Traders' freighter (Karl, 2026-11-20: "a thin central spine ... realistic cargo holds ... nacelles ... unique to traders and as
detailed as the Rust Bucket. Small glass cockpit at the front"). A medium ship, the Zero Day's class: about 60 m long and 15 m across
the nacelles. Generator: `Source/PartKit/Generators/FreighterParts.cs` (`--gen-parts freighter_parts`); blueprint
`Constructs/Blueprints/PacketSpineFreighter.json` ("Packet Spine Freighter"); parts in `Constructs/Parts/Packet` (unlisted, like
every ship-named piece). Built the way the Dray and the Zero Day are: a hull lofted through stations, rooms inside it, each big part
dealt into welded pieces by `SplitDeck` and `SplitBands`.

## Layout (bow is -z, z 0 near the spine's middle so the cargo stands within 30 m of the origin)

- **Cab** (z -27.5 to -11.5): a small glass cockpit at the nose (two Flight Couches, the Packet Flight Computer, a dash), then a
  corridor with the bunk room, the head and the trade office (a Computer, two Server Racks, a Filing Cabinet) to port, and the
  galley, the airlock over the boarding shaft (belly hatch, ladder, MMU Dock, two suit lockers, life support) and the stores to
  starboard.
- **Spine** (z -11.5 to 17.75): a 1.6 m corridor inside the hull itself. Karl (2026-11-20) said the spine needs little gap between
  hull and wall, or can be just hull, so it has no wall runs: the hull's lining is the corridor's wall, one length to each station
  band, and the hull close round it. Three cargo racks hang off each flank on hardpoints through the hull.
- **Engineering** (z 17.5 to 26.5): power plant, Power Board, batteries, Engineer's Panel, a suit locker and a Pressure Locker
  for the walk out to the nacelles; a fuel manifold up each wall to the pylons.
- **Nacelles**: a fuel tank forward of an engine housing (`EngineHousing.Draw`) on a swept pylon off engineering's flank. Each has a
  Drive Nozzle M outside its tail plate, a Drive Core M and Fuel Pump M inside, a service hatch outboard, and RCS on its nose, top
  and bottom.
- A blunt glass nose sealed by a slightly rounded Nose Cap (Karl, 2026-11-20).
- Lift jets and small (S) leg pods under the cab and engineering (Karl asked for smaller legs), attitude jets at both ends. The racks clear the ground by about 0.9 m.

## Cargo

Each rack takes two standard containers stacked, lying along the ship: 2.5 x 2.5 x 6.5 m, three 2x2x2 m parts in a row. Karl set
that size on 2026-11-20; the container itself belongs to the "Design the standard cargo container" work ("Standard Container 2m").
Each container's origin is the middle of its roof, and its hatch is the whole +x side, hinged at the top, opening to an awning. The
racks are open outboard so a container slides out to a shell or a crane, and the port containers are turned half round so their
hatches face out. Allow about 2.5 m clear outboard for an open hatch. A frozen Cargo Volume on each rack holds its two containers.
Until the standard container's part lands, the blueprint carries a stand-in of the same size, the `Packet Freight Box`. Swap
`BoxName` and the cargo list in `Blueprint()` for the real part once it exists.

## Checks (2026-11-20)

- `--check-vehicle`: 0 coplanar faces between parts. The wheel and drive failures are a ground vehicle's and don't apply.
- `--selftest-only "No z-fighting, Assembling, Compound collision, Mods"`: all pass.
- `--ladder-check`: the boarding ladder climbs up and down.
- `--flight-check`: it lifts at 2.2x its weight and holds a hover. The one miss is the Ship keys pitch test: the nose goes 53
  degrees down in 6 s where the test wants 55. The shipped Dray gets 40, and a stronger gyro made it unstable.
- `--check-building` finds 0 m2 to stand on for every ship tried (the Dray and the Zero Day too), so it can't judge ships yet.
