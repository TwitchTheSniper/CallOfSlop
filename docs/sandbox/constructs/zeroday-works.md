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
| General line (bay 3) | | not yet |
| Yard, generator hall and power | | not yet: nothing has power until then |
| Shell, catwalks, dressing | | not yet |

**Size.** About 3,300 parts and 6,100 links so far, 360 of them floor slabs (`Construct.MaxParts` is 16,000, `MaxLinks` 48,000).
`--machine-blocks` on the file lists every block machine and whether its bed is whole. `--check-vehicle` on the file
takes about 2 minutes on the cloud box.
