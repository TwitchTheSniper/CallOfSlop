# Merged meshes

A construct's static parts are drawn as a few GPU-resident **draw groups** instead of box by box, so the cost of a frame no
longer grows with how many boxes the parts have. It is render only: the same on a host and on clients, nothing on the wire
or in a save, and `--no-merge` draws everything the old way (the GL bench compares the two pictures pixel by pixel). Code:
`Source/Render/ConstructRenderBatch.cs` (what goes where, when it is rebuilt) and `Source/Render/Renderer.Merged.cs` (the GL
side).

## What is merged

Each rigid moving piece of a construct (a **segment**: the body itself, or what hangs on a joint, a freeform weld's piece) is
cut into **chunks** of space, `MergeCell` (32 m) a side in the segment's frame and at most `MergeChunkBoxes` (2,048) boxes. A
chunk's boxes sit on the GPU in the segment's frame and are drawn at the segment's pose this frame (`SegMatrix` times the
body's matrix), so doors, turrets and arms on joints move as one piece without a rebuild.

A part is merged when nothing about it moves or changes on its own. These are walked every frame as before
(`StaticBoxes`, `MergeBoxes`):

- joints and rails (their leaf side moves, an overloaded one is tinted), suspensions and every box with a rig, wheels and
  track units, rotors and spinning or rolling boxes, a modular engine's parts (they rock and pump), control surfaces
- levers and buttons (their boxes move or light), dials, beacons and terminals (a lamp box), spawners, machines with work boxes
- a lamp set to blink, a merged gun's parts (drawn bigger), an event door's parts, a surface a flush pod sinks into
- a part that is burning, for as long as it does

Merged all the same since 2026-11-20 (the 1 km Mainframe walked 28,000 boxes a frame, most of them still: `--gl-bench
... --bench-walked` lists a body's walked parts by type and why):

- **A part that holds stored parts** (racks, lockers, shelving): its own boxes are in its chunk; only what it holds is
  drawn each frame (`MergeSet.Extra`, `DrawStored`).
- **A part with glass**: its opaque boxes are in its chunk; only its panes are drawn each frame, blended as before
  (`DrawPanes`).
- **Lamps**: a lamp's lit look (on or off, its colour) is part of its chunk's looks. A chunk with lamps is checked on
  every check (4 drawn frames), so a row of lamps across chunks switches together, and every frame while a lamp's
  configure panel is open on that body, so the colour preview follows the slider. A lamp with glass is walked.
- **Designed ordnance**: merged with its wings folded, as it sits until it drops (`FoldWing` in `BuildChunk`).

A body with fewer than `MergeMinBoxes` (24) static boxes is walked whole: its own draws would cost more than its boxes do
in the shared instanced batch. Outlines, ghosts, selection and the frozen outline are drawn over the top as before.

## On the GPU

A chunk is a `Renderer.MergedMesh`: a box is a **slot**, with its model matrix in the chunk's frame (three vec4s, the
translation in w: 48 bytes) in one buffer and its **look** (tint and material code; a glowing box has a negative code: 16
bytes) in another. Both are per-instance attributes over the box buffer's own cube and part shapes. Slots are sorted by
shape, plain boxes first, and each shape group is one `glDrawArraysInstanced` (GL 4.1 has no base instance, so a group's
draw points the attributes at its first slot). A chunk is therefore one draw for its plain boxes plus one for each kind of
contoured shape in it. The vertex shader rebuilds the instance matrix and the fragment shader is the box shader's own, so a
merged box is lit, textured, worn, fogged and glow-marked exactly as it would be drawn alone; edges can differ by a pixel
where the float maths runs in another order. One program for both GL tiers (`#version 410 core` through `RenderCaps.Glsl`).

## Keeping it right

- **Looks** (paint, texture, damage, frost): a hash over each chunk's parts is checked every `MergeCheckFrames` (4) draws of the
  body, half the chunks at a time, so a change shows within 8 drawn frames; only that chunk's looks are written again (16
  bytes a box), never its geometry. A body out of view isn't checked until it is drawn again.
- **Edits**: a rebuild (`Construct.RebuildStamp`) or a change in which parts are types, fires or stores (`DynKey`) plans
  the body again: every part's chunk, and a hash of each chunk's parts' types, places and turns in order. A chunk whose hash
  is unchanged keeps its GPU data (a removed part elsewhere only renumbers its members: slots name a member's place, not a
  part index); the rest are rebuilt.
- **Budget**: at most `MergeBudgetBoxes` (8,192) boxes are rebuilt a frame (one chunk always goes); a chunk waiting its
  turn is drawn part by part that frame, so a big edit never stalls and never shows a stale shape.
