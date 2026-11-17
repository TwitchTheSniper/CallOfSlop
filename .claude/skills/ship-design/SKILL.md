---
name: ship-design
description: Design or change a ship, a big vehicle or a construct part for Call of Slop and show it to the user in the Ship gallery, a turnable 3D Artifact. Use for new ships (frigates, miners), ship parts, or when the user wants to see a ship.
---

# Ship design, with the Ship gallery

A ship is a **blueprint of parts**. Big ships are generated in a C# part generator: the structure is many pieces welded together (section 1b), plus stock fittings on their sockets. Worked examples: the rust bucket (`Source/PartKit/Generators/ShipParts.cs`, `--gen-parts ship_parts`, notes in `docs/sandbox/constructs/ship.md`), the Magpie miner (`MinerParts.cs`, `docs/sandbox/constructs/miner.md`) and the Zero Day torpedo frigate (`Source/PartKit/Generators/TorpedoFrigateParts.cs`, notes in `docs/sandbox/constructs/torpedo-frigate.md`): the one to copy for a sleek shape, because its hull is lofted, not stacked (section 1c). Read ship.md and `docs/sandbox/constructs/look.md` ("Valid is not finished", "Rules of thumb", "Off the axis") before you start. For the part file itself follow the `construct-part` skill.

## 1. Plan the shape before the parts

- Write down the job (miner, frigate, hauler), the crew, the rooms and the rough size in metres. Describe the silhouette: nose, flanks, stern, where the engines sit.
- Write down each drive (its type, thrust, and hull drive or pod), each turret and launcher, and the rooms their works need (section 1d), before the hull: a drive room is the last room before the stern.
- **No plain boxes.** Every big face gets an angle: cut corners at bow and stern, sides that flare below the deck line and lean in above it, tapers toward the nose, a raked canopy, a spine or radiator on the roof. A tower of identical flat panels reads as a box too: vary panels deck to deck, taper, add ribs. Use `"shape": "hull"` boxes with `points` for faceted panels (any angle), `wedge` with `taper`/`shift` for tapering pieces, `bevel`/`rounded` with `radius` for edges, `cylinder`/`tube` for tanks, nozzles and pods. A hull facet is drawn only: give it hidden solid boxes behind it for collision and sockets.
- Three colours (base, trim, accent under a tenth), a finish on every big face, lamps at every way in.
- Ship parts go in the `"group": "Ship"` (with a `section` per ship), so the gallery finds them. Work in a generator (C#, `Source/PartKit`), never by hand-editing generated JSON.
- **A ship's own pieces are not parts the player picks (Karl, 2026-11-16).** Its decks, hulls, bays, keel and everything else named after it come with its blueprint only: `Constructs/Categories.json`'s `"unlisted"` rules keep group `Ship` out of the Parts tab and its search unless the section is `Ship fittings`, and any `Ship` part whose name holds a ship's name. A new ship's section is hidden by itself; add its name to the name rule there. Spawn the ship from the Blueprints tab.
- **A fitting any ship can use has a generic name (Karl, 2026-11-16).** It goes in section `Ship fittings`, under a name that names no ship (the `Drill Dock`, not "Magpie Drill Dock"), with a description that says it fits any ship, and a plain wall or floor socket so it welds on anywhere. A generator that makes one for its ship writes it to `Constructs/Parts/Fittings` (MinerParts.cs's Drill Dock). Before making a ship-named jet, locker or gun, check whether a generic one already fits.

## 1b. Make it of pieces, welded together

Karl (2026-11-15): one big part per deck meant one hit took a whole deck. A ship's structure is **individual parts that break off one at a time**: every floor, wall run, corner frame, partition and hull panel is its own part, welded to whatever it touches. The Zero Day does this (`Source/PartKit/Generators/TorpedoFrigateParts.cs`, `SplitDeck`, `SplitBands`, `Connect`; "Breaking up" in `docs/sandbox/constructs/torpedo-frigate.md`); the Halberd, its first user, was removed (Karl, 2026-11-16). Copy its approach:

- **Draw a storey whole, then split it.** Keep building each storey as one `Part` with your normal helpers, then deal its boxes out to pieces by where each one sits (`Split`). The deck's floor slab and anything loose in the room go to the **Deck** (it keeps the storey's name, so the blueprint still finds it). Each **Wall** run takes what is on its face and what stands outside it, such as a turret housing. The four **Corner** frames take the chamfer, the rib and the corner armour. Each **Partition** takes its door frames. The outer skin goes on **Hull** panels per face (`HullPanels`), so knocking off a panel opens the room behind it.
- **Sockets go with their face.** Give each socket to the piece whose solid or hidden box face it sits on, facing out (`SockOwner`). Hang furniture through a wrapper that looks up that piece (`Hang`/`HangTurned` in the generator), not on the old storey's step.
- **Weld every touching pair.** Wherever two pieces' solid boxes touch face to face, add one facing socket pair on the biggest contact (`Connect`, sockets `j0`, `j1` ...). Place the pieces outward from the first over those pairs (`PlaceOrder`; each is a `bp.Put(name, zero, 0)`). One pair places a piece, and the game's `ConstructBuild.AutoLink` welds every other free facing pair within 2 cm. A piece that touches nothing throws, which catches a floating bit at once.
- **Tough frame, softer skin.** Give the corner frames the most health, then walls, then panels and partitions, so a ship loses its skin before its bones. Keep the total mass near the old one, or the flight tuning changes.
- **Watch the seams.** Split pieces lose the z-fight corrector that worked inside one part: details flush with a neighbour piece's face (kick strips on a floor's plane, a strip top level with a desk top) must be offset 3 to 5 mm in the generator. `--check-vehicle` lists "coplanar faces between parts"; get it to 0.
- **Try it.** In a window, knock pieces off: `--window-shot OUT.png --blueprint NAME --do "secs 3; damage PIECE 20000; secs 2; view body N | 18,6,90"` (the shot prints the body number). `callofslop.log` says what broke off and what fell away as wrecks. Only the piece and what stood on it should go. The rest stays one body.
- **Limits.** A part holds at most 256 boxes and 64 sockets, and its name is at most 32 characters. Raise the blueprint's `maxSteps` for the extra parts.

