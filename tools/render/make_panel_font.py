#!/usr/bin/env python3
"""Writes Source/Render/PanelFontData.cs: the display panels' font as a signed distance field.

docs/sandbox/CONSTRUCTS.md, "Display panels". Each printable ASCII character of DejaVu Sans Mono Bold is drawn large, its distance
to the outline worked out, and the result kept small: a cell of CELL_W by CELL_H texels, 16 cells by 6, one byte a texel, 128 on the
outline. A field keeps its edges sharp at any size with one texture read. A cell is the layout's 6 by 8 box plus a clear margin wider
than the field, and a glyph's quad covers the whole cell, so nothing past the box is cut off (descenders reach into the margin), and
every cell's edge is 0, so a big glyph can't pick up its neighbour in the atlas (that was the stray lines of the old bitmap font).

DejaVu's licence (Bitstream Vera, free to embed and change) is in NOTICE.txt. The game holds this output, so it needs no font file.
Run it from the repo root: python3 -I tools/render/make_panel_font.py (needs Pillow, numpy and scipy; --check only compares). Fix this, not its output.
"""

import base64
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import distance_transform_edt

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"
OUT = os.path.join("Source", "Render", "PanelFontData.cs")
BOX_W, BOX_H = 30, 40  # the texels of the layout's 6 by 8 box
MARGIN = 9  # clear texels round the box in each cell
CELL_W, CELL_H = BOX_W + 2 * MARGIN, BOX_H + 2 * MARGIN
EM = 42  # texels: capitals about 3/4 of the box, a character's advance 5/6 of its width, like the old font
BASELINE = 5  # texels above the box's bottom
SCALE = 8  # drawn this much bigger
SPREAD = 3.0  # texels of distance a byte covers either side of the outline
COLS, ROWS = 16, 6


def glyph(font, ch):
    w, h = CELL_W * SCALE, CELL_H * SCALE
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    x = (w - font.getlength(ch)) / 2
    y = h - (MARGIN + BASELINE) * SCALE
    d.text((x, y), ch, 255, font=font, anchor="ls")
    ink = np.asarray(im) >= 128
    if not ink.any():
        return np.zeros((CELL_H, CELL_W), np.uint8)  # (a space)
    inside = distance_transform_edt(ink)
    outside = distance_transform_edt(~ink)
    field = (outside - inside) / SCALE  # texels, plus outside
    small = field.reshape(CELL_H, SCALE, CELL_W, SCALE).mean(axis=(1, 3))
    return np.clip(128 - small / SPREAD * 127, 0, 255).astype(np.uint8)


def atlas():
    font = ImageFont.truetype(FONT, EM * SCALE)
    a = np.zeros((ROWS * CELL_H, COLS * CELL_W), np.uint8)
    for g in range(95):
        c = glyph(font, chr(32 + g))
        edge = max(c[0].max(), c[-1].max(), c[:, 0].max(), c[:, -1].max())
        if edge != 0:
            raise SystemExit(f"{chr(32 + g)!r} reaches its cell's edge ({edge}): more margin")
        x, y = g % COLS * CELL_W, g // COLS * CELL_H
        a[y:y + CELL_H, x:x + CELL_W] = c
    return a


def main():
    a = atlas()
    text = base64.b64encode(zlib_free(a)).decode()
    lines = [text[i:i + 150] for i in range(0, len(text), 150)]
    body = "\n".join(f'        "{ln}" +' for ln in lines)
    src = f"""namespace CallOfSlop;

/// <summary>The display panels' font, a signed distance field of DejaVu Sans Mono Bold (see NOTICE.txt). Written by tools/render/make_panel_font.py: fix it, not this file.</summary>
public static class PanelFontData
{{
    /// <summary>Texels of a cell, of the margin on each side of it, and of the 6 by 8 box the layout places: a glyph's quad grows by Margin / BoxW and Margin / BoxH.</summary>
    public const int CellW = {CELL_W}, CellH = {CELL_H}, Margin = {MARGIN}, BoxW = {BOX_W}, BoxH = {BOX_H}, Columns = {COLS}, Rows = {ROWS};

    /// <summary>The atlas, one byte a texel, packed by runs (a count, then a value) and in base 64.</summary>
    public const string Atlas =
{body}
        "";
}}
"""
    if "--check" in sys.argv:
        same = os.path.exists(OUT) and open(OUT, encoding="utf-8").read() == src
        print("up to date" if same else f"out of date: {OUT}")
        return 0 if same else 1
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(src)
    print(f"{OUT}: {a.shape[1]} x {a.shape[0]}, {len(text)} characters")
    return 0


def zlib_free(a):
    """Runs of (count, value): the field is mostly one value, and this leaves nothing for the game to link."""
    out = bytearray()
    flat = a.ravel()
    i = 0
    while i < len(flat):
        v = flat[i]
        n = 1
        while i + n < len(flat) and flat[i + n] == v and n < 255:
            n += 1
        out += bytes((n, v))
        i += n
    return bytes(out)


if __name__ == "__main__":
    sys.exit(main())
