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
## 08 Truck Stop (medium, noisy smooth outline, about 121 x 81 m, 8,750 m2, 12 players): desert highway truck stop at dusk

**Theme.** A truck stop on a desert highway at dusk (`dusk` preset with an orange desert haze and the first stars; the
sun sits low in the north-west, so neither team looks straight into it along the east-west fights). A two-storey
L-shaped motel round a courtyard with a drained pool, a diner, a gas station with its convenience store, a water tower,
a tire shop and a big lot full of semis and trailers, with the highway along the south edge. The neon MOTEL sign lights
letter by letter with a chasing arrow and a flickering NO in VACANCY, the billboards' lamps flicker, the pumps' price
panels and the ice machines glow. Terrain is flat desert (sand) inside a ranch fence, with red mesas, low hills and buttes
beyond it.

**Shape.** A `smooth` outline through 22 control points with `noise` (1.2 m, 16 m bumps) and a weathered wooden `fence`
edge. It follows the buildings: tight behind the motel, the diner and the tire shop, a long straight run along the far
side of the highway, the south-west and south-east corners cut off diagonally, so the highway leaves the map at both
ends (closed by water barriers, road works signs and arrow boards).

**Layout and routes.** Blue spawns in the service yard behind the motel (west), Red at the east end of the truck lot.
- **The motel (Blue's side, vertical).** Two wings of rooms (8 and 5 a floor) with 2 m walkways under balconies, joined
  at the lobby (reception, TV corner, ice and vending machines, a back door to Blue's yard). 13 rooms are walk-in and
  furnished (bed, TV, bedside table and lamp, a bathroom behind a partition with toilet, sink and shower), the others are
  locked (painted doors, curtained windows). A breezeway runs through the west wing from the yard to the walkway. Two
  staircases lead up to the balconies (south end of the west wing, east end of the north wing), which look over the
  courtyard, the car park and the approach from the middle. The courtyard has a drained pool (a terrain pit: 1 m deep,
  1.8 m at the deep end, a ramp cut into the shallow end, a step at the drop) with loungers, umbrellas and a tired lawn.
- **The middle (contested).** The diner (booths along the front windows, a counter with stools, the kitchen behind,
  dumpsters by its back door), the diner lot, the water tower, the convenience store (shelving aisles, fridges, the
  counter) and the gas canopy over two pump islands, with the closed highway beyond (a jack-knifed semi, a crash with a
  police car and cones, street lamps along the sidewalk, two billboards).
- **The truck lot (Red's side, cover maze).** Three double rows of trailers in bays (box trailers, reefers, flatbeds
  with lumber, pipes or machinery, half of them coupled to semi tractors), aisles between the rows, a cross aisle, a
  truck fuel island under its own canopy with a semi filling up. The flatbeds are a mantle up (1.4 m deck, cargo on top),
  two box trailers have crate steps up to their roofs (3.76 m). The tire shop (three bays with a car up on a lift, tire
  racks, tool chests, a tire changer, an office) has a fire escape on its east wall up to its roof, which looks over the lot.
- **Hills:** under the gas canopy (between the pump islands), the diner lot, and inside the diner by the counter (a small
  zone, 3 m). **Flags:** at the back of each team's spawn.

**Balance (not mirrored).** Blue's motel gives height (balconies 3.4 m up over the courtyard and the car park), Red's
lot gives cover (trailers you can only see over from their roofs) and flanks (aisles, gaps between paired trailers, the
garage roof). The hills sit on the line of equal walk, found by walking a 0.5 m grid from each team's spawns in the
generator (Blue/Red: canopy 60/59 m, diner lot 57/58 m, diner 67/63 m, enemy flag 119/119 m), and the self-test measures
it again on the game's nav graph, averaged over each team's spawns: canopy 70/69 m (+0.7%), diner lot 69/70 m (-2.4%),
diner 83/86 m (-2.9%), enemy flag 131/130 m (+0.7%), all well inside the 10% the test allows. Solid cover within 6 m of
each team's routes to the hills: Blue 117, Red 123 (95%; the test wants 70%). No Blue spawn sees a Red one at eye height
(the motel, the store and the trailers are in the way). Bots reach every level (nav nodes in the main nav area: 12 on the
balconies, 4 on the pool floor, 33 on the tire shop roof, 27 on trailer roofs). In the 12-player bot matches (90 s) the
free-for-all had 70 kills and team deathmatch 47 (Blue 28, Red 19, with the idle host on Blue), no stuck bots, worst
server tick 0.75 ms.

**Placement** (with `townkit.py`): rooms furnished against their walls with the doorways and an aisle clear; bins and a
dumpster by every back door with trash bags and litter round them; AC units, vents and dishes on the flat roofs, wall
units on the outside walls; pumps on their islands under the canopies; cars square in their bays in the motel car park
and the diner lot; street lamps along the sidewalk every 15 m with the arm over the road; trailers aligned with their bays
and tractors coupled to them; junk (pallets, drums, tire stacks, crates, cable spools) scattered off the walking lanes;
litter, tires, wheels and cans scattered by `Clutter`. Explosive barrels (4) stand in the tire shop and the truck lot,
more than 10 m from every spawn. Foliage: dry desert grass and sage everywhere (none indoors or on intact paving, 10% on
the busted asphalt of the lots), thicker scrub along the edges and the motel's back wall, a lawn in the courtyard, nothing
in the pool, a few hand-placed trees.

**Pack props** (`Maps/Cluttered/Props/TruckStop`, written by `tools/cluttered/truckstop_props.py`): Semi Tractor (red,
blue, white), Box Trailer, Box Trailer Rusty, Reefer Trailer, Flatbed Lumber, Flatbed Pipes, Flatbed Machinery, Car Lift,
Car Lift Raised (walk under the car), Tool Chest, Tire Changer, Air Compressor, Tire Rack, Diner Booth, Ice Machine,
Housekeeping Cart, Pool Ladder, Diesel Dispenser, Motel Neon Sign, Billboard Diner and Billboard Motel. The map also uses
Old Town's fuel pump (`Maps/Cluttered/Props/OldTown`).

**Object counts** (from the self-test): 3,140 props (517 by hand, 2,623 clutter; 417 solid, 2,723 cosmetic, 1,467
breakable, 4 explosive, 53 animated). By set (solid/cosmetic): Truck Stop 48/2, Household 81/24, Industrial 70/185,
Animated 24/11, Kitchen 24/5, Street 25/0, Rooftop 23/16, Vehicles 23/123, Office 22/4, Garden 18/1, Nature 14/0, Urban
14/0, Utility 14/30, Old Town 8/0, Signage 5/6, Furniture 3/0, Misc 0/94, Debris 1/2,222. 14,013 plants (12,459 grass,
1,465 shrubs, 73 bushes, 16 trees), 566 meshes, 241 boxes drawn one by one (walls, slabs, stairs, pillars, tree trunks;
1,110 solid boxes with the prop colliders), 22 spawns (8 + 8 team spawns, 6 more for free-for-all), 202 waypoints, a
1,066-node nav graph.

**Performance.** About two thirds of Old Town's props and a third of its plants (Old Town draws 4,908 props and about
31,800 plants at about 1,690 fps on the user's PC), with fewer boxes than Old Town or Sprawl, so it should run at or above
Old Town's frame rate. Props, meshes and plants are instanced; animated props freeze past 80 m (the neon sign and the
billboard lamps at 160 m). The 12-player bot matches keep the worst server tick under 1 ms. The smoke run's
`cluttered_truckstop` (the whole stop from above the highway) and `cluttered_truckstop_street` (the courtyard from the
west balcony) views log fps, props, plants and draw calls.

`tools/cluttered/truckstop.py` regenerates the map and, given a PNG path, a top-down preview with the spawns, hills and any
spawn-to-spawn sight lines; it prints the walk check and the counts.

## 12 Powder Keg (large, noisy smooth outline, about 195 x 130 m, 21,000 m2, 16 players): a warehouse full of explosives

**Theme.** A distribution warehouse under a grey `overcast` sky, stocked with things that go bang: hundreds of red fuel
barrels on pallets, in lines along the aisles and on the shelves of the racking, propane bottles and three bulk propane
tanks, cages of gas cylinders, yellow flammables cabinets, chemical drums and IBC totes.

**Shape.** A `smooth` outline through 19 control points with `noise` (1.8 m, 20 m bumps). No visible edge style: the
edge is dressed in runs (chain-link fence panels round the yards, tall hedges along the north and east, building backs
behind the container stack).

**Layout.**
- **The hall** (64 x 52 m, roof at 9.5 m): six back-to-back rows of 4.4 m pallet racking running east-west (loaded
  bays are solid cover, about a quarter are open bays you can see and shoot through, with barrels on their shelves), a
  north-south cross aisle, and a 12 m staging band through the middle with four staggered racking islands (so no line
  runs from a dock door to the dispatch door). Hill 1 is in the staging band.
- **The annex** (the hall's east 14 m, two storeys): break room, dispatch and maintenance shop downstairs; two offices
  and an open mezzanine upstairs (deck at 4.8 m), an inside stair from dispatch and an outside steel stair on the east
  wall. **Two catwalks** run west from the mezzanine over the two racking aisles, 0.4 m above the rack tops, with yellow
  railings (only the top rail collides: they keep you on but bullets fly through) and stairs down at their west ends.
  The catwalks and stairs sit on the game's nav grid lines so bots find them.
- **West, the loading yard (Blue):** four dock doors (semi trailers backed onto two, a gap behind them), the fuel depot,
  a container stack two high in places, a truck park and a guard hut, and parked trucks screening the spawn yard.
- **North:** the walk-in chemical store (hill 2 is in the road in front of it) and the propane tank farm in a low bund.
- **South:** the rail spur with a boxcar and a flat wagon of crates, the loading canopy at the south roller door,
  stock in the yard (hill 3).
- **East (Red):** the staff car park, the entrance canopy, the outside stair up to the offices.
- **Flags** are at the back of each spawn yard.

**Asymmetry and balance.** Nothing is mirrored. Blue's side is outdoors and full of cover (trucks, containers,
trailers) with two long flanks round the hall; Red's side is compact with the high ground (the offices' windows over the
hall, the mezzanine, both catwalks). The catwalks are exposed (railings don't stop bullets) and Blue can climb them from
their west ends, so the high ground costs exposure. Fairness is by measurement: the generator walks a 0.5 m grid from
each team's spawns and puts the hills on the line of equal walk (Blue/Red: hall 81/83 m, north 91/91 m, south 89/88 m,
enemy flag 180/178 m), and the self-test measures it again on the game's nav graph (within 10%), checks that no Blue
spawn sees a Red one (eye to eye through the world), counts the cover within 6 m of each team's routes to the hills
(held to a floor of 50%: Blue has more by design) and how much of each route runs on raised floors.

