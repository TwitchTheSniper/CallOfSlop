# Making maps for Call of Slop

Every map is a single `.json` file, kept in a **map pack**: a folder in this `Maps` folder. The maps that ship with the game are ordinary map files in packs such as `Legacy` (`Maps/Legacy`: the 24 Classic maps, the six Collision Point maps and the six High Ground terrain maps). Open any of them to see how a real map is put together.

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

This prints `OK` with the box, spawn and waypoint counts, or the exact problem, for example `op #4: Building needs "doors"`, then runs the same checks the game's self-test runs on every shipped map (see [Testing a map](#testing-a-map)) and prints each one's result. Files that fail to load are skipped, and the lobby shows a red line naming the file. Fix the file and refresh. `CallOfSlop.exe --check-maps Maps\MyPack` checks every map in a folder. `CallOfSlop.exe --map-ops` lists every op with its parameters, straight from the game.

## Top-level fields

| Field | Required | Meaning |
|---|---|---|
| `format` | no | `"call-of-slop-map/1"`. Recommended, so future versions can recognise old files. |
| `name` | **yes** | Shown in menus, and it's how servers and clients identify the map. It must be unique. |
| `description` | no | One line, shown in the map list. Plain ASCII only: the game's font has no curly quotes, long dashes, accented letters or symbols like × and °, which show up as `?`. Write `'`, `-`, `x` and `deg` instead. `%` is fine. |
| `author` | no | Shown as "(by ...)" in the map list. |
| `order` | no | Sort position in the list (the shipped maps use 1–24). Ties sort by name. Default 0. |
| `halfX`, `halfZ` | **yes** (no, with a `boundary`) | Half the map's width (X) and depth (Z) in metres. The playable area runs from `-halfX..halfX` and `-halfZ..halfZ`. The size class is worked out from these: Small up to ~75 m across, Medium up to ~135 m, Large beyond that. Large and Medium maps get distance fog (and Small maps with a `boundary`, from beyond the playable area only). With a `boundary` they're worked out from its outline (and ignored if given), except as the default size of a `rect` boundary. |
| `boundary` | no | A playable area that isn't a rectangle: a circle, an ellipse, a polygon or a smooth blob, optionally wobbly, with a wall, cliff, fence, hedge, rocks or props round it. See [Boundaries](#boundaries). |
| `maxPlayers` | no | The most players (people + bots) the map takes, 2-32. Leave it out to get the default for the map's size (see [Player limits](#player-limits)). |
| `sky` | no | Flat sky and fog colour (the classic look). Ignored when there's a `skybox`. |
| `skybox` | no | A drawn sky: gradient, sun, moon, stars and clouds, or an image. Its sun and colours also light the map and colour the fog. See [Sky](#sky). |
| `prototypeGrid` | no | `true` draws the dev-grid texture on everything (as in Proto Box). |
| `penetration` | no | `false` stops bullets going through thin walls on this map (on by default; see the main README, "Bullet penetration"). |
| `destructible` | no | `true`: the map's buildings (named Building ops) can be blown apart; `false`: they can't. Without it, the pack's `pack.json` decides (the World Tour has `"destructible": true`; every other pack is off). See [Buildings/README.md](../Buildings/README.md#destruction). |
| `fallKill` | no | A world height: anyone whose feet drop below it dies (they fell off into the void). For maps that float in the air, like the Space pack's sky station: see [Void maps](#void-maps-falling-off). |
| `flagPool` | no | The national flags that decorative flag props pick from at random: a list of flag ids like `["uk", "us"]`, `"none"` (flags keep their own striped cloth) or `"all"` (the default: every flag in `Props/Flags/flags.json`). See [Flags](#flags). |
| `terrain` | no | Hills, cliffs and valleys instead of a flat floor. See [Terrain](#terrain). |
| `gravity` | no | `{ "type": "point", "center": [x, y, z] }` makes a planet: everything falls towards that point. `{ "type": "zones", ... }` gives the map several gravity regions (low gravity, walls you fight on, rocks). `{ "type": "uniform", "strength": 6 }` is low gravity everywhere. Leave it out (or `"uniform"`) for ordinary straight-down gravity. See [Planets](#planets-point-gravity) and [Gravity zones](#gravity-zones). |
| `ops` | **yes** | The list of building steps, run in order. |
| `foliage` | no | Grass, bushes, shrubs and trees. See [Foliage](#foliage). |
| `movers` | no | Rotating platforms, sliding platforms and elevators. See [Movers](#movers). |
| `logic` | no | Map events: buttons, levers, pressure plates, zones and timers wired to doors, lamps, movers, sounds and the Zombies run, with delays and logic gates. See [Map logic](#map-logic). |
| `test` | no | What the self-test (and `--check-map`) must find true of this map: shape, dressing floors, symmetry, fair walks, deck probes, a short bot match. Not part of the fingerprint. See [Testing a map](#testing-a-map). |

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
| `Collider` | x0, y0, z0, x1, y1, z1 | A hidden box: it collides like a `Block` (players, bullets, grenades, bots) but is never drawn. Put a cosmetic `Mesh` over it to make round or curved things solid (see [Void maps](#void-maps-falling-off)). |
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
| `Building` | cx, cz, w, d, stories, wall, floor, doors, roofAccess, parapet = 1 | A walk-in building with windows on every floor, inside stairs, and optional stairs onto the roof. `doors` is any of `"NSEW"`. It needs a depth of at least 10 m. With a `name` instead, it places a furnished building from the Buildings folder: see [Buildings](#buildings). |
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
| `Waypoint` | x, z, y = 0 | A point bots roam between, `y` above the ground (a bridge, a raised deck). |
| `AutoWaypoints` | spacing | Waypoints on a grid over all open ground. This is the easy option. |

A map must have at least one spawn.

**Lights** (looks only: never in the fingerprint; see [Lighting](#lighting))

| Op | Parameters | What it does |
|---|---|---|
| `LightSource` | x, y, z, color, intensity = 1, range = 8, type = "point", yaw = 0, pitch = 90, cone = 60, w = 1, d = 1, flicker = false | A static light baked into the map: a point, a `"spot"` cone or an `"area"` rectangle. |
| `BakedLighting` | cell = 0.5, indoor = 0.25, sunShadows = true, propLights = true, region = null, top = 0, cellY = 0 | Turns baked lighting on and sets it up. |

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

### Holes and caves

Two more feature types cut into the finished ground: **`hole`** (the surface is simply not there) and **`cave`** (a tunnel or chamber under the surface, with a floor, walls and a rock ceiling). They always run **after every height feature** (whatever their place in the list), in file order among themselves, so they follow the ground as it ends up.

| Type | Parameters (default) | What it does |
|---|---|---|
| `hole` | shape (`"circle"`, `"ellipse"` or `"polygon"`), x, z, radius, radiusZ, angle (0), points, depth (4), floor | Removes the surface inside the shape: a circle (`x`, `z`, `radius`), an ellipse (`x`, `z`, `radius` across x, `radiusZ` across z, turned by `angle` degrees) or a polygon (`points`, at least 3). Over a cave it opens into the cave (a skylight, a sinkhole). Anywhere else it's a pit `depth` metres deep, following the ground, or down to the absolute height `floor`. |
| `cave` | points, width (5), height (3.5), depth (6), open ("both") | A tunnel along `points` (`[x, z]` or `[x, z, floor height]`), `width` wide with an arched roof `height` tall: a flat floor in the middle third that curves up into the walls. A point without a height has its floor `depth` under the ground there, except the ends named by `open` (`"both"`, `"start"`, `"end"` or `"none"`), which sit **on the surface**: the tunnel ramps up there and breaks out as an entrance. **One point** makes a round chamber `width` across (a dome `height` tall). Overlapping caves join into one space. |

How it works and what it means for a map:

- The ground is still one grid, but each cell is either **solid** (normal ground), a **hole** (no surface: the ground there is the floor below it) or a **cave** (solid up to the cave floor, open up to its ceiling, then at least **1 m of rock** up to the surface). Holes are cut cell by cell: a cell is a hole when its middle is inside the shape, so on a 1 m grid edges are 1 m steps. Wherever a cave's ceiling comes within 1 m of the surface (where a tunnel ramps up to an entrance) the cells become holes automatically, so the entrance is an open trench that turns into a tunnel once there's a roof.
- Players, bots, bullets and grenades all see the same thing: you fall into holes, walk through tunnels and bump your head on cave ceilings; rays pass through holes and hit the floors, walls and ceilings; grenades drop in and bounce off the walls. Hole sides are vertical rock walls; cave walls are where the curved ceiling meets the floor.
- **Walkable ramps:** keep a tunnel's slope under 30 degrees (the walk limit is 45 degrees over the footprint, with the usual slower uphill walking): an entrance whose floor drops `d` metres needs about `2 × d` metres of tunnel between the surface point and the first buried point. Give buried points explicit heights (`[x, z, -7]`) when the ground above is uneven, so the floor stays level.
- **Headroom:** players are 1.8 m tall standing, so `height` 3 or more is comfortable (the arch is lower at the sides). Chambers want `height` 4-6. The 1 m of roof above the ceiling is enforced (a ceiling that would come closer to the surface is pushed down), so put caves deep enough or raise the ground with a `hill` over them.
- **Light:** cave floors, walls and ceilings are darker the further they are (walking) from an opening, and hole sides and floors get a little less light at the bottom of deep pits. There are no shadows, so boxes and props inside a cave are lit as usual; keep them few.
- **Nothing grows or gets scattered over holes:** foliage and `Clutter` keep clear of them (and their rims). Grass grows on the ground above caves, never inside them. Hand-placed plants over a hole are left out (with a message).
- **Spawns, waypoints and ops** are placed on the surface as before; in a hole that means on its floor. To put something **inside a roofed cave**, use `"anchor": "world"` with an absolute `y` (the cave floor's height). The bots' nav graph finds cave floors itself (it adds nodes along every cave's centre line), so caves don't need waypoints.
- **Pits players can't climb out of** (deeper than 1.7 m, with no tunnel out) are traps: bots never choose to go into them. Make pits shallower than 1.7 m (they can be mantled out of) or give them a way out, like the Cave Test sinkhole that drops into a side passage.
- Keep holes and cave mouths away from the map's edge and from `Perimeter` walls, and put entrances on fairly level ground (a `flatten` pad), or the floor at the mouth won't meet the ground smoothly.
- Maps without `hole` or `cave` features are exactly as before (same look, same fingerprint).

Example (the Dev pack's **Cave Test** map has the full set):

```json
{ "type": "cave", "points": [[-36, 0], [-18, 0, -7], [0, 0, -7], [18, 0, -7], [36, 0]], "width": 6, "height": 3.6 },
{ "type": "cave", "points": [[0, 0, -7]], "width": 16, "height": 5 },
{ "type": "hole", "shape": "ellipse", "x": 3, "z": -3, "radius": 2.5, "radiusZ": 1.6, "angle": 30 },
{ "type": "hole", "shape": "circle", "x": -30, "z": 34, "radius": 3, "depth": 1.4 }
```

A tunnel from a west entrance to an east one through a chamber 16 m across, a skylight into the chamber and a shallow pit you can climb out of.

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

**Lessons from the High Ground maps** (`Maps/Legacy` 31-36, see `DESIGN-HighGround.md` there):

- **Fair terrain:** build one half and turn it 180 degrees about the centre for the other team (negate every x and z, swap N/S and E/W doors, flip stair and ramp `dir`). A cliff's `side` stays the same when you turn it. Keep `noise` small (under about 1 m) so it doesn't tip the balance.
- **Hill zones need level ground.** Players count as on a hill from 1 m below its centre to 3 m above it, so put a `flatten` pad (radius a little over the zone) under a hill on uneven ground. Hand-place `hills` on the line where both teams have the same walk.
- **Paint without shaping:** a `ridge` or `hill` with `"height": 0.01` and a `material` paints a strip or patch, for example snow (a white `sand` colour) along a crest, without changing the ground.
- **Per-point heights replace `height`:** in a `ridge` or `valley` whose points carry heights, the feature's own `height` only applies to points without one.
- **Pads cut through berms and banks.** A building pad that crosses a berm levels it there and leaves a gap for sightlines. Fill the gap with a wall (taller than eye height, 1.6 m) or move the pad.
- **Cliffs run on past their end points** until their taper fades them out, so stop the line a taper's length short of anything that has to stay walkable.
- **Lifts up a cliff:** an `elevator` at the foot of the face, with a thin `Block` landing at the top that reaches over the face to the edge (the face is `face` metres wide). Give bots a ramp to the same place.
- **Cluttered pockets:** the bots' nav graph samples the ground every few metres. A small space full of cover can end up with too few nodes to join up, so add a couple of `Waypoint` ops there. Each waypoint becomes a nav node.
- **The real mover checks the nav graph:** while it builds, every link where the sampled checks can't be sure (a mantle or a drop, steps or a ramp of boxes, a slope steeper than 40 degrees, a squeeze past a ledge, something low or waist-high beside the line, any link on a void map) is walked by the actual movement code, the way a bot follows it. A direction it can't finish gets no edge: a slope you slide back down becomes one-way, a ledge with no room to land on is only a drop, a window you can't climb through isn't a route. Such a link comes back only if it's the one way into somewhere, at a huge cost and never as a bot goal. A building's doorway and stair waypoints stay where the building put them (a shut door beside one doesn't move it). The self-test's "Nav edge walks" section walks every map's edges again and lists any that fail (position, cause, what's in the way). So give ledges room to stand on top, keep windows that bots should use clear of furniture, and keep at least one walkable way into every room and floor.
- **Finding spots where bots get stuck:** every time a bot is stuck long enough for the stuck watch to start its recovery (3 s by default: a hop and a back-off, then a re-plan, then another back-off; after 15 s, or 4 s in a pit with no way out, it kills itself as a last resort), the spot is recorded in the stuck-spot report with the map, position, nearest nav node and cause (blocked, ledge climb, slope, no route, pit, off the nav graph, void), clustered by spot (2.5 m). The game writes the session's report to `Logs/stuck-spots-<date>.json` in the save folder at every match end and logs a per-map summary (`[bots] match over on <map>: ...` and `[bots] stuck spots on <map>: ...`); self-kills are also logged as `[bots] stuck: <bot> on <map> (<mode>) at (x, y, z): <cause>`. The self-test writes `stuck-spots.json` (every bot match of the run) beside `selftest.log`, and CI uploads it with the logs. `CallOfSlop.exe --stuck-report DIR [--out FILE]` merges every report and every `[bots] stuck:` line of the `*.log` files under DIR into one table, worst spots first. Fix a spot by its cause: geometry or prop placement for blocked, ledge and slope spots (a crate in front of a door, a plinth across a shop door), an exit (steps, a door, `Waypoint` ops along a narrow way out) for pits and rooms bots can't leave, nav coverage for off-graph spots. A map's `test` block limits its stuck spots with `maxStuckSpots` (default 0).

### For tools

The game builds the heightfield in double precision, in exactly the order above, and rounds every height to a multiple of 1/1024 m. The surface between samples is bilinear. The heightfield (sample heights, surface ids and grid size, plus the hole and cave data when there is any) is part of the map's multiplayer fingerprint. The map designer's Python tools carry a line-by-line port of the generator (`terrain.py`). Its fingerprint matches the game's (`TerrainData.Hash`), and the self-test checks that against a lab terrain that uses every height feature. Holes and caves aren't part of the port: maps that use them get their fingerprint from the game.

## Foliage

A map can have grass, bushes, shrubs and trees, described in an optional top-level `"foliage"` section next to `"ops"`. Plants are scattered over areas you mark out, or placed one by one. `Maps/Dev/FoliageTest.json` shows every feature. On [terrain](#terrain) maps every plant sits on the ground, and scattered plants skip slopes too steep to walk up (cliff faces).

**Only tree trunks are solid.** Each tree (`tree` or `pine`) gets a box for its trunk, which blocks movement, bullets and bot sight like any other box. Grass, bushes, shrubs and the leafy tops of trees are purely for looks: you can walk and shoot straight through them, and bots see through them. (Plants still hide players visually, so a thicket is real concealment, but never cover.)

| Field | Meaning |
|---|---|
| `seed` | Whole number that reshuffles every region at once. Default 0. |
| `paths` | Lines that scattered plants keep clear of: `{ "points": [[x, z], [x, z], ...], "width": 3 }`. Add `"density": 0.1` for a busted path where an odd tuft still grows (0 to 1, default 0 = nothing). |
| `regions` | Scatter areas (below). |
| `items` | Single plants: `{ "type": "tree", "x": 4, "z": -8, "height": 7 }`. They're always drawn, at every quality setting. |
| `auto` | `false` turns the automatic [no-go rules](#no-go-zones) off. Default `true`. |
| `surfaces` | Foliage allowance per surface, replacing the built-in ones: `{ "Concrete": 0, "Tiles": 0.12 }`. See [No-go zones](#no-go-zones). |
| `exclude` | Areas where scattered plants don't grow, or grow thinner: `[ { "shape": "circle", "center": [x, z], "radius": 5, "density": 0.1 } ]`. |
| `include` | Areas where the automatic rules don't apply (a courtyard garden), optionally on a roof: `[ { "shape": "rect", "center": [x, z], "halfX": 3, "halfZ": 2, "onTop": true } ]`. |

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

Placement is deterministic: the same file gives exactly the same plants and trunks on every PC, so multiplayer collision matches. **Plants hide people from bots** (only bots: bullets and movement pass through): lying in tall grass, crouching behind a chest-high bush, or behind a tree's crown, someone is out of a bot's sight beyond 2.5 m, while a head above the grass is seen. Bushes count about 1 m of leaves per metre, grass a fraction depending on its density, so dense grass (6+ per m²) hides someone lying in it from a standing bot 15 m away and a lawn (2 per m²) mostly doesn't. Bots fire blind into cover where they think you are (see the main README, "Bots and foliage"). The foliage counts towards the map's fingerprint, so changing it is a new version of the map online. Scattered trees also keep 1.5 m away from spawn points. Plants are placed after the ops, so they see everything the ops built.

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

### No-go zones

Nothing grows indoors or on intact paving. When scattering, every plant goes through these rules (hand-placed `items` don't: they're always where the file puts them):

1. **Explicit `exclude` zones** win: inside one, a plant grows only at the zone's `density` (default 0, nothing).
2. **Explicit `include` zones** switch rules 3 and 4 off where you want plants anyway: a courtyard garden under a pergola, a greenhouse. With `"onTop": true`, plants in the zone stand on top of whatever is there (a roof, a planter box, a mesh roof) instead of on the ground: a rooftop garden. Only regions plant things, so a region has to cover the zone too.
3. **Indoors (automatic).** A ray straight up from 12 cm above the plant's base, up to 20 m, through the map's boxes, solid props and cosmetic meshes and models. If it hits anything, the plant is under a roof, a ceiling, an upper floor, a **bridge deck** or an **awning**, or inside a box or a closed mesh, and it's left out. Bushes and shrubs also check the edge of their footprint, so they don't poke through a wall or out from under an eave. Boxes and walls next to a plant are kept clear as before (`avoidSolid`). **Trees are never roofs**: grass grows under canopies, both foliage trees and mesh-built ones (cosmetic `cone`, `sphere`, `icosphere` and `capsule` meshes in a leafy green colour count as canopies). Under bridges and awnings counts as indoors: use an `include` zone if you want grass there.
4. **Pavement (automatic).** A short ray down finds a box or mesh lying on the ground under the plant (a road slab, a tiled market floor, a deck: its top at most 12 cm above the ground and at most 40 cm below it) and uses its surface's **allowance**: the fraction of the normal density that may grow there (0 to 1). Without one it's the terrain's surface type, or `Ground` on a flat map. Anything higher already counts as rule 3. A map's own drawn ground (the floor that follows a boundary outline) is the ground, not a surface.

**Partly allowed ground** (an allowance between 0 and 1: busted pavement, a thinned zone or path) keeps grass tufts and shrubs at that fraction, bushes at half of it and no trees, so cracked paving gets the odd tuft rather than a forest.

Surfaces are named after the game's textures (a box's texture is picked from its colour and shape, a mesh's from its `material`):

| Surface | Built-in allowance | What it is |
|---|---|---|
| `Concrete`, `Tiles`, `Brick`, `Wood`, `Metal`, `Grid` | 0 | Paved and built surfaces: roads, plazas, floors, decks. |
| `Plain` | 0 | An untextured mesh or a model lying on the ground (a rug, a mat). |
| `Dirt` | 1 | Earthy boxes and meshes, and dirt terrain. |
| `Ground` | 1 | A flat map's ground (the `Ground` op), whatever its colour. |
| `Grass`, `Rock`, `Sand` | 1 | Terrain surface types. |

There's no separate cracked texture: mark cracked or busted paving with the `surfaces` table (e.g. Sprawl's `"Tiles": 0.12` for its market floor), an `exclude` zone with a `density`, or a path `density` (Carousel's busted paths use 0.1). Values from 0.05 to 0.15 look like weeds through cracks.

Zone shapes are the [boundary](#boundaries) shapes, with the same fields: `rect` (`center`, `halfX`, `halfZ`, `round`; `halfX` and `halfZ` are required), `circle` (`center`, `radius`), `ellipse`, `polygon` and `smooth` (`points`), plus optional `noise`. Zones may be small (from 0.25 m²).

```json
"foliage": {
  "surfaces": { "Concrete": 0.08 },
  "exclude": [ { "shape": "polygon", "points": [[-10, -4], [10, -4], [10, 4], [-10, 4]] },
               { "shape": "circle", "center": [20, 6], "radius": 6, "density": 0.1 } ],
  "include": [ { "shape": "rect", "center": [-21, -21], "halfX": 2, "halfZ": 2, "onTop": true } ],
  "regions": [ { "type": "grass", "density": 4, "rect": [-40, -40, 40, 40] } ]
}
```

The rules are deterministic like the rest of the placement, so they're part of the foliage fingerprint (a map's online version); maps without foliage are unaffected. They cost about one or two short ray casts per scattered plant against the map's BVH at load (the self-test reports the time per map; Sprawl's 70,000 plants stay well under a second on a desktop). The log says how many plants each rule removed. `"auto": false` brings back the old placement (plus your explicit zones).

The older `Tree` and `TreeIfClear` ops still work: they build a blocky tree out of solid boxes (trunk and leaves both solid), as in the Classic maps (Legacy 1-24).

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
| `running` | `false`: it stands still at the start of its loop until [map logic](#map-logic) starts or advances it. |
| `boxes` | 1 to 16 boxes in the mover's own frame: `size` `[w, h, d]`, optional `offset` `[x, y, z]` (the box's centre relative to `at`; default `[0, -h/2, 0]`, so the box's top is at `at`), optional `yaw` in degrees (turn a box within the mover, e.g. two squares at 0 and 45 make a near-round disc) and `color`. |

Movement eases into and out of every point (no jolts), and a map may have up to 32 movers.

**Timing is shared by everyone.** A mover's position is worked out from the server's clock, so every PC sees it in the same place without sending anything over the network. Movers count towards the map's fingerprint (maps without them keep theirs).

**Bots** never plan routes through the area a mover sweeps over (its whole path and turning circle): to them it's a pillar they walk around. Waypoints inside that area are dropped, and a spawn inside it is an error. So give bots another way to everything a mover reaches: the Physics Test's elevator deck and gap decks have ramps.

**Design tips**

- Keep a mover's path clear of walls and other movers, and don't put a ceiling over an elevator.
- A platform top 0.3 to 0.5 m above the floor can simply be stepped onto. Parts that stick up more than 0.55 m (a bar on a disc) are walls that sweep players along; players can jump 0.95 m.
- Overlapping boxes with the same top look fine when they're the same colour. Different colours on the same top surface flicker, so make markers stick up (posts) instead.
- Movers are drawn lit but without the surface texture (a texture would slide over a moving surface), so use colours and posts to make the motion visible.

## Map logic

A map can wire things together in an optional top-level `"logic"` section: a button opens a gate, walking into a
courtyard lights its lamps, a timer runs a lift, two levers and an AND gate open a vault, turning on the Zombies power
starts a lift and opens a shutter, breaking a wall sets off a cave-in. It works in every mode. `Maps/Dev/LogicTest.json`
(the **Dev** pack's "Logic Test", written by the C# map kit: `Source/MapKit/Generators/LogicTest.cs`) has one of each.

```json
"logic": {
  "entities": [
    { "type": "door", "name": "Gate", "box": [-15, 0, -1.2, -14.7, 2.6, 1.2], "color": "#b0763c" },
    { "type": "button", "name": "GateButton", "at": [-14.64, 1.3, 2.3], "yaw": 90, "prompt": "open the gate",
      "outputs": { "pressed": ["Gate.open", { "to": "Gate.close", "delay": 8 }] } },
    { "type": "zone", "name": "Courtyard", "box": [-6, 0, -6, 6, 3, 6] },
    { "type": "lamp", "name": "Lamp1", "box": [-6.25, 2.6, -6.25, -5.75, 3, -5.75], "color": "#ffd873" }
  ],
  "wires": [
    { "from": "Courtyard.occupied", "to": "Lamp1.turnOn" },
    { "from": "Courtyard.empty", "to": "Lamp1.turnOff", "delay": 2 }
  ]
}
```

### The signal model

- **Entities** are named things (names are 1 to 32 letters, digits, `_` or `-`, unique, any case). Each has **events**
  (outputs: something happened to it) and **actions** (inputs: what it can be told to do).
- A **wire** joins one entity's event to another's action: `"from": "Entity.event"`, `"to": "Entity.action"`. Write
  wires in `"wires"`, or inside the entity that sends them, as `"outputs": { "event": ["Other.action", { "to": ..., ... }] }`.
  Both are the same thing.
- What travels on a wire is a **signal**: a value (true for most events; false for `off`, `released`, `left`, `empty`,
  `closed`, `stopped`; the new state for `changed` and a gate's `out`), a number (a counter's count, a round, a team, a
  zone's head count, a timer's fire count) and who set it off (the player, if anyone). Actions such as `set` or a gate's
  inputs use the value; `teleport.send` and `text` with `"to": "activator"` use the player.
- A wire may add `"delay"` (seconds, 0 to 3600), `"once": true` (it passes one signal a match), `"value": true/false`
  or `"n": 5` (send that instead), and `"when": 5` (pass only a signal whose number is 5: `zombies.roundStarted` with
  `"when": 10` fires on round 10).
- Every entity takes `enable` and `disable`; a disabled entity ignores everything else (a disabled button can't be
  pressed, a disabled zone doesn't count anyone, a disabled timer doesn't fire, a disabled `zdoor` can't be bought).
  `"enabled": false` starts it disabled.
- **Timing.** The server runs the logic in its fixed tick order. Signals wait in a queue for their tick and are
  delivered in the order they were sent; a zero-delay wire is delivered later in the same tick. At most 256 signals
  are delivered a tick, the rest the next tick, so a loop of zero-delay wires can't stall the game (give a loop a delay).
  Nothing is polled except zones and pressure plates (each tick, every living player's feet against their boxes), so a
  map can have a lot of logic for free. The same match plays the same logic on every run.
- **Multiplayer.** Only the server decides. Clients get what they draw (doors, lamps, levers, buttons cooling down,
  plates) and the movers' clocks; a mover map logic starts or stops changes course 0.1 s after the server decides, so
  the platform under a player doesn't jump. Someone joining mid-match gets it all.

This is the model the planned sandbox construct mode's logic is meant to share: typed nodes with named outputs and
inputs, boolean-and-number signals, wires with a delay, and a deterministic queue. A node type added for one (a new
interactable, a gate) fits the other.

### Entities

Positions are world coordinates, `[x, y, z]` (or `[x, z]` on the ground); a box is `[x0, y0, z0, x1, y1, z1]`.

| Type | Fields | Events | Actions |
|---|---|---|---|
| `button` | `at`, `yaw` (its face looks that way: 0 north, 90 east), `reach` (2.2 m), `prompt` (the hint), `cost` (Zombies points; free in other modes), `cooldown` (1 s), `once`, `power` (needs the Zombies power), `draw` (false: you dress it yourself), `color` | `pressed` | `press` |
| `lever` | as `button`, plus `on` (starts on); `cooldown` 0.5 s | `on`, `off`, `changed` | `toggle`, `turnOn`, `turnOff`, `set` |
| `plate` | `box` (a pressure plate: drawn as a pad at its bottom), `who`, `draw`, `color` | `pressed` (the first body on), `released` (the last off), `changed` | |
| `zone` | `box`, `who` (`players`: everyone alive, `humans`, `bots`) | `entered`, `left` (each player), `occupied` (the first in), `empty` (the last out), `changed` | |
| `timer` | `interval` (s), `repeat` (true), `running` (true: it runs from the start of the match) | `fired` (n: how many times) | `start`, `stop`, `toggle`, `reset` (start counting again) |
| `relay` | | `out` | `trigger` (passes the signal on: with `enable`/`disable`, a switch in a chain) |
| `and`, `or` | `inputs` (2 to 4) | `out` (on a change), `true`, `false` | `a`, `b`, `c`, `d` (each input is the signal's value), `reset` |
| `not` | | `out`, `true`, `false` (on a change; it starts true) | `in` |
| `counter` | `target`, `repeat` (start again at 0 on reaching it) | `reached`, `changed` (n: the count) | `add`, `subtract` (by the signal's n, or 1), `set` (to n), `reset` |
| `random` | `choices` (2 to 8) | `out1` ... `out8` (one at random, seeded: the same every run) | `trigger` |
| `door` | `box`, `open` (starts open), `color`, `nav` | `opened`, `closed` | `open`, `close`, `toggle`, `set` |
| `lamp` | `box`, `on`, `color` (lit), `offColor` (dark) | `on`, `off` | `turnOn`, `turnOff`, `toggle`, `set` |
| `mover` | `mover` (a named mover in `"movers"`) | `started`, `stopped`, `arrived` | `start`, `stop`, `toggle`, `advance` (to its next stop: an elevator goes up, then down), `reset` |
| `sound` | `at`, `sound` (a game sound: `reconBeep`, `breakMetal`, `mortarWhistle`, `levelUp`...; the error lists them), `volume` (0-2), `range` (40 m; 0: heard everywhere) | | `play` |
| `text` | `text` (ASCII, up to 100), `to` (`all`: the kill feed; `activator`: only whoever set it off) | | `show` |
| `teleport` | `at`, `yaw` | `sent` | `send` (moves whoever set the signal off) |
| `breakable` | `at` (the breakable prop placed nearest it, within 1.5 m) | `destroyed` (however it broke) | `destroy` |
| `zdoor` | `door` (a Zombies door or debris by name) | `opened` (bought or opened) | `open` (free); `disable` locks it |
| `zmachine` | `machine` (`power`, `box`, `perk`, `slotUpgrade`, `slotMachine`, `wallBuy`, `bench`, `salvage`, `casino`), `at` (which one, when there are several) | `used` (a buy went through) | `disable` locks it |
| `challenge` | `seconds` (to survive), `zone` (a zone or plate its players must stay in; leave it out: anywhere), `who`, `reward` (Zombies points each survivor gets) | `started` (n: how many took it on: everyone alive, standing and in the zone), `survived` (the time ran out with someone still in it; n: how many), `failed` (everyone in it went down, died or left the zone, or `fail`), `ended` (value: survived) | `start`, `stop` / `fail` (it fails), `win` (it's survived now) |
| `reward` | `points` (Zombies points), `powerUp` (`random`, `maxAmmo`, `instaKill`, `doublePoints`, `nuke`, `carpenter`, `infiniteAmmo`) dropped at `at` | `given` | `give` (points to whoever set it off, and the power-up), `giveAll` (points to everyone standing, and the power-up), `drop` (only the power-up) |
| `zclimb` | `box` (the foot of the wall the horde climbs: zombies pile up in it), `top` (where they come over: at least 2 m above the box's floor, beside it), `over` (how far past the top they land, 0-6 m: 1.2) | `started` (n: the round), `reached` (the first zombie came over the top), `stopped`, `given` | `start` (Zombies only, during a round: the horde climbs), `stop`, `giveAll` (the climb's reward, from the rules' `"climb"`, to everyone standing) |

Two entities are always there: **`match`** (events `started`, `ended`, `flagTaken` and `flagCaptured` with n the team,
`hillTaken` (n: the team), `hillMoved` (n: the hill)) and, on maps with a `"zombies"` section, **`zombies`** (events
`power`, `roundStarted` and `roundEnded` with n the round; actions `power` (turn it on), `spawn` (add n zombies to this
round: needs `"n"`), `pause` and `resume` (the spawning)).

**Doors** are solid boxes (bodies, bullets, grenades) while shut and gone while open; drawn as a panel with a band that
sinks into the floor as it opens. A door won't shut on someone standing in it: it shuts the moment the doorway is clear.
Its `"nav"` (default true) is a link for bots through the doorway, across its thin side, 0.9 m out each side on the floor
of the box (or give the two ends: `"nav": [[x, y, z], [x, y, z]]`; `false`: none). The nav graph is built with every
logic door shut, so it is the same whatever the doors do; the link is switched on while the door is open and off while
it's shut, and bots and zombies route by it. Opening doors never changes the map's fingerprint.

**Movers** with `"running": false` stand at the start of their loop until map logic starts them (or advances them a
stop at a time); every mover can be stopped and started. Their pose is still worked out from the clock, so nothing but
the start and stop moments is sent.

### Zombies: a bell that calls the horde (a challenge)

Ringing the bell is a challenge, not a purchase: it floods the horde up the tower, and holding the tower until the bell
stops pays out. A `challenge` counts everyone alive and standing in its zone when it starts; it's survived when its time
runs out with any of them still standing there, and failed when the last of them goes down, dies or leaves.

```json
{ "type": "zone", "name": "Belfry", "box": [-4, 12, -24, 4, 16, -16] },
{ "type": "button", "name": "Bell", "at": [0, 13.4, -20], "yaw": 180, "prompt": "ring the bell", "cooldown": 300, "power": true,
  "outputs": { "pressed": ["Siege.start", { "to": "zombies.spawn", "n": 24 }, "BellSound.play"] } },
{ "type": "challenge", "name": "Siege", "seconds": 60, "zone": "Belfry", "reward": 1500,
  "outputs": { "started": "SiegeText.show", "survived": ["Spoils.drop", "Won.show"], "failed": "Lost.show" } },
{ "type": "reward", "name": "Spoils", "powerUp": "random", "at": [0, 12, -20] },
{ "type": "sound", "name": "BellSound", "at": [0, 15, -20], "sound": "mortarWhistle", "range": 0 },
{ "type": "text", "name": "SiegeText", "text": "The bell tolls. Hold the belfry!" },
{ "type": "text", "name": "Won", "text": "The belfry held." },
{ "type": "text", "name": "Lost", "text": "The belfry fell." }
```

**A horde that climbs the tower** (`zclimb`, Ashgrave Cathedral): wire the bell to a `zclimb` as well as the
challenge. While it runs, zombies at the windows leave them for the foot of the wall (`box`), pile up into a ramp or
pyramid against it a layer at a time, and the rest climb the pile and then the wall to the `top`, vault the parapet and
chase whoever is up there; a shot zombie on the pile falls off (and the ones above it move down a place). `started` adds
the rules' extra zombies to the round. The challenge's `survived` should fire the climb's `giveAll` (the reward is the
rules' `"climb"` section, scaled by the round), and its `ended` the climb's `stop` (everyone still on the wall falls off
and goes back to the windows). The climb is the server's; clients see the climbers' positions and a climbing pose.

```json
{ "type": "button", "name": "Bell", "at": [-28.3, 31.3, -20.25], "yaw": 180, "prompt": "ring the bell", "cooldown": 240,
  "outputs": { "pressed": "Climb.start" } },
{ "type": "challenge", "name": "Siege", "seconds": 75, "zone": "Belfry",
  "outputs": { "survived": "Climb.giveAll", "ended": "Climb.stop" } },
{ "type": "zclimb", "name": "Climb", "box": [-33.6, 12, -30, -24.4, 14.5, -26.02], "top": [-29, 30, -23.6], "over": 1.6,
  "outputs": { "started": "Siege.start" } }
