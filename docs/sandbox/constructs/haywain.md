# The Haywain hauler

The farmers' medium hauler (Karl, 2026-11-21): "a new ship for the farmers faction ... 90s and 80s combine harvester vibe, blocky with
the ships having a cockpit as a room attached to the ship, slightly protruding", and "not so much farm equipment in space as it is the
aesthetic, colour choices, textures, cladding". Rooms inside a separate lofted hull, as the Satchel courier and the Packet freighter
are built (their helpers copied). Generator `Source/PartKit/Generators/HaywainParts.cs` (`--gen-parts haywain_parts`); blueprint
`Constructs/Blueprints/HaywainHauler.json` ("Haywain Hauler"); parts in `Constructs/Parts/Haywain` (unlisted, Categories.json's Ship
name rule). Mood board: https://claude.ai/artifact/PzLdq4PWBjtjkZ4HGcyDgm.

## Layout (bow is -z, z 0 the bay's middle)

- **Cab room** (z -23.6 to -20.05): a glass box half the body's width (4.4 m), standing taller than the body and ahead of it, on the
  shoulder where the hull steps out. One big windscreen, a roof visor with work lamps, an amber beacon, mirrors on arms, a cream sill
  panel with headlamps and a bumper; a tread-plate walkway with a rail each side of it on the shoulder's foot and a ladder down.
  Inside: two Flight Couches, the flight computer, a dash, a ceiling of switches.
- **Crew block** (to z -11.95): the Satchel's cab rooms moved aft by `D2`: bunk room and head to port, galley and airlock over the
  boarding shaft (belly hatch, ladder, two suit lockers, life support) to starboard; a cross passage behind.
- **Bay** (z -10.45 to 10.45): six "Standard Container 2m", two across and three rows along (`Rows`, `RowZ`), on twist locks
  `lock{L|R}{row}{0..3}`, hung by `foot.0` on `lock{n}{row}0`. Open to the sky inside a raised **tank coaming** (four plate walls
  leaning out, `CoamH`, `CoamOut`) like a grain tank's sides, with floodlights at its corners. A passage down each side.
- **Engine box** (z 10.45 to 17.3): steps down 0.3 m from the bay; louvred grilles down its flanks, two exhaust stacks and an air
  intake on its roof, a radiator grille and a ladder on the transom. Power plant, batteries, Power Board, Engineer's Panel, MMU dock,
  suit and Pressure Lockers.
- **Drive pods**: a Drive Nozzle M pod (Drive Core M, Fuel Pump M inside) slung low on a stub pylon each side of the engine box.
- Six lift jets (1.3 MN), four S leg pods, attitude jets at both ends and on the pods.

## The look

Barn red ribbed cladding (`Plates`: ribbed panels, access doors with hinges and a latch, louvred grilles, stencilled panels, bare
galvanised panels), a dark chassis below the beam and on the keel, a cream stripe band with a dark pinstripe along the upper sides
(`Livery`), cream cab roof and trim, amber hazard ends and beacons. Inside: cream walls over an olive dado.

## Checks (2026-11-21)

- `--check-vehicle`: builds, 225 parts, about 136 t, 0 coplanar faces between parts; the wheel and drive lines are ground-vehicle checks.
- `--selftest-only "No z-fighting, Compound collision, Assembling, Mods"`: pass.
- `--flight-check`: flies, lift 3.6x its weight, hover tilt 0.
- `--ladder-check "Haywain Hauler"`: the boarding ladder climbs up and down.
- Not built: recipes, a farmers faction or routes, a dock crane for the containers.
