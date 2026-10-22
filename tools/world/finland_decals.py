#!/usr/bin/env python3
"""Writes the Finland map's pack-local sign decals (Maps/World/Decals/Finland/*.json + PNG): invented Finnish place,
shop and mine signage in the Finnish style (blue road-name boards with white letters, painted shop fascias, stencilled
mine signs, yellow-black danger boards, green exit signs). Text is drawn with Pillow into a PNG next to each file
(the decal atlas resamples it to 128 x 128, so words are short and bold).

Usage: python3 tools/world/finland_decals.py <repo root>
"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(REPO, "Maps/World/Decals/Finland")
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

# name, file stem, size [w, h] metres, background, text colour, border colour or None, lines, description
SIGNS = [
    ("Honkala Village Sign", "honkala", [2.6, 0.7], "#1f4e9a", "#ffffff", "#ffffff", ["HONKALA"], "A blue Finnish place-name board: HONKALA."),
    ("Kuusela Village Sign", "kuusela", [2.6, 0.7], "#1f4e9a", "#ffffff", "#ffffff", ["KUUSELA"], "A blue Finnish place-name board: KUUSELA."),
    ("Kirkonmaki Village Sign", "kirkonmaki", [3.0, 0.7], "#1f4e9a", "#ffffff", "#ffffff", ["KIRKONMÄKI"], "A blue Finnish place-name board: KIRKONMAKI (church hill)."),
    ("Vuorikoski Mine Sign", "vuorikoski", [3.4, 1.0], "#1f4e9a", "#ffffff", "#ffffff", ["VUORIKOSKI", "KAIVOS"], "A blue board at the mine gate: VUORIKOSKI KAIVOS (mine)."),
    ("Kauppa Fascia", "kauppa", [3.2, 0.66], "#f2efe6", "#1f4e9a", None, ["KAUPPA"], "A village shop's painted fascia: KAUPPA (shop)."),
    ("Headframe Lettering", "headframe", [6.0, 1.6], "#8a8780", "#f2efe6", None, ["VUORIKOSKI"], "Big white letters on the concrete headframe: VUORIKOSKI."),
    ("Rikastamo Sign", "rikastamo", [3.6, 0.7], "#2a2c30", "#f2c818", None, ["RIKASTAMO"], "A dark steel board on the concentrator: RIKASTAMO (concentrator)."),
    ("Toimisto Sign", "toimisto", [2.4, 0.6], "#f2efe6", "#2a2c30", "#2a2c30", ["TOIMISTO"], "A white door board: TOIMISTO (office)."),
    ("Korjaamo Sign", "korjaamo", [2.6, 0.6], "#f2efe6", "#2a2c30", "#2a2c30", ["KORJAAMO"], "A white board over the workshop door: KORJAAMO (repair shop)."),
    ("Vaara Sign", "vaara", [1.2, 0.6], "#f2c818", "#111111", "#111111", ["VAARA"], "A yellow and black danger board: VAARA (danger)."),
    ("Rajahde Sign", "rajahde", [1.6, 0.6], "#c82818", "#ffffff", "#ffffff", ["RÄJÄHDE"], "A red explosives-store board: RAJAHDE (explosive)."),
    ("Hissi Sign", "hissi", [1.4, 0.5], "#f2c818", "#111111", None, ["HISSI"], "A stencilled board by the shaft: HISSI (lift)."),
    ("Ulos Sign", "ulos", [1.4, 0.5], "#1a7a3a", "#ffffff", None, ["ULOS >"], "A green exit board in the drifts: ULOS (out) with an arrow."),
    ("Taso Sign", "taso", [1.6, 0.6], "#f2efe6", "#111111", "#111111", ["TASO 120"], "A white level board in the shaft station: TASO 120 (level 120 m)."),
    ("Sauna Sign", "sauna", [1.4, 0.45], "#6a4a2e", "#f2e2b8", None, ["SAUNA"], "A carved wooden board over the sauna door: SAUNA."),
    ("Kirkko Sign", "kirkko", [1.6, 0.45], "#f2efe6", "#3a4a5a", None, ["KIRKKO"], "A white board by the church gate: KIRKKO (church)."),
]


def render(path, size, bg, fg, border, lines):
    W = 512
    H = max(64, int(round(W * size[1] / size[0])))
    img = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(img)
    if border:
        b = max(4, H // 14)
        d.rectangle([b, b, W - 1 - b, H - 1 - b], outline=border, width=b)
    pad = H * 0.16 + (H // 14 if border else 0)
    avail_h = (H - 2 * pad) / len(lines)
    for i, text in enumerate(lines):
        fs = int(avail_h * 0.95)
        while fs > 6:
            font = ImageFont.truetype(FONT, fs)
            l, t, r, bt = d.textbbox((0, 0), text, font=font)
            if r - l <= W - 2 * pad and bt - t <= avail_h * 0.9:
                break
            fs -= 1
        l, t, r, bt = d.textbbox((0, 0), text, font=font)
        x = (W - (r - l)) / 2 - l
        y = pad + i * avail_h + (avail_h - (bt - t)) / 2 - t
        d.text((x, y), text, font=font, fill=fg)
    img.convert("RGB").save(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, stem, size, bg, fg, border, lines, desc in SIGNS:
        png = stem + ".png"
        render(os.path.join(OUT, png), size, bg, fg, border, lines)
        d = {"format": "call-of-slop-decal/1", "name": name, "description": desc, "author": "Call of Slop", "size": size,
             "rotation": 0, "surfaces": "wall", "fadeDistance": 110, "blend": "alpha", "image": png}
        with open(os.path.join(OUT, stem + ".json"), "w") as f:
            f.write(json.dumps(d, indent=2) + "\n")
    print(f"{len(SIGNS)} decals written to {OUT}")


if __name__ == "__main__":
    main()
