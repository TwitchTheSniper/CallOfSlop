# Zombies: the weapon builder (milestones 1 and 2)

Status: **in development**. Milestone 1 is the modular weapon system; milestone 2 applies the user's decisions (below):
grip-fixed fire styles, conserving splitters with damage multipliers and receiver upgrades, shared feeds, the late-game
weight path, standard complete builds, the real push cart, the melee slot and four weapon slots. All of it is playable
today as a sandbox: main menu > **WORKBENCH (ZOMBIES PREVIEW)**. The Zombies mode itself (World at War style, co-op 1-4,
rounds, new maps) comes later and plugs into the hooks described here.

Anything marked **proposed** is a working default the code implements so it can be tested. It is open for the user to
change. The "Open creative questions" at the end list the calls that are the user's to make.

Code: `Source/Zombies/` (PartDef, PartFile, WeaponBuild, BuildRules, BuildStats, BuildModel, BuildStore, MeleeStats,
StandardBuilds, RunTables, PushCart), firing in `Source/Game/GameBuilder.cs`, the match-side systems (cart, turret,
slots, melee builds, practice dummies) in `Source/Game/GameZombies.cs`, UI in `Source/UI/UiWorkbench.cs`, the range
session and cart drawing in `Source/App/Program.Workbench.cs`, self-test section "Weapon builder" in
`Source/App/SelfTestWeaponBuilder.cs` and `SelfTestWeaponBuilder2.cs`. Data: `Parts/<Category>/*.json`, standard
builds in `Parts/Builds/*.json`, drop tables in `Parts/Tables/DropTables.json`, the range map
`Maps/Dev/WorkbenchRange.json`.

## The user's decisions (2026-10-21) and what was built

| # | Decision | Built in milestone 2 |
| --- | --- | --- |
| 1 | Fire style across receivers is **fixed by the grip** | Each grip file has one `"fireStyle"`; builds have no choice; the workbench shows it ("Fire style: VOLLEY (fixed by the Rifle Grip)"). Old saves that chose a style load with the grip's. |
| 2 | **Weight** stays the main limiter; huge multi-receiver guns must be reachable late in a run | Rarer frames carry more (Mule Frame 16 kg / 6 receivers, Exo Harness 30 kg / 12, Gantry Rig 48 kg / 16), weight-relief parts raise the carry weight, and the drop tables put those tiers late (see "Run progression"). |
| 3 | The push cart is a **real pushed object** that can be **parked as a turret** | Server-side cart physics (rolls with you, collides, step and slope limits, holds you at the bar), hands on the bar, and E parks it as an **auto-firing turret** (proposed, open: see "The push cart"). Replicated (protocol 16). |
| 4 | **Splitters conserve total damage**; rarity buys spread/accuracy/outputs. Add **damage multiplier** parts and **receiver upgrade** parts on new receiver sockets | Every splitter gives 1/N per output (the file key `outputMult` is gone and refused); rarer splitters have `spreadMult` below 1 and more outputs. New categories: Damage Multiplier (6, one `amp` socket per receiver) and Receiver Upgrade (8, `upgrade_rear` and `upgrade_side` sockets). |
| 5 | A **shared feed** that feeds several receivers; per-receiver feeds still allowed | Five shared feeds on a grip's `hopper` socket, with rules for which receivers they feed, a pooled magazine and shared ammo. |
| 6 | **Mystery box = complete builds only** | `Parts/Builds/*.json`: 25 standard builds by tier with where they come from (box, wall, start); the workbench loads them as presets. |
| 7 | Weapon slots **2 -> 4** via an on-map upgrade, per run; the mode is a **roguelite** | Slots 3-4 exist in the game (keys 3 / 4, the swap key and wheel cycle them), unlocked per player per run (a flag the server owns); the range has a station (E) and the workbench a toggle. Drop tables by round are proposed data. |
| 8 | The chainsaw grip is just a grip; add a **melee slot** built from melee-only parts | Hilts, heads and mods (21 parts) build melee weapons with damage, swing time, reach, cleave and stun; the melee key swings them (in the range / Zombies only), with their own workbench section. |


