# Inventories, shelves, trays and chutes carry run-time parts by key

Survey of 2026-11-13 (main at protocol 290), the plan to close the gaps, and the questions for the user. A run-time part is one a
machine makes with no part file (extrusions, wheels, fan and wing pieces, formed, lofted and cut panels, pressed parts and barrels,
ordnance designs, racks, parts of another material: `ConstructPartDef.RuntimeMade`). Its id (from `Extrusion.RuntimeBase`, 0x4000) is
this machine's own and is freed by `RuntimeParts` when nothing holds it, so on the wire and in a save it must travel by its key.

## What already carries the key

- **The wire, stores.** `ConstructStorage.WriteDef`/`ReadDef` (protocol 244) write a catalogue id as a U16, a run-time part as
  `Extrusion.WireMark` and its key (`Extrusion.Write`: section, round, pipe, wheel, fan, blade, wing as bytes; ordnance, lofted, pressed,
  cut, rack and other-material parts by their name). Every store slot, a volume store's or bin's kinds, the backpack (`CBodyKind.Pack`),
  the conveyors' riders (protocol 247), the Crafting Terminal's chains and tray (271) use it. Phase 1 moves it to `PartRef` (below).
- **The save.** Every part number in a world save goes through `WorldNames.Part`: the file lists each part once by its name, and a
  run-time part's name is its key (`ConstructParts.Find` parses it back: `Extrusion.Parse`, `Pressed.Parse`, `HullLoft.Parse`, ...).
  Store slots, kinds, the backpack, the hands (`WorldPlayer.Held`) and the lifters' pieces are saved this way.
- **Bodies** (`Construct` Full messages) write a part by key since the extrusions began; a piece on a Press, a former or a Piece Rack
  is a part of a body or a rack slab (`ConstructRackPack`, by `ExKey.Stable`).

## The table

In = it can be put in; Show = its real name and picture; Out = taken out as the same part; Save = saved and loaded; Client = a joined
client sees the same part. "ok" means by key; a gap names what is wrong.

| Holder | In | Show | Out | Save | Client |
| --- | --- | --- | --- | --- | --- |
| Hands (`SandboxHand.Held`) | ok (`Give`, take from a slot, rack, F pick-up) | name is the raw key text on the tool card | ok | ok (names) | **raw id in the hand state** (`SandboxModule.WriteHand`): a client shows another part, or none |
| Inventory counts (`SandboxInventory.Parts`) | **refused**: an array by catalogue id (`Add` returns false) | - | - | catalogue only (`ReadPlayer` drops ids past `MaxKinds`) | catalogue only |
| Backpack (a `CStore`) | ok (`PackSlotFor`) | **no picture**: the icon atlas has catalogue slots only (`SandboxTiles.SlotOf`), a swatch is drawn | ok | ok | ok |
| Shelf, storage part slots | ok (`SetIn`) | **look line shows the raw key text** (`Program.Sandbox.cs` store look); no picture | ok | ok | ok |
| Storage volume, bin kinds | ok | same as a shelf | ok | ok | ok |
| Machine trays and inputs (Assembler, Crafting Terminal, Gun Workbench) | ok (stores) | raw key text | ok | ok | ok |
| Press, formers, Piece Rack | ok (body parts, rack slabs) | `Extrusion.Label` (logistics code) | ok | ok | ok |
| Beacon network, Smart Terminal | in: ok (`ToNetwork` puts any non-physics part in a store) | **the tally indexes `BeaconView.Counts[id]` (4096 long) with a run-time id: an IndexOutOfRange on the server tick** | **not listed, so not taken out** | stores: ok | counts by catalogue id only |
| Taking from the network with a run-time part in the hands | - | - | **lost**: `FromNetwork` puts the held part in the counted inventory, ignores the refusal and empties the hands | - | - |
| Chutes (`ConstructFlexChute`, links) | carry nothing themselves: a chute network moves parts between stores (above) | - | - | - | - |
| Loose piece (F, `PickUp`) | ok (`Give`) | look line: `Extrusion.Label` for an extrusion, raw key for the rest | ok | a body: ok | a body: ok |
| Lifters' hands (`ConstructLifters.Write`) | ok | - | ok | ok (names) | **raw `U16(l.Def)`**: the lifters session's file, reported to it |

Smaller notes: `GiveResult.Inventory` (the world full) loses a run-time part, since the counts refuse it; the Tab inventory's
categories (`PartCategories.cs`) know catalogue parts only; HUD notices (`Give`, `ToNetwork`) say the raw key text.

## The plan

- **Phase 1 (this branch, protocol 294).** One shared way to write and read "a part, catalogue or run-time": `PartRef.Write`/`Read`
  (`ConstructStorage.WriteDef`/`ReadDef` forward to it, so the logistics callers stay as they are), and `PartRef.Shown(def)`, the name a
  player reads (an extrusion's label, an ordnance design's description, else the name). The hand state carries the held part by key,
  with a per-hand cache on the reader so a hand that holds the same part allocates nothing. The beacon tally skips run-time parts instead
  of throwing, and taking from the network with full hands never loses the held part. The store look line and the tool card show
  `Shown`.
- **Phase 2: the counted inventory takes run-time parts.** `SandboxInventory` gets a short list of run-time kinds beside its array (key
  def id and count, at most 64 kinds), saved by name in the player record (a new section so older builds skip it), sent by key in
  `CBodyKind.Inventory`, marked by the `RuntimeParts` sweep. `Add`/`Take`/`Count` work for both, so salvage, `ReturnHeld` and
  `GiveResult.Inventory` keep run-time parts. The Tab inventory lists them under one category (question 2).
- **Phase 3: the network and terminals.** `BeaconView` counts run-time kinds in a list beside its array (sent by key); the Smart
  Terminal lists and hands them out; Inventory Beacon pulls take them.
- **Phase 4: pictures.** Run-time icons in the atlas: a small pool of tiles past the blueprint and tool slots, drawn on demand from the
  part's boxes and given back when the part is swept (question 3). Then the hand-held guns from pressed parts (sized-gun-engine-parts.md,
  answer 7) can start.

## Questions for the user

1. **Should run-time parts go into the counted inventory (Tab), or only into the hands, backpack and stores?** a) Yes, up to 64
   run-time kinds a player (recommended: salvage and taking parts back then work the same for every part, and the hand-held guns need
   it); b) no, they stay physical (a backpack slot or the ground).
2. **Where do they show in the Tab inventory?** a) one "Made parts" category, sorted by kind then size (recommended); b) each in the
   category of what it is (beams with beams, wheels with wheels), by rules in `Categories.json`.
3. **Pictures for run-time parts?** a) real icons drawn on demand, a pool of 512 tiles (recommended); b) a coloured swatch with the
   shape's letter, no rendering.
4. **A Smart Terminal and its network:** a) list run-time parts with the rest, one row per key (recommended); b) one row per shape,
   any size, the nearest size handed out.
