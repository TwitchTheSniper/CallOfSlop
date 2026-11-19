# Conveyor inclines, lifts and overpasses (design)

The user, 2026-11-17: conveyors, chutes and other logistics pieces should go **up and over** an aisle, so a factory's aisles
aren't crisscrossed with chutes and machines. For that a conveyor needs a better way to climb: today a conveyor that must
rise runs flat, turns abruptly into a vertical leg and turns back to flat, instead of one straight conveyor climbing at an
incline. Built on `claude/conveyor-inclines` (2026-11-17) as the user's answer at the end asks: what was built is listed there.

## 1. Where things stand (main, 2026-11-17)

- **A link is a polyline** of nodes (`CRoute`, `CRouteNode`, `ConstructRoutes.cs`), each snapped onto and hosted by a part,
  on the 5 cm grid, spans 0.08 to 8 m (`MaxSegment`). Links are drawn, never bodies: only the parts under their nodes hold them.
- **The bend rule** (`BendRule`): chutes and conveyors bend in 45 degree steps up, down and sideways; there is **no slope
  rule**, so a conveyor may run straight up with pieces riding at 1 m/s, and a chute moves an item or a load a second
  (`ChuteSeconds`) whichever way it runs.
- **The auto-router** (`RouteSearch`, `ConstructAutoRoute.cs`) searches a Hanan lattice in the six axis directions with a
  cost per bend, so every rise is a vertical leg between two 90 degree elbows: the abrupt climb the user sees. Drawing is a
  straight bed per segment and a mitred elbow per node (`RouteRender.Conveyor`, `ConveyorElbow`).
- **The factory kit** lays chutes 0.2 m off the floor along lanes (`FactoryScript.ChuteVia`); its design (3.4) asks for an
  overhead layer (2.4 m clear over walkways, 4.5 m over the vehicle lane), not built yet. Its spacing is being reworked.
- **The large-piece link** is laid like any link, then set to a size (`CRoute.ConvW`; user, 2026-11-11). This design keeps
  that shape: the incline comes from how it is laid, the bed kind is a setting on it.

## 2. Grades: how steep each carrier may climb

A link's **grade** on a segment is its angle above level. A **graded** link (section 8) keeps every segment within its
carrier's limit; plan turns (left and right) keep their 45 degree steps.

