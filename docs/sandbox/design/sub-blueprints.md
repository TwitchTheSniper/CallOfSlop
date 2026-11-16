# Sub-blueprints: ships built from submodules on many stations (design)

The user, 2026-11-16: "submodules can be constructed". A player who designed a whole ship wants it built fast: several
production lines build parts of it at once as submodules, so the factory is more varied and production quicker.

The user's decisions (binding):

1. **Nested blueprints.** A blueprint can contain other saved blueprints (a turret used four times in a ship). Each use is
   built as its own job.
2. **Final join.** A finished submodule leaves its station as one body; cranes and the existing lifters bring it to the main
   station, which fits it as **one big step**: welded at its spot, with a long fit time.
3. **Dispatch** is automatic over the network. The main station orders each use as it orders a part; a free station on its
   network whose bay is big enough takes the job, builds it and sends it back.
4. **Depth**: any, with a loop guard and a depth cap (4).
5. **Edits**: live by default (a ship uses the newest saved version of each sub-blueprint; if that no longer fits its spot,
   the ship is marked broken with the reason), with a per-use **lock** to one saved version.

Builds on: the Building Station (production.md "Building station", `ConstructStation.cs`), `ConstructBuild.FitStep` /
`BuildSteps` / `FinishBuild`, the Selector and Blueprint Desk (`ConstructClip.ToBlueprint`, `PlayerBlueprints`,
`BlueprintLoad`, `UiSandboxBlueprints.cs`), `ConstructWorld.Merge` (`ConstructJoin.cs`), the logistics planner's chained
orders (`ConstructLogisticsPlan`) and the lifters (`arms-and-cranes.md`, `ConstructLifters*.cs`, `ConstructCraneRails.cs`).

## 1. What the player sees and does

**Making a ship from submodules.** There is no separate designer: a player builds in the world and saves with the Desk. So a
use is made by **stamping**. Every stamp of a saved blueprint (Tab, Blueprints, Stamp) tags the parts it lays as one **use**
of that blueprint (its id, version, and where it stands). The player builds the hull, stamps the Turret four times onto it,
selects the whole ship and presses Comma: the save writes the hull's parts as steps and each intact Turret as a use, not as
58 parts. Nothing new to learn beyond one window.

- **Seeing uses.** With the selector out, each use in view has a thin cyan frame and a label: "Turret v5 (live)", "Engine
  Pod v3 (locked)". A use whose parts were changed since the stamp is framed amber: "Turret v5: changed". The look line on a
  part says "part of Turret #2 (v5, live)". Uses inside uses are framed only when the outer one is selected.
- **The save window** gains a **Uses** list, grouped by blueprint: "Turret x4 - v5" with a row per use, each with a
  **Live / Lock** toggle (default Live; Lock pins it to the version shown). A changed use's row offers three buttons, the
  first chosen by default: **As plain parts** (it is saved as steps), **New version of Turret** (saves the changed parts as
  Turret v6; every live use everywhere then gets it), **New blueprint** (asks a name). A use cut in half by the selection is
  saved as plain parts and the row says so.
- **Nesting.** The highest intact use wholly inside the selection becomes a use; a changed outer use dissolves into its
  intact inner uses (an edited ship stamped whole keeps its untouched Turrets as uses).
- **Square only.** A use is saved only if it stands square to the ship (one of the 24 turns, as a snapped stamp does) and on
  the 1 cm grid. A freeform-placed stamp is saved as plain parts and the row says why: the final join merges bodies by Rot24
  turns (`ConstructWorld.Merge`).
- **The Blueprints tab.** A blueprint with uses shows "uses: Turret x4 (v5 live), Engine Pod x2 (v3 locked)" in its details,
  with the same Live / Lock toggles (a change re-saves the file as a new version). A blueprint that no longer builds is red:
  "BROKEN: Turret v6 doesn't fit at use 3: the Barrel (Turret step 4) is in the way of the Hull Plate (step 88)", with a
  **Lock use to v5** button when an older version fitted. A broken blueprint can't be started or stamped.
- **Saving a sub-blueprint again** (Turret as v6): the save window lists what uses it live ("used live by Frigate, Corvette")
  and checks each (a few steps a frame, `BlueprintLoad`) before it closes, so the player sees at once what broke.

