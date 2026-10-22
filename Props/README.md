# Making props for Call of Slop

Props are the small things that make a map look lived in: trash cans, crates, bricks, newspapers, benches, litter. Every prop is one `.json` file in this `Props` folder, sorted into category folders: the starter library (`Street`, `Industrial`, `Debris`, `Furniture`, `Nature`), the outdoor set (`Vehicles`, `Rooftop`, `Utility`, `Signage`, `Urban`; see [Categories](#categories)), and `Animated` for props with moving or lit parts (see [Animation](#animation)). Maps place them with the `Prop` and `Clutter` ops (see [Maps/README.md](../Maps/README.md#props)).

To add a prop, drop a file into a category folder (or make a new folder: the folder name is the category), then press **Reload map files** in the pause menu, or **Refresh maps** in the lobby. A prop that only one map pack needs goes in that pack's own `Props` folder instead (see [Pack props](#pack-props)). The Dev pack's **Prop Gallery** shows the starter props on their own pads, in rows by category, and **Prop Gallery 2** the outdoor set, so you can check them by eye.

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
| `flag` | no | Makes the prop a flag that can fly a national flag from the flag pool instead of its own cloth: `{ "cloth": [4, 7, 10], "default": "random" }`. See [Flags](#flags). |
| `concealment` | no | Cosmetic props only: `true` makes it hide people from bots (tall crops, see [Concealment](#concealment)). |

Unknown keys, numbers out of range and missing parts are errors with a message saying which file and which part, so typos don't fail silently. A file with an error is skipped and named in the game log (`callofslop.log`).

### Solid or cosmetic

- **Solid** props block movement, bullets, grenades and bots' line of sight, like a wall. Their collision is the `collider` boxes, not the model, so keep colliders simple: one box is usually right. A street lamp's collider is just the base and the pole, a table's is the top and four legs. A prop turned by an angle that isn't a multiple of 90 degrees gets a few boxes following its turned outline. Anything up to 0.55 m tall can be stepped onto; up to 1.7 m can be mantled.
- **Cosmetic** props are for looks only, like grass: you walk and shoot straight through them. Use it for litter, newspapers, bottles, loose bricks: anything small enough that bumping into it would feel wrong. Small cosmetic props fade out with distance.

### Concealment

A cosmetic prop with `"concealment": true` (the **Corn Patch A** and **Corn Patch B** in `Farm`) is still walked and shot straight through, but it hides people from bots the way it hides them from players: a bot's sight line that runs through at least 1.2 m of concealing props in total is blocked, unless the two are within 2.5 m of each other. So someone a step inside a cornfield is hidden from the road, a bot right next to you still sees you, and bots also use the corn as cover to hide in. Only the bots' line of sight checks it (server side): it never changes movement, bullets, the nav graph or the map's fingerprint. The props' bounds go in a coarse grid when a bot first looks, so a query costs a few microseconds (`Map.Concealed`, Source/Props/MapConcealment.cs).

### Parts

| Field | Meaning |
|---|---|
| `size` | `[w, h, d]`, each 0.001 to 24 m (the whole model also fits in 24 m each way). |
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
| `explode` | none | It goes off when it breaks, like a frag grenade owned by whoever broke it: `radius` (1-15 m) and `damage` at the centre (1-300). It breaks other breakables in the blast too. With `fuse` (seconds, 0-5) the hit that breaks it lights it instead: it stays put, spits sparks for that long and then goes off; one lit by another blast burns 0.12-0.32 s at most, so a cluster ripples. `knockback` (m/s at the centre, 0-20, default 9) is how hard the blast pushes players, bodies and debris away. `trigger` makes it a pressure-plate mine (see [Landmines](#landmines)). |

How breaking works in a match:

- **The host decides.** It tracks every breakable prop's health and tells everyone when one breaks (players who join mid-match are told which props are already broken). Every PC then hides the prop, drops its collision and throws out its debris.
- **Bullets stop at solid props**, breakable or not: the shot that breaks a crate stops there too, the next one goes through the gap. Cosmetic breakables (bottles) break as a bullet passes through them.
- **Chain reactions stay cheap.** At most 4 fused props go off per server tick (the rest wait a tick or two), and at most 20 prop fireballs show in any 0.6 s: more still hurt and break things, with their debris and sound.
- **Debris is for looks only.** The pieces fly away from the hit (explosions push them outwards), bounce, settle and fade out after 4 to 5 seconds. At most 384 pieces exist at once; the oldest go first.
- **Every match starts with every prop whole again.**
- Bots plan their routes as if every prop were whole, so a broken one just gives them a little more room.

Heavy things (dumpsters, concrete barriers, vending machines, lamps) are left unbreakable in the starter library, so cover you rely on stays put.

A map can bring any breakable prop back during a match: `{ "op": "Prop", "name": "Landmine", "x": 18, "z": 10, "respawn": 8 }` puts it back 8 seconds after it broke (1 to 3600 s; the default 0 means not until the next match). It waits while anyone is standing on or right next to its spot, so it never comes back under someone's feet. `respawn` is for the `Prop` op only (not `Clutter`), and an unbreakable prop with one is an error.

### Landmines

`Props/Military/Landmine.json` is a half-buried pressure-plate mine: a low olive casing in a dirt ring with a small prong that glints now and then, cosmetic (you walk over it, not into it) and fair to spot if you look at the ground. **Mine Warning Sign** (a red MINES board on a stake, facing +Z) marks a minefield's edge. The Dev pack's **Landmines** map has a small minefield behind signs, a lone mine to try, a mine next to an explosive barrel, one behind a wall, one that comes back after 8 s and a lane bots walk round.

A mine is an explosive breakable prop with a `trigger` in its `explode` block:

```json
"breakable": { "health": 10, "breakBy": "all", "sound": "metal",
               "explode": { "radius": 5, "damage": 160, "fuse": 0.35, "knockback": 12,
                            "trigger": { "radius": 0.6, "height": 0.5 } } }
```

| `trigger` key | Default | Meaning |
|---|---|---|
| `radius` | 0.6 | How far (metres across the ground, scaled with the prop) from its centre feet press the plate, 0.1-3. |
| `height` | 0.5 | How high above its base feet still press it (0.05-2): jump over it and nothing happens. |

How a mine plays:

- **Step on it and it clicks** (every PC hears it). It goes off `fuse` seconds later (0.35 s; a mine without a `fuse` gets 0.35), whether you stay on it or not: the click commits. Standing still on it is death; a quick sprint off it the moment it clicks gets you out with a wound. Living players, bots and zombies all press it.
- **Shoot it, knife it or blow it up** (grenades, launchers, kill streaks, a barrel or another mine) and it goes off after its fuse too; one caught in another blast burns 0.12-0.32 s, so a minefield ripples.
- **The blast is neutral: it hurts everyone**, falling off with distance like a grenade's, shielded by walls, less for anyone prone or wearing Flak Jacket, with knockback, ragdolls, debris, a scorch mark and the smaller fireball. Whoever last hit the mine (shot it, knifed it, threw the grenade) gets the kills they could have made anyway: enemies, and themselves. Everyone else's death, and every death from a mine nobody hit, is nobody's kill: `Landmine` in the feed, a death, no XP or streak for anyone. What a neutral blast sets off (a barrel next to the mine) is neutral too.
- **Bots know where the mines are** (they're part of the map): their routes keep about 1.6 m clear of every mine still there, and close up they step round one. They don't shoot mines on purpose.
- **Network:** the host decides, through the same messages as other breakable props (lit, broken, and since protocol 31 back again). Every match starts with every mine in place.
- **Zombies** maps don't use mines (zombies would set them off for free), but the mechanics work there: zombies press plates and a mine's blast kills them, as the shooter's kills if someone shot it.

## Pack props

Props go in this folder whenever they can be reused. A prop that is specific to one map pack goes in that pack's own `Props` folder, `Maps/<Pack>/Props` (same file format, category subfolders work the same way). Only that pack's maps can use it:

- A pack's maps look a name up in the pack's `Props` folder first, then here. Two packs can each have a prop with the same name without clashing.
- A pack prop with the same name as a prop here **shadows** it for that pack's maps only (in name lookups and in its category); the game log notes it. `"global:Name"` (or `"global:Category"` in a Clutter list) always means the prop from this folder.
- Names must be unique within a pack's folder, as here.
- The pack props a map uses count towards its fingerprint (the file's contents, whitespace and comments aside), so everyone in a game needs the same copy of the pack.

The Dev pack's `Maps/Dev/Props/AppleCrate.json` is an example: a magenta crate that replaces the Kitchen **Apple Crate** on the Dev pack's **Pack Assets** map. See [Maps/README.md](../Maps/README.md#where-assets-go) for the whole rule (props, models and sky images).

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

### Material health guide

Pick a prop's `health` and `sound` from what it's made of, then nudge it for size (a big wooden kiosk takes more than a sandwich board). A rifle bullet does about 25-35, a melee swing 55, a frag grenade up to 120.

| Material | `sound` | `health` | Breaks after | Examples |
|---|---|---|---|---|
| Glass, paper | `glass` / `soft` | 5-20 | one shot | Neon Sign 10, Solar Panel 15, Skylight 20, bottles, newspapers |
| Cardboard, thin plastic | `soft` | 8-30 | one or two shots | cardboard boxes, traffic cone, recycling bin |
| Plastic (moulded, big) | `soft` | 30-60 | two or three shots | Shop Sign 40, Plastic Water Tank 50, Water Barrier 60, Portable Toilet 60 |
| Wood | `wood` | 40-150 | small 40-60, furniture 70-100, big 100-150 | Sandwich Board 40, Hanging Shop Sign 50, crates 40-70, Park Picnic Table 100, Newsstand 140 |
| Thin metal (sheet, tube) | `metal` | 60-180 | three to six shots | Jerry Can 60, Bicycle 60, signs 100, Window AC Unit 120, Air Duct 160 |
| Heavy metal (cabinets, machines) | `metal` | 200-400 | a magazine, or a grenade | Junction Box 200, Rooftop Condenser 250, Ticket Machine 300, Golf Cart 320, ATM and Phone Booth 400 (explosions and strikes only) |
| Concrete, brick, vehicles, structures | | unbreakable | never | cars, buses, trucks, chimneys, poles, billboards, bus shelters, scaffolding, concrete blocks |

Leave out `breakable` for anything players should be able to trust as cover. The self-test holds the outdoor folders to these bands (glass up to 25, soft up to 60, wood 30-150, metal 60-450) and checks that each material's middle value is in the order glass, soft, wood, metal.

## Flags

Decorative flags fly national flags from the **flag pool**: the designs in `Props/Flags/flags.json` (the US, UK, French and Finnish flags). A flag prop says which of its parts are the cloth; the game builds one copy of the prop per design, replacing the cloth with the design's stripes, crosses and stars, and maps pick which one each flag flies. The `Flags` folder holds designs, not props (the prop loader skips it).

### Making a prop a flag

Add a `flag` block naming the cloth's **segments**: the parts that swing in the wind (each with an `anim` block), listed from the pole outwards. Each segment's `follow` parts are its stripes in the prop's own look; together they are the cloth, a flat upright sheet (thin along z or x).

```json
"flag": { "cloth": [4, 7, 10], "default": "random" },
```

- `cloth`: part numbers (1 = the first part) of the segments. Parts outside the cloth (the pole, the base, the finial) stay exactly as they are, and nothing outside the cloth may move with a part of it.
- `default`: `"random"` (pick from the map's pool; right for decorative flags), `"none"` (keep the prop's own cloth unless a map asks for a flag) or a flag id. **Flag on a Pole** defaults to random; **Pennant Pole** and **Hanging Banner** keep their own colours unless a placement names a flag.

The design is **letterboxed**: it keeps its own shape (US 10:19, UK 1:2, France 2:3, Finland 11:18), as large as fits in the cloth, tied on at the top of the hoist (the pole side). Each segment carries the slice of the design that falls on it, so the wind moves the stripes and crosses with the cloth; the segments keep their swing, pivot and phase.

### Adding a flag design

Add an entry to `Props/Flags/flags.json` and press **Reload map files**:

```json
{
  "id": "france",
  "name": "France",
  "aspect": [2, 3],
  "field": "#e4e4e0",
  "shapes": [
    { "color": "#0055a4", "rects": [[0, 0, 0.333333, 1]] },
    { "color": "#ef4135", "rects": [[0.666667, 0, 1, 1]] }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Lowercase letters, digits or `-`, unique. It's what maps write (`"flag": "france"`). |
| `name` | Plain ASCII, for the log and tools. |
| `aspect` | `[height, width]` of the real flag, e.g. `[10, 19]`. Height / width must be 0.3 to 1.5. |
| `field` | The background colour (named colour, `"#rrggbb"` or `[r, g, b]`). |
| `shapes` | Coloured pieces drawn in order on top of the field. Each is `{ "color": ..., "rects": [[u0, v0, u1, v1], ...] }` or `{ "color": ..., "lines": [[u0, v0, u1, v1], ...], "width": w }`. |

- **Flag space:** `u` runs from the hoist (0, at the pole) to the fly (1), `v` from the top (0) to the bottom (1), whatever the flag's shape. Everything must stay inside 0..1.
- **`rects`** are rectangles. A rect with 8 numbers `[u0, v0, u1, v1, countU, countV, stepU, stepV]` repeats as a grid (the US flag's 7 red stripes are one rect repeated 7 times 2/13 apart; its 28 stars are two grids).
- **`lines`** are straight bands from (u0, v0) to (u1, v1), `width` a fraction of the flag's height wide, cut square at the flag's edges (the Union Jack's saltires).
- **Layers:** a shape that overlaps an earlier one is drawn a little thicker (2.5 mm more on each face per layer), so overlapping colours never flicker; shapes side by side share a layer. At most 6 layers and 80 pieces per flag: simplified patterns read better at game distances anyway.
- Bad entries are skipped and named in the log; the self-test ("Flag pool") checks every design.

### Which flag a placed flag flies

1. The placement's own `"flag"` (`{ "op": "Prop", "name": "Flag on a Pole", "x": 4, "z": -6, "flag": "uk" }`): a flag id, `"random"` or `"none"`.
2. Otherwise the prop's `default`.
3. `"random"` picks from the map's pool: every design, or the map file's `"flagPool": ["uk", "us"]` list (for location-specific maps), or none at all with `"flagPool": "none"`.

The random pick hashes the flag's position (rounded to 1/8 m, measured from the map's centre lines), so it's the same on the server and every client, the same every match and after a reload, and flags mirrored across a symmetric map fly the same flag. Flags are looks only: the prop's name, colliders and the map's fingerprint don't change. See [Maps/README.md](../Maps/README.md#flags).

**Cost:** a flag flying a design is one more prop type in the instanced mesh library (one draw per design per chunk, no new shader). The UK and US flags on a Flag on a Pole are about 1,100 and 1,400 vertices (France and Finland a few hundred) against 288 for the plain striped flag: thousands of times fewer than the grass on a Cluttered map. The **Flag Lineup** map in the Dev pack shows all four.

## Categories

| Folder | What's in it |
|---|---|
| `Street` | Bins, dumpsters, benches, lamps, hydrants, mailboxes, cones, barriers, the vending machine, signs. |
| `Industrial` | Crates, pallets, drums (one explosive), pipes, sandbags, tyres, cable spools, gas cylinders. |
| `Military` | A pressure-plate **Landmine** and a **Mine Warning Sign** (see [Landmines](#landmines)). |
| `Volatile` | Things that go off, with a short fuse: Red Fuel Barrel, Propane Tank, Propane Bulk Tank (an 11 m blast), Gas Bottle, Gas Bottle Cage, Flammables Cabinet; and two that only break: Chemical Drum, IBC Tote. |
| `Debris` | Bricks, rubble, planks, cardboard, newspapers, bottles, cans, bags, litter. |
| `Furniture` | Chairs, a table, a sofa, a filing cabinet. |
| `Nature` | A planter, a log, a stump, a hay bale. |
| `Vehicles` | Cars (hatchback, sedan, SUV, pickup, van, taxi, police car, ambulance, burnt-out wreck, car on bricks, overturned car), a bus, a box truck, a truck cab, a forklift, a golf cart, a motorbike, a moped, a bicycle, a kick scooter, a trailer, and loose bits (wheel, door, engine block, jerry can). Vehicles are solid, unbreakable cover with a simple two-box collider; long ones run along X. |
| `Rooftop` | Air conditioners (window unit, condenser, split unit, big HVAC unit), vents, air ducts (straight, elbow, riser, round), an exhaust fan, chimneys and flues, a water tower and a plastic tank, a satellite dish, aerials, a radio mast, a solar panel, a skylight, a roof hatch, a pipe rack. |
| `Utility` | Power poles and transformers, junction boxes, street cabinets, manholes and drains, meters, fire escape stairs, landing and ladder, ladders, a scaffold tower and boards, generators, a gas valve, a floodlight tower, a road plate. |
| `Signage` | Traffic lights, a pedestrian signal, road signs (speed limit, yield, one way, no parking, crossing, road works, direction), a parking meter, a bus stop sign, a billboard, shop signs (box, hanging, neon), an arrow board trailer, a price pylon, a street clock. |
| `Urban` | A bus shelter, a phone booth, a payphone, a newsstand, a newspaper box, an ATM, a ticket machine, a post box, bins, a bike rack, a picnic table, a hot dog cart, a market stall, fences and barriers, a concrete block, a speed bump, a boom gate, a portable toilet, a builder's skip. |
| `Farm` | Field corn (Corn Patch A and B: 3 x 3 m, taller than you, concealment; Trampled Corn), a scarecrow, a row crop tractor, a hay wagon, a big round bale, a grain bin, a split rail fence, a wheel line irrigator, a roadside farm stand, a stock tank, a pumpkin pile, a farm fuel tank. Written by `tools/world/mainstreet_props.py`. |
| `Rail` | Railroad track (6 m sections of rails, ties and ballast), a crossbuck sign, a freight boxcar, an open boxcar you can climb through, a covered hopper car (the cars run 11.6 m along X). |
| `Town` | A town-square bandstand gazebo you can step up into, a granite memorial obelisk. |
| `Military` | The reusable military set (see [Military/README.md](Military/README.md)): sandbag walls, corners, nests and piles, barbed wire, a steel hedgehog, tank traps, a stake barrier, a field cannon, an anti-tank gun, a howitzer, a mortar pit, a machine gun, ammunition crates, cases and cans, shell crates (explosive), pallets, a camo net, tents (the command tent is walk-in), fuel drum and jerrycan racks (explosive), a searchlight, a guard tower. Its `Vehicles` subfolder is the category `Military Vehicles`: five tanks (WWII medium, heavy, modern main battle tank, light, wrecked), four parked planes (WWII fighter, twin-engine bomber, jet fighter, a walk-in transport plane), a jeep, a covered cargo truck, an armoured car, a fuel tanker and a sidecar motorcycle, with Desert and Winter colour variants. Generated by `tools/propviz/gen_military.py`; shown on the Dev pack's **Military Props** map. |
| `British` | UK street and railway props: a black cab, a double-decker bus, a Belisha beacon (pulsing; it is also on **Animated Props**), iron park railings, a heritage lamp post, a canal narrowboat, a canal lock gate (the leaf over the water, the balance beam on the bank), a mooring bollard, railway track, a commuter train car, a railway signal and a station ticket gate. Generated by `tools/world/london_props.py`; shown on the Dev pack's **London Street** map. |
| `Animated` | Props with moving or lit parts (see [Animation](#animation)): fans (ceiling, desk, pedestal, drum, exhaust box, AC condenser, cooling unit), a roof turbine vent, a windmill, a small wind turbine, a weather vane, an anemometer, flags, a banner, a swinging shop sign, a hanging lantern, a flickering street lamp, a neon OPEN sign, beacons and alarm lights, a cycling traffic light, railway crossing lights, a police light bar, a lighthouse, radar and satellite dishes, a ticking street clock, a ferris wheel and a carousel, an oil pumpjack, conveyor rollers, a running generator and washer, a cement mixer, a water wheel, a barber pole, a rotating diner sign, a piston pump, a pinwheel, a lawn sprinkler, a rocking chair, a hammock, wind chimes, a grandfather clock, a metronome, a globe, a Newtons cradle, a swing set, a bell frame, a boom gate, a gantry hook, a freight hoist, a buoy, a fishing bobber, a duck on a pond, a bird on a perch, a laundry line, a smoking chimney, a fountain, a TV, a monitor, an arcade cabinet, a slot machine and a glowing vending machine. Most stand on the ground; the Ceiling Fan hangs (use `"y"`: its top is 0.7 m up). |
| `Forest` | Boreal forest floor and cover (tools/world/finland_props.py): granite boulders and a granite outcrop, a fallen spruce log, a spruce log pile and a birch firewood stack (solid); autumn bracken, birch leaf litter, mossy stones, fly agaric toadstools, a dead pine branch, lingonberry heath and an anthill (cosmetic). |
| `Mining` | Mine kit: an ore mine cart, mine rail, a tunnel timber frame and cage lamp, a mine timber stack, blasting explosives (they go off), an ore bin hopper, a belt conveyor incline, a jaw crusher, a dump truck, a tracked excavator, a floodlight mast, a diesel tank, a blasting warning sign, a broad-gauge railway track section, a rail buffer stop and an ore hopper wagon. |
| `Nordic` | Nordic village bits: a roadside mailbox rack, a kicksled, a moose warning sign, a wooden rowing boat, a lifebuoy post, a jetty ladder, a birch log bench and a sauna bucket. |

Things that face a way (signs, traffic lights, AC units, shop fronts) face **+Z**. Things meant for a wall (Window AC Unit, Vent Grille, Shop Sign, Neon Sign, Electric Meter) are modelled standing on y = 0 with their back at -Z: put them against a wall and lift them with the `Prop` op's `y` (for example `"y": 2.5`). Fire Escape Stairs climb 3 m along +X and meet a Fire Escape Landing (deck at 3 m) placed at the top.

## Tips

- Keep the part count sensible: most starter props use 10 to 40 boxes. A box is 12 triangles; the renderer draws hundreds of props in a few draw calls, so detail is cheap, but very fiddly props are hard to read in the game's chunky style anyway.
- Give a prop a few details that read from a distance (a lid, a band, a handle, a label) and sensible colours rather than lots of tiny parts.
- Overlapping parts with the same top height and different colours flicker. Lift one a millimetre or two.
- Check a new prop on the Prop Gallery: add it to `Maps/Dev/PropGallery.json` with `{ "op": "Prop", "name": "My Prop", "x": 0, "z": 0 }` (or put it on any map) and reload.
- `tools/propviz/viz.py` renders prop files to a PNG contact sheet without the game (Python with numpy and Pillow): `python3 tools/propviz/viz.py Source/Props/PropFile.cs Props sheet.png [name filter...]`. `tools/propviz/gen.py` is the script the starter library was generated from, and `tools/propviz/gen_outdoor.py` writes the outdoor folders and `Maps/Dev/PropGallery2.json` (run it from the repository root).

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
