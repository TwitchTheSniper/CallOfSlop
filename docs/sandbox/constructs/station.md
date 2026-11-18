# Space stations

The station kit is a part kit generator, `space_station` (`Source/PartKit/Generators/SpaceStation*.cs`):

- `SpaceStationLook.cs`: the shared industrial look (palette, finishes, plated walls with bands, seams, ribs and windows, roof clutter, PDC turrets). Every station piece uses it, so stations share one style.
- `SpaceStationKit.cs`: the building blocks, each a call that makes one or more pieces: `Block` (a square block of decks: floor, roof, four walls, deck slabs round a lift core, rooms inside a pressure wall with a service gap), `Spine` (the truss between blocks, with its lift tube), `Arm` (a docking arm in pieces of about 20 m: walkway, ducts, feeds, gantry rails), `Cradle` (a berth's posts; the middle one carries a Docking Clamp Large and a boarding tube), `Gantry`, `Apron` (a freight deck with a lift table and a tunnel under it), `Band`, `Mast`, `TankFarm`, `Radiator` (a truss boom with tapered panels above and below, heat pipes glowing at the root), `Refinery` (an open cage of process plant: hopper under the ore chute, crusher drum, smelter, columns, gas spheres, fins).
- `SpaceStationHex.cs`: hexagonal blocks (`HexBlock`: floor, roof, the six walls as one piece, decks, lift core; a corner points east), `Pad` (a landing pad with a Docking Hatch and a Docking Clamp Medium), `SkyBridge`, `Fittings` (open floors' planters, bunk pods, stalls, beds, desks, tables). Their looks are hulls; their collision is hidden axis-aligned boxes, a staircase inside each slab and a run of steps inside each slanted wall, so pieces still meet face to face. Caledon Highport's commons is a hex concourse on the spine with the hub tower and six towers on it (varied in size, height, setback and windows), pads on the towers' roofs.
- `SpaceStationParts.cs`: the stations themselves. `Highport()` lays out Caledon Highport bottom to top in metres (x east, y up the spine, z south); `Make()` joins touching pieces with socket pairs (`Connect`, which throws on overlaps), orders them out from the first, moves each origin to its piece's centre (`Recentre`: a box may reach at most 200 m from its origin) and writes the pieces to `Constructs/Parts/SpaceStations/Highport/` and the blueprint to `Constructs/Blueprints/CaledonHighport.json`.

Pieces are in the `Station` group, unlisted in the Tab inventory and out of the Assembler (blueprint-only). Shaped looks are detail only and every piece says `"collider": "boxes"`. Berths are sized from the ships (`ZeroDay`, `Magpie`, `Dray` in `SpaceStationParts.cs`).

## Adding a station

Write a new plan method beside `Highport()` with the kit's calls, call it from `Make()`, then `--gen-parts space_station`, `--check-vehicle Constructs/Blueprints/NAME.json` (the drive, mass and 220-part lines are a vehicle's and don't apply to a station; read the z-fighting ones) and `--selftest-only "No z-fighting, Compound collision"`.

## The Station gallery

`--preview-gallery DIR --group Station` exports every station and station part; it is published as the Station gallery Artifact, https://claude.ai/artifact/WbNLguag82bvYRDQK2mHgm (separate from the Ship gallery; republish it with the changed `models/*.json`). The layout sheets from the first design are in the project files under `station/highport/`.

Nothing spawns a station in a world yet.

The chutes, conveyors, pipes, sky bridges and boarding tubes are looks only: nothing runs through them yet (no links). The clamps and hatches are the real parts, not yet wired.
