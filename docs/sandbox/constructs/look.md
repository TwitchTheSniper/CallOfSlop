# How a build should look

Read this before you author a blueprint, a generator or a building. The rest of the guide says how to make a build that
is **valid** (it loads, it drives, it fires); this file says what makes it **finished**. A blueprint that loads proves the
rules can build it, nothing more. In a persistent world with factions a build is seen every day, so it has to read as made by
someone.

## Valid is not finished

A finished build has, where it has the parts for them:

- **Paint by section**: the frame, the body, the moving parts and the trim each in their own colour, never the whole build
  in one colour or left in the parts' own greys.
- **A finish** on the big faces: deck plate on what is walked on, plated steel or painted plating on hulls, concrete on
  slabs, worn or rusty paint on what is old, hazard paint on what bites.
- **Lights**: a lamp at the way in, on the cab, at a control; switched on in the blueprint.
- **Something alive**: a joint that moves, a screen or sign that says something, a stack or a lamp that glows. A build
  that has a turntable, a sign or a lamp and starts dead looks broken.
- **Detail**: a few parts that serve the look (panels, signs, clutter), within the budget below.

Check a set of blueprints with `--look-report [FILE|DIR]` (below); judge vehicles by the vehicle kit's own tools too.

## The authoring fields

All of these are read by the blueprint loader (`ConstructBuild.ParseBlueprint`);
it refuses an unknown key with the list of the keys it knows.

| Field | Where | What it does |
| --- | --- | --- |
| `"paint": "#rrggbb"` | a step; a routed link (`"links"`) | The part's (or pipe's, cable's) colour. A bad colour is refused. |
| `"finish": "tread plate"` | a step | A material of `Materials/Surfaces.json` on every box of the part but glass and glow. Name, any case. **A name the build doesn't offer is silently the part's own** (no error): `--look-report` counts them. |
| `"config": {"colour": "#ffb347", "brightness": 120, "blink": true}` | a Lamp step | Glow colour, brightness (10-200 %), blink. |
| `"config": {"startOn": true}` | a Switch or Toggle step | It starts on: wire it to a lamp or a motor. |
| `"config": {"period": 2, "duty": 50, "startOn": true}` | a Timer step | A blinker: wired to a lamp it flashes. |
| `"config": {"speed": 30}` | a Turntable or Motor Hinge step | Its speed (deg/s); it turns while its input is on. |
| `"text": ["OPEN", "CLOSED"]` | a Text Sign or Light Bank step | Its two messages (a sign shows the second while a wire into it is on); up to 60 ASCII characters. |
| `"wires": [[from, to], ...]` | the blueprint | Signal wires by step, checked by the linking tool's rules. |

The configure tool's keys are listed in [The configure tool](building-and-tools.md#the-configure-tool); every key a part
has can go in `"config"`. In the vehicle kit, `v.Paint(part, colour)` and `v.Finish(part, material)` (`VehicleCore.cs`)
write the same fields; `Finish` refuses a name the tool doesn't offer, which the bare blueprint field doesn't.

