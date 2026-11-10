# The vehicle kit (Source/VehicleKit)

A vehicle generator is C# code inside the game assembly that fills a `Vehicle` and writes an ordinary blueprint file. It exists so an agent can make vehicles for a faction
without working out sockets by hand, in the same way the map kit (docs/mapkit/MAPKIT.md) lets it make maps. Everything goes through the game's own rules as it is added, and the
result is **checked by the real game** before anyone drives it: the file goes back through the blueprint loader, and the real physics and weapons drive, turn and fire it.

Namespace `CallOfSlop.VehicleKit`. The committed files are what reviews, tests and the menu's tiles use: the game loads `Constructs/Blueprints/*.json`. A server can also run a
generator while the game plays, for a seed nobody committed ("Dynamic spawn" below). Either way the vehicle is an ordinary construct, spawned, driven, shot and salvaged part by part
like any other. **Fix the generator, not its files.** No AI is in here yet: each generated vehicle carries the data one will need ("Behaviour data").

## Commands

| Command | What it does |
| --- | --- |
| `CallOfSlop --gen-vehicle "Bandit Technical" [--seed S] [--out DIR] [--plan]` | Runs a generator (one seed, or every seed it ships), writes `Constructs/Blueprints/<Name><seed>.json` in the repository when the game runs from a checkout, else DIR, and prints the check. Each seed runs twice and must write the same text. `--plan` draws a PNG next to the exe. Exit code: how many seeds failed. |
| `CallOfSlop --gen-vehicles [--out DIR] [--only FILE,FILE] [--order reverse\|shuffle:N] [--between]` | Every shipped seed against its committed file: `identical`, `DIFFERS` or `new`. `--only` some files, `--order` in another order, `--between` runs the full check (with its tow) or a turret-seat count between seeds: a seed's text must not depend on what ran before it. Exit code: how many aren't identical. |
| `CallOfSlop --sweep-vehicle "Bandit War Bus" [--seeds N]` | Seeds 1 to N (default 30) built and checked, nothing written: how often a shape fails, on what, and what a generation costs. Run it before calling a shape done: a shipped seed proves only itself. It also compares seeds (`VehicleVariety`: kinds of part, plate and beam materials not counted, plus bounding shape): it prints the most different seeds it found, and fails when two shipped seeds are closer than 0.25. Choose `Seeds` from that line. |
| `CallOfSlop --diversity "Bandit Box Truck" [--seeds N]`, `--diversity-style Bandit`, `--kit-report`, `--vehicle-sheet "Bandit Box Truck"`, `--grade-vehicle "Bandit Box Truck 7" 4 "note"`, `--grade-report` | The quality tools: how alike seeds are, what never varies, what fails and what it costs, contact sheets and grades. See "Quality tools". |
| `CallOfSlop --identity "IT War Bus" [--seeds N] [--shipped] [--raw]`, or `--identity IT` for every shape of a style | How much each seed reads as its faction (0 to 1) with the breakdown, its weak points, the style it reads as best after its own, then the mean, the weak-point pass rate and every vehicle that reads more as another style. Default 6 seeds; `--shipped`: the committed seeds. See "Identity and weak points". |
| `... --no-identity-gate` on any of these | Kit 2 without its identity re-plan, to measure what the check changes. |
| `... --kit 1`, `--kit 2` or `--kit 3` on any of these | Every seed by that kit (see "Shape grammar" and "Layered variety"), to compare them: without it a seed is made by its own kit. `--gen-vehicles --kit 2` reports the committed files as differing, as it should. |
| `... --kit 5 [--language NAME] [--roofline NAME] [--scale NAME]`, `--body-report STYLE\|all [--seeds N]`, `--no-sections` | Kit 5, the current kit since 2026-11-07: rooms lofted from run-time hull bays (`HullLoft`) in the style's body language, with phase 2's scale classes, proportions, rooflines, doors either side or in the back and paint runs (docs/vehiclekit/BODY-STYLES.md, sections 7 and 8). `--language` puts every room in one language, `--roofline` and `--scale` every room on one line or at one class; `--body-report` measures rooms' cross-sections within and between styles (and counts coplanar faces inside lofted bays); `--no-sections` measures diversity and identity without their front-profile terms, as they were before. `--kit 3` and `--kit 4` still make the fixed-bay kits. Phase 3 (section 9): cabs and hoods lofted in the same language; `--cab KIND` puts every cab of that kind where the plan allows (enclosed, crew, caged, armoured, cab-over, merged), `--fixed-cabs` makes kit-5 bodies with phase 2's fixed cab shells and hoods (to measure against), and `--body-report` also measures cabs and hoods between styles. Phase 4 (section 10): each faction's trim on its rooms' bay sides and fittings on its cabs and hoods; `--no-trim` makes kit-5 bodies without them, and `--body-report` also measures room exteriors (front profile with what is on them, and relief by height) and whole bodies (front and side outline) between styles. Phase 5 (section 11): new spaces: caged rooms (bars you see and shoot through), upper storeys over a long room's middle (an enclosed saloon or an open deck behind a bulwark) reached by a stair through a hatch, and side pods; `--cage` cages every room, `--upper saloon\|deck\|none` puts that storey where the plan allows, `--no-spaces` (or `--no-cages`, `--no-uppers`, `--no-pods`) leaves them out to measure against; the check holds a player in a caged room or on an open deck, walks a body up the stair, and checks the storey as a room. Phase 6 (section 12): balconies outside a room's door (a railed platform at floor level on the floor's edge socket, the ladder at its open end; each faction's rail) and lopsided vehicles (pods, the far side's trim and the spikes on one side), both rates in the style's rule; `--no-balconies` and `--no-asym` leave them out to measure against; the check stands a player on a balcony, walks them out onto it through the door and holds them in by its rail, and `--body-report` tallies balconies and lopsided vehicles. Phase 7a (section 13): interiors: a ground-floor bay's lining in its faction's look (bare ribs and welds, olive or pale or showy panels, timber, moquette and panelling), following the hull or inset as the style's rule says, with a lamp a bay; a dash in enclosed, crew, armoured and caged cabs; role gear, layout repeats and faction props by the style's `Gear`, `Fill`, `Clutter` and `Props`; `--no-interiors` leaves them out to measure against. Phase 7b (section 14): windows cut in lined bays' side walls by the faction's rule, glazed by pane parts on the bays' `win` sockets, and lamps that light the room through the dynamic lights (unlit maps too); `--body-report` counts lamps and panes a vehicle. Phase 8a (section 16): archetypes (muscle cars by era, a supercar, a European hatch and estate, a minibus, a rigid box truck and a curtain-sider) laid over a faction's body rule for one vehicle, and the Car shape (phase 8b, section 18: buggies, monster trucks, war wagons and carriages, bubble cars and streamliners, giant trucks, and their shapes); `--archetype NAME` puts every vehicle of a shape it takes in that archetype, `--no-archetypes` leaves them out to measure against. Phase 7c (section 15): upper storeys lined, glazed and furnished (an open deck's bulwark lined), closed ends lined, a console in capsule and merged cabs and a steering wheel in every lined cab, props on furniture tops when the floor is full. Phase 9a (section 19): each faction's cab sub-styles (its cab family, laid over any archetype), each car archetype's own body plan (a faction's car outline), per-faction fenders, Militia's slats on every side; `--no-cab-styles` leaves the sub-styles out to measure against. |
| `... --layers chassis=5,wear=9` on any of these | Kit 3's layer seeds on every vehicle made: the seed's own vehicle with those layers rolled from other seeds ("Layered variety"). `--gen-vehicle` then writes `BanditBoxTruck7chassis5wear9.json`, never a committed seed's file. |
| `... --vary wear [--base S]` on any of these | The seeds a tool loops over become one layer's seeds on base seed S (default 1): `--diversity "Bandit Box Truck" --vary chassis --base 5 --seeds 8` is seed 5's body and kit on eight chassis. |
| `CallOfSlop --role "Bandit raider" --tier 2 [--seeds N] [--check] [--sliced]`, `--kit-report --roles` | Kit 4: vehicles for a role and tier, measured against it, and the matrix of every style, role and tier. See "Roles and tiers". |
| `CallOfSlop --family "Bandit raider" [--seed S] [--tier T] [--check]` | A model line: its Mk1 to Mk3, its variants, a named unique and two other families of the role, with their distances; the family must read closer to itself than to the others. See "Families and convoys". |
| `CallOfSlop --convoy Bandit [--seed S] [--count N] [--tier T] [--mix raider,tanker] [--no-check] [--write DIR]` | A convoy's plan (members, roles, models, the paint scheme, where each stands), each member made and given the full check, the leader made twice. See "Families and convoys". |
| `CallOfSlop --context "Bandit Box Truck"` (or `"Bandit raider" --tier 2`, or a model's key) `--biome desert\|snow\|swamp\|forest\|road --gravity G --air 0\|1 --wood W --ore O [--seeds N] [--from S] [--check]`, or `--sweep` for every context | What a spawn context changes against neutral, seed by seed: what each lean did, the parts and mass that differ, the identity, and the full check with `--check`; a model's line says whether its body and chassis stayed its family's. See "Spawn context". |
| `CallOfSlop --brief "Bandit Box Truck" [--seed S]`, `--brief "Bandit raider" [--tier T] [--seed S]`, `--brief MODEL_KEY` or `--brief FILE` | The behaviour brief of a vehicle made as the spawn makes it (or of any blueprint file): driving, guns and their arcs, weak points, crew, loot, and that its save bytes read back the same. See "Behaviour data". |
| `CallOfSlop --cargo "Bandit gun platform" [--tier T \| --tiers 1,4] [--seeds N] [--list] [--write DIR] [--no-cargo]`, or `--cargo all [--style S] [--role R]` | Kit 4's cargo: what each role vehicle carries for itself (by kind, in slots and loose, the loose mass), its role, drive pulse and identity, what was left behind and why, and the totals; `--no-cargo` makes the same seeds without the pass (also on any other tool). See "Cargo and spares". |
| `CallOfSlop --drive-check FILE [--gravity G]` | The spawn's drive check on any blueprint: level, and a throttle pulse moves it. `--no-drive-gate` on any of the tools turns the check off in generation, to measure it. See "Dynamic spawn", "The drive check". |
| `CallOfSlop --check-armoury [--recipe ID]`, `--armoury-report [--seeds N] [--style S] [--shape NAME] [--check]`, `--no-armoury` on any tool | The armoury's recipes fitted and checked one by one, and the guns each style's vehicles carry over seeds. See "The armoury". |
| `CallOfSlop --check-vehicle FILE` | The same check on any blueprint file, hand-made or generated (a generator's own seed also gets its rooms walked). Handy as a baseline: the shipped Buggy drifts about 3 degrees left on the same run, and every technical 5 to 10, so the drift limit is 12. |

The console goes to `callofslop.log` next to the exe. A report reads

```
Bandit Technical 7: OK; 90 parts from 90 steps, 2 freeform welds, 6 links, 3 cargo, 1193 kg, drive 94.5 m at 24.4 m/s yaw -6.2 deg, turn 197 deg, worst tilt 2.2 deg, stability 0.85, 10 shots
  fuel 60.0 l; power: no electrics
  wheels: 4, 2 driven, 2 steered; wheelbase 3.13 m
  centre of mass 0.95 m up, 0.02 m off the middle, 53% of the way back along the wheelbase
  coplanar faces between parts: 0 among 315 boxes
  guns: 1, seats Driver, Gunner
  the Mounted MG bears within 2.2 degrees of the gunner's look (sides, behind, up), 0.4 off the way it is laid
```

and every `FAIL` line says what went wrong and, where it can, what to do. What it checks (`VehicleChecks`, defaults in `VehicleCheckOptions`; a generator's `Checks` may change them):
it builds from the file with no refusal; a working powertrain, a seat that drives through a Driving Module and fuel piped to the engine; driven and steered wheels; the centre of mass inside the
wheels and its stability (half the track over its height); no coplanar faces between parts, in their paint (wheels and what's on joints don't count: they don't stay at rest); part count and mass; it
stands level and stays in one piece; full throttle for 6 s through the seat (distance, heading drift, that forward is forward); a full-right turn at speed (that right is right, and it doesn't roll);
every gun has a load (a flame gun drinks the construct's fuel), joints and a gunner, fires on Fire in that seat and follows the gunner's look round the sides, behind and up (since phase 9b: its bearing, read level, within 30 degrees of the look's, and its line within 12 degrees of the way the game lays it each tick, `ConstructWeapons.LaidWay`, the very call the gunner's seat makes; a shell gun laid for range stands 40 degrees and more over a level look by design; before that, judged against `ConstructWeapons.AimWay`: at the point the gunner looks at, out to the gun's reach, raised for a slow round's drop, within 12 degrees; a short gun mounted off its gunner's eye, or a slow round's lead, stands off the look's own line by design, so that line is only held to 30 degrees. Before 2026-11-08 the look's line was the measure, at 12 and 20 degrees, which failed a roof Firebreather on British War Bus 6 at 25 degrees while it was laid on its point to 0.0, and every slow-round Kingpin gun at 14); every cargo piece rides in a frozen volume and is still there after the drive; six spawns' loot rolls differ, and something rolled.

**Rooms** (`Vehicle.Rooms`, given to the check by the generator): headroom against a 1.8 m player; the real walker stands on the floor, walks towards the far wall, and goes out by the door; the seats and stores are near the aisle; the report gives the door sill's height and whether a ladder is under it. A player walking out into a door leaf swings it (`BlockPlayer`), so a shut door doesn't count as stopping anyone. Coplanar faces under 6 cm2 count as slivers, not failures: sockets land on a 1 cm grid, so a quarter plate's edge can sit in a wall's end.

A check covers a flat floor and a few simulated seconds, that's all. It doesn't try rough ground, climbing the ladder, a client over the network, or how the thing looks: a person reviews that in game.

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

## Shape grammar

Kit 1 made each shape with one generator: a Box Truck was always a hull on a chassis with the same nose, room and tail in the same places, so seeds differed in parts
and hardly at all in outline (Bandit Box Truck, November 2026: silhouette 0.057 of the distance against 0.39 for parts). **Kit 2** (`VehicleGrammar.cs`) assembles the
heavy shapes from **modules** instead, so the outline varies with the seed while every part still comes from the style.

**Which kit.** `VehicleKitVersion.For(generator, seed)`: `Force` (`--kit N`), else kit 1 for a shape with no recipe (the Fuel Trailer
and any new generator; the Double Decker is the grammar's since body styles phase 5, the Technical and the Tanker Rig since phase 9b, BODY-STYLES.md section 20) or a seed in `VehicleKitVersion.Frozen`, else the current kit: **kit 5 since 2026-11-07** (the user's decision), kit 3's
grammar and layers (or kit 4's role) with its rooms lofted in the style's body language ("Body styles" in BODY-STYLES.md). Every committed seed of a shape with a
recipe was made again by kit 5 that day (continuity wasn't required: a saved world holds built constructs, not seeds), with new seeds picked from each shape's
sweep, so `Frozen` is empty and `--gen-vehicles` reports every file identical. Kits 1 to 3 still make what they made (`--kit 2` byte for byte). A kit file says
so in a comment after its first brace (`// kit 5; body room+bed, cab caged, ..., language slab 2.21 m, ...`; blueprints allow comments), `Vehicle.Kit` and
`Vehicle.Modules` hold the same, and the spawn logs it. A seed committed from now on is made by the kit current when it was committed: grade and commit from a
sweep of that kit; when a later kit changes a committed seed, either the shapes are committed again or the seeds go into `Frozen` with their kit. A role's,
family's, convoy's or context's vehicle is kit 5 too (kit 4's role on lofted rooms); `--kit 4` makes it with fixed bays.
Phase 2 (BODY-STYLES.md section 7) made the committed seeds again the same day; a room's door may now be on either side or in its back (`Room.DoorFace`
says which: anything that walks to the door or keeps out of its way reads it, never assumes +x), its bays run 0.75 to 1.5 m (read a bay's length from its
key, `HullLoft.ShapeOf`, not its bounds: caps stand past it), and its turret and gear spots are judged by distance along the room with sizes from the parts
(`BaySpots.Turret`).

**Modules** (`ShapeGrammar.Catalogue`). Each has a family, a name, the style parts it is made of (a module whose parts the style doesn't list is never made), and its
connection rules as tags: what it needs, what it provides, what it can't stand beside, and the gear recipes it allows.

| Family | Modules | Rules |
| --- | --- | --- |
| cab | caged, enclosed, armoured, cab-over; kit 5 also crew, merged | from the style's `heavyCab` list; kit 5 (lofted) from `BodyStyleRule.Cabs` instead, whatever the list; crew needs a metre of bare rails first, merged a room first; cab-over and merged provide "cabover" |
| nose | long hood, short hood, plated hood, ram, flat | from the style's `hood` list (ram: its `nose` list's Ram Plow or Bull Bar on any hood); flat needs a cab-over, the rest exclude it |
| body | room, bed, gun bed, hutch, gun bay, tank, gap | segments front to back (below); a tank allows only the Scrap Tanker's gear |
| rear | porch, porch hitch, gun deck, hitch, bare | porch carries the drum; a gun deck needs a room last; hitch and bare need fuel or a drum carried ahead |
| under | stilts, tub, skirted | tub and skirted need a high floor (a low one is always on stilts); skirted needs a room; tub can't go round a tag or tandem axle |
| flank | bare, panniers, loaded | a room's waists |
| sides | walls, rails, open | a bed's edges; walls allow only the Flatbed Fort's gear (they hang under the floor's edge) |
| roof | none, rack | a rack needs a room with a metre bay: deck plates and tube rails over two flat tall bays side by side |

**Body segments** run back from behind the cab (z 0.2): a **room** is a hull of 3.5 m and up (brow, metre bays, the 1.5 m door bay, tail) whose bays the seed walks
between tall, low and narrow, with its roofline changing at least once (twice from three metre bays), the door bay tall; a **bed** (gun bed: with a deck gun) an open
deck in half metres; a **hutch** the Gun Wagon's deck; a **gun bay** the Scrap Tanker's gun on its own sill; a **tank** its barrel on cradles; a **gap** bare rails between
the cab and the body. The rear axle stands 1.25 to 2.25 m in from the body's end, by the seed.

**Recipes** (`ShapeGrammar.Recipes`): a shape name is a set of body patterns, rears, a gun count and gear. The Box Truck is a room, sometimes behind a gap, with an open
bed or a gun bed behind it or a gun bed ahead of it; the War Bus a long room (5.5 to 7.5 m); the Flatbed Fort a gun bed and a bunker either way round; the Gun Wagon a
hutch, a gun bay and a gun bed, or a crew room and a gun bed; the Scrap Tanker a gun bay or a crew room before its tank. So the spawn API's and the committed files'
shape names all still work.

**What the assembler keeps to.** One fuel tank feeds the engine (an engine has one inlet): a tank segment, else a drum on a bed, the hutch or the gun deck, else a porch.
Guns go where their mounts clear: deck guns half a metre from the stilts and clear of every axle, a gun behind a room faces aft (never a Whaler, whose magazine is
where the seat goes); roof turrets only on flat tall bays (on a sloped one the arm runs into the next bay), a flame gun never on a sponson (it doesn't follow the look
round), and a long gun on a low mount near the front not at all (it would want a pedestal). Only a room's guns are fed from its magazine, worked from its seats: a belt
from the room to a bed gun would cross the room where a player walks; the others carry their own box, a hutch's gun its feed. A short room has one seat and its loot
against the end wall first (`Vehicle.EndChest`), never in the middle, where the check stands a player, and Trim's crooked patches stay off the hull bays (they would land
inside). Finally the built body is measured as the check measures it: stability under 0.63 or a side or plan fill within half a point of `MaxSideFill`/`MaxTopFill`
throws, and so does anything the game refuses; then **the seed plans again** from the next fork (`"grammar 1"`...), gear included, up to four times. A retry shows in the
kit line as "plan 2". A plan that builds is then scored for its faction ("Identity and weak points"): under the style's threshold, or missing one of its marks, it is
planned again once, and the better of the two is kept.

**Seeds.** All of it rolls from forks of `v.Dice` ("grammar", "grammar build", by attempt); the module bodies use the same forks kit 1's code does ("deck bed", "hood",
"weapon"...), so a seed is the same text on every machine. Generation yields where `BlueprintScript` does. Kit 3 splits these forks into layers ("Layered variety").

**Measured** (2-core box, generation only unless said; kit 1 is `--kit 1` or the committed seeds' state before the change):

| | kit 1 | kit 2 |
| --- | --- | --- |
| Bandit Box Truck, seeds 1-20: silhouette part / nearest neighbour / mean pair | 0.057 / 0.066 / 0.162 | 0.116 / 0.128 / 0.222 |
| Bandit Box Truck: seeds closer than 0.08 | 14 in 6 groups | 2 in 1 group |
| Highland Box Truck, seeds 1-20: silhouette / nearest neighbour | 0.051 / 0.076 | 0.103 / 0.120 |
| Bandit, every shape, 6 seeds each: nearest neighbour | 0.112 | 0.134 |
| Highland, every shape: nearest neighbour; Box Truck and War Bus apart | 0.103; 0.212 | 0.126; 0.230 |
| Generation failures, Bandit and Highland, 10 seeds of each recipe shape | 1 of 100 | 0 of 100 (with retries) |
| Full check (`--sweep-vehicle`), Bandit Box Truck / War Bus / Flatbed Fort | 8 of 12 / 4 of 10 / 2 of 10 | 12 of 12 / 12 of 12 / 10 of 10 |
| Generation, mean (worst) per seed, Bandit | 247 ms (652) | 242 ms (1196, a retry) |

Kit 1's check failures (a chest in a short room's middle, a crooked patch inside a hull, a boxy bus) are what the grammar's rules above avoid; kit 1's committed seeds
passed, and its code is unchanged. The Silhouette rules are the grammar's rules: separate masses (a cab, a gap, a room, a bed or a tank, each its own height), a roofline
that steps, wheels in the open, a room only for a crew and loot.

**Adding to it.** A module is a row in `Catalogue` plus its body in `ShapeGrammar.Build` (reuse the kit-1 generator's code for it, put where the plan says); a recipe is a
row in `Recipes`. Sweep the shapes it can appear in with `--sweep-vehicle "Bandit Box Truck" --seeds 12 --kit 2` (the full check) and measure with `--diversity ... --kit 2`.

## Layered variety

The user's reviews of kit 2: "all use about the same wheelbase and suspension", "still too boxy", "interiors are too alike". **Kit 3** (`VehicleKitVersion.Layered`, the current
kit) is kit 2's grammar with its variety in four independent layers, each rolled from a root of its own (`Vehicle.UseLayers`, `Dice.Routed`), so one can be kept while
another rerolls:

| Layer | What it decides | Where |
| --- | --- | --- |
| chassis | the corners (spring and wheel at the front, at the rear with its differential and the floor height it allows), wheel and spring swaps within a class, the axle layout (one rear axle, a tag, a pusher, a driven tandem, at 1.25 or 1.5 m), where the front axle stands, how far in from the body's end the rear one is, the front track, what the floor stands on (stilts, tub, skirts) | `VehicleChassis.cs`, `LayerTables.Chassis` |
| body | the body pattern and segment lengths, cab, nose, rear, bed sides, roof, the room's bays (heights, steps, rake), the decks' and walls' plates, the hood, the ladder, paint | `ShapeGrammar.Plan`, `LayerTables.Body` |
| kit | the guns (how many, where, which), the flanks' panniers, the room's furniture and chest, the ammo, ram bars, spikes, trophies, masts, the Pipe Box, a passenger | `ShapeGrammar.Guns`, `LayerTables` has no kit table: the style's own lists |
| wear | an age; panels gone over to the style's rust, low panels dark with road dirt (the frame colour), soot round guns and the exhaust (the palette's darkest), patch repairs welded over outer panels, an open deck's edge rusted out; the style's crooked plates (Trim's "crooked" fork) | `VehicleWear.cs`, `LayerTables.Wear` |

**How a fork finds its layer.** `VehicleLayers.LayerOf` maps a root fork's topic to a layer: "chassis", "gear", "axles", "drive" are the chassis's; "wear", "crooked" the wear's;
"grammar kit", "weapon", "rounds", "ram", "show", "kit", "piper", "furnish", "end chest", "cargo", "crew", "panniers" the kit's; anything else the body's. So `Vehicle`'s
own methods (`Deck` forks "deck bed", `Turret` "weapon 2", `Trim` "paint") roll from the right layer without knowing about layers. A new fork in a kit-3 path picks its
layer by its name: name it for what it decides. A layer left unset takes the seed, and a root of the seed's own number forks exactly as the plain dice would.

**The API.** `VehicleLayers(Chassis, Body, Kit, Wear)`, each an optional seed. `VehicleRegistry.Make(g, seed, layers: new(Wear: 99))` is seed 7's truck with seed 99's wear;
`new(Chassis: 42)` the same body on another chassis. `game.VehicleSpawn.Request(style, shape, seed, ground, yaw, ..., layers: new(Wear: 99))` does the same in game
(nothing on the wire: only the server generates). A vehicle with overrides is named for them, "Bandit Box Truck 7 [wear 99]", so it never reads as a committed seed, and
`Vehicle.Layers`, `VehicleGenJob.Layers` and the kit line carry every layer's seed: `// kit 3; seeds chassis 42 body 7 kit 7 wear 7, chassis 3 axles, tandem 1.5 m, front
under the hood, inset 1 m, coil/hydraulic, wing 0.5, wear age 0.64 (11 rusty, 15 dirty, 0 sooty, 1 patched, 1 rusted out), body room, ...`. Only the grammar's shapes
have layers: the Technical, the trailers and the Tanker Rig refuse them (kit 1; the Double Decker has a recipe since body styles phase 5), and so does a request for them.

**What "independent" means.** Each layer's dice are its own, but what it builds sits on the others: a new chassis moves the rear axle, so the guns' spots clear of it can move
(kit decides which gun, the axles where it may stand), and a different kit puts panniers on, so the wear's rust lands on a different set of panels. Measured on Bandit Box
Truck 5 with each layer varied over 8 seeds (`--vary`, mean pair distance): wear 0.000 (it moved no part and no trait, only paint, patches and deck edges, which the
distance doesn't count); kit 0.068 (only the guns, stores and dressing: length, wheelbase, track, suspension, body and cab never varied); chassis 0.099 (wheelbase and
track 4 and 3 values, 4 kinds of suspension; length, body, cab and guns never varied); body 0.191 (outline, length 6 values, cab 4; the axle layout 7 of 8 the same, and
the suspension follows the body where a body module limits the gear, as a walled bed or a tank does).

**Ranges.** `LayerTables` (`VehicleLayers.cs`) holds a table per layer by style name, so `Styles.cs` is untouched and a style not named (Militia) gets the defaults:

- `ChassisRange`: the rear axle's inset (quarter metres, kit 2: 1.25 to 2.25 in halves), the front axle's stations and their weights (-1.675 under the hood's back, kit 2's;
  -1.925 forward; -0.925 set back beside the cab), the front wing (0.5 or 0.75 m), the axle layouts' weights (second axles weigh 0.3 times under a 4 m body up to twice
  under a 9 m one), their spacings, and the chances of mixing recipes' corners, of a wheel or spring swap, and of a dead tag axle on Rigid Mounts. A cab-over keeps kit 2's
  front axle; a moved front axle keeps kit 2's track and leaves the hood's fenders off (`Vehicle.NoFenders`), which stand where kit 2's wheels are.
- `BodyRange`: how segment lengths lean in the recipe's range, the room sections' height weights (tall, low, narrow), how often the roofline steps, how often a brow and
  a tail are raked down. The defaults are kit 2's rolls.
- `WearProfile`: the shares of rusty, dirty and sooty panels, patch repairs, rusted-out deck edges, and the age range that scales them. Bandit heavy, the IT Department
  light, the Clan "well loved" (mud and mended, little rust), the Ministry and the Call Centre between.

The corners come from the style's own `HeavyGear`: a chassis takes one of its recipes, then (by the table's odds) the front or the rear of another recipe (the rear with its
differential and floor height: a swing arm's throw is what lets a floor sit low), a wheel of the same class (at the rear under a body, never a bigger one: a wheel's top meets
the walls hung under the floor) and a spring of the same mount (Leaf Spring, Heavy Suspension, Hydraulic Suspension; Double Wishbone, Heavy Double Wishbone; Swing Arm,
Heavy Swing Arm). A second axle needs a high floor on a wide differential and the shape's own gear list to allow it ("tandem" for tag and pusher axles, "six" for a driven
tandem), so a Flatbed Fort's walled bed or a tanker keeps to what kit 2 let them have. A pusher that would stand under the cab goes behind instead. "Walking beam" and "bogie"
suspensions have no part of a size for these vehicles (the Bogie parts are tracks a metre and a half across): a close tandem stands for a bogie. "None" is the dead axle.

**Wear and the checks.** Wear never touches a room's hull (rays out of a room find no new hole), a fuel tank, a tyre, a gun, the frame (stacked beams of one colour don't
fight) or anything with slots, and its paint is only ever one of the style's own colours, because the identity score counts a painted part as the style's only when its colour
is in the palette: a mixed "rusty beige" read as foreign paint and cost the Bandit Box Truck 0.04 of identity before it was changed. So for every style but the Bandits, whose
"rust" role is rust, rust shows as their panel colours and the wear reads mostly as dirt, soot, patches and missing plates. A patch is a freeform weld, which the build rules
don't check, so `Vehicle.ClearOf` tests its box against every part's bounds first. Deck cells only rust out on an open deck's edge (bed, porch, rack, plated hood) and never
beside a hole or a whole plate. **Not done:** bent trim. (Kit 5's lofted cabs carry their windscreens and back windows as glass parts of their own since body styles phase 3, so a shot breaks the glass, not the cab.)

**Two fixes the wider ranges needed.** A driven tandem under a long kit-3 wheelbase ran its laid shaft more than 8 m in one segment, which the game refuses: past 7.9 m the
shaft takes a node halfway (`Vehicle.Powertrain`; shorter shafts are laid as before, so kit 1 and 2 are unchanged). And a kit-3 plan must measure a point under the side
and plan fill limits, not half a point: one built at 62.0 % after measuring 61.6 %.

**Measured** (2-core box shared with other agents, load 3 to 8, so times are rough; `--kit 2` against `--kit 3` on the same build, which makes kit 2 byte for byte as before):

| | kit 2 | kit 3 |
| --- | --- | --- |
| Bandit Box Truck, seeds 1-20: mean pair / nearest neighbour / least | 0.222 / 0.128 / 0.074 | 0.238 / 0.179 / 0.157 |
| the same: layout part / silhouette part; seeds closer than 0.08 | 0.266 / 0.116; 2 | 0.306 / 0.118; none |
| the same, traits: wheelbase / wheelbase share of length / track / wheel sets / suspension kinds (values) | 8 / 7 / 3 / 4 / 5 | 10 / 7 / 6 / 8 / 10 |
| Highland Box Truck, seeds 1-12: mean pair / nearest / layout; suspension kinds, wheel sets, 3-axle share | 0.220 / 0.143 / 0.260; 4, 3, 33 % | 0.239 / 0.167 / 0.315; 8, 7, 58 % |
| IT War Bus, seeds 1-12: mean pair / nearest / layout; suspension kinds, wheel sets | 0.180 / 0.128 / 0.210; 5, 4 | 0.203 / 0.143 / 0.227; 6, 5 |
| Highland, every shape, 6 seeds each: mean pair / nearest neighbour | 0.328 / 0.126 | 0.333 / 0.140 |
| IT, every shape, 6 seeds each: mean pair / nearest neighbour | 0.326 / 0.119 | 0.332 / 0.134 |
| Identity mean by style, `--kit-report --seeds 4` (Bandit, IT, Highland, Call Centre, British, Militia) | 0.95, 0.90, 0.95, 0.88, 0.91, 0.91 | 0.96, 0.89, 0.95, 0.88, 0.91, 0.91 |
| Weak points all there and hittable, same run | 31/31, 26/32, 32/32, 21/32, 34/34, 32/32 | 30/30, 24/32, 32/32, 21/32, 34/34, 32/32 |
| Generation failures over every style and shape, 4 seeds | 3 of 196 (Technical, kit 1) | 4 of 196 (the same 3, a Bandit Scrap Tanker) |
| Full check (`--sweep-vehicle --seeds 6`): Bandit Box Truck, IT War Bus, Highland Flatbed Fort, British Gun Wagon | 6, 6, 6, 6 of 6 | 6, 5, 6, 6 of 6 |
| Generation, mean over every style and shape (load varied) | 312 ms (worst 2182) | 348 ms (worst 1840) |

Kit 3's one full-check failure (IT War Bus 5) is a Network Switch trophy on a Long Hood's top socket, coplanar with the V8 Engine under it: a pairing kit 2 can roll too, not
the layers' doing. Fixed where dressing stands (2026-11-06): `Dress` and the marks fit with `Onto(..., clear: true)`, which refuses a spot where a face would be
coplanar with a part already there (`VehicleChecks.ZFightsWith`, the check's own rule on the parts nearby), and the part goes to the next panel. The cause is in the
data: every kit engine's details stand above its +y socket (the V8's rocker covers 12 cm, the Engine's 11 cm) and poke through a Long or Snub Hood's skin (5.5 to 6.5 cm
at its middle), which a hood's shape would have to change to hide. The IT War Bus's lower identity is its old missing mast (5 of 12 against 3 of 12): a bus that plans again
more often (a boxy plan, an ammo reel) has fewer plans left for the identity re-plan, and that is what the guaranteed-marks pass is for. Generation cost is within the
box's noise: more plans are made (the fill margin is a point now) and each costs the same.

## Roles and tiers

Kit 3 makes a faction's vehicle by its shape: ask for a Box Truck and you get a box truck, whatever it is for. **Kit 4** (`VehicleKitVersion.Roles`, `VehicleRoles.cs`) adds what a
vehicle is *for*: a **role**, stated in needs the built vehicle is measured against, while the style still decides how it looks; and a **tier**, the threat ladder's rung within the
faction (zombies 1, bandits 2, military 3, high tech 4 as a place's danger; a Bandit tier 4 is a warlord's truck, still a Bandit's). Harder places hold harder enemies and better
loot, and players salvage what survives.

**Which kit.** Only a vehicle asked for by role is kit 4. Every request by shape, every committed seed and every tool run without a role is kit 3 (or kit 1) exactly as before, so
`--gen-vehicles` still reports all 123 identical and `Current` stays kit 3. `--kit 3` on a role run makes the seed without its role and measures it against the role all the same,
which is how the numbers below compare the two.

**The roles** (`VehicleRoles.All`; needs at tier 2; the speed is the cheap estimate below):

| Role | For | Shapes (weight) | Needs | Leans |
| --- | --- | --- | --- | --- |
| scout | fast, light, 1-2 crew, one light gun, little loot | Technical | seats up to 3, 1 gun up to medium, 27+ m/s, loot up to 3, up to 7 m | one axle |
| raider | fast to middling, 2-4 crew, guns, some loot | Technical 3, Box Truck 2, Gun Wagon 2 | seats 2-4, guns 1-3, 22+ m/s, loot 1+, up to 9.5 m | gun beds, rams, short |
| gun platform | slow, heavy gun(s), a deck | Flatbed Fort 3, Gun Wagon 3 | 2+ guns, medium+, an open deck | gun beds, hutches, gun decks, walled sides |
| carrier | an APC: crew space, armour, little gun | War Bus 3, Box Truck 2, Double Decker 2 | 3+ seats, up to 2 guns, armour 11+, a room | rooms, long, armour |
| tanker | fuel or cargo, weak points exposed, rich loot | Scrap Tanker 4, Tanker Rig 2 | up to 2 guns, 400+ l of fuel, its fuel in the open, loot 1+ | tanks, a chest on the deck |
| recovery | crane, winch or tow, little gun | Box Truck 2, Flatbed Fort 1, Gun Wagon 1 | up to 2 guns, a tow hitch, a winch | hitches, beds |
| command | masts and radio, armour, one gun, the best loot | War Bus 2, Box Truck 2, Double Decker 1 | 1-2 guns, armour 11+, a room, 2+ signals | rooms, roof racks, armoured cabs |
| boss | the largest: most guns and armour, the best loot; rare | War Bus 3, Flatbed Fort 1, Double Decker 1 | 3+ guns, armour 11.5+, 8.5 m+, a room | long, gun beds and decks, armour |

A role names only the shapes that can meet it: the Box Truck was a scout in the first draft, and measured 20 to 25 m/s against a Technical's 25 to 34, so no grammar shape is a
scout. A style fields only the shapes it builds (the British style's Double Decker is an extra the others don't ship).

**The measure** (`Vehicle.MeasureRole`, `RoleFit`): seats (every seat part), guns (turrets) and the heaviest gun's class (`VehicleRoles.ClassOf`, by the receiver's bore and action:
carbines and pump guns light, full-auto rifles medium, harpoons and flame guns heavy), the **estimated top speed** (the engine's peak power, 60 % of it at the wheels, against
rolling resistance 0.03 and drag Cd 0.9 over the frontal box: it ranks vehicles and runs nothing; a Bandit Technical estimates 31 and drives 24 in the check's 6 s), mass, the
**armour rating** (the mean armour of the body's panels by area: sheet steel 6, hull bays 8, tread plate 10, plate steel 20, armour plate 40), loot (containers that roll
from a table plus loose cargo pieces) and its table's tier, the length, fuel carried, and whether it has a room, an open deck (a bed or a porch), a tow hitch, a winch, its fuel
in the open (the identity score's fuel weak point, there and hittable) and how many signals (the style's `mast` parts, else its trophies).

**Tiers** (`RoleAsk`) move the needs and lean the choices:

- guns: a fighting role (raider, gun platform, boss) may carry one more from tier 3, rolls one more at tier 4 where its shape has room (a lean, not a need: a Technical carries one; needing it failed 41 of 144 tier 4 vehicles), and needs at least a medium gun from tier 3; an armoured fighting
  role (the boss) wants a heavy one at tier 4. At tier 1 nothing heavier than medium (no harpoon, no flame gun). A style with no heavy gun (IT, the Call Centre, Militia)
  fields its heaviest. Guns weigh by class and rarity: a rarer receiver (the Whaler, the Firebreather, Militia's battle rifle) is likelier from tier 3, less likely at tier 1.
- armour: the needs rise half a point a tier (a whole point failed 18 of 144 at tier 4: appliqué and plates reach 12 to 14); the plates lean further to armour (+0.15 a tier).
- size: segment lengths lean longer, 0.15 a tier.
- loot: the role's loot tier is its base plus the tier less one (scout, raider, gun platform, carrier and recovery 1, tanker 2, command and boss 3), to 4; tiers 1 and 2 roll
  the style's own table, 3 and 4 its richer ones (`"bandit 3"`, `"bandit 4"` in `Constructs/Loot.json`: two to five rolls, rarely empty, circuit boards, control units,
  seekers, a jetpack). A tier 4 vehicle also tries one more chest.
- part quality: running gear and the light cab weigh by the tier of their recipe (`FactionStyle.Weigh`): among a style's own engines a tier 1 Technical leans to the Engine,
  a tier 4 one to the V8. Heavy vehicles' slots list one engine in every style, so for them the tier shows in armour, guns, size and loot.

**How a role leans the grammar.** Its module factors multiply the style's (`VehicleRole.Modules`, body patterns by their segments'), its length lean adds to the style's, the
chance an open bed carries a gun is its own (a gun platform 1, a carrier 0.2), the gun count is rolled within its needs' range clipped to the recipe's, guns are picked by
class and rarity, and the chassis table leans by its speed (a fast role weighs one axle up, a slow or armoured one a second axle). Armour leans the cab (armoured, enclosed),
walled bed sides and loaded flanks, and the plates: `FactionStyle.Weigh`, a per-thread hook that is null for every other vehicle, weighs the style's own plate materials by
their armour (only its own: an Armour Plate deck or wall, tried first, stands thicker than any a style lists and refused a Flatbed Fort's bunker brow beside it; the armour plate goes on as appliqué instead, below), so the identity stays the style's.

**The role pass** (`Vehicle.RoleKit`, after the marks pass, on forks of the kit layer: "kit role winch", "kit role armour", "kit role signals", "kit role chests"):

- a recovery vehicle's Cargo Winch on a free up-facing socket of its porch, bed, rack, a tank or the tail rails, clear of every turret's sweep and under open sky;
- **appliqué armour** for a role that leans to armour (0.3 and up): the style's heaviest armour plate, a metre square, welded flat over the sides of its rooms' hull bays a few
  centimetres proud of each bay's widest point (no shared face, nothing inside the room), never on the door's side of the door bay, a metre apart and more, only where it meets
  nothing (panniers, sponsons, the ladder, wheels), up to half the bays' sides, in the style's body colours. Hull bays are armour 8, so without it a room's armour hardly moved
  (carriers 10.0 by shape, 10.8 leaned);
- a command vehicle's signals on the marks pass's spots (the roof, the cab, a post);
- the role's extra chests: against a room's end wall, or for a vehicle with no room (a tanker), on its deck in the open;
- every loot container's table set to the role's loot tier (`BlueprintScript.RetableLoot`).
- its own cargo: ammo for its guns, spares from its own build and spare guns, stowed as a crew would ("Cargo and spares").

**Re-plan.** Each plan that builds is measured and scored for its faction; a plan that misses the role, or has a mark missing or an identity under the style's threshold,
plans again, up to three built plans (`VehicleRoleMake.Plans`; plans the game refuses as it builds don't count, up to six tries), and the best is kept: fewest role misses,
then fewest marks missing, then identity. A kit-1 shape (the Technical, the Tanker Rig) has no grammar to lean: it is built by its generator with the
role's part weights, the marks and the role pass, measured, and if it misses it is tried again from seeds derived from the asked one (`VehicleSpawnKit.Reroll`); the
seed that made it is in its name. A vehicle that still misses its role is kept and says so ("role missed (...)"): the spawn wants a vehicle, and the miss is reported.

**What it carries.** The name gets the role: "Bandit Box Truck 7 [raider, tier 2]" (never a committed seed's). The kit line: `// kit 4; role raider, tier 2, shape Box Truck,
role met, role plans 2, fit seats 3, guns 2 (medium), est. top speed 23 m/s, 2.4 t, armour 10.3, loot 1 (tier 2), 8.4 m, fuel 60 l, tow hitch, room, deck; seeds chassis 7
body 7 kit 7 wear 7, chassis ..., body room+gun bed, ...`; `Vehicle.Role`, `Vehicle.Fit` and `Vehicle.KitNotes` hold the same; the description starts "A raider (tier 2).";
the spawn's "generated" line carries the notes (`VehicleGenJob.RoleNotes`).

**The API.**

```csharp
game.VehicleSpawn.Request("Bandit", "raider", tier: 3, seed: null, ground, yaw, owner: 255, frontOf: 255, done: ...);
game.VehicleSpawn.Request("Highland", role: null, tier: 1, seed: 42, ground, yaw); // null: a role the style fields, by its weights
```

The server picks the shape from the role and the seed (`VehicleRoles.ShapeFor`: the same seed always picks the same), then makes it as any dynamic spawn (sliced, rerolled,
falling back to a committed seed of that shape), role and tier kept through the rerolls. `layers` works as for a shape request on the grammar's shapes. The request by shape is
unchanged. Nothing is on the wire: only the server generates.

**Which roles a faction fields** (`VehicleRoles.Fielded`, by style name, additive: `Styles.cs` is untouched; a style not named fields `DefaultFielded`):

| Style | scout | raider | gun platform | carrier | tanker | recovery | command | boss |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Bandit | 3 | 4 | 2 | 1 | 2 | 1 | 0.5 | 0.3 |
| IT | 2 | 1 | 1 | 3 | 1 | 2 | 2 | 0.3 |
| Call Centre (Scam) | 3 | 2 | 1 | 2 | 1 | 1 | 1.5 | 0.3 |
| Highland | 2 | 4 | 2 | 2 | 1 | 1 | 1 | 0.4 |
| British | 1 | 1 | 1 | 4 | 1 | 2 | 2 | 0.3 |
| Militia | 2 | 2 | 3 | 3 | 1 | 1 | 2 | 0.4 |

**Commands.** `--role "Bandit raider" --tier 2 [--seeds N] [--check] [--sliced]`: each seed's vehicle, how it met the role, its identity and what the role pass placed, the hit
rate and commonest misses; the first seed is made again and must write the same text, with `--sliced` once more as the dynamic spawn makes it (2.5 ms slices on the kit's
thread), whose hash must match; `--check` runs the full check on each. `--kit-report --roles [--seeds N] [--style S] [--role R] [--tier N] [--raw]`: every style, role and
tier (default 3 seeds a cell): a line a cell, then a table (met, failed, identity, every mark, plans, cost, shapes) and the same by role, by tier and by style.

**Measured** (2026-11-07, `--kit-report --roles --seeds 3`: every style (Bandit, IT, Highland, Call Centre, British, Militia) x every role x tiers 1 to 4, seeds 1 to 3,
576 vehicles, generation only; the 2-core box shared with other agents, load 6 to 12, so times are rough. Kit 3 is the same run with `--kit 3`: the same shapes and
seeds made without the role, measured against it):

| | kit 3 (no role) met | kit 4 met | kit 4 identity | kit 4 mean ms (kit 3) | kit 4's commonest misses |
| --- | --- | --- | --- | --- | --- |
| scout | 44/72 | 62/72 | 0.95 | 308 (276) | 9 est. top speed under 27 |
| raider | 40/72 | 59/72 | 0.96 | 369 (316) | 7 no loot (a Technical), 5 over 9.5 m |
| gun platform | 40/72 | 67/72 | 0.97 | 569 (485) | 5 under 2 guns |
| carrier | 4/72 | 62/72 | 0.97 | 1383 (760) | 10 armour under the need |
| tanker | 0/72 | 72/72 | 0.94 | 183 (203) | |
| recovery | 0/72 | 68/72 | 0.96 | 429 (421) | 4 no winch |
| command | 4/72 | 62/72 | 0.97 | 875 (638) | 10 armour under the need |
| boss | 1/72 | 45/72 | 0.98 | 1628 (751) | 18 armour, 9 under 3 guns |
| tier 1 / 2 / 3 / 4 | 35 / 34 / 32 / 32 of 144 | 130 / 127 / 125 / 115 of 144 | 0.96 / 0.96 / 0.96 / 0.97 | 645 / 695 / 744 / 787 | tier 4: 19 armour |
| all | 133/576, 24 failed to generate | 497/576 (86 %), 2 failed | 0.96, every mark on 574/574 | 718 (481), worst 3314 | |

Kit 3's misses are what the roles are for: no winch, no chest on a tanker, no signals, too little armour, a single gun on a gun deck. Kit 3's 24 failures are kit-1
Technical seeds with nowhere to stand a part; kit 4 tries a derived seed. Kit 4 re-planned 305 plans for a role over the 576 (about half a plan more a vehicle), which is its
cost; the slowest are the armoured War Buses, whose appliqué tests every part's bounds per plate. By style kit 4 met 79 % (Call Centre) to 91 % (Highland); identity held at
every tier (0.92 Militia, its derived signature, to 0.99 British), and every vehicle that generated carried every mark.

What is left over: armour is the hard need. A room's area is its hull bays (armour 8), and appliqué covers at most half their sides (plates a metre apart, so no two share
a face), so a War Bus tops out near 13 to 14 and the tier 4 needs (carrier and command 12, boss 12.5) miss about one in eight. A heavy vehicle's engine is the style's only
heavy engine, so a tier shows on it in armour, guns, size and loot, not part quality. The speed is an estimate, not a drive.

**Checked** (the full check, `--role ... --check`: drive, turn, guns, coplanar faces, rooms): Bandit, IT, Highland and British, every role, tier 3, seed 5: 32 of 32 passed.
Earlier samples found two things now fixed: Armour Plate in a style's decks (the style's own plate materials only now) and a tier 1 Militia Technical under the check's
stability (a kit-1 shape's role vehicle is held to its check's stability now), and one not the roles' doing: the Network Switch hood trophy z-fight (IT Box Truck 2), fixed on
main by the dressing fix. A role seed made twice writes the same text, and made as the dynamic spawn makes it (`--sliced`) the same hash; the dynamic kit-3 seeds tried
(Bandit Box Truck 1000, IT War Bus 77, Highland Flatbed Fort 55, Call Centre Gun Wagon 12, British Scrap Tanker 40, Militia Box Truck 5, Bandit Technical 30) write the
same files as before kit 4, byte for byte.

## Families and convoys

Kit 4 makes one vehicle for a role. **Families** make model lines of them, and **convoys** make groups that read as a fleet (`VehicleFamilies.cs`, `VehicleFamilyNames.cs`,
`VehicleConvoys.cs`; roadmap phase 6). Nothing here changes a vehicle made any other way: every committed seed, every request by shape and every kit-4 request by role
makes the same text as before (`--gen-vehicles`: all 123 identical; `--kit-report --roles --raw` for every IT role, tier and two seeds: the same numbers as main).

**A model** (`VehicleModel`) is a small record: style, role, the family's base tier, seed, mark (1 to 3), variant, paint scheme, layer overrides and a named flag. The same
model always makes the same vehicle, so its key is all a world, a log or a convoy needs to spawn it again: `Bandit/raider/2/282030/mk2/scheme 292943/wear 419532`
(`VehicleModel.Key`, `TryParse`; `VehicleSpawner.RequestModel` spawns one). Every family vehicle's kit line starts `model KEY`, and the spawn's "generated" line carries it.

**Marks.** A family is a style, a role, a base tier and a seed. The seed picks the shape (`VehicleRoles.ShapeFor`, so every mark is the same shape) and the layers. Mk1 is
made at the base tier, Mk2 a tier up and Mk3 two (to 4), but their frame leans by the base tier (`RoleAsk.FrameAt`: segment lengths, the chassis table's axles, cabs,
bed sides, plate materials and running gear), so the marks share one body on one chassis. The kit follows the mark's tier: guns weigh by it, the loot tier rises, and the
appliqué grows (`RoleAsk.KitArmourLean`: at least 0.3 for a Mk2, 0.5 for a Mk3). A room's hull bays keep Mk1's appliqué (kit 4's, at the frame's lean: plates high on a hull
raise the centre of mass, and a mark that failed the stability gate for them went to another plan, another body), and the mark's own goes on the sides, low (`Vehicle.SideArmour`:
the style's heaviest armour plate a metre square, welded upright 4 cm proud of the outermost plate or beam at a station, 0.35 m and more over the ground and its middle under
the centre of mass, so it only steadies the vehicle, only where it meets nothing, a metre from any other weld; two on a Mk2, four on a Mk3, more as the lean asks). A mark is made from its **first plan that builds**, never planned again for its role or its identity:
re-planning would give a later mark another body. So a family's mark misses its role more often than a kit-4 vehicle does, and says so in its kit line as any role vehicle
does. A plan under the check's stability throws as it builds (a family's mark has no role re-plan to catch a tall one), and the next plan is taken; a mark whose first
plans didn't build has "plan N" in its kit line, and `--family` flags a mark on another plan than Mk1's.

**Variants** change one layer on purpose, on the family's body and chassis: **up-armoured** (the kit layer on a seed of its own, and the most appliqué: lean +0.3, four side
plates and more), **gun truck** (the kit layer on a seed of its own, one more gun where the shape has a spot, every open bed a gun bed, one more allowed by its needs) and **tanker conversion**
(up to three Fuel Drums on its open deck, clear of the guns, under open sky, never coplanar with what is there; a loot tier up for the cargo). A kit-1 shape (the Technical,
the Tanker Rig) has no layers: its variants change only what the pass adds, and its marks are its seed's with the mark's tier.

**Named uniques.** A model is a named unique by a roll on its seed (`VehicleFamilies.RollNamed`: 8 % for a command or boss model, 2 % for the rest; `Named` in the key forces
it): its own name, a tier up, the best loot (loot tier 4), one more chest and one more mark (the style's masts, else its trophies, on the marks pass's spots).

**Names** (`VehicleFamilyNames.ByStyle`, by style name; Karl may edit them, and a changed list renames families without making any vehicle differently): each style has its
model names, how its marks read and its uniques' names, all ASCII. Bandit "Rustbucket Mk2", "Old Mother Rust"; IT "Model T-1000 Helpdesk", "Patch Tuesday SP2",
"Cloud Migration Enterprise", "The Mainframe"; the Call Centre "Hot Lead Gold", "Lucky Draw Platinum"; Highland "Glen Rover", "Bothy Bus II", "Bothy Bus Mor", "The Big Yin";
British "Wellington Mk II", "Windsor Mk III", "Her Majesty's Displeasure"; a style not named (Militia) gets `Default` ("Patriot Mk1"). Each variant has a style word: Bandit
"Plated", "Gunrig", "Guzzler"; IT "Hardened", "Pen Test", "Data Lake"; British "Ironclad", "Gunboat", "Bowser". A vehicle is named "Bandit Rustbucket Mk2 [raider, tier 3]",
never a committed seed's name.

**Measured** (`--family "STYLE ROLE" --seed 5 --tier 2 --check`, 2026-11-07, the 2-core box at load 3 to 9; distances as `--diversity` measures them):

| Family | Shape | Mk1 to Mk2 / Mk3 | up-armoured / gun truck / tanker / named | within the family (worst) | to two other families (least) | full check |
| --- | --- | --- | --- | --- | --- | --- |
| Highland raider 5 | Box Truck | 0.007 / 0.015 | 0.081 / 0.074 / 0.014 / 0.003 | 0.049 (0.112) | 0.375 (0.344) | 9 of 9 |
| IT carrier 5 | War Bus | 0.014 / 0.018 | 0.105 / 0.069 / 0.011 / 0.004 | 0.055 (0.115) | 0.220 (0.171) | 9 of 9 |
| British command 5 | Box Truck | 0.008 / 0.016 | 0.052 / 0.067 / 0.017 / 0.003 | 0.040 (0.083) | 0.229 (0.204) | 9 of 9 |
| Bandit raider 7 | Gun Wagon | 0.017 / 0.030 | 0.044 / 0.045 / 0.021 / 0.014 | 0.039 (0.075) | 0.305 (0.273) | 9 of 9 |

The marks read as one vehicle (under 0.08) and the variants as close kin; every other family of the role is three to seven times further away. What a mark changes
that the distance doesn't count: paint, loot, plate materials (none: the frame's), and the guns' classes.

**Convoys** (`VehicleConvoys.Plan(style, count, tier, seed, mix)`; nothing is made to plan one, and the same seed always plans the same):

- a **leader**: a command vehicle, or a boss (half as likely as a command vehicle from tier 3, an eighth below), a tier up;
- **escorts**: one raider or scout line (by the style's `Fielded` weights), Mk1s with a Mk2 among them now and then and a gun truck or an up-armoured variant on one; past
  three escorts, sometimes a second line for the last;
- **cargo** (from three vehicles): a tanker line, else a recovery or carrier line (weighted 3, 1, 1 by what the style fields); a second cargo vehicle is its twin, or for a
  line that isn't a tanker its tanker conversion;
- the order: half the escorts, the cargo, the rest of the escorts at the rear. A column, or from four vehicles a wedge (a third of the time): the leader in the middle, the
  rest alternately in a lane each side, 7 m out;
- **one paint scheme** (`VehicleConvoys.SchemeOf`): for every palette role but the rust, one of the style's colours (two of a role that lists four or more), so the
  convoy shares its body, hood, frame and war paint; every colour is still the style's own, so the identity's palette score holds. It works as `FactionStyle.Weigh` does: a
  per-thread `FactionStyle.Scheme` while a member is made, the same one roll over the scheme's colours, null for every other vehicle. Each member also takes its own wear seed
  (`Layers.Wear`, on the grammar's shapes), so twins differ in their rust, dirt and patches;
- **one or two model families** behind the leader (the leader is its own), and no two lines share a model name (a family seed whose name is taken is moved on).

**In the game.** `VehicleSpawner.RequestConvoy(style, count, tier, seed, at, heading, owner, done, mix)` plans it and queues its members; each is generated and set down as any
dynamic spawn (sliced, rerolled, falling back to a committed seed of its shape). Where a member stands is worked out when it is ready: the leader at `at`, each one after it
`VehicleConvoys.Gap` (5 m) behind the last tail in its lane (`Vehicle.ExtentZ`, the generated vehicle's own reach ahead of and behind its origin; a committed stand-in's from its
shape's usual), on the ground, heading the convoy's way. **Caps:** a convoy's members have their own cap (`ConvoyQueueMax`, 6, beside the 8 single requests, which they don't
count against), one convoy at a time on the server (`ConvoysAtOnce`), so a player has at most one; a request beyond it is refused with "a convoy is on its way already". One log
line per convoy, when its last member is down: `[vehicles] Bandit convoy 3: 4 vehicles, column, tier 2, 3 families, scheme 292943: 4 of 4 down in 7.2 s: leader Bandit Roadrash
Mk1 [command, tier 3] as 21 (KEY); escort ...; scheme body #6f7477+#5f6a55, ...`. The members' own "generated" and "set down" lines come as before.

**The parts menu.** With "Faction vehicles: new seed" on, the Blueprints tab has "A faction tile makes": *its shape* (as before), *by role* (a role, any by default, and a tier:
the tile's style, by role, `CActKind.FactionVehicle`, protocol 191) or *a convoy* (how many, 2 to 6, and a tier). A convoy is set down passing in front of the player: its leader
22 m ahead and to one side, heading across their view, the rest in a line behind it, so the whole convoy is seen side on. The host's "Roles and convoys" (on by default; off: requests
by role, model and convoy are refused and the tiles make their shape, as before kit 4) sits beside "Server generates them" (off: every member is a committed seed of its shape).

**Checked.** `--convoy STYLE --seed 11 --count 6 --tier 3` for IT, British, Highland and the Call Centre and `--convoy Bandit --seed 3` generate every member, the leader twice
to the same text. The full check passed every member of the table's four families (36 of 36) and 4 of the 5 of Bandit convoy 3; its leader (a tier 3 command
Box Truck, plan 1) stood 10.5 degrees off level and didn't drive: its chassis mixed Heavy Pogo Struts in front (they hang the wheel 1.375 m under the mount) with Heavy
Suspension behind, so it stood nose up on its tail rails. Fixed in the chassis (2026-11-07, "Things that caught the vehicles so far"): the leader now has its own rear on a
level front and passes the full check (71 m in 6 s, 1.7 degrees worst tilt); the other 39 members of eight convoys tried came out byte for byte the same. In game (an offline host on
Construct Yard and a client over RakNet at 80 ms and 2 % loss, the client asking by the wire): Bandit convoy 3 (4 vehicles, column) down in 7.2 s and IT convoy 11 (6, wedge)
in 20.2 s, spaced and laned as planned, the client with the same parts on every body, and every member's text hash the one `--convoy` prints for it (made in slices as the spawn makes it); the worst ticks were the first slice of a generation (21 to 33 ms, the same for a single role
spawn on this box) and one 52 ms join with a garbage collection in it.

## Spawn context

Roadmap phase 7 (`VehicleContext.cs`, `Source/Game/GameSandboxVehicleContext.cs`): the place a vehicle is spawned nudges its build, while the faction's style still decides how it
looks. A desert Bandit truck is a Bandit truck on bigger tyres in the sandier half of its palette; an IT van in the snow has chains.

**The record** (`SpawnContext`): `Biome` (neutral, desert, snow, swamp, forest, road), `Gravity` (times the world's standard), `Air` (density, 1 at sea level, 0 off world),
`Wood` and `Ore` (0 to 1: how much of the place is forest, how much metal ore lies under it), `Rain` (the weather's, 0 to 1) and `Read` (how the server read it, for the log).
It *matters* when the biome isn't neutral, gravity is under 0.75 or over 1.3, the air is under 0.2, or wood or ore is 0.5 or more; anything else is neutral, and **a neutral context
makes exactly today's vehicles**: every hook below is a no-op unless `SpawnContext.Current` matters. Measured: `--gen-vehicles` all 123 identical; on the branch's base and on
this branch the same 8 convoys (40 members, every style) and 8 dynamic kit-1, kit-3 and kit-4 seeds wrote the same text, but for the one vehicle the chassis fix changed (Bandit
convoy 3's leader, "Families and convoys"). No kit version: no existing seed changes meaning.

**Reading it** (`SpawnContexts.At(ground)`, server only, at the request): five ground samples (the point and four 8 m round it) through `TerrainData.Sample`, `SampleNormal`,
`Locate` and `Material` (dug and streamed ground read right; no loop over the grid), the resource field's deposit under each (`KindAt`: a forest for wood, iron, copper, gold or
levite for ore), `GravityFrames.GravityAt` over `World.Gravity` (the host's setting and a planetoid's field included), `ConstructAir.Density`, and `Weather.RainHere` (the
weather's own reading, at the player, who is near: the parts menu sets vehicles down in front of them; 8 mm/h is 1). The biome is the first that holds: **snow** within 20 m
under the surface's snow line or above it; **desert** on sand (the material, or under the sand line) under 3 of the 5 samples; **swamp** within 4 m of the terrain's lowest ground,
nearly flat, in rain of 0.4 and more (maps have no water yet); **forest** in a wood (3 of 5 samples) or on rough ground (rock or slopes over 15 degrees under 3 of 5); **road** on flat
bare dirt; else neutral. A map with no terrain (Construct Yard) or a planetoid reads neutral ground (gravity and air still count). 0.01 to 0.08 ms a reading on this box. A
probe over 100-odd points of each map read Adobe Mesa 86 neutral, 20 rough, 4 desert; Alpine Pass 48 rough, 5 desert (its snow line is above the play area); Proving Ground road 10, forest 21 (8 of them a wood), ore under 18.

**What it leans** (each only among the style's own parts and colours, or a spring's own class as kit 3's swaps are; none rolls a die of its own: a weighted pick takes the same
one roll the plain pick did, so the rest of the seed rolls as it does in a neutral context):

| Context | Running gear | Body and kit |
| --- | --- | --- |
| desert | tyres: the biggest the style's gear has that may go there (a front up to 0.56 m, a rear under a body never bigger, a Dually Wheel at the rear where the style has it); a wide front wing weighs four times | body and panel paint from the style's colours nearest sand |
| snow | Spiked Wheels (the kit's chains) where the style's gear has them, no bigger; second axles 1.6 times | paint nearest snow |
| swamp | the biggest tyres, the longest-travel spring of each corner's class (Hydraulic Suspension); second axles twice | paint nearest mud |
| forest | the biggest tyres, long-travel springs (high clearance); the rear axle half a metre further in and the set-back front station four times as likely (a short wheelbase); second axles 0.6 times | segments lean short (-0.45); paint nearest the woods |
| road | Slick Wheels where the style has them; the shortest-travel spring of the class (a lower, stiffer ride); one axle twice as likely, second axles 0.4 times | segments lean a little long |
| low gravity (under 0.75) | second axles twice (more ground contact); a wide front wing | plates lean to the lightest of the style's (sheet over plate steel) |
| high gravity (over 1.3) | the strongest spring of the class (Heavy Suspension, Heavy Double Wishbone, Heavy Swing Arm); second axles 1.8 times | room sections lean low (tall 0.4 times, low 2.5 times); segments a little short |
| no air (under 0.2) | | every engine slot is the Electric Motor, a Battery Pack on a free side of it or its gearbox (else on the frame), no fuel line (the tanks stay, still a weak point) |
| wood (0.5 and up) | | plate slots that list Wood Planks lean to them (up to six times) |
| ore (0.5 and up) | | plate slots lean to their metal (plate steel, tread, armour: up to four times) |

A kit-1 shape (the Technical, the trailers, the Tanker Rig) has no chassis layer: its recipe pick leans the same way (`VehicleContext.Pick`), then its tyres and
springs as above. A swap never makes a chassis whose corners hang differently (`VehicleChassis.Level`). How strongly a style leans is its row in `VehicleContext.ByStyle`
(additive: `Styles.cs` is untouched): the Bandits 1.2 (they build from whatever is lying about), IT and the Call Centre 0.7, the British 0.6 and only their panels take the
ground's colours (the bus stays red unless a listed colour is near the ground's), Highland's panels only (the Saltire stays). A style not named takes `VehicleContext.Default`.

**Not done, for want of parts:** dust filters (the only "Filter" is a logic part), cold-weather kit, floats (no buoyant part, and maps have no water), and tracks: no style
lists a track, so "tracked where the style has tracks" never fires. Gearing isn't touched: every style lists one gearbox and no ratio is configurable on it, so a road vehicle is
faster by its tyres and its single axle. Off world the Electric Motor is 10 cm lower than an engine and half a V8's length, so a hood stands on a Drone
Battery on the motor's top (`Vehicle.HoodOn`: within 2 cm of where it always stands), and plated hoods (decked at the engine's height) and cab-overs weigh more in the grammar
(`VehicleContext.Module`) and a kit-1 hood slot; a moulded hood that still meets the gearbox fails as it is built and the seed plans again, and a request that makes nothing
electric is refused with "no air here: no ... could be made to run without it" rather than given a committed seed that burns fuel. The full check takes an Electric Motor's
battery for its fuel.

**A family's model in another context** keeps its family: the same body and chassis (modules, axle stations and outline to the decimetre), so a convoy reads as one fleet
wherever it is met. Only the context's kit changes: tyres within 5 cm of the model's own size and no bigger (Large Wheel to Slick on a road, Off-Road to Spiked in snow), paint
from its colours nearest the ground, plate materials (wood, ore, light: the same plates, another material) and the electric drive off world; no chassis table, length, body
range or spring lean (`SpawnContext.KitOnly`, set when the request is a model's). Its key stays the same: the context is the request's, not the model's.
`--context "Bandit/command/3/553571/mk1/scheme 292943" --sweep` prints, for each context, the kit that changed and whether the body and chassis stayed the family's.

**What it carries.** A vehicle made in a context that matters is named for it ("Bandit Box Truck 4711 [desert, ore]", after its role and model labels), so it never reads as a
committed seed or as the neutral vehicle; its blueprint carries a second comment, `// context desert, gravity 1.00, air 1.00, wood 0.00, ore 1.00, rain 0.00; changed chassis
table leaned (desert), tyres Large Wheel/Dually Wheel, deck plates leaned (ore), rust paint desert (#8c5230/#94603a), ...` (`Vehicle.ContextLine`, `Context`, `ContextNotes`),
and the spawn's "generated" line ends `; context ... (read: sand under 4 of 5 samples)` (`VehicleGenJob.ContextNotes`). Nothing is on the wire.

**The API.** Every request takes it (`context: null` is neutral): `Request(style, shape, ..., context: SpawnContexts.At(ground))`, `Request(style, role, tier, ...)`,
`RequestModel(model, ..., context)`, `RequestConvoy(..., context)` (one context for the whole convoy, read at its anchor), and the kit's own `VehicleRegistry.Make(g, seed, ...,
context)`, `VehicleFamilies.Make(model, context)`, `new VehicleGenJob(g, seed, layers, role, context)`. The parts menu's three requests read the ground they set the vehicle on.
Rerolls and fallbacks keep the context; off world, a request with the host's committed seeds only is refused at once, and a fallback to a committed file is refused.

**Measured** (2026-11-07, `--context ... --sweep --check`; the 2-core box shared with other agents, so costs are rough):

| Run | Made | Full check | Identity | What changed |
| --- | --- | --- | --- | --- |
| Bandit Box Truck 2 and 3, every context | 20 of 20 | 20 of 20 | 0.93 to 0.97 a context | the chassis table in 14 of the 20, tyres in 9 (Large, Spiked, Slick), springs in 5, paint in 5, plates in 5, a low profile in 2 |
| IT raider tier 2, seeds 1 and 2 (Technicals, kit 1), every context but no air | 18 of 18 | 13 of 18 | 0.94 to 1.00 a context | the recipe pick in 12 of the 18, tyres in 9, springs in 8, paint in 4, plates in 4; the five failures are seed 1's top fill (85 %, at most 84 %), which "wood" fails with nothing changed: not the context's |
| Bandit Box Truck 2 to 9, no air | 8 of 8 | 4 of 4 tried | 0.92 to 0.95 | Electric Motor, Battery Pack, a Drone Battery riser under the hood |
| IT raider tier 2, seeds 1 to 4, no air | 3 of 4 | | 0.95 | one Technical's Snub Hood still met the gearbox: refused |
| Model Highland/carrier/2/5/mk2 (a War Bus), every context | 10 of 10 | 10 of 10 | | body and chassis its family's in all ten; tyres (Spiked, Slick at the rear), panel paint, plates, the electric drive |
| Model Bandit/command/3/553571/mk1 (Box Truck), every context | 10 of 10 | | | the same body and chassis (modules, axles, outline to 0.1 m) in all ten |

Generation in a context costs what it does neutral plus the drive check (15 to 50 ms a plan); a context's own work is a few dictionary lookups a part pick. In slices
(`VehicleGenJob` with a context, 2.5 ms a slice) a desert-and-ore Box Truck wrote the same hash as made directly.

## Cargo and spares

Roadmap phase 9 (`VehicleCargo.cs`): a kit-4 vehicle (asked for by role, or a family's model, or a convoy's member) carries what its crew would carry for itself: **ammo
for its own guns, spare parts for its own repairs and spare guns**, stowed the way a player would stow them. All of it is real parts, so players shoot it, spill it, steal
it and salvage it like anything else; nothing is abstract and nothing unlocks. Every other vehicle (a request by shape, a committed seed, kit 1 to 3) carries nothing new:
`--gen-vehicles` reports all 123 identical, and kit 3 seeds write the same text as before.

**What it carries** (`Vehicle.StowCargo`, at the end of the role pass, on forks of the kit layer: "kit cargo" for the amounts, "kit cargo shelf" for a spares shelf; the layout rolls nothing):

- **Ammo it uses.** For each gun, magazines of the kind its build takes (the build's `feed`: a Box Magazine for a carbine, an Extended Mag for a Scrap MG, a Shell Feed for
  a pump gun, a Harpoon Breech for a Whaler, a Fuel Canister for a flame gun); Ammo Boxes for the guns that load from boxes; and on a gun deck a loose Ammo Crate. Rounds
  are universal (docs/sandbox/CONSTRUCTS.md, "Ammo feeds": any type fits any gun, the bore sets the calibre), so a box serves every gun but a flame gun, whose ammo is fuel:
  it gets Small Fuel Tanks instead. A box or crate comes full (300 and 1,200 rounds), of FMJ: a slot or a cargo piece holds no setting, so the type is the default; a
  magazine out of a store comes empty, as any does (fill it at a box).
- **Spares from its own build**, chosen from its parts, the commonest first: wheels of its own kinds (front and rear may differ), its commonest spring, plates of its own
  materials and sizes (0.5 m and 1 m before quarter plates: a patch must cover a hole), its commonest frame beam (1 m and under), fuel for its engine (Small Fuel Tanks, and a
  Fuel Drum when it already runs on drums), a Battery Pack for an electric drive (a no-air context, "Spawn context").
- **Spare guns** by their parts: a receiver, barrel, muzzle device, action and magazine of a gun it mounts (the commonest first), and a spare barrel.

How much, at tier 2 (`VehicleCargoTables.ByRole`, keyed by role; a fraction is the chance of one more; ammo per gun):

| Role | magazines | boxes | crates | wheels | springs | plates | beams | fuel | spare guns | barrels |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| scout | 1 | 0.5 | 0 | 0.5 | 0 | 0 | 0.3 | 0.5 | 0 | 0.3 |
| raider | 1.5 | 1 | 0.3 | 1 | 0.3 | 1 | 0.5 | 1 | 0.3 | 0.5 |
| gun platform | 2 | 2 | 1 | 1 | 0.5 | 2 | 1 | 1 | 1 | 1 |
| carrier | 1.5 | 2 | 0.3 | 1 | 0.5 | 3 | 1 | 1 | 0.5 | 0.5 |
| tanker | 1 | 1 | 0.5 | 2 | 1 | 2 | 2 | 2 | 0.3 | 0.5 |
| recovery | 1 | 1 | 0 | 2 | 2 | 4 | 4 | 1 | 0.3 | 1 |
| command | 2 | 2 | 0.5 | 1 | 0.5 | 2 | 1 | 1 | 1 | 1 |
| boss | 3 | 3 | 1 | 2 | 1 | 3 | 2 | 2 | 2 | 1 |

A tier scales every amount (0.7 at tier 1, 1 at 2, then 0.3 a tier more), and a style its ammo and its spares (`VehicleCargoTables.ByStyle`, additive: `Styles.cs` is
untouched): the Bandits carry 1.3 times the spares (they scavenge everything), the IT Department 0.8 of both (it travels light), the Call Centre 0.8 and 0.9, Highland 1.1
times the ammo, the British 1.1 times the spares; a style not named (Militia) 1 and 1. A vehicle carries at most 18 loose pieces, of at most 12 % of its own mass.
`Constructs/Loot.json` is untouched: the spares come from the vehicle's own parts, not a table, and a role's loot still rolls as before.

**Stowed as a crew would** (the order when room runs short: Ammo Boxes, magazines, crates, fuel, wheels, a gun's parts, springs, plates, beams):

- **In slots** first, for anything that is an item: the free slots of its storage (room furniture, chests, shelves, a deck chest), nearest the gun for ammo, by the game's
  own slot rule (`ConstructStorage.Fits`: the size class and shape; a shelf's rack slot piles as many of one kind as fit). A container that rolls loot keeps at least half
  its slots for its loot. A room with no slot left gets one **spares shelf** (the style's `shelf`, else its `chest`) against its end wall, out of the room's middle, which
  rolls no loot. The blueprint lists them as `"stored"` (docs/sandbox/CONSTRUCTS.md, "Blueprints"): set in at spawn (`ConstructLoot.Stow`), the same every spawn, before
  the loot rolls into what is left.
- **Loose**, for physics-only pieces (plates, beams, Dually Wheels, a Battery Pack, Ammo Crates) and what no slot took: on its open decks (the bed, a fort's deck, the porch,
  then a roof rack for pieces of 16 kg and under), each deck with a frozen **Cargo Volume** (its control box on a free socket on a deck plate's side, its box round what was
  laid with 15 cm to spare, on the configure tool's 10 cm steps). Pieces lie on the deck's 12.5 cm grid nearest where they are used (ammo by its gun, spares at the back),
  3 cm apart and from anything built, plates, beams and wheels flat in their thinnest turn (a wheel that won't lie flat stands on its tread), plates stacked four high,
  beams three and wheels two, each a few millimetres off the one below so no faces lie flush. Nothing stands where a turret's arm or seat swings (under the ring's top within
  its reach), on a hole or over a deck's edge. A Technical's own crates (the shape's `CargoBay`) keep their place; the pass lays round them.

**In play.** A stored item is a slot's part: taken by hand (F), shot with its container, spilled when the container goes; it doesn't cook off. A loose piece is a body of its
own riding in the frozen volume: shot, lifted out and carried off; shoot the volume's control box and everything it held goes free at the vehicle's speed. **A loose Ammo
Box or Crate cooks off when shot**, as any box does (`ConstructWorld.DamagePart`), so a gun platform's crate on its open deck is a weak point the brief names
(`BriefStow.CooksOff`); a Fuel Drum or tank on the deck burns as it would anywhere. The loose pieces' mass is real: the drive check builds the vehicle with them aboard,
and the role's speed estimate counts them (`Vehicle.SpareMass`); the role's loot need counts neither (`SparePieces`), so a scout isn't over its loot for a spare magazine.

**What it says.** The kit line carries a note (`cargo 4 ammo (1 loose), 3 magazine loose, 2 wheel loose, 4 plate loose, 1 fuel loose; 170 kg loose`), `Vehicle.Stowage`
holds the manifest (kind, part, count, where, what for) and `Vehicle.KitCargoMisses` what was left behind and why. The brief's `Stowed` ("Behaviour data") lists each stack
and loose piece with the guns it serves (a magazine the guns that take it, by the workbench's rule; rounds every gun but a flame gun; fuel the flame gun), so an AI can
rearm and patch from its own cargo; the brief's summary ends `cargo 17 (6 ammo and magazines, 1 loose rounds)`.

**Measured** (2026-11-07, `--cargo all --seeds 1` and the same with `--no-cargo`: six styles x eight roles x tiers 1 to 4, seed 1, 192 vehicles; the 2-core box shared
with other agents):

| | without (`--no-cargo`) | with |
| --- | --- | --- |
| roles met | 166/192 | 166/192, the same vehicles |
| identity | | every vehicle's score the same |
| drive check | 283 plans, 4 refused | 283 plans, 4 refused (the same); the throttle pulse 0.07 m shorter on average (-0.7 to +1.1), at least 2.0 m on every vehicle (the gate is 1 m) |
| generation, warm | 650 ms a vehicle | 713 ms (the cargo pass 17 ms a plan, worst 65) |
| carries something | | 181 of 192 (Militia and Call Centre scouts and Militia raiders, Technicals with no deck and nothing rolled) |
| full check (`--role ... --tier 3 --seed-list 5 --check`, Bandit, IT, Highland, British x every role) | | 32 of 32 passed |

Pieces and loose mass a vehicle by role: scout 1.9 (19 kg), raider 6.0 (61 kg), gun platform 15.7 (147 kg), carrier 13.7 (70 kg), tanker 6.9 (92 kg), recovery 11.3
(111 kg), command 16.1 (68 kg), boss 21.5 (91 kg); 82 kg a vehicle, 3.5 % of its mass. Over the 192, in slots / loose: ammo 402 / 269, magazines 64 / 542, gun parts
14 / 515, fuel 10 / 141, springs 1 / 57, and wheels 61, plates 86, beams 69 all loose. Left behind for want of deck room (a room-only War Bus or Box Truck has no open
deck, a Gun Wagon's hutch is small, and nothing stands in a gun's line of fire): beams 81 times, plates 68 and armour plates 44, wheels 124, fuel tanks and drums 91,
crates 43. The first seed of a role made as the dynamic spawn makes it (`--role "Highland tanker" --tier 2 --sliced`) gave the same hash as made directly; a convoy
(`--convoy Bandit --seed 3`) made every member and its leader twice to the same text; a brief with its cargo saved and read back the same (`--brief`, 1,460 bytes).

A probe (a throwaway command, not kept) built role vehicles from their text in a world of their own, as a spawn does: the stowed items were in their slots beside the
rolled loot; a destroyed Chest and a destroyed Crate Stack spilled their pieces (3 and 5); a destroyed Cargo Volume let its 11 and 18 carried pieces go; a loose Ammo Box held 300 rounds, and bolted to the vehicle's
gun (the attach rules, `ConstructBuild.Place` and `Attach`) it added its 300 rounds to what the gun could draw.

**Not done.** No new part: there is no gun rack or tool rack part, so spare guns lie as parts in slots or on a deck, and a style's racks are its shelves. A room-only vehicle
leaves its physics-only spares behind (a cargo volume on a room floor would stand in the aisle the room check walks). A box's ammo type is the default (slots and cargo hold
no settings). Spares aren't added to the identity's weak points (they ride on another body): the brief's `Stowed` names them instead. Nothing was tried through a
player's own input in the game window (taking a box out with F, the attach tool).

## Quality tools

The kit will make vehicles during play with nobody looking, so these tools keep variety up and each faction looking like itself. They generate and measure (0.1 to 1 s a seed on the 2-core box); none runs the drive or gun checks unless asked, and the defaults stay small. Everything prints to `callofslop.log`. Source: `VehicleDiversity.cs` (the metric), `VehicleQuality.cs` (the commands), `Program.VehicleSheet.cs` (the sheet).

**The distance** (`VehicleDiversity`). Each seed is built from its own blueprint text, as the game builds it, and measured. Five parts, each 0 to 1, weighted into one number from 0 (the same vehicle) to 1 (nothing shared); the weights in brackets are the old ones, before the front term (2026-11-07), which `--no-sections` still measures:

| Part | Weight | What it compares |
| --- | --- | --- |
| front | 0.15 (none) | The cross-section from the front (`FrontProfile`, the same measure `--body-report` prints): the half width in 12 bands of its own height, each over the widest, for the rooms when both have rooms and for the whole body without its wheels; the mean of the two mean differences, times `FrontScale` (3), at most 1. What the side outline can't see: a pod against a slab, a gable against a flat roof. |
| silhouette | 0.35 (0.40) | Along the length in 16 slices, nose first, over the fixed parts (no wheels, no turrets, as `MaxSideFill` sees them): how much of each slice's side is filled, its plan width, and its top's height, each in its own bounding box (`VehiclePlan.Profiles`), plus the whole side and plan fill. The mean difference. |
| parts | 0.15 (0.20) | Kinds of part (`VehicleVariety.Kind`: a plate is a plate in any material or size, a beam a beam; counts capped at 8): one less their weighted overlap. |
| layout | 0.20 (0.25) | Rooms, turrets, seats, axles (wheels within half a metre are one), wheelbase, track, and the sets of wheel and spring parts. |
| size | 0.15 | The bounding box, each axis as a fraction of the larger. |

Paint and loot don't count. Silhouettes are compared in their own box, so a longer copy of a shape reads the same there and differs only in size. Read the parts as well as the total: on the Box Truck in November 2026 the silhouette part was 0.06 on average against 0.39 for parts, which is the user's "they're all too similar" in numbers. Two seeds under 0.08 (`VehicleDiversity.Alike`, set by looking at a Bandit Box Truck sheet: seeds 5, 6 and 7 at 0.05 to 0.07 differ only in their hood) read as one vehicle.

**`--diversity "Style Shape" [--seeds N] [--from S] [--seed-list A,B,C] [--alike D] [--checked]`** (default seeds 1 to 20). Prints the spread (mean distance over pairs, each seed's nearest neighbour: mean and least, with the closest pair), the mean pair by part, groups of seeds closer than D to each other (single linkage: each group reads as one vehicle), the most different set (`VehicleVariety.Spread`, as `--sweep-vehicle` picks it) beside the shipped seeds, then every trait's spread, least varied first:

```
  =rooms                  1 value(s), commonest 100 % "1"  NEVER VARIES
  fender                  1 value(s), commonest 100 % "Wide Arch Fender" of 3 listed  NEVER VARIES
  floor                   1 value(s), commonest 100 % "Bolt Deck" of 1 listed  fixed (the style lists one)
  cab                     4 value(s), commonest 50 % "Roll Bar" of 7 listed
```

A trait is a style slot the seed picked from (the choice it picked most, when it picked several times; colours as `colour ROLE`; the gear recipe as `gear`), or, with `=`, read off the body: weapons, rooms, turrets, axles, wheels, springs, length and height to the half metre. "NEVER VARIES" on a slot the style lists several choices for is a generator that always asks in the same place or a dice fork that never moves; "fixed" is the style's own choice. Over 85% one value is "rarely varies". Knobs a generator rolls itself (lengths, `Chance`) show only through the `=` traits. `--checked` runs the full check too and leaves out seeds that fail it (seconds a seed).

**`--diversity-style Style [--seeds N] [--extras]`** (default 10 a shape): the same per shape without the trait table, then the whole style: mean pair and nearest neighbour over every vehicle, the three pairs of shapes that read closest to each other (mean distance between their seeds), and the style slots that never varied over any shape.

**`--kit-report [--seeds N] [--style S] [--shape NAME] [--extras] [--check]`** (default 10 seeds; every style in `Styles.All`, its shipped shapes and the extras it names): a table of seeds that generate, the failure rate, generation time (mean and worst; one throwaway generation first, so the JIT isn't counted), part count, mass, and the commonest refusal; then the ten commonest refusals over everything, numbers and quoted names taken out so one reason on several seeds counts once. `--check` also runs the full check and counts its first failure as the reason. Eight shapes of one style at 10 seeds take about half a minute; the whole matrix about four minutes, so filter with `--style` and `--shape`.

**Identity.** `--kit-report` has two more columns: `ident`, the shape's mean identity over the seeds that generated, and `weak`, the share whose designed weak points are all there and hittable; under the table, each style's mean and weak-point pass rate, and every seed that reads more as another style. `--diversity` prints the same for its shape and each seed's breakdown ("Identity and weak points").

**`--vehicle-sheet "Style Shape" [--seeds N] [--from S] [--seed-list A,B,C]`** (default 12) under xvfb, as `--vehicle-shots`: one PNG next to the log, `vehicle_sheet_<Name>.png`, three seeds a row, each with its side and front three-quarter views, labelled with the seed, its grade if it has one, and "shipped". A seed that doesn't generate gets a dark cell. About 4 s a seed. It is for the user to grade when they ask for it: don't send sheets to anyone, and don't make them only to show work.

**Grades.** `docs/vehiclekit/grades.json` holds the user's grades, one a line, sorted: `{ "style", "shape", "seed", "grade" (1 bin it, 5 ship it), "note", "date" }`. `--grade-vehicle "Bandit Box Truck 7" 4 "good stance, dull hood"` adds one, or replaces that seed's grade; it writes only from a checkout's build. Only the user's grades go in: an agent records what the user said, never its own opinion. `--grade-report [Style]` turns them into suggestions per shape: the mean, seeds graded 4 or 5 that don't ship yet (commit them: add them to the style's `Seeds` and run `--gen-vehicle`), shipped seeds graded 2 or less (drop them), low grades' notes; then per style, trait values whose seeds grade a point or more under the style's mean (two seeds or more each; each graded seed is generated again to read its traits). It is data only: nothing changes a weight or a list by itself.

**A round of grading.** `--diversity` (or `--kit-report`) for the numbers; `--vehicle-sheet` of the most different set and a few from each too-alike group when the user wants to grade; their grades with `--grade-vehicle`; `--grade-report` for what to ship and which traits to look at in the generator or style.

## Identity and weak points

As the grammar makes more kinds of outline, every vehicle must still read as its faction, and it must still have the cheap, exposed parts a player shoots to stop it
without wrecking what is worth salvaging (the loot loop: disable, then strip). `VehicleIdentity.cs` scores a built vehicle for both. It runs no physics and uses no dice: 15
to 50 ms a vehicle on the 2-core box.

**A style's signature** (`StyleSignature`). `FactionStyle.Signature` when a style sets one; else the table `VehicleIdentity.Signatures`, by style name (the shipped
styles, written from what this file and their slots say; `Styles.cs` is untouched); else one derived from the style's own dressing slots (`Derived`: its spike, trophy, mast,
gear and piper parts are its characteristic parts and its one mark; tyres and fuel its weak points), which is what Militia gets. A signature holds:

| Field | What it is |
| --- | --- |
| `Marks` | Features every vehicle carries (`StyleMark`): at least `Min` of some parts, on every vehicle that meets `When` ("seat", "room", "heavy", "light"; empty: all), and with `Open` > 0 seen from that many of the six directions. |
| `Parts`, `PartTarget` | Characteristic parts and how much each says "this style" (a name counts at most three times); `PartTarget` of it reads as fully this style. |
| `Modules` | Characteristic grammar modules by family (`cab`, `nose`, `flank`...), each family judged against its best; a module not named counts 0.3. Kit 2 only. |
| `Silhouette` | The outline range (`SilhouetteRange`): side fill, plan fill, height over length, roofline wander and rake (how far the ends drop below the highest point). |
| `WeakPoints` | The designed weak points (`WeakPointRule`), below. |
| `Threshold` | Under this (default 0.75), or with a mark missing, a kit-2 plan is planned again. |

A part is picked by name or by a role keyword (`PartPick`): "fuel" (any fuel tank: a drum, a tanker's barrel), "tyres", "driver" (the driver's seat), "seat", "engine",
"ammo" (ammo boxes and crates).

**The score**, 0 to 1, six parts weighted into one (brackets: before the section, 2026-11-07, as `--no-sections` measures); a part with nothing to judge (no paint, no marks that apply, no seat for traits: a trailer's dressing is its tower's) is
left out and the rest weighed up:

| Part | Weight | What it measures |
| --- | --- | --- |
| marks | 0.30 | The share of the marks that apply which are there (and in the open where asked). |
| palette | 0.15 (0.20) | Painted parts in the style's colours (0.6) and how many of its palette roles show at all (0.4). |
| traits | 0.20 | Characteristic parts against `PartTarget`, and for a grammar vehicle the mean of its modules' weights; the mean of the two. |
| silhouette | 0.10 (0.15) | Each of the five outline numbers in range scores 1, falling to 0 a quarter of the range (plus 0.05) outside it. |
| weak points | 0.15 | Each rule that applies: 1 there and hittable, 0.25 there but covered, 0 missing. |
| section | 0.10 (none) | Each room bay from the front (`FrontProfile`) against the nearest of the style's body languages as the style draws them (`BodyStyleRule`): within `SectionFloor` (0.008 mean difference) reads 1, `SectionScale` (0.03) further 0; the mean over the bays. No room: left out. A pod on a Bandit truck reads less Bandit; kit 3's fixed bays read about 0.45 on an IT bus. |

The breakdown reads `0.78 as IT: marks 1/2 (no mast or dish); palette 100 % (4/4 roles); traits 0.92 (parts 1.00, modules 0.83); silhouette 5/5; weak points 2/3
(antenna missing, tyres 6/6 hittable, fuel 1/1 hittable)`.

**Weak points.** A rule names a part pick and how exposed it must be: seen from `MinSides` of the six directions (a ray from far off along each axis, at the part's middle or
one of four points round it, meets that part first), and hit first by a ray from a standing player's eye (1.6 m over the ground under the wheels) `Range` metres away (20 by
default, and outside the vehicle's footprint) from `MinBearings` of 16 bearings round it. Rays go through what is drawn, every part's real boxes and meshes in the rest pose,
wheels and turrets included (`VehiclePlan.PartTriangles`, the triangles `VehicleChecks.Gaps` shoots through, each tagged with its part), so a drum behind a bed wall, a
crate in a room's magazine or an antenna under a rack counts as covered. `Min` of the picked parts must pass; `Optional`: none there doesn't count against it. The rays
are a geometric test: they don't know a part's health, armour or what a round does on the way, and a hit on a part's own other face counts as a hit.

**Guaranteed marks (`Vehicle.GuaranteeMarks`, `VehicleMarks.cs`).** Kit 2 doesn't leave a mark to chance: after the dressing (`Trim`), every mark of the signature that
applies and that the vehicle has too few parts of (`VehicleIdentity.Lacking`, by name; a mark picked by role, the Bandit's fuel in the open, is the shape's to carry) gets
one, as the Pipe Box pass stands its box. Each mark rolls on its own fork of the seed (`"mark " + label`), so nothing else of a seed moves. Its parts are the mark's names
the style lists in its `mast`, `spike`, `gear` and `trophy` slots, as often as it lists them, then the rest of the names; it tries them on a ranked list of spots:

1. up-facing free sockets on a room's roof, the cab shell, the hood, a roof rack, the bed, the porch;
2. a post: half a metre of the frame's beam (in the frame's paint) on the rearmost free top socket of the body's frame and plates, the mark on its tip: where a War Bus
   whose roof sockets all went to turrets still has room;
3. sideways on the headboard, the flanks, a room's walls, the cab and the hood's sides.

Every spot is tested before anything goes on, since nothing placed is taken off again: an upright mark wants open sky over its footprint (not under a rack, a deck or an
overhang, so the antenna or flag weak point is hittable), stays out of every turret's sweep (lower than the ring's top, or further from it than the longest gun reaches
or the gunner's arm, 1.2 m), and a post wants room for itself and the mark. There is no bracket on a turret ring: the ring's fixed side sockets sit inside the sweep of the
gunner's arm and seat. A mark with nowhere to go is left missing and the gate below re-plans as before. The pass runs on every kit-2 plan, before the stability and fill
checks, and on the seeds of the kit-1-only shapes (Technical, Fuel Trailer, Tanker Rig) that ship nowhere (`VehicleRegistry.Make`: not in the generator's
`Seeds`, or with `--kit 2`), so a dynamic Technical carries its mast too while every committed file stays as it is. `--identity ... --raw` lists what it placed
(`marks placed: flags, beacons or brollies: a Belisha Beacon on the Cab Over Shell`).

**In generation.** `ShapeGrammar.Make` hands each built plan to `VehicleIdentity.Gate`. Under the threshold or with a mark missing, the seed plans again from the next fork
(logged: `[vehicles] IT War Bus 1: plan 1 reads 0.78 as IT (no mast or dish): ...; planning again`); the second plan is kept if it is better, else the first, so identity
costs at most one more plan (a shape that can't carry a mark would otherwise pay for all four). If every later plan fails as it is built, the best plan scored is kept.
Kit 1, the committed seeds included, is never re-planned: it is only reported. `--no-identity-gate` turns the re-plan off.

**Against every style.** `--identity` and the quality tools score each vehicle against every style in `Styles.All` and report any that reads more as another style than its own.
Bandit is nearly always the runner-up: many styles start from the Bandit slots, every vehicle carries its fuel in the open, and an Iron Spike is a Bandit mark.

**Measured** (6 seeds of every shape a style ships, generation only, 2026-11-06; kit 1 for the Technical, the trailers, the Tanker Rig and the Double Decker, kit 2 for the
rest; `--kit-report --seeds 6`):

| Style | Mean identity | Weak points all pass | Read as another style | Kit 2 with every mark, gate off / on | Closest margin to another style |
| --- | --- | --- | --- | --- | --- |
| Bandit | 0.94 | 47/47 | 0 | 22/22 / 22/22 | 0.23 (Militia) |
| IT | 0.89 | 35/48 | 0 | 19/26 / 22/26 | 0.11 (Bandit) |
| Call Centre | 0.89 | 35/48 | 0 | 14/23 / 20/23 | 0.07 (Bandit) |
| Highland | 0.96 | 48/48 | 0 | 20/20 / 20/20 | 0.18 (Bandit) |
| British | 0.89 | 52/52 | 0 | 13/24 / 15/24 | 0.10 (Bandit) |
| Militia | 0.91 | 48/48 | 0 | 30/30 / 30/30 | 0.10 (Bandit) |

What it found: the IT and Call Centre vehicles carry no mast or dish on 16 and 19 of 42 vehicles with a seat, the most on the Technical (kit 1: one in six has one) and the
War Bus (kit 2: its roof's sockets go to turrets first, and a re-plan doesn't always help: 2 of the 5 kit-2 IT seeds measured still have none); the British Scrap Tanker often has neither a flag nor
anything for tea (no room, no cab shell to dress). Those were a generator's job, not the score's: the guaranteed marks pass above. Tyres and fuel are
hittable on every vehicle; the only covered weak points seen are the rounds in a room's magazine, which is why the Bandit's "ammo crate" applies to light vehicles only.
Generation with the gate cost IT and the Call Centre about a fifth more over all their shapes (a War Bus 1.2 s on average, 2.3 s at worst), the rest nothing measurable.
The silhouette range is the same for every style: outlines come from the shapes, which every style shares, and the styles' 5th to 95th percentiles agreed to a few points,
so for now the range keeps a style off a box or a sliver rather than telling one from another.

**With guaranteed marks** (the same 6 seeds of every shape, `--identity STYLE --seeds 6`, 2026-11-06):

| Style | Kit 2 with every mark, before / after | Identity re-plans (kit 2) | Mean identity | Weak points all pass | Dynamic kit-1 Technicals with every mark (seeds 30 to 37) |
| --- | --- | --- | --- | --- | --- |
| IT | 22/26 / 26/26 | 7 / 0 | 0.89 / 0.93 | 35/48 / 42/48 | 7/7 |
| Call Centre | 20/23 / 23/23 | 9 / 0 | 0.89 / 0.93 | 35/48 / 43/48 | 6/7 |
| British | 15/24 / 24/24 | 11 / 0 | 0.89 / 0.95 | 52/52 / 52/52 | 7/7 |

Every vehicle still short of a mark is a committed kit-1 seed (the IT and Call Centre War Bus 6, Scrap Tanker 1, Gun Wagon 1 and their committed Technicals, the
British Box Truck 1, Double Decker 1 and 5), which the pass never touches; the antenna weak points that fail are those seeds' missing antennas, and every mark the pass
placed is hittable. The one dynamic Technical without a mark (Call Centre 32) has its only free spots under the gun's sweep. With no identity re-plans left, a kit-2 IT War
Bus took 0.78 s on average and 1.2 s at worst in a run as quiet as the one before (its kit-1 shapes took the same time; 1.2 s and 2.2 s before), the Call Centre's
0.60 s and 1.1 s (0.95 s and 1.8 s); later runs with other builds on the 2-core box read up to 1.3 s on average for both kits alike, so read the re-plan count as the cost.

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

Rust and bare metal, one colour of frame, war paint on a few panels, plates that don't match and some that are missing. `tools/constructs/bandit_parts.py` writes what the game didn't have (`Constructs/Parts/Bandit`): the Fuel Drum, spikes, skulls and a hubcap. Run `tools/constructs/assembling.py` after it for their recipes.

The designed weak points: the Fuel Drum (55 health, and on the technical the only fuel) stands in the open, its pipe under the floor; nothing covers the tyres; the driver sits in the open with no roof or glass. On the Scrap Tanker the big tanks stand in the open too. **Signature** (`VehicleIdentity.Signatures["Bandit"]`): marks, fuel in the open (seen from two sides) and spikes or trophies (Iron Spikes, skulls, a Hubcap Plate); characteristic parts those, the drum, a Ram Plow, mesh-guard and slit cabs; modules caged and armoured cabs, ram and plated noses, panniers. Weak points: the fuel, two tyres or more, the open driver on light vehicles, and a light vehicle's ammo crate when it has one. A technical's crates ride loose in a Cargo Volume under the bed, so they stay aboard until the volume breaks or someone lifts them out.

Loot: containers in rooms roll from the `bandit` table in `Constructs/Loot.json` (docs/sandbox/CONSTRUCTS.md, "Loot"). Tier 1 and 2 parts, common and uncommon weapon parts. **Ammo Crates stay out of it on purpose:** a stored item doesn't cook off, and a crate is 45 kg, physics-only, so it couldn't go in a slot anyway. An Ammo Box in a slot is safe loot; a technical's loose Ammo Crate is the one that blows.

### The IT style

The IT Department (`Styles.It`, group "IT Department"), a parody of modern office work and the first faction to try faction work with. Beige cases and carpet-tile grey on the panels, corporate blue on the hoods, war paint in lanyard yellow, link blue, paper white and failed-build red; plates that line up (it passed an audit). `tools/constructs/office_parts.py` writes its dressing (`Constructs/Parts/Office`): a CRT Monitor, an Office Plant, a Desk Fan and an Employee of the Month plaque as trophies, Router Antennas and a Wi-Fi Mast as spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room. Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). It is the tech faction, and its vehicles carry IT hardware: a Satellite Dish, a Network Switch and a Cable Reel join the trophies, and a Satellite Dish the spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room, furnished with Server Racks (loot on their shelves), Computers and Fax Machines: the Computer and the Fax Machine are the only line to the IT Department (docs/story/STORY.md, "The IT Department's fax"). Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). Loot rolls from the `it` table: circuit boards, wiring, controls and readouts up to tier 3, Fax Machines, Computers and Server Racks, common and uncommon weapon parts. Every IT shape ships its own seeds. **Signature**: marks, mast or dish (Satellite Dish, Wi-Fi Mast, Router Antenna) and office gear (monitors, switches, reels, plants, fans, plaques, racks, computers, fax machines); modules enclosed and cab-over cabs, short and flat noses, bare flanks. Weak points: the antenna (two sides, two bearings), two tyres, the fuel.

**More dressing (`Vehicle.Kit`).** A style with a `"mast"` slot gets a last pass of its own (its own fork, so nothing else of a seed moves, and a style without the slot never rolls it): masts on the rooms' roofs and the cab, spikes down the rooms' outer walls, and its `"gear"` standing on open beds and porches. The IT style's masts are dishes and antennas, its gear reels, switches, monitors and dishes. The dressing rolls how many; the guaranteed marks pass ("Identity and weak points") then puts a mast and a piece of office gear on any kit-2 vehicle still without one, on a post at the back when the roof went to turrets.

### The Call Centre style

The Call Centre (`Styles.Scam`, group "The Call Centre"): the Scammers (docs/story/STORY.md, "The Scammers") on wheels, a parody of the scam call centre. Cubicle-fabric greys on the panels, corporate teal on the hoods, war paint in WINNER yellow, hot pink and dollar green. It starts from the IT style's slots and swaps the dressing: `tools/constructs/callcentre_parts.py` writes `Constructs/Parts/CallCentre`: an Office Phone, a Call Headset on a stand, a GIFT CARDS sign and a Prize Wheel as trophies and gear, Loudhailers down the flanks as spikes, Phone Masts on the roofs. Rooms are a call floor (rows of Cubicle Desks round a Water Cooler), the manager's office (a Computer, a cubicle, a Server Rack) or a break room; each Cubicle Desk has a drawer of loot. Guns are named for the job: the Cold Caller (a carbine), Robocall (a machine gun) and Final Notice (a pump gun). They run on other people's batteries, so loot rolls from the `scam` table: batteries, circuit boards and wiring up to tier 3, the odd Computer, common and uncommon weapon parts. Every shape ships its own seeds (the sweep's most different passing ones). **Signature**: marks, phone mast or loudhailer and call centre gear (phones, headsets, GIFT CARDS signs, the prize wheel, cubicles, the water cooler); modules as IT's. As for IT, the guaranteed marks pass ("Identity and weak points") gives every kit-2 vehicle a Phone Mast or a Loudhailer when its dressing didn't. Weak points: the antenna (a mast or loudhailer), two tyres, the fuel.

### The Highland style

The Clan MacSlop (`Styles.Highland`, group "Clan MacSlop"), a Highland clan on wheels and the loudest faction on the map. Tartan reds, hunting green, navy and tweed on the panels, Saltire blue and white on the hoods, war paint in pillar-box red, lion-rampant yellow, white and black; plates a little crooked (well loved, not neglected). `tools/constructs/highland_parts.py` writes its parts (`Constructs/Parts/Highland`): Saltire Flags on the roofs (masts), Thistles, a Haggis on a Post and a Coo Heid as trophies, Wee Cabers down the flanks as spikes, Wee Kegs on the beds, and Whisky Casks in the rooms (two loot slots each). Rooms are a bothy, a distillery, a ceilidh hall or a kit room. Guns are named for the glen: the Claymore (a pump gun), the Sgian Dubh (a carbine), the Highland Charge (a machine gun) and the Caber Toss (the harpoon). Loot rolls from the `highland` table: tier 1 and 2 drive parts, pump guns and carbines, and the odd Pipe Box.

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


## How it scales

Measured on this box (2 cores) with a mix of the 19 shipped vehicles on Proving Ground, driven directly in a `ConstructWorld`; no clients:

| Vehicles | Parts | Spawn each | Memory | Parked ms/tick | All driving ms/tick (worst tick) |
| --- | --- | --- | --- | --- | --- |
| 10 | 1,568 | 9.4 ms | 1 MB | 0.08 | 0.50 (1.3) |
| 50 | 7,820 | 8.5 ms | 5 MB | 0.44 | 2.53 (4.2) |
| 100 | 15,640 | 8.2 ms | 9 MB | 0.59 | 2.14 (4.9) |
| 200 | 31,203 | 8.1 ms | 19 MB | 1.67 | 4.90 (7.6) |

Parked vehicles sleep, so a map of them costs little; a spawn is 4 to 16 ms, so spread big groups over ticks. A blueprint file is 9 to 21 KB. Generating one takes 0.2 to 1.1 s (the socket search, on the same thread), so it can't run in a tick.

**Files or generated.** Keep shipped vehicles as a few seeds a shape, for review and the menu. Don't commit hundreds: the game builds each blueprint once at start-up to check it (about 8 ms each), and the Blueprints tab's spawn message carries the index as a byte (`CActKind.Blueprint`), so 255 files is a hard limit (204 blueprints ship today). Variety comes from dynamic spawn instead: any seed, made on the server when it is asked for, with nothing on anyone's disk.

## Dynamic spawn

The server makes a faction vehicle when it is asked for, from (style, shape, seed), by running the generator in the game (`Source/Game/GameSandboxVehicles.cs`, `Source/VehicleKit/VehicleSpawn.cs`).

```csharp
game.VehicleSpawn.Request("Bandit", "Box Truck", seed: null, ground, yaw, owner: 255, frontOf: 255, done: (built, name, why, owner) => ..., layers: new(Wear: 99));
```

`seed` null: the server picks one (1 to 999,999). `layers` (optional): kit 3's per-layer seeds ("Layered variety"), kept if the seed rerolls. `ground` and `yaw` say where it stands, or `frontOf` names a player it is set down in front of, as they stand when it is ready (the parts menu's way). The return is why it was refused, or null: queued. `done` hears how it went, on the tick. At most 8 wait, 2 from one player.

**The parts menu.** The Blueprints tab has "Faction vehicles: new seed" (on by default) and a seed box (0: any); by role and by convoy: "Families and convoys". With it on, a faction vehicle's tile (a blueprint named "Style Shape seed") asks for a new one of that style and shape (`CActKind.Vehicle`: the style's and the shape's names and a seed; protocol 186); off, the tile spawns the committed file as before (`CActKind.Blueprint`, which tests and every other blueprint still use). The host also has "Server generates them": off (`VehicleSeeds.Committed`), every request gets a committed seed of its style and shape, the asked one when it ships, so a session plays only what CI checks.

**Only the server generates.** A built vehicle is an ordinary construct: clients get it as bodies (`CBody`), as they get anything built, so nothing about the seed is on the wire and a client never runs the kit. That is simpler and safer than clients regenerating: no machine-to-machine determinism to trust, no fallback to send a blueprint when a hash disagrees, and nothing changes when a client's build differs in a way the protocol doesn't catch. The text's FNV-1a hash is in the log line (`[vehicles] generated Bandit Box Truck 733786 (hash ...)`), and a seed always writes the same text: in game and in `--gen-vehicle` alike (the first shipped seed of every shape in every shipped style, made in slices this way, matched its committed file byte for byte). It saves and loads with the world and is salvaged like anything else, because that is all it is.

**Off the tick, but never beside it.** A generation costs 5 to 900 ms (measured on this box, warm: a Fuel Trailer 5, a Box Truck 180 to 400, a War Bus 420 to 560, the Double Decker 900), far past the 8 ms tick. `VehicleGenJob` runs it on a thread of its own, but that thread only runs inside `Step`, while the tick that called it waits: the tick gives it `VehicleSpawner.SliceMs` (2.5 ms) and it hands back at the next `VehicleGenJob.Yield`, which the kit's own steps call (each socket try in `Put`, after each part, weld, wire and link; between steps of the game's own build code, never inside one). So the two threads never run at once, nothing the game shares (the snap build's caches, the weapon builder, the statics `World.Enter` swaps) is touched by two threads, and no part of the game had to be made thread-safe. The cost: a generation's time comes off the tick's, a slice at a time, so a Box Truck takes 100 to 600 ticks (2 to 10 s; a seed that plans three times is the long end) and a Double Decker about 250. A generator stuck between yields for 10 s is abandoned (logged) and the request falls back. `Yield` costs one thread-static read anywhere else, so the command line and the self-tests run as before.

**Short spans.** A slice ends at the first yield after its 2.5 ms, so a slice is its budget plus the span it was in. Measured span by span (a stopwatch from each yield to the next, labelled by call site; 2026-11-06), the long ones were: `Put`'s scan of every free socket of every part before its first try (about 1 ms on a 300-part body, it now yields per part); a part's attach and the construct's rebuild after it (now a yield between them, and the same in `Weld` and `Wire`); `Fill`, the grammar's silhouette measure, 4 ms warm and 20 ms cold (`VehiclePlan.Outline`: it now yields per part and every 128 triangles, keeps its face and triangle buffers for the next call instead of a megabyte or two on the large object heap each plan, and makes its two projection delegates once instead of twice a triangle); a plan again, whose new world and plan ran with the failed plan's measure (yields either side of it); and the blueprint text (a yield every 64 steps), its hash and its parse, each a span of its own. The rebuild itself was never the cost (about 0.1 to 0.6 ms on a big body). Warm, no span averages over 0.2 ms and the longest ones are 1 to 3 ms; past that on this box it is the machine, not the kit (below). The output is the same: every committed seed `identical`, and a sliced job's hash equal to a direct run's for kit 1 and kit 2 seeds of every shape tried.

**The first one.** Without a warm-up the first generation in a process paid for compiling the kit and the build code it calls: about a second in all, with spans of 40 to 80 ms (the first rebuild of a construct with guns, the first link). In the command line it also loads the parts (1.8 s), which a game has done long before. `VehicleGenJob.Warm` runs a Bandit Technical once, at once, in a world of its own (written, hashed, parsed, measured as a spawn's is) and plans a grammar Box Truck without building it, when the server starts its first Sandbox match (`ResetSandbox`): 700 to 900 ms on this box, once a process, logged as `[vehicles] warmed the kit in N ms`. A first spawn's longest span after it is 3 to 6 ms (the grammar's builder, compiled on its first use).

**An honest slice.** A slice over `VehicleGenJob.OverrunMs` (4 ms) is logged, the first 10 and then every 100th: `[vehicles] slice 7.4 ms in Bandit Box Truck 733786 (budget 2.5, the kit ran 7.4 of it): longest span 5.7 ms, VehicleCore.cs:226 to VehicleCore.cs:157`, with ", a garbage collection ran" when one did. `Yield` takes its caller's file and line from the compiler (constants: nothing is allocated), so the two yields named are either side of the long span. "The kit ran" is the slice's spans added up: when it is near the slice, a span needs a yield inside it (or the machine took the CPU in it); when it is much less (2.5 of 5, 0.3 of 5), the thread woke late. The "generated" line gives the generation's longest span and where it ran.

**Setting it down, a slice a tick.** Building the blueprint (`ConstructBuild.BuildOnGround`, all at once) cost 6 to 20 ms for a big vehicle (a Box Truck 6 to 10, a War Bus 7 to 10, the Double Decker 12 to 19, measured on a tick on this box; 37 ms when first measured), past the tick's 8 ms. Two changes:

- Plain waste, gone: a step looked its part up by walking every part's name (now a dictionary, `ConstructParts.Find`), each wire rebuilt the whole body (now once after all of them, `AddWire(rebuild: false)`), and each part's auto-link allocated a list and walked every touching part's links before checking where their sockets were. That halved it (a Double Decker about 7 ms in all), with every blueprint building byte for byte as before (all 204, two poses each, compared with the old build).
- What is left runs in slices (`ConstructBuild.GroundJob`). The steps run a `VehicleSpawner.SliceMs` (2.5 ms) slice a tick on a `ConstructWorld` of their own, which nothing simulates, saves or sends; no step is over 1 ms (a freeform weld's rebuild), so a slice ends within about a millisecond of its budget. Then, on a tick of its own, the body joins the real world whole (`ConstructWorld.Adopt`: a new id, its parts' tags and stores from this world, the caps) and what follows the steps runs there at once (`FinishBuild`: wires, routed links, hoses, ropes, tracks, the rebuild, cargo; then the loot roll and standing it on the ground): 1 to 3 ms. Where it stands is chosen at the join, so "in front of the player" is where they are then. A tow's steps follow its tower's the same way and it joins on the same tick. Anything a fresh body doesn't hold (a merge that popped a part off, a store holding parts) isn't adopted: it is built again at once (logged as `[build]`), correct at a tick's cost.

Measured in game (host and a `--net-client` over RakNet at 80 ms and 2% loss, Construct Yard), the worst server tick while setting one down: Box Truck 4.0 ms (2 ticks of steps, join 3.1 ms), War Bus 3.0 ms (3 ticks, join 1.1 ms), Double Decker 2.6 ms (3 ticks, join 1.3 ms), the RV 2.7 ms; the client's copy had the same parts, links, joints, routes and mass. A blueprint of at most `VehicleSpawner.ImmediateSteps` (48) steps, every hand-made one but the RV (under 2 ms), is still set down at once.

The parts menu's big blueprints (a faction vehicle's file with "new seed" off, the RV) queue in the same `VehicleSpawner` (`RequestBlueprint`: the same limits, "placing the ..." then "the ... is down"), so they arrive a few ticks later rather than on the click. The survival start kit and a map's own constructs are still built at once: that is the match's start, not a tick in play. Self-tests and the kit's checks build at once too (`BuildOnGround`, unchanged).

**What a spawn costs a tick** (2026-11-06; an offline host on Construct Yard, the game's own `Update` timed tick by tick while a request is generated and set down; the same throwaway probe on main before and on this branch after, on this 2-core box with other agents building, load 3 to 5):

| Vehicle (kit) | Worst tick before | Ticks over 8 ms before | Worst tick after | Ticks over 8 ms after |
| --- | --- | --- | --- | --- |
| Bandit Box Truck 733786 (2, three plans) | 42.2 ms | 51 of 419 | 6.1 to 8.5 ms | 0 to 2 of 490 to 640 |
| Bandit War Bus 5001 (2) | 24.0 ms | 23 of 280 | 7.9 to 10.8 ms | 0 to 1 of 400 to 480 |
| Bandit Flatbed Fort 5002 (2) | 12.0 ms | 8 of 195 | 6.0 to 10.9 ms | 0 to 4 of 280 to 400 |
| Bandit Double Decker 5003 (1) | 12.0 ms | 9 of 156 | 4.9 to 7.3 ms | 0 of 250 |

Idle, the same tick is 0.1 ms. The slices take longer in all (a slice now ends sooner after its budget, so a generation takes more of them). The garbage collector paused 0 to 2 ms in a whole generation. What is left over 4 ms is this box: a bare thread hand-off here takes 13 us at the median and up to 4.6 ms in its tail (2,000 hand-offs, load 3), and a tick has two; the overrun lines say so ("the kit ran 0.3 of 5.0"). Spinning the tick while it waits, rather than sleeping, was tried and made it far worse here (the kit's thread then has one core less to run on), so it waits as before. A machine with free cores should see slices of the budget plus a span: 2.5 to 4 ms.

**Which kit.** A request's seed is made by its own kit ("Shape grammar"): a committed seed as its file, anything else by the grammar where the shape has a recipe. The
"generated" line names it, one line a spawn (`[vehicles] generated Bandit Box Truck 733786 (hash ..., kit 2: body room+bed, cab caged, ...) in 1305 ms over 489 ticks, longest slice 5.4 ms, longest span 3.3 ms (...)`, `VehicleGenJob.Kit` and `Modules`), and the vehicle's blueprint
text carries it in its kit line. A grammar seed that fails as it is built plans again inside the generation (up to four plans), before the rerolls below.

**Seeds that fail.** A seed whose generator throws (some seeds have nowhere to stand a part; the sweep skips them) or whose text doesn't load rerolls up to 3 times, each new seed derived from the last (`VehicleSpawnKit.Reroll`: the same seed always rerolls the same way), then a committed seed of that style and shape stands in (`VehicleSpawnKit.Committed`). A style that ships nothing (Militia) has nothing to fall back to: the player is told. Failures are logged, the first 10 and then every 100th. The full check (`VehicleChecks`: driving, turning, firing for simulated seconds) doesn't run at spawn; the drive check below does, so a seed that builds but stands crooked or doesn't move is made again. `--sweep-vehicle` gives a shape's pass rate; run it before calling a shape fit for dynamic spawn.

**The drive check** (`VehicleDriveCheck.cs`, `Vehicle.DriveGate`). Every vehicle no file holds (a grammar plan, a kit-4 plan, a kit-1 seed that ships nowhere, anything in a spawn context
that matters) is built from its own blueprint text in a world of its own, settled for 60 ticks on a flat floor, measured (at most 8 degrees off level, as the full check), then
given 75 ticks of full throttle through its seat: its centre of mass must go 1 m forward (a working Box Truck goes 2 to 6 m) and it mustn't tip past 25 degrees. A refusal throws
as a refused part does, so the grammar takes its next plan (as the identity gate does: a plan that fails it never counts as built), kit 4 its next plan or derived seed, and a kit-1
seed rerolls in the spawner. A committed seed is never checked (CI checks it), so `--gen-vehicles` is untouched. It yields as the kit does: the build goes a step at a time
(`ConstructBuild.GroundJob` on a world of its own, 0.4 ms at a time, then `Join`), the physics a tick at a time. The physics runs inside a world context of its own (flat, plain
straight-down gravity at the spawn context's strength, no gravity parts, no planetoid), entered round each span and left before each yield, so the game's statics (gravity, its
gravity parts' tables, the ride bodies) are never touched and a seed checks the same in game as on the command line (a sliced job's hash matched a direct run's). The kit line
doesn't change: what it measured is `Vehicle.DriveCheck`, and a refusal is logged (`[vehicles] Bandit Box Truck 2 [recovery, tier 1]: the drive check refused it (a 1.25 s
throttle pulse moved it 0.43 m (at least 1; rpm 3040)); planning again`). Measured 2026-11-07: `--kit-report --roles --seeds 2` over every style, role and tier (384 vehicles, 557
plans checked): 9 plans refused (1.6 %), each a real failure the full check confirms, run with `--no-drive-gate`: an IT Scrap Tanker 2 for a tanker whose wheels spun on the spot
at every tier (0 m in 6 s, "forward takes it backwards"), a Militia War Bus 1 for a boss at every tier (0.7 m a pulse; 14.4 m in the check's 6 s), a Bandit Box Truck 2 for recovery
at tier 1 (0.4 m; 6.8 m in 6 s). Each was planned again into a plan that passes it (the full check on those not run). The old leader of Bandit convoy 3 fails it (10.4 degrees). 8 ms mean a plan over the matrix
(Technicals are quick), 15 to 50 ms for a Box Truck warm, 126 ms worst (cold); in slices, no span of its own showed among a generation's longest.

A towed vehicle (the Tanker Rig's trailer) comes with it: a committed trailer when its blueprint ships, else made by its own generator in the same job. A trailer (`VehicleCheckOptions.Towed`) is never pulsed: it has no engine, so the gate
passes it (`DriveGate(towed: true)`, `VehicleRegistry.Towed`). Before 2026-11-09 it was pulsed and refused ("no engine"), and the rig picked its trailer from the Fuel Trailer
class's own seeds rather than the style's shipped ones, so British Tanker Rig 19 towed an unshipped "British Fuel Trailer 2": its check (`--gen-vehicle`) and its spawn failed
while `--gen-vehicles`, which never builds a tow, reported it identical. That looked like a seed depending on what ran before it; it was not. The rig now picks from the style's
shipped trailer seeds (rig 19 tows trailer 3), and `--gen-vehicles --order reverse` or `--order shuffle:N`, with `--between`, shows each seed's text is the same in any order.

## Behaviour data

Roadmap phase 8 (`VehicleBrief.cs`, `VehicleBriefMake.cs`, `Source/App/Program.VehicleBrief.cs`): every generated vehicle carries a **brief**, what an AI driver or gunner
needs to know about it, worked out once when it is made, so whoever writes the AI never has to analyse a vehicle. It is data only: nothing reads it yet but the debug view.
It changes nothing that is built: every committed seed, kit 3 and kit 4 seed, model and context makes the same text and hash as before (`--gen-vehicles`: all 123 identical;
a kit-4 role seed made in slices gives the same hash as made directly).

**Where it is.** `Construct.Brief` (a `VehicleBrief`), on the server only. It is never sent to clients (nothing on the wire, no protocol change): an AI runs where the
physics does. It saves with the world, at the end of the body's bytes after its tracks (docs/sandbox/WORLD-SAVES.md), 600 to 950 bytes a vehicle; an old save has none, an
older build stops reading before it, and a brief that doesn't read is left off with a log line, never the body. A brief of a later `FormatVersion` is skipped. Format 2 (phase 9) adds `Stowed` at the end; a format 1 brief reads with none.

**When it is made.** In the generation's own sliced job (`VehicleGenJob.MakeBrief`, after the blueprint text and its hash, so it can't change them), from a body built from
that very text: the drive check's ("The drive check"), when the plan kept was checked with the same steps (kit 4 notes its role after the check, so the names differ but the
steps don't: `VehicleBriefMake.SameBuild`), else a drive check run for it, whose verdict isn't a gate. It reads only the body's rest frame, so the pulse the check drove
doesn't matter. Every ray yields, so it adds slices, not long ones; 20 to 110 ms of a generation on the 2-core box under load (a Highland Box Truck 49, an IT War Bus 104; the rays dominate). When the vehicle is down,
the spawner puts the brief on its construct (`VehicleSpawner.Brief`): each part reference takes the built body's tag after its def is checked against the part at its index
(a brief that doesn't fit is left off, logged), with its convoy place and model. The warm-up compiles the path once. `VehicleGenJob.BriefsOn = false` turns it off.

**What has none:** a committed seed that stands in for a failed one, a blueprint from the parts menu, a tow, a player's own build, and anything in an old save.
`VehicleBriefMake.Rebuild(construct, name, style)` makes one from any construct (no role, no drive numbers, no loot list), at once on the calling thread: run it off the
tick, or accept 20 to 110 ms.

**Part references** (`BriefRef`): the part's stable `CPart.Tag` (the server's; saved), its index when last found (a hint), its def, and a point on it in the body's rest frame
(`construct.ToWorld(r.At)` for anything rigid with the body; a turret's points are at rest). Parts break, are shot off and reload in another order, so before reading
indices call `brief.Refresh(construct)`: nothing to do unless the construct was rebuilt since (`RebuildStamp`); then each reference checks its hint and otherwise finds its tag
(a scan of the body's parts). It allocates nothing either way (measured: 0 B; 1000 refreshes with good hints 0.24 ms). Then `VehicleBrief.Has(c, r)` (there and not broken)
and `VehicleBrief.Index(r)` (-1: gone).

**Directions** are the vehicle's own: `Forward` is the drive line's forward (`Construct.DriveFwd`, level), bearing 0 is dead ahead and bearings go clockwise seen from above
(90 is its right; `Way(bearing)` gives the body-frame direction). Gun arcs use 32 bins of 11.25 degrees, weak points 16 of 22.5. Sides are 0 front (-45 to 45), 1 right, 2 back,
3 left (`SideOf`).

| Field | What it holds | Where it comes from |
| --- | --- | --- |
| `Name`, `Style`, `Shape`, `Role`, `Tier`, `Kit`, `Model` | Who it is: the role and tier (kit 4), the family model's key | the kit |
| `Slot`, `Convoy`, `Formation`, `ConvoyIndex`, `Lane`, `Leader` | A convoy member's place (leader, escort, cargo), its convoy's seed, column or wedge, its place behind the leader (0 the leader), its lane (-1, 0, 1), the leader's construct id | the spawner, when it is set down |
| `Mass`, `Length`, `Width`, `Height` | kg; metres along, across and up | the body |
| `TopSpeed` | the cheap estimate (`VehicleRoles.Speed`, as `RoleFit`) | the engine's power, mass, frontal box |
| `Measured`, `PulseMoved`, `PulseSpeed`, `Accel` | the drive check's 1.25 s throttle pulse from rest: metres, the speed it reached, and 2d/t2 | the drive check |
| `Steer`, `MaxSteer`, `TurnRadius`, `Wheelbase`, `Track`, `Wheels`, `Driven` | steered wheels (lock in degrees: a radian at most, the steering pivots' limits) or skid steer (turns on the spot, radius 0); the turning radius is wheelbase over tan(lock) plus half the track | the wheels and steering joints |
| `Clearance`, `ComHeight`, `TipSide`, `TipEnd` | the lowest fixed part over the ground; the centre of mass's height; the static tip angles sideways and end on (the wheels' spread round the centre of mass over its height) | the body at rest |
| `Climb` | degrees: a hint, the lesser of what 60 % of peak power pulls at 3 m/s and the driven wheels' grip (0.8 of their share) | estimate |
| `Fuel`, `Burn`, `Electric`, `Charge`, `Endurance`, `Range` | litres and litres a second at full throttle (the engines' `BurnRate`), or an electric drive's kJ; minutes at full throttle, and km at the estimated top speed | the parts |
| `Guns[]` | each gun: its part (`At` the muzzle), `Mount` (turret, hinge or fixed), `Class` (light, medium, heavy: flame, harpoons, shells and big blasts heavy, full-auto medium), `Band` and `Range` (full-damage range: close under 25 m, short under 60, medium under 150), `MaxRange`, `Dps`; its turntable's range (`FullCircle` or `YawMin`/`YawMax` from rest) and the hinge's `ElevMin`/`ElevMax`; `Clear` (the bins it can turn to and fire clear of its own vehicle at some elevation) and `Level` (clear at the elevation nearest level, for a target on the ground); `Pivot`; `Seat` (index into `Seats`) or `Input` (a logic part wired into it) | the guns (`CGun`), the aim chain's joints and their limits, and rays from the muzzle swept round the turntable at five elevations through the vehicle's own drawn parts (what turns with the gun doesn't count) |
| `Engage` | the range to fight at: the guns' full-damage ranges, weighed by damage (a median) | the guns |
| `BestBin`, `BestSide`, `BestDps` | the bearing where the most gun damage reaches clear (a tie goes to a broadside), its side and how much | the guns' `Clear` |
| `Weak[]` | each weak point: its part, its kind (the style's rule label, "fuel drum", "open driver", "antenna", "pipe box"..., else fuel, tyres, ammo, engine), `Bearings` (a standing player 20 m off hits it first from there) and `Top` (seen from straight above) | the style's identity rules (`VehicleIdentity.SignatureOf(style).WeakPoints`) plus the plain ones, the identity check's own rays |
| `KeepAway` | the side to keep from the enemy: where its weak points show most, fuel and ammo weighed 3, the engine and driver 2, the rest 1 | the weak points |
| `Seats[]` | each seat: what it controls (drive, guns: a bit per gun, winch, signals: horns, lamps, sirens, a radio, beacons; other wired parts), and `Open`: its sitter can be shot (seen from above, or from four bearings) | the drive seat, the guns' seats, the wires one module on |
| `Entries[]` | doors, ladders and hatches, with where they are | the parts |
| `Loot[]`, `Cargo` | the containers that rolled loot, their table and its tier ("bandit 3" is 3, a style's own 2); loose cargo pieces | the blueprint's loot list |
| `Stowed[]` | its own cargo ("Cargo and spares"): each stowed stack (`Container` and `Slot`) or loose piece (`At`, rest frame; the body is carried by this vehicle), its kind, part and count, `CooksOff` (a loose box of rounds) and `Guns` (bit k: gun k can use it: a magazine it takes, rounds, a flame gun's fuel) | the blueprint's `"stored"` and `"cargo"` lists |
| `Hitches[]`, `Winches[]`, `FuelOpen` | tow points (`At` the coupling point) and their kind; winches; how many fuel tanks can be hit | the parts and the weak points |

What it doesn't know: anything that changes while it lives (damage, ammo, fuel left, who sits where: read the construct), a gun's arc against other vehicles or the ground,
and the climb and range are hints, not drives. A weak point's exposure and a gun's clear bins are judged on the vehicle as made: a plate shot off later can expose more.

**Seeing it.** `--brief` prints a vehicle's (made as the spawn makes it, or any blueprint file); its last line gives the cost and the save bytes' round trip. In a dev build,
on the host, with the look-at inspector open (F6), the faction vehicle under the crosshair shows its brief: each gun's clear bins as spokes from its pivot (green clear at
level, amber only lifted), the engagement range as a ring on the ground with the best bearing drawn across it, the weak points (red hittable, grey covered), a "keep away"
arrow, and the brief as text down the left. A client sees nothing (the brief is the server's). The "generated" log line ends with the brief's summary ("brief: 2 guns
engage 31 m, best right; 10 weak points, keep the back away; 4 seats; top 23 m/s, wheels steer").

**Checked** (2026-11-07, by hand: an offline host on Construct Yard through the real spawner, the box at load 9 to 10): a Bandit raider (a Gun Wagon) and IT convoy 11 (a
command War Bus, a raider Box Truck, a tanker Scrap Tanker) each got a brief that fitted its body, the convoy's with its slots, place and leader; a gun's part marked broken
read as gone; taking part 0 off moved every reference down by one and they followed their tags (0 B); the world saved and loaded with 4 briefs and the same text. No brief
span was among a generation's long ones.
