# HANDOFF: state of Call of Slop (2026-10-23)

**Current state:**
- `main` is at `723cd91` (three fix branches merged on 2026-10-23: readme-dedupe, socket-marker, floating-parts).
- Protocol 45.
- CI on main: the full Linux suite passed on `e6828e2` (run 39932715846, 156 sections, 0 failures). The last Windows run passed on `814ffbf`, the release build; Windows hasn't run since the Appearance, action placement and test-filter merges, so push with `[windows ci]` before the next release.
- The first public release is **v0.2026.1023.7**, built from `814ffbf`. The appearance screen, action placement and test filter came after it, so they are not in a release yet.
- No agents or branches are in progress.

## Done (all merged to main)

**Modes**
- Free-for-all, Team Deathmatch, King of the Hill, Capture the Flag and Gun Game.
- **Test Range** (id 8): endless free-for-all. Nothing ends the match or changes the map.
- **Co-op PvE** (id 7): enemy soldiers and a spawn director. One mission so far: `Maps/World/Missions/SiloBreach.json`, on Silo.
- **Zombies** (id 5), milestones 1 to 3 (see `docs/zombies/ZOMBIES-MODE.md`):
  - rounds, boarded windows, buyable doors, wall buys, mystery box, perks, power switch, workbenches, salvage and scrap;
  - dropped builds on death, and 2 → 4 weapon slots;
  - power-ups (Max Ammo, Insta-Kill, Double Points, Nuke, Carpenter);
  - special ammo effects for every part flag;
  - solid machine props;
  - the first real map, **Blackmoor Asylum** (`Maps/Zombies/01-BlackmoorAsylum.json`, 7 areas);
  - procedural zombie animation (tearing boards, vaulting, rising from the ground, gaits, swipes);
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

Nothing. All agents finished and every branch is merged. A weekly scheduled task (Mondays, 8:51 Chicago time) reminds the user about the Foxhole-style sandbox idea; see the project doc `claude/ideas-backlog.md`.

## Open bugs and loose ends

**Gameplay and visual**
- **Zombie hit box at windows:** the zombie is drawn up to about 1 m ahead of its hit box while it reaches for the boards. Side-angle shots can miss. Proposed fix, not yet answered by the user: move the outside stand spots closer to the windows in the maps.
- **Parts that still float** (found 2026-10-23 while fixing the first three; `tools/parts/floaters.py` does not cover grip combinations, so check with a script that reuses its box logic): Wire Stock, Shoulder Harness, Bayonet and Cup Holder on some grips. None is used by a standard build.
- **Action placement overlaps (accepted):**
  - drum mags, belts and hoppers can overlap the under-receiver trigger group;
  - the pump forend clips the bottom barrel on the Hellwheel and Pepperbox.
- **Empty hands in third person:** another player with an empty slot may hold an invisible gun.
- **Menu and lobby music:** can click once when the slider crosses 0 dB (the limiter switches on).
- **Test Range:**
  - bot count and difficulty can't change mid-session;
  - in a hosted game the map only changes by leaving and hosting again (idea: a host-only "back to lobby" pause option);
  - kill XP can be farmed (still an open question).
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
- "Audio mixer meters": 1,800 bytes allocated. Linux, once. Again 6,984 bytes once in the agent container under load 15 on 2 cores (2026-10-23), with "boosted music costs 1.51x" in the same run; both passed on rerun.

Fixed 2026-10-23: "Test Range" ("a killed bot didn't respawn" / "the kill wasn't counted", ~6% of runs). The check read one bot's state at a single instant while the other bots kept fighting: a bot already in Last Stand (1 HP, shield up) ate the test's shot, or the victim was killed again by another bot within a second of respawning. The check now clears the shield and watches the kill and the respawn as they happen (0 failures in 300 runs).

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

## Open questions for the user

Defaults are in place for all of them.

1. **Zombies:**
   - the power-up numbers: 3% a kill, at most 4 a round, 30 s;
   - should special ammo hurt players in other modes;
   - should co-op bots buy things;
   - downed weapon: today it's the gun in your hands;
   - melee builds from the box;
   - Second Wind's price (1500) and whether it counts toward the perk limit;
   - Blackmoor's layout and prices, and more maps.
2. **Actions:** should the Break Action and Muzzleloader (slow novelties) be tuned?
3. **Zombies at windows:** move the stand spots closer, so the hit box matches the drawing?
4. **Test Range:** keep kill XP?

## Recommended next steps

1. Play-test on the PC what has never been seen on screen: Blackmoor, the zombie animations, Appearance, the action models. Fix what looks wrong.
2. Cut the next release so testers get Appearance and the action placement: Actions → Release, or ask the agent.
3. Get answers to the open questions above, one at a time, then tune `Parts/Tables/ZombiesRules.json`.
4. Fix the window hit-box offset and the four remaining floating parts.
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
