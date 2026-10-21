# Cluttered: design notes

Three maps with no straight outer edge (a circle, a bean and an irregular sprawl), built to see how many objects a map can
hold and how much life props and foliage bring. Each map's section below gives its theme, shape, layout, routes and object
counts. The self-test's `Cluttered pack` section checks every map (shape, density floors, nothing outside the outline,
objectives, fairness, a bot match).

## 01 Carousel (small, circle, 68 m across, 3,619 m2, 10 players): abandoned fairground

**Theme.** A fairground left to go to seed in a round clearing, under the `dusk` sky preset: hedge all round, long grass
and weeds pushing through, popcorn boxes, cans and paper everywhere. In the middle the carousel still turns. The game
stalls, food trucks, two bandstands and the caravans are all dressed with balloons, striped awnings and umbrellas.

**Shape.** A `circle` boundary of radius 34 m with a light wobble (`noise` 0.8 m) and a 2.8 m `hedge` edge (57 segments).

**Layout and routes.** Blue comes in through the west entrance gate, Red through the east one. A gate screen and two ticket
booths stop anyone looking out of a base down the middle. There are three lanes and tight loops round the centre:
- **Mid (short).** Gate to plaza, past a ticket kiosk and a candy cart, round the **carousel** (a `rotate` mover: a 9.5 m
  deck you can step onto, with poles and seats, round a solid core that blocks the long line through the middle). Bots
  treat the ride as a pillar and loop round it, players can ride it.
- **North and south lanes.** Each runs past a **bandstand** at x = 0 (1.6 m deck with stairs on three sides, a backdrop and
  footlight lips) with hay bale audience rows in front. One team's end of a lane is the midway (game stalls with
  counters to fight over, an operator watchtower 2.4 m up over the plaza), the other team's end is the food court
  (food trucks, umbrella tables, vending machines, a fuel barrel). So each lane gives Blue one end and Red the other.
- **Flanks.** A caravan with crate steps up to its roof (high ground over the lane and the gate) on one side of each base,
  the backstage strip behind the stalls and trucks along the hedge, and cut-throughs between the lanes at x = +-12.
- **Hills** sit in front of each bandstand (0, -13) and (0, 13), between the stage, the tower and the carousel.
  **Flags** are behind the gate screens (+-31, 0).

**Fairness.** The whole map is one half turned 180 degrees about the centre: every block, stair, mesh, hand-placed prop,
spawn and tree has a twin at (-x, -z), and the scatter uses `"mirror": true`. Team spawn clusters are symmetric about
z = 0 as well, so each hill is the same walk from both teams. The top-down check found no line of sight between Blue and
Red spawns. The self-test's bot TDM leans to Red (about 20-30) because its host is an idle player on Blue, so Blue
fights with four bots against five.

**Density.** The game's `Clutter` keeps solid props off every bot route, which on a map this full left room for only a
dozen. So the solid cover is hand-placed: 24 mirrored piles of crates, bales, bins and pallets that the generator found
in the pockets between the lanes (at least 2.4 m from any box and 2.8 m from the walking lines), on top of the designed
props. `Clutter` adds about 1,850 cosmetic and breakable props (litter, cans, bottles, bags, boxes, planks).

**Object counts** (what the game placed, from the self-test):
- 922 boxes in the physics world (191 map boxes; the rest are prop colliders and tree trunks), plus the carousel's 16 mover boxes.
- 2,118 props: 292 solid, 1,826 cosmetic, 1,304 breakable (266 hand-placed, 1,852 clutter), 1 explosive barrel per side.
- 14,576 plants: 13,743 grass clumps, 664 shrubs, 157 bushes, 12 trees.
- 208 meshes: the carousel canopy, awnings, umbrellas, balloons and signs.
- Bots (10 players, 90 s): about 50 kills in free-for-all and in TDM, no stuck, fallen or escaped bots, worst tick work
  5.4 ms (JIT warm-up ticks aside) against the 8 ms budget.

`tools/cluttered/carousel.py` regenerates the file (with `carousel_boxes.py`, which reads the box geometry back for
the cover search).

