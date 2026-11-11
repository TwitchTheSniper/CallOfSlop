# Materials and effects

Developer request (2026-11-06): shared materials, GPU particles and post effects, using the newer OpenGL where it pays. Code:
`Source/Render/Surfaces.cs`, `Source/Render/Fx/` (`Fx.cs`, `FxGpu.cs`, `PostFx.cs`), `Source/Render/ConstructRenderFlames.cs`,
`Source/App/Program.Effects.cs`. Data: `Materials/Surfaces.json`, `Materials/Particles.json`.

## Materials

A material is one row of `Materials/Surfaces.json`: roughness, metalness, how much of the box's own colour it keeps, a pattern,
edge wear, how it rusts, and the row a painted box uses. Rows keep their order and new rows go at the end. Rows 0 to 6 are the old
texture layers (`enum Material`), drawn the classic way, so a map box's `Material` is a row number already.

- **A reference, never a copy.** A part box holds a `ushort` look (`BoxLooks`: colour, tone, material, each combination stored once);
  a prop vertex holds the row in the top byte of its packed colour (`PropMeshes.Pack`); an extrusion's boxes are part boxes.
  `Construct`, saves, blueprints, baked maps and the wire hold no row: a saved part reads its material through its def, and rows are
  worked out at load. Don't send or hash a row's number or a look's. A part a player textured (`CPart.Finish`, a row in memory) goes
  to saves, blueprints and the wire by the material's **name**, so a row's name is 1 to 32 plain ASCII characters and is
  never renamed lightly (a name this build doesn't have reads as the part's own materials).
- **One table on the GPU.** 256 rows of four vec4s (`SurfaceGpu`): a storage buffer at binding 6 on the 4.6 path, a float texture on
  unit 16 on the 4.1 path. `Surfaces.Glsl`'s `SurfRow` reads either. Changing a material is one row.
- **Which part gets what** (`SurfaceRules`): a part a player textured (the texture paint tool, docs/sandbox/CONSTRUCTS.md: `CPart.Finish`,
  carried by name), else the box's or part's own `"finish"`, an extrusion's bulk material (`"bulk"`), else the first
  of the file's `"rules"` that fits, on role, material, folder, name, glass, tyre, hue and brightness. No part file needs editing, and
  no generator has to know. Props go by their texture material (`"props"`); a pane or glowing part keeps the classic light.
- **Interior rows** (2026-11-11, docs/sandbox/INTERIORS-HISTORY.md phase 12): deck plate, grating, wall panelling, worn and rusty paint, rubber
  matting, lagging, hazard paint, soot, with painted kinds that keep their pattern. Interior parts name them; the interior builder gives
  the Building shell its faction's by a step's `"finish"`.
- **Paint is a tint over a material:** `ConstructPaint.BoxColour` as ever, and `Surfaces.OnPaint` swaps to the row's `"painted"` row, so
  painted steel stops being metal but keeps its panel seams and worn edges.

### Shading

`SurfaceLit` (in `Surfaces.Glsl`) is the box and prop shaders' path for rows from 7 up: `ApplyLightAt` for the diffuse (the baked grid
and `LitFrame` as ever), then Schlick's fresnel over `SkyGradient` along the bounce for the sky's reflection, dimmed by the cell's
ambient, and a normalised Blinn-Phong lobe for the sun and the baked grid's main light. Everything else is maths on what the pixel
already has, with no textures:

- `vLocal`, `vLocalN`, `vSize` (the point in the box in metres, its face, its size) keep detail on a moving part, which a world-space
  texture can't, and give the distance to the box's edges, which is the edge wear.
- Patterns: brushed, speckle, grain, weave, tread, panel. They fade out between 18 and 60 m, so a far hull pays for the light only.
- **Filtered by the pixel** (2026-11-07, see "Shimmer and the grid" below): every detail fades to its own mean as it gets finer
  than a pixel, from the face's uv's screen derivatives; lines (seams, grooves, worn edges) are drawn with their cover spread
  over the pixel. Nothing crawls as the camera moves.
- **Panel** fits whole panels of about 1/scale a side to each box, with seams only between them, so a seam never cuts across a
  box at random; a box's own edges show through the edge wear. Painted steel has no seams (a faint orange peel instead).
- A part's health comes in the fraction of the row's code (`Surfaces.Code`): rust and scorch grow from noise as it falls.

Low skips all of this (`uSurfQ.x`, in `LitFrame`) and draws what the game always drew. High adds a second noise octave and tilts the
normal by the pattern. Terrain, buildings and map boxes aren't in it.

### Shimmer and the grid (user report, 2026-11-07)

A video showed surfaces crawling as the camera moved, a grey panel with moire stripes that changed from frame to frame, and
hull plates, wall plates and door panels covered in a grid of lines as if they were 1 m blocks.

- **The grid** was the "panel" pattern of **painted steel**: a seam every 50 cm in the box's own space, offset by a hash of the
  box's size, so the lines bore no relation to the part's edges. Every part in Hull, Body and Doors is painted steel by rule, and
  every painted steel, aluminium, copper or brass part turns to it under paint, which is why plates and doors showed it and blocks
  didn't stand out. Painted steel now has no seams; the panel pattern is box-fitted and only "plated steel" and "painted plating"
  (rows for the texture paint tool) use it.