- **Culling**: a body is culled whole as before (`CullLevel`), then each chunk by its sphere against the view. A walked
  part of a merged body is skipped when its own sphere is out of view (`WalkSphere`), unless it moves on a joint, a door or
  a wheel, burns or smokes.
- **Memory**: 64 bytes a box on the GPU, plus a few bytes a box on the CPU (each slot's member and box). Bodies not drawn
  for 600 frames give their buffers back.
- Nothing allocates in a steady frame (the GL bench prints the most the constructs pass allocated in one). Planning and
  rebuilds allocate only when something changed.

## Far detail

A chunk's slots are sorted by shape, then by **size class**, biggest first (`SizeClass`: the square root of a box's two
biggest sides, in five classes at 4 m, 1 m, 25 cm and 6 cm; a glowing box or a lamp's two classes up, so lights read from
afar). `MergedMesh.GroupClassEnd` holds, for each shape group, how many of its first slots are of each class or bigger, so
a far chunk draws only the head of each group: no second copy of anything, no rebuild as the eye moves. A class is left
out once its biggest box would be under `LodPixels` pixels (1; the projection's scale and the view's height give pixels a
metre) at the chunk's nearest side. On a 1080p view at the default field of view that is 6 cm boxes past about 50 m,
25 cm past 190 m, 1 m past 770 m. Video settings: **Far detail** (Low 2.5 pixels, Medium 1, High: every box at any
distance); `--no-chunk-lod`, `--lod-px N`.

## Occlusion

`ConstructRenderOcclusion.cs`. After the opaque constructs are drawn, each merged chunk of the main view with at least
`OcclusionMinSlots` (48) slots has its bounds (half a metre bigger) drawn as a box with colour and depth writes off inside
an occlusion query. The next frame draws that chunk inside `glBeginConditionalRender(query, GL_QUERY_NO_WAIT)`: the GPU
skips it if no sample of its box passed, and the CPU never reads a result or waits. From outside, a ship's inside costs
the GPU nothing; from a room, what is behind its walls costs nothing.

- **Holes**: a query tests against the depth actually drawn, so a hole shot or cut in a wall shows what is behind it on
  the next frame. Glass is not in the depth when the queries run, so what is behind a window is always seen.
- **Not tested**: a chunk whose sphere is within `OcclusionNear` (4 m) of the eye (a box round the eye is cut by the near
  plane); small chunks (a query costs about what they do); the camera feeds' passes (not the view of the frame before).
- **Stale results are never used**: a chunk is drawn untested unless its query was issued the occlusion frame before
  (`MergedMesh.QueryFrame`) and the eye has moved under `OcclusionEyeMove` (2 m) in that chunk's own frame since
  (`OcclusionEyeSteady`: it holds whatever the render origin is, and covers a camera cut, a third person toggle, a ship
  passing at speed, a snapped pose and a segment on its joint). A rebuilt chunk forgets its query, and a frame after the
  eye turned about 10 degrees, or after a quarter of a second with no world drawn (a minimised window), draws everything.
  `--gl-bench ... --bench-lag [--bench-origin-eye] [--bench-jump N --bench-jump-by M] [--bench-move MM] [--bench-turn MDEG]`
  draws every frame both ways and counts the pixels that differ (2026-11-20: 0 on 150 m cuts every 4th frame).
- **The cost of being a frame late**: a chunk that comes out from behind something is drawn one frame after it could first
  be seen. If that ever shows as flicker at an edge, raise `OcclusionPad`; Video settings: **Skip hidden parts**;
  `--no-occlusion`.

The CPU still submits a hidden chunk's draw calls (it doesn't know the answer); what is saved is the GPU's vertex and
pixel work. Ways on: read the results back a few frames late to skip the calls too, and rooms and portals for interiors.

### Numbers (2026-11-20, the Mainframe: 5,684 parts, about 1 M triangles; Mesa llvmpipe under xvfb, 640x360, the 4.6 path)

`--gl-bench "Proving Ground" --bench-bp Mainframe --bench-ship-views --bench-shots` (views far off, alongside and three
inside along its length, the ship standing still; configurations "no occlusion" and "no far detail" beside "no merge").

| | Before | After |
| --- | --- | --- |
| Boxes walked one by one each frame | about 28,000 | 363 parts' moving boxes |
| Construct draw calls (alongside / inside) | 5,155 | 1,909 / 2,152 |
| Body walk CPU | 44 ms | 1.8 to 2.9 ms |
| Triangles sent (alongside / inside) | about 1,000 k | 342 k / 342 k (537 k / 769 k without far detail) |
| Chunks the queries found hidden (alongside / inside) | | 28 of 33 / 65 of 66 tested |
| Frame (alongside / inside) | 80 ms | 21 ms / 29 ms (39 / 43 without occlusion) |

