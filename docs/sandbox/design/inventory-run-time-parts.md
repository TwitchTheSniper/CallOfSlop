# Inventories, shelves, trays and chutes carry run-time parts by key

Survey of 2026-11-13 (main at protocol 290), the plan to close the gaps, and the questions for the user. A run-time part is one a
machine makes with no part file (extrusions, wheels, fan and wing pieces, formed, lofted and cut panels, pressed parts and barrels,
ordnance designs, racks, parts of another material: `ConstructPartDef.RuntimeMade`). Its id (from `Extrusion.RuntimeBase`, 0x4000) is
this machine's own and is freed by `RuntimeParts` when nothing holds it, so on the wire and in a save it must travel by its key.

## The finding that decides the rest

**Every run-time part a machine makes is physics-only** (decision 21): an extrusion, wheel, fan, blade or wing piece is
`PhysicsWhy = "extruded"` (`Extrusion.Def`), a pressed part "pressed to size" (`Pressed.cs`), an ordnance design "built at a bench", a
part of another material "made of another material". So the hands, the backpack, a shelf's slots, a volume's or bin's kinds and the
counted inventory all refuse them by rule (`Give` drops one as a body, `SetIn` says "physics-only (extruded): it can't go in the Parts
Shelf", `PickUp` refuses it), and they live as bodies: on the ground, on a Piece Rack's slabs, a conveyor, a lifter, a machine's die or
bay, or in a cargo volume as a body. Those already go by key. The only run-time parts that can be items are those parsed through the
part rules (`ConstructParts.Parse`): a lofted part, formed or cut panel or rack that is light, not welded and not bulky.

So the holders are sound for what they hold today; what is missing is the choice to let small made parts be items (question 1), which
the hand-held guns from pressed parts need, and the gaps below, which bite the moment that changes (and today for a light rack or panel).

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
client sees the same part. "ok" means by key; a gap names what is wrong. "Refused" (physics-only) applies to every machine-made part;
the rest of the row is for a run-time part that is an item (a light rack or panel, or every made part once question 1 says so).

| Holder | In | Show | Out | Save | Client |
| --- | --- | --- | --- | --- | --- |
| Hands (`SandboxHand.Held`) | refused (physics-only); an item: ok | name is the raw key text on the tool card | ok | ok (names) | **raw id in the hand state** (`SandboxModule.WriteHand`): a client shows another part, or none. Fixed in phase 1 |
| Inventory counts (`SandboxInventory.Parts`) | **refused**: an array by catalogue id (`Add` returns false) | - | - | catalogue only (`ReadPlayer` drops ids past `MaxKinds`) | catalogue only |
| Backpack (a `CStore`) | refused (physics-only); an item: ok (`PackSlotFor`) | **no picture**: the icon atlas has catalogue slots only (`SandboxTiles.SlotOf`), a swatch is drawn | ok | ok | ok |
| Shelf, storage part slots | refused (physics-only); an item: ok (`SetIn`) | **look line shows the raw key text** (`Program.Sandbox.cs` store look); no picture | ok | ok | ok |
| Storage volume, bin kinds | refused (physics-only); an item: ok | same as a shelf | ok | ok | ok |
| Machine trays and inputs (Assembler, Crafting Terminal, Gun Workbench) | ok (stores) | raw key text | ok | ok | ok |
| Press, formers, Piece Rack | ok (body parts, rack slabs) | `Extrusion.Label` (logistics code) | ok | ok | ok |
| Beacon network, Smart Terminal | in: an item only (`ToNetwork` puts any non-physics part in a store) | **the tally indexed `BeaconView.Counts[id]` (4096 long) with a run-time id: an IndexOutOfRange on the server tick** (guarded in phase 1) | **not listed, so not taken out** | stores: ok | counts by catalogue id only |
| Taking from the network with a run-time part in the hands | - | - | **lost**: `FromNetwork` put the held part in the counted inventory, ignored the refusal and emptied the hands (fixed in phase 1: refused, it is given aside) | - | - |
| Chutes (`ConstructFlexChute`, links) | carry nothing themselves: a chute network moves parts between stores (above) | - | - | - | - |
| Loose piece (F, `PickUp`) | refused (physics-only: moved with the physics gun); an item: ok (`Give`) | look line: `Extrusion.Label` for an extrusion, raw key for the rest | ok | a body: ok | a body: ok |
| Lifters' hands (`ConstructLifters.Write`) | ok | - | ok | ok (names) | **raw `U16(l.Def)`**: the lifters session's file, reported to it |

Smaller notes: `GiveResult.Inventory` (the world full) loses a run-time part, since the counts refuse it; the Tab inventory's
categories (`PartCategories.cs`) know catalogue parts only; HUD notices (`Give`, `ToNetwork`) say the raw key text.

## The plan

- **Phase 1 (this branch, protocol 292).** One shared way to write and read "a part, catalogue or run-time": `PartRef.Write`/`Read`
  (`ConstructStorage.WriteDef`/`ReadDef` forward to it, so the logistics callers stay as they are), and `PartRef.Shown(def)`, the name a
  player reads (an extrusion's label, an ordnance design's description, else the name). The hand state carries the held part by key,
  with a per-hand cache on the reader so a hand that holds the same part allocates nothing. The beacon tally skips run-time parts instead
  of throwing, and taking from the network with full hands never loses the held part. The store look line and the tool card show
  `Shown`.
  Done (2026-11-13): `--window-shot ... --do "inv hold NAME; inv show; inv reload"` (`Program.WindowShotInv.cs`) put an extrusion
  and a pressed part into the hands (forced past the physics-only rule, as a test state), and a host and a joined client over RakNet
  printed the same part, by name, for both players.
The phases below were rewritten on 2026-11-13 for the user's answers (at the end of this file).

- **Phase 2: the counted inventory takes made parts** (answers 1 and 2, as read below; was phase 3). `SandboxInventory` keeps a
  short list of run-time kinds beside its array (def id and count, at most 64 kinds), so `Add`/`Take`/`Count` work for every part and
  salvage, `GiveResult.Inventory` and the refunds keep made parts. In the counted inventory (infinite parts off, not survival: the same
  rule as catalogue parts) a made part taken off a body with the remove tool, a loose made piece included, goes into the count instead
  of being cut free; picked in the Tab inventory it is set down in front of the player as a body (`SpawnInFront`, as a physics-only
  catalogue part is), never into the hands. Saved in the player record by name (the save already writes every part by name; an older
  build drops the run-time names, as it did), sent by key in `CBodyKind.Inventory`, marked by the `RuntimeParts` sweep (inventories
  are tracked like hands). The Tab inventory lists them in one "Made parts" category, sorted by kind then size, by their shown names.
  The pick goes to the server by key (protocol 298). Made parts stay physics-only: the hands, backpack, shelves and F still refuse them.
  Done (2026-11-13; `SandboxInventory.MadeDef`/`MadeCount`, `WorldPlayer.Made` by key, `CActKind.TakeMade`, `UiSandboxMade.cs`): a made
  part is always counted, creative too (it is never endless). `--window-shot ... --do "inv drop NAME; inv takeback NAME; inv count;
  inv setdown NAME"` (`Program.WindowShotInv.cs`) took a pressed bracket and an I-beam back into the count with the real remove edit,
  listed them under "Made parts" (beams before pressed parts, by their labels), kept both through `saveworld` and `loadworld` in a
  second process, and set them down as the same parts; over RakNet a joined client saw its own count by key and set its beam down by
  key. Not done: the Line Layer and Replacer don't pick made parts, a search doesn't find them, and the beacon network and refunds
  that go there (phase 3) still treat them as bodies.
- **Phase 3: the network and terminals** (answer 5). `BeaconView` counts run-time kinds in a list beside its array (sent by key); the
  Smart Terminal lists them with the rest, one row per key, and hands them out as bodies in front of the player; Inventory Beacon pulls
  and the network's refunds take them. Stores that refuse physics-only parts keep refusing, so a network holds a made part only where
  bodies are kept (a storage volume's bodies, a Piece Rack): this phase first counts what those hold.
- **Phase 4: pictures** (answer 4). Run-time icons in the atlas: a pool of 512 tiles past the blueprint and tool slots, each drawn once
  from the part's boxes when first shown (cached: no per-frame work) and given back when the part is swept or the pool is full (the
  longest unseen first). The Tab inventory, the terminal's rows and the store look line use them.
- **Dropped: made parts as hand items** (answer 1). The hand-held guns from pressed parts (sized-gun-engine-parts.md, answer 7) need
  another way: a gun finished at a bench as one item, or the user asked again.

## Questions for the user

1. **Every machine-made part is physics-only today. Should small ones be items?** a) Yes: a made part light enough to carry and not
   structural can be held, backpacked, shelved and picked up with F, by the part files' own rule (recommended: the hand-held guns
   from pressed parts need it, and a pressed bracket is a hand part); b) only pressed parts; c) no, they stay bodies (the physics gun,
   racks and conveyors).
