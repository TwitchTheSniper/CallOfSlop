# Paths round the colony

Karl (2026-11-14): people in the penal colony walked into the buildings, the wheelbarrow gangs ran through walls, and
the zombie mode had good pathing, so use it. They now use the same pathing as every bot in the game: the nav graph, A*,
path following with shortcuts, and the stuck watch (`Source/Game/BotNav*.cs`, `BotMove.cs`, `BotStuck.cs`).

## Why there was none

A planetoid is kilometres of ground all round, too much for a nav graph, so `NavGraph` built none on one and every bot
steered straight at its goal (`SandboxBotAvoid` only slid it along the construct in front). And the colony's buildings
are constructs, which the map's collision world (what a nav graph is built from) doesn't hold.

## How it works

- **Sites.** The host names the places people live and work (`Source/Game/GameNavSites.cs`): the penal colony (a
  rectangle in the colony construct's frame round every anchored construct within 110 m, and 8 m more) and the quarry
  (round the works, 16 m more). It asks for a graph over them with `NavGraph.RequestSites`, 4 s after the colony stands,
  and again 12 s after what stands there last changed (a building added, taken down or rebuilt). The log says
  `[paths] colony 131 by 97 m, quarry 59 by 59 m: the bots' graph is built again`.
- **The build** (`Source/Game/NavSites.cs`) copies the map's collision world as any build does, then adds the solid boxes
  of every anchored construct on the sites (walls, floors, stairs, furniture). Event doors are left out and listed: they
  are ways through. What hangs on a hinge (doors, gates) is listed too and goes in as it stands open (swung as far as F
  swings it, or to where it first meets something solid), so a path keeps clear of the open leaf rather than hugging
  the jamb it opens against. Nodes sit on a 1.5 m grid in the site's frame, every
  surface under each point (roof, upper floors, ground floor); under a roof the cell's quarters are tried too (0.75 m
  apart, for rooms and doorways), and a point on a wall or under a table is moved round its cell until a body fits.
  Links join neighbours a player could walk between both ways (`World.CanWalkCurved`). It runs a slice per tick like
  any graph; on the cloud box about 2 s of work, slices of 5-15 ms (the last two, joining up the graph, are the long
  ones). It is never baked: the constructs are the world's, not the map's.
- **Bots** then path as anywhere. On a site graph: a shut door is opened as F opens it when the next nodes of the path
  pass it (from 3.5 m, so the leaf has swung before the bot is in its way) or it is within 2.2 m ahead, and shut 5 s
  later once nobody stands in it; bots don't shove door leaves as players do (they rubbed an open door shut and were
  slapped by it swinging back); a door seen standing open well short of where the graph has it, two looks running, is
  learnt (`NavGraph.LeafStands`) and the graph built again: `[paths] a door of Penal Colony stands open at 69 degrees,
  not 95`; a goal in a room the graph doesn't reach is walked as near as it goes; `NavGraph.Clear` (shortcuts, the nearest node in sight) also asks the constructs
  (`ConstructsClear`, three rays a body wide), so nobody cuts a corner through a wall; a bot leaving a site makes for its
  edge towards the goal and walks straight from there; one off the sites steers straight as before, roams where it is,
  and the stuck watch leaves it alone (a planetoid's height isn't its y); one in a corner the graph has no way out of
  makes for the nearest spot that has one.
- **The convict gangs** (LABOUR.md) are pictures, not bots: once the graph is built the host routes each walking gang's
  way on it (a leg's path pulled tight, its ends moved out of anything solid), at most `LabourScene.MaxPoints` points,
  and sends the gangs again. `[paths] The rock pile: its way routed round the buildings, 2 points to 5`; one it can't
  route is kept as written and the log says why.

## What the log tells whoever lays out the rooms

Once per graph, for each colony building some of whose floor can't be walked to from outside:
`[paths] Colony Infirmary: 7 of 33 spots on the ground floor and 0 of 0 above it (roofs too) can be walked to from
outside`. A spot is cut off by furniture, a doorway too tight, a step over 0.55 m (`World.StepHeight`) or a stair a
body can't climb standing. On 2026-11-14 no upper floor could be reached (the stairs), and most of the infirmary was
behind something 0.6 m tall across its hall.

## How well it walks

`--traffic-check 290` with the bodies sent between ten spots round the colony every 40 s (a throwaway debug hook, not
kept): 10 of 35 walks arrived in time walking straight, 27 of 35 on paths. What's left: the canteen's far end (behind
its furniture), a long walk still under way, and two bodies that stopped to face the test's players.

## Not done

- Sites elsewhere: the spaceport, the outposts, faction settlements (a site is a rectangle and a name; add them in
  `NavSitesNow`).
- Story people still use the barracks' doorway route and put-in (GameStoryPosts.cs), now with the graph under them.
- Mantles and ladders: a site graph has neither (`CanWalkCurved` only).
- The graph is built from the colony as it stands; a door left open by a player, or a wall knocked down since, is only
  seen at the next rebuild (bots slide round what's new with `SandboxBotAvoid`).