```

(The cathedral's own entities are in its map file, written by its generator's `BellLogic`.)

A `button` can also simply cost points (`"cost": 500`). The power switch can drive the rest of the map: `zombies.power`
to `Lift.start` and `Shutter.open`; a `zdoor`'s `opened` to a `lamp`; `zombies.roundStarted` with `"when": 5` to a
`door`. Disable a `zdoor` or `zmachine` to lock it ("Locked") until something enables it. Points and power-ups are the
Zombies mode's: in other modes a reward only sends `given` (a challenge still starts, ends and sends its events).

### Checking it

`--check-map` refuses a logic section with the reason: an unknown type, field, entity, event or action (with the
nearest name), an event used as an action or the other way round, a mover or Zombies door that isn't there, a sound
the game doesn't have, text that isn't ASCII. The map's `"test"` block can play the logic through (the Map contract
and `--check-map` run it on a server of its own): `"events"` is a list of steps, each doing at most one thing, waiting,
then checking.

```json
"events": [
  { "use": "GateButton", "expect": ["GateButton.pressed", "Gate.opened"], "on": ["Gate"] },
  { "wait": 8.5, "expect": ["Gate.closed"], "off": ["Gate"] },
  { "enter": "Courtyard", "expect": ["Lamp1.on"] },
  { "leave": "Courtyard", "wait": 2.5, "expect": ["Courtyard.empty"], "off": ["Lamp1"] },
  { "do": "LiftTimer.reset", "wait": 9, "expect": ["LiftControl.arrived"] }
]
```

| Key | What it does |
|---|---|
| `use` | a player stands 0.9 m in front of the button or lever and uses it |
| `do` | `"Entity.action"`: delivered as if a wire did |
| `send` | `"Entity.event"`: sent as if it happened |
| `enter`, `leave` | the player stands in the zone or plate (`enter`), or back at the first spawn (`leave`) |
| `wait` | seconds to run after it (0.5 by default) |
| `expect`, `expectNot` | `"Entity.event"`s that must (must not) have gone out during the step |
| `on`, `off` | entities that must be on (open, lit, pulled, true, running) or off afterwards |

The "Map logic" self-test section covers the engine itself (the refusals, doors, nav links, mover control, that the
same steps give the same events, that the logic allocates nothing while it runs, and a host and two clients over a
real network connection).

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
- **Looks:** on a flat map the `Ground` op's floor follows the outline plus `groundMargin` metres, with a darker ground beyond that fades into the sky's horizon colour within a few dozen metres of the edge (the fade starts just past the floor in each of 64 directions, so long or irregular outlines fade right past their edge on every side) (measured from the map, so it blends the same from any camera height, and below the horizon the sky is drawn in its horizon colour too, so there's no dark band under it); on terrain the land more than `groundMargin` outside the outline is shaded darker. Map thumbnails frame the outline.
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

## Void maps (falling off)

A map can float in the air: give it a top-level `"fallKill"` height and leave out the `Ground` op. With no ground there is
nothing under the decks, and anyone whose feet drop below `fallKill` dies: the server kills them like the pause menu's
"Kill yourself" (a death, no kill for anyone, "Suicide" in the feed, the usual respawn). The Space pack's **Stratos
Station** (`Maps/Space/01-SkyStation.json`, written by `tools/space/skystation.py`) is the example.

- **Decks:** round platforms are cosmetic `Mesh` cylinders over hidden `Collider` strips just inside them (collision only,
  never drawn, so they cost no draw calls: map boxes are drawn one by one). Put the mesh's top 2 cm above the colliders.
  Bridges are box meshes turned with `yaw` and tilted with `roll`, over colliders that step up a few centimetres at a time.
- **Railings:** a collider band from 0.45 to 1.1 m above the deck holds people in while bullets, grenades and prone
  shots go under it; draw the railing with meshes. Leave a gap only where a bridge joins.
- **Bots:** the nav graph only links where there's floor under every step (a gap with nothing within a few metres below
  is never crossed), and on a map with `fallKill` a node needs floor under all four sides of a player's footprint, so
  none sits on a railing top or a deck's very edge. The grid can miss a narrow bridge: put `Waypoint` ops (with `y`)
  every few metres along each bridge.
- **The outline:** use a `boundary` with `"edge": "none"` round the whole station; the air between the platforms is
  inside it (you can fall there), the barrier stops anyone leaving the map at any height.
- **Looks:** a sky can't draw anything below the horizon except its ground colour, so a cloud sea belongs in an image
  sky (`tools/space/make_stratos_sky.py` paints one) and, for depth, a few big flattened cosmetic spheres below the decks.
- `fallKill` is from -4 to 55 m. Maps without it are unchanged: nobody falls out.

## Props

Props are the small things that make a map look lived in: trash cans, dumpsters, crates, pallets, bricks, newspapers, bottles, benches, lamps. Each one is a file in the `Props` folder next to `Maps` (see [Props/README.md](../Props/README.md) for making your own). The **Prop Gallery** map in the Dev pack shows the starter props in rows by category (**Prop Gallery 2** the outdoor set: vehicles, rooftop, utility, signage, urban; **Prop Gallery 3** the indoor and everyday set; **Animated Props** the moving ones; **Military Props** the military set in `Props/Military`); **Prop Yard** is a small dressed street to play on.

| Op | Parameters | What it makes |
|---|---|---|
| `PlaceProp` | name, x, z, yaw = 0, scale = 1, y = 0, flag = "", respawn = 0 | One prop standing at (x, z), turned `yaw` degrees. `{ "op": "Prop", "name": "Trash Can", ... }` is the same thing. `flag` is for flag props only: `"us"`, `"uk"`, `"france"`, `"finland"`, `"random"` or `"none"` (see [Flags](#flags)). `respawn` (breakable props only, 1-3600 s) brings it back that long after it breaks, within the match (see [Props/README.md](../Props/README.md#breakable-props)). |
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
- **Mirrored maps:** on a map built as one half plus the same half turned 180 degrees (like the Legacy pack's High Ground maps), give Clutter the area in one team's half and `"mirror": true`. Each prop then also goes at (-x, -z), facing the other way, and a pair is only placed when both spots are free, so both teams get exactly the same props. `count` is per half. Place hand-placed props in pairs the same way: (x, z, yaw) and (-x, -z, yaw + 180).
- **Clutter runs last**, after every op, the foliage and the objectives, whatever its place in the list, so it can keep out of their way. Every prop stays out of walls, boxes and other clutter. **Solid** props also keep clear of spawns (2.5 m), waypoints (1.2 m), the routes between neighbouring waypoints, hills, flags and movers, and stay 1.1 m from any wall or box they can't step over, so they never close a doorway or a gap a player fits through. If there's no room for all of them, fewer are placed (the log says how many).
- **Collision:** solid props (bins, crates, barriers) block movement, bullets and sight like any box, and bots walk round them. Cosmetic ones (litter, newspapers, bottles) are looks only. Cosmetic props marked `"concealment"` (the Farm folder's corn patches) also hide people from bots: see [Props/README.md](../Props/README.md#concealment).
- **Breakable props** (crates, boxes, bottles, bins, chairs...) break when shot, hit or blown up, and every match starts with them whole again. See [Props/README.md](../Props/README.md#breakable-props).
- **Fingerprint:** props count towards the map's fingerprint, so host and players need the same map file and the same Props folder. Maps without props keep their old fingerprint.
- **Pack props:** a pack can carry props of its own in `Maps/<Pack>/Props`, seen only by that pack's maps; `"global:Name"` always means the prop from the `Props` folder. See [Where assets go](#where-assets-go).
- **Cost:** props are drawn instanced, a few hundred on a map cost well under a millisecond. Hand-placed solid props are best for cover you design around; Clutter is best for litter and dressing.

### Flags

Flag props (**Flag on a Pole**, and on request **Pennant Pole** and **Hanging Banner**) fly a national flag from the flag pool in `Props/Flags/flags.json`: the US, UK, French and Finnish flags. A **Flag on a Pole** picks one at random by default; the pick comes from the flag's position, so it's the same on every PC and the same every match, and flags mirrored across a symmetric map fly the same flag. To choose:

```
{ "op": "Prop", "name": "Flag on a Pole", "x": 4, "z": -6, "flag": "france" },
{ "op": "Prop", "name": "Hanging Banner", "x": 0, "z": -24, "y": 3.3, "flag": "uk" },
{ "op": "Prop", "name": "Flag on a Pole", "x": 9, "z": -6, "flag": "none" }
```

- **`flag`** on a placement: a flag id, `"random"` (from the map's pool) or `"none"` (the prop's own striped cloth). Leave it out for the prop's default (random for a Flag on a Pole, its own cloth for pennants and banners).
- **`"flagPool"`** at the top of the map file limits the random ones, e.g. `"flagPool": ["uk", "us"]` for a map set in one place, or `"flagPool": "none"` to keep every flag's own cloth. A placement's own `flag` always wins.
- Flags are looks only: they never change a map's fingerprint. Maps in the Legacy pack (the old Classic, Collision Point and High Ground packs) have no flag props and keep their look; if you add one there, give that map `"flagPool": "none"`. The **Flag Lineup** map in the Dev pack shows all four flags side by side.

## Buildings

`{ "op": "Building", "name": ..., "at": [x, z] }` places a whole building from the `Buildings` folder (see [Buildings/README.md](../Buildings/README.md)): walls with door and window openings, floors and ceilings, stairs, a roof, porches and balconies, doors that open, shut and break, shootable window glass, and furniture placed room by room.

```json
{ "op": "Building", "name": "Small House", "at": [46, -11], "yaw": 180, "seed": 5, "spawns": true,
  "variant": { "palette": "yellow brick", "mirror": true, "floors": 1, "roof": "hip", "furnish": true, "doors": "closed" } }
