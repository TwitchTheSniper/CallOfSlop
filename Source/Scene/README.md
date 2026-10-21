# Scene graph

The presentation-side object hierarchy: things built from parented parts (guns, arms, attachments, players, movers,
kill-streak entities, later vehicles and glTF models). Gameplay, physics and networking don't use it; they keep their
own state and the scene graph only holds how it's drawn.

## Pieces

| File | What it is |
| --- | --- |
| `SceneNode.cs` | A node: name, flags (hidden, socket, gravity frame), local TRS or matrix, parent, children (draw order), cached world matrix, `Renderable`, optional `IRenderable`. Lookups: `FindChild`, `Find`, `FindSocket`, `BuildIndex`; traversal: `Visit` with a struct visitor; `SetParent(parent, keepWorld)`; `GravityUp`. |
| `Scene.cs` | Registry of roots and the dirty queue. `UpdateTransforms()` recomputes only dirty subtrees, each once, from its topmost dirty ancestor. |
| `Renderable.cs` | What a node draws: a unit-cube box (colour, alpha, shading mode, material, blend flags) or a mesh id. `IRenderable` for custom drawables, `IMeshDrawer` for the mesh pipeline, `IDrawRecorder` for headless tests. |
| `DrawList.cs` | `Flatten(root)` / `UpdateAndFlatten(root)` (one walk does both) into a list of node references, `Submit(renderer, meshes)` draws them with the existing `Renderer` (boxes) and `MeshRenderer` (meshes, instanced). |
| `SceneBuilder.cs` | Builds a tree from glTF-shaped records (name, parent index anywhere in the list, TRS or matrix, renderable, sockets). |
| `SceneMeshes.cs` | Mesh handles (`MeshAsset.Id`) and `BuildModel`: a loaded glTF `ModelAsset` (its `ModelNode` list) as a node tree. |
| `ScenePresentation.cs` | The game's scene: the viewmodel, a rig per player and streak entity id, the map's movers. Program calls its Draw* methods where it used to draw immediately. `CALLOFSLOP_SCENEGRAPH=0` switches back to the old code. |
| `Rigs/` | `GunRig` (gun -> body / magazine / optic / muzzle_device / underbarrel groups, sockets grip, support, mag_well, action, optic_mount, underbarrel_mount, stock, muzzle -> flash), `ArmNodes` (shoulder -> upper -> forearm -> hand, hand shapes, hand_r / hand_l sockets, carried magazine), `ViewModelRig`, `PlayerRig` (upper_body -> stances, pelvis -> thigh -> shin -> foot per side posed by `LegAnimator`, upper_gear -> gun and arms), `MoverRig` (+ riders node), `VehicleRig` (seats as sockets, its own gravity frame), `TurretRig`, `DroneRig`. |

## Conventions

System.Numerics row vectors, as the renderer: local = S * R * T and `World = Local * Parent.World`. A node's local
-Z is forward and +Y up. glTF TRS maps straight across; a glTF matrix must be transposed.

## Rules of thumb

- Build trees once (allocates), pose them every frame (doesn't): set locals, hide or show nodes, change colours.
- The child order is the draw order. Rigs keep the old code's order so the pixels are identical.
- A draw list holds node references: flatten, then submit, before transforms change again.
- Animation and IK stay where they were (`ViewModelAnimator`, `ThirdPersonArms`, `ArmRig`); rigs copy their results
  into nodes. A gun's boxes come from `GunModel.Emit`, the same code that draws it immediately.
- `IRenderable.Emit` must not flatten its own subtree into the same list.

## Cost

The self-test ("Scene graph" section) measures 10,000 nodes (100 moving roots of 100 nodes) updated and flattened in
one walk: about 0.15 ms per frame on the CI runner, about 0.07 ms when nothing moved, and a game-like frame (viewmodel,
8 players, movers, a turret: about 1,000 nodes) including the animation in about 30 us, with no allocations.
