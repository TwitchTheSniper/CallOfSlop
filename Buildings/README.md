# Making buildings for Call of Slop

A building is a folder in this `Buildings` folder: a `building.json` that describes its rooms, walls, doors, windows, stairs, roof and furniture, plus (optionally) its own props and furnishing rules. Maps place buildings with one op:

```json
{ "op": "Building", "name": "Small House", "at": [46, -11], "yaw": 180, "seed": 5, "variant": { "palette": "yellow brick", "mirror": true } }
```

The game turns the file into walls with real openings, floors, ceilings, stairs, a roof, doors you can open, shut and break, windows you can shoot out, and furniture placed room by room. Everything is deterministic: the same file, seed and placement give the same building on every PC.

The Dev pack's **Building Yard** map (`Maps/Dev/BuildingYard.json`) shows every building in this folder, with a few variants. Start there. Content sets have their own showcase maps in the Dev pack, named `Maps/Dev/Buildings<Set>.json` (for example **Buildings Industrial**: the factory, brewery, sawmill, substation, cold store, recycling plant, print shop, chemical lab, machine shop and grain barn; **Residential Street**, `Maps/Dev/BuildingsResidential.json`, for the ten residential buildings; **Commercial Street**, `Maps/Dev/BuildingsCommercial.json`, for the ten commercial ones: the barber shop, pizzeria, bank, pub, laundromat, pharmacy, hotel, cinema, bookshop cafe and electronics store; **Finnish Buildings**, `Maps/Dev/BuildingsFinnish.json`: the Finnish wooden house, red barn cottage, village shop, wooden church and lakeside sauna; **Mall Showcase**, `Maps/Dev/BuildingsMall.json`, for the Shopping Mall; **Countryside Buildings**, `Maps/Dev/BuildingsCountryside.json`, for the Normandy set: the Norman Farmhouse, the Half-Timbered Barn and the Norman Cottage, written by `tools/world/bocage_buildings.py`); the self-test wants every building on the Building Yard or on one of those, and checks that bots reach every room on each.
The Dev pack's **Building Yard** map (`Maps/Dev/BuildingYard.json`) shows every building in this folder, with a few variants. Start there. Content sets have their own showcase maps in the Dev pack, named `Maps/Dev/Buildings<Set>.json` (for example **Buildings Industrial**: the factory, brewery, sawmill, substation, cold store, recycling plant, print shop, chemical lab, machine shop and grain barn; **Residential Street**, `Maps/Dev/BuildingsResidential.json`, for the ten residential buildings; **Commercial Street**, `Maps/Dev/BuildingsCommercial.json`, for the ten commercial ones: the barber shop, pizzeria, bank, pub, laundromat, pharmacy, hotel, cinema, bookshop cafe and electronics store; **Finnish Buildings**, `Maps/Dev/BuildingsFinnish.json`: the Finnish wooden house, red barn cottage, village shop, wooden church and lakeside sauna; **Village Buildings**, `Maps/Dev/BuildingsVillage.json`, for the English village set: the Stone Cottage, the Thatched Cottage and the Post Office Stores, written by `tools/world/village_buildings.py`); the self-test wants every building on the Building Yard or on one of those, and checks that bots reach every room on each.
The Dev pack's **Building Yard** map (`Maps/Dev/BuildingYard.json`) shows every building in this folder, with a few variants. Start there. Content sets have their own showcase maps in the Dev pack, named `Maps/Dev/Buildings<Set>.json` (for example **Buildings Industrial**: the factory, brewery, sawmill, substation, cold store, recycling plant, print shop, chemical lab, machine shop and grain barn; **Residential Street**, `Maps/Dev/BuildingsResidential.json`, for the ten residential buildings; **Commercial Street**, `Maps/Dev/BuildingsCommercial.json`, for the ten commercial ones: the barber shop, pizzeria, bank, pub, laundromat, pharmacy, hotel, cinema, bookshop cafe and electronics store; **Finnish Buildings**, `Maps/Dev/BuildingsFinnish.json`: the Finnish wooden house, red barn cottage, village shop, wooden church and lakeside sauna; **Mall Showcase**, `Maps/Dev/BuildingsMall.json`, for the Shopping Mall; **Silo Buildings**, `Maps/Dev/BuildingsSilo.json`: the missile-complex set of the World Tour map Silo, from the Launch Control Centre and the Silo Power Plant to the access bunker and the Concrete Guard Tower, written by `tools/world/silo_buildings.py`); **Station Buildings**, `Maps/Dev/BuildingsStation.json`, for the sky-station set: the Station Control Hub, the Station Hangar Bay, the Station Crew Quarters and the Refinery Pump House, written by `tools/space/station_buildings.py`; the self-test wants every building on the Building Yard or on one of those, and checks that bots reach every room on each.

