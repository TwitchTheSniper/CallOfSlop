# Making props for Call of Slop

Props are the small things that make a map look lived in: trash cans, crates, bricks, newspapers, benches, litter. Every prop is one `.json` file in this `Props` folder, sorted into category folders (`Street`, `Industrial`, `Debris`, `Furniture`, `Nature`). Maps place them with the `Prop` and `Clutter` ops (see [Maps/README.md](../Maps/README.md#props)).

To add a prop, drop a file into a category folder (or make a new folder: the folder name is the category), then press **Reload map files** in the pause menu, or **Refresh maps** in the lobby. The **Prop Gallery** map in the Dev pack shows every prop on its own pad, in rows by category, so you can check them by eye.

A prop is a model made of boxes, the same style as everything else in the game. It's drawn instanced (one copy of the model on the graphics card, hundreds of props in a handful of draw calls), so a map can have hundreds of them for very little cost.

## The quick version

```json
{
  "format": "call-of-slop-prop/1",
  "name": "Trash Can",
  "category": "Street",
  "description": "A galvanized steel trash can with its lid on.",
  "collision": "solid",
  "collider": [ { "size": [0.58, 1.0, 0.58] } ],
  "scatter": { "randomYaw": true, "scale": [0.95, 1.05] },
  "breakable": { "health": 70, "breakBy": "all", "sound": "metal", "debris": { "count": 10, "size": [0.08, 0.2] } },
  "parts": [
    { "size": [0.56, 0.82, 0.56], "offset": [0, 0.43, 0], "color": "galvanized", "material": "metal" },
    { "size": [0.62, 0.04, 0.62], "offset": [0, 0.88, 0], "color": "steel" },
    { "size": [0.2, 0.03, 0.04], "offset": [0, 0.92, 0], "color": "darkgrey" }
  ]
}
```

## Units and directions

- Metres. The prop's origin is **on the ground at its centre**: y = 0 is the ground, so a part sitting on the ground has its centre at half its height.
- +X is the prop's "along" direction (benches, barriers and planks are long in X). With `"align": true` on a `Clutter` line, +X follows the line.
- Angles are degrees.

## Fields

| Field | Required | Meaning |
|---|---|---|
| `format` | no | `"call-of-slop-prop/1"`. Recommended. |
| `name` | **yes** | Up to 40 characters, plain ASCII. Unique across all props: it's how maps name the prop. |
| `category` | no | Defaults to the folder the file is in. Maps can scatter a whole category. |
| `description` | no | One line, plain ASCII. |
| `author` | no | |
| `collision` | no | `"solid"` (default) or `"cosmetic"`. See below. |
| `collider` | no | Solid props only: `"bounds"` (default: one box around the whole model), `"parts"` (a box per part, at most 8) or a list of up to 8 boxes `{ "size": [w, h, d], "offset": [x, y, z] }` (offset is the box centre; default: standing on the ground). |
| `parts` | **yes** | The model: 1 to 128 boxes (below). |
| `scatter` | no | Hints for the `Clutter` op (below). |
| `fadeDistance` | no | Cosmetic props only: how far away (metres) they dither out. Default from the prop's size: 28 m for tiny litter up to 90 m. |
| `breakable` | no | Makes the prop breakable (below). |

Unknown keys, numbers out of range and missing parts are errors with a message saying which file and which part, so typos don't fail silently. A file with an error is skipped and named in the game log (`callofslop.log`).

### Solid or cosmetic

- **Solid** props block movement, bullets, grenades and bots' line of sight, like a wall. Their collision is the `collider` boxes, not the model, so keep colliders simple: one box is usually right. A street lamp's collider is just the base and the pole, a table's is the top and four legs. A prop turned by an angle that isn't a multiple of 90 degrees gets a few boxes following its turned outline. Anything up to 0.55 m tall can be stepped onto; up to 1.7 m can be mantled.
- **Cosmetic** props are for looks only, like grass: you walk and shoot straight through them. Use it for litter, newspapers, bottles, loose bricks: anything small enough that bumping into it would feel wrong. Small cosmetic props fade out with distance.

### Parts

| Field | Meaning |
|---|---|
| `size` | `[w, h, d]`, each 0.001 to 12 m. |
| `offset` | `[x, y, z]`, the box centre. Default `[0, h/2, 0]`: centred, standing on the ground. |
| `rotation` | `[pitch, yaw, roll]` in degrees: about X, Y (turning it on the ground) and Z. Or give any of `pitch`, `yaw`, `roll` on their own. |
| `color` | A name (below), `[r, g, b]` with values 0-1, or `"#rrggbb"`. Default grey. |
| `material` | A surface texture: `concrete`, `brick`, `wood`, `metal`, `tiles`, `dirt`, `grid`, or `none` (default: plain colour). |
| `texScale` | How many times denser the texture is (default 1: one repeat per 2 m, like the map boxes). |

Colour names: black, rubber, darkgrey, grey, lightgrey, white, paper, newsprint, concrete, steel, galvanized, chrome, rust, brick, wood, darkwood, lightwood, pallet, cardboard, tape, red, orange, yellow, green, darkgreen, olive, blue, navy, tan, sand, brown, glass, amber.

The bottom of every part gets a little baked-in shadow near the ground, so props sit on the floor instead of floating. Each copy of a prop also gets a slight random brightness, so a row of bins doesn't look copy-pasted.

**Round things:** a box and the same box turned 45 degrees makes an 8-pointed star, not a cylinder. For an octagon `d` across, use four boxes `d x h x 0.414d` turned 0, 45, 90 and 135 degrees (that's how the drums, cones and the hydrant are made). Lying down, turn them with `pitch` (along X) or `roll` (along Z).

### Scatter hints

| Field | Default | Meaning |
|---|---|---|
| `randomYaw` | true | Clutter turns it to any angle. With false it's left square (plus `yawJitter`). |
| `yawStep` | 0 | Turn only in steps of this many degrees (90 for dumpsters, barriers and other things that sit square to walls). |
| `yawJitter` | 0 | Up to this many degrees either way on top of the step or alignment. |
| `scale` | [0.9, 1.1] | Size range: a number or `[min, max]` (0.25 to 4). |
| `groundSnap` | true | Stands on a low box under it (a kerb, a step, up to 0.6 m) as well as on the ground. With false it only goes on open ground. |
| `weight` | 1 | How often Clutter picks it compared with the others in the same list or category. |

### Breakable props

```json
"breakable": { "health": 60, "breakBy": "all", "sound": "wood",
               "debris": { "count": 12, "size": [0.06, 0.4], "speed": 4, "colors": ["wood", "#8a6a44"] },
               "explode": { "radius": 5.5, "damage": 110 } }
```

| Field | Default | Meaning |
|---|---|---|
| `health` | 50 | Damage it takes to break. A rifle bullet does about 25-35, a melee swing 55, a frag grenade up to 120 at the centre. |
| `breakBy` | `"all"` | `"all"` or a list of `"bullets"`, `"melee"`, `"explosions"` (grenades, explosive props), `"streaks"` (kill-streak strikes). Other damage does nothing to it. |
| `sound` | `"wood"` | `"wood"`, `"glass"`, `"metal"` or `"soft"` (cardboard, plastic, paper). |
| `debris` | | What it breaks into: `count` pieces (0-40, default 8) of `size` (a number or `[min, max]` metres) thrown at `speed` m/s, in `colors` (default: the colours of its parts, weighted by size). |
| `explode` | none | It goes off when it breaks, like a frag grenade owned by whoever broke it: `radius` (1-15 m) and `damage` at the centre (1-300). It breaks other breakables in the blast too. |

How breaking works in a match:

- **The host decides.** It tracks every breakable prop's health and tells everyone when one breaks (players who join mid-match are told which props are already broken). Every PC then hides the prop, drops its collision and throws out its debris.
- **Bullets stop at solid props**, breakable or not: the shot that breaks a crate stops there too, the next one goes through the gap. Cosmetic breakables (bottles) break as a bullet passes through them.
- **Debris is for looks only.** The pieces fly away from the hit (explosions push them outwards), bounce, settle and fade out after 4 to 5 seconds. At most 384 pieces exist at once; the oldest go first.
- **Every match starts with every prop whole again.**
- Bots plan their routes as if every prop were whole, so a broken one just gives them a little more room.

Heavy things (dumpsters, concrete barriers, vending machines, lamps) are left unbreakable in the starter library, so cover you rely on stays put.

## Tips

- Keep the part count sensible: most starter props use 10 to 40 boxes. A box is 12 triangles; the renderer draws hundreds of props in a few draw calls, so detail is cheap, but very fiddly props are hard to read in the game's chunky style anyway.
- Give a prop a few details that read from a distance (a lid, a band, a handle, a label) and sensible colours rather than lots of tiny parts.
- Overlapping parts with the same top height and different colours flicker. Lift one a millimetre or two.
- Check a new prop on the Prop Gallery: add it to `Maps/Dev/PropGallery.json` with `{ "op": "Prop", "name": "My Prop", "x": 0, "z": 0 }` (or put it on any map) and reload.
- `tools/propviz/viz.py` renders prop files to a PNG contact sheet without the game (Python with numpy and Pillow): `python3 tools/propviz/viz.py Source/Props/PropFile.cs Props sheet.png [name filter...]`. `tools/propviz/gen.py` is the script the starter library was generated from.

## The indoor and everyday set

The `Household`, `Office`, `Kitchen`, `Garden` and `Misc` folders hold 123 props for houses, offices, shops and back gardens, generated by `tools/propviz/gen_indoor.py` (run `python3 tools/propviz/gen_indoor.py Props Maps/Dev`; it rewrites those five folders and the **Prop Gallery 3** map in the Dev pack, which shows every one of them in rows by category).

- **Household** (34): beds (double, single, bunk, baby cot), wardrobe, chest of drawers, bookshelf, bedside table, sofa, armchair, coffee table, TV stand, CRT TV, lamps, rugs, dining table and chair, washing machine, tumble dryer, ironing board, toilet, sink, bathtub, shower, laundry basket, radiator, coat stand, mirror, piano, fireplace, vacuum cleaner, fish tank.
- **Office** (20): desk, swivel chair, monitor, PC tower, laptop, photocopier, printer, water cooler, whiteboard, server rack, lockers, partition, archive boxes, conference table, pot plant, shredder, desk phone, metal shelving, safe, reception desk.
- **Kitchen** (21): fridge, counter, sink unit, cooker, microwave, kettle, toaster, pot, frying pan, plates, crates of apples, oranges and cabbages, stacked produce crates, potato sacks, dishwasher, pedal bin, coffee maker, knife block, bar stool, chest freezer.
- **Garden** (23): lawnmower, wheelbarrow, gnome, hose reel, kettle and gas barbecues, patio table and chair, sun lounger, picket fence, fence panel, flower pots, watering can, shed, compost bin and bags, bird bath, raised bed, tools, dog kennel, paddling pool.
- **Misc** (25): ducks (rubber duck, a two-metre inflatable one, a wooden decoy, a mallard, a hen, a family with ducklings, a duck pond), a cat, a dog, a chicken, pigeons, books, toy blocks, a teddy bear, mop and bucket, broom, paint cans, toolbox, suitcase, guitar, duffel bag, backpack, step ladder, sports balls, a bucket. (Air ducts and vents are in the outdoor set.)

Big furniture and appliances are solid (cover you can mantle or hide behind); things you'd find on a desk, a counter or the floor (monitors, kettles, toys, animals) are cosmetic, so put them on a table or a counter and they won't snag players. Desk-top props stand on y = 0: place them on a table or counter top with a `Block` under them or the Prop op's ground snap (up to 0.6 m) onto a counter-height box.

### Health by material (indoor set)

The break sound doubles as the material class, and health follows it (the self-test checks the order and that bigger props of a material are tougher than small ones):

| Material (sound) | Health | Examples |
|---|---|---|
| Glass, porcelain, terracotta (`glass`) | 1-45 | plates 1, lamps 2-4, mirror 3, monitor 5, fish tank 8, flower pots 8-15, shower 15, sink 35, toilet 45 |
| Paper, cardboard, plastic, fabric (`soft`) | 1-130 | rubber duck 1, books 4, laptop 4, laundry 10, archive boxes 20, suitcase 25, water cooler 30, office chair 45, compost bin 55, partition 60, giant duck 60, armchair 90, sofa 130 |
| Wood (`wood`) | 3-450 | toy blocks 3, decoy 5, guitar 8, crates 30, dining chair 35, bedside table 45, fence panel 70, bookshelf 110, wardrobe 180, double bed 200, piano 250, shed 450 (explosions only) |
| Thin metal (`metal`) | 15-420 | kettle 15, PC tower 25, patio chair 50, bar stool 60, whiteboard 70, barbecues 90-150, lawnmower 110, photocopier 170, washing machine 220, fridge 300, bunk bed 320, server rack 420 (no melee) |
| Heavy metal, stone, cast iron | unbreakable | bathtub, fireplace, safe, bird bath, frying pan |