- **The shimmer** was the procedural patterns, which had no filtering at all: brushed steel's streaks are 2.4 cm apart (42 a
  metre), weave 40 a metre, the panel seam 1.8 cm wide and the edge wear 6 mm, all drawn at full strength out to 18 m and fading
  only by distance after that. Past a few metres, and at grazing angles much nearer, a pixel covers several streaks, so each frame
  sampled a different one. Every pattern now takes the pixel's size on the face (`fwidth` of its uv, taken before any branch
  that differs between pixels) and fades each detail to its mean once it is finer than about half a pixel (`SurfKeep`); lines
  are box-filtered (`SurfLine`); the edge wear spreads its band over the pixel at its share of the cover; High's tilted normal
  takes its slope over at least a pixel; the rust noise's fine octave fades the same way.
- **Textures**: the old texture layers (map boxes, terrain, prop texture rows) had mipmaps already; their anisotropic filtering is
  now asked for through `RenderCaps.Anisotropy`, which reads the driver's limit and asks only where it is core (4.6) or the
  extension is there (4.1: `GL_ARB/EXT_texture_filter_anisotropic`), so the 4.1 path can't raise an invalid-enum error. The decal
  and mesh texture arrays go through it too.

Measured with `--gl-bench "Construct Yard" --bench-wall --bench-move 8 --bench-dist 3 --bench-view "0,1.7,0,0,1.0,-10"`: a floor
and a wall of 2 m steel plates, half painted grey, the camera gliding 8 mm a frame, and the mean second difference in time of
each channel over the middle of the picture (`[glbench] shimmer`: a pattern that crawls makes it jump; llvmpipe, 960x540, 14
frames). Low draws no patterns, so it is the floor (edges, terrain):

