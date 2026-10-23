# Zombies: the game mode (milestone 3)

Status: **playable on its first real map**, **Blackmoor Asylum** (lobby: the **Zombies** pack, then the game mode
"Zombies"), plus the dev map `Maps/Dev/ZombiesTest.json` ("Zombies Test", Dev pack). Zombies only shows up for maps that
have a `"zombies"` section. Milestone 3 added the map, solid machine props, power-ups and special ammo effects. Styled after Call of
Duty: World at War's Nazi Zombies: solo or co-op up to 4, rounds that never end, points for every hit and kill,
boarded windows, doors that open new areas, wall guns, a mystery box, perk machines and the power switch. It's a
**roguelite run**: you start low (the "start" builds) and climb, inside one run, to broken silly guns through the
mystery box, wall buys, part drops and the workbench (the weapon builder: `WEAPON-BUILDER.md`).

Everything marked **proposed** is a working default for the user to change; the numbers live in data files.

Code: `Source/Game/ZombiesMode.cs` (run state, rounds, points, players, downs, the interact key, buyables, box, parts,
workbench), `ZombiesDrops.cs` (dropped builds), `ZombiesPowerUps.cs` (power-ups), `ZombiesAmmoEffects.cs` (special ammo), `ZombieAI.cs` (spawning, AI, hit tests), `ZombiesNet.cs` (messages, client mirror), `ZombieBots.cs` (co-op
bots, downed controls), `Source/Maps/MapZombies.cs` (the map section), `Source/Zombies/ZombiesRules.cs` (the rules
file), `Source/UI/UiZombies.cs` (HUD), `Source/UI/UiWorkbenchRun.cs` (the workbench in a run),
`Source/App/Program.Zombies.cs` (drawing, input, smoke step), self-tests `Source/App/SelfTestZombies.cs` and
`SelfTestZombiesMap.cs` (milestone 3).
Data: `Parts/Tables/ZombiesRules.json`, `Parts/Tables/DropTables.json` (box tiers, part drops, prices),
`Parts/Builds/*.json` (`start` / `wall` / `box` builds), `Maps/Zombies/01-BlackmoorAsylum.json` (written by
`tools/zombies/make_blackmoor.py`), `Props/Zombies/*.json` (the machine props), `Maps/Dev/ZombiesTest.json`.

## How a run plays

- **Start.** Ready up, a 5 s countdown, then round 1 comes after `firstRoundDelay` (4 s). Everyone starts in the map's
  start area with the **start builds** (today: Wall Rifle in slot 1, Wall Pistol in slot 2, the Machete on the melee
  key) and **500 points**. No loadout screen, no multiplayer perks, no grenades, no kill streaks.
- **Rounds.** Each round sends a number of zombies (below); they come out of the **active spawners** one by one (at
  most 24 alive at once, the rest wait in a queue). When every zombie of the round is dead there's a **10 s break**,
  then the next round. The HUD shows the round in red tally marks (I to IIIII, then the number) bottom-left, the round
  and what's left of it at the top.
- **Zombies** come from spawners behind **windows**: they walk to the window, tear the boards off one at a time,
  climb in, then go after the nearest player standing, through open doors. Some spawners are in the ground of an area
  (they rise out of it). A zombie swipes at you when it's close (a windup you can back out of). Zombies at a window
  swipe through it at players standing right at it.
- **Downs.** Zombie hits take health (it regenerates as usual). At 0 you're **downed**: you lie there (no walking, you
  can still shoot), lose **every perk** (`downs.losePerksOnDown`), and bleed out in 30 s unless a teammate holds the
  interact key on you for 3 s (Quick Revive halves it). Bleeding out: you're out until the **start of the next round**,
  then back in the start area with the start gear; perks and 10 percent of your points are lost
  (`downs.bleedoutPointLoss`), and your **builds drop on the floor where you went down** (see "Dropped builds").
