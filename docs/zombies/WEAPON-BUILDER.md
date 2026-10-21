# Zombies: the weapon builder (milestone 1)

Status: **in development**. Milestone 1 is the modular weapon system, playable today as a sandbox: main menu >
**WORKBENCH (ZOMBIES PREVIEW)**. The Zombies mode itself (World at War style, co-op 1-4, rounds, new maps) comes
later and plugs into the hooks described here.

Anything marked **proposed** is a working default the code implements so it can be tested. It is open for the user to
change. The "Open creative questions" at the end list the calls that are the user's to make.

Code: `Source/Zombies/` (PartDef, PartFile, WeaponBuild, BuildRules, BuildStats, BuildModel, BuildStore), firing in
`Source/Game/GameBuilder.cs`, UI in `Source/UI/UiWorkbench.cs`, the range session in `Source/App/Program.Workbench.cs`,
self-test section "Weapon builder" in `Source/App/SelfTestWeaponBuilder.cs`. Data: `Parts/<Category>/*.json`, the
range map `Maps/Dev/WorkbenchRange.json`.

## How it fits the Zombies loop

- **Finding parts.** Zombies drop parts (later rounds roll rarer tiers), and salvage spots on the map give scrap.
- **Workbenches** on the map are where you assemble, swap and break down parts.
- **Mystery box** gives a *standard complete configuration*: a whole build (for example a ready-made Minigun or a
  Twin Splitter Shotgun), not a single part. You can then take it to a bench and change it.
- **Wall weapons** are plain, working guns (one grip, one receiver). Bought off the wall, they are builds too, so you
  can modify them at a bench.
- **Weapon slots.** Players carry **2 builds**. A purchasable unlock (the "Mule Kick" of this game) raises it to **4**.
  The workbench sandbox already shows 4 save slots, with slots 3 and 4 marked "unlockable in Zombies".

## The component model

A weapon is a **tree of parts** joined at **sockets**. Every part is one JSON file (`call-of-slop-part/1`) with a
category, a rarity tier, a weight, its sockets, a box model and stat effects (the same `stat`/`set`/`add`/`mul`
effects attachments use).

```
Grip (root: the part you hold)
 |- receiver_1 .. receiver_N    <- the grip decides N (pistol grip 3, rifle grip 9, push cart 16)
 |    |- barrel                 <- a barrel, or a SPLITTER whose outputs take barrels (or another splitter)
 |    |    \- muzzle            <- muzzle device
 |    |- feed                   <- box / drum / belt / tube / cell / hopper
 |    |- ammo                   <- calibre / special rounds
 |    \- utility                <- side rail (laser, bayonet, cup holder)
 |- stock
 |- optic
 \- foregrip                    <- vertical / angled / chaingun handle
```

**The grip is the root.** It holds the player's hand and has one `receiver` socket per receiver it can hold, so the grip
sets the receiver capacity. Each receiver has its **own subtree**: its own barrels, splitters, feed and ammo, so one
weapon can mix a pistol receiver, a shotgun receiver and a minigun receiver if the grip can carry the weight.

| Category | What it does | Count |
| --- | --- | --- |
| Grip | Root grips (capacity, fire styles, how it is held, carry weight) and foregrips (off hand) | 11 |
| Receiver | The action: semi, auto, burst, pump, bolt, rotary (minigun, spools up, barrels take turns), chain (spool-up). Sets the bore (calibre class) and base fire rate | 11 |
| Barrel | Range, damage, spread; bundles have several muzzles (Barrel of Barrels: 7) | 10 |
| Splitter | One shot in, N barrels out (barrel sockets take barrels or another splitter) | 5 |
| Feed | Magazine size and reload (box, stick, extended, drum, belt, hopper, tube, shell drum, cell, breech, toaster) | 12 |
| Ammo | Damage, pellets, range, special flags | 10 |
| Stock | Recoil, aim time, mobility | 4 |
| Optic | The game's optics (red dot, holo, ACOG, 4x, 8x) with their reticles and lens | 5 |
| Muzzle | Flash hider, compensator, brake, suppressor | 4 |
| Utility | Laser, bayonet, cup holder | 3 |

**Bores.** Each receiver fires one calibre class (`pistol`, `rifle`, `heavy`, `shell`, `launcher`, `energy`). Barrels,
splitters, feeds, ammo and muzzles list the bores they fit.

### Graph rules (BuildRules.cs)

Every refusal comes with a plain sentence saying why. These are the actual messages:

| Rule | Example "why not" |
| --- | --- |
| A part only goes in a socket of its mount type | "Red Dot mounts on an optic socket, not a receiver socket." |
| A socket can limit what it takes | "Hellwheel Rotary Receiver's barrel_1 socket takes barrels only, not splitters." |
| A build starts from a grip | "Rifle Grip is a grip you build on: start a new build with it instead." |
| Grip capacity | "The Pistol Grip holds 3 receivers; it has no receiver_4 slot." |
| Bores must match the receiver | "Box Mag is made for rifle rounds; the Plinker Receiver fires pistol rounds." |
| Actions: belts and hoppers need something that pulls them | "Belt Box needs auto, chain or rotary action; the Triad Burst Receiver is burst." |
| Splitters stack at most 2 deep | "Splitters only stack 2 deep: the shot gets too thin." |
| Barrels per receiver (default 12, the Hellwheel takes 3-6) | "Too many barrels on the Workhorse Receiver: 13; it handles at most 12." |
| Limits per weapon: 96 parts, 32 barrels, 240 projectiles per pull | "Too many projectiles per trigger pull: 256; the most is 240." |
| Weight: a grip carries up to 2x its carry weight | "Too heavy: 9.1 kg; the Pistol Grip can't carry more than 8 kg." |
| The fire style must be one the grip allows | "The Pistol Grip can't fire rotate; it does volley." |
| Required sockets (barrel, feed, ammo) | "Plinker Receiver needs a feed." (shown as "Needs:", not an error) |