## How it fits the Zombies loop

- **Finding parts.** Zombies drop parts (later rounds roll rarer tiers), and salvage spots on the map give scrap.
- **Workbenches** on the map are where you assemble, swap and break down parts.
- **Mystery box** gives a *standard complete configuration* only (decided): a whole build from `Parts/Builds` (for
  example the Minigun or the Twin Splitter Shotgun), never a single part. You can then take it to a bench and change it.
- **Wall weapons** are plain, working guns (one grip, one receiver): the standard builds marked `"wall"`. Bought off
  the wall, they are builds too, so you can modify them at a bench.
- **Weapon slots.** Players carry **2 builds**. An upgrade bought on the map (the "Mule Kick" of this game, proposed
  price 4000) raises it to **4** for the rest of the run.
- **A roguelite run.** You start each run with low-level gear (the `"start"` builds: a wall rifle, a wall pistol, a
  machete) and climb, inside one run, to broken, overpowered, silly guns. Bad parts along the way can make it hard.

## The component model

A weapon is a **tree of parts** joined at **sockets**. Every part is one JSON file (`call-of-slop-part/1`) with a
category, a rarity tier, a weight, its sockets, a box model and stat effects (the same `stat`/`set`/`add`/`mul`
effects attachments use).

```
Grip (root: the part you hold; it fixes the fire style)
 |- receiver_1 .. receiver_N    <- the grip decides N (pistol grip 3, rifle grip 9, Gantry Rig and push cart 16)
 |    |- barrel                 <- a barrel, or a SPLITTER whose outputs take barrels (or another splitter)
 |    |    \- muzzle            <- muzzle device
 |    |- feed                   <- box / drum / belt / tube / cell (or none: fed by the shared feed)
 |    |- ammo                   <- calibre / special rounds (or none: the shared feed's ammo)
 |    |- amp                    <- DAMAGE MULTIPLIER
 |    |- upgrade_rear           <- RECEIVER UPGRADE behind the receiver
 |    |- upgrade_side           <- RECEIVER UPGRADE on its side
 |    \- utility                <- side rail (laser, bayonet, cup holder, weight relief)
 |- hopper                      <- SHARED FEED (feeds several receivers) \- ammo
 |- stock
 |- optic
 \- foregrip                    <- vertical / angled / chaingun handle

Hilt (root of a MELEE weapon)
 |- head                        <- blade / hammer head / fish (required)
 |- mod_1, mod_2                <- nails, grip tape, counterweight, shock coil...
```

**The grip is the root.** It holds the player's hand and has one `receiver` socket per receiver it can hold, so the grip
sets the receiver capacity. Each receiver has its **own subtree**: its own barrels, splitters, feed and ammo, so one
weapon can mix a pistol receiver, a shotgun receiver and a minigun receiver if the grip can carry the weight.

| Category | What it does | Count |
| --- | --- | --- |
| Grip | Root grips (capacity, fire style, how it is held, carry weight) and foregrips (off hand) | 14 |
| Receiver | The action: semi, auto, burst, pump, bolt, rotary (minigun, spools up, barrels take turns), chain (spool-up). Sets the bore (calibre class) and base fire rate | 11 |
| Barrel | Range, damage, spread; bundles have several muzzles (Barrel of Barrels: 7) | 10 |
| Splitter | One shot in, N barrels out at 1/N each (barrel sockets take barrels or another splitter) | 6 |
| Feed | Magazine size and reload (box, stick, extended, drum, belt, hopper, tube, shell drum, cell, breech, toaster) and 5 shared feeds | 17 |
| Ammo | Damage, pellets, range, special flags | 10 |
| Damage Multiplier | x1.1 to x2.5 damage, one per receiver, with a drawback | 6 |
| Receiver Upgrade | Fire rate or damage up, two per receiver (rear, side) | 8 |
| Stock | Recoil, aim time, mobility (the Shoulder Harness relieves weight) | 5 |
| Optic | The game's optics (red dot, holo, ACOG, 4x, 8x) with their reticles and lens | 5 |
| Muzzle | Flash hider, compensator, brake, suppressor | 4 |
| Utility | Laser, bayonet, cup holder, and weight relief (sling, anti-grav coil, helium balloons) | 6 |
| Hilt | Melee roots: base damage, swing time, reach, cleave, hands | 6 |
| Head | Melee heads: machete, bat, sledgehammer, fire axe, katana, chainsaw blade, frying pan, frozen fish | 8 |
| Melee Mod | Nails, grip tape, counterweight, barbed wire, shock coil, rocket booster, whoopee cushion | 7 |