## 1c. Loft the hull first, then fit the rooms in it

Karl's verdict (2026-11-15): the Halberd (since removed), a stack of octagonal storeys whose walls are the hull, reads "very simple" and ugly; the Zero Day, lofted, "so good". Rooms first gives a box; hull first gives a ship.

- **Stations.** Write the hull as data: a half profile of about five points (keel edge, lower chine, beam, upper chine, spine edge) at six to eight stations bow to stern, and `At(z)` blending between them. The silhouette is tuned by moving stations: a long chisel nose, the widest beam well aft, a pinch at the stern.
- **Facets** are plates stretched between two stations along one profile edge (keel, the sides, the spine). Every part along the ship draws its own band of them, so the skin stays continuous across parts.
- **Fit the rooms to the skin:** a `SkinHalf(z, y)` (inner half-width at a height) and a check that throws when a room or floor pokes through. Hidden boxes out to the skin (`Void`) give collision without overlapping neighbours.
- **Decks run the length** (Karl prefers it to storeys): two decks, each split fore and aft into parts, a corridor with rooms off it.
- **Detail in the facet's frame** (along, across, out): bevelled armour slabs in ~3 m panels with a fixed pattern picked by facet and panel index (both sides match), chine rails on the profile corners, raked shoulder blocks. Big shapes first (sponsons, a dorsal block, the drive collar following the stern), small detail only once the silhouette reads.
- **What a spaceship has:** bulkhead doors with hazard heads, railed ladder hatches, an airlock with a cycle light over a boarding shaft, light strips, a dado; rooms sized for the systems in them (walk-in torpedo bays by their magazines), a drive room for the drives' works (1d), magazines by their turrets and room for turret trunks. Karl wants **no glass on the bridge**: armour over it, screens inside (view screen, camera screens).
- **Then split it (1b).** Lofting decides the shape; the pieces decide how it breaks. The Zero Day does both: each big part is drawn whole, then `SplitDeck` and `SplitBands` deal it into floor, wall, ceiling and hull-band pieces (torpedo-frigate.md "Breaking up"). Record each wall run as you draw it, so the split knows the walls without guessing; pieces of one part must not overlap (one part may overlap itself, two may not), and a piece needs a drawn box and a solid one. A jet takes its fuel from the tank it is linked to, so a split tank shares its capacity out to every piece that carries a thruster, then run `--flight-check` against the whole ship's figures.

## 1d. Engines and weapons have insides

