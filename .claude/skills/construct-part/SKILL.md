---
name: construct-part
description: Add or change a construct part in Sandbox - a part file under Constructs/Parts, usually through its generator in Source/PartKit. Use when adding a part, a size or tier of one, changing a part's boxes, sockets, stats, ports or recipe, or when a part is refused, lands in the wrong inventory category or looks wrong.
---

# Adding or changing a construct part

A part is one JSON file in `Constructs/Parts/`, loaded at start-up. Most are written by a generator, so the first question is always which one. Read `docs/sandbox/constructs/parts-and-data.md` "Part files" for the fields and `docs/sandbox/PARTKIT.md` for the kit; `docs/sandbox/CONSTRUCTS.md` is the index to the file for the system the part belongs to (drive, power, production, weapons).

## 1. Find who writes it

```
git grep -n "PART NAME" -- Source/PartKit Constructs/Parts
```

- **A generator names it** (`Source/PartKit/Generators/*.cs`): change the generator, never the file. A new part of an existing family is a line in that family's generator. A new family is a new `IPartGenerator` added to `PartKit.All` (PARTKIT.md "Adding a generator").
- **Nobody does**: it is hand-written. Edit the file, and keep it out of a generator's folder: `WriteParts` owns its folder and removes any other `.json` there on the next write.
- New helper code is C# in the game (`--gen-...`), never a new Python file.

## 2. Make the part

- **Sockets** sit on whole centimetres (a part's place rounds to the centimetre); the size class (`Small`, `Medium`, `Large`, `Capital`) decides what a socket takes, and a part of another class needs an adapter. A shaped box (a cylinder) is `detail`: give the part a plain solid box for sockets and collision as well.
- **No two faces in one plane** that both draw: offset by 2 to 5 mm (`.claude/rules/z-fighting.md`); a generated part goes through `PartZFight.Resolve` by itself.
- **Looks**: read `docs/sandbox/constructs/look.md` first ("Valid is not finished", "Rules of thumb", "A detail budget", and "Machine look" for a machine). `--look-report` gives the numbers.
- **What kind of part it is** (`role`, `tool`, physics-only or an item, cargo volume, ports) decides how every other system treats it: check each against the table in parts-and-data.md, not against a part that looks alike.
- **A recipe** comes from the `assembling` generator, which refuses an output that names no part, an input that isn't an item and more than six inputs; one amount over `MaxUnits` makes the whole table refuse.
- **Where it shows in the inventory** is decided by `Constructs/Categories.json`: rules run top to bottom and the first match places it (group, section, role, logic kind, words in the name). Add a rule only when no existing one fits.
- **A part a faction teaches** is named in a technology's `"parts"` under `"teaches"` in that faction's file (`Story/Factions/*.json`); in a story game the build menu then shows it locked until the player has learnt it.

## 3. Write and check

```
dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts NAME            # write the generator's files
dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts all --check     # exit 0: nothing else went stale
dotnet bin/Release/net10.0/CallOfSlop.dll --check-part FILE           # one hand-written file
dotnet bin/Release/net10.0/CallOfSlop.dll --check-construct-parts     # every part and blueprint; prints nothing
```

Then read `callofslop.log` next to the DLL for `REFUSED`: a bad part is refused with its reason and the rest still load, so the console tells you nothing. Generators that read every part file (`assembling`, `make_rv`, `building_parts`) run again after yours; `--gen-parts all` does it in order. If a part the vehicle kit uses moved, `--gen-vehicles` shows which committed vehicles changed: say so in your report, since they are regenerated once, after the last merge (CLAUDE.md, "Token budget").

## 4. What a new part breaks elsewhere

- **Ids follow the sorted file names**, so a new file shifts the ids after it. Nothing saved or sent may hold a part id: blueprints, maps and saves name parts, and the wire sends a key (`.claude/rules/constructs.md`, `network.md`). A piece made at run time (an Extruder's, a Press's) has no shared id at all.
- **A new kind of thing** (a role, a setting, a flag, a panel, a packet) is a code change: grep every reader of the flag you set, and a packet change bumps `ProtocolVersion` (the guard refuses the commit otherwise).
- **The neighbours' sections.** Run by name the ones that count parts of this kind: `--selftest-only "Sandbox inventory, No z-fighting"`, plus "Machine tier looks" for a machine, "Assembling" for a recipe, "Interaction sweep" for anything F opens. Read `selftest.log`.

## 5. Try it as a player

Place it from the menu, fit it, use it, take it off: `--window-shot OUT.png --blueprint NAME --do "..."` under xvfb, on a joined client too if it holds state (`--net join:IP`). Look at the picture yourself for faces that flicker, a part sunk or floating on its socket, and a colour that reads as missing. For anything about looks, hand the picture to the `look-critic` agent.
