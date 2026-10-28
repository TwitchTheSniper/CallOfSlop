# OpenGL 4.6 with a 4.1 fallback

The game asks for an OpenGL 4.6 core context and falls back to 4.1 core when the driver cannot make one. Two tiers,
nothing in between: on the 4.6 path a handful of newer features are on (listed below); on the 4.1 path the game runs
exactly as it always did, which keeps the Mac release (4.1 is the most macOS offers) and older drivers working.
Everything here lives in `Source/Render/RenderCaps.cs`, `GlDebug.cs`, `GlCompute.cs`, `StreamRing.cs` and the window
code in `Source/App/Program.cs` (`CreateWindow`).

## The context

`Program.CreateWindow` asks GLFW for 4.6 core (forward-compatible; a debug context in dev builds, not in release
builds and not in `--gl-bench`), and on failure (GLFW raises `VersionUnavailable`) asks again for 4.1 core. It never
asks a Mac for 4.6, and `--gl41` forces the fallback anywhere, so the 4.1 path can be tested on any machine.
`RenderCaps.Detect` then reads what was made and logs one line, for example:

    [gl] OpenGL 4.6 path (context 4.6: 4.6 (Core Profile) Mesa 25.2.8; llvmpipe), features: debug output, labels, buffer storage, multi-draw indirect, compute, DSA; experiments: persistent off, mdi on
    [gl] OpenGL 4.1 path (context 4.5: 4.5 (Core Profile) Mesa 25.2.8; llvmpipe), fallback: the driver could not make a 4.6 core context

`RenderCaps.Gl46` is true only when 4.6 was asked for and made. A driver that hands back a newer context than the 4.1
asked for (Mesa gives 4.5) still runs the 4.1 path: every feature flag (`DebugOutput`, `Labels`, `BufferStorage`,
`MultiDrawIndirect`, `Compute`, `Dsa`) is off unless `Gl46` is on and the entry points loaded. Every 4.6-only piece of
code checks one of those flags, so the 4.1 path never reaches a 4.6 entry point.

## Shaders: one source, two versions

Every shader source in the game starts with `#version 410 core` on its own line and goes through `RenderCaps.Glsl`
before `SetSource`. That swaps the first line for the tier's prelude:

    #version 460 core          |   #version 410 core
    #define GL46 1             |   #define GL46 0

so one source serves both tiers, and anything 4.6-only (`gl_BaseInstance`, `gl_DrawID`, storage blocks) sits inside
`#if GL46 ... #endif`. A source without that first line throws, so a new shader cannot skip the prelude by accident.
The "GL 4.6" self-test section (`Source/App/SelfTestGl46.cs`) finds every shader source by reflection (27 of them),
checks each turns into a valid 4.6 and 4.1 source, that 4.6-only GLSL is guarded, and that the 4.1 tier has every
feature off; it runs headless, without a context. A compute shader (`GlCompute`) is 4.6-only whole and exempt.

## Free wins on the 4.6 path

- **KHR_debug output** (`GlDebug.Install`, dev builds): the driver reports errors, undefined behaviour and performance
  hints as they happen, synchronously, so a `[gl] error` log line carries the failing call's stack trace (the first
  five) instead of a later `glGetError` finding a flag someone set frames ago. Notifications are muted; each message
  id prints at most three times, 200 messages in all. `GlDebug.Error()` is what the renderers' set-up lines and the
  smoke test print: the `glGetError` flag (both paths) or, failing that, the first error the callback caught. The
  first run found a real bug: `SightLens.Timer.Collect` read a timestamp query that had never been stamped
  (`GL_INVALID_OPERATION`: not a query object yet), which `glGetError` had been reporting as a stray `0x502` in the
  decals' smoke line for who knows how long.
- **Object labels** (`GlDebug.Label`): every renderer names its programs, vertex arrays and buffers (`PropRenderer.
  animProgram`, `Renderer.batchRing`, ...), so debug messages and GPU capture tools say what a buffer is.
