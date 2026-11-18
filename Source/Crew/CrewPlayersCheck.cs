using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--crew-check --crew-fleet players</c> (docs/sandbox/design/faction-controllers.md phase 5): players in the politics on the relations engine with a
/// stand-in game. A player faction at war with the Militia proposes a ceasefire and the Militia signs it; its leader breaks it, and the Militia's allies
/// (the Traders) hear and think less of the faction; a member's stray fire is forgiven, deliberate fire breaks a second ceasefire; the Militia beaten
/// offers one by itself; an alliance with the Traders makes their crews fight the faction's foes; a blood feud refuses every pact; a tribute unpaid lapses;
/// contracts (escort, deliver, raid) offered, taken, tracked and paid, one failed, one refused; a save round trip, and a save from before (version 2).
/// Creative's engine (no controllers: no Might) is the one used, so contracts are shown to work there.
/// </summary>
public static class CrewPlayersCheck
{
    private sealed class Host : IContractHost
    {
        public bool ShipAlive = true, AdaHere = true;
        public int Paid;
        public readonly List<string> Told = [];

        public bool Escortee(byte power, out ushort body, out uint uid, out Vector3 at)
        {
            (body, uid, at) = (5, 77, new Vector3(100, 0, 0));
            return ShipAlive;
        }

        public bool ShipAt(ushort body, out Vector3 at)
        {
            at = new Vector3(100, 0, 0);
            return ShipAlive && body == 5;
        }

        public ushort BodyOfUid(uint uid) => uid == 77 ? (ushort)5 : (ushort)0;
        public bool PartyNear(byte taker, Vector3 at, float range) => true;
        public int Cargo(byte taker, Vector3 at, float range, byte material, int units, bool take) => units;

        public bool DeliverPlace(byte power, byte taker, out Vector3 at)
        {
            at = new Vector3(0, 0, 800);
            return true;
        }

        public void Pay(byte player, int credits) => Paid += credits;
        public void Tell(byte player, string line) => Told.Add(line);
        public byte PlayerNamed(string name) => name == "Ada" && AdaHere ? (byte)0 : (byte)255;
        public bool AnyoneFor(byte power) => true;
    }

