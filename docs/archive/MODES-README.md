# Game modes: archived guide

The parts of Source/Game/Modes/README.md about the archived arena modes, moved verbatim on 2026-11-11. The rules that still bind stay there.

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