- [The quick version](#the-quick-version)
- [Step by step: a new building](#step-by-step-a-new-building)
- [Coordinates and conventions](#coordinates-and-conventions)
- [Top-level fields](#top-level-fields)
- [Surfaces and palettes](#surfaces-and-palettes)
- [Floors and rooms](#floors-and-rooms)
- [Openings: doors, windows, arches and holes](#openings-doors-windows-arches-and-holes)
- [Stairs](#stairs)
- [Roofs](#roofs)
- [Extras: porches, awnings, balconies, platforms, blocks and props](#extras-porches-awnings-balconies-platforms-blocks-and-props)
- [Anchors](#anchors)
- [Furnishing](#furnishing)
- [The building's own props and rules](#the-buildings-own-props-and-rules)
- [The Building op and variants](#the-building-op-and-variants)
- [Doors and windows in the game](#doors-and-windows-in-the-game)
- [Bots, foliage and spawns](#bots-foliage-and-spawns)
- [Performance](#performance)
- [Tools](#tools)
- [Checklist](#checklist)

## The quick version

```json
{
  "format": "call-of-slop-building/1",
  "name": "Garden Shed",
  "theme": "A creosoted timber shed: dark wood, a felt roof, tools and junk",
  "category": "residential",
  "description": "A one-room garden shed.",
  "storeyHeight": 2.6,
  "exterior": { "color": "#4a3626", "material": "wood" },
  "interior": { "color": "#6b5038", "material": "wood" },
  "floors": [
    {
      "rooms": [ { "id": "shed", "type": "storage", "rect": [-1.5, -1.25, 1.5, 1.25] } ],
      "openings": [
        { "type": "door", "at": [0, 1.25], "width": 0.9, "front": true },
        { "type": "window", "at": [-1.5, 0], "width": 0.8, "sill": 1.0, "head": 2.0 }
      ]
    }
  ],
  "roof": { "type": "shed", "dir": "-z", "pitch": 12, "color": { "color": "#2a2a2a", "material": "tiles" } }
}
```

That's a complete building: walls with a doorway and a window cut in them, a floor, a ceiling, a roof, a wooden door, window glass, and shelves, crates and boxes from the `storage` rule.

## Step by step: a new building

1. **Pick a theme.** Every building has one, kept consistent all the way through: the outside (walls, trim, roof), the materials and colours inside, the room types and the furniture. "A 1950s chrome diner", "a dentist's surgery", "a biker bar", "a farmhouse kitchen". Write it in `"theme"`.
2. **Copy `Buildings/_Template`** to `Buildings/<YourName>/` (one word, no spaces: the folder name). Folders starting with `_` aren't loaded, so the template never shows up in the game.
3. **Sketch the plan on squared paper**, 1 m squares, origin in the middle, the front at the bottom (+Z). Draw each room as a rectangle; rooms that touch share a wall. Write down the corners: those are the room `rect`s.
4. **Fill in `floors`**: one entry per storey, each with its `rooms` and `openings`. Give every room a `type` (it picks the furniture).
5. **Cut the openings**: a `door` (or `arch`) between rooms that connect and to the outside (one of them `"front": true`), `window`s or `autoWindows` on the outside walls.
6. **Stairs** if there's more than one floor (and onto a flat roof if you like).
7. **Roof**, **palettes**, **extras** (porch, signs, AC units, bins), **anchors**.
8. **Check it without the game**: `python3 tools/buildings/buildings.py validate "Your Name"` and `python3 tools/buildings/buildings.py preview "Your Name" preview.png` (floor plans and cut-away views, with the furniture). Fix what it says and look at the picture.
9. **Put it on a map**: add `{ "op": "Building", "name": "Your Name", "at": [x, z] }` to a map (the Building Yard is a good test bed: add yours to `tools/buildings/gen_starter.py`'s `building_yard()` or straight into the map file), then press **Reload map files** in the pause menu.
10. **Run the self-test** (`CallOfSlop.exe --selftest`, section "Buildings"): it builds every building in every variant and turn, checks the rooms join up, the furniture stays in its rooms and out of doorways, and that bots can reach every room.

## Coordinates and conventions

- **Metres.** +X is east, +Z is south (the map's directions), Y is up. The building's origin `[0, 0]` is wherever you like, usually the middle; the op's `"at"` puts it on the map.
- **The front faces +Z** (south). A map turns it with `"yaw"` (multiples of 90: buildings stay square to the map, so collision stays exact). `"yaw": 180` makes the front face north.
- **Room rects run to the wall centre lines.** Walls are `wallThickness` thick, centred on the rect edges, so a room's floor inside the walls is its rect shrunk by half the thickness on each side. Two rooms sharing an edge get one wall between them; an edge with nobody on the other side is an outside wall.
- **Heights.** The ground floor's floor is 0.1 m above the ground. Each floor is `storeyHeight` (or its own `height`) from floor to floor; floor slabs and the roof slab are 0.2 m thick, so a room's clear height is the floor height minus 0.2. Openings measure their `sill` and `head` from the floor of their storey.
- **Props face +Z** at yaw 0 and stand on y = 0 (see [Props/README.md](../Props/README.md)). The furnishing puts a prop's back (-Z) against walls.
- **Plain ASCII** in every text (name, theme, description): the game's font has no curly quotes or long dashes.
- Unknown keys, numbers out of range, rooms that overlap, openings that don't sit in a wall, stairs that don't fit and unknown prop names are errors with a message saying which file and where. A broken building file is skipped (and named in the game log); a map that uses it then fails to load with the reason.

## Top-level fields

| Field | Required | Meaning |
|---|---|---|
| `format` | no | `"call-of-slop-building/1"`. Recommended. |
| `name` | **yes** | Up to 40 characters, unique across buildings: maps use it (or the folder name). |
| `theme` | no | One line: the building's theme (see step 1). |
| `category` | no | `residential` (default), `commercial`, `industrial` or `special`. |
| `description`, `author` | no | One line each. |
| `seed` | no | The default furnishing seed (the op's `"seed"` overrides it). |
| `wallThickness` | no | 0.1 to 0.6 m, default 0.25. |
| `storeyHeight` | no | Floor to floor, 2.4 to 8 m, default 3. |
| `doorHeight` | no | Default door and arch height, default 2.1 m. |
| `exterior`, `interior`, `floor`, `ceiling`, `trim` | no | Default [surfaces](#surfaces-and-palettes): outside walls, inside walls, floors, ceilings, trim (sills, railings, posts, porch roofs, blocks). |
| `door` | no | Default door colour. |
| `palettes` | no | Named colour sets (below). |
| `floors` | **yes** | The storeys, ground floor first (below). |
| `floorCount` | no | How many floors a copy gets by default when one floor is `"repeat"`. |
| `stairs`, `roof`, `extras`, `anchors` | no | Below. |

## Surfaces and palettes

A **surface** is `{ "color": ..., "material": ... }`, or just a colour. Colours are `"#rrggbb"`, `[r, g, b]` (0-1), a prop colour name (`"wood"`, `"steel"`, `"brick"`... see Props/README.md) or `"$key"`, a key of the palette. Materials are the map textures: `brick`, `concrete`, `wood`, `metal`, `tiles`, `dirt`, `grid`, or `none` (plain colour: plaster, paint).

**Palettes** give a building variants without copying it:

```json
"palettes": {
  "red brick":    { "wall": "#9a4b3c", "interior": "#e6dcc4", "floor": "#7a5638", "door": "#2f5d3a", "roof": "#4a3a36" },
  "yellow brick": { "wall": "#c4a468", "interior": "#dfe3d2", "floor": "#8a6a48", "door": "#7a2b24", "roof": "#3c3f45" }
},
"exterior": { "color": "$wall", "material": "brick" }
```

The first palette is the default; a map picks another with `"variant": { "palette": "yellow brick" }`. Every palette must have the same keys. Use `$keys` for anything that should change between variants and literal colours for things that shouldn't (a bathroom's tiles).

## Floors and rooms

```json
"floors": [
  { "autoWindows": { "spacing": 2.8, "width": 1.2, "sill": 0.9, "head": 2.2 },
    "rooms": [ ... ], "openings": [ ... ] },
  { "height": 2.8, "rooms": [ ... ], "openings": [ ... ] }
]
```

| Floor field | Meaning |
|---|---|
| `height` | This floor's floor-to-floor height (default `storeyHeight`). |
| `rooms` | At least one room (below). |
| `openings` | Doors, windows, arches and holes in this floor's walls ([below](#openings-doors-windows-arches-and-holes)). |
| `autoWindows` | Windows spread along every outside wall of this floor's rooms: `spacing` (centre to centre, default 3), `width` (1.2), `sill` (0.9), `head` (2.2), `health` (glass health, default 1: one shot). They skip anything that would overlap a door or another window, and keep 0.35 m from the corners. |
| `repeat` | `true` on one floor: copies of it make taller variants (`"variant": { "floors": 5 }`), like an apartment or office floor. The building then has its listed floors by default (or `floorCount`), and up to 12. |

| Room field | Meaning |
|---|---|
| `id` | **Required.** Unique on its floor. |
| `type` | **Required.** Picks the furnishing rule (`living`, `kitchen`, `bedroom`, `bathroom`, `office`, `shop`, `storage`, `garage`, `corridor`, ... see [Furnishing](#furnishing)). A type without a rule stays bare (stair halls). |
| `rect` / `rects` | The room: `[x0, z0, x1, z1]`, or a list of rects for an L-shaped (or any rectilinear) room; no wall between pieces of the same room. |
| `floor`, `wall`, `ceiling` | The room's own surfaces (bathroom tiles, a kitchen's paint). |
| `openTo` | Ids of rooms on the same floor with no wall between (open plan: a kitchen-diner). |
| `windows` | `false`: no automatic windows in this room. |
| `autoWindows` | This room's own automatic windows (small high ones in a bathroom). |
| `furnish` | `"auto"` (default: the rule for its type), `"none"` or `"explicit"` (only its `props`). |
| `rules` | Use another type's rule (`"rules": "diner"`) while keeping the type. |
| `props` | Explicit furniture (placed first; [below](#explicit-placements)). |
| `seed` | Reshuffles this room's furniture only. |

Rooms on a floor must not overlap. An upper floor can be smaller than the one below it: the uncovered part of the lower floor gets a flat roof with a 1 m parapet (a terrace). It can also overhang. Every room must be reachable from the front door: through doors, arches, low holes, `openTo` joins and stairs.

## Openings: doors, windows, arches and holes

```json
{ "type": "door", "at": [-0.25, 4], "width": 1.0, "front": true, "hinge": "left", "swing": "in", "material": "wood" },
{ "type": "window", "at": [3, 4], "width": 1.4, "sill": 0.9, "head": 2.2, "health": 1 },
{ "type": "arch", "at": [1, 1.5], "width": 1.2 },
{ "type": "hole", "at": [-4.5, -2], "width": 1.6, "sill": 0.3, "head": 1.9 }
```

`"at"` is a point on a wall's centre line (on a room rect's edge); the opening is centred there and must fit inside its wall with 0.18 m to spare at each end (more if the wall is thicker), and not overlap another opening in the same wall.

| Field | Meaning |
|---|---|
| `type` | `door` (a leaf that opens, shuts and breaks), `window` (shootable glass), `arch` (a doorway with no door), `hole` (an opening at any height, no glass: a breach, a hatch, a serving hatch). |
| `width` | Metres (default 1, windows 1.2). Doors and arches must be at least 0.9 m wide: bots are 0.8 m wide. |
| `sill`, `head` | Bottom and top above the floor. Doors and arches start at the floor; their top defaults to `doorHeight`. Windows default to 0.9 and 2.2. |
| `front` | `true` on the main entrance (bots, the self-test and tools start from it). |
| `material` | Doors: `wood` (150 health), `metal` (400) or `glass` (60). |
| `health` | Doors and windows: override the health. Windows default to 1 (any bullet); give shop windows 10-40 so they take a few. |
| `hinge` | Doors: `left` (default) or `right`, seen from the side the door opens away from. |
| `swing` | Doors: `in` (default) or `out`. An outside door opens into the building; an inside door opens towards +x or +z (the room on the larger side); `out` flips it. |
| `open` | Doors: start open. |
| `color` | Doors: the leaf's colour (default `door`). |

**Windows you can climb through.** A broken window is an empty frame: a player can mantle through it if it's no more than 1.7 m above the ground they're standing on and at least 1.3 m tall (the default 0.9 to 2.2 is). Ground-floor windows are therefore a way in once they're shot out; make the sill 1.4 m or higher (bathrooms, warehouse strip windows) where you don't want that.

## Stairs

```json
"stairs": [
  { "floor": 0, "rect": [-1.33, 0.65, -0.33, 3.6], "dir": "-z" },
  { "floor": 1, "rect": [0.75, -3.5, 1.85, 0.5], "dir": "+z", "alt": { "rect": [-1.85, -3.5, -0.75, 0.5], "dir": "-z" } }
]
```

A straight flight from floor `floor` up to the next one (or onto the roof from the top floor; the roof must then be flat), climbing in `dir` (`+x`, `-x`, `+z`, `-z`) across `rect`. The steps rise at most 0.36 m (players step 0.55 m), and need at least 0.2 m of run each: a 3 m storey needs about 2 m along `dir`. The rect must sit inside one room, clear of its walls, with at least 0.9 m from the first step to the wall in front of it and 0.9 m from the last step to the wall on the floor above (room to stand: bots need a nav node at both ends). The floor above gets a hole over the rect, with 1 m railings round its open sides (`"railing": false` for none). `color` is the steps' surface (default: the room's floor).

**Stairs must give 2.1 m headroom.** Everything over a flight (the floor slab and ceiling above, beams, lintels, an upper room's wall, a mezzanine's ceiling) must stay 2.1 m above every step a standing player (0.8 m wide, 1.8 m tall) can be on, the landings just off both ends included. The generator does its part: it lengthens the hole in the floor above back past the first step until the slab's edge is high enough (the railings follow the hole), so a flight needs about 0.3-0.6 m more free floor above its foot than its own rect on low storeys. What it can't fix is yours: don't stand an upper room's wall over the stairwell, keep a mezzanine (`platform`) at least 2.1 m under the ceiling, and keep flights at least **1.0 m** clear wide (railing to wall lining) with **1.2 m** clear beyond the top and the foot or room to turn off sideways. The self-test's "Stairs" section walks every flight of every building and map standing (both movers, up and down) and fails on any of these, naming the flight and the clearance in cm.

**Stairwells on repeated floors**: two flights can't stack in the same place (you'd hit your head on the next one), so give a repeated floor's stairs an `alt` rect and direction: every other copy uses it, like a switchback. Lay them out so the top of one flight lands at the foot of the next (see the Apartment Block and Office Building).

## Roofs

```json
"roof": { "type": "gable", "ridge": "x", "pitch": 33, "overhang": 0.45, "color": { "color": "$roof", "material": "tiles" } }
```

| Field | Meaning |
|---|---|
| `type` | `flat` (default), `gable`, `hip`, `shed` (one slope, rising towards `dir`) or `none` (open to the sky: a ruin). |
| `ridge` | Gable and hip: the ridge runs along `x` or `z` (default: the longer side). |
| `pitch` | 5 to 60 degrees (default 30). |
| `overhang` | Eaves past the walls, 0 to 1.5 m (default 0.3). |
| `parapet` | Flat roofs: the low wall round the edge, 0 to 2 m (default 0.6; at least a 0.1 m coping is always added). |
| `dir` | Shed roofs: the high side (`+x`, `-x`, `+z`, `-z`). |
| `color` | The roof surface (default dark tiles). |

Pitched roofs cover the top floor's bounding rectangle and are built as stepped layers (the game's chunky look; the collision matches the steps exactly, so players can stand on them). A flat roof with stairs onto it is a fighting position: give it a 1 m parapet.

## Extras: porches, awnings, balconies, platforms, blocks and props

| Type | Fields | What it makes |
|---|---|---|
| `porch` | `rect`, `height` (2.6), `roof` (true), `posts` (true), `color` | A raised floor outside a door, a roof on posts over it (no posts where it meets the building). |
| `awning` | `rect`, `height`, `roof`, `posts` (false), `color` | A canopy (shop awnings, a gas station canopy with `"posts": true`). |
| `balcony` | `rect`, `floor`, `railing` (1.0), `color` | A slab outside an upper floor with railings on its open sides. On a repeated floor every copy gets one. Put a door in the wall behind it. |
| `platform` | `rect`, `floor` (0), `height`, `railing`, `color`, `stairs`: `{ "rect", "dir" }` | A mezzanine inside a tall room: a slab on posts at `height` above the floor, railings on its open sides, and its own stairs (arriving on it). Furniture under it must fit under (`height` - 0.25 m). |
| `block` | `min`, `max` (`[x, y, z]`, y above the ground), `color` | Any box: a chimney, a sign board, a stripe, a step, a loading dock. |
| `prop` | `name`, `at` (`[x, z]` or `[x, y, z]`), `yaw`, `scale`, `onRoof` | A prop outside (or anywhere): bins, signs, AC units, a car. `"onRoof": true` measures y from the roof, so it follows the floor count. |

Wall-mounted props (Window AC Unit, Shop Sign, Neon Sign, Electric Meter) face +Z with their back at -Z: put `at` on the wall's outer face (wall centre line plus half the thickness) and lift them with y.

## Anchors

```json
"anchors": [ { "type": "spawn", "name": "living room", "floor": 0, "at": [-3.5, 2.6] }, { "type": "hill", "name": "roof", "floor": 1, "at": [0, 0] } ]
```

Named points for maps and game modes: `spawn`, `hill`, `flag` or `point`, at `[x, z]` on `floor`. A Building op with `"spawns": true` turns its spawn anchors into spawn points (they're moved to the nearest free spot if furniture is in the way). The others are listed on the map (`Map.BuildingAnchors`) and printed by `buildings.py anchors`, for putting a map's `hills` and `flags` in the right place.

## Furnishing

Each room is furnished by the rule for its `type`: a file in `Buildings/Furnishing/<type>.json` (or the building's own `Furnishing` folder). The rule is a list of items, placed in order on a 0.1 m grid of the room's floor:

```json
{
  "format": "call-of-slop-furnishing/1",
  "type": "bedroom",
  "items": [
    { "id": "bed", "props": ["Double Bed", "Single Bed"], "place": "wall", "prefer": "far" },
    { "id": "bedside", "props": ["Bedside Table"], "place": "beside", "of": "bed", "count": [1, 2] },
    { "props": ["Table Lamp"], "place": "on", "of": "bedside", "y": 0.6, "chance": 0.8 },
    { "props": ["Wardrobe"], "place": "wall", "count": [1, 2] },
    { "props": ["Rug", "Striped Rug"], "place": "centre", "chance": 0.6 },
    { "props": ["Laundry Basket", "Suitcase", "Teddy Bear"], "place": "floor", "count": [1, 3] }
  ]
}
```

| Item field | Meaning |
|---|---|
| `props` | **Required.** Prop names; each copy picks one at random (repeat a name to make it likelier). The building's own props are found first, `"global:Name"` skips them. |
| `place` | **Required.** Where (below). |
| `id` | Names this item so later items can go `on`, `beside`, `in front of` or `around` it. |
| `of` | The `id` of an earlier item (for `on`, `beside`, `front`, `around`). |
| `count` | A number or `[min, max]` (default 1). |
| `chance` | 0 to 1: the item is skipped otherwise (one roll for the whole item). |
| `prefer` | `far` or `near` (from the room's doorways), or `any` (default). Beds and sofas far, shop counters near. |
| `y` | `on`: the surface height above the parent's base (a desk 0.77, a counter 0.9, a bedside table 0.6, a coffee table 0.45; default: the parent's top). `ceiling`: the gap under the ceiling. |
| `gap` | Space kept to other furniture (default 0.08 m; walls and doorway strips may be closer). |
| `distance` | `front` / `around`: the gap to the parent (default 0.1; negative tucks a chair under a desk). |
| `area` | `[min, max]` room area in square metres for the item to apply (a table only in a big kitchen). |
| `turn` | Degrees added to the facing. |
| `scale` | Size (default 1). |

| `place` | Where it goes |
|---|---|
| `wall` | Back against a wall, facing into the room. Tall things (higher than a window's sill + 0.2 m) never stand in front of a window. |
| `corner` | Against a wall, in a corner. |
| `centre` | In the middle of the room, turned to line up with the room's long side (nudged aside if something's there). |
| `floor` | Anywhere free (solid props square to the walls, cosmetic ones at any angle). Litter, toys, boxes. |
| `on` | On top of each piece of the `of` item (cosmetic things: lamps, monitors, kettles). |
| `beside` | Next to the `of` item along its wall, backs lined up (bedside tables: the first left, the second right). |
| `front` | In front of the `of` item, facing it (a chair at a desk; a TV stand `distance` 2.2 in front of a sofa). |
| `around` | Round the `of` item, facing it: two along a long side, one on a short side, long sides first (chairs round a table). |
| `ceiling` | Hanging under the ceiling, spread along the room (ceiling fans, lamps). |

**What the furnishing guarantees**, in every room: a 1.25 m deep strip in front of each doorway and arch (the door's width plus 0.4 m) stays free of furniture; a metre-wide walkway joins every doorway and staircase of the room, so a room you can walk through never gets closed off; the stairs and the spaces at their foot and top stay clear; nothing stands in a wall or outside the room; tall things don't cover windows; solid furniture keeps its `gap`. An item that finds no space is skipped (the preview tool lists those as notes): that's normal for a crowded rule in a small room. The order matters: put the big, important things first.

**Explicit placements.** A room's own `props` go in first, exactly where you say:

```json
"props": [
  { "name": "Car Lift", "at": [-1.6, -1.6] },
  { "name": "Sedan", "at": [-1.6, -1.6], "y": 1.2 },
  { "name": "Workbench", "wall": "W", "along": 1.0 }
]
```

`at` is measured from the middle of the room's bounding rectangle; `wall` (`N`, `S`, `E` or `W`) puts the prop's back against that wall of the room, `along` metres from the wall's middle (+ is east for N and S walls, south for E and W), facing in; `yaw` turns it further; `y` lifts it. Explicit props aren't checked against doorways: keep them clear yourself (`validate` reports any that end up in a doorway).

**The shared rules** (`Buildings/Furnishing`): `living`, `kitchen`, `dining`, `bedroom`, `kidsroom`, `bathroom`, `wc`, `restroom`, `hall`, `corridor`, `laundry`, `office`, `meeting`, `reception`, `breakroom`, `shop`, `kiosk`, `storage`, `garage`, `warehouse`, `ruin`. Add new room types as new files; a building that needs its own take on a type (a diner's dining room, a farmhouse kitchen) keeps that rule in its own folder.

## The building's own props and rules

A building that needs objects nobody else does keeps them in its own folder:

```
Buildings/Diner/
  building.json
  Props/DinerBooth.json         (call-of-slop-prop/1, see Props/README.md)
  Props/Jukebox.json
  Furnishing/diner.json         (call-of-slop-furnishing/1)
```

- **Props** in `Buildings/<Name>/Props` are found by that building (its files, its rules and its room rules) before the map's props and the shared `Props` folder. Two buildings can each have a prop of the same name. `"global:Name"` means the shared one. Put a prop here only if it's specific to this building; anything reusable belongs in `Props/`.
- **Rules** in `Buildings/<Name>/Furnishing` replace the shared rule of the same type for this building only.
- Both count towards the fingerprint of maps that use the building (everyone in a game needs the same files).

## The Building op and variants

```json
{ "op": "Building", "name": "Apartment Block", "at": [60, 15], "yaw": 180, "seed": 3, "spawns": true,
  "variant": { "palette": "brown brick", "floors": 4, "mirror": false, "roof": "flat", "furnish": true, "doors": "closed" } }
```

| Field | Meaning |
|---|---|
| `name` | The building's `name` (or its folder's name). |
| `at` | `[x, z]` or `[x, y, z]`: where the building's origin goes (y: above the ground). On [terrain](../Maps/README.md#terrain) maps the building stands on the highest ground under it, with a foundation down to the lowest. |
| `yaw` | 0, 90, 180 or 270 (degrees, clockwise from above, like a prop's): which way the front faces (0: south). |
| `seed` | Reshuffles the furniture (default: the building's own `seed`). |
| `spawns` | `true`: the building's spawn anchors become spawn points. |
| `variant.palette` | One of the building's palettes. |
| `variant.floors` | The number of floors (buildings with a `repeat` floor). |
| `variant.mirror` | `true`: mirrored east to west (the front door on the other side). |
| `variant.roof` | Another roof type (`flat`, `gable`, `hip`, `shed`, `none`). |
| `variant.furnish` | `false`: empty rooms. |
| `variant.doors` | `closed` (default: each door as its file says), `open` or `random`. |

The old `Building` op without a `name` (`cx`, `cz`, `w`, `d`, `stories`...) still makes the simple blocky buildings it always did.

## Doors and windows in the game

- **Doors**: press the interact key (F) while looking at one within 2.4 m to open or shut it. It swings round its hinge over a third of a second. A shut door blocks movement, bullets, grenades and sight like a wall; an open one is out of the way. It won't shut on someone standing in the doorway. Bots open the doors they walk into (they plan their routes as if doors were open). Bullets, melee and explosions wear a door down (explosions count three times: a frag grenade within about 5 m breaks a wooden door, within about 2 m a metal one; a rifle takes about 5 hits for wood, 14 for metal); a broken door bursts into debris and the doorway stays open for the rest of the match.
- **Windows**: the glass blocks bodies but not bullets: you can shoot through it, and the bullet breaks it (shop windows with more `health` take a few). Melee and grenades in reach break glass too. It shatters with debris and a crash, leaving an empty frame you can climb through.
- **Online**: the host decides. Doors opening, shutting, taking damage and breaking, and windows breaking, are sent to everyone; a player who joins mid-match gets every door's state and health and the broken windows. Every match starts with every door as built and every window whole.

## Destruction

On maps that switch it on (the World Tour pack's `pack.json` has `"destructible": true`; a map file's own `"destructible"` and a Building op's `"destructible"` override it), a building's shell is **cut into chunks** when the map loads and every chunk can break.

- **Chunks**: walls are cut into panels about 1 m wide and 1.25 m tall (rows counted from the building's base), floors, ceilings and flat roofs into 2 x 2 m slabs, big solid blocks into 2 m cubes; small boxes (steps, trim) stay one chunk. Cuts sit on a grid fixed in the world, so a wall and its 2 cm lining are cut in the same places (a lining breaks with the wall behind it). No piece is thinner than 0.3 m (`minPiece`). The openings are already holes in the wall, so they stay as they are. A building has at most `maxPerBuilding` chunks (4,000: the cells grow until it fits) and a map `maxPerMap` (40,000: a building over it stays whole and the log says so).
- **Materials**: each chunk's material is the surface name of its box (the same names bullet-impact decals use: `Concrete`, `Plaster` (pale concrete walls and untextured walls), `Brick`, `Wood`, `Metal`, `Tiles`...; untextured floors are `Concrete`). `Materials/destruction.json` gives each material its health (for a reference 1 x 1.25 m, 0.2 m thick panel; bigger chunks get more by the square root of their volume, 0.5x to 2x), a damage multiplier per kind of hit (`bullet`, `melee`, `grenade`, `launcher`, `streak`, `propBlast`, `landmine`; 0: no effect), its `span` (how far a chunk may reach sideways or hang before it falls), `density` (how hard it lands on someone), the debris size and count, the dust colour and the break sound (any `Sfx` name).
- **Load-bearing**: the foundation, the ground-floor slab and the ground-floor wall chunks within `cornerPillar` (0.8 m) of a footprint corner never break.
- **Support**: chunks that touch face to face are linked. Support flows up from the chunks on the ground: free from the chunk right underneath, the distance between the centres sideways, 1.5 times that hanging from a chunk above. A chunk farther from the ground along its best path than its reach falls: its material's `span`, or what it needed as built plus `spanMargin` (1 m), whichever is more, so every building stands as built. The flood runs over one building, only when something in it broke, in about a millisecond (Dijkstra over its chunk graph, ties broken by index, so every PC gets the same answer from the same events).
- **What breaks with a chunk**: its linings, any door or window glass set in it, the furniture standing on a fallen floor slab.
- **Online**: the host decides what a hit breaks and sends just those chunks (a structure id and 2 bytes a chunk); every PC breaks them and runs the same collapse. A player joining mid-match gets a bit per chunk for each damaged building. Protocol 35.
- **Drawing**: chunks are ordinary shell boxes for the building renderer (one instance each, the same draw calls); a broken one is written with no size, and only the damaged building's run of the static buffer is uploaded again. The `[buildings]` log line counts chunks, broken chunks and uploads.
- **Bots**: the nav graph is built from the map as built; when something breaks the host patches it in slices (0.4 ms a tick): nodes on fallen floors switch off, links across the damage are walked again, and a hole at floor level gets a node on each side (from 160 spares kept on destructible maps) linked to everything around it. The patches are undone at the next match.
- **Fingerprint**: a destructible map's fingerprint includes its chunks and the destruction table (players need the same `Materials/destruction.json`); breaking never changes it. Maps without destruction keep theirs.
- **Test map**: the Dev pack's **Destruction Test** (`Maps/Dev/DestructionTest.json`): a brick house, a wooden house, a corner shop, a concrete bunker, a four-storey apartment block, an office and a garage, test walls of brick, plaster, wood and concrete, a brick tower and barrel chains.

## Bots, foliage and spawns

- **Bots**: every doorway, arch, low hole and staircase of a building adds waypoints (both sides of each doorway, the foot and top of each flight; rooms with neither get one in their middle), so the bots' nav graph runs through the building and up its stairs. Shut doors don't count for planning; bots open them on the way.
- **Foliage** never grows inside a building's footprint, roofed or not (on top of the automatic no-go rules: see [Maps/README.md](../Maps/README.md#no-go-zones)).
- **Spawns**: use `"spawns": true` and spawn anchors, or the map's own spawn ops outside.

## Performance

FPS comes first. How buildings stay cheap:

- **Drawing**: every wall, floor, roof and stair box of every building is one instance in one static buffer, drawn with one instanced draw call per run of visible buildings (buildings out of view are skipped). Doors and glass are a second small buffer, rewritten only while a door swings or something breaks. The Building Yard (18 buildings, about 5,000 boxes) is a handful of draw calls; the same boxes as plain map boxes would be 5,000. The game log prints a `[buildings]` line with the boxes and draw calls.
- **Collision**: building boxes join the map's collision world (one BVH), like any wall. Each box costs a little memory and BVH time; there's no per-frame cost.
- **Furniture** is props: instanced, culled by chunk, cosmetic small things fade with distance (see Props/README.md).
- **Budgets** (a guide): a house is 150-350 boxes and 40-60 props; a big building 500-900 boxes and 150-200 props. Aim for under 30 buildings and 10,000 shell boxes on a map. Each extra opening adds 3-6 boxes (wall pieces round it, both linings); each extra room a few. Repeated floors multiply everything: a 6-storey block is twice a 3-storey one.
- **Building cost**: a building is generated when the map loads, a few milliseconds each (the self-test reports the average and fails over 25 ms).
- **Destructible buildings** (see [Destruction](#destruction)) have three to five times as many boxes (chunks): the same draw calls, a bigger instance buffer and BVH. A collapse costs the server a few milliseconds once; nothing costs anything per frame while nothing breaks.

## Tools

All in `tools/buildings` (Python 3, with numpy and Pillow for pictures). Run from the repository root.

- `python3 tools/buildings/buildings.py validate [NAME...]`: builds every building (or the named ones) in its default form and several variants (mirrored, turned, other seeds, palettes and floor counts) with a Python port of the game's generator, and checks what the self-test checks about the plan: rooms don't overlap, openings fit their walls, stairs fit and land in rooms, every room is reachable from the front door, no solid furniture in doorways or walls, every prop exists. Prints a line per building and the notes (furniture that found no space).
- `python3 tools/buildings/buildings.py preview NAME [OUT.png] [--variant '{"palette": "stone"}'] [--seed N]`: the building from outside, a cut-away view of each floor (walls cut at 1.5 m, furniture in place) and a floor plan of each floor (rooms, walls, doors with their hinges and swing, windows, furniture footprints, doorway strips, waypoints).
- `python3 tools/buildings/buildings.py sheet [OUT.png]`: every building on one contact sheet.
- `python3 tools/buildings/buildings.py anchors NAME X Z [YAW]`: where a building's doors, anchors and footprint land on a map at that placement.
- `tools/buildings/gen_starter.py`: the script the starter set and the Building Yard were first written with: a worked example of building files built in code.

The Python port follows the game's code closely (same layout rules and random numbers), but the numbers can differ in the last decimals; the game's self-test is the final word.

## Checklist

- [ ] A clear theme, carried through the outside, the materials, the rooms and the furniture.
- [ ] `validate` passes with no errors; the notes list only minor things that didn't fit.
- [ ] The preview looks right: walls where you meant, doors where you meant, nothing odd in the furniture.
- [ ] One `"front": true` door; every room reachable; at least one way up each floor.
- [ ] Windows on the outside walls (or a reason not to); ground-floor windows you don't want climbed through have a high sill.
- [ ] Palettes for a couple of variants; extras for the outside (signs, bins, AC, a car).
- [ ] Your own props and rules, if any, in the building's own folder.
- [ ] On the Building Yard (or a test map), loaded in the game, the self-test's "Buildings" section green.
