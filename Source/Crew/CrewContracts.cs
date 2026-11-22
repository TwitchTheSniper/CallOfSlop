using System.Numerics;

namespace CallOfSlop;

public enum ContractKind : byte
{
    None,
    Escort, // keep near one of the power's crewed ships while it lives, for a while
    Deliver, // bring units of a material to the power's place
    Raid, // destroy or take a ship of a power it fights
}

public enum ContractState : byte
{
    None,
    Offered,
    Taken,
    Done,
    Failed,
}

/// <summary>One contract (a job a power offers to players, paid on the taker's tab with that power: faction-controllers.md phase 5).</summary>
public struct Contract
{
    public ContractKind Kind;
    public ContractState State;
    public byte Power, Target, Taker, Party, Material; // Target: a raid's victim; Taker: the person who took it (255: none); Party: their faction then
    public ushort Serial, Units, Body; // Body: the ship escorted (0 after a load until found by Uid)
    public uint Uid; // the escorted ship's captain's crew uid
    public int Pay, Progress, Need; // an escort's seconds near, and needed
    public Vector3 At; // a delivery's place; an escort's ship as last seen
    public long Until, StateAt; // ticks: the offer's or the job's end; when its state last changed
    public string TakerName; // a save finds the taker again by name
}

/// <summary>What contracts need of the game (Game/GameCrewContracts.cs; a tool's stand-in).</summary>
public interface IContractHost
{
    /// <summary>A crewed ship of <paramref name="power"/> out in the world to be escorted (a convoy's first): its body, its captain's uid, where.</summary>
    bool Escortee(byte power, out ushort body, out uint uid, out Vector3 at);

    /// <summary>Body <paramref name="body"/> is whole, crewed and where (false: lost).</summary>
    bool ShipAt(ushort body, out Vector3 at);

    /// <summary>The crewed body whose crew holds uid <paramref name="uid"/> (0: none).</summary>
    ushort BodyOfUid(uint uid);

    /// <summary>The taker, or someone of their faction, alive within <paramref name="range"/> of <paramref name="at"/>.</summary>
    bool PartyNear(byte taker, Vector3 at, float range);

    /// <summary>Units of <paramref name="material"/> in bodies the taker's side owns within <paramref name="range"/> of <paramref name="at"/>; with <paramref name="take"/>, that many taken out.</summary>
    int Cargo(byte taker, Vector3 at, float range, byte material, int units, bool take);

    /// <summary>Where a delivery for <paramref name="power"/> goes for <paramref name="taker"/> (its home place, else a place picked near them).</summary>
    bool DeliverPlace(byte power, byte taker, out Vector3 at);

    /// <summary>The power owes <paramref name="player"/> <paramref name="value"/> more on their tab with it (no credits: Game/GameTabs.cs).</summary>
    void Pay(byte player, byte power, int value);

    void Tell(byte player, string line);

    /// <summary>The person here by that name (255: none): a loaded contract's taker.</summary>
    byte PlayerNamed(string name);

    /// <summary>Someone who could take a contract from <paramref name="power"/> is here (its standing with them 0 or more, or a pact).</summary>
    bool AnyoneFor(byte power);

    /// <summary>
    /// Contracts from needs (Karl, 2026-11-22): a good one of <paramref name="power"/>'s stations in this world is short of (picked by
    /// <paramref name="rand"/>), how many units bring it to its want, and the pay (its price for a short good, with a premium). False: none.
    /// </summary>
    bool Shortage(byte power, uint rand, out byte material, out int units, out int station, out int pay)
    {
        (material, units, station, pay) = (0, 0, -1, 0);
        return false;
    }

    /// <summary>Where station <paramref name="station"/> (StationMarkets.All) is in this world; false: not here.</summary>
    bool StationAt(int station, out Vector3 at)
    {
        at = default;
        return false;
    }

