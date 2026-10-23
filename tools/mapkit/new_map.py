#!/usr/bin/env python3
"""Scaffolds a new map: the generator, its doc stub and its decal folder, and says what else the branch needs.

    python3 -I tools/mapkit/new_map.py <Pack> <NN> "<Name>" [--theme "a few words"] [--python] [--force]

For example `python3 -I tools/mapkit/new_map.py Space 13 "Orbital Foundry" --theme "zero-g steelworks, slag and sparks"`
writes:

    Source/MapKit/Generators/OrbitalFoundry.cs   the generator (an IMapGenerator for the C# map kit, Source/MapKit),
                                                 from the template below; --python writes tools/<pack>/orbital_foundry.py
                                                 instead, a self-contained Python generator with no kit at all
    Maps/Space/13-OrbitalFoundry.md              the map's doc, with the standard sections to fill in
    Maps/Space/Decals/OrbitalFoundry/            where the map's own decals go (a README.md keeps the folder in git)

and prints: the registry line to add to the kit's generator registry, the `order` the pack's convention gives the map
(Space: 600 + NN), the reminder to fill the map's test block / self-test section, the `[tests: ...]` line the branch's
commits should carry, and how to produce the JSON (`CallOfSlop --gen-map "<Name>"`, or the Python script).

The C# template compiles against the kit (Source/MapKit: IMapGenerator with Name, Pack, Number and Build(Kit kit);
docs/mapkit/MAPKIT.md); if the kit's API changes, fix the one template below, not every generator. Existing files are never overwritten without --force.
"""
from __future__ import annotations

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# The all-maps self-test sections a map branch always runs (see Maps/README.md, "Before you push").
ALWAYS_SECTIONS = ["Map horizons", "Map objectives", "Props", "Animated props", "Pack assets", "Maps", "Map files",
                   "Nav determinism", "Stairs"]


def slug_of(name: str) -> str:
    """"Orbital Foundry" -> "OrbitalFoundry" (the file-name style of Maps/<Pack>/NN-<Slug>.json)."""
    words = re.findall(r"[A-Za-z0-9]+", name)
    if not words:
        sys.exit("new_map.py: the name needs letters or digits")
    return "".join(w[:1].upper() + w[1:] for w in words)


def snake_of(name: str) -> str:
    return "_".join(w.lower() for w in re.findall(r"[A-Za-z0-9]+", name))


def pack_order(pack_dir: str, number: int) -> int:
    """The `order` the pack's convention gives map NN: existing numbered maps show the base (Space 600 + NN, World 500 +
    NN, Cluttered 400 + NN, Legacy NN); a pack with none yet uses 100 x its pack.json order."""
    bases = []
    for fn in sorted(os.listdir(pack_dir)):
        m = re.match(r"^(\d+)-.*\.json$", fn)
        if not m:
            continue
        try:
            with open(os.path.join(pack_dir, fn), encoding="utf-8") as f:
                order = json.load(f).get("order")
        except (OSError, ValueError):
            continue
        if isinstance(order, int):
            bases.append(order - int(m.group(1)))
    if bases:
        return max(set(bases), key=bases.count) + number
    try:
        with open(os.path.join(pack_dir, "pack.json"), encoding="utf-8") as f:
            return int(json.load(f).get("order", 9)) * 100 + number
    except (OSError, ValueError):
        return 900 + number


def pack_sections(pack: str, slug: str, name: str) -> list[str]:
    """Self-test sections that mention the pack or this map (by name or slug) in Source/App/SelfTest.cs."""
    path = os.path.join(ROOT, "Source", "App", "SelfTest.cs")
    found = []
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                m = re.search(r'Section\("([^"]+)"', line)
                if not m:
                    continue
                hay = line.lower()
                if pack.lower() in hay or slug.lower() in hay or name.lower() in hay:
                    found.append(m.group(1))
    except OSError:
        pass
    return found


