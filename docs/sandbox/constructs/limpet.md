# The Limpet (the Zero Day's boarding pod)

Karl, 2026-11-17: after the first pod became the Shuttle ([shuttle.md](shuttle.md)), a simple 8-person boarding pod with a
cutter at the front, flown from the carrier's flight controller. The approved layout is v3
(`/mnt/project-files/limpet/limpet-layout-v3.png`): eight seats, four a side facing the aisle; the seal skirt and the
breaching cutter's ring on the nose, round a front door that splits into two sliding leaves; RCS only, no main drive (a
one-way trip); the receiver and the ladder centred at the aft end of the roof; docked flat under the Zero Day's keel on the
centreline, the ladder climbing straight up into the lower deck.

Generator: `Source/PartKit/Generators/TorpedoFrigateLimpet.cs` (a partial of `TorpedoFrigateParts`: `--gen-parts
torpedo_frigate_parts` writes it with the frigate, then `--gen-parts assembling`). Parts in `Constructs/Parts/Limpet`, the
blueprint `Constructs/Blueprints/Limpet.json` (unlisted in the Tab inventory, as ship pieces are: Categories.json).

## The pod

A box 3 m wide, 2.35 m tall and 5.6 m long (x across, y up from the belly, z back: the nose is -z), 7.8 t, in welded
pieces: Limpet Floor (the deck, the console aft to starboard, the seats', batteries' and ladder's sockets), Limpet Side
Port and Starboard (chamfered walls, the outriggers with two RCS quads each, lamps and switches), Limpet Roof (ceiling,
light strip, the receiver: a 1.1 m hole with an armoured collar, and the Docking Hatch's pad under it), Limpet Nose (the
bulkhead in four blocks round the door, the seal skirt with its gasket, the cutter's and mag locks' mounts), Limpet Stern.

Fittings: two Limpet Door Leaves (event doors, sliding 0.56 m apart, `"door"` slide), the Limpet Breaching Cutter (a ring
round the door, `"breach": cutter`, reach 2 m, 900 a second raw), four Limpet Mag Locks at the skirt's corners
(`"breach": grapple`, reach 0.6 m), the Limpet Ladder (up to its own docking hatch, its rails stopping under the ceiling;
docked, the climb runs on up the Zero Day's shaft ladder), eight Flight Couches, a Flight Stabilizer (no pilot's seat: it takes orders), two Batteries, 16
RCS Thrusters and four Bulkhead Lights. Three switches by the door (starboard) work the locks, the cutter and the door; the
port switch the lights. A Docking Hatch hangs upside down under the receiver and slides forward along the ceiling.

## Docked under the Zero Day

The Zero Day's legs are 0.75 m longer for it (`Ground` -4.85). The pod hangs under the keel on the centreline, nose
forward, its roof against the keel, held by a Docking Clamp Medium hung upside down under the keel to port
(`LimpetClampConfig`: starts holding, its box round the pod). The keel has a 1.1 m shaft over the receiver (`LimpetShaft`,
lined, a hazard frame round its mouth) under a flush Deck Hatch 1.1 m in the lower deck, hung by its starboard edge (Karl,
2026-11-22: no rails, walked over shut), with its own Zero Day Limpet Ladder on its aft side from the deck down to just over
a Docking Hatch in a pocket at its foot that slides aft into the keel. Docked with both docking hatches open, F at either
end climbs the two ladders as one, from the ship's lower deck to the pod's floor or back (`ConstructRide.Chained`: a ladder
end by a docking hatch of its own body meets the docked craft's ladder lined up with it; a shut hatch there says "the
docking hatch is shut: nothing is docked there"). Crews don't yet path through it, and `--check-building` walks the carrier
alone, so it says the shaft ladder has no room at its foot; `--ladder-check` climbs it docked. The two hatches open together
while the clamp holds the pod and shut when it lets go (`ConstructDoors.StepDocks`). The blueprint's `"carry"` list builds
the pod held (`ConstructCargo.BuildCarry`).

## Boarding (the Board order, Game/GameNavBoard.cs)

The captain in the command view (docs/space/COMMAND.md "Boarding") picks the carrier, right-clicks a door or a hull
panel of another craft and picks BOARD. The dock lets the pod go and the host flies it, nobody at its controls:

1. **Drop**: 0.3 m down out of its dock, held level as it hung (its jets hold it up: gravity is taken back each tick).
2. **Out**: sideways between the carrier's legs, still turned as it hung, until it is clear of the carrier's plan.
3. **Across**: to a spot 4 m off the target's skin (climbing up to 12 m over the straight way while far, so it doesn't drag
   on the ground), turning nose-in, its roof up the target's way, at most 14 m/s.
4. **In**: at 0.8 m/s until two mag locks reach the hull (or, after 4 s pressing, one lock or the nose itself), then the
   locks go on and it grips (`ConstructBreach.Grip`); a pod gripped by the order holds while its locks are on
   (`Construct.BreachSealed`), hull under them or not.
5. **On a door**: the target's door opens, then the pod's own. **On the hull**: the cutter burns for 10 s, then takes a
   real hole out of the hull (below), and the pod's door opens.

### The hole (Karl, 2026-11-18: "option C")

On the hull the order first picks where to cut (`BoardNearRoom`): a spot with a room behind it (`ConstructBreach.RoomBehind`:
a floor, head room and a roof of its own within 6 m), its floor level with the pod's (within 0.35 m, the spot moved up to
2.5 m up or down its face where the nose still sits flat). The picked spot first, then the parts round it, nearest first;
failing a level floor, any room; failing that, the spot as picked. The Zero Day's bow tip is solid, so a Board order on it
lands on a flank.