Pictures: "no occlusion" matched pixel for pixel in every view; "no far detail" differed by at most 7 of 230,400 pixels
(sub-pixel lamps); "no merge" by up to 257 (0.1%), on rows of lamps a pixel or less tall, where the two paths round edges
differently. llvmpipe draws on the CPU, so these frame times say what work went, not what a GPU gains: measure on the PC.

## Draw groups and fused units

`Renderer.MergedMesh` knows nothing about constructs: it is a set of slots (matrix and look) in one frame, drawn with
`DrawMerged(mesh, frame)` between `BeginMerged` and `EndMerged`. A construct chunk owns one.

A **fused unit** (docs/sandbox/CONSTRUCTS.md, "Fusing") owns its own chunks: a fused part's chunk key holds its segment
and the low bits of its unit's tag in place of the segment alone (`ConstructRenderBatch.Plan`), still cut into 32 m cells
and 2,048 boxes, so a unit is drawn as its own merged meshes and an edit beside it (a part added or shot off another
piece) never plans its chunks again. A piece of the unit breaking off rebuilds only the chunk it was in; when a cut makes
two units, their parts go into chunks of their own at the next plan. Looks (paint, damage, frost) are rewritten as for any
chunk. What a part needs to be merged is unchanged: a unit's special parts that move or light are walked as before.

Measured with `--gl-bench --bench-capital 15000 --bench-fused` against the same without `--bench-fused` (llvmpipe, 640x360,
2026-11-07): the same 2 merged draw calls in the overview and 6 at the spawns, the body walk 0.16 to 0.18 ms either way, the
frames within the run-to-run noise. Fusing changes nothing in a steady frame; what it keeps from being redone is the plan
of the unit's chunks after edits elsewhere.

## Part box cap

`ConstructParts.MaxBoxes` is 256 (it was 32 while every box was a draw call). Walked parts still cost a draw per box per
frame, so the parts that move (above) should stay lean; designed ordnance keeps its own cap of 32
(`OrdnanceDesign.MaxBoxes`). An Ordnance Rack now has a hook per station while its boxes allow, and the old rail per column
(a drum: per place round it) past that.

## Measuring

`--gl-bench MAP --bench-bp "A,B" [--bench-copies N] [--bench-capital PARTS] [--bench-edit] [--bench-shots] [--bench-walked]
[--bench-ship-views]` adds "no merge", "no occlusion" and "no far detail"
configurations, prints the constructs pass's draw calls and CPU (the body loop, then the flush that draws the merged chunks
and the box batch), the merged meshes' chunks, slots and GPU bytes, and with `--bench-shots` compares every picture with the
merged one. `--bench-edit` paints, damages and removes parts while it draws, the same in every configuration.

### Numbers (2026-11-07, Mesa llvmpipe under xvfb, 640x360, the 4.6 path; medians of 16 frames)

"Before" is main as it was (a body of 64 parts or more had one instanced draw for its plain frames and panels, the rest box
by box); "after" is merged meshes. The constructs pass's CPU is the walk over the bodies (our code, plus the draws of what
is still walked); on llvmpipe the vertex work of the merged draws runs on the CPU in the flush, where a real GPU does it
alone, so the flush and the frame times here are not what a GPU would show.

| Scene | Draw calls (constructs) | Body walk CPU | Frame (llvmpipe) |
| --- | --- | --- | --- |
| Mixed fleet: 9 faction vehicles x 4 (36 bodies, 3,768 parts) | 1,204 -> 958 | 28-32 ms -> 2.6-2.8 ms | 38-45 -> 33-46 ms |
| Buses: Double Decker 13 and IT War Bus 6 x 16 (32 bodies, 6,560 parts) | 3,861 -> 3,137 | 66-105 ms -> 23 ms | 78-116 -> 104-124 ms |
| Capital hull, 4,050 parts (the self-test's) | 1 -> 6 | 14-15 ms -> 0.05 ms | 42-45 -> 64 ms |

The frame columns were measured minutes apart on a shared 2-core box with a load of 6 to 15, so they move by tens of percent
on their own; within one run, merged against `--no-merge`, the merged frame was lower in every scene. Most of the draws left
in the bus scene are merged shape groups (2,324 of 3,137: about 15 segments a bus, a draw per contoured shape in each); the
rest are walked parts and glass. Memory on the GPU: 64 bytes a box (1.2 MB for the 32 buses, 1.4 MB for the hull's 22,275
boxes). Nothing allocated in a steady frame. Pictures merged against `--no-merge`: at most 24 of 230,400 pixels differed, on
edges (float rounding), the same on the 4.1 path; with `--bench-edit` (paint, damage, parts removed) 2 pixels.

Not measured: a real GPU (desktop or Apple), Windows. Ways on: fold freeform segments into the segment they are rigid with
(fewer chunks), one draw for all of a chunk's shapes (vertex pulling, or multi-draw indirect on 4.6).
