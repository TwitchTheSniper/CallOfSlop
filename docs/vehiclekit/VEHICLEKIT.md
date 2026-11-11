# The vehicle kit (Source/VehicleKit)

A vehicle generator is C# code inside the game assembly that fills a `Vehicle` and writes an ordinary blueprint file. It exists so an agent can make vehicles for a faction
without working out sockets by hand, in the same way the map kit (docs/mapkit/MAPKIT.md) lets it make maps. Everything goes through the game's own rules as it is added, and the
result is **checked by the real game** before anyone drives it: the file goes back through the blueprint loader, and the real physics and weapons drive, turn and fire it.

Namespace `CallOfSlop.VehicleKit`. The committed files are what reviews, tests and the menu's tiles use: the game loads `Constructs/Blueprints/*.json`. A server can also run a
generator while the game plays, for a seed nobody committed ("Dynamic spawn" below). Either way the vehicle is an ordinary construct, spawned, driven, shot and salvaged part by part
like any other. **Fix the generator, not its files.** No AI is in here yet: each generated vehicle carries the data one will need ("Behaviour data").

## Where the guide's sections are

The guide is split by subject. This file keeps the two sections a generator's author needs first; the others moved
verbatim to the files below. A code comment naming `VEHICLEKIT.md "Section"` finds its file here by the heading.

- This file: "Writing a generator", "A faction style".
- [QUALITY-TOOLS.md](QUALITY-TOOLS.md): "Commands", "Quality tools".
- [VARIETY.md](VARIETY.md): "Shape grammar", "Layered variety", "Identity and weak points".
- [ROLES.md](ROLES.md): "Roles and tiers", "Families and convoys", "Cargo and spares".
- [SPAWNING.md](SPAWNING.md): "Spawn context", "How it scales", "Dynamic spawn", "Behaviour data".

## Writing a generator

A generator is a **shape**: where the room, guns and tanks go. It names no colour and no favourite part, so the same shape builds in any style. `HeavyShape` (`Generators/BoxTruck.cs`) holds what the big vehicles share; this is a whole one:

```csharp
using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

public sealed class GunWagon(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Gun Wagon";            // "Bandit Gun Wagon 3", BanditGunWagon3.json

    public override void Build(Vehicle v)
    {
        v.Description = "A gun wagon. A short crew room, two deck guns on an open bed, the drum at the tail.";
        Chassis(v, 4.75f, 1.75f);                                    // rails, tail
        v.Under("tub", 0.2f, 3.2f, 3.2f);                            // what the floor stands on: "stilts", "skirted" or "tub"
        v.Sill(v.RailZ(3.7f), layers: 2);
        var room = v.Room("room", new Vehicle.Rect(-Edge, Edge, 0.2f, 3.2f), Under, Vector3.UnitX, 1.2f,
            seatSpots: [(-0.75f, 0.825f)], mounts: [(-0.375f, 0.7f)], chamfer: true);
        v.Turret(v.Mounts["room"][0], seat: room.Seats[0]);          // turrets before furniture: it takes what's left
        v.Furnish(room);
        var spot = new Vehicle.Rect(0.375f, 1.375f, 4.45f, 5.45f);
        v.Deck("bed", Under, new Vehicle.Rect(-Edge, Edge, 3.45f, 5.2f), "bed", [new Vehicle.Rect(-0.375f, 0.375f, 4.45f, 5.2f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, 4.5f)), 1);
        v.Turret(v.Sill(4.825f, layers: 2));
        v.Hood();
        v.Trim("roomwalls", "hoodsides");
    }
}
```

Add it to `VehicleRegistry.Shapes`, run `--sweep-vehicle "Bandit Gun Wagon" --seeds 16`, then `--gen-vehicle` and commit the files. A small vehicle derives from `VehicleShape`, says its `Seeds` and uses `Ladder`, `SteeredAxle` and so on itself (`Technical.cs`). This one took four tries and four minutes once the kit's rooms worked.

**Coordinates.** x right, y up, -z forward (a seat faces -z). y 0 is the rails' centre, about wheel-axle height. Frames work on the sockets' grid: rails stand at x = ±0.25 (a differential's and a cross beam's socket spacing), a plate's corners on odd eighths (±0.125, ±0.375...), and `Lengths` makes a run of square beams from any multiple of 0.25 m.

### The layers

**`BlueprintScript`** (`VehicleCore.cs`) is the bottom: a live construct and the steps that made it. You rarely need more than this:

