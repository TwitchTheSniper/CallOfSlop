---
paths:
  - "Props/**"
  - "Source/Props/**"
  - "Parts/**"
  - "Source/Zombies/**"
  - "Source/Weapons/**"
  - "Source/Game/Horde/**"
  - "Source/Media/**"
  - "tools/browser/**"
  - "docs/sandbox/WEAPON-BUILDER.md"
  - "docs/sandbox/MASS-HORDE.md"
---

# Props, weapon-builder parts, the crowd and the Browser helper: what to know

Add a new line at the end: date, what went wrong, why, the rule.

- **Prefabs and prop parts.** A prop part may `use` another prop (`{"use": "Landing Beacon", "pos": [...], "yaw": 90, "scale": 1.1}`): the loader flattens it and refuses cycles (Props/README.md "Prefabs"). Prop parts rotate about their own centre, so to tilt an assembly compute each part's offset around the pivot.
- **Weapon-builder parts.** Parts must form one connected piece ("Model connectivity", `--check-builder-parts [DIR]`). Box tags: `"irons"` draws a box only with a receiver fitted and no optic; `"with:SOCKET"` only while that socket holds a part. Each receiver has a required `action` socket: the Action part gives the fire type, the receiver the calibre.
- `Source/Zombies/` holds the weapon builder (PartDef/PartLibrary, WeaponBuild, BuildRules, BuildStats, BuildModel), which Sandbox guns use, so it is live code; the Zombies rules, RunTables and PushCart beside it are archived.
- **The crowd** (2026-11-09): a Sandbox client drew a far horde the host didn't have, because the sends stopped with the last zombie and a full Msg.ZCrowd always restarted at cell 0. A delta stream sends its final empty state and resumes where a full message stopped; measure what the client holds against the server's counts (`--zrange-host`, `CrowdStale`).
- **The Browser helper** is a second program, rebuilt when its source changes: compare the built file's date with the commit before believing a fix failed. A probe runs through the shipped wrapper and settings (CefSharp starts with `--mute-audio`); anything crossing a process gets a throttled log line per hop.
