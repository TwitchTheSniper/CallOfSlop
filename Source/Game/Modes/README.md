# Game modes: how to add one

Every game mode is a **module**: one class deriving from `GameModeModule` (`GameModeModule.cs`), registered in one place
(`GameModeRegistry.cs`). The game, the bots, the network, the lobby, the server browser, the map vote, the HUD and the
self-test all go through the module's hooks, so a new mode is a new folder here and one line in the registry. Nothing
else needs a `switch` on the mode.

```
Source/Game/Modes/
  GameModeModule.cs            the base class: identity, capability flags, hooks (read this first)
  GameModeRegistry.cs          the one list of modes (menu order); ids are the wire format
  FreeForAll/                  FreeForAllModule         id 0  the original rules (the base class defaults)
  TeamDeathmatch/              TeamDeathmatchModule     id 1
  KingOfTheHill/               KingOfTheHillModule      id 2  owns the hill state, HUD, bots, drawing
  CaptureTheFlag/              CaptureTheFlagModule     id 3  owns FlagState x2, HUD, bots, drawing
  GunGame/                     GunGameModule + GunGame  id 4  the ladder, fixed perks, no loadout
  Zombies/                     ZombiesModule            id 5  routes into the Zombies subsystems (ZombiesMode.cs, ZombieAI.cs...)
  LastManStanding/             LastManStandingModule    id 6  the TEMPLATE (Dev-only): copy this
  Coop/                        CoopModule               id 7  co-op PvE missions; routes into Source/Game/Pve (enemies, director, objectives)
```

What stays shared, in `Game` (GameModes.cs and friends): the `GameMode` enum, team assignment / autobalance / team
spawns (used by every `TeamBased` mode), `TeamScore`, the score limit scaling (ScoreLimits.cs), the match phases and
clock (Match.cs), ready-up, the map vote, and the shared part of the mode-state packet. A module never needs to touch
those.

## Steps

1. **Add an id** to `enum GameMode` in `Source/Game/GameModes.cs`, at the end. Ids are bytes on the wire (packets,
   server info, `settings.txt`): never renumber or reuse a shipped one.
2. **Copy the template**: `Source/Game/Modes/LastManStanding/LastManStandingModule.cs` to
   `Source/Game/Modes/<YourMode>/<YourMode>Module.cs`. Rename the class, set `Id`, `Name`, `Short` (3-4 capitals) and
   `Rules()`. Keep player-visible text ASCII (the self-test checks).
3. **Register it**: one line in `GameModeRegistry.Factories`. The position is the menu order.
4. **Set the capability flags** (all virtual, all default to free-for-all behaviour):
   `TeamBased`, `CoOp`, `UsesLoadout`, `AllowsStreaks`, `FixedPerks`, `UsesThrowables`, `ServerPicksWeapons`,
   `UsesMapVote`, `HasTimeLimit`, `ScoreRule` + `ScoreUnit`, `Needs` (what the map must have), `DevOnly`,
   `ShowsRespawnMessage`, `ShowsKillNotices`, `MaxPlayers(map)`, `Fits(map)` / `MapWarning`.
