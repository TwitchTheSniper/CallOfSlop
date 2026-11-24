# Construct preview pages and the ship gallery

Two commands write web pages that show constructs in 3D, built through the game's own build rules from the data next to
the DLL (rebuild after `--gen-parts`). The summary line goes to `callofslop.log`.

- `--preview-gallery DIR [--only "A,B"]`: **the usual one.** Every ship (a blueprint of 30 parts or more whose first part is in the `Ship`
  group, as the Animation gallery counts them), every part those ships use and every other `Ship` part, or only the names given, as one gallery: `DIR/index.html` (the viewer with a picker),
  `DIR/models/index.json`, one `DIR/models/NAME.json` per ship and one `DIR/models/parts/SECTION.json` per part section (the part's
  `"section"`, such as `zero-day-frigate` or `ship-fittings`: an object of models keyed by part id, which the page fetches once and takes
  the part from; a part's index entry names its `file` and `key`). An Artifact version holds at most 511 files, and a file a part
  filled the Ship gallery by 13 ships; packed, 18 ships and 800 parts are about 60 files. A `--only` run adds its parts to the
  section files already in DIR and leaves the other section files alone. A second run leaves unchanged files alone and prints
  the changed ones (`changed: ...`), so the gallery Artifact is republished with only those. `--group Station` makes the
  same gallery of another part group (the space stations: their own Station gallery Artifact; the page then says "Whole station").
  The Ship gallery is for trying a ship's look while it is designed (Karl, 2026-11-21): once a ship's look is settled, add its name
  (and its part section) to `gallery-skip.txt` here, and a full run leaves it out and lists its files as removed.
- `--preview-construct NAME|FILE [--out OUT.html]`: one blueprint (by name, or a draft file not yet in the data) or one
  part, as a single self-contained page.

`viewer.html` is the page for both: one puts the model's JSON in its `model` script tag, the gallery puts `null` there and
the page fetches `models/index.json` and a model when it is picked (a ship picker, then the whole ship, a deck or one of its parts; `#ship--deck-2` or `#ship--part-id` in the address picks a view). Decks are found by the export: floors with a ceiling over them. The viewer turns,
cuts away by height, colours by finish, picks parts and shows the links (Links: cables, pipes, chutes and shafts as the build laid them, one colour a type; Links through walls draws them over everything). Each model carries `"links"`: `[type, x, y, z, ...]` from the output through the nodes to the input. `--preview-shot ... --links` draws them in the PNG too. It is a design aid, not the renderer: no textures, no lamps'
light, legs and tracks left out, joints at rest, hidden collision boxes skipped.

A ship that carries craft (a blueprint's `"carry"` list) shows them aboard, each part named "Craft: Part"; the Mainframe's
gallery model also puts a Zero Day in its berth (`ConstructPreview.GalleryAboard`). A model over 300,000 triangles is written
instanced (`ConstructPreview.Instanced`): `"tpl"`, one template per kind of part (its own triangles and scale), and `"inst"`,
30 bytes a placed copy (uint16 kind, float32 x y z, float32 quaternion x y z w); the page expands them as it loads, so the
Mainframe's 1.2 million triangles are 5.4 MB. The page's near plane follows the camera's distance and its grid's cells grow
with the model (1 m up to about 200 m across, then 10 m or 100 m); its part list shows run-time pieces (cut plates,
extrusions) as one row per kind.

The page loads three.js 0.160.0 from cdn.jsdelivr.net. A cloud container's browser can't reach that CDN, and the gallery
fetches its models, so to look at it there serve DIR over http on localhost and route the CDN to the npm package's files
(Playwright `page.route`; launch with the proxy's `bypass: "localhost"`), as the ship-design skill describes.

## The Animation gallery

`--anim-gallery DIR` writes the same kind of gallery for things that move, with a timeline: a Play button, a time slider to
scrub through the motion, marks that jump to each step, "Open and shut" (plays it open, pauses, plays it shut) and "Slow"
(a quarter speed). Three cuts work on it and on the Ship and Station galleries too: **Cut at** (height), **Cut side** (across
x) and **Cut length** (along z). The top picker is the kind (Ships, Stations, Doors and hatches, Machines...), the second
the one to show; the Ship and Station galleries also have a third for a ship's decks and its own parts. A moving piece shows a faint orange outline of where it rests. The Artifact is
https://claude.ai/artifact/K33B3W7yHsqTQApajCijUD: republish it from a new run with only the changed files (the run prints
them, like the Ship gallery), after reading or listing it once from a new session.

What it picks up by itself, from the part data the game draws by (code: `Source/DevTools/AnimGallery.cs`):

- **Doors and hatches**: every part with a `"door"` block (event doors, docking and launch hatches), over its `"seconds"`.
- **Hinges and joints**: a hinge's or joint's leaf boxes, from its "min" to its "max" at its motor speed (a turntable
  with no limits goes round).
- **Switches and levers**: boxes with a `"rig"` of lever, knob, slide, flip, press or stick, from low or off to high or on.
- **Machines**: `"work"` boxes that spin or stroke, looped.

Not shown: suspension travel, walking legs, a control surface's flap, lamps and screens.

**Assemblies** (several parts moving in order: a pod's canopy, then its gantry) come from a timing file in
`tools/preview/anim/NAME.json` (comments allowed):

```json
{
  "blueprint": "Landing Leg Pod M",
  "title": "Landing leg pod: deploy",
  "shelf": "Landing legs",
  "description": "Optional; the blueprint's own otherwise.",
  "seconds": 6,
  "steps": [
    { "part": "Landing Leg Pod M Canopy L", "at": 0, "label": "Canopy opens" },
    { "part": "Landing Leg Pod M Canopy R", "at": 0 },
    { "part": "Landing Leg Pod M Gantry", "at": 1.2, "seconds": 2.5, "label": "Gantry lowers the leg" }
  ]
}
```

`"shelf"` (optional) names the top picker's group it goes under; without it a whole ship or station (a blueprint of 30 parts
or more whose first part is in the Ship or Station group) goes under Ships or Stations, anything else under Assemblies.
`"blueprint"` is a shipped blueprint's name or a draft blueprint file (a path, or a file name beside the timing file).
Every event door in it opens, with the parts riding its leaf; a step's doors (every door part of that name) start at
`"at"` seconds and take the step's `"seconds"` or their own; doors no step names start at 0. `"seconds"` at the top is the
timeline's length (default: when the last door is open). A step naming no door of the blueprint is listed in
`callofslop.log` with the doors it has. Nothing new goes in the part files: a part animates in the gallery the way it does
in the game, so a motion the game can't do yet can't be shown here yet either.

The model JSON is the preview's with two more keys: `"track"` (base64, one byte a triangle: 0 still, k the k-th track) and
`"anim"`: `{"seconds", "loop", "marks": [[t, label]...], "tracks": [{"name", "frames": [[x, y, z, qx, qy, qz, qw]...]}]}`,
each track's pose in the model frame sampled evenly over the seconds (15 a second, at most 121); the page interpolates.

## The Lighting test

`--light-test DIR [--only "id,id"]` (Karl, 2026-11-24: walk round real places on a phone and spot lighting faults) writes the
places named in `light-test.json` here as one page, `DIR/index.html` (`light-test.html`), `DIR/models/index.json` and one
`DIR/models/ID.json` each: the construct's model as the gallery writes it, plus every lamp `ConstructLamps` would offer (a lined
bay's lamps, ceiling strips, Lamp parts as if on: kind 0, 1 and 2), the map's sky light (`Map.Light`: the star's direction and
colour, the sky and ground ambient) and the views to stand at (a part's middle, a point or outside). Code:
`Source/DevTools/LightTest.cs`. The Artifact is republished with the changed model files (read or list it first from a new session).

The page lights the model the way `Lighting.Glsl`'s mode 5 does: hemisphere ambient scaled by the dark interiors' eight diagonal
maps (a near set round the eye and a far set over the whole model, as `RoomShade` blends them), the star with a shadow map round
the eye, the nearest lamps in reach (`ConstructLamps.Reach`, faded as they hand over, 12 on Phone and 32 on Full) with `DynLight`'s
falloff, spots and fill, their own six-face shadows and the soft knee, and glow boxes drawn as they glow. Touch: a stick under the
left thumb, drag to look, up and down buttons; Walk keeps you on floors, off flies through walls. Not shown: material patterns and
shine, bloom, AO, fog, contact shadows, lamp beams, the lamps' hidden-from-eye ordering, terrain, and dusk's longer lamp throw.
