# Zombies: the weapon builder (milestones 1 and 2)

Status: **in development**. Milestone 1 is the modular weapon system; milestone 2 applies the user's decisions (below):
grip-fixed fire styles, conserving splitters with damage multipliers and receiver upgrades, shared feeds, the late-game
weight path, standard complete builds, the real push cart, the melee slot and four weapon slots. All of it is playable
today as a sandbox: main menu > **WORKBENCH (ZOMBIES PREVIEW)**. The Zombies mode itself (World at War style, co-op 1-4,
rounds, new maps) comes later and plugs into the hooks described here.

Anything marked **proposed** is a working default the code implements so it can be tested. It is open for the user to
change. The "Open creative questions" at the end list the calls that are the user's to make.

Code: `Source/Zombies/` (PartDef, PartFile, WeaponBuild, BuildRules, BuildStats, BuildModel, BuildStore, MeleeStats,
StandardBuilds, RunTables, PushCart, BenchModel), firing in `Source/Game/GameBuilder.cs`, the match-side systems (cart,
slots, melee builds, practice dummies) in `Source/Game/GameZombies.cs`, UI in `Source/UI/UiWorkbench.cs` (the editing
model it draws is `Source/Zombies/BenchModel.cs`; part pictures `Source/Render/PartThumbnails.cs`), the range
session and cart drawing in `Source/App/Program.Workbench.cs`, self-test section "Weapon builder" in
`Source/App/SelfTestWeaponBuilder.cs` and `SelfTestWeaponBuilder2.cs` (and "Workbench UI" in `SelfTestWorkbenchUi.cs`). Data: `Parts/<Category>/*.json`, standard
builds in `Parts/Builds/*.json`, drop tables in `Parts/Tables/DropTables.json`, the range map
`Maps/Dev/WorkbenchRange.json`.

## The user's decisions (2026-10-21) and what was built

| # | Decision | Built in milestone 2 |
| --- | --- | --- |
| 1 | Fire style across receivers ~~is fixed by the grip~~ is a **per-build "Chain fire" checkbox** (user, 2026-10-23: "change the grips to have volley fire versus chain fire modes be a toggle with a checkbox instead of two separate groups of grips") | Each grip file's `"fireStyle"` is only its default; the build's choice is saved in its text's style field. The chain-fire twin grips (Competition Pistol Grip, Tactical Rifle Grip) merged into the Pistol Grip and Rifle Grip; old builds on them load there with Chain fire on. See "Grips and Chain fire". |
| 2 | **Weight** stays the main limiter; huge multi-receiver guns must be reachable late in a run | Rarer frames carry more (Mule Frame 16 kg / 6 receivers, Exo Harness 30 kg / 12, Gantry Rig 48 kg / 16), weight-relief parts raise the carry weight, and the drop tables put those tiers late (see "Run progression"). |
| 3 | The push cart is a **real pushed object**; parked, it's a **gun you man and fire yourself** (user, 2026-10-21: "A turret would be a little too strong. A turret can be added later, but it'd be a separate thing.") | Server-side cart physics (rolls with you, collides, step and slope limits, holds you at the bar), hands on the bar, F parks it, tap F at its bar to man its gun (aim within traverse limits, fire it by hand), hold F to push it again. No auto turret. Replicated (protocol 17). |
| 4 | **Splitters conserve total damage**; rarity buys spread/accuracy/outputs. Add **damage multiplier** parts and **receiver upgrade** parts on new receiver sockets | Every splitter gives 1/N per output (the file key `outputMult` is gone and refused); rarer splitters have `spreadMult` below 1 and more outputs. New categories: Damage Multiplier (6, one `amp` socket per receiver) and Receiver Upgrade (8, `upgrade_rear` and `upgrade_side` sockets). |
| 5 | A **shared feed** that feeds several receivers; per-receiver feeds still allowed | Five shared feeds on a grip's `hopper` socket, with rules for which receivers they feed, a pooled magazine and shared ammo. |
| 6 | **Mystery box = complete builds only** | `Parts/Builds/*.json`: 25 standard builds by tier with where they come from (box, wall, start); the workbench loads them as presets. |
| 7 | Weapon slots **2 -> 4** via an on-map upgrade, per run; the mode is a **roguelite** | Slots 3-4 exist in the game (keys 3 / 4, the swap key and wheel cycle them), unlocked per player per run (a flag the server owns); the range has a station (F) and the workbench a toggle. Drop tables by round are proposed data. |
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
 |- receiver_1 .. receiver_N    <- the grip decides N (first-tier grips 1, pistol grip 3, rifle grip 9, Gantry Rig and push cart 16)
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
| Grip | Root grips (capacity, fire style, how it is held, carry weight) and foregrips (off hand) | 17 |
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

**Bores.** Each receiver fires one calibre class (`pistol`, `rifle`, `heavy`, `shell`, `launcher`, `energy`). Feeds and
ammo list the bores they hold. **Barrels, splitters and muzzle devices fit every receiver**: the receiver adapts the
barrel (see "Barrels and receivers").

### Barrels and receivers (2026-10-23)

The user: "remove typing from weapon workbench weapons so that all barrels are compatible with all receivers. The
receiver will determine what type of modifications happen to the barrel ... if a barrel is attached to a pistol
receiver, it'll be smaller than if it's attached to a rifle receiver or an autocannon receiver. But this should remove
friction in matches."