**Bores.** Each receiver fires one calibre class (`pistol`, `rifle`, `heavy`, `shell`, `launcher`, `energy`). Barrels,
splitters, feeds, ammo and muzzles list the bores they fit.

### Graph rules (BuildRules.cs)

Every refusal comes with a plain sentence saying why. These are the actual messages:

| Rule | Example "why not" |
| --- | --- |
| A part only goes in a socket of its mount type | "Red Dot mounts on an optic socket, not a receiver socket." |
| A socket can limit what it takes | "Hellwheel Rotary Receiver's barrel_1 socket takes barrels only, not splitters." |
| A build starts from a grip (or a hilt) | "Rifle Grip is a grip you build on: start a new build with it instead." |
| Grip capacity | "The Pistol Grip holds 3 receivers; it has no receiver_4 slot." |
| Bores must match the receiver | "Box Mag is made for rifle rounds; the Plinker Receiver fires pistol rounds." |
| Actions: belts and hoppers need something that pulls them | "Belt Box needs auto, chain or rotary action; the Triad Burst Receiver is burst." |
| Splitters stack at most 2 deep | "Splitters only stack 2 deep: the shot gets too thin." |
| Barrels per receiver (default 12, the Hellwheel takes 3-6) | "Too many barrels on the Workhorse Receiver: 13; it handles at most 12." |
| Limits per weapon: 96 parts, 32 barrels, 240 projectiles per pull | "Too many projectiles per trigger pull: 256; the most is 240." |
| Weight: a grip carries up to 2x its carry weight (plus weight relief) | "Too heavy: 9.1 kg; the Pistol Grip can't carry more than 8 kg." |
| Shared feed ammo must fit the feed | "00 Buckshot is made for shell rounds; the Manifold Hopper feeds pistol or rifle rounds." |
| Shared feed capacity and bores | "Workhorse Receiver needs a feed (the Manifold Hopper can't feed it: it feeds 3 receivers at most)." |
| Melee and gun parts don't mix | "Wooden Handle is a hilt you build a melee weapon on: start a melee build with it instead." |
| A melee weapon weighs 14 kg at most | "Too heavy: 15.2 kg; a melee weapon can't be more than 14 kg." |
| Required sockets (barrel, feed, ammo; a hilt's head) | "Plinker Receiver needs a feed." (shown as "Needs:", not an error) |

A build that is incomplete still gets a model for the live preview. Only complete, legal builds get a `WeaponDef`.

## Stat pipeline (BuildStats.cs)

The output is an ordinary `WeaponDef`, the same type every weapon file makes, so the existing firing, recoil, ADS,
sound, HUD and bots all work unchanged. The pipeline is **deterministic**: the same tree always gives the same numbers
and the same muzzle positions, and the self-test checks this by assembling every example twice.

1. **Each receiver on its own.** Start from the receiver's base numbers (class defaults, action, fire rate), then
   apply its parts in stages: receiver, ammo, feed, damage multiplier, receiver upgrades, splitters. A receiver fed by
   the shared feed takes the shared feed as its feed (and its ammo, if it has none). Then each barrel path (barrel plus
   muzzle device) is applied to its own copy, so a long barrel and a stub barrel on one splitter really differ.
   Upgrades change the fire rate as a ratio, so a minigun's rate per barrel scales the same way.
2. **Splitter rule (decided): splitters conserve damage.** Every output gets 1/N of the incoming shot (N = the
   splitter's outputs); nested splitters divide again (a Party Tray of Tridents: 12 barrels at 1/12). The total damage
   of a shot is the same with or without splitters; what changes is coverage (more barrels, splay) and accuracy: each
   splitter's `spreadMult` multiplies its outputs' spread (Y 1.0, Over-Under 0.85, Trident 0.8, Peacock 0.75, Party
   Tray 0.7, Hydra 0.6). Pellets are per barrel, so a split shotgun really fires twice the pellets at half the damage
   each. Damage comes from **damage multipliers** and **receiver upgrades** instead.
3. **Combine the receivers** by the grip's fire style (**fixed by the grip**, see below).
4. **The grip and what's on it:** grip, stock, foregrip, optic and utility parts, applied once to the whole weapon.
5. **Weight against the grip's carry weight** (**proposed**; the carry weight is the grip's plus every weight-relief
   part's `"carry"`): mobility `-0.012/kg`, and `-0.03/kg` over the carry weight; ADS time `+0.008 s/kg` (+0.02 over); draw time `+0.03 s/kg` (+0.05 over); recoil times
   `sqrt(6 / (kg + 4))` (heavy guns kick less per shot, clamped 0.55-1.25x). Then the frame's own mobility (a cart
   is slow however light it is).
6. **Clamp** everything into the ranges weapon files allow (mobility 0.4-1.2 for builds).

### Several receivers (the grip decides)

| | Volley (default) | Rotate |
| --- | --- | --- |
| A trigger pull | Fires every receiver at once | Fires the next receiver in turn |
| Fire rate | The slowest receiver's (they are linked) | The receivers' rates add up (max 3000 rpm) |
| Magazine | Per-receiver feeds, shown as the smallest (all run dry together); a shared pool lasts pool / receivers it feeds | Feeds add up; a shared pool counts once |
| Recoil | Root of the sum of squares (3 receivers = 1.7x one) | The average |
| Trigger | The strictest: semi beats burst beats auto | Same |
| Reload | The longest feed, plus 0.2-0.35 s per extra receiver (the grip sets it); every feed is swapped at once | Same |

**Grips (decided: one fixed style each).** Volley: Pistol Grip, Rifle Grip, Briefcase, Chainsaw Grip, Mule Frame,
Gantry Rig, Push Cart. Rotate: Competition Pistol Grip, Tactical Rifle Grip, Steering Wheel, Exo Harness. The workbench
shows the style next to the grip and in the stats panel; picking another grip picks its style.

### Damage multipliers and receiver upgrades

Every receiver has three new sockets: `amp` (one **damage multiplier**) and `upgrade_rear` / `upgrade_side` (two
**receiver upgrades**). Their effects apply to that receiver only, before its barrels, so they stack across receivers.

| Part | Tier | Socket | Effect |
| --- | --- | --- | --- |
| Hot Load Kit | common | amp | damage x1.1, recoil x1.08 |
| Overpressure Chamber | uncommon | amp | damage x1.2, fire rate x0.95 |
| Tungsten Core Injector | rare | amp | damage x1.35, reload x1.05 |
| Rail Accelerator | epic | amp | damage x1.5, reload x1.1 |
| Singularity Chamber | legendary | amp | damage x1.8, aim time +0.05 s |
| Spite Amplifier | wonder | amp | damage x2.5, hip spread x1.6 |
| Lightweight Bolt | common | upgrade | fire rate x1.08 |
| Match Trigger Group | common | upgrade | damage x1.05 |
| Buffer Spring Kit | uncommon | upgrade | fire rate x1.15, recoil x1.05 |
| Heavy Hammer | uncommon | upgrade | damage x1.12, fire rate x0.95 |
| Gas Overclock | rare | upgrade | fire rate x1.25 |
| Coil Hammer | rare | upgrade | damage x1.2 |
| Twin Sear | epic | upgrade | fire rate x1.4, hip spread x1.1 |
| Overclock Core | legendary | upgrade | fire rate x1.6, damage x1.1 |

### Shared feeds (hoppers and manifolds)

A shared feed sits on a grip's `hopper` socket (rifle grips, the chainsaw grip, the steering wheel, the briefcase,
the new frames and the cart; not the pistol grips). Per-receiver feeds are still allowed. The rules:

- It feeds receivers that have **no feed of their own**, in receiver-socket order, up to its **capacity** (`"shared":
  {"feeds": N}`); the rest need their own feed ("...the Manifold Hopper can't feed it: it feeds 3 receivers at most").
- A receiver is fed only if its bore is one the shared feed takes, its action is one the feed needs (if any), and the
  shared feed's ammo fits it.
- Ammo in the shared feed's `ammo` socket is **shared**: fed receivers need no ammo of their own.
- The magazine is **one pool** (its `mag` stat): in a volley, each pull takes a round per fed receiver, so it lasts
  pool / k pulls (next to any own magazines, the smallest wins); rotating, each shot takes one round and the pool
  counts once next to the receivers' own magazines. The reload is the longest feed's, as before.

| Shared feed | Tier | Feeds | Bores | Pool | Reload |
| --- | --- | --- | --- | --- | --- |
| Manifold Hopper | rare | 3 | pistol, rifle | 120 | 4.0 s |
| Shell Hopper | epic | 4 | shell | 48 | 5.0 s |
| Ammo Can Manifold | epic | 6 | rifle, heavy | 300 | 6.0 s |
| Bottomless Bucket | legendary | 9 | pistol, rifle, heavy | 900 | 8.0 s |
| Power Bus | legendary | 8 | energy | 400 | 5.0 s |

