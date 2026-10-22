#!/usr/bin/env python3
"""Writes the Cluttered pack's supermarket props (Maps/Cluttered/Props/Supermarket/*.json): shop fittings no global prop
covers (gondola shelving, end caps, checkout counters, freezer cases, a deli counter, produce bins, bakery racks, nested
carts and a cart corral, hanging aisle and department signs, ceiling lights, the store's front sign) and the stock room's
kit (pallet racks, pallets of goods, cardboard bales, a baler, roll cages, a pallet jack). They're pack-local
(Props/README.md, "Pack props"): only the Cluttered pack's maps see them. 07 Supermarket (tools/cluttered/supermarket.py)
uses them.

Usage: python3 tools/cluttered/supermarket_props.py <repo root>

Conventions as in the global library: the origin is on the floor at the prop's centre, +X is "along" (shelves, counters),
+Z is the front (the shopper's side). Store fittings are solid and unbreakable (cover you can trust: shelves, counters,
freezers, racks); loose stock (produce bins, promo stacks, pallets, bales, bakery racks, roll cages) breaks. Ceiling lights
and hanging signs are cosmetic and placed with the Prop op's "y" (their top touches the ceiling).
"""
import json, os, random, sys

REPO = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(REPO, "Maps/Cluttered/Props/Supermarket")


def part(size, offset, color, **kw):
    p = {"size": [round(v, 3) for v in size], "offset": [round(v, 3) for v in offset], "color": color}
    p.update(kw)
    return p


PROPS = []


def add(name, description, parts, collision="solid", collider=None, scatter=None, breakable=None, fade=None):
    d = {"name": name, "description": description, "collision": collision, "parts": parts}
    if collider is not None:
        d["collider"] = collider
    if scatter is not None:
        d["scatter"] = scatter
    if breakable is not None:
        d["breakable"] = breakable
    if fade is not None:
        d["fadeDistance"] = fade
    PROPS.append(d)


SQUARE = {"randomYaw": False, "yawStep": 90, "scale": [1, 1]}

# ---------------------------------------------------------------- gondola shelving (double-sided, 2.4 x 2.0 x 1.1)
SHELF_KINDS = {
    "Cans": (0.30, ["#b8262a", "#c84a1e", "#2e7a3c", "#d8c23a", "#8a2a2a", "#e8e2d0"], ["#f2efe6", "#e8d24a", "#202020"],
             "Canned soup, beans and tomatoes"),
    "Cereal": (0.36, ["#e8b81e", "#d8402a", "#2c5fb0", "#e0782a", "#6a3a9a", "#3a9a5a"], ["#ffffff", "#f6e04a", "#202020"],
               "Cereal boxes in loud colours"),
    "Bottles": (0.34, ["#2a6ad0", "#c02028", "#2a9a4a", "#e8a020", "#6a2a7a", "#8ac8e8"], ["#ffffff", "#f0f0f0", "#202020"],
                "Soft drinks, juice and water bottles"),
    "Snacks": (0.26, ["#e83a2a", "#f0c020", "#2a8ad8", "#e86ab0", "#40b040", "#ff8a20"], ["#ffffff", "#202020", "#f6e04a"],
               "Crisps, sweets and biscuits"),
    "Household": (0.34, ["#2a5ab0", "#f08a1a", "#e8e8ec", "#3aa060", "#a02a8a", "#30b0c0"], ["#ffffff", "#202020", "#e8d24a"],
                  "Detergent, kitchen roll and cleaning sprays"),
}
LEVELS = [0.14, 0.58, 1.02, 1.46]
for kind, (ph, cols, bands, what) in SHELF_KINDS.items():
    rnd = random.Random(sum(map(ord, kind)))
    L, D = 2.4, 1.1
    ps = [part((L, 0.14, D - 0.02), (0, 0.07, 0), "darkgrey"),
          part((L, 1.86, 0.06), (0, 1.07, 0), "#cfd2d4", material="metal"),
          part((0.05, 2.0, D), (L / 2 - 0.025, 1.0, 0), "#9aa0a6"),
          part((0.05, 2.0, D), (-L / 2 + 0.025, 1.0, 0), "#9aa0a6")]
    for y in LEVELS:
        for s in (1, -1):
            ps.append(part((L - 0.1, 0.03, 0.48), (0, y + 0.015, s * 0.29), "#c4c8cc"))
            ps.append(part((L - 0.1, 0.05, 0.012), (0, y + 0.01, s * 0.535), "#f2d23a" if y in (0.58, 1.46) else "white"))
            for k, x in enumerate((-0.78, 0.0, 0.78)):
                h = ph * rnd.uniform(0.82, 1.0) * (0.9 if y == LEVELS[-1] else 1.0)
                c = rnd.choice(cols)
                ps.append(part((0.72, h, 0.4), (x, y + 0.03 + h / 2, s * 0.3), c))
                ps.append(part((0.66, h * 0.32, 0.012), (x, y + 0.03 + h * 0.55, s * 0.506), rnd.choice(bands)))
    ps.append(part((0.7, 0.34, 0.5), (0.55, 2.17, 0), "cardboard"))
    ps.append(part((0.6, 0.28, 0.45), (-0.6, 2.14, 0.02), "cardboard"))
    add(f"Grocery Shelf {kind}", f"Double-sided supermarket gondola shelving stocked with {what.lower()} on both faces. Solid, unbreakable.",
        ps, collider=[{"size": [2.4, 2.0, 1.1]}], scatter=SQUARE)

# ---------------------------------------------------------------- end cap (promo display at the end of a shelf run)
rnd = random.Random(7)
ps = [part((1.1, 0.14, 0.55), (0, 0.07, 0), "darkgrey"), part((1.1, 1.66, 0.05), (0, 0.97, -0.25), "#cfd2d4", material="metal"),
      part((0.04, 1.8, 0.55), (0.53, 0.9, 0), "#9aa0a6"), part((0.04, 1.8, 0.55), (-0.53, 0.9, 0), "#9aa0a6")]
