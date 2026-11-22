# Hull dress: Zero Day detail on production hull pieces (design and state)

Sean, 2026-11-21. The agreed design is the Claude Doc "Detailed hull pieces: design"
(https://claude.ai/artifact/A8vX91cfLzYFq4so1J2FkH); the background, the Bake Oven and the design store are on the
"Zero Day Hull Pieces" page (https://claude.ai/artifact/HLvEvfJwDuEB9vwMZJWcna). In short:

- A formed skin is dressed with subparts (slabs, rails, strips and the extra bits), welded, and baked into one
  `Baked (#hash)` part by the Bake Oven (production.md "Bake Oven and baked parts").
- **Dress surfaces.** Every formed skin has one on each facet; subparts lie flush on it in 5 cm steps, slabs made
  tapered to their spot (the sleek look, not square on the grid).
- **The Dress step is in the first version:** a setting on a skin (a style and a salt) that lays the style's pattern out
  as a sub-blueprint the Building Station builds; a player may change any subpart before baking.
- **Extra bits, first run:** A bolt rows and painted markings, B vents and grilles, D navigation, running and warning
  lights, F outside conduit along the chines. Hatch outlines (C) are left out.
- **Working fittings** (sensor domes, antennas, jet pads) are modules in module sockets. Socket plates are never baked;
  a well socket is cut into the skin before baking. A socket added to a piece already baked makes a **no-collide hole**:
  the baked part's key records it, its collision boxes split round it, and the renderer drops the piece's fragments
  inside it (`Renderer.Cuts.cs`).
- **32 render holes a frame** (Sean), up from 16, shared with the flush shell pods.

## State

| Step | What | State |
| --- | --- | --- |
| 1 | The Zero Day's dressing as a shared library: `Source/Constructs/HullDress.cs` (bands, slabs, strips, chine rails, shoulders, the panel layout) | Done. `TorpedoFrigateParts.cs` calls it; `--gen-parts torpedo_frigate_parts --check` shows no change |
| 2 | Dress styles as data: `Constructs/DressStyles.json` (`DressStyles`), IT from the Zero Day | Done (IT only) |
| 3 | Hull sections: `Hull Section (...)`, a dressed skin made at run time from its key (`HullLoft.Section.cs`), the dress drawn into the part (Sean's choice between dress in the skin's key and separate subparts on facet welds, 2026-11-21: the separate parts would sit on slopes, which a grid-placed part can't, and each facet would cost a freeform weld) | Done; `--hull-section [KEY\|example]` |
| 4 | Production: a machine makes sections (the factory layout is a separate set of agents) | Plain skins: the Panel Former forms them from a 4 cm steel sheet blank (production.md "Plain hull skins"); the dress goes on at a Building Station, so a dressed key is refused at the former |
| 4b | The bake: a welded group into one `Baked (#hash)` part and back, a design store per world (`BakedParts.cs`; production.md "Bake Oven and baked parts") | Done; `--bake-check` |
| 6 | The Dress step (Sean's option B, 2026-11-21): a plain skin (`Hull Section` of style `-`) with dress anchors, `Hull Dress (...)` subparts, the dress blueprint, the Building Station's Dress job (`HullLoft.Dress.cs`, `ConstructStation.Dress.cs`) | Done; `--dress-skin [KEY\|example] [STYLE] [SALT] [EXTRAS]`. The Panel Former makes plain skins (step 4); a cut skin's key is not a plain one |
| 5 | 32 render holes (`Renderer.CutMax`, `uCut[96]`, `CutRun` = first x 64 + count) | Done; the no-collide socket hole waits for the Bake Oven |

The hull's void (the hidden boxes behind the Zero Day's skin) stays in its generator until open item 3 of the design is
settled.

## The dress station

A **plain skin** is a Hull Section of style `-` (salt 0, extras `-`): its facets in bare steel (paintable, `#8f9196`), its
void boxes and `a`/`b`/`i` sockets, and **dress anchors** `d{k}.{a}`: hidden 3 cm pads 6 mm off each facet's outer face, in
two staggered rows (a third and two thirds across), one per 25 cm of length, 2 to 32 a facet (fewer when the facets would
pass the part's 255 sockets). An anchor's socket points along the facet normal's largest axis.

A **dress subpart** `Hull Dress (KIND ARGS [pN] @F.A | <the dressed section's head and profiles>)` is one piece of a
style's pattern: `panel K.I`, `rail I`, `strip`, `run`, `nav`, `warn W`, `chev`, `vent I`, `pipe`. It is drawn in the
skin's frame with the same arithmetic as a dressed Hull Section, so a dressed skin looks as the dressed section does, and
it has one socket, `skin`, on its anchor: fitted, it sits at the skin's own place and turn. A player may take any off or
put another on.

The **dress blueprint** `Dress {style} {salt} {extras}: {skin key}` is made at run time (`HullLoft.DressBlueprint`): step
0 the skin (painted the style's skin colour), then each subpart on the free anchor nearest its middle on its facet (none
free: it is left out with a log line).

The **Dress job** is the Building Station's blueprint `Dress skins` (first in its list). Started, the station waits for a
plain skin lying loose in its bay or held in its hold, makes that skin's dress blueprint from its Dress settings
(`dressStyle`, shown when there are two styles or more; `dressSalt` 0 to 99; `dressExtras` All, No conduit, No lights,
Bolts and vents, None) and builds it, then waits for the next, as many as its build count. Dress subparts are **laid**,
not ordered: an arm lays each from the station's linked steel, a unit per kilogram (free in creative); short, the build
says so.

## Hull sections

`Hull Section (600 f1-4 s it 4 ABDF | 280,-160 530,30 600,330 500,630 260,740 | =)`: length (cm, 50 to 3200), facets
`f<first>-<last>` (facet k runs from point k-1 to k), side (`p` port, `s` starboard), dress style, salt (0 to 99), extras
(some of `ABDF` in that order, or `-`), then profile A (the front end, z 0) and profile B (z = length, `=` for the same):
3 to 8 points `H,Y` in cm, half width and height, keel first. The origin is the centreline at height 0 and the front end,
so sections cut from the same stations line up when placed at their station's z.

A section is the skin's facets (4 cm, odd facets staggered 6 mm), the style's armour panels on each, chine rails at the
corners between its facets (the beam's size at the widest point), a glowing strip over the beam, a hidden void box under
each facet (with sockets `a`/`b` on its ends and `i` inside), and the extras: A bolt rows on the slabs and hazard
chevrons at the front of the lowest facet, B louvred vents, D running lamps along the strip, a red or green light at its
front and amber lamps at the top facet's ends, F a pipe on brackets along the top chine. Extras fill what is left of the
256 boxes in that order (lights, conduit, vents, chevrons, bolts). Strips on a twisted facet stand off by half its twist.
`--hull-section KEY` prints the counts and fails on any coplanar faces.

## The style file

`Constructs/DressStyles.json`, format `call-of-slop-dress-styles/1`. A style has a `palette` (names to `#rrggbb`),
`panel` (`length`: the armour panels' length, m; `gap`) and `patterns`. Each panel on a band takes pattern
`(facet x 7 + panel x 3 + salt x 5 + at) % count`, so both sides of a ship match. A pattern is a list of layers: a
`slab` (`d`: [from, to] m out, `bevel`) or a `strip` (`d`: m out), with `u` (along) and `v` (across) edges, each
`"lo"`, `"hi"`, `"mid"` or `{"from", "frac", "off", "cap"}` (from + min(cap, frac x span) + off). A style's optional
`trim` block sets the rails, strip, bolts, chevrons, vents, lamps and conduit (`DressTrim` in HullDress.cs has every key and
its default; colours are palette names or `#rrggbb`). A bad file logs
`[dress] REFUSED` and leaves no styles; the part kit then refuses to make the Zero Day.