    /// <summary>Units of <paramref name="material"/> in the taker's side's ships moored at station <paramref name="station"/>; with <paramref name="take"/>, that many taken out and into its stock.</summary>
    int StationCargo(byte taker, int station, byte material, int units, bool take) => 0;
}

/// <summary>
/// Contracts paid on a tab (docs/sandbox/design/faction-controllers.md phase 5): each power with a mind offers a few jobs in turn (escort one of its ships,
/// deliver a material to its place, raid a power it fights), seen by everyone in the Faction window; a person whose standing with the power is 0 or more
/// (or whose faction holds a pact with it) takes one, and it is tracked once a second until it is done (paid on the taker's tab with the power, the standing rises:
/// <c>contractDone</c>) or fails (out of time, the ship lost, given up: <c>contractFailed</c>). Creative pays as survival does (its tabs are unlimited
/// anyway). Fixed arrays, worked once a second; a notice allocates.
/// </summary>
public sealed class PowerContracts
{
    public const int Max = 24, OffersEach = 2, TakenEach = 2;
    public const int LingerSeconds = 60; // a done or failed contract stays shown this long

    public readonly Contract[] All = new Contract[Max];
    public int Version; // bumped on any change clients see
    public int Done, Failed, Offered; // counts (a tool's)
    private ushort _serial;
    private long _offerAt;
    private int _next;
    private uint _rng = 0x9E3779B9;
    private static int T => World.TickRate;

    public void Clear()
    {
        Array.Clear(All);
        Version++;
        _offerAt = 0;
    }

