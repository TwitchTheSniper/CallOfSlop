# The procedural ship generator

The one guide to the ship generator (Karl, 2026-11-22: "a procedural generator which makes the ships using programming"). The
design is the Claude Doc "Procedural ship generator v2" (XfQfYLjXQc3MbGGREuUjEU); this page says what is built and how to use it.
It starts fresh: nothing of the old vehicle kit's ship code (v4) is used.

## What it makes

A **hull**, sold and bought on the market, with **sized slots** that a buyer (a player or an NPC) fits with the parts it has. Each
hull ships with a **reference fit** that fills every slot, which proves the slots work: the blueprint is the hull plus that fit.

Each faction has its own generator with its own architecture and look; they share one contract (below). Built so far:

| Faction | Generator | Job | Shipped hulls |
| --- | --- | --- | --- |
| Traders | `Source/ShipGen/TraderHauler.cs` | carry goods between markets | Tithe Hauler (seed 1), Tender Hauler (seed 7) |

## Running it

- `--gen-parts shipgen_parts` makes every shipped hull (the briefs in `ShipGenParts.Shipped`): parts under
  `Constructs/Parts/ShipGen/<class>/` (the shared ones under `Shared/`), the blueprint `Constructs/Blueprints/<Class>Hauler.json`,
  and the report `docs/sandbox/shipgen/reports/<class>.md`. `--check` says whether the files are up to date.
- A new hull is a new brief: add `new("Traders", "trade between markets", SEED)` to `Shipped`, regenerate, and read its report.
  Two briefs that roll the same class name fail; give one a `Name`.
- After `--gen-parts`: `--check-construct-parts` (grep REFUSED in `callofslop.log`), `--check-vehicle FILE` (builds it, counts
  coplanar faces between parts; the wheel and drive lines are a ground vehicle's), `--check-building "<Class> Hauler"`,
  `--ladder-check "<Class> Hauler"`, `--flight-check FILE`, `--selftest-only "No z-fighting"`.

## Legible by design

Karl couldn't tell how v4 worked, so every run writes a **report**: each choice the dice made, its range and why, stage by stage
(Brief, Layout, Hull, Inside, Slots, Drive and lift, Dress, Pieces), and the numbers the stages worked out (zones, mass, lift).
A hull that breaks a rule writes **nothing**: the run fails with every problem listed. Rules checked in the generator today:

- every room fits inside its section with 10 cm to spare (`Fits`);
- every piece is welded to the rest (`HullPieces.PlaceOrder` names any that aren't);
- at most 256 boxes and 64 sockets a part, names of 32 characters;
- lift at least twice the weight with the reference fit and empty containers.

## The contract (Source/ShipGen/ShipGenCore.cs)

- `ShipBrief`: faction, job, seed, optional name.
- `Dice`: one stream per subject (`name`, `layout`, `hull`, `drive`, `dress`), each seeded from the seed and its name, so a
  change in how the dress is rolled never moves the layout. Every roll is recorded in the report with its range and why.
- `ChoiceReport`: the choices, the notes and the problems, written as Markdown.
- `HullPieces`: draws boxes straight into the piece they belong to and welds the pieces (`Connect`: a facing socket pair on the
  biggest face two pieces' solid boxes share; `PlaceOrder`: out from the first). All state is per run. Socket names are unique
  over the hull, so a fitting finds its piece by the socket's name alone. One tolerance (1 mm) for faces that meet.

## The Traders hauler

- **Architecture** (fixed by the job): a tall crew cab under a glass nose (cockpit, bunk room, head, trade office, galley, airlock
  over a boarding shaft, stores), a neck to a thin walkable spine with container cradles off its flanks, a neck to a squat, wide
  engineering block, a short stern, and a drive nacelle on a pylon either side of engineering. One deck all along.
- **Rolled**: 2 to 4 bays, containers 1 or 2 high, the tumblehome, the nose, the cab's beam and chines, the spine's and
  engineering's beam, the nacelles' size, stand-off, height, tank and tail, the livery (blue keel, blue belt, blue crown) and the
  cab roof's gear (plain, antenna farm, sun visor).
- **The loft**: every station's section has the same facet directions (flat keel, 45 degree chines, vertical lower side, the
  hull's tumblehome, 45 degree upper chine, flat roof), so every facet between two stations is a flat trapezoid. Ribs stand
  proud at each station where the hull doesn't step in; plates stop short of them, and neighbouring facets' ends and ribs are
  staggered so no two faces share a plane.
- **Pieces**: keel, port side, starboard side and roof cut at every station, plus the nose, the cab's and engineering's walls and
  the stern: a hit takes a panel, not a deck. The cab's and engineering's keel bands are the lift jets' fuel tanks.
- **Slots**: `pilot`, `copilot` and `computer` floor sockets in the cockpit; a keyed 2x2 engine Module Socket on each nacelle's
  tail plate (the nacelle is the drive's mount: its Drive Core bolts in behind the plate, mended through the service hatch); a 2x2
  reactor Module Socket in engineering; a 1x1 sensor Module Socket on the cab's roof; a cradle hardpoint each side of each bay.
- **Reference fit**: Flight Couches and the Trader Hauler Flight Computer; a Rust Bucket Drive (600 kN) and Drive Core S per
  nacelle; the Trader Hauler Power Plant (400 kW, a generator: it reads as a reactor module); a Long Range Sensor; a Trader
  Hauler Cradle with a frozen Cargo Volume and its Standard Containers on each hardpoint; the crew's fittings, lamps and doors.

## Not done yet

- Hulls without nacelles (drives through the transom), more jobs, other factions.
- A command to make any seed without adding it to the shipped list.
- Windows in the cab's sides that look into the rooms (the portholes are dressing).
