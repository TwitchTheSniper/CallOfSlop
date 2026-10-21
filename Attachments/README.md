# Making attachments for Call of Slop

Every attachment is a single `.json` file in this folder. The 27 that ship with the game are sorted into one subfolder per slot (`Optic/`, `Muzzle/`, `Barrel/`, `Underbarrel/`, `Magazine/`, `Stock/`), but the game reads every `*.json` under `Attachments/`, however deep. Drop a file in and restart the game.

An attachment says which slot it goes in, which weapons it fits, and a list of stat changes ("effects") it makes to the weapon's numbers. The weapon numbers are the fields described in [Weapons/README.md](../Weapons/README.md).

## The quick version

```json
{
  "format": "call-of-slop-attachment/1",
  "name": "Oil Can Suppressor",
  "author": "YourName",
  "slot": "Muzzle",
  "order": 100,
  "unlockOrder": 100,
  "blurb": "Homemade and very quiet. Not very durable, not very accurate.",
  "pros": ["Quiet, muffled gunshot"],
  "cons": ["-25% range", "+15% hip spread"],
  "fits": { "classes": ["SMG", "Pistol"], "sounds": ["Gun"] },
  "effects": [
    { "stat": "suppressor", "set": true },
    { "stat": "sound", "set": "Suppressed" },
    { "stat": "rangeM", "mul": 0.75 },
    { "stat": "hipSpreadDeg", "mul": 1.15 }
  ]
}
```

Check it with `CallOfSlop.exe --check-weapon Attachments\Muzzle\OilCan.json`. That prints which weapons it fits, or the problem.

## Fields

