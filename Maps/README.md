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
| `halfX`, `halfZ` | **yes** (no, with a `boundary`) | Half the map's width (X) and depth (Z) in metres. The playable area runs from `-halfX..halfX` and `-halfZ..halfZ`. The size class is worked out from these: Small up to ~75 m across, Medium up to ~135 m, Large beyond that. Large and Medium maps get distance fog. With a `boundary` they're worked out from its outline (and ignored if given), except as the default size of a `rect` boundary. |
| `boundary` | no | A playable area that isn't a rectangle: a circle, an ellipse, a polygon or a smooth blob, optionally wobbly, with a wall, cliff, fence, hedge, rocks or props round it. See [Boundaries](#boundaries). |
| `maxPlayers` | no | The most players (people + bots) the map takes, 2-32. Leave it out to get the default for the map's size (see [Player limits](#player-limits)). |
| `sky` | no | Flat sky and fog colour (the classic look). Ignored when there's a `skybox`. |
| `skybox` | no | A drawn sky: gradient, sun, moon, stars and clouds, or an image. Its sun and colours also light the map and colour the fog. See [Sky](#sky). |
| `prototypeGrid` | no | `true` draws the dev-grid texture on everything (as in Proto Box). |
| `terrain` | no | Hills, cliffs and valleys instead of a flat floor. See [Terrain](#terrain). |
| `ops` | **yes** | The list of building steps, run in order. |
| `foliage` | no | Grass, bushes, shrubs and trees. See [Foliage](#foliage). |
| `movers` | no | Rotating platforms, sliding platforms and elevators. See [Movers](#movers). |

Trash cans, crates, litter and other props are placed with ops; see [Props](#props).

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
| `Prop` | x, z, w, h, d, color, y = 0 | A box w × h × d standing at (x, z), on the ground or on height `y`. With a `name` instead of a size it places a prop from the Props folder: see [Props](#props). |
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
| `rockSlope` | 38 | Slopes from this many degrees are rock (players can walk terrain up to 45°). |
| `sandBelow` | none | Gentle ground at or below this height is sand: beaches and river beds. |
| `variation` | 0.06 | Large, soft brightness patches (0–0.5) so big fields aren't one flat colour. |

Each sample stores its surface type. Painted features win. Otherwise the rules apply in the order rock, dirt, sand, grass. The drawn colours blend smoothly between them.

### Slopes, cliffs and movement

- Players walk up terrain up to **45°**, measured over their footprint (0.4 m around the feet), so fine noise doesn't count. Walking uphill is full speed up to 25° and slower above that (60% straight up 45°). Steeper ground can't be walked up: walking into it stops you, and standing on it slides you down (at most 8 m/s, and you can steer out sideways). Cliffs are real walls.
- **Grip and terrain steps:** a narrow steep patch (walkable ground within 0.45 m before and after it) that rises at most 0.55 m against the slope around it is walked over, like a stair. A taller bank needs a mantle.
- Low banks can be **mantled** like crates: a bank up to 1.7 m tall whose top is flat enough to stand on (jump + forward). With a jump that reaches about 2.7 m, and about 3.8 m with the Parkour perk. **Make a cliff at least 4 m tall if it must never be climbed.** Mantling never works up a steep face, only onto a walkable top.
- Comfortable ramps are 15–30° (height ÷ length 0.27–0.58). A `ramp` steeper than 45° is a wall. Slopes that should stop players (a scarp, a mesa face) need to be clearly steeper than 45°: aim for 50° or more (a `cliff`, or a `plateau` with a small `edge`). Boxes and meshes keep a 42° limit.
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

Bots pick waypoints they can walk to in a straight line: no box at knee or chest height and, on terrain, nothing a player couldn't walk by the slope rules above, and no hill in between. Uphill links cost the time they take (slower walking), so routes prefer gentler ways up. They walk around cliffs if waypoints exist around them. Use `AutoWaypoints` with a spacing of 6–8 m, and make sure every area of the map is reachable by gentle slopes or ramps.

### Design tips

- **Order matters:** shape first (noise, hills, ridges, valleys, cliffs), then plateaus, then pads and ramps last, so roads and building pads aren't roughened or buried by later features.
- **Hilltop buildings:** `hill` followed by a `"mode": "cut"` `flatten` at a height a little below the top gives a flat summit with natural slopes. A `set` flatten at the very top builds steep banks round it instead, because the blend lands where the hill already falls away.
- **Plateaus with access:** a `plateau` with a small `edge` (cliff sides), plus a `ramp` from the ground to a point just inside its rim at the plateau's height.
- **Cliff with a way round:** a `cliff` with a long `taper` (20 m or more) at the open end. A `ramp` with points on both sides of the face cuts a road up it.
- **Valleys** with `sandBelow` a little above their floor get sandy beds. A `CoverLine` along the floor drapes over the bumps.
- **Sightlines:** hills and ridges block sight like walls do, so use them to break up long lines across open maps.
- Keep slopes where people fight below about 25° (full speed), so movement feels smooth.
- `CallOfSlop.exe --check-map` prints the terrain size, height range and how much of it is rock.

**Lessons from the High Ground pack** (`Maps/HighGround`, see its DESIGN.md):

- **Fair terrain:** build one half and turn it 180 degrees about the centre for the other team (negate every x and z, swap N/S and E/W doors, flip stair and ramp `dir`). A cliff's `side` stays the same when you turn it. Keep `noise` small (under about 1 m) so it doesn't tip the balance.
- **Hill zones need level ground.** Players count as on a hill from 1 m below its centre to 3 m above it, so put a `flatten` pad (radius a little over the zone) under a hill on uneven ground. Hand-place `hills` on the line where both teams have the same walk.
- **Paint without shaping:** a `ridge` or `hill` with `"height": 0.01` and a `material` paints a strip or patch, for example snow (a white `sand` colour) along a crest, without changing the ground.
- **Per-point heights replace `height`:** in a `ridge` or `valley` whose points carry heights, the feature's own `height` only applies to points without one.
- **Pads cut through berms and banks.** A building pad that crosses a berm levels it there and leaves a gap for sightlines. Fill the gap with a wall (taller than eye height, 1.6 m) or move the pad.
- **Cliffs run on past their end points** until their taper fades them out, so stop the line a taper's length short of anything that has to stay walkable.
- **Lifts up a cliff:** an `elevator` at the foot of the face, with a thin `Block` landing at the top that reaches over the face to the edge (the face is `face` metres wide). Give bots a ramp to the same place.
- **Cluttered pockets:** the bots' nav graph samples the ground every few metres. A small space full of cover can end up with too few nodes to join up, so add a couple of `Waypoint` ops there. Each waypoint becomes a nav node.

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

## Movers

A map can have moving solids in an optional top-level `"movers"` list next to `"ops"`: **rotating platforms**, **sliding platforms** and **elevators**. Each one is a few boxes that move together on a constant loop. `Maps/Dev/PhysicsTest.json` shows every kind.

Players standing on a mover ride it: a turning platform carries them round and turns them (and their view) with it, they walk on it as on any floor, and when they step or jump off they keep its speed. A mover pushes players out of its way instead of passing through them; a low mover (a step, 0.55 m or less) scoops them up onto it instead. Nobody is ever crushed: a player who'd be squeezed into a wall is put on top of the mover or nudged up. Bullets, grenades and sight lines hit movers where they are at that moment.

```json
"movers": [
  { "type": "rotate", "name": "Disc", "at": [-20, 0.4, 0], "speed": 20,
    "boxes": [
      { "size": [10, 0.4, 10], "color": "#eb8c26" },
      { "size": [10, 0.4, 10], "yaw": 45, "color": "#eb8c26" },
      { "size": [0.4, 1, 0.4], "offset": [4.5, 0.5, 0], "color": "#26262e" }
    ] },
  { "type": "slide", "name": "Ferry", "points": [[11.6, 2, 12], [22.4, 2, 12]], "speed": 2.5, "dwell": 1.5,
    "boxes": [ { "size": [3, 0.4, 3] } ] },
  { "type": "slide", "name": "Loop", "points": [[8, 0.4, -22], [28, 0.4, -22], [28, 0.4, -34], [8, 0.4, -34]], "loop": true, "speed": 3,
    "boxes": [ { "size": [3, 0.4, 3], "color": [0.62, 0.35, 0.85] } ] },
  { "type": "elevator", "name": "Lift", "at": [20, 0.3, 28], "rise": 3.7, "speed": 1.5, "dwell": 2.5,
    "boxes": [ { "size": [3, 0.3, 3], "color": [0.35, 0.8, 0.35] } ] }
]
```

| Field | Meaning |
|---|---|
| `type` | `rotate` (spins in place about a vertical axis), `slide` (travels along `points`) or `elevator` (goes up `rise` metres and back down). |
| `name` | Optional label (shown in logs and tests). |
| `at` | `rotate`: the pivot. `elevator`: where the platform is at the bottom. A point `[x, y, z]`; with the default box `offset` it's the middle of the platform's **top** surface. |
| `points` | `slide` only: two or more points `[x, y, z]` the mover travels through (its origin, so again the top centre with the default offset). |
| `loop` | `slide` only: `true` goes from the last point straight back to the first; the default goes back and forth. |
| `rise` | `elevator` only: how far up it goes, in metres. |
| `speed` | `rotate`: degrees per second (negative turns the other way). `slide` / `elevator`: average metres per second (0.05 to 30). Default 30 deg/s or 2 m/s. |
| `dwell` | Seconds it waits at each point (`slide`, default 1) or at the top and bottom (`elevator`, default 2). |
| `spin` | `slide` / `elevator`: also turn while travelling, in degrees per second. |
| `yaw` | Starting angle in degrees. |
| `phase` | Seconds to offset the timing, so movers sharing a route don't move in step. |
| `color` | Default colour for its boxes. |
| `boxes` | 1 to 16 boxes in the mover's own frame: `size` `[w, h, d]`, optional `offset` `[x, y, z]` (the box's centre relative to `at`; default `[0, -h/2, 0]`, so the box's top is at `at`), optional `yaw` in degrees (turn a box within the mover, e.g. two squares at 0 and 45 make a near-round disc) and `color`. |

Movement eases into and out of every point (no jolts), and a map may have up to 32 movers.

**Timing is shared by everyone.** A mover's position is worked out from the server's clock, so every PC sees it in the same place without sending anything over the network. Movers count towards the map's fingerprint (maps without them keep theirs).

**Bots** never plan routes through the area a mover sweeps over (its whole path and turning circle): to them it's a pillar they walk around. Waypoints inside that area are dropped, and a spawn inside it is an error. So give bots another way to everything a mover reaches: the Physics Test's elevator deck and gap decks have ramps.

**Design tips**

- Keep a mover's path clear of walls and other movers, and don't put a ceiling over an elevator.
- A platform top 0.3 to 0.5 m above the floor can simply be stepped onto. Parts that stick up more than 0.55 m (a bar on a disc) are walls that sweep players along; players can jump 0.95 m.
- Overlapping boxes with the same top look fine when they're the same colour. Different colours on the same top surface flicker, so make markers stick up (posts) instead.
- Movers are drawn lit but without the surface texture (a texture would slide over a moving surface), so use colours and posts to make the motion visible.

## Boundaries

Without a `boundary` section a map is the classic rectangle `-halfX..halfX` by `-halfZ..halfZ`, exactly as before (same looks, same movement, same fingerprint). Add a top-level `"boundary"` to make the playable area any shape: a circle, an oval, a polygon, a smooth blob such as a bean, with an optional seeded wobble so the edge isn't regular. `Maps/Dev/ShapeTest.json` (the **Dev** pack's "Shape Test") is a complete example: a bean with a curved wall round it.

```
"boundary": {
  "shape": "smooth",
  "points": [[-38, 0], [-32, -15], [-14, -22], [8, -22], [28, -16], [40, -2], [34, 13], [18, 18], [4, 11], [-10, 18], [-28, 16]],
  "noise": { "amplitude": 1.5, "scale": 10, "seed": 3 },
  "edge": { "style": "wall", "height": 4, "thickness": 0.8, "color": [0.56, 0.58, 0.6], "material": "Concrete" }
}
```

**What a boundary does**

- **The outline is the map.** `halfX` and `halfZ` become the outline's bounding box (around the origin), so leave them out. The size class, the player limit and the automatic spawns use the area **inside** the outline: a circle 60 m across counts as about 53 x 53 m, not 60 x 60.
- **Nobody leaves.** An invisible barrier runs exactly along the outline: players (both movement modes), bots, grenades, debris and kill-streak strikes all stay inside, at any height. Bullets and sight lines stop at the barrier up to the edge's height (at least 2.5 m), and fly out over it above that, so a shot over a low fence doesn't spark off thin air.
- **Everything stays inside.** Spawns and waypoints that end up outside (or closer than a player's width to the edge) are moved in like spawns inside a wall. `RingSpawns` follows the outline (its `frac` is a fraction of the way from the middle to the edge in each direction), `AutoWaypoints`, generated spawns, foliage regions and `Clutter` only fill the inside (plants keep 0.3 m from the edge, trees 1 m, clutter 1.1 m), bots never plan a route outside, and hand-placed `hills`, `flags` and `teamSpawns` outside the outline are an error.
- **Looks:** on a flat map the `Ground` op's floor follows the outline plus `groundMargin` metres, with a darker ground beyond that fades into the fog; on terrain the land more than `groundMargin` outside the outline is shaded darker. Map thumbnails frame the outline.
- `Perimeter` is an error on a map with a boundary: use `"edge": { "style": "wall" }` instead.

**Shapes** (`"shape"`, required). Points are `[x, z]`, in order round the outline, either direction.

| Shape | Fields | Example |
|---|---|---|
| `rect` | `halfX`, `halfZ` (default: the top-level ones), `center` `[x, z]` (default `[0, 0]`), `round` (corner radius, default 0) | `{ "shape": "rect", "halfX": 40, "halfZ": 25, "round": 10 }` |
| `circle` | `radius`, `center` | `{ "shape": "circle", "center": [0, 0], "radius": 35 }` |
| `ellipse` | `radiusX`, `radiusZ`, `rotation` (degrees, turned like a prop's `yaw`), `center` | `{ "shape": "ellipse", "radiusX": 50, "radiusZ": 30, "rotation": 20 }` |
| `polygon` | `points` (3 or more corners; straight edges, concave is fine) | `{ "shape": "polygon", "points": [[-30, -25], [30, -25], [30, 25], [10, 25], [10, -5], [-10, -5], [-10, 25], [-30, 25]] }` |
| `smooth` | `points` (3 or more control points; a closed curve passes through every one) | `{ "shape": "smooth", "points": [[-30, 0], [0, -20], [30, 0], [8, 10], [0, 4], [-8, 10]] }` |

`smooth` is a closed centripetal Catmull-Rom curve: it goes through each point in turn and never forms loops or cusps however the points are spaced. 6 to 14 points make good beans, kidneys and blobs. A bean: an oval of points with one or two pulled in towards the middle on one side (the Shape Test's point `[4, 11]` makes its dent).

**Noise** (optional, any shape): `"noise": { "amplitude": 2, "scale": 12, "seed": 1, "octaves": 2 }` pushes the outline in and out along its normal by up to `amplitude` metres (0-30), with bumps about `scale` metres apart (1-200), plus `octaves - 1` finer layers (1-4), each half as long and half as strong. The wobble joins up seamlessly all the way round, and the same `seed` gives the same outline on every PC. Keep `amplitude` well under the tightest curve's radius and under about `scale / 3`, or the outline crosses itself (that's an error naming where).

```
"boundary": { "shape": "circle", "radius": 40, "noise": { "amplitude": 3, "scale": 14, "seed": 7 }, "edge": "rocks" }
```

**Edges** (`"edge"`, optional, default `"none"`): how the boundary is shown. Either a style name, `"edge": "wall"`, or an object with `style` and settings. Fields left out take the style's default.

| `style` | What it is | Settings (default) |
|---|---|---|
| `none` | Nothing visible, only the invisible barrier (use it with your own walls, water, a drop...). | |
| `wall` | A solid-looking wall just outside the outline, one straight piece per outline segment, lengthened at outward corners so the top closes. On terrain each piece reaches from below the lowest ground under it to `height` above the highest. | `height` (4), `thickness` (0.8), `color` (grey), `material` ("Concrete") |
| `cliff` | **Terrain maps only.** The ground outside the outline rises by `height` over `width` metres (level at the outline, steepest half way out), so the edge is a natural cliff or berm. Runs after every terrain feature. Make the terrain `margin` at least `width` + a few metres. | `height` (10), `width` (5) |
| `fence` | Posts every `spacing` metres just outside the outline, with two rails between them. | `height` (1.4), `thickness` (post size, 0.14), `spacing` (2.5), `color` (wood brown), `material` ("Wood") |
| `hedge` | A tall green hedge, like `wall` with a slightly uneven top and shade. | `height` (2.4), `thickness` (1.4), `color` (dark green), `material` ("Dirt"), `seed` |
| `rocks` | Boulders every `spacing` metres, straddling the outline (mostly outside), random sizes up to `height`. | `height` (2.6), `spacing` (2.4), `color` (grey), `material` ("Concrete"), `seed` |
| `props` | Props from the Props folder lined up along the outline, just outside it, each turned to follow it. `props` takes prop names and category names like `Clutter`. Solid props are real cover (and can break; the barrier stays). | `props` (required), `spacing` (3), `seed` |

`material` is one of the box textures (`Concrete`, `Brick`, `Wood`, `Metal`, `Tiles`, `Dirt`, `Grid`) or `""` for plain colour, as on [meshes](#meshes-and-models). Walls, fences, hedges and rocks are drawn as instanced meshes (a few draw calls for the whole edge); they aren't boxes, so they don't count as cover from inside: the barrier is what stops you.

```
"edge": { "style": "cliff", "height": 8, "width": 5 }
"edge": { "style": "fence", "height": 1.2, "spacing": 3 }
"edge": { "style": "hedge", "height": 2.8 }
"edge": { "style": "rocks", "height": 3, "spacing": 2, "seed": 4 }
"edge": { "style": "props", "props": ["Road Barrier", "Sandbag Wall"], "spacing": 3.5 }
```

**Other boundary fields**

| Field | Default | Meaning |
|---|---|---|
| `groundMargin` | 6 | Flat maps: how far the floor is drawn past the outline; terrain maps: where the shading starts. 0-64. |
| `barrierHeight` | the edge's height, at least 2.5 | How high above the ground bullets and sight lines meet the invisible barrier (movement is held in at any height regardless). 0-100. |
| `tolerance` | 0.05 | How far (metres) the straight outline segments may stray from the true curve, 0.005-1. Smaller makes more segments. |

**How the outline is built.** The shape is sampled densely (every 0.25 m on curves), the noise is added, and the result is simplified to the fewest straight segments that stay within `tolerance` of it, so tight curves get many short segments and gentle ones few. Segments are then cut to at most 8 m (walls and the barrier follow the terrain piece by piece) and the outline is checked: closed, not crossing itself, at least 100 m² inside, within 500 m of the centre, at most 4096 segments. That polygon is the one truth: the barrier, the wall pieces, the movement clamp and every "is this inside" test use exactly its vertices. `--check-map` prints the shape, segment count, area and edge.

**Design tips**

- **Odd shapes play differently.** A bean's dent makes a natural pinch point; a long ellipse plays like a lane map; a noisy circle feels like a clearing. Put cover where the shape funnels players, and keep at least 6-8 m between the outline and important cover so nobody gets wedged in a curve.
- **Concave bits block sight.** The barrier is solid up to the edge height, so a dent in a walled bean blocks shots across it just like a wall would. With a low `fence` or `rocks` edge, set `barrierHeight` low (2.5) so players can see and shoot over the dent's tip.
- **Fair halves:** for a point-symmetric map, make the outline symmetric too (for every point `[x, z]` there's a `[-x, -z]`), and use `"mirror": true` on Clutter.
- **Terrain + cliff:** put the map's own hills well inside; the cliff edge rises from the ground as it is at the outline. A `wall` edge on terrain follows the ground segment by segment, so on steep slopes its top steps.
- **Spawns:** `RingSpawns` with `frac` 0.7-0.8 works for any roundish shape. For a U or L shape, place `Spawn` ops yourself.
- **Cost:** the barrier is one collider per segment in the physics world's BVH, and the inside test is a grid lookup away from the edge, so outlines of a few hundred segments cost nothing measurable. The Shape Test bean has 65 segments.

### For tools

The outline is generated in single precision from the spec, deterministically (same file, same outline on every PC). Maps with a boundary mix a fingerprint of the outline and barrier into the map's fingerprint; maps without one keep theirs. The `cliff` edge isn't part of the Python terrain port (`terrain.py`): a map with a cliff boundary gets its terrain fingerprint from the game.

## Props

Props are the small things that make a map look lived in: trash cans, dumpsters, crates, pallets, bricks, newspapers, bottles, benches, lamps. Each one is a file in the `Props` folder next to `Maps` (see [Props/README.md](../Props/README.md) for making your own). The **Prop Gallery** map in the Dev pack shows the starter props in rows by category (**Prop Gallery 3** the indoor and everyday set); **Prop Yard** is a small dressed street to play on.

| Op | Parameters | What it makes |
|---|---|---|
| `PlaceProp` | name, x, z, yaw = 0, scale = 1, y = 0 | One prop standing at (x, z), turned `yaw` degrees. `{ "op": "Prop", "name": "Trash Can", ... }` is the same thing. |
| `Clutter` | props, x0, z0, x1, z1, count, seed = 0, width = 0, align = false, gap = 0.15, mirror = false | Scatters up to `count` props over the rectangle between the corners, or with `width` above 0 along a strip that wide from (x0, z0) to (x1, z1). With `"mirror": true` every prop also gets a twin turned 180 degrees about the map's centre (see below). |

```
{ "op": "Prop", "name": "Dumpster", "x": -15, "z": -11.9 },
{ "op": "Prop", "name": "Street Lamp", "x": 8, "z": 9.4, "yaw": 180 },
{ "op": "Clutter", "props": ["Trash Can", "Bench", "Bollard", "Newspaper"], "x0": -38, "z0": 8.5, "x1": 38, "z1": 8.5, "width": 2.4, "count": 16, "seed": 2, "align": true },
{ "op": "Clutter", "props": ["Debris", "Wooden Crate"], "x0": -38, "z0": 12, "x1": -10, "z1": 30, "count": 40, "seed": 3 }
```

- **Placing:** a prop stands on the ground, or on a low box under it such as a kerb or a step (up to 0.6 m). Give `y` to put it higher (on a table, a roof: y is the height above the ground). On [terrain](#terrain) it stands on the lowest ground under it; with `"anchor": "world"`, `y` is an absolute height.
- **`props`** in Clutter is a list of prop names and category names (`"Debris"` means every prop in the Debris folder). Repeat a name to make it more common; each prop's own `weight` counts too.
- **Clutter turns and sizes** each prop the way its file says (random angle, square to the walls, a size range). With `"align": true` on a strip, props turn to follow it (benches along a pavement).
- **Deterministic:** the same file and `seed` give the same props on every PC. Change the seed to reshuffle.
- **Mirrored maps:** on a map built as one half plus the same half turned 180 degrees (like the High Ground pack), give Clutter the area in one team's half and `"mirror": true`. Each prop then also goes at (-x, -z), facing the other way, and a pair is only placed when both spots are free, so both teams get exactly the same props. `count` is per half. Place hand-placed props in pairs the same way: (x, z, yaw) and (-x, -z, yaw + 180).
- **Clutter runs last**, after every op, the foliage and the objectives, whatever its place in the list, so it can keep out of their way. Every prop stays out of walls, boxes and other clutter. **Solid** props also keep clear of spawns (2.5 m), waypoints (1.2 m), the routes between neighbouring waypoints, hills, flags and movers, and stay 1.1 m from any wall or box they can't step over, so they never close a doorway or a gap a player fits through. If there's no room for all of them, fewer are placed (the log says how many).
- **Collision:** solid props (bins, crates, barriers) block movement, bullets and sight like any box, and bots walk round them. Cosmetic ones (litter, newspapers, bottles) are looks only.
- **Breakable props** (crates, boxes, bottles, bins, chairs...) break when shot, hit or blown up, and every match starts with them whole again. See [Props/README.md](../Props/README.md#breakable-props).
- **Fingerprint:** props count towards the map's fingerprint, so host and players need the same map file and the same Props folder. Maps without props keep their old fingerprint.
- **Cost:** props are drawn instanced, a few hundred on a map cost well under a millisecond. Hand-placed solid props are best for cover you design around; Clutter is best for litter and dressing.

## Meshes and models

Maps aren't limited to boxes: the `Mesh` op places procedural shapes (cylinders, cones, spheres, capsules, rings, ramps, pipes, rounded boxes) and the `Model` op places glTF 2.0 models (`.glb` or `.gltf` files in the `Models` folder; see [Models/README.md](../Models/README.md)). The **Mesh Test** map in the Dev pack shows every shape, a LOD line and the test models.

**They are cosmetic for now:** players, bullets, grenades and bots pass through them, and they don't count towards the map's fingerprint. Where players need cover, put a `Block` or a solid `Prop` under or inside the mesh (a `Block` the size of a pillar inside a `cylinder`, say). Collision for meshes comes with the physics work.

| Op | Parameters | What it makes |
| --- | --- | --- |
| `Mesh` | mesh, x, z, color, y = 0, width = 1, height = 1, depth = 1, radius = 0, inner = 0, segments = 0, detail = 0, yaw = 0, pitch = 0, roll = 0, scale = 1, material = "" | A procedural shape standing at (x, z), on the ground (or terrain) raised by `y`. |
| `Model` | file, x, z, y = 0, yaw = 0, pitch = 0, roll = 0, scale = 1 | A glTF model (`"Models/barrel.glb"`) standing at (x, z), with its own materials. |

Every shape stands on its lowest point (y = 0 is its bottom) and is centred on (x, z). `yaw` turns it about the vertical axis like a prop's yaw, `pitch` tips it forward about x and `roll` about z (degrees, applied roll, then pitch, then yaw), and `scale` grows it evenly. Zero for `radius`, `inner`, `segments` and `detail` means "the default":

| `mesh` | Shaped by | Defaults |
| --- | --- | --- |
| `box` (`cube`) | width (x), height (y), depth (z) | |
| `roundedbox` | width, height, depth; `radius` = edge rounding; `segments` = steps per rounded edge | radius 0.1, segments 3 |
| `cylinder` | width and depth = diameters, height; `segments` around | segments 24 |
| `cone` | as cylinder, coming to a point at the top | segments 24 |
| `sphere` | width, height, depth = diameters (ellipsoids are fine); `segments` around, `detail` = rings | 24 around, 12 rings |
| `icosphere` | as sphere; `detail` = subdivisions 0-5 (an even, geodesic sphere) | detail 2 |
| `capsule` | `radius` (or width / 2), height = total height; `segments`; `detail` = rings per cap | 24, 6 |
| `torus` (`ring`) | `radius` = ring radius, `inner` = tube radius, lying flat; `segments` around, `detail` round the tube | radius 0.4, inner 0.1, 24, 12 |
| `wedge` (`ramp`) | width, height, depth: a ramp rising towards +z (turn it with yaw) | |
| `tube` (`pipe`) | `radius` (or width / 2) = outer radius, `inner` radius, height; `segments` | inner 0.8 x radius, 24 |

`material` gives untextured meshes one of the box textures (`Concrete`, `Brick`, `Wood`, `Metal`, `Tiles`, `Dirt`, `Grid`), mapped in world space exactly like a box's, tinted by `color`. Leave it out for a plain colour.

```
{ "op": "Mesh", "mesh": "cylinder", "x": -6, "z": 4, "color": [0.7, 0.42, 0.35], "width": 1, "height": 3, "segments": 24, "material": "Brick" },
{ "op": "Block", "x0": -6.35, "y0": 0, "z0": 3.65, "x1": -5.65, "y1": 3, "z1": 4.35, "color": [0.7, 0.42, 0.35] },
{ "op": "Mesh", "mesh": "torus", "x": 0, "z": 0, "color": "#e07a5f", "radius": 0.7, "inner": 0.2, "pitch": 90, "y": 0.9 },
{ "op": "Mesh", "mesh": "ramp", "x": 3, "z": 0, "color": [0.7, 0.7, 0.68], "width": 2, "height": 1, "depth": 3, "yaw": 90, "material": "Tiles" },
{ "op": "Model", "file": "Models/barrel.glb", "x": 6, "z": 2, "yaw": 40 },
{ "op": "Model", "file": "Models/lamp_post.gltf", "x": 9, "z": -3, "yaw": 90 }
```

(The `Block` in the example is the solid core of the brick pillar: the mesh gives the look, the block the collision. A cylinder 1 m across fits a 0.7 m block inside without it showing.)

- **Turned meshes:** a shape is turned about its own base point, so tipping one over (`"pitch": 90`) puts half of it under the ground: raise it with `y` (by its radius for a lying capsule or pipe, by `radius + inner` for a standing ring).
- **Mistakes** give a readable message: an unknown shape suggests the closest name (`"cilinder"`: did you mean "cylinder"?), a missing model file lists the models that exist, and a broken model file says what's wrong with it.
- **Cost:** every copy of the same shape (same name and parameters) shares one mesh and is drawn instanced, culled outside the view and simplified with distance. Thousands of meshes cost a handful of draw calls; the game log shows a `[mesh]` line every 5 seconds on maps with meshes (parts drawn, draw calls, LODs, CPU and GPU time).

## Sky

Without a `skybox`, a map has the classic look: the `sky` colour fills the background and the fog, and the sun is the game's usual one. Add a `skybox` and the game draws a real sky, and the map's lighting follows it: the sun's direction and colour, the ambient light and the fog all come from the sky, so distant hills fade into exactly the sky behind them. A `skybox` is looks only: it doesn't change the map's fingerprint, so it never stops anyone joining.

The quickest way is a preset:

```
"skybox": "dusk",
```

| Preset | What it is |
|---|---|
| `clear` (also `clear day`) | Blue sky, the game's usual high sun, fair-weather clouds. |
| `overcast` | Grey and cloudy, soft light, no sun disc. |
| `dusk` (also `sunset`) | Sun just above the western horizon, orange glow, warm clouds, the first stars. |
| `night` | Dark blue sky, moon and stars; the moon lights the map (dimmer, but still playable). |
| `desert` (also `desert haze`) | Hot afternoon, dusty pale horizon, a few thin clouds. |
| `alpine` | Deep blue high-altitude sky, bright sun, puffy clouds. |

Start from a preset and change what you like; anything you leave out comes from the preset (or the defaults when there's no preset):

```
"skybox": {
  "preset": "clear",
  "horizon": "#c8c3b4",
  "sun": { "time": 16.5, "latitude": 35 },
  "clouds": { "coverage": 0.5, "speed": 10 },
  "haze": { "color": "#eadcc0", "strength": 0.4 }
},
```

| Field | Meaning |
|---|---|
| `preset` | One of the presets above. |
| `type` | `procedural` (default), `cubemap` or `equirect`. `faces` or `image` set it for you. |
| `zenith`, `horizon`, `ground` | Gradient colours: straight up, at the horizon, below it. The horizon colour is also the fog colour and the map's background colour. |
| `sun` | `{ ... }`: where the sun is, given one way: `time` (hours, 0-24, 12 = noon) with optional `latitude` (degrees, -90 to 90, default 40) and `day` (1-366, default 80 = March equinox), **or** `elevation` + `azimuth` (degrees up; compass degrees, 0 = north, 90 = east), **or** `direction` `[x, y, z]` pointing at it. Also `heading` (degrees: turns the compass; north is -Z by default), `size` (disc diameter in degrees, default 1.6), `color`, `intensity` (1 = the usual sunlight), `glow` (halo strength) and `disc` (`false` hides the disc). |
| `moon` | `true`, `false` or `{ "direction", "size", "color", "intensity" }`. Without a direction it sits opposite the sun. Once the sun is down, the moon lights the map (`intensity` is moonlight as a fraction of the usual sun). It shows phases: it's lit from the sun's side. |
| `stars` | Star brightness, 0 (none) to 2. Seen only where the sky is dark enough. |
| `clouds` | `false` or `{ ... }`: `coverage` (0 = clear, 1 = covered), `density` (0-1, how opaque), `scale` (pattern size in metres, default 900), `speed` (metres per second), `heading` (compass degrees they drift towards), `altitude` (metres; lower looks bigger and faster), `color` and `shade` (the colour of the thick parts). |
| `haze` | `{ "color", "strength" }`: a glow along the horizon on the sun's side (dusk orange, desert dust). Fog picks it up too. |
| `fog` | `{ "fromSky", "max" }`: `fromSky: false` fogs towards the flat horizon colour instead of the sky in each direction; `max` is how much of the sky colour the fog reaches at its far distance (default 0.85: distant things never fully vanish). The fog distances themselves stay the ones for the map's size. |
| `ambient` | Ambient light multiplier, 0.2-3 (1 = the usual). |
| `image`, `faces` | For image skies, see below. |
| `exposure`, `rotation` | Image skies: brightness (for HDR images, the exposure before tone mapping) and a turn around the vertical axis in degrees. |

**Image skies.** Put the images next to your map file (a `Skies` folder is tidy) and name them relative to the map file:

```
"skybox": { "faces": "Skies/sample_{face}.png", "sun": { "time": 17.3 } },
"skybox": { "image": "Skies/sample.hdr", "exposure": 1.2, "sun": { "time": 17.3 } },
```

- A **cubemap** is six square PNGs of the same size, in the order +X, -X, +Y, -Y, +Z, -Z (OpenGL's cube map layout: north is -Z, up is +Y). Give a list of six names, or one name with `{face}` in it, which becomes `px`, `nx`, `py`, `ny`, `pz` and `nz`.
- An **equirect** (panorama) is one image twice as wide as it is tall: `.png`, or Radiance `.hdr` for high dynamic range (tone mapped in the game). The middle of the image is north (-Z), the top row is straight up.
- Image skies draw only the image (no procedural sun disc, clouds or stars). Still set the `sun` to match the sun in the picture, because it lights the map. Gradient colours you don't give are measured from the image, so the fog matches it.
- Images are read with Brutal.Texture when its native library works on the PC, otherwise with the game's own PNG and RGBE readers. Faces up to 2048 x 2048 and panoramas up to 8192 wide; keep them small (a few hundred pixels is plenty for a sky).
- The sample sky in `Maps/Dev/Skies` was made by `tools/skies/make_sample_sky.py`.

**Mistakes** give a readable message naming the key, for example `Maps/MyMap.json: skybox: clouds: "coverage" is a number from 0 to 1, got 1.5`, or `unknown preset "sunrise" (presets: clear, overcast, dusk, night, desert, alpine)`.

**Try it:** the **Sky Test** map in the Dev pack is open ground with a tall tower, made for looking at skies. **Settings > Video > Sky quality**: Low draws the gradient, sun and moon; High (default) adds clouds and stars. The sky is cheap either way (well under 0.2 ms of GPU time at 1440p); the game log shows a `[sky]` line every few seconds on maps with a sky.

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
| `trueRandom` | `false` keeps the pack out of **True Random** (random from every pack), like the Dev test maps. Its maps can still be picked directly. Default `true`. Packs with `false` are listed last. |

Map names must still be unique across **all** packs, because that's how servers and clients find a map. Packs only change how the lobby groups maps; they don't matter online.

**Sharing a pack:** zip the pack folder (for example `MyPack.zip` containing `MyPack/pack.json` and the maps). To install one, unzip it into `Maps` so it becomes `Maps/MyPack/`, then click **Refresh maps**.

## Multiplayer

Servers and clients match maps by **name plus a fingerprint of the geometry**. When you host on your own map, everyone who joins needs the identical file. A player who doesn't have it, or has a different version, is told which map to get. In the server browser, a map you don't have is shown in orange. If you change a published map, give it a new name (for example "Bunker v2") so old and new copies don't clash.

## Player limits

Servers hold up to 32 players, but every map has its own limit: the lobby's bot slider stops there, people can't join past it (a bot makes room if there is one), and the map vote only offers maps big enough for the people playing (extra bots leave a smaller map and come back on a bigger one). Without `maxPlayers` the limit comes from the playable area:

| Size | Area | Limit |
|---|---|---|
| Small (up to ~75 m across) | under 2,500 m² / above | 8 / 10 |
| Medium (up to ~135 m) | under 9,000 m² / above | 12 / 16 |
| Large (beyond) | under 30,000 m² / above | 24 / 32 |

A map needs **one spawn per player**. If it has fewer, the game adds spawns when it loads the map (your file isn't changed): free spots on rings 3-11 m around your spawns, on the same floor, in sight of the spawn they grow from, clear of walls, other spawns and moving platforms. The result is the same on every PC and doesn't change the map's fingerprint. If even that can't fit enough, the limit drops to the spawn count. `--check-map` prints how many were generated; place your own `Spawn` ops if you'd rather choose the spots.

## Tips

- Start from the shipped map closest to what you want: copy its file into your pack folder, rename it, and change `name` and `order`.
- `CallOfSlop.exe --export-maps SomeFolder` rewrites every loaded map in the standard tidy layout, in its pack folder, which is handy after hand-editing.
- Keep cover near spawns, and leave sightlines under ~60 m on Small maps.
- Ops run in order, so `TreeIfClear` and `AutoWaypoints` only see what was built before them. Put them last.
