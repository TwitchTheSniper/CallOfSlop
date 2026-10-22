# 02 Slopview Mall (World Tour, US, medium, 16 players)

Generated: `python3 tools/world/mall_props.py .`, `python3 tools/world/mall_decals.py .`, `python3 tools/world/mall_building.py .`
(also writes `Maps/Dev/BuildingsMall.json`), then `python3 tools/world/mall.py .`.

## The place
A 1990s American two-storey mall, the **Shopping Mall** building (`Buildings/ShoppingMall`, general and reusable), on a
terraced suburban lot:
- **Atrium**: tiled walkways under the upper balconies, a skylight frame open to the sky, two bridges across the void,
  four escalators (solid steps up to the bridges), and a **sunken fountain court** cut 0.9 m into the terrain between the
  bridges (the Drip Drop Coffee kiosk, benches, palms, pendant lights and sale banners).
- **Shops** (all invented brands, fascia sign + logo plaque + poster each): ground floor Voltwave (electronics),
  Dog-Eared Books, Sole Purpose (shoes), Full Tilt Sports, Loom & Linen (clothing), Gilt Trip (jewellery), Gumdrop Galaxy
  (sweets), Topsy Toys; upper floor Token Town (arcade), Gloss Lab (beauty), a vacant unit behind COMING SOON hoarding,
  Wagtail Pets, Nest & Nook Home, Cellsius (phones), Customer Service, Hemline (fashion). The anchor is the Holloway &
  Finch department store (west, two floors, its own stair); east is the food court (Wokstar, Bun Deal, Slice of Life)
  with the Stardust Cinemas lobby and two screens above.
- **Back of house**: concrete service corridors behind both shop rows on both floors, stock rooms, restrooms and the
  loading dock (two roll-up openings onto a truck well); the back-of-house roof is a terrace reached from the upper
  service corridor.
- **Outside**: the car park falls 3.6 m to the south (two bay rows, grass islands with trees and lamps, cars, carts,
  corrals, the bus stop), the entrance road juts out to the south-east past the road pylon, the multi-storey car park
  stands on the west with an alley jutting past it, the service yard and truck well on the north with the service road
  climbing out through the wooded embankment, and an east service drive. Wooded banks rise all round into the
  outline's cliff edge (noisy polygon, no straight box walls).

## Lanes
- **Atrium**: the long walkway sightlines are broken by the fountain court, kiosks, carts, palms and the escalators;
  the bridges and balconies give a second level over it.
- **Store-through-store flanks**: every shop has an arched front and display windows (shoot them out and climb
  through) onto the atrium or balcony, and a back door onto the service corridor.
- **Back corridors**: ground and upper rings behind the shops, joining the department store, food court, cinema,
  loading dock and the fire exits to the car park.
- **Upper ring**: balconies, bridges, the department store upper floor, the cinema; stairs in the north hall, the
  entrance hall and the department store, plus the escalators.
- **Outside**: the car park (Blue), the service yard (Red), the east drive and the west alley/multi-storey round the flanks.

## Objectives
Blue spawns in the car park, Red in the service yard (not mirrored). Hills: the sunken court, the food court, the
department store's upper floor. Flags: the car park walkway and the service yard.

## Assets
- `Props/Retail`: 70 general shop fittings (see Props/README.md).
- `Maps/World/Props/Mall`: brand fascia signs, logo plaques, food stall headers, the coffee kiosk, carrier bags, the
  mall sign and pylon (pixel lettering from `tools/world/pixelkit.py`).
- `Maps/World/Decals/Mall`: 42 decals (brand posters, film posters, wayfinding and dock signs, sale and food posters,
  the floor medallion, painted floor markings), 128 x 128 PNGs.
