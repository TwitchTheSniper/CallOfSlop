# Limits (fixed caps and what they protect)

Every hard limit in `Source/` that a big Sandbox build can reach: `Max*` / `Most*` / `*Cap` / `*PerTick` / `*PerPacket`
constants, fixed pools and the narrowing casts found on the wire. Audited on 2026-11-06 (branch `feat/caps-audit`) after
`ConstructParts.MaxSockets` went from 64 to 255 (ba67442a), because capital ships and a traction city kept hitting them.

Not listed: physics tuning that isn't a capacity (top speeds, accelerations, slopes), and the archived modes (Zombies,
Frenzy, bots, PvE, the arena modes: `Game/Modes/`, CTF, the casino, grenades, launchers, tactical devices), the vehicle kit (`Source/VehicleKit/`, its own agents) and the self-tests.

## The four classes

- **Wire width**: a field's size on the wire (a byte count, a u16 index, bits of a mask). Keep it, or widen it with a
  protocol bump when it blocks big builds, and say the cost in bytes. A count that is a byte may rise to 255 for free.
- **Cost budget**: tick time (8 ms a server tick), draw calls, memory. Make it scale with size where possible (a window
  that turns, a fixed number of tests a tick), and say so when it is reached (`[caps]` lines, below).
- **Data sanity**: a load check on files, blueprints and settings. Raise it to what the wire and the engine allow.
- **Pool size**: zero-allocation arrays. Growable, but only outside the tick or when a new item is added (grow by
  doubling, never shrink), or sized from the world at load.

## When a limit is reached: `[caps]` lines

`Source/Perf/Caps.cs`: `Caps.Hit(what, cap, want)` writes one line to the log, at most every 30 s for each limit
(`[caps] guns a construct: the limit of 256 was reached`), and counts `Caps.Hits`. Nothing allocates unless a line is
written. Called where something is otherwise left out without a word: wheel and track rays, guns, thrusters, rotors,
modular engines a construct; shells in the air; gun events a tick. A user's "my last turrets don't fire" is then read from
their log. Add a call wherever a new silent cap goes in.

## What changed in this audit

| Limit | Before | Now | Why it was safe |
| --- | --- | --- | --- |
| Ordnance Rack stations (`ModularRack.MaxStations`) | 56 | 247 | it is `MaxSockets - 8` (five mounts), so it followed the 255 sockets; the hooks stay one rail a column (16 to 23 boxes at the most stations), a salvo is one bomb a tick, each a shell |
| Shells in the air (`ConstructWorld.MaxShells`) | 256 | 1,024 | the tick tests at most `ShellTestsPerTick` (22); only a crowded sky is looked at less often (a hit up to 0.8 s late at 1,024). The pool is made with the first shell, ~300 KB |
| Guns a construct (`Construct.MaxGuns`) | 64 | 256 | per-gun arrays already grew; the recoil list is a segment each (now its own `MaxRecoils`, 64, the old memory) |
| Thrusters a construct (`Construct.MaxThrusters`) | 64 | 512 | the array was 64 on every body (loose blocks too); now grown at a rebuild. The keep-level match is one walk |
| Modular engines a construct (`ConstructEngine.MaxEngines`) | 8 | 32 | `EngineOf` is a byte; bank scratch 256 slots on the stack |
| Freeform welds a construct (`Freeform.MaxPerConstruct`) | 64 | 192 | they share the 255 segments with joints, which already bound the cost |
| Cargo volumes, Storage Volumes, Tank Volumes a construct | 4 each | 32 each | checked only when fitted; nothing is sized by them. Each frozen cargo volume is a broadphase look at 10 Hz |
| Bodies a construct carries (`ConstructCargo.MaxPerConstruct`) | 24 | 128 | `Construct.Cargo` was 24 entries on every body; now grown by doubling when a carrier holds more than ever before |
| Couplings, wires between constructs, hoses, world links (the world) | 32, 64, 32, 64 | 255 each | each count is a byte on the wire already; `WriteLinks` reserves its 4.6 KB; a contact pair looks through at most 255 coupled pairs |
| Run-time part names (`ConstructParts.MaxRuntimeName`) | 32 | 128 | a sanity check: names are length-prefixed in saves and never in a fixed-width field. Part files keep 32 (`MaxName`) |
| Pose air window start (`ConstructNet.WriteAir`) | u8 | u16 | **a bug**: it wrapped past 255 rotors plus control surfaces, so a client set the wrong surfaces. Protocol 189 (+1 byte a pose that has air) |

## Risky, not changed (proposed)

