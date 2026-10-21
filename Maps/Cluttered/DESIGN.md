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
Red spawns. The bot TDM leans to whichever team holds the idle host (the self-test's host player stands still and only
feeds kills).

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
