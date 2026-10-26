using System.Numerics;

namespace CallOfSlop;

/// <summary>A slot machine's state (server-owned, mirrored in <see cref="Msg.ZWorld"/>).</summary>
public enum ZSlotState : byte
{
    Idle,

    /// <summary>The reels spin (the result is already rolled: clients stop the reels on it one by one).</summary>
    Spinning,

    /// <summary>The result shows for a moment (paid at the start of it); then the machine takes another pull.</summary>
    Showing,
}

/// <summary>What a pull paid.</summary>
public enum ZSlotResult : byte
{
    Nothing,
    Scrap,
    Points,
    Part,
}

/// <summary>One slot machine on the map: its state, heat (the pity timer) and the last pull's reels and result.</summary>
public sealed class ZSlotMachine
{
    public ZSlotState State;

    /// <summary>When the state ends (the server's clock; clients set it from the seconds left they're sent).</summary>
    public double Until;

    /// <summary>Seconds left in the state (the server works it out every tick; clients get it in ZWorld).</summary>
    public float Left;

    public byte Owner = byte.MaxValue;

    /// <summary>Pulls since the last rare-or-better part (<see cref="ZombiesRules.SlotPityTier"/>).</summary>
    public byte Heat;

    /// <summary>Counts pulls (wrapping), so a client sees a new spin even when the state byte didn't change between two messages.</summary>
    public byte SpinId;

    /// <summary>The three reel symbols of the last pull (<see cref="Game.SlotSymSkull"/> ...).</summary>
    public readonly byte[] Symbols = new byte[3];

    public ZSlotResult Result;
    public Rarity Tier;

    /// <summary>The part paid (an index into <see cref="Parts.All"/>), or -1.</summary>
    public short Part = -1;

    /// <summary>The price paid for this pull (points back pay a share of it).</summary>
    public int Paid;

    /// <summary>The paid part was turned into scrap (you already held <see cref="ZombiesRules.SlotAutoScrapAt"/> of it loose).</summary>
    public bool AutoScrapped;

    // Client animation (Program.ZombiesSlots.cs): when the spin seen started, and how many reels have clicked to a stop.
    public byte SeenSpin;
    public double SpinStart = -1e9;
    public int ReelsStopped = 3;

    public void Reset()
    {
        State = ZSlotState.Idle;
        Until = 0;
        Left = 0;
        Owner = byte.MaxValue;
        Heat = 0;
        SpinId = 0;
        Symbols[0] = Symbols[1] = Symbols[2] = 0;
        Result = ZSlotResult.Nothing;
        Tier = Rarity.Common;
        Part = -1;
        Paid = 0;
        AutoScrapped = false;
        SeenSpin = 0;
        SpinStart = -1e9;
        ReelsStopped = 3;
    }
}

