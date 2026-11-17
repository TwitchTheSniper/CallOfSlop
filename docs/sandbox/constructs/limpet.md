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
(`"breach": grapple`, reach 0.6 m), the Limpet Ladder (its rails stop under the ceiling; its `ladder` climb reaches the
Zero Day's lower deck), eight Flight Couches, a Flight Stabilizer (no pilot's seat: it takes orders), two Batteries, 16
RCS Thrusters and four Bulkhead Lights. Three switches by the door (starboard) work the locks, the cutter and the door; the
port switch the lights. A Docking Hatch hangs upside down under the receiver and slides forward along the ceiling.

## Docked under the Zero Day

The Zero Day's legs are 0.75 m longer for it (`Ground` -4.85). The pod hangs under the keel on the centreline, nose
forward, its roof against the keel, held by a Docking Clamp Medium hung upside down under the keel to port
(`LimpetClampConfig`: starts holding, its box round the pod). The keel has a 1.1 m shaft over the receiver (`LimpetShaft`,
lined, rungs on its aft side, a hazard frame round its mouth, a rail round its top on the lower deck, open aft where the
ladder lands) and a Docking Hatch in a pocket at its foot that slides aft into the keel. The two hatches open together
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
5. **On a door**: the target's door opens, then the pod's own. **On the hull**: the cutter burns through, and once it is
   through (it then cuts no deeper, wedged in its hole) the pod's door opens.

The pod is steered as a physics body (its speed and turn set straight, within 12 m/s² and 1.2 rad/s), not as a space ship:
it stays a body of the bubble while it boards (GameSpaceLaunch.cs leaves it be), so it boards in space as in a sky. The order
is the pod's, so its card shows it; the target is the order's point on the wire (protocol 340).

## Not done

- The RCS jets don't flame while it flies (it is steered straight); no fuel is spent.
- No path finding: it flies straight once clear of its carrier, so a target behind a mountain or another ship is hit.
- A pod that has boarded stays on the target: there is no way back to the carrier (one way, as designed).
- The grip isn't kept through a save and load (as the Shuttle's).
- Tried on the host only (a Glacier test world, the Zero Day boarding a Magpie by its hull and by a door); a joined
  client's view of the order wasn't tried.
