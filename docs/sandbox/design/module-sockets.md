# Module sockets: a standard for ship modules (design)

The user, 2026-11-19: "Create a system for standard size sockets that will be used to help create custom modules for ships.
Sockets should have a few different size classes, such as 1 meter square, 1 by 2, 2 by 2, 2 by 3, 1 by 3, 3 by 3. These
different sockets will be used to attach modules and submodules that the player creates or the systems generate. [...] a
ship would have 2 engine mounts, and there would be engine modules that could be attached to those. Or a ship needs a
reactor, and there is a reactor socket, which will allow for the swapping out and placing of different reactors so long as
it has the correct socket. This concept can also extend to turret pods. Basically, this is creating a universal framework
for ships to be customized with different pieces and modules that can change their look, function, behavior, and
abilities." Modules are made by players or generated, and are sold and bartered.

The user, the same day: "Before you start building systems, give me a design document that covers what we're building and
how it will be implemented. Also, I want the ability for these sockets to have openings in the ship, flush mount options,
as well as extended proud systems."

**Status: design only. Nothing is built until the user approves this document.** Part A says what is built, part B how.
Section 13 lists the choices for the user; the text assumes each recommended default.

# Part A: what we are building

## 1. The idea in seven rules

1. **A socket is a part.** A *Module Socket* is a plate of one standard footprint (1x1 to 3x3 m) that a player or a
   generator places on a ship. It says what kind of module it takes, how the module is mounted (proud, flush or in an
   opening), how much room the module has, and what it carries across (power, data, fuel, coolant, ammunition, pieces).
2. **A module is a blueprint with a plug.** A module is an ordinary player blueprint (or a shipped or generated one) whose
   base is a *Module Plug* of the same footprint, saved with a `"module"` block: kind, size, the mounting styles it suits,
   its stats. It is a sub-blueprint (docs/sandbox/design/sub-blueprints.md), so ids, versions, Live / Lock, packs and the
   Building Station's job tree come with it.
3. **Three mounting styles.** *Proud*: the plate sits on the hull and the module stands out of it (engine pods, turret
   pods). *Flush*: the plate sits at the bottom of a recess and the module's outer face is level with the hull (a hatch-like
   sensor, a cargo bay door, a buried reactor's lid). *Opening*: the plate sits at the bottom of a well cut through the hull
   and the module stands in it, below and above the surface as it needs (a turret well, a reactor bay).
4. **Openings are real.** The hull over a flush or opening socket really has a hole, made of the Laser Cutter's cut parts
   (`HullLoft.Cuts.cs`: openings that are drawn, weighed and collide as holes), so players, shots and bodies pass into a
   well. A **Module Well** liner lines the hole's walls. No surface is merely hidden over a solid.
5. **Fitting is a weld, not a joint.** Plug meets plate face to face on the Rot24 grid; the module's body is merged into
   the ship's with `ConstructWorld.Merge`, as the sub-blueprint big fit and the tool dock do. Once fitted it is ordinary
   parts: no extra body, no per-tick cost. The plate is the module's only contact, so taking it off is cutting one link and
   splitting the body.
6. **Couplings are a fixed pinout per size.** Each footprint carries the same couplings in the same places, so any module
   of that size and kind fits any socket of that size and kind, and its pipes and data lines meet without the player laying
   a link.
7. **Data first.** Sizes, kinds, styles, couplings and ratings are one table (`Constructs/ModuleSockets.json`); plates,
   plugs, bench and crates are the part kit's output; wells are run-time parts made from a key.

## 2. The socket standard

### 2.1 Sizes

| Size | Face (m) | Bolts | Turns about the normal | Couplings (2.5) | Load rating (proposed) |
| --- | --- | --- | --- | --- | --- |
| 1x1 | 1 x 1 | 4 | 4 | structure, power, data x2, pipe | 2 t, 200 kN |
| 1x2 | 1 x 2 | 6 | 2 | + data x4 | 4 t, 500 kN |
| 1x3 | 1 x 3 | 8 | 2 | + pipe x2 | 6 t, 800 kN |
| 2x2 | 2 x 2 | 8 | 4 | + coolant, ammo, chute | 12 t, 2 MN |
| 2x3 | 2 x 3 | 10 | 2 | + data x8, pipe x2 | 20 t, 4 MN |
| 3x3 | 3 x 3 | 12 | 4 | + coolant x2, chute x2 | 40 t, 8 MN |

