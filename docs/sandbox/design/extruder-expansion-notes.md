# Extruder expansion pieces: notes (2026-11-13)

Branch `claude/extruder-expansion-29fe07`, Windows PC. **Protocol 304 -> 310** (new part files move ids; a socket kind). Proposal and the user's
answer: smart-logistics-phase5-notes.md ("Proposal: Extruder expansion pieces", "Answers, 2026-11-13"). Code guide: production.md "Extrusion",
"Expansion pieces".

## Built

- **Die Extensions**: Medium (2 m a row, to 16 x 16 m) and Large (4 m, to 32 x 32 m). Welded to the machine's side sockets on the die face's
  row, flush with the die face; chained outward. Counted per side of the die (left, right, up, down), so the die's centre moves with them; capped
  at the machine's `growSection`. No Capital one: its 32 m die is already `Extrusion.MaxSectionCm`.
- **Run-out Tables**: Small (8 m, to 64 m), Medium (16 m, to 128 m), Large (16 m, to 192 m), on a new `outBed` socket under each die (new
  `SocketKind.RunOut`, class-matched), chained end to end; tops below the die's foot so the piece rides over them. No Capital one (192 m is
  `Extrusion.MaxLengthCm`).
- **One reader for the grown limits**: `ConstructExtruder.Reach` (die and longest piece in one walk of the joined pieces; `Die` and `MaxLength`
  call it). Readers moved onto it: the step's length cap, the look line, the machine's screen, `WorkOrders.Refusal` (Crafting Terminal, Panel
  Designer, bills), the picker's refusal and its die text, the planner's `WidestTaken`, `LongestTaken`, `TooWideText` (the Panel Former's strips rule).
- **Fit texts** in the order asked for: a piece on this machine, the bigger Extruder, then strips (sheets only). Seen:
  `a section 10 x 1 m is wider than the Medium Extruder's die (8 x 8 m): weld Medium Die Extensions flush with its die face (its die reaches 16 x 16 m with them)`;
  `a piece 50 m long is longer than the Extruder makes (48 m): add Run-out Tables to its out bed (to 64 m with them)`.
- Config ranges (Width, Height, Cut every) on the grown limits; categories: the pieces land in Production > Extrusion by the existing rule (no edit).
- Window tool verb `extr` (Program.WindowShotExtr.cs): a machine's die, length and an order's refusal as each peer has them; host `build`, `queue`, `shoot`.

## Costs chosen and why

Each piece: **600 steel, 150 rubber** plus parts (Gear Sets, Bearings, Wiring Looms, a Control Unit at tier 3), at its class's tier (Small 1,
Medium 2, Large 3). The Medium, Large and Capital Extruders all cost 2000 steel and 500 rubber (the Assembling cap, `MaxUnits`), so the gap in
material between classes is nothing and in tier is one step; "most of the gap" became **about 30% of the next machine's recipe each**: one row
or table is much cheaper than the next Extruder (and needs no higher-tier Assembler), the whole jump to its size on one axis costs 120% of it, a
square jump 240%. The Small's two tables (1200 steel) cost less than a Medium (2000), but the Medium also brings an 8 m die and twice the speed.
No power cost: a piece draws nothing (the proposal said "material and power"; a draw on a passive part needs the power code to count it).

## Tried by hand

Host and a joined client (`--net host:27688` / `join`, the `extr` verb): a Medium Extruder refused a 10 x 1 m order with the new text on both
peers; with a Medium Die Extension fitted (`fitpart ... -x.4.5`) both read die 10 x 8 m and "takes it"; the order printed (the client saw the
cut piece); shot off, both refused again. The same for a Run-out Table on the small Extruder (48 -> 56 m and back). Offline: a Large Die Extension
(+x) and Large Run-out Table (die 20 x 16 m, 144 m), a 30 x 30 cm piece growing over a small table (not blocked). Build; `--gen-parts all --check`;
`--check-construct-parts` 0 REFUSED; sections Extruder, Extruder network, Compound collision, Machine tier looks, Sandbox inventory, Sandbox parts
menu, Construct configure, Display screens, Interaction sweep: 0 failures.

## Not tried

- Fitting by hand with the welder's ghost (the window tool's `fitpart` goes through Place and Attach, the hand's own path, but not the ghost).
- A Medium or Large piece the full grown length over chained tables; a Die Extension on +y (over the hopper's front) or chained two deep.
- Saves: a world with pieces saved and loaded (part names, nothing new in the format). The conveyor `Piece out` port's opening is still the
  def's (`ConstructPorts`: a static port), so a conveyor under a grown die is as wide as the plain die.
- The Crafting Terminal's list row and the Panel Former's link with an expansion on the network (both read `WorkOrders.Refusal` / the planner, tried only through the verb).

## Open questions (recommendation first)

1. Should pieces draw power while the machine runs (the proposal's "and power")? **Recommend no** for now: the material and tier already steer
   the choice, and a passive part's draw needs the power step to learn about it.
2. Small Extruder: Die Frames stop at 6 m, the Medium's die is 8 m. Raise the frames' cap to 8 m so every class reaches the next? **Recommend yes**, a one-number change.
3. A Capital piece does nothing today (both of its limits are the game's). Leave the Capital without pieces? **Recommend yes**.
