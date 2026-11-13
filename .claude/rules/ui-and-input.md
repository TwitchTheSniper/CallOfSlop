---
paths:
  - "Source/UI/**"
  - "Source/App/Program*.cs"
  - "Source/App/Keybinds.cs"
  - "Source/Constructs/SnapStroke.cs"
  - "Source/Constructs/SandboxAim.cs"
  - "Source/Constructs/SandboxKeys.cs"
  - "Source/Constructs/SelectController.cs"
  - "Source/Constructs/PanelTouch.cs"
---

# Input, tools and UI: what went wrong here before

Each line is a past failure's rule. Add a new one at the end: date, what went wrong, why, the rule.

- ImGui is called only between NewFrame and Render. Input stores what to draw and the ImGui pass draws it; `Hud.CanDraw` skips and counts anything else (a native crash otherwise, with no exception).
- Per-frame state holding indices into a construct is stamped when taken and checked against the construct as it is when used (`ConstructRender.GhostLive`, `Program.LinkStamp`, `LinkStateCheck`, `LinkHitUsable`): a tick, a packet or the other peer can shift parts between input and draw, and a hit may name no part.
- Clamp bounds worked out at run time go through `SafeMath.Clamp`. Host actions, Sandbox input and its ImGui pass stay behind their guards (`Game.SandboxActGuarded`, `Program.SandboxInputGuarded`, `DrawSandboxUiGuarded`).
- After a crash, read the `[crash]` block in callofslop.log, crash.txt and breadcrumb.txt before guessing.
- Hold-to-repeat inputs never act on what the same stroke created (`SnapStroke`); try a stationary press, not only a sweep.
- Tool-mode state changes on transitions (taken out, put away), not per frame. A pick made before its tool arrives is kept for a bounded wait. A ring's marker reads the mode, and a tool's default option sits in the middle of its arc.
- The held-part ghost path is `Program.SandboxHeldOnConstruct`: a single-purpose part falls back to the crosshair's pick, then `SnapBuild.Nearest`. A refused aim always says why.
- `Keybinds.Migrate` unbinds every later-added action on its default before stepping versions.
- A wait for a key to come up reads the real key (`Keybinds.IsDown`); a press that opens a menu with a text box is spent until the key comes up (`_sbMenuKeyEaten`).
- Before taking a bit out of the input, find what reads it; a body's state is held at the body (`SandboxPlayer`).
- An ImGui button only notes its action; lists change after the drawing (`Ui.TwApply`), and an index is checked again after a change.
- Anything a machine holds is taken out through the player's own aim (`ConstructPress.MachineOf`).
- A view that replaces another has its own remembered mode and is tried with the other modes on.
- Player-visible text is ASCII. Text that isn't a plain u8 literal goes through `ImGuiSafe`: the source scan for printf text reads one `?:` of u8 literals only (2026-11-10 two `ImGui.TextColored` calls in Lobby.cs, 2026-11-12 two Crafting Terminal hints written as nested `?:`), so write one literal (or one `?:`) per printf-style ImGui call.
- 2026-11-10: from the Ordnance Bench's merge four sections failed in the full suite only: the Interaction sweep opened the bench, and neither `SandboxLocalReset` nor Esc closed it, so `MenusOpen` ate every later click. A new panel that holds the mouse joins `SandboxLocalReset` and the Esc chain in the same change.
- 2026-11-12: main went red after the Panel Designer merge: the Sandbox inventory section named icon slots as parts, blueprints, tools, resources, and the designer added preset tiles after the resources. A branch that adds icon tiles or inventory entries runs "Sandbox inventory" by name before it is merged.
- 2026-11-13: main went red for five runs (clicks lost) after a merge whose own checks passed: the Interaction sweep's F on a Job Board left its window open. A section that opens a window through real input closes it in its `finally` (`Program.SweepEnd`); after a merge that adds part kinds or settings, read main's CI before the next launch.
