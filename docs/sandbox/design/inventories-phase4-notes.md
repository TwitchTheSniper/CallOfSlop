# Inventories phase 4: the design's leftovers

Notes of 2026-11-13 for `inventory-run-time-parts.md` (the design and the user's answers) and `inventories-phase3-notes.md`. Protocol 307
(on its branch; main was 304).

## Built

- **Beacon pulls of made parts.** A made piece on a Piece Rack chuted to a working Inventory Beacon is listed in the Tab inventory's
  "Made parts" with the player's own count ("x3, 4 on the network", or "4 on the network"); a click takes one out of the count first,
  else off the rack, set down in front of the player as a body (`ConstructEdits.TakePart` no longer skips made parts on a network;
  `CActKind.TakeMade` accepts a key the player's network lists). Made parts stay bodies: they never go to the hands (`Refill` refuses them).
- **Refunds onto racks.** A made part coming back to a player in reach of their beacon goes onto a Piece Rack on the network, one already
  holding its kind first, by the conveyors' own packing (`RackStore.PutPart`, `ConstructWorld.RackPut`); slots and volumes refuse made parts
  through the network (they are never slot items). Paths: the remove tool in survival, a selection's delete refund and a paste that came up
  short (`ConstructEditsSelect`), the Line Layer's and Replacer's undo (`ReturnFromAid` -> `ReturnMade`: rack, else the count, else a body).
  A catalogue part still never goes onto a rack through the network.
- **Line Layer and Replacer by kind.** Both take made parts: picked from the "Made parts" rows (or the aimed part, MMB) with a mode on, laid or
  fitted out of the count, then a rack on the network, then a loose piece in range (`TakeMade`); the Replacer's picker lists the made parts the
  player has that meet the part's connections. The stroke and the swap name their part by `PartRef` (protocol 307), so a client's made part
  goes by key. The Line Layer's old refusal of extrusions is gone. The picked part is kept from the sweep (`RuntimeParts.Pinned`).
- **Search finds made parts**: a search in the Parts tab lists the made rows whose shown name or key has every word, after the catalogue's.
- **Made rows' labels** (the notes' question 1): a cut part reads "Cut lofted panel 300 x 200 cm, steel, 3 openings, 3 corners" (openings and
  corners counted apart; a plate "Cut plate 200 x 20 x 300 cm, steel, ..."); every row's tooltip ends with its key. Replacer and terminal
  notices use the listed name (an extrusion's length shows).
- **Tiles drawn twice (4 of 64), found and fixed**: the catalogue atlas is remade once after start-up (its key changes) and that also cleared the
  made-part pool, whose pages are its own; a remake now keeps it. A made tile also follows its part's boxes (a panel's facets swapped in after
  its first part are drawn again, the old picture shown meanwhile) and is not drawn from a panel's stand-in.
- Window tool steps (`Program.WindowShotInv.cs`): `inv rack N RACK NAME`, `inv rackforce N TEMPLATE`, `inv made`, `inv madesum`, `inv search`,
  `inv hits`, `inv mode`, `inv aidpick`, `inv line N PART NAME`, `inv replace PART NAME`, `inv tiles FROM N TEMPLATE`, `inv survival on|off`
  (a test state on each peer), `inv players N` and `inv beacon` (wait on the other window); `inv takeback` finishes survival's timed remove.
  A joined window now waits 180 s to reach play (two windows loading take 60-90 s here).

## Tried by hand (Windows PC, real window, throwaway save dirs, a throwaway blueprint in the build output only: a beacon and a Smart Terminal chuted to a Piece Rack)

- Host, survival: 5 + 2 I-beams on the rack; the Made parts rows listed them "on the network"; a pick set one down (rack 5 -> 4); the remove
  tool on that body put it back on the rack (4 -> 5, "into the network (Piece Rack)").
- Host, creative: Line Layer laid 2 counted beams from a Foundation (3rd didn't fit), the count went 3 -> 1; the Replacer swapped a 1 m beam
  for a 2 m one, the 2 m left the count and the 1 m came back to it; a search found the made rows with pictures.
- Host and joined client over RakNet (80 ms lag): the client's Made rows listed the network's beams by key and its pick took one off the rack
  (x5 -> x4, a body by it); the client's Line Layer and Replacer worked by key (the count by key followed); **the client's own use key at
  the Smart Terminal** (`aim Smart Terminal; use`) pulled a made beam off the rack as a body (rack 5 -> 4).
- **A network made list past 12000 bytes**: 64 cut loft panel kinds on a rack, 12032 bytes of made entries; the client's list had all 64 with
  the host's digest and drew 64 pictures.
- **The pool full and its swap**: 600 made parts asked for in batches: 512 in use, 88 handed on, then drawn.
- **Drawn twice**: 64 cut panels now draw 64 tiles (was 68).
- Sections passed by name: Inventory beacon, Inventory beacon network, Sandbox inventory, Sandbox Tab inventory, Construct storage, Custom bins,
  Construct salvage, Interaction sweep, Extruder network, Display screens, Sandbox snap build, Assembling network.

## Not tried

- The Line Layer and Replacer in survival with a real station and a made part off a rack (creative only: the aids are free there; the take
  goes through the same `TakeMade`).
- A selection's delete refund and a short paste with made parts (the code paths, not driven); a Mirror Table's image of a made part.
- Rows with real formed panels mid-forming on screen (the stand-in wait counted 0 in these runs: the keys finished before they were drawn).
- More than 512 made parts on screen in one frame thrashes the pool (each asks the slot of one not yet asked); the Tab shows at most 128 made
  rows (64 counted, 64 on the network), so play never gets there.

## Open questions (recommendation first)

1. **Cut rows of one blank and one opening count still read alike** (64 rows "Cut lofted panel 300 x 200 cm, steel, 3 openings, 3 corners"):
   the key is in the tooltip. Recommend: leave it until a player names them (a design's name from the Panel Designer, when the cutter
   carries it on the part); else add the first opening's place ("at 1.1 x 0.6 m").
2. **A made part removed in survival near a beacon now goes onto a rack, not on the ground.** Recommend keeping it (catalogue parts already go to
   the network there); the alternative is racks only for refunds.
3. **The joined client's spawn in the window tool** sometimes never came (a joined player not alive in 180 s, about half the runs, with or
   without loss): not this branch's code; worth a look by whoever owns joining.

STATUS: built; tried on host and a joined client; named sections pass.

## After the merge review (2026-11-13)

- A selection's delete with more than `MaxDrops` kept parts no longer keeps a made part in the left body as well as refunding it (the kept
  rule is the refund list's). Tried: survival, beacon in reach, 40 wrecked parts and two made beams deleted: the beams went onto the rack,
  the 40 stayed as one body with no beam.
- A paste's survival check (`ClipHave`) counts a made part as the paying path takes it: the count, a rack on the network, loose pieces.
  Tried: two made beams in a clip: missing with none, "all there" with two on the rack, one short then all there with one and two counted.
- The Line Layer's HUD number counts a made part the same way (creative included: made parts are never endless).
- Tool steps: `inv select delete|need BROKEN KEEP PART`, `inv bodies`, `inv line NxM ...` (a field).