- **Fit.** Every barrel fits every receiver's barrel socket and every splitter output; every muzzle device fits every
  barrel. Only the parts that hold the rounds stay typed: **feeds and ammo** (the ammo *is* the receiver's calibre, and a
  magazine's size is tuned to its rounds; shared feeds keep their bores and actions). Belts and hoppers still need an
  action that pulls them. So a refusal in a match is now about what really can't work (a 9 mm round in a .50 action),
  not about a tube.
- **Size.** The receiver's bore class sizes the barrel's model, from a table in `Parts/_Registry.json` (each bore's
  `"barrelScale": [width, height, length]`, relative to a rifle): pistol `[0.8, 0.8, 0.7]` (thinner, shorter), rifle
  `[1, 1, 1]` (as drawn), heavy `[1.35, 1.35, 1.3]`, shell `[1.5, 1.5, 1]`, launcher `[2.7, 2.7, 1.3]`, energy
  `[1, 1, 1.2]`. A receiver file can override its own size with `"barrelScale"` in its `receiver` block (the Hammerfall
  Autocannon: `[1.9, 1.9, 1.5]`). A barrel is drawn for one class (`"sizedFor"`, default `rifle`: Big Iron is drawn for
  `heavy`, Scattermouth and Blunderbuss Bell for `shell`, Launcher Tube for `launcher`, Coil for `energy`), so the scale is
  receiver size / drawn-for size: a Big Iron on a heavy receiver looks exactly as before, on a pistol it shrinks. The
  barrel's boxes, socket points, muzzle points and hand point all scale; a muzzle device on it sits on the scaled muzzle
  point and keeps its own size, and the shot leaves from the device's end. The receiver a barrel belongs to is always the
  nearest one up the tree (through splitters), never the build's first receiver.
- **Stats.** A barrel's stat changes are relative (`mul`, `add`, `grow`), so they apply on top of what the receiver and
  its ammo set: a Long Barrel's x1.4 range is 1.4 x a pistol's range on a pistol (a longer-range pistol, still a pistol)
  and 1.4 x the autocannon's on an autocannon. Each bore class's `"barrelStrength"` scales those changes (x m becomes
  x (1 + (m - 1) k), + a becomes + a k): pistol 0.85 (a short action gets less out of a long barrel), heavy 1.15,
  launcher 0.5, the rest 1. `"barrelPellets"` (shell only) says whether a barrel may change the pellet count: a
  Blunderbuss Bell on a rifle doesn't double its bullets.
- **The "fits nowhere" message** explains the real reason: if a socket's own rules take the part, it's the build's limit
  ("Too heavy: 10.8 kg; the Pistol Grip can't carry more than 10 kg."); otherwise the refusal of an empty socket of its
  type first, judged against the receiver that socket hangs under. (The bug report's screenshot: a Standard Barrel
  dropped on a Stampede under a Thumper chain gun on a Pistol Grip was refused with the Thumper's bore. The real
  refusal was the weight, 7.9 + 0.35 kg over the old 8 kg cap; the message blamed the first barrel socket it looked at.
  Now barrels aren't typed, the merged Pistol Grip carries 5 kg (10 at most) and the barrels go on.)
- **Saves.** Builds only gain options: every old build loads unchanged (old part files' barrel `"bores"` only say what
  the barrel is drawn for). Servers validate with the same rules (protocol 38).

### Graph rules (BuildRules.cs)

Every refusal comes with a plain sentence saying why. These are the actual messages:

| Rule | Example "why not" |
| --- | --- |
| A part only goes in a socket of its mount type | "Red Dot mounts on an optic socket, not a receiver socket." |
| A socket can limit what it takes | "Hellwheel Rotary Receiver's barrel_1 socket takes barrels only, not splitters." |
| A build starts from a grip (or a hilt) | "Rifle Grip is a grip you build on: start a new build with it instead." |
| Grip capacity | "The Pistol Grip holds 3 receivers; it has no receiver_4 slot." |
| Feeds and ammo must match the receiver's bore (barrels, splitters and muzzles fit any) | "Box Mag is made for rifle rounds; the Plinker Receiver fires pistol rounds." |
| Actions: belts and hoppers need something that pulls them | "Belt Box needs auto, chain or rotary action; the Triad Burst Receiver is burst." |
| Splitters stack at most 2 deep | "Splitters only stack 2 deep: the shot gets too thin." |
| Barrels per receiver (default 12, the Hellwheel takes 3-6) | "Too many barrels on the Workhorse Receiver: 13; it handles at most 12." |
| Limits per weapon: 96 parts, 32 barrels, 240 projectiles per pull | "Too many projectiles per trigger pull: 256; the most is 240." |
| Weight: a grip carries up to 2x its carry weight (plus weight relief) | "Too heavy: 10.8 kg; the Pistol Grip can't carry more than 10 kg." |
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
3. **Combine the receivers** by the build's fire style (the Chain fire checkbox, see below).
4. **The grip and what's on it:** grip, stock, foregrip, optic and utility parts, applied once to the whole weapon.
5. **Weight against the grip's carry weight** (**proposed**; the carry weight is the grip's plus every weight-relief
   part's `"carry"`): mobility `-0.012/kg`, and `-0.03/kg` over the carry weight; ADS time `+0.008 s/kg` (+0.02 over); draw time `+0.03 s/kg` (+0.05 over); recoil times
   `sqrt(6 / (kg + 4))` (heavy guns kick less per shot, clamped 0.55-1.25x). Then the frame's own mobility (a cart
   is slow however light it is).
6. **Clamp** everything into the ranges weapon files allow (mobility 0.4-1.2 for builds).

### Several receivers: volley or chain fire

| | Volley (Chain fire off) | Chain fire (on; "rotate" in files and saves) |
| --- | --- | --- |
| A trigger pull | Fires every receiver at once | Fires the next receiver in turn |
| Fire rate | The slowest receiver's (they are linked) | The receivers' rates add up (max 3000 rpm) |
| Magazine | Per-receiver feeds, shown as the smallest (all run dry together); a shared pool lasts pool / receivers it feeds | Feeds add up; a shared pool counts once |
| Recoil | Root of the sum of squares (3 receivers = 1.7x one) | The average |
| Trigger | The strictest: semi beats burst beats auto | Same |
| Reload | The longest feed, plus 0.2-0.35 s per extra receiver (the grip sets it); every feed is swapped at once | Same |

### Grips and Chain fire (2026-10-23)

**One grip per slot count, and a checkbox.** Every multi-receiver build has a **Chain fire** checkbox in the build
panel (off = volley, all together; on = the receivers take turns, one per shot). It is saved with the build (the style
field of its text: `Name|volley|...` or `Name|rotate|...`; `grip` or anything else = the grip's default), one undo step,
kept when you change grip, and validated by the server like the rest of the build. A grip file's `"fireStyle"` is only
the default for a new build (Steering Wheel and Exo Harness default to chain fire, the rest to volley); standard build
files may say `"chainFire": true/false`.

The chain-fire twins merged: **Competition Pistol Grip -> Pistol Grip**, **Tactical Rifle Grip -> Rifle Grip**
(`WeaponBuild.Renamed`). The merged grips keep the larger carry weight (Pistol Grip 5 kg, Rifle Grip 12 kg) and gained the
twins' utility socket, so every old build on them loads complete, with Chain fire on (their saves say `rotate`).

**Slot counts 1 to 9.** The gap between the 3-slot Pistol Grip and the 9-slot Rifle Grip is filled, each a little
heavier and slower to handle than the last:

| Slots | Grip | Tier | Carry | Weight | Mobility | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Service Pistol / Service Rifle / Sport Rifle Grip | common / uncommon | 4 / 8 / 10 kg | | | first tier |
| 2 | **Double-Tap Grip** | rare | 4.5 kg | 0.42 kg | | one-handed, side by side |
| 3 | Pistol Grip | rare | 5 kg | 0.4 kg | | one-handed |
| 4 | **Quad Rack Grip** | rare | 7 kg | 0.7 kg | x0.99 | 2 over 2, stock and foregrip |
| 5 | **Penta Rack Grip** | rare | 8 kg | 0.8 kg | x0.98 | 3 below, 2 above |
| 6 | **Hex Rack Grip** (also Mule Frame, Chainsaw Grip) | rare | 9 kg | 0.85 kg | x0.98 | shared-feed socket |
| 7 | **Septet Rack Grip** | epic | 10 kg | 0.9 kg | x0.97 | third tier, shared feed |
| 8 | **Octet Rack Grip** (also Steering Wheel) | epic | 11 kg | 0.95 kg | x0.97 | shared feed |
| 9 | Rifle Grip | epic | 12 kg | 1.0 kg | | shared feed |

The grip picker lists grips by slot count.

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
  rolled distance, gun aim) and `RunState` (server -> one client: the slot upgrade). **Protocol 17** (the manned cart)
  changes two of them: `CartUse` carries one byte (0 tap, 1 hold) and `CartState` carries the manned gun's aim (yaw
  offset from the cart's facing and pitch, 30 bytes per cart) instead of turret seconds; cart mode 2 is now "manned".
  Built
  weapons and melee builds are still host-local in the Workbench Range (single player). **In the Zombies mode
  (protocol 18) they replicate**: the server sends every build's text in `ZBuild` and each client assembles the same
  weapon (see ZOMBIES-MODE.md). Until then, clients draw other players' carts as a bare
  Push Cart, and a remote pusher's own movement isn't predicted against the cart (the server corrects it).

## The workbench (the UI, 2026-10-22)

The user asked for "a nicer, cleaner UI so that your gun builds are presented in a nice linear fashion", a grip first,
drag and drop from an inventory on the left that only snaps to the right spots, faster and more fun, copying a whole
receiver assembly when you're missing the parts, and a way to see which parts are missing. What's built:

- **Step 1: the grip.** "New weapon" (key N, and any empty slot) opens the grip picker: a card per grip with its 3D
  picture, receiver slots ("1 RECEIVER SLOT" for the first tiers), fire style, hands, carry weight and how many you own
  (in a run). A grip you don't own can still be picked to plan a build: it shows as missing. "Change grip" (G) moves the
  current build onto another grip (receivers into the same-numbered slots).
- **Step 2: assembly.** Left: your parts, grouped by category with counts (a run: loose parts plus the slot's own), a
  search box and category chips, 3D icons (rendered once into textures and cached, `PartThumbs`), a tooltip with what
  the part does. A **Show incompatible parts** checkbox (saved in `settings.txt` as `wb_show_incompatible`, off by
  default) decides what's listed: off, only parts that fit somewhere on the build and have a spare copy (a "(N hidden)"
  hint); on, every part you own, the rest dimmed with a short reason in the tooltip ("needs a shell bore feed socket",
  or "none spare" when every copy is already on the build) and bouncing back if dropped. The chip counts follow the toggle and the search (`BenchInventory`).
  With the toggle off, **whole categories hide** (no chip, no group header) when nothing you own in them could go on
  the build right now: the same per-part check as the list (a free *or occupied* socket whose rules take it, since a
  swap counts, plus a spare copy). An empty grip shows no Feed, Ammo, Barrel or Splitter until a receiver is fitted
  (barrels, splitters and muzzles fit every receiver but still hang off one; feeds and ammo must match its bore), and a
  grip without a stock socket (Pistol Grip) shows no Stock. They come back the moment the build changes. A chosen chip
  whose category hides falls back to All, and clicking an empty socket row whose category is hidden says so instead of
  opening an empty list. With the toggle on every category you own a part of is shown; categories you own none of
  (a run's stock) stay hidden either way. Visibility is worked out once per build/stock/toggle change, each part's fit
  checked once (self-test section "Workbench hidden categories"). Middle: the gun large
  (drag to orbit, wheel to zoom, double-click or R to reset) with socket markers: yellow rings on empty sockets the
  build needs, red on missing parts, orange on the selection. Right: the build as a **linear list** (Grip > Receiver(s) >
  Feed > Ammo > Barrel(s) > Muzzle > damage multiplier / upgrades > Stock > Optic > Foregrip > Utility, a splitter's
  barrels indented under it, empty needed sockets as yellow "+ Feed: needs feed" rows) and **stat bars** (damage, fire
  rate, recoil, magazine, DPS, reload, mobility, weight against the grip's carry) that show the change in green and red
  while you hover a part or drag it over a socket.
- **Drag and drop.** Drag a part from the list: every socket it fits glows green (cyan: it would swap out the part
  there), the one under the mouse previews the part snapped in place (highlighted) with the stat changes, and releasing
  snaps it in with a click, a little camera nudge and the part sliding into its socket. Rows of the linear list are drop
  targets too. A refused drop bounces back to the list with the reason ("Box Mag is made for rifle rounds; the Plinker
  Receiver fires pistol rounds."). **Swap semantics:** a part dropped on an occupied socket replaces it, and what was
  plugged into the old part moves onto the new one where it has the same socket and still fits (a swapped barrel keeps
  its muzzle device; a swapped receiver keeps barrel, feed and ammo of its bore); the rest goes back to your parts.
  Drag a part off the gun (or a row off the list) to take it off, or onto another socket to move it with its parts.
  **Double-click** a part in the build list or on the gun to take it off, back to your parts (2026-10-23). What's plugged
  into it comes off with it (a receiver brings its barrel, feed and ammo), the same as dragging it off, and one Undo puts
  the whole assembly back: re-homing the children elsewhere would move parts you didn't touch. The grip can't come off
  (Change grip); double-click on an empty socket row does nothing. Double-click in the parts inventory still auto-places,
  and double-click on empty space in the view resets the camera.
- **Fast.** Double-click a part: it goes to its best free socket (needed ones first). **Auto-complete** (A) fills every
  empty needed socket with parts you have (the rarest that leads to a complete build first), as one undo step.
  Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo and redo (64 steps), Del takes the selected part off, C copies the selected
  receiver, D (twice) disassembles the slot's build at a run's workbench, Esc cancels a drag, leaves the picker, then
  closes.
- **Copy receiver assembly.** Right-click a receiver (on the gun or in the list) > "Copy receiver assembly" duplicates
  it and everything under it onto the next free receiver slot (refused only when the grip is full or the build's limits,
  like weight, say no). Without the parts it still goes on: the copy's parts you don't own are drawn as red ghosts on
  the gun, red rows with a MISSING tag in the list, and the strip under the view says "Missing: 1x Long Barrel, 1x
  Compensator, 1x Extended Mag". The build can't be saved or applied until they're owned.
- **Missing parts everywhere.** Saved builds and presets (standard builds load as plans) carry a red "[N missing]"
  badge, the Save / Apply button's tooltip lists what's missing and what's still needed, and the run's workbench offers
  **CRAFT MISSING & APPLY (N scrap)** when every missing part can be crafted this round (3x its scrap value; never wonder
  parts, nor tiers above what the round's drop table rolls). The sandbox's **Practice parts** toggle (two of every
  common part, one of every uncommon) lets you try all of this without a run.
- **Disassemble (a run's workbench, 2026-10-23).** The user asked: "allow for the user to completely disassemble a gun
  into inventory items from the workbench". **Disassemble [D]** beside Revert takes the selected slot's whole build
  apart, the grip or hilt included: every part goes to your loose parts and the slot is left empty (the editor goes to
  the grip picker, ready for a new build from your parts). It's destructive-ish, so it needs **two clicks** (or D twice)
  within 4 s: the first arms it (the button turns red and the strip says how many parts will come off). One **Undo**
  (the footer's "Undo disassemble" button, or Ctrl+Z once the new build has nothing left to undo) puts the same build
  back from your loose parts while the bench is open (it's an ordinary Apply: the server checks the parts again). The
  footer also shows the slot buttons while the grip picker is up, so an empty slot isn't a dead end. Breaking parts
  down for scrap stays a separate action (the Loose parts list). The rules, the network and the empty slot are in
  ZOMBIES-MODE.md, "Disassembling a build". **The main menu's sandbox has no Disassemble**: its stock is every part
  without limit (or the fixed practice stock), there's no inventory for parts to go back to, so taking a build apart
  would change nothing; "New weapon" (N) already starts from an empty grip there.
- **Server authority is unchanged**: Apply sends the build's text in `ZBench` and the server checks the parts again;
  crafting the missing parts is the existing craft request, sent once per part before the build (reliable, ordered).
  No new messages for these (Disassemble is a new `ZBench` kind, protocol 41). The workbench is a menu for the cursor
  rule (`CursorPolicy`): the mouse stays free.
- **Cost.** The editing model (`BenchModel`) does its allocating work only when the build changes (assembling, the
  list, the sockets, what's missing); the per-frame calls (camera, projection, the ray pick, the stock fingerprint)
  allocate nothing (self-test). Part pictures render 8 per frame at most, once per part per session.

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
  it. Iron-sight boxes (`"tag": "irons"`, only grips have them) stand on the receiver: they are drawn only when a
  receiver is fitted and no optic is. Grips with `"ads": false` (cart, briefcase) are hip fire only and drop the optic's
  zoom.
- **Conditional boxes:** `"tag": "with:SOCKET"` draws a box only while that socket of the same part holds something (the
  hopper chute under the rifle and rack grips, the big frames' stock mounts). A part file naming a socket it doesn't
  have is refused.
- **Nothing floats (2026-10-23).** The user: "remove the weird frame pieces that float above the grips when it's not
  intentional and just there to support the optics". Every part's boxes form one connected piece (within 1 cm), and
  every part of an assembled weapon touches the rest. The pass fixed: the grips' iron sights (hovering over an empty
  plate; now only with a receiver, front post moved back over the receiver and both reaching down to it); the rack
  grips' cage posts (Quad to Rifle Grip: they stood 4.4 cm behind the plate, so the crossbar, optic rail and irons hung
  in the air; now on the plate's back corners); the Gantry Rig's optic mount (a box and post hanging between the
  crossbars; now on a spine between them); the Chainsaw Grip's receiver plate (3 cm off the body); the Steering Wheel's
  rim (gaps between segments; diagonal spokes now carry the diagonal receivers); the Exo Harness, Gantry Rig and Mule
  Frame plates (1.7 cm under the receivers; shelf bars under each upper row); hopper chutes and stock mounts on the
  frames whose hopper or stock socket hangs off them; the push cart's basket floor; small fixes to the Longbow screw,
  the Street Sweeper drum, the Shoulder Harness, the Frozen Fish (on a short line now) and the Whoopee Cushion.
  Sockets, stats and sight heights are unchanged. `python3 tools/parts/floaters.py` lists floaters; the self-test
  section **Model connectivity** fails on any, with an allow-list for intentional separate pieces (the cooling fins of
  the Bastille, Sawmill and Hammerfall receivers, which the barrel runs through; the Barbed Wire Wrap and Nail Ring on
  the hilt; the push cart's upper rows of receivers, piled in the basket).

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

## Making parts (fragments, bases, hot reload, tools)

Parts are data, and the folder is set up for fast iteration. Nothing here changes what a part *is*: everything resolves
at load time into the same part a file written out in full would give (the self-test proves it), so builds, saves, the
network and the fingerprint can't tell the difference. Top-level entries in `Parts/` starting with `_` are never parts.

| Where | What |
| --- | --- |
| `Parts/_Template/` | `Template.json`: every key, commented, to copy into a category folder. Two more examples show `extends`. Checked by the self-test, never in the game. |
| `Parts/_Fragments/*.json` | Reusable model pieces (format `call-of-slop-fragment/1`: `name`, `about`, `model`): Picatinny Rail, Screw Head, Cooling Fins, Drum Shell, Scope Tube, Magazine Body, Barrel Shroud, Muzzle Ring, Trigger Guard. |
| `Parts/_Bases/*.json` | Templates to `extends` (Base Barrel, Base Box Mag, Base Auto Receiver, Base One-Receiver Grip: the first-tier grips extend it). Never in the game; may leave things out. |
| `Parts/_Registry.json` | Socket types, bores and each category's default mount and display name (format `call-of-slop-part-registry/1`). |

**Fragments.** In a `model` list, `{"use": "Picatinny Rail", "pos": [0, 0.07, -0.02], "rot": [0, 0, 0], "scale": 1.5,
"color": "accent"}` brings the fragment's boxes: scaled along the fragment's own axes (a number or `[x, y, z]`; exact for
boxes that aren't turned inside the fragment), turned by `rot` (the box's own turn first, then the use's), then moved by
`pos`. `color`, `alpha` and `tag` override every box it brings. Fragments may use fragments (8 deep at most; a loop is
reported). The 64-box limit counts the expanded boxes.

**extends.** `"extends": "Base Barrel"` (a base or any other part) starts from that file:

- the part's own keys replace the base's, objects (`receiver`, `frame`, `split`, `hilt`, `melee`...) merge key by key
  (`"receiver": {"rpm": 760}` keeps the action, class and bore), and `null` removes a key;
- `"+model"`, `"+sockets"`, `"+stats"`, `"+muzzles"`, `"+flags"`, `"+bores"` append to the base's lists (`+sockets`
  replaces a base socket with the same name);
- `"-sockets"`, `"-stats"`, `"-flags"`, `"-bores"` remove entries by name (a socket's name, a stat's `stat`).

A variant is a few lines (`Parts/_Template/ReceiverVariantExample.json`):

```json
{
  "extends": "Base Auto Receiver",
  "name": "Template Sprinter Receiver",
  "rarity": "uncommon",
  "flavor": "The workhorse after a strong coffee.",
  "receiver": {"rpm": 760},
  "-sockets": ["upgrade_side"],
  "+model": [{"use": "Screw Head", "pos": [0.031, 0.05, -0.1], "rot": [0, 0, -90]}]
}
```

**The registry.** Adding a socket type (a bayonet lug, a second rail) or a bore is a line in `_Registry.json`; part
files can use it straight away. The core socket types the rules in code rely on (receiver, barrel, feed, ammo, stock,
foregrip, optic, muzzle, hopper, head) can't be removed; the categories themselves are fixed in code (each one is
treated differently by the stats and rules), the registry gives each its default mount and workbench name (grip, hilt
and receiver mounts are fixed). Without the file the game uses the same built-in lists.

**Hot reload.** Run `CallOfSlop.exe --watch-parts` and open the workbench: saving any file under `Parts` reloads the
parts within a second and redraws the preview. In the Workbench Range or an offline Zombies game the guns in your hands
are rebuilt in place too (same slots; ammo refilled). The pause menu has **RELOAD PART FILES** (Zombies and the range)
for the same by hand. How it works: a part that already existed keeps its object and gets the new fields, so every build
holding it follows; built weapons are re-assembled from their build text (`WeaponDef.BuildText`); a file with an error
keeps its previous version until you fix it (the error is shown); new files are added, deleted ones drop out. The
standard builds (`Parts/Builds`) are re-read too. Rules: only offline or as a host with nobody connected (other players
built their weapons from the old files). Every Welcome now carries the part set's fingerprint (`PartSet`, after the
existing fields, so older builds ignore it): a joiner whose Parts folder differs is told so and disconnected, like
weapons and attachments.

**Tools.**

- `CallOfSlop.exe --check-part FILE`: a part (or fragment, base, registry) file: errors, sockets, every socket it fits,
  and the set-wide problems it causes. Fragments and bases come from the Parts folder the file is in.
- `CallOfSlop.exe --check-parts [DIR]`: every part, fragment and template, plus the set-wide checks: a part that fits
  nowhere (no socket of its mount type takes its category), a socket nothing fits; notes for unused socket types and
  `accepts` entries nothing can fill. Prints the part set fingerprint. Exit code 1 on any problem.
- `CallOfSlop.exe --export-parts DIR`: writes every part, fragment and base file normalised (keys in the standard order,
  one socket, box or stat per line; comments dropped), for tidy diffs. `--resolved` writes each part out in full instead
  (`extends` merged, fragments expanded into plain boxes).
- `python3 tools/parts/floaters.py [Parts] [--receiver NAME]`: boxes floating free of their part, of an assembled grip
  (empty, one receiver, every receiver, an optic, everything) or of a standard build, like the self-test's Model
  connectivity section. Exit code 1 on any not on its allow-list.
- `python3 tools/parts/viz.py [Parts] [out.png]`: a contact sheet of every part with its sockets (labelled, coloured by
  type), muzzles and hand points, without the game. `--fragments` draws the fragment library, `--templates` adds the
  examples, `--only NAME...`, `--category barrel`, `--lint` runs the set-wide checks only.

The self-test section **Part library** checks the registry against the built-in lists, that every fragment expands and
every template loads, that `extends` and fragment uses give byte-identical parts, the merge rules, readable errors for
broken files (unknown fragment or base, loops, typos, bad scale, unknown socket type, broken registries), the set-wide
checks, that normalising is stable and lossless, and hot reload from a temporary copy (in-place updates, a broken file
kept, a new part added, the fingerprint moving and refusing a joiner, weapons in hand rebuilt, `workbench.txt` still
loading) before restoring the real folder.

## The part set: 142 parts

Milestone 1's 75 parts, 48 from milestone 2 and the three one-receiver grips of the workbench rework. By tier, all
categories (first-tier grips, common and uncommon, hold exactly **one receiver**: the user's call, 2026-10-22):

| Category | Common | Uncommon | Rare | Epic | Legendary | Wonder |
| --- | --- | --- | --- | --- | --- | --- |
| Grip (21) | **Service Pistol Grip (1, 4 kg)**, **Service Rifle Grip (1, 8 kg)**, Vertical Foregrip | **Sport Rifle Grip (1, 10 kg)**, Angled Foregrip | Double-Tap (2), Pistol Grip (3, 5 kg), Quad Rack (4), Penta Rack (5), Hex Rack (6), Briefcase (4, hip fire), Chaingun Handle, **Mule Frame (6, 16 kg)** | Septet Rack (7), Octet Rack (8), Rifle Grip (9, 12 kg), Steering Wheel (8, chain fire), **Exo Harness (12, chain fire, 30 kg)** | Chainsaw Grip (6), **Gantry Rig (16, 48 kg)** | Push Cart (16, a real cart) |
| Receiver (23) | Plinker, Stampede, Workhorse, Farmhand Pump, **Courier Carbine** (rifle, light auto), **Buzzbox Machine Pistol** (pistol, 1100 rpm) | Longreach Bolt, Triad Burst, **Kingpin Revolver** (heavy rounds, pistol action), **Reaper Battle Rifle** (rifle semi), **Ratchet Burst SMG** (pistol burst) | Bucket Launcher, Gravedigger Auto, **Longbow DMR** (rifle semi, tight ADS), **Street Sweeper** (shell semi), **Bastille LMG** (rifle auto) | Hellwheel Rotary, Thumper Chain Gun, **Railspike Coil** (energy bolt) | Sparkplug Energy, **Hammerfall Autocannon** (heavy auto, barrels x1.9 thick) | **Pepperbox** (pistol rotary, 2-6 barrels), **Sawmill** (shell chain gun) |
| Barrel (10) | Standard, Stub, Scattermouth, Launcher Tube | Long, Blunderbuss Bell | Big Iron, Fluted Match | Coil | | Barrel of Barrels |
| Splitter (6) | Y-Splitter (2) | Over-Under (2, spread x0.85) | Trident (3, x0.8) | Peacock (5 fanned, x0.75) | Party Tray (4, x0.7) | **Hydra (6, x0.6)** |
| Feed (17) | Box, Stick, Shell Tube, Single Breech | Extended, Heavy Box | Drum, Shell Drum, Energy Cell, **Manifold Hopper (shared, 3)** | Belt Box, **Shell Hopper (shared, 4)**, **Ammo Can Manifold (shared, 6)** | Backpack Hopper, **Bottomless Bucket (shared, 9)**, **Power Bus (shared, 8)** | Toaster Feed |
| Ammo (10) | 5.56 FMJ, 9mm Ball, 00 Buckshot, 40mm Frag | Slug | .50 BMG, Plasma Charge | Explosive Tip, Dragon's Breath | | Rubber Duck Rounds |
| Damage Multiplier (6, new) | Hot Load Kit | Overpressure Chamber | Tungsten Core Injector | Rail Accelerator | Singularity Chamber | Spite Amplifier |
| Receiver Upgrade (8, new) | Lightweight Bolt, Match Trigger Group | Buffer Spring Kit, Heavy Hammer | Gas Overclock, Coil Hammer | Twin Sear | Overclock Core | |
| Stock (5) | Fixed, Wire | | Recoil Buffer, **Shoulder Harness (+5 kg carry)** | | Gyro | |
| Optic (5) | Red Dot | Holographic | 2.5x ACOG | 4x Scope | 8x Sniper Scope | |
| Muzzle (4) | Flash Hider | Compensator, Muzzle Brake | Suppressor | | | |
| Utility (6) | Bayonet, **Padded Sling (+1.5 kg)** | Laser Sight | | | **Anti-Grav Coil (+12 kg)** | Cup Holder, **Helium Balloons (+15 kg)** |
| Hilt (6, new) | Wooden Handle, Bat Grip | Long Haft, Fishing Rod Grip | Sword Hilt | Motor Hilt | | |
| Head (8, new) | Machete Blade, Bat Barrel | Sledgehammer Head, Fire Axe Head | Katana Blade, Frying Pan | Chainsaw Blade | | Frozen Fish |
| Melee Mod (7, new) | Nail Ring, Grip Tape | Counterweight, Barbed Wire Wrap | Shock Coil | | Rocket Booster | Whoopee Cushion |

Special flags (incendiary, explosive, penetrating, plasma, squeaky, bouncy, bayonet, suppressed, chainsaw, fish, ...) are
carried into the built weapon and shown in the workbench; their gameplay effects come with the Zombies mode.

## Weight and run progression (proposed, open for the user)

Weight stays the main limiter (decided). A grip carries up to twice its carry weight, and the build gets slower and
slower to aim and move past its carry weight. Bigger, rarer frames carry more and hold more receivers, and
weight-relief parts (`"carry"`) raise what any grip carries, so the huge guns are a **late-run** thing:

| Rounds (proposed) | What drops (part tiers) | Frames you can expect | What a build looks like |
| --- | --- | --- | --- |
| 1-2 | common 85%, uncommon 15% | Service Pistol Grip (4 kg), Service Rifle Grip (8 kg), Sport Rifle Grip (10 kg): **one receiver each** | the start builds: Wall Rifle, Wall Pistol, a Machete |
| 3-7 | + rare 8% | + Double-Tap (2), Pistol Grip (3), Quad / Penta / Hex Rack (4-6), Briefcase | the first two- to six-receiver guns, a Y-splitter shotgun, first upgrades |
| 8-14 | rare 24%, epic 6% | + **Mule Frame (16 kg, 6)**, Shoulder Harness | three to six receivers, a Manifold Hopper, Gas Overclock |
| 15-24 | epic 20%, legendary 7%, wonder 1% | + **Exo Harness (30 kg, 12)**, Septet / Octet Rack (7, 8), Rifle Grip (9), Steering Wheel | ten-plus receivers on shared feeds, damage multipliers |
| 25+ | legendary 17-28%, wonder 5-12% | + **Gantry Rig (48 kg, 16)**, Anti-Grav Coil, the Push Cart | broken, silly, sixteen receivers (Gantry Sixteen, Shopping Spree) |

The numbers live in `Parts/Tables/DropTables.json` (format `call-of-slop-drops/1`): part tiers by round, the chance a
zombie drops a part by round (4% -> 10%), the mystery box's build tier by round, and prices (box 950, slot upgrade 4000,
wall weapon 500). `RunTables.Roll(rows, round, seed)` rolls deterministically (the same seed gives the same tier on
the server and every client). Nothing rolls them yet: the Zombies mode will. Bad parts along the way (heavy, slow,
drawbacks on multipliers) are what make a run hard.

## Standard builds: the mystery box and wall weapons

The mystery box gives **complete builds only** (decided). They live in `Parts/Builds/*.json`, one per file:

```json
{
  "format": "call-of-slop-build/1",
  "name": "Twin Splitter Shotgun",
  "tier": "uncommon",
  "source": ["box"],
  "flavor": "A pump shotgun through a Y-splitter: twice the pellets, half the damage each, the same total.",
  "build": "Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel,barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:00 Buckshot},stock:Wire Stock}"
}
```

`source` says where it shows up: `box` (mystery box), `wall` (wall weapon), `start` (a run's starting gear). Every file
must assemble into a complete, legal build with today's parts, or it's refused with the reason (self-test). The
workbench lists them under **Standard builds** and loads one as a preset into the editor. An optional `"chainFire": true`
(or false) sets the Chain fire checkbox; without it the build uses the grip's default. The 30 shipped:

| Tier | Guns | Melee |
| --- | --- | --- |
| Common | Wall Rifle (wall, start), Wall Pistol (wall, start), Wall Pump (wall), Wall SMG (wall): all on the one-receiver Service grips | Machete (start, box) |
| Uncommon | Twin Splitter Shotgun, Burst Marksman (box, wall): on the Sport Rifle Grip; Kingpin Hand Cannon (a heavy-round revolver, its Long Barrel sized down) | Nail Bat, Fire Axe |
| Rare | Hopper Triplets (Mule Frame, shared feed), Auto Shotgun, Triple Pistol (the three-receiver Pistol Grip), Double-Tap Twins (chain fire), Longbow Marksman | Sledgehammer, Frying Pan |
| Epic | Minigun, Belt-Fed Chaingun, Exo Twelve (12 receivers, chain fire, ammo can manifold) | Chainsaw Sword |
| Legendary | Gantry Sixteen (16 receivers, bottomless bucket), Lumberjack, Hammerfall Cannon (autocannon) | Rocket Katana |
| Wonder | Shopping Spree (the push cart), Toaster Duck, Hydra Platter, Pepperbox Pistol | The Codfather (a frozen fish on a rod) |

## Weapon slots: 2 -> 4 (decided: an on-map upgrade, per run)

- The game has **four weapon slots** now. Slots 3 and 4 only open once the player's run flag `ExtraSlots` is set
  (server-owned, sent to that client in `RunState`); otherwise everything behaves exactly as before (2 slots).
- Keys: **3** = slot 3, slot 4 is unbound by default (bind it in the settings), and the swap key and mouse wheel cycle
  through every slot you have. The HUD lists your other slots.
- **Workbench Range:** a station stands by the spawn line (right side); **F** there unlocks slots 3-4 (free in the
  sandbox; proposed price 4000 points in Zombies). The workbench has a toggle to start with them unlocked; slots 3-4
  take the workbench slots that aren't the selected or second one.

## The push cart (a real object)

The Push Cart grip is now a life-size shopping cart (bar at about waist height, sixteen receivers in the basket).
When you hold a cart build, a **cart entity** exists in the world (server-authoritative, `GameZombies.cs`; physics in
`PushCart.cs`):

- **Pushed.** Every tick the cart turns toward where you face (2.2 rad/s: carts turn slowly) and rolls to a spot in
  front of your feet (up to 7 m/s), colliding with the world as a box. It **rolls over curbs up to 15 cm**, but **not
  higher steps** and **not slopes steeper than 22 degrees** (front wheels against back wheels). When it's stuck it
  slides along the wall if it can; otherwise **you are held at the push bar** (you can't walk through your own cart).
  It's slow to push because a cart build's mobility is low (the frame's 0.65 times the weight penalty).
- **Hands on the bar.** In first person the cart isn't a held gun: it's drawn where it really is on the ground (its
  world pose in camera space) and both arms IK onto the push bar; it rattles a little as it rolls. Your eyes are well
  above the basket, so the view swings the cart up about the eye until its basket is no more than 17 degrees below the
  middle of the screen (the hands follow the bar in from the bottom). **It doesn't fire
  while you push it** (decided with the manned gun: both hands are on the bar, and it keeps "park it, then man it" the
  one way to use its firepower). Pulling the trigger tells you how.
- **Parked (F while pushing).** The cart stays where it is and you go back to the weapon you held before. It does
  nothing on its own. Switching weapons while pushing parks it the same way. A dead owner's cart is gone (it comes
  back in front of you on respawn).
- **Manned (tap F at a parked cart's bar).** Decided by the user: **a gun you fire yourself, not a turret**. You stand
  at the push bar (your feet are held there; the movement keys do nothing) with your hands on its handles, and the
  cart body stays put. The view sits low behind the gun (just over the push bar, a step back) so the gun in the basket
  is in view, and your shots leave from there. The mouse turns its gun (everything mounted on the cart: the receivers
  and feeds in the basket turn as one block about their middle) up to **60 degrees either side** of the cart's facing,
  **15 degrees down and 30 up** (proposed); your view is held inside those limits. The fire key fires the cart's build
  through the normal server-authoritative shot path with the same stats (damage, fire rate, spread, rays per
  receiver), reload works as usual, and the HUD shows the cart gun's ammo and "Leave: F   Push it: hold F". The
  server clamps every shot's direction to the traverse limits too.
- **Leaving.** Tap F again, or jump or sprint: you step off and get the weapon you held before back. **Hold F**
  (0.4 s) at a parked or manned cart to push it again; tap F stays "man it / step off". Selecting the cart's slot next
  to it also picks it up; far away you're told where it is.
- **An auto turret may come later as a separate deployable.** The turret behaviour (auto-targeting, 30 s, two
  magazines) is removed from the cart entirely; nothing of it is left in the code.
- **Network:** the server sends every cart in `CartState` with the snapshots while any exist; the interact key is
  `CartUse` (see "Firing" for the protocol notes and what co-op still needs).
- **Workbench Range:** a cart lane on the left of the spawn (x -12): an 8 cm curb the cart rolls over and a 30 cm step
  it can't climb. The workbench's "Practice dummies" setting (0-8, default 4) puts dummies down the range for the
  cart gun, cleave and stun.

## The melee slot (decided: melee-only parts)

Today's melee (a quick knife / butt strike on V, `Melee.cs`) stays exactly as it is in normal multiplayer modes. In
the Workbench Range (and Zombies later) the **melee slot** replaces it with a built melee weapon:

- **Parts:** a **hilt** (the root: base damage, swing time, reach, cleave, hands), a **head** (required) and up to two
  **mods**. Heads and mods change the numbers with a `melee` block: damage added then multiplied, swing time
  multiplied, reach / cleave / stun added. Every kg over 2 adds 0.03 s to the swing. A melee weapon weighs 14 kg at
  most. Models are built like guns (parts at sockets, along -Z from the hand).
- **Stats:** damage per target, swing time, reach from the eyes, **cleave** (how many targets one swing hits, nearest
  first; cleaving weapons sweep a wider arc), **stun** (seconds a hit target stands still: bots today, zombies later;
  players aren't stunned). From behind it does double damage.
- **The swing** uses the existing first-person melee animation (the one-handed arc, arms IK), with the built model in
  your hand for the swing, timed to the weapon (a bit over half its swing time).
- **Server:** the same lag-compensated, line-of-sight-checked hit test as today's melee, with the build's reach, arc
  and cleave; one damage event per target.
- **Workbench:** the **MELEE SLOT** switch shows hilts, heads and mods, the melee stats with deltas, and saves to the
  melee slot (`melee=` in `workbench.txt`); "Use the standard strike" clears it.

| Example | Parts | Damage | Swing | Reach | Cleave | Stun |
| --- | --- | --- | --- | --- | --- | --- |
| Machete | Wooden Handle, Machete Blade | 50 | 0.59 s | 2.1 m | 2 | 0 |
| Nail Bat | Bat Grip, Bat Barrel, Nail Ring | 61 | 0.72 s | 2.1 m | 1 | 0.3 s |
| Sledgehammer | Long Haft, Sledgehammer Head | 110 | 1.55 s | 2.5 m | 2 | 1.0 s |
| Fire Axe | Long Haft, Fire Axe Head | 82 | 1.18 s | 2.6 m | 3 | 0 |
| Chainsaw Sword | Motor Hilt, Chainsaw Blade, Grip Tape | 55 | 0.27 s | 2.3 m | 4 | 0 |
| Frying Pan | Wooden Handle, Frying Pan | 55 | 0.69 s | 1.9 m | 1 | 1.2 s |
| The Codfather | Fishing Rod Grip, Frozen Fish, Whoopee Cushion | 27 | 0.6 s | 2.8 m | 1 | 2.5 s |

(The standard strike: 55 damage, 0.8 s, 2.4 m, one target, a kill from behind.)

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
| **Minigun**: Hellwheel with six standard barrels, Backpack Hopper, 5.56 | Rifle Grip | 1 / 6 (one per shot) | 26 | 2400 (1.2 s spool) | 400 | 9.0 s | 0.81 | 1040 |
| **Lumberjack**: six Gravedigger auto shotguns, shell drums, buckshot | Chainsaw Grip | 6 / 6 | 672 (6 x 8 x 14) | 260 auto | 20 | 5.6 s | 0.40 | 2912 |
| **Shopping Spree**: sixteen Stampede SMG receivers (hip fire only) | Push Cart | 16 / 16 | 320 (16 x 20) | 900 auto | 20 | 4.9 s | 0.40 | 4800 |
| **Party Platter**: Workhorse, Party Tray of four Tridents, twelve stub barrels | Rifle Grip | 1 / 12 | 26 (12 x 2.2: conserved) | 335 auto | 75 | 3.4 s | 0.87 | 145 |
| **Toaster Duck**: Bucket Launcher, Launcher Tube, Toaster Feed, Rubber Duck Rounds | Pistol Grip | 1 / 1 | 90 | 90 semi | 2 | 1.0 s | 0.96 | 135 |

The big multi-receiver numbers (Lumberjack, Shopping Spree) are deliberately silly in this sandbox; in Zombies they
are gated by weight (both sit at the 0.40 mobility floor), part rarity and scrap cost, and by zombie health scaling with
rounds. Balancing them is part of the open questions.

## Checklists

Milestone 1:

- [x] Part data and validation (75 parts, 10 categories, 6 tiers).
- [x] Assembly rules with "why not" messages; deterministic stats into a `WeaponDef`.
- [x] Multi-barrel and multi-receiver firing from each muzzle socket; standard weapons unchanged; Last Stand rule.
- [x] Model from part models at sockets via the scene graph; hand anchors; optic sight line.
- [x] Workbench UI (parts inventory, live 3D preview, receiver slots, stat deltas, errors, 4 build slots, test fire
      in the Workbench Range). Reworked 2026-10-22: grip first, drag and drop, linear list, copy assembly, missing parts.
- [x] Self-test section "Weapon builder" and a smoke step.

Milestone 2 (the user's decisions):

- [x] Fire style fixed per grip; shown in the workbench.
- [x] Splitters conserve damage (all of them); damage multipliers and receiver upgrades (new sockets, tiers, models).
- [x] Shared feeds with capacity, bore/action/ammo rules and a pooled magazine.
- [x] Late-game frames and weight relief; the progression curve and drop tables as proposed data.
- [x] Standard complete builds (`Parts/Builds`), loaded as workbench presets.
- [x] Weapon slots 3-4 behind a per-run flag; station in the range, toggle in the workbench.
- [x] The push cart as a real, replicated, server-authoritative object; parked, it's a manned gun (no auto turret).
- [x] The melee slot: hilts, heads, mods; damage, swing, reach, cleave, stun; workbench section; standard melee unchanged.
- [x] Self-test (fire style, splitter conservation, multipliers, upgrades, shared feed, standard builds, drop tables,
      slots, cart physics and parking, melee builds, standard melee, determinism) and smoke shots (melee on the bench,
      the cart pushed and manned, a melee swing, the minigun). The manned cart: parked it never fires on its own (10 s,
      nobody manning), manned it hits a dummy, the traverse limits clamp the aim and the shots, leaving gives back the
      weapon held before, hold F pushes it again, and the whole run is deterministic.

## Decided (2026-10-21)

1. **Volley or rotate?** Fixed by the grip.
2. **Feeds?** Per-receiver feeds plus shared feeds.
3. **Scaling?** Weight is the main limiter; huge guns come late (rarer frames, weight relief).
4. **The cart and the chainsaw?** The cart is a real pushed object; parked, it's a gun you man and fire yourself (not a
   turret: "a turret would be a little too strong"; one may come later as a separate deployable); the chainsaw grip is
   just a grip, and melee gets its own slot built from melee parts.
6. **Splitter balance?** Splitters always conserve; damage comes from multipliers and receiver upgrades.
7. **Mystery box?** Complete standard builds only.
9. **Slots?** 2 -> 4 via an upgrade bought on the map, per run (a roguelite).

## Open questions (for the user)

1. **More silly grips?** Ideas waiting for a yes: a **Guitar** (receivers in the body, strum to fire), a **Leaf
   Blower**, a **Bicycle Handlebar**, a **Shield** with receivers behind it.
2. **Special ammo effects** (incendiary, explosive, penetrating, plasma, squeaking ducks; and now chainsaw, fish,
   shock, nails on melee) are flagged but have no effect yet: which matter most for Zombies, and do they **stack**
   across receivers (two incendiary receivers = twice the burn, or the same burn)?
3. **The manned cart:** are the traverse limits right (60 degrees either side, 15 down, 30 up)? Should it ever fire
   while pushed (hip fire with heavy spread), or stay "park it, then man it"?
4. **The numbers marked proposed:** the drop tables and prices, the weight penalties, melee stun
   on players (off today).