## 02 Crescent Bazaar (medium, bean, 137 x 83 m, 9,140 m2, 16 players): walled market

**Theme.** A walled market town in warm sandstone under the `desert` sky preset (hot afternoon haze). A caravanserai at each end, a crescent plaza with a fountain, a covered
souk hall in the middle, and a palace garden with a pavilion and a minaret along the outer curve. Every open space is full
of crates, sacks, stalls, carts, rugs, pots, lanterns, bunting and litter.

**Shape.** A `smooth` boundary through 11 control points, mirrored about x = 0, with a 5 m sandstone wall all round. The
north side bends inwards: the dent's tip reaches z = -22.5 between two lobes that go out to z = -38.5. The south side is
the long convex outer arc.

**Layout and routes.** Blue holds the west lobe, Red the east. Each team's caravanserai is a walled courtyard (gates on all
four sides, domed corner towers, a well, the flag at the back postern). Blind walls in front of the east gates stop anyone
looking out of a base straight down the thoroughfare. There are three east-west routes:
- **Inner curve (short, contested).** The crescent alley runs along the wall under the dent. Because the wall bends
  round the dent's tip, you can't see from one north pocket (the spice yards, with a store shed and sack heaps) to the
  other. The sightline bends with the wall, and you only see the other side once you come round the tip into the plaza.
  The plaza (hill 1, on the fountain basin) is framed by two 6.5 m shophouses and a ring of market stalls.
- **Middle.** The main thoroughfare runs from gate to gate through the souk hall (hill 2): a tiled roof on piers, offset
  doors on the ends, counters on the long sides, and two tall stall islands. These break up the long line through the hall.
- **Outer arc (long, covered).** The garden walk loops round the south past hedged beds, a water channel, orchard trees,
  cypresses and the domed pavilion (hill 3) under the minaret, which you can see from everywhere.
  Cut-throughs link each base to the plaza and to the garden, and a north-south spine joins all three hills.

**Fairness.** Everything solid comes in mirrored pairs (x, z, yaw) and (-x, z, -yaw), from the blocks and hand-placed
props to the scattered cover. Only cosmetic litter is scattered by `Clutter`, which can only mirror through the centre
point. The map generator found no line of sight from a Blue spawn to a Red spawn. The self-test checks that every solid
prop has its twin, and that the walk from each team's spawns to every hill and to the enemy flag is equal (within 10%).

**Object counts.** These are the generator's numbers. The self-test prints what the game actually placed.
- 113 boxes: walls, stalls, the hall, shophouses, hedges, cart and fountain.
- 330 hand-placed solid props, 238 of them breakable. 264 of these are deterministic mirrored scatter in seven zones,
  kept clear of the walking lanes, spawns, hills and flags.
- Up to 1,280 cosmetic clutter props (litter, bottles, bags, boxes, planks, tyres; many break).
- 431 meshes: awnings, domes, pots, rugs, lanterns, bunting and the minaret.
- 26 hand-placed trees, plus grass, shrubs and bushes. The garden is lush (8 grass clumps per m2), with patchy dry
  tufts and wall weeds everywhere else.

`tools/cluttered/crescent_bazaar.py` regenerates the file and a top-down preview PNG.

## 03 Sprawl (large, 32 players)

**Theme.** An overgrown edge of town on a late, hazy afternoon (`clear` sky preset, low sun, light clouds). Six
districts, each with its own colours and props: Blue's tent camp (W), overgrown ruins on a gentle rise (NW), a park
with a reedy pond, a gazebo and hedgerows (SW), a packed market with a clock tower in the middle, a forest with a
logging camp and a hunter's stand on a ridge (N and NE), and a junk field of containers, wrecks and spoil heaps under
a crane (SE), with Red's truck depot at the east end.

**Shape.** A `smooth` outline through 16 uneven control points plus `noise` (2.5 m, 22 m bumps), about 216 x 195 m
and 27,900 m2 inside. A north bay and a south bay bite in from opposite sides at different x, so the map pinches into
a diagonal waist where the market sits; a narrow peninsula sticks out in the NE (the forest point) and a wider one in
the S (the junk spit). The edge is a terrain `cliff` berm all round, and the scenery beyond it changes with the
district: pines behind the forest, broken walls and trees behind the ruins, trees behind the park, stacked containers
behind the junk field and town rooftops beyond both bays (cosmetic meshes, outside the playable area).