```

| Parameter | Meaning |
|---|---|
| `name` | The building's name (or its folder's name). Without a `name`, `Building` is the old blocky building op above. |
| `at` | `[x, z]` or `[x, y, z]` (y above the ground): where the building's origin goes. On terrain it stands on the highest ground under its footprint, with a foundation down to the lowest. |
| `yaw` | 0, 90, 180 or 270. At 0 the front faces south (+Z); like a prop's yaw. |
| `seed` | Reshuffles the furniture (default: the building's own). |
| `spawns` | `true`: the building's spawn anchors become spawn points. |
| `variant` | `palette` (one of the building's), `floors` (buildings with a repeating floor), `mirror`, `roof` (`flat`, `gable`, `hip`, `shed`, `none`), `furnish` (`false`: empty), `doors` (`closed`, `open`, `random`). |
| `destructible` | `true` / `false`: this building can (can't) be blown apart, whatever the map's `destructible` says. |

`{ "op": "DestructibleBox", "x0": ..., "y0": ..., "z0": ..., "x1": ..., "y1": ..., "z1": ..., "color": ..., "material": "Brick" }` is a block that breaks like a building's wall (cut into chunks, drawn with the buildings): `material` is a key of `Materials/destruction.json` (`Brick`, `Concrete`, `Wood`, `Plaster`, `Metal`, `Tiles`...) and picks its texture. It stands on whatever is under its lowest chunks. It works on any map (it doesn't need `destructible`).

Everything a building makes counts towards the map's fingerprint: its boxes, its props, its waypoints and its files (building.json, its own props and the furnishing rules it used), so players need the same Buildings folder as the host. Maps without named Building ops keep the fingerprint they always had. Keep buildings apart (and off the map's edge): the op doesn't check for overlaps; `python3 tools/buildings/buildings.py anchors NAME X Z YAW` prints where a placement's doors and footprint land. The Dev pack's **Building Yard** shows every starter building with variants.

## Meshes and models

Maps aren't limited to boxes: the `Mesh` op places procedural shapes (cylinders, cones, spheres, capsules, rings, ramps, pipes, rounded boxes) and the `Model` op places glTF 2.0 models (`.glb` or `.gltf` files in the `Models` folder; see [Models/README.md](../Models/README.md)). The **Mesh Test** map in the Dev pack shows every shape, a LOD line and the test models.

**They are cosmetic by default:** players, bullets, grenades and bots pass through them, and they don't count towards the map's fingerprint (except a model from the map's own pack folder, see [Where assets go](#where-assets-go)). Where players need cover, put a `Block` or a solid `Prop` under or inside the mesh (a `Block` the size of a pillar inside a `cylinder`, say), or give a `Mesh` `"solid": true`: its exact triangles then collide (a tilted slab is a ramp at any angle, a sphere is a rock) and it counts towards the fingerprint, like a planet's surface meshes. Models stay cosmetic.

| Op | Parameters | What it makes |
| --- | --- | --- |
| `Mesh` | mesh, x, z, color, y = 0, width = 1, height = 1, depth = 1, radius = 0, inner = 0, segments = 0, detail = 0, yaw = 0, pitch = 0, roll = 0, scale = 1, material = "", solid = false | A procedural shape standing at (x, z), on the ground (or terrain) raised by `y`. `solid` makes it collide. |
| `Model` | file, x, z, y = 0, yaw = 0, pitch = 0, roll = 0, scale = 1 | A glTF model (`"Models/barrel.glb"`) standing at (x, z), with its own materials. A map in a pack finds `Maps/<Pack>/Models/barrel.glb` first; `"global:Models/barrel.glb"` skips the pack (see [Where assets go](#where-assets-go)). |

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

## Planets (point gravity)

A map can be a small world you walk all the way round: give it a `"gravity"` field that pulls towards a point, put a
`Planet` op there, and place everything else by **latitude and longitude** with the Surface ops. The **Pocket Planet**
map in the Space pack (`Maps/Space/02-Planet.json`, written by `tools/planet/make_pocket_planet.py`) is the example.

```json
  "halfX": 90, "halfZ": 90,
  "gravity": { "type": "point", "center": [0, 0, 0], "strength": 18, "axis": [0, 0, -1] },
  "ops": [
    { "op": "Planet", "radius": 56, "color": [0.55, 0.47, 0.4], "material": "Dirt" },
    { "op": "SurfaceBlock", "lat": 0, "lon": 45, "w": 5, "h": 0.5, "d": 5, "color": "#4d4d57", "material": "Tiles" },
    { "op": "SurfaceMesh", "mesh": "cylinder", "lat": 50, "lon": 90, "color": "#9ea3ad", "width": 0.4, "depth": 0.4, "height": 11 },
    { "op": "Crater", "lat": 35, "lon": 0, "radius": 7, "color": "#6b615c" },
    { "op": "SurfaceScatter", "mesh": "icosphere", "count": 110, "color": "#6b615c", "latMin": -72, "latMax": 72 },
    { "op": "SurfaceSpawn", "lat": 72, "lon": 0 },
    { "op": "PlanetWaypoints", "spacing": 12 }
  ]
