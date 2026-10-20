# Making maps for Call of Slop

Every map is a single `.json` file, kept in a **map pack**: a folder in this `Maps` folder. The 24 maps that ship with the game are ordinary map files in the `Classic` pack (`Maps/Classic`). Open any of them to see how a real map is put together.

To add a map, drop its file into a pack folder, for example your own `Maps/MyPack/Bunker.json`, then click **Refresh maps** in the lobby. You don't need to restart or recompile. See [Map packs](#map-packs) below.

**Editing a map while you play:** start a single-player game on it, edit and save the file, then press Esc and choose **Reload map files**. The map is rebuilt around you straight away. Anyone left inside a new wall is moved to the nearest open spot. When you're hosting, an edited map takes effect from the next match, because the people connected loaded the old version.

## The quick version

```json
{
  "format": "call-of-slop-map/1",
  "name": "Bunker",
  "description": "Two concrete bunkers and a trench between them.",
  "author": "YourName",
  "order": 100,
  "halfX": 30,
  "halfZ": 25,
  "sky": "#8c9eb8",
  "ops": [
    { "op": "Ground", "color": [0.45, 0.43, 0.4] },
    { "op": "Perimeter", "height": 6, "color": "#9e9ea3" },
    { "op": "Building", "cx": -15, "cz": 0, "w": 12, "d": 10, "stories": 1,
      "wall": [0.5, 0.5, 0.52], "floor": [0.42, 0.42, 0.44], "doors": "E", "roofAccess": true },
    { "op": "Building", "cx": 15, "cz": 0, "w": 12, "d": 10, "stories": 1,
      "wall": [0.5, 0.5, 0.52], "floor": [0.42, 0.42, 0.44], "doors": "W", "roofAccess": true },
    { "op": "CoverLine", "x0": 0, "z0": -20, "x1": 0, "z1": 20, "height": 1.1, "thick": 0.8, "color": "#7a6a50" },
    { "op": "Prop", "x": 0, "z": 0, "w": 2, "h": 2.5, "d": 2, "color": "#f28026" },
    { "op": "RingSpawns", "count": 8, "frac": 0.8 },
    { "op": "AutoWaypoints", "spacing": 8 }
  ]
}
```

Check a file without starting the game:

```
CallOfSlop.exe --check-map Maps\MyPack\Bunker.json
```

This prints `OK` with the box, spawn and waypoint counts, or the exact problem, for example `op #4: Building needs "doors"`. Files that fail to load are skipped, and the lobby shows a red line naming the file. Fix the file and refresh. `CallOfSlop.exe --map-ops` lists every op with its parameters, straight from the game.

## Top-level fields

| Field | Required | Meaning |
|---|---|---|
| `format` | no | `"call-of-slop-map/1"`. Recommended, so future versions can recognise old files. |
| `name` | **yes** | Shown in menus, and it's how servers and clients identify the map. It must be unique. |
| `description` | no | One line, shown in the map list. Plain ASCII only: the game's font has no curly quotes, long dashes, accented letters or symbols like × and °, which show up as `?`. Write `'`, `-`, `x` and `deg` instead. `%` is fine. |
| `author` | no | Shown as "(by ...)" in the map list. |
| `order` | no | Sort position in the list (the shipped maps use 1–24). Ties sort by name. Default 0. |
| `halfX`, `halfZ` | **yes** | Half the map's width (X) and depth (Z) in metres. The playable area runs from `-halfX..halfX` and `-halfZ..halfZ`. The size class is worked out from these: Small up to ~75 m across, Medium up to ~135 m, Large beyond that. Large and Medium maps get distance fog. |
| `sky` | no | Sky and fog colour. |
| `prototypeGrid` | no | `true` draws the dev-grid texture on everything (as in Proto Box). |
| `terrain` | no | Hills, cliffs and valleys instead of a flat floor. See [Terrain](#terrain). |
| `ops` | **yes** | The list of building steps, run in order. |
| `foliage` | no | Grass, bushes, shrubs and trees. See [Foliage](#foliage). |

## Game mode objectives (optional)

Team modes need two teams' spawn points, King of the Hill needs hill zones, and Capture the Flag needs two flags. You can place them with three optional top-level fields. Anything you leave out is worked out automatically, the same way on every PC:

- **Team spawns:** the two spawns furthest apart anchor the teams, and every spawn joins the side it's closer to, split evenly.
- **Hills:** waypoints on open ground that lie on the routes between the two teams' spawns, central and equally far from both teams. Two hills on small maps, three on bigger ones, spread apart. Radius 3.5 m (small), 4.5 m (medium) or 6 m (large).
- **Flags:** on each team's spawn that's deepest in its own half.

| Field | Meaning |
|---|---|
| `teamSpawns` | Two lists of points, Blue's then Red's: `[[[x, y, z], ...], [[x, y, z], ...]]`. A point is `[x, y, z]` or `[x, z]` (on the floor: y = 0, or on the ground on [terrain](#terrain) maps). Each list needs at least one point. |
| `hills` | Hill zones in rotation order: `[{ "x": 0, "z": 10 }, { "x": -20, "z": -5, "y": 3.2, "radius": 5 }]`. `y` (default: the floor, y = 0, or the ground on terrain maps) is the floor height, `radius` defaults to the size-based value. |
| `flags` | Exactly two points, Blue's flag then Red's: `[[-24, 0, 0], [24, 0, 0]]` (or `[x, z]`, on the floor). |

On terrain maps a `y` you give here is a world height, not a height above the ground: leave it out and the point sits on the ground.

```json
  "teamSpawns": [[[-24, -8], [-24, 8], [-20, 0]], [[24, -8], [24, 8], [20, 0]]],
  "hills": [{ "x": 0, "z": 10 }, { "x": 0, "z": -10, "radius": 4 }],
  "flags": [[-26, 0, 0], [26, 0, 0]],