    private uint Rand()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng;
    }

    public static string KindWord(ContractKind k) => k switch { ContractKind.Escort => "escort", ContractKind.Deliver => "delivery", ContractKind.Raid => "raid", _ => "contract" };

    private static string Name(byte p) => CrewPowers.Of(p)?.Name ?? $"power {p}";

    /// <summary>A contract in words (the window's and a notice's; allocates).</summary>
    public static string Describe(in Contract c) => c.Kind switch
    {
        ContractKind.Escort => $"Escort {Name(c.Power)}'s ship: stay within range of it for {c.Need / 60f:0.#} min while it lives",
        ContractKind.Deliver when c.Target > 0 && c.Target <= StationMarkets.All.Length => $"Deliver {c.Units} {RecyclingTable.Current.NameOf(c.Material)} to {StationMarkets.All[c.Target - 1].Name}: dock there with it aboard",
        ContractKind.Deliver => $"Deliver {c.Units} {RecyclingTable.Current.NameOf(c.Material)} to {Name(c.Power)}'s place",
        ContractKind.Raid => $"Raid {Name(c.Target)}: destroy or take one of their ships",
        _ => "",
    };

    private int TakenBy(byte player)
    {
        var n = 0;
        foreach (ref readonly var c in All.AsSpan())
            n += c.State == ContractState.Taken && c.Taker == player ? 1 : 0;
        return n;
    }

    /// <summary>A server tick: once a second, every contract is worked, and in turn a power makes an offer.</summary>
    public void Step(PowerRelations rel, IContractHost host)
    {
        var tick = rel.Tick;
        if (tick % T != 3)
            return;
        var r = rel.Rules;
        Settle(rel, host);
        for (var i = 0; i < Max; i++)
        {
            ref var c = ref All[i];
            switch (c.State)
            {
                case ContractState.Offered:
                    if (tick >= c.Until || rel.Gone[c.Power] || c.Kind == ContractKind.Raid && rel.Gone[c.Target])
                        (c.State, Version) = (ContractState.None, Version + 1);
                    break;
                case ContractState.Taken:
                    Work(rel, host, i, ref c);
                    break;
                case ContractState.Done or ContractState.Failed:
                    if (tick - c.StateAt > LingerSeconds * T)
                        (c.State, Version) = (ContractState.None, Version + 1);
                    break;
            }
        }
        if (tick < _offerAt)
            return;
        _offerAt = tick + (long)r.ContractOfferSeconds * T;
        var n = CrewPowers.Count;
        for (var tries = 0; tries < n; tries++)
        {
            _next = _next % n + 1;
            if (MakeOffer(rel, host, (byte)_next))
                break;
        }
    }

    /// <summary>The taker is here as the one who took it (a player id is reused by the next joiner once they leave): else it is held for them by name, its clock
    /// stopped, until they come back. True: here.</summary>
    private bool Present(IContractHost host, ref Contract c)
    {
        if (c.Taker != 255 && host.PlayerNamed(c.TakerName) == c.Taker)
            return true;
        var back = host.PlayerNamed(c.TakerName);
        if (back != c.Taker)
        {
            if (c.Taker != 255)
                Console.WriteLine($"[crew] contract {c.Serial}: its taker {c.TakerName} is away: held for them");
            c.Taker = back; // (255: away; else they are back, perhaps under another id)
            Version++;
        }
        return c.Taker != 255;
    }

    private void Work(PowerRelations rel, IContractHost host, int i, ref Contract c)
    {
        var tick = rel.Tick;
        if (!Present(host, ref c))
        {
            c.Until += T; // (held for the taker while they are away: its clock stops)
            return;
        }
        if (rel.Gone[c.Power])
        {
            End(rel, host, ref c, false, $"{Name(c.Power)} is gone");
            return;
        }
        switch (c.Kind)
        {
            case ContractKind.Escort:
            {
                if (c.Body == 0 && c.Uid != 0)
                    c.Body = host.BodyOfUid(c.Uid);
                if (c.Body == 0 || !host.ShipAt(c.Body, out var at))
                {
                    End(rel, host, ref c, false, "the ship was lost");
                    return;
                }
                c.At = at;
                if (host.PartyNear(c.Taker, at, rel.Rules.EscortRange))
                {
                    c.Progress++;
                    Version++;
                    if (c.Progress >= c.Need)
                    {
                        End(rel, host, ref c, true, "");
                        return;
                    }
                }
                break;
            }
            case ContractKind.Deliver when c.Target > 0: // (a station's need: delivered by docking there with the goods aboard)
                if (host.StationCargo(c.Taker, c.Target - 1, c.Material, c.Units, false) >= c.Units)
                {
                    host.StationCargo(c.Taker, c.Target - 1, c.Material, c.Units, true);
                    End(rel, host, ref c, true, "");
                    return;
                }
                break;
            case ContractKind.Deliver:
                if (host.PartyNear(c.Taker, c.At, rel.Rules.DeliverRange * 2) && host.Cargo(c.Taker, c.At, rel.Rules.DeliverRange, c.Material, c.Units, false) >= c.Units)
                {
                    host.Cargo(c.Taker, c.At, rel.Rules.DeliverRange, c.Material, c.Units, true);
                    End(rel, host, ref c, true, "");
                    return;
                }
                break;
        }
        if (tick >= c.Until)
            End(rel, host, ref c, false, "out of time");
    }

    private void End(PowerRelations rel, IContractHost host, ref Contract c, bool done, string why)
    {
        c.State = done ? ContractState.Done : ContractState.Failed;
        c.StateAt = rel.Tick;
        Version++;
        var def = CrewPowers.Of(c.Power);
        if (done)
        {
            Done++;
            host.Pay(c.Taker, c.Power, c.Pay);
            if (def != null)
                rel.MovePlayer(c.Power, c.Taker, rel.Amount(RelEvent.ContractDone, def));
            host.Tell(c.Taker, $"Contract done: {Name(c.Power)} owes you {c.Pay} more on your tab and thinks better of you.");
        }
        else
        {
            Failed++;
            if (def != null && c.Taker != 255)
                rel.MovePlayer(c.Power, c.Taker, rel.Amount(RelEvent.ContractFailed, def));
            if (c.Taker != 255)
                host.Tell(c.Taker, $"Contract failed ({why}): {Name(c.Power)} thinks less of you.");
        }
        Console.WriteLine($"[crew] contract {c.Serial} ({KindWord(c.Kind)} for {Name(c.Power)}) {(done ? "done" : "failed")}{(why.Length > 0 ? $": {why}" : "")}");
    }

    /// <summary>Power <paramref name="a"/> offers one contract if it has room and anyone here could take it. True: offered.</summary>
    private bool MakeOffer(PowerRelations rel, IContractHost host, byte a)
    {
        if (CrewPowers.Of(a) is not { Controller: true } || rel.Gone[a] || !host.AnyoneFor(a))
            return false;
        var mine = 0;
        var free = -1;
        for (var i = 0; i < Max; i++)
        {
            if (All[i].State == ContractState.None)
                free = free < 0 ? i : free;
            else if (All[i].State == ContractState.Offered && All[i].Power == a)
                mine++;
        }
        if (mine >= OffersEach || free < 0)
            return false;
        var r = rel.Rules;
        var c = new Contract { Power = a, Taker = 255, State = ContractState.Offered, Serial = ++_serial == 0 ? ++_serial : _serial, TakerName = "", StateAt = rel.Tick, Until = rel.Tick + (long)r.ContractOfferMinutes * 60 * T };
        var start = (int)(Rand() % 3);
        for (var k = 0; k < 3 && c.Kind == ContractKind.None; k++)
            switch ((start + k) % 3)
            {
                case 0: // raid a power it fights
                {
                    var n = CrewPowers.Count;
                    var from = (int)(Rand() % (uint)n);
                    for (var j = 0; j < n; j++)
                    {
                        var b = (byte)((from + j) % n + 1);
                        if (b != a && !rel.Gone[b] && CrewPowers.Of(b) is { Controller: true } && rel.Get(a, b) < 0)
                        {
                            (c.Kind, c.Target, c.Pay) = (ContractKind.Raid, b, r.RaidPay);
                            break;
                        }
                    }
                    break;
                }
                case 1:
                    if (host.Escortee(a, out var body, out var uid, out var at))
                        (c.Kind, c.Body, c.Uid, c.At, c.Need, c.Pay) = (ContractKind.Escort, body, uid, at, r.EscortSeconds, r.EscortPay);
                    break;
                default:
                {
                    // A station's need first (Karl, 2026-11-22: contracts come from needs); a random material only where the power has no
                    // station here short of anything (a tool's host, or a power with no station).
                    if (host.Shortage(a, Rand(), out var sm, out var su, out var ss, out var sp) && ss >= 0 && ss < 255)
                    {
                        var dup = false;
                        foreach (ref readonly var o in All.AsSpan())
                            dup |= o.State is ContractState.Offered or ContractState.Taken && o.Kind == ContractKind.Deliver && o.Target == ss + 1 && o.Material == sm;
                        if (!dup)
                            (c.Kind, c.Material, c.Units, c.Pay, c.Target) = (ContractKind.Deliver, sm, (ushort)Math.Clamp(su, 1, ushort.MaxValue), sp, (byte)(ss + 1));
                        break;
                    }
                    var name = r.DeliverMaterials[Rand() % (uint)r.DeliverMaterials.Length];
                    var m = RecyclingTable.Current.IdOf(name);
                    if (m == 0)
                    {
                        Console.WriteLine($"[crew] contracts: no material \"{name}\" (Crew/Relations.json contracts.materials): no delivery offered");
                        break;
                    }
                    var steps = Math.Max(1, (r.DeliverMax - r.DeliverMin) / 10 + 1);
                    var units = Math.Min(r.DeliverMax, r.DeliverMin + (int)(Rand() % (uint)steps) * 10);
                    (c.Kind, c.Material, c.Units, c.Pay) = (ContractKind.Deliver, m, (ushort)units, r.DeliverBase + r.DeliverPerUnit * units);
                    break;
                }
            }
        if (c.Kind == ContractKind.None)
            return false;
        All[free] = c;
        Offered++;
        Version++;
        Console.WriteLine($"[crew] contract {c.Serial} offered: {Describe(c)} for {c.Pay} on a tab");
        return true;
    }

    /// <summary>A person takes contract <paramref name="slot"/> (its <paramref name="serial"/> as they saw it). Null: taken; else why not.</summary>
    public string? Take(PowerRelations rel, IContractHost host, byte player, int slot, ushort serial, string playerName)
    {
        if (slot < 0 || slot >= Max || All[slot].State != ContractState.Offered || All[slot].Serial != serial)
            return "that contract is gone";
        ref var c = ref All[slot];
        var party = rel.Table.Of[player];
        if (party != 0 && rel.PartyFeud[party * PowerRelations.N + c.Power - 1])
            return $"{Name(c.Power)} has sworn a blood feud against you: no work for you";
        if (rel.Peek(c.Power, player) < 0 && !rel.PartyHoldsFire(c.Power, player))
            return $"{Name(c.Power)} doesn't trust you with work: its standing with you must be 0 or more (or a pact)";
        if (TakenBy(player) >= TakenEach)
            return $"you hold {TakenEach} contracts already: finish or give one up first";
        if (c.Kind == ContractKind.Raid && rel.PartyPact(c.Target, party) is var pact and not PactKind.None)
            return $"your faction holds a {PowerRelations.PactName(pact)} with {Name(c.Target)}: break it first";
        if (c.Kind == ContractKind.Escort && c.Body == 0 && c.Uid != 0)
            c.Body = host.BodyOfUid(c.Uid); // (a loaded world's offer)
        if (c.Kind == ContractKind.Escort && !host.ShipAt(c.Body, out _))
        {
            c.State = ContractState.None;
            Version++;
            return "that ship is gone";
        }
        if (c.Kind == ContractKind.Deliver && c.Target > 0)
        {
            if (!host.StationAt(c.Target - 1, out var sat))
                return "that station isn't in this world";
            c.At = sat;
        }
        else if (c.Kind == ContractKind.Deliver)
        {
            if (!host.DeliverPlace(c.Power, player, out var at))
                return $"{Name(c.Power)} has nowhere to take a delivery here";
            c.At = at;
        }
        var r = rel.Rules;
        (c.State, c.Taker, c.Party, c.TakerName, c.StateAt, c.Progress) = (ContractState.Taken, player, party, playerName, rel.Tick, 0);
        c.Until = rel.Tick + (long)(c.Kind switch { ContractKind.Escort => r.EscortMinutes, ContractKind.Deliver => r.DeliverMinutes, _ => r.RaidMinutes }) * 60 * T;
        Version++;
        host.Tell(player, $"Contract taken: {Describe(c)}. Pay: {c.Pay} on your tab with them.");
        return null;
    }

    /// <summary>The taker gives contract <paramref name="slot"/> up: it fails.</summary>
    public string? Drop(PowerRelations rel, IContractHost host, byte player, int slot, ushort serial)
    {
        if (slot < 0 || slot >= Max || All[slot].State != ContractState.Taken || All[slot].Serial != serial || All[slot].Taker != player)
            return "you don't hold that contract";
        End(rel, host, ref All[slot], false, "given up");
        return null;
    }

    /// <summary>Server: <paramref name="player"/> (of faction <paramref name="party"/>) did <paramref name="e"/> to <paramref name="victim"/>: a raid contract on it
    /// held by them or their faction is done (paid when its next second's work runs: no host here).</summary>
    public void Deed(RelEvent e, byte victim, byte player, byte party)
    {
        if (e is not (RelEvent.DestroyShip or RelEvent.Capture or RelEvent.RaidConvoy))
            return;
        for (var i = 0; i < Max; i++)
        {
            ref var c = ref All[i];
            if (c.State == ContractState.Taken && c.Kind == ContractKind.Raid && c.Target == victim && (c.Taker == player || party != 0 && c.Party == party))
            {
                c.Progress = c.Need = 1;
                Version++;
            }
        }
    }

    /// <summary>A raid whose deed was done is paid (the deed came inside the relations' post, with no host): run from <see cref="Step"/>'s work.</summary>
    public void Settle(PowerRelations rel, IContractHost host)
    {
        for (var i = 0; i < Max; i++)
        {
            ref var c = ref All[i];
            if (c.State == ContractState.Taken && c.Kind == ContractKind.Raid && c.Need == 1 && c.Progress == 1 && Present(host, ref c))
                End(rel, host, ref c, true, ""); // (paid only to its taker, here: else when they come back)
        }
    }

    // ---- Saves (in the Powers section, version 3) -------------------------------------------------------------------------------------

    /// <summary>u8 n, each (u8 kind, u8 state, u8 power index, u8 target index (255 none), u8 party, text taker's name, u8 material, u16 units, u32 uid, i32 pay,
    /// i32 progress, i32 need, 3 x f32 place, i32 seconds left). Done and failed ones aren't kept.</summary>
    public void Write(SaveWriter w)
    {
        var n = 0;
        foreach (ref readonly var c in All.AsSpan())
            n += c.State is ContractState.Offered or ContractState.Taken ? 1 : 0;
        w.U8((byte)n);
        foreach (ref readonly var c in All.AsSpan())
            if (c.State is ContractState.Offered or ContractState.Taken)
                w.U8((byte)c.Kind).U8((byte)c.State).U8((byte)(c.Power - 1)).U8(c.Target == 0 ? (byte)255 : (byte)(c.Target - 1)).U8(c.Party).Str(c.TakerName ?? "")
                    .U8(c.Material).U16(c.Units).U32(c.Uid).I32(c.Pay).I32(c.Progress).I32(c.Need).F32(c.At.X).F32(c.At.Y).F32(c.At.Z)
                    .I32((int)Math.Max(0, (c.Until - _tickForSave) / T));
    }

    private long _tickForSave;

    /// <summary>The relations' tick, for the seconds left (set by the relations before writing or reading).</summary>
    public long TickNow { set => _tickForSave = value; }

    public void Read(SaveReader r, byte[] map)
    {
        Array.Clear(All);
        int n = r.U8();
        var k = 0;
        for (var j = 0; j < n; j++)
        {
            var kind = (ContractKind)r.U8();
            var state = (ContractState)r.U8();
            int a = r.U8(), b = r.U8();
            var party = r.U8();
            var name = r.Str();
            var mat = r.U8();
            var units = r.U16();
            var uid = r.U32();
            var pay = r.I32();
            var progress = r.I32();
            var need = r.I32();
            var at = new Vector3(r.F32(), r.F32(), r.F32());
            var left = r.I32();
            if (a >= map.Length || map[a] == 0 || kind is not (ContractKind.Escort or ContractKind.Deliver or ContractKind.Raid) || state is not (ContractState.Offered or ContractState.Taken)
                || kind == ContractKind.Raid && (b >= map.Length || map[b] == 0) || k >= Max)
            {
                Console.WriteLine($"[crew] a saved contract ({kind}) doesn't fit this build's powers: left out");
                continue;
            }
            All[k++] = new Contract
            {
                Kind = kind, State = state, Power = map[a], Target = kind == ContractKind.Raid ? map[b] : kind == ContractKind.Deliver && b < StationMarkets.All.Length ? (byte)(b + 1) : (byte)0, // (a delivery's station by index) Party = party, Taker = 255, TakerName = name, Material = mat,
                Units = units, Uid = uid, Pay = pay, Progress = progress, Need = need, At = at, Serial = ++_serial == 0 ? ++_serial : _serial,
                Until = _tickForSave + (long)left * T, StateAt = _tickForSave,
            };
        }
        Version++;
    }
}
