# Baked lighting

Developer request (2026-10-27): light sources in maps, made in the map kit, with the light baked into the map so the
game never has to render every light every frame; Ashgrave Cathedral is to be lit with it next.

Code: `Source/Maps/MapLights.cs` (the `LightSource` and `BakedLighting` ops, prop lights), `Source/Maps/LightBaker.cs`
(the baker and the `LightBake` grid), `Source/Render/LightGrid.cs` (the GPU side), `Lighting.Glsl`'s `ApplyLightAt`
(`Source/Render/Lighting.cs`), `Source/MapKit/Lights.cs` (the kit), `Source/App/Program.Lighting.cs` (dynamic lights,
`--light-shots`), the self-test `Source/App/SelfTestLighting.cs` ("Baked lighting"). The demo is the Dev map **Light
Test** (`Maps/Dev/LightTest.json`, written by `Source/MapKit/Generators/LightTest.cs`).

## 1. What a map gives

- **`LightSource` ops**: point, spot or area lights with a colour, an intensity, a range and an optional `flicker`
  (Maps/README.md "Lighting").
- **Prop lights**: a prop file's `"lights"` list (Props/README.md "Lights"), in the prop's frame; placing the prop turns,
  moves and scales its lights. Lamps, lanterns, street lights, a fireplace and the new `Props/Lighting` set (Iron
  Torch, Candle Stand, Altar Candles, Chandelier, Brazier) carry them.
- **`BakedLighting` op** (optional): grid cell size, how much sky light reaches enclosed places (`indoor`), sun shadows,
  prop lights on or off, and a `region` and `top` to bake only part of a big map.

A map is **lit** when it has a `BakedLighting` or a `LightSource` op. Only lit maps bake anything: every other map,
including ones with lamp props, draws exactly as before (the shader takes the old path; under Mesa llvmpipe the unlit
maps rendered pixel-identical to main, two pixels in six pictures off by 1/255). Lights are looks only: they are not in
the map's fingerprint and never go over the network (no protocol change).

## 2. The representation: a 3D light grid

Everything is boxes, players and zombies move, and the budget says no per-light work at draw time. So the light is
baked into a **3D grid over the map** (an irradiance volume), one cell every 0.5 m by default, uploaded as three small 3D
textures and sampled per pixel at the pixel's world position, nudged 0.6 of a cell along its normal so a wall reads the
air in front of it:

| Texture | Format | Holds |
| --- | --- | --- |
| A | RGBA16F | rgb: the static lights' colour reaching the cell, as a surface facing them sees it; a: the share that flickers |
| B | RGBA8 snorm | xyz: the lights' average direction, weighted by brightness (its length says how one-sided the light is); w: ambient left (sky visibility) |
| C | RG8 | r: how much of the sun reaches the cell; g: the flicker phase of the brightest flickering light |

Why a grid and not per-vertex colours or lightmaps:

- **One light for everything.** Map boxes, props, buildings, terrain, foliage, decals, meshes, players, zombies, gibs
  and the first-person gun all read the same grid, so a zombie walking past a torch is lit by it. Per-vertex or
  lightmapped boxes would light only the static world.
- **No change to how the world is drawn.** Boxes are instanced (one draw call for a whole map); per-vertex colours would
  mean subdividing and un-instancing them, and lightmaps an atlas and UVs for every box face.
- **Bounded cost.** Three filtered texture reads and a few multiplies per pixel, whatever the number of lights.
- **The direction stops leaks.** A thin wall between a light and a room is thinner than a cell, so filtering mixes the
  two sides; but the lit side's direction points at the light, and a surface facing away gets `max(dot(n, dir), 0)` of
  it, so the far side of a wall stays dark. Light from all round (several lights) has a short direction and lights every
  face a little (60%).

The trade-off is softness: shadow edges are about a cell wide (0.5 m), and very thin geometry can leak sun at its edges.
That suits the blocky look; the Cathedral can use `cell: 0.35` in its nave if it wants crisper edges.

## 3. The baker

`LightBaker.Bake(map)` runs at the end of `Map.Validate` for lit maps (so a map read from its bake already has it):