### Firing (GameBuilder.cs)

- Every barrel fires from **its own muzzle socket**. The ray starts at the eye moved sideways and up by that muzzle's
  offset from the barrels' centre (capped at 0.45 m so rays don't start inside a wall next to you), turned by the
  splitter splay, with its own pellets, damage and spread.
- **Rotary receivers** (minigun): one barrel per shot, in turn; the fire rate is `rpmPerBarrel x barrels` and spools
  up from `spoolStart` over `spoolTime` while the trigger is held (spools down twice as fast). Chain guns spool too.
- The shot seed (the client's shot counter) picks whose turn it is, so client prediction and the server agree.
- **Standard weapons are unchanged.** Weapons from the `Weapons` folder have no barrel list, and `ShotRays` gives them
  exactly the old rays (one per pellet from the eye, seeded by pellet index). The self-test compares all 50.
- **Last Stand rule.** One trigger pull is **one damage event per victim**: the server adds up every pellet from every
  barrel and receiver that hit a player, then applies it once, like a shotgun blast. So a Last Stand shield loses one
  charge per pull, not one per barrel. Rotating grips and rotary receivers fire one receiver/barrel per pull anyway.
  (Self-test: Triple Pistol, Nine Lives and the Twin Splitter Shotgun each use exactly one charge.)
- **Network:** protocol **16** (milestone 2) adds three messages at the end of the list: `CartUse` (client -> server:
  the interact key), `CartState` (server -> all, with snapshots while any cart exists: owner, mode, position, yaw,
  rolled distance, aim pitch, turret seconds left) and `RunState` (server -> one client: the slot upgrade). Built
  weapons and melee builds are still host-local in the Workbench Range (single player); co-op will need builds sent to
  clients (their text form) so they can draw and predict them. Until then, clients draw other players' carts as a bare
  Push Cart, a remote pusher's own movement isn't predicted against the cart (the server corrects it), and a turret's
  tracers on clients start at its owner.

## Rarity tiers and scrap hooks

| Tier | Colour | Scrap value | Craft cost (3x) | Where it comes from (proposed) |
| --- | --- | --- | --- | --- |
| Common | white | 5 | 15 | wall weapons, early drops |
| Uncommon | green | 12 | 36 | drops from round 3 |
| Rare | blue | 25 | 75 | drops from round 8, salvage |
| Epic | purple | 50 | 150 | drops from round 15, mystery box |
| Legendary | orange | 100 | 300 | mystery box, bosses, late drops |
| Wonder | red | 150 | can't be crafted | mystery box and special drops only: fun first, balance second |

Hooks in `Scrap` (PartDef.cs): `SalvageValue` (break a part down), `CraftCost` (3x its value; Wonder parts can't be
crafted), `UpgradeCost(from, to)` (same category, higher tier: the craft cost difference minus half the old part's
value). A part file can override its value with `"scrap"`. A build's scrap value is the sum of its parts.

## Model building (BuildModel.cs)

- Each part's boxes sit in its own space. The build becomes a **scene graph tree** (`SceneNode`): the grip at the
  root, a socket node per socket (`receiver_N`, `optic_mount`, `mag_well`, `support`, `stock`, `muzzle_mount`, the
  GunRig names) with the part plugged in under it, and one `muzzle_N` socket per barrel muzzle.
- The tree is flattened into a `GunMesh`, so the existing gun drawing, the scene graph `GunRig`, the IK arms and the
  sight lens system all draw built weapons with no special case.
- **Hand anchors:** the whole model moves so the grip part's `hand` point sits where the trigger hand is on standard
  guns. The off hand goes to the foregrip's `hand` if one is fitted, else the grip's `support` point (the chainsaw's
  wrap bar, the cart's push bar, the wheel's rim), else the lead barrel. The reload hand goes to the lead receiver's
  feed.
- **Sight line:** the optic sits on the grip's `optic` socket; the sight lens uses the mount height and position from
  it. Iron-sight boxes (`"tag": "irons"`) come off when an optic is fitted. Grips with `"ads": false` (cart,
  briefcase) are hip fire only and drop the optic's zoom.

**Chainsaw view.** The chainsaw grip is just a grip (decided): the normal first-person pose with the off hand on its
top wrap bar. The push cart is no longer a held gun: see "The push cart".

## The part file format (`Parts/<Category>/*.json`)

```json
{
  "format": "call-of-slop-part/1",
  "name": "Y-Splitter",
  "category": "splitter",
  "rarity": "common",
  "flavor": "One shot in, two half-shots out, side by side. Physics stays roughly intact.",
  "weight": 0.6,
  "split": {"outputMult": 0.5, "splay": 0},
  "sockets": [
    {"name": "barrel_1", "type": "barrel", "pos": [-0.035, 0, -0.06], "accepts": ["barrel", "splitter"]},
    {"name": "barrel_2", "type": "barrel", "pos": [0.035, 0, -0.06], "accepts": ["barrel", "splitter"]}
  ],
  "model": [
    {"size": [0.11, 0.04, 0.06], "pos": [0.0, 0.0, -0.03], "color": "steel"},
    {"size": [0.03, 0.03, 0.02], "pos": [0, 0, -0.005], "color": "black"}
  ],
  "stats": []
}
```

Other blocks: `receiver` (action, class, bore, soundOf, rpm, burstCount, burstDelay, spoolTime, spoolStart,
minBarrels, maxBarrels, rpmPerBarrel), `frame` for root grips (fireStyle, hands, carry, mobility, ads, ironHeight,
support, reloadPerReceiver; an older `fireStyles` list reads as its first style), `split` (splay, spreadMult; the old
`outputMult` is refused: splitters always conserve), `shared` for shared feeds (feeds; with `"mount": "hopper"`),
`carry` (weight relief, kg), `hilt` for melee roots (damage, swing, reach, cleave, stun, hands), `melee` for heads and
mods (damage, damageMult, swingMult, reach, cleave, stun), `hand`, `gripAngle`, `muzzles`, `optic`, `sight`,
`feedStyle`, `flags`, `requiresAction`, `mount`, `scrap`. Unknown keys, bad numbers, unknown colours and boxes far from the part are refused with the file and
the problem; the self-test loads every part file and feeds it broken ones.

Builds save as one line in `workbench.txt` (a new file next to the game; nothing older changes):
`Name|volley|Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}`.
A save naming a part that no longer exists, or a receiver past a grip's capacity, loads with that part dropped. The
style field is the grip's (an older save's own choice is ignored). Milestone 2 adds `melee=` (the melee slot),
`slots4=` and `dummies=` lines; files without them load as before.

