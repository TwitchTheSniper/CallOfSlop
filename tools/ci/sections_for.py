#!/usr/bin/env python3
"""Pick the self-test sections a change needs, from the files it touches.

    python3 -I tools/ci/sections_for.py [BASE]      # files changed vs BASE (default origin/main), prints a tag line
    python3 -I tools/ci/sections_for.py --files a b  # for an explicit list of paths

Prints one line, ready for a commit message:  [tests: A, B, C]
Every match adds its sections; the union is printed in --selftest-list order where known. Unknown paths add nothing,
and a change to any Source/App/SelfTest*.cs adds the section(s) that file registers (read from SelfTest.cs). Rules
live in RULES below: keep them short and keep the all-maps block in step with CLAUDE.md.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

ALL_MAPS = ["Maps", "Map files", "Pack assets", "Map horizons", "Map objectives", "Props", "Animated props",
            "Nav determinism", "Nav edge walks", "Stairs", "Map contract"]
PARTS = ["Part library", "Part coverage", "Model connectivity", "Optic mounts", "Workbench UI",
         "Workbench barrels and grips", "Workbench actions", "Action placement", "Weapon builder", "Zombies mode",
         "Zombies flow"]

# (regex on the repo-relative path, sections)
RULES = [
    (r"^Maps/Space/", ALL_MAPS + ["Space: gravity physics", "Gravity zones", "Planet gravity"]),
    (r"^Maps/Cluttered/", ALL_MAPS + ["Cluttered pack"]),
    (r"^Maps/World/", ALL_MAPS + ["World Tour: Silo", "World Tour: Bocage", "World Tour: Main Street", "World: Vuorikoski",
                                  "World: Slopview Mall", "World Tour: Village", "World: London", "Co-op"]),
    (r"^Maps/Zombies/", ALL_MAPS + ["Zombies mode", "Zombie animation"]),
    (r"^Maps/Legacy/", ALL_MAPS + ["High Ground pack", "Slopes", "Movers", "Flag pool", "Map select", "Map vote"]),
    (r"^Maps/Dev/", ALL_MAPS + ["Decals", "Pack decals", "Landmines", "Destruction"]),
    (r"^Maps/", ALL_MAPS),
    (r"^Props/|^tools/props/", ["Props", "Animated props", "Pack assets", "Military props"]),
    (r"^tools/(space|planet|cluttered|world|zombies|mapkit)/", ALL_MAPS + ["Map contract"]),
    (r"^Decals/|/Decals/", ["Decals", "Decal surfaces", "Pack decals", "Pack assets"]),
    (r"^Buildings/", ["Buildings", "Destruction", "Pack assets"]),
    (r"^Weapons/", ["Weapon files", "Loadouts and perks", "Recoil", "Gun mix", "Mode modules"]),
    (r"^Attachments/", ["Attachments", "Optics", "Attachment variety", "Sight pictures", "Recoil", "Loadouts and perks",
                        "Bot gear level", "Loudener", "Mode modules"]),
    (r"^Constructs/|^Source/Constructs/|^Source/Game/GameSandbox|^Source/Game/Modes/Sandbox/|^Source/Maps/MapSandbox|^Source/Render/ConstructRender",
     ["Constructs", "Construct logic", "Sandbox parts menu", "Mode modules"]),
    (r"^Source/UI/UiSandboxParts|^Source/App/Program\.Sandbox", ["Sandbox parts menu", "Constructs"]),
    (r"^Parts/", PARTS),
    (r"^Source/Zombies/", PARTS),
    (r"^Source/Game/Zombies|^Source/Game/ZombieAI|^Source/Game/ZombieBots", ["Zombies mode", "Zombies flow", "Zombie animation", "Co-op"]),
    (r"^Source/Game/Modes/", ["Mode modules", "Mode state and matches", "Test Range", "Team Deathmatch", "King of the Hill",
                              "Capture the Flag", "Gun Game", "Map vote", "Pre-match lobby"]),
    (r"^Source/Game/Bot|^Source/Game/Bots", ["Bots", "Nav determinism", "Nav edge walks", "Bot stuck watch", "Bot doors",
                                            "Insane bots", "Bot vantage points", "Mode modules"]),
    (r"^Source/Physics/", ["Physics", "Collision parity", "Slopes", "Mantling", "Movers", "Planet gravity", "Gravity zones",
                           "Nav determinism", "Blast push", "Ragdolls and knockback"]),
    (r"^Source/Maps/", ALL_MAPS + ["Map reload", "Map packs", "Map shapes", "Planet gravity", "Gravity zones"]),
    (r"^Source/Net/", ["Packets", "Server browser", "Invite codes", "Paste and packets", "Mode modules", "Steam transport"]),
    (r"^Source/Platform/Steam/|^Source/Game/GameSteam|^Source/App/Program\.Steam|^docs/steam/", ["Steam platform", "Steam transport"]),
    (r"^Source/Audio/", ["Sound", "Gun samples", "Gun mix", "Audio mixer", "Music", "Weird hardbass", "Frenzy music",
                         "Frenzy gun sound"]),
    (r"^Source/Render/", ["Arms", "Gun models", "Legs", "Body model", "Scene graph", "Sky", "Zombie animation", "Optic mounts"]),
    (r"^Source/UI/UiWorkbench", ["Workbench UI", "Workbench incompatible parts", "Workbench hidden categories"]),
    (r"^Source/UI/", ["HUD layout", "Keybinds", "Weapon select", "Loadout navigation", "Appearance", "Text"]),
    (r"^Source/Updates/", ["Updates", "Update cleanup", "Save folder"]),
    (r"^Source/App/Settings|^Source/App/SavePaths", ["Save folder", "Keybinds", "Map select"]),
    (r"^Source/Game/", ["Mode modules", "Matches", "Health", "Kill streaks", "Melee", "Launchers"]),
]


def changed_files(base: str) -> list[str]:
    out = subprocess.run(["git", "diff", "--name-only", f"{base}...HEAD"], cwd=ROOT, capture_output=True, text=True)
    files = [f for f in out.stdout.split("\n") if f]
    out = subprocess.run(["git", "status", "--porcelain"], cwd=ROOT, capture_output=True, text=True)
    files += [line[3:] for line in out.stdout.split("\n") if line]
    return sorted(set(files))


def list_order() -> list[str]:
    """Section names in registration order, from SelfTest.cs."""
    src = (ROOT / "Source/App/SelfTest.cs").read_text(encoding="utf-8", errors="replace")
    return re.findall(r'Section\("([^"]+)"', src)


def sections_of_test_file(path: str, order: list[str]) -> list[str]:
    """The sections a SelfTest*.cs file registers (by the method names it defines)."""
    src = (ROOT / "Source/App/SelfTest.cs").read_text(encoding="utf-8", errors="replace")
    try:
        body = (ROOT / path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    methods = set(re.findall(r"static void (Check\w+)\(", body))
    found = []
    for name, method in re.findall(r'Section\("([^"]+)",\s*(\w+)', src):
        if method in methods:
            found.append(name)
    return found


def main() -> int:
    args = sys.argv[1:]
    if args[:1] == ["--files"]:
        files = args[1:]
    else:
        files = changed_files(args[0] if args else "origin/main")
    order = list_order()
    picked: set[str] = set()
    for f in files:
        for pattern, secs in RULES:
            if re.search(pattern, f):
                picked.update(secs)
                break
        if re.match(r"^Source/App/SelfTest\w*\.cs$", f):
            picked.update(sections_of_test_file(f, order))
    known = [s for s in order if s in picked]
    unknown = sorted(picked - set(order))
    if unknown:
        print("# not in --selftest-list (dropped): " + ", ".join(unknown), file=sys.stderr)
    if not known:
        print("# no section rule matched; run the sections you judge relevant", file=sys.stderr)
        return 1
    print("[tests: " + ", ".join(known) + "]")
    return 0


if __name__ == "__main__":
    sys.exit(main())
