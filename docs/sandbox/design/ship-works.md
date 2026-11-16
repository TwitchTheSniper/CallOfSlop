# Ship works: engines and turrets with insides (design)

Design only (2026-11-16). No code changed, nothing built, nothing run. Written for one builder (Opus, extra effort) who
takes the whole feature, phase by phase, a WIP commit after each step. Section 14 holds the open questions and Karl's
answers; an answer there overrides the text above it.

Karl's ask (2026-11-16): when a ship is designed, its engines and turrets have no interior component. Larger engines
should have an interior part, or a set of them, that the design has to make room for, different for each way an engine
makes thrust. An engine mounted inside the hull is armoured, protected and easy to service; a nacelle manoeuvres better
but is less protected and has to be serviced from outside (a space walk, or land and go out). Turrets get the same
trade: one that stays on the outside of the hull gives up ammunition, protection and power, one with an interior can
carry a bigger laser generator, a bigger autoloader or more ammunition, and a missile battery with an interior carries
reloads, where an external one fires what it holds until someone hangs more on it by hand.

Sources read: `ConstructThrust.cs`, `ConstructBoost.cs`, `ConstructDriveParts.cs` (`ParseThruster`),
`GameSandboxBreakdowns.cs`, `ConstructPower.cs` (head), `ConstructRadar.cs`, `ConstructWeapons.cs` (`CGun`, the
director and turret wiring), `ConstructAmmo.cs` (the function list), `ConstructOrdnance.cs` (the function list),
`ConstructShipAi.cs` (through torpedo-frigate.md), the generators `ShipParts.cs`, `FrigateParts.cs`,
`TorpedoFrigateParts.cs`, `MinerParts.cs` (the drive, gun, reactor and blueprint parts of each), the part files of
the four ships' drives, `Constructs/Categories.json`, `Constructs/Breakdowns.json`, the ship-design skill, and the docs
weapons.md (construct weapons, ammo feeds, air weapons, radar, ordnance racks, cook-offs), flight.md (jets, rockets and
repulsors), ship.md, frigate.md, torpedo-frigate.md, miner.md, SURVIVAL.md, INTERIORS.md.
Not verified, for phase 0 to check before building on it: how a turntable's base and top are told apart in the socket
links (section 4.2); whether anything limits how fast a battery's charge can be drawn (section 8.3); whether a broken
(disabled) part can be repaired today other than by the breakdown's mend (section 7); the Dray hauler's branch (PR 242,
described to me as frame, walls and doors only).

## 1. What exists today

| Thing | How it works now | Where |
| --- | --- | --- |
| A thruster | One part, role `thruster`: `force`, `burnRate` (or `power`), `spool`, `exhaust`, a `kind` (plain, air, rocket, solid, repulsor), an optional gimbal. It pushes while it is `Working`, fed (`FuelReaches`) and the construct has fuel (`ConstructThrust.Live`). Nothing else is needed | `ConstructThrust.cs`, `ConstructBoost.cs` |
| The ships' drives | Every one is a plain fuel thruster on the outside: Rust Bucket Drive 2 x 600 kN on the stern pods, Magpie Drive 2 x 900 kN on the nacelles, Halberd Main Drive 4.5 MN under the thrust frame, Zero Day Drive Array 9 MN on the stern. All burn about 3 l/s a MN (the Zero Day 2.5) | the four generators |
| What is in an engine room | Looks and power: the rust bucket's `Ship Engine Block` is role `frame` (a prop), the Halberd and Zero Day Reactors are `generator` parts for the circuit. No part in any engine room has anything to do with thrust | `ShipParts.EngineBlock`, `TorpedoFrigateParts.Reactor` |
| Breakdowns | Survival, the ship start's own ship only: a random thruster warns for 90 s, then fails (`Broken`, health kept). The attach tool at the part or at the `Engineer's Panel` (found by name) sees to it; the panel exists because the thrusters hang outside | `GameSandboxBreakdowns.cs` |
| A turret | A Turntable, a Motor Hinge, a gun, an Ammo Box bolted to the gun (300 rounds), aimed and fired from a seat through a Turret Module at a Gunnery Console. All of it stands on the hull's outside (the Halberd's stow in housings) | `Turret()` in the generators |
| Ammunition from inside | Possible but awkward: a feed link crosses a joint as a flexible section that stops at 135 degrees of bend and tears near 169 (about +-80 degrees of traverse); an Ammo Reel goes all the way round but is plugged by hand and has no routing | weapons.md "Ammo feeds" |
| Missiles | An Ordnance Rack (grid or drum) fires or drops what hangs on it. A weapon is hung by hand (the physics gun snaps it to a station: `ConstructOrdnance.TrySnap`). Nothing refills a rack. The Zero Day's bays hold a six-round drum inside each sponson, firing through a port | `ModularRack.cs`, `ConstructOrdnance.cs` |
| Radar and fire control | A radar part gives a scope; it locks only while wired into a working Fire Director. Missiles take the craft's lock at launch | `ConstructRadar.cs` |
| Lasers, capacitors | None. No beam weapon, no part that stores charge for a burst, no gun that draws power. The railguns are ordinary guns fed from an Ammo Box. SURVIVAL.md decides that burst costs hull space for capacitors; nothing builds it yet | - |
| A modular engine (land vehicles) | The precedent for this design: pieces that touch are one engine, found at the construct's rebuild (`ConstructEngine.Build`), with faults said on the look line | `ConstructEngine.cs` |

So today the outside of a ship does all the work and the inside is furniture. A design loses nothing by leaving the
engine room out.

## 2. The idea in five rules

1. **A big engine, a turret with an interior and a reloading launcher are assemblies.** The part on the outside is the
   **head** (a drive's nozzle, a gun on its turret, a launcher rack). Behind it, through the hull, are its **works**:
   machinery parts bolted on in line. What the head can do is set by the works behind it.
2. **Works are found by touch,** at the construct's rebuild, the way a modular engine's pieces are: from a head, down
   whatever it stands on, to a **mount** (a piece of hull that says works may be bolted to it), then every works piece
   bolted to that mount or to another works piece. No new link type, nothing per tick, nothing on the wire: every peer
   derives it from the parts.
3. **Small things stay whole.** A thruster of 500 kN or less (RCS, small manoeuvre and retro jets), a gun with its
   box and a rack on the hull work as they do now, with no interior. They are the convenient option and pay for it:
   small jets burn more for their push, a surface gun has what is bolted to it, a surface rack fires what it holds.
   **Every thruster over 500 kN has works** (Karl, 2026-11-16, answer 1): lift jets and big retros too. It is either
   mounted on the hull with its works inside, or a pod with them in its cowl.
4. **A pod is a drive with its works in the cowl.** One part, hung outside on a pylon: no hull space, a wide gimbal and
   a long arm from the centre of mass, but only the cowl's armour, and it is serviced at the part, outside.
5. **Everything is data.** Which kinds of works a head needs, what each piece serves, the classes and every number are
   in the part files and one table (`Constructs/ShipWorks.json`), so a new engine type is a few part files.

Rejected: joining works to their head by pipe. A pipe lets the works stand anywhere, so nothing puts the engine room by
the engines, and it would need a new fluid and a walk of the pipe graph. Touch forces the layout Karl asks for, reads
at a glance (the machinery is behind the bells) and makes a nacelle a different thing by construction.

## 3. The mechanism (code)

New file `Source/Constructs/ConstructWorks.cs` (partial `Construct` for the caches, a static `ConstructWorks` for the
build, the look lines and the report), called from the construct's rebuild after the thrusters, guns and racks are
built. The numbers table is `Source/Constructs/ShipWorksTable.cs` reading `Constructs/ShipWorks.json`, loaded and
refused with a reason like `WeaponParts.json` is.

### 3.1 Part data

A head says what it needs, a piece says what it gives. Both blocks are new top-level keys (add them to the known-key
list in `ConstructParts.cs` beside `"radar"`).

```jsonc
// On a head (a thruster, a gun, a rack). No block: it needs nothing, as today.
"works": {
  "needs": ["core", "pump"],      // kinds it must have behind it
  "service": "inside"             // "inside" (default) or "outside" (a pod: its works are in itself)
}

