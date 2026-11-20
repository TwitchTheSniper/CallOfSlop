# The Sheaf field barge (first try)

Karl, 2026-11-20: "make the first try for a flying field, medium size", after the space farmer mood board
(https://claude.ai/artifact/J9LCM6HV2M7LnueA6UCPyc). The space farmers are a new faction idea, not yet in the game's faction list.

Generator `Source/PartKit/Generators/FarmBargeParts.cs` (`--gen-parts farm_barge`), parts in `Constructs/Parts/Sheaf`, blueprint
`Sheaf Field Barge` (Blueprints tab). Its parts are unlisted (`Categories.json`'s ship-name rule) and have no Assembling recipes yet
(`Assembling.SkipFolders`), as for any draft ship.

## What it is

The Dray's frame (HaulerParts.cs, copied, kept in step by hand): the same lofted hull, crew decks, drive room, two torch drives with
their engines, lift jets, six leg pods, boarding hatch and welded pieces, about 58 m long.

- The hold is two **fields** (rows 1-2 and 4-5): three raised beds a row (planks, soil, two rows of green, a water line), 1.5 m walks
  between, a grow light hung over each bed on rods from the glazing bars. The middle beds by the end bulkheads stop 1.4 m short so the
  doors keep a landing.
- The roof over the fields is **glass**: the hull's upper profile raised to a greenhouse pitch (ridge at 10.4 m), glazing bars along and
  across each pane, an eaves beam on each side and a ridge beam (they weld the roof bands to the fields). No ceiling: the fields are open
  to the glass. Gable walls close the ends above the bulkheads.
- Row 3 is the **packing bay**: the Dray's side doors and belly well, two container slots.
- Paint: field olive skin, straw cream bands, barn red doors, beacon amber, greenhouse glass, grow-light pink.

## Gone from the Dray

The crane and its girders, the Lifter Cab, the cargo volumes and starting containers, and the saddle tanks (fuel is the keel's, bow's and
stern's only).

## Not done

- The glass has no collision of its own (drawn only); the frames and beams do.
- The cab is the Dray's low bridge: the board's tall glass cab with a beacon is not drawn yet.
- Nothing grows: the beds are looks, not a machine.
- No air check for the open fields; no lamps or switches in the fields beyond the Dray's wall lamps.
- `--flight-check` fails the Ship keys nose test by the same margin as the Dray (nose 41 degrees down for 55).
