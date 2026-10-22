"""Hand-picked "model" blocks for the 50 shipped weapons. write_designs() puts them at the end of each JSON file."""
import json, glob, os, re

D = {
    # ---- Pistols ----
    "P9 Sidearm": dict(base="pistol", finish="black", furniture="polymer", accentStyle="none", sights="post"),
    "Mk.23 Tactical": dict(base="pistol", receiverLength=0.25, receiverHeight=0.082, receiverWidth=0.054, barrelLength=0.035, barrelWidth=0.024,
                           muzzle="crown", topRail=True, finish="gunmetal", furniture="tan", accentStyle="none", gripAngle=12),
    "Viper .45": dict(base="pistol", receiverLength=0.19, receiverHeight=0.07, finish="chrome", furniture="darkwood", grip="wood",
                      hammer=True, gripAngle=20, accentStyle="none", sights="post"),
    "Rattler Revolver": dict(base="revolver", barrelLength=0.16, barrelStyle="square", finish="steel", furniture="wood", gripAngle=24,
                             grip="birdshead"),
    "Bastion Hand Cannon": dict(base="pistol", receiverLength=0.29, receiverHeight=0.095, receiverWidth=0.06, barrelLength=0.02, barrelWidth=0.03,
                                muzzle="ported", finish="chrome", furniture="black", topRail=True, accentStyle="stripe", gripAngle=10),
    "Wasp Machine Pistol": dict(base="pistol", receiverLength=0.2, magLength=0.07, muzzle="comp", foregrip="stub", finish="black",
                                furniture="polymer", accentStyle="stripe"),
    "Stinger Burst Pistol": dict(base="pistol", receiverLength=0.22, magLength=0.045, muzzle="comp", foregrip="folding", finish="gunmetal",
                                 furniture="black", accentStyle="stripe", sights="ghost"),
    "Hush SD": dict(base="pistol", receiverLength=0.19, barrelLength=0.15, barrelWidth=0.02, shroud="suppressor", shroudLength=1.0,
                    finish="black", furniture="polymer", accentStyle="none", sights="none"),
    "Pocket Derringer": dict(base="derringer"),

    # ---- SMGs ----
    "Hornet SMG": dict(base="smg", receiver="round", receiverHeight=0.08, handguard="ribbed", handguardLength=0.11, handguardHeight=0.062,
                       mag="curved", magLength=0.13, stock="crane", stockLength=0.2, sights="aperture", bolt="top", accentStyle="mag"),
    "Buzzsaw": dict(base="smg", receiver="slab", receiverLength=0.17, receiverHeight=0.1, receiverWidth=0.058, handguard="none",
                    barrelLength=0.07, mag="grip", magLength=0.09, stock="wire", stockLength=0.2, foregrip="strap", bolt="top",
                    finish="gunmetal", accentStyle="mag"),
    "Courier": dict(base="smg", receiver="slab", receiverLength=0.22, receiverHeight=0.095, receiverWidth=0.066, handguard="rail",
                    handguardLength=0.13, handguardHeight=0.075, mag="box", magLength=0.14, stock="skeleton", topRail=True,
                    finish="polymer", furniture="tan", accentStyle="mag"),
    "Whisper SD": dict(base="smg", receiver="round", receiverHeight=0.08, barrelLength=0.25, shroud="suppressor", shroudLength=0.95,
                       barrelWidth=0.032, handguard="none", mag="curved", magLength=0.13, stock="folding", sights="aperture",
                       finish="black", accentStyle="none"),
    "Kestrel PDW": dict(base="pdw", finish="black", furniture="olive", magColor="tan", accentStyle="none", sights="none"),
    "Tempest": dict(base="smg", receiverLength=0.2, receiverHeight=0.095, handguard="none", barrelLength=0.15, shroud="perforated",
                    shroudLength=0.55, foregrip="vertical", stock="wood", stockLength=0.24, mag="drum", magOffset=0.01,
                    finish="steel", furniture="wood", muzzle="comp", bolt="top", accentStyle="none"),
    "Ratchet Burst SMG": dict(base="smg", receiver="slab", receiverLength=0.2, receiverHeight=0.14, receiverWidth=0.06, barrelLength=0.12,
                              handguard="rail", handguardLength=0.07, mag="stick", magOffset=-0.03, magAngle=-12, stock="folding",
                              topRail=True, finish="white", furniture="black", accentStyle="stripe"),

    # ---- Assault rifles ----
    "BR-1 Prototype": dict(base="rifle", receiverLength=0.18, handguard="wood", handguardLength=0.17, handguardHeight=0.06,
                           stock="straight", grip="straight", stockLength=0.25, stockDrop=6, mag="box", magLength=0.07,
                           barrelLength=0.3, finish="gunmetal", furniture="wood", sights="aperture", bolt="side", accentStyle="mag"),
    "Vanguard AR": dict(base="rifle", topRail=True, handguard="quad", stock="crane", mag="curved", magLength=0.16, sights="post",
                        gasBlock=False, muzzle="flash", finish="black", furniture="polymer", accentStyle="none", magColor="polymer"),
    "Sentinel": dict(base="rifle", receiverHeight=0.095, handguard="wood", handguardLength=0.17, handguardHeight=0.07, gasBlock=True,
                     mag="curved", magLength=0.21, magAngle=10, stock="wood", stockDrop=8, sights="tall", bolt="side",
                     muzzle="brake", finish="gunmetal", furniture="wood", accentStyle="none", magColor="rust"),
    "Raptor": dict(base="rifle", receiver="slab", receiverLength=0.25, receiverHeight=0.095, handguard="rail", handguardLength=0.2,
                   topRail=True, stock="folding", stockLength=0.23, mag="curved", magLength=0.15, muzzle="flash", finish="tan",
                   furniture="black", accentStyle="stripe", sights="ghost"),
    "Bulldog": dict(base="rifle", receiverLength=0.24, receiverHeight=0.115, receiverWidth=0.078, barrelLength=0.25, handguard="slim",
                    handguardLength=0.12, handguardHeight=0.085, muzzle="brake", stock="wire", mag="box", magLength=0.13, magAngle=6,
                    gasBlock=True, sights="tall", finish="gunmetal", furniture="black", accentStyle="mag"),
    "Tri-Shot": dict(base="rifle", carryHandle=True, handguard="ribbed", handguardLength=0.17, handguardHeight=0.075, stock="fixed",
                     stockLength=0.26, mag="curved", magLength=0.15, muzzle="flash", gasBlock=True, finish="black",
                     furniture="black", accentStyle="mag"),
    "Phalanx Bullpup": dict(base="bullpup", foregrip="vertical", handguard="slim", furniture="olive", finish="black", mag="box",
                            magLength=0.12, magColor="white", accentStyle="none", muzzle="flash", sights="none", carryHandle=False,
                            topRail=True),
    "Specter SD": dict(base="rifle", receiverLength=0.22, barrelLength=0.4, barrelWidth=0.031, shroud="suppressor", shroudLength=0.9,
                       handguard="wood", handguardLength=0.15, handguardHeight=0.075, stock="thumbhole", stockLength=0.24,
                       mag="stick", magLength=0.1, finish="black", furniture="darkwood", accentStyle="none", sights="tall"),

    # ---- Marksman ----
    "Marksman M14": dict(base="dmr", receiverLength=0.2, handguard="stock", handguardLength=0.27, handguardHeight=0.06, stock="wood",
                         stockLength=0.26, cheekRest=False, mag="box", magLength=0.09, gasBlock=True, muzzle="flash", finish="gunmetal",
                         furniture="wood", bolt="side", accentStyle="none"),
    "Longbow DMR": dict(base="dmr", handguard="tube", handguardLength=0.3, handguardHeight=0.065, barrelStyle="fluted", barrelWidth=0.034,
                        stock="chassis", stockLength=0.25, bipod="folded", topRail=True, muzzle="brake", finish="black",
                        furniture="black", accentStyle="mag", mag="box", magLength=0.08),
    "Reaper Battle Rifle": dict(base="battle_rifle", carryHandle=True, handguard="slim", handguardLength=0.2, furniture="black",
                                finish="gunmetal", stock="fixed", stockLength=0.25, mag="box", magLength=0.12, magAngle=5,
                                sights="post", gasBlock=False, accentStyle="mag"),
    "Talon Burst DMR": dict(base="bullpup", receiverLength=0.45, receiverHeight=0.115, barrelLength=0.28, handguard="slim",
                            handguardLength=0.12, finish="white", furniture="gunmetal", mag="cell", magLength=0.09, accentStyle="glow",
                            muzzle="comp", cheekRest=True),
    "Hawk Semi": dict(base="dmr", receiverLength=0.19, receiverHeight=0.085, barrelWidth=0.022, handguard="slim", handguardLength=0.18,
                      handguardHeight=0.06, stock="thumbhole", stockLength=0.24, mag="box", magLength=0.06, finish="steel",
                      furniture="lightwood", accentStyle="none"),
    "Warden Lever-Action": dict(base="lever_rifle", finish="brass", furniture="wood", barrelLength=0.42),

    # ---- Snipers ----
    "Needle Bolt-Action": dict(base="bolt_sniper"),
    "Leviathan .50": dict(base="bolt_sniper", receiver="slab", receiverLength=0.34, receiverHeight=0.12, receiverWidth=0.075,
                          barrelWidth=0.04, barrelLength=0.5, handguard="quad", handguardLength=0.22, handguardHeight=0.09,
                          muzzle="brake", bipod="deployed", stock="chassis", stockLength=0.24, mag="box", magLength=0.09,
                          bolt="charging", finish="black", furniture="black", topRail=True, accentStyle="mag"),
    "Kite Semi-Sniper": dict(base="bolt_sniper", receiver="box", receiverLength=0.22, receiverHeight=0.09, handguard="wood",
                             handguardLength=0.2, handguardHeight=0.06, stock="thumbhole", stockLength=0.26, mag="curved",
                             magLength=0.1, bolt="side", muzzle="flash", bipod="none", finish="gunmetal", furniture="wood",
                             accentStyle="mag", magColor="gunmetal"),
    "Ghost SD": dict(base="bolt_sniper", receiver="box", receiverLength=0.26, barrelLength=0.62, shroud="suppressor", shroudLength=0.45,
                     barrelWidth=0.026, handguard="tube", handguardLength=0.28, handguardHeight=0.06, stock="folding", stockLength=0.26,
                     mag="box", magLength=0.07, finish="black", furniture="olive", bipod="folded", accentStyle="none"),
    "Railspike": dict(base="coil_rifle"),

    # ---- Shotguns ----
    "Breacher Pump": dict(base="pump_shotgun", barrelLength=0.24, handguardLength=0.13, stock="crane", stockLength=0.22, furniture="black",
                          muzzle="breacher", sideSaddle=True, sights="ghost", finish="black"),
    "Thunder Auto": dict(base="semi_shotgun", finish="gunmetal", furniture="black", stock="crane", sights="ghost"),
    "Double Barrel": dict(base="double_barrel", furniture="darkwood", finish="steel"),
    "Slug Pump": dict(base="pump_shotgun", barrelLength=0.36, barrelWidth=0.036, handguardLength=0.15, stock="wood", stockLength=0.27,
                      furniture="lightwood", finish="gunmetal", topRail=True, sights="post", muzzle="ported", shroud="heatshield",
                      shroudLength=0.5),
    "Street Sweeper": dict(base="drum_shotgun", finish="black", magColor="accent"),
    "Scattergun Lever": dict(base="lever_shotgun", barrelLength=0.26, muzzle="crown", finish="black", furniture="wood"),

    # ---- LMGs ----
    "Anvil LMG": dict(base="lmg", finish="olive", furniture="black"),
    "Hailstorm": dict(base="lmg", receiver="slab", receiverHeight=0.11, receiverWidth=0.07, shroud="perforated", shroudLength=0.6,
                      handguard="none", barrelWidth=0.034, carryHandle=False, sights="tall", stock="fixed", stockLength=0.25,
                      bipod="deployed", mag="beltbox", finish="gunmetal", furniture="black", accentStyle="mag"),
    "Bastille": dict(base="lmg", barrelStyle="fluted", barrelWidth=0.044, handguard="none", shroud="none", stock="thumbhole",
                     stockLength=0.26, furniture="wood", finish="black", carryHandle=True, mag="beltbox", bipod="folded"),
    "Grinder Rotary": dict(base="rotary"),
    "Torrent": dict(base="lmg", receiver="box", receiverHeight=0.11, receiverWidth=0.072, handguard="rail", handguardLength=0.2,
                    shroud="none", mag="drum", stock="crane", carryHandle=False, sights="tall", topRail=False, bipod="folded",
                    finish="black", furniture="olive", accentStyle="mag"),

    # ---- Special ----
    "Nailgun": dict(base="tool"),
    "Arc Carbine": dict(base="energy"),
    "Old Faithful Musket": dict(base="musket"),
    "Toy Blaster": dict(base="toy"),
}