CS_TEMPLATE = '''namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// {pack} {nn} {name}: {theme}
/// Generated map: `CallOfSlop --gen-map "{name}"` writes Maps/{pack}/{nn}-{slug}.json, loads it back and prints the
/// MapChecks report (run it after every change and commit the JSON; the "Map kit" self-test checks the committed file
/// matches). Design notes: Maps/{pack}/{nn}-{slug}.md. The kit: docs/mapkit/MAPKIT.md.
/// </summary>
public sealed class {slug} : IMapGenerator
{{
    public string Name => "{name}";
    public string Pack => "{pack}";
    public int Number => {number};

    public void Build(Kit kit)
    {{
        // 1. Header: description, order ({order} is the pack's convention: see new_map.py), players, size, sky.
        kit.Describe("{theme}", order: {order}, maxPlayers: 12);
        kit.Size(40, 30); // a rectangle; a shaped map calls kit.Boundary(Geometry.Ellipse(...) or FromHalf(...), "smooth", Kit.Edge("wall", height: 6))
        kit.Skybox("day");

        // 2. The floor and the edge.
        kit.Ground("#4d4f54");
        kit.Perimeter(4, "#737880");

        // 3. Layout: lanes, cover, levels. Build Blue's half inside Symmetric(); the kit adds Red's twin of every op,
        //    spawn, hill and flag (Symmetry.Turn180, MirrorX or MirrorZ).
        kit.Symmetric(Symmetry.Turn180, () =>
        {{
            // kit.Block(-20, 0, -6, -14, 2.2, -4, "#80828a");
            // Structures.Stairs(kit, -14, -5, 0, 2.2, 1, 0, width: 1.6);
            // 4. Objectives: hills along the symmetry axis (a hill at the centre stays single), the flag and spawns behind cover.
            // kit.Hill(-20, 0, 5);
            // kit.Flag(-32, 0);
            // kit.Spawn(0, -34, -8);
            // 5. Dressing: props (Props/, and this pack's own), decals (Maps/{pack}/Decals/{slug}/), clusters.
            // kit.Prop("Wooden Crate", -10, 4, yaw: 90);
            // kit.Decal("{slug_lower}_emblem", -30, 0, size: 3);
            // Clusters.CargoStack(kit, -24, 6, 0);
        }});
        kit.Hill(0, 0, 5);

        // 6. Nav last: waypoints see everything built before them.
        kit.AutoWaypoints(6);
    }}
}}
'''

PY_TEMPLATE = '''#!/usr/bin/env python3
"""{pack} {nn} {name}: {theme}

    python3 -I tools/{pack_lower}/{snake}.py            # writes Maps/{pack}/{nn}-{slug}.json
    python3 -I tools/{pack_lower}/{snake}.py --check    # exit 1 if the committed file differs

Self-contained (no kit): the ops are written as plain dicts in the order the game runs them (Maps/README.md).
Design notes: Maps/{pack}/{nn}-{slug}.md. Check the result with `CallOfSlop --check-map Maps/{pack}/{nn}-{slug}.json`
and `python3 -I tools/mapkit/check_branch.py` before pushing.
"""
from __future__ import annotations

import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Maps", "{pack}", "{nn}-{slug}.json")

HALF_X, HALF_Z = 40, 30


def r(v):
    v = round(v + 0.0, 2)
    return int(v) if float(v).is_integer() else v


def block(ops, x0, y0, z0, x1, y1, z1, color):
    ops.append({{"op": "Block", "x0": r(x0), "y0": r(y0), "z0": r(z0), "x1": r(x1), "y1": r(y1), "z1": r(z1), "color": color}})


def turned(ops):
    """The 180-degree twin of every Block, Prop and Spawn so far (x, z -> -x, -z): build one half, call this once."""
    for op in list(ops):
        kind = op.get("op")
        if kind == "Block":
            ops.append(dict(op, x0=r(-op["x1"]), x1=r(-op["x0"]), z0=r(-op["z1"]), z1=r(-op["z0"])))
        elif kind in ("Prop", "Spawn"):
            t = dict(op, x=r(-op["x"]), z=r(-op["z"]))
            if kind == "Prop":
                t["yaw"] = r((op.get("yaw", 0) + 180) % 360)
            ops.append(t)


def build():
    ops = [
        {{"op": "Ground", "color": [0.3, 0.31, 0.33]}},
        {{"op": "Perimeter", "height": 4, "color": [0.45, 0.47, 0.5]}},
    ]
    # Layout: one half of the map, then turned(ops) for the other half.
    # block(ops, -20, 0, -6, -14, 2.2, -4, [0.5, 0.5, 0.52])
    # ops.append({{"op": "Prop", "name": "Wooden Crate", "x": -10, "z": 4, "yaw": 90}})
    # ops.append({{"op": "Spawn", "x": -34, "z": -8}})
    turned(ops)
    ops.append({{"op": "RingSpawns", "count": 12, "frac": 0.8}})  # replace with Spawn ops (and teamSpawns) once the layout exists
    ops.append({{"op": "AutoWaypoints", "spacing": 6}})
    return {{
        "format": "call-of-slop-map/1",
        "name": "{name}",
        "description": "{theme}",
        "author": "Call of Slop",
        "order": {order},
        "halfX": HALF_X,
        "halfZ": HALF_Z,
        "sky": [0.56, 0.62, 0.7],
        # "hills": [...], "flags": {{...}}, "teamSpawns": {{...}}: see Maps/README.md
        "ops": ops,
    }}


def render(m):
    head = {{k: v for k, v in m.items() if k != "ops"}}
    text = json.dumps(head, indent=2)[:-2].rstrip() + ",\\n  \\"ops\\": [\\n"
    text += ",\\n".join("    " + json.dumps(op, separators=(", ", ": ")) for op in m["ops"])
    return text + "\\n  ]\\n}}\\n"


def main(argv):
    text = render(build())
    try:
        with open(OUT, encoding="utf-8", newline="") as f:
            current = f.read()
    except FileNotFoundError:
        current = None
    if "--check" in argv:
        print("up to date" if current == text else f"{{os.path.relpath(OUT, ROOT)}} differs: run this script")
        return 0 if current == text else 1
    with open(OUT, "w", encoding="utf-8", newline="\\n") as f:
        f.write(text)
    print(f"wrote {{os.path.relpath(OUT, ROOT)}} ({{len(build()['ops'])}} ops)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
'''