```

These fields (and the waypoints, which the automatic hills use) are part of the map's fingerprint, so host and players must have the same version. Put spawns, hills and flags on open floor: something standing on them shouldn't be inside a wall.

## Units and directions

- Distances are in metres. **Y is up**, the ground is at y = 0 (on [terrain](#terrain) maps, y is measured from the ground), and (0, 0) is the middle of the map.
- North is −Z, south is +Z, west is −X and east is +X. These are used by `doors` on `Building`.
- A player is 1.8 m tall. A storey (`Building`) is 3.2 m. Players can step up 0.55 m without jumping, and can mantle through window openings.
- Colours can be written as `[r, g, b]` (0–1) or `"#rrggbb"`. The game picks a surface texture (concrete, brick, wood, foliage…) from each box's colour and shape.

## Ops

Parameters marked `= value` are optional. Parameter names are case-insensitive, and an unknown name is an error, so typos don't fail silently. Every op also takes an optional `"anchor"` that sets how it sits on terrain (see [Placing ops on terrain](#placing-ops-on-terrain-the-anchor-rule)).

**Basics**

| Op | Parameters | What it makes |
|---|---|---|
| `Ground` | color | The floor slab under the whole map. |
| `Perimeter` | height, color, t = 1 | Boundary walls around the edge. |
| `Block` | x0, y0, z0, x1, y1, z1, color | A box from one corner to the opposite corner. Everything else is built from these. |
| `Prop` | x, z, w, h, d, color, y = 0 | A box w × h × d standing at (x, z), on the ground or on height `y`. |
| `WallX` | x0, x1, z, t, y0, y1, color, holes = [] | A wall running along X from x0 to x1, filling z to z+t, with openings. |
| `WallZ` | z0, z1, x, t, y0, y1, color, holes = [] | The same wall running along Z. |
| `Slab` | x0, z0, x1, z1, yTop, thick, color, hole = null | A floor or roof, optionally with one rectangular hole. |
| `StairsX` / `StairsZ` | (z0, z1, x) or (x0, x1, z), dir, y0, height, color | A solid staircase. `dir` is 1 or −1 and gives the climbing direction. |
| `Ramp` | x, z, width, height, alongX, dir, color, y0 = 0 | A long ramp of small steps up to a platform. |

Wall openings are written as `"holes": [ { "center": 4, "width": 1.5, "bottom": 1.0, "top": 2.3 } ]`, where `center` is measured along the wall. A slab hole is written as `"hole": { "x0": 1, "z0": 1, "x1": 3, "z1": 4 }`.

**Ready-made pieces**

| Op | Parameters | What it makes |
|---|---|---|
| `Building` | cx, cz, w, d, stories, wall, floor, doors, roofAccess, parapet = 1 | A walk-in building with windows on every floor, inside stairs, and optional stairs onto the roof. `doors` is any of `"NSEW"`. It needs a depth of at least 10 m. |
| `Tower` | cx, cz, w, d, h, wall, roof | A solid city block you can't enter. |
| `Hangar` | cx, cz, w, d, h, wall, roof, doorsOnX | A big open shed with wide doors on two ends. |
| `Watchtower` | x, z, height, color, stairDir = 1 | Legs, a railed platform and stairs. |
| `Container` | x, z, alongX, color, y = 0, open = false | A shipping container. An open one is a tunnel. |
| `Car` | x, z, alongX, color | Car-sized cover. |
| `TrainCar` | x, z, alongX, color, open = false | A long wagon. An open one can be stood in. |
| `Plane` | x, z, alongX, color | A blocky aircraft. |
| `Tank` | x, z, r, h, color | A round-ish tank or silo. |
| `Bridge` | x0, z0, x1, z1, h, color, rails = true | A raised walkway on pillars. |
| `CoverLine` | x0, z0, x1, z1, height, thick, color, segment = 6, gap = 2 | A row of low cover with gaps. |
| `RuinWall` | x0, z0, x1, z1, maxH, color, seed | Broken wall chunks at random heights. |
| `Hill` | cx, cz, w, d, height, color, step = 1.4 | Terraced hill you can walk up. |
| `Rocks` | x, z, size, color, seed | A cluster of boulders. |
| `Tree` | x, z, h = 6, y = 0 | A blocky tree. |
| `TreeIfClear` | x, z, h = 6 | A tree, but only if the spot is open. |

**Players and bots**

| Op | Parameters | What it does |
|---|---|---|
| `Spawn` | x, z, y = 0 | A spawn point. Use 8 or more. Spawns inside geometry are moved to the nearest free spot. |
| `RingSpawns` | count, frac, phase = 0 | Spawns spread around an ellipse at `frac` of the map size. |
| `Waypoint` | x, z | A point bots roam between. |
| `AutoWaypoints` | spacing | Waypoints on a grid over all open ground. This is the easy option. |

A map must have at least one spawn.

## Terrain

A map can have real terrain: hills, cliffs, valleys and plateaus instead of a flat floor. Add an optional top-level `"terrain"` object. The game turns it into a **heightfield**, a grid of ground heights every `cell` metres, by running a list of **features** in order. Players walk on it, bullets and grenades hit it, bots see and walk around it, and boxes from the ops are placed on it. Maps without `"terrain"` are exactly as before: flat ground at y = 0.

`Maps/Dev/TerrainTest.json` (the **Dev** pack) is a complete example: a hill with a hut on its cut-off top, a cliff with a ramp cut up its face and a gentle way round its end, a sandy valley and a walled plateau with a ramp.

```json
"terrain": {
  "cell": 1,
  "margin": 20,
  "surface": { "grass": "#5c7f3d", "dirt": "#7a6446", "rock": "#7b7770", "sand": "#b5a577", "sandBelow": -3 },
  "features": [
    { "type": "noise", "amplitude": 0.7, "scale": 20, "seed": 7 },
    { "type": "hill", "x": -28, "z": -26, "radius": 24, "height": 9 },
    { "type": "flatten", "x": -28, "z": -26, "radius": 8, "height": 6.5, "mode": "cut" },
    { "type": "cliff", "points": [[-90, 10], [-14, 10]], "height": 7, "side": "right", "back": 40, "taper": 26 },
    { "type": "ramp", "points": [[-54, -2], [-40, 16]], "width": 4, "material": "dirt" },
    { "type": "valley", "points": [[-4, 40], [26, 30], [80, 38]], "width": 24, "depth": 5 },
    { "type": "plateau", "x": 30, "z": -26, "radius": 13, "height": 7, "edge": 4 },
    { "type": "ramp", "points": [[14, -4], [23.5, -17, 7]], "width": 4, "edge": 2 }
  ]
}
```

### Terrain fields

| Field | Default | Range | Meaning |
|---|---|---|---|
| `cell` | 1 | 0.25–8 | Distance between height samples, in metres. 1 suits most maps; 0.5 for small, detailed maps; 2 for very large ones. Cliffs can't be sharper than about one cell. |
| `margin` | 16 | 0–128 | How far the terrain carries on past the playable edge, so the view doesn't end at a cut-off. Raise the ground out there (a hill or ridge beyond the edge) to frame the map. |
| `base` | 0 | any | Height of every sample before the features run. |
| `min`, `max` | none | any | Optional clamps, applied after all features. |
| `surface` | see below | | Colours and the slope/height rules for grass, dirt, rock and sand. |
| `features` | [] | | The shaping steps, run in order. |

The grid has `(2 × (halfX + margin) / cell + 1) × (2 × (halfZ + margin) / cell + 1)` samples. Keep that under about 250,000 (for example 1 m cells on a 400 × 400 m map with a 40 m margin). It's built once when maps load and drawn in chunks, so a grid that size costs little.

### Features

Every feature has `"type"` and its parameters. Unknown parameters are errors (typos don't fail silently). Heights are in metres. **Shape** features (`hill`, `basin`, `ridge`, `valley`, `cliff`, `noise`, `grid` with `add`) add to the ground. **Level** features (`plateau`, `flatten`, `ramp`/`path`, `grid` with `set`) blend the ground towards a target height.

| Type | Parameters (default) | What it does |
|---|---|---|
| `hill` | x, z, radius, height, radiusZ (= radius), falloff ("smooth") | A round (or, with `radiusZ`, oval) hill. A negative `height` makes a pit. |
| `basin` | x, z, radius, depth, radiusZ (= radius), falloff ("smooth") | A hollow: a hill pointing down. |
| `ridge` | points, width, height, falloff ("smooth") | A ridge along a line through `points` (`[[x, z], ...]`). `width` is the full width at the foot. A point can carry its own height, `[x, z, height]`, and the height changes smoothly along the line, so ridges can rise to a peak. |
| `valley` | points, width, depth, falloff ("smooth") | A ridge pointing down: a river bed, a trench, a canyon. Per-point depths work the same way. |
| `plateau` | x, z, radius **or** w + d, height, edge (6), mode ("fill") | A flat top at the absolute `height`, blended into the ground over `edge` metres around a circle or a w × d rectangle. Small `edge` = steep sides (see slopes below). |
| `flatten` | x, z, radius **or** w + d, height (the ground at x, z), edge (4), mode ("set") | A level pad for buildings and other ops. Without `height` it uses the ground at its centre as it is at that point in the list. |
| `cliff` | points, height, face (2), back (25), side ("left"), taper (8) | An escarpment along `points` (the top edge). One `side` is raised by `height`: the steep `face` drops on the other side over `face` metres, and the high side slopes gently back down over `back` metres. "left" is to your left walking from the first point to the next (with north up on the map, a line going east has its left side to the north). The first and last segments run on past their points, and the cliff fades out over `taper` metres at both ends: a long taper makes a gentle way round the end, a short one makes the end a wall too. |
| `ramp` (or `path`) | points, width (4), edge (3), mode ("set") | A smooth, flat-across road along `points`. A point with a height (`[x, z, y]`) is at that absolute height; a point without one follows the ground there as it is before this feature. Heights change linearly between points. Use it for ramps up to plateaus and cliff tops, roads, and (with `"mode": "cut"`) cuttings through hills. |
| `noise` | amplitude, scale (24), octaves (4), persistence (0.5), lacunarity (2), seed (1), x, z, radius | Natural roughness: fractal value noise of ±`amplitude` metres whose biggest bumps are `scale` metres across. Add `x`, `z` and `radius` to keep it to one patch. Put it first, so pads and ramps flatten it where they need to. |
| `smooth` | passes (1), x, z, radius | Softens the ground (a 3 × 3 blur per pass, up to 20). With `x`, `z`, `radius` only that patch. |
| `grid` | x0, z0, cell, cols, rows, heights, mode ("add") | An explicit height grid for tools: `cols` × `rows` numbers row by row (rows along +Z), the first at (x0, z0), `cell` metres apart, blended bilinearly. "add" adds them, "set" replaces the ground inside the grid. |

Any feature except `noise`, `smooth` and `grid` can also take `"material": "grass" | "dirt" | "rock" | "sand"` to paint its core (where it has at least half its effect) that surface, for example dirt roads.

**Profiles** (`falloff`): `"smooth"` (default; soft top and foot), `"linear"` (a cone or an even ramp), `"dome"` (rounded top, steep foot), `"gauss"` (a bell shape with a long gentle foot).

**Modes** for level features: `"set"` blends towards the target (cut and fill), `"fill"` only raises the ground, and `"cut"` only lowers it. A `cut` flatten slices the top off a hill without building banks round the side. A `fill` plateau keeps any hill that's taller than it.

**Steepness:** a smooth transition of `h` metres over `w` metres has a steepest slope of about `1.5 × h ÷ w` (`1 × h ÷ w` for "linear"). For example, a hill of height 9 and radius 24 peaks at 1.5 × 9 ÷ 24 = 0.56, which is 29°, so it's walkable. A plateau 7 m high with a 4 m edge peaks at 2.6 (69°), so its sides are cliffs.

### Surface

| Field | Default | Meaning |
|---|---|---|
| `grass`, `dirt`, `rock`, `sand` | green, brown, grey, beige | Colours (`[r, g, b]` or `"#rrggbb"`). |
| `dirtSlope` | 24 | Slopes from this many degrees are dirt. |
| `rockSlope` | 38 | Slopes from this many degrees are rock (players can walk up to 42°). |
| `sandBelow` | none | Gentle ground at or below this height is sand: beaches and river beds. |
| `variation` | 0.06 | Large, soft brightness patches (0–0.5) so big fields aren't one flat colour. |

Each sample stores its surface type. Painted features win. Otherwise the rules apply in the order rock, dirt, sand, grass. The drawn colours blend smoothly between them.

### Slopes, cliffs and movement

- Players walk up anything up to **42°**. Steeper ground can't be walked up: walking into it stops you, and standing on it slides you down. Cliffs are real walls.
- Low banks can be **mantled** like crates: a bank up to 1.7 m tall whose top is flat enough to stand on (jump + forward). With a jump that reaches about 2.7 m, and about 3.8 m with the Parkour perk. **Make a cliff at least 4 m tall if it must never be climbed.** Mantling never works up a steep face, only onto a walkable top.
- Comfortable ramps are 15–30° (height ÷ length 0.27–0.58). A `ramp` steeper than 42° is a wall.
- Grenades bounce off the slope they hit and roll; they come to rest on slopes up to about 38°.
- Nobody falls through the terrain or out of the map: the ground is a hard floor everywhere.

### Placing ops on terrain (the `anchor` rule)

On a terrain map every op is placed **on the ground**, so y = 0 in an op means "the ground here". You can change that per op with an `"anchor"` key (it works on every op):

| `anchor` | Placement |
|---|---|
| `"ground"` | The op is built as on flat ground, then the whole thing is raised (rigidly, nothing bends) so it stands on the **lowest** ground under its footprint. The footprint is the area covered by all the boxes the op made, and "lowest" is measured at 9 points: the corners, the edge midpoints and the centre. Nothing floats, but on a slope the uphill side sinks into the ground. |
| `"drape"` | Each box the op made is raised separately to the lowest ground under that box. Good for long, broken-up things that should follow the ground. |
| `"world"` | No change: every y is an absolute world height. Use it for bridges over valleys and floating platforms. |
| `"auto"` (or no `anchor`) | `drape` for `CoverLine`, `RuinWall` and `Rocks`; `world` for `Ground` and `Perimeter` (they handle terrain themselves); `ground` for everything else. |

- **Buildings, hangars, containers and other big ops need a level pad.** Put a `flatten` (or `plateau`) under them, a little bigger than the op (about 2 m each side), so they don't sink into a slope and their doors open onto the ground. A pad cut into a hillside leaves a steep bank on the uphill side; leave room for it, or approach from downhill.
- `Spawn` (`y`) and `Waypoint` are measured from the ground at their own point (with `"anchor": "world"`, `y` is absolute). `RingSpawns`, `AutoWaypoints`, `TreeIfClear` and `Tree` follow the ground too.
- `Ground` adds nothing on a terrain map: the terrain is the floor.
- `Perimeter` walls reach from 1 m below the lowest ground along each edge to `height` above the highest.
- When the map loads, spawns and waypoints under the ground are lifted onto it, and any on ground too steep to stand on (or inside a box) are moved to the nearest good spot. `AutoWaypoints` skips steep ground.

### Bots

Bots pick waypoints they can walk to in a straight line: no box at knee or chest height and, on terrain, no stretch steeper than 42° or hill in between. They walk around cliffs if waypoints exist around them. Use `AutoWaypoints` with a spacing of 6–8 m, and make sure every area of the map is reachable by gentle slopes or ramps.

### Design tips

- **Order matters:** shape first (noise, hills, ridges, valleys, cliffs), then plateaus, then pads and ramps last, so roads and building pads aren't roughened or buried by later features.
- **Hilltop buildings:** `hill` followed by a `"mode": "cut"` `flatten` at a height a little below the top gives a flat summit with natural slopes. A `set` flatten at the very top builds steep banks round it instead, because the blend lands where the hill already falls away.
- **Plateaus with access:** a `plateau` with a small `edge` (cliff sides), plus a `ramp` from the ground to a point just inside its rim at the plateau's height.
- **Cliff with a way round:** a `cliff` with a long `taper` (20 m or more) at the open end. A `ramp` with points on both sides of the face cuts a road up it.
- **Valleys** with `sandBelow` a little above their floor get sandy beds. A `CoverLine` along the floor drapes over the bumps.
- **Sightlines:** hills and ridges block sight like walls do, so use them to break up long lines across open maps.
- Keep slopes where people fight below about 25°, so movement feels smooth.
- `CallOfSlop.exe --check-map` prints the terrain size, height range and how much of it is rock.

### For tools

The game builds the heightfield in double precision, in exactly the order above, and rounds every height to a multiple of 1/1024 m. The surface between samples is bilinear. The heightfield (sample heights, surface ids and grid size) is part of the map's multiplayer fingerprint. The map designer's Python tools carry a line-by-line port of the generator (`terrain.py`). Its fingerprint matches the game's (`TerrainData.Hash`), and the self-test checks that against a lab terrain that uses every feature.

## Foliage

A map can have grass, bushes, shrubs and trees, described in an optional top-level `"foliage"` section next to `"ops"`. Plants are scattered over areas you mark out, or placed one by one. `Maps/Dev/FoliageTest.json` shows every feature. On [terrain](#terrain) maps every plant sits on the ground, and scattered plants skip slopes too steep to walk up (cliff faces).

**Only tree trunks are solid.** Each tree (`tree` or `pine`) gets a box for its trunk, which blocks movement, bullets and bot sight like any other box. Grass, bushes, shrubs and the leafy tops of trees are purely for looks: you can walk and shoot straight through them, and bots see through them. (Plants still hide players visually, so a thicket is real concealment, but never cover.)

| Field | Meaning |
|---|---|
| `seed` | Whole number that reshuffles every region at once. Default 0. |
| `paths` | Lines that scattered plants keep clear of: `{ "points": [[x, z], [x, z], ...], "width": 3 }`. |
| `regions` | Scatter areas (below). |
| `items` | Single plants: `{ "type": "tree", "x": 4, "z": -8, "height": 7 }`. They're always drawn, at every quality setting. |

**Regions**

| Field | Meaning |
|---|---|
| `type` | `grass`, `bush`, `shrub`, `tree` (broadleaf) or `pine`. |
| `density` | Plants per square metre at High quality, 0 to 20. Grass 2–6, bushes 0.2–0.6, shrubs 0.5–1.5, trees 0.02–0.05 (one tree per 20–50 m²) are good starting points. |
| `rect` / `circle` / `polygon` | The area, exactly one of them: `"rect": [x0, z0, x1, z1]`, `"circle": [x, z, radius]` or `"polygon": [[x, z], [x, z], [x, z], ...]`. |
| `seed` | Reshuffles this region only. Default: the region's position in the list. |
| `scale` | Size multiplier for grass, bushes and shrubs: a number or `[min, max]`. Default `[0.8, 1.25]`. |
| `height` | Height in metres for trees, a number or `[min, max]` (2–30). Default `[5, 8]` for trees and `[6, 10]` for pines. |
| `color` | Base colour; each plant varies a little around it. Default: a natural green per type. |
| `patchiness` | 0 = even cover, 1 = strongly clumped into patches with bare ground between. Default 0.35 for grass, 0.25 for bushes and shrubs, 0 for trees. |
| `avoidSolid` | `false` lets plants grow into boxes. Default `true`: they keep out of walls, crates and trunks. |
| `avoidPaths` | `false` lets plants grow on paths. Default `true`. |

Placement is deterministic: the same file gives exactly the same plants and trunks on every PC, so multiplayer collision matches. The foliage counts towards the map's fingerprint, so changing it is a new version of the map online. Scattered trees also keep 1.5 m away from spawn points. Plants are placed after the ops, so they see everything the ops built.

**Cost.** Foliage is drawn in big instanced batches, so tens of thousands of plants are cheap, but not free. As a guide, a 100 x 100 m map with 4 grass clumps per m² everywhere (about 30,000 plants) plus a few hundred bushes and a grove of 40 trees is fine on any recent GPU at High. Grass is by far the most numerous: put dense grass where players look (meadows, along paths) rather than everywhere on a huge map. Trees cost one box each, so keep a map's trees in the hundreds, not thousands. A map may have at most 400,000 plants in total.

Players pick **Off / Low / Medium / High** in Settings > Video. Lower settings draw fewer grass blades, bushes and shrubs and thin them out sooner with distance; trees and their trunks are always there.

```json
"foliage": {
  "seed": 7,
  "paths": [ { "points": [[0, -50], [0, 50]], "width": 3 } ],
  "regions": [
    { "type": "grass", "density": 4, "rect": [-49, -49, 49, 49] },
    { "type": "bush", "density": 0.5, "circle": [26, 28, 9], "scale": [0.9, 1.5] },
    { "type": "shrub", "density": 1, "polygon": [[-44, -42], [-14, -44], [-10, -26], [-42, -16]] },
    { "type": "tree", "density": 0.035, "rect": [10, -44, 44, -14], "height": [5.5, 8.5] }
  ],
  "items": [
    { "type": "tree", "x": -12, "z": 20, "height": 9 },
    { "type": "pine", "x": 24, "z": 16 }
  ]
}
```

The older `Tree` and `TreeIfClear` ops still work: they build a blocky tree out of solid boxes (trunk and leaves both solid), as in the Classic maps.

## Map packs

Each folder directly inside `Maps` is a pack. In the lobby, players first pick a pack, then play a random map from it or choose one of its maps, and the end-of-match vote offers maps from the same pack.

```
Maps/
  README.md            this guide
  Classic/             the shipped pack
    pack.json
    01-ProtoBox.json
    ...
  MyPack/              your pack
    pack.json          optional
    Bunker.json
    night/Bunker2.json subfolders are fine: they still belong to MyPack
  Stray.json           a file straight in Maps goes in the "Other" pack