```

**`gravity`** fields: `type` (`"point"` or `"uniform"`), `center` (default `[0, 0, 0]`), `strength` (m/s², 1 to 60, default
18, the game's usual; a small world feels right lighter: Pocket Planet is 11) and `axis` (default `[0, 0, -1]`). Players' yaw is measured from "north" in each spot's ground plane,
and that north is the `axis` laid flat on the ground; it has no direction on the two points of the planet the axis passes
through, so **put something solid on both** (Pocket Planet has a monolith on each, at latitude 0, longitude 0 and 180).
Players never notice the frame: the game carries your view along as you walk, so going all the way round never turns it.
`halfX` and `halfZ` must reach past the planet: the distance `max(halfX, halfZ)` from the centre is the top of the sky.

**Latitude and longitude** (degrees): latitude 90 is the +Y pole, -90 the -Y pole; longitude 0 is the +Z side of the
planet and 90 the +X side. Headings are degrees from geographic north (towards latitude 90; at the poles, -Z), turning
like a prop's yaw. Every surface shape stands upright on the ground where it is.

| Op | Parameters | What it makes |
| --- | --- | --- |
| `Planet` | radius, color, x = 0, y = 0, z = 0, detail = 5, material = "Dirt" | The planet: a solid geodesic ball (detail 5 is about 20,000 triangles). One per map; place it first. |
| `SurfaceMesh` | mesh, lat, lon, color, y = 0, width, height, depth, radius, inner, segments, detail, heading = 0, pitch = 0, roll = 0, scale = 1, material = "", solid = true | Any `Mesh` shape standing on the surface, raised by `y` (negative sinks it). **Solid** unless `"solid": false`. |
| `SurfaceBlock` | lat, lon, w, h, d, color, y = 0, heading = 0, sink = 0.3, material = "" | A solid box `w` x `h` x `d` on the surface, sunk `sink` m so its flat bottom never floats over the curve. Walls, cover, steps (0.5 m steps climb), platforms. |
| `Crater` | lat, lon, radius, color, rim = 1.4, count = 0, seed = 1, material = "Dirt" | A ring of leaning rock slabs round a spot. |
| `SurfaceScatter` | mesh, count, color, minSize = 0.8, maxSize = 2.5, seed = 1, latMin = -90, latMax = 90, clear = 4, material = "", solid = true | Random rocks (any shape) over a band of latitudes, kept `clear` m from spawns. |
| `SurfaceSpawn` | lat, lon | A spawn point (and a waypoint) on the ground. Place one per player: planet maps don't generate extra spawns. |
| `SurfaceWaypoint` | lat, lon | A waypoint. |
| `PlanetWaypoints` | spacing = 8 | Waypoints all over the planet, about `spacing` m apart. |

- **Objectives** (`teamSpawns`, `hills`, `flags`) are world points `[x, y, z]`, as usual; give all three (the automatic ones
  are worked out for flat maps). `tools/planet/make_pocket_planet.py` shows how to turn a latitude and longitude into a
  point. A hill zone is a disc across the ground at its point and up to 3 m above it.
- **What works on a planet:** walking, sprinting, jumping, crouching and going prone (always with the capsule movement,
  whatever the server's physics setting), shooting and hit boxes, melee, grenades and launcher rounds, blast pushes,
  ragdolls, gibs and debris, bots (their graph covers the sphere; the horizon hides people from them as from you), every
  game mode. **Not yet:** mantling, the death camera, Insane bots' squads (they play as Hard), and kill streaks that fly
  over the map (strikes, drones, turrets, supply drops; Recon/Counter Sweep, Overdrive, Ballistic Vests, EMP and the
  Juggernaut suit work). Terrain, movers, buildings, props and boundaries are for flat maps.
- **Sky:** in space there's no horizon: on a planet map the procedural sky draws stars, the sun and the moon all the way
  round, and there's no distance fog. A `night` preset with the same dark colour for `zenith`, `horizon` and `ground`, a
  big `moon` (`"size": 26` makes a gas giant) and `ambient` raised to about 2 works well; the sun lights one side of the
  planet, so pick its direction so both bases get some light.
- **Fingerprint:** the gravity field and the solid meshes are part of it.

## Gravity zones

One map can hold **several gravity regions**: a normal deck, a low-gravity hall, a wall or ceiling you fight on, and a
rock with a pull of its own you walk all the way round. Give `"gravity"` the type `zones`: the field at any position is
the **first zone in the list that contains it**, else the `default`. The field is worked out from the position and the
map file on every PC, so nothing new goes over the network. The **Gravity lab** in the "Gravity zones" self-test is a
small worked example; the Space pack's **Keelhaul Yard** (`03-KeelhaulYard.json`) and **Twin Shard** (`04-TwinShard.json`)
are full maps.

```json
  "gravity": { "type": "zones",
    "default": { "type": "uniform" },
    "zones": [
      { "shape": "box", "name": "cargo hall", "min": [-40, -1, -20], "max": [-10, 30, 20],
        "field": { "type": "uniform", "strength": 6 } },
      { "shape": "box", "name": "the wall", "min": [9, 8.3, -24], "max": [18.5, 24, 24],
        "field": { "type": "uniform", "direction": [1, 0, 0] } },
      { "shape": "sphere", "name": "the rock", "center": [0, 12, -14], "radius": 9.5,
        "field": { "type": "point", "center": [0, 12, -14], "strength": 18 } }
    ] },
