---
name: ship-design
description: Design or change a ship, a big vehicle or a construct part for Call of Slop and show it to the user in the Ship gallery, a turnable 3D Artifact. Use for new ships (frigates, miners), ship parts, or when the user wants to see a ship.
---

# Ship design, with the Ship gallery

A ship is a **blueprint of parts**. Big ships are a few large generated parts (keel, decks, roof, pods) plus stock fittings on their sockets. Worked examples: the rust bucket (`Source/PartKit/Generators/ShipParts.cs`, `--gen-parts ship_parts`, notes in `docs/sandbox/constructs/ship.md`) and the Halberd Frigate (`Constructs/Parts/Frigate`, `Constructs/Blueprints/HalberdFrigate.json`). Read ship.md and `docs/sandbox/constructs/look.md` ("Valid is not finished", "Rules of thumb", "Off the axis") before you start. For the part file itself follow the `construct-part` skill.

## 1. Plan the shape before the parts

- Write down the job (miner, frigate, hauler), the crew, the rooms and the rough size in metres. Describe the silhouette: nose, flanks, stern, where the engines sit.
- **No plain boxes.** Every big face gets an angle: cut corners at bow and stern, sides that flare below the deck line and lean in above it, tapers toward the nose, a raked canopy, a spine or radiator on the roof. A tower of identical flat panels reads as a box too: vary panels deck to deck, taper, add ribs. Use `"shape": "hull"` boxes with `points` for faceted panels (any angle), `wedge` with `taper`/`shift` for tapering pieces, `bevel`/`rounded` with `radius` for edges, `cylinder`/`tube` for tanks, nozzles and pods. A hull facet is drawn only: give it hidden solid boxes behind it for collision and sockets.
- Three colours (base, trim, accent under a tenth), a finish on every big face, lamps at every way in.
- Ship parts go in the `"group": "Ship"` (with a `section` per ship), so the gallery finds them. Work in a generator (C#, `Source/PartKit`), never by hand-editing generated JSON.

## 2. Build and export

In the container (CLAUDE.md has the SDK setup):

```
export XDG_RUNTIME_DIR=/tmp/xdg-runtime
dotnet build -c Release --no-restore
dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts ship_parts     # your generator
dotnet build -c Release --no-restore                                 # the game reads parts from bin/
dotnet bin/Release/net10.0/CallOfSlop.dll --preview-gallery $SCRATCH/ship-gallery
```

`--preview-gallery DIR` exports every ship (a blueprint whose first part is in the Ship group), every part a ship uses and every other Ship part through the game's own build rules, and finds each ship's decks (floors with a ceiling over them, named after the part with the most floor there): `DIR/index.html`, `DIR/models/index.json` and `DIR/models/NAME.json`. A step that doesn't fit is REFUSED with the reason (the exit code counts refusals). The last line, in `bin/Release/net10.0/callofslop.log`, lists the files that changed (`changed: ...`). `--only "Name A,Name B"` limits it; `--preview-construct NAME|FILE --out X.html` makes one self-contained page (a draft blueprint file not yet in the data).

## 3. Look at it yourself once

The cloud browser can't reach cdn.jsdelivr.net and the gallery fetches its models, so serve DIR on localhost and route three.js from npm (allowed):

```
mkdir -p $SCRATCH/npm && cd $SCRATCH/npm && npm pack three@0.160.0 && tar xzf three-0.160.0.tgz
cd $SCRATCH/ship-gallery && python3 -m http.server 8765 &
```

Playwright: `chromium.launch({ proxy: { server: process.env.HTTPS_PROXY, bypass: 'localhost' }, args: ['--use-gl=swiftshader','--enable-webgl','--ignore-gpu-blocklist'] })`, `newPage({ ignoreHTTPSErrors: true })`, `page.route(/cdn\.jsdelivr\.net\/npm\/three@0\.160\.0\/(.*)/, r => r.fulfill({ path: NPM + '/package/' + tail, contentType: 'application/javascript' }))`, stub `fonts.googleapis` with empty CSS, open `http://localhost:8765/index.html#ship-name`, wait 5 s, screenshot. For a deck view set the slider: `el = document.getElementById('cut'); el.value = N; el.dispatchEvent(new Event('input'))`. One outside shot and one per deck is enough; these pictures are for you, not the user.

Check:

- **Finishes fit a ship.** Hull and decks are plated or dirty steel, deck plate, grating, painted plating. Bathroom tiles, lino, terrazzo, wall panelling or concrete belong to colony buildings: on a ship at most on the wet room's floor. Turn on "Colour by finish" and hover anything that stands out.
- **Nothing clips.** Furniture inside walls or the hull, a fitting through a floor, a stair through a ceiling, pods through the hull, glass in a wall. Cut at each deck and look along every wall.
- **Nothing floats.** Fittings stand on floors, pods touch the hull, jets sit on their mounts.
- **The silhouette reads** from the Side and Top views: not a rectangle, a nose that makes sense, engines that push the way it flies (the game's bow is -z).
- **Colour**: not one colour, not all dark grey; the accent marks doors, ramps and controls.

Then the game's checks: `--check-building "NAME"` for a walkable ship, `--check-vehicle FILE` (z-fighting between parts, it builds, it stands), `--check-construct-parts`. Read REFUSED lines in `callofslop.log`.

## 4. Update the Ship gallery

There is one **Ship gallery** Artifact for the project. Its link is in project memory (`ship-preview-artifacts`); else find it with the Artifact tool's `list`. Never publish a second gallery.

- From a session that didn't publish it: `read` it first (`action: "read"`, its `url`), then publish with `url`, `file_path` = `DIR/index.html`, `root` = DIR, and `files` = only the paths the export printed as changed (`{"models/x.json": "models/x.json", ...}`, always with `models/index.json` when it changed). Files left out stay as they were.
- The export deletes models it no longer writes (a ship or part renamed or removed) and prints them as `removed (publish them as null)`: drop each with `"models/old.json": null`.
- Copy DIR to `/mnt/project-files/ship-gallery/` too.
- The page has two pickers: a ship (or "Parts in no ship"), then the whole ship, one deck (cut at chest height from above, like a floor plan) or one of its parts on its own. Links name the view: `#halberd-frigate`, `#halberd-frigate--deck-3`, `#halberd-frigate--part-halberd-pdc`.
- Reply with the link and the view's `#name`, plus two or three lines on what changed and what to look at ("cut at 13 m for the crew deck"). Don't send screenshots unless a question needs one.

The preview is a design aid, not the renderer: no textures, no lamp light, legs and tracks left out, joints at rest, hidden collision boxes skipped. Judge final looks in the game (`--window-shot`, or `playtest-tour` on Karl's PC) before merging.

## Gotchas from past ships

- The rust bucket started as a box with windows; Karl's steer was "less boxy, corners, a nose that makes sense". Start faceted.
- A part's place rounds to the centimetre: sockets on whole centimetres, floors on a half-metre lattice.
- No two drawn faces in one plane: offset 2 to 5 mm (`.claude/rules/z-fighting.md`).
- Stairs: the Wide Stair under its Wide Stair Floor, or people crouch. Furniture on `"wall"` sockets, not mid-room.
- Lift jets stand round the centre of mass, or the hover hold can't keep it level.
- The game reads data from `bin/`: rebuild after `--gen-parts`, or the gallery shows the old parts.
