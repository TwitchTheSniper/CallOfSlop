---
paths:
  - "Source/Maps/**"
  - "Source/MapKit/**"
  - "Maps/**"
  - "Source/Game/World*.cs"
  - "Source/Game/GameTravel*.cs"
  - "Source/Game/GameSpace*.cs"
  - "Source/Space/**"
  - "docs/space/**"
  - "docs/mapkit/**"
---

# Maps and worlds: what to know before changing them

Add a new line at the end: date, what went wrong, why, the rule.

- **Maps belong to another developer** (CLAUDE.md "Who owns what"): map development and destructible terrain are handed off. For map work read `Maps/README.md` and `docs/mapkit/MAPKIT.md` first. Many tests use the first map (Proto Box, `order` 1): new maps get a high order.
- **Baked maps** (`Source/Maps/MapBake*.cs`) write every field of a built `Map`. A field set after a map is built is marked `[NotBaked]`; a new kind of shared library object needs a root in `MapBake.IndexFor`.
- **A world per Game** (docs/space/FREE-TRAVEL.md). Several Games can run in one process on different maps: each enters its `Game.Place` (`WorldContext`) before ticking, and `World.Enter` swaps the statics (`World.Map`, `Physics`, gravity). A new static that holds one world's state is added to `WorldContext`'s swap, or kept in the Game or the map; code acting on a Game outside its `Update` enters its `Place` first.
- A static initializer never loads data that can lead back to its caller (2026-11-10: `World`'s initializer loaded the maps, a sandbox map looked up a blueprint, and the blueprints' Lazy was already running): take it on first use (`World.Map`).
- A check that changes a shared map restores it in its `finally` (2026-11-10: a section ended on Pocket Planet, and its gravity threw the next section's body over a wall).
- In flight, along the ground is `Along(v, up)` and up is `BodyUp`; try flight changes away from the world's +Y side.
- 2026-11-13: a new body was inserted in the middle of `Systems/Frostreach.json`, which moved Shard's index; saved ships keep their primary as a body index, so an old save would have reloaded orbiting the wrong body (caught by the merge review). Rule: a new body in a system file goes at the end of `"bodies"`, never between existing ones.
