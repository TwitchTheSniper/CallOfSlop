# Weapons

Covers construct weapons, merged receivers, weapon builder parts in Sandbox, the Gun Workbench. Part of the construct guide: index in ../CONSTRUCTS.md.

## Construct weapons (milestone 3)

Decision 10: construct weapon parts like the Zombies weapon builder's, but assembled in the world into fixed or turreted
guns on vehicles and bases. Board item dev-sandbox-m3, part 1 of 4 (the developer approved it 2026-10-29). Everything is
a part fitted with the **attach tool** like any other (no workbench, decision 2), and every part's weight counts.

### The parts (`Constructs/Parts/Weapons`)

- **Guns on a weapon builder receiver** (`"gun": { "receiver": ..., "action": ... }`): **Rifle Receiver** (the
  Workhorse, full auto), **Heavy Receiver** (the Thumper chain gun, heavy calibre) and **Shell Receiver** (the Street
  Sweeper). Each is a 20-26 cm block with six sockets: **front** (a barrel or a splitter), **left** (a feed), **right**
  (ammo), **top** (an optic), **bottom** (its mount) and **back**. The gun parts bolted to it are put together as the
  weapon builder would (a `BuildNode` tree: its action, the feed, the ammo, the barrel or splitter with its barrels, a
  muzzle device on each barrel's front) and checked by the weapon builder's own socket rules (`BuildRules.SocketWhy`: a
  shell feed on a rifle receiver is refused with the workbench's reason); its numbers come from the weapon builder's
  stat pipeline (`BuildStats.ReceiverStats`: damage, fire rate, magazine, reload, spread, range, pellets, its flags).
- **Gun parts** (`"gunPart": { "part": ... }`, a weapon builder part by name): **Gun Barrel** (Standard Barrel), **Long
  Gun Barrel**, **Twin Splitter** (Y-Splitter: two front sockets for two barrels), **Box Magazine** (30, rifle), **Belt
  Feed** (150, rifle or heavy), **Shell Feed**, **FMJ / AP / HE / Incendiary Rounds** (FMJ, Armour Piercing, Explosive
  Tip, Dragon's Breath), **Gun Muzzle Brake**, **Gun Suppressor** and **Gun Scope** (4x Scope: a gun with an optic has
  60% of its spread). A receiver with no ammo part fires FMJ.
- **Heavy construct guns** (their own numbers in the `"gun"` block; original designs): **Mounted MG** (24 damage, 600
  rpm, 100 a belt), **Autocannon** (explosive 30 mm shells, 150 rpm, a 1.8 m splash), **Cannon** (one 260-damage
  explosive shell, a 3.5 m splash, 3.5 s reload, a big kick back), **Rocket Pod** (six rockets, a slow smoke streak and
  a 3 m blast each) and **Flamethrower** (a 9 m gout of incendiary fire). Each mounts by its **bottom** socket, has
  sockets for a box on its back, left, right and top, and takes an ammo gun part bolted to it (the weapon builder's
  universal ammo: AP rounds make the MG penetrating, HE explosive).
- **Ammo Box** (300 rounds) and **Ammo Crate** (1,200), and the **Feed Chute** (a 0.5 m pipe, like a fuel line).
- **Wood Panel**: a cheap 1 m plank panel that burns (for targets; material `wood`).

| `gun` key | Meaning |
| --- | --- |
| `receiver`, `action` | A weapon builder receiver (its numbers come from the parts bolted to it) and its action (default: the receiver's stock action). Not with the numbers below. |
| `damage`, `rpm`, `spread`, `range`, `mag`, `reload`, `pellets` | A heavy gun's numbers (`spread` in degrees, the whole cone; `range` the full-damage range in metres). |
| `flags` | `penetrating`, `explosive`, `incendiary`. |
| `blast`, `blastDamage` | An explosive round's splash: radius (m) and damage at the centre (default: its damage). |
| `maxRange` | How far its rounds reach at all (default 300 m; the flamethrower's 9). |
| `muzzle` | Where its rounds come out (part frame; -Z is forward). |
| `kick` | The recoil a shot, a barrel (N s): an impulse back along the barrel at its tip, through the joints under the gun and the body. |
| `fx`, `soundOf` | `bullet`, `cannon`, `rocket` or `flame` (how its tracer and flash look); the weapon file whose report it uses. |

An ammo box's block: `"ammoBox": { "rounds": 300, "cookOff": { "radius": 3.5, "damage": 260 } }`. A gun part's:
`"gunPart": { "part": "Long Barrel" }` (barrels, splitters, feeds, ammo, muzzle devices and optics only).

A bomb (any role): `"explosive": { "radius": 14, "damage": 260, "fuse": 1, "knockback": 18, "crater": 5 }`. When the part
breaks it is armed, and after `fuse` seconds it goes off where it broke with an explosive prop's blast (players, props,
buildings, constructs, fireballs; `crater`: the crater it digs, as a prop's "crater"), credited to the construct's owner,
so one bomb sets off the next. The Aerial Bomb and Small Nuke parts (Weapons > Bombs) use it.

### How a gun works

- **Its feed.** A gun is fed by the ammo boxes **bolted to it, or to any of its gun parts** (a receiver's magazine), or
  at the end of a chain of **feed chutes** (up to four boxes, the nearest first). It loads a magazine's (or a belt's)
  worth from them, taking the reload time, as soon as it is empty and a box has rounds, then fires those; a gun with no
  box fires what it has loaded and then nothing. The HUD and the look line say "no ammo box on its feed", "reloading",
  "out of ammo" or "24/30, 270 in the boxes".
- **Its trigger.** A gun is a logic **input** (it takes up to 4 wires): with anything wired into it, it fires while the
  wires add up to 0.5 or more (a switch, a button, an input reader on a driver's fire key, a proximity sensor, a timer).
  A [Turret Module](joints-and-logic.md#control-modules) linked into it is such a wire: its seat's sitter fires it with their fire key. A
  seat with no module fires nothing, wherever it sits. Every shot fires each barrel's pellets at once, at the
  gun's rate; semi-automatic and burst actions fire at their rate while held (no trigger discipline on a mount).
- **Its aim.** A **fixed** gun (on the body) points where its construct points. A gun on a **turntable** (and on a **motor
  hinge** for elevation) is pointed by the **look** of the gunner in the seat on its Turret Module: every turntable and motor hinge up the gun's chain turns,
  each about its own axis, until the gun points at what the gunner looks at (a ray from the gunner's eye along their
  look: the map, another construct or a player, else the gun's reach), so the rounds go where the crosshair is however
  far the gun sits from the eye. A hinge lying across the gun only tips it up and down (it never swings over the top to
  point behind; the turntable does the bearing). A seated gunner overrides the wires on those joints (the Gunner Turret's
  lever turns it when nobody sits there). The gunner's own view no longer turns with a seat that the gunner points:
  they look where they look and the turret follows.
- **A sentry.** A gun with a **proximity sensor** wired into it points its turntables and motor hinges (unwired ones)
  at the nearest player in the sensor's range and holds fire until it points within 8 degrees of them (it aims at the
  chest). The sensor's output is the trigger, so it fires only while someone is in range.
- **Rounds** are resolved at once (hitscan; a cannon shell or a rocket is drawn travelling at its `speed` but lands at
  once): the nearest of the map, a construct part (never its own construct's) and a player, bot or zombie. A part takes
  the round's damage less its armour. **Penetrating** rounds go on through the part (75% of their damage left, less 1.5
  times its armour; up to 3 parts), so an AP round through a plank panel hits the block behind it. **Explosive** rounds
  splash where they stop (every construct part in range, falling off with distance, and players at half that, walls
  shielding). **Incendiary** rounds set **flammable** parts alight (wood, rubber, fuel tanks, ammo boxes): a burning part
  burns for 12 s, losing a tenth of its health (plus 2) a second whatever its armour, and half-way sets flammable parts
  bolted to it alight. Players hit by incendiary rounds burn as from the weapon builder's guns.
- **Cook-off.** An ammo box holed to zero health (shot, blasted or burnt) blows up 0.35 s later, by how full it was
  (radius and damage from its file, half to all of it): a blast that can set off the box next to it. A **fuel tank** that
  gives way while burning goes up too (by the fuel in it).
- **Who gets the kill**: the gunner, else the player the construct belongs to (a sentry's kills are its owner's), else
  nobody (the victim's own, like being run over by a driverless car).
- **Weight**: guns, boxes and gun parts weigh what their files say, like any part (the Hood Gun Buggy is 46 kg
  heavier than the Buggy without its tow hitch).
- **Recoil** (board item gr01; `ConstructWeapons.Recoil`, `ConstructPhysics.AddRecoil`): each shot pushes back along
  the barrel with the gun's `kick` (N s) at each barrel tip (a splitter's every muzzle; a merged receiver's kick is
  already scaled by the merge rules), as a real impulse on the gun's segment. On the body it pushes and twists the
  vehicle (an off-centre cannon yaws a buggy, a high one pitches it back). Up a joint chain each turntable, motor hinge
  or piston takes the part about (or along) its axis as a jolt, and its motor holds or gives by the strength model: a
  stock turntable kicks a few degrees under a cannon and comes back, an elevation hinge climbs and recovers, a weak or
  unpowered joint visibly gives, and an MG's sustained fire walks an unpowered turntable round. What a joint's motor
  and stops push back with passes into the body (an anchored body doesn't move; its joints still kick). The shots of a
  tick add up per segment (no allocation). Clients see it through the poses (no wire change); the gunner (in the gun's
  seat, or the driver of its body) gets a small camera shake to the pull's kick (`GunnerShake`: an MG a tremor, a
  Cannon about 0.6). Kicks: Mounted MG 12, Autocannon 120, Cannon 900, Rocket Pod 10 (rockets are nearly recoilless),
  Flamethrower 0; receivers 6 (Rifle), 8 (Shell), 25 (Heavy), generated receivers `receiverKick` (6).

### Flame and harpoon

Two fire types of the one modular weapon system (`Source/Constructs/ConstructHarpoon.cs`, `Source/Game/GameSandboxHarpoon.cs`; the
builder's side is in docs/sandbox/WEAPON-BUILDER.md, "Flame and harpoon"). They are weapon builder parts, so each is a handheld gun
at the workbench and, generated like every builder part, a gun on a construct: bolt a barrel and a feed to the **Firebreather
Receiver** or the **Whaler Receiver**, link a [Turret Module](joints-and-logic.md#control-modules), and a seated gunner fires it. The hand-made
Flamethrower heavy gun stays as it was. Numbers are in `Constructs/WeaponParts.json`.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `flameReach` | How far a flame goes at all, as a share of its full-damage range (1.25). |
| `flameReachPerStep`, `harpoonReachPerStep` | Range added per step of a merged receiver above 1x1 (0.5: a 2x2 has 1.5 times the reach, a 3x3 twice). A merge's own `range` power is 0, so this is what gives a big receiver its reach. |
| `flameRoundsPerLitre` | Rounds of fire in a litre of the construct's fuel (5). |
| `flameArmourHalf` | The armour at which flame does half (10). |
| `harpoonReel`, `harpoonHandReel` | Reel speed on a construct (4 m/s) and in the hand (3 m/s). |

**Flame.** A flame gun is a pellet gun with an end: 4 licks a pull, 8 pulls a second, each an ordinary `Round` (one ray against the
map, `ConstructWorld.Raycast` and the targets, in fixed arrays), so a flame adds no per-tick allocation and a tick's cost is the
licks fired, at most 32 rays a second a gun. The ray stops at `MaxRange` (hand: `WeaponDef.Reach`, also in the client's prediction).
Each lick lights what it touches through the same `Ignite` as incendiary rounds (wood, rubber, cloth, fuel tanks, ammo boxes: 12 s of
burning, spreading half-way), including a hand gun's now: `SandboxShotDamage` used to ignore incendiary rounds. A cut fuel pipe under
fire lights its tank (`ConstructRoutes.Damage`'s `blast`, now also for fire from any incendiary round). Heat isn't stopped by armour
as a small bullet is: `damage / (1 + armour / flameArmourHalf)`, raw. Rays that reach the map go on to `ShotProps` (`FlameWorld`), so
a mounted flame sets off explosive props as a hand gun does. Players burn as from any incendiary round (`IgnitePlayer`).

| Flame | Lick | Pulls a second | Full damage to | Reach | Rounds a pull |
| --- | --- | --- | --- | --- | --- |
| Hand or 1x1, plain | 4 x 4 | 8 | 8 m | 10 m | 1 |
| Hand, Napalm Gel | 5 x 4 | 8 | 7.2 m | 9 m | 1 |
| 2x2 merged | 16 x 4 | 4 | 12 m | 15 m | 4 |
| 3x3 merged | 36 x 4 | 2.7 | 16 m | 20 m | 9 |

**Fuel.** A flame gun loads from the ammo boxes its feed reaches, like any gun, and then from the construct's own fuel
(`ConstructHarpoon.TakeFuel`): 5 rounds a litre, so a truck's tank is its flamer's magazine. The look line counts both.

**Harpoon.** One barbed head, a hitscan round drawn flying at 80 m/s (the wound lands at once, like a cannon shell). Where it strikes it
ties a real rope (`ConstructWorld.TieLine`: a `CConnector` with `Line` set; launcher at A, head at B): a construct's part, a wreck, a
loose body (all parts, tied by tag, so a part that breaks takes the rope with it), the map (a world end, which goes when the ground does), or a player. A miss ties nothing; the
head is lost. Reach is its range: 40 m, 60 m on a 2x2, 80 m on a 3x3, at most `ConnectorMaxLength`. The rope behaves as any rope does: it
holds and drags through the contact solve, and snaps over its rating with the crack, whip and word to the players near.

| Launcher | Rope |
| --- | --- |
| Hand | S (1.5 t) |
| 1x1 receiver | M (6 t) |
| 2x2 | L (25 t) |
| 3x3 and up | XL (120 t) |

- **One line at a time.** While a line is out the launcher can't fire. **Fire reels it in** (a new press: the pull that fired it doesn't
  reel); **R cuts it**: the seated gunner's reload key, or the hand gun's. The attach tool's RMB and the remover take a line off as any
  rope. A wired trigger reels too; a wired gun has no key to cut with. A reel stops at 1.5 m of rope, and stalls with 1.5 m of stretch when both
  ends are held, so it doesn't tear its own rope. Survival: a shot costs its round, not the rope's wood (the rope is in the head's price).
- **An end on a player.** `ConnectorEnd.Player` (id + 1): a world end each PC keeps on the player (the server each tick, each PC each
  frame as drawn), so drawing, aiming at the line and the list work unchanged. The solver can't move a player, so the shared
  movement step does (`Game.TetherPlayer`, on the server and in the client's prediction): past the rope's length plus 0.6 m of give a player is
  drawn back along it, up to 14 m/s, staggered so the ground's grip doesn't undo it; the mover keeps them out of walls. That give's
  stretch pulls the body at the other end, softly (`PlayerPullStiffness`, 2.5 kN/m: about 1.5 kN at the most): a crate follows a player,
  a parked truck stops them. Riders on a construct aren't moved: a line held from a moving deck tears when it runs out.
- **A player hit** takes the head's damage (45, 50 with a Barbed Head, which also bleeds), and is held on the line: they can't walk past
  it, and reeling hauls them in. **F tears it out**, for 10 damage (never fatal); they go free as well when the launcher lets go,
  dies, leaves, or puts the gun away, or when the rope snaps or tears.
- **Saves.** A line on a player isn't saved. One between constructs loads as a plain rope (the gun can fire again).
- **Not in Sandbox** a Whaler is a plain heavy bolt: no rope.

Wire: protocol 148 (a connector's two player bytes, `CActKind.Harpoon`: 0 cut, 1 reel, 2 stop, 3 tear out).

### Ammo feeds

Code: `Source/Constructs/ConstructAmmo.cs`; generator: the part kit's `--gen-parts ammo_parts` (Source/PartKit/Generators/AmmoParts.cs). A vehicle's ammo can sit in one armoured place, with feeds running
out to its turrets.

- **What sets a mounted gun's rounds.** What feeds it. No part on a mounted gun sets the type any more: Ammo parts on a gun (FMJ, AP, HE and Incendiary Rounds
  and the builder's) do nothing there, though an old one is left in place and a box bolted to it still feeds. Handheld guns are unchanged: the workbench's Ammo socket.
- **Ammo type.** A setting on Ammo Boxes, Crates and the Ammo Container (configure tool; default FMJ). A type is one of the weapon builder's Ammo parts
  (`ConstructAmmo.Types`, an index on the wire and in saves, so new ones go at the end), so its stats and flags go through `BuildStats` unchanged: Armour Piercing penetrates,
  Dragon's Breath lights, Explosive Tip splashes, a Siege Shell digs deep. Ammo stays universal: **any type fits any gun**. Calibre comes from the receiver's bore and the
  store only says the round type, so one crate feeds a rifle receiver and a howitzer alike. (A flame or harpoon gun takes the type's numbers but stays what it is.)
- **Loading.** A gun loads at its reload, from its feed's store first, else a magazine on a socket, else its own magazine, else the nearest box bolted to it with rounds in it, and only that type. Its numbers change there
  (`BuildGuns`, allocating once at the change). A different type clears what was left in the gun. A seated gunner's **reload key** clears it now, losing those rounds, to take what a switch has chosen.
- **The Ammo Container.** A Storage Volume's box you size with the configure tool (Weapons > Ammo boxes), the same code, so handles, saves, the wire, sealing, spilling and the fill drawn
  come with it. It holds the bulk material `ammo`, 400 units a cubic metre, 10 rounds a unit, 1 kg a unit: 4,000 rounds a cubic metre. Sealed-volume rules apply as to any
  solid: each open face costs a sixth of 10% of its capacity, it pours under hard acceleration, and `ammo` is on `Sealing.json`'s flammable list, so an open box burns. Bulk ammo comes from a Foundry
  (1 steel + 1 bronze: 8 ammo) and arrives by chute or tipped in; creative sets `fill`. Only ammo feeds guns, though it takes anything a Storage Volume does. Holed to nothing it blows up by what is
  in it (3.5 m + 0.25 x the square root of the units, at most 12 m; 260 + 20 x that root, at most 1,500).
- **The ammo feed.** A new link type in the linking tool, `RouteType.Feed` ("Ammo out" to "Ammo in"): a flat belt with a yellow stripe, 60 health a segment, routed, auto-routed, branched, loomed and carried in
  trays like any link. Like a chute it may cross a moving joint as a flexible section, so a feed reaches a gun that swings through a limited arc. Every gun has an implicit "Ammo in" (at its magazine socket, "left") and every box and crate an "Ammo out"; a
  cut feed delivers nothing. Hits on it are shielded by trays as for any link. A flexible section has the flexible limits: at 135 degrees of bend it stops delivering, past about 169 it tears. Measured through a real host and client at 80 ms with 15% loss, firing: a feed across a turntable and a motor hinge feeds through +-80 degrees, stops, and tears at about 182 degrees of traverse. For a gun that goes all the way round, use a reel.
- **The Ammo Reel.** Ammo Reel (8 m) and Large Ammo Reel (16 m), parts in Weapons > Feed chutes, like the data lead reels (`PartRole.Hose`, `HoseCarry.Ammo`, `Source/Constructs/ConstructHoses.cs`). Feed its "Ammo in" with a feed link, or bolt a box to it, take the belt by hand (F) and plug it into an **Ammo Socket** (F), bolted to the gun or wired on with a feed into the gun. The belt is a rope: it follows the gun however the turret turns, on one construct or across two, and comes out only past its length. Differences from a laid feed: no bend limit, no routing or trays, no health, nothing to hit, so no cook-off, and it is plugged by hand. Measured the same way: a full 360 degree traverse, firing all the way round, with the rounds drawn from the crate. A blueprint plugs its reels with `"hoses": [[reel, inlet], ...]` (step numbers).
- **A gun's magazine.** The Feed category's parts are now called **Magazines** (the construct menu and the workbench alike; hand-made Belt Feed and Shell Feed keep their names, as blueprints name them). A receiver holds one on its magazine socket ("left" on a generated receiver). Early guns are reloaded by hand: hold an empty magazine in your **Hands**, press F at a box, crate or Ammo Container and it fills, the store's type and all, out of that store's rounds; fit it with the attach tool as any part; a gun takes its rounds at its reload. Take the empty one off with the remove tool and go back. It keeps what is left in it, wherever you carry it (below). The rounds and their type sit in the part's `Rounds` (`ConstructAmmo.Pack`), so there is no change on the wire or in saves. In creative one comes full of FMJ. A merged gun takes N squared rounds a shot from a magazine of the same size, so one lasts a quarter or a ninth as long.
- **A magazine on an Ammo Socket.** For the early game, before there is a crate, reel or belt: a magazine clips onto any free face of an Ammo Socket (the attach tool's held-part path, with a ghost), on a socket bolted to the gun or one with a feed link from its "Ammo out" to the gun. The gun needs no magazine of its own, draws its 3 s of ready rounds from it at `feedReload`, and takes the type of the rounds in it. When it runs out, take it off with the remove tool, or hold a filled one of the same kind and press F on the fitted one: they change places. A magazine a gun can't take (a shell magazine on a rifle's socket) is refused with the workbench's reason, on the HUD and at the attach (`ConstructAmmo.GunTakes`, `SocketRefusal`); one already on a socket when a link goes in is ignored by that gun.
- **The order of a gun's sources.** A working feed with rounds behind it (a link, or a plugged reel), then a magazine on a socket along that feed, then the magazine on the gun, then a box bolted to the gun. Why: what is brought automatically should run first so the hand-carried rounds stay in reserve for a cut belt, and the socket's magazine comes before the gun's own because taking it off doesn't touch the gun. A gun with a magazine on it keeps that magazine's reload time even while a feed serves it.
- **Carrying a magazine.** Its rounds and type stay with it in the hands, on a construct, and lying loose (dropped, set down or spilled from a dead player's backpack). Put away in the backpack, the inventory or a store it is only a part id in a count, so the player keeps the rounds: `ConstructEditsMags.cs` notes them by kind, and the fullest of that kind comes back the next time a magazine of it is in their hands. So two of a kind in a backpack hand out their rounds fullest first, and a magazine left in a crate leaves its rounds with the player who put it there. Server only: carried rounds aren't in a world save (the file has no place for them yet); fitted or loose ones are.
- **Feeding a gun directly.** A feed or a bolted Ammo Socket is a gun's magazine's later replacement: with either, no magazine is needed, and the socket is free. The gun draws from the far store and holds only `feedReadySeconds` of fire (3 s: 30 rounds at 600 rpm, at least 1), taking `feedReload` (0.6 s) to draw more. A cut feed leaves only the ready rounds. **A magazine and a feed can sit together**: a working feed goes first, its rounds are the gun's, and the magazine goes in if the feed is cut or its store runs dry. A box bolted to the gun works with a magazine as it always has, and a fitted magazine goes first.
- **Barrels on merged receivers.** A generated receiver's `front` socket sits on the bore (the mean of its builder barrel sockets) rather than the middle of its model's bounds, which side pieces skew, and barrels, muzzles and splitters have front and back on their axis: the Hammerfall Autocannon was 49 mm off, the Bunker Buster 34 mm, the Firebreather 25 mm. A merged receiver scales that by N (`ConstructMerge`, `CentreSocket`), with the magazine socket flush on the merged face at 2x2 and 3x3.
- **The Ammo Switch.** 2, 4 and 8-way parts (Weapons > Feed chutes): feeds into "Ammo in N", one "Ammo out", a data input choosing: **0 the first, 1 the next**, rounded, held at the last.
  A lever works one as a 2-way. Switches chain, 8 deep. The choice takes effect at the gun's next reload.
- **Cook-off.** A loaded feed (its store has rounds; behind a switch, always) that is cut catches fire: from the cut, each way, a segment goes up every `feedCookStep` (0.12 s): a blast of
  `feedCookRadius` (1.6 m) and `feedCookDamage` (70), and the segment is cut. It runs `feedCookRun` (8 m) each way at most and **stops at the end of that link**: a switch, a gun or a store is not crossed, though a
  blast at the end hurts what stands there. So a switch is a fire door. A chain reaches a store only by its blast, and armour and health decide; a store that goes then cooks off as ever. At most 16 burns at once.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `feedCookRun`, `feedCookRadius`, `feedCookDamage`, `feedCookStep` | Above. |
| `feedReadySeconds`, `feedReload` | What a directly fed gun holds, in seconds of fire (3), and its time to draw more (0.6 s). |
| `roundSpeed` | What a gun's round leaves at (350 m/s, times its `bulletSpeed`), unless its file gives a `speed`. |
| `digDamage` | A round doing this much or more when it hits the ground digs (60). |
| `digRadius`, `digRadiusMax` | The crater at that damage (0.5 m), growing with the square root of it, and the largest (2.5 m). |
| `digPerSecond`, `digGunPerSecond` | Rounds' digs a second: the world's (4, a one-second burst) and one gun's (3). |

### Falling rounds

Every construct round falls (`ConstructWeapons.Round`): at most `ArcPieces` (6) straight pieces of its real path, each cast as a straight round was, at least 40 m long, so a short range is one piece, the
cost of a shot at most 6 times what it was, and nothing is allocated. Speed is `roundSpeed` or the gun's own: an MG's 350 m/s drops about 2.9 m at 200 m. Flames and rockets (self-propelled) fly straight.
A Sandbox hand-held built gun falls too (`Source/Game/GameRoundArc.cs`): its shot is still a hitscan, resolved at once with lag compensation, but along up to 8 straight pieces of the arc, and the shot's usual code runs on the piece that meets something. Stock guns, beams, flames, ropes, launchers and every other mode keep their straight line. A gunner's aim allows for it: the look's point goes through `ConstructShells.Lead`, as for artillery, and the landing cross and HUD line show for every seated gun. Tracers are drawn straight from muzzle to where the round
ended, as the round's end is what the event says.

### Grenades, launcher rounds and cover

`World.Query` is the map alone: constructs aren't in it, so anything that flies has to ask them too. `Throwables.Bodies` is the constructs of the Game ticking (set at the top of each tick, null outside the Sandbox). A thrown thing sweeps each sub-step's motion relative to each nearby body (`Throwables.ConstructContact`), so a thin plate or one coming at it isn't passed, and bounces by the relative velocity. Slow on an up-facing surface it rests on the construct (`Projectile.RideBody`, in that segment's frame): it rolls to a stop, goes with the vehicle, and flies again if it tips over 60 degrees, rolls off or the part goes. A sticky thing stuck to a construct goes with it. Launcher rounds (`RocketFlight.Step`) meet constructs too, and shells always did. A blast's players are covered by a construct between them (`Throwables.ConstructCover`; glass doesn't cover). A construct's own parts aren't: `ConstructWorld.Blast` hits every part in range by distance, through the hull.

### Flying rounds

Code: `Source/Constructs/ConstructRounds.cs`. A Sandbox round is resolved at once over its reach, as ever (a built hand gun's 200 m, times its charge above 100%; a mounted gun's range). A round that met nothing in its reach doesn't end there: it goes into a pool and flies until it lands, with `CFireKind.RoundOut` telling every PC, and a client flies its own copy for the tracer. Stock guns and every other mode end at their reach. Flame and harpoon guns don't fly on, and shells have their own pool.

- **Cap.** 256 rounds at once (`MaxRounds`); past that a round ends at its reach. 48 of them look for what they hit a tick (`RoundRaysPerTick`), in turn, each over the whole stretch since its last look, so nothing is flown through and a busy sky costs a bounded tick. Nothing allocates.
- **Air.** Drag of 0.3 a second (`RoundDrag`): against gravity of 18 m/s2 a falling round settles at 60 m/s. A 350 m/s round shot straight up reaches 782 m and is down in 23 s at 59 m/s. It flies 60 s at most, and goes at the map's edge.
- **Impact.** A flying round hits players, zombies and construct parts with its damage times its speed over its muzzle speed: that round lands with a sixth of its damage, at least 1 on a player. It hits the map with no mark, and an explosive round doesn't go off.
- **Space.** On a planetoid or out in space there is no drag, and a round follows `ConstructShells.GravityAt` (the map's point gravity and every gravity hook). It flies 120 s: 42 km at 350 m/s, 126 km at 1,050 m/s. It goes 180 km from the bubble's origin (`RoundSpaceReach`), where floats coarsen and ships leave the bubble: it doesn't leave with them.

### Powder charge

One number for how hard a round leaves, 25% to 300% of the standard load (`Source/Zombies/PowderCharge.cs`). 100% is the gun as it was. **On a construct gun** it is a setting on the receiver (configure tool, `CfgKey.Charge`, group Gun): every builder receiver and the Rifle, Heavy and Shell Receivers have it; on a merged receiver any member's panel sets it (the first member away from 100% counts). **On a hand gun** it is a slider beside the build in the Sandbox's Gun Workbench, kept in the build text's style field after an `@` (`volley@150`), which the default doesn't write. It isn't on a beam (`energy`), `fuel`, `harpoon` or `launcher` receiver, nor on melee weapons. Zombies ignores it, and no other mode has built guns' arcs.

| Effect | Rule |
| --- | --- |
| Muzzle velocity | Times the charge up to the barrel's limit, half as much past it. |
| Barrel | The limit is 100% + 300% x (length in m - 0.2), from 120% to 300%: a 0.25 m barrel 120%, a 0.6 m one 220%, 0.87 m and up all of it. Length is the barrel part's muzzle from its breech. |
| Spread | x(1 + 1.5 x charge past the limit): only past it. |
| Damage | x the square root of the velocity (a shell's blast doesn't change: that's its filling). |
| Recoil | x the velocity. |
| Range | Damage range x the velocity; a mounted gun's longest flight x the velocity above 100%; a shell's reach x the velocity squared. |

So at 25% a rifle round leaves at 88 m/s and falls 1.6 m in 40 m, at 100% it is 350 m/s and 160 m, and at 300% from a long barrel it is 1,050 m/s. The look line on a gun and the workbench show the speed, how far it flies before it has dropped a metre, and the recoil. Rounds are still resolved at once (a shell flies for real); a round under 95% of 320 m/s is drawn travelling, on its arc (`Tracer.Bow`). Everything that aims uses the real speed: a mounted gun's `RoundV` (the gunner's lead, the landing cross, sentries, bandits) and a shell's `Speed`. It isn't wired to a logic port: a gun's one input is its trigger.

### Rounds that dig

A round that reaches the ground with `digDamage` or more digs, through `Game.Dig`, radius `digRadius x sqrt(damage / digDamage)`: a Heavy Receiver's .50 (85) 0.6 m, a 2x2 merge of it (340) 1.2 m. An MG (24) doesn't.
The limit is shared with construct guns' explosive rounds, which dug without one. Each op is 15 bytes on the wire and a line in the save's log, so at the world's rate an hour of steady firing is 14,400 ops at most.

### Artillery

Real shells on a real arc, in the same modular system (`Source/Constructs/ConstructShells.cs`, `Source/Game/GameSandboxShells.cs`; parts in
docs/sandbox/WEAPON-BUILDER.md, "Artillery"). Bolt a tube, a feed and a box to a **Field Gun Receiver** or **Howitzer Receiver**, put it on
a Motor Hinge on a Turntable, link a seat through a [Turret Module](joints-and-logic.md#control-modules) as for any gun, and fire. Merge them for more.
They can't be held: the workbench refuses a receiver with the `mounted` flag ("too big to hold").

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `shellBlast` | Blast radius (m) of a 320-damage shell. It grows with the square root of the damage, so a merge's N-squared damage is N times the radius. |
| `shellBlastMax` | The biggest blast (45 m). |
| `shellCrater`, `shellCraterMax` | Crater radius as a share of the blast (0.4), and the biggest (14 m). |
| `shellSiegeCrater` | A Siege Shell's crater against that (1.5). |
| `shellRangePerStep` | Range added per merge step above 1x1 (0.25). |
| `shellKick` | Recoil a shot (1500 N s), times N squared. |

| Piece | Blast damage | Blast | Crater | Shots a minute | Reach |
| --- | --- | --- | --- | --- | --- |
| Field Gun | 320 | 9 m | 3.6 m | 10 | 420 m |
| Howitzer | 512 | 11.4 m | 4.6 m | 6 | 420 m |
| Howitzer, Long Gun Tube, Siege Shell | 819 | 14.4 m | 8.6 m | 4 | 504 m |
| 3x3 merged Howitzer, Siege Shell | 7371 | 43 m | 14 m | 1 | 504 m (420 x 0.8 x 1.5) with a Howitzer Tube |

**Shells.** A gun's reach is its range at 45 degrees, which sets the muzzle speed (`sqrt(range x gravity)`; 87 m/s for 420 m at the
game's 18 m/s2). Gravity is `World.Gravity` with the host's scale, and points at the planet on a curved map. A shell takes the gun body's
speed with it. High Explosive, **Siege** (a deeper crater, slow to load), **Incendiary** (lights what burns in the blast, and burns
players) and **Smoke** (three clouds, nearly no blast) are ammo parts; any other ammo fits, and Napalm Gel makes an incendiary shell.

**Aiming an arc.** The gunner looks where the shell should land: `ConstructShells.Lead` solves the arc to that point and the turntable and hinge
follow, so the crosshair is the landing point; **hold Aim for the high arc**, to lob over a wall or a ridge. The stock Motor Hinge goes to 100 degrees either
way, which is plenty; the Turret Module's `pitch` output (straight down to straight up) works too, for a piece driven by wires. A gun on the body points with its
construct. The drop aid works in every case because it reads the barrel, not the look: each frame `ConstructShells.Predict` flies the arc in half-second steps
against the map, an orange cross marks where the gun would land as it points now, and the HUD line says
`Field Gun Receiver: lands 115 m out, 1.3 s, elevation 7 - RMB: high arc`. It sees the map only, not constructs.

**On the wire and what clients see** (protocol 150). `CFireKind.ShellOut` (id, kind, muzzle, velocity) and `ShellLand` (id, where, radius, kind) go reliably to
everyone in `Msg.CFire`. Each PC flies its own copy with the same step; only the server tests for hits, and its landing event ends the
shell everywhere, so a client's copy runs about half a round trip behind (10 m at 80 ms) and never lands by itself. Both events carry world positions, so
a shot works for a client far from the gun or the target; no interest filter applies to shells, ground ops or blasts. In a big world the far
bodies' poses are what thin out (`WritePosesFor`), so a far target shows its damage as late as its poses. A joiner doesn't get shells in flight.

**Cost.** Everything is in fixed arrays and a tick allocates nothing:
- At most `MaxShells` (256, raised from 64 for designed weapons' bomblets) in the air; a gun holds fire, its round not spent, while the pool is full.
- Each shell tests for what it hit one tick in three, over the whole stretch, so nothing is flown through and 64 shells cost about 21 ray sets a tick. With more than 66 in the air the period grows (`ShellTestsPerTick`, 22), each look over a longer stretch, so the rays a tick stay about the same.
- One blast goes on at a time, walked `ShellPartsPerTick` (400) parts a tick; when one finishes inside the tick the next starts on what is left of the budget, up to 12 a tick (a cluster's bomblets go off together, a big blast still takes ticks), over the bodies found when it landed (256 at most). The blast's event, the players, the
  crater and the sound happen at once. A later shell of a salvo waits its turn, about 3 ticks per 900-part target.

Measured on the cloud box: 63 shells at once on a 900-part slab: a mean of 0.1 ms a tick for the guns' step, one tick of 8.5 ms (the slab coming apart), the next 3.6 ms, the rest under 1 ms.

Blasts don't touch props or destructible buildings, as with other construct rounds. Shells aren't saved.

### Air weapons

Bombs, flak, rockets, missiles and flares, all on the shell pool (`Source/Constructs/ConstructOrdnance.cs`, `ConstructShells.cs`; parts from `tools/constructs/air_weapons_parts.py`, in `Constructs/Parts/AirWeapons`). They share `MaxShells`, the blast walk and the wire events of artillery. Demo blueprints: **Light Bomber**, **Flak Gun**, **Gunship**.

**Bomb racks.** (The [Ordnance Rack](#ordnance-racks) is the mount players build now; the old racks below load and work as they did.) A Bomb Rack (`CLogic.BombRack`, mechanical: it needs no power) holds any part with `"explosive"` bolted or welded to it. While its input is on it lets one go every third of a second as a `ShellKind.Bomb` shell, with the point velocity of where it hung; its output is how many it holds. `explosive.radius`, `.damage` and `.crater` are the blast, and `explosive.fuse` is the delay after impact, during which the bomb lies where it hit and rides on the body it hit. The way down from the bomb is a ray along gravity, 8 m, against its own construct: anything there holds it (a closed bay door, a hull, a wheel), the look line says what, and an open door has swung out of the ray. The rack looks again six times a second, so a door that opens lets it go. Parts cut off by a bomb leaving fall away as usual. One bomb a body a tick: a part coming off renumbers the rest.

The drop aid: a pilot or a gunner with a rack in reach gets the orange cross (`GunnerMark`) where the next bomb lands as the craft flies now, and a line with how many are held and the fall time (`ConstructShells.Fall`, against the map, no wind).

**A heavy gun's shell.** In a heavy gun's `gun` block `"shell"` (flak, rocket, missile, flare, or an artillery kind) makes its rounds shells, with `speed`, `blast`, `blastDamage` and `crater`; `thrust` (m/s2) and `burn` (s) push it along its flight, `turn` (degrees a second) turns a missile, `prox` (m) is a proximity fuse. Triggers, feeds, reels, ammo and recoil are a gun's as ever.

| Part | Shell | What it does |
| --- | --- | --- |
| AA Gun | flak, 340 m/s, 12 m blast | Bursts within 4 m of a flying body, or on its sight's time. An air burst digs nothing. |
| Rocket Pod Mk2 | rocket, 40 m/s + 220 m/s2 for 1.5 s | Aimed straight at the point (no arc). The drop aid flies its push. |
| Missile Rail | missile, 30 m/s + 160 m/s2 for 3 s, 70 degrees a second | Goes off 6 s after its burn if it has met nothing. An old mount now: not offered (a designed missile on an Ordnance Rack does its job). |
| Flare Dispenser | flare, 5 s | Sinks on a drag of 1.5 a second, and does no harm. |

"Flying", to a fuse or a seeker, is an unanchored body doing 3 m/s or more that isn't carried; a fuse never takes the gun's own body.

**Sights.** A part with `"sight"` bolted to a gun. An **AA Sight** (`lead`) takes the body the gunner looks at and holds it while the look stays within its cone: the gun aims ahead of it by the time of flight, allowing for the gun's own motion, and each shell's fuse is set a little past that, so the proximity fuse has the first go. With nothing held its `fuseRange` setting (0: off) bursts shells that far out, for a barrage. It works for a fire director's gunner too. A **Seeker Head** (`seeker`) locks a Missile Rail at launch on the moving body nearest its axis in its cone, with no gunner, so a wired trigger works; with no seeker a missile locks what its gunner has in view (a ray along the look, held 6 degrees). No lock, it flies straight. The HUD line says what is locked.

**Missiles and flares.** A missile turns with `RocketFlight.Turn` towards where its target will be, at most 3 s ahead. Every 12 ticks it looks for flares within 300 m and in its eye (60 degrees): each pair decides once, by their ids, and 65% of flares draw it. A drawn missile has lost its lock: it goes for the flare, bursts near it, and flies straight once that has burnt out.

**Stabilised mounts.** A Turret Module's Stabilised setting holds the seat's look on the point it was left on, on the ground or on a body, while the mouse is still; moving the mouse sets a new point. It is done to the local look (`Game.SandboxStabilise`), so the guns follow as they follow any gunner, and the server has nothing more to do. It needs guns on joints (`SeatAims`).

A Flight Stabilizer's last output, `fire`, is its pilot's fire key, for a rack or a gun wired by hand.

**On the wire** (protocol 178). `ShellOut` adds a rocket's or missile's thrust, turn, burn and target, so a client far from the gun flies the same path; for a bomb its `Part` is the bomb's part type, not a gun. `ShellStop` lands a bomb's copy where it hit, and `ShellSteer` gives a missile drawn by a flare the server's place and speed. A client steers by its own copies of the bodies; `ShellLand` ends a shell everywhere as ever.

**Cost.** Nothing allocates in a tick. Fly adds a few float operations a shell. Each proximity shell takes one broadphase lookup and a box test on the moving bodies near it, one tick in three, at most 21 a tick, along the whole stretch since its last look, so a 340 m/s shell doesn't pass a target between looks; a missile walks the 64 shells once in 12 ticks, only with a flare in the air; a lead sight spends no ray while its lock holds. Measured on the cloud box: 63 shells of the three kinds among 60 flying bodies: `StepShells` a mean of 0.04 ms a tick, the worst 0.3 ms with the box quiet, 0 bytes. A bomb's release is one `RemovePart`, with its rebuild and its structure message, at most once a body a tick.

Bombs share the shell's limit: their blasts reach players, zombies and constructs but not props or destructible buildings.

### Ordnance racks

One rack for bombs and missiles, set by the configure tool (`Source/Constructs/ModularRack.cs`; presets from `tools/constructs/air_weapons_parts.py`, recipes from `assembling.py`). The **Ordnance Rack** is a `CLogic.BombRack` whose shape is its settings. Every station takes any ordnance with a lug: a designed weapon whose motor lights as it leaves (a missile: `OrdnanceDesign.Powered`, as the bench writes it in the key's letter) **launches** forward along the rack's -Z (the side of its `front` socket) at 12 m/s more than the craft and lights at once, as on a Launch Rail; anything else (a designed bomb, a Rocket Assist bomb, the Light Bomb, Aerial Bomb and Small Nuke) **drops** as from a Bomb Rack, with its fuses and the drop aid as ever (`ConstructOrdnance.Launches`, `LaunchDir`). A missile whose first stage pushes less than its weight here stays on its station, and the look line gives both in kN. A launch's way out is a ray ahead of it against its own construct, so a weapon in the row in front holds the one behind until it has gone. Bombs first or missiles first is not a setting: one at a time takes the stations in order, skipping any whose way is shut.

| Setting | Values | What it does |
| --- | --- | --- |
| Layout | grid, drum | Grid: rows fore and aft by columns across, every station hanging below the beam. Drum: layers along an axis down the rack by segments round it. |
| Rows (a drum's Layers) | 1 to 16 | Rows fore and aft, or rings along the drum's axis. |
| Columns (a drum's Segments) | 1 to 16 | Stations side by side across, or round the drum: 1 on its bottom, 2 its sides, 3 bottom and sides, 4 and up all four faces, side by side on a face past four (a part turns only in quarter turns, so a drum is four-sided). A drum with a top row hangs by a yoke over it instead of a spine. |
| Max ordnance length | 0.25 to 60 m | The longest weapon a station takes: the spacing fore and aft (a tenth more than this), the beam's depth, and the rating follow. |
| Max ordnance diameter | 0.05 to 20 m | The widest body a station takes (fins may overlap the next): the spacing across (this), the scale of the beam and hooks, and the rating follow. |
| Fire order | one at a time, salvo | One each interval while the input is on; or one press lets everything go, a tick apart (one a body a tick). |
| Interval | 0.05 to 3 s | Between releases one at a time, and a drum's turn. |

At most 56 stations in all (`ModularRack.MaxStations`: a part's 64 sockets, with room for its mounts; a salvo of 56 takes about a second); the configure panel says so in red while the rows and columns ask for more, and the server refuses the shape. A shape whose boxes would reach past 190 m from its middle is refused too ("too big").

**Ratings.** A station's rated load comes from its volume (diameter squared by length) on a curve through the old ratings at their sizes: 500 kg at 25 cm by 1 m, 5 t at 62.5 cm by 2.5 m, 2000 t at 2 m by 8 m, a power of the volume between and past them (`ModularRack.StationLoad`; a 25 cm by 2.5 m station takes 1077 kg). The whole rack takes a station's rating plus three quarters of one for each more station. Mass, health, armour, assembly time and survival cost grow with the stations and the cube root of a station's volume against the old light one's.

**Limits.** A station takes a weapon only if it is no longer and no wider than the rack's settings (a designed weapon's own length and diameter, from the bench; an old part file's length and the body under its lug) and no heavier than a station's rating, and the whole rack holds no more than its rating. One past any of them isn't held (it stays bolted on and won't go), the physics gun's ghost turns red and won't snap it on, and the look line says which: "too long: 2.25 m, and a station of the Ordnance Rack takes 2 m", "too wide: ...", "too heavy: ... a station takes ...", "too heavy: ... rated for ... in all". The physics gun hangs a weapon nose forward along the rack, and its neighbours on the same rack don't count as in its way.

**Presets** (the Tab inventory's Weapons, Ordnance racks): Ordnance Rack (1 x 1, 25 cm by 2.5 m), Twin Ordnance Rack (1 x 2, 25 cm by 1 m), Triple Ordnance Rack (1 x 3), Ordnance Rack 2x2 and 2x4 (25 cm by 2.5 m), Ordnance Rack 4x4 (25 cm by 1 m), Heavy Ordnance Rack (62.5 cm by 2.5 m, 5 t), Giant Ordnance Rack (2 m by 8 m, 2000 t), Launch Rack (1 x 1, 25 cm by 3 m: the old Launch Rail's job), Twin Launch Rack, Launch Rack 2x2 and 1x6 (25 cm by 3 m: the gunsmith's missile batteries), Missile Pod 2x2 (20 cm by 1.5 m), Light Ordnance Drum (1 x 6), Heavy Ordnance Drum (1 x 8, 62.5 cm by 2.5 m) and Double Ordnance Drum (2 x 6). A preset's file is only its shape (`"modularRack": {"layout": "grid", "rows": 2, "columns": 4, "length": 2.5, "diameter": 0.25}`, a drum's `"layers"` and `"segments"`); the game makes the whole part from it, as it makes any other shape, so a preset and a rack set to that shape are one part. The demo blueprints use them: the Light Bomber a Twin Ordnance Rack, the Strike Plane and the Glide Bomber a Launch Rack under each wing.

**A shape is a part.** Every shape is one def: a preset's file when one has that shape, else one made at run time, named by its key (`Ord Rack (2x4 grid 250x2000)`: rows or layers by columns or segments, the layout, then the widest by the longest ordnance in mm; also its name in saves and blueprints, at most 32 characters), in the extrusions' id space (read it through `CPart.D`, `DefOf` or `ConstructParts.Def`, never `All[id]`). Changing a Rack setting swaps the part in place for the new shape with the Replacer's plan (`ConstructReplace.Plan` with its hooks kept free, `Apply`): its welds, wires and the socket it hangs by stay, and it grows from there. Refused, with the reason and nothing changed: anything on its hooks ("take everything off its hooks first"), something in the way, more than 56 stations, and in survival a dearer rack than it was (build a bigger preset at an Assembler). A blueprint names the shape as its part; a `"config"` that sets a Rack setting is refused with the part name it means. The panel follows the rack to its new part.

**The drum.** A weapon on a drum leaves from the bottom of its ring: `ConstructOrdnance.ReleaseAt` turns it there about the drum's axis, its way out is looked for from there, and its shell (a bomb's drop or a missile's launch) starts there. A drum lets one go each interval, salvo or not. The drum isn't drawn turning.

**The old mounts.** The Bomb Rack, Twin, Heavy and Giant Bomb Racks, the Missile Rail gun, the Launch Rail and the Heavy Launch Rail are `"legacy"`: they load and work exactly as before in old saves and blueprints (a missile on an old Bomb Rack still falls 0.4 s and lights; a rail launches anything along the weapon's own nose), but the Tab inventory doesn't offer them and an Assembler doesn't make them. The Modular Bomb Rack's presets (Modular Bomb Rack, Triple and Quad Bomb Racks, 5- to 8-Station Bomb Racks, Sixteen Bomb Rack, Light Rotary Drum, Rotary Bomb Drum) are legacy too, and its run-time shapes (`Bomb Rack (5 light, pairs)`) still load: each is the Ordnance Rack with the same stations in the same places (`ModularRack.FromLegacy`: a row is 1 x n, pairs 2 rows of half, rounded up, a drum layers of three; light, heavy and giant are 25 cm by 1 m, 62.5 cm by 2.5 m and 2 m by 8 m), its old rating, mass and health, and no size limits, so whatever an old save had on it is still held. An odd count in pairs or on a drum keeps the stations it had (5 in pairs is a 2 x 3 with one gap). Its hooks are drawn as a rail along each column now. On one, a designed missile launches forward instead of dropping. Its configure panel shows the Ordnance Rack's settings at those values; change a Rack setting and it becomes a new Ordnance Rack, with limits.

**Network and cost** (protocol 188). A run-time shape goes in a Full's part list as a mark and nine bytes (layout and old size, rows or layers, columns or segments, the station count, max length and diameter in mm; `ModularRack.Write`); its settings ride in a CBody as every part's do. A rack's tick is the old rack's plus two setting lookups; the limits are read when the construct's caches are rebuilt, at a release and for the look line, never each tick; a reshape is the Replacer's work, once a change. Nothing allocates in a tick.

### Modular ordnance

Bombs and missiles designed from sections at an **Ordnance Bench** (stages 1 to 4 of the ordnance work; code `Source/Constructs/OrdnanceDesign.cs`, `OrdnanceFlight.cs`, `OrdnanceStore.cs`, `ConstructOrdnance.cs`, `Game/GameSandboxOrdnance.cs`, `UI/UiOrdnanceBench.cs`; parts, rails and the demo blueprints from `tools/constructs/ordnance_parts.py`, recipes from `assembling.py`). Demo blueprints: **Strike Plane** (a heat-seeking missile on a Launch Rack under each wing) and **Glide Bomber** (a GPS glide bomb on a Launch Rack under each wing). Everything here is arcade: numbers chosen to play well, not to model real weapons.

**The design.** A design is a diameter (5 cm to 20 m) and sections from nose to tail, each with a count in a row, a length (for the sections that have one, below) and its settings. Bomb or missile is not a choice: every section goes in every design, and a motor that lights as it leaves (any motor but Rocket Assist, which waits after the drop) makes it a **missile**, otherwise it is a **bomb** (`OrdnanceDesign.Powered`). A missile goes off 6 s after its last burn if it has met nothing, and on a rail it must out-push its weight; a bomb falls until it hits. The key still carries the kind as its first letter (`B`, `M`): the bench writes the one the sections give, and a key keeps the letter it was saved with, so keys made before the bench was unified (a bomb with a Solid Motor, say) load and fly exactly as they did; only building one again at the bench makes it what its sections say (the bench notes it when a loaded design changes). Its key is text, `Ordnance:M25:Heat Seeker+Proximity Fuse@range=8+Warhead HE*2+Tail Fins+Solid Motor@burn=4`, and that key is the part's name in saves and blueprints; on the wire it goes as numbers (`Ordnance.Write`). The part is made at run time, one per key, in the extrusions' id space (read it through `CPart.D`, `DefOf` or `ConstructParts.Def`, never `All[id]`). A section's numbers are at 25 cm: mass, thrust and filling go with the cube of the scale, turn rates with one over its square root. There is no cap but weight: the numbers hold up to 50,000 t.

**Section lengths.** Sections where size is the point have a length the bench sets: the warheads (HE, Blast, Bunker, Kinetic Penetrator), the sub-payload bays (Cluster Dispenser, Fire Bomblets, Mine Dispenser, MIRV Bus, Rod Bundle), the Missile Fuel Tank, the Body Tube and the motors that carry their own propellant (Solid, Boost, Hybrid, Cold Gas, Rocket Assist). Fuses, seekers, steering, fins, wings, chutes, separators, the Tandem Spacer and the motors that burn a tank's fuel or air (Liquid Motor, Ramjet, Turbojet) keep their size; a bigger one of those is its Thrust class. A section's `ordnance.lengths` (`min`, `max`, `step`, `presets`, m at 25 cm, from `sizes()` in the generator: warheads and bays half to six times their own length, motors half to four times, the tank and tube a fifth to eight times) says it may be set; its numbers stay written at its own `length`. A section set to k times its own length is, number for number, k of it in a row: mass, filling (and so blast, damage and crater, on the same curve), built-in fuel and propellant, its survival cost and its look all go with k (`OrdPiece.Stretch`, `Amount`, `Kg`). Settings marked `perLength` (a tank's Fuel load, a solid motor's Burn time, a bay's Count) have their least, most and default at its own length and go with its length too (`OrdSetting.MinAt`, `MaxAt`, `DefaultAt`; the bench scales the value when the length changes). The deliberate differences from a row: a longer motor pushes with one motor's thrust for longer (the same total impulse as a row, a gentler and longer push), and a tank's `massFixed` 3 kg (its ends) is paid once however long it is. A row of charges is still one charge: a series, one after the other, is the Tandem Spacer's job. In the key a length is a setting-like `@length=` in m at 25 cm, written only when it isn't the section's own (`Warhead HE@length=1.5*2`), read before the settings so their ranges are its length's; on the wire each section carries its length (u16, mm at 25 cm; protocol 187). A key is never rewritten on load: two of a section in a row stay two.

**Folded parts.** The Long Missile Fuel Tank was the Missile Fuel Tank at 1 m, so it is gone: an old key's `Long Missile Fuel Tank` reads as `Missile Fuel Tank@length=1` with its old default fuel (40 kg) (`OrdnanceDesign.Folded`), the same weapon in mass, length, fuel and flight; the tank's fuel range is now 2 to 75 kg a half metre (150 a metre, the old long tank's most). A stored Long Missile Fuel Tank part (in a bay, a backpack) loads as one Missile Fuel Tank (`ConstructParts.Renamed`). An old design with a long tank now takes two tank parts to build in survival.

**The bench.** F at the Ordnance Bench opens it: a picture of the design across the top, the sections on the left, the design in the middle (nose at the top), its type and numbers on the right, and saved designs (`ordnance.txt` in the save folder).

- **Sections in build order.** The picker groups them nose to tail, the order a weapon is built in: Fuses, Seekers, Forward control (Canards, Reaction Jets), Warheads (with the Tandem Spacer), Sub-payloads, Body and fuel, Wings, Propulsion (motors, Rocket Assist and the Stage Separator), Tail control (Tail Fins, Gimbal Mount, Jet Vanes, Glide Guidance Kit), Stabilizers and brakes. The group is the section's data (`ordnance.group` and `groupOrder`, written by the generator from its slot); a section with none goes last under Other. A picked section goes into the design after the last section of its own group or an earlier one, so a design stays nose to tail as it is built (the arrows still move anything, for a stage's own tanks behind a separator); a plain section next to its twin of the same length adds one to the row. A section with a length has its presets (m at 25 cm) beside its name in the picker: the name adds it at its own length, a preset at that one. In the design list it has a Length slider (m at the design's diameter, kept to its step) and its presets; a row of it, or it and the same section behind it with the same fixed settings, has **Merge**, which makes them one section as long as all of them (per-length settings add up), when that fits its longest. This is the bench's picker only: the Tab inventory keeps its "Ordnance sections" tab.
- **The picture** (`OrdPicture`): the design from above, to scale from the sections' own boxes (length, diameter, fins, wings, nozzles), nose on the left, each section in the colour of its swatch in the design list (fins and wings darker), a row of one section marked where each ends. Long wings run off its edge so the body stays readable. It marks the centre of mass (a ringed cross), each Stage Separator (a dashed line, with S1, S2... over the stages: S1 burns first), each powered stage's push (an arrow over its tail-most motor, pointing to the nose) and the first stage's flame; the line over it gives the length, diameter, the span with fins and wings and where the centre of mass is. Hover a section for its name, length and mass; click it to pick it in the design list.
- **Type.** What its sections make it, with why: Free-fall, Retarded, Glide, Guided, Guided glide or Rocket-assisted bomb; Unguided rocket, Guided, Multi-stage or Cruise missile (air motor and wings). Sections that do nothing or something odd in the design say so rather than being hidden: a Gimbal Mount or Jet Vanes with nothing that burns, fins with no seeker to steer for, a chute on a missile, a MIRV Bus with no seeker.

The numbers are mass, length, payload, blast, damage, crater and fuse; the blast's curve (radius against kilograms of filling, the design marked on it); thrust against weight at launch, each stage's thrust, burn, fuels and mass, a level run's range and top speed; turn rates; wings' hold and glide ratio; drag; seeker, fuses and charges; and the engine limits it meets, as `Limit:` lines. Build makes it beside the bench. Creative: free. Survival: its section parts come out of the hands, the backpack and the bench's bays: one part for each own length begun (a 1.2 m HE is three Warhead HE parts), and a section at more than 25 cm takes the cube of its scale, rounded up, of each. So a section's recipe (`assembling.py`) is the cost of one own length, and a longer section costs in proportion.

**The blast.** A charge's radius is 2.84 m times the cube root of its weighted filling (kg), its damage 19 times the square root, its crater a share of the radius: 120 kg of plain filling is about the Aerial Bomb's 14 m. A warhead's `blast`, `damage` and `crater` weight its kilograms (thin-cased Warhead Blast: wider, weaker; Warhead Bunker: a deep crater; the massive warheads below: weights in the thousands and millions). A blast of 100 m or more is a shock front over several ticks ([Massive blasts](#massive-blasts)).

**Sections** (`Constructs/Parts/Ordnance/Sections`, the "Ordnance sections" tab). Any section goes in any design (an `ordnance.for` in an older or modded file is read and ignored). Slots, with what each adds:

| Slot | Sections | What it does |
| --- | --- | --- |
| motor | Solid Motor, Boost Motor, Liquid Motor, Hybrid Motor, Cold Gas Thruster, Ramjet, Turbojet, Rocket Assist (lights 1 s after the drop: it drives a bomb on and doesn't make a missile) | Thrust (N at 25 cm) times its Thrust class setting. Solid: a Burn time setting (each second and class adds mass). Liquid and air motors burn what their stage's tanks hold at their use rate; hybrid and cold gas carry some of their own. Air-breathing motors push with the air's thickness (`ConstructAir.Density`, none off world); a Ramjet only from 150 m/s. A Liquid Motor gimbals while it burns. A motor dropped off a hook waits 0.4 s (Rocket Assist: 1 s) before it lights. |
| fuel | Missile Fuel Tank (any length) | A Fuel load setting (kg, carried as mass) for the motors of its stage. A liquid or air motor with no fuel in its stage is refused. |
| stage | Stage Separator | Splits the design into stages: the sections behind it burn first. |
| control | Tail Fins, Canards, Gimbal Mount, Jet Vanes, Reaction Jets, Glide Guidance Kit | Turn rate, degrees a second: fins and canards in thick air at speed (all of it from 60 m/s), gimbal and vanes while a motor burns, reaction jets any time anywhere. |
| seeker | Heat Seeker, Radar Seeker, Command Link, Laser Seeker, GPS Unit, Radio Homer, Beacon Homer | What it steers for (below). |
| fuse | Impact Fuse, Delay Fuse, Proximity Fuse, Altitude Fuse, Timer Fuse, Airburst Fuse, Remote Fuse | Any together; impact always works (Delay: a set time after). Proximity: something flying in its range. Altitude: coming down to its height over the ground. Timer: its time since launch. Airburst: its distance from what it steers for. Remote: a Wireless Sender on its channel turning on, at any range. |
| payload | Warhead HE, Blast, Bunker; Fission, Fusion and Antimatter Warheads; Cluster Dispenser, Fire Bomblets, Mine Dispenser, MIRV Bus, Rod Bundle, Kinetic Penetrator | Filling for the charge. A sub-payload lets its Count go when the charge goes off (below). The massive warheads make a shock front ([Massive blasts](#massive-blasts)). |
| spacer | Tandem Spacer | Ends a charge: the payload behind it is the next charge of a series. |
| wings | Glide Wings, Pop-out Wings, Cruise Wings | Hold up a share of its weight at speed (all of it from 45 m/s at a hold of 1) and turn its sink into speed along: a glide. Pop-out Wings open 0.8 s after the drop. |
| stabilizer | Bomb Tail Fins, Retard Chute, Ballute, Ring Tail | Drag; a chute or ballute opens a moment after the drop. |
| body | Body Tube | Room, and weight. |

**Hooks and rails.** A designed weapon hangs by its lug on any rack (`CLogic.BombRack`). A rack's `load` (kg) is its rating: one over it isn't held (it stays bolted on, and the look line says why), and the physics gun won't snap it on. A Bomb Rack drops what it holds; a **Launch Rail** (`logic.rail`) sends it forward along itself at 12 m/s more than the craft, its way out a ray ahead of the weapon rather than down. A missile on a rail whose first stage's push here is less than its weight doesn't leave (the look line gives both in kN); off a hook it falls and lights anyway. The old Bomb Rack, Twin, Heavy and Giant racks, the Missile Rail gun, the Light Bomb and the other explosive parts work as before; the [Ordnance Rack](#ordnance-racks), which replaces the old mounts in the Tab inventory, drops a bomb and launches a missile from any station.

**Flight** (`OrdnanceFlight.Fly`, every PC). Gravity, then the push along its way (after its wait; an air motor's share by the air's thickness, none under its least speed), drag (a share of its speed a second at 100 m/s in thick air, with the square of its speed: a terminal speed), wings, and a turn of at most its turn rate towards what it steers for: `RocketFlight.Turn`, the Missile Rail's pursuit. Thrust is a push in m/s2 of what is still flying, so dropping a stage makes the rest quicker. A missile goes off 6 s after its last burn if it has met nothing; a bomb falls until it hits. A shell is given up on after its burns and waits, 6 s and half a minute (more than 90 s only for a long burn: a `Limit:` line says so).

**Stages.** When a stage burns out, the server drops it: a loose run-time part made from its sections and its separator (`OrdKind.Spent`, `Ordnance:S25:...`), put to one side of the line the weapon flies so its own ray never meets it, moving as it was. The next stage lights at once, and a ShellSteer gives every PC its push.

**Seekers.** Locks are made as it leaves the rack, by the server:
- **Heat**: the moving body nearest its axis in its cone (the Missile Rail's `Seek`); flares draw it. A Seeker Head bolted to the rack locks this way for a weapon with no seeker of its own.
- **Radar**: the unanchored body nearest its axis, moving or not; flares don't.
- **Command Link**: what the first seated person on the launching craft looks at: a body, or the point on a base or the ground.
- **Laser**: the spot a Camera on its channel looks at (a drone's camera: `ConstructRemote.FindCamera`), else the spot its launcher's gunner looks at, followed as it flies.
- **GPS**: the launching craft's Flight Stabilizer waypoint (`WaypointX`, `Y`, `Z`; on a flat world the ground under it), read as it flies, so a waypoint wired from logic moves it.
- **Radio**: the nearest Wireless Sender on its channel, followed.
- **Beacon**: the working beacon nearest its axis in its cone.
A body lock is steered for by every PC from its own copy of the body. A point is the server's: ten times a second it looks again, and when the point has moved 0.75 m a ShellSteer gives every PC the new one.

**Charges and sub-payloads.** Payload sections between Tandem Spacers are charges, nose first. When a charge goes off the next is laid 1.2 m (at 25 cm) on along its way, 3 ticks later: a tandem charge's second goes off deeper. A charge with a sub-payload lets its Count go as new shells fanned round its way, each with a share of the charge's filling, and its own blast is only a pop: bomblets (a blast where they land), fire bomblets (incendiary shells), mines (they lie up to their life and go off when a body moves at 0.5 m/s within 4 m, after 2 s to wake), MIRV warheads (small guided missiles fanned round its aim) and rods (thrown down at 60 m/s, kinetic). A kinetic hit (rods, the Kinetic Penetrator) does its damage times its speed over 300 m/s, squared. Everything goes off through the shell pool and the blast walk: there are no new effects.

**Limits that scale** (the developer's "no caps but weight"): a charge lets at most 48 sub-munitions fly, and more are carried by those (each a share of the filling); the pool's test period and the blasts a tick grow with it as above; a long burn raises the shell's life; a big blast walks its parts over more ticks; a massive one is a shock front, held to 2 km, its crater to 120 m. Each is shown as a `Limit:` line at the bench.

**On the wire** (protocol 183). ShellOut gains a byte after its flight: 1 when a designed weapon's block follows (its push, turn, burn and target for a bomb too, its size, air push, fin and gimbal turns, drag, wings, least speed, wait, deploy time, flags, aim point and life). ShellSteer gains a byte: 1 an aim point follows, 2 a new stage's push, burn, gimbal, least speed and flags, 4 a guide's update (the lock stays; without it a ShellSteer is a flare drawing it, as before). The design itself goes in a Full's part list as numbers (protocol 180), `OrdKind.Spent` too.

**Cost.** Flying a designed weapon is a few more float operations a tick than a missile. A guided one's point is one ray (or two: map and constructs) ten times a second; a Laser on a Camera also walks the bodies' camera lists. A stage dropping makes a part (once a stage), a sub-payload adds its shells and events in one tick (at most 48, and never past the fire events' room). Nothing allocates in a tick but those.

**In the air** (`ShellLooks.cs`, `Render/ConstructRenderShells.cs`; protocol 184). A bomb or missile that leaves its rack is drawn with the model it had there, at its real size: a designed weapon's run-time part (one model a key, made the first time it is seen), a part file's for the Light Bomb, Aerial Bomb and Small Nuke. A launcher's rocket or missile (Missile Rail, Rocket Pod) has no model of its own, so it flies as a stand-in made from the bench's sections (`RocketKey`, `MissileKey`); bomblets, mines and MIRV warheads stay the small box. ShellOut of a bomb, rocket or missile ends with its look: a byte (none, a part file's id, a design's `ExHash`), the id, and its part's world turn on the rack (four signed bytes). Every PC then turns its own copy each tick in `FlyShells`: the nose (-Z) eased from the rack's turn onto the velocity (the least turn, so the roll is kept), quickening over 45 ticks, then along it; below 1.5 m/s it keeps its turn, so a bomb let go from a hover tips over as it falls. What the flight knows is shown: a spent stage's sections are gone from the shell once it drops (`OrdStats.BoxPiece`; a client counts stages from ShellSteer), Pop-out Wings are folded inside the body on the rack and until the weapon deploys, then spring out (`OrdStats.BoxFold`), a Retard Chute opens as a canopy on four lines well behind and a Ballute as a balloon close behind once deployed, and a burning stage (not cold gas) gives the thrusters' flame at its tail. Cost: plain boxes go into the frame's instanced batch and allocate nothing; each contoured section is a draw call, so a shell is drawn in full only while its length is over 1.5% of its distance and the frame's shells stay near 400 contoured draws (the threshold rises while they want more, so the nearest keep their model); the rest, and every shell out of view, are one batched box or nothing. At most 64 nozzle flames a frame, within 600 m. Measured under llvmpipe with 256 burning two-stage missiles (16 boxes, 14 contoured, each): about 480 draws and 4-5 ms of the frame (1.9 ms with every shell one box), 0 bytes allocated.

### Massive blasts

The user asked for "massive ordnance options" (2026-11-07): three warhead sections in the bench's Warheads group, each a big step up. They are arcade game items: the numbers are chosen to look and play big and are not modelled on anything (code `Source/Constructs/ConstructMegaBlast.cs`, `Game/GameSandboxMegaBlast.cs`, `Render/Fx/MegaBlastFx.cs`; parts from `ordnance_parts.py` `massive()`, recipes from `assembling.py`; costs in docs/perf/LIMITS.md, "Massive blasts").

| Section (own length, 25 cm) | Mass | Blast | Damage at the middle | Crater | Look | Survival recipe (tier 4 Assembler) |
| --- | --- | --- | --- | --- | --- | --- |
| Fission Warhead (1.2 m) | 1.4 t | 166 m | 19,900 | 60 m | warm white flash, orange fireball | 120 s: 120 steel, 40 concrete, 25 gold, 2 Control Units, 4 Circuit Boards |
| Fusion Warhead (1.6 m) | 3.2 t | 380 m | 25,500 | 110 m | white flash, a bigger, slower plume | 240 s: 200 steel, 40 gold, 120 hydrogen, 60 helium, 3 Control Units |
| Antimatter Warhead (0.9 m) | 2.2 t | 612 m | 41,600 | 99 m | white and violet, the longest flash | 480 s: 160 steel, 60 gold, 80 levite, 40 helium, 2 Levite Cores, 4 Control Units |

They are lengthable like the other warheads (half to six times) and scale with the diameter like every section, so a wide one asks for kilometres; the engine holds any blast to 2 km (`MegaMaxRadius`, a `Limit:` line says so) and a crater to 120 m. Their mass is the gate: at 25 cm each is over a light station's 500 kg rating, so only a heavy or giant rack station (or a big Ordnance Rack) holds one, and only a big airframe lifts it. In creative they are free; in survival they come out of the hands, backpack and bays like any section, and their recipes need the top Assembler, rare materials (gold, levite, hydrogen, helium) and minutes of work.

**The shock front.** Any blast of `MegaRadius` (100 m) or more, a massive warhead's or a huge pile of plain filling's, landed or set off where it hangs (a cook-off), goes off as a front growing from its centre at 340 m/s (faster for the biggest: none takes more than 4 s to its edge):
- **Players and zombies**: each tick, those the front passed take damage falling off as the square of the distance (players half what parts take, as from any blast) and the Small Nuke's knockback. Inside half the radius nothing shields; further out a hill or a hull does. Zombies are hit all at once on its first tick.
- **Constructs**: walked nearest first as the front reaches them, about 800 parts a tick over every massive blast at once. A body wholly inside half the radius is gone at once (its contents too). Elsewhere every part in reach takes its damage, and the parts it breaks come off together; an ammo box, a tank or another bomb broken by it still cooks off or goes off (a chain), parts that stop working rather than fall off (engines, seats) stay. Near the middle what burns is lit (not for a plain filling). The pieces cut off fall as wrecks, at most 96 a blast, the rest are gone; a body is thrown at most 30 m/s.
- **The crater** is dug behind the front once it has passed, a band of rows a tick (each one `DigOp` with its slice: protocol 195), so a 120 m crater takes 52 ticks and never more than 3,500 columns in one.
- **Caps**: four at once (a fifth landing goes off as a plain blast; one set off on its rack waits half a second), the nearest 2,048 bodies.
- **The host's cap**: Match tab, "Biggest blast" (no limit, 1,000, 500, 250 or 100 m): every blast's radius is held to it, its crater shrinking with it; the damage at the middle stays.

**The look** (every PC, from the ShellLand, which now carries the look: protocol 195). A flash that whites out the screen by how big and near it is (warm for fission, pure white for fusion, white-violet and longest for antimatter), a fireball swelling to a third of the radius and rising, a ring of dust running out over the ground at the front's speed, then a smoke stem climbing into a rolling cap over a quarter of a minute; on lit maps the fireball's light. The bang arrives at the speed of sound (343 m/s), low and heard at any range, and the view shakes as the shock passes. All of it is the shared particle pool (both GL paths) and a full-screen rect: no new shader, nothing allocated.

### Cook-offs of ordnance

The user asked (2026-11-07) that "bombs or missiles or ordnance that take enough damage detonate if they possess a warhead" (code `ConstructWorld.Arm` and `SetOff` in `Source/Constructs/ConstructWorld.cs`, `Source/Constructs/ConstructCookOffs.cs`; measured with `--bench-cookoff`, below).

- **What goes off.** A part with a **warhead**: a designed bomb or missile whose charges have filling or a sub-payload (`ConstructPartDef.Warhead`, `OrdnanceDesign.HasWarhead`), and the old explosive parts (Light Bomb, Aerial Bomb, Small Nuke). A Kinetic Penetrator alone and a spent stage only break. The fuse doesn't matter: broken is set off.
- **Where.** On a rack, loose on the ground, held, in cargo, stored in a container (a stored part's health is the store's: `DamageStored`), and **in the air**: a bomb or missile in flight takes the rounds that meet it (construct guns, flying rounds and players' guns: `RayShell`, a capsule along its way as long and wide as its part) and the blasts that reach it (any blast, a massive blast's front as it passes), less its armour, and at its part's health it goes off where it is (`HurtShell`, `BlastShells`: in the blasts' turn; bomblets, MIRV warheads and artillery shells aren't hit).
- **The blast is the weapon's own.** A cook-off is a landed shell of the weapon's own charges (`SetOff`): its yield, its sub-payloads, its tandem charges, a massive warhead's shock front, crater and look, credited to the body's owner. It goes off 0.35 s after the break (a part file's own fuse for the old bombs), a few ticks apart by station, riding the body it was on. The part is gone in its own blast: no wreck of it falls.
- **Chains.** The blast walks its neighbours like any blast and may set them off in turn. Blasts are queued with every other shell's (`StepShells`: a few a tick within the 400-part budget), so a chain spreads itself over ticks; the warheads one blast sets off on a body come out together when the walk leaves it (one structure rebuild, `WarheadsGone`). When a rack itself breaks while ordnance on it is already hurt, everything with a warhead it holds goes off with it (`RackGoes`, sympathetic detonation) rather than fall as a heap: a rack's stations sit closer than its bombs' fins, so a heap of a hundred loose bombs overlaps and cost the physics up to 100 ms a tick. A rack shot off with nothing on it hurt drops its load as before. With the shell pool full a cook-off waits in the old queue (`CookOffs`).
- **Health.** A designed weapon with a warhead has 40 hit points plus 0.4 a kilogram (at least 60) and a casing of 2 armour, 12 for a massive warhead: a 25 cm HE bomb (34 kg) has 60, a Fission bomb about 600, so a few rifle rounds don't set it off, heavy fire, a cannon or a blast nearby does. Without a warhead: 0.8 a kilogram and 1 armour as before. A weapon with a motor that burns (not cold gas) or a fuel tank is flammable (`ConstructPartDef.Burns`): fire burns it down to its cook-off in about ten seconds. There is no inert setting. The look line shows its health like any part's.
- **Network and saves.** All on the server; every PC sees the blast from its ShellLand, as for any landed shell. Nothing new on the wire (protocol unchanged) or in saves.
- **Cost** (`--bench-cookoff [KEY] [--rows R] [--columns C] [--map NAME]`: a frozen 16 x 15 Ordnance Rack of 240 small HE bombs, one shot; a host and a client over loopback; the second of two runs): all 240 go off over 160 ticks (2.7 s), server tick median 0.1 ms, worst 2.2 ms with the JIT warm (17 ms the first time the rare paths run), allocating 1.2 MB in all (the shells' events and wrecks). One by one (each bomb's part taken out on its own, wrecks of them falling) the same chain had ticks of 50 to 1,000 ms.

**Not done.** Props, trees and destructible buildings are not knocked down by a massive blast (as with every shell; only the Small Nuke prop's own blast breaks props). The fireball is drawn on clients only from the event; a joiner who arrives mid-blast sees the crater but no fireball. The light and shake are the local player's, not a spectator camera's.

**Not done yet / decisions the developer may change.** No wind on designed weapons; the drop aid's cross is the plain fall, not a glide or a boosted path; a missile has no lift of its own (it arcs as the Missile Rail's does); the HUD doesn't show a designed weapon's lock; mines are set off by bodies, not by players on foot.

### Fuel blasts

The user asked (2026-11-09) that "any tank containing fuel when destroyed should explode" (code `Source/Constructs/ConstructFuelBlasts.cs`,
called from `ConstructWorld.CookOff`; numbers in `Constructs/FuelBlasts.json`; measured with `--bench-fuelblast`).

- **What goes up.** Any part that holds something that burns, burning or not, when it breaks: a fuel tank of any size (Fuel Tank, the
  small and large ones, the Fuel Drum, the Tanker Barrels, the Fuel Bunkers) by its share of the body's fuel pool (its capacity over the
  working tanks', itself counted), a Fluid Tank or Tank Volume by what it holds, and a designed weapon with fuel or a burning motor but no
  warhead by its fuel's kilograms (`propellantPerKg`). A part that only stops working (the tanks) goes up all the same, and its share leaves
  the pool. Empty: nothing. Lift cells keep their own hydrogen rule ("Air and lift"). Props (jerrycans on maps) are not parts and are not covered.
- **How big.** Litres times the fluid's `energy` (fuel 1, hydrogen 1.5, oxidiser 0.6, oil 0.35; water and the rest 0). Radius
  `0.5 + cube root` m, damage at the middle `60 + 30 x square root`, crater a fifth of the radius, held to 160 m, 3,000 and 25 m: a 15 l
  can 3 m and 176, a 60 l drum 4.4 m and 292, a 600 l tanker barrel 8.9 m and 795, a 20,000 l bunker 27.6 m and 3,000, the Capital Fuel
  Bunker (4 million l) a 159 m shock front ("Massive blasts").
- **The blast.** A landed incendiary shell (`ShellKind.Incendiary`: an orange fireball, the shell blast's sound and shake, and it lights
  what burns in reach), so it walks bodies, digs, hurts players and sets off ordnance and other tanks like any shell's blast, queued within
  the parts budget. It goes off `delayTicks` (0.3 s) after the break plus up to `staggerTicks` by the part's place, riding the body, so a
  chain ripples. No wire change: clients see the ShellLand event. Server only; nothing allocates.
- **Fused units.** Every member keeps its own health, so a tank fused into a hull goes up when it breaks like any other.
- **Cost** (`--bench-fuelblast`, second of two runs, a host and a client over loopback on the shared 2-core box): a drum, a tanker or a
  bunker, worst server tick under 1 ms CPU; a row of 24 drums 2 m apart, one shot: all 24 go over 516 ticks, worst 0.5 ms; the Capital
  bunker's front, worst 1.9 ms; a fused 616-part sheet with 16 tanks 1.5 m apart: 5 go at once (the blast lights what burns in reach), the blasts' ticks
  under 3.3 ms; the one tick of 7 to 10 ms is the sheet's rebuild after the first tank breaks (a part that stops working on a big fused
  body, as before this change), not the blasts.

### The yard

Construct Yard gains a **Gunner Turret** (a turntable, a gunner's seat on the arm, a Mounted MG on a motor hinge at the
front and an ammo box: sit in it, look, fire) facing a **Target Wall** (a steel panel on a post, a fuel tank and an ammo
box behind, a wood panel on the side); a **Hood Gun Buggy** (the buggy with a Mounted MG on its hood, fired by the driver's
fire key through an input reader); a **Sentry** (a Mounted MG on a turntable with a 6 m proximity sensor wired into it,
south-east, away from the spawns); a **Gun Bench** (a weapon builder gun from parts: a rifle receiver, barrel, muzzle
brake, box magazine fed by an ammo box, AP rounds and a scope, fired by a switch on its base) pointing at an **Ammo Dump**
(two crates: shoot one, and it sets off the other). Each is a blueprint in the parts menu too. The rack hands out every
weapon part.

### Decisions made for construct weapons (the developer may change them)

1. **Feed by touch, not wires**: ammo boxes feed a gun when bolted to it or its gun parts, or through feed chutes, like
   fuel through fuel lines (wires stay for signals).
2. **A receiver gun is its parts on the construct**: barrel, splitter, feed, ammo, muzzle device and optic are each a
   construct part naming a weapon builder part (13 hand-made; since wp01 every other builder part is generated, see
   "Weapon builder parts in Sandbox"). The action comes with the receiver part (its stock action, or one named in its
   file) unless an Action part is bolted on; grips, stocks, upgrades and utility parts take a free builder socket the
   builder's rules allow.
3. **Rounds are hitscan** with travelling tracers (a cannon shell or rocket lands at once; players can't dodge it yet).
4. **Gunners aim at what they look at** (convergence), and a seated gunner overrides the wires on the joints they point.
5. **A sentry is a gun with a proximity sensor wired into it**; it targets any living player in range (no factions yet:
   its owner too) and only players (bots are players; zombies only when it is a gunner's gun or a credited sentry).
6. **Optics**: a gun with an optic has 60% of its spread (no zoomed gunner view yet). **Semi and burst actions** fire
   at their rate while the trigger is held.
7. Players take **half** a blast's construct damage (a cannon would otherwise one-shot from across the yard).
8. **The Gunner Turret lost its shield panel**: the motor hinge and the gun sit on the arm's front instead.

## Merged receivers

Board item dev-sandbox-receiver-merge-rm01 (the developer approved it 2026-10-30). Code: `Source/Constructs/ConstructMerge.cs`;
rules: `Constructs/ReceiverMerge.json`; self-test: "Construct merged receivers".

- **What merges.** Weapon builder receivers (Rifle Receiver, Heavy Receiver: the `gun` parts with a `receiver`) of the
  **same type**, turned the same way, on the same segment, side by side in one plane (their backs flush, on the grid of
  their own size) and forming an **NxN square** (N from `minSize`, 2, to `maxSize`, 6) are one **merged receiver** drawn
  N times bigger: its sides flush with the square's, its back with the members' backs, so it reaches N times as far
  forward. T, L and 1x4 shapes stay single receivers. A broken receiver takes no part.
- **Found greedily, largest first**: every size from the largest that could fit down to 2, each scanned in rows (the
  receivers' up, then their right); a square whose cells are all free is taken. A 3x3 inside a complete 4x4 is the 4x4;
  a 3x4 is a 3x3 and three singles. The member with the lowest part index **leads**: it is the merged gun.
- **Derived, never stored**: the merges (and the scale of their gun parts) are worked out from the parts and links with
  every rebuild, so every client and every copy of a construct finds the same ones; nothing new goes on the wire (the
  protocol is unchanged) and a saved construct would need nothing either. Finding them allocates nothing.
- **One centre socket set.** The members' own sockets take no gun parts ("that receiver is merged: fit gun parts to the
  merged receiver's centre sockets"; their structural links stay: mounts, frames, each other). The lead gets the merged
  receiver's sockets after its own (socket index `own count + s`): the receiver's sockets N times out from the merged
  origin (the barrel at the front centre, the feed on the left, ammo on the right, the optic on top...). They take gun
  parts only. Ghosts show them like any socket.
- **Scaled gun parts.** A gun part on a centre socket, and every gun part bolted on through it (a muzzle device on the
  barrel, a splitter's barrels), is drawn and simulated **N times bigger** (its boxes, sockets, hits and contact points)
  and weighs N to the `partMass` power times its file (N squared). Ammo boxes stay their size. Ammo boxes bolted to any
  member feed the merged gun; a wire into any member is its trigger.
- **Its numbers** (each a power of N in the rules file, so the developer can change them): damage N^2, fire rate / N,
  recoil (kick) N^2, rounds a shot N^2 (out of its load and boxes), magazine N^2 (the same shots a load), spread and
  range unchanged. So a 2x2 is a slow cannon worth four rifles' damage a shot at four rifles' ammo, not four rifles.
  `sameTypeOnly: false` would let mixed receivers of the same size merge (the lead's type).
- **Building** (the server settles every receiver attached or removed): when a square forms, the gun parts on its
  members' own sockets **pop off** (with ammo boxes hanging only on them) into the builder's hands, else their backpack,
  else they drop as loose parts where they were (never lost). An old merge's centre parts move
  onto the new merge that holds most of its members (its same socket, scaled to its size), so growing a 2x2 to a 3x3
  keeps the barrel; when there is no such merge, or something would be in the way, they pop off.
- **Taking apart**: the attach tool takes off only the member whose own cell is under the crosshair (the outline is that
  cell, never the block); the rest merge again (a 3x3 less a corner is a 2x2 and four singles, its centre parts at 2x).
- **Damage**: each member keeps its own health and hitbox; a round damages the cell it hits; the look line shows the
  members' health together ("Rifle Receiver (2x2 merged, 620/720): ..."). A cell shot to pieces breaks the merge as
  taking it off does (its centre parts move or drop: nobody placed them).
- **In blueprints** a step on a merged receiver's centre socket names it `"merged "` and the receiver's own socket (`"merged front"`: `ConstructMerge.SocketName`); building
  one finds the merges again before a gun part's step (`ConstructMerge.SocketIndex`), as a rebuild would, so its place and scale are the merged ones. Save as blueprint writes them,
  and the vehicle kit's merged turret guns use them (docs/vehiclekit/VEHICLEKIT.md, "The armoury").
- The merged receiver's reach beyond its members is solid to building (nothing can be fitted into it) but not to rounds
  or the world: rounds hit the members and the gun parts.

### Decisions made for merged receivers (the developer may change them)

1. **Only identical receiver types merge** (`sameTypeOnly`), turned the same way (the proposal; mixed types: the
   developer's question, not asked yet).
2. **The scaling table** is the proposal, in `Constructs/ReceiverMerge.json`: damage N^2, fire rate / N, recoil N^2,
   ammo N^2 a shot, magazine N^2, spread and range unchanged, scaled gun parts' weight N^2. N from 2 to 6.
3. **The merged receiver grows forward** (its back flush with the members'), not about their centre.
4. **Popped parts** go to the hands, then the backpack, then drop (the shared give rule); ammo boxes hanging only on them come too; with
   nobody placing (damage) they drop where they were.
5. **The members' structural links stay** (only gun parts are refused on their own sockets), and boxes bolted to any
   member feed the gun.

## Weapon builder parts in Sandbox

Board item dev-sandbox-weapon-parts-wp01 (decision 23, 2026-10-30). Code: `Source/Constructs/ConstructWeaponParts.cs`;
rules: `Constructs/WeaponParts.json`; self-test: "Sandbox weapon parts" (`Source/App/SelfTestSandboxWeaponParts.cs`).

- **Every builder part is an item.** Every part in `Parts/<Category>` (guns, melee and grenade parts) has a Sandbox
  construct def, so it can be held, stowed in the backpack and storage slots, set down, and fitted to a construct.
  The defs are **generated at load** from the part library, never written by hand: each builder part becomes a part
  file's text that goes through the ordinary parser (so it is checked like any file): its model's boxes (tagged boxes
  left out, each as the box round it turned, the biggest 15 kept) scaled by the rules' `scale` (2: the hand-made
  construct gun parts are about twice the handheld size) and centred on their bounds, a hidden solid box round them, a
  socket on each face (right, left, top, bottom, back, front), the builder's weight times `massScale` (at least 0.5 kg),
  health `healthBase + healthPerKg x mass`. They are fitted as an item and light (at most `ConstructParts.HandCarryLimit`, 40 kg), so
  they are always items, never physics-only bodies (decision 21; the self-test checks it). A receiver becomes a `gun` on that receiver (its stock action, else the
  first action its socket takes) with a **bottom** mount like the hand-made ones; every other part a `gunPart`.
- **Ids.** The generated defs come after the hand-made files, in the builder's own sorted order, so the hand-made parts
  (and saves and blueprints naming them) keep their ids, and every peer with the same files gets the same ids (peers
  check the build on join). A builder part a hand-made file already wraps (the 13 gun parts and the three receivers) is
  not made again. With today's files: 327 generated. A name a construct part already
  has gets " (W)".
- **The inventory.** They are in the Tab inventory's **Weapon parts** category with the hand-made receivers and gun
  parts, the builder's categories folded into eight sections (the search still finds "muzzle" or "splitter"), with their 3D tiles. They are not on the
  parts rack by default (a map's `rackParts` can still name them).
- **Creative-only.** The rules' `creativeOnly` categories (`upgrade` and `multiplier`, the Zombies-economy parts) are
  offered only with infinite parts: the parts menu refuses them on a map with `"infiniteParts": false` ("the X is
  creative-only (a Zombies-economy part)"). Question for the developer: should they ship in survival?
- **On construct guns.** Any builder part can be fitted to a construct receiver. Barrels, splitters, feeds, ammo,
  optics and muzzle devices go where they did; an **Action** part bolted on replaces the receiver's stock action; any
  other part (grip, stock, utility, upgrade, multiplier...) takes the receiver's first free builder socket that
  `BuildRules.SocketWhy` allows, or the gun's look line says the builder's reason. The numbers come from
  `BuildStats.ReceiverStats` as before, so a construct gun has the Zombies builder's numbers for the same parts.
- **Merging.** Every generated receiver is a receiver gun, so the merge rules (`Constructs/ReceiverMerge.json`) apply to
  all of them: a merged NxN receiver has exactly one front barrel socket, at its front centre.

| `WeaponParts.json` key | Meaning |
| --- | --- |
| `scale`, `massScale` | Model size and mass against the handheld part. |
| `healthBase`, `healthPerKg`, `armour` | Health and armour. |
| `attachSeconds`, `receiverAttachSeconds`, `receiverKick` | Fitting time; a receiver's kick a shot. |
| `creativeOnly` | Builder categories offered only with infinite parts. |
| `skip` | Builder categories never generated. |

## The Gun Workbench (wp01, part 2)

Code: `Source/Game/GameSandboxBench.cs` (the server's rules and the sync), `Source/Constructs/ConstructGunBench.cs` (the
parts at a bench), `Source/UI/UiWorkbenchRun.cs` (the real workbench window in a Sandbox mode), `Source/UI/UiSandboxBuilds.cs`
(the Builds tab); part `Constructs/Parts/Storage/GunWorkbench.json` (a storage part with `"storage": {"gunBench": true}`:
six slots on its top, two long ones for barrels); self-test "Sandbox gun bench" (`Source/App/SelfTestSandboxGunBench.cs`).
Protocol 96: `CActKind.Bench` (kinds 6 open, 0 build, 3 take apart, 7 spawn a standard build); `Msg.ZBuild` and `Msg.ZRun` are
now also routed in Sandbox.

- **A handheld built weapon.** The Zombies path, reused: a gun goes in slot 1 (`Player.BuiltPrimary`, replacing the
  loadout primary), a melee weapon in `MeleeBuild`, a grenade in `GrenadeBuild`; one of each kind. Synced as the build's text
  (`Msg.ZBuild`), so every PC assembles the same numbers (`BuildStats`); a joiner is sent everyone's builds. Ammo is endless in
  Sandbox. It is used with bare hands (tool 1/2), like any gun. It survives death; a new match clears it.
- **At the bench**, F (not on a lit slot) opens the same `UiWorkbench` window with slots Gun / Melee / Grenade. The server
  needs the player within `ConstructEdits.BenchReach` of a bench; the window closes when the player walks away.
- **Creative** (`infiniteParts`): every part, endless; building or taking apart consumes and returns nothing.
- **Survival**: the stock is the part in the hands, the backpack and the bench's slots (`Msg.ZRun` tells the window). A build
  is checked whole (a missing part refuses it and nothing is taken), then takes what it adds and gives back what it gave up.
  Taking it apart returns every part through `BenchGive`: the bench's slots first, then `ConstructEdits.Give` (hands,
  backpack, the floor). Creative-only parts have no survival item, so they can't be built with there.
- **Standard builds**: the Tab inventory's **Builds** tab spawns any of the 143 complete into the hands (creative only).

Decisions the developer may change: the gun replaces the loadout primary (taking it apart leaves empty hands in slot 1, not
the old gun); creative disassembly returns nothing; consumption order is bench slots, backpack, hands; no auto-switch of
tool when a weapon is built.