- The metre lattice lands on both the Small class's 0.25 m cells and the Medium class's 1 m cells (parts-and-data.md "Size
  classes"). The plate is Medium class; its back also carries a Small-class grid of sockets (as an adapter pad does), so it
  bolts onto Small and Medium hulls alike.
- A rectangle has 2 turns (0 and 180 degrees), a square 4. A socket may be **keyed** (a setting): exactly one turn fits,
  marked by a notched corner on the plate and on the plug. Engine sockets come keyed, so a nozzle can only face aft.
- **Bolts**: a ring of bolt heads at a 0.5 m pitch round the rim and the key notch: the look that says "standard socket",
  the same on every faction's ship. Detail boxes only.
- **Larger ships**: a 4 m series (4x4 to 12x12, Large class) is a later row of the table with the same rules (question 7).

### 2.2 Kinds

`engine`, `reactor`, `turret`, `utility`, `cargo`, `generic`:

| Kind | Extra rule |
| --- | --- |
| engine | The plate is a **mount** for ship works (`"worksPiece": {"kind": "mount"}`, ship-works.md 3.1): a drive module over 500 kN is satisfied by works inside the module (the nacelle rule, ship-works.md rule 4) or by works bolted behind the plate inside the ship. Keyed by default |
| reactor | The look line shows the circuit's balance with and without the module; the hot-swap rule (5.4) checks the circuit can do without it |
| turret | Its room includes the swept volume of whatever turns on the module (3.3); the ammo coupling feeds a Turret Ring's `Ammo in` (ship-works.md 5.2) |
| utility, cargo | Nothing extra; cargo carries the chute couplings by default |
| generic | Takes any module of its size |

**Fit rule (question 1):** a socket takes modules of its own kind or `generic` ones; a `generic` socket takes any module of
its size. The kind is a choice setting on the plate (configure tool), so it gets a `ConstructConfig.DefaultType` entry
(constructs.md, lesson of 2026-11-11).

### 2.3 Mounting styles

A socket has one **style** at a time, chosen when it is placed and changeable with the configure tool while nothing is
fitted. A module declares which styles it suits. The measures, along the plate's normal, from the **hull surface** (the
outer face of the hull part the socket was placed on):

| Style | Where the plate sits | The socket's room | What the module declares |
| --- | --- | --- | --- |
| **Proud** | on the surface | a box over the footprint, `above` metres tall (default twice the short side), plus `side` clearance (default 5 cm) | its height over the plug face; it must fit `above` |
| **Flush** | at the bottom of a well `depth` deep | the well only: nothing may stand above the surface | its **face**: the height of its outer face over the plug face. It fits when face = depth (to the 1 cm grid); its outer face then stands 3 mm proud of the surface (z-fighting rule) |
| **Opening** | at the bottom of a well `depth` deep | the well, plus a box `above` metres over the surface (0 allowed: a closed bay) | how far below and above the surface it stands when seated at that depth: it fits when its bounds lie inside the well and the box above |

- **Depth** is 0.25 to 6 m in 0.25 m steps (3 m wells only for 2x2 and up, proposed); **above** 0 to 12 m in 0.5 m steps.
- Which side the module is on: a well opens to the outside of the hull it is cut through. A socket placed on an inner
  deck works the same way, from the deck's top (a reactor in a floor well of the engine room).
- **A flush module is an opening module with a face.** The same module can suit both: a turret pod whose ring deck is at
  face 0.6 m fits a 0.6 m flush well (ring level with the hull) or any deeper opening (sunk lower, more armour round it).