DOC_TEMPLATE = '''# {nn} {name} ({pack} pack)

{theme_sentence} One paragraph: what the place is, who built it (invented names only), what the match feels like.
Size class (about W x D m, area), players, modes. Generated by {generator} (`{gen_cmd}`). Everything is original.

## Theme

What it looks like: materials, colours, light, the sky. The three or four things a player should remember.

## Shape

The outline (rectangle, bean, hexagon...), its symmetry (180-degree turn or a mirror and about which axis), levels and
their heights, what is beyond the edge (walls, a void with `fallKill`, fog).

## Layout

| Area | Where | What's there |
|---|---|---|
| | | |

## Lanes

The main routes between the bases (left, middle, right), where they cross, the short cuts and the sight lines each
one gives and denies. How long the walk is from each base to the middle.

## Fairness

How the two halves match (built as one half and turned/mirrored), the walks to each hill and flag from each team
(from the self-test: "hill 1 NN/NN m"), and anything deliberately asymmetric and why it is still fair.

## Counts (cost)

Boxes, meshes, props, decals, spawns, waypoints, nav nodes and build time, buildings, terrain, movers (from the
self-test log). Keep the nav build well under a second and a server tick under 8 ms.

## What to check in game

Three or four walks and sight lines to try, and what should look or feel right on each.
'''

DECAL_README = '''# Decals for {name} ({pack} pack)

Decals only this map (and the rest of the {pack} pack) can use: one JSON per decal with its PNG next to it, in the format of
the global `Decals/` folder (see Decals/README.md). Use `"category": "{slug}"`. A decal every pack could use belongs in
the global `Decals/` folder instead.
'''


def write(path: str, text: str, force: bool) -> bool:
    if os.path.exists(path) and not force:
        print(f"  kept    {os.path.relpath(path, ROOT)} (exists; --force overwrites)")
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(f"  wrote   {os.path.relpath(path, ROOT)}")
    return True


