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
workbench), `ZombiesDrops.cs` (dropped builds), `ZombiesPowerUps.cs` (power-ups), `ZombiesSlots.cs` (slot machines), `ZombiesAmmoEffects.cs` (special ammo), `ZombieAI.cs` (spawning, AI, hit tests), `ZombiesNet.cs` (messages, client mirror),
`ZombieAnimNet.cs` (animation clocks and sync), `Source/Render/ZombieAnim.cs` (the zombie poses, see "Animation"), `ZombieBots.cs` (co-op
bots, downed controls), `Source/Maps/MapZombies.cs` (the map section), `Source/Zombies/ZombiesRules.cs` (the rules
file), `Source/UI/UiZombies.cs` (HUD), `Source/UI/UiWorkbenchRun.cs` (the workbench in a run),
`Source/App/Program.Zombies.cs` (drawing, input, smoke step), self-tests `Source/App/SelfTestZombies.cs`,
`SelfTestZombiesMap.cs` (milestone 3) and `SelfTestZombieAnim.cs` (animation).
Data: `Parts/Tables/ZombiesRules.json`, `Parts/Tables/DropTables.json` (box tiers, part drops, prices),
`Parts/Builds/*.json` (`start` / `wall` / `box` builds), `Maps/Zombies/01-BlackmoorAsylum.json` (written by
`tools/zombies/make_blackmoor.py`), `Props/Zombies/*.json` (the machine props), `Maps/Dev/ZombiesTest.json`.

## How a run plays

- **Start.** Ready up, a 5 s countdown, then round 1 comes after `firstRoundDelay` (4 s). Everyone starts in the map's
  start area with the **start builds** (today: Wall Rifle in slot 1, Wall Pistol in slot 2, the Machete on the melee
  key) and **500 points**. No loadout screen, no multiplayer perks, no grenades, no kill streaks.
- **Rounds.** Each round sends a number of zombies (below); they come out of the **active spawners** one by one (at
  most 24 alive at once, the rest wait in a queue). When every zombie of the round is dead there's a **break**: the
  next round waits until everyone alive is **ready** (see "Between rounds"; a plain 10 s break in a bots-only run). The HUD shows the round in red tally marks (I to IIIII, then the number) bottom-left, the round
  and what's left of it at the top.
- **Zombies** come from spawners behind **windows**: they walk to the window, tear the boards off one at a time,
  climb in, then go after the nearest player standing, through open doors. Some spawners are in the ground of an area
  (they climb out of it, arms first, in `zombie.riseSeconds`: 1.4 s, 0.9 s before the animations). A zombie swipes at you when it's close (a windup you can back out of). Zombies at a window
  swipe through it at players standing right at it.
- **Downs.** Zombie hits take health (it regenerates as usual). At 0 you're **downed**: you lie on the floor and
  **crawl** (see "Between rounds, downs and spectating"; you can still shoot), lose **every perk**
  (`downs.losePerksOnDown`), and bleed out in 30 s unless a teammate holds the interact key on you for 3 s (Quick Revive
  halves it). Bleeding out: you **spectate** a living teammate and are out until the **end of the round** (its last
  zombie dies), then back in the start area with the start gear; perks and 10 percent of your points are lost
  (`downs.bleedoutPointLoss`), and your **builds drop on the floor where you went down** (see "Dropped builds").
- **Self-revive: Second Wind** (a perk machine in the start room, no power needed): when you go down with it you get
  back up on your own after `downs.selfReviveSeconds` (5 s), **solo or co-op**; the perk is used up by it, and you can
  buy it again. Downed alone without it is the game over. Quick Revive is back to World at War co-op behaviour
  (revive teammates twice as fast) and no longer self-revives solo (user decision, 2026-10-21; see below).
- **Game over** when nobody is left standing (everyone downed or bled out). The results screen says
  "Game over on round N: survived N-1 rounds"; after it a fresh run starts on the same map (no map vote in Zombies).
- **No friendly fire**; your own grenade still hurts you.

## Between rounds, downs and spectating (user requests, 2026-10-23)

The user asked for four changes: crawl while downed, spectate living players when dead, ready-up between rounds
instead of the 10 s interval, and the dead back at the end of a round. Code: `Source/Game/ZombiesFlow.cs` (ready-up,
the respawn, the spectator helpers), `ZombieBots.cs` (`ZombieInput`: the crawl; `ZombieBotCrawl`),
`Source/App/Program.Zombies.cs` (the spectator camera and keys), `Source/UI/UiZombies.cs` (HUD lines); self-test
section **"Zombies flow"** (`Source/App/SelfTestZombiesFlow.cs`).