**At the main station.** Start is as today. The panel's left side is unchanged; the right side gains a **tree** under the
bill:

```
Frigate v3 - 412 own parts: 186 fitted        uses: 6 (2 in place)
  Turret #1   Station B     building 31/58
  Turret #2   Station C     done - Bridge Crane bringing it
  Turret #3   this station  in place - welding, 14 s
  Turret #4   waiting       no station free: 2 busy (B, C); this station builds it in 22 s
  Engine Pod #1  Station D  costing
    Nozzle Ring #1  Station E  building 3/12
```

A sub-station's own panel shows its job with a header: "for Station A: Frigate, Turret #2 - deliver to its bay: Bridge
Crane reaches both" or "nothing carries it: move it into Station A's bay by hand". Each station gets a setting **Sub-jobs**
(group Bay; Auto, Off): Off keeps a station for the player's own builds.

## 2. Data

**Blueprint identity.** A player blueprint gains `"id"` (16 hex digits, made once, kept through renames) and `"version"` (1 up,
+1 on every overwrite and every lock change). An id is added only when a blueprint is first stamped as a use or saved with
uses (the file is re-written then, atomically with its `.bak`), so old files are untouched until used. Shipped blueprints
have the id `ship:<name>` and no versions.

**History.** An overwrite keeps the old text in `ConstructBlueprints/.versions/<id>/<version>.json`. Kept: every version a
lock in any blueprint names, and the last 8 (proposed). `ConstructBlueprints` is already in `SavePaths.SaveFolders`, so a
subfolder needs no new list entry; `tools/package.ps1` and `MIGRATING.md` are checked to copy subfolders.

**A use** in the parent's file (a new top-level list; steps stay as they are):

```json
"uses": [
  { "id": "9f3c41e07a2b5d18", "name": "Turret", "version": 5, "lock": false,
    "turn": 7, "at": [3.20, 1.85, -6.00],
    "on": [[88, "top", 0, "base"]] }
]
```

- `turn` is the Rot24 turn and `at` the place of the sub-blueprint's step 0 in the parent's step-0 frame (as every `"at"`).
- `on` are the links joining the use to the parent: `[parent step, its socket, sub step, sub socket]`, the same meaning as a
  step's `"also"`. A use's parent step can be another use's: `[["u2", 14], "rim", 0, "base"]` (use 2's step 14).
- `name` is a hint for messages and for finding a missing id by name; `id` decides.
- A routed link, wire, hose or rope between a use and the parent goes in the parent's lists with an end written as
  `["u2", 14]` instead of a step number. Links inside a use are the sub-blueprint's own.
- A step of the parent may stand on a use's part: its `"on"` is `["u2", 14]`. The parser reads either form; `ExactLinks`
  holds.

**Resolving and flattening** (new `Source/Constructs/SubBlueprints.cs`, pure functions, no world):

- `Resolve(Blueprint)` walks the uses: a live use takes the newest version of its id, a locked one its history file. It
  keeps a path stack of ids: a repeat is a **loop** ("Turret contains Frigate, which contains Turret"), more than 4 levels
  is **too deep**. Each (id, version) is parsed once into a cache (`SubCache`, at most 64 entries, least used dropped).
- `Flatten(tree)` gives one ordinary `ConstructBuild.Blueprint` (every step of the parent, then each use's steps, renumbered
  and transformed by its turn and place, `"at"` and weld poses turned as `Merge` turns them, then quantised) plus a
  `UseMap`: each flat step's use (or -1) and each use's first step, count, parent use and its joining links. Every
  existing reader (the station, a stamp, the preview, `--check-vehicle`) builds the flat blueprint and needs nothing new.