```

| Field | Meaning |
| --- | --- |
| `default` | The field outside every zone: `{ "type": "uniform", "strength": 18 }` (straight down; leave it out for the usual gravity) or a `point` field. A map-wide uniform field always points straight down. |
| `zones` | 1 to 32 zones, checked in order. `name` is optional (for messages). |
| `shape: "box"` | `min` and `max` corners `[x, y, z]`, at least 0.5 m apart on every axis. |
| `shape: "sphere"` | `center` `[x, y, z]` and `radius` (1 to 400 m). |
| `field` | The zone's gravity: `{ "type": "uniform", "direction": [x, y, z], "strength": 18, "axis": [...] }` (any direction: `[1, 0, 0]` makes the +X face of a wall the floor, `[0, 1, 0]` turns a ceiling into one) or `{ "type": "point", "center": [...], "strength": 18, "axis": [...] }` (a rock). `strength` is 1 to 60 m/s². `axis` is the frame's north (see Planets; by default -Z, or +X when the pull runs along Z). |

A `"gravity": { "type": "uniform", "strength": 6 }` without zones makes the whole map low-gravity.

**How it plays.** A zone map uses the capsule movement everywhere (like a planet). Where the field is the plain
straight-down one, everything is as on a flat map: mantling, props, buildings, boundaries, terrain-free decks. In a zone
with its own up, players walk, jump, crouch and shoot in that frame; grenades, launcher rounds, ragdolls, gibs and debris
fall the zone's way; the hit boxes and the drawn bodies stand along the local up; and when you cross a zone's edge your
view keeps pointing where it did while the horizon rolls round over about a quarter of a second. A jump at 6 m/s² goes
about three times as far and as high as at 18 m/s², so a low-gravity gap can be 10-15 m.

**Rules of thumb.**
- **Zones cover bodies, not just feet.** A player's field is read at their feet, but a grenade is thrown from their eyes:
  make a wall zone reach at least 2 m out from the wall, and don't let another zone (listed earlier) poke into it.
- **Bots and the mover agree.** The nav build and the "Nav edge walks" self-test walk zone links with the movement the
  game uses there (the capsule mover in the local field), so a link only stays if a player really walks it.
- **Transitions bots can walk.** The bots' graph gets nodes in every zone whose up isn't straight down (probed along the
  zone's own up, over its bounds: a grid for a uniform zone, a sphere round the centre for a point zone) and links any two
  nodes a player could walk between along the local ground, which must be continuous: a surface within a step (0.55 m),
  never steeper than 42 degrees from the local up on either side of the edge. So turn gravity round in **steps of at most
  30 degrees**: a quarter pipe of solid slabs (`Mesh` with `"solid": true` and `roll`) under three zones whose
  directions are 30, 60 and 90 degrees round, each zone's edge where the pipe's slope is halfway between its neighbours'
  (the lab does this: nine slabs 10 degrees apart, zone edges at 15, 45 and 75 degrees). A rock is reached by a pier
  ending just short of its top, with the pier in a plain-gravity box zone listed **before** the rock's sphere, so the pier
  stays level right up to the step off. Make that box only the deck's footprint, from just under its top: wider, and
  people beside the deck stand in plain gravity on the rock's curve and slide off it. A plain wall between two frames is not walkable: players can jump or fall across
  it, bots won't try.
- **Spawns, hills and flags** go in plain-gravity areas: spawns are checked and generated the flat way, and the automatic
  objectives are worked out for flat ground. Give `teamSpawns`, `hills` and `flags` yourself.
- **Solid meshes** (`"solid": true` on `Mesh`, the `Planet` op and the Surface ops) collide exactly, so a tilted slab is a
  ramp at any angle. A `Planet` op makes the rock and lets the Surface ops dress it by latitude and longitude, whatever the
  map's gravity.
- **Not yet, in a zone with its own up:** mantling (fine on the plain parts), the death camera, Insane bots' squads (they
  play as Hard) and the kill streaks that fly over the map, as on a planet. The "Stairs" self-test walks every flight of
  steps the straight-down way, so keep runs of three or more steps out of sideways zones.
- **Fingerprint:** the default field and every zone's shape, bounds and field are part of it.

### The host's gravity setting

The host's *Gravity* (25% to 200%, Host page and lobby room; `Source/Game/GameGravity.cs`) multiplies every field a map
declares, the default and every zone alike, and everything that falls. Your map's numbers are what it plays at 100%.
Design for 100%, but keep in mind:
- **Planets have a floor.** A map whose whole gravity is a point field plays at no less than the percentage that keeps a
  sprinting player on the ground with room to spare (2 x sprint speed² / radius, rounded up to 5%; Pocket Planet: 60%).
  Every PC works it out from the map, so nothing more is sent.
- **Jumps scale as 1/g.** At 50% a gap twice as long can be jumped, at 200% half. A low-gravity zone stays relatively low.
  Railings (1.1 m) can be jumped at 25% (a 4 m jump), so a void map is riskier there; bots don't hop in fights in low
  gravity over a void.
- **Bots' graphs don't change.** The nav graph is built at 100% for every setting (its links are walks, mantles and drops),
  so it's deterministic and cached once per map.

## Decals

Decals are flat pictures projected onto surfaces: oil stains, cracks, manhole covers, road markings, graffiti, posters, soot. They come from the `Decals` folder (one file each, see [Decals/README.md](../Decals/README.md)) and are placed with two ops. The **Decal Test** map in the Dev pack shows them on floors, walls, a kerb, a mesh and the terrain round a hole. The **Decal Surfaces** map shows every placeable decal in a street, two rooms (ceiling decals too) and a wall gallery; [the catalogue](../Decals/README.md#the-catalogue) lists them. The **Decal Impacts** map has a target of every bullet-impact surface.

| Op | Parameters | What it makes |
| --- | --- | --- |
| `Decal` | decal, x, z, y = 0, onto = "floor", size = 0, rotation = -1, tint = "", reach = 3, seed = 0 | One decal, projected from (x, y, z) `onto` the first surface it finds. |
| `DecalScatter` | decals, x0, z0, x1, z1, count, seed = 0, onto = "floor", y = 0, minSize = 0, maxSize = 0, tint = "", gap = 0 | `count` decals from the list scattered over the rectangle. |

- **`onto`:** `"floor"` looks down from 1 m above `y` (so give the floor's height: 0 on the ground, 3.2 on an upper floor, 0.8 on a table), `"ceiling"` looks up from `y`, `"wall+x"`, `"wall-x"`, `"wall+z"` and `"wall-z"` look from (x, y, z) towards that side (`"wall-z"` puts a poster on a wall north of the point), and `"wall"` takes the nearest wall in any of the four directions. `reach` is how far it looks (metres). On terrain maps `y` is above the ground at (x, z), like every other op (`"anchor": "world"` makes it absolute).
- **Size and turn:** `size` is metres across the decal's longer side (0: the decal's own size and its random variation); `rotation` is degrees (-1: the decal file's rule, usually random). On floors the top of the picture points north (-z) before turning; on walls it points up. `tint` (a colour name or `"#rrggbb"`) is multiplied in.
- **Scatter:** sizes are random between `minSize` and `maxSize` (both 0: each decal's own), at least `gap` metres apart, and deterministic for a `seed`. With `"onto": "wall"` each decal goes at a random height from `y + 0.3` to `y + 2.4` m onto the nearest wall within 3 m. Repeat a name to make it more common. A decal only lands on surfaces its file allows (a floor-only oil stain never goes on a wall), so a scatter can place fewer than `count` (the log says how many).
- **What they land on:** boxes and buildings (walls, floors, roofs, doors), the terrain, and `Mesh` / `Model` shapes. Not props (they are models; their collider is only a box), glass, Zombies window openings or movers. A decal wraps over edges, kerbs and steps within its depth (about a third of its size), fading where its depth runs out, and never covers a terrain hole. A decal on a door or a breakable box disappears while that box is open, broken or moved, and comes back when it's restored.
- **Cosmetic:** decals add no collision and are **not part of the map's fingerprint**, so adding, moving or removing decals never stops players with an older copy of the map from joining (they just see different decals). Decals come from the global `Decals` folder only (no pack-local decals yet).
- **Cost:** a map's decals are baked once into one buffer (the log shows a `[decals] baked` line with the time) and drawn in a few draw calls, culled by 24 m chunk and faded out with distance. A map may ask for up to 6000 decals.

```
{ "op": "Decal", "decal": "Manhole Cover", "x": 0, "z": 0 },
{ "op": "Decal", "decal": "Graffiti Tag", "x": -8, "z": -6, "y": 1.8, "onto": "wall-z", "size": 2.6 },
{ "op": "DecalScatter", "decals": ["Oil Stain", "Oil Stain", "Floor Cracks"], "x0": -20, "z0": -10, "x1": 20, "z1": 10, "count": 20, "seed": 3, "gap": 2.5 }
```

Bullet impacts are decals too, but they are made by shots during play, not placed by maps (see [Decals/README.md](../Decals/README.md)).

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

**Image skies.** Put the images next to your map file (a `Skies` folder is tidy) and name them relative to the map file. The game looks next to the map file first, then in the map's pack folder (for a map in a subfolder of its pack), then in the `Maps` folder itself; `"global:Skies/x.png"` looks in the `Maps` folder only (see [Where assets go](#where-assets-go)):

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

## Lighting

A map can have **baked lighting**: torches, candles, lamps, spotlights and glowing windows whose light, with the sun's
shadows and how much sky each spot sees, is worked out once when the map is built and stored with it, then read per
pixel at almost no cost (docs/render/LIGHTING.md). It lights everything the same way: walls, props, players, zombies,
your gun. A map is lit when it has a `BakedLighting` or a `LightSource` op; every other map looks exactly as before,
even with lamp props on it. Lights are looks only (not in the fingerprint), so lighting a map never stops anyone joining.

```json
{ "op": "BakedLighting", "cell": 0.5, "indoor": 0.2 },
{ "op": "LightSource", "x": 0, "y": 3, "z": 0, "color": "#ffc878", "intensity": 1.5, "range": 10, "flicker": true },
{ "op": "LightSource", "x": 6, "y": 6, "z": -2, "color": "#ffe9c0", "type": "spot", "pitch": 90, "cone": 40, "intensity": 2.5, "range": 10 },
{ "op": "LightSource", "x": -13.9, "y": 4, "z": 1, "color": "#ff7a6a", "type": "area", "yaw": 90, "pitch": 0, "w": 1.6, "d": 2.5, "intensity": 1.2 },
{ "op": "Prop", "name": "Iron Torch", "x": -12, "z": -7.6, "y": 2.2 }
```

`LightSource` (y is above the ground on terrain maps):

| Parameter | Meaning |
|---|---|
| `x`, `y`, `z` | Where the light is (an area light: the rectangle's centre). Keep it out of solid boxes, or just inside its own lamp's box (a light inside a box ignores that box). |
| `color` | `[r, g, b]` (0-1) or `"#rrggbb"`. Flames `#ffb45a`, candles `#ffc878`, bulbs `#ffd8a8`, daylight `#e8f0ff`, moonlight `#9fb4e8`. |
| `intensity` | 0-20. At 1 a white surface right by the light is lit about twice as brightly as by full sun. |
| `range` | 0.5-40 m. The light falls off like a real one near the source and reaches nothing past the range: about 60% a quarter of the way out, 20% half way, 3% at three quarters. |
| `type` | `"point"` (every way), `"spot"` (a cone) or `"area"` (a glowing rectangle that lights the side it faces). |
| `yaw`, `pitch` | Spot and area: which way it shines: compass `yaw` (0 = north, -Z; 90 = east) and `pitch` degrees below the horizontal (90 = straight down, the default; 0 = level, -90 = up). |
| `cone` | Spot: the cone's full angle, 1-170 degrees (soft edge in the outer quarter). |
| `w`, `d` | Area: the rectangle's width and depth (with `pitch` 0 it stands upright: `w` wide, `d` tall), 0.05-20 m. |
| `flicker` | `true`: its light wavers (flames, a failing bulb). Cheap: the share of each spot's light that flickers is baked, the waver is two sines in the shader. |