- **Crawling.** Downed, the move keys still work, at `downs.crawlSpeed` (0.8) x the prone speed: about 1.06 m/s
  (prone is 0.22 x 6 m/s). Turning and aiming as usual, and you shoot with the gun in your hands. No sprint, jump (so
  no mantling), crouch or standing up; no doors, buying, workbench, pickups or reviving (the interact key does nothing).
  The camera is the prone eye (0.45 m). The server runs the same input filter as the client's prediction, so it's
  server-authoritative and predicted like any movement; the downed flag already went over the wire (`ZWorld`), so the
  crawl itself needs no new fields. **Revives**: the reviver must stay within `downs.reviveRange` (1.8 m) of you;
  crawling out of reach interrupts the revive (its bar resets; it starts again from nothing once you're back in reach,
  as letting go of the key always did). The bleed-out clock runs on while you crawl; bleeding out drops your builds
  where you are then. Zombies still ignore downed players (as before: they go for whoever is standing).
- **Downed bots crawl** (decision: crawl, not stay put): away from a chasing zombie within 4 m, else straight towards
  the nearest teammate standing until well inside revive reach (1.1 m), else they lie still. Walls and window openings
  stop them like anyone. They don't shoot while down (as before).
- **Spectating.** After bleeding out the death cam plays (or a click skips it), then you watch a **living** teammate
  (downed ones count: they're alive) from **over their shoulder** (2.1 m back, 0.35 m up, 0.45 m right, pulled in short
  of walls; the death cam was a third-person camera already, and first person would need the other player's arms and
  gun drawn from your PC). **Spectate next / previous** (new keys: **Right / Left arrow**, rebindable; **Fire / Aim**
  work too while you're out) cycle through the living by player id, wrapping round; if the one you watch dies you move
  on to the next. The HUD says "Spectating Name - [Mouse Left] or [Right] next / [Mouse Right] or [Left] previous". It
  ends when you're back. If everyone is down or out the run ends as before. Client-side only (no wire change).
- **Ready-up between rounds.** When a round's last zombie dies the break **waits**: every living person presses the
  **Ready key** (the existing **F3**, "Ready up (warmup; Zombies: the next round)"; pressing it again takes it back).
  Bots count as ready; the dead (and a host sitting out, in the self-tests) don't block. When everyone is ready the
  next round starts `rounds.readyGoSeconds` (3 s) later, with its banner and sound as before ("ROUND 4", the whistle).
  The HUD shows "Press [F3] when ready - 1/2 ready" (centre) and "Break: 1/2 ready" in the match bar; the scoreboard
  (Tab) tags each player [READY] / [not ready]. **Safety valve**: `rounds.readyFallbackSeconds` (300 s = 5 minutes,
  0 = off) after the round ended it starts anyway (the bar counts it down); the **host** can force it: once ready, press
  F3 again ("host: press [F3] again to start now"). Walking about, buying and the workbench all work during the break.
  `rounds.readyUp: false` brings back the plain `breakSeconds` timer, which bots-only runs (nobody to ready up) still
  use, so the bots' digests didn't change.
- **Back at the round's end.** Bleeding out during a round, you're back the moment its last zombie dies (before the
  break), in the start area with the start gear (the usual respawn rules: an untouched start kit, 2 slots, slots 3-4
  gone with the upgrade, your dropped builds still on the floor for their 5 minutes). Bleeding out **during the break**
  (downed at the round's end and nobody revived you) you're back when the next round starts (the old rule).
- **Network (protocol 47).** `ZWorld`'s player flags gain bit 3 (ready) and bit 4 (counts for ready-up), and the
  message ends with one byte (bit 0: the break is waiting for ready-up); `ZPhaseLeft` is then the fallback's seconds
  left. `Msg.Ready` during a Zombies match means "ready for the next round". The crawl and spectating add nothing.

| Rules (`Parts/Tables/ZombiesRules.json`) | Default | What |
| --- | --- | --- |
| `rounds.readyUp` | true | ready-up between rounds (false: the `breakSeconds` timer) |
| `rounds.readyFallbackSeconds` | 300 | start anyway this long after the round ended (0 = never; the host can still force it) |
| `rounds.readyGoSeconds` | 3 | the countdown once everyone is ready |
| `rounds.breakSeconds` | 10 | the break when ready-up is off or nobody can ready up (bots only) |
| `downs.crawlSpeed` | 0.8 | crawl speed, as a fraction of the prone speed |

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
| **Perk machine** | A perk from the rules file (one machine per perk). No cap: beyond the 4th each perk costs double the step before (the 5th 2x its price, the 6th 4x...); all lost when downed | per perk |
| **Weapon-slot upgrade** | Slots 3-4 for the rest of the run (the "Mule Kick" of this game) | 4000 |
| **Power switch** | Turns the power on, once (free). Buyables marked `"power": true` don't work until then | free |
| **Workbench** | Opens the workbench over the match (the zombies don't wait) on your own slots | free |
| **Salvage pile** | Scrap, once per round per pile | +25 scrap |
| **Slot machine** | Three reels spin 2 s and pay a loose part, scrap, points back or nothing (see "Slot machines") | 500 + 25 a round after the first, at most 1250 |

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
(decided 2026-10-26)**: a fixed 1500 every time (no rising price of its own), it counts as a perk for the doubling
below (it's a perk like the others), it's used up by the revive (and lost with the others on a down anyway).
Zombies don't attack downed players today, so a self-revive can't be "finished off" yet: being revived by a teammate
first also ends it (the perk is still used up). Quick Revive lost its 500 solo price with its solo self-revive.

**More than 4 perks (developer answer, 2026-10-26).** There's no hard limit any more: holding 4 perks, the 5th costs
2x its listed price, the 6th 4x, the 7th 8x (each step doubles), for every perk including Second Wind (so a 5th-perk
Second Wind is 3000). The prompt shows the real price and the step ("Hold F to buy Second Wind [3000] (5th perk:
x2)"). Rules: `buy.fullPricePerks` (4; 0 = never dearer), `buy.extraPerkFactor` (2), `buy.maxPerks` (now 8, every
perk: a hard cap if a map ever wants one). Co-op bots pay the same prices. Self-test: "Zombies mode" ("perk price").

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

- **What.** At a workbench, **Disassemble slot N** (top button row; key D; click twice, or D twice, within 4 s) takes the build in the slot being
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
- **At the mystery box (board item "Zombies", 2026-10-26).** "Disassemble weapons directly at the mystery box into
  parts, not only at a workbench." Standing at the box (in reach of its prompt), a second HUD line under the box's
  prompt says "Press [G] twice to take the Wall Rifle apart into parts (free)": **G** (the grenade key, rebindable as
  "Throw grenade (Zombies: take apart at the box)"; there are no grenades in Zombies) **twice within 4 s** takes the
  **gun in your hands** apart; the first press arms it (the line turns orange and says how many parts). It's the
  **same server action** as the workbench's (`ZBench` kind 3): the server accepts it at a workbench or within 2.4 m of
  the box's current spot, so it gives exactly the same parts, the same empty slot and the same checks (self-test: the
  same build at the box and at a bench gives the same parts). The other bench actions (salvage, craft, Apply) still
  need a bench. Decisions (proposed, easy to change): **free** (no points, no scrap: you only move parts you own); it
  works while the box is **idle, spinning or offering** (it's your own gun; taking apart the gun in your hands just
  before taking the offered build puts the build into the emptied slot), but **not while the teddy bear has it** (the
  box is flying off); the teddy bear has no other role; the melee slot is taken apart at a bench only (the box key
  works on what's in your hands). No wire change (the request is the same message; only where the server accepts it).
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
  "slots": [ {"at": [x, z], "yaw": 180, "power": false, "cost": 0} ],
  "machineProps": true
}
```

- **Machine props** (`"machineProps": true`, milestone 3; open question 9): every machine stands as a **solid prop** from
  `Props/Zombies` (players, zombies, bullets and the bots' nav graph treat it like any solid prop): **Perk Machine** (the
  game paints the perk's colour and lit sign on its front), **Mystery Box Crate** (one at every box spot; the box itself
  is drawn a little bigger over the crate at the spot it's in), **Zombies Workbench**, **Power Switch Box** (the lever
  and lamp are drawn on it) and **Weapon Locker** (the slot upgrade; its screen is drawn on it) and **Reel Cabinet** (a slot machine; its reels, heat
  meter and topper lights are drawn on it). The section places them
  itself (they're not ops), facing the machine's `yaw`, so put each machine where its prop fits: a perk machine is
  0.9 x 0.7 m, a box crate 1.3 x 0.66, a workbench 1.6 x 0.8, the locker 1.0 x 0.6, the reel cabinet 0.9 x 0.7 and the power box 0.7 x 0.3
  (place it 0.2 m off a wall). They count towards the map's prop fingerprint. Without the key (Zombies Test) machines are drawn
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
| **Receiving Hall** (start) | | 3 windows onto the ambulance bay, Quick Revive and Second Wind (no power), the Wall Rifle, box spot 1, **slot machine 1**, a reception desk |
| **East Ward** | the Ward Door (750) | 2 windows, Juggernog (power), the Wall SMG, salvage, hospital beds |
| **West Offices** | the Records Debris (750) | 2 windows, Speed Cola (power), the Wall Pistol, box spot 2, **workbench 1**, desks and filing |
| **Courtyard** (open sky) | the Courtyard Gate from the ward (1000) or the Garden Debris from the offices (1250) | 3 boarded gates, 2 ground spawners among the graves, the Wall Pump, box spot 3, salvage, a fountain |
| **Generator House** | the Generator Door from the courtyard (1250) | 2 windows, **the power switch**, **workbench 2**, the Burst Marksman, salvage, generators |
| **Chapel** | the Chapel Door from the offices (1500) | 2 windows, a ground spawner, Double Tap (power), the Wall SMG, box spot 4, pews and an altar |
| **Research Lab** | **the Lab Shutter** from the ward (750, **needs the power**) | 3 windows, a ground spawner, the **weapon-slot upgrade**, **slot machine 2** (needs the power), the Wall Pump, box spot 5, salvage, consoles |

Seven areas (six to unlock), 17 boarded windows, 21 spawners (4 in the ground), 7 wall buys, 5 box spots, 5 perk
machines (3 on the power), 2 slot machines (1 on the power), every machine a solid prop. Two routes to the courtyard make a loop through the middle of
the map (ward - courtyard - offices) for training zombies. The self-test walks a zombie in through every window and
runs zombies from every area to the far end through every door (none may get stuck), and measures the AI cost there.

## Slot machines (board item "Zombies slots", 2026-10-26)

"Slot machines: gamble points for weapon-builder parts, with rarity rising as the rounds go on (a per-round odds table,
a machine 'heat' pity timer, and jackpot rounds every 5th round)." The mystery box gives complete builds only (a user
decision), so the slots are where a run gambles for **single parts**. Code: `Source/Game/ZombiesSlots.cs` (server,
odds, reels, network), `Source/App/Program.ZombiesSlots.cs` (drawing); prop `Props/Zombies/ReelCabinet.json`;
self-test section **"Zombies slots"** (`Source/App/SelfTestZombiesSlots.cs`). Every number is data
(`ZombiesRules.json` "slots", `DropTables.json` "slotTiers").

- **A pull.** Walk up (a machine's prompt: "Hold [F] to pull the slot machine [550] - heat 2/5"), hold F like any buy
  and pay **500 + 25 a round after the first** (at most 1250; a machine's own `cost` overrides it). The three reels spin
  for 2 s and stop one by one (a click each) on what the server rolled; the result shows for 2.5 s (the reels flash, a
  part won floats over the cabinet), then the machine takes the next pull. One player at a time per machine.
- **What the reels mean.** Symbols: a **skull**, a **cog** (scrap), a **coin** (points back) and a **gem** per rarity
  in its colour (the wonder gem only on jackpot rounds). **Three of a gem**: a loose part of that rarity; **two of a
  gem**: a part one tier lower (two commons: scrap); otherwise a cog pays **40 scrap**, a coin **1.5 x the price back**
  in points, and skulls and odd gems pay **nothing**. The server rolls the outcome first and then picks reels that read
  as it (self-test: they always do), so the table, not the reels, sets the odds.
- **Parts** go into your **loose parts** (as salvage drops do: use them at the workbench; never a standard build, never
  a part straight onto a gun) from the same pool as zombie part drops. A part you already hold **3 or more** of loose is
  **auto-scrapped** into its scrap value instead (`autoScrapAt`). A legendary or wonder part is announced to everyone.
- **Rarity rising: three levers that stack.**
  1. **By round** (`DropTables.json` "slotTiers", rows from round 1, 5, 10, 15, 25, 35): weights for nothing, scrap,
     points and each part tier; the share of blanks falls (30 -> 20 percent) and the part mix climbs (round 1: mostly
     common and uncommon, 2 percent rare or better; round 36: about half rare or better).
  2. **Machine heat** (the pity timer): every pull that doesn't pay a **rare-or-better** part (`pityTier`) adds 1 heat;
     every **5** heat (`heatStep`) moves the part weights **one tier up** (at most 3 steps, `heatMaxSteps`); a
     rare-or-better part resets it to 0. Heat belongs to the machine (shared by the team, so a cold machine is a choice).
     The cabinet shows it: a 5-segment meter under the reels fills towards the next step (yellow, orange, red; flashing
     at the top), and the topper's lights chase faster as it warms up.
  3. **Jackpot rounds** (every 5th round, `jackpotEvery`): the **wonder gem** goes on the reels and legendary (+6) and
     wonder (+4) weights are added; the topper flashes gold and pink and the prompt says "JACKPOT ROUND". Outside
     jackpot rounds wonder parts never come up from the slots.
- **Bad-luck protection**: at most **3** "nothing" results in a row per player per machine (`maxNothing`); the next
  would-be blank pays scrap instead.
- **Placement**: Blackmoor Asylum has two (the Receiving Hall, against the east wall by the Ward Door, and the Research
  Lab, which needs the power); Zombies Test two (the start room, and the Power Room on the power). Map key `"slots"`
  (spots like a perk machine's: `at`, `yaw`, `power`, `cost`), at most 8; with machine props each stands as a solid
  **Reel Cabinet** (0.9 x 0.7 m, 1.95 m tall, its front facing the yaw).
- **Network (protocol 51).** `ZWorld` ends with the machines: a count, then 11 bytes each (state, heat, owner, a spin
  counter, the three symbols, result / tier / auto-scrapped, the part, tenths of a second left). Clients start the reel
  animation when the spin counter changes (worked back from the seconds left, so a joiner mid-spin lines up); the parts
  and scrap come in `ZRun` as for any payout. Bots don't use the machines. Nothing in the machines' tick allocates
  (self-test).

| Rules (`ZombiesRules.json` "slots") | Default | What |
| --- | --- | --- |
| `costBase`, `costPerRound`, `costMax` | 500, 25, 1250 | the price: base + per round after the first, capped |
| `spinSeconds`, `showSeconds` | 2, 2.5 | the spin, and how long the result shows |
| `heatStep`, `heatMaxSteps`, `pityTier` | 5, 3, rare | heat per tier step, the most steps, the tier that resets heat |
| `maxNothing` | 3 | blanks in a row per player per machine, at most |
| `jackpotEvery`, `jackpotLegendary`, `jackpotWonder` | 5, 6, 4 | jackpot rounds and their extra weights |
| `scrapPayout`, `pointsBack`, `autoScrapAt` | 40, 1.5, 3 | scrap for a cog, points back (x the price) for a coin, auto-scrap from this many loose |

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

- **Drops.** Every zombie a player kills may drop one: `dropChance` 4 percent, at most `maxPerRound` (4) a round (the
  count resets when a round starts). The kind is rolled from `weights` (Max Ammo 2.5, Insta-Kill 1, Double Points 1,
  Nuke 0.8, Carpenter 0.8, Infinite Ammo 0.5: Max Ammo is about 38 percent of drops, 43 with every board up, and
  Infinite Ammo about 8); the Carpenter only drops while a board is down. A zombie killed outside (at a window) drops
  it at that window's inside spot. Kills by a burn, a splash or a chain drop like any kill; the Nuke's kills drop nothing.
- **Looks.** It floats 0.9 m up, turning and bobbing, a glowing token for its kind (an ammo can, a skull, two gold coins,
  a bomb, a hammer, a purple infinity sign under a gold round) over a green halo with a faint beam; its name floats over it within 12 m. It lasts `lifetime` (30 s)
  and blinks for the last `blinkSeconds` (10 s), faster in the last 3.
- **Taking one:** the first player standing who walks within `pickupRange` (1.3 m) takes it **for the whole team**; a
  feed line ("Tester grabbed Insta-Kill: every hit kills for 30 seconds!") and a banner ("INSTA-KILL!") go to everyone.

| Power-up | What it does |
| --- | --- |
| **Max Ammo** (the most common, about 38 percent) | Every player's guns get a full reserve (magazines stay as they are, as in World at War; a bot's magazine fills) |
| **Insta-Kill** | For `duration` (30 s) every hit on a zombie kills it |
| **Double Points** | For 30 s every point earned (hits, kills, boards) counts twice |
| **Nuke** | Every zombie alive dies at once (no kill points or drops); every player gets `nukePoints` (400) |
| **Carpenter** | Every window's boards go back up; every player gets `carpenterPoints` (200) |
| **Infinite Ammo** (rare, about 8 percent) | For `durations.infiniteAmmo` (30 s) every magazine is full and no shot uses ammo, so nothing reloads: plain guns, every build (all receivers, shared feeds, special ammo), launchers and push carts. A second one restarts the clock (no stacking) |

The timed ones last their `durations` entry (`instaKill`, `doublePoints`, `infiniteAmmo`; `duration` otherwise). The
HUD shows Insta-Kill, Double Points and Infinite Ammo with their seconds left, bottom centre (blinking in the last 5 s);
while Infinite Ammo runs the ammo counter reads `40 | INF`. The server owns the timer; clients get it in `ZWorld` and
fill their own magazines from it.

**Decision (user, 2026-10-23):** add Max Ammo as one of the most common drops and an Infinite Ammo one. Read as
power-up drops (not perk machines). Max Ammo's weight went 1.2 -> 2.5 (about 38 percent, up from 25) and `dropChance`
3 -> 4 percent, so a round of 24 kills shows about one power-up instead of 0.7 (still at most 4 a round); Infinite
Ammo is timed, 30 s, weight 0.5.

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
| `explosive` (Explosive Tip, Frag) | Splash: the zombies within 2.2 m of the one hit take half the hit, less further out; walls shield |
| `shock` (Shock Coil, Shock rounds) | Chain: arcs to the 3 nearest other zombies within 3.5 m (not through walls) for 40 percent, stunning them 0.4 s |
| `penetrating` (Armour Piercing, Sabot) | Pierce: goes on along the ray through the next 2 zombies within 8 m behind (until a wall), at 60 percent |
| `plasma` (Plasma) | Slow: half speed for 2 s, crackling blue |
| `bouncy` (Rubber Duck) | Bounce: on to the nearest other zombie within 4 m, at half |
| `squeaky` (Rubber Duck, Whoopee Cushion) | A squeak and a 0.3 s stun |
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

## Animation (zombie bodies)

Procedural and data-light: no skeletal assets. The zombie keeps its blocky box body (torn shirt, green skin, glowing
eyes) but its arms and legs are now two bones each (upper arm 0.38 m + forearm 0.42 m, thigh 0.45 m + shin 0.43 m),
placed by an analytic two-bone IK from a handful of targets per frame (pelvis, torso lean / roll / twist, head roll /
nod, both hands, both feet). `Source/Render/ZombieAnim.cs` works the targets out from the zombie's state and clocks
(`ZombieAnim.Targets`), blends them for a third of a second after a change of state, solves the IK (`ZombieAnim.Solve`)
and draws the boxes (`ZombieAnim.Draw`). Movement, collision and the nav graph are unchanged. Hit boxes are too, except at a
window: there the box follows the drawn body (see "Tearing boards" below).

| State | What the body does |
| --- | --- |
| Walking to the window, chasing | A **shamble** (walkers: short heavy steps, arms out in front or hanging), a **jog** (runners: arms pumping low) or a **sprint** (sprinters: hunched, big strides). The walk cycle is keyed to how fast it really moves, so the feet don't slide; standing still it sways in place. |
| Per zombie (seeded by its id, the same on every PC) | Arm pose (both reaching, one reaching and one hanging, or both hanging), a limp on one leg (about half of them, mild to bad: shorter steps and a hip drop), the head hanging to one side and forward, a bit more or less lean, arm height. |
| Tearing boards | It steps up to the window (up to 1.05 m forward of where it stands, so the hands reach; up on its toes for the top boards) and reaches both hands to the **top remaining board**. Hands on it, it tugs (the board wobbles), braces, and on the tick the board comes off it **jerks back with the board in its hands**: the torn board follows the same yank as the hands, then is flung aside, tumbles to the ground outside, lies there and shrinks away (1.5 s) instead of popping. Then it reaches for the next one. With no boards left it claws at the opening. **The hit box goes with the body:** `ZombieAnim.BodyShift` gives the flat offset (the step plus half the lean, so the box is centred between the feet and the head) from the same input both sides draw from; the server writes the zombie's lag-compensated history entry with it (`ZombieHitShift`, ZombieAI.cs) and the client's hit prediction uses it, so what you see is what you hit, from the side and head-on. Its own position (the AI, collision, the climb) doesn't move. |
| Climbing in | Hands go onto the **sill** and stay there while the body tips forward over it with the legs tucked up; then it lets go, the legs reach down for the floor inside, it lands with bent knees and **staggers** for 0.7 s. Driven by the climb's progress on the server's own climb path (`climbSeconds`): the timing, collision and nav are unchanged. |
| Rising (ground spawners) | Deep in the ground, the arms burst up first, clawing; the hands plant on the ground and push, the torso and head come out, then the right leg steps up and the left. Dirt is thrown up round the hole. |
| Swiping | The swiping arm (left or right, per zombie) winds up behind during the windup and strikes across the front **right as the damage lands** (the windup, `zombie.windup`), then recovers; the other arm reaches. At a window it swipes through the opening. |
| Shot | A flinch: the torso snaps back and the head jerks (stronger for bigger hits; at a window the hands stay on the board). |
| Dead (ragdolls off) | Tips over backwards with the arms flying up, then sinks away (as before). |

**Network (protocol 43).** Clients see the same thing: every clock is the server's and read at the same interpolation
tick as the positions. A zombie's `ZEnts` entry stays **12 bytes**: while it tears or climbs (it faces its window
then, so the yaw is known from the map) the yaw byte carries the **animation byte** instead: the board cycle (0-255, the
board comes off at 255) or the climb's progress. The state byte's changes give the state, swipe and landing clocks (at
the snapshot's tick), and a drop of the health byte gives the flinch. Clients extrapolate the animation byte between
snapshots from the rules file's `boardTearSeconds` / `climbSeconds`.

**Cost.** Zombies outside the view frustum aren't posed at all; beyond 28 m each limb is one box (the old box count).
Up close a zombie is about 22 boxes (thighs, shins, feet, torso and head, eyes, upper arms, forearms, hands). Posing
allocates nothing. The self-test poses and draws 24 zombies headlessly (a recorded renderer) for 300 frames and
fails on any allocation or more than 60 us a zombie; the server's per-tick animation update is part of the "AI cost"
line. The smoke step's barrier line logs `zombie anim: N posed, N culled, N boxes per frame, CPU N us/frame`.

**Self-test** (`SelfTestZombieAnim.cs`, section "Zombie animation"): both hands within 3 cm of the board at every
window of Blackmoor Asylum and Zombies Test, from four places a tearing zombie can stand, for every board count and
seven zombie looks; the yanked board within 7 cm of the hands; both hands within 3 cm of the sill along the server's climb
path; bones that keep their lengths and finite poses in every mode; walking feet on the ground; rising arms first;
the swipe behind at the end of the windup and across the front after it; the variety over 200 ids; a host and a
client giving the same pose input for tearing, climbing, rising, swiping and chasing zombies (and the same flinch
tick); no allocations and the cost. The hit box at the window: rays through the drawn head, chest and hips of a zombie
at every window of both maps (from the side, head-on from the inside spot, from behind and from above, for every board
count, while clawing and swiping, and while the step is still blending in) all hit the shifted hit box, and the drawn
points sit at least 3 cm inside it. "Zombies mode" shoots a real tearing zombie through the server's shot path at its
drawn head and chest from the side and from the inside spot and expects every shot to land.

**What to check by eye** (nothing graphical runs in CI): a zombie's hands on the top board while tearing and the board
coming away with them; the body leaning over the sill with the hands planted while climbing in, then the stagger;
ground zombies clawing out; the shamble / jog / sprint and the limps; the swipe landing as you take the hit; shots
at a zombie tearing boards landing from a steep side angle (the hit box follows the step in).

## Sound (zombie voices and footsteps, 2026-10-26)

Before this zombies only borrowed generic sounds (a melee hit when they strike you, a flesh hit when shot, wood
breaking at windows). Those stay; on top, every zombie now has a voice and a body (`Source/Audio/ZombieSounds.cs` decides
what plays, `SynthZombie.cs` makes the sounds, `Audio.Zombies.cs` plays them):

| When | Sound |
| --- | --- |
| A foot plants (the walk cycle passes pi/2 + k pi, the same cycle the legs are drawn with) | By speed class: a walker's dragged **shuffle** (heard within 16 m), a runner's **thud** (26 m), a sprinter's stamping **slap** (36 m). Moving only (not standing, tearing or climbing). |
| A new swipe starts (the windup) | A hissing intake and a rising **snarl** (35 m). The hit you take is still its own sound when it lands. |
| Shot, not killed | A **grunt**; a **screech** that chokes off when the hit was in the head. Burns and bleeds flinch every tick, so at most one grunt every 0.3 s. |
| Killed | A falling **death rattle**; a headshot kill is a screech **cut off** into a gurgle. |
| Rising from the ground, climbing in | A **growl**. |
| On its own | **Groans and moans** (walking to its window: every 5-10 s, mostly moans) and **growls** (chasing or swiping: every 3-6 s; tearing boards: every 2.2-4.5 s; mostly growls). Sprinters are a fifth more vocal. A zombie first seen waits 0.5-4.5 s, so a horde doesn't start in chorus. |

**The sounds** are synthesised at start-up like every other effect (no samples): a buzzing glottal pulse with jitter,
a period-doubling growl, a wet gurgle and breath noise through three formant resonators gliding between vowels, then a
little drive. 40 buffers: 6 groans, 5 growls, 4 snarls, 4 grunts, 3 screeches, 3 death rattles, 3 cut-off screeches and
4 of each step. Every zombie has its own voice pitch (0.84-1.16, by id, the same on every PC; its steps are a little
heavier or lighter to match), a small random pitch and level on top, and never plays the same variant twice in a row.

**Caps.** Zombie voices have their own 8 OpenAL sources and their steps another 8, so a horde never takes the voices
gunfire and players' steps use. A new sound takes a free voice, else the farthest one if it is nearer, else it is
dropped; an idle groan counts as 1.5x as far away as it is, so it never takes a voice from a nearer snarl, grunt or
death. One mouth per zombie: an event cuts the zombie's own last vocal, and an idle groan waits for it. The idle groans
share a budget of 3 a second for the whole horde (bursts of 4). Sounds beyond their range don't start at all.

**Network and cost.** Every PC (host and clients) works the sounds out every frame from what it already knows to draw
the zombies: the walk cycle, the swipe and flinch clocks and the state (see "Animation"). The server does no extra work.
The one thing clients didn't know was whether a hit was in the head, so **protocol 50** gives a zombie's state byte in
`ZEnts` a head bit (bit 3; the state is the low 3 bits): alive, a head hit landed in the last 4 ticks (two snapshots see
it); dead, the kill was a headshot (which also gives clients the headshot ragdoll). Still 12 bytes a zombie. The
director allocates nothing and costs a couple of microseconds a frame for 24 zombies.

**Mixer** (F8): *Zombies* is everything zombies make; *Zombie voices* and *Zombie footsteps* (keys `zombies.voices`,
`zombies.steps`, 0 dB by default) are on top of it, so the Zombies row still turns all of it up or down. Zombie steps
also follow Settings > Audio's footstep volume. Saved mixes from before keep their levels and get 0 dB for the new rows.

**Self-test** (`SelfTestZombieSounds.cs`, section "Zombie sounds"): every sound synthesises with at least 3 variants
(the screech brighter than a grunt and a groan, a death rattle longer than a grunt, a cut-off screech shorter than a
rattle) on its mixer row; the rows' defaults and an old save; one step per foot plant at each speed and none standing;
one snarl per swipe; a grunt for a body hit, a screech for a head hit, burn flinches rate-limited; a death rattle and a
cut-off screech; nothing for a zombie first seen or 90 m away; the idle rate chasing vs walking to a window; a
24-zombie sprinting horde with swipes and hits for 10 s never has more than 8 + 8 voices busy, keeps to the groan
budget and allocates nothing; and over the network a real head shot and body shot on the host give a screech and a
grunt on host and client, and a headshot kill the cut-off screech on both.

**What to check by ear** (nothing audible runs in CI): a horde coming round a corner (steps and growls from the right
side), the snarl just before a swipe lands, headshots screeching, and a big round staying a horde rather than a wall
of noise.

## Network (protocol 19; protocol 40 for milestone 3; 41 for disassembly; 43 for the animations)

Protocol 50 (sounds, see "Sound"): bit 3 of a zombie's state byte in `ZEnts` is the head bit (a fresh head hit, or a
headshot kill); the state is the low 3 bits.

Protocol 43 (animations, see "Animation"): a tearing or climbing zombie's yaw byte in `ZEnts` is its animation byte
(the board cycle / the climb's progress); still 12 bytes a zombie.

Protocol 41: `ZBench` kind 3 takes a slot's build apart; `ZBuild` flag 4 = the slot is now empty (no text), also sent to
joiners for empty slots 1-2 (see "Disassembling a build").

Protocol 40 (milestone 3): `ZWorld` ends with the floating power-ups (id, kind, position, tenths of a second left) and
Insta-Kill / Double Points' seconds left; `ZEvent` gains `MaxAmmo` (refill your reserves) and `Fx` (a special ammo
effect: kind, sound and a position after the text; unreliable); bits 6-7 of a zombie's state byte in `ZEnts` say
burning / zapped. (38 and 39 are other features on main.)

Protocol 51 (2026-10-26): `ZWorld` ends with the slot machines (see "Slot machines"). (50 is the zombie sounds on main.)

Protocol 49 (2026-10-23): `ZWorld`'s floating power-ups may be kind 5 (Infinite Ammo), and a third timer (Infinite
Ammo's tenths of a second left) follows Insta-Kill's and Double Points'.


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
24 zombies chasing inside the start room (see the report line "AI cost"); it includes the per-tick animation update
(the animation byte and clocks, a few comparisons a zombie). Drawing cost: see "Animation".

## Controls

F (Interact): hold to buy, revive, rebuild boards, salvage, pull a slot machine; press at a workbench to open it (Esc / Close goes back).
F3 (Ready): between rounds, ready for the next one (again: not ready; the host, once ready: start it now).
Right / Left arrow (or Fire / Aim): while out after bleeding out, spectate the next / previous living teammate.
Downed: the move keys crawl.
At the workbench, D twice (or Disassemble clicked twice) takes the slot's build apart; at the mystery box, G twice takes the gun in your hands apart.
Melee (V): the melee slot's build (the Machete to start). Weapon slots 1-4 as before (3-4 after the upgrade).

## Open questions (for the user)

1. **Numbers**: the round curve, points, prices, perks, reserve ammo and the drop chances are all proposed.
2. ~~Bleeding out~~: **decided**: builds drop as pickups for 5 minutes (see "Dropped builds"). Still open: should
   anyone be able to pick up your builds (today), or only you; and a points refund for the owner?
3. ~~Solo self-revive~~: **decided**: Second Wind, a perk in the start room (solo and co-op). Still open: its price
   (fixed 1500, or rising each buy), whether it should count toward the perk limit (today it does), and whether solo
   Quick Revive should be cheaper again.
4. ~~Power-ups~~: **done in milestone 3** (all five, World at War style; see "Power-ups"). Still open: the numbers
   (4 percent a kill since 2026-10-23, 4 a round, 30 s; Max Ammo 38 percent of drops), and whether drops should follow World at War's points-based schedule instead.
5. ~~Special part flags~~: **done in milestone 3** (every flag on a part; see "Special ammo effects"). Still open: the
   numbers, and whether effects should also hurt players in other modes.
6. **Co-op bots** hold the start area, shoot, revive and rebuild boards, but never buy anything (still the default).
   Should they buy doors / guns / perks with their own points?
7. **Downed**: shoot with whatever is in your hands (still the default), or a forced pistol (World at War's last stand)?
   Crawling (2026-10-23) is 0.8 x the prone speed (`downs.crawlSpeed`): slower or faster?
8. **Melee builds from the box** go to the melee slot (still the default): or should the box give guns only?
9. ~~Machines have no collision~~: **done in milestone 3** on real maps (`"machineProps": true`, solid props from
   `Props/Zombies`); Zombies Test keeps them collision-free.
10. **Second Wind's price** (still a fixed 1500, counting toward the 4 perks; see question 3).
11. **Blackmoor Asylum**: the layout and prices (750 / 750 / 1000 / 1250 / 1250 / 1500, the power shutter 750) are
    proposed; more Zombies maps next?
12. **Ready-up** (2026-10-23): the fallback is 3 minutes and the countdown 3 s; the host forces the start by pressing F3
    again once ready (rather than a separate key). Spectating is over the shoulder only (first person would need the
    watched player's arms and gun drawn). OK?
13. **Slot machines** (2026-10-26, proposed defaults): the odds table, heat (5 a step, 3 steps, shared by the team),
    jackpot every 5th round, 3 blanks at most, auto-scrap from 3 loose copies. Not done: **bet levels** (1x / 2x / 5x)
    and a machine that **moves** on a skull like the box's teddy bear (the cabinets are solid map props); bots don't pull.
