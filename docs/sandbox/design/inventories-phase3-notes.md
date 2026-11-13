# Inventories phase 3: made parts' pictures, a network's made parts, the Smart Terminal's rows

Notes of 2026-11-13 for `inventory-run-time-parts.md` (the design and the user's answers). This phase does that file's phases 3
(the network and terminals) and 4 (pictures). Protocol 303 (on its branch).

## Built

- **Pictures on demand** (`Source/Render/SandboxTiles.Made.cs`). A made part has no atlas slot (any key can be made), so a pool of
  512 tiles (`MadePool`, two atlas pages of their own, made only when used) is filled on demand: `SandboxTiles.TryGetPart(id)` gives a
  catalogue part's atlas tile or a made part's pooled tile, asking for it when it is missing; `Pump` draws at most 4 waiting tiles a
  frame (`MadePerFrame`, inside the atlas's 3 ms budget). A tile belongs to a def, not an id (an id freed by the sweep and made again for
  another key is drawn again); when the pool is full the slot longest unseen goes, never one seen this frame. Nothing is allocated once
  it is warm. Used by the Tab inventory's "Made parts" rows (a 40 px picture beside each row, a colour swatch until drawn) and the
  backpack strip. Options > Regenerate icons clears the pool with the atlas.
- **A network counts made parts** (`BeaconView`): a list of up to 64 made kinds beside the catalogue table (`MadeDef`/`MadeCount`,
  `CountOf(id)` for either), tracked by the `RuntimeParts` sweep. Sent by key at the end of `CBodyKind.Beacon` and `Terminal`: u8 0 when
  unchanged, else u8 1 (a whole list) or 2 (continued), u8 n, n x (PartRef, u16); a list past 12000 bytes goes on in the next message.
  The callers that indexed `Counts[id]` with any part (salvage counts, the build aids, the parts list) use `CountOf`.
- **Piece Racks on a network** (`RackStore`, before `SlotStore` in `ConstructBeacons.Kinds`): a Piece Rack chuted to a
  network counts its pieces by kind (catalogue pieces too: a beacon builds from a rack, as storage). A Lay-down Pad is a work point for the
  arms and lines, so it is left out (matched by name until it has a flag); so is a Crafting Terminal's, Building Station's or Panel
  Designer's hold. Nothing is put
  into a rack through the network.
- **Smart Terminal rows**: made parts are listed with the rest, one row per key, by `PartRef.Listed` ("Round 0.1 m, 0.7 m long,
  steel"). The use key asks by key (`CActKind.TerminalPull` flag 4, the PartRef after the flags); a made piece comes off the rack and is
  set down in front of the player as a body (it is never a tray or hand item: to the tray it is refused with that reason). A terminal
  store's pull goes material flag first, then a material id or a part by PartRef (it was a raw id: a client would have thrown the
  packet away for a made part).
- Window tool steps (`Program.WindowShotInv.cs`): `inv fill N TEMPLATE` ({i} {j} {k} {e} {t} {q} vary the key), `inv sum` (a digest
  to compare peers), `inv open`, `inv icons`, `inv force N STORE NAME` (a test state), `inv net`, `inv pull`.

## Tried by hand (Windows PC, real window, throwaway save dirs)

- Host alone: 64 kinds of cut plate and of cut loft panel in the count; the Made parts list drew 64 pictures, no swatches.
- Host and a joined client over RakNet (80 ms lag, 2% loss, a private port): 64 kinds of cut loft panel, 12928 bytes of made entries
  (so the inventory went in two messages); the client's count matched the host's (64 kinds, 127 parts, the same digest) and drew all
  64 pictures. With a throwaway blueprint (a Smart Terminal chuted to a Piece Rack, in the build output only, not committed) holding 5
  extruded rounds: the host's and the client's terminal both listed "Round 0.1 m, 0.7 m long, steel x5" (rows and the screen); a pull
  on the host and one from the client (by key) each took one off the rack and set it down as a body beside that player; the client's
  row went to x4.
- Sections passed by name: Sandbox inventory, Sandbox Tab inventory, Inventory beacon, Inventory beacon network, Construct storage,
  Construct salvage.
- The 64-kind send needed no fix: phase 2's split holds.

## Not tried

- The client's use key at the terminal itself: after `bring` its crosshair missed the screen, so the pull was sent by the tool's
  `inv pull` (the same call the key makes).
- A network made list past 12000 bytes (64 cut-panel kinds on racks): the same split code path as the inventory, not driven.
- The pool full (512 kinds on screen) and the swap of its longest-unseen tile; a panel still forming when first drawn (4 of 64 were drawn
  twice in one run, likely that).
- Refunds into the network, the Inventory Beacon's own pulls of made parts, the Line Layer and Replacer, a search for made parts.

## Open questions (recommendation first)

1. **Made rows are hard to tell apart** when the label is cut short ("Cut Panel (loft 300x200x100 t12 b-10,-.."): every cut panel of
   one blank reads the same. Recommend: a cut part's label names its blank and the count of openings ("Cut loft panel 3 x 2 m, 5
   openings"), the key in the tooltip.
2. **Storage Volumes holding loose bodies** are still not counted (no store keeps bodies today). Recommend: leave until a store does.
3. **Putting a made piece onto a rack through the network** (refunds, the Inventory Beacon's put): recommend no; the conveyors and
   lifters pack racks.
4. **Pictures on the terminal's screen**: it is a text panel; recommend leaving it text.

STATUS: built; tried on host and a joined client.

## After the merge review (2026-11-13)

- Past 64 made kinds a list leaves the rest out: it now says how many (u16 with a whole list), the terminal's screen shows "+N" on its
  title and the look line "N more made kinds not listed"; logged once a run.
- A pull under way keeps its part from the sweep: `RuntimeParts.MarkRoots` marks each terminal job's part and every store's
  `PullId`, so the store message never names a freed id.
- Lay-down Pads left out of networks (above).
- Sections passed by name: Inventory beacon, Inventory beacon network, Sandbox inventory, Sandbox Tab inventory, Construct storage,
  Custom bins, Extruder network, Construct salvage, Assembling, Display screens, Interaction sweep (two re-run alone after a port clash
  with another session's test).
