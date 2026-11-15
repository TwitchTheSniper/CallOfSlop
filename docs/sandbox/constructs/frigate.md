# The Halberd frigate (a tail-sitter combat ship)

Karl (2026-11-15): a second player ship, a mid-size combat frigate with point-defence turrets, a med bay, a robot area, a
layered hull and a lived-in interior; "make new parts too, go wild", and none of the rust bucket's bathroom tiles or
clipping. Generator: `Source/PartKit/Generators/FrigateParts.cs` (`--gen-parts frigate_parts`), which writes
`Constructs/Parts/Frigate/*.json` and `Constructs/Blueprints/HalberdFrigate.json`. Change the generator, never its output.
Built the way the rust bucket is (ship.md): big parts that share the body's origin, each holding a storey's floor, walls,
hull band and the hidden boxes for them, furniture on their sockets.

## The stack

It stands on its tail like a rocket: an octagonal tower about 9.5 by 8.5 m and 28 m tall, decks 3.25 m apart, one ladder
spine up through them (the ladder swaps side each storey, the hatch it climbs to is railed). Bottom to top:

| Storey | Part | What is in it |
| --- | --- | --- |
| -1 | Halberd Thrust Frame | the fuel tankage (9,000 l), the Main Drive's ring, four landing legs, four manoeuvre jets |
| 0 | Halberd Engineering Deck | the reactor, power board and batteries, pipe bank, workshop rack, suit lockers; the airlock (two bulkhead doors), its boarding platform and the 6 m boarding ladder to the ground |
| 1 | Halberd Robot Bay Deck | three Robot Charging Cradles (Robot Shells on them), two Shell Control Chairs, the armoury behind a bulkhead door, the gun station for the port and starboard PDCs |
| 2 | Halberd Med Deck | two med couches (they heal), a decon shower and scanner arms; aft, the galley: stove, Galley Coffee Station, locker, mess table |
| 3 | Halberd Crew Deck | four Crash Couches (respawn bunks), the head behind a swing door (toilet, basin), a desk, lockers |
| 4 | Halberd Ops Deck | the gun station for the bow and stern PDCs, a plot table, the ship's locker; the armoured shoulder steps in above it |
| 5 | Halberd Cockpit Deck | two Flight Couches on motor hinges before a wall of outside-view screens (no window), the dashboard, the Halberd Flight Computer, a chart |
| 6 | Halberd Nose | the armoured cap, the railgun's shroud, sensor mast, dish, antennae, running lights |

The walls are painted panelling (pale over a dark dado), the med bay gloss white and green: no ceramic tiles anywhere.
Labelled pipes run up the corners. `--check-building "Halberd Frigate"` passes (141 m2 a standing person can reach,
from the ground up the boarding ladder).

## The hull

Each storey's band is pale skin plates (trimmed a few mm at alternate edges so neighbours never share a plane), a dark
frame rib up every corner and a girder at the deck line, and over that bevelled slate armour slabs: one half of each flat
face armoured in two steps, the other half bare skin with its panel seams, the side swapping face to face and storey to
storey, raked slabs on the chamfered corners with primer-red scuffs, hazard flashes. Four armoured PDC housings stand out of the faces. The
shoulder over the ops deck steps the tower in from 8 m to 5.5 m wide, so the silhouette reads as a hull with a head.

## Flight

The Main Drive (4.5 MN) pushes up the spine; four Halberd Manoeuvre Jets (500 kN each) under the thrust frame's overhang
hold it upright and lean it, since one engine on the spine can't and the flight computer's gyro turns it only about its
own up. About 125 t: the Flight Computer (a flight stabilizer with a 3 MN m gyro) flies it on Rotorcraft keys (Shift
climbs, W leans it forward, A and D turn it). Tried in a window: it climbs at about 9.5 m/s, holds its height, goes
forward at 18 m/s and stops level.

## Weapons

- **Four Halberd PDCs** (rotary, 1,500 rpm, 300 rounds a box), each with its Ammo Box on a turntable and motor hinge,
  worked in pairs from the gunnery stations on the robot bay and ops decks (a Gunnery Console with a Turret Module, a
  Flight Couch facing it).
- **They stow** (Karl, 2026-11-15). Each turret lies level in an armoured housing on the deck's face (3 m by 0.84 m inside,
  1.5 m deep), on a Piston 2x2 Medium on the housing's back plate, and a **Halberd PDC Hatch** (a swing door, 90 degrees
  up and out about the mouth's top edge) seals the mouth. Each gunnery station has a **data point**: an OR Gate on its
  Turret Module. While any input is high the pair's pistons push the guns 1 m out and the hatches open; when it falls the
  guns pull in and a second OR Gate, held off a 3 s Delay, keeps the hatches open until they are back (the stroke takes
  2.5 s). A Bakelite Switch on the deck's wall is wired in; wire anything else into the same OR Gate (a sensor, a timer,
  a radio).
- **The Halberd Spinal Railgun** (700 damage, penetrating) fixed up the spine through the nose, fired by the pilot through
  a Turret Module; an Ammo Box on its breech. The ship is the aim.

## The flight couches

The cockpit's two Flight Couches stand on Motor Hinges across the ship (Karl, 2026-11-15: they turn as thrust is applied).
A Driving Module on the railgun's Turret Module takes the pilot's seat and sends its `lift` (Shift: the main drive, Ctrl:
down) to both hinges, which lay the couches back up to 35 degrees under full climb and tip them 15 forward on full
descent, at 20 degrees a second. Its drive output stays unwired, so the flight computer still flies the ship. The
constants are `CouchBack`, `CouchForward` in FrigateParts.cs; the blueprint carries the two links as `"links"` with a
`fromPort` (`BuildingParts.Blueprint.DataLinks`).

## Wiring

The lamp switches' wires are laid as conduit along the walls and ceilings although a ship isn't anchored: the blueprint
says `"conduit": true` (`ConstructBuild.Blueprint.Conduit`, power-and-links.md "Conduit in pre-builts"). The gun wires
cross a turret's joints, so they stay straight.

## The new parts

Big parts: the six decks, the nose, the thrust frame. Fittings: Halberd Main Drive, Halberd Manoeuvre Jet, Halberd
Landing Leg, Halberd Reactor (1.5 MW, no fuel), Halberd PDC, Halberd Spinal Railgun, Halberd Spine Ladder, Halberd
Boarding Ladder, Halberd PDC Hatch, Robot Charging Cradle, Crash Couch, Flight Couch, Galley Coffee Station, Gunnery Console, Halberd Flight
Computer.

## Not yet

- The pilot flies on Rotorcraft keys, so the tower stays upright and the railgun points at the sky in flight; Rocket keys
  would lean it to aim, untried.
- The gun stations' wires to the PDCs cross their rooms in the open (they ride the turret joints, so no conduit).
- The couches follow the pilot's climb key, not the ship's real acceleration (there is no acceleration sensor); W and A/D
  don't move them. The copilot's couch, on a joint, counts as a gunner's seat (its prompt says "man the gun").
- An opened hatch stands out level over its mouth; a turret raised steeply right under it may clip it (untried).