- **Self-revive: Second Wind** (a perk machine in the start room, no power needed): when you go down with it you get
  back up on your own after `downs.selfReviveSeconds` (5 s), **solo or co-op**; the perk is used up by it, and you can
  buy it again. Downed alone without it is the game over. Quick Revive is back to World at War co-op behaviour
  (revive teammates twice as fast) and no longer self-revives solo (user decision, 2026-10-21; see below).
- **Game over** when nobody is left standing (everyone downed or bled out). The results screen says
  "Game over on round N: survived N-1 rounds"; after it a fresh run starts on the same map (no map vote in Zombies).
- **No friendly fire**; your own grenade still hurts you.

## The round curve (proposed, World at War like; `Parts/Tables/ZombiesRules.json` "rounds")

| | Formula | Solo | 4 players |
| --- | --- | --- | --- |
| Zombies in a round | `(24 + 6 x (players - 1)) x f`; f = 0.25, 0.3, 0.5, 0.7, 0.9 for rounds 1-5, then round / 5; from round 10 also x round x 0.15 | 6, 7, 12, 17, 22, 29, 34, 38, 43, 72 ... | 10, 13, 21, 29, 38 ... |
| Zombie health | 150, +60 a round to round 9 (630), then x 1.06 a round (slowed 2026-10-23, was +100 and x 1.1) | r10 668, r20 1196, r30 2142 | the same |
| Speed | walk 1.3 m/s; runners (3.2 m/s) from round 3 (+15 percent a round, max 90); sprinters (5.2 m/s) from round 5 (+10 percent a round, max 75) | | |
| Spawn delay | 2 s x 0.95 a round, at least 0.25 s | | |
| Most alive at once | 24 (performance cap, `maxAlive`) | | |

Players walk at 6 m/s, so even sprinters can be outrun; zombies swipe for 40 (3 hits from full health).

## Points (proposed; "points")

| Action | Points |
| --- | --- |
| A hit that doesn't kill (one per trigger pull per zombie) | 10 |
| A kill | 60 |
| A headshot kill | 100 |
| A melee kill | 130 |
| A board put back on a window | 10 (at most 500 a round from boards) |
| Start | 500 |

Points are **personal** (World at War style). Your own +N / -N popups show beside your points; everyone's points are
listed bottom-right.

## Buyables (all placed in the map's "zombies" section)

The prompt says what the interact key (**F** by default) does, with the price: "Hold F to buy the Wall SMG [1000]". You **hold F**
for `holdSeconds` (0.25 s) to buy; reviving and rebuilding boards go on while you hold it. Red: not enough points;
grey: not possible now (needs the power, already owned...).