// On a works piece.
"worksPiece": {
  "kind": "core",                 // core, pump, exchanger, turbopump, manifold, conditioner, loader, lasergen, hoist, or "mount"
  "serves": 6000000               // what it carries, in its kind's unit: N of thrust, kW of beam, kg of ordnance; a mount has none
}
```

- A **mount** is any part with `"worksPiece": {"kind": "mount"}`: the generic Hull Mount (section 9), or a ship's own
  stern or wall piece that its generator flags. It gives nothing; it says "works bolted to me belong to the head bolted
  to me".
- A **pod** is a head whose file has both blocks: `works.service: "outside"` and no `needs`, or `needs` it fills
  itself. Simplest: a pod has no `needs` and only `service: "outside"`, which marks where it is repaired and that the
  panel can't mend it (section 7).

### 3.2 Finding an assembly

At a rebuild, for each head with `works.needs`:

1. Start at the head. Walk the socket links (`FirstLink`/`NextLink`/`OtherEnd`) through joint parts only (a gun's
   hinge and turntable) until a mount is reached. A drive bolted straight to a mount takes no steps.
2. From that mount, flood through every touching part that is a mount or a works piece. A generator flags mounts
   sparingly (the stern and the one wall the works stand against), so the flood stays small; cap it at 64 parts an
   assembly and log past that.
3. Sum `serves` per kind over the **working** pieces found. Heads that reach the same mount are one assembly and share
   it: the need of a kind is the sum over the assembly's heads (a drive's `force`, an emitter's rating).
4. The head's **cap** is the least, over the kinds it needs, of `min(1, served / needed)`. A kind that is missing
   altogether gives 0 and a problem text ("no Drive Core behind it").
5. **A thruster never falls under a quarter** (Karl, 2026-11-16, answer 2): its cap is at least `ShipWorks.json`'s
   `bareShare` (0.25), so a bare nozzle, or one whose works are all shot out, still pushes at 25% and the ship can
   limp. Below full works it burns at the jet's rate for what it pushes (3 l/s a MN), not the torch's. The floor is for
   thrusters only: a Laser Emitter with no generator fires nothing (the Laser Pod is the whole version), and an
   autoloader or a hoist that is missing simply isn't there.

Kept per head: `Cap` (a float on `CThruster`, `CGun`, and for a rack on the part's cached entry) and a `Problem` string
for the look line, made only at the rebuild. A part that breaks already marks the construct dirty, so a holed pump
drops the cap at the next rebuild with nothing done per tick. Bounded per body: one pass over the heads, a flood that
visits each mount and works piece once (keep a visited stamp per part, as the power rebuild's queue does).

### 3.3 Where the cap is used

`Find every reader` applies here. `ThrustForce` is read in more than twenty places and most of them plan with the
full figure. Add one helper, `Construct.ThrustOf(int k)` (the def's force times the thruster's cap), and move every
reader that means "what this thruster can push now" to it:

- `ConstructThrust.Thrusters` (the push, and `all`/`shown`), `BuildThrusters` (the mixes: a dead drive must not be
  counted on for yaw), `LiftCounter`, `LiftTrim`, `SpaceThrust`, `NavDrive`.
- `ConstructEffectors.cs` (the stabilizer's scan and gimbal authority), `ConstructFlight.cs:478`,
  `ConstructFlightPanel.cs:41`, `ConstructBoost.ForceNow` and its repulsor clamp.
- The fuel burn scales with level as now; a capped drive burns in step with its push (multiply `burn` by the cap too).
- A thruster's cap is never 0 (the quarter floor, 3.2), so `Live` is unchanged; the look line says why it is at 25%
  ("no Drive Core behind it: running at a quarter").
- Leave alone: the default flame's size (`ConstructParts.cs:981`, a def-time figure) and the parse.
- The flame's level (`ConstructThrust.Level`, the byte sent) is multiplied by the cap, so a starved drive burns low.

Guns and racks: sections 5 and 6 say where each cap lands.

### 3.4 What the player is told

- The look line of a head: `Drive: 9.0 MN, works 100%` or `Drive: no Drive Core behind it`, `Fuel Pump 1 of 2 working:
  50%`. Of a works piece: what it serves and which drive it is part of. ASCII, cached by a key as the breakdown look is.
- The `Engineer's Panel` look line lists each drive's state (section 7).
- `--check-vehicle FILE` prints one `works:` line a head: its kinds, what was found, the cap, and `REFUSED`-style text
  when a head has no works (a thruster at the quarter floor). The exit code counts a ship head with a cap under 1.

