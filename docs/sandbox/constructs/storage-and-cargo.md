# Storage and cargo

Covers storage volumes, physical storage, modular shelves, the Smart Terminal, the Inventory Beacon. Part of the construct guide: index in ../CONSTRUCTS.md.

## Storage volumes

A construct's own interior as storage. The **Storage Volume** (`Constructs/Parts/Storage/StorageVolume.json`, attach tool, 14 kg, 400 health) is a small hopper with a box of space on its construct that you size yourself. It is one custom-sized store for **bulk or parts**: one bulk material, as a bin holds, or stored parts by kind, never both at once.

- **Why a part of its own, not a mode of the Cargo Volume.** A cargo volume is a logic part: its signal freezes bodies and it has no store. Everything a bin does (the store, chutes, F, the look line, spilling, mass) hangs off `PartRole.Storage` and `CPart.Store`, so a storage part with the cargo volume's six box settings gets all of that, and the box comes along with the sizing tool for nothing. One part with two modes would have needed a store on a logic part and a per-part switch at every one of those places.
- **Sizing: the cargo volume's own tool.** With the configure tool out, every box near you shows a handle on each face: hold Fire on one and drag (10 cm grid, the opposite face stays); R on the hopper then Fire-drag draws a new box corner to corner; Fire on the hopper opens the panel, where size and centre are numbers (`sizeX/Y/Z` 0.2 to 40 m, `atX/Y/Z` up to 40 m from the part). While you drag, the look line says what that box would hold. A big box is set from wherever its face is: the server measures your reach to the box, not to the part. Handles only show within the tool's 8 m, so walk to a far face.
- **What counts: the whole box as you size it** (`ConstructStoreVolume.Usable`; the developer, 2026-11-02). A box isn't clipped to the construct, and its fill draws to that size. The box and fill don't collide, so walls are the player's choice, at a price: [Sealed volumes](power-and-links.md#sealed-volumes).
- **Set by hand in creative.** The configure tool's `material` and `fill` settings put that much of a material in the box, up to what its open faces leave. They are creative only, as a Fluid Tank's: the server refuses them in survival and the panel shows them read-only. They follow the store four times a second (`ConstructStoreVolume.Follow`), so the panel, a blueprint and a paste say what is in it; a box of parts refuses them ("take them out first"). A survival blueprint or paste arrives empty.
- **Capacity.** Bulk: `binUnitsPerCubicMetre` in `Constructs/Recycling.json` (400 a cubic metre, so 60 m3 is 24,000 units, and iron ore is 1.6 t a cubic metre), at most 10,000,000 units a box. Parts: each takes its bounding box (at least a litre) out of the usable box times `itemPackShare` (0.7: the gaps), at most 256 kinds and 60,000 of a kind. Every part that is an item goes in, however many: physics-only parts are refused as for any storage (a cargo volume carries them), and so is storage itself.
- **Not solid.** The box and its fill collide with nothing: you walk through, bodies pass, and a shot passes through the fill. Only a player's look ray meets the fill (`Raycast(..., look: true)`), so F works at the level wherever you stand, and a stray round can't land on a hopper 30 m away. Reach is to the box. The walls are your panels.
- **Its fill** rises along the part's up from the usable box's floor: the material's colour, or a crate colour for parts, a box with three lumps on top, four draws at any size. Parts are not drawn one by one: a level and the look line's count stand for them ("Storage Volume: 34 parts, 3 kinds, 62% full: Lamp x10, Engine Mount x2... - F: take a Lamp"). Bulk: "Storage Volume: 1200/24000 iron ore - F: take 100".
- **Using it.** F tips in the materials you carry, or takes a load of 100; with parts in it F takes one of the kind put in last into your hands (the same rules as a slot: full hands go to the backpack, or it's refused). Holding a part, a click at the hopper or the fill puts it in, and the look line says why when it won't go.
- **Chutes.** Two named ports on top of the hopper: **Chute in** (green) and **Chute out** (orange), one way each, both in the linking tool's picker. In a blueprint a chute names them with `"toPort": "Chute in"`, `"fromPort": "Chute out"`; a chute that names none takes the only port that way. A chute moves a load of 160 bulk (the Storage Volume's `chuteLoad`, like a Plasma machine) or one part a second (`ConstructRoutes.ChuteMove`): parts come from slots or another volume, and go on into slots or a volume, and what won't go stays at the source. Named chute ports work on any storage part: a part with `"type": "chute"` ports has them instead of its plain one.
- **Mass.** A load counts at the fill's centre through the usual path (`Construct.LoadOf`, `LoadStep` at 4 Hz, "Bin loads"); stored parts weigh their own mass, unlike parts in slots. `LoadKey` is a double, so one unit off a huge load still shows. 10,000,000 units of gold ore is 80,000 t: a buggy's wheels bottom out and it stays put, with every number finite.
- **Limits.** 4 Storage Volumes a construct. A box may not grow into another on its segment ("that box runs into another Storage Volume"; a fresh part's default box may start overlapping one, and only growing the overlap is refused). A box too small for what it holds is refused and undone ("Empty it first").
- **When the construct changes.** The store moves with the part through a split. Capacity follows the bounds, so a split or a lost wall can leave it **over-full**: nothing is lost, the load keeps its weight, it draws full, and nothing more goes in until it is under again. The attach tool refuses to take off a Storage Volume that holds anything ("empty the Storage Volume first"). The remover still deletes it (and spills). A destroyed one spills up to 24 Material Bundles, or loose parts, in the box; the rest is lost and logged.
- **Survival.** It costs what its part costs, whatever the box's size; the box itself is free. Anyone may take from it, as from any store, and the contested rule (decision 19) holds for F, a click and the configure tool.
- **Blueprints.** The box is its settings, so a step's `"config"` holds it and `ConstructConfig.ToJson` writes it; `ConstructRoutes.LinksJson` writes the ports. Bulk contents ride in `material` and `fill`, in creative only; stored parts aren't in blueprints. The **Storage Bay** blueprint is the example.
- **Network (protocol 116).** Two more bits in a store's flags byte: 8, the amount's top u16 follows (a store under 65,536 costs what it did); 16, parts follow (u16 kinds, then u16 part and u16 count: 4 bytes a kind, 1,032 bytes at 256). `CBody` already carries settings and port bytes. Bad values are refused.
- **Performance.** The cost is a box clip per look at the store; a kinds list of at most 256, walked on a change. The arrays are made once, the first time parts go in, and stay with the pooled store. The steady path (chutes, the load step, fill and fit checks) allocates nothing.
- **The Requisition Crate** (`Constructs/Parts/Storage/RequisitionCrate.json`, `"requisition": true`) is a Storage Volume with a faction's courier at the end of it: the same box, settings, chutes and limits, counted among the construct's four. A story faction's Supply job is filled in one (docs/story/STORY.md, "Jobs"): parts or bulk in its box, and cut pieces lying in it (an end in the box will do). When it holds the lot the courier takes exactly what was asked and the rest stays.
- **Reading any store** (for things that list or fetch across stores): `ConstructWorld.Holdings(c, part, span)` writes one `CHolding(IsPart, Id, Count)` per kind held, for slots, bins and volumes, on either side; `ConstructWorld.TakeHolding(c, part, what, count)` takes up to N of it on the server and returns how many came out. Reach and the contested rule are the caller's.

Code: `Source/Constructs/ConstructStoreVolume.cs`, `ConstructCargo.cs` (the box), `Source/App/Program.SandboxCargo.cs` (the handles), `Source/Render/ConstructRenderCargo.cs`. **Self-test**: "Custom bins" and "Custom bins network" (`Source/App/SelfTestCustomBin.cs`).

Choices made (the developer may change them): a separate part, not a cargo volume mode; 400 units a cubic metre against a Materials Bin's 2,200, since loose bulk packs less tightly than a bin's rated load; 40 m and 10,000,000 units; 160 a second a chute however big the box, so filling 24,000 by one chute takes about 2.5 minutes; over-full rather than spilling the excess; the hopper is the one weak point: destroy it and nearly all of a huge load is lost; parts drawn as a level; the kind put in last comes out first by F and by chute; no pick of a kind at the unit itself.

## Physical storage (decision 18)

The developer's decision 18 (2026-10-29, "Inventories"; board item dev-sandbox-physical-storage-ps02): creative keeps the
inventory window (above); survival keeps things physical. Storage parts are construct parts with **slots** you set parts
into in the world, and bins that hold bulk material with a visible fill level. There is no storage screen: the only UI is
the look-at tooltip. It works on every Sandbox map (the parts are parts), so the yard's creative players can use it too.

### The parts (`Constructs/Parts/Storage`)

- **Chest** (attach tool): two medium slots inside an open steel box. The **Chest** blueprint adds a **Lid Hinge** (a long,
  flat motorised hinge, `power` 0) on its back edge and a **Chest Lid** on that: F on the lid opens and shuts it. Shut,
  the lid is in the way: nobody sees into the chest or takes anything out (the look ray meets the lid).
- **Parts Shelf** (attach tool): two big slots below (an engine fits), three **parts bins** above, each piling up to six of
  one small part (a pile of lamps, of buttons...).
- **Wheel Rack** (attach tool): three slots that take only wheels, standing upright (a slot's shape).
- **Materials Bin** (attach tool): an open bin of 600 units of **one bulk material at a time** (any; the first deposit sets it), its level rising in the material's colour.
- **Recycler** (attach tool): see [Salvage haul](gathering-and-salvage.md#salvage-haul-decision-31).
- **Storage Volume** (attach tool): a box of interior space you size yourself, up to 40 m a side, for one bulk material or stored parts: see [Storage volumes](#storage-volumes).
- **Shelf Bays** (attach tool): modular racks that snap together: see [Modular shelves](#modular-shelves).
- **Material Bundle**: what a broken bin spills, a wrecked part of 100 units of the bin's material (the Recycler gives them back).
- Blueprint **Storage Corner**: a foundation with a shelf and a bin on it (anchored).

More storage (crates, tool and gun racks, pegboards, cargo beds) is data: a part with `"role": "storage"` and a
`"storage"` block.

```json
"storage": {
  "slots": [
    { "min": [-0.405, 0.045, -0.255], "max": [-0.005, 0.545, 0.255] },
    { "min": [-0.51, 0.06, -0.42], "max": [-0.19, 0.88, 0.42], "fits": ["wheel"], "label": "wheel" },
    { "min": [-0.56, 0.64, -0.26], "max": [-0.2, 1.16, 0.26], "stack": 6, "label": "parts bin" }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `slots` | Up to 12 boxes in the part's frame (`min`/`max`, or `size` and `at`), at least 5 cm a side, inside the part, clear of its solid boxes and of each other: the room a stored part takes. |
| `fits` | Optional: only parts of these roles (`wheel`, `engine`, `gun`...): a slot of a **shape**. Without it, any part whose box fits. |
| `stack` | 1 to 12: how many of one kind pile up in it (a bin of small loose items). |
| `rack` | `true`: a [shelf](#modular-shelves) slot: it takes as many of one kind as really fit it, at most 48, in rows and layers. No `stack` with it. |
| `label` | What the tooltip calls the slot ("wheel", "parts bin"); without it the **size class** from its longest side: small (up to 35 cm), medium (up to 70 cm), large. |
| `bulk` | Instead of slots: `material` (`any`, or one of the recycling table's materials), `capacity` (up to 60,000) and the fill box (`min`, `max`) its contents rise in. Or `{"material": "any", "volume": true}` and the six box settings in `config`: a [Storage Volume](#storage-volumes). |

A part **fits** a slot when its box goes inside the slot's in one of its 24 turns (upright turns are tried first, no
turn first); it sits centred on the slot's floor in that turn. A pile stacks its copies up as far as the slot's height
allows (at least one drawn; the count is in the tooltip). A bad block is refused with the reason
(`--check-construct-parts`).

### Using it

- **Putting a part away**: carry it (from the rack, the parts menu, picked up, taken off with a tool), look at a storage
  part within reach (3.2 m): every slot it fits lights up as a **ghost outline** (blue), the one nearest the crosshair
  green with the part shown where it would sit. **Click** sets it in, at once, with any hands (a tool out or not). A part
  carried in both hands no longer fires the gun.
- **Taking it out**: look at a stored part and press **F**: it comes into your (empty) hands. Anyone may: stored parts
  can be stolen. A pile gives one at a time.
- **A bin**: F tips in the materials you carry (the bin's own, or the first it takes; as much as fits); with none to tip, F takes a load of 100 out into your carried materials.
- **The tooltip** (the look line): "Chest: 1/2 slots used (1 part), 2 medium slots - click a lit slot to set the Lamp
  in"; "Lamp x4 (6 fit) in the Parts Shelf, 100% - F to take one"; "Materials Bin: 230/600 metal - F: tip in your 40 metal".
- **Damage**: a stored part is hit like any part (bullets, construct guns, blasts: a blast on the container hits what is
  in it); its armour counts, and at zero health it is destroyed. A container that goes (broken off, taken off, wrecked)
  **spills** what it held where it was, as loose parts anyone may pick up (a pile as many as there were, while the caps
  on bodies allow; the rest is lost and logged), and a bin's contents as up to six Material Bundles of its material. A loose container still
  holding something can't be picked up ("empty the Chest first").
- **Survival salvage**: on a map with `"infiniteParts": false` and `"partsMenu": false` (survival) nothing is counted
  abstractly: a part salvaged (or taken off your own construct) comes into your **hands**; when they are full it goes
  into the backpack if it fits there, else is set down where it came off, ready to be carried to storage. A wrecked part comes off as a wrecked-part body (hauled to a Recycler). Creative maps keep the counted inventory as before.

- **Giving a part to a player** (board item rc01): one rule, `ConstructEdits.Give` (ConstructGive.cs), for every path that
  hands a part over (taking a part off, salvage into the hands, receiver-merge pop-offs): the hands if empty, else the
  backpack (the Stow rules: a free slot it fits, at most 40 kg), else a loose pickup dropped where it was (it falls and
  rests); what the hands hold stays held, and the player gets a HUD line ("Engine dropped: hands full"). Taking from a
  storage slot or picking up a loose part with full hands goes into the backpack when it fits, else is refused (the
  part stays put); drawing from the backpack with full hands is refused. A hands state naming no loaded part is logged
  and emptied each tick, and an edit that throws is logged and dropped, never a crash.

### Server, network and state (protocol 85)

- Each storage part holds a handle (`CPart.Store`) into the world's pool of stores (`ConstructWorld.Stores`, 1,024 of them,
  allocated once): per slot the stored part's type, its turn, how many and the top one's health; a bin's amount. The
  handle moves with the part when constructs split or renumber, so nothing is copied.
- The server decides every change (`CActKind.StoreIn`, `TakeStored`, `Bin`; checked again: reach, the hands, the fit).
  Two players taking the same part: the first request the server handles wins; the other is told "that slot is empty".
- A construct's structure lists its storage parts' handles; **`CBodyKind.Store`** sends one container's contents when
  they change (four bytes a slot, or two for a bin), after the structures in the same tick; a joiner gets every live
  store. Bad values are refused.
- `ConstructStorage.Write` / `Read` are the state's bytes (the same as the wire's): a world save, when there is one, will
  write them as they are. Nothing is saved yet (constructs reset with the match).
- Drawing: each stored part's boxes are drawn with its container (its segment's pose: a store on a turntable turns with
  it), a bin's fill as a box with lumps on top; nothing allocates per frame or per tick (the fit check the look runs each
  frame returns a code, not text; the tooltip is rebuilt only when what it shows changes).

### Self-test and pictures

The **Construct storage** section (`Source/App/SelfTestConstructStorage.cs`): the storage parts load with their slots and
ten kinds of bad block are refused for their reason; what fits (a suspension in a chest slot, not an engine; an engine on
the shelf; a wheel in the rack, not a suspension; six lamps pile, the seventh and a button refused); the state's bytes
read back the same (a truncated or unknown one refused); in an offline game on the yard the shut lid hides the chest's
slot and F opens it, a suspension set in is stored, sits inside its slot and a ray finds it there, an engine is refused
and stays in the hands, a taken slot is refused, taking out returns it to the hands, two players taking one part: the
first wins, a stored lamp shot to pieces goes (the chest untouched), the shelf piles six lamps and refuses a button, a
loose stocked shelf can't be picked up, the bin takes 120 metal and gives 100 back with its level rising, a destroyed
chest spills its suspension and battery as loose parts and frees its store, a broken bin spills six bundles, and in
survival the gate's door salvaged comes into the hands; no allocation in 120 ticks of stocked storage, rays into the
slots, fit checks, takes and sets and the store's message; and a real host with two clients: a client's part goes in,
both clients see the contents, both grab the same part and exactly one gets it, and a late joiner sees the contents.

### The backpack

The developer's answer on carrying (2026-10-30): the hands plus a visible backpack of about
eight slots of different sizes, still no inventory screen. Every player in survival wears one (`ConstructEdits.Packs`, Source/Constructs/ConstructBackpack.cs). It isn't an item you pick up: it comes with the mode. **Creative has none** (the user, 2026-11-05):

- **Creative** means endless parts (`SandboxInfiniteParts`), the same answer on every peer. No pack is drawn, no slot strip shows, and nothing goes in: Stow and Draw say "no backpack in creative". With full hands, `Give` skips the pack and drops the part as a loose pickup; taking from a slot or picking up a loose part is refused and it stays put.
- **An old creative save** that holds parts in a pack keeps them: the player wears that pack (`ConstructEdits.WearsPack`) and draws from it until it is empty, and then it is gone. Nothing new goes in.
- The **Backpack part** in the Tab inventory stays a storage part to bolt on a construct. It isn't worn.
- **Story mode starts without one** (Karl, 2026-11-08): the warden's induction hands it over at its backpack step (docs/sandbox/TUTORIAL.md). Until then the player's pack has no slots (`HasPack` false, `ConstructEdits.IssuePack`), so it behaves as creative's does, and Stow says it hasn't been given yet.

In survival:

- **The layout is data**: the **Backpack** storage part (`Constructs/Parts/Storage/Backpack.json`): a pack frame with
  four small pouches, a big bag, two side pouches and a hip pouch, and `"maxMass": 40` (a storage block's new optional
  limit: no part heavier than that goes in). A part goes in when its box fits a slot (as for any storage) and it weighs
  at most 40 kg: a lamp, a battery, a suspension, a gun barrel do; an engine is too heavy, a wheel too big, and storage
  parts never go inside storage. As a part, the backpack also works as storage bolted onto a construct.
- **Keys** (rebindable; binds version 11, given to older files when the key is free): **Stow** (H) puts the part in
  your hands into the smallest free slot it fits ("the Engine is too heavy for the backpack (120 kg; it takes 40)", "the
  Wheel is too big for the backpack (carry it in your hands)", "no free backpack slot takes the Battery"); **Draw** (J)
  takes the next part out into empty hands, round the slots from the last one drawn. Not while seated. The hands still
  carry one part, as before.
- **Seen**: on every player's back in third person (the pack frame and the parts in their slots; `BackpackRender`,
  the immediate path and the scene graph's `PackNode` emit the same boxes), and in first person as a strip of the slot
  outlines at the bottom of the screen (`UiSandboxPack.cs`), each square sized by its class with its letter (S, M) and the
  part in it as a tiny 3D tile from the inventory's atlas (a colour swatch until the tile is made). The HUD line says
  "Backpack 2/8: Suspension, Lamp (H: stow, J: draw)" while it holds anything.
- **Server and network**: the server decides (`CActKind.Stow`, `CActKind.Draw`); a changed pack goes to every client
  (`CBodyKind.Pack`: the player id and the store's bytes, the same format as storage) and a joiner gets every pack that
  holds anything. Packs empty with a new match.
- **Death**: the pack's contents drop round the body as loose parts anyone may pick up (the hands' part goes back as
  before); a player who leaves drops theirs too. Chosen over keeping them: survival stays physical, and what a player
  carried can be looted.

The **Sandbox backpack** section (`Source/App/SelfTestSandboxBackpack.cs`): the layout (eight slots, three kinds, a weight
limit) and the keys (unshared, migrated from binds version 10); in an offline game a lamp stows into a small slot and a
suspension into the big bag, an engine is refused as too heavy and a wheel as too big (both stay in the hands), drawing
is refused with full hands, takes both parts out in turn, then says the pack is empty; the pack is drawn behind the
player with its parts, the HUD line and the eight-slot strip show (headless, no ImGui error, nothing allocated per
frame); the pack's message and drawing allocate nothing; a death drops both parts near the body and empties it; and a
real host with two clients: a client's stow reaches the server, its own copy, the other client and a late joiner.

### Choices made (the developer may change them)

- **Carrying**: one part in the hands plus the backpack above (eight slots, 40 kg a part); no belt yet.
- **The backpack** drops its contents on death (rather than keeping them); stowing picks the smallest slot that fits;
  drawing goes round the slots; keys H and J.
- Storage works on every Sandbox map, creative too (alongside the inventory window).
- Setting in and taking out are **instant** (no tool, no timer); clicking with any hands.
- Fitting is by the part's **box** (it must physically fit in some turn), plus an optional role filter for shapes;
  smaller parts fit bigger slots.
- The chest lid is a **motorised** hinge (a passive hinge's latch can't hold a lid up against gravity); like other
  motors it needs power on a map that requires it.
- **Survival salvage** goes to the hands (or is set down when they are full); carried materials count on the player, and bins
  store them (F tips in, or takes 100 out).
- Stored parts add **no mass** to their construct yet, and keep only the top one's health in a pile (a part taken out
  comes out whole, as the hands carry no health).
- A spilled pile becomes as many loose parts as the caps allow; a broken bin's contents become at most six Material Bundles of 100.
- A loose container holding anything can't be picked up; a construct that is cleared away (debris expiry) takes its
  contents with it.

## Modular shelves

The user's "a modular shelf so the player can stack and store loose parts". Parts: `Constructs/Parts/Shelves`, written by `tools/constructs/shelf_parts.py` (with the Smart Terminal and the **Smart Depot** blueprint). Recipes in `tools/constructs/assembling.py`, tier 1, planks and a little steel.

- **The family.** A **Shelf Bay** is a steel cabinet (two sides, a back) with wooden boards at the bottom, the top and between its tiers, open at the front, on its class's lattice, with sockets on every face but the front, so bays stack up, run along and stand back to back, and bolt to any frame of their class.

| Part | Class | Size (wide x high x deep) | Shelf slots |
| --- | --- | --- | --- |
| Shelf Bay 0.5 | small | 0.5 x 0.5 x 0.5 m | 1 |
| Shelf Bay 1 | small | 1 x 0.5 x 0.5 m | 2 |
| Tall Shelf Bay 1 | small | 1 x 1 x 0.5 m | 4 (2 tiers) |
| Medium Shelf Bay 1 | medium | 1 x 1 x 1 m | 1 |
| Medium Shelf Bay 2 | medium | 2 x 1 x 1 m | 2 |
| Medium Shelf Bay 4 | medium | 4 x 2 x 1 m | 8 (2 tiers) |

- **How a part is held: in a slot, frozen to the shelf.** A part on a shelf is not a body. It is a count in the store, drawn where it stands (`"rack": true` on a slot; `ConstructStorage.PileShape`, `StackOf`, `ItemOrigin`). So a shelf on a truck that rolls over keeps its stack, a full wall of shelves costs the physics nothing, and every store rule works as it does (chutes, beacons, the look line, spilling, saves, the wire). The price: a stack doesn't topple, and a shot hits the pile, not one piece in it.
- **A visible stack.** A shelf slot takes as many of one kind as fit, from the part's box: rows across the floor, the back row first, then layers up, in the turn that packs most (at most 48 a slot). A Lamp, 20 cm a side, stands 8 to a Shelf Bay 1 slot. The ghost shows where the next one goes. One kind a slot: "that shelf holds Lamps", "that shelf is full (8)".
- **Setting a part on.** Click a lit slot, as on any storage part; or let go of a loose part over a shelf, by a drop from the hands (RMB) or the physics gun's release: within 0.8 m above a board it goes into the nearest slot that takes it (`ConstructEdits.Shelve`, run on the release, never per tick). What a shelf won't take (physics-only pieces, a part too big, a slot taken) stays a body and comes to rest on the board as on any floor.
- **Chutes and beacons.** A bay is a store: a chute feeds it and takes from it, and a beacon or terminal counts it.
- **Cost at scale.** A look or a shot meets a pile as one box (`ConstructStorage.PileBox`), and past 30 m a pile is drawn as one box too (`ConstructRender.FarPile`). Stores went from 128 to 1,024 handles, as a wall of bays is a store each (the look is what walks them, and chutes; nothing else scans the pool per tick).

Choices made (the developer may change them): slots, not bodies on a board; small and medium only (items are at most 40 kg, so a Large bay's 8 m floor would hold a person's parts out of reach, and physics-only pieces don't go on shelves); one kind a slot; no mass for what stands on a shelf, as for all storage; a pile keeps only its top part's health; a Medium bay's 4 m size for pallets of small parts; let go within 0.8 m.

## The Smart Terminal

The user's "some way to interact with a chute system in an area, so a smart inventory system could pull an item from connected inventory volumes". Code: `Source/Constructs/ConstructTerminal.cs` (the view, the rules, the networks' side, the server's pulls, `Shelve`), `Source/Game/GameSandboxTerminal.cs` (views per player), `Source/App/Program.SandboxTerminal.cs` (the keys and the list).

- **The part** (`Constructs/Parts/Shelves/SmartTerminal.json`, attach tool, physics-only, 70 kg, 60 W): a cabinet with a lit screen over a tray, a storage part with `"terminal": true` and its tray as a `rack` slot. It needs power in survival, draws only while on, takes a **Run** wire, and stays in place wrecked, with what is on its tray. Its lamp lights while it works.
- **Two chute ports.** **Network** (both ways) reads: any intact chute on it, either way round, joins the terminal to a chute network, and it moves nothing. **Chute out** (out only) carries what was pulled away, and what it feeds is not on the network by that chute. A chute into a terminal brings nothing in. So stores may hang off it in a star (link from Network to each), or in a chain that runs away from it (Network to shelf, shelf to volume), and nothing drains out of them. In a blueprint: `"fromPort": "Chute out"`; a chute that names none takes Network.
- **The network** is the beacons' (`ConstructBeacons.Graph`): a terminal is a node of the same union-find, so it reaches junctions, world links and cargo hoses, and stops at a cut. Its stores are the same kinds (`INetworkStore`), with the same contested rule and owner rule (yours, or nobody's). Its tray isn't a store of the network. It doesn't need a beacon, and works in creative and survival: it moves real stock either way.
- **The list, on its screen.** No window and no cursor (the user prefers no menus). The terminal has a [display panel](joints-and-logic.md#display-panels) (`"source": "terminal"`, six lines, drawn out to 16 m): stand within 4.6 m and it shows the network's list: `Item 3/12`, four rows round the picked one, materials first, then parts by name, each with its count at the right edge and the pick lit and marked `>`, and a last line for what it is doing: `Lamp 2s +1` (seconds until it lands, pulls waiting behind it), `100 metal waiting`, `Tray: Lamp x2` or `Ready`. Look at it within reach: the wheel moves the pick, and the look line reads `Smart Terminal: 3 stores, Lamp x8 - Wheel: pick - F: to hands - R: to chute`. Off, the screen says why; further off than 4.6 m it shows its stores and parts. A wrecked terminal's screen goes dark.
- **Each viewer has their own screen.** A panel is drawn on each peer for that peer's player, and the list goes to one player only, so `ConstructTerminals.FillPanel` reads the local player's `TerminalView` (`ConstructWorld.LocalTerminal`, which `Game` sets) when it is about this terminal. Two players at one terminal each see their own list position and pick on the same screen, and neither moves the other's. The pick lives in `TerminalView` on that peer (`Picked`, `Step`) and is never on the wire. What a pull is doing rides in the terminal's store, so every peer's screen shows that. Anyone not at it sees the totals, from the store too.
- **The data side stands apart.** `TerminalView` holds the terminal's handle, its stock (a `BeaconView`), `Rows`, made again only when the stock changes, and the pick; a row says what it is (`TerminalRow`: part or material, id, name, count). `Program.SandboxTerminal.cs` is only the keys.
- **A pull** (`CActKind.TerminalPull`, `ConstructEdits.TerminalPull`; the server decides). It finds the store nearest by chutes that holds the item (`NearestStore`: hops relax over the chutes the graph kept, on the request, never per tick) and waits a chute move per hop, one second each, so a part two hops off takes two seconds; a terminal works through its pulls in turn, at most 8 waiting. Then it takes the item from the real store, so nothing is ever in flight to save or lose: a pull under way is simply forgotten by a save. If that store has lost it, the next nearest serves; if none has, "no Lamp left on the network".
  - **F: to the hands**: `Give`'s rules (hands, backpack, ground) if you are still within 8 m; further off a part lands on the tray.
  - **R: to the chute**: a part lands on the tray in its pile, where a chute on Chute out takes it on, or F takes it, as from any slots. Nothing can be set on a tray. A part too big for it is refused ("pull it to your hands"), and a full tray leaves the part where it was.
  - **Materials**: F fills what you carry up to a load (100). R needs a chute on Chute out: up to a load waits in the terminal for the chute (`CStore.Amount`), then goes as from a bin.
- **When it is worked out.** Never per tick. The graph runs on a change, the counts when a store changed, at most at 10 Hz (`ConstructBeacons.Step`), with beacons or terminals in the world. A player's list is made at 4 Hz, only while they stand within 4.6 m of a terminal, and only if something changed; it walks only that network's stores. All of it is fixed arrays: up to 64 terminals, 64 networks, 32 pulls under way, 2,048 chutes kept for hops (past that a store counts as one hop). Nothing allocates in a steady tick; the rows are made on a change, on the peer that shows them.
- **Network (protocol 142).** `CBodyKind.Terminal`, to its player alone, only on a change: u16 the terminal's store handle (0: none), then a beacon message's bytes (`ConstructBeacons.Write`: bounded, in turns; it is the same delta). The look line's numbers ride in the store, as a beacon's do. A store's second flags byte gains 8 (protocol 149): what a terminal is pulling (u16 id, u8 is-a-material, u8 pulls under way, u8 seconds), sent when one starts, ends or a second goes. `CActKind.TerminalPull`: u16 body, u16 part, u16 id, u8 flags (1: to the hands, 2: the id is a material).
- **Saved and copied.** A terminal, its ports and chutes, and what is on its tray save with the world; shelves save their piles. A blueprint carries the parts and chutes, Chute out by name, and comes empty, as every store does. A joiner hears the stores; a joiner's list goes whole the first time they stand at a terminal.

Choices made (the developer may change them):

- A list of four rows at a time on a six-line card, cut to the screen's 18 characters a row (the look line has the full name); the keys wheel, F and R, not a click, so any tool can be out.
- Each viewer's own pick on their own screen; a pull's state is shown to everyone.
- A pull reads through a chute either way round, as a beacon does, and against the way it runs: the wait is the chutes between, not their direction.
- A pull takes a chute move per hop however long the chutes are; one at a time per terminal; 100 a pull for materials, as F on a bin.
- A pull's item stays in its store until it arrives, so another player or a chute can take it first; the player hears it.
- No filter on Network: a terminal reads every store on its network. Connecting a chute to someone else's store exposes it, unless they stand defending it, as with a beacon.
- One terminal shows its list only to players standing at it, and one list at a time, the nearest.

## The Inventory Beacon

A build aid for survival, and the deliberate exception to decision 18: the developer asked for a beacon that, "when connected to
a chute network, will broadcast all the available items so the player can access them as if they were using creative mode, but in
survival", and that takes parts back to the network rather than the hands. Code: `Source/Constructs/ConstructBeacons.cs` (the table, the
networks, the views, the message), `ConstructEditsBeacon.cs` (the server's takes and returns), `Source/Game/GameSandboxBeacon.cs` (views per
player, the HUD), `Source/UI/UiSandboxBeacon.cs` (the window).

- **The part** (`Constructs/Parts/Storage/InventoryBeacon.json`, attach tool, physics-only, 90 kg, 120 W): a cabinet with a screen, a mast with
  three cross arms, an aerial and a lamp that lights green while it broadcasts. It is a storage part with `"storage": { "beacon": "inventory" }`
  and no room of its own: that gives it a store handle, a chute port and the machines' flags, and chutes moving items ignore it.
  It needs power in survival (`ConstructRecycler.Powered`), draws only while on, and takes a **Run** wire (`ConstructGatherer.HeldOff`). A
  wrecked one stays in place (`"onBreak": "disable"`) and says so. The **Beacon Depot** blueprint: a Foundation, a beacon, a Battery, a Chest, a
  Parts Shelf and a Materials Bin, with a chute from the beacon to each.
- **The table** (`Constructs/Beacons.json`, format `call-of-slop-beacons/1`; `BeaconTable`): `id` and `rangeMetres` (1 to 1000; the
  beacon's part to the player's eye). The shipped `inventory` beacon reaches 60 m. A bad file is refused with the reason
  (`--check-construct-parts`) and the built-in table is used. A part naming an unknown id is refused.
- **The network.** Every intact chute joins its two ends, whichever way it runs (its `FedEnd`: a chute cut anywhere before its end joins
  nothing, so a branch beyond a cut goes too), through junctions (a branch runs from the root's source) and world links. It's
  a union-find over store handles (`ConstructBeacons.Graph`). Hoses carry fluid, power and data, not items, so they join nothing.
  A Gun Workbench isn't a store here (its slots are a gun being built); a machine's output is, for taking only; its input never.
  Two beacons on one network share it; every network in reach merges; each store counts once.
- **When it is worked out.** Never per tick. `Construct.StoreNetKey` hashes a body's stores and intact chutes at each rebuild, and the
  world's loop compares one integer per body; a store freed or a world link changed does the same (`ConstructBeacons.Dirty`). The
  graph then runs at the next 10 Hz step, or at once for an action. `Stock` goes up on each change to a store, and the counts run then.
  A player's view is made at 4 Hz, only if the `Epoch`, what they reach or their state changed. All of it is fixed arrays, at most 64
  beacons (more are logged and do nothing). Nothing allocates in a steady tick.
- **Who.** A player's own beacon, or nobody's (the map's, or one whose owner left). A [faction](overview-and-playing.md#factions) mate's serves you as
  your own does (decision 19). Someone else's serves nobody else; a store on the network that is defended (the contested rule) is skipped.
- **Survival only.** With `"infiniteParts": true` nothing goes through a network; the look line still gives its numbers. On a counted map (the
  menu on, parts not infinite) the window is the usual one and the badges add the network's count; the inventory is used first.
- **The window.** Tab opens the same inventory when `Game.SandboxInventoryAllowed`. On a survival map it shows Parts, Materials and Tools:
  the Parts tab lists what the network holds with its counts, grouped as creative groups it; "Hide what it doesn't hold" unticked greys the rest.
  Out of range the window closes. Tab with no network says the beacon's reason, or the map's rule with no beacon near.
- **Taking** (`CActKind.Rack`, as creative's click). The server walks the real stores and takes one from the first that has it (`ConstructEdits.FromNetwork`):
  what you hold goes back first, then it comes into your hands by `Give`. A physics-only piece arrives in front of you (decision 21;
  storage takes none, so only a part that became physics-only while stored can be there). Both players asking for the last one: the first the
  server handles has it, the other hears "no Lamp left on the network". A row: after a fit or a set-down the next comes off the network (`Refill`).
  **Materials** (`CActKind.BeaconMaterial`): fills what you carry of that material up to a load (100), from the bins in turn.
- **Coming back** (`ConstructEdits.ToNetwork`): a part taken off or salvaged, what you held when you took another (the menu, the rack, the pick and
  the recent wheel), a merged receiver's pop-offs and a Gun Workbench's returns that its own slots can't hold go into a store on the network: one
  already holding that kind first, then any free slot it fits, by the slot rules. No room or no fit: the usual path, with "no room on the network
  for the Lamp: it comes to you". The HUD says where it went: "Lamp into the network (Parts Shelf)". **Physics-only pieces stay as they are**: slots
  never take them, so they are cut free as bodies. Wrecked parts stay wrecked-part bodies. Picking up a loose part or taking from a chest by hand
  stays in the hands: those are asks for the part. The remover deletes outright, so nothing comes back.
  **Materials** the player is given (the ground drill's scoops: `MaterialToNetwork`) go to pure bins holding that material, then empty ones (never a
  machine's output); what doesn't fit stays carried.
- **The HUD and look line**: "Beacon: 34 stores, 512 parts, range 60 m" (with ", 230 material" when it has some); off, the reason: "Beacon off: no
  power (a charged battery, linked to it)", "Beacon off: its Run wire is off", "Beacon off: wrecked", "Beacon off: no stores on its chute network",
  "Beacon: that one is someone else's", and, within two ranges, "Beacon: out of range (70 m away, range 60 m)". Holding a part: "12 more on the network".
- **Smart Terminals** ([The Smart Terminal](#the-smart-terminal)) are nodes of the same graph, with their own list; a terminal's Chute out joins nothing, and a terminal's tray isn't a store here.
- **Adding a store kind.** `INetworkStore` is all the beacons know of a store: `Serves`, `Parts`, `Tally`, `Holds`, `TakePart`, `TakeBulk`, `PutPart`,
  `PutBulk`. Add one to `ConstructBeacons.Kinds`, before `SlotStore` if both would serve the part.
- **Network (protocol 116).** `CBodyKind.Beacon`, to its player alone, only on a change: flags (1: start empty), state, stores, parts, bulk, range,
  distance, then (part id, count) for up to 240 kinds that changed and the materials that changed: about 1,100 bytes at most, in one reliable
  packet. What doesn't fit goes next tick, from where it stopped; leaving a network sends the state alone. A joiner's starts empty. A store's flags byte gains 8: a
  beacon's stores and parts follow (u16 each), for the look line on any peer. `CActKind.BeaconMaterial` (u8 material).

**Self-test**: "Inventory beacon" and "Inventory beacon network" (`Source/App/SelfTestBeacon.cs`): the table and part; the depot on the real step: chutes, a junction, a
world link, two beacons, two networks; no graph or count run and no allocation over 240 steady ticks; range, ownership, power, Run, a wreck; the real take, held part
returned, a physics-only piece, the contested last one; bulk both ways; a row refilled; a part taken off into the network, a full network into the hands, a
attached piece a body; a cut chute; the message's bound; and a real host, two clients and a late joiner, with the window headless.

Choices made (the developer may change them):

- **Delivery is instant, by radio.** It costs nothing per item. A small power draw per item taken (say 1 kJ, about a second of a Smelter) would fit the machines, and make a
  big rebuild drain a battery; it isn't in, because the beacon already draws 120 W and it would need a refusal for "battery too flat for this take".
- The range (60 m), the draw (120 W) and the 64-beacon limit. Range is measured to the eye.
- A beacon nobody owns serves anyone. Connecting a chute to someone else's store exposes it to your beacon (unless they stand defending it).
- It reads through any chute, whichever way the chute runs, though the items themselves only move one way.
- A chute cut part way drops everything beyond the cut, even if the far side still touches a junction.
- Blueprints, Spawn and Builds aren't on the survival window. Physics-only parts can't be on a network, so structure still has to be hauled.
- A material take is a top-up to 100, not 100 more. Fill still pays from what is carried, not the network.
- Bots have no view. A dead player's view empties.