- Limits: 64 uses a blueprint, 256 in a tree, `Construct.MaxParts` flat parts; each refused with the numbers.
- **Broken**: a flat build in a scratch world (sliced) that refuses. The result is kept in `ConstructBlueprints/.checks/`
  keyed by the hash of every (id, version) in the tree, so a list shows red without building, and is redone only when a
  version under it changes. A missing id or history file is broken too ("Turret v4 is not in this game: copy it in, or
  re-save Frigate").

**Sharing.** A file with uses names ids another PC may not have. The Blueprints tab gains **Pack**: one file
`<name>.pack.json` with the blueprint and every version its tree names (`"packed": {"<id>": {"<version>": {...}}}`). A pack
dropped in the folder is unpacked on the next list: each sub-blueprint lands in history; one whose id isn't here becomes a
live blueprint under its name (a name in use gets " (2)"). A shipped blueprint may use shipped ones only; a player one may
use both (a shipped use is always live: it changes with the game).

**The world: tags.** `Construct.Uses` (a small list, null on most bodies): per use its id, version, lock, turn, origin, the
parent use (tree), the parts' hash at the stamp; each part a `u16 Use` index (0: none) in `CPart`. A split moves tags with
parts; a merge renumbers them; removing or adding a part inside a use, repainting it or changing its settings marks it
changed (the hash, recomputed at save, not per edit).

**World saves.** A `USES` block per body after `SIDE` (absent when none): the use table, then runs of part indices per use.
An older save reads no block and has no uses. A station job's save gains: its role (root or sub), its root's body, part and
job stamp and use index (a sub), and for a root its pinned tree as deflated flat text and `UseMap` (a few KB), so a world
moved to another PC or a sub-blueprint edited since goes on building what was started. A delivered body's `PartOf` is saved
with the root.

## 3. The station's job tree

**Start** (root). `Find` returns the resolved, flat blueprint and its `UseMap`, worked out **off the game thread** (file reads
and parse, as a stamp's upload is) and pinned to the job. Costing is today's: the flat steps on a scratch world, 1 ms a
tick, the bay check against the whole ship's bounds. Each use becomes a **sub-order** row on the job.

**Own steps go on meanwhile.** The flat steps that belong to the parent are fitted by the existing rules (first unfitted step
whose parent is in place and whose kind is held). A step that is a use's is "external": never ordered, never fitted alone.
A parent step whose `"on"` is a use's part waits for that use to be fitted, as any step waits for its parent; the bill and
the orders skip use steps, so the hold fills only with the station's own parts.

**When a use may be fitted.** Every parent step and other use its joining links name is fitted. That is the flat
blueprint's own order: nothing new decides it. A use on a turret ring waits for the ring; the panel says "waits for the
Turret Ring (step 40)".

**Dispatch** (the root's 2 Hz look, only for a use with no station and not yet dispatched):

1. Candidates: Building Stations **on the same construct** as the root whose network the root's reaches (its chute,
   conveyor or lifter map, `CraftNet`), with Sub-jobs Auto, powered, **idle** (no job, bay clear) and whose bay fits the
   use's blueprint (`QuickRefusal`, then its costing). At most 16 a body, listed when the net is worked out, never per tick.
2. Order among candidates: one with a carrier to the root (a crane or arm reaching both bays, or bays that touch, section
   4) before one without; then the smallest bay that fits; then the nearest.
3. A sub-job never pre-empts: a station busy with a player's own build is skipped; one job a station.
4. The chosen station gets a `StationJob` with `Root` set and the use's flat sub-blueprint; it costs it and orders its own
   parts as any build, its chained orders carrying the **root's** rank and age stamp, so a ship's parts queue fairly with
   other orders and the oldest ship goes first.
5. **Nothing takes it** for 30 s (proposed): the root builds the use itself, part by part, as plain steps (its steps stop
   being external). The tree row says so first: "no station free: this station builds it in 22 s".

A sub-job's uses are dispatched by the sub-station the same way (depth ≤ 4).

**A sub-job's end.** Its steps and its own internal links are laid; it skips `FinishBuild` and the fill stage (fused units,
hoses, tracks and fill are the whole ship's, done once by the root). The body stays frozen, `HeldBy` the root's construct,
tagged with the use (`Construct.SubOf`: root body, part, job stamp, use), and its `PartOf` table goes to the root. Its
state: "done - waiting to be carried". The station is free again only once the body has left its bay.

## 4. Delivery

A submodule is a body of many parts. Conveyors and chutes carry single pieces, so they never carry one. Ways, in the order a
sub-station looks for one:

1. **A lifter** (recommended main way). A Bridge Crane, or a Loader Arm for a small one, whose reach holds the sub-bay and
   the use's final spot, and whose payload and largest piece take the body's mass and bounds (a Medium crane: 30 t, 7.2 by
   16 m). New lifter work, first in its list ahead of station steps: "a finished sub-body at a sub-bay whose spot is in
   reach". The pick is the body as it stands; the put is **the use's own spot on the build**, so the carry ends where it is
   welded. Moved as a lifter moves a piece: a scripted path from its two ends on every peer, passing through everything, the
   body frozen and posed each frame; players in the way as today (pushed 0.75 m, else it waits). The lift word gains a body
   id instead of a def.
2. **Bays that touch.** A sub-station whose bay touches or overlaps the root's bay hands the body over itself: the root's boom
   draws it across to its spot during the fit (below). This is the no-crane layout: helper stations round a big bay, the
   panel's tip says so.
3. **By hand.** When neither exists the body is freed in place (frozen, as a stamped group lands) and still tagged: any
   player may move it with the physics gun, tow it, or drive it if it has wheels. Set down within 8 m of the root's bay, the
   root takes it at its next look and draws it to its spot. Its edits are allowed; the fit checks them.

Recommendation: 1 for big ships, 2 for compact factories, 3 always open as the fallback, so a factory with no crane still
finishes. Carts that drive submodules round are not part of this design (no such carrier exists today).

## 5. The big fit

When the body reaches its spot (a lifter's put) or the root takes it (bays that touch, by hand), the root starts the fit:

- **Check first**: every part the use's `PartOf` names is there with its def, turn and place within 1 cm of the flat
  blueprint's, no part extra. Otherwise it refuses with the count and the first difference ("Turret #2 arrived changed: 2
  parts missing (Barrel, step 4)") and the body is left free; the row offers **Cancel use** (it is dispatched again). Damage
  is not a difference: health carries over.
- **Timing**: 5 s plus 1 s a tonne, at most 60 s (proposed), on the root's own fit timer: the station's boom fits nothing
  else meanwhile; Loader Arms go on fitting its own steps. While it runs the body moves from where it lies to its spot (a
  scripted path, flat, at most the timer's first third), then the weld effect runs along each joining link and the sound plays.
- **Alignment** is exact, never physical: the body is set at the flat blueprint's pose (Rot24 turn and quantised place), so
  there is no tolerance to tune. What the flat costing built at Start fitted there, so the use cannot collide with the
  build's parts. A loose body or a player standing in the spot makes it wait, as a crane's put does.
- **The weld** is one server action in one tick: `ConstructWorld.Merge` with the use's turn (fails only on the body limits,
  which the flat costing already passed), then `PartOf[flat step] = offset + sub PartOf[step]` for the use's steps, then
  `LinkAlso` for its joining links. Links between the use and the rest are laid after the last part as today. One big fit a
  tick per world (`FitTick`), so a 2,000-part merge (about 2 ms commit, as a selection join) stays inside 8 ms.

## 6. When it goes wrong

| Case | What happens |
| --- | --- |
| A live use's new version doesn't fit | The parent is broken (section 2) and won't start; a running job is pinned, so it isn't touched |
| A loop or more than 4 levels | Refused at the save that would make it, with the path; a file edited by hand is broken |
| A missing id or history file | Broken, with the name and version; a pack fixes it |
| No station's bay fits, none free, none on the network | After 30 s the root builds it itself; the row says why each candidate was skipped |
| A sub-job cancelled at the sub-station | Its body is taken apart (parts back as Cancel does); the use goes back to waiting and is dispatched again |
| The root cancelled | Every sub-job under it cancels; delivered but unfitted bodies are taken apart too |
| A sub-station wrecked mid-job | Its body is freed as it stands (as a damaged build); the root's next look sees the link gone and dispatches the use again |
| The root wrecked | Sub-jobs finish and wait "no root - Release"; Release frees the body |
| A body damaged in transit | Fitted with its damage; a part lost is the "changed" case |
| Nothing can carry it | It is freed by hand (section 4, way 3) and the row says "move Turret #2 into this bay" |
| Save and load mid-tree | Every job reloads pinned; the roots' links to sub-jobs and delivered bodies are restored; a lifter holding a body loads holding it |
| A client joins | It gets each body's Full, its `USES` and `Station` words; the panel tree comes from the root's word (below) |

## 7. Wire and saves

- **Protocol** 323 to the next number, with a history line. `CBodyKind.Uses` (new, at the end): a body's use table and part
  runs, on a change and to a joiner (clients need it for the frames and for a client-side save). `CBodyKind.Station` gains
  per root: a use count, then per use (at most 64 sent) its state byte, the station's body and part, fitted and steps
  (9 bytes); per sub-job its root and use. The lift word gains a body carry (a flag and a u16 body). `StationOp` gains
  `CancelUse` at the end; the Sub-jobs setting is a part setting (the part's settings list: no new bytes).
- **Stamp upload**: a client resolves its own blueprint's tree and uploads it packed, so the host builds what the client saw.
  The station still reads the host's folder.
- **Saves**: the `USES` block, the job fields of section 2; older saves load with none. `SavePaths.SaveFiles` and
  `SaveFolders` are unchanged (subfolders of `ConstructBlueprints`).

## 8. Performance

- Resolve and flatten off the game thread (Start, the panel, a stamp); the game thread takes the finished result.
- Costing: unchanged, 1 ms a tick over the flat steps. A sub-station costs its own use; both are budgeted per world, not per
  job (`CostBudgetMs` shared), so ten stations starting together don't take 10 ms.
- The root's look: at 2 Hz, walking its uses (≤ 64) once; candidates come from the net's cached station list, worked out on a
  rebuild, never per tick; dispatch is tried again only when the net's key or a station's state changes.
- No per-tick allocation: use rows are a fixed array made at Start; the `UseMap` is arrays.
- The big fit: one per world per tick, measured with the biggest shipped blueprint as the root and a 2,000-part use, on the
  2-core box and the PC, under 8 ms.
- A carried body costs its pose a frame (a client's drawing) and nothing on the server between a move's ends.

## 9. Phases (one builder, the whole feature; a WIP commit `[skip ci]` after each step)

1. **Data.** Ids and versions, history, `"uses"` and `["uN", k]` ends in the parser and writer, `SubBlueprints.Resolve` and
   `Flatten`, the guards, the broken check and its cache, packs. A check command `--sub-blueprint NAME` prints the tree and
   compares the flat build with a build that merges each use's body (every part within 1 mm): the proof that the final join
   gives the same ship.
2. **Tags and the save.** Stamp tags, `Construct.Uses`, `CPart.Use`, split and merge, the `USES` save block, `CBodyKind.Uses`,
   the selector's frames and look line, the save window's Uses list (lock, changed, square rule), the Blueprints tab's uses,
   toggles and red broken rows.
3. **One station, by hand.** The root job on the flat blueprint with external use steps, the fallback "builds it itself",
   the big fit from a body set down in or near the bay (a hand-built or stamped submodule), its check and timing, the
   panel's tree.
4. **Dispatch.** Sub-jobs on other stations, the root's rank and stamp on their orders, the Sub-jobs setting, Cancel use,
   the failure table, depth, the sub-station's header.
5. **Carriers.** Lifters carry a body (crane, arm), bays that touch, the lift word's body carry.
6. **Proof.** Save and load mid-tree, a joined client watching, tick and allocation measured; production.md's "Building
   station" gains a "Submodules" part; a shipped showcase blueprint with two Turret uses and a second station under a crane
   (the part kit's generator, so the files stay generated). No self-test section unless the user asks.

## 10. Open questions for the user

1. **No station takes a use**: recommended, the root builds it itself after 30 s, so a ship always finishes; else it waits
   until a station is free.
2. **Which stations are "on the network"**: recommended, every Building Station on the same construct that the root's
   chute, conveyor or lifter network reaches, with a Sub-jobs Auto / Off setting; else only stations wired to it.
3. **The big fit's time**: recommended 5 s plus 1 s a tonne, at most 60 s.
4. **What changes a use**: recommended, any change (parts, paint, settings) makes it "changed" and the save asks; else paint
   could be a per-use colour that keeps the link.
5. **A delivered body that was edited**: recommended, refuse it with the reason and offer Cancel use; else fit the parts that
   match and build the rest in place.
6. **Limits**: recommended depth 4, 64 uses a blueprint, 256 in a tree, history of every locked version plus the last 8.
7. **Arms carry bodies too**: recommended yes, within their payload and largest piece (a Plasma arm: 8 t, 2.5 by 10 m), so
   small submodules need no crane.
