# Tool upgrades

Each player's tools get better as they play survival (Karl, 2026-11-08: "a per player upgrade system for the phys gun and
tools"). There is no money: an upgrade is a **module**, a small part an Assembler builds from what the production chains
make. Some modules are only built once a faction has taught them. A player fits a module by pressing F on it while it lies
loose. It is used up, and from then on that player's tool is better. Upgrades are each player's own: they are kept in the
player's record in a saved world, they come back when the player returns, and in co-op every player has their own.
Creative ignores upgrades, since every tool is already at its best there.

## What can be upgraded

| Stat | Tool | Start | Modules (value) | What it does |
| --- | --- | --- | --- | --- |
| Lift | physics gun | 1,500 kg | Lift Coil Mk 2 (3,000), Mk 3 (6,000), Mk 4 (12,000), Mk 5 (25,000) | the heaviest thing the beam takes; its pull is 30 N a kg of lift, so anything at the limit still gets 30 m/s2 |
| Reach | physics gun | 12 m | Beam Lens Mk 2 (15), Mk 3 (18), Mk 4 (22), Mk 5 (26) | how far the beam reaches and holds |
| Drill bit | ground drill | x1 | Drill Bit Mk 2 (x1.25), Mk 3 (x1.5), Mk 4 (x1.8) | the scoop's radius, times `Digging.json`'s |
| Drill motor | ground drill | x1 | Drill Motor Mk 2 (x1.35), Mk 3 (x1.75), Mk 4 (x2.25) | scoops a second |
| Fitting speed | attach tool | x1 | Fitter Servo Mk 2 (x1.5), Mk 3 (x2), Mk 4 (x3) | timed fits, removals and welds (socket, point, freeform) go this much faster |
| Tractor beam | physics gun | off | Tractor Emitter | the beam catches things out to twice its reach and reels them in to it |
| Throw | physics gun | off | Kinetic Coil | holding something, Reload throws it along the look: as fast as the beam can push it in 0.3 s, at most 25 m/s |

A higher Mk can be fitted over a lower one, skipping those between. A module at or under what the player has is refused
with the reason, on the look line and on F.

**Where the modules come from.** Recipes are in `Constructs/Assembling.json` (from `tools/constructs/upgrade_parts.py`
through `assembling.py`): Mk 2 at a tier 1 or 2 Assembler from copper, steel, glass and bronze plus a component; Mk 4 and
Mk 5 at tier 4 with gold, levite and Field Coils. Factions teach the top ones (`teaches` in `Story/Factions`):

- Ministry of Slop, Contractor: **Heavy Lifting Permit** (Lift Coil Mk 4 and Mk 5).
- IT Department, Registered: **Remote Access** (Tractor Emitter, Beam Lens Mk 5).
- Clan MacSlop, Friend of the Clan: **The Caber Arm** (Kinetic Coil).
- The Call Centre, Valued Customer: **Productivity Drive** (Drill Motor Mk 4, Fitter Servo Mk 4).

**Seeing them.** The Tab inventory's **Upgrades** tab lists every stat: what the player has now, its Mk, and the next
module and what it gives. A loose module's look line says what it gives and "F: fit to your tools", or why it can't be
fitted. Fitting one shows a notice ("Fitted: physics gun lift 6,000 kg"). With the throw fitted, the physics gun's key
line adds "R: throw" while it holds something.

## The data and the code

- `Constructs/ToolUpgrades.json` (format `call-of-slop-tool-upgrades/1`, written by `tools/constructs/upgrade_parts.py`
  with the module parts in `Constructs/Parts/Upgrades`). Each stat: `stat` (one of `lift`, `reach`, `drillBit`,
  `drillMotor`, `fitter`, `tractor`, `throw`), `name`, `about`, `base`, `creative` and `levels` (`part`, `value`), each
  more than the last. A function is base 0 and one level of 1. Values are held to the code's limits (lift 50 kg to
  20,000 t, reach 2 to 30 m, the drill 0.5 to 3 or 5 times). A file that is refused leaves the built-in table (the same
  numbers) and `--check-construct-parts` says why, as it does for a level naming a part that isn't there.
- `Source/Constructs/ToolUpgrades.cs`: the table and `ToolStat`. A stat's byte is its column on the wire and in a save: a
  new stat goes at the end of the enum and none is renumbered.
- `Source/Constructs/ConstructEditsUpgrades.cs`: each player's levels (`ConstructEdits.Levels`, 16 columns a player),
  `LiftOf`, `PullOf`, `ReachOf`, `CatchOf`, `FitSeconds`, `FitUpgrade` (F on a module) and `Throw`. The physics gun
  (`ConstructGrab.cs`), the ground drill (`GameSandboxSculpt.cs`, `Program.SandboxSculpt.cs`) and the timed edits ask
  these, on the server and the client alike.
- **Network** (protocol 215): every player's levels follow their hands in the Sandbox mode state (state version 4, about
  10 times a second), so a client aims, reaches and refuses as the server will. The throw is `CActKind.GrabThrow`.
- **Saves**: a `TOOL` section in the world file (`WorldSaveWriter.ToolsTag`), each person's key, Steam id and levels;
  an older build skips it and everyone starts at the base. `Game/GameSandboxUpgrades.cs` catches the levels into the
  record and gives them back on arrival. A new match without a saved world starts everyone at the base; a death does not
  take upgrades away.

**Choices made (Karl may change them).** Start at 1,500 kg and 12 m; five Mks of lift up to 25 t; fitting by F on a loose
module rather than from the backpack; a module is used up; the nudge tool keeps its own 1,500 kg cap and 12 m reach and
isn't upgraded; the tractor beam and throw are physics gun functions; creative has every function.
