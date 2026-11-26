# The ship designer

Karl (2026-11-23): an in-game ship designer, based on the Zero Day: choose hull segments and shape the hull as you like (the hull
pieces), put external pieces on it, set docking points, lay out the interior (decks, rooms, cargo holds), connect sockets such
as airlocks, docks and cargo hatches to the rooms inside, look round it in 3D, drag parts on and off, and cut open where two
hull bodies meet (an option a body). First as an Artifact in C#, then in the game.

**State.** Phase 1: the designer's model in C# (`Source/ShipDesigner`, built into the game and, unchanged, into the web page)
and the web page (`tools/ship-designer`, published as the Ship Designer Artifact, https://claude.ai/artifact/3EGCrZFDUpEqUAEvFVYHni).
Nothing in the game uses the model yet. Phase 2 is the designer in the game: the same model behind a screen of its own, and a
design turned into a blueprint (each band of each body a pair of Hull Sections, `HullLoft.Section.cs`, whose key takes the
same keel-to-spine half profiles; floors, walls and doors from the rooms; the pieces on their sockets).

## The model (`Source/ShipDesigner`)

Plain data, no game types, so it compiles to WebAssembly. Coordinates are the Zero Day generator's: x across (0 the
centreline), y up, z along the ship with the bow at the smallest z, metres.

- `ShipDesign`: hull bodies, pieces, decks and rooms; the design file (JSON, `"format": "CallOfSlop ship design"`).
- `HullBody`: a lofted, faceted body between stations, each a half profile of 3 to 8 points keel first (the Zero Day's `At`,
  `SkinHalf` and `MustFit`, and a Hull Section's profile). Every station of a body has the same point count. Offset, Mirror
  (a pair at -x) and Cut (where it meets another body, the skin inside the other is left out).
- `PieceKinds`: drives (on a body's end face), RCS, guns, systems, and the sockets: Airlock, Docking collar, Cargo hatch and
  Boarding hatch (the boarding shaft kit's belly hatch). Each draws itself in its own frame (X across, Y out of the skin, Z
  towards the bow). A piece sits at a station position and a place round the profile, or on an end face.
- `RoomKinds`: what a room is for. A Hold is a cargo hold; a Shaft joins the decks it overlaps.
- `DesignScene`: the checks and the page's scene. Rooms must sit 6 cm inside the skin (MustFit's margin) and apart; rooms
  that share 0.9 m of wall get a door; each socket must be within its reach of a room of the kind it needs (a cargo hatch a
  Hold, a boarding hatch a room on the lowest deck over it); every room must be reachable from a way aboard.
- `DesignEditor`: every edit as a JSON command with undo (a drag's commands after its first merge into one step).
- `DesignTemplates`: the Zero Day start (its ten stations, two decks and a room plan in its rooms' widths), section shapes and
  bodies to add. Shapes are starts only: no ship kit, every point is the player's.

## The page (`tools/ship-designer`)

`ShipDesigner.Web.csproj` compiles `Source/ShipDesigner` plus `Api.cs` (the `[JSExport]` calls) with the .NET SDK's own
WebAssembly SDK (no workload). The page draws with three.js and asks C# for everything else. Build and stage:

```
cd tools/ship-designer
dotnet publish -c Release -o OUT
```

then copy `OUT/wwwroot/index.html` and `OUT/wwwroot/_framework/*` (not `.br` or `.gz`) into a folder as `index.html` and
`framework/` (an Artifact reserves paths starting with `_`), and publish it to the same Artifact with the `.wasm` files as
`application/wasm`. File names carry a hash: remove the old ones (null) when they change. The game's own build leaves
`tools/ship-designer` out (`Directory.Build.props`).

## Flying a design (Karl, 2026-11-26)

A spawned design flies from its own helm on its own drives and RCS; nothing is turned into the shipped parts. All the mapping
is in `DesignParts.cs` (the bill of materials):

- **Seats** (`DesignSeats.cs`): a Seat socket dropped on the skin by a room (or linked to one) stands on that room's floor,
  facing the bow (turned by Turn). Its role is Helm, Captain, Gunner, Engineer, Navigation, Sensors, Comms or Passenger; in the
  game it is a seat part on its own element, the Helm the drive seat, the others the cockpit stations' kinds where one
  matches. The design checks warn when a ship with drives has no Helm. F on the hull sits you at the free helm.
- **Thrusters:** a drive pushes along its face's normal (`ThrustExhaust`, any direction, not only an axis), 3 MN for a Main
  drive and 0.8 MN for a Small one at size 1, by size squared. An RCS quad is four nozzle elements (two across, two along), 5 MN
  each at size 1 (Heavy RCS 1.6 times its size). **Unfed for now:** `EngineFuel.None`, no tank, no power drawn, in Creative and
  Survival alike, until designs have tank pieces.
- **Flight:** a design with thrust gets a hidden flight computer by its helm, a Flight Stabilizer on the Ship pattern with no
  wires, which takes the helm (Shift up, Ctrl down, W ahead, A/D turn, as any ship).
- `--design-check [NAME]` prints the flight line and flies a sortie from the helm on the proving ground (lift, hover, ahead,
  stop, turn, hold, land), judging each leg and the world step's time (`--no-fly` skips it).
