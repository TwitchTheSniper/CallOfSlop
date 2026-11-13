# The shared agent setup

This folder is what every Claude Code session on the repo shares: cloud threads, sessions on a developer's PC, and their subagents. It is committed apart from each machine's own files (`worktrees/`, `settings.local.json`, session state: see `.gitignore`). Nothing here adds to GitHub CI, and a push that changes only this folder doesn't start a CI run.

| Piece | What it is |
| --- | --- |
| `rules/*.md` | The lessons and gotchas, one file per area. Each starts with a `paths:` list; Claude Code puts the file in front of a session when it reads a file under those paths. `docs/LESSONS.md` is the index. |
| `settings.json` | Runs `hooks/guard.sh` before every Bash or PowerShell command (a `PreToolUse` hook). Your own settings go in `settings.local.json`, which git ignores. |
| `hooks/guard.sh` | The guard rails below. |

## Rules

A rule file is plain Markdown with a `paths:` list in its front matter (globs from the repo root). Keep each under about 5 KB: a file loads whole the first time one matching file is read. Put a lesson where its code is, one line: date, what went wrong, why, the rule. A lesson that keeps coming back in every area moves to CLAUDE.md's "Lessons and gotchas", which every session loads.

What the loading does not cover: a session that changes a file without reading it, or reads it only through a shell command, may not get that file's rules. Open the file you are about to change.

## The guard

`guard.sh` reads each shell command before it runs and refuses three things. A refusal prints what to do instead.

1. **Switching branches in a shared checkout.** A main folder on a developer's PC is used by several sessions and by the developer's own game build at once; two sessions clashed there on 2026-11-13. `git checkout BRANCH`, `git checkout -b`, `git switch` and a detached checkout are refused in a main checkout on a PC. They are never refused in a linked worktree (`git worktree add`), which is where a session works, nor in the cloud (`CLAUDE_CODE_REMOTE`), where each session has its own clone. Restoring files (`git checkout -- FILE`, `git checkout .`) is not a switch. A developer who wants to switch by hand in sessions of their own folder runs `git config callofslop.ownCheckout true` there once; an agent never sets it in a folder it did not make.
2. **A wire-format change without a protocol bump.** A commit that changes `enum Msg`, `CActKind` or `CBodyKind`, or adds or removes a packet read or write (`.U8(`, `.U16(`, `.Cm(` and the like, outside the self-tests, `Source/Worlds` and `Source/Maps`), is refused while `ServerBrowser.ProtocolVersion` is what it was where the branch left main. One bump covers the branch. If no byte changes, put the words `wire-same` in the commit command (the message will do) and say why.
3. **A save list change without its three lists.** A commit that changes `SavePaths.SaveFiles` or `SaveFolders` is refused until the branch has also touched `tools/package.ps1`, `tools/MIGRATING.md` and `.gitignore`. If they need no change, put the words `save-lists-same` in the commit command.

It judges the working tree against `HEAD`, since a command often stages and commits in one line. It does not judge a commit that finishes a merge, a cherry-pick or a revert. It cannot see a script's own git calls (`tools/coordinator/merge.ps1` switches to main itself), a new save file that was never listed, or a wire change with none of the marks above: the rules still hold where the guard is blind.

**How it runs.** `settings.json` starts it through a git alias, so the only thing it needs is git: Git for Windows brings the `sh` it runs under, whether the session's shell is Git Bash or PowerShell. It exits 42 to refuse (the alias line turns that into the hook's blocking exit 2); any other exit, a missing file and a syntax error all let the command through, so a broken guard never stops work.

**Trying it by hand** (from the repo root; exit 42 and a message is a refusal, 0 lets it through):

```
printf '{"cwd":".","tool_input":{"command":"git checkout -b probe"}}' | CLAUDE_CODE_REMOTE= sh .claude/hooks/guard.sh; echo $?
printf '{"cwd":".","tool_input":{"command":"git commit -am probe"}}' | sh .claude/hooks/guard.sh; echo $?
```

The first refuses in a main checkout and passes in a worktree. The second passes on a clean tree; add a member to `enum Msg` in `Source/Net/Net.cs` (and take it out again) to see it refuse.
