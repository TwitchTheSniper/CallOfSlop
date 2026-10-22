# Decals

Decals are flat pictures projected onto the world: stains, cracks, markings, signs, soot, and the bullet holes shots leave behind. Each decal is **one JSON file** in a category folder here (`Decals/Ground/OilStain.json`); the game loads every file at startup, paints its picture into the decal atlas and makes it available to maps by its `name`. Folders starting with `_` (like `_Template`) are skipped.

- **Maps** place decals with the `Decal` and `DecalScatter` ops: see [Maps/README.md](../Maps/README.md#decals).
- **Bullet impacts** are decals with an `"impact"` section: shots pick one for the surface they hit (below).
- The **Decal Test** map in the Dev pack shows the starter set on floors, walls, a kerb, a mesh and the terrain; the **Decal Impacts** map has a target of every impact surface.

## A decal file

```
{
  "format": "call-of-slop-decal/1",
  "name": "Oil Stain",
  "description": "A dark oily patch for car parks, garages and roads.",
  "author": "Call of Slop",
  "size": 1.6,
  "sizeVariation": 0.35,
  "rotation": "random",
  "tintVariation": 0.1,
  "blend": "multiply",
  "surfaces": "floor",
  "layers": [
    { "shape": "splat", "radius": 0.33, "count": 7, "jag": 0.4, "seed": 51, "color": [0.35, 0.33, 0.3], "alpha": 0.75, "softness": 0.12, "noise": 0.35, "noiseScale": 5 },
    { "shape": "splat", "radius": 0.18, "count": 5, "jag": 0.35, "seed": 52, "at": [0.56, 0.46], "color": [0.22, 0.21, 0.2], "alpha": 0.7, "softness": 0.08 }
  ]
}
```

Copy `_Template/Template.json` to start (it lists every key with a comment; `//` comments are allowed in decal files). Every key is checked: an unknown key, a number out of range or a bad shape stops the file loading with a message in the game log (`[decals] skipped: ...`) and the self-test.

| Key | Default | Meaning |
| --- | --- | --- |
| `format` | | `"call-of-slop-decal/1"` |
| `name` | (required) | Unique, at most 40 plain ASCII characters. Map ops use it. |
| `category` | the folder | Shown in tools; the first folder under `Decals` is used when left out. |
| `description`, `author` | | Plain ASCII text. |
| `size` | 1 | Metres: `[width, height]` or one number for a square (0.02 to 16). The picture is stretched to it. |
| `sizeVariation` | 0 | Each placement is up to this fraction bigger or smaller (0 to 0.9). |
| `rotation` | `"random"` | `"random"`, or a fixed turn in degrees (signs, arrows, stripes). |
| `tintVariation` | 0 | Each placement is up to this fraction brighter or darker (0 to 0.5). |
| `blend` | `"alpha"` | `"alpha"`: painted on, lit like the surface (paint, posters, metal covers, bullet holes). `"multiply"`: darkens the surface by its colour (dirt, oil, soot, water stains, cracks); white does nothing, it is never lit and never brightens. |
| `surfaces` | `"all"` | Where it may land: a list of `"floor"` (faces up), `"wall"` (faces sideways), `"ceiling"` (faces down), or `"all"`. |
| `opacity` | 1 | Overall strength (0.02 to 1). |
| `fadeDistance` | from the size | Metres; it fades out over the last quarter. Default 30 m plus 25 m per metre of size, at most 150. |
| `image` | none | A PNG next to the file (at most 512 x 512, any of grey / grey+alpha / RGB / RGBA, not interlaced), resampled to 128 x 128 and drawn under the layers. |
| `layers` | | The picture: shapes drawn in order (below). A decal needs layers, an image, or both. |
| `impact` | none | Makes it a bullet-impact mark: `{ "materials": ["Concrete", "Tiles"], "weight": 1 }` (below). |

### The picture: layers

The picture is a square from 0 to 1 on both axes, `[0.5, 0.5]` in the middle, **y up** (`[0.5, 1]` is the top edge). Each layer is one shape with a colour, painted over the layers before it (or cutting them away with `"erase": true`). Edges are anti-aliased; keep shapes a little inside the square so nothing is cut off at the border.

| Key (any shape) | Default | Meaning |
| --- | --- | --- |
| `shape` | (required) | `circle`, `ring`, `rect`, `splat`, `cracks`, `speckle`, `line`, `stripes` |
| `color` | dark grey | A colour name from the prop list (`black`, `white`, `rust`, `brick`, `yellow`...), `[r, g, b]` (0 to 1) or `"#rrggbb"`. |
| `alpha` | 1 | Opacity of this layer (0 to 1). |
| `softness` | 0 | Edge blur in picture units (0.1 is a tenth of the picture). |
| `noise`, `noiseScale` | 0, 8 | Grunge: takes up to `noise` (0 to 1) of the coverage away in a blotchy pattern of about `noiseScale` blotches across. |
| `erase` | false | Cut this shape out of the layers below instead of painting (rings, torn edges, stencils). |
| `seed` | 0 | Reshuffles the irregular shapes (splat, cracks, speckle). |

| Shape | Keys | Notes |
| --- | --- | --- |
| `circle` | `at`, `radius` | |
| `ring` | `at`, `radius`, `width` | |
| `rect` | `at`, `size` (`[w, h]` or one number), `rotation` (degrees) | A turned square is a diamond; erase half of it for a triangle (see Road Arrow). |
| `splat` | `at`, `radius`, `count` (lobes), `jag` (0 to 1) | A ragged blob with a few droplets round it: stains, splashes, chipped edges. |
| `cracks` | `at`, `count`, `length`, `width`, `jag` | Crooked lines radiating from `at`, tapering, with the odd branch. |
| `speckle` | `at`, `radius`, `count`, `width` | `count` dots of up to `width` radius scattered within `radius`: grit, chips, rust spots. |
| `line` | `at`, `to`, `width` | A straight stroke with round ends: streaks, drips, tags. |
| `stripes` | `at`, `size`, `rotation`, `period`, `duty` | Diagonal bands inside a rectangle; `duty` is the painted part of each `period` (hazard stripes, grates). |

The atlas holds 128 x 128 pixels per decal, so fine detail on a big decal goes soft: draw big, simple shapes for big decals, and keep small text-like detail for small ones. Pictures are painted once at startup (the whole starter set in a few milliseconds).

## Bullet impacts

A decal with an `"impact"` section is a bullet-hole mark for the surfaces it lists. Every shot works out the surface it ended on from the map alone (so every client agrees):

| Material | Surfaces |
| --- | --- |
| `Concrete`, `Brick`, `Wood`, `Metal`, `Tiles`, `Dirt`, `Grid` | Boxes with that texture (the game picks a box's texture from its shape and colour; a building part uses its own texture, a building door its material). |
| `Asphalt` | Dark grey concrete floors: roads, car parks, yards (and a mid-grey map floor). |
| `Plaster` | Pale concrete walls: rendered, painted and interior walls. |
| `Grass`, `Sand`, `Snow` | Earthy boxes by colour (green hills and hedges, sand slabs; pale ones snow), and the terrain where it is grass or sand (snow where the map paints its sand white). |
| `Rock`, `Dirt` | The terrain where it is rock (or steep enough to draw as rock) or dirt. |
| `Terrain` | Any terrain or earth surface whose own material has no impact decals. |
| `Default` | Anything else (and any material whose chain has none). |
| `Explosive` | Not a surface: the scorch a grenade, launcher round, mortar or strafing run (or an exploding prop) leaves on the nearest floor, wall or ceiling within about 1.5 m. |

A material with no impact decal of its own borrows its parent's: `Asphalt`, `Plaster`, `Rock`, `Tiles` and `Grid` use `Concrete`; `Grass`, `Sand` and `Snow` use `Terrain`, which uses `Dirt`; everything ends at `Default`. `Explosive` never falls back (no scorch decal: blasts leave nothing).

When several impacts list the same material, each hit picks one, weighted by `weight` (default 1), from the hit position, so **every player sees the same mark in the same place** (the server sends every client the same hit points; impacts add no network traffic). Give the plain look of a material the biggest weight so it dominates, and its rarer variants (a glancing gouge, a big chunk) smaller ones. Impact decals must use `"blend": "alpha"` and be at most 1 m across; real bullet holes are 0.1 to 0.25 m. Size and turn variation apply per hit. A mark that would hang over the edge of a box slides inwards, shrinks on a narrow face, and isn't placed on a sliver (a thin rail).

**Calibre:** marks are scaled by the weapon's `caliber` (0 to 1 in its weapon file): from 0.7 times the decal's size for light pistols to 1.35 times for heavy rifles and slugs; shotgun pellets are 0.8 times (and merge under pressure, below). Blast scorches use the decal's own size.

The impact set (`Decals/Impacts`, 46 files):

| Material | Variants (weight, size in m) |
| --- | --- |
| `Concrete` | Bullet Hole Concrete (2, 0.16), Bullet Chip Concrete (1.2, 0.12), Bullet Hole Concrete Spalled (0.9, 0.18), Bullet Hole Concrete Deep (0.8, 0.21) |
| `Plaster` | Bullet Hole Plaster (2, 0.15), Bullet Hole Plaster Cracked (1, 0.18), Bullet Hole Plaster Torn (0.6, 0.22) |
| `Asphalt` | Bullet Hole Asphalt (2, 0.15), Bullet Chip Asphalt (1, 0.14), Bullet Scar Asphalt (0.7, 0.2) |
| `Brick` | Bullet Hole Brick (2, 0.17), Bullet Chip Brick (1.2, 0.12), Bullet Hole Brick Cracked (0.9, 0.17), Bullet Hole Brick Chunk (0.8, 0.21) |
| `Wood` | Bullet Hole Wood (1.5, 0.15), Bullet Hole Wood Splintered (1, 0.2), Bullet Hole Wood Dark (0.8, 0.14), Bullet Gouge Wood (0.7, 0.19) |
| `Metal` | Bullet Dent Metal (1.5, 0.13), Bullet Dent Metal Paint (1, 0.15), Bullet Hole Metal Pierced (1, 0.1), Bullet Ricochet Metal (0.6, 0.17) |
| `Tiles` | Bullet Hole Tile (2, 0.15), Bullet Chip Tile (1.2, 0.1), Bullet Hole Tile Shattered (0.8, 0.2) |
| `Dirt` | Bullet Scuff Dirt (1.5, 0.24), Bullet Puff Dirt (1, 0.22), Bullet Furrow Dirt (0.6, 0.26) |
| `Terrain` | Bullet Scuff Dirt (1.5, 0.24), Bullet Puff Dirt (1, 0.22), Bullet Furrow Dirt (0.6, 0.26) |
| `Grass` | Bullet Divot Grass (2, 0.22), Bullet Puff Grass (1, 0.16), Bullet Scar Grass (0.6, 0.28) |
| `Sand` | Bullet Puff Sand (2, 0.2), Bullet Spray Sand (1, 0.2), Bullet Crater Sand (0.7, 0.28) |
| `Rock` | Bullet Chip Rock (2, 0.13), Bullet Spall Rock (0.8, 0.19), Bullet Scar Rock (0.7, 0.18) |
| `Snow` | Bullet Hole Snow (2, 0.17), Bullet Puff Snow (1, 0.22), Bullet Slush Snow (0.8, 0.16) |
| `Default` | Bullet Hole (1.5, 0.14), Bullet Hole Small (1, 0.1), Bullet Hole Scuffed (0.8, 0.16) |
| `Explosive` | Blast Scorch (2, 0.95), Blast Scorch Pitted (1, 0.8), Blast Scorch Rays (1, 1) |

The Dev pack's **Decal Impacts** map has one target per surface (concrete, plaster, brick, metal and wood walls; asphalt, tile, sand, dirt and grass boxes; grass, dirt, rock and snow terrain) for trying them out; the self-test shoots every target and checks it gets its own set, the same marks twice, and no allocation.

**The impact pool** (Settings > Video > **Bullet impacts**):

- **High** keeps 1024 marks at once (default), **Medium** 512, **Low** 128, **Off** none (and costs nothing).
- **Each player keeps at most 64**: their oldest fades as they add another.
- **Oldest first**: when the pool is nearly full, the oldest mark starts fading an eighth of the pool ahead of being reused, so marks fade out instead of popping.
- **Shotgun pellets merge** when the pool is three quarters full (or a player is near their 64): pellets of one shot landing close together on the same surface make one slightly bigger mark.
- **Breakable things**: marks on a door, window or box that breaks, opens or moves fade out at once. Props (drawn as models, with only a box as their collider) don't take marks, so a mark never floats in the air beside a prop.
- A new map clears the pool. Marks are drawn up to 70 m away (50 on Medium, 35 on Low), fading out with distance.

Impacts are separate from impact particles and sounds (those are drawn and played by their own systems).

## Cost and performance

- **Map decals** are baked once per map (the log shows `[decals] baked 'Map': N/M decals placed, T triangles in C chunks, X ms`) into one static buffer, culled by 24 m chunk and fade distance and merged into a few draw calls (multiply decals first, then alpha). A decal on a box that breaks or opens is hidden on the GPU through a small per-box texture; nothing is rebaked.
- **Impacts** are one instanced draw over a fixed ring of slots; a shot uploads only the slots it changed (a few dozen bytes), with no allocation.
- **Budget:** the whole decal pass (map decals plus a full High impact pool) targets **under 0.1 ms** of GPU time. Every 5 seconds the game logs one `[decals]` line: map decals and visible chunks, impacts showing / in the ring / capacity (with added, merged, popped, player-capped and removed counts), draw calls, triangles, CPU and GPU ms.
- Prefer one larger decal to many overlapping small ones, keep `fadeDistance` short for small floor clutter, and use `multiply` for grime (it needs no lighting).

## Adding decals (for content makers)

1. Copy `_Template/Template.json` into a category folder (`Ground`, `Wall`, `Road`, `Impacts`, or a new one) and give it a unique `name`.
2. Draw the picture with layers (and/or a small PNG next to the file). Use `multiply` for dirt and stains, `alpha` for paint and objects.
3. Set `surfaces` so scatters can't put it somewhere silly (puddles on floors only, posters on walls only).
4. Place it on a test map with a `Decal` op (or look at it on the Dev pack's **Decal Test** map), and run the self-test: every decal file is loaded and painted, and a broken one fails it with the file and the problem.
5. For a new bullet impact, add `"impact": { "materials": [...] }`; for more variety on a material, add several impact decals listing it (the pick is weighted by `weight`). The self-test wants at least 3 variants for every material (fallbacks count) and checks the pick matches the weights.

Decals live in this top-level folder so every map and pack can use them (packs can't have their own decals yet). Decals are cosmetic and never part of a map's fingerprint.

**From code:** anything that builds a map (a building generator, a future prop type) can call `Map.Decal(...)` and `Map.DecalScatter(...)` directly; calls made inside another op aren't written to the map file as ops of their own, and are baked with the map like any other decal. A decal on one of the boxes it made hides automatically while that box is broken or moved (for example an open door).
