# Making props for Call of Slop

Props are the small things that make a map look lived in: trash cans, crates, bricks, newspapers, benches, litter. Every prop is one `.json` file in this `Props` folder, sorted into category folders (`Street`, `Industrial`, `Debris`, `Furniture`, `Nature`, and `Animated` for props with moving or lit parts). Maps place them with the `Prop` and `Clutter` ops (see [Maps/README.md](../Maps/README.md#props)).

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
| `anim` | Makes the part move or light up (optional, see [Animation](#animation)). |

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

## Animation

Any part can move or light up with an `"anim"` block: fan blades spin, signs swing, buoys bob, generators shake, neon blinks, traffic lights cycle. The `Animated` folder has 65 examples and the **Animated Props** map in the Dev pack shows them all.

```json
{ "size": [0.62, 0.02, 0.15], "offset": [0.31, 0.2, 0], "color": "darkwood",
  "anim": { "type": "spin", "axis": "y", "pivot": [0, 0.2, 0], "rpm": 120 } }
```

It's **for looks only**: animation never moves the prop's colliders, never changes what bullets or bots see, and isn't sent over the network. So give an animated prop a `collider` list that covers its still parts (the base, the post): the default `"bounds"` box would wrap the moving parts' rest pose too. The motion is worked out on the graphics card from the time and the prop's position, so it costs nothing on the CPU, every copy of a prop starts at a different point (a row of fans doesn't turn in lockstep), and every PC shows the same thing at the same time. Beyond its freeze distance (80 m unless a part says otherwise) a prop holds still in its rest pose, the pose the file describes; swings and bobs settle over the last 15% before that.

### Motion types

| Type | Does | Keys |
|---|---|---|
| `spin` (or `rotate`) | Turns round and round: fans, wheels, windmills, radar dishes, clock hands. | `rpm` (turns a minute, negative turns the other way, up to 600), `axis` (default `"y"`) |
| `swing` (or `oscillate`) | Swings back and forth: signs, pendulums, flags, lanterns, rocking chairs. | `angle` (degrees either way), `period` (seconds per swing there and back, default 1), `axis` (default `"x"`), `oneWay` (true: swings out to one side and back, resting in between: a boom gate, a Newton's cradle ball) |
| `bob` (or `translate`) | Moves along the axis and back: buoys, pistons, a goods lift, smoke puffs. | `distance` (metres either way), `period`, `axis` (default `"y"`) |
| `shake` (or `vibrate`) | A small fast jitter: a running generator, a washing machine on spin. | `distance` (keep it tiny: 0.005-0.02), `period` (default 0.08), `axis` (default `"y"`; `[1, 0.3, 0.2]` shakes in several directions) |
| `follow` | No motion of its own: moves with its `parent`. Use it for the rest of a moving assembly (the cage of a fan head). | `parent` |

Shared keys for the motion types:

| Key | Default | Meaning |
|---|---|---|
| `axis` | see above | `"x"`, `"y"`, `"z"` (or `"-x"`...) or `[x, y, z]` (any direction, normalised), in the prop's frame. |
| `pivot` | the part's centre | The point it turns about, in the prop's frame like `offset`. Put a sign's pivot at its top edge, a fan's at its hub. |
| `phase` | 0 | Where in its cycle it starts, 0 to 1 (0.5 = half a cycle later). Use it to stagger parts of one prop. |
| `randomPhase` | true | Each copy of the prop adds its own offset (from its position on the map). Set false to keep copies in step. |
| `jitter` | 0 | Each copy runs up to this much faster or slower (0 to 0.5: 0.2 = plus or minus 20%). |
| `wind` | 0 | 0 to 1: how much the foliage's wind gusts push it. Swings and bobs get wider in a gust; spins speed up and slow down. |
| `parent` | none | The number of another animated part (1 = the first part in `parts`) whose motion this part also follows, after its own. Chains of up to 4 parts. |
| `glow` | 0 | Constant glow (0 to 4) in `color` (default the part's colour), e.g. a spinning lighthouse lamp. |
| `freezeDistance` | 80 | Metres. Beyond this the whole prop holds still (the largest of its parts' values counts). |

### Light types

The part changes between its `off` and `color` colours and glows (light added on top of the sun and shade) while it's on. Beyond the freeze distance it shows its own `color` from the `parts` entry, so pick the colour you want it to have far away.

| Type | Does | Keys |
|---|---|---|
| `blink` | On for `duty` of each `period`, then off: warning lights, neon, crossing lamps. | `period`, `duty` (0 to 1, default 0.5) |
| `pulse` | Fades smoothly on and off: beacons, a glowing vending machine. | `period` |
| `flicker` | Mostly on, with random dips: a dodgy bulb, a lantern flame, a TV screen. | `rate` (changes a second, default 10), `duty` (how often it's on, e.g. 0.85) |
| `cycle` | The period is cut into `steps`; it's on in the steps listed in `on`: traffic lights, chasing bulbs. | `period`, `steps` (2 to 16), `on` (step numbers, 0 = the first) |

Every light type also takes `color` (on, default the part's colour), `off` (default a dark version of it), `glow` (default 0.6), `phase`, `randomPhase`, `jitter` and `parent` (a light can ride on a moving part).

### Examples

A hanging shop sign (the board swings about its top; its lettering follows it):

```json
{ "size": [0.8, 0.5, 0.04], "offset": [0.55, 2.2, 0], "color": "darkwood",
  "anim": { "type": "swing", "axis": "x", "pivot": [0.55, 2.58, 0], "angle": 12, "period": 2.6, "wind": 1, "jitter": 0.25 } },
{ "size": [0.6, 0.12, 0.05], "offset": [0.55, 2.25, 0], "color": "yellow", "anim": { "type": "follow", "parent": 5 } }
```

A desk fan's blades spin about their hub while the head they're on swivels (the blades name the head as their `parent`):

```json
{ "size": [0.1, 0.1, 0.12], "offset": [0, 0.38, -0.07], "color": "darkgrey",
  "anim": { "type": "swing", "axis": "y", "pivot": [0, 0.38, -0.05], "angle": 35, "period": 8 } },
{ "size": [0.13, 0.06, 0.01], "offset": [0.065, 0.38, 0.025], "color": "blue",
  "anim": { "type": "spin", "axis": "z", "pivot": [0, 0.38, 0.025], "rpm": 240, "parent": 3 } }
```

A buoy bobbing (the bottom part) and rocking on it (the rest follow), and a traffic light's green lamp (lit for the first three of eight steps of a 16 s cycle):

```json
{ "size": [0.3, 0.3, 0.3], "offset": [0, 0.15, 0], "color": "darkgrey", "anim": { "type": "bob", "distance": 0.12, "period": 3.2 } },
{ "size": [0.18, 0.18, 0.03], "offset": [0, 2.87, 0.14], "color": "#30ff60",
  "anim": { "type": "cycle", "period": 16, "steps": 8, "on": [0, 1, 2], "color": "#30ff60", "off": "#063a10", "glow": 1.4 } }
```

A generator shaking, and a flickering bulb:

```json
"anim": { "type": "shake", "axis": [0.4, 1, 0.2], "distance": 0.008, "period": 0.07 }
"anim": { "type": "flicker", "rate": 7, "duty": 0.82, "color": "#fff0c0", "off": "#403c30", "glow": 1.2 }
```

Tips:

- A ferris wheel's gondolas stay level by spinning back the other way about their hanger (`rpm: -3` with the wheel's `rpm: 3` as `parent`).
- Several parts with the same block (four fan blades) stay together: they get the same per-copy phase and speed.
- Keep moving parts out of the `collider` list; small parts that move a centimetre (a shaking generator body) can stay in it.
- `tools/propviz/viz_anim.py Source/Props/PropFile.cs Props/Animated sheet.png [name...]` draws each animated prop at three moments, to check pivots and axes without the game. `tools/propviz/gen_animated.py` is the script the `Animated` folder and the Animated Props map were generated from.
- Unknown keys, a missing `rpm` or `angle`, keys that don't apply to the type (a `pivot` on a light), a pivot more than 1 m outside the model, a `parent` without an `anim` block or parents going round in a loop are errors that name the file and the part.

How it's drawn: the animated parts' vertices carry the number of their animation record (a second vertex stream next to the shared prop mesh); the records sit in a small float texture. Animated prop types near the camera are drawn by a second shader that reads them; everything else, including animated props past their freeze distance, uses the plain prop shader, so props without animation draw exactly as before. The `[props]` log line adds how many animated props were drawn moving and still and the animated pass's GPU time.

## Tips

- Keep the part count sensible: most starter props use 10 to 40 boxes. A box is 12 triangles; the renderer draws hundreds of props in a few draw calls, so detail is cheap, but very fiddly props are hard to read in the game's chunky style anyway.
- Give a prop a few details that read from a distance (a lid, a band, a handle, a label) and sensible colours rather than lots of tiny parts.
- Overlapping parts with the same top height and different colours flicker. Lift one a millimetre or two.
- Check a new prop on the Prop Gallery: add it to `Maps/Dev/PropGallery.json` with `{ "op": "Prop", "name": "My Prop", "x": 0, "z": 0 }` (or put it on any map) and reload.
- `tools/propviz/viz.py` renders prop files to a PNG contact sheet without the game (Python with numpy and Pillow): `python3 tools/propviz/viz.py Source/Props/PropFile.cs Props sheet.png [name filter...]`. `tools/propviz/gen.py` is the script the starter library was generated from.
