# Decals

Decals are flat pictures projected onto the world: stains, cracks, markings, signs, soot, and the bullet holes shots leave behind. Each decal is **one JSON file** in a category folder here (`Decals/Ground/OilStain.json`); the game loads every file at startup, paints its picture into the decal atlas and makes it available to maps by its `name`. Folders starting with `_` (like `_Template`) are skipped.

- **Maps** place decals with the `Decal` and `DecalScatter` ops: see [Maps/README.md](../Maps/README.md#decals).
- **Bullet impacts** are decals with an `"impact"` section: shots pick one for the surface they hit (below).
- The **Decal Test** map in the Dev pack shows the starter set on floors, walls, a kerb, a mesh and the terrain; the **Decal Impacts** map has a target of every impact surface.
- The **Decal Surfaces** map in the Dev pack shows every placeable decal (see [the catalogue](#the-catalogue)): ground grime and litter, road and court markings, wall damage, signs, posters and graffiti, things lying on the floor, and ceiling stains.

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

## The catalogue

Every placeable decal that ships (bullet impacts are listed under [Bullet impacts](#bullet-impacts)). The Dev pack's **Decal Surfaces** map shows each one: a street with a car park and a sports yard, two rooms under a roof (floor, wall and ceiling decals) and a wall gallery facing a row of ground decals. Sizes are metres (width x height; floors: the picture's top points north before turning). Turn "random" means each placement is turned at random; a fixed turn is the decal's default, and a map op's `rotation` overrides it.

**Ground** (25)

| Decal | Size | Blend | Surfaces | Turn | What it is |
| --- | --- | --- | --- | --- | --- |
| Asphalt Patch | 2.4 x 1.6 | alpha | floor | random | A rectangular tarmac repair with a sealed seam, for roads and car parks. |
| Carpet Stain | 1.1 | multiply | floor | random | A dried spill on carpet: a faint patch with a darker tide mark. |
| Coffee Stain | 0.5 | multiply | floor | random | A cup ring and a dribble of spilled coffee for desks and floors. |
| Concrete Patch | 1.8 x 1.4 | alpha | floor, wall | random | A newer, lighter square of concrete filled into an old floor or wall. |
| Cracked Tiles | 2 | multiply | floor | 0 | A grid of grubby tile joints with one broken, cracked tile. |
| Dirt Patch | 3 | multiply | floor | random | A soft, blotchy patch of trodden-in dirt and grit for any floor. |
| Drain Grate | 0.9 x 0.45 | alpha | floor | 0 | A rectangular gutter grate. |
| Dry Grass | 2 | alpha | floor | random | A patch of dead, straw-coloured grass and stalks. |
| Expansion Joint | 6 x 0.1 | multiply | floor | 0 | A long dark joint line between concrete slabs (runs east-west; turn it 90). |
| Floor Cracks | 2 | multiply | floor, wall | random | Spidering cracks for worn concrete, tarmac and tiles. |
| Floor Drain | 0.45 | alpha | floor | random | A small round floor drain for showers, kitchens and garages. |
| Footprints | 1.5 | multiply | floor | random | A trail of four muddy boot prints walking north (turned at random). |
| Gravel Scatter | 1.5 | alpha | floor | random | Loose grey stones spilled over a path or tarmac. |
| Leaf Litter | 1.8 | alpha | floor | random | Fallen autumn leaves scattered thinly over the ground. |
| Manhole Cover | 0.9 | alpha | floor | random | A round cast-iron cover with a ribbed rim, for streets and yards. |
| Moss Patch | 1.4 | multiply | floor, wall | random | Damp green moss creeping over stone, concrete or brick. |
| Mud Patch | 2.2 | multiply | floor | random | A churned patch of wet mud with clods round it. |
| Oil Stain | 1.6 | multiply | floor | random | A dark oily patch for car parks, garages and roads. |
| Paint Spill | 1.4 | alpha | floor | random | A splash of spilled paint with a few drops (tinted per placement). |
| Puddle | 2.4 | multiply | floor | random | A shallow rain puddle: a dark wet patch with a smaller one beside it. |
| Rust Stain | 1.2 | multiply | floor, wall | random | An orange-brown rust stain where something metal stood or dripped. |
| Sand Drift | 3 x 2 | alpha | floor | random | Wind-blown sand piled thinly over a floor, with ripples. |
| Skid Marks | 1.4 x 5 | multiply | floor | random | Two black rubber skid marks, heavier where the tyres locked. |
| Tyre Tracks | 2 x 4 | multiply | floor | random | Muddy tread tracks from a truck, two wheels wide. |
| Water Stain | 2.2 | multiply | floor | random | A dried puddle: a faint darker patch with a tide-mark rim. |

**Road** (17)

| Decal | Size | Blend | Surfaces | Turn | What it is |
| --- | --- | --- | --- | --- | --- |
| Court Centre Circle | 6 | alpha | floor | 0 | A sports court's centre circle and half-way line in white. |
| Court Key | 5 x 6 | alpha | floor | 0 | A sports court's key: a painted lane with a circle at its end (court edge to the south). |
| Crosswalk Bars | 4 | alpha | floor | 0 | Six white crossing bars (bars run north-south, for a road running north-south). |
| Double Yellow Lines | 4 x 0.35 | alpha | floor | 0 | Two parallel yellow no-parking lines along a road edge. |
| Hazard Chevrons | 2.4 x 0.8 | alpha | all | 0 | Black chevrons on yellow pointing east, for barriers, bays and ramps. |
| Hazard Stripes | 2 x 0.5 | alpha | all | 0 | Yellow and black warning stripes for loading bays, kerbs and machinery. |
| Helipad | 8 | alpha | floor | 0 | A landing pad: a dark disc with a yellow rim, a white ring and a big H. |
| Lane Dash | 0.15 x 3 | alpha | floor | 0 | One worn white lane dash (runs north-south; turn it 90 for east-west roads). |
| Loading Bay Box | 4 | alpha | floor | 0 | A yellow loading-bay box: an outline filled with diagonal hatching. |
| Parking Bay | 2.6 x 5.2 | alpha | floor | 0 | A white U-shaped parking bay outline, open to the south. |
| Road Arrow | 1.2 x 3 | alpha | floor | 0 | A worn white lane arrow painted on tarmac. |
| Stencil Number 1 | 1.2 | alpha | floor, wall | 0 | A stencilled white number 1 for parking bays, docks and walls. |
| Stencil Number 2 | 1.2 | alpha | floor, wall | 0 | A stencilled white number 2 for parking bays, docks and walls. |
| Stencil Number 3 | 1.2 | alpha | floor, wall | 0 | A stencilled white number 3 for parking bays, docks and walls. |
| Stop Line | 3.5 x 0.4 | alpha | floor | 0 | A thick white stop line across a lane. |
| Turn Arrow | 1.6 x 3 | alpha | floor | 0 | A worn white arrow bending right, for junction lanes. |
| Yellow Kerb Paint | 3 x 0.3 | alpha | all | 0 | A worn strip of yellow kerb paint (for the kerb's top or its side). |

**Wall** (23)

| Decal | Size | Blend | Surfaces | Turn | What it is |
| --- | --- | --- | --- | --- | --- |
| Brick Patch | 1.4 | alpha | wall | 0 | Plaster broken away to show the brickwork behind it. |
| Chalk Marks | 1.2 x 0.8 | alpha | wall, floor | 0 | Chalk tally marks, a circle and a cross scrawled on a wall or floor. |
| Electrical Panel | 0.8 x 1.1 | alpha | wall | 0 | A grey electrical cabinet door with a yellow warning triangle (no text). |
| Exit Sign | 0.8 x 0.4 | alpha | wall | 0 | A green emergency-exit style sign: a door and an arrow, no words. |
| Graffiti Burst | 1.6 | alpha | wall | random | A spray-painted starburst with a white core. |
| Graffiti Scribble | 2 x 1 | alpha | wall | 0 | A fast purple zig-zag tag with an underline (abstract, no letters). |
| Graffiti Tag | 2 x 1 | alpha | wall | 0 | A spray-painted tag with drips. |
| Graffiti Throw-Up | 2.6 x 1.3 | alpha | wall | 0 | Chunky orange bubble letters with black outlines and drips (not any real word). |
| Grime Streaks | 1.6 x 2.4 | multiply | wall | 0 | Rain streaks of dirt running down a wall. |
| Handprints | 0.6 | multiply | wall | random | Two grubby handprints, as if someone leaned on the wall. |
| Mould | 1.4 | multiply | wall, ceiling | random | Black-green mould spots spreading in a damp corner. |
| Peeling Paint | 1.6 | alpha | wall | random | Paint flaking off a wall, showing grey plaster underneath. |
| Plaster Patch | 1.4 | alpha | wall, ceiling | random | A smoothed-over patch of fresh plaster with trowel marks. |
| Poster Circles | 0.7 x 1 | alpha | wall | 0 | An abstract gig-style poster: a big sun over bars of blank text. |
| Poster Diagonal | 0.7 x 1 | alpha | wall | 0 | An abstract poster: red diagonal bands on navy with a pale panel. |
| Rust Bleed | 0.8 x 2 | multiply | wall | 0 | Rust running down a wall from a bolt or bracket. |
| Scorch Mark | 2.4 | multiply | all | random | Soot from a fire or blast, for walls and floors. |
| Soot Plume | 2 x 3 | multiply | wall | 0 | Soot fanning up a wall from a window, vent or fire below. |
| Stencil Arrow | 0.8 x 1.2 | alpha | wall, floor | 0 | A yellow stencilled arrow pointing up (turn it for any direction). |
| Torn Poster | 0.7 x 1 | alpha | wall | 0 | A faded paper poster, half torn away. |
| Vent Grille | 0.8 | alpha | wall, ceiling | 0 | A square louvred vent grille with corner screws, for walls and ceilings. |
| Wall Cracks | 2.2 | multiply | wall | random | Settlement cracks running across plaster or concrete. |
| Water Damage | 2 x 2.4 | multiply | wall, ceiling | 0 | A brown leak stain with a tide-mark rim and runs below it. |

**Objects** (14)

| Decal | Size | Blend | Surfaces | Turn | What it is |
| --- | --- | --- | --- | --- | --- |
| Broken Glass | 1.2 | alpha | floor | random | Shards and glittering crumbs of a broken window or bottle. |
| Cardboard Flat | 1.2 x 0.9 | alpha | floor | random | A flattened cardboard box with fold lines and a strip of tape. |
| Cigarette Butts | 0.6 | alpha | floor | random | A little cluster of stubbed-out cigarette ends and ash. |
| Doormat | 0.9 x 0.6 | alpha | floor | 0 | A bristly coir doormat with a black rubber border. |
| Leaf Pile | 1.3 | alpha | floor | random | A dense heap of fallen leaves swept against something. |
| Oil Drips | 1.2 x 2.2 | multiply | floor | random | Oil dripped under a parked vehicle: engine and gearbox spots. |
| Paper Litter | 1.6 | alpha | floor | random | Scattered sheets of paper and a couple of crumpled balls. |
| Rectangular Rug | 2.4 x 1.6 | alpha | floor | 0 | A red and gold patterned rug with a navy centre medallion. |
| Round Rug | 1.8 | alpha | floor | random | A round blue rug with cream and ochre rings. |
| Round Sewer Grate | 0.7 | alpha | floor | random | A round cast-iron sewer grate: rings of slots with spokes. |
| Rubble Dust | 2 | alpha | floor | random | Grey dust and broken chunks of concrete and brick from a collapse. |
| Square Manhole Cover | 0.8 | alpha | floor | 0 | A square steel access cover with a diamond tread pattern. |
| Trench Grate | 3 x 0.3 | alpha | floor | 0 | A long slotted channel drain for yards, garages and kerbs (runs east-west). |
| Utility Cover | 0.6 x 0.4 | alpha | floor | 0 | A small rectangular utility access plate with hinges and a lift slot. |

**Ceiling** (3)

| Decal | Size | Blend | Surfaces | Turn | What it is |
| --- | --- | --- | --- | --- | --- |
| Ceiling Tile Stain | 1.2 | multiply | ceiling | 0 | Drop-ceiling tile joints (a 2 x 2 tile square) with one water-stained tile. |
| Ceiling Water Spot | 1.6 | multiply | ceiling | random | A brown leak stain on a ceiling: rings of old tide marks. |
| Cobweb | 1 | alpha | ceiling, wall | 0 | A dusty cobweb spun out from a corner (the corner is the decal's top left). |

The self-test (section **Decal surfaces**) checks this set: every decal loads with a unique name, every placeable decal's file states its `surfaces` and suits its folder (`Objects` are floors only, `Ceiling` decals allow ceilings), no line, ring or rectangle is thinner than about 1.2 atlas pixels, painting stays cheap, every placeable decal is shown by a `Decal` op on a Dev decal map, and every single `Decal` op on Decal Surfaces lands on a surface its decal allows. **When you add a placeable decal, add a `Decal` op for it to `Maps/Dev/DecalSurfaces.json`.**

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

1. Copy `_Template/Template.json` into a category folder (`Ground`, `Road`, `Wall`, `Objects`, `Ceiling`, `Impacts`, or a new one) and give it a unique `name`.
2. Draw the picture with layers (and/or a small PNG next to the file). Use `multiply` for dirt and stains, `alpha` for paint and objects.
3. Set `surfaces` so scatters can't put it somewhere silly (puddles on floors only, posters on walls only).
4. Place it with a `Decal` op on the Dev pack's **Decal Surfaces** map (`Maps/Dev/DecalSurfaces.json`; the self-test wants every placeable decal shown there or on Decal Test), and run the self-test: every decal file is loaded and painted, and a broken one fails it with the file and the problem.
5. For a new bullet impact, add `"impact": { "materials": [...] }`; for more variety on a material, add several impact decals listing it (the pick is weighted by `weight`). The self-test wants at least 3 variants for every material (fallbacks count) and checks the pick matches the weights. Impact decals don't need to be placed on a map.

Decals live in this top-level folder so every map and pack can use them. Decals are cosmetic and never part of a map's fingerprint.

## Pack decals

A map pack can carry decals of its own in `Maps/<Pack>/Decals/` (same file format, category subfolders allowed, PNG images next to the files): branding and signage for one pack's invented shops, say. **Put a decal there only if it belongs to that pack**; anything generally useful (a stain, a road marking, a generic sign) goes here in the global folders. The World Tour pack keeps one subfolder per map: `Maps/World/Decals/<MapName>/`.

- **Visible to that pack's maps only**, by name; maps in other packs (or loose in `Maps`) never see them, and two packs may both have a decal called "Shop Sign".
- **Shadowing.** A pack decal with the same name as a global one replaces it for that pack's maps only, and the log says so (`[decals] pack MyPack: "Exit Sign" shadows the global decal...`). `"global:Exit Sign"` in a `Decal` or `DecalScatter` op always means the global one.
- **Names are unique within the pack** (a duplicate is skipped and reported, like here). A pack may hold up to **4096 decals** (a couple of thousand is fine: 2000 files parse in well under a second, on all cores), and **one map may use up to 1024 different pack decals** (a map naming more is refused when it loads).
- **No impact decals** in packs: bullet impacts are global (an `impact` decal in a pack is skipped with a message).
- **Cosmetic:** pack decals don't change any map's fingerprint, so a player with different pack decal files still joins (and just sees their own pictures).
- **Example:** `Maps/Dev/Decals/Dev/` has a blue **Exit Sign** (shadowing the green one) and the **Dev Pack Badge** (a PNG with a painted ring); the Dev pack's **Pack Decals** map shows them next to `"global:Exit Sign"`. The self-test section **Pack decals** checks the scope rules and the atlas budget.

**The atlas is map-scoped.** Every decal is one 128 x 128 layer of one texture array, but only the decals the current map needs get a layer: the whole global library (it may hold at most **256** decals, and the self-test wants a quarter of that free; it is always there because bullet impacts can land anywhere) and then **only the pack decals the map actually places**, in the order the map's decal bake first meets them. When a map loads, its decals are baked on a worker thread, then the atlas is rebuilt on a worker thread: the global layers are copied from a CPU copy (at most 256 x 64 KB = 16 MB) and only the map's pack decals are painted; the frame never waits (map decals appear a few frames later, impacts straight away). So a pack with 2000 decals costs parse time and a few MB of definitions, not video memory: video memory is about 85 KB per layer with mipmaps (130 global + 300 pack decals on one map: about 36 MB). A map's atlas needs at most 256 + 1024 = 1280 layers, inside the 2048 that OpenGL 4.1 guarantees; the game also asks the driver (`[decals] GPU allows N array texture layers` in the log) and, if a map would go past it, logs how many of its pack decals show another decal's picture instead of failing.

**Resolution: 128 px per decal, whatever its size or shape.** A non-square decal (`"size": [2.4, 0.8]`) stretches its 128 x 128 picture over that rectangle, so a 3:1 sign gets 128 pixels across its whole width: its detail across is a third as fine as its detail up and down. There are no larger or wide layers (the texture array has one layer size). For a big or wide shop sign: keep the artwork bold (block letters made of `rect`s, simple logo shapes, no fine text), or split it into 2-3 decals placed side by side (`"Brand Sign Left"`, `"Brand Sign Right"`), each with its own 128 px of detail. A PNG may be up to 512 x 512 but is resampled to 128 x 128.

**From code:** anything that builds a map (a building generator, a future prop type) can call `Map.Decal(...)` and `Map.DecalScatter(...)` directly; calls made inside another op aren't written to the map file as ops of their own, and are baked with the map like any other decal. A decal on one of the boxes it made hides automatically while that box is broken or moved (for example an open door).
