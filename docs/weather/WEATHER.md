# Weather: moving clouds and hurricanes (slice 1, 2026-11-02)

The first slice of the weather engine in the design "BRUTAL Planetary Weather System Architecture" (the project's
reference PDF). It gives the Streamed Terrain map a living sky: clouds that drift with a simulated wind, and tropical
cyclones that form over a warm sea, grow through the categories, travel, recurve and die over land. Press **F11** in a
match on a map with weather for the weather map.

## Where it stands against the design

The design assumes Vulkan 1.3 compute queues, a cubed-sphere shallow-water core (Tier 0), a DCGrid mesoscale RANS
solver with six-phase microphysics (Tier 1), electrification (Tier 2) and Nubis-style volumetric clouds. The game runs
on OpenGL 4.1 / 4.6 (no compute on macOS), so this slice is a CPU stand-in for Tiers 0 and 1, sized so it costs
nothing on the frame:

| Design | This slice |
| --- | --- |
| Tier 0: global SWE on a cubed sphere | A 3,072 km square region round the map (256 x 256 columns of 12 km), a beta plane (latitude from the map's `latitude`); the synoptic flow is prescribed: trade easterlies, a subtropical ridge that wobbles north and south over 6.5 days, westerlies with troughs travelling east, easterly waves travelling west, a divergence-free eddy field. |
| Tier 1: DCGrid RANS + 6-phase microphysics | Two water phases: column vapour and cloud water, carried by the wind (semi-Lagrangian), evaporation from the sea (faster in strong wind, the storms' fuel), condensation where air converges or is lifted, cloud evaporating into dry air, warm rain-out (Kessler-like). |
| Cyclogenesis, hurricane branch (axisymmetric, Coriolis, upper-ocean energy budget) | Each storm is an axisymmetric vortex (solid-body core, power-law decay, tapered edge) whose maximum wind grows towards the potential intensity of the sea under it (from its temperature), held back by shear and dry air, killed by land (e-folding about 11 h). The sea cools in its wake (the upper-ocean budget) and recovers over ten days. Its winds (inflow low down, outflow aloft) are added to the grid's, so moisture spirals in and the cloud shield spirals out; the eyewall and two logarithmic rain bands lift the air, the eye and the gaps between bands sink. |
| Genesis | Blocks of 16 x 16 columns are scored like a genesis potential index: open sea over 26.8 C, cloud clustered, cyclonic turning in the flow (the easterly waves' troughs), shear under 13 m/s, away from other storms; a seeded random draw turns a likely block into a tropical depression. |
| Motion | The storm-free flow averaged over a ring 400 km out (the deep-layer mean) plus beta drift (poleward and west, stronger for stronger storms). Storms run west in the trades and recurve east when the ridge lets them into the westerlies. |
| Renderer (Nubis, Hillaire LUTs) | The existing procedural sky: its cloud layer's coverage and thickness now come from the simulation at the point of the layer each pixel sees, blended between steps, and the pattern drifts with the simulated wind aloft at the player and moves past the camera. |

Not yet: tornadoes and dust, ice phases, lightning, terrain coupling (the hills don't lift the air or make rain
shadows), a diurnal cycle, rain and wind at ground level (particles, sound, pushing players and constructs), syncing
the weather between players, and a volumetric cloud renderer.

## How it runs

- `Source/Weather/WeatherSim.cs`: the simulation. A step is 10 minutes of weather. Deterministic for a seed, no
  allocations per step, and the same whether done whole (`Step`) or a few rows at a time (`Advance`).
- `Source/Weather/Weather.cs`: the running weather on this PC. One simulation for the current map, on its own worker
  thread (`Weather`), so a frame never waits for it. The main thread moves the target time on (`Speed` minutes of
  weather per real second, 60 by default, set per map), and the worker steps up to it and publishes a snapshot:
  the sky's cloud texture (RG8: the noise threshold for the coverage and the thickness), the weather map picture
  (only while the window is open), the storms and their tracks, and the weather over the camera. The main thread
  uploads them (two sky textures, previous and current, so the sky blends between steps). The worker never runs more
  than three hours of weather behind; a slow PC drops time instead.
- `Source/Weather/WeatherImage.cs`: the pictures (satellite view or rain radar; the sky texture).
- `Source/Render/Sky/SkyRenderer.cs`: `uWeather`, `uWxCam` and the two textures on units 8 and 15.
- `Source/UI/UiWeather.cs`: the window. `Source/App/Program.Weather.cs`: the key and the per-frame call (from the
  arena pass, so the lobby's preview of the map has the weather too).
- `Source/Weather/WeatherCli.cs`: `CallOfSlop --weather-sim [--days N] [--seed S] [--cells N] [--latitude L] [--png path{day}.png]`
  runs it headless and prints every storm every 12 hours (in `callofslop.log`), with a PNG of the region each day.

Costs, measured on the 4-core cloud box: about 5 ms a step at 256 x 256 (winds 1.2 ms, transport and physics 3.2 ms)
on the worker; at the default speed that is 6 steps a second, about 3% of one core. The frame pays only the uploads
(128 KB for the sky after each step). Building the simulation takes about 0.1 s, on the worker.

## The map section

```
"skybox": "clear",
"weather": { "seed": 7, "latitude": 24 },
```

Maps/README.md, "Weather", lists the keys. Weather needs a procedural sky (High sky quality) to show in the sky; the
weather map works either way.

## The F11 window

Day and time of the weather, every storm (name, class, wind in km/h and knots, central pressure, heading and speed,
distance and direction from you), its track coloured by class, the 25 km map as a small square with you in it, and
what the weather is doing over you. Speed: paused, 1 min, 10 min, 1 h, 3 h or 6 h of weather a second. Rain radar
shows the rain instead of the clouds. Right-click the map (or **Seed a storm**) to start a tropical depression; it
only grows over warm sea with low shear.

## Tuning notes

- Classes are Saffir-Simpson on the maximum sustained wind: depression under 17.5 m/s, tropical storm under 33,
  Cat 1 to Cat 5 at 33 / 43 / 50 / 58 / 70 m/s. Pressure comes from the wind (Atkinson-Holliday style).
- Potential intensity is 18 + 13.5 (SST - 26) m/s, capped at 85; Cat 5 needs a sea of about 30 C, low shear and a
  moist ring round the storm.
- The rain bands are drawn by the lift they add (`PhysicsRow`): change the spiral's pitch (1.5 / log r) or the arms (2).
- Seed 7 at latitude 24 forms its first storm within the first day of weather (about 25 s at the default speed).

## Next slices (proposed)

1. Weather at ground level: wind pushing smoke, foliage, loose props and constructs; rain particles and sound under
   the rain rate; darker light under thick cloud (the sky's ambient follows the coverage over you).
2. Sync: the server owns the weather time and sends it with the seed, so every player sees the same storm at once.
3. Terrain coupling: orographic lift and rain shadows from the streamed heights, sea and land from the planet.
4. A volumetric cloud pass (ray-marched, after Nubis) fed by the same fields, on the GL 4.6 path first.

## Cold storm (2026-11-12)

Weather at ground level for a cold world, separate from the regional simulation above: a map's `storm` section
(Maps/README.md, "Storm"; `StormSettings`). Looks and sound only, worked out on each PC from its own clock, so
players see the same kind of storm but not the same gusts. Nothing goes on the wire and the simulation never sees it.

- `Source/Weather/ColdStorm.cs`: the state. The level swells and falls over `cycle` seconds (two sines), gusts on
  top, thinned by the height over a planetoid's ground (`RoughAltitude`, gone at `ceiling`). The wind runs along the
  ground (up is `GravityFrames.UpAt`), its heading wandering. Ten times a second five rays go up from the eye (one
  straight up, 30 m; four leaning, 14 m) through `World.RaycastWorld` (map, props, the planetoid's ground, so a mine's
  ceiling counts) and the constructs: the open share, eased, is `Outside`. Frost builds over about 45 s outside and
  melts in 12 s. The light (`ColdStorm.Light`) spreads most of the sun into the ambient, pulls the sky colours to the
  whiteout and turns the fog flat and full (`FogMax` 1); it moves in steps of 1/40 so `Lighting.Use` re-sends it to
  the scene shaders a few times a minute, not every frame. The fog runs from the eye to the visibility.
- `Source/Render/SnowRenderer.cs`: the snow is one instanced draw of streaks with no vertex buffer: a flake's place in
  a 28 m box round the camera is a hash of its number plus the storm's drift (kept in doubles on the CPU, wrapped),
  wrapped in the vertex shader; streaks stretch along the flake's speed, fade near the eye, at the box's edge and into
  the fog. 3,000 / 6,000 / 10,000 flakes at the Particles setting, fewer as the storm or `Outside` drops. The veil is
  one triangle on the far plane after the sky, so the sky goes to the same white as the fogged ground.
- `Source/Audio/SynthWind.cs` and `Audio.Storm.cs`: a 12 s stereo loop (a low roar, a howl through a narrow band whose
  note climbs with the gusts, a second whistle in the strongest, the hiss of snow; whole cycles over the loop and a
  crossfaded seam), made off the main thread the first time a storm is heard, on one source of its own on the
  mixer's "Storm wind" row (`MixBus.Weather`). Under a roof it drops to about a fifth and the EFX low-pass dulls it.
- `Source/App/Program.Storm.cs`: the frame's glue (after the map's fog is set), the frost drawn at the screen's edges
  under the HUD, and the stop when no world has been drawn for 0.3 s (the menus).

Cost: about ten rays a second and a few dozen uniforms a frame on the CPU, no allocations per frame; on the GPU one
small instanced draw (10,000 thin quads at High) and one full-screen triangle where the sky shows.
