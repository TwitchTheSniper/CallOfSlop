# Sub-blueprints: builder's notes (status)

Branch `claude/sub-blueprints`, protocol 324. Spec: `sub-blueprints.md` (the user's answers at its end win). What a player gets and
how it works: production.md "Building station", "Submodules".

## Done
- Phase 1, data (`SubBlueprints.cs`): ids, versions, history (`.versions`, last 8 and locked), `"uses"` with `"after"` (the parent
  step its steps follow: the build order the writer chose), `["uN", k]` ends, resolve (packed, `ship:`, library, history), flatten
  (spins worked out from the sub's own build, welds, links, lists, fuse units, overrides, per-step link exactness), guards and limits
  (8 deep, 128, 1,024), cache (64), packs, broken check cached in `.checks`. `--sub-blueprint NAME [--roundtrip] [--tree-size]`.
- Phase 2, tags and saves: `CPart.Use`, `Construct.Uses` through copy, split, merge, freeform weld, the Full (and so a Patch),
  the `USES` block. The writer (`ConstructClipUses.cs`, `ConstructClip.ToBlueprint`): highest intact use kept, changed ones
  dissolve into their inner ones, a selection of one use keeps its inner ones, overrides, square and grid rule, New version / New
  blueprint. Save window's Uses list; Blueprints tab's uses line, Live / Lock, red broken rows with a lock button, Pack. Selector
  look line "part of Pod #2 (v1, live)".
- Phases 3 and 4, the job tree (`ConstructStationTree.cs`): external use steps, dispatch to stations on the same construct
  (Sub-jobs Auto/Off, network shared, idle, bay fits; touching bays first, smallest, nearest), 30 s fallback, the big fit (check,
  repair of missing parts in the bay, extra parts refused, 5 s + 1 s/t up to 60 s, drawn over its first third, one merge, links,
  tags), Cancel use, root cancel, sub-job failure and loss (dispatched again, that station skipped), depth (a sub-job is a root of
  its own uses), resolve off the game thread, the panel's tree and the sub-station's header, the `SUBJ` block (pinned tree).
- Phase 5, partly: bays that touch hand over; a body set down within 8 m of the bay is taken (by hand, the physics gun, wheels).
- Phase 6, partly: save and load mid-tree, a joined client's tree, timings, docs (production.md, MIGRATING.md).

## Tried by hand (2-core box, window tool under xvfb)
- Ship (2 plain beams, two Pod uses, one turned) on a yard of two stations with touching bays: both Pods dispatched to the second
  station, handed over, welded; done in 45 s. Stations apart: freed, `subbp carry`, taken and welded; with `subbp cut` (a part
  off) the missing part ordered and fitted here. 30 s fallback: built here part by part. `station reload` mid-tree: rows, the
  freed body and its tags came back; it finished. A joined client saw the rows, the header and the merged body's tags.
- `--sub-blueprint Fleet --roundtrip` (two Ships, each two Pods, turned, a paint override): join check, round trips as a station
  build, a stamp and with a part off all build back every part in place; the override survives.
- Tree at the limits (8 deep, 1,005 uses, 2,606 steps): flattened in 1.5 s off the game thread; costing worst slice 1.4-2.6 ms
  (7-10 ms on the first run, JIT); the big fit of a 2,605-part use 2.5 ms warm (9 ms first). The box was loaded (load 8-13).
- Self-test sections run by name, all passed: Constructs, Construct storage, Sandbox body sync, Paste and packets, Save folder,
  Construct routed links, Construct configure, Freeform weld, Packets, Sandbox spawns, Interaction sweep, Sandbox Tab inventory,
  Sandbox inventory, Frame allocations, Construct placement.

## Not done / not verified
- Loader Arms and Bridge Cranes carrying a submodule (the lift word's body carry): not built.
- The selector's cyan frames and labels round uses: not built (the look line names them).
- Sub-jobs' craft orders carrying the root's rank and age stamp: not built (they queue as the sub-station's own).
- A shipped showcase blueprint with uses and a second station (part kit generator): not built.
- The stamp, the save window's Uses list, New version / New blueprint and the Blueprints tab's toggles and Pack were not driven
  through the real UI (the writer and reader were, by `--roundtrip`); no pictures were looked at, so the body's draw to its spot is
  not seen.
- Uses with ropes, springs, no-collide pairs or tracks in the selection are saved as plain parts; a wire added inside a use after
  its stamp is lost on save (a use's own links are its blueprint's).
- The save window's plan resolves each use's blueprint on the frame (cached after the first): a big sub-blueprint may hitch once.

## Merge review fixes (2026-11-16)
- Release early and a failure now end the tree too: sub-jobs still building cancel, finished and fitting bodies are freed as they
  stand (not held, not frozen); Cancel still takes them apart. A repeat sends its uses out again.
- A body's table is matched tag by tag (`SubBlueprints.EnsureTable`, `Construct.UsesIndex`): a world loaded mid-build tags its later
  parts into the loaded tags (tried: two uses, three parts each, both kept by the save plan after a reload mid-build).
- A loose body is taken only if it is near the bay (tested first, 8 bodies a look at most), not anchored, the station owner's or a
  team mate's, and not another root's sub-job body (`Construct.SubStamp`). A player's own stamped or hand-built use near the bay is
  taken on purpose (the design's "by hand" way).
- A mod's blueprint with uses skips the override's quick build check and is flattened and built with the shipped ones after they
  are all read, refused there with why.

## Leftovers (branch claude/sub-blueprints-2, 2026-11-16)
- Done: Loader Arm / Bridge Crane carry of a finished body (`ConstructLifterBodies.cs`, no wire change); sub-job orders at the main
  station's class and age; the selector's frames and labels (`UseFrameRender.cs`); the Submodule showcase (part kit generator
  `submodule_showcase`: pod, skiff, yard); the `bpui` window verb.
- Tried: the yard end to end (both pods built at the second station, carried by the arm, welded; 42 s). Through `bpui` and real keys:
  stamp Ship (placed with Fire, attached), the frame's label, select all, SelectSave opened the save window, its Uses list kept Ship
  as a use (Lock), saved; the Blueprints tab's list, Live toggle (v2), Pack. Found and fixed: a use attached by a socket read as off
  the 1 cm grid (float sums: now 1 mm); shipped sub-blueprints' bodies had no id (TagStamp takes the use's).
- Not tried: a Bridge Crane's carry; the frames seen in a picture (the label list was read); a joined client during an arm carry.
