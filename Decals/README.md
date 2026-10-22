# Decals

Decals are flat pictures projected onto the world: stains, cracks, markings, signs, soot, and the bullet holes shots leave behind. Each decal is **one JSON file** in a category folder here (`Decals/Ground/OilStain.json`); the game loads every file at startup, paints its picture into the decal atlas and makes it available to maps by its `name`. Folders starting with `_` (like `_Template`) are skipped.

- **Maps** place decals with the `Decal` and `DecalScatter` ops: see [Maps/README.md](../Maps/README.md#decals).
- **Bullet impacts** are decals with an `"impact"` section: shots pick one for the surface they hit (below).
- The **Decal Test** map in the Dev pack shows the starter set on floors, walls, a kerb, a mesh and the terrain.

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

A decal with an `"impact"` section is a bullet-hole mark for the surfaces it lists:

| Material | Surfaces |
| --- | --- |
| `Concrete`, `Brick`, `Wood`, `Metal`, `Tiles`, `Dirt`, `Grid` | Boxes and building parts with that texture (the game picks a box's texture from its shape and colour). |
| `Terrain` | The terrain. |
| `Default` | Anything with no impact of its own (meshes, and any material above that has none). |

When several impacts list the same material, each hit picks one, weighted by `weight` (default 1), from the hit position, so **every player sees the same mark in the same place** (the server sends every client the same hit points; impacts add no network traffic). Impact decals must use `"blend": "alpha"` and be at most 1 m across; real bullet holes are 0.1 to 0.25 m. Size and turn variation apply per hit. A mark that would hang over the edge of a box slides inwards, shrinks on a narrow face, and isn't placed on a sliver (a thin rail).

**The impact pool** (Settings > Video > **Bullet impacts**):

- **High** keeps 1024 marks at once (default), **Medium** 512, **Low** 128, **Off** none (and costs nothing).
- **Each player keeps at most 64**: their oldest fades as they add another.
- **Oldest first**: when the pool is nearly full, the oldest mark starts fading an eighth of the pool ahead of being reused, so marks fade out instead of popping.
- **Shotgun pellets merge** when the pool is three quarters full (or a player is near their 64): pellets of one shot landing close together on the same surface make one slightly bigger mark.
- **Breakable things**: marks on a prop, door or window that breaks, opens or moves fade out at once. Props themselves (drawn as models) don't take marks; the wall behind does.
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
5. For a new bullet impact, add `"impact": { "materials": [...] }`; for more variety on a material, add several impact decals listing it (the pick is weighted by `weight`).

Decals live in this top-level folder so every map and pack can use them (packs can't have their own decals yet). Decals are cosmetic and never part of a map's fingerprint.
