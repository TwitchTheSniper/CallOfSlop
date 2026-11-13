---
paths:
  - "Source/Worlds/**"
  - "Source/App/SavePaths.cs"
  - "Source/Game/GameWorldSave.cs"
  - "Source/UI/UiWorlds.cs"
  - "tools/package.ps1"
  - "tools/MIGRATING.md"
  - "docs/sandbox/WORLD-SAVES.md"
---

# Saves: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- Saves live in `%APPDATA%\CallOfSlop`, or next to the exe with `portable.txt`, or wherever `--save-dir` points. Every save file is listed in `SavePaths.SaveFiles` (a folder: `SaveFolders`), written atomically, and keeps a `.bak`.
- A new save file is also added to the `tools/package.ps1` personal-file list (or testers ship their own saves), `tools/MIGRATING.md` and `.gitignore`. The commit guard (`.claude/hooks/guard.sh`) refuses a commit that changes `SaveFiles` or `SaveFolders` on a branch that hasn't touched all three; it can't see a file written without being listed, so list it first.
- Smoke tests never touch real saves: a throwaway copy or `--save-dir`.
- A save records what is: steps' `"at"`, links' `"laid"`/`"hosts"`, `"exactLinks"`, a step's `"also"`, points relative to step 0. Round trips compare parts, joints, links and bodies, on selections of real vehicles too.
- Never store a material in a save or a blueprint: a row's number in `Materials/Surfaces.json` is this build's own. Store the look (`BoxLooks`).
- 2026-11-12: a world loaded with every main switch off: `ReadBody` read each part's State, then called `ConstructConfig.Init`, whose `StartState` sets a switch or toggle from its Start on setting, which is read further down. A loader that calls an initialiser after reading saved fields keeps the saved ones (`WorldSaveBodies.ReadBody`), and a save is tried in a fresh process, not by reading the block back in the one that wrote it.
