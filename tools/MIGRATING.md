## Moving your progression to this build

Older builds of Call of Slop kept your saves **inside the game folder**, next to `CallOfSlop.exe`. From this release on, your saves live in one fixed place that every new build uses:

```
%APPDATA%\CallOfSlop
```

(Paste that into File Explorer's address bar, or in game: **Settings > Player > Open save folder**.)

Once your saves are there, every future update keeps them automatically. You only do this once.

### Your save files

| File | What it holds |
| --- | --- |
| `progress.txt` | Your level, XP, prestige and unlocks |
| `loadout.txt` | Your loadouts |
| `settings.txt` | Your settings and keybinds |
| `workbench.txt` | Your workbench weapon builds |
| `ordnance.txt` | Your saved bomb and missile designs (the Ordnance Bench) |
| `blueprints.txt` | Your workbench blueprint library (newer builds; the first time it opens it copies in the builds from `workbench.txt`) |
| `audio_mix.json` and the `AudioMixes` folder | Your audio mixer levels and presets |
| The `ConstructBlueprints` folder | Blueprints you saved at a Blueprint Desk in Sandbox |
| `appearance.txt` | Your soldier's look and hat (newer builds; older ones kept the hat in `loadout.txt`) |
| The `Worlds` folder | Your saved Sandbox worlds, one folder a world (newer builds) |

The `Cache` folder in the save folder is not a save: it holds maps the game built itself (`Cache/Maps`, so the next start is quick) and bot route maps it worked out (`Cache/Nav`), for maps it had no ready-made ones for. Don't copy it; it's safe to delete, and the game rebuilds what it needs. The `DevEdits` folder (change packages from the dev prop editor) isn't a save either: attach the packages to reports, and copy them over only if you want to keep them.

### The easy way (automatic)

1. Unzip the new build **next to your old one**, in the same parent folder. For example:
   ```
   Games\CallOfSlop-old\
   Games\CallOfSlop-0.2026.1023.1\
   ```
2. Start the new `CallOfSlop.exe`.
3. On its first run, the game finds the old build beside it and copies your saves into `%APPDATA%\CallOfSlop`. If you have more than one old build there, it uses the one you played most recently.
4. Check your level in the main menu. If it's right, you're done.

Nothing is moved or deleted. Your old build's files are left exactly as they were.

### The manual way

Use this if the automatic copy didn't find your old build, for example because it was in a different folder or drive.

1. **Close the game** if it's running.
2. Open your **old** build's folder: the one with the old `CallOfSlop.exe`.
3. Copy these files and folders, whichever of them you have:
   - `progress.txt`
   - `loadout.txt`
   - `settings.txt`
   - `workbench.txt`
   - `blueprints.txt` (if you have it)
   - `audio_mix.json`
   - `appearance.txt` (if you have it)
   - the `AudioMixes` folder
   - the `Worlds` folder (if you have it)
   - the `ConstructBlueprints` folder
4. Paste them into `%APPDATA%\CallOfSlop`. Create the folder if it isn't there. If Windows asks, replace the files that are already there.
5. Start the new build and check your level in the main menu.

> Do this before playing much on the new build. Whatever you copy in replaces the new build's progress.

Not save files (you don't need to copy them): `update_check.txt` (when the game last asked GitHub for updates) and `update.log` (what the updater removed). Both live in the save folder and are safe to delete. The `Logs` folder isn't a save either: it holds the stuck-spot reports (`stuck-spots-*.json`, where bots got stuck in your matches). Send it to the developer if bots got stuck a lot; it's safe to delete.

### Old versions

Each update unzips into its own folder (`CallOfSlop-<version>`) with a small `.callofslop-build` marker in it. Once the new build reaches its main menu it removes older versions **that the updater installed**, keeping the previous one for rollback (**Settings > Player > Updates** to change that, or **Clean up old versions now** to see the list first). Folders you unzipped yourself, folders with `portable.txt` or save files in them, and anything else are never deleted. If you kept saves in an old portable build, copy them out before you delete that folder.

### The new default audio mix

The game's built-in sound levels are now the **SeabMix2** mix: your own gun, other players' guns, explosions, bullet whizz and the hit sounds are louder, footsteps a little quieter (README, "Audio mixer", lists every level). If you never saved a mix, you get it straight away. If you have an `audio_mix.json` (or presets in `AudioMixes`), every level you changed in the F8 mixer stays as you set it, and every level you left at its old default moves to the new one the first time the file loads; press **Save** in the mixer to keep the result. To start over from the new defaults, use **Revert to defaults** and Save, or delete `audio_mix.json`.

### Map packs removed (2026-11-12)

The **Legacy**, **Cluttered**, **World Tour** and **Space** map packs were removed: the game is Sandbox now. **Proto Box** and **Pocket Planet** moved to the **Dev** pack. A `settings.txt` that still picks random maps from a removed pack picks from **Sandbox** instead; one that names a removed map plays Proto Box. Co-op had its only mission on the World Tour map Silo, so it has none now. If you unzip a new build over an old one, delete `Maps\Legacy`, `Maps\Cluttered`, `Maps\World` and `Maps\Space` from the game folder.

### The Legacy map pack

The **Classic**, **Collision Point** and **High Ground** map packs are now one pack called **Legacy** (`Maps/Legacy`, 36 maps, same maps and same names: Proto Box to Military Base, then Blueprint to Confluence, then Gravel Pit to Alpine Pass). A `settings.txt` that still picks random maps from one of the old packs switches to Legacy on its own the first time it loads. If you unzipped the new build **over** an old one, the leftover `Maps\Classic`, `Maps\CollisionPoint` and `Maps\HighGround` folders are harmless: the game sees that their maps are the same as Legacy's and ignores them (`callofslop.log` says so), but you can delete them. A map you edited in one of those folders still loads, as its own pack. Online, these maps are the same maps as before: hosts and joiners on either layout can play together.

### Workbench builds on removed receivers

Three weapon-builder receivers that were only a fire rate are gone: the **Ratchet Burst SMG**, **Triad Burst** and **Buzzbox Machine Pistol** receivers (the burst or buzzsaw is the Action part now). A saved build in `workbench.txt` that uses one loads on the **Stampede**, **Workhorse** or **Plinker** receiver with the **Ratchet Burst Group**, **Three-Round Burst Group** or **Buzzsaw Sear** action, so it fires as before, and the file is saved back that way. Nothing else in your builds changes; receiver damage numbers did change with the late-game power curve (docs/sandbox/WEAPON-BUILDER.md, "The power curve").

### Workbench blueprints

The workbench now has a **blueprint library** (`blueprints.txt` in your save folder): any number of saved guns, melee weapons and grenades. The first time you open the workbench on this build, the builds already in your `workbench.txt` slots (and your melee and grenade) are copied into the library, once. Your slots in `workbench.txt` stay as they were.

### Cut-off Saws in a saved world

The Cut-off Saws are gone: an Extruder now cuts by itself. A saved world that has one loads without it, and `callofslop.log` says how many were left out. Wire what fed the saw into the Extruder's **Cut** input, or set its **Cut every**.

### Assemblers in a saved world (2026-11-13)

Seventy-three parts a Press makes now (the Gear Set, brackets, plates, housings, tanks, the Frame Beam and others) left the Assembler's recipe list. An Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again. A blueprint whose Assembler names one of those parts still loads, with its recipe set to None.

### Panel Formers and wide panels in a saved world (2026-11-13)

A Panel Former no longer splits a panel wider than your Extruders make into strips by itself. An order for one now waits and says which bigger Extruder, or Die Frames on the one you have, would make it. To keep the old behaviour, open the Panel Former with the configure tool and turn on **Too wide: make strips** (in its Logistics settings).
### Engine cylinders in a saved world (sized parts, 2026-11-13)

Fifteen more rows left the Assembler's recipe list: the **Small, Medium and Large Cylinder** (a Press with a Cylinder Die makes them now) and the twelve **Bored, Performance and Racing Cylinders** (a bigger bore replaces them: they can't be made any more). As before, an Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again. Tier cylinders you already have, and the vehicles that carry them, keep working.

A cylinder pressed before this version still loads, but it is now shaped and rated like a real cylinder of its size: its power comes from its bore and its revs from its length, so an engine built with one may give less (or more) power than before, and the model looks different. Look at the engine's readout after loading. A part or a made piece this version can't build is left out with a line in `callofslop.log`.

### Barrels, engine pieces and presses in a saved world (sized parts, 2026-11-13)

Forty-one more rows left the Assembler's recipe list: the **Gun Barrel, Long Gun Barrel, Flame Nozzle, Harpoon Tube, Howitzer Tube and Long Gun Tube** (a Press with a Barrel Die makes them) and every **crank section, flywheel, carburettor, injector rail, turbo, supercharger, starter motor, exhaust outlet and smokestack** except the Capital Smokestack (a Press with an Engine Die makes them). As before, an Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again.

Pressed pieces in a saved world keep their key and load as the same piece: same size, numbers and mass (their models settle by a few millimetres so no two faces flicker). A Press's Pattern setting keeps its meaning, and a Press set to Auto makes what it made before: the receivers a Receiver Die can now make (22 that had no recipe, the Bucket Launcher and Courier Carbine among them) come only from its Pattern setting or an order. Recycling a part a Press makes from a preset (a Gear Set, a Housing, a Gun Barrel and others) now gives back at most what pressing it took: less than before for 35 of them.

### Pressed engine pieces in a saved world (bored cylinders, 2026-11-14)

Pressed engine pieces (cylinders, crank sections, flywheels, fuel systems, boosters, starters, outlets) keep their key and load as the same piece, the same size, shape and health, but they now weigh what the catalogue piece of their size weighs for their metal: most are a third to a half of their old mass, so a vehicle with a pressed engine gets lighter, and **recycling one gives back its new, lower mass**. A pressed cylinder's power now also follows its **bore** (its width less both walls): a cylinder with the usual wall for its width gives what it gave before; a thinner wall gives more, a thicker one less. Its revs are unchanged. The Engine Kit's old bored cylinders: `pipe10 50x50x85` now 56 kW (was 50), `pipe10 55x55x95` 74 kW (was 62), `pipe15 60x60x105` 51 kW (was 74, its wall is thick). Their old pressed fuel systems now feed most but not all of the extra (an old Small B16 Bored engine reads 981 kW, was 962; a Performance one 1222 kW, was 1180; a racing one less than before). Look at the engine's readout after loading. Catalogue engine pieces and pieces pressed at a catalogue preset are unchanged. Nothing is dropped.

### Long and short strokes in a saved world (stroke power, 2026-11-14)

A pressed cylinder's **stroke** (its length against its width) now adds power as well as lowering revs: a cylinder 1.6 times the usual length gives 1.6 times the power at 0.625 of the revs, so 2.56 times the torque; a short one gives less. Every piece keeps its key, size, mass and health. Catalogue cylinders, pressed cylinders of the usual length, and every engine the Engine Kit made before this version (all of the usual length) are unchanged. An engine you built with long-stroke cylinders and the fuel system it had will read **the same power as before** (its revs are still the lower ones), with "bank 1 starved: ... feeds 62.5% of its cylinders": a fuel system feeds by power, so press a wider carburettor or injector for that bank to get the extra (a Small one about 55 cm across for a 1.6 stroke; then six Small cylinders of 1.2 m give 384 kW where they gave 240). A short-stroke engine gives less power than before at the same revs. An engine with long strokes on a crank a little smaller than the first size that needs a starter (from about 0.85 of it) may now ask for a starter motor. Look at the engine's readout after loading.

### Keeping saves on a USB stick (portable mode)

Put an empty file called `portable.txt` next to `CallOfSlop.exe`. The game then keeps its saves in its own folder, like the old builds did. Copy your save files into that folder yourself.

### If something went wrong

- **Level is back to 1:**
  1. Close the game.
  2. Do the manual copy above.
  3. Check that you copied from the old build's own folder: the one that has `CallOfSlop.exe` in it.
- **A save got damaged:** every save keeps its previous version as `NAME.bak`, for example `progress.txt.bak`, in the same folder. Close the game, delete the damaged file, rename the `.bak` file back to the original name, and start the game again.
- **Still stuck:** send `callofslop.log` from the game folder. Its `[saves]` lines say which folder the game used and what it copied.
- **The game crashed:** send `callofslop.log`, `crash.txt`, `breadcrumb.txt` and the newest `crash-NNNN.dmp` from the game folder (on a Mac: `~/Library/Logs/CallOfSlop`). They are logs, not saves: you don't need to copy them to a new build, and they are safe to delete.
