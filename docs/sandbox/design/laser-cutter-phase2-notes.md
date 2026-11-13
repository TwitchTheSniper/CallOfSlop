# Laser Cutter, phase 2: notes (2026-11-13)

Phase 2 of `laser-cutter.md`: openings that work. A cut panel or cut plate now has its own collision, so shots, bodies and
players pass any opening of 10 cm or more. No wire change: a cut part still travels by its key, and every peer makes the
same boxes from it. The protocol stays at main's number.

## What was built

- **The design follows the user's answers to phase 1** (its first commit): 4.4 (plate of any thickness, a flat bar either
  way up, the bed's height the only bound), section 5 (the cutting law `seconds = 2 + E x edge x thickness / power`,
  E = 0.36 kJ a metre per mm, an estimate; `storage.cut.kw` replaces `speed`; the offcut counted in tenths in the
  cutter's own count, whole units into the bin), the phase 3 row's tries, and a line under section 13.
- `HullLoft.Cuts.cs`: `PlanRects` makes the solid sheet into rectangles (the panel's up to four strips, each cut into
  bands at the holes' ends, a band's free spans across; a span that carries on unchanged is the same rectangle).
  `CutPanelCollision` gives a cut panel one hidden box per rectangle, the bounds of the drawn quads over it, its rims a
  whole centimetre inside the drawn edges (the uncut panel's rule, now on every opening's rim too). A cut plate's boxes
  are exact at the plate's own edges (as the extrusion's) and a centimetre in from an opening's rim.