| Materials | Before | After |
| --- | --- | --- |
| Low (no patterns; its code didn't change, measured once) | 0.887 | 0.887 |
| Medium | 1.318 | 0.972 |
| High | 2.037 | 0.987 |

So what the patterns added fell from 0.43 to 0.09 on Medium and from 1.15 to 0.10 on High; the 4.1 path measured 0.986 on
Medium. The plated floor's grid is gone in the pictures; brushed streaks still show within a few metres and fade smoothly
beyond. Not measured: a real GPU, where MSAA and the driver's own filtering differ, and Windows.

### Big bodies

Constructs' static parts are merged meshes (docs/render/MERGED-MESHES.md): a box is a slot with its matrix (48 bytes) and its
look (tint and material code, 16 bytes) on the GPU, and the row is per slot, so no material ever splits a draw. The old body
batch (`Renderer.BodyBox`, 32 bytes a plain box of bodies of 64 parts or more) is gone.

## Effects

One pool, one kind table (`Materials/Particles.json`), one way in: `Fx.Emit` and the helpers (`Fx.Blast`, `Fx.Impact`, `Fx.Muzzle`,
`Fx.EngineSmoke`, `Fx.Flame`). Effects are worked out on each peer from what it has; nothing goes on the wire and nothing touches
the simulation. A frame's births go into a fixed array of 8,192 (more are dropped and counted); nothing allocates.

- **4.6 path** (`RenderCaps.GpuParticles`, experiment `gpufx`): births go through a persistent `StreamRing` to a compute pass that writes
  them into the pool (a storage buffer, 16,384 / 65,536 / 131,072 by setting), a second pass steps every slot and lists the living
  (`atomicAdd` on the indirect command's instance count), and one `glDrawArraysIndirect` draws the list, pulling each particle in the
  vertex shader. The CPU touches a particle once, at its birth. With nothing alive and nothing new no pass runs at all.
- **4.1 path, or `--gl-no=gpufx`:** the same births, kinds and shaders. `Fx.StepCpu` moves 512 / 1,024 / 2,048 of them and they go to one
  instanced draw; only where a particle comes from differs (`FX_PULL`). It emits 30% of the density.
- Particles are premultiplied light plus cover, in one draw with dual-source blending (core since 3.3): `glow` 1 only adds, 0 hides
  what is behind it. Unsorted: smoke over smoke can show its order. Not soft against the world: a puff meets a wall with a hard line.

### Flames by data

Any part gets a flame with a line in its file; no code:

    "flame": { "at": [0, 0, 1.2], "dir": "+z", "radius": 0.4, "length": 5, "kind": "jet", "source": "thrust", "colour": "#ffd0a0" }

`"at"` and `"dir"` are required; a list gives several nozzles. `"kind"`: flame, jet, rocket (any kind in the table). `"source"`: **thrust**
(`Construct.ThrustShow`), **burner** (the burner's level), **engine** (its load), **signal** (the part's output), **always**. A thruster or
burner with no block gets one from its own numbers. To burn by a new state, add a case to `ConstructRender.FlameLevel`.

Flames emit from every construct in `ConstructRender.Draw`, seen or not, with the speed of what they are fixed to, and put a spot of
heat haze over themselves. A client has no burner levels (the pose has no room for them): the host sends the hottest burner in the pose's
smoke share, with no change to the wire's format, so a balloon's flame shows on every peer. A client's flame on a craft that also has a
smoking stack burns at the higher of the two.

## Post effects

Each is a setting, and Low makes nothing. The world is still drawn straight into the multisampled back buffer. After the effects, the
back buffer is resolved once into a texture, and everything works from that at small sizes.

- **Bloom** blooms only what glows, with no HDR target and no brightness threshold (a white wall in the sun doesn't bloom). What glows
  marks the back buffer's alpha while the world is drawn: opaque unlit boxes (`Renderer.MarkGlow`), prop glow, panels (a constant
  share), tracers (`BlendGlowing`), particles (their alpha). `Renderer.BlendDefault` leaves alpha alone, so all other blending must use
  it. It goes back to 1 after the pass. With no alpha in the back buffer it falls back to the brightest pixels.
- **Heat haze** (`PostFx.AddHaze`, 32 a frame): small quads in one instanced draw that read the copy a little off to the side.
- **Ambient occlusion** (High): the depth buffer resolved, 8 samples at half size, a small blur, multiplied in before the effects. It is off
  by itself, with a log line, if the driver can't resolve depth.

`PerfGpuPass`: Ambient occlusion, Particles, Bloom and heat haze are their own lines in the F9 window; `--gl-bench` has `particles` and `post`.

## Costs

Measured 2026-11-06 in the agent container: Mesa llvmpipe (software GL), 640x360, 2 cores shared with other agents. "Base" is main before this
work with the bench added. The same setup swung 20 to 30 ms from run to run, so only differences that hold over all runs mean anything,
and **none of this is a real GPU's cost**: a resolve, a compute dispatch and every fragment are CPU work here.

Median frame with `glFinish`, ms, three runs each (no particles alive; Materials Medium here is Post Low):

| Scene | Base | All Low | Materials Medium | Materials and Post Medium |
| --- | --- | --- | --- | --- |
| Construct Yard, 12 bodies, 1,182 parts | 30.2, 20.8, 21.8 | 24.2, 18.7, 21.4 | 30.1, 20.3, 19.5 | 32.7, 23.4, 35.1 |
| Ashgrave Cathedral | 86.5, 103.4, 107.5 | 70.3, 65.1, 106.1 | not run alone | 73.9, 71.0, 77.3 |

- **The idle pool costs nothing:** the particles pass reads 0.00 to 0.08 ms of CPU and 0.00 to 0.07 ms of GPU timer with nothing alive.
- **All Low hasn't lost anything** against base on either scene.
- **Materials Medium** is within the noise in the yard at this size, where parts cover little of the screen. Filling the screen costs more:
  with parts in close-up and the pool still stepping, an earlier run put the constructs pass at 22.7 to 23.8 ms of GPU timer against 15.3 to
  17.4 on Low, and High at 27.1 to 27.9.
- **Post effects are the one line that shows:** bloom and haze on Medium take 3.6 to 5.1 ms of GPU timer plus 5.0 to 9.6 ms of CPU clock a frame
  (the resolve, on the CPU here). So **Post effects default to Low**; Materials and Particles default to Medium. A real card should do far
  better, but nothing here shows that: `--gl-bench --bench-post 1` on one decides whether Medium can be the default.
- **Particles alive** are all software fragments here: about 20,000 alive at Medium made a 111 ms frame, about 60,000 a 310 ms one. Earlier
  run, under more load; useful only to show that overdraw, not the step, is what they cost.
- **4.1 path:** 23.8 ms base, 34.9 ms with Materials and Post Medium and a small particle load (one run).

Memory:

| What | Before | After |
| --- | --- | --- |
| A part box (11,961 of them) | 128 B | 108 B, plus 1,704 shared looks at 20 B |
| A body batch's plain box | 88 B on the GPU and 88 B in memory | 32 B on the GPU (now a merged mesh slot: 64 B on the GPU, docs/render/MERGED-MESHES.md) |
| A prop vertex | 48 B | 40 B |
| The material table | none | 16 KB |
| The particle pool | none | 3.1 MB at Medium, 6.3 MB at High, 1.2 MB for the births; made on the first frame that draws them, at any setting |

## Measuring

`--bench-wall` adds the plated floor and wall above; `--bench-move MM` glides the camera MM millimetres a frame (sideways, and half
of that forward) and prints `[glbench] shimmer`; `--bench-view "ex,ey,ez,tx,ty,tz"` replaces the views with one, relative to the
first spawn; `--bench-one` draws only "all on".

`--gl-bench [map] --bench-bp "A,B" [--bench-copies N] [--bench-wear %] [--bench-dist m] [--bench-fx N] [--bench-mat L] [--bench-particles L]
[--bench-post L] [--bench-full]`: constructs in front of the camera, N particles alive, the settings 0 to 2, and `[glbench] materials:` the
library's sizes.
