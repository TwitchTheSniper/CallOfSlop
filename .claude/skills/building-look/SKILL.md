---
name: building-look
description: Make a building, outpost or other place in the story's worlds look right for its faction and its job. Use when authoring or changing a building blueprint or its generator (BuildingParts*.cs), dressing a site, or when a place is reported as bland, blocky or samey.
---

# Making a building look right

A building that passes the build check is valid, not finished. The style guides hold the taste; this is the order of work that gets a building from valid to finished and proves it in a picture.

## 1. Read, then write the job down

1. `docs/sandbox/constructs/look.md` ("Valid is not finished", "Rules of thumb", "A detail budget"), then `docs/sandbox/constructs/buildings.md`: "Every building, whoever built it" and the faction's own section (the Ministry of Slop's is Brutalism outside and the cold war inside).
2. If the faction has no section yet, write it first, to the same headings (material, accents, light, roof, windows, weight, the way in, signature, signs). No building before its style.
3. Write what the building is for as the comment over its blueprint in the generator: who comes in, who never does, what must be kept dry, dark, warm or watched. The job gives the plan and the outline. "A room with furniture in" is not a job.

## 2. Build it in the generator

- Buildings are construct blueprints from the part kit: `Source/PartKit/Generators/BuildingParts*.cs`. Change the generator, never the written blueprint. Use the faction's helpers (`Roof`, `Lid`, `Porch`, `Steps`, `Piers`, `Outside`, `Strip`, `Switch`, `Sign`, `Door` for the Ministry) and read "Building in the style" in buildings.md for what the build check has already taught.
- One bold move per building (an overhang, a tall hall, a stack, a dock), and no two outlines alike in one street.
- No coplanar faces: where two boxes meet flush, one moves 2 to 5 mm (`.claude/rules/z-fighting.md`).

## 3. Check it, in this order

```
dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts building_parts
dotnet build -c Release --no-restore                 # the game reads data from bin/
dotnet bin/Release/net10.0/CallOfSlop.dll --check-construct-parts   # then read callofslop.log for REFUSED
dotnet bin/Release/net10.0/CallOfSlop.dll --look-report Constructs/Blueprints/NAME.json
```

Then look at it, because none of those see it:

- A place on Glacier: `--window-shot OUT.png --map Glacier --survival --calm --do "skip; wait 120; places; view NAME | 60,25,135; wait 1500; shot far; view NAME | 14,1.7,60; wait 1500; shot eye"` (the spot syntax is the play-test tour's; `places` prints the names). Under xvfb in the cloud, on the real GPU on the PC.
- Take three pictures at least: from 50 m (does the outline read, does it differ from its neighbours), from a person's eye at the door (is the way in marked, lit and named), and inside (is it fitted for the people in it). Add one in the storm (without `--calm`) for a building on a cold world.
- If things are stood in it at run time (lore items, posts, wake spots), run `--traffic-check 5` and read the `[lore]` and `[story]` lines: the build check can't see those spots.

## 4. Have it judged by someone who didn't build it

Run the `look-critic` agent (`.claude/agents/look-critic.md`) with the pictures' paths, the building's name, its faction and its job. Builders grade their own work kindly; the critic reads the style guide and says what a player would see. Fix what it finds and shoot again. Two rounds is usual; stop when it finds nothing that names a rule.

## 5. Before it merges

- The frame rate at that spot has not fallen: the play-test tour's `[perf]` line for the spot before and after (the `playtest-tour` skill). Lamps and small detail parts are where it goes.
- What you learnt that the next builder would trip on goes into "Building in the style" in buildings.md.
- Pictures are for your own checking. Send one to the user only with a question that holds up the work (CLAUDE.md, "No review pictures unless asked").
