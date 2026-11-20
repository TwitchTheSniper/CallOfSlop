using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Barter and the tab (Karl, 2026-11-20: option C of the "Trade without currency" study, credits removed everywhere at once). There is no
/// money: goods and parts are physical and carry a value (<see cref="TradeValues"/>). When a trade with a power is uneven, the difference
/// stays on a tab: what that power owes the person (in value units, the old credit prices), spendable only with that power. A sale to a
/// station of the Traders puts value on your tab with the Traders; a hire, a wage, a tribute or a purchase from them spends it. A power
/// that goes to war with you (its standing at or below the war line) or falls wipes its tab with you. Every place that paid credits pays
/// a tab now: stations (GameStationMarket.cs), hiring and wages (GameCrewHire.cs, with the hand's own power), a capture's bounty (the power
/// most hostile to the ship taken that trades with you), ransom (the officer's power), tribute and trade pacts (GameCrewPolitics.cs) and
/// contracts (the power that offered it). Creative: every tab is unlimited. Server side; each person's tabs reach their client in
/// <see cref="Msg.Tabs"/> when they change, and are saved with the world (the Crew section, version 2).
/// </summary>
public sealed partial class Game
{
    /// <summary>Power ids run 1..<see cref="CrewPowers.Max"/>; slot 0 is unused.</summary>
    public const int TabPowers = CrewPowers.Max + 1;

    private readonly long[] _tab = new long[256 * TabPowers];
    private readonly bool[] _tabDirty = new bool[256], _tabSeen = new bool[256];
    private long _tabWipeAt;

    /// <summary>Client: this person's tabs as the host last sent them, by power (<see cref="TabUnlimited"/>: Creative).</summary>
    public readonly long[] TabHere = new long[TabPowers];

    public bool TabUnlimited { get; private set; }

    /// <summary>The HUD's tab line, made when a tab changes ("" before the first word from the host).</summary>
    public string TabLine { get; private set; } = "";

    /// <summary>What <paramref name="power"/> owes <paramref name="player"/> (Creative: unlimited). On a client, its own from the host.</summary>
    public long TabOf(byte player, byte power)
    {
        if (SandboxInfiniteParts)
            return long.MaxValue;
        if (power == 0 || power >= TabPowers)
            return 0;
        return IsServer ? _tab[player * TabPowers + power] : player == LocalId ? TabHere[power] : 0;
    }

    private static string TabPowerName(byte power) => CrewPowers.Of(power)?.Name ?? "nobody";

    /// <summary>Server: <paramref name="player"/> spends <paramref name="cost"/> of their tab with <paramref name="power"/>. Null: paid; else why not.</summary>
    public string? TabPay(byte player, byte power, long cost)
    {
        if (SandboxInfiniteParts || cost <= 0)
            return null;
        if (power == 0 || power >= TabPowers)
            return "nobody to settle that with";
        ref var t = ref _tab[player * TabPowers + power];
        if (t < cost)
            return $"that costs {cost} on your tab with the {TabPowerName(power)}: they owe you {t} (trade them goods first)";
        t -= cost;
        _tabDirty[player] = true;
        return null;
    }

    /// <summary>Server: <paramref name="power"/> owes <paramref name="player"/> <paramref name="amount"/> more (a negative amount takes off, to 0).</summary>
    public void TabGive(byte player, byte power, long amount)
    {
        if (amount == 0 || power == 0 || power >= TabPowers)
            return;
        ref var t = ref _tab[player * TabPowers + power];
        t = Math.Max(0, t + amount);
        _tabDirty[player] = true;
    }

