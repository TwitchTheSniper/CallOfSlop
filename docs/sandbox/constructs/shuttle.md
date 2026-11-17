# The Shuttle (the Zero Day's boarding craft)

Karl, 2026-11-17: the Zero Day carries a boarding shuttle; of four looks he picked a hull-cutting pod (then called the
Limpet), and on seeing it said it reads as a shuttle: renamed the Shuttle, kept, and a simpler 8-person boarding pod (the new
Limpet, flown from the ship's flight controller, cutting from the front) is designed apart. An 11 m faceted
wedge for a pilot and four boarders rides a Docking Clamp cradle on the frigate's spine. Its docking hatch under the belly
and the Zero Day's on the cradle open together while the clamp holds it. It can set down on a ship's hull and grip it with
four grapples; the boarders open the hatch by hand and go down (the boarding itself, Twitch's crews that hold, surrender,
fight or scuttle: Game/GameCrewBoard.cs). Karl's changes the same evening: two drives on the stern, each with a small
cylinder engine (Drive Core XS) in the cabin, which now runs to the stern so they can be worked on; the lockers moved to the
side walls; the belly's breaching cutter gave way to a Docking Hatch (on the Zero Day too). The Breaching Cutter part is
still made, for any craft that wants one.

Generator: `Source/PartKit/Generators/TorpedoFrigateShuttle.cs` (a partial of `TorpedoFrigateParts`, so
`--gen-parts torpedo_frigate_parts` writes the pod with the frigate). Pieces in `Constructs/Parts/Shuttle`, the two boarding
fittings in `Constructs/Parts/Boarding`, the blueprint `Constructs/Blueprints/Shuttle.json`.

## The pod

A lofted hull like the frigate's (five stations from the nose at z -5 to the stern at 5, x across, y up from the grapple soles),
in welded pieces: Shuttle Belly (the floor with the hatch opening, the keel, skids, the four grapples' mounts, the docking
hatch's rim), Shuttle Hull Port and Starboard (beam facets and walls, 600 l of fuel each, the lift sponsons with their jets and
RCS), Shuttle Roof, Shuttle Nose (sensor blister, a view screen in the front bulkhead), Shuttle Stern (a 0.3 m mount plate, the
cabin's end wall: `Works.Mount`), Shuttle Bulkhead (open frame between the cockpit and the cabin).

Inside: the pilot's Flight Couch in the cockpit with the flight computer ahead of it; the cabin behind with two seats a side
facing in, a locker against each side wall ahead of them, four Batteries between the seats and the engines, and at the back
the two engines: a Drive Core XS each (0.6 m across, 1.1 m long, 300 kN; ShipWorksParts' fourth Core size) bolted to the
stern's plate on its drive's axis, standing on the floor with a 0.8 m aisle between them. The two Shuttle Drives (60 kN each)
ask for a core although the table's 500 kN would let them go without (Karl wanted the engines), so a shot-out engine leaves its
drive at a quarter. The hatch opening is in the middle of the cabin floor with hazard lips and rails round it. It weighs about
12.2 t and flies on its own (`--flight-check Constructs/Blueprints/Shuttle.json`: lift 2.0x).

Switches on the port wall: **B** the grapples (A is spare); the starboard switches are the lamps.

## Grapples and the cutter (ConstructBreach.cs; the Shuttle no longer carries a cutter)

Two new kinds of lamp logic part, by their `"breach"` block (only lamp logic parts may have one):

| Part | Block | What it does |
| --- | --- | --- |
| Hull Grapple | `{"kind": "grapple", "reach": 0.6}` | While its input is on and a body's hull is within its reach under its face (its -y), moving under 3 m/s against it, the craft grips that body: it is carried as a docking clamp carries a craft (`CarriedBy`, no volume, its pose in the frame of the gripped part's segment), so it moves with the hull and nothing new goes over the wire. Every grapple off (or the last one broken) lets go. |
| Breaching Cutter | `{"kind": "cutter", "reach": 2.0, "rate": 900}` | While its craft is gripped and its input is on, it burns the nearest part of the gripped body under its ring (five rays), `rate` damage a second, raw (armour doesn't stop it), one part after another until nothing is left there: then "the cutter is through", once. |

Server only, ten times a second, allocating nothing. A gripped craft lets go when no grapple that is on has the hull within its
reach any more (the hull under it shot away or broken off). Its mass joins whatever it grips, a wreck too. A ship in space flight (`SpaceShip != 0`) and a body already carried
can't be gripped. Damage is billed to the pod's driver (or owner).

## On the Zero Day

The dorsal block, step and mast aft of the railgun gave way to a cradle deck (y 7.75, z 34 to 45.2) with raked flank plates and
hazard edges; the new mast stands aft of it to starboard. A Docking Clamp Medium under the deck (`ShuttleClampConfig`: starts
holding, its box round the pod) holds the pod, which the blueprint's new `"carry"` list builds there
(`ConstructCargo.BuildCarry`: `{"blueprint", "at", "turn"}` in the carrier's frame; a carried blueprint carries nothing of its
own; a Building Station's build carries none). The pod's hatch sits over a shaft through the spine into the mess ceiling, with
the Zero Day's own Docking Hatch on the cradle over it, and the Zero Day Shuttle Ladder climbs from the mess up into the pod
(both hatches open while the clamp holds it: big-systems.md, "Event doors"; tried headless: open within a second of the
spawn, both shut when the clamp let go). The pilot's key lets go of the clamp, as for any docked craft.

## Not done yet

- A grip isn't kept through a save and load.
- No ladder from the target back up into the pod, and none from the ground into a landed pod.
- No air: the hole doesn't vent the room below.
- Gripping a hull, the hatch opens only by hand, and opening it on a hull vents nothing (no air model).
- `--check-building` walks the carrier alone, so it says the Zero Day Shuttle Ladder has no room at its top; checked by hand
  (rays in a built world): the foot stands under the open shaft in the mess, the top lands on the pod's floor aft of the hatch
  with 1.9 m of head room.
- The Zero Day's lift jets went from 1.5 to 1.7 MN for the pod's 11 t (at 1.5 the hover hold overshot the flight check's metre).
- Tried headless only (`ConstructBreach` on the Zero Day itself: grip, then the cutter took its railgun and a spine piece and
  was through in about 25 s); not yet flown and docked by a player.