| Call | What it does |
| --- | --- |
| `Put(part, centre, orient, reach, on, by, fit)` | Searches the free sockets (of `on`, else the body) for the place nearest `centre` (the middle of the part's bounds), through `ConstructBuild.Place`. `orient`: `Orient.Forward`, `Upright`, `Along(axis)`, `Facing(dir)`. `fit`: half sizes it must take up (0 on an axis: any), which settles a long plate's way round. Refused with the nearest try and what stood in the way, named. |
| `Stand(part, at, orient, on, by)` | `Put` by one of its own sockets: a seat's feet, an engine's mount. |
| `Onto(on, socket, part, by, spin)` | A named socket. `by` null: `SnapBuild.Fit` picks; `spin` null: `SmartRotation.BestSpin`. |
| `Weld(part, on, centre, orient, pitch, yaw, roll)` | A blueprint's freeform step: anywhere, at any angle. |
| `Wire(from, to)`, `Link(type, from, to, ...)` | Through `ConstructBuild.CanWire` and `ConstructRoutes.BuildLink`. `laid: true`: only ends and lengths are checked, so a pipe may pass through a plate. |
| `Paint(part, colour)`, `Config(part, (key, value)...)`, `Cargo(part, at, turn)` | Step paint, settings and cargo. |

A step can't be invalid: what the game refuses throws a `VehicleKitException` when it is added. Nothing here mirrors a rule of the game.

**`Vehicle`** (`Vehicle.cs`, `Vehicle.Body.cs`) is the fluent layer; each call fits real parts and keeps handles for later ones, so call them in this order:

- **Frame.** `Ladder(frontAxleZ, railLength, nose, tail)`: rails, the front cross beam, the rear differential in line with the rails. `Sill(z, wing, layers)`: a cross beam on the rails with `wing` metres out each side, on `layers` beams; a sill is what a bed, a seat or an engine stands on.
- **Axles.** `PickGear(names...)` rolls a recipe from the style's `LightGear` or `HeavyGear`: springs and wheels at each end (they needn't match), the differential, the floor's height, a free-rolling `Tag` axle or a driven `Tandem` one (6x6; pass it to `Ladder`). `SteeredAxle(wheel, spring)` (Steering Pivots on the cross beam), `DrivenAxle(wheel, spring)`, `TagAxle(z, spring, wheel)`. A Swing Arm throws a wheel out past the body, so that recipe's floor can sit low between the wheels. `TowHitch()`, and `Drawbar()` on what's towed; set `v.Tow` to the towed blueprint's name.
- **Powertrain.** `Powertrain(z)`: engine on its own sill, gearbox behind it, a shaft to the differential. `FuelFrom(tank, nodes...)`: the pipe.
- **Heavy.** `v.Heavy()` before `Ladder` makes the chassis slots read "heavyWheel", "heavyEngine" and so on; `frontWing` widens the front axle to a Wide Differential's track.
- **Crew.** `OpenCab()`: the seat sill, the driver, sometimes a passenger, the style's "cab" shell (roll bar, roll cage, tin roof, chop top, mesh guard, slit cab, windscreen frame), a floor. Kit 5 (BODY-STYLES.md section 9): `LoftCab(module)` instead, the cab a run-time part in the body's language (`HullLoft.CabShape`, a `Hull Loft (cab...)` key) with its window panes (`Hull Loft (pane...)`, glass parts of their own on the cab's `glass0`/`glass1` sockets), a crew cab's second row; `MergeCab(room)` after a room for a cab merged into it. Phase 4 (BODY-STYLES.md section 10): `Trim()` ends with the faction's fittings on the cab and hood (`FactionDecos`, before the piper and trophies take their sockets; a Highland chrome grille goes on with the hood) and its trim on every free bay waist (`FactionTrim`, `Hull Loft (trim...)` parts cut for each bay's side), each fitted with `clear: true` and a check that it draws into nothing (`DrawnClash`), painted after the rooms (`PaintTrim`). `Driver(at)`: seat, Driving Module, wires (seat to module, module to engine). `Passenger(at)`. `Turret(mount, weapon, seat, aft)`: turntable, arm, motor hinge, a gun from its parts, Turret Module; `seat` null puts the gunner on the arm, else a seat in a room works it from inside; `aft` starts it facing the stern, and every piece on the arm (seat, hinge, gun) is turned as the arm is, so the gunner faces where the gun points at rest: the full check refuses a turret seat more than 15 degrees off its gun's line (`VehicleChecks.SeatOffGun`; aft guns stood sideways before). The turntable and hinge go by the gun's class (`TurretTable`, `TurretHinge`): light guns a Turntable 1x1 Long (180 deg/s) and Motor Hinge 1x1 Long, Highland and British light guns a 1x1 Medium half the time, medium guns a Turntable 2x2 Medium (90 deg/s), heavy and merged guns the Turntable (45 deg/s); a light gun on a pedestal beam takes the 2x2 (a 1x1 would share the beam's sides). `--armoury-report` prints the joints fitted and the seat count. `SideGun(bay, side, seat)`: a Gun Sponson on a bay's waist with a gun on it. `Feeds(crateA, crateB, typeA, typeB, leverOn, leverSocket, gun, more...)`: two crates of different rounds (`ammoType`, not ammo parts: `FitGun` drops an "ammo" entry), an Ammo Switch, a lever and feed links, the way a player wires it. `Panniers(bays, dice)`: faceted boxes on free waists.
- **Under a floor.** `v.FloorY` is the floor's height: a recipe's `Gear.Floor`. `Under(kind, z0, z1, gaps...)`: "stilts" (sills, in the open: one layer under a low floor, two under a high one), "skirted" (the same, the next room gets Hull Skirts under its floor's edges) or "tub" (closed Chassis Tubs on the rails, no stilts).
- **Rooms.** `Hull(tag, z0, under, bays, seatSpots)`: a Bolt Deck floor and a run of faceted bays from `Constructs/Parts/Hull`, named less "Hull ": `["Brow T", "Door", "Bay T-L", "Tail L"]`. Sections are T tall, L low, N narrow; "Bay T-L" goes from one to the other, "Door" is the Door Bay (a flush door on a Long Door Hinge in its +x side). Always start with a Brow and end with a Tail: a Bay's ends are open, and the check finds the hole. `room.Bays` gives a turret's mount (a bay's "top") and a side gun's ("+x0", "-x0"). `Furnish(room)` (kit 5 adds role gear and the faction's props first and last: BODY-STYLES.md section 13): furniture standing on the floor down the side walls by a layout from the style ("layout" picks one, "layout NAME" lists its slots; bunkroom, workshop, hold, mess), never in the doorway, keeping the middle clear (a player is 0.8 m across), a chest if nothing else takes loot. What has slots rolls from the loot table at each spawn.
- **Body and armour.** `Deck(tag, y, rect, slot, holes, smallest)`: a flat deck patched from seeded plate sizes. `Wall(tag, face, at, from, to, bottom, height, slot, gap)`: upright plates along an edge. Armour is a `Deck` or `Wall` on a slot of armour plate names ("Armour Plate"). Both remember their panels under `tag` in `v.Panels`.
- **Off the grid.** `Crooked(tag, part, on, at, face, dice)`: a welded plate tilted up to the style's `tiltDegrees`.
- **Dress.** `Hood()`: a style "hood" part on the engine (or "plates", tiled), its "nose" and "fender"; kit 5 lofts it (`Hull Loft (hood...)`, the plan's hood part giving its length) with the same sockets, so noses and fenders go on as before; `Sides`, `TubeRails`, `DrumOn`, `Crates` for beds. `Trim(wallTags...)` (ram bars, crooked patches, spikes, trophies, paint: everything a shape shares), `RamBars(top, z, dice)`, `Dress(slot, panels, dir, count, dice)` (spikes and trophies on free sockets facing `dir`, never where a face would z-fight with a part already there), `CargoBay(volume, centre, size, slot, count, spots...)`, `PaintAll(parts, role, dice)`.

A deck or wall plate that no socket takes leaves a gap, on purpose: rust. Anything else that doesn't fit throws.

**Seeds.** `v.Dice` is the seed. Take a `Fork("name")` per feature, so another roll in the paint doesn't change the chassis, and pass it down. Never use `Random`, `HashCode` or anything that varies by machine. `--gen-vehicle` runs each seed twice to prove it.

### Silhouette

The rules for every faction's vehicles. A vehicle body is a set of volumes, not a box with cut corners, and rounding corners doesn't fix a box.

- **Separate masses.** Nose, cab, body and load are distinct volumes with their own heights and widths, with air between them: a hood, a gap, then a body. A cab is narrower than its bed, or stands apart. No plate wall behind a driver.
- **Break the outline both ways.** From the side the roofline steps or slopes and the ends rake or taper. From above something narrows: a nose, a cab, a tail. A side breaks at least twice between floor and roof (hull sections do this for you), and bulges where a sponson or a pannier hangs.
- **Wheels aren't under a lid.** Outboard on arms between a low body, or under an arched fender, or exposed with the mechanicals showing. Stilts only where the design wants them (a fort, a tanker's cradles).
- **Not everything is a room.** A room earns its place: loot and a crew to hide. A tanker is its tank, a gun wagon its deck.
- **Mismatch is allowed.** Wheels front and rear, a stepped roof, one side's panniers. A bandit vehicle needn't be symmetric.

The check measures it: `MaxSideFill` (62%) and `MaxTopFill` (84%) are how much of its bounding box the body may fill from the side and from above, over the fixed parts (no wheels, no turrets), and the note gives how much the roofline wanders. Every room is also shot with rays through what's drawn, and any two that leave side by side are a hole. A new shape that fails these needs a different outline, not a tweak to the limit.

New shapes: a "hull" box (`"shape": "hull"`, `"points"`: corners in the part's frame; the drawn shape is their convex hull) takes any facet at any angle; hidden boxes stand for it to the build rules, as with every shape. `tools/constructs/hull_parts.py` shows the idiom.

### Looking at one

`--plan` draws real meshes, so slopes show in it. For shaded pictures from the side, three quarters, above and inside: `xvfb-run -a -s "-screen 0 1280x720x24" dotnet bin/Release/net10.0/CallOfSlop.dll --vehicle-shots "Bandit Box Truck 1" "Bandit War Bus 10"` writes `smoke_vehicle_*.bmp` next to the log. Look at them before calling a shape done; they are for you, not for review.

### Guns by their parts

`WeaponOption(name, build, box)`: `build` is the weapon builder's own text, as in `Parts/Builds`: `"Whaler Receiver{barrel:Harpoon Tube,feed:Harpoon Breech,ammo:Barbed Head,action:Harpoon Trigger}"`, with `{muzzle:...}` allowed on a barrel. `Vehicle.FitGun` fits each as a construct part (builder names work whatever part wraps them: "Workhorse Receiver" is the Rifle Receiver) on the receiver's faces, the barrel on its front and the others on the first face free, then the game's own `BuildRules` judge the gun; a refusal throws their words ("Belt Pouch is made for heavy rounds; the Workhorse Receiver fires rifle rounds"). `box`: the Ammo Box on the feed; null for a flame gun, which drinks the construct's fuel.

### The armoury

The styles' own `Weapons` lists hold a handful of guns each (Militia one). **The armoury** (`Constructs/VehicleArmoury.json`, `Source/VehicleKit/VehicleArmoury.cs`) adds turret gun recipes
as data, each with a weight and its own name per faction: `FactionStyle.Weapons` is the style's own list followed by every recipe its faction names, as many times as its weight
(`OwnWeapons` is the list without them; `--no-armoury` on any tool turns the armoury off, to measure against). Every pick the kit makes draws from that list: the grammar's bed,
bay, roof and flank guns, a role's weighted pick (by class, rarity and now the recipe's tier) and a generator's own `Turret` calls. A vehicle with no role is armed for tier 2
(`VehicleArmoury.PlainTier`): a recipe whose least tier is above that is a tenth as likely, so bosses' guns turn up on plain spawns rarely.

**A recipe** is a gun in the weapon builder's text, fitted by `Vehicle.FitGun` through the game's rules like any style gun, so every one is a gun a player could build:

| Key | Meaning |
| --- | --- |
| `id`, `about`, `inspired` | Its name in the file, a line on what it is, and the Zombies mystery box builds (`Parts/Builds`) it comes from. |
| `build` | `Receiver{barrel:...,feed:...,action:...}`; a splitter's barrels as `barrel_N` inside it (`{barrel:Cross Splitter{barrel_1:Stub Barrel,...}}`: the Nth goes on the splitter's `front-N`, or a hand-made splitter's outputs in order), a muzzle device as `{muzzle:...}` on a barrel. |
| `merge` | 1, or 2 or 3: an NxN square of that receiver, merged by the game into one receiver N times the size (docs/sandbox/CONSTRUCTS.md, "Merged receivers"), its gun parts on the merged receiver's centre sockets and drawn N times bigger. Damage N^2 a shot, fire rate / N. |
| `box` | What feeds it (default `Ammo Box`); `null` for a flame gun, which drinks the vehicle's fuel. |
| `class`, `tier`, `tags` | light, medium or heavy (`VehicleRoles.ClassOf` takes it, and a merged gun with none is heavy); the least tier it is common at; what it is for: light, medium, heavy, boss, aa, anti-armour, close, suppress, flame, oddity. |
| `mount` | `pintle` (open, as before), `shield` (a half-metre plate standing on the turret's arm between the gun and the gunner, below the gun's line, turning with the turntable; where the arm has no room, as on bed turrets whose seat rides the arm behind a long receiver, the gun goes on open) or `twin` (a second receiver of the same type side by side on its right, turned alike, both wired to the one Turret Module: a 1x2 doesn't merge). Sponsons stay the grammar's own mount (`SideGun`). |
| `factions` | Style name to `[weight 1 to 6, "the faction's name for it"]`; names are ASCII. |

**How a merged gun is fitted** (`Vehicle.MergeBlock`): the lead receiver on the hinge's leaf, the rest of the square to its right and above it, each turned exactly as the lead
(`OntoTurned` previews the turn with `ConstructBuild.Place` before fitting), then a rebuild; it throws if the game didn't merge them. Its barrel, feed and action go on the merged
receiver's centre sockets (`OntoCentre`, which a blueprint names `"merged front"`, `"merged left"`... : `ConstructMerge.SocketName` and `SocketIndex`; a blueprint finds the merges again
before a gun part's step, and a player's saved blueprint now keeps a merged gun's parts too). The box goes on the magazine, else on the receiver or any of its cells; on a room's fed
turret the belt's Ammo Socket goes on any cell (a socket on a member feeds the merged gun). `GunReach` counts the merged size. A twin on a fed turret keeps a magazine and a box on each
gun (`SelfFed`: the cradle has no free face for the belt's socket), and `Feeds` lays no belt to it.

**Adding a recipe.** Add an object to `recipes`, then run `--check-armoury [--recipe ID]`: it fits every recipe on a test turret (a Militia stand) through the game's rules, checks a
merge came out NxN and a twin fires both guns, writes the blueprint and builds it back (the same gun, the same damage and rate), and prints a line a recipe: N, mount ("(shld)" when the
shield found no room on a turret whose seat rides the arm), class, tier, damage a shot, rpm, dps, shots, range, magazine, mass on the gun's segment, reach and the factions. A
refusal prints the rules' reason for every face it tried. Then `--armoury-report [--seeds N] [--style S] [--shape NAME] [--check]` over the grammar's shapes: vehicles made, guns,
distinct names and builds, vehicles mixing guns, merged and heavy guns, generation time, the full check with `--check`, and per style the distinct guns over every shape.
Keep a recipe's dps in its tier's band (tier 1 under about 450, tier 2 and 3 under about 650, 1100 and up only for boss guns at tier 4) and its weight low when it is heavy.

**The mystery box picks** (the Zombies box's prebuilt guns in `Parts/Builds` that make good turrets; each recipe's `inspired` names its own):

- Merged blocks, from the many-receiver rigs: Backbone, Heavy Hitter, Hammerfall Cannon (a 2x2 rifle autocannon), Firing Squad and Reaper Marksman (a 2x2 battle rifle cannon), Suppressing
  Fire and Bastille Squad LMG (a 2x2 heavy MG), Dragon's Choir and Clown Car (a 2x2 pump "grapeshot"), Heavy Pockets and Road Rage (a 2x2 carbine), Lighthouse Keeper (a 2x2 DMR),
  Titan's Tantrum (a 2x2 chain gun, a boss gun), Gantry Sixteen and Exo Twelve (a 3x3 rifle cannon, nine rifles a shot), Five-Alarm Chili (a 2x2 flamer), Power Grid (a 2x2 energy carbine).
- Multi-barrel: Crossroads (a chain gun through a cross splitter), Totem Pole (four stacked stub barrels: flak), Tower of Power, Line Dance, Hydra Platter, Fire Escape (an autocannon
  through a ladder splitter), Pyramid Scheme, Bunk Bed, Twin Splitter Shotgun.
- Twin cradles: Twin Thumpers, Conga Line, Choke Hold, Forked Tongue, Double-Tap Twins, Toaster Duck (twin parade cannons), the Harpoon Gun twice.
- Oddities and heavy singles: Gutter Ball, Seven Shooter, Gutbuster, Elephant Gun, Ground Breaker, Hot Takes, Auto Shotgun and Tumble Dryer, Belt-Fed Chaingun, Bucket Lobber and
  Grenade Organ, Rail Battery, Sewing Kit.
- Left out: the melee builds, pistols, and rigs whose frame is the point (Kitchen Sink's nine receivers of different types can't merge; Minigun and Pepperbox: a rotary receiver takes
  one barrel on a construct, so their barrels can't be fitted; the Peacock splitter's launcher tubes meet each other at construct size, so Party Cannon became the twin Prize Draw).

**By faction** (what each leans to): Bandit, cobbled and mismatched: merged rifle blocks on stub barrels, chain guns, oddities, a 3x3 "Whole Scrapyard"; Militia, standard issue: service
cannons, heavy MGs, quad AA, twins, Hammerfall, the bunker buster; IT, high tech: coil and energy guns, merged marksman cannons, cascades of barrels; Scam (the Call Centre), showy:
splitters (Pyramid Scheme, Hydra Platter, Line Dance), Forked Tongue, the Prize Draw; Highland: shotguns and grapeshot, coach guns, twin claymores, harpoons; British: heavy LMG blocks,
flak in a teapot, elephant guns, a big flamer ("Proper Brew").

**Measured** (2026-11-08, `--armoury-report --seeds 10`, generation only, the five grammar shapes x six styles, 300 vehicles; before is the same build with `--no-armoury`, which
picks exactly as main did):

| Style | distinct guns before (names) | after | made before | after |
| --- | --- | --- | --- | --- |
| Bandit | 5 (3) | 19 (18) | 49 | 49 |
| IT | 4 (3) | 15 (14) | 49 | 49 |
| Highland | 4 (4) | 20 (20) | 48 | 47 |
| Scam (Call Centre) | 4 (3) | 16 (15) | 50 | 50 |
| British | 5 (4) | 17 (16) | 49 | 49 |
| Militia | 1 (1) | 11 (11) | 50 | 50 |

Per shape over its 10 seeds, a style's vehicles carried 3 to 5 distinct guns before (Militia 1) and 5 to 12 after; vehicles carrying two different guns rose from 75 to 114, 102 of
422 guns are merged receivers and 129 are heavy (none read as heavy before: `VehicleRoles.ClassOf` looked for the receiver's bore in a feed part's list, which is empty on a receiver,
so every gun read as medium; it reads `Receiver.Bore` now, so Whalers and Firebreathers are heavy, as this section always said).
The full check (`--armoury-report --seeds 6 --check`, the five shapes x six styles x seeds 1 to 6): 177 of 180 pass with the armoury, the 3 others failing to generate (Bandit
Scrap Tanker 3's tank barrel and Highland War Bus 5's box shape, as without it, and Highland Scrap Tanker 3's piper slot); the same build without it passes 178 of 180 (main: 177).
Before the aim measure below was fixed, 11 more failed it: slow-round Kingpin guns laid 14 degrees up for their drop, and a roof Firebreather converging on its point.
Generation took 389 ms a vehicle against 415 before (no gun takes noticeably longer to fit), and 6 of 300 failed against 5 (the Scrap Tanker's tank barrel seeds, as before, and one
Highland Scrap Tanker's piper slot after the dice moved). The armoury adds nothing to a tick: the guns are ordinary construct guns.

### Things that caught the vehicles so far

- Walls hang 0.12 m under the floor they're on, so nothing may stand in that band: a Large Wheel's top (hence "heavyWheel" is the Off-Road Wheel on two-layer sills), or a sill in the line of a wall (keep sills 0.25 m clear of a room's end).
- Only 1 m wide plates come 2 m tall: a room's sides are whole metres, a door takes 1.25 m of one, and the rest of that wall must be 1 m or more. So a side door needs a room 3 m long or more.
- A room's door (docs/sandbox/CONSTRUCTS.md, "Doors") sits in the wall's plane, from the floor to the wall's top, in front of the floor's and the roof's edges. The Long Door Hinge is a freeform weld to the plate beside the opening, because a plate's edge sockets are at its back face and on its own rows; the door then goes on by the hinge's socket (`Onto`, not `Put`: a welded part's frame isn't the body's). The wall's plates either side of an opening are always Plate Steel, so the door is flush whatever the rest of the wall is. It opens outwards, on the rust palette.
- A ladder hangs from its top: the Scrap Ladder is 0.8 m, a Boarding Ladder 1.2 m, and that one drags on the ground under a floor this low. A drag shows as a vehicle that stands tilted and won't move.
- `Deck`, `Wall` and `Boarding` swallow "no socket here" on purpose. Anything else must not be swallowed: a refused part once left every vehicle silently ladderless.
- A long gun at rest reaches about 1.4 m forward of its arm, and rest poses are what `Blocked` compares, so leave that much in front of a deck gun for the longest gun in the style. A sweep finds it: only a seed with that gun fails.
- Modules and stores eat the aisle: turrets first, modules behind the seat, stores on what's left, none within 1.3 m of the door.

- **`Feeds`** gives a turret's gun a reel and belt, not a laid feed (a flexible section tears on a full traverse). Set `v.FedTurrets = true` before `Turret`, so `FitGun` leaves off the magazine and box. Reels sit on the crates' free faces; crates in a hutch pass `reelAt`, a spot on the floor. Blueprints plug the belts through `"hoses"`.
- A differential's **Input shaft** shares its `-z0` socket: a rail bolted there leaves the port "taken". `Ladder` stops the left rail short and hangs the differential on the right rail.
- Rails at axle height leave the wheels' tops above them: a bed wider than the wheels stands on two-layer sills (`layers: 2`).
- `ConstructBuild.Blocked` compares what's on joints in the rest pose, so a long gun's barrel can refuse a tall wall in front of it. Keep walls under a turret's gun line.
- A routed link with nodes is refused through solids unless `laid`. A shaft with no nodes runs straight and is only checked at its ends.
- Stacked beams' bands, and bolts on a plate under an engine's details, are coplanar. Paint a frame one colour (one look doesn't fight); the hood uses `smallest: 2`.
- `Freeform` parts aren't checked against the rest for overlap. Keep a crooked plate proud of what it covers: 6 cm clears 5 degrees on a 0.5 m plate.
- A gun fires over its own deck: a flame gun's licks and a pump gun's pellets spread, and they hit the vehicle's own loose cargo (a separate body: its own construct is
  skipped, its load isn't). The first cargo pass put Small Fuel Tanks on a War Bus's roof rack in front of a roof Firebreather; the full check's firing set them off, the
  blast took parts off and the turret no longer followed its gunner (104 to 160 degrees off). Loose cargo now stays under every gun's muzzle by a margin growing with the
  distance (`VehicleCargo` `TurretSweeps`: 0.25 m plus 13 cm a metre for flame and pump guns, 3 cm a metre for the rest), and under a turret's ring within its arm's sweep.
- A pogo strut hangs its axle 0.6 to 2.5 m below its mount (the Heavy Pogo Strut 1.375 m); every other spring holds it level with its mount. Kit 3's chassis once mixed the stilt recipe's front (Heavy Pogo Struts) with another recipe's rear on Heavy Suspension: Bandit convoy 3's leader stood 10.5 degrees nose up on its tail rails and didn't move, and only a build was checked at spawn. A mix now keeps corners that hang alike (`VehicleChassis.Hang`, `Level`: within 0.3 m), and every generated vehicle passes the drive check ("Dynamic spawn").

## A faction style

A style (`Styles.cs`, class `FactionStyle`) is all a generator may vary and the look it varies within. Shapes never name a colour or a favourite part, so **a new style is all a new faction needs**: every shape comes with it. `Styles.Militia` proves it (run `--gen-vehicle "Militia Box Truck" --out DIR`; it ships nothing). `Styles.Shipped` says which styles' files are committed.

```csharp
public static readonly FactionStyle Militia = new()
{
    Name = "Militia", Group = "Militia",                        // Group: its heading in the inventory's Blueprints tab
    Palette = new() { ["body"] = ["#4f5a3a", "#5c6644"], ... }, // colours by role
    Parts = new() { ["wheel"] = ["Off-Road Wheel"], ["sheet"] = ["Armour Plate"], ... }, // choices by slot; twice in the list is twice as likely
    Weapons = [new("Service MG", "Reaper Battle Rifle Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ}")], // the weapon slot
    LootTable = "bandit",                                       // Constructs/Loot.json
    Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 }, // what a seed may do
};
```

A slot a generator asks for and the style lacks throws, naming the slot. Sheet slots hold a material's first words ("Plate Steel"): the kit adds the size.
A style needs `LightGear` and `HeavyGear` recipes (`Gear`); Militia now borrows the Bandits' (without them nothing of it generated). Its identity signature is optional (`Signature`, "Identity and weak points"): Militia names none and gets one derived from its dressing slots.

**Module weights** (the shape grammar, kit 2). `FactionStyle.Modules` scales how likely the grammar makes each module, by family and name:

```csharp
Modules = new()
{
    ["nose"] = new() { ["plated hood"] = 2 },      // twice as likely as the style's hood list makes it
    ["body"] = new() { ["room"] = 0.5, ["bed"] = 2 }, // body patterns weigh the product of their segments' factors
    ["rear"] = new() { ["gun deck"] = 0 },          // never
},
```

A family or module not named keeps its default, and the defaults come from the style's own slots: a cab or a nose weighs as many entries as the style's `heavyCab` or
`hood` list has of its parts, so a style with three Long Hoods and one Snub Hood makes long hoods three times as often, and a style that lists no Cab Over Shell never
makes a cab-over. A style that names no modules (all of them so far) works unchanged, and its identity comes through: the IT Department's cabs are enclosed or
cab-over, the Bandits' anything. `--diversity` lists the modules a seed chose as traits (`module nose`, `module body`...).

**Its own seeds.** A seed's dice fall differently over another style's lists, so a shape's seeds that pass in one style can fail in another. `FactionStyle.Seeds` names the seeds a style ships for a shape (`["Box Truck"] = [1, 20, 21]`); a shape it doesn't name ships its own. Pick them with `--sweep-vehicle "IT Box Truck"` ("most different").

**The weapon slot.** `FactionStyle.Weapons` lists what a turret may mount, by parts ("Guns by their parts"); each turret rolls from its own fork. A name twice is twice as likely.

### The Bandit style

Rust and bare metal, one colour of frame, war paint on a few panels, plates that don't match and some that are missing. The part kit's `--gen-parts bandit_parts` (Source/PartKit/Generators/BanditParts.cs) writes what the game didn't have (`Constructs/Parts/Bandit`): the Fuel Drum, spikes, skulls and a hubcap. Run `tools/constructs/assembling.py` after it for their recipes.

The designed weak points: the Fuel Drum (55 health, and on the technical the only fuel) stands in the open, its pipe under the floor; nothing covers the tyres; the driver sits in the open with no roof or glass. On the Scrap Tanker the big tanks stand in the open too. **Signature** (`VehicleIdentity.Signatures["Bandit"]`): marks, fuel in the open (seen from two sides) and spikes or trophies (Iron Spikes, skulls, a Hubcap Plate); characteristic parts those, the drum, a Ram Plow, mesh-guard and slit cabs; modules caged and armoured cabs, ram and plated noses, panniers. Weak points: the fuel, two tyres or more, the open driver on light vehicles, and a light vehicle's ammo crate when it has one. A technical's crates ride loose in a Cargo Volume under the bed, so they stay aboard until the volume breaks or someone lifts them out.

Loot: containers in rooms roll from the `bandit` table in `Constructs/Loot.json` (docs/sandbox/CONSTRUCTS.md, "Loot"). Tier 1 and 2 parts, common and uncommon weapon parts. **Ammo Crates stay out of it on purpose:** a stored item doesn't cook off, and a crate is 45 kg, physics-only, so it couldn't go in a slot anyway. An Ammo Box in a slot is safe loot; a technical's loose Ammo Crate is the one that blows.

### The IT style

The IT Department (`Styles.It`, group "IT Department"), a parody of modern office work and the first faction to try faction work with. Beige cases and carpet-tile grey on the panels, corporate blue on the hoods, war paint in lanyard yellow, link blue, paper white and failed-build red; plates that line up (it passed an audit). `Source/PartKit/Generators/OfficeParts.cs` writes its dressing (`Constructs/Parts/Office`): a CRT Monitor, an Office Plant, a Desk Fan and an Employee of the Month plaque as trophies, Router Antennas and a Wi-Fi Mast as spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room. Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). It is the tech faction, and its vehicles carry IT hardware: a Satellite Dish, a Network Switch and a Cable Reel join the trophies, and a Satellite Dish the spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room, furnished with Server Racks (loot on their shelves), Computers and Fax Machines: the Computer and the Fax Machine are the only line to the IT Department (docs/story/STORY.md, "The IT Department's fax"). Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). Loot rolls from the `it` table: circuit boards, wiring, controls and readouts up to tier 3, Fax Machines, Computers and Server Racks, common and uncommon weapon parts. Every IT shape ships its own seeds. **Signature**: marks, mast or dish (Satellite Dish, Wi-Fi Mast, Router Antenna) and office gear (monitors, switches, reels, plants, fans, plaques, racks, computers, fax machines); modules enclosed and cab-over cabs, short and flat noses, bare flanks. Weak points: the antenna (two sides, two bearings), two tyres, the fuel.

**More dressing (`Vehicle.Kit`).** A style with a `"mast"` slot gets a last pass of its own (its own fork, so nothing else of a seed moves, and a style without the slot never rolls it): masts on the rooms' roofs and the cab, spikes down the rooms' outer walls, and its `"gear"` standing on open beds and porches. The IT style's masts are dishes and antennas, its gear reels, switches, monitors and dishes. The dressing rolls how many; the guaranteed marks pass ("Identity and weak points") then puts a mast and a piece of office gear on any kit-2 vehicle still without one, on a post at the back when the roof went to turrets.

### The Call Centre style

The Call Centre (`Styles.Scam`, group "The Call Centre"): the Scammers (docs/story/STORY.md, "The Scammers") on wheels, a parody of the scam call centre. Cubicle-fabric greys on the panels, corporate teal on the hoods, war paint in WINNER yellow, hot pink and dollar green. It starts from the IT style's slots and swaps the dressing: `Source/PartKit/Generators/CallCentreParts.cs` writes `Constructs/Parts/CallCentre`: an Office Phone, a Call Headset on a stand, a GIFT CARDS sign and a Prize Wheel as trophies and gear, Loudhailers down the flanks as spikes, Phone Masts on the roofs. Rooms are a call floor (rows of Cubicle Desks round a Water Cooler), the manager's office (a Computer, a cubicle, a Server Rack) or a break room; each Cubicle Desk has a drawer of loot. Guns are named for the job: the Cold Caller (a carbine), Robocall (a machine gun) and Final Notice (a pump gun). They run on other people's batteries, so loot rolls from the `scam` table: batteries, circuit boards and wiring up to tier 3, the odd Computer, common and uncommon weapon parts. Every shape ships its own seeds (the sweep's most different passing ones). **Signature**: marks, phone mast or loudhailer and call centre gear (phones, headsets, GIFT CARDS signs, the prize wheel, cubicles, the water cooler); modules as IT's. As for IT, the guaranteed marks pass ("Identity and weak points") gives every kit-2 vehicle a Phone Mast or a Loudhailer when its dressing didn't. Weak points: the antenna (a mast or loudhailer), two tyres, the fuel.

### The Highland style

The Clan MacSlop (`Styles.Highland`, group "Clan MacSlop"), a Highland clan on wheels and the loudest faction on the map. Tartan reds, hunting green, navy and tweed on the panels, Saltire blue and white on the hoods, war paint in pillar-box red, lion-rampant yellow, white and black; plates a little crooked (well loved, not neglected). The part kit's `--gen-parts highland_parts` (Source/PartKit/Generators/HighlandParts.cs) writes its parts (`Constructs/Parts/Highland`): Saltire Flags on the roofs (masts), Thistles, a Haggis on a Post and a Coo Heid as trophies, Wee Cabers down the flanks as spikes, Wee Kegs on the beds, and Whisky Casks in the rooms (two loot slots each). Rooms are a bothy, a distillery, a ceilidh hall or a kit room. Guns are named for the glen: the Claymore (a pump gun), the Sgian Dubh (a carbine), the Highland Charge (a machine gun) and the Caber Toss (the harpoon). Loot rolls from the `highland` table: tier 1 and 2 drive parts, pump guns and carbines, and the odd Pipe Box.

**The Pipe Box (`Vehicle.Piper`).** A style with a `"piper"` slot stands exactly one on every vehicle with a seat, before the trophies take the sockets: on the hood, else the cab, a room's roof, the bed, a porch, the headboard or a flank. Its own fork, so nothing else of a seed moves, and a seed with nowhere to stand one throws, so the sweep skips it. A trailer has no seat and gets none. The box strikes up the pipes while someone drives (docs/sandbox/MEDIA.md, "Pipe Boxes"), so you hear the clan coming. **Signature**: marks, Pipe Box (the `piper` pass above) and clan dressing (Saltires, thistles, the coo's heid, haggis, wee cabers, kegs, casks); modules long hoods and rams, caged cabs, panniers. Weak points: the Pipe Box (shoot it and the pipes stop), two tyres, the fuel.

### The British style

The Ministry of Slop (`Styles.British`, group "Ministry of Slop"), a very British faction, affectionate and mostly understated rather than on the nose. Bus red on most panels with the odd racing green, cream or wet-Tuesday grey; racing green, cream and navy on the hoods; war paint in the flag's blue, red and white and double-yellow-line yellow; plates neat but not new. `tools/constructs/british_parts.py` writes its parts (`Constructs/Parts/British`): Union Flags, Belisha beacons and rain gauges on the roofs (masts), a Teapot in its cosy, a Traffic Cone nobody will move, a Garden Gnome and a Rain Gauge as trophies, Furled Brollies down the flanks as spikes (it's that sort of day), Queue Posts on the beds and porches (gear), and in the rooms a Pillar Box (two loot slots) and a cut-down red Phone Box (loot under the shelf) round a Tea Urn. Rooms are a tea room, a post office, lost property or a snug. Guns are named the way the British complain: Terribly Sorry (a machine gun), Bit of Bother (a carbine), the Strongly Worded Letter (a pump gun) and Builder's Tea (the flamer). Loot rolls from the `british` table: tier 1 and 2 drive parts, carbines and pump guns. **Signature**: marks, flags, beacons or brollies (Union Flag, Belisha Beacon, Furled Brolly, Rain Gauge) and tea, cones or a queue (teapot, cone, gnome, queue post, pillar box, phone box, tea urn); modules enclosed cabs, long hoods, bare flanks. Weak points: two tyres, the fuel, and a flag or beacon when it carries one. A Scrap Tanker has no room and often little to dress, so it used to go without both marks; the guaranteed marks pass ("Identity and weak points") now stands a Belisha Beacon, a Union Flag or a Rain Gauge, and a teapot, cone or queue post, on its cab or a post, so every kit-2 British vehicle carries both.

**The Double Decker (`Generators/DoubleDecker.cs`).** An open-top double-decker bus: a long walk-in hull below (side door, loot, the seats that work the guns) and a railed top deck on the tall bays' roofs, with the two roof guns standing up through holes in it. The deck is laid from the bays' `top` sockets (each sits on a deck cell's centre), stops where the tail slopes down (a stepped roof), and a ladder hangs from its back edge, else down the right side. It is an **extra shape**: `VehicleRegistry.Extras` holds shapes a style ships only when it names them in `FactionStyle.Extra` (`Extra = ["Double Decker"]`), so no other faction's committed files change; any style can still run one by name (`--gen-vehicle "Bandit Double Decker" --out DIR`).

**The Car (`Generators/Car.cs`, phase 8a).** An extra shape made by the grammar's `"Car"` recipe only: a lofted cab in the style's archetype (BODY-STYLES.md, section 16: Bandit's coke-bottle and seventies wedge, Highland's early fastback, IT's supercar, British and Scam's hatch and estate), its hood and (phase 8a part 2, section 17) a car body: two seats, or a crew cabin over a second row with the archetype's fastback over the whole cabin, then a boot, a lofted tail deck (a hood in the cabin's language turned round) over a Fuel Tank hung from a sill, the gun on a pedestal between the cabin and the boot with its seat facing it. Bandit, IT, Scam, Highland and British ship it (`Extra`). Phase 9a (section 19): an archetype may name its own body plans (`BodyArchetype.Bodies`): Bandit's coke-bottle and wedge cars are cut down to a pickup (a gun bay and an open bed) more often than not, Highland's fastback is a two-seat coupe, IT's supercar has a long tail deck, the wedge and the estate a long four-door cabin.

**Phase 8b's shapes (BODY-STYLES.md, section 18).** Five more extra shapes made by grammar recipes only, each always in one of its style's archetypes:
the **Buggy** (`"Buggy"`: a caged cab, a gun bay behind it, the drum on a porch; low gear on arms or hydraulics), the **Monster Truck** (`"Monster Truck"`: a gun bay
and a short bed on the `monster` gear, Heavy Pogo Struts and Monster Wheels, which no other recipe takes: `ShapeGrammar.SpecialGears`), the **Wagon** (`"Wagon"`: a
room, a bed behind it now and then: a war wagon or a carriage), the **Giant Truck** (`"Giant Truck"`: a long room at the giant scale class, on two or three axles) and the
**Concept** (`"Concept"`: the car body with two seats only: a bubble car or a streamliner).

@@SHIPPED@@
