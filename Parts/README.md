# Weapon-builder parts

One JSON file per part in `Parts/<Category>/` (Action, Ammo, Barrel, Feed, Grip, Head, Hilt, MeleeMod, Multiplier,
Muzzle, Optic, Receiver, Splitter, Stock, Upgrade, Utility). The full format, the rules and the design decisions are in
`docs/zombies/WEAPON-BUILDER.md` ("The part file format", "Model building", "Making parts"); the commented starting
point is `_Template/Template.json`. Shared model pieces live in `_Fragments/` (used with `{"use": "Name", ...}`),
templates to `extends` in `_Bases/`, socket types and bores in `_Registry.json`, the 140 standard builds in `Builds/`.

Check a file with `CallOfSlop --check-part FILE` (it prints the boxes, triangles, sockets and where the part fits),
the whole folder with `--check-parts`, and watch it live in the workbench with `--watch-parts`.
`python3 tools/parts/viz.py` draws a contact sheet without the game and `python3 tools/parts/floaters.py` lists any
model piece that floats free of its part or build.

## Model boxes

A part's `"model"` is a list of boxes in the part's own space (metres; -Z towards the muzzle, +Y up). Each box takes
`size` [w, h, l], `pos`, `rot` (degrees about X, Y, Z, applied Z, X, Y), `color`, `alpha`, `tag` and a `shape`:

- `box` (the default), `plate` (a box, by name thin) and `rod` (an octagonal bar along Z, two boxes).
- The **contoured shapes** below. Each is drawn *inside* its box: the box's size is the shape's bounding box, so it
  sits, turns and scales exactly like a box, and everything that measures the gun by its boxes (the optic seat, the
  workbench framing, the cart) keeps working.

## Shapes

| Shape | What it is | Settings (all optional) |
| --- | --- | --- |
| `cylinder` | Round along Z (an N-sided prism, flats on top and bottom; smooth shading from 8 sides) | `segments` (3-32, default 12), `inner` (0-0.95: a bore, as a fraction of the outside), `ends` (`closed` default, `open`, `front`, `back`), `taper` (the front end's size, a number or [x, y] fraction), `shift` (moves the front end, fraction of the size) |
| `tube` | A cylinder with a bore (`inner` 0.7) and open ends (only the rims drawn) | as cylinder; `ends: "back"` closes the back like a cup |
| `wedge` | A box whose front (-Z) face is smaller and/or moved | `taper` (default [1, 0.3]), `shift` ([x, y]; `taper/2 + |shift|` must stay within 0.5, e.g. `"taper": [1, 0.4], "shift": [0, 0.3]` keeps the top flat) |
| `bevel` | A box with its edges cut off flat | `radius` (0.01-0.5 of the smallest side cut into, default 0.25), `edges` (`all` default, or `long`: only the four along Z) |
| `rounded` | A box with rounded edges | `radius`, `edges` as bevel; `segments` (1-8 per corner, default 4) |
| `capsule` | Round along Z with half-round ends | `segments` (default 12) |
| `sphere` | A ball (an ellipsoid filling the box) | `segments` (default 12) |
| `arc` | A curved band: a ring sector in the Y-Z plane, as wide as the box in X, stretched to fill the box's height and length | `angle` (sweep, 5-360 degrees, default 90), `inner` (default 0.6; 0 = a solid pie), `dir` (where its middle points: 0 down, 90 forward, 180 up, 270 back), `segments` (along the curve; default one per 15 degrees) |

A setting a shape doesn't take is an error, as are impossible ones (a wedge's front end leaving its box, fewer than 3
sides). Settings scale with the box, so a fragment used with `"scale"` keeps its shape.

Examples: a trigger guard is an `arc` with `"angle": 180, "inner": 0.75` (a U opening upwards); a curved magazine an
`arc` with a small angle and a large `inner` (`"angle": 25, "inner": 0.8, "dir": 90`); a drum a `cylinder` turned 90
degrees about Y; a suppressor a `tube` with `"ends": "back"` and a bevelled or rounded collar; a stock's comb a `wedge`.

**Cost.** Each distinct shape (shape, settings and size) is made once when the parts load and shared by every part and
build that uses it; it is drawn with one draw call like a box, and nothing is made per frame. Typical shapes are 12 to
150 triangles (a box is 12; a 12-sided cylinder 48, a tube 96-108, a rounded box 100-300). A part may draw at most 6000
triangles in all; `--check-part` prints the count. Keep small pieces (screws, pins, sights) as plain boxes.

**Connectivity.** Every part must be one connected piece, and a contoured shape only counts where it really is: a
tube touches through its wall, an arc along its band, not anywhere in their boxes (the self-test "Model connectivity"
and `tools/parts/floaters.py` use a few boxes inside each shape, `PartShapes.Proxies` / `tools/parts/shapes.py`).

The generator is `Source/Zombies/PartShapes.cs`; `tools/parts/shapes.py` mirrors it for the Python tools (keep them in
step). The self-test "Part shapes" checks every shape stays inside its box, faces outwards, is shared and reaches the
drawn gun.
