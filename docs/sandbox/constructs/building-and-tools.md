# Building and tools

Covers the configure tool, fusing, free placement, painting, the remover, snappy/socket/point/freeform attach, building sounds. Part of the construct guide: index in ../CONSTRUCTS.md.

## The configure tool

Board item dev-sandbox-configure-tool-ct01. The configure tool (the tool wheel's SE segment, or the inventory's Tools
tab) changes a part's **settings**. With it out, the look line names the part and how many settings it has; **Fire**
on a part opens its **settings panel** (the mouse comes free for it); **Shift+Fire** adds a part to (or takes it out
of) a selection of parts of the same kind on the same construct, which the panel then changes together; **Aim** clears
the selection. The panel closes on Esc, its Close button, a click away from it, putting the tool away, sitting, dying,
the part going, or walking more than 10 m from it.

### The panel

`Source/UI/UiSandboxConfig.cs`, drawn like the inventory (the same window style; readable at any UI scale):

- **Anchored beside the part**: the card sits to the right of the part on screen (to its left near the edge), and
  follows it as you move; with the part off screen it sits at the right edge.
- **A plain window, not a menu**: no dim behind it (`Ui.SceneDim`; the parts inventory doesn't dim either, the pause
  menu still does), the match runs on, the part keeps its outline and the previews show; its background is slightly
  see-through. **Drag it** by the title bar or any empty area (kept fully on screen): once moved, every panel opens
  where you left it for the rest of the session; **Snap back** (or a double-click on the title bar) puts it beside
  the part again.
- **Header**: the part's tile (the inventory's picture of it), its name, the construct (number, and whose: yours,
  nobody's or the owner's name), the part number and a health bar.
- **Settings, grouped** as the part file groups them (Timing, Light, Motor...), each with a plain label (units in the
  value: "2.5 s", "45 deg", "60 %"), a tooltip (what it does, its range and default) and a small `x` that puts that one
  setting back to its default. The controls by type: **number**: a slider (Ctrl+click it to type) and a numeric box
  (Enter sets it), held inside its range and on its step; **choice**: a dropdown; **toggle**: a check box; **colour**: a
  colour picker and eight swatches; **key** (an input reader's key): "Press to bind" then the key itself (only keys a
  seat passes on: fire, aim, reload, jump, sprint, forward, back, left, right; Esc cancels), or the dropdown.
- **Wiring**: every wire into and out of the part ("In from: Switch (part 3)"); hovering a row highlights the part at
  the other end in the world, clicking it opens that part's settings.
- **Reset** (every setting to its default), **Copy** and **Paste** (only onto a part of the same kind; the button names
  the kind it holds), **Change all N selected** (on by default with a selection) and **Apply to selected** (every value
  shown onto all of them).
- Changes go to the server as they are made: a dropdown, toggle, swatch or typed number at once, a dragged slider at
  most ten times a second and when it is let go. The server's values flow back into the panel (a value just sent is
  kept until the server's answer arrives or 0.6 s pass). Nothing is allocated per frame while it is open (every text is
  made once; the health and wiring lines again only when they change).

**Live previews** in the world while the panel is open (`Source/Render/ConstructRenderConfig.cs`, seen through parts,
following the panel's values before the server answers): the part tinted purple (and the selected ones fainter), its
wires (green in, orange out) and the parts at their ends; a **lamp** lit in the colour and brightness being picked; a
**piston's** travel from its pulled-in to its pushed-out end with a ghost of the leaf at full travel; a **hinge's**
limits as an arc about its pivot, with a line where it is now; a **proximity sensor's** range as three rings.

### Settings are data

Each part file lists its settings in `"config"` (up to 8), so a new part gets its panel by itself:

```json
"config": [
  { "key": "period", "label": "Period", "group": "Timing", "unit": "s", "min": 0.05, "max": 60, "step": 0.05, "tooltip": "How long one on-and-off cycle takes." },
  { "key": "startOn", "label": "Start on", "group": "Timing", "default": false, "tooltip": "..." },
  { "key": "colour", "type": "colour", "default": "#ffed99" },
  { "key": "mode", "options": ["both ways", "switching on", "switching off"], "default": 0 }
]
```

| Field | Meaning |
| --- | --- |
| `key` | What the setting means to the game (below); a key the game doesn't read, or one the part's kind doesn't use, refuses the file with the reason. |
| `type` | `number`, `choice`, `toggle`, `colour` or `key`; by default from the key (`startOn`, `latched`: toggle; `colour`; `mode`, `operator`, `units`, `setting`: choice; else number). |
| `min`, `max`, `step` | A number's range and step (0: any value). |
| `default` | A number, true/false, a colour, or a choice's option (name or index). Without it: what the part did before (a joint's limits, speed and strength; a suspension's spring), else 0. |
| `unit`, `label`, `group`, `tooltip` | What the panel shows (ASCII; the group defaults to "General"). |
| `creative` | `true`: set in creative worlds only. The server refuses it in survival, the panel shows it read-only and a survival world leaves a blueprint's value out. |
| `options` | A choice's 2 to 16 names. |

**Folded in.** A part's own choice from before (`ConstructPartDef.Settings`: an input reader's key, a manual gearbox's
starting gear, a gear selector's positions, a lockable differential's lock, a transfer case's 2WD/4WD, an adjustable
suspension's presets) is a `setting` entry by itself (a dropdown; the reader's a key field), kept in the part's setting
byte as before, so the driver's shift keys, the HUD and the wiring work unchanged. A timer's, delay's, compare's,
scale's or proximity sensor's old `settings` list are now **presets** of its number (period, seconds, threshold,
factor, range): the linking tool's R steps through them as before, and the panel sets any value in range.

