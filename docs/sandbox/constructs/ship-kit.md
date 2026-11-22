# The ship kit

A base for new lofted ships (Karl, 2026-11-21: new ships only; the older ships keep their own generators). It is the code the Satchel, the Docket, the Dibber and the Grapnel each carried a copy of, gathered once in `Source/PartKit/ShipKit`. The Knacker breaker (knacker.md) is the first ship on it in the game.

**What it is not:** a set of shared ship parts. Every piece and fitting a ship gets is still drawn for that ship, in its own part folder and under its own name prefix. The kit holds the method (how a hull is lofted, split, welded and dressed), never the look. Factions keep their look: a ship's colours come from a `ShipStyle`, its shapes from its own sections and its own fitting specs.

## The pieces

- `ShipStyle.cs`: a faction's paint for a lofted ship (skin, armour, frame, hazard, glass, the inside's walls, decks and screens). Each ship gives its faction's record (the Knacker's `Salvagers`); the kit draws nothing with a colour of its own.
- `LoftedShip.cs`: the abstract base (`LoftedShip(prefix, section, style)`). A ship sets `Hull` (its stations, each a five-point half profile), `Bands` (where the hull is cut into pieces) and `RibZ`, then uses the loft helpers: `At`, `Facet`, `Band`, `Chine`, `Slab`, `FStrip`, `Plate`, `Ribs`, `VoidT` (the hidden fill that welds a piece to its neighbours), `SkinHalf`, `FacetX`, `RcsPad`, `MountPad`, `Big` (a structure part). Odd facets are staggered 6 mm and trimmed 1.5 mm (no coplanar faces between bands).
- `LoftedShip.Pieces.cs`: rooms and the split. `Floor`, `WallRun` (with `Door` and window `Gap`s), `DoorSock`, `HatchRail`, `CeilStrip`, `Room`, `CeilingAt`, `OnWall`; `SplitDeck` and `SplitBands` cut a big part into its welded pieces (`{Prefix} {short} Wall N`, `Hull Port/Stbd N`, `Ceiling`, `N`), `DealSockets` gives each socket to the piece whose face it is on, `Connect` and `PlaceOrder` weld them (and throw `pieces joined to nothing` naming the stray), `FuelPort`. For the blueprint: `PlaceStructure`, `Hang`, `Holder`, `Paint`, `HangDoor`, `Lamp`, `Switch`, and `Emit` (checks names fit 32 characters, writes the part files and the blueprint).
- `LoftedShip.Fittings.cs`: fittings drawn from specs the ship writes, so two ships' jets or consoles share code, not shapes: `JetSpec` / `Jet` (lift, aft or fore; its bell profile, sides, shroud and louvres), `PlantSpec` / `PowerPlant` (its cells' layout and cage), `ConsoleSpec` / `FlightComputer`, `LadderSpec` / `Ladder`, `HatchSpec` / `BellyHatch`.

## A new ship on it

1. A generator in `Source/PartKit/Generators/<Name>Parts.cs`, `sealed class ... : LoftedShip, IPartGenerator`, listed in `PartKit.cs`. Its sections, rooms, bands, specs and blueprint are its own; read look.md and the faction's ships first.
2. `--gen-parts <name>_parts`, then rebuild (the game reads `bin/`).
3. The checks: `--check-vehicle FILE` (coplanar faces; ignore its wheel lines), `--flight-check FILE`, `--ladder-check "NAME"`, `--check-building "NAME"` (every doorway's landing, seat and switch reachable; the map is `building-walk.txt`), `--check-construct-parts` (grep REFUSED), `--selftest-only "No z-fighting, Compound collision"`.
4. One Assembling row per piece at the end of `Assembling.cs`' Tail (regenerate `assembling`, `--selftest-only "Assembling"`), the name in `Categories.json`'s unlisted Ship rule, and a `ProtocolVersion` bump for the new part folder.

## Rules learnt on the first kit ships

- A thruster draws from the part it hangs on: drives on a stern piece make that piece a fuel tank (`"fueltank"`, `FuelPort`).
- A solid pad (a leg's, a jet's) sits inside one station band, or it is in the neighbouring piece's way.
- A socket sits on a solid face of the piece that owns it; a socket on a drawn bezel or a wall another piece owns is refused.
- A door's head is at the floor plus 2.25 m: the ceiling stands higher, or the head z-fights it.
- Keep a body's width (0.8 m) clear on the far side of each doorway: a hatch rail 4 cm from a door's middle line failed the walk check.
