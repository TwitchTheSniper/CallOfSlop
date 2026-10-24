# HANDOFF: state of Call of Slop (2026-10-23)

**Current state:**
- `main` holds everything from the 2026-10-23 coordinator session: README dedupe, empty action socket marker, floating parts, workbench UI (HUD hidden, Disassemble button, tree click), optic mounts, Legacy map pack, rack grips without frames + Twin/Triple Rifle Grips, universal ammo + feed coverage, SeabMix2 default audio mix, more optics/muzzles (the Loudener)/grips in both systems, updater cleanup, the Test Range flake fix, gravity zones, and ten new Space maps (03-12).
- **Protocol 46** (feat/attachments widened loadout kits).
- CI on main: **green on Linux and Windows** on `763445c` (run 40012259653; Windows after one rerun of a one-off tick-time check). Ready for a release.
- The first public release is **v0.2026.1023.7**, built from `814ffbf`. Nothing above is in a release yet.
- No agents or branches are in progress. Build/test fast path on the user's PC: `Agent Copies\_tools\pcrun.sh` (see the project doc `claude/agent-rules.md`); the PC was offline at the end of the session, so the VM may need its .NET and seed repo again.

## Done (all merged to main)

**Modes**
- Free-for-all, Team Deathmatch, King of the Hill, Capture the Flag and Gun Game.
- **Test Range** (id 8): endless free-for-all. Nothing ends the match or changes the map.
- **Co-op PvE** (id 7): enemy soldiers and a spawn director. One mission so far: `Maps/World/Missions/SiloBreach.json`, on Silo.
- **Zombies** (id 5), milestones 1 to 3 (see `docs/zombies/ZOMBIES-MODE.md`):
  - rounds, boarded windows, buyable doors, wall buys, mystery box, perks, power switch, workbenches, salvage and scrap;
  - dropped builds on death, and 2 → 4 weapon slots;
  - power-ups (Max Ammo, Insta-Kill, Double Points, Nuke, Carpenter, Infinite Ammo since 2026-10-23);
  - special ammo effects for every part flag;
  - solid machine props;
  - the first real map, **Blackmoor Asylum** (`Maps/Zombies/01-BlackmoorAsylum.json`, 7 areas);
  - procedural zombie animation (tearing boards, vaulting, rising from the ground, gaits, swipes); at a window the hit
    box follows the drawn body (`ZombieAnim.BodyShift`: the server shifts the lag-compensated history entry, the
    client its hit prediction), so shots at what you see land from every angle (fixed 2026-10-23);
  - Disassemble at the workbench (empty-hands slots);
  - slower health scaling: +60 a round to round 9, then ×1.06 a round.

**Weapon builder / workbench** (see `docs/zombies/WEAPON-BUILDER.md`)
- Grip-first drag-and-drop editor.
- Double-click a part to remove it.
- "Show incompatible parts" checkbox. Parts you have zero of are hidden, and so are categories that can't fit the current build.
- Barrels, splitters and muzzles fit every receiver; the receiver sizes them.
- Chain fire is a per-build checkbox.
- Grips exist for every slot count from 1 to 9.
- 23 receivers. Receivers set the calibre only.
- **28 Action parts** set the fire type. Trigger groups sit under the receiver; bolts, cranks and drives sit on the side; the pump and ramrod sit at the front.
- 64 standard builds, 59 of them in the mystery box.
- Model connectivity check: no floating boxes.

**Bots**
- Nav graph built deterministically, with risky edges walked by the real mover.
- Trap areas, and a stuck watch with self-kill.
- Vantage points for long-range weapons; foliage blocks their sight.
- Insane squads.
- Open doors ahead of them and shut them sensibly. Co-op enemies use doors; zombies bash them.
- Loadouts match the lobby's level.

**World**
- Map packs: Legacy (the old Classic, Collision Point and High Ground packs), Cluttered, World Tour, Space (Stratos Station and the spherical Pocket Planet), Zombies, Dev.
- Destructible buildings with bullet penetration.
- Props, decals, terrain holes and caves.

**Player**
- 120 HP with faster regen. Blood by health, ragdolls, gibs, blast push.
- Kill streaks, frenzy, launchers, landmines.
- Text chat, team chat and proximity voice.
- Pre-match lobby, and a scoreboard that shows levels instead of HP.
- **Appearance** menu: the hat moved there from the loadout, plus every bot look axis, with a live preview. Saved in `appearance.txt`.

**Infrastructure**
- Saves live in `%APPDATA%\CallOfSlop`, with automatic migration from older builds.
- In-game updater that installs GitHub releases.
- Release workflow.
- Invite codes, STUN, UPnP, and an automatic server list started by hosts.
- F8 audio mixer, -50 to +50 dB, with a limiter on boosted music.
- Self-test section filter, and the `[tests: ...]` tag on branch CI.

## In progress

