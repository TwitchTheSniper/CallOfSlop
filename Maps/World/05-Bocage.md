# 05 Bocage (World Tour, France; large, smooth noisy outline about 248 x 200 m, 32,000 m2, 22 players)

**Theme.** Normandy hedgerow country on a showery-bright afternoon (`clear` preset, sun at 15:36, latitude 49, clouds
drifting east, a pale haze). Small fields boxed in by tall hedgerows on earth banks, lanes running between hedges and
sunk between their banks for long stretches (chemins creux), a slow brook winding through the middle, and a hamlet on
both banks of it round a stone bridge: on the west bank the chapel (the Church in grey stone) with a granite calvary at
the junction, a signpost, a post box and benches; on the east bank the mairie (a whitewashed Norman Farmhouse) with the
tricolour flying and bunting across its front, a well, a cafe table and cider barrels. At each end a stone farm: a Norman
Farmhouse and a Half-Timbered Barn round a farmyard (tractor, hay wagon, well, trough, muck heap, bales, hens, a dog
kennel), a concrete grain silo, a cider orchard of apple trees behind the house (crates, a ladder, windfalls, sheep
grazing under the trees) and a farmworker's thatched cottage over the lane. Cottages in the village and out in the
fields; hay meadows with round bales, pastures with Normande cows, troughs and cow pats, a ploughed field with a plough
and a tractor, telegraph poles along the lanes, oaks standing in the hedgerows, willows along the brook.

**Shape and edge.** A `smooth` outline (42 control points, point-symmetric, `noise` 0.9 m over 16 m) with four narrow
lobes where the lanes run out of the map as little sunken dead ends. The edge is a terrain `cliff` berm (6 m over 8 m),
so the land rises into wooded hills all round (about 340 cosmetic mesh trees on and beyond the berm).

**Terrain.** Gentle rolling ground (pairs of `hill` features, 1-2.6 m, plus 0.45 m of noise): the farms each on a rise,
knolls in the north and south fields, the chapel's knoll, a wet hollow. Building pads and the two village squares are
flattened; the lanes are painted dirt and cut 1.3 m below the ground on long stretches (the west and east lanes between
the farms and the village, the north and south lanes out to their lobes) with banks either side; the brook is a level
sandy bed 1.8 m down (2.6 m under the bridge) with flat water strips on it and stepping stones at two fords.

**Hedgerows** (the point of the map): 452 `Bocage Hedgerow` props (6 m pieces every 5.6 m along each hedge line, 3 m pieces
for the odd lengths and on banks, rounded 2 m ends at every gap; a few on steep banks lifted up to 0.4 m so every top stays
at least 3.8 m over the ground at its middle) along 26 field hedges and both sides of every lane, outside the village
squares, the farm fronts and the brook. Each is an earth bank under a dense hedge: one solid collider 4.3 m tall and 2.3 m
deep, so it stops bullets, grenades and sight, can't be walked through and is above a Parkour mantle's reach. Gaps are
field gates (4.6 m, timber gate swung open between granite posts) or worn 3.4 m gaps, two or three per field and every
~30 m along the lanes, staggered side to side; each gap has waypoints on both sides so bots path through. Sight lines are
short in the fields (25-35 m across), long down the lanes and the brook, and longest from the barn haylofts (loft doors in
both gables and over the front doors: the eye is 5 m up, over the hedges).

**Fairness.** Everything that shapes play (terrain shapes, lanes, hedges and gaps, buildings, spawns, objectives) is
point-symmetric about the centre; only the village plots differ (chapel / mairie) and the dressing. Blue holds the west
farm, Red the east farm. Hills: 1 on the bridge deck in the middle (y 0.35, radius 5), 2 and 3 in the narrow fields beside
the north and south lanes, placed where the averaged walks are equal on the generator's 0.5 m grid (Blue/Red 99/98,
123/126, 125/123 m; enemy flag 202/202 m; on the game's nav graph in CI 100/100, 120/124, 124/124 and 198/197 m). No Blue spawn sees a Red one. The self-test ("World Tour: Bocage") measures
the same on the game's nav graph (within 10%), casts every spawn-to-spawn eye ray, checks the nav graph reaches every
spawn, hill and flag, and that bots reach every room of every building.

**Objects** (self-test counts: 1,554 props, 990 solid; 44,967 plants, 88 trees; 1,058 meshes; 91 boxes drawn one by one, the
bridge and the trunks; 8,337 boxes in all with the buildings and colliders): 12 buildings (2 Norman Farmhouses and 2 Half-Timbered Barns at the farms, 6 Norman
Cottages, the chapel and the mairie), about 650 hand-placed props (452 hedgerow pieces, 37 field gates, the yards,
orchards, fields and village), a few cosmetic Clutter ops (windfalls, straw, sheep), 3 boxes drawn one by one (the
bridge deck and parapets) plus about 90 tree trunks, about 1,060 meshes (scenery trees, the water, the bridge's arch and
copings, stepping stones), about 240 decals plus scatters (cart ruts, mud and puddles down the lanes, hoof prints and mud
at every gate, cow pats, apple blossom, lichen, moss and whitewash wear on the walls, the village signs, posters, bunting).

**New assets.** General: `Props/Countryside` (31 props, `tools/world/bocage_props.py`) and three buildings
(`Buildings/NormanFarmhouse`, `Buildings/HalfTimberedBarn`, `Buildings/NormanCottage`, `tools/world/bocage_buildings.py`),
shown on the Dev pack's **Countryside Buildings** map. Pack-local: `Maps/World/Decals/Bocage` (9 decals,
`tools/world/bocage_decals.py`).

**Performance.** Buildings are instanced shells, hedges and everything else instanced props, the scenery trees
instanced meshes; only the bridge's three boxes and the trunks are drawn one by one. The smoke run's `world_bocage` (the
whole map from above the south-west) and `world_bocage_lane` (down the sunken west lane at eye height) views log fps,
props, plants and draw calls.

`tools/world/bocage.py <repo> [preview.png]` regenerates the map and prints the walk balance and sight-line checks
(`BOCAGE_HILLS=1` searches for equal-walk hill spots).
