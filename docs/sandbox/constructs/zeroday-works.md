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

## The shed's shell parts

The steel shed over the bays is built from part kit generator `factory_shell` (`Source/PartKit/Generators/FactoryShellParts.Mill.cs`), parts in `Constructs/Parts/Factory/Shed`, section "Factory shell". Nothing places them yet: the works generator does that.

- **Why the roof isn't carried by the inner rows.** A Hall Crane's bridge is drawn up to 2.62 m over its rails' top, and the bays on either side of x 31, 51 and 71 share one rail, so nothing can stand on an inner rail line under that height. The roof is one chain of half trusses from wall to wall, its bottom chord level 4 m over the rail sockets (y 22.8 on the works), carried only by Eaves Posts on the two outer rows. A construct has no load paths, so a 96 m chain holds like a 30 m one.
- **Eaves Post.** Its "rail" socket goes on an outer Crane Rail Block's outer side (the -x of the west row, the +x of the east row), one per mast column (every 8 m). The "seat" on its bracket is over the rail's centre line, 4 m over the socket; its "wall" socket on the outer face takes a "Wall Sheet 8x3 m Eaves".
- **Roof Truss Half 10, 12, 15, 16, 20 m.** Low end at z 0 (an eave or a valley), ridge at z = L, pitch 0.3 (rise 0.3 m a metre), 1.2 m deep at the low end. Two halves join ridge to ridge ("ridge" to "ridge") and valley to valley ("low" to "low"); "seat" sits on an Eaves Post. On the works: 15+15 over bay 1 (x 1 to 31), 20+20 over bay 2 (31 to 71, the ridge over the 51 rail), 12+12 over bay 3 (71 to 95). Ridges come out at y 28.5, 30.0 and 27.6.
- **Roof Slope WxL m Eaves / Valley** (W 8 or 4): sheeting a truss bay wide along +x, "rest" on a truss's "topLow". Eaves ones overhang the low end by 2 m (over the wall); valley ones stop 0.3 m short, inside a **Valley Gutter** set on the valley's "topLow".
- **Ridge Cap** or **Ridge Monitor** (the louvred vent the doc shows) on a truss's "topRidge".
- **Wall Sheet WxH m** (W 8 or 4, H 4, 3, 2): cladding 0.5 m off the face its "back" socket is on, the foot 1 m under that socket. On the side walls they hang on the masts' outer sockets (every 2 m up, so 4 m sheets stack from the floor to 20.3 m), then the Eaves sheet on the post closes 20.3 to 23.3 m under the overhang.
- **Shed Column 12, 16, 20, 22.5 m** for the end walls: wall sockets every 4 m (1, 5, 9 ... m) on all faces; the 22.5 m one stands on the 0.3 m floor right under a gable truss's bottom chord. **Gable Sheet L m** on a truss's "+x" or "-x" closes the triangle above, in line with Wall Sheets on columns standing under the truss's line.