**Finishes offered** (rows 7 up whose `"offer"` isn't false): steel, painted steel, aluminium, rubber, wood, painted wood,
copper, brass, fabric, concrete, dirty steel, rust, plastic, stone, tread plate, plated steel, painted plating, matte paint,
gloss paint, chrome, carbon weave, deck plate, painted deck plate, grating, painted grating, wall panelling, worn paint,
rusty paint, rubber matting, lagging, hazard paint, soot. Paint and finish are separate: a finished part keeps its paint.

**How lamps light.** A Lamp, Floodlight (S, M, L) or Hanging Lantern is a `lamp` logic part: lit while a wire into it is
on. A mains lamp (Wall Light, Hall Floodlight: logic `"mains": true`) needs no wire: it is lit while a working battery's
circuit reaches it. With a Battery on the build every logic part needs power (the frame is the circuit); without one, and
on a map without `"power": "required"`, nothing does ([Power](power-and-links.md#power)). Lit lamps light what is round
them (`Source/Render/ConstructLamps.cs`: the nearest to the eye within 16 m, at most 4 dynamic lights a frame, 4.5 m
reach), so the old line "lamps cast no light yet" in the configure tool's section is out of date.

**How paint treats a box.** A painted part's paintable boxes are drawn in the paint times the box's **tone**: its
brightness against the part's main colour, clamped to 0.65-1.3 (`ConstructPaint.ToneOf`, `BoxColour`), then capped at
1 per channel. So a part keeps its shading, but only inside that band: a light paint clips its bright boxes to flat white,
and a dark paint makes the whole part dark. Glass, tyres and a lamp's lens don't take paint. **Whole-construct paint**
(the paint tool's mode) puts one colour on every part and link: the sections, the trim and the parts' own two-tone
colours are lost, and the build reads as one colour. Paint by section, step by step.

## A detail vocabulary (parts that exist)

From `Constructs/Categories.json` and the part files:

- **Lights**: Lamp; Floodlight, Floodlight Medium, Floodlight Large; Hanging Lantern, Hanging Lantern Medium; Wall Light
  and Hall Floodlight (mains); Belisha Beacon; Light Bank 4 and 8 (S, M, L: labelled lamps on a panel).
- **Signs and screens**: Text Sign, Medium and Large Text Sign; Deck Sign; Fire Point; Number Readout, Dial Gauge, Bar
  Gauge, Graph Screen (they show something live).
- **Stripes and markings**: the `hazard paint` finish; Bay Marking, Lane Marking, Kick Plate (interior dressing).
- **Panels and services**: Wall Rib, Pipe Bank, Duct Run, Cable Tray Run, Tool Board; Fender, Bumper, Hubcap Plate.
- **Chimneys and stacks**: Medium Chimney and Smokestack (1x2 to 2x8 m), Large (4x8 to 8x32 m), Capital (16x32 to 32x128 m).
- **Clutter and identity** ("Body panels"): Satellite Dish, Router Antenna, Wi-Fi Mast, Phone Mast, Loudhailer, Cable Reel,
  Traffic Cone, Queue Post, Office Plant, Desk Fan, Teapot, Wee Keg; faction pieces: Saltire Flag, Union Flag, Thistle,
  Haggis on a Post, Wee Caber, Skull Trophy, Horned Skull, Iron Spike, Gift Card Sign, Employee of the Month.
- **Furniture** (interiors): Locker, Tool Rack, Gun Rack, Mess Table, Chart Table, Galley Stove, Medical Station.

## Rules of thumb

- **Three colours**: a base (most of the build), a second (frame or trim, clearly lighter or darker), an accent (under a
  tenth of the parts: doors, controls, hazards). Pick them for the faction (`FactionStyle` palettes in the vehicle kit).
- **No build of one colour**, and not all dark grey: today 79 % of shipped paint is dark or grey (baseline below).
- **Break big faces**: a finish with a pattern (plated steel, deck plate, grating), a rib, a panel, a sign or a second
  colour on any face bigger than about 4 by 2 m.
- **Mark** doors and hatches (accent colour, a lamp over them), hazards (hazard paint on moving and cutting edges, ramps,
  guns' arcs) and controls (a lit switch, a sign, a gauge).
- **Light the way in**: every door, ramp and ladder a player uses has a lamp near it, on in the blueprint.
- **Show life**: a turntable that turns, a blinking beacon, a sign with words, a stack.

## A detail budget

There is no fixed number for dressing or lamps (user, 2026-11-11): a build is judged by `--look-report` and by eye in the game. Dressing is not optional because it has no quota: every build gets some, and at least one lamp where it has a way in. Only the 4 lamps nearest within 16 m light anything at a time.

What dressing costs: frames, beams, plates and wedges are **plain** parts; everything else is special
([Rebuilds](overview-and-playing.md#rebuilds)). On a body of 1,024 parts or more, edits to plain parts are journalled; a
special part (a lamp, a sign, a stack) makes edits next to it a full rebuild, and any freeform weld on the body turns the
journal off. [Fusing](building-and-tools.md#fusing) caches plain parts only. So keep dressing as separate, few, larger
parts on big bodies, put it on the body's own segment, and prefer a **finish or paint** (free: no parts, no rebuild cost)
over a part wherever a finish does the job. A construct holds 16,000 parts ([Performance and caps](parts-and-data.md#performance-and-caps)).

## Machine look

Production machinery reads as machinery, not as a painted box with a screen (the user, 2026-11-11). Every machine a generator
makes (the Extruder and Formers, the Press and Die Lathe, the Line Console today; a Panel Former, a Laser Cutter, a building
station, arms and cranes to come) follows seven points. The shared pieces are `Source/PartKit/MachineLook.cs`.

1. **Shape says function.** A silhouette that reads at 50 m and differs from its neighbours': the Extruder is long and low
   with a hopper at the back and a die head at the front; the Press a tall open frame with a ram; a Former a headstock
   with a big bare hub; the console a desk. A new machine picks a shape no other has.
2. **Work is visible and moves, and stops when the machine stops.** A detail box may carry `"work"`: `"spin"` (about
   `"axis"`, through its own centre), `"stroke"` (`"travel"` metres along `"axis"` and back: a ram), `"glow"` (hot orange,
   its own dull colour at rest: a die) or `"smoke"` (puffs from its top). Drawn only while `ProductionLines.Working` says
   the machine runs, from the store each peer already has: nothing on the server or the wire. The status lamp's band and
   the bin's fill are the game's own.
3. **Flow reads one way.** The in side has a white band and arrow, the out side an orange band and arrow, and every port
   where something moves has `hazard paint`.
4. **Three tones**, never one colour: a dark structural base, the family's body colour, a bright accent only on what a
   hand works or what moves. Base, steel, accent and markings are `"paintable": false`, so a machine painted whole (a
   station's colour in a blueprint) keeps its three tones; only the body takes the paint.
5. **Tier by build**: more guarding, cladding and piping at a higher tier, the same silhouette
   ([Machine tiers](production.md#machine-tiers)).
6. **One control face** on the aisle side: the screen's middle about 1.5 m above the machine's floor, the status lamp
   over it (`PartScreens.Mount(..., lamp: true)`), and under the plate a shelf of three accent buttons and a red stop at
   about 1.3 m (`MachineLook.Buttons`). A machine lower than that stands its screen on a board (the small Extruder's).
7. **Real finishes**, by name on the box (`"finish"`): `tread plate` on tops that are walked on, `steel` on working
   parts, `hazard paint` on strips; the body keeps its paint.

| Tone | Colour | On |
| --- | --- | --- |
| Base | `#22262b` | skirts, beds, frames, guards, housings, the control board |
| Steel | `#98a1a9`, finish `steel` | dies, collars, rams, hubs, chucks' plates, work tops |
| Accent | `#f2b01e` | buttons, pulleys, chucks, a ram's hazard edge |
| In / Out | `#ecebe4` / `#f07818` | the band and arrow of each side |
| Stop | `#d02a1e` | the stop button |
| Body | Extruder `#b5452b`, Formers `#8050a8`, Press `#2f5f8f`, Die Lathe `#2a6878`, console `#e0b020` | cladding; takes paint |

Rules of the build: everything added is a `"detail"` box, so collision, bounds, sockets and ports stay the solid boxes';
keep a look box 12 mm thick or more (the game refuses under 1 cm, and `MachineLook.D` throws between); anything on a
face a Die Frame, a piece or a conveyor stands against is under 2 cm proud; faces sit a few mm apart by construction.
Budget: about 40 look boxes at tier 1 or the small class, 80 at the top; racks and junctions about 25. Not done yet:
storage (Materials Bin, Storage Volume and Parts Shelf are hand-made files), tier by build on the Extruders, Formers,
Press and Die Lathe, and a Press has no status lamp.

**The tiered buildings** (`TierMachine.cs`, over `TierStyle`'s bodies; phase 2). A tier keeps its own body and palette
([Machine tiers](production.md#machine-tiers)), so the language goes on as look boxes in metres, after the screen is
mounted and the faces are settled: no solid box, socket, port or screen moves. `Site.Add` leaves out a box that would
cover a port, a socket, a bin, a tray, the screen, the aisle or an Assembler's open front (the "Machine tier looks"
rules, by construction). What `TierStyle` draws as `dark`, `edge` or `pipe` stops taking paint, so a painted machine
keeps a dark stack and a steel blade.

- **A line's cues are the same at every tier.** Smelter: a white-rimmed hopper, a dark charge door with two slots and
  a tap hole that glow over a steel spout, a collared stack that smokes. Foundry: the mouth glows, a steel crucible on a
  trunnion pours into three moulds, both stacks smoke. Sawmill: steel rails through the frame, a carriage and log that
  feed (`stroke`), the blade turns (`spin`), planks over the out bed. Assembler family: the bit, an accent collar and
  a steel platen stroke over the trays; a Thruster Assembler's nozzle glows, a Tailor's bolt turns. Tier 5 keeps
  its lit core instead of a mouth.
- **Flow**: a band over or under each chute port (white in, orange out) with a black arrow pointing back to front,
  hazard paint on the port's other side; tread plate and hazard ends on a furnace's aisle; an orange strip on the
  tray or the trays' lip.
- **Tier by build** adds, never reshapes: tier 2 a guard rail round the hopper, a second stack collar, a door frame,
  hazard strips at the blade and on an Assembler's front posts; tier 3 a pipe main with a valve wheel, a third plank,
  a cable run; tier 4 steel or tread-plate cladding; tier 5 a third collar. A stack's cap grows with the tier.
- A box added with `clear: true` gives way to a detail the tier already has there, so a tier's own dressing wins.

**Phase 3** (the Recycler, the gathering modules, racks and junctions).

- **Recycler** (`recycler_parts`, body `#5f7a34`): an open hopper with a white rim and a hazard front edge, two toothed
  shafts in the throat and a pulley on its side that `spin`, a steel discharge door under an orange band on the control
  face. The throat stays open between the shafts, so the bin's fill shows.
- **Drill and Saw Modules** (`ModuleLook.cs`, over the settled tier body): a dark skirt, an orange band at the way out,
  white bands where wood comes in, a steel fill band with quarter marks up the saw's bin, an accent collar on the bit and
  hub caps on the blades that `spin`, hazard paint on the cutting edge. A module has no screen: its buttons are a strip
  on the rim's top beside the lamp. Tier 2 adds hazard strips, 3 a conduit with clamps, 4 steel corner plates, 5 a
  second collar or a rim strip. A box gives way to the tier's own dressing.
- **Racks**: the frame is the body, cross bars base, slats steel, top rails accent with a dark fill mark at each quarter
  of the length, a white band and hazard sill at Piece in with a white arrow on each runner, a steel load plate on the
  first posts. Nothing is added inside the hold, so stored pieces stay in view.
- **Junctions**: steel rollers on a dark deck, hazard-painted corner posts, and at each way a band on the side and an
  arrow on the deck between the rollers: white pointing in, orange pointing out. No cue for the branch in use: a
  junction has no store for `"work"` to read.
- A look box starts a few mm inside the wall it stands on and stops short of the body's own planes (a skirt 4 mm above
  the underside, ends 6 mm in or out), or the "No z-fighting" check finds it.

## Off the axis

A blueprint step may be a **freeform weld** instead of a socket step: `{"part": "Panel", "weld": {"on": 3, "pos": [x, y, z],
"rot": [x, y, z, w]}}` puts the part on a new rigid segment at any pose in step 3's segment frame (within 4,096 m;
[Freeform attach](building-and-tools.md#freeform-attach-board-item-fw01-decision-25)). Up to 192 a construct
(`ConstructFreeform.MaxPerConstruct`; they share the 255 joints; the "at most 8" in that section is out of date). The
vehicle kit uses them for tilted plates (`Vehicle.Body.cs`); 142 shipped blueprints have weld steps. Parts on a welded
piece snap to it on its own grid. Costs: a segment each, and a big body with any weld loses the plain-edit journal.

## Pointers

- Vehicles: `docs/vehiclekit/VEHICLEKIT.md` (generators, faction styles), `VARIETY.md` (shape grammar, layered variety,
  identity), `BODY-STYLES.md` (cabs, hulls, rooms, lamps in bays); the silhouette rules every faction follows are there.
- Interiors of big vehicles and bases: `docs/sandbox/INTERIORS.md`.
- Machines: per-tier palettes and signature shapes in [Machine tiers](production.md#machine-tiers).
- Buildings: [buildings.md](buildings.md): what every building needs and each faction's style (the Ministry of Slop:
  Brutalism, cold-war fittings). Bases have no guide yet: use the rules above.

## Worked example: the Gate, dressed

The guide's Gate (`Constructs/Blueprints/Gate.json`, 6 steps, no paint) with a concrete pad, a steel-blue post in painted
plating, a hazard-striped top block, a worn sand door, an amber lamp on top switched on, and a battery to run it:

```json
{ "format": "call-of-slop-construct-blueprint/1", "name": "Dressed Gate", "steps": [
  { "part": "Foundation", "paint": "#8a8478", "finish": "concrete" },
  { "part": "Frame Block", "on": 0, "socket": "top01", "by": "-y", "paint": "#35556b", "finish": "painted plating" },
  { "part": "Frame Block", "on": 1, "socket": "+y", "by": "-y", "paint": "#35556b", "finish": "painted plating" },
  { "part": "Frame Block", "on": 2, "socket": "+y", "by": "-y", "paint": "#e0b020", "finish": "hazard paint" },
  { "part": "Hinge", "on": 3, "socket": "+x", "by": "base", "paint": "#2c2e30" },
  { "part": "Door", "on": 4, "socket": "leaf", "by": "hang", "paint": "#b8ab8c", "finish": "worn paint" },
  { "part": "Lamp", "on": 3, "socket": "+y", "by": "-y", "config": { "colour": "#ffb347" } },
  { "part": "Switch", "on": 1, "socket": "-x", "by": "+x", "config": { "startOn": true } },
  { "part": "Battery", "on": 0, "socket": "top33", "by": "-y", "paint": "#35556b" }
], "wires": [[7, 6]] }
```

Load-tested 2026-11-11 from a temp folder: `--check-vehicle` built all 9 parts from 9 steps with the wire and the battery
(its other failures are the vehicle checks: no engine, no wheels); `--look-report` on it: 78 % painted, 5 colours, 29 %
dark or grey, 5 finishes, 1 lamp. The lamp being lit in a window was not checked.

## The baseline (`--look-report`, 2026-11-11)

`--look-report [FILE|DIR]` (default `Constructs/Blueprints`; `Source/App/Program.LookReport.cs`) reads part files and
blueprint JSON only (no build, no maps; about 10 s). Dark: luma under 0.25; grey: saturation under 0.15. The terminal gets
the summary, the ten least finished and the pairs most alike by colour histogram; the whole table goes to
`look-report.txt` next to the DLL.

- 328 blueprints, 27,855 steps; **88 with no paint**; 59 % of steps painted; median 7 colours a painted blueprint,
  15 of one colour only.
- **79 % of paint uses are dark or grey**; the ten most used colours are all dark or grey (#7d8388, #3b3f44, #2a2c30 ...).
- **Finish: 1 blueprint** (TitanWalkerFitted, 3,115 steps). **Lamps: 4 blueprints** have a lamp part; 62 have a lamp or
  a lit bay.
- 352 of 53,628 pairs overlap 0.90 or more by colour; different families at 1.00 include Pogo Buggy / Wishbone Buggy,
  the fuel trailers of four factions, Deck Plane / Light Plane and Starter Sawmill / Starter Smelter.
