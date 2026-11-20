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

**Status: design only, second draft, rewritten to the user's answers of 2026-11-19 (kept at the end; they win where any
text differs). Nothing is built until the user approves it.** Part A says what is built, part B how. Section 18 holds the
few questions still open; the text assumes each recommended default.

# Part A: what we are building

## 1. The idea in eight rules

1. **A socket is a part.** A *Module Socket* is a plate of a whole-metre footprint (1x1 to 3x3 m built now, the formats
   and machinery planned to about 30x30 m) placed on a ship by a player or a generator. It says which module kinds it
   takes (any, or one restricted kind), how the module is mounted (proud, flush or in an opening) and how much room it has.
2. **A module is a physical thing with a plug.** It is whatever parts are welded onto a *Module Plug*: a player can build
   one freely in the world, welding on any pieces they like, even while it sits fitted in a socket (answer 11), and its
   tags, stats and look come live from those parts. It may also be saved as a reusable design (a blueprint with a
   `"module"` block), which makes it a sub-blueprint (docs/sandbox/design/sub-blueprints.md), so ids, versions, Live /
   Lock, packs and the Building Station's job tree come with it. There are no crates or abstract items: a module on a ship,
   in a hold, on a rack or lying on the ground is the same body with the same look.
3. **Anything with a plug is a module.** There is no rule that a module must do something sensible: a reactor with turrets
   on it and nothing else is allowed. What a module is (its tags) is read from its parts, so a restricted socket can't be
   fooled by a label.
4. **Three mounting styles.** *Proud*: the plate sits on the hull and the module stands out (engine and turret pods).
   *Flush*: the plate sits at the foot of a well and the module's outer face ends level with the hull. *Opening*: the module
   stands in a well cut through the hull, below the surface and above it as it needs (a turret well, a reactor bay).
5. **An opening socket cuts the hull itself.** Placed in flush or opening style, it burns its well through the hull parts
   under it, taking time and power; the parts it cuts become cut parts with a real hole (`HullLoft.Cuts.cs`), so players,
   shots and bodies pass into the well. Large sockets are cut by a fitting arm's cutter.
6. **Welded, never jointed.** A fit merges the module's body into the ship's with `ConstructWorld.Merge`, as the
   sub-blueprint big fit does. Once fitted it is ordinary parts: no extra body, no per-tick cost. The plate is the module's
   only contact, so taking it off is one cut link and a split.
7. **Small by hand, large by machinery.** A small module is fitted with the physics gun or F at its socket; a large one
   needs **Module Fitters**, big arms with a gripper, a welding torch and a cutting head, which carry it, cut the well and
   weld it on where everyone can see. Swaps are allowed any time, in flight too, wherever the module can be reached.
8. **Data first.** Sizes, kinds, styles, couplings, ratings, fitters and faction preferences are data
   (`Constructs/ModuleSockets.json`, the faction files); plates, plugs and fitters are the part kit's output; wells and
   large plates are run-time parts made from a key.

## 2. The socket standard

### 2.1 Sizes

Sockets and modules grow in **whole metres**: a footprint is W x L with 1 <= W <= L <= 30. **Built now**: 1x1, 1x2, 1x3,
2x2, 2x3, 3x3. Every number that depends on size comes from a rule in the table, not a list, so a 6x10 socket later is a
key, not new code.

| Rule | Value (proposed) | 1x1 | 2x2 | 3x3 | 10x10 | 30x30 |
| --- | --- | --- | --- | --- | --- | --- |
| Turns about the normal | 4 if square, else 2 | 4 | 4 | 4 | 4 | 4 |
| Mass rating | 3 t a m2 | 3 t | 12 t | 27 t | 300 t | 2,700 t |
| Thrust or recoil rating | 250 kN a m2 | 250 kN | 1 MN | 2.25 MN | 25 MN | 225 MN |
| Bolts | every 0.5 m round the rim (every 2 m from 8 m) | 8 | 16 | 24 | 20 | 60 |
| Lifting lugs (2.6) | 4 corners (+ every 6 m of edge) | 4 | 4 | 4 | 12 | 20 |
| Couplings (2.5) | by area, then repeated along the edges | 5 | 10 | 14 | 30 | 60 |
| Fitted by | hand up to 2x2 and 5 t, else a fitter (section 7) | hand | hand | S fitter | M or L fitter | Capital fitter |

- The metre lattice lands on the Small class's 0.25 m cells and the Medium class's 1 m cells (parts-and-data.md "Size
  classes"). A plate up to 3x3 is Medium class with a Small-class grid of sockets on its back (as an adapter pad has), so
  it bolts onto Small and Medium hulls; a bigger plate puts its back sockets on a 1 m pitch up to 15 m and a 2 m pitch past
  that, so a 30x30 plate has 225 (a part has at most 255, `ConstructPartDef.MaxSockets`).
- A socket may be **keyed** (a setting): one turn only, marked by a notched corner on the plate and the plug. Engine sockets
  come keyed, so a nozzle can only face aft.
- **Bolts** are detail boxes: the look that says "standard socket", the same on every faction's ship.

### 2.2 Kinds

A socket is **generic** by default: it takes any module of its size. A socket may instead be **restricted** to one kind:

| Restricted kind | Takes | Extra rule |
| --- | --- | --- |
| engine | modules tagged `engine` only (a life support module can't go in one) | the plate is a ship-works mount (`"worksPiece": {"kind": "mount"}`, ship-works.md 3.1), so a drive over 500 kN is satisfied by works in the module (the nacelle rule) or bolted behind the plate inside the ship. Keyed by default |
| reactor | modules tagged `reactor` only | the look line shows the circuit's balance with and without the module |
| turret | modules tagged `turret` only | the room includes the turning parts' sweep (3.3); its ammo coupling feeds a Turret Ring's `Ammo in` (ship-works.md 5.2) |

Reactors fit generic sockets too, so a ship can carry a second reactor wherever it has a generic socket of the size; the
same holds for every kind. A module's **primary tag** decides what a restricted socket sees (4.2).

**Turret bays.** A turret bay is a module whose top carries turret sockets of its own: fitted to a generic (or opening)
socket of the ship, it gives the ship turret sockets, and turret modules attach to those. A bay can hold its ring wells,
magazines and feeds inside it, so a ship gains armed positions by fitting bays rather than cutting turret wells everywhere.
This is the submodule case (4.4).

### 2.3 Mounting styles

A socket has one **style**, chosen when it is placed and changeable while nothing is fitted (going from proud to a well
cuts; going back leaves the hole, which the player patches or uses). Measures are along the plate's normal from the **hull
surface** (the outer face of the part the socket was placed on):

| Style | Where the plate sits | The socket's room | What the module declares |
| --- | --- | --- | --- |
| **Proud** | on the surface | a box over the footprint, `above` metres tall (default twice the short side), plus `side` clearance (5 cm) | its height over the plug face |
| **Flush** | at the foot of a well `depth` deep | the well only: nothing stands above the surface | its **face**: its outer face's height over the plug face. It fits when face = depth (1 cm grid); the face then stands 3 mm proud of the surface (z-fighting rule) |
| **Opening** | at the foot of a well `depth` deep | the well, plus a box `above` metres over the surface (0: a closed bay) | how far below and above the surface it stands when seated at that depth |

- **Depth** 0.25 m upward in 0.25 m steps, at most the short side x 2 (6 m for a 3x3; planned to 30 m); **above** 0 to
  4 x the long side in 0.5 m steps.
- A well opens to the side of the surface the socket was placed on: a socket on an inner deck's top makes a floor well (a
  reactor in the engine room's floor), one on the outer hull a well open to space.
- **A flush module is an opening module with a face**; a module may suit several styles (a turret pod whose ring deck is at
  0.6 m fits a 0.6 m flush well, any deeper opening, or proud). The save window offers every style its bounds allow.

### 2.4 The opening: how the hull is cut and drawn

- **The Module Well** is the liner: four walls from the plate to the surface plus 3 mm, a rim cap on the outer edge in
  hazard paint, and the plate at its foot. It is a run-time part made from a key, as cut and extruded parts are:
  `Module Well (2x2 d150 steel)` (size, depth in cm, material). Its walls are solid, so a well is a real shootable box in
  the ship.
- **The cut.** When an opening or flush socket is placed, the server plans the cut: every hull part whose solid enters the
  well's column is either a formed panel or flat plate, which is replaced by its **cut part** with an opening the footprint
  plus 2 cm a side (`Cut Panel (... | rU,V,A,B,C)`, `Cut Plate (...)`, laser-cutter.md; openings of 10 cm and more open
  their collision, `HullLoft.CutOpenCm`, and get an `o` socket on the edge), or something that can't be cut (a girder, a
  machine, a tank), in which case placing is refused with the list ("in the way: the Frame Girder (step 90)"). A part cut
  twice keeps both openings (`HullLoft.MaxCuts`, 6). Lofted hull parts are question 1 in section 18.
- **Cost.** The plate's own **cutting ring** (a ring of heads round its rim) burns the outline: 2 s a metre of outline for
  every 10 cm of hull it passes (proposed), drawing 4 kW a metre of footprint side from the ship's circuit (a 2x2 cut
  through 30 cm of plate: 8 m x 3 x 2 s = 48 s at 8 kW). No power: it waits, "the cut needs 8 kW". In Creative it is
  instant. In Survival the cut-out pieces fall away as loose salvage (their material is what the cut parts lost). A socket
  over 3x3 has no ring: a fitter's cutter makes its cut (section 7).
