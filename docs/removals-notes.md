# Removals (2026-11-17)

The user approved a batch of three removals (2026-11-17), done on `claude/removals` in this order. The survey that listed
every path is the coordinator's; this file is what was done and what changed for old saves.

## 3. The penal colony tutorial (Karl's)

- **Starts.** `Story/Starts/PenalColony.json` is gone, with the Survival Tutorial tickbox (`Settings.SurvivalTutorial`,
  key `survival_tutorial`: an old settings file still loads, the key is skipped). The built-in fallback and the menus'
  default are the Rust Bucket. A start file's `"tutorial"` must be `"none"` or left out; an opening's `"colony"` is refused.
- **Old saves.** A world saved without a start (one from before starts: every such world was a penal colony world) plays
  `StoryStarts.Legacy`: no opening. Its colony, barracks, depot, quarry and Ministry buildings stay as plain constructs.
  The save now writes the start for every world (`STRT`; before, the default's was left out). The `Opening` section
  keeps only the map, the start's ship, the crash site and who has been through it; an older world's tail (barracks,
  depot, data centres, quarry, buildings) is left unread. The `TUTR`, `DEPO` and `QUAR` sections are no longer written
  and are skipped like any unknown section (their tag constants stay so nobody reuses them).
- **Wire (protocol 348).** `Msg.Tutorial`, `ColonyAlarm`, `Depot`, `Quarry`, `Objective`, `Labour`, `LabourHands`,
  `Breakout`, `Errand`, `LabourDay` and `LabourIncident` keep their slots as `Removed*`, never sent. `CActKind.QuarrySkip`
  and `ErrandDrop` keep their bytes and do nothing. `RespawnSpot.Colony`, `Sfx.ColonyAlarm` and the unused
  `OpeningStage` beats keep their bytes. The colony's lore and building parts went, so part ids move.
- **Code gone.** The induction (GameTutorial, Tutorial, GameTutorialBounds, UiTutorial), the colony's buildings, site,
  lockdown and duty-desk PC (GameColony*), the quarry duty and its bus (GameQuarry, UiQuarry, Program.Quarry), the labour
  gangs (GameLabour*, LabourRender), the Orbital Depot (GameDepot, UiDepot), the errands (GameErrands, Errands, UiErrand),
  the break-out (GameBreakout*, UiBreakout), the spaceport (GameSpaceport), the "what next" goals (GameObjectives,
  UiObjectives), the pacing numbers (GamePace), the colony's nav sites (GameNavSites), the story people's posts
  (GameStoryPosts), the stand-ins' ambient traffic (GameTraffic, TrafficCheck, `--traffic-check`) and the factions'
  outposts round the colony (GameOutposts, OutpostData, OutpostCheck, `--outpost-check`, BuildingParts.Outposts). The
  opening (GameSandboxOpening) keeps its ship and wreck starts.
- **Data gone.** Story/Tutorial, Duties, Labour, Depot, Errands, Posts, Traffic, Outposts, Objectives.json, Lore/PenalColony,
  the colony's Breakout and Errands story files, Opening/Opening.json, ColonyBuildings.json, Spaceport.json, Breakout.json;
  Constructs/Parts/Lore (LoreParts); the blueprints Penal Colony, Colony*, the Ministry's buildings, Remote Works, Orbital
  Depot, Depot Lander, Ministry Courier, Spaceport*, the five outposts. Site, street and Brutalist parts stay (generic).
- **Kept.** NavSites.cs (the graph's site machinery, unused now), the story fragments' planetoid sites, uniforms (a user
  decision), SynthAlarm (the tannoy uses it).
- **Bench.** `tools/bench/glacier.json` keeps only `spine_ridge`; older baselines are not comparable
  (docs/perf/BENCHMARK.md). The play-test tour keeps its ice spots.
- **Docs.** The colony's docs moved to docs/archive/colony (TUTORIAL, DUTIES, LABOUR, DEPOT, SPACEPORT and the old
  OPENING); OPENING.md, STARTS.md, SURVIVAL.md, DEATH.md, UNIFORMS.md, WORLDS.md, MANUAL.md and CLAUDE.md edited.

## 2. Old faction relics

- **The vehicle styles stay** (Raiders fly Bandit, Mercenaries British, Salvagers Scam, Traders IT and Highland): their
  shapes, palettes and seeds are as they were. What went is the old factions' dressing and names: each style's Blueprints
  tab group is now its style's name ("IT", "Scam", "Highland", "British"), its guns, model names (VehicleFamilyNames)
  and room layouts are renamed for the faction that flies it, and its trophies, masts, gear and spikes are generic pieces.
- **Bagpipes.** The Pipe Box, `ConstructPipes` (it ran once a second in the server tick), `MusicPipes` (the pipe tunes and
  their renderer), the catalogue's pipe group, the "piper" slot (`Vehicle.Piper`) and the kit's Pipe Box check are gone.
- **Parts gone** (with their generators' code, recipes and loot rows): the Highland set (HighlandParts: Saltire Flag,
  Thistle, Coo Heid, Haggis on a Post, Wee Caber, Wee Keg, Whisky Cask, Pipe Box); British: Union Flag, Teapot, Rain
  Gauge, Furled Brolly, Belisha Beacon, Queue Post, Pillar Box, Phone Box, Tea Urn; the scam call centre: Office Phone,
  Call Headset, Gift Card Sign, Prize Wheel, Cubicle Desk; IT jokes: CRT Monitor, Employee of the Month, Cable Reel.
  Kept as generic: Traffic Cone, Garden Gnome, Loudhailer, Phone Mast, Water Cooler, the Office set and the Fax Machine
  (the office desk mechanic, GameSandboxOffice, still uses it), the Bandit set, the site, street and Brutalist parts.
- **Buildings.** The Ministry's building helpers (BuildingParts.Ministry) went; the concrete palette is
  BuildingParts.Concrete. The outposts went with section 3.
- **Story.** Story/Factions (Ministry, Clan MacSlop, IT Department, Call Centre) and Scenarios/ITDepartment are gone; the
  archived story loads with no factions (`--check-story` passes).
- **Committed vehicles** regenerated once (188 of 238 changed, the 50 Bandit ones as they were; file and blueprint names
  kept); `--gen-vehicles` is identical after, `--check-construct-parts` refuses nothing.
- **Old saves.** A world or player blueprint with a removed part loses that part (REFUSED in the log, the body loads).
  No wire change: parts are data, and part ids moved already under the protocol bump.
- **Left alone.** Constructs/VehicleArmoury.json's per-style gun names (some are old-faction jokes: "Flak in a Teapot",
  "Twa Dugs"); the planetoid and system descriptions (the map owner's, made by tools/planets/make_worlds.py); the office
  desk's "IT Department" voice (Story/Office); the vehicle kit's history docs.

## 1. Levelling, prestige and the prestige hats

- **Gates first.** `LoadoutStore.Active()`/`Effective(l)` swap only what doesn't fit a slot; every loadout slot is open
  (`LoadoutStore.MaxSlots`); `Attachments.Sanitize(w, kit)` drops only what doesn't fit; `BotGear.Full` is 1, so every
  bot gear check passes and bots draw loadouts as they did at the old max level (Sandbox spawn guns come from
  `LoadoutStore.Active()` as before). `Attachments.UnlockXp`/`MaxXp`/`IronSightsXp` stay only as list order (archived
  gunsmith screens).
- **Gone.** Progression.cs (account XP, levels, the unlock table, weapon XP, prestige), UiPrestige, the HUD's XP bar,
  popups and level-up banners, the loadout screen's Progression page and level bar, the Settings page's Reset level,
  the scoreboard's and lobby room's rank tags, kill/match/co-op XP awards. The Gun Game ladder goes by the weapon list's
  order; perks list starters first.
- **Hats deleted** (the user's decision): Cosmetics/hats.json, `HatId`, the catalogue, `Player.Hat`/`Prestige`/`Level`,
  the APPEARANCE screen's hat row, the hat icons. HatDef/HatRender stay for the uniforms' caps.
- **Wire (protocol 348, one history line with the colony's).** The roster entry loses prestige, level and hat (3 bytes);
  the Loadout message loses its hat byte and the prestige and level after it; the Hello keeps two zero bytes where
  prestige and level were, so an older build's version is still read where it sends it and it gets a clean refusal.
- **Saves.** progress.txt left `SavePaths.SaveFiles`, tools/package.ps1, tools/MIGRATING.md and .gitignore; an old one stays
  on disk unread (still on the update checker's never-ship list). `SavePaths.MarkerFile` (settings.txt) is the new mark of
  a build's saves for `FindPreviousBuild` and a save folder's first run (builds before this have it too). appearance.txt's
  old `hat=` line is skipped and not written again; loadout.txt loads as it was, with nothing locked.
- **Self-test.** "Save folder" now uses loadout.txt and plain `SavePaths` lines for its migration, atomic-write and
  newer-format checks; the archived "Loadouts and perks", "Attachments", "Weapon files", "Weapon select", "Loadout
  navigation", "Launchers", "Sniper scopes", "Gun Game", "Ragdolls" (a uniform's cap) and "Mode switch reset" were edited
  to build. No section added; MaxSections unchanged.
