# 08 London (medium, UK, 16 players): an East London neighbourhood

**Theme.** The user asked for "a UK map ... in medium map size"; the lead chose a London neighbourhood, and this map is
an East End corner of it on a grey, just-rained afternoon (`overcast` sky, sun at 15:30 at London's latitude, a pale
haze): a canal with a towpath, moored narrowboats and a lock; a Victorian brick railway viaduct whose arches are let as
a craft-beer bar, a garage and a joiner's workshop, with a small station in the middle; a high street with a corner
pub, a barber and a corner shop; bay-fronted terraces and a Victorian townhouse; a raised square behind iron railings
with a memorial and plane trees; and a 1960s council tower block over the canal at each end. Every name is invented:
Whitmore Road station, The Lamplighter, Patel & Son, Sharp Lads, Viaduct Motors, Arch Seven Taproom, Bow Joinery,
Whitmore Cut, Brite Tea. UK flags only (`"flagPool": ["uk"]`), UK road markings and left-hand traffic.

**Shape.** A `polygon` outline (54 corners, 11,321 m2, 150 x 103 m across its juts), mirrored about x = 0. The north
edge is the canal bank (the water is outside, like Dockside's sea). Everywhere else the edge has lanes poking out of it:
the high street runs on past the last houses into a road-works dead end at each side, the towpath runs on to a gate,
and on the south side Canal Street climbs into a dead-end lane, a mews poking out behind the terraces and a narrow
court behind the square. The edge itself is the invisible barrier (no wall style); the canal, the embankments and the
houses frame it.

**Terrain (real elevation).** `cell` 0.5 m. The canal is cut 3.5 m below the streets (water at -2.88), the towpath sits
2.4 m down behind a brick retaining wall with ramps up at the lock (the lock steps), at Canal Street (a sloping street
down to the water) and at each end. The viaduct deck is 5.4 m up; at each end it runs out of the arches onto a grassy
railway embankment (a `plateau` fill, edge 15 m, about 28 degrees at its steepest), which the teams climb straight from
their spawns. The south side rises: the terraces stand on a 0.3 m pad with front steps, the back alley is 0.75 m up,
Canal Street climbs on to about 2.5 m in its dead end, and the square is a 1.5 m raised plateau with a retaining edge
and railings, reached up steps from the zebra crossing and from the back alley. Everything people fight on is on level
pads or ramps under 25 degrees, except the embankments (walkable, slower).

**Layout, north to south.**
- **Towpath** (y -2.4): the long northern lane, 5 m wide, moored narrowboats alongside, a lock with balance beams in
  the middle (hill 1), benches, bollards, lamps and two staggered brick piers in each half that make a chicane and cut
  the end-to-end sight line.
- **Canal-side yards** (y 0): a boatyard in each half (a narrowboat on cradles, timber, gas, a yard office), the lock
  yard in the middle (steps down to the lock, the station hall's back door), and the council tower block (Apartment
  Block, 5 floors, flat roof with stairs: the highest point, overlooking the deck, the yards and the high street).
- **Viaduct deck** (y 5.4): two tracks, parked trains (two cars standing at the middle, one car per half on the other
  line by the embankment), a short platform with a canopy and a shelter on the bridge, signals, relay cabinets and
  weeds. Parapets 1.1 m along both sides. Reached by the stair bay next to the station hall in each half (from the hall
  and from Arches Lane) and by the embankment at each end. Hill 2 is on the platform over the station.
- **Under the viaduct**: in each half a Railway Arches section (stair bay, arch bar, open through-arch for Canal
  Street, garage, workshop, all with front and back doors), and in the middle the iron bridge over the Tube-style
  **station hall** (oxblood tiles, ticket gates, ticket machines, entrances on both sides): the central N-S route.
- **Arches Lane** (setts): along the arches' fronts, with vans, a skip, tyres and cafe tables, and the shops' back
  yards behind low walls with gates.
- **High street**: UK left-hand traffic (eastbound in the north lane), double yellows, a zebra crossing with Belisha
  beacons, zig-zags and LOOK RIGHT at Station Approach, bus stops with a double-decker at each, black cabs and parked
  cars, heritage lamp posts, phone boxes, a post box, street trees in pits. North side: barber, corner shop (with a fruit
  and veg stall) and The Lamplighter on the corner of Canal Street. South side: a Victorian townhouse beside the square
  and two Bay Fronted Terraces in each half (front garden walls and bins, back yards with sheds, washing lines and BBQs).
- **The square** (y 1.5, hill 3): lawns, a memorial obelisk on the cross walk, benches, a drinking fountain, swings,
  plane trees, railings.
- **Back alley** (y 0.75): behind the terraces' yards, fly-tipping, bins, a burnt-out car in the mews.
- **Canal Street** (x = +-28): the cross street of each half, from the towpath slipway through the viaduct arch,
  between the corner shop and the pub, over the high street and up the hill to the south dead end.

**Teams and objectives.** Blue spawns at the west end, Red at the east: 8 spawns each (4 in the high street's dead end, 2 at the
high street's end below the bank, 2 at the towpath's end), plus 6 free-for-all spawns spread over the middle.
Flags at the high street ends (x = +-64.5). Hills on the line x = 0: the lock on the towpath, the platform on the deck,
the square. Lanes: towpath, yards, deck, Arches Lane / back yards, high street, back alley, with the station hall,
Station Approach and Canal Street as the cross routes.

**Fairness.** Everything is mirrored about x = 0 (`placement.Site(mirror="x")`): blocks, meshes, props (twin yaw
-yaw, so vehicles keep their UK lane direction), decals (signs are laid out per half so they still read left to right),
foliage regions and Clutter (`"mirror": true`). Buildings are placed as twins with yaw -yaw and the `mirror` variant
flipped, so each half's houses, shops, arches and tower are exact mirror images; only the furniture inside can differ.
The generator's sight check finds no line from a Blue spawn to a Red spawn at eye height (the buses, the chicanes and
the buildings are in the way).