| Key | Parts | Does |
| --- | --- | --- |
| `period`, `duty`, `startOn` | Timer | Seconds a cycle; % of it on; each run starts with the on part |
| `seconds`, `mode` | Delay | How long; what waits (both ways, switching on only, switching off only; the other follows at once) |
| `operator`, `threshold`, `hysteresis` | Compare | > >= = <= < against the threshold; once on, it holds until the sum is that far back |
| `factor`, `offset`, `low`, `high` | Scale | sum x factor + offset, held between low and high |
| `latched`, `hold` | Button, Switch | Push on, push off; or on for `hold` seconds after a press |
| `value`, `snap`, `low`, `high`, `spring`, `rest` | Analog Lever | Where the handle stands; the step it clicks through; what it puts out at 0 and 1; goes back to `rest` when let go ([The Analog Lever](self-tests-and-history.md#the-analog-lever)) |
| `startOn` | Switch, Toggle | A new one starts on |
| `radius` | Proximity Sensor | Its range (a sentry turret aims within it too) |
| `units`, `threshold` | Speed Sensor | m/s, km/h or mph; above 0, on/off at that speed instead of the number |
| `colour`, `brightness`, `blink` | Lamp | Its glow's colour and brightness; flashes a second while lit |
| `pitch`, `volume` | Horn | Its beep |
| `min`, `max`, `speed`, `strength` | Piston, Motor Hinge, Hinge (limits), Turntable (speed, strength) | Limits (degrees; a piston's metres) inside the part's own, speed, force or torque. A turntable's or motor hinge's speed runs from stopped to twice its rated speed, the rated speed (the default) in the middle; the panel's slider shows it as -100% to +100% of rated (`CConfigDef.Rated`), the box beside it in deg/s, and the stored value stays deg/s, so older saves and blueprints keep their speed |
| `pushable` | Passive hinges | A player walking into what hangs on it swings it (on by default). [Doors](big-systems.md#doors) |
| `strength` | Brake | 100%: the wheels lock; less: they drag (`jFwd` blended towards a stop) |
| `stiffness`, `damping` | Suspension (a plain spring) | Spring frequency (Hz) and damping ratio |
| `friction`, `dirtGrip`, `sideGrip`, `rolling`, `bounciness` | Every wheel, track and tyre, without a line in its part file | The tyre: [Wheels](drivetrain-and-engines.md#wheels-the-rim-and-the-cylinder) |
| `fluid`, `fill` | Fluid Tank | What it holds (none, or any fluid: the options are filled in from the game's list) and how full it is, %. Creative only ([Fluid tanks](power-and-links.md#fluid-tanks)) |
| `setting` | Input Reader, gearboxes, selectors, differentials, transfer cases, adjustable suspensions | Their own choice, above |
| `rackLayout`, `rackRows`, `rackColumns`, `rackLength`, `rackDiameter` | Ordnance Rack | Its shape: grid or drum, rows (layers) by columns (segments), the longest and widest ordnance a station takes; setting one swaps the part for that shape ([Ordnance racks](weapons.md#ordnance-racks)). The old Modular Bomb Rack's `stations` and `rackSize` are retired |
| `fireOrder`, `rackInterval` | Ordnance Rack | One at a time or a salvo; seconds between releases |
| `walkProfile`, `stride`, `stepLift`, `bodyHeight`, `gait`; `legPace`, `legLift`, `legSpread`, `legGroup` | Walker legs | The gait profile (Simple, Basic, Advanced) and its settings: the walker reads its lead leg's (the highest profile), each leg's own under Advanced (docs/sandbox/GIANTS.md, phase 3) |

Values live on the part (`CPart.Cfg`, eight floats inline, so they move with the part when parts are renumbered or a
construct splits; a colour is 0xRRGGBB, exact in a float), are read through `ConstructConfig.Get` (a table lookup, no
allocation), start at the defaults when a part is fitted, and are clamped wherever they are set. A part taken off,
stored or picked up loses its settings (it goes back to the inventory as a kind).

### Reach

A part's numbers are in the frame of its segment, and a freeform-welded part keeps the numbers of the piece it came from, so a point on a part is `Construct.SegToWorld`, never `ToWorld`. `ConstructEdits.ReachPoint` (take off, use), `AttachReach` (fitting a part: the server's check and the client's ghost, which turns red and says "too far away" rather than offer what the click is refused) and `WeldReach` (the welds) are the ways to measure it, and take the eye from the world, in or out of a ride frame.

### Server, permissions and the network

The client asks with `CActKind.Configure` (protocol 89; [Network](network-and-scale.md#network-protocol-82-84-for-free-placement-86-for-the-drivetrains-rpm-87-for-glass-shattering-88-for-the-physics-gun)); the
server (`ConstructEdits.Configure`, `Game.SandboxConfigRefusal`) checks the configure tool is out, the player isn't
seated, every part is of one kind on that construct (at most 32), the nearest is within 8 m, decision 19's shared
contested rule (`ConstructEdits.Contested`, the physics gun's: in survival an enemy construct with its owner alive near is
"defended"), and **who may**
(decision 19; a [faction](overview-and-playing.md#factions) mate's count as your own): your own constructs, nobody's (the map's), wrecks and loose parts;
someone else's is refused, "Owned by NAME - defended" while its owner is alive within 40 m (plus the construct's size),
"Owned by NAME: only its owner can configure it" otherwise. It clamps every value, rebuilds a joint's limits, speed and
strength, and sends the construct's structure again (the settings ride in `CBody`). A refusal comes back as the usual
notice.

### Self-test

"Construct configure" (`SelfTestConstructConfig.cs`): every configurable part's schema is valid (slots, ranges,
defaults, options, presets on their step; a new part at its defaults, a joint's default speed its own) and the parts that
should have settings do; bad schemas are refused with a reason; values clamp (range, step, NaN, toggles, colours,
choices); each setting changes behaviour (a timer's period doubles its pulses and its duty sets its on time, a delay's
mode, a compare's operator, a scale's offset and limit, a brake's strength, a latched button and a hold time, a speed
sensor's units and threshold, a proximity sensor's range, a piston's travel, a motor hinge's limit and speed); the
server's checks (the tool, the reach, one kind, a missing slot, someone else's defended or not, your own and nobody's
allowed, several parts at once); a blueprint's `"config"` builds, is written back (`ConstructConfig.ToJson`) and builds the
same, and the construct message carries every setting; the panel headless in a game (values sent as the host, the
preview, copy and paste between lamps, refused onto a horn, reset, a selection changed together, no allocation over 60
open frames, refused with the tool put away); and over the real transport a client's change reaches the server and its
own copy, and the host's construct with the host beside it is refused as defended.

### Choices made (the developer may change them)

- Fire (not Use) opens the panel: Use keeps working buttons, switches and doors with the configure tool out.
- Changes apply live (no Apply button for a single part): the previews and the construct follow at once.
- Permissions follow decision 19: someone else's construct is refused whether or not it is defended (owner only for
  now); only the wording differs. Wrecks, loose parts and nobody's constructs are open to all.
- The configure tool has no number key (3, 4 and 5 are taken; the wheel and the inventory have it).
- Settings aren't kept by a part taken off or stored, and constructs aren't saved with the world yet; blueprints carry
  them.
- The panel's key field takes only keys a seat passes on to input readers (they are the only keys the server sees).
- Not done: a pressure plate's "who triggers" and weight, a proximity sensor's targets and line of sight, a lamp's light
  range (lamps light their surroundings now: look.md), a seat's exposed keys, a lever's positions (the game has only players to detect, no
  lamp lights, and seats pass on a fixed set of keys). Controller support waits for Steam Input.

## Fusing

The user's design (the project doc `claude/part-merging.md`): fusing is for performance. A group of parts is fused into one
**unit**, and the engine treats the unit as one thing where it can, while every part inside stays a real part.

### The fuse tool

`ConstructTool.Fuse` (wire byte 17), on the tool wheel after the texture paint tool (thirteen segments; Hands still straight
down). Code: `Source/App/Program.SandboxFuse.cs` (input), `Source/Constructs/ConstructEditsFuse.cs` (the server),
`Source/Game/GameSandboxFuse.cs` (the request, `CActKind.Fuse`), `Constructs/Tools/FuseTool.json` (the model).

- **LMB** fuses. With a Selector selection on the construct under the crosshair (two parts or more), the selection: pick the
  group with the Selector as for copy or delete (its box drag, Shift to add), then take the fuse tool; the selection stays
  outlined while the fuse tool is out. Without one, the aimed part's whole rigid piece: everything linked to it on its own
  segment. Parts already in a unit join the new one.
- **RMB** un-fuses the unit under the crosshair: free, at once, and the parts and links are exactly as they were (nothing was
  ever taken away from them).
- The unit under the crosshair is outlined orange (up to 2,048 parts), and the look line says "fused, N parts".
- A selection that isn't one linked piece is fused as its linked groups ("fused 300 parts into 2 units"); a part alone is
  left as it is. Every member of a unit is on one segment: parts across a joint or a freeform weld can't share a unit.
- The rules are the paint tool's: no seat, the tool out, the aimed part in reach, the contested-construct rule. Free and
  instant in creative and survival.

Choices made (the developer may change them): the Selector picks groups (rather than a stroke of the fuse tool's own); a
click with no selection fuses the whole piece; un-fuse is RMB rather than a ring option.

### What a unit is

The truth is a tag on every part, `CPart.Fuse` (a `uint`, 0: not fused; the server gives new ones, `ConstructFuse.NewTag`).
It moves with the part through every compaction, split, merge and weld. Everything else is a cache made from the tags at each
rebuild (`Construct.FuseRefresh`, `Source/Constructs/ConstructFuse.cs`): the units, their members, and per unit a rough
collider and a share of the mass sums. A unit is the parts with one tag, on one segment, linked to each other.

- **Mass.** A full rebuild of a big body (the sums of [Rebuilds](overview-and-playing.md#rebuilds)) adds each unit's cached share of the sums
  for its plain parts (frames, panels, wedges) instead of summing them; the share is summed again only when the unit changed.
  The result is the same to the last bit (mass, centre of mass and inertia were compared on every scene in the bench).
  A journalled edit takes a part's own share off as before. Bodies under 1,024 parts sum as they always did.
- **Collision.** The collider ([Compound collision](network-and-scale.md#compound-collision)) has the unit's **rough boxes** in place of its
  plain parts' own leaves, for construct against construct and against the map and the ground. Special parts in a unit
  (wheels, scoops, guns, anything with a role) keep their exact leaves, so a wheel still rides on its rays and cylinder.
  A rough leaf stands for the unit's first plain member (friction and no-collide pairs are a plain part's).
- **Exact everywhere else.** The part tree is untouched, so rays, bullets, blasts and damage find the exact piece; players
  walk, stand and ride on the exact parts (decks, slopes, doors, rooms); building, ghosts and sockets see the exact parts.
  Every part keeps its own health and breaks off on its own.
- **Systems.** Every system already walks only the special parts (`Construct.Specials`), so the plain parts of a unit cost
  the systems nothing, fused or not, and the special parts inside a unit (engines, wheels, guns, logic, links) work
  exactly as before. Summarising them (a unit's combined thrust, drive or power) is not done: there was nothing left to save
  on the plain parts, and the special parts are few.
- **Drawing.** A unit's parts are merged-mesh chunks of their own (`ConstructRenderBatch.Plan`: the unit's tag in the
  chunk key), so an edit beside a unit never plans its chunks again ([MERGED-MESHES.md](../../render/MERGED-MESHES.md)).
- **Uses.** A unit that is a body of its own is moved as any body: the physics gun, a cargo volume, socket and point welds of
  the whole body (its tags come along). `Construct.IsOneUnit` says when a body is one unit. It never goes into the hands or
  the inventory (a fused unit is not a custom part: not wanted for now).

### The rough collider

`ConstructFuseFit.Fit`, in the unit's segment frame, when the collider is next built (so a client never fits one):

1. The plain members' solid boxes (the collider's own: `SolidCount`/`SolidBox`, wedges by their hidden boxes) are marked on a
   grid of cubes over their bounds, at most 131,072 cells (the cell is the bounds' volume over that, cube-rooted, at least
   5 cm). A cell is full when any box reaches into it.
2. Full cells are merged greedily into boxes: along x, then whole rows along y, then whole slabs along z, in a fixed order,
   no side longer than 16 m (`MaxSide`). Past 1,024 boxes the cells grow 1.6 times and it starts again.
3. Each box is shrunk to the union of the real boxes clipped to it. The boxes together still cover every real box
   (conservative: nothing real is outside), and a face the real boxes reach is where they are, so a flat deck rests on its
   own underside, to the millimetre.

Deterministic (the same parts in the same order give the same boxes). **The error** is a cell: a point of a rough box is
within one cell diagonal of real geometry (a partly filled cell, the steps of a wedge), and a hollow narrower than a cell is
filled. Measured: a 16,000-block sheet, 43 boxes, cell 28.5 cm, rough volume 1,990.3 m3 against 1,990.0 m3 of boxes; a
15,600-part capital hull (decks and plates, 300 by 416 m), 609 boxes, cell 1.83 m, 524,160 m3 against 232,148 m3 (its
plates stand at several heights, so its rough top is the highest plate's). A sheet on half-metre ground came to rest at
628 mm against 629 mm unfused. The 16 m limit is from a measurement: a 300 m hull fitted as one box stood on four corner
contacts and sank through a floor its parts stood on; with the limit it rests as its parts do.

Cost: 3 ms for the sheet, 6 ms for the capital hull (container, loaded). It runs when a unit is made or grows, after a split,
and when an eighth of its members have gone since the last fit; until then the rough shape stays as it was: loose round
what went, never short. **A fit is a one-off cost in a tick** and can pass the 8 ms budget for the biggest units on a slow PC.

### Damage, splits and rebuilds

- **Batched.** Removals only mark the body; the unit is looked at in the tick's one rebuild, so a unit is rebuilt at most
  once a tick however many of its parts went.
- **Splits.** A part going is looked round (`FuseNoteRemoval`, before the arrays are compacted): if its unit's members linked
  to it still meet each other within 512 parts over members, the unit is whole and isn't flooded. Otherwise (or when a unit
  that isn't the whole body lost parts some other way) the refresh floods it: each linked group of two parts or more stays
  fused as a unit of its own (the largest keeps the tag), and a part left alone is an ordinary part again. A ship cut in half
  is two units, in one body or two.
- **The journal.** Fused parts are plain parts to the journal, so their edits stay journalled. A unit that lost members has
  its collider built again (cheap: a few hundred leaves), not remapped.

### Saves, blueprints and the wire

- **Wire (protocol 193).** A Full ends with the fused units as runs of parts (first part, length, tag). Fusing, un-fusing and
  splits are structure changes: the body goes out again (a big body as a patch on its last Full).
- **World saves.** A part's unit is a part setting an older reader skips, `fuse:unit`, numbered 1, 2, ... in its body;
  loading gives new tags.
- **Blueprints.** A step's `"fuse": n` (1, 2, ...: the steps with one number are one unit), written by Save as blueprint and
  the clipboard, given new tags at each build. A paste of a copy gets new tags too.

The bench's round trips: two units (300 and 240 parts) on one sheet came back as 300+240 through a blueprint, a client's Full
and a world save, and a strip cut across one of them left 144 on the hull and 240+144 in the piece that fell away.

### Measuring

`--bench-fuse [PARTS] [--capital] [--stack] [--sheet]` builds each scene plain and fused and prints min / median times
(`Source/App/BenchFuse.cs`, a command-line benchmark, not a self-test section). `--bench-fuse-net [LAG] [LOSS]` runs a host
and a client over RakNet on one PC with the client's network simulated bad, and drives the client's own fuse requests. The
numbers are in [LIMITS.md](../../perf/LIMITS.md), "Fused units".

## New constructs (free placement)

Board item dev-sandbox-new-construct-nc01 (the developer's note, 2026-10-29): a construct no longer only comes from a
blueprint or the map. Holding any placeable part with a tool out, while not aiming at a construct's socket, shows a ghost
of the part on the **surface you aim at** (terrain, the map's boxes, a floor or deck of the map); holding fire with the
right tool for the part (decision 2: attach tool for frames, panels and foundations, attach tool for bolted parts) sets it down
there as a **new construct rooted at that part**, with its own sockets to build on (`Source/Constructs/ConstructPlace.cs`;
milestone 1's "set it down on the ground", which took only a yaw, is what it generalises: the same `CActKind.PlaceNew`
and `EditKind.PlaceNew`).

- **The pose**: the construct's up follows the surface's normal, except on near-level ground (within 12 degrees), where
  it stands upright and rests on the highest ground under its footprint's corners (a bump under one corner lifts it, so
  it never starts inside the terrain). Its yaw is the look's. The **arrow keys** turn the part through its 24 turns
  (`Rot24`) relative to the camera and **R** puts it back upright ([Turning the ghost](overview-and-playing.md#turning-the-ghost)). On a wall it
  stands out of the wall.
- **The grid** (the inventory window's "Snap new constructs to the grid", off by default): the spot snaps to a 0.5 m world
  grid across the surface and the yaw to 90 degrees, so constructs set down apart line up (a block set down next to
  another one meets it face to face).
- **Green or red**: the ghost is green when it fits and red, with the reason in the look text, when not: inside the ground
  or a wall, someone standing there, another construct in the way (every part box of every construct near it, turned
  boxes tested exactly), no surface, too far, or the limit reached. The server checks again when the placement starts and
  when it is done (something may have moved there in the 1.5-2.5 s it takes).
- **Free or anchored**: a placement is **free** (physics: it rests on what it was set on and falls if that goes) unless the
  part is a **foundation** (anchored as before) or creative's **"Anchor new constructs to the ground"** toggle (the
  inventory window; only on maps with infinite parts: elsewhere build on a foundation) **pins** it: `Construct.Pinned`, held
  still like a foundation, sent in CBody's flags.
- **The Construct Core** (`Constructs/Parts/ConstructCore.json`, `"core": true`): a 0.5 m orange attached block with a socket
  on each face, in the inventory's Structure group: the obvious starting point. A core is never a loose part (it can't be
  picked up with F; a single dropped Frame Block can) and is the root after seats and foundations.
- **Building onto a wreck or a loose part** (a dropped part, the map's loose parts) counts as building on that construct:
  the ghost sockets show on it, and the first part fitted **claims** it (no longer a wreck, the builder's own, so it no
  longer clears away as debris).
- **Joining two constructs**: with the attach tool out, aiming at a part whose free structural socket meets a free structural
  socket of another construct (face to face, within 3 cm, both on their main bodies rather than beyond a joint, the two
  bodies square to each other) shows "touches construct N - F to attach the two into one"; **F** joins them
  (`CActKind.Join`, `ConstructWorld.Merge` in `ConstructJoin.cs`). The other construct's parts move into this one's frame
  (each takes one more Rot24 turn, so nothing bends), with its joins, joints (their pivots and axes turned), wires, fuel
  and charge; every other socket pair the two meet at joins too; a coupling between them goes; a wire between them becomes
  one of its own. Both must be yours, or free for anyone to take (a wreck, a loose part, the map's): someone else's
  working construct can't be joined. The one aimed at stays (unless only the other is yours) and becomes yours. Refused
  while anyone sits in either, or past the caps ([Performance and caps](parts-and-data.md#performance-and-caps)).
- **Ownership and limits**: what a player sets down (or claims, or joins) is theirs (`Construct.Owner`; shared with your [faction](overview-and-playing.md#factions)).
  A player may own **12** constructs (wrecks and loose parts don't count); past that a new one is refused ("take one apart
  or join two"; the host may change the number). The world has no fixed caps since board item cl01, only the host's
  safety cap ([Scale](network-and-scale.md#scale-thousands-of-constructs)).
- **Network** (protocol 84): `CAct.PlaceNew` carries the surface point, its normal, the yaw, the turn and two flags (grid,
  anchor); the server finds the surface again under the point itself. `CAct.Join` names the part. CBody's flags carry
  "pinned". Everything else (the new construct, the merged one, the one that went) travels as before.
- **Cost**: the client runs the pose, the fit check and the join search every frame while a part is held: fixed loops over
  the bodies near it, no allocation (the self-test measures it).


**Self-test**: the **Construct placement** section (`Source/App/SelfTestConstructPlace.cs`): the Rot24 helpers (compose,
quaternions, nearest) and the rotate key's order; the pose on level ground, on the grid and on a wall; a beam turned on its
side joined to a block keeps its place in the world; in an offline game on the yard the core set down on open ground is a
one-part construct of the player's with six sockets to build on, a Frame Block attaches to it; placing over a construct,
over a player, in the air, or anchored outside creative is refused with the reason; a free block rests on the ground and
moves when pushed while a pinned block and a foundation don't; building onto the map's loose Frame Beam claims it; a block
set down beside the first construct is found to touch it and joined (every part reaching the root, the other gone); at 12
constructs a new one is refused; the placement checks allocate nothing; and over a real loopback a client's pinned core
is pinned and theirs on its copy, and the block it then joins in is gone there too.

**Choices made (the developer may change them)**: the level snap is 12 degrees; the grid is 0.5 m and off by default;
the limit is 12 constructs a player; joining is instant (the attachment, no timer) and on F with the attach tool; anchoring without a
foundation is creative-only; the core is a frame-role part with a flag rather than a new role; placing on top of another
construct's surface without a socket isn't offered (only map surfaces).

## The paint tool (board item pp01)

Decision 29. A spray can on the tool wheel's north-west segment (it took the hand drill's place: the wheel keeps eight
segments and the compass-point flicks of the other tools stay where they were). With it out, **LMB** paints the part (or
routed link: a pipe, cable, shaft or chute) under the crosshair; **hold LMB and sweep** to paint every part the crosshair
passes over, each once per stroke (`PaintStroke`: a set of what is done, cleared when the button goes up, so a click
paints one and a part is never acted on twice); **RMB** is the eyedropper (it reads this PC's copy of the part: its paint,
else its own main colour; nothing is sent) and puts the colour in the can.

**The ring round the tool** (`SandboxPaintWheel`, an `IToolOptions`): the modes **Part** (the default), **Whole
construct** (every part and link of the aimed construct; in survival the first click asks for a second within four
seconds) and **Reset colour** (back to the part's or link's own colours); ten swatches (orange, black, white, grey, rust,
olive, sand, red, blue, yellow); **Custom...**, which frees the mouse and opens a small ImGui picker (hue, saturation and
value, the recent colours, Use / Close; Esc closes it); and **Recent colours**, whose third-tier arc holds the last eight
colours used (moved to the front when used again). Letting go on the tool itself keeps the last mode and colour. The
recent colours are kept for the session only (nothing is saved).

**What takes paint.** A part's colour is `CPart.Paint` and a link's `CRoute.Paint`: a `uint`, 0 for "own colours", else
`0x01000000 | 0xRRGGBB` (`ConstructPaint`). It moves with its part or link when the arrays close up, split or join. A part
file's box says `"paintable"`: by default every box is, except **glass** and a **wheel's tyre** (a wheel's solid boxes;
its rim details take paint); the Lamp's lens says `"paintable": false`. A painted part's paintable boxes are drawn in
the paint times their **tone** (the box's brightness against the part's main colour, kept between 0.65 and 1.3), so a
hinge's dark pin stays darker than its plate; the other boxes keep their colours. Frozen and overloaded tints, damage
shading and a lit lamp's glow work as before. A painted link's segments, elbows and caps are drawn in the colour (a
pipe's bands keep theirs). Cargo volume outlines and stored items are not painted. **World links are not paintable yet.**

**Free and instant** in both modes; the server (`ConstructEdits.Paint`) checks the paint tool is out, no seat, the reach
and decision 19's contested-construct rule (`Contested`: survival, an enemy construct while its owner is alive nearby).
A part with nothing that takes paint says so.

**Network (protocol 100).** `CActKind.Paint` (construct, mode | 4 for a link, part or link index, r, g, b) at the end of
`CActKind`; the server sends changes in `CBodyKind.Paint` (at the end of `CBodyKind`: construct, kind, index, colour; at
most 150 a message, flushed with the structure each tick), and `CBody` Full ends with the painted parts and links (so a
late joiner, a structure resend and a copy made for a save have it). Nothing allocates while painting. **Blueprints**:
a step and a link take `"paint": "#rrggbb"`.

**Self-test** "Paint tool": the wheel's segment and ring; only paintable boxes change (pane, wheel, lamp, panel); the
eyedropper; a link; whole-construct fill and Reset; the stroke acts on each swept part once; the refusals (tool, reach,
seat, bad colour, a defended construct in survival); the construct message and the change messages; the blueprint
round trip and bad colours; paint stays with its part when another is taken off; no allocation; and a real host with a
separate client and a late joiner agree.

### Choices made (the developer may change them)

- The paint tool took the hand drill's slot (north-west) instead of adding a ninth segment, which would have moved every
  other tool off its compass point. (The ground drill later needed a ninth segment all the same.)
- Reset acts on one part or link (hold and sweep to reset several); Whole construct paints, it does not reset.
- Painting is free in survival (a paint item could come later); only the whole-construct mode asks twice there.
- A box keeps its tone against the part's main colour instead of going flat.
- The recent colours are not saved between sessions.

## The texture paint tool

User request (2026-11-07). A roller beside the paint tool on the tool wheel (`ConstructTool.Texture`, byte 16; model
`Constructs/Tools/TexturePaintTool.json`). It puts a **shared material** (a row of `Materials/Surfaces.json`,
docs/render/MATERIALS.md) on parts: **texture and colour are separate**, so a textured part keeps its own colour or its paint,
and the colour paint tool keeps a part's texture. Code: `ConstructFinish.cs` (data and wire), `ConstructEditsFinish.cs` (the
server), `GameSandboxTexture.cs` (the request), `Program.SandboxTexture.cs` (input), `SandboxTextureWheel.cs` (its ring),
`UiSandboxTexture.cs` (the list), `Source/Render/SurfaceThumbnails.cs` (the swatches).

- **Keys**: **LMB** puts the roller's material on the part under the crosshair; **hold LMB and sweep** to texture every part
  passed over, each once per stroke (the colour paint tool's `PaintStroke`: a stroke never acts on what it already did, and a
  stationary press acts once); **RMB** picks up the aimed part's material (its texture, else what its first box is drawn
  with; read off this PC's copy, nothing is sent); **R** opens the list.
- **The list** (R, or "Textures..." on the ring): every material the file offers, each a swatch drawn by the real box shader
  on a grey block (so a swatch is what a part will look like; drawn a few a frame and kept for the session), with its name, a
  search box (name or category) and the categories as buttons (All, then each row's `"category"`: Metal, Painted, Other, Wood,
  Stone). Clicking one puts it on the roller and closes the list; Close or Esc leaves the roller as it was.
- **The ring** (`SandboxTextureWheel`): **Part** (the default), **Whole construct** (every part; in survival the first click
  asks for a second within four seconds), **Reset texture** (back to the part's own materials), **Textures...** and **Recent
  textures** (the last eight used, each drawn with its swatch, as the third tier).
- **What is offered**: rows from 7 up (0 to 6 are the old texture layers) whose `"offer"` isn't false (glass isn't: a pane is
  blended and drawn apart). Seven rows were added for the tool: tread plate, plated steel (panels fitted to each box, seams
  only between whole panels), painted plating, matte paint, gloss paint, chrome and carbon weave.
- **Per part for now**: `CPart.Finish` (a byte, 0: its own materials) is the row every box of the part is drawn with, glass and
  glowing boxes aside (`ConstructFinish.Takes`), in place of its own row and of the row paint would turn it to. A per-box (or
  per-face) texture would be a short list of (box, row) beside the part, carried by name the same way, with the tool aiming at
  the box the crosshair's ray hit; the merged meshes already hold a material per box slot, so drawing it costs nothing more.
- **Never a row number outside this machine.** The byte is this build's own (rows are worked out at load). The wire, saves and
  blueprints carry the material's **name** (at most 32 ASCII characters, checked when the file loads); a name this build
  doesn't have reads as "its own materials". Saves: a part's texture is a setting `"finish:<name>"` in the body's settings
  list, which an older build skips (no format change). Blueprints: a step's `"finish": "tread plate"`. Copy and paste keep it.
- **Free and instant** in both modes, with the colour paint tool's rules (`ConstructEdits.Finish`): the tool out, no seat, the
  reach, decision 19's contested-construct check; a part with nothing that takes a texture says so.
- **Network (protocol 192)**: `CActKind.Texture` (construct, mode, part, the name) at the end of `CActKind`; the server sends
  changes in `CBodyKind.Finish` (at the end of `CBodyKind`: construct, part, name; at most 100 a message, after the paint
  changes each tick), and a `CBody` Full ends with the textured parts (a late joiner, a structure resend, a big body's patch).
  No string is made either side: names go as ASCII bytes and are matched against the table. Nothing allocates while painting.
- **Drawing**: a texture change is a look change. A merged chunk's look hash takes `Finish`, so only that chunk's looks are
  written again (docs/render/MERGED-MESHES.md), never its geometry. On Materials Low a texture draws the classic way, as every
  material does.
- Tried by hand (2026-11-07): a host and a client over RakNet with 100 ms and 3% loss on the client: part, whole construct,
  reset and a late joiner agree; the save, blueprint and Full round trips; the merged pictures with textures changing every
  frame match the unmerged ones (1 pixel of 230,400, one level); the list and swatches in a real window on both GL paths.
  The smoke test textures its row of parts with every offered material and opens the list.

### Choices made (the developer may change them)

- R opens the list (no new key to bind); RMB is the texture eyedropper, as RMB picks a colour with the paint tool.
- Every box but glass and glow takes the texture, tyres included (a steel tyre is the player's choice).
- An explicit texture wins over the row paint would choose: chrome painted red stays chrome-shiny under a red tint.
- The no-collide tool moved from the north-north-west to the south-south-east to keep hands due south on a twelve-segment wheel.
- The recent textures are not saved between sessions.

## The material painter

User request (2026-11-07): "to complement the texture painter, add a material painter as well". The texture paint tool changes how
a part **looks** (a shared surface, `CPart.Finish`); the material painter changes what it **is made of**: a steel plate becomes a
wooden or a stone plate of the same shape, lighter or heavier, weaker or stronger, salvaging as wood or stone, drawn in that
material. `ConstructTool.Material`, byte 18; model `Constructs/Tools/MaterialPainter.json`. Code: `PartMaterials.cs` (the parts
and the wire), `ConstructEditsMaterial.cs` (the server), `GameSandboxMaterial.cs` (the request), `Program.SandboxMaterial.cs`
(input), `SandboxMaterialWheel.cs` (its ring), `UiSandboxMaterial.cs` (the list), `BenchMaterial.cs` (`--bench-material`).

- **What a material is.** A part file's `"material"` (steel, wood, stone, concrete...) was already the part's physical material: it
  picks the salvage class (Recycling.json), the build sounds, whether it burns and its surface rules; mass, health and armour are
  the file's own numbers. A material's physical numbers already existed for extrusions: `Constructs/Extrusion.json`'s
  `"materials"`, per cubic metre (`unitsPerM3` times Recycling.json's `kgPerUnit` is its density, `healthPerM3`, `armourPerCm` up
  to `maxArmour`, `class` for salvage). The painter uses those rows: no second table.
- **Which materials, which parts**: Extrusion.json's `"painter"`: `"materials"` (steel, metal, bronze, copper, wood, planks, laminate,
  stone, concrete; each a row of `"materials"`) and `"roles"` (frame, panel, foundation). A part can be remade when its role is
  listed and its own material is a listed row; never glass, a bomb, a material bundle, a core, a joint, or a part a machine made
  (an extrusion, a design, a rack): 1,008 of 2,211 part types today. A material Recycling.json doesn't have isn't offered.
- **The part it becomes** is a run-time type (`PartMaterials.Variant`), made the first time it is asked for, as an extrusion is:
  a copy of the file part with the same boxes and sockets, its mass, health and armour scaled by the two rows (mass by density,
  health by `healthPerM3`, armour by `armourPerCm`, at most the new row's `maxArmour`), `Material` the new row's class, its plain
  boxes in the material's colour (Recycling.json's `"colours"`) and surface (`SurfaceRules.OfBulk`; bolts, trim and glass keep
  theirs). Remade in its own material it is the file part again (**Own material** on the ring). Always physics-only (never an item:
  an inventory or a store would have to carry its key). It counts as plain structure when its file part does (journalled
  rebuilds, fused units' rough shapes). A steel plate of 0.5 m (5 kg, 70 hp, armour 20) is 1.4 kg, 25 hp, armour 0.7 in wood,
  7.5 kg, 42 hp, armour 6.7 in concrete (`--bench-material` prints every material).
- **The swap** (`ConstructEdits.Rematerial`): the part's type changes in place. The two types have the same boxes and sockets, so
  its index, place, links, wires, settings, paint, texture and fuse tag stay and its health keeps its share. The body is rebuilt
  once (a full rebuild: the journal can't say a type changed), a fused unit sums its mass again (`FuseMassStale`; the Replacer
  does that now too), and the body goes to the clients again (`StructureDirty`), so merged meshes rebuild with the new looks.
- **Keys**: **LMB** remakes the part under the crosshair in the material in hand; **hold LMB and sweep** for each part passed over,
  once per stroke (`PaintStroke`); **RMB** picks up what the aimed part is made of; **R** opens the list. The look line says what
  the part is made of, or why it can't be remade.
- **The list** (R, or "Materials..." on the ring): each material with its swatch, density, health and armour a cm (and at most),
  what it salvages as, and for the part last aimed at what it would become (mass, health, armour, the units it takes). Click one
  to take it; Close or Esc leaves it.
- **The ring** (`SandboxMaterialWheel`): **Part** (the default), **Whole construct** (every part that can be; in survival the first
  click asks for a second within four seconds), **Own material**, **Materials...** and **Recent materials** (the last eight, as the
  third tier, each a swatch).
- **Cost**: creative is free. In survival a part takes its mass in units of the new material (mass over `kgPerUnit`, rounded up)
  out of what the player carries (`SandboxInventory`, as the ground drill's fill does), and gives back what the old part would
  salvage for at the Recycler (`RecyclingTable.YieldOf`), so remaking is never better than recycling. Not enough: "not enough
  wood: a Plate Steel 0.5 m (wood) takes 2, you carry 1". The whole construct is all or nothing. A beacon network's bins aren't
  drawn on yet.
- **Rules**: the tool out, no seat, the reach, decision 19's contested-construct check, and nobody else driving it.
- **By name, never a number.** A remade part's name is its key: "Plate Steel 0.5 m (wood)" (`PartMaterials.KeyOf`).
  `ConstructParts.Find` reads it, so world saves and blueprints keep it as any part name, and copy and paste keep the type. On
  the wire it goes in a Full as a run-time part (after `Extrusion.WireMark`, kind 246): the file part's id and the material's
  name as ASCII bytes. A name this build doesn't offer reads as the file part.
- **Network (protocol 194)**: tool byte 18, `CActKind.Material` (construct, mode, part, the material's name) at the end of
  `CActKind`, and the Full's run-time part kind. Nothing allocates while aiming; the look line is made when the aim changes.
- **The tool wheel**: its own slot (`SandboxToolWheel.Slot.Material`), off the default wheel as the Selector is (a fourteenth
  segment would move every tool off its compass point): add it in Settings > Tool wheel, or take it from Tab > Tools.
- Tried by hand (2026-11-07): `--bench-material 120 3` (a host and a client over RakNet, 120 ms and 3% loss on the client: one
  part, a swept row, Own material and the whole sheet agree on both; blueprint, Full and world save round trips keep every
  part's name; the survival cost, a refusal and the salvage back); the smoke test remakes its fused row of 80 parts in wood
  (19,200 kg to 5,376 kg) and back, with the unit still whole.

### Choices made (the developer may change them)

- Off the default wheel (see above). R opens the list, RMB is the eyedropper, as with the texture paint tool.
- Remade parts are physics-only, so a plank that was an item becomes a welded piece once remade.
- Survival pays from what the player carries only, and refunds at the Recycler's rate (no exploit, at some loss).
- Glass is not offered (a pane is blended and drawn apart), nor rubber or cloth (no row in `"materials"`).
- A part keeps its texture (`Finish`) and paint when remade: the texture paint tool still decides how it looks if it has one.
- The recent materials are not saved between sessions.

## The remover tool (board item rt01)

The developer's request (2026-10-31). A red-banded eraser on the tool wheel's south-west segment (it took the scanner's place:
the wheel keeps eight segments and every other tool stays on its compass point). Code: `Source/Constructs/ConstructRemover.cs`
(the selection and the controller), `ConstructEditsRemover.cs` (the server), `ConstructWorldRemove.cs` (the batch removal),
`Source/App/Program.SandboxRemover.cs` (the keys), `Source/Game/GameSandboxRemover.cs` (the request); model
`Constructs/Tools/RemoverTool.json`. Protocol 104.

**Selecting.** **LMB** on a part or a routed link marks it (a red outline; a link is red through the parts); LMB on a marked
one unmarks it. **Hold LMB and sweep** to mark each part passed over once per stroke (a stroke that starts on a marked part,
or with **Shift**, unmarks as it goes; a press held still marks once and stays). **Double-click** a part to mark its whole
construct (a loose body is its own construct), **Shift + double-click** to unmark it. Only what the tool may delete is marked
(`SandboxAim.Remover`: a red outline with the reason when it can't; the marks themselves are the red outlines). The HUD says
"N parts selected" (and links). The selection is this PC's (nothing is sent until the delete), kept when the tool is put
away, and dropped part by part if the construct changes under it (a mark remembers its part type and place and is checked every frame).

**Deleting.** **RMB** with something marked shows "Delete N parts? Right-click again to confirm" next to the crosshair (no
window: the game keeps running). A second RMB within four seconds sends the request. **LMB** (that click marks nothing),
**Esc**, putting the tool away, the four seconds running out, or the selection changing cancel the question and keep the
selection. **Shift + RMB** clears the selection; RMB with nothing marked says so.

**What it does.** The server (`ConstructEdits.Remover`; `CActKind.Remover`: u16 n, then per thing u16 body, u8 is-a-link,
u8 index) checks the tool, no seat, the reach, and per construct `RemoverRefusal`, all or nothing. The marked parts are
destroyed outright (nothing is handed to anyone, no scrap); routed links (and so wires) on a deleted part go too; a link
marked on its own goes alone; pieces cut off from the root split off as wrecks exactly as when one part is taken off
(`ConstructWorld.RemoveParts`: one flood, one split and one compaction for the whole batch, where `RemovePart` takes one);
a deleted container spills what it holds as loose pickups (the owner is nobody's); a whole construct (every part marked)
is removed. A merged receiver in the batch falls back to one `RemovePart` at a time (the merge settles after each).
Nobody may delete a construct someone is driving or sitting in.

**Modes.** *Creative* (infinite parts): free, instant, any construct. *Survival*: your own constructs; loose pieces and
wrecks unless their owner is defending them (decision 19, `Contested`); never someone else's working construct or the map's
("salvage it with the attach tool"). It gives nothing back. The map can turn it off for survival with
`"sandbox": {"removerInSurvival": false}` (the server refuses it: "the remover is switched off on this server").

**Performance.** 200 loose pieces and 60 parts of a 90-beam chain (260 targets) delete in one request in about 4 ms here
(the self-test's budget is 8 ms where CPU time is measured, Windows). No allocation per frame while the tool is out.

**Self-test** "Sandbox remover": the wheel segment, wire byte and model; through the controller `Program` drives, with
scripted keys and the game's own crosshair ray: click, click again, Shift+click, a stationary press, a sweep (and an
unmarking one), a double-click, Shift+RMB; the question deletes nothing and the second RMB deletes exactly the marked
parts, the cut-off end becoming a wreck; LMB, Esc, the timeout and the tool put away cancel it; a link alone and a part's
link; a chest's contents spilling; creative, survival (own, someone else's, defended, wreck, loose, the host's switch,
nothing handed back); the batch's time; no allocation; and a real host with a separate client agree.

### Choices made (the developer may change them)

- The remover took the scanner's slot (south-west) rather than adding a ninth segment, which would have moved every other tool off its compass point (as the paint tool did with the hand drill).
- Deleting is **not** part of creative's Ctrl+Z (sb01): the undo remembers placements, and putting back a construct with its links, wires and storage is not practical. Left out.
- Survival: allowed on your own constructs and on loose pieces and wrecks nobody defends; the map's constructs are refused. The host's setting is a map key (`removerInSurvival`), not a lobby setting.
- All or nothing: one refused construct in the request refuses the lot, so what was confirmed is what goes.
- A double-click needs the same part twice within 350 ms; a click on a different part is a new click.

## Building sounds

Every sound is synthesised at start-up (`Source/Audio/SynthBuild.cs`) and plays on the mixer's **Building** row (F8, key `build`), on 8 one-shot and 3 loop voices of its own (`Audio.Build.cs`), so building never takes a gun's voice. `BuildSounds.Play(kind, position, size)` is the one call: it plays on this peer only and nothing goes over the wire. Same-kind hits within 0.3 s and 7 m merge into a burst of at most 5 hits, so a paste of 200 parts is a short rattle. A clunk's size class comes from the part's mass (small under 40 kg, medium under 1.6 t, large under 63 t, else capital) and its flavour from `material`. Ticks for the ghost, node clicks and refusals play where the input knows them. Everything else comes from `Game.SandboxBuildSounds` (once a frame, the same on host and client), which watches what every peer already has: part and link counts per body, bodies appearing and going (`ConstructWorld.GoneRing`), and the hands in the state tail (edits in progress give the attach tool's arc and the attach tool's ratchet, an attachment that ends near its finish a tick, the physics gun's hold its cues). A change only sounds with a player near it holding a tool other than the physics gun, so a part shot off, a joiner's world and machines stay quiet. New bulk actions need no call. `size` is a mass in kg.

## Snappy building (decision 24, board item sb01)

Building is quick: hold a part and its connection points show, the aim is helped by a screen magnet, and a row of parts
is one sweep of the mouse. The server still decides everything; the client's ghost is only a preview.

- **Connection types** (`ConnType`, `Source/Constructs/SnapBuild.cs`): Mount (a structural face), Barrel, Muzzle, Optic,
  Feed, ShaftIn, ShaftOut, Fuel, Power, Data, Hub, Leaf. Part files name none of them: `SnapBuild` reads a type from the
  part's role, the socket's face and a gun part's category, so no part file changed.
- **Markers**: while a part is held, its own connection points are drawn on the ghost and every free socket on the aimed
  construct it can use is drawn too (not only near the aim; they fade out with distance), colour-coded by type
  (`SnapRender`, `SnapBuild.Colour`). Sockets the part cannot use are hidden.
- **Single-purpose parts** (barrel, muzzle, optic, feed part, wheel, door: `SnapBuild.SinglePurpose`) show and snap only to
  their functional sockets. The screen magnet (`SnapBuild.Magnet`) takes the cached socket nearest the crosshair within
  80 px at 1080 lines (scaled with the window height), nearest by screen distance and then depth. A barrel therefore
  cannot go on a frame face.
- **Multi-connection parts** (gearbox, engine, seat) mount by their structural face first. Where one of the placed ghost's
  functional points lines up with a free matching socket (`SnapBuild.Lineups`), the ghost marks the connection that will
  be made. **K** (Switch connection point, binds version 15) steps to the next of the part's own sockets that fits;
  F is Interact and the middle button the tool wheel.
- **Item parts only**: attach tool and physics-only parts cannot be ghost-attached from the hands (cargo volumes, decision 21),
  so snapping from the hands applies to item parts.
- **Feel**: the ghost eases to a new socket, a soft click and a flash on a new snap, a heavier thunk when the part lands.
- **Creative only** (infinite parts, and the player has asked for it with the `snap_build` setting, on by default):
  quick building fits at once and keeps the part in the hands; **hold Fire and sweep** to place the same part on each newly
  hovered socket (once per socket per press); a quick **middle-button tap** on a part picks its kind into the hands (a
  held middle button is still the tool wheel); **Ctrl+Z** undoes the last placements (8 kept per player, server-checked:
  the construct is still theirs, in reach, and the part where it was put); **L** (hold, flick, let go) is a wheel of the
  last 8 placed kinds.
- **Cost**: matching sockets are cached per held type and per construct (`SnapCache`), rebuilt only when the construct's
  signature (parts, turns, links) or the held type changes. A steady frame hashes the construct and allocates nothing.
- **Network** (protocol 94): `CAct.Snap` at the end of `CActKind` (undo, quick building on or off, pick a part off a
  construct, take a kind into the hands). No new messages.

**Self-test**: "Sandbox snap build" (`Source/App/SelfTestSandboxSnap.cs`): a barrel sees only a receiver's front and the
magnet takes it from a near aim but not far off; a muzzle sees nothing on a bare receiver; a gearbox mounts by a face and
a switch lines its shaft input with an engine's output; every part with sockets reports its points; the switch key; six
lamps with one held part; pick; undo (and the refusals); the cache rebuilds once per change and allocates nothing.

Choices made (the developer may change them): K and L are the new keys (F and the middle button were taken); the magnet
is 80 px; 8 placements are kept for undo; quick building and paint are creative-only; survival keeps one press per part.

## Centre sockets (board item cs02)

At load, a frame, panel or foundation part gets a structural socket named "centre +y" (and so on) at the centre of each large
face (both sides at least `ConstructParts.CentreFaceMin`, 0.75 m), unless one of its own sockets faces out of that face within
`CentreNear` (10 cm) of the centre, or the centre isn't solid. They are appended after the file's own sockets, so no socket
index in a link, blueprint or save moves. `"centreSockets": false` opts a file out. Parts on the 0.25 m cell lattice
(Structure, Building) get one only where it lies on the lattice; damage stages and the parts that turn into them are left
alone (they must keep identical sockets). Generated weapon parts and joints are not touched. "Centre sockets" checks all of it.

## Joint sides (board item js01)

A joint part (hinge, motor hinge, turntable, piston, steering pivot, lid hinge, every actuator size) has two sides. The **base** is
what attaches to the construct and stays with it (every socket but the leaf); the **moving side** is the leaf socket and everything
that hangs on it (it carries the joint's segment). The part files say which is which:

- **Model.** Boxes carry `"side": "base"` or `"leaf"`; the loader refuses a joint part that tags neither (`--check-construct-parts` prints
  the reason). Base: a dark body, a mounting flange and four bolts. Moving side: a safety-orange collar over the leaf half with a stepped
  arrow on it (one head and a tail tick for a turning joint, a head at each end for a piston). The orange boxes are details and not
  paintable, so paint never hides the sides. `tools/constructs/make_joint_sides.py` writes them (idempotent; `--check`): fix the script,
  not the files.
- **Markers and ghost.** With a part held, a joint's moving-side socket is an orange disc with a bar, its base sockets a steel-blue plate
  with four bolts (`SnapRender`); the held joint's ghost draws its moving-side boxes orange (dark red when it does not fit), and the HUD
  says which side mounts where ("the Frame Block will ride on the Hinge's moving side").
- **Look-at.** Aiming at an assembled joint says "Hinge - Base: fixed to construct 7 (12 parts) | Moving side: 3 parts" and, for 2.5 s after
  the crosshair lands, outlines the two groups in two colours (steel blue: the base side, orange: the parts on its segment and every
  segment hanging from it; at most 48 parts a side). With the configure tool out the moving side also swings (or slides) through the
  joint's range as a pale ghost. `JointSides` (Source/Constructs/JointSides.cs) holds the side logic; nothing in it allocates.

## Freeform attach (board item fw01, decision 25)

The attach tool has five modes on the tool wheel's outer ring round it (`SandboxWeldModes`), clockwise Rope, Freeform attach, Socket attach, Spring, Cylinder, Socket attach in the middle so a flick straight out lands on the default ([Ropes and springs](power-and-links.md#ropes-and-springs) has the two ends): **Socket attach**, the default
(held parts snap onto sockets, as before), **Freeform attach**: position a piece by hand with the physics gun at any
angle and offset, then attach it to the vehicle exactly where it is. [Point attach](#point-attach-board-item-pw01) is Socket attach with Shift held at its first step.

### Using it

- **Two clicks.** The first LMB picks a part (outlined amber), the second LMB on a part of another body joins the two at
  their current relative position and turn. No socket, no grid. The two must come within **3 m** (`Freeform.MaxGap`), measured
  between the closest points of the two parts' boxes (`Freeform.Gap`), and each must be in the usual reach of you. RMB or
  LMB on nothing drops the first pick. A refused second click keeps it, so you can aim at something nearer.
- **Nothing is pulled** and no step spins: the two stay where they are, so the 30 m pull and the "go on while red" of
  the socket and point attachments don't apply; a red click is refused with the reason.
- **Preview** while aiming: both parts outlined and the attachment bead drawn between their closest points, green when the attachment
  is allowed and red when not, the reason on the look line. A part in your hands goes on a socket as in any mode.
- **Cutting** (either mode): the attach tool's aim (RMB) on a freeform join (within 0.6 m of anywhere along its bead, on either of its two
  parts) cuts it: the piece falls away as a body of its own where it is, moving as it moved. Destroying the anchor part
  (or the attached-on part) detaches it like any part.

### Rules (`Freeform.Check`, `ConstructEdits.FreeformRefusal`)

Two different bodies (a loose piece, a wreck, another construct); the closest points of the two parts' boxes within **3 m**
(touching isn't needed: the bead bridges it); neither part wrecked; at most **8** freeform attachments a construct (they share
`Construct.MaxJoints` with the joints, and the world's joint cap); the attached-on piece without raycast wheels, not riding
in a cargo volume and not carrying cargo; nobody driving or sitting in it (as joining). Then the usual attach tool rules: the
attach tool out, out of a seat, within reach, your own constructs or ones anyone may salvage (decision 19), the contested rule
(`ConstructEdits.Contested`) and, in survival, the attachment takes **2.5 s** (`EditKind.Weld`, checked again when done; creative
joins at once). The physics-only rule (decision 21) is unchanged. Which body keeps its frame: one held to the ground (a
foundation or pinned, not just frozen), else the one that isn't a loose part, else the one with more parts, else the
second picked; the other becomes the freeform segment. The attach tool's owner takes the result, as joining does.

### How it works (`Source/Constructs/ConstructFreeform.cs`)

A construct part still turns only in Rot24 steps on its segment's 1 cm grid. The attached-on piece (its whole body) joins the
construct as a new rigid **segment** whose rest frame sits at a fixed, arbitrary pose on its parent segment: a joint of type
`JointType.Fixed` (wire byte 5) with `CJoint.FixRot` / `FixPos` (`Construct.SegPose`), its anchor part in `Hinge` and the
attached-on part in `Child`. Everything inside the segment stays on its own Rot24 grid, so socket parts attach to a freeform
panel as usual (they go on its segment; `ConstructBuild.Blocked` compares parts in the same frame, `Construct.FrameOf`).
Nothing solves it: the physics skips freeform attachments (no slot, no chain entry, no motor), their parts' corners are **hull
points** of the body (`Construct.Rigid`), and the mass, centre of mass and inertia take them turned into the body frame
(`Construct.RestPose`, `RestBounds`; each box's own inertia turned with it). It costs what a jointed segment costs minus
the joint, and the moving-joint passes only run for constructs with a moving joint (`Construct.MovingJoints`). Collision,
damage rays and drawing already work per segment. The attachment is an edge in the connectivity flood, so losing the anchor or
the attached-on part splits the piece off where it is (`ConstructWorld.Compact` moves the body's frame to a freeform
segment when what stays hangs beyond an attachment that went, so nothing jumps). A cut marks the attachment and runs the same split.

### Network, blueprints and saves (protocol 97)

`CBody` carries the freeform attachments at its end (each one's joint, attached-on part, position and quaternion as exact
floats); `CActKind.FreeformWeld` (u16 body, u8 part, u16 second body, u8 its part) and `CActKind.FreeformCut` (u16 body,
u8 part, u8 attach) at the end of `CActKind`; `EditKind.Weld` in the hands tail. A blueprint step may be an attachment instead of a
socket step: `{ "part": "Panel", "weld": { "on": 3, "pos": [x, y, z], "rot": [x, y, z, w] } }` puts the part on a new
freeform segment, its own frame at that pose in step 3's segment frame (`ConstructBuild.WeldJson` writes one from an attachment,
`Freeform.StepPose`). Constructs aren't saved with the world yet; the construct message and the blueprint step are what
a save will use.

### Self-test

"Freeform weld" (`SelfTestFreeformWeld.cs`): a panel held at 30 degrees over the buggy's plate is attached by one
ray-through click without moving and stays exactly there (to a micrometre) while the buggy drives 30 m and turns, with no
allocation per tick; the two-click fallback joins two panels side by side; a gap of 0.6 m is refused with the reason;
a beam socket-attaches to the freeform panel on its segment; the attach tool cuts it free where it was, beam and all;
destroying the anchor detaches the panel where it was; survival's attach waits its 2.5 s; a fresh copy from the construct
message and a blueprint with the attachment step put the panel at the same pose; a real host with a separate client joins and
cuts over the transport and the client's copy agrees on the pose; the attach tool's default mode is socket attach.

### Choices made (the developer may change them)

- The gap limit is 0.25 m and a construct takes 192 freeform attachments (`ConstructFreeform.MaxPerConstruct`); attach strength is a normal attach's (the anchor and the
  attached-on part take the damage).
- Cutting is the attach tool's aim on the join (RMB within 0.6 m of the bead), and it is instant (survival included);
  attaching is LMB.
- In freeform mode the held part isn't fitted (switch back to socket attach on the wheel to fit it).
- A piece with raycast wheels can't be freeform-attached (wheels roll in the body's own frame); a part on a freeform piece
  can't be socket-joined to another construct with F (attach it freeform instead).
- **Wheels** (the "Wheel welds" fix): a freeform attachment of a wheel to a suspension whose free axle sockets are within
  0.35 m and face each other (`Freeform.AxleSnap`) is done as the socket attach of the axle onto the axle at the nearest
  grid turn, so the wheel rolls and springs as a fitted as an item one does (the look line: "puts the Wheel on the
  Suspension's axle (it rolls)"). A wheel attached freeform to anything else is a **fixed wheel**: attached on, it doesn't
  roll, and the look line says so.
- A routed link copied in with the attached piece keeps its nodes in their host parts' frames.

## Socket attach (board item sw01, decision 26)

The attach tool's default mode, **Socket attach** (the outer ring's other mode is [Freeform attach](#freeform-attach-board-item-fw01-decision-25)),
is a three-step flow. Sockets are an alignment and snapping aid here, not a strict connection system.

1. **Pick the source socket.** With the attach tool out and nothing in your hands, the aimed body and the bodies nearest
   the crosshair (at most four, within 30 m at this step, 7 m at the next, at most 64 markers, the nearest the crosshair)
   show their sockets as a dot and a short arrow along the socket's facing, drawn bigger past 7 m: blue free, grey in use,
   orange the picked one, green a target, white the one the crosshair snaps to (within about 4 degrees). LMB on a socket
   of the piece to move (a loose body or a separate construct) picks it; the piece stays where it is. A piece that can't
   move whatever the target says so here and isn't picked: held to the ground (pinned, a working foundation), riding in a
   cargo volume, carrying cargo, someone else's. A frozen piece can be picked: the physics gun's freeze holds it, it doesn't anchor it.
2. **Pick the target.** LMB on a socket of the construct to attach to (the magnet snap), or on any flat face of one of
   its parts: the 1 cm grid point under the crosshair on that face is a virtual socket. A ghost of the piece shows its
   socket on the target point, facing opposite the target's normal, turned to smart rotation's best guess (a one-part
   piece onto a socket; others start at 0), green or red with the reason on the look line (an overlap, the ground,
   someone standing there, someone else's construct, a full construct, wheels off the grid...). **LMB goes on either
   way**: a first guess is only a guess, and the spin is what puts it right. A thin line joins a piece more than 1.5 m
   away to where it goes.
3. **Spin it about the socket axis.** Mouse X (the view holds still) or the wheel turns the ghost; it snaps to 15
   degree steps with a click on each 90 degree step and a ring of ticks round the socket (bigger on 90), Shift for free
   angles, Ctrl for 5 degree steps. R takes the next facing smart rotation ranked (else half a turn: upside down). The
   ghost stays red with the reason until it is valid, and the look line says "spin it until it fits", or, when no 15
   degree step fits, "no angle fits here" and RMB for another spot (a hint: it doesn't stop the spin, and finer angles may
   still fit). LMB confirms: only this click needs a valid pose, and a red one is refused with the reason. The attachment takes
   the piece's fit time in survival and is instant in creative; the body snaps to the ghost.
   RMB steps back one step (3 to 2 to 1 to nothing; at step 1 RMB is the attach tool's usual cut free), Esc cancels.

**Pulling from afar.** The piece may be up to 30 m from the eye (`SocketWeld.PullReach`, survival and creative); the
target stays within the usual reach of the player or the Build Gantry's range (`ConstructEdits.WeldReach`, the server's
rule and the preview's, which reads it every frame, so walking off turns the ghost red: "too far away: ..."; with the
gantry's camera on, the preview leaves it to the server). The piece is measured at its part's `ReachPoint` or the
picked socket, the nearest; a body over 8 m round at the nearest of its bounding sphere. How it travels: it doesn't.
Nothing crosses the space between: it stays where it lies until the attachment is done, then jumps to the ghost, through
whatever is in the way, exactly as a near one does, so the wire is unchanged. The picking player sees a streak along the line
for the attachment's time and a third of a second more (`SocketWeldRender.Pull`; drawn on that PC only). Decision 19's contested rule, ownership,
"someone is driving it" and the survival recheck when the time is up all stand. Picks and reach go through the frame
like any aim ([Contraption space](self-tests-and-history.md#contraption-space)): the look ray and the eye are the client's and server's usual ones.

**On the grid or freeform.** A multiple of 90 degrees onto a real free socket of the same kind on the construct's main
body keeps the piece on the construct's Rot24 grid: the two merge as a join does, linked socket to socket, the parts
on the centimetre grid. Any other angle, a face point, a socket of another kind, or a target on a joint's or a freeform
piece's segment joins the piece as a fixed-pose rigid segment, exactly as a freeform attachment does
(`JointType.Fixed`, `ConstructWorld.FreeformWeld`; at most 192 a construct, no wheels on such a piece). The attach tool cuts
either free again (a grid part with RMB held, a freeform join with RMB on its bead).

**Wheels.** A wheel's axle onto a suspension's axle socket always goes on the grid, at any spin (a wheel turned about
its own axle is the same wheel: it snaps to the nearest grid turn), and a rolling piece (a wheel or a suspension, no
joints of its own) onto a socket on a moving joint's segment (a steering pivot's) merges onto that segment
(`JoinPlan.Seg`) instead of becoming a fixed-pose segment: the wheel rolls, steers and springs exactly as one the
attach tool fitted. Before this fix a wheel attached at a non-90 spin, or onto a steering pivot's suspension, was held on a
fixed segment and acted rigid. An attached wheel off a suspension's axle (a face point, an off-grid angle elsewhere) is a
fixed wheel; the look line says "a wheel on it is fixed and won't roll". **Self-test** "Wheel welds"
(`SelfTestWheelWeld.cs`): the buggy's wheels taken off and attached back (socket attach at 0, 15, 90 and 200 degrees, two
of them on the steering pivots; freeform attachments 4 cm and 4 degrees off each axle) give the attach tool's four raycast wheels:
they spin when driven, the springs carry the car and compress landing a 0.35 m drop, it drives and turns as the
blueprint's buggy does, a client's copy from the construct message has the same wheels; a wheel attached to a beam is
fixed; driving allocates nothing.

**Joints (board item wh01).** An attachment onto a joint's leaf socket (a hinge's or motor hinge's leaf, a piston's or
turntable's top, a steering pivot's leaf, every actuator size) always hangs the piece on that joint, as a fitted part's part
does: the attach tool share the leaf code (`ConstructBuild.OnLeaf`, `LeafRefusal`, `HangOnLeaf`: the joint is
made at rest, or an empty one it had is used again). On the grid it merges onto the joint's segment, linked to the leaf
(a fitted part's pose exactly); off the grid (any spin) it is a fixed-pose segment whose parent is the joint's segment, so it
moves with the joint. A freeform attachment whose anchor is a joint part and whose nearest point on it is on the leaf's end
(`Freeform.OnLeafSide`) hangs on the joint the same way; one onto the base side stays rigid with the base. A piece on
the leaf that would also attach to the joint's fixed side (a free socket of it meeting one there, face to face) is refused:
"that would lock the joint" (`SocketWeld.LockRefusal`); only resting against it, as a lid on its chest, is fine. The look
line says "on the X's moving side". Two bugs were behind the report: the socket attach merged a piece onto the joint
part's own (base) segment, linked to the leaf but with no joint made, so pistons and turntables built with the attach tool
carried nothing; and `Construct.Rebuild` gave every joint whose anchor part is a joint part that part's joint type, so
a fixed-pose segment anchored on a hinge (an off-grid attach, a freeform attachment on it) turned into a second joint about the
body's origin and lost its pose: the piece showed up mirrored through the hinge. Freeform attach now keep their type.
**Self-test** "Joint welds" (`SelfTestJointWelds.cs`): every joint part on a powered, anchored rig with a lever wired
in (the lid hinge on the Chest); a panel (a door on the passive hinge, the chest's lid on the lid hinge) socket-attached on
the grid and 30 degrees off it and freeform-attached onto the leaf sits outward at the ghost's pose (a fitted part's within
1 cm and 1 degree), hangs on the joint, a client's copy agrees, and the lever drives it out and back as the blueprint-built
one moves; base-side joins stay rigid; an attachment onto both sides is refused; driving allocates nothing.

**Smart rotation learns** from step 3: confirming a grid angle other than its guess records the correction for that
part, socket kind and side, as fitting a held part does.

**Server and network (protocol 99).** The client sends `CActKind.SocketWeld`: the piece and its part and socket, the
construct, its part and socket (255: a face point, then the point in that part's segment rest frame and its facing),
and the angle in hundredths of a degree. The server checks the final pose with the same rules (`SocketWeld.Check`:
the sockets, overlap with the construct, the map and players, the merge and freeform limits) plus reach, ownership and
decision 19's contested rule, and in survival again when the attachment time is up (`EditKind.SocketWeld`, shown as
"Attaching..."). Code: `Source/Constructs/SocketWeld.cs` (rules, pose, markers), `SocketWeldFlow.cs` (the steps),
`ConstructEditsSocketWeld.cs` (the server), `Source/App/Program.SandboxSocketWeld.cs` (the input),
`Source/Render/SocketWeldRender.cs` (markers, ghost, ticks).

**Self-test** "Socket weld 3-step" (`SelfTestSocketWeld.cs`): decision 27's tools by function over every part file;
an aimed loose beam lists its markers (no allocation); its end socket on a frame block's side: the ghost aligned and
facing out; a 30 degree spin joins it as a fixed-pose segment exactly at the ghost, 90 degrees stays on the grid
(merged and linked) and smart rotation learns the correction; R and RMB step by step; a red target (an overlap) is
refused at step 3's confirm; a face point in survival takes the fit time; the construct message and a blueprint keep the pose; a real host
with a separate client joins both ways and both agree.

**Choices made (the developer may change them).** 30 m in both modes, not the physics gun's 12 m in survival: the gun
carries 500 kg live, while the attach tool stands still for the fit time and is how a heavy machine gets moved there at all.
There is still no weight limit, and the pull needs no clear path. R steps through smart rotation's ranked facings rather than a
separate list of "variants" (a socket has one facing; the spin covers the rest). Face points and off-grid angles are
always freeform segments (a face point has no socket to link). A piece held to the ground (pinned or on a working
foundation) can't be the piece moved: pick it as the target. There is no weight limit beyond the merge limits.

## Point attach (board item pw01)

The developer's request: "click on the part that's going to move, and where you click is where the weld alignment will
originate from. Then you click on the target location and have a rotation option. And then weld, like the socketed
welded tool, but based on where you click." **Point attach** is Socket attach with **Shift** (the sprint key) held at its first step: the look line and the hint say so
(`Program.PointAttachNow`). Shift counts only while no flow is under way: from step 2 on the flow goes on by itself, so you can let go, and Shift is the spin's free angle again. It has no place on the ring.

1. **The origin.** With the attach tool out, nothing in your hands, LMB on the piece to move (a loose body or a separate
   construct up to 30 m away; not one held to the ground, riding in or carrying cargo, or someone else's): the point under the crosshair on that face (on the 1 cm grid of the piece's
   main body) and the face's normal are the attachment's origin and alignment. An orange dot, arrow and cross mark it.
2. **The target.** Aim at another body: a live ghost puts the origin on the point under the crosshair, its normal facing
   the target's (face to face), green or red with the reason on the look line (a green dot marks the target point). LMB
   picks it, red or green, as the socket attach's does; the target is in the usual reach.
3. **Spin.** Mouse X (the view holds still) or the wheel turns the ghost about the target normal in 15 degree steps (a
   click on each 90), Shift free, Ctrl 5 degrees, with the socket attach's ring of ticks; R flips it half a turn. It says
   what is wrong and whether any 15 degree step fits, as the socket attach does (sockets lined up per angle). LMB joins, once it is green.
   RMB steps back (3 to 2 to 1), Esc cancels; putting the attach tool away, another mode, or the piece or target changing
   under it (the picked parts' kind and place are stamped and checked every frame) clears it.

**How it joins.** It is the socket attach with a face point on both sides: the same pose (`SocketWeld.Pose`), rules and
refusal reasons (`SocketWeld.Check`: overlap with the construct, the map and players, the merge, joint and freeform
limits, the lock refusal), the same server checks (reach, ownership, decision 19's contested rule) and the same time
(survival: the piece's fit time, `EditKind.SocketWeld`; creative at once). **Sockets lined up**
(`PointWeld.Resolve`, worked out by the client's ghost and again by the server on the same numbers): where the
face-to-face pose puts a free socket of the clicked piece part within 2 cm of a free socket of the clicked target part,
facing it within about 3 degrees, it is that socket attach (so a multiple of 90 degrees onto a socket of the same kind
merges on the grid, linked); a wheel and a suspension whose free axle sockets come within 0.35 m, facing, go axle onto
axle, so the wheel rolls and springs. Otherwise the piece is a fixed-pose freeform segment at the clicked points. A
**joint's leaf**: a target point on the leaf's end of a joint part (the outer quarter along the leaf socket's facing, as
the freeform attachment has it; `SocketWeld.OnLeaf`) hangs the piece on the joint (the socket attach's face points onto a leaf
now do the same), and a piece that would also attach to the joint's fixed side is refused ("that would lock the joint").
Drivetrain parts attached across a join work as the freeform drivetrain fix has it (fd01).

**Network (protocol 107).** `CActKind.PointWeld` at the end of `CActKind`: u16 piece, u8 part, the point (its body
frame) and its facing, u16 construct, u8 part, the point (that part's segment rest frame) and its facing, u16 angle
(0.01 degree). The server resolves the sockets itself and runs the socket attach's path (`ConstructEdits.BeginPointWeld`;
the piece's point is kept in the hands as `SrcAt`/`SrcDir` for a timed attach). Code: `Source/Constructs/PointWeld.cs`,
`Source/App/Program.SandboxPointWeld.cs` (the input; the steps are a `SocketWeldFlow` whose source is a face point),
`SocketWeldRender` (markers and ghost).

**Self-test** "Point weld" (`SelfTestPointWeld.cs`), through the real input (`Program.SweepFrame` outside an ImGui frame,
`SweepDraw` in a real headless frame with error recovery): a loose panel's clicked point onto a frame block's face,
spun 30 degrees with the wheel (R to 210 and back), joins at the ghost: the point on the block's (1 cm), normals opposed
and the spin as asked (1 degree); clicked points on two sockets merge on the grid, linked; a panel that would overlap
the construct is red with the reason, LMB goes on to the spin and the next LMB joins nothing; RMB, Esc, the attach tool put away, socket mode and the piece
removed all clear it; holding still in steps 2 and 3 allocates nothing; a real host and a separate client (lag, loss):
the client's attach lands at its ghost on both peers. Through the server path with the points the input would pick: a
panel on a motor hinge's leaf hangs on the joint and the lever turns it; a block on the leaf beside a wall on the fixed
side is refused (lock), without the wall joins; the buggy's four wheels attached back by points a few centimetres off
their hubs and the suspensions' axle faces roll, spring and steer like a fitted one; the engine attached back onto the
gearbox by clicked points drives the buggy like the blueprint's. "Interaction sweep" covers the mode too.

**Choices made (the developer may change them).** Point attach is a third ring option, not the default (the developer's
"change the default connection" was about the linking tool). Points snap to the 1 cm grid like the socket attach's face
points. Sockets "line up" within 2 cm and 3 degrees (an axle 0.35 m); only the two clicked parts' sockets are looked at.
The spin starts at 0 (smart rotation's guess is the socket attach's: there is no socket to rank).