| Buyable | What it does | Price (proposed) |
| --- | --- | --- |
| **Door / debris** | Opens (a door sinks into the floor, debris is cleared): the area behind it is **unlocked**, its spawners go live and zombies can route through it | per door in the map (750, 1000, 1250 on Zombies Test) |
| **Wall buy** | A chalk outline on a wall with the gun on it: a standard build tagged `"wall"` with a full magazine and reserve, into an empty slot 3-4 if you have one, else replacing the gun in your hands. Owned already: **ammo** for it | per wall buy; default `costs.wall` 500; ammo half the gun's price (`ammoCostFraction`) |
| **Mystery box** | Spins 4 s, then offers a **complete build** tagged `"box"` for 10 s (only the buyer can take it); its tier is rolled from `DropTables.json` `boxTiers` by round, then a build of that tier you don't already carry (in a gun slot or the melee slot; 59 box builds, 10-16 per tier) After 4-8 spins (rolled) the next spin is the **teddy bear**: refunded, the box flies off to another of its spots | `costs.box` 950 |
| **Perk machine** | A perk from the rules file (one machine per perk). At most 4 perks; all lost when downed | per perk |
| **Weapon-slot upgrade** | Slots 3-4 for the rest of the run (the "Mule Kick" of this game) | 4000 |
| **Power switch** | Turns the power on, once (free). Buyables marked `"power": true` don't work until then | free |
| **Workbench** | Opens the workbench over the match (the zombies don't wait) on your own slots | free |
| **Salvage pile** | Scrap, once per round per pile | +25 scrap |

### Perks (proposed; "perks", up to 8)

| Perk | Price | Effect |
| --- | --- | --- |
| Juggernog | 2500 | zombie hits do 40 percent (the health bar stays 100: 6 hits instead of 3) |
| Speed Cola | 3000 | reloads twice as fast |
| Double Tap | 2000 | fires a third faster (client and server fire-rate checks both know it) |
| Quick Revive | 1500 | revive teammates twice as fast |
| Second Wind | 1500 (every buy) | down: back up on your own after 5 s (solo or co-op); used up by it |

On Zombies Test, Quick Revive and Second Wind (both in the start room) work without power; the other three need it.
A perk with `"selfRevive": true` is a self-revive perk (any perk can be made one in the rules file). **Second Wind
(proposed)**: a fixed 1500 every time (no rising price), it counts toward the 4-perk limit (it's a perk like the
others, and the limit makes it a real choice), it's used up by the revive (and lost with the others on a down anyway).
Zombies don't attack downed players today, so a self-revive can't be "finished off" yet: being revived by a teammate
first also ends it (the perk is still used up). Quick Revive lost its 500 solo price with its solo self-revive.

### Ammo

In Zombies guns have a **reserve** (the HUD shows "magazine | reserve"): a bought gun comes with 6 magazines of
reserve, the start guns with 4. Reloads come out of the reserve; wall ammo refills it. (Every other mode keeps endless
reloads.)

## Parts, scrap and the workbench (the roguelite climb)

- **Drops.** Every zombie kill may drop a part: the chance and the part's tier by round come from `DropTables.json`
  (`dropChance`, `partDrops`); the part is one of that tier. It lies on the floor for 30 s as a small glowing box in its
  rarity colour; walk over it to pick it up (a feed line says what it was). Each kill also gives 1 scrap.
- **Inventory.** Per player, per run: loose parts (by name, with counts) and scrap. The parts on your guns are yours
  too.
- **Workbench.** F at a workbench opens the same editor as the main menu's sandbox, but it lists **only the parts you
  own this run** (loose plus those already on the build) and edits **your slots** (1-2, 3-4 with the upgrade, and the
  melee slot). APPLY sends the build to the server, which checks you're at a bench and that every added part is in
  your loose parts; parts taken off go back to your loose parts. Scrap: **break down** a loose part (its scrap value:
  common 5 ... legendary 100, wonder 150), or **craft** a part for 3x its value, up to the highest tier the drop table
  rolls this round (never wonder parts).
- **Disassemble**: take a slot's whole build apart into your loose parts (below, "Disassembling a build").
- **The workbench UI** (WEAPON-BUILDER.md, "The workbench"): pick a grip, then drag your parts onto the gun; the build
  reads as a linear list with live stats. Standard builds load as plans and a receiver assembly can be copied without
  the parts: what's missing shows red, and **CRAFT MISSING & APPLY** crafts every missing part with scrap and applies
  the build in one click (the same server checks). First-tier grips (common, uncommon) hold one receiver.

## Disassembling a build (2026-10-23)

The user asked: "In the zombie game mode, allow for the user to completely disassemble a gun into inventory items from
the workbench." Code: `Source/Game/ZombiesDisassemble.cs` (server, messages, the empty slot), `Source/UI/UiWorkbenchRun.cs`
(the button); self-test `Source/App/SelfTestZombiesDisassemble.cs` (in the "Zombies mode" section).

- **What.** At a workbench, **Disassemble [D]** (click twice, or D twice, within 4 s) takes the build in the slot being
  edited apart: **every part, the grip too**, goes to your loose parts (counts add up: a build with two Box Mags gives
  two), and the slot is **empty**. It works on any build: guns in slots 1-4 (wall-buy and box builds too, they're builds
  like any other) and the **melee slot** (hilt, head and mods). One **Undo** puts the same build back from your loose
  parts while the bench stays open (an ordinary Apply, checked again; it's gone once you close the bench or the slot gets
  something else). Breaking loose parts down for scrap is still its own action.
- **Checks (server).** You must be standing at a workbench, alive and not downed (as for every bench action), the slot
  must be yours (3-4 only after the slot upgrade) and hold a build; a stock gun (no build) can't be taken apart. Parts are
  only moved, never made or lost: loose plus on builds stays the same (self-test).
- **The empty slot.** An empty slot 1-2 holds **Empty hands** (no model in first or third person, no magazine; the
  trigger only clicks and the server refuses its shots); an empty slot 3-4 is free, as before it was filled. Switching
  (1-4, the swap key, the wheel) skips empty slots, the HUD lists them as "(empty)". If the gun in your hands is taken
  apart you switch to your first slot with a gun, or, with **no gun left** (allowed), stand empty-handed: the melee key
  still swings your melee build, or the standard strike if that was taken apart too. New guns go to an **empty slot
  first** (slots 1-2, then 3-4): wall buys, the mystery box and dropped builds you pick up. An empty slot keeps its
  reserve count for the next build put there at the bench (like any bench edit); bleeding out brings the start gear back
  as usual, and empty slots drop nothing.
- **Network (protocol 41).** The client asks with `ZBench` kind 3 (the slot, no text). The server checks it, moves the
  parts, and sends `ZRun` (your loose parts) to you and a `ZBuild` with flag 4 ("cleared", no text; the weapon id of the
  Empty hands copy for slots 1-2) to everyone, so every PC shows the slot empty and the hands move the same way. Joiners
  get a cleared `ZBuild` for every empty slot 1-2 after the builds. An older build can't join (protocol 40).
- **The main menu's sandbox** has no Disassemble: its stock is unlimited (or the fixed practice stock), so there's no
  inventory to put parts back into; "New weapon" already starts from an empty grip.

## Map format: the "zombies" section

```json
"zombies": {
  "start": "Start Room",
  "areas": [ {"name": "Start Room", "rect": [x0, z0, x1, z1]}, ... ],
  "doors": [ {"name": "the Hall Door", "kind": "door" | "debris", "cost": 750, "power": false,
              "box": [x0, y0, z0, x1, y1, z1], "areas": ["Start Room", "Hall"]} ],
  "windows": [ {"area": "Start Room", "box": [x0, y0, z0, x1, y1, z1], "outside": [x, z], "inside": [x, z], "boards": 6} ],
  "spawners": [ {"window": 0, "at": [x, z]}, {"area": "West Wing", "at": [x, z]} ],
  "wallBuys": [ {"build": "Wall SMG", "at": [x, z], "yaw": -90, "cost": 1000, "ammoCost": 500} ],
  "box": {"cost": 950, "start": 0, "spots": [ {"at": [x, z], "yaw": 0}, ... ]},
  "workbenches": [ {"at": [x, z], "yaw": 0} ],
  "perks": [ {"perk": "jugg", "at": [x, z], "yaw": 90, "power": true} ],
  "slotUpgrade": {"at": [x, z], "yaw": -90, "cost": 4000},
  "power": {"at": [x, z], "yaw": -90},
  "salvage": [ {"at": [x, z], "cost": 25} ],
  "machineProps": true
}
```

- **Machine props** (`"machineProps": true`, milestone 3; open question 9): every machine stands as a **solid prop** from
  `Props/Zombies` (players, zombies, bullets and the bots' nav graph treat it like any solid prop): **Perk Machine** (the
  game paints the perk's colour and lit sign on its front), **Mystery Box Crate** (one at every box spot; the box itself
  is drawn a little bigger over the crate at the spot it's in), **Zombies Workbench**, **Power Switch Box** (the lever
  and lamp are drawn on it) and **Weapon Locker** (the slot upgrade; its screen is drawn on it). The section places them
  itself (they're not ops), facing the machine's `yaw`, so put each machine where its prop fits: a perk machine is
  0.9 x 0.7 m, a box crate 1.3 x 0.66, a workbench 1.6 x 0.8, the locker 1.0 x 0.6 and the power box 0.7 x 0.3 (place
  it 0.2 m off a wall). They count towards the map's prop fingerprint. Without the key (Zombies Test) machines are drawn
  without collision, as before.

- **Areas** are rectangles; the **area graph** is made by the doors: an area is unlocked when it's the start area or
  one of its doors is open. Zombies route between areas through open doors (a breadth-first search over the doors),
  and inside an area along the bots' nav graph (or straight when the player is in plain view).
- **Doors** are solid boxes (players, zombies, bullets) until bought; opening one parks its box under the world like a
  broken prop (the map fingerprint doesn't change). Each side's standing point is worked out across the door's thin
  axis. Build the wall gap with a hole in a `WallX` / `WallZ` op and put the door box in it.
- **Windows**: the `box` is the opening (a hole in a wall op with a sill). It's on its own collision layer (**Clip**):
  it blocks bodies (players can't climb out, zombies can't walk in, the nav graph doesn't cross it) but **not
  bullets**, so you shoot zombies at the window. `outside` is where zombies stand to tear boards, `inside` (in the
  area) where they land and where players stand to rebuild.
- **Spawners** behind a window name it (they belong to its area); others name an area and rise out of the ground.
- Points are `[x, z]` (on the ground; wall buys 1.3 m up) or `[x, y, z]`. `yaw` is degrees: the way the thing faces
  (0 = -Z, 90 = +X).
- Unknown keys, bad references and missing spawners in the start area are refused with the reason.
- Builds named by wall buys must be standard builds tagged `"wall"`; perks must be keys from the rules file.

The dev map **Zombies Test** (48 x 48 m): a start room (two windows, a wall SMG, the box's first spot, a workbench,
Quick Revive), the **Hall** behind a door (window, wall pump, Juggernog, a box spot, salvage), the **West Wing**
behind debris (window, a ground spawner, the wall rifle for ammo, Speed Cola, a box spot) and the **Power Room**
behind a door (window, the power switch, Double Tap, the slot upgrade, a workbench, salvage, the Burst Marksman), plus
a shutter between the Hall and the Power Room that needs the power.

## Blackmoor Asylum (milestone 3, the Zombies pack)

`Maps/Zombies/01-BlackmoorAsylum.json`, written by `tools/zombies/make_blackmoor.py` (the script also checks its own
layout: props keep clear of doors, windows, spawners, machines, wall buys and each other). An abandoned village asylum
and its research wing at night, about 68 x 62 m inside a walled, wobbly polygon outline, with terrain (rough ground
round the ring, hills beyond the wall, a mound in the courtyard), grass and a few trees, a night sky with moonlight and
the ambient raised so it stays readable, flickering caged lamps, tube lights and lanterns, grime, damp and blood decals.

| Area | Way in (price) | What's there |
| --- | --- | --- |
| **Receiving Hall** (start) | | 3 windows onto the ambulance bay, Quick Revive and Second Wind (no power), the Wall Rifle, box spot 1, a reception desk |
| **East Ward** | the Ward Door (750) | 2 windows, Juggernog (power), the Wall SMG, salvage, hospital beds |
| **West Offices** | the Records Debris (750) | 2 windows, Speed Cola (power), the Wall Pistol, box spot 2, **workbench 1**, desks and filing |
| **Courtyard** (open sky) | the Courtyard Gate from the ward (1000) or the Garden Debris from the offices (1250) | 3 boarded gates, 2 ground spawners among the graves, the Wall Pump, box spot 3, salvage, a fountain |
| **Generator House** | the Generator Door from the courtyard (1250) | 2 windows, **the power switch**, **workbench 2**, the Burst Marksman, salvage, generators |
| **Chapel** | the Chapel Door from the offices (1500) | 2 windows, a ground spawner, Double Tap (power), the Wall SMG, box spot 4, pews and an altar |
| **Research Lab** | **the Lab Shutter** from the ward (750, **needs the power**) | 3 windows, a ground spawner, the **weapon-slot upgrade**, the Wall Pump, box spot 5, salvage, consoles |

Seven areas (six to unlock), 17 boarded windows, 21 spawners (4 in the ground), 7 wall buys, 5 box spots, 5 perk
machines (3 on the power), every machine a solid prop. Two routes to the courtyard make a loop through the middle of
the map (ward - courtyard - offices) for training zombies. The self-test walks a zombie in through every window and
runs zombies from every area to the far end through every door (none may get stuck), and measures the AI cost there.

## Dropped builds (user decision, 2026-10-21)

The user's answer to "should bleeding out stay this harsh?": "instead of losing your builds, they become pickups where
your player has died. And if you can't get to them in time, will despawn after 5 minutes."

- **What drops.** Bleeding out drops every built weapon in your slots (slots 1-4 and the melee build) at the spot you
  went down. Untouched **start builds** don't drop (you get them back with the start gear anyway); a start build you
  changed at the workbench does. Perks and 10 percent of your points are still lost (both data values: `downs`).
- **Where.** Spread on rings of 0.7, 0.35 and 1.05 m around the spot, at least 0.45 m apart, each one where a player
  could stand: not in a wall or a prop, inside the map and the area you went down in (never through a window or a shut
  door), on the ground at about the same height, in straight walking reach of the spot. Short of room they share the
  spot itself. Deterministic.
- **How long.** `downs.buildDropSeconds` (300 s = 5 minutes) from the drop, then gone. The last
  `downs.buildDropWarnSeconds` (30 s) the pickup and its label blink red.
- **Looks.** The build's own model lying on its side, slowly turning and bobbing, over a ring of glowing dashes (gold
  for guns, blue for melee builds, red when about to despawn) with a faint beam. Within 8 m the HUD lists each one:
  "Tester's Minigun: 4:31 left".
- **Who can pick up: anyone on the team** (proposed), so teammates can rescue your gear (and hand it back, or keep it).
  No points refund to the owner. The owner gets a feed line when someone else takes their build.
- **Picking up**: **hold F** (like a buy, `buy.holdSeconds`): a gun goes into a free slot 3-4 if you have one, else
  it **swaps with the gun in your hands**; a melee build goes to the **melee slot**, swapping out the melee build there.
  A swapped-out build **drops in the same spot with the picked one's remaining time** (not a fresh 5 minutes: swapping
  back and forth can't keep builds on the floor forever). Bleed-out drops come with a full magazine and reserve; a
  build left by a swap comes back with a magazine and the reserve the slot had (no free ammo by swapping).
- At most 40 dropped builds at once (the oldest goes). Parts on a dropped build travel with it (the workbench treats
  them as the new holder's, like any build in their slots).
- `downs.dropBuildsOnBleedout: false` brings back the old rule (builds lost).

## Power-ups (milestone 3, World at War style; proposed numbers in "powerUps")

- **Drops.** Every zombie a player kills may drop one: `dropChance` 3 percent, at most `maxPerRound` (4) a round (the
  count resets when a round starts). The kind is rolled from `weights` (Max Ammo 1.2, Insta-Kill 1, Double Points 1,
  Nuke 0.8, Carpenter 0.8); the Carpenter only drops while a board is down. A zombie killed outside (at a window) drops
  it at that window's inside spot. Kills by a burn, a splash or a chain drop like any kill; the Nuke's kills drop nothing.
- **Looks.** It floats 0.9 m up, turning and bobbing, a glowing token for its kind (an ammo can, a skull, two gold coins,
  a bomb, a hammer) over a green halo with a faint beam; its name floats over it within 12 m. It lasts `lifetime` (30 s)
  and blinks for the last `blinkSeconds` (10 s), faster in the last 3.
- **Taking one:** the first player standing who walks within `pickupRange` (1.3 m) takes it **for the whole team**; a
  feed line ("Tester grabbed Insta-Kill: every hit kills for 30 seconds!") and a banner ("INSTA-KILL!") go to everyone.

| Power-up | What it does |
| --- | --- |
| **Max Ammo** | Every player's guns get a full reserve (magazines stay as they are) |
| **Insta-Kill** | For `duration` (30 s) every hit on a zombie kills it |
| **Double Points** | For 30 s every point earned (hits, kills, boards) counts twice |
| **Nuke** | Every zombie alive dies at once (no kill points or drops); every player gets `nukePoints` (400) |
| **Carpenter** | Every window's boards go back up; every player gets `carpenterPoints` (200) |

The HUD shows Insta-Kill and Double Points with their seconds left, bottom centre (blinking in the last 5 s).

## Special ammo effects (milestone 3; proposed numbers in "ammoEffects")

The flags parts carry come alive against zombies (open question 5). A build's flags are a **set**: two incendiary
receivers are still one "incendiary". Each effect goes off **at most once per trigger pull or swing**: effects on the
zombie hit once per zombie hit (a shotgun's pellets on one zombie are one hit), area effects once per shot, from the
zombie that took the most damage. A burn and a bleed share one slot (the stronger ticks; a new one restarts the clock,
never adds). The effects' own damage never sets off more effects, pays points like any hit (burn and bleed ticks pay
only the kill), and works with Insta-Kill and Double Points.

| Flag (parts) | Effect on zombies |
| --- | --- |
| `incendiary` (Dragon's Breath) | Burn: 60 percent of the hit (at least 40) over 3 s, ticking 4 times a second; the zombie glows with flames |
| `explosive` (Explosive Tip, 40mm Frag) | Splash: the zombies within 2.2 m of the one hit take half the hit, less further out; walls shield |
| `shock` (Shock Coil) | Chain: arcs to the 3 nearest other zombies within 3.5 m (not through walls) for 40 percent, stunning them 0.4 s |
| `penetrating` (.50 BMG) | Pierce: goes on along the ray through the next 2 zombies within 8 m behind (until a wall), at 60 percent |
| `plasma` (Plasma Charge) | Slow: half speed for 2 s, crackling blue |
| `bouncy` (Rubber Duck Rounds) | Bounce: on to the nearest other zombie within 4 m, at half |
| `squeaky` (Rubber Duck Rounds, Whoopee Cushion) | A squeak and a 0.3 s stun |
| `toasty` (Toaster Feed) | A half-strength burn, and the toaster **pops the zombie up** off the floor |
| `nails` (Nail Ring), `chainsaw` | Bleed: 30 percent of the hit over 3 s (the chainsaw also stuns 0.3 s) |
| `bonk` (Frying Pan) | A 1 s stun with a cartoon bonk |
| `fish` (Frozen Fish) | A wet slap that **knocks the zombie back** (4.5 m/s) |
| `rocket` (Rocket Booster) | Knocks the zombie flying (6 m/s) |
| `party` (Helium Balloons) | A zombie it kills **floats away** on its balloons |
| `refreshing` (Cup Holder) | Every kill gives the shooter 5 health back |
| `bayonet` (Bayonet) | Hits within 2.2 m do 1.5x damage |
| `suppressed`, `cart` | No effect on zombies (they already do their own thing) |

The self-test fails if a part ever carries a flag that's in neither list. Effects players see: a short flash where an
effect goes off (an orange burst for a splash, blue sparks for a chain, pink for party balloons...) and its sound;
burning and zapped zombies glow.

## Network (protocol 19; protocol 40 for milestone 3; 41 for disassembly)

Protocol 41: `ZBench` kind 3 takes a slot's build apart; `ZBuild` flag 4 = the slot is now empty (no text), also sent to
joiners for empty slots 1-2 (see "Disassembling a build").

Protocol 40 (milestone 3): `ZWorld` ends with the floating power-ups (id, kind, position, tenths of a second left) and
Insta-Kill / Double Points' seconds left; `ZEvent` gains `MaxAmmo` (refill your reserves) and `Fx` (a special ammo
effect: kind, sound and a position after the text; unreliable); bits 6-7 of a zombie's state byte in `ZEnts` say
burning / zapped. (38 and 39 are other features on main.)


New messages at the end of `Msg`: `ZUse` (client: interact key down / up), `ZEnts` (every zombie with each snapshot:
id, quantised position, yaw, state, speed, health; 12 bytes each, about 300 bytes for 24), `ZWorld` (10 Hz: round,
phase, doors, boards, box, every player's points / downs / perks / bleed and revive progress, parts on the floor),
`ZEvent` (feed lines, ammo refills, banners), `ZRun` (your loose parts and scrap), `ZBuild` (a build went into a
player's slot: **the build's text**, which every PC assembles into the same weapon deterministically) and `ZBench`
(client workbench changes). **Built weapons now work in co-op**: clients get their own builds (and see everyone else's
guns) from `ZBuild`, fire them with the same stats as the server, and the server validates them as before. Clients
interpolate zombies on the same clock as players; shots against zombies are lag compensated (32 ticks of history).
Joiners get every player's builds when they connect. Game mode 5 is Zombies.
Protocol 19 adds `ZPickup` at the end of `Msg` (reliable): a dropped build appeared (id, exact position, seconds left,
owner id and name, full-ammo / melee flags, the build's text) or went (picked up by player X, or despawned); joiners
get every dropped build on the floor when they connect. Clients keep the time left on their own clock.

## AI cost

Zombies move with the shared player physics (the same mover as players), pick targets twice a second, and share at
most 2 nav graph searches per tick (the rest steer straight until their turn). The self-test measures the cost with
24 zombies chasing inside the start room (see the report line "AI cost").

## Controls

F (Interact): hold to buy, revive, rebuild boards, salvage; press at a workbench to open it (Esc / Close goes back).
At the workbench, D twice (or Disassemble clicked twice) takes the slot's build apart.
Melee (V): the melee slot's build (the Machete to start). Weapon slots 1-4 as before (3-4 after the upgrade).

## Open questions (for the user)

1. **Numbers**: the round curve, points, prices, perks, reserve ammo and the drop chances are all proposed.
2. ~~Bleeding out~~: **decided**: builds drop as pickups for 5 minutes (see "Dropped builds"). Still open: should
   anyone be able to pick up your builds (today), or only you; and a points refund for the owner?
3. ~~Solo self-revive~~: **decided**: Second Wind, a perk in the start room (solo and co-op). Still open: its price
   (fixed 1500, or rising each buy), whether it should count toward the perk limit (today it does), and whether solo
   Quick Revive should be cheaper again.
4. ~~Power-ups~~: **done in milestone 3** (all five, World at War style; see "Power-ups"). Still open: the numbers
   (3 percent a kill, 4 a round, 30 s), and whether drops should follow World at War's points-based schedule instead.
5. ~~Special part flags~~: **done in milestone 3** (every flag on a part; see "Special ammo effects"). Still open: the
   numbers, and whether effects should also hurt players in other modes.
6. **Co-op bots** hold the start area, shoot, revive and rebuild boards, but never buy anything (still the default).
   Should they buy doors / guns / perks with their own points?
7. **Downed**: shoot with whatever is in your hands (still the default), or a forced pistol (World at War's last stand)?
8. **Melee builds from the box** go to the melee slot (still the default): or should the box give guns only?
9. ~~Machines have no collision~~: **done in milestone 3** on real maps (`"machineProps": true`, solid props from
   `Props/Zombies`); Zombies Test keeps them collision-free.
10. **Second Wind's price** (still a fixed 1500, counting toward the 4 perks; see question 3).
11. **Blackmoor Asylum**: the layout and prices (750 / 750 / 1000 / 1250 / 1250 / 1500, the power shutter 750) are
    proposed; more Zombies maps next?