- **Seen.** While cutting, a white-hot kerf line runs along the outline from the ring's heads with sparks, and stays orange
  for 10 s after. When it finishes the panels change to their cut parts in the same tick and the slug drops.
- **Seams and skins** a hull part draws up to 4 cm over its surface at the rim are dropped inside the rim by the render cut
  a flush shell pod uses (`Renderer.Cuts.cs`, branch `claude/flush-pod-pocket`); render only, the hole itself is the cut
  part's.
- **Z-fighting** (`.claude/rules/z-fighting.md`): the well's walls stand 5 mm inside the hole's edge; the rim cap is 3 mm
  proud of the surface; the plate's face 3 mm under the plug's; a flush module's face 3 mm proud of the surface.

### 2.5 Couplings (the pinout)

A plate and a plug each carry the size's couplings as **named ports** (ConstructPorts.cs; power-and-links.md "Named link
ports") at fixed places: on the plate's inner face (in a well, the well's outer walls near its foot), and on the plug's
inner face. When the plug is welded on, each pair joins at the rebuild:

| Coupling | How it crosses |
| --- | --- |
| Structure | the plug-plate socket link: what holds the module on |
| Power | through the frame, as today (the frame is the circuit): a welded module is on the ship's circuit |
| Data | a pass-through: Data k on the plate and on the plug act as one wire end, both ways. Thrusters need no wire (they answer the flight control as any thruster on the body); data is for what the module's maker wired |
| Pipe, coolant | a pass-through node: a pipe into the plate's Fuel A continues out of the plug's Fuel A (coolant is `noEffect` today) |
| Ammo | the ship's feed into the plate continues into the module's ring feed |
| Chute | a chute junction across the face (2x2 and up) |
| Shaft | not carried: a drive shaft never crosses a joint |

The count grows with area: 1x1 has power, data 1-2 and pipe A; 2x2 adds data 3-4, pipe B, coolant A, ammo, chute A; 3x3
adds coolant B and chute B; past 3x3 the 3x3 set repeats every 6 m along the edges. Pass-throughs are derived at the
rebuild from the plug-plate link, as ship works are: nothing per tick, nothing on the wire.

### 2.6 Lifting lugs

Every plug has a **lifting lug** at each corner (and every 6 m of edge on big plugs), a standard pick-up point. Fitters
grip lugs, and the physics gun's beam attaches at the plug's middle, so any module, however odd, can be carried the same
way. Lugs are detail plus a small solid each; they stand inside the footprint, so they never leave the room.

## 3. Fitting checks: clearance and collision

### 3.1 The socket's column (at placing and at every rebuild)

The footprint plus side clearance, from the plate to the surface plus 4 cm (flush, opening) and up to `above` (proud,
opening). Solids of the ship's other parts inside it are listed ("in the way"), the plate's lamp shows amber, and nothing
can be fitted until it is clear. The well, the plate and the cut parts' remaining sheet are not in the way.

### 3.2 The module against its room (at the fit, and when a module is saved)

- Its solids, turned and seated at the style's place, lie inside the room; a flush module's solids stop at its face
  (detail may stand 4 mm over).
- Then the build's own overlap test against the ship (`ConstructBuild.Blocked`, the first blocker named by `BlockerOf`),
  the plate excepted: the room is the reservation, the overlap test is the truth.
- Mass and peak thrust or recoil within the size's rating; the primary tag against a restricted socket.

### 3.3 Moving parts

