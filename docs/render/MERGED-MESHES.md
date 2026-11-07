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
- levers, buttons and lamps (their boxes move or light), dials, beacons and terminals (a lamp box), spawners
- glass (blended, drawn after the opaque world), designed ordnance (its wings fold), a merged gun's parts (drawn bigger)
- a part that is burning or holds stored parts, for as long as it does

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
- **Culling**: a body is culled whole as before (`CullLevel`), then each chunk by its sphere against the view.
- **Memory**: 64 bytes a box on the GPU, plus a few bytes a box on the CPU (each slot's member and box). Bodies not drawn
  for 600 frames give their buffers back.
- Nothing allocates in a steady frame (the GL bench prints the most the constructs pass allocated in one). Planning and
  rebuilds allocate only when something changed.

## Draw groups and fused units

`Renderer.MergedMesh` knows nothing about constructs: it is a set of slots (matrix and look) in one frame, drawn with
`DrawMerged(mesh, frame)` between `BeginMerged` and `EndMerged`. A construct chunk owns one today. A later **fused unit**
(several parts the player fuses into one, with a rough collider and cached mass, each piece keeping its own health) can own
one the same way: fill its slots from its pieces in the unit's frame, write the looks again when a piece's health or paint
changes, and when a cut leaves two units, build two meshes. Nothing in the merged path needs a part to belong to a chunk.

## Part box cap

`ConstructParts.MaxBoxes` is 256 (it was 32 while every box was a draw call). Walked parts still cost a draw per box per
frame, so the parts that move (above) should stay lean; designed ordnance keeps its own cap of 32
(`OrdnanceDesign.MaxBoxes`). An Ordnance Rack now has a hook per station while its boxes allow, and the old rail per column
(a drum: per place round it) past that.

## Measuring

`--gl-bench MAP --bench-bp "A,B" [--bench-copies N] [--bench-capital PARTS] [--bench-edit] [--bench-shots]` adds a "no merge"
configuration, prints the constructs pass's draw calls and CPU (the body loop, then the flush that draws the merged chunks
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
