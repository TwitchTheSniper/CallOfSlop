using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Players in the politics, the game's side (docs/sandbox/design/faction-controllers.md phase 5; the engine is Crew/CrewPartyPacts.cs and
/// Crew/CrewContracts.cs): the Faction window's acts (a leader's pacts, a member's call for help, anyone's contracts), the faction's notices, a tribute's
/// and a trade pact's credits, and the contracts sent to everyone (<see cref="Msg.Contracts"/>). Server only but the read and the view.
/// </summary>
public sealed partial class Game : IContractHost
{
    /// <summary>This peer's view of the contracts (the host's copied from its engine, a client's as sent). <see cref="Contract.Until"/> holds the seconds left here.</summary>
    public readonly Contract[] ContractsView = new Contract[PowerContracts.Max];
    public int ContractsCount, ContractsVersion;

    /// <summary>How near a person must be to a power's crewed ship for it to count as the foe they call help against (m).</summary>
    public const float CallFoeRange = 2000;

    private int _contractsSent = -1, _notesRead;
    private long _contractsSentAt;

    /// <summary>Server, each tick after the relations' step: contracts worked, notices out, pacts' credits, contracts to clients.</summary>
    private void PoliticsTick(PowerRelations rel)
    {
        rel.PartyBroke ??= PartyBrokeTell;
        rel.Contracts.Step(rel, this);
        if (rel.NoteTotal - _notesRead > rel.Notes.Length)
            _notesRead = rel.NoteTotal - rel.Notes.Length;
        for (; _notesRead < rel.NoteTotal; _notesRead++)
            if (rel.NoteAt(_notesRead) is { } n)
                FactionTellAll(n.Party, n.Text);
        if (ServerTick % World.TickRate == 11)
            PactCredits(rel, ServerTick % (60L * World.TickRate) == 11);
        var cs = rel.Contracts;
        if (cs.Version != _contractsSent && ServerTick - _contractsSentAt >= World.TickRate)
        {
            _contractsSent = cs.Version;
            _contractsSentAt = ServerTick;
            ContractsShow(rel);
            if (!Net.IsOffline)
            {
                WriteContracts(SbW);
                Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
            }
        }
    }

    /// <summary>A member of faction <paramref name="party"/> who is here to pay or be paid: the leader, else the first member here (255: none).</summary>
    private byte PartyPayer(byte party)
    {
        var t = FactionTable;
        if (t.Leader[party] < MaxPlayers && Players.ContainsKey(t.Leader[party]))
            return t.Leader[party];
        foreach (var p in Players.Values)
            if (t.Of[p.Id] == party && FactionPerson(p))
                return p.Id;
        return 255;
    }

    /// <summary>Once a second: tributes due are paid by the faction's leader (or a member here; nobody here: it waits); each minute (<paramref name="minute"/>), a trade pact pays them.</summary>
    private void PactCredits(PowerRelations rel, bool minute)
    {
        for (var i = 0; i < PowerRelations.MaxPartyPacts; i++)
        {
            var k = rel.PartyK[i];
            if (k is not (PactKind.Tribute or PactKind.Trade))
                continue;
            var party = rel.PartyOf[i];
            var payer = PartyPayer(party);
            if (payer == 255)
                continue;
            var power = rel.PartyPower[i];
            if (k == PactKind.Tribute && rel.PartyDue[i] != 0 && rel.Tick >= rel.PartyDue[i])
            {
                var cost = rel.Rules.TributeCredits;
                var why = TabOrGoods(payer, power, cost, GoodsShipOf(payer, cost), out var how); // (off the payer's tab with that power, else goods from a hold: GameTabs.cs)
                rel.TributePaid(i, why == null);
                if (why == null)
                {
                    FactionTell(payer, $"You paid {CrewPowers.Of(power)?.Name} tribute for your faction: {how}.");
                    if (PowersActive && power <= PowerControllers.N)
                        Controllers.Powers[power].Stock += cost; // (a tab's value and a power's units are taken one for one)
                }
                else
                    FactionTellAll(party, $"The tribute to {CrewPowers.Of(power)?.Name} went unpaid ({why}): it lapses.");
            }
            else if (k == PactKind.Trade && minute)
            {
                TabGive(payer, power, rel.Rules.TradeCredits); // (a trade pact's share goes on the payer's tab with that power)
                if (PowersActive && power <= PowerControllers.N)
                {
                    Controllers.Powers[power].Stock += rel.Rules.TradePerMinute;
                    Controllers.Powers[power].Earned += rel.Rules.TradePerMinute;
                }
            }
        }
    }

