# Holes through ships: the phase 0 spike

The study is the Claude Doc "Punching holes through capital ships" (https://claude.ai/artifact/PN79Syp1w9HwMJzfpEipQU); this is what the
spike built to measure it, and what it found. Karl asked for it on 2026-11-14 ("Start the spike now"). It is a measuring branch: nothing
in play fires these lines yet.

## What it built

- `Source/Constructs/HullHoles.cs`: holes as data. A holed part is a named run-time part, as a cut panel or a pressed part is. Its key is
  `Holed (Base | x,y,z,dx,dy,dz,r;...)`: the file part's name, then each hole as a line in the part's own frame (a point on it in cm, its
  direction in thousandths, its radius in cm). Every machine makes the same part from the key, so saves, blueprints and the wire carry it
  as text (protocol 318: after `Extrusion.WireMark` and `HullLoft.WireKind`, no new messages).
- The cut, per box: a plain box loses a square tunnel along the axis that opens least (up to four boxes round it); a thin hull facet
  (`"shape": "hull"`, the faceted ships' skin) is cut in its own plane into up to four convex facets round the opening; any other shaped
  box the hole reaches is left out. Sockets, health and links stay; mass goes with the solid volume left. Parts that can take holes:
  frames, panels, foundations, tanks and storage with no joint, rotor, rig, work, engine, router, use, status or panel boxes.
- `HullHoles.Bore`: the railgun's line through one body (server): every part it crosses, nearest first, takes a hole until its power is
  spent; the body is marked for a rebuild and its structure goes out as any edit's does.
- `--bench-holes [--holes N] [--radius R] [--capital P] [--lines-per-tick K] [--seed S]` (Source/App/BenchHoles.cs): a host and a client
  over loopback, the Rust Bucket and a capital hull of P parts (`SelfTest.CapHull`), seeded lines until about N holes; each tick timed
  apart (bore, rebuild, collider, server update with F9's areas, client update), then the client's parts checked against the host's.
- GL bench: `--gl-holes N` holes through the bodies before the timing, `--gl-holes-live K` K lines a frame (with `--bench-edit`).
- Window tool: `bore N [R] [GAP]` (host) N lines from the eye along the look; `holes` prints the holed parts. A part name in the window
  tool's verbs also finds its holed copies.

## What it found (cloud box, 2 cores; Karl's PC below)

12,000-part capital hull, one line a tick, 949 holes (about 19 parts a line):

| Per hit tick | median | p95 |
| --- | --- | --- |
| The holes themselves (keys and cuts) | about 0.03 ms a part | |
| Bore (the part tree built again after the last rebuild, then the line) | 2.7 ms | 5.3 ms |
| Body rebuild (full) | 4.5 ms | 15.6 ms |
| Compound collider built again | 8.1 ms | 14.5 ms |
| Server update (the structure patch goes out at 10 Hz for a big body) | 0.6 ms | 4.8 ms |
| Client update (it reads the patch and rebuilds its copy) | 0.06 ms | 13.3 ms |

The Rust Bucket (72 parts) costs well under a millisecond a hit. The client matched the host part for part in every run. 1,000 holes
added about 9,000 boxes (most parts 10 to 25 boxes; the Rust Bucket's deck 249 of 256).

Findings:

1. The cut is cheap; the whole-body work after it is not. Each hit rebuilds the whole ship, its collider and its part tree, and the
   client does the same: about 15 ms a hit on a 12,000-part hull here, so 300 hits a second can't run this way. Phase 1 must make a
   holed part a local edit: the journalled rebuild (`RebuildPlain`) taking a def swap, the collider and tree patched for the parts that
   changed, and hits on one body in a tick sharing one rebuild.
2. Holes kept in the part's key cap them by the key's length (240 characters): a part with a long name holds 6 or 7 holes, and the
   Rust Bucket's few big parts (keel, deck, roof 10-18 m) fill up after a few lines (242 of 268 refusals). Phase 1 should keep holes per
   ship (a list on the body, saved and sent with it), not per part.
3. Every new hull facet bumps the shape table (`PartShapes.Version`), which makes the renderer look at its meshes again; 261 bumps for
   1,000 holes here. To measure on the PC (below).
4. Holes are square tunnels and square cut-outs in facets: fine at railgun size, blocky for a shell's breach. Rounder openings cost
   boxes (eight a box for an octagon).

## Karl's PC (ChronosPC, 2026-11-14)

Headless, 12,000-part hull, run 2 of 2 (idle server tick 0.07 ms):

| Per hit tick | 1 line a tick, median / p95 | 5 lines a tick, median / p95 |
| --- | --- | --- |
| Bore (part tree built again, then the line) | 2.80 / 4.55 ms | 3.28 / 7.82 ms |
| Body rebuild | 4.54 / 5.59 ms | 4.56 / 8.12 ms |
| Collider | 5.35 / 7.40 ms | 5.70 / 6.65 ms |
| Server update | 0.46 / 3.14 ms | 0.46 / 3.02 ms |
| Client update | 0.05 / 9.07 ms (worst 20.4) | 0.04 / 9.10 ms |
| Sent | 9.6 KB a line | 0.6 KB a line |

About 13 ms a hit tick on the host, past the 8 ms budget, and five hits in one tick cost about what one does (they share the
rebuild); the patch sent at 10 Hz carries several hits at once. Building the 1,050 holed parts took 47 ms in all (0.045 ms each). The
Rust Bucket: about 0.1 ms a hit tick. The client matched the host part for part.

Frame rate (`--gl-bench "Proving Ground" --frames 120 --bench-bp "Rust Bucket" --bench-capital 12000`, 12,072 parts, mean of 3 views):

| | CPU | Frame | GPU | Draw calls |
| --- | --- | --- | --- | --- |
| No holes | 0.739 ms | 1.994 ms | 0.891 ms | 192-217 |
| 1,000 holes (`--gl-holes 1000`) | 0.735 ms | 2.025 ms | 0.893 ms | 209-234 |
| Plus a new line through each body every frame (`--gl-holes-live 1 --bench-edit`) | 22.2 ms | 23.7 ms | 0.933 ms | 358-514 |

Holes once made cost almost nothing to draw (+1.5% frame, +243 KB on the GPU). Made live, 60 lines a second on each ship, the CPU
spends about 22 ms a frame: 3 to 5 ms of it in the drawing's own chunk rebuilds ("bodies"), the rest in the body's rebuild and the cut
done in the frame (not split further). The GPU doesn't notice.

## Without rebuilding the whole ship (2026-11-14, second pass)

Karl asked whether a hit has to rebuild the whole ship. It doesn't: a holed part now goes through the plain-edit journal as a swap in place.

- `Construct.JournalSwap(i, def)` (ConstructRebuild.cs): the part's mass taken off and put back, its bounds counted off and on (a face
  is summed again only when the old shape was the last on it), the collider's leaves for that part dropped (`Remap`) and added again
  (`AddParts`, which now takes swapped parts with hull leaves), and `RebuildPlain` instead of a full rebuild. A swap the journal can't
  take (a merged, stored, routed or jointed part, a body under 1024 parts) falls back to the full rebuild as before.
- Faceted plates (`OwnShapes`, `Hulls`, `HullCovers`) count as plain for a swap (`PlainParts.OfShape`), and are no longer kept as special
  parts (`SpecialFree`): before this every plate on a capital hull was one, and each plain rebuild walked all 12,000.
- The collider's spare group for added leaves grows with the body (`Math.Clamp(LeafCount / 8, 512, 8192)`), so a run of holes doesn't
  fill it and force a rebuild.
- Clients get `Msg.Holes` (body, part count, then each part's number and its def as `ConstructStorage.WriteDef` writes it, 32 a
  message) instead of the whole structure patch; they swap the same way. A part whose count or base differs is left (counted as
  `Unplaced`) and the structure digest resyncs the body.
- `CALLOFSLOP_REBUILD_CHECK=1` compares each journalled rebuild with a full one: 114 of 114 matched, and the client matched the host
  part for part with 0 holes unplaced.

12,000-part capital hull, one line a tick:

| Per hit tick | Before (PC) | Karl's PC | Cloud box, bounds fix |
| --- | --- | --- | --- |
| Bore | 2.80 ms | 0.11 ms (p95 2.06) | 0.10 ms (p95 0.72) |
| Rebuild | 4.54 ms | 3.18 ms (p95 6.49) | 1.14 ms (p95 2.14) |
| Collider | 5.35 ms | 0 (worst 6.75) | 0 (worst 9.69) |
| Server update | 0.46 ms | 0.41 ms | 0.27 ms |
| Client update | 0.05 / 9.07 ms p95 | 0.05 / 6.61 ms p95 | 0.02 / 2.00 ms p95 |
| Hole messages | 9.6 KB a line (patch) | 1.3 KB a line | 1.2 KB a line |

About 13 ms a hit tick becomes about 3.5 ms on the PC (before the bounds fix: every capital plate sits on the hull's top face, so each
swap summed the bounds of all 12,000 parts) and about 1.5 ms on the cloud box after it. The collider's rare worst ticks are its spare
group being rebuilt once it fills.

GL bench on the PC (journal, before the bounds fix): 1,000 holes standing cost 1.73 ms a frame (CPU 0.67); a new line through each ship
every frame cost 6.8 ms of CPU (was 22.2), 3.4 to 4.9 ms of it the drawing's own chunk rebuilds ("bodies"). Those chunks are the next
thing to make local.

## For phase 1

- The host and client side of a local edit is done (above). Left: the drawing's merged-mesh chunks rebuilt for the parts that changed,
  not the whole body's (hits on one body in a tick already share one `RebuildPlain`). Then the battle size Karl confirmed (about 10 capital
  ships, 300 penetrating hits a second) is measured again.
- Keep holes per ship, not in the part's key.
- Round the openings for breaches big enough to board through.