for y in (0.14, 0.62, 1.1):
    ps.append(part((1.02, 0.03, 0.46), (0, y + 0.015, 0.02), "#c4c8cc"))
    ps.append(part((1.02, 0.05, 0.012), (0, y + 0.01, 0.255), "#e83030"))
    for x in (-0.25, 0.25):
        h = rnd.uniform(0.28, 0.38)
        ps.append(part((0.46, h, 0.4), (x, y + 0.03 + h / 2, 0.02), rnd.choice(["#e8b81e", "#d8402a", "#2c5fb0", "#3a9a5a", "#e86ab0"])))
ps += [part((1.1, 0.34, 0.05), (0, 1.97, -0.2), "#e02828"), part((0.7, 0.12, 0.012), (0, 2.0, -0.17), "#ffe04a"),
       part((0.16, 0.16, 0.012), (-0.42, 1.97, -0.17), "white"), part((0.16, 0.16, 0.012), (0.42, 1.97, -0.17), "white")]
add("Shelf End Cap", "The promo display at the end of a shelf run: three shelves of offers under a red SALE header (front faces +Z). Solid.",
    ps, collider=[{"size": [1.1, 1.8, 0.55]}], scatter=SQUARE)

# ---------------------------------------------------------------- dairy chiller (open multideck fridge against a wall)
rnd = random.Random(11)
ps = [part((2.4, 0.2, 0.9), (0, 0.1, 0), "#2a3036"), part((2.4, 2.0, 0.1), (0, 1.0, -0.4), "#e8ecef"),
      part((0.06, 2.0, 0.9), (1.17, 1.0, 0), "#d8dde2"), part((0.06, 2.0, 0.9), (-1.17, 1.0, 0), "#d8dde2"),
      part((2.4, 0.22, 0.75), (0, 2.09, -0.07), "#e8ecef"), part((2.3, 0.16, 0.02), (0, 2.08, 0.31), "#2a8ad8"),
      part((2.26, 0.04, 0.06), (0, 1.94, 0.2), "#dff4ff", anim={"type": "flicker", "rate": 1.5, "duty": 0.97, "color": "#eef8ff", "off": "#7a8a96", "glow": 0.9, "freezeDistance": 60})]
for y, depth in ((0.2, 0.8), (0.62, 0.7), (1.02, 0.62), (1.4, 0.54)):
    ps.append(part((2.28, 0.03, depth), (0, y + 0.015, -0.35 + depth / 2), "#c8d0d8"))
    for x in (-0.84, -0.28, 0.28, 0.84):
        h = rnd.uniform(0.2, 0.3)
        ps.append(part((0.5, h, depth - 0.1), (x, y + 0.03 + h / 2, -0.35 + depth / 2), rnd.choice(["#f4f6f8", "#f4f6f8", "#7ab0e0", "#f0d860", "#e88aa0", "#a0d070"])))
add("Dairy Chiller", "An open-fronted multideck fridge of milk, yoghurt and cheese with a lit blue header (front faces +Z). Solid, unbreakable.",
    ps, collider=[{"size": [2.4, 2.2, 0.9]}], scatter=SQUARE)

# ---------------------------------------------------------------- freezer case (upright, glass doors) and freezer island
rnd = random.Random(13)
ps = [part((2.4, 0.15, 0.95), (0, 0.075, 0), "#2a3036"), part((2.4, 2.0, 0.1), (0, 1.075, -0.425), "#e8ecef"),
      part((0.06, 2.15, 0.95), (1.17, 1.075, 0), "#e8ecef"), part((0.06, 2.15, 0.95), (-1.17, 1.075, 0), "#e8ecef"),
      part((2.4, 0.26, 0.95), (0, 2.02, 0), "#e8ecef"), part((2.3, 0.18, 0.02), (0, 2.03, 0.48), "#2a7fd0"),
      part((1.0, 0.07, 0.012), (0, 2.03, 0.492), "white"),
      part((2.28, 0.04, 0.06), (0, 1.86, 0.3), "#dff4ff", anim={"type": "flicker", "rate": 1.5, "duty": 0.97, "color": "#eef8ff", "off": "#7a8a96", "glow": 1.0, "freezeDistance": 60})]
for y in (0.15, 0.55, 0.95, 1.35):
    ps.append(part((2.28, 0.03, 0.7), (0, y + 0.015, -0.05), "#c8d0d8"))
    for x in (-0.84, -0.28, 0.28, 0.84):
        h = rnd.uniform(0.22, 0.3)
        ps.append(part((0.5, h, 0.55), (x, y + 0.03 + h / 2, -0.05), rnd.choice(["#2a6ad0", "#e8eef4", "#c02828", "#3aa060", "#f0c020", "#8a4ac0"])))
for x in (-1.14, -0.57, 0.0, 0.57, 1.14):
    ps.append(part((0.05, 1.75, 0.04), (x, 1.02, 0.455), "#b8c0c8"))
for x in (-0.66, -0.09, 0.48, 1.05):
    ps.append(part((0.03, 0.6, 0.04), (x, 1.1, 0.49), "chrome"))
ps.append(part((2.3, 0.05, 0.04), (0, 1.88, 0.455), "#b8c0c8"))
ps.append(part((2.3, 0.05, 0.04), (0, 0.16, 0.455), "#b8c0c8"))
add("Freezer Case", "An upright glass-door freezer of frozen food with a lit interior and a blue FROZEN header (front faces +Z). Solid, unbreakable.",
    ps, collider=[{"size": [2.4, 2.15, 0.95]}], scatter=SQUARE)

rnd = random.Random(17)
ps = [part((2.4, 0.76, 1.1), (0, 0.38, 0), "#eef1f3", material="metal"), part((2.44, 0.06, 1.14), (0, 0.79, 0), "#cfd6dc"),
      part((2.42, 0.1, 1.12), (0, 0.22, 0), "#3a6ea5"), part((2.42, 0.08, 1.12), (0, 0.04, 0), "#2a3036"),
      part((2.28, 0.02, 0.98), (0, 0.81, 0), "#1d3550")]