5. **Override the hooks you need** (see the table). Anything you don't override behaves like free-for-all.
6. **Replicated state**: keep it in fields on the module, write it in `WriteState`, read it in `ReadState`, bump
   `StateVersion` whenever the layout changes. The packet carries your tail with a version and a length, so a client on
   another version skips it instead of misreading it. Big or frequent state (Zombies' horde) gets its own `Msg` ids,
   handled in `HandlePacket`.
7. **Run the self-test** (CI does it on every push): the "Mode modules" section lists every registered mode, checks ids
   and ASCII text, round-trips the state tail, and plays a short bot match in each mode, calling `SelfTestVerify` at the
   end. Add a section of your own for rules worth checking (see `SelfTestModes.cs` for the shipped modes).
8. **Ship it**: drop `DevOnly` once the mode is ready. Dev-only modes show up in the lobby only for maps of a dev pack
   (Dev: Zombies Test), so they can be played and smoke-tested without being offered to everyone.

## The hooks

| Hook | Runs on | When | Shipped examples |
| --- | --- | --- | --- |
| `OnReset(game)` | server, every module | a new match or a mode change: clear your state | KotH hill, CTF flags, Gun Game levels, Zombies run |
| `OnMapChanged(game)` | server, every module | the map was hot-swapped | KotH, CTF (objectives come from the new map) |
| `OnClientMatchStart(game)` | client, every module | a match started | Zombies (doors shut, no zombies) |
| `OnSpawn(game, p)` | server, active | after the normal spawn + loadout | Gun Game (ladder gun), Zombies (start gear) |
| `OnKill(game, attacker, victim, killId, now)` | server, active | a kill was counted | TDM (team score), Gun Game (ladder), LMS |
| `OnPlayerLeave(game, p)` | server, active | someone left | LMS |
| `CanRespawn(game, p, now)` | server, active | a dead player wants back | LMS (one life) |
| `ServerTickPlaying(game, now)` | server, active | every tick while playing: objectives, win check, time limit | all; `TeamWinCheck` / `FreeForAllWinCheck` do the common endings |
| `HandlePacket(game, id, ref r, sender, now)` | both, every module | a `Msg` id the core doesn't know | Zombies (ZUse, ZEnts, ZWorld...) |
| `BotTick(game, bot, brain, now)` | server, active | take over a bot's whole tick | Zombies (co-op bots) |
| `BotObjective(game, bot)` | server, active | where a bot with nobody in sight heads (routing is done for you) | KotH, CTF, LMS |
| `WriteState` / `ReadState` / `StateVersion` | server / client | the mode-state tail, ~10 Hz | KotH (7 bytes), CTF (36), LMS (1) |
| `GoalText(game)` | client | the match log / warmup bar line | FFA, Zombies |
| `HudLines(game, me, lines, ref pointAt)` | client | the objective panel under the match bar + an arrow target | KotH, CTF, Gun Game, LMS |
| `ScoreBar(game)` | client | the match bar for non-team modes (team modes get the Blue - Red bar) | Gun Game, LMS |
| `ScoreboardColumns` / `ScoreboardColumnsShort` / `ScoreboardCells` / `RankScore` / `ScoreboardTag` | client | the scoreboards | Gun Game (Gun / Kills), CTF ([FLAG]) |
| `ResultsLine(game)` | client | a line on the results screen for non-team modes | Gun Game |
| `DrawObjectives(gfx, game, now)` | client | world objectives (rings, halos, beams: `ObjectiveRender` has the pieces) | KotH, CTF |
| `SelfTestVerify(game)` | self-test | after the short bot match: a problem, or null | TDM, Gun Game, Zombies, LMS |

What the module can use on `Game`: `Players`, `Local` / `LocalId`, `NameOf`, `TeamScore`, `ScoreLimit`, `TimeLeft`,
`Phase`, `Objectives` (team spawns, hills, flags), `Standings()`, `SoloMatch`, `ModeEvent(text, sound)` (a kill-feed
line for everyone), `EndMatch(winner, text, now, winnerCode)`, `SyncLocalSlots`, and anything public. `game.Module` is
the active module; `game.ModuleOf<T>()` another mode's instance.

## A minimal module

```csharp
public sealed class InfectedModule : GameModeModule
{
    public override GameMode Id => GameMode.Infected;      // 1. a new enum value
    public override string Name => "Infected";
    public override string Short => "INF";
    public override string Rules(int limit = 0) => "One player starts infected. Infected kill to spread; the last survivor wins.";
    public override bool DevOnly => true;                  // until it's ready

    private readonly HashSet<byte> _infected = new();     // the mode's own state

    public override void OnReset(Game game) => _infected.Clear();

    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now)
    {
        if (_infected.Contains(attacker.Id) && _infected.Add(victim.Id))
            game.ModeEvent($"{victim.Name} is infected!", Sfx.HitTick);
    }

    public override void ServerTickPlaying(Game game, double now)
    {
        if (_infected.Count == 0 && game.Players.Count > 1)
            _infected.Add(game.Players.Values.OrderBy(p => p.Id).First().Id);
        var survivors = game.Players.Values.Where(p => !_infected.Contains(p.Id)).ToList();
        if (survivors.Count == 1 && game.Players.Count > 1)
            game.EndMatch(survivors[0], $"{game.NameOf(survivors[0].Id)} survived", now);
        else if (game.TimeLeft <= 0)
            game.EndMatch(null, $"Time! {survivors.Count} survived", now);
    }

    public override void WriteState(Game game, PacketWriter w)
    {
        w.U8((byte)_infected.Count);
        foreach (var id in _infected) w.U8(id);
    }

    public override void ReadState(Game game, ref PacketReader r)
    {
        _infected.Clear();
        int n = r.U8();
        for (var i = 0; i < n && r.Has(1); i++) _infected.Add(r.U8());
    }

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt) =>
        lines.Add(new HudLine(_infected.Contains(me.Id) ? "INFECTED - spread it" : $"{game.Players.Count - _infected.Count} survivors left", HudOrange));
}
```

Then `() => new InfectedModule()` in `GameModeRegistry.Factories`, and it plays: the lobby lists it (on dev-pack maps
while `DevOnly`), bots fight in it, clients see its HUD line, the self-test plays a bot match in it.

## Rules of the road

- A mode's state lives on the module, never on `Game`. (`Game.HillOwner`, `Game.Flags` and friends are forwarders
  kept for the older code and the self-tests.)
- Reset hooks run for **every** module, active or not, so no stale state leaks between matches in different modes.
  Gameplay hooks run for the active module only.
- The protocol version (ServerBrowser.cs) covers the mode-state layout; the per-module `StateVersion` covers your tail.
  Bump `StateVersion` for a tail change; bump the protocol only when the shared part changes.
- Behaviour that is really a *mode rule* belongs in a flag or hook, not in an `if (game.Mode == ...)` somewhere else.
  The Zombies subsystems still check `Mode == GameMode.Zombies` in their own files; that's the one exception, they are
  the mode's own code.
- Keep text ASCII, and draw non-literal text with `ImGuiSafe`.
