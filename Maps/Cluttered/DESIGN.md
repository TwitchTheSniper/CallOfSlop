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

## 04 Cul-de-sac (small, keyhole, 4,228 m2, 10 players): trash day at the end of the street

**Theme.** The turning circle at the end of a quiet residential street on a bright morning (`clear` sky preset, sun at
9:36): a ring of houses round the circle, lawns, drives with parked cars, and every house's bins out at the kerb with
bags, cans and newspapers blowing about. Backyards behind the houses run along the hedge, with fences between them.

**Shape.** A `polygon` keyhole: the circle (radius 35 m round (-14, 0)) plus the street stub (20 m wide, out to x = 40),
with a light wobble (`noise` 0.5 m) and a 2.7 m `hedge` edge. The street's mouth is closed by road works (fence panels,
an arrow board, a generator, cones, a dug-up trench under a road plate); outside the hedge the street carries on and
curves away between the neighbours' roofs (cosmetic meshes).

**Layout and routes.** The map is mirrored about z = 0, the street's axis: Blue has the north half, Red the south.
- **Walk-in house** (one per side): living room and kitchen, an attached garage (car, workbench, shelving, a running
  washer) with its door rolled up, front, back and garage doors and windows to mantle through.
- **Duplex** on the axis at the west end: two mirrored units (living room, kitchen-diner), a doorway between the two
  kitchens through the party wall, back doors onto two yards split by a fence.
- **Closed houses** (two per side), with AC units, meters, dishes, vents, chimneys and a weather vane.
- **Routes:** the ring road round the island, the street, side yards between every pair of houses, and the back route
  along the hedge through all the yards (pool, trampoline, swings, laundry line, kennel, the pocket park with the duck
  pond). **Hill 1** is the planted island in the middle of the circle, **hill 2** the street in front of the road works.
  **Flags** are in the walk-in houses' backyards (-11.5, -+32.2).

**Fairness.** Every solid prop has a twin at (x, -z) turned 180 - yaw (the self-test checks it); the duplex, the island
and the road works are symmetric themselves. The nav walk from each team's spawns: hill 1 33/34 m, hill 2 55/56 m, the
enemy flag 66/63 m. The generator found no line of sight from a Blue spawn to a Red one (houses and fences in between).

**Placement rules** (`tools/cluttered/placement.py`, shared with the other Cluttered generators): lamps, hydrants, signs,
mailboxes and the utility pole stand `along` the pavements facing the road (lamp arms over it); cars are parked along
the kerb or square on their drives; bins stand at the kerb in front of their house and litter and bags are scattered
`around` each bin; AC units, meters and compost bins stand `against` side and back walls; dishes, vents, antennas and
chimneys sit `on_roof` (following the roof slope); kitchens, living rooms and the garage are furnished by `room`
templates (furniture against the walls, the floor in front of every door clear, chairs square to the table, small
things `on_top` of counters and tables); garden things are `scatter`ed in the yards; ducks round the ponds. Solid props
keep off the bot lanes, spawns, hills and flags. Foliage: mown lawn everywhere, longer grass and shrubs out back, a hedge
row along the boundary, flower beds in front of the houses, reeds round the pond, street and yard trees; pavements and
drives are `Tiles` at 10% (weeds in the cracks), the asphalt `Concrete` at 0, nothing grows indoors.

**Object counts** (from the self-test): 1,699 props (580 by hand, 1,119 clutter; 245 solid, 1,454 cosmetic, 966
breakable), about 14,000 plants, 20 trees, 387 meshes (roads, pavements, roofs, glass, the pool, the trampoline, the
scenery outside), 156 boxes drawn (walls, slabs, steps, the pool and trampoline cores, trunks), 12 spawns, 130
waypoints. Bots: about 45 kills in free-for-all and 55 in TDM in 90 s, no stuck bots, under 1 ms of tick work.

**Performance.** Everything numerous is instanced (props, meshes, plants); boxes stay at about 150 draw calls, below
the Carousel's 191, and the plant count is about the Carousel's, so the cost should match it.

`tools/cluttered/culdesac.py` regenerates the file and, given a PNG path, a top-down preview with the lanes.
## 05 Dockside (medium, crescent, 141 x 79 m, 7,807 m2, 12 players): harbour front