for x in (-0.86, -0.29, 0.29, 0.86):
    for z in (-0.26, 0.26):
        h = rnd.uniform(0.08, 0.14)
        ps.append(part((0.5, h, 0.42), (x + rnd.uniform(-0.03, 0.03), 0.82 + h / 2, z), rnd.choice(["#2a6ad0", "#e8eef4", "#c02828", "#f0c020", "#3aa060"])))
for z in (-0.5, 0.0, 0.5):
    ps.append(part((2.3, 0.03, 0.03), (0, 0.93, z), "chrome"))
for x in (-1.15, 1.15):
    ps.append(part((0.03, 0.12, 1.04), (x, 0.88, 0), "chrome"))
add("Freezer Island", "A long open chest freezer in the middle of the aisle, frozen goods under sliding glass lids. Waist-high cover, unbreakable.",
    ps, collider=[{"size": [2.4, 0.92, 1.1]}], scatter=SQUARE)

# ---------------------------------------------------------------- deli counter, bakery rack, bread display
ps = [part((2.4, 0.85, 0.95), (0, 0.425, 0), "#b8bec4", material="metal"), part((2.4, 0.12, 0.02), (0, 0.06, 0.48), "black"),
      part((2.4, 0.22, 0.02), (0, 0.6, 0.48), "#a8322a"), part((2.3, 0.04, 0.72), (0, 0.87, 0.08), "white"),
      part((2.4, 0.03, 0.36), (0, 1.29, -0.27), "#d8dde2"), part((2.4, 0.05, 0.05), (0, 1.27, 0.12), "#b8c0c8"),
      part((0.04, 0.45, 0.6), (1.18, 1.08, 0.12), "#b8c0c8"), part((0.04, 0.45, 0.6), (-1.18, 1.08, 0.12), "#b8c0c8"),
      part((0.32, 0.12, 0.3), (0.8, 1.37, -0.27), "white"), part((0.2, 0.08, 0.012), (0.8, 1.38, -0.115), "#203040")]
for k, x in enumerate((-0.95, -0.57, -0.19, 0.19, 0.57, 0.95)):
    ps.append(part((0.34, 0.07, 0.26), (x, 0.925, 0.0), ["#e88a8a", "#c84040", "#f0c0a0", "#d86a6a", "#e8d0a0", "#a83a3a"][k]))
    ps.append(part((0.34, 0.06, 0.24), (x, 0.92, 0.3), ["#f0d0b0", "#d85a5a", "#e8a0a0", "#c8a070", "#e89090", "#f4e0c0"][k]))
    ps.append(part((0.12, 0.07, 0.01), (x, 0.93, 0.44), "white"))
add("Deli Counter", "A stainless deli counter: trays of meat and cheese under a sneeze guard, a scale on the top shelf (customers stand at +Z). Solid, unbreakable.",
    ps, collider=[{"size": [2.4, 1.32, 1.0]}], scatter=SQUARE)

ps = []
for x in (-0.68, 0.68):
    for z in (-0.28, 0.28):
        ps.append(part((0.03, 1.9, 0.03), (x, 0.95, z), "#9aa0a6"))
for k, y in enumerate((0.3, 0.65, 1.0, 1.35, 1.7)):
    ps.append(part((1.4, 0.02, 0.6), (0, y, 0), "#b8bec4"))
    for j, x in enumerate((-0.42, 0.0, 0.42)):
        if (k + j) % 4 == 3:
            ps.append(part((0.62, 0.07, 0.07), (x, y + 0.045, 0.12), "#d8a050", rotation=[0, 12, 0]))
        else:
            ps.append(part((0.34, 0.15, 0.2), (x, y + 0.085, 0.05 * (1 if j % 2 else -1)), ["#c88a40", "#a86a30", "#e0b070", "#8a5428"][(k + j) % 4]))
add("Bakery Rack", "A steel bakery rack loaded with loaves, rolls and baguettes.", ps,
    collider=[{"size": [1.4, 1.9, 0.6]}], scatter=SQUARE,
    breakable={"health": 160, "breakBy": "all", "sound": "metal", "debris": {"count": 16, "size": [0.06, 0.3], "speed": 3.5, "colors": ["#c88a40", "#e0b070", "#9aa0a6"]}})

ps = [part((1.6, 0.72, 0.9), (0, 0.36, 0), "lightwood", material="wood"), part((1.64, 0.04, 0.94), (0, 0.74, 0), "darkwood")]
for k, x in enumerate((-0.52, 0.0, 0.52)):
    ps.append(part((0.46, 0.14, 0.7), (x, 0.83, 0), "#b88a50", material="wood"))
    for j, z in enumerate((-0.18, 0.0, 0.18)):
        ps.append(part((0.34, 0.11, 0.16), (x + (0.04 if j == 1 else -0.03), 0.95, z), ["#c88a40", "#e0b070", "#a86a30"][(k + j) % 3]))
ps += [part((0.03, 0.4, 0.03), (0.72, 0.95, 0.38), "darkwood"), part((0.3, 0.2, 0.02), (0.72, 1.2, 0.38), "white"),
       part((0.22, 0.05, 0.012), (0.72, 1.24, 0.392), "#c02828")]
add("Bread Display", "A wooden bakery table with wicker baskets of fresh loaves and a price card.", ps,
    collider=[{"size": [1.6, 0.95, 0.9]}], scatter=SQUARE,
    breakable={"health": 90, "breakBy": "all", "sound": "wood", "debris": {"count": 14, "size": [0.06, 0.3], "speed": 3.5, "colors": ["#c88a40", "#e0b070", "#b88a50"]}})