- **Grid:** the playable area plus a metre, from half a metre below the floor (or the terrain's lowest point) to 1.5 m
  above the tallest box or light (at most 96 m), or the `region` and `top` given. At most 2^20 cells (about 13 MB): a
  bigger map gets coarser cells (each try 25% bigger) unless it gives a region.
- **Occluders:** `Map.Solid` (map boxes, prop colliders, building walls, foliage trunks, hidden colliders) without the
  boxes that come and go or don't block sight: building doors and glass, Zombies doors and barriers, map logic boxes,
  clip boxes. They are filed in a uniform grid of box lists (cells of 2 m or more) and rays walk it with a 3D DDA, each
  box tested once per ray. Terrain maps also step rays along the heightfield. A light inside a box (a bulb in its
  lamp's collider) ignores that box.
- **Each cell:** its centre, pushed out of any box it falls in (to the nearest face); then every light whose range
  reaches it (lights are binned in 4 m bins): falloff `(1 - (d/r)^2)^2 / (1 + 8 (d/r)^2)` (an inverse-square-like core,
  zero at the range), a spot's soft cone, an area light as 1-9 point samples with a cosine emitter, and one shadow ray
  per light (per sample). One ray towards the sun. Cells still inside solid take their open neighbours' average.
- **Sky (ambient occlusion):** on a half-resolution grid, 16 cosine-weighted rays over the upper hemisphere; a ray that
  gets 24 m (or out of the occluders' grid) sees the sky. Ambient = `indoor + (1 - indoor) * seen`. It is what makes a
  roofed hall dark and a courtyard bright, and darkens corners.
- **Deterministic:** each cell depends only on the map and its lights are summed in light order, so threads change
  nothing ("Baked lighting" bakes the scene again and on one thread and compares the bytes).

**Cost** (the agent container, 2 cores, shared): the Light Test map (44 x 32 m, 92 x 19 x 68 cells, 20 lights) bakes in
250-330 ms and takes 1.6 MB; the self-test scene (68 x 15 x 68 cells, 4 lights) 50-100 ms. Ashgrave Cathedral with 200
extra test lights (128 x 112 m, 38 m tall: coarsened to 0.98 m cells, 134 x 39 x 117, 231 lights) baked in 1.1 s and
takes 8.2 MB; with a `region` round the cathedral building it would get finer cells for the same memory. Shipped maps
are baked once by the release (`--bake`), dev builds cache theirs (MapBake), so players never wait for it.

## 4. Bakes and keys

The grid is a field of the map (`Map.LightBake`), so the generic map bake (docs/perf/STREAMING.md section 7) writes and
reads it like everything else ("Map bake" checks every shipped map, Light Test included, reads back exactly). The key
already covers every input: the map file's text (its `LightSource` and `BakedLighting` ops), the Props folder (prop
lights) and the exact build (the baker's code). A changed light changes the key and the map is baked again ("Baked
lighting" checks the key and the grid change). `LightBake.BakeMs` is `[NotBaked]`.

## 5. Drawing

- `LightGrid` uploads a map's grid once (on its first frame) to texture units 12-14, and keeps one 224-byte uniform block,
  `LitFrame`, at binding 7, shared by every scene program: mode, time, the grid's origin and scale, a probe and up to
  four dynamic lights. Each program binds the block and its samplers once in `Lighting.Upload`.
- The block is rewritten at most three times a frame (`glBufferSubData`): the world pass (mode 1, the grid at each
  pixel), the first-person gun (mode 2: drawn in camera space, so the CPU samples the grid once at the eye and turns the
  light's direction into view space) and menus and previews (mode 0, the classic light; every frame starts there).
- `ApplyLightAt(albedo, n, wrap, worldPos)` replaces `ApplyLight` in the box, building, prop, mesh, terrain, foliage and
  decal shaders. Mode 0 returns `ApplyLight` exactly; modes 1 and 2 scale the hemisphere ambient by the cell's sky
  visibility, the sun by its sun visibility, and add the baked light times how much the surface faces it, wavering by
  up to 60% for the flickering share (`LitFlicker`: three sines of time and phase). The box shaders' rim light is scaled
  by the same ambient (`gLitAmb`), so it doesn't glow in the dark. Prop glow (lit flames, screens) is added on top as before.
- No per-light loop and no per-frame allocation (the self-test runs the block and probe 2,000 times: 0 bytes). The
  dynamic-light loop runs only in mode 1, at most 4 times.

**Dynamic lights** (optional, capped at 4 a frame): your muzzle flash and blasts younger than half a second light lit
maps (`Program.AddFrameLights`); unlit maps never get them.

**Settings > Video > Lighting:** Off (the classic light everywhere), Baked, Baked + flashes (default). `lighting=` in
settings.txt. The baked grid costs the same whatever the number of lights.

## 6. Destruction and moving things

Destroyed boxes keep the light they had: the grid is not re-lit at run time (a wall blown open leaves its shadow on the
floor behind it, faintly). The alternatives were a local re-bake on a worker (tens of ms per hole, a texture re-upload)
or a dynamic shadow, both costing frame or tick time for a rare event; on the maps that will be lit (indoor Zombies
maps) destruction is off. Doors, windows and Zombies barriers are left out of the occluders, so an open door lets
light through. Movers aren't occluders either.

## 7. Looking at it

- `CallOfSlop --light-shots "Light Test" [--at x,y,z,yaw,pitch ...]` writes `smoke_light_<view>_on.bmp` and `_off.bmp`
  next to the log and quits (views: the map-select overview, each `--at` camera, or the first spawns). It works without a
  GPU under a virtual X server with Mesa's software GL, which is how this was developed in the agent container:
  `xvfb-run -a -s "-screen 0 1280x720x24" dotnet CallOfSlop.dll --light-shots "Light Test"`. (The whole `--smoketest`
  runs there too.)
- The map's `test` block takes `lightProbes` (Maps/README.md "Testing a map"), which the Map contract and `--check-map`
  check: bright where the lights are, dim where they aren't.
- The log prints `[lighting] light grid ...` when a lit map's grid is uploaded.

## 8. Lighting Ashgrave Cathedral (the follow-up)

The Cathedral is a kit generator (`Source/MapKit/Generators/AshgraveCathedral.cs`). To light it:

1. `k.BakedLighting(cell: 0.5, indoor: 0.15, region: (x0, z0, x1, z1))` round the cathedral and its yard (keep the
   cells at or below 0.5 m in the nave).
2. Give its pack props lights (`Maps/Zombies/Props/Cathedral`: Wall Torch, Iron Brazier, Iron Candelabrum, Iron
   Chandelier, Votive Candle Stand; a `"lights"` list each, flickering), so every one placed brings its light.
3. Stained glass: `Lights.Window(k, ...)` inside the Lancet and Rose windows, in the glass colour.
4. Add `lightProbes` to its `test` block for the nave, a crypt corner and the belfry.
5. Run `--light-shots` on it and `"Baked lighting, Map bake, Map contract, Zombies cathedral"`.
