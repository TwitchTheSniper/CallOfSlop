# Depth: reversed float depth on the 4.6 path

Hull plates on big ships sit 2-5 mm apart (the z-fighting rule). The window's depth buffer is 24-bit fixed point, and
with a perspective projection its step grows with the square of the distance: about z^2 / (near * 2^24), so 10 cm at
300 m with the first-person near plane of 0.05 m. Past about 100 m those plates fought, hatched patches over the
Mainframe's hull seen from above (Karl, 2026-11-20).

## What the 4.6 path does (`RenderCaps.ReversedDepth`)

- `glClipControl(GL_LOWER_LEFT, GL_ZERO_TO_ONE)`: clip depth runs 0..1, not -1..1.
- `Renderer.GpuProjection` turns every projection given to `SetCamera`/`SetCameraAt` into its reversed form
  (z' = (w - z) / 2: near plane 1, far plane 0), worked in doubles. `Gfx.Proj`, `ViewProj` and `ViewProjRelative` are
  the reversed matrices, which is what every shader uploads.
- Depth test `GL_GEQUAL`, depth cleared to 0.
- The main frame's world is drawn into a scene target (`Renderer.BeginScene`: RGBA8 colour, `GL_DEPTH_COMPONENT32F`
  depth, the window's own sample count, so 4x MSAA stays). `Renderer.Present` puts it in the window before the UI (and
  before any pixel read): a blit with no samples; with samples, a resolve blit into a one-sample texture and a
  full-screen triangle (`PostFx.DrawFullScreen`), since a blit may not write a multisampled window. If the target can't
  be made, it logs once and the world draws straight to the window from then on. A float's
  precision follows the reversed perspective's, so the step at 300 m is a few hundredths of a millimetre.
- Off with `--gl-no=revz` (a start-up switch, not a bench experiment: shaders are built for it). The 4.1 path never
  has it and is unchanged. Thumbnails, camera feeds and the lobby draw into 24-bit buffers with the same reversed
  convention: correct, with the old precision.

## Rules for new code

- A shader that writes clip depth itself uses the prelude's `REVZ`: the far plane is `0.0` under `#if REVZ`, `1.0`
  otherwise (the sky, the snow's far quad); `gl_FragDepth` is `clip.z / clip.w` under `REVZ`, not `* 0.5 + 0.5`; a depth
  texture read back is clip depth as it is under `REVZ` (PostFx's ambient occlusion), and empty is 0 not 1.
- Polygon offsets go through `Renderer.PolygonOffset`, which flips the sign (negative is toward the camera either way).
- Frustum planes from a view-projection take near and far from `Renderer.DepthPlanes`: GL's w + z / w - z do not hold
  for reversed depth (the far plane is z >= 0). A CPU matrix built outside the renderer for those planes goes through
  `Renderer.GpuProjection` first.
- Code that binds "the screen" again inside the frame binds `Renderer.MainFbo`, not 0.
- A projection that skips `SetCamera` (the scope lens, the frenzy glow on the first-person gun) goes through
  `Renderer.GpuProjection` itself, or it fails the depth test against what `SetCamera` drew.

## Measured (cloud, llvmpipe, Mainframe on Proving Ground, 1280x720)

`--gl-bench "Proving Ground" --bench-bp Mainframe --bench-ship-views --bench-ship-far 700 --bench-one --bench-shots`
against `--gl-no=revz`: the far view's hull hatching is gone; the other views match to 0.0-0.2% of pixels; draw calls
are the same; frame times within noise (the copy to the window is one full-screen blit).