**Explosives.** About 285 explosive props from the global `Props/Volatile` set, all with a short fuse: a hit that
breaks one lights it (sparks and a clank, it stays whole), it goes off 0.4-1.5 s later (by type), and a blast lights its
neighbours with a 0.12-0.32 s fuse, so clusters and lines ripple rather than pop in one tick: lines of barrels run along
the staging band, down the cross aisle, past the trailers and the boxcar, to be lit towards the enemy. None is closer to
a spawn than its blast radius plus 9.5 m (15 m for a barrel, 20.5 m for a bulk tank). Cost under mass chain
reactions is bounded in the game code: at most 4 fused props go off per server tick (the rest wait a tick), at most 20
prop fireballs show in any 0.6 s (more still hurt and break things, with debris and sound), and debris has its fixed
pool. The self-test shoots one barrel in the biggest cluster and times every tick of the ripple.

**Logical placement.** Barrels on pallets at the dock staging and in the chemical store, on the racks' open shelves, in
lines where they'd wait to be loaded; propane at the forklift charging bay and the tank farm; gas cylinders by the
shop's welding bay; flammables cabinets against walls; forklifts and pallet jacks in the aisles; conveyor, packing
tables and shelving in dispatch; a kitchen, tables and vending machines in the break room; desks, monitors, filing
cabinets, a photocopier, a safe and a conference table upstairs; high-bay lamps under the roof (a few flicker); trailers
at the docks with their cabs hitched; dumpsters by the doors; lamps along the car park. Foliage: none indoors (the
automatic rule), none on the intact roads and car park, about 10% on the busted yard concrete, long grass, shrubs and
bushes in a band along the edge, weeds at the foot of the walls, trees round the edge.