**Theme.** A working harbour curving round a bay under a grey `overcast` sky (thick clouds drifting east, a pale
haze). Quays with mooring bollards and life rings, two timber piers flying flags and pennants, a mole with a sea wall
and a lighthouse on its tip, a container yard under a ship-to-shore crane in each horn, warehouses and a harbour
office you can fight through, a fish market street, a waterfront road with lamps and parked vans, back alleys with
dumpsters, and boats, buoys and ducks on the water.

**Shape.** A `polygon` of 156 segments. The land side is a smooth curve (Catmull-Rom through 9 points, sampled every
2.5 m, plus a generator-side wobble of up to 1 m, so it isn't regular) with a cosmetic brick wall along it and town
roofs beyond. The bay side follows the quays exactly: the horns' straight quays, the round inner corners, the two
piers on each side and the mole with its round tip. The bay opens to the north.

**Water.** The engine has no water surfaces, so the sea is terrain: `plateau` features in `cut` mode drop the ground
3 m just outside the bay-side outline (the ground stays level for 0.8 m beyond it, so every terrain cell under a
player is flat), `fill` plateaus put the piers and the mole back, and one big flat cosmetic box mesh at y = -0.9 is the
water surface. Quay-wall faces, coping stones, pier decks and posts are meshes over the drop (`"anchor": "world"`).
The sea is outside the outline, so nobody can walk on it or fall in; the invisible barrier is kept low
(`barrierHeight` 0.1) so shots and sight lines cross the bay. The only things outside the outline are floaters on the
water (moored boats, buoys, a fishing bobber, ducks), placed at the water level with `"anchor": "world"`.

**Layout and routes.** Blue spawns at the back of the west horn's container yard, Red in the east one, flags behind
the containers. Three ways to the middle:
- **Quays (water side).** Down the horn quay under the crane legs, past the horn pier, round the inner corner and
  along the bay-bottom quay to the mole. The **mole** (hill 1, by the lighthouse) has a 2.3 m sea wall down its spine,
  so you can't see from one basin to the other at eye height.
- **The waterfront road.** Out of the yard, down the horn road between parked vans and the quay, round the bend and
  along the front to the zebra crossing (hill 2, between the market mouth and the mole root).
- **Buildings and alleys.** Through the depot lot and the harbour office (reception, a back office), down the side
  lane, through the warehouse (store floor with shelving and a forklift, an office and a break room behind
  partitions; doors to the road, the side lane, the market and the alley) or along the back alley, into the **fish
  market** street (hill 3) between the two warehouses.

**Fairness.** Everything solid is mirrored about x = 0: (x, z, yaw) and (-x, z, -yaw). The generator finds no line of
sight from a Blue spawn to a Red spawn. The self-test checks the twins and the walks from each team's spawns to every
hill and the enemy flag (within 10%: blue/red 91/97, 73/79, 83/88 and 153/150 m on the game's nav graph).

**Placement rules** (`tools/cluttered/placekit.py`, reusable, used by `dockside.py`):
- Dumpsters stand with their backs to a building wall in the alleys and yards (`dumpster_spot`), with trash bags piled
  round them, flattened boxes and litter further out and wheelie bins beside them; litter rings round benches, the
  hot dog cart and the picnic table (`trash_ring`).
- Street lamps (arm over the road), hydrants, parking meters, bins, a bus stop and benches along the kerbs at fixed
  spacing, facing the road (`kerb_row`); vans, pickups and box trucks parked nose to tail along the kerb, turned with
  the road, with the loading bay kept clear (`park_row`).
- Bollards, life rings, flags and pennants along every quay and pier edge, facing the water (`edge_row`).
- HVAC units, condensers, turbine vents, skylights, antennas and dishes on the flat roofs, clear of the edge
  (`roof_kit`); vent grilles, window AC units and meters on the outside walls (`wall_kit`).
- Rooms furnished against their walls and clear of their doors, desk and table things on the tops (`room`): two
  offices (desks, chairs, filing cabinets, phones, laptops, a photocopier, a whiteboard), a reception, a break room
  (fridge, vending machines, counter, sink, table and chairs, kettle and microwave on top) and the store floor.
- Boats moored alongside the piers and quays, buoys out in the bay, ducks on the water and on the quay by the water
  (`afloat`).
- Foliage: none indoors or on the road, tiles and decks; busted concrete grows weeds (9%); `include` strips along the
  foot of the land wall and in the quay corners grow long grass, shrubs and bushes; the depot lots are overgrown; trees
  by the lots and along the road.
The self-test checks the rules too: every dumpster by a wall with trash bags round it, desk and kitchen things under a
roof, rooftop plant on a roof, floaters over water.

**Pack props** (`Maps/Cluttered/Props/Harbour`, written by `tools/cluttered/harbour_props.py`): Fishing Trawler,
Harbour Launch and Rowing Boat (cosmetic; they bob and roll on the GPU), Mooring Bollard, Life Ring Post, Upturned
Dinghy, Lobster Pot Stack, Fish Counter, Fish Box Stack, Fish Box, Fishing Net Pile and Rope Coil.

**Object counts** (from the self-test): 2,713 props (703 hand-placed, 2,010 from 26 cosmetic Clutter ops; 511 solid,
2,202 cosmetic, 1,281 breakable, 24 afloat). By set (solid/cosmetic): Harbour 96/167, Industrial 110/98, Street 64/0,
Office 54/14, Rooftop 40/16, Animated 39/10 (flags, pennants, the lighthouse, ceiling fans, turbine vents, the
gantry hook and freight hoist, buoys, the bobber, ducks on the water), Furniture 24/0, Vehicles 20/213 (with the
loose wheels, doors and jerry cans), Urban 14/0, Nature 14/0, Kitchen 10/4, Signage 8/2, Utility 8/20, Misc 0/93,
Debris 10/1,565. About 16,400 plants (no trees scattered; 22 hand-placed), 795 meshes (water, quay walls, paving,
the crane, the wall and the town beyond, awnings), 109 boxes drawn one by one (buildings, containers, crane legs,
the sea wall), 22 tree trunks.

**Performance.** Well under Sprawl on every count (about half the props, a quarter of the plants, a fifth of the
boxes), so it should run faster than Sprawl's ~1,700 fps on the user's PC. The bot match (12 players, 60 s) keeps the
worst server tick under 1 ms. The smoke run's `cluttered_dockside` (the bay from above the west horn) and
`cluttered_dockside_street` (the waterfront road at eye height) views log fps, props and plants drawn.

`tools/cluttered/dockside.py` regenerates the map and a top-down preview PNG (it also checks the quay edge, that the
ground inside the outline is level, and spawn-to-spawn sight lines).
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
## 11 Deepcut Mine (large, noisy smooth outline, about 179 x 120 m, 18,300 m2, 16 players): mine site with tunnels

**Theme.** A working mine on a dusty afternoon (`desert` preset turned dustier: a tan horizon, a strong ochre haze, the
sun at 16:12). Down low on the west, the workers' camp (portable cabins: a canteen with tables and vending machines, a
locker room, the site office, toilets, a generator and floodlights) and the processing plant (a walk-in shed with
conveyor rollers, a jaw crusher, shelving and a forklift; an ore hopper on legs you can walk under, stockpiles, the rail
line with mine carts coming down from the North Drift). In the middle, the open pit and two spoil dumps either side of
it. Up on the east, a terraced massif: bench 1 (4.5 m), bench 2 (9 m) and the top (13.5 m) with the headframe over the
shaft (its two sheave wheels turn), the winder house with its drum and ropes, and the second camp. Haul trucks,
excavators, conveyors, floodlight masts, timber stacks, fuel tanks in a bund, and the explosives magazine on bench 1.

**Shape.** A `smooth` outline through 17 control points with `noise` (1.5 m, 18 m bumps), wider at the west end. The
edge is a terrain `cliff` berm (9 m over 7 m) all round, the natural edge of a mine cut into the hills.

**Not mirrored.** The two halves look and play differently: Blue holds the low, open camp and plant side (spawns at
y = 0), Red the high side, the top of the massif (spawns at y = 13.5). Red has the height: bench 1 and the top's rim
look down into the pit and the conveyor yard. Blue has the cover (cabins, the plant, stockpiles, the haul truck and the
excavator in the pit, the dumps) and the tunnels, which give the low side its ways up.

**Tunnels** (the engine's terrain is a heightfield, so they're cut and covered: a terrain `ramp` in `cut` mode carves
the trench, with a flat floor 4.4 m wide and rock walls, and a box roof closes each stretch at the hillside's own
height, so the surface carries on over the top; dark timber lagging on the walls and ceiling, a timber set every 2.5 m,
a flickering mine lamp every 9 m; nothing grows inside, and the plants on the hillside grow on the roofs too):

- **North Drift** (69 m): from the plant's rail head at ground level (y = 0) under the north dump, climbing gently to
  come out on bench 1 (y = 4.5), with the rails along its floor. A side **adit** branches off half way and drops out
  into the conveyor yard (y = 0).
- **South Adit** (48 m): from the pit floor (y = -4) under the haul road and the south dump, out onto bench 1 (y = 4.5).
- **Main Haulage** (25 m): from bench 1 into the hill to the **shaft station**, a timbered junction room under the
  headframe. Two branches leave it: **Shaft North**, climbing out onto bench 2 (y = 9), and **Shaft South**, out onto
  bench 1 south. The **shaft cage** (an elevator) runs from the station up the shaft to the top, through the middle of
  the headframe (players only; bots take the ramps and tunnels).

Every tunnel has waypoints every 3 m along its floor, so the bots' nav graph runs through them (the self-test checks a
nav node, a roof and no plants at ten points along them).

