## Moving your saves to this build

Older builds of Call of Slop kept your saves **inside the game folder**, next to `CallOfSlop.exe`. From this release on, your saves live in one fixed place that every new build uses:

```
%APPDATA%\CallOfSlop
```

(Paste that into File Explorer's address bar, or in game: **Settings > Player > Open save folder**.)

Once your saves are there, every future update keeps them automatically. You only do this once.

### Your save files

| File | What it holds |
| --- | --- |
| `loadout.txt` | Your loadouts |
| `settings.txt` | Your settings and keybinds |
| `workbench.txt` | Your workbench weapon builds |
| `ordnance.txt` | Your saved bomb and missile designs (the Ordnance Bench) |
| `blueprints.txt` | Your workbench blueprint library (newer builds; the first time it opens it copies in the builds from `workbench.txt`) |
| `audio_mix.json` and the `AudioMixes` folder | Your audio mixer levels and presets |
| The `ConstructBlueprints` folder | Blueprints you saved at a Blueprint Desk in Sandbox |
| `appearance.txt` | Your soldier's look (newer builds) |
| The `Worlds` folder | Your saved Sandbox worlds, one folder a world (newer builds) |

(`progress.txt`, an older build's level, XP and prestige, is no longer read: levelling, prestige and the prestige hats were removed. It can stay or go.)

The `Cache` folder in the save folder is not a save: it holds maps the game built itself (`Cache/Maps`, so the next start is quick) and bot route maps it worked out (`Cache/Nav`), for maps it had no ready-made ones for. Don't copy it; it's safe to delete, and the game rebuilds what it needs. The `DevEdits` folder (change packages from the dev prop editor) isn't a save either: attach the packages to reports, and copy them over only if you want to keep them.

### The easy way (automatic)

1. Unzip the new build **next to your old one**, in the same parent folder. For example:
   ```
   Games\CallOfSlop-old\
   Games\CallOfSlop-0.2026.1023.1\
   ```
2. Start the new `CallOfSlop.exe`.
3. On its first run, the game finds the old build beside it and copies your saves into `%APPDATA%\CallOfSlop`. If you have more than one old build there, it uses the one you played most recently.
4. Check your loadouts in the main menu. If they're right, you're done.

Nothing is moved or deleted. Your old build's files are left exactly as they were.

### The manual way

Use this if the automatic copy didn't find your old build, for example because it was in a different folder or drive.

1. **Close the game** if it's running.
2. Open your **old** build's folder: the one with the old `CallOfSlop.exe`.
3. Copy these files and folders, whichever of them you have:
   - `loadout.txt`
   - `settings.txt`
   - `workbench.txt`
   - `blueprints.txt` (if you have it)
   - `audio_mix.json`
   - `appearance.txt` (if you have it)
   - the `AudioMixes` folder
   - the `Worlds` folder (if you have it)
   - the `ConstructBlueprints` folder (with its subfolders: `.versions` holds older versions your blueprints' uses may name)
4. Paste them into `%APPDATA%\CallOfSlop`. Create the folder if it isn't there. If Windows asks, replace the files that are already there.
5. Start the new build and check your loadouts in the main menu.

> Do this before playing much on the new build. Whatever you copy in replaces the new build's files.

Not save files (you don't need to copy them): `update_check.txt` (when the game last asked GitHub for updates) and `update.log` (what the updater removed). Both live in the save folder and are safe to delete. The `Logs` folder isn't a save either: it holds the stuck-spot reports (`stuck-spots-*.json`, where bots got stuck in your matches). Send it to the developer if bots got stuck a lot; it's safe to delete.

### Old versions

Each update unzips into its own folder (`CallOfSlop-<version>`) with a small `.callofslop-build` marker in it. Once the new build reaches its main menu it removes older versions **that the updater installed**, keeping the previous one for rollback (**Settings > Player > Updates** to change that, or **Clean up old versions now** to see the list first). Folders you unzipped yourself, folders with `portable.txt` or save files in them, and anything else are never deleted. If you kept saves in an old portable build, copy them out before you delete that folder.

### The new default audio mix

The game's built-in sound levels are now the **SeabMix2** mix: your own gun, other players' guns, explosions, bullet whizz and the hit sounds are louder, footsteps a little quieter (README, "Audio mixer", lists every level). If you never saved a mix, you get it straight away. If you have an `audio_mix.json` (or presets in `AudioMixes`), every level you changed in the F8 mixer stays as you set it, and every level you left at its old default moves to the new one the first time the file loads; press **Save** in the mixer to keep the result. To start over from the new defaults, use **Revert to defaults** and Save, or delete `audio_mix.json`.

### Ship engines (2026-11-17)

Every ship's main drive now has its own engine in line behind it (a Drive Core, and on big drives a Fuel Pump), and the drive runs at a quarter without it. Ships in a world saved before this build were saved without those engines: the **Rust Bucket** and **Magpie** in an old Survival world push at a quarter (their look line says "no engine in line behind this bell"). Start a new world, or build a Drive Core S on the inner face of each nacelle's tail plate. The **Zero Day Drive Array** and the **Drive Pod S/M** are gone (the Zero Day now has a drive collar and four bells); a saved Zero Day or a blueprint that used them loads without its main drive.

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

A pressed cylinder's **stroke** (its length against its width) now adds power as well as lowering revs: a cylinder 1.6 times the usual length gives 1.6 times the power at 0.625 of the revs, so 2.56 times the torque; a short one gives less. Every piece keeps its key, size, mass and health. Catalogue cylinders, pressed cylinders of the usual length, and every engine the Engine Kit made before this version (all of the usual length) are unchanged. An engine you built with long-stroke cylinders keeps its revs (they were already the lower ones) and **never loses power**: a fuel system feeds by power, so its old one now feeds only part of the bigger cylinders, and the engine gives its old power times whichever is less, the stroke or what the fuel system had to spare. With a fuel system just the cylinders' size that is **the same power as before**, and the readout says "bank 1 starved: ... feeds 62.5% of its cylinders" (six Small cylinders 1.2 m long on two Small Carburettors: 240 kW, as before); one a size up gives more (a kit-made I4 turbo engine whose 50 cm carburettor had a sixth to spare: 309 kW before, 363 kW now). Press a wider carburettor or injector for that bank to get it all (about 55 cm across for Small cylinders at a 1.6 stroke: those six give 384 kW). A turbo or supercharger is sized by the cylinders' bore, so it boosts them whole at any stroke. A short-stroke engine gives less power than before at the same revs. An engine with long strokes on a crank a little smaller than the first size that needs a starter (from about 0.85 of it) may now ask for a starter motor. Look at the engine's readout after loading.

### Conveyors, presses and Building Stations in a saved world (2026-11-14)

- **Conveyors run at 1 m/s**, twice the old speed. A factory that worked before still works, but its pieces arrive sooner: a machine that ran short before may now keep up, and one whose output conveyor was only just long enough may now see pieces bunch at a junction.
- **A press with a Piece out conveyor** that leads, through Joins, to a Router, a Piece Rack that takes the part, an Assembler or another press now sends its tray parts (gears, coils, brackets) on that conveyor, tagged for the order they serve, instead of keeping them in its tray. A press whose conveyor leads anywhere else (a Splitter, a cutter, a former) keeps them in its tray as before.
- **A Building Station orders everything it can fit, and the next level ahead, while keeping room**: every part whose parent is already fitted (or the first part) is ordered at once; parts one level deeper, whose parent's part is already in the hold or on its way, are ordered ahead too, but only while the hold, counting everything in it and on its way, still has room for the largest part that can be fitted now. Nothing deeper is ordered, so the hold can't jam. If a hold fills anyway (parts you put in by hand), the station moves parts it can't fit yet to a Piece Rack on its network with a road back and fetches them again later; with no rack its screen asks you to take one out. A part you take out of the hold by hand isn't ordered again while you are near the station: bring it back, or walk away and it orders another. A part too long for the hold is refused once: the screen says to build a larger station or fit it by hand. A save keeps the parts it moved out, so a load doesn't order them again.
- **Player blueprints keep a machine's "orders only"**: a machine that took no orders but your own (a queue, even empty) is saved and built that way again. Blueprints saved before this version build their machines running their own settings, as they did.

### Sub-blueprints (2026-11-16)

- **Blueprints in blueprints.** Stamping one of your blueprints into a build and saving the whole keeps the stamp as a *use* of that
  blueprint. A blueprint gets an `"id"` and `"version"` in its file the first time it is stamped or saved with uses; older files are
  untouched until then and load as before. Saving over a blueprint with an id keeps its old text in `ConstructBlueprints/.versions`,
  so copy the whole `ConstructBlueprints` folder, subfolders included, when you move your saves. To give a friend a blueprint with
  uses, press **Pack** in the Blueprints tab and send them `ConstructBlueprints/Packs/NAME.pack.json`; they drop it in their own
  `ConstructBlueprints` folder.
- **Worlds.** A world saved mid-build keeps each Building Station's sub-jobs and the versions its blueprint started with. An older
  world loads with no uses on its bodies; a build started before this version goes on as a plain build.
- **Building Stations** gain a **Sub-jobs** setting (Auto by default): an idle station on the same construct takes other stations'
  uses. Set it Off on a station you keep for your own builds.

### Linked logistics (2026-11-16)

- **Worlds.** A world keeps its link ends' codes and pairs, ships' manifests, Route Computers' routes and Dispatchers' fleets on
  their parts, and the host's link ledger (what was asked of linked factories, and what a closed world owes) in the main world's
  save. An older build skips them; an older world loads with none. No new save file.
- **Building Stations' Sub-jobs** is now a choice: Off, Auto (as before) or **Linked** (also takes uses sent over a logistics link).
- **Storage Volumes can have walls.** A new Walls setting gives the box its own concrete walls and floor, 0.15 m inside it, and leaves the top open. A Storage Volume you place new from the inventory comes walled; the ones in your older worlds and blueprints stay open as you built them (turn Walls on in the configure tool if you want it).
- **A drill works out its spot.** Each 3 m spot of a deposit gives 1,200 units, then the drill says "this spot is worked out". Move it on, or carry it on a Linear Actuator rail with nothing wired into Set: it moves itself on.

### Keeping saves on a USB stick (portable mode)

Put an empty file called `portable.txt` next to `CallOfSlop.exe`. The game then keeps its saves in its own folder, like the old builds did. Copy your save files into that folder yourself.

### If something went wrong

- **Level is back to 1:**
  1. Close the game.
  2. Do the manual copy above.
  3. Check that you copied from the old build's own folder: the one that has `CallOfSlop.exe` in it.
- **A save got damaged:** every save keeps its previous version as `NAME.bak`, for example `loadout.txt.bak`, in the same folder. Close the game, delete the damaged file, rename the `.bak` file back to the original name, and start the game again.
- **Still stuck:** send `callofslop.log` from the game folder. Its `[saves]` lines say which folder the game used and what it copied.
- **The game crashed:** send `callofslop.log`, `crash.txt`, `breadcrumb.txt` and the newest `crash-NNNN.dmp` from the game folder (on a Mac: `~/Library/Logs/CallOfSlop`). They are logs, not saves: you don't need to copy them to a new build, and they are safe to delete.
