# The Zero Day works

The factory that makes every piece of the Zero Day frigate and builds it in an open yard (Sean, 2026-11-22). The plan is the
"Zero Day factory layout" doc (https://claude.ai/artifact/LrowDScngfcuZGPZ6Yk1MU): read it before changing the layout.

**What it is.** One blueprint, `Constructs/Blueprints/ZeroDayWorks.json` (group Factories), written by the part kit generator
`zeroday_works` (`Source/PartKit/Generators/ZeroDayWorks*.cs`, one partial file an area) through the game's own build rules.
Regenerate with `--gen-parts zeroday_works` after the machine generators; never edit the JSON. Spawn it in creative from the
Blueprints tab on flat ground on a planet: it is 96 x 239 m.

**Coordinates.** x east across the site (0 to 96), z south as negative numbers (0 at the north edge, -239 at the south), y up;
the floor is Works Floor Slabs (8 x 8 m, `FactoryShellParts`), their tops at y 0.3, with a top socket every whole metre: a part
stands on a whole metre, and the 2 m machine cells' middles are the odd metres. (The slab replaced 2 m Foundations, which link
4 times an edge: 5,760 tiles used up the body's `Construct.MaxLinks` of 48,000, and the links past the cap were dropped without a
word, so the later machines welded to nothing.) North to south: generator hall (z 0 to -24), materials row under the annex (-24 to -40), inline extruder
strip (-40 to -68), crane bays (-68 to -199), assembly yard (-199 to -239).

**Built so far (by area, each its own PR).**

| Area | File | State |
| --- | --- | --- |
| Floor, column rows, crane rails, four bay cranes | `ZeroDayWorks.cs`, `.Frame.cs` | Rails on 9-mast (18 m) columns every 8 m at x 1, 31, 51, 71, 95 (spans 30, 20, 20, 24 m), z -41 to -197; each span's Crane Core at its run's north end, west of its near run |
| Materials row | `.Materials.cs` | Ore bunkers (iron ore, copper ore, sand), stone and wood bunkers, Smelter, Workshop Smelter (copper), glass Smelter, Foundry, Sawmill, Recycler, refined bins (metal, copper, glass, steel, planks), chuted |
| Armour lines A to D | `.Lines.cs` | Each line its own Sheet Extruder (die blocks, Run-out Tables), a roller run into its Panel Former (bed welded, a Mast Block on each corner, pins 4 m), Out Tables, a run onto a skin pad. A to C turn the pad east into a Laser Cutter (a cutter takes its blank by conveyor only, and its bed lies on its Core's +x side), its cut pieces waiting at a pick run's end for the bay crane. The crane then carries each piece to the dress station (a Building Station), the Bake Oven's bed and the line's stores. Line D has no cutter: its skins wait on their pad for the crane and its Building Station. Loader arms: 4 Industrial on each of A and B, 3 Industrial on C (a Plasma arm's base doesn't fit beside its station), 2 Plasma on D |
| General line (bay 3) | `.Bay3.cs` | Under the 24 m span (clear x 73 to 93), north to south: the walls line (a 2-cell Sheet Extruder with 3 Run-out Tables straight onto a pad that feeds a 2 x 9 Laser Cutter: `ArmourLine` with no former), the module Building Station (5 x 6 bay, masts 3 high) with a 3 x 4 stock pad and 2 Plasma arms, four Presses in two rows (a 3 x 4 bed east of each Core, a shell block on the north and south rows, a 2 m swing strip each side kept clear) with four Die Racks and the Die Lathe between them, the block Assembler (4 x 4 bed, 2-block masts, a Gantry Carriage a row, the Thruster Jig docked on its west side, Power Coupling, Servo Rack, Coolant Loop) with the Block Rack (welded on freeform: it is a medium part and the floor's sockets small; a chute from it into the Core is the tool swap's way) and the Sewing Table Block on the floor (shelve it on the rack by hand), the walls store (5 x 8) and fittings store (4 x 8), and the fittings run south along the bay's east side. The repair shop, spare parts and maintenance rooms come with the shell |
| Generator hall and power | `.Yard.cs` (`LayPower`) | Three Medium Gensets (1 MW plants, welded on freeform: medium parts), fuelled by pipe from two Medium Fluid Bunkers (fill them in creative), four Ship Battery Banks. The frame is the circuit, so every part the floor's links reach is powered. The doc's three engine-kit sets with 1 MW heads can't be: a construct runs one engine only (the first that runs), and plants have no such limit. No standby switch yet: all three follow the load |
| Assembly yard | `.Yard.cs` (`LayYard`) | The yard's Building Station (30 x 12 bay, x 17 to 75, z -203 to -225, 8 Mast Blocks on each corner), a yard crane on 22 m columns with its rails east-west at z -199 and -231 (a 32 m span) and its Core at the north run's west end, a 6 x 5 staging pad and a 5 x 4 fittings pick pad east of the station's Core, ten Plasma Loader Arms (nine along the bay's south side between the columns, one at the pick pad). Lines A, B and D's transfer runs end at the yard's north rail line |
| Shell, catwalks, dressing | | not yet |

**Size.** About 4,500 parts so far, 360 of them floor slabs (`Construct.MaxParts` is 16,000, `MaxLinks` 48,000).
`--machine-blocks` on the file lists every block machine and whether its bed is whole. `--check-vehicle` on the file
takes about 2 minutes on the cloud box.

**Known.** `--check-vehicle` (about 4 minutes) finds about 290 coplanar pairs between parts, all small detail boxes where two
machines' cells meet on the 2 m grid (a Mast Block's stripe against a Roller Tile's or a Laser Bed Block's edge detail). They come
from the parts' own details sitting on their cell faces, not from the layout, so they are fixed in those parts' generators.