- **GPU timer queries** were already there (`Source/Render/GpuStampRing.cs`, 3.3 `ARB_timer_query`), and the bench
  uses them for its GPU column.

## Experiments

Each experiment runs only on the 4.6 path, keeps the 4.1 code as its other branch, and has a default:
`--gl-on=NAME[,NAME]` / `--gl-no=NAME[,NAME]` flip one for an A/B (`RenderCaps.Active`). Pictures must not change:
`--gl-bench --bench-shots` draws each configuration and compares the pixels.

| Name | What | Default | Why |
| --- | --- | --- | --- |
| `mdi` | The prop renderer's visible runs (one `glDrawElementsInstanced` each on 4.1, with two `glVertexAttribPointer` calls before it) are queued as `DrawElementsIndirectCommand`s and drawn with one `glMultiDrawElementsIndirect` per pass; the base instance offsets the instanced attributes, so the vertex shader is unchanged (`PropRenderer.QueueRun` / `FlushIndirect`). | on | 90 draw calls become 3 on Ashgrave Cathedral, 34 become 1 on Horde Test; identical pictures; CPU submission equal or a little lower in every run. |
| `persistent` | The box batch's per-frame instance data (horde bodies, players, guns) written straight into a persistently mapped, triple-buffered, fenced ring (`StreamRing`: `glBufferStorage` + `GL_MAP_PERSISTENT_BIT`, DSA when there) and drawn with `glDrawArraysInstancedBaseInstance`, instead of `glBufferData` orphaning + `glBufferSubData` (`Renderer.FlushBatch`). | off | On llvmpipe it costs 1-2.5 ms of CPU a frame on Horde Test (5,800 batched boxes) and shows nothing on Downtown; see below. Worth an A/B on a real GPU with `--gl-on=persistent`. |

Also on the 4.6 path, behind their flags and ready for use: **DSA** (`StreamRing` makes its buffer with
`glCreateBuffers` / `glNamedBufferStorage` when `RenderCaps.Dsa`), and **compute** (`GlCompute`, below). Not tried:
bindless textures (not widely enough supported) and SSBO-for-uniform tricks (nothing needed one).

### Compute: the hook for skinning and culling

`GlCompute.Create(source, label)` compiles a compute shader through the prelude (null on the 4.1 path), `BindStorage`
binds an SSBO, `Dispatch` runs it and `Barrier` makes its writes visible to the next vertex fetch, indirect command
or read-back. `--gl-bench` runs `GlCompute.SelfCheck` first (a kernel that doubles 256 floats, read back:
`[glbench] compute self-check: ok`). Nothing in the game uses it yet. Where it goes:

- **Horde skinning:** `ZombieAnim.Evaluate` poses each zombie on the CPU and `ZombieAnim.Draw` pushes its boxes into
  the box batch (`Renderer.BeginBatch` / `EndBatch`). A compute pass would take one record per zombie (position,
  yaw, gait, mode: the `ZombieAnimInput` fields) in an SSBO, write the batch's instance floats (`Renderer.BatchFloats`
  a box) into a `StreamRing` segment, and the batch would draw from that with the base instance it already uses,
  behind `RenderCaps.Compute` with the CPU path kept.
- **Culling:** the prop and foliage renderers cull chunks on the CPU (`PropRenderer.DrawChunks`, frustum and distance)
  and now emit indirect commands; a compute pass could write those commands itself (frustum, distance LOD) and
  `glMultiDrawElementsIndirectCount` would draw them, with no CPU loop at all.

## Measuring: `--gl-bench`