**Layout and routes.** Haul roads (dirt ramps, about 17 degrees) join the levels: into the pit from the plant (west) and
from the conveyor yard (north-east), the pit's south ramp, bench 1 down to the yard and to the strip east of the pit,
both dumps from both sides, bench 1 up to bench 2 at each end, and the main haul road straight down the massif from the
top to bench 1, with a blast wall at its head so there's no line down it. A low berm runs along the top's west rim.
**Hills:** the pit's north-east corner, the conveyor yard, and the south dump's east end. **Flags:** in the camp and
behind the winder house.

**Balance (by measurement, not mirroring).** Red's spawns sit on the west half of the top, Blue's in the camp, and the
hills sit on the line of equal walk, found by the generator on a 0.5 m grid over the terrain with the tunnels as a
second layer (Blue/Red: pit 91/95 m, yard 84/84 m, south dump 102/97 m, enemy flag 180/179 m). Red needs 17.5 m of
descent to reach the pit (about 60 m of ramp), so its spawns are closer to the middle than Blue's. The self-test measures
it again on the game's nav graph (within 10%), checks that no Blue spawn can see a Red one (the processing shed, the core
shed and the top's berm and blast wall are in the way), and compares the cover near both teams' routes to the hills.

**Explosives.** Four explosives crates at the magazine on bench 1 (three inside, one on the apron: shoot it and the rest
go too), and three explosive barrels in the yard, the pit and on bench 2, all well over 10 m from any spawn.