ROOT = os.environ.get("CALLOFSLOP", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def fmt_val(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return repr(v) if isinstance(v, float) else str(v)
    return json.dumps(v)


def block(m):
    items = [f'"{k}": {fmt_val(v)}' for k, v in m.items()]
    lines, cur = [], ""
    for it in items:
        if cur and len(cur) + len(it) + 2 > 110:
            lines.append(cur + ",")
            cur = it
        else:
            cur = it if not cur else cur + ", " + it
    lines.append(cur)
    return '  "model": {\n' + "\n".join("    " + l for l in lines) + "\n  }"


def write_designs():
    for f in sorted(glob.glob(os.path.join(ROOT, "Weapons", "**", "*.json"), recursive=True)):
        if os.sep + "Frenzy" + os.sep in f:
            continue  # Weapons/Frenzy/classes.json is frenzy data, not a weapon
        text = open(f).read()
        name = json.loads(text)["name"]
        if name not in D:
            print("no design:", name)
            continue
        # strip an existing model block (always the last field)
        i = text.find('\n  "model": {')
        if i >= 0:
            text = text[:i].rstrip().rstrip(",") + "\n}\n"
        body = text.rstrip()
        assert body.endswith("}")
        body = body[:-1].rstrip()
        text = body + ",\n" + block(D[name]) + "\n}\n"
        json.loads(text)
        open(f, "w").write(text)


if __name__ == "__main__":
    write_designs()