A build that is incomplete still gets a model for the live preview. Only complete, legal builds get a `WeaponDef`.

## Stat pipeline (BuildStats.cs)

The output is an ordinary `WeaponDef`, the same type every weapon file makes, so the existing firing, recoil, ADS,
sound, HUD and bots all work unchanged. The pipeline is **deterministic**: the same tree always gives the same numbers
and the same muzzle positions, and the self-test checks this by assembling every example twice.

1. **Each receiver on its own.** Start from the receiver's base numbers (class defaults, action, fire rate), then
   apply its parts in stages: receiver, ammo, feed, splitters. Then each barrel path (barrel plus muzzle device) is
   applied to its own copy, so a long barrel and a stub barrel on one splitter really differ.
2. **Splitter rule.** Every output gets the incoming shot's damage times the splitter's `outputMult`. Nested
   splitters multiply. `1/N` conserves the shot (Y-Splitter: 2 x 0.5). Higher tiers give more than `1/N` (Trident
   3 x 0.4 = 1.2x, Party Tray 4 x 0.4 = 1.6x, Party Tray of Tridents = 12 barrels at 0.16 each = 1.92x). Pellets are
   per barrel, so a split shotgun really fires twice the pellets at half the damage each.
3. **Combine the receivers** by the grip's fire style (**proposed**, see below).
4. **The grip and what's on it:** grip, stock, foregrip, optic and utility parts, applied once to the whole weapon.
5. **Weight against the grip's carry weight** (**proposed**): mobility `-0.012/kg`, and `-0.03/kg` over the carry
   weight; ADS time `+0.008 s/kg` (+0.02 over); draw time `+0.03 s/kg` (+0.05 over); recoil times
   `sqrt(6 / (kg + 4))` (heavy guns kick less per shot, clamped 0.55-1.25x). Then the frame's own mobility (a cart
   is slow however light it is).
6. **Clamp** everything into the ranges weapon files allow (mobility 0.4-1.2 for builds).

### Several receivers (proposed, open for the user)

| | Volley (default) | Rotate |
| --- | --- | --- |
| A trigger pull | Fires every receiver at once | Fires the next receiver in turn |
| Fire rate | The slowest receiver's (they are linked) | The receivers' rates add up (max 3000 rpm) |
| Magazine | Per-receiver feeds, shown as the smallest (all run dry together) | Feeds add up |
| Recoil | Root of the sum of squares (3 receivers = 1.7x one) | The average |
| Trigger | The strictest: semi beats burst beats auto | Same |
| Reload | The longest feed, plus 0.2-0.35 s per extra receiver (the grip sets it); every feed is swapped at once | Same |

**Feeds are per receiver** (each receiver needs its own feed and ammo). That keeps every receiver's subtree complete on
its own and lets mixed calibres work. A shared-feed part (one drum feeding several receivers) is a good candidate for a
later legendary part.

Grips choose which styles they allow: the Pistol Grip and Chainsaw Grip are volley only, the Competition Pistol Grip and
the Steering Wheel rotate, rifle grips and the cart allow both (the player picks).

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
- **Network:** no packet changed (protocol stays at 15). Built weapons are host-local in the Workbench Range (single
  player). Co-op will need the build sent to clients (its text form, below) so they can draw it and predict shots:
  that's a protocol bump in the Zombies milestone.

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

**Chainsaw and cart view (proposed, open for the user).** Milestone 1 uses the normal first-person pose: the trigger
hand on the grip part's hand point and the off hand IK'd to the support point (chainsaw: the top wrap bar; cart: the
push bar). The cart model is the gun body, so it floats in front of you at hip height. A proper version would put the
cart on the ground (its own scene node parented to the player, not the view), push the hands forward onto the bar
with both arms straight, bob it with the walk cycle, and turn the camera less sharply (it is a cart). The chainsaw could
get an idle shake and a pull-start animation on equip.

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
minBarrels, maxBarrels, rpmPerBarrel), `frame` for root grips (fireStyles, hands, carry, mobility, ads, ironHeight,
support, reloadPerReceiver), `hand`, `gripAngle`, `muzzles`, `optic`, `sight`, `feedStyle`, `flags`, `requiresAction`,
`mount`, `scrap`. Unknown keys, bad numbers, unknown colours and boxes far from the part are refused with the file and
the problem; the self-test loads every part file and feeds it broken ones.

Builds save as one line in `workbench.txt` (a new file next to the game; nothing older changes):
`Name|volley|Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}`.
A save naming a part that no longer exists, or a receiver past a grip's capacity, loads with that part dropped.

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