- **feat/mapkit-tests** (map development tooling, to merge after the release): the "Map contract" section (`Source/App/SelfTestMapContract.cs`) checks every map against its file's optional `test` block (Maps/README.md, "Testing a map") and replaced the Space pack's six bespoke sections (Space: Stratos Station, refinery and processor, arboretum and reactor, vault and bazaar, shipyard and outpost, gravity maps; those names are gone from `--selftest-list`, only "Space: gravity physics" remains). A new map gets a `test` block, not a section; `--check-map FILE` / `--check-maps DIR` run the same checks on files and print each result. The Space generators (`tools/space/*.py`) don't write the `test` block yet: re-generating a map drops it until they do (the block is inserted after `"maxPlayers"`, json indent 1).

Otherwise nothing. All agents finished and every branch is merged. A weekly scheduled task (Mondays, 8:51 Chicago time) reminds the user about the Foxhole-style sandbox idea; see the project doc `claude/ideas-backlog.md`.

- **feat/gravity-strength** (gravity-maps): (1) the "Matches: Keelhaul Yard: N player(s) below the floor" failure: partly
  the check (on a void map it compared with y = 0, and Keelhaul's hold floor is at -3; now the fall-kill height, as the Map
  contract does), partly real: bots walked off unrailed dock edges and the yard's corners (railed now, the deck runs under
  the hull's ends), crawled under Twin Shard's tethers where they enter the rocks (filled; the Line's zone covers only its
  deck), and anyone who fell off outside play (warm-up, countdown, results) hung alive 5 m under the map; the server now
  puts them back on their nearest spawn (`Game.FallRescue`). "Space: gravity physics" plays seeded matches on both maps and
  checks nobody is alive under the map. (2) The host's *Gravity* setting (25-200%, protocol 48) and Pocket Planet at
  11 m/s2 with a 60% floor; section "Gravity strength".

## Open bugs and loose ends

**Gameplay and visual**
- **Parts that still float** (found 2026-10-23 while fixing the first three; `tools/parts/floaters.py` does not cover grip combinations, so check with a script that reuses its box logic): Wire Stock, Shoulder Harness, Bayonet and Cup Holder on some grips. None is used by a standard build.
- **Action placement overlaps (accepted):**
  - drum mags, belts and hoppers can overlap the under-receiver trigger group;
  - the pump forend clips the bottom barrel on the Hellwheel and Pepperbox.
- **Empty hands in third person:** another player with an empty slot may hold an invisible gun.
- **Menu and lobby music:** can click once when the slider crosses 0 dB (the limiter switches on).
- **Test Range:** kill XP can be farmed (still an open question). The host's Esc > HOST page (bots, BACK TO LOBBY) was
  added 2026-10-23 (HostControls.cs); nothing on the server can end the match or change the map (the "Test Range"
  section scripts 17 kills each for the player and a bot, a 20-minute clock jump and a direct end request).
- **Never checked on a real screen:**
  - Blackmoor's look and FPS;
  - zombie animations;
  - the Appearance screen layout and preview;
  - action models;
  - the Tipped Farm Cart fix;
  - the Bank's new west side door.

**Flaky tests (passed on rerun; root cause unknown)**
- "Bot stuck watch": the pit bot doesn't self-kill within 9 s. Windows, once.
- "Civic Buildings": a single 507 ms wall-clock tick, from a runner stall. Linux, once.
- "Maps" tick work on Main Street: one 9.6 ms server tick (limit 8 ms; no JIT, no GC) on Windows CI, run 40012259653 on 763445c (2026-10-23); passed on rerun. If it recurs, suspect the attachments branch's bot hearing (loudness) or the gravity-zone field lookup.
- "Matches" on Keelhaul Yard: "4 player(s) below the floor" once in the agent container (2026-10-23, fix/test-range-qol, unseeded bot match on the gravity-zone map); passed on rerun.
- "Space: gravity maps" (now the "Map contract" bot match on Twin Shard): "3 bots barely moved in a KingOfTheHill match" on main's Linux run 40035032503 (2026-10-23). The seeded 12 s match is the same every run on one machine but not across machines (float differences); the map's `test` block allows 4 stuck bots since.
- "Audio mixer meters": 1,800 bytes allocated. Linux, once. Again 6,984 bytes once in the agent container under load 15 on 2 cores (2026-10-23), with "boosted music costs 1.51x" in the same run; both passed on rerun.

Fixed 2026-10-23: "Test Range" ("a killed bot didn't respawn" / "the kill wasn't counted", ~6% of runs). The check read one bot's state at a single instant while the other bots kept fighting: a bot already in Last Stand (1 HP, shield up) ate the test's shot, or the victim was killed again by another bot within a second of respawning. The check now clears the shield and watches the kill and the respawn as they happen (0 failures in 300 runs).

Fixed 2026-10-23: "Test Range" ("adding bots mid-session gave 5 (4 alive)", main run 40035041351; ~5% of runs). The host-bots check counted live bots half a second after adding them, while the bots fought: a newcomer could already be dead. It now checks the newcomers are alive straight after the call (before any tick) and only the roster after the ticks (0 failures in 200 repeats; the old reading would have failed 10 of the same 200).

