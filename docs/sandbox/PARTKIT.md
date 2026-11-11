# The part kit: construct part generators in C#

The Python generators in `tools/constructs/` are being ported to C# (`Source/PartKit/`), so every Sandbox helper runs from the game
itself (CLAUDE.md, "Helper scripts are C#"). A port is accepted only when its output is byte-identical to the committed files; the
Python script is deleted in the same change, and every pointer to it is fixed.

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
  `scale.py`'s; `Part.Json()` runs `PartZFight.Resolve` as the Python did.

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

## Ported and remaining

Ported (Source/PartKit/Generators): `free_parts`, `bandit_parts`, `media_parts`, `ammo_parts`, `highland_parts`, `shell_parts`;
the helpers `scale.py` (PartScale.cs) and `zfight.py` (PartZFight.cs; `zfight.py` itself stays while Python scripts import it).

Remaining in `tools/constructs/` (45 files): `actuator_parts`, `air_cargo`, `air_parts`, `air_weapons_parts`, `assembling`,
`big_drive_parts`, `body_parts`, `boost_parts`, `british_parts`, `building_parts`, `callcentre_parts`, `capital_parts`,
`control_parts`, `door_parts`, `drone_parts`, `engine_parts`, `extrusion_parts`, `flight_parts`, `fluid_tank_parts`, `hull_parts`,
`hydraulics_parts`, `interior_parts`, `large_systems`, `leg_parts`, `linear_parts`, `machine_tiers`, `make_joint_sides`,
`make_ports`, `make_rv`, `office_parts`, `ordnance_parts`, `pilot_parts`, `press_parts`, `scale`, `scoop_parts`, `screens`,
`shelf_parts`, `stabiliser_parts`, `station_parts`, `structure_parts`, `suspension_parts`, `tier_style`, `upgrade_parts`,
`wing_parts`, `zfight`. Scripts that import another generator (`air_parts` imports `big_drive_parts`) are ported after it.