Each joint of the module is swept through its range in 15 degree steps (a turntable a full turn, a hinge its limits), the
leaf's parts posed at each step and tested against the ship's solids (at most 24 x 13 poses, off the game thread for big
modules, cached by module version, socket pose and the ship's structure stamp). A clash refuses the fit in Survival and
warns in Creative: "the Cannon hits the Mast (step 412) at 140 degrees, elevation 20". The same sweep runs for the
fitter's carry path (7.3).

### 3.4 Players, shots and bodies

Nothing special: the hole is a real hole, the well's walls and the module are solid parts of the ship. A player can climb
into a turret well, a shot down a well hits the module, a fitted module never collides with its own ship.

## 4. Modules

### 4.1 Making one

A module is built like any construct: place a Plug (on the ground, on a **Module Bench**, a plate on legs of any size up to
3x3 that holds it square and can stand at a well depth to try a flush or opening module, or straight into a ship's socket)
and weld parts onto it. Nothing has to be saved: the moment a plug has parts on it, it is a module (4.5). Saving it as a
design is optional: selecting it and pressing Comma opens the save window, which gains **Save as module** when the
selection has exactly one plug at its base (the plug is step 0). The window shows the tags, the primary tag, the styles its
bounds allow, the stats and a thumbnail, and names it.

### 4.2 Tags: what a module is

Tags are read from the parts, never typed: `engine` (a thruster or a drive), `reactor` (a power plant or genset),
`turret` (a gun on a joint), `turret-bay` (turret sockets on it), `missile` (racks, launchers), `armour` (armour plate over
a share of its face), `harvest` (drills, scoops, gatherers), `cargo` (storage), `power-store` (batteries, capacitors),
`sensor` (radar, cameras, dishes), `life-support` (seats, bunks, hydroponics), `utility` (anything else). A module has every
tag its parts give; its **primary tag** is the one its maker picks among them (default: the tag with the most mass behind
it), and only the primary counts for a restricted socket. The reactor-with-turrets is `reactor, turret`; fitted to a
reactor socket as a reactor, its turrets work too.

### 4.3 Stats, versions, look

- **Stats** come from a flat build in a scratch world (as the broken check, sub-blueprints.md 2): mass, thrust and burn
  (with the works cap), power made and drawn, guns, damage a second, rounds a minute, armour, storage, harvest rate,
  bounds and face, and the **bill** (materials). Kept with the broken check's cache in `ConstructBlueprints/.checks/`.
- **Name and version**: the blueprint's. A built module is what was built; a newer version changes ships only through
  blueprints that use it live.
- **Look**: a thumbnail by the `--preview-shot` path (3 s, no window) at save. The plate, plug and lugs keep the standard
  look; everything else is the maker's.

### 4.4 Submodules and adapters

Modules may carry sockets: a turret bay's turret sockets, a reactor's coolant pump sockets. Fitted modules are uses
(`Construct.Uses`, `CPart.Use`), so a module in a module is a use in a use (limits: 8 deep, 128 a blueprint, 1,024 a tree).
An **adapter** is a module too: a 2x2 plug with a 1x2 or 1x1 plate on top, so a smaller module goes in a bigger socket
(sizes match exactly otherwise). A player can put **custom sockets** on any module by welding a Module Socket plate onto
it, so new forms grow: a reactor with a 1x1 sensor socket on its flank, a turret bay of the player's own layout.

### 4.5 Free-built and customised modules (answer 11)

- **Recognised by its plug.** A module is a Plug and every part joined to it on the plug's own side: at the rebuild, a
  flood from each plug through socket links, never crossing the plug's face (where it meets a plate) and stopping at any
  plate's face (beyond a plate is a submodule, with a plug of its own). The flood is bounded (each part visited once, a
  visited stamp per part, as the power rebuild's queue) and run only on a rebuild, so it costs nothing per tick. Parts it
  reaches carry the module's use index (`CPart.Use`); a module never saved has a use with no blueprint id ("unsaved
  module", named "Module on Socket 3" until saved).
- **Live tags and stats.** The same rebuild works the tags (4.2) and a cheap stats summary from the parts' own numbers
  (mass, thrust and burn, power made and drawn, guns, storage, harvest rate, bounds): what the look line on the plug, the
  socket's panel and the fitter panel show, and what restricted sockets and faction scoring read. The full flat-build stats
  (works caps, damage a second) are worked out when the module is saved, or off the game thread at most once every 2 s
  while it is being changed.
- **Building on a fitted module.** Welding parts onto a module that sits in a socket is allowed, each part checked as it is
  placed: it must lie inside the socket's room (3.2) and clear the sweep of the module's own joints (3.3); the module's
  mass and thrust must stay within the size's rating; and the part may join the module's parts only, never the ship's as
  well (a part touching both is refused: "this would weld the module to the hull: keep it inside the socket's room").
  So a module stays one piece that comes off whole. A restricted socket rechecks the primary tag at each rebuild: a module
  in an engine socket that no longer has a thruster is marked "not an engine any more" on the plate and does nothing until
  it is one again (it is not thrown off).
- **Loose modules** (on a bench, on the ground) have no room: they are built freely, and the room, rating and tag checks
  come at the fit. Past the Construct's own limits (16,000 parts a body) nothing else caps them.
- **Relation to designs.** A saved module fitted from a blueprint is a use of that design; adding, removing or moving
  parts in it makes it "changed" (the sub-blueprint rule, the user's answer 4 of 2026-11-16), paint and settings don't.
  Saving offers the usual three: as plain parts (an unsaved module again, but still a module because the plug is there),
  a new version of the design, or a new module design. An unsaved module is saved the same way; until then it can't be
  stamped, packed, sold by design or chosen by factions, but it is fitted, carried, traded and fought with like any other.

## 5. A module is a physical object

- **Where a module can be.** Fitted to a ship's socket; fitted to a **Module Stand** (a plate on a frame, any size to 3x3,
  floor or rack style; a Module Rack is stands stacked in a frame) where it is welded but nothing couples, so it is stored,
  not working; carried by a fitter, a crane, a Loader Arm or the physics gun; held in a cargo volume (which freezes bodies,
  storage-and-cargo.md); or loose on the ground or a deck. In every case it is the same body, looks the same and keeps its
  damage. "A module on a shelf" is a module on a stand.
- **Moving between ships**: carried, or in a hold. A module loose aboard a moving ship rides it as any loose body aboard does
  (docs/sandbox/design/local-frame-physics.md); outside a flying ship it is left behind, so an in-flight swap outside the
  hull is done by a fitter on that ship, which holds the module in the ship's frame.
- **No counts in inventories.** The Tab inventory never lists modules; the Modules tab (Creative) lists designs, and in
  Survival only says where the player's modules are ("Torch Pod Mk2: on the Dray, socket 3; on the rack in Hangar A").

## 6. Attaching, swapping, detaching

### 6.1 Who fits what

| Module | Way | Time |
| --- | --- | --- |
| Up to 2x2 and the physics gun's Survival lift (3 t, `ConstructEdits.LiftOf`, capped by `ConstructGrab.SurvivalLiftCap`) | **By hand**: carry it with the physics gun and Socket attach it, or set it down within 2.5 m of a free socket and press F on the socket (the tool dock's pull, `ConstructEditsDock.cs`, past its 8 parts) | 5 s + 1 s a tonne |
| Bigger or heavier | **A Module Fitter** that reaches both the module and the socket (section 7), or a Building Station's fitter | the fitter's cycle (7.4) |
| Creative, any size | the Modules tab: aim at a free socket, the ghost stands on it at the first turn that fits, R the next turn, LMB fits it at once (built from nothing) | instant |

The snap allows only the socket's turns and seats the plug at the style's depth. The fit is exact, never physical: the
module is set at the socket's pose (Rot24 turn, quantised place).

### 6.2 What happens at the fit

One server action in one tick: `Merge` with the turn, a socket link plug to plate, the parts tagged as a use of the
module's id and version, the plate's hidden `Fitted` setting set to that use. At the next rebuild the couplings join, the
works flood sees an engine plate's mount, the module's guns, thrusters and stores are the ship's own. One fit a tick a
world (`FitTick`). Until the weld finishes the module does nothing.

### 6.3 Swapping any time

A module can be taken off or fitted at any time, in flight too, by whoever can reach it: a player in reach of the plug
(small modules), a fitter whose reach holds the socket (large ones). Nothing waits for it to be idle: an engine taken off
under thrust stops pushing the tick it is cut free, a reactor taken off takes its power with it, a turret with a gunner
seated in it puts the gunner out first (the seat's way out). The look line warns before the cut: "this is your only
reactor: the ship goes dark".

### 6.4 Detaching and damage

- **Taking off** (cut free on the plug by hand, a fitter, a Creative remove): the plug-plate link is cut and the use's parts
  split into their own body (`ConstructWorld.SplitPieces`) with their links, wires and fuel; the pass-throughs go at the
  next rebuild. Out of a well it is lifted along the normal, never pushed through the walls.
- **Damage**: a plug or plate at zero health breaks off (`PartBreak.Detach`), so the module falls away as a wreck with its
  tags (salvage it, mend it, fit it again). A broken part inside is mended as any part.

## 7. Module Fitters (the machinery)

### 7.1 What they are

A **Module Fitter** is a lifter (docs/sandbox/design/arms-and-cranes.md; `ConstructLifters*.cs`, its 10 Hz step, scripted
paths and two-link IK) built for modules: a heavy turret base, two long links, a wrist and a **tool head** with three tools
on a turning carrier: a **gripper** (a spreader frame that grips the plug's lugs and sizes itself to the plug), a **welding
torch** and a **cutting head**. It mounts on a ship (aboard, for swaps in flight), a station, a yard, or a Crane Rail run
(a **rail fitter**: its base travels the run like a Crane Bridge, so one fitter serves a ship's whole length).

| Fitter | Reach | Payload | Largest module | Cut, weld (m/s of seam) | Power | Built |
| --- | --- | --- | --- | --- | --- | --- |
| S | 1.5 to 8 m | 20 t | 3x3, 6 m tall | 0.10, 0.25 | 8 kW | now |
| M | 3 to 20 m | 150 t | 8x8, 16 m | 0.15, 0.4 | 40 kW | later |
| L | 6 to 48 m | 1,200 t | 20x20, 40 m | 0.2, 0.6 | 200 kW | later |
| Capital | 12 to 96 m | 6,000 t | 30x30, 60 m | 0.25, 0.8 | 800 kW | later |

(Proposed numbers; the class ladder follows the crane classes' 30 t, 300 t, 3,000 t steps.) Only S is built now, since
sockets stop at 3x3; the larger ones are the same part generator at a bigger scale.

### 7.2 How it is told what to do

F on a fitter opens its panel: the sockets in its reach on its own construct and any other within reach (a ship moored or
held beside it), each with its module or "free", and the modules in its reach (on stands, in the open, in a hold whose
volume it reaches). Jobs: **Fit** (module to socket), **Take off** (socket to a stand or the floor), **Swap** (both, the
old module set down first), **Cut** (a socket's well, for sockets with no ring). Jobs queue (at most 8 a fitter). A data
in starts the queue, so a ship's crew can refit by a button. A Building Station's build uses its fitters for its uses
(sub-blueprints.md 4, the lifter way) as well as its Loader Arms.

### 7.3 Reach and paths

A job is offered only if the socket's face and the module's lugs lie in the fitter's reach, the module is within payload
and size, and the carry path is clear: lift along the module's current normal by its height plus 0.5 m, travel (at the
highest of start and end plus clearance), turn to the socket's pose, lower along the socket's normal into the well. The
path is checked against the solids of both constructs at 0.5 m steps off the game thread, as the sweep is (3.3); a blocked
path names the blocker. Players in the way are pushed 0.75 m or waited for, as lifters do now. A module wider than one
gripper's span is carried by **two fitters together**: each grips one side, one path for both, the body posed at the
midpoint of their wrists; the panel pairs them when both reach.

### 7.4 The cycle and what it looks like

1. **Cut** (an opening socket with no ring, or a hull that needs it): the cutting head traces the outline at the cutting
   speed for each 10 cm of hull, a white kerf line with a spray of sparks and a glow on the head, the slug lifted out by the
   gripper and set down, the panels changing to their cut parts at the end.
2. **Pick**: the spreader opens to the plug, closes on the lugs (a clamp sound), the module lifts.
3. **Carry and seat**: along the path; the wrist turns it; it lowers the last metre slowly and stops on the plate.
4. **Weld**: the torch traces the plug's perimeter at the welding speed, a bright bead that cools from white to orange to
   dark over 10 s and stays as a seam (drawn, no part), sparks and the welding sound; the merge happens when the bead closes.
5. **Back**: the head returns to rest.

A 2x2, 8 t module on an S fitter 6 m away: pick 3 s, carry 6 s, weld 8 m / 0.25 = 32 s, about 45 s. Taking off reverses
it with the cutting head on the seam. Every step is drawn from the job's clock on every peer (host and client see the same
pose), as the lifters and the Shell Gantry do.

## 8. Ships declare sockets

- **Players** take Module Socket (Fittings > Modules), choose size, style, depth and above on the tool wheel's outer ring
  before placing, then kind and keying with the configure tool. A placed opening socket starts its cut.
- **Generators**: the Vehicle Kit's `BlueprintScript` gains `Socket(...)` and `Module(...)` (section 14). A ship generator
  leaves the openings in its own hull (it draws its parts, so it simply doesn't put hull where a well goes), lays the well
  and the plate, then fills each socket by the faction's choice (section 9). One hull generator gives many ships.

## 9. Faction and NPC module choice

Factions and NPC captains choose modules by tags and stats with trade-offs, wherever they outfit a ship: a generator making
a faction ship, a faction controller refitting at its station, a station deciding what it stocks and pays for.

### 9.1 What a faction wants (data)

Each faction file (`Crew/Factions/*.json`) gains:

```jsonc
"modules": {
  "tags":  { "turret": 1.0, "missile": 0.9, "armour": 0.8, "reactor": 0.7, "engine": 0.7, "sensor": 0.4 }, // a combat group
  "stats": { "speed": 0.8, "power": 0.6, "fuel": 0.3, "mass": 0.4, "cost": 0.5 },  // how much each trade-off matters
  "needs": { "accel": 1.5, "powerMargin": 1.15, "minGuns": 2 },                    // hard floors for a ship
  "pick":  3                                                                       // chosen among the best N (variety)
}
```

A farming or mining faction weights `harvest` and `cargo`; Traders `cargo`, `engine` and `armour`; Raiders `turret` and
`engine` with `cost` high.

### 9.2 Scoring a candidate

For one socket, the candidates are the modules the faction can get (shipped and generated ones, its station's stock) that
fit it (size, kind, style, room, sweep). Each gets:

```
score = sum over tags t of  tags[t] x benefit_t(module)      (a tag's benefit: its stat against the best candidate's, 0..1)
      - stats.power x  max(0, draw - spare power) / ship's power made
      - stats.mass  x  module mass / ship mass
      - stats.fuel  x  burn / the fuel the ship carries for an hour
      - stats.cost  x  bill / the faction's budget for the ship
      + stats.speed x  (thrust gain / ship mass) / needs.accel      (engines only)
```

So a faster engine wins while the ship has the power for it; when its draw would eat the margin, the cheaper, slower one
scores higher, or the reactor socket is upgraded first. **Order**: restricted sockets first (engines, then reactors, then
turrets), then turret bays, then generic sockets by the tags still unmet. After one pass the ship's floors are checked
(`needs`): short of power, the most power-hungry module is swapped for the next best or a bigger reactor taken; short of
acceleration, the engines; short of guns, a generic socket takes a turret bay. At most three passes. The pick among the
top `pick` candidates uses the generator's dice, so a seed is repeatable and a fleet varies.

### 9.3 Where it runs

At generation (`--gen-vehicle`, the faction ships' seeds), at a faction controller's refit (a power's ship moored at its
station with credits in its stock: the station's fitters do it in the world if a player is near, else by orbit as the
far layer trades), and in a station's market (9.2's score of a module for its power's typical ship sets what it pays).
All of it is off the game thread or at a controller's coarse rate, never per tick.

## 10. The economy

- **Value**: a module's bill, every part's recipe materials at the station's prices for those materials
  (`Crew/Stations.json`), times the demand of the power that holds the station: 0.7 to 1.0 of the bill when it buys,
  1.2 to 1.5 when it sells, by how well the module scores for its power (9.2), shifted by standing (below 0: no trade).
- **Markets are physical.** A station's stock of modules is a list of blueprints and their state while nobody is near;
  aboard or moored, its modules stand on its racks as real bodies (spawned on stand sockets, frozen; gone again when the
  bubble empties, as far groups do). **Buying** a module makes it yours where it stands; the station's fitter carries it to
  a free socket of your moored ship (fitted) or into your hold. **Selling** a module needs it physically at the station: on
  your moored ship (the fitter takes it off) or on a stand there; it joins the station's stock under its maker's name.
- **Contracts** (faction controllers' phase 5): "deliver a 2x2 reactor making at least 3 MW to Glacier Watch, 4,000
  credits", checked against the delivered module's stats.
- **Barter** between players is physical: hand the module over (carry it, set it on their stand or in their hold).
- **Sharing designs**: Pack (`<name>.pack.json`) shares a module design with its submodules. In Survival a design must be
  built from materials or bought as a module; a file never gives goods. **Mods** add module blueprints under
  `Constructs/Blueprints/` (docs/mods/MODS.md).

## 11. Retrofitting the main ships

Each is changed in its generator (never its output), keeping its look, its interior and its numbers: a fitted ship is the
old ship with its pieces as modules. Sizes stay within 3x3 now; positions that want bigger are noted for later.

| Ship (generator) | Sockets | Modules made from what it has now |
| --- | --- | --- |
| **Rust bucket** (`ShipParts.cs`) | 2 keyed engine sockets 3x3 proud on the stern pods' mounts; 1 reactor socket 2x2 opening in the engine room floor; 1 generic 2x3 proud on the spine (the strapped container); 1 generic 1x1 on the mast | Rust Pod (the stern pod, intake cowl and cooling ring); Scrap Reactor; Spine Container (cargo); Mast Dish (sensor). The folded deck crane stays hull |
| **Magpie** (`MinerParts.cs`) | 2 keyed engine sockets 3x3 proud at the pylons' ends; 4 generic 2x2 opening through the keel for the drills; 1 reactor socket 2x2 opening; 1 generic 1x1 for the ground radar | Magpie Nacelle (main drive and lift jet; the fuel pod stays on the pylon, later a 3x6 module); Drill Ram (drill on its ram, harvest); reactor; Radar Dish. Hoppers stay hull |
| **Zero Day** (`TorpedoFrigateParts.cs`) | 4 keyed engine sockets 2x2 opening in the stern plate for the drive bells (their Drive Core and Fuel Pump stay inside, behind the plate as mount: ship-works.md rule 2); 1 reactor socket 3x3 opening in the lower deck aft; 4 generic 3x3 opening for the PDC barbettes, each with a turret bay; 2 generic 2x3 proud for the torpedo sponsons | Drive Bell M; Zero Day Reactor; PDC Bay (ring well, its own magazine, one turret socket 2x2) with a PDC turret module; Torpedo Sponson (missile). The spinal railgun, the shuttle cradle and the Limpet's clamp stay as they are |
| **Dray** (`HaulerParts.cs`) | 2 keyed engine sockets 3x3 proud on the drive shrouds; 1 reactor socket 3x3 and 2 generic 2x2 opening in the drive room floor (empty today); 1 generic 1x1 on the sensor mast | Dray Drive; a reactor and two power stores for the drive room; Mast Sensor. The container slots stay twistlocks (containers aren't modules); the crane stays |

Every retrofit is tried by flying it (`--flight-check`), climbing its ladders (`--ladder-check`, constructs.md lesson of
2026-11-17), its crew's checks where it has crew (`--crew-check`), and a swap of one module of each kind with a fitter.
Saves holding the old ships keep the old bodies (a blueprint change doesn't touch built ships).

## 12. What exists and how it maps

| Existing | Relation | Do |
| --- | --- | --- |
| Sub-blueprints (`SubBlueprints.cs`, `ConstructStationTree.cs`, `ConstructClipUses.cs`) | A module is a sub-blueprint with a plug; fitted modules are uses | Reuse: ids, versions, Live / Lock, packs, flatten, big fit, `USES` |
| Socket attach (`SocketWeld`, `SocketWeldFlow`), Rot24, `Merge`, `SplitPieces` | The fit and take-off | Add the size, turn and seat-depth rule |
| Cut parts (`HullLoft.Cuts.cs`) | The well's hole | Reuse; the socket plans and makes the cut keys |
| Flush shell pod pocket (`Renderer.Cuts.cs`, branch `claude/flush-pod-pocket`) | Hides the seams band at a rim | Reuse once merged; wells share its budget |
| Lifters, Crane Rails (`ConstructLifters*.cs`, `ConstructLifterCarry.cs`, `ConstructCraneRails.cs`) | The Module Fitter is a lifter with a tool head | Extend; the body carry exists (sub-blueprints' delivery) |
| Tool dock, Shell Gantry | Small precedents (pull onto a dock; arms swap modules) | Leave them |
| Ship works, Drive Pod, Turret Ring | Engine plate = mount; turret socket feeds `Ammo in` | Flags only |
| Shell frames and slots | Same words, but shells are worn bodies | Leave |
| Modular engines | Shafts don't cross | Leave |
| Docking clamps, `"carry"` | Carried craft fly off; modules never do | Leave |
| Station markets (`FactionOp.StationBuy` / `StationSell`) | Module buying and selling | Add a module case |

# Part B: how it will be implemented

## 13. Data formats

### 13.1 `Constructs/ModuleSockets.json`

```jsonc
{
  "format": "call-of-slop-module-sockets/1",
  "sizes": { "most": 30, "built": ["1x1", "1x2", "1x3", "2x2", "2x3", "3x3"] },   // W x L whole metres, W <= L
  "rating": { "massPerM2": 3000, "thrustPerM2": 250000 },
  "bolts": { "pitch": 0.5, "pitchFrom8": 2 }, "lugs": { "every": 6 },
  "kinds": ["generic", "engine", "reactor", "turret"],                         // order is the wire's: new ones at the end
  "styles": ["proud", "flush", "opening"],                                     // order is the wire's
  "tags": ["engine", "reactor", "turret", "turret-bay", "missile", "armour", "harvest", "cargo",
           "power-store", "sensor", "life-support", "utility"],               // order is the wire's
  "couplings": [                                                               // where, in the footprint's 0..1 square
    { "name": "Data 1", "type": "data", "at": [0.15, 0.15], "from": "1x1" },
    { "name": "Fuel A", "type": "pipe", "fluid": "fuel", "at": [0.85, 0.15], "from": "1x1" },
    { "name": "Coolant A", "type": "pipe", "fluid": "coolant", "at": [0.85, 0.5], "from": "2x2" },
    { "name": "Ammo", "type": "ammo", "at": [0.5, 0.85], "from": "2x2" },
    { "name": "Chute A", "type": "chute", "at": [0.5, 0.5], "from": "2x2" }
  ],
  "repeatEdge": 6,                                                             // past 3x3 the 3x3 set repeats every 6 m
  "depth": { "step": 0.25, "most": "2 x short" }, "above": { "step": 0.5, "most": "4 x long" }, "side": 0.05,
  "cut": { "secondsPerMetrePer10cm": 2, "wattsPerMetre": 4000, "ringUpTo": "3x3" },
  "hand": { "upTo": "2x2", "mass": "physics gun", "seconds": 5, "perTonne": 1, "pull": 2.5 },
  "fitters": [
    { "name": "S", "reach": [1.5, 8], "payload": 20000, "largest": [3, 3, 6], "cut": 0.1, "weld": 0.25, "power": 8000 },
    { "name": "M", "reach": [3, 20], "payload": 150000, "largest": [8, 8, 16], "cut": 0.15, "weld": 0.4, "power": 40000 }
  ],
  "price": { "buy": [0.7, 1.0], "sell": [1.2, 1.5] }
}
```

Loaded and refused with a reason, as `ShipWorks.json` is (`ModuleSocketTable.cs`).

### 13.2 Part blocks and keys

```jsonc
"moduleSocket": { "w": 2, "l": 2 }    // Module Socket 2x2: settings kind, style, depth, above, keyed; hidden: Fitted, Cut
"modulePlug":   { "w": 2, "l": 2 }
"moduleStand":  { "w": 2, "l": 2, "rack": false }
"moduleFitter": { "class": "S" }      // beside its "storage.lifter" block (the lifter's own data)
```

- The six built sizes are part files (with Assembler recipes appended at the end, `Assembling.cs` `LateHand`). Bigger plates
  and plugs are run-time keyed parts, `Module Socket (6x10)` and `Module Plug (6x10)`, made the way wells are, and their
  recipes are made from the key by area when they are built (later).
- `Module Well (2x2 d150 steel)`: run-time def, ids from `Extrusion.RuntimeBase`, read through `CPart.D` /
  `ConstructParts.Def`, sent and hashed by key (constructs.md's first lesson).
- `SocketKind.Module` at the end of the enum; `CSocket` gains `W` and `L` bytes (0: not a module socket), so a plug snaps
  only to a plate of its size. `SnapBuild.Build` classifies the family in the same change. Kind and style are choice
  settings with `ConstructConfig.DefaultType` entries.

### 13.3 A module blueprint

```jsonc
{
  "name": "Torch Pod Mk2", "id": "4be1c9a0d27f3e65", "version": 3,
  "module": {
    "w": 2, "l": 2, "primary": "engine", "tags": ["engine", "power-store"],
    "styles": { "proud": { "height": 4.2 }, "opening": { "below": 1.5, "above": 2.7 } }, // flush: { "face": m }
    "maker": "Twitch",
    "stats": { "mass": 8400, "thrust": 1800000, "burn": 3.6, "made": 0, "draw": 120, "dps": 0, "armour": 0,
               "store": 0, "harvest": 0, "bounds": [2.0, 4.2, 2.0], "bill": { "steel": 3100, "copper": 240 } }
  },
  "steps": [ { "part": "Module Plug 2x2", "at": [0, 0, 0] }, "..." ]
}
```

`tags`, `styles` and `stats` are written by the save window from the flat build; a hand edit is redone at the next check and
logged.

### 13.4 A socket in a ship's blueprint

Ordinary steps: the cut parts round it by key, the well by key, the plate on it with its settings
(`"cfg": {"kind": "engine", "style": "opening", "depth": 1.5, "above": 3, "keyed": true}`), and a fitted module as a
`"uses"` entry whose `"on"` is `[plate step, "face", 0, "face"]` (the sub-blueprint format). A blueprint's socket is built
already cut: the cut parts are steps, so a station builds the hole as it builds the hull.

## 14. Code

### 14.1 New files

| File | What it holds |
| --- | --- |
| `Source/Constructs/ModuleSocketTable.cs` | The table: sizes and their rules, kinds, styles, tags, couplings, cut, hand, fitters, prices |
| `Source/Constructs/ModuleSocketDef.cs` | `moduleSocket`, `modulePlug`, `moduleStand`, `moduleFitter` parsing; keyed plates and plugs past 3x3 |
| `Source/Constructs/ModuleWell.cs` | The well def from its key; `Column(c, plate)`; `InTheWay`; `PlanCut` (cut keys per hull part, or the refusal) |
| `Source/Constructs/ConstructModuleSockets.cs` | Partial `Construct`: `CModSocket[]` at the rebuild after ship works (plate, size, kind, style, depth, above, fitted use, column state, cut progress, couplings); the pass-through tables for logic, routes and ammo |
| `Source/Constructs/ModuleFit.cs` | `FitWhy` (room, overlap, rating, tag, sweep), `Seat`, `Fit` (merge and tags), `TakeOff` (split) |
| `Source/Constructs/ModuleSweep.cs` | Joint sweeps and carry-path checks, sliced, off the game thread, cached |
| `Source/Constructs/ModuleCut.cs` | The cut job: timer, power draw, slug and salvage, the swap to cut parts in one tick |
| `Source/Constructs/ModuleTags.cs` | The plug flood at the rebuild (which parts are each module), live tags and the cheap stats summary, the primary tag; full stats from a flat build (`ModuleStats.Of`) |
| `Source/Constructs/ModuleBlueprints.cs` | The `"module"` block read and written; the thumbnail request |
| `Source/Constructs/ConstructModuleFitters.cs` | The fitter: jobs (fit, take off, swap, cut), the queue, the tool head's phases, paired carries, on the lifter step |
| `Source/Constructs/ConstructEditsModules.cs` | Server edits: place socket, set style, fit and take off by hand, F pull, stand; the placing rule for parts welded onto a fitted module (room, sweep, rating, no bridge to the hull); reach, who may, Survival timers |
| `Source/Constructs/ModuleChoice.cs` | Section 9's scoring and passes, pure functions over candidates and a ship summary |
| `Source/Game/GameSandboxModules.cs` | `CActKind.Module`'s server side, `FitTick`, the fitter panel's ops, market glue |
| `Source/App/Program.SandboxModules.cs` | The client: placing options, the module ghost on a socket, look lines, the fitter panel's input |
| `Source/UI/UiSandboxModules.cs`, `UiModuleFitter.cs` | The Modules tab (Creative designs, Survival "where are mine"), the socket's configure additions, the fitter panel (Esc and `SandboxLocalReset` close it) |
| `Source/Render/ModuleSocketRender.cs`, `ModuleFitterRender.cs` | The room's wire box, the clash marker, the plate's lamp, the cutting ring's kerf; the fitter's arm, tool head, kerf, sparks and cooling bead |
| `Source/PartKit/Generators/ModuleSocketParts.cs` | Part kit `module_sockets`: six plates, six plugs, bench, stands and rack, adapters (as shipped module blueprints), the S fitter |
| `Source/VehicleKit/ModuleGen.cs` | `--gen-module TAG W L [--style S] [--faction F] [--seed N]`, the shipped faction modules |

### 14.2 Existing code that changes

- `ConstructParts.cs`: `SocketKind.Module`, `CSocket.W/L`, known keys; `ConstructConfig.DefaultType`.
- `SnapBuild.cs`, `SocketWeld.cs`, `SocketWeldFlow.cs`: plug onto plate only by allowed turns, seated at depth.
- `ConstructLogic`, `ConstructRoutes`, `ConstructAmmo.FedFrom`, `ConstructWorks`: pass-throughs and the mount flag, at the
  rebuild.
- `ConstructLifters*.cs`, `ConstructLifterCarry.cs`: a lifter kind `fitter` with a tool head and a body carry by lugs; the
  lift word gains the tool phase.
- `ConstructClip.ToBlueprint`, `ConstructClipUses.cs`, `PlayerBlueprints`, `UiSandboxBlueprints.cs`: Save as module.
- `SubBlueprints.cs`: a use may be a module joined at a plate.
- `ConstructStation.cs`, `ConstructStationTree.cs`: station fitters fit uses; `StationOp` gains nothing (the fitter has its
  own ops).
- `Renderer.Cuts.cs` (after `claude/flush-pod-pocket`): wells add their rim band; the 16-within-60-m budget is shared,
  nearest first, raised only if measured.
- `Interiors/InteriorPlanner.cs`: a socket's column inside the hull is reserved volume.
- `FactionStyle` / the faction files' loader: the `"modules"` block; Vehicle Kit `BlueprintScript.Socket(w, l, kind,
  style, depth, above, at, turn)` and `Module(socket, name)`; ship generators (section 11).
- `FactionOp.StationBuy` / `StationSell`, `Crew/Stations.json`: module stock and racks; the far layer's refits.
- `Categories.json`: Fittings > Modules.

### 14.3 Wire and saves

- **Protocol**: one bump (the number after the latest at merge) with a history line. `CActKind.Module` at the end:
  `u16 body, u16 plate part, u8 op (fit, take off, F pull, set style), u16 module body, u8 turn`. `Msg.ModuleFitter` at the
  end of `Msg` for the fitter's jobs and queue (as `Msg.Gantry` is for the Shell Gantry), sent to every peer and each
  joiner. Plate settings (kind, style, depth, above, keyed, Fitted, Cut progress) are part settings: no new bytes. A fitted
  module rides `CBodyKind.Uses` (exists). Wells and big plates are run-time defs sent by key.
- **Saves**: the `USES` block (exists), part settings, well and cut keys, a `MFIT` block for a fitter's job in progress
  (its phase, the body it holds) after the `LIFT` block. Module blueprints, thumbnails and checks live in
  `ConstructBlueprints` and its subfolders, already in `SavePaths.SaveFolders`: no new save file or list entry. Older saves
  have no sockets and load unchanged; a world with an old Zero Day keeps it.

### 14.4 Performance and limits, planned to 30x30

- A fitted module is plain parts: nothing per tick beyond its parts' own cost.
- The rebuild: one pass over the sockets (at most 64 a construct, planned: at most 255 with big ships); column tests use
  the build's box query; nothing allocated per tick.
- A fit is one merge: 2.5 ms warm for a 2,605-part use (sub-blueprints-notes.md). A 30x30 module may hold 10,000 parts,
  past the 8 ms tick: before sizes over 8x8 are built, the merge and the split are prepared off the game thread and
  committed in slices of at most 2,000 parts a tick, the module frozen and inert until the last slice (planned now so the
  formats don't change; built with the M fitter).
- Sweeps, paths, stats, thumbnails and choice scoring run off the game thread or at a controller's rate.
- Limits: depth at most 2 x the short side, above 4 x the long side, a module at most `Construct.MaxParts` with its ship
  (16,000), the use limits as sub-blueprints, 8 jobs a fitter, one fit and one cut commit a tick a world. Every refusal
  gives the numbers; every dropped coupling or part logs a line.

## 15. Tools, commands and checks

- `--check-modules [DIR]`: every module built flat: tags, styles, stats, room, rating; to `callofslop.log` with REFUSED
  lines (sampled when there are many).
- `--check-sockets BLUEPRINT`: each socket: style, column, what is in the way, which shipped modules fit, each sweep.
- `--module-choice SHIP --faction F [--seed N]`: section 9's scores per socket and the passes, as a table.
- `--gen-parts module_sockets [--check]`, `--gen-module ...`, `--preview-shot` for thumbnails.
- Window tool verbs: `modsock place W L STYLE`, `modsock fit NAME [turn]`, `modsock off`, `modfitter job fit|off|swap|cut`,
  `modsock state`.
- **No self-test section** is added. Each phase is tried by hand, host and joined client; the report says what wasn't.

## 16. Phases (one builder, the whole feature; WIP commits `[skip ci]`)

1. **The standard, proud, by hand** (large): the table, `module_sockets` parts (plates, plugs, bench, stands), the socket
   kind and sizes, the column and room box, the plug flood, live tags and stats, free building on a plug and on a fitted
   module (its placing rule), Save as module with full stats, Creative fit, hand fit (physics gun, F pull), take-off, swap
   any time, damage. Tried with a hand-built 2x2 module on a test hull.
2. **Wells and cuts** (large): the well def, flush and opening styles, the cutting ring (time, power, kerf, slug, salvage),
   cut parts made by the socket, the rim band, the sweep. Tried: a turret in an opening turning full circle, a flush
   sensor, a floor reactor bay, a player climbing into a well, shots down it.
3. **Couplings and submodules** (medium): data, pipe, ammo and chute pass-throughs, the mount flag, ratings, turret bays,
   adapters, the Modules tab.
4. **The S fitter** (large): the fitter part on the lifter code, the tool head, jobs and queue, paths, paired carries, cut
   and weld visuals, the fitter panel, a ship-mounted fitter swapping in flight, station fitters for uses, `MFIT` saves.
5. **Faction choice and generators** (large): the faction `"modules"` block, `ModuleChoice`, `BlueprintScript.Socket` /
   `Module`, `--gen-module`, shipped faction modules, `--module-choice`.
6. **Retrofits** (large): the rust bucket, Magpie, Zero Day and Dray as section 11, each flown, climbed and swapped.
7. **Markets** (medium): physical racks at stations, buying and selling modules, demand prices, a contract kind, refits by
   faction controllers.
8. **Later**: sizes past 3x3 (keyed plates and plugs, sliced merges, the M, L and Capital fitters), bigger retrofits (the
   Magpie's whole nacelle as a 3x6 module).

## 17. Choices settled by the user's answers

Kinds: generic by default, engine / reactor / turret restricted, reactors also in generic sockets, turret bays as modules
(answer 1). Exact sizes with adapters (2). The socket cuts its own opening at a cost in time and power; large ones by a
fitter's cutter (3). Welded, with large modules fitted by arms that visibly weld and cut (4). Swaps any time, in flight,
where reachable (5). Small by hand, large by machinery (6). Whole metres, 3x3 now, planned to 30x30 (7). No crates: always
the physical module, odd creations allowed (8). Faction choice by tags and stats with trade-offs; prices from the bill and
demand (9). Retrofit the main ships (10). Modules built and customised freely in the world, with custom sockets (11).

## 18. Settled by default (user approved the design with these, 2026-11-19)

1. **Cutting a lofted hull.** The main ships' hulls are lofted parts, and today only formed panels and flat plates can be
   cut (`HullLoft.Cuts.cs`). Recommended: generators leave the openings for the retrofits, and in play an opening socket on
   a lofted part is refused ("this hull can't be cut here yet") until loft cutting is built after phase 6. Else: build loft
   cutting in phase 2, a larger phase.
2. **The by-hand limit.** Recommended: up to 2x2 and the physics gun's Survival mass (5 t). Else: by mass alone (any size
   the gun can lift).
3. **Station stock on racks.** Recommended: a station's modules stand on its racks as real bodies while players are near
   and are a list when nobody is. Else: they are a list on the station's card until bought, then spawned at the berth.

## The user's answers (2026-11-19)

These override the text above where they differ.

1. **Socket kinds:** sockets are universal by default (a generic socket takes any module of its size), but some sockets are restricted to one kind. An engine socket only ever takes engines (a life support module can't go in one). A second reactor must be possible (reactor modules fit generic sockets as well as reactor sockets). A **turret bay** is itself a module: fitted to a socket, it gives turret sockets that turret modules then attach to.
2. **Size match:** exact; a smaller module goes in a bigger socket through an adapter module.
3. **Openings in Survival:** the socket cuts the hull itself (an opening-style socket cuts its well when placed, at a cost in time and power), not the Laser Cutter by hand.
4. **Fixing:** modules are welded onto the ship. Larger modules need supporting machinery to fit: large arms that move the pieces into place, with visible welding and hole-cutting gear on the arms (they do the socket's hull cut and the weld).
5. **Swapping:** any time, in flight too, if the module can be reached (and, for large modules, the machinery is there).
6. **Who fits:** small modules by hand (physics gun / F at the socket); large ones need the fitting arms or a station.
7. **Sizes:** sockets and modules grow in whole metres. Build up to 3x3 m now; plan the formats, limits and machinery for sizes up to about 30x30 m.
8. **Trade and handling:** there are no crates or abstract items. A module is always the physical thing: it keeps its appearance and is moved around physically, so an installed module and one lying on a shelf are the same object. Players may make odd creations (a reactor with turrets on it, and nothing else).
9. **Faction and NPC choice:** modules carry types and stats that factions and NPCs weigh when choosing modules for ships: a farming faction looks for harvesting modules; a combat group looks for turrets, armour, missile bays, reactors and thrusters. Stats decide between candidates with trade-offs: a faster engine is preferred where possible, but its greater power cost is weighed. (Prices: no separate rule given; default stays the material bill, with demand shaped by these preferences.)
10. **Existing ships:** retrofit the main ships with sockets (engine, reactor, turret and others), rather than only adding a showcase ship.
11. **Free-built modules (2026-11-19):** "Modules can also be customized by players adding pieces as they wish, welding them onto the module socket, allowing unique form and function to develop with the custom sockets as well." A module is whatever is welded onto its plug, built in the world like any construct (fitted or loose), its type, stats and look worked out from its parts; players can add sockets to modules; saving it as a design is optional.


## Approval (2026-11-19)

The user approved this design with the three defaults in section 18. Build starts at phase 1 (the standard, proud, by hand).

## What was built (builder, 2026-11-20)

**Phase 1 (the standard, proud, by hand): built.** `Constructs/ModuleSockets.json` (`ModuleSocketTable.cs`); the part kit's
`module_sockets` (`ModuleSocketParts.cs`) makes Module Socket, Module Plug and Module Stand in 1x1, 1x2, 1x3, 2x2, 2x3 and 3x3
(Constructs/Parts/Modules, the Tab inventory's Ship Modules; Assembler recipes appended after every older row,
`Assembling.LaterFolders`); `SocketKind.Module` and six settings (ModKind, ModStyle, ModDepth, ModAbove, ModKeyed,
ModPrimary); the rebuild's plug flood, live tags, primary tag and stats, each socket's room and what is in it
(`ModuleSockets.cs`); fit and take-off by F (`ConstructEditsModules.cs`), the attach tool's socket weld and part placing
under the fit's rules; a saved one-module selection writes a `"module"` block (`ModuleBlueprints.cs`); protocol 378.
Tried by hand on a test rig, host and joined client: tags and look lines, fit from a stand, take-off, back to the stand,
a restricted socket's refusal, building on a fitted module and its room refusal. The window verb `modsock state` prints
every socket and module.

**Phase 2 (wells and cuts): built** (builder, 2026-11-20, protocol 379). A flush or opening socket whose well has hull in it burns
the well through with its own cutting ring (`ModuleCut.cs`): the rebuild flags the body (`Construct.ModCutPending`), the server
plans the cut as a tunnel the footprint plus 7 cm a side from 5 cm over the surface to 1 cm over the plate's face, and every part it
reaches becomes its holed part (`HullHoles`, the breaching cutter's cut: plain boxes and lofted hull facets alike, so the main ships'
lofted hulls can be cut, which answers 18.1 better than the default); a part wholly inside the well burns away; one that can't be
holed (a machine, a joint's part, a part made at run time such as a formed panel or an extruded plate) blocks the socket and is named
on its look line. The part nearest the surface takes the well's liner (a `HullHoles` seal sleeve, 5 cm walls 1 cm outside the
footprint, from 3 mm over the surface to 3 mm over the plate). Time and power from the table (2 s a metre of outline per 10 cm of
hull, 4 kW a metre of the mean side, `Construct.ModCutWatts` in the circuit's draw); no power, it waits; Creative at once (the window
verb `timed` makes a Creative world wait as Survival does). Survival drops the metal cut out as a loose steel plate (none when it would
weigh more than what was lost). The plate's hidden `ModCut` setting (the job's seconds) tells every peer to draw the ring: four heads
round the outline with sparks and the passed kerf glowing (`Program.SandboxModules.cs`, particles only), and the look line's percent.
The joint sweep (`ModuleSweep.cs`): at a fit, each joint of the module swept through its range (turntables and free swivels a full
turn, 15 degree steps; pistons every 0.25 m), the others at rest, its carried solids as oriented boxes against the ship's: refused in
Survival with both parts and the angle named, logged in Creative. A well's room is the footprint plus 5 mm a side (inside the liner).
Tried by hand: a Creative cut through a deck and frame blocks, a timed cut's progress and draw, the slug, a turntable module's sweep
clash against the liner and a stand beside the well.

**Phase 3 (couplings and submodules): partly.** Built: pipe pass-throughs. Plates take "Fuel A" (every size), "Fuel B" and
"Coolant A" (2x2 and up) and "Coolant B" (3x3) in at their back; plugs give the same names out of their top; at the routes' rebuild a
pipe leaving a fitted plug's port takes its source from the pipe ending at the plate's port of that name (`ModuleSockets.PassSource`;
nested modules up to 8 deep; a stand passes nothing), so a ship's tank feeds a module's thruster. An engine socket's plate is the
ship-works mount of a thruster standing on the module's plug (`ConstructWorks.FindMount`, `ModuleSockets.EngineMountOf`). Shipped
adapters written by the part kit (`Constructs/Blueprints/ModuleAdapter*.json`: 2x2 to 1x1 and 1x2, 3x3 to 1x1 and 2x2). Plugs gained
a Small socket in each metre cell's middle (`s0.0`), since 0.5 m parts on the corner grid met the lugs. The Blueprints tab marks module
designs ("module 2x2 engine", and the filter finds them) and lists the modules in the world and where each is (the Modules tab's
"where are mine", in that tab). Tried: a tank's pipe through a fitted 1x1 engine module to an RCS thruster (sourced from the tank, fed),
the four adapters spawned and read as modules. Not built: data, ammo and chute pass-throughs; a Modules tab of its own.

**Phases 4 to 7: not started** (the S fitter, faction choice and generators, the retrofits, markets).

Defaults taken (no questions asked):
- The plate is placed at the well's foot; Well depth is measured from its face up to the surface (the design's "plate at the
  foot of a well", without the plate moving itself down).
- Module membership comes from the plug flood at every rebuild, not from `CPart.Use`: an unsaved module needs no use
  record, and nothing new is sent or saved for it. Saved designs are plain blueprints with a `"module"` block.
- A module is saved as a module automatically when the selection is exactly one module (no separate "Save as module" button).
- F fits and takes off at once (no 5 s + 1 s a tonne timer); the attach tool's socket weld keeps its own fit time.
- The hand's mass limit is the player's physics gun lift, `ConstructEdits.LiftOf(player, creative)` (3 t in Survival, `ConstructGrab.SurvivalLiftCap`); the attach tool's socket weld, which knows no player there, uses the cap.
- `turret` is any gun on the module, fixed or on a joint.
- A Module Stand is the bench as well; F on a socket also fits a module stored on a stand of the same construct.
- A late refusal after a module was lifted off its stand (someone standing where it goes) leaves it loose beside the stand.
- Changing the room or style while a module is fitted is allowed; it is checked at the next fit.
- (Phase 2) The hole is the footprint plus 7 cm a side, not 2 cm: the liner's 5 cm walls stand in it. A part beside the footprint
  within 7 cm is notched.
- (Phase 2) The cut's hull thickness is the sum of each cut part's extent along the normal inside the well.
- (Phase 2) A save made mid-cut starts the cut again on load (the job is the server's; the plate keeps only its seconds).
- (Phase 2) A well opened before the cut (nothing in the column) gets no liner; the sweep is checked at the fit, not for parts welded
  onto a fitted module; each joint is swept alone with the others at rest.
- (Phase 2) The rim band and seam cut of `Renderer.Cuts.cs` are not needed: `HullHoles` leaves out the detail the tunnel reaches.
- (Phase 3) Pass-throughs run one way, ship to module (the plate's ports are inlets, the plug's outlets).
- (Phase 3) A thruster finds an engine plate as its mount only when it stands on the plug itself (or on a joint on it).