- **Proud modules can also go in shallow wells:** a proud-only module is refused in a well, so a player who wants a pod
  half sunk declares the module as opening-capable (the save window offers each style the module's bounds allow).

### 2.4 The opening in the hull

For flush and opening styles the hull must have a hole over the footprint. It is made of real parts:

- **The Module Well** is the liner: four walls from the plate up to the surface plus 3 mm, inset 5 mm from the hole's edge,
  a rim cap on the outer edge in the plate's hazard colour, and the plate at its foot. It is a run-time part made from a key,
  as cut and extruded parts are: `Module Well (2x2 d150 steel)` (size, depth in cm, material). The plate and the well are
  placed together as one placement (the well's foot carries the plate's socket); its walls are real solid boxes, so a well
  is a pressure-tight, shootable box in the ship.
- **The hole** in each hull part the well passes through is a **cut part**: a formed panel or a flat plate with an opening
  (`Cut Panel (... | rU,V,A,B,C)`, `Cut Plate (...)`, laser-cutter.md), its opening the footprint plus 2 cm a side. Cut
  parts already open their collision round an opening of 10 cm or more (`HullLoft.CutOpenCm`), so shots, players and bodies
  pass, and they get an `o` socket on the opening's edge. A footprint of up to 3 m is within `CutMaxCm` (600).
- **Making it**: the socket's look line lists what is in the well's column ("in the way: the Hull Plate (step 88), the
  Frame Girder (step 90)"). The configure panel's **Cut opening** button plans it: each formed panel or flat plate in the
  way gets its cut key; a part that can't be cut (a girder, a machine, a lofted part with no flat sheet) is listed for the
  player to move. In Creative the button replaces the panels at once. In Survival it queues Laser Cutter work orders for the
  cut parts (`WorkOrders.Queue`, production.md "The Laser Cutter") and marks the old ones for the remover; the player or a
  Building Station swaps them. Generators lay the cut keys directly.
- **Seams and skins**: decoration that a hull part draws up to 4 cm over its surface (seams, studs, skins) at the well's rim
  is dropped inside the rim by the same render cut a flush shell pod uses (`Renderer.Cuts.cs`, branch
  `claude/flush-pod-pocket`: boxes of the holding body marked, the box shader dropping them inside the opening). That is
  render only and covers only the 4 cm band; the hole itself is the cut part's.
- **Z-fighting** (`.claude/rules/z-fighting.md`): the well's walls stand 5 mm inside the hole's edges; the rim cap is 3 mm
  proud of the surface; the plate's face is 3 mm under the plug's; a flush module's outer face 3 mm proud of the surface;
  the plug's flange overhangs nothing. No two drawn faces share a plane.

### 2.5 Couplings (the pinout)

A plate and a plug each declare the size's couplings as **named ports** (ConstructPorts.cs; power-and-links.md "Named
link ports") at fixed places: on the plate's inner face (ship side) and on the plug's inner face (module side). In a well
the ship-side ports are on the well's outside walls near its foot, so the ship's links reach them inside the hull. When
the plug is welded on, each pair is joined at the rebuild:

| Coupling | How it crosses | Notes |
| --- | --- | --- |
| Structure | the plug-plate socket link | what holds the module on |
| Power | through the frame, as today (power-and-links.md "Power": the frame is the circuit) | nothing new: a welded module is on the ship's circuit |
| Data | a pass-through: Data k on the plate and Data k on the plug act as one wire end, both ways | thrusters need no wire (they answer the flight control as any thruster on the body does); data is for what the module's maker wired: a turret's seat link, a reactor's switch |
| Pipe, coolant | a pass-through node: a pipe into the plate's Fuel A continues into whatever leaves the plug's Fuel A | coolant is `noEffect`, as every coolant port is today |
| Ammo | the ship's feed into the plate continues into the module's ring feed | turret kind |
| Chute | a chute junction across the face | 2x2 and larger |
| Shaft | not carried | a drive shaft never crosses a joint; ground-vehicle engine modules are a later question |

Pass-throughs are derived at the rebuild from the plug-plate link, as ship works are (ship-works.md 3.2): nothing per tick,
nothing new on the wire. An unused coupling is left open; a fluid mismatch gets today's warning
(`ConstructRoutes.FluidWarning`).

## 3. Fitting checks: clearance and collision

Three checks, each with a reason on the look line and the numbers:

### 3.1 The socket's own column (when placed, at every rebuild)

The column is the footprint plus the side clearance, from the plate up to the surface plus 4 cm (flush, opening) or to
`above` (proud, opening). Solid boxes of the ship's other parts inside it are listed ("in the way"); the plate's lamp shows
amber and no module can be fitted until it is clear. The well, the plate and the cut parts' remaining sheet (outside their
openings) are not in the way. The column is kept per socket at the rebuild, so a part built into it later is seen.

### 3.2 The module against its room (at the fit, and when a module is saved)

- Its solid boxes, turned by the chosen turn and seated at the style's place, lie inside the room (2.3). A flush module's
  solids stop at its face (detail boxes may stand 4 mm over, so the face's bolts show).
- Then the build's own overlap test against the ship (`ConstructBuild.Blocked`, the first blocker named through
  `BlockerOf`), the plate excepted: the room is the reservation, the overlap test is the truth.
- Mass, peak thrust and a gun's recoil within the size's load rating.

### 3.3 Moving parts (turret kind, and any module with joints)

At the fit, each joint of the module is swept through its range in 15 degree steps (a turntable by its full turn, a hinge
by its limits), the parts on its leaf posed at each step and tested against the ship's solids (`Blocked` on the posed
boxes; 24 x 13 poses at most, off the game thread for big modules). A clash refuses the fit in Survival and warns in
Creative: "the Cannon hits the Mast (step 412) at 140 degrees, elevation 20". A turret socket's room is the swept circle's
cylinder, so a well must be wide enough for the turning part or the ring must sit above the surface.

