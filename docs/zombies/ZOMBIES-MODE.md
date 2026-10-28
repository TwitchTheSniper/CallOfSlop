# Zombies: the game mode (milestone 3)

Status: **playable on two real maps**, **Blackmoor Asylum** and **Ashgrave Cathedral** (lobby: the **Zombies** pack,
then the game mode "Zombies"), plus the dev map `Maps/Dev/ZombiesTest.json` ("Zombies Test", Dev pack). Zombies only shows up for maps that
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
`tools/zombies/make_blackmoor.py`), `Maps/Zombies/02-AshgraveCathedral.json` (written by the C# map kit's generator
`Source/MapKit/Generators/AshgraveCathedral.cs`; its props `Maps/Zombies/Props/Cathedral`, written by
`tools/zombies/make_cathedral_props.py`; the bell siege `ZombiesClimb.cs`), `Props/Zombies/*.json` (the machine props), `Maps/Dev/ZombiesTest.json`.

## How a run plays

- **Start.** Ready up, a 5 s countdown, then round 1 comes after `firstRoundDelay` (4 s). Everyone starts in the map's
  start area with the **start builds** (today: Wall Rifle in slot 1, Wall Pistol in slot 2, the Machete on the melee
  key), **500 points** and the round's **grenades** (below). No loadout screen, no multiplayer perks, no equipment, no
  kill streaks.
- **Rounds.** Each round sends a number of zombies (below); they come out of the **active spawners** (at most 24 alive
  at once early on, rising with the round to hundreds late in a run: see "Hordes"; the rest wait in a queue). When every zombie of the round is dead there's a **break**: the
  next round waits until everyone alive is **ready** (see "Between rounds"; a plain 10 s break in a bots-only run). The HUD shows the round in red tally marks (I to IIIII, then the number) bottom-left, the round
  and what's left of it at the top.
- **Zombies** come from spawners behind **windows**: they walk to the window, tear the boards off one at a time,
  climb in, then go after the nearest player standing, through open doors. Some spawners are in the ground of an area
  (they climb out of it, arms first, in `zombie.riseSeconds`: 1.4 s, 0.9 s before the animations). A zombie swipes at you when it's close (a windup you can back out of). Zombies at a window
  swipe through it at players standing right at it.
- **Downs.** Zombie hits take health (it regenerates as usual). At 0 you're **downed**: you lie on the floor and
  **crawl** (see "Between rounds, downs and spectating"; you can still shoot), lose **every perk**
  (`downs.losePerksOnDown`), and bleed out in 30 s unless a teammate holds the interact key on you for 3 s (Mender's Mead
  halves it). Bleeding out: you **spectate** a living teammate and are out until the **end of the round** (its last
  zombie dies), then back in the start area with the start gear; perks and 10 percent of your points are lost
  (`downs.bleedoutPointLoss`), and your **builds drop on the floor where you went down** (see "Dropped builds").
- **Self-revive: Second Wind** (a perk machine in the start room, no power needed): when you go down with it you get
  back up on your own after `downs.selfReviveSeconds` (5 s), **solo or co-op**; the perk is used up by it, and you can
  buy it again. Downed alone without it is the game over. Mender's Mead is back to World at War co-op behaviour
  (revive teammates twice as fast) and no longer self-revives solo (user decision, 2026-10-21; see below).
- **Game over** when nobody is left standing (everyone downed or bled out). The results screen says
  "Game over on round N: survived N-1 rounds"; after it a fresh run starts on the same map (no map vote in Zombies).
- **No friendly fire**; your own grenade still hurts you.
- **Grenades (board bug fix, 2026-10-27).** Everyone carries Frag grenades (`grenades.kind` in ZombiesRules.json; Frag
  or Impact), thrown with the grenade key (G) as in multiplayer. At the **start of every round** each player is topped
  up (never taken down) to `start + floor((round - 1) / roundsPerExtra)`: 1 at rounds 1-3, 2 at 4-6, 3 at 7-9, 4 at
  10-12, 5 at 13-15 and on up, with **no cap** (developer's decision; ask before adding one). A spawn in a round gets
  that round's count. A blast does `ObjectDamage x 3` to zombies with a 1.4 falloff (a Frag kills round-1 zombies within
  about 5.5 m), credits the thrower with the kill and its points like any kill, and staggers and scatters the horde
  (below); it doesn't open bought doors or tear boards. The server decides the grenade and the count and sends both in
  `ZWorld` (protocol 63); the client's HUD and key follow it. At a slot machine or at the box while it offers you a
  build, G does that machine's thing instead (the bet, the take-apart) and throws nothing. Before the fix the server
  took every grenade away on spawn while the client kept its loadout count, so a throw played its sound, counted down
  and was dropped by the server. Self-test "Zombies grenades".