Any gripped cutter (the Shuttle's too) burns `ConstructBreach.HoleTicks` (10 s) on one spot, then `OpenHole` cuts a tunnel
1.1 m wide (the ring's opening) and 2.1 m tall from just over the room's floor, as deep as that room's wall (1.2 m when no room
was found), level in the target's frame. Every part it crosses that can take a hole is swapped for a holed part
(`Source/Constructs/HullHoles.cs`): a named run-time part, `Holed (BASE | x,y,z,dx,dy,dz,ux,uy,uz,w,h,l)`, its boxes cut round
the tunnel and its thin hull facets (the skin, which collides) cut in their own plane, so the hole is gone from the drawing,
the collider and a player's moves alike: you see space through it and walk through the gap. It travels as its key (saves,
blueprints and the wire: protocol 352), so a joined client builds the same part. A part that can't take a hole (a machine)
but is at most 2.5 m each way is burned away; bigger ones are left and logged (`[breach]` in callofslop.log). The body is
rebuilt once, as any edit's.

The seal (Karl, 2026-11-18: "a door-shaped O ring so you can't see outside"): once the hole is out, `ConstructBreach.Seal` lines
the way from the cutter's face to the room behind with a seal sleeve on the pod part nearest the cutter (the Limpet Nose): a
rectangular tube lofted from the pod's door (square to the pod, 1.14 by 1.96 m) to the hole's far end (level in the target,
1.5 cm inside the hole's edge, so it hides the cut faces), four 5 cm walls in the gasket's black rubber that draw and collide as
hull plates. Nothing outside shows through the gap between a slanted hull and the skirt, or through a hollow (a torpedo bay's
port) the hole passes. It is a `HullHoles.Sleeve` entry in the part's key (`Holed (Limpet Nose | s...)`, protocol 360). The
look line shows a cut or sealed part by its file part's name ("Zero Day Torpedo Bay L (cut through)"), not its key.

Trying it: the window tool's `boardwalk [BACK] [TICKS]` (host) puts this player in the gripped pod BACK m behind its cutter
facing the hole, prints the doors and what lies ahead, and holds W. On the Zero Day start (`--survival --start ZeroDay
--map Caledon --do "wait 60; skip; wait 200; command 600; cmdpick 1; order board 2 hull; esc; wait 5500; boardwalk 1.6 400"`)
the pod cuts in by the Traders' Upper Fore Hull Port 2 and the player ends in their ship.

The pod is steered as a physics body (its speed and turn set straight, within 12 m/s² and 1.2 rad/s), not as a space ship:
it stays a body of the bubble while it boards (GameSpaceLaunch.cs leaves it be), so it boards in space as in a sky. The order
is the pod's, so its card shows it; the target is the order's point on the wire (protocol 342).

## Not done

- The RCS jets don't flame while it flies (it is steered straight); no fuel is spent.
- No path finding: it flies straight once clear of its carrier, so a target behind a mountain or another ship is hit.
- A pod that has boarded stays on the target: there is no way back to the carrier (one way, as designed).
- The grip isn't kept through a save and load (as the Shuttle's).
- A hole stays a hole: nothing repairs it, and there is no air to vent (no compartments yet).
- A holed part keeps its whole health; its ceiling or spine edge may be cut too where the room is lower than 2.1 m.
- Tried on the host only (a Glacier test world, the Zero Day boarding a Magpie by its hull and by a door); a joined
  client's view of the order wasn't tried.
