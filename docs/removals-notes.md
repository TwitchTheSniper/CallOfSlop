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
- **Wire (protocol 344).** `Msg.Tutorial`, `ColonyAlarm`, `Depot`, `Quarry`, `Objective`, `Labour`, `LabourHands`,
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

(in progress)

## 1. Levelling and prestige

(to do)