2. **Should run-time parts go into the counted inventory (Tab), or only into the hands, backpack and stores?** a) Yes, up to 64
   run-time kinds a player (recommended: salvage and taking parts back then work the same for every part); b) no, they stay physical
   (a backpack slot or the ground).
3. **Where do they show in the Tab inventory?** a) one "Made parts" category, sorted by kind then size (recommended); b) each in the
   category of what it is (beams with beams, wheels with wheels), by rules in `Categories.json`.
4. **Pictures for run-time parts?** a) real icons drawn on demand, a pool of 512 tiles (recommended); b) a coloured swatch with the
   shape's letter, no rendering.
5. **A Smart Terminal and its network:** a) list run-time parts with the rest, one row per key (recommended); b) one row per shape,
   any size, the nearest size handed out.

## The user's answers (2026-11-13)

1. **c) No, they stay bodies.** Machine-made parts do not become hand-held items: the physics gun, racks and conveyors move them.
2. **a) Yes, into the counted inventory (Tab), up to 64 run-time kinds a player.**
3. **a) One "Made parts" category, sorted by kind then size.**
4. **a) Real icons drawn on demand, a pool of 512 tiles.**
5. **a) A Smart Terminal and its network list run-time parts with the rest, one row per key.**

**How answers 1 and 2 are read together** (the coordinator's reading, which the plan above builds on; the user can correct it): a made
part is never carried in the hands or a backpack slot as an item, but it can be counted in the Tab inventory the way catalogue parts
are: taken back from a body or salvaged into the count, set down or fitted from the count as a body (or a part of one), listed by
stores and terminals, saved and sent by key. Where nothing is counted abstractly (survival: physical storage) a made part is a body,
as every part is there.
