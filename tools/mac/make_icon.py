"""Draws the Mac app icon, tools/mac/AppIcon.png (1024 x 1024): a blocky crosshair made of boxes, like the game.

Run: python3 -I tools/mac/make_icon.py   (needs Pillow). The release workflow's Mac job turns the PNG into
CallOfSlop.app/Contents/Resources/AppIcon.icns with sips + iconutil (tools/mac/package_mac.sh).
"""
import os
import sys

from PIL import Image, ImageDraw

S = 1024
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "AppIcon.png")


def main() -> int:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # macOS icon grid: an 824 px rounded square centred on the canvas.
    m = 100
    d.rounded_rectangle([m, m, S - m, S - m], radius=185, fill=(28, 32, 40, 255))
    d.rounded_rectangle([m + 18, m + 18, S - m - 18, S - m - 18], radius=170, outline=(58, 66, 80, 255), width=10)
    c = S // 2
    orange, shade, light = (240, 128, 32, 255), (176, 84, 18, 255), (255, 186, 110, 255)

    def box(x0, y0, x1, y1):
        # A box with a lit top edge and a shaded right/bottom edge, as the game draws them.
        d.rectangle([x0, y0, x1, y1], fill=orange)
        d.rectangle([x0, y0, x1, y0 + 14], fill=light)
        d.rectangle([x1 - 14, y0, x1, y1], fill=shade)
        d.rectangle([x0, y1 - 14, x1, y1], fill=shade)

    arm, thick, gap = 250, 70, 70
    box(c - thick // 2, c - gap - arm, c + thick // 2, c - gap)  # top
    box(c - thick // 2, c + gap, c + thick // 2, c + gap + arm)  # bottom
    box(c - gap - arm, c - thick // 2, c - gap, c + thick // 2)  # left
    box(c + gap, c - thick // 2, c + gap + arm, c + thick // 2)  # right
    box(c - 26, c - 26, c + 26, c + 26)  # the centre dot
    img.save(OUT)
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
