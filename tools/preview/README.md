# Construct preview pages

`--preview-construct NAME|FILE [--out OUT.html]` builds a blueprint (by name, or a blueprint file not yet in the game's data)
or a single construct part (by name) through the game's own build rules, and writes one self-contained web page: the
triangles the renderer draws (shape meshes, paint, finish, glass, glow), a three.js viewer that turns, cuts away by height,
colours by finish and picks parts. `viewer.html` is the page; the command puts the model's JSON in place of `__MODEL__` and
the name in `<title>`. Default output: `preview/NAME.html` next to the DLL. The summary line goes to `callofslop.log`.

It is for agents designing ships and parts: the page is published as a claude.ai Artifact for the user to turn round
(the ship-design skill). It is a design aid, not the renderer: no textures, no lamps' light, legs and tracks left out,
joints at rest, hidden collision boxes skipped. Parts come from the data next to the DLL, so rebuild after `--gen-parts`.

The page loads three.js 0.160.0 from cdn.jsdelivr.net. A cloud container's browser can't reach that CDN; to look at a
page there, serve the npm package's files to the page (Playwright `page.route`), as the skill describes.
