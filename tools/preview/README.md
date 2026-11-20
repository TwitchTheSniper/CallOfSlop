# Construct preview pages and the ship gallery

Two commands write web pages that show constructs in 3D, built through the game's own build rules from the data next to
the DLL (rebuild after `--gen-parts`). The summary line goes to `callofslop.log`.

- `--preview-gallery DIR [--only "A,B"]`: **the usual one.** Every ship (a blueprint whose first part is in the `Ship`
  group), every part those ships use and every other `Ship` part, or only the names given, as one gallery: `DIR/index.html` (the viewer with a picker),
  `DIR/models/index.json` and one `DIR/models/NAME.json` per model. A second run leaves unchanged files alone and prints
  the changed ones (`changed: ...`), so the gallery Artifact is republished with only those. `--group Station` makes the
  same gallery of another part group (the space stations: their own Station gallery Artifact; the page then says "Whole station").
- `--preview-construct NAME|FILE [--out OUT.html]`: one blueprint (by name, or a draft file not yet in the data) or one
  part, as a single self-contained page.

`viewer.html` is the page for both: one puts the model's JSON in its `model` script tag, the gallery puts `null` there and
the page fetches `models/index.json` and a model when it is picked (a ship picker, then the whole ship, a deck or one of its parts; `#ship--deck-2` or `#ship--part-id` in the address picks a view). Decks are found by the export: floors with a ceiling over them. The viewer turns,
cuts away by height, colours by finish, picks parts and shows the links (Links: cables, pipes, chutes and shafts as the build laid them, one colour a type; Links through walls draws them over everything). Each model carries `"links"`: `[type, x, y, z, ...]` from the output through the nodes to the input. `--preview-shot ... --links` draws them in the PNG too. It is a design aid, not the renderer: no textures, no lamps'
light, legs and tracks left out, joints at rest, hidden collision boxes skipped.

The page loads three.js 0.160.0 from cdn.jsdelivr.net. A cloud container's browser can't reach that CDN, and the gallery
fetches its models, so to look at it there serve DIR over http on localhost and route the CDN to the npm package's files
(Playwright `page.route`; launch with the proxy's `bypass: "localhost"`), as the ship-design skill describes.

## The Procedural gallery

`--procship-gallery DIR [--per N] [--seeds "F17,C3d"] [--add]` writes the procedural ship generator's ships (Source/ProcShips,
`docs/procships/PROCSHIPS.md`) as the same kind of models, each with its plan laid over it (`"proc"`: rooms, doors, airlocks,
hatches, ladders and module sockets in the model's frame), and `DIR/index.html` from `tools/preview/procedural.html`, a page
of its own: a ship list with filters (class, hull, decks, role, drive) and Roll, looks (Outside, Cutaway, Section, No skin, one per deck; the cutaway is a
shader that throws away fragments inside boxes, as planes can't cut a notch), the three cuts, the skin, interior, ways
through and sockets drawn or hidden, the plan marked, and grades, notes and requests kept in the artifact's database for the
next session that works on the generator. The Artifact is https://claude.ai/artifact/EcWdMmsYpbjndGutjc6pmK; PROCSHIPS.md
"Working the feedback" says how to republish it and answer the notes.

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
