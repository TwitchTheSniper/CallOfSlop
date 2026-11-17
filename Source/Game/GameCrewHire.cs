using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a player's crew order (<see cref="CActKind.Crew"/>) does.</summary>
public enum CrewOrder : byte
{
    Release, // a prisoner goes free
    Drop, // a prisoner is put off (the ship landed or still)
    Hire, // a prisoner joins your crew, paid in credits (uid 0, body 0: the nearest friendly NPC crew member within reach)
    Dismiss, // one of your crew leaves
    HoldFire, // the whole crew of that body: hold fire
    FireAtWill, // ... fire at will
}

/// <summary>
/// Your own crew and credits (docs/sandbox/design/bot-brain.md section 9, phase 6; the user's answers 4 and 6 and the credits note), server
/// side with the client's roster and balance. Credits are a placeholder currency: each player's balance is saved with the world (the Crew
/// section); in Creative it is unlimited. A new player in Survival starts with <see cref="CreditsStart"/>; taking a ship pays a bounty.
/// Hired NPC crew are paid in credits: a prisoner on a ship you took (its faction's <c>hire.cost</c>), or a hand from a friendly crew
/// (their kind's standing with you 20 or more) within <see cref="HireReach"/> m, who joins the ship you are aboard or your nearest own ship.
/// A hired hand works under the Player Crew rules: they take the guns, the engine room and the magazine of your ship, never its helm, and
/// never stand in your way (you, your logic, remote and autopilot drive it as ever); they fight who hits the ship or whose faction is at
/// war with yours. Every <see cref="WageMinutes"/> minutes each hand is paid their kind's wage; unpaid, they leave.
/// </summary>
public sealed partial class Game
{
    public const long CreditsStart = 500;
    public const int CaptureBounty = 100, PrisonerBounty = 25;
    public const float HireReach = 15, OwnShipReach = 60;
    public const int WageMinutes = 10;

    private readonly bool[] _creditsDirty = new bool[256];
    private long _wageAt;

    /// <summary>Client: this player's credits as the host last sent them (<see cref="Msg.Credits"/>; -1: unlimited, Creative).</summary>
    public long CreditsHere;

    /// <summary>The HUD's credits line, made when the balance changes.</summary>
    public string CreditsLine { get; private set; } = "";

    /// <summary>Server: <paramref name="player"/> pays <paramref name="cost"/> credits. Null: paid; else why not.</summary>
    public string? CreditsPay(byte player, long cost)
    {
        if (SandboxInfiniteParts || cost <= 0)
            return null;
        if (_credits[player] < cost)
            return $"that costs {cost} credits: you have {_credits[player]}";
        _credits[player] -= cost;
        _creditsDirty[player] = true;
        return null;
    }

    public void CreditsGive(byte player, long amount)
    {
        if (amount == 0)
            return;
        _credits[player] = Math.Max(0, _credits[player] + amount);
        _creditsDirty[player] = true;
    }

    private readonly bool[] _creditsStarted = new bool[256], _creditsSeen = new bool[256];

    /// <summary>Server, a person's record came back: whether they had their start balance.</summary>
    private void CreditsFirst(Player p, WorldPlayer rec)
    {
        _creditsStarted[p.Id] = rec.CreditsStarted;
        _creditsSeen[p.Id] = true;
    }

