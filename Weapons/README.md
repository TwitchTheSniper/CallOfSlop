# Making weapons for Call of Slop

Every weapon is a single `.json` file in this folder. The 50 weapons that ship with the game are ordinary weapon files too, sorted into one subfolder per class (`Pistols/`, `SMGs/`, `AssaultRifles/`, ...). Open any of them to see a real one. Subfolders are only for tidiness: the game reads every `*.json` under `Weapons/`, however deep.

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
| `class` | `Pistol`, `SMG`, `AssaultRifle`, `DMR`, `Sniper`, `Shotgun`, `LMG` or `Special`. Pistols go in the **secondary** slot, everything else in the **primary**. The class also picks the gun's model shape, the reload sound and which attachments fit. | required | |
| `blurb` | One line shown in the loadout menu. | `""` | |

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
| `recoilDeg` | View kick per shot, in degrees. | `0.7` | 0.1-5.5 | 0-20 |
| `mobility` | Movement speed multiplier while holding it. The server also uses it to cap your speed. Attachments can push it, but kitted guns are clamped to 0.6-1.25. | `1` | 0.75-1.12 | 0.3-1.5 |
| `adsZoom` | Zoom when aiming down sights (the field of view is divided by this). Snipers use 3.5-4.5 for their built-in scope. | `1.35` | 1.1-4.5 | 1-10 |
| `equipTime` | Seconds to draw the weapon. | `0.45` | 0.25-1.1 | 0.05-5 |
| `adsTime` | Seconds to fully aim down sights (kitted guns are clamped to 0.08-0.5). | `0.16` | 0.16 | 0.05-2 |

### Look

The model is built procedurally from the class and these numbers (plus a few name hints: a name containing "Rotary", "Double", "Lever" or "Musket" gets those details).

| Field | Meaning | Default | Usual | Allowed |
| --- | --- | --- | --- | --- |
| `length` | Overall length in metres. | `0.42` | 0.15-0.86 | 0.1-1.5 |
| `bulk` | Thickness multiplier for the receiver and furniture. | `1` | 0.75-1.7 | 0.3-3 |
| `accent` | Accent colour: `[r, g, b]` with values 0-1, or `"#rrggbb"`. | `[0.95, 0.5, 0.15]` | | each 0-1 |

### Sound

The gunshot is synthesised from these when the game starts.

| Field | Meaning | Default | Allowed |
| --- | --- | --- | --- |
| `sound` | Recipe: `Gun`, `Suppressed` (built-in silencer), `Boom` (huge, for .50 cal and muskets), `Zap` (energy), `Toy` or `Pneumatic`. | `Gun` | |
| `caliber` | 0 = small and snappy, 1 = huge and deep. | `0.5` | 0-1 |
| `bright` | High-frequency crack of the blast. | `0.5` | 0-1 |
| `tail` | Length of the echo. | `0.5` | 0-1 |

Some numbers to aim for: a typical rifle kills in 4-5 body shots (`damage` 22-30), an SMG fires 650-1150 rpm with 11-18 m of range, and a sniper does 70-140 damage. `--check-weapon` prints DPS and shots to kill so you can compare with the shipped guns.

## Ids and order

Weapons are sorted by `order`, then by name. A weapon's position in that list is its id, which the network uses. The shipped weapons use `order` 1-50 (pistols 1-9, SMGs 10-16, rifles 17-24, marksman 25-30, snipers 31-35, shotguns 36-41, LMGs 42-46, special 47-50). Give new weapons 100 or more, or leave `order` out (it defaults to 1000), so they're listed after the shipped ones.

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