Karl (2026-11-16): big drives and turrets have an interior part the ship must make room for; inside is armoured and easy to service, a pod or nacelle turns better but is exposed and needs a space walk or a landing to fix. Design: `docs/sandbox/design/ship-works.md`; code `Source/Constructs/ConstructWorks.cs`; numbers `Constructs/ShipWorks.json`; parts `Source/PartKit/Generators/ShipWorksParts.cs` (`ship_works`, the generic Hull Mount, Drive Nozzle, Drive Core, Fuel Pump, Coolant Exchanger, Drive Nacelle and Service Hatch, in `Constructs/Parts/ShipWorks`).

- **One engine a bell** (Karl, 2026-11-16, ship-works.md section 15). Every main drive over `jetMost` (500 kN) gets its own detailed, clad cylinder straight behind it, in line, and nothing is shared between bells. Lift jets, retros and RCS are whole parts with no works. Decide each drive before the hull: its thrust, and hull drive or nacelle. ShipWorks.json's `drives` table says which pieces it needs (a torch: a Drive Core over 0.5 MN, a Fuel Pump from 2 MN, a Coolant Exchanger from 6 MN). Give the head its block with `Works.Head(p, Works.Table(), "torch", force)` (`nacelle: true` in a nacelle), never by hand. `--check-vehicle` fails a main drive with no engine.
- **A hull drive**: the bell on a mount (`Works.Mount(p)`) on the hull's outer face; its engine hangs on the mount's inner face on the bell's axis: a Drive Core by its `front`, then a Fuel Pump and a Coolant Exchanger by `back` -> `front`. `ConstructWorks.Build` follows only pieces within 0.15 m of the bell's line and 5 degrees of its axis, and stops at a mount. Plan the cylinder's length (`CoreD` + `PumpD` + `ExchangerD`) and a walkway round it, in a room joined to engineering (not a sealed room of its own). Each piece has a metre clear in front of its gauge and is reached standing from the engine room's door. A hull drive burns `ShipWorksParts.HullBurn` (2 l/s a MN). The Zero Day's engine bay is the reference.
- **A nacelle**: a hollow housing on a pylon outside the skin (`EngineHousing.Draw` for a ship's own nacelle, or the generic Drive Nacelle S/M), its tail plate a mount flagged `Works.Mount(p, outside: true)`, the drive on the plate's outer face and a Drive Core on its inner face, a Service Hatch hung by `Works.HangHatch`. Serviced outside, through the hatch. `ShipWorksParts.NacelleBurn` (2.4).
- **Jets** of 500 kN or less (RCS, small manoeuvre and retro jets) are whole and need nothing. **Every lift jet and retro over 500 kN** is a hull drive (flag the keel or bow lengths as its mount, and the floor over them, and stand a Drive Core on that floor: the Zero Day's lift jets) or a pod where nothing walkable is above it (the rust bucket's, the Magpie's).
- **Turrets: surface or trunked.** A surface turret (Turntable, hinge, gun, a box bolted to the gun) needs nothing inside. A **trunked** one stands on a **Turret Ring S/M/L** (`ship_works`): its trunk goes 0.6 to 1.5 m down through the deck or into a barbette's well, so leave that clear (the trunk's radius plus 2 cm all round). An ammo feed into the ring's **Ammo in** (the trunk's foot) feeds every gun on the ring through any traverse, with no link across the joint, and **Autoloaders** bolted to the trunk's sides cut a heavy gun's reload to 0.4 of its time (a belt gun doesn't pause). Give each trunked turret its own magazine (an Ammo Container whose box is a sealed bunker: walls, deck and the hull's void within 35 cm of each face) inside the hull right by its ring, with its container reachable from a room. A feed through solid hull or void can't be routed: the generator lays it with nodes (`Feeds` with nodes, `"laid": true`; the Zero Day's `MagazineFeed`). One magazine for several far rings gave feeds the router gave up on (2026-11-16).
- **Launchers**: a rack on a Hull Mount reloads itself from a Reload Hoist bolted to the mount's inner face, with Ordnance Racks bolted to the hoist as its magazine (ship-works.md 6). Give the magazine a room behind armour and a door, deep enough that the hanging weapons clear the mount (a hoist's sides and `under` are set back for that). **Burst weapons** get Capacitor Banks in a later phase (5.4): plan a capacitor room in engineering for a railgun or laser.
- **Energy guns**: a railgun or a laser fires from Capacitor Banks only, never from batteries (Karl, 2026-11-17; ship-works.md 8). Give the ship a Capacitor Bank on its circuit, sized for a few shots, in engineering or behind armour; `--check-vehicle` prints a `capacitors:` line and fails an energy gun with none. A Laser Emitter stands on a Turret Ring with Laser Generators bolted to its trunk (as Autoloaders are); a Laser Pod needs none.
- **Check**: `--check-vehicle` prints a `works:` line a head: every hull head at 100%, every pod "serviced outside"; `--check-building` reaches every works piece; `--flight-check` again after the works' mass went in.

