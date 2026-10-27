using System.Numerics;

namespace CallOfSlop;

/// <summary>A slot machine's state (server-owned, mirrored in <see cref="Msg.ZWorld"/>).</summary>
public enum ZSlotState : byte
{
    Idle,

    /// <summary>The reels spin (the result is already rolled: clients stop the reels on it one by one).</summary>
    Spinning,

    /// <summary>A win shows for a moment (paid at the start of it); then the machine takes another pull. A pull that wins
    /// nothing skips this: the machine is free the moment its reels stop (developer request, 2026-10-27).</summary>
    Showing,
}

/// <summary>What a pull paid.</summary>
public enum ZSlotResult : byte
{
    Nothing,

    // (1 was scrap until protocol 56: Zombies has no scrap since 2026-10-27; the wire values after it moved down.)
    Points,
    Part,

    /// <summary>A complete melee build into the melee slot (developer answer, 2026-10-26: melee comes from the slots, not the box).</summary>
    Melee,
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

    /// <summary>The bet level (an index into <see cref="ZombiesRules.SlotBets"/>): set at the machine with the second key, kept until changed (shared, like the heat).</summary>
    public byte Bet;

    /// <summary>Counts pulls (wrapping), so a client sees a new spin even when the state byte didn't change between two messages.</summary>
    public byte SpinId;

    /// <summary>The three reel symbols of the last pull (<see cref="Game.SlotSymSkull"/> ...).</summary>
    public readonly byte[] Symbols = new byte[3];

    public ZSlotResult Result;
    public Rarity Tier;

    /// <summary>The part paid (an index into <see cref="Parts.All"/>; a melee build: into <see cref="StandardBuilds.All"/>), or -1.</summary>
    public short Part = -1;

    /// <summary>The price paid for this pull (points back pay a share of it).</summary>
    public int Paid;

    /// <summary>The paid part came back as points instead (you already held <see cref="ZombiesRules.SlotDupePointsAt"/> of it loose).</summary>
    public bool Refunded;

    // The last win, as everyone saw it (Msg.ZSlotWin, protocol 55): who, what (a part or a melee build: an index as in
    // Part), its rarity, whether it was refunded as points, and when it came in (this PC's clock). WinSeq counts wins (0: none yet).
    public byte WinSeq, WinPlayer = byte.MaxValue;
    public ZSlotResult WinResult;
    public short WinIndex = -1;
    public Rarity WinTier;
    public bool WinRefunded;
    public double WinAt = -1e9;

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
        Bet = 0;
        SpinId = 0;
        Symbols[0] = Symbols[1] = Symbols[2] = 0;
        Result = ZSlotResult.Nothing;
        Tier = Rarity.Common;
        Part = -1;
        Paid = 0;
        Refunded = false;
        WinSeq = 0;
        WinPlayer = byte.MaxValue;
        WinResult = ZSlotResult.Nothing;
        WinIndex = -1;
        WinTier = Rarity.Common;
        WinRefunded = false;
        WinAt = -1e9;
        SeenSpin = 0;
        SpinStart = -1e9;
        ReelsStopped = 3;
    }
}

