# Network and scale

Covers the network protocol for constructs, thousands of constructs in a world. Part of the construct guide: index in ../CONSTRUCTS.md.

## Network (protocol 82; 84 for free placement; 86 for the drivetrain's rpm; 87 for glass shattering; 88 for the physics gun)

- `Msg.CBody` (reliable): a construct's whole structure (parts on their 1 cm grid with their logic setting and signal,
  links, joints with their type, lock and wired flags, wires, pose: about 20 bytes plus 14 per part and 2 per wire),
  sent when it changes (an edit, a break, a split, a wire, a setting) and to each joiner; also part health batches, a
  construct gone, and "clear all" at a new match.
- Protocol 81 (salvage): `CBody` also carries the construct's **owner** (a player id, 255: nobody's) and a **damaged**
  flag; `CBodyKind.Inventory` sends one player their own inventory, whole, when it changes and when they join (a flags
  byte (infinite parts), then each part kind held as id and count, then each material held; about 4 bytes plus
  4 a kind; protocol 106 dropped the scrap). Nobody else's inventory is ever sent.
- Protocol 82 (construct weapons): `CBody` ends with the parts holding rounds or on fire (part, rounds, a burning /
  reloading flag: 4 bytes each); `CBodyKind.Ammo` batches the changes (a box's rounds, a gun's load, a part catching fire
  or going out: 6 bytes each); `Msg.CFire` (reliable, only on ticks a gun fires) carries what the guns did that tick: each
  round (construct id, gun part, where it ended, what it met: 17 bytes) and each blast or cook-off (where, how big: 14
  bytes). Clients draw the tracers from their own copy of the gun's muzzle.
- Protocol 83 (links and power): `CBody` also carries the construct's charge (kJ), a power-flags byte (electrical, power
  on, engine running, starter failed) and its gear; `CBodyKind.Links` sends every coupling (construct and part at each
  end, the chain's length) and every wire between constructs (construct and part at each end), whole, whenever one is
  made, broken or renumbered, and to each joiner; `CPose` adds 5 bytes a body (charge share, power flags, gear, fuel);
  `CAct.Wire` carries the construct the wire goes into (0: the same one).
- `Msg.CPose` (unreliable sequenced, 20 Hz): the bodies that move (32 bytes plus 4 per joint, the angle and speed, plus
  a byte per signal part when it has logic), any whose signals changed (a lamp switched on in an anchored base), plus
  four sleeping ones in turn so a lost packet never leaves one wrong for long. Clients blend between poses and fit their
  wheels to the ground themselves.
- Protocol 101 (body sync, board item pd01): when more bodies move than one pose packet holds, they take turns (the
  next packet starts at the first body left out, `ConstructWorld.PoseCursor`), with room kept for the sleeping ones'
  turn; a body that goes still sends its final pose three more times. A `CBody` Full carries the server tick of its pose,
  so a Full sent again after a loss doesn't put a newer pose back. Every 2 s the server sends the body digest
  (`CBodyKind.Digest`, reliable and in order with the structure: each body's id, `ConstructNet.StructureHash`, sleep
  state and a sleeping one's pose); a client removes the bodies it doesn't list and asks for the ones it holds wrong
  (`CAct.Resync`), which come back whole. "Sandbox body sync" tests it with a real host and client, lag and loss.
- `Msg.CAct` (reliable): a client's request (tool, attach, place, remove, cancel, use, stand, rack, drop, pick up; wire,
  unwire, a setting, a seated player's fire/aim/reload keys; protocol 80: `SeatLook`, a seated player's seat-relative
  look, sent unreliably); the server re-checks everything and answers a refusal with its reason. Protocol 79: `Rack` carries a pick byte (0: the rack's next kind, at the rack; n: part id n - 1, from the
  inventory, anywhere) and `Blueprint` (a blueprint index) builds one in front of the player.
- The Sandbox mode-state tail (~10 Hz, version 3): every player's tool (the linking tool and the physics gun too), held
  part, seat and edit in progress, and (version 3) the construct the physics gun holds and the grab point.
- Protocol 90 (routed links): `CBody` carries the construct's routed links in place of its wires (per link a type and
  style byte, its ends and ports, its junction, its nodes on the 1 cm grid: 9 bytes plus 8 a node and a health byte a
  segment); the client rebuilds the logic's wires from the data links. `CActKind.Route` (a whole link as the client
  routed it) and `CActKind.RouteEdit` (delete, move a node, repair). Part ids are two bytes everywhere (`CBody` parts,
  stores, the inventory, the mode-state tail's held part, `CAct.Rack`'s pick).
