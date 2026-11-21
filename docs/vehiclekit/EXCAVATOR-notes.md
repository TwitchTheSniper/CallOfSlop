# The Excavator: notes (builder, 2026-11-17)

The user's note (factory-generator-notes.md, "Extraction must follow the ore"): drills deplete the ground under them, so extraction has to move its drills over
the deposit; a mining vehicle shaped like an excavator helps. This is that vehicle: a vehicle-kit generator, `Source/VehicleKit/Generators/Excavator.cs`,
Militia style (`Militia Excavator`, seeds 1, 14 and 24 committed: `--sweep-vehicle --seeds 30` passes 30 of 30, the shipped three 0.28 apart).

## What it is

- **Undercarriage.** The kit's ladder frame with a Wide Differential at the back, a Stiff Suspension and a 5 m Track on each side reaching forward; the
  V8 and an Automatic Gearbox ahead of the differential, an Alternator on the engine, the fuel tank on a tail sill. It skid steers like the Tracked Crawler.
- **House.** A Turntable 3x3 Medium on a three-layer winged sill ahead of the engine, a 2 m spine and a cross beam on it, a plated deck. At the back the
  power pack: a Small Genset (100 kW) on the spine's end with its own Fuel Tank and the Battery. The arm's motors draw tens of kilowatts while they move
  (work over 0.8), so the 300 kJ battery alone went flat in a few seconds and every joint let go; an alternator gives 1.5 kW. The battery stays because a
  drill needs one in survival (`ConstructRecycler.Powered`).
- **Arm.** Built in its travelling pose: the boom upright on a Motor Hinge 3x3 Medium at the cross beam's right, the stick out forward on another 3x3, the
  Digging Bucket hanging from a Motor Hinge 2x2 Medium under the stick's end, mouth to the cab. A tier-1 Drill Module on the stick's top at its end, its
  bit forward past the end, its Chute out linked into the bucket (a chute flexes across a joint). By seed: rails 3.25 to 4 m, boom 2.5, 2.75 or 3 m, stick 1.75 or
  2 m (lengths a beam run makes exactly); the bucket (Digging twice as likely as Rock or Trenching); the cab shell (Roll Cage, Tin Roof, Chop Top, Mesh
  Guard); a work lamp on the cab, a spare tank, a push bar on the nose and armour plates on the power pack.
- **Controls.** The seat (left, behind the boom's foot) links to a Driving Module: W/S/A/D drive the tracks, and the module's `roll` output (Q and E)
  goes into the turntable, which turns while it is off zero and holds where it stops. Three Analog Levers on the seat's sill (left to right: boom, stick,
  bucket) are wired to the hinges, and the Switch beside them to the drill's Run. Each lever starts where the arm was built: the boom's at 0 (upright;
  its lever lays it forward to 95 degrees), the stick's and the bucket's in the middle (low -1, high 1: +-90 and +-100 degrees). A hinge's input is a
  position (`ConstructPhysics.PositionTarget`), and a lever stays where it is left, so the arm holds a pose with no key held. Any crew driver can take the
  seat (it is the drive seat); a crew that works the arm is future work (it needs an act that sets levers).

## How it is used

1. Drive to the face. Lever the boom down (the stick then hangs straight), turn the bucket's edge down, pull the stick and curl the bucket through the
   ground: it fills with dirt or stone (one material a bucket). Raise the boom; the bucket is mouth up.
2. Q or E slews the house; lever the bucket open over a hauler's open bed (the Militia Ore Hauler's Tipper Body takes it) or over open ground (a heap).
3. Ore: curl the bucket out of the way, lever the boom down so the drill's bit is on the vein, switch the drill on. It chutes ore into the bucket; swing
   it over the hauler and pour. A tier-1 drill works iron and stone; swap it for a Workshop Drill Module for copper (the heavier one takes the boom past
   its rating).

## Checks

- `--gen-vehicle "Militia Excavator"` and `--sweep-vehicle "Militia Excavator" --seeds 16`: the kit's full check. `VehicleChecks` now accepts a body that
  skid steers (`Construct.SkidSteer`) with no steered wheel; before, "it needs both" failed every tracked vehicle.
- `--excavator-check [--seed N] [--map NAME] [--no-client]` (`Source/VehicleKit/ExcavatorCheck.cs`): a real host on the Construct Yard (or a planetoid)
  and a joined client. The host's own player sits in the seat; the levers are set as a hand sets them (`AnalogLever.SetValue`) and E is held through the
  seat's keys. It digs, curls, slews, tips the spoil onto the ground, fills the bucket through the drill's chute, sets an Ore Hauler under it and pours,
  then puts the arm in its drilling pose. On a round world it stands over a tier-1 vein and runs the drill. The client's copy is compared with the host's
  for the boom and the slew. About a minute of game time; not a self-test section.
- `--gen-vehicles` compares Militia's committed files too: a style that doesn't ship names the shapes it commits (`FactionStyle.Committed`: the Ore
  Hauler and the Excavator), and `VehicleRegistry.All` adds them.

## Measured (2026-11-17)

`--excavator-check` passes every step on the Construct Yard with a joined client (the client's boom and slew within 3 degrees of the host's) and on the
Dev Planetoid, where it stands over an iron vein and the lowered drill fills its buffer (20 iron ore in 10 s). Named sections: No z-fighting, Construct
actuators, Construct drivetrain, Terrain digging and Constructs pass; Gathering failed only its ray timing (457 ms against 200) at a load average of 26 on
the shared 2-core box: this branch changes no ray or gathering code. `--gen-vehicles`: 242 identical.

## Found on the way

- A stick of 2.25 m was laid as 2 + 0.5 m (the beam run overshot), so the bucket's hinge missed the stick's sockets; the run now refuses a length it can't make.
- The lip a bucket pours from swings out as it opens: the check backs the hauler's bed under the lowest lip while it pours, as a driver would.
- A yard sentry shot the seated host player and its rounds broke the arm off: the check stands the excavator 40 m or more from any armed body.
- The boom's 3x3 hinge reads 85 to 110% of its rating with the arm laid out (a few degrees of give). A Motor Hinge Medium (500 kN m) needs a medium
  socket, and no adapter takes a medium part onto a small socket. Hydraulic cylinders would be the real answer (a ram with leverage), but the vehicle kit's
  `BlueprintScript` can't lay a cylinder (they are tool-made connectors); next step if the arm needs more.

## Not done

- No crew act works the arm: a crew can drive it (the seat is the drive seat) but not dig.
- No shipped style other than Militia builds it (any style can by name: `--gen-vehicle "Raider Excavator"`).