## The starter set: 75 parts

Tiers: Common 24, Uncommon 14, Rare 16, Epic 10, Legendary 6, Wonder 5.

| Category | Common | Uncommon | Rare | Epic | Legendary | Wonder |
| --- | --- | --- | --- | --- | --- | --- |
| Grip (11) | Pistol Grip (3 receivers), Vertical Foregrip | Rifle Grip (9), Angled Foregrip | Competition Pistol Grip (3, rotate), Briefcase (4, hip fire), Chaingun Handle | Tactical Rifle Grip (9), Steering Wheel (8, rotate) | Chainsaw Grip (6) | Push Cart (16, hip fire) |
| Receiver (11) | Plinker (semi pistol), Stampede (auto pistol), Workhorse (auto rifle), Farmhand Pump (shell) | Longreach Bolt (heavy), Triad Burst (rifle) | Bucket Launcher, Gravedigger Auto (shell) | Hellwheel Rotary (minigun, 3-6 barrels, spool-up), Thumper Chain Gun (heavy, spool-up) | Sparkplug Energy | |
| Barrel (10) | Standard, Stub, Scattermouth, Launcher Tube | Long, Blunderbuss Bell | Big Iron, Fluted Match | Coil | | Barrel of Barrels (7 muzzles) |
| Splitter (5) | Y-Splitter (2 x 0.5) | Over-Under (2 x 0.55) | Trident (3 x 0.4) | Peacock (5 x 0.3, fanned) | Party Tray (2x2 at 0.4) | |
| Feed (12) | Box, Stick, Shell Tube, Single Breech | Extended, Heavy Box | Drum, Shell Drum, Energy Cell | Belt Box | Backpack Hopper (400) | Toaster Feed |
| Ammo (10) | 5.56 FMJ, 9mm Ball, 00 Buckshot, 40mm Frag | Slug | .50 BMG, Plasma Charge | Explosive Tip, Dragon's Breath | | Rubber Duck Rounds |
| Stock (4) | Fixed, Wire | | Recoil Buffer | | Gyro | |
| Optic (5) | Red Dot | Holographic | 2.5x ACOG | 4x Scope | 8x Sniper Scope | |
| Muzzle (4) | Flash Hider | Compensator, Muzzle Brake | Suppressor | | | |
| Utility (3) | Bayonet | Laser Sight | | | | Cup Holder (draws faster) |