**Placement rules.** Lamps along the kerbs facing the road; bins beside benches; beacons at the crossing's kerbs;
cars parked nose to tail on the correct side of the road, the bus at its stop with the shelter and the flag; trees in
pits; bins and gas by the yards' back walls; tyres, engine blocks and drums outside the garage; tables outside the bar;
mooring bollards along the canal edge; balance beams on the lock; benches and railings along the square's paths;
fly-tipping in the back alley; litter scattered by `Clutter` (cosmetic) on every street; puddles, oil, cracks,
graffiti, grime, soot and posters as decals.

**New assets (general, reusable):**
- `Props/British/` (tools/world/london_props.py): Black Cab, Double Decker Bus, Belisha Beacon (animated pulse; also on
  Animated Props), Canal Narrowboat, Canal Lock Gate, Railway Track, Commuter Train Car, Railway Signal, Iron Railings,
  Heritage Lamp Post, Ticket Gate, Mooring Ring Bollard. Generic designs, no liveries.
- `Buildings/RailwayArches/` (40 m viaduct section with five bays, its own `archbar` furnishing rule) and
  `Buildings/BayFrontedTerrace/` (three bay-windowed Victorian houses), written by tools/world/london_buildings.py and
  shown on the Dev pack's **London Street** (`Maps/Dev/BuildingsLondon.json`, tools/world/london_showcase.py).
- Pack decals `Maps/World/Decals/London/` (tools/world/london_decals.py): the invented fascias, the station board,
  street name plates, LOOK RIGHT, BUS STOP and two posters, cut into square tiles so the lettering stays sharp.

**Abutments.** Where each arch section meets its embankment a brick abutment block fills the step, so the deck runs
level from the bridge over the station, along the arches' roofs and out onto the bank top (bots route both ways).

**Balance (from the self-test, game nav graph, averaged over each team's spawns, Blue/Red):** towpath lock 93/95 m,
platform on the deck 85/81 m, the square 86/83 m, enemy flag 140/138 m (all within 5%). No Blue spawn sees a Red one
(generator check). Bots reach all 198 rooms and 30 stair tops of the buildings, the deck included.

**Performance (from the self-test).** Medium, 150 x 103 m across the juts, 11,321 m2, 16 players. 16 buildings with
8,168 shell boxes, drawn by the instanced building renderer; only 83 boxes are drawn one by one (roads, pavements,
the retaining wall, the bridge and abutments, the station facades, the platform, the chicanes); 95 meshes; 3,661 props
(1,261 solid; hand-placed, building furniture and dressing, and cosmetic Clutter litter that fades out); 8,040 plants;
680 of 700 decals placed (28,157 triangles in 47 chunks, about 160 of them the sign tiles); nav graph 1,726 nodes.
That is fewer props, plants and drawn boxes than Old Town (4,908 props, about 32,000 plants), so it should run at or
above Old Town's frame rate. The smoke run's `world_london` (the whole map from above the west end) and
`world_london_street` (the high street at eye height) log fps, props and draw calls.

**Regenerate.** `python3 tools/world/london_props.py`, `python3 tools/world/london_buildings.py`,
`python3 tools/world/london_decals.py`, `python3 tools/world/london_showcase.py`, then
`python3 tools/world/london.py . preview.png` (writes Maps/World/08-London.json and a top-down preview, and prints the
counts, skipped placements, the spawn sight-line check and spawn clearance).
