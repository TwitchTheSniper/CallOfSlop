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
| 6 | **Mystery box = complete builds only** | `Parts/Builds/*.json`: 140 standard builds by tier (98 box guns, 32 slot melee builds) with where they come from (box, wall, start, slots); the workbench loads them as presets. |
| 7 | Weapon slots **2 -> 4** via an on-map upgrade, per run; the mode is a **roguelite** | Slots 3-4 exist in the game (keys 3 / 4, the swap key and wheel cycle them), unlocked per player per run (a flag the server owns); the range has a station (F) and the workbench a toggle. Drop tables by round are proposed data. |
| 8 | The chainsaw grip is just a grip; add a **melee slot** built from melee-only parts | Hilts, heads and mods (21 parts) build melee weapons with damage, swing time, reach, cleave and stun; the melee key swings them (in the range / Zombies only), with their own workbench section. |


## How it fits the Zombies loop

- **Finding parts.** Zombies drop parts (later rounds roll rarer tiers), salvage spots on the map give a part once a
  round, and the slot machines pay them (there is no scrap since 2026-10-27: "the gambling machines replace the need").
- **Workbenches** on the map are where you assemble, swap and take apart builds.
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
 |    |- action                 <- ACTION: the fire type and rate (required)
 |    |- barrel                 <- a barrel, or a SPLITTER whose outputs take barrels (or another splitter)
 |    |    \- muzzle            <- muzzle device
 |    |- feed                   <- box / drum / belt / tube / cell (or none: fed by the shared feed)
 |    |- ammo                   <- the ROUND TYPE (FMJ, hollow point, buckshot, slug, frag...; any ammo on any receiver), or none: the shared feed's ammo
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
| Grip | Root grips (capacity, fire style, how it is held, carry weight) and foregrips (off hand: vertical, angled, skeleton, thumb stub, recoil pad, pogo, chaingun handle, third arm) | 30 |
| Receiver | The **calibre**: bore class, damage (its bore's, scaled: see "Receivers: a damage spread"), class (recoil, arm poses), sound, barrel size and limits. No fire type: see Action | 25 |
| Action | The **fire type and rate**, one per receiver: single shot, semi, burst, pump, lever, bolt, full auto, rotary, chain, muzzle-loaded (see "Actions: the fire type is a part") | 31 |
| Barrel | Range, damage, spread, and the round's **size and speed** (Bloat Barrel: twice the size at half the speed; Needle Barrel: half the size, 1.7x the speed); bundles have several muzzles (Barrel of Barrels: 7) | 20 |
| Splitter | One shot in, N barrels out at 1/N each (barrel sockets take barrels or another splitter): rows, vertical stacks, grids, a cross, a rosette; plus **offsets** (one output: they only move the barrel) | 23 |
| Feed | Magazine size and reload (box, stick, extended, drum, conga drum, belt, pouch, chute, hopper, tube, shell box and drum, cell, clip, pack, breech, cylinder, toaster) and 7 shared feeds; 3+ per bore | 28 |
| Ammo | The round type on the receiver's calibre: relative damage, range, pellets, fall-off and flags; fits every receiver | 18 |
| Damage Multiplier | x1.1 to x5 damage, one per receiver, with a drawback | 8 |
| Receiver Upgrade | Fire rate or damage up, two per receiver (rear, side) | 10 |
| Stock | Recoil, aim time, mobility, carry (the Shoulder Harness, Exo Spine, Gyro and Jetpack stocks relieve weight) | 7 |
| Optic | The game's optics (red dot, holo, ACOG, 4x, 6x, 8x, scout, night tube, periscope, rangefinder, bead, pocket prism, the All-Seeing Eye) with their reticles and lens | 13 |
| Muzzle | Flash hider, compensator, brake, suppressor, Loudener (2.5x the report: bots and the gunshot mix hear it that much further), shrouded can, duckbill, recoil hook, thread protector, vent collar, blunderbuss flare; Recoil Eater, Dragon Mouth, Velocity Booster, Long Choke, Fork Crown, Tumbler Ring, Hush Baffles | 18 |
| Utility | Laser, bayonet, cup holder, and weight relief (sling, gravity sock, exo sling, anti-grav coil, helium balloons) | 8 |
| Hilt | Melee roots: base damage, swing time, reach, cleave, hands | 8 |
| Head | Melee heads: machete, bat, sledgehammer, fire axe, katana, chainsaw blade, frying pan, frozen fish, wrecking ball, swordfish, grandfather clock | 11 |
| Melee Mod | Nails, grip tape, counterweight, barbed wire, shock coil, tesla wrap, rocket booster, jet engine, whoopee cushion | 9 |

**Bores.** Each receiver fires one calibre class (`pistol`, `rifle`, `heavy`, `shell`, `launcher`, `energy`), and the
registry gives each class its **calibre** (base damage, range, pellets, fall-off, headshot multiplier). Feeds list the
bores they hold. **Barrels, splitters, muzzle devices and ammo fit every receiver**: the receiver adapts the barrel (see
"Barrels and receivers") and the ammo only changes the calibre's numbers relatively (see "Universal ammo").

### Barrels and receivers (2026-10-23)

The user: "remove typing from weapon workbench weapons so that all barrels are compatible with all receivers. The
receiver will determine what type of modifications happen to the barrel ... if a barrel is attached to a pistol
receiver, it'll be smaller than if it's attached to a rifle receiver or an autocannon receiver. But this should remove
friction in matches."

- **Fit.** Every barrel fits every receiver's barrel socket and every splitter output; every muzzle device fits every
  barrel. Only the **feeds** stay typed (a magazine's size is tuned to its rounds; shared feeds keep their bores and
  actions); ammo was typed too until "Universal ammo" below. Belts and hoppers still need an action that pulls them. So
  a refusal in a match is now about what really can't work (a 9 mm magazine in a .50 action), not about a tube.
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

### Splitters, barrels and muzzles (developer request, 2026-10-27)

The developer: "add more splitter models in the workbench, including vertical options. Also include a few offset
splitters that don't provide more attachment points, but move the location of the points." / "add more barrel options.
The barrels don't really do much beyond velocity changes. I would like barrels that can make the bullets bigger and
slower, or a barrel that can make the bullets smaller and faster." / "muzzle devices that have more noticeable effects on
your weapon's performance." All of it is data in `Parts/Splitter`, `Parts/Barrel` and `Parts/Muzzle`; the rarity tags
are a first placing (the late-game pass sets the power curve). Self-test section **Parts mechanics**
(`Source/App/SelfTestPartsMechanics.cs`).

**Splitters.** Twelve new multi-output splitters and five offsets:

| Splitter | Tier | Outputs | Layout | Spread | Splay |
| --- | --- | --- | --- | --- | --- |
| Narrow Twin | common | 2 | side by side, 3.6 cm apart | x0.95 | 0 |
| Wide Y | common | 2 | side by side, 14 cm apart (hip spread x1.05) | x1.0 | 1.5 |
| Diagonal | uncommon | 2 | low left, high right | x0.9 | 1.5 |
| Stepped | uncommon | 3 | a staircase | x0.85 | 1.5 |
| Tower | rare | 3 | **stacked vertically** (vertical recoil x1.08) | x0.8 | 1.5 |
| Pyramid | rare | 3 | one over two | x0.8 | 1.5 |
| Quad Line | rare | 4 | a row (hip spread x1.1) | x0.8 | 2.5 |
| Totem | epic | 4 | **stacked vertically** (vertical recoil x1.15, aim +0.02 s) | x0.75 | 2 |
| Cross | epic | 4 | up, down, left, right | x0.7 | 3 |
| Ladder | legendary | 6 | two columns of three | x0.65 | 1.5 |
| Rosette | wonder | 6 | a ring | x0.55 | 3 |
| Wall | wonder | 9 | a 3 x 3 grid (recoil x1.1, aim +0.04 s, mobility -0.03) | x0.5 | 2 |

- **Splay in two directions.** A splitter whose outputs are all at one height (a row: the old Y, Trident, Peacock, Hydra
  and the new rows) fans sideways from -splay to +splay across its sockets in order, exactly as before. Any other layout
  fans **outward from the outputs' centre**: each output turns by splay x its offset from the centre / the farthest
  output's offset, sideways and up or down (`BarrelLeaf.PitchDeg`, `BarrelPoint.PitchDeg`, which the shot rays already
  used). So a Tower fires a vertical line that opens up and down, a Rosette opens like a flower. Every barrel still fires
  from its own muzzle (`Game.ShotRays`): a vertical stack's rays start one above the other.
- **Offsets** (one `barrel` socket): Riser (up 5 cm, vertical recoil x1.06), Drop (down 4 cm, vertical recoil x0.92,
  aim +0.01 s), Sidestep (5 cm right, horizontal recoil x1.08), Dogleg (up and left 4 cm, spread x0.97) and Canted (right
  and up, the barrel rolled 30 degrees with its socket's `rot`; recoil bias +0.1). They take the whole shot (1/1), have
  no splay (a file with one output and a splay is refused), add no barrel, never count towards the 2-deep stacking limit
  (`BuildRules.SplitterTooDeep`, `BuildNode.SplitterDepth`), and work anywhere a splitter does: on a receiver, on a
  splitter's output, between two stacked splitters. Their barrel keeps aiming down the sights (a canted barrel is a look,
  not a crooked shot). `PartDef.IsOffset` / `BarrelOutputs`; the workbench describes them as "offset: moves the barrel
  socket". A part file's splitter now needs one or more barrel sockets (it used to need two).

**Bullet size and speed: two new stats.** `bulletSize` and `bulletSpeed` are `WeaponDef` fields (1 on every stock gun;
like `loudness` they are part and attachment stats, never weapon-file keys), carried per barrel (`BarrelPoint.Size`,
`ShotRay.Size`). Shots stay hitscan, so:

- **Size** is the hit. A round above 1 hits with a radius of 6 cm per unit above 1 (`Game.BulletRadius`, at most 18 cm):
  for that ray the server grows every target's hit box by it (players, zombies, co-op enemies; `Box.Grown`), and the
  shooter's predicted tracer does the same against zombies and enemies, so a fat slug clips a target a thin bullet would
  miss. Size also draws the tracer wider and the impact mark bigger (`Game.SizeLook`: size^0.75, 0.6x to 2.2x). A round
  smaller than 1 keeps the plain ray. Damage and wall penetration are the parts' own `damage` and `penetrationMult`
  stats, so every barrel says its trade-off in its file. (No bullet drop: the game has none for bullets.)
- **Speed** is the reach and the look. Each barrel path's range (the full-damage distance) is multiplied by its speed
  (`BuildStats.ReceiverStats`, after the barrel and muzzle device), so a faster round carries its damage further and a
  slow one falls off sooner. A round below 0.95 is drawn as a slug with a short streak that **visibly travels** at 320 x
  speed m/s (`Game.TracerSpeed`, `Tracer.Speed`): a Cannon Sleeve's ball crawls out at 128 m/s. The damage still lands at
  once.
- **No wire change.** Built weapons replicate as build text and every client assembles the same weapon, so the size and
  speed are known everywhere; the hit radius is server-side. The protocol stays as it was.

| Barrel | Tier | Size | Speed (= range) | Damage | Other |
| --- | --- | --- | --- | --- | --- |
| Heavy Bore | uncommon | x1.5 | x0.8 | x1.2 | recoil x1.15, penetration x0.8 |
| Bloat | rare | x2.2 | x0.5 | x1.45 | recoil x1.3, hip x1.1, penetration x0.55, aim +0.03 s |
| Cannon Sleeve | legendary | x3.2 | x0.4 | x1.9 | recoil x1.6, FOV punch x1.5, hip x1.2, penetration x0.4, aim +0.06 s, mobility -0.04 |
| Squeeze Bore | uncommon | x0.75 | x1.3 | x0.9 | penetration x1.35, hip x0.9, recoil x0.92 |
| Needle | rare | x0.5 | x1.7 | x0.78 | penetration x1.8, hip x0.75, aimed spread x0.8, recoil x0.75, aim +0.02 s |
| Flechette Sleeve | epic | x0.4 | x2.0 | x0.7 | shotguns x1.5 pellets, penetration x2.2, hip x0.6, recoil x0.85 |
| Sawn-Off | common | | | x1.15 | hip x1.8, aimed spread x1.3, range x0.55, aim -0.04 s, mobility +0.04 |
| Ported | uncommon | | | | recoil x0.75, vertical x0.85, loudness x1.25, range x0.92 |
| Heatsink | rare | | | | fire rate x1.12, shake x1.2, hip x1.05 |
| Marksman | epic | | x1.5 | | hip x0.55, aimed spread x0.65, recoil x0.9, aim +0.05 s, mobility -0.03 |

**Muzzle devices.** The existing ones were pushed so each is felt (the Loudener, Thread Protector, Duckbill and
Blunderbuss Flare are unchanged): Flash Hider hip x0.88 and shake x0.8; Compensator vertical recoil x0.55 (hip x1.05,
loudness x1.1); Muzzle Brake horizontal x0.5, shake x0.45, recoil x0.8, hip spread x0.9 (recoil x0.9 and no hip change until the power curve; loudness x1.25); Recoil Hook vertical x0.6,
recovery x1.4; Suppressor loudness x0.35, recoil x0.9, range x0.88; Shrouded Can loudness x0.3, recoil x0.88; Vent
Collar jitter x0.5, sway x0.7. New:

| Muzzle | Tier | Effect |
| --- | --- | --- |
| Recoil Eater | epic | recoil x0.45, vertical x0.75, shake x0.6; hip spread x1.5, aimed spread x1.4 |
| Dragon Mouth | legendary | 4x the projectiles at x0.55 damage each (2.2x if all land; x0.34 until the power curve, 2026-10-27), hip x3, aimed x2.5, range x0.6, size x1.6, speed x0.5 (so range x0.3 in all), loudness x1.4, **incendiary** |
| Velocity Booster | epic | speed x1.6 (range x1.6), damage x1.08, recoil x1.15, loudness x1.3 |
| Long Choke | rare | hip spread x0.55, aimed x0.8, aim +0.03 s, mobility -0.02 (any gun) |
| Fork Crown | epic | splits every projectile in two at x0.6 (1.2x if both land), hip and aimed spread x1.3 |
| Tumbler Ring | rare | size x1.4, damage x1.12, range x0.8, penetration x0.8 |
| Hush Baffles | legendary | loudness x0.25 (the floor), recoil x0.8, shake x0.8, range x0.9, aim +0.05 s, mobility -0.03, suppressed |

A muzzle device's pellet change works on any bore (as the Blunderbuss Flare's always did). The projectile limit now
counts what each barrel really fires (`BuildStats.PelletsPerBarrel`: the receiver's pellets, then that barrel's and that
muzzle device's), instead of every barrel's pellet change on every barrel.

### Actions: the fire type is a part (2026-10-23)

The user: "change the modular weapons so that fire type is selected by parts, not by the receiver. Receivers should just
be caliber selection, basically. So single, semi, burst, muzzle load is determined from your parts, not the receiver."

- **Receivers are calibre.** A receiver file's `receiver` block holds the class, bore, sound, barrel limits and barrel
  size, and its stats are the calibre's (base damage through its ammo, spread, reload, handling). The old `action`,
  `rpm`, `burstCount`, `burstDelay`, `spoolTime`, `spoolStart` and `rpmPerBarrel` keys are refused there, with a pointer
  to actions. Shotgun receivers keep their pellets (that's calibre). The rotary clusters (Hellwheel, Pepperbox) are
  receivers with six barrel sockets; the autocannon is a heavy receiver with fat barrels; the energy receivers fire
  `energy` rounds. What they fire *like* is the action.
- **Actions (new category, `Parts/Action`).** Every receiver has one required `action` socket (trigger groups and sears
  are drawn under the receiver in front of the grip, the pump and ramrod at its front, bolts, levers, cranks and drives
  on its right side towards the back: see "Where actions sit"). The action part's `action` block is the fire
  type (`kind`: single, semi, burst, pump, lever, bolt, auto, rotary, chain, muzzleLoad), the rate (`rpm`, or
  `rpmPerBarrel` for rotary), `burstCount` and `burstDelay`, `spoolTime` and `spoolStart` (rotary, chain), and
  `chamber` (rounds it holds whatever the feed holds: single shot and muzzle-loaded hold 1, so every shot is a reload).
  Its `stats` change the handling like any part's (applied after the feed, before damage multipliers and upgrades).
  Every action fits every receiver. A receiver without one is a missing part (a yellow "+ Action: needs action" row,
  "Plinker Receiver needs an action."), and the build can't fire.
- **Rules.** Feeds that need a fire type (belts, hoppers) are checked against the receiver's action: "Belt Box needs full
  auto, chain or rotary action; the Workhorse Receiver's Turn Bolt is bolt." and the other way round, "Turn Bolt can't
  work the Belt Box: ...". A receiver with no action yet takes any feed (the action decides). A cluster receiver with a
  rotary action takes turns (one barrel per shot, rate per barrel); with any other action every barrel fires at once
  and, like a splitter, each gets 1/N of the shot. Chain fire across receivers (the grip's checkbox) is unchanged.
- **Stats** (BuildStats.cs): the receiver's base numbers start from its action's rate and trigger (`ModeOf`: burst; auto
  for auto, rotary and chain; one shot per pull for the rest), then the stages run: receiver, ammo, feed, **action**,
  damage multiplier, upgrades, splitters. Spool-up and the rotary flag come from the action. The server checks builds
  with the same rules and takes the text as sent (no stock actions added; protocol 44).
- **Migration.** Each receiver file names the action it fired like before (`"stockAction"`), and gave that action its
  weight (the receiver is lighter by the action's weight, so an old build weighs the same). A build text with **no
  action part anywhere** was written before actions: loading it (workbench.txt, standard build files, hot reload)
  puts each receiver's stock action in, and workbench.txt is saved back. Texts that are always current (undo steps,
  what a client sends the server) are read as written. Every standard build in `Parts/Builds` names its actions.
- **Workbench.** The Action chip and group (hidden like the other receiver categories until a receiver is on the
  build), the action row first under each receiver, the receiver's list line and tooltip show its calibre ("rifle
  calibre"; "comes with the Full-Auto Sear"), an action's show its fire type and rate, and the stats panel a "Fire type:"
  line ("full auto (Full-Auto Sear)", or "semi-auto x2, pump" for several receivers).

| Action | Tier | Fire type | Rate | Effects | Stock action of |
| --- | --- | --- | --- | --- | --- |
| Break Action | common | single shot (1 round) | 120 | damage x1.3, reload x0.45 | |
| Semi-Auto Trigger | common | semi | 400 | | Plinker, Peashooter, Reaper |
| Revolver Hammer | common | semi | 150 | | Kingpin |
| Lob Trigger | common | semi | 60 | | Bucket Launcher, Hand Mortar, Parade Cannon |
| Full-Auto Sear | common | auto | 600 | | Workhorse, Bastille |
| Carbine Sear | common | auto | 720 | | Courier, Sparkplug |
| Blowback SMG Sear | common | auto | 900 | | Stampede |
| Buzzsaw Sear | uncommon (was common) | auto | 1100 | | (the removed Buzzbox) |
| Pump Slide | common | pump | 70 | | Farmhand Pump |
| Turn Bolt | common | bolt | 45 | | Longreach, Railspike, Sunbeam Lance |
| Riot Trigger | uncommon | semi | 200 | | Street Sweeper |
| Marksman Trigger | uncommon | semi | 300 | | Longbow |
| Heavy Auto Sear | uncommon | auto | 260 | | Gravedigger |
| Three-Round Burst Group | uncommon | 3-round burst | 800, 0.25 s | | (the removed Triad) |
| Ratchet Burst Group | uncommon | 3-round burst | 1000, 0.18 s | | (the removed Ratchet Burst SMG) |
| Lever Action | uncommon | lever | 110 | aim -0.02 s | |
| Muzzleloader Ramrod | uncommon | muzzle-loaded (1 round) | 60 | damage x2.5, reload x1.6 + 1.5 s, recoil x1.4 | |
| Autocannon Drive | rare | auto | 200 | | Hammerfall, Bunker Buster |
| Hand-Crank Rotary | rare | rotary | 150 per barrel, 0.3 s spool from 50% | | Pepperbox |
| Hair Trigger | rare | semi | 800 (was 650) | hip spread x1.15 | |
| Straight-Pull Bolt | rare | bolt | 60 | | |
| Binary Trigger | rare | 2-round burst | 1800, 0.05 s (was 900, 0.12 s) | damage x1.05, recoil x1.05 | |
| Electric Rotary Drive | epic | rotary | 400 per barrel, 1.2 s spool from 25% | | Hellwheel |
| Chain Drive Motor | epic | chain | 450, 0.4 s spool from 50% | | Thumper |
| Hyperburst Group | epic | 5-round burst | 2400, 0.1 s (was 1400, 0.35 s) | damage x1.1, recoil x1.1 | |
| Overdrive Sear | legendary | auto | 2400 (was 1400) | damage x1.15, recoil x1.2, hip spread x1.15 | |
| **Thunderclap Hammer** | legendary | semi | 300 | damage x1.9, recoil x1.6, hip spread x1.1 | |
| **Avalanche Burst Group** | legendary | 10-round burst | 2400, 0.2 s | recoil x1.15 | |
| Bicycle Chain Drive | wonder | chain | 360, 0.6 s spool from 40% | | Sawmill |
| Panic Button | wonder | auto | 3000 (was 2400) | recoil x1.3, hip spread x1.6 | |
| **Perpetual Sear** | wonder | auto | 2200 | damage x1.15, reload x0.25, recoil x1.25 | |

**Balance.** Old builds keep their fire mode, magazine, reload and weight exactly; their rate is the receiver's old
one except where one action now serves two receivers: Bastille 650 -> 600 rpm, Plinker 420 -> 400, Reaper 380 -> 400,
Railspike 50 -> 45, Sparkplug 700 -> 720 (each within 10% of its old DPS). Actions drop in runs like any part (the
tier tables); common actions from round 1, and no receiver's stock action is rarer than the receiver. The self-test
section "Workbench actions" (`SelfTestActions.cs`) checks all of this, rebuilding every standard build with its
receivers' old numbers to compare, and fires every fire type through the real trigger loop.

### Universal ammo (user, 2026-10-23)

The user: ammo is universal and selects the **shell type and effect, not the calibre**; the calibre is dictated by the
receiver. Also: more feed options for heavy, and no part category may have fewer than 3 parts.

- **Fit.** Every ammo part fits every receiver's ammo socket and every shared feed (`BuildRules.BoreBound` is the feed
  alone). An ammo file with `"bores"`, or one that `set`s damage, range or pellets, is refused with the reason: ammo is a
  **round type** applied on top of the receiver's calibre, never a calibre.
- **The calibre comes from the bore.** `Parts/_Registry.json` gives each bore a `"calibre"` (`BoreClass.Calibre`):
  damage per projectile, full-damage range, projectiles per round, damage left at long range and the headshot
  multiplier. `BuildStats.BaseDef` starts every receiver from its bore's calibre, then the receiver's own stats (a
  revolver's headshot multiplier, a shotgun's spread), then the ammo's relative changes. Pistol 20 dmg / 15 m, rifle
  26 / 28, heavy 85 / 120 (fall-off to 0.8), shell 14 x 8 pellets / 9 m (fall-off to 0.2), launcher 120 / 40 (headshots
  x1), energy 30 / 40: exactly what the old calibre-named common ammo set, so the standard builds keep their numbers.
- **Round types** (`Parts/Ammo`, 14): the calibre names went. 5.56 FMJ and 9mm Ball are **FMJ** (the baseline: changes
  nothing), 00 Buckshot is **Buckshot**, 40mm Frag is **Frag**, .50 BMG is **Armour Piercing**, Plasma Charge is
  **Plasma**, Rubber Duck Rounds are **Rubber Duck**; Slug, Explosive Tip and Dragon's Breath kept their names and
  became relative. New: **Hollow Point**, **Match Grade**, **Subsonic**, **Shock**, **Sabot**. Old build texts load
  through `WeaponBuild.Renamed` (an old build names the calibre its receiver fires anyway, so it loads with the same
  numbers; a .50 build keeps its penetrating rounds).
- **The round block.** `"round": {"pellets": N}` splits the round into N projectiles **sharing the calibre's damage**
  (the damage per projectile is rounded to a whole number, so a split loses a few percent at low calibres): Buckshot on
  a shotgun is what it fires anyway (8 of 14); on a rifle it's eight pellets of 3 (24 of the 26); on a heavy receiver
  eight of 11. A Slug or Sabot (`pellets: 1`) turns a shotgun's eight pellets into one of 112, and is just the bullet on
  a rifle. A pellet *stat* (Dragon's Breath's +2) counts on shotgun bores only, like a barrel's. Everything else is a
  `mul`/`add`, so a Plasma round on a pistol and on an autocannon both read as "that calibre, plus the plasma effect".
  Flags are unchanged and keep their Zombies effects, which since 2026-10-26 also hurt players wherever such a build meets them, friendly fire respected (ZOMBIES-MODE.md, "Special ammo effects").

| Round type | Tier | Change on the calibre | Flags |
| --- | --- | --- | --- |
| FMJ | common | none | |
| Hollow Point | common | damage x1.15, headshots x1.1, range x0.85, fall-off x0.75 | |
| Buckshot | common | split into 8 pellets | |
| Slug | uncommon | one projectile, range +15 m, fall-off x2 (to 1 at most), hip spread x0.5 | |
| Armour Piercing | uncommon | range x1.15, fall-off x1.25, wall penetration x1.5 | penetrating |
| Frag | uncommon | damage x1.1, headshots x0.5 (a blast doesn't care) | explosive |
| Match Grade | rare | range x1.2, hip spread x0.8, ADS spread x0.8, headshots x1.15 | |
| Plasma | rare | fall-off x1.25, hip spread x0.9 | plasma |
| Subsonic | rare | damage x0.9, range x0.8, recoil x0.85, the gun is quiet | suppressed |
| Explosive Tip | epic | damage x1.4, range x1.07, fire rate x0.9 | explosive |
| Dragon's Breath | epic | damage x0.85, +2 pellets (shotguns), range x1.1 | incendiary |
| Shock | epic | damage x0.9 | shock |
| Sabot | legendary | one projectile, damage x1.25, range x1.5, fall-off x1.5, fire rate x0.85, penetration x2 | penetrating |
| Rubber Duck | wonder | damage x0.75, range x1.25, headshots x3 (6 at most) | bouncy, squeaky |

- **Standard builds.** The 46 that name ammo were rewritten to the new names. Their numbers are unchanged except: the
  heavy builds' range (Armour Piercing's x1.15: Belt-Fed Chaingun 203 -> 233 m), and Grenade Organ (480 -> 528 DPS) and
  Party Cannon (180 -> 198) from Frag's x1.1.
- **Feeds per bore.** Every bore has at least 3 per-receiver feeds, 2 of them common or uncommon: heavy got the Belt
  Pouch (common, 25, needs an automatic action), Heavy Drum (uncommon, 30) and Link Chute (rare, 60, belt); shell the
  Shell Box (uncommon, 10); energy the Battery Clip (common, 24) and Capacitor Pack (uncommon, 32); launchers the
  Three-Round Tube (common, 3) and Revolver Cylinder (uncommon, 6). Every category already had 3 or more parts.
- **Workbench.** Ammo lists as "any calibre" and its tooltip says what the round does on the calibre (and how it's
  split); the Ammo chip shows for any receiver (the hide-categories rule is the same per-part fit check), and a
  receiver swap keeps the ammo like it keeps the barrel.
- **Checks.** The self-test section **Part coverage** (`SelfTestPartCoverage.cs`) pins the rules: 3+ feeds per bore with
  2 cheap ones, no bore-bound ammo and 3+ common/uncommon round types, 3+ parts in every category, and assembles the
  commonest receiver of every bore with each of its feeds and every ammo (each round type within x0.5 to x1.6 of the
  calibre's damage per shot and x0.5 to x3 of its range, and its split as the round block says).

### Where actions sit (2026-10-23)

The user: "Mix where the actions sit on the gun." Only the model moves: build texts, sockets, stats, fire behaviour,
saves and the network are unchanged (the action still plugs into the receiver's one `action` socket).

- **Data.** A socket can carry named `"points"` besides its `"pos"`, and a part can say `"placement"`: the part is
  drawn at that point of its socket, in that point's space (no point of that name, `"side"` or no placement: at `pos`).
  Every receiver's action socket has `pos` on its right side towards the back (as before) and
  `"points": {"under": [0, 0, 0.017], "front": [0, y, z]}`. `BuildModel`, the workbench's socket spots and
  `tools/parts/floaters.py` all place parts this way.
- **Under** (trigger groups and sears, 17 actions): Semi-Auto Trigger, Full-Auto Sear, Carbine Sear, Blowback SMG Sear,
  Buzzsaw Sear, Heavy Auto Sear, Overdrive Sear, Panic Button, Riot Trigger, Marksman Trigger, Hair Trigger, Lob
  Trigger, Revolver Hammer (a double-action trigger: it reads as the trigger mechanism), Three-Round Burst, Ratchet
  Burst, Binary Trigger, Hyperburst Group. The point is on the receiver's bottom centre line, 1.7 cm behind the receiver
  socket: on every grip with a pistol handle (all the standard grips put receiver_1 over the same handle) the group
  sits just in front of the handle, where the grip's own trigger is. Each group is a housing (3.2 cm wide, 2.2 cm deep
  from the receiver's bottom, 2.2 cm long), a trigger blade, and a guard (front post and bar, 4.2 cm below the
  receiver's bottom); what tells them apart is on the right face: a selector (all but the Panic Button and Revolver
  Hammer), a sear block (the sears; a bigger one on the Heavy Auto Sear, a second pin on the Buzzsaw, a glowing strip on
  the Overdrive), one dot per round of the burst (Binary 2, Three-Round and Ratchet 3, Hyperburst 5), a cross-bolt
  safety (Riot), a thin brass blade (Marksman), a yellow blade and wire (Hair), a fat blade (Lob), a brass blade and
  an accent catch (Revolver Hammer), the big red button (Panic Button).
- **Front** (2 actions): the Pump Slide (a grooved wooden forend, 7 cm) and the Muzzleloader Ramrod (a wooden rod with
  brass pipes and tip, 13 cm) reach forward from the receiver's front face, just under the barrel. Each receiver's
  `front` point is on its front face (its barrel socket's z) just under its lowest barrel at the receiver's bore size,
  and under the cooling fins on the Bastille and Hammerfall; at least 1.1 cm above the receiver's bottom so it stays
  clear of the grips' rail and plate (the Hellwheel's sits at 1.5 cm to clear an Angled Foregrip; its and the
  Pepperbox's bottom barrel and the Sawmill's lowest fin plates overlap the forend by a few mm; the Sawmill's point
  sits up at 1.7 cm to clear a Shell Tube under it).
- **Side** (9 actions, as before): Turn Bolt, Straight-Pull Bolt, Lever Action, Break Action, Hand-Crank Rotary,
  Electric Rotary Drive, Chain Drive Motor, Autocannon Drive, Bicycle Chain Drive.
- **Empty socket marker.** An empty action socket has no action to ask, so its workbench marker sits where the
  receiver's stock action would go (`BenchModel.EmptyPos`), else at the "under" point. Checked in **Workbench UI**.
- **Checks.** The self-test section **Action placement** (`SelfTestActionPlacement.cs`) holds the table above (a new
  action must be added to it), checks every receiver has both points and every action is drawn at its point, and that
  no under or front action cuts more than 1 mm into the grip's handle, a foregrip hanging under the gun (Vertical,
  Angled; the Chaingun Handle's posts rise through the barrel line anyway) or any magazine of the receiver's bore, on
  the Rifle Grip and the Service Pistol Grip. A feed that reaches back to within the trigger group's length of the
  handle leaves no room in front of it and may overlap the group: the drum mags on pistol receivers, belt boxes and
  backpack hoppers on some, the Farmhand's shell drum. **Workbench actions** checks every receiver x action pair for
  floating boxes, and `python3 tools/parts/floaters.py` (sections 3 and 3b) does both.

### Graph rules (BuildRules.cs)

Every refusal comes with a plain sentence saying why. These are the actual messages:

| Rule | Example "why not" |
| --- | --- |
| A part only goes in a socket of its mount type | "Red Dot mounts on an optic socket, not a receiver socket." |
| A socket can limit what it takes | "Hellwheel Rotary Receiver's barrel_1 socket takes barrels only, not splitters." |
| A build starts from a grip (or a hilt) | "Rifle Grip is a grip you build on: start a new build with it instead." |
| Grip capacity | "The Pistol Grip holds 3 receivers; it has no receiver_4 slot." |
| Feeds must match the receiver's bore (barrels, splitters, muzzles and ammo fit any) | "Box Mag is made for rifle rounds; the Plinker Receiver fires pistol rounds." |
| Fire types: belts and hoppers need an action that pulls them | "Belt Box needs full auto, chain or rotary action; the Workhorse Receiver's Three-Round Burst Group is 3-round burst." |
| Splitters stack at most 2 deep (offsets, one output, don't count) | "Splitters only stack 2 deep: the shot gets too thin." |
| Barrels per receiver (default 12, the Hellwheel takes 3-6) | "Too many barrels on the Workhorse Receiver: 13; it handles at most 12." |
| Limits per weapon: 96 parts, 32 barrels, 240 projectiles per pull | "Too many projectiles per trigger pull: 256; the most is 240." |
| Weight: a grip carries up to 2x its carry weight (plus weight relief) | "Too heavy: 10.8 kg; the Pistol Grip can't carry more than 10 kg." |
| Shared feed capacity and bores | "Workhorse Receiver needs a feed (the Manifold Hopper can't feed it: it feeds 3 receivers at most)." |
| Melee and gun parts don't mix | "Wooden Handle is a hilt you build a melee weapon on: start a melee build with it instead." |
| A melee weapon weighs 14 kg at most | "Too heavy: 15.2 kg; a melee weapon can't be more than 14 kg." |
| Required sockets (action, barrel, feed, ammo; a hilt's head) | "Plinker Receiver needs a feed." (shown as "Needs:", not an error) |

A build that is incomplete still gets a model for the live preview. Only complete, legal builds get a `WeaponDef`.

## Stat pipeline (BuildStats.cs)

The output is an ordinary `WeaponDef`, the same type every weapon file makes, so the existing firing, recoil, ADS,
sound, HUD and bots all work unchanged. The pipeline is **deterministic**: the same tree always gives the same numbers
and the same muzzle positions, and the self-test checks this by assembling every example twice.

1. **Each receiver on its own.** Start from the receiver's base numbers (its bore's calibre from the registry, the
   class defaults, and its action part's fire type and rate), then apply its parts in stages: receiver, ammo (the
   round block, then its relative stats), feed, action, damage multiplier, receiver upgrades, splitters. A receiver fed by
   the shared feed takes the shared feed as its feed (and its ammo, if it has none). Then each barrel path (barrel plus
   muzzle device) is applied to its own copy, so a long barrel and a stub barrel on one splitter really differ.
   Upgrades change the fire rate as a ratio, so a minigun's rate per barrel scales the same way.
2. **Splitter rule (decided): splitters conserve damage.** Every output gets 1/N of the incoming shot (N = the
   splitter's outputs); nested splitters divide again (a Party Tray of Tridents: 12 barrels at 1/12). The total damage
   of a shot is the same with or without splitters; what changes is coverage (more barrels, splay) and accuracy: each
   splitter's `spreadMult` multiplies its outputs' spread (Y 1.0, Over-Under 0.85, Trident 0.8, Peacock 0.75, Party
   Tray 0.7, Hydra 0.6; the 2026-10-27 splitters fit the same ladder: no splitter is less accurate than one of a
   commoner tier, which the self-test checks; offsets are outside it). Pellets are per barrel, so a split shotgun really fires twice the pellets at half the damage
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
| A trigger pull | Every receiver fires by its own trigger; the ready ones fire on the same tick | Fires the next receiver in turn |
| Fire rate | Each receiver its own (the stat bar shows the fastest; DPS adds them up) | The receivers' rates add up (max 3000 rpm) |
| Magazine | Each receiver its own feed (shown added up); a shared pool counts once, a round per receiver that fires | Feeds add up; a shared pool counts once |
| Recoil | Adds up per shot: root of the sum of squares of the receivers that fired (all 3 = 1.7x one) | The average |
| Trigger | Each receiver's own: auto keeps firing while held, burst fires its burst and waits for the next pull, semi/pump/bolt/lever fire once a pull at their own cycle, rotary/chain spool up on their own | The strictest: semi beats burst beats auto |
| Reload | A receiver that runs dry reloads its own feed (its own time) while the others fire; a full reload (R, or all dry) takes the longest feed plus 0.2-0.35 s per extra receiver (the grip sets it) | The full reload only |

**Independent fire rates (developer decision, 2026-10-27, board item fr01).** A volley used to link its receivers: the
slowest set the rate and the strictest set the trigger. Now each receiver keeps its own action: a 100 rpm cannon beside a
900 rpm SMG fires 2 and 15-16 shots in a second, both on the first tick. This is the volley, and the default;
chain fire stays the staggered alternative. In code: `WeaponDef.FireGroups` (one `ReceiverFire` per receiver, from
`BuildStats.FireGroupsOf`: its rate scaled like the combined rate by the grip's parts, its mode, burst, spool and feed),
fired by `FireGroupState` (`Source/Game/GameFireGroups.cs`) for the local player and bots (fixed arrays, no
allocations). The Fire message carries a 16-bit mask of the receivers that fired (protocol 64) and the server budgets each
receiver at its own rate, dropping one that claims a shot too early. The workbench lists each receiver's rate, trigger,
magazine and DPS; the HUD shows each magazine under the ammo count. "Independent fire rates" self-tests it. Of the
standard builds, only Gantry Sixteen (x1.05: its two receiver types differ in rate) and Kitchen Sink (x4.4: nine mixed
actions, no longer held to its 70 rpm pump) change DPS; their tier bands still hold.

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
| 2 | **Twin Rifle Grip** | rare | 5.5 kg | 0.55 kg | x0.99 | two-handed, side by side; stock, foregrip; hip spread x0.95 |
| 3 | Pistol Grip | rare | 5 kg | 0.4 kg | | one-handed |
| 3 | **Triple Rifle Grip** | rare | 6.5 kg | 0.62 kg | x0.99 | two-handed, three abreast; stock, foregrip; hip spread x0.96, aim time +0.01 s |
| 4 | **Quad Rack Grip** | rare | 7 kg | 0.7 kg | x0.99 | 2 over 2, stock and foregrip |
| 5 | **Penta Rack Grip** | rare | 8 kg | 0.8 kg | x0.98 | 3 below, 2 above |
| 6 | **Hex Rack Grip** (also Mule Frame, Chainsaw Grip) | rare | 9 kg | 0.85 kg | x0.98 | shared-feed socket |
| 7 | **Septet Rack Grip** | epic | 10 kg | 0.9 kg | x0.97 | third tier, shared feed |
| 8 | **Octet Rack Grip** (also Steering Wheel) | epic | 11 kg | 0.95 kg | x0.97 | shared feed |
| 9 | Rifle Grip | epic | 12 kg | 1.0 kg | | shared feed |

The two-handed **Twin** and **Triple Rifle Grips** (2026-10-23) are the rifle-style twins of the one-handed Double-Tap and
Pistol Grips: a pistol handle, a rail for the other hand, a stock socket and a foregrip socket. Like every multi-slot grip
they have the Chain fire checkbox.

The grip picker lists grips by slot count.

### Damage multipliers and receiver upgrades

Every receiver has three new sockets: `amp` (one **damage multiplier**) and `upgrade_rear` / `upgrade_side` (two
**receiver upgrades**). Their effects apply to that receiver only, before its barrels, so they stack across receivers.

| Part | Tier | Socket | Effect |
| --- | --- | --- | --- |
| Hot Load Kit | common | amp | damage x1.1, recoil x1.08 |
| Overpressure Chamber | uncommon | amp | damage x1.2, fire rate x0.95 |
| Tungsten Core Injector | rare | amp | damage x1.35, reload x1.05 |
| Rail Accelerator | epic | amp | damage x1.65 (was x1.5), reload x1.1 |
| Singularity Chamber | legendary | amp | damage x2 (was x1.8), aim time +0.05 s |
| **Tripler Chamber** | legendary | amp | damage x3, reload x1.2, recoil x1.3 |
| Spite Amplifier | wonder | amp | damage x2.5, hip spread x1.6 |
| **Five-Alarm Chamber** | wonder | amp | damage x5, fire rate x0.7, recoil x1.6 |
| Lightweight Bolt | common | upgrade | fire rate x1.08 |
| Match Trigger Group | common | upgrade | damage x1.05 |
| Buffer Spring Kit | uncommon | upgrade | fire rate x1.15, recoil x1.05 |
| Heavy Hammer | uncommon | upgrade | damage x1.12, fire rate x0.95 |
| Gas Overclock | rare | upgrade | fire rate x1.25 |
| Coil Hammer | rare | upgrade | damage x1.2 |
| Twin Sear | epic | upgrade | fire rate x1.5 (was x1.4), damage x1.1, hip spread x1.1 |
| Overclock Core | legendary | upgrade | fire rate x1.7, damage x1.2 (were x1.6, x1.1) |
| **Doom Hammer** | legendary | upgrade | damage x1.75, recoil x1.15 |
| **Overkill Coprocessor** | wonder | upgrade | fire rate x1.8, damage x1.8, hip spread x1.1 |

### Shared feeds (hoppers and manifolds)

A shared feed sits on a grip's `hopper` socket (rifle grips, the chainsaw grip, the steering wheel, the briefcase,
the new frames and the cart; not the pistol grips). Per-receiver feeds are still allowed. The rules:

- It feeds receivers that have **no feed of their own**, in receiver-socket order, up to its **capacity** (`"shared":
  {"feeds": N}`); the rest need their own feed ("...the Manifold Hopper can't feed it: it feeds 3 receivers at most").
- A receiver is fed only if its bore is one the shared feed takes, its action is one the feed needs (if any), and the
  shared feed's ammo fits it.
- Ammo in the shared feed's `ammo` socket is **shared**: fed receivers need no ammo of their own.
- The magazine is **one pool** (its `mag` stat): in a volley, every fed receiver that fires takes a round from it (each at
  its own rate; all firing together it lasts pool / k volleys), and it counts once next to the receivers' own magazines;
  rotating, each shot takes one round. The full reload is the longest feed's, as before; a dry pool reloads on its own
  (the longest fed receiver's time) while receivers with their own magazines keep firing.

| Shared feed | Tier | Feeds | Bores | Pool | Reload |
| --- | --- | --- | --- | --- | --- |
| Manifold Hopper | rare | 3 | pistol, rifle | 120 | 4.0 s |
| Shell Hopper | epic | 4 | shell | 48 | 5.0 s |
| Ammo Can Manifold | epic | 6 | rifle, heavy | 300 | 6.0 s |
| Bottomless Bucket | legendary | 9 | pistol, rifle, heavy | 900 | 8.0 s |
| Power Bus | legendary | 8 | energy | 400 | 5.0 s |
| **Shell Silo** | legendary | 6 | shell | 160 | 5.0 s |
| **Clown Car Hopper** | wonder | 16 | pistol, rifle, heavy, shell, energy | 999 | 3.0 s |

### Firing (GameBuilder.cs)

- Every barrel fires from **its own muzzle socket**. The ray starts at the eye moved sideways and up by that muzzle's
  offset from the barrels' centre (capped at 0.45 m so rays don't start inside a wall next to you), turned by the
  splitter splay (sideways and, off a row, up and down), with its own pellets, damage, spread and round size (a round
  above size 1 grows the target's hit box: see "Splitters, barrels and muzzles").
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
  (in a run). A **Show all grips** checkbox (`wb_show_all_grips` in `settings.txt`, off by default; "Show all hilts" on
  the melee slot) decides what's listed: off, only the grips you own (a "(N hidden)" hint); on, every grip in the game,
  the ones you don't own dimmed with their rarity, slots and stats still readable (2026-10-26 board item). In the
  sandbox an unowned grip can still be picked to plan a build (it shows as missing); at a Zombies run's bench it is
  locked ("LOCKED - none owned") and a click only says why (`BenchGrips`, self-test "Workbench grip list"). The sandbox's
  every-part stock owns every grip, so it lists them all either way. "Change grip" (G) moves the current build onto
  another grip (receivers into the same-numbered slots).
- **Step 2: assembly.** Left: your parts, grouped by category with counts (a run: loose parts plus the slot's own), a
  search box and category chips, 3D icons (rendered once into textures and cached, `PartThumbs`), a tooltip with what
  the part does. A **Show incompatible parts** checkbox (saved in `settings.txt` as `wb_show_incompatible`, off by
  default) decides what's listed: off, only parts that fit somewhere on the build and have a spare copy (a "(N hidden)"
  hint); on, every part you own, the rest dimmed with a short reason in the tooltip ("needs a shell bore feed socket",
  or "none spare" when every copy is already on the build) and bouncing back if dropped. The chip counts follow the toggle and the search (`BenchInventory`).
  With the toggle off, **whole categories hide** (no chip, no group header) when nothing you own in them could go on
  the build right now: the same per-part check as the list (a free *or occupied* socket whose rules take it, since a
  swap counts, plus a spare copy). An empty grip shows no Feed, Ammo, Barrel or Splitter until a receiver is fitted
  (barrels, splitters, muzzles and ammo fit every receiver but still hang off one; feeds must match its bore), and a
  grip without a stock socket (Pistol Grip) shows no Stock. They come back the moment the build changes. A chosen chip
  whose category hides falls back to All, and clicking an empty socket row whose category is hidden says so instead of
  opening an empty list. With the toggle on every category you own a part of is shown; categories you own none of
  (a run's stock) stay hidden either way. Visibility is worked out once per build/stock/toggle change, each part's fit
  checked once (self-test section "Workbench hidden categories"). Middle: the gun large
  (drag to orbit, wheel to zoom, double-click or R to reset) with socket markers: yellow rings on empty sockets the
  build needs, red on missing parts, orange on the selection. **Pressing Build on an incomplete weapon** (a click on the
  greyed-out Save / Save melee / Test fire / Apply button; 2026-10-26 board item) circles every empty socket the build
  needs in red (`BenchModel.RefuseBuild`: the `Needed` spots, i.e. what `BuildRules.NeedsFilling` requires in each
  receiver subtree - barrel, feed, ammo, action, a splitter's outputs - and one receiver slot on an empty grip; optional
  sockets are never marked). The rings sit at the socket positions the yellow ones use (an empty action socket at its
  stock action's spot), are drawn over the 3D view so they show from any angle and through the model, pulse hard for
  1.6 s and then beat gently; hovering one says what goes there, clicking it shows that category in the parts list. Each
  ring goes once its socket is filled (for good: undo brings the socket back yellow), and all of them when another
  build is loaded. The message line names the sockets; the button's tooltip keeps the full reason (self-test
  "Workbench refused build"). Right: the build as a **linear list** (Grip > Receiver(s) >
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
- **Tree click (2026-10-23).** A single click on a part row in the build tree (e.g. "Feed: Box Mag") sets the parts list's category chip to that part's category and scrolls the list to its top, when the list shows that category; clicking an empty socket row did this already. Double-click and right-click on the row are unchanged.
- **Fast.** Double-click a part: it goes to its best free socket (needed ones first). **Auto-complete** (the A key; in the sandbox also a button in the top row) fills every
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
  badge, and the Save / Apply button's tooltip lists what's missing and what's still needed (CRAFT MISSING & APPLY went
  with scrap, 2026-10-27: missing parts come from drops, salvage piles and the slot machines). The sandbox's **Practice parts** toggle (two of every
  common part, one of every uncommon) lets you try all of this without a run.
- **Disassemble (a run's workbench, 2026-10-23).** The user asked: "allow for the user to completely disassemble a gun
  into inventory items from the workbench". **Disassemble slot N** (D), the button that replaced Auto-complete in the top row (New weapon, Change grip, Undo, Redo, Disassemble) takes the selected slot's whole build
  apart, the grip or hilt included: every part goes to your loose parts and the slot is left empty (the editor goes to
  the grip picker, ready for a new build from your parts). It's destructive-ish, so it needs **two clicks** (or D twice)
  within 4 s: the first arms it (the button turns red and the strip says how many parts will come off). One **Undo**
  (the footer's "Undo disassemble" button, or Ctrl+Z once the new build has nothing left to undo) puts the same build
  back from your loose parts while the bench is open (it's an ordinary Apply: the server checks the parts again). The
  footer also shows the slot buttons while the grip picker is up, so an empty slot isn't a dead end. The rules, the network and the empty slot are in
  ZOMBIES-MODE.md, "Disassembling a build". **The main menu's sandbox has no Disassemble**: its stock is every part
  without limit (or the fixed practice stock), there's no inventory for parts to go back to, so taking a build apart
  would change nothing; "New weapon" (N) already starts from an empty grip there.
- **Disassemble at the mystery box (board items, 2026-10-26).** While the box offers you a build, **G twice** (the
  grenade key; Zombies has no grenades) takes the **offered** build apart instead of taking it: its parts go to your
  loose parts and the box closes; the gun in your hands is never touched (a bug fix: the first version took the held
  gun apart). `ZBench` kind 5, protocol 54 (ZOMBIES-MODE.md, "Disassembling a build").
- **Server authority is unchanged**: Apply sends the build's text in `ZBench` and the server checks the parts again.
  No new messages for these (Disassemble is a new `ZBench` kind, protocol 41). The workbench is a menu for the cursor
  rule (`CursorPolicy`): the mouse stays free.
- **Cost.** The editing model (`BenchModel`) does its allocating work only when the build changes (assembling, the
  list, the sockets, what's missing); the per-frame calls (camera, projection, the ray pick, the stock fingerprint)
  allocate nothing (self-test). Part pictures render 8 per frame at most, once per part per session.

## Rarity tiers

| Tier | Colour | Where it comes from (proposed) |
| --- | --- | --- |
| Common | white | wall weapons, early drops |
| Uncommon | green | drops from round 1 (15%) |
| Rare | blue | drops from round 4, salvage, slot machines from round 1 (2%) |
| Epic | purple | drops from round 10, slot machines from round 5, the box from round 1 (5%) |
| Legendary | orange | drops from round 18, slot machines from round 20, the box from round 15 |
| Wonder | red | drops from round 25, slot machines in jackpot rounds from round 25, the box from round 22: fun first, balance second |

The rounds are the late-game pass's (2026-10-27); see "The power curve" below.

**No scrap (developer request, 2026-10-27).** Parts used to have a scrap value (break one down at the workbench, craft
one for 3x its value). Zombies has no scrap now: the `Scrap` hooks, part values and the bench's break-down and craft
actions are gone; parts come from drops, salvage piles, the slot machines and taking builds apart (ZOMBIES-MODE.md,
"Parts and the workbench"). A part file's old `"scrap"` key is still accepted and ignored.

## The power curve (late-game pass, 2026-10-27)

The developer: "add more weapon parts and rescale the parts. I want much more powerful items to be available later
game. It'll take longer to get to them, but currently I don't think there's enough late game, ultra powerful, funny
options." / "just add some more very strong options for later game progression." / "add more damage variants to the
receivers. There is only a few different damage amounts, so all the different receivers end up feeling very similar.
also remove receivers that were attributed to fire rates, such as a burst receiver." "Rescale" means progression, damage
and performance, not the models' size.

**How a part is measured** (`Source/Zombies/PowerCurve.cs`, only the self-test and these docs use it). Gun parts are
fitted to a reference gun (a Workhorse on the Rifle Grip with a standard barrel, box mag, FMJ and the Full-Auto Sear) and
scored by how much they change its **gun power**: sustained body damage per second over a magazine and its reload,
headshots a quarter of the time, accuracy (hip and aimed spread, the recoil and its pattern, the round's size), reach (range x zoom) and handling (mobility,
aim time), times what its flags are worth in Zombies (explosive x1.3, launch x1.4, marks x1.3, incendiary x1.2...).
Feeds are scored on their bore's commonest receiver against a common feed (a shared feed x the root of the receivers it
feeds); barrels, splitters (standard barrels on every output, +5% an extra output) and muzzle devices on the reference
rifle too; receivers by their damage per trigger pull on their stock action; root grips by receiver slots x the root of the
carry weight; hilts, heads and melee mods by melee power (damage per second, cleave, stun, reach, flags).

**The rules** (self-test "Power curve", `SelfTestPowerCurve.cs`): in every row the best part of a tier is never weaker
than the best part of the tier below; in the damage rows (receiver, action, ammo, multiplier, upgrade, hilt, head, melee
mod) the best part of a tier is at least this many times the best common part:

| Tier | Common | Uncommon | Rare | Epic | Legendary | Wonder |
| --- | --- | --- | --- | --- | --- | --- |
| Floor (best of the tier / best common) | x1 | x1 | x1.1 | x1.3 | x1.6 | x2.2 |
| Arrives: zombie drops | round 1 | round 1 | round 4 | round 10 | round 18 | round 25 |
| Arrives: slot machines (at full heat and bet too) | round 1 | round 1 | round 1 | round 5 | round 20 | round 25 (jackpot rounds) |
| Arrives: mystery box (complete builds) | - | round 1 | round 1 | round 1 | round 15 | round 22 |

The curve as it stands (the best part of each tier and its score; CI's log lists every part):

| Row (score) | Common | Uncommon | Rare | Epic | Legendary | Wonder |
| --- | --- | --- | --- | --- | --- | --- |
| Receiver (damage a pull) | Farmhand Pump 112 | Longreach Bolt 119 | Bucket Launcher 126 | Railspike Coil 150 | Bunker Buster 255 | Parade Cannon 491 (360, explosive) |
| Action (power x) | Blowback SMG Sear 1.24 | Buzzsaw Sear 1.35 | Binary Trigger 1.37 | Hyperburst Group 1.63 | Overdrive Sear 2.0 | Perpetual Sear 4.4 |
| Ammo | Hollow Point 1.14 | Frag 1.27 | Plasma 1.34 | Explosive Tip 1.72 | Depleted Core 2.08 | Bowling Ball 3.27 |
| Damage Multiplier | Hot Load Kit 1.11 | Overpressure Chamber 1.15 | Tungsten Core Injector 1.31 | Rail Accelerator 1.57 | Tripler Chamber 2.71 | Five-Alarm Chamber 3.91 |
| Receiver Upgrade | Lightweight Bolt 1.04 | Heavy Hammer 1.08 | Coil Hammer 1.19 | Twin Sear 1.36 | Doom Hammer 1.75 | Overkill Coprocessor 2.4 |
| Barrel | Standard Barrel 1.0 | Heavy Bore 1.21 | Bloat 1.4 | Coil 1.55 | Cannon Sleeve 1.82 | Barrel of Barrels 2.31 |
| Splitter (+5% an extra output) | Narrow Twin 1.05 | Stepped 1.1 | Quad Line 1.14 | Peacock 1.19 | Ladder 1.24 | Wall 1.35 |
| Muzzle | Flash Hider 1.01 | Muzzle Brake 1.01 | Blunderbuss Flare 1.15 | Duckbill Spreader 1.48 | Dragon Mouth 1.57 | - |
| Feed | Belt Pouch 1.55 | Revolver Cylinder 1.75 | Manifold Hopper 2.29 | Ammo Can Manifold 3.87 | Bottomless Bucket 4.93 | Clown Car Hopper 7.15 |
| Grip (slots x root of carry) | Service Rifle Grip 2.8 | Sport Rifle Grip 3.2 | Mule Frame 24 | Exo Harness 66 | Gantry Rig 111 | Parade Float 124 |
| Foregrip | Skeleton 1.0 | Angled 1.01 | Pogo 1.01 | - | Third Arm 1.04 | - |
| Stock | Wire Stock 1.0 | - | Shoulder Harness 1.08 | Exo Spine Stock 1.15 | Gyro Stock 1.19 | Jetpack Stock 1.33 |
| Optic | Red Dot 1.05 | Pocket Prism 1.08 | 2.5x ACOG 1.17 | Lighthouse 6x 1.42 | 8x Sniper Scope 1.47 | All-Seeing Eye 1.62 |
| Utility | Bayonet 1.04 | Laser Sight 1.05 | Gravity Sock 1.08 | Exo Sling 1.17 | Anti-Grav Coil 1.24 | Helium Balloons 1.36 |
| Hilt (melee power) | Bat Grip 116 | Long Haft 130 | Sword Hilt 184 | Motor Hilt 229 | Lamp Post 252 | Turbine Hilt 522 |
| Head | Machete Blade 114 | Sledgehammer Head 127 | Katana Blade 163 | Chainsaw Blade 264 | Wrecking Ball 469 | Swordfish 1014 |
| Melee Mod (power x) | Nail Ring 1.36 | Barbed Wire Wrap 1.43 | Shock Coil 1.55 | Tesla Wrap 2.11 | Rocket Booster 2.4 | Jet Engine 4.48 |

Commons are as before; the top tiers stack (a receiver x multiplier x two upgrades x ammo), so a late Bunker Buster with a
Tripler Chamber, two Doom Hammers and Depleted Core would do 255 x 3 x 1.75 x 1.75 x 1.7 = about 4000 a round, and
used to reach the 1000-a-projectile cap; builds have no cap now (below, "No damage cap for builds"). Zombie health is about 2100 at round 30 and 3800 at round 40.

**Rescaled existing parts.** Receivers: below. Actions: Buzzsaw Sear common -> uncommon (it was the Buzzbox's stock
action), Hair Trigger 650 -> 800 rpm, Binary Trigger 900 rpm / 0.12 s -> 1800 / 0.05 s and damage x1.05, Hyperburst
Group 1400 / 0.35 s -> 2400 / 0.1 s and damage x1.1, Overdrive Sear 1400 -> 2400 rpm and damage x1.15, Panic Button
2400 -> 3000 rpm. Ammo: Plasma damage x1.15. Damage multipliers: Rail Accelerator x1.5 -> x1.65, Singularity Chamber
x1.8 -> x2. Upgrades: Twin Sear rate x1.4 -> x1.5 and damage x1.1, Overclock Core rate x1.6 -> x1.7 and damage x1.1 ->
x1.2. Gyro Stock: +8 kg carry, mobility +0.04, recoil x0.55 -> x0.5. Laser Sight: hip spread x0.8 -> x0.5, aimed spread
x0.85. Barrels, splitters and muzzle devices (the splitters, barrels and muzzles pass, merged the same day) are on the
curve too: Coil Barrel damage x1.15 -> x1.3; Duckbill Spreader rare -> epic (on a single-projectile gun it doubles the
round at x0.8 each); Dragon Mouth x0.34 -> x0.55 a projectile; Muzzle Brake recoil x0.9 -> x0.8 and hip spread x0.9.
The rest kept their tiers and numbers.

### Receivers: a damage spread (2026-10-27)

Receivers are the calibre (the action is the fire type), and until now their damage was only their bore's: six numbers
for 23 receivers. Each receiver now scales its bore's damage (a `damage` `mul` in its stats, applied before the ammo), and
its handling differs too:

| Receiver | Tier | Bore | Damage (x the bore) | Handling |
| --- | --- | --- | --- | --- |
| **Peashooter** (new) | common | pistol | 13 (x0.65) | almost no kick (recoil x0.6), mobility +0.07, quickest to aim |
| Stampede | common | pistol | 18 (x0.9) | soft-shooting (recoil x0.85) |
| Plinker | common | pistol | 20 | |
| Courier Carbine | common | rifle | 23 (x0.9) | light, quick |
| Workhorse | common | rifle | 26 | |
| Farmhand Pump | common | shell | 14 x 8 | |
| Reaper Battle Rifle | uncommon | rifle | 35 (x1.35) | recoil x1.25, aim +0.02 s |
| Kingpin Revolver | uncommon | heavy | 64 (x0.75) | a hand-cannon, recoil x1.6 |
| Longreach Bolt | uncommon | heavy | 119 (x1.4) | |
| Bastille LMG | rare | rifle | 30 (x1.15) | |
| Longbow DMR | rare | rifle | 43 (x1.65) | recoil x1.15 |
| Gravedigger Auto | rare | shell | 11 x 8 (x0.8) | recoil x0.8 |
| Street Sweeper | rare | shell | 15 x 8 (x1.1) | |
| Bucket Launcher | rare | launcher | 126 (x1.05) | |
| Hellwheel Rotary | epic | rifle | 31 (x1.2) | |
| Thumper Chain Gun | epic | heavy | 76 (x0.9) | |
| Railspike Coil | epic | energy | 150 (x5) | recoil x1.6 |
| **Hand Mortar** (new) | epic | launcher | 144 (x1.2) | recoil x1.3, full damage at any range |
| Sparkplug Energy | legendary | energy | 39 (x1.3) | |
| Hammerfall Autocannon | legendary | heavy | 187 (x2.2) | |
| **Bunker Buster** (new) | legendary | heavy | 255 (x3) | autocannon drive, recoil x1.9, fat barrels |
| Pepperbox | wonder | pistol | 32 (x1.6) | |
| Sawmill | wonder | shell | 18 x 8 (x1.3) | |
| **Sunbeam Lance** (new) | wonder | energy | 240 (x8) | plasma, penetrating, perfect aimed accuracy |
| **Parade Cannon** (new) | wonder | launcher | 360 (x3) | small cannon shells: explosive, party; barrels x3.3 |

**Removed: the receivers that were only a fire rate.** The Ratchet Burst SMG Receiver (a burst pistol action), the Triad
Burst Receiver (a burst rifle action) and the Buzzbox Machine Pistol Receiver (a pistol action named for its 1100 rpm)
went: since actions are parts, their fire mode and rate are the Ratchet Burst Group, the Three-Round Burst Group and the
Buzzsaw Sear on any receiver. `WeaponBuild.RemovedReceivers` maps each to a replacement of its bore and the action it
fired with: Ratchet Burst -> Stampede + Ratchet Burst Group, Triad Burst -> Workhorse + Three-Round Burst Group, Buzzbox
-> Plinker + Buzzsaw Sear. A build text naming one loads on the replacement; one written before actions were parts gets
the old action (so it fires as it did); `workbench.txt` is saved back. The standard builds that used them (Burst
Marksman, Four-by-Three, Ratchet PDW, Buzzbox Machine Pistol, Buzzbox Akimbo) name the replacements, so they are the
same guns (damage and rates as before, apart from the Stampede's x0.9). The Thumper and Sawmill (chain guns) and the
Hellwheel and Pepperbox (rotary clusters) stay: their models and barrel sockets are what they are, not a rate.

### The late-game parts (35, 2026-10-27)

| Part | Category | Tier | What it does |
| --- | --- | --- | --- |
| Peashooter Receiver | Receiver | common | the light end of the receiver spread: 13 damage, nearly no recoil, fast |
| Hand Mortar Receiver | Receiver | epic | a held trench mortar: 144 a bomb, no fall-off |
| Bunker Buster Receiver | Receiver | legendary | a .50 bored out for tank shells: 255 a round on the autocannon drive |
| Parade Cannon Receiver | Receiver | wonder | fires small cannon shells: 360, explosive and party, huge barrels |
| Sunbeam Lance Receiver | Receiver | wonder | a slice of the sun: 240, plasma, penetrating, a bolt action |
| Thunderclap Hammer | Action | legendary | semi, 300 rpm, damage x1.9 |
| Avalanche Burst Group | Action | legendary | ten-round bursts at 2400 rpm |
| Perpetual Sear | Action | wonder | auto 2200 rpm, damage x1.15, reload x0.25 ("reloads itself out of spite") |
| Depleted Core | Ammo | legendary | damage x1.7, range x1.3, penetrating |
| Thermobaric | Ammo | legendary | damage x1.45, explosive and incendiary |
| Bowling Ball | Ammo | wonder | one ball a shot, damage x2.6, short range, bouncy, launches zombies |
| Pocket Sun | Ammo | wonder | damage x2.2, plasma and incendiary |
| Conga Drum | Feed | legendary | two drums back to back: 150 rounds (pistol, rifle), 3 s reload |
| Shell Silo | Feed | legendary | shared: six shotguns, 160 shells |
| Clown Car Hopper | Feed | wonder | shared: 999 rounds of any round but grenades, for sixteen receivers |
| Titan Harness | Grip | legendary | the Exo Harness with legs: 12 receivers, 45 kg carry |
| Parade Float | Grip | wonder | a carnival gantry: 16 receivers, 60 kg carry, pennants |
| Third Arm | Grip (foregrip) | legendary | a robot arm: recoil x0.55, hip spread x0.7 |
| Lighthouse 6x | Optic | epic | 6x variable scope |
| All-Seeing Eye | Optic | wonder | 3x; marks every zombie on the map through walls while held ("marks" flag) |
| Exo Spine Stock | Stock | epic | +8 kg carry, recoil x0.75 |
| Jetpack Stock | Stock | wonder | +12 kg carry, mobility +0.15, recoil x0.6, quicker draw |
| Doom Hammer | Upgrade | legendary | damage x1.75 |
| Overkill Coprocessor | Upgrade | wonder | fire rate x1.8 and damage x1.8 |
| Tripler Chamber | Damage Multiplier | legendary | damage x3, reload x1.2 |
| Five-Alarm Chamber | Damage Multiplier | wonder | damage x5, fire rate x0.7, recoil x1.6 |
| Gravity Sock | Utility | rare | +4 kg carry |
| Exo Sling | Utility | epic | +8 kg carry, hip spread x0.9 |
| Lamp Post | Hilt | legendary | 70 damage, reach 3 m, cleave 3 |
| Turbine Hilt | Hilt | wonder | 45 damage at 0.3 s, cleave 4 |
| Wrecking Ball | Head | legendary | +110 damage, stun 1.2 s, launches zombies |
| Swordfish | Head | wonder | +85, faster, +0.7 m reach, cleave +3, fish and launch |
| Grandfather Clock | Head | wonder | +160, stun 2.2 s, cleave +3, bonk and launch |
| Jet Engine | Melee Mod | wonder | +30, swing x0.6, rocket and launch |
| Tesla Wrap | Melee Mod | epic | +12, stun 0.9 s, shock |

Every one is a box model in one piece (self-test "Late-game parts" builds each on a complete gun or melee weapon and
checks it connects; most receivers and the two big grips `extends` an existing part, so their sockets and action points
are the tested ones). Their slot machines are the category machines that already exist.

**New effects.** `launch` (Zombies, `ZombiesAmmoEffects.cs`): the zombie hit is thrown away at `launchSpeed` (11 m/s)
and up at `launchUp` (8 m/s) and stunned `launchStun` (1.2 s) (ZombiesRules.json `ammoEffects`; it replaces a rocket or
fish knockback on the same hit). `marks` (the All-Seeing Eye): while the gun is in hand the HUD draws a mark over every
zombie, through walls (`Game.MarksZombies`, `TacticalHud.cs`; client side, no network). A **slot row's `"top"`**
(DropTables.json `slotTiers`): the rarest tier the row pays; heat, bets and the jackpot bonus that would go past it pay
that tier instead, so heat and a 5x bet can't pull a legendary before round 20.

**Balance.** No PvP mode uses builds: they exist in Zombies and the Workbench Range only, where they meet players only
through friendly fire (ZOMBIES-MODE.md); the Frenzy buffs are untouched. The mystery box bands per trigger pull and DPS
were raised for the late tiers (epic 1500 / 3000, legendary 3000 / 8000, wonder 5000 / 12000); the Rail Battery is 1188
a pull now (six 150-damage Railspikes) and the Minigun 1240 DPS (Hellwheel x1.2). A jetpack-hop stock was considered and
left out: a jump boost needs the movement state on the wire (it would be a protocol change); the Jetpack Stock speeds
you up instead.

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
  it. Iron-sight boxes (`"tag": "irons"`, or `"irons:SOCKET"` which also needs that socket filled, as on the rack grips; only grips have them) stand on the receiver: they are drawn only when a
  receiver is fitted and no optic is. Grips with `"ads": false` (cart, briefcase) are hip fire only and drop the optic's
  zoom.
- **Optic mount height (2026-10-23).** The user: "large receivers need to have the optic mount location adjusted
  accordingly". The grip's `optic` socket height suits a small receiver; `BuildModel.MountSeat` raises the mount to the
  top of the highest drawn box under the optic's rail strip (2.5 cm each side, 6 cm along), so a big receiver (LMG,
  autocannon, launcher) or its top rail never pokes into the optic. It only raises: iron sights and see-through boxes
  don't count. The mount height is what the rail, the optic, the sight line and the aim point are built from, so ADS
  moves with it. Self-test "Optic mounts" checks every grip x receiver x optic that nothing rises into the optic.
- **Conditional boxes:** `"tag": "with:SOCKET"` draws a box only while that socket of the same part holds something (the
  hopper chute under the rifle and rack grips, the big frames' stock mounts). A part file naming a socket it doesn't
  have is refused.
- **Nothing floats (2026-10-23).** The user: "remove the weird frame pieces that float above the grips when it's not
  intentional and just there to support the optics". Every part's boxes form one connected piece (within 1 cm), and
  every part of an assembled weapon touches the rest. The pass fixed: the grips' iron sights (hovering over an empty
  plate; now only with a receiver, front post moved back over the receiver and both reaching down to it); the rack
  rack grips' frame (Quad to Rifle Grip: posts and a crossbar round the receiver slots; **removed entirely**, user 2026-10-23:
  "remove entirely"; each upper row of receivers now has its own thin plate and spine, drawn only while that slot holds a
  receiver, and the iron sights hang on a short sight rail over the top row); the Gantry Rig's optic mount (a box and post hanging between the
  crossbars; now on a spine between them); the Chainsaw Grip's receiver plate (3 cm off the body); the Steering Wheel's
  rim (gaps between segments; diagonal spokes now carry the diagonal receivers); the Exo Harness, Gantry Rig and Mule
  Frame plates (1.7 cm under the receivers; shelf bars under each upper row); hopper chutes and stock mounts on the
  frames whose hopper or stock socket hangs off them; the push cart's basket floor; small fixes to the Longbow screw,
  the Street Sweeper drum, the Shoulder Harness, the Frozen Fish (on a short line now) and the Whoopee Cushion.
  Sockets, stats and sight heights are unchanged. `python3 tools/parts/floaters.py` lists floaters; the self-test
  section **Model connectivity** fails on any, with an allow-list for intentional separate pieces (the cooling fins of
  the Bastille, Sawmill and Hammerfall receivers, which the barrel runs through; the Barbed Wire Wrap and Nail Ring on
  the hilt; the push cart's upper rows of receivers, piled in the basket).

- **Contoured shapes (2026-10-27, board item dev-part-models-pm01).** Besides box, rod and plate, a model box may be
  a `cylinder`, `tube`, `wedge`, `bevel`, `rounded`, `capsule`, `sphere` or `arc` with a few settings (segments, inner
  size, capped ends, taper and shift of the front end, bevel or rounding radius and which edges, an arc's sweep and
  direction); Parts/README.md, "Shapes", lists them. Each shape is a small mesh made once per (shape, settings, size)
  when the parts load (`Source/Zombies/PartShapes.cs`) and drawn **inside its box**: the box's matrix and size are
  unchanged, so bounds, the optic seat, the workbench's framing and picking, the cart's collision and every test that
  measures a gun by its boxes keep working. The shape id rides on `PartBox.ShapeId`, `GunPart.Shape`,
  `Renderable.Shape` and `DrawItem.Shape`; `Renderer.Draw` uploads new shapes into its box vertex buffer once and draws a
  shaped piece with one draw call, like a box (not batched with the horde's instanced boxes). Normals are stored divided
  by the size so the box's own scale turns them right. Budgets: 6000 triangles per part (`PartFile.MaxTriangles`), 30000
  per standard build (self-test "Part shapes"; the heaviest, the Gantry Sixteen, draws about 17000). Connectivity counts
  a shape only where it really is, through a few proxy boxes inside it (`PartShapes.Proxies`, mirrored in
  `tools/parts/shapes.py` for `floaters.py` and `viz.py`). Every part category with part models was remodelled with
  them (optics are drawn by `GunModel` and ammo has no model), keeping sockets, hand points, sizes and silhouettes; the
  old `rod` drew its 45-degree copy about Z whatever its long axis was, so the push cart's bar (long in X) drew as a
  cross; it is a cylinder now.

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

A model box may also take a contoured `"shape"` with its settings, e.g. `{"shape": "tube", "size": [0.022, 0.022, 0.2],
"pos": [0, 0, -0.1], "inner": 0.45, "ends": "back"}` (Parts/README.md, "Shapes").

Other blocks: `receiver` (class, bore, soundOf, minBarrels, maxBarrels, barrelScale, stockAction), `action` for
action parts (kind, rpm, burstCount, burstDelay, spoolTime, spoolStart, rpmPerBarrel, chamber), `frame` for root grips (fireStyle, hands, carry, mobility, ads, ironHeight,
support, reloadPerReceiver; an older `fireStyles` list reads as its first style), `split` (splay, spreadMult; the old
`outputMult` is refused: splitters always conserve), `shared` for shared feeds (feeds; with `"mount": "hopper"`),
`carry` (weight relief, kg), `hilt` for melee roots (damage, swing, reach, cleave, stun, hands), `melee` for heads and
mods (damage, damageMult, swingMult, reach, cleave, stun), `hand`, `gripAngle`, `muzzles`, `optic`, `sight`,
`round` for ammo (`pellets`: the projectiles the round is split into, sharing its damage), `feedStyle`, `flags`,
`requiresAction`, `mount`, `placement` (which of its socket's `points` the part is drawn at) (an old `scrap` is ignored); sockets
take `name`, `type`, `pos`, `points` (named other positions: `{"under": [x, y, z]}`), `rot`, `accepts`, `required`. Unknown keys, bad numbers, unknown colours and boxes far from the part are refused with the file and
the problem; the self-test loads every part file and feeds it broken ones.

Builds save as one line in `workbench.txt` (a new file next to the game; nothing older changes):
`Name|volley|Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:FMJ}}`.
A save naming a part that no longer exists, or a receiver past a grip's capacity, loads with that part dropped; a part
that was renamed or merged (`WeaponBuild.Renamed`: the chain-fire grips, the calibre-named ammo) loads as the new one. The
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
  (`"receiver": {"stockAction": "Carbine Sear"}` keeps the class, bore and sound), and `null` removes a key;
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
  "receiver": {"soundOf": "Vanguard AR", "stockAction": "Carbine Sear"},
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

## The part set: 267 parts

Milestone 1's 75 parts, 48 from milestone 2, the three one-receiver grips of the workbench rework, 28 actions, the
universal-ammo pass's 4 extra round types and 8 feeds, the six optics, seven muzzle devices and four foregrips of the attachments pass
(2026-10-23), the 17 splitters, 10 barrels and 7 muzzle devices of the splitters, barrels and muzzles pass, and the late-game
pass's 35 parts less the three removed receivers (2026-10-27; its new parts in bold italics are listed in "The late-game
parts"). By tier, all
categories (first-tier grips, common and uncommon, hold exactly **one receiver**: the user's call, 2026-10-22):

| Category | Common | Uncommon | Rare | Epic | Legendary | Wonder |
| --- | --- | --- | --- | --- | --- | --- |
| Grip (30) | **Service Pistol Grip (1, 4 kg)**, **Service Rifle Grip (1, 8 kg)**, Vertical Foregrip, Skeleton Foregrip | **Sport Rifle Grip (1, 10 kg)**, Angled Foregrip, Thumb Stub | Double-Tap (2), Pistol Grip (3, 5 kg), Quad Rack (4), Penta Rack (5), Hex Rack (6), Briefcase (4, hip fire), Chaingun Handle, **Mule Frame (6, 16 kg)** | Septet Rack (7), Octet Rack (8), Rifle Grip (9, 12 kg), Steering Wheel (8, chain fire), **Exo Harness (12, chain fire, 30 kg)** | Chainsaw Grip (6), **Gantry Rig (16, 48 kg)**, ***Titan Harness (12, 45 kg)***, ***Third Arm*** | Push Cart (16, a real cart), ***Parade Float (16, 60 kg)*** |
| Receiver (25) | ***Peashooter*** (13), Plinker (20), Stampede (18), Workhorse (26), Farmhand Pump (14 x 8), **Courier Carbine** (23) | Longreach Bolt (119), **Kingpin Revolver** (64), **Reaper Battle Rifle** (35) | Bucket Launcher (126), Gravedigger Auto (11 x 8), **Longbow DMR** (43), **Street Sweeper** (15 x 8), **Bastille LMG** (30) | Hellwheel Rotary (31), Thumper Chain Gun (76), **Railspike Coil** (150), ***Hand Mortar*** (144) | Sparkplug Energy (39), **Hammerfall Autocannon** (187), ***Bunker Buster*** (255) | **Pepperbox** (32), **Sawmill** (18 x 8), ***Sunbeam Lance*** (240), ***Parade Cannon*** (360) |
| Barrel (20) | Standard, Stub, Scattermouth, Launcher Tube, **Sawn-Off** | Long, Blunderbuss Bell, **Heavy Bore**, **Squeeze Bore**, **Ported** | Big Iron, Fluted Match, **Bloat**, **Needle**, **Heatsink** | Coil, **Flechette Sleeve**, **Marksman** | **Cannon Sleeve** | Barrel of Barrels |
| Splitter (23) | Y-Splitter (2), Narrow Twin (2), Wide Y (2), **Riser Offset** | Over-Under (2, spread x0.85), Diagonal (2), Stepped (3), **Drop / Sidestep Offset** | Trident (3, x0.8), Tower (3 high), Pyramid (3), Quad Line (4), **Dogleg / Canted Offset** | Peacock (5 fanned, x0.75), Totem (4 high), Cross (4) | Party Tray (4, x0.7), Ladder (6) | **Hydra (6, x0.6)**, Rosette (6), Wall (9) |
| Feed (28) | Box, Stick, Shell Tube, Single Breech, **Belt Pouch** (heavy, 25, belt), **Battery Clip** (energy, 24), **Three-Round Tube** (launcher) | Extended, Heavy Box, **Heavy Drum** (30), **Shell Box** (10), **Capacitor Pack** (energy, 32), **Revolver Cylinder** (launcher, 6) | Drum, Shell Drum, Energy Cell, **Link Chute** (heavy, 60, belt), **Manifold Hopper (shared, 3)** | Belt Box, **Shell Hopper (shared, 4)**, **Ammo Can Manifold (shared, 6)** | Backpack Hopper, **Bottomless Bucket (shared, 9)**, **Power Bus (shared, 8)**, ***Conga Drum*** (150), ***Shell Silo (shared, 6)*** | Toaster Feed, ***Clown Car Hopper (shared, 16, 999)*** |
| Ammo (18, universal) | FMJ, Hollow Point, Buckshot | Slug, Armour Piercing, Frag | Match Grade, Plasma, Subsonic | Explosive Tip, Dragon's Breath, Shock | Sabot, ***Depleted Core***, ***Thermobaric*** | Rubber Duck, ***Bowling Ball***, ***Pocket Sun*** |
| Damage Multiplier (8) | Hot Load Kit | Overpressure Chamber | Tungsten Core Injector | Rail Accelerator | Singularity Chamber, ***Tripler Chamber*** | Spite Amplifier, ***Five-Alarm Chamber*** |
| Receiver Upgrade (10) | Lightweight Bolt, Match Trigger Group | Buffer Spring Kit, Heavy Hammer | Gas Overclock, Coil Hammer | Twin Sear | Overclock Core, ***Doom Hammer*** | ***Overkill Coprocessor*** |
| Stock (7) | Fixed, Wire | | Recoil Buffer, **Shoulder Harness (+5 kg carry)** | ***Exo Spine (+8 kg)*** | Gyro (+8 kg) | ***Jetpack Stock (+12 kg, mobility +0.15)*** |
| Optic (13) | Red Dot, Bead Rail | Holographic, Pocket 1.8x Prism | 2.5x ACOG, Outrider 2x Scout, Periscope Sight | 4x Scope, Owl Tube 2x, ***Lighthouse 6x*** | 8x Sniper Scope, Rangefinder 4x | ***All-Seeing Eye*** (marks zombies) |
| Muzzle (18) | Flash Hider, Thread Protector | Compensator, Muzzle Brake, Recoil Hook, Vent Collar | Suppressor, Blunderbuss Flare, **Long Choke**, **Tumbler Ring** | Shrouded Can, Duckbill Spreader (rare until 2026-10-27), **Recoil Eater**, **Velocity Booster**, **Fork Crown** | **Loudener** (2.5x loudness, +6% damage, +25% recoil, 1.4 kg), **Dragon Mouth**, **Hush Baffles** | |
| Utility (8) | Bayonet, **Padded Sling (+1.5 kg)** | Laser Sight | ***Gravity Sock (+4 kg)*** | ***Exo Sling (+8 kg)*** | **Anti-Grav Coil (+12 kg)** | Cup Holder, **Helium Balloons (+15 kg)** |
| Hilt (8) | Wooden Handle, Bat Grip | Long Haft, Fishing Rod Grip | Sword Hilt | Motor Hilt | ***Lamp Post*** | ***Turbine Hilt*** |
| Head (11) | Machete Blade, Bat Barrel | Sledgehammer Head, Fire Axe Head | Katana Blade, Frying Pan | Chainsaw Blade | ***Wrecking Ball*** | Frozen Fish, ***Swordfish***, ***Grandfather Clock*** |
| Melee Mod (9) | Nail Ring, Grip Tape | Counterweight, Barbed Wire Wrap | Shock Coil | ***Tesla Wrap*** | Rocket Booster | Whoopee Cushion, ***Jet Engine*** |

Special flags (incendiary, explosive, penetrating, plasma, squeaky, bouncy, bayonet, suppressed, chainsaw, fish, ...) are
carried into the built weapon and shown in the workbench; their gameplay effects come with the Zombies mode.

## Weight and run progression (proposed, open for the user)

Weight stays the main limiter (decided). A grip carries up to twice its carry weight, and the build gets slower and
slower to aim and move past its carry weight. Bigger, rarer frames carry more and hold more receivers, and
weight-relief parts (`"carry"`) raise what any grip carries, so the huge guns are a **late-run** thing:

| Rounds (proposed) | What drops (part tiers) | Frames you can expect | What a build looks like |
| --- | --- | --- | --- |
| 1-3 | common 85%, uncommon 15% | Service Pistol Grip (4 kg), Service Rifle Grip (8 kg), Sport Rifle Grip (10 kg): **one receiver each** | the start builds: Wall Rifle, Wall Pistol, a Machete |
| 4-9 | + rare 8% | + Double-Tap (2), Pistol Grip (3), Quad / Penta / Hex Rack (4-6), Briefcase | the first two- to six-receiver guns, a Y-splitter shotgun, first upgrades |
| 10-17 | rare 24%, epic 6% | + **Mule Frame (16 kg, 6)**, Shoulder Harness, then the epic frames: **Exo Harness (30 kg, 12)**, Septet / Octet Rack (7, 8), Rifle Grip (9), Steering Wheel | three to twelve receivers, a Manifold Hopper, Gas Overclock, Rail Accelerator |
| 18-24 | epic 22%, legendary 5% | + **Gantry Rig (48 kg, 16)**, **Titan Harness (45 kg, 12)**, Anti-Grav Coil | the legendary parts: Bunker Buster, Tripler Chamber, Doom Hammer, Depleted Core, Conga Drum |
| 25+ | legendary 18-30%, wonder 5-13% | + **Parade Float (60 kg, 16)**, the Push Cart, Jetpack Stock | broken, silly, sixteen receivers: Parade Cannons on a Five-Alarm Chamber, the Clown Car Hopper, the All-Seeing Eye |

The numbers live in `Parts/Tables/DropTables.json` (format `call-of-slop-drops/1`): part tiers by round, the chance a
zombie drops a part by round (4% -> 10%), the mystery box's build tier by round, and prices (box 950, slot upgrade 4000,
wall weapon 500), and what a **slot machine** pull pays by round (`slotTiers`: nothing, points back or a
loose part of a tier; ZOMBIES-MODE.md, "Slot machines": the slots are how a run gambles for single parts, since the box
gives complete builds only). `RunTables.Roll(rows, round, seed)` rolls deterministically (the same seed gives the same tier on
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
  "build": "Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Y-Splitter{barrel_1:Scattermouth Barrel,barrel_2:Scattermouth Barrel},feed:Shell Tube,ammo:Buckshot},stock:Wire Stock}"
}
```

`source` says where it shows up: `box` (mystery box), `wall` (wall weapon), `start` (a run's starting gear). Every file
must assemble into a complete, legal build with today's parts, or it's refused with the reason (self-test). The
workbench lists them under **Standard builds** and loads one as a preset into the editor. An optional `"chainFire": true`
(or false) sets the Chain fire checkbox; without it the build uses the grip's default. The first 64 (2026-10-23: 34
new box builds from the workbench rework's receivers and 2-8 slot grips; 76 more followed on 2026-10-27, see "The prefab
builds" below, 140 in all):

| Tier | Guns | Melee |
| --- | --- | --- |
| Common | Wall Rifle (wall, start), Wall Pistol (wall, start), Wall Pump (wall), Wall SMG (wall): all on the one-receiver Service grips | Machete (start, box) |
| Uncommon | Twin Splitter Shotgun, Burst Marksman (box, wall): on the Sport Rifle Grip; Kingpin Hand Cannon (a heavy-round revolver, its Long Barrel sized down); **Reaper Marksman** (semi battle-rifle DMR, holo), **Ratchet PDW** (a Stampede on the Ratchet Burst Group: a burst carbine), **Buzzbox Machine Pistol** (a Plinker on the Buzzsaw Sear, 1265 rpm, laser), **Courier Carbine** (light auto carbine, long barrel) | Nail Bat, Fire Axe, **Barbed Slugger** (barbed wire and nails: cleave 2, bleed) |
| Rare | Hopper Triplets (Mule Frame, shared feed), Auto Shotgun, Triple Pistol (the three-receiver Pistol Grip), Double-Tap Twins (chain fire), Longbow Marksman; **Street Sweeper** (semi shotgun on a shell drum), **Bastille Squad LMG** (drum, ACOG), **Buzzbox Akimbo** (two Plinkers on Buzzsaw Sears, chain fire), **Four-by-Three** (four Workhorses on Three-Round Burst Groups, the Quad Rack), **High Five** (five pistols on the Penta Rack), **Executive Decision** (two SMGs in the Briefcase on a Manifold Hopper, hip fire), **Elephant Gun** (.50 bolt action, tungsten core) | Sledgehammer, Frying Pan, **Shock Katana** (shock arcs), **Big Bonk** (a frying pan on a long haft: bonk and shock) |
| Epic | Minigun, Belt-Fed Chaingun, Exo Twelve (12 receivers, chain fire, ammo can manifold); **Firing Squad** (seven battle rifles, Septet Rack), **Dragon's Choir** (four pumps on a Shell Hopper of Dragon's Breath), **Road Rage** (eight carbines round the Steering Wheel, chain fire), **Demolition Marksman** (DMR on explosive tips, 4x), **Twin Thumpers** (two belt-fed chain guns, Mule Frame), **Grenade Organ** (four launchers on the Quad Rack, chain fire), **Rail Battery** (six coil rifles on plasma, Hex Rack) | Chainsaw Sword, **Lawnmower** (a chainsaw bar on a long haft with wire and nails: cleave 5, bleed) |
| Legendary | Gantry Sixteen (16 receivers, bottomless bucket), Lumberjack, Hammerfall Cannon (autocannon); **Power Grid** (eight Sparkplugs on a Power Bus, Octet Rack), **Siege Battery** (two autocannons with singularity chambers on an ammo can), **Hellfire Minigun** (long barrels, explosive tips, overclock core), **Kitchen Sink** (nine different receivers on one Rifle Grip), **Last Word** (suppressed .50 bolt action, 8x), **Suppressing Fire** (six LMGs on a bottomless bucket) | Rocket Katana, **Rocket Hammer** (sledgehammer, rocket booster, shock coil) |
| Wonder | Shopping Spree (the push cart), Toaster Duck, Hydra Platter, Pepperbox Pistol; **Party Cannon** (a toaster-fed launcher through a Peacock splitter, balloons on: explosive, toasty, party), **Seven Shooter** (a revolver behind the Barrel of Barrels), **Sawmill Inferno** (two shotgun chain guns on the Chainsaw Grip, Dragon's Breath), **Drive-Thru** (eight SMGs round the Steering Wheel on a bottomless bucket, with a cup holder: refreshing) | The Codfather (a frozen fish on a rod), **Flying Fish** (a frozen fish with a rocket booster and a whoopee cushion: knockback, stun) |

**The box pool.** The box rolls uncommon to wonder (`boxTiers`) and gives **guns only** (developer answer, 2026-10-26):
uncommon 17, rare 22, epic 20, legendary 20, wonder 19 (98 box guns, since the prefab builds of 2026-10-27). The melee column above is tagged `"slots"` instead:
the **slot machines** pay those melee builds (32 with the prefab builds) as a complete build into the melee slot (ZOMBIES-MODE.md, "Slot
machines"); the loader refuses a melee build tagged `"box"` and a gun tagged `"slots"`. Melee parts (hilts, heads, mods)
stay in the part pool (drops, slot parts, salvage piles). The roll picks a gun of the rolled tier that you **don't already
carry** in a gun slot. How a build is tiered: within one tier of its rarest part (a build of common parts on a rare grip
is rare; a wonder part makes it legendary or wonder), placed by what it does next to the others of that tier.

**Self-test** (section "Weapon builder", `CheckBoxBuilds` in SelfTestWeaponBuilder2.cs): every box build is complete and legal and its model has no
floating boxes (section "Model connectivity"); its tier is within one of its rarest part's (slot melee builds too); every
box tier has 5 or more guns; and its numbers stay in the tier's band: a gun does 60 DPS or more (body, close range), and at
most 250 / 250 / 450 / 1500 / 3000 / 5000 body damage per trigger pull and 600 / 600 / 1200 / 3000 / 8000 / 12000 DPS
(common to wonder); a melee build does 30 DPS or more and at most 150 / 150 / 200 / 250 / 400 / 400 damage a hit. The
Zombies section checks the box never offers a gun you carry nor any melee build, and offers 5 or more different guns of
every tier; "Zombies slots" checks the melee payouts. Approximate numbers (CI's log lists them all as `box ...` lines):

| Build | Tier | Per pull | RPM | DPS | Mag |
| --- | --- | --- | --- | --- | --- |
| Reaper Marksman / Courier Carbine / Ratchet PDW / Buzzbox Machine Pistol | uncommon | 35 / 23 / 18 x3 burst / 20 | 400 / 720 / 1000 / 1265 | 233 / 276 / 150 / 422 | 45 |
| Street Sweeper / Bastille Squad LMG / Elephant Gun | rare | 120 / 30 / 193 | 200 / 600 / 45 | 400 / 300 / 145 | 20 / 75 / 10 |
| Buzzbox Akimbo / Four-by-Three / High Five / Executive Decision | rare | 20 / 104 / 100 / 40 | 2200 / 800 burst / 420 / 900 | 733 / 657 / 700 / 600 | 40 / 20 / 20 / 60 |
| Firing Squad / Dragon's Choir / Road Rage / Twin Thumpers | epic | 245 / 480 / 23 / 152 | 400 / 70 / 3000 / 450 | 1633 / 560 / 1150 / 1140 | 20 / 12 / 160 / 150 |
| Demolition Marksman / Grenade Organ / Rail Battery | epic | 81 / 139 / 1188 | 270 / 240 / 45 | 365 / 556 / 891 | 30 / 4 / 40 |
| Power Grid / Siege Battery / Hellfire Minigun / Suppressing Fire | legendary | 416 / 748 / 52 / 180 | 720 / 200 / 3000 / 600 | 4992 / 2493 / 2600 / 1800 | 50 / 150 / 400 / 150 |
| Kitchen Sink / Last Word | legendary | 451 / 343 | 70 / 76 | 526 / 434 | 6 / 10 |
| Party Cannon / Seven Shooter / Sawmill Inferno / Drive-Thru | wonder | 713 / 581 / 450 / 40 | 162 / 150 / 360 / 3000 | 1925 / 1453 / 2700 / 2000 | 2 / 10 / 20 / 900 |

New melee: Barbed Slugger 69 dmg, 0.72 s, cleave 2; Shock Katana 68, 0.40 s; Big Bonk 75, 1.14 s, stun 1.8 s; Lawnmower
85, 0.61 s, cleave 5; Rocket Hammer 125, 1.07 s, stun 1.6 s; Flying Fish 58, 0.35 s, stun 2.5 s.

The build files are plain data in today's format (receivers carry their own fire type), so a later migration that
moves fire types into parts can rewrite them like any other build.

### The prefab builds (developer request, 2026-10-27)

The developer: "make 50 to 100 more new prefabricated weapons to go along with this" (the splitters, barrels, muzzles and
late-game parts of the same day). 76 new standard builds, data only (`Parts/Builds`), spread over every tier: everyday
guns at common and uncommon that show off the new splitters (side by side, stacked, staircases, offsets), the bullet-size
barrels (heavy bore, bloat, squeeze bore, needle) and the stronger muzzles; and late guns built on the late-game parts
(Parade Cannon, Sunbeam Lance, Bunker Buster, Clown Car Hopper, Parade Float, All-Seeing Eye, Five-Alarm Chamber, Bowling
Ball, Pocket Sun, Perpetual Sear, Rosette and Wall splitters, Cannon Sleeve, Dragon Mouth...). The commons are tagged
`"wall"` (presets a map can put on a wall; the box rolls uncommon and up), the guns from uncommon up `"box"`, the melee
builds `"slots"`. The drop, box and slot tables needed no change: they roll a tier, and each tier's pool simply grew.

**Rules each build keeps** (self-tests "Weapon builder" and the new **Standard builds**, `SelfTestStandardBuilds.cs`):
complete and legal (BuildRules: carry weight, parts, barrels, splitter depth), one connected model ("Model connectivity"),
its tier within one of its rarest part's, inside its tier's band per trigger pull and DPS (above), and: every part the
splitters/barrels/muzzles and late-game passes added is in at least one build (69 of 69); names unique even ignoring case,
spaces and punctuation; descriptions of 160 characters at most; at least 8 / 15 / 20 / 18 / 18 / 18 guns and 3 / 5 / 6 /
4 / 4 / 5 melee builds per tier (common to wonder); the median gun power and the median melee power (`PowerCurve.GunPower`
/ `MeleePower`) rise tier by tier, and every wonder box gun is stronger than the median epic one. The medians now: guns
66 / 134 / 160 / 404 / 1028 / 1757, melee 114 / 136 / 227 / 431 / 573 / 1727.

**The old wonder box guns, raised** so wonder feels like wonder (they were weaker than the median epic gun): Hydra Platter
(perpetual sear, pocket suns, spite amplifier: 295 -> 6050 DPS), Toaster Duck (two parade cannons firing ducks: 141 ->
810), Party Cannon (a parade cannon and an overkill coprocessor: 209 -> 1925), Pepperbox Pistol (pocket suns, overkill
coprocessor: 320 -> 2268), Seven Shooter (depleted core, tripler chamber: 280 -> 1453), Shopping Spree (pocket suns: 4320
-> 9600) and Drive-Thru (pocket suns: 900 -> 2000).

**Model fixes found on the way.** A new check in "Standard builds" fits every multiplier and upgrade into every receiver's
sockets and checks the model connects: the Thumper Chain Gun's amp and side-upgrade sockets, the Bunker Buster's (it
extends the Thumper) and the Hellwheel's amp socket sat 2 to 3.5 cm off the receiver's body, so any part there floated.
They now sit on the body's sides (`Parts/Receiver/ThumperChainGun.json`, `HellwheelRotary.json`).

**No damage cap for builds** (developer decision, 2026-10-27: "Remove it for builds"). A built gun's damage a projectile
is no longer clamped to the weapon files' 1000 (`BuildStats.Clamp`; only a bound of 1,000,000, `BuildStats.MaxDamage`,
far below int overflow once headshots and perks multiply it). Weapon files (multiplayer guns) keep the 1000 cap
(`WeaponFile.Ranges`). Damage never goes on the wire (builds replicate as build text; no message carries a damage number), zombie and player
health are ints, so nothing overflows; no protocol change. The box bands per pull and DPS still hold every box build.
Over 1000 a projectile among the standard builds: **The Oracle** (1201 a bolt). "Standard builds" checks a fully stacked
Bunker Buster build does more than 1000 a projectile (26443) while a weapon file asking for 5000 is refused.

| Build | Source | Numbers (body damage, close range) |
| --- | --- | --- |
| **Common** | | |
| Pea Shooter | wall | 1 rec, 13 a pull, 400 rpm, 87 DPS |
| Sawn-Off Special | wall | 1 rec, 168 a pull, 120 rpm, 336 DPS |
| Coach Gun | wall | 1 rec, 128 a pull, 70 rpm, 149 DPS |
| Wide Load | wall | 1 rec, 23 a pull, 720 rpm, 276 DPS |
| High Rider | wall | 1 rec, 26 a pull, 400 rpm, 173 DPS |
| Paper Route | wall | 1 rec, 21 a pull, 900 rpm, 315 DPS |
| Weed Whacker | slots | melee: 62 a hit, 0.53 s, cleave 2, 118 DPS |
| Batting Practice | slots | melee: 49 a hit, 0.65 s, cleave 1, 76 DPS |
| **Uncommon** | | |
| Heavy Hitter | box | 1 rec, 31 a pull, 600 rpm, 310 DPS |
| Skinny Dipper | box | 1 rec, 21 a pull, 720 rpm, 252 DPS [penetrating] |
| Bunk Bed | box | 1 rec, 35 a pull, 285 rpm, 166 DPS |
| Staircase | box | 1 rec, 18 a pull, 900 rpm, 270 DPS |
| Diagonal Argument | box | 1 rec, 64 a pull, 150 rpm, 160 DPS |
| Low Rider | box | 1 rec, 26 a pull, 600 rpm, 260 DPS |
| Sidewinder | box | 1 rec, 26 a pull, 720 rpm, 312 DPS |
| Riot Control | box | 1 rec, 112 a pull, 200 rpm, 373 DPS |
| Prairie Lever | box | 1 rec, 64 a pull, 110 rpm, 117 DPS |
| Pea Souper | box | 1 rec, 12 a pull, 1100 rpm, 220 DPS |
| Fence Post | slots | melee: 72 a hit, 1.04 s, cleave 3, 69 DPS |
| Bait and Tackle | slots | melee: 69 a hit, 0.81 s, cleave 2, 85 DPS |
| Hedge Trimmer | slots | melee: 74 a hit, 0.65 s, cleave 3, 114 DPS |
| **Rare** | | |
| Gutbuster | box | 1 rec, 44 a pull, 600 rpm, 440 DPS |
| Sewing Kit | box | 1 rec, 34 a pull, 300 rpm, 170 DPS |
| Tower of Power | box | 1 rec, 26 a pull, 750 rpm, 325 DPS |
| Pyramid Scheme | box | 1 rec, 120 a pull, 200 rpm, 400 DPS |
| Line Dance | box | 1 rec, 18 a pull, 1100 rpm, 330 DPS |
| Cant Stop | box | 1 rec, 35 a pull, 400 rpm, 233 DPS |
| Dogleg Express | box | 1 rec, 30 a pull, 672 rpm, 336 DPS |
| Tumble Dryer | box | 1 rec, 96 a pull, 260 rpm, 416 DPS |
| Heavy Pockets | box | 3 rec, 69 a pull, 720 rpm, 828 DPS |
| Choke Hold | box | 2 rec, 112 a pull, 140 rpm, 261 DPS |
| Kendo Club | slots | melee: 53 a hit, 0.45 s, cleave 1, 118 DPS |
| Pan Handler | slots | melee: 85 a hit, 1.04 s, cleave 3, 82 DPS |
| Paper Cut | slots | melee: 66 a hit, 0.45 s, cleave 3, 147 DPS |
| **Epic** | | |
| Totem Pole | box | 1 rec, 33 a pull, 900 rpm, 495 DPS |
| Crossroads | box | 1 rec, 76 a pull, 450 rpm, 570 DPS [penetrating] |
| Lighthouse Keeper | box | 1 rec, 77 a pull, 300 rpm, 385 DPS |
| Steady Eddie | box | 1 rec, 37 a pull, 900 rpm, 555 DPS [plasma] |
| Forked Tongue | box | 2 rec, 96 a pull, 800 rpm, 1280 DPS |
| Flechette Storm | box | 1 rec, 96 a pull, 390 rpm, 624 DPS |
| Trench Party | box | 2 rec, 158 a pull, 120 rpm, 316 DPS [explosive] |
| Backbone | box | 3 rec, 105 a pull, 900 rpm, 1575 DPS |
| Slingshot Squad | box | 7 rec, 126 a pull, 720 rpm, 1512 DPS |
| Whisper Network | box | 6 rec, 192 a pull, 400 rpm, 1280 DPS [suppressed] |
| Zap Happy | slots | melee: 73 a hit, 0.72 s, cleave 1, 101 DPS |
| Motor Axe | slots | melee: 84 a hit, 0.54 s, cleave 3, 155 DPS |
| Lightning Rod | slots | melee: 82 a hit, 0.78 s, cleave 3, 105 DPS |
| **Legendary** | | |
| Ground Breaker | box | 1 rec, 883 a pull, 200 rpm, 2943 DPS [penetrating] |
| Clap Back | box | 1 rec, 384 a pull, 300 rpm, 1920 DPS [penetrating] |
| Snowball Effect | box | 3 rec, 78 a pull, 2400 rpm, 1733 DPS |
| Hot Takes | box | 1 rec, 1080 a pull, 200 rpm, 3600 DPS [explosive,incendiary] |
| Titan's Tantrum | box | 12 rec, 76 a pull, 3000 rpm, 3800 DPS [penetrating] |
| Fire Escape | box | 1 rec, 636 a pull, 200 rpm, 2120 DPS [penetrating] |
| Library Card | box | 1 rec, 146 a pull, 300 rpm, 730 DPS [penetrating,suppressed] |
| Helping Hand | box | 1 rec, 45 a pull, 2400 rpm, 1800 DPS [explosive,incendiary] |
| Hat Trick | box | 1 rec, 184 a pull, 800 rpm, 1162 DPS |
| Grain Elevator | box | 6 rec, 1056 a pull, 200 rpm, 3520 DPS [explosive,incendiary] |
| Conga Line | box | 2 rec, 118 a pull, 2688 rpm, 5286 DPS [penetrating] |
| Street Light | slots | melee: 115 a hit, 0.54 s, cleave 4, 213 DPS |
| Wrecking Crew | slots | melee: 182 a hit, 1.52 s, cleave 5, 120 DPS |
| Last Orders | slots | melee: 164 a hit, 1.55 s, cleave 3, 106 DPS |
| **Wonder** | | |
| Grand Marshal | box | 8 rec, 3168 a pull, 90 rpm, 4752 DPS [explosive,party,toasty] |
| Sixteen Clowns | box | 16 rec, 432 a pull, 1100 rpm, 7920 DPS [incendiary,party,plasma] |
| The Oracle | box | 1 rec, 1201 a pull, 300 rpm, 6005 DPS [incendiary,marks,penetrating,plasma] |
| Five-Alarm Chili | box | 2 rec, 1500 a pull, 252 rpm, 6300 DPS [chainsaw,incendiary] |
| Gutter Ball | box | 3 rec, 1014 a pull, 128 rpm, 2163 DPS [bouncy,launch] |
| Bouquet | box | 1 rec, 139 a pull, 3000 rpm, 6950 DPS [incendiary,party,plasma] |
| Brick Wall | box | 1 rec, 1850 a pull, 140 rpm, 4317 DPS [explosive,incendiary] |
| Dragon's Hoard | box | 1 rec, 2512 a pull, 200 rpm, 8373 DPS [incendiary,plasma] |
| Perpetual Motion | box | 1 rec, 106 a pull, 3000 rpm, 5300 DPS [penetrating] |
| Clown Car | box | 8 rec, 120 a pull, 2080 rpm, 4160 DPS [incendiary] |
| Perfect Game | box | 1 rec, 936 a pull, 170 rpm, 2652 DPS [bouncy,explosive,launch,party] |
| Sushi Chef | slots | melee: 160 a hit, 0.25 s, cleave 7, 640 DPS |
| Time Out | slots | melee: 264 a hit, 1.69 s, cleave 6, 157 DPS |
| Blender | slots | melee: 94 a hit, 0.28 s, cleave 6, 337 DPS |
| Cuckoo Clock | slots | melee: 207 a hit, 1.03 s, cleave 5, 200 DPS |
| Hydra Platter (raised) | box | 1 rec, 165 a pull, 2200 rpm, 6050 DPS [incendiary,plasma] |
| Toaster Duck (raised) | box | 2 rec, 540 a pull, 90 rpm, 810 DPS [bouncy,explosive,party,squeaky,toasty] |
| Party Cannon (raised) | box | 1 rec, 713 a pull, 162 rpm, 1925 DPS [explosive,party,toasty] |
| Pepperbox Pistol (raised) | box | 1 rec, 126 a pull, 1080 rpm, 2268 DPS [incendiary,plasma] |
| Seven Shooter (raised) | box | 1 rec, 581 a pull, 150 rpm, 1452 DPS [penetrating] |
| Shopping Spree (raised) | box | 16 rec, 640 a pull, 900 rpm, 9600 DPS [cart,incendiary,plasma] |
| Drive-Thru (raised) | box | 8 rec, 40 a pull, 3000 rpm, 2000 DPS [incendiary,plasma,refreshing] |

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
| **Plain Rifle**: Workhorse, standard barrel, flash hider, box mag, FMJ, fixed stock, red dot | Rifle Grip | 1 / 1 | 26 | 600 auto | 30 | 2.2 s | 0.95 | 260 |
| **Triple Pistol**: three Plinkers, stick mags, FMJ (volley) | Pistol Grip | 3 / 3 | 60 (3 x 20) | 400 semi | 20 | 2.4 s | 0.99 | 400 |
| **Nine Lives**: nine Workhorses with stub barrels, fixed stock | Rifle Grip | 9 / 9 | 234 (9 x 26) | 600 auto | 20 | 3.9 s | 0.67 | 2340 |
| **Twin Splitter Shotgun**: Farmhand Pump, Y-Splitter, two Scattermouths, buckshot | Rifle Grip | 1 / 2 | 112 (2 x 8 x 7) | 70 pump | 6 | 3.0 s | 0.95 | 131 |
| **Belt-Fed Chaingun**: Thumper, Big Iron, Belt Box, Armour Piercing, chaingun handle | Rifle Grip | 1 / 1 | 76 | 450 (0.4 s spool) | 150 | 6.0 s | 0.86 | 570 |
| **Minigun**: Hellwheel with six standard barrels, Backpack Hopper, FMJ | Rifle Grip | 1 / 6 (one per shot) | 31 | 2400 (1.2 s spool) | 400 | 9.0 s | 0.81 | 1240 |
| **Lumberjack**: six Gravedigger auto shotguns, shell drums, buckshot | Chainsaw Grip | 6 / 6 | 528 (6 x 8 x 11) | 260 auto | 20 | 5.6 s | 0.40 | 2288 |
| **Shopping Spree**: sixteen Stampede SMG receivers (hip fire only) | Push Cart | 16 / 16 | 288 (16 x 18) | 900 auto | 20 | 4.9 s | 0.40 | 4320 |
| **Party Platter**: Workhorse, Party Tray of four Tridents, twelve stub barrels | Rifle Grip | 1 / 12 | 26 (12 x 2.2: conserved) | 335 auto | 75 | 3.4 s | 0.87 | 145 |
| **Toaster Duck**: Bucket Launcher, Launcher Tube, Toaster Feed, Rubber Duck | Pistol Grip | 1 / 1 | 94 | 90 semi | 2 | 1.0 s | 0.96 | 141 |

The big multi-receiver numbers (Lumberjack, Shopping Spree) are deliberately silly in this sandbox; in Zombies they
are gated by weight (both sit at the 0.40 mobility floor), part rarity, and by zombie health scaling with
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

1. **Volley or rotate?** Fixed by the grip. (Since: a per-build Chain fire checkbox; a volley's receivers fire at their own rates, 2026-10-27.)
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
   **Universal ammo balance** (2026-10-23): the Frag's x1.1 made the two Frag launchers 10% stronger (keep, or x1.0?);
   Buckshot on a pistol loses a fifth to rounding (2 x 8 = 16 of 20): raise the pistol calibre to 24 or accept it; the
   Slug's +15 m and Armour Piercing's x1.15 range on a heavy receiver (138-240 m) are far beyond any map; and the
   Hollow Point / Match Grade / Subsonic numbers are first guesses.
3. **The manned cart:** are the traverse limits right (60 degrees either side, 15 down, 30 up)? Should it ever fire
   while pushed (hip fire with heavy spread), or stay "park it, then man it"?
4. **The numbers marked proposed:** the drop tables and prices, the weight penalties, melee stun
   on players (off today).
5. **The power curve (2026-10-27):** are the floors right (the best part of a tier x1 / x1 / x1.1 / x1.3 / x1.6 / x2.2
   the best common in the damage rows), and the arrival rounds (legendary drops from 18, wonder from 25; slots capped at
   epic before round 20)? (The 1000-a-projectile cap is gone for builds: decided 2026-10-27.) A jetpack-hop stock needs the jump on the wire (a protocol change), so the Jetpack
   Stock gives speed instead: worth doing the hop? Should the Thumper and Sawmill (named for chain fire) go the way of
   the burst receivers too? There is no wonder muzzle device yet.