`BakedLighting` (all optional):

| Parameter | Meaning |
|---|---|
| `cell` | The grid spacing, 0.25-4 m (default 0.5). Shadow edges are about a cell wide. The grid is capped at about a million cells (13 MB): a big map gets coarser cells unless you give a `region`. |
| `indoor` | 0-1: how much sky light reaches a fully enclosed place (default 0.25). Lower is darker inside, where your lights matter. |
| `sunShadows` | `false`: no sun shadows (the sun lights everything as before). |
| `propLights` | `false`: props' own lights are left out. |
| `region` | `{ "x0", "z0", "x1", "z1" }`: bake only this rectangle (a big map with one lit building). On screen the light at its edge carries on outside it (put the edge in open air, just past the lit buildings); the first-person gun outside it gets the classic light. |
| `top` | The grid's top height in metres (0: just above the tallest box or light). |
| `cellY` | The cells' height, 0.25-4 m (0, the default: as `cell`). A tall map with several floors can keep fine cells across with taller ones (Ashgrave Cathedral: 0.5 m across, 1 m up). |

**Props bring their lights.** A prop with a `"lights"` list (Props/README.md, "Lights") lights a lit map wherever it is
placed, turned and scaled with it: the `Lighting` props (**Iron Torch**, **Candle Stand**, **Altar Candles**,
**Chandelier**, **Brazier**) and the lamps (Hanging Lantern, Street Lamp, Flickering Street Lamp, Heritage Lamp Post,
Lantern Street Lamp, Caged Bulkhead Lamp, Tube Light, Floor Lamp, Table Lamp, Fireplace, Mall Ceiling Light, Mall
Pendant Light). On an unlit map they are only their glowing parts, as before.

