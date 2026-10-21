# Models for Call of Slop

This folder holds 3D models that maps can place with the `Model` op (see [Maps/README.md](../Maps/README.md#meshes-and-models)).
Everything in it is copied next to the game when it's built, and a map refers to a model by its path from the game
folder, for example `"file": "Models/barrel.glb"`.

Models that can be reused belong here. A model that only one map pack needs goes in that pack's own `Models` folder,
`Maps/<Pack>/Models`: a map in the pack finds `"Models/my_door.glb"` there first and here second, and
`"global:Models/my_door.glb"` always means this folder. Pack models a map uses count towards its fingerprint. See
[Maps/README.md](../Maps/README.md#where-assets-go); `Maps/Dev/Models/dev_beacon.glb` (written by
`tools/models/make_pack_models.py`) is an example.

The game reads **glTF 2.0**, the open standard that Blender, Maya, 3ds Max, SketchUp (with a plugin), Substance and most
online model stores export. Both kinds of glTF file work:

- **`.glb`**: one binary file with everything inside. The easiest to pass around.
- **`.gltf`**: a JSON file, with its geometry either embedded (base64) or in a `.bin` file next to it, and textures in
  image files next to it. Keep those files together in this folder.

## Shipped test models

| File | What it is | What it tests |
| --- | --- | --- |
| `barrel.glb` | A red oil barrel with two steel hoops, 0.61 x 0.88 m | Two materials; one hoop mesh used by two nodes |
| `traffic_cone.glb` | An orange traffic cone with white bands, 0.72 m | An embedded PNG base colour texture with texture coordinates |
| `lamp_post.gltf` | A green street lamp on an arm, 3.5 m | Text glTF with an embedded buffer; a five-node hierarchy using both TRS and a matrix |

They are written by `tools/models/make_models.py` (plain Python 3, no Blender needed). Run
`python3 tools/models/make_models.py` from the repository root to rebuild them; the output is byte-identical every time.
The **Mesh Test** map in the Dev pack shows all three.

## Adding a model

1. **Model it in metres, Y up, standing on the ground.** The model's origin is the point the map places, and y = 0 is the
   ground, so put the origin at the middle of the base. Blender's glTF exporter converts its Z-up scene to Y-up for you
   (keep "+Y Up" ticked). 1 Blender unit = 1 metre.
2. **Keep it small.** A few hundred to a few thousand triangles per prop is plenty. Apply modifiers, and apply or keep
   the scale at 1.
3. **Materials:** the game uses each material's **base colour** (the factor and, if set, the base colour texture). Other
   maps (normal, metal/roughness, occlusion, emissive) are ignored, and transparent materials are drawn opaque.
   Textures are PNG or JPEG; the game resizes each to 256 x 256, so square power-of-two images look best.
4. **Export** as glTF 2.0 (`.glb` is simplest). In Blender: File > Export > glTF 2.0, format "glTF Binary", Include
   "Selected Objects" if you only want part of the scene. Don't tick Draco mesh compression: the game can't read it.
5. **Put the file in this folder** and place it in a map: `{ "op": "Model", "file": "Models/my_crate.glb", "x": 4, "z": 2 }`.
   Click **Refresh maps** in the lobby (or **Reload map files** in the Esc menu in single player) to see it.
6. **Check it** with `CallOfSlop.exe --check-map Maps/YourPack/YourMap.json`: a broken model gives a message naming the
   file and what's wrong (a missing buffer file, an index past the end of the vertices, an unsupported extension...).
   The game log (`callofslop.log`) prints one `[mesh] model ...` line per model with its node, mesh, triangle and
   texture counts and any warnings (things that were ignored).

## What the game does with a model

- **Node tree:** the file's node hierarchy (names, translation, rotation, scale, parent, mesh) is kept as it is, so a
  model with a separate door or wheel node keeps it. Meshes used by several nodes are stored once.
- **Drawing:** every mesh goes into one shared vertex and index buffer, and every copy on a map is an instance: 50
  barrels cost the same few draw calls as one. Far-away copies switch to simplified versions (LODs) made by
  meshoptimizer when the model is loaded, and anything outside the view isn't drawn at all.
- **Lighting:** models get the same sun, sky light and fog as the rest of the map.
- **Collision:** none yet. Models are **cosmetic**: players and bullets pass through them. The game already builds their
  collision data (the exact triangles, a box and a simplified convex shape) for when the physics work can use it. Put a
  `Block` or a solid `Prop` where players need something to hide behind.

## What isn't supported

Skinned and animated models are drawn in their rest pose (animations are ignored), morph targets, cameras and lights
are ignored, and files that require compression extensions (`KHR_draco_mesh_compression`, `EXT_meshopt_compression`,
`KHR_mesh_quantization`, `KHR_texture_basisu`) are refused with a message. Only triangle meshes are drawn (no lines or
points).