# ---------------------------------------------------------------- produce bins (tilted display tables)
PRODUCE = {
    "Produce Bin Fruit": (["#c8202a", "#f08a1a", "#7ab830"], ["#e8302a", "#ffa030", "#9ad040"], "apples, oranges and limes"),
    "Produce Bin Greens": (["#3a8a2a", "#8ac850", "#6a2a6a"], ["#4aa038", "#a0d868", "#8a3a8a"], "lettuce, cabbage and red onions"),
    "Produce Bin Melons": (["#3a7a2a", "#f0d030", "#e04a40"], ["#4a8a38", "#f8e050", "#f06a50"], "watermelons, bananas and tomatoes"),
}
for name, (base, top, what) in PRODUCE.items():
    ps = [part((1.8, 0.7, 1.0), (0, 0.35, 0), "lightwood", material="wood"), part((1.84, 0.05, 1.04), (0, 0.72, 0), "darkwood"),
          part((1.8, 0.36, 0.05), (0, 0.98, -0.5), "darkwood"), part((0.05, 0.25, 1.0), (0.9, 0.86, 0), "darkwood"),
          part((0.05, 0.25, 1.0), (-0.9, 0.86, 0), "darkwood")]
    for k, x in enumerate((-0.58, 0.0, 0.58)):
        ps.append(part((0.56, 0.16, 0.94), (x, 0.84, 0), base[k], rotation=[10, 0, 0]))
        ps.append(part((0.32, 0.1, 0.34), (x - 0.08, 0.95, -0.18), top[k], rotation=[10, 0, 0]))
        ps.append(part((0.26, 0.09, 0.3), (x + 0.1, 0.91, 0.18), top[k], rotation=[10, 0, 0]))
        ps.append(part((0.18, 0.11, 0.012), (x, 0.82, 0.53), "white"))
    add(name, f"A tilted wooden produce display heaped with {what}, price cards along the front.", ps,
        collider=[{"size": [1.8, 1.0, 1.0]}], scatter=SQUARE,
        breakable={"health": 110, "breakBy": "all", "sound": "wood", "debris": {"count": 18, "size": [0.06, 0.3], "speed": 3.5, "colors": base + ["#c8a070"]}})

# ---------------------------------------------------------------- promo stack (soda cases on a pallet)
ps = [part((1.2, 0.14, 1.0), (0, 0.07, 0), "pallet", material="wood")]
for (x, z) in ((-0.29, -0.24), (0.29, -0.24), (-0.29, 0.24), (0.29, 0.24)):
    ps.append(part((0.56, 0.3, 0.46), (x, 0.29, z), "#c8202a"))
for (x, z) in ((-0.29, 0.0), (0.29, 0.0)):
    ps.append(part((0.56, 0.3, 0.46), (x, 0.59, z), "#d82a30"))
ps.append(part((0.56, 0.3, 0.46), (0, 0.89, 0), "#c8202a"))
for y in (0.29, 0.59, 0.89):
    ps.append(part((1.14 if y < 0.8 else 0.5, 0.08, 0.012), (0, y, 0.475 if y < 0.5 else 0.235), "white"))
ps += [part((0.03, 0.9, 0.03), (0.5, 0.6, 0.42), "darkgrey"), part((0.4, 0.3, 0.02), (0.5, 1.2, 0.42), "#ffe04a"),
       part((0.3, 0.1, 0.012), (0.5, 1.22, 0.432), "#c02828")]
