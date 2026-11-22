---
paths:
  - "Props/**"
  - "Buildings/**"
  - "Models/**"
  - "Parts/**"
  - "Constructs/Parts/**"
  - "Source/PartKit/**"
  - "Source/Props/**"
  - "Source/Buildings/**"
  - "Source/VehicleKit/**"
  - "Source/Interiors/**"
  - "Source/Constructs/HullLoft*.cs"
  - "Source/App/ZFight.cs"
  - "tools/constructs/**"
  - "tools/props/**"
  - "tools/buildings/**"
---

# No coplanar faces (z-fighting, user 2026-10-30)

Boxes that touch or overlap must never share a face plane that both draw: two parts lined up exactly flicker through each other.

- When parts meet flush, make one of them a little different in size or position so one face is clearly in front: 2 to 5 mm, 10 mm on patches up to 8 m, 1/800 of the patch above that (at most 50 mm). The depth buffer (24 bit, near 0.05 m) resolves 2 mm to 41 m and 10 mm to 92 m.
- Move the less seen face, keep sockets and silhouettes. Faces of one plain colour need nothing (the check knows, except in a prop's baked ground shade below 0.45 m).
- It applies to props, prefabs, models, buildings, construct parts and weapon-builder parts, and to generator scripts: fix the script, not only its output.
- `PartZFight.cs` is the corrector (`Resolve`, `SettleProps`; `tools/constructs/zfight.py` for the Python generators). `--zfight-props FILE...` runs it on prop files, `--zfight-moves OLD_REV` measures how far boxes moved. The "No z-fighting" section checks shipped data.
- 2026-11-14: every Wheel, Fan and Airframe Former z-fought with the floor it stood on: the hopper arrow's two centre arguments were swapped (`MachineLook.Arrow(..., 0, 1.8, ...)`), so it sat half in the floor at x 1.8 with its top on the floor's plane. A look box on a face is checked against the part's own bounds and against the floor plane; `--check-vehicle` on a blueprint standing the part on Foundations shows it.
- 2026-11-15: the rust bucket's second hull passed `--check-vehicle` (0 coplanar faces) and went red in CI's "No z-fighting": that check is only between parts, and the Keel drew ring 5's facets and the chin's rail twice (a hull loop and the piece's own block), hoop rib segments overlapped at their corners with equal thickness, and a crane's end box was flush with its boom. A generator that splits a hull into pieces runs `--selftest-only "No z-fighting"` too, and neighbouring segments of a ring differ in thickness.
- 2026-11-15: CI went red on "No z-fighting" for nine Zero Day frigate parts though `--check-vehicle` said 0 coplanar faces: that counts only pairs between parts, and the lofted hull's plates crossed at shared edges with their end caps on one plane, chine rails and strips ended flush with the plates under them, and a twisted quad was split into two near-coplanar triangles. A lofted hull staggers neighbouring facets' ends (`TorpedoFrigateParts.FacetStagger`), keeps details off the faces of the plates they sit on, and is checked with `--selftest-only "No z-fighting"` before it is pushed.
- 2026-11-21: a 32 m twisted Hull Section had coplanar faces at its ends (rails and strip trimmed 3 mm against a facet whose stagger grows with length). A trim at a long piece's end goes by its length (`HullLoft.TrimOf`: 3 mm to 8 m, then 1/800, at most 50 mm); `--hull-section` and `--dress-skin` on a 32 m key show it.
