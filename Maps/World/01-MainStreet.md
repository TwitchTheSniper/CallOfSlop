# 01 Main Street (World Tour, USA; small, 12 players)

**Theme.** Prairie Hollow, a small Midwestern farm town on a late-summer afternoon (`clear` sky, sun at 17:12, warm
haze). One Main Street of shops with US flags and banners, alleys behind them, a stopped freight train at the rail
crossing, and corn all round, climbing a gentle rise to a shelter belt of trees. Every shop name and logo is invented
(Dot's Diner, Main St. Drug, Prairie Savings Bank, Rosa's Pizza, Hollow Grocery, Starline gas, the Hollow Grain Co-op).

**Shape.** A noisy `polygon` (about 97 x 77 m with its lanes, 5,480 m2, Small) with a `cliff` edge: the ground rises
4.5 m past the outline, with a tree line on top. Two dead-end farm lanes poke out north and south along Elm Street's
farm track, and short stubs east and west where the road runs on out of town.

**Terrain.** The town tilts down to the south (north alley about 2.3 m, Main Street 1 m, south alley about 0 m), so
the north storefronts sit on pads cut into the slope and the south ones on built-up pads; Elm Street is the sloping
street. The corn climbs to the north rise, a creek runs along the south field, and the rail line runs level (2.2 m):
through a cutting in the north and on an embankment in the south, with Main Street ramping up over the crossing.
The town square is a raised lawn with a retaining wall. Roads are painted rock (asphalt), sidewalks sand (pale
concrete), fields and alleys dirt.

**Layout and routes** (Blue west, Red east; not mirrored, fairness measured):
- **Main Street** (two lanes, parallel parking, sidewalks with lamps, hydrants, benches, meters, newspaper boxes):
  north side Barber Shop and Dot's Diner (west), Prairie Savings Bank and Hollow Grocery (east); south side Rosa's
  Pizza and Main St. Drug (west), the town square (bandstand gazebo, memorial obelisk, benches, trees) and the
  Starline gas station (east). Every building is a walk-in, furnished `Building` op. A double-parked bus and a box
  truck break the long view down the street.
- **Elm Street** crosses at x = 0 (four-way stop, crosswalks, STOP markings), and carries on as farm tracks into
  the lanes (a hay wagon and a tractor on them break the long north-south view).
- **Alleys** behind both rows: dumpsters and bins by the back doors, sheds, a pickup, utility poles, posters and
  graffiti, picket and chain-link fences with gaps into the corn; a squeeze behind the bank.
- **The corn** (110 patches of 3 x 3 m, taller than a player) fills the outer layer: close-range flanks round both
  alleys, with trampled paths, clearings with scarecrows, an irrigator and a tractor.
- **Blue (west):** the co-op farm yard: two grain bins, tractor, hay wagon, fuel tank, round bales, farm stand,
  welcome sign. **Red (east):** behind the freight train (an open boxcar to climb through, a covered hopper across the
  crossing, a boxcar, 1.6 m gaps between cars), crossing lights and crossbucks.
- **Hills:** the crossroads (0, 0) and Elm Street in the north alley (2.5, -19.7). **Flags:** at the back of each
  team's spawn. Generator walk check: hill 1 44/44 m, hill 2 49/49 m, enemy flag 89/89 m; no spawn-to-spawn sight.

**Concealment (engine addition).** Corn patches are cosmetic (walk and shoot through) props with the new
`"concealment": true` key: bots' line of sight is blocked by 1.2 m or more of concealing props (unless within
2.5 m), so the corn hides you from bots as it does from players (`Source/Props/MapConcealment.cs`, one line in
`Bots.HasLineOfSight`). Corn is props, not foliage, because foliage grass fades at 22-60 m and thins on Low quality.

**Assets.** General props (`tools/world/mainstreet_props.py`): `Props/Farm` (Corn Patch A/B, Trampled Corn, Field
Scarecrow, Row Crop Tractor, Hay Wagon, Big Round Bale, Grain Bin, Split Rail Fence, Wheel Line Irrigator, Roadside
Farm Stand, Stock Tank, Pumpkin Pile, Farm Fuel Tank), `Props/Rail` (Railroad Track 6m, Railroad Crossbuck, Freight
Boxcar, Freight Boxcar Open, Covered Hopper Car), `Props/Town` (Bandstand Gazebo, Memorial Obelisk), all shown on the
Dev pack's **Farm and Rail Props** map. Map decals (`tools/world/mainstreet_decals.py`): 29 PNG decals in
`Maps/World/Decals/MainStreet` (shop signs split into side-by-side pieces, welcome sign, water tower and co-op
lettering, posters, STOP and RR pavement markings). Scenery outside the outline (cosmetic meshes): the water tower,
the grain elevator and bins, the tree line, the road running on.

**Counts** (generator): 7 buildings, 4 boxes drawn one by one, about 290 hand-placed props plus 110 corn patches and
17 Clutter ops, about 240 meshes, about 110 decal ops, 12 spawns (6 + 6 team), AutoWaypoints at 4 m plus 16 by hand.

`python3 tools/world/mainstreet.py . preview.png` regenerates the map and a top-down preview and prints the walk and
sight checks. The self-test section **World Tour: Main Street** checks size, terrain, objectives, corn concealment,
spawn sight lines, fair walks, reach (every building room, the corn, behind the train).
