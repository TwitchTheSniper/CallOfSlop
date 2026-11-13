# Lessons

What went wrong before, why, and the rule that prevents it. Since 2026-11-13 the lessons and the gotchas live with the code they are about, in `.claude/rules/`: Claude Code loads a file by itself when a session reads a file under the paths it names, so the right lessons are in front of an agent without anyone choosing to read them. The few that keep coming back whatever the area are in CLAUDE.md, "Lessons and gotchas", which every session loads.

| File in `.claude/rules/` | Loads with |
| --- | --- |
| `constructs.md` | `Source/Constructs/`, `Source/Game/GameSandbox*.cs`: parts, welds, blueprints and bodies, run-time parts |
| `production.md` | the machines, power, logic, links and routes, `Constructs/*.json` |
| `drive-and-vehicles.md` | wheels, tracks, joints, walkers, `Source/VehicleKit/`, `Source/EngineKit/` (the old "Vehicles, wheels and joints") |
| `generated-geometry.md` | `Source/PartKit/`, hull lofts, interiors, buildings: generators, not outputs (the old "Generated geometry") |
| `z-fighting.md` | props, buildings, models, parts and their generators: no coplanar faces |
| `network.md` | `Source/Net/`, `ConstructNet`, `Game.cs`: the protocol, budgets, smoothing, run-time ids on the wire |
| `physics-and-riding.md` | `Source/Physics/`, colliders, rides, flight |
| `performance.md` | all game code: the 8 ms tick, allocations, work bounded per body |
| `terrain.md` | dug and streamed ground, planetoids, drills and scoops |
| `maps-and-worlds.md` | `Source/Maps/`, `Maps/`, `World`, travel and space: baked maps, a world per Game |
| `render.md` | `Source/Render/`, `Materials/`: shared materials, the bloom's alpha, OpenGL 4.6 / 4.1, baked lighting |
| `ui-and-input.md` | `Source/UI/`, `Program*.cs`: ImGui, frame state, tools, keys, panels that hold the mouse |
| `saves.md` | `Source/Worlds/`, `SavePaths.cs`, `tools/package.ps1`, `tools/MIGRATING.md` |
| `story-and-colony.md` | `Source/Story/`, `Story/`, the colony, the tutorial, stand-ins |
| `props-and-weapon-parts.md` | `Props/`, `Parts/`, the weapon builder, the crowd, the Browser helper |
| `tests-and-ci.md` | `Source/App/SelfTest*.cs`, the workflows (the old "When a test is asked for" and "Checks and CI") |

**Adding one.** When you fix something that was marked done, or a check fails for a reason seen before, add a line at the end of the file that covers the code: date, what went wrong, why, the rule. A lesson about two areas goes in both. One that fits no file gets a new file with a `paths:` list at its top (copy a neighbour's). The coordinator folds repeats together now and then, and moves a lesson that keeps coming back into CLAUDE.md's short list.

**History.** The lessons were folded by theme on 2026-11-03 and 2026-11-09 and sorted into the files above on 2026-11-13. The dated stories behind the folded ones are in `git show 4a567bec:CLAUDE.md` and `git show c6b159b26:CLAUDE.md`; the single list as it stood before the sort is `git show 6b80c5c6:docs/LESSONS.md`.