| Field | Meaning | Default |
| --- | --- | --- |
| `format` | Always `"call-of-slop-attachment/1"`. | |
| `name` | Shown in the gunsmith and saved in `loadout.txt`. Unique, 1-40 characters, no `=` or `,`. | required |
| `slot` | `Optic`, `Muzzle`, `Barrel`, `Underbarrel`, `Magazine` or `Stock`. | required |
| `order` | Position inside its slot (lower first, ties by name). | `1000` |
| `unlockOrder` | Where it comes in each weapon's unlock sequence. Lower unlocks earlier. The shipped ones use 1-23. | `1000` |
| `author` | Your name. Optional. | `""` |
| `blurb` | One line describing it. | `""` |
| `pros`, `cons` | Short lines shown in green and red in the gunsmith, no full stop at the end (e.g. `"+45% range"`). Like `name` and `blurb`, these are plain ASCII only: the game's font has no curly quotes, long dashes, accented letters or symbols like × and °, which show up as `?`. Write `'`, `-`, `x` and `deg` instead. `%` is fine. | `[]` |
| `fits` | Which weapons take it (below). | fits everything |
| `effects` | What it changes (below). | no change |
| `sight` | Optics only: how it looks when aiming (see [Sight block](#sight-block)). | the default for its `optic` look |

**At most 15 attachments per slot.** A loadout packs each slot into 4 bits (0 = empty), so a 16th attachment in a slot is skipped with a message. In multiplayer, kits travel as these indices, so **never change the `order` of a shipped attachment**: add new ones after the existing ones (saves store names, so they don't care).

## Fits

Every rule you give must pass. Leave `fits` out (or `{}`) to fit everything. Rules are checked against the weapon's own stats, before any attachments are applied.

| Rule | Passes when |
| --- | --- |
| `classes` | the weapon's class is in the list, e.g. `["AssaultRifle", "LMG"]` |
| `notClasses` | the weapon's class is not in the list |
| `sounds` / `notSounds` | the weapon's `sound` is (or isn't) in the list, e.g. `["Gun", "Boom"]` |
| `minMag` / `maxMag` | the magazine is at least / at most this big |
| `weapons` / `notWeapons` | the weapon's name is (or isn't) in the list |

## Effects

Each effect changes one weapon stat. Effects run in order, and attachments apply slot by slot (Optic first, Stock last).

| Effect | Does | Example |
| --- | --- | --- |
| `set` | replaces the value | `{ "stat": "adsZoom", "set": 2.5 }` |
| `add` | adds (negative to subtract) | `{ "stat": "mobility", "add": -0.04 }` |
| `mul` | multiplies (whole numbers are rounded) | `{ "stat": "recoilDeg", "mul": 0.8 }` |
| `grow` | for whole numbers like `mag`: multiplies and rounds, but always adds at least 1 | `{ "stat": "mag", "grow": 1.5 }` |

Add `"min"` and/or `"max"` to `add`, `mul` or `grow` to clamp the result, e.g. `{ "stat": "adsTime", "add": -0.02, "min": 0.08 }`.

Any weapon number can be changed, plus these extras that only attachments use:

- `optic`: the sight drawn when aiming: `Iron`, `RedDot`, `MiniDot`, `Holo`, `Prism2`, `Acog`, `Hybrid3`, `Scope4`, `Lpvo6`, `Scope8` or `Scope12`. It picks the model, the reticle and the lens (see [Optics](#optics)). Pair it with an `adsZoom`.
- `suppressor`: `true` draws a suppressor, makes the gunshot much quieter and dims the muzzle flash and tracers. Pair it with `"sound": "Suppressed"`.

Recoil fields from a weapon's `recoil` block are changed by their names with a `recoil` prefix: `recoilVertical`, `recoilHorizontal`, `recoilBias`, `recoilJitter`, `recoilFirstShot`, `recoilRecovery`, `recoilRecoveryDelay`, `recoilShake`, `recoilPunch`, `recoilKickUp` and so on (every field in [Weapons/README.md](../Weapons/README.md#recoil)). `recoilDeg` scales all of the gun's recoil at once. The shipped attachments use them like this:

| Attachment | Recoil effect |
| --- | --- |
| Compensator | -30% vertical kick, -20% view punch |
| Muzzle Brake | -30% sideways kick, -40% camera shake |
| Suppressor | -25% camera shake |
| Vertical Grip | -30% vertical kick, steadier viewmodel |
| Angled Grip | -40% first-shot kick |
| Heavy Stock / Heavy Barrel | -20% / -15% overall recoil; the Heavy Stock also recovers 40% faster |
| Light Stock / No Stock | +10% / +25% overall recoil; No Stock also recovers 20% slower |

You can't change `name`, `class` or `blurb`. After all attachments are applied, `mobility` is clamped to 0.6-1.25 and `adsTime` to 0.08-0.5, and however much is stacked a gun keeps at least half its `hipSpreadDeg` and 40% of its recoil (vertical and sideways).

The gun model shows the shipped attachments by name (a "Drum Mag" draws a drum, for example). A new attachment changes the stats but is drawn as the plain gun, apart from `optic` and `suppressor`.

## Optics

The ten shipped optics (index = `order`, which the network uses; never renumber them):

| # | Name | Look (`optic`) | Zoom | Aimed spread | Aim time | Speed | Reticle | Fits | Unlock |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Red Dot | `RedDot` | 1.5x | -15% | | | red dot | everything | 1 |
| 2 | Holographic | `Holo` | 1.6x | -22% | +0.02 s | | cyan ring + dot | not pistols | 6 |
| 3 | 2.5x ACOG | `Acog` | 2.5x | -35% | +0.05 s | | red chevron + drop line | not pistols, shotguns, specials | 12 |
| 4 | 4x Scope | `Scope4` | 4x | -52% | +0.10 s | -3% | duplex crosshair | AR, LMG, DMR, sniper | 17 |
| 5 | 8x Sniper Scope | `Scope8` | 8x | -65% | +0.15 s | -5% | fine crosshair + dot | DMR, sniper | 21 |
| 6 | Pinhole Reflex | `MiniDot` | 1.4x | -12% | -0.02 s | | tiny green dot | everything | 3 |
| 7 | Brick 2x Prism | `Prism2` | 2x | -28% | +0.035 s | | horseshoe + dot | not pistols | 9 |
| 8 | Tandem 3x Hybrid | `Hybrid3` | 3x | -42% | +0.07 s | -1% | circle-dot + drop ticks | not pistols, shotguns, specials | 14 |
| 9 | Variant 6x LPVO | `Lpvo6` | 6x | -58% | +0.12 s | -4% | stadia crosshair + lit dot | AR, LMG, DMR | 19 |
| 10 | Longshot 12x | `Scope12` | 12x | -70% | +0.18 s | -6% | mil-dot crosshair | sniper | 23 |

How they look when aiming:

- **Every optic is a physical sight on the gun.** As you aim, the gun comes up and the sight moves to your eye; its whole housing stays on the gun the whole way, and you look through its lens. Nothing changes into a flat overlay.
- **Higher magnification fills more of the screen.** Reflex sights (Pinhole, Red Dot, Holographic) stay a small window on top of the gun, exactly as before. The 2x-3x prisms fill a good part of the screen (25-40% at 16:9), the 4x about two thirds, the 6x three quarters, and the 8x and 12x all of it but a thin eyepiece rim in the corners (95% and 99%). That comes from how close each optic's lens sits to the eye at full aim (its `screen` or `eyeDistance`, below), so the lens grows smoothly as you raise it.
- **A clear sight picture.** Inside the lens you see only the world (as it is behind the lens on screen) and the reticle, with a faint glass tint and, on magnified optics, a soft vignette and a dark metal eyepiece rim around it. The rail, receiver, barrel and the inside of the tube never show through the glass. A magnified lens is dark glass at the hip and clears as you aim; a reflex window starts clear and hides the gun behind it as you aim.
- **Reticles are crisp at any resolution** (drawn per pixel) and sit where the optic points: the middle of the screen when you're steady, and they climb with the gun's recoil. On magnified optics they grow with the lens as it comes up and reach their full size at full aim; crosshair lines reach the lens edge.
- **One field of view.** The whole screen zooms to the optic's `adsZoom` (no picture-in-picture view with a less zoomed world around the lens): a second view of the world would cost a second full world pass every frame, about as much as the world itself. At 4x and up the lens fills most of the screen anyway.
- High-power lenses are steadied: they sit a couple of centimetres from the eye, so only part of the gun's sway, bob and kick moves them (the reticle still climbs with recoil).
- Fitting any optic removes the parts it would clash with: pistol iron sights and LMG carry handles. The rail stays.
- Guns with a built-in scope (long-range rifles without an optic attachment) work the same way: their lens size follows their `adsZoom` (about 0.8 of half the screen height at 2x, 1.3 at 4x).

### Sight block

An optic file can say how it looks with a `"sight"` block. Every key is optional: missing ones come from the defaults for the file's `optic` look and `adsZoom` (so a file without a block looks like the shipped optic of that look). The shipped optics all have one:

```
"sight": { "reticle": "FineCross", "reticleColor": [1, 0.15, 0.1], "screen": 1.8, "housing": 1.2, "vignette": 0.35, "tint": [0.65, 0.82, 1], "tintAlpha": 0.05 }
```

| Key | Meaning | Range |
| --- | --- | --- |
| `reticle` | `Dot`, `HoloRing`, `Chevron`, `Horseshoe`, `CircleDot`, `Duplex`, `FineCross` (with a centre dot), `Stadia` (with range ticks) or `MilDot`. | |
| `reticleColor` | Colour of the lit parts (dots, rings, chevrons): `[r, g, b]` with values 0-1, or `"#rrggbb"`. Etched lines are always black. | 0-1 |
| `reticleSize` | Scale of the reticle; 1 = its design size on a 1080p screen. | 0.25-4 |
| `screen` | The lens radius at full aim, as a fraction of half the screen height: 1 touches the top and bottom of the screen; about 1.75 or more covers 90% of a 16:9 screen. The optic is brought that close to the eye. Leave it out on reflex sights to keep their window as it is. | 0.1-2.5 |
| `eyeDistance` | Metres from the eye to the lens at full aim, instead of `screen`. | 0.012-0.4 |
| `lensRadius` | The lens radius in metres; leave it out to use the model's eyepiece. | 0.005-0.05 |
| `housing` | The eyepiece rim's outer radius as a multiple of the lens radius (round lenses). | 1-2 |
| `vignette` | How much the lens darkens towards its edge. | 0-1 |
| `tint`, `tintAlpha` | The glass tint over the view, and how strong it is. | 0-1, 0-0.5 |

The defaults per look: Red Dot, Pinhole and Holographic keep their windows; Brick 2x Prism `screen` 0.8, 2.5x ACOG 0.88, Tandem 3x Hybrid 0.95, 4x Scope 1.3, Variant 6x LPVO 1.5, 8x Sniper Scope 1.8, Longshot 12x 1.95. The sight block is only how it looks: it never changes where bullets go.

A new optic file can reuse any of the looks above with its own stats and sight. A brand-new look (model) needs code: a value in `OpticKind`, its sight height in `GunModel.SightHeight`, its model in `GunModel.DrawOptic`, its lens (eyepiece or window) in `GunModel.Lens`, and its default sight in `Sights.Default`.

## Unlocking and balance

Attachments unlock per weapon with weapon XP. Each gun's fitting attachments, in `unlockOrder`, are spread along a curve. The total depends on how many fit (`2000 + 220 x count`, rounded to 50), so adding an attachment to a class makes those guns take a little longer to max out.

## Multiplayer

As with weapons, everyone in a game needs identical `Attachments` (and `Weapons`) folders. A client with a different set is told which folder differs when it joins.
