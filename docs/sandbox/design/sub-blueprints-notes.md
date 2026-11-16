# Sub-blueprints: builder's notes (status)

Branch `claude/sub-blueprints`. Spec: `sub-blueprints.md` (the user's answers at its end win).

## Done
- Phase 1 data: `Source/Constructs/SubBlueprints.cs`. `"id"`, `"version"`, `"uses"` (with `"after"`: the parent step its steps
  follow in the build order), `"packed"`, `["uN", k]` step ends in steps' on/also, wires, hoses, links. Resolve (packed, shipped
  `ship:<name>`, the library by id, `.versions/<id>/<v>.json` history), flatten (turns, places, spins worked out from the sub's own
  build, welds, links, lists, fuse units, overrides), loop and depth guards, limits (depth 8, 128 a blueprint, 1,024 a tree),
  cache (64), packs (`Pack`, `Unpack` of `NAME.pack.json`), broken check cached in `.checks`. Station, stamp (client ghost and
  server, uploaded packed) and shipped list read the flat form.
- `--sub-blueprint NAME [--tree-size]`: the tree, and the join check (each use built alone and merged at its place = flat build).
  Measured (2-core box): a tree at the limits (depth 8, 1,005 uses, 2,606 flat steps) flattens in 1.5 s off the game thread;
  costing 89 slices, worst 1.41 ms (first run 7 ms with the JIT).
- Tags: `CPart.Use`, `Construct.Uses` (copied on copy and split, merged on merge and freeform weld), in the body's Full (protocol 324).

## Next
Phase 2 rest (save block, writer of uses, save window, Blueprints tab), 3-6.
