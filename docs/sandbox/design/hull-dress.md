# Hull dress: Zero Day detail on production hull pieces (design and state)

Sean, 2026-11-21. The agreed design is the Claude Doc "Detailed hull pieces: design"
(https://claude.ai/artifact/A8vX91cfLzYFq4so1J2FkH); the background, the Bake Oven and the design store are on the
"Zero Day Hull Pieces" page (https://claude.ai/artifact/HLvEvfJwDuEB9vwMZJWcna). In short:

- A formed skin is dressed with subparts (slabs, rails, strips and the extra bits), welded, and baked into one
  `Baked (#hash)` part by the Bake Oven (built in its own thread).
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
| 3 | Dress surfaces on formed skins; subpart keys for slabs, rails, strips, bolts, vents, lights, conduit | Next |
| 4 | The Dress setting on a skin: lays the pattern out as a sub-blueprint | Next |
| 5 | 32 render holes (`Renderer.CutMax`, `uCut[96]`, `CutRun` = first x 64 + count) | Done; the no-collide socket hole waits for the Bake Oven |

The hull's void (the hidden boxes behind the Zero Day's skin) stays in its generator until open item 3 of the design is
settled.

## The style file

`Constructs/DressStyles.json`, format `call-of-slop-dress-styles/1`. A style has a `palette` (names to `#rrggbb`),
`panel` (`length`: the armour panels' length, m; `gap`) and `patterns`. Each panel on a band takes pattern
`(facet x 7 + panel x 3 + salt x 5 + at) % count`, so both sides of a ship match. A pattern is a list of layers: a
`slab` (`d`: [from, to] m out, `bevel`) or a `strip` (`d`: m out), with `u` (along) and `v` (across) edges, each
`"lo"`, `"hi"`, `"mid"` or `{"from", "frac", "off", "cap"}` (from + min(cap, frac x span) + off). A bad file logs
`[dress] REFUSED` and leaves no styles; the part kit then refuses to make the Zero Day.
