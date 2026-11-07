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
  `Construct`, saves, blueprints, baked maps and the wire hold nothing: a saved part reads its material through its def, and rows are
  worked out at load. Don't send or hash a row's number or a look's.
- **One table on the GPU.** 256 rows of four vec4s (`SurfaceGpu`): a storage buffer at binding 6 on the 4.6 path, a float texture on
  unit 16 on the 4.1 path. `Surfaces.Glsl`'s `SurfRow` reads either. Changing a material is one row.
- **Which part gets what** (`SurfaceRules`): the box's or part's own `"finish"`, an extrusion's bulk material (`"bulk"`), else the first
  of the file's `"rules"` that fits, on role, material, folder, name, glass, tyre, hue and brightness. No part file needs editing, and
  no generator has to know. Props go by their texture material (`"props"`); a pane or glowing part keeps the classic light.
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
- A part's health comes in the fraction of the row's code (`Surfaces.Code`): rust and scorch grow from noise as it falls.

Low skips all of this (`uSurfQ.x`, in `LitFrame`) and draws what the game always drew. High adds a second noise octave and tilts the
normal by the pattern. Terrain, buildings and map boxes aren't in it.

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

`--gl-bench [map] --bench-bp "A,B" [--bench-copies N] [--bench-wear %] [--bench-dist m] [--bench-fx N] [--bench-mat L] [--bench-particles L]
[--bench-post L] [--bench-full]`: constructs in front of the camera, N particles alive, the settings 0 to 2, and `[glbench] materials:` the
library's sizes.
