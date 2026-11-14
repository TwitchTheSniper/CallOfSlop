---
paths:
  - "Source/Constructs/**"
  - "Source/Game/GameSandbox*.cs"
  - "docs/sandbox/CONSTRUCTS.md"
  - "docs/sandbox/constructs/**"
---

# Constructs: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule. Drive lines, production machines, the network, physics and riding have files of their own beside this one, which load with their code.

- **Extrusions** (docs/sandbox/constructs/production.md "Extrusion"). A piece an Extruder, a Press or a Wheel Former makes is a `ConstructPartDef` made at run time, one per key (shape, section, size, length, material): its id (from `Extrusion.RuntimeBase`) is this machine's own and isn't in `ConstructParts.All`, so read a part's def through `CPart.D`, `DefOf` or `ConstructParts.Def`, never `All[id]`, and on the wire or in a hash use its `ExKey`, never its id.
- A part given to a player goes through `ConstructEdits.Give` (hands, backpack, drop).
- Try a part-onto-part feature through the player's whole chain (menu, PlaceNew, loose / frozen / mounted, the client's ghost, the Attach message, the server's result) for hand-made AND generated parts. Sweep a "won't attach" report over every receiver and category, not one pair.
- A "don't move into X" check refuses only moves that make a NEW overlap and skips what the body is joined to. Try held bodies off centre, on the floor and coupled.
- A visual change of a part's size also changes its collider, player blocking, ray hit, outline and ghost checks: solid shape goes through `Construct.SolidCount/SolidBox/ShapeBounds`.
- Scaled sockets apply recursively to every part fitted on a scaled part, so try merges with a part that has its own sockets (a splitter), not only a barrel.
- A fixed-pose (freeform) segment is rigid with its parent for every system: ask `Construct.MoveSeg/Rigid/FrameOf`, never `Seg != 0`. Compare link points in the rest frame (`ToRest`); sockets meeting across a seam are `SeamLinks`.
- Welder and wrench share the leaf code (`ConstructBuild.OnLeaf/LeafRefusal/HangOnLeaf`). Try a weld path with every role whose behaviour depends on its links (wheels, suspensions, joints, drive line), driven, not only posed.
- A part's behaviour is gated on its own inputs and links, never on the construct being a vehicle (a seat, free, awake). An optimisation that skips idle bodies wakes them on an input or power change (`ConstructPhysics.EngineLive`).
- **Blueprints and bodies.** A save records what is: steps' `"at"`, links' `"laid"`/`"hosts"`, `"exactLinks"`, a step's `"also"`, points relative to step 0. Round trips compare parts, joints, links and bodies, on selections of real vehicles too.
- Carrying parts across bodies remaps every joint index (`Hinge`, `Child`) and moves only what sits in the body frame (`FrameOf(seg) == 0`); try a join with a turned welded piece and read it back as a client.
- Points on a part go through `SegToWorld`; reach is one helper per action shared by client and server (`AttachReach`, `WeldReach`, `ReachPoint`).
- 2026-11-10: every headless run failed or hung in `World`'s static initializer: a shell gun built while the blueprints loaded read `World.GravityScale`, the initializer loaded the maps, a sandbox map looked up a blueprint and re-entered the blueprints' Lazy. A static initializer never loads data that can lead back to its caller: take it on first use (`World.Map`).
- 2026-11-10: event doors (plain "hang" socket, role door) had no usable snap point, since SnapBuild made every door single-purpose by its hinge leaf. A part family with a new way to attach is classified in `SnapBuild.Build` in the same change.
- 2026-11-11: a new CfgKey with "options" was refused ("options belong to a choice") and its part dropped, which also dropped its recipes and blueprints; `--check-construct-parts` prints to callofslop.log, not stdout, so the refusal went unseen. A new choice key goes in `ConstructConfig.DefaultType` (or its part file says `"type": "choice"`); read the REFUSED lines in the log, not the console.
- 2026-11-11: every working production machine drew 0 W in survival: a Data out set `Monitor`, which made `HasOutput` and so `IsSignal` true, and `ConstructPower.Step` counts a signal part's draw only while its `Out` is on. A flag that changes what a part is (`HasOutput`, `IsSignal`, roles) is checked against every reader of it (`grep -n "\.IsSignal"`).
- 2026-11-12: main's full suite went red with a day's production merges, all on branches that ran only their own sections (a screen became its line state; machines gained a Data out; tier cylinders made 52 pieces; the tile atlas gained materials after the tools; round wheels have no hull). A change to what a part is, shows or holds runs the live sections that name that kind of part by name before merging (`--selftest-list | grep`).
- 2026-11-12: a pick from a machine's own Recipe button stayed on the host (`OwnRecipe` returned before `StructureDirty` was marked). Any server path that changes a part's setting marks its body dirty; try a setting change once on a joined client.
- 2026-11-13: no glass sheet could be formed (and so no pane's blank): `PanelNumbers` clamped armour with `Math.Clamp(x, 1, MaxArmour)`, and glass's MaxArmour is 0, so the clamp threw. A clamp whose bounds come from a data table guards min over max (or skips the clamp), and a new material is tried through `--form-panel` once.
