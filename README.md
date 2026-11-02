# Agent claims

This branch holds no code. It records which files each developer's agents are working on,
so two agents never edit the same files at once. It is separate from `main` so updates
never need merging.

## Files

One file per developer: `.agents/claims/<developer>.json`. Each developer's coordinating
agent writes ONLY its own file and never edits anyone else's.

```json
{
  "developer": "twitch",
  "updatedAt": "2026-11-01T20:00:00Z",
  "agents": [
    {
      "name": "api-refactor",
      "project": "callofslop",
      "task": "One line on what it is doing",
      "model": "sonnet",
      "status": "running",
      "files": ["src/net/Client.cs", "src/net/protocol/"],
      "lastStep": "One line",
      "remaining": "",
      "startedAt": "2026-11-01T19:40:00Z",
      "updatedAt": "2026-11-01T20:00:00Z"
    }
  ]
}
```

- `status`: `queued`, `running`, `done`, `incomplete`, `stopped` or `failed`.
- `files`: repo-relative paths the agent will edit. A path ending in `/` claims the whole folder.
- An agent holds its files while `queued`, `running`, `incomplete` or `stopped`.
  It releases them when `done` or `failed`. Remove `done` and `failed` entries after a day.

## Rules for coordinating agents

1. **Before launching an agent that edits code**, fetch this branch and read every claims file.
   Two paths overlap when they are equal, or when one ends in `/` and the other starts with it.
2. **If a claim overlaps another developer's active agent, do not launch.** Tell your user which
   file, which developer and which agent holds it.
3. **With no overlap**, add your agent to your own file with its `files`, push, then launch it.
   Tell the agent to stay inside those files. If it needs another file, it stops and reports;
   check claims again before widening its claim.
4. **Update your file** when an agent's status or files change, and at least every 20 minutes
   while it runs. Always refresh `updatedAt`.

## Commands

From an existing clone of the repo:

```sh
# one-time: a separate working folder for this branch
git fetch origin agent-claims
git worktree add ../callofslop-claims agent-claims

# read everyone's claims
git -C ../callofslop-claims pull --rebase -q
cat ../callofslop-claims/.agents/claims/*.json

# publish your own file after editing it
git -C ../callofslop-claims add .agents/claims/<developer>.json
git -C ../callofslop-claims commit -m "claims: <developer> update"
git -C ../callofslop-claims push origin agent-claims
# rejected because someone else pushed first? pull --rebase, then push again.
# Each developer has their own file, so this never conflicts.
```

## Agent Board

The owner's Agent Board copies everyone's entries from this branch during each status check,
so it may lag behind this branch. This branch is the source of truth for overlaps.
