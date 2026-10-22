# 06 Vuorikoski (Finland)

World Tour map 506, Large, 24 players. File `06-Finland.json`, written by `tools/world/finland.py` (run it from the
repository root; it prints the costs and can draw a top-down preview). Early autumn in the Finnish lake district: a
copper mine in a spruce and birch forest, two wooden lake villages and a church on the hill.

## Layout (north is -Z)

- **Honkala (Blue, west) and Kuusela (Red, east):** a gravel village street at x = -+90 with red ochre and yellow
  ochre wooden houses (Finnish Wooden House), the village shop (Village Shop, KAUPPA fascia), a garage, cottages (Red
  Barn Cottage; Kuusela has the Hunting Lodge), the flag green, mailbox racks, moose signs, utility poles. Below each
  village a lake with a jetty, rowing boats, ducks and a Lakeside Sauna. Each team's flag is on its village green.
- **Kirkonmaki** on the church hill in the north (3.75 m up): the Wooden Church with its churchyard, a separate bell
  tower (kellotapuli), two houses.
- **The Vuorikoski mine** on a levelled pad in the middle: the 36 m concrete headframe over the shaft (a hoist hall
  you can walk through, a cage lift down the shaft), the concentrator (Recycling Plant, blue steel) with a conveyor
  gallery up to the headframe, the hoist house (Substation Control House), the workshop (Machine Shop), offices and
  a garage, haul trucks, tanks, floodlights; the rail spur east-west at z = 18 with the ore load-out (hill 3) and
  hopper wagons; slag heaps; the open pit in the south (benches at -3, -6 and -9 m, haul ramps, hill 2 on the floor).
- **Underground, level 120 (floor -8 m):** the shaft station under the headframe (hill 1, the cage lands here), the
  main drift east-west, a haulage loop south with the pump station (west) and the explosives magazine (east), the
  north drift to the crusher chamber under the concentrator, the pit drift out onto the pit floor, and adits up to the
  forest behind the ridges, to each village's lake shore and to the church hill. Rails, timber frames, cage lamps,
  a continuous mine railway (sleepers and rails meshes laid on the real cave floor and tipped to every ramp, via tools/world/caves.py, a port of the game's cave generator) with carts standing on it, signs (TASO 120, HISSI, RAJAHDE, ULOS).
- **Elevation:** forest hills up to 8 m with granite crowns, a ridge screening each village from the mine, slag cones,
  the church hill, the pit down to -9 m, the tunnels at -8 m; the wavy noise outline rises into a cliff of forested
  hills (instanced mesh spruces and birches beyond the edge).

## Fairness

Terrain, tunnels, roads, cover scatter and objectives are a mirror image about x = 0 (each scattered cover prop has its
twin); the villages are dressed differently. The self-test (`SelfTestWorldFinland.cs`) checks the nav walks from each
team's spawns to every hill and the enemy flag match within 6%, and that no Blue spawn sees a Red one.

## Costs

Foliage trunks are boxes drawn one by one, so the forest has a few hundred trees and the rest of the woods' look comes
from the understory (grass, heath, young spruce bushes), the cosmetic clutter and the mesh trees beyond the edge. Map
blocks are only the headframe, jetties, railings and the bell tower. Everything else is buildings (instanced), props
(instanced), meshes (instanced) and baked decals. The self-test notice "World: Vuorikoski" prints the counts.

## Assets

General: `Props/Forest`, `Props/Mining`, `Props/Nordic` (tools/world/finland_props.py), buildings `Buildings/
FinnishWoodenHouse`, `RedBarnCottage`, `VillageShop`, `WoodenChurch`, `LakesideSauna` (tools/world/finland_buildings.py,
shown on the Dev pack's Finnish Buildings map). Pack-local: the sign decals in `Maps/World/Decals/Finland`
(tools/world/finland_decals.py).
