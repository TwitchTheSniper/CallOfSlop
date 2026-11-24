# Foundry tiers (2026-11-24)

The Foundry became a block machine (plan PR 2: docs/sandbox/constructs/production.md "The Foundry"), so its tiers went:
`Constructs/Parts/Refining/FoundryT2.json` to `FoundryT5.json` (Workshop, Industrial, Military and Plasma Foundry), the Foundry line of the
part kit's `machine_tiers` (`MachineTiers.cs`, its shape in `TierStyle.cs` and `TierMachine.cs`: the furnace, its hopper and tray), their
four Assembler recipe rows and the Foundry's recipe gates (bronze 2, circuits 3 in `Refining.json`).

- **Kept:** the Foundry's file and name (now the Core, from `FoundryParts.cs`), all its mixes, its Assembler row; the Sawmill keeps its tiers.
- **Saves and blueprints:** the four names load as the Core (`ConstructParts.Renamed`); a saved recipe keeps its recipe (each old tier's
  list was a start of the full one).
- **Rebuilt:** the Production Showcase (a Core, a 2 x 2 and a 1 x 1 with Mould Lines), the Militia refineries (`--gen-factory refinery`);
  the Zero Day works keeps a bare Core where each Foundry stood; the Foundry Works and the Starter Foundry keep a bare Core.

# Smelter tiers (2026-11-24)

The Smelter became a block machine (plan PR 1: docs/sandbox/constructs/production.md "Refiners as block machines"), so its tiers went:
`Constructs/Parts/Refining/SmelterT2.json` to `SmelterT5.json` (Workshop, Industrial, Military and Plasma Smelter), the Smelter line of the
part kit's `machine_tiers` (`MachineTiers.cs`, its shape in `TierStyle.cs` and `TierMachine.cs`), their four Assembler recipe rows and
the Smelter's recipe gates (copper 2, gold 3, levite 4 in `Refining.json`).

- **Kept:** the Smelter's file and name (now the Core), its Assembler row; the Foundry and Sawmill kept their tiers until their own PRs (the Foundry's went the same day, above).
- **Saves and blueprints:** the four names load as the Core (`ConstructParts.Renamed`), and a saved recipe choice keeps its recipe
  (`ConstructRefining.SavedRecipe`).