- **Built grenades (modular grenades, 2026-10-27).** The workbench's **grenade slot** (slot 5, after the four gun slots
  and the melee slot) takes a grenade built from a casing, a fuse, a payload and mods (docs/zombies/WEAPON-BUILDER.md,
  "Modular grenades"). With one in the slot your grenades are that build (`ThrowableKind.Built`) instead of the stock
  frag; the round's refill above gives the same count **plus the build's extras** (Pocket Clip +1, Bandolier Loop +2,
  Infinite Pockets +3, a Garden Gnome or Disco Ball casing +1). Applying, rebuilding or taking it apart keeps the grenades
  you carry, now of the new kind (taken apart: the stock frag again). The grenade build is kept when you bleed out (it
  never drops on the floor; proposed). Built grenades never hurt players (their thrower included; proposed); the
  stock frag still does. Grenade parts come from zombie drops, salvage piles, the classic slot machines and the new
  **grenade parts machine** (below).

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
| Zombies in a round | `(24 + 12 x (players - 1)) x f`; f = 0.25, 0.3, 0.5, 0.7, 0.9 for rounds 1-5, then round / 5; from round 10 also x round x 0.15; worked out again mid-round when someone joins or leaves ("Player scaling") | 6, 7, 12, 17, 22, 29, 34, 38, 43, 72 ... r20 288, r30 648 | 15, 18, 30, 42, 54 ... r10 180, r20 720 |
| Zombie health | 150, +60 a round to round 9 (630), then x 1.06 a round (slowed 2026-10-23, was +100 and x 1.1) | r10 668, r20 1196, r30 2142 | the same |
| Speed | walk 1.3 m/s; runners (3.2 m/s) from round 3 (+15 percent a round, max 90); sprinters (5.2 m/s) from round 5 (+10 percent a round, max 75) | | |
| Spawn delay | 2 s x 0.95 a round, at least 0.25 s | | |
| Most alive at once | `maxAliveCurve`: 24 at round 1, 40 at 5, 90 at 10, 220 at 20, 400 at 30, 600 from 40 (straight lines between), +50 percent per extra player (`maxAlivePerExtraPlayer`), never over `maxAlive` (600) or the host's horde cap | r10 90, r20 220, r30 400, r40 600 | r10 225, r20 550, r25 on 600 |
| Spawning | a burst per spawn time (`spawnBurstCurve`: 1 to round 12, 4 at 20, 7 at 30, 9 at 40; raised with the caps 2026-10-27) from the `activeSpawnersCurve` spawners nearest the players (4 to round 15, 7 at 25, 10 at 35), one per spawner, a spawner resting `spawnerCooldown` (0.35 s) after use | | |

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
| **Mystery box** | Spins 4 s, then offers a **complete build** tagged `"box"` for 10 s (only the buyer can take it at first; the **last 4 s are free for all** since 2026-10-27: anyone can take it, the first take wins; see below); its tier is rolled from `DropTables.json` `boxTiers` by round, then a gun of that tier you don't already carry (**guns only** since 2026-10-26: 46 box guns, 7-12 per tier; melee builds come from the slot machines). After 4-8 spins (rolled) the next spin is the **teddy bear**: refunded, the box flies off to another of its spots | `costs.box` 950 |
| **Perk machine** | A perk from the rules file (one machine per perk). No cap: beyond the 4th each perk costs double the step before (the 5th 2x its price, the 6th 4x...); all lost when downed | per perk |
| **Weapon-slot upgrade** | Slots 3-4 for the rest of the run (the "Mule Kick" of this game) | 4000 |
| **Power switch** | Turns the power on, once (free). Buyables marked `"power": true` don't work until then | free |
| **Workbench** | Opens the workbench over the match (the zombies don't wait) on your own slots | free |
| **Salvage pile** | Search it for a loose part (rolled like a zombie's part drop), once per round per pile | free, +1 part (`salvageParts`) |
| **Slot machine** | Three reels spin 2 s and pay a loose part, a complete melee build, points back or (on a blank) a consolation part; a consolation frees the machine at once (see "Slot machines") | 500 + 25 a round after the first, at most 1250 |
| **Casino table** | Blackjack, roulette, high-low or Bone Dice for points: press the interact key for the table's panel (see "Casino tables") | your bet, within the table's limits |

### Perks (proposed; "perks", up to 16)

| Perk | Key | Price | Power (Blackmoor) | Effect | Rules fields |
| --- | --- | --- | --- | --- | --- |
| Ironhide Cider | jugg | 2500 | yes | zombie hits do 40 percent (the health bar stays 100: 6 hits instead of 3) | `damageTaken` 0.4 |
| Swiftload Seltzer | speed | 3000 | yes | reloads twice as fast | `reload` 0.5 |
| Twinfire Punch | doubletap | 2000 | yes | fires a third faster (client and server fire-rate checks both know it) | `fireRate` 1.33 |
| Mender's Mead | revive | 1500 | no | revive teammates twice as fast | `revive` 0.5 |
| Second Wind | secondwind | 1500 (every buy) | no | down: back up on your own after 5 s (solo or co-op); used up by it | `selfRevive` |
| Fleetfoot Fizz | fleetfoot | 2000 | yes | move 20 percent faster on foot (not while crawling) | `moveSpeed` 1.2 |
| Marksman Malt | marksman | 2500 | yes | headshot pellets do 1.5x; a headshot kill pays 50 more (150) | `headshotDamage` 1.5, `headshotPoints` 50 |
| Bloodroot Tonic | bloodroot | 2000 | no | melee does double damage; every swing that hits a zombie heals you 30 | `meleeDamage` 2, `meleeHeal` 30 |
| Windfall Soda | windfall | 3000 | yes | each of your kills has a 6 percent chance of its own to drop a power-up, which may go up to 2 over the round's cap | `powerUpChance` 0.06, `powerUpExtra` 2 |
| Hammerhand Ale | hammerhand | 1000 | no | rebuild barriers 2.5x as fast; each board pays double (and the round's board cap doubles with it) | `repair` 0.4, `repairPoints` 2 |
| Salvage Stout | salvage | 2000 | no | your kills drop parts twice as often; a salvage pile gives you one part more | `partDrops` 2, `salvageBonus` 1 |
| Bulwark Brew | bulwark | 3500 | yes | an armour plate takes a whole zombie hit, then grows back 12 s later (its HUD badge goes grey meanwhile) | `plateSeconds` 12 |

**Seven more perks (developer request, 2026-10-27: "Add five to eight more perks into the zombie mode. And make machines
for them").** Each is about one thing the existing five don't touch, so no two overlap: moving (Fleetfoot), aiming
for the head (Marksman), melee and staying alive without a teammate (Bloodroot), the power-ups (Windfall), the boards
(Hammerhand), the weapon builder's parts (Salvage) and a flat hit blocker that's different from Ironhide Cider's cut
(Bulwark: one free hit every 12 s; with Ironhide Cider too, the hits that do land are cut). Ideas left out: faster
workbench / box / slot handling (the bench already pauses nothing and a box spin is short: too small to feel), bonus
slot odds (the slot machines' bad-luck protection and jackpot rounds already shape them; Salvage feeds the same
builder loop more visibly), explosive resistance (zombies don't explode and Zombies has no self-damage), a slide or
dodge (new movement code and animation for one perk), faster aiming (no aim-time stat in the builder yet), extra
magazine size (the builder's Feed parts own that), reviving from range (overlaps Mender's Mead). The cheap Hammerhand
sits in the start room for the early rounds; four of the seven need the power. The HUD shows every perk held as a
round badge with its own glyph (`"icon"`: heart, drum, rounds, cross, wind, chevrons, target, fang, star, hammer,
gear, shield; UiZombiesPerks.cs), and every machine has its name over it within 9 m ("NEEDS POWER" under it while it
waits). The perk flags went from 8 to 16 bits and a player's flags gained the plate bit (protocol 57). Self-test:
**"Zombies more perks"**.

**A machine for every perk (developer request, 2026-10-27: "make each of the perk machines more unique. They're
suffering from rectangle syndrome").** `tools/zombies/make_perk_machines.py` writes Props/Zombies/PerkMachines: one
prop per perk, found by key (`"jugg"` -> "Jugg Perk Machine"; a modded perk without one stands as the plain "Perk
Machine"), each its own silhouette on the theme of its effect: a riveted pressure tank (Ironhide Cider), a revolver
cylinder on end with a speed loader (Swiftload Seltzer), twin barrels with a cartridge belt (Twinfire Punch), a slim medical
column under a big plus sign with paddles (Mender's Mead), bellows under swept wings (Second Wind), a raked tower with
fins (Fleetfoot), a bullseye on an easel (Marksman), a gnarled stump on roots (Bloodroot), a gumball globe with a coin
dial (Windfall), a stack of planks with a hammer on top (Hammerhand), a scrap hopper on legs with a gear (Salvage) and
a heater shield under a helmet (Bulwark). They share one face where the game draws: the sign board at 1.62 m (the lit,
pulsing sign in the perk's colour), two lamps blinking in turn at its top corners, a glow strip under it, and the
dispenser below. All stay inside the plain machine's 0.9 x 0.7 m footprint. The same script reshaped the Power Switch
Box (a breaker panel with ceramic insulators; the lever and lamp stay where the game draws them). Without machine
props (Zombies Test) a machine's prop is drawn without collision.

On Zombies Test, Mender's Mead, Second Wind and Hammerhand (start room), Bloodroot and Salvage (West Wing) work without
power; the other seven need it (Fleetfoot, Marksman, Windfall and Bulwark in a row along the Hall's far wall).
A perk with `"selfRevive": true` is a self-revive perk (any perk can be made one in the rules file). **Second Wind
(decided 2026-10-26)**: a fixed 1500 every time (no rising price of its own), it counts as a perk for the doubling
below (it's a perk like the others), it's used up by the revive (and lost with the others on a down anyway).
Zombies don't attack downed players today, so a self-revive can't be "finished off" yet: being revived by a teammate
first also ends it (the perk is still used up). Mender's Mead lost its 500 solo price with its solo self-revive.

**More than 4 perks (developer answer, 2026-10-26).** There's no hard limit any more: holding 4 perks, the 5th costs
2x its listed price, the 6th 4x, the 7th 8x (each step doubles), for every perk including Second Wind (so a 5th-perk
Second Wind is 3000). The prompt shows the real price and the step ("Hold F to buy Second Wind [3000] (5th perk:
x2)"). Rules: `buy.fullPricePerks` (4; 0 = never dearer), `buy.extraPerkFactor` (2), `buy.maxPerks` (16 since
2026-10-27, at least every perk: a hard cap if a map ever wants one). Co-op bots pay the same prices. Self-test: "Zombies mode" ("perk price").

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
common gems read as coins; a part you already hold 3 of loose paid the coin payout instead (`dupePointsAt`, 0 since 2026-10-27: every copy is kept); a salvage
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
- **The box offer's last seconds are free for all (board item, 2026-10-27).** For the final `box.freeFraction` (0.3) of
  `offerSeconds` or the last `box.freeSeconds` (4), whichever is longer (`ZombiesRules.json`; both 0 turns it off), anyone
  can take the offered gun, for nothing; before that other players see "Reserved for <buyer>" and the key does nothing.
  In the window the box's glow turns orange and flashes, with a rim, a beam and a FREE FOR ALL label over it for
  everyone, and the prompt reads "Free for all - hold [E] to take ...". The buyer can still take it, or take it apart
  (which stays the buyer's alone). The server decides: the first valid take in a tick wins (players in id order), and
  everyone else's held key on the box is used up, so a closed box never turns their hold into a spin. If the buyer has
  left, the offer is free at once. Clients work the window out from ZWorld's box time (no wire change). Co-op bots take
  someone else's free offer only when they would have taken it as their own (it beats the gun in hand) and the box is
  within their shopping range; each bot looks at a free offer once.
- **At the mystery box: the OFFERED build (board bug fix, 2026-10-26).** The first version (earlier the same day) made
  G twice at the box take apart the **gun in your hands**; testers read the prompt as "take the box's gun apart" and lost
  the gun they were holding. Now, while the box is **offering you a build** (you spun it), a second HUD line under
  "Hold [E] to take the X" says "Press [G] twice to take the X apart into parts instead": **G** (the grenade key,
  rebindable as "Throw grenade (Zombies: take apart at the box)"; at the box's offer it throws nothing) **twice within 4 s**
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
  "casino": [ {"game": "blackjack", "at": [x, z], "yaw": 0, "power": false, "min": 50, "max": 1000} ],
  "traderSpots": [ {"at": [x, z], "yaw": 0} ],
  "machineProps": true,
  "testKit": true
}
```

- **Machine props** (`"machineProps": true`, milestone 3; open question 9): every machine stands as a **solid prop** from
  `Props/Zombies` (players, zombies, bullets and the bots' nav graph treat it like any solid prop): **the perk's own machine**
  ("Jugg Perk Machine": Props/Zombies/PerkMachines, see "Perks"; the plain **Perk Machine** for a perk without one, the
  game painting the perk's colour on its front; the game draws the lit sign and lamps on either), **Mystery Box Crate** (one at every box spot; the box itself
  is drawn a little bigger over the crate at the spot it's in), **Zombies Workbench**, **Power Switch Box** (the lever
  and lamp are drawn on it) and **Weapon Locker** (the slot upgrade; its screen is drawn on it) and **Reel Cabinet** (a slot machine; its reels, heat
  meter and topper lights are drawn on it). The section places them
  itself (they're not ops), facing the machine's `yaw`, so put each machine where its prop fits: a perk machine is
  0.9 x 0.7 m, a box crate 1.3 x 0.66, a workbench 1.6 x 0.8, the locker 1.0 x 0.6, the reel cabinet 0.9 x 0.7 and the power box 0.7 x 0.3
  (place it 0.2 m off a wall). They count towards the map's prop fingerprint. Without the key (Zombies Test) machines are drawn
  without collision, as before.

- **Test kit** (`"testKit": true`, a test map: Horde Test): the host's "Test kit" lobby setting works here (see "Horde
  Test, start round and test kit").

- **Crowd** (`"crowd": 10000`, the mass horde's dev map: Mass Horde Test): each round is that many zombies in three
  tiers: full zombies near the players, particles behind them, per-cell counts far out; drawn on the GPU, a crowd
  roar per cluster, and dense crowd is ground players stand on. Off on every other map. See [MASS-HORDE.md](MASS-HORDE.md).

- **Stress** (`"stress": {"staging": [[x, z], ...], "rally": [x, z], "countdown": 10, "gather": 0.75, "gatherSeconds": 300,
  "waveSeconds": 60}`, with `"crowd"`; the horde stress map: Horde Stress): each round's crowd (the host's lobby size,
  else `"crowd"`) waits on the staging areas (1-6) for the countdown, descends to the rally point, merges there and
  advances on the players; endless waves on request. See [MASS-HORDE.md](MASS-HORDE.md), "Horde Stress".

- **Areas** are rectangles, optionally with a height band and extra boxes, for maps with floors over each other:
  `{"name": "Crypt", "rect": [x0, z0, x1, z1], "y": [y0, y1], "more": [[x0, z0, x1, z1, y0, y1], ...]}`. A point is in
  the area when it's in the rectangle and the band (`"y"`, at least 2 m tall; leave it out: any height) or in one of the
  `more` boxes (a stairwell, an alcove). Where areas overlap the first one listed wins, so list an upper floor before the
  hall under it. A door's two sides stand on the door box's floor when its bottom is over 0.3 m above the ground (a door
  on an upper floor or in a cellar). Zombies only count a door side as reached on its own floor (within 1.2 m of its
  height).
- The **area graph** is made by the doors: an area is unlocked when it's the start area or
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
Mender's Mead), the **Hall** behind a door (window, wall pump, Ironhide Cider, a box spot, salvage), the **West Wing**
behind debris (window, a ground spawner, the wall rifle for ammo, Swiftload Seltzer, a box spot) and the **Power Room**
behind a door (window, the power switch, Twinfire Punch, the slot upgrade, a workbench, salvage, the Burst Marksman), plus
a shutter between the Hall and the Power Room that needs the power.

## Blackmoor Asylum (milestone 3, the Zombies pack)

`Maps/Zombies/01-BlackmoorAsylum.json`, written by `tools/zombies/make_blackmoor.py` (the script also checks its own
layout: props keep clear of doors, windows, spawners, machines, wall buys and each other). An abandoned village asylum
and its research wing at night, about 68 x 62 m inside a walled, wobbly polygon outline, with terrain (rough ground
round the ring, hills beyond the wall, a mound in the courtyard), grass and a few trees, a night sky with moonlight and
the ambient raised so it stays readable, flickering caged lamps, tube lights and lanterns, grime, damp and blood decals.

| Area | Way in (price) | What's there |
| --- | --- | --- |
| **Receiving Hall** (start) | | 3 windows onto the ambulance bay, Mender's Mead, Second Wind and Hammerhand Ale (no power), the Wall Rifle and the Buzzbox Machine Pistol (600), box spot 1, **slot machine 1**, a reception desk, the **high-low table** |
| **East Ward** | the Ward Door (750) | 2 windows, Ironhide Cider (power), the Wall SMG, the Kingpin Hand Cannon (750) and the Ratchet PDW (1100), salvage, hospital beds |
| **West Offices** | the Records Debris (750) | 2 windows, Swiftload Seltzer (power), Bloodroot Tonic, the Wall Pistol, the Courier Carbine (900) and the Prairie Lever (1000), box spot 2, **workbench 1**, desks and filing, the **blackjack table** |
| **Courtyard** (open sky) | the Courtyard Gate from the ward (1000) or the Garden Debris from the offices (1250) | 3 boarded gates, 2 ground spawners among the graves, the Wall Pump, Riot Control (1250) and the Heavy Hitter (1400), box spot 3, salvage, Salvage Stout, a fountain |
| **Generator House** | the Generator Door from the courtyard (1250) | 2 windows, **the power switch**, **workbench 2**, the Burst Marksman, the Elephant Gun (1750) and the Bastille Squad LMG (2000), salvage, Fleetfoot Fizz and Windfall Soda (power), generators, the **dice table** |
| **Chapel** | the Chapel Door from the offices (1500) | 2 windows, a ground spawner, Twinfire Punch and Bulwark Brew (power), the Wall SMG, the Reaper Marksman (1500) and the Auto Shotgun (1750), box spot 4, pews and an altar |
| **Research Lab** | **the Lab Shutter** from the ward (750, **needs the power**) | 3 windows, a ground spawner, the **weapon-slot upgrade**, **slot machine 2** (needs the power), Marksman Malt (power), the Wall Pump and the Bucket Lobber (a grenade launcher, 2250), box spot 5, salvage, consoles, the **roulette table** |

Seven areas (six to unlock), 17 boarded windows, 21 spawners (4 in the ground), 19 wall buys (2026-10-27: twelve more, from 600 to 2250, most of them box guns tagged `"wall"` too), 5 box spots, 12 perk
machines (7 on the power), 2 slot machines (1 on the power), every machine a solid prop. Two routes to the courtyard make a loop through the middle of
the map (ward - courtyard - offices) for training zombies. The self-test walks a zombie in through every window and
runs zombies from every area to the far end through every door (none may get stuck), and measures the AI cost there.

## Ashgrave Cathedral (the Zombies pack, board item zc01, 2026-10-27)

`Maps/Zombies/02-AshgraveCathedral.json`, written by the C# map kit generator `AshgraveCathedral`
(`--gen-map "Ashgrave Cathedral"`; it checks its own layout: machines inside their area, nothing on a door, window,
spawner, stair or wall buy, every slot category present). An original gothic cathedral and its precinct at night, 128 x
112 m (the precinct about 104 x 60), on five levels: the dungeons (2 m), the crypt and ossuary (7), the ground (12),
the galleries and organ loft (18) and the roof walk and ringing chamber (22.8), with the belfry at 30. (The whole map is
built at ground 0 and lifted 12 m, because a map without terrain keeps players above -5 m and counts anything under the
ground as fallen.) Architectural detail (board item cd01, 2026-10-28; `AshgraveCathedral.Detail.cs`): responds at a bay
rhythm carry quadripartite rib vaults with bosses in the nave, aisles, transept, choir, Lady Chapel, crypt and ossuary;
plinths, string courses and cornices divide the walls; blind arcades, niches (saints, candles, reliquaries), wall tombs,
plaques and banners fill the bays; the windows get frames and surrounds; the vestry, chapter house and dungeons get
timber beams or coffers and transverse arches; a chequered band runs up the processional way, ledger stones lie in the
floors. All of it is looks only (the props in `Props/Gothic`, ribs and mouldings as meshes) except three wall tombs
placed through the layout check; the nav graph is unchanged. Plan PNGs per level: `--gen-plan` with `--level y0,y1` (docs/mapkit/MAPKIT.md).

| Area | Way in (price) | What's there |
| --- | --- | --- |
| **Nave** (start) | | 4 windows, Mender's Mead, Second Wind and Hammerhand Ale (no power), the Wall Rifle, Wall Pistol and Buzzbox, box spot, **slot machine (classic)**, the **high-low table**, pews, confessionals |
| **Organ Loft** (galleries over the aisles and the west end) | the Loft Stair debris from the nave (1000) | a ground spawner, Twinfire Punch (power), the Courier Carbine and Prairie Lever, box spot, the organ |
| **Transept** (the crossing) | the Rood Screen debris (1000) | 4 windows, the Wall SMG, Kingpin and Wall Pump, box spot |
| **Choir** | the Choir Screen (1250) | 2 windows, Ironhide Cider (power), the Ratchet PDW and Riot Control, box spot, the high altar; the crypt stair |
| **Lady Chapel** | the Reliquary Grate (1500, **needs the power**) | 3 windows, Bulwark Brew (power), the Bastille LMG, box spot |
| **Vestry** | the Vestry Door from the choir (1000) | a ground spawner, Swiftload Seltzer (power), the Heavy Hitter, **Ammo and Feed slot machines**, the **blackjack table** |
| **Bell Tower** | the Tower Door from the nave (1250) or the Roof Door from the roof walk (1500) | stairs up the inside (five flights), 2 ground spawners, the Elephant Gun; the ringing chamber: Marksman Malt (power), the **roulette table**; the belfry: **the great bell** |
| **Roof Walk** (over the north aisle) | the Roof Door (1500, from the tower) | 2 ground spawners, Windfall Soda (power), the Reaper Marksman, box spot |
| **Cloister** (open sky) | the Cloister Door from the nave (750) | 2 ground spawners, a well, three cheap wall guns, box spot, salvage |
| **Chapter House** | the Chapter Door from the cloister (1250) | a ground spawner, Salvage Stout, the Wide Load, **workbench**, **Optic, Utility and Upgrade slot machines** |
| **Tithe Barn** | the Barn Door from the cloister (1000) or the Barn West Door from the close (750) | 3 windows, the High Rider and Coach Gun, box spot, **workbench**, salvage, **Muzzle, Stock and Grip slot machines** |
| **Close** (the west precinct, open sky) | the West Doors from the nave (750) | 5 windows (gates), Fleetfoot Fizz (power), the Burst Marksman, box spot, salvage |
| **Lodge** | the Lodge Door from the close (1000) | 2 windows, the Wall SMG, **workbench**, **Receiver, Action and Barrel slot machines** |
| **Graveyard** (open sky) | the Sexton's Gate debris from the vestry (1250) or the Chapter Garden Door (1500) | 5 windows, 2 ground spawners among the graves, the Bucket Lobber and Auto Shotgun, box spot, 2 salvage, the **dice table** |
| **Mausoleum** | the Mausoleum Gate (1750) or the Tomb Stair from the tunnel (1000) | a ground spawner, the **weapon-slot upgrade**, the **grenade slot machine**, sarcophagi |
| **Crypt** (under the choir) | the Crypt Door at the foot of the crypt stair (1250) | 2 ground spawners, **the power switch**, Bloodroot Tonic, the Ratchet PDW, box spot, **workbench** |
| **Ossuary** (under the crossing) | the Bone Pile debris from the crypt (1250) | a ground spawner, the Wall Pump, box spot, **Head, Hilt and MeleeMod slot machines**, the dungeon stair |
| **Dungeons** | the Dungeon Gate from the ossuary (1500) | cells, a guard room, 2 ground spawners, the Wall Rifle, box spot, **workbench**, salvage |
| **Inquisitor's Chamber** | the Inquisitor's Door (1750) | a ground spawner, the Kingpin, **Multiplier and Splitter slot machines**, a rack and cages |
| **Smugglers' Tunnel** | the Tunnel Debris from the dungeons (1500) | a ground spawner, salvage; up to the Tomb Stair |

Twenty areas, 22 doors and debris (750 to 1750, one on the power), 28 boarded windows, 75 spawners (19 in the ground,
on every level), 13 box spots, 28 wall buys, 12 perk machines, 18 slot machines (every category, the grenade one too), 4
casino tables, 5 workbenches, 7 salvage piles, every machine a solid prop; 37 pack props (`Maps/Zombies/Props/Cathedral`:
pews, altar, organ, font, confessionals, statues, gargoyles, sarcophagi, ossuary walls, cells, the great bell...).
Rings: nave - cloister - barn - close - nave on the ground, and choir - vestry - graveyard - chapter house - cloister. Stairs
are flights of at most 11 steps with landings between (the nav graph links a flight's foot to its top and can't stand a
node on a tread), and stairwells have walls or tall parapets, not rails a zombie would try to vault. The nav graph is
built with the doors shut, so its rooms are pockets (the map's test block sets `navPockets`).

### The bell: a siege up the tower

Ringing the great bell (the button at its rope, in the belfry; free, a 240 s cooldown, during a round) starts a
**survival challenge**, not a purchase: a 75 s challenge for everyone standing in the belfry, and the **climb**
(`zclimb` map logic, `Source/Game/ZombiesClimb.cs`). The round gains `extraZombies + extraPerRound x round` zombies (at
most `extraMax`). Zombies at the windows leave them and walk round to the foot of the tower's north face, where they
pile up against the wall into a living pyramid, a layer at a time (`layerHeight` apart, `columnSpacing` apart, each
layer `layerShrink` places narrower than the one under it, the bottom `baseRows` layers two deep, at most `layers` high);
the rest climb the pile and then the bare wall above it (one in `climbEvery` goes straight up the wall from the start),
vault the belfry's parapet and chase whoever is there. A shot body lets go and falls (pushed out `fallPush` m, dazed
`fallStun` s where it lands) and the bodies above it slide down a place, so shooting the pile's base holds the climb
back. When the challenge is survived every player standing gets `rewardPoints + rewardPerRound x round` points (at most
`rewardMax`), `rewardParts` loose parts rolled as if `rewardRoundBonus` rounds later, and `rewardPowerUp` drops on the
belfry; when it ends either way the climb stops (whoever is on the face falls, the rest go back to the windows).

Server-authoritative and cheap: the climb keeps one list of climbers and their places (no per-tick allocation; 0.1 ms
a tick with 60 on the face in the self-test). Clients get nothing new but the zombie state: `Scaling` (state 7) with the
position, and they draw a braced or climbing pose from the vertical motion. Tuning: the rules file's `"climb"` section.

| Key | Default | What |
| --- | --- | --- |
| `layerHeight`, `columnSpacing`, `layers`, `baseRows`, `layerShrink` | 1.3 m, 0.75 m, 10, 1, 2 | the pyramid's shape |
| `climbEvery` | 3 | one in this many arrivals climbs straight away instead of joining the pile |
| `settleSpeed`, `pileClimbSpeed`, `wallClimbSpeed`, `vaultSeconds` | 3, 2, 0.8 m/s, 0.8 s | how fast bodies settle into place, climb the pile and the wall, vault the parapet |
| `fallStun`, `fallPush` | 1.2 s, 2.5 m | a shot climber's fall |
| `extraZombies`, `extraPerRound`, `extraMax` | 12, 2, 80 | the siege's extra zombies |
| `rewardPoints`, `rewardPerRound`, `rewardMax` | 1500, 100, 6000 | the points each survivor gets |
| `rewardParts`, `rewardRoundBonus`, `rewardPowerUp` | 2, 10, `maxAmmo` | the parts and the power-up |

Self-test "Zombies cathedral": the layout (every machine in its area and on its floor, the slot categories, the casino
games, wall-tagged wall buys), a zombie in through every window, zombies from every area to the belfry and to the
inquisitor's chamber with every door open (none may get stuck), the siege at round 10 (the climb starts and adds
zombies, the pyramid forms, a shot body falls and the one above moves down, someone comes over the parapet, the reward is
paid, the climb stops cleanly, tick time and allocations, a client reads a climber back) and the siege's replay digest.

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
  part won floats over the cabinet), then the machine takes the next pull. **No pull pays nothing** (board item,
  2026-10-27): a blank (skulls, odd gems) pays a **consolation part** of the machine's own category (the classic machine:
  any part) and still frees the machine the moment the reels stop: no result time and no cooldown, so you can pull
  again at once. It shows as a small win: "Consolation: <part>" over the machine for everyone (a smaller model, a brief
  soft glow on the reels) and in your own feed. Self-tests "Zombies slots" and "Zombies slot consolation". One player at
  a time per machine.
- **Consolation parts.** Their rarity ignores the round's table (so commons stay available in late rounds when the
  main table no longer rolls them) and follows the **bet level** (`slots.consolation`, weights per rarity per level):
  **1x** common; **2x** common 60 / uncommon 40; **5x** uncommon 85 / rare 15. A consolation is never rarer than the
  main roll's lowest part tier at that bet (the round's row with the bet's steps, no heat): any weight above it pays that
  floor. A consolation **never resets the heat**, even a rare one (it isn't a pity-tier win): it adds 1 like any other
  pull. The real win odds, jackpot rounds and the coin payout are unchanged.
- **What the reels mean.** Symbols: a **skull**, a **coin** (points back) and a **gem** per rarity in its colour (the
  wonder gem only on jackpot rounds; the scrap cog went with scrap, 2026-10-27). **Three of a gem**: a loose part of
  that rarity; **two of a gem**: a part one tier lower (two commons: coins); otherwise a coin pays **1.5 x the price
  back** in points, and skulls and odd gems pay a **consolation part** (above). The server rolls the outcome first and then picks reels that read
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
  a part straight onto a gun) from the same pool as zombie part drops. **Every copy is kept** (board item, 2026-10-27:
  `dupePointsAt` 0): there is no cap on loose parts anywhere (the server, `ZRun` since protocol 71, the workbench's "x N"
  counts), so you can bank as many as you like; set `dupePointsAt` above 0 to pay the coin payout (`pointsBack` x the
  price) for a part you already hold that many of. A legendary or wonder part is announced to everyone.
- **Rarity rising: three levers that stack.**
  1. **By round** (`DropTables.json` "slotTiers", rows from round 1, 5, 10, 15, 20, 25, 35): weights for nothing,
     points and each part tier (no scrap since 2026-10-27: its weight went to points; "nothing" is the blank, which pays a consolation part); the share of blanks falls (30 -> 20 percent) and the part mix climbs (round 1: mostly
     common and uncommon, 2 percent rare or better; round 36: about half rare or better).
  2. **Machine heat** (the pity timer): every pull that doesn't pay a **rare-or-better** part (`pityTier`) adds 1 heat;
     every **5** heat (`heatStep`) moves the part weights **one tier up** (at most 3 steps, `heatMaxSteps`); a
     rare-or-better part resets it to 0. Heat belongs to the machine (shared by the team, so a cold machine is a choice).
     The cabinet shows it: a 5-segment meter under the reels fills towards the next step (yellow, orange, red; flashing
     at the top), and the topper's lights chase faster as it warms up.
  3. **Jackpot rounds** (every 5th round, `jackpotEvery`): the **wonder gem** goes on the reels and legendary (+6) and
     wonder (+4) weights are added; the topper flashes gold and pink and the prompt says "JACKPOT ROUND". Outside
     jackpot rounds wonder parts never come up from the slots.
  4. **Each row's top tier** (the late-game pass, 2026-10-27: `"top"` in a `slotTiers` row, default wonder): the
     rarest tier that row pays. Heat, bets and the jackpot weights that would go past it pay the top tier instead, so the
     slots pay epic at most before round 20 and legendary at most before round 25 (wonder from the jackpot round 25),
     however hot the machine or big the bet (WEAPON-BUILDER.md, "The power curve").
- **Bets (developer answer to question 13, 2026-10-26).** At an idle machine the **second key** (G, the Grenade key:
  it throws nothing at a machine) moves its bet up a level, wrapping round: **1x / 2x / 5x** the current price
  (`slots.bets`). A pull then moves the part odds `slots.betSteps` (0 / 1 / 2) rarities up the ladder, on top of the
  machine's heat (the same shift as a heat step). The nothing and coin weights don't change, so a higher bet
  **never guarantees a big win**: skulls (consolations) come up as often at 5x as at 1x (round 3: 28.6 percent at every bet; the part
  mix goes from 1.9 percent rare-or-better at 1x to 12 at 2x and 33 at 5x). Coins pay 1.5x what you paid. The bet
  belongs to the machine (shared by the team, like the heat) and stays until someone changes it; the prompt shows it
  ("Hold F to pull the slot machine [1100] - bet 2x (G to change)") and so does the cabinet (a lamp per level under the
  heat meter: green, gold, red). Server-authoritative: a client asks with `Msg.ZBench` kind 4 (the machine in its slot
  byte); the server checks you're at the machine and it's idle. Bots pull at whatever bet is set. Self-test "Zombies
  slots": the rarer mix and the unchanged blanks at every bet over seeded rolls, the price, the prompt, the wrap and the
  client copy.
- **Bad-luck protection**: was at most **3** "nothing" results in a row per player per machine (`maxNothing`). Unused
  since consolation parts (no pull pays nothing); the key stays so it can come back.
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

- **Which categories.** The sixteen weapon-builder part categories (`PartCategory`): Receiver, Barrel, Splitter, Feed,
  Ammo, Stock, Grip, Optic, Muzzle, Utility, Receiver Upgrade, Damage Multiplier, Hilt, Head, Melee Mod, Action, and
  the **grenade parts machine** (`"category": "grenade"`, 2026-10-27: it pays any casing, fuse, payload or grenade mod;
  the four grenade categories share it, `ZombiesMap.SlotCategories`; its cabinet is a giant ribbed hand grenade). A
  map's slot entry names one with `"category"` (the enum name or the display name, case-insensitive; an unknown one
  refuses the map); without it the machine is the **classic** one and pays any part, as before. Blackmoor Asylum has
  its grenade parts machine on the chapel's west wall, Zombies Test on the Arcade's west wall, Horde Test at the end of
  the Armoury's row.
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
  gems, 8 blade); protocol 71 sets bit 6 of the result byte (and bit 1 of `ZSlotWin`'s flags) for a consolation part
  (result 2) and widens `ZRun` (a u16 count of kinds, a u32 count each). Clients start the reel
  animation when the spin counter changes (worked back from the seconds left, so a joiner mid-spin lines up); the parts
  come in `ZRun` as for any payout. Bots pull now and then with plenty to spare (see "Co-op bots"). Nothing in the machines' tick allocates
  (self-test).

| Rules (`ZombiesRules.json` "slots") | Default | What |
| --- | --- | --- |
| `costBase`, `costPerRound`, `costMax` | 500, 25, 1250 | the price: base + per round after the first, capped |
| `spinSeconds`, `showSeconds` | 2, 2.5 | the spin, and how long a win shows (a consolation frees the machine at once) |
| `heatStep`, `heatMaxSteps`, `pityTier` | 5, 3, rare | heat per tier step, the most steps, the tier that resets heat |
| `maxNothing` | 3 | unused since consolation parts (was: blanks in a row per player per machine, at most) |
| `consolation` | [{common 100}, {common 60, uncommon 40}, {uncommon 85, rare 15}] | a blank's consolation part: rarity weights per bet level (never above the main roll's floor at that bet) |
| `jackpotEvery`, `jackpotLegendary`, `jackpotWonder` | 5, 6, 4 | jackpot rounds and their extra weights |
| `pointsBack`, `dupePointsAt` | 1.5, 0 | points back (x the price) for a coin; a part you hold this many of loose pays that instead (0: keep it) |
| `bets`, `betSteps` | [1, 2, 5], [0, 1, 2] | the bet levels' price multipliers and their rarity steps |
| `winSeconds` | 4 | how long a win shows over the machine |
| `machines.<Category>` `price`, `melee` | see above | a category machine's price (x the classic) and whether it has the blade |
| `DropTables.json` `slotTiers` `melee` | 5 | the weight of a complete melee build (its tier from the row's part weights) |

## The trader (board item dev-zombies-trader-tr01, 2026-10-28)

A wandering parts dealer, filed by the developer: "a trader NPC that appears at the start of every 5th round at one of
the map's trader spots ... with a rotating selection of parts from the rarity tiers below what the round's tables now
favour, plus a few consumables; server-authoritative purchases". Code: `Source/Game/ZombiesTrader.cs` (the visits, the
shelf, purchases, the wire), `Source/UI/UiTrader.cs` (the shelf panel and the waypoint label), `Source/App/Program.ZombiesTrader.cs`
(the figure and his stall); rules `Parts/Tables/ZombiesRules.json` "trader"; self-test section **"Zombies trader"**.

- **Visits.** At the start of every `everyRounds`-th round (5, 10, 15, ...) he sets up at one of the map's
  `traderSpots`, picked at random (an open area's first; a map without any uses its box spots and workbenches: he
  stands 1.4 m in front of one, his back to it). Everyone gets a feed line ("The trader has set up his stall in the
  Nave: 8 parts up to Uncommon. He leaves when round 6 starts."), a sound, the banner THE TRADER IS HERE and a
  **waypoint**: a gold "TRADER 34 m" label over his stall through the walls (a HUD line with the distance when he's
  off screen) and a thin gold beam over him, brighter in his first seconds. He stays `stayRounds` rounds (each with the
  break after it: the default 1 means the round he came in and the break after it) and packs up when the next round
  begins ("The trader packed up his stall and left.").
- **The look.** Original: a tall figure in a long teal coat and a wide-brimmed hat, a grey beard and a red scarf,
  behind a wooden counter with his wares on it (a small box per shelf item in its rarity colour, flat when sold) and a
  lantern on a pole at the counter's end. No brands, no borrowed faces. He's drawn, not a body: **zombies take no
  notice of him and nothing can hurt him** (he isn't solid either, so he never blocks a doorway).
- **No purchase limit** (developer change, 2026-10-28: "the trader is a chance to buy lots of lower-level components
  for silly builds"). Every item on the shelf can be bought **any number of times by anyone**: nothing sells out and
  the shelf never changes during a visit.
- **The shelf.** Walk up ("Press F to see what the trader has") and press the interact key: his shelf opens on the
  right with the mouse free, over the match (the world goes on, as at a casino table; Esc, Back to the fight or walking
  away closes it). Each row: the part in its rarity colour (and how many you hold), its rarity and category, the price
  of one (red when you can't afford it) and **x1 / x5 / x10** buttons (a lot you can't pay for in full is greyed out;
  `maxPerBuy` 10 is the most in one purchase). A **fresh shelf every visit** of `stock` (14) **parts, wide and low**:
  the categories (every part category, grenade parts as one) in a shuffled order, **one part each** while there are
  categories left, each part's rarity rolled from `tierWeights` (common 60, uncommon 32, rare 8) up to the shelf's
  **top tier**: the tier the round's `partDrops` row favours (its heaviest weight; a tie goes to the rarer) less
  `tiersBelow` (1), kept between `topMin` (uncommon) and `topMax` (rare). So the shelf is commons and uncommons up to
  round 24 and mostly commons and uncommons after (a few rares); a category without the rolled tier gives its nearest
  lower one, no part twice. With `extraChance` (0.35) he also has **one extra part a tier above the top** (at most
  `extraMax`, rare): the occasional rare. Then the **consumables**, one at a time: a **grenade refill** (the round's
  count, only when you're short; `grenadePrice`) and a **full reserve for every gun** (what Max Ammo gives; `ammoPrice`).
  **Prices** are `prices[rarity]` (250 / 500 / 1000 / 2000 / 4000 / 8000) marked up `pricePerRound` (3 percent) a round
  after round 1, to the nearest 10; the shelf is priced for the round he came in. Bought parts go **straight to your
  loose parts** (no cap: unlimited banking) for the workbench.
- **Server-authoritative.** A click asks (`Msg.ZTraderBuy`: the item, the visit it was seen on and how many); the
  server checks he's here, it's the same shelf, the count is 1 to `maxPerBuy`, you're alive and within `useRange`
  (2.2 m), and you can pay **for the whole lot** (else nothing is taken), then takes the points and adds the parts.
  `Msg.ZTrader` (here or gone, the visit, where he stands, the round he leaves, every item's kind, part and price) goes
  to everyone when he comes or goes, and to a joiner; a purchase changes nothing on it (your points go out in `ZWorld`,
  your parts in `ZRun`). Two players buying the same thing in one tick both get it and both pay. Nothing allocates per
  tick.
- **Co-op bots** (ZombieBotsShop.cs, after the slot upgrade and before the box): while he's here, a bot still buys
  **one part a visit** (people have no limit), the rarest it can afford with `botSurplus` (1500) over its reserve plus the price, walking to his counter
  when it's safe like any other purchase (0: bots never shop at him).
- **Maps.** `"traderSpots": [ {"at": [x, z], "yaw": 0}, ... ]` in the zombies section (at most 32): open floor with room
  for his 1.8 x 1.6 m stall (his counter is in front, -Z before the yaw) and the queue. Blackmoor Asylum has one per
  area (seven; `tools/zombies/make_blackmoor.py` checks them like machines), Ashgrave Cathedral eight on every level
  (`Source/MapKit/Generators/AshgraveCathedral.cs`, `TraderSpots()`).

## Casino tables (board item "Zombies: casino games to bet points", 2026-10-27)

"Add gambling for points to Zombies: a few casino games where you bet your points and win more back or lose your bet,
more interactive than a single button press." Developer's note: **points only** (the run's currency; no real money and
no progression stakes). Code: `Source/Game/Casino.cs` (the maths), `Source/Game/ZombiesCasino.cs` (the server's
tables), `Source/Game/ZombiesCasinoNet.cs` (the wire), `Source/UI/UiCasino.cs` (the panel and the labels),
`Source/App/Program.ZombiesCasino.cs` (cards, wheel, chips and dice on the tables); props
`Props/Zombies/Casino` (written by `tools/zombies/make_casino_tables.py`); self-test section **"Zombies casino"**.

- **Playing.** Walk up to a table ("Press F to play Blackjack (bets 50-1000 points)") and press the interact key: the
  table's panel opens on the right with the mouse free, over the match. **The world goes on**: zombies keep coming and
  can hit you while you play (you don't move while the panel is open). Esc or "Back to the fight" closes it. Choose
  your bet with the chip buttons (+10, +100, +500, Min, Max) within the table's limits and the points you have; points
  are taken when the bet goes down and **never go below zero** (a bet you can't cover is refused and nothing is taken).
- **Blackjack** (one dealer, up to **four seats**: one per player): bet to sit down; the deal waits 5 s after the first
  bet for others (or deals at once with every seat taken); a bet before the deal can be taken back. Then **hit, stand
  or double** (double: on your first two cards, the bet again for exactly one card). The dealer **draws to 16 and
  stands on every 17** (soft 17 too), peeks with an ace or a ten up (its natural ends the hand at once), and
  doesn't draw when every hand is bust or a natural. A **natural pays 3:2**, a win 1:1, a push gives the bet back. No
  splits or insurance (kept simple). A hand still deciding after 20 s stands. The dealer's hole card goes out to
  clients face down until it's turned (it can't be read off the wire). House edge: about half a percent to a few
  percent depending on how you play (the self-test's draw-to-17 player loses 5 percent).
- **Roulette** (a **single zero**, 37 pockets; everyone at the table bets on one spin): put chips on a **number**
  (35:1), a **split** of two side-by-side numbers (17:1; tick "Split" and click both), **red / black**, **odd / even**,
  **1-18 / 19-36** (1:1), a **dozen** or a **column** (2:1); the zero loses every outside bet. The first chip starts a
  12 s betting clock (take your chips back before the spin if you like), then the wheel spins 6 s and the ball lands
  in the pocket the server rolled at the start of the spin. Every bet keeps exactly 1 in 37 (2.7 percent) for the
  house. The table's limit is a player's **total on one spin**. Chips ride the spin even if you walk off.
- **High-low** (one player at a time): bet and a card is dealt; guess whether the next is **higher or lower**
  (an endless deck: every rank 1 in 13; **a tie loses**). Each right guess multiplies the pot by **(1 - 5 percent)
  over the guess's chance** (rounded down): from a two, "higher" pays x1.02; from a seven, either way x1.76; "higher"
  on an ace (or "lower" on a two) can't be made. **Cash out** whenever you like; after 6 right guesses the table cashes
  you out; a hand left for 30 s cashes out too.
- **Bone Dice** (the pass line, one shooter at a time): bet, then **roll**: a first roll of 7 or 11 wins, 2, 3 or 12
  loses, anything else is **the point**: roll it again before a 7 to win (1:1). It wins exactly 244 hands in 495 (a
  1.41 percent house edge). Dice left for 30 s roll themselves.
- **Walking away forfeits.** Go more than 4 m from a table (`leaveRange`), go down or leave with a blackjack, high-low
  or dice hand unfinished and its bet is lost (a blackjack bet before the deal comes back instead). Roulette chips ride.
- **Everyone sees the table.** Cards, chip stacks (a colour per player), the turning wheel and ball, the tumbling dice
  and the point puck are drawn on the table for everybody; a label over it within 10 m names the game, its limits and
  what's on it (the hands' totals, the pocket, the pot, the point). A win of **1000 points or more over the bet**
  (`announceAt`) is announced to everyone: a gold line in the feed, a sound at the table and "X won N points!" over it
  (within 25 m), like a slot machine's big win.
- **Server-authoritative and seeded.** Each round (a deal, a spin, a hand) is seeded from the run's seed, so the same
  run seed and the same moves give the same cards; the self-test checks the payout maths exactly. The tables' tick
  allocates nothing. **Bots ignore the tables** (they never bet).
- **Network (protocol 66).** `Msg.ZCasino` (server to everyone, reliable, on a change and to each joiner): a table's
  whole state (game, phase, round, tenths of a second left, the last big win, every seat's player, bet, flags, outcome,
  payout and cards, the dealer's cards with the hole card hidden, the pot and guesses, the dice, point and roll
  counter, the pocket, every chip). `Msg.ZCasinoAct` (client to server): table, act (bet, hit, stand, double, higher,
  lower, cash out, roll, clear, leave), arg (a roulette spot: 0-36 numbers, then 24 splits across, 33 down, red,
  black, odd, even, 1-18, 19-36, the dozens, the columns) and amount; the server checks everything again.
- **Map logic.** A table is a `zmachine` with `"machine": "casino"` (Maps/README.md "Map logic"): it sends `used` on
  every bet that goes down, and `disable` closes it ("Locked"; no bets) until `enable`.

**Placing tables (for a map generator: the Cathedral and later maps).** Add a `"casino"` list to the zombies section:
`{"game": "blackjack" | "roulette" | "highLow" | "dice", "at": [x, z], "yaw": degrees, "power": false, "min": 50,
"max": 1000}` (`min` / `max` optional: the rules' limits for the game; at most 8 tables). With `"machineProps": true`
each table stands as its solid prop ("Blackjack Table", "Roulette Table", "High-Low Table", "Dice Table"), placed by
the game from the zombies section: don't add a Prop op for it. Players stand on the table's front, which faces -Z
before the yaw (yaw 0: the players on the -Z side, yaw 180: on +Z), so leave about 1.5 m clear there. Footprints
(x by z before the yaw, from the spot): blackjack 1.95 x 1.25 (z -0.79 to 0.46: its curved front reaches further
forward), roulette 2.6 x 1.15, high-low 1.35 x 1.35 (its sign is 2.2 m tall at the back), dice 2.05 x 1.15. A table
is played within 2.2 m of its spot (`useRange`), so keep other machines' spots more than that away or the nearest wins
the prompt. In a C# map kit generator, add the entries to the zombies section's `casino` array like the `slots`
entries (`Source/MapKit/Generators/HordeTest.cs` shows the slots). **Themed rooms**: give each table a room that
suits it, as Blackmoor Asylum does: **high-low** at the night porter's desk in the Receiving Hall (the start room: a
cheap early game), **blackjack** on the staff card table in the West Offices, **roulette** in the Research Lab (the
"probability experiment", behind the power) and the **dice** tub with the stokers by the Generator House turbine. For
the Cathedral: a card table in the vestry or the sacristy (blackjack), the wheel under the bell tower or in the crypt
(roulette), high-low by the confessionals, dice among the gravestones' masons' tools.

| Rules (`ZombiesRules.json` "casino") | Default | What |
| --- | --- | --- |
| `blackjack`, `roulette`, `highLow`, `dice` `min` / `max` | 50-1000, 10-1000, 50-500, 50-1000 | each game's limits (roulette: a player's total on one spin); a table's own `min` / `max` win |
| `betStep` | 10 | every bet is a whole number of these |
| `dealSeconds`, `turnSeconds` | 5, 20 | blackjack: the wait for other seats after the first bet; the time to act (then you stand) |
| `rouletteBetSeconds`, `spinSeconds` | 12, 6 | roulette: betting after the first chip; the wheel's spin |
| `actSeconds` | 30 | high-low and dice: the time to act (then high-low cashes out, dice roll) |
| `showSeconds` | 4 | how long a result shows before the table opens again |
| `useRange`, `leaveRange` | 2.2, 4 | how near you play from; how far an unfinished hand may wander before it's forfeit |
| `highLowEdge`, `highLowMaxSteps` | 0.05, 6 | high-low's edge on every guess; the most guesses in a hand |
| `announceAt` | 1000 | a win this far over the bet is shown to everyone |

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

## Dropping things (board item "drop items", 2026-10-27)

The developer's note: a drop key drops the held build as a world pickup with its own model, anyone takes it with the
use key (swapping if their slots are full); loose parts drop from the part inventory (the workbench and a quick
inventory view, picking part and amount) as small labelled pickups with a rarity glow. Code: Source/Game/ZombiesDropItems.cs,
Source/UI/UiZombiesInventory.cs; self-test "Zombies drop items".

- **Drop item (T, rebindable; G stays the grenade key)** drops the gun in your hands as a dropped build (above: its own
  model over a gold ring, a world label, hold F to pick up, a swap if the picker's slots are full). The next gun comes
  up. Only a build drops (not empty hands), and **never your last gun** (proposed default: "can't drop your last weapon";
  the developer may want to allow it). The melee build and the grenade build don't drop with the key (proposed).
  The dropped gun comes back with a magazine and the picker's slot reserve, like a gun left by a swap (no free ammo).
- **Parts inventory (I, rebindable)** opens a small window over the match (the mouse is free; I or Esc closes it):
  your loose parts in rarity colours; click one, pick how many (1 to `playerDrops.maxPartsPerDrop`, 50) and DROP. The
  workbench's Loose parts list has the same picker. The parts land about 0.7 m in front of you (placed like dropped
  builds: never in a wall, in reach, in your area) as **one pickup** holding that many: the part's own boxes, small and
  turning, over a ring of dashes in its rarity colour with a faint beam.
- **Picking up**: hold F (as a buy). Parts go to your loose parts; the dropper hears who took them. In a pile, the
  pickup **you look at** comes first. Looking at a pickup (within 6 m) or standing by it names it with its rarity,
  its owner and how long it stays.
- **How long**: things dropped on purpose last **to the end of the round after the one they were dropped in**
  (dropped in round 3 or in the break after it: gone when round 4 ends) or `playerDrops.seconds` (600 s), whichever is
  first (`playerDrops.rounds: 0` keeps only the time). They blink red in their last `downs.buildDropWarnSeconds`.
  A build swapped out by picking one up keeps the picked one's time and last round. Bleed-out drops keep their own rule.
- **Cap**: at most 40 dropped builds and `playerDrops.maxPartDrops` (48) part pickups on the map; past it a drop is
  **refused** with a message and you keep the item (a bleed-out drop still pushes out the oldest build).
- **Server-authoritative**: a client asks with `Msg.ZDropItem`; the server checks it (you have the part and the amount,
  the gun is a build and not your last) and takes the item from you in the same step as the pickup appears, so nothing
  is ever in two places. A pickup is removed before it is given, so when two players grab it in the same tick the first
  (in player order) gets it and the second finds nothing. Clients get part pickups in `Msg.ZPartDrop` (joiners get
  every one); `Msg.ZPickup` carries the dropped build's last round (protocol 70).
- **Co-op bots ignore pickups** (no prompt, and the server refuses their claims), bleed-out drops included (proposed;
  ask before letting bots share).

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
| `launch` (Wrecking Ball, Swordfish, Grandfather Clock, Jet Engine, Bowling Ball; 2026-10-27) | **Throws the zombie** away at 11 m/s and up at 8 m/s, stunned 1.2 s (`launchSpeed`, `launchUp`, `launchStun`); on a player only a rocket's knockback |
| `party` (Helium Balloons) | A zombie it kills **floats away** on its balloons |
| `refreshing` (Cup Holder) | Every kill gives the shooter 5 health back |
| `bayonet` (Bayonet) | Hits within 2.2 m do 1.5x damage |
| `suppressed`, `cart` | No effect on zombies (they already do their own thing) |
| `marks` (All-Seeing Eye) | No effect on a zombie: while the gun is in hand, the HUD marks every zombie through walls (client side) |

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
tick as the positions. A zombie's `ZEnts` entry stays **12 bytes** (11 without its window byte since protocol 58): while it tears or climbs (it faces its window
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

## Dismemberment (board item, 2026-10-27)

Source/Game/ZombieLimbs.cs; numbers in the rules file's `"limbs"` section; self-test "Zombie dismemberment".

- **Limb health.** Each arm and leg has `healthFraction` of the zombie's health (at least `healthMin`), times its type's
  `healthMult` (`types`: walk, run, sprint today; brutes and specials get their own entries when they exist, and
  `"severs": false` makes a type ignore dismemberment). The head stays the kill shot and has no limb health.
- **Hits.** A standing zombie's hit box is split: below the hips (0.88 m) a leg by side, at the shoulders (0.85-1.4 m)
  and at least 0.2 m out to the side an arm, the rest the body. A pellet on a limb takes `damageShare` of its damage off
  that limb as well as the zombie's health as before. A blast (grenades, launchers, landmines) takes `blastShare` off every
  limb in reach, each by its own distance: a grenade at the feet takes both legs. A shotgun's pellets add up per limb,
  so one pull can take several. Melee and the special ammo effects count on the body only.
- **States.** One arm: swipes with the other, `oneArmDamage` / `oneArmWindup` / `oneArmSwing`. No arms: a bite and a
  lunge (`biteDamage`, `biteReach`, `biteWindup`). One leg: a hop and a limp at `oneLegSpeed`. No legs: a crawler at
  `crawlSpeed`, with a prone-sized hit box (long and low: a chest-high shot passes over it, its arms are the front of the
  box), `crawlReach`, `crawlDamage`, it only reaches a target within `crawlAttackHeight` of its feet (stand on something),
  and it climbs in through windows `crawlClimb` times slower. Players already walk through zombies, so you can step over
  one. Crawlers still path with the horde (the same flow fields and mover; the horde isn't held up by its slow members).
  Bots aim low at crawlers.
- **Gore.** A severed limb flies off as pooled gibs (its two parts in the zombie's colours and a little meat that leaves
  a splat, `Gibs.SpawnLimb`), at most `goreCap` limb pieces at once (the oldest reused), each gone after `goreSeconds`,
  with a squelch on the gore bus. Nothing flies with gore off (a flesh hit sound instead). A ragdoll leaves the lost
  limbs off; the living body draws a raw stump at the joint.
- **Points.** `limbPoints` per limb shot off a living zombie (0 by default: the developer's call).
- **Network (protocol 65).** Zombie ids are 15 bits; the top bit of an entry's id in `ZEnts` says one more byte follows
  with the lost limbs (bits 0-3: right arm, left arm, right leg, left leg; both legs is a crawler), so an intact zombie
  costs nothing more and a dismembered one 1 byte. A limb coming off sends the zombie at once (like a death). Clients throw
  each limb's gore once when its bit first appears; a zombie first heard of already without limbs throws none.

## Zombie types (board item dev-zombie-variety-zv01, 2026-10-28)

Source/Zombies/ZombieTypes.cs (the data), Source/Game/ZombieTypes.cs (the behaviour); the rules file's `"types"`
section; self-test "Zombie types". **The hard rule: no type has a ranged attack of any kind.** Every type has to reach
you to hurt you: a type's reach (`reachMult` x the rules' reach) may never pass 2.2 m, the loader refuses a file whose
type does, and an unknown key (`spitRange`, `projectile`...) refuses the file too.

Every type is data. Type 0 is always the walker (the plain zombie of every round, with its own `weight`). A type has:
`healthMult` on the round's health (or `healthFixed`, see the question below), `gait` (walk / run / sprint, or `mix`
for the round's roll) and `speedMult`, `damageMult` / `reachMult` / `windupMult` / `swingMult`, its size (`scale`, or
`width` and `height`: the drawn body, the hit box, the head's height, how high it reaches and the body players are held
off all scale with it), `skin` / `shirt` / `pants`, `voicePitch` / `voiceGain` (the voice and footsteps), `pointsMult`
on kill points, `limbHealthMult` and `severs`, `firstRound`, `weight` by round (its share of a spawn's roll against
the others'), `maxAlive` at once (0: no cap) and `pack` (that many more of it out of the same spawner). Each special:

| Type | Size | Health | Gait | Hits | From round | Cap | Points | Special |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Walker | 1 | 1x | the round's mix | 1x | 1 | - | 1x | none |
| Brute | 1.45 wide, 1.5 tall | 5x | walk x0.9 | 1.6x, reach 1.3x, slower | 6 | 4 | 3x | `knockback` 7 m/s (a shove through the blast push: the client predicts it); `stunImmune`; `tearMult` 3 (boards) |
| Skitterer | 0.75 / 0.7 | 0.4x | sprint x1.05 | 0.5x, quick | 4 | 24 | 0.75x | `pack` 3: four come out together |
| Wall climber | 0.85 / 0.9 | 0.25x | run x1.1 | 0.8x | 8 | 8 | 1.25x | `climbHeight` 14 m at `climbSpeed` 2.2 m/s: climbs any wall |
| Armoured | 1.1 | 1.5x | run x0.85 | 1.2x | 10 | 6 | 2x | `armour` 300: a helmet and a chest plate shot off before the body |
| Grabber | 1 / 1.1 | 1.2x | run x0.95 | 0.5x, then a hold | 12 | 4 | 2x | `grabSeconds` 4, `grabDamage` 12 a second: holds you until it is hit |
| Pouncer | 0.9 / 0.95 | 0.6x | run x1.15 | 1.1x | 14 | 6 | 1.5x | `pounceRange` 6 m, `pounceSpeed` 9, `pounceCooldown` 4 s: a leap, then a swipe where it lands |
| Bloater | 1.35 / 1.1 | 1.5x | walk x0.9 | 0.8x | 10 | 3 | 2x | **off** (`"enabled": false`): `burstRadius` 2 m, `burstDamage` 50 when it dies or its swipe lands |
| Juggernaut | 1.8 / 2 | 20x | walk x0.8 | 2.5x, reach 1.6x | 10 | 1 | 10x | `bossEvery` 10: one boss every 10 rounds on top of the round, a health bar on the HUD; shoves, stun immune |

- **Spawning.** Each spawn rolls a type from the round's seeded stream: every type that is on, phased in (`firstRound`),
  under its cap and weighted that round takes its `weight.At(round)` share against the walker's. Rounds 1-3 are all
  walkers; by round 12 about a third are specials, never more than about half. A `pack` type brings its pack out of the
  same spawner in the same burst (within the alive cap and the round's count). A boss round (`round % bossEvery == 0`)
  queues its one boss first, on top of the round's count. Counts by type come from one pass over the horde each tick.
- **Brute.** Its hit lands through `Game.Knock`, the blast push the client already predicts (so no rubber-banding), with
  the usual lift; stuns and staggers (melee stuns, the blast stagger) don't touch it; boards come off 3x faster.
- **Armoured.** `Plates` bits (helmet, chest) with `armour` health each. A shot on the head or the chest (the torso, not a
  limb) goes into that plate: the plate takes it all (a hit marker, no damage) and comes off at 0 with a metal clang
  (`ZPlatesOff`). A melee swing goes into the chest plate. Limbs are bare (shoot the legs: it ties into dismemberment).
  Blasts and burns go round the plates. Drawn as a steel helmet and breastplate; clients get the plates in the type
  state byte.
- **Grabber.** Its landed swipe starts a hold: `ZPlayer.HeldBy` / `HeldUntil` on the server, `Held` on both sides
  (`ZWorld` flags bit 6). Held, your movement speed is `Game.HeldMoveMult` (2 percent) in both the server's speed check and
  the client's prediction, and the HUD flashes "HELD - shoot or hit the grabber to get free". It bites every second
  (`grabDamage`). Any damage on it (yours or a teammate's; a burn tick doesn't count), its death, the hold running out,
  going down or getting out of its arms frees you; it swings again after its usual pause. Bots are held like anyone.
- **Pouncer.** With its prey in plain view between its reach and `pounceRange`, on the ground and off cooldown, it
  leaps: a ballistic jump (`pounceSpeed` flat, the vertical speed solved to land beside the target, forward input held in
  the air so the mover's drag doesn't eat it). Landed, it swipes at once if anyone is in reach, as any zombie: a melee
  lunge, never a ranged hit. The self-test checks it hurts only from within its reach.
- **Wall climber.** Chasing with its prey at least 1.5 m above it (or stuck against something), with a wall within
  1.4 m towards them, it probes the wall every half metre up to `climbHeight` for the top (a flat ray that no longer
  hits) and for headroom where it stands (a ceiling or an overhang: not this wall, try again in 2 s), checks there is a
  landing just past the top (a free-standing wall: it drops off the far side), then climbs: the bell siege's
  `ZState.Scaling` state and climbing pose, moved by `FreeClimbMove` (straight up at `climbSpeed`, then the siege's vault
  over the top onto the landing, then after its prey again). Shot on the wall, it lets go and falls like a siege climber
  (`ClimbFall`: pushed off, dazed), and waits 3 s before another go. Ceilings aren't climbed (no feasible way down).
- **Bloater.** Off until the developer says a melee-range burst isn't a ranged attack. On, its burst hurts only players
  within `burstRadius` of it and in plain view (`TypeBurst`), when it dies (any cause) or when its swipe lands (then it
  dies on the spot with no kill credited).
- **Juggernaut.** A boss: `bossEvery` rounds, one at a time (`maxAlive` 1), `weight` 0 so it never comes from the roll.
  The HUD shows its label and a health bar (`Game.ZBoss`, the health byte on clients). Too big for a ragdoll: it tips over
  as drawn (so does anything more than 25 percent off a player's size).
- **Bodies.** The hit box is a player's scaled about the feet by width and height (`ZombieBox`); head and limb tests
  undo the scale (`ZombieUnscale`). The blocking cylinder (Zombie bodies) is `ZBlockRadius` x width, the player's height x
  height. Bots aim at the scaled chest and head. Gore and ragdolls use the type's colours. The drawn pose is the plain
  walker's, scaled about the feet each frame (`ZombieAnim.Scale`); the voice is pitched and scaled by the type.
- **Hordes and pooling.** Types ride on the horde controller unchanged (a zombie is a zombie to it); the pool's reset
  puts every type field back (the "Zombie hordes" reset check covers them, and never writes into the shared type
  definitions). Alive-by-type counts are one pass a tick; nothing allocates per tick (the self-test's 16-type horde).
- **Network (protocol 73).** In `ZEnts` the limbs byte (sent for any typed zombie, flagged by the id's top bit) carries
  the type in its top four bits, and a typed zombie carries one more byte: its type state (plates on, holding someone).
  A walker costs what it did; a typed zombie 2 bytes more. A plate coming off or a grab sends the zombie at once.
  `ZWorld` player flags bit 6: held by a grabber. An older server's zombies are all walkers to a client.

**Decisions for the developer** (the proposal ships; the alternative is a data change):

- Health scales per round with the type's multiplier (ships). The alternative, a fixed health per type, is
  `"healthFixed": N` on a type (0: scaling).
- The bloater is implemented but **off**: `"enabled": true` on `types.bloater` switches it on if a 2 m burst counts as
  melee.
- Every number in the table (sizes, multipliers, first rounds, weights, caps, points) is proposed.

## Zombie bodies (developer request, 2026-10-27)

Zombies are solid to players, so a horde is a wall and you can't run through it (`Source/Game/ZombieBlock.cs`,
self-test section "Zombie bodies").

- **The collider.** Each zombie that isn't dead is an upright cylinder at its feet: radius 0.35 m (a little under a
  player's 0.4 m), as tall as a standing player (1.8 m). A crawler (no legs, see Dismemberment) is a low cylinder,
  0.6 m tall, so a jump clears it. Zombies climbing through a window or on the bell tower's face count too.
- **Players.** After each tick of a player's movement the body is pushed out of every cylinder it overlaps, at most
  0.3 m a tick, and the part of its velocity going into a zombie is taken away. So a sprint into a horde stops at the
  bodies, a single zombie is slid round, and a gap of about 1.5 m between zombie centres lets you squeeze through
  (zombies keep about 0.9 m apart, so a packed horde has no gaps).
- **Never trapped.** A push that would put the body into a wall is cut to one axis or dropped. A player more than
  halfway into a zombie (one that rose or climbed in right on them) is only pushed out and never held, so they can
  always walk out.
- **Heads.** Landing on a zombie's head slides you off: the cylinder pushes sideways while your feet are below its top.
  There is no standing on the horde.
- **Downed players** neither block nor are blocked.
- **Zombies.** A walking zombie is held off standing players in the same way (server only). So a chasing horde stops
  at your body instead of shoving you, and zombies standing still don't push. Their reach (1.25 m plus your radius) is
  longer than the contact distance (0.75 m), so swipes still land.
- **Bots** (co-op bots are players) are blocked like people. The nav graph doesn't know about zombies, and the
  stuck watch handles the rest.
- **Prediction.** The client runs the same push after each predicted tick and after each replayed tick. It uses the
  newest position each zombie had in `ZEnts`, quantised to 3 cm, so there is no wire change. A packed horde stands
  still, so prediction and the server agree to within a few centimetres; with moving zombies the usual smoothing
  covers the difference.
- **Cost.** A uniform grid of the bodies is rebuilt each tick (counting sort, no allocation). With 600 zombies the
  rebuild plus four players' pushes measured about 0.03 ms a tick. The 600-zombie horde tick went from about 1.97 to
  2.1 ms.
- **Not covered.** Co-op (PvE) enemies run their own movement and aren't blocked (they're few and they shoot).

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
  (free); **perks** in `perkOrder` (Ironhide Cider, Mender's Mead, Hammerhand Ale, Swiftload Seltzer, Twinfire Punch, Second Wind), at the players'
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
| `perkOrder` | jugg, revive, hammerhand, speed, doubletap, secondwind | the perks it wants, first first (Hammerhand early: cheap, and bots rebuild boards; the other new perks are left out, since past the 6th the doubling price would have a bot saving for one all game) |
| `boxSurplus`, `slotSurplus` | 500, 2500 | spare points before a box spin / a slot pull |
| `wallBetter` | 1.15 | how much better (damage per second) a wall gun must be |
| `doors` | true | bots open doors (between rounds, one a break) |
| `thinkSeconds`, `giveUpSeconds` | 0.5, 15 | how often it plans; how long it tries to reach something |

## Network (protocol 19; protocol 40 for milestone 3; 41 for disassembly; 43 for the animations; 54 for the box's offer; 57 for 16-bit perk flags; 58 for hordes; 59 for the start round and test kit; 65 for dismemberment; 67 for modular grenades; 68 for the bell's climb)

Protocol 73 (the trader, see "The trader"): `ZTrader` (server -> all when he comes or goes, and each joiner: the trader and
his shelf) and `ZTraderBuy` (client -> server: an item, the visit it was seen on and how many) at the end of `Msg`.

Protocol 68 (the bell siege, see "Ashgrave Cathedral"): a zombie's state in `ZEnts` can be 7 (`Scaling`: on the climb's
pile or the tower's face; the low 3 bits); maps can carry `zclimb` logic and areas with heights.

Protocol 67 (modular grenades, Source/Game/GameGrenades.cs): a grenade build travels in `ZBuild` as slot 5 like any
build (every client assembles it, so every client flies everyone's built grenades with the same numbers); `ZWorld`'s
grenade kind byte can be `Built`; `Throw` of a built grenade ends with the cook time, `ProjSpawn` with what it is
(thrown, bomblet, aftershock) and `Explode` with the payload, radius, duration and that byte; `ZGrenade` (the last
message) carries a client's remote detonation and a lure's closing bang.

Protocol 58 (hordes, see "Hordes"): `ZEnts` per client, interest-managed, at most 80 zombies a packet, 16-bit horde size
and entry count, the window byte only outside, at a window or swiping (11 or 12 bytes a zombie), unreliable (not
sequenced).

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


New messages at the end of `Msg`: `ZUse` (client: interact key down / up), `ZEnts` (the zombies with each snapshot:
id, quantised position, yaw, state, speed, health; 11-12 bytes each, about 280 bytes for 24; interest-managed per
client since protocol 58), `ZWorld` (10 Hz: round,
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

Zombies move with the shared player physics (the same mover as players). Since the hordes (2026-10-27) the thinking
is the horde controller's ("Horde controller" below): shared flow fields instead of an A* search per zombie, a level
of detail by distance, the grid for separation. The self-test measures 24 zombies chasing inside the start room (the
report line "AI cost"; it includes the per-tick animation update), and "Zombie hordes" 24, 100 and 300 on Blackmoor
(see "Hordes"). Drawing cost: see "Animation" and "Hordes".

## Player scaling (developer request, 2026-10-27)

The round's size and its alive cap both follow the player count (bots count; a host who sits out doesn't), and since
2026-10-27 more steeply: each extra player adds 12 zombies to the round's base of 24 (`countPerExtraPlayer`, was 6) and
half again to the alive cap (`maxAlivePerExtraPlayer` 0.5, was 0.15), on the steeper `maxAliveCurve` above (hard cap
`maxAlive` 600). All of it is in `Parts/Tables/ZombiesRules.json` "rounds".

| Players | Round 1 / 5 / 10 / 20 / 30 zombies | Alive cap at round 1 / 5 / 10 / 20 / 30 / 40 |
| --- | --- | --- |
| 1 | 6 / 22 / 72 / 288 / 648 | 24 / 40 / 90 / 220 / 400 / 600 |
| 2 | 9 / 32 / 108 / 432 / 972 | 36 / 60 / 135 / 330 / 600 / 600 |
| 3 | 12 / 43 / 144 / 576 / 1296 | 48 / 80 / 180 / 440 / 600 / 600 |
| 4 | 15 / 54 / 180 / 720 / 1620 | 60 / 100 / 225 / 550 / 600 / 600 |

**Joining or leaving mid-round** (a person, a bot added or removed, a player dropping) works the round out again at
once (`Game.RescaleRoundForPlayers`, ZombiesMode.cs): the round's total becomes its count for the new number (map
logic's extra spawns kept), and the zombies still to spawn become that total less the ones already out (killed or
alive), never below 0. The alive cap reads the player count live. A leave never ends the round there and then: with
more out than the smaller round would send, nothing more spawns and the round ends when those still alive are killed;
with every zombie dead and some still owed, one more comes and its kill ends the round. The server logs a
`[zombies] round N: A -> B players` line for each change.

**The budget at 600 alive** ("Zombie hordes" measures a scripted 600-zombie horde on Blackmoor besides 24, 100 and
300): on the 2-core cloud box under load the server tick was 1.8 ms CPU on average (2.2 ms worst; budget 8 ms), no
allocation, the interest-managed snapshots stay at about 26 KB/s per client (80 zombies a packet, the near ones
every snapshot), and the client's zombie work (reading, posing, the instanced draw's boxes, the sounds) about 2.3 ms
a frame for 8400 boxes. GPU time for the instanced draw is not measured headless.

## Hordes (developer request, 2026-10-27)

"As the rounds progress, I want to see hundreds of zombies all on the map at the same time. Late game." The alive cap
now rises with the round (`maxAliveCurve` in "rounds"; the round curve table above): 24 early, 90 by round 10,
220 by round 20, 400 by round 30, 600 from round 40 (raised by the player scaling, 2026-10-27; it was 60 / 150 / 300 /
400), half again per extra player, never over the rules' hard cap `maxAlive` (600, also the code's own limit,
`Game.MaxZombies`; a settings file's old "no limit" of 512 still reads as no limit). **The host can lower it**: the lobby room's World
section has **Zombie horde cap** (48, 100, 150, 200, 300, 400 or no limit; settings key `zombie_horde_cap`) for weak
PCs; the rest of the round waits its turn as before. Round 30 solo is 648 zombies, so the cap is what decides how
many are up at once.

To fill a big cap, late rounds spawn in **bursts** (`spawnBurstCurve`) from **more spawners** (`activeSpawnersCurve`),
spread out: one zombie per spawner per burst, a spawner rests `spawnerCooldown` after use, and a spawner with someone
standing on it puts the new zombie on a free spot of a ring round it (0.9 m, then 1.8 m; standable and in view of the
spawner), never on top of another. Round 30 on Blackmoor fills to its cap of 400 in about 28 s.

At the windows the horde **queues** instead of jamming: two zombies (`tearers`) work the boards, the rest stand on
queue spots in rows of three behind the window (checked standable and in view when the map loads), and when a
window's queue is longer than `queueSplitAt` (6) its back members walk round to the open window with the shortest
queue within `flankRange` (30 m of route). Only one climbs through at a time, as before.

**Budgets and measurements** (the "Zombie hordes" self-test on Blackmoor Asylum; the user's PC through the agent
runner, Linux): server tick CPU per tick, ZEnts bytes per client per second, the client's zombie work per frame (reading
snapshots, posing and drawing every zombie it knows into the headless recorder, the sounds).

| Zombies | Before: tick | Before: ZEnts | Before: client | After: tick (mean / worst) | After: ZEnts | After: client |
| --- | --- | --- | --- | --- | --- | --- |
| 24 | 0.06 ms | 8.6 KB/s | 0.05 ms | 0.06 / 0.09 ms | 8.1 KB/s | 0.06 ms |
| 100 | 0.36 ms | 31.8 KB/s (only 90 sent) | 0.18 ms (90 drawn) | 0.22 / 0.27 ms | 26.3 KB/s (all 100 known) | 0.18 ms |
| 300 | 1.94 ms | 31.8 KB/s (only 90 sent) | 0.15 ms (90 drawn) | 0.62 / 0.71 ms | 26.3 KB/s (all 300 known) | 0.49 ms |

Before, the tick also allocated about 115 bytes per zombie per tick (a LINQ closure in the area check every zombie made
every tick: 35 KB a tick at 300); now nothing. The slower cloud box measured 4.3 ms before and 1.8 ms after at 300.
The self-test's budgets: no allocation per tick; on Windows no tick over 8 ms of CPU (the thread's cycles); elsewhere a
mean of at most 1 / 2 / 4 ms at 24 / 100 / 300 (slack for noisy runners); ZEnts at most 30 KB/s per client and one
datagram per packet; the client at most 1.5 ms a frame (3 ms at 300).

**Network (protocol 58).** `ZEnts` is now sent to each client on its own, **interest-managed**: every zombie gets a
priority per client, how many snapshots since that client last heard of it times how near it is (8x within 12 m, 3x
within 30 m, 1.5x within 60 m); a new zombie, a changed state (a death, a climb) or one not heard of for 24 snapshots
goes first, and the top 80 go (at most 9 + 80 x 12 = 969 bytes). A horde of up to 80 is sent whole every snapshot as
before; a bigger one keeps the near zombies at or near 30 Hz and the far ones at a few Hz. The header has a 16-bit horde
size and entry count (the count was one byte, and the server stopped at 90), an entry carries its window byte only
outside, at a window or swiping (11 or 12 bytes). The packets are unreliable but not sequenced: a late one still
updates the zombies that have no newer news. Clients keep a zombie they haven't heard of for 1.6 s at most, a dead one
for 1.7 s, carry a far zombie on at its last speed for up to a quarter of a second past its last update (no stop and
jump), and look zombies up by id (it was a list search per entry). Over loopback with 80 ms of lag and 3 percent loss a
client knows all 300 of a horde, gets 26 KB/s of ZEnts, and the near zombies are never more than about 200 ms old.

**Drawing.** The horde's opaque boxes go into **one instanced draw** per 4096 boxes (`Renderer.BeginBatch`: an
instanced copy of the box shader with the same lighting and fog; anything with blood marks or blending is drawn as
before, in order). **Animation level of detail**: beyond 28 m a zombie is posed every 2nd frame and beyond 50 m every
4th (staggered by id; a horde over 120 starts a little nearer), its last pose carried along with the body in between
(`ZombieAnim.Shift`); beyond 28 m each limb is one box as before. Corpses: at most 10 zombie ragdolls at once (the
rest vanish when they die, as before), gibs keep their own caps. Sounds: the 8 vocal and 8 footstep voice caps stay;
the idle groan budget (3 a second) grows with the square root of the horde past 48 zombies, up to twice (6 a second
at 300).

**Co-op bots** look for a target nearest first and stop at the first one in a clear line of fire (a few ray casts, not
one per zombie in range), and find the nearest zombie and the window to repair without LINQ (the horde's grid).

**Economy.** Points per hit and kill are unchanged, so a horde round pays more as it has more zombies; power-ups still
come on the team's points schedule, at most 4 a round. Zombie health still grows by round, so a 300-zombie round 30 is
mostly a crowd-control problem (explosives, penetration, the special ammo effects).

## Horde controller (developer request, 2026-10-27)

"An agent may need to be assigned to create a horde controller which dictates large groups of NPCs and how they
respond to things." `Source/Game/Horde/` is that system, generic over the NPC (`IHordeAgent`: a horde record, the
position, alive, the barrier it is working towards and whether it is at the front, an id); Zombies wires its zombies
in (`Source/Game/ZombieHorde.cs`), the co-op enemies could later (docs/pve/DESIGN.md, "Horde controller"). The
numbers are the rules file's **"horde"** section.

- **Groups.** Fixed slots: one per **portal** (a window: members outside, queueing), one per **target** (a standing
  player; a player keeps their slot while they stand), one **lure** group. Members move between them (splitting and
  merging): a long portal queue sends its back members to another portal's group; members inside join the target
  group with the best score: distance, plus `areaPenalty` (20 m) for another area, plus `pressureWeight` (0.4 m) for each
  member already on that player past the first `pressureFree` (8), so a big horde **spreads over the players** instead
  of piling on the nearest (re-checked about every half second, with hysteresis).
- **Group decisions** (each group about every `groupThinkSeconds`, 0.5 s, staggered): portal groups split their queue
  ("Hordes" above); a target group of at least 4 that got less than 0.6 m closer in 2.5 s while nobody is at its
  target and the target itself stayed put (moved less than 1.5 m: a player kiting the horde is chased, not a block)
  **regroups** for 1.2 s: the members near the group's centre, or at least as near the target as the centre, hold; the
  stragglers behind it close up on it (so closing up never walks anyone away from the target); then they go again with
  fresh routes. (Before 2026-10-27 a kited group regrouped every few seconds and every member walked at the centre,
  which for a horde spread round its target is somewhere else entirely: on Horde Test the dais in the middle, so the
  whole horde turned to the origin together. Board bug dev-horde-wrong-target-hz01; self-test "Horde targets".)
- **Surrounding.** Between 2.5 and 9 m of their target, members close in on a ring point at their own angle (from the
  id), so a group comes at a player from all sides; nearer, straight at them.
- **Stimuli** (handled at the start of the next tick, in order): **gunfire** (every trigger pull in Zombies; heard to
  35 m) and a **grabbed power-up** (45 m) turn members in earshot that aren't already fighting (within 10 m of their
  own target) on the shooter; a **hurt** zombie turns its neighbours within 12 m the same way; a **blast** staggers the
  zombies near it (0.3 s), they **scatter** away from it for 0.6 s, then their group regroups and turns on the thrower;
  a player **going down** sends everyone after them to someone else at once; a **lure** (`Game.HordeLure`: no item uses
  it yet; grenades trigger the blast reaction) draws the members in range to it until it ends, then back to the players.
- **Level of detail.** Members nearer a player than 12 / 30 / 60 m think every 1 / 2 / 4 ticks, the rest every 8
  (staggered by id; `thinkEvery`): the line-of-sight checks, the corner cuts, the area and target checks. Movement
  (the shared physics), separation and attacking run every tick.
- **Routes.** **Flow fields** over the bots' nav graph (`HordeFlowFields`): one Dijkstra over the reversed edges per
  goal node (a player's node, a door side, a window, a lure) gives every member heading there its next node in one
  lookup. 24 are cached (least recently used goes), at most 1 new one is built a tick (a member whose field isn't
  ready steers straight meanwhile), and one older than a second is rebuilt when next asked for. Edges destruction took
  away are left out. On Blackmoor (549 nodes) a field takes about 0.01-0.03 ms.
- **Separation** comes from a uniform grid of the members (1.25 m cells, rebuilt every tick with a counting sort), not
  the old loop over every pair.
- **Deterministic** (no random numbers in the controller; ordered by list index and id) and **allocation-free** per tick.

The self-test checks each of these with short seeded scenarios on Blackmoor: 60 zombies near one of two players spread
over both, 20 at one window queue on its spots and split to the other two, a gunshot turns a group in earshot, a blast
scatters the zombies near it and their group regroups and goes on, a down sends a group to the other player, a lure
draws a group and lets it go, a group shut in a room regroups, and the same horde twice plays out the same.
Section **"Horde targets"** (Horde Test): 40 zombies plus round 10's spawns chasing the host round the dais for 26 s
(about 50 target nodes and 80 flow fields built, every field dropped once mid-run): never more than a sixth of the
moving horde walking away from the host at once, never a run of them heading for the origin, no zombie walking away
for more than a second (routing round the towers), no regroup of the kited group, and a regrouping member only closing
up from behind the centre. The code before the fix fails all five.
Section **"Horde respawn"** (Horde Test, 2026-10-27): the chased player stops being a target and comes back, and the
horde goes on. Zombies moving a tick (of about 40-50): solo with Second Wind 39 before, 2 while down (nobody standing:
they wait), 42 after it gets the player up; two players 39 before, then 40 with the host down (all on the mate), 40
revived, 39 bled out, 29-33 in the next round (fewer up yet), after the mate leaves, rejoins, and the host missing for
one raw tick. A solo player not standing for one tick (Alive false, no Second Wind) is the run's game over, as a solo
down is in play, so the match ends and the horde stops with it: that, not the controller, was the "1 zombie moving
instead of 40" seen while writing "Horde targets". In play Alive only goes false by bleeding out.

## Horde Test, start round and test kit (developer request, 2026-10-27)

"Add an agent to make a horde test map. Getting a high round in zombies takes a lot of time." Three tools for seeing and
testing big hordes quickly:

**Horde Test** (`Maps/Dev/HordeTest.json`, Dev pack, order 986): written by the C# map kit
(`Source/MapKit/Generators/HordeTest.cs`; `CallOfSlop --gen-map "Horde Test"`, never edit the JSON by hand). A 48 x 48 m
walled arena (the start area) with 15 boarded windows in its south, east and west walls, and two annexes on its north
side, the **Power Annex** behind the Power Door (750: the power switch, the slot upgrade, a workbench, salvage, a ground
spawner) and the **Armoury** behind the Armoury Debris (1000: a slot machine for every part category in a row, a box spot, the Coach Gun,
salvage, a ground spawner), with three windows each: 21 windows, **two spawners behind each** (44 spawners). Outside
the walls a 5-10 m open strip runs right round the map, so the queues at the windows, the splits to other windows and
the flanks are easy to watch: from the **dais** in the middle (1.6 m, stairs east and west) or the two railed **lookout
towers** either side of it (3.2 m), which see over the 3 m walls into the strip. In the arena: **every perk machine in a
row** along the north wall (seven on the power), the box (three spots), a workbench, four wall buys (Wall Rifle, Wall SMG,
Wall Pump, Burst Marksman) and a classic slot machine. Every machine is a solid prop. Round 30 fills to its cap of 300 in
about 28 s. Its `test` block (Maps/README.md, "Testing a map") checks the contract; the outside strip and the annexes are
nav pockets of their own (the windows and shut doors cut them off), so `minMainNavPct` is 40.

**Start round** (the lobby room's World section, Zombies: 1, 5, 10, 20, 30, 40 or 50; settings key
`zombie_start_round`; `CallOfSlop host --zombies-round 30`, not saved): the run's first round is that one, with the
economy of a run that got there (`Source/Game/ZombiesStartRound.cs`):
- **Points**: the start points plus `points.startRoundKeep` (12 percent) of a player's share of what the skipped rounds
  paid (`points.startRoundPerZombie`: 90 a zombie, a kill and a few hits); the rest went on doors, perks and guns. Solo:
  round 5 950, 10 2750, 20 19730, 30 67060, 40 160250; each of 4 players at round 30: 29620. Someone who joins the run
  later gets the same.
- **Power-ups**: the team's points schedule is moved on by everything the skipped rounds paid, so the next drop is one
  of that round's (rising) steps away.
- The round itself brings the rest as usual: zombie count, health and speeds, the alive cap and spawn bursts, the box's
  and the drops' tiers, the slot machines' price.

**Test kit** (a checkbox under Start round, only on maps whose section says `"testKit": true`: Horde Test; settings key
`zombie_test_kit`; `--test-kit`): everyone starts with **every perk**, slots 3-4, the four strongest standard box guns
(Wonder one-offs left out) and the best melee build, and the power is on, so you can stand on the dais and watch. Perks
are still lost on a down; bleeding out brings the kit back with the start gear.

**Skipping rounds**: in a Zombies run the host's pause menu has a **HOST** page (Esc > HOST, as in Test Range) with
**NEXT ROUND NOW** and **SKIP TO IT** (any round 1-100): the zombies up and queued go (no points or drops), the round
starts at once (a break or ready-up is cut short), and anyone short of that round's start points is raised to them (the
power-up schedule moves on too). Server-side only: no new message.

**Network (protocol 59).** Both settings are host settings: `Msg.LobbyState` ends with the start round byte and a
Zombies flags byte (bit 0: the test kit), so clients' lobby rooms show them ("starts at round 30, test kit"). Round 1
without the kit plays exactly as before (the seeded match digests are unchanged).

Self-test **"Zombies start round"**: the start points by round (solo and 4 players); a run on Horde Test started at round
30 with the kit (round 29 waiting, the points, every perk, 4 guns and a melee build, the power, the power-up schedule
moved on); the horde filling to the round-30 cap of 300 (28 s; the user's PC: 0.17 ms a tick mean, 2.0 ms worst; at
most 35 s, on Windows no tick over 8 ms); the host's skip to round 32 (horde cleared, round started, points raised); the
settings file; a loopback client seeing the host's start round and kit.

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
   6th 4x, 7th 8x...), Second Wind included; solo Mender's Mead unchanged (see "Perks").
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
14. **Zombie types** (2026-10-28): health keeps scaling per round with a type multiplier (`healthMult`, ships) or
    becomes fixed per type (`healthFixed`)? Is the bloater's 2 m burst on death / on contact a ranged attack? It ships
    switched off. Every number in the types table is proposed (see "Zombie types").
