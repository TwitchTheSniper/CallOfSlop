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
- **Buildings and bases have no style guide yet**: the user is deciding it. Use the rules above.

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
