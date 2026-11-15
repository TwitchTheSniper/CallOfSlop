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
- **Tutorial** (Survival only, a tickbox): the Ministry's penal colony on Glacier and the warden's induction, in a world
  of its own that is never saved. Untick it to play your own world.

Multiplayer's HOST A GAME opens the lobby with the same choices; the lobby keeps Creative or Survival, the saved world and
the limits, and no longer offers the archived arena modes for a Sandbox game.

## Where Survival is going

Karl's notes from the talk, as direction for later work (none of this is built yet):

- **Logistics is the game.** Ships carry what the player needs; keeping those lines running, and defending them, is the
  play.
- **Gated resources.** The factions each hoard certain resources by holding the planets they are found on. What a player
  can make depends on what they can get to.
- **First goal: a mining and refining ship.** The player's first big build is a ship that digs and refines, so they can
  work a world they don't live on.
- **Then a combat ship.** Built to take on a faction, to win the new parts and resources it holds.

Open questions for when this is picked up: which resource each faction's planets hold and how scarce it is; how a faction
defends its planet (patrols, outposts, convoys to raid); what "taking out" a faction gives (its planet's resources, its
parts, both); and how the rust bucket's hold seeds the first mining run.

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

What stays live: the penal colony (its buildings, quarry, depot, spaceport and labour gangs) as the tutorial's world, the
opening's card and wake, and the warden's induction. Its staff are story people, so the colony has nobody walking it while
the story is archived; the induction's lines still come, signed by the warden.

A world saved while the story was on keeps loading, and its story is kept: the Story section is read as bytes and written
back unread on every save (`GameStory.StoryRead`), so `--story` later finds it whole. The tutorial's crew bus is a
stand-in craft and is still kept up and sent to friends who join without the story (`StandInsKeep`). A world saved with
a start that is gone (the archived Castaway) plays the On the ground start.