**Pack props** (`Maps/Cluttered/Props/PowderKeg`, written by `tools/cluttered/powderkeg_props.py`, which also writes the
global `Props/Volatile` set): Pallet Racking Bay (loaded, wrapped and open), Safety Railing, Semi Trailer, Pallet Jack,
Dock Bumper, Hazard Placard, High Bay Light (steady and flickering).

**Object counts** (from the self-test): 4,923 props (893 by hand, 4,030 from Clutter; 843 solid, 4,080 cosmetic, 2,279
breakable). By set (solid/cosmetic): Debris 8/3,150, Misc 0/410, Industrial 69/273, Volatile 304/0, Warehouse 227/41,
Vehicles 29/194, Urban 119/0, Office 40/8, Street 23/0, Furniture 10/0 and a few others. 288 explosives (263 Red Fuel
Barrels, 9 Propane Tanks, 3 Propane Bulk Tanks, 4 Flammables Cabinets, 3 Gas Bottle Cages, 3 Gas Bottles, 3 Jerry Cans).
34,890 plants (31,197 grass, 3,087 shrubs, 587 bushes, 19 trees; 7,479 dropped indoors and 19,255 on paving by the no-go
rules), 341 meshes, 241 boxes drawn one by one, 26 spawns (9 + 9 team spawns), a 1,438-node nav graph with 179 nodes
above 2 m (mezzanine, catwalks, stairs).

