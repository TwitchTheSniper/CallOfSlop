# Survival and Creative

Karl (2026-11-14), after a talk with TwitchTheSniper: the game turns away from the storyline towards Survival and
Creative. This file is the direction for Survival and what was done to get there. The building system, the constructs and
the space layer are unchanged; the code guide for them stays `docs/sandbox/CONSTRUCTS.md`.

## The two ways to play

- **Survival.** Gather, refine and make everything you build; no parts menu away from an Inventory Beacon. Death rules
  as before (`GameSandboxDeath.cs`).
- **Creative.** Every part and blueprint on the Tab parts menu, free; nothing to gather.

The main menu has **Single player** and **Multiplayer**; each offers Survival or Creative and a start (`Story/Starts`,
docs/story/STARTS.md):

- **Rust bucket** (first, both modes): wake aboard your own patched freighter in orbit over Glacier.
- **On the ground** (both modes): the map you pick, with the start kit by its parts rack in Survival.
- **Magpie** and **Zero Day**: a mining ship or a frigate over Crumb.

The penal colony tutorial (the Ministry's colony on Glacier and the warden's induction) was removed (2026-11-17,
docs/removals-notes.md): Survival has no tutorial now.

Multiplayer's HOST A GAME opens the lobby with the same choices; the lobby keeps Creative or Survival, the saved world and
the limits, and no longer offers the archived arena modes for a Sandbox game.

## Where Survival is going

Karl's direction, from the talk with TwitchTheSniper and his review of the Design Roadmap notes (2026-11-15; the review
page is the Artifact https://claude.ai/artifact/VULKVwr3ZSKKVUuije6gnJ). None of it is built yet. The notes behind it were
a brainstorm, so their examples are not rules: only what is listed as decided below is decided, and the rest is open until
Karl says otherwise.

### Decided

- **What the game is.** A production and simulation game about logistics, wars and diplomacy. No story and no ending.
  Capital shipyards and big battles are the current progression marker; never call them an endgame.
- **Logistics is the game.** Ships carry what the player needs; keeping those lines running, and defending them, is the
  play. Threats to a supply line are shown and flagged (enemy ships heading for a place or a route).
- **One star system, five planets,** for now. Today's `Systems/` still holds four faction systems plus Dev; that changes
  when this is picked up.
- **Gated resources.** Factions hold certain resources by holding the planets they are found on, so what a player can
  make depends on what they can reach. A costly lift-off (high gravity, thick air) is a gate on some worlds only, and needs
  a powerful ship. Hazards start with a few. Ice is one resource among several, nothing special.
- **First goal: a mining ship.** The player's first big build digs, paired with a small refinery station or a refinery on
  the ground, depending on the gating. Where a resource is refined is per resource: on the ground when refining sheds mass
  before lift-off, in space when the process needs it (zero g). A refinery ship only makes sense at a huge size and is not
  needed now. The first thing mined may be a found asteroid as well as a planetoid.
- **Then a combat ship,** to take on a faction and win the parts and resources it holds.
- **Capture and salvage** are in: derelicts, and independent ships making a name for themselves.
- **Ships see other ships** (radar and lock-on).
- **The reactor cap is a balance device.** A ship scales through burst, which costs hull space for capacitors; that stops
  missile and meme-boat spam. Station reactors are heavier and hold more, and don't pay their way on a ship.
- **No currency: barter and a tab (Karl, 2026-11-20,** replacing "currency is in"). Goods and parts are physical and carry a value; an uneven
  trade leaves a tab the power owes you, spendable only with that power, wiped by war or the power's fall (`Game/GameTabs.cs`). NPCs have needs and wants.
- **NPC factions are kinds,** such as traders and mercenaries, details not fixed. The story's factions (IT Department, Clan
  MacSlop, the scammers, the Ministry of Slop) are archived with the story; the player faction system (F2) stays.
