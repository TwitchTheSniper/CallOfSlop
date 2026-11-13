# Press rework phase 4: orders, bills, the terminal (2026-11-12)

Branch `claude/press-phase4-1c5df1`, Windows PC. Protocol 268 -> 269. Code guide: production.md "A press's orders".

## Done
- Work orders carry a pressed part by its key (named shape 255, as a panel's) or a preset by its catalogue part; a Press takes them
  (`WorkOrders.PressRefusal`), works its head order (pattern from the order, bays take only its blank) and counts what it makes.
- Crafting Terminal: a press's presets listed with now / chain / later; pressed parts in stock listed by key; a press's lacks in the row.
- Chains: an order at a press asks a linked Extruder for its blank and a maker for a part kit; both ride in and count.
- Bills: `Bill.Pressed`, blanks into `Pieces`, `Kits`, the `"pressed"` blueprint bill line, presets dealt to presses, `--bill` group.
- Wire and saves: pressed orders in work orders, crafting orders and `CraftNamed`; chain lines' kinds by key (a run-time blank broke a
  client's whole crafting packet before: it was sent as a u16 id); chain marks found again after a load for piece and pressed lines.

## Verified by hand (throwaway saves, Production Showcase)
- `logi order Pressed Part (bracket.l l-angle 20x20x30 metal) 2`: placed; the chain asked Extruder#250 for 2 blanks; a blank rode tagged
  toward the press; a wrong blank (20x20x40) refused "Order"; the right blank taken and pressed (1/2); the part landed in the press's rack
  and was claimed for the order ("collect it there": that rack has no route to the tray in the showcase's layout).
- Save and load (two processes) with the order pending at 1/3: the press queue, the terminal row and the chain link came back, and the
  press went on with the next blank.
- Host and a joined client: the client sees the press queue by key, the Extruder's blank line, the terminal row with its chain line, and
  the bay card naming the order's blank.
- `--bill` of a scratch blueprint with two pressed steps and a `"pressed"` bill line; an Assembler two-level chain (Engine) unchanged.

## Not verified
- A whole run through the showcase's conveyors end to end (too slow to watch there, and its splitter sends every other blank to line B
  by its layout): the blank's ride was seen to start and the press stroke was fed by `feed`, the conveyor's own rule.
- A console bill Sent to a press (the showcase's orders console isn't wired to its press); a part kit chained to an Assembler; a preset
  ordered by its catalogue part at a terminal; survival timing and power.

## Choices made (questions for the user)
1. **The order does not change the press's Pattern setting** (design 7 says the order sets it): the head order's pattern is used while it
   is the head, and the setting is left alone, so the press is back on the player's own pattern when its orders are done. Should the
   order write the setting instead?
2. **A press's queue goes when it runs out** (a press has no Clear button, unlike an Extruder). Should a press get Queue/Clear buttons?
3. **A blank fed by hand doesn't shorten the chain's ask**: the Extruder still makes the blanks it was asked for; the extra ones wait at
   the press's door, and once its queue is done the press presses them on its own Pattern. Count hand-fed blanks against the chain?
4. **The terminal lists presets only**, not "to size" parts (as the design says); a player orders a sized part through a blueprint's
   bill or a station. Should the terminal get a size picker for a die's patterns?
5. **A preset whose catalogue part still has an Assembler recipe** goes to whichever linked machine takes it first (Assembler or Press).

## Remaining (not in this phase)
- Picker tiles for patterns and presets drawn from run-time defs (the Recipe button from phase 3b already sets the Pattern).
- The press's own queue on its screen (`WorkOrders.FillQueue`), and a Cancel/Clear touch on a press.
