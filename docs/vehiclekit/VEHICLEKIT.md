# The vehicle kit (Source/VehicleKit)

A vehicle generator is C# code inside the game assembly that fills a `Vehicle` and writes an ordinary blueprint file. It exists so an agent can make vehicles for a faction
without working out sockets by hand, in the same way the map kit (docs/mapkit/MAPKIT.md) lets it make maps. Everything goes through the game's own rules as it is added, and the
result is **checked by the real game** before anyone drives it: the file goes back through the blueprint loader, and the real physics and weapons drive, turn and fire it.

Namespace `CallOfSlop.VehicleKit`. The committed files stay the product: the game loads `Constructs/Blueprints/*.json`, never a generator, so a generated vehicle is spawned, driven,
shot and salvaged part by part like any other. **Fix the generator, not its files.** No AI is in here: crew and behaviour are a later job.

## Commands

| Command | What it does |
| --- | --- |
| `CallOfSlop --gen-vehicle "Bandit Technical" [--seed S] [--out DIR] [--plan]` | Runs a generator (one seed, or every seed it ships), writes `Constructs/Blueprints/<Name><seed>.json` in the repository when the game runs from a checkout, else DIR, and prints the check. Each seed runs twice and must write the same text. `--plan` draws a PNG next to the exe. Exit code: how many seeds failed. |
| `CallOfSlop --gen-vehicles [--out DIR]` | Every shipped seed against its committed file: `identical`, `DIFFERS` or `new`. Exit code: how many aren't identical. |
| `CallOfSlop --sweep-vehicle "Bandit War Bus" [--seeds N]` | Seeds 1 to N (default 30) built and checked, nothing written: how often a shape fails, on what, and what a generation costs. Run it before calling a shape done: a shipped seed proves only itself. It also compares seeds (`VehicleVariety`: kinds of part, plate and beam materials not counted, plus bounding shape): it prints the most different seeds it found, and fails when two shipped seeds are closer than 0.25. Choose `Seeds` from that line. |
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

**Its own seeds.** A seed's dice fall differently over another style's lists, so a shape's seeds that pass in one style can fail in another. `FactionStyle.Seeds` names the seeds a style ships for a shape (`["Box Truck"] = [1, 20, 21]`); a shape it doesn't name ships its own. Pick them with `--sweep-vehicle "IT Box Truck"` ("most different").

**The weapon slot.** `FactionStyle.Weapons` lists what a turret may mount, by parts ("Guns by their parts"); each turret rolls from its own fork. A name twice is twice as likely.

### The Bandit style

Rust and bare metal, one colour of frame, war paint on a few panels, plates that don't match and some that are missing. `tools/constructs/bandit_parts.py` writes what the game didn't have (`Constructs/Parts/Bandit`): the Fuel Drum, spikes, skulls and a hubcap. Run `tools/constructs/assembling.py` after it for their recipes.

The designed weak points: the Fuel Drum (55 health, and on the technical the only fuel) stands in the open, its pipe under the floor; nothing covers the tyres; the driver sits in the open with no roof or glass. On the Scrap Tanker the big tanks stand in the open too. A technical's crates ride loose in a Cargo Volume under the bed, so they stay aboard until the volume breaks or someone lifts them out.

Loot: containers in rooms roll from the `bandit` table in `Constructs/Loot.json` (docs/sandbox/CONSTRUCTS.md, "Loot"). Tier 1 and 2 parts, common and uncommon weapon parts. **Ammo Crates stay out of it on purpose:** a stored item doesn't cook off, and a crate is 45 kg, physics-only, so it couldn't go in a slot anyway. An Ammo Box in a slot is safe loot; a technical's loose Ammo Crate is the one that blows.

### The IT style

The IT Department (`Styles.It`, group "IT Department"), a parody of modern office work and the first faction to try faction work with. Beige cases and carpet-tile grey on the panels, corporate blue on the hoods, war paint in lanyard yellow, link blue, paper white and failed-build red; plates that line up (it passed an audit). `tools/constructs/office_parts.py` writes its dressing (`Constructs/Parts/Office`): a CRT Monitor, an Office Plant, a Desk Fan and an Employee of the Month plaque as trophies, Router Antennas and a Wi-Fi Mast as spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room. Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). It is the tech faction, and its vehicles carry IT hardware: a Satellite Dish, a Network Switch and a Cable Reel join the trophies, and a Satellite Dish the spikes. Rooms are an open plan, a server room, a stationery cupboard or a break room, furnished with Server Racks (loot on their shelves), Computers and Fax Machines: the Computer and the Fax Machine are the only line to the IT Department (docs/story/STORY.md, "The IT Department's fax"). Guns are named for the job: the Ticket Closer (a carbine), Reply All (a machine gun) and Hard Reset (a pump gun). Loot rolls from the `it` table: circuit boards, wiring, controls and readouts up to tier 3, Fax Machines, Computers and Server Racks, common and uncommon weapon parts. Every IT shape ships its own seeds.

**More dressing (`Vehicle.Kit`).** A style with a `"mast"` slot gets a last pass of its own (its own fork, so nothing else of a seed moves, and a style without the slot never rolls it): masts on the rooms' roofs and the cab, spikes down the rooms' outer walls, and its `"gear"` standing on open beds and porches. The IT style's masts are dishes and antennas, its gear reels, switches, monitors and dishes.

### The Call Centre style

The Call Centre (`Styles.Scam`, group "The Call Centre"): the Scammers (docs/story/STORY.md, "The Scammers") on wheels, a parody of the scam call centre. Cubicle-fabric greys on the panels, corporate teal on the hoods, war paint in WINNER yellow, hot pink and dollar green. It starts from the IT style's slots and swaps the dressing: `tools/constructs/callcentre_parts.py` writes `Constructs/Parts/CallCentre`: an Office Phone, a Call Headset on a stand, a GIFT CARDS sign and a Prize Wheel as trophies and gear, Loudhailers down the flanks as spikes, Phone Masts on the roofs. Rooms are a call floor (rows of Cubicle Desks round a Water Cooler), the manager's office (a Computer, a cubicle, a Server Rack) or a break room; each Cubicle Desk has a drawer of loot. Guns are named for the job: the Cold Caller (a carbine), Robocall (a machine gun) and Final Notice (a pump gun). They run on other people's batteries, so loot rolls from the `scam` table: batteries, circuit boards and wiring up to tier 3, the odd Computer, common and uncommon weapon parts. Every shape ships its own seeds (the sweep's most different passing ones).

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

**Files or generated at load.** Keep shipped vehicles as a few seeds a shape, for review and the menu. Don't commit hundreds: the game builds each blueprint once at start-up to check it (about 8 ms each), and the Blueprints tab's spawn message carries the index as a byte (`CActKind.Blueprint`), so 255 is a hard limit. For maps, take a small set of seeds per shape: paint and loot are what a player sees differ, and loot rolls at spawn anyway. If a map needs true variety, generate on the server at map load on a worker: clients get the bodies, so nothing about a generated vehicle needs to be on their disk. That path isn't built.
