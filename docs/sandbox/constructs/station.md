# Space stations

The station kit is a part kit generator, `space_station` (`Source/PartKit/Generators/SpaceStation*.cs`):

- `SpaceStationLook.cs`: the shared industrial look (palette, finishes, plated walls with bands, seams, ribs and windows, roof clutter, PDC turrets). Every station piece uses it, so stations share one style.
- `SpaceStationKit.cs`: the building blocks, each a call that makes one or more pieces: `Block` (a square block of decks: floor, roof, four walls, deck slabs round a lift core, rooms inside a pressure wall with a service gap), `Spine` (the truss between blocks, with its lift tube), `Arm` (a docking arm in pieces of about 20 m: walkway, ducts, feeds, gantry rails), `Cradle` (a berth's posts; the middle one carries a Docking Clamp Large and a boarding tube), `Gantry`, `Apron` (a freight deck with a lift table and a tunnel under it), `Band`, `Mast`, `TankFarm`, `Radiator` (a truss boom with tapered panels above and below, heat pipes glowing at the root), `Refinery` (an open cage of process plant: hopper under the ore chute, crusher drum, smelter, columns, gas spheres, fins).
- `SpaceStationHex.cs`: hexagonal blocks (`HexBlock`: floor, roof, the six walls as one piece, decks, lift core; a corner points east), `Pad` (a landing pad with a Docking Hatch and a Docking Clamp Medium), `SkyBridge`, `Fittings` (open floors' planters, bunk pods, stalls, beds, desks, tables). Their looks are hulls; their collision is hidden axis-aligned boxes, a staircase inside each slab and a run of steps inside each slanted wall, so pieces still meet face to face. The ring station uses its `FacePanel` and `Pad`; `HexBlock` and the square `Block` are unused since the third pass.
- `SpaceStationRing.cs`: the ring station (third pass, Karl 2026-11-18, after his reference picture `station/reference-ring-station.png` in the project files). `RingStation()` lays out Caledon Highport: a ring of 16 segments (apothem 108 to 132 m; pad, habitat, hangar, industry and tower kinds, each a stepped profile mitred to its neighbours under a joint collar) round one hexagonal hub with a faceted command dome and mast, three spokes, and a docking spindle under the hub: the customs drum, the dock collar (Zero Day arms north and south, Magpie arms east and west), the freight collar (Dray arms east and west), engineering with two radiator booms, the reactor and the drives. Looks are hulls in each piece's turned frame; collision is the `Lattice`: hidden boxes on one 2 m grid, each piece of a layer taking the cells its convex footprint holds, so pieces at any angle meet face to face and weld (`Reach` says where an arm welds on). About 47,000 triangles.
- `SpaceStationParts.cs`: `Make()` joins touching pieces with socket pairs (`Connect`, which throws on overlaps), orders them out from the hub, moves each origin to its piece's centre (`Recentre`: a box may reach at most 200 m from its origin) and writes the pieces to `Constructs/Parts/SpaceStations/Highport/` and the blueprint to `Constructs/Blueprints/CaledonHighport.json`. The first two passes (a 310 m tower, then a hex commons of seven towers) are in PR 301's history.

Pieces are in the `Station` group, unlisted in the Tab inventory and out of the Assembler (blueprint-only). Shaped looks are detail only and every piece says `"collider": "boxes"`. Berths are sized from the ships (`ZeroDay`, `Magpie`, `Dray` in `SpaceStationParts.cs`).

## Adding a station

Write a new plan method beside `RingStation()` with the kit's calls, call it from `Make()`, then `--gen-parts space_station`, `--check-vehicle Constructs/Blueprints/NAME.json` (the drive, mass and 220-part lines are a vehicle's and don't apply to a station; read the z-fighting ones) and `--selftest-only "No z-fighting, Compound collision"`.

## The Station gallery

`--preview-gallery DIR --group Station` exports every station and station part; it is published as the Station gallery Artifact, https://claude.ai/artifact/WbNLguag82bvYRDQK2mHgm (separate from the Ship gallery; republish it with the changed `models/*.json`). The layout sheets from the first design, the findings from the second and Karl's reference picture are in the project files under `station/highport/`.

Nothing spawns a station in a world yet.

The chutes, conveyors, pipes, sky bridges and boarding tubes are looks only: nothing runs through them yet (no links). The clamps and hatches are the real parts, not yet wired.