**What casts shadows:** every solid box (walls, floors, roofs, solid props' colliders, building walls, tree trunks,
hidden `Collider`s). Doors, windows, Zombies barriers and map logic boxes don't (they open). Meshes, cosmetic props and
foliage don't. Destroyed boxes keep the light they had.

**Cost.** Baking takes a fraction of a second for a small map and about a second for a big one, once (the release bakes
shipped maps; your build caches its own). Drawing costs the same whatever the number of lights. Players can turn it off
(**Settings > Video > Lighting**: Off, Baked, Baked + flashes, which also lets muzzle flashes and blasts light the map).

**Try it:** the Dev pack's **Light Test** is a dusk-lit stone hall with torches, candles, a chandelier, a spot-lit altar,
stained glass and a firelit side room. `CallOfSlop --light-shots "My Map"` saves pictures of a map with the lighting on
and off (docs/render/LIGHTING.md "Looking at it"), and the `test` block's `lightProbes` check the light stays where it
should. The map kit has helpers for rows, rings, wall fittings and windows (docs/mapkit/MAPKIT.md "Lights").

## Map packs

Each folder directly inside `Maps` is a pack. In the lobby, players first pick a pack, then play a random map from it or choose one of its maps, and the end-of-match vote offers maps from the same pack.

```
Maps/
  README.md            this guide
  Legacy/              the shipped pack (Classic, Collision Point and High Ground maps)
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
| `order` | Sort position in the pack list (Legacy is 1). Ties sort by name. Default 1000. The "Other" pack always comes last. |
| `trueRandom` | `false` keeps the pack out of **True Random** (random from every pack), like the Dev test maps. Its maps can still be picked directly. Default `true`. Packs with `false` are listed last. |
| `destructible` | `true`: the pack's maps have destructible buildings unless a map says `"destructible": false` (the World Tour). Default `false`. See [Buildings/README.md](../Buildings/README.md#destruction). |

Map names must still be unique across **all** packs, because that's how servers and clients find a map. Packs only change how the lobby groups maps, and which [pack-local assets](#where-assets-go) a map can use.

## Where assets go

**The rule:** assets used inside maps go in the general, reusable top-level folders whenever possible: `Props/` for props, `Models/` for glTF models, `Decals/` for decals, and shared sky images in the `Maps` folder itself. Only an asset that is specific to one map or one pack goes in that pack's folder, so the pack stays self-contained and other packs never see it:

```
Maps/
  MyPack/
    pack.json
    Bunker.json
    Props/             props only this pack's maps can use (same format as Props/, category subfolders allowed)
      Bunker/Crate.json
    Models/            glTF models only this pack's maps can use
      bunker_door.glb
    Decals/            decals only this pack's maps can use (same format as Decals/, PNGs next to them)
      Bunker/BunkerSign.json
    Skies/             sky images for this pack's maps
      dusk.hdr