**Environment**
- The cloud agent container has no .NET SDK, and the proxy blocks dot.net, builds.dotnet.microsoft.com and packages.microsoft.com, so agents cannot build or run the self-test locally. They verify with `floaters.py` and branch CI (`[tests: ...]`) instead. Allowing those hosts under Settings > Capabilities would let agents build locally; restoring the Ahwoo packages would still need the NuGet API, which the user's rule forbids.

## Key decisions and why

- **Fire type comes from Action parts; receivers are calibre (user, 2026-10-23).** Fire behaviour should be built from parts.
  - Old builds get their receiver's `stockAction` on load.
  - Five receivers changed rate slightly to share actions; the user kept this: Bastille 600, Plinker 400, Reaper 400, Railspike 45, Sparkplug 720.
- **Action placement is mixed (user).** Triggers go under the receiver, mechanical actions on the side.
- **Universal barrels (user).** Every barrel fits every receiver, and the receiver sizes and scales it, to cut workbench friction. Feeds still match the receiver; ammo is universal since 2026-10-23 (see WEAPON-BUILDER.md, "Universal ammo").
- **Mystery box gives complete builds only. Fire style across receivers is the Chain fire checkbox. The push cart is a manned gun, not a turret.** These are user decisions from milestone 1–2; see the docs.
- **Bleeding out drops your builds as pickups for 5 minutes (user).**
- **Server listing:** invite codes plus server lists that hosts start automatically. **No DHT** (user rejected it).
- **Saves live in `%APPDATA%`** so progress survives new builds.
- **Releases are unsigned for now.** Azure Artifact Signing is planned later, at about $10/month. Files installed by the in-game updater carry no Mark-of-the-Web, so updates avoid SmartScreen.
- **Nav graphs are built from the map as built**, so runs are deterministic; frame-clipping window climbs are only a last resort. This was the root cause of the flaky Rooftops and Mall tests.
- **One-off JIT is left out of the 8 ms tick-work rule.** It is warm-up, not steady cost, and it was blocking releases.
- **Zombie health scales slower (user).** At round 20 it is 1,196 instead of 2,711.
- **Zombies round flow (user, 2026-10-23; branch `feat/zombies-downed-flow`, protocol 47).** Downed players crawl
  (0.8 x prone speed, no stand / jump / buy / bench / revive; crawling out of reach interrupts a revive; downed bots crawl
  to the nearest teammate standing). The dead spectate living teammates over the shoulder (Right / Left arrows or
  Fire / Aim), and come back when the round's last zombie dies. Rounds wait for ready-up (F3; bots ready, the dead
  don't block; 3-minute fallback, host presses F3 again to force). Bots-only runs keep the 10 s timer. See
  ZOMBIES-MODE.md, "Between rounds, downs and spectating"; self-test "Zombies flow".

## Open questions for the user

Defaults are in place for all of them.

1. **Zombies:**
   - ~~the power-up numbers~~: answered 2026-10-23 (the user asked for Max Ammo to be among the most common and for an
     Infinite Ammo one): 4% a kill, at most 4 a round, timed ones 30 s; weights Max Ammo 2.5 (about 38% of drops),
     Insta-Kill 1, Double Points 1, Nuke 0.8, Carpenter 0.8, Infinite Ammo 0.5 (about 8%). Still tunable in ZombiesRules.json;
   - should special ammo hurt players in other modes;
   - should co-op bots buy things;
   - downed weapon: today it's the gun in your hands; the crawl speed (0.8 x prone);
   - ready-up: 3-minute fallback, 3 s countdown, host force start by pressing F3 again; spectating over the shoulder only;
   - melee builds from the box;
   - Second Wind's price (1500) and whether it counts toward the perk limit;
   - Blackmoor's layout and prices, and more maps.
2. **Actions:** should the Break Action and Muzzleloader (slow novelties) be tuned?
3. **Test Range:** keep kill XP?

## Recommended next steps

1. Play-test on the PC what has never been seen on screen: Blackmoor, the zombie animations, Appearance, the action models. Fix what looks wrong.
2. Cut the next release so testers get Appearance and the action placement: Actions → Release, or ask the agent.
3. Get answers to the open questions above, one at a time, then tune `Parts/Tables/ZombiesRules.json`.
4. Fix the four remaining floating parts.
5. Look into the four flaky tests if they recur.
6. Backlog:
   - Zombies: more maps, perk machine visuals, bots buying things;
   - co-op phases 2–4;
   - planet-map limits (mantling, death cam, Insane squads, flying streaks);
   - ~~baking the user's audio mix as the new defaults~~ done 2026-10-23: SeabMix2 is the built-in mix (`AudioMixDefaults.cs`; old saves move untouched levels to it);
   - Azure code signing;
   - the Foxhole-style sandbox mode.

## How the previous session worked

- One lead session spawned agents in git worktrees, one branch each, and merged them into `main` with `--no-ff`.
- After each merge it checked CI and pushed the PC copy as a git bundle to `A:\AI Creations\BRUTAL\CallOfSlop\bin\update.bundle`, then fast-forwarded it there.
- The shared agent brief and the user's rules are mirrored in `CLAUDE.md` and the project doc `claude/agent-rules.md`.
