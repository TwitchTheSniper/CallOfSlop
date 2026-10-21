#!/usr/bin/env python3
"""Writes the pack-local test model of the Dev map pack: Maps/Dev/Models/dev_beacon.glb.

It shows that a map pack can carry its own models (see Maps/README.md, "Where assets go"): only maps in the Dev pack
find it as "Models/dev_beacon.glb". Same builder as make_models.py; deterministic (byte-identical every run).
Usage:  python3 tools/models/make_pack_models.py [output folder, default: Maps/Dev/Models next to the repo root]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_models import Doc, Geo, box, colour, cylinder_strips, lathe  # noqa: E402


def dev_beacon(path):
    """A 1.6 m survey beacon: a dark base, a teal post with magenta bands and a magenta cube on top."""
    d = Doc()
    dark = d.material("Base", colour(0.12, 0.12, 0.14), roughness=0.9)
    teal = d.material("Post", colour(0.10, 0.62, 0.62), roughness=0.6)
    magenta = d.material("Bands", colour(0.86, 0.16, 0.62), roughness=0.5)
    base = Geo()
    lathe(base, cylinder_strips(0.32, 0.26, 0.0, 0.12), 12, lambda y: y)
    post = Geo()
    lathe(post, cylinder_strips(0.07, 0.06, 0.12, 1.3), 10, lambda y: y)
    band = Geo()
    lathe(band, cylinder_strips(0.085, 0.085, -0.05, 0.05), 10, lambda y: y)
    top = Geo()
    box(top, (-0.18, 0.0, -0.18), (0.18, 0.3, 0.18))
    m_body = d.mesh("BeaconBody", [(base, dark), (post, teal)])
    m_band = d.mesh("Band", [(band, magenta)])
    m_top = d.mesh("Top", [(top, magenta)])
    body = d.node("Body", m_body)
    b1 = d.node("BandLow", m_band, translation=[0.0, 0.5, 0.0])
    b2 = d.node("BandHigh", m_band, translation=[0.0, 0.95, 0.0])
    t = d.node("Top", m_top, translation=[0.0, 1.3, 0.0])
    d.node("DevBeacon", children=[body, b1, b2, t])
    d.write_glb(path)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "Maps", "Dev", "Models")
    os.makedirs(out, exist_ok=True)
    dev_beacon(os.path.join(out, "dev_beacon.glb"))
    print("dev_beacon.glb", os.path.getsize(os.path.join(out, "dev_beacon.glb")), "bytes")


if __name__ == "__main__":
    main()
