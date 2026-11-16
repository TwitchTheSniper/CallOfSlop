# The Halberd frigate (a tail-sitter combat ship)

Karl (2026-11-15): a second player ship, a mid-size combat frigate with point-defence turrets, a med bay, a robot area, a
layered hull and a lived-in interior; "make new parts too, go wild", and none of the rust bucket's bathroom tiles or
clipping. Generator: `Source/PartKit/Generators/FrigateParts.cs` (`--gen-parts frigate_parts`), which writes
`Constructs/Parts/Frigate/*.json` and `Constructs/Blueprints/HalberdFrigate.json`. Change the generator, never its output.
Built the way the rust bucket is (ship.md), big parts that share the body's origin, but each storey is broken into pieces
so a hit takes one piece, not a deck (see "Breaking up"); furniture on the pieces' sockets.

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
Labelled pipes run up the corners. `--check-building "Halberd Frigate"` passes (142 m2 a standing person can reach,
from the ground up the boarding ladder).

## The hull

Each storey's band is pale skin plates (trimmed a few mm at alternate edges so neighbours never share a plane), a dark
frame rib up every corner and a girder at the deck line, and over that bevelled slate armour slabs: one half of each flat
face armoured in two steps, the other half bare skin with its panel seams, the side swapping face to face and storey to
storey, raked slabs on the chamfered corners with primer-red scuffs, hazard flashes. Four armoured PDC housings stand out of the faces. The
shoulder over the ops deck steps the tower in from 8 m to 5.5 m wide, so the silhouette reads as a hull with a head.

## Breaking up

Karl (2026-11-15): attacking one deck made the whole deck vanish; he chose every wall and floor as a part of its own. Each
storey is still drawn whole by its builder (`Engineering()` ...), then `Split` deals its boxes out to pieces: the
**Deck** (the floor and what stands loose in the room, the part that keeps the storey's old name), four **Wall** runs
(Bow, Stbd, Stern, Port, with what is on their faces and outside them: the PDC housings, the shoulder), four **Corner**
frames (the chamfer, the corner rib and armour; tough, 30,000 health) and each **Partition** (recorded by `Partition()`).
The four **Hull** panels per storey are made by `HullPanels`. A socket goes to the piece whose box face it is on
(`SockOwner`), and the blueprint's `Hang` wrapper puts furniture on that piece. `Connect` gives every two pieces whose solid
boxes touch face to face a facing socket pair (`j0`, `j1` ...): one pair places a piece (`PlaceOrder`, out from the
engineering deck), the game welds the rest (`ConstructBuild.AutoLink`), so a piece knocked off leaves the others welded
(tried: the starboard hull and wall of two storeys shot away, the rest of the ship stood; a floor's loss drops only what
stood on it). 233 parts in all; `--check-vehicle` finds no coplanar faces between them.

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
says `"conduit": true` (`ConstructBuild.Blueprint.Conduit`, power-and-links.md "Conduit in pre-builts"). So are the gun
stations' wires (they run in the room, pass through the hull into the PDC housings and cross the turret joints once) and the
couches' two `"links"` (power-and-links.md "Ship links and walls").

## The new parts

Big parts: the six storeys' pieces (deck, walls, corners, partitions, hull panels), the nose, the thrust frame. Fittings: Halberd Main Drive, Halberd Manoeuvre Jet, Halberd
Landing Leg, Halberd Reactor (1.5 MW, no fuel), Halberd PDC, Halberd Spinal Railgun, Halberd Spine Ladder, Halberd
Boarding Ladder, Halberd PDC Hatch, Robot Charging Cradle, Crash Couch, Flight Couch, Galley Coffee Station, Gunnery Console, Halberd Flight
Computer.

## Not yet

- The pilot flies on Rotorcraft keys, so the tower stays upright and the railgun points at the sky in flight; Rocket keys
  would lean it to aim, untried.
- The couches follow the pilot's climb key, not the ship's real acceleration (there is no acceleration sensor); W and A/D
  don't move them. The copilot's couch, on a joint, counts as a gunner's seat (its prompt says "man the gun").
- An opened hatch stands out level over its mouth; a turret raised steeply right under it may clip it (untried).
