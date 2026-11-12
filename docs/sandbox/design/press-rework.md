# Press rework: sized parts (design)

Design only (2026-11-12). No code changed, nothing built. Written for an Opus agent to build in six phases of 60 to 90
minutes. The user's nine decisions are taken as final and are referred to as D1 to D9.

Sources read: `docs/sandbox/constructs/production.md` (Extrusion, Wheel Former, Press, Panel Former, Assembling, Production
lines, Work orders, Crafting terminal, Reservations), `docs/sandbox/constructs/flight.md` (the Fan and Airframe Formers),
and the code named in section 1. Counts come from a throwaway script over `Constructs/Assembling.json` and the part files
(`/tmp/presssort/sort.py`, not in the repo).

Note for the coordinator: the brief speaks of 1,240 Assembler rows. `Assembling.json` on main holds **1,899** recipes.

## 1. What exists today

| Thing | How it works now | Files |
| --- | --- | --- |
| Extruded piece | A run-time `ConstructPartDef` per `ExtrusionKey` (shape, mask, W, H, L in cm, material, wall). Id from `Extrusion.RuntimeBase` (0x4000), this machine's own; wire, saves and hashes carry the key (`Extrusion.Write/Read`, `WireMark` 0xFFFE; names such as `Extrusion round 50x50x400 metal`). `Extrusion.Def(key)` makes it under a lock on first ask; `MaxDefs` 48,000 | `ConstructExtrusion.cs`, `ConstructExtrusionTable.cs`, `Constructs/Extrusion.json`, `RuntimeParts.cs` |
| Named run-time part | A part made from its name alone (`IsNamedRuntime`): lofted hull parts and formed panels, one door `HullLoft.Parse`, wire kind 245 plus the name as text. `ConstructParts.Find` dispatches by prefix. A formed panel's first make was 4 to 8 ms, so `HullLoft.PumpPanels` gives a cheap part at once and its facets in slices | `HullLoft.cs`, `HullLoft.Panels.cs`, `ConstructPanelFormer.cs` |
| Remade part | A file part in another material: `ShallowCopy` of the file's def, mass, health and armour scaled by material rows, always physics-only, key `Name (material)`, wire kind 246 | `PartMaterials.cs` |
| Press | A part with 2 or 3 bays and a die slot, all sockets; a piece or die in them is a part of the machine's construct, found from its link (`PieceIn`, `DieIn`). 10 Hz step over constructs with `HasPress`. A stroke makes `row.Output` (a **catalogue part**), `row.Count` of them, and takes **length** off each piece (`StrokeCm`, a def swap); a 2 m strip gives 20 Buttons | `ConstructPress.cs` (1,070 lines) |
| Pressing.json | Format `call-of-slop-pressing/1`: 33 flat `rows` (die, output, count, seconds, bays with sections, materials, minAcross, maxAcross, `use` or `useAcross`) and 13 `lathe` rows. The first row whose bays are all satisfied runs. Dies: Button, Board, Bracket, Gear, Piston, Housing (4 classes), Cylinder (4 classes) | `ConstructPressTable.cs`, `Constructs/Pressing.json`, generator `Source/PartKit/Generators/PressParts.cs` (`--gen-parts press_parts`) |
| Die | A part file with `"pressDie": "Family"`, first socket of kind `pressdie`; fits a press of its own size class only. Small and Medium are items, Large and Capital physics-only. Cut on a Die Lathe (the press's code, one bay, a `recipe` setting) | `Constructs/Parts/Press/Dies`, `ConstructPress.ParseDie` |
| Assembler | A `recipe` setting picks one row of `Assembling.json` (`output`, `count`, `materials`, `parts`, `seconds`, `minTier`, `machine`). At most 6 inputs, one recipe a part. Inputs are bulk in a mixed input and item parts in the store's kinds; **a physics-only part can't be an input**. Output: tray (items, 12 a slot), a body at `outAt`, or the Piece out conveyor | `ConstructAssembling.cs`, `ConstructAssemblingTable.cs`, generator `Source/PartKit/Generators/Assembling.cs` (46 by hand, the rest by rule from part files) |
| Work orders, bills | `WorkOrder`: a piece's key and count (kind 0), a catalogue part (kind 1, an Assembler's), or a name (kind 2, `NamedShape` 255, a Panel Former's). Queues on `CPart.Orders`; a Line Console's bill; `ConstructBill.FromBlueprint` lists `Pieces`, `Panels`, `Parts`, link stock; `--bill` | `WorkOrders.cs` |
| Crafting Terminal, reservations | Lists what linked machines and stores can give; orders reserve stock (`ConstructReserve`) or queue work orders. Named run-time parts in a store are not listed yet; "press products" are on its not-yet list | `ConstructCrafting.cs`, `ConstructReserve.cs` |
| Flight | Wings, control surfaces, props and rotors are already keyed pieces off the Airframe and Fan Formers (`ExShape.Wing`, `ExShape.Fan`). The catalogue files in `Parts/Wings` and `Parts/Rotors` have no recipe; the formers take no orders | `ConstructExtruder.GrowWhole`, `Extrusion.BuildWing/BuildFan` |

What the rework changes at the root: a stroke's output stops being a row's fixed catalogue part taken by length, and becomes
**a part whose key is the die's pattern plus the blank in bay 1** (D1, D2, D8).

## 2. The recipe sort

**Rule used.** A recipe is *single piece: becomes a die* when all of these hold, else it is *multi-part: stays*:

1. its output is a part file (27 outputs are not: names only the generator knows);
2. it is the plain Assembler's (the Thruster Assembler's 34 and the Tailor's 4 stay where they are);
3. it has **at most one kind of part input** (that one kind is the bay 2 resource of D3; bulk materials are the blank);
4. its output's role is not an assembly by nature: `storage` (every machine, bin, rack, station), `engine`, `thruster`, `gun`,
   `door`, `seat`, `generator`, `alternator`, `battery`, `burner`, `leg`, `wheel`.

| | Recipes | Name families (sizes, tiers and materials folded) |
| --- | --- | --- |
| All | 1,899 | |
| **Single piece: becomes a die** | **1,519** | about 595 |
| **Multi-part: stays** | **380** | about 190 |

Why a recipe stays: an assembly role 226; two or more kinds of part input 89; Thruster Assembler or Tailor 38; output not a
part file 27.

**Single piece, by role and by its one part input (the bay 2 resource):**

| Role | Recipes | Families | Bay 2 today (recipes) | Die (section 3) |
| --- | --- | --- | --- | --- |
| panel | 356 | 114 | none 350; Bearing 2, Copper Coil 1, Gear Set 1, Lens 1, Servo Motor 1 | Plate (the kit components among them: Gear, Piston, Board, Cable) |
| frame | 660 | 250 | none 636; Circuit Board 12, Control Unit 5, Gear Set 3, Servo Motor 3, Bearing 1 | Beam, Bracket, Housing, Mould |
| foundation | 8 | 6 | none | Mould |
| logic | 154 | 92 | Circuit Board 151; Lens 1; none 2 | Control, Button |
| joint | 112 | 14 | Servo Motor 72, Bearing 26, Gear Set 14 | Actuator |
| hinge | 6 | 3 | Bearing 6 | Actuator |
| suspension | 53 | 23 | Bearing 52; none 1 | Spring |
| transfer | 40 | 13 | Gear Set 40 | Drive, Shaft |
| differential | 15 | 8 | Gear Set 15 | Drive |
| transmission | 10 | 8 | Gear Set 10 | Drive |
| enginePiece | 36 | 9 | Piston Set 36 | Engine |
| fluidtank, fueltank, fuelline | 26 | 11 | none | Tank |
| ammobox, ammoswitch, feedchute | 12 | 6 | none | Housing |
| hitch | 11 | 7 | none | Bracket (eyes, hooks), Drive (winches) |
| hose | 11 | 10 | Wiring Loom 11 | Cable |
| gunpart | 9 | 8 | none 8; Lens 1 | Barrel, Receiver |
| **Total** | **1,519** | | none 1,054; one kind 465 | |

The largest families: Housing 21, Armour Plate 20, Chimney 18, Girder 18, Curved Plate 17, Piston 15, Wedge 15, Smokestack
15, eleven panel families of 15 (Armour Belt, Hull Plate, Riveted Plate, Deck, Grate Deck, ...), Linear Actuator 14, Free
Slider 14, Shaft Linear Drive 14, Column 14. Of the frame and panel rows, 441 are `Parts/Capital` and 153 `Parts/Building`:
the same shapes at more sizes, which is what one sized die replaces.

**Multi-part: stays, by role:** storage 130 (60 families: every production machine, bin, shelf, rack, station, scoop),
door 32, thruster 28, wheel 26, engine 23, panel 17 and frame 8 and logic 9 (two or more kinds of part), gun 15, generator
13, enginePiece 12, leg 12, seat 9, battery 6, alternator 4, burner 4, fluidtank 4 (the Tailor's), differential 1, and the
27 outputs that are not part files. Of the 226 kept for their role, most have bulk-only recipes today; they keep those
until a piece recipe is written for them (section 6, open question 5).

## 3. The dies

### 3.1 How a die is read

- **Blank** (D1): the piece in bay 1. Any extruded section preset, `round` or `pipe`; a custom mask is refused ("section").
  A plate or sheet is the `plate` preset. Unit frame of a blank: `x` across its width W, `y` across its height H, `z`
  along its length L, each 0 to 1.
- **Features** (D2): boxes (or lathe rings on a round) given in the blank's unit frame, so they stretch with the blank. A
  long bar under the Button die is a long bar with a long raised cap. A feature may carry `minCm` and `maxCm` on its
  thickness so a lip never thins below 1 cm or a bolt boss never grows to a metre. Every feature stands proud of, or is
  let into, the blank's face by the z-fighting rule's gap for its patch: 3 mm up to 2 m, 10 mm up to 8 m, 1/800 of the
  patch above (at most 50 mm). The builder computes that gap; data never states a coplanar face.
- **Pattern**: what the stroke makes of the blank. One die has several patterns of one family (the Drive die: gearbox,
  CVT, angle gearbox, differential...). Each pattern names a **like** part, the catalogue part whose behaviour it gives
  (role, logic kind, ports, settings, joint), and its **bay 2** resource. The pattern is a setting on the **die part**
  ("Pattern", filled per family as the parts load, as a lathe's recipe list is), so it travels with the die, saves as any
  setting and needs no second die (D3). Open question 1.
- **Bay 2** (D3, D4): `amount = clamp(ceil(base + rate x measure), 1, max)`, where `measure` is one of `length` (m),
  `across` (m), `area` (m2 of the largest face), `volume` (m3) or `mass` (kg of the blank). A pattern with no bay 2 makes
  the plain formed piece.
- **Bounds**: per die `across` (the blank's smaller side), `length` and `aspect` (length over across) ranges; the press's
  own limit still applies (0.45, 1.8, 7.2, 28.8 m across by class), and a die fits a press of its own class only, as today.
  A die therefore covers, by class: Small 0.10 to 0.45 m, Medium 0.45 to 1.8 m, Large 1.8 to 7.2 m, Capital 7.2 to 28.8 m
  across, cut down by its own row below.
- **Seconds**: `base + perKg x blank mass`, over the press's speed (x1, x2, x4, x8).

### 3.2 The die list

Blanks: S solid bar, P plate, H hollow box, I I-beam, C C-channel, L L-angle, T T-bar, R round, Pi pipe.
Scaling is written against the pattern's preset (section 5): `d` across, `L` length, `A` section area, `V` volume,
`S` the material's strength (its `healthPerM3` over steel's 5,000).

| Die | Blanks | Features it adds (share of the blank) | Patterns (like parts) | Bay 2 and amount | Behaviour and how it scales | Across, length; aspect |
| --- | --- | --- | --- | --- | --- | --- |
| **Bracket** (exists) | L, P, S | a web across the inner corner, 0.15 of each leg; a boss at 0.2 and 0.8 of each leg, 0.06 across | l, corner, gusset, adapter, eye, hook, hitch | none | structure: health and armour from the material row and the real volume, as an extrusion | 0.10 to 28.8; 0.10 to 8 m; 0.5 to 8 |
| **Plate** (new) | P | a rolled lip round the rim, 0.04 of the width, 1 to 10 cm; per pattern a 4 by 4 field of ribs, treads or rivets 0.03 high, or a dish 0.08 deep | hull, armour, riveted, tread, grate, deck, slope, glass, steel plate, lens | none | structure; armour = `armourPerCm` x thickness up to `maxArmour` | 0.10 to 28.8; 0.25 to 64 m; 1 to 64 |
| **Beam** (new) | S, H, I, C, T, R | an end flange at each end 0.05 of the length and 1.15 of the section; lightening pockets at 0.25, 0.5, 0.75 on a web | girder, truss, column, strut, wedge, keel, spreader, rail, ladder | none | structure | 0.10 to 28.8; 0.5 to 128 m; 2 to 64 |
| **Mould** (new) | S | the pattern's carve: its like part's boxes in the blank's unit frame (a wall's courses, a roof's pitch, a dome's steps) | wall, floor, roof (gable, hip, barrel), dome, spire, chimney, buttress, parapet, foundation | none | structure; stone, concrete, planks, steel | 0.25 to 28.8; 0.25 to 128 m; 0.25 to 16 |
| **Housing** (exists) | H, S | a flange round each open end 0.06 of the section; a lid seam at 0.9 of the height; corner bosses | housing, box frame, tub, ammo box, magazine, feed chute, ammo switch | none | structure; an ammo box's rounds x `V` | 0.15 to 28.8; 0.15 to 32 m; 0.5 to 8 |
| **Tank** (new) | Pi, H | a dished cap on each end 0.08 of the length; a filler neck at 0.5; two saddle bands | fuel tank, drum, bunker, fluid tank, fuel main, lift cell shell | none; lift cell: rubber, `2 + 4 x area` | `FuelCapacity` and a fluid tank's litres x `V` of the bore; a pipe's `Wall` sets armour | 0.20 to 28.8; 0.25 to 64 m; 0.5 to 16 |
| **Gear** (exists) | R, S | a tooth rim: 12 to 24 teeth 0.08 of the diameter; a hub boss 0.3 across; a bore 0.15 | set (Gear Set), bearing (Bearing, bronze) | none | a kit: its item | 0.10 to 0.45; 0.05 to 0.5 m; 0.2 to 4 |
| **Drive** (new) | H, S | bearing caps on the shaft faces 0.35 of the section; a split seam at 0.5 of the height; a sump 0.1 | gearbox (8 kinds), reduction, angle, T, transfer case, differential (5 kinds), axle, clutch, winch | Gear Set, `1 + 0.02 x mass` (max 20) | `MaxTorque` x `d^3 x S`; ratios, efficiency and gears are the pattern's; `WinchRating` x `d^2 x S` | 0.20 to 7.2; 0.20 to 16 m; 0.5 to 8 |
| **Shaft** (new) | R, Pi | a yoke or flange at each end 0.08 of the length and 1.4 of the diameter | drive shaft, stub axle, U joint, CV joint, wheel hub | Bearing, `2 + 0.5 x length` (max 12) | `MaxTorque` x `d^3 x S` (a pipe: less its bore's `d^3`); `MaxAngle` the pattern's | 0.10 to 1.8; 0.25 to 32 m; 1 to 64 |
| **Piston** (exists) | R | ring grooves at 0.7, 0.8, 0.9 of the length; a wrist pin boss | set (Piston Set) | bronze, `1 + 20 x volume` | a kit: its item | 0.10 to 0.45; 0.10 to 0.5 m; 0.5 to 3 |
| **Actuator** (new) | Pi, R, S | a gland collar at 0.85 of the length; a rod 0.5 of the bore out of the tip; an eye at each end | piston, linear actuator, jack, slider, shaft linear drive, motor hinge, turntable, pivot, swivel, ball joint, hinge | Servo Motor (powered), `1 + 0.01 x mass` (max 8); Bearing (free), `1 + 2 x across` | `MotorStrength` x `d^2 x S` (N) or x `d^3 x S` (N m); travel x `L`; `MotorSpeed` x `1/d`; `SideLoad` x `d^3/L` | 0.10 to 7.2; 0.10 to 32 m; 0.25 to 32 |
| **Cylinder** (exists) | Pi | an end cap each end 0.06; ports at 0.1 and 0.9; tie rods | hydraulic cylinder, engine cylinder | rubber (seals), `1 + 6 x across` | `HydArea` x bore `d^2`; force = pressure x area as today; `HydSpeed` the pattern's | 0.10 to 28.8; 0.25 to 32 m; 1 to 16 |
| **Spring** (new) | S, L, T, P | a wound or leafed middle from 0.2 to 0.8 of the length; a mount eye each end | suspension, wishbone, pogo strut, swing arm, trailing arm, leaf spring, landing strut, rigid mount | Bearing, `1 + 2 x across` | `MaxLoad` x `A x S`; travel x `L`; frequency presets the pattern's | 0.10 to 7.2; 0.25 to 16 m; 1 to 16 |
| **Button** (exists) | P, S, R | a raised cap 0.6 of the face and 0.3 of the thickness; a bezel lip 0.05 | button, mushroom button, switch, toggle, lever, knob, slider, pressure plate | copper (the wire), `1 + 2 x length` | logic input: no number scales; its use box is the cap, so a long bar is a long button | 0.10 to 1.8; 0.10 to 8 m; 0.25 to 16 |
| **Board** (exists) | P | a 6 by 4 field of pads 0.02 high; an edge connector 0.1 of the length | board (Circuit Board) | copper, `1 + 10 x area` | a kit: its item | 0.20 to 0.45; 0.20 to 1 m; 0.5 to 4 |
| **Control** (new) | P, H, S | a face plate let in 0.02; a screen or dial window 0.7 of the face (the like part's panel, in the unit frame); a cable gland | gates, timers, sensors, gauges, readouts, screens, lamps, valves, flight controls (92 families) | Circuit Board, `1 + 4 x area` (max 16) | logic: behaviour is the pattern's; a panel's text rows x its face; a lamp's reach x `sqrt(area)`; draw x `area` | 0.10 to 7.2; 0.10 to 8 m; 0.1 to 8 |
| **Cable** (new) | R, Pi | a ribbed jacket (rings every 0.1 of the length); a plug or reel flange each end | wiring loom, copper coil, lead reel, lead socket, hose reel, hose inlet, fuel line | copper (data, power) or rubber (hose), `1 + 3 x length` (the user's "cable by length") | reach of a lead or hose x `L` x 8; flow x bore `d^2` | 0.10 to 1.8; 0.10 to 16 m; 0.5 to 64 |
| **Engine** (new) | S, H, R | the like part's bolt faces and fins in the unit frame | crank section, carburettor, injector rail, turbo, supercharger, starter, exhaust, stack | Piston Set, `1 + 0.02 x mass` | **presets only at first**: a modular engine reads piece size classes (`PieceSize`), not free sizes. Open question 4 | each preset's own size |
| **Barrel** (new, gap) | R, Pi | a muzzle crown 0.03 of the length; a chamber swell over the first 0.15; brake slots or a can per pattern | gun barrel, suppressor, muzzle brake, scope (Lens, 1) | none; scope: Lens 1 | **presets only at first**: each makes the weapon builder's part of that name (`GunPartName`). Open question 4 | each preset's own size |
| **Receiver** (new, gap) | S, H | a rail on top 0.8 of the length; an ejection port at 0.5; a magazine well | receivers, sears and actions, splitters, box magazine, belt feed, shell feed | Gear Set (the action), 1 | presets only at first, as Barrel | each preset's own size |
| **Aerofoil** (new, gap) | P | none of its own: the output is the Airframe Former's piece | wing, tail plane, fin, swept, aileron, elevator, rudder, flap (`Extrusion.json` "airframes") | hinged surfaces: Bearing, `1 + 0.5 x length` | output key is `Extrusion wing-KIND CHORDx0xSPAN MATERIAL`: chord = the plate's width, span = its length. Lift from area, as `BuildWing` does today | 0.10 to 28.8 chord; 0.25 to 192 m span |
| **Rotor** (new, gap) | P (square), R | none of its own: the output is the Fan Former's piece | propeller, rotor, ducted fan (`"fanKinds"`), blade style and count as die settings | Bearing, `1 + 1 x across` | output key is `Extrusion fanK.STYLE.N 0xDx0 MATERIAL`: D = the blank's across. Thrust and power by the kind's row, as `BuildFan` | 0.40 to 28.8 m across |

Twenty-one dies: seven exist (Bracket, Housing, Gear, Piston, Cylinder, Button, Board), fourteen are new. The Aerofoil and
Rotor dies add no new part family: they give the catalogue wings and rotors a recipe (their presets) and give a press the
keys the formers already make. The Fan and Airframe Formers stay.

**Press tier.** A die exists in the classes its bounds reach (Gear, Piston, Board: Small only; Shaft, Button, Cable: Small
and Medium; the rest up to Large or Capital). The Die Lathe's rows stay as they are: a die of class C is cut from a solid
steel bar at least 0.25, 1, 4 or 8 m across.

### 3.3 The data: `Constructs/Pressing.json`, format `call-of-slop-pressing/2`

Written by the part kit (`--gen-parts press_parts`), never by hand. Sizes in cm, as keys are.

```json
{
  "format": "call-of-slop-pressing/2",
  "strength": { "of": "healthPerM3", "ref": 5000 },
  "dies": [
    {
      "die": "Drive",
      "blanks": ["hollow", "solid"],
      "materials": ["steel", "metal", "bronze"],
      "across": [20, 720], "length": [20, 1600], "aspect": [0.5, 8],
      "seconds": { "base": 3, "perKg": 0.02 },
      "features": [
        { "tag": "cap", "box": [0.325, 0.325, 0.0, 0.675, 0.675, 1.0], "proud": "z", "minCm": 1, "maxCm": 20, "look": "bare" },
        { "tag": "seam", "box": [0.0, 0.48, 0.0, 1.0, 0.52, 1.0], "proud": "xy", "look": "dark" }
      ],
      "sockets": [
        { "name": "in", "at": [0.5, 0.5, 0.0], "dir": "-z" },
        { "name": "out", "at": [0.5, 0.5, 1.0], "dir": "+z" },
        { "name": "mount", "at": [0.5, 0.0, 0.5], "dir": "-y" }
      ],
      "patterns": [
        {
          "id": "cvt", "like": "CVT", "label": "CVT",
          "preset": "solid 50x50x75 steel",
          "bay2": { "part": "Gear Set", "base": 1, "rate": 0.02, "by": "mass", "max": 20 },
          "scale": [ { "field": "maxTorque", "by": "across", "power": 3, "strength": true, "ref": 900 } ]
        }
      ]
    }
  ],
  "presets": [
    { "part": "Speed Gearbox", "die": "Drive", "pattern": "speed", "blank": "solid 50x50x75 steel" }
  ],
  "lathe": [ { "output": "Drive Die", "seconds": 30, "bays": [ { "label": "Block", "sections": ["solid"], "materials": ["steel"], "minAcross": 0.25, "maxAcross": 0.45, "use": 0.25 } ] } ]
}
```

- `features[].box` is min then max in the unit frame; `proud` names the axes on which the builder adds the z-fighting gap;
  `look` is one of the machine look's tones (blank, bare, dark, accent), never a material row number.
- `sockets[].name` must cover every socket name the like part's behaviour needs (`LeafSocket`, `Ports`, `NamedPorts`,
  `DriveInSocket`): the table refuses a pattern whose like part names a socket the die lacks. Socket class comes from the
  blank's size across, as a wheel's axle does (under 2 m small, under 7 m medium, under 24 m large, else capital).
- `scale[].ref` is needed only where the like part's own number is 0 ("no limit"), so that size matters.
- `presets` are generated: one per single-piece recipe of section 2 (section 4.4).
- Limits: at most 64 dies, 64 patterns a die, 24 features and 16 sockets a die, 4,000 presets. A bad file gives no dies and
  a reason, as today (`PressTable.Problem`).

## 4. The pressed part

### 4.1 Its key is its name

One family with extruded pieces and formed panels: a named run-time part (`IsNamedRuntime`), made from its name the first
time it is asked for, id this machine's own, never in `ConstructParts.All`.

```
Pressed Part (DIE.PATTERN PROFILE WxHxL MATERIAL)
Pressed Part (drive.cvt solid 50x50x75 steel)
Pressed Part (button.button plate 10x10x240 laminate)      a long bar-shaped button (D2)
Pressed Part (shaft.drive pipe3 20x20x400 steel)           a 3 cm wall pipe
```

| Word | What | Rule |
| --- | --- | --- |
| `DIE.PATTERN` | the die's family and the pattern's id, lower case | both must exist in Pressing.json |
| `PROFILE` | the blank's section, exactly as `Extrusion.Name` writes it: a preset id, `round`, `pipeN` | no custom mask, no wheel, fan, blade or wing |
| `WxHxL` | the blank's width, height, length in cm | on the Extruder's lattice (5 cm steps, 10 cm least section); inside the die's bounds |
| `MATERIAL` | a row of `Extrusion.json` the die lists | |

- The words after `DIE.PATTERN` are an extrusion's own name less its prefix, so one parser serves both
  (`Extrusion.ParseKey("Extrusion " + rest)`), and the key holds all a later machine needs (D9 reads sizes from it).
- A key past a bound, off the lattice, or spelt another way than a machine writes it is refused ("not a pressed part"),
  never clamped: every peer makes the same part or none. Longest key: under 80 characters (`MaxRuntimeName` is 128).
- **Bay 2 is not in the key.** Its resource and amount follow from the pattern and the blank (D4), so two peers can never
  disagree about it, and a part cannot exist "without its kit".
- **Aerofoil and Rotor** patterns have no pressed key: their output is an `Extrusion wing-...` or `Extrusion fan...` key.
- The key's `ExHash` is the name's FNV, as a loft's; `StructureHash` mixes that.

### 4.2 Its def at run time

New file `Source/Constructs/Pressed.cs` (static class `Pressed`: `Prefix`, `Parse`, `KeyOf`, `Make`, `Forget`,
`Bay2For`, `Cli`). `Make(key)`:

1. Parse the key; find the die and pattern; check bounds. Null on any failure.
2. **Preset check** (4.4): if the key equals a preset's, return the catalogue part's own def. Nothing is made.
3. `var d = like.ShallowCopy()` (as `PartMaterials.Make` does): role, logic, ports, settings, joint, panels come with it.
4. **Geometry.** The blank's boxes exactly as `Extrusion` meshes them (at most 16 for a section; a round: its drawn
   lathe shape over three hidden boxes; a pipe: 32 hidden boxes), with the blank's origin moved to its middle. Then each
   feature: its unit box times (W, H, L), thickness clamped to `minCm`..`maxCm`, pushed out by the z-fighting gap on its
   `proud` axes. On a round or pipe blank a feature is a lathe ring (a unit lathe shape, already pinned). No hulls, no JSON
   parse, `ColliderBoxes` set: at most 16 + 24 boxes drawn, at most 32 solid.
5. **Sockets.** The die's `sockets` at their unit places on the blank's bounds, class by size across; then the blank's own
   face sockets (`base`, `tip`, `+x0`...) so a pressed bar still builds as a bar. At most 38 + 16.
6. **Numbers.** Mass = the blank's mass plus the bay 2 amount's mass (a pressed part weighs what went in; today's stamped
   parts don't). Health and armour from the material row and the real volume, as `Extrusion.Stats`. Behaviour numbers by
   section 5. `PhysicsOnly = true`, `PhysicsWhy = "pressed to size"` (open question 2). `IsNamedRuntime = true`,
   `Stage`-like: never in menus.
7. `Extrusion.Adopt(d)` takes a slot of `MaxDefs`; `RuntimeParts` frees it like any named part (add `Pressed.Forget` to
   its sweep).

**Cost of the first make.** No hull, no facets, no parse: the work is a shallow copy and under 60 boxes, the same order as
a new extrusion def (about 2.5 KB, well under 0.1 ms). So a pressed part is made **whole at once**, on the server at the
stroke and on a client at the first packet naming it. The rule that guards it: `--press-part` prints the first make's
time; if any die's worst blank passes 0.3 ms on the 2-core box, that die's features move behind `HullLoft.PumpPanels`'s
pattern (the blank's boxes at once, features 2 ms an update, swapped in with `RuntimeParts.Redrawn`; nothing solid changes
because features are draw-only: step 4 keeps collision on the blank's boxes). Design for that from phase 1 by keeping
features out of the solid set. A Full naming 200 new keys is then at most 200 cheap makes; phase 1 measures 200 in one
update and reports it.

### 4.3 What it is in the world

- Physics-only: it leaves the press by the **Piece out** conveyor to a Piece Rack, or drops at the front as a body, as a
  bracket or housing does today. Racks, conveyors, the physics gun and the attach tool carry it by key already.
- It builds as its like part does (attach tool, its sockets), wires as it (ports by name), and is configured as it.
- Salvage: the Recycler gives the blank's material by its mass; the bay 2 resource is lost (as a recipe's parts are today).

### 4.4 Presets (D6): old parts stay exactly what they were

**A preset is a catalogue part named as the product of one die, one pattern and one standard blank.** The mapping runs one
way only, and no old id or name changes:

| Case | What happens |
| --- | --- |
| A blueprint, save or shipped vehicle names `Gear Set` | Nothing new. It is the file part: same id on this build, same look, same numbers. Pressed keys never enter old data |
| A press strokes the preset's exact blank with that die and pattern | `Pressed.Make` returns the **file part** (step 2). The output is `Gear Set`, an item, to the tray, 12 a slot, as today |
| Something writes the preset's pressed key (a hand-written blueprint) | `ConstructParts.Find` returns the file part; it is saved back under the catalogue name (the canonical name), as a part remade in its own material is the file's part again |
| A press strokes any other blank | A `Pressed Part (...)`: the family's sized part |
| An order or a bill names the catalogue part | `PressTable.PresetOf(part)` gives its die, pattern, blank and bay 2: the order is a press's (section 7) |

- Where presets come from: the Assembling generator. For every single-piece recipe (section 2's rule, in code) it writes a
  preset instead of an Assembler recipe: die and pattern by the part's role and name family (a table in the generator),
  **blank** = the smallest lattice blank of the die's first allowed section whose bounds hold the part's bounds, in the
  recipe's first material; bay 2 from the recipe's one part input. The preset's own numbers are the file's, so the
  recipe's old material count is dropped: the blank is the cost (D8).
- Continuity: section 5 scales every number from the preset, so a blank 5 cm longer than standard gives a part a few per
  cent stronger, not a jump. The look does jump (catalogue model at the preset, blank plus features beside it); the
  generator's `featuresFrom` option cuts a die's default features from its lead like part so the two stay close.
- A preset whose die bounds exclude its own blank is a generator error (`--gen-parts press_parts --check` fails).

## 5. Function by formula (D5)

**One law for every number:** `value = likeValue x (measure / presetMeasure)^power x (S / presetS)^(strength ? 1 : 0)`,
where the preset's measure comes from the pattern's `preset` blank and `S = healthPerM3 / 5000`. So the preset has the
file's numbers exactly (D6), there is no table and no class (D5), and data states only `field`, `by`, `power`. A field
whose like value is 0 ("no limit") takes the row's `ref` as its like value. Results are rounded to three significant
figures so every peer computes the same float from the same integers.

| Behaviour | Field (ConstructPartDef) | Law | Read today in |
| --- | --- | --- | --- |
| Gear, gearbox, differential, transfer | `MaxTorque` | `d^3 x S` (a shaft in torsion) | `ConstructDrive.cs`, `ConstructDriveParts.cs` |
| | `DriveRatio`, `DiffRatio`, `TransEfficiency`, gear settings | the pattern's, unscaled | same |
| Shaft, U and CV joint | `MaxTorque` | `d^3 x S`; a pipe `(d^4 - bore^4) / d` | `ConstructDrive.cs` |
| Piston, linear actuator, jack | `MotorStrength` (N) | `d^2 x S` (bore area) | `Construct.cs`, `ConstructWorld.cs`, `ConstructConfig.cs` |
| | travel (`JointMin/Max`) | `L` | `ConstructPhysics.cs` |
| | `MotorSpeed` | `d^-1` | same |
| | `SideLoad` | `d^3 / L` | `Construct.cs`, `ConstructPhysics.cs` |
| Motor hinge, turntable, pivot | `MotorStrength` (N m) | `d^3 x S` | same; `ConstructFlex.cs` for flex joints (`FlexStiffness` x `d^4 / L`) |
| Bearing, free pivot, hinge | `JointGive`, `OverloadDamage` | unscaled; rating by `MotorStrength` law above | `Construct.cs` |
| Hydraulic cylinder | `HydArea` | bore `d^2` | `ConstructHydraulics.cs` |
| | `HydForce` (electric head) | `d^2 x S` | `ConstructHydraulics.cs`, `ConstructEditsConnectors.cs` |
| Suspension, strut, spring | `MaxLoad` (kg) | `A x S` | `ConstructDrive.cs`, `ConstructPhysics.cs`, `ConstructTracks.cs` |
| | travel | `L` | `ConstructPhysics.cs` |
| Bracket, plate, beam, housing | `Health`, `Armour`, `Mass` | material row x real volume and thinnest wall, as an extrusion (`Extrusion.Stats`) | every damage and physics path, unchanged |
| Tank | `FuelCapacity`, fluid litres | `V` of the bore or the inner box | `ConstructFluid.cs`, `ConstructBoost.cs`, `ConstructFuelBlasts.cs` |
| Ammo box | `BoxRounds`, `CookRadius` | `V`, `V^(1/3)` | `ConstructAmmo.cs` |
| Winch, hitch | `WinchRating` | `d^2 x S` | `ConstructLinks.cs`, `ConstructSystems.cs` |
| Button, switch, lever, gate, sensor | none | size changes its use box and look only | `LogicBlocks.cs`, `LogicInputs.cs` |
| Screen, gauge, sign | panel rows and columns | face size over the preset's face (the `PanelDef` rect scales with the unit frame) | `DisplayPanel.cs` |
| Lamp | reach | `sqrt(area)` | `ConstructParts` `Lamps`, the renderer |
| Lead, hose reel | reach | `8 x L` | `ConstructLinks.cs` |
| Wing, control surface | `Surface` | existing: `Extrusion.BuildWing` from chord, span, airfoil | `ConstructFlightParts.cs` |
| Rotor, fan | `Rotor` | existing: `Extrusion.BuildFan` from diameter and the kind's row | `ConstructFlightParts.cs`, `ConstructRotor.cs` |
| Engine piece, gun part | none at first | presets only (open question 4) | `ConstructEngine.cs`, `ConstructWeaponParts.cs` |

Sanity bounds: every scaled field is clamped to its part-file range (`ConstructParts.Range` values), and the die's bounds
keep the ratio within about 1/64 to 64 of the preset. `--press-part` prints each scaled number beside the preset's.

## 6. The Assembler after the rework (D7, D9)

- **It loses** every single-piece recipe (1,519 rows become presets). **It keeps** multi-part things (380 rows) and gains
  *piece recipes*: recipes whose inputs are sized pieces and whose result is sized by them.
- **Pieces in.** A sized piece is physics-only, so the Assembler gets what the Crafting Terminal has: an implicit
  **Piece in** conveyor port at its hopper and an input hold that is a Piece Rack's (`storage.pieces`, packed by volume,
  drawn, F on a stack takes it back). A loose piece let go in the hopper's mouth is drawn in, as a press bay does today.
  Item parts and bulk keep the mixed input.

**Recipe shape** (`call-of-slop-assembling/2`; a v1 row with only `materials` and `parts` is still valid, so the 380 rows
need no rewrite on the day):

```json
{
  "output": "Wheel Hub", "seconds": { "base": 4, "perKg": 0.05 }, "minTier": 1,
  "pieces": [
    { "label": "Disc", "kind": "pressed:plate.disc", "count": 1, "across": [20, 400] },
    { "label": "Ring", "kind": "pressed:shaft.hub", "count": 1 },
    { "label": "Studs", "kind": "extrusion:round", "count": 5, "across": [10, 20] }
  ],
  "parts": { "Bearing": { "base": 1, "rate": 2, "by": "Disc.across" } },
  "fit": [
    { "a": "Ring.across", "b": "Disc.across", "ratio": 1.0, "tolerance": 0.10 },
    { "a": "Studs.length", "b": "Disc.height", "ratio": 2.0, "tolerance": 0.25 },
    { "same": "material", "of": ["Disc", "Ring"] }
  ],
  "size": { "lead": "Disc", "scale": [1.0, 1.0, 1.6] }
}
```

- `kind`: `pressed:DIE.PATTERN`, `extrusion:PROFILE`, `panel:KIND`, or `part:NAME` (a catalogue or preset part, by count).
- **Tolerance (D9).** The first piece listed is the **lead**: it is accepted on its own ranges and sets the scale. Every
  `fit` is then checked as `abs(a / (b x ratio) - 1) <= tolerance` (default 0.10). The Assembler takes a piece into its
  hold only if it can serve the recipe with the lead it holds; otherwise the piece waits on the conveyor or stays in the
  hand and the reason is shown on the look line, the screen and the line console's row.
- **The result's size.** Its key is `Assembled Part (RECIPE WxHxL MATERIAL)`, where W, H, L are the lead's dims times
  `size.scale`, rounded to the 5 cm lattice, and MATERIAL the lead's. Its def: the output part's own boxes and sockets
  scaled by (W, H, L) over its file bounds (an assembly's look is its model; non-uniform stretch is bounded by the lead's
  ranges), z-fighting gaps recomputed for the new patch sizes by `PartZFight.Resolve` (so this def **is** sliced: boxes and
  sockets at once, the corrected draw boxes behind the pump, since `Resolve` over a 200-box machine is not cheap). Numbers
  by section 5's law with the output part as its own preset. When the dims equal the file's bounds the result **is** the
  catalogue part (D6 again).
- **Bay-2-style amounts** in `parts` and `materials` may be a number (as today) or `{base, rate, by}` on a piece's measure.

**Refusal texts** (ASCII, one reason each; `AsmRefuse` byte in `CStore.Blocked` as the press's `BayRefuse`):

| When | Text |
| --- | --- |
| A piece no recipe line takes | `not a piece of this recipe: the Wheel Hub takes a Disc, a Ring and 5 Studs` |
| Lead out of range | `the Disc is 4.20 m across: the Wheel Hub takes 0.20 to 4.00 m` |
| Fit, too big | `the Ring is 0.80 m across: with this 0.60 m Disc it must be 0.54 to 0.66 m` |
| Fit, too small | `the Ring is 0.40 m across: with this 0.60 m Disc it must be 0.54 to 0.66 m` |
| Material | `the Ring is bronze: it must be steel, as the Disc is` |
| No lead yet | `feed the Disc first: it sets the size` |
| Result too large for the machine | `too large for this Assembler: 3.2 x 3.2 x 1.1 m, it builds up to 2 m (a Workshop Assembler: 4 m)` |
| Hold full | `its piece hold is full` |
| Recipe left the Assembler | `the Gear Set is pressed now: a Gear Die and a steel round 10x10x15` |

The last line is what an Assembler set to a removed recipe, and an order for a preset part, say.

## 7. Orders, bills, the terminal, the picker, the building station

| System | Today | Change |
| --- | --- | --- |
| `WorkOrder` | kind 0 piece key, 1 catalogue part, 2 name (`NamedShape` 255) | A pressed part is kind 2 by its name (`Pressed Part (...)`), no new field. A **preset** stays kind 1 (the catalogue part): `WorkOrders.Refusal` routes it by `PressTable.PresetOf`. `Assembled Part (...)` is kind 2, an Assembler's |
| `WorkOrders.TakesOrders` | Extruders, Wheel Formers, Assemblers | plus `IsPress` (not the lathe). A press with a queue works orders only, as every machine |
| A press's order | none | Accepted when its fitted die is the key's die and of this class, and the blank is inside the press's across. Refusals: `the Press holds a Button Die: this order needs a Drive Die`, `a Medium Press's order: the blank is 0.80 m across`, `not a pressed part`. Head order: the die's Pattern setting is set by the order; bay 1 takes only that blank (`wants solid 50x50x75 steel for its order`); Starved names bay 2's shortfall (`needs 3 Gear Set in bay 2, holds 1`) |
| Line Console bill, `Send` | pieces to Extruders, parts to Assemblers, panels to Panel Formers | pressed lines and preset parts to presses, round robin as the rest. A kept line names a press's reason |
| `ConstructBill.FromBlueprint` | `Pieces`, `Panels`, `Parts`, links | New list `Pressed` (key, count): every `Pressed Part (` step, and every catalogue part with a preset. For each it also adds **the blank** to `Pieces` (an Extruder's order) and the bay 2 amount to a `Kits` tally, so a bill is complete down to stock. Aerofoil and Rotor presets add to `Pressed` too: the catalogue wings and rotors stop being "no recipe" |
| `--bill NAME` | groups: extruded, formed, catalogue by category, links | New group "Pressed": part or key, die and pattern, press class, blank, bay 2 (`3 Gear Set`), seconds. Catalogue lines say `pressed (Drive Die)` where they said the Assembler's recipe. "no recipe" should fall to the multi-part things with no piece recipe and to hull lofts with no former |
| Blueprint bill line | `piece`, `panel`, `part` | `{"step": n, "pressed": "Pressed Part (drive.cvt solid 50x50x75 steel)", "count": 2}` in `ConstructBill.ParseLine/FromLine` |
| Crafting Terminal list | stock, Assembler recipes, four sections per Extruder, first wheel | per linked press: its fitted die's presets (`now` when the blank is in linked stores or a linked Extruder can make it: the one-level chain rule), and "to size" rows are not listed (the bill makes those). Also fixes its not-yet item: named run-time parts in linked stores are listed by name ("in stock") |
| Recipe picker | refiners and Assemblers; "none" for an Extruder | On a press: one tile per **pattern** of the fitted die (the like part's picture, the pattern's label, `any size`), then one per preset of that die (the catalogue part's picture, its blank and bay 2 as icons, seconds). A click sets the die's Pattern (`CActKind.LineRecipe` already carries a setting's value). No die: `fit a die first` |
| Tile pictures | `SandboxTiles` by part def; resource icons | a pressed key's tile is drawn from its run-time def (as a part's is), cached by name, dropped when `RuntimeParts` frees the def |
| Press bay cards (`FillBay`, `FillDie`) | wants, holds, refusal | **Bay 1**: `Blank: solid 50x50x75 steel` and `makes: CVT 0.5 x 0.5 x 0.75 m` (or the preset's name) before the stroke. **Bay 2** (D4): `Gear Set: needs 3 for this blank, holds 1`. **Die card**: Pattern, with Up and Down |
| Building station (not built yet) | will issue a blueprint's bill | no change of plan: it calls `ConstructBill.ToConsole`, which now also deals pressed lines. `ConstructReserve` claims a store, a kind, a count: kinds go through `ConstructStorage.ReadDef`/its writer, which already carry run-time parts by key |
| Production lines | `ProductionLines.PressRaw` | Starved also covers "bay 2 short"; the console's detail line shows the head order |

## 8. Net and saves

| What | On the wire | A late joiner | A save |
| --- | --- | --- | --- |
| A pressed or assembled part on a body, in a rack, on a conveyor | `Extrusion.WireMark`, then the named kind (245, `HullLoft.WireKind`), then the name as text. `HullLoft.Read` and `ConstructParts.Find` learn the two new prefixes; no new kind byte | the Full names it; the client makes it from the name (4.2) | the name, as every named run-time part (length-prefixed) |
| A preset's product | the catalogue part's id, as today | nothing new | the catalogue name |
| The blank in bay 1 | a part of the press's construct on its bay socket, as today (`Growing` key) | in the Full | as today |
| Bay 2's stock | the store's mixed input (flags bit 64) and part kinds (bit 16), the Assembler's own bytes, now sent for a press | in the store's state after the Full | the store's input block, written for a press as for an Assembler. An older save has none: bay 2 loads empty |
| The die's Pattern | a part setting (`CfgKey` new at the end) | in the Full's settings | by setting key |
| Stall and refusal | `PressStall` gains `Bay2Short`, `WantsOrder`; `BayRefuse` gains `Bounds` (outside the die's range), `Pattern` (the pattern doesn't take this section); both ride `Refused` and `Blocked` as today | state bytes | not saved |
| Orders | kind 2 by name, already on the wire (protocol 253) | `CBodyKind.Orders` | by name |
| Assembler piece hold (phase 6) | a rack's kinds on an Assembler's store | store state | the rack block, written for an Assembler |

- **Protocol 258** in phase 1 (a peer on 255 would throw `MalformedPacketException` on a `Pressed Part (` name, and new die
  part files move part ids). Later phases bump again only where the table says bytes change: phase 3 (a press's input
  bytes, the Pattern setting), phase 6 (the Assembler's hold). Phases 2, 4 and 5 change data and part ids: one bump each
  is the safe reading of the rule; the merger renumbers.
- **Old saves.** A world saved with a piece in a press's bay 2 or 3: bay 2 is a hopper now, so the piece is set loose in
  front of the machine at load and the world's log says `left 1 piece out of the Press's bay 2` (bay 3 stays a piece bay:
  the next blank waits there). An Assembler whose `recipe` names a removed row loads with no recipe and shows section 6's
  last refusal. **Phase 5 must first check how a choice setting is saved** (by name or by index): if by index, removing
  1,519 rows shifts every saved Assembler's recipe, and the phase adds a by-name migration before it removes a row.
- **Hashes.** `StructureHash` mixes `ExHash` for run-time parts already.
- **Mods.** A mod's Pressing.json or Assembling.json in the old formats is refused with a reason (`--check-mods` says which
  format it wants); docs/mods/MODS.md gets the new shapes in phase 5.

## 9. The check command

`--press-part DIE[.PATTERN] BLANK [BAY2]` (in `Pressed.Cli`, dispatched beside `--form-panel` in `Program.cs`; output to
`callofslop.log` next to the DLL; exit code = problems found).

```
--press-part drive.cvt "solid 50x50x75 steel"
--press-part button "plate 10x10x240 laminate" copper=4
--press-part "Pressed Part (drive.cvt solid 50x50x75 steel)"
--press-part list                      every die: patterns, blanks, bounds, classes, preset count
--press-part presets [DIE]             every preset: part, key, bay 2; checks each resolves to its catalogue part
--press-part sweep DIE [--samples N]   N seeded blanks across the die's bounds (default 24): sampled, never every size
```

It prints: the key; whether it is a preset (and of which part); the press class that takes it and the die class; boxes
drawn and solid, sockets; mass, health, armour; bay 2's resource, the amount needed and, when `BAY2` is given, whether
that is enough; each scaled number beside the preset's (`maxTorque 7200 N m (preset 900 at 0.50 m)`); seconds on each
press class; a hash of its geometry; the first make's time and bytes; that a second make from the name gives the same hash
(a client's path); that no two drawn boxes share a face plane (`PartZFight`'s check on the one def). Refusals print the
same text the bay card shows. Phase 6 adds `--assemble-part RECIPE PIECE...` on the same lines (fit checks, the result's key).

## 10. Phases

Each phase: its own branch and worktree, WIP commits with `[skip ci]`, the final push without. No self-test sections are
added (tests only when asked); each phase is tried by hand as listed and reports what it did not verify. Docs
(`production.md` "The Press" and "Assembling", PARTKIT.md) are updated once, at the end of phases 3 and 6.

| # | Title | Work | Files | Stop point | Try by hand | Still works |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | **The pressed part and `--press-part`** | `Pressed.cs`: key parse and write, `Make` (blank boxes, features, sockets, numbers by law), `Forget`. Pressing.json **/2 read beside /1**: `dies`, `patterns`, `presets` sections are optional and the 33 old `rows` stay in force, so the machine is untouched. Generator writes three dies' data only: Bracket, Housing, Plate (structure, no bay 2). Doors: `ConstructParts.Find`, `HullLoft.Read` prefix, `RuntimeParts` sweep. Protocol 258 | new `Source/Constructs/Pressed.cs`; `ConstructPressTable.cs`, `ConstructParts.cs` (Find), `HullLoft.cs` (Read), `RuntimeParts.cs`, `Program.cs`, `PartKit/Generators/PressParts.cs`, `Pressing.json`, `Net/ServerBrowser.cs` | `--press-part list`, `sweep bracket`, `sweep housing`, `sweep plate` print clean; a blueprint step naming a pressed key builds | `--press-part` as section 9; `--window-shot /tmp/p1.png --blueprint` of a scratch blueprint with three pressed parts and `--net host` plus `join` to see the same names on a client; a creative save and load holding them | Everything: the Press strokes its 33 rows as before. **Shippable alone**: sized parts exist for blueprints, generators and creative; nothing a player had changes |
| 2 | **One piece, one part** | The stroke for a die with v2 data: bay 1's whole piece goes, the output is `Pressed.Make(pattern, blank)` or its preset's catalogue part; `BayRefuse.Bounds`; bay 1 card (`makes: ...`); presets for the old rows of those dies (Bracket's four parts, Housing's 21) so they still come out by name; output by Piece out or as a body. Dies without v2 data keep the old row path | `ConstructPress.cs` (`RowFor`, `StateOf`, `Stroke`, `Refusal`, `FillBay`, `Look`), `ConstructPressTable.cs`, `PressParts.cs` | A Bracket, a Housing and a Plate die each press a standard blank to the catalogue part and an odd blank to a sized part, in a window | `--window-shot` on Production Showcase: `--do "near Press#1 3; panel Press#1"` with an L-angle fed; one with a 1.2 m L-angle on a Medium Press; host and client agree on the rack's kinds | Button, Board, Gear, Piston, Cylinder on old rows. Changed on purpose: a long strip in a Bracket die is one long bracket, not twenty (D8) |
| 3 | **Bay 2 and the working dies** | Bay 2 becomes a resource hopper on the Assembler's input code (chute and F); amount by formula on its card; `PressStall.Bay2Short`; the Pattern setting on dies; the scale law for behaviour fields; v2 data for Gear, Piston, Button, Board, Cylinder and new Drive, Shaft, Actuator, Spring, Cable, Control, Tank, Beam; their Die parts and lathe rows; old rows deleted with `/1` support; old-save bay 2 piece set loose. Protocol bump | `ConstructPress.cs`, `Pressed.cs`, `ConstructPressTable.cs`, `ConstructAssembling.cs` (input helpers made shared), `PressParts.cs`, `CfgKey`, world save of a press's input (`Worlds/WorldSaveBodies.cs`), `ServerBrowser.cs`, docs | A long bar pressed with the Button die is one long working button; a Drive die's part carries more torque when bigger (`--press-part` numbers, and one driven by hand) | Shots: bay 2 card before a stroke with too little, then enough; the long button wired to a Lamp and pressed (`touch`); `--press-part sweep` for every die | The 33 old outputs, now presets. If the phase runs long, stop after the hopper and Button, Gear, Drive: the other dies are data and go to a phase 3b |
| 4 | **Orders, bills, terminal, picker** | Section 7: a press takes orders; console Send; `Bill.Pressed` with blanks and kits; `--bill` group; blueprint `pressed` bill lines; Crafting Terminal rows and named parts in stock; picker tiles for patterns and presets; line state detail | `WorkOrders.cs`, `ConstructPress.cs` (queue head), `ConstructCrafting.cs`, `UI/UiRecipePicker.cs`, `App/Program.SandboxRecipe.cs`, `ProductionLines.cs`, `Render/` tiles | A console bill with a blank line and a pressed line is dealt to an Extruder and a Press and both run to the rack | `--bill "Production Showcase"`; `--window-shot` with `click` on the console's Recipe and Send; a terminal order for a preset part, host and client | Orders for pieces, panels and Assembler parts unchanged |
| 5 | **Every single-piece recipe leaves the Assembler** | The sort rule in `Generators/Assembling.cs`: single-piece rows become `presets` (die and pattern by role and family table, blank by bounds); remaining dies' data (Mould, Engine, Barrel, Receiver, Aerofoil, Rotor) and their Die parts; `featuresFrom`; the removed-recipe refusal; the choice-setting save check and migration (section 8); SurvivalStart gets a Press, a lathe and starter dies it lacks; `--press-part presets` clean; mods doc | `PartKit/Generators/Assembling.cs`, `PressParts.cs`, `Assembling.json`, `Pressing.json`, `SurvivalStart.json`, `ConstructAssembling.cs`, `ConstructAssemblingTable.cs`, docs/mods | `--gen-parts assembling --check` and `press_parts --check` clean; Assembling.json holds about 380 rows; `--bill` of the ten vehicles of production.md shows no "no recipe" for wings, rotors or gun parts | `--bill` for the ten vehicles before and after (to files; compare counts); a survival start checked by hand for a path from ore to a first Gear Set; an old save with an Assembler on a removed recipe | Old blueprints and shipped vehicles: no file changes (catalogue names). This phase is mostly data: if the family table is not done in 90 minutes, families without a row keep their Assembler recipe and are listed in the report |
| 6 | **The Assembler takes pieces** | Section 6: Assembling.json /2 `pieces`, `fit`, `size`; Piece in and the piece hold; tolerance refusals; `Assembled Part (...)` key and scaled def behind the pump; `--assemble-part`; three first piece recipes (Wheel Hub, Piston, Gearbox) as the worked examples. Protocol bump | `ConstructAssembling.cs`, `ConstructAssemblingTable.cs`, new `Source/Constructs/Assembled.cs`, `ConstructConveyor.cs` (Piece in on an Assembler), `Generators/Assembling.cs`, `MachineTiers` (the hold), `ServerBrowser.cs`, docs | A bigger disc and ring give a bigger hub; a ring 30 % too big is refused with its text; both on a client | Shots of the hold with the lead only, the refusal line, the finished hub on the rack; `--assemble-part` for the three | The 380 v1 rows run as before |

Order matters: 2 needs 1; 3 needs 2; 4 and 5 need 3 and touch different files except `ConstructPress.cs` (run 4 before
5); 6 needs 5's generator. `ConstructPress.cs` is edited in phases 2, 3 and 4: never two of them at once.

## 11. Risks and open questions

**Risks (no decision needed, but the builder must mind them):**

- **Scale of phase 5.** 595 name families need a die and pattern each. The generator's family table is the long pole;
  the fallback (unmapped families keep their Assembler recipe) keeps the game whole but leaves D7 partly done.
- **Sockets on free sizes.** Catalogue parts sit on a class lattice (0.25, 1, 4, 16 m cells); a pressed part's sockets land
  wherever the blank's proportions put them. They snap by class, and freeform and point attach reach the rest, as with
  extrusions, but lattice-perfect builds from odd-sized parts are not promised.
- **Look next to a preset.** A catalogue Gearbox and a gearbox pressed 5 cm longer will not look alike unless that die's
  features were cut from the catalogue model (`featuresFrom`). Worth one picture review when the user asks for it.
- **Survival bootstrap.** Tier 1 machines must still be reachable from a start kit once Gear Sets and the like are pressed:
  the start needs a Press, an Extruder and the starter dies (phase 5 checks one path by hand).
- **`minTier` is lost for pressed parts.** A recipe's tier gate has no equal on a press. This design leaves a die's
  material and class (what its lathe needs) as the gate.
- **Run-time table.** A factory of free sizes makes many keys; 48,000 slots with `RuntimeParts`' sweep is ample, but a
  press fed random lengths makes a new def per piece. The Extruder's Cut every keeps real lines to a few keys.
- **How choice settings save** (section 8) was not verified in this design phase.

**Open questions for the user** (one at a time; the recommended option first):

1. **How does a die choose between the things its family makes (a Drive die: gearbox, CVT, differential...)?**
   - A (recommended): a **Pattern setting on the die**; one die a family, 21 dies in all.
   - B: one die per thing (several hundred dies to cut, rack and swap).
   - C: a Pattern setting on the press instead of the die (the die only gates the family).
2. **A pressed part of a non-standard size cannot go in an inventory, a shelf or a tray today (run-time parts are
   physics-only). What should small ones do?**
   - A (recommended): stay physics-only: they ride conveyors to Piece Racks, are carried with the physics gun and fitted
     with the attach tool; standard sizes (presets) stay items.
   - B: make slots, inventories and chutes carry parts by key (a large change to stores, the inventory, the wire and
     saves: a project of its own, after this one).
   - C: let the press make only preset sizes for item-sized families (buttons, kits), free sizes for the rest.
3. **Building pieces (walls, roofs, domes, chimneys: about 590 rows in `Parts/Building` and `Parts/Capital`) are single
   pieces by the rule. Where do they go?**
   - A (recommended): a **Mould die** on the Press (stone, concrete, planks, steel blocks), as section 3.
   - B: they stay Assembler recipes; only mechanical and metal parts move to dies.
   - C: a separate machine later (a caster); until then they stay on the Assembler.
4. **Gun parts and modular engine pieces get dies now, which closes their no-recipe gap. Should size change them?**
   - A (recommended): **presets only** for now: each die makes the standard parts; sized barrels and cylinders wait for
     their own design (the weapon builder and the engine code read fixed part kinds and size classes).
   - B: sized from the start (a longer barrel shoots further, a bigger cylinder gives more power): needs changes in the
     weapon builder and `ConstructEngine` first.
5. **Machines, doors, seats, wheels and engines (226 rows) are assemblies, but most have bulk-only recipes today. Until
   each gets a piece recipe (D9), what do they cost?**
   - A (recommended): keep today's bulk recipe on the Assembler, and convert family by family after phase 6.
   - B: no recipe until a piece recipe exists (they come from the start kit and wrecks only).
6. **Wings and rotors already come off the Airframe and Fan Formers. Should the Press make them too?**
   - A (recommended): yes, through the Aerofoil and Rotor dies, which give the same parts and give the catalogue wings
     and rotors their recipe; the formers stay for growing big ones in place.
   - B: no dies; instead the formers learn to take orders and the catalogue wings map to former presets.

## 12. User answers to section 11 (2026-11-12 08:50; these override the text above)

1. **Pattern is a setting on the PRESS**, not on the die (option C). The die only unlocks the family; the press's panel picks the pattern. Phase 3 puts the Pattern setting on the press.
2. **Inventories carry odd-sized parts too** (option B): slots, inventories, shelves, trays and chutes hold run-time parts by key. A project of its own AFTER the Press rework (stores, inventory, wire, saves). Until it lands, odd sizes are physical pieces.
3. **No Mould die.** Building pieces (about 590 rows) keep their Assembler recipes for now. The user: "A lot of these pieces' roles will be replaced by pieces made on the extruder and the building stations. My intent is extruded pieces to be the majority of pieces a user is building with." So phase 5's sort leaves `Parts/Building` and `Parts/Capital` rows on the Assembler; replacing them with extruded pieces is later work.
4. **Gun parts and engine pieces are sized from the start** (option B): a longer barrel shoots further, a bigger cylinder gives more power. Needs the weapon builder and `ConstructEngine` to read size by formula first: a new phase (design, then build) before the Barrel, Receiver and Engine dies; those dies leave phase 5.
5. **Assemblies keep today's bulk recipe** until converted family by family after phase 6.
6. **No Aerofoil or Rotor dies.** The Airframe Former and Fan Former learn to take work orders, and catalogue wings and rotors become former presets. A new phase replaces those two dies.

Die list is now 18 at most: the 21 less Mould, Aerofoil, Rotor; Engine, Barrel, Receiver wait for the sized gun and engine phase.