/// <summary>
/// Zombies slot machines (board item "Zombies slots", 2026-10-26; docs/zombies/ZOMBIES-MODE.md, "Slot machines"): gamble
/// points for loose weapon-builder parts. Walk up, hold the interact key, pay; three reels spin and pay a part (into your
/// loose parts, for the workbench), points back or nothing (a nothing frees the machine at once: pull again).
/// Zombies has no scrap since 2026-10-27 (developer: "the gambling machines replace the need"). Three levers raise the rarity, all data: the round
/// (DropTables.json "slotTiers"), the machine's heat (every pull without a rare-or-better part adds one; every
/// <see cref="ZombiesRules.SlotHeatStep"/> heat moves the part odds one tier up) and jackpot rounds (every
/// <see cref="ZombiesRules.SlotJackpotEvery"/>th: the wonder symbol is on the reels and legendary / wonder get a bonus).
/// At most <see cref="ZombiesRules.SlotMaxNothing"/> nothing-results in a row per player per machine. The server rolls,
/// clients animate the reels (they get the symbols in <see cref="Msg.ZWorld"/>, protocol 51). Nothing here allocates per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Reel symbols: a skull (nothing), a coin (points back), then a gem per rarity (<see cref="SlotSymGem"/> + the tier),
    /// then a blade (<see cref="SlotSymBlade"/>: two blades and a gem pay a melee build of the gem's rarity). The scrap cog
    /// went with scrap (protocol 56): the symbols after it moved down one.</summary>
    public const byte SlotSymSkull = 0, SlotSymCoin = 1, SlotSymGem = 2, SlotSymBlade = SlotSymGem + (int)Rarity.Wonder + 1;

    /// <summary>Is <paramref name="s"/> a gem (not a skull, coin or blade)?</summary>
    public static bool IsSlotGem(byte s) => s >= SlotSymGem && s < SlotSymBlade;

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

    /// <summary>The 1x price of a pull on machine <paramref name="i"/> this round (the map's own price, or the rules' rising one).</summary>
    public int SlotBaseCost(ZombiesMap zm, int i) => i >= 0 && i < zm.SlotMachines.Count
        ? zm.SlotMachines[i].Cost > 0 ? zm.SlotMachines[i].Cost : ZRules.SlotCost(ZRound, zm.SlotMachines[i].SlotCategory)
        : ZRules.SlotCost(ZRound);

    private static readonly string[] SlotNames = Enum.GetValues<PartCategory>().Select(c => $"{PartDef.BuiltInCategoryName(c)} slot machine").ToArray();
    private static readonly string[] SlotLabels = Enum.GetValues<PartCategory>().Select(c => $"{PartDef.BuiltInCategoryName(c).ToUpperInvariant()} SLOTS").ToArray();

    /// <summary>"Barrel slot machine" (a per-category machine) or "slot machine" (the classic one, any part).</summary>
    public static string SlotMachineName(int category) => category >= 0 && category < SlotNames.Length ? SlotNames[category] : "slot machine";

    /// <summary>The label over a machine ("BARREL SLOTS"; "SLOTS: ANY PART" for the classic one).</summary>
    public static string SlotMachineLabel(int category) => category >= 0 && category < SlotLabels.Length ? SlotLabels[category] : "SLOTS: ANY PART";

    /// <summary>A machine's category colour (its label and the plate on its topper; the props use the same colours).</summary>
    public static Vector3 SlotCategoryColor(int category) => category switch
    {
        (int)PartCategory.Receiver => new(0.85f, 0.30f, 0.25f),
        (int)PartCategory.Barrel => new(0.55f, 0.60f, 0.68f),
        (int)PartCategory.Splitter => new(0.30f, 0.80f, 0.75f),
        (int)PartCategory.Feed => new(0.85f, 0.65f, 0.20f),
        (int)PartCategory.Ammo => new(0.95f, 0.75f, 0.30f),
        (int)PartCategory.Stock => new(0.60f, 0.42f, 0.25f),
        (int)PartCategory.Grip => new(0.45f, 0.75f, 0.35f),
        (int)PartCategory.Optic => new(0.35f, 0.65f, 1.00f),
        (int)PartCategory.Muzzle => new(1.00f, 0.50f, 0.20f),
        (int)PartCategory.Utility => new(1.00f, 0.25f, 0.25f),
        (int)PartCategory.Upgrade => new(0.75f, 0.50f, 1.00f),
        (int)PartCategory.Multiplier => new(1.00f, 0.85f, 0.25f),
        (int)PartCategory.Hilt => new(0.70f, 0.50f, 0.30f),
        (int)PartCategory.Head => new(0.80f, 0.85f, 0.90f),
        (int)PartCategory.MeleeMod => new(0.60f, 0.90f, 0.40f),
        (int)PartCategory.Action => new(0.95f, 0.45f, 0.60f),
        _ => new(1f, 0.78f, 0.2f),
    };

    /// <summary>The price of a pull on machine <paramref name="i"/> at its bet.</summary>
    public int SlotCost(ZombiesMap zm, int i) => ZRules.SlotBetCost(SlotBaseCost(zm, i), i >= 0 && i < ZSlots.Length ? ZSlots[i].Bet : 0);

    /// <summary>"2x" for bet level <paramref name="bet"/>.</summary>
    public string SlotBetName(int bet) => ZRules.SlotBets.Length == 0 ? "1x" : $"{ZRules.SlotBets[Math.Clamp(bet, 0, ZRules.SlotBets.Length - 1)]:0.#}x";

    /// <summary>The workbench message kind (<see cref="Msg.ZBench"/>) that moves a slot machine's bet up a level (wrapping round); its slot byte is the machine.</summary>
    public const int BenchSlotBet = 4;

    /// <summary>
    /// Server: <paramref name="p"/> changes machine <paramref name="i"/>'s bet (the next level, wrapping round to 1x).
    /// Only standing at an idle machine. Returns why not, or null.
    /// </summary>
    public string? ServerSlotBet(Player p, int i)
    {
        if (ZMap is not { } zm || i < 0 || i >= zm.SlotMachines.Count || i >= ZSlots.Length)
            return "That's not a slot machine.";
        if (Flat(zm.SlotMachines[i].At - p.Position) > UseRange + 0.3f)
            return "You're not at the slot machine.";
        var m = ZSlots[i];
        if (m.State != ZSlotState.Idle)
            return "Wait for the reels to stop.";
        var levels = Math.Max(1, Math.Min(ZRules.SlotBets.Length, 255));
        m.Bet = (byte)((m.Bet + 1) % levels);
        ZFeedTo(p, $"Slot machine bet: {SlotBetName(m.Bet)} ({SlotCost(zm, i)} a pull; rarer parts, no sure win)", Sfx.HitTick);
        return null;
    }

    /// <summary>Local: the second key at a slot machine changes its bet (the host does it, a client asks).</summary>
    public bool SlotBetKey()
    {
        if (Local is not { } me || FindZUse(me) is not { Kind: ZUseKind.SlotMachine } u)
            return false;
        BenchRequest(BenchSlotBet, u.Index, "");
        return true;
    }

    /// <summary>The interact prompt at machine <paramref name="i"/> (FindZUse).</summary>
    private ZUse SlotUse(ZombiesMap zm, int i)
    {
        var m = ZSlots[i];
        var cost = SlotCost(zm, i);
        if (zm.SlotMachines[i].NeedsPower && !ZPower)
            return new ZUse(ZUseKind.SlotMachine, i, cost, $"{SlotMachineName(zm.SlotMachines[i].SlotCategory)}: needs the power on", Blocked: true);
        if (m.State != ZSlotState.Idle)
            return new ZUse(ZUseKind.SlotMachine, i, cost, m.State == ZSlotState.Spinning ? "The reels are spinning..." : SlotResultText(m), Blocked: true);
        var step = Math.Max(1, ZRules.SlotHeatStep);
        var steps = Math.Min(ZRules.SlotHeatMaxSteps, m.Heat / step);
        var heat = steps >= ZRules.SlotHeatMaxSteps ? "heat MAX" : $"heat {m.Heat % step}/{step}{(steps > 0 ? $" (+{steps})" : "")}";
        var bet = ZRules.SlotBets.Length > 1 ? $" - bet {SlotBetName(m.Bet)} ([G] to change)" : "";
        return new ZUse(ZUseKind.SlotMachine, i, cost, $"Hold [E] to pull the {SlotMachineName(zm.SlotMachines[i].SlotCategory)} [{cost}]{bet} - {heat}{(ZRules.SlotJackpot(ZRound) ? " - JACKPOT ROUND" : "")}");
    }

    /// <summary>What the last pull of <paramref name="m"/> paid, for the prompt and the feed.</summary>
    public string SlotResultText(ZSlotMachine m) => m.Result switch
    {
        ZSlotResult.Part when m.Part >= 0 && m.Part < Parts.All.Length => m.Refunded
            ? $"{PartDef.RarityName(m.Tier)}: {Parts.All[m.Part].Name} (you have plenty: +{SlotPointsBack(m.Paid)} points instead)"
            : $"{PartDef.RarityName(m.Tier)}: {Parts.All[m.Part].Name}!",
        ZSlotResult.Melee when m.Part >= 0 && m.Part < StandardBuilds.All.Length => $"{PartDef.RarityName(m.Tier)} melee: the {StandardBuilds.All[m.Part].Name}!",
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
        m.Refunded = false;
        var jackpot = ZRules.SlotJackpot(ZRound);
        var category = zm.SlotMachines[i].SlotCategory;
        var (result, tier) = RollSlotOutcome(ZRules, RunTables.Current, ZRound, m.Heat, ZNext(), ZRules.SlotBetStep(m.Bet));
        if (result == ZSlotResult.Nothing && ZRules.SlotMaxNothing > 0 && zp.SlotNothing[i] >= ZRules.SlotMaxNothing)
            result = ZSlotResult.Points; // bad-luck protection: never more than maxNothing blanks in a row (coins: points back)
        if (result == ZSlotResult.Melee && !ZRules.SlotPaysMelee(category))
            result = ZSlotResult.Part; // no blade on a gun category's reels: a part of the same tier (the same odds of a win)
        m.Part = -1;
        if (result == ZSlotResult.Part)
        {
            m.Part = PickSlotPart(tier, ZNext(), category, jackpot);
            if (m.Part < 0)
                result = ZSlotResult.Points; // no part of that tier (a modded part set): points back instead
            else
                tier = Parts.All[m.Part].Rarity; // (a category without that tier pays its nearest: the reels show what's paid)
        }
        else if (result == ZSlotResult.Melee)
        {
            m.Part = PickSlotMelee(tier, jackpot, zp.Slots[4]?.Name, ZNext());
            if (m.Part < 0)
                result = ZSlotResult.Points; // no "slots" melee builds (a modded set): points back instead
            else
                tier = StandardBuilds.All[m.Part].Tier; // (the nearest tier when that one had none: the reels show what's paid)
        }
        m.Result = result;
        m.Tier = tier;
        SlotSymbols(result, tier, jackpot, ZNext(), m.Symbols);
        Audio.Effect3D(Sfx.StreakCall, zm.SlotMachines[i].At + new Vector3(0, 1.2f, 0), 0.6f);
    }

    /// <summary>
    /// A part of category <paramref name="category"/> (-1: any) for a slot payout: of <paramref name="tier"/>, else the
    /// nearest tier that category has (lower first; wonder only in a jackpot round), as an index into <see cref="Parts.All"/>;
    /// -1 when the category has no parts. No allocation.
    /// </summary>
    public static short PickSlotPart(Rarity tier, uint seed, int category, bool jackpot)
    {
        if (category < 0)
            return PickSlotPart(tier, seed);
        var all = Parts.All;
        var top = (int)(jackpot ? Rarity.Wonder : Rarity.Legendary);
        for (var step = 0; step <= (int)Rarity.Wonder; step++)
            for (var side = 0; side < (step == 0 ? 1 : 2); side++)
            {
                var t = (int)tier + (side == 0 ? -step : step);
                if (t < 0 || t > Math.Max(top, (int)tier))
                    continue;
                var n = 0;
                foreach (var part in all)
                    if ((int)part.Category == category && (int)part.Rarity == t)
                        n++;
                if (n == 0)
                    continue;
                var k = (int)((seed * 2654435761u) % (uint)n);
                for (var i = 0; i < all.Length; i++)
                    if ((int)all[i].Category == category && (int)all[i].Rarity == t && k-- == 0)
                        return (short)i;
            }
        return -1;
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

    private static StandardBuild[]? _slotMeleeFor;
    private static int[] _slotMelee = [];

    /// <summary>The standard builds a slot machine can pay (tagged "slots": melee builds), as indices into <see cref="StandardBuilds.All"/>.</summary>
    public static int[] SlotMeleeBuilds()
    {
        var all = StandardBuilds.All;
        if (!ReferenceEquals(_slotMeleeFor, all))
        {
            _slotMelee = Enumerable.Range(0, all.Length).Where(i => all[i].Sources.Contains("slots") && all[i].IsMelee).ToArray();
            _slotMeleeFor = all;
        }
        return _slotMelee;
    }

    /// <summary>
    /// A melee build of <paramref name="tier"/> for a slot payout (not the one called <paramref name="held"/>): that tier,
    /// else the nearest tier with one (never wonder outside a jackpot round), else -1. An index into <see cref="StandardBuilds.All"/>.
    /// </summary>
    public static short PickSlotMelee(Rarity tier, bool jackpot, string? held, uint seed)
    {
        var all = StandardBuilds.All;
        var pool = SlotMeleeBuilds();
        var top = (int)(jackpot ? Rarity.Wonder : Rarity.Legendary);
        for (var step = 0; step <= (int)Rarity.Wonder; step++)
            for (var side = 0; side < (step == 0 ? 1 : 2); side++)
            {
                var t = (int)tier + (side == 0 ? -step : step);
                if (t < 0 || t > top)
                    continue;
                var n = 0;
                foreach (var i in pool)
                    if ((int)all[i].Tier == t && !all[i].Name.Equals(held, StringComparison.OrdinalIgnoreCase))
                        n++;
                if (n == 0)
                    continue;
                var k = (int)((seed * 2654435761u >> 5) % (uint)n);
                foreach (var i in pool)
                    if ((int)all[i].Tier == t && !all[i].Name.Equals(held, StringComparison.OrdinalIgnoreCase) && k-- == 0)
                        return (short)i;
            }
        return -1;
    }

    /// <summary>
    /// Server, every tick: a spin that ends pays out; a win that has shown long enough frees the machine. A spin that wins
    /// nothing frees it the moment the reels stop (no show, no cooldown: pull again at once; developer request, 2026-10-27).
    /// </summary>
    private void TickSlots(ZombiesMap zm, double now)
    {
        for (var i = 0; i < zm.SlotMachines.Count && i < ZSlots.Length; i++)
        {
            var m = ZSlots[i];
            if (m.State == ZSlotState.Spinning && now >= m.Until)
            {
                PaySlot(zm, i, m, now);
                if (m.Result == ZSlotResult.Nothing)
                {
                    m.State = ZSlotState.Idle; // a loss: the machine is free at once (the reels stay on the skull until the next pull)
                    m.Owner = byte.MaxValue;
                }
                else
                {
                    m.State = ZSlotState.Showing;
                    m.Until = now + ZRules.SlotShowSeconds;
                }
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

    /// <summary>Bytes per machine at the end of <see cref="Msg.ZWorld"/> (12 since protocol 53: the bet).</summary>
    public const int ZSlotBytes = 12;

    /// <summary>Server: every machine (state, heat, owner, spin counter, the three symbols, result and tier, the part, tenths of a second left).</summary>
    private void WriteSlots(PacketWriter w)
    {
        var n = World.Map.Zombies is { } zm ? Math.Min(zm.SlotMachines.Count, ZSlots.Length) : 0;
        w.U8((byte)n);
        for (var i = 0; i < n; i++)
        {
            var m = ZSlots[i];
            w.U8((byte)m.State).U8(m.Heat).U8(m.Owner).U8(m.SpinId).U8(m.Symbols[0]).U8(m.Symbols[1]).U8(m.Symbols[2])
                .U8((byte)((byte)m.Result | ((byte)m.Tier << 3) | (m.Refunded ? 128 : 0))).U16((ushort)m.Part)
                .U8((byte)Math.Clamp((int)MathF.Ceiling(m.Left * 10), 0, 255)).U8(m.Bet);
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
            var bet = r.U8();
            if (m == null)
                continue;
            m.Bet = bet;
            m.State = state;
            m.Heat = heat;
            m.Owner = owner;
            m.SpinId = spin;
            m.Symbols[0] = s0;
            m.Symbols[1] = s1;
            m.Symbols[2] = s2;
            m.Result = (ZSlotResult)Math.Min(res & 7, (int)ZSlotResult.Melee);
            m.Tier = (Rarity)Math.Min((res >> 3) & 7, (int)Rarity.Wonder);
            m.Refunded = (res & 128) != 0;
            m.Part = part;
            m.Left = left;
        }
    }

    private void PaySlot(ZombiesMap zm, int i, ZSlotMachine m, double now)
    {
        // Heat: a rare-or-better part resets it, anything else warms the machine up.
        if (m.Result is ZSlotResult.Part or ZSlotResult.Melee && m.Tier >= ZRules.SlotPityTier)
            m.Heat = 0;
        else if (m.Heat < 255)
            m.Heat++;
        if (!Players.TryGetValue(m.Owner, out var p))
            return; // (the player left mid-spin)
        var zp = ZP(p);
        zp.SlotNothing[i] = m.Result == ZSlotResult.Nothing ? (byte)Math.Min(255, zp.SlotNothing[i] + 1) : (byte)0;
        switch (m.Result)
        {
            case ZSlotResult.Part when m.Part >= 0 && m.Part < Parts.All.Length:
            {
                var part = Parts.All[m.Part];
                if (ZRules.SlotDupePointsAt > 0 && zp.Parts.GetValueOrDefault(part.Name) >= ZRules.SlotDupePointsAt)
                {
                    m.Refunded = true; // you hold plenty of it loose: the coin payout instead ("dupePointsAt")
                    AddPoints(p, SlotPointsBack(m.Paid));
                }
                else
                    zp.Parts[part.Name] = zp.Parts.GetValueOrDefault(part.Name) + 1;
                SendZRun(p);
                ServerSlotWin(i, m, p); // everyone sees it: the part over the machine and a line in the feed (protocol 55)
                break;
            }
            case ZSlotResult.Melee when m.Part >= 0 && m.Part < StandardBuilds.All.Length:
            {
                // Into the melee slot; the melee build there goes on the floor a few steps in front of the machine (as a
                // swap would, so you can change your mind; far enough out that the machine's prompt stays the nearest),
                // unless it's the untouched start one (you get that back with the start gear anyway).
                var sb = StandardBuilds.All[m.Part];
                if (zp.Slots[4] is { } old && !IsStartBuild(old))
                {
                    var spot = zm.SlotMachines[i];
                    SpawnWeaponDrop(old, DropSpots(spot.At + World.FlatForward(spot.Yaw) * 3.2f, 1, p.Id)[0], now + ZRules.BuildDropSeconds, p, full: true, now);
                }
                GiveBuild(p, 4, sb.Build(), full: true);
                ServerSlotWin(i, m, p);
                break;
            }
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
    public static (ZSlotResult Result, Rarity Tier) RollSlotOutcome(ZombiesRules rules, RunTables tables, int round, int heat, uint seed, int betSteps = 0)
    {
        Span<float> w = stackalloc float[RunTables.SlotKeys.Length];
        SlotWeights(rules, tables, round, heat, w, betSteps);
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
            RunTables.SlotPoints => (ZSlotResult.Points, Rarity.Common),
            RunTables.SlotMelee => (ZSlotResult.Melee, MeleeTier(w, h)),
            _ => (ZSlotResult.Part, (Rarity)(k - RunTables.SlotPart)),
        };
    }

    /// <summary>A melee payout's rarity: rolled from the same weights as a part's (the round's row, heat and jackpot included).</summary>
    private static Rarity MeleeTier(ReadOnlySpan<float> w, uint h)
    {
        h = (h ^ 0x9E3779B9u) * 2246822519u;
        h ^= h >> 13;
        var total = 0f;
        for (var t = 0; t <= (int)Rarity.Wonder; t++)
            total += w[RunTables.SlotPart + t];
        if (total <= 0)
            return Rarity.Common;
        var pick = (h % 1000000u) / 1000000f * total;
        for (var t = 0; t <= (int)Rarity.Wonder; t++)
        {
            pick -= w[RunTables.SlotPart + t];
            if (pick < 0 && w[RunTables.SlotPart + t] > 0)
                return (Rarity)t;
        }
        for (var t = (int)Rarity.Wonder; t > 0; t--)
            if (w[RunTables.SlotPart + t] > 0)
                return (Rarity)t;
        return Rarity.Common;
    }

    /// <summary>The weights a pull rolls from (see <see cref="RollSlotOutcome"/>), into <paramref name="w"/> (<see cref="RunTables.SlotKeys"/> order).</summary>
    public static void SlotWeights(ZombiesRules rules, RunTables tables, int round, int heat, Span<float> w, int betSteps = 0)
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
        // The row's top tier (2026-10-27): the late tiers only from their rounds; what a jackpot adds above it pays the top.
        var rowTop = RunTables.SlotPart + (int)row.Top;
        if (rowTop < top)
        {
            for (var t = top; t > rowTop; t--)
            {
                w[rowTop] += w[t];
                w[t] = 0;
            }
            top = rowTop;
        }
        // Heat steps (at most heatMaxSteps) plus the bet's steps (2026-10-26): only the part tiers move, never the blanks.
        var steps = Math.Min(rules.SlotHeatMaxSteps, heat / Math.Max(1, rules.SlotHeatStep)) + Math.Max(0, betSteps);
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
    /// of its tier; two of a gem pay a part one tier lower (two commons: points back); otherwise a coin pays points back,
    /// and skulls and odd gems nothing.
    /// </summary>
    public static (ZSlotResult Result, Rarity Tier) ReadSlotSymbols(ReadOnlySpan<byte> s)
    {
        // Two blades (or three): a melee build, of the third symbol's gem (common without one).
        var blades = (s[0] == SlotSymBlade ? 1 : 0) + (s[1] == SlotSymBlade ? 1 : 0) + (s[2] == SlotSymBlade ? 1 : 0);
        if (blades >= 2)
        {
            for (var i = 0; i < 3; i++)
                if (IsSlotGem(s[i]))
                    return (ZSlotResult.Melee, (Rarity)(s[i] - SlotSymGem));
            return (ZSlotResult.Melee, Rarity.Common);
        }
        if (IsSlotGem(s[0]) && s[0] == s[1] && s[1] == s[2])
            return (ZSlotResult.Part, (Rarity)(s[0] - SlotSymGem));
        byte pair = 0;
        if (IsSlotGem(s[0]) && (s[0] == s[1] || s[0] == s[2]))
            pair = s[0];
        else if (IsSlotGem(s[1]) && s[1] == s[2])
            pair = s[1];
        if (pair != 0)
            return pair == SlotSymGem ? (ZSlotResult.Points, Rarity.Common) : (ZSlotResult.Part, (Rarity)(pair - SlotSymGem - 1));
        for (var i = 0; i < 3; i++)
            if (s[i] == SlotSymCoin)
                return (ZSlotResult.Points, Rarity.Common);
        return (ZSlotResult.Nothing, Rarity.Common);
    }

    /// <summary>
    /// Reels that read as <paramref name="r"/> / <paramref name="tier"/> (<see cref="ReadSlotSymbols"/>), varied by
    /// <paramref name="seed"/>: a part comes up as three of its gem or as two of the next gem up, a melee build as two
    /// blades and its gem; near misses (odd gems, now and then a lone blade) fill the rest. The wonder gem only shows in jackpot rounds. No allocation.
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
                var g = Next() % 3 == 0 ? (Next() % 4 == 0 && a != SlotSymBlade && b != SlotSymBlade ? SlotSymBlade : SlotSymSkull) : Gem();
                if (g is SlotSymSkull or SlotSymBlade || (g != a && g != b))
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
            case ZSlotResult.Melee:
                // Two blades and the build's gem (the gem anywhere on the line).
                s[0] = s[1] = s[2] = SlotSymBlade;
                s[at] = (byte)(SlotSymGem + (int)tier);
                return;
            case ZSlotResult.Points when Next() % 3 == 0:
                s[0] = s[1] = s[2] = SlotSymGem; // two commons
                s[at] = Odd(SlotSymGem, SlotSymGem);
                return;
            default:
            {
                // One coin (points) or one skull (nothing), then two odd symbols that make no pair.
                var key = r == ZSlotResult.Points ? SlotSymCoin : SlotSymSkull;
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