**Measured** (self-test): nav walk Blue/Red hill 1 93/92 m, hill 2 101/99 m, hill 3 100/96 m, enemy flag 188/186 m (all
within 4%); no spawn-to-spawn sight line; cover near the routes to the hills 114/98 (86%). Bots, 16 players: 119 kills
in 90 s of free-for-all and 37 in team deathmatch, no stuck, fallen or escaped bots, worst tick about 1 ms. Chain reaction
(one barrel shot in the biggest cluster, 21 explosives down the cross aisle): it lit, went off after 0.62 s, 19 of the 21
went off in a ripple, at most 15 fireballs at once, ticks p99 0.18 ms.

**Performance.** About Old Town's counts (4,908 props, 31,758 plants) with fewer boxes, so expect about the same frame
rate (~1,600-1,700 fps on the user's PC). Racks, railings, trailers and every barrel are instanced props; lamps and
flickering tubes are animated props that hold still past 80 m. The smoke run's `cluttered_powderkeg` (the map from the
south-west) and `cluttered_powderkeg_hall` (the staging band at eye height) views log fps, props and plants drawn.

`tools/cluttered/powderkeg.py` regenerates the file (and a top-down preview PNG with the walk and sight-line checks).
## 07 Supermarket (medium, noisy polygon, about 101 x 99 m, 8,135 m2, 12 players): SLOP MART after closing

**Theme.** A run-down supermarket on a grey evening (`overcast` preset, sun at 17:36), most of the map indoors under one
roof (6.5 m): rows of gondola shelving stocked with cans, cereal, bottles, snacks and household goods, end caps with SALE
headers, hanging blue aisle signs and department signs, pendant fluorescent lights (about one in eight flickering, more in
the stock room), tilted produce bins and crates, bakery racks and bread tables, open dairy chillers, a deli counter with
its prep kitchen behind it, glass-door freezer cases and open freezer islands, ten checkout lanes (belt, scanner,
register, sweets rack, a lit lane number), the front end (customer service desk, nested carts, vending and gumball
machines), and over the deli a staff mezzanine with an office and a break room looking out over the floor. Behind the
shop floor: the stock room (pallet racks, pallets of goods and water, roll cages, staff lockers, a forklift, a baler with
its cardboard bales, a walk-in cooler) and the loading dock (three open dock doors, a closed one with a box truck backed up
to it, dumpsters with trash round them, a smokers' bench). Outside: the car park in front (bays, planted islands with
trees and lamps, cart corrals, abandoned carts, a bus stop, the price pylon), a service alley down the west side (a
delivery truck, AC condensers, the bins at the side doors) and the dock apron. The `SLOP MART` sign is lit over the
entrance.

**Shape.** A `polygon` of 23 corners with `noise` (0.8 m, 9 m bumps), 107 segments: it hugs the store's blank east wall,
takes in the west alley and the dock apron, and bulges round the car park with bites in its south and west edges. No
`edge` style: `townkit.edge_run` dresses it per section, a tall hedge round the car park, chain-link fence along its east
side and along the dock, brick backs of the neighbouring buildings across the alley.