Special flags (incendiary, explosive, penetrating, plasma, squeaky, bouncy, bayonet, suppressed, ...) are carried into the built weapon and shown in the
workbench; their gameplay effects come with the Zombies mode.

## Example builds

All of these are in the workbench (the first four fill the save slots on first run) and the self-test checks each one
is legal, deterministic and inside its documented DPS band. Numbers are from the CI self-test (body damage, close
range; DPS = damage per shot x shots per second, every projectile landing).

| Build | Grip | Receivers / barrels | Damage per shot | RPM | Mag | Reload | Mobility | DPS |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Plain Rifle**: Workhorse, standard barrel, flash hider, box mag, 5.56, fixed stock, red dot | Rifle Grip | 1 / 1 | 26 | 600 auto | 30 | 2.2 s | 0.95 | 260 |
| **Triple Pistol**: three Plinkers, stick mags, 9mm (volley) | Pistol Grip | 3 / 3 | 60 (3 x 20) | 420 semi | 20 | 2.4 s | 0.99 | 420 |
| **Nine Lives**: nine Workhorses with stub barrels, fixed stock | Rifle Grip | 9 / 9 | 234 (9 x 26) | 600 auto | 20 | 3.9 s | 0.67 | 2340 |
| **Twin Splitter Shotgun**: Farmhand Pump, Y-Splitter, two Scattermouths, buckshot | Rifle Grip | 1 / 2 | 112 (2 x 8 x 7) | 70 pump | 6 | 3.0 s | 0.95 | 131 |
| **Belt-Fed Chaingun**: Thumper, Big Iron, Belt Box, .50 BMG, chaingun handle | Rifle Grip | 1 / 1 | 85 | 450 (0.4 s spool) | 150 | 6.0 s | 0.86 | 637 |
| **Minigun**: Hellwheel with six standard barrels, Backpack Hopper, 5.56 | Tactical Rifle Grip | 1 / 6 (one per shot) | 26 | 2400 (1.2 s spool) | 400 | 9.0 s | 0.81 | 1040 |
| **Lumberjack**: six Gravedigger auto shotguns, shell drums, buckshot | Chainsaw Grip | 6 / 6 | 672 (6 x 8 x 14) | 260 auto | 20 | 5.6 s | 0.40 | 2912 |
| **Shopping Spree**: sixteen Stampede SMG receivers (hip fire only) | Push Cart | 16 / 16 | 320 (16 x 20) | 900 auto | 20 | 4.9 s | 0.40 | 4800 |
| **Party Platter**: Workhorse, Party Tray of four Tridents, twelve stub barrels | Rifle Grip | 1 / 12 | 50 (12 x ~4) | 335 auto | 75 | 3.4 s | 0.87 | 279 |
| **Toaster Duck**: Bucket Launcher, Launcher Tube, Toaster Feed, Rubber Duck Rounds | Pistol Grip | 1 / 1 | 90 | 90 semi | 2 | 1.0 s | 0.96 | 135 |

