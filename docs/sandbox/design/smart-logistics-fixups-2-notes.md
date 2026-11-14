# Smart logistics fix-ups 2: notes (2026-11-13)

Branch `claude/logistics-fixups-2-d99486`, Windows PC. **No wire change** (protocol stays 304). The faults left by
`verification-2026-11-13c.md` and `smart-logistics-phase5-notes.md`. Each was seen first in a throwaway window run of the Production
Showcase (`--window-shot ... --save-dir %TEMP%\lf2\... --blueprint "Production Showcase" --do "..."`), then tried again the same way.

| # | Fault | Result |
| --- | --- | --- |
| 1 | An unordered store part jams its maker's out conveyor at the Router (NoStorage) | **Fixed**, host and joined client |
| 2 | A terminal's tray refuses a store part, "the tray is full" | **Fixed**: the tray takes it |
| 3 | A hood's strips go to the Long Piece Rack, no route on to the Panel Former | **Not a fault** (cause below) |
| 4 | The terminal's row warns "too wide" only after the order | **Fixed** |
| 5 | `Rank` not saved | **Fixed**, older saves read Normal |
| 6 | ~125 B a step from `ConstructCrafting.Missing` | **Fixed**: 40 B a step to 0 on the showcase |
| 7 | Tried: two terminals racing, the Router's cost with riders, a two-level order with a stock target, Press stock x5 | Below |

## 1. A piece no rack can take never blocks a line
Seen: `logi feed 1 Ammo Container; wait 2000; logi riders`: `Ammo Container on 26 (Assembler#284 -> Conveyor Router#287) ... Stuck
reason NoStorage`, the Router's Alarm 1 and nothing behind it moving.

Now (`ConstructLogistics.NoPlace`): a piece with no destination still waits at the Router while some plain Piece Rack on its network
could take it once it has room (`CouldWait`: the rack's check is 0 or full, its filter accepts it, a way reaches it). That is the
back-pressure of a full store, as before. A piece no rack will ever take (a store part, a piece too big or filtered out everywhere,
no rack reachable) is **set off the line beside the Router** as a loose body (`ConstructConveyor.SetOff`: to the right of its way
in, clear of the Router), and the line runs on. The Router's Alarm stays on until the next piece crosses it or the network changes
(`LogiMap.OffDef`, server); its look line says `ALARM - set Ammo Container off the line beside it: no Piece Rack takes a store part
(a rack holds pieces)`, its screen `ALARM / Set off Ammo Container / no Piece Rack takes it`; the server's event line `Ammo Container set off the line beside Router 1: ...`. A joined client hears the
Alarm as the Router's signal: its lamp lights and its look line reads `ALARM - a piece waits here or was set off the line beside it`
(the reason is the server's: no wire change). Tried: a Gear Set fed after it crossed to the Piece Rack and the Alarm went to 0; with
every rack full of Gear Sets a Gear Set still waits stuck (no change). Not seen in a picture: where the loose container lies.

## 2. The terminal's tray and a store part
The tray refused a store part at `RackCheck` (code 2, a rack takes pieces, not stores) and the note read that as full. Seen on the
arrival path: `logi order Ammo Container 1; logi mix Assembler|rubber|10; touch Start @ Line Console#2`: the made container was turned
from the tray at the Router (`a part went to storage: its destination is full`). A tray, like a station's hold, only ever gets a new
empty part (an order's rider, or a def from stock: a store part is never in a rack's or a volume's stock, `logi stow` is refused
"storage doesn't go in storage"), so `RackCheck` now lets a Crafting Terminal's tray take one; the held note says "the tray is full"
only for a full tray. Tried: `#1 1/1 Ammo Container: in the tray`, the same on a joined client.

## 3. Strips to the Panel Former: not a fault
With strips on (`logi set Panel Former|formerStrips|1`, then the hood order and Line Console #2 started) the three `solid 50x10x30`
strips rode to the Panel Former, the chain closed and the former formed the hood. The **finished hood** went out of the former into
Long Piece Rack #3, which has no conveyor on to the terminal, so the order reads `no route from the Long Piece Rack: collect it there`,
which is true. The verification read that line as the strips'. Strips are already off by default (phase 5). If the showcase should
deliver panels to the tray, its Panel Former's out conveyor needs a way to the Router: the Showcase's owner's.

## 4. "Too wide" before the order
`ConstructLogisticsPlan.TooWideRow`: the terminal's list row for a formed panel asks the sheet its order would ask for (strips as now);
if no Extruder on the network makes it the row is "can't make" with `too wide: sheet 1.5 m wide: the widest Extruder here makes
0.5 m. Weld Die Frames on the Extruder (its die reaches 6 m with them), or set the Panel Former to make strips (3 across)`. With strips
on the row goes back to `needs 10 steel sheet in the Panel Former`. Only in the list (it allocates; never in a step).

## 5. Rank saved
`WorkOrders.WriteSave`: a list's flags byte gains bit 1 (`SavedRanks`) and each line's rank comes first. An older save has no bit, so
its lines read 0 (Normal). Tried (fresh processes, `saveworld`/`loadworld`): a High terminal's Wheel line rank 4 was 0 after a load
before; now 4. A save from before this change still loads (rank 0). No `MIGRATING.md` line: nothing changes for a player.

## 6. `Missing` allocates only on a change
It fingerprints what the recipe lacks without text and gives a recent call's text back when the machine, recipe and lack are the
same (16 entries). `logi cost 400` with a starved Ammo Container order: 40 B a step before, 0 B after.

## 7. Tried now
- **Two terminals, one piece of stock** (one Gun Barrel in Piece Rack #286; orders from the Crafting Terminal, the Building Station and
  the Panel Designer in one frame, all Normal): the first placed claims it (`Fetching`, then `InTray`), the others queue at the
  Assembler. In the other order the Panel Designer claimed it (`Collect`: no route to it). No double claim. Rank decides nothing here.
- **Router cost with riders** (`logi steps N`, new: the conveyor step N times): 60 riders, 10 to 60 us a step, worst 0.32 ms; with every
  rack full and the Router holding a piece, 9 to 51 us, worst 0.78 ms; 0 B in a steady step. Steps that deliver into a Press or a
  former allocate (a part is added and the body rebuilt), once a piece.
- **A two-level Assembler order and a stock target on one machine:** not reachable on today's showcase. Smart Terminal's chain stops at
  Circuit Board (pressed since Press phase 5, no Circuit die fitted); only Assembler #442 makes Tyre Carcass and Gun Barrel, and
  Piece Rack #274 (the one a target can keep Gun Barrels in) is not on #442's network (`refill ... by nobody`). A Wheel's Tyre Carcass
  link plus that target were both open with no clash. Needs a scratch blueprint: not done.
- **Press stock** five times alone: 5 passes (14 to 20 s). Not found. Likely cause of the one failure: its client check waits 3 s of
  wall clock (`Await`) while each update moves at most 0.1 s of game time, so under heavy load (nine sections, other sessions' builds)
  the client may not have had the body's resend yet.

## Not verified
- A client's own order through its message (`logi send x@301 ...` reached no order in a two-process run; the host closed early that
  run, so it is unclear which), and the loose container's resting place.
- `CouldWait` with a rack's Takes filter refusing (the code path is shared with storage choice).

Files outside this round's three: `ConstructConveyor.cs` (`SetOff`; `RackCheck` lets a terminal's tray take a store part), `RouterScreen.cs` (the set-off Alarm),
`WorkOrders.cs` (the ranks in the save), `Program.WindowShotLogi.cs` (`logi stow`, `logi mix`, `logi steps`, rank in `logi chain`).