```

**Resolution order.** When a map in a pack names an asset, the game looks in the pack first and in the global folders second:

| Asset | Looked up in | Forcing the global one |
|---|---|---|
| Prop (`Prop`, `PlaceProp`, `Clutter`, a `props` boundary edge) | `Maps/<Pack>/Props`, then `Props` | `"global:Wooden Crate"`, or `"global:Street"` for a category |
| Decal (`Decal`, `DecalScatter`) | `Maps/<Pack>/Decals`, then `Decals` | `"global:Exit Sign"` |
| Model (`Model` op) | the path inside the pack folder (`"Models/door.glb"` finds `Maps/<Pack>/Models/door.glb`), then the same path from the game folder (`Models/door.glb`) | `"global:Models/door.glb"` |
| Sky image (`skybox`) | next to the map file, then the pack folder, then the `Maps` folder | `"global:Skies/dusk.hdr"` (the `Maps` folder only) |

- **Namespaced per pack.** Two packs can each have a prop called "Crate": each pack's maps get their own, and maps in other packs (or loose in `Maps`) never see either.
- **Shadowing.** A pack prop with the same name as a global prop replaces it for that pack's maps only, in name lookups and in its category, and the game log says so (`[props] pack MyPack: "Crate" shadows the global prop...`). A pack model at the same path as a global one does the same. Use `global:` where you still want the global one. Pack decals shadow global decals the same way (see [Decals/README.md](../Decals/README.md#pack-decals)).
- **Fingerprint.** The pack-local props and models a map uses count towards its fingerprint (each prop file's contents, ignoring whitespace and comments, and each model's geometry), so a player whose copy of the pack has different or missing asset files gets the usual "different version of the map" message instead of a game with different cover. The pack folder's name doesn't count, and a map that uses no pack-local assets keeps the fingerprint it always had. Sky images and decals (pack-local or not) stay looks only, like the rest of the `skybox`.
- **Cost.** A pack's Props and Decals folders are read the first time one of its maps loads and then cached until **Refresh maps**; a pack without one costs a single folder check. Pack decals (up to 4096 per pack) only get atlas layers for the ones the current map places (128 px each, up to 1024 different ones per map), painted on a worker thread when the map loads; see [Decals/README.md](../Decals/README.md#pack-decals).
- **Example:** the Dev pack has its own **Apple Crate** (`Maps/Dev/Props/AppleCrate.json`, magenta, shadowing the global one) and a model (`Maps/Dev/Models/dev_beacon.glb`, written by `tools/models/make_pack_models.py`). The **Pack Assets** map shows them next to `"global:Apple Crate"` and the global barrel; **Prop Gallery 3** names `"global:Apple Crate"` so it keeps showing the global one.
- The Dev pack also has its own decals (`Maps/Dev/Decals/Dev/`: a blue **Exit Sign** shadowing the global one, and the **Dev Pack Badge** PNG), shown by the **Pack Decals** map; the Dev decal galleries name `"global:Exit Sign"`.
- Files in a pack's `Props` and `Decals` folders are props and decals, never maps.

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

## Making a map on a branch (the tools)

**The map kit** (`Source/MapKit`, namespace `CallOfSlop.MapKit`; the guide is [docs/mapkit/MAPKIT.md](../docs/mapkit/MAPKIT.md))
is how new maps are generated: a C# `IMapGenerator` fills a `Kit` with ops, objectives and dressing (symmetry, outlines,
railings, stairs, ramps, prop clusters, a seeded generator), `CallOfSlop --gen-map "<Name>"` writes the map file, loads
it back and prints the `MapChecks` report (objectives, horizon fade, nav reach, fair walks, stairs, counts),
`--gen-maps` says which generators' outputs differ from the committed files and `--gen-plan <map> <png>` draws a
plan of any map. Twin Shard (`Source/MapKit/Generators/TwinShard.cs`) is the example.

Three scripts in `tools/mapkit/` take the chores out of a new map. Run them from the repository root with `python3 -I`.

**Scaffold:** `python3 -I tools/mapkit/new_map.py <Pack> <NN> "<Name>" [--theme "a few words"]` writes the generator
(`Source/MapKit/Generators/<Slug>.cs`, an `IMapGenerator` for the C# map kit in `Source/MapKit`; `--python` writes a
self-contained `tools/<pack>/<slug>.py` instead), the doc stub `Maps/<Pack>/NN-<Slug>.md` with the standard sections
(theme, shape, layout, lanes, fairness, counts, what to check), and the decal folder `Maps/<Pack>/Decals/<Slug>/`. It
prints the registry line to add, the `order` the pack's convention gives the map (Space 600 + NN, World 500 + NN,
Cluttered 400 + NN), a reminder to fill the map's test block, and the `[tests: ...]` line for the branch's commits.
Nothing existing is overwritten (`--force`).

**Pre-push checklist:** `python3 -I tools/mapkit/check_branch.py [--base origin/main] [--fix]` lists what the branch
changed (maps, props, decals, tools), runs `--check-map` on every changed map, checks every new global prop is on a
Dev gallery (the generated galleries current: `tools/props/gallery.py --check`, see Props/README.md; `--fix`
regenerates them), finds prop-name clashes (in the tree, and against props `origin/main` gained since the branch
started), warns about files the branch adds that already exist on `origin/main`, and prints the `[tests: ...]`
sections to put in every commit message: always Map horizons, Map objectives, Props, Animated props, Pack assets,
Maps, Map files, Nav determinism and Stairs, plus the pack's own sections and those for whatever else changed. The
exit code is the number of problems. The all-maps rules it names (Map horizons: fog within 22 m of the floor; Map
objectives: hills clear of geometry; Stairs; Nav determinism) run only in the full suite on main, which is why a branch
lists them.

**Plans:** `python3 -I tools/mapkit/plan_min.py Maps/<Pack>/<file>.json plan.png` draws a plan of a map file without
the game or any library: boxes shaded by height, props, clutter rectangles, spawns, hills, flags, the outline. CI's
Linux job draws one for every map a push changed and uploads them as the **map-plans** artifact (the map kit's own
`CallOfSlop --gen-plan` when the build has it), so a reviewer can see a layout without running the game.

## Tips

- Start from the shipped map closest to what you want: copy its file into your pack folder, rename it, and change `name` and `order`.
- `CallOfSlop.exe --export-maps SomeFolder` rewrites every loaded map in the standard tidy layout, in its pack folder, which is handy after hand-editing.
- Keep cover near spawns, and leave sightlines under ~60 m on Small maps.
- Ops run in order, so `TreeIfClear` and `AutoWaypoints` only see what was built before them. Put them last.
- **Stairs must give 2.1 m headroom** and be walkable standing: nothing (a roof slab, a beam, a lintel, a railing's top rail, terrain) within 2.1 m over any step a player can stand on, steps of at most 0.55 m, at least 1.0 m clear between the sides, 1.2 m clear at the top and the foot (or room to turn off sideways), and nothing parked across the foot. The self-test's "Stairs" section finds every flight on every map (any run of three or more step-shaped boxes, whatever op or prop built it) and walks it standing with both movers; it names the flight and the clearance in cm when one fails. On terrain, keep the ground below the treads: a plateau or bank edge that rises over the upper steps makes them a wall.

## Testing a map

Every shipped map is checked by the game's self-test ("Map contract", `Source/App/SelfTestMapContract.cs`), and
`CallOfSlop.exe --check-map FILE` runs exactly the same checks on one file in seconds, printing each check's result
(`ok` or `FAIL`) with its numbers: the fairness walks, the nav graph, the bot match. `--check-maps DIR` does a whole
pack. Run it before pushing a map; main's full self-test takes twenty minutes to tell you the same thing.

Every map gets the **universal checks**: a hill and the flags that aren't inside geometry, no spawn inside geometry, one
spawn per player, and (with a `boundary`) every spawn, waypoint and objective inside the outline. The rest is driven by
an optional top-level **`test`** block, which says what must be true of this map. Every key is optional; without the
block only the universal checks run. The block is not part of the map's fingerprint, so adding or changing it never
stops anyone joining.

```json
"test": {
  "shape": "non-rectangular", "area": [8000, 14000], "size": "medium", "maxPlayers": 16,
  "minProps": 180, "minSolidProps": 120, "minDecals": 150, "minDecalShare": 80, "minMeshes": 1200, "maxDrawnBoxes": 120,
  "symmetry": "turn", "minTeamSpawns": 8, "walkFairness": 0.10,
  "probes": [["bow dock", -60, -4, 20.0], ["crown deck", 0, 0, 28.0]],
  "botMatchSeconds": 20, "botMatchPlayers": 12, "explosivesClear": 10
}
```

| Key | Default | Check |
|---|---|---|
| `shape` | `"any"` | `"non-rectangular"` (any `boundary` but a rect), or the exact shape: `"rect"`, `"circle"`, `"ellipse"`, `"smooth"`, `"polygon"`. |
| `area` | – | `[min, max]`: the play area inside the outline, in square metres. |
| `size`, `maxPlayers` | – | The size class (`"small"`, `"medium"`, `"large"`) and the player limit the map must have. |
| `minProps`, `minSolidProps`, `minCosmeticProps`, `minBreakableProps`, `minDecals`, `minMeshes`, `minPlants`, `minTrees`, `minPackProps` | 0 | Dressing floors: at least this many props (solid, cosmetic, breakable), decals placed, meshes (not counting the boundary's), plants, trees, pack-local props. |
| `minDecalShare` | 75 | At least this percentage of the requested decals must find a surface (0: not checked). |
| `maxDrawnBoxes` | – | At most this many boxes drawn one by one (each is a draw call; use props, meshes and hidden colliders). |
| `requireProps`, `requireDecals` | `[]` | Prop names that must be placed; decal name prefixes that must be placed. |
| `symmetry` | `"none"` | `"turn"` (Red's spawns, the flags and the hills are Blue's turned 180 degrees about the centre: twins at (-x, -z)), `"mirrorX"` (twins at (-x, z)) or `"mirrorZ"` (twins at (x, -z)). Both teams get the same number of spawns. A hill on the symmetry line is its own twin. |
| `symmetryTolerance` | 0.3 | How far (metres) a twin may be from its mirrored spot. |
| `unpairedSpawns` | 0 | How many Blue spawns may lack a Red twin (one nudged off a slope). |
| `symmetricHills` | true | With a `symmetry`: the hills are twinned too. |
| `symmetricProps` | false | With a `symmetry`: every hand-placed solid prop has its twin (same prop, mirrored position and yaw; Clutter mirrors its own). |
| `minHills` | 3 | At least this many hill zones. |
| `minTeamSpawns` | half the player limit | At least this many spawns per team. |
| `handPlaced` | true | Team spawns, hills and flags are hand-placed (`teamSpawns`, `hills`, `flags`), and there is a placed spawn per player. |
| `walkFairness` | 0.10 | The average walk from each team's spawns to every hill, and to the enemy flag, must be within this share of each other (0: only that routes exist). The numbers are printed. |
| `hillWalks` | `"centre"` | `"zone"`: the walk to a hill ends at the nearest nav node anywhere in the zone at the hill's height (hills on raised decks). |
| `maxNavBuildMs` | 3000 | The nav graph must build within this (0: not checked). |
| `minMainNavPct` | 75 | At least this percentage of nav nodes in the main area (pockets bots can't leave fail it). |
| `navPockets` | false | The map is meant to have pockets (Zombies maps whose doors are shut while the nav graph is built): spawns, objectives and probes need a nav node near them, not one in the main area. |
| `minNavNodesAbove` | – | `[height, count]`: at least `count` main-area nodes above `height` (the raised floors are on the graph). |
| `void` | fallKill set, no `Ground`, no terrain | A void map: no nav link crosses the void (floor under every half metre of every link), no nav node below the fall-kill height. Any map with `fallKill` also fails if a nav link leads below it. |
| `voidAt` | `[]` | `[[x, z], ...]`: open to the void there (no floor from the top of the map down to the fall-kill height). |
| `probes` | `[]` | `[["name", x, z, y], ...]`: the floor at (x, z), found from 1.2 m above y, is at y (within 0.1 m), and the nav graph's main area reaches it. Name every deck, roof and pit. |
| `lightProbes` | `[]` | `[{ "at": [x, y, z], "min": 0.3 }, { "at": [...], "max": 0.1 }]`: on a lit map, the baked static light's brightness there (the luminance of the light a surface facing it gets; full sun is about 0.55) is at least `min` and at most `max`. Bright under the chandelier, dark in the crypt. See [Lighting](#lighting). |
| `fallKill` | – | `{ "drop": [x, y, z], "safe": [x, y, z] }`: a player dropped at `drop` dies within 4 s; one put at `safe` stands and lives. |
| `botMatchSeconds` | 8 | A seeded bot match of this many simulated seconds (0: none): no crash, nobody outside the outline or below the floor at any tick, no server tick over 8 ms of work. |
| `botMatchMode`, `botMatchPlayers` | `"ffa"`, the player limit | `"ffa"`, `"tdm"` or `"koth"`; how many players (bots plus the host). |
| `maxStuckBots` | – | At most this many bots may move under 8 m in the match (a match under 12 s is too short to judge). |
| `maxStuckSpots` | 0 | At most this many spots in the match where a stuck bot reached recovery stage 2 (6 s stuck: a hop and back-off didn't free it) or gave up; stage-1 spots are listed but not counted (-1: not checked). |
| `spawnsStand` | false | Every spawn settles where it is, standing, with the body clear (on gravity-zone maps: not in a zone with a sideways up). |
| `propsInside` | true | With a `boundary`: every prop and drawn box is inside the outline too (set false when you dress the view past the edge). |
| `propsClear` | true | No solid prop box within 0.8 m of a spawn or 1.5 m of a hill's centre or a flag, at body height. |
| `explosivesClear` | 0 | No explosive prop within this many metres of a spawn. |

The self-test prints one summary line per map and the usual `FAIL:` lines; `--check-map` prints every check. Keep the
block honest: thresholds are what the map has with a margin, not aspirations, so a change that quietly strips a map
fails the build. A map that needs a check the block can't express gets a small section of its own (the Space pack's
gravity maps have "Space: gravity physics" for standing on walls and the Pebble drop loop).

## Zombies maps

A map with a `"zombies"` section can be played in the Zombies mode (and only those maps offer it in the lobby): its
areas, doors and debris, boarded windows, zombie spawners, wall buys, mystery box spots, workbenches, perk machines,
the weapon-slot upgrade, the power switch, salvage piles and slot machines. The format is in
[docs/zombies/ZOMBIES-MODE.md](../docs/zombies/ZOMBIES-MODE.md) ("Map format"); `Dev/ZombiesTest.json` is a worked
example and `Zombies/01-BlackmoorAsylum.json` (the **Zombies** pack, written by `tools/zombies/make_blackmoor.py`) a
full map. With `"machineProps": true` in the section every machine stands as a solid prop from `Props/Zombies`, placed
by the section itself (leave room for each: see the doc). `"testKit": true` lets the host's Test kit setting act on the map (`Dev/HordeTest.json`, written by the C# map kit, is the big-horde test map). `Zombies/02-AshgraveCathedral.json` (written by the C# map kit's `Ashgrave Cathedral` generator) is a map on five levels: its areas have heights (`"y"`) and extra boxes (`"more"`), see the doc. Doors are solid boxes until bought; window openings block bodies but not bullets. The section is written back
unchanged when a map is saved, and opening doors never changes the map's fingerprint. The doors, debris, power switch and machines can drive and be driven by the map's
[logic](#map-logic) (`zdoor`, `zmachine` and the built-in `zombies` entity: a bell that calls the horde, the power
starting a lift).