## 4. Engines

### 4.1 Types and classes

A drive's **type** is how it makes thrust, and decides which kinds of works it needs. Its **class** is its size, and
decides how much of each. Provisional numbers; all of them live in the part files and `ShipWorks.json`.

| Type | Today's `kind` | Works it needs | What the room is like | Character |
| --- | --- | --- | --- | --- |
| Jet (self-contained) | plain | none | - | RCS and small manoeuvre and retro jets. Thirsty: 3 l/s a MN as now. At most 500 kN a part (`jetMost`) |
| Torch drive | plain, on fuel | **Drive Core**, then a **Fuel Pump**, then a **Coolant Exchanger** as the class grows | A core on its bed with a walkway round it; pumps on the fuel side; the exchanger against the hull with its radiators outside | The ships' main drives, and every lift or retro jet over 500 kN. Efficient: 2 l/s a MN |
| Rocket | rocket (fuel and oxidiser) | a **Turbopump** for each propellant and a **Mixing Manifold** | Small and dense: pumps between the two tank feeds | Most thrust for its room, thirstiest, and the pumps burn |
| Ion drive | plain, electric | a **Power Conditioner** | Cabinets and bus bars; no fuel, a great deal of power | Little thrust, no tank: a hauler's cruise engine, and it competes with the weapons for the reactor |
| Turbojet | air | none | - | Breathes, so it is a pod by nature. Unchanged |
| Solid booster, repulsor, Anti-Grav | solid, repulsor | none | - | Unchanged |

| Class | Thrust of the drive part | Torch drive needs | Room to allow (guide for a designer) |
| --- | --- | --- | --- |
| 0, a jet | 500 kN or less | nothing | none |
| 1, light | over 0.5 MN, to 2 MN | Drive Core S | a bay about 3 by 3 m against the stern, 2.5 m high |
| 2, medium | 2 to 6 MN | Drive Core M, Fuel Pump M | a room about 5 by 4 m |
| 3, heavy | 6 to 20 MN | Drive Core L, Fuel Pump L, Coolant Exchanger L | a hall about 8 by 6 m, better two decks high |
| 4, capital | over 20 MN | cores, pumps and exchangers in number until they serve it | an engine hall the width of the stern |

Pieces serve by capacity (`serves`), so a class is a guide, not a rule in code: Drive Core S serves 2 MN, M 6 MN, L 12
MN; Fuel Pump M 6 MN, L 12 MN; Coolant Exchanger L 20 MN. `ShipWorks.json` holds, per type, the thrust above which
each further kind is needed (torch: core over 0.5 MN, pump from 2 MN, exchanger from 6 MN); the part kit's drive helper
reads it and writes the head's `needs`, so a generator never picks the list by hand. Two drives on one mount share one
core room, and a ship with two pumps where one would do keeps half its thrust when one is holed: redundancy is bought
with room.

A jet is whole but capped: the part kit refuses to generate a thruster in group `Ship` over `ShipWorks.json`'s
`jetMost` (500 kN) that has no `works` block, whatever it is for. The old Boost parts (Capital Jet and the rest) are
untouched: the Orbiter and Jumper climb on them, and the cap is on the new family, not the loader.

**Lift jets and retros over 500 kN** (answer 1) are drives like any other and come the same two ways:

- **Under the hull with works inside.** Lift jets hang on the keel, so flag the keel lengths as mounts: the flood
  passes through touching mounts, so one **lift machinery bay** on the lowest deck, standing on the keel, serves every
  lift jet on it. Need is summed over the assembly (3.2), so four 1.5 MN lifters ask for 6 MN of core: one Drive Core
  M, and each lifter's own `needs` is only a core since it is under 2 MN. A keel length shot away takes its jet and
  cuts the flood there, which is right. The same goes for retros through the bow.
- **As a pod.** A lift jet under a tank, a hopper or a nacelle, with nothing walkable above it, is a pod:
  `service: "outside"`, the cowl's armour, mended from underneath on the ground or on a space walk.

A designer may mix them: lift bay for the keel's jets, pods on the outriggers.

### 4.2 Hull drive or pod

The same drive comes two ways, and this is the trade Karl describes.

| | Hull drive (nozzle, mount, works inside) | Drive pod (one part on a pylon) |
| --- | --- | --- |
| Hull space | The works' room, at the stern | None |
| Armour | The works have their own and the hull's in front of them; only the nozzle shows | The cowl's only: about 40% of the armour and half the health of the same class's works |
| Service | At the piece, inside, under way, in orbit; warnings can be seen to from the Engineer's Panel | At the pod, outside: a space walk, or land. The panel reports it and can't mend it |
| Turning | Small gimbal (3 degrees), short arm: the ship turns on its RCS | Wide gimbal (12 degrees) on a long arm: it turns the ship with its drives, and a pod on a Motor Hinge vectors |
| Losing one | A holed pump or core lowers the cap; the piece is mended in place | Shot off, it is gone with its thrust; or disabled outside until someone goes out |
| Overdrive (4.4) | Yes, at the core | No: nobody is at it |
| Burn | 2 l/s a MN | 2.4 l/s a MN (a smaller core in the cowl) |
| Mass | Nozzle, mount and works: the most | About 70% of that |