- **Rebuilt:** the Zero Day works' copper smelter (Core and a 1 x 1), the Production Showcase, the Militia refineries (`--gen-factory
  refinery`); the Starter Smelter and Smelting Station keep a bare Core.

# The IT vehicles (2026-11-21)

Karl asked (2026-11-21) for the IT vehicles to go as well: "We will work on implementing new vehicles for the new factions
down the line". Deleted: the IT style (`Styles.It`) and its 47 blueprints, its per-style tables in the vehicle kit
(identity, context, family names, roles, cargo, body language, chassis, body and wear), its loot tables (`it`, `it 3`,
`it 4`), its interior palette, its armoury names and the 8 recipes only it used. `Styles.Shipped` is empty; Militia is the
kit's one style and still makes the ore loop's Ore Hauler and Excavators. The Traders' `"styles"` is empty.

- **Kept for the new styles:** every generator and shape, the archetypes (their weights still name IT, so none is taken)
  and the IT-flavoured parts (Office parts, the Server Rack), which the ships and the IT desk use.
- **Placeholders:** the powers' fleets (`Crew/Controllers.json`) name the hand-built 4x4 Truck (cargo) and Hood Gun Buggy (every armed role);
  the crew checks fly the hand-built Small Helicopter and the convoy check drives the hand-built ground vehicles (its
  followers now keep 9.7 m off their places, inside the 20 m limit). Warm-ups and help text name Militia.
- **Left alone:** the ships' IT-style paint (the Zero Day, the Satchel, the Packet Spine) and the `it` dress style.

# The story engine (2026-11-21)

Karl asked (2026-11-21) for the original story elements to go with the old factions, files and notes too. It had been
switched off since 2026-11-14 (`Game.StoryOn`); now its code, data and docs are deleted:

- **Code:** `Source/Story/` (the story world, sifter, patterns, people and their plans, drama, lines and radio, plays,
  places, the roster, the story's factions, fragments, the IT and Salvager desks' story side), `Source/StandIns/`, the
  game's side (`GameStory*.cs`, `GameUniforms.cs`), the windows (`UiStory*`, the Logbook, ranks, the Job Board), the
  client's talk, lore, logbook and job board files, the talk camera, and the Data Centre Terminal and Sealed Data Centre.
  The window tool's `talk`, `say` and `standing` verbs went; the rest of its script verbs are in
  `Program.WindowShotScript.cs`. Every part counts as taught (no story faction teaches tool upgrades or recipes).
- **Data:** `Story/` keeps only `Starts`, `Opening` and `Office`. The uniforms are the Space Suit alone.
- **Wire:** protocol bumped; the story's messages keep their `Msg` slots as `Removed*`, `CActKind.Talk` is `RemovedTalk`.
- **Keys:** the Story window, Logbook and Join talk actions keep their slots, unbound and hidden.
- **Saves:** a world's Story section is no longer written; one saved before is read without it. Old saves don't matter
  (Karl, 2026-11-15).
- **Docs:** `docs/archive/story`, `docs/archive/colony`, the story-writing and world-site skills and the story rules file
  were deleted; MANUAL, SURVIVAL, WORLDS, STARTS, OPENING, UNIFORMS, TOOL-UPGRADES and the building docs were cleaned.
- **Left alone:** the maps' kept flat sites and survey posts (the map owner's), the building pieces of the colony's site
  (`BuildingParts.Site.cs`), the crew powers' standing rows (`FactionTable.StoryNames`, which Twitch's powers use).

# Old factions, part 2 (2026-11-21)

Karl asked (2026-11-21) for the last of the old story factions to go: Scam, British, Scottish (Highland), Ministry of Slop, Bandit
and Highland, with their parts, vehicles and references. The crew factions (Traders, Mercenaries, Raiders, Militia, Salvagers,
Player Crew, Test Hostile) stay; Twitch's faction system runs them on ships.

- **Bandit, British and Scam vehicles gone** (PR 434 first renamed them Raider, Mercenary and Salvager; Karl: remove them, not
  rename them). The three styles, their 141 blueprints, their per-style tables in the vehicle kit (signature, body language,
  chassis, wear, roles, cargo, family names, context, the archetypes only they built), loot tables, interior palettes and the 15
  armoury recipes no other style used are deleted. The Raiders, Mercenaries and Salvagers name no style now (`"styles": []`, as
  Player Crew). Only IT ships vehicles (51 blueprints with Militia's ore-loop pair); its parts and gear come from
  `Styles.Base` (the old Bandit lists, never generated itself). Militia's loot table is `militia` (the old `raider` table). The
  controllers' placeholder fleets (Crew/Controllers.json) for the Raiders and Militia use IT vehicles, as do the crew checks
  that spawned Raider helicopters and convoys. The Double Decker, Monster Truck and Wagon generators stay, shipped by no style.
- **Highland style gone:** its 50 blueprints, its slots, signature, body language, chassis, wear, family names, loot tables and
  palette row. The Traders fly IT alone. Its hull-loft kinds (clinker trim, timber stakes, timber lining, ports) stay in
  `HullLoft` unused. The vehicle armoury lost its Highland rows.
- **The Scammers gone:** the Computer's two advert buttons, `GameSandboxScams`, `ScamDesk`, `Story/Office/Scammers.json`, the
  inbox lines in chat and IT's malware fax. The Computer still emails IT.
- **Parts moved, not removed** (their Parts tab categories were already by function and didn't change): the Bandit set is the
  part kit's `scrap_parts` (`Constructs/Parts/Scrap`), the cone and gnome `street_parts` (`Constructs/Parts/Street`), the
  loudhailer, phone mast and water cooler `signal_parts` (`Constructs/Parts/Signals`). The unused Ministry mine pieces
  (`Constructs/Parts/Story`: Mine Face, Pit Prop, Rubble) were deleted. The cold war fittings lost their Ministry descriptions.
- **Left alone:** `Props/British` and the London dev map (the map owner's); "highland" as a word for Caledon's ground; the old
  vehicle kit history (`docs/archive/vehiclekit/BODY-STYLES-HISTORY.md`). The story engine is removed separately.

# Old production blueprints (2026-11-21)

Sean chose to delete them (2026-11-21) when the Press Core on the block system no longer fitted their old spots: the
**Titan Walker Fitted** (`TitanWalkerFitted.json`, its Presses in workshop rooms), the **Factory Showcase**
(`FactoryShowcase.json`, with its generator `Source/PartKit/Generators/FactoryShowcase.cs`) and the **Works Runabout** the
showcase's station built (`WorksRunabout.json`, from the same generator). A new blueprint is built once the block-machine
changes are in, and what still uses the old production pieces is cleaned up then. The bare Titan Walker stays;
`--interior-walk` now defaults to it. A saved world that holds one of these keeps its bodies; only the blueprints are gone.

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