/// <summary>
/// Zombies slot machines (board item "Zombies slots", 2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Slot machines"): gamble
/// points for loose weapon-builder parts. Walk up, hold the interact key, pay; three reels spin and pay a part (into your
/// loose parts, for the workbench), scrap, points back or nothing. Three levers raise the rarity, all data: the round
/// (DropTables.json "slotTiers"), the machine's heat (every pull without a rare-or-better part adds one; every
/// <see cref="ZombiesRules.SlotHeatStep"/> heat moves the part odds one tier up) and jackpot rounds (every
/// <see cref="ZombiesRules.SlotJackpotEvery"/>th: the wonder symbol is on the reels and legendary / wonder get a bonus).
/// At most <see cref="ZombiesRules.SlotMaxNothing"/> nothing-results in a row per player per machine. The server rolls,
/// clients animate the reels (they get the symbols in <see cref="Msg.ZWorld"/>, protocol 51). Nothing here allocates per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Reel symbols: a skull (nothing), a cog (scrap), a coin (points back), then a gem per rarity (<see cref="SlotSymGem"/> + the tier).</summary>
    public const byte SlotSymSkull = 0, SlotSymScrap = 1, SlotSymCoin = 2, SlotSymGem = 3;

    /// <summary>Every machine's state (only the first <see cref="ZombiesMap.SlotMachines"/>.Count are used).</summary>
    public readonly ZSlotMachine[] ZSlots = MakeSlots();

    private static ZSlotMachine[] MakeSlots()
    {
        var a = new ZSlotMachine[ZombiesMap.MaxSlotMachines];
        for (var i = 0; i < a.Length; i++)
            a[i] = new ZSlotMachine();
        return a;
    }

    private void ResetSlots()
    {
        foreach (var m in ZSlots)
            m.Reset();
    }

    /// <summary>The price of a pull on machine <paramref name="i"/> this round (the map's own price, or the rules' rising one).</summary>
    public int SlotCost(ZombiesMap zm, int i) => i >= 0 && i < zm.SlotMachines.Count && zm.SlotMachines[i].Cost > 0 ? zm.SlotMachines[i].Cost : ZRules.SlotCost(ZRound);

    /// <summary>The interact prompt at machine <paramref name="i"/> (FindZUse).</summary>
    private ZUse SlotUse(ZombiesMap zm, int i)
    {
        var m = ZSlots[i];
        var cost = SlotCost(zm, i);
        if (zm.SlotMachines[i].NeedsPower && !ZPower)
            return new ZUse(ZUseKind.SlotMachine, i, cost, "Slot machine: needs the power on", Blocked: true);
        if (m.State != ZSlotState.Idle)
            return new ZUse(ZUseKind.SlotMachine, i, cost, m.State == ZSlotState.Spinning ? "The reels are spinning..." : SlotResultText(m), Blocked: true);
        var step = Math.Max(1, ZRules.SlotHeatStep);
        var steps = Math.Min(ZRules.SlotHeatMaxSteps, m.Heat / step);
        var heat = steps >= ZRules.SlotHeatMaxSteps ? "heat MAX" : $"heat {m.Heat % step}/{step}{(steps > 0 ? $" (+{steps})" : "")}";
        return new ZUse(ZUseKind.SlotMachine, i, cost, $"Hold [E] to pull the slot machine [{cost}] - {heat}{(ZRules.SlotJackpot(ZRound) ? " - JACKPOT ROUND" : "")}");
    }

    /// <summary>What the last pull of <paramref name="m"/> paid, for the prompt and the feed.</summary>
    public string SlotResultText(ZSlotMachine m) => m.Result switch
    {
        ZSlotResult.Part when m.Part >= 0 && m.Part < Parts.All.Length => m.AutoScrapped
            ? $"{PartDef.RarityName(m.Tier)}: {Parts.All[m.Part].Name} (you have plenty: scrapped for {Scrap.SalvageValue(Parts.All[m.Part])})"
            : $"{PartDef.RarityName(m.Tier)}: {Parts.All[m.Part].Name}!",
        ZSlotResult.Scrap => $"Scrap: +{ZRules.SlotScrap}",
        ZSlotResult.Points => $"Coins: +{SlotPointsBack(m.Paid)} points",
        _ => "Nothing. Better luck next pull.",
    };

    private int SlotPointsBack(int paid) => (int)MathF.Round(paid * ZRules.SlotPointsBack / 10f) * 10;

    // ---- Server ---------------------------------------------------------------------------------------

    /// <summary>Server: <paramref name="p"/> paid for a pull on machine <paramref name="i"/>: roll it now, pay it when the reels stop.</summary>
    private void PullSlot(Player p, ZPlayer zp, ZombiesMap zm, int i, int cost, double now)
    {
        var m = ZSlots[i];
        m.State = ZSlotState.Spinning;
        m.Until = now + ZRules.SlotSpinSeconds;
        m.Owner = p.Id;
        m.SpinId++;
        m.Paid = cost;
        m.AutoScrapped = false;
        var jackpot = ZRules.SlotJackpot(ZRound);
        var (result, tier) = RollSlotOutcome(ZRules, RunTables.Current, ZRound, m.Heat, ZNext());
        if (result == ZSlotResult.Nothing && ZRules.SlotMaxNothing > 0 && zp.SlotNothing[i] >= ZRules.SlotMaxNothing)
            result = ZSlotResult.Scrap; // bad-luck protection: never more than maxNothing blanks in a row
        m.Part = -1;
        if (result == ZSlotResult.Part)
        {
            m.Part = PickSlotPart(tier, ZNext());
            if (m.Part < 0)
                result = ZSlotResult.Scrap; // no part of that tier (a modded part set): scrap instead
        }
        m.Result = result;
        m.Tier = tier;
        SlotSymbols(result, tier, jackpot, ZNext(), m.Symbols);
        Audio.Effect3D(Sfx.StreakCall, zm.SlotMachines[i].At + new Vector3(0, 1.2f, 0), 0.6f);
    }

    /// <summary>A part of <paramref name="tier"/> (the same pool as zombie part drops), or -1. No allocation.</summary>
    public static short PickSlotPart(Rarity tier, uint seed)
    {
        var all = Parts.All;
        var n = 0;
        foreach (var part in all)
            if (part.Rarity == tier)
                n++;
        if (n == 0)
            return -1;
        var k = (int)((seed * 2654435761u) % (uint)n);
        for (var i = 0; i < all.Length; i++)
            if (all[i].Rarity == tier && k-- == 0)
                return (short)i;
        return -1;
    }

    /// <summary>Server, every tick: a spin that ends pays out; a result that has shown long enough frees the machine.</summary>
    private void TickSlots(ZombiesMap zm, double now)
    {
        for (var i = 0; i < zm.SlotMachines.Count && i < ZSlots.Length; i++)
        {
            var m = ZSlots[i];
            if (m.State == ZSlotState.Spinning && now >= m.Until)
            {
                m.State = ZSlotState.Showing;
                m.Until = now + ZRules.SlotShowSeconds;
                PaySlot(zm, i, m);
            }
            else if (m.State == ZSlotState.Showing && now >= m.Until)
            {
                m.State = ZSlotState.Idle;
                m.Owner = byte.MaxValue;
            }
            m.Left = m.State == ZSlotState.Idle ? 0 : (float)Math.Max(0, m.Until - now);
        }
    }

    // ---- Network (protocol 51: the end of Msg.ZWorld) ------------------------------------------------------

    /// <summary>Bytes per machine at the end of <see cref="Msg.ZWorld"/>.</summary>
    public const int ZSlotBytes = 11;

    /// <summary>Server: every machine (state, heat, owner, spin counter, the three symbols, result and tier, the part, tenths of a second left).</summary>
    private void WriteSlots(PacketWriter w)
    {
        var n = World.Map.Zombies is { } zm ? Math.Min(zm.SlotMachines.Count, ZSlots.Length) : 0;
        w.U8((byte)n);
        for (var i = 0; i < n; i++)
        {
            var m = ZSlots[i];
            w.U8((byte)m.State).U8(m.Heat).U8(m.Owner).U8(m.SpinId).U8(m.Symbols[0]).U8(m.Symbols[1]).U8(m.Symbols[2])
                .U8((byte)((byte)m.Result | ((byte)m.Tier << 3) | (m.AutoScrapped ? 128 : 0))).U16((ushort)m.Part)
                .U8((byte)Math.Clamp((int)MathF.Ceiling(m.Left * 10), 0, 255));
        }
    }

    /// <summary>Client: the machines from <see cref="Msg.ZWorld"/>.</summary>
    private void ReadSlots(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        int n = r.U8();
        for (var i = 0; i < n && r.Has(ZSlotBytes); i++)
        {
            var m = i < ZSlots.Length ? ZSlots[i] : null;
            var state = (ZSlotState)Math.Min(r.U8(), (byte)ZSlotState.Showing);
            var heat = r.U8();
            var owner = r.U8();
            var spin = r.U8();
            byte s0 = r.U8(), s1 = r.U8(), s2 = r.U8();
            var res = r.U8();
            var part = (short)r.U16();
            var left = r.U8() / 10f;
            if (m == null)
                continue;
            m.State = state;
            m.Heat = heat;
            m.Owner = owner;
            m.SpinId = spin;
            m.Symbols[0] = s0;
            m.Symbols[1] = s1;
            m.Symbols[2] = s2;
            m.Result = (ZSlotResult)Math.Min(res & 7, (int)ZSlotResult.Part);
            m.Tier = (Rarity)Math.Min((res >> 3) & 7, (int)Rarity.Wonder);
            m.AutoScrapped = (res & 128) != 0;
            m.Part = part;
            m.Left = left;
        }
    }

    private void PaySlot(ZombiesMap zm, int i, ZSlotMachine m)
    {
        // Heat: a rare-or-better part resets it, anything else warms the machine up.
        if (m.Result == ZSlotResult.Part && m.Tier >= ZRules.SlotPityTier)
            m.Heat = 0;
        else if (m.Heat < 255)
            m.Heat++;
        if (!Players.TryGetValue(m.Owner, out var p))
            return; // (the player left mid-spin)
        var zp = ZP(p);
        zp.SlotNothing[i] = m.Result == ZSlotResult.Nothing ? (byte)Math.Min(255, zp.SlotNothing[i] + 1) : (byte)0;
        var at = zm.SlotMachines[i].At + new Vector3(0, 1.2f, 0);
        switch (m.Result)
        {
            case ZSlotResult.Part when m.Part >= 0 && m.Part < Parts.All.Length:
            {
                var part = Parts.All[m.Part];
                if (ZRules.SlotAutoScrapAt > 0 && zp.Parts.GetValueOrDefault(part.Name) >= ZRules.SlotAutoScrapAt)
                {
                    m.AutoScrapped = true;
                    zp.Scrap += Scrap.SalvageValue(part);
                }
                else
                    zp.Parts[part.Name] = zp.Parts.GetValueOrDefault(part.Name) + 1;
                SendZRun(p);
                if (m.Tier >= Rarity.Legendary)
                    ModeEvent($"{p.Name} hit the jackpot on a slot machine: {PartDef.RarityName(m.Tier)} {part.Name}!", Sfx.StreakReady);
                else
                    ZFeedTo(p, $"Slot machine: {SlotResultText(m)}", Sfx.LevelUp);
                Audio.Effect3D(m.Tier >= Rarity.Epic ? Sfx.StreakReady : Sfx.LevelUp, at, 0.8f);
                break;
            }
            case ZSlotResult.Scrap:
                zp.Scrap += ZRules.SlotScrap;
                SendZRun(p);
                ZFeedTo(p, $"Slot machine: {SlotResultText(m)} ({zp.Scrap} now)", Sfx.BreakMetal);
                break;
            case ZSlotResult.Points:
                AddPoints(p, SlotPointsBack(m.Paid));
                ZFeedTo(p, $"Slot machine: {SlotResultText(m)}", Sfx.HitTick);
                break;
            default:
                ZFeedTo(p, "Slot machine: nothing.", Sfx.DryFire);
                break;
        }
    }

    // ---- The odds and the reels (static, deterministic: the self-test rolls thousands) ------------------------

    /// <summary>
    /// What a pull pays in <paramref name="round"/> on a machine with <paramref name="heat"/>: the "slotTiers" row for
    /// the round; outside jackpot rounds wonder parts can't come up, in them legendary and wonder get the rules' bonus;
    /// then every <see cref="ZombiesRules.SlotHeatStep"/> heat moves the part weights one tier up (the top tier keeps
    /// what reaches it). Deterministic from <paramref name="seed"/>; no allocation.
    /// </summary>
    public static (ZSlotResult Result, Rarity Tier) RollSlotOutcome(ZombiesRules rules, RunTables tables, int round, int heat, uint seed)
    {
        Span<float> w = stackalloc float[RunTables.SlotKeys.Length];
        SlotWeights(rules, tables, round, heat, w);
        var total = 0f;
        foreach (var x in w)
            total += x;
        if (total <= 0)
            return (ZSlotResult.Nothing, Rarity.Common);
        var h = seed * 2654435761u;
        h ^= h >> 15;
        var pick = (h % 1000000u) / 1000000f * total;
        var k = w.Length - 1;
        for (var i = 0; i < w.Length; i++)
        {
            pick -= w[i];
            if (pick < 0 && w[i] > 0)
            {
                k = i;
                break;
            }
        }
        while (k > 0 && w[k] <= 0)
            k--;
        return k switch
        {
            RunTables.SlotNothing => (ZSlotResult.Nothing, Rarity.Common),
            RunTables.SlotScrap => (ZSlotResult.Scrap, Rarity.Common),
            RunTables.SlotPoints => (ZSlotResult.Points, Rarity.Common),
            _ => (ZSlotResult.Part, (Rarity)(k - RunTables.SlotPart)),
        };
    }

    /// <summary>The weights a pull rolls from (see <see cref="RollSlotOutcome"/>), into <paramref name="w"/> (<see cref="RunTables.SlotKeys"/> order).</summary>
    public static void SlotWeights(ZombiesRules rules, RunTables tables, int round, int heat, Span<float> w)
    {
        w.Clear();
        RoundRow? row = null;
        foreach (var r in tables.SlotTiers)
            if (r.FromRound <= Math.Max(1, round))
                row = r;
        row ??= tables.SlotTiers.Count > 0 ? tables.SlotTiers[0] : null;
        if (row == null)
        {
            w[RunTables.SlotNothing] = 1;
            return;
        }
        for (var i = 0; i < w.Length && i < row.Weights.Length; i++)
            w[i] = row.Weights[i];
        var jackpot = rules.SlotJackpot(round);
        var legendary = RunTables.SlotPart + (int)Rarity.Legendary;
        var wonder = RunTables.SlotPart + (int)Rarity.Wonder;
        if (jackpot)
        {
            w[legendary] += rules.SlotJackpotLegendary;
            w[wonder] += rules.SlotJackpotWonder;
        }
        else
            w[wonder] = 0; // the wonder symbol is only on the reels in jackpot rounds
        var top = jackpot ? wonder : legendary;
        var steps = Math.Min(rules.SlotHeatMaxSteps, heat / Math.Max(1, rules.SlotHeatStep));
        for (var s = 0; s < steps; s++)
        {
            w[top] += w[top - 1];
            for (var t = top - 1; t > RunTables.SlotPart; t--)
                w[t] = w[t - 1];
            w[RunTables.SlotPart] = 0;
        }
    }

    /// <summary>
    /// How three reel symbols read (the player-facing rules, also what the reels are made from): three of a gem pay a part
    /// of its tier; two of a gem pay a part one tier lower (two commons: scrap); otherwise a cog pays scrap, a coin points
    /// back, and skulls and odd gems nothing.
    /// </summary>
    public static (ZSlotResult Result, Rarity Tier) ReadSlotSymbols(ReadOnlySpan<byte> s)
    {
        if (s[0] >= SlotSymGem && s[0] == s[1] && s[1] == s[2])
            return (ZSlotResult.Part, (Rarity)(s[0] - SlotSymGem));
        byte pair = 0;
        if (s[0] >= SlotSymGem && (s[0] == s[1] || s[0] == s[2]))
            pair = s[0];
        else if (s[1] >= SlotSymGem && s[1] == s[2])
            pair = s[1];
        if (pair != 0)
            return pair == SlotSymGem ? (ZSlotResult.Scrap, Rarity.Common) : (ZSlotResult.Part, (Rarity)(pair - SlotSymGem - 1));
        for (var i = 0; i < 3; i++)
            if (s[i] == SlotSymScrap)
                return (ZSlotResult.Scrap, Rarity.Common);
        for (var i = 0; i < 3; i++)
            if (s[i] == SlotSymCoin)
                return (ZSlotResult.Points, Rarity.Common);
        return (ZSlotResult.Nothing, Rarity.Common);
    }

    /// <summary>
    /// Reels that read as <paramref name="r"/> / <paramref name="tier"/> (<see cref="ReadSlotSymbols"/>), varied by
    /// <paramref name="seed"/>: a part comes up as three of its gem or as two of the next gem up; near misses (odd gems)
    /// fill the rest. The wonder gem only shows in jackpot rounds. No allocation.
    /// </summary>
    public static void SlotSymbols(ZSlotResult r, Rarity tier, bool jackpot, uint seed, Span<byte> s)
    {
        var h = seed * 2246822519u + 0x9E3779B9u;
        uint Next()
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return h;
        }
        var topGem = (byte)(SlotSymGem + (int)(jackpot ? Rarity.Wonder : Rarity.Legendary));
        byte Gem() => (byte)(SlotSymGem + Next() % (uint)(topGem - SlotSymGem + 1));
        // A filler that is a skull or a gem different from both others (so it never makes a pair).
        byte Odd(byte a, byte b)
        {
            for (var tries = 0; tries < 6; tries++)
            {
                var g = Next() % 3 == 0 ? SlotSymSkull : Gem();
                if (g == SlotSymSkull || (g != a && g != b))
                    return g;
            }
            return SlotSymSkull;
        }
        var at = (int)(Next() % 3); // where the odd one out goes
        switch (r)
        {
            case ZSlotResult.Part:
            {
                var gem = (byte)(SlotSymGem + (int)tier);
                if (gem + 1 <= topGem && Next() % 2 == 0)
                {
                    // Two of the gem one tier up.
                    var up = (byte)(gem + 1);
                    s[0] = s[1] = s[2] = up;
                    s[at] = Odd(up, up);
                }
                else
                    s[0] = s[1] = s[2] = gem;
                return;
            }
            case ZSlotResult.Scrap when Next() % 3 == 0:
                s[0] = s[1] = s[2] = SlotSymGem; // two commons
                s[at] = Odd(SlotSymGem, SlotSymGem);
                return;
            default:
            {
                // One cog (scrap), one coin (points) or one skull (nothing), then two odd symbols that make no pair.
                var key = r switch { ZSlotResult.Scrap => SlotSymScrap, ZSlotResult.Points => SlotSymCoin, _ => SlotSymSkull };
                var a = Gem();
                var b = Odd(a, a);
                s[at] = key;
                s[(at + 1) % 3] = a;
                s[(at + 2) % 3] = b;
                return;
            }
        }
    }

    // ---- Self-test hooks --------------------------------------------------------------------------------

    /// <summary>Self-test: the slot machines' server tick alone (payouts and freeing machines).</summary>
    public void SelfTestTickSlots(double now)
    {
        if (ZMap is { } zm)
            TickSlots(zm, now);
    }
}
