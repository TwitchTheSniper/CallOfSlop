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
| `shipdesign.json` | The design open in the Ship Designer (newer builds) |
| `shipdesign.unread.json` | A saved design this build could not read, kept aside (newer builds) |
| The `ShipDesigns` folder | Ship designs you kept from the Ship Designer, which Creative can spawn (newer builds) |
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
   - `shipdesign.json` and `shipdesign.unread.json` (if you have them)
   - the `AudioMixes` folder
   - the `Worlds` folder (if you have it)
   - the `ShipDesigns` folder (if you have it)
   - the `ConstructBlueprints` folder (with its subfolders: `.versions` holds older versions your blueprints' uses may name)
4. Paste them into `%APPDATA%\CallOfSlop`. Create the folder if it isn't there. If Windows asks, replace the files that are already there.
5. Start the new build and check your loadouts in the main menu.

> Do this before playing much on the new build. Whatever you copy in replaces the new build's files.

Not save files (you don't need to copy them): `update_check.txt` (when the game last asked GitHub for updates) and `update.log` (what the updater removed). Both live in the save folder and are safe to delete. The `Logs` folder isn't a save either: it holds the stuck-spot reports (`stuck-spots-*.json`, where bots got stuck in your matches). Send it to the developer if bots got stuck a lot; it's safe to delete.

### Old versions

Each update unzips into its own folder (`CallOfSlop-<version>`) with a small `.callofslop-build` marker in it. Once the new build reaches its main menu it removes older versions **that the updater installed**, keeping the previous one for rollback (**Settings > Player > Updates** to change that, or **Clean up old versions now** to see the list first). Folders you unzipped yourself, folders with `portable.txt` or save files in them, and anything else are never deleted. If you kept saves in an old portable build, copy them out before you delete that folder.

### The new default audio mix

The game's built-in sound levels are now the **SeabMix2** mix: your own gun, other players' guns, explosions, bullet whizz and the hit sounds are louder, footsteps a little quieter (README, "Audio mixer", lists every level). If you never saved a mix, you get it straight away. If you have an `audio_mix.json` (or presets in `AudioMixes`), every level you changed in the F8 mixer stays as you set it, and every level you left at its old default moves to the new one the first time the file loads; press **Save** in the mixer to keep the result. To start over from the new defaults, use **Revert to defaults** and Save, or delete `audio_mix.json`.

### The Assembler builds only what nothing else makes (2026-11-28)

The **Assembler** now builds only what no Press and no Bake Oven makes: the hand-held items no die presses yet (seats, wheels, tyres, receivers, logic, signs and the like) and the giant parts no oven holds whole (the 64 m and 128 m Capital frames, tracks, keels, decks and so on). Everything else a Press presses or a Bake Oven bakes from a kit, and an Assembler set to one of those parts in your world now stands idle: pick another recipe, or build a Press, Extruder and Bake Oven for it. Its bed can now grow to 32 x 32 blocks (64 m a side, 512 blocks), and it needs no upgrade modules or tool blocks.

The **Gantry Carriage**, the **Overdrive Unit**, the **Feed Buffer**, the **Thruster Jig Block** and the **Sewing Table Block** are retired. In a saved world or an old blueprint they still load where they stood, as **Retired Assembler** parts that do nothing: recycle them for their materials. They are no longer in the inventory and no recipe makes them; an Assembler that was set to build one now builds the recipe before it (the Assembly Bed Block, or the Small Solid Booster, which a Bake Oven makes). Power Couplings and the other tier pieces on an Assembler's back do nothing there now; they still work on the machines that have tiers. The **Block Rack** stays, as a die rack with 2 m bays.

A new Survival world's start kit (on the Ground start) now also has a **Starter Press** (a Press fed by an Extruder, with its own genset; fit one of the kit's dies) and the **Starter Works** (an Extruder, a Building Station that fits a kit's pieces and a Bake Oven that bakes it: order a part at the oven, tip steel and rubber into its two bins), beside the Starter Assembler, Smelter, Foundry, Sawmill and the rigs. Worlds started before keep the kit they had.

### Assembler recipes in worlds saved before the docks (2026-11-28)

A world saved before the Dock Expansions (2026-11-25) or the Ship Docking Station (2026-11-26) had their recipes put into the middle of the Assembler's list, so some of its Assemblers loaded with a recipe one to four rows off. Those worlds now load with the recipe that was set. A world saved by a development build from 2026-11-24 to 2026-11-28 can't always be told from one saved just after those updates, so it may still show **another recipe** on an Assembler: open it and pick the recipe again.

### The Zero Day Works have no Assemblers (2026-11-28)

A **Zero Day Works** or **Zero Day Works II** spawned from this build has no Assembler. Works II's two press lines each have a third Press (with a Ring Die, for Bearings) where the line's Assembler stood, its bed on the line, so pressed parts still roll through it to the main station; the big Assembler by the main station is gone. Works I's block Assembler in bay 3 gives way to a small Press with a Cable Die. The fittings the Assemblers built are baked in the Bake Ovens from kits, and every kit line's Extruder is now chuted from the copper, concrete, rubber and cloth bins (Works I has no cloth). The **Server Rack**, **Robot Shell** and **Manned Manoeuvring Unit** are now pressed (a Housing Die, a steel box with Circuit Boards in the second bay), and an **Ord Rack** or a designed **bomb or missile** is baked from a kit in a Bake Oven where one can take it; an Assembler set to one of the three pressed parts builds the recipe before it in its list: pick its recipe again. **A works saved before this update keeps its Assemblers**: they still build the parts no oven or press on the works can make, as a fallback. Spawn the works again from the blueprint to get the new layout.

### Thrusters and lift cells are baked or pressed (2026-11-28)

The Thruster Jig's and the Sewing Table's parts are no longer only an Assembler's. Jets, Lift Fans, Turbojets, Rocket Engines, Solid Boosters, Repulsors, Anti-Grav Generators and the bigger Decouplers are made from **kits** baked in a **Bake Oven**, as other placed parts are: their Turbine Stages, Bearings, Thrust Chambers, Field Coils, Levite Cores, Wiring Looms and Circuit Boards are fitted as kit pieces. The Medium, Large and Capital **Lift Cells** are kits too: a steel frame with a **cloth sheet** over it (the small Lift Cell still comes from an Assembler with a Sewing Table). The **Extruder now takes cloth**. The small **Decoupler** is pressed on the **Button Die** from a steel plate with a Circuit Board in the second bay. Where no Bake Oven on the same construct can take the order, or no Extruder there is fed the material a kit piece needs, an Assembler with the right tool block still builds the part, so nothing stalls. A world saved before this update has its Assemblers' recipe choices moved to match when it loads; an Assembler that was set to the Decoupler builds the **Medium Decoupler** instead: pick its recipe again. Its queued orders for Decouplers are dropped when it next works (a log line says so), and the station that asked orders them from a press.

### A construct holds up to 1,000 links (2026-11-28)

A construct used to hold at most 254 links (wires, cables, pipes, shafts, chutes and conveyors); it now holds 1,000, so a big base such as the Zero Day Works II has room for more machines and your own wiring. Older worlds and blueprints load as they were. A world saved with more than 254 links on one construct does not load in an older build, and this build cannot play online with an older one.

### Placed parts are baked, not assembled (2026-11-28)

Every placed part the Assembler used to build (doors, walls, furniture, machines, hull pieces, structure: about 2,250 kinds) is now made from a **kit of extrusions** baked in a **Bake Oven**: a Building Station welds the kit (one extrusion for each of the part's boxes, with its Bearings, Circuit Boards and other components laid on top, and a rubber or concrete sheet where the part takes rubber or concrete) and the oven bakes it into the part itself. A part whose kit is a single extrusion needs no station: an Extruder whose conveyor reaches a roller run into the oven makes the piece and it rolls straight onto the bed. The **Extruder now takes rubber**. Where a Bake Oven on the same construct can take the order (a station whose line makes every piece, a tunnel that holds the kit), the order goes to the oven; otherwise the Assembler still builds the part, so nothing stalls (`callofslop.log` says once per part which way it went). An Assembler set by hand to one of these parts still builds it. Small items, the Thruster Jig and Sewing Table rows still come from the Assembler. Orders go to the smallest oven whose tunnel holds the kit; a bigger oven takes one only when the smaller ones are four orders behind.

### Bearings, lenses, shells and ordnance sections are pressed (2026-11-28)

62 parts the Assembler used to build are now pressed, each from one standard blank with its kit in the press's second bay. Three new dies, cut on the **Die Lathe** like the others: the **Ring Die** presses **Bearings** (a bronze round bar) and **Turbine Stages**; the **Bell Die** presses the **Thrust Chamber** and the ordnance motors and thrusters (Solid, Boost, Liquid and Hybrid Motors, Cold Gas Thruster, Ramjet, Turbojet, Rocket Assist, Jet Vanes); the **Shell Die** presses the four shells and the bomb and missile sections (warheads, fuses, seekers, body tubes, fins, wings, dispensers). The **Lens** is pressed on the Plate Die from glass, the **Field Coil** on the Cable Die, the **Levite Core** on the Board Die, and the Zero Day frigate's small fittings on the Housing, Control and Button Dies (Bulkhead Light, Ammo Box, Ammo Container, Requisition Crate, Medicine Cabinet, Ship Toilet, Ship Basin, Fire Director, Long Range Sensor, Computer, Turret Module, Driving Module). A survival start now has a Ring Die too. Seats and Flight Couches no longer take a Tyre Carcass. A world saved before this update has its Assemblers' recipe choices moved to match when it loads; an Assembler that was set to one of these parts builds the recipe before it in the list instead, or nothing if it was set to Bearings, Control Units or Servo Motors: pick its recipe again. An Assembler's queued orders for any of these parts are dropped when it next works (a log line says so), and the station that asked orders them from a press. A stocked Die Wall now holds a Ring Die, so it no longer holds a Spring Die; the Bell and Shell Dies are cut on the Die Lathe.

### Control Units fold into Circuit Boards; no Gear Sets in guns or thrusters (2026-11-28)

The **Control Unit** is no longer made: everything that took one now takes 2 **Circuit Boards** instead. A world saved before this update has its Assemblers' recipe choices moved to match when it loads (an Assembler that was set to build Control Units builds nothing: pick its recipe again). Control Units you already hold stay in your stores but nothing uses them: recycle them. Guns, gun receivers and thrusters no longer take **Gear Sets**; gearboxes, differentials, shafts and other drivetrain parts still do.

### Servo Motors and the Zero Day Works II in a saved world (2026-11-26)

The **Servo Motor** is now pressed, not assembled: a Press with an **Actuator Die** presses one from a solid steel blank with a **Copper Coil** pressed into it (the coil comes from a Press with a Cable Die). Its row left the Assembler's recipe list, and a world saved before this update has its Assemblers' recipe choices moved to match when it loads, so they keep their recipe (an Assembler that was set to build Servo Motors builds **Control Units** instead: pick its recipe again). The **Heavy Power Terminal** is new at the very end of the list. A block Assembler's bed now lets pieces on a roller run roll across it, as a Panel Former's does, so check any roller run that ends at an Assembler's bed. A **Zero Day Works II** saved before this update keeps its old layout: its pressed parts don't roll on to the main station, its Assemblers have no modules and it has no big Assembler. Spawn it again from the blueprint to get them.

### Sawmills in a saved world (2026-11-24)

The Sawmill is now one **Sawmill** core with no tiers, as the Smelter and the Foundry are: **Saw Blocks** welded on its +x side make it bigger, cut into squares that each work their own band mill (one block makes it 2.25 times as fast, a 2 x 2 seven times), and a **Plank Stacker** on a square's far edge takes the boards. The Workshop, Industrial, Military and Plasma Sawmills are gone: in a world saved before this version each one loads as a plain Sawmill core where it stood, at the core's own speed (4 planks a second) until you weld Saw Blocks on it, and a blueprint naming one builds the core. Its chosen recipe is kept (planks or cloth). The Sawmill core is smaller than the old tier 1 (2 m, not 3 m) and its ports are in other places (the Chute in is on the log bunker on top, the Chute out on its -x side): check the chutes into and out of an old Sawmill. Its tier rows left the plain Assembler's recipe list, and a world saved before this update has its Assemblers' recipe choices moved to match when it loads, so they keep their recipe (an Assembler set to a removed tier builds the Core). A world saved by a development build between the Smelter's and the Scrap Breaker's updates (2026-11-24) may show **another recipe** on an Assembler: open it and pick the recipe again. Saw Block and Plank Stacker are new at the very end of the list, after the Head Stand. Parts welded on an old Sawmill's sides other than its +x come loose when the world loads (`callofslop.log` names each weld dropped). Saw Blocks cut: keep out of a working Sawmill's bands and carriages.

### Foundries in a saved world (2026-11-24)

The Foundry is now one **Foundry** core with no tiers, as the Smelter is: **Crucible Blocks** welded on its +x side make it bigger, cut into squares that each pour from their own crucible (one block makes it 2.25 times as fast, a 2 x 2 seven times), and a **Mould Line** on a square's far edge takes the pour. The Workshop, Industrial, Military and Plasma Foundries are gone: in a world saved before this version each one loads as a plain Foundry core where it stood, at the core's own speed (2 steel a second) until you weld Crucible Blocks on it, and a blueprint naming one builds the core. Its chosen recipe is kept, and every Foundry now makes bronze and circuits, with no tier needed. The Foundry core is smaller than the old tier 1 (2 m, not 4 m), so its ports are in other places: check the chutes into and out of an old Foundry. Its tier rows left the plain Assembler's recipe list, and a world saved before this update has its Assemblers' recipe choices moved to match when it loads, so they keep their recipe (an Assembler set to a removed tier builds the Core). A world saved by a development build between the Smelter's and the Scrap Breaker's updates (2026-11-24) may show **another recipe** on an Assembler: open it and pick the recipe again. Crucible Block and Mould Line are new at the very end of the list. Parts welded on an old Foundry's sides other than its +x come loose when the world loads (`callofslop.log` names each weld dropped).

### Smelters in a saved world (2026-11-24)

The Smelter is now one **Smelter** core with no tiers: **Hearth Blocks** welded on its +x side make it bigger, cut into square furnaces that all work at once (a bigger square pays more: one block makes it 2.25 times as fast, a 2 x 2 seven times), and a **Casting Bed** on a square's far edge takes its pour. The Workshop, Industrial, Military and Plasma Smelters are gone: in a world saved before this version each one loads as a plain Smelter core where it stood, so it works at the core's own speed until you weld Hearth Blocks on it, and a blueprint naming one builds the core. Its chosen recipe is kept. Every Smelter now smelts copper, gold and levite ore, with no tier needed. A blueprint naming one of them keeps its recipe choice too. Its tier rows left the plain Assembler's recipe list, and a world saved before this update has its Assemblers' recipe choices moved to match when it loads, so they keep their recipe (an Assembler set to a removed tier builds the Core). A world saved by a development build between the Smelter's and the Scrap Breaker's updates (2026-11-24) may show **another recipe** on an Assembler: open it and pick the recipe again. Hearth Block and Casting Bed are new at the very end of the list. Parts welded on an old Smelter's -x side or its back come loose when the world loads (those faces have no sockets now: `callofslop.log` names each weld dropped), and a part welded on its +x side stays welded, though the Core's +x socket is in another place now: check what was built against an old Smelter's sides.

### The Assembler in a saved world (2026-11-21)

The Assembler is now one **Assembler** core with no tiers: **Assembly Bed Blocks** welded beside it make a bed for bigger parts, **Mast Blocks** outside the bed's corners give it height, **Gantry Carriages** on the bed build more parts at once, and **upgrade modules** bolted on its back (Power Coupling, Coolant Loop, Servo Rack, Plasma Conditioner) unlock the recipes that need them. The old Assemblers of every tier, the Thruster Assemblers and the Tailors are gone: a world saved before this version loads without them (its log says "left out"), and a blueprint naming one is refused at that step. Recipe numbers shifted too: rebuild your Assemblers, fit the modules their recipes need, and set the recipe again.

### Presses, dies and Die Lathes in a saved world (2026-11-21)

The Press is now one **Press** core plus **Press Blocks** welded beside it: blocks level with it make the bed, and blocks stacked on the bed's two long sides make its clamshell walls (keep a clear strip beside each wall for its swing). Dies have no sizes any more: one die a family, a 0.5 m cassette that fits any press, and one **Die Lathe** cuts them all from 0.25 m of steel bar. The Medium, Large and Capital Presses, their dies and Die Lathes and the Medium Die Rack are gone: a world saved before this version loads without them (its log says "left out"), and a blueprint naming one is refused at that step. A Die Lathe in an older world may show **another die** than the one you set: check its Cuts setting. Rows left the Assembler's recipe list: an Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again.

### Ore on the planets (2026-11-22)

Ore deposits on the planets are now far fewer and much bigger, with the ground coloured by what lies under it. In a world saved before this build the deposits stand in new places and start full: a drill or Ore Hauler post set on the old ore may stand on bare ground now. Look at a drill to see the nearest deposit and the way to it, and move it there.

### More parts are pressed (2026-11-22)

160 parts that the Assembler used to build are now made by a Press with the right die: the other sizes of hinges, pistons, turntables, pivots and suspensions, the suspension arms, steel sheets and plates 0.25 to 2 m, armour plates and belts, drive shafts, the large tanks, I-beams, square beams, rods, tubes, angles, channels, Medium Girders and Columns, braces and frame junctions, switches and knobs, logic blocks and sensors, and seven gearboxes and axles. The Assembler's recipe list no longer has them (they show dimmed under "Pressed"). An Assembler saved with a recipe chosen may now show a different recipe, because the list got shorter: open it and pick the recipe again. A blueprint that set one of these as an Assembler's recipe loads with the recipe None. The landing leg pod pieces (S to XL) went the same way, pressed from steel alone.

### Building Stations in a saved world (2026-11-21)

A Building Station's bay is no longer three settings: weld **Bay Blocks** (2 x 2 m floor tiles, made at an Assembler) beside the station in a full rectangle, and stack **Mast Blocks** on its four corners for height (2 m a block; no masts: 2 m). A station in an older world or blueprint loads without its bay settings and says "has no bay" until you weld Bay Blocks to it. The station itself is now 2 x 6 m, so a station in an older blueprint may need its neighbours moved.

### Laser Cutters in a saved world (2026-11-21)

The Laser Cutter is now one **Laser Cutter Core** plus **Laser Bed Blocks** welded beside it: the bed's size sets the largest plate it cuts, and each block across the bed adds a cutting head. The Medium, Large and Capital Laser Cutters are gone: a world saved before this version loads without them (its log says "left out"), and a blueprint naming one is refused at that step. A Laser Cutter you already have cuts nothing until you weld Laser Bed Blocks beside it. Two rows left the Assembler's recipe list: an Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again.

### Extruders in a saved world (2026-11-21)

The Extruder is now one **Extruder Core** plus **Die Blocks** welded beside it for a bigger die, and one **Run-out Table**. The Medium, Large and Capital Extruders, the Die Frames, the Die Extensions and the Medium and Large Run-out Tables are gone: a world saved before this version loads without them (its log says "left out"), and a blueprint naming one is refused at that step. An Extruder or a Run-out Table you already have becomes the new one, which is bigger (2 m across), so check that nothing stands in its way. Nine rows left the Assembler's recipe list: an Assembler in a world saved before this version may show **another recipe** than the one you set, or none: check your Assemblers and set their recipe again.

### Bigger planets (2026-11-20)

The Frostreach planets are four times their old size (Glacier is 32 km in radius, Caledon 24 km). A Sandbox world saved on them before this build is not moved to the new ground: its bases and ships would be inside the planet. Start a new world.

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

### Wheel, Fan and Airframe Formers in a saved world (2026-11-21)

The twelve Wheel, Fan and Airframe Formers are now one machine, the **Grow Former**. A saved world loads each old former as a Grow Former. Its size now comes from blocks: weld **Former Blocks** on the floor in front of it and stack **Mast Blocks** on it, then set **Makes** (Wheel, Prop or Wing) with the configure tool. An Assembler that was set to a recipe may show a different one after this update: pick its recipe again.

### Grow Former bins and tier pieces (2026-11-21)

A Grow Former's bin now goes with its bed: 400 and 100 for each Former Block in its bed, so a former on its own holds 400, not 13,200. One in an older world that holds more than its new bin keeps what it has, but takes no more until it uses some. Four new **tier pieces** (Power Coupling, Coolant Loop, Servo Rack, Plasma Conditioner, made at an Assembler) bolt on the slots along its back: each raises its tier by one when every lower piece is there, and a higher tier works faster, holds more and draws more power. New recipes were added at the end of the Assembler's list, so your Assemblers keep their recipes.

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

### Loader arms in a saved world (2026-11-24)

The fixed Loader Arms (Loader, Workshop, Industrial, Military, Plasma, Heavy, Shipyard, Long) and the Module Fitter S are gone. A saved one comes back as its kit arm (Kit Loader Arm and so on), standing where it stood. An Assembler that was set to build one now builds an Arm Core.

### Building Stations in a saved world (rovers, 2026-11-25)

A Building Station no longer has a hold or a crane: its Core is a dock pad for one rover, which fits every piece from the station's pick pads. A station in an older world loads with its old Core's place and sockets changed, so look at it: weld Bay Blocks to its pad's mouth, lay Pad Tiles just past the bay's edge for its parts (it says "no pick pad" until you do), and weld Dock Expansions to the bay for more rovers or heavier pieces (a Small rover lifts 8 t, a Medium 52 t, a Large 120 t). A conveyor into the old Core's Piece in now ends at nothing: run it onto the pad instead.

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