The big multi-receiver numbers (Lumberjack, Shopping Spree) are deliberately silly in this sandbox; in Zombies they
are gated by weight (both sit at the 0.40 mobility floor), part rarity and scrap cost, and by zombie health scaling with
rounds. Balancing them is part of the open questions.

## Milestone 1 checklist

- [x] Part data and validation (75 parts, 10 categories, 6 tiers).
- [x] Assembly rules with "why not" messages; deterministic stats into a `WeaponDef`.
- [x] Multi-barrel and multi-receiver firing from each muzzle socket; standard weapons unchanged; Last Stand rule.
- [x] Model from part models at sockets via the scene graph; hand anchors; optic sight line.
- [x] Workbench UI (parts inventory, live 3D preview, receiver slots, stat deltas, errors, 4 build slots, test fire
      in the Workbench Range).
- [x] Self-test section "Weapon builder" and a smoke step.

## Open creative questions (for the user)

1. **Volley or rotate by default?** Volley makes multi-receiver guns feel like one huge shot (and makes the 9-receiver
   rifle a monster); rotate makes them feel like one very fast gun. Should the player choose per build (as now, where
   the grip allows it), or should each grip have one fixed personality?
2. **Per-receiver feeds or a shared feed?** Today each receiver needs its own feed and ammo (mixed calibres work, but
   9 magazines is a lot of scavenging). Do you want a shared-feed part (one drum, many receivers), or should the feed
   live on the grip for some grips?
3. **How hard should receivers scale?** Should multi-receiver guns be held back mostly by weight and mobility (now), by
   a scrap cost per receiver, by heat/jamming, or by recoil that grows faster than the damage?
4. **The cart and the chainsaw.** Should the cart be a real object on the ground you push (slow turning, both hands on
   the bar, maybe you can let go of it and it stays put as a deployable turret)? Should the chainsaw grip also be a
   melee weapon (it is a chainsaw)?
5. **More silly grips?** The Steering Wheel (8 receivers round the rim, rotate) and the Briefcase (4 hidden receivers,
   hip fire) are in. Ideas waiting for a yes: a **Guitar** (receivers in the body, strum to fire), a **Leaf Blower**,
   a **Bicycle Handlebar**, a **Shield** with receivers behind it.
6. **Splitter balance.** Conserving splitters (1/N) are honest; the rarer ones multiply damage (up to 1.92x for a
   Party Tray of Tridents). Is that the reward for rarity you want, or should splitters always conserve and pay off in
   spread and coverage instead?
7. **Mystery box contents.** Complete standard configurations only (as planned), or occasionally a single Wonder part?
8. **Special ammo effects** (incendiary, explosive, penetrating, squeaking ducks) are flagged but have no effect yet:
   which matter most for Zombies, and should they stack across receivers?
9. **Slots.** 2 builds, 4 with the unlock: is the unlock bought once per match (perk machine) or once per account?
