# Smart logistics network: design (2026-11-12)

For the Opus agent that builds it. The user's decisions are in `smart-logistics-brief.md` (D1 to D5 below) and are final.
Everything marked *proposed* is this design's choice; section 14 lists the real choices left for the user.
Read from main at protocol 260 (261 is reserved for Press phase 2). Nothing was built or run for this file.

| D | Decision (brief) |
| --- | --- |
| D1 | Smart junctions on today's conveyors: a Router reads where a piece is wanted. No carriers, carts or drones |
| D2 | Set-up is automatic, with optional overrides (store filter, line priority, feed first) |
| D3 | Orders pull everything, down to ore; storage keeps stock targets. No push from the raw end |
| D4 | A Router shows its choice and the piece's destination; a network board draws the map |
| D5 | No destination, or destination full: storage on the network with room |

## 1. What exists today

| Thing | How it works now | Where |
| --- | --- | --- |
| A rider | `CRider`: def, `Route` (a byte: the conveyor on its construct), `At` (front, metres), health, paint, `ViaPlus1` (the link it left through a junction). Riders belong to one construct (`Construct.Conveyed`, at most 1,024), grouped by link, front first | `ConstructConveyor.cs` |
| Moving | A 10 Hz step walks only bodies with riders: 0.5 m/s, 10 cm gap. Only the lead piece of a link looks further: at a junction it joins `waiting`, else `Deliver` | `StepBody` |
| Delivery | `Deliver`: a Piece Rack (`RackPut`; a terminal's tray also calls `ConstructCrafting.Arrived`), a Panel Former, a Press bay (by the port's name). A refusal leaves the piece waiting at the link's end; the line backs up to the machine | `Deliver`, `Jam` |
| A Splitter | `Pass`: the junction's outgoing conveyors, round robin from the input link's `Cursor`, past one that is cut, too narrow, too short or has no room at its start (`Takes`). No knowledge of the piece's purpose | `Pass`, `Takes` |
| A Join | The same `Pass` with one output; inputs are served in turn (`Turn`, the outputs' `Timer`) | `Turn` |
| Conveyor ends | Out: Extruder, Wheel Former, Panel Former, Press, Assembler, Piece Rack (not a terminal's tray), junctions. In: Piece Rack, terminal and station trays, Press bays, Panel Former, junctions. An Assembler has no conveyor input: its parts and bulk come by chute (`IntoAssembler`) | `ConstructPorts.cs` |
| Conveyors and constructs | A conveyor is a `CRoute` of one construct. `WorldLinks` carry chutes, power, pipes and data between constructs, never conveyors | `WorldLinks.cs` |
| A chute | Not a rider: every `ChuteSeconds` a fed chute makes one `ChuteMove` from its source store to its end: a load of bulk, else the first item the end accepts. The end decides (a refiner's `Feed`, `WantsPart`, a press's `Chute`, a terminal's `ChuteInto`). A chute junction shares by `Share` | `ConstructRoutes.StepChute`, `ChuteMove` |
| An order finding its machine | A Line Console's `BillSend` deals each line round robin to its wired machines that accept it (`WorkOrders.Refusal`). A terminal asks "the first linked machine that accepts it" (`WorkOrders.Queue`) | `WorkOrders.cs`, `ConstructCrafting.StepTerminal` |
| Where a made piece goes | Onto the machine's Piece out conveyor and wherever that leads; the terminal's order then claims it in the rack it landed in and fetches it again | `PutMade`, `HandOff` |
| "Network" to the terminal | `ConstructCrafting.NetOf`: on the terminal's own construct, everything joined by intact conveyors and chutes either way round (through junctions, machines, stores), plus the machines of each Line Console wired into Orders and what those are joined to. Cached per terminal under a hash of the body's links (`KeyOf`); 64 machines, 64 stores, 16 refiners | `ConstructCrafting.cs` |
| Getting stock to the tray | `RouteTo`: a store's conveyor to the tray, straight or through Joins only ("a splitter deals in turn, so it is no route"), or one chute hop; else "no route: collect it there" | `RouteTo` |
| The station | A Crafting Terminal by data. Its job places craft orders (`ConstructCrafting.Place`) for kinds whose step can go on; the terminal's code does the rest | `ConstructStation.cs` |
| Reservations | `ConstructReserve`: store, kind, count, owner (a store handle) and its order id; one table a world (1,024). Available is held less reserved. Never saved | `ConstructReserve.cs` |
| Chains | The terminal's list knows one level ("chain": a missing input is made by a linked refiner or Assembler) but orders nothing for it: the player queues the input | `Missing`, `RefinerMakes` |
| Line state, monitors | Six states from the store on either peer; a display wired to a Data out is a monitor of that source; `PanelCard` is 16 lines of text, a bar, and `PanelDraw` shapes (`Flight` is a drawn schematic) | `ProductionLines*.cs`, `PanelRender.cs` |
| Power | `ConstructRecycler.Powered`: battery, mains, the part's own Powered bit | `ConstructPower.cs` |

What is missing: a piece has no purpose once it rides; a junction cannot honour one; a made piece takes two trips; nothing orders
inputs; storage never asks for anything.

## 2. The network

### 2.1 Membership and reach (*proposed*; question Q1)

**A logistics network is one connected group of parts on one construct, joined by intact conveyors and chutes, either way
round.** It is the flood `ConstructCrafting.Build` already does, taken out of the terminal and made the body's own:
`ConstructLogistics.NetsOf(c)`.

| Rule | Why |
| --- | --- |
| One construct | Conveyors cannot leave a construct, riders are a construct's, and a base is one construct on its Foundations (the showcase is). Costs nothing new on the wire |
| Conveyors and chutes join; data, power and pipes do not | Goods links only. A wire is an instruction, not a path |
| A cut link does not join | `FedEnd`, as today. Mending it joins the two halves again at the next rebuild |
| A Line Console wired into a terminal's Orders port still lends its machines to that terminal | Kept as it is: those machines may be *asked*, but what they make can only travel on their own network. With no goods path the order says "no route from X: collect it there", as today |
| A network is *smart* where a Router is downstream | A source whose conveyor reaches a Router asks the network before it lets a piece go. A line with no Router behaves exactly as today |
| Linked constructs | Not in phases 1 to 5. Phase 6 (optional): a chute world link makes the far side's stores and machines *remote members*: stock there can be claimed and bulk comes through the chute; pieces still cannot cross |

### 2.2 The cached map

`LogiMap`, one per construct that has a conveyor, built on either peer (the board needs it on a client), held on the
`Construct` and pooled (`Source/Constructs/ConstructLogistics.cs`, new).

| Part of the map | Contents | Bound |
| --- | --- | --- |
| Nodes | Every part that ends a conveyor or chute: `Part`, `Kind` (Source, Dest, Store, Router, Splitter, Join, Machine), `Net` (its group, a byte), its place in the body frame (for the board) | 255 |
| Conveyor edges | One per conveyor: `From` node, `To` node, `Route`, length in metres (walked once here, not per step), width in cm, intact | 254 (`MaxRoutes`) |
| Chute edges | One per chute: from, to. Used for membership, the board and "can bulk reach it"; never for next hops | 254 |
| `NetOfPart` | part to group (255: none), so "same network?" is one array read | parts |
| Destinations | Nodes a piece can be sent to: racks, trays, press bays (one per bay port), the Panel Former, from phase 2 an Assembler's Piece in | 128 |
| Next hop | `Hop[router, dest]`: the Router's output conveyor that leads to `dest` by the shortest intact way (255: none), and `Narrow[router, dest]`: the narrowest conveyor on that way in 5 cm steps | 32 Routers x 128 = 4 KB + 4 KB a construct |
| Storage order | Per group: its stores that take overflow, by priority then by index | 64 |

**When it is rebuilt.** `Construct.LogiStamp` is bumped where the links' caches are remade (`ConstructRoutes.Rebuild`: build,
delete, cut, mend, a part removed), by the conveyor size edit (`RouteEditOp.Size`) and by a change of a logistics setting.
The map carries the stamp it was built from; a reader with another stamp rebuilds. (The builder checks that a cut segment
does reach `Rebuild`; if one path doesn't, fall back to the terminal's `KeyOf` hash, widened by `ConvW`.) Never per tick,
never per piece.

**Cost and slicing.**

| Step | When | Work | Target |
| --- | --- | --- | --- |
| Graph (nodes, edges, groups, storage order) | first read after the stamp moved | one pass over the routes, one flood | 0.3 ms at 254 links; typical 0.02 ms |
| One destination's column of `Hop` | the first time a piece is sent to that destination after a rebuild | one shortest-path pass back from the destination over conveyor edges (at most 254), fixed arrays on the stack | 0.02 ms |
| Slice | at most 4 new columns a conveyor step (10 Hz); a piece whose column isn't ready waits one step at its Router | | under 0.1 ms a step |

So a rebuild never costs more than the graph pass in one tick, and columns arrive as pieces need them. Most destinations are
never used and never computed. A client builds the graph only (no columns).

The cost of a way is its length in metres plus 2 m for each junction crossed. A plain Splitter's outputs are **not** edges
for next hops (it deals in turn, so a way through it is not a way); a Join's single output is.

### 2.3 Several networks

| Case | Behaviour |
| --- | --- |
| Two groups on one construct | Each has its own destinations, storage and orders. A terminal sees its own group (plus wired consoles' machines, 2.1). The board shows the group of the Router it is wired to |
| Two terminals or stations on one group | They share it. `ConstructReserve` and the wants table (4.1) keep them from counting the same part; section 7.3 says who is served first |
| A group split by a cut | Two groups from the next rebuild. A piece riding toward a destination now on the other side is re-assigned at its next Router (4.4) |
| Constructs welded or split | `Merge`, `MoveTo` and `Remap` already carry riders; a rider's destination is a store handle (4.1), which survives renumbering. The stamp moves, the maps rebuild |
| A vehicle with a line on board | Its own network; nothing special |

## 3. The Router

### 3.1 The part

Made by the part kit: `conveyor_junction_parts` gains `Router()` beside `Junction()` (`Source/PartKit/Generators/ConveyorJunctionParts.cs`),
output `Constructs/Parts/ConveyorJunctions/ConveyorRouter.json` and `LargeConveyorRouter.json`. Data flag on the part:
`"router": true` (`ConstructPartDef.IsRouter`; it is also `IsConveyorJunction`).

| Point | Router | Large Router |
| --- | --- | --- |
| Footprint, piece | the Splitter's (pieces up to 1 x 1 m) | the Large Splitter's (2 x 2 m) |
| Conveyor ports | **Piece in** at -z; **Piece out**, **2**, **3** at +z, +x, -x: the Splitter's bytes and places, so the replace tool swaps a Splitter for a Router with its links kept | same |
| Data ports | **Data out** (a display wired to it is the network board, section 9), **Alarm** (1 while a piece is stuck on its network), **Moving** (pieces riding on its network) | same |
| Power | 20 W; without power it deals in turn like a Splitter and its lamps are out (so a flat battery slows a factory, never stops it) | 40 W |
| Screen | a 4-row panel on the signal post, panel source `router` (3.2) | 6 rows |

**Look** (look.md "Machine look"; the builder reads it before the generator):

| Point of the machine look | The Router |
| --- | --- |
| 1 Shape says function | The Splitter's low roller table plus what no junction has: a **signal post** at the in-left corner, 1.6 m high (clear of a 1 m piece; 2.6 m on the Large one), with a head of three arrow lamps (ahead, left, right) over a small screen. From 50 m: a table with a railway signal |
| 2 Work is visible | A **diverter**: a steel fence on the deck that swings to the chosen way (`"work": "turn"`, new: yaw about its own pivot to 0, +90 or -90 degrees over 0.3 s). The deck arrow and the head lamp of the chosen way light orange; the others are dark. All stop when it has no power |
| 3 Flow reads one way | White band and arrow at the in side, orange at each out, hazard paint at each port, as the Splitter |
| 4 Three tones | Base `#22262b`, steel deck and fence, accent on the fence's hazard edge and the post's cap; body (takes paint) logistics green `#3f8f5a` (*proposed*: no machine family uses green; check `--look-report`'s nearest pairs) |
| 5 Tier | one tier now |
| 6 One control face | the post's screen, middle at 1.5 m, facing the in side's left aisle; no buttons (it is set with the configure tool) |
| 7 Finishes | `steel` on the deck and fence, `hazard paint` on posts and port strips, `tread plate` nowhere (not walked on) |

Z-fighting: every lamp, arrow and mark sits 3 to 4 mm proud, as the junction generator does now.

### 3.2 Showing its choice (D4), with nothing new on the wire for it

| What the player sees | Where it comes from |
| --- | --- |
| The diverter turned and the way's lamp and deck arrow lit | Boxes marked `"way": 1`, `2`, `3` (new box key, like `"status"`). Drawn lit while the Router's *last choice* is that output. A client knows the choice from the riders it already hears: a rider with `ViaPlus1` set whose old link ends at this Router is on the chosen output. The choice stays shown until the next piece |
| Lamp colour | orange: to an order; white: to storage; red blinking 3 Hz: the lead piece at its input is stuck (4.4) |
| Look line (aimed at the Router) | `Router: last piece Gear Set -> Crafting Terminal (order 12, 3 of 4)`; waiting: `Router: holding L-angle 20x20x75 - no way to the Press: its conveyor is cut`; idle: `Router: 2 ways out, 14 destinations, storage 57%` |
| Look line (aimed at a riding piece) | today's line plus `-> Building Station (Buggy)` or `-> storage: Long Piece Rack` |
| Its screen (`router`) | row 1 the piece crossing or waiting and its destination; row 2 why (order, overflow, stuck reason); rows 3-4 the next two pieces coming on its input |

The destination text needs the rider's destination on clients: section 10, protocol 262.

### 3.3 Settings (configure tool, group Logistics)

| Setting | Values | Default | Meaning |
| --- | --- | --- | --- |
| `routerSpill` | Auto, Ahead, Left, Right | Auto | Where a piece with no order goes: Auto follows the map to storage; a fixed way pins it (the player's "everything unclaimed goes down this lane") |
| `routerHold` | Off, On | Off | On: a piece with no way to its destination waits here rather than going to overflow (for a line that must never mix) |
| Text size | 50-150 % | 100 | as every screen |

### 3.4 Plain junctions on a smart network

| Junction | Behaviour |
| --- | --- |
| Conveyor Splitter | Unchanged: deals in turn, whatever the piece wants. It is the tool for sharing work between twin lines on purpose. Next hops never go through it; a piece it sends the wrong way is put right at the next Router (every Router can reach every destination it has a way to). Its look line gains "deals in turn (a Router sends by order)" when a Router is on its network |
| Conveyor Join | Unchanged and fully usable on a way (one output) |
| Replace | The replace tool offers Splitter to Router and back, links kept (same port bytes). No automatic conversion of old builds |

## 4. Destinations

### 4.1 What a rider carries

| New `CRider` field | Size | Meaning |
| --- | --- | --- |
| `Dest` | u16 | Server: the destination's store handle (0: none yet). Handles survive part renumbering, splits and merges. A client holds the destination's part index as heard |
| `Tag` | u16 | The order it serves at that destination (a `CraftOrder.Id`, a chain entry's id, a stock target's slot; 0: storage) |
| `Flags` | u8 | bit 0 storage, bit 1 overflow (it had a destination and lost it), bit 2 stuck; bits 3-7 a reason code (table 4.4) |
| `Hops` | u8 | Junctions crossed since it was assigned (the loop guard) |
| `Wait` | u8 | Conveyor steps its Router has failed to pass it (saturates at 255) |

Seven bytes a rider, 7 KB a full construct. `Dest` names a *port* for a press (the bay is the port's): the destination table
has one entry a bay, and `Deliver` already reads the bay from the port.

**Wants** are the other half: `ConstructWants` (new, beside `ConstructReserve`; one table a world, at most 1,024):

| Field | Meaning |
| --- | --- |
| `Dest`, `Port` | who wants it (store handle; a press's bay) |
| `Def` | the kind |
| `Count`, `Riding` | still wanted; of those, already on a conveyor toward it |
| `Owner`, `OwnerId` | whose order it is for (as a reservation's) |
| `Maker` | 0, or the store handle of the machine asked to make it (pieces from that machine go to this want first) |
| `Prio`, `Age` | section 7.3 |

A reservation is a claim on parts that exist in a store; a want is a claim on parts that will arrive. Together: *free stock =
held - reserved*, and *still to find = asked - got - riding - reserved for it*. No part is counted twice because every part is
in exactly one of: a store (claimed or free), a rider (its `Tag`), a machine's queue (its want's `Count - Riding`).
Wants are never saved; they are posted again from orders after a load (section 10).

### 4.2 Where a destination is assigned

`ConstructLogistics.Assign(world, c, def, fromPart)`, called once for a piece:

| Moment | Caller |
| --- | --- |
| A machine puts a piece on its conveyor and a Router is downstream | `ConstructConveyor.HandOff`, `PutMade` (before `Insert`) |
| An order sends a piece from a rack | `ConstructCrafting.StepTerminal` (it already knows the destination: no search) |
| A piece with no destination reaches a Router (an old save, a line with dumb machines upstream, a piece put on by a mod) | `Pass` |
| A piece's destination has gone or closed (4.4) | `Pass` |

The choice, in order:

1. The open want for this kind on this network with the highest priority, then the oldest, whose `Maker` is this machine or
   is 0, that has a way from here wide enough for the piece. `Riding++`.
2. Else storage (D5): the stores of this network that take overflow, accept this kind (their filter, 7.1), and have room
   for it less what is already riding to them; highest store priority first, then the shortest way. Flag *storage*.
3. Else none. At a source, the piece stays in the machine: stall `ExtrudeStall.NoStorage`, line state Blocked, "blocked: no
   storage on the network has room for it". At a Router mid-way: 4.4.

Step 1 scans the wants table (1,024 small structs, one pass, about a microsecond) once per piece. If measuring says it
matters, wants are bucketed by network; not before.

### 4.3 Choosing the branch

`Pass` for a Router with power, once per lead piece per step it is at the head of the input:

| Step | Rule |
| --- | --- |
| 1 | No `Dest`, or `Dest` gone, closed, or on another network: `Assign` |
| 2 | `out = Hop[router, dest]` (one array read; the column is made if missing, 2.2) |
| 3 | `out` exists and the piece is no wider than `Narrow`: `Takes(out)`. Yes: it crosses (as today: `Remove`, `Insert`, `ViaPlus1`), `Hops++`, `Wait = 0`. No room: `Wait++` and try again next step; nothing else is recomputed |
| 4 | No `out`, too narrow, or `Wait` past 50 steps (5 s): section 4.4 |

A piece between junctions costs what it costs today: nothing is added to the per-rider loop. The Router's work is per lead
piece at its input, and only while one waits there.

### 4.4 When it goes wrong

| Case | Detected | What happens | Reason text (order row, Router, board) |
| --- | --- | --- | --- |
| Way broken (cut, deleted, a part gone) | step 2: no hop after the rebuild | The want gives the piece up (`Riding--`), the piece is re-assigned: another want, else storage, flag *overflow* | "no way to the Press: its conveyor is cut" |
| Way too narrow | step 3 | same | "the way to the Press is 0.30 m wide: the piece needs 0.50 m" |
| Destination full or refusing (tray full, bay holds another piece, rack too short) | the piece waits at the destination's end as today; pieces behind it back up; at a Router, `Wait` past 5 s toward that destination | The *waiting* piece at the Router goes to storage as overflow; the order keeps its count and fetches it back from that store when there is room (stock first). The piece already at the destination's door stays | "held: the tray is full - take parts out" (the destination's own refusal) |
| Order cancelled or satisfied while riding | step 1 (the want is closed) | re-assigned; usually storage | "order cancelled: to storage" |
| Loop (a Splitter keeps dealing it back) | `Hops` reaches 32 | to storage as overflow; `Hops` reset. If it loops again it is *stuck* | "going round: a Splitter keeps sending it back" |
| Storage full or none | `Assign` step 3 at a Router | The piece waits at the Router's input, flag *stuck*; the Router's lamp blinks red, Alarm is 1, the board marks it. Tried again when the stamp moves, a store's version changes, or every 2 s. With `routerHold` on this is also what a broken way does | "stuck: no storage on the network has room" |
| Router without power | | deals in turn like a Splitter; destinations are kept and honoured by the next powered Router | "no power: dealing in turn" |
| Destination's construct part destroyed | handle no longer resolves | as way broken | "the Press is gone" |

Nothing is ever dropped loose by the network: a loose body is only made by a cut conveyor under a piece, as today.

**Overflow rule (D5) in one line:** a piece that has lost its destination, or cannot get to it within 5 s at a Router, goes
to the highest-priority store on its network that accepts it and has room; its order is told why and claims it back from
there.

## 5. Orders pull everything (D3a)

### 5.1 The model

The **planner** (`ConstructLogistics.Plan`, new file `ConstructLogisticsPlan.cs`) replaces "ask the first machine" in
`StepTerminal` and adds the levels below it. It runs at 2 Hz and only for bodies with open demand (`world.CraftOpen`, widened
to bodies with stock targets or chain entries).

| Term | Meaning |
| --- | --- |
| Root | Something that wants a product for itself: a terminal's or station's `CraftOrder`, a stock target (section 6) |
| Need | What a machine lacks for its **head** order: parts (an Assembler's recipe parts, a press's blank and bay 2 kit, a Panel Former's sheet) and bulk (units of a material) |
| Chain entry | "machine M was asked for N of kind K for destination D, because of root R": `{Id, Maker, Kind, Asked, Made, Dest, Root, Parent, Depth, State, Reason}`. One table a world, at most 256, at most 64 a network. Server only; a summary goes to clients (section 10) |

### 5.2 The algorithm: one level a step

Each planner step, for each root and then each chain entry (oldest first), the same four moves:

| Move | For a need of kind K, count N, at destination D |
| --- | --- |
| 1 Count | `short = N - in D - riding to D (wants) - reserved for D - asked for D (chain)`. Nothing short: done |
| 2 Stock | Reserve free stock of K in the network's stores for D (`ConstructReserve.Add`, owner D). A store with a conveyor way to D sends one piece a step as a rider with `Dest = D` (the Routers do the rest; `RouteTo`'s "joins only" limit goes: the map answers "is there a way"). A store with a chute into D is emptied by the chute's own moves. Neither: "no route from X: collect it there", as today |
| 3 Make | Still short: pick a maker on the network. Candidates accept the order (`WorkOrders.Refusal == null`); prefer one already making K, then the shortest queue, then the shortest way to D. Queue a `WorkOrder` (count rounded up to the recipe's batch), add a chain entry, post a want `{Dest D, Def K, Count, Maker}` |
| 4 Explain | Nobody accepts: the entry's parent is marked `Waiting` with the best refusal ("nothing on the network makes the Gear Set", "the Bearing needs a tier 2 Assembler"). It stays open: stock that turns up (by hand, by another line) is claimed at move 2 on a later step |

The maker now has a head order; **its** needs are found by the same four moves on the next step. So a chain of depth 4
is fully ordered 2 s after the root, each level reserving before the next looks, and no step does more than one level.
A step makes at most 8 new chain entries a network (the rest next step).

Needs are worked out for the head order, and for the one after it only when the head's inputs are complete, so a machine
never hoards inputs for work it can't start.

### 5.3 Guards

| Guard | Rule |
| --- | --- |
| Cycle | Before move 3, walk the entry's `Parent` links: if K is already being made for an ancestor, stop with "recipe loop: the Gear Set needs itself". At most `MaxDepth` steps |
| Depth | `MaxDepth = 6` levels below a root: "chain too deep (6 levels)" |
| Size | 64 entries a network, 256 a world; full: the parent waits with "the network is planning as much as it can (64)" and continues as entries finish |
| Count | A need's count is capped at the maker's `WorkOrders.MaxCount` (999) a line; the remainder is asked when that line is done |
| Double asking | The count at move 1 includes `asked for D`, and a chain entry is the only thing that may queue for D |
| Stale entries | An entry whose maker has gone, or whose work order is no longer in the maker's queue (the player pressed Clear), is dropped and its parent asks again |

A `WorkOrder` gains a server-only `Chain` (u16: the entry's id; 0: the player's own order). It is saved (section 10), not sent.
The machine's `Made` reports to its entry; a made piece leaving the machine is assigned to that entry's want first.

### 5.4 "Down to ore" (*proposed*; question Q3)

Parts are counted; bulk is levelled.

| Level | What the planner does |
| --- | --- |
| A part made from parts (Assembler, Press) | exact chain entries, as 5.2 |
| A piece made from bulk (Extruder, Formers, Panel Former sheet) | exact chain entry for the piece. Its bulk need (units of metal) is a **bulk need** on the machine |
| Bulk need at a machine | Met by the chutes into it, which already pull what it accepts. The planner checks only that it *can* be met: a chute edge from a store or refiner holding or making that material. It reserves nothing by unit (bins are buffers). If the head order's material isn't arriving: move 4, "the Extruder needs metal: no bin on the network holds it and no refiner makes it" |
| A refiner whose output is short | Refiners take no queue. The planner sets a **wanted output** on it (server field in its store, `CStore.Wanted`): while set and its `recipe` setting is Auto, the recipe filter is that output's recipe (the same filter a set recipe uses, so nothing is stranded). It runs as long as fed; its output bin is the buffer |
| A gatherer (Drill, Saw Module) | Nothing is ordered: it digs while it has power, a deposit and room. The planner reads its state for the explanation: "needs iron ore: the Drill Module is starved - nothing below" |
| The end of the chain | A material no bin holds, no refiner makes and no gatherer brings: "needs copper ore: nothing on the network brings it" |

So "down to ore" means: every part and piece is ordered exactly; every bulk input is checked back to a bin, a refiner or a
gatherer, with refiners steered to the wanted output; and the first missing link is named. Exact bulk orders (reserve 12 metal
for this beam) are not proposed: a bin holds one material, chutes move loads, and levels (section 6) do the job with far less
state.

An Assembler needs a way to receive parts by conveyor for any of this to be seen: **phase 2 adds an implicit "Piece in" to
every Assembler** (opposite its Piece out; `ConstructAssembling.TakeFromConveyor`, which is `WantsPart` then `FeedPart`, the
chute's own test). Chutes into an Assembler keep working.

### 5.5 Cancel and partial failure

| Event | Result |
| --- | --- |
| Root cancelled | Its reservations and wants go at once (as today). Every chain entry under it: an unstarted work order leaves its maker's queue; a started one finishes the piece on the die (no waste) and stops; pieces riding go to storage at their next Router ("order cancelled: to storage"). Inputs already inside a machine stay there for its next order |
| One branch has no maker | The other branches go on. The root's row: "3 of 5 inputs coming - waiting on Gear Set: nothing on the network makes it". Everything gathered stays reserved, so supplying the missing part by hand finishes the order |
| A maker destroyed mid-order | Stale entry (5.3): asked again elsewhere, or move 4 |
| A machine stopped at its console | Its entries wait: "queued at the Assembler: its line is stopped". The network never starts a line the player stopped |
| A root that can never finish | It waits, with its reason. No time-out: the player cancels. (A station's job already names "NOTHING MAKES" before Start.) |

### 5.6 What the player sees when a link has no maker

| Place | Text |
| --- | --- |
| Terminal and station panel, Orders tab | The order's row, and under it an indented line a chain entry (at most 6 shown, "and 3 more"): `Axle x2 - Assembler 2: making 1 of 2`, `  Gear Set x4 - waiting: nothing on the network makes it` in the Fault colour |
| The terminal's screen | the first failing reason in place of "queued at X" |
| The machine that waits | look line and screen: `Starved: needs 4 Gear Set for the Axle (Building Station: Buggy) - nothing makes it` |
| A machine working for the network | its queue rows end in `for Crafting Terminal` / `for stock: Long Piece Rack` |
| The board (section 9) | the waiting machine blinks in its state colour; the caption names the reason |
| Line Console | its detail line is the machine's, as today |

## 6. Stock targets (D3b)

| Point | Design |
| --- | --- |
| What | A store may hold up to 8 targets: `{kind (a WorkOrder's What: piece key, part, or name), keep}`; a bin's is `{material, keep units}` (one: a bin holds one material). `CPart.Targets` (`StockTargets`, on the part so it moves with it) |
| Where set, parts and pieces | The Crafting Terminal's panel gains a **Stock** tab: the same tiles; pick one, pick a store of the network from a list (name, fill, what it keeps), a count, **Keep**. Rows list every target on the network with have / keep / on order and **Remove**. `CActKind.StockTarget` (body, store, op, the kind's 22 bytes, keep) |
| Where set, quick | Configure tool on the store, group Logistics: `Keep what it holds` (count; applies to each kind in it now, up to 8) and, on a bin, `Keep units` (0 off). So a target needs no terminal |
| Standing order | A target is a root (5.1) owned by its store: destination the store, kind, count. The planner serves it like any order: stock from *other* stores is **not** moved (a target never robs a store; it only makes), so move 2 is skipped |
| Hysteresis | Two marks: `keep` and `low = keep - max(1, keep / 4)`. An order opens only when `have + riding + asked <= low`, for `keep - have - riding - asked`, rounded up to the maker's batch. One open order a target; a new one no sooner than 5 s after the last closed. So "keep 20" refills at 15, to 20, and a part taken now and then does not start the line each time |
| Bulk targets | `keep 200` on a bin: the refiners and gatherers whose chutes lead to it are **held** (line state Stopped, "target met: 200 metal") from `have >= keep` until `have <= low`. This is the only throttle on bulk; a bin with no target fills as today |
| Priority | Stock targets are served after every order (7.3), so a refill never starves a build |
| Room | A target never asks for more than the store has room for, less what rides to it |
| Seen | The store's look line: `keeps 20 Steel Beam (has 14, 6 on order)`; its monitor adds a row a target; the board draws the keep mark on its fill bar |
| Checked | 1 Hz, spread by store handle (a store is looked at once a second), only stores with targets (`world.TargetStores`) |

## 7. Overrides (D2)

### 7.1 The settings

All are configure-tool settings in a new group **Logistics** (`CfgKey` values at the end of the enum; saved, in blueprints
and on the wire as every setting). None needs to be touched for the network to work.

| On | Setting | Values | Default | The user's example |
| --- | --- | --- | --- | --- |
| Any store | `storeTakes` | Anything; Only what it holds now; a material (metal, planks, rubber ...: the Extrusion table's and bins' materials); Nothing (orders and targets only) | Anything | "this storage takes only steel" |
| Any store | `storePriority` | 1 to 5 | 3 | which store fills first |
| Order-taking machine, press, terminal, station | `feedPriority` | Low, Normal, High | Normal | "this machine gets fed first" = High |
| Line Console | `linePriority` | Low, Normal, High | Normal | "this line has priority": applies to every machine wired to its Run |
| Router | `routerSpill`, `routerHold` | 3.3 | Auto, Off | a manual lane |

"Only what it holds now" is read live (the kinds in the store when a piece asks), so it needs no stored list: an empty store
set to it takes nothing until the player puts the first piece in by hand. A material filter accepts pieces and panels by their
key's material and bulk by material; catalogue parts have no material and are refused by it.

### 7.2 Conflicts

| Conflict | Winner |
| --- | --- |
| A machine's `feedPriority` and its console's `linePriority` | The machine's when it isn't Normal; else the console's (the highest of several consoles) |
| A store's filter and a stock target on it | The target: a store always accepts what it keeps. The look line says "takes only metal, and keeps 20 Gear Set" |
| A store's filter and an order that reserves stock already in it | Nothing to resolve: filters only govern what *arrives* as storage or overflow |
| A filter and overflow | Filters hold for overflow too. A piece no store accepts is *stuck* at its Router (4.4): "stuck: no storage accepts planks" |
| `routerSpill` pinned and the map's storage choice | The pin wins at that Router for pieces flagged storage; ordered pieces always follow their order |
| `routerHold` and overflow | Hold wins at that Router |
| Two stores equal in priority | The shorter way from the piece; then the lower part index (stable) |
| A terminal's tray set as a store | Not possible: trays and holds are never storage |

### 7.3 Priorities between competing orders (*proposed*; question Q2)

When two wants ask for the same kind (a made piece, or free stock):

1. **Class**: High, Normal, Low from 7.1 (the destination's `feedPriority`, a root's own destination for a chain entry's
   pieces, so a build marked High pulls its whole chain first). Stock targets are always below Low.
2. **Age**: the older want first (a u32 counter stamped when the root was placed; a chain entry inherits its root's).
3. A want with `Maker` set is served by that maker's pieces before any other want of the same class.

Makers work their queues in order, as today; a High root's chain entries are queued *ahead of* Normal ones not yet started
(inserted after the head). No starvation guard is proposed: age within a class is fair, and the player sets classes.

## 8. Chutes and loose material (*proposed*; question Q4)

**Recommendation: chutes are not routed. They stay as they are, and the network steers bulk by levels.**

| Why not the same routing | |
| --- | --- |
| Bulk has no identity | A unit of metal can't carry a destination; "this metal is for that order" would need per-unit bookkeeping in every bin |
| A chute already pulls | Its end accepts or refuses (`Feed`, `WantsPart`, a recipe filter), so material only goes where it can be used |
| D1 | The Router is a conveyor junction; chutes "stay" |
| What the network adds for bulk instead | Membership (2.1); "can this material reach that machine" for explanations (5.4); a refiner's wanted output (5.4); bulk targets that hold refiners and gatherers (section 6); bins on the board with their fill |

Parts that move by chute (a shelf into an Assembler) keep working and count as "a chute into D" at move 2 of the planner.
The one thing chutes can't do, share one bin's output between two machines *by need*, is a later option: a chute
junction's `Share` weighted by each end's open bulk need (no new part). Not in the phases below.

## 9. The network board (D4)

A display wired to a Router's **Data out** is the board of that Router's network (`ProductionLines.CanMonitor` displays;
`MonitorKind.Network`, `ProductionLines.Monitors.cs` calls `ConstructLogisticsBoard.Fill`, new file). Any Router of the
network gives the same board.

### 9.1 By display size (the monitor bands)

| Band | Shows |
| --- | --- |
| Tiny (1 line) | `Network: 14 moving, 3 orders - storage 57%`, or `STUCK at Router 2: no storage has room`, in the worst colour |
| Small (3-6 rows) | the title line, then: stuck spots, then stores by fill (`Long Piece Rack 62%`), moving on by itself as a monitor list does |
| Medium (7-14 rows) | title; `Moving 14  Orders 3  Waiting 1`; open roots with their state (the terminal's rows); stores with fill and targets |
| Large (15 rows up, and the panel at least 0.8 m wide) | **the map** (9.2) with a two-line caption under it. A large panel too narrow for the map shows Medium |

### 9.2 The map (`PanelDraw.Map`, new, beside `PanelDraw.Flight`)

| Layer | Drawn from | How |
| --- | --- | --- |
| Layout | The map's nodes and the links' own nodes (`CRouteNode.Pos`) in the body frame, flattened on the two axes the network is widest on, scaled to the panel with a margin, aspect kept. A real plan of the floor: what is left of the Press in the world is left of it on the board |
| Conveyors | their polylines, 2 px lines in the conveyor's paint colour (dim), an arrowhead at each end. Cut: red, dashed. `PanelRender` draws axis-aligned quads: a 45 degree run is a stair of short quads (or a rotated-quad helper, the builder's choice) |
| Chutes | 1 px dim lines (they join things; nothing moves on them visibly) |
| Machines | a square in its line state's lamp colour, blinking as its lamp does; a two-letter tag (EX, AS, PR, PF, WF, RF, CT, BS) when the cell is 10 px or more |
| Stores | a box with a fill bar (rack: `ConstructRackPack` fill; bin: amount / capacity), a tick at each target's keep mark |
| Routers, Splitters, Joins | a diamond, a triangle, a small dot; the Router's chosen way a short bright stub |
| Pieces | a dot at each rider's place along its link (the same `Pose` maths, flattened): orange for an order, white for storage, red for stuck. At most 256 dots; beyond that every n-th |
| The stuck spot | a red ring blinking 3 Hz round the Router or link end, and the caption's first line: `STUCK at Router 2: Long Piece Rack full` (the oldest stuck piece; `+2 more`) |
| Caption | line 1 the stuck reason or `All moving`; line 2 `14 moving  3 orders  storage 57%` |

### 9.3 Cost

| Part | When | Target |
| --- | --- | --- |
| Static layer (links, nodes, tags) | built into the panel slot's own quad list when the map's stamp or the panel's size changes | 0.5 ms once; at most 1,024 line quads (longer polylines are thinned) |
| Dynamic layer (dots, fills, states, ring) | at the panel fill (4 Hz, in view only): one pass over the body's riders and the map's stores | 0.15 ms, 0 bytes |
| Between fills | dots are moved on along their line segment by the frame's time (as the world's riders are drawn between steps), so they glide; no fill | a few adds a dot |
| Server | nothing: the board is each viewer's own, from what that peer already holds (links, riders, stores, line states). Alarm and Moving are worked out at 10 Hz with the consoles' outputs, only for bodies with a Router | |

## 10. Net and saves

| Phase | Protocol | On the wire |
| --- | --- | --- |
| 1 | **262** | `CBodyKind.Riders`: each rider gains `u16 dest part` (0xFFFF none), `u16 tag`, `u8 flags` after `ViaPlus1`. New `CfgKey`s `routerSpill`, `routerHold` (settings travel as settings). `CBodyKind.Craft` unchanged |
| 2 | **263** | New `CBodyKind.Logi` (at the end of the enum), per body on a change (at most 2 Hz), after a Full and to a joiner: `u8 n`, then per chain entry `u16 id, u16 maker part, u16 dest part, kind (ConstructStorage.WriteDef), u16 asked, u16 made, u16 root owner part, u16 root id, u8 depth, u8 state, u8 reason`; at most 64. Reasons are codes; each peer makes the text |
| 3 | **264** | `CActKind.StockTarget`; `CBodyKind.Logi` gains a second list: per store with targets `u16 part, u8 n`, each `kind, u16 keep, u16 on order`. `CfgKey`s `storeTakes`, `storePriority`, `feedPriority`, `linePriority` |
| 4 | none | The board is local. Router outputs are ordinary signals |
| 5 | none expected | A held refiner or gatherer says so through its store's `Blocked` byte (a new `GatherStall` / stall value), sent as it is today. `CStore.Wanted` is server only |
| 6 | 265 if built | remote members |

Numbers are for planning: main is 260, 261 is the Press's; when two branches bump, the merger renumbers. Each bump gets its
comment line above `ServerBrowser.ProtocolVersion`. New messages and kinds go at the end of their enums.

| Question | Answer |
| --- | --- |
| What a late joiner needs | The Full (links, settings: the map is built from these on the client), then `Riders` (with destinations), `Orders`, `Craft`, `Logi`. No map, no next hops, no wants, no reservations beyond what `Craft` already sends |
| What a client works out itself | the graph and groups, the Router's shown choice (3.2), every look line and board, line states |
| What only the server holds | next-hop columns, wants, chain entries in full, `Wait`, `Hops`, target timers |
| Bandwidth | `Riders` is already the whole list on a change: +5 bytes a rider (1,024 riders: +5 KB worst case, on changes only). `Logi` at most about 64 x 20 bytes, at most twice a second while a chain changes |

**Saves.** A new block after a body's station block, **`LOGI`**, length-prefixed (an older build skips it; an older save ends
before it), written only when it has content:

| In the block | Form |
| --- | --- |
| Rider destinations | per rider in the riders block's order: dest part index (0xFFFF none), tag, flags. `Hops` and `Wait` start at 0 |
| Chain entries | id, maker part, dest part, kind (by the save's names), asked, made, root owner part, root id, parent id, depth |
| Work-order marks | per machine queue: the `Chain` id of each order (so a loaded queue is not asked for twice) |
| Stock targets | per store: its targets (kind by name, keep) |
| Not saved | wants and reservations (posted again by the first planner step from roots, chain entries and riders' tags), the map, `CStore.Wanted` (set again by the planner) |

Settings save as settings. **Old saves** load unchanged: riders have no destination and get one at their first Router; queues
have no marks, so every order in them is the player's own; nothing is asked twice. **Blueprints**: settings ride as settings;
a store step may carry `"targets": [{"piece": "...", "keep": 6}]` (parsed like a bill line, `ConstructBill.ParseLine`); no
chain entries, no riders (a blueprint is a design). Copies and player blueprints keep the settings; targets when the bill
line writer is extended (phase 3, with the showcase).

## 11. Performance budget

The 8 ms tick rule is the ceiling; these are the targets on the 2-core box, release build, measured with a throwaway probe
that is removed (no new self-test sections).

| What | Budget | Allocations |
| --- | --- | --- |
| A piece between junctions | +0 (the rider loop is untouched; `CRider` is 7 bytes bigger) | 0 |
| A Router's decision | one `Hop` read, one `Takes`; under 0.5 microseconds; once per lead piece per step it waits | 0 |
| `Assign` | one pass of the wants table and the group's stores; under 3 microseconds; once a piece, again only on a failure | 0 |
| Conveyor step, 200 riders, 20 links, 5 Routers | at most 0.07 ms (today with Splitters: 0.05 ms, max 0.15) | 0 |
| Map graph rebuild | 0.3 ms at 254 links, once per link change | 0 after the first (pooled arrays) |
| One next-hop column | 0.02 ms; at most 4 a conveyor step | 0 (stack spans) |
| Planner | 2 Hz, only bodies with open demand: 0.05 ms a network with 32 roots and 64 entries; at most 8 new entries a step | 0 in a steady step; a new chain entry's note string only when its reason changes |
| Stock targets | 1 Hz spread: a few compares a target | 0 |
| A world with Routers but nothing moving and no orders | one list check a tick (as `CraftOpen`) | 0 |
| Board fill | 0.15 ms at 4 Hz in view; static layer 0.5 ms on a change | 0 a fill |
| Client step | today's 0.003 ms; the Router's shown choice is read while drawing its part | 0 |

**Numbers to measure and report, per phase:** the conveyor step's median and worst (200 riders, 5 Routers); the worst server
tick while a 254-link construct rebuilds its map and the first 4 columns; `Assign` with the wants table full; the planner's
step with 32 roots; bytes allocated over 600 steady ticks (expect 0); `Riders` message size at 200 riders; on a host and a
joined client at 80 ms and 2 % loss: every rider's destination, every order row and the board's caption equal on both peers.

## 12. Phases

Each is 60 to 90 minutes with one stop point, on its own branch (`feat/logistics-N`), WIP commits with `[skip ci]`, a build
that passes, a by-hand run reported with what was not verified, no new self-test sections, docs (`production.md`, a new
"Smart logistics" section listed in `CONSTRUCTS.md`) updated at the end of the phase. Phases 1 and 2 touch
`ConstructConveyor.cs`, `ConstructCrafting.cs` and `ConstructPorts.cs`, which the Press rework also touches: run them one
after another with it, never side by side.

The window tool gains one verb family for these runs, `logi` (in the window tool's action table, as `craft` and `station`
are): `logi net PART#n` (groups, destinations, stores to the log), `logi riders` (each rider: kind, link, destination, flags),
`logi chain` (chain entries), `logi why PART#n` (a Router's or machine's reason). They print `[logi]` lines; no pictures are
needed to judge a run.

### Phase 1: Routers send pieces where they are wanted, else to storage (shippable alone)

| | |
| --- | --- |
| Builds | `ConstructLogistics.cs` (the map: nodes, edges, groups, lazy next-hop columns, `Assign`, storage choice, stamp); `ConstructWants.cs`; `CRider`'s new fields; `Pass` by table for a powered Router (4.3) with the failures of 4.4; the source's `NoStorage` stall; the Router parts (generator, `"router"`, `"way"` boxes, `"work": "turn"`); the lit choice and look lines (3.2) without the screen; `routerSpill`, `routerHold`. Terminal and station: when an order asks a machine it posts a want, so the made piece rides **straight to the tray**; `RouteTo` asks the map, so stock comes back through Routers. Storage choice: stores with room, nearest first (filters and priorities are phase 3) |
| Files | new `Source/Constructs/ConstructLogistics.cs`, `ConstructWants.cs`; `ConstructConveyor.cs`, `ConstructCrafting.cs`, `ConstructRoutes.cs` (stamp), `ConstructParts.cs`, `ConstructPorts.cs`, `ConstructConfig.cs`, `ConstructNet.cs`, `ConstructExtruder.cs` / `ConstructAssembling.cs` / `ConstructPress.cs` (the new stall's text), `ConstructReplace.cs`, `ProductionLines.cs` (Blocked from the stall), `Render/ConstructRender*.cs` (way boxes, turn), the look-line callers (grep `ConstructConveyor.Look` and `JamText`), `App/Program.WindowShot.cs` (the `logi` verbs), `PartKit/Generators/ConveyorJunctionParts.cs`, `ProductionShowcase.cs`, `Net/ServerBrowser.cs` (262), world save (riders' destinations in `LOGI`) |
| Stop point | On the showcase: a part ordered at the terminal rides from the Assembler through the Router into the tray without touching a rack; the bill's parts go to the rack; with the rack full the Assembler shows Blocked "no storage on the network has room" |
| Try by hand | `--window-shot out.png --blueprint "Production Showcase" --do "near Crafting Terminal 3; craft order Gear Set 2; wait 600; logi riders; aim Conveyor Router#1; logi why Conveyor Router#1; logi net Conveyor Router#1"`; then cut the Router's tray-side conveyor mid-order (the piece goes to the rack, the order row says why); then host and a joined client (`--net host`, `--net join:IP`): the Router's lit way and both look lines equal |
| Keeps working | Every line without a Router, byte for byte (Splitters, Joins, racks, the press line, the terminal's fetch through Joins, old saves, blueprints). A Router without power is a Splitter |

### Phase 2: Orders pull their inputs (chains)

| | |
| --- | --- |
| Builds | `ConstructLogisticsPlan.cs`: needs, the four moves, chain entries, guards (5.3), cancel (5.5); `WorkOrder.Chain`; machine needs for the press (blank, bay 2), the Panel Former (sheet) and the Assembler (parts); the Assembler's implicit **Piece in** and `TakeFromConveyor`; `StepTerminal`'s "ask the first machine" replaced by the planner; `CBodyKind.Logi`; the chain lines in the terminal's and station's Orders tab and on waiting machines (5.6); bulk only as far as "can it reach" explanations |
| Files | new `ConstructLogisticsPlan.cs`; `ConstructCrafting.cs`, `ConstructStation.cs` (rows only), `WorkOrders.cs`, `ConstructAssembling.cs`, `ConstructPress.cs`, `ConstructPanelFormer.cs`, `ConstructPorts.cs`, `ConstructConveyor.cs` (`Deliver`), `ConstructNet.cs`, `UI/UiCrafting.cs`, `UI/UiStation.cs`, save (`LOGI`: entries, marks), `ServerBrowser.cs` (263), showcase |
| Stop point | Ordering an Axle-like part whose recipe needs a part another Assembler makes: one click, both machines queue, the input rides into the first Assembler's Piece in, the product rides to the tray; with the second Assembler removed the row reads "waiting on Gear Set: nothing on the network makes it" |
| Try by hand | `--do "craft order <two-level part> 1; wait 300; logi chain; wait 1500; logi chain; logi riders"`; cancel mid-chain (`craft cancel 1`) and check queues and riders; save and load mid-chain (nothing asked twice); a recipe loop made with a mod recipe file gives "recipe loop" |
| Keeps working | The player's own Queue, Bill and Send (orders with `Chain` 0 are never touched by the planner); a station build on a dumb line; chutes into Assemblers |

### Phase 3: Stock targets and overrides

| | |
| --- | --- |
| Builds | `StockTargets` on stores, the standing-order roots and hysteresis (section 6); the terminal's Stock tab and `CActKind.StockTarget`; the quick `Keep what it holds`; `storeTakes`, `storePriority`, `feedPriority`, `linePriority` and the conflict rules (7.2); priority classes and age in `Assign` and in queue insertion (7.3); look lines and store monitors showing targets; blueprint `"targets"` |
| Files | `ConstructLogistics.cs`, `ConstructLogisticsPlan.cs`, new `StockTargets.cs`, `ConstructStorage.cs`, `StoreMonitor.cs`, `ConstructConfig.cs`, `ConstructCrafting.cs`, `UI/UiCrafting.cs`, `Program.SandboxCrafting.cs`, `WorkOrders.cs` (`ParseLine`), `ConstructNet.cs`, save, `ServerBrowser.cs` (264), showcase |
| Stop point | A rack set to keep 6 L-angles refills by itself when 2 are taken (and not when 1 is); a rack set to planks only never receives metal overflow; a High station gets a shared part before a Normal terminal |
| Try by hand | `--do "near Long Piece Rack 2; logi why Long Piece Rack#1; touch take@Long Piece Rack#1; touch take@Long Piece Rack#1; wait 900; logi chain"`; set `storeTakes` with the configure tool and send the bill; two orders for one kind with different `feedPriority` |
| Keeps working | Stores with no setting behave as in phase 1; orders with no priority set are first come, first served |

### Phase 4: The network board and the Router's screen

| | |
| --- | --- |
| Builds | `PanelDraw.Map` in `PanelRender`; `ConstructLogisticsBoard.cs` (the four bands, static and dynamic layers, the stuck spot, caption); `MonitorKind.Network` from a Router's Data out; the Router's own screen (`router` source); the Router's Alarm and Moving outputs |
| Files | new `ConstructLogisticsBoard.cs`; `Render/PanelRender.cs`, `Constructs/DisplayPanel.cs`, `ProductionLines.Monitors.cs`, `ConstructPorts.cs` / `ConveyorJunctionParts.cs` (ports, screen), `ProductionLines.cs` (`Step`: outputs), showcase |
| Stop point | A Large Text Sign wired to a Router shows the showcase's lines as a plan with dots moving along them and the racks' fills; filling the rack marks the stuck Router in red with its reason; a Number Readout on the same port shows the one-line form |
| Try by hand | `--window-shot board.png --blueprint "Production Showcase" --do "near NETWORK BOARD 4; wait 600" --frames 900 --every 300` (this phase is the one where the agent opens its own pictures: the layout is the point); the same on a joined client |
| Keeps working | Every other monitor and display; a display on a console's Data out is still that line's board |

### Phase 5: Bulk: levels, wanted output, down to ore

| | |
| --- | --- |
| Builds | Bulk targets on bins holding refiners and gatherers (section 6); a refiner's `Wanted` output steering its Auto recipe (5.4); the planner's bulk reach check through chute edges to bins, refiners and gatherers, and its reasons down to "nothing on the network brings it"; the new held state in line state and lamps |
| Files | `ConstructLogisticsPlan.cs`, `ConstructRefining.cs`, `ConstructGatherer.cs`, `ProductionLines.cs`, `ConstructStorage.cs`, `StoreMonitor.cs`, showcase |
| Stop point | With the metal bin set to keep 100, the Smelter stops at 100 and starts again at 75; ordering a steel beam with the bin empty starts the Smelter by itself; with the ore box empty the order reads "needs iron ore: nothing on the network brings it" |
| Try by hand | `--do "logi why Smelter#1; craft order <beam> 4; wait 600; logi chain; logi why Extruder#1"` in a survival world copy (creative refiners finish in seconds) |
| Keeps working | Bins with no target; refiners with a set recipe; consoles' Start and Stop (the network never starts a stopped line) |

### Phase 6 (optional, only if Q1 says so): remote members over chute world links

Stores and machines of a construct joined by a chute world link can be claimed and asked; bulk and chuted parts arrive by
the link; pieces don't cross. Files: `ConstructLogistics.cs` (groups across bodies by tag), `WorldLinks.cs`,
`ConstructCrafting.cs`. Stop point: a terminal on a base orders a part made on a parked factory truck and it arrives by chute.

## 13. The Production Showcase

All through its generator (`Source/PartKit/Generators/ProductionShowcase.cs`, `--gen-parts production_showcase`, after the
part generators), then `--look-report` on the file. One sign a thing to try, as now.

| Phase | Added |
| --- | --- |
| 1 | A new method `SmartCorner()`: the Assembler's Piece out now runs to a **Router**; its ways go to the Crafting Terminal's Piece in, the Building Station's Piece in and the Assembler's Piece Rack (storage). The rack's Piece out returns through a Join to the Router's input. Line A's Splitter (press or long rack) becomes a second Router. Signs: "ORDER AT THE TERMINAL: THE PART RIDES STRAIGHT TO THE TRAY", "SEND THE BILL: UNORDERED PARTS GO TO THE RACK", "LOOK AT THE ROUTER: IT NAMES THE DESTINATION", "CUT THIS CONVEYOR: THE PIECE GOES TO STORAGE AND THE ORDER SAYS WHY". The spur's too-narrow conveyor stays as it is |
| 2 | A second, small Assembler set to nothing (the network gives it orders), its Piece out through a Join to the Router, and a conveyor from the Router's free way to the first Assembler's new Piece in. The press gets its blank by order: sign "ORDER A BRACKET: THE EXTRUDER, THE ROUTER AND THE PRESS DO THE REST" (after Press phase 2 gives the press orders). Sign at the terminal: "ORDER A PART MADE OF PARTS: WATCH BOTH ASSEMBLERS START" |
| 3 | The Long Piece Rack keeps 6 L-angles (`"targets"` on its step); a new small Piece Rack beside it set to planks only; the Building Station set to feed High. Signs: "TAKE TWO ANGLES: THE LINE MAKES TWO MORE", "PLANKS ONLY", "THE STATION IS FED FIRST" |
| 4 | A Large Text Sign on a mast at the right-hand end wired to the first Router's Data out, with a red Lamp on its Alarm; a Number Readout on the second Router. Signs: "NETWORK BOARD", "FILL THE RACK TO SEE THE STUCK SPOT" |
| 5 | The refining row's metal bin keeps 100; sign "THE SMELTER STOPS AT 100 AND STARTS AT 75" |

The floor is 64 by 14 m with a panels strip behind; the smart corner fits between the assembling and crafting blocks (the
terminal's return conveyor is rerouted through the Router). If the blueprint passes 500 steps the station's costing slice
(3.0 ms at 442 steps) is measured again.

## 14. Open questions for the user

Only real choices; the first option is the recommendation and what the phases assume. One at a time, as clickable options.

**Q1. How far does a network reach?**
- A (recommended): one construct. A base on its foundations is one network per joined group; no extra cost; phases 1 to 5.
- B: also constructs joined by a chute world link, for stock and bulk (pieces can't cross: conveyors don't leave a construct). Adds phase 6.
- C: everything within a radius of a base (a new "base" idea, no links needed). Largest change; not designed here.

**Q2. Two orders want the same part. Who gets it?**
- A (recommended): priority class first (High, Normal, Low, set per machine, line or station), then the older order; stock targets last.
- B: strictly the older order; no priority setting.
- C: shared in turn between everyone asking (fair, but both builds finish later).

**Q3. What does "down to ore" mean for loose material?**
- A (recommended): parts and pieces are ordered exactly; bulk is kept by levels (bins are buffers, targets hold refiners and drills, an order steers a refiner's recipe and names the first missing link).
- B: bulk is ordered exactly too: an order reserves 12 metal, the Smelter is asked for exactly 12, the drill for exactly 24 ore. Tighter, no surplus, but much more state in every bin and chute and slower to react.

**Q4. Do chutes get smart junctions too?**
- A (recommended): no. Chutes keep pulling what their end accepts; the network steers bulk through levels and recipes.
- B: later, a chute junction that shares by need (the machine with the open order gets the material first). No new part.
- C: a Chute Router part with the conveyor Router's rules (needs per-load destinations for bulk; depends on Q3 B).

**Q5. What does a plain Splitter do on a smart network?**
- A (recommended): stays as it is (deals in turn), for sharing work between twin lines on purpose; the replace tool swaps it for a Router.
- B: every Splitter on a network with a Router becomes smart by itself (nothing to replace, but old lines that relied on dealing in turn change behaviour).

**Q6. The Router's look.**
- A (recommended): the Splitter's roller table with a signal post (three arrow lamps and a small screen) and a diverter fence that swings to the chosen way; body logistics green.
- B: no post: only the deck arrows light and the fence swings (lower, cheaper to draw, harder to read from across a hall).
- C: a turntable: the whole deck turns with the piece to face the way out (best to watch; a new piece animation in a junction, about half a phase more).

**Q7. A piece that can't reach its destination and finds no storage with room.**
- A (recommended): waits at its Router, marked stuck (red lamp, Alarm, the board); nothing is dropped.
- B: is dropped loose beside the Router so the line keeps moving (pieces on the floor, more bodies).


## 15. User answers to the open questions (2026-11-12 11:30; these override the text above)

- Q1 Reach: **one construct** (A). No phase 6.
- Q2 Competing orders: **priority class, then age; stock targets last** (A).
- Q3 Loose material: **kept by levels** (A); parts and pieces ordered exactly.
- Q4 Chutes: **share-by-need later** (B): a later phase makes existing chute junctions feed the machine with an open order first; no new part, no Chute Router.
- Q5 Plain Splitter: **stays as it is** (A); the replace tool swaps it for a Router.
- Q6 Router look: **deck lamps only** (B): no signal post; arrows on the deck light and the fence swings. The destination is shown when the Router is looked at (and later on the Router screen, phase 4).
- Q7 Stuck piece with no storage: **waits at its Router**, marked stuck (red lamp, Alarm, the board) (A).
