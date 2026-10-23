# Making weapons for Call of Slop

Every weapon is a single `.json` file in this folder. The 58 weapons that ship with the game (50 guns and 8 launchers) are ordinary weapon files too, sorted into one subfolder per class (`Pistols/`, `SMGs/`, `AssaultRifles/`, ..., `Launchers/`). Open any of them to see a real one. Subfolders are only for tidiness: the game reads every `*.json` under `Weapons/`, however deep. The one exception is `Weapons/Frenzy/`: `classes.json` there holds the kill frenzy's per-class character and buffs (see the main README), not a weapon.

To add a weapon, drop its file in here (or in a subfolder such as `Weapons/MyPack/`) and restart the game. Nothing needs recompiling. The gun's 3D model and its gunshot, reload and equip sounds are generated from its stats, so a file is all a weapon needs.

Attachments work the same way, in the `Attachments` folder: see [Attachments/README.md](../Attachments/README.md).

## The quick version

```json
{
  "format": "call-of-slop-weapon/1",
  "name": "Pepperbox SMG",
  "author": "YourName",
  "order": 100,
  "unlockLevel": 12,
  "class": "SMG",
  "blurb": "Spits tiny rounds very fast. Falls apart at range.",
  "damage": 16,
  "rangeM": 12,
  "rpm": 1000,
  "mag": 45,
  "reload": 2.3,
  "hipSpreadDeg": 2.2,
  "recoilDeg": 0.4,
  "recoil": { "horizontal": 1.1, "jitter": 1.6 },
  "mobility": 1.07,
  "length": 0.35,
  "accent": "#3fbf6f",
  "caliber": 0.2,
  "bright": 0.7,
  "tail": 0.3
}
```

Check a file without starting the game:

```
CallOfSlop.exe --check-weapon Weapons\MyPack\PepperboxSMG.json
```

It prints `OK:` with the weapon's DPS, shots to kill, the attachments that fit it and when it unlocks, or `ERROR:` with the file and the problem. A file with a mistake is skipped when the game starts, and the lobby shows a red line naming the file and the problem (the full list goes to `callofslop.log`).

## Fields

Only `name`, `class`, `damage`, `rangeM`, `rpm`, `mag`, `reload` and `hipSpreadDeg` are required. Everything else has the default shown. The shipped files write every field out, so you can copy one and edit it. A field name the game doesn't know (a typo) is an error, so mistakes don't go unnoticed. Comments (`//`) and trailing commas are allowed.

A number outside the allowed range is rejected. The "usual" column is what the shipped weapons use.

### About the file