    /// <summary>A faction broke a pact: the player factions allied with it hear.</summary>
    private void PartyBrokeTell(byte party, byte power, PactKind kind)
    {
        var t = FactionTable;
        for (var f = (byte)1; f < FactionTable.Max; f++)
            if (f != party && t.Names[f].Length > 0 && t.Relation(party, f) == FactionTable.Relations.Allied)
                FactionTellAll(f, $"Your allies {t.Names[party]} broke their {PowerRelations.PactName(kind)} with {CrewPowers.Of(power)?.Name}. Word gets around.");
    }

    // ---- Acts from the Faction window ----------------------------------------------------------------------------------------------

    /// <summary>Server: a politics act (the op read). Null: done; else why not.</summary>
    private string? PoliticsAct(Player p, FactionOp op, ref PacketReader r)
    {
        var t = FactionTable;
        var mine = t.Of[p.Id];
        var rel = Relations;
        byte a = 0, b = 0;
        ushort serial = 0;
        if (op is FactionOp.Pact or FactionOp.PactAnswer)
            (a, b) = (r.U8(), r.U8());
        else if (op == FactionOp.PactBreak)
            a = r.U8();
        else if (op is FactionOp.ContractTake or FactionOp.ContractDrop)
            (a, serial) = (r.U8(), r.U16());
        if (!CrewStandingOn)
            return "the world's powers don't deal with anyone here";
        if (op is FactionOp.Pact or FactionOp.PactAnswer or FactionOp.PactBreak)
        {
            if (mine == 0)
                return "pacts are a faction's: found or join one first";
            if (t.Leader[mine] != p.Id)
                return "only your faction's leader can make or break pacts";
            if (CrewPowers.Of(a) is not { } def)
                return "that power is gone";
            switch (op)
            {
                case FactionOp.Pact:
                {
                    var kind = (PactKind)b;
                    var why = rel.Propose(mine, a, kind);
                    if (why == null && rel.PartyPact(a, mine) != kind)
                        FactionTellAll(mine, $"{p.Name} proposed a {PowerRelations.PactName(kind)} to {def.Name} for {t.Names[mine]}: they answer soon.");
                    return why;
                }
                case FactionOp.PactAnswer:
                    return rel.Answer(mine, a, b != 0);
                default:
                    return rel.Break(mine, a, p.Position);
            }
        }
        switch (op)
        {
            case FactionOp.CallAllies:
            {
                if (mine == 0)
                    return "you are in no faction: you have no allies to call";
                if (!p.Alive)
                    return "you can't call for help while dead";
                var cool = (long)rel.Rules.CallCooldownMinutes * 60 * World.TickRate;
                if (rel.CallAt[mine] != 0 && rel.Tick - rel.CallAt[mine] < cool)
                    return $"your faction called for help lately: again in {(cool - (rel.Tick - rel.CallAt[mine])) / World.TickRate + 1} s";
                if (!PowersActive)
                    return "no power sends help in this world (their minds run in survival worlds): allies' crews nearby still fight your foes";
                var enemy = CallFoe(p);
                rel.CallAt[mine] = rel.Tick;
                Controllers.CallForParty(mine, p.Id, enemy, p.Position);
                FactionTellAll(mine, enemy != 0 ? $"{p.Name} calls the faction's allies for help against {CrewPowers.Of(enemy)?.Name}."
                    : $"{p.Name} calls the faction's allies for help.");
                return null;
            }
            case FactionOp.ContractTake:
                return rel.Contracts.Take(rel, this, p.Id, a, serial, p.Name);
            case FactionOp.ContractDrop:
                return rel.Contracts.Drop(rel, this, p.Id, a, serial);
        }
        return "unknown faction action";
    }

