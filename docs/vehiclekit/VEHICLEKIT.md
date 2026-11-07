# The vehicle kit (Source/VehicleKit)

A vehicle generator is C# code inside the game assembly that fills a `Vehicle` and writes an ordinary blueprint file. It exists so an agent can make vehicles for a faction
without working out sockets by hand, in the same way the map kit (docs/mapkit/MAPKIT.md) lets it make maps. Everything goes through the game's own rules as it is added, and the
result is **checked by the real game** before anyone drives it: the file goes back through the blueprint loader, and the real physics and weapons drive, turn and fire it.

Namespace `CallOfSlop.VehicleKit`. The committed files are what reviews, tests and the menu's tiles use: the game loads `Constructs/Blueprints/*.json`. A server can also run a
generator while the game plays, for a seed nobody committed ("Dynamic spawn" below). Either way the vehicle is an ordinary construct, spawned, driven, shot and salvaged part by part
like any other. **Fix the generator, not its files.** No AI is in here: crew and behaviour are a later job.

## Commands

| Command | What it does |
| --- | --- |
| `CallOfSlop --gen-vehicle "Bandit Technical" [--seed S] [--out DIR] [--plan]` | Runs a generator (one seed, or every seed it ships), writes `Constructs/Blueprints/<Name><seed>.json` in the repository when the game runs from a checkout, else DIR, and prints the check. Each seed runs twice and must write the same text. `--plan` draws a PNG next to the exe. Exit code: how many seeds failed. |
| `CallOfSlop --gen-vehicles [--out DIR]` | Every shipped seed against its committed file: `identical`, `DIFFERS` or `new`. Exit code: how many aren't identical. |
| `CallOfSlop --sweep-vehicle "Bandit War Bus" [--seeds N]` | Seeds 1 to N (default 30) built and checked, nothing written: how often a shape fails, on what, and what a generation costs. Run it before calling a shape done: a shipped seed proves only itself. It also compares seeds (`VehicleVariety`: kinds of part, plate and beam materials not counted, plus bounding shape): it prints the most different seeds it found, and fails when two shipped seeds are closer than 0.25. Choose `Seeds` from that line. |
| `CallOfSlop --diversity "Bandit Box Truck" [--seeds N]`, `--diversity-style Bandit`, `--kit-report`, `--vehicle-sheet "Bandit Box Truck"`, `--grade-vehicle "Bandit Box Truck 7" 4 "note"`, `--grade-report` | The quality tools: how alike seeds are, what never varies, what fails and what it costs, contact sheets and grades. See "Quality tools". |
| `CallOfSlop --identity "IT War Bus" [--seeds N] [--shipped] [--raw]`, or `--identity IT` for every shape of a style | How much each seed reads as its faction (0 to 1) with the breakdown, its weak points, the style it reads as best after its own, then the mean, the weak-point pass rate and every vehicle that reads more as another style. Default 6 seeds; `--shipped`: the committed seeds. See "Identity and weak points". |
| `... --no-identity-gate` on any of these | Kit 2 without its identity re-plan, to measure what the check changes. |
| `... --kit 1`, `--kit 2` or `--kit 3` on any of these | Every seed by that kit (see "Shape grammar" and "Layered variety"), to compare them: without it a seed is made by its own kit. `--gen-vehicles --kit 2` reports the committed files as differing, as it should. |
| `... --layers chassis=5,wear=9` on any of these | Kit 3's layer seeds on every vehicle made: the seed's own vehicle with those layers rolled from other seeds ("Layered variety"). `--gen-vehicle` then writes `BanditBoxTruck7chassis5wear9.json`, never a committed seed's file. |
| `... --vary wear [--base S]` on any of these | The seeds a tool loops over become one layer's seeds on base seed S (default 1): `--diversity "Bandit Box Truck" --vary chassis --base 5 --seeds 8` is seed 5's body and kit on eight chassis. |
| `CallOfSlop --check-vehicle FILE` | The same check on any blueprint file, hand-made or generated (a generator's own seed also gets its rooms walked). Handy as a baseline: the shipped Buggy drifts about 3 degrees left on the same run, and every technical 5 to 10, so the drift limit is 12. |

The console goes to `callofslop.log` next to the exe. A report reads

```
Bandit Technical 7: OK; 90 parts from 90 steps, 2 freeform welds, 6 links, 3 cargo, 1193 kg, drive 94.5 m at 24.4 m/s yaw -6.2 deg, turn 197 deg, worst tilt 2.2 deg, stability 0.85, 10 shots
  fuel 60.0 l; power: no electrics
  wheels: 4, 2 driven, 2 steered; wheelbase 3.13 m
  centre of mass 0.95 m up, 0.02 m off the middle, 53% of the way back along the wheelbase
  coplanar faces between parts: 0 among 315 boxes
  guns: 1, seats Driver, Gunner
  the Mounted MG follows the gunner's look within 2.2 degrees (sides, behind, up)
```

and every `FAIL` line says what went wrong and, where it can, what to do. What it checks (`VehicleChecks`, defaults in `VehicleCheckOptions`; a generator's `Checks` may change them):
it builds from the file with no refusal; a working powertrain, a seat that drives through a Driving Module and fuel piped to the engine; driven and steered wheels; the centre of mass inside the
wheels and its stability (half the track over its height); no coplanar faces between parts, in their paint (wheels and what's on joints don't count: they don't stay at rest); part count and mass; it
stands level and stays in one piece; full throttle for 6 s through the seat (distance, heading drift, that forward is forward); a full-right turn at speed (that right is right, and it doesn't roll);
every gun has a load (a flame gun drinks the construct's fuel), joints and a gunner, fires on Fire in that seat and follows the gunner's look round the sides, behind and up; every cargo piece rides in a frozen volume and is still there after the drive; six spawns' loot rolls differ, and something rolled.

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
- **Crew.** `OpenCab()`: the seat sill, the driver, sometimes a passenger, the style's "cab" shell (roll bar, roll cage, tin roof, chop top, mesh guard, slit cab, windscreen frame), a floor. `Driver(at)`: seat, Driving Module, wires (seat to module, module to engine). `Passenger(at)`. `Turret(mount, weapon, seat, aft)`: turntable, arm, motor hinge, a gun from its parts, Turret Module; `seat` null puts the gunner on the arm, else a seat in a room works it from inside; `aft` starts it facing the stern. `SideGun(bay, side, seat)`: a Gun Sponson on a bay's waist with a gun on it. `Feeds(crateA, crateB, typeA, typeB, leverOn, leverSocket, gun, more...)`: two crates of different rounds (`ammoType`, not ammo parts: `FitGun` drops an "ammo" entry), an Ammo Switch, a lever and feed links, the way a player wires it. `Panniers(bays, dice)`: faceted boxes on free waists.
- **Under a floor.** `v.FloorY` is the floor's height: a recipe's `Gear.Floor`. `Under(kind, z0, z1, gaps...)`: "stilts" (sills, in the open: one layer under a low floor, two under a high one), "skirted" (the same, the next room gets Hull Skirts under its floor's edges) or "tub" (closed Chassis Tubs on the rails, no stilts).
- **Rooms.** `Hull(tag, z0, under, bays, seatSpots)`: a Bolt Deck floor and a run of faceted bays from `Constructs/Parts/Hull`, named less "Hull ": `["Brow T", "Door", "Bay T-L", "Tail L"]`. Sections are T tall, L low, N narrow; "Bay T-L" goes from one to the other, "Door" is the Door Bay (a flush door on a Long Door Hinge in its +x side). Always start with a Brow and end with a Tail: a Bay's ends are open, and the check finds the hole. `room.Bays` gives a turret's mount (a bay's "top") and a side gun's ("+x0", "-x0"). `Furnish(room)`: furniture standing on the floor down the side walls by a layout from the style ("layout" picks one, "layout NAME" lists its slots; bunkroom, workshop, hold, mess), never in the doorway, keeping the middle clear (a player is 0.8 m across), a chest if nothing else takes loot. What has slots rolls from the loot table at each spawn.
- **Body and armour.** `Deck(tag, y, rect, slot, holes, smallest)`: a flat deck patched from seeded plate sizes. `Wall(tag, face, at, from, to, bottom, height, slot, gap)`: upright plates along an edge. Armour is a `Deck` or `Wall` on a slot of armour plate names ("Armour Plate"). Both remember their panels under `tag` in `v.Panels`.
- **Off the grid.** `Crooked(tag, part, on, at, face, dice)`: a welded plate tilted up to the style's `tiltDegrees`.
- **Dress.** `Hood()`: a style "hood" part on the engine (or "plates", tiled), its "nose" and "fender"; `Sides`, `TubeRails`, `DrumOn`, `Crates` for beds. `Trim(wallTags...)` (ram bars, crooked patches, spikes, trophies, paint: everything a shape shares), `RamBars(top, z, dice)`, `Dress(slot, panels, dir, count, dice)` (spikes and trophies on free sockets facing `dir`), `CargoBay(volume, centre, size, slot, count, spots...)`, `PaintAll(parts, role, dice)`.

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

## Shape grammar

Kit 1 made each shape with one generator: a Box Truck was always a hull on a chassis with the same nose, room and tail in the same places, so seeds differed in parts
and hardly at all in outline (Bandit Box Truck, November 2026: silhouette 0.057 of the distance against 0.39 for parts). **Kit 2** (`VehicleGrammar.cs`) assembles the
heavy shapes from **modules** instead, so the outline varies with the seed while every part still comes from the style.

**Which kit.** `VehicleKitVersion.For(generator, seed)`: a seed committed before the grammar (`VehicleKitVersion.Frozen`, the 123 files of 2026-11-06) and every seed of a
shape with no recipe (the Technical, the Fuel Trailer, the Tanker Rig, the Double Decker, and any new generator) is kit 1, made exactly as before, so `--gen-vehicles`
stays identical; every other seed, the dynamic spawn's included, is the current kit: kit 3 since 2026-11-06 ("Layered variety"), kit 2's grammar with its variety in
layers. Kit 3 gives a kit-2 seed another vehicle; no kit-2 seed was ever committed, and a saved world holds built constructs, not seeds, so nothing changed under anyone
(`--kit 2` still makes kit 2's, byte for byte). A kit-2 or kit-3 file says so in a comment after its first brace (`// kit 2; body room+bed, cab caged,
nose ram, rear porch, ...`; blueprints allow comments), `Vehicle.Kit` and `Vehicle.Modules` hold the same, and the spawn logs it (`[vehicles] Bandit Box Truck 733786:
kit 2 (body room, cab enclosed, ...)`). A seed committed from now on is made by the kit current when it was committed: grade and commit from a sweep of that kit. No kit-2
seed was committed, so `Frozen` still holds kit 1's only; when a kit 4 comes, the kit-3 seeds committed by then go into a list of their own, with `For` returning their kit.

**Modules** (`ShapeGrammar.Catalogue`). Each has a family, a name, the style parts it is made of (a module whose parts the style doesn't list is never made), and its
connection rules as tags: what it needs, what it provides, what it can't stand beside, and the gear recipes it allows.

| Family | Modules | Rules |
| --- | --- | --- |
| cab | caged, enclosed, armoured, cab-over | from the style's `heavyCab` list; cab-over provides "cabover" |
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
have layers: the Technical, the trailers, the Tanker Rig and the Double Decker refuse them (kit 1), and so does a request for them.

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
beside a hole or a whole plate. **Not done:** broken glass (no vehicle carries a glass part: a cab shell is one part) and bent trim.

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
the layers' doing; it wants fixing where trophies stand on a hood. The IT War Bus's lower identity is its old missing mast (5 of 12 against 3 of 12): a bus that plans again
more often (a boxy plan, an ammo reel) has fewer plans left for the identity re-plan, and that is what the guaranteed-marks pass is for. Generation cost is within the
box's noise: more plans are made (the fill margin is a point now) and each costs the same.

## Quality tools

The kit will make vehicles during play with nobody looking, so these tools keep variety up and each faction looking like itself. They generate and measure (0.1 to 1 s a seed on the 2-core box); none runs the drive or gun checks unless asked, and the defaults stay small. Everything prints to `callofslop.log`. Source: `VehicleDiversity.cs` (the metric), `VehicleQuality.cs` (the commands), `Program.VehicleSheet.cs` (the sheet).

**The distance** (`VehicleDiversity`). Each seed is built from its own blueprint text, as the game builds it, and measured. Four parts, each 0 to 1, weighted into one number from 0 (the same vehicle) to 1 (nothing shared):

| Part | Weight | What it compares |
| --- | --- | --- |
| silhouette | 0.40 | Along the length in 16 slices, nose first, over the fixed parts (no wheels, no turrets, as `MaxSideFill` sees them): how much of each slice's side is filled, its plan width, and its top's height, each in its own bounding box (`VehiclePlan.Profiles`), plus the whole side and plan fill. The mean difference. |
| parts | 0.20 | Kinds of part (`VehicleVariety.Kind`: a plate is a plate in any material or size, a beam a beam; counts capped at 8): one less their weighted overlap. |
| layout | 0.25 | Rooms, turrets, seats, axles (wheels within half a metre are one), wheelbase, track, and the sets of wheel and spring parts. |
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

**The score**, 0 to 1, five parts weighted into one; a part with nothing to judge (no paint, no marks that apply, no seat for traits: a trailer's dressing is its tower's) is
left out and the rest weighed up:

| Part | Weight | What it measures |
| --- | --- | --- |
| marks | 0.30 | The share of the marks that apply which are there (and in the open where asked). |
| palette | 0.20 | Painted parts in the style's colours (0.6) and how many of its palette roles show at all (0.4). |
| traits | 0.20 | Characteristic parts against `PartTarget`, and for a grammar vehicle the mean of its modules' weights; the mean of the two. |
| silhouette | 0.15 | Each of the five outline numbers in range scores 1, falling to 0 a quarter of the range (plus 0.05) outside it. |
| weak points | 0.15 | Each rule that applies: 1 there and hittable, 0.25 there but covered, 0 missing. |

The breakdown reads `0.78 as IT: marks 1/2 (no a mast or a dish); palette 100 % (4/4 roles); traits 0.92 (parts 1.00, modules 0.83); silhouette 5/5; weak points 2/3
(antenna missing, tyres 6/6 hittable, fuel 1/1 hittable)`.

**Weak points.** A rule names a part pick and how exposed it must be: seen from `MinSides` of the six directions (a ray from far off along each axis, at the part's middle or
one of four points round it, meets that part first), and hit first by a ray from a standing player's eye (1.6 m over the ground under the wheels) `Range` metres away (20 by
default, and outside the vehicle's footprint) from `MinBearings` of 16 bearings round it. Rays go through what is drawn, every part's real boxes and meshes in the rest pose,
wheels and turrets included (`VehiclePlan.PartTriangles`, the triangles `VehicleChecks.Gaps` shoots through, each tagged with its part), so a drum behind a bed wall, a
crate in a room's magazine or an antenna under a rack counts as covered. `Min` of the picked parts must pass; `Optional`: none there doesn't count against it. The rays
are a geometric test: they don't know a part's health, armour or what a round does on the way, and a hit on a part's own other face counts as a hit.

**In generation.** `ShapeGrammar.Make` hands each built plan to `VehicleIdentity.Gate`. Under the threshold or with a mark missing, the seed plans again from the next fork
(logged: `[vehicles] IT War Bus 1: plan 1 reads 0.78 as IT (no a mast or a dish): ...; planning again`); the second plan is kept if it is better, else the first, so identity
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
anything for tea (no room, no cab shell to dress). Those are a generator's job (a mast pass that guarantees one, as the Pipe Box does), not the score's. Tyres and fuel are
hittable on every vehicle; the only covered weak points seen are the rounds in a room's magazine, which is why the Bandit's "ammo crate" applies to light vehicles only.
Generation with the gate cost IT and the Call Centre about a fifth more over all their shapes (a War Bus 1.2 s on average, 2.3 s at worst), the rest nothing measurable.
The silhouette range is the same for every style: outlines come from the shapes, which every style shares, and the styles' 5th to 95th percentiles agreed to a few points,
so for now the range keeps a style off a box or a sliver rather than telling one from another.

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

The IT Department (`Styles.It`, group "IT Department"), a parody of modern office work and the first faction to try faction work with. Beige cases and carpet-tile grey on the panels, corporate blue on the hoods, war paint in lanyard yellow, link blue, paper white and failed-build red; plates that line up (it passed an audit). `tools/constructs/office_parts.py` writes its dressing (`Constructs/Parts/Office`): a CRT Monitor, an Office Plant, a Desk Fan and an Employee of the Month plaque as trophies, Router Antennas and a Wi-Fi Mast as spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room. Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). It is the tech faction, and its vehicles carry IT hardware: a Satellite Dish, a Network Switch and a Cable Reel join the trophies, and a Satellite Dish the spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room, furnished with Server Racks (loot on their shelves), Computers and Fax Machines: the Computer and the Fax Machine are the only line to the IT Department (docs/story/STORY.md, "The IT Department's fax"). Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). Loot rolls from the `it` table: circuit boards, wiring, controls and readouts up to tier 3, Fax Machines, Computers and Server Racks, common and uncommon weapon parts. Every IT shape ships its own seeds. **Signature**: marks, a mast or a dish (Satellite Dish, Wi-Fi Mast, Router Antenna) and office gear (monitors, switches, reels, plants, fans, plaques, racks, computers, fax machines); modules enclosed and cab-over cabs, short and flat noses, bare flanks. Weak points: the antenna (two sides, two bearings), two tyres, the fuel.

**More dressing (`Vehicle.Kit`).** A style with a `"mast"` slot gets a last pass of its own (its own fork, so nothing else of a seed moves, and a style without the slot never rolls it): masts on the rooms' roofs and the cab, spikes down the rooms' outer walls, and its `"gear"` standing on open beds and porches. The IT style's masts are dishes and antennas, its gear reels, switches, monitors and dishes.

### The Call Centre style

The Call Centre (`Styles.Scam`, group "The Call Centre"): the Scammers (docs/story/STORY.md, "The Scammers") on wheels, a parody of the scam call centre. Cubicle-fabric greys on the panels, corporate teal on the hoods, war paint in WINNER yellow, hot pink and dollar green. It starts from the IT style's slots and swaps the dressing: `tools/constructs/callcentre_parts.py` writes `Constructs/Parts/CallCentre`: an Office Phone, a Call Headset on a stand, a GIFT CARDS sign and a Prize Wheel as trophies and gear, Loudhailers down the flanks as spikes, Phone Masts on the roofs. Rooms are a call floor (rows of Cubicle Desks round a Water Cooler), the manager's office (a Computer, a cubicle, a Server Rack) or a break room; each Cubicle Desk has a drawer of loot. Guns are named for the job: the Cold Caller (a carbine), Robocall (a machine gun) and Final Notice (a pump gun). They run on other people's batteries, so loot rolls from the `scam` table: batteries, circuit boards and wiring up to tier 3, the odd Computer, common and uncommon weapon parts. Every shape ships its own seeds (the sweep's most different passing ones). **Signature**: marks, a Phone Mast or a Loudhailer and call centre gear (phones, headsets, GIFT CARDS signs, the prize wheel, cubicles, the water cooler); modules as IT's. Weak points: the antenna (a mast or loudhailer), two tyres, the fuel.

### The Highland style

The Clan MacSlop (`Styles.Highland`, group "Clan MacSlop"), a Highland clan on wheels and the loudest faction on the map. Tartan reds, hunting green, navy and tweed on the panels, Saltire blue and white on the hoods, war paint in pillar-box red, lion-rampant yellow, white and black; plates a little crooked (well loved, not neglected). `tools/constructs/highland_parts.py` writes its parts (`Constructs/Parts/Highland`): Saltire Flags on the roofs (masts), Thistles, a Haggis on a Post and a Coo Heid as trophies, Wee Cabers down the flanks as spikes, Wee Kegs on the beds, and Whisky Casks in the rooms (two loot slots each). Rooms are a bothy, a distillery, a ceilidh hall or a kit room. Guns are named for the glen: the Claymore (a pump gun), the Sgian Dubh (a carbine), the Highland Charge (a machine gun) and the Caber Toss (the harpoon). Loot rolls from the `highland` table: tier 1 and 2 drive parts, pump guns and carbines, and the odd Pipe Box.

**The Pipe Box (`Vehicle.Piper`).** A style with a `"piper"` slot stands exactly one on every vehicle with a seat, before the trophies take the sockets: on the hood, else the cab, a room's roof, the bed, a porch, the headboard or a flank. Its own fork, so nothing else of a seed moves, and a seed with nowhere to stand one throws, so the sweep skips it. A trailer has no seat and gets none. The box strikes up the pipes while someone drives (docs/sandbox/MEDIA.md, "Pipe Boxes"), so you hear the clan coming. **Signature**: marks, the Pipe Box and clan dressing (Saltires, thistles, the coo's heid, haggis, wee cabers, kegs, casks); modules long hoods and rams, caged cabs, panniers. Weak points: the Pipe Box (shoot it and the pipes stop), two tyres, the fuel.

### The British style

The Ministry of Slop (`Styles.British`, group "Ministry of Slop"), a very British faction, affectionate and mostly understated rather than on the nose. Bus red on most panels with the odd racing green, cream or wet-Tuesday grey; racing green, cream and navy on the hoods; war paint in the flag's blue, red and white and double-yellow-line yellow; plates neat but not new. `tools/constructs/british_parts.py` writes its parts (`Constructs/Parts/British`): Union Flags, Belisha beacons and rain gauges on the roofs (masts), a Teapot in its cosy, a Traffic Cone nobody will move, a Garden Gnome and a Rain Gauge as trophies, Furled Brollies down the flanks as spikes (it's that sort of day), Queue Posts on the beds and porches (gear), and in the rooms a Pillar Box (two loot slots) and a cut-down red Phone Box (loot under the shelf) round a Tea Urn. Rooms are a tea room, a post office, lost property or a snug. Guns are named the way the British complain: Terribly Sorry (a machine gun), Bit of Bother (a carbine), the Strongly Worded Letter (a pump gun) and Builder's Tea (the flamer). Loot rolls from the `british` table: tier 1 and 2 drive parts, carbines and pump guns. **Signature**: marks, flags, beacons or brollies (Union Flag, Belisha Beacon, Furled Brolly, Rain Gauge) and tea, cones or a queue (teapot, cone, gnome, queue post, pillar box, phone box, tea urn); modules enclosed cabs, long hoods, bare flanks. Weak points: two tyres, the fuel, and a flag or beacon when it carries one.

**The Double Decker (`Generators/DoubleDecker.cs`).** An open-top double-decker bus: a long walk-in hull below (side door, loot, the seats that work the guns) and a railed top deck on the tall bays' roofs, with the two roof guns standing up through holes in it. The deck is laid from the bays' `top` sockets (each sits on a deck cell's centre), stops where the tail slopes down (a stepped roof), and a ladder hangs from its back edge, else down the right side. It is an **extra shape**: `VehicleRegistry.Extras` holds shapes a style ships only when it names them in `FactionStyle.Extra` (`Extra = ["Double Decker"]`), so no other faction's committed files change; any style can still run one by name (`--gen-vehicle "Bandit Double Decker" --out DIR`).

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

**The parts menu.** The Blueprints tab has "Faction vehicles: new seed" (on by default) and a seed box (0: any). With it on, a faction vehicle's tile (a blueprint named "Style Shape seed") asks for a new one of that style and shape (`CActKind.Vehicle`: the style's and the shape's names and a seed; protocol 186); off, the tile spawns the committed file as before (`CActKind.Blueprint`, which tests and every other blueprint still use). The host also has "Server generates them": off (`VehicleSeeds.Committed`), every request gets a committed seed of its style and shape, the asked one when it ships, so a session plays only what CI checks.

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

**Seeds that fail.** A seed whose generator throws (some seeds have nowhere to stand a part; the sweep skips them) or whose text doesn't load rerolls up to 3 times, each new seed derived from the last (`VehicleSpawnKit.Reroll`: the same seed always rerolls the same way), then a committed seed of that style and shape stands in (`VehicleSpawnKit.Committed`). A style that ships nothing (Militia) has nothing to fall back to: the player is told. Failures are logged, the first 10 and then every 100th. The in-game check is that it builds: `VehicleChecks` (driving, turning, firing for simulated seconds) doesn't run at spawn, so a seed that builds but drives badly is spawned anyway. `--sweep-vehicle` gives a shape's pass rate; run it before calling a shape fit for dynamic spawn.

A towed vehicle (the Tanker Rig's trailer) comes with it: a committed trailer when its blueprint ships, else made by its own generator in the same job.