### 3.4 Players, shots and bodies

Nothing special: the hole is a real hole (2.4), the well's walls and the module's parts are solid parts of the ship. A
player can climb into a turret well, a shot down a well hits the module, a fitted module never collides with its own ship
(parts of one body don't).

## 4. Modules

### 4.1 Making one

The player builds the module in the world from a Plug on a **Module Bench** (a plate on legs, any size by setting, that
holds the plug square while building; a bench may also be set to a well depth, to try a flush or opening module in a real
well). Selecting it and pressing Comma opens the save window, which gains **Save as module** when the selection has exactly
one plug at its base: the plug is step 0, the module's frame is the plug's. The window shows the styles the bounds allow
and the stats, and names it.

- **Stats** are worked out, never typed: a flat build in a scratch world (as the broken check is, sub-blueprints.md 2):
  mass, thrust and burn (with the works cap), power made and drawn, guns and rounds a minute, storage, bounds, face. Kept
  with the broken check's cache in `ConstructBlueprints/.checks/`.
- **Name and version**: the blueprint's. A module fitted to a ship is what was built; a newer version changes ships only
  through blueprints that use it live (sub-blueprint rules).
- **Look**: a thumbnail drawn by the `--preview-shot` path (3 s, no window) at save, for the Modules tab and markets. The
  plate and plug keep the standard look; everything else is the maker's.

### 4.2 Submodules

A module may carry sockets of its own (a turret pod with a 1x1 sensor socket; a reactor with two coolant pump sockets).
Fitted modules are uses (`Construct.Uses`, `CPart.Use`), so a module in a module is a use in a use; limits are the
sub-blueprint ones (8 deep, 128 a blueprint, 1,024 a tree). **Socket adapters** are modules too: a 2x2 plug with a 1x2 or
1x1 plate on top, so a small module fits a big socket (question 2).

## 5. Attaching, swapping, detaching

### 5.1 Ways to fit

1. **Creative**: the Modules tab, aimed at a free socket, filters to what fits; the ghost stands on it at the first turn
   that fits, R takes the next allowed turn, LMB fits it at once (built from nothing, as a stamp).
2. **By hand (Survival)**: a built module is a loose body. Carried (physics gun, a Loader Arm, a crane, a vehicle), it is
   fitted with Socket attach (building-and-tools.md): the plug's face is the source, the plate the target; the snap allows
   only the socket's turns and seats it at the style's depth; LMB starts a fit of 5 s plus 1 s a tonne, at most 60 s (the
   big fit's time). The tool dock (`ConstructEditsDock.cs`) is today's 8-part case of this.
3. **Building Station**: a ship built from a blueprint with modules builds them as sub-jobs and fits them with the big fit,
   unchanged. A new **Refit** job on a station whose bay holds a ship: pick a socket and a module (a crate in the hold, a
   body in the bay, or "build it here"); its lifters take the old module to the bay floor and fit the new one (the Shell
   Gantry's swap, mining-shell.md, at ship scale).
4. **A station berth (later)**: a faction station refits a moored ship for credits.

The fit is exact, never physical: the module is set at the socket's pose (Rot24 turn, quantised place).

### 5.2 What happens at the fit

One server action in one tick: `Merge` with the turn, a socket link plug to plate, the parts tagged as a use of the
module's id and version, the plate's hidden `Fitted` setting set to that use. At the next rebuild the couplings join, the
works flood sees an engine plate's mount, the module's guns, thrusters and stores are the ship's own. One fit a tick a
world (`FitTick`).

### 5.3 Detaching and damage

- **Taking off** (attach tool's cut free on the plug, a Refit, a Creative remove): the plug-plate link is cut and the use's
  parts split into their own body (`ConstructWorld.SplitPieces`), with their links, wires and fuel; pass-throughs vanish at
  the next rebuild. The module is frozen in place for a second, then loose; out of a well it is lifted out along the normal
  by the tool or lifter, never pushed through the well's walls.
- **Damage**: a plug or plate at zero health breaks off (`PartBreak.Detach`), so the module falls away as a wreck with its
  tags (salvage it, mend it, fit it again). A broken part inside is mended as any part. A well's walls can be shot through
  like any wall.

### 5.4 Hot swap (question 5)

A module can be taken off or fitted only while idle: no thrust, no gun firing or reloading, nobody seated in it, a
reactor's output not needed by the circuit, and the ship under 2 m/s against what it is near. Otherwise the look line says
which.

### 5.5 Modules as items: crates

A **Module Crate** per size stores one built module: a station or F on the crate takes the module in and keeps its id,
version and every part's health in hidden settings, as a Shell Pod keeps its shell (shell-pod.md). A crate is a part, so
it rides cargo volumes, docking clamps and lifters, sells at markets and is fitted from a station's hold; unpacking at a
socket costs the fit time. Blueprints never carry a crate's contents.

## 6. Ships declare sockets

- **Players** take Module Socket from the inventory (Fittings > Modules), choose size, style, depth and above on the tool
  wheel's outer ring before placing (as the linking tool's options), then kind and keying with the configure tool.
- **Generators**: the Vehicle Kit's `BlueprintScript` gains `Socket(...)` and `Module(...)` (section 10); ship generators
  put keyed engine sockets aft (proud for nacelle pods, opening for a hull drive's bell), a reactor opening in the engine
  room with its well reserved, turret openings or proud pods along the spine, then fill each from the faction's module set,
  so one hull generator gives many ships.

## 7. The economy

- **Value**: a module's bill, every part's recipe materials (Assembling.json, the Press, Former and Cutter keys) at the
  station's prices for those materials (`Crew/Stations.json`). A station buys at bill x 0.7 and sells at x 1.3 (proposed),
  only to players its power's standing allows (below 0: no trade, as now).
- **Markets**: a station's card gains a Modules list (its power's generated modules as crates); BUY and SELL for crates
  aboard a moored ship. A sold player module joins that station's stock under its maker's name.
- **Contracts** (faction controllers' phase 5): "deliver a 2x2 reactor of at least 3 MW to Glacier Watch, 4,000 credits",
  checked against the crate's stats.
- **Barter** between players is physical: hand crates over. A trade window is not part of this design.
- **Sharing files**: Pack (`<name>.pack.json`) shares a module with its submodules. In Survival a file is a design; only a
  crate is goods. **Mods** add modules as blueprints under `Constructs/Blueprints/` (docs/mods/MODS.md).

## 8. What exists and how it maps

| Existing | Relation | Do |
| --- | --- | --- |
| Sub-blueprints (`SubBlueprints.cs`, `ConstructStationTree.cs`, `ConstructClipUses.cs`) | A module is a sub-blueprint with a plug; fitted modules are uses | Reuse: ids, versions, Live / Lock, packs, flatten, big fit, `USES` saves |
| Socket attach (`SocketWeld`, `SocketWeldFlow`), Rot24, `Merge` | The fit path | Add the size, turn and seat-depth rule to the snap |
| Cut parts (`HullLoft.Cuts.cs`, the Laser Cutter) | The hull's opening | Reuse as is; add the planner that picks cut keys |
| Flush shell pod pocket (`Renderer.Cuts.cs`, branch `claude/flush-pod-pocket`) | Hides the seams band at a well's rim | Reuse once that branch merges; wells share its budget |
| Tool dock (`ConstructEditsDock.cs`) | A tiny socket | Leave it |
| Size class adapters | Precedent for socket adapters | Same idea, as modules |
| Ship works, Drive Pod (ship-works.md) | Engine plate = mount; a Drive Pod on a plug = engine module | Plate flagged as mount |
| Turret Ring, Vehicle Kit `Turret()` | Turret pod = ring + gun + Turret Module on a plug | Ammo coupling to `Ammo in` |
| Reactors and gensets (`Constructs/Parts/BigDrive/Plants`) | Reactor modules | Generated modules round them |
| Shell frames, slots, Shell Gantry | Same vocabulary, but shells are worn bodies with a kit byte array | Leave; don't migrate |
| Modular engines (`ModularEngine.json`) | Shafts don't cross | Leave |
| Docking clamps, `"carry"` (Shuttle, Limpet) | Carried craft fly off; a module never flies | Leave; crates ride them |
| Shipped ships (Zero Day, Magpie, Dray, rust bucket) | Built before sockets | Leave (question 10); one showcase ship |

# Part B: how it will be implemented

## 9. Data formats

### 9.1 The table: `Constructs/ModuleSockets.json`

```jsonc
{
  "format": "call-of-slop-module-sockets/1",
  // A size's place in the list is its byte on the wire and in saves: a new size goes at the end.
  "sizes": [
    { "name": "1x1", "w": 1, "l": 1, "turns": 4, "mass": 2000, "thrust": 200000, "recoil": 20000,
      "couplings": ["power", "data1", "data2", "pipeA"] },
    { "name": "2x2", "w": 2, "l": 2, "turns": 4, "mass": 12000, "thrust": 2000000, "recoil": 120000,
      "couplings": ["power", "data1", "data2", "data3", "data4", "pipeA", "pipeB", "coolantA", "ammo", "chuteA"] }
    // ... 1x2, 1x3, 2x3, 3x3
  ],
  "kinds": ["generic", "engine", "reactor", "turret", "utility", "cargo"], // order is the wire's
  "styles": ["proud", "flush", "opening"],                                 // order is the wire's
  // Where each coupling sits on a face, in the footprint's own 0..1 square, and what port it is.
  "couplings": {
    "data1":   { "type": "data",  "at": [0.15, 0.15] },
    "pipeA":   { "type": "pipe",  "at": [0.85, 0.15], "fluid": "fuel" },
    "coolantA":{ "type": "pipe",  "at": [0.85, 0.50], "fluid": "coolant" },
    "ammo":    { "type": "ammo",  "at": [0.50, 0.85] },
    "chuteA":  { "type": "chute", "at": [0.50, 0.50] }
  },
  "depth": { "min": 0.25, "max": 6, "step": 0.25 }, "above": { "max": 12, "step": 0.5 }, "side": 0.05,
  "fit": { "seconds": 5, "perTonne": 1, "most": 60 }, "swap": { "speed": 2 },
  "price": { "buy": 0.7, "sell": 1.3 }
}
```

Loaded and refused with a reason, as `ShipWorks.json` is (`Source/Constructs/ModuleSocketTable.cs`).

### 9.2 Part blocks

```jsonc
// Module Socket 2x2 (part kit output). Its settings: kind, style, depth, above, keyed; hidden: Fitted (use index).
"moduleSocket": { "size": "2x2" }
// Module Plug 2x2
"modulePlug": { "size": "2x2" }
// Module Crate 2x2
"moduleCrate": { "size": "2x2" }
```

Both new keys go into the known-key list in `ConstructParts.cs`. The plate's face socket and the plug's face socket are
`SocketKind.Module` (new, at the end of the enum), and `CSocket` gains a `Size` byte (0: none; else 1 + the size's index)
beside `Class`, so a plug snaps only to a plate of its size. `SnapBuild.Build` classifies the new family in the same change
(constructs.md lesson of 2026-11-10). The ports come from the table: the generator writes `"ports"` from `"couplings"`.

### 9.3 The well's key

`Module Well (2x2 d150 steel)`: size, depth in cm, material. A run-time def made from the key, as `Extrusion` makes its
pieces (ids from `Extrusion.RuntimeBase`, read through `CPart.D` / `ConstructParts.Def`, keyed by its key on the wire and in
hashes: constructs.md's first lesson). Its boxes: four walls, the rim cap, the foot carrying the plate's back sockets; its
ship-side coupling ports on the outer walls near the foot. Blueprints name it by key, as they name cut parts.

### 9.4 A module blueprint

```jsonc
{
  "name": "Torch Pod Mk2", "id": "4be1c9a0d27f3e65", "version": 3,
  "module": {
    "kind": "engine", "size": "2x2",
    "styles": { "proud": { "height": 4.2 }, "opening": { "below": 1.5, "above": 2.7 } }, // flush: "face": m
    "maker": "Twitch",
    "stats": { "mass": 8400, "thrust": 1800000, "burn": 3.6, "made": 0, "draw": 120,
               "guns": 0, "store": 0, "bounds": [2.0, 4.2, 2.0], "bill": { "steel": 3100, "copper": 240 } }
  },
  "steps": [ { "part": "Module Plug 2x2", "at": [0, 0, 0] }, "..." ]
}
```

`styles` and `stats` are written by the save window from the flat build, never edited by hand (a hand edit is redone at the
next check and logged when it differs).

### 9.5 A ship's socket in a blueprint

Ordinary steps: the well (by key) and the plate on it, the plate's settings in its step as every part's are
(`"cfg": {"kind": "engine", "style": "opening", "depth": 1.5, "above": 3, "keyed": true}`), cut parts round it by key,
and a fitted module as a `"uses"` entry whose `"on"` is `[plate step, "face", 0, "face"]` (the sub-blueprint format,
unchanged).

## 10. Code

### 10.1 New files

| File | What it holds |
| --- | --- |
| `Source/Constructs/ModuleSocketTable.cs` | The table: sizes, kinds, styles, couplings, limits; load and refusals |
| `Source/Constructs/ModuleSocketDef.cs` | Parsing `moduleSocket`, `modulePlug`, `moduleCrate`; the defs' fields |
| `Source/Constructs/ModuleWell.cs` | The run-time well def from its key; `Column(c, plate)` (3.1); `InTheWay`; `PlanCuts` (which parts become which cut keys, or why not) |
| `Source/Constructs/ConstructModuleSockets.cs` | Partial `Construct`: `CModSocket[]` built at the rebuild after ship works (plate part, size, kind, style, depth, above, fitted use, column state, joined couplings); the pass-through tables read by logic, routes and ammo |
| `Source/Constructs/ModuleFit.cs` | `FitWhy(c, plate, module, turn)` (room, overlap, rating, sweep: section 3), `Seat` (the pose for a style), `Fit` (the merge and tags), `TakeOff` (the split) |
| `Source/Constructs/ModuleSweep.cs` | The joint sweep of 3.3, sliced and runnable off the game thread |
| `Source/Constructs/ModuleBlueprints.cs` | The `"module"` block read and written; `ModuleStats.Of(flat)`; the thumbnail request |
| `Source/Constructs/ModuleCrates.cs` | Crate settings: put in, take out, value |
| `Source/Constructs/ConstructEditsModules.cs` | Server edits: place socket (plate and well), set style, cut opening, fit, take off, crate; reach, who may, Survival timers |
| `Source/Game/GameSandboxModules.cs` | The server side of `CActKind.Module`, `FitTick`, the hot-swap rule, Refit job glue |
| `Source/App/Program.SandboxModules.cs` | The client: the socket's placing options, the module ghost on a socket, the look lines |
| `Source/UI/UiSandboxModules.cs` | The Modules tab (filters, thumbnails, stats lines) and the socket's configure panel additions (Cut opening, the in-the-way list) |
| `Source/Render/ModuleSocketRender.cs` | The room's wire box (green free, amber blocked, red clash), the sweep clash marker, the plate's lamp |
| `Source/PartKit/Generators/ModuleSocketParts.cs` | Part kit `module_sockets`: 6 plates, 6 plugs, 6 crates, Module Bench, socket adapters (as shipped module blueprints) |
| `Source/VehicleKit/ModuleGen.cs` | `--gen-module KIND SIZE [--style S] [--seed N]` and the faction module sets |

### 10.2 Existing code that changes

- `ConstructParts.cs`: `SocketKind.Module`, `CSocket.Size`, known keys; `ConstructConfig.DefaultType` for kind and style.
- `SnapBuild.cs`, `SocketWeld.cs` / `SocketWeldFlow.cs`: a plug onto a plate takes only the allowed turns and is seated at
  the style's depth; a module body is pulled like any piece (`PullReach`).
- `ConstructLogic` (rebuild): data pass-through pairs as wire ends; `ConstructRoutes` (rebuild): pipe and chute
  pass-through nodes; `ConstructAmmo.FedFrom`: a ring fed through a plate; `ConstructWorks`: the plate as a mount.
- `ConstructClip.ToBlueprint`, `ConstructClipUses.cs`, `PlayerBlueprints`, the save window (`UiSandboxBlueprints.cs`): Save
  as module, the styles offered, the stats.
- `SubBlueprints.cs`: a use may be a module (its plug-plate join), resolved and flattened as any use.
- `ConstructStation.cs`, `ConstructStationTree.cs`: the Refit job (`StationOp.Refit`, `StationOp.RefitCancel` at the end).
- `ConstructBuild.Blocked` callers: the plate excepted for its own module; the column check.
- `Renderer.Cuts.cs` (after `claude/flush-pod-pocket` merges): wells add their rim band to the openings list; the budget
  (16 within 60 m) is shared, wells after pods, nearest first, and raised only if measured.
- `Interiors/InteriorPlanner.cs`: a socket's column inside the hull is reserved volume.
- Markets (`FactionOp.StationBuy` / `StationSell`, `Crew/Stations.json`): a module case and a `"modules"` list.
- `Assembling.cs` `LateHand`: the new recipes at the end (saved recipe numbers keep).
- `Categories.json`: Fittings > Modules.

### 10.3 Wire and saves

- **Protocol**: one bump (the number after 371 at merge time) with a history line. `CActKind.Module` at the end of the
  enum: `u16 body, u16 plate part, u8 op (fit, take off, cut opening, crate in, crate out), u16 module body or u8 crate
  slot, u8 turn`. The plate's settings are part settings (no new bytes). A fitted module rides `CBodyKind.Uses` (exists).
  The Refit job rides the Station word. A well is a run-time def sent by key, as an extrusion's is.
- **Saves**: the `USES` block (exists), part settings, the crate's hidden settings, well keys. Module blueprints, thumbnails
  and checks live in `ConstructBlueprints` and its subfolders, already in `SavePaths.SaveFolders`: no new save file or list
  entry. Older saves and blueprints have no sockets and load unchanged.

### 10.4 Performance and limits

- A fitted module is plain parts: nothing per tick beyond its parts' own cost.
- The rebuild: one pass over the sockets (at most 64 a construct): column test (the build's box query), coupling pairs,
  mount flag. No allocation per tick; the socket array is resized only at the rebuild.
- A fit is one merge (2.5 ms measured for a 2,605-part use warm, sub-blueprints-notes.md), a take-off one split, each at
  most one a tick a world. Sweeps, stats and thumbnails run off the game thread; a sweep result is cached by (module id,
  version, socket pose, ship's structure stamp).
- Limits (proposed): 64 sockets a construct, depth 6 m, above 12 m, 2,000 parts a module; the use limits as
  sub-blueprints. Every refusal gives the numbers; every dropped coupling or part logs a line.

## 11. Tools, commands and checks

- `--check-modules [DIR]`: every module blueprint built flat; its styles, stats, room and rating checked; to
  `callofslop.log` with REFUSED lines (a loop over every module samples when there are many, per the tests rule).
- `--check-sockets BLUEPRINT`: each socket of a ship: style, column state, what is in the way, which shipped modules fit,
  each fit's sweep result.
- `--gen-parts module_sockets [--check]`, `--gen-module ...`, `--preview-shot` for module thumbnails.
- Window tool verbs for trying it by hand: `modsock place SIZE STYLE`, `modsock cut`, `modsock fit NAME [turn]`,
  `modsock off`, `modsock state`.
- **No self-test section** is added (the user's rule). Each phase is tried by hand, host and joined client, and the report
  says what was not tried.

## 12. Phases (one builder, the whole feature; WIP commits `[skip ci]`)

1. **The standard** (large): the table, `module_sockets` parts, `SocketKind.Module` and `Size`, proud style end to end:
   placing, the column, the room box, Creative fit and take-off, the split. Tried with a hand-built 2x2 module on a test
   hull, host and client.
2. **Wells and openings** (large): the well def and key, flush and opening styles, seat depth, Cut opening (planner,
   Creative replace, Survival work orders), the rim band render cut, the sweep check. Tried: a turret in a 1 m opening that
   turns full circle, a flush sensor, a reactor in a closed floor well, players climbing into the well, shots down it.
3. **Modules** (medium): the `"module"` block, Save as module, styles from bounds, stats, thumbnails, the Modules tab,
   coupling pass-throughs (data, pipe, ammo, chute), ratings.
4. **Survival** (medium): the timed fit by hand, hot swap, damage and break-off, crates, the Refit job.
5. **Generators** (large): `BlueprintScript.Socket` / `Module`, `--gen-module`, Engine Kit modules, faction module sets, a
   showcase ship (two keyed proud engine sockets, a reactor opening, a turret opening and a proud turret pod) whose modules
   swap.
6. **Economy** (medium): values, market lists, BUY and SELL of crates, a contract kind.
7. **Later**: the 4 m series, berth refits, migrating a shipped ship if wanted.

## 13. Open questions for the user

1. **Does the kind limit what fits?** Recommended: a socket takes its own kind or `generic` modules, and a `generic` socket
   takes anything of its size. Else: kinds are labels only, and size alone decides.
2. **A small module in a big socket?** Recommended: sizes match exactly; a smaller module goes in through a socket adapter
   (itself a module). Else: any module fits a socket at least its size, centred.
3. **How an opening is made in Survival.** Recommended: real cut parts from the Laser Cutter, ordered by Cut opening and
   swapped in by the player or a station. Else: the socket cuts the hull itself when placed (instant, no cutter needed),
   which skips the production loop.
4. **Physics.** Recommended: welded into the ship's body (no joint, no cost). Else: held by a joint that can break under
   load, so a crash can tear modules off.
5. **Hot swap.** Recommended: only while the module is idle and the ship is under 2 m/s. Else: any time, or only at a
   station.
6. **Fitting in Survival.** Recommended: by hand with the attach tool and a fit time, and by stations. Else: stations
   only, so a refit needs a yard.
7. **Bigger series.** Recommended: 1 to 3 m now, a 4 m series (4x4 to 12x12) for capital ships later. Else: both now.
8. **How modules are traded.** Recommended: as Module Crates. Else: only as whole bodies carried in holds and clamps.
9. **Market prices.** Recommended: the bill at material prices, bought at x0.7 and sold at x1.3, by standing. Else: a
   maker's own asking price for player modules sold through a station.
10. **Existing ships.** Recommended: leave the shipped ships as they are and add one showcase ship built on sockets. Else:
    rework the Zero Day's PDC rings and drives into sockets and modules.
