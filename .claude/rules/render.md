---
paths:
  - "Source/Render/**"
  - "Source/UI/ImGuiRenderer.cs"
  - "Materials/**"
  - "docs/render/**"
---

# Rendering: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- **Shared materials** (docs/render/MATERIALS.md). A part box holds a look (`BoxLooks`: colour, tone, material) and a material is a row of `Materials/Surfaces.json`: never store a material in a part, a save, a blueprint or the wire; a row's number is this build's own. Rows 0-6 are the old texture layers. A new shader that draws materials takes `Surfaces.Glsl` after `Lighting.Glsl` and calls `SurfaceLit`.
- **The back buffer's alpha is the bloom's mark** (docs/render/MATERIALS.md "Post effects"). Blended drawing in the world leaves it alone (`Renderer.BlendDefault`, not `BlendFunc`), and an opaque shader writes 1.
- **OpenGL 4.6 / 4.1** (docs/render/OPENGL46.md). The game asks for 4.6 core and falls back to 4.1 (always on macOS; `--gl41` forces it). Every shader source starts with `#version 410 core` and goes through `RenderCaps.Glsl`; 4.6-only GLSL sits in `#if GL46`; 4.6-only code checks a `RenderCaps` flag and keeps the 4.1 branch. Under xvfb llvmpipe gives 4.5, so set `MESA_GL_VERSION_OVERRIDE=4.6 MESA_GLSL_VERSION_OVERRIDE=460` for the 4.6 path. Dev builds log `[gl] error` lines with stack traces.
- **Baked lighting** (docs/render/LIGHTING.md). Every scene shader lights through `ApplyLightAt` and the `LitFrame` block (binding 7, texture units 12-14: don't reuse them).
- Per-frame state holding indices into a construct is stamped when taken and checked against the construct as it is when used (`ConstructRender.GhostLive`, `Program.LinkStamp`, `LinkStateCheck`, `LinkHitUsable`): a tick, a packet or the other peer can shift parts between input and draw, and a hit may name no part.
- "What is under the crosshair" on a moving thing uses the picture just drawn (`PanelRender.DrawnSpot`).
- Everything drawn together is sampled at one tick; when something looks wrong on a client, check the host alone first.
- Game code stamps tracers on `ConstructRender.Now`. Pool slots past the count are stale.
- 2026-11-10: from the shared materials to fix/zombie-far-horde no crowd zombie was drawn: the box FragmentSrc gained `vLocal`/`vLocalN`/`vSize` inputs, `Renderer.Crowd.cs` reuses it without writing them, the link failed once into the log and the F9 counters still counted the prepared instances. A shader that reuses another's fragment code is updated with its inputs in the same change (grep `FragmentSrc.Replace`), and a link failure logs the info log.
- 2026-11-12: a `"work": "smoke"` stack never smoked and a burning part showed no flames: `ConstructRender._fxDt` was only set in `FlamesBegin`, which nothing called, so every `Fx.Share(rate * _fxDt)` was 0; phase 1 shipped the tag without seeing a machine run. A visual effect is done when a `--window-shot` of the running machine shows it, and a per-frame value has a caller before it has users.
- 2026-11-12: a Conveyor Router's way lamps and fence never lit on the Production Showcase: a big body draws parts that never change from a cached batch (`ConstructRenderBatch.StaticBoxes`), and the Router's new box keys weren't in its list of moving or lit parts. A new kind of box drawn by state (lamp, rig, turn) also goes in `StaticBoxes`' list, and is checked on a big body, not a small test build.
- 2026-11-13: a made part's tile was drawn twice (4 of 64) and could keep a panel's unfinished picture: the catalogue atlas's remake (its key changes once after start-up) also cleared the made-part pool, which has pages of its own, and a tile was keyed to its def while a panel's facets arrive later by swapping `d.Boxes`. A cache of something drawn from a run-time def keeps through unrelated rebuilds and follows the def's boxes (`SandboxTiles.Made.cs`), not only the def.