- Protocol 88 (the physics gun): `CBody`'s flags carry frozen (32) and a byte after the owner says who froze it;
  `CActKind` Grab, GrabSteer, GrabRelease, GrabFreeze, UnfreezeAll ([The physics gun](overview-and-playing.md#the-physics-gun)).
- Protocol 121 (capital parts): every part index is two bytes, positions in a construct and link nodes three a coordinate
  (`PacketWriter.Cm`, +-83 km on the 1 cm grid), health three; `CBody` part rows are 20 bytes, links 6. A pose carries at
  most 96 joints and 256 signals; a body with more sends a window that turns (first and count on the wire). A body of 128 parts
  or more sends `CBodyKind.Patch` for an edit (`ConstructPatch.cs`): both sides keep its last Full (`PatchBase`) and an edit goes as
  copies from it plus new bytes, at most 10 Hz; a joiner and a resync get the base and the next patch. RakNet splits
  whatever exceeds a packet.
- Protocol 89 (the configure tool): `CBody` ends with the part settings that are off their defaults (a u16 count, then
  part, slot and an f32 value: 6 bytes each; a choice a part already had, like a gear, stays its setting byte);
  `CActKind.Configure` (u16 construct, u8 part count, the parts, u8 setting count, slot and f32 value each) sets them on
  one or more parts of one kind; the tool id `ConstructTool.Configure` (4).

### Joint collision

Board item dev-sandbox-joint-collision-jc01 (a hinged beam sank into the terrain: only the body's own hull touched
anything, the segments beyond a joint were posed from the joint value and went through whatever was there). Now:

- Every part beyond a joint is in that joint's **rigid group** of the construct's collider
  ([Compound collision](#compound-collision)), so it meets the map, the ground and other constructs as boxes, exactly as the
  body's own parts do, and moves with the body *and* with every joint up its segment's chain. Raycast wheels on a steering
  pivot still roll on their rays.
- **The solve** works on generalised velocities: the body's six and one per joint. A contact on a segment carries, for
  each joint up its chain, the velocity a unit of joint speed gives its point; its impulse moves the body (if it is free)
  and turns or slides each of those joints by its inverse inertia. A contact between two constructs does that on both
  sides. The **motors and limits** are constraints in the same sequential-impulse loop (a motor's impulse clamped to its
  strength a tick, a limit one-sided and speculative), solved before the contacts in each pass so non-penetration has
  the last word. So a passive hinge rests on the ground; a motor pushing into a wall stalls at its strength; a strong
  motor driving an arm into the ground lifts its free construct (the arm becomes a leg).
  A locked hinge has no give: its contacts move the body instead.
- **No tunnelling**: every contact is speculative (approach is allowed up to the gap, not past it), its reach covers the
  whole tick's travel using the speed a motor can reach this tick, so a stalled arm keeps seeing what it pushes against, and
  a fast thin piece is tried along its way. A door swung at 25 rad/s (25 m/s at its edge) stops at a 5 cm wall. Joints are
  capped at 25 rad/s (or m/s).
- **Why reduced coordinates still**: the joint stays one number, so the pieces can never drift apart, stretch or explode,
  and it stays the cheapest thing to send (`CPose` already carries each joint's angle and speed; no wire change,
  protocol 79). Contacts drive the joint value directly, which is what constraint-linked bodies would have to work hard
  to match. The approximation: each joint's inertia is its carried mass about its axis (as the motors already used) and
  the body's and joints' masses are treated as independent in the solve, which only shares out how a push moves things,
  never whether a contact holds.
- Not yet: a segment against its **own** construct (joint limits keep the built ones apart), players pushing a moving
  segment back (they are pushed out of it, as before), the body's mass properties following its joints.

### No-collide pairs

The **No-collide tool** (on the default wheel and in Tab > Tools; `ConstructTool.NoCollide`, no ring): click a piece, then another, and that pair passes through each other. Click the pair
again to let them collide. Clicks pick as Freeform attach does; RMB or a click on nothing drops the first pick. While the tool is
out every pair is drawn as a dashed line through everything, with a tick at each end, and the second pick's look line says the
state (`Frame Block and Frame Beam: no-collide - LMB: let them collide`).

- **What a click means.** A click takes the whole rigid piece under the crosshair: a plain construct, or one moving segment of a
  mechanism. With Shift held it takes that one part. The two ends are chosen apart, so a part can pass through a whole piece.
- **Two bodies or one.** Parts of one construct never collide, so a pair within one construct does nothing until the two come
  apart (a cut seam, a split), and then they still don't meet. Two parts of one rigid piece are refused.
- **The table.** `ConstructWorld.NoCollides` (ConstructNoCollide.cs), at most 1,024 pairs, each end a part tag, like a
  connector's. A hash on the two bodies is remade only when the list changes; a body with a pair carries the world's stamp, so
  a pair of bodies with none costs two integer compares in `PairContacts` and nothing allocates. Both-whole pairs skip at the
  group, others at the leaf. `NewOverlap` (the physics gun's held sweep) and `GroundRay` (a wheel or foot on a deck) ask too.
- **What ends a pair.** The part it names going. That holds for a whole-piece end as well: the part clicked is the anchor, so a
  piece that loses that part loses the pair.
- **Taking it off while they overlap.** Nothing pushes them apart gently: the contacts take over from wherever they are, and one piece
  fully inside another can stay there.
- **Carried by** saves (section `NoCollide`, older saves have none), joiners and changes (`CBodyKind.NoCollide`, the whole list, at most
  10 times a second), a copy, and blueprints (`"noCollide": [{ "a": 0, "b": 2, "wholeA": true, "wholeB": true }]`, a step is a part
  index). A blueprint holds one construct, so a pair between two constructs isn't in one.
- The solve is the server's; a client holds the list for the marker and the look line.

### Joints on the wire

A pose carries every joint's angle with the body's server tick. A client keeps them with the pose (`Construct.NetHistAngle`, as `NetHistPos`) and draws each joint at the same tick as the body, through the same cubic (`ConstructNet.SampleJoints`; a free joint goes the short way round). A joint set as its pose arrived was a round trip's delay ahead of a body drawn on the ticks, so a moving chain came apart from its body and from itself. Without a render clock (a pose with no tick), the old path stands.

### Compound collision

A construct's collider is its real shape. `Source/Constructs/ConstructCollider.cs` (the boxes and trees),
`ConstructNarrow.cs` (the pair tests), `ConstructPhysicsContacts.cs` (who meets what, the points, warm starting),
`ConvexHull.cs` (shaped parts).

- **The collider.** Every solid box of every part is a leaf (`SolidCount`/`SolidBox`, so a merged receiver is its one big
  box). Leaves are grouped by **rigid group**: the body and its freeform attachments are group 0, each real joint's parts another
  (`Construct.MoveSeg`). Each group has a bounding volume tree in its own frame, so a joint moving costs nothing: the frame
  moves and the tree with it. A freeform-attached box is a leaf turned in its group. The tree is built on first use after the
  parts change (`Construct.Collider`, in the physics step: a client never builds one) and reuses its arrays. From 2,048
  leaves (`ConstructCollider.FastMin`) a group's tree is built from Morton order: the leaf centres on a lattice of 1,024 cubes
  along the group's longest side, radix sorted, cut where the codes first differ (a cell boundary), the bounds made bottom up (O(n); sorting the leaves at every
  level cost 13 to 18 ms at 16,000 parts, and a tree-ordered list made the median split's selection slow). Smaller trees are
  built as before. A big body's plain edits renumber the tree instead of building it ([Rebuilds](overview-and-playing.md#rebuilds)).
- **Against other constructs** (`PairContacts`). The two trees are walked together to the leaf pairs within this tick's
  travel plus 2 cm, then the separating-axis test: the three face axes of each box and the nine edge crosses
  (`ConstructNarrow.BoxBox`). A face gives its incident face clipped to the reference face, up to four points; an edge pair
  one. Each point has its gap: above 0 it is speculative (it may close that far and no further), below 0 it is pushed out.
- **Against the map** (`WorldCompound`). `IWorldQuery.Gather` gives the boxes, oriented boxes, mesh triangles, boundary
  pieces and posed movers near the group; each goes through the same tree and test. Trees are `ConstructTrunks.cs`, box by
  box, as before.
- **Against the ground.** `Gather` gives the triangles of the cells under the group, through `TerrainData`, so dug, filled
  and streamed ground and caves all come the same way (a group over more than 900 cells asks leaf by leaf, so a big
  construct on fine ground never walks all of it; streamed tiles come on demand). A triangle is a sheet, not a solid, so its
  contact is always along its own normal (`ConstructNarrow.PieceTriangle`): the piece's lowest face, clipped to the prism
  over the triangle. A plate lying across a ridge rests on the ridge line from both sides, wherever its corners are, and no
  neighbouring triangle's edge can catch a sliding box. A two-sided sheet (a dug wall) pushes to the side the piece is on.
- **Seams.** Two floor slabs side by side: the second slab's side face stands at floor height in front of a sliding box,
  and separating axes alone would stop the box on it. So a side face met within 3 cm of a level top the piece rests on or
  skims (never more than half the thinner piece's half height, so a thin plate edge-on still meets a wall) gives way to that
  top (`ConstructNarrow.Ledge`). A crate rides over a 3 cm kerb; a deck of many plates has no joins.
- **Fast thin pieces.** The reach covers the travel, so a piece meets what it will reach. A thin piece whose travel this
  tick is more than half its thickness, and that met nothing where it stands, is also tried along its way (up to 6 steps,
  `ConstructPhysics.Swept`): what it finds is moved back to now with the gap it closes on the way.
- **Fewer points.** Each rigid group's points against one thing are thinned per normal to the deepest and the eight furthest
  out round it (`ReducePoints`): a capital hull flat on a floor has the contacts of a crate. Past 4,096 candidate points a walk takes every Nth piece, so they stay spread over the body.
- **The solver.** Each contact starts from last tick's normal impulse (a table per world keyed by the pair and the spot on
  the first body, on a 5 cm grid, `WarmEntry`), so a stack carries its weight from the first pass; not from a blow (a
  contact closing fast), which would throw the body back, and not its friction (carried over, a stack's small sways fed
  themselves until it fell). Passes run forwards and backwards in turn (one order turns a face a little every pass).
  An overlap never adds speed: a plain body is moved out by a split impulse (`PushRows`, its own motion for the tick, 3
  passes), and a point inside the 2 mm slop is held where it is (the old 1 cm let each block of a stack rock 2 degrees).
  Only a contact on a part beyond a joint is pushed out by speed (a joint has no second set of numbers).
- **Sleep.** Bodies pressing on each other count their still ticks together (`SleepTogether`), so a stack sleeps as one and
  never drops off from the bottom, block by block.
- **Wheels** stand on rays and are a cylinder to everything else ([Wheels](drivetrain-and-engines.md#wheels-the-rim-and-the-cylinder)): one moving
  leaf per wheel, and a turning wheel's tread carries what it touches. A track unit stays rays and boxes that only meet
  something loose. A wheel with no suspension is a plain solid part.
- **Couplings.** A coupling's hitch parts don't collide with each other's construct, and points within 0.7 m of a hitch are
  dropped, as before.
- **Self-collision.** None: parts of one construct never collide with each other (joint limits keep the built ones apart).
- **No-collide pairs.** The no-collide tool ([No-collide pairs](#no-collide-pairs), below) takes a pair of pieces or parts out of these contacts.
- **The physics gun.** `HeldSweep` tries each axis of the step and refuses only a move that makes a NEW overlap of a piece
  with a piece (`NewOverlap`; the 2026-10-31 rule), so a held piece slides along a wall and into a slot 3 cm wider than it,
  and skips what it is coupled to or carried by.

#### What it costs

"Compound collision" (`CollisionCost`) measures these and fails on any allocation (milliseconds a tick, the user's PC runner's
Linux VM, wall time; the same scenes on main before this work, with the old hull points):

| Scene | Before | Now | Contacts before | Now |
| --- | --- | --- | --- | --- |
| 300 blocks falling (no contacts) | 0.19 | 0.17 | 0 | 0 |
| A pile of 200 settling | 2.97 | 2.98 | 3,499 | 1,792 |
| One 96-part hull sliding on a floor | 0.006 | 0.015 | 40 | 4 |
| Eight 96-part hulls stacked, sliding on each other | 2.08 | 0.87 | 502 | 100 |
| One 96-part hull and 40 blocks on half-metre ground | 0.09 | 0.22 | 162 | 255 |
| 300 blocks landing on half-metre ground | | 0.73 | | 1,702 |
| "Construct scale": 2,000 bodies, 195 awake | 2.22 | 1.41 | | |
| "Construct scale": 5,000 constructs, 300 awake | 2.21 | 2.51 | | |

A hull on ground is the dearer case: the ground's triangles under it are looked at each tick, where hull points asked one ray
each. Everything else is cheaper or level, because the trees only ever reach the pieces near something and thinning keeps
the solve short. These rows were measured at 96 parts a construct. At 8,000 (container, full suite): one hull sliding 0.49 ms,
one hull and 40 blocks on half-metre ground 2.5 ms, a buggy on a capital deck 0.53 ms (was 23 to 39 ms with sampled hull
points). Eight 8,000-part hulls stacked cost 84 ms a tick, over the 8 ms budget: each pair walks every piece of the faces that
touch before the candidates are thinned. Since `MaxParts` went to 16,000 these scenes build hulls of the new cap (container, the
section alone, 2026-11-07): one hull sliding 0.83 ms (0.65 at 8,000 the same day), one hull and 40 blocks on half-metre
ground 3.7 ms, eight 16,000-part hulls stacked 126 ms (59 ms at 8,000): the stacked case doubles with the parts.

#### Shaped parts

A part's contoured shapes are detail boxes over hidden solid boxes. Per shape (`ConstructHulls`; the shapes' table is in
`Source/Constructs/ConvexHull.cs`):

| Shape | Collider |
| --- | --- |
| Wedge | a convex hull from its mesh: a real slope (a block driven at it rides up), not the steps |
| Cylinder, tube | a convex hull from its mesh (a tube's outside; its bore stays solid) |
| Arc | its hidden boxes: not convex, and the steps follow the curve |
| Rounded, bevel | its solid box: nearly the same shape |
| A wheel on a suspension | a sixteen-sided prism at the hub ([Wheels](drivetrain-and-engines.md#wheels-the-rim-and-the-cylinder)) |
| More than 32 corners (the Small Nuke's smooth cylinders) | the hidden boxes |

A hull replaces the hidden solid boxes inside its detail box for **collision and ray hits** (bullets, the look ray:
`ConstructWorld.Raycast` tests the hull, so a bullet over a slope passes). **Mass, inertia, building overlap, ghosts, the outline and
player walking keep the boxes**: a player walks up a wedge's hidden steps (each within a step of the slope, the self-test
holds them to it), which feels like a slope. A hull beats a box in the separating-axis test by testing every face and
edge direction (`ConstructNarrow.HullHull`), and is only reached when something comes close. A part file can say
`"collider": "boxes"` to keep its hidden boxes whatever its shapes (or `"shape"`, the default).

### The frozen outline

A frozen construct is drawn with an icy outline round each part (no more than 24 parts; bigger ones show the frost tint
alone): one box round the whole read as its collider, and it never was.

## Scale: thousands of constructs

Board item dev-sandbox-construct-limits-cl01. The world's fixed caps (64 bodies, 1,200 parts, 128 joints, 512 wires,
chosen to keep the tick in budget when every loop walked every body) are gone. A world holds thousands of constructs,
wrecks and loose parts; what is at rest costs next to nothing; the per-construct caps are in
[Size classes](parts-and-data.md#size-classes-the-capital-parts).

- **Sleep.** A resting body sleeps (40 still ticks, as before). In a big world (from `ConstructWorld.LinearBelow`, 48
  bodies) the physics steps only a list made each tick (`ConstructPhysics.SimList`): the awake bodies, the held and
  changed ones, and whatever they may touch or are coupled to. A sleeping body is not integrated, not in the contact
  solve and not sent; a sleeping one with motors is only asked whether a motor wants to move (`WakeCheck`). Contact with
  a moving body, a tool, an edit, a blast, a logic or power change wakes it, as before.
- **Broadphase** (`Source/Constructs/ConstructBroadphase.cs`). A hashed uniform grid (8 m cells, 4,096 buckets) over the
  bodies' spheres in x and z, rebuilt lazily (the first query after a step, two passes, no allocation; bodies made or
  grown since go in a short list every query walks). Ray tests, player blocking, blasts, the physics' body pairs, cargo
  volume scans and bot obstacles ask it (`Near`, `NearRay`) for the bodies near them; small worlds walk the list exactly
  as before. A seeded check compares 200 rays through the grid with a walk over every body.
- **Network** (no wire change: protocol 100). In a big world each client gets its own pose packets
  (`ConstructNet.WritePosesFor`, Game `SandboxPoses`): the bodies that move or changed (and for 8 pose ticks after),
  every pose tick within 80 m of the client's player, every 2nd to 200 m, 4th to 400 m, 8th beyond, staggered by id; a
  few sleeping ones refreshed in turn; up to 6 packets a client a pose tick, the start turning with the tick so nobody is
  starved. A joiner gets the constructs **streamed**, 16 KB a tick (about 1 MB/s; `GameSandboxStream.cs`), and the stores,
  links and its inventory after the last one; small worlds keep the broadcast and the single burst.
- **Rendering** (`Source/Render/ConstructRenderCull.cs`). A construct out of view or past 700 m isn't drawn; one past
  140 m with 5 parts or more is drawn as one box over its bounds in its first part's colour. Near ones draw part by part.
  A client's blend skips sleeping bodies already where their last pose put them.
- **The host's safety cap.** Not a design cap: "Max constructs" (the lobby room's Match tab in Sandbox; default 10,000
  bodies, and 25 parts a body on average: 250,000 parts) only guards against runaway spam, and "Constructs per player"
  (default 12, "No limit" for a private game; lower it on a public server). Refusals say which limit and what it is.
  Joints and routed links in all scale with the body cap (4 and 8 a body). `Settings.SandboxMaxBodies` and
  `SandboxMaxPerPlayer` (`settings.txt`).
- **Save and load.** There is no world save file yet (blueprints hold one construct); the world's state
  travels whole only to a joiner, and "Construct scale" checks that every copy of 2,000 matches the host's.

Measured by the "Construct scale" self-test (seeded, a few ticks each; GitHub Actions runners, 2026-10-31; the Windows
column is the busiest tick's CPU time, which the test holds under the 8 ms budget). The numbers also come out as a notice
on a branch's CI run.

| World | Awake | Simulated a tick | Tick (wall, Linux CI) | Tick (CPU, Windows CI) | Allocated |
| --- | --- | --- | --- | --- | --- |
| 2,000 bodies, ~4,300 parts | ~195 | ~195 | 1.5 to 1.8 ms average (worst 1.9 to 2.5) | 2.0 to 2.2 ms worst (1.4 to 1.8 ms average, wall) | 0 |
| 5,000 constructs, 100,000 parts, + 300 blocks | ~300 | ~300 | 1.7 to 1.8 ms average (worst 1.9 to 2.1) | 1.7 to 2.2 ms worst (1.5 to 1.9 ms average, wall) | 0 |
| 2,000 bodies at rest | 0 | 0 | 0.14 ms | | 0 |

A joiner had all 2,000 bodies after 0.3 s over the loopback; queries looked at about 25 bodies (a ray down), 85 (a
40 m ray along a row) and 70 (a player's blocking) of 2,000.

### Choices made (the developer may change them)

- The grid's cell (8 m), the bands (80 / 200 / 400 m), the stream rate (16 KB a tick) and the LOD and cull distances
  (140 m, 700 m) are constants, not host settings.
- Worlds under 48 bodies keep every old code path exactly (the walk over the list, the pose broadcast, the join burst),
  so the existing tests and small games behave as they did.
- No protocol bump: the poses and the structure keep their format; a client takes several pose packets a tick.
- Far constructs are one box in the colour of their first part; instanced per-construct batches are left to the render
  preparation work (dev-perf-threading-mt01).
- Every construct's arrays are sized to it (the capital parts), so a world of small bodies costs less than the 44 KB a body it did.
