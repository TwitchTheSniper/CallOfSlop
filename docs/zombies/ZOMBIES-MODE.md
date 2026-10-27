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
| **Mystery box** | Spins 4 s, then offers a **complete build** tagged `"box"` for 10 s (only the buyer can take it); its tier is rolled from `DropTables.json` `boxTiers` by round, then a gun of that tier you don't already carry (**guns only** since 2026-10-26: 46 box guns, 7-12 per tier; melee builds come from the slot machines). After 4-8 spins (rolled) the next spin is the **teddy bear**: refunded, the box flies off to another of its spots | `costs.box` 950 |
| **Perk machine** | A perk from the rules file (one machine per perk). No cap: beyond the 4th each perk costs double the step before (the 5th 2x its price, the 6th 4x...); all lost when downed | per perk |
| **Weapon-slot upgrade** | Slots 3-4 for the rest of the run (the "Mule Kick" of this game) | 4000 |
| **Power switch** | Turns the power on, once (free). Buyables marked `"power": true` don't work until then | free |
| **Workbench** | Opens the workbench over the match (the zombies don't wait) on your own slots | free |
| **Salvage pile** | Search it for a loose part (rolled like a zombie's part drop), once per round per pile | free, +1 part (`salvageParts`) |
| **Slot machine** | Three reels spin 2 s and pay a loose part, a complete melee build, points back or nothing; a loss frees the machine at once (see "Slot machines") | 500 + 25 a round after the first, at most 1250 |

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

## Parts and the workbench (the roguelite climb)

**No scrap (developer request, 2026-10-27):** "Edit the zombies mode to remove the scrap feature. I believe the gambling
machines replace the need." Scrap is gone from Zombies: kills and salvage piles no longer give it, the workbench no
longer breaks parts down or crafts them (CRAFT MISSING went with it), and the slot machines lost the cog. Parts come
from zombie drops, salvage piles, the slot machines, and taking builds apart (the mystery box's offer too). What
replaced what (proposed, all data): the cog's weight in `slotTiers` went to **points** (coins), so a pull wins as
often as before (round 1: 71.4 percent); bad-luck protection and the "no part of that tier" fallbacks pay coins; two
common gems read as coins; a part you already hold 3 of loose pays the coin payout instead (`dupePointsAt`); a salvage
pile gives a loose part (`parts.salvageParts`). Older rules and drop tables with the scrap keys still load (an old
row's scrap weight counts as points); old part files' `"scrap"` is ignored. Protocol 56. Self-test "Zombies no
scrap".

- **Drops.** Every zombie kill may drop a part: the chance and the part's tier by round come from `DropTables.json`
  (`dropChance`, `partDrops`); the part is one of that tier. It lies on the floor for 30 s as a small glowing box in its
  rarity colour; walk over it to pick it up (a feed line says what it was).
- **Inventory.** Per player, per run: loose parts (by name, with counts). The parts on your guns are yours
  too.
- **Workbench.** F at a workbench opens the same editor as the main menu's sandbox, but it lists **only the parts you
  own this run** (loose plus those already on the build) and edits **your slots** (1-2, 3-4 with the upgrade, and the
  melee slot). APPLY sends the build to the server, which checks you're at a bench and that every added part is in
  your loose parts; parts taken off go back to your loose parts. The bench lists your loose parts (there is no breaking
  down or crafting since scrap went, 2026-10-27).
- **Disassemble**: take a slot's whole build apart into your loose parts (below, "Disassembling a build").
- **The workbench UI** (WEAPON-BUILDER.md, "The workbench"): pick a grip, then drag your parts onto the gun; the build
  reads as a linear list with live stats. Standard builds load as plans and a receiver assembly can be copied without
  the parts: what's missing shows red, and the build can't be applied until you own them (drops, salvage piles, the
  slot machines). First-tier grips (common, uncommon) hold one receiver.

## Disassembling a build (2026-10-23)

The user asked: "In the zombie game mode, allow for the user to completely disassemble a gun into inventory items from
the workbench." Code: `Source/Game/ZombiesDisassemble.cs` (server, messages, the empty slot), `Source/UI/UiWorkbenchRun.cs`
(the button); self-test `Source/App/SelfTestZombiesDisassemble.cs` (in the "Zombies mode" section).

- **What.** At a workbench, **Disassemble slot N** (top button row; key D; click twice, or D twice, within 4 s) takes the build in the slot being
  edited apart: **every part, the grip too**, goes to your loose parts (counts add up: a build with two Box Mags gives
  two), and the slot is **empty**. It works on any build: guns in slots 1-4 (wall-buy and box builds too, they're builds
  like any other) and the **melee slot** (hilt, head and mods). One **Undo** puts the same build back from your loose
  parts while the bench stays open (an ordinary Apply, checked again; it's gone once you close the bench or the slot gets
  something else).
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
- **At the mystery box: the OFFERED build (board bug fix, 2026-10-26).** The first version (earlier the same day) made
  G twice at the box take apart the **gun in your hands**; testers read the prompt as "take the box's gun apart" and lost
  the gun they were holding. Now, while the box is **offering you a build** (you spun it), a second HUD line under
  "Hold [E] to take the X" says "Press [G] twice to take the X apart into parts instead": **G** (the grenade key,
  rebindable as "Throw grenade (Zombies: take apart at the box)"; there are no grenades in Zombies) **twice within 4 s**
  takes the **offered build** apart: all its parts (grip included) go to your loose parts and the box closes, exactly as
  taking it would; **your slots and the gun in your hands are untouched**. With no offer (idle, spinning, teddy bear,
  someone else's spin) G at the box does nothing. Free (the spin was the price). Loose parts have no cap (the parts
  message carries up to 200 kinds and 255 of each). The server checks it (`ZBench` kind 5, protocol 54: within 2.4 m of
  the box, offering, you're the buyer); `ZBench` kind 3 (a slot's build) is a **workbench-only** action again. Self-test
  ("Zombies mode", `SelfTestZombiesDisassemble.cs`): the box's offer gives exactly its build's parts, leaves the held build,
  every slot and the melee slot as they were, closes the box, and refuses with no offer, someone else's offer or from
  too far, for the host and for a client's request.
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
  "slots": [ {"at": [x, z], "yaw": 180, "power": false, "cost": 0, "category": "Barrel"} ],
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
  for 2 s and stop one by one (a click each) on what the server rolled. A **win** shows for 2.5 s (the reels flash, a
  part won floats over the cabinet), then the machine takes the next pull. A pull that **wins nothing** (skulls, odd
  gems) frees the machine the moment the reels stop: no result time and no cooldown, so you can pull again at once
  (developer request, 2026-10-27; self-test "Zombies slots"). One player at a time per machine.
- **What the reels mean.** Symbols: a **skull**, a **coin** (points back) and a **gem** per rarity in its colour (the
  wonder gem only on jackpot rounds; the scrap cog went with scrap, 2026-10-27). **Three of a gem**: a loose part of
  that rarity; **two of a gem**: a part one tier lower (two commons: coins); otherwise a coin pays **1.5 x the price
  back** in points, and skulls and odd gems pay **nothing**. The server rolls the outcome first and then picks reels that read
  as it (self-test: they always do), so the table, not the reels, sets the odds.
- **Melee builds (developer answer, 2026-10-26).** The mystery box gives guns only, so melee builds come from here: a
  `"melee"` weight in each `slotTiers` row (5, about 5 percent of pulls) pays a **complete melee build** (the 14
  standard builds tagged `"slots"`), straight into your **melee slot**. Its rarity is rolled from the same row's part
  weights, so it follows the slot tiers: the round, the machine's heat and jackpot rounds (a rare-or-better one resets
  the heat like a part); never the one you hold, the nearest tier with one when that tier has none, never wonder outside
  a jackpot round. The reels show **two blades and the build's gem** (a lone blade is a near miss); a legendary or
  wonder one is announced to everyone, and a glowing blade in its rarity colour floats over the cabinet. The melee build
  you had goes on the floor 3 m in front of the machine as a dropped build (5 minutes, anyone can pick it up), unless
  it's the untouched start Machete. Melee parts stay in the part pool.
- **Parts** go into your **loose parts** (as salvage drops do: use them at the workbench; never a standard build, never
  a part straight onto a gun) from the same pool as zombie part drops. A part you already hold **3 or more** of loose pays
  the **coin payout** (1.5 x the price) instead (`dupePointsAt`; 0 keeps every copy). A legendary or wonder part is announced to everyone.
- **Rarity rising: three levers that stack.**
  1. **By round** (`DropTables.json` "slotTiers", rows from round 1, 5, 10, 15, 25, 35): weights for nothing,
     points and each part tier (no scrap since 2026-10-27: its weight went to points); the share of blanks falls (30 -> 20 percent) and the part mix climbs (round 1: mostly
     common and uncommon, 2 percent rare or better; round 36: about half rare or better).
  2. **Machine heat** (the pity timer): every pull that doesn't pay a **rare-or-better** part (`pityTier`) adds 1 heat;
     every **5** heat (`heatStep`) moves the part weights **one tier up** (at most 3 steps, `heatMaxSteps`); a
     rare-or-better part resets it to 0. Heat belongs to the machine (shared by the team, so a cold machine is a choice).
     The cabinet shows it: a 5-segment meter under the reels fills towards the next step (yellow, orange, red; flashing
     at the top), and the topper's lights chase faster as it warms up.
  3. **Jackpot rounds** (every 5th round, `jackpotEvery`): the **wonder gem** goes on the reels and legendary (+6) and
     wonder (+4) weights are added; the topper flashes gold and pink and the prompt says "JACKPOT ROUND". Outside
     jackpot rounds wonder parts never come up from the slots.
- **Bets (developer answer to question 13, 2026-10-26).** At an idle machine the **second key** (G, the Grenade key:
  there are no grenades in Zombies) moves its bet up a level, wrapping round: **1x / 2x / 5x** the current price
  (`slots.bets`). A pull then moves the part odds `slots.betSteps` (0 / 1 / 2) rarities up the ladder, on top of the
  machine's heat (the same shift as a heat step). The nothing and coin weights don't change, so a higher bet
  **never guarantees a reward**: skulls come up as often at 5x as at 1x (round 3: 28.6 percent at every bet; the part
  mix goes from 1.9 percent rare-or-better at 1x to 12 at 2x and 33 at 5x). Coins pay 1.5x what you paid. The bet
  belongs to the machine (shared by the team, like the heat) and stays until someone changes it; the prompt shows it
  ("Hold F to pull the slot machine [1100] - bet 2x (G to change)") and so does the cabinet (a lamp per level under the
  heat meter: green, gold, red). Server-authoritative: a client asks with `Msg.ZBench` kind 4 (the machine in its slot
  byte); the server checks you're at the machine and it's idle. Bots pull at whatever bet is set. Self-test "Zombies
  slots": the rarer mix and the unchanged blanks at every bet over seeded rolls, the price, the prompt, the wrap and the
  client copy.
- **Bad-luck protection**: at most **3** "nothing" results in a row per player per machine (`maxNothing`); the next
  would-be blank pays coins (points back) instead.
- **Placement**: Map key `"slots"` (spots like a perk machine's: `at`, `yaw`, `power`, `cost`, and an optional
  `category`, below), at most 24; with machine props each stands as a solid **Reel Cabinet** (0.9 x 0.7 m, 1.95 m tall,
  its front facing the yaw), a category machine as its own cabinet. Blackmoor Asylum: the classic machine in the
  Receiving Hall and one machine for every part category round the map (below; `tools/zombies/make_blackmoor.py`
  refuses to write the map while a `Parts/<Category>` folder has no machine, and "Zombies category slots" fails while a
  map with category machines lacks one); Zombies Test: two classic ones (the start room, and the
  Power Room on the power) and the **Arcade** (behind the West Wing, the Arcade Door, 750) with one machine for every
  category in a row.

### A machine per part category (board item, 2026-10-26)

"A separate slot machine for every weapon part category, each with its own distinct machine shape; each machine only
rolls parts from its own category; when a player wins a part, a visible display of what they got that nearby teammates
can see." Code: `Source/Game/ZombiesSlots.cs` (rolls), `Source/Game/ZombiesSlotWin.cs` (the win message),
`Source/App/Program.ZombiesSlots.cs` (drawing), `Source/UI/UiZombies.cs` (labels); props `Props/Zombies/SlotMachines/`
(written by `tools/zombies/make_slot_cabinets.py`); self-test section "Zombies category slots".

- **Which categories.** All sixteen part categories (`PartCategory`): Receiver, Barrel, Splitter, Feed, Ammo, Stock, Grip,
  Optic, Muzzle, Utility, Receiver Upgrade, Damage Multiplier, Hilt, Head, Melee Mod, Action. A map's slot entry names
  one with `"category"` (the enum name or the display name, case-insensitive; an unknown one refuses the map); without
  it the machine is the **classic** one and pays any part, as before.
- **The rules are the classic machine's, per machine.** The same `slotTiers` odds by round, bad-luck protection, bets,
  jackpot rounds (wonder parts on every machine's reels) and **its own heat** (pulls on one machine never warm another).
  A part result picks a part of the machine's category of the rolled tier, or the **nearest tier that category has**
  (lower first; never wonder outside a jackpot round); the reels then show the tier actually paid. The blade (a whole
  melee build) is only on the classic machine and the **melee categories** (Hilt, Head, Melee Mod); on a gun category's
  machine a melee result pays a part of that tier instead, so the chance of a win is the same everywhere.
- **Prices (proposed).** `ZombiesRules.json` `slots.machines.<Category>.price`: a multiple of the classic price (to the
  nearest 10), so it still rises with the round and the bets multiply it: Receiver 1.2, Barrel 0.9, Splitter 1.3, Feed
  0.9, Ammo 0.8, Stock 0.8, Grip 1.0, Optic 1.0, Muzzle 0.8, Utility 0.8, Receiver Upgrade 1.2, Damage Multiplier 1.4,
  Hilt 0.9, Head 0.9, Melee Mod 0.8, Action 1.1; `melee` turns the blade on or off per category. A map spot's `cost`
  still overrides the price.
- **Shapes (own bodies, 2026-10-27).** Each category has its own cabinet BODY round the classic machine's face: the reel
  window, category band, topper marquee, heat meter, bet lamps, button shelf and lever sit exactly where they are on the
  Reel Cabinet (the game draws the reels and lamps there), on a plate whose front is at z -0.255, and the body round and
  behind it is the category's own shape, built from boxes (round parts are regular 12-sided prisms: six boxes turned 30
  degrees apart): **Receiver** a chunky boxy receiver with a top rail, a charging handle and its ejection port open on
  one side; **Barrel** a fluted barrel standing on end with its muzzle crown, bore and front sight; **Splitter** a Y
  fork (a trunk, a manifold and two arms with nozzle caps); **Feed** a drum on its edge with a curved box magazine rising
  from it and a round in its feed lips; **Ammo** a giant cartridge (rim, case, shoulder, copper bullet); **Stock** a stock
  standing on its butt pad, its back a sloped wedge with a cheek comb; **Grip** a raked pistol grip with finger grooves
  under a frame, the trigger in its open guard; **Optic** a scope lying across it in rings, round lens ends; **Muzzle**
  a flared skirt, a stem and a ported bell; **Utility** a red tool chest on casters with a top box, a carry handle and a
  spanner; **Receiver Upgrade** a spur gear on its axle with an upright gear on top; **Damage Multiplier** a stepped
  tower with chevrons and a big "x2"; **Hilt** a sword hilt (pommel, wrapped grip, curled guard, blade stub); **Head** an
  axe head on its haft in a chopping block; **Melee Mod** a nail-studded club; **Action** a bolt on a coil spring with
  its handle and lugs. All keep the classic footprint (about 0.9 x 0.7 m) and are solid; they stand 1.9 to 2.7 m tall.
  Zombies Test (no machine props)
  draws the same cabinet without collision. Over each machine within 9 m a label names it ("BARREL SLOTS", "SLOTS:
  ANY PART") in the category colour; the prompt says "Hold [E] to pull the Barrel slot machine".
- **The win, for everyone.** When a machine pays a part or a melee build the server sends `Msg.ZSlotWin` (protocol 55:
  machine, player, result, the part or build index, tier, flags: bit 0 a duplicate paid as points, protocol 56) to every client. Every PC then shows for
  `winSeconds` (4 s) the won part's **own model** turning over the machine (0.95 m over the cabinet's tallest point,
  so it clears every body; the labels go over it) with a halo of glow boxes and a beam in the
  rarity colour, a label "Name: a Rare Long Barrel" over it (within 25 m), and a kill-feed line in the rarity colour
  ("Name won a Rare Long Barrel on the Barrel slot machine!"); legendary and wonder wins add a light pillar, sparks
  spiralling up and a sound heard across the map. A duplicate paid as points says so ("points instead"). The old feed lines for slot
  wins are replaced by this one.
- **Blackmoor Asylum's spread (proposed).** Receiving Hall: the classic machine; East Ward: Barrel; West Offices: Grip,
  Stock; Courtyard: Ammo, Feed (against the hall's back wall); Generator House: Action; Chapel: Hilt, Head; Research Lab
  (on the power): Receiver, Optic. Splitter, Utility, Receiver Upgrade, Damage Multiplier and Melee Mod parts come from
  the classic machine there (and drops and salvage piles).
- **Open questions for the developer.** Every map all machines, or a selection (as now)? The per-category prices above?
  Should the classic any-part machine stay next to the category ones?
- **Network (protocol 51).** `ZWorld` ends with the machines: a count, then 11 bytes each (state, heat, owner, a spin
  counter, the three symbols, result / tier / a duplicate paid as points, the part, tenths of a second left); protocol 53 adds result 4
  (a melee build: the part field is then an index into the standard builds), the blade symbol (9) and a 12th byte, the
  bet level; protocol 56 drops scrap (results 0 nothing, 1 points, 2 part, 3 melee; symbols 0 skull, 1 coin, 2-7
  gems, 8 blade). Clients start the reel
  animation when the spin counter changes (worked back from the seconds left, so a joiner mid-spin lines up); the parts
  come in `ZRun` as for any payout. Bots pull now and then with plenty to spare (see "Co-op bots"). Nothing in the machines' tick allocates
  (self-test).

| Rules (`ZombiesRules.json` "slots") | Default | What |
| --- | --- | --- |
| `costBase`, `costPerRound`, `costMax` | 500, 25, 1250 | the price: base + per round after the first, capped |
| `spinSeconds`, `showSeconds` | 2, 2.5 | the spin, and how long a win shows (a loss frees the machine at once) |
| `heatStep`, `heatMaxSteps`, `pityTier` | 5, 3, rare | heat per tier step, the most steps, the tier that resets heat |
| `maxNothing` | 3 | blanks in a row per player per machine, at most |
| `jackpotEvery`, `jackpotLegendary`, `jackpotWonder` | 5, 6, 4 | jackpot rounds and their extra weights |
| `pointsBack`, `dupePointsAt` | 1.5, 3 | points back (x the price) for a coin; a part you hold this many of loose pays that instead (0: keep it) |
| `bets`, `betSteps` | [1, 2, 5], [0, 1, 2] | the bet levels' price multipliers and their rarity steps |
| `winSeconds` | 4 | how long a win shows over the machine |
| `machines.<Category>` `price`, `melee` | see above | a category machine's price (x the classic) and whether it has the blade |
| `DropTables.json` `slotTiers` `melee` | 5 | the weight of a complete melee build (its tier from the row's part weights) |

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

- **Drops: a team points schedule** (World at War style; developer answer, 2026-10-26, replacing the flat 4 percent a
  kill roll, so rewards come more evenly). Every point the team **earns** (hits, kills, boards; Double Points counted as
  paid; not the Nuke's, the Carpenter's or a slot machine's coins) goes into one shared tally. When it passes
  `firstAt` (1800) a drop is **due**, and the next zombie a player kills drops it; the next one is due a step later
  (from the points at that drop), and each step is `stepGrowth` (1.02) x the one before, at most `stepMax` (4000): 1836,
  then 2194 by the 10th drop, 3260 by the 30th. At most `maxPerRound` (4) a round (the count resets when a round
  starts): a drop due at the cap waits for the next round's first kill. `dropChance` adds an optional flat chance per
  kill on top (0: off). Tuning: a scripted model of solo and 4-player runs to round 30 (the real round curve, hits per
  kill from zombie health against a gun that improves) gives the same number of drops as the old rule (x1.00: 12 by
  round 10 solo, 91 by round 30), more evenly spread; self-test "Zombies mode" ("power-up schedule", "power-up rate").
  Server-only (no wire change). The kind is rolled from `weights` (Max Ammo 2.5, Insta-Kill 1, Double Points 1,
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
Ammo is timed, 30 s, weight 0.5. (2026-10-26: the per-kill chance became the points schedule above, at the same average rate.)

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

**On players too (developer answer, 2026-10-26).** Wherever a gun or melee build carrying these flags can be used
against players (the Workbench Range's targets today; any mode that lets builds meet players), the effects hurt players
the way they hurt zombies, with the same numbers (`Source/Game/AmmoEffectsPlayers.cs`): burns and bleeds tick on the
player (as napalm: Flak Jacket helps), the splash hits the players round the one hit (walls shield), the shock chains to
the 3 nearest, the pierce goes on through the players behind, the rubber duck bounces to the nearest, the bayonet
hits harder at point blank, the cup holder gives a sip per kill, and the rocket booster, the frozen fish and the toaster
knock players about (the predicted knock, as a blast's). Stuns (shock, squeak, bonk, chainsaw) hold bots still, as a
melee build's stun does; the plasma slow and the party balloons stay zombie-only (a player's movement is predicted,
and a slow would need replicated state). **Friendly fire is respected**: all of it goes through the normal damage rules
(no damage to teammates in team modes or co-op, so in Zombies nothing changes), area effects never pick a player the
shooter can't damage (a teammate doesn't soak up a chain), and the shooter is never hit by their own effects. The
flashes show in every mode (the `ZEvent` effect message is read outside Zombies too; no wire change). Self-test
section **"Ammo effects on players"** (a flat lab map, scripted shots in FFA and TDM, no matches; no allocation in the
burn tick, an effect shot allocates no more than a plain one).

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

## Co-op bots (developer answer, 2026-10-26)

Bots hold the area they started in, shoot, revive and rebuild boards as before, and now **spend their own points**
the way a player would (`Source/Game/ZombieBotsShop.cs`; rules `ZombiesRules.json` "bots"; self-test section
**"Zombies bot shopping"**). Everything goes through the same interact key, prices and hold time as a person.

- **When**: only when it's safe: in the break between rounds (or before round 1), or mid-round with no zombie within
  `quietRange` (10 m). Running from a zombie on top of it, **reviving** a downed teammate and **boarding** windows in its
  area always come first; a bot with a downed teammate doesn't shop at all.
- **Where**: within `shopRange` (40 m) of its post, in unlocked areas, walking area to area through the open doors'
  sides; afterwards it walks back to its post, so nobody is left alone at the windows (no stranding the team).
- **What, in order** (a think every `thinkSeconds`, 0.5 s; always keeping `reserve`, 250 points): the **power switch**
  (free); **perks** in `perkOrder` (Juggernog, Quick Revive, Speed Cola, Double Tap, Second Wind), at the players'
  prices (the doubling past the 4th included); the first one it can reach but can't afford yet is what it **saves
  for**, so the rest only spends what's left over; a **wall gun** with `wallBetter` (1.15) x the damage per second of
  the gun in its hands (or into a free slot); the **slot upgrade**; the **mystery box** with `boxSurplus` (500) spare,
  taking the offer only when it beats the gun in hand (or a slot is free), else letting it go back; a **door**,
  between rounds only and one a break for the whole team (the cheapest off an open area); a **slot machine pull**
  with `slotSurplus` (2500) spare, once a round, at the machine's current bet.
- Something it couldn't reach or buy (the nearest buyable at its spot was something else, or `giveUpSeconds`, 15 s,
  went by) is left alone for 20 s. `bots.buy: false` turns it all off.
- **Cost**: deterministic (no random numbers, fixed orders), the planning allocates nothing (self-test: 500 plans, 0
  bytes) and runs twice a second per bot; on the way it only steers. The seeded Zombies digest in "Mode modules" was
  re-recorded (the bots now shop in the 2-bot match).

| Rules (`ZombiesRules.json` "bots") | Default | What |
| --- | --- | --- |
| `buy` | true | bots spend their points |
| `reserve` | 250 | points a bot always keeps |
| `quietRange`, `shopRange` | 10, 40 | no shopping mid-round with a zombie this close; how far from its post it shops (m) |
| `perkOrder` | jugg, revive, speed, doubletap, secondwind | the perks it wants, first first |
| `boxSurplus`, `slotSurplus` | 500, 2500 | spare points before a box spin / a slot pull |
| `wallBetter` | 1.15 | how much better (damage per second) a wall gun must be |
| `doors` | true | bots open doors (between rounds, one a break) |
| `thinkSeconds`, `giveUpSeconds` | 0.5, 15 | how often it plans; how long it tries to reach something |

## Network (protocol 19; protocol 40 for milestone 3; 41 for disassembly; 43 for the animations; 54 for the box's offer)

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
`ZEvent` (feed lines, ammo refills, banners), `ZRun` (your loose parts), `ZBuild` (a build went into a
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
At the workbench, D twice (or Disassemble clicked twice) takes the slot's build apart; at the mystery box, G twice takes the box's offered build apart (your own gun is never touched).
Melee (V): the melee slot's build (the Machete to start). Weapon slots 1-4 as before (3-4 after the upgrade).

## Open questions (for the user)

1. **Numbers**: the round curve, points, prices, perks, reserve ammo and the drop chances are all proposed.
2. ~~Bleeding out~~: **decided**: builds drop as pickups for 5 minutes (see "Dropped builds"). Still open: should
   anyone be able to pick up your builds (today), or only you; and a points refund for the owner?
3. ~~Solo self-revive~~: **decided**: Second Wind, a perk in the start room (solo and co-op). **Answered 2026-10-26**:
   a fixed 1500 every buy; no hard perk limit any more: each perk beyond the 4th costs double the step before (5th 2x,
   6th 4x, 7th 8x...), Second Wind included; solo Quick Revive unchanged (see "Perks").
4. ~~Power-ups~~: **done in milestone 3** (all five, World at War style; see "Power-ups"). **Answered 2026-10-26**: a
   World at War style team points schedule replaced the 4 percent a kill roll (rewards come more evenly), tuned to the
   same average rate; 4 a round, 30 s, Max Ammo 38 percent kept.
5. ~~Special part flags~~: **done in milestone 3** (every flag on a part; see "Special ammo effects"). **Answered
   2026-10-26**: yes, they hurt players too wherever such a gun meets players, friendly fire respected ("On players
   too"). The numbers stay as proposed.
6. ~~Co-op bots buying~~: **answered 2026-10-26**: yes: they spend their own points on doors, wall guns, the box,
   perks, the slot upgrade and slot pulls, with sensible priorities and never at the cost of revives or boards (see
   "Co-op bots").
7. ~~Downed~~: **answered 2026-10-26**: keep as is (crawl at 0.8 x the prone speed, shoot with the gun in your hands).
8. ~~Melee builds from the box~~: **answered 2026-10-26**: the box gives guns only; the slot machines pay complete
   melee builds into the melee slot (rarity from the slot tiers); melee parts stay in the part pool (see "Slot
   machines").
9. ~~Machines have no collision~~: **done in milestone 3** on real maps (`"machineProps": true`, solid props from
   `Props/Zombies`); Zombies Test keeps them collision-free.
10. ~~Second Wind's price~~: **answered 2026-10-26**: a fixed 1500; it counts as a perk for the doubling past the 4th
    (see question 3).
11. ~~Blackmoor Asylum~~: **answered 2026-10-26**: keep the layout and prices (750 / 750 / 1000 / 1250 / 1250 / 1500,
    the power shutter 750). More Zombies maps are wanted, but later, after the developer talks to testers.
12. ~~Ready-up~~: **answered 2026-10-26**: the fallback is now 5 minutes; the 3 s countdown, F3 again to force the start
    and over-the-shoulder spectating stay.
13. ~~Slot machines~~: **answered 2026-10-26**: bet levels added (1x / 2x / 5x the price, each a rarity step up on top
    of the heat; blanks as likely at every bet, so no sure win; see "Bets"); the other numbers stay; no moving machines.