    /// <summary>The power whose crew is fighting <paramref name="p"/> nearest them (0: none in reach).</summary>
    private byte CallFoe(Player p)
    {
        byte best = 0;
        var d2 = CallFoeRange * CallFoeRange;
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Faction: not 0, Own: false, Prize: false, MemberCount: > 0 } s && (s.Contact == p.Id || s.Foe(p.Id) || CrewStanding(s.Faction, p.Id) < 0)
                && Constructs.Get(s.Body) is { Wreck: false } c && Vector3.DistanceSquared(c.WorldCom, p.Position) is var d && d < d2)
                (best, d2) = (s.Faction, d);
        return best;
    }

    // ---- The contracts' host ----------------------------------------------------------------------------------------------------------

    bool IContractHost.Escortee(byte power, out ushort body, out uint uid, out Vector3 at)
    {
        (body, uid, at) = (0, 0, default);
        var convoy = false;
        for (var i = 0; i < Crew.ShipHigh; i++)
        {
            if (Crew.Ships[i] is not { Own: false, Prize: false, MemberCount: > 0 } s || s.Faction != power || Constructs.Get(s.Body) is not { Wreck: false } c)
                continue;
            var isConvoy = Crew.Fleet.Of(s.Group) is { Task: GroupTask.Convoy } g && g.Flagship == s.Body;
            if (body != 0 && (convoy || !isConvoy))
                continue;
            (body, uid, at, convoy) = (s.Body, s.CaptainUid != 0 ? s.CaptainUid : Crew.Uid[s.Members[0]], c.WorldCom, isConvoy);
        }
        return body != 0;
    }

    bool IContractHost.ShipAt(ushort body, out Vector3 at)
    {
        at = default;
        if (body == 0 || Constructs.Get(body) is not { Wreck: false } c || Crew.ShipOf(body) is not { MemberCount: > 0, Own: false, Prize: false })
            return false;
        at = c.WorldCom;
        return true;
    }

    ushort IContractHost.BodyOfUid(uint uid)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { MemberCount: > 0 } s)
            {
                if (s.CaptainUid == uid)
                    return s.Body;
                for (var k = 0; k < s.MemberCount; k++)
                    if (Crew.Uid[s.Members[k]] == uid)
                        return s.Body;
            }
        return 0;
    }

    private bool OnSide(byte taker, byte who)
    {
        if (who == taker)
            return true;
        var f = FactionTable.Of[taker];
        return f != 0 && FactionTable.Of[who] == f;
    }

    bool IContractHost.PartyNear(byte taker, Vector3 at, float range)
    {
        foreach (var p in Players.Values)
            if (p.Alive && p.CrewSlot < 0 && FactionPerson(p) && OnSide(taker, p.Id) && Vector3.DistanceSquared(p.Position, at) < range * range)
                return true;
        return false;
    }

    int IContractHost.Cargo(byte taker, Vector3 at, float range, byte material, int units, bool take)
    {
        var have = 0;
        foreach (var c in Constructs.Bodies)
        {
            if (c.Wreck || c.Owner == 255 || !OnSide(taker, c.Owner) && !Factions.Shares(c.Owner, taker) || Vector3.Distance(c.WorldCom, at) > range + c.Radius)
                continue;
            for (var k = 0; k < c.PartCount && have < units; k++)
            {
                if (Constructs.StoreOf(c, k) is not { Amount: > 0 } st || st.Material != material)
                    continue;
                if (take)
                    have += Constructs.Withdraw(c, k, units - have);
                else
                    have += Math.Min(st.Amount, units - have);
            }
            if (have >= units)
                break;
        }
        return have;
    }

    bool IContractHost.DeliverPlace(byte power, byte taker, out Vector3 at)
    {
        at = default;
        if (PowersActive && power <= PowerControllers.N && Controllers.Powers[power] is { Started: true, PlaceCount: > 0 } cp
            && string.Equals(cp.World, World.Map.Name, StringComparison.Ordinal))
            for (var k = 0; k < cp.PlaceCount; k++)
                if (!cp.Razed[k])
                {
                    at = cp.Place[k];
                    return true;
                }
        if (!Players.TryGetValue(taker, out var p))
            return false;
        // No place of its own here (creative, or a power with no plan): a meeting point 800 m off, on a bearing of the power's own.
        var yaw = power * 2.39996f;
        var want = p.Position + new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * 800;
        if (!((IPowerHost)this).Ground(want, out at))
            at = want;
        return true;
    }

    bool IContractHost.Shortage(byte power, uint rand, out byte material, out int units, out int station, out int pay)
    {
        (material, units, station, pay) = (0, 0, -1, 0);
        if (!IsServer)
            return false;
        var all = StationMarkets.All;
        var r = Relations.Rules;
        // (the short goods of its stations in this world, counted, then the rand-th of them: no list is made)
        var count = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            var pick = pass == 1 ? (int)(rand % (uint)count) : -1;
            var seen = 0;
            for (var d = 0; d < all.Length; d++)
            {
                if (CrewPowers.Find(all[d].Power) != power || _marketByDef.Length <= d || _marketByDef[d] is not { Body: not 0 } m)
                    continue;
                for (var k = 0; k < all[d].Goods.Length; k++)
                {
                    if (!m.Short(k))
                        continue;
                    if (pass == 0)
                    {
                        count++;
                        continue;
                    }
                    if (seen++ != pick)
                        continue;
                    var g = all[d].Goods[k];
                    units = Math.Clamp((g.Target - m.Stock[k] + 9) / 10 * 10, r.DeliverMin, r.DeliverMax * 4);
                    (material, station) = (g.Material, d);
                    pay = (int)Math.Ceiling(m.BuyValue(k, units) * r.DeliverPremium); // (what it would pay for the goods short, and a premium for the run)
                    return true;
                }
            }
            if (count == 0)
                return false;
        }
        return false;
    }

    bool IContractHost.StationAt(int station, out Vector3 at)
    {
        at = default;
        if (station < 0 || station >= _marketByDef.Length || _marketByDef[station] is not { Body: not 0 } m || Constructs.Get(m.Body) is not { Wreck: false } st)
            return false;
        at = st.WorldCom;
        return true;
    }

    int IContractHost.StationCargo(byte taker, int station, byte material, int units, bool take)
    {
        if (station < 0 || station >= _marketByDef.Length || _marketByDef[station] is not { Body: not 0 } m)
            return 0;
        var have = 0;
        foreach (var (shipId, _) in m.Moored)
        {
            if (Constructs.Get(shipId) is not { Wreck: false } c || c.Owner == 255 || !OnSide(taker, c.Owner) && !Factions.Shares(c.Owner, taker))
                continue;
            for (var k = 0; k < c.PartCount && have < units; k++)
            {
                if (Constructs.StoreOf(c, k) is not { Amount: > 0 } st || st.Material != material)
                    continue;
                have += take ? Constructs.Withdraw(c, k, units - have) : Math.Min(st.Amount, units - have);
            }
            if (have >= units)
                break;
        }
        if (take && have > 0)
        {
            var goods = StationMarkets.All[station].Goods;
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == material)
                {
                    m.Stock[k] = Math.Min(goods[k].Most, m.Stock[k] + have); // (the delivery goes into its stock)
                    StationsVersion++;
                    break;
                }
        }
        return have;
    }

    void IContractHost.Pay(byte player, byte power, int value) => TabGive(player, power, value); // (on the taker's tab with the power that offered it)

    void IContractHost.Tell(byte player, string line)
    {
        if (player < MaxPlayers && Players.ContainsKey(player))
            SandboxHands.Notices.Add((player, line));
    }

    byte IContractHost.PlayerNamed(string name)
    {
        if (name.Length == 0)
            return 255;
        foreach (var p in Players.Values)
            if (FactionPerson(p) && p.CrewSlot < 0 && p.Name.Equals(name, StringComparison.Ordinal))
                return p.Id;
        return 255;
    }

    bool IContractHost.AnyoneFor(byte power)
    {
        var rel = _rel;
        foreach (var p in Players.Values)
            if (FactionPerson(p) && p.CrewSlot < 0 && (rel.Peek(power, p.Id) >= 0 || rel.PartyHoldsFire(power, p.Id)))
                return true;
        return false;
    }

    // ---- Network -----------------------------------------------------------------------------------------------------------------

    /// <summary>The engine's contracts into this peer's view (the host's own window).</summary>
    private void ContractsShow(PowerRelations rel)
    {
        var n = 0;
        var all = rel.Contracts.All;
        for (var i = 0; i < all.Length; i++)
            if (all[i].State != ContractState.None)
            {
                ref readonly var c = ref all[i];
                ContractsView[n] = c;
                ContractsView[n].Until = Math.Max(0, (c.State == ContractState.Offered || c.State == ContractState.Taken ? c.Until - rel.Tick : 0) / World.TickRate);
                ContractsView[n].Body = (ushort)i; // (the view's Body holds its slot)
                n++;
            }
        ContractsCount = n;
        ContractsVersion++;
    }

    /// <summary><see cref="Msg.Contracts"/>: u8 n, each (u8 slot, u16 serial, u8 kind, u8 state, u8 power, u8 target, u8 taker, u8 material, u16 units, i32 pay,
    /// u16 seconds left, u16 progress, u16 need, 3 x f32 place).</summary>
    private void WriteContracts(PacketWriter w)
    {
        w.Begin(Msg.Contracts).U8((byte)ContractsCount);
        for (var i = 0; i < ContractsCount; i++)
        {
            ref readonly var c = ref ContractsView[i];
            w.U8((byte)c.Body).U16(c.Serial).U8((byte)c.Kind).U8((byte)c.State).U8(c.Power).U8(c.Target).U8(c.Taker).U8(c.Material).U16(c.Units).U32((uint)c.Pay)
                .U16((ushort)Math.Min(c.Until, 65535)).U16((ushort)Math.Clamp(c.Progress, 0, 65535)).U16((ushort)Math.Clamp(c.Need, 0, 65535)).F32(c.At.X).F32(c.At.Y).F32(c.At.Z);
        }
    }

    /// <summary>Server: the contracts to one joiner.</summary>
    private void SendContractsTo(RakNetGUID to)
    {
        if (!CrewStandingOn || ContractsCount == 0)
            return;
        WriteContracts(SbW);
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: the contracts (after the id).</summary>
    internal void ReadContracts(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        int n = r.U8();
        var k = 0;
        for (var i = 0; i < n && r.Has(33); i++)
        {
            var c = new Contract
            {
                Body = r.U8(), Serial = r.U16(), Kind = (ContractKind)r.U8(), State = (ContractState)r.U8(), Power = r.U8(), Target = r.U8(), Taker = r.U8(), Material = r.U8(),
                Units = r.U16(), Pay = (int)r.U32(), Until = r.U16(), Progress = r.U16(), Need = r.U16(), TakerName = "",
            };
            c.At = new Vector3(r.F32(), r.F32(), r.F32());
            if (k < ContractsView.Length)
                ContractsView[k++] = c;
        }
        ContractsCount = k;
        ContractsVersion++;
    }

    /// <summary>A new world: no contracts shown, every note read.</summary>
    private void PoliticsClear()
    {
        ContractsCount = 0;
        ContractsVersion++;
        _contractsSent = -1;
        _notesRead = _rel.NoteTotal;
    }

    /// <summary>The window verb <c>crewpolitics standing</c> (host, a test state): how power <paramref name="power"/> sees <paramref name="me"/>'s faction (else them) set.</summary>
    public string PoliticsStanding(Player me, byte power, int v)
    {
        if (!CrewStandingOn || CrewPowers.Of(power) is null)
            return "no such power";
        var rel = Relations;
        var party = FactionTable.Of[me.Id];
        if (party != 0)
            rel.MoveParty(power, party, v - rel.PartyGet(power, party));
        else
            rel.MovePlayer(power, me.Id, v - rel.Peek(power, me.Id));
        return $"{CrewPowers.Of(power)!.Name} now at {CrewStanding(power, me.Id)}";
    }

    /// <summary>A tool's line (the window verb <c>crewpolitics</c>; allocates): this peer's pacts with factions and contracts.</summary>
    public string PoliticsLine()
    {
        var t = FactionTable;
        var sb = new System.Text.StringBuilder($"{(IsServer ? "host" : "client")}: party pacts {t.PartyPactCount}:");
        for (var k = 0; k < t.PartyPactCount; k++)
            sb.Append($" [{(PactKind)t.PartyPactKind[k]} {(t.PartyPactPower[k] < t.StoryNames.Length ? t.StoryNames[t.PartyPactPower[k]] : "?")} - {t.Names[t.PartyPactParty[k]]}]");
        sb.Append($"; offers {t.OfferCount}, asks {t.AskCount}; contracts {ContractsCount}:");
        for (var i = 0; i < ContractsCount; i++)
            sb.Append($" [{ContractsView[i].Kind} {CrewPowers.Of(ContractsView[i].Power)?.Name} {ContractsView[i].State} taker {ContractsView[i].Taker} pay {ContractsView[i].Pay}]");
        return sb.ToString();
    }
}
