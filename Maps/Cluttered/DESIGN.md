# Cluttered: design notes

Three maps with no straight outer edge (a circle, a bean and an irregular sprawl), built to see how many objects a map can
hold and how much life props and foliage bring. Each map's section below gives its theme, shape, layout, routes and object
counts. The self-test's `Cluttered pack` section checks every map (shape, density floors, nothing outside the outline,
objectives, fairness, a bot match).

## 02 Crescent Bazaar (medium, bean, 137 x 83 m, 9,140 m2, 16 players): walled market

**Theme.** A walled market town in warm sandstone. A caravanserai at each end, a crescent plaza with a fountain, a covered
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
- 26 hand-placed trees, plus grass, shrubs and bushes. The garden is lush (6 grass clumps per m2), with patchy dry
  tufts and wall weeds everywhere else.

`tools/cluttered/crescent_bazaar.py` regenerates the file and a top-down preview PNG.
