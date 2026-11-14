# Panel Designer phase 3 (the loft): questions for the user

Phase 3 (feat/panel-designer-3, 2026-11-12). The plan in `docs/sandbox/constructs/production.md` ("Loft: the plan") left these open;
the simpler choice was taken each time. Say if you want the other.

1. **A readable key, not `Formed Panel (#HASH)`.** The plan expected a lofted panel's key not to fit the 128-character run-time name, and
   so proposed a hash key with the design's body stored once per world (a second list in `ConstructWorld.Designs`, section `DSGN`, sent
   before any body naming it, a `"designs"` block in blueprint files). With the heights in whole centimetres within 99 cm either way, the
   whole design fits: `Formed Panel (loft 200x120x100 t20 b0,8,13,15,15,13,8,0 f-6,0,4,6,6,4,0,-6 steel)` is 82 characters, the longest
   possible 102 and the material's name. So the loft travels and saves exactly as every other formed panel (wire, saves, blueprints,
   racks, conveyors, bills), with no store, no ordering problem for late joiners and no "hash with no body" case. The cost: heights
   are whole cm within -99 to 99, and a profile has exactly eight points. *Keep the readable key, or do you want taller profiles (the hash
   route)?*
2. **Ruled only.** The sheet between the two edges is ruled (straight lines from each back point to the nose point across from it), not
   eased. Eased (smoothstep) would need a word in the key and more facets along. *Wanted?*
3. **Eight evenly spaced heights an edge**, a Catmull-Rom curve through them; mirrored by default (a handle moves its twin). A design
   loaded from a list opens with the mirror off when it is lopsided.
4. **The nose has its own width** (the plan didn't say): the sheet tapers or flares straight between the two widths. The former must take
   the wider end.
5. **No crown or rake on a loft**: the profiles are the shape (one way to say a thing, one key per shape).
