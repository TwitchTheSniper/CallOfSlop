---
name: merge-branch
description: Merge a finished branch into main on Call of Slop without turning main red. Use when a branch or pull request is ready to merge, when the coordinator merges an agent's work, or when a merge has conflicts in the protocol number, Msg or a flags byte.
---

# Merging a branch into main

Most red mains came from a merge whose own checks passed: the branch ran its own sections and broke a neighbour's. This is the order that catches that. It adds nothing to GitHub CI.

## 1. Bring main in first

```
git fetch origin main
git merge origin/main        # in the branch's own worktree, never by switching a shared checkout
```

Resolve by hand, and look for what shows no text conflict:

- **Protocol.** If both sides bumped `ServerBrowser.ProtocolVersion`, main keeps its number and the branch takes the next one. Keep both history lines, in order, and change the number wherever the branch names it (its commit titles don't matter; its docs and comments do).
- **`Msg`, `CActKind`, `CBodyKind` and other wire enums.** Both sides' new members go at the end, main's first. Never renumber one that is on main.
- **Flags bytes and `(byte)` casts.** Read each one both sides touched: a bit used twice or a narrowing cast merges cleanly and breaks at run time.
- **Generated files.** Don't hand-merge an output: merge the generators, then `--gen-parts all` and commit what it writes. When several branches change committed vehicles, regenerate `Constructs/Blueprints` once, after the last merge.

## 2. Check what the branch can break, not what it added

1. `dotnet build -c Release --no-restore` passes.
2. `--check-construct-parts`, then read `callofslop.log` next to the DLL for `REFUSED` (it prints nothing to the console). `--gen-parts all --check` when a generator or part file changed (on Windows see the CRLF note in `.claude/rules/generated-geometry.md`).
3. **The neighbours' sections.** List what changed in kind (a part kind, a setting, an icon tile or inventory entry, a panel or window, a machine's screen or output, a flag such as `IsSignal`). Find the live sections that name that kind and run them by name:
   ```
   dotnet bin/Release/net10.0/CallOfSlop.dll --selftest-list          # names, also in callofslop.log
   dotnet bin/Release/net10.0/CallOfSlop.dll --selftest-only "Sandbox inventory, Interaction sweep"
   ```
   then read `selftest.log` next to the DLL (the exit code is the failure count). Known pairs: icon tiles or inventory entries, "Sandbox inventory"; anything a sweep can open with F, the Interaction sweep and what runs after it; machine looks, "Machine tier looks"; production, the list in `tools/coordinator/merge.sh`.
4. **A second pair of eyes.** For a branch that changes code, run the `merge-reviewer` agent on it (`.claude/agents/merge-reviewer.md`) and settle each finding: fix it, or say why it doesn't apply. For a branch that changes how something looks, take the pictures the play-test tour or `--window-shot` gives and run the `look-critic` agent on them. Skip both for docs-only and data-typo merges.

## 3. Merge

- **From a cloud thread:** push the branch (the final push never carries `[skip ci]`), open the pull request, wait for its run, then merge it with a merge commit titled `Merge WHAT (#N)`. A push that changes only `*.md`, `docs/`, `tools/` or `.claude/` starts no run: merge it once it has been read.
- **On the Windows PC:** from the main checkout, with a clean tree and the game closed: `powershell -File tools\coordinator\merge.ps1 BRANCH "MESSAGE"`. It builds, runs the checks above and pushes; it stops on the first failure. The main checkout stays on main.
- **On a Linux box:** `MODEL="..." SESSION=https://... tools/coordinator/merge.sh BRANCH "MESSAGE"` (same steps; `SECTIONS=""` for a docs-only merge).

Nobody waits for a review: once the checks pass, merge and report it (CLAUDE.md, "Merge without asking").

## 4. After

- Read main's CI run for the merge before launching the next piece of work. If it is red, fix it or revert the merge at once: every later merge hides which one broke it.
- If it failed for a reason seen before, add the lesson to the `.claude/rules/` file that covers the code (one line: date, what went wrong, why, the rule).
- Playtest board items the merge finishes are set to `done` (CLAUDE.md, "Player feedback").
