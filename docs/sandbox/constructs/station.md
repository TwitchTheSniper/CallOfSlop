# Freeport One, the first space station

Karl (2026-11-17) asked for the game's first space station; the layout was agreed on a floor-plan page first (project files
`station/station-plan.html`, the "Freeport One Floor Plan" artifact, version 3). Generator: `Source/PartKit/Generators/StationParts.cs`
(`--gen-parts station_parts`), parts in `Constructs/Parts/Freeport`, blueprint `Freeport One Station`
(`Constructs/Blueprints/FreeportOneStation.json`). The name is a working one.

## What it is

A harbour in orbit, about 290 m from the hub's bow to the reactor and 210 m across the piers. A 6 m walkway spine on a 7 m deep
truss runs north to south with everything hung off it, and each side takes one kind of traffic.

**Three tiers** (Karl, 2026-11-17, on the first model: more levels round a central spine, broken up). The spine steps 12 m at
each of two lift towers (sealed shafts with braced corners, lit lift glazing down both sides, a door onto each tier, a beacon
crown): the **works** low at the stern (u -12: engineering, the reactor boom, the south frigate finger and its fuel spheres), the
**harbour** in the middle (u 0: cargo pier, warehouse, the drum and roof pad, the north finger and its fuel), the **trade quarter**
high at the bow (u +12: marina, market, customs and the hub). The generator raises a piece onto its tier with `At(u, ...)`.

- **West, freight:** an open cargo pier 74 x 22 m off the warehouse's big door, under a gantry crane (legs every 11 m, a bridge
  with a trolley, hook block and cab). A Cargo Dock pad with a Docking Clamp Large sits where a Dray's belly well comes down;
  dolly rails run from it to painted container slots by the door.
- **East, frigates:** two 100 m fingers with a Docking Clamp Large each, fenders and green guide lights on the ship's edge, a
  gangway stub, a floodlight tower at the root and a **repair arm** near the end (Karl, 2026-11-17: repairs take time, and you
  see an arm mend the ship from outside). The arms are drawn at rest over the berth; making them work is later work.
- **North, small craft:** a marina of four short fingers with a Docking Clamp Medium each, beside the market hall (sawtooth roof,
  striped awning) and customs (a berth board facing the spine). The command hub stands at the bow: two hexagonal storeys, a
  lit sensor band, four gun domes and a lattice mast with the traffic radar.
- **Middle:** the habitat drum, a 52 x 20 m cylinder lying across the spine, with the roof pad on top (above the harbour
  tier: a Docking Clamp Large for a Magpie-sized ship, reached by a ladder tower).
- **Under the decks:** a fuel farm of six 14 m spheres hung under two catwalks between the fingers.
- **South:** engineering (stacks, gun domes) at the root of a 46 m boom to the reactor, with a radiator wing each side.

## Rules it follows

- **Sealed buildings** (Karl, 2026-11-17, "keep it simple"): every building is a floor, four walls and a roof, sealed, with
  doors drawn on its walls and nothing inside. The station's services come from a docking screen (dock from the command
  view, a tabbed screen once docked, Leave lets the clamp go), which is not built yet.
- **Welded pieces** as the Dray and the Zero Day: 57 pieces placed at one origin, `Connect` adds a socket pair wherever two
  pieces' solid boxes touch, `PlaceOrder` places them out from Spine 4. Round looks are drawn only (`"collider": "boxes"`);
  each piece has solid or hidden boxes for collision.
- **Blueprint-only:** section `Freeport station` in group `Ship`, and `Freeport` in Categories.json's name rule, keep the pieces
  out of the Parts tab. No Assembling recipes (`Assembling.SkipFolders`): the game places the station.
- Neighbouring deck slabs' edge bands stand out by different amounts (`_slabs`), so their overlapping ends never share a plane.

## Not done yet

NPCs and their routes, NPC ships docking and the visible unloading (only when a player is near; otherwise a timer), the docking
screen, working repair arms, real guns in the domes, gravity on the decks, lamps as parts (the lights are glow looks), and a place
in a star system.