```

`pack.json` is optional. It describes the pack and is never loaded as a map:

```
{
  "format": "call-of-slop-pack/1",
  "name": "My Pack",
  "description": "Six close-quarters maps built around one bunker.",
  "author": "YourName",
  "order": 10
}
```

| Field | Meaning |
|---|---|
| `format` | `"call-of-slop-pack/1"`. Recommended. |
| `name` | Shown in the lobby's pack list. Without it (or without a pack.json at all) the folder name is used. |
| `description` | One line, shown under the pack's map count. Plain ASCII only, like map descriptions. |
| `author` | Shown as "(by ...)". |
| `order` | Sort position in the pack list (Classic is 1). Ties sort by name. Default 1000. The "Other" pack always comes last. |

Map names must still be unique across **all** packs, because that's how servers and clients find a map. Packs only change how the lobby groups maps; they don't matter online.

**Sharing a pack:** zip the pack folder (for example `MyPack.zip` containing `MyPack/pack.json` and the maps). To install one, unzip it into `Maps` so it becomes `Maps/MyPack/`, then click **Refresh maps**.

## Multiplayer

Servers and clients match maps by **name plus a fingerprint of the geometry**. When you host on your own map, everyone who joins needs the identical file. A player who doesn't have it, or has a different version, is told which map to get. In the server browser, a map you don't have is shown in orange. If you change a published map, give it a new name (for example "Bunker v2") so old and new copies don't clash.

## Tips

- Start from the shipped map closest to what you want: copy its file into your pack folder, rename it, and change `name` and `order`.
- `CallOfSlop.exe --export-maps SomeFolder` rewrites every loaded map in the standard tidy layout, in its pack folder, which is handy after hand-editing.
- Keep cover near spawns, and leave sightlines under ~60 m on Small maps.
- Ops run in order, so `TreeIfClear` and `AutoWaypoints` only see what was built before them. Put them last.
