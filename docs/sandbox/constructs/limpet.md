# The Limpet boarding pod (the Zero Day's boarding craft)

Karl, 2026-11-17: the Zero Day carries a boarding shuttle; of four looks he picked the Limpet hull-cutter. An 11 m faceted
wedge for a pilot and four boarders rides a Docking Clamp cradle on the frigate's spine, flies to an enemy ship, sets down
on its hull, grips it, burns a hole through it with a ring cutter in its belly, and the boarders drop through into the room
below, where the boarding itself (Twitch's crews that hold, surrender, fight or scuttle: Game/GameCrewBoard.cs) starts.

Generator: `Source/PartKit/Generators/TorpedoFrigateLimpet.cs` (a partial of `TorpedoFrigateParts`, so
`--gen-parts torpedo_frigate_parts` writes the pod with the frigate). Pieces in `Constructs/Parts/Limpet`, the two boarding
fittings in `Constructs/Parts/Boarding`, the blueprint `Constructs/Blueprints/LimpetBoardingPod.json`.

## The pod

A lofted hull like the frigate's (five stations from the nose at z -5 to the stern at 5, x across, y up from the grapple soles),
in welded pieces: Limpet Belly (the floor with the hatch, the keel, skids, the four grapples' mounts, the cutter's socket),
Limpet Hull Port and Starboard (beam facets and walls, 450 l of fuel each, the lift sponsons with their jets and RCS), Limpet
Roof, Limpet Nose (sensor blister, a view screen in the front bulkhead), Limpet Stern (300 l, the drive), Limpet Bulkhead
(open frame between the cockpit and the cabin).

Inside: the pilot's Flight Couch in the cockpit with the flight computer ahead of it; the cabin behind with two seats a side
facing in, lockers, four Batteries; the hatch in the middle of the cabin floor with hazard lips and rails round it. It weighs
about 11.3 t and flies on its own (`--flight-check Constructs/Blueprints/LimpetBoardingPod.json`: lift about 2.2x).

Switches on the port wall: **A** runs the cutter, **B** the grapples; the starboard switches are the lamps.

## Grapples and the cutter (ConstructBreach.cs)

Two new kinds of lamp logic part, by their `"breach"` block (only lamp logic parts may have one):

| Part | Block | What it does |
| --- | --- | --- |
| Hull Grapple | `{"kind": "grapple", "reach": 0.6}` | While its input is on and a body's hull is within its reach under its face (its -y), moving under 3 m/s against it, the craft grips that body: it is carried as a docking clamp carries a craft (`CarriedBy`, no volume, its pose in the frame of the gripped part's segment), so it moves with the hull and nothing new goes over the wire. Every grapple off (or the last one broken) lets go. |
| Breaching Cutter | `{"kind": "cutter", "reach": 1.2, "rate": 900}` | While its craft is gripped and its input is on, it burns the nearest part of the gripped body under its ring (five rays), `rate` damage a second, raw (armour doesn't stop it), one part after another until nothing is left there: then "the cutter is through", once. |

Server only, ten times a second, allocating nothing. A ship in space flight (`SpaceShip != 0`) and a body already carried
can't be gripped. Damage is billed to the pod's driver (or owner).

## On the Zero Day

The dorsal block, step and mast aft of the railgun gave way to a cradle deck (y 7.75, z 34 to 45.2) with raked flank plates and
hazard edges; the new mast stands aft of it to starboard. A Docking Clamp Medium under the deck (`LimpetClampConfig`: starts
holding, its box round the pod) holds the pod, which the blueprint's new `"carry"` list builds there
(`ConstructCargo.BuildCarry`: `{"blueprint", "at", "turn"}` in the carrier's frame; a carried blueprint carries nothing of its
own; a Building Station's build carries none). The pod's hatch sits over a shaft through the spine into the mess ceiling, and
the Zero Day Limpet Ladder climbs from the mess up into the pod. The pilot's key lets go of the clamp, as for any docked craft.

## Not done yet

- A grip isn't kept through a save and load.
- No ladder from the target back up into the pod, and none from the ground into a landed pod.
- No air: the hole doesn't vent the room below.
- `--check-building` walks the carrier alone, so it says the Zero Day Limpet Ladder has no room at its top; checked by hand
  (rays in a built world): the foot stands under the open shaft in the mess, the top lands on the pod's floor aft of the hatch
  with 1.9 m of head room.
- The Zero Day's lift jets went from 1.5 to 1.7 MN for the pod's 11 t (at 1.5 the hover hold overshot the flight check's metre).
- Tried headless only (`ConstructBreach` on the Zero Day itself: grip, then the cutter took its railgun and a spine piece and
  was through in about 25 s); not yet flown and docked by a player.
