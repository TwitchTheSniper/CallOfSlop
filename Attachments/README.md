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
| `pros`, `cons` | Short lines shown in green and red in the gunsmith. | `[]` |
| `fits` | Which weapons take it (below). | fits everything |
| `effects` | What it changes (below). | no change |

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

- **The housing fades away as you aim** (fully gone at 85% aimed), so nothing blocks the view. Reflex sights (Pinhole, Red Dot, Holographic) keep their thin window frame; magnified optics show a round eyepiece rim with a faint lens tint and a soft vignette at the edge.
- **The reticle is drawn flat in the HUD**, crisp at any resolution, exactly at the screen centre where the sight line is.
- **Only 4x and up get the full scope view** (an `adsZoom` of 4 or more): a round lens with a soft edge and black around it, which fills ultra-wide screens properly. Below 4x there are no side bars and no dimming.
- Fitting any optic removes the parts it would clash with: pistol iron sights and LMG carry handles. The rail stays.

A new optic file can reuse any of the looks above with its own stats. A brand-new look (model and reticle) needs code: a value in `OpticKind`, its sight height and eye distance in `GunModel.SightHeight`/`AdsZ`, its model in `GunModel.DrawOptic`, and its lens and reticle in `GunModel.Look`.

## Unlocking and balance

Attachments unlock per weapon with weapon XP. Each gun's fitting attachments, in `unlockOrder`, are spread along a curve. The total depends on how many fit (`2000 + 220 x count`, rounded to 50), so adding an attachment to a class makes those guns take a little longer to max out.

## Multiplayer

As with weapons, everyone in a game needs identical `Attachments` (and `Weapons`) folders. A client with a different set is told which folder differs when it joins.