| Carrier | Up to | Down to | Why |
| --- | --- | --- | --- |
| Conveyor, **roller** bed (today's) | 10 deg | 10 deg | powered rollers: a piece slides back or runs away past this |
| Conveyor, **belt** bed | 20 deg | 20 deg | a plain belt's grip on a steel piece |
| Conveyor, **cleated** belt | 40 deg | 40 deg | cleats hold each piece; the gap between pieces is at least the cleat pitch (0.3 m) |
| Steeper (to vertical), pieces | **Piece Lift** part | Piece Lift | a vertical reciprocating lift: a car carries one piece |
| Chute, level or falling | level | any, 5 deg steps | gravity or a powered duct; a fall of 30 deg or more is drawn as an open trough |
| Chute, rising | **Bucket Elevator** part | - | bulk and small parts go up in buckets, never up a duct |
| Chute, steep fall in little room | **Spiral Chute** part | - | a helix down 2 to 12 m in a 1.5 m footprint |

The bed is a setting on the conveyor link, like its width: aimed at with the linking tool, a key cycles roller, belt and
cleated (the look line: "Belt conveyor 0.40 m wide, 14 deg (max 20)"). A setting that the link's steepest segment breaks is
refused with the reason ("too steep for a roller bed: 18 deg, max 10 - a belt takes 20"). Belt and cleated beds cost more in
the link's recipe per metre (rubber and a drive drum), so a flat run stays on rollers. Grades go in **5 degree steps**: the
tool snaps a node's height so the segment lands on its step to 1 cm, which keeps a 1 m span within 0.6 degrees.

**Nothing slides on a graded link.** The rule is checked when the link is laid, so a piece on a legal grade simply rides; there
is no slide-back simulation. Slide-back is only the reason for the limits, and the reason the tool gives.

## 3. Smooth transitions: vertical curves

Where a graded conveyor's grade changes (level to 15 deg, 15 deg to level), the node is a **vertical curve**: the bed and the
ride follow an arc of radius R tangent to both segments, starting and ending a tangent length `t = R tan(dTheta / 2)` either
side of the node. Plan turns keep today's elbows.

- **Radius:** `R = max(1.5 m, 3 x bed width)` for a roller or belt bed, `max(1.0 m, 2 x width)` for a cleated belt (its cleats
  hold a piece through a tighter crest). From level to 20 deg at R 1.5 m the transition is 0.52 m long.
- **Room:** both segments either side must be at least `t` long (with a following curve's `t`), the same kind of rule as
  `ConveyorRoom` for plan turns, refused with "too short a run for that change of grade: 0.21 m, needs 0.26 m".
- **Length and pose:** `ConstructConveyor.Length` and `Pose` walk segments trimmed by their curves' tangents and the arcs
  between; a piece's middle sits on the arc and its long axis follows the tangent there. A piece longer than the arc bridges
  it (drawn as it is, no lift-off physics).
- **Drawn** as 3 to 8 straight pieces along the arc, rails and chevrons following; a chute's grade change gets the same arc.
  Arcs are cached per node on Rebuild (an array beside `RouteNodes`), never worked out per rider.

## 4. How the router chooses an incline or a lift

The auto-router keeps its square lattice search (it is fast and its result passes the path rule) and gets a **ramp pass**
after it, for conveyors and chutes only:

1. The search runs as now, with a vertical step costing `VerticalCost` per metre (2.5) rather than its length, so it prefers
   one rise to several.
2. The ramp pass takes each **flat - vertical - flat** run of the result and tries to replace the vertical leg with an incline
   at the bed's limit: the climb starts back along the flat run before it (and may end forward along the one after), so the
   incline needs `rise / tan(limit)` of run plus its two curves' tangents. It tries the bed's limit first, then 5 degrees less
   (a gentler slope that still fits), checking each candidate with the one path rule. The first that passes replaces the leg.
3. Where no incline fits, the leg stays vertical, and the link is **not graded** until a lift stands there: the preview is
   amber, "a 3.2 m rise needs 8.8 m of run at 20 deg (belt), or a Piece Lift here". The hand tool never places parts for the
   player; the factory kit places the lift itself (section 7).

**Cost of a route** (for the factory kit, which compares options): `length + 0.8 x bends + 2.5 x vertical metres + 6 per
lift`, a lift counting as a fixed 6 m so a short climb takes a slope and a tall one in a tight hall takes the lift. A chute's
fall costs its length only; its rise is never searched (it must go through a Bucket Elevator). The pass is client-side like
the search, inside its 2 ms a frame budget, and sends only nodes, which the server checks as any laid route.

## 5. Lifts and spirals (new parts)

All three are part kit parts (`Source/PartKit/Generators`, a new `lift_parts` generator), sized in 0.5 m steps of height, and
work as **junctions**: a link ends at the foot port and another starts at the head port, so the links stay graded.

- **Piece Lift** (a vertical reciprocating conveyor): Piece in at the foot, Piece out at the head, or the reverse (a setting).
  Heights 2 to 12 m; two tiers of car (1 x 1 m and 6 m long; 2 x 2 m and 12 m). A car carries one piece at 0.5 m/s, its
  position worked out from a timer on both peers as a junction's lead-in is (`ConstructConveyor.LeadIn`, `CRider.ViaPlus1`):
  the piece rides the lift's vertical lead-in, the next waits at the foot. Draws power while the car moves.
- **Bucket Elevator:** Chute in at the foot, Chute out at the head; heights 2 to 16 m; moves a chute's rate (a load or one
  part a second) like any chute hop, drawing power while it carries. Buckets drawn moving on the belt in the casing's window.
- **Spiral Chute:** Chute in at the top, Chute out at the foot; heights 2 to 12 m; no power. Also takes small pieces off a
  conveyor (a Piece in at the top), sliding them down to a Piece out: the cheap way down for pieces in a tight spot.

## 6. Overpasses: up and over an aisle

A link has no supports of its own and no new support rule is added: its nodes sit on parts and its spans are at most 8 m,
which already forces something to stand under every 8 m. What is missing is the **something**: parts made to carry links.

- **Trestle** (part kit, `trestle_parts`): two legs and a cross-beam whose top carries a **saddle**, a named link mount that a
  node snaps to on its centreline (the cross-beam's width sets the most links side by side: 1, 2 or 4 lanes). Heights 1 to 8 m
  in 0.5 m steps, so the beam's top lands where the clearance needs it. A single-post variant for one link along a wall.
- **Link Bridge** (part kit): a truss 4, 6 or 8 m long with saddles every 2 m, standing on two trestles or two walls, carrying
  up to four links over an aisle in one span. Its underside is the clearance line.
- **Pipe rack** (the factory design's "pipe bridge part of the shell"): the shell's beams get saddles too, so a hall's links
  ride its roof structure with no trestles at all.

**Clearance** is measured from the floor to the lowest of the bridge's underside, the bed's underside and the bottom of any
riding piece's swept box (a conveyor's riders hang over its rails by up to half their width):

| Aisle | Clear height | Source |
| --- | --- | --- |
| Walkway (0.9 m) | 2.4 m | factory-generator.md 3.4 |
| Main aisle (1.1 m) | 2.4 m; no floor crossing at all | factory-generator.md 3.4 |
| Vehicle lane | 4.5 m | factory-generator.md 3.4 |
| Crane bay | above the crane's hook height, or nothing over it | factory-generator.md 3.2 |

A typical overpass: a conveyor from a machine's Piece out at 1 m climbs 3.7 m at 20 deg on a belt bed over 10.2 m of run, crosses
a 6 m lane on a Link Bridge at 4.7 m (bed underside 4.55 m) and comes down the same way to a rack, about 27 m of floor in
all; a cleated bed at 40 deg does it in about 15 m; a tight cell uses two Piece Lifts.

## 7. Laying one by hand

On the linking tool, for a conveyor or chute (nothing new for other types):

- **The preview** shows the next segment's grade beside the crosshair ("+15 deg, belt max 20"), green within the bed's
  limit, amber past it with the reason and what would take it (a steeper bed, a lift), red when the path rule fails. Vertical
  curves are drawn in the preview as they will be laid.
- **Grade snap** (a new rebindable key, default G, checked against `Keybinds.Overlap`): cycles **level** (the next node at
  the last one's height), **max** (the bed's limit, the node's height snapped so its span climbs or falls at it) and **free**
  (today's surface snap, rounded to the 5 degree step). Held-to-adjust (lg01) still moves the node along its normal.
- **Saddles** snap a node to their centreline. Auto-route uses the ramp pass (section 4); Re-route and the Loom lay graded links.

## 8. Old links and saves

- `CRoute` gains **`Bed`** (a byte: roller, belt, cleated) and a **`Graded`** flag. Links in saves and blueprints older than
  this change load with `Bed` roller and `Graded` false: they keep **today's rules exactly** (any slope, rising chutes, sharp
  elbows) and draw as now. A link is graded when it is laid, re-routed or loomed after the change, or a blueprint says so.
- **Save:** after the conveyor sizes, one byte a conveyor or chute link (bed in bits 0-1, graded in bit 2); an older save ends
  before it. **Wire:** the same byte after a link's size in a Full, and on `RouteEditOp.Size` (now size and bed): bump
  `ServerBrowser.ProtocolVersion` with a history line. Node data is unchanged: curves are derived from the nodes and bed.
- **Blueprints:** `"bed": "belt"` and `"graded": true` on a link; without them, legacy. The vehicle kit's `BlueprintScript`
  route step and the factory kit's `Route` take both. Committed blueprints keep their links until their generator runs again.
- **Lift parts** keep their car's state like a junction keeps a piece in its lead-in: a piece saved in a lift loads at its
  head's output start (as a piece saved in a junction does now).

## 9. Performance

Arcs are cached on Rebuild; `Pose` adds an arc evaluation only for a piece on one. Target: the measured 200 riders on 20
links (0.05 ms a step) stays under 0.1 ms with every link graded; if long overpasses cost more, keep each rider's last
segment index (a byte in `CRider`) instead of walking from the start. A lift is one timer on the 10 Hz conveyor step; a
Bucket Elevator one chute hop; the ramp pass sits inside the router's 2 ms frame budget with fixed arrays; curve pieces
are instanced like elbows.

## 10. The factory kit

After the spacing rework merges, the kit's router (factory-generator.md 3.4) gets its overhead layer for real:

- **Floor for people, overhead for flow.** Edges that cross a walkway, the main aisle or the vehicle lane go overhead; the
  router climbs out of the source cell (a belt or cleated incline inside the cell's margin, else a Piece Lift or Bucket
  Elevator), runs on Link Bridges or the shell's pipe rack at the layer height (2.6 m bed, 4.7 m over the lane, from the
  clearance table) and comes down in the target's cell (a Spiral Chute for bulk where a slope won't fit).
- **One rack per crossing:** edges crossing the same aisle share a Link Bridge, laid one width apart (as the Loom lays them),
  so a hall has a few tidy crossings rather than many.
- **Checks** (`--factory-check`, already listed in 3.4 item 4): every graded link within its bed's limit; nothing under the
  clearance over any aisle; every node on a part; every lift powered.

## 11. Phases (one builder)

Start after the cargo parts and factory builder's branch merges: both touch the factory kit's routing, and phase 4 builds on
its spacing.

1. **Grades and curves.** `CRoute.Bed` and `Graded`; the bend rule split into plan turns (45 deg steps) and grade (5 deg steps
   within the bed's limit); vertical curves in the cache, `Length`, `Pose`, `Tails` and drawing; the bed setting on the
   linking tool and its look line; save, wire (protocol bump), blueprint keys. Done when a hand-laid belt conveyor climbs at
   20 deg over a curve with pieces riding smoothly on host and joined client, and an old save's links are untouched.
2. **The tool and the router.** Grade preview and grade snap; the ramp pass in `RouteSearch`; the route cost with lifts as
   a fixed cost. Done when auto-routing a conveyor between machines 3 m apart in height gives one incline, not two elbows.
3. **Parts.** Trestle, Link Bridge, saddles (a link mount in the part kit), Piece Lift, Bucket Elevator, Spiral Chute; the
   chute rising rule for graded chutes. Done when a conveyor crosses an aisle on a bridge by hand, and ore goes up an elevator.
4. **The factory kit.** Overhead layer, crossings on shared bridges, lifts where slopes don't fit, the clearance checks; the
   Factory Showcase and the Militia factory regenerated. Done when `--factory-check` passes and no link lies on an aisle floor.

## 12. Open questions for the user

1. **Should rising chutes be refused on new links?** Recommend yes: a graded chute only falls or runs level; up goes through
   a Bucket Elevator. Old chutes keep rising as they do. Or: chutes stay direction-blind and only conveyors get grades.
2. **One conveyor link with a bed setting, or separate link types?** Recommend one link, the bed a setting beside its width
   (your "laid down then configured" direction). Or: belts and cleated belts as their own link types in the picker.
3. **The grade limits.** Recommend 10 / 20 / 40 deg (roller / belt / cleated), close to real conveyors. Or: one limit for
   every conveyor (say 30 deg) and lifts above it, simpler to learn.
4. **Same speed on a slope?** Recommend yes, 1 m/s along the bed whatever the grade (pieces arrive in the same time per metre).
   Or: slower up steep grades, which makes overpasses cost throughput.
5. **A support rule?** Recommend none beyond today's (nodes on parts, 8 m spans): trestles and bridges make supports possible
   without a new refusal. Or: graded spans over 4 m need a saddle under them, so long climbs look held up.
6. **Clearances.** Recommend 2.4 m over walkways and the main aisle, 4.5 m over the vehicle lane, as the factory design has.
   Or: 3 m over walkways for a roomier look, at the cost of longer climbs.
7. **Lifts and power.** Recommend Piece Lifts and Bucket Elevators draw power while moving and stop without it (a raid target).
   Or: unpowered, like conveyors today.

## The user's answer (2026-11-17): visual routing only

"Don't modify transfer rate or transfer behaviour. I'm talking purely about the visualisation and the visual routing of a conveyor." This overrides the text above:
- **No behaviour change:** no slope limits by bed, no refusal of rising chutes, no speed or capacity change on a slope, no new lift parts that change how material moves, no power for lifts. Links move pieces and material exactly as today.
- **What changes is how a link is laid out and drawn:** the router and the link tool lay a rise as one straight inclined run with smooth curved transitions, not flat, vertical, flat; runs can go up and over an aisle at a set clearance (2.4 m walkways, 4.5 m vehicle lanes) on drawn supports (trestles, bridges as looks or simple structural parts); the factory kit routes its links overhead so aisles stay clear.
- Keep the link's path data compatible (saves and wire bump only if the stored path changes shape); old links draw as before.

### What was built (2026-11-17, visual routing only)

- **Grade bends** (`ConstructRoutes.BendRule(type, from, to)`, `GradeOnly`): a chute's or conveyor's change of grade alone, up to a
  right angle, is any angle; plan turns keep their 45 degree steps. No bed kinds, no limits, no new refusals, no `Bed`/`Graded`
  fields: nothing in a save or on the wire changed for links.
- **Vertical curves** (`RouteCurves`): drawn by `RouteRender.Curve` (3 to 8 pieces, a few mm fatter or thinner by turns: no
  coplanar faces) and ridden in the drawing by `ConstructConveyor.Pose(..., drawn: true)`/`OnCurve` for 2 to 80 degree grade
  changes. The link's length, a piece's distance along it and every server-side position stay the straight segments'.
- **The tool**: Reload while routing a chute or conveyor cycles the grade snap (free 5 degree steps, level, 20, 40;
  `ConstructRoutes.Grade`, `GradeTurn`; back to free when the link ends); the HUD shows the next segment's grade. Not the G key: G
  is the grenade, live in Sandbox.
- **The router.** A ramp pass (section 4) was built and dropped in review: an incline in place of a vertical leg makes the link
  shorter, which changes travel time and how many pieces fit. On 2026-11-19 the user asked for the straight path first, so the
  router now tries a straight run or incline before the lattice (power-and-links.md "The straight way first"); the transfer code
  is untouched, only the geometry is shorter.
- **Supports** (part kit `link_supports`, Constructs/Parts/LinkSupports): Trestle and Trestle Post 1 to 8 m, Link Bridge 4/6/8 m,
  saddles as `tray` lanes; their recipes go after every older Assembling row (`Assembling.LateFolders`), so saved Assemblers keep
  their recipe numbers; protocol 351 (the new files move part ids). The shell's rafters got no saddles (the pipe rack).
- **Not built**, as the answer rules out: bed kinds and grade limits, Piece Lift, Bucket Elevator, Spiral Chute, power for lifts,
  refusing rising chutes. **The factory kit** already lays its chutes on the service strip behind the machines, never across the
  aisle or the road, so no overhead layer was needed at today's spacing; `--factory-check` now fails a chute or conveyor under the
  clearance (2.4 m aisle, 4.5 m vehicle lane) over a walkway tile (`FactoryScript.Walkways`), and the three refineries and the
  extraction post pass it.