    /// <summary>Server, each tick: tabs to their owners when they changed; once a second, the tabs of powers at war with their holder or fallen wiped.</summary>
    private void TabTick()
    {
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.CrewSlot >= 0)
                continue;
            if (!_tabSeen[p.Id])
                _tabSeen[p.Id] = _tabDirty[p.Id] = true; // (first seen this run: their tabs to them)
            if (_tabDirty[p.Id])
            {
                _tabDirty[p.Id] = false;
                TabSend(p);
            }
        }
        if (ServerTick < _tabWipeAt)
            return;
        _tabWipeAt = ServerTick + World.TickRate;
        foreach (var p in Players.Values)
            for (var f = 1; f < TabPowers; f++)
            {
                var power = (byte)f;
                if (_tab[p.Id * TabPowers + f] <= 0 || CrewPowers.Of(power) == null)
                    continue;
                var fallen = PowersActive && power <= PowerControllers.N && Controllers.Powers[power] is { Fallen: true };
                if (!fallen && CrewStanding(power, p.Id) > Relations.Rules.WarLine)
                    continue;
                TabWipe(p, power, fallen);
            }
    }

    private void TabWipe(Player p, byte power, bool fallen)
    {
        var had = _tab[p.Id * TabPowers + power];
        _tab[p.Id * TabPowers + power] = 0;
        _tabDirty[p.Id] = true;
        SandboxHands.Notices.Add((p.Id, fallen ? $"The {TabPowerName(power)} have fallen: what they owed you ({had}) is gone."
            : $"The {TabPowerName(power)} are at war with you: they won't honour what they owed you ({had})."));
        Console.WriteLine($"[tab] player {p.Id}: tab of {had} with {TabPowerName(power)} wiped ({(fallen ? "fallen" : "war")})");
    }

    /// <summary><see cref="Msg.Tabs"/>: u8 unlimited, u8 n, each (u8 power, i64 owed).</summary>
    private void TabSend(Player p)
    {
        if (p.Id == LocalId)
        {
            for (var f = 0; f < TabPowers; f++)
                TabHere[f] = _tab[p.Id * TabPowers + f];
            TabSet(SandboxInfiniteParts);
        }
        if (Net.IsOffline || p.Id == LocalId || GuidOf(p) is not { } guid)
            return;
        var n = 0;
        for (var f = 1; f < TabPowers; f++)
            if (_tab[p.Id * TabPowers + f] > 0)
                n++;
        _w.Begin(Msg.Tabs).U8((byte)(SandboxInfiniteParts ? 1 : 0)).U8((byte)n);
        for (var f = 1; f < TabPowers; f++)
            if (_tab[p.Id * TabPowers + f] is var v and > 0)
                _w.U8((byte)f).U64((ulong)v);
        Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: <see cref="Msg.Tabs"/>.</summary>
    private void ReadTabs(ref PacketReader r)
    {
        if (!r.Has(2))
        {
            Console.WriteLine("[tab] a tabs message didn't read: dropped");
            return;
        }
        var unlimited = r.U8() != 0;
        int n = r.U8();
        Array.Clear(TabHere);
        for (var k = 0; k < n && r.Has(9); k++)
        {
            var f = r.U8();
            var v = (long)r.U64();
            if (f < TabPowers)
                TabHere[f] = v;
        }
        TabSet(unlimited);
    }

    private void TabSet(bool unlimited)
    {
        TabUnlimited = unlimited;
        if (unlimited)
        {
            TabLine = "Trade: unlimited (Creative)";
            return;
        }
        var sb = new System.Text.StringBuilder("Owed to you: ");
        var any = false;
        for (var f = 1; f < TabPowers; f++)
            if (TabHere[f] > 0)
            {
                sb.Append(any ? ", " : "").Append(TabPowerName((byte)f)).Append(' ').Append(TabHere[f]);
                any = true;
            }
        TabLine = any ? sb.ToString() : "Owed to you: nothing (trade goods at a station to open a tab)";
    }

    // ---- Saves (the Crew section) ------------------------------------------------------------------------------------------------------

    /// <summary>A person's tabs for their record, by power name.</summary>
    private (string Power, long Owed)[] TabsOf(byte player)
    {
        var n = 0;
        for (var f = 1; f < TabPowers; f++)
            if (_tab[player * TabPowers + f] > 0 && CrewPowers.Of((byte)f) != null)
                n++;
        var list = new (string, long)[n];
        n = 0;
        for (var f = 1; f < TabPowers; f++)
            if (_tab[player * TabPowers + f] is var v and > 0 && CrewPowers.Of((byte)f) is { } d)
                list[n++] = (d.Name, v);
        return list;
    }

    /// <summary>A person arrived: their tabs from their record (a power this build doesn't have is dropped with a line).</summary>
    private void TabsArrive(Player p, WorldPlayer rec)
    {
        for (var f = 0; f < TabPowers; f++)
            _tab[p.Id * TabPowers + f] = 0;
        foreach (var (name, owed) in rec.Tabs)
        {
            if (CrewPowers.Find(name) is var k and > 0 and < TabPowers)
                _tab[p.Id * TabPowers + k] = Math.Max(0, owed);
            else
                Console.WriteLine($"[tab] player {p.Id}: a tab with \"{name}\" ({owed}) names no power here: dropped");
        }
        _tabDirty[p.Id] = true;
    }
}