- **Joints a construct, 255.** A segment is a byte everywhere (parts, joints, the pose's window, saves). Widening is a
  protocol-wide change; freeform welds (192 now) and joints share it.
- **Wheel and track rays, spring feet, 256.** A raycast each a tick. A traction city on many track units may reach it;
  it is logged now. Raising it wants a measured cost (or rays thinned on big bodies, like the shells' tests).
- **Rotors 32, control surfaces 512.** Blade elements and surfaces each a tick; logged (rotors).
- **Gun events a tick, 255.** The `CFire` count is a byte and its pool 256. Past it, clients miss tracers and sounds (the
  shots still hit). Raising needs a u16 count and a split across packets (255 shots are already ~4 KB).
- **`ConstructTerminal.MaxTerminals` 64** (more read as having no stores) and **`MaxEdges` 2,048**: a big base may reach
  them; the hop search's cost would need measuring.
- **Beacon networks 64** (a bit of a 64-bit mask) and **routed links 254 a construct** (a parent byte): wire widths.
- **`ConstructParts.MaxBoxes` 256** (was 32, a draw call a box): raised with merged meshes (docs/render/MERGED-MESHES.md),
  which draw a static part's boxes in its chunk's few draws. Moving parts are still drawn box by box; designed ordnance keeps
  32 (`OrdnanceDesign.MaxBoxes`).

## Parts a construct: 8,000 to 16,000 (branch `feat/max-parts`, protocol 190)

What was done:

- **Scratch, not the stack.** The ~80 spans sized to `PartCount` or `LinkCount` (floods, the drive line, the drill feeds, the
  logic step, merges, power, guns, routes' legacy migration, splits, the engine union-find) take their memory from
  `Scratch` (`Source/Constructs/Scratch.cs`): one byte arena a thread, used last in, first out (`using var scratch =
  Scratch.Begin();` then `scratch.Take<T>(n)`, cleared as `stackalloc` was). It grows by doubling when a frame needs more
  than ever before, and `Construct.ReserveParts` / `ReserveLinks` grow it on the edit (`Construct.ScratchBytes`), so a
  steady tick allocates nothing; spans taken before a growth stay valid. Small spans with a fixed bound (a part's
  sockets, the joints, a track's wheels, `Rot24`) stay on the stack. A 16,000-part scene ran on a thread with a 1 MB
  stack (Windows' main thread). The gun step's per-gun span had been a `stackalloc` inside the loop (a part count of stack
  for every gun, never given back until the method returned); it is a frame a gun now.
- **Widened.** `ConstructEngine`'s union-find is `int` (was `short`); `PaintStroke.Key` and `RemoverSelection.Key` hold a
  31-bit index (were 15 bits; local hash keys, not on the wire). Seat keys that packed a part index in 8 bits (the
  stabilised look `_stabSeat`, `_sbViewSeat`, the third-person seat key, the configure look key) mixed seats past part
  255 even at 8,000; they hold 16 bits now.
- **On the wire (protocol 190).** Part indices and the link count were u16 already (48,000 links fit). A Full's count of
  part settings off their defaults was a u16, which wraps past four settings a part at 16,000 parts: it is a u32. A Full
  of a 16,000-part plate grid is 482 KB, at Steam's 512 KB message limit (`SteamTransport.MaxMessageSize`), so a reliable
  message over it goes as `Msg.Fragment` pieces of 256 KB on its own lane and is put back together before the game reads
  it (tried with a 1.2 MB message on the recorded backend). RakNet splits big messages itself.
- **Poses.** A pose found its signal parts by walking every part three times a body (16,000 parts each pose tick); the
  list is kept with the construct now (`ConstructNet.Signals`, found again after a rebuild or a part count change).
- `MaxParts` 16,000, `MaxLinks` 48,000.

Measured in the container (a throwaway harness, not committed; a 2-core box at load 8 to 12 with other agents building,
so numbers are min / median of repeats and real PCs are faster). Each world: a Wired Buggy with a grid of Frame Blocks
linked on four sides (two links a part), one driven and pushed at 3 m/s, one parked:

| | 8,000 parts (15,752 links) | 16,000 parts (31,689 links) | Against |
| --- | --- | --- | --- |
| `Rebuild` (one edit) | 1.7-2.5 / 2-8 ms | 5.4-8.7 / 6-13 ms | 8 ms a tick: **at the edge** at 16,000 |
| Add a part and take it off (two rebuilds, a split check) | 3.9-4.8 / 4-8 ms | 10-22 / 14-44 ms | a tick with one edit each way |
| World step, both bodies | 0.73-0.78 / 0.9-1.2 ms | 1.1-1.3 / 1.3-1.8 ms | 8 ms: fine |
| Logic step, one body | 0.11-0.16 ms | 0.38-0.48 ms | |
| `WriteFull` | 240 KB, 0.4 ms | 482 KB, 0.9 ms | Steam's 512 KB (fragments past it) |
| `ConstructPatch.Write` (one change) | 0.8 ms | 1.7 ms | at most every 6 ticks a dirty big body |
| `StructureHash` | 0.12 ms | 0.3-0.4 ms | |
| `WritePoses` (both bodies) | 0.15-0.2 ms before the signal list, ~0 after | 0.6-0.8 ms before, ~0 after | per pose tick per client |
| A client reading the Full | 2-5 ms | 6-15 ms | once a structure change, a client |
| Steady allocation | 0 B a tick | 0 B a tick | |
| Scratch arena | 1 MB | 2 MB a thread | |

The steady tick holds easily. The cost that grows is the **rebuild** (an edit, a part broken off, a split): mostly
`BuildMass`, `BuildSegments`, `ConstructPower.Rebuild` and the drive line, each a walk over every part, at 16,000 parts
about as long as the 8 ms budget in this container. That is why the cap stops at 16,000 and not at the 21,845 the link
count allows: going further wants an incremental rebuild (only the systems an edit touches) first. (Since done for plain
edits: "Rebuilds" below.) The "Compound
collision" cost scenes build hulls of the cap: at 16,000 one hull sliding 0.83 ms a tick, one on half-metre ground with 40
blocks 3.7 ms, eight stacked 126 ms (59 ms at 8,000 on the same box; it was over budget already). Not measured on Windows. Steam's default send buffer is 512 KB (`k_ESteamNetworkingConfig_SendBufferSize`, not set by the game), so a
Full near that size sent while other reliable data waits may be refused; the transport logs it and the far side drops the
part it has when the next first piece comes. Raising the buffer is a backend setting worth adding.

## Rebuilds: by the journal (branch `feat/incr-rebuild`)

What a rebuild costs a big body after an edit, and how it is kept down (the design: docs/sandbox/CONSTRUCTS.md,
"Rebuilds"; the code: `Source/Constructs/ConstructRebuild.cs`):

- **One rebuild a tick.** An edit only marks the body; the tick's first look at it rebuilds it once, so ten edits in a
  tick are one rebuild (as before; now counted in the measurements below).
- **The journal.** A body of 1,024 parts or more (`Construct.IncrementalMin`) keeps the plain edits since its last rebuild
  (plain structure added, linked or taken off, a part breaking off, a plain piece cut away). After those only it rebuilds
  by the journal (`RebuildPlain`): the mass, centre of mass and inertia from sums kept in doubles (each part adds or takes
  off its own share; summed from the parts again every 256 plain rebuilds), the collider and the part tree renumbered
  (new parts in a small tree of their own), the circuit flooded only if a powered part's neighbours don't meet round it
  within 512 parts, every system built from the special parts alone (`Construct.Specials`). `RemovePart` skips its flood
  over the whole body when the part's neighbours meet round it.
- **Full rebuilds are cheaper too.** The systems walk the special parts, not every part; the drive line's walk reads each
  part's links, not every link for every part it reaches; a big collider's trees are built from Morton order (radix
  sort, cut at the lattice's cells: O(n)) instead of sorting the leaves at every level; compacting moves each run of parts
  that stay in one copy.
- **Still full**: an edit on a special part (a gun, a wheel, a battery, a seat, anything with a role, sockets other than
  structural, a store, a routed link on it), a part on a joint's segment, a body with freeform welds or merged receivers,
  a joint added or cut, a split that takes a special part with it, a change of load alone (that is `RefreshMass`: for a
  big body now from the sums and the special parts' loads, O(special parts)), and any other `Dirty = true`.
- **A split of a big piece can't be spread over ticks.** The flood that finds what is cut off, copying the piece into its
  own body and that body's mass and collider are all needed before the next physics step moves the two bodies apart;
  what could wait (the new wreck's systems) is now a walk over its special parts, a fraction of a millisecond. The piece's
  collider is already built only when the physics step first asks for it.
- **The debug check.** `CALLOFSLOP_REBUILD_CHECK=1` (or `Construct.RebuildCheck`) follows every plain rebuild with a full
  one and compares mass, centre of mass, inertia, bounds, segments, circuit, drive line, wheels, special parts, the
  collider's leaves and trees, and the part tree. Over 1,300 random ticks of edits (removes, adds, breaks, new links, on
  bodies of 1,500, 3,000 and 5,000 parts, with and without a battery, with horns as special parts on the frame): 1,261
  plain rebuilds checked, none different.

Measured in the container (a throwaway harness, not committed; the 2-core box at load 5 to 15 with other agents building,
so numbers are min / median of repeats; main and this branch run back to back). A Wired Buggy with a grid of Frame Blocks
linked on four sides, driven at 3 m/s; "edit + step" is the edit and the tick that rebuilds it, less a steady tick:

| | 8,000 parts, before | 8,000, now | 16,000 parts, before | 16,000, now |
| --- | --- | --- | --- | --- |
| `Rebuild`, full | 1.7-1.9 / 2.3-2.8 ms | 0.8-1.1 / 1.0-1.3 ms | 5.2-6.5 / 5.9-16 ms | 2.7-3.3 / 3.5-5.9 ms |
| Collider built | 16-21 / 22-23 ms | 0.7-0.9 / 0.7-1.0 ms | 9-17 / 15-21 ms | 1.4-1.9 / 1.7-2.0 ms |
| Add a block (edit + step) | 7-10 / 11-23 ms | 0.05-0.08 / 0.10-0.13 ms | 20-42 / 21-48 ms | 0.08-0.24 / 0.15-0.32 ms |
| Take off an inner block (edit + step) | 6 / 10-14 ms | 0.25-0.39 / 0.35-0.47 ms | 18-23 / 21-28 ms | 0.6-0.8 / 0.8-1.3 ms |
| Take off the last block on a face of the bounds | 10-13 / 17-18 ms | 0.4-0.6 / 0.5-0.7 ms | 20-23 / 24-32 ms | 0.9-1.2 / 1.0-1.4 ms |
| Ten blocks off in one tick | 13-17 / 22 ms | 1.3-1.4 / 1.4-1.6 ms | 30-33 / 34-37 ms | 3.0-6.9 / 3.6-9.0 ms |
| Four blocks shot off a tick (each a debris body) | 8-13 / 14-17 ms | 0.9-1.0 / 1.4-1.7 ms | 20-25 / 30 ms | 1.9-2.0 / 2.3-5.7 ms |
| A split: the whole grid falls away (with the step) | 16-20 / 21-47 ms | 7-21 / 8-21 ms | 30-32 / 32-64 ms | 19-42 / 19-59 ms |
| Steady allocation, ten removes and their ticks | 0 B | 0 B | 0 B | 0 B |

A typical edit at 16,000 parts is under the 2 ms target (0.15-1.4 ms) and ten in a tick are 3.6 to 9 ms in this container
(each take-off still compacts the arrays and indexes the links again: O(parts) memory moves, 0.2 ms at 16,000). A split
of a big piece is O(its parts) and stays over the budget, as it was. "Compound collision" with the Morton trees (same
runs): one 16,000-part hull sliding 0.84-0.97 ms (main 0.59-0.83), a hull and 40 blocks on half-metre ground 2.2-4.2 ms
(main 3.2-4.5), eight stacked 98 ms (main 111-126 ms): over the budget either way.

**`MaxParts` stays at 16,000.** For edits the numbers would hold the 21,845 parts the wire's link count allows (about 1.4
times these: a take-off under 2 ms). What doesn't hold: a split of a big piece (already 19 to 59 ms here), big hulls
pressed together (98 ms at 16,000), and a Full of a plate grid (482 KB at 16,000, Steam's 512 KB message: fragmented, but
a client reads it in 6 to 15 ms). Raising it wants a split that doesn't copy the piece part by part and cheaper big
contact pairs first.

## Fused units (branch `feat/fuse`, protocol 193)

What fusing a big build buys (the design and the rules: docs/sandbox/CONSTRUCTS.md, "Fusing"). Measured with
`--bench-fuse 16000 --capital --sheet --stack` in the container (2-core box at load 5 to 12 with other agents building:
min / median of repeats; both columns in one run). The sheet: 15,920 Frame Blocks in a 12-wide sheet linked on four
sides and 80 Lamps (special parts, left out of the unit); "fused" has the blocks in one unit. "Edit + step" is the edit and
the tick that rebuilds it, less a steady tick measured just before.

| | Plain | Fused |
| --- | --- | --- |
| Collider: leaves, memory in use | 16,000, 1,767 KB | 123 (43 rough boxes and the lamps' own), 13 KB |
| Collider built | 4.9 / 5.1 ms | 0.19 / 0.21 ms |
| Rough fit (when made, after a split, after an eighth gone) | | 3.0 / 3.2 ms |
| `Rebuild`, full | 15.0 / 15.3 ms | 5.5 / 5.7 ms (the mass sums: 7.7 of the plain one's ms, 1.7 fused) |
| Sliding on a floor | 2.7 / 3.0 ms a tick | 0.82 / 0.93 ms |
| 40 blocks dropped on it (construct against construct) | 4.7 / 5.2 ms | 1.5 / 2.3 ms |
| On half-metre ground | 3.4 / 6.7 ms | 0.77 / 3.1 ms |
| Eight such hulls stacked, the top ones sliding | 32 / 45 ms | 4.2 / 4.8 ms |
| A part placed beside it | 0.44 / 0.36 ms | 0.13 / 0.14 ms |
| Take off an inner block | 1.96 / 1.91 ms | 1.89 / 1.84 ms |
| Ten blocks off in one tick | 8.0 / 9.7 ms | 5.9 / 14.5 ms (one median caught a stall; 6.0 to 6.6 in other runs) |
| Four blocks shot off a tick | 6.5 / 6.0 ms | 4.5 / 4.7 ms |
| A special part added (a full rebuild) | 10.2 / 8.8 ms | 6.1 / 6.3 ms |
| Cut in half (a split) | 38 ms | 37 ms |
| Steady allocation, 30 ticks | 0 B | 0 B |

A capital hull (`SelfTest.CapHull`, 15,600 decks and plates, 300 by 416 m, 400,000 t): collider 54,600 leaves built in
17.6 ms against 609 rough boxes in 1.0 ms, the fit 5.6 ms, a full rebuild 3.9 against 2.3 ms, a tick on the floor 0.87
against 0.34 ms. (At this size the joint tests' floor holds it in neither form: it sinks through, plain more slowly. At 4,050
parts both rest on it, within a millimetre of each other.) Drawing is unchanged (docs/render/MERGED-MESHES.md, "Draw
groups and fused units"). Mass, centre of mass and inertia were the same, plain and fused, in every scene; with
`CALLOFSLOP_REBUILD_CHECK=1` 44 journalled rebuilds of fused and plain bodies matched a full one.

Where fused is not cheaper:

- **Take off an inner block** is level (1.9 ms either way): the journal already made it cheap; fused it builds the small
  collider again where plain remaps the big one, which comes out the same.
- **The fit itself** is new work, 3 to 6 ms at 16,000 parts, done in the tick that needs the collider after the unit is
  made or grows, after a split, or after an eighth of its members have gone. On a slow PC it can pass the 8 ms budget for the
  biggest units; between fits the rough shape stays loose round what went.
- **A split** costs the same: it is the split's copying, not the unit.
- The ground scene allocated in both forms (6.5 MB plain, 0.5 MB fused in 40 ticks, the ground's own lists growing at first
  use); it is not new.

The network (`--bench-fuse-net 120 3`: a host and a client over RakNet on one PC, the client at 120 ms and 3% loss): the
client's own requests fused a 240-block sheet, un-fused it, fused half of it as a selection, and a row the host shot across
the fused half split it; each step reached the client's copy within 0.2 s, no packet dropped.

## Massive blasts (branch `feat/ord-mega`, protocol 195)

The Fission, Fusion and Antimatter Warheads (docs/sandbox/CONSTRUCTS.md, "Massive blasts") go off 166, 380 and 612 m
across at their own length and 25 cm, kilometres more when built wider; the engine holds any blast to 2 km. A blast of
`MegaRadius` (100 m) or more is a shock front, not a one-tick blast (`Constructs/ConstructMegaBlast.cs`):

- **The front** grows at `MegaFrontSpeed` (340 m/s), faster for the biggest so none takes more than `MegaFrontSeconds`
  (4 s) to reach its edge. Each tick the players it passed take their share (the game's `MegaFront`, one distance and at
  most one ray each), and the bodies it reached are walked nearest first: about `MegaPartsPerTick` (800) parts a tick over
  every massive blast at once, a body at a time (the budget may run over by one body's parts), its broken parts taken out
  together by `RemoveParts` (one structure rebuild; one by one, a 60-part hull cost 0.1 ms a part and a fusion blast
  over 300 hulls took 49 ms in one tick). A body wholly inside `MegaVaporiseShare` (half) of the radius is removed at once.
- **The bodies**: the nearest `MegaMaxBodies` (2,048; `[caps]` past it), sorted once at the start. At most `MaxMegas` (4)
  at once (`[caps]`); a fifth landing goes off as a plain blast, a fifth set off on its rack waits half a second.
- **Wrecks**: pieces it cuts off fall as wrecks up to `MegaDebrisMax` (96) a blast, then are gone, and it throws a body
  at most `MegaShoveMax` (30 m/s): the cost after a blast is the physics of what it threw (the fission row below).
- **The crater** (`Game/GameSandboxMegaBlast.cs`): at most `MegaCraterMax` (120 m), dug behind the front (it waits
  until the front is past it) a band of rows a tick, each band at most `MegaDigColumnsPerTick` (3,500) columns: one
  `DigOp` with its slice and slices (two more bytes; at most 255 bands), logged and sent as any op (17 bytes), so the log
  takes a 120 m crater in 52 ops and a joiner replays them. At most `MaxMegaDigs` (8) craters wait; a ninth digs a prop
  bomb's 60 m at once. The first big crater on a map makes its columns (about 48 B each, 6 MB for 120 m); later ones
  reuse them.
- **The host's cap**: the lobby's "Biggest blast" (Settings `sandbox_max_blast`: no limit, 1,000, 500, 250 or 100 m)
  shrinks any blast's radius and its crater with it (`ConstructWorld.CapRadius`).
- **The look** (`Render/Fx/MegaBlastFx.cs`): at most 4 drawn at once (the oldest gives way), about 140 particle births a
  sixtieth of a second each for 16 s (the pool's own caps still apply), a full-screen rect, one dynamic light, two sounds.

Measured with `--bench-mega KEY --map "Proving Ground"` (a host and a client over loopback; 300 hulls of 60 Frame
Blocks out to 202 m, two players; the second of two runs; 2-core box with other agents building), every server tick of
the blast's life timed:

| Warhead (25 cm, own length) | Server tick median / worst over 5 s | Over 8 ms | The front's walk, worst tick | A crater band, worst | Allocated | Sent to the client | Left |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Before (steady, the same scene) | 1.2 / 1.6 ms | 0 | | | 0 | | 321 bodies |
| Fission, 166 m, crater 60 m | 2.1 / 7.1 ms | 0 | 0.37 ms | 0.88 ms | 1.1 MB (wrecks made) | 441 KB, worst tick 10 KB | 278 bodies, 9,248 parts |
| Fusion, 380 m, crater 110 m | 0.43 / 1.9 ms | 0 | 0.23 ms | 0.48 ms | 294 KB | 106 KB, worst tick 2.7 KB | 31 bodies |
| Antimatter, 612 m, crater 99 m | 0.04 / 3.8 ms | 0 | 0.14 ms | 3.7 ms | 21 KB | 56 KB, worst tick 0.7 KB | 2 bodies |
| Without a crater (`--no-crater`), antimatter | 0.05 / 1.5 ms | 0 | 0.09 ms | | 16 KB | 35 KB | 2 bodies |

The biggest the engine allows (`Ordnance:B200:...Antimatter Warhead@length=5.4...`, 8.9 km asked, held to 2 km, a 120 m
crater in 52 bands) on Streamed Terrain over 300 hulls out to 600 m: the front's walk 0.15 ms and a band 0.46 ms at
worst; the ticks stayed at the scene's own 11 ms (a hundred hulls sliding on its slopes before the blast) until it had
removed them all, then 0.04 ms. The client applied every band (49 ops), at worst 7 ms of its update in one tick.

**On a planetoid** (2026-11-07, the user's report of an antimatter warhead on the Planetoid as host with three players; the perf snapshot sent was taken after the blast and showed none of it). Big blasts there had never been run, only built:

- **Crater bands.** A planetoid's dug column costs several times a flat map's, so a 3,500-column band took 2.9 ms (warm) to 5.8 to 10 ms (with a scene) of a tick, and a client's update up to 4.8 ms. Bands there are now `MegaDigPlanetColumnsPerTick` (1,000): a 120 m crater in about 200 bands (3.3 s), 0.4 to 0.5 ms each with nothing on it, 3 ms worst with hulls and three peers, a client's update 0.8 ms (`--bench-mega --map Planetoid --bodies 0 --immune`, the second run).
- **The first crater on a map** made its 195,000 columns in the bands that dug them, doubling the store as it went: 23 MB allocated, its worst band 56 ms. Now the ground makes room ahead (`IDigGround.Reserve`, 15,000 span lists a tick over the crater's wait behind the front, the store sized once): bands at most 7 ms that first time, though the one tick that sizes the store (5 MB of large arrays) still took 38 to 54 ms on the shared 2-core box. Later craters on that map allocate nothing.
- **The look.** Its particles are tens to hundreds of metres across, so seen from inside the cloud every one covers the screen. Under llvmpipe (`--mega-host --clients 0`, 960 x 540) the host's frames went from 79 ms to 925 ms for 20 s, the time all in drawing them; fewer are now born for a viewer inside the radius (`MegaBlastFx.Crowd`: a fifth inside a third of it, all of them past it), and the same blast's frames are 337 to 375 ms. What is left is mostly the dug ground's drawing (the Terrain area, 13 ms before to 125 to 150 ms after, planet crater chunks: map code, not changed here) and llvmpipe's own filling.
- **Measured in the real window**: `--mega-host [KEY] [--clients N] [--hulls N] [--dist M] [--seconds S] [--warm]` and `--mega-client` (`Source/App/Program.MegaWindow.cs`), one process each under xvfb plus `--net-client` for a third player: the host lays hulls round a spot ahead, sets the design off there as if landed, and every PC logs each frame's perf areas from 5 s before to S s after, the worst frames and the worst server tick. `--warm` sets off a small one first, unmeasured, so the JIT is warm. On the 2-core box three windows under llvmpipe share two cores with other agents, so their frame times are tens of times a real GPU's: read them for what grows, not for milliseconds. With `--warm`, host alone: one server tick median 0.34 ms, worst 6.7 ms through the blast (the scene of 100 hulls before it: 2.6 ms); with two clients the host's worst tick was 17 to 23 ms in frames of 6 ticks under that contention, and its front walk 4.9 to 7.8 ms at worst (0.13 ms headless).
- **Not done.** The dug ground's drawing on a planetoid and llvmpipe's fill are not changed (map and terrain code belong to another developer); the window client measure lost its session before the big blast in three of five runs (the first two from its own timeouts, since fixed; the last not chased), so the client frames above come from the one run that measured it, before the particle change: frame median 166 ms before, 865 ms through the blast, Render 852, Particles 57 to 116, Meshes 120 to 260, Terrain 61 to 271, packets read 55 ms at worst (34 crater bands of 3,500 columns then).

Before the crater waited for the front, digging it under the hulls woke them all onto dug ground every tick: 6 to 9 ms
ticks for half a second. A blast with nothing to walk costs a hundredth of a millisecond a tick. Frames: under llvmpipe
the effect's particles cost nothing measurable beside the 100 ms frames; its births are 0 bytes (the pool's), checked on
the 4.6 and 4.1 paths.

## The inventory

Grouped by class; the rows changed in this audit come first in each group. "What it protects" is the code's own comment
where it has one. Line numbers are those of this audit.

| Class | Limit | Where | Value | What it protects | Action |
| --- | --- | --- | --- | --- | --- |
| Wire width | `MaxHoses` | `Constructs/ConstructHoses.cs:34` | `255` | in the world (was 32): its count is a byte on the wire (ConstructNet), so 255 is the most; docs/perf/LIMITS.md | **Raised 32 to 255 in the world** (byte count) |
| Wire width | `MaxCouplings` | `Constructs/ConstructLinks.cs:39` | `255` | in the world (was 32): its count is a byte on the wire (ConstructNet), so 255 is the most; docs/perf/LIMITS.md (a contact pair looks thro... | **Raised 32 to 255 in the world** (its count is a byte in Links); a contact pair looks through the coupled pairs (`_exA`), 255 compares at most |
| Wire width | `MaxXWires` | `Constructs/ConstructLinks.cs:40` | `255` | in the world (was 64): its count is a byte on the wire (ConstructNet), so 255 is the most; docs/perf/LIMITS.md | **Raised 64 to 255 in the world** (byte count); Links reserves its 4.6 KB |
| Wire width | Pose air window start | `Constructs/ConstructNet.cs:1508` | u8, now u16 | which rotors and control surfaces a pose carries (`PoseAir` 24 of them, a window that turns) | **Fixed** (narrowing bug): `first` wrapped past 255 on a body with many surfaces, so clients set the wrong surfaces. Protocol 189, one byte more a pose with air. Round trip tried with 600 surfaces |
| Wire width | `MaxStations` | `Constructs/ModularRack.cs:52` | `ConstructParts.MaxSockets - 8` | in all (247: a part's 255 sockets, with room for its five mounts; a station's socket is a byte on the wire). A salvo lets one go a tick (... | **Now 247** (`MaxSockets - 8`, follows ba67442a's 255 sockets; was 56). Tried: 16x15 grid (16 boxes, 245 sockets), 15x16 drum (23 boxes, 243 sockets), keys round-trip, 16x16 refused. A salvo is one a tick (about 4 s), each a shell |
| Wire width | `MaxWorldLinks` | `Constructs/WorldLinks.cs:53` | `255` | in the world (was 64): its count is a byte on the wire (ConstructNet), so 255 is the most; docs/perf/LIMITS.md | **Raised 64 to 255 in the world** (byte count); nodes still share `MaxWorldNodes` |
| Wire width | `MaxLinks` | `Constructs/Construct.cs:232` | `MaxParts * 3` | - | **48,000** (follows `MaxParts`): the CBody link count is a u16 (65,535), so 21,845 parts at most without widening it |
| Wire width | `MaxJoints` | `Constructs/Construct.cs:231` | `255` | a segment is a byte: 0 is the body, k hangs on joint k - 1 | Kept: a segment is a byte everywhere (CPart.Seg, CJoint.ParentSeg, the pose's joint window); widening is a protocol-wide change |
| Wire width | `MaxBeacons` | `Constructs/ConstructBeacons.cs:340` | `64` | a network is a bit of a player's mask: this many networks at most | Kept: a network is a bit of a 64-bit mask |
| Wire width | `MaxEntries` | `Constructs/ConstructBeacons.cs:979` | `240` | kinds a message carries (4 bytes each); a longer change takes more messages, a tick each | Kept |
| Wire width | `MaxBanks` | `Constructs/ConstructEngine.cs:162` | `8` | modular engines a construct (was 8; EngineOf is a byte, the bank scratch is 256 slots on the stack); past this the pieces are logged and... | Kept |
| Wire width | `MaxBladeStyles` | `Constructs/ConstructExtrusion.cs:69` | `8` | three bits of a Fan's Wall | Kept |
| Wire width | `MaxEntries` | `Constructs/ConstructFluid.cs:52` | `100` | tanks a packet carries (it stays under the MTU | Kept: tanks a packet carries, the rest next time |
| Wire width | `MaxKinds` | `Constructs/ConstructInventory.cs:12` | `ConstructParts.MaxTypes` | part ids (two bytes since protocol 90 | Kept |
| Wire width | `MaxMaterials` | `Constructs/ConstructInventory.cs:13` | `64` | slot 0 is unused: RecyclingTable ids are 1-based | Kept: follows the recycling ids |
| Wire width | Pose joint window | `Constructs/ConstructNet.cs:1575` | u8 start, 96 a pose | the joints a pose carries | Kept: joints are at most 255 |
| Wire width | `CFire` count | `Constructs/ConstructNet.cs:236` | 255 a tick | what the guns did this tick (a byte count; the pool is `Fires[256]`, ConstructWorld.cs:133) | Kept, now logged when full (`[caps] gun events a tick`): only the clients' tracers, sounds and shatters are lost, the shots still hit. Raising needs a u16 count and a packet split |
| Wire width | `MaxTypes` | `Constructs/ConstructParts.cs:795` | `4096` | - | Kept: part ids u16; 4,096 file parts (run-time parts use the extrusion id space) |
| Wire width | `MaxSockets` | `Constructs/ConstructParts.cs:798` | `255` | a part's sockets: indices 0-254 (one byte on the wire, CLink.SA/SB; 255 means "no socket" in places such as ConstructModules.LinkWhy | Kept (ba67442a): a socket index is a byte, 255 means none |
| Wire width | `MaxPartMass` | `Constructs/ConstructParts.cs:800` | `50_000_000f` | kg, hp, which three bytes carry; a capital chassis deck | Kept: health and mass ride three bytes |
| Wire width | `MaxPartHealth` | `Constructs/ConstructParts.cs:800` | `16_000_000f` | kg, hp, which three bytes carry; a capital chassis deck | Kept: u24 on the wire |
| Wire width | `MaxMaterials` | `Constructs/ConstructRecycling.cs:15` | `63` | the inventory holds 64 slots; id 0 is "none"; ids are one byte on the wire, so this is only arrays | Kept: material ids are a byte |
| Wire width | `MaxBlades` | `Constructs/ConstructRotor.cs:57` | `16` | CfgKey.BladeLost and the wear settings are 16-bit masks; past this a disc is near solid anyway | Kept |
| Wire width | `MaxRoutes` | `Constructs/ConstructRoutes.cs:78` | `254` | a route's parent is a byte, 255: none | Kept: a route's parent is a byte, 255 none; RouteCount is a byte on the wire |
| Wire width | `MaxKinds` | `Constructs/ConstructStorage.cs:43` | `256` | a Storage Volume's parts: kinds, and how many of one (each a u16 on the wire | Kept |
| Wire width | `MaxPerKind` | `Constructs/ConstructStorage.cs:43` | `60000` | a Storage Volume's parts: kinds, and how many of one (each a u16 on the wire | Kept |
| Wire width | `MaxRack` | `Constructs/ConstructStorage.cs:250` | `48` | a shelf's slot: at most this many of a kind, however small the part (one byte each on the wire; 48 keeps a full bay's draw calls bounded | Kept |
| Wire width | `MaxStores` | `Constructs/ConstructStorage.cs:891` | `1024` | a wall of shelf bays is a store each; on the wire every handle is a u16 | Kept: store handles u16 (could rise to 65,535 with the arrays) |
| Wire width | `MaxTracks` | `Constructs/ConstructTracks.cs:169` | `64` | - | Kept: TrackCount is a byte on the wire |
| Wire width | `MostBodies` | `Constructs/ConstructWorld.cs:32` | `60000` | - | Kept: body ids are u16 |
| Wire width | `MaxCount` | `Constructs/OrdnanceDesign.cs:196` | `1000` | of a section in a row (two bytes on the wire; a safety for the numbers, far past any airframe | Kept: two bytes |
| Wire width | `PaintStroke.Key` / `ConstructRemover.Key` | `Constructs/PaintStroke.cs:17` | 31 bits (was 15) | a part or route index in a selection key (0x7FFFFFFF whole, 0x80000000 a route) | **Widened** (local hash keys, not on the wire) |
| Wire width | `MaxGames` | `Net/ServerBrowser.cs:771` | `512` | - | Kept |
| Wire width | `MaxQueriesPerSecond` | `Net/ServerBrowser.cs:774` | `4` | - | Kept |
| Wire width | `MaxParts` | `Net/Snapshot.cs:112` | `8` | - | Kept (player model parts, not constructs) |
| Wire width | `MaxMessageSize` | `Net/SteamTransport.cs:19` | `512 * 1024` | - | Kept |
| Cost budget | `MaxParts` | `Constructs/Construct.cs:231` | `16000` (was 8,000) | - | **Raised** (see "Parts a construct" above): part-sized spans in `Scratch`, keys and the engine union-find widened, settings count u32 and Steam fragments (protocol 190); a full rebuild at 16,000 parts was about the 8 ms budget; since the journal (see "Rebuilds") a typical edit is 0.15-1.4 ms, but a big split and big contact pairs keep it here |
| Cost budget | `MaxGuns` | `Constructs/Construct.cs:235` | `256` | guns a construct (8 until the capital parts, 64 until docs/perf/LIMITS.md): a turn through the gun step each a tick; per gun arrays grow... | **Raised 64 to 256**, logged when reached; per-gun arrays already grow (`ReserveGuns`), recoil folds per segment (`MaxRecoils` 64, unchanged memory) |
| Cost budget | `MaxSurfaces` | `Constructs/ConstructAero.cs:21` | `512` | - | Kept. With rotors, up to 544 items: the pose window's start was a byte (wrapped past 255): **fixed, u16, protocol 189** |
| Cost budget | `MaxVolumes` | `Constructs/ConstructCargo.cs:26` | `32` | cargo volumes a construct (more are refused when fitted; was 4: a carrier has a hangar a deck). Each frozen one is a broadphase look ever... | **Raised 4 to 32**: each frozen volume is one broadphase look every `ScanEvery` ticks |
| Cost budget | `MaxEngines` | `Constructs/ConstructEngine.cs:162` | `32` | modular engines a construct (was 8; EngineOf is a byte, the bank scratch is 256 slots on the stack); past this the pieces are logged and... | **Raised 8 to 32**, logged when reached; `EngineOf` is a byte, bank scratch 256 slots on the stack |
| Cost budget | `MaxBoxes` | `Constructs/ConstructParts.cs:796` | `256` | a part's boxes (a box index fits a byte) | **Raised** from 32 with merged meshes (docs/render/MERGED-MESHES.md): static parts' boxes are drawn per chunk, not per box |
| Cost budget | `MaxThrusters` | `Constructs/ConstructThrust.cs:32` | `512` | was 64; a raycast-free force each a tick. Past this a thruster is a plain part: logged, docs/perf/LIMITS.md | **Raised 64 to 512**; the array is no longer 64 a body (every loose block paid ~4 KB): it grows by doubling at a rebuild, and the old-level match is one walk, not n x old; logged when reached |
| Cost budget | `MaxSpots` | `App/Program.SandboxSculpt.cs:14` | `128` | - | Kept |
| Cost budget | `MaxMasters` | `App/Settings.cs:202` | `8` | - | Kept |
| Cost budget | `MaxVoicePlayers` | `App/Settings.cs:371` | `500` | - | Kept |
| Cost budget | `MaxChunk` | `Audio/MusicRenderer.cs:68` | `SawSynth.MaxBlock` | - | Kept |
| Cost budget | `MaxSaws` | `Audio/MusicSynth.cs:413` | `9` | - | Kept |
| Cost budget | `MaxBlock` | `Audio/MusicSynth.cs:416` | `1024` | - | Kept |
| Cost budget | `MaxConceal` | `Audio/Voice/JitterBuffer.cs:31` | `3` | - | Kept |
| Cost budget | `MaxSample` | `Audio/Voice/VoiceDsp.cs:194` | `32700` | - | Kept |
| Cost budget | `MaxHeardTalkers` | `Audio/Voice/VoiceRules.cs:68` | `6` | - | Kept |
| Cost budget | `MaxWheels` | `Constructs/Construct.cs:232` | `256` | - | Kept, now logged when reached (`[caps]`): a raycast each a tick; past it a wheel is a plain part |
| Cost budget | `MaxSprings` | `Constructs/Construct.cs:233` | `256` | - | Kept: a raycast each a tick |
| Cost budget | `MaxWires` | `Constructs/Construct.cs:234` | `1024` | - | Kept: logic wires, arrays grow with the construct |
| Cost budget | `MaxSeamLinks` | `Constructs/Construct.cs:249` | `256` | - | Kept: derived at a rebuild |
| Cost budget | `NetHistMax` | `Constructs/Construct.cs:441` | `8` | - | Kept |
| Cost budget | `MagMost` | `Constructs/ConstructAmmo.cs:56` | `2047` | - | Kept |
| Cost budget | `MaxFeedBurns` | `Constructs/ConstructAmmo.cs:360` | `16` | - | Kept |
| Cost budget | `MaxPoints` | `Constructs/ConstructAutoRoute.cs:43` | `1 << 20` | lattice points (2 MB, about 100 planes an axis | Kept |
| Cost budget | `MaxStates` | `Constructs/ConstructAutoRoute.cs:44` | `1 << 18` | points with a direction reached (a search that needs more gives up | Kept |
| Cost budget | `MaxExpansions` | `Constructs/ConstructAutoRoute.cs:45` | `200_000` | states searched in one stage | Kept |
| Cost budget | `ShaftMaxBends` | `Constructs/ConstructAutoRoute.cs:46` | `4` | a shaft that needs more bends wants a gearbox, not a worse route | Kept |
| Cost budget | `MaxReach` | `Constructs/ConstructAutoRoute.cs:48` | `24f` | the longest stretch it searches: a link has no limit, but further than this the player lays a node on the way and it carries on from there | Kept |
| Cost budget | `MaxKinds` | `Constructs/ConstructBeacons.cs:15` | `16` | - | Kept |
| Cost budget | `MaxNodes` | `Constructs/ConstructBeacons.cs:341` | `256` | beacons and build stations (a station is a beacon with an aid: BuildAids.cs); more are ignored, logged once | Kept |
| Cost budget | `MaxCellSpan` | `Constructs/ConstructBroadphase.cs:26` | `24` | a body (or a query) wider than this many cells goes in the always-walked list (or walks the list | Kept |
| Cost budget | `MaxWeldReach` | `Constructs/ConstructBuild.cs:402` | `4096f` | the farthest a blueprint's weld step may be from its anchor (a capital hull's pieces lie far apart | Kept |
| Cost budget | `MaxParts` | `Constructs/ConstructClip.cs:29` | `Construct.MaxParts` | - | Kept |
| Cost budget | `MaxConfigParts` | `Constructs/ConstructConfigure.cs:9` | `32` | - | Kept |
| Cost budget | `MaxConnectors` | `Constructs/ConstructConnectors.cs:75` | `1024` | - | Kept |
| Cost budget | `MaxDeckPushes` | `Constructs/ConstructDeck.cs:19` | `1024` | - | Kept |
| Cost budget | `MaxShaftFeeds` | `Constructs/ConstructDrillDrive.cs:28` | `16` | drills on one construct's drive line; more have no drive | Kept |
| Cost budget | `MaxScoopsPerStep` | `Constructs/ConstructDrillShaft.cs:25` | `1` | across the world, at 10 Hz: however many drills run, the ops stay slow | Kept |
| Cost budget | `MaxLog` | `Constructs/ConstructDrillShaft.cs:26` | `TerrainEdits.MaxOps / 2` | drills stop digging here, so blasts always have room in the log | Kept |
| Cost budget | `MaxGates` | `Constructs/ConstructDrive.cs:26` | `16` | - | Kept |
| Cost budget | `MaxGroups` | `Constructs/ConstructDrive.cs:27` | `12` | - | Kept |
| Pool size | Part-sized scratch (`Scratch`) | `Constructs/Scratch.cs` | grows by doubling, 2 MB a thread at 16,000 parts | zero-allocation scratch for floods, the drive line, logic, merges (was ~80 `stackalloc` spans) | **New**: grown at `ReserveParts` / `ReserveLinks` or when a frame needs more, never shrinks |
| Cost budget | `MaxRemoveTargets` | `Constructs/ConstructEditsRemover.cs:19` | `1024` | - | Kept |
| Cost budget | `MaxChunk` | `Constructs/ConstructEditsSelect.cs:28` | `256` | part indices a message | Kept |
| Cost budget | `MaxDrops` | `Constructs/ConstructEditsSelect.cs:33` | `16` | parts a delete hands back one at a time; more with nowhere to go stay together as a body | Kept |
| Cost budget | `Max` | `Constructs/ConstructEffectors.cs:50` | `96` | - | Kept |
| Cost budget | `MaxEmits` | `Constructs/ConstructEngine.cs:137` | `4` | - | Kept |
| Cost budget | Engine union-find | `Constructs/ConstructEngine.cs:207` | `int` (was `short`, 32,767) | part indices of a construct's engine pieces | **Widened** |
| Cost budget | `MaxRects` | `Constructs/ConstructExtrusion.cs:55` | `16` | solid boxes a section may mesh into (each is a draw and a collision box | Kept |
| Cost budget | `MaxWallCm` | `Constructs/ConstructExtrusion.cs:60` | `200` | and is at most this thick | Kept |
| Cost budget | `MaxStations` | `Constructs/ConstructExtrusion.cs:769` | `9` | - | Kept |
| Cost budget | `MaxStabs` | `Constructs/ConstructFlight.cs:78` | `8` | - | Kept |
| Cost budget | `MaxBeams` | `Constructs/ConstructFreeChain.cs:16` | `64` | - | Kept |
| Cost budget | `MaxCouplePairs` | `Constructs/ConstructFreeChain.cs:23` | `128` | joint pairs in a chain that pass motion to each other; a longer chain's far pairs go without | Kept |
| Cost budget | `MaxGap` | `Constructs/ConstructFreeform.cs:17` | `3f` | the closest points of the two parts' boxes at most this far apart (touching not needed: the bead bridges it | Kept |
| Cost budget | `MaxParts` | `Constructs/ConstructGravity.cs:20` | `64` | - | Kept |
| Cost budget | `HalfShaftMaxReach` | `Constructs/ConstructHalfShaft.cs:61` | `200f` | a part's own reach: ConstructParts.MaxPartReach | Kept |
| Cost budget | `MaxCount` | `Constructs/ConstructInventory.cs:14` | `9999` | of one kind | Kept |
| Cost budget | `MaxBins` | `Constructs/ConstructLegacyScrap.cs:13` | `3` | - | Kept |
| Cost budget | `MaxCells` | `Constructs/ConstructLift.cs:29` | `32` | - | Kept |
| Cost budget | `MaxBurners` | `Constructs/ConstructLift.cs:29` | `16` | - | Kept |
| Cost budget | `MaxCells` | `Constructs/ConstructLine.cs:24` | `400` | a stroke (a 20 by 20 field); more is two strokes | Kept |
| Cost budget | `MaxList` | `Constructs/ConstructMedia.cs:76` | `32` | - | Kept |
| Cost budget | `MaxMerges` | `Constructs/ConstructMerge.cs:185` | `24` | - | Kept |
| Cost budget | `MaxTurretMods` | `Constructs/ConstructModules.cs:24` | `32` | - | Kept |
| Cost budget | `MaxPoints` | `Constructs/ConstructNarrow.cs:31` | `4` | - | Kept |
| Cost budget | `SleepingPerPacket` | `Constructs/ConstructNet.cs:45` | `4` | - | Kept |
| Cost budget | `MaxPosePackets` | `Constructs/ConstructNet.cs:1385` | `6` | - | Kept |
| Cost budget | `StreamBytesPerTick` | `Constructs/ConstructNet.cs:1391` | `16 * 1024` | - | Kept |
| Cost budget | `MaxNoCollide` | `Constructs/ConstructNoCollide.cs:41` | `1024` | - | Kept |
| Cost budget | `MaxBodies` | `Constructs/ConstructNudge.cs:15` | `64` | bodies one nudge moves together | Kept |
| Cost budget | `BoxesPerTick` | `Constructs/ConstructNudge.cs:19` | `128` | boxes tested against the map each tick: a big body goes round them in turn | Kept |
| Cost budget | Rack releases a tick | `Constructs/ConstructOrdnance.cs:735` | 16 racks | racks that let go in one tick, world-wide (one bomb a body a tick) | Kept: a full rack still empties in 247 ticks |
| Cost budget | `MaxAttachSeconds` | `Constructs/ConstructParts.cs:801` | `60f` | - | Kept |
| Cost budget | `MaxPistonTravel` | `Constructs/ConstructParts.cs:801` | `64f` | - | Kept |
| Cost budget | `MaxHitchReach` | `Constructs/ConstructParts.cs:801` | `64f` | - | Kept |
| Cost budget | `MaxChain` | `Constructs/ConstructPhysics.cs:48` | `32768` | the joint chain entries of every contact on a segment, in all | Kept |
| Cost budget | `MaxCand` | `Constructs/ConstructPhysicsContacts.cs:18` | `4096` | - | Kept |
| Cost budget | `MaxPlants` | `Constructs/ConstructPlant.cs:14` | `32` | - | Kept |
| Cost budget | `Max` | `Constructs/ConstructRemover.cs:34` | `ConstructEdits.MaxRemoveTargets` | - | Kept |
| Cost budget | `MaxLinks` | `Constructs/ConstructReplace.cs:20` | `64` | - | Kept |
| Cost budget | `MaxAirTicks` | `Constructs/ConstructRide.cs:34` | `180` | - | Kept |
| Cost budget | `MaxRotors` | `Constructs/ConstructRotor.cs:31` | `32` | - | Kept (blade elements each a tick), now logged when reached |
| Cost budget | `RoundRaysPerTick` | `Constructs/ConstructRounds.cs:24` | `48` | rounds that look for what they hit a tick | Kept |
| Cost budget | `MaxNodesPerRoute` | `Constructs/ConstructRoutes.cs:80` | `MaxRouteNodes` | - | Kept |
| Cost budget | `MaxDataPorts` | `Constructs/ConstructRoutes.cs:324` | `32` | a part's output ports; an input port's 15 is the watch link's, WatchPort | Kept |
| Cost budget | `MaxLinkPorts` | `Constructs/ConstructRoutes.cs:429` | `32` | a Flight Stabilizer: 13 in, 16 out | Kept |
| Cost budget | `AdjustMaxCm` | `Constructs/ConstructRoutes.cs:1366` | `50` | the search and the wheel go at most this far out along the normal | Kept |
| Cost budget | `LoomMaxLinks` | `Constructs/ConstructRoutesLoom.cs:25` | `12` | links in a bundle | Kept |
| Cost budget | `LoomMaxGuide` | `Constructs/ConstructRoutesLoom.cs:26` | `32` | points of its run | Kept |
| Cost budget | `MaxScoops` | `Constructs/ConstructScoops.cs:62` | `32` | - | Kept |
| Cost budget | `MaxBacklog` | `Constructs/ConstructScoops.cs:129` | `4` | swept ground waits for this many bites; past that the ground holds as hard as for a full scoop | Kept |
| Cost budget | `ShellTestsPerTick` | `Constructs/ConstructShells.cs:241` | `22` | ...so that no tick tests many more than this | Kept: the bound that makes `MaxShells` cheap to raise |
| Cost budget | `ShellPartsPerTick` | `Constructs/ConstructShells.cs:242` | `400` | parts of a blast walked a tick | Kept |
| Cost budget | `ShellMaxBodies` | `Constructs/ConstructShells.cs:243` | `256` | bodies one blast reaches (the nearest the broadphase gives | Kept |
| Cost budget | `ShellMaxAge` | `Constructs/ConstructShells.cs:244` | `90 * World.TickRate` | ticks in the air before it is given up on | Kept |
| Cost budget | `MaxBlastsPerTick` | `Constructs/ConstructShells.cs:435` | `12` | each sends its own event and walks the broadphase once | Kept |
| Cost budget | `MegaPartsPerTick` | `Constructs/ConstructMegaBlast.cs:63` | `800` | parts a massive blast's front walks a tick, over all of them | **New** ("Massive blasts" above) |
| Cost budget | `MegaMaxBodies` | `Constructs/ConstructMegaBlast.cs:62` | `2048` | bodies one massive blast reaches, the nearest | **New** |
| Cost budget | `MaxMegas` | `Constructs/ConstructMegaBlast.cs:61` | `4` | massive blasts under way at once | **New** |
| Cost budget | `MegaDebrisMax` | `Constructs/ConstructMegaBlast.cs:65` | `96` | wrecks one massive blast cuts off; past it the pieces are gone | **New** |
| Cost budget | `MegaMaxRadius` | `Constructs/ConstructMegaBlast.cs:56` | `2000` | m: any blast's radius (a host may set less) | **New** |
| Cost budget | `MegaCraterMax` | `Constructs/ConstructMegaBlast.cs:59` | `120` | m: a crater's radius (columns of dug ground: memory) | **New** |
| Cost budget | `MegaDigColumnsPerTick` | `Game/GameSandboxMegaBlast.cs:14` | `3500` | columns a band of a big crater digs (one band a tick) | **New** |
| Wire width | `DigOp.Slices` | `Maps/TerrainEdits.cs` | `255` | bands of one crater (a byte) | **New**: a 120 m crater is 52 |
| Cost budget | `MaxMix` | `Constructs/ConstructStorage.cs:44` | `8` | a mixed input's materials (a Foundry's or an Assembler's: ConstructRefining.cs, ConstructAssembling.cs | Kept |
| Cost budget | `MaxStack` | `Constructs/ConstructStorage.cs:249` | `12` | - | Kept |
| Cost budget | `MaxBulk` | `Constructs/ConstructStorage.cs:251` | `60000` | - | Kept |
| Cost budget | `MaxSpillBundles` | `Constructs/ConstructStorage.cs:254` | `6` | - | Kept |
| Cost budget | `MaxBulk` | `Constructs/ConstructStoreVolume.cs:24` | `10_000_000` | units, whatever the box's size: every weight stays exact in a float and the inertia in range | Kept |
| Cost budget | `MaxSpill` | `Constructs/ConstructStoreVolume.cs:25` | `24` | bundles or loose parts a destroyed volume leaves; the rest is lost | Kept |
| Cost budget | `MaxJobs` | `Constructs/ConstructTerminal.cs:91` | `32` | pulls under way: in the world, at one terminal | Kept |
| Cost budget | `MaxJobsEach` | `Constructs/ConstructTerminal.cs:91` | `8` | pulls under way: in the world, at one terminal | Kept |
| Cost budget | `MaxTerminals` | `Constructs/ConstructTerminal.cs:282` | `64` | more are ignored: they read as having no stores | Kept, risky for big bases: more terminals read as having no stores (proposed: log or raise with the hop search's cost measured) |
| Cost budget | `MaxEdges` | `Constructs/ConstructTerminal.cs:283` | `2048` | chutes kept for working out hops; past this a store further off counts as one hop | Kept |
| Cost budget | `MaxBoxes` | `Constructs/ConstructTools.cs:74` | `24` | - | Kept |
| Cost budget | `MaxTrackWheels` | `Constructs/ConstructTracks.cs:170` | `48` | on one track | Kept |
| Cost budget | `MaxStations` | `Constructs/ConstructTracks.cs:214` | `12` | contact rays between a track's wheels | Kept |
| Cost budget | `MaxLoop` | `Constructs/ConstructTracks.cs:215` | `1600f` | metres round | Kept |
| Cost budget | `MaxPierce` | `Constructs/ConstructWeapons.cs:151` | `3` | parts a penetrating round goes through | Kept |
| Cost budget | `MaxYields` | `Constructs/ConstructWorldNotes.cs:60` | `128` | - | Kept |
| Cost budget | `MaxCorners` | `Constructs/ConvexHull.cs:12` | `32` | - | Kept |
| Cost budget | `MaxPanels` | `Constructs/DisplayPanel.cs:342` | `8` | - | Kept |
| Cost budget | `Max` | `Constructs/Factions.cs:19` | `32` | - | Kept |
| Cost budget | `MaxReach` | `Constructs/ModularRack.cs:53` | `190f` | m from its middle to its farthest edge (a part's boxes reach at most ConstructParts.MaxPartReach | Kept |
| Cost budget | `MaxSubs` | `Constructs/OrdnanceFlight.cs:385` | `48` | bomblets one charge lets go: more are carried by these | Kept |
| Cost budget | `MaxRegions` | `Constructs/PanelTouch.cs:26` | `8` | - | Kept |
| Cost budget | `MaxKinds` | `Constructs/ResourceField.cs:25` | `16` | - | Kept |
| Cost budget | `MaxBodies` | `Constructs/SelectController.cs:56` | `256` | constructs a selection spans | Kept |
| Cost budget | `MaxFreeSockets` | `Constructs/SmartRotation.cs:211` | `768` | the free sockets clearance looks at (more are ignored | Kept |
| Cost budget | `MaxBodies` | `Constructs/SocketWeld.cs:58` | `4` | the aimed body and the bodies nearest the crosshair | Kept |
| Cost budget | `MaxPlayers` | `Game/Game.cs:59` | `Map.HardMaxPlayers` | - | Kept |
| Cost budget | `MaxStarvedTicks` | `Game/Game.cs:62` | `4` | - | Kept |
| Cost budget | `MaxBlastRays` | `Game/GameDestruction.cs:22` | `512` | - | Kept |
| Cost budget | `MaxDebrisPerEvent` | `Game/GameDestruction.cs:25` | `150` | - | Kept |
| Cost budget | `MaxSlabsPerEvent` | `Game/GameDestruction.cs:25` | `48` | - | Kept |
| Cost budget | `MaxNavPatches` | `Game/GameDestructionNav.cs:18` | `16` | - | Kept |
| Cost budget | `MaxPerTick` | `Game/GameLogic.cs:49` | `256` | - | Kept |
| Cost budget | `MaxMoverTickSkew` | `Game/GameMovers.cs:21` | `30` | - | Kept |
| Cost budget | `MaxPropDetonationsPerTick` | `Game/GameProps.cs:121` | `4` | - | Kept |
| Cost budget | `MaxPropBlastsShown` | `Game/GameProps.cs:128` | `20` | - | Kept |
| Cost budget | `FellPerPacket` | `Game/GameResources.cs:13` | `256` | - | Kept |
| Cost budget | `BuildFreshMax` | `Game/GameSandboxBuildSounds.cs:21` | `8` | - | Kept |
| Cost budget | `MaxMail` | `Game/GameSandboxOffice.cs:26` | `16` | - | Kept |
| Cost budget | `SpawnZombieMax` | `Game/GameSandboxSpawns.cs:60` | `StressMax` | - | Kept |
| Cost budget | `SpawnIndividualsMax` | `Game/GameSandboxSpawns.cs:69` | `64` | - | Kept |
| Cost budget | `SpawnBotCapMax` | `Game/GameSandboxSpawns.cs:72` | `Map.HardMaxPlayers - 4` | - | Kept |
| Cost budget | `SpawnBotsPerTick` | `Game/GameSandboxSpawns.cs:97` | `1` | - | Kept |
| Cost budget | `SpawnZombiesPerTick` | `Game/GameSandboxSpawns.cs:97` | `8` | - | Kept |
| Cost budget | `SpawnCrowdPerTick` | `Game/GameSandboxSpawns.cs:97` | `10000` | - | Kept |
| Cost budget | `QueueMax` | `Game/GameSandboxVehicles.cs:32` | `8` | - | Kept |
| Cost budget | `SpaceShipCapacity` | `Game/GameSpace.cs:23` | `256` | - | Kept |
| Cost budget | `StoryThinksPerTick` | `Game/GameStory.cs:122` | `4` | - | Kept |
| Cost budget | `StoryBodiesMax` | `Game/GameStoryBodies.cs:24` | `8` | - | Kept |
| Cost budget | `DigOpsPerPacket` | `Game/GameTerrainDig.cs:23` | `64` | - | Kept |
| Cost budget | `BoxCapacity` | `Game/LobbyChat.cs:46` | `MaxLength + 1` | - | Kept |
| Cost budget | `MaxBoxes` | `Game/Materials/PenetrationTrace.cs:19` | `12` | - | Kept |
| Cost budget | `MaxFieldNodes` | `Game/NavZones.cs:108` | `12000` | - | Kept |
| Cost budget | `SepQueryMax` | `Game/PlayerSeparation.cs:37` | `32` | - | Kept |
| Cost budget | `SepMaxCarry` | `Game/PlayerSeparation.cs:73` | `12` | - | Kept |
| Cost budget | `MaxRewindTicks` | `Game/World.cs:126` | `24` | lag compensation rewinds at most 400 ms | Kept |
| Cost budget | `MaxHealth` | `Game/World.cs:269` | `120` | - | Kept |
| Cost budget | `MaxSubSteps` | `Physics/Ragdoll.cs:249` | `4` | - | Kept |
| Cost budget | `MaxAwakeSteps` | `Physics/Ragdoll.cs:263` | `60 * 9` | - | Kept |
| Cost budget | `MaxSpans` | `Planets/PlanetEdits.cs:31` | `TerrainEdits.MaxSpans` | - | Kept |
| Cost budget | `MaxLevels` | `Planets/PlanetRenderer.cs:43` | `16` | - | Kept |
| Cost budget | `Budget` | `Planets/Planetoid.cs:100` | `1024` | - | Kept |
| Cost budget | `Capacity` | `Render/BodyModel.cs:83` | `12` | - | Kept |
| Cost budget | `MaxGroupBoxes` | `Render/ConstructRenderSelect.cs:13` | `2048` | a ghost's boxes drawn a frame | Kept |
| Cost budget | `ShellShapedBudget` | `Render/ConstructRenderShells.cs:15` | `400` | a few dozen near ones in full: each contoured box is a draw call) // contoured draws a frame for shells in full (the rest are one box each | Kept |
| Cost budget | `ShellFlamesMax` | `Render/ConstructRenderShells.cs:17` | `64` | nozzle flames a frame (a salvo of rockets shares the particles with everything else | Kept |
| Cost budget | `MaxBirths` | `Render/Fx/Fx.cs:47` | `8192` | - | Kept |
| Cost budget | `MaxHaze` | `Render/Fx/PostFx.cs:19` | `32` | - | Kept |
| Cost budget | `MaxPerId` | `Render/GlDebug.cs:30` | `3` | - | Kept |
| Cost budget | `MaxLogged` | `Render/GlDebug.cs:30` | `200` | - | Kept |
| Cost budget | `MaxTraces` | `Render/GlDebug.cs:30` | `5` | - | Kept |
| Cost budget | `MaxReach` | `Render/LegAnimator.cs:65` | `(Thigh + Shin) * 0.98f` | - | Kept |
| Cost budget | `MaxDynamic` | `Render/LightGrid.cs:36` | `4` | - | Kept |
| Cost budget | `MaxVertices` | `Render/Mesh/GltfModelLoader.cs:22` | `1 << 21` | - | Kept |
| Cost budget | `MaxLods` | `Render/Mesh/MeshLibrary.cs:108` | `3` | - | Kept |
| Cost budget | `MaxBoxes` | `Render/Ragdolls.cs:66` | `80` | - | Kept |
| Cost budget | `Capacity` | `Render/Ragdolls.cs:131` | `24` | - | Kept |
| Cost budget | `MaxAwake` | `Render/Ragdolls.cs:131` | `16` | - | Kept |
| Cost budget | `MaxZombieBodies` | `Render/Ragdolls.cs:131` | `10` | - | Kept |
| Cost budget | `MaxFace` | `Render/Sky/SkyImages.cs:155` | `2048` | - | Kept |
| Cost budget | `MaxPanoramaWidth` | `Render/Sky/SkyImages.cs:155` | `8192` | - | Kept |
| Cost budget | `MaxImageTextures` | `Render/Sky/SkyRenderer.cs:319` | `3` | - | Kept |
| Cost budget | `MaxLevels` | `Render/StreamedTerrainRenderer.cs:40` | `16` | - | Kept |
| Cost budget | `Capacity` | `UI/NameTags.cs:198` | `64` | - | Kept |
| Cost budget | `RowMax` | `UI/RadialMenu.cs:97` | `7` | - | Kept |
| Cost budget | `MaxPaste` | `UI/SafeClipboard.cs:27` | `4096` | - | Kept |
| Cost budget | `RecentMax` | `UI/SandboxPaintWheel.cs:28` | `8` | - | Kept |
| Cost budget | `StoryGraphMax` | `UI/UiStory.cs:27` | `400` | - | Kept |
| Data sanity | `MaxVolumes` | `Constructs/ConstructFluid.cs:55` | `32` | Tank Volumes a construct (a sanity check when fitted, was 4: each is sealed at a rebuild, nothing is sized by it | **Raised 4 to 32**: only checked when fitted; each is sealed at a rebuild |
| Data sanity | `MaxPerConstruct` | `Constructs/ConstructFreeform.cs:18` | `192` | freeform welds a construct (they share Construct.MaxJoints with the joints: a segment is a byte, so 255 in all; was 64, this leaves 63 fo... | **Raised 64 to 192**: shares the 255 segments with moving joints, which already bounded the cost |
| Data sanity | `MaxRuntimeName` | `Constructs/ConstructParts.cs:803` | `128` | a part made at run time ("stage": the Ordnance Rack's shapes): its name is its key in saves and blueprints, never a fixed-width field (a... | **New, 128**: a run-time (`"stage"`) part's name, i.e. the Ordnance Rack keys. Names are length-prefixed in saves (i32) and never sent in fixed fields; packet text is 255 bytes. Extrusion and ordnance keys never went through this check |
| Data sanity | `MaxPerConstruct` | `Constructs/ConstructStoreVolume.cs:22` | `32` | a sanity check when fitted, was 4: nothing is sized by it | **Raised 4 to 32**: only checked when fitted, nothing is sized by it |
| Data sanity | `MaxRecipes` | `Constructs/ConstructAssemblingTable.cs:48` | `2000` | - | Kept |
| Data sanity | `MaxInputs` | `Constructs/ConstructAssemblingTable.cs:48` | `6` | - | Kept |
| Data sanity | `MaxUnits` | `Constructs/ConstructAssemblingTable.cs:48` | `2000` | - | Kept |
| Data sanity | `MaxPartCount` | `Constructs/ConstructAssemblingTable.cs:48` | `20` | - | Kept |
| Data sanity | `MaxOutput` | `Constructs/ConstructAssemblingTable.cs:48` | `12` | - | Kept |
| Data sanity | `MaxUpload` | `Constructs/ConstructEditsSelect.cs:29` | `1 << 21` | compressed bytes of a blueprint | Kept: 2 MB compressed blueprint |
| Data sanity | `MaxBlueprintText` | `Constructs/ConstructEditsSelect.cs:30` | `1 << 24` | its text | Kept: 16 MB blueprint text |
| Data sanity | `MaxLengthCm` | `Constructs/ConstructExtrusion.cs:57` | `19200` | 192 m: no piece is longer (the Capital Extruder's; ConstructParts.MaxPartReach is 200 m | Kept |
| Data sanity | `MaxRows` | `Constructs/ConstructExtrusionTable.cs:28` | `32` | - | Kept |
| Data sanity | `MaxSide` | `Constructs/ConstructGravity.cs:23` | `24f` | - | Kept |
| Data sanity | `MaxTier` | `Constructs/ConstructParts.cs:797` | `5` | a machine's highest tier | Kept |
| Data sanity | `MaxPartReach` | `Constructs/ConstructParts.cs:799` | `200f` | how far a box goes from a part's origin (a capital hull section, a 128 m keel from its end cell | Kept: positions are 3 bytes a coordinate on the 1 cm grid; a box's reach is a sanity check |
| Data sanity | `MaxName` | `Constructs/ConstructParts.cs:802` | `32` | a part file's name (a sanity check: the inventory's tiles and lists show it whole | New name for the old literal 32: a part file's name |
| Data sanity | `MaxPerPlayer` | `Constructs/ConstructPlace.cs:36` | `12` | constructs a player may own by default (wrecks and loose parts don't count; the host's ConstructWorld.PerPlayerCap | Kept: the host's per-player default |
| Data sanity | `MaxRows` | `Constructs/ConstructPressTable.cs:59` | `2000` | - | Kept |
| Data sanity | `MaxBays` | `Constructs/ConstructPressTable.cs:59` | `3` | - | Kept |
| Data sanity | `MaxRecipes` | `Constructs/ConstructRefiningTable.cs:39` | `64` | - | Kept |
| Data sanity | `MaxUnits` | `Constructs/ConstructRefiningTable.cs:39` | `100` | - | Kept |
| Data sanity | `MaxMixInputs` | `Constructs/ConstructRefiningTable.cs:39` | `4` | - | Kept |
| Data sanity | `MaxSide` | `Constructs/ConstructStoreVolume.cs:23` | `40f` | a box's side (metres; the part file's settings stop here too | Kept |
| Data sanity | `DefaultBodyCap` | `Constructs/ConstructWorld.cs:30` | `10000` | - | Kept: host setting |
| Data sanity | `DefaultPartCap` | `Constructs/ConstructWorld.cs:31` | `250000` | - | Kept: host setting |
| Data sanity | `MostParts` | `Constructs/ConstructWorld.cs:33` | `2000000` | - | Kept: the host's safety cap |
| Data sanity | `MaxLines` | `Constructs/DisplayPanel.cs:70` | `8` | - | Kept |
| Data sanity | `MaxChars` | `Constructs/DisplayPanel.cs:70` | `32` | - | Kept |
| Data sanity | `NameMax` | `Constructs/Factions.cs:22` | `24` | - | Kept |
| Data sanity | `MaxChars` | `Constructs/LogicText.cs:14` | `60` | - | Kept |
| Data sanity | `MaxSide` | `Constructs/ModularRack.cs:54` | `16` | rows, columns, layers or segments | Kept: 16 x 16 is 256 > 247, so the station cap decides |
| Data sanity | `MaxName` | `Constructs/PlayerBlueprints.cs:14` | `32` | - | Kept |
| Data sanity | `MaxPrestige` | `Cosmetics/Cosmetics.cs:45` | `5` | - | Kept |
| Data sanity | `MaxVertices` | `Decals/DecalBaker.cs:61` | `400_000` | - | Kept |
| Data sanity | `MaxSize` | `Decals/DecalFile.cs:21` | `16f` | - | Kept |
| Data sanity | `MaxImageSize` | `Decals/DecalFile.cs:24` | `512` | - | Kept |
| Data sanity | `MaxGlobal` | `Decals/DecalPacks.cs:72` | `256` | - | Kept |
| Data sanity | `MaxPerPack` | `Decals/DecalPacks.cs:78` | `4096` | - | Kept |
| Data sanity | `MaxPackDecalsPerMap` | `Decals/DecalPacks.cs:84` | `1024` | - | Kept |
| Data sanity | `MaxSize` | `Decals/ImpactDecals.cs:106` | `1.2f` | - | Kept |
| Data sanity | `MaxDecals` | `Decals/MapDecals.cs:34` | `6000` | - | Kept |
| Data sanity | `MaxLength` | `Game/LobbyChat.cs:43` | `120` | - | Kept |
| Data sanity | `MaxCells` | `Maps/LightBaker.cs:174` | `1 << 20` | - | Kept |
| Data sanity | `MaxVertices` | `Maps/MapBoundary.cs:133` | `4096` | - | Kept |
| Data sanity | `MaxGravityZones` | `Maps/MapFile.Gravity.cs:9` | `32` | - | Kept |
| Data sanity | `MaxGravitySources` | `Maps/MapFile.Gravity.cs:143` | `16` | - | Kept |
| Data sanity | `MaxMascons` | `Maps/MapFile.Gravity.cs:143` | `4096` | - | Kept |
| Data sanity | `MaxTetras` | `Maps/MapFile.Gravity.cs:143` | `4096` | - | Kept |
| Data sanity | `MaxTetraVertices` | `Maps/MapFile.Gravity.cs:143` | `8192` | - | Kept |
| Data sanity | `MaxMovers` | `Maps/MapFile.Movers.cs:14` | `32` | - | Kept |
| Data sanity | `MaxMoverBoxes` | `Maps/MapFile.Movers.cs:14` | `16` | - | Kept |
| Data sanity | `MaxEntities` | `Maps/MapLogic.cs:171` | `256` | - | Kept |
| Data sanity | `MaxWires` | `Maps/MapLogic.cs:171` | `1024` | - | Kept |
| Data sanity | `MaxZones` | `Maps/MapLogic.cs:171` | `64` | - | Kept |
| Data sanity | `MaxDoors` | `Maps/MapLogic.cs:171` | `64` | - | Kept |
| Data sanity | `MaxMeshPlacements` | `Maps/MapMeshes.cs:32` | `20000` | - | Kept |
| Data sanity | `HardMaxPlayers` | `Maps/MapPlayers.cs:19` | `32` | - | Kept |
| Data sanity | `MaxOps` | `Maps/TerrainEdits.cs:83` | `65536` | - | Kept |
| Data sanity | `MaxSpans` | `Maps/TerrainEdits.cs:86` | `16` | - | Kept |
| Data sanity | `MaxLevels` | `Maps/TerrainEditsSmooth.cs:29` | `4 * MaxSpans * 2 + 2` | - | Kept |
| Data sanity | `Budget` | `Maps/TerrainStream.cs:23` | `1536` | - | Kept |
| Data sanity | `MaxWidth` | `Media/MediaBlock.cs:13` | `1280` | - | Kept |
| Data sanity | `MaxHeight` | `Media/MediaBlock.cs:13` | `720` | - | Kept |
| Data sanity | `MaxBehindFrames` | `Media/MediaBlock.cs:18` | `Audio.SampleRate / 3` | - | Kept |
| Data sanity | `MaxRestarts` | `Media/MediaBrowser.cs:29` | `2` | - | Kept |
| Data sanity | `MaxSpeakers` | `Media/MediaDirector.cs:17` | `8` | - | Kept |
| Data sanity | `MaxChars` | `Media/MediaEntry.cs:17` | `1024` | - | Kept |
| Data sanity | `MaxPieces` | `Props/FlagPool.cs:81` | `80` | - | Kept |
| Data sanity | `Capacity` | `Props/Gibs.cs:71` | `320` | - | Kept |
| Data sanity | `MaxPerBurst` | `Props/Gibs.cs:74` | `40` | - | Kept |
| Data sanity | `MaxChain` | `Props/PropAnim.cs:87` | `4` | - | Kept |
| Data sanity | `Capacity` | `Props/PropDebris.cs:16` | `384` | - | Kept |
| Data sanity | `MaxParts` | `Props/PropFile.cs:17` | `128` | - | Kept |
| Data sanity | `MaxColliders` | `Props/PropFile.cs:18` | `8` | - | Kept |
| Data sanity | `MaxSize` | `Props/PropFile.cs:21` | `24f` | - | Kept |
| Data sanity | `MaxRows` | `Render/Surfaces.cs:37` | `256` | - | Kept |
| Data sanity | `MaxSystems` | `Space/Galaxy.cs:16` | `10` | - | Kept: the design's ten systems |
| Data sanity | `MaxLy` | `Space/Galaxy.cs:19` | `1000` | - | Kept |
| Data sanity | `MaxWarp` | `Space/SpacePos.cs:98` | `100_000` | - | Kept |
| Data sanity | `MaxGrid` | `Space/SpaceShips.cs:108` | `1 << 10` | - | Kept |
| Data sanity | `MaxBodies` | `Space/SpaceSkyView.cs:45` | `8` | - | Kept |
| Data sanity | `MaxShips` | `Space/SpaceSkyView.cs:45` | `16` | - | Kept |
| Data sanity | `MaxStars` | `Space/SpaceSkyView.cs:59` | `Galaxy.MaxSystems - 1` | - | Kept |
| Data sanity | `MaxLine` | `Story/ItDesk.cs:13` | `96` | - | Kept |
| Data sanity | `MaxLines` | `Story/ItDesk.cs:13` | `6` | - | Kept |
| Data sanity | `MaxScams` | `Story/ScamDesk.cs:14` | `200` | - | Kept |
| Data sanity | `MaxAdverts` | `Story/ScamDesk.cs:14` | `64` | - | Kept |
| Data sanity | `MaxLabel` | `Story/ScamDesk.cs:17` | `28` | - | Kept |
| Data sanity | `MaxPopup` | `Story/ScamDesk.cs:17` | `16` | - | Kept |
| Data sanity | `MaxKnown` | `Story/StoryAgents.cs:79` | `32` | - | Kept |
| Data sanity | `MaxSteps` | `Story/StoryAgents.cs:79` | `16` | - | Kept |
| Data sanity | `MaxNodes` | `Story/StoryAgents.cs:79` | `40` | - | Kept |
| Data sanity | `MaxRests` | `Story/StoryAgents.cs:79` | `8` | - | Kept |
| Data sanity | `PickMax` | `Story/StoryAgents.cs:133` | `12` | - | Kept |
| Data sanity | `MaxAired` | `Story/StoryAgents.cs:170` | `64` | - | Kept |
| Data sanity | `Cap` | `Story/StoryChronicle.cs:40` | `20_000` | - | Kept |
| Data sanity | `MaxDirections` | `Story/StoryDrama.cs:78` | `8` | people nudged for one story at most | Kept |
| Data sanity | `MaxCandidates` | `Story/StoryDrama.cs:78` | `1024` | people nudged for one story at most | Kept |
| Data sanity | `MaxExpansions` | `Story/StoryHtn.cs:12` | `400` | - | Kept |
| Data sanity | `MaxRules` | `Story/StoryLines.cs:35` | `2000` | a line fits the chat (120) with a name in front | Kept |
| Data sanity | `MaxLines` | `Story/StoryLines.cs:35` | `24` | a line fits the chat (120) with a name in front | Kept |
| Data sanity | `MaxLength` | `Story/StoryLines.cs:35` | `110` | a line fits the chat (120) with a name in front | Kept |
| Data sanity | `MaxParams` | `Story/StoryPeopleRules.cs:138` | `4` | - | Kept |
| Data sanity | `MaxSteps` | `Story/StoryPeopleRules.cs:138` | `8` | - | Kept |
| Data sanity | `MaxStates` | `Story/StoryPlaces.cs:28` | `64` | - | Kept |
| Data sanity | `MaxRecent` | `Story/StoryPlaces.cs:28` | `4` | - | Kept |
| Data sanity | `MaxPlays` | `Story/StoryPlays.cs:42` | `200` | - | Kept |
| Data sanity | `MaxBeats` | `Story/StoryPlays.cs:42` | `16` | - | Kept |
| Data sanity | `MaxRunning` | `Story/StoryPlays.cs:248` | `4` | - | Kept |
| Data sanity | `MaxLeads` | `Story/StoryPlays.cs:248` | `16` | - | Kept |
| Data sanity | `MaxVars` | `Story/StoryRules.cs:100` | `8` | - | Kept |
| Data sanity | `MaxStages` | `Story/StoryRules.cs:100` | `8` | - | Kept |
| Data sanity | `MaxText` | `Story/StoryTalk.cs:58` | `118` | a filled line is cut to this (the chat holds 120 | Kept |
| Data sanity | `MaxEntities` | `Story/StoryWorld.cs:43` | `100_000` | - | Kept |
| Data sanity | `MaxSize` | `Updates/UpdateManifest.cs:40` | `1536L * 1024 * 1024` | - | Kept |
| Data sanity | `MaxPerSlot` | `Weapons/Attachments.cs:115` | `(1 << SlotBits) - 1` | - | Kept |
| Data sanity | `MaxOverrides` | `Weapons/WeaponTweaks.cs:124` | `1200` | - | Kept |
| Data sanity | `MaxWeapons` | `Weapons/Weapons.cs:400` | `KillStreaks.ExtraKillIdBase` | weapon ids are kill-feed ids: the later streaks use 214-229 | Kept |
| Data sanity | `MaxStorms` | `Weather/WeatherSim.cs:56` | `8` | - | Kept |
| Data sanity | `MaxNameLength` | `Worlds/WorldStore.cs:23` | `32` | - | Kept |
| Pool size | `MaxPerConstruct` | `Constructs/ConstructCargo.cs:27` | `128` | bodies a construct carries at once (the rest stay loose; was 24: a carrier's air wing). Its cargo list grows with what it carries (Constr... | **Raised 24 to 128**; `Construct.Cargo` was 24 entries on every body, now empty and grown by doubling when a carrier holds more than ever before (an event, never shrinks) |
| Pool size | `MaxShells` | `Constructs/ConstructShells.cs:239` | `1024` | in the air at once: a gun holds its fire while it is full (designed weapons' bomblets share it: OrdnanceFlight.cs). Was 256: a full Ordna... | **Raised 256 to 1024** (a full Ordnance Rack's salvo is 247); logged when full. Tick cost stays bounded by `ShellTestsPerTick`; a sky over 264 shells is looked at less often, so a hit can go off up to `MaxShells / 22` ticks late (0.8 s at 1,024) |
| Pool size | `MaxVoices` | `Audio/Audio.Mixer.cs:260` | `112` | - | Kept |
| Pool size | `MaxBursts` | `Audio/BuildSounds.cs:61` | `6` | - | Kept |
| Pool size | `MaxNotes` | `Audio/MusicSynth.cs:413` | `4` | - | Kept |
| Pool size | `MaxRecoils` | `Constructs/Construct.cs:310` | `64` | segments with a shot's recoil in one tick; past this the last entry takes the rest (AddRecoil | New: the recoil list's size (was `MaxGuns`), one entry a segment, the last takes the rest |
| Pool size | `MaxMuzzles` | `Constructs/Construct.cs:415` | `16` | muzzles a gun with a splitter fires from (one a barrel | Kept: per-gun stride of `GunMuzzles` |
| Pool size | `MaxGhosts` | `Constructs/ConstructBuild.cs:28` | `96` | - | Kept |
| Pool size | `MaxDefs` | `Constructs/ConstructExtrusion.cs:63` | `48000` | distinct pieces in one run of the game (a length step of one line is one): past this no new shape can be made | Kept: run-time pieces in one run |
| Pool size | `MaxFlexSections` | `Constructs/ConstructFlexChute.cs:15` | `32` | - | Kept |
| Pool size | `MaxRailSections` | `Constructs/ConstructLinear.cs:22` | `64` | sections a stack; a longer line is two rails | Kept |
| Pool size | `MaxHeld` | `Constructs/ConstructOrdnance.cs:34` | `ModularRack.MaxStations` | bombs a rack lets go of in turn (an Ordnance Rack's most stations | Follows `MaxStations` (stack spans of 247 ushorts) |
| Pool size | `MaxContacts` | `Constructs/ConstructPhysics.cs:35` | `16384` | a few hundred awake bodies at once: board item cl01 | Kept: 16,384 a tick, thinned per normal |
| Pool size | Contact scratch | `Constructs/ConstructPhysicsContacts.cs:25` | 65,536 leaves, 131,072 pairs | a capital deck on a floor | Kept |
| Pool size | `MaxSenders` | `Constructs/ConstructRadio.cs:12` | `256` | - | Kept |
| Pool size | `MaxRounds` | `Constructs/ConstructRounds.cs:23` | `256` | in the air at once | Kept: past it a round ends at its reach as before; rays bounded by `RoundRaysPerTick` |
| Pool size | `MaxRouteNodes` | `Constructs/ConstructRoutes.cs:79` | `8192` | a construct's storage for link nodes, 65 km of link at a full span each; a link's length has no limit of its own | Kept: grows with the construct |
| Pool size | `MaxSlots` | `Constructs/ConstructStorage.cs:42` | `12` | - | Kept |
| Pool size | `MaxNotes` | `Constructs/ConstructWorldNotes.cs:44` | `1024` | - | Kept |
| Pool size | `MaxDrawnTrees` | `Constructs/ResourceField.cs:145` | `30000` | - | Kept |
| Pool size | `MaxDrawnOutcrops` | `Constructs/ResourceOutcrops.cs:13` | `16000` | - | Kept |
| Pool size | `MaxLearned` | `Constructs/SmartRotation.cs:112` | `256` | the oldest correction goes first beyond this | Kept |
| Pool size | `MaxMarkers` | `Constructs/SocketWeld.cs:57` | `64` | markers shown at once (the nearest the crosshair | Kept |
| Pool size | `MaxWorldNodes` | `Constructs/WorldLinks.cs:54` | `Construct.MaxRouteNodes` | the world's storage for world link nodes; a link's length has no limit of its own | Kept: the world's node store (`MaxRouteNodes`) |
| Pool size | `MaxLayers` | `Decals/DecalAtlas.cs:27` | `2048` | - | Kept |
| Pool size | `MaxLayers` | `Decals/DecalFile.cs:18` | `48` | - | Kept |
| Pool size | `MaxSections` | `Game/AllocProfile.cs:18` | `128` | - | Kept |
| Pool size | `MaxMarks` | `Game/BodyBlood.cs:37` | `8` | - | Kept |
| Pool size | `MaxPendingInputs` | `Game/Game.cs:134` | `240` | - | Kept |
| Pool size | `MaxQueued` | `Game/GameLogic.cs:49` | `1024` | - | Kept |
| Pool size | `MaxPendingKnocks` | `Game/Knockback.cs:210` | `32` | - | Kept |
| Pool size | `MaxQueuedInputs` | `Game/World.cs:811` | `60` | - | Kept |
| Pool size | `MaxSnaps` | `Game/World.cs:851` | `64` | - | Kept |
| Pool size | `MaxInstances` | `Maps/Foliage.cs:184` | `400_000` | - | Kept |
| Pool size | `MaxLights` | `Maps/MapLights.cs:117` | `2048` | - | Kept |
| Pool size | `MaxPages` | `Media/MediaDirector.cs:14` | `4` | - | Kept |
| Pool size | `MaxDepth` | `Perf/Perf.cs:75` | `32` | - | Kept |
| Pool size | `MaxPages` | `Planets/PlanetRenderer.cs:41` | `512` | - | Kept |
| Pool size | `MaxInstances` | `Planets/PlanetRenderer.cs:44` | `4096` | - | Kept |
| Pool size | `MaxPending` | `Planets/PlanetRenderer.cs:47` | `64` | - | Kept |
| Pool size | `MaxLayers` | `Props/FlagPool.cs:83` | `6` | - | Kept |
| Pool size | `MaxSplatsPerBurst` | `Props/Gibs.cs:80` | `5` | - | Kept |
| Pool size | `MaxLights` | `Props/PropFile.cs:328` | `16` | - | Kept |
| Pool size | `MaxAidGhosts` | `Render/ConstructRender.cs:1060` | `240` | - | Kept |
| Pool size | `MaxSideOutlines` | `Render/ConstructRender.cs:1367` | `48` | - | Kept |
| Pool size | `MaxGlass` | `Render/ConstructRender.cs:1483` | `512` | - | Kept |
| Pool size | `MaxOutlines` | `Render/ConstructRenderSelect.cs:12` | `2048` | selected parts outlined a frame | Kept |
| Pool size | `MaxLinksDrawn` | `Render/ConstructRenderTracks.cs:12` | `900` | on one track: past it the links drawn get longer | Kept |
| Pool size | `MaxLayers` | `Render/Mesh/MeshTextures.cs:15` | `64` | - | Kept |
| Pool size | `MaxSlots` | `Render/PanelRender.cs:23` | `128` | panels shown at once: past this the rest wait | Kept |
| Pool size | `MaxInstances` | `Render/Ragdolls.cs:135` | `Capacity * RagdollBody.MaxBoxes` | - | Kept |
| Pool size | `MaxMarkers` | `Render/SnapRender.cs:23` | `160` | - | Kept |
| Pool size | `MaxPages` | `Render/StreamedTerrainRenderer.cs:38` | `512` | - | Kept |
| Pool size | `MaxInstances` | `Render/StreamedTerrainRenderer.cs:41` | `4096` | - | Kept |
| Pool size | `MaxPending` | `Render/StreamedTerrainRenderer.cs:44` | `64` | - | Kept |
| Pool size | `MaxDepth` | `Space/StarSystem.cs:129` | `8` | - | Kept |
| Pool size | `MaxPages` | `Story/ItDesk.cs:13` | `200` | - | Kept |
| Pool size | `MaxDepth` | `Story/StoryHtn.cs:12` | `12` | - | Kept |
| Pool size | `MaxHits` | `UI/BloodOverlay.cs:25` | `8` | - | Kept |
| Pool size | `MaxInstances` | `UI/BloodOverlay.cs:28` | `4 + BaseCount + MaxHits` | - | Kept |
| Pool size | `MaxEntries` | `UI/TextCache.cs:17` | `2048` | - | Kept |
