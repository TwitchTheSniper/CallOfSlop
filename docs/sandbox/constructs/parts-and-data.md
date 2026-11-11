# Parts and data

Covers part files and data, physics-only parts, structural parts and size classes, building pieces, caps. Part of the construct guide: index in ../CONSTRUCTS.md.

## The data

### Part files: `Constructs/Parts/*.json`

One part per file, loaded at start-up; ids follow the sorted file names. A bad file is refused with a reason (the rest
still load): `CallOfSlop --check-construct-parts` prints every part and blueprint, or the reason it was refused.

```json
{
  "format": "call-of-slop-construct-part/1",
  "name": "Suspension",
  "description": "A sprung wheel mount: bolt it to a frame's side, then a wheel onto it.",
  "role": "suspension",
  "tool": "wrench",
  "attachSeconds": 1.5,
  "mass": 15, "health": 110, "armour": 3, "material": "steel",
  "color": "#b08a2e",
  "boxes": [
    { "size": [0.3, 0.3, 0.3] },
    { "size": [0.12, 0.44, 0.12], "color": "#d0b050", "detail": true }
  ],
  "sockets": [
    { "name": "mount", "at": [-0.15, 0, 0], "dir": "-x" },
    { "name": "axle", "at": [0.15, 0, 0], "dir": "+x", "kind": "axle" }
  ],
  "suspension": { "travel": 0.3, "frequency": 2.2, "damping": 0.7 }
}
```