**Layout and routes.** Blue spawns at the west end, Red at the east end, 190 m apart. Three lanes run between them:

- **North:** Blue through the ruins (walls, roofless houses, a broken colonnade) and the creek; Red down the forest
  track past the logging camp. They meet at hill 2, just east of the north bay's tip.
- **Centre:** the dirt roads from both camps into the market: shophouse blocks with alleys between them, stalls with
  tables and crates under canopies, two walk-in townhouses with roof access (one nearer each side) and the fountain.
  Hill 1 is the open plaza; the clock tower is the landmark.
- **South:** Blue along the park path past the pond; Red along the junk-field road between the containers. They meet
  at hill 3, just west of the south bay's tip.
- **Flank:** a shallow sandy creek bed runs from the north bay down to the pond on Blue's side, low ground out of the
  market's sightlines; on Red's side the forest's bushes and the junk field's containers play the same role.

Bays and buildings cut every long sightline across the waist; no spawn sees the enemy spawn.

**Balance (not mirrored).** The districts differ, so fairness comes from route length and cover instead of symmetry.
The hills sit on the line of equal walk (found with a 1 m grid walk over the terrain, slopes slowing you above 25
degrees), and the self-test measures it again on the game's own nav graph: averaged over each team's spawns, Blue/Red
walk 99/100 m to hill 1, 110/112 m to hill 2, 106/110 m to hill 3, and 200/199 m to the enemy flag (all within 3%).
Solid cover at least 0.9 m tall within 6 m of each team's routes to the hills: Blue 81, Red 70 (86%).

**Object counts** (from the self-test): 5,048 props (125 hand-placed, 4,923 from 15 Clutter ops: 548 solid, 4,500
cosmetic, 1,949 breakable, 2 explosive barrels well away from spawns), 72,570 plants (57,791 grass, 4,955 bushes,
9,630 shrubs, 167 broadleaf trees, 27 pines), 455 meshes (stall canopies and poles, the fountain, tents, the crane
jib and the scenery outside the edge), 17 glTF models (lamp posts, cones), 597 boxes drawn one by one (buildings,
walls, containers, cars, tree trunks), 1,866 solid boxes in all with the prop colliders. 36 spawns, 17 team spawns a
side, 569 waypoints (spacing 7 plus 18 by hand in tight pockets), a 1,132-node nav graph.

**Performance.** Props, plants and meshes are instanced, chunked and frustum-culled; litter dithers out with
distance; plants thin out with distance and the foliage quality setting. Boxes are still one draw call each, so the
map keeps them under 600 and puts the density into props, meshes and plants. The 32-player bot match (60 s, about
100 kills, no stuck bots) keeps the server tick at 0.13 ms median, 0.44 ms p99 and 1.0 ms of work at worst (budget 8 ms). The smoke run's
`cluttered_large` and `cluttered_large_market` views log fps, props and plants drawn and draw calls.

**Tools.** The map is generated by a Python script (outline, districts, clutter, scenery) and checked with a
top-down preview and grid walk-time analysis before it ever runs in the game.

## 06 Old Town (large, noisy smooth outline, about 141 x 124 m, 19,970 m2, 16 players): overgrown town centre

**Theme.** A small-town centre left to the weeds, at dusk (`dusk` preset, sun at 18:36, a few stars), so the neon, the
flickering street lamps, the cycling traffic lights and the crossing lamps read. Main Street with its shops, the town
square, a town hall and a church, a park with a duck pond, a gas station, a car park, houses with back gardens, a vacant
lot and two railway lines along the edge.

