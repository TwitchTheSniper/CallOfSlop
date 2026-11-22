# The Zero Day works

The factory that makes every piece of the Zero Day frigate and builds it in an open yard (Sean, 2026-11-22). The plan is the
"Zero Day factory layout" doc (https://claude.ai/artifact/LrowDScngfcuZGPZ6Yk1MU): read it before changing the layout.

**What it is.** One blueprint, `Constructs/Blueprints/ZeroDayWorks.json` (group Factories), written by the part kit generator
`zeroday_works` (`Source/PartKit/Generators/ZeroDayWorks*.cs`, one partial file an area) through the game's own build rules.
Regenerate with `--gen-parts zeroday_works` after the machine generators; never edit the JSON. Spawn it in creative from the
Blueprints tab on flat ground on a planet: it is 96 x 239 m.

**Coordinates.** x east across the site (0 to 96), z south as negative numbers (0 at the north edge, -239 at the south), y up;
the floor is 2 m Foundations, their tops at y 0.3. A part standing on the floor goes at a tile's centre (odd x and z) or a
0.5 m socket of it. North to south: generator hall (z 0 to -24), materials row under the annex (-24 to -40), inline extruder
strip (-40 to -68), crane bays (-68 to -199), assembly yard (-199 to -239).

**Built so far (by area, each its own PR).**

| Area | File | State |
| --- | --- | --- |
| Floor, column rows, crane rails, four bay cranes | `ZeroDayWorks.cs`, `.Frame.cs` | Rails on 9-mast (18 m) columns every 8 m at x 1, 31, 51, 71, 95 (spans 30, 20, 20, 24 m), z -41 to -187; each span's Crane Core at its run's north end, west of its near run |
| Materials row | `.Materials.cs` | Ore bunkers (iron ore, copper ore, sand), stone and wood bunkers, Smelter, Workshop Smelter (copper), glass Smelter, Foundry, Sawmill, Recycler, refined bins (metal, copper, glass, steel, planks), chuted |
| Armour lines A to D | | not yet |
| General line (bay 3) | | not yet |
| Yard, generator hall and power | | not yet: nothing has power until then |
| Shell, catwalks, dressing | | not yet |

**Size.** About 7,000 parts so far, 5,760 of them floor tiles (`Construct.MaxParts` is 16,000). `--check-vehicle` on the file
takes about 2 minutes on the cloud box.