- Edge sockets keep their names; one whose spot a cut takes away (within half a centimetre) is left out, on panels and
  plates. Each opening of 10 cm or more both ways whose middle is on the sheet adds `o0`.. in its middle, on the sheet's
  middle plane, facing the surface's nearest axis. It stands in air, so it is added to the part after the file is read
  (as an extrusion's sockets are made in code), on every peer alike.
- Limits and their words: "too many collision boxes (N of 24): line the cut-outs up, or use fewer"; the thickness rule is
  now the bed's ("too thick for any Laser Cutter's bed: 2.4 m, the largest takes 2 m"). `--form-panel` prints the
  offcut to the nearest tenth.
- The window tool (`Program.WindowShotCuts.cs`): `ray PART U,V [M]` (the bullets' construct ray, square to the sheet),
  `walk PART U,V M FRAMES` (noclip off, Forward held), `side PART U,V`, and `put PART U,V M` on the host, since a client
  can't move its own player. U and V are a spot on the sheet, as a key places its cuts.

## Choices made

1. **A hole is an opening's whole extent**, cut-back corners included: the drawn metal in those corners collides with
   nothing, so nothing unseen ever blocks a ray (the design's rule).
2. **A cut corner opens by the rectangle inside its triangle** (half its two sides). A little air along the diagonal still
   collides. Question 1.
3. **Boxes are axis-aligned bounds of the quads over each rectangle**, and the axis a rim is inset along is the one the
   sheet runs along there (z, or y on a fold's raised leg). On steep or curved sheet the clear opening is smaller than the
   drawn one (risk 3, unchanged). A web left too thin for a box after its two insets is drawn only, and sockets on it are
   left out, so a part is never refused for it.
4. **`back` is left out when the middle is cut away**, by the same rule as every socket. A door skin with a central
   window loses it. Question 2.

## Tried by hand (this PC)

- `--form-panel` on a plate with a round port (4 boxes, `+y1`/`-y1` gone, `o0`), a 3 x 2.5 m wall with a doorway notch
  and a slot (6 boxes; `w2`, `w3`, `back` gone; `o0`, `o1`), the door skin with a window (4 boxes, `back` gone), a flat
  bar with a hatch and a cut corner (5), a plate with a slot and two cut corners (5; `s0`, `s1`, `e0`, `w0`, `back` gone).
  Each: a second make in slices is the same part.
- Window tool, Proving Ground, `--spawn-at 140,0,-100`, a scratch blueprint (in `bin` only, removed): a Concrete
  Foundation 4x4 m with that wall standing on it (welded by `s3`, a socket the doorway keeps).
  - **Host:** rays square to the wall pass the doorway (100,70) and the 10 by 40 cm slot (150,190), and hit the wall at
    1.98 m of 4 beside each (100,160 and 150,175). Walking from 3 m out for 150 frames: through the doorway the player
    ends 0.61 m behind the wall; beside it they stop 0.42 m in front (the capsule's radius).
  - **Joined client** (`--net join:127.0.0.1:27611`, the host putting the player): the same four rays the same way, the
    client's own copy of the part made from its key; through the doorway 2.63 m behind, beside it stopped 0.42 m in front.
    A first client try, with Forward held before the host's teleport settled, stopped 1 m short; with a settle wait it
    passed.
- `--check-construct-parts` and `--gen-parts all --check`: pass. Self-test by name, `Compound collision` and
  `Press stock`: pass.

## Measured (a throwaway probe, removed; this PC, host and client in one process)

| What | Figure |
| --- | --- |
| The worst key: six octagonal openings on a 6 x 4.5 m loft (182 characters, 90 pieces, 20 boxes) | whole 4.9 ms; deferred, its first part 1.5 ms, then 6 pumps, the worst **5.8 ms** |
| One slice of it, split | read 0.2 ms, file 0.25 ms, parse 0.6 ms: about 0.9 ms |
| Twenty new cut lofts at once (three cuts each), as a client reads one packet | first parts together **14.9 ms**; then 65 pumps, the worst 5.1 ms |
| Their names on the wire | 2,560 bytes for 20, so about 5 KB of names in a Full of 40 |
| A free chain of 40 cut plates, 240 solid boxes, falling and lying on the ground | spawn's 3 s: worst server update 4.7 ms, client 0.8; then mean 0.19 ms server, 0.14 client (worst 0.40, 0.84) |
| The same chain of 40 plain plates, 40 boxes | spawn's worst 0.4 ms; then mean 0.21 server, 0.14 client (worst 1.95, 0.67) |

So a body of many cut parts costs no more a tick than the same body uncut. Two findings are over the 8 ms rule or the
pump's own 2 ms budget, and are **not fixed here**:

- **Twenty new cut keys in one update take 14.9 ms** (0.74 ms each, over twice a panel's first part: the plan and up to 24
  boxes and slabs to parse). A packet that names many new cut keys at once goes over 8 ms. The fix proposed for the start
  of phase 3: first parts beyond a budget (say 4 ms an update) wait a tick behind a stand-in, or a cut key's first part
  parses its boxes without slabs.
- **A pump runs 5 to 6 ms, not 2**, though a slice is about 0.9 ms when timed alone. That is phase 1's pumping (slices
  inside the pump cost more than alone, or the budget check lets several through); it needs its own look, with
  `RuntimeParts.Redrawn` timed too.

## Not done (phase 2's remaining work)

- **A flat bar on edge** (the user's answer 3): still refused, "lay the bar flat". It needs the plate's frame turned (the
  sheet `L` by `H` in the piece's `y`, its facets, boxes and sockets swapped x for y). Phase 3, or a short 2b.
- `--form-panel` does not yet print each opening's clear size (the design's readout for risk 3).
- The rims checked by eye at 1 m and 30 m (no pictures this phase); a pane on `o0` (phase 6).
- The two findings above.

## Not verified

- A real fired bullet: the `ray` verb calls `ConstructWorld.Raycast`, the call the bullets' `SandboxRay` loops on, not a
  gun's whole shot path.
- A moving vehicle with cut parts against the world, and bodies (not players) through an opening.
- Cut panels on steep or bent sheet in a window (only flat walls and plates were tried there).
- Linux CI.

## Questions for the user (asked together; the first option is the recommendation)

1. **The collision at a cut corner.** A cut corner is a triangle, and boxes are rectangles.
   - A (recommended): the rectangle inside the triangle: a little air along the diagonal still stops things.
   - B: the triangle's whole extent: a little drawn metal at the corner lets things through.
   - C: a staircase of two or three boxes: closer, but each cut corner costs more of the 24 boxes.
2. **The `back` socket when a cut takes the middle away** (a door skin with a window in its middle).
   - A (recommended): left out, as every cut-away socket: a blueprint that needs it uses a different design.
   - B: moved to the nearest solid spot on the sheet's centre line, keeping its name.

## Phase 2b (2026-11-13): new keys within the tick, and the bar on edge

No wire change: the protocol stays at main's number (290).

### What was built

- **Stand-ins for new keys off the wire** (`HullLoft.FirstParts.cs`). A key read from the wire (`HullLoft.Read`) is made at
  once while the update's first parts have cost under `FirstBudgetMs` (3 ms). Past it, the key gets a stand-in: its own id and
  name at once, one plain box over the blank sheet's bounds (from the key's text alone: no plan, no part file) and one socket.
  The pump (`PumpPanels`, 2 ms an update) makes the real part from the key, oldest first, and **copies it into the stand-in in
  place**, so its id never changes and nothing that holds it has to be found; every body holding one is marked for a full
  rebuild (`RuntimeParts.Reshaped`), and its facets are queued as any new panel's. Formed panels get the same treatment.
  Anything that asks for a key by name (a blueprint, a save, a build step, `--form-panel`) gets the real part at once: a
  stand-in still waiting is finished there (`Parse`), so sockets are right wherever they are used. A batch that had
  stand-ins logs one line when it is whole (`[cuts] N new keys off the wire waited behind stand-ins ...`).
- **Slices parse only their facets**: a slice's part file has one stand-in box and socket instead of the part's collision,
  sockets and numbers (a slice of six facets: part file 0.18 to 0.08 ms, parse 0.62 to 0.44 ms).
- **A flat bar on edge is cut** (the user's answer 3; design 4.4, "A cut on edge"): the plate's sheet, `L` by `H` and `W`
  thick, turned a quarter about the length (`TurnEdge`; `Builder.Edge` turns facets and hidden boxes as they are written).
  Its boxes, the extrusion's own sockets (judged by `U` and `V` up the bar), the `o` sockets (facing `-x`), mass and the
  stand-in's bounds follow; the key is unchanged (`H` over `W` says it). "lay the bar flat" is gone; the size rule reads the
  sheet's width (`H` on edge).
- Fixed on the way: a cut panel's `back` socket is clamped onto its box. A wide loft with a seam at its middle (for example
  `loft 230x160x100 ... | r50,60,30,40,5 r100,60,30,40,5 r150,60,30,40,5`) passed `CutRefusal` and was then refused by the
  loader ("socket back ... not on that face"); keys that worked are unchanged.

### Measured (a throwaway probe, removed; this PC, which five other sessions shared)

The update that reads the keys (one packet), then the pumps:

| New keys | Before: the update | After: the update | After: pumps (worst; count) |
| --- | --- | --- | --- |
| 1 loft (three cuts) | 0.7 ms | 1.1 ms | 2.4 ms; 3 |
| 20 lofts | 11.5 to 16.2 ms | 4.8 ms (14 stand-ins) | 5.1 ms; 50 |
| 100 lofts | 69 ms | 4.1 ms (94 stand-ins) | 4.2 ms, one of 10.5 ms with a GC; 256 |
| 1 plate | 0.5 ms | 0.9 ms | 1.4 ms; 1 |
| 20 plates | 9.6 ms | 4.2 ms (12 stand-ins) | 2.4 ms; 16 |
| 100 plates | 37.6 ms | 3.8 ms (90 stand-ins) | 2.5 ms; 68 |

- A body that already has them: asking again for 100 keys, 0.01 to 0.03 ms.
- Every part made through stand-ins equals the same key made whole at once (boxes, colours, tones, sockets, mass, health,
  bounds): 0 differ in every run above.
- A stand-in costs about 0.015 ms; a first part 0.45 (plate) to 0.6 ms (loft), of which the loader's parse is 70%.
- **In the game** (window tool, Proving Ground at 140,0,-100, a host with a scratch blueprint of a foundation, a cut bar on
  edge and a chain of 40 cut plates of 40 different keys, a client joining after it was spawned; four runs): the client
  made 1 key and 40 stand-ins in the update that read them (4.1 to 6.0 ms); 16 to 19 pumps made them whole, 42 to 65 ms in
  all. The worst pump was 8.6 to 19 ms in each run: one slice of 7 to 17 ms wall, but its own thread's CPU cycles were 1.2 to
  2.5 M, against a mean of about 1.1 M a slice (0.5 ms): the main thread was waiting, not working (another thread or the
  machine's load). Not fixed; see the questions.

### Tried by hand

- `--form-panel` on `Cut Plate (solid 10x40x200 steel | r100,20,40,20,3)` (on edge: bounds 0.10 x 0.40 x 2.00 m, 4 boxes, 11
  sockets with `o0`), the same with a cut corner (5 boxes; `+x0`/`-x0` cut away), and the lying-flat bar and plate of phase 2
  (same counts as before). A second make in slices is the same part in each.
- Window tool, Proving Ground, `--spawn-at 140,0,-100`, the scratch blueprint (in `bin` only, removed), a 2 m bar 60 cm high
  and 10 cm thick standing on a foundation by `-y0`, a 40 by 30 cm opening in its middle:
  - **Offline:** rays square to the bar pass the opening (100,30) and hit beside, above and below it (100,55; 30,30; 100,8)
    at 1.95 m of 4 (its 5 cm half thickness).
  - **Joined client** (`--net join:127.0.0.1:27611`): the same rays the same way on the client's copy, and the last of the 40
    plates (the last stand-in made real) passes at its opening and stops beside it.
- `--check-construct-parts`, `--gen-parts all --check`: pass. Self-test by name, `Compound collision` and `Press stock`: pass.

### Not done, not verified

- `--form-panel` still does not print each opening's clear size.
- A player walking through an opening of a bar on edge (only rays), and bodies through it.
- The body rebuilds a stand-in's swap asks for were not timed on their own (they run in the world's tick; the in-game pumps
  above do not include them). A body of thousands of parts holding stand-ins rebuilds once a pump that fills one.
- A stand-in collides as its whole blank for a few updates: on a client a shot or a player at an opening in that time is
  stopped (the host's own bodies never use stand-ins unless a client's edit names many new keys at once).
- A malformed key from a peer now becomes a box stand-in rather than the old "Hull Bay T" stand-in, and logs once.
- Linux CI.

### Questions for the user (asked together; the first option is the recommendation)

1. **The first-part budget an update** (stand-ins past it).
   - A (recommended): 3 ms, as built: a packet's update stays under about 6 ms, and 100 keys are whole in about a second.
   - B: 1.5 ms: more stand-ins, a lighter update, whole a little later.
   - C: 5 ms: fewer stand-ins, an update near the 8 ms limit with the rest of a tick.
2. **The occasional slow pump on a busy PC** (a slice whose own work is 0.5 ms waiting 7 to 17 ms).
   - A (recommended): leave it: the work is within the budget; look again with the game alone on the machine.
   - B: move part making to a worker thread (the loader's parse off the game's thread): a bigger change, its own phase.
