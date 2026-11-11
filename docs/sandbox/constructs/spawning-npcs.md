# Spawning bots and zombies

Covers spawning bots and zombies in Sandbox. Part of the construct guide: index in ../CONSTRUCTS.md.

## Spawning bots and zombies

Board items dev-sandbox-spawn-npcs-sn01 and dev-sandbox-spawn-bots-zombies-sb02, done as one feature (2026-10-30). Code:
`Source/Game/GameSandboxSpawns.cs` (requests, the queue, sides, the spawners' tick, zombies on constructs, bots round
constructs), `Source/UI/UiSandboxSpawn.cs` (the inventory's Spawn tab), `Source/App/Program.SandboxSpawn.cs` (placing:
the ghost circle), parts in `Constructs/Parts/Spawn`. Protocol 92.

**One category, "Spawn"**: the inventory has a **Spawn** tab (the direct spawns, clear all, the host's settings and a
button for each spawner part), and the two spawner parts sit in the Parts tab's **Spawn** group (section "Spawner
parts"), so a search or the tree finds them like any other part.

**Direct spawns** (creative worlds only: a map with `"infiniteParts": false` refuses them, "its enemies come from the
world"): Msg.CAct `Spawn` (u8 what, u8 difficulty, u8 gun kind, u8 its value, u8 zombie mix, u32 count, the aimed spot)
and `ClearSpawns`. The server checks the permission (joined players may spawn unless the host turns that off in the Spawn
tab), the spot (`SandboxSpawnSpot`: ground within 3 m below, not inside a wall, not on a construct; at most 150 m away) and
the caps (a request over a cap is cut down to it, with a note), then queues it. The queue spreads big asks over ticks: a
4 bots a tick, 8 individual zombies a tick, 10,000 crowd zombies a tick (500,000 take 50 ticks). A tick's new bots share
one roster message.

**Joined players** have the same tab. Bots have no limit of their own any more (Karl, 2026-11-08: fights of hundreds):
`SpawnClientBots` is the whole bot cap, shared with everyone, which the host can lower in the Spawn tab ("Most bots";
192 by default, the most there are ids for). Zombies stay limited: `SpawnClientZombies` (64) out at once, spawners on
their constructs included, under the individual tier's cap (300 zombies at once, shared).
Their zombies are always individuals: the crowd and mass tiers have no owners, so they couldn't be counted or cleared per
player. A client's clear (**Clear mine**) takes only what is theirs, even when the host has turned their spawning off; the
host's Clear all takes everything. What a player spawned is nobody's once they leave, so the next person given that id
neither inherits it nor has it count against them.

**Sides**: a spawned bot is a full bot (the bot AI, the nav graph) with team 10 (friendly) or 11 (enemy), replicated in the
mode state like any team. The Sandbox's `AreEnemies`: enemy bots against everyone else and never each other; friendly bots
never against people or each other; people still free-for-all among themselves. A friendly bot follows whoever spawned it
(a spawner's holds near the spawner), and with zombies within 22 m fights them as a Zombies co-op bot does; an enemy bot
hunts. Spawned bots never respawn: 3 s after dying they leave.

**Ids and scale.** Every body needs a player id (a byte). Ids 0 to 31 are the lobby's seats, for people (and the arena
modes' bots); ids 32 to 223 (`Game.FreeNpcId`) are for bodies the world spawns: Spawn tab and spawner bots, the story's
people, the opening's rescuers, the colony's riot officers. So there are at most 192 such bodies at once
(`SpawnBotCapMax`), and a world full of them never stops a person joining. Owner bytes from 224 up are players away from
a saved world (`WorldSession.OfflineBase`; 31 records fit, a later one's things are nobody's while they're away). The
snapshot's presence mask has a bit per id (`PlayerMask`, protocol 212) and a snapshot splits into up to 15 parts. Far
bodies already go every other snapshot; the Sandbox hands state sends a spawned body only while its hand holds
something. `--bot-bench [sizes] [--seconds S] [--net]` measures a fight (README, "Testing without playing"): on the
cloud box 183 bots fighting cost about 6 ms a server tick (budget 8), 100 about 3 ms; a client gets about 115 KB/s of
snapshots with 190 bodies. What keeps it there: a crowded scan looks at the nearest 12 enemies in view, nearest first
(`BotScanLooksCrowded`), and a shot only rewinds bodies near its line (`ServerResolveShot`'s cull). Ragdolls stay
capped by their pool (`Ragdolls.Capacity`), so a battlefield keeps the newest bodies.

**Zombies**: the Sandbox gives the Zombies AI a stand-in Zombies map (`SandboxZMap`: one area over the whole map, one
spawner out of the ground, no doors, windows or buyables, a crowd so the mass horde is on), ticked from the Sandbox tick
with none of the mode's rounds, points or downs: a zombie's hit is an ordinary hit and a death "killed by the enemy".
Zombies hunt people and friendly bots (`ZPrey`), not enemy bots. Up to 64 come as individuals rising at the spot (exactly
that many); a bigger group goes into the crowd round the spot, the first 4,000 as particles and the rest straight into
the mass tier (a count per cell), and the tiers wake the nearest as individuals as on a crowd map. `Game.StressMax` is now
500,000 (the Horde Stress lobby's custom size too). Constructs keep individuals out as they keep players out, and one held
off by a construct swipes the part in its way like a building door (40 damage, every windup + 0.6 s): doors and panels
give way. Crowd and mass zombies pass through constructs (they are far away). The zombie messages (ZEnts, ZEvent, ZCrowd)
go to Sandbox clients as in Zombies. Construct guns hit zombies of every tier (2026-11-09): individuals by their hit boxes, crowd particles and mass
zombies by the same capsules a player's shot meets (`Game.CwCrowdRay`), and their shells and explosive rounds blast
individuals, the crowd and the mass (`BlastZombies` was Zombies-mode only). A hand gun's straight shot that meets nothing
within its reach (World.Range, 200 m) goes on into the far crowd up to the fog (at most `CrowdShotReach`, 1200 m),
stopped by the ground and constructs (`CrowdShotFar`, server and the client's prediction alike); a round that falls flies
on as a round of its own (ConstructRounds.cs). Damage falls off with range as ever.

**Spawner parts** (`"logic": {"kind": "botspawner"}` / `"zombiespawner"`, role logic, the attach tool): settings from the
configure tool (team, difficulty, gun class; or walkers / mixed types; rate a minute, group size, max alive, total (0: no
limit), radius, start on; a part takes up to 12 settings now) and named **data ports**: inputs **enabled** (port 0, the
part itself), **spawn now**, **rate**; outputs **alive** (port 0: how many it has out) and **spawned** (a one-tick pulse).
A data link names a port in its port byte (`0x80 | k`, k > 0; 255 is port 0), the linking tool's picker lists them, a
blueprint link says `"toPort": "spawn now"` / `"fromPort": "spawned"`, and the logic's wires carry the ports
(`CWire.FromPort` / `ToPort`). Enabled (its input on, or unwired and set to start on), a spawner spawns a group at once,
then one each 60 / rate seconds, while it has fewer than its max alive out and its total isn't reached (and the host's
caps allow); a rising "spawn now" spawns a group at once, on or off. What a spawner made carries its construct and the
part's stable tag, so it counts them back however the parts are renumbered; its zombies are never put back into the
crowd (they'd stop counting). It shows a light (blue, green) while on. A wire from another construct goes into port 0.

**Bots and constructs**: after a bot steers, a solid part box of a construct in the way (in the part's frame, a player's
radius round it, from a step up to head height: a floor it can step onto or a doorway between walls is not in the way)
turns it along the box's side towards its goal, and a bot held up against one for 1.5 s marks the path edge it was on
blocked for 8 s and plans again (A* goes round). Bots already step up onto builds with the players' mover. The nav graph
stays the map's (deterministic). Follow-ups for bots inside built buildings (from the building pieces work): the tops of
walkable parts as nav nodes, nav links through door openings (opened with Interact first), and a nav patch when a
construct is built, changed or broken.

### Self-test

**Sandbox spawns** (`Source/App/SelfTestSandboxSpawns.cs`): the parts' files and ports; 3 friendly and 3 enemy bots take
sides (friendly bots aim at enemy bots, never the host; an SMG class holds); mid-air, a survival world and the bot cap
refused; clear all; 7 zombies spawn exactly 7; 500,000 accepted and in the mass tier after 60 ticks, the tick time and
no allocation in 60 steady spawn ticks; a cap of 1,000 holds; a friendly bot reaches the host past a construct; a Zombie
Spawner on a switch (off: none; on: one at once, one a second, at most 3 alive, its alive output 3, its spawned pulse
toggles a toggle; refilled to its total of 5; spawn now off); a Bot Spawner's two enemy Easy shotgun bots; blueprint links
and the construct message round trip with their ports and settings; over the real transport a client refused until the
host allows everyone, then its zombies on both sides, a friendly bot and a clear all.

### Choices made (the developer may change them)

1. One "Spawn" tab plus the spawner parts in the Parts tab's Spawn group (not a Characters group).
2. Spawner parts work in survival worlds too (only the direct spawns are creative); a survival world gets them only as
   its rack or salvage hands them out.
3. Host caps: 16 bots (at most 28: player ids), 500,000 zombies; the host settings aren't saved between sessions.
4. Individuals up to 64 a group; spawner zombies are individuals only (max alive at most 64 a spawner).
5. Friendly bots follow the spawning player (3 m off); spawner friendlies hold near the spawner; enemy bots hunt.
6. Zombies leave enemy bots alone; enemy bots ignore zombies.
7. A construct swipe is a building door's (40 damage); the crowd and mass tiers ignore constructs.