The turning advantage is already real in the simulation: `BuildThrusters` works out each thruster's yaw and roll share
from where it sits, and a gimballed nozzle is commanded by the stabilizer's scan. The pod's gimbal and its place are
all it needs; no new flight code.

### 4.3 The pieces

Generic parts, section `Ship fittings`, names that name no ship (section 9). Sizes S, M and L as the tables above ask.

- **Drive Nozzle S/M/L** (the head; torch, 1.5, 5 and 10 MN): `works.needs` from its thrust. Bolts to a mount's outer
  face.
- **Hull Mount S/M/L**: an armoured ring plate, 30 cm thick, a socket on each face. The piece of hull a head goes
  through. The same part serves drives, launchers and (as the Turret Ring's trunk, section 5) turrets.
- **Drive Core S/M/L**: the reaction chamber on its bed. The biggest piece; the Overdrive setting is on it; a gauge.
  Takes the rust bucket's `Ship Engine Block` look for S.
- **Fuel Pump M/L**: on the fuel side, flammable (it joins the parts incendiary rounds light), a pressure gauge.
- **Coolant Exchanger L**: against the hull, with a radiator panel drawn on its outer face.
- **Drive Pod S/M** (1.5 and 5 MN): nozzle and works in one cowl, `service: "outside"`, a 12 degree gimbal, a service
  hatch drawn on its side where the attach tool is used.
- Later, with their types (phase 6): **Turbopump**, **Mixing Manifold**, **Power Conditioner**, **Rocket Nozzle**,
  **Ion Nozzle**.

A ship's own drive (the Zero Day Drive Array, the Halberd Main Drive) keeps its bespoke look and name and takes the
same `works` block; its works are the generic pieces unless its generator makes its own.

Every works piece: `onBreak: "disable"` (it stays, dead, to be mended, as tanks do), a tool of `welder`, a service
face with 1 m clear in front of it (section 10 makes the designer keep it), and the ship-fitting look rules (bolted
down, rails, worn paint).

### 4.4 Overdrive

A setting on the Drive Core (the configure tool, a new `CfgKey` at the end of the enum): 100 to 125%. The assembly's
cap is multiplied by it, the burn by its square. In Survival every overdriven minute counts as three on that ship's
breakdown clock and the next breakdown goes to that drive's works (section 7). It is the engineer's lever: a crew
member at the core buys a quarter more thrust for a chase and pays in fuel and a repair. A pod has none.

## 5. Turrets and guns

### 5.1 Surface mount or trunked mount

| | Surface mount (today's turret) | Trunked mount (a Turret Ring through the deck) |
| --- | --- | --- |
| What is outside | Turntable, hinge, gun, its box | The ring's top, the hinge, the gun |
| Ammunition | What is bolted to the gun: an Ammo Box (300) or a Crate (1,200), in the open, cooking off outside | A feed from a store inside (an Ammo Container holds 4,000 rounds a cubic metre), behind the hull, with an Ammo Switch as its fire door |
| Reloading | By hand, outside | From inside, at the store; an Autoloader behind the trunk shortens a heavy gun's reload |
| Lasers | A Laser Pod: emitter and a small generator in one | A Laser Emitter on the ring with Laser Generators below it: several times the beam |
| If it is hit | The whole turret and its ammunition are on the outside to be shot off | Only the head shows. The magazine is safe from the outside and a danger inside if the hull is opened |
| Cost | None but its mass | A hole in the deck, the room under it, the magazine's place |

### 5.2 The Turret Ring

**Turret Ring S/M/L**: a turntable (a joint part made beside the Turntables by the part kit, the same strength
model) whose base carries a **trunk**: a drum going 0.6 to 1.5 m down through the deck, flagged as a mount, with
works sockets on its sides and an **Ammo in** port at its foot.

- **The ring feeds its guns.** A feed link from a store ends at the ring's `Ammo in` on the still side, and every gun
  standing on that ring draws from it, with no link across the joint and so no bend limit. In `ConstructAmmo.FedFrom`
  this is a new first source: "the ring this gun stands on has a working feed with rounds behind it". The order after
  it is unchanged (a feed, a socket's magazine, the gun's magazine, a bolted box). The gun's ready rounds and
  `feedReload` are as for any fed gun.
- **Works reach the guns the same way**: section 3.2's walk goes from the gun down its hinge to the ring, which is the
  mount, and floods the pieces bolted to the trunk. Phase 0 checks how a turntable's base side and top side appear in
  the socket links, since the flood must start from the base.
- Power and data already cross a turntable (the Halberd's gun stations wire through one); nothing new.
- The ring's size limits what stands on it by the turntable's existing rating.

### 5.3 The Autoloader

**Autoloader S/M/L** (`kind: loader`), bolted to the trunk. Needs the ring fed. It serves a ring of its size.

- A gun with a magazine of six or fewer (the Cannon and its like): its reload is multiplied by `loaderReload` (0.4).
- A belt gun: no `feedReload` pause between its 3 s lots; it fires on until the store is empty.
- No autoloader: the gun reloads at its own rate. No ring feed: the autoloader does nothing and its look line says so.
- It lands on `CGun.ReloadTicks` and the feed's reload where `BuildGuns` sets them (at the rebuild, not per shot).

### 5.4 Lasers (new weapon kind; phase 5)

Nothing of this exists, so it is the biggest piece of new work and comes last.

- **Laser Emitter S/M/L**: a gun (role `gun`, `fx: "beam"`, a new `GunFx` at the end of the enum) with
  `works.needs: ["lasergen"]` and a rating in kW of beam. It is hitscan like any gun; a beam does its damage every
  `Interval` as rounds do, so the existing fire path, armour and the gunner's aim all apply. No ammunition.
- **Laser Generator S/M/L** (`kind: lasergen`, `serves` kW): below the ring. The emitter's damage a second is the
  served beam power times `ShipWorks.json`'s `beamDamagePerKw`, up to the emitter's rating.
- **It fires from the capacitors**: each shot takes `gun.energy` kJ from the construct's charge (section 8). No charge,
  no shot, and the HUD line says "capacitors flat".
- **Laser Pod**: emitter and generator in one part for a surface mount, at about a fifth of the Laser Emitter S's beam.
- A beam needs a look: a line from the muzzle to the hit for a few frames, drawn from the fire event as tracers are.

## 6. Missile batteries

- **A launcher on the hull** is an Ordnance Rack as now: it fires what hangs on it and is re-hung by hand, outside.
- **Reload Hoist S/L** (`kind: hoist`, `serves` kg of one weapon): works behind the launcher's Hull Mount. Ordnance
  Racks bolted to the hoist (grids or drums, of any shape that takes the weapon) are its **magazine**.
- While the launcher has an empty station, the hoist has power and a magazine rack holds a weapon that fits the
  station (`ConstructOrdnance.LoadWhy` is the existing test), the hoist moves one: after `hoistSeconds` (6 s for a 25
  cm weapon, growing with the square root of its mass) the part leaves its magazine station and is put on the
  launcher's (the existing pull-off and snap: `PullOff`, `TrySnap`'s placing, one structure change). One move at a
  time a hoist, looked at a few times a second, not every tick.
- The launcher rack gets a `works.needs: ["hoist"]` only by choice: a rack with no hoist is today's rack. So the cap
  idea isn't used here; the hoist is found by the same walk and simply acts.
- The magazine is ordnance inside the hull: the cook-off rules already cover it (a rack that breaks with hurt
  ordnance on it takes everything with it). That is the risk that balances the reloads, and a reason to put the
  magazine behind armour and a bulkhead door.
- The Zero Day's bays already are an interior launcher (a drum firing through a port). With a hoist they could carry
  a second drum as reloads (section 11).
- The AI crew needs nothing: it presses the same buttons, and the hoist refills by itself.

## 7. Crew, damage and servicing

What a crew member can now do, and where:

| Where | What | Tool |
| --- | --- | --- |
| Engineer's Panel | Read every drive: its works, its cap, what is groaning or broken and where. See to a **warning** on a hull drive. Not mend a pod | Look; attach tool |
| A works piece (inside) | See to a warning; mend a broken piece (metal, by its size); read its gauge | Attach tool |
| Drive Core | Set Overdrive | Configure tool |
| A drive pod (outside) | See to a warning, mend it: the only place it can be done | Attach tool, on a space walk or landed |
| The magazine | Carry ammunition into the store; hang reloads on the magazine racks | Hands, physics gun (as now) |
| Ammo Switch on the feed | Choose the round type; it is also the fire door (existing) | Lever |
| Gunnery station | Aim and fire (existing) | Seat |

Damage:

- A works piece is a part: health, armour, the hull pieces in front of it stop rounds first (rounds hit the nearest
  part; only penetrating rounds go on, losing damage at each). So "armoured by being inside" needs no new rule.
- A piece at zero health is disabled, not knocked off. Its `serves` is gone, the cap falls at the rebuild, the look
  line and the panel say which piece. With two of a kind the drive runs at half.
- Fuel Pumps and Turbopumps are flammable. A Drive Core that breaks only stops (no blast): open question 5.
- A pod is one part with the cowl's armour. Disabled, it gives nothing until mended outside; shot off, it is a wreck.
- A magazine inside is ordnance or bulk ammo: its cook-off is the existing one.

Servicing (extends `GameSandboxBreakdowns.cs`):

- **Breakdowns move onto the works.** Where a drive has works, the breakdown picks one of its works pieces (or the pod
  itself) instead of the nozzle; drives with none (jets) are picked as now. `BreakdownPart` finds the part again by
  def and place as it does.
- **The panel rule changes** from "sees to anything" to: a warning on a works piece inside can be seen to at the
  panel; anything on a `service: "outside"` part (a pod), and on a whole jet, which hangs outside too, is reported
  with where it is and seen to at the part. That is the whole of "easy to service" against "go outside", in one flag.
- **Mending a piece broken by damage** uses the same act as a failed breakdown (the attach tool's Fire, `MendPay`),
  with a cost by the piece's size from `ShipWorks.json`. Today that act is only for a part that broke down (`Broken`
  with health kept); phase 1 widens it to a works piece at zero health and restores its health. Check first whether
  another repair path already covers disabled parts, and use it if so.
- **Every crewed player ship breaks down** (Karl, 2026-11-16, answer 4), in Survival, not only the ship start's own.
  What changes in `GameSandboxBreakdowns.cs`:
  - Today's state is one warning for one ship (`_opShip`, `_bdNext`, `_bdDef`, `_bdPos`, `_bdLeft`, `_bdCreak`). It
    becomes a ship's own: the clock and the warning under way are kept on the `Construct` (a few fields, as `LiftStamp`
    is; no dictionary that grows with bodies).
  - Which bodies: walk the players, not the bodies. Each look (four a second, as now), for every living player take
    the body they ride or sit in, once a body, and tick it if it is a **ship**: it has a thruster over `jetMost` or a
    Flight Stabilizer, isn't a wreck, and has no AI crew fighting for it (`GameSandboxShipAi`). So the cost is bounded
    by the players, whatever the world holds, and an enemy frigate doesn't break down until it is taken.
  - A ship's first breakdown comes `firstMinutes` after it is first crewed, then by `minMinutes` to `maxMinutes`, all
    crewed time, as now. `maxFailed` is per ship. Overdrive (4.4) runs that ship's clock faster.
  - The clocks aren't saved today and needn't be yet: a loaded ship starts its wait again.
  - The client's side (`_bdSeenBody` and the messages) is already by body; check that a player hears only the ship
    they are aboard, and that two ships warning at once don't share the one seen-part slot.
  - `Breakdowns.json`'s comment and ship.md "Breakdowns" say "the ship start's own ship": change both.

## 8. Power and the capacitor rule

SURVIVAL.md decides that the reactor is capped and burst costs hull space for capacitors. This design gives that
rule its first parts.

1. **`gun.energy`** (kJ a shot, a new key in the `gun` block; 0 for every gun today): taken from the construct's
   charge when the shot is fired. Short of it, the gun doesn't fire.
2. **Capacitor Bank S/M/L**: a part that holds charge (role `battery`, sized for a burst: much charge for its volume,
   heavy, and it loses its charge when broken). The reactor refills it at the reactor's rate. So a ship's sustained
   fire is its reactor, its burst is its capacitor room, and the room is the cost.
3. Phase 5 checks whether anything limits how fast charge is drawn. If nothing does, a battery and a capacitor differ
   only in numbers, which is acceptable to start with.
4. **The railguns take it first.** The Halberd Spinal Railgun and the Zero Day Railgun get an `energy`, and each ship
   a Capacitor Bank in engineering sized for a few shots. Lasers follow.
5. The ion drive draws from the same circuit, so its ship chooses between cruising and shooting.

## 9. The parts system

- **Generator**: a new part kit generator `Source/PartKit/Generators/ShipWorksParts.cs` (`--gen-parts ship_works`),
  writing to `Constructs/Parts/Fittings`. It owns the generic family: Hull Mount, Drive Nozzle, Drive Core, Fuel Pump,
  Coolant Exchanger, Drive Pod, Turret Ring, Autoloader, Reload Hoist, Capacitor Bank, the laser parts.
- **A helper for ship generators** in the same file: `Works.NeedsFor(type, force)` (the `needs` list from the table),
  `Works.Mount(part)` (flags a ship's own piece as a mount and gives it inner works sockets on a solid face) and
  `Works.DriveRoom(bp, mount, force)` (hangs the standard set in a row and returns the steps), so the four ships'
  generators share one way of doing it.
- **Names and listing** (Karl, 2026-11-16): the family is generic, so it is in section `Ship fittings` with names that
  name no ship and is listed in the Parts tab. A ship's own drive stays ship-named and unlisted. `Categories.json`
  needs no new rule for that; add placing rules so the family sorts into Ship fittings' sub-sections (Drives, Drive
  works, Turret works).
- **Recipes**: none in this work. Ship building is TwitchTheSniper's production system and agents don't change it
  (SURVIVAL.md). The parts are in Creative and in blueprints; raise their recipes with Twitch (question 6). Do not let
  the Assembling generator pick them up by a default: the table sits near `MaxRecipes`.
- **Limits**: a part holds 256 boxes and 64 sockets, and a name 32 characters ("Coolant Exchanger L" fits).
- **Checks**: `--check-vehicle` gains the `works:` lines (3.4). `--check-building` already proves a standing person
  reaches doors, seats and stores; add works pieces and the Engineer's Panel to what it must reach on a ship whose
  heads are `service: "inside"`, which turns "easy to service" into something a design can fail.
- **Protocol**: caps are derived, so they need no message. Bump `ProtocolVersion` where the wire does change: the
  Overdrive `CfgKey` if settings are sent by index, the mend act if it gets a new `CActKind`, the beam `GunFx`. One
  bump a phase that needs it, with its history line.
- **Saves**: nothing new is saved; old saves need not load (Karl, 2026-11-15).
- **Performance**: everything is found at the rebuild and bounded per body (3.2). Per tick: one multiply a thruster,
  a hoist's look a few times a second, nothing allocated. A reload is a structure change, at most one a hoist at a
  time. Measure the rebuild with the biggest ship (the Zero Day, 247 parts) and with the fitted Titan Walker to show
  that a body with no heads pays nothing.

## 10. The ship-design skill

`.claude/skills/ship-design/SKILL.md` gains a section after 1c, "1d. Engines and weapons have insides", and three
lines elsewhere. The text to add, in substance:

- **Decide each drive before the hull.** For every main drive: its type, its thrust, and hull drive or pod. Read the
  class table (this file, 4.1) for the room it needs and put that room in the stations: a hull drive's works stand
  directly inboard of its nozzle, so the drive room is the last room before the stern, as wide as the bells.
- **A hull drive**: the nozzle on the stern piece's outer face, the stern piece (and the one wall the works stand
  against) flagged as mounts with `Works.Mount`, the works hung with `Works.DriveRoom`. Each works piece has 1 m
  clear in front of its service face and is reached standing from the engine room's door. The Engineer's Panel is in
  that room.
