#!/usr/bin/env python3
"""Pixel lettering and pixel-art logos built from prop parts (boxes), for shop signs.

A 5 x 7 pixel font (capitals, digits and a few marks) and small pixel-art emblems are turned into as few boxes as
possible (greedy rectangles), so a whole shop fascia with its name and logo fits in one prop (at most 128 parts).
Used by tools/world/mall_props.py. All text is plain ASCII.
"""

FONT = {
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "B": ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
    "C": [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
    "D": ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
    "E": ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
    "F": ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
    "G": [".####", "#....", "#....", "#.###", "#...#", "#...#", ".###."],
    "H": ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "I": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
    "J": ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
    "K": ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
    "L": ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
    "M": ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
    "N": ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
    "O": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "P": ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
    "Q": [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
    "R": ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
    "S": [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
    "T": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    "U": ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "V": ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
    "W": ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
    "X": ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
    "Y": ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
    "Z": ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
    "0": [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
    "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "2": [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
    "3": ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
    "4": ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
    "5": ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
    "6": [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
    "7": ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
    "8": [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
    "9": [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
    "&": [".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#"],
    "'": ["..#..", "..#..", ".#...", ".....", ".....", ".....", "....."],
    "-": [".....", ".....", ".....", ".###.", ".....", ".....", "....."],
    ".": [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
    "!": ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
    "%": ["##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##"],
    "$": ["..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.."],
    " ": [".....", ".....", ".....", ".....", ".....", ".....", "....."],
}
GLYPH_W, GLYPH_H = 5, 7


def rects(grid, key="#"):
    """Greedy rectangle cover of the cells equal to key in a list of strings (row 0 at the top). Returns (col, row, w, h)."""
    rows = [list(r) for r in grid]
    H = len(rows)
    W = max(len(r) for r in rows) if rows else 0
    for r in rows:
        r.extend("." * (W - len(r)))
    used = [[False] * W for _ in range(H)]
    out = []
    for y in range(H):
        for x in range(W):
            if rows[y][x] != key or used[y][x]:
                continue
            w = 1
            while x + w < W and rows[y][x + w] == key and not used[y][x + w]:
                w += 1
            h = 1
            while y + h < H and all(rows[y + h][x + k] == key and not used[y + h][x + k] for k in range(w)):
                h += 1
            for yy in range(y, y + h):
                for xx in range(x, x + w):
                    used[yy][xx] = True
            out.append((x, y, w, h))
    return out


def text_grid(text, spacing=1):
    """The text as one pixel grid (7 rows)."""
    rows = [""] * GLYPH_H
    for i, ch in enumerate(text.upper()):
        g = FONT.get(ch, FONT[" "])
        for r in range(GLYPH_H):
            rows[r] += g[r] + ("." * spacing if i < len(text) - 1 else "")
    return rows


def text_width(text, px, spacing=1):
    return (len(text) * (GLYPH_W + spacing) - spacing) * px


def text_parts(text, px, cx, cy, z, depth, color, spacing=1, anim=None):
    """Parts for the text centred on (cx, cy), the letters' face at z (they stand out depth towards +z)."""
    grid = text_grid(text, spacing)
    W = len(grid[0])
    x0 = cx - W * px / 2
    ytop = cy + GLYPH_H * px / 2
    parts = []
    for (c, r, w, h) in rects(grid):
        p = {"size": [round(w * px, 4), round(h * px, 4), round(depth, 4)],
             "offset": [round(x0 + (c + w / 2) * px, 4), round(ytop - (r + h / 2) * px, 4), round(z - depth / 2, 4)],
             "color": color}
        if anim:
            p["anim"] = dict(anim)
        parts.append(p)
    return parts


def art_parts(art, palette, px, cx, cy, z, depth, layer=0.004):
    """Parts for a pixel-art picture: art is a list of strings, palette maps a character to a colour (or to (colour,
    anim)). '.' is empty. Each colour gets its own greedy rectangles; later palette entries stand slightly proud so
    touching colours never flicker."""
    H = len(art)
    W = max(len(r) for r in art)
    x0 = cx - W * px / 2
    ytop = cy + H * px / 2
    parts = []
    for li, (key, val) in enumerate(palette.items()):
        col, anim = (val if isinstance(val, tuple) else (val, None))
        for (c, r, w, h) in rects(art, key):
            d = depth + li * layer
            p = {"size": [round(w * px, 4), round(h * px, 4), round(d, 4)],
                 "offset": [round(x0 + (c + w / 2) * px, 4), round(ytop - (r + h / 2) * px, 4), round(z - depth / 2 + li * layer / 2, 4)],
                 "color": col}
            if anim:
                p["anim"] = dict(anim)
            parts.append(p)
    return parts


# ---------------------------------------------------------------- logos: 9 to 11 pixels square, one character per colour
LOGOS = {
    "bolt": ["....aa.", "...aa..", "..aa...", ".aaaaa.", "...aa..", "..aa...", ".aa....", "aa.....", ],
    "book": ["aaa.bbb", "aaa.bbb", "aaa.bbb", "aaa.bbb", "aaa.bbb", "aaacbbb", ".ccccc."],
    "shoe": [".......", "aa.....", "aab....", "aaabb..", "aaaaabb", "aaaaaaa", "ccccccc"],
    "ball": ["..aaa..", ".abbba.", "abbabba", "aababaa", "abbabba", ".abbba.", "..aaa.."],
    "gem": [".aaaaa.", "abbabba", "aaaaaaa", ".abbba.", "..aba..", "...a..."],
    "candy": ["a..bbb..a", "aa.bcb.aa", "aaabbbaaa", "aa.bcb.aa", "a..bbb..a"],
    "bear": ["aa...aa", "aaaaaaa", "abaaaba", "aaacaaa", ".aaaaa.", "..aaa.."],
    "joystick": ["...a...", "..aaa..", "...b...", "...b...", ".ccccc.", "ccccccc"],
    "lips": [".aa.aa.", "aaaaaaa", "abbbbba", ".aaaaa.", "..aaa.."],
    "phone": [".aaaaa.", ".abbba.", ".abbba.", ".abbba.", ".abbba.", ".aaaaa.", ".aacaa."],
    "paw": ["a.a.a", ".....", "a...a", ".bbb.", "bbbbb", ".bbb."],
    "house": ["...a...", "..aaa..", ".aaaaa.", "aaaaaaa", ".b.c.b.", ".b.c.b.", ".bbcbb."],
    "needle": ["aaaa...", "a..a...", "aaaa...", "...bb..", "....bb.", ".....bb"],
    "star": ["...a...", "...a...", "aaaaaaa", ".aaaaa.", "..aaa..", ".aa.aa.", "aa...aa"],
    "cup": [".a.a...", "..a.a..", "bbbbbb.", "bbbbbbb", "bbbbb.b", ".bbbbb.", "ccccccc"],
    "wok": ["a.a.a..", ".......", "bbbbbbb", "cbbbbbbcc", ".bbbbb.", "..bbb.."],
    "burger": [".aaaaa.", "aaaaaaa", "bbbbbbb", "ccccccc", "aaaaaaa"],
    "pizza": ["aaaaaaa", ".bcbbb.", ".bbbcb.", "..bbb..", "..bc...", "...b..."],
    "monogram": ["a.a...b", "a.a.bbb", "aaa..b.", "a.a..bb", "a.a..b."],
    "film": ["aaaaaaa", "a.a.a.a", "aaaaaaa", "abbbbba", "aaaaaaa", "a.a.a.a", "aaaaaaa"],
    "token": [".aaaaa.", "aabbbaa", "abaaaba", "abaaaba", "abaaaba", "aabbbaa", ".aaaaa."],
    "info": ["..a..", ".....", ".aa..", "..a..", "..a..", ".aaa."],
}


def logo_size(name):
    art = LOGOS[name]
    return max(len(r) for r in art), len(art)
