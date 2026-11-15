# Construct preview pages and the ship gallery

Two commands write web pages that show constructs in 3D, built through the game's own build rules from the data next to
the DLL (rebuild after `--gen-parts`). The summary line goes to `callofslop.log`.

- `--preview-gallery DIR [--only "A,B"]`: **the usual one.** Every ship (a blueprint whose first part is in the `Ship`
  group) and every `Ship` part, or only the names given, as one gallery: `DIR/index.html` (the viewer with a picker),
  `DIR/models/index.json` and one `DIR/models/NAME.json` per model. A second run leaves unchanged files alone and prints
  the changed ones (`changed: ...`), so the gallery Artifact is republished with only those.
- `--preview-construct NAME|FILE [--out OUT.html]`: one blueprint (by name, or a draft file not yet in the data) or one
  part, as a single self-contained page.

`viewer.html` is the page for both: one puts the model's JSON in its `model` script tag, the gallery puts `null` there and
the page fetches `models/index.json` and a model when it is picked (`#name` in the address picks one). The viewer turns,
cuts away by height, colours by finish and picks parts. It is a design aid, not the renderer: no textures, no lamps'
light, legs and tracks left out, joints at rest, hidden collision boxes skipped.

The page loads three.js 0.160.0 from cdn.jsdelivr.net. A cloud container's browser can't reach that CDN, and the gallery
fetches its models, so to look at it there serve DIR over http on localhost and route the CDN to the npm package's files
(Playwright `page.route`; launch with the proxy's `bypass: "localhost"`), as the ship-design skill describes.
