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
| `CallOfSlop --check-vehicle FILE` | The same check on any blueprint file, hand-made or generated. Handy as a baseline: the shipped Buggy wanders about 3 degrees on the same run. |

The console goes to `callofslop.log` next to the exe. A report reads

```
Bandit Technical 7: OK; 90 parts from 90 steps, 2 freeform welds, 6 links, 3 cargo, 1193 kg, drive 94.5 m at 24.4 m/s yaw -6.2 deg, turn 197 deg, worst tilt 2.2 deg, stability 0.85, 10 shots
  fuel 60.0 l; power: no electrics
  wheels: 4, 2 driven, 2 steered; wheelbase 3.13 m
  centre of mass 0.99 m up, 0.05 m off the middle, 57% of the way back along the wheelbase
  coplanar faces between parts: 0 among 356 boxes
  guns: 1, seats Driver, Gunner
  the Mounted MG follows the gunner's look within 2.2 degrees (sides, behind, up)
```

and every `FAIL` line says what went wrong and, where it can, what to do. What it checks (`VehicleChecks`, defaults in `VehicleCheckOptions`; a generator's `Checks` may change them):
it builds from the file with no refusal; a working powertrain, a seat that drives through a Driving Module and fuel piped to the engine; driven and steered wheels; the centre of mass inside the
wheels and its stability (half the track over its height); no coplanar faces between parts, in their paint (wheels and what's on joints don't count: they don't stay at rest); part count and mass; it
stands level and stays in one piece; full throttle for 6 s through the seat (distance, heading drift, that forward is forward); a full-right turn at speed (that right is right, and it doesn't roll);
every gun has a load, joints and a gunner, fires on Fire in that seat and follows the gunner's look round the sides, behind and up; every cargo piece rides in a frozen volume and is still there after the drive.

A check covers a flat floor and a few simulated seconds, that's all. It doesn't try rough ground, a client over the network, or how the thing looks: a person reviews that in game.

## Writing a generator

```csharp
namespace CallOfSlop.VehicleKit.Generators;

public sealed class RaiderBuggy : IVehicleGenerator
{
    public string Name => "Raider Buggy";              // blueprint "Raider Buggy 12", file RaiderBuggy12.json
    public FactionStyle Style => Styles.Bandit;
    public IReadOnlyList<int> Seeds => [12, 19];        // what ships
    public VehicleCheckOptions? Checks => null;

    public void Build(Vehicle v)
    {
        v.Description = "A light raider.";
        v.Ladder(frontAxleZ: -1.175f, railLength: 2f, nose: 0.625f, tail: 0.75f)
         .SteeredAxle().DrivenAxle().Powertrain(z: -0.925f);
        v.Sill(0.075f, wing: 0.5f);
        v.Driver(new Vector3(-0.5f, Vehicle.SillTop(1), 0.075f));
        var tank = v.Put("Fuel Tank", new Vector3(0, 0.5f, 1.3f), Orient.Forward, reach: 0.2f);
        v.FuelFrom(tank);
    }
}
```

Add it to `VehicleRegistry.All`, run `--gen-vehicle "Raider Buggy"`, read the report, commit the blueprint files. `BanditTechnical.cs` is the worked example.

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
- **Axles.** `SteeredAxle()` (Steering Pivots on the cross beam), `DrivenAxle()`. Springs and wheels come from the style.
- **Powertrain.** `Powertrain(z)`: engine on its own sill, gearbox behind it, a shaft to the differential. `FuelFrom(tank, nodes...)`: the pipe.
- **Crew.** `Driver(at)`: seat, Driving Module, wires (seat to module, module to engine). `Passenger(at)`. `Turret(sill, weapon)`: turntable, arm, gunner's seat, Turret Module, motor hinge, gun and feed.
- **Body and armour.** `Deck(tag, y, rect, slot, holes, smallest)`: a flat deck patched from seeded plate sizes. `Wall(tag, face, at, from, to, bottom, height, slot, gap)`: upright plates along an edge. Armour is a `Deck` or `Wall` on a slot of armour plate names ("Armour Plate"). Both remember their panels under `tag` in `v.Panels`.
- **Off the grid.** `Crooked(tag, part, on, at, face, dice)`: a welded plate tilted up to the style's `tiltDegrees`.
- **Dress.** `RamBars(top, z, dice)`, `Dress(slot, panels, dir, count, dice)` (spikes and trophies on free sockets facing `dir`), `CargoBay(volume, centre, size, slot, count, spots...)`, `PaintAll(parts, role, dice)`.

A deck or wall plate that no socket takes leaves a gap, on purpose: rust. Anything else that doesn't fit throws.

**Seeds.** `v.Dice` is the seed. Take a `Fork("name")` per feature, so another roll in the paint doesn't change the chassis, and pass it down. Never use `Random`, `HashCode` or anything that varies by machine. `--gen-vehicle` runs each seed twice to prove it.

### Things that caught the first vehicle

- A differential's **Input shaft** shares its `-z0` socket: a rail bolted there leaves the port "taken". `Ladder` stops the left rail short and hangs the differential on the right rail.
- Rails at axle height leave the wheels' tops above them: a bed wider than the wheels stands on two-layer sills (`layers: 2`).
- `ConstructBuild.Blocked` compares what's on joints in the rest pose, so a long gun's barrel can refuse a tall wall in front of it. Keep walls under a turret's gun line.
- A routed link with nodes is refused through solids unless `laid`. A shaft with no nodes runs straight and is only checked at its ends.
- Stacked beams' bands, and bolts on a plate under an engine's details, are coplanar. Paint a frame one colour (one look doesn't fight); the hood uses `smallest: 2`.
- `Freeform` parts aren't checked against the rest for overlap. Keep a crooked plate proud of what it covers: 6 cm clears 5 degrees on a 0.5 m plate.

## A faction style

A style (`Styles.cs`, class `FactionStyle`) is all a generator may vary and the look it varies within. A generator asks the style; it never names a colour or a favourite part itself.

```csharp
public static readonly FactionStyle Militia = new()
{
    Name = "Militia", Group = "Militia",                        // Group: its heading in the inventory's Blueprints tab
    Palette = new() { ["body"] = ["#4f5a3a", "#5c6644"], ... }, // colours by role
    Parts = new() { ["wheel"] = ["Off-Road Wheel"], ["sheet"] = ["Armour Plate"], ... }, // choices by slot; twice in the list is twice as likely
    Weapons = [new WeaponOption("Autocannon")],                 // the weapon slot
    Limits = new() { ["crookedPlates"] = 0, ["tiltDegrees"] = 0, ["thirdSeat"] = 1 },    // what a seed may do
};
```

A slot a generator asks for and the style lacks throws, naming the slot. Sheet slots hold a material's first words ("Plate Steel"): the kit adds the size. To use one generator for two factions, have the generator take its style in its constructor and register it twice under two names.

**The weapon slot.** `FactionStyle.Weapons` lists what a turret may mount; each seed picks one. `WeaponOption(gun, feed, mount, feedAt, feedBy)` is data: the gun part, the part that feeds it (null: none), the socket it mounts by, and where the feed goes on it. A flamethrower or harpoon launcher is one more line in `Styles.Bandit.Weapons`, plus the regenerated files.

### The Bandit style

Rust and bare metal, one colour of frame, war paint on a few panels, plates that don't match and some that are missing. `tools/constructs/bandit_parts.py` writes what the game didn't have (`Constructs/Parts/Bandit`): the Fuel Drum, spikes, skulls and a hubcap. Run `tools/constructs/assembling.py` after it for their recipes.

The Bandit Technical's designed weak points: the Fuel Drum (55 health, the only fuel) stands in the open at the tail, its pipe under the bed; nothing covers the tyres; the driver sits in the open with no roof or glass. Its crates ride loose in a Cargo Volume, and the volume sits under the bed, so shooting the crates doesn't free them but breaking the volume does.