    public static bool Run()
    {
        Console.WriteLine("fleet players: pacts with a player faction, a broken ceasefire heard by allies, contracts");
        var ok = true;
        void Check(bool c, string what)
        {
            Console.WriteLine($"  {(c ? "ok" : "FAILED")}: {what}");
            ok &= c;
        }
        byte traders = CrewPowers.ForKindName("Traders"), raiders = CrewPowers.ForKindName("Raiders"), militia = CrewPowers.ForKindName("Militia"), mercs = CrewPowers.ForKindName("Mercenaries");
        if (traders == 0 || raiders == 0 || militia == 0 || mercs == 0)
        {
            Console.WriteLine("fleet players: FAILED (Traders, Raiders, Militia and Mercenaries powers are needed)");
            return false;
        }
        var t = new FactionTable();
        const byte party = 1, ada = 0;
        (t.Names[party], t.Leader[party], t.Of[ada]) = ("Test Outfit", ada, party);
        var rel = new PowerRelations { Table = t };
        rel.Reset();
        var heardBy = 0;
        rel.PartyBroke = (f, p, k) => heardBy++;
        void Minutes(double m)
        {
            for (var k = 0; k < m * 60 * World.TickRate; k++)
                rel.Step();
        }
        void Keep(byte a, byte b, int v) => rel.Move(a, b, v - rel.Get(a, b));
        void KeepParty(byte p, int v) => rel.MoveParty(p, party, v - rel.PartyGet(p, party));
        string LastNews()
        {
            for (var k = rel.NewsTotal - 1; k >= Math.Max(0, rel.NewsTotal - 4); k--)
                if (rel.NewsLine(k) is { } l && l.Contains("breaks"))
                    return l;
            return rel.NewsLine(rel.NewsTotal - 1) ?? "";
        }

        // The Militia and Traders allied against the Raiders.
        Keep(traders, militia, 75);
        Keep(militia, traders, 75);
        Keep(traders, raiders, -70);
        Keep(militia, raiders, -70);
        Minutes(1);
        Check(rel.PactOf(traders, militia) == PactKind.Alliance, $"the Traders and the Militia ally against the Raiders ({rel.PactOf(traders, militia)})");
        // At war with the Militia: a ceasefire proposed and signed.
        KeepParty(militia, -70);
        KeepParty(traders, 30);
        var why = rel.Propose(party, militia, PactKind.Ceasefire);
        Minutes(0.2);
        Check(why == null && rel.PartyPact(militia, party) == PactKind.Ceasefire && rel.PartyHoldsFire(militia, ada) && rel.PartyGet(militia, party) == rel.Rules.CeasefireRelation,
            $"the faction at war with the Militia ({-70}) proposes a ceasefire and it is signed ({rel.PartyPact(militia, party)}, standing {rel.PartyGet(militia, party)}; {why ?? "asked"})");
        // The leader breaks it: the Militia takes it hard, its allies hear.
        var tradersBefore = rel.PartyGet(traders, party);
        var militiaBefore = rel.PartyGet(militia, party);
        rel.Break(party, militia, default);
        Check(rel.PartyPact(militia, party) == PactKind.None && LastNews().Contains("breaks its ceasefire"), $"the leader breaks it: news \"{LastNews()}\"");
        Check(rel.PartyGet(militia, party) < militiaBefore, $"the Militia takes it hard ({militiaBefore} -> {rel.PartyGet(militia, party)})");
        Check((rel.LastHeard >> (traders - 1) & 1) != 0 && rel.PartyGet(traders, party) < tradersBefore && heardBy == 1,
            $"its ally the Traders hear and think less of the faction ({tradersBefore} -> {rel.PartyGet(traders, party)}; heard by {rel.HeardList(rel.LastHeard)})");
        // A second ceasefire; a member's stray rounds are forgiven, deliberate fire breaks it.
        rel.Propose(party, militia, PactKind.Ceasefire);
        Minutes(0.2);
        var signed = rel.PartyPact(militia, party) == PactKind.Ceasefire;
        for (var k = 0; k < rel.Rules.FriendlyFireHits; k++)
            rel.Post(RelEvent.HitBody, militia, 0, ada, default);
        var held = rel.PartyPact(militia, party) == PactKind.Ceasefire;
        rel.Post(RelEvent.HitBody, militia, 0, ada, default);
        Check(signed && held && rel.PartyPact(militia, party) == PactKind.None, $"a second ceasefire: {rel.Rules.FriendlyFireHits} stray hits forgiven, the next breaks it ({LastNews()})");
        // A member's splash on an allied ship (two hits) is forgiven: no deed (the game clears the foe mark the hit made), the pact holds; a kill under a ceasefire breaks it at once.
        rel.Propose(party, militia, PactKind.Ceasefire);
        Minutes(0.2);
        Minutes(1.1); // (a fresh minute of stray hits)
        var before = rel.PartyGet(militia, party);
        var forgiven = rel.Post(RelEvent.HitBody, militia, 0, ada, default) & rel.Post(RelEvent.HitCrew, militia, 0, ada, default);
        Check(rel.PartyPact(militia, party) == PactKind.Ceasefire && forgiven && rel.PartyGet(militia, party) == before,
            $"splash on a partner's ship: forgiven, no deed, the ceasefire holds ({rel.PartyPact(militia, party)}, standing {before} -> {rel.PartyGet(militia, party)})");
        var killForgiven = rel.Post(RelEvent.KillCrew, militia, 0, ada, default);
        Check(!killForgiven && rel.PartyPact(militia, party) == PactKind.None && rel.PartyGet(militia, party) < before,
            $"a kill under a ceasefire is never stray: it breaks the pact at once ({before} -> {rel.PartyGet(militia, party)})");
        // The Militia beaten by the faction offers a ceasefire itself.
        for (var k = 0; k < 4; k++)
            rel.Post(RelEvent.DestroyShip, militia, 0, ada, default);
        Minutes(0.2);
        var offered = rel.Offer[party * PowerRelations.N + militia - 1];
        Check(offered == PactKind.Ceasefire && rel.Answer(party, militia, true) == null && rel.PartyPact(militia, party) == PactKind.Ceasefire,
            $"beaten, the Militia offers a ceasefire and the leader takes it ({offered})");
        // An alliance with the Traders: their crews fight a power fighting the faction, though they think it neutral.
        KeepParty(traders, 70);
        KeepParty(raiders, -60);
        Keep(traders, mercs, 30);
        rel.Propose(party, traders, PactKind.Alliance);
        Minutes(0.2);
        Check(rel.PartyPact(traders, party) == PactKind.Alliance && rel.ShipFights(traders, mercs, 0, ada) && !rel.ShipFights(traders, mercs, 0, 255),
            $"allied with the Traders: their crews fight the Mercenaries fighting the faction, and leave them be otherwise ({rel.PartyPact(traders, party)})");
        // A tribute unpaid lapses.
        KeepParty(mercs, -60);
        rel.Propose(party, mercs, PactKind.Tribute);
        Minutes(0.2);
        var ti = rel.PartyIndex(mercs, party);
        var tribute = ti >= 0 && rel.PartyK[ti] == PactKind.Tribute;
        rel.TributePaid(ti, false);
        Check(tribute && rel.PartyPact(mercs, party) == PactKind.None, "a tribute signed, then unpaid: it lapses");
        // A refused proposal says why and waits.
        KeepParty(raiders, 30);
        rel.Propose(party, raiders, PactKind.Alliance);
        Minutes(0.2);
        Check(rel.PartyPact(raiders, party) == PactKind.None && rel.Propose(party, raiders, PactKind.Alliance) is { } wait && wait.Contains("turned you down"), "an alliance the Raiders don't want: turned down, and asked again only later");
        // Blood feud (where the controllers run: Might set).
        var war = new PowerRelations { Table = t, Might = _ => 1 };
        war.Reset();
        war.MoveParty(raiders, party, -150);
        for (var k = 0; k < 3; k++)
            war.Post(RelEvent.DestroyShip, raiders, 0, ada, default);
        Check(war.PartyFeud[party * PowerRelations.N + raiders - 1] && war.Propose(party, raiders, PactKind.Ceasefire) is { } nope && nope.Contains("blood feud"),
            $"ships destroyed at the floor: the Raiders swear a blood feud and refuse a ceasefire ({war.PartyGet(raiders, party)})");
        // Contracts (every power trusting the faction enough to give it work).
        for (var p = (byte)1; p <= CrewPowers.Count; p++)
            if (!rel.PartyFeud[party * PowerRelations.N + p - 1])
                KeepParty(p, 30);
        var host = new Host();
        var cs = rel.Contracts;
        for (var k = 0; k < 60 * World.TickRate * 4 && cs.Offered < 6; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        int Find(ContractKind kind)
        {
            for (var i = 0; i < PowerContracts.Max; i++)
                if (cs.All[i].State == ContractState.Offered && cs.All[i].Kind == kind)
                    return i;
            return -1;
        }
        var esc = Find(ContractKind.Escort);
        var del = Find(ContractKind.Deliver);
        var raid = Find(ContractKind.Raid);
        Check(esc >= 0 && del >= 0 && raid >= 0, $"offers made: {cs.Offered} (escort {esc}, deliver {del}, raid {raid})");
        string? Take(int i) => i < 0 ? "none" : cs.Take(rel, host, ada, i, cs.All[i].Serial, "Ada");
        // A raid on a power the faction holds a pact with is refused.
        var raidOn = raid >= 0 ? cs.All[raid].Target : (byte)0;
        string? refused = null;
        if (raidOn != 0 && rel.PartyPact(raidOn, party) != PactKind.None)
            refused = Take(raid);
        else if (raidOn != 0)
        {
            rel.MoveParty(raidOn, party, 80 - rel.PartyGet(raidOn, party));
            rel.Propose(party, raidOn, PactKind.NonAggression);
            Minutes(0.2);
            refused = rel.PartyPact(raidOn, party) != PactKind.None ? Take(raid) : "no pact made";
            rel.Break(party, raidOn, default);
        }
        Check(refused != null && refused.Contains("break it first"), $"a raid on a power the faction holds a pact with is refused ({refused})");
        var takes = (Take(esc), Take(del));
        Check(takes is (null, null), $"the escort and the delivery taken ({takes.Item1 ?? "ok"}, {takes.Item2 ?? "ok"})");
        Check(Take(raid) is { } third && third.Contains("already"), "a third contract at once is refused");
        // The taker leaves (their id may go to the next joiner): the contracts are held for them by name, nothing worked or paid, their clocks stopped.
        host.AdaHere = false;
        var heldPaid = host.Paid;
        var until = cs.All[esc].Until;
        for (var k = 0; k < 10 * World.TickRate; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        Check(cs.All[esc].State == ContractState.Taken && cs.All[esc].Taker == 255 && cs.All[esc].Progress == 0 && host.Paid == heldPaid && cs.All[esc].Until > until,
            $"the taker away: held for them (taker {cs.All[esc].Taker}, progress {cs.All[esc].Progress}, clock stopped {cs.All[esc].Until > until})");
        host.AdaHere = true;
        var paid0 = host.Paid;
        var done0 = cs.Done;
        var tradersStand = rel.PartyGet(cs.All[del].Power, party);
        for (var k = 0; k < (rel.Rules.EscortSeconds + 5) * World.TickRate; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        Check(cs.Done - done0 == 2 && host.Paid > paid0,
            $"escorted {rel.Rules.EscortSeconds} s and delivered: both done and paid ({host.Paid - paid0} credits; standing {tradersStand} -> {rel.PartyGet(cs.All[del].Power, party)})");
        var raid2 = Find(ContractKind.Raid);
        if (raid2 >= 0 && rel.PartyPact(cs.All[raid2].Target, party) != PactKind.None)
            rel.Break(party, cs.All[raid2].Target, default);
        var raidTaken = raid2 >= 0 && Take(raid2) == null;
        if (raidTaken)
        {
            rel.Post(RelEvent.DestroyShip, cs.All[raid2].Target, 0, ada, default);
            for (var k = 0; k < 2 * World.TickRate; k++)
            {
                rel.Step();
                cs.Step(rel, host);
            }
        }
        Check(raidTaken && cs.All[raid2].State == ContractState.Done, $"a raid taken and done by destroying a ship of {(raid2 >= 0 ? CrewPowers.Of(cs.All[raid2].Target)?.Name : "none")}");
        // An escort whose ship is lost fails.
        for (var k = 0; k < 60 * World.TickRate * 6 && Find(ContractKind.Escort) < 0; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        var esc2 = Find(ContractKind.Escort);
        var took2 = esc2 >= 0 && Take(esc2) == null;
        host.ShipAlive = false;
        for (var k = 0; k < 2 * World.TickRate; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        host.ShipAlive = true;
        Check(took2 && cs.All[esc2].State == ContractState.Failed, "an escort whose ship is lost fails");
        // A save keeps the pacts with the faction, the feud and the contracts; a save from before (version 2) reads with none.
        for (var k = 0; k < 60 * World.TickRate * 4 && Find(ContractKind.Deliver) < 0; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        var del2 = Find(ContractKind.Deliver);
        if (del2 >= 0)
            Take(del2);
        var bytes = rel.SaveBytes();
        var back = new PowerRelations { Table = t };
        back.Load(bytes);
        var taken = 0;
        foreach (var c in back.Contracts.All)
            taken += c.State == ContractState.Taken && c.TakerName == "Ada" ? 1 : 0;
        Check(back.PartyPact(traders, party) == rel.PartyPact(traders, party) && back.PartyPact(militia, party) == rel.PartyPact(militia, party) && rel.PartyPact(militia, party) != PactKind.None && taken == (del2 >= 0 ? 1 : 0),
            $"a save round trip keeps the pacts ({back.PartyPact(traders, party)} with the Traders, {back.PartyPact(militia, party)} with the Militia) and the taken contract ({taken})");
        var wb = war.SaveBytes();
        var wback = new PowerRelations { Table = t, Might = _ => 1 };
        wback.Load(wb);
        Check(wback.PartyFeud[party * PowerRelations.N + raiders - 1], "... and the blood feud");
        var empty = new PowerRelations { Table = new FactionTable() };
        empty.Reset();
        var v3 = empty.SaveBytes();
        var v2 = v3[..^3];
        v2[0] = 2;
        var old = new PowerRelations { Table = new FactionTable() };
        old.Reset();
        old.Move(traders, militia, 7);
        old.Load(v2);
        Check(old.Get(traders, militia) == empty.Get(traders, militia), "a powers section from before (version 2) reads, with no pacts with factions or contracts");
        // Its cost: steps with nothing to say allocate nothing.
        for (var k = 0; k < 120; k++)
        {
            rel.Step();
            cs.Step(rel, host);
        }
        // Relations alone (every power thinking of the faction each think: offers weighed, pacts kept): nothing allocated while nothing is said.
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        var said0 = rel.NewsTotal + rel.NoteTotal;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var steps = 60 * World.TickRate;
        for (var k = 0; k < steps; k++)
            rel.Step();
        var us = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency / steps;
        var used = GC.GetAllocatedBytesForCurrentThread() - b0;
        var said = rel.NewsTotal + rel.NoteTotal != said0;
        Console.WriteLine($"  cost: relations {us:0.00} us a tick, {used} bytes in {steps} ticks{(said ? " (news or notes said: a line allocates)" : "")}");
        Check(said || used == 0, "the relations' steps allocate nothing while nothing is said");
        // Contracts alone, between offers (a second's work for each held one).
        var cb = GC.GetAllocatedBytesForCurrentThread();
        var cv = cs.Offered + cs.Done + cs.Failed;
        var c0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var k = 0; k < 30 * World.TickRate; k++)
            cs.Step(rel, host);
        var cus = (System.Diagnostics.Stopwatch.GetTimestamp() - c0) * 1e6 / System.Diagnostics.Stopwatch.Frequency / (30 * World.TickRate);
        var cused = GC.GetAllocatedBytesForCurrentThread() - cb;
        var cchanged = cs.Offered + cs.Done + cs.Failed != cv;
        Console.WriteLine($"  cost: contracts {cus:0.00} us a tick, {cused} bytes in {30 * World.TickRate} ticks{(cchanged ? " (an offer or an end: a line allocates)" : "")}");
        Check(cchanged || cused == 0, "the contracts' steps allocate nothing between offers and ends");
        foreach (var line in host.Told)
            Console.WriteLine($"  [told] {line}");
        for (var k = Math.Max(0, rel.NoteTotal - 12); k < rel.NoteTotal; k++)
            Console.WriteLine($"  [note] {rel.NoteAt(k)?.Text}");
        for (var k = Math.Max(0, rel.NewsTotal - 14); k < rel.NewsTotal; k++)
            Console.WriteLine($"  [news] {rel.NewsLine(k)}");
        Console.WriteLine($"fleet players: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