def main(argv: list[str]) -> int:
    args = [a for a in argv if not a.startswith("--")]
    if len(args) < 3:
        print(__doc__)
        return 2
    pack, nn_text, name = args[0], args[1], args[2]
    theme = ""
    if "--theme" in argv:
        i = argv.index("--theme")
        theme = argv[i + 1] if i + 1 < len(argv) else ""
        if theme in args:
            args.remove(theme)
    force = "--force" in argv
    use_python = "--python" in argv
    if not nn_text.isdigit():
        sys.exit("new_map.py: NN is the map's number in the pack, two digits (13)")
    number = int(nn_text)
    nn = f"{number:02d}"
    pack_dir = os.path.join(ROOT, "Maps", pack)
    if not os.path.isdir(pack_dir):
        sys.exit(f"new_map.py: no pack folder Maps/{pack} (packs: {', '.join(sorted(d for d in os.listdir(os.path.join(ROOT, 'Maps')) if os.path.isdir(os.path.join(ROOT, 'Maps', d))))})")
    slug = slug_of(name)
    for fn in os.listdir(pack_dir):
        if fn.startswith(nn + "-") and not force:
            sys.exit(f"new_map.py: Maps/{pack}/{fn} already uses number {nn}")
    order = pack_order(pack_dir, number)
    theme = theme.strip() or f"A {pack} pack arena."
    theme_sentence = theme if theme.endswith(".") else theme + "."

    print(f"{pack} {nn} {name} (slug {slug}, order {order})")
    if use_python:
        generator = f"tools/{pack.lower()}/{snake_of(name)}.py"
        gen_cmd = f"python3 -I {generator}"
        write(os.path.join(ROOT, generator), PY_TEMPLATE.format(pack=pack, pack_lower=pack.lower(), nn=nn, name=name, slug=slug,
                                                                 snake=snake_of(name), theme=theme_sentence, order=order), force)
    else:
        generator = f"Source/MapKit/Generators/{slug}.cs"
        gen_cmd = f'CallOfSlop --gen-map "{name}"'
        write(os.path.join(ROOT, generator), CS_TEMPLATE.format(pack=pack, nn=nn, name=name, slug=slug, slug_lower=slug.lower(),
                                                                 number=number, theme=theme_sentence, order=order), force)
    write(os.path.join(pack_dir, f"{nn}-{slug}.md"),
          DOC_TEMPLATE.format(nn=nn, name=name, pack=pack, theme_sentence=theme_sentence, generator=generator, gen_cmd=gen_cmd), force)
    write(os.path.join(pack_dir, "Decals", slug, "README.md"), DECAL_README.format(name=name, pack=pack, slug=slug), force)

    sections = ALWAYS_SECTIONS + [s for s in pack_sections(pack, slug, name) if s not in ALWAYS_SECTIONS]
    print()
    print("Next:")
    if use_python:
        print(f"  1. Fill in build() in {generator}, then `{gen_cmd}` writes Maps/{pack}/{nn}-{slug}.json.")
    else:
        print(f"  1. Fill in Build() in {generator} and register it: add to GeneratorRegistry.All (Source/MapKit/Generators.cs) the line")
        print(f"         new Generators.{slug}(),")
        print(f"     then `{gen_cmd}` writes Maps/{pack}/{nn}-{slug}.json, loads it back and prints the MapChecks report (objectives,")
        print(f"     horizon fade, nav reach, fair walks, stairs, counts). Commit the JSON too: the game loads the file, not the class.")
    print(f"  2. `order` is {order} ({pack}'s convention; the map select sorts by it). Keep it unless the pack says otherwise.")
    print(f"  3. The map's own checks beyond MapChecks (a short seeded bot match, what its areas must have) go in a section in")
    print("     Source/App/SelfTest*.cs when the pack has one. The all-maps rules (Map horizons: fog within 22 m of the floor;")
    print("     Map objectives: hills clear of geometry; Stairs; Nav) run on main only, so run them yourself before pushing.")
    print(f"  4. Fill Maps/{pack}/{nn}-{slug}.md (theme, shape, layout, lanes, fairness, counts) and add the map to Maps/{pack}/pack.json's description.")
    print(f"  5. Check the branch: `python3 -I tools/mapkit/check_branch.py` (changed maps, --check-map, props on a gallery, name clashes).")
    print("  6. Every commit on the branch carries this line (CI then runs only these sections; main runs everything):")
    print()
    print(f"[tests: {', '.join(sections)}]")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
