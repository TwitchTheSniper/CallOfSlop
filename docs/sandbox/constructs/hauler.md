# The Dray container hauler

Karl (2026-11-16): a hauler built with the ship builder, "just the frame and interior walls/doors" for now, carrying 10 to 20
cargo containers with an onboard crane to pick them up. Survival is logistics: the Dray is the ship that moves the goods.

Generator: `Source/PartKit/Generators/HaulerParts.cs` (`--gen-parts hauler_parts`), which writes `Constructs/Parts/Dray/*.json`
and `Constructs/Blueprints/DrayContainerHauler.json`. Change the generator, never its output. Built the Zero Day's way
(torpedo-frigate.md): a lofted hull first, the rooms a box of their own inside it, each big part drawn whole and then dealt
out into welded pieces (floors, wall runs, ceilings, hull bands; the keel and spine cut at the stations). Its pieces are
blueprint-only (`Categories.json`'s name rule has "Dray").

## The shape

About 58 m long, 14 m across and 10 m tall, 157 t. A low, narrow cab forward (the crew section) steps out through a raked
shoulder to the wide hold, which runs straight to the drive room, where the stern pinches in to a transom with two big
drives on shrouds. Heavy ring frames stand proud of the hull at every container row; between them the hold's sides are ribbed like a
container's walls, alternating pale and freight blue row by row. Freight blue armour on the cab, a strongback (two box
girders) along the hold's roof, a sensor mast, radiators over the drive room.

## Inside

| Part | What is in it |
| --- | --- |
| Dray Crew Lower Deck | mess, stores, the suit room with the ladder up, the airlock over the boarding shaft, the door aft into the hold |
| Dray Crew Upper Deck | the sealed bridge (a view screen, no glass), two cabins off a hall, the head, the aft hall with the ladder |
| Dray Hold Fore / Well / Aft | five rows of three container slots (6.4 m rows, lanes 2.7 m apart), less the slot over the well: 14 slots |
| Dray Drive Room | aft of the hold, empty for now |

- **Containers.** Each slot is marked by four twistlock pads for a 20-foot box (6.06 by 2.44 m, 2.6 m tall). No container
  part exists yet.
- **The crane.** Two runs of seven Crane Rail Medium 4 m, 8 m apart, on girders cantilevered off the hold's walls (rails at
  5.25 m), and a Medium Crane Bridge on the port run. Its hook drops 8 m: from the rails to about 4 m under the floor, so
  the ship can set down over a container on legs that leave about 2.9 m under the keel and lift it up through the well.
  In a window run the bridge sits on its rails and reports Idle (nothing to pick); it needs power to move.
- **The well** is the middle lane of row 3 (6.4 by 2.9 m), through the floor and the keel. Two belly door leaves
  (Dray Well Door L/R, slide doors) hang from tracks across the well's ends and slide outboard 1.5 m to open it.
- **Side doors** (Karl, 2026-11-16): one each side beside the well, in the lower side panel of row 3 (5.2 m long, floor to
  3.05 m), through the hull piece and the hold wall. Dray Side Door L/R are ribbed swing doors hinged along their head at
  the beam: they swing up and out 90 degrees to an awning over the doorway. Each hangs by `sideDoorL`/`sideDoorR` under a
  hinge beam in the well band's hull piece; the brackets of the crane's girder stand at the doorway's jambs.
- **Doors** are Bulkhead Doors 1.5x2.25 m in hazard orange.

- **Drives** (Karl, 2026-11-17: two large thrusters on the ship's middle line that look substantial from behind, the drive
  room slim and stretched to meet the stern so every piece can be reached; ship-works.md 15, one engine per bell): two Drive
  Nozzle L (10 MN each, 20 MN) on the stern's `drive0`/`drive1` seats at x -2, +2 (`DriveX`), axis height `DriveY` (2.9 m,
  the transom's middle), each in a broad shroud (r 1.85, `ShroudR`). The stern is the mount (`Works.Mount`); behind each nozzle
  its own engine bolts to the transom bulkhead's inner face (`engine0`/`engine1` at `StIn` 53.4, on the nozzle's axis): a
  Drive Core L, a Fuel Pump L and a Coolant Exchanger L, one cylinder on saddles running forward to z 47.8. They stand on a
  raised engine floor (`Fe`, 1.5 m up, from `ZPlat` 48.0 to the bulkhead) reached by a Steel Stair Flight 1.5 m from the door;
  the room is 8.4 m wide inside (`Ew` 4.2) and 4.5 m high (`CeilE`), with a walkway between the engines and one outboard of
  each. Three beams across under the ceiling (`EngineFrameZ`), on posts against the walls, hang a pair of straps onto each
  engine. The stern's bottom station is fuller so the room fits it. The Engineer's Panel is on the port wall by the stair.
  `--check-vehicle` prints both heads at 100%.
- **Attitude jets** (Karl, 2026-11-16: it flips and burns on its main drives, with small jets every way for small moves):
  16 Heavy RCS Thrusters (250 kN, under the 500 kN that needs works) on pads, a cluster at the bow (z 3, and two in the
  nose tip) and one at the stern (z 52, and two on the transom): up, down, out to each side and along the ship, two of
  each, so it can slide any way and turn on any axis.

## Not yet

Legs, the boarding ladder, a seat and flight stabiliser, lamps, wiring and fittings (Karl's scope for the first cut). Without legs and a
boarding ladder `--check-building` can't reach the decks from the ground; its map shows every room joined through its doors.
No container part, no power for the crane, no cargo storage volumes.