| Field | Meaning |
| --- | --- |
| `name` | Unique, 1-32 ASCII characters. Blueprints, maps and the rack name parts by it. |
| `role` | `frame`, `panel`, `seat`, `wheel`, `suspension`, `engine`, `fueltank`, `fluidtank`, `fuelline`, `hinge`, `door`, `foundation`; milestone 2: `joint`, `transmission`, `differential`, `logic`; milestone 3: `gun`, `gunpart`, `ammobox`, `feedchute` ([Construct weapons](weapons.md#construct-weapons-milestone-3)); `generator`, `thruster` ([Big drive](drivetrain-and-engines.md#big-drive)); `leg`, a walker leg (docs/sandbox/GIANTS.md). |
| `tool` | Optional, `wrench` or `welder`: only a hint that the part is structural (`welder`: physics-only; `wrench`, or none: an item unless it is heavy or bulky). The attach tool fits every part whichever it says, and the words stay so part files need no change. Old files load unchanged. |
| `attachSeconds` | Time to fit (default 1.5 an item, 2.5 a structural part); removing takes 60% of it. |
| `mass`, `health`, `armour` | kg, hit points, flat damage taken off each hit. |
| `material`, `friction` | `steel`, `rubber`, `concrete`...; friction defaults from the material (rubber 1.0, else 0.6). |
| `onBreak` | `detach` (breaks off) or `disable` (stays, stops working; the default for engines, tanks, seats, transmissions and differentials). |
| `boxes` | Up to 32: `min`/`max`, or `size` (and `at`), each with an optional `color`. `"use": true` marks what a hand works, all the yellow glow covers ([The yellow glow](overview-and-playing.md#the-yellow-glow)). `"detail": true` boxes are drawn only (no bounds, mass, collision or hit area) and may stick out. At least one box is solid. `"hidden": true`: a solid box that is never drawn. A detail box may have a `"shape"` and its settings ([Structural parts](#structural-parts-the-scale-chart)). `"paintable": false` (or `true`) says whether the paint tool's colour replaces the box's own ([The paint tool](building-and-tools.md#the-paint-tool-board-item-pp01)); the default is yes, except glass and a wheel's tyre (its solid boxes). `"side": "base"` or `"leaf"` tags a joint part's box with the side it belongs to ([Joint sides](building-and-tools.md#joint-sides-board-item-js01)); every joint part must tag both. |
| `sockets` | Up to 40 attachment points: `at` (on a face of the solid boxes' bounds, or on a face of one solid box facing out of it: a door's jamb inside an opening, a window's sill), `dir` (`+x -x +y -y +z -z`, out of that face), `kind` (`struct` default, `axle`: a wheel onto a suspension, `leaf`: a door onto a hinge; a joint's leaf also takes any `struct` socket, [Doors](big-systems.md#doors)). Two sockets mate when their kinds match and they face each other. |
| `engine` | `torque` (N m at the peak), `maxRpm`, `idleRpm`, `peakRpm`, `curve` (`petrol`, `diesel`, `electric`, `turbine`, `flat`, `steam`), `fuel` (`fuel`, `electric`, `none`), `burnRate` (litres a second at full throttle), `starter` (needs a battery), `starterKJ`, `spool` (seconds), `noise` (0 to 1). [Drivetrain parts](drivetrain-and-engines.md#drivetrain-parts-the-families). |
| `wheel` | `radius`, `grip`, `offroad` (times grip on dirt), `side` (times grip sideways), `roll` (rolling resistance), `drive` (false: a caster), `flat` (goes flat when wrecked), `track` (`length`, `rays`). Needs an `axle` socket. |
| `suspension` | `travel` (m), `frequency` (Hz, at most 6), `damping` (ratio), `maxLoad` (kg a wheel), `active` (a wired signal sets its height), `presets` (`label`, `frequency`, `damping`, `hold`), `ride` (a gas strut), `slew`, `motor` (a hub motor), `path` (the wheel's way through its travel: [Suspension types](drivetrain-and-engines.md#suspension-types)). Needs an `axle` socket. |
| `transmission` | `kind` (`manual`, `sequential`, `automatic`, `cvt`, `reversing`), `gears` (reductions, lowest gear first), `labels`, `gear`, `reverse`, `shift` (s), `efficiency`; a CVT's `range`. |
| `differential` | `kind` (`open`, `locked`, `limited`, `lockable`), `ratio` (the final drive), `efficiency`. |
| `ports` | The part's link ports by function (board item np01, [Named link ports](power-and-links.md#named-link-ports-np01)): a list of `{name, type, dir, socket or at + face, fluid, noEffect}`. |
| `drive` | A transfer part (role `transfer`): `kind` (`shaft`, `ujoint`, `cvjoint`, `gearbox`, `chain`, `belt`, `clutch`, `transfercase`, `pto`, `hub`, `stub`), `ratio`, `efficiency`, `ports`, `maxAngle`, `maxTorque`, `front`, `setting`. |
| `fuelTank` | `capacity` (litres; a tank comes full). |
| `fluidTank` | `capacity` (litres; it comes empty) and `flow` (litres a second through its pipes at most): [Fluid tanks](power-and-links.md#fluid-tanks). `envelope` makes a sized box a lift cell: [Air and lift](flight.md#air-and-lift). |
| `seat` | `at`: where the sitter's feet go; `helm`: true, it drives before any other seat ([Large systems](big-systems.md#large-systems)); `heal`: health a second the sitter gets back while it works, 0 to 50 (the Medical Station: docs/sandbox/INTERIORS.md phase 8). |
| `hinge` | `axis` (a direction), `pivot`, `min`/`max` (degrees), `leaf` (the leaf socket: what attaches there swings). |
| `joint` | Milestone 2, a moving joint: `type` (`motor`, `turntable`, `piston`, `steering`), `axis`, `pivot`, `min`/`max` (degrees; a piston's are metres, `min` 0 and `max` its travel up to 4 m; a turntable without them turns forever), `leaf` (a struct socket: anything fitted there moves, a door's `leaf` socket too), `speed` (degrees or metres a second), `strength` (N m, or N for a piston). |
| `dock`, `launch`, `conveyor` | [Large systems](big-systems.md#large-systems): a docking clamp's or magnetic lock's block on a cargo logic part, a catapult's or arrestor's, a conveyor's. |
| `logic` | Milestone 2, a logic part: `kind` (below), and for the kinds with a setting `settings` (the values it cycles through), `labels` (what the HUD shows; an input reader's are its keys), `setting` (the starting index), `settingName`. |
| `group`, `section` | Clues for the inventory's rules, `Constructs/Categories.json` ("The inventory"; each up to 32 characters; a section needs a group). A `group` that names a category places the part there. Without them it goes by its role. |
| `yield` | A material bundle (the Material Bundle a broken bin spills): the units the Recycler gives back (its Setting is the material id). Other parts give back by mass and class ([Salvage haul](gathering-and-salvage.md#salvage-haul-decision-31)). The old `scrap` key is gone (protocol 106). |
| `config` | The part's settings schema (up to 8), the configure tool's panel ([The configure tool](building-and-tools.md#the-configure-tool)). |
| `walk` | Building pieces: `floor`, `stairs`, `ramp` or `ladder`. Players and bots step up onto it ([Building pieces](#building-pieces)); bots' navigation may treat its tops as ground. |
| `damaged` | `{"below": 0.6, "part": "Concrete Wall 4 m Damaged"}`: below that share of its health the part turns into that stage in place (a wall losing a chunk). The stage needs `"stage": true`, the same role and exactly the same sockets; a chain's shares must fall. |
| `stage` | `true`: a damage stage of another part; not offered in the parts menu or on a rack. |
| `faces`, `faceAxis` | Smart rotation's facing hint ([Smart rotation](overview-and-playing.md#smart-rotation)): `faces` is `none`, `forward`, `back`, `up`, `outward`, `side` or `toSeat`, and `faceAxis` which of the part's own directions is its face (`-z`, its front, by default). Without them the role decides (seats and guns `forward` by `-z`; wheels `side` by `+x`; panels `outward` by `+y`; doors `outward` by `+z`; gauges `toSeat` by their dial's face). |

Milestone 1 shipped 13 parts: Frame Block, Frame Beam, Panel, Corner, Seat, Wheel, Suspension, Engine, Fuel Tank, Fuel Line,
Hinge, Door and Foundation. Milestone 2 adds 25: Deck Plate; Motor Hinge, Turntable, Piston, Steering Pivot; Transmission,
Differential; Button, Lever, Switch, Pressure Plate, Input Reader, Speed Sensor, Proximity Sensor, AND Gate, OR Gate, NOT
Gate, Timer, Delay, Toggle, Compare, Scale, Lamp, Horn and Brake. Milestone 3's weapons add 25 (in `Constructs/Parts/Weapons`:
three receivers, thirteen gun parts, five heavy guns, two ammo boxes and the feed chute) and the Wood Panel.

**No coplanar faces (z-fighting).** Two drawn boxes of a part whose faces lie on the same plane and face the same way
flicker through each other: make one a little different (a detail box inset or proud by 2 to 5 mm; a socket stays on
the solid box's face). `--check-construct-parts` prints the pairs it finds and the self-test section "No z-fighting"
checks every shipped part. The generators in `tools/constructs/` nudge their boxes through `tools/constructs/zfight.py`
before they write a file, so regenerate rather than hand-edit their output.

### Blueprints: `Constructs/Blueprints/*.json`

A blueprint is a **build script**: each step fits a part onto a socket of an earlier step's part, through exactly the
same rules a player builds by, so a blueprint that loads proves the rules can build it.

```json
{ "format": "call-of-slop-construct-blueprint/1", "name": "Gate", "steps": [
  { "part": "Foundation" },
  { "part": "Frame Block", "on": 0, "socket": "top01", "by": "-y" },
  { "part": "Hinge", "on": 1, "socket": "+x", "by": "base" },
  { "part": "Door", "on": 2, "socket": "leaf", "by": "hang", "spin": 0 }
] }
```

A step's `"spin"` (0 to 3, the fixed order) may be `"auto"`: smart rotation's context guess for that socket and `by`
socket, without any player's memory, so every machine builds it the same ([Smart rotation](overview-and-playing.md#smart-rotation)). A step may carry `"setting": n` (a logic part's starting setting) and `"config": {"period": 2.5, "colour": "#ff2010",
"startOn": true, "setting": "jump"}` (its settings by key: numbers, true/false, colours, option names), and a blueprint may list `"wires": [[from step, to
step], ...]`, each checked by the linking tool's rules. Shipped: **Buggy** (23 parts: rear-wheel drive through a
transmission and a differential, front wheels on steering pivots), **Wired Buggy** (30 parts, 6 wires), **Gate** (6
parts), **Motor Gate** (8 parts, 2 wires), **Lift** (7 parts, 3 wires) and **Turret** (6 parts, 1 wire); milestone 3:
**Gunner Turret** (9 parts, 1 wire), **Hood Gun Buggy** (26 parts, 2 wires), **Sentry** (7 parts, 1 wire), **Gun Bench**
(9 parts, 1 wire), **Target Wall** (8 parts) and **Ammo Dump** (3 parts); the drivetrain families: **4x4 Truck** (38
parts), **Tracked Crawler** (18), **Racer** (24) and **Electric Cart** (23), and the buggies rebuilt on the structural
parts (the Buggy 26 parts, the Wired Buggy 34, the Hood Gun Buggy 27; [Drivetrain parts](drivetrain-and-engines.md#drivetrain-parts-the-families)).
A blueprint may say `"tow": "Name"`: spawning it also spawns that blueprint behind it with its first Tow Eye on this one's first Tow Hitch, coupled (`ConstructBuild.BuildOnGround`). A blueprint may say `"group": "Bandit"`: the Blueprints tab shows it under that group instead of by what it holds. The **Bandit Technicals** (`Constructs/Blueprints/BanditTechnical*.json`, parts in `Constructs/Parts/Bandit` from the part kit's `--gen-parts bandit_parts`, body parts in `Constructs/Parts/Body` and `Constructs/Parts/Hull` from `tools/constructs/body_parts.py` and `hull_parts.py`) are written by the vehicle kit (docs/vehiclekit/VEHICLEKIT.md): change the generator, not the files.
A blueprint may say `"loot": [{"step": n, "table": "bandit"}]`: that step's container fills from the table when the blueprint is spawned ([Loot](#loot)).
A blueprint may say `"stored": [{"step": n, "slot": k, "part": "Box Magazine", "count": 2}]`: those parts are set in that container's slot when it is spawned, before its loot rolls into what is left, by the slot's own rule (an item that fits; a slot holding another kind is left alone). The vehicle kit's cargo pass stows a faction vehicle's spares this way (docs/vehiclekit/VEHICLEKIT.md, "Cargo and spares"). A player's own blueprint of such a vehicle keeps neither.
The **RV** (96 build steps, 90 parts after shaft chains migrate, about 2.65 t, 8 wires, a fuel pipe) is a walk-in motorhome: a driven, sprung, steered chassis, a cab, a hinged door with a boarding ladder, a dinette, a bunk over a cargo bay, lamps on switches and a motorised awning. It is generated by `tools/constructs/make_rv.py` and checked by the "RV blueprint" self-test.

### Loot

`Constructs/Loot.json` (`ConstructLoot.cs`) holds a table per faction: `maxTier` (the Assembler tier of the most advanced part it may hold, 2 for bandits: Scrap and Workshop), `rarities` (for weapon builder parts), `rolls` (picks a container gets), `empty` (the chance it holds nothing) and `items` (part, weight, count). What breaks those rules, a physics-only part above all, since slots hold items, is left out and listed by `--check-construct-parts`.

**When it rolls.** In `ConstructBuild.BuildOnGround`, so on the server and on every spawn: the Tab inventory, a map's constructs, a survival world's. Not in a scratch build, a stamp or a clip. Rolls come from a seed the world steps per spawn, nothing to do with the simulation, so they touch no seeded digest. What rolls goes into the container's ordinary slots through `ConstructStorage.Check`, and clients get it as any store change; no wire change.

**Saves.** From then on it is stored parts: saved with the world, taken by hand, spilled when the container goes. A load restores what the store held and rolls nothing. A player's own blueprint of such a vehicle keeps no loot and no key.

### The map's `"sandbox"` section

```json
"sandbox": {
  "rack": [-22, 23, 180],
  "rackParts": ["Frame Block", "Frame Beam", "Seat", "Wheel"],
  "constructs": [ { "blueprint": "Buggy", "at": [-8, 4], "yaw": 0 } ],
  "loose": [ { "part": "Wheel", "at": [-14, 20] } ]
}
```

`rack` is `[x, z, yaw]`; `rackParts` defaults to every part; `"partsMenu": false` turns the inventory off (it is
on by default). Milestone 3: `"infiniteParts": false` makes the rack and the parts menu hand out only what the player's
inventory holds (on by default, so Construct Yard stays endless); `"debrisSeconds"` (default 180, 0 to 3600, 0: never)
and `"debrisNear"` (metres, default 12) set when wrecks are cleared away; `"removerInSurvival"` (default true) lets a survival map turn the [remover tool](building-and-tools.md#the-remover-tool-board-item-rt01) off. Unknown parts or blueprints, and bad values,
refuse the map with the reason.
Only the server reads the section (clients get every construct from the server), so it is not part of the fingerprint.
The Sandbox mode is offered only on maps with this section.

## Physics-only parts and cargo volumes (decision 21)

Board item dev-sandbox-cargo-volumes-cv01, the developer's decision 21 (2026-10-30).

### Physics-only parts

A part is **physics-only** when it is fitted with the attach tool, when it is heavier than the hand-carry limit
(`ConstructParts.HandCarryLimit`, 40 kg: the backpack's limit), or when its file says `"physicsOnly": true` (light
but bulky pieces: `tools/constructs/building_parts.py` sets it for the 1.25 m wood door, the fence gate, the wood
railings, the 2 m wood beam, the tower brace and the 1 m window glass). The rule reads the attach tool hint and the mass, never a
list, so every new structural or heavy part follows by itself. Such a part is never an item:

- It can't be held, stowed in the backpack, set into a storage slot or picked up; each refusal says why ("the Frame
  Block is physics-only (structural): it can't go in the backpack - move it with the physics gun or carry it in a cargo
  volume").
- The give rule (`ConstructEdits.Give`) drops it as a body where it was, so every path follows it (salvage, merge
  pop-offs, storage). Taking one off a construct cuts it free as a body at its place, moving as the construct moved.
- The creative inventory (Tab) and the parts rack spawn it as a body a short way in front of you at eye level (clear of
  the map, constructs and players; it then falls and settles). With the physics gun out, the gun holds it. An item
  still comes into your hands, ready to fit.
- It is built onto things with the attach tool (decision 27): the three-step [socket attach](building-and-tools.md#socket-attach-board-item-sw01-decision-26)
  (pick its socket, pick where it goes, spin it), or by moving it into place with the physics gun and joining it (F).
- The inventory tile has a small boxed corner mark and its tooltip says "PHYSICS ONLY (structural)" or "item"; the look
  text says "physics only" too.
- What a save held in hands, backpacks or storage that is physics-only drops into the world as bodies
  (`ConstructEdits.DropPhysicsOnly`; world saving isn't built yet, so the server runs it at every match start).
- **Creative** (infinite parts on): an item's fit or removal is instant; a physics-only piece keeps its time. Survival and counted parts keep the timed edits.

### Cargo volumes

The **Cargo Volume** part (`Constructs/Parts/Storage/CargoVolume.json`, fitted as an item, a logic part of kind `cargo`)
is a small control box with a box of space on its construct: its size (`sizeX/Y/Z`, 0.2 to 8 m, 10 cm steps) and
centre (`atX/Y/Z`, from the part, in its frame) are its settings, and `startOn` makes it start frozen. Idle, only a
faint marking of its floor shows; with a tool out the whole box is outlined (ice blue frozen, amber free) and the
control box's lamp shows the same colour.

- **Frozen**: F on the control box flips it; a data link into it sets it on a change of its input (the last change
  wins, so both work on one part). While frozen, every loose body mostly inside the box (its centre of mass in it and
  at least half its part mass) rides with the construct kinematically: no physics of its own, no collisions with the
  carrier or the carrier's other cargo, its mass, centre of mass and inertia added to the carrier's. It is still a
  body: it can be shot, stood on and picked up (an item), and other bodies hit it.
- **Unfrozen** (or the volume broken or gone, or the box resized away from it), everything goes free at the carrier's
  velocity at that point.
- **A gunner's look** passes through what their own construct carries (`ConstructWeapons.GunnerPoint`, `ConstructWorld.Raycast`'s
  `ignoreCargo`), as it passes through the construct itself: a seated gun doesn't converge on its own load. Rounds still hit it.
- **Things entering while frozen** (the open choice): caught once they have settled, moving with the construct under
  1 m/s relative to it (checked ten times a second); freezing catches everything inside at once, moving or not. A body
  held by the physics gun is never caught, and grabbing a carried body takes it out. Not caught: anything anchored or
  frozen by the physics gun, anyone's seat in use, something hitched to the carrier, another carrier with cargo.
- **The volume tool** is a configure-tool mode: with the configure tool out, every volume near shows a handle on each
  face; hold Fire on one and drag to move that face (snapped to the 10 cm grid; the opposite face stays). R on the
  control box then Fire-drag on the construct draws a new box from corner to corner (its height kept). Fire on the
  control box opens its settings panel, where the size and centre are numbers. Each change is the configure tool's own
  request (`CActKind.Configure`), checked and snapped on the server.
- **Limits**: 4 volumes a construct (a fifth is refused when fitted), 24 carried bodies a construct, 8 m a side.
- **Blueprints**: a `"cargo"` list of loose parts (`{ "part", "at", "turn" }` in the construct's frame) built inside a
  volume that starts frozen; `ConstructCargo.ToJson` writes a construct's cargo back in that form. The **Cargo Rack**
  blueprint is the example. World saving isn't built yet: frozen contents round-trip through the construct message and
  blueprints, relative to the construct.
- **Network** (protocol 93): server-authoritative. A carried body's `CBody` ends with its carrier and its pose in the
  frame of the volume's segment; every machine moves it with the carrier (a client after blending the poses), so a
  carried body costs no pose traffic. The frozen state rides in the part's signal.
- **Cost**: carried bodies skip the physics; the cargo step moves each carried body every tick and looks for what
  entered or left ten times a second; it allocates nothing.

Code: `Source/Constructs/ConstructCargo.cs` (the rules), `ConstructPhysicsOnly.cs` (the item rule),
`Source/App/Program.SandboxCargo.cs` (the handles), `Source/Render/ConstructRenderCargo.cs` (the drawing).

**Self-test**: "Sandbox physics-only parts", "Sandbox cargo volumes" and "Sandbox cargo network"
(`Source/App/SelfTestSandboxCargo.cs`).

Choices made (the developer may change them): things entering a frozen volume are caught once settled (not at once,
not only on the next freeze); a carried body's mass counts at its place in the body (a volume on a joint carries its
cargo with the joint, but the joint's load doesn't count it); the volume's frozen clamps need no power; no cargo in
cargo; the volume tool is a configure-tool mode (no new tool or key); the six building pieces above plus the window
glass are the bulky ones; creative means infinite parts on (as for the physics gun).

### Gravity parts (space phase 5)

The **Gravity Well** (pulls, or pushes on a negative signal, everything near it; on a ship in space, the other ships)
and the **Gravity Generator** (its box gets its own down, the part's -Y; sized like a cargo volume) are logic parts in
`Constructs/Parts/Gravity/`, switched with F or a data signal, needing power. Neither moves its own construct. Code:
`ConstructGravity.cs`; how they reach players, constructs and ships: docs/space/GRAVITY.md.

## Structural parts (the scale chart)

The developer's scale chart (2026-10-29, "Realistic"): a player is 1.8 m, a buggy 4.0 by 1.8 m, a truck 8 m, a crawler
7 m; **frame step 0.25 m** (beams and plates 0.25 to 2 m); wheels 0.2 to 1.2 m radius; engines 0.4 to 1.0 m; the 1 cm
grid and the 24 turns unchanged. The structural parts are written by `tools/constructs/structure_parts.py` into
`Constructs/Parts/Structure` (110 files; run it again after changing a family, `--check` compares) and are plain part
files like any other.

**The cell rules** (the "Structural parts" self-test checks every part in every turn):

- A part is made of 0.25 m cells, and its origin is the centre of one of them. Every socket sits on a cell face, in the
  middle of the cell's face. Out of the face a socket is half a centimetre off the whole ones (12.5 cm from a cell
  centre; a thin plate's front, a rod's side and a cap's front likewise: thicknesses are whole centimetres, rods and
  tubes 5 and 11 cm across), along the face on a cell centre. So two sockets that meet always put the new part's
  origin on a whole centimetre, and its cells carry on the old part's 0.25 m lattice.
- A thin plate lies on the face of its cells (its back on the cell face): a plate on a beam sits flush on it, a plate
  on a plate stacks. Its edge sockets are 1 cm into the plate, so a plate edge attached to a beam's side sits on the
  beam's middle.
- The old big pieces (Frame Block, Frame Beam, Panel, Corner, Deck Plate, Wood Panel, Bumper, Fender) keep their sizes
  and sockets, so every blueprint and test that uses them still builds (they are on a 0.5 m grid: a new part on one of
  their sockets lands half a centimetre off its lattice, which nobody sees). The blueprints move to the new parts
  separately (the drivetrain work); the old pieces can then be retired.

**The families** (the inventory's groups and sections come from each file's `"group"` and `"section"`):

| Group | Sections and parts |
| --- | --- |
| Beams | Square beams 0.25 (a cube: the frame's node), 0.5, 0.75, 1, 1.5, 2 m; I-beams 0.5 to 2 m; steel tubes (11 cm) and rods (5 cm) 0.5, 1, 2 m; angle iron and channel 0.5, 1, 2 m; braces 45 degrees across 0.5, 1, 2 m and 1:2 across 0.5x1, 1x2 m, cross braces 0.5 and 1 m; junctions (corner, T, cross, 3-way corner, 6-way hub); L and corner brackets, gussets 0.25 and 0.5 m; beam and tube end caps; Blocks (the old Frame Block and Frame Beam) |
| Panels | sheet steel and plate steel 0.25 to 2 m (squares, halves 0.5x1 and 1x2, quarters); wood planks, glass panes, mesh grates, tread plate; openings (window frames 1 m and 1x0.5 m, gun port, vent, hatch frame, door frame 1x2 m: real holes, rounds go through them); triangle plates 0.5, 1, 2 m; Body panels (the old Bumper and Fender) |
| Armour | armour plate 0.5 to 2 m, heavy armour 0.5 to 2 m, gun ports in both |
| Shapes | wedges 1:1 (0.25, 0.5, 1 m), 1:2 and 2:1; slope (outer) and inner corners 0.5 and 1 m; quarter rounds 0.25, 0.5, 1 m and curved plates (quarter pipes) 1 and 2 m; pyramid caps; round and chamfer trims 1 and 2 m |

**Materials** (mass and health grow with the area or length; the armour is the flat amount taken off every hit, the
same for bullets, the construct guns' rounds and blasts; burning ignores it):

| Material | Thickness | kg per m2 | Health (1 m2) | Armour | What it stops |
| --- | --- | --- | --- | --- | --- |
| Glass | 3 cm | 9 | 28 | 0 | nothing (any round breaks it) |
| Wood | 5 cm | 7 | 90 | 1 | nothing; it burns |
| Mesh grate | 3 cm | 5 | 80 | 3 | bars you see and shoot through |
| Sheet steel | 2 cm | 8 | 70 | 6 | the toy blaster |
| Tread plate | 3 cm | 12 | 140 | 10 | buckshot pellets up to 10 |
| Plate steel | 5 cm | 20 | 160 | 20 | pistols and SMGs up to 20, every buckshot pellet |
| Armour plate | 8 cm | 45 | 320 | 40 | every SMG, rifle, LMG and buckshot round, pistols up to 40, the mounted MG |
| Heavy armour | 12 cm | 80 | 550 | 68 | every pistol, SMG, rifle, LMG and marksman round and buckshot (snipers over 68, the slug, the autocannon, cannon, rockets and blasts get through) |

Steel beams: square 10 kg a metre, armour 8; I-beam 13 kg, armour 10 (tougher); tube 4 kg, armour 5; rod 1.6 kg, armour
3; angle 4 kg and channel 6 kg, armour 6 and 7. Shapes are solid steel (armour 8). Armour class parts recycle at a higher rate than steel (the recycling table). Wood and glass are fitted with the attach tool, everything else with the attach tool;
heavy armour takes 5 s to attach.

**Shapes**: a part box may take one of the weapon builder's contoured shapes (`"shape"`: wedge, arc, cylinder, bevel,
rounded..., and on construct parts `hull`: `"points"` in the part's frame, drawn as their convex hull; `Source/Zombies/PartShapes.cs`, Parts/README.md "Shapes"), but only as a detail box (drawn only): collision
and hit areas stay boxes, so a shaped part stands on **hidden** solid boxes (`"hidden": true`: solid, never drawn) that
step along its shape (a wedge's slope in 4 to 12 steps). The shape turns with its part (`CPartBox.Model`); the tiles,
ghosts and the part in your hands draw it too.


**Choices made (the developer may change them)**: the old big pieces are kept unchanged (not rescaled) so the
blueprints still build; the armour numbers (40 and 68) are set so armour plate stops every automatic small arm and
heavy armour everything short of a sniper rifle; glass is see-through ([Glass](#glass), below); a mesh grate is solid bars with real gaps (rounds pass between them); a part's inventory place is data
(`"group"`, `"section"`), the top folder's parts listed after the subfolders'.

### Glass

A part box with `"glass": true` (or how much of it is drawn, 0.1 to 0.9; `true` is 0.3) is a see-through pane. The four
Glass Panes' panes are (`tools/constructs/structure_parts.py` writes them); the "Construct glass" self-test checks every
part of `"material": "glass"` has one. A glass box may be solid or a detail, not hidden.

- **Drawing** (`Source/Render/ConstructRender.cs`): the opaque pass leaves panes out and keeps them for `DrawGlass`, which
  runs in the blended pass after everything opaque (the mode's `DrawObjectives`): farthest first by distance from the eye,
  blending on, depth writes off, lit and fogged by the box shader (mode 0, so both GL paths; no new texture units or
  bindings). A pane is tinted by its colour, drawn more opaque and lighter at grazing angles (a little reflection), and
  has a thin lighter strip along each long edge so it still reads as glass edge-on. At most 512 panes a frame are kept;
  more are drawn opaque. The inventory's tiles (`SandboxTiles.DrawTile`), the part in your hands (`ToolRender.Emit`) and
  a stored or packed pane draw it blended after the rest; a ghost's pane is fainter than its frame.
- **Collision**: a pane is a solid box like any other: players, constructs and wrecks are stopped by it.
- **Bullets**: a player's round goes through up to four panes (`Game.SandboxRay`), hits what is behind (a player in a
  cab, a part, the map) and damages each pane it passed, at full strength. A construct gun's round does the same. Blasts
  hurt glass like any part.
- **Breaking**: glass is weak (health 13 to 48, armour 0). A broken pane **shatters**: it is removed, no wreck piece falls,
  and a `CFireKind.Shatter` event (protocol 87: where, and its world bounds' half size in centimetres) bursts small shards
  out of it on every PC (the prop debris pool) with the glass sound.

**Choices made (the developer may change them)**: the opacity 0.3; rounds keep their full damage through glass; glass
shatters whole (the frame round a Glass Pane goes with it); sorting is by each pane's centre (two panes crossing each
other may blend in the wrong order where they cross).

## Size classes (the capital parts)

The developer's request (2026-11-03): "all the parts that are in the sandbox mode are what I would call small vehicle
parts. I'm planning on vehicles getting quite large. Compare the size difference between a plane atop an aircraft
carrier." The gameplay is a mothership launching small vehicles: a carrier (300 m, 100,000 t) or a Mortal Engines traction
city (a tracked land mothership under a multi-storey town). Smelters reach 10 m and Storage Volumes 40 m already.

| Class | Scale | Cell (lattice) | Plate | Frame members | What it builds |
| --- | --- | --- | --- | --- | --- |
| Small | x1 | 0.25 m | 5 cm, 40 kg/m2 | 0.25 to 2 m | a buggy, a plane (every part before this; they stay as they are, group by family) |
| Medium | x4 | 1 m | 10 cm, 80 kg/m2 | 1 to 8 m | a truck, a tank, a bomber; balconies and rooms |
| Large | x16 | 4 m | 20 cm, 200 kg/m2 | 4 to 32 m | a hangar, a storey of a town, a big walker, a frigate |
| Capital | x64 | 16 m | 50 cm, 500 kg/m2 | 16 to 128 m | a carrier's hull, a traction city's chassis |

**One helper**: `tools/constructs/scale.py` holds the classes (`SMALL`, `MEDIUM`, `LARGE`, `CAPITAL`) and what every big part's
generator shares: `Part`, `box`, `block_sockets`, `write_parts`. `tools/constructs/capital_parts.py` writes the structure
(`Constructs/Parts/Capital`, 441 files; `--check` compares). Part files say `"sizeClass": "capital"`; a socket takes its part's
class unless it says `"class"`.

**Sockets.** A larger class's part is made of its own cells and its sockets step on its own coarser lattice (a capital plate
has a socket every 16 m, so 255 sockets a part are plenty). A socket snaps, autolinks, joins and socket-joins only to a
socket of its own class, and a mismatch says "use an adapter". The freeform and point attachments (one part anywhere on another)
ignore classes.

**Adapters** (section Adapters, brass pads) join two classes: one socket of the big class on the back, a 5 x 5 grid of the
smaller class on the front (`Adapter Capital to Small` and `...Wide`, `Adapter Capital to Large`, `to Medium`, `Adapter Large to
Medium`, `to Small`, `Adapter Medium to Small`). A small part's whole-centimetre origin is kept: the pad's back is on a
whole centimetre and a small-class front half a one off it.

**The kit** (Medium, Large and Capital unless noted; the inventory has a group per class, a section per family; search finds
any part, and the tree stays usable at 441 more tiles). Traction-city pieces first, because a land mothership cares about
ground bearing and height:

| Section | Parts |
| --- | --- |
| Frames | frame node; girders 1 to 8 cells; hollow box frames; open trusses (chords collide, lacing is drawn); round columns; 45 degree struts; stepped buttresses; stepped corbels that let a storey overhang |
| Chassis (Large, Capital) | Chassis Deck grillages up to 128 x 64 m: deep beams between two plates, sockets underneath for track units and on top for the town; tapered Load Spreaders |
| Plates, Armour | hull plate, rough riveted plate; armour plate and armour belt, rivets drawn; 1 x 1 to 4 x 4 cells |
| Walls | brick and plaster walls; window walls with real openings; arcades of 1 to 3 arches, stepped spandrels with a drawn arch ring |
| Decks | steel, timber and grate decks; railed balconies; terraces with a parapet on three sides |
| Roofs | domes and onion domes (stacked frustums), spires, hip, gable and barrel roofs |
| Stacks | banded riveted smokestacks and brick chimneys, up to 32 x 128 m; collision is a square of walls |
| Shapes | curved plates, wedges |
| Stairs | a flight (every step within the 0.55 m a player walks up) and a steep ship ladder for one cell, at every class |
| Hull (Large, Capital) | hollow hull sections (a floor, a ceiling and two sides, open ends), bows, keels |

A hollow part spends boxes where it shows: state of it, about 5.5 boxes a part in the test hull. A part has at most 32
boxes and 255 sockets and reaches at most 200 m from its origin.

**Limits**, and what set each (every fixed cap in the game, its class and what was done about it: [docs/perf/LIMITS.md](../../perf/LIMITS.md)):

| What | Before | Now | What stopped it there |
| --- | --- | --- | --- |
| Parts a construct | 96 | 16,000 (8,000 until docs/perf/LIMITS.md) | a rebuild's cost (about 2 ms at 8,000 parts, 5-9 ms at 16,000 in the container), and the link count: a u16 on the wire, so 21,845 parts at three links each without widening it. The part-sized spans are per-thread scratch now (`Scratch.cs`), not the stack |
| Links | 288 | 48,000 | three a part, indexed by part (`Construct.FirstLink`); the count is a u16 on the wire |
| Joints + freeform attachments | 16 (8 joins) | 255 (192) | a segment is a byte everywhere |
| Wheels and track rays, spring feet | 12 | 256 | a raycast each a tick; the wire doesn't care |
| Guns | 8 | 256 | a turn through the gun step |
| Routed links, nodes | 64, 768 | 254, 3,000 | a route's parent is a byte with 255 meaning none (a hundred sites and the tests); widening it is mechanical if a ship needs more |
| Wires | 64 | 1,024 | |
| Part mass, health | 50 t, 100,000 | 50,000 t, 16,000,000 | health is u24 on the wire |
| Part reach, sockets | 5 m, 40 | 200 m, 255 | a socket index is a byte |
| Piston travel, hitch reach | 4 m, 1.5 m | 64 m, 64 m | |

Every per-construct array is sized to the construct now and grows by doubling on an edit (`ReserveParts`, `ReserveLinks`,
`ReserveJoints`, `ReserveGuns`, `ReserveRoutes`): a loose block costs less than it did and a world of thousands stays small.
Nothing grows in a tick.

**Keeping it fast.** Nothing that runs per tick or per frame walks a big body's parts any more:

- `Construct.FirstLink` / `NextLink`: a threaded list through the links, so `SocketUsed`, the split flood, the circuit and the
  drive line's alternators cost a part's own links. `AddLink` keeps it up; a compaction calls `LinksChanged`.
- `ConstructPartTree.cs`: a tree over the rigid parts (rest frame, built on first use after a rebuild; parts on moving
  joints and parts added since are always handed back). Rays, a player's blocking, grab-ons, placement and ghost checks ask
  it (`PartsInBox`, `PartsOnRay`). Under `TreeMinParts` (32) they walk the list as they always did. `ConstructCollider`
  (feat/collision) has its own tree over boxes for contacts; the two can merge later.
- Physics: joints are laid out end to end for the bodies simulated (`LayOutJoints`), not a fixed 255 slots a body. The inertia
  inverts in doubles (a float determinant of a 100,000 t hull's terms is close to overflowing). Spin counts as still by what
  it does at the body's far end, so a slow 300 m hull doesn't fall asleep rolling.
- Drawing (`Source/Render/ConstructRenderBatch.cs`, docs/render/MERGED-MESHES.md): every body's static parts (a capital
  hull's included) are merged meshes, chunks of up to 2,048 boxes 32 m a side per segment on the GPU, one draw for a chunk's
  plain boxes and one per contoured shape in it, culled chunk by chunk. Only what moves or changes on its own is walked:
  joints, wheels, rigs, levers, lamps, glass, engines, burning parts and stores. Looks are rewritten alone when health,
  paint or frost change; an edit rebuilds only the chunks it touched. A big body is never drawn as one far box, and is drawn
  to 3,000 m.
- Network ([below](network-and-scale.md#network-protocol-82-84-for-free-placement-86-for-the-drivetrains-rpm-87-for-glass-shattering-88-for-the-physics-gun)): u16 indices, positions in 3 bytes a coordinate
  (+-83 km on the 1 cm grid), and patches.

**Numbers** ("Capital parts", "Capital network"; a generated body of 4,050 parts, 100,000 t, 300 x 108 x 4 m, through
`ConstructBuild.Place` and `Attach`; the developer's PC VM, or the loaded cloud box where it says so, wall clock). A body of this
size could not exist before, so "before" is the old method measured or counted on it.

| What | Before (the old method at 4,050 parts) | After |
| --- | --- | --- |
| Rebuild | not measured; the circuit, the split and `SocketUsed` were each parts x links (about 40 million steps) | 2.4 ms (9 ms loaded) |
| Tick, dragged, awake, on flat ground | not measured | 0.01 ms median, 0.01 ms worst (4 ms worst loaded), 0 bytes allocated |
| A ray | 1,094 us (every solid box of the 4,050 parts, measured) | 2 to 10 us |
| A player's blocking, a tick | 22,000 boxes, about the ray's cost | 4 us |
| A part off the middle | not measured | 2.6 ms (21 ms loaded) |
| Draw calls | 22,275, one a box | 1 instanced draw; 0 parts walked for this plain hull |
| Whole body on the wire | 133 KB per edit | 133 KB once; an added part 125 to 144 B |
| Client receives it, loopback | | 0.10 s |
| Late joiner, loopback | | 0.10 to 0.15 s |

**Not in budget yet:** a small vehicle on the deck. A buggy resting on the hull worst-cases 23 to 39 ms a tick, because the
body-on-body contacts still walk the big body's parts (`ConstructPhysics.BodyContacts`, 40 hull points x every part). That is the
compound collider's work (feat/collision), and "Capital parts" prints the number without failing on it.

**Choices made (the developer may change them)**: the physics gun lifts 20,000 t in creative with at least 8 m/s2 (it was 15 t), and survival stays at 500 kg, so a big part there goes on with the socket attach or a crane; four classes at x4 steps; strict socket classes with adapters, and the
freeform attachments as the escape; a stair is one cell at every class; plate thickness and kg/m2 by class; the game's own soft
density (a 500 kg/m2 capital plate is a fifth of real steel at 50 cm) so that 4,000 parts come to about 100,000 t; hit points
by area, capped at 16 million.

## Building pieces

Board item dev-sandbox-building-pieces-bp01 (the developer's note, 2026-10-30): large prefabricated parts for anchored
builds (bases, bunkers, towers) in the inventory's **Buildings** category. They are written by
`tools/constructs/building_parts.py` into `Constructs/Parts/Building` (178 files; run it again after changing a family,
`--check` compares), and the same script writes the four example blueprints (it mirrors the game's Rot24 turns and fitting
order to work out each step's sockets from where the part should go).

**The module.** The pieces keep the structural parts' 0.25 m cell rules (origin on a cell centre, sockets on cell faces at
cell centres), so they line up with the beams and panels. Walls are 0.25 m thick and 2.75 m tall; a floor slab is 0.25 m
thick, so a storey is **3 m floor to floor**. Lengths are **2 m and 4 m**. A room's corners are **L-shaped corner pieces
with 2 m legs** (8 cells each, sharing the corner cell): four close a 4 by 4 m room, and straight 2 m and 4 m walls fill the
module between corners on bigger rooms (4 by 6, 4 by 8...). An inside wall meets an outside one at a **Wall T** (a 2 m
crossbar, a 1.75 m stem), a free wall ends in a **Wall End**. Floors, foundations and bunker roofs have sockets at the
module points (cells 0, 7, 8 and 15 of each side) where walls stand; a slab on the walls' tops joins to every one it
meets, and the next storey's corners stand on the slab.

| Section | Parts |
| --- | --- |
| Walls | per material: wall 2 and 4 m, half wall 2 and 4 m (1.25 m), parapets 2 and 4 m (0.75 m; wood and concrete) |
| Wall openings | window walls 2 m (one 1 m window) and 4 m (two), sills 1 m up; door wall 2 m (1.5 by 2.25 m doorway, a hinge socket on each jamb); garage wall 4 m (3 by 2.5 m); slit wall 2 m (two 0.5 m slits at 1.3-1.65 m, eye height); Window Glass 1 m (fits a sill) |
| Corners and ends | per material: wall corner, door corner (a doorway in one leg), window corner, wall T, wall end; parapet corner |
| Floors | wood, steel, concrete, reinforced: 2x2, 2x4, 4x4 m; hatch floor (1 by 2 m hatch for a ship ladder), stair floor (1 by 3 m opening a cell in from one side), stairwell floor (2 by 2.5 m) |
| Foundations | concrete 2x2 and 4x4 m pads (0.5 m), stepped (its back half 0.5 m deeper, for slopes), deep (four piers 1 m down), reinforced 4x4 m |
| Roofs | roof slopes 4x4 and 2x4 m (wood, corrugated; 1 m rise), corrugated lean-tos, ridge roofs 4x4 m (one piece, closed ends), gable walls (close a ridge made of two slopes) |
| Stairs and access | straight stairs 3 m (wood, steel, concrete: eight 0.375 m steps over 3 m), stair flights 1.5 m, half-turn stairs (2 by 2.5 m, a storey), Ship Ladder 3 m (steep: 0.375 m steps over 0.25 m), landings 2x2 m at 1.5 m, ramps (concrete 2x4 and 4x4, steel 2x4, wood 1x3 m), railings 2 and 4 m (steel, wood) |
| Doors and gates | Wood and Steel Door 1.25 m, Garage Door Leaf, Fence Gate: door-role leaves hung on the existing **Hinge** (so Interact and logic open them; a hinge's base goes on a jamb's socket). The flush doors for these openings are in [Doors](big-systems.md#doors) |
| Defences | sandbag walls 2 m, 1 m, low 2 m, corner; Concrete Barrier 2 m; Blast Wall 2 m (3 m T-wall); wire-topped Wire Fences 2 and 4 m (seen and shot through, not walked through); Bunker Module (4 by 4 by 3 m, slits, a back doorway) and Pillbox (3 by 3 m), both foundations that anchor themselves |
| Watchtower | Tower Platform 2x2 m (parapet on three sides), Tower Brace, Tower Post 1.25 m, Tower Roof; the legs are Wood Posts |
| Columns and beams | concrete, steel and wood columns 2.75 m; beams 2 and 4 m |
| Damaged | the walls' damage stages (not offered) |

**Materials** (per square metre of wall; walls burn by their material, as before):

| Material | kg | Health | Armour | Class |
| --- | --- | --- | --- | --- |
| Wood | 30 | 45 | 2 (burns) | item |
| Corrugated metal | 18 | 55 | 4 | item |
| Concrete | 300 | 260 | 25 | structural |
| Steel plate | 160 | 320 | 35 | structural |
| Reinforced concrete | 360 | 420 | 45 | structural |

A part's mass may now be up to 50 t (a 4 m pad is about 14 t; was 5 t).

**Walls lose chunks.** The straight walls (2 and 4 m, every material) have two damage stages (`"damaged"` in the file):
below 60% of its health a wall turns into its **Damaged** stage (a 1 m chunk gone from a top corner), below 30% into
**Breached** (also a hole at the bottom, 1 m wide and 1.5 m tall: crouch through it), and at zero it breaks off as before
(`ConstructWorld.LoseChunk`). A stage has exactly the wall's sockets, so every attach stays; its health carries on from the
wall's. The structure goes out again (the part's id is in it): **no wire change**. Window, door and other walls break
whole.

**Walking.** `ConstructWorld.BlockPlayer` now steps a player (and a bot: the same code runs for every player) up onto the
boxes of anchored constructs and of parts marked `"walk"` once the player's disc reaches them, up to `World.StepHeight`
(0.55 m) above where the feet were that tick, as the map's own steps are: stairs, ramps, the ship ladder, onto a pad or a
low sandbag wall. A box whose top is below the feet no longer pushes (so walking from one floor slab or step onto the
next never snags). Loose and free constructs are unchanged (a parked car still blocks you).

**What bots still need** (the sandbox-spawns work owns bot navigation over constructs): today bots walk up stairs and
through doorways only when they already walk that way (their nav graph is the map's). For paths into and through
buildings they need the constructs' walkable tops as nav nodes (every `"walk"` part's top boxes; `Floor`, `Stairs`,
`Ramp`, `Ladder`), links through door openings (a door leaf is a passive joint: open it with Interact first, as players
do) and a rebuild when a construct is built, changed or breaks.

**Blueprints** (`Constructs/Blueprints`): **Two-Storey House** (21 parts: two pads, a 4 by 8 m concrete house with a
front door, window walls on both floors, a straight stair under a stair floor, two wooden ridge roofs), **Bunker** (11:
a Bunker Module on two pads with a steel door onto a sandbagged yard), **Watchtower** (15: a platform 3 m up on four
posts with three braces, a ship ladder and a corrugated roof) and **Small Outpost** (15: a corrugated hut with a door, a
fenced yard with a gate, sandbags at the front).

**Choices made (the developer may change them)**: corners are L pieces with 2 m legs, so a room's sides are at least
4 m (2 m walls go between corners); walls are 2.75 m under a 0.25 m slab; stairs rise 0.375 m a step (45 degrees on the
straight stair, a storey in 3 m); the ship ladder is a steep stair rather than a climbing mechanic; stair floors' openings
sit a cell in from the edge so the stair runs along a wall; doors are new 1.25 m leaves on the existing Hinge (the
milestone 1 Door is 1 m); only straight walls lose chunks, in two fixed stages, and the lost chunk leaves no rubble piece;
windows are open (a Window Glass fits each); the bunker and pillbox are foundations; masses are realistic-ish (a concrete
4 m wall is 3.3 t), which needs the 50 t cap.

## Performance and caps

A construct holds at most 16,000 parts, 48,000 links, 255 joints and freeform attachments (64 joins), 256 wheels and track rays,
256 spring feet, 256 guns, 512 thrusters, 32 modular engines, 254 routed links and 3,000 link nodes, 1,024 wires ([Size classes](#size-classes-the-capital-parts)
says what limits each). 4 inputs a part, wires up to 8 m (12 m between constructs, 255 of those), 255 couplings, 16,384
contacts a tick (each rigid group's points against one thing thinned to nine a normal). Fixed arrays throughout: the self-test drives a buggy round the yard for 120 ticks and
measures **no allocation** (with the turret turning, the lift and the motorised gate moving and their logic running);
the yard's constructs take about 0.1-0.4 ms a tick in the container. The logic step is a few spans of per-thread scratch (`Scratch.cs`: part-sized spans are never on the stack). Sleeping bodies cost
nothing. Other modes never touch any of it (every hook returns at once outside Sandbox; the seeded mode digests are
unchanged).