- **A pod**: on a pylon outside the skin, clear of the hull by its gimbal's swing, with a way to reach it (a handrail
  or a walkway on the pylon, and the airlock on that side). Say in the ship's doc that it is serviced outside.
- **Jets** of 500 kN or less (RCS, small manoeuvre jets) are whole and need nothing inside. **Every lift jet and
  retro over 500 kN** is a hull drive too (flag the keel or bow as its mount and give the lifters one lift machinery
  bay on the lowest deck) or a pod where nothing walkable is above it.
- **Each turret**: surface or trunked. A trunked one needs the deck under it clear for the trunk (its drum's depth
  and 0.5 m round it), a place for its works on the trunk, and a magazine: inside the armour, off the corridor, behind
  a door, its feed through an Ammo Switch. Gun consoles stay within 8 m of their Turret Modules.
- **Each launcher**: surface, or with a Reload Hoist and magazine racks behind its mount. A magazine is a room with
  walk-in space to hang reloads, sized by the racks, behind a bulkhead door.
- **Burst weapons need capacitors**: a Capacitor Bank's room in engineering for every railgun or laser.
- **Check**: `--check-vehicle` shows every head at 100% and no head without works; `--check-building` reaches every
  works piece; `--flight-check` again after the works' mass went in.
- In section 1 ("Plan the shape before the parts"), add the drives, turrets and their rooms to what is written down
  first. In "What a spaceship has" (1c), add the drive room, magazines and trunks. In the gotchas, one line: a ship
  with bare nozzles and no works has a quarter of its thrust, which seldom lifts it.