| Field | Meaning | Default | Allowed |
| --- | --- | --- | --- |
| `format` | Always `"call-of-slop-weapon/1"`. | | |
| `name` | Shown everywhere and used in save files. Must be unique. | required | 1-40 characters, no `=` |
| `author` | Your name. Optional. | `""` | |
| `order` | Position in the weapon list. Lower comes first, ties sort by name. See "Ids and order" below. | `1000` | any whole number |
| `unlockLevel` | Account level that unlocks it. See "Unlocking" below. | max level | 1 or more |
| `class` | `Pistol`, `SMG`, `AssaultRifle`, `DMR`, `Sniper`, `Shotgun`, `LMG`, `Special` or `Launcher`. Pistols and launchers go in the **secondary** slot, everything else in the **primary** (a launcher fires projectiles: see [Launchers](#launchers)). The class also picks the gun's model shape, the reload sound and which attachments fit. | required | |
| `blurb` | One line shown in the loadout menu. Plain ASCII only: the game's font has no curly quotes, long dashes, accented letters or symbols like × and °, which show up as `?`. Write `'`, `-`, `x` and `deg` instead. `%` is fine. | `""` | |

### Damage

| Field | Meaning | Default | Usual | Allowed |
| --- | --- | --- | --- | --- |
| `damage` | Damage per bullet (per pellet for shotguns) inside `rangeM`. Players have 100 health. | required | 6-140 | 1-1000 |
| `headMult` | Headshot multiplier. | `2` | 1.5-3 | 0.5-10 |
| `pellets` | Bullets per shot. Above 1 makes a shotgun spread. | `1` | 1-10 | 1-50 |
| `rangeM` | Metres of full damage. Past it, damage falls off linearly until 3x this distance. | required | 5-300 | 0.5-1000 |
| `minDamageMult` | The damage multiplier reached at 3x `rangeM` and beyond. `1` = no falloff. | `0.6` | 0.12-1 | 0-1 |

### Handling

| Field | Meaning | Default | Usual | Allowed |
| --- | --- | --- | --- | --- |
| `rpm` | Fire rate, rounds per minute. For burst guns, the rate inside a burst. | required | 30-1500 | 1-3000 |
| `mode` | `Semi` (one shot per click), `Auto` (hold to fire) or `Burst`. | `Auto` | | |
| `burstCount` | Rounds per burst (only for `Burst`). | `3` | 2-3 | 1-20 |
| `burstDelay` | Seconds of pause after each burst. | `0.25` | 0.2-0.3 | 0-5 |
| `mag` | Magazine size. | required | 1-200 | 1-1000 |
| `reload` | Reload time in seconds. | required | 1.3-7 | 0.1-20 |
| `hipSpreadDeg` | Hip-fire cone, in degrees. Firing blooms it up to about 2.5x. | required | 0.8-7 | 0.01-45 |
| `adsSpreadMult` | Spread while aiming, as a fraction of hip spread. `0` = perfectly accurate when scoped. | `0.3` | 0-0.85 | 0-2 |
| `recoilDeg` | Overall recoil: view kick per shot, in degrees. The `recoil` block's kicks are fractions of it, and attachments that change it scale everything. | `0.7` | 0.1-5.5 | 0-20 |
| `recoil` | How the gun kicks: its spray pattern, recovery and camera shake. See "Recoil" below. Any field you leave out (or the whole block) gets the default of the weapon's `class`. | class defaults | | |
| `mobility` | Movement speed multiplier while holding it. The server also uses it to cap your speed. Attachments can push it, but kitted guns are clamped to 0.6-1.25. | `1` | 0.75-1.12 | 0.3-1.5 |
| `adsZoom` | Zoom when aiming down sights (the field of view is divided by this). Snipers use 3.5-4.5 for their built-in scope. | `1.35` | 1.1-4.5 | 1-10 |
| `equipTime` | Seconds to draw the weapon. | `0.45` | 0.25-1.1 | 0.05-5 |
| `adsTime` | Seconds to fully aim down sights (kitted guns are clamped to 0.08-0.5). | `0.16` | 0.16 | 0.05-2 |

### Recoil

The `recoil` block sits right after `recoilDeg`. Every field is optional: leave one out and it takes the value of the weapon's class (the class defaults are what the plain shipped guns use, for example the Anvil LMG, the P9 Sidearm or the Breacher Pump). The kicks are fractions of `recoilDeg`, so `"vertical": 1` with `"recoilDeg": 0.6` kicks 0.6 degrees up per shot.

```
"recoilDeg": 0.6,
"recoil": {
  "vertical": 1, "horizontal": 0.35, "bias": 0.2, "jitter": 0.2, "firstShot": 1.15,
  "seed": 1666, "patternShots": 12, "rampVertical": 1.2, "rampHorizontal": 1.3, "sway": 0.7, "swayPeriod": 14,
  "recovery": 9, "recoveryDelay": 0.1, "recoveryAmount": 0.7, "resetTime": 0.3,
  "crouchVertical": 0.85, "crouchHorizontal": 0.75, "proneVertical": 0.7, "proneHorizontal": 0.55, "adsVertical": 0.75, "adsHorizontal": 0.6,
  "shake": 0.12, "shakeFreq": 18, "shakeTime": 0.12, "punch": 0.35, "punchFov": 1.2, "punchRoll": 0.8, "punchTime": 0.1,
  "kickBack": 0.04, "kickUp": 2.5, "kickRoll": 1.5, "kickSide": 0.004, "kickTime": 0.08
},
```

How a shot kicks: the aim goes up by `recoilDeg x vertical` and right by `recoilDeg x horizontal x (bias + sway wave + jitter)`, both growing along the ramp, times the stance and aiming multipliers. The pattern (ramp and sway) depends only on the gun and which shot of the spray it is, so it's the same every time and you can learn to pull against it. The jitter is random-looking but seeded too. A spray starts over (shot 1 again) after `resetTime` seconds without firing.

**The kick**

| Field | Meaning | Usual | Allowed |
| --- | --- | --- | --- |
| `vertical` | Upward kick per shot, as a fraction of `recoilDeg`. | 0.5-1.4 (the Toy Blaster uses 2) | 0-5 |
| `horizontal` | Sideways kick per shot, as a fraction of `recoilDeg`. Multiplies `bias`, `sway` and `jitter`. | 0.05-1.2 | 0-5 |
| `bias` | Steady drift: positive pulls right, negative left, `0` = no preference. | -0.35-0.35 | -1-1 |
| `jitter` | Random sideways (and a little vertical) shake per shot. High = erratic (SMGs use 1-1.8), low = clean. | 0.15-1.8 | 0-3 |
| `firstShot` | Multiplier on the first shot of a spray. Above 1 = a hard first kick; below 1 = a soft start (the Grinder's spin-up). | 0.35-1.5 | 0-5 |

**The pattern**

| Field | Meaning | Usual | Allowed |
| --- | --- | --- | --- |
| `seed` | Picks this gun's pattern. `0` = made from the weapon's name. Change it to get a different shape with the same strength. | any | 0-1000000 |
| `patternShots` | Shots over which the ramps grow, from 1x at the first shot to the full ramp. | 1-40 | 1-100 |
| `rampVertical` | Vertical kick multiplier reached after `patternShots` (above 1 = the climb gets steeper). | 1-2.6 | 0-5 |
| `rampHorizontal` | The same for the sideways kick. | 1-2.6 | 0-5 |
| `sway` | Size of the seeded side-to-side wave the spray follows (it fades in over the first 4 shots, so sprays start straight up). | 0-1.6 | 0-3 |
| `swayPeriod` | Shots per sway cycle: short = zig-zag, long = a slow snake. | 3-18 | 2-100 |

**Recovery**

| Field | Meaning | Usual | Allowed |
| --- | --- | --- | --- |
| `recovery` | How fast the aim drifts back after you stop firing (per second: 9 = about 63% of the way in 0.11 s). | 3-22 | 0-40 |
| `recoveryDelay` | Seconds after the last shot before recovery starts. Automatic guns also wait out their fire interval, so they never recover mid-spray. | 0.03-0.35 | 0-2 |
| `recoveryAmount` | Fraction of the climb that comes back. It never pulls past where you started, so pulling down yourself isn't punished. | 0.6-0.9 | 0-1 |
| `resetTime` | Seconds without firing after which the next shot starts a new spray. | 0.15-0.6 | 0.05-3 |

**Stance and aiming** (multipliers on the kick; standing hip fire is 1)

| Field | Meaning | Usual | Allowed |
| --- | --- | --- | --- |
| `crouchVertical`, `crouchHorizontal` | Crouched. LMGs use about 0.08 sideways: braced, they barely move left or right. | 0.08-1 | 0-3 |
| `proneVertical`, `proneHorizontal` | Prone. LMGs use 0.03-0.06 sideways. | 0.03-1 | 0-3 |
| `adsVertical`, `adsHorizontal` | Aiming down sights (blended in as you raise the gun). | 0.6-1 | 0-3 |

**What you see** (visual only: none of these move your actual aim)

| Field | Meaning | Usual | Allowed |
| --- | --- | --- | --- |
| `shake` | Camera shake per shot, in degrees (smaller through magnifying optics). | 0.08-1.2 | 0-10 |
| `shakeFreq` | Shake frequency in Hz: low = heavy wobble, high = buzz (the Arc Carbine uses 60). | 7-60 | 0-80 |
| `shakeTime` | Seconds for the shake to die down. | 0.06-0.45 | 0.01-2 |
| `punch` | Camera punch upward per shot, in degrees, on top of the real kick. | 0.15-5 | 0-10 |
| `punchFov` | Field-of-view punch per shot, in percent (the view briefly widens). | 0.5-8 | 0-20 |
| `punchRoll` | Camera roll per shot, in degrees (alternating sides). | 0.3-6 | 0-20 |
| `punchTime` | Seconds for the punch to settle. | 0.06-0.3 | 0.01-2 |
| `kickBack` | The gun in your hands jumps back towards you, in metres. | 0.015-0.22 | 0-0.3 |
| `kickUp` | The gun's muzzle flips up, in degrees (pistols flip a lot). | 0.6-30 | 0-30 |
| `kickRoll` | The gun rolls, in degrees. | 0.4-12 | 0-30 |
| `kickSide` | The gun shifts sideways with the kick, in metres. | 0.002-0.01 | 0-0.1 |
| `kickTime` | Seconds for the gun to settle back. | 0.04-0.3 | 0.01-2 |

Bots don't follow the pattern, but their aim is knocked by each shot from the same numbers (`vertical`, `horizontal`, `bias`, `sway`, `jitter`). Attachments change recoil fields by their own names with a `recoil` prefix, for example `{ "stat": "recoilVertical", "mul": 0.7 }` (see [Attachments/README.md](../Attachments/README.md)).

### Look

Without a `model` block the gun is built from its class and these numbers (plus a few name hints: a name containing "Rotary", "Double", "Lever" or "Musket" gets those details). Every shipped weapon has a `model` block instead (below), so each one has its own silhouette.

| Field | Meaning | Default | Usual | Allowed |
| --- | --- | --- | --- | --- |
| `length` | Overall length in metres. | `0.42` | 0.15-0.86 | 0.1-1.5 |
| `bulk` | Thickness multiplier for the receiver and furniture. | `1` | 0.75-1.7 | 0.3-3 |
| `accent` | Accent colour: `[r, g, b]` with values 0-1, or `"#rrggbb"`. | `[0.95, 0.5, 0.15]` | | each 0-1 |

#### The `model` block

An optional object that says what the gun is made of. It starts from an archetype (`"base"`) and changes any part of it:

```
"model": { "base": "pump_shotgun", "barrelLength": 0.24, "stock": "crane", "muzzle": "breacher", "sideSaddle": true, "finish": "black" }
```

- **Archetypes** (`base`, default `rifle`): `pistol`, `revolver`, `derringer`, `smg`, `pdw`, `rifle`, `battle_rifle`, `bullpup`, `dmr`, `lever_rifle`, `bolt_sniper`, `pump_shotgun`, `semi_shotgun`, `double_barrel`, `drum_shotgun`, `lever_shotgun`, `lmg`, `rotary`, `musket`, `coil_rifle`, `energy`, `tool`, `toy`, and for launchers `rocket_tube` and `grenade_launcher`. An archetype's sizes follow the weapon's `length` and `bulk`; sizes you write yourself are metres, angles are degrees.
- **Receiver:** `receiver` (box, slab, round, slide, revolver, bullpup, lever, break, tool, toy, sci), `receiverLength`, `receiverHeight`, `receiverWidth`, `topRail`, `bolt` (none, side, rear, charging, top), `hammer`, `lever`, `canister`, `sideSaddle`.
- **Barrel:** `barrels` (1-8), `barrelLayout` (single, side, over, rotary), `barrelStyle` (round, square, fluted), `barrelLength`, `barrelWidth`, `shroud` (none, jacket, perforated, heatshield, suppressor, coils, rails) with `shroudLength` (fraction of the barrel), `gasBlock`, `ramrod`, `coils` (0-10), `muzzle` (none, crown, flash, brake, bell, comp, tip, breacher, ported; a muzzle attachment replaces it).
- **Handguard:** `handguard` (none, rail, slim, ribbed, wood, quad, tube, forend, pump, stock), `handguardLength`, `handguardHeight`, `foregrip` (none, vertical, stub, folding, strap), `bipod` (none, folded, deployed).
- **Stock and grip:** `stock` (none, fixed, crane, skeleton, folding, wire, wood, straight, thumbhole, chassis, bullpup, toy, spade), `stockLength`, `stockDrop`, `cheekRest`, `grip` (pistol, wood, straight, birdshead, none), `gripAngle`.
- **Magazine:** `mag` (box, curved, stick, drum, tube, pan, topbox, side, internal, beltbox, helical, grip, cell, strip, striker, none), `magLength` (0 = from the magazine size), `magOffset`, `magAngle`.
- **Sights:** `sights` (post, tall, bead, aperture, ghost, none), `carryHandle`. Iron sights and carry handles come off when an optic is fitted, and anything that would reach into the optic is trimmed to the rail.
- **Colours:** `finish`, `furniture`, `magColor`: `accent`, a name (black, gunmetal, steel, chrome, brass, copper, polymer, tan, olive, wood, darkwood, lightwood, white, rust, orange, yellow), `[r, g, b]` or `"#rrggbb"`; `accentStyle` (none, stripe, mag, panel, glow, tips, bands) says where the accent colour shows.

The hands follow the model (grip, handguard or pump, magazine). `tools/gunviz/viz.py` (Python + Pillow) draws contact sheets of every gun from these blocks, side and top views, to design with.

### Sound

The gunshot is synthesised from these when the game starts. Real firearms (`Gun`, `Boom`, `Suppressed`) also get recorded gunshot layers: the weapon's `class`, `sound`, `caliber` and `rpm` pick a sample set by the rules in `Audio/Samples/Guns/manifest.json`, `caliber` and `bright` set its pitch and `tail` its length (see `Audio/Samples/Guns/README.md`).

| Field | Meaning | Default | Allowed |
| --- | --- | --- | --- |
| `sound` | Recipe: `Gun`, `Suppressed` (built-in silencer), `Boom` (huge, for .50 cal and muskets), `Zap` (energy), `Toy`, `Pneumatic`, or for launchers `Rocket` (a backblast roar and the motor pulling away) and `Thump` (a grenade launcher's hollow thoomp). Launcher sounds are synthesised only (no recorded layers). | `Gun` | |
| `caliber` | 0 = small and snappy, 1 = huge and deep. | `0.5` | 0-1 |
| `bright` | High-frequency crack of the blast. | `0.5` | 0-1 |
| `tail` | Length of the echo. | `0.5` | 0-1 |

**Bullet penetration** (see the main README, "Bullet penetration"; the material table is `Materials/penetration.json`):

| Field | Meaning | Default | Allowed |
| --- | --- | --- | --- |
| `penetration` | Penetration power: how much wall a round goes through (10 cm of wood costs 10, 10 cm of brick 45). `-1` works it out from the weapon's `class` and `caliber` (and gives shotgun pellets almost none). | `-1` | -1-500 |
| `penetrationMult` | Multiplies the power; for attachments (AP Rounds 1.6, Hollow Point Mag 0.6). | `1` | 0-5 |

**Every weapon sounds like itself.** On top of its sample set, each weapon gets its own sonic identity (`Source/Audio/GunVoice.cs`), derived from a hash of its `name`, its stats and its rank among the weapons of its set and class (ranked by that hash, so file order and ids don't matter; the same weapon files give the same sounds on every client and every run): which of the set's recorded firearms it mostly plays (the weapons of a set take them in turn; a main one for about three shots in four, a second one mixed in), a character (five tone steps from bright to dark and a body resonance band, a low thump or a high ring, so no two weapons of a class share one), a pitch offset (about +-3%, a longer gun a touch lower), the tone nudged by `bright`, the resonance a little lower for a big `caliber` and a long gun, how sharp the crack is, a mechanism click for guns that cycle a bolt or slide (timed by `rpm`), how much of the synthesised shot is blended in, and how dry and short the body is (from `tail`). The log lists every weapon's identity as a `[gunvoice]` line. Barrel and muzzle attachments colour it a little more (a long or heavy barrel lower, a short barrel or brake higher and louder).

To pin any of it, add an optional `"voice"` block (every key optional; the rest stays derived):

```json
"voice": { "recording": "Walther PPQ", "secondRecording": "Bersa", "pitch": 1.04, "tone": -0.3, "body": 0.6, "bodyHz": 420, "crack": 0.7, "click": 0.5, "tail": 0.9 }
```

| Key | Meaning | Allowed |
| --- | --- | --- |
| `seed` | Replaces the name hash: a different whole number gives a different-sounding gun. | any whole number |
| `recording` / `secondRecording` | The main and second recorded firearm, by the `gun` names in `Audio/Samples/Guns/manifest.json` (the source folder, e.g. `"Walther PPQ"`, `"AK-47"`, `"Mosin Nagant"`). A name that isn't in the weapon's set is ignored. | text |
| `pitch` | Extra pitch on top of the caliber's. | 0.85-1.15 |
| `tone` | Tilt: -1 dark, +1 bright. | -1 to 1 |
| `body` | Body resonance amount (the receiver / barrel ring). | 0-1 |
| `bodyHz` | Body resonance frequency. | 80-1500 |
| `crack` | How sharp the transient is. | 0-1 |
| `click` | Mechanism click level (0 = none). | 0-1 |
| `tail` | Body length and dryness (lower = drier, shorter). | 0.5-1.5 |

Some numbers to aim for: a typical rifle kills in 4-5 body shots (`damage` 22-30), an SMG fires 650-1150 rpm with 11-18 m of range, and a sniper does 70-140 damage. `--check-weapon` prints DPS and shots to kill so you can compare with the shipped guns.

## Launchers

A weapon of class `Launcher` fires a real projectile instead of a hitscan bullet (Source/Game/Launchers): a rocket, a grenade or a sticky charge that flies, drops and goes off with a blast. Launchers are secondaries, carry a few spare rounds instead of endless reloads, only take attachments made for launchers (an attachment's `fits` must name `"Launcher"` in `classes`, or the weapon in `weapons`), and sit out Gun Game. The shipped ones are in `Weapons/Launchers/`.

For a launcher some of the usual fields mean something a little different: `damage` is the blast's damage at its centre against props, buildings, structures and kill streak machines (it falls off to nothing at `radius`; players take it times `playerMult`, over `playerRadius`), `rangeM` is the range bots and the stat bars use, `hipSpreadDeg` / `adsSpreadMult` are the launch cone, `headMult` and `pellets` aren't used. Everything else lives in a `launcher` block (every key optional; the defaults are a dumb-fire rocket):

```
"launcher": {
  "projectile": "Rocket", "guidance": "None", "speed": 22, "maxSpeed": 46, "thrust": 40, "gravity": 0.15,
  "drag": 0, "radius": 6.5, "falloff": 1.1, "direct": 60, "vehicleMult": 1, "playerMult": 1.35, "playerRadius": 8.5,
  "selfDamage": 0.6, "armDistance": 8, "proximity": 0, "fuse": 7, "bounces": 0, "reserve": 2, "lockTime": 1.2,
  "lockRange": 160, "lockCone": 9, "turnRate": 90, "cluster": 0, "wallPierce": 0
}
```

| Key | Meaning | Default | Allowed |
| --- | --- | --- | --- |
| `projectile` | `Rocket` (a motor, nearly flat, goes off on contact), `Grenade` (an arc, goes off on contact, after its bounces or at its fuse) or `Sticky` (sticks to the map or a player; its owner aims and pulls the trigger to set them all off). | `Rocket` | |
| `guidance` | `None` (dumb fire), `LockOn` (aim at an enemy's flying kill streak until it locks, then it homes; ground streaks never lock) or `Wire` (it turns towards wherever its owner looks while they hold the launcher). | `None` | |
| `speed` | Launch speed, m/s. | `30` | 1-300 |
| `maxSpeed` | Rockets: the motor pushes it up to this. Must be at least `speed` when there is `thrust`. | `60` | 1-300 |
| `thrust` | Rockets: motor push, m/s per second (0 = no motor). | `60` | 0-1000 |
| `gravity` | Fraction of the world's gravity (rockets 0.05-0.2, grenades about 1). | `0.1` | 0-3 |
| `drag` | Fraction of the speed lost per second. | `0` | 0-5 |
| `radius` | Blast radius, metres. | `5` | 0.5-20 |
| `falloff` | Damage is `damage x (1 - distance / radius) ^ falloff`. | `1.2` | 0.3-4 |
| `direct` | Extra damage to a player hit straight on. | `50` | 0-1000 |
| `vehicleMult` | Multiplier on everything it does to kill streak machines (before their own explosive multiplier: the sentry takes 1.5x). | `1` | 0-10 |
| `playerMult` | Multiplier on what the blast and an armed direct hit do to players (the owner's own share too). Objects and machines keep `damage`. A dud's `direct` bonk isn't multiplied. | `1` | 0-5 |
| `playerRadius` | Blast radius against players, and for the push, the flash and ragdolls (0 = `radius`). | `0` | 0-25 |
| `selfDamage` | Share of the blast (against players) its owner takes. | `0.6` | 0-1 |
| `armDistance` | Metres of flight before it arms; an unarmed hit is a dud (only `direct` damage, no blast). | `6` | 0-60 |
| `proximity` | Goes off this close to an enemy's flying streak (0 = no proximity fuse). | `0` | 0-12 |
| `fuse` | Seconds before it goes off on its own (rockets self-destruct, stickies wait at most this long). | `8` | 0.1-40 |
| `bounces` | Grenades: bounces before contact sets it off. | `0` | 0-10 |
| `reserve` | Spare rounds per life on top of the magazine. | `3` | 0-30 |
| `lockTime`, `lockRange`, `lockCone` | Lock-on: seconds to lock while aiming, furthest target (m), cone half-angle (degrees). | `1.2`, `160`, `9` | 0.1-10, 5-600, 1-45 |
| `turnRate` | Guided: degrees per second it can turn. | `90` | 0-720 |
| `cluster` | Bomblets the blast throws out (each 45% of the radius, 40% of the damage). | `0` | 0-12 |
| `wallPierce` | Thermobaric: share of the blast that gets round walls. | `0` | 0-1 |

Attachments change these by their field names with a `launcher` prefix, e.g. `{ "stat": "launcherLockTime", "mul": 0.7 }` or `{ "stat": "launcherReserve", "add": 1 }`. Kitted launchers keep at least 70% of their lock time, launch speeds within 70-140% and at most double the reserve.

| Shipped launcher | Type | Damage / radius / direct (objects, machines) | Players: x playerMult / playerRadius | Rounds | Unlock |
| --- | --- | --- | --- | --- | --- |
| Mule RPG | dumb-fire rocket | 150 / 6.5 m / +60 | x1.35 / 8.5 m | 1 + 2 | 6 |
| Clover Break-Action | single-shot grenade launcher | 130 / 5 m / +40 | x1.35 / 6.6 m | 1 + 5 | 9 |
| Carousel Drum Launcher | 6-round revolving grenade launcher | 100 / 4.2 m / +30 | x1.3 / 5.5 m | 6 + 6 | 14 |
| Skylance AA | lock-on anti-air rocket | 110 / 3.5 m / +140 | x1.3 / 4.6 m | 1 + 3 | 18 |
| Gluepot Sticky Launcher | sticky charges | 125 / 4.5 m / - | x1.35 / 6 m | 3 + 3 | 22 |
| Wireline TOW | wire-guided rocket | 140 / 5 m / +80 | x1.35 / 6.6 m | 1 + 2 | 27 |
| Hailcrack Cluster | cluster rocket (6 bomblets) | 90 / 4 m / +40 | x1.4 / 5.3 m | 1 + 2 | 33 |
| Furnace Thermobaric | thermobaric rocket | 120 / 8 m / +40 | x1.3 / 10.5 m | 1 + 1 | 39 |

## Ids and order

Weapons are sorted by `order`, then by name. A weapon's position in that list is its id, which the network uses. The shipped weapons use `order` 1-50 (pistols 1-9, SMGs 10-16, rifles 17-24, marksman 25-30, snipers 31-35, shotguns 36-41, LMGs 42-46, special 47-50) and the launchers 101-108. Give new weapons 110 or more, or leave `order` out (it defaults to 1000), so they're listed after the shipped ones.

Your progress doesn't depend on ids: weapon XP in `progress.txt` (`wx.<weapon name>=...`) and loadouts in `loadout.txt` are stored by **name**. Renaming a weapon starts its weapon XP from zero, and removing a weapon file keeps its XP in the save in case it comes back.

## Unlocking

Each account level unlocks one item. The shipped weapons unlock in a hand-tuned order, built into the game and matched by name. Any other weapon unlocks at its `unlockLevel` (1 = available from the start), or at the max level if it has none. Levels above the max count as the max. Setting `unlockLevel` on a shipped weapon overrides its built-in level.

## Attachments

Which attachments a weapon takes is decided by each attachment's file (its `fits` rules), mostly by weapon `class`. For example, scopes fit long guns, the Choke fits shotguns and stocks fit everything but pistols and specials. A new weapon gets the attachments of its class automatically. `--check-weapon` lists them. Weapon XP needed for each attachment is worked out from how many fit (3-7 matches to unlock everything), the same as for the shipped guns.

## Multiplayer: everyone needs the same files

Weapon stats are part of the game rules. The host simulates every shot, and clients predict their own with the same numbers. So **everyone in a game must have identical `Weapons` and `Attachments` folders**. When you join, the host sends a fingerprint of its weapon set. If yours is different, you're told which folder differs and disconnected. Copy the host's folders over yours and rejoin. Spacing, line endings and comments don't count, only the values.

## Command-line helpers

- `CallOfSlop.exe --check-weapon FILE`: check one weapon file (or attachment file) and print its key numbers.
- `CallOfSlop.exe --export-weapons DIR`: write every loaded weapon and attachment back out in the standard layout. This is handy for tidying hand-edited files or as a starting point.

## If the folder is missing

The game still starts: it has one built-in weapon per slot (the P9 Sidearm and the BR-1 Prototype) and uses them for any slot with no weapon files.
