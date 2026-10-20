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
| `description` | no | One line, shown in the map list. |
| `author` | no | Shown as "(by …)" in the map list. |
| `order` | no | Sort position in the list (the shipped maps use 1–24). Ties sort by name. Default 0. |
| `halfX`, `halfZ` | **yes** | Half the map's width (X) and depth (Z) in metres. The playable area runs from `-halfX..halfX` and `-halfZ..halfZ`. The size class is worked out from these: Small up to ~75 m across, Medium up to ~135 m, Large beyond that. Large and Medium maps get distance fog. |
| `sky` | no | Sky and fog colour. |
| `prototypeGrid` | no | `true` draws the dev-grid texture on everything (as in Proto Box). |
| `ops` | **yes** | The list of building steps, run in order. |

## Game mode objectives (optional)

Team modes need two teams' spawn points, King of the Hill needs hill zones, and Capture the Flag needs two flags. You can place them with three optional top-level fields. Anything you leave out is worked out automatically, the same way on every PC:

- **Team spawns:** the two spawns furthest apart anchor the teams, and every spawn joins the side it's closer to, split evenly.
- **Hills:** waypoints on open ground that lie on the routes between the two teams' spawns, central and equally far from both teams. Two hills on small maps, three on bigger ones, spread apart. Radius 3.5 m (small), 4.5 m (medium) or 6 m (large).
- **Flags:** on each team's spawn that's deepest in its own half.

| Field | Meaning |
|---|---|
| `teamSpawns` | Two lists of points, Blue's then Red's: `[[[x, y, z], ...], [[x, y, z], ...]]`. A point is `[x, y, z]` or `[x, z]` (y = 0). Each list needs at least one point. |
| `hills` | Hill zones in rotation order: `[{ "x": 0, "z": 10 }, { "x": -20, "z": -5, "y": 3.2, "radius": 5 }]`. `y` (default 0) is the floor height, `radius` defaults to the size-based value. |
| `flags` | Exactly two points, Blue's flag then Red's: `[[-24, 0, 0], [24, 0, 0]]`. |

```json
  "teamSpawns": [[[-24, -8], [-24, 8], [-20, 0]], [[24, -8], [24, 8], [20, 0]]],
  "hills": [{ "x": 0, "z": 10 }, { "x": 0, "z": -10, "radius": 4 }],
  "flags": [[-26, 0, 0], [26, 0, 0]],
```

These fields (and the waypoints, which the automatic hills use) are part of the map's fingerprint, so host and players must have the same version. Put spawns, hills and flags on open floor: something standing on them shouldn't be inside a wall.

## Units and directions

- Distances are in metres. **Y is up**, the ground is at y = 0, and (0, 0) is the middle of the map.
- North is −Z, south is +Z, west is −X and east is +X. These are used by `doors` on `Building`.
- A player is 1.8 m tall. A storey (`Building`) is 3.2 m. Players can step up 0.55 m without jumping, and can mantle through window openings.
- Colours can be written as `[r, g, b]` (0–1) or `"#rrggbb"`. The game picks a surface texture (concrete, brick, wood, foliage…) from each box's colour and shape.

## Ops

Parameters marked `= value` are optional. Parameter names are case-insensitive, and an unknown name is an error, so typos don't fail silently.

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
| `description` | One line, shown under the pack's map count. |
| `author` | Shown as "(by …)". |
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
