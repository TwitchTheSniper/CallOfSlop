---
paths:
  - "Source/Constructs/ConstructDrive*.cs"
  - "Source/Constructs/ConstructEngine.cs"
  - "Source/Constructs/ConstructSuspension.cs"
  - "Source/Constructs/ConstructTracks.cs"
  - "Source/Constructs/ConstructWheelShape.cs"
  - "Source/Constructs/ConstructHalfShaft.cs"
  - "Source/Constructs/ConstructLinear.cs"
  - "Source/Constructs/ConstructRotor.cs"
  - "Source/Constructs/ConstructHydraulics.cs"
  - "Source/Constructs/ConstructWalk*.cs"
  - "Source/Constructs/ConstructLegParts.cs"
  - "Source/Constructs/JointSides.cs"
  - "Source/Constructs/SmartRotation.cs"
  - "Source/VehicleKit/**"
  - "Source/EngineKit/**"
  - "docs/vehiclekit/**"
  - "docs/sandbox/constructs/drivetrain-and-engines.md"
  - "docs/sandbox/constructs/joints-and-logic.md"
---

# Wheels, joints, drive lines and the vehicle kit: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- Several contact rays of one part are one wheel to the drive line: counting a Track part's rays as wheels on an open differential passed a tenth of the torque whenever one ray lost the ground, so a crawler crept at 2 m/s over rough ground (`TrackUnitsShare`). Try a drive change over bumps, not only on the flat.
- A drawn thing with thickness is in the contact height: a track's links hung a pad's thickness below the wheel's radius, so they sat in the ground and their inner face fought it (`CWheel.Band`). A hand check of ride height reads a settled, simulated body, not a spawned one.
- A contact the rim has refused is a wall, level: the cylinder's top edge threw a buggy up and onto a block just past the 0.6 R limit (`Rides`). Test the band just above a limit, not only well past it.
- A wheel's forward sense comes from `Construct.DriveFwd` (`RollSign`, `RollAxis`), never `CWheel.Axle`. Try a turn's direction against the key, and a straight run's yaw after any tyre-force change (`HoldHeading`).
- A joint's motor gives back its gained motion through the whole body's inertia, never a holding force; per-joint maths over a chain is posed through it (`PoseCarried`), and chain tests compare against a hand moment.
- A part made for what goes on it is turned for that part (`SmartRotation.AxleUp`); try a feature from the ghost's own default on every face.
- Every generated vehicle has corners that hang alike (`VehicleChassis.Level`) and passes the spawn drive check (`VehicleDriveCheck`).
- 2026-11-10: fix/turret-seat ran the rotary joints' speed from stopped (min 0) and Construct actuators still wanted a minimum above zero; a range change in a part generator is followed by the checks that read that range.
- 2026-11-11: all-wheel drive moved `FrontAxleZ` an eighth ahead of the rails, and `Vehicle.RailZ` still counted socket rows from it, so every Gun Wagon bed sill was asked for between two sockets (the T Gearbox got the blame). Work out a socket grid from the part that holds the sockets (`RailGrid`), never from an axle line, and read the refusal's "m from where it was asked" before blaming a placement.