## 2. Build and export

In the container (CLAUDE.md has the SDK setup):

```
export XDG_RUNTIME_DIR=/tmp/xdg-runtime
dotnet build -c Release --no-restore                                 # after each change to the generator's code
dotnet bin/Release/net10.0/CallOfSlop.dll --gen-parts ship_parts     # your generator (also copies what it writes into bin/)
dotnet bin/Release/net10.0/CallOfSlop.dll --preview-shot "Ship Galley Range" --out $SCRATCH/part.png
dotnet bin/Release/net10.0/CallOfSlop.dll --preview-gallery $SCRATCH/ship-gallery
```

**The fast loop for a part's looks** is `--gen-parts` then `--preview-shot`: about 1 s and 3 s, no window, browser or GPU, no second build. The compile after a code change is the slow step (about 50 s here, 26 s on Karl's PC), so make several changes per round. `--preview-shot NAME|FILE` draws the model straight to a PNG: a sheet of the four corners at three quarters by default (a part's front is +z), or `--views iso,iso2,back,back2,front,bow,side,right,top,under`; `--cut Y` drops what is above a height (a deck), `--size N` a tile's pixels. Its line goes to `callofslop.log`, not the console. Open the PNG with Read; it is for you, not the user.

`--preview-gallery DIR` exports every ship (a blueprint whose first part is in the Ship group), every part a ship uses and every other Ship part through the game's own build rules, and finds each ship's decks (floors with a ceiling over them, named after the part with the most floor there): `DIR/index.html`, `DIR/models/index.json` and `DIR/models/NAME.json`. A step that doesn't fit is REFUSED with the reason (the exit code counts refusals). The last line, in `bin/Release/net10.0/callofslop.log`, lists the files that changed (`changed: ...`). `--only "Name A,Name B"` limits it; `--preview-construct NAME|FILE --out X.html` makes one self-contained page (a draft blueprint file not yet in the data).

## 2b. Links: name the ends, let the build route them

Karl (2026-11-15): chutes and cables that run from one side of a room to the other are bugs. A walkable ship's blueprint says `"conduit": true`, and then the build routes every wire and every `"links"` entry **with no nodes** along walls, floors and ceilings, through a wall, deck or hull plate where it must, and across a joint once (power-and-links.md "Ship links and walls"). So in a ship generator:

- Give a link its two ends and ports only (`"from"`, `"to"`, `"fromPort"`, `"toPort"`); never hand-place `"nodes"`, and never set `"laid"`.
- Put ports where a run along a surface exists: a hold's chute inlet against a wall or the ceiling, a drill's or a machine's Chute out facing a wall, a switch on the wall it wires along. A port in the middle of a room makes even a routed chute cross the room in the open (the first Magpie's drill chutes crossed the walkway round its drill well at 1.6 m).
- Walls a cable has to pass are frame or panel boxes no thicker than 30 cm; thicker armour needs an opening or a thinner plate where the cable goes.
- Check: `--conduit-check "Ship Name"` lists every link (nodes, length against the straight line, metres in the open) and the log's `[conduit]` lines name the ones left straight and why; `--preview-shot "Ship Name" --links --cut Y` draws them (data yellow, power red, pipe blue, chute orange); the gallery's Links toggle shows them to Karl. Aim for no link left straight but logic hops under half a metre, and little in the open.

## 3. Look at it yourself once

For a part, or a ship from a few sides, `--preview-shot` (section 2) is enough. For the gallery page itself, the cloud browser can't reach cdn.jsdelivr.net and the gallery fetches its models, so serve DIR on localhost and route three.js from npm (allowed):

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

