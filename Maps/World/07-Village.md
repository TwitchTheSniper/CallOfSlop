# 07 Cotswold Village (World Tour, England, small, 12 players)

**Theme (the lead's choice for "a UK map in small size").** An English village in the Cotswolds on a sunny afternoon:
honey limestone walls under grey slate and straw thatch, dry-stone walls along the lanes, hedges, a village green with a
memorial cross, a parish church on its raised churchyard, allotments, the village shop and post office, a pub with a beer
garden, a stream under a stone bridge and a meadow. Invented names only (THE PLOUGH, VILLAGE STORES); no royal cyphers,
real brands or real places. The flag pool is the UK flag only. Driving on the left: parked cars face the way of their
side of the road, the bus waits on the eastbound (north) side.

`tools/world/village.py` regenerates `07-Village.json` (and prints the walks check; give it a PNG path for a top-down
preview). The new assets come from `tools/world/village_props.py` (Props/Countryside), `village_buildings.py`
(Buildings/StoneCottage, ThatchedCottage, PostOfficeStores), `village_decals.py` (Maps/World/Decals/Village) and
`village_showcase.py` (the Dev pack's **Village Buildings** map).

## Shape and terrain

- **Outline:** a hedged polygon (light noise), about 82 x 73 m overall, 5,430 m2 inside (Small: under 75 m on a side
  by area). Lanes jut out where they leave the village: the High Street west over the bridge and east, Church Lane east,
  a footpath north out of the churchyard and one south over the fields, each ending at the hedge with a gate or a sign.
- **Hillside:** the ground falls south at 9% (about 5 degrees): roughly 6 m at the churchyard's north wall, 3 m on the
  green, 1 m behind the pub. Beyond the hedge the land rises into wooded hills (terrain hills plus ~120 cosmetic trees).
- **Raised churchyard:** a plateau at 6.3 m behind a stone retaining wall 2-3 m tall along Church Lane, entered by
  steps through the lychgate, a second flight east of it, a path up from the cottage's orchard (west) and two gaps from
  the allotments (east).
- **The stream:** a valley 1.3-1.8 m deep comes in through the west hedge, runs down the cottage's back garden, under
  the High Street's stone bridge (2.75 m deep there: you can walk the bed under the deck) and away across the meadow.
- The lanes are terrain ramps painted in the asphalt colour, the pavements and paths paving grey; kerbs are cosmetic
  strips tipped to the slope.

## Layout and routes (Blue uphill in the north, Red downhill in the south)

- **North (Blue):** the church (honey limestone palette) on the churchyard plateau with headstones, chest tombs and
  yews; the allotments east of it (plots of bean canes, raised beds, cold frames, sheds, water butts, a scarecrow).
- **Church Lane** below the churchyard wall: the dry-stone allotment wall with an open field gate, parked cars, a SLOW
  marking at the east end.
- **The green (hill 1):** sloping, with the memorial cross (blank plaques, a poppy wreath), two oaks, benches, a village
  fete (two stalls, hay bales, picnic tables), the timber bus shelter and the bus at the stop, the red phone box, a UK
  flag, a noticeboard. **West Lane** and **East Lane** run down either side (hills 2 and 3 sit on them, by the junctions'
  give-way markings).
- **West block:** a Stone Cottage facing West Lane, its front wall and gate, the orchard and a garden shed behind, the
  stream at the bottom of the garden. **East block:** the Row Houses terrace (yellow stone) facing East Lane with front
  walls; its ginnel runs through the back of the terrace from Church Lane to the High Street (an indoor flank).
- **The High Street (south, Red):** the Post Office Stores (village shop and post office) (VILLAGE STORES fascia, post box, crates), the
  footpath south between it and the pub, the pub garden with picnic tables and parasols, the pub (Cotswold stone
  palette, THE PLOUGH), a zebra crossing with zig-zags in front of it, the Thatched Cottage behind its wall; back yards
  behind them (bins, a skip, pallets, a shed). **The meadow** (south-west): long grass, willows, the stream, a log and a
  bale.
- **Flanks:** west, the orchard path down from the churchyard, the cottage garden and the stream bed under the bridge
  into the meadow; east, the allotments, the terrace's ginnel and back yards, the cottage gardens. **Verticality:** the
  first floors of the cottages, the terrace, the shop's flat and the pub (all with stairs) overlook the lanes and the
  green; the churchyard wall overlooks Church Lane; the church's bell chamber.
- **Sightlines:** the long High Street is broken by the bus at the stop, the parked cars and van, the bridge parapets;
  Church Lane by the parked cars and the lychgate's corner; the green by the stalls, oaks, memorial and bus shelter.

## Objectives and fairness

- Spawns: Blue 3 in the churchyard, 3 in the allotments; Red 3 in the meadow and behind the shop, 3 behind the pub and
  the thatched cottage; 2 more (north of the church, behind the pub) for free-for-all. 12 players.
- Hills: the green (0, -0.5), West Lane (-17, -1.6), East Lane (17, -0.6), each on a levelled pad.
- Flags: Blue in the churchyard (-25.5, -24), Red in the thatched cottage's back garden (25.5, 29.5).
- Not mirrored. The generator's grid walk (open ground and doorways, uphill costing more) gives blue/red: green
  46/44 m, West Lane 48/48 m, East Lane 45/47 m, enemy flag 75/74 m. The self-test ("World Tour: Village", map name "Cotswold Village") measures the
  same on the game's nav graph and fails over 15% apart; it also checks the nav graph reaches every spawn, hill, flag and
  every room of the six buildings.

## Objects (generator numbers; the self-test line prints the game's)

- 6 buildings (Church, Stone Cottage, Row Houses, Post Office Stores, Pub, Thatched Cottage): about 2,200 shell boxes in the
  instanced building renderer.
- 19 map boxes (retaining wall pieces, the bridge), about 370 cosmetic meshes (kerbs, water, scenery trees, the cottages
  beyond the hedge), about 200 hand-placed props (170 solid), plus ~170 Clutter props.
- Decals: road markings (centre dashes, zebra and zig-zags, double yellows, give-way lines and triangles, BUS STOP,
  SLOW), road wear, leaves, mud, moss and grime, the shop and pub signs; 7 pack decals in Maps/World/Decals/Village.
- Foliage: grass over the village, long grass and shrubs in the meadow, reeds along the stream, hedges, flower beds,
  13 trees (oaks, yews, orchard, willows).

## What changed outside the map

- New general props `Props/Countryside` (18), buildings `Buildings/StoneCottage`, `ThatchedCottage`, `PostOfficeStores`
  (with 5 shop props and a shop furnishing rule), and 3 global road decals (`Decals/Road`: Give Way Lines, Give Way
  Triangle, Road Zigzag Lines) with their Decal ops on `Maps/Dev/DecalSurfaces.json`.
- New palettes: "honey limestone" on the Church and "cotswold stone" on the Pub (added; existing palettes unchanged).
- `Maps/Dev/BuildingsVillage.json` (the showcase), `Source/App/SelfTestWorld.Village.cs` plus one Section line in
  SelfTest.cs, two smoke views in Program.Props.cs (`world_village`, `world_village_street`).