add("Promo Stack", "A pyramid of soda cases on a pallet with a yellow offer sign. Waist-high cover.", ps,
    collider=[{"size": [1.2, 1.04, 1.0]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10},
    breakable={"health": 90, "breakBy": "all", "sound": "soft", "debris": {"count": 18, "size": [0.06, 0.3], "speed": 4, "colors": ["#c8202a", "white", "#c8a070"]}})

# ---------------------------------------------------------------- checkout counter (along X; customers at +Z, cashier at -Z)
ps = [part((3.4, 0.86, 0.8), (0, 0.43, 0), "#3a3f46", material="metal"), part((3.42, 0.04, 0.82), (0, 0.88, 0), "#c9ccd0"),
      part((2.0, 0.03, 0.5), (-0.55, 0.915, 0.05), "rubber"), part((0.4, 0.05, 0.05), (-0.2, 0.955, 0.05), "#e8e8e8"),
      part((0.35, 0.02, 0.35), (0.75, 0.91, 0.05), "#203040"),
      part((0.3, 0.012, 0.03), (0.75, 0.925, 0.05), "#ff3030", anim={"type": "blink", "period": 1.0, "duty": 1.0, "color": "#ff4040", "off": "#401010", "glow": 1.0, "freezeDistance": 40}),
      part((0.42, 0.12, 0.36), (1.32, 0.96, -0.18), "#2a2e34"), part((0.38, 0.26, 0.04), (1.32, 1.17, -0.27), "#2a5070", rotation=[-15, 0, 0]),
      part((0.05, 0.25, 0.05), (0.98, 1.02, 0.28), "#2a2e34"), part((0.12, 0.16, 0.06), (0.98, 1.2, 0.28), "#202428"),
      part((0.6, 1.2, 0.22), (-1.35, 0.6, 0.51), "#2a2e34")]
for k, y in enumerate((0.35, 0.65, 0.95)):
    for j, x in enumerate((-1.5, -1.35, -1.2)):
        ps.append(part((0.13, 0.12, 0.03), (x, y, 0.635), ["#e83a2a", "#f0c020", "#2a8ad8", "#e86ab0", "#40b040", "#ff8a20", "#8a3ac0", "#e8e8e8", "#2a2a2a"][k * 3 + j]))
ps += [part((0.07, 2.3, 0.07), (1.62, 1.15, -0.32), "#9aa0a6"), part((0.3, 0.3, 0.3), (1.62, 2.45, -0.32), "#1d3f8a"),
       part((0.2, 0.2, 0.012), (1.62, 2.45, -0.164), "white"), part((0.2, 0.2, 0.012), (1.62, 2.45, -0.476), "white"),
       part((0.18, 0.08, 0.18), (1.62, 2.64, -0.32), "#40ff70", anim={"type": "blink", "period": 9.0, "duty": 0.92, "color": "#50ff80", "off": "#103a18", "glow": 1.2, "freezeDistance": 60})]
add("Checkout Counter", "A supermarket checkout: conveyor belt, scanner, register and card reader, a sweets rack at the shoppers' end (+Z side) and a lit lane number on a pole. Solid, unbreakable.",
    ps, collider=[{"size": [3.4, 0.92, 0.8]}, {"size": [0.6, 1.2, 0.22], "offset": [-1.35, 0.6, 0.51]}, {"size": [0.1, 2.3, 0.1], "offset": [1.62, 1.15, -0.32]}],
    scatter=SQUARE)

# ---------------------------------------------------------------- shopping carts: a nest of five, and a cart corral


def cart(x, y=0.0, w=0.56, h=0.42, red=True, k=0):
    """A simplified cart along +X (handle at -X), nested carts shrink by a few millimetres so faces never coincide."""
    e = k * 0.006
    return [part((0.9, h - e, 0.02), (x, y + 0.75, w / 2 - e), "#8e959c"), part((0.9, h - e, 0.02), (x, y + 0.75, -w / 2 + e), "#8e959c"),
            part((0.9, 0.03, 0.03), (x, y + 0.96 - e, w / 2 - e), "chrome"), part((0.9, 0.03, 0.03), (x, y + 0.96 - e, -w / 2 + e), "chrome"),
            part((0.03, 0.5, 0.03), (x - 0.4, y + 0.3, w / 2 - 0.05 - e), "#6a6e74"), part((0.03, 0.5, 0.03), (x - 0.4, y + 0.3, -w / 2 + 0.05 + e), "#6a6e74"),
            part((0.88, 0.02, w - 2 * e), (x, y + 0.55 + e, 0), "#a8aeb4"), part((0.02, h - e, w - 2 * e), (x + 0.45, y + 0.75, 0), "#b8bec4"),
            part((0.05, 0.05, w + 0.02 - 2 * e), (x - 0.5, y + 1.0 + e, 0), "#d02828" if red else "#2a5ab0"),
            part((0.75, 0.03, 0.03), (x, y + 0.12 + e, w / 2 - 0.06), "#6a6e74"), part((0.75, 0.03, 0.03), (x, y + 0.12 + e, -w / 2 + 0.06), "#6a6e74"),
            part((0.06, 0.1, w - 0.1), (x + 0.36, y + 0.05, 0), "rubber")]


ps = []
for k in range(5):
    ps += cart(-0.6 + k * 0.3, k=k)
add("Shopping Cart Nest", "Five shopping carts pushed into each other in a row. Solid, unbreakable.", ps,
    collider=[{"size": [2.25, 1.05, 0.62], "offset": [0.05, 0.525, 0]}], scatter=SQUARE)

ps = []
for x in (-1.75, 1.75):
    for z in (-0.68, 0.68):
        ps.append(part((0.06, 1.1, 0.06), (x, 0.55, z), "galvanized"))
for y in (0.5, 1.05):
    for z in (-0.68, 0.68):
        ps.append(part((3.56, 0.05, 0.05), (0, y, z), "galvanized"))
ps.append(part((0.05, 0.05, 1.36), (-1.75, 1.05, 0), "galvanized"))
ps += [part((0.08, 2.4, 0.08), (-1.9, 1.2, 0), "galvanized"), part((0.9, 0.55, 0.04), (-1.9, 2.25, 0), "#1f4fa0"),
       part((0.7, 0.08, 0.012), (-1.9, 2.36, 0.026), "white"), part((0.5, 0.08, 0.012), (-1.9, 2.18, 0.026), "white")]
for k in range(3):
    ps += cart(-0.7 + k * 0.3, k=k, red=False)
add("Cart Corral", "A car park cart return: galvanised rails with three nested carts in it and a blue CART RETURN sign. Solid, unbreakable.", ps,
    collider=[{"size": [3.6, 1.15, 1.42]}, {"size": [0.12, 2.4, 0.12], "offset": [-1.9, 1.2, 0]}], scatter=SQUARE)

ps = []
for k in range(3):
    y = k * 0.11
    ps += [part((0.5, 0.12, 0.34), (0, y + 0.06, 0), "#d02828"), part((0.46, 0.02, 0.3), (0, y + 0.115, 0), "#a81e1e")]
ps += [part((0.02, 0.08, 0.26), (0.12, 0.4, 0), "black", rotation=[0, 0, 20]), part((0.02, 0.08, 0.26), (-0.12, 0.4, 0), "black", rotation=[0, 0, -20])]
add("Shopping Basket Stack", "A stack of red plastic shopping baskets.", ps, collision="cosmetic", scatter={"randomYaw": True}, fade=40)

# ---------------------------------------------------------------- stock room: pallets, bales, racks, baler, roll cage, pallet jack


def pallet_load(x, z, y, w, d, h, color, label="white", band="#e8ecef"):
    return [part((w, 0.14, d), (x, y + 0.07, z), "pallet", material="wood"),
            part((w * 0.48, h / 2, d * 0.48), (x - w * 0.25, y + 0.14 + h / 4, z - d * 0.25), color),
            part((w * 0.48, h / 2, d * 0.48), (x + w * 0.25, y + 0.14 + h / 4, z - d * 0.25), color),
            part((w * 0.48, h / 2, d * 0.48), (x - w * 0.25, y + 0.14 + h / 4, z + d * 0.25), color),
            part((w * 0.48, h / 2, d * 0.48), (x + w * 0.25, y + 0.14 + h / 4, z + d * 0.25), color),
            part((w * 0.96, h / 2 - 0.02, d * 0.96), (x, y + 0.14 + h * 0.75, z), color),
            part((w * 0.98, 0.05, d * 0.98), (x, y + 0.14 + h * 0.52, z), band),
            part((0.2, 0.14, 0.012), (x + w * 0.2, y + 0.14 + h * 0.75, z + d * 0.482), label)]


add("Pallet of Goods", "A pallet stacked with strapped cases of stock, chest high.", pallet_load(0, 0, 0, 1.2, 1.0, 1.4, "cardboard"),
    collider=[{"size": [1.2, 1.54, 1.0]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8},
    breakable={"health": 100, "breakBy": "all", "sound": "soft", "debris": {"count": 18, "size": [0.06, 0.35], "speed": 3.5, "colors": ["#c8a070", "#a8865a", "white"]}})
add("Pallet of Water", "A pallet of shrink-wrapped bottled water packs.", pallet_load(0, 0, 0, 1.2, 1.0, 1.2, "#5a9ad8", label="#1f4fa0", band="#dff0ff"),
    collider=[{"size": [1.2, 1.34, 1.0]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 8},
    breakable={"health": 100, "breakBy": "all", "sound": "soft", "debris": {"count": 18, "size": [0.06, 0.3], "speed": 3.5, "colors": ["#5a9ad8", "#dff0ff", "#c8a070"]}})

add("Store Cardboard Bale", "A compressed bale of flattened cardboard tied with black straps, ready for collection.",
    [part((1.1, 0.85, 0.75), (0, 0.425, 0), "cardboard"), part((1.12, 0.03, 0.77), (0, 0.2, 0), "#8a6a40"),
     part((1.12, 0.03, 0.77), (0, 0.45, 0), "#9a7a4a"), part((1.12, 0.03, 0.77), (0, 0.7, 0), "#8a6a40"),
     part((0.03, 0.87, 0.78), (-0.35, 0.435, 0), "black"), part((0.03, 0.87, 0.78), (0.0, 0.435, 0), "black"),
     part((0.03, 0.87, 0.78), (0.35, 0.435, 0), "black"), part((0.3, 0.2, 0.012), (0.2, 0.55, 0.381), "#d8d0b8")],
    collider=[{"size": [1.1, 0.85, 0.75]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 10},
    breakable={"health": 120, "breakBy": "all", "sound": "soft", "debris": {"count": 16, "size": [0.08, 0.4], "speed": 3.0, "colors": ["#c8a070", "#9a7a4a"]}})

ps = []
for x in (-1.36, 1.36):
    for z in (-0.5, 0.5):
        ps.append(part((0.08, 3.6, 0.08), (x, 1.8, z), "#2a4f9a", material="metal"))
for y in (1.3, 2.6):
    for z in (-0.5, 0.5):
        ps.append(part((2.8, 0.12, 0.06), (0, y, z), "#e06a1a"))
    ps.append(part((2.64, 0.02, 0.96), (0, y + 0.07, 0), "#8a8f94"))
for k, y in enumerate((0.0, 1.38, 2.68)):
    for j, x in enumerate((-0.68, 0.68)):
        if k == 2 and j == 1:
            continue
        h = [0.95, 0.95, 0.7][k]
        ps += [part((1.2, 0.14, 0.95), (x, y + 0.07, 0), "pallet"), part((1.1, h, 0.88), (x, y + 0.14 + h / 2, 0), ["cardboard", "#5a9ad8", "cardboard", "#d8d0c0"][(k + j) % 4]),
               part((0.24, 0.16, 0.012), (x + 0.3, y + 0.14 + h * 0.6, 0.445), "white")]
add("Pallet Rack", "A tall stock room pallet rack, blue uprights and orange beams, three levels of pallets. Solid, unbreakable.",
    ps, collider=[{"size": [2.8, 3.6, 1.1]}], scatter=SQUARE)

ps = [part((1.6, 2.6, 1.1), (0, 1.3, 0), "#7d8a96", material="metal"), part((1.3, 1.1, 0.04), (0, 0.85, 0.57), "#e8c020"),
      part((1.3, 0.9, 0.04), (0, 1.95, 0.57), "#6a7682"), part((0.8, 0.4, 0.6), (0, 2.8, 0), "#3a3f46"),
      part((0.25, 0.35, 0.15), (0.92, 1.4, 0.3), "#9aa0a6"), part((0.08, 0.08, 0.04), (0.92, 1.45, 0.39), "#e02020"),
      part((0.08, 0.08, 0.04), (0.92, 1.33, 0.39), "#20b040"), part((1.3, 0.06, 0.012), (0, 1.43, 0.592), "black"),
      part((0.5, 0.25, 0.012), (0, 2.1, 0.592), "#f0f0f0")]
for k in range(5):
    ps.append(part((0.2, 0.06, 0.012), (-0.5 + k * 0.25, 1.43, 0.6), "#e8c020", rotation=[0, 0, 40]))
add("Cardboard Baler", "A vertical cardboard baler with a yellow loading door and a control box. Solid, unbreakable.", ps,
    collider=[{"size": [1.6, 2.6, 1.1]}], scatter=SQUARE)

ps = [part((0.8, 0.12, 0.7), (0, 0.14, 0), "#8a8f94")]
for x in (-0.33, 0.33):
    for z in (-0.28, 0.28):
        ps.append(part((0.08, 0.1, 0.08), (x, 0.05, z), "black"))
        ps.append(part((0.03, 1.55, 0.03), (x * 1.15, 0.97, z * 1.18), "#9aa0a6"))
for y in (0.6, 1.1, 1.65):
    ps.append(part((0.78, 0.03, 0.03), (0, y, 0.33), "#9aa0a6"))
    ps.append(part((0.78, 0.03, 0.03), (0, y, -0.33), "#9aa0a6"))
    ps.append(part((0.03, 0.03, 0.66), (0.38, y, 0), "#9aa0a6"))
ps += [part((0.7, 0.4, 0.58), (0, 0.4, 0), "cardboard"), part((0.6, 0.36, 0.5), (-0.03, 0.78, 0.02), "#d8d0c0"),
       part((0.66, 0.3, 0.55), (0.02, 1.13, 0), "cardboard")]
add("Roll Cage", "A wheeled steel roll cage of stock waiting to go out on the shop floor.", ps,
    collider=[{"size": [0.8, 1.7, 0.7]}], scatter={"randomYaw": False, "yawStep": 90, "yawJitter": 20},
    breakable={"health": 150, "breakBy": "all", "sound": "metal", "debris": {"count": 14, "size": [0.05, 0.3], "speed": 3.5, "colors": ["#9aa0a6", "#c8a070"]}})

add("Pallet Jack", "A red hand pallet truck left on the floor, forks down.",
    [part((1.15, 0.08, 0.16), (0.1, 0.04, 0.2), "#d62d20"), part((1.15, 0.08, 0.16), (0.1, 0.04, -0.2), "#d62d20"),
     part((0.3, 0.35, 0.55), (-0.6, 0.2, 0), "#d62d20"), part((0.05, 1.1, 0.05), (-0.8, 0.85, 0), "#2a2e34", rotation=[0, 0, 18]),
     part((0.05, 0.05, 0.32), (-0.97, 1.37, 0), "black"), part((0.12, 0.12, 0.12), (-0.6, 0.06, 0.2), "black"),
     part((0.12, 0.12, 0.12), (-0.6, 0.06, -0.2), "black")],
    collision="cosmetic", scatter={"randomYaw": True}, fade=60)

# ---------------------------------------------------------------- signs and lights (hung from the ceiling: top of the model at the ceiling)


def hanging_sign(board, accent, text_widths, num=True):
    """A sign board hung on two wires (the board swings gently in the air conditioning; everything follows it). The
    model's top (the wire tops) is 1.75 m up: place it with y = ceiling height - 1.75."""
    swing = {"type": "swing", "axis": "x", "pivot": [0, 1.75, 0], "angle": 2.5, "period": 5.0, "wind": 0.2, "jitter": 0.3, "freezeDistance": 40}
    ps = [part((1.7, 0.55, 0.05), (0, 0.28, 0), board, anim=swing)]
    follow = {"type": "follow", "parent": 1}
    ps += [part((0.015, 1.2, 0.015), (0.7, 1.15, 0), "#2a2a2a", anim=dict(follow)), part((0.015, 1.2, 0.015), (-0.7, 1.15, 0), "#2a2a2a", anim=dict(follow))]
    x0 = -0.35 if num else -0.7
    if num:
        ps.append(part((0.36, 0.36, 0.07), (-0.6, 0.28, 0), accent, anim=dict(follow)))
        ps.append(part((0.06, 0.22, 0.075), (-0.6, 0.28, 0), "#1a1a1a", anim=dict(follow)))
    for k, w in enumerate(text_widths):
        y = 0.42 - k * 0.14
        ps.append(part((w, 0.07, 0.06), (x0 + w / 2, y, 0), "white", anim=dict(follow)))
    return ps


add("Hanging Aisle Sign", "A blue aisle sign with a yellow number, hung from the ceiling on two wires (place its top at the ceiling: y = ceiling - 1.75).",
    hanging_sign("#1f4fa0", "#f2d23a", [0.9, 0.7, 0.8]), collision="cosmetic", scatter={"randomYaw": False}, fade=90)
for dept, board, widths in (("Produce", "#2a8a3a", [1.1, 0.8]), ("Bakery", "#b8641e", [1.0, 0.7]), ("Deli", "#b8202a", [0.7, 0.9]),
                            ("Frozen", "#1f8ab8", [1.0, 0.6]), ("Dairy", "#3a6ad0", [0.8, 1.0]), ("Checkout", "#6a2a9a", [1.2, 0.5])):
    add(f"Department Sign {dept}", f"A {dept.lower()} department sign hung from the ceiling on two wires (place its top at the ceiling: y = ceiling - 1.75).",
        hanging_sign(board, "white", widths, num=False), collision="cosmetic", scatter={"randomYaw": False}, fade=90)

LIGHT = [part((1.3, 0.08, 0.36), (0, 0.06, 0), "#d8dadc"), part((0.012, 0.9, 0.012), (0.55, 0.55, 0), "#3a3a3a"),
         part((0.012, 0.9, 0.012), (-0.55, 0.55, 0), "#3a3a3a")]
add("Ceiling Light Panel", "A pendant fluorescent light panel, hung on two rods (its top is 1 m up: place it with y = ceiling - 1).",
    LIGHT + [part((1.24, 0.02, 0.3), (0, 0.01, 0), "#fbfcff")], collision="cosmetic", scatter={"randomYaw": False}, fade=120)
add("Flickering Ceiling Light", "A pendant fluorescent light panel with a dying tube that keeps flickering (its top is 1 m up: y = ceiling - 1).",
    LIGHT + [part((1.24, 0.02, 0.3), (0, 0.01, 0), "#e8eaf0", anim={"type": "flicker", "rate": 6, "duty": 0.72, "color": "#fbfcff", "off": "#55585e", "glow": 1.4, "freezeDistance": 70})],
    collision="cosmetic", scatter={"randomYaw": False}, fade=120)

# the store's name over the entrance: SLOP MART in lit white letters on a red board (back at -Z, for a wall)
GLYPHS = {
    "S": [(0, 1.2, 0.9, 1.4), (0, 0.6, 0.9, 0.8), (0, 0, 0.9, 0.2), (0, 0.7, 0.2, 1.3), (0.7, 0.1, 0.9, 0.7)],
    "L": [(0, 0, 0.2, 1.4), (0.2, 0, 0.9, 0.2)],
    "O": [(0, 0, 0.2, 1.4), (0.7, 0, 0.9, 1.4), (0.2, 1.2, 0.7, 1.4), (0.2, 0, 0.7, 0.2)],
    "P": [(0, 0, 0.2, 1.4), (0.2, 1.2, 0.9, 1.4), (0.2, 0.6, 0.9, 0.8), (0.7, 0.8, 0.9, 1.2)],
    "M": [(0, 0, 0.2, 1.4), (0.7, 0, 0.9, 1.4), (0.2, 1.2, 0.7, 1.4), (0.35, 0.6, 0.55, 1.2)],
    "A": [(0, 0, 0.2, 1.4), (0.7, 0, 0.9, 1.4), (0.2, 1.2, 0.7, 1.4), (0.2, 0.6, 0.7, 0.8)],
    "R": [(0, 0, 0.2, 1.4), (0.2, 1.2, 0.9, 1.4), (0.2, 0.6, 0.9, 0.8), (0.7, 0.8, 0.9, 1.2), (0.65, 0, 0.85, 0.6)],
    "T": [(0, 1.2, 0.9, 1.4), (0.35, 0, 0.55, 1.2)],
}
lit = {"type": "blink", "period": 1.0, "duty": 1.0, "color": "#ffffff", "off": "#806060", "glow": 0.8, "freezeDistance": 200}
ps = [part((10.4, 2.2, 0.2), (0, 1.1, 0), "#c4161c"), part((10.5, 0.1, 0.24), (0, 2.25, 0), "#8a0e12"), part((10.5, 0.1, 0.24), (0, 0.05, 0), "#8a0e12"),
      part((8.6, 0.12, 0.05), (0, 0.24, 0.12), "#ffd23a")]
x = -9.55 / 2
for ch in "SLOP MART":
    if ch == " ":
        x += 0.6
        continue
    for (a, b, c, d) in GLYPHS[ch]:
        ps.append(part((c - a, d - b, 0.06), (x + (a + c) / 2, 0.4 + (b + d) / 2, 0.13), "#f4f4f4", anim=dict(lit)))
    x += 0.9 + 0.25
add("Slop Mart Sign", "The supermarket's name board, SLOP MART in lit white letters on red, 10.4 m wide (mount it on a wall: back at -Z, lift it with y).",
    ps, collision="cosmetic", scatter={"randomYaw": False}, fade=320)

# ---------------------------------------------------------------- small things
add("Wet Floor Sign", "A yellow folding wet floor sign.",
    [part((0.3, 0.62, 0.02), (0, 0.3, 0.09), "#f2d23a", rotation=[-14, 0, 0]), part((0.3, 0.62, 0.02), (0, 0.3, -0.09), "#f2d23a", rotation=[14, 0, 0]),
     part((0.16, 0.16, 0.012), (0, 0.36, 0.17), "black", rotation=[-14, 0, 0]), part((0.06, 0.04, 0.2), (0, 0.6, 0), "#f2d23a")],
    collision="cosmetic", scatter={"randomYaw": True}, fade=45,
    breakable={"health": 10, "breakBy": "all", "sound": "soft", "debris": {"count": 6, "size": [0.05, 0.15], "speed": 3, "colors": ["#f2d23a"]}})
add("Dropped Groceries", "Groceries spilled on the floor: rolling cans, an apple, a cereal box and a milk carton.",
    [part((0.07, 0.12, 0.07), (0.1, 0.035, 0.05), "#b8262a", rotation=[0, 30, 90]), part((0.07, 0.12, 0.07), (-0.15, 0.035, 0.12), "#2e7a3c", rotation=[0, 70, 90]),
     part((0.07, 0.12, 0.07), (0.3, 0.035, -0.12), "#d8c23a", rotation=[0, 10, 90]), part((0.08, 0.08, 0.08), (-0.3, 0.04, -0.1), "#c8202a"),
     part((0.2, 0.06, 0.28), (0.05, 0.03, -0.25), "#e8b81e", rotation=[0, 25, 0]), part((0.08, 0.2, 0.08), (-0.05, 0.04, 0.3), "white", rotation=[0, 80, 90])],
    collision="cosmetic", scatter={"randomYaw": True}, fade=35,
    breakable={"health": 5, "breakBy": "all", "sound": "soft", "debris": {"count": 6, "size": [0.04, 0.12], "speed": 3}})
add("Gumball Machine", "A red gumball machine by the entrance, its glass globe full of sweets.",
    [part((0.32, 0.62, 0.32), (0, 0.31, 0), "#c4161c"), part((0.4, 0.05, 0.4), (0, 0.64, 0), "#8a0e12"),
     part((0.36, 0.36, 0.36), (0, 0.85, 0), "#d8eef8"), part((0.36, 0.36, 0.36), (0, 0.85, 0), "#d8eef8", rotation=[0, 45, 0]),
     part((0.08, 0.08, 0.02), (0.06, 0.8, 0.181), "#e83a2a"), part((0.08, 0.08, 0.02), (-0.08, 0.9, 0.181), "#40b040"),
     part((0.08, 0.08, 0.02), (0.1, 0.95, -0.181), "#2a8ad8"), part((0.08, 0.08, 0.02), (0.181, 0.84, 0.0), "#f0c020"),
     part((0.2, 0.08, 0.2), (0, 1.07, 0), "#c4161c"), part((0.12, 0.08, 0.04), (0, 0.45, 0.17), "chrome")],
    collider=[{"size": [0.4, 1.1, 0.4]}], scatter={"randomYaw": True},
    breakable={"health": 20, "breakBy": "all", "sound": "glass", "debris": {"count": 14, "size": [0.03, 0.1], "speed": 4, "colors": ["#e83a2a", "#40b040", "#2a8ad8", "#f0c020", "#d8eef8"]}})

# ---------------------------------------------------------------- write
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT):
    if f.endswith(".json"):
        os.remove(os.path.join(OUT, f))
names = set()
for p in PROPS:
    assert p["name"] not in names, p["name"]
    names.add(p["name"])
    assert len(p["name"]) <= 40, p["name"]
    d = {"format": "call-of-slop-prop/1", "name": p["name"], "category": "Supermarket", "description": p["description"],
         "author": "Call of Slop", "collision": p["collision"]}
    for k in ("fadeDistance", "collider", "scatter", "breakable"):
        if k in p:
            d[k] = p[k]
    d["parts"] = p["parts"]
    assert 1 <= len(d["parts"]) <= 128, (p["name"], len(d["parts"]))
    for q in d["parts"]:
        assert all(0.001 <= s <= 12 for s in q["size"]), (p["name"], q)
    path = os.path.join(OUT, p["name"].replace(" ", "") + ".json")
    with open(path, "w") as f:
        f.write("{\n")
        for k in d:
            if k == "parts":
                f.write('  "parts": [\n' + ",\n".join("    " + json.dumps(q) for q in d["parts"]) + "\n  ]\n")
            else:
                f.write(f'  "{k}": {json.dumps(d[k])},\n')
        f.write("}\n")
print(f"{len(PROPS)} supermarket props -> {OUT}")