**Pack props** (`Maps/Cluttered/Props/DeepcutMine`, written by `tools/cluttered/mine_props.py`): Mine Cart, Rail Track,
Haul Truck, Excavator, Headframe (animated sheave wheels and a beacon), Inclined Conveyor (ore riding the belt, drums
turning), Jaw Crusher (flywheels and jaw), Ore Hopper, Timber Set, Mine Lamp (flickers), Timber Stack, Explosives Crate
(explodes), Danger Sign, Fuel Tank and Floodlight Mast (lamps burning).

**Object counts** (from the generator; the self-test prints what the game placed): 215 boxes (72 of them tunnel roofs,
the rest cabins, sheds, walls and stockpile cores), 377 hand-placed props and about 3,700 asked of 32 Clutter ops (the
ones that reach over tunnels are cosmetic only, so nothing solid lands on a tunnel floor), 277 meshes (tunnel lagging,
portal frames, stockpile cones, ropes, floors), dusty grass with patches everywhere, scrub on the dumps, pines round the
camps.

**Performance.** Fewer boxes than Old Town, a similar prop count and fewer plants, so it should run at least as fast;
animated props (the sheaves, conveyors, crushers, generators, lamps) freeze past their freeze distance. The smoke run's
`cluttered_deepcut` (the pit and terraces from above the camp) and `cluttered_deepcut_tunnel` (inside the North Drift)
views log fps, props and plants drawn.

`tools/cluttered/deepcut.py` regenerates the map and, given a PNG path, a top-down preview with the equal-walk line and
any spawn sight lines (it uses `terrain.py`, the Python port of the game's terrain generator).
