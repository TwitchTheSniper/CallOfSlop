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
| `blueprints.txt` | Your workbench blueprint library (newer builds; the first time it opens it copies in the builds from `workbench.txt`) |
| `audio_mix.json` and the `AudioMixes` folder | Your audio mixer levels and presets |
| `appearance.txt` | Your soldier's look and hat (newer builds; older ones kept the hat in `loadout.txt`) |

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
4. Paste them into `%APPDATA%\CallOfSlop`. Create the folder if it isn't there. If Windows asks, replace the files that are already there.
5. Start the new build and check your level in the main menu.

> Do this before playing much on the new build. Whatever you copy in replaces the new build's progress.

Not save files (you don't need to copy them): `update_check.txt` (when the game last asked GitHub for updates) and `update.log` (what the updater removed). Both live in the save folder and are safe to delete. The `Logs` folder isn't a save either: it holds the stuck-spot reports (`stuck-spots-*.json`, where bots got stuck in your matches). Send it to the developer if bots got stuck a lot; it's safe to delete.

### Old versions

Each update unzips into its own folder (`CallOfSlop-<version>`) with a small `.callofslop-build` marker in it. Once the new build reaches its main menu it removes older versions **that the updater installed**, keeping the previous one for rollback (**Settings > Player > Updates** to change that, or **Clean up old versions now** to see the list first). Folders you unzipped yourself, folders with `portable.txt` or save files in them, and anything else are never deleted. If you kept saves in an old portable build, copy them out before you delete that folder.

### The new default audio mix

The game's built-in sound levels are now the **SeabMix2** mix: your own gun, other players' guns, explosions, bullet whizz and the hit sounds are louder, footsteps a little quieter (README, "Audio mixer", lists every level). If you never saved a mix, you get it straight away. If you have an `audio_mix.json` (or presets in `AudioMixes`), every level you changed in the F8 mixer stays as you set it, and every level you left at its old default moves to the new one the first time the file loads; press **Save** in the mixer to keep the result. To start over from the new defaults, use **Revert to defaults** and Save, or delete `audio_mix.json`.

### The Legacy map pack

The **Classic**, **Collision Point** and **High Ground** map packs are now one pack called **Legacy** (`Maps/Legacy`, 36 maps, same maps and same names: Proto Box to Military Base, then Blueprint to Confluence, then Gravel Pit to Alpine Pass). A `settings.txt` that still picks random maps from one of the old packs switches to Legacy on its own the first time it loads. If you unzipped the new build **over** an old one, the leftover `Maps\Classic`, `Maps\CollisionPoint` and `Maps\HighGround` folders are harmless: the game sees that their maps are the same as Legacy's and ignores them (`callofslop.log` says so), but you can delete them. A map you edited in one of those folders still loads, as its own pack. Online, these maps are the same maps as before: hosts and joiners on either layout can play together.

### Workbench builds on removed receivers

Three weapon-builder receivers that were only a fire rate are gone: the **Ratchet Burst SMG**, **Triad Burst** and **Buzzbox Machine Pistol** receivers (the burst or buzzsaw is the Action part now). A saved build in `workbench.txt` that uses one loads on the **Stampede**, **Workhorse** or **Plinker** receiver with the **Ratchet Burst Group**, **Three-Round Burst Group** or **Buzzsaw Sear** action, so it fires as before, and the file is saved back that way. Nothing else in your builds changes; receiver damage numbers did change with the late-game power curve (docs/zombies/WEAPON-BUILDER.md, "The power curve").

### Workbench blueprints

The workbench now has a **blueprint library** (`blueprints.txt` in your save folder): any number of saved guns, melee weapons and grenades. The first time you open the workbench on this build, the builds already in your `workbench.txt` slots (and your melee and grenade) are copied into the library, once. Your slots in `workbench.txt` stay as they were.

### Keeping saves on a USB stick (portable mode)

Put an empty file called `portable.txt` next to `CallOfSlop.exe`. The game then keeps its saves in its own folder, like the old builds did. Copy your save files into that folder yourself.

### If something went wrong

- **Level is back to 1:**
  1. Close the game.
  2. Do the manual copy above.
  3. Check that you copied from the old build's own folder: the one that has `CallOfSlop.exe` in it.
- **A save got damaged:** every save keeps its previous version as `NAME.bak`, for example `progress.txt.bak`, in the same folder. Close the game, delete the damaged file, rename the `.bak` file back to the original name, and start the game again.
- **Still stuck:** send `callofslop.log` from the game folder. Its `[saves]` lines say which folder the game used and what it copied.