- **NPCs are streamlined into assets;** the storylets stay archived.
- **Robot shells come early.**
- **Nothing happens while the game isn't running,** and nothing saves a player whose tank is empty, for now.
- **Ships are built through TwitchTheSniper's production system** (machines, recipes, ship building). It is work in
  progress and theirs: agents don't change it, and anything that leans on it is raised with them first.

### Open

- Diplomacy: unknown.
- Trade: which goods each power needs, and how parts it buys get fitted.
- Asteroids (none in the game yet) and a radar display (missile seekers, the Fire Director and the Long Range Sensor part
  exist; no display does).
- Which resource each planet holds and how scarce it is; how a faction defends its planet (patrols, outposts, convoys to
  raid); what taking out a faction gives; how the rust bucket's hold seeds the first mining run.

### Ideas, for later (not rules)

- Acting at a distance: load into a robot shell aboard a nearby ship through the on-board antenna at high power, then
  command it.
- A fleet room (top-down command, as in a strategy game), power routing between systems, a subsystems list, rail
  launchers, faction politics, and late on, players shifting markets (cutting a route as a diplomatic move).

Dropped from the notes: the "everything exists for a reason" rule, hand-placed shipyards, a rule that only small things
are procedural, the vehicle terrain table, and urgent cargo.

## Built since

- **Breakdowns that warn you first** (the roadmap's first step, 2026-11-15): in Survival the rust bucket's thrusters break
  down now and then while someone is aboard. A part creaks and groans for a while first; the attach tool at the engine
  room's Engineer's Panel (or at the part) sees to it for nothing in that time, and once it has failed it takes a little
  metal to repair. `Source/Game/GameSandboxBreakdowns.cs`, numbers in `Constructs/Breakdowns.json`, and
  docs/sandbox/constructs/ship.md "Breakdowns".

## What was archived (2026-11-14)

Recoverable, not deleted:

- **The story layer.** `Game.StoryOn` (GameStory.cs) is false, so no story engine runs in play: no story people or their
  bodies, factions' standing and technologies, storylets (the colony career, the break-out, the ending), drama, jobs,
  errands, objectives, lore items, or the factions' settlements on other worlds. The code and the `Story/` data stay where
  they are. `--story` on the command line turns it back on for one run (the story's own tools, `--traffic-check` and
  `--outpost-check`, turn it on by themselves); setting `StoryOn` to true brings it back for good.
- **The main plot and the ending.** The five sealed data centres are not built (`FragmentSitesBuild`), and the ending's
  storylets never run. The storyline, the engine's guide and the plot's docs moved to `docs/archive/story`
  (STORYLINE.md, STORY.md, FRAGMENTS.md, BREAKOUT.md, OBJECTIVES.md, OUTPOSTS.md, PATHS.md).
- **The menus' story entries.** The STORY page and HOST STORY CO-OP are gone; so are the arena modes' LOADOUTS, the Zombies
  workbench preview and the arena level bar on the main menu (the code behind them stays). The placeholder Castaway start
  is in `docs/archive/story/starts`.

What stays live: the opening's card and wake (docs/story/OPENING.md). The penal colony, its buildings, quarry, depot,
spaceport, labour gangs and the warden's induction were removed (2026-11-17); a world from before starts loads with its
colony as plain constructs and plays no opening.

A world saved while the story was on keeps loading, and its story is kept: the Story section is read as bytes and written
back unread on every save (`GameStory.StoryRead`), so `--story` later finds it whole. A world saved with a start that is gone (the archived Castaway, the
penal colony) plays the On the ground start.

**Faction politics (decided, agreed by Karl 2026-11-17).** NPC factions are kinds; powers (the factions that act) are made from kinds, up to 16. Relations between powers and with players run from blood enemies who never make peace to wanting to merge, moved by events and fading over time, with pacts (ceasefire, non-aggression, alliance, trade, tribute, vassalage). A planet belongs to the power with majority control, key sites weighted by how rare they are there. Admirals coordinate groups of captains, combat or civilian. Design: `docs/sandbox/design/faction-controllers.md`.
