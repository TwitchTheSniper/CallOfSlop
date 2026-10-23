#!/usr/bin/env python3
"""Pre-push checklist for a map branch: what changed against origin/main and the traps map branches hit late.

    python3 -I tools/mapkit/check_branch.py [--base origin/main] [--exe PATH] [--fix] [--no-game]

Run from the repository root with the game built (`dotnet build -c Release --no-restore`). It:

  1. lists the maps, props, decals, buildings and tools the branch adds or changes (committed and uncommitted);
  2. runs `CallOfSlop --check-map` on every changed map (it loads the file and runs the Map contract checks) and shows its verdict (the game writes to callofslop.log);
  3. checks that every new or changed global prop is on a Dev gallery map: the generated galleries must be current
     (tools/props/gallery.py --check; --fix regenerates them) and a prop no gallery or hand-made Dev map shows is named;
  4. looks for prop-name clashes: a new prop's name against every other prop in the tree and against props that
     origin/main has gained since the branch started (two branches adding a "Crate" each only clash at the merge);
  5. checks that files the branch adds don't already exist on origin/main under the same path (another branch's tool or
     map that landed first: a merge conflict waiting to happen);
  6. prints the `[tests: ...]` line the branch's commits should carry: the all-maps sections every map branch needs
     (Map horizons, Map objectives, Props, Animated props, Pack assets, Maps, Map files, Nav determinism, Stairs) plus
     the pack's own sections and the ones for what else changed (decals, military props, buildings...).

Exit code: the number of problems (notes don't count). Nothing is changed unless --fix is given.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ALWAYS_SECTIONS = ["Map horizons", "Map objectives", "Props", "Animated props", "Pack assets", "Maps", "Map files",
                   "Nav determinism", "Stairs"]
# What else changed -> the sections that cover it (a path prefix or regex, and the section names).
EXTRA_SECTIONS = [
    (r"^Decals/|^Maps/[^/]+/Decals/", ["Decals", "Pack decals"]),
    (r"^Props/Military/", ["Military props"]),
    (r"^Props/Volatile/", ["Volatile props"]),
    (r"^Buildings/|^Maps/[^/]+/Buildings/", ["Buildings"]),
    (r"^Models/|^Maps/[^/]+/Models/", ["Pack assets", "Mesh"]),
    (r"^Maps/[^/]+/Skies/|^Maps/[^/]+/[^/]+\.png$", ["Skies"]),
    (r"^Maps/Zombies/", ["Zombies mode"]),
    (r"^Maps/Cluttered/", ["Cluttered pack"]),
]

problems = 0
notes = 0


def problem(msg):
    global problems
    problems += 1
    print(f"  PROBLEM  {msg}")


def note(msg):
    global notes
    notes += 1
    print(f"  note     {msg}")


def ok(msg):
    print(f"  ok       {msg}")


def git(*args, check=True):
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True)
    if check and r.returncode != 0:
        sys.exit(f"check_branch.py: git {' '.join(args)} failed: {r.stderr.strip()}")
    return r.stdout


def changed_files(base):
    """(added, modified, deleted) paths against base, including the working tree and untracked files."""
    added, modified, deleted = set(), set(), set()
    for line in git("diff", "--name-status", "-M", base).splitlines():
        parts = line.split("\t")
        status, path = parts[0], parts[-1]
        if status.startswith("A"):
            added.add(path)
        elif status.startswith("D"):
            deleted.add(path)
        elif status.startswith("R"):
            added.add(path)
            deleted.add(parts[1])
        else:
            modified.add(path)
    for path in git("ls-files", "--others", "--exclude-standard").splitlines():
        if path:
            added.add(path)
    return sorted(added), sorted(modified), sorted(deleted)


def section_names():
    names = []
    try:
        with open(os.path.join(ROOT, "Source", "App", "SelfTest.cs"), encoding="utf-8") as f:
            for line in f:
                m = re.search(r'Section\("([^"]+)"', line)
                if m:
                    names.append((m.group(1), line.lower()))
    except OSError:
        pass
    return names


def find_exe(explicit):
    if explicit:
        return explicit
    for cand in [os.path.join(ROOT, "bin", "Release", "net10.0", "CallOfSlop.dll"),
                 os.path.join(ROOT, "bin", "Release", "net10.0", "CallOfSlop.exe")]:
        if os.path.exists(cand):
            return cand
    return None


def run_game(exe, *args):
    """Runs the game headless and returns its exit code and the log it wrote (console output goes to callofslop.log)."""
    cmd = ["dotnet", exe] if exe.endswith(".dll") else [exe]
    r = subprocess.run([*cmd, *args], cwd=os.path.dirname(exe), capture_output=True, text=True, timeout=300)
    log = ""
    try:
        with open(os.path.join(os.path.dirname(exe), "callofslop.log"), encoding="utf-8", errors="replace") as f:
            log = f.read()
    except OSError:
        pass
    out = (r.stdout + r.stderr).strip()
    if out and out in log:
        out = ""  # --check-map prints to the terminal and the log alike: don't show it twice
    return r.returncode, (log.strip() + ("\n" + out if out else "")).strip()


def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def prop_names_in_tree():
    """Every prop name in the tree: name.lower() -> list of paths (global Props/ and every pack's Props/)."""
    names = {}
    folders = [os.path.join(ROOT, "Props")]
    maps = os.path.join(ROOT, "Maps")
    for pack in sorted(os.listdir(maps)):
        p = os.path.join(maps, pack, "Props")
        if os.path.isdir(p):
            folders.append(p)
    for folder in folders:
        for dirpath, dirnames, files in os.walk(folder):
            dirnames.sort()
            if os.path.basename(dirpath).lower() == "flags":
                continue
            for fn in sorted(files):
                if not fn.endswith(".json"):
                    continue
                path = os.path.join(dirpath, fn)
                try:
                    d = load_json(path)
                except (OSError, ValueError):
                    continue
                if isinstance(d, dict) and "parts" in d and isinstance(d.get("name"), str):
                    names.setdefault(d["name"].strip().lower(), []).append(os.path.relpath(path, ROOT).replace("\\", "/"))
    return names


def prop_names_on(base, paths):
    """name.lower() -> path for prop files at base (read with git show)."""
    out = {}
    for path in paths:
        text = git("show", f"{base}:{path}", check=False)
        if not text:
            continue
        try:
            d = json.loads(text)
        except ValueError:
            continue
        if isinstance(d, dict) and "parts" in d and isinstance(d.get("name"), str):
            out[d["name"].strip().lower()] = path
    return out


def main(argv):
    base = "origin/main"
    exe = None
    fix = "--fix" in argv
    no_game = "--no-game" in argv
    for i, a in enumerate(argv):
        if a == "--base" and i + 1 < len(argv):
            base = argv[i + 1]
        if a == "--exe" and i + 1 < len(argv):
            exe = argv[i + 1]
    if not git("rev-parse", "--verify", "-q", base, check=False).strip():
        sys.exit(f"check_branch.py: {base} not found (git fetch origin, or --base <ref>)")
    branch = git("rev-parse", "--abbrev-ref", "HEAD").strip()
    merge_base = git("merge-base", "HEAD", base).strip()
    print(f"branch {branch} vs {base} (merge base {merge_base[:8]})")

    added, modified, deleted = changed_files(base)
    changed = added + modified
    maps = [p for p in changed if re.match(r"^Maps/[^/]+/[^/]+\.json$", p) and not p.endswith("/pack.json")]
    props = [p for p in changed if re.match(r"^Props/.+\.json$", p) or re.match(r"^Maps/[^/]+/Props/.+\.json$", p)]
    decals = [p for p in changed if re.match(r"^Decals/.+\.(json|png)$", p) or re.match(r"^Maps/[^/]+/Decals/.+\.(json|png)$", p)]
    tools = [p for p in changed if p.startswith("tools/") or p.startswith("Source/")]
    others = [p for p in changed if p not in maps and p not in props and p not in decals and p not in tools]

    print()
    print("1. Changed against", base)
    for label, lst in [("maps", maps), ("props", props), ("decals", decals), ("code and tools", tools), ("other", others), ("deleted", deleted)]:
        if lst:
            print(f"  {label} ({len(lst)}):")
            for p in lst[:40]:
                print(f"      {'+' if p in added else '-' if p in deleted else '~'} {p}")
            if len(lst) > 40:
                print(f"      ... and {len(lst) - 40} more")
    if not changed and not deleted:
        print("  nothing changed")

    print()
    print("2. --check-map on every changed map")
    exe = None if no_game else find_exe(exe)
    if not maps:
        print("  no maps changed")
    elif exe is None:
        note("game not built (bin/Release/net10.0/CallOfSlop.dll): build it or pass --exe; --check-map skipped")
    else:
        for m in maps:
            full = os.path.join(ROOT, m)
            if not os.path.exists(full):
                continue
            code, log = run_game(exe, "--check-map", full)
            verdict = next((ln for ln in log.splitlines() if ln.startswith(("FAILED:", "ERROR:"))),
                           next((ln for ln in log.splitlines() if ln.startswith("OK:")), log.splitlines()[-1] if log else "(no output)"))
            if code == 0:
                ok(f"{m}: {verdict}")
                for ln in log.splitlines():
                    if ln.startswith("note:"):
                        note(f"{m}: {ln}")
            else:
                problem(f"{m}: {verdict}")

    print()
    print("3. Every global prop on a Dev gallery")
    gallery = os.path.join(ROOT, "tools", "props", "gallery.py")
    if os.path.exists(gallery):
        r = subprocess.run([sys.executable, "-I", gallery, "--check"], cwd=ROOT, capture_output=True, text=True)
        if r.returncode == 0:
            ok("generated galleries are current (tools/props/gallery.py --check)")
        elif fix:
            subprocess.run([sys.executable, "-I", gallery], cwd=ROOT)
            ok("regenerated the galleries (--fix): commit the maps it wrote and re-pin their fingerprints in SelfTestPackAssets.cs")
        else:
            problem(f"{(r.stdout + r.stderr).strip().splitlines()[0]} (or run this tool with --fix)")
    else:
        note("tools/props/gallery.py not on this branch: check Maps/Dev shows every new prop yourself")
    # Props shown by a Prop op on any Dev map (Clutter is random).
    shown = set()
    dev = os.path.join(ROOT, "Maps", "Dev")
    for fn in sorted(os.listdir(dev)):
        if fn.endswith(".json") and fn != "pack.json":
            try:
                for op in load_json(os.path.join(dev, fn)).get("ops", []):
                    if op.get("op") == "Prop" and isinstance(op.get("name"), str):
                        shown.add(op["name"].replace("global:", "").strip().lower())
            except (OSError, ValueError):
                pass
    for p in props:
        if not p.startswith("Props/") or "/Flags/" in p:
            continue
        try:
            d = load_json(os.path.join(ROOT, p))
        except (OSError, ValueError) as ex:
            problem(f"{p}: not valid JSON ({ex})")
            continue
        if "parts" not in d or not isinstance(d.get("name"), str):
            continue
        if d["name"].strip().lower() not in shown:
            problem(f"{p}: '{d['name']}' is on no Dev map (run tools/props/gallery.py, or place it on a hand-made Dev map)")
    if props:
        ok(f"{len(props)} changed prop file(s) checked for a Dev map")

    print()
    print("4. Prop-name clashes")
    tree = prop_names_in_tree()
    for name, paths in sorted(tree.items()):
        globals_ = [x for x in paths if x.startswith("Props/")]
        if len(globals_) > 1:
            problem(f"two global props are called '{name}': {', '.join(globals_)}")
        by_pack = {}
        for x in paths:
            m = re.match(r"^Maps/([^/]+)/Props/", x)
            if m:
                by_pack.setdefault(m.group(1), []).append(x)
        for pack, xs in by_pack.items():
            if len(xs) > 1:
                problem(f"pack {pack} has two props called '{name}': {', '.join(xs)}")
            elif globals_ and any(x in changed for x in xs + globals_):
                note(f"pack {pack}'s '{name}' shadows the global one ({globals_[0]}) for that pack's maps: intended?")
    # Props origin/main gained since the merge base: a name this branch also adds clashes only at the merge.
    gained = [ln.split("\t")[-1] for ln in git("diff", "--name-status", "--diff-filter=AR", merge_base, base).splitlines()
              if re.search(r"^(Props/|Maps/[^/]+/Props/).+\.json$", ln.split("\t")[-1])]
    if gained:
        main_names = prop_names_on(base, gained)
        mine = {}
        for p in props:
            if p in added:
                try:
                    d = load_json(os.path.join(ROOT, p))
                    if isinstance(d, dict) and isinstance(d.get("name"), str):
                        mine[d["name"].strip().lower()] = p
                except (OSError, ValueError):
                    pass
        for name, path in mine.items():
            if name in main_names and main_names[name] != path:
                problem(f"'{name}' ({path}) was also added on {base} ({main_names[name]}) since this branch started: rename one")
        ok(f"{len(mine)} new prop name(s) compared with the {len(main_names)} props {base} gained since the merge base")
    else:
        ok(f"{base} gained no props since the merge base")

    print()
    print("5. New files that already exist on", base)
    new_on_branch = [p for p in added if p not in deleted]
    collisions = []
    for p in new_on_branch:
        if subprocess.run(["git", "cat-file", "-e", f"{base}:{p}"], cwd=ROOT, capture_output=True).returncode == 0:
            # Added here and present on base: a clash only if the merge base lacked it.
            if subprocess.run(["git", "cat-file", "-e", f"{merge_base}:{p}"], cwd=ROOT, capture_output=True).returncode != 0:
                collisions.append(p)
    for p in collisions:
        problem(f"{p} was created on this branch and on {base} since the merge base: rename or merge {base} first")
    if not collisions:
        ok(f"{len(new_on_branch)} new file(s), none already on {base}")

    print()
    print("6. Self-test sections for this branch")
    sections = list(ALWAYS_SECTIONS)
    names = section_names()
    packs = sorted({m.group(1) for p in maps for m in [re.match(r"^Maps/([^/]+)/", p)] if m})
    hints = set(packs)
    for p in maps:
        m = re.match(r"^Maps/[^/]+/(?:\d+-)?([A-Za-z0-9]+)\.json$", p)
        if m:
            hints.add(m.group(1))
            try:
                nm = load_json(os.path.join(ROOT, p)).get("name")
                if isinstance(nm, str):
                    hints.add(nm)
            except (OSError, ValueError):
                pass
    for sec, line in names:
        if sec in sections or sec in ("Map horizons", "Map objectives"):
            continue
        if any(h.lower() in line for h in hints if h.lower() not in ("dev",)):
            sections.append(sec)
    for pattern, secs in EXTRA_SECTIONS:
        if any(re.search(pattern, p) for p in changed + deleted):
            for s in secs:
                if s not in sections and any(s == sec for sec, _ in names):
                    sections.append(s)
    if "Dev" in packs:
        for s in ["Props", "Animated props", "Military props", "Decals"]:
            if s not in sections and any(s == sec for sec, _ in names):
                sections.append(s)
    print("  put this line in every commit message on the branch (CI runs only these; main runs everything):")
    print()
    print(f"[tests: {', '.join(sections)}]")
    print()
    print(f"{problems} problem(s), {notes} note(s)")
    return min(problems, 100)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
