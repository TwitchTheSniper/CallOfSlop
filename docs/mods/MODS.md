# Mods

Mods change or add to the game's data without touching the shipped files: star systems, Sandbox construct parts and
blueprints. This is the "surgical patching" of the space architecture (phase 6). Code: `Source/App/Mods.cs`; the loaders
that use it are `StarSystems.LoadFolder`, `ConstructParts.LoadFolder` and `ConstructBuild.LoadBlueprints`.

## A mod folder

Each mod is a folder in `Mods/` next to the game's data (the folder with `Maps/`, `Systems/` and `Constructs/`):

```
Mods/
  LongJumps/
    mod.json
    Systems/Ember.json              (a patch)
    Systems/Lantern.json            (a new star system)
    Constructs/Parts/JumpDrive.json (a patch)
    Constructs/Blueprints/...       (new or changed blueprints)
```

`mod.json`:

| Key | Meaning |
| --- | --- |
| `name` | Required, unique among the mods. Messages name the mod by it. |
| `description`, `version` | Free text, shown by `--check-mods`. |
| `order` | -1000 to 1000, default 0. Mods apply from low to high, then by folder name. A later mod works on what the earlier ones left. |
| `enabled` | `false` skips the mod. |

Any other key is refused, so a typo doesn't pass silently.

The files under the mod sit where the game's own would: `Systems/*.json` (one level), `Constructs/Parts/**/*.json`
(subfolders allowed; a new part's folder is its catalogue folder, as for shipped parts) and `Constructs/Blueprints/*.json`.
A mod's file name doesn't matter. What it targets is its `"name"`, or `"patch"`.

## What a file does

A mod file does one of three things:

- **Add.** It has a `"name"` that no shipped file and no earlier mod has. It is a whole file, exactly as a shipped one would
  be. New files load after the shipped ones, mod by mod. So a new star system takes the next galaxy index, and the shipped
  systems keep theirs.
- **Replace.** It has `"override": true` and the whole file, with the `"name"` of the thing it replaces. That thing keeps its
  place (its galaxy index or part id).
- **Patch.** It has `"patch": "Name"` and only what changes:
  - Objects merge key by key. `"jump": {"range": 20}` changes the range and keeps the charge and energy.
  - `null` removes a key.
  - A list whose entries are all objects with a `"name"` (a system's `bodies`, a part's `sockets`) merges by name:
    - an entry with a name the list has merges into that entry;
    - a new name goes on the end;
    - `{"name": "Soot", "remove": true}` takes that entry out.
  - Any other value, including any other list, replaces the old one.
  - A patch can't change the `"name"`. To make a renamed copy, add a new file instead.

A new file whose name is already taken is refused unless it says `"override": true`. This means two mods can't replace each
other's work by accident.

## Checks

Each change goes through the data's own loader as it is made, so a modded file meets every check a shipped one does: a
part's ranges, a system's orbits, the galaxy's ten-system limit and spacing, a blueprint that must build. A refused change
is dropped with the mod's name and the reason, and what was there before stays: the shipped file, or an earlier mod's
version. A bad patch never takes a shipped part or system away. Refusals are printed with `[mods] refused:` in
`callofslop.log`.

Star systems are hashed after mods (`StarSystems.Entry.Hash`, `Galaxy.Hash`). So a host and a client with different system
mods see that their galaxies differ, just as with different system files.

Construct parts are not checked between peers yet. Like the shipped data, every player in a game needs the same part mods,
because part ids go over the wire.

**Pressed and sized parts.** A mod blueprint may name a pressed part by its key, as a shipped one does (`Pressed Part
(cylinder.cylinder pipe15 60x60x105 steel)`, `Pressed Part (barrel.plain round 10x10x70 steel)`): the key is the part, made on
each peer from `Constructs/Pressing.json`, which mods can't change. A modded part keeps its catalogue place but has no press
pattern or preset, so a press never makes it; since sized parts phase 6 (2026-11-13) the shipped barrels, engine pieces and
generated receivers are made on a Press, not an Assembler, so a patch to one of those part files changes what the press's preset
gives (its look, mass and numbers) while its blank stays the same. A patch that makes a preset part heavier than its blank and
bay 2 is not refused: the recycler gives back no more than pressing it took, and `--press-part presets` lists it as OVER.

## Checking a mod

`CallOfSlop --check-mods [DIR]` loads the mods in DIR (default: the game's `Mods/` folder) over the shipped data. It prints
to `callofslop.log`:

- each mod;
- each file a mod changed or added;
- the star systems with their hashes;
- the part and blueprint counts;
- every refusal.

The exit code is the number of problems.

## The example

`docs/mods/example/LongJumps` is a small working mod. It does three things:

- It patches the Jump Drive to 20 light-years and 12 s of charge.
- It patches Ember: a new world, Kiln, and a more eccentric Ashfall.
- It adds a new star system, Lantern, 14 light-years out. The stock drive can't reach it, but the modded one can.

To try it, copy the folder into `Mods/` next to the game. The "Mods" self-test uses it together with mods it makes itself:
a second patch on the same part, a list entry removed, a blueprint added, and each kind of refusal.
