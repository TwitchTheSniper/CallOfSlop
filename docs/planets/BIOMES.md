# Biomes and plants on planetoids

A planetoid can have **biomes** (Sean, 2026-11-22: "biomes where they fit, such as the Earth-type planet"): regions with
their own ground colours and their own plants. Caledon has six; the barren worlds (Cinder, Glacier, Crumb) have one
"rubble" biome that grows only loose boulders and scree. A world without a `"biomes"` object is drawn as before.

The code: `Source/Planets/PlanetBiomes.cs` (the climate and the map file), `Source/Planets/PlanetFlora.cs` (where each plant
stands, and the trunks constructs hit) and `Source/Render/FoliageRendererPlanet.cs` (the drawing). The worlds' biomes are
written by `tools/planets/make_worlds.py` (`CALEDON_BIOMES`, `barren`).

## The climate

Two smooth fields over the sphere, both functions of the direction and the features' height alone (so every peer and
every thread gets the same answer, and nothing is stored or sent):

- **Temperature** (degrees): `tempEquator` at the equator to `tempPole` at the poles (by the square of the sine of the
  latitude), less `lapse` degrees per km of height above 0, broken by `tempNoise` degrees of noise `tempScale` m across.
- **Wetness** (about 0 to 1): seamless noise `wetScale` m across, plus up to `wetLowAdd` in ground lower than `wetLow`
  (fading over `wetLowEdge` m): hollows and glen floors are wetter.

Each biome names its `temp`, `wet` and `height` ranges and the steepest `slope` it covers; outside a range it fades over
`tempSoft` (2 degrees), `wetSoft` (0.06) and `heightSoft` (40 m). Where several hold they share the ground by weight: the
ground colour blends and the plants mix. Where none holds the world's own surface shows, with no plants.

## The map file

```json
"planetoid": { "biomes": {
  "seed": 4, "tempPole": 7, "tempEquator": 17, "lapse": 6.5, "wetScale": 2200, "wetLow": -60, "wetLowAdd": 0.3,
  "clearFlats": true,
  "list": [
    { "name": "pinewood", "temp": [5, 10.5], "wet": [0.35, 0.85], "height": [-600, 820], "slope": 30,
      "grass": "#3a4c2c", "dirt": "#4d3f30",
      "plants": [ { "type": "scotspine", "density": 0.012, "size": [12, 20], "color": "#2f4a2a", "bark": "#8a5a3c",
                    "patch": 0.45, "patchScale": 140 } ] }
  ] } }
```

- A biome's `grass`, `dirt` and `rock` replace the world's colours there (the surface's slope rules, sand, snow line and ice
  patches still apply).
- A plant: `type`, `density` (per square metre at High foliage), `size` (trees: height in metres; others: a scale),
  `color`, `bark` (trees), `jitter` (each plant's colour strays this much), `patch` and `patchScale` (0 even; 1 in clumps
  that wide with bare ground between) and `slope` (the steepest ground it grows on).
- `clearFlats` (on by default) keeps the world's `flatten` places bare: the landing ground and the kept sites.
- At most 12 biomes, 12 plants each; a plant's density is capped by its layer (below). Bad values are refused with the reason.

## The plants

| Layer | Plants | A sample point every | Drawn within (Low / Medium / High) |
| --- | --- | --- | --- |
| Ground cover | grass, heather, scree | 0.9 m | grass 22 / 38 / 60 m, heather and scree 45 / 70 / 110 m |
| Brush | shrub (fern), bracken, reed, bush | 1.8 m | 45 to 150 m |
| Trees and rocks | tree (oak-like), birch, pine, spruce, scotspine, boulder | 4.5 m | trees 300 / 450 / 700 m, boulders 0.6 of that |

Each layer has a lattice of sample points on the six faces' grids, each jittered by its own hash. A point takes a plant from
the biomes under it, each plant's chance its density times its biome's weight (times its patch noise), or nothing. A point
whose ground is dug grows nothing (the cells round it are made again within 2 s of the dig).

Trees are drawn as a canopy and a thin **stem** (`FoliageType.Stem`, in the tree's bark colour). Beyond the trees' reach a
forest is its biome's darker ground colour.

## Solid trunks

Sean (2026-11-22): **trees are solid near constructs, and only there**; people walk through them, and underbrush and rocks
never collide. The construct contacts ask `PlanetFlora.GatherTrunks` for the trees round each body group
(`ConstructPhysicsContacts`, after the planet's ground): each trunk is a box standing along "up", as tall as its stem.
Only the tree layer's points round the box are asked, through a cache (32,768 points), so a vehicle in a forest costs a
few lookups a tick; a body more than about 90 m across asks nothing (its pieces would). A box over a cube face's edge
sees only one face's trees.

## What it costs

- **Making cells** (worker threads, at most two at once, the nearest first): `--check-planetoid` prints the time a cell
  of each layer takes and what a cell at the pole holds. About 1 to 7 ms a cell on the cloud box.
- **Drawing**: one instanced draw per plant type, mesh (near or far) and visible cell; nothing is uploaded except finished
  cells (at most three a frame), nothing is allocated per frame. The `[foliage] planet` line in the log every 5 s gives the
  cells, plants, draws and CPU time.
- Foliage quality Off draws none and frees the cells.

## Trying it

`--check-planetoid FILE [--at LAT,LON]` prints each biome's share of the ground, the cells' cost, and at each `--at` place
its temperature, wetness and biomes; `--png` colours the picture with the biomes' ground. In a window:
`--window-shot OUT.png --map Caledon --calm --do "view geo 45,60 | 40,12,30; wait 300"`.

## Not yet

- Trees aren't felled for wood on planets (the flat maps' forests, ResourceField.cs, are a resource).
- Plants grow through a construct standing on them (a base's floor): only dug ground clears them.
- Bullets and tools don't hit trunks; only constructs do.
- Mille (hollow) and Slopolis (a city) have no biomes; Redrock and the Dev worlds neither.
