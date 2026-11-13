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
