# Smart logistics phase 5: notes (2026-11-13)

Branch `claude/logistics-5-e7fb99`, Windows PC. **Protocol 300 -> 304** (a new setting slot on Panel Formers). Code guide: production.md
"Smart logistics" (the chains bullet and the Logistics settings bullet). Earlier phases: smart-logistics-phase3-notes.md, phase4-notes.md.

## Built

1. **A panel too wide for the network's Extruders** (the user's answer to 4c question 2). A Panel Former's chained sheet that no Extruder
   on the network takes now waits, and its link and the order's note say what would make it (`ConstructLogisticsPlan.TooWideText`):
   Die Frames when an Extruder here could take it with them, else the smallest bigger Extruder in the catalogue, then the strips option
   with its count. Seen: `waiting on sheet 1.5 m wide: the widest Extruder here makes 0.5 m. Weld Die Frames on the Extruder (its die
   reaches 6 m with them), or set the Panel Former to make strips (3 across)`. Strips are the Panel Former's new setting **"Too wide:
   make strips"** (`formerStrips`, Logistics group, off by default; not on the Laser Cutter). A waiting sheet link follows the setting
   and the network as they change (`Rekey`: on turning strips on, the 1.5 m link became 3 x `solid 50x10x40` asked of the Extruder).
   Note: the small Extruder's **Die Frames are already expansion pieces** for its section (to 6 m); see the proposal below.
   **The Router's name**: already set only in the configure tool's Name box since 4c; the Router's screen has only Spill and Hold. Nothing to change.
2. **Two terminals on one network.** Tried on a scratch Showcase with a second Crafting Terminal on the Router's free way 2 and the
   orders console (not committed). Found: a High terminal's order queued at the shared Assembler went *behind* a Normal terminal's
   lines (rank decided who claims stock and who is served first, not the maker's queue: design 7.3, left over from phase 3). Fixed:
   a newly queued line goes ahead of the first unstarted lower-rank line after the head (`Ranked`; `WorkOrder.Rank`, server only).
   Seen on the host and on a joined client (its order through its own message, `logi send`): queue `GB 0/1 (T1, head), GB 0/1 (T2
   High), GB 0/2 (T1)`, the client's copy of the queue the same. Stock: first order placed claims it (Place claims at once), as before.
   Also fixed: a chain link's reason names the refusal of the machine of its kind (`waiting on Gear Set: the Press holds a Bracket Die:
   this order needs a Gear Die`), not the Assembler's `no recipe makes the Gear Set`.
3. **Cost** (scratch big network: 19 machines and 20 stores on one terminal, 4 terminals, 13 orders, 4 chain links (3 blocked), a
   stock target; window tool `logi cost N`, the real 2 Hz step run N times):

   | | before | after |
   | --- | --- | --- |
   | planner step, mean | 70 us | 27 us |
   | planner step, worst of 400 | 232 us | 199 us |
   | allocated a step | 6,084 B | 189 B |
   | network board list fill (16 rows) | 21 us, 0 B | 19 us, 0 B |

   The allocation was blocked links asking every machine every 0.5 s (each refusal is text). Now a blocked link is asked again only
   when its network's fingerprint changes (`Seen`: links, rebuilds, each machine's queue, bin and settings) or after a minute; the
   order note built from a link is made only when its numbers change. Left: ~125 B a step from `ConstructCrafting.Missing` ("needs
   1 Gear Set", a queued order's note every fifth step) and the minute fallback; both outside this round's files or amortised.

Checks: build; `--check-construct-parts` 0 REFUSED; self-test by name `Construct configure, Display screens, Wheel orders, Press stock,
Interaction sweep`: 0 failures. Files outside the logistics set: `ConstructConfig.cs` (CfgKey.FormerStrips at the end, its fit line),
`StockTargets.cs` (the setting), `WorkOrders.cs` (`Rank` field), `ConstructCrafting.cs` (one `Ranked` call), `ServerBrowser.cs`,
`Program.WindowShotLogi.cs` (`logi send`, `logi order @N`, `logi cost`).

## Not tried
- A joined client setting `formerStrips` in the configure tool (the setting goes the standard configure path, which marks the body
  dirty; the client saw the host's change take effect).
- Changing `feedPriority` after lines are queued does not re-rank them. Two terminals in the same rank tick competing for one fresh
  piece of stock (Place claims at order time, so first pressed wins).
- The Router pass per tick with many riders (0 riders in the measured state); the plan map on the big network.
- The pieces riding end to end on the big network (its Assemblers were idle for want of the showcase's own state).

## Proposal: Extruder expansion pieces
The small Extruder already grows by **Die Frames** (die to 6 x 6 m); the Medium, Large and Capital machines are their die and take none.
Proposed, for the bigger machines and for length:
- **Die Extension** blocks (one per size class) welded to a big Extruder's die face add one cell row or column of section, up to the
  next class's die; each costs most of the gap in material and power, so building the next machine stays the cheaper road for big jumps.
- **Run-out Table** sections on the out bed add `maxLength` (e.g. +8 m each on the small one, up to its class's next step), as
  `ExtMaxLength` reads its welded tables the way `ConstructExtruder.Die` reads frames.
- `TooWideText` then names them ("weld a Die Extension on the Large Extruder") before the next machine and before strips.

## Open questions (recommendation first)
1. Re-rank queued lines when a terminal's priority changes? **Recommend no** (it moves only new lines; simpler, predictable).
2. Should the Crafting Terminal's list row warn "too wide" before ordering (today the note says it right after)? **Recommend yes**, next phase.
3. The fallback retry of a blocked link is a minute: keep, or retry only on a seen change? **Recommend keep** (a safety net).

## After the merge review (2026-11-13)
- `Ranked` returns where it put the line and `AheadOfUsers` moves that line (it took the queue's last, which could be another order).
  Tried: a stock refill queued, then a terminal's Wheel on the same Assembler: `[Caster Wheel (head), Wheel, Gun Barrel refill]`. The
  review's case (an input queued at the machine of the line that uses it) needs a depth-2 chain, which the showcase can't make without
  scratch recipes: not tried by hand.
- `Rank` is not saved: the queue's save is `WorkOrders.WriteSave`'s format (another area's), and changing it would break old worlds'
  queues. A chain line's rank comes back with its link at load (`Mark`); a terminal's own line reads Normal after a load.
- `tools/MIGRATING.md`: strips are now off by default in loaded worlds.
- Router name: on main the name is only in the configure tool (`UiSandboxConfig.cs` "Name" box). The Router's screen has only Spill and Hold
  (part files and `PanelTouch`); F on a Router doesn't open a text box (`Program.SandboxControls.cs`); no wheel entry names it.
- A joined client set `formerStrips` 1 with the configure tool in hand (window tool `logi cfg`): the host logged it and both peers read 1.
- Sections by name (Construct configure, Assembling, Assembling network, Extruder, Extruder network, Press stock, Wheel orders, Display
  screens, Interaction sweep): 0 failures; one earlier run failed Press stock "part-way: the client never saw the piece", which passed alone
  twice and in the next full run of the nine (a timing flake, not looked into).

## Answers, 2026-11-13

- **Extruder expansion pieces:** build as proposed (the user): Die Extension blocks for width up to the next class, Run-out Table sections for length, on every class; the too-wide text names them before the next machine and before strips.
- Standing by recommendation: a priority change does not re-rank queued lines; the row warns "too wide" before ordering; the one-minute retry stays.