Then the game's checks: `--check-building "NAME"` for a walkable ship, `--check-vehicle FILE` (z-fighting **between** parts, it builds, it stands), `--check-construct-parts`, and `--selftest-only "No z-fighting"` for faces sharing a plane **inside** each part (`--check-vehicle` doesn't look there, and CI fails on it). Read REFUSED lines in `callofslop.log`: a refused part just goes missing from the build.

## 4. Update the Ship gallery

There is one **Ship gallery** Artifact for the project. Its link is in project memory (`ship-preview-artifacts`); else find it with the Artifact tool's `list`. Never publish a second gallery.

- From a session that didn't publish it: `read` it first (`action: "read"`, its `url`), then publish with `url`, `file_path` = `DIR/index.html`, `root` = DIR, and `files` = only the paths the export printed as changed (`{"models/x.json": "models/x.json", ...}`, always with `models/index.json` when it changed). Files left out stay as they were.
- `--only` writes a `models/index.json` that lists only the ships it exported: merge its entries into the published index (keep the other ships and their parts) before publishing, or the gallery loses them.
- The export deletes models it no longer writes (a ship or part renamed or removed) and prints them as `removed (publish them as null)`: drop each with `"models/old.json": null`.
- Copy DIR to `/mnt/project-files/ship-gallery/` too.
- The page has two pickers: a ship (or "Parts in no ship"), then the whole ship, one deck (cut at chest height from above, like a floor plan) or one of its parts on its own. Links name the view: `#zero-day-frigate`, `#zero-day-frigate--deck-1`, `#zero-day-frigate--part-zero-day-pdc`.
- Reply with the link and the view's `#name`, plus two or three lines on what changed and what to look at ("cut at 13 m for the crew deck"). Don't send screenshots unless a question needs one.

The preview is a design aid, not the renderer: no textures, no lamp light, legs and tracks left out, joints at rest, hidden collision boxes skipped. Judge final looks in the game (`--window-shot`, or `playtest-tour` on Karl's PC) before merging.

## Gotchas from past ships

- One part per deck vanished whole under fire (the Halberd, 2026-11-15, since removed): build the structure of welded pieces from the start (1b).
- A ship with bare nozzles and no works has a quarter of its thrust (ShipWorks.json's `bareShare`), which seldom lifts it: hang the works and read `--check-vehicle`'s `works:` lines (1d).
- The rust bucket started as a box with windows; Karl's steer was "less boxy, corners, a nose that makes sense". Start faceted.
- A part's place rounds to the centimetre: sockets on whole centimetres, floors on a half-metre lattice.
- No two drawn faces in one plane: offset 2 to 5 mm (`.claude/rules/z-fighting.md`).
- Stairs: the Wide Stair under its Wide Stair Floor, or people crouch. Furniture on `"wall"` sockets, not mid-room.
- Lift jets stand round the centre of mass, or the hover hold can't keep it level.
- The game reads data from `bin/`. `--gen-parts` copies what it writes there too (2026-11-15); a file changed any other way needs a rebuild, or the gallery shows the old parts.
- Fittings: the building kit's furniture (Galley Stove, Mess Table, Locker...) reads as a house's (Karl, 2026-11-15, the rust bucket's galley). A ship gets ship fittings: bolted down, clamps, rails, grab bars, worn paint (`Ship Galley Range`, `Ship Mess Table` in ShipParts.cs keep the stock parts' sockets, so they swap in).
- Lofted plates z-fight in ways boxes don't (Zero Day, 2026-11-15): neighbouring facets cross at their shared edge, so their end caps share a plane; stagger the ends of alternate facets (odd ones 6 mm aft, chine rails between, each 1.5 mm short of its band: `FacetStagger`), draw a slightly twisted quad as one plate round all four corners, not two near-coplanar triangles, and start strips, glows and blocks a few mm off the face of the plate they sit on.
- Thrusters need their own `"flame"`: the default is sized from force (radius `sqrt(F) * 0.002 + 0.05`), so a 9 MN drive got one flame 12 m across from its middle and a 1.5 MN jet one 5 m across. Give one flame per nozzle, at its mouth, as wide as it.
- Walkable from the ground: leave 2.2 m or more under the keel to the boarding shaft, put an airlock's inner door where both its landings are clear of the shaft's rail, and keep tables out of doorways and ladder landings (`--check-building`'s map: 'o' is a floor that fits but can't be reached).
- A wire reaches 8 m centre to centre: give each room its own switch, and put gun consoles within 8 m of their turret modules.