**Layout and routes.** Blue spawns in the far drive aisle of the car park (z about 44), Red on the dock apron (z about -40).
- **Front (Blue's way in):** the entrance and exit doors under their canopies, three smashed shop windows with 1 m sills
  to mantle over, then the checkouts (waist-high counters you can vault) and the front aisle. The west alley is Blue's
  long flank: past the delivery truck to the produce fire exit or on to the stock room's side door.
- **Back (Red's way in):** three dock doors into the stock room, then the bakery door, the main double door (a rack
  island stops anyone looking straight from the middle dock door through it) or the deli's back door under the
  mezzanine. Red also owns the back stair from the stock room up to the break room.
- **Shop floor:** eleven rows of shelving (two runs each, a cross aisle between them) form ten aisles with a back aisle
  and a front aisle; the middle of row 5 is taken out for the **centre court** (hill 1, radius 3.3, two roof columns for
  cover). **Produce** (hill 2) is the open west side among the bins, **frozen** (hill 3) the east side between the freezer
  islands and the freezer wall.
- **Mezzanine:** 3.6 m up over the deli, a walkway behind a 1.05 m parapet (cover and the view over the shelves) in front
  of the office and break room; the main stair climbs from the back aisle, the back stair from the stock room.
- **Flags:** Blue's at the back of the car park, Red's on the dock apron behind the middle dock door.

**Balance (not mirrored).** The two ends are different places on purpose: Red's side is closer to the mezzanine (the high
ground over the floor, with its own back stair), Blue's side has more ways in (two doors, three windows, the alley flank)
and more low cover on its way to the hills (checkout counters, promo stacks, cart nests). The generator measures the map
instead of mirroring it: a 0.5 m grid walk from every spawn (anything 0.55-1.7 m tall can be mantled at 2 m extra, like
the game's nav graph) puts the hills and spawns where both teams have the same walk (hill 1 51/51 m, hill 2 54/54 m,
hill 3 61/64 m, the enemy flag 93/94 m, Blue/Red averaged over the team spawns), and checks that no Blue spawn can see a
Red one. The self-test (`SelfTestCluttered.Supermarket.cs`) measures the same on the game's nav graph (within 10%): hill 1
54/52 m (+3%), hill 2 56/56 m (0%), hill 3 61/65 m (-6%), the enemy flag 98/95 m (+3%); solid cover within 6 m of each
team's routes to the hills 143/169 (85%, at least 70% needed); 0 spawn-to-spawn sight lines (real ray casts at eye
height); 12 nav nodes on the mezzanine in the main area; and that the map really isn't mirrored (0% of the 505 solid props
have a twin across x = 0, z = 0 or the centre; at most 35% allowed). Those checks are this map's own, so the twin checks of
the mirrored maps stay as strict as they were. Hill 3 leans to Blue because Red's way to the frozen section goes round the
deli counters; Red gets the mezzanine and the back stair in exchange.

