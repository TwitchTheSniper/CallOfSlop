# Steam achievements, stats, leaderboards and Timeline (Phase 3): the proposal

**Status: PROPOSAL, waiting for the developer's OK.** Nothing here is built yet. Change anything: names, numbers, what is
hidden, what is dropped. Once agreed, this file becomes the reference and the game generates the table you enter on the
Steamworks site (`docs/steam/achievements_steamworks.csv` plus icon specs), so you never copy names out of this text by hand.

46 achievements, 22 stats, 2 leaderboard families, 12 Timeline events. The open questions at the end are the decisions
that change what gets built; the defaults in **bold** are what happens if you just say OK.

## Ground rules (how unlocking works)

- **Your own game unlocks your own achievements.** Each player's game (host or client) watches the events it already
  receives (the kill feed, the round number, the Zombies messages) and unlocks for the local Steam user only. Nothing new
  goes on the wire for this, so the protocol stays at 75.
- **Progression stays in the game's save.** Level, prestige and the career numbers live in the save as today; Steam
  mirrors them. A player who levelled up before Steam existed gets the level and prestige achievements on their first
  Steam launch (the game pushes the save's values up once).
- **Stats are stored at the end of a match, a Zombies round, or on quitting, never per tick.** In between the game only
  adds to its own counters (no allocation, no Steam call).
- **What does not count:** Dev pack maps, Test Range, the Zombies **start round** above 1 and the **test kit** (those runs
  can still unlock the casino, slot machine and build achievements, which don't depend on how far you got), and anything
  while Steam is off (non-Steam builds behave exactly as today).
- **Kills on bots count** for the PvP kill and headshot achievements (most matches have bots), but not for leaderboards.
- All Steamworks stats and achievements are "Set by: Client" (the site default for a game without a game server).

## Achievements

API names are what the code uses (upper case, `ACH_` prefix). "Progress" means Steam shows a progress bar bound to the
named stat. "Hidden" means the name and description show only once unlocked.

### Progression (mirrors the save)

| # | API name | Display name | Description | Hidden | Unlocks when | Progress |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `ACH_LEVEL_10` | Getting Sloppy | Reach level 10. | no | the save's level reaches 10 | `STAT_LEVEL` 10 |
| 2 | `ACH_LEVEL_25` | Seasoned Slop | Reach level 25. | no | level 25 | `STAT_LEVEL` 25 |
| 3 | `ACH_LEVEL_MAX` | Fully Boxed | Reach the maximum level. | no | level reaches `Progression.MaxLevel` | `STAT_LEVEL` max |
| 4 | `ACH_PRESTIGE_1` | Fresh Hat | Prestige for the first time. | no | `Progression.DoPrestige` (prestige 1) | `STAT_PRESTIGE` 1 |
| 5 | `ACH_PRESTIGE_3` | Hat Collector | Reach prestige 3. | no | prestige 3 | `STAT_PRESTIGE` 3 |
| 6 | `ACH_PRESTIGE_MAX` | Hat Tower | Reach the highest prestige. | no | prestige `Cosmetics.MaxPrestige` (5) | `STAT_PRESTIGE` 5 |

### Multiplayer

| # | API name | Display name | Description | Hidden | Unlocks when | Progress |
| --- | --- | --- | --- | --- | --- | --- |
| 7 | `ACH_FIRST_WIN` | Winner Winner | Win a match. | no | match over, you (or your team) won | |
| 8 | `ACH_KILLS_100` | Box Breaker | Get 100 kills in multiplayer modes. | no | `STAT_PVP_KILLS` reaches 100 | `STAT_PVP_KILLS` 100 |
| 9 | `ACH_KILLS_1000` | Slop Machine | Get 1,000 kills in multiplayer modes. | no | `STAT_PVP_KILLS` 1000 | `STAT_PVP_KILLS` 1000 |
| 10 | `ACH_HEADSHOTS_250` | Top Box | Get 250 headshot kills in multiplayer modes. | no | `STAT_PVP_HEADSHOTS` 250 | `STAT_PVP_HEADSHOTS` 250 |
| 11 | `ACH_TRIPLE_KILL` | Triple Slop | Get a triple kill. | no | your multi-kill chain (`KillNotices`) reaches 3 | |
| 12 | `ACH_PENTA_KILL` | Five Box Pileup | Get five kills in one multi-kill chain. | no | chain reaches 5 | |
| 13 | `ACH_TOP_STREAK` | Air Superiority | Call in a Gunship or a Juggernaut. | no | you use `StreakId.Gunship` or `StreakId.Juggernaut` | |
| 14 | `ACH_FRENZY_MAX` | Hardbass Overload | Reach the top frenzy level. | no | your frenzy meter reaches `FrenzyMeterCore.MaxLevel` | |
| 15 | `ACH_FLAWLESS` | Untouchable | Win a Free-for-All match without dying. | no | FFA win with 0 deaths | |
| 16 | `ACH_GUN_GAME` | Full Ladder | Win a Gun Game match. | no | Gun Game win | |
| 17 | `ACH_ALL_MODES` | Mode Tourist | Win a match in Free-for-All, Team Deathmatch, King of the Hill, Capture the Flag and Gun Game. | no | `STAT_MODES_WON` (a bit per mode) has all five | |
| 18 | `ACH_GRENADE_TRIPLE` | Spread the Love | Kill three enemies with one grenade. | no | 3 kills credited to one throwable | |
| 19 | `ACH_COOP_MISSION` | Mission Slopped | Complete a Co-op mission. | no | Co-op mission won | |

### Zombies: rounds and maps

| # | API name | Display name | Description | Hidden | Unlocks when | Progress |
| --- | --- | --- | --- | --- | --- | --- |
| 20 | `ACH_Z_ROUND_10` | Night Shift | Reach round 10 in Zombies. | no | a round 10 starts with you alive or spectating (start round 1) | `STAT_Z_HIGHEST_ROUND` 10 |
| 21 | `ACH_Z_ROUND_20` | Graveyard Shift | Reach round 20 in Zombies. | no | round 20 | `STAT_Z_HIGHEST_ROUND` 20 |
| 22 | `ACH_Z_ROUND_30` | Double Shift | Reach round 30 in Zombies. | no | round 30 | `STAT_Z_HIGHEST_ROUND` 30 |
| 23 | `ACH_Z_ROUND_50` | No Such Thing As Overtime | Reach round 50 in Zombies. | yes | round 50 | `STAT_Z_HIGHEST_ROUND` 50 |
| 24 | `ACH_Z_SOLO_20` | Party of One | Reach round 20 in Zombies alone. | no | round 20 with one human player (bots allowed? see question 3) | |
| 25 | `ACH_Z_COOP_FULL_20` | Full House | Reach round 20 with four players. | no | round 20 with 4 human players | |
| 26 | `ACH_Z_BLACKMOOR_15` | Discharged | Reach round 15 in Blackmoor Asylum. | no | round 15 on that map | |
| 27 | `ACH_Z_ASHGRAVE_15` | Last Rites | Reach round 15 in Ashgrave Cathedral. | no | round 15 on that map | |
| 28 | `ACH_Z_BELL_SIEGE` | For Whom It Tolls | Survive the bell siege. | no | the siege's reward is paid to you (`ZombiesClimb`) | |
| 29 | `ACH_Z_BELL_SIEGE_SOLO` | Belfry Hermit | Survive the bell siege alone. | yes | the siege is survived with you the only player in the belfry | |
| 30 | `ACH_Z_KILLS_1000` | Pest Control | Kill 1,000 zombies. | no | `STAT_Z_KILLS` 1000 | `STAT_Z_KILLS` 1000 |
| 31 | `ACH_Z_KILLS_10000` | Extinction Event | Kill 10,000 zombies. | no | `STAT_Z_KILLS` 10000 | `STAT_Z_KILLS` 10000 |
| 32 | `ACH_Z_CRAWLER` | Mind the Gap | Kill a crawler. | no | you kill a zombie that has lost its legs (`z.Crawler`) | |
| 33 | `ACH_Z_REVIVES_25` | Get Up, Slop | Revive 25 players. | no | `STAT_Z_REVIVES` 25 | `STAT_Z_REVIVES` 25 |
| 34 | `ACH_Z_NUKE` | Clean Sweep | Kill 20 zombies with one Nuke. | no | a Nuke power-up you picked up kills 20 or more | |

### Zombies: the shops, slots and casino

There is no trader in the game today; the "trader purchases" idea maps onto every buyable (wall buys, the box, perks,
slot pulls, doors, the slot upgrade).

| # | API name | Display name | Description | Hidden | Unlocks when | Progress |
| --- | --- | --- | --- | --- | --- | --- |
| 35 | `ACH_Z_BUYS_100` | Valued Customer | Buy 100 things in Zombies. | no | `STAT_Z_BUYS` 100 | `STAT_Z_BUYS` 100 |
| 36 | `ACH_Z_SPENT_1M` | Big Spender | Spend 1,000,000 points in Zombies. | no | `STAT_Z_POINTS_SPENT` 1,000,000 | `STAT_Z_POINTS_SPENT` 1M |
| 37 | `ACH_Z_PERKS_8` | Fully Carbonated | Hold eight perks at once. | no | your perk count reaches 8 | |
| 38 | `ACH_Z_TEDDY` | Not Today | Watch the mystery box fly away. | yes | your spin is the teddy bear | |
| 39 | `ACH_Z_WONDER_SLOT` | Wonderful Pull | Win a Wonder part from a slot machine. | no | a slot pull you made pays a part of rarity Wonder | |
| 40 | `ACH_Z_BLACKJACK` | Natural Talent | Get a blackjack. | no | your blackjack hand is a natural | |
| 41 | `ACH_Z_HIGHLOW_6` | Six Sense | Make six right guesses in a row at High-Low. | no | the table cashes you out after 6 | |
| 42 | `ACH_Z_ROULETTE_NUMBER` | Thirty-Five to One | Win a straight-up bet at roulette. | no | your number bet wins | |
| 43 | `ACH_Z_HOUSE_WINS` | The House Always Wins | Lose 5,000 points at the casino in one run. | yes | your casino losses minus wins in one run reach 5,000 | |

### Zombies: builds and grenades

| # | API name | Display name | Description | Hidden | Unlocks when | Progress |
| --- | --- | --- | --- | --- | --- | --- |
| 44 | `ACH_Z_NINE_RECEIVERS` | Nonuple Threat | Fire a gun with nine or more receivers. | no | you fire a build with `Receivers.Count >= 9` | |
| 45 | `ACH_Z_SILLY_BUILD` | Function Over Form | Kill a zombie with a Push Cart, a Parade Float, a Frozen Fish, a Swordfish or a Grandfather Clock. | no | a kill whose build has one of those parts (a list in the code, easy to change) | |
| 46 | `ACH_Z_GRENADE_KILLS_100` | Some Assembly Required | Kill 100 zombies with modular grenades. | no | `STAT_Z_GRENADE_KILLS` 100 | `STAT_Z_GRENADE_KILLS` 100 |

Possible extras if you want more (not counted above): a kill with a Rubber Duck Flock grenade (hidden), five zombies
with one modular grenade, a win on every slot machine category, a roulette spin with chips on 0 that lands on 0, a
jackpot-round pull.

## Stats

All are INT, "Set by: Client", increment-only unless noted. Steam keeps the total across machines; the game's save keeps its
own copy (the save stays the source of truth for level and prestige).

| API name | What | Stored |
| --- | --- | --- |
| `STAT_LEVEL` | your level (mirror of the save) | on level-up |
| `STAT_PRESTIGE` | your prestige (mirror) | on prestige |
| `STAT_PVP_KILLS` | kills in PvP modes (bots included) | match end |
| `STAT_PVP_HEADSHOTS` | headshot kills in PvP modes | match end |
| `STAT_PVP_DEATHS` | deaths in PvP modes | match end |
| `STAT_MATCHES_PLAYED` | PvP and Co-op matches finished | match end |
| `STAT_MATCHES_WON` | matches won | match end |
| `STAT_MODES_WON` | a bit per PvP mode won (FFA 1, TDM 2, KotH 4, CTF 8, Gun Game 16); "max" kept, never lowered | match end |
| `STAT_Z_KILLS` | zombie kills | round end |
| `STAT_Z_HEADSHOTS` | zombie headshot kills | round end |
| `STAT_Z_ROUNDS` | Zombies rounds survived, all runs | round end |
| `STAT_Z_HIGHEST_ROUND` | highest round reached (start round 1); max kept | round end |
| `STAT_Z_POINTS_SPENT` | points spent on buyables, slots and casino bets | round end |
| `STAT_Z_BUYS` | buyables bought | round end |
| `STAT_Z_REVIVES` | players revived | round end |
| `STAT_Z_DOWNS` | times downed | round end |
| `STAT_Z_GRENADE_KILLS` | kills with modular grenades | round end |
| `STAT_Z_SLOT_PULLS` | slot machine pulls | round end |
| `STAT_Z_CASINO_BET` | points bet at casino tables | round end |
| `STAT_Z_CASINO_WON` | points won back at casino tables | round end |
| `STAT_Z_BELL_SIEGES` | bell sieges survived | round end |
| `STAT_PLAY_SECONDS` | seconds in a live match or run | match end |

Achievements unlock the moment the event happens (`SetAchievement` is cheap and local), but `StoreStats` (which
uploads and shows the toast) waits for the end of the round or match, at most once a minute. Question 1 asks whether
the toast should show straight away instead.

## Leaderboards

The game creates them itself the first time they are needed (`FindOrCreateLeaderboard`, sort descending, display
numeric, keep the best score), so a new Zombies map needs no site work. You can also make them on the site first to
control the display names.

| Name | Score | Details (shown nowhere by Steam; the game can read them) |
| --- | --- | --- |
| `Z_<MapId>_Solo` (one per Zombies map: `Z_BlackmoorAsylum_Solo`, `Z_AshgraveCathedral_Solo`) | highest round reached, alone, start round 1 | kills, points, seconds |
| `Z_<MapId>_Coop` | highest round reached with 2 to 4 players | the same, plus the player count |
| `HORDE_PeakAlive` | the most zombies alive at once in a round you survived (any Zombies map, start round 1) | the round, the map |
| `HORDE_RoundKills` | the most zombies you killed in one round | the round, the map |

Bots never post. An in-game leaderboard page (friends and global, top 10 around you) would come in the Zombies menu;
question 4 asks whether you want that now or Steam's own pages are enough.

## Timeline (ISteamTimeline)

The game mode: **Playing** in a live match or run, **Staging** in the pre-match lobby and between Zombies rounds,
**Menus** in the menus, **LoadingScreen** while a map loads. Events (the icon is one of Steam's built-in ones):

| Event | Title | Priority | Icon |
| --- | --- | --- | --- |
| Multi-kill 3+ | "Triple kill" / "N kills" | 1 (5+: 2) | `steam_effect` |
| Kill streak called in | the streak's name | 1 | `steam_bolt` |
| Frenzy max level | "Hardbass overload" | 1 | `steam_fire` |
| Match won | "Won <mode> on <map>" | 2 | `steam_crown` |
| Zombies round 10, 20, 30, every 10 after | "Round N" | 1 (30+: 2) | `steam_flag` |
| Bell siege started / survived | "Bell siege" / "Siege survived" | 1 / 2 | `steam_alarm` / `steam_crown` |
| Downed / revived | "Down" / "Back up" | 0 | `steam_death` / `steam_heart` |
| Run over | "Run over at round N" | 1 | `steam_death` |
| Legendary or Wonder part from a slot machine | "<part name>" | 1 / 2 | `steam_treasure` |
| Casino big win (1000 over the bet) | "Won N points" | 1 | `steam_cash` |
| Prestige | "Prestige N" | 2 | `steam_crown` |
| Achievement unlocked | its display name | 1 | `steam_achievement` |

## Icons (what you will upload)

Steam wants two 256 x 256 JPG (or PNG) images per achievement: the unlocked one and the locked one (usually the same
picture in greyscale). The generated table lists, for each achievement, a one-line picture brief in the game's look
(everything made of boxes, flat colours), e.g. "a stack of five box hats" for Hat Tower. Question 5 offers to draw them.

## Questions for the developer

1. **Toasts:** Steam shows the "achievement unlocked" popup only after `StoreStats`. Store at the end of the round or
   match (**default: popups come at the end of the round or match**), or store straight away on each unlock (popups at
   once, still never per tick: an unlock is rare)?
2. **The list:** OK as it stands (**default**), or tell me which to add, drop or rename. Anything else hidden?
3. **Bots in Zombies:** do the "alone" achievement and the solo leaderboard allow co-op bots? (**default: no bots for
   solo; bots don't count as players for Full House either**.)
4. **In-game leaderboard page** in the Zombies menu now (**default: no, Steam's community pages only; it can come later**)?
5. **Icons:** I can generate all 92 icons as 256 x 256 PNGs from the game's own box renderer (a `--steam-icons DIR`
   command; locked versions in greyscale) for you to upload (**default: yes, generated**), or you make them yourself from
   the briefs.
6. **Horde records:** are "most alive at once" and "most kills in one round" the horde stress records you meant, or did
   you mean the Dev pack's Horde Test map (which this proposal excludes, being a test map)?
