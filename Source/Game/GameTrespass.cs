namespace CallOfSlop;

/// <summary>
/// Trespass on held planets (economy step 7; Karl, 2026-11-22: "mining there is trespass", by "how many resources are taken", and "the whole
/// planet counts"). Anyone may mine anywhere, but what a person mines on a planet a power holds (Crew/Territory.json: more than its majority
/// of the planet's control) lowers that power's standing with them by the value of what was taken (<see cref="TradeValues"/>) times
/// <c>contracts.trespassPerValue</c> in Crew/Relations.json. A person whose faction holds an alliance or a trade pact with the holder mines
/// freely. Their constructs' drills, gatherers and buckets are summed by the construct world (<see cref="ConstructWorld.Yield"/>), read once a
/// second here; a Mining Shell's drill arm reports itself. The first time a person trespasses on a planet they are told, once a game.
/// </summary>
public sealed partial class Game
{
    private readonly float[] _trespassOwed = new float[MaxPlayers * 256]; // (standing owed short of a whole point, by player and power)
    private readonly HashSet<int> _trespassTold = new();

    /// <summary>Host: player <paramref name="player"/> mined <paramref name="units"/> of <paramref name="material"/> here.</summary>
    private void Trespass(byte player, byte material, int units)
    {
        if (!IsServer || !PowersActive || units <= 0 || player >= MaxPlayers || !Players.TryGetValue(player, out var who) || who.Bot != null)
            return; // (a person only: a crew's miner works for its power, and a player gone bills no one)
        var c = Controllers;
        var pl = -1;
        var here = World.Map.Name;
        for (var i = 0; i < c.PlanetCount; i++)
            if (c.Territory.Planets[i].Equals(here, StringComparison.OrdinalIgnoreCase))
                pl = i;
        if (pl < 0 || c.PlanetHolder[pl] is not (> 0 and var holder))
            return;
        var rel = _rel;
        var pact = rel.PartyPact(holder, FactionTable.Of[player]);
        if (pact is PactKind.Alliance or PactKind.Trade)
            return;
        var k = player * 256 + holder;
        _trespassOwed[k] += TradeValues.Of(material) * units * RelationRules.Current.TrespassPerValue;
        var whole = (int)_trespassOwed[k];
        if (_trespassTold.Add(k * 64 + pl))
            SandboxHands.Notices.Add((player, $"{here} is held by {CrewPowers.Of(holder)?.Name}: what you mine here costs you their goodwill."));
        if (whole <= 0)
            return;
        _trespassOwed[k] -= whole;
        rel.MovePlayer(holder, player, -whole);
    }

    /// <summary>Host, a person arrived: an id used before starts clean (nothing owed, told again).</summary>
    private void TrespassArrive(byte player)
    {
        if (player >= MaxPlayers)
            return;
        Array.Clear(_trespassOwed, player * 256, 256);
        _trespassTold.RemoveWhere(k => k / 64 / 256 == player);
    }

    /// <summary>Host, once a second: the players' constructs' mining since the last look, as trespass.</summary>
    private void TrespassTick()
    {
        var w = Constructs;
        w.YieldsOn = IsServer;
        for (var i = 0; i < w.YieldCount; i++)
            if (w.Yields[i].Kind == CYieldKind.Gathered && w.Yields[i].Owner < MaxPlayers)
                Trespass(w.Yields[i].Owner, w.Yields[i].Material, w.Yields[i].Units);
        w.YieldCount = 0;
    }
}
