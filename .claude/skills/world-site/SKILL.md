---
name: world-site
description: Add a place to one of the story's worlds - an outpost, a faction's settlement, a site by the penal colony, a spaceport or a data centre's ground. Use when asked to put buildings somewhere on a planetoid, to add an outpost or a settlement, or when a placed building stands in the air, sinks, lands on another site or leaves people with nowhere to stand.
---

# Adding a site to a world

A site is data: a place on a planetoid, a list of blueprints laid there, and sometimes people. The host builds it once, the first time a story game plays that world, and finds it again on a saved world by where it stands. Pick the kind first: each has its own file and its own check.

| Kind | Data | Code and guide | Check |
| --- | --- | --- | --- |
| A faction's outpost (a few buildings, people at posts and on patrol) | `Story/Outposts/WORLD.json` | docs/story/OUTPOSTS.md "Adding outposts" | `--outpost-check` |
| A faction's settlement at its home (up to 12 buildings, nobody living there yet) | `"settlement"` in `Story/Factions/*.json` | docs/planets/WORLDS.md "Settlements", `GameStorySettlements.cs` | a window shot on that world; a faction file that is wrong is refused with its reason in `callofslop.log` |
| A piece of the penal colony's site (roads, mains, fence, yard) | `Story/Opening/ColonyBuildings.json` (`"bench"`, rows with `"site"` or `"elsewhere"`) | docs/sandbox/constructs/buildings.md "The site", `GameColonySite.cs` | `--traffic-check 5` (logs each `[site]` footprint) |
| A big place of its own near the colony (the spaceport) | a file of its own in `Story/Opening/` | docs/story/SPACEPORT.md "Where it stands", `GameSpaceport.cs` | a window shot, the tour |
| The ground itself (a flat place, a new world) | `tools/planets/make_worlds.py` writes `Maps/Planetoids/` | docs/planets/WORLDS.md "Places kept flat", PLANETS.md "Making a world" | `--check-planetoid FILE --at LAT,LON` |

Prefer the first four: they add data. Before changing the ground, see who is working on it (`git log` on `tools/planets` and `Maps/Planetoids`; CLAUDE.md, "Who owns what"), and fix a world in its script, never in the file the script wrote.

## The order of work

1. **The buildings first.** Follow the `building-look` skill: the faction's style in buildings.md, then blueprints in the building generator (`--gen-parts building_parts`), then `--check-construct-parts` and `REFUSED` in `callofslop.log`. A site of bare boxes is the commonest finding.
2. **Choose the ground, don't assume it.** Latitude 90 is the +Y pole and longitude 0 faces +Z. `--check-planetoid FILE --at LAT,LON` prints the height there; `--line` a row of heights. Outposts and the spaceport search for level ground themselves (a `"flat"` radius, or the most level spot along a bearing), so give them room to move; a settlement's buildings and a colony site row are laid where you say.
3. **Keep clear of what already stands.** Sites a few hundred metres apart; clear of the colony's bench and its quarry; 8 m or more from a home's survey post; settlement buildings within 60 m of their place. A piece laid round the colony is checked against the spots the colony's systems stand on (the gate, the story's middle, the posts): on 2026-11-13 a covered way over the gate's spot left every story person without a body, quietly.
4. **Long thin pieces** (fence, road, mains) get a fixed spot: the ring search clears by a body's radius and pushes whatever is placed after them far away.
5. **Old worlds.** Say what a world saved before your site gets: outposts and the spaceport are added the next time it is loaded; the colony's site is graded only on a fresh world. Keep a mark (a story Thing, a name in the colony's list) so a site is built once and a building pulled down stays down.
6. **People** come last: posts, patrols and lines in the site's file, bodies only near a player, within the caps (`Game.OutpostBodiesMax`). Their words follow the `story-writing` skill.

## Check it

- Run the kind's check from the table and read its log lines: every site placed, built, woken and asleep again, nothing `REFUSED`, nothing given up without a line.
- Look at it: `--window-shot OUT.png --map Glacier --survival --calm --do "skip; wait 120; view outpost 2 | 60,20,135; wait 300; shot site"` under xvfb (the `view` targets are a colony building's blueprint name, `outpost N`, `geo LAT,LON`, `X,Y,Z`; the `places` action prints the names). Look for buildings in the air or sunk, a ramp to nowhere, bare ground between it and the next place. Then with the storm, which is how a player meets it.
- Give the pictures to the `look-critic` agent with the faction and what the place is for.
- **Cost.** A site adds constructs and lamps to a frame. Add a spot for it to the play-test tour (`tools/playtest/tour.ps1`) and, on the Windows PC, compare `--bench` before and after (the `slow-frame` skill): frame rate comes first in visual work.
- No wire or save format change is needed for a site of the kinds above (bodies are players, buildings are constructs). If yours needs one, that is a different job: stop and say so.