    /// <summary>Server, each tick: balances to their owners when they changed; wages every <see cref="WageMinutes"/> minutes.</summary>
    private void CrewHireTick()
    {
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.CrewSlot >= 0)
                continue;
            if (!_creditsSeen[p.Id])
            {
                _creditsSeen[p.Id] = _creditsDirty[p.Id] = true; // (first seen this run: their balance to them)
                if (!_creditsStarted[p.Id] && SandboxSurvivalOption && !SandboxInfiniteParts)
                {
                    _creditsStarted[p.Id] = true;
                    _credits[p.Id] += CreditsStart; // (a new person in a Survival world: the start balance, once)
                }
            }
            if (_creditsDirty[p.Id])
            {
                _creditsDirty[p.Id] = false;
                CreditsSend(p);
            }
        }
        if (ServerTick < _wageAt)
            return;
        _wageAt = ServerTick + WageMinutes * 60L * World.TickRate;
        if (SandboxInfiniteParts)
            return;
        for (var i = 0; i < Crew.ShipHigh; i++)
        {
            if (Crew.Ships[i] is not { Own: true } s || Constructs.Get(s.Body) is not { } c || !Players.ContainsKey(c.Owner))
                continue;
            for (var k = s.MemberCount - 1; k >= 0; k--)
            {
                var m = s.Members[k];
                if (Crew.State[m] == CrewState.Dead || CrewFactions.Of(Crew.Fac[m]) is not { Wage: > 0 } def)
                    continue;
                if (CreditsPay(c.Owner, def.Wage) is { } why)
                {
                    SandboxHands.Notices.Add((c.Owner, $"{CrewNames.Of(Crew.Name[m])} left your crew unpaid ({why})"));
                    Console.WriteLine($"[crew] construct {c.Id}: hand {Crew.Uid[m]} left unpaid");
                    Crew.Release(m, this);
                }
            }
        }
    }

    private void CreditsSend(Player p)
    {
        var amount = SandboxInfiniteParts ? -1 : _credits[p.Id];
        if (p.Id == LocalId)
            CreditsSet(amount);
        if (!Net.IsOffline && p.Id != LocalId && GuidOf(p) is { } guid)
        {
            _w.Begin(Msg.Credits).U64((ulong)amount);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: Msg.Credits (i64 balance; -1: unlimited).</summary>
    private void ReadCredits(ref PacketReader r)
    {
        if (!r.Has(8))
        {
            Console.WriteLine("[crew] a credits message didn't read: dropped");
            return;
        }
        CreditsSet((long)r.U64());
    }

    private void CreditsSet(long amount)
    {
        if (amount == CreditsHere && CreditsLine.Length > 0)
            return;
        CreditsHere = amount;
        CreditsLine = amount < 0 ? "Credits: unlimited" : $"Credits: {amount}";
    }

    /// <summary>The bounty for a ship taken (GameCrewBoard.cs's capture).</summary>
    private void CrewBounty(byte by, int prisoners)
    {
        if (by == 255)
            return;
        var pay = CaptureBounty + PrisonerBounty * prisoners;
        CreditsGive(by, pay);
        SandboxHands.Notices.Add((by, $"Bounty: {pay} credits for the ship"));
    }

    // ---- Hiring --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server: <paramref name="me"/> hires prisoner <paramref name="uid"/> on <paramref name="body"/>, or (body 0) the nearest member of a friendly
    /// NPC crew within <see cref="HireReach"/>. Null: hired; else why not.
    /// </summary>
    public string? CrewHire(Player me, ushort body, uint uid)
    {
        int m;
        CrewShip? from;
        if (body != 0)
        {
            if (Constructs.Get(body) is not { } pc || Crew.ShipOf(body) is not { } ps)
                return "no crew aboard that";
            if (pc.Owner != me.Id && !Factions.Shares(pc.Owner, me.Id))
                return "not your ship";
            m = CrewByUid(ps, uid);
            if (m < 0 || Crew.State[m] != CrewState.Surrendered)
                return "no such prisoner";
            from = ps;
        }
        else
        {
            (m, from) = CrewHireable(me);
            if (m < 0 || from == null)
                return $"no friendly crew within {HireReach:0} m to hire from (their kind must think well of you: standing 20 or more)";
        }
        var def = CrewFactions.Of(Crew.Fac[m]);
        if (def is { CanHire: false } || from.Prize == false && def == null)
            return "they won't be hired";
        var ship = CrewOwnShip(me, from);
        if (ship == null)
            return $"be aboard your own ship, or within {OwnShipReach:0} m of one, to hire crew for it";
        // Every refusal before anything changes: a refused hire leaves no empty roster (and no ShipAi driver slot) behind.
        var cost = def?.HireCost ?? 0;
        var to = Crew.ShipOf(ship.Id);
        if (to != null && !to.Own && !to.Prize)
            return "that ship has a crew that isn't yours";
        if (to != null && to.MemberCount >= CrewWorld.MaxCrewPerBody)
            return "your ship's crew is full";
        if (!SandboxInfiniteParts && cost > 0 && _credits[me.Id] < cost)
            return $"that costs {cost} credits: you have {_credits[me.Id]}";
        to ??= Crew.Crew(ship, ShipAiMode.Strangers, faction: CrewFactions.Find("Player Crew"), empty: true);
        if (to is { Prize: true, Own: false })
        {
            // A prize's first hand: its roster becomes your crew (the rest stay prisoners aboard it).
            to.Prize = false;
            CrewWorld.SetFaction(ship, to, CrewFactions.Find("Player Crew"));
            to.Act = BoardAct.Hold;
            CrewTeamsFollow(to);
        }
        if (to == null || !to.Own)
            return "that ship can't take a crew";
        if (CreditsPay(me.Id, cost) is { } why)
            return why;
        var (uidKeep, name, skill, health, fac) = (Crew.Uid[m], Crew.Name[m], Crew.Skill[m], Crew.Health[m], Crew.Fac[m]);
        Crew.Release(m, this);
        var hand = Crew.Add(to, CrewJob.Spare, -1, uidKeep, health, skill, name);
        if (hand >= 0)
            Crew.Fac[hand] = fac; // (their wage and their kind's standing)
        if (def != null)
            ((ICrewHost)this).Deed(def.Id, me.Id, 2);
        CrewLog();
        Console.WriteLine($"[crew] player {me.Id} hired {CrewNames.Of(name)} ({def?.Name ?? "no kind"}) for {def?.HireCost ?? 0} credits onto construct {ship.Id}");
        SandboxHands.Notices.Add((me.Id, $"{CrewNames.Of(name)} joined your crew ({def?.HireCost ?? 0} credits; wage {def?.Wage ?? 0} every {WageMinutes} minutes)"));
        to.SeatsDirty = true;
        return null;
    }

    /// <summary>The nearest member of a friendly NPC crew within reach, not a pilot at work if another will do.</summary>
    private (int, CrewShip?) CrewHireable(Player me)
    {
        var best = -1;
        CrewShip? bestShip = null;
        var bestD = HireReach;
        for (var i = 0; i < Crew.ShipHigh; i++)
        {
            if (Crew.Ships[i] is not { Faction: not 0, Own: false, Prize: false, Tier: CrewTier.Bodied } s || CrewFactions.Of(s.Faction) is not { CanHire: true } def
                || CrewStanding(s.Faction, me.Id) < 20 || s.Foe(me.Id) || Constructs.Get(s.Body) is not { } c)
                continue;
            var d = Vector3.Distance(c.WorldCom, me.Position) - c.Radius;
            if (d > bestD)
                continue;
            for (var k = 0; k < s.MemberCount; k++)
            {
                var m = s.Members[k];
                if (Crew.State[m] is CrewState.Dead or CrewState.Scuttling || Crew.Job[m] == CrewJob.Pilot && s.MemberCount > 1)
                    continue;
                (best, bestShip, bestD) = (m, s, d);
                break;
            }
        }
        return (best, bestShip);
    }

    /// <summary>The own ship a hand joins: the prize they are on, the body you are aboard, else your nearest within <see cref="OwnShipReach"/>.</summary>
    private Construct? CrewOwnShip(Player me, CrewShip from)
    {
        if (from.Prize && Constructs.Get(from.Body) is { } prize && prize.Owner == me.Id)
            return prize;
        ref var h = ref SandboxHands.Hand(me.Id);
        var aboard = h.SeatBody != 0 ? h.SeatBody : me.Sim.RideBody;
        if (aboard != 0 && Constructs.Get(aboard) is { } a && a.Owner == me.Id && !a.Wreck)
            return a;
        Construct? best = null;
        var bestD = OwnShipReach;
        foreach (var c in Constructs.Bodies)
        {
            if (c.Owner != me.Id || c.Wreck || c.Loose || c.PartCount < 4)
                continue;
            var d = Vector3.Distance(c.WorldCom, me.Position) - c.Radius;
            if (d < bestD)
                (best, bestD) = (c, d);
        }
        return best;
    }

    // ---- Orders --------------------------------------------------------------------------------------------------------------------------

    /// <summary>Client (or host): a crew order (u16 body, u32 uid, u8 CrewOrder).</summary>
    public void SandboxSendCrewOrder(ushort body, uint uid, CrewOrder op)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Crew).U16(body).U32(uid).U8((byte)op);
        SandboxSendBuilt(w);
    }

    /// <summary>Server: a <see cref="CActKind.Crew"/>. Null: done; else why not.</summary>
    private string? CrewOrderAct(Player p, ushort body, uint uid, CrewOrder op)
    {
        switch (op)
        {
            case CrewOrder.Release:
                return CrewPrisoner(p, body, uid, "release");
            case CrewOrder.Drop:
                return CrewPrisoner(p, body, uid, "drop");
            case CrewOrder.Hire:
                return CrewHire(p, body, uid);
        }
        if (Constructs.Get(body) is not { } c || Crew.ShipOf(body) is not { Own: true } s)
            return "no crew of yours aboard that";
        if (c.Owner != p.Id && !Factions.Shares(c.Owner, p.Id))
            return "not your ship";
        switch (op)
        {
            case CrewOrder.Dismiss:
                var m = CrewByUid(s, uid);
                if (m < 0)
                    return "no such hand";
                Crew.Release(m, this);
                s.SeatsDirty = true;
                return null;
            case CrewOrder.HoldFire:
            case CrewOrder.FireAtWill:
                s.HoldFire = op == CrewOrder.HoldFire;
                s.SeatsDirty = true;
                return null;
        }
        return "no such crew order";
    }

    // ---- The roster to clients (the Crew window) ------------------------------------------------------------------------------------------

    /// <summary>One crewed body as a client knows it (an own crew, or prisoners on a prize).</summary>
    public sealed class CrewRosterView
    {
        public ushort Body;
        public byte Owner;
        public bool Own, Prize, HoldFire;
        public readonly List<(uint Uid, string Name, CrewJob Job, CrewState State, byte Hp, byte Fac)> Members = new(CrewWorld.MaxCrewPerBody);
    }

    /// <summary>Client (and the host): the rosters of own crews and prizes (<see cref="Msg.CrewRoster"/>).</summary>
    public readonly Dictionary<ushort, CrewRosterView> CrewRosters = new();

    private int _rosterAt;

    /// <summary>Host: own crews' and prizes' rosters when they change (a few a tick; rare).</summary>
    private void CrewRosterSend()
    {
        var sent = 0;
        for (var n = 0; n < Crew.ShipHigh && sent < 4; n++)
        {
            if (_rosterAt >= Crew.ShipHigh)
                _rosterAt = 0;
            var i = _rosterAt++;
            if (Crew.Ships[i] is not { } s || !(s.Own || s.Prize) || !s.RosterDirty || Constructs.Get(s.Body) is not { } c)
                continue;
            s.RosterDirty = false;
            WriteRoster(s, c);
            if (!Net.IsOffline)
                Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
            var r = new PacketReader(_w.Span[1..]);
            ReadCrewRoster(ref r); // (the host's own window)
            sent++;
        }
    }

    private void WriteRoster(CrewShip s, Construct c)
    {
        var n = 0;
        for (var k = 0; k < s.MemberCount; k++)
            if (Crew.State[s.Members[k]] != CrewState.Dead)
                n++;
        _w.Begin(Msg.CrewRoster).U16(s.Body).U8(c.Owner).U8((byte)((s.Own ? 1 : 0) | (s.Prize ? 2 : 0) | (s.HoldFire ? 4 : 0))).U8((byte)n);
        for (var k = 0; k < s.MemberCount; k++)
        {
            var m = s.Members[k];
            if (Crew.State[m] == CrewState.Dead)
                continue;
            _w.U32(Crew.Uid[m]).U16(Crew.Name[m]).U8((byte)Crew.Job[m]).U8((byte)Crew.State[m]).U8((byte)Math.Clamp(Crew.Health[m] / CrewWorld.MemberHealth * 255, 0, 255)).U8(Crew.Fac[m]);
        }
    }

    /// <summary>A joiner hears every own crew's and prize's roster.</summary>
    private void SendCrewRosters(RakNetGUID to)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { } s && (s.Own || s.Prize) && Constructs.Get(s.Body) is { } c)
            {
                WriteRoster(s, c);
                Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
            }
    }

    /// <summary>Client: Msg.CrewRoster (u16 body, u8 owner, u8 flags: 1 own, 2 prize, 4 hold fire; u8 n, n x (u32 uid, u16 name, u8 job, u8 state, u8 health, u8 kind); n 0 and flags 0: gone).</summary>
    private void ReadCrewRoster(ref PacketReader r)
    {
        var body = r.U16();
        var owner = r.U8();
        var flags = r.U8();
        int n = r.U8();
        if (n > CrewWorld.MaxCrewPerBody || !r.Has(n * 10))
        {
            Console.WriteLine($"[crew] a roster of {n} for construct {body} didn't read: dropped");
            return;
        }
        if (n == 0 && flags == 0)
        {
            CrewRosters.Remove(body);
            return;
        }
        if (!CrewRosters.TryGetValue(body, out var v))
            CrewRosters[body] = v = new CrewRosterView { Body = body };
        (v.Owner, v.Own, v.Prize, v.HoldFire) = (owner, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
        v.Members.Clear();
        for (var k = 0; k < n; k++)
            v.Members.Add((r.U32(), CrewNames.Of(r.U16()), (CrewJob)Math.Min((int)r.U8(), (int)CrewJob.Builder), (CrewState)Math.Min((int)r.U8(), (int)CrewState.Scuttling), r.U8(), r.U8()));
    }

    /// <summary>Host: a roster that went (the crew disbanded): clients drop it.</summary>
    private void CrewRosterGone(ushort body)
    {
        if (!CrewRosters.Remove(body))
            return; // (never sent: not an own crew or a prize)
        _w.Begin(Msg.CrewRoster).U16(body).U8(255).U8(0).U8(0);
        if (!Net.IsOffline)
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }
}
