# smart-logistics-2 (agent status)
Branch feat/smart-logistics-2 from origin/main 1ac7d570, worktree /home/claude/wt/docs. Protocol 266.
## Plan
Leftovers: L1 dests Assembler Piece in + Panel Former (press bays deferred: no wants until press orders; stay out of press files); L2 stall argument; L3 order row overflow reason; L4 bill piece to rack w/ room; L5 red lamp pictures host+client; L6 zero alloc probe; L7 whole tick p99/worst.
Phase 2 one level: Assembler implicit Piece in + TakeFromConveyor; chain entries (terminal order -> Assembler M -> part need -> other maker M2), WorkOrder.Chain, wants by chain, cancel, explanation "nothing on the network makes X"; showcase second Assembler.
## Done
## Findings
## Done (verified)
- Chain one level: Clutch order -> Assembler#284 queued, Gear Set asked of Assembler#442 (chain 1), rides into Piece in, Clutch to tray. Cancel removes entry. No maker: "waiting on Gear Set: nothing on the network makes it". Save mid-chain + load 2nd process: 3/3, nothing asked twice.
- L4 bill piece -> storage Piece Rack. L5 red lamps seen host+client (fix: ConstructRenderBatch.StaticBoxes excluded Routers). L6 0 bytes over 28 decisions. L7 4599 ticks median 0.073 p99 0.85 worst 12.4 ms (box shared with other builds); decisions ~36 us each.
## Next: remove LogiProbe + PROBE lines, docs, checks, merge main, push
## FINAL: merged main 5aee93b5 (265) -> ours 266. Checks all 0. Pushed.
## Plan for the rest of phase 2 (not built)
- Deeper chains: run Asked() for chain entries' makers too (root = entry), depth guard 6, cycle guard by walking Parent ids (add Parent to ChainEntry).
- Stock first for chained inputs: reserve free stock (ConstructReserve, owner = Assembler store handle, id = entry id) and fetch by PutMade(dest Assembler, tag entry) when map Reaches.
- Priority class then age between roots; stock targets last (phase 3 with targets).
- Building Station as a root (StationJob placing craft orders already: call Asked from its path).
- Orders tab indented chain lines (UI/UiCrafting.cs) + CBodyKind.Logi summary for clients.
- Press bays as destinations once Press orders exist; Panel Former sheet needs.
- Router decision cost ~36 us mean (storage candidates force columns, rider scans): bucket riding counts per dest if it matters.
