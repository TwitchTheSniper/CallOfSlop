# The part kit: construct part generators in C#

The construct part generators are C# (`Source/PartKit/`), run from the game itself (CLAUDE.md, "Helper scripts are C#"). They were
the Python scripts of `tools/constructs/`, ported in four phases (feat/partkit-1 to -4, 2026-11-11): each port was accepted only when
its output was byte-identical to the committed files, and the script was deleted in the same change. Every one is ported.

## Commands

Arguments to `dotnet bin/Release/net10.0/CallOfSlop.dll` (run from the repo root, or anywhere under it: the kit finds the folder
that holds `Constructs/Parts` and `tools/`). No window, no maps: a run takes well under a second.

| What | Command |
| --- | --- |
| Write one generator's files, or every one | `--gen-parts NAME` or `--gen-parts all`, `[--out DIR]` (another root to write under) |
| Compare with the committed files | `--gen-parts NAME --check`: prints each stale or out-of-date file and, for the first, the first line that differs; the exit code is the number of differing files |
| The writer's own check | `--gen-parts --roundtrip DIR`: every .json under DIR parsed and written back; prints those that don't come back byte for byte |
| zfight on a prop file | `--gen-parts --settle-props FILE`: the file's parts settled (`PartZFight.SettleProps`), printed |

Output also goes to `callofslop.log`, as everything the game prints does.

## Writing a generator

One class per old script in `Source/PartKit/Generators/`, implementing `IPartGenerator` (`Name` is the old script's name without
`.py`), added to `PartKit.All`. `Make(PartOutput o)` adds every file by its path from the repo root:

- `PartKitMath.WriteParts(o, "Constructs/Parts/Free", parts)` is `scale.write_parts`: it owns the folder (any other .json in it is
  stale and removed on a write) and writes each part as `json.dumps(d, indent=2)` plus a newline.
- `o.File(path, text)` adds any other file (a blueprint, a table); `o.ReadJson(path)` reads a committed input as Python's `json.load`.
- `Part`, `Box`, `BlockSockets`, `Pick`, `FileName`, `R`, `Vec`, `Fmt` and the size classes (`Small`, `Medium`, `Large`, `Capital`) are
  the old `scale.py`'s; `Part.Json()` runs `PartZFight.Resolve` as the Python did.

## Byte for byte

Python and C# disagree on numbers, so the port keeps the Python's types and operations, not just its values:

- **Ints and floats.** A JSON value is an `int`/`long` (Python int) or a `double` (Python float); `3` and `3.0` are written differently.
  C# literals type like Python's, except division: `/` on two ints is a float in Python, so make one side a double.
  `Part.Stats` takes armour and seconds as `object` so each keeps its type. Beware `cond ? 1.5 : 2L` (C# makes both double).
- **Floats** are written by `Py.Repr` (Python's repr: shortest digits, `1e-05`, `1e+16`, `2.0`), rounded by `Py.Round` (the exact
  binary value, half to even, as CPython; never `Math.Round(x, n)`), and formatted for names by `Py.G` (`f"{v:g}"`).
- **sum()** is `Py.Sum`: CPython 3.12+ adds floats with Neumaier compensation, so `a + b + c` can differ from `sum([a, b, c])`.
- **Key order** is insertion order: `PyDict` keeps a key's first place when it is set again, as `dict.update` does. Keyword
  arguments become `Set(("key", value), ...)` in the same order; a `**shape` becomes a `PyDict` in the order the call gave.
- **Strings** are escaped with `ensure_ascii` on (non-ASCII as `\uXXXX`, lower-case hex), as `json.dumps` does by default.
- **zfight** (`PartZFight`) is a line-for-line port of `zfight.py`; the order of float operations is the Python's, and sorts are stable.

Check a new port with `--gen-parts NAME --check` before deleting the script. If a script can't be made identical within reason,
it stays in Python and the reason goes in the list below.

## Adding a generator

1. A class in `Source/PartKit/Generators/` implementing `IPartGenerator` (`Name` is what `--gen-parts` takes), added to `PartKit.All`
   after the generators whose files it reads (`assembling`, `make_rv` and `building_parts`' blueprints read every part file).
2. Build parts on `PartScale.Part` (or `BuildingParts.BPart` for the building module's face-metre boxes), write them with
   `WriteParts` (it owns the folder) or `o.File`; another layout gets its own writer (below).
3. `--gen-parts NAME` writes, `--gen-parts NAME --check` compares; `--gen-parts all --check` is the whole kit (exit 0 when clean).
   Run `--check-construct-parts` after a change, and `--gen-vehicles` when parts the vehicle kit uses moved.

No new Python generator: a change to a part family goes in its C# generator.

## What there is

Generators (`--gen-parts` lists them): `free_parts`, `bandit_parts`, `media_parts`, `ammo_parts`, `highland_parts`, `shell_parts`,
`pilot_parts`, `callcentre_parts`, `office_parts`, `british_parts`, `door_parts`, `station_parts`, `shelf_parts`, `linear_parts`,
`scoop_parts`, `big_drive_parts`, `fluid_tank_parts`, `air_parts`, `boost_parts`, `suspension_parts`, `flight_parts`, `drone_parts`,
`air_cargo`, `air_weapons_parts`, `ordnance_parts`, `hull_parts`, `body_parts`, `wing_parts`, `actuator_parts`, `make_joint_sides`,
`control_parts`, `hydraulics_parts`, `press_parts`, `extrusion_parts`, `structure_parts`, `capital_parts`, `large_systems`,
`stabiliser_parts`, `leg_parts`, `upgrade_parts`, `engine_parts`, `building_parts` (the Building pieces and nine example blueprints),
`interior_parts`, `make_rv` (RV.json), `machine_tiers` (with `TierStyle.cs`), `assembling`. Helpers: `PartScale.cs` (the size classes,
`Part`, `Box`, `BlockSockets`, `WriteParts`), `PartZFight.cs` (zfight), `PartScreens.cs` (machine screens), `JointSides.cs`, `PyJson.cs`.
Shared pieces: `BigDriveParts` keeps the drive helpers public for the generators on it, `AirParts.Airship` is shared with `FlightParts`,
`ControlParts.Lever`/`Config` with `HydraulicsParts`, `Assembling` reads `UpgradeParts.Recipes` and `AirWeaponsParts.OrdnanceRacks`,
`BuildingParts` holds the Rot24 fitting mirror (`Blueprint`, `Apply`, `Turn`, `Upright`) and the wall and floor tables that
`InteriorParts` and `MakeRv` use.

Three layouts are not `json.dumps(indent=2)`, and have their own writers: `JointSides.Fmt` (a joint part's boxes one a line,
separators `", "` and `": "`), `Assembling.Layout` (one recipe a line; `ToolUpgrades.json` is written the same way by `UpgradeParts`)
and `MachineTiers.Layout` (one key a line, one box, socket, port and bin a line). `--gen-parts --roundtrip DIR` knows the last.
`actuator_parts` applies the joint-sides pass to its own files; `make_joint_sides` runs it over the hand-written joint parts. The pass
skips a part whose solid box has no `"size"` (the door hinges' min/max boxes, which draw their own sides).

Checks the generators make, as the Python did: `assembling` refuses a recipe whose output names no part (in `Constructs/Parts` or
the weapon builder's `Parts/*/`), a part input that isn't an item and more than six inputs; `building_parts` refuses a socket on no
solid face and a stage whose sockets differ from its whole wall's; `machine_tiers` a box thinner than 1 cm and more than 33 boxes;
`make_rv` a step that overlaps an earlier part. Not ported: machine_tiers' leftover z-fight report (the "No z-fighting" self-test covers
shipped parts) and make_rv's `--list`/`--zfight` printouts.

Some generators read committed files as inputs: `pilot_parts`, `air_weapons_parts` and `ordnance_parts` build on
`Constructs/Blueprints/LightPlane.json` (written by `wing_parts`), `air_weapons_parts` also on `GunnerTurret.json` and
`SmallHelicopter.json`, `suspension_parts` on `Buggy.json`, `big_drive_parts` on `Constructs/Parts/Differential.json` and
`Drivetrain/DiffWide.json`, `make_joint_sides` on the hand-written joint parts, `building_parts` on the hinges, doors and furniture
in other folders, and `assembling` and `make_rv` on every part file. Run them again after changing those.

Retired, not ported: `make_ports.py`, a one-off pass (board item np01) that added named link ports and a stub box per port to the
drivetrain, tank, battery and alternator part files that had none. On 2026-11-11 it changed nothing (every part it knew had its ports;
it stopped on the BigDrive axles and shafts, which `big_drive_parts` gives their own). New parts get their ports from their generator.

## Python left elsewhere

`tools/constructs/zfight.py` stays: the prop and map scripts of `tools/cluttered`, `tools/world`, `tools/space`, `tools/props`,
`tools/propviz`, `tools/buildings` and `tools/zombies` import it (its C# twin is `PartZFight.cs`; `--gen-parts --settle-props FILE` runs
it on a prop file). The other Python under `tools/` (space, planets, story, weapon-builder parts, props, audio, render, the map tooling)
is surveyed for the next phases; map tooling belongs to another developer and archived modes' scripts are not ported.