**Placement rules** (`townkit.py` plus the map's own code in `supermarket.py`): shelves in rows square to the walls with
end caps facing out of every run; fittings against the walls with their backs to them (chillers and bakery racks on the
back wall, freezer cases on the east wall, the deli's kitchen on the partition behind the counters); checkouts in a row
across the front with the belt towards the aisles and the register towards the doors; nested carts by the doors, cart
corrals in car park bays, lamps and trees in the planted islands; dumpsters with their backs to the dock wall and the
alley wall beside a door, trash bags heaped round them and litter around (`bins_by_door`); AC condensers, meters and
vents on the outside walls, rooftop plant on the roof (`roof_kit`, `wall_kit`); office and break room furnished on the
mezzanine floor (desks, chairs, monitors, filing cabinets, a safe, a table and chairs, a fridge, a counter with a
microwave and kettle, lockers, a sofa); ceiling lights and hanging signs with their tops at the ceiling. Every doorway is
kept clear for 1.1 m each side, solid loose cover stays off the walking lanes, and fixtures keep out of what the
self-test protects (1 m round a spawn, a hill's radius, 2 m round a flag). **Foliage:** none indoors (the roof), none on
the intact pavement in front of the store (`Concrete`), 10% on the busted asphalt (`"surfaces": {"Plain": 0.1}`), lush
verges between the car park and the hedge and along the dock fence, planted islands, weeds along the foot of the walls.

**Pack props** (`Maps/Cluttered/Props/Supermarket`, written by `tools/cluttered/supermarket_props.py`, 40 props): five
Grocery Shelf kinds, Shelf End Cap, Dairy Chiller, Freezer Case, Freezer Island, Deli Counter, Bakery Rack, Bread Display,
three Produce Bins, Promo Stack, Checkout Counter, Shopping Cart Nest, Cart Corral, Shopping Basket Stack, Pallet of Goods,
Pallet of Water, Store Cardboard Bale, Pallet Rack, Cardboard Baler, Roll Cage, Pallet Jack, Hanging Aisle Sign, six
Department Signs, Ceiling Light Panel, Flickering Ceiling Light, the Slop Mart Sign, Wet Floor Sign, Dropped Groceries and
a Gumball Machine. Fittings are solid and unbreakable; loose stock breaks; lights and signs are cosmetic.

**Object counts** (from the self-test): 2,948 props (about 700 by hand, 2,240 from 13 cosmetic Clutter ops; 505 solid,
2,443 cosmetic, 1,599 breakable), 6,948 plants (16 trees; the roof removes 9,786 and the paving 6,655), 382 meshes,
120 boxes drawn one by one (walls with their openings, the roof, the mezzanine, stairs, columns, the
cooler), 96 animated props (flickering lights, freezer and chiller lights, checkout lane lights, the sign's letters,
swinging signs), 19 spawns, about 300 waypoints and a 1,390-node nav graph. Bots (12 players, 90 s): about 60 kills in
free-for-all and 52 in TDM, no stuck, fallen or escaped bots, worst tick 0.8 ms.

**Performance.** Fewer boxes than any other Cluttered map (120 drawn), about the props of Dockside and less than half its
plants, so it should run at least as fast as Dockside on the user's PC. Animated props freeze past their distance (40-80 m
for the signs and lights, which also keeps the shelves' dense interior cheap). The smoke run's `cluttered_supermarket` (the
car park and store front from above) and `cluttered_supermarket_inside` (the floor from the mezzanine) views log fps,
props and plants drawn.

`tools/cluttered/supermarket.py` regenerates the map and, given a PNG path, a top-down preview with the spawns, hills and
any spawn-to-spawn sight lines (run `supermarket_props.py` first if the props changed).

## 10 Timberline (large, noisy smooth outline, about 191 x 137 m, 20,700 m2, 16 players): forest, cabins and hidden bunkers

**Theme.** A pine forest on rolling ground under the `alpine` sky (mid-morning sun): little log cabins all over (11 on
the ground plus two over the bunkers), a creek running down from the north-east hills into a lake in the south-west with
a dock, canoes, a rowing boat and ducks, dirt trails between clearings, campsites with tents and campfires, picnic tables,
fallen logs, boulders, toadstools and ferns. Blue holds the **ranger station** by the lake (the radio, the fire danger
board, the flag, a generator shed, the lake cabin and the fishing shack); Red holds the **logging camp** up the hill (a
sawmill shed, the log landing with its piles, a bunkhouse, a tool shed, a truck and trailer on the logging road, a
floodlight). In between: a fire lookout on a knoll (hill 2), the middle campsite (hill 1), the meadow below the creek
(hill 3), a woodcutter's cabin and woodshed (axe in the chopping block, firewood racks), an old cabin, a hermit's cabin,
a trail hut and a south cabin, each with a porch and a rocking chair that rocks.

**Shape.** A `smooth` outline through 18 control points with `noise` (1.8 m, 20 m bumps), wider than it's deep. The edge
is a terrain `cliff` berm (5 m over 7 m) with cosmetic pines on it and beyond (about 460 mesh trees), so the woods carry
on past the barrier.

**Not mirrored.** The two halves look and play differently: Blue's west is low (spawns about 0 m) with the lake, the
creek bed and the densest woods (the dark north-west wood: extra pines and bushes); Red's east is the high ground (spawns
about 8 m up the long rise to the camp) with more open ground round the landing. Fairness comes from measurement:
- **Walks.** The hills sit where both teams walk the same distance. The generator walks a 0.5 m grid over the terrain
  (walls, props, trunks and slopes too steep to walk block; the bunker tunnels are links) from every team spawn: Blue/Red
  91/91 m to hill 1, 105/104 m to hill 2, 108/110 m to hill 3, 186/183 m to the enemy flag (all within 2%). The
  self-test measures the same on the game's nav graph (within 6% in CI) and fails past 10%.
- **Sight.** Wooded berms (3.6 m) screen each team's spawns, staggered so the trails through them don't open a straight
  line, and big erratic boulders (the Mossy Boulder prop scaled to over head height) break the long lines across the
  middle. The generator finds no Blue spawn that sees a Red spawn over the terrain and the solid things at their real
  heights (every line clears its blocker by at least half a metre, before the scattered trees); the self-test casts
  every eye-to-eye ray in the game world.
- **Cover for the high ground.** Red looks down on the middle; Blue gets more solid cover in its half (about 150 fallen
  logs, boulders and stumps against 110) and the covered flanks: the creek bed, the lake shore and the north-west wood.
  The self-test counts solid props and boxes 0.9 m or taller (not tree trunks: where the scatter puts them is luck)
  within 6 m of each team's nav routes to the hills: Blue must have at least 90% of Red's, and each team at least 70% of
  the other's (the generator's estimate is Blue 78, Red 69).
- **Bunkers.** The two bunkers are the same plan turned 180 degrees about the centre, so neither team has a better
  tunnel: Blue's runs from the hunting cabin (north-west) to a trench that comes up towards hill 2 and the middle, Red's
  from the trapper cabin (south-east) to the creek towards hill 3 and the middle.

**Bunkers (how they're built).** The terrain is a heightfield without holes, so each bunker is a pit cut into a level pad
(`cut` flattens 3.5 m deep, 1 m wider than the rooms) with dark concrete walls of boxes inside and roof slabs at the
surface over the pits only (dirt-coloured boxes 6 cm proud of the ground, grass and shrubs growing on them through
`include` zones with `onTop`, an `exclude` at 99% so no tree takes root on a roof). The floor is the pit's own ground
under a cosmetic concrete mesh: a box floor would bury the waypoints the bots need (a waypoint stands on the terrain, and
one inside a box is moved away). Inside: a 13 x 10 m room (three bunk beds, lockers, a map table with pins, a desk with
a field radio, a running generator, gas cylinders, supply shelves and crates, ammo cans, caged lamps flickering on the
generator every few metres) and a 2.4 m corridor to a door. Two ways in each:
- **The hatch.** Inside the hunting cabin (Blue) and the trapper cabin (Red) a plank stair goes down through the floor,
  the hatch lid propped open beside it. The stair is a cosmetic wedge on a `cut` ramp of ground (30 degrees, 6 m, from the
  cabin's floor level down to the room): box steps over a pit hid the drop under them from the bots' nav links (they check
  the ground under the line), so the hatch never joined the nav graph. The cabin stands on the ground itself (the pit
  isn't cut under it), so its floor and the landing get waypoints too.
- **The trench.** A `cut` ramp runs from the door up to the surface 13 m away: Blue's comes up on the slope under brush
  (a timber door frame, a steel door hanging open), Red's runs down a gully into the creek through a big concrete
  culvert pipe. Nothing grows on the trench floor (a trunk would block it).
The self-test checks each bunker: the floor is more than 2.8 m under the surface, a roof covers the room, the nav graph's
main area reaches the room, and both the hatch landing and the trench end have a nav route to it.

**Placement.** Cabins are built from the pack's log-wall kit (`Maps/Cluttered/Props/Timberline`: 3 m walls plain, with
a door or with a window, 1.5 m walls, pitched roofs for 4.5 to 9 m cabins, a porch, a stone chimney), so they're
instanced props, not boxes, and furnished as rooms (the ranger's office with the radio, living rooms with a fireplace,
bunk rooms, sheds with shelving and a generator). Solid forest cover keeps off the trails, doorways, spawns, hills and
flags; litter and camp things lie round the campfires and tables; logs lie at the landing and in the sawmill; ducks
on the lake (on the water, `"anchor": "world"`) and on the shore; reeds and long grass round the lake and along the creek;
the roofs of the bunkers carry fallen branches, ferns and logs at the roof height. Foliage: grass and shrubs everywhere,
bushes in the woods, more in the north-west wood and along the creek, brush over the trench mouths, none indoors (the
roof rule), none in the water (the water meshes are roofs to it), thinned on the trails (12%) and the hill clearings.

**Pack props** (`tools/cluttered/timberline_props.py`): Ridge Tent, Dome Tent, Campfire Ring (flickering), Sleeping
Bag, Camp Chair, Camp Cooler, Camp Lantern, Canoe, Axe Stump, Pine Log Pile, Firewood Rack, Mossy Fallen Log, Fallen
Branch, Mossy Boulder, Forest Stones, Toadstools, Leaf Litter, Fern Clump, Trail Sign, Fire Danger Sign, Outhouse, Bird
Box Post, Field Radio (blinking), Map Table, Bunker Cage Lamp (flickering), Supply Crate, Ammo Can, Bunker Hatch Lid,
and the cabin kit.

**Object counts** (from the self-test): 4,290 props (740 hand-placed, about 3,550 from 87 cosmetic Clutter ops of forest
floor litter, branches, stones, ferns and toadstools, and camp and trail litter; 519 solid, 514 breakable, 4 explosive
barrels and 2 jerry cans, all over 10 m from any spawn), 51,968 plants (313 trees: pines and broadleaf), 1,038 meshes (the
scenery pines, water, the creek, bridges' rails, floors and ceilings, roofs), 411 boxes drawn one by one (313 of them
trunks; the rest the bunkers' walls and roofs, the sawmill, the woodshed, the lookout, the dock and bridge decks, rocks),
24 spawns (9 + 9 team, 6 more), about 570 waypoints. Bots: 16-player free-for-all and team matches with no stuck, fallen
or escaped bots and under 1 ms of tick work.

**Performance.** About 100 boxes plus the trunks are drawn one by one (the cabins are instanced kit props, not boxes);
props, plants and meshes are instanced and culled; litter fades with distance; animated props hold still past 80 m. The
plants (52,000) sit between Old Town's 32,000 and Sprawl's 72,000 and the props match Old Town's, both of which run near
1,700 fps on the user's PC, so this map should too. The smoke run's `cluttered_timberline` (from above the lake) and
`cluttered_timberline_forest` (eye height on Blue's trail) views log fps, props, plants and draw calls.

`tools/cluttered/timberline.py` regenerates the map (with `terrain.py`, the Python port of the game's terrain generator,
for the heights the bunkers and the water need) and, given a PNG path, a top-down preview with the walks and sight lines.
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
top to bench 1 and on down a cutting through the pit's terraced east face to the pit floor, with a blast wall at its
head so there's no line down it. A low berm runs along the top's west rim.
**Hills:** the pit's north-east corner, the east end of the conveyor yard, and the south dump's east end. **Flags:** in the camp and
behind the winder house.

**Balance (by measurement, not mirroring).** Red's spawns sit on the west half of the top, Blue's in the camp, and the
hills sit on the line of equal walk, found by the generator on a 0.5 m grid over the terrain with the tunnels as a
second layer (Blue/Red: pit 93/84 m, yard 87/82 m, south dump 102/104 m, enemy flag 176/177 m; the game's nav graph makes Red's
walks 12-16 m longer than this grid does, so the pit and yard hills are set a little Red-ward of the grid's equal line). Red needs 17.5 m of
descent to reach the pit (about 60 m of ramp), so its spawns are closer to the middle than Blue's. The self-test measures
it again on the game's nav graph (within 10%), checks that no Blue spawn can see a Red one (the processing shed, the core
shed and the top's berm and blast wall are in the way), and counts the solid cover within 6 m of each team's routes to
the hills: the low side must have at least as much as the high side (that's what pays for the height), and the high
side at least 30% of it. Waypoints every 3 m down each haul road and tunnel keep the bots' nav graph on the ramps,
which are narrower than its grid. No spawn stands over a tunnel roof (it would be put on the tunnel floor).

**Explosives.** Four explosives crates at the magazine on bench 1 (three inside, one on the apron: shoot it and the rest
go too), and three explosive barrels in the yard, the pit and on bench 2, all well over 10 m from any spawn.

**Pack props** (`Maps/Cluttered/Props/DeepcutMine`, written by `tools/cluttered/mine_props.py`): Mine Cart, Rail Track,
Haul Truck, Excavator, Headframe Tower and Headframe Head (props are at most 12 m, so the 25 m headframe is two: the head with its turning sheave wheels and a beacon stands on the tower), Inclined Conveyor (ore riding the belt, drums
turning), Jaw Crusher (flywheels and jaw), Ore Hopper, Timber Set, Mine Lamp (flickers), Timber Stack, Explosives Crate
(explodes), Danger Sign, Fuel Tank and Floodlight Mast (lamps burning).

**Object counts** (from the self-test): about 280 boxes drawn one by one (215 map boxes, 72 of them tunnel roofs, the
rest cabins, sheds, walls and stockpile cores; plus the tree trunks), about 3,950 props (386 hand-placed, the rest from
34 Clutter ops; about 290 solid, 750 breakable; the Clutter ops that reach over tunnels are cosmetic only, so nothing
solid lands on a tunnel floor), about 35,000 plants (dusty patchy grass everywhere, scrub on the dumps, 66 trees, pines
round the camps), 277 meshes (tunnel lagging, portal frames, stockpile cones, ropes, floors).

**Performance.** Fewer boxes than Old Town, a similar prop count and fewer plants, so it should run at least as fast;
animated props (the sheaves, conveyors, crushers, generators, lamps) freeze past their freeze distance. The smoke run's
`cluttered_deepcut` (the pit and terraces from above the camp) and `cluttered_deepcut_tunnel` (inside the North Drift)
views log fps, props and plants drawn.

`tools/cluttered/deepcut.py` regenerates the map and, given a PNG path, a top-down preview with the equal-walk line and
any spawn sight lines (it uses `terrain.py`, the Python port of the game's terrain generator).
