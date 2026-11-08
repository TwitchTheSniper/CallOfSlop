# Death in a survival world

Code: `Source/Game/GameSandboxDeath.cs`. Survival worlds (the crash landing, the Ministry's penal colony, the warden's
induction) choose what a death means when the world is made. Creative worlds always respawn as before.

## The options

Chosen on the Single player page (with Survival ticked) and in the lobby room's Match tab (Survival). A saved world keeps its
own; they are greyed out once a world is picked, as the map and mode are. The world list shows them ("Survival, permadeath").

- **Death**:
  - *Respawn*: back at the world's spawn points, as before these options. Older saves read as this.
  - *Last checkpoint* (the default for new worlds): back at your checkpoint (below).
  - *Permadeath*: one life. Dead is dead in this world; the Respawn button reads DEAD FOR GOOD and the server never spawns
    you again, even if you leave and come back. You can still watch. A death during the opening (lying in the wreck) doesn't
    count: the story's start is not your life.
- **Keep the backpack on death**: off, what you carry drops where you fall as pickups (as before); on, you keep it. With
  permadeath the backpack always drops, so friends can pick it up.

## What a checkpoint is

1. **A Bunk** you lay in. The Bunk is a new part (a seat with `"seat": {"bunk": true}`, Seats and Logic > Seats and helms,
   made in the Assembler from cloth and steel). Lie in it (Use) and it is your checkpoint; you wake lying in it, and get up
   as from any seat. A bunk on a ship or truck travels with it, so a mothership can carry everyone's checkpoint. If someone
   is in it you wake on top of it. If the bunk is destroyed, or its construct is gone, you are told and fall back to 2.
2. **The penal colony's cell block**, once the colony stands: where the story first wakes you.
3. Otherwise the world's spawn points.

Tutorial steps and other story beats are not checkpoints: the induction carries on from where it was whatever happens.

## How it works

- The rule byte (`Game.DeathByte`: the rule, and 0x80 for keep the backpack) is in `WorldMeta.Death`, the Welcome after the
  survival byte, and bits 3 to 5 of the lobby state's second Zombies byte (protocol 207).
- The server sees a death in `ServerTickSandbox` (`SandboxDied`): the backpack, permadeath, and `_sbDied`, so the next spawn
  is placed by `SandboxRespawnAt` (after a saved world's own place, which only the first spawn uses).
- A bunk is remembered by construct id, segment and place in the rest frame (parts are renumbered; places stay), checked in
  the seat loop (`SandboxBunkSeen`). Construct ids are stable in a save; a body that splits or merges gets new ids and the
  checkpoint falls back.
- Each person's client hears where they will wake and whether they are dead for good in `Msg.Fate` when it changes; the
  Respawn screen's line comes from it (`SandboxRespawnLine`). `SandboxModule.CanRespawn` refuses anyone dead for good.
- Saved worlds keep each record's checkpoint and fate in the `FATE` section (docs/sandbox/WORLD-SAVES.md).

## Not done yet

- Lying down: a bunk seats you upright, as a seat does.
- A way back from permadeath (start over as a new prisoner) for friends who want one.