The vehicle kit's generators (`Source/VehicleKit`, ground vehicles and aircraft) and the Interiors planner
(`Source/Interiors`, the Titan Walker) are not changed by this plan. Their engines are modular engines and their jets
are under the size that needs works. If the planner later lays out ships, its `RoomTypes.json` gets a drive room and a
magazine with the sizes above as keep-outs.

## 11. Migrating the ships

Each in its own generator, never the output; `--flight-check` before and after, since mass and thrust both move.
Thrust figures stay as they are unless the flight check says otherwise.

| Ship | Now | After | Notes |
| --- | --- | --- | --- |
| **Rust Bucket** (`ShipParts.cs`) | Two 600 kN drives on the stern pods; four 800 kN lift jets under the pods and saddles; a prop engine block in the engine room | **Pods** (Karl, answer 7: its pods are nacelles): the two Rust Bucket Drives and the four lift jets take `service: "outside"`, the drives a 12 degree gimbal. The RCS thrusters are jets. No works inside; the `Ship Engine Block` stays a fitting (it may become the ship's generator later) | The start ship teaches the nacelle's price: a breakdown is mended outside, on a space walk or landed. The Engineer's Panel now only reports (which part, where); change the breakdown's messages to say so (`BreakdownHeard`), and try the first breakdown (4 crewed minutes in) from waking to mended outside, in orbit and landed. Add a handrail and a step by each pod |
| **Magpie** (`MinerParts.cs`) | Two 900 kN drives on the nacelles' tails | **Pods**: each Magpie Drive takes `service: "outside"` and a 12 degree gimbal; the nacelle gets a handrail and a hatch by the drive. The four 1.2 MN lift jets under the hoppers and nacelles are pods too; the 500 kN retros stay whole | The miner teaches the other half: a breakdown means going out on the pylon. Its engine deck stays a power room. No works to place |
| **Halberd** (`FrigateParts.cs`) | 4.5 MN Main Drive under the thrust frame; four 500 kN manoeuvre jets | **Hull drive, class 2**: the thrust frame is the mount; Drive Core M and Fuel Pump M on the engineering deck over the drive ring (the reactor and board move to the walls). The 500 kN manoeuvre jets are at the figure, not over it, and stay whole. PDCs: two of the four on Turret Rings fed from an Ammo Container in the armoury, two left as surface mounts in their housings. The railgun takes an `energy` and a Capacitor Bank in engineering | The engineering deck is 9.5 by 8.5 m and already crowded: lay it out again rather than squeeze. It also shows both turret kinds on one ship |
| **Zero Day** (`TorpedoFrigateParts.cs`) | 9 MN Drive Array on the stern; engineering in the lower deck aft | **Hull drive, class 3**: a drive room at the aft end of the lower deck against the stern (the stern and that wall are mounts), with Drive Core L, Fuel Pump L and Coolant Exchanger L; engineering keeps the reactor forward of it. The four 1.5 MN lift jets: the keel lengths are mounts and a lift machinery bay with a Drive Core M stands on the keel amidships on the lower deck. The two 1.2 MN retros: the bow is their mount with a Drive Core M in a small bay under the bridge if `MustFit` allows, else they become pods in the bow's cheeks. PDCs on Turret Rings with one magazine amidships. Railgun: `energy` and capacitors. Torpedo bays: unchanged at first; a second drum and a Reload Hoist a bay is the stretch (phase 4) | The reference ship: it should show the whole idea. `MustFit` will say whether the drive room fits inside the skin at the stern; if not, shorten the stern tank, and share its fuel out as the keel does |
| **Dray hauler** (PR 242) | Frame, walls and doors only | Design it with this plan from the start: an Ion or torch hull drive with a drive room, no weapons or surface mounts only | Whoever picks the Dray up reads section 10. If it merges before phase 1, it gets today's thrusters and joins the phase 2 migration |

The crewed Zero Day follows the Zero Day (one blueprint builder with a flag). After the last ship: regenerate the
blueprints once, run `--preview-gallery` and republish only the changed models to the one Ship gallery (the skill,
section 4), and update ship.md, frigate.md, torpedo-frigate.md and miner.md.

## 12. Phases

Each phase builds, is tried by hand in a throwaway run, and ends with a WIP commit; the checks named are commands,
not new self-test sections (the rule: tests only when asked, the suite doesn't grow). Run the existing live sections
that name what a phase touches before its merge (`--selftest-list | grep -i "thrust\|flight\|gun\|ammo\|ordnance"`,
then `--selftest-only`). If something fails that a command can't catch, one section may be made for it in
`SelfTest.Archived`, run by name.

| Phase | Builds | Tried by |
| --- | --- | --- |
| **0. The mechanism** | `ShipWorks.json` and its table; the `works` and `worksPiece` blocks; `ConstructWorks.Build` at the rebuild; the look lines; `--check-vehicle`'s `works:` lines. No part uses it yet, so no ship changes | `--check-construct-parts` clean; a hand-written test blueprint (a nozzle, a mount, a core) reports 100%, and 25% with the core taken out; rebuild time on the Zero Day and the Titan Walker unchanged |
| **1. Drives** | `ship_works` generator: Hull Mount, Drive Nozzle, Drive Core, Fuel Pump, Coolant Exchanger, Drive Pod. The cap in every thrust reader (3.3). Burn figures. The panel's report, the service flag, mending a broken piece | A demo blueprint of each class flown in a window: full thrust; shoot the pump, half or a quarter; mend it; a pod's failure refuses at the panel. A joined client sees the same look lines and flames. `--flight-check` on the demos |
| **2. The ships' drives** | Rust Bucket, Magpie, Halberd, Zero Day as section 11 (drives only). Breakdowns onto works and onto every crewed player ship (section 7). The skill's new section. The four ship docs | Each: `--gen-parts`, `--check-vehicle` (works 100%, 0 coplanar), `--selftest-only "No z-fighting"`, `--check-building`, `--flight-check` against the old figures, `--conduit-check`. The rust bucket start in a window to a first breakdown, mended outside at the pod; a second ship (a Halberd from the Blueprints tab, boarded) to its own first breakdown, seen to inside; a host and a joined client aboard different ships. `--ship-ai-check` for the Zero Day. The gallery republished |
| **3. Turret Rings and autoloaders** | Turret Ring (the joint and its trunk), the ring feed in `ConstructAmmo`, Autoloader. The Halberd's and Zero Day's PDCs as section 11 | A ring fed from a container fires through 360 degrees with the store emptying; cut the feed, 3 s of rounds left; an autoloaded cannon's reload against a bare one; host and client. The two ships' checks again |
| **4. Reload Hoists** | Reload Hoist, its magazine racks, the move. Optionally the Zero Day's second drums | A launcher of four with a magazine of eight fires twelve with nobody outside; power off, it stops; a magazine hit cooks off inside (`--bench-cookoff` for the cost); a client sees each reload arrive |
| **5. Energy and lasers** | `gun.energy`, Capacitor Bank, the railguns' draw; then Laser Emitter, Laser Generator, Laser Pod, the beam's look | A railgun fires its burst and waits on the reactor; a trunked laser against a Laser Pod on the same target; capacitors shot out, no fire. `--ship-ai-check` (its railgun still fires) |
| **6. The other types and Overdrive** | Rocket and Ion works and nozzles; the Overdrive setting; the Dray if it is waiting; docs folded into `docs/sandbox/constructs/` (a new `ship-works.md` there, indexed in CONSTRUCTS.md) and this file marked history | A demo of each type; overdrive's thrust, burn and breakdown clock |

Phases 0 to 2 are the engines and stand alone. 3 and 4 are independent of each other. 5 is the largest and newest.
Each phase's branch gets `merge-reviewer` before it merges, and phases 2 and 3 `look-critic` on `--preview-shot`
sheets of the changed rooms.

## 13. Risks

- **The flight tuning moves.** Works add mass low and aft; pods lose it. Every migrated ship is re-checked and its
  lift jets trimmed if the hover hold leans.
- **Readers of thrust.** A reader left on the full `ThrustForce` makes a stabilizer count on a dead drive. Grep it
  again at the end of phase 1.
- **The AI crew and the autopilot** (`ConstructShipAi`, `GameNav`) fly by `NavDrive` and the stabilizer: both go
  through the cap once 3.3 is done. Fly `--ship-ai-check` after phase 2.
- **Room in the hulls.** The Halberd's engineering deck and the Zero Day's stern are tight. Expect to move rooms.
- **A reload is a structure change** and renumbers parts: the hoist finds its parts again by def and place after each
  move, as the breakdown does.
- **Too many kinds at once.** If time runs short, phase 6's types can wait; the mechanism doesn't change for them.

## 14. Open questions, and Karl's answers

The builder goes ahead on the recommended answer for any still open.

1. **Which engines must have insides?** Answered: B, everything over a thrust figure, lift jets too. (A was main
   drives only.)
2. **A big nozzle with no works behind it:** Answered: B, it runs at a quarter. (A was that it doesn't run.)
3. **Lasers and capacitors, which don't exist yet:** Answered: A, in this plan, as its last big phase. (B was a
   plan of their own later.)
4. **Breakdowns:** Answered: B, every crewed player ship in Survival. (A was the ship start's own ship only.)
5. **A Drive Core that is destroyed:** A (recommended): the drive stops, no blast. B: it goes up like a fuel tank.
6. **Recipes for the new parts:** A (recommended): none here; raised with TwitchTheSniper for the production system.
   B: the part kit writes Assembling recipes for them now.
7. **The rust bucket:** Answered: B, it keeps its two pod drives as true pods, serviced outside. (A was one centre
   hull drive with a real engine room.)

Answers:

- **1: B** (Karl, 2026-11-16, decision card). Every thruster over a thrust figure has works, lift jets and retros
  included. The figure is `jetMost`, 500 kN, picked so that RCS thrusters (80 and 250 kN), the Halberd's manoeuvre
  jets and the Magpie's retros (500 kN) stay whole and every lift jet on the four ships (800 kN to 1.5 MN) does not.
  Sections 2, 4.1, 10 and 11 are revised for it. The figure is Karl's to move.
- **2: B** (Karl, 2026-11-16, decision card). A big engine with no machinery behind it runs at a quarter
  (`bareShare` 0.25), and so does one whose works are all destroyed. Section 3.2's step 5 and 3.3 are revised for it.
- **3: A** (Karl, 2026-11-16, decision card). Lasers and capacitors are built in this plan, as phase 5. Phase 6
  (the other drive types and Overdrive) is small and may be done before it, so that lasers are the last thing built.
- **7: B** (Karl, 2026-11-16, in the thread: "The Rust Bucket pods are nacelles, so they have the downside that you
  have to go outside and mend them or land it"). The rust bucket's drives and lift jets are pods. Section 11's row is
  revised. It follows that no ship shows a class 1 hull drive; the Halberd and the Zero Day show hull drives.
- **4: B** (Karl, 2026-11-16, decision card). Breakdowns happen on every crewed player ship in Survival. Section 7's
  last point is revised with what that changes in the code. It is done in phase 2, with the move onto the works.