**Shape.** A `smooth` outline through 19 control points with `noise` (1.6 m, 18 m bumps), following the town: a lobe for
the park in the NW, the rail yard pushing out in the NE, the car park in the SE, the gardens in the SW. No visible
`edge` style: the edge is dressed by `townkit.edge_run` in runs that fit what's next to it, tall leafy hedges (cosmetic
meshes outside the barrier, bushes inside) along the park and the churchyard, chain-link fences along the car park and the
rail yard, wooden fence panels and picket fences along the gardens, brick building backs (cosmetic, with windows) where
Main Street runs out of the map. Road-works fences and an arrow board close Main Street short of each spawn.

**Layout and routes.** The street skeleton is point-symmetric about the centre: West Main Street (z -9..-1) is offset north,
East Main Street (z 1..9) south, so no line runs down the town, and they meet across the square. Shop rows line both
sides, each with a back lane behind it (bins and junk), a cut-through passage in the middle of each north/south row, a
side road off each half (Mill Road going south from West Main, Station Road going north from East Main, each with a
traffic-light junction and a level crossing at the edge). The town hall closes the square's north side and the church
its south side, each with front, side and back doors. Blue spawns west of the diner and the cinema, Red east of the
laundromat and the gas station. Routes: Main Street (long, parked cars and street furniture for cover), the back lanes
(close, cluttered), and the park / car park flanks, plus the walk-in shops: the diner, barber, arcade, laundromat,
corner store, two offices, two flats (kitchen and living room with a door between), the gas station kiosk, the hall and
the church all have front and back doors, so fights go through them.
**Hills:** the square round the statue (radius 5.5), inside the town hall (4.5) and inside the church (4.5).
**Flags:** at the west and east ends of Main Street.

**Fairness.** The buildings, lanes and roads are each other's 180-degree twins, the dressing isn't. The generator walks a
0.5 m grid from each team's spawns (Blue/Red: square 83/83 m, town hall 87/91 m, church 91/87 m, enemy flag 178/177 m)
and checks that no Blue spawn sees a Red one; the self-test measures the same on the game's nav graph (within 10%).

**Logical placement** (all in `tools/cluttered/townkit.py`, reusable): street lamps at the kerb every 14 m with the arm
over the road, street trees in tree pits between them, benches with a bin beside them, hydrants, mailboxes, newspaper
boxes and sandwich boards on the shop side, all kept off doorways, junctions and crossings; cars parked nose to tail along
the kerbs and square in the car park's bays, with parking meters beside some; a dumpster against the back wall beside
every back door, wheelie bins next to it, trash bags heaped in front and a ring of litter; crates, pallets, drums and
meters lined up against the back walls, off the walking lanes; AC condensers, HVAC units, turbine vents, satellite
dishes, hatches and chimneys on the flat roofs, kept off the edges; window AC units, vent grilles and electric meters
up the back walls; furniture against the walls facing into each room with an aisle kept from the front door to the back
door (diner counter and stools, booths, kitchen behind; washers and dryers down both walls; barber chairs facing mirrors;
arcade cabinets and slot machines in rows; shelving aisles and fridges; desks with chairs, monitors and filing cabinets;
pews and an altar; chairs facing the hall's table); ducks on and round the pond with reeds, a bench facing it; garden
furniture, sheds and gnomes in the gardens; junk in the vacant lot; pumps on islands under the canopy. Every doorway is
kept clear for 1.1 m each side.

**Foliage.** Grass and weeds everywhere, removed by the no-go rules on intact paving (`Concrete`: Main Street, sidewalks)
and indoors, thinned to 10% on busted paving (`"surfaces": {"Tiles": 0.1, "Plain": 0.1}`: the square, the side roads, the
back lanes, the car park, the forecourt), lush in the park, the gardens, the lot, the churchyard and the yards, weeds
along the foot of the walls, along the kerbs and between the rail sleepers, hedges of bushes along the gardens and the
park, ivy (cosmetic leafy slabs) up the walls.

**Performance.** Buildings are a few hundred boxes (walls with door and window openings, one roof slab each); facades,
paving, road markings, awnings, the canopy, tracks and scenery are instanced meshes; props and plants are instanced and
frustum-culled, litter fades out with distance and animated props hold still past 80 m.
`tools/cluttered/oldtown.py` regenerates the file (and a top-down preview PNG with the walk check).
