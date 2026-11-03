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

# What a change to any construct code or part runs. A rule stops at the first match, so a rule above the general ones carries these.
SANDBOX_CORE = ["Constructs", "Construct logic", "Seated riders", "Construct run-over", "Ride frame", "Boarding assist",
                "Sandbox parts menu", "Sandbox tool wheel", "Mode modules", "Sandbox physics gun", "Sandbox body sync",
                "Construct links and power", "Sandbox cargo volumes", "Sandbox weapon parts", "Centre sockets"]
WELDS = ["Socket weld 3-step", "Freeform weld", "Freeform drivetrain", "Wheel welds", "Joint welds", "Construct joints", "Construct actuators", "Sandbox snap build"]

# (regex on the repo-relative path, sections)
RULES = [
    (r"^Maps/[^/]+/pack\.json$", ALL_MAPS + ["Map packs", "Map select", "Map vote", "Mode modules"]),
    (r"^Maps/Space/", ALL_MAPS + ["Space: gravity physics", "Gravity zones", "Planet gravity"]),
    (r"^Maps/Cluttered/", ALL_MAPS + ["Cluttered pack"]),
    (r"^Maps/Sandbox/", ALL_MAPS + ["Map packs", "Map select", "Constructs", "Sandbox parts menu", "Mode modules", "Map bake"]),
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
    (r"^Constructs/Beacons\.json|^Constructs/Parts/Storage/InventoryBeacon|^Constructs/Blueprints/BeaconDepot|^Source/Constructs/(ConstructBeacons|ConstructEditsBeacon)|^Source/Game/GameSandboxBeacon|^Source/UI/UiSandboxBeacon",
     ["Inventory beacon", "Inventory beacon network", "Construct storage", "Sandbox full hands", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Sandbox Tab inventory", "Constructs", "Mode modules"]),
    (r"^Source/Constructs/(ConstructStorage|ConstructEdits|ConstructGive|ConstructRoutes|WorldLinks)\.cs$",
     ["Inventory beacon", "Inventory beacon network", "Construct storage", "Sandbox full hands", "Constructs", "Construct logic", "Seated riders", "Construct run-over", "Ride frame", "Boarding assist",
      "Sandbox parts menu", "Sandbox tool wheel", "Mode modules", "Sandbox physics gun", "Sandbox body sync", "Construct links and power", "Sandbox cargo volumes", "Sandbox weapon parts", "Centre sockets"]),
    (r"^tools/constructs/(assembling|machine_tiers)|^Constructs/Assembling\.json|^Constructs/Parts/(Assembling|Components|Refining)/|^Constructs/Blueprints/(AssemblyBay|FoundryWorks|SmeltingStation)|^Constructs/Recycling\.json|^Constructs/Refining\.json|^Source/Constructs/ConstructAssembling|^Source/App/SelfTestAssembling",
     ["Assembling", "Assembling network", "Refining", "Refining network", "Gathering", "Bin loads", "Salvage haul", "Salvage haul network", "Inventory beacon", "Custom bins", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Sandbox Tab inventory", "Constructs", "Construct storage", "Mode modules", "Socket weld 3-step", "Freeform weld", "Sandbox physics gun", "Gathering network", "Module Run input", "Modules network", "Construct drivetrain", "Standalone powertrain", "Named link ports", "Link auto type", "Custom bins network"]),
    (r"^tools/constructs/machine_tiers|^Constructs/Resources\.json|^Constructs/Parts/Gathering/|^Source/Constructs/ConstructGatherer|^Source/Constructs/ConstructDrillShaft|^Source/Constructs/ConstructDrillDrive|^Source/Constructs/ResourceField|^Source/App/SelfTestDrillDrive",
     ["Gathering", "Gathering network", "Refining", "Refining network", "Module Run input", "Modules network", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Mode modules",
      "Construct drivetrain", "Standalone powertrain", "Named link ports", "Link auto type", "Custom bins", "Custom bins network"]),
    (r"^Source/Constructs/(ConstructDrive|ConstructDriveParts|ConstructPorts|ConstructPower)\.cs$",
     ["Construct drivetrain", "Standalone powertrain", "Freeform drivetrain", "Named link ports", "Link auto type", "Gathering", "Construct links and power", "Constructs", "Mode modules"]),
    (r"^Constructs/Extrusion\.json|^Constructs/Parts/Extrusion/|^Constructs/Blueprints/ExtruderLine|^Source/Constructs/(ConstructExtrusion|ConstructExtruder)|^Source/App/SelfTestExtruder",
     ["Extruder", "Extruder network", "Refining", "Bin loads", "Salvage haul", "Socket weld 3-step", "Freeform weld", "Point weld", "Construct configure", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Sandbox body sync", "Mode modules"]),
    (r"^Constructs/Refining\.json|^Constructs/Recycling\.json|^Constructs/Parts/Refining/|^Constructs/Blueprints/SmeltingStation|^Source/Constructs/ConstructRefining",
     ["Refining", "Refining network", "Bin loads", "Salvage haul", "Salvage haul network", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Mode modules"]),
    (r"^Constructs/Parts/Storage/|^Constructs/Blueprints/StorageBay|^Source/Constructs/(ConstructStoreVolume|ConstructStorage|ConstructCargo)\.cs$|^Source/App/Program\.SandboxCargo|^Source/Render/ConstructRenderCargo",
     ["Custom bins", "Custom bins network", "Sandbox cargo volumes", "Sandbox cargo network", "Construct storage", "Bin loads", "Salvage haul", "Salvage haul network",
      "Refining", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Mode modules", "Socket weld 3-step", "Freeform weld"]),
    (r"^tools/constructs/engine_parts|^Constructs/ModularEngine\.json|^Constructs/Parts/ModularEngine/|^Source/Constructs/ConstructEngine|^Source/Render/EngineFx|^Source/Audio/(EngineSounds|SynthEngine|Audio\.Engine)|^Source/App/SelfTestModularEngine",
     ["Modular engine", "Modular engine network", "Construct drivetrain", "Standalone powertrain", "Freeform drivetrain", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Mode modules",
      "Socket weld 3-step", "Freeform weld", "Wheel welds", "Joint welds", "Sandbox snap build", "Sandbox weapon parts"] + SANDBOX_CORE),
    (r"^tools/constructs/big_drive|^Constructs/Parts/BigDrive/|^Source/Constructs/(ConstructThrust|ConstructPlant|ConstructDrive|ConstructDriveParts|ConstructPower)\.cs$",
     ["Large drive", "Construct drivetrain", "Standalone powertrain", "Freeform drivetrain", "Wheel welds", "Construct links and power", "Interaction sweep", "No z-fighting",
      "Sandbox parts menu", "Sandbox Tab inventory", "Sandbox body sync", "Constructs", "Mode modules", "Modular engine", "Gathering"] + SANDBOX_CORE),
    (r"^Constructs/Parts/Systems/|^tools/constructs/large_systems|^Source/Constructs/(ConstructDeck|ConstructLauncher|ConstructSystems)\.cs$",
     ["Large systems", "Large systems network", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Constructs", "Construct logic", "Construct links and power", "Construct weapons", "Sandbox cargo volumes", "Sandbox cargo network", "Socket weld 3-step", "Freeform weld", "Wheel welds", "Joint welds", "Construct actuators", "Mode modules"] + SANDBOX_CORE),
    (r"^tools/constructs/(scale|capital_parts)|^Constructs/Parts/Capital/|^Source/Constructs/(Construct|ConstructNet|ConstructPatch|ConstructPartTree|ConstructBuild|ConstructPhysics|ConstructWorld|ConstructParts)\.cs$|^Source/Render/ConstructRenderBatch|^Source/Net/Net\.cs$",
     ["Capital parts", "Capital network", "Structural parts", "No z-fighting", "Constructs", "Construct scale", "Sandbox body sync", "Sandbox parts menu", "Sandbox Tab inventory", "Modular engine", "Extruder network", "Assembling network", "Flexible joints", "Large systems", "Large drive"] + SANDBOX_CORE + WELDS),
    (r"^Constructs/Parts/Actuators/|^Constructs/Parts/Hoses/|^tools/constructs/actuator_parts|^Source/Constructs/(ConstructFlex|ConstructFlexChute|ConstructHoses|JointSides)\.cs$|^Source/Render/RouteRender",
     ["Flexible joints", "Flexible joints network", "Construct joints", "Construct actuators", "Joint welds", "Joint sides", "Construct routed links", "World links", "Construct links and power", "Interaction sweep", "No z-fighting", "Sandbox parts menu", "Socket weld 3-step", "Freeform weld", "Wheel welds",
      "Constructs", "Construct logic", "Seated riders", "Construct run-over", "Ride frame", "Boarding assist", "Sandbox tool wheel", "Mode modules", "Sandbox physics gun", "Sandbox body sync"]),
    (r"^Constructs/|^Source/Constructs/|^Source/Game/GameSandbox|^Source/Game/Modes/Sandbox/|^Source/Maps/MapSandbox|^Source/Render/ConstructRender",
     SANDBOX_CORE),
    (r"^Source/Constructs/(SocketWeld|ConstructFreeform|ConstructEditsFreeform|ConstructEditsSocketWeld|ConstructJoin|ConstructBuild|Construct)\.cs$|^Source/App/Program\.SandboxSocketWeld",
     WELDS),
    (r"^Source/UI/UiSandboxParts|^Source/App/Program\.Sandbox",["Sandbox parts menu", "Sandbox tool wheel", "Constructs"]),
    (r"^Source/UI/RadialMenu|^Source/UI/SandboxToolWheel", ["Sandbox tool wheel"]),
    (r"^Parts/", PARTS),
    (r"^Source/Zombies/", PARTS),
    (r"^Source/Game/Zombies|^Source/Game/ZombieAI|^Source/Game/ZombieBots", ["Zombies mode", "Zombies flow", "Zombie animation", "Co-op"]),
    (r"^Source/Game/Modes/", ["Mode modules", "Mode state and matches", "Test Range", "Team Deathmatch", "King of the Hill",
                              "Capture the Flag", "Gun Game", "Map vote", "Pre-match lobby"]),
    (r"^Source/Game/Bot|^Source/Game/Bots", ["Bots", "Nav determinism", "Nav edge walks", "Bot stuck watch", "Bot doors",
                                            "Insane bots", "Bot vantage points", "Mode modules"]),
    (r"^Source/Physics/", ["Physics", "Collision parity", "Slopes", "Mantling", "Movers", "Planet gravity", "Gravity zones",
                           "Nav determinism", "Blast push", "Ragdolls and knockback"]),
    (r"^Source/Maps/Terrain|^Source/Render/TerrainRenderer|^Source/Game/GameTerrainDig|^Source/App/SelfTestTerrain", ["Terrain", "Terrain holes", "Terrain digging", "Terrain sculpting", "Terrain sculpting network", "Constructs"]),
    (r"Sculpt|^Source/Constructs/ConstructDigging|^Constructs/Digging\.json|^Constructs/Tools/HandDrill", ["Terrain sculpting", "Terrain sculpting network", "Terrain digging", "Interaction sweep", "Sandbox tool wheel", "Sandbox tool wheel outer ring", "Sandbox tool models"]),
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
    (r"^Source/App/Keybinds", ["Keybinds", "Sandbox Tab inventory", "Sandbox ghost turns", "Link free routing", "Sandbox snap build"]),
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
