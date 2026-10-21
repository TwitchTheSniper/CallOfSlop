# Cluttered: design notes

Three maps with no straight outer edge (a circle, a bean and an irregular sprawl), built to see how many objects a map can
hold and how much life props and foliage bring. Each map's section below gives its theme, shape, layout, routes and object
counts. The self-test's `Cluttered pack` section checks every map (shape, density floors, nothing outside the outline,
objectives, fairness, a bot match).

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