`CallOfSlop --gl-bench ["Map" ...] [--frames N] [--horde N] [--bench-size WxH] [--bench-shots]` (`Source/App/
Program.GlBench.cs`) opens the window, and for each map (Downtown, Ashgrave Cathedral and Horde Test unless named)
draws the map-select overview and two spawn views at a fixed clock, with a synthetic horde of N zombies (default 300,
posed by the real animation code, drawn through the real batch) in front of the camera on Zombies maps. Each view is
drawn with every experiment on, each one off in turn and all off, the configurations taking turns frame by frame so
load from the rest of the machine falls on all alike, and the log gets per configuration the median CPU time to
submit the frame, the median frame time with `glFinish` and the median GPU time (timestamp queries), the prop draw
calls and runs, and a per-pass CPU/GPU split. Run it as is and with `--gl41`. It uses the smoke test's throwaway save
copy, no debug context and vsync off. `--bench-size 320x180` keeps the pixels cheap so submission shows.

### Numbers (2026-10-28)

Measured in the agent container: Mesa 25.2 llvmpipe (software GL) under `xvfb-run`, on a 2-core box shared with
other agents (load average 3-8 while measuring), 320x180, 30 frames a configuration, 300 zombies. **These are CPU
numbers from a software renderer**, where "GPU" time is more CPU time and a buffer upload is a memcpy; they say
nothing about a real driver's upload or draw-call costs, and the same configuration swung between 17 and 31 ms from
run to run with the box's load. What is solid: draw-call counts and the pictures.

| Map, view | all on | no persistent | no mdi | all off | props: draws for runs (mdi on / off) |
| --- | --- | --- | --- | --- | --- |
| Downtown, mean of 3 views | cpu 1.13, frame 4.73 | 1.13, 4.76 | 1.13, 4.72 | 1.12, 4.74 | no props |
| Ashgrave Cathedral, mean | cpu 30.2, frame 34.1 | 29.2, 34.8 | 31.0, 35.2 | 28.9, 34.3 | 3 for 90 / 90 for 90 (overview), 13 for 92 / 92 for 92 (spawn) |
| Horde Test, overview | cpu 5.27, frame 5.41 | 2.70, 4.90 | 5.68, 5.75 | 2.71, 4.88 | 1 for 34 / 34 for 34 |
| Horde Test, spawn0 (5,816 batched boxes) | cpu 10.8, frame 14.3 | 8.40, 14.3 | 11.3, 14.8 | 8.46, 14.5 | 1 for 32 / 32 for 32 |
| Horde Test, spawn1 | cpu 7.90, frame 13.3 | 6.63, 13.7 | 8.41, 13.7 | 6.57, 13.5 | 1 for 3 / 3 for 3 |

(ms, medians; "frame" includes `glFinish`.) Pictures: every configuration matched "all on" to within one channel
level on a few pixels (MSAA resolve order), 0 differing on Horde Test. The same views with `--gl41` (the 4.1 path,
`--frames 30`): Downtown cpu 1.21 / frame 3.81, Ashgrave 22.7 / 26.7, Horde Test 5.87 / 10.3: no regression beyond
the run-to-run noise (a later Ashgrave pair gave 4.6 path 17.2 and 23.8, 4.1 path 31.2 and 18.5).

The `--light-shots` pictures of Ashgrave Cathedral match between the two paths: 4 of 6 byte-identical, the other two
differ in under 0.07% of pixels by one channel level (the GLSL 460 and 410 compilers order the lighting maths a
little differently).

## Testing

- **Headless:** the "GL 4.6" self-test section (every push, CI Linux).
- **With a window, no GPU:** `xvfb-run -a -s "-screen 0 1280x720x24" dotnet bin/Release/net10.0/CallOfSlop.dll
  --smoketest` (or `--light-shots`, `--gl-bench`). Mesa llvmpipe makes a 4.5 core context at most, so the game takes
  the 4.1 path by default there; `MESA_GL_VERSION_OVERRIDE=4.6 MESA_GLSL_VERSION_OVERRIDE=460` makes it give 4.6 and
  the game runs the 4.6 path with every feature on (KHR_debug included: the smoke test passes clean). Test both.
- **On a real GPU:** `--gl-bench` as is and with `--gl41`; `--bench-shots` to compare the pictures.
- CI does not open a window; the Linux and Windows self-tests cover the code that needs no context.
