using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Players in the politics (docs/sandbox/design/faction-controllers.md phase 5): pacts between a power and a player faction, made through the Faction
/// window by the faction's leader. A leader proposes (ceasefire, non-aggression, trade, alliance, or a tribute the leader pays in credits) and the power
/// answers at its next think by its own scoring, as it answers another power; a power offers one by itself when its scoring wants it, and the leader
/// takes it or leaves it. A pact holds crews' fire both ways; an alliance makes the power's crews fight the faction's foes they see and answer its call
/// for help (the controllers: CrewControllers.cs). Breaking one (the leader's button, or an attack by a member) is news, the power takes it hard, and
/// every power that knows (its allies and friends, a pact partner of the breaker's, a crew that saw it) thinks less of the breaker. A faction's grave
/// deeds against a power already at the floor make a blood feud, as between powers (phase 3's question 5). Fixed arrays; a line of news or a notice
/// allocates, nothing else.
/// </summary>
public sealed partial class PowerRelations
{
    public const int MaxPartyPacts = 32, Parties = FactionTable.Max;

    // Pacts with player factions: kind, the power, the faction (its FactionTable id), start and end (0: none), real hours kept, a tribute's next payment.
    public readonly PactKind[] PartyK = new PactKind[MaxPartyPacts];
    public readonly byte[] PartyPower = new byte[MaxPartyPacts], PartyOf = new byte[MaxPartyPacts];
    public readonly long[] PartyStart = new long[MaxPartyPacts], PartyEnd = new long[MaxPartyPacts], PartyDue = new long[MaxPartyPacts];
    public readonly int[] PartyKept = new int[MaxPartyPacts];

    /// <summary>How power p sees faction f is a blood feud, at [f * N + p - 1]: no peace, ever (saved).</summary>
    public readonly bool[] PartyFeud = new bool[Parties * N];

    /// <summary>A faction's proposal to a power, waiting for its think ([f * N + p - 1]).</summary>
    public readonly PactKind[] PartyAsk = new PactKind[Parties * N];
    private readonly long[] _partyAskAfter = new long[Parties * N]; // a refused faction asks again after this tick

    /// <summary>A power's offer to a faction, standing until <see cref="OfferUntil"/> ([f * N + p - 1]).</summary>
    public readonly PactKind[] Offer = new PactKind[Parties * N];
    public readonly long[] OfferUntil = new long[Parties * N];
    private readonly long[] _offerAfter = new long[Parties * N];

    // A war with a faction: since when, the power's losses to it, its last.
    private readonly long[] _partyWarSince = new long[Parties * N], _partyLastLoss = new long[Parties * N];
    private readonly int[] _partyLoss = new int[Parties * N];
    private readonly long[] _partyStrayAt = new long[Parties * N];
    private readonly byte[] _partyStrayHits = new byte[Parties * N];

    /// <summary>When each faction last called its allies (the Faction window's button).</summary>
    public readonly long[] CallAt = new long[Parties];

    /// <summary>The powers that heard of the last broken pact with a faction, a bit each (power - 1): a tool's and the notice's.</summary>
    public int LastHeard;

    /// <summary>Lines for one faction's members (a pact signed, turned down, offered, ended, broken), a ring the game reads (<see cref="NoteTotal"/>).</summary>
    public readonly (byte Party, string Text)[] Notes = new (byte, string)[32];
    public int NoteTotal;

    /// <summary>A faction broke a pact with a power: the game tells the player factions allied with it (party, power, kind).</summary>
    public Action<byte, byte, PactKind>? PartyBroke;

    /// <summary>Contracts for credits the powers offer (Crew/CrewContracts.cs), saved with the relations.</summary>
    public readonly PowerContracts Contracts = new();

    private void ResetParties()
    {
        Array.Clear(PartyK);
        Array.Clear(PartyFeud);
        Array.Clear(PartyAsk);
        Array.Clear(_partyAskAfter);
        Array.Clear(Offer);
        Array.Clear(OfferUntil);
        Array.Clear(_offerAfter);
        Array.Clear(_partyWarSince);
        Array.Clear(_partyLastLoss);
        Array.Clear(_partyLoss);
        Array.Clear(_partyStrayAt);
        Array.Clear(_partyStrayHits);
        Array.Clear(CallAt);
        LastHeard = 0;
        Contracts.Clear();
    }

    /// <summary>A line for one faction's members (the controllers': a call answered).</summary>
    public void NoteFor(byte party, string text) => Note(party, text);

    private void Note(byte party, string text)
    {
        Notes[NoteTotal % Notes.Length] = (party, text);
        NoteTotal++;
    }

    /// <summary>A note posted <paramref name="total"/>th, if still held.</summary>
    public (byte Party, string Text)? NoteAt(int total) => total >= 0 && total < NoteTotal && total >= NoteTotal - Notes.Length ? Notes[total % Notes.Length] : null;

    private string PartyWord(byte party) => party > 0 && party < Parties && Table.Names[party] is { Length: > 0 } n ? n : $"faction {party}";

    private static int PK(byte party, byte power) => party * N + power - 1;

    private bool PartyLive(byte party) => party > 0 && party < Parties && Table.Names[party].Length > 0;

    // ---- Reading -----------------------------------------------------------------------------------------------------------------

    public int PartyIndex(byte power, byte party)
    {
        for (var i = 0; i < MaxPartyPacts; i++)
            if (PartyK[i] != PactKind.None && PartyPower[i] == power && PartyOf[i] == party)
                return i;
        return -1;
    }

    /// <summary>The pact between power <paramref name="power"/> and faction <paramref name="party"/> (None: none).</summary>
    public PactKind PartyPact(byte power, byte party)
    {
        if (party == 0)
            return PactKind.None;
        var i = PartyIndex(power, party);
        return i < 0 ? PactKind.None : PartyK[i];
    }

    /// <summary>A pact holds between power <paramref name="power"/> and <paramref name="player"/>'s faction: crews hold their fire.</summary>
    public bool PartyHoldsFire(byte power, byte player) => player != 255 && PartyPact(power, Table.Of[player]) != PactKind.None;

    /// <summary>Power <paramref name="power"/> is allied with <paramref name="player"/>'s faction: its crews fight their foes and answer their call.</summary>
    public bool PartyAllied(byte power, byte player) => player != 255 && PartyPact(power, Table.Of[player]) == PactKind.Alliance;

    /// <summary>How power <paramref name="power"/> sees faction <paramref name="party"/> (its start for one it hasn't met).</summary>
    public int PartyGet(byte power, byte party)
    {
        if (Def(power) is not { } def || party == 0 || party >= Parties)
            return 0;
        var k = PK(party, power);
        return MetFaction[k] ? Table.StoryStanding[k] : def.StartPlayers;
    }

    private ref sbyte PartyStanding(byte power, byte party)
    {
        var k = PK(party, power);
        if (!MetFaction[k])
        {
            MetFaction[k] = true;
            Table.StoryStanding[k] = Def(power)!.StartPlayers;
            Table.Version++;
        }
        return ref Table.StoryStanding[k];
    }

    /// <summary>How <paramref name="power"/> sees faction <paramref name="party"/> moves by <paramref name="delta"/> (never past -99 but by a blood feud; a feud never moves).</summary>
    public void MoveParty(byte power, byte party, int delta)
    {
        if (delta == 0 || Def(power) is null || party == 0 || party >= Parties || PartyFeud[PK(party, power)])
            return;
        ref var v = ref PartyStanding(power, party);
        var was = v;
        v = (sbyte)Math.Clamp(v + delta, BloodValue + 1, 100);
        if (v == was)
            return;
        Table.Version++;
        var k = PK(party, power);
        if (was >= 0 && v < 0)
            Console.WriteLine($"[crew] {Def(power)!.Name} now fight faction {party} (standing {v})");
        if (was > Rules.WarLine && v <= Rules.WarLine)
        {
            (_partyWarSince[k], _partyLoss[k]) = (Tick, 0);
            Say($"{Name(power)} declares war on {PartyWord(party)}");
        }
        else if (was <= Rules.WarLine && v > Rules.WarLine)
            _partyWarSince[k] = 0;
        else if (was < Rules.FriendlyLine && v >= Rules.FriendlyLine)
            Say($"{Name(power)} now counts {PartyWord(party)} as friends");
    }

    private void SetParty(byte power, byte party, int v)
    {
        ref var s = ref PartyStanding(power, party);
        if (s == v)
            return;
        var was = s;
        s = (sbyte)Math.Clamp(v, -100, 100);
        Table.Version++;
        if (was <= Rules.WarLine && s > Rules.WarLine)
            _partyWarSince[PK(party, power)] = 0;
    }

    // ---- Deeds by a person --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A person did <paramref name="e"/> to power <paramref name="victim"/>: a member's attack breaks their faction's pact with it (stray fire in a minute
    /// is forgiven first, as between powers), a grave deed at the floor makes a blood feud, and a war's losses count. True: forgiven (no deed).
    /// </summary>
    private bool PlayerDeed(RelEvent e, NpcPowerDef v, byte victim, byte player, Vector3 at)
    {
        var party = Table.Of[player];
        if (party == 0)
            return false;
        var k = PK(party, victim);
        if (Attack(e) && PartyIndex(victim, party) is var i and >= 0)
        {
            if (e <= RelEvent.Capture)
            {
                if (Tick - _partyStrayAt[k] > 60L * T)
                    _partyStrayHits[k] = 0;
                _partyStrayAt[k] = Tick;
                if (_partyStrayHits[k] < 255)
                    _partyStrayHits[k]++;
                if (_partyStrayHits[k] <= Rules.FriendlyFireHits)
                {
                    if (_partyStrayHits[k] == 1)
                        Console.WriteLine($"[crew] {v.Name} hit by {PartyWord(party)}'s fire under their {PactName(PartyK[i])}: stray, forgiven");
                    return true;
                }
            }
            BrokenParty(i, at, $"{PartyWord(party)}'s attack");
        }
        if (Might != null && e is RelEvent.DestroyShip or RelEvent.Capture or RelEvent.CaptureOfficer or RelEvent.RaidConvoy or RelEvent.TakePlanet
            && PartyGet(victim, party) <= BloodValue + 1 && v.Pride >= Rules.BloodPride && !PartyFeud[k])
        {
            SetParty(victim, party, BloodValue);
            PartyFeud[k] = true;
            Version++;
            Say($"{v.Name} swears a blood feud against {PartyWord(party)}");
            Note(party, $"{v.Name} has sworn a blood feud against you: they will never make peace.");
        }
        if (e is RelEvent.KillCrew or RelEvent.DestroyShip && PartyGet(victim, party) <= Rules.WarLine)
        {
            _partyLoss[k] += e == RelEvent.DestroyShip ? 5 : 1;
            _partyLastLoss[k] = Tick;
        }
        return false;
    }

    // ---- Wanting a pact ---------------------------------------------------------------------------------------------------------

    /// <summary>A power both see below 0: power <paramref name="a"/> sees it so, and it sees faction <paramref name="party"/> so (<paramref name="war"/>: one at war).</summary>
    private bool PartyCommonEnemy(byte a, byte party, bool war)
    {
        for (var c = (byte)1; c <= Count; c++)
            if (c != a && !Gone[c] && Def(c) is { Controller: true } && Get(a, c) < 0 && PartyGet(c, party) < 0
                && (!war || BandOf(a, c) == RelBand.War || PartyGet(c, party) <= Rules.WarLine))
                return true;
        return false;
    }

    /// <summary>A power's own wish for a ceasefire with a faction: its losses to it this war past its pride line, or a long war gone quiet.</summary>
    private bool PartyCeasefireWanted(byte a, byte party)
    {
        var k = PK(party, a);
        if (PartyGet(a, party) >= 0 || PartyFeud[k])
            return false;
        var since = _partyWarSince[k];
        var stale = (long)Rules.StaleWarMinutes * 60 * T;
        return _partyLoss[k] >= Rules.CeasefireLosses * (0.5f + Def(a)!.Pride)
            || since > 0 && _partyLastLoss[k] > since && Tick - since >= stale && Tick - _partyLastLoss[k] >= stale / 3;
    }

    /// <summary>
    /// Whether power <paramref name="a"/> wants pact <paramref name="kind"/> with faction <paramref name="party"/>: <paramref name="asked"/>, as an answer to the
    /// faction's proposal (a power not too aggressive takes a ceasefire from anyone it fights); else as its own offer (stricter, as between powers). Null: yes;
    /// else why not (the leader's notice).
    /// </summary>
    private const string No = "no"; // (a power weighing its own offer: no reason worded, nothing allocated)

    public string? PartyWants(byte a, byte party, PactKind kind, bool asked, bool explain = true)
    {
        var def = Def(a)!;
        var v = PartyGet(a, party);
        var cur = PartyPact(a, party);
        if (PartyFeud[PK(party, a)])
            return !explain ? No : $"{def.Name} has sworn a blood feud against you: no peace, ever";
        switch (kind)
        {
            case PactKind.Ceasefire:
                if (cur != PactKind.None)
                    return !explain ? No : $"you already have a {PactName(cur)} with {def.Name}";
                if (v >= 0)
                    return !explain ? No : $"{def.Name} isn't fighting you: a ceasefire is for a war";
                if (PartyCeasefireWanted(a, party) || asked && def.Aggression < Rules.CeasefireRefuseAggression)
                    return null;
                return !explain ? No : $"{def.Name} wants to fight on: hurt them more first (their losses to you: {_partyLoss[PK(party, a)]})";
            case PactKind.Tribute:
                if (cur is not (PactKind.None or PactKind.Ceasefire))
                    return !explain ? No : $"you already have a {PactName(cur)} with {def.Name}";
                if (v >= 0)
                    return !explain ? No : $"{def.Name} isn't fighting you: there's nothing to pay for";
                if (!asked)
                    return "a power never asks a faction for tribute";
                return def.Greed >= Rules.TributeGreed ? null : !explain ? No : $"{def.Name} won't be bought";
            case PactKind.NonAggression:
                if (cur is not (PactKind.None or PactKind.Ceasefire))
                    return !explain ? No : $"you already have a {PactName(cur)} with {def.Name}";
                if (v < Rules.NeutralLine)
                    return !explain ? No : $"{def.Name} doesn't trust you enough: it needs to be neutral (it is at {v}, neutral is {Rules.NeutralLine})";
                return asked || PartyCommonEnemy(a, party, war: false) ? null : "no common enemy";
            case PactKind.Trade:
                if (cur is not (PactKind.None or PactKind.Ceasefire or PactKind.NonAggression))
                    return !explain ? No : $"you already have a {PactName(cur)} with {def.Name}";
                if (v < Rules.NeutralLine)
                    return !explain ? No : $"{def.Name} doesn't trust you enough to trade: it needs to be neutral (it is at {v})";
                if (def.Greed < Rules.TradeGreed)
                    return !explain ? No : $"{def.Name} has no interest in trade";
                return asked || Might != null && MightOf(a) > 0 ? null : "no economy";
            case PactKind.Alliance:
                if (cur == PactKind.Alliance)
                    return !explain ? No : $"you are already allied with {def.Name}";
                if (v < Rules.FriendlyLine)
                    return !explain ? No : $"{def.Name} isn't friendly enough for an alliance: it needs {Rules.FriendlyLine} (it is at {v})";
                if (def.Loyalty < Rules.AllianceLoyalty)
                    return !explain ? No : $"{def.Name} makes no alliances";
                return PartyCommonEnemy(a, party, war: !asked) ? null : !explain ? No : $"{def.Name} sees no common enemy to ally against";
        }
        return "that is no pact a faction can make";
    }

    // ---- The leader's acts (the Faction window: GameCrewPlayerPacts.cs) -------------------------------------------------------------

    /// <summary>Faction <paramref name="party"/>'s leader proposes <paramref name="kind"/> to <paramref name="power"/>: answered at its next think (or at once, if
    /// the power offered that very pact). Null: asked; else why not.</summary>
    public string? Propose(byte party, byte power, PactKind kind)
    {
        if (!PartyLive(party))
            return "you are in no faction";
        if (Def(power) is not { Controller: true } def || Gone[power])
            return "that power doesn't deal with anyone";
        if (kind is not (PactKind.Ceasefire or PactKind.NonAggression or PactKind.Trade or PactKind.Alliance or PactKind.Tribute))
            return "that is no pact a faction can make";
        var k = PK(party, power);
        if (Offer[k] == kind && Tick < OfferUntil[k])
            return Answer(party, power, true);
        if (PartyAsk[k] != PactKind.None)
            return $"{def.Name} hasn't answered your {PactName(PartyAsk[k])} yet";
        if (Tick < _partyAskAfter[k])
            return $"{def.Name} turned you down lately: ask again in {(_partyAskAfter[k] - Tick) / (60 * T) + 1} min";
        if (PartyWants(power, party, kind, asked: true) is { } why && (PartyFeud[k] || PartyPact(power, party) == kind))
            return why; // (no hope: said at once; the rest is the power's answer to give)
        PartyAsk[k] = kind;
        Version++;
        return null;
    }

    /// <summary>The leader takes (or turns down) a power's offer.</summary>
    public string? Answer(byte party, byte power, bool yes)
    {
        var k = PK(party, power);
        var kind = Offer[k];
        if (kind == PactKind.None || Tick >= OfferUntil[k])
            return "that offer is gone";
        Offer[k] = PactKind.None;
        _offerAfter[k] = Tick + (long)Rules.PartyOfferCooldownMinutes * 60 * T;
        Version++;
        if (!yes)
        {
            Note(party, $"You turned down {Name(power)}'s {PactName(kind)}.");
            return null;
        }
        if (PartyWants(power, party, kind, asked: true) is { } why)
            return why;
        FormParty(kind, power, party);
        return null;
    }

    /// <summary>The leader breaks the faction's pact with <paramref name="power"/> (<paramref name="at"/>: where the leader is, for who sees it).</summary>
    public string? Break(byte party, byte power, Vector3 at)
    {
        var i = PartyIndex(power, party);
        if (i < 0)
            return $"you have no pact with {Name(power)}";
        BrokenParty(i, at, "");
        return null;
    }

    /// <summary>A tribute paid (or not) by the leader: the game's, when <see cref="PartyDue"/> comes.</summary>
    public void TributePaid(int i, bool paid)
    {
        if (i < 0 || i >= MaxPartyPacts || PartyK[i] != PactKind.Tribute)
            return;
        if (paid)
        {
            PartyDue[i] = Tick + (long)Rules.TributeEveryMinutes * 60 * T;
            return;
        }
        var (power, party) = (PartyPower[i], PartyOf[i]);
        EndParty(i, "lapses: unpaid");
        MoveParty(power, party, -20);
    }

    // ---- Forming, ending, breaking ------------------------------------------------------------------------------------------------

    private void FormParty(PactKind kind, byte power, byte party)
    {
        var i = PartyIndex(power, party);
        if (i < 0)
            i = Array.IndexOf(PartyK, PactKind.None);
        if (i < 0)
        {
            Console.WriteLine($"[crew] {Name(power)} and {PartyWord(party)} would sign a {PactName(kind)}, but the world holds {MaxPartyPacts} pacts with factions already: left out");
            Note(party, $"No room for another pact with a faction in this world ({MaxPartyPacts}).");
            return;
        }
        (PartyK[i], PartyPower[i], PartyOf[i], PartyStart[i], PartyKept[i]) = (kind, power, party, Tick, 0);
        PartyEnd[i] = kind == PactKind.Ceasefire ? Tick + (long)Rules.CeasefireMinutes * 60 * T : 0;
        PartyDue[i] = kind == PactKind.Tribute ? Tick : 0; // (the first payment at once)
        if (kind is PactKind.Ceasefire or PactKind.Tribute && PartyGet(power, party) < Rules.CeasefireRelation)
            SetParty(power, party, Rules.CeasefireRelation);
        var k = PK(party, power);
        (PartyAsk[k], Offer[k]) = (PactKind.None, PactKind.None);
        Version++;
        Say(kind switch
        {
            PactKind.Alliance => $"{PartyWord(party)} and {Name(power)} are now allies",
            PactKind.Tribute => $"{PartyWord(party)} pays tribute to {Name(power)} for peace",
            _ => $"{PartyWord(party)} and {Name(power)} sign a {PactName(kind)}",
        });
        Note(party, kind == PactKind.Ceasefire ? $"Ceasefire with {Name(power)}: their crews hold fire for {Rules.CeasefireMinutes} min. Attacking them breaks it."
            : $"{Name(power)} signs your {PactName(kind)}.");
    }

    private void EndParty(int i, string why)
    {
        var (kind, power, party) = (PartyK[i], PartyPower[i], PartyOf[i]);
        PartyK[i] = PactKind.None;
        Version++;
        Say($"The {PactName(kind)} between {PartyWord(party)} and {Name(power)} {why}");
        Note(party, $"Your {PactName(kind)} with {Name(power)} {why}.");
    }

    /// <summary>
    /// Faction pact <paramref name="i"/> broken by the faction (<paramref name="how"/>: "" by its leader, else the attack): news, the power takes it hard, and every
    /// power that knows (in a pact with or friendly to the victim, in a pact with the breaker, or with a crew that sees <paramref name="at"/>) thinks less of the
    /// faction; the game tells the faction and its allies.
    /// </summary>
    private void BrokenParty(int i, Vector3 at, string how)
    {
        var (kind, victim, party) = (PartyK[i], PartyPower[i], PartyOf[i]);
        PartyK[i] = PactKind.None;
        Version++;
        Say(how.Length == 0 ? $"{PartyWord(party)} breaks its {PactName(kind)} with {Name(victim)}" : $"{PartyWord(party)} breaks its {PactName(kind)} with {Name(victim)} ({how})");
        MoveParty(victim, party, Amount(RelEvent.BreakPact, Def(victim)!));
        LastHeard = 0;
        for (var c = (byte)1; c <= Count; c++)
            if (c != victim && !Gone[c] && Def(c) is { Controller: true } cd
                && (PactOf(c, victim) != PactKind.None || Get(c, victim) >= Rules.FriendlyLine || PartyPact(c, party) != PactKind.None || Sight?.Sees(c, at) == true))
            {
                MoveParty(c, party, Amount(RelEvent.BreakPactHeard, cd));
                LastHeard |= 1 << (c - 1);
            }
        Note(party, LastHeard == 0 ? $"You broke your {PactName(kind)} with {Name(victim)}. Nobody else heard of it."
            : $"You broke your {PactName(kind)} with {Name(victim)}. Word gets around: {HeardList(LastHeard)} think less of you.");
        PartyBroke?.Invoke(party, victim, kind);
    }

    /// <summary>The powers in a mask, in words (a notice: allocates).</summary>
    public string HeardList(int mask)
    {
        var sb = new System.Text.StringBuilder();
        for (var c = 1; c <= Count; c++)
            if ((mask >> (c - 1) & 1) != 0)
                sb.Append(sb.Length > 0 ? ", " : "").Append(Name((byte)c));
        return sb.ToString();
    }

    // ---- A power's think ------------------------------------------------------------------------------------------------------------

    /// <summary>Power <paramref name="a"/>'s pacts with factions: kept, expired or fallen apart; the proposals put to it answered; its own offers made.</summary>
    private void PartyThink(byte a, NpcPowerDef def)
    {
        for (var i = 0; i < MaxPartyPacts; i++)
        {
            if (PartyK[i] == PactKind.None || PartyPower[i] != a)
                continue;
            var party = PartyOf[i];
            if (!PartyLive(party))
            {
                PartyK[i] = PactKind.None; // (the faction is gone: its pacts with it)
                Version++;
                continue;
            }
            if (PartyEnd[i] != 0 && Tick >= PartyEnd[i])
            {
                EndParty(i, "runs out");
                continue;
            }
            var v = PartyGet(a, party);
            if (PartyK[i] is PactKind.Alliance or PactKind.Trade && v < Rules.NeutralLine - Rules.Hysteresis || PartyK[i] == PactKind.NonAggression && v <= Rules.WarLine)
            {
                EndParty(i, "falls apart");
                continue;
            }
            if ((Tick - PartyStart[i]) / (3600L * T) > PartyKept[i])
            {
                PartyKept[i]++;
                MoveParty(a, party, Amount(RelEvent.KeepPact, def));
            }
        }
        for (var party = (byte)1; party < Parties; party++)
        {
            var k = PK(party, a);
            if (!PartyLive(party))
            {
                if (PartyAsk[k] != PactKind.None || Offer[k] != PactKind.None || PartyFeud[k] || MetFaction[k])
                    (PartyAsk[k], Offer[k], PartyFeud[k], MetFaction[k], _partyLoss[k], _partyWarSince[k]) = (PactKind.None, PactKind.None, false, false, 0, 0); // (an id freed: a new faction there starts clean)
                continue;
            }
            if (PartyAsk[k] is var kind and not PactKind.None)
            {
                PartyAsk[k] = PactKind.None;
                Version++;
                if (PartyWants(a, party, kind, asked: true) is { } why)
                {
                    _partyAskAfter[k] = Tick + (long)Rules.AnswerCooldownMinutes * 60 * T;
                    Note(party, $"{def.Name} turns down your {PactName(kind)}: {why}.");
                    Console.WriteLine($"[crew] {def.Name} turns down {PartyWord(party)}'s {PactName(kind)} ({why})");
                }
                else
                    FormParty(kind, a, party);
                continue;
            }
            if (Offer[k] != PactKind.None)
            {
                if (Tick >= OfferUntil[k])
                {
                    (Offer[k], _offerAfter[k]) = (PactKind.None, Tick + (long)Rules.PartyOfferCooldownMinutes * 60 * T);
                    Version++;
                }
                continue;
            }
            if (!MetFaction[k] || Tick < _offerAfter[k])
                continue;
            var cur = PartyPact(a, party);
            var want = cur == PactKind.None && PartyCeasefireWanted(a, party) ? PactKind.Ceasefire
                : PartyWants(a, party, PactKind.Alliance, false, false) == null ? PactKind.Alliance
                : PartyWants(a, party, PactKind.Trade, false, false) == null ? PactKind.Trade
                : PartyWants(a, party, PactKind.NonAggression, false, false) == null ? PactKind.NonAggression
                : PactKind.None;
            if (want == PactKind.None)
                continue;
            (Offer[k], OfferUntil[k]) = (want, Tick + (long)Rules.PartyOfferMinutes * 60 * T);
            Version++;
            Note(party, $"{def.Name} offers you a {PactName(want)}: your leader can take it in the faction window ({Rules.PartyOfferMinutes} min).");
        }
    }

    // ---- Saves (version 3 of the Powers section) --------------------------------------------------------------------------------------

    /// <summary>u8 k, each (u8 kind, u8 power index, u8 faction id, i32 seconds held, i32 seconds left (-1 none), u8 hours kept, i32 seconds to the next tribute
    /// (-1 none)); u8 f, each feud (u8 faction id, u8 power index). Faction ids are the world's own (its Factions section keeps them).</summary>
    private void WriteParties(SaveWriter w)
    {
        var n = 0;
        foreach (var k in PartyK)
            if (k != PactKind.None)
                n++;
        w.U8((byte)n);
        for (var i = 0; i < MaxPartyPacts; i++)
            if (PartyK[i] != PactKind.None)
                w.U8((byte)PartyK[i]).U8((byte)(PartyPower[i] - 1)).U8(PartyOf[i]).I32((int)((Tick - PartyStart[i]) / T))
                    .I32(PartyEnd[i] == 0 ? -1 : (int)Math.Max(0, (PartyEnd[i] - Tick) / T)).U8((byte)Math.Min(PartyKept[i], 255))
                    .I32(PartyDue[i] == 0 ? -1 : (int)Math.Max(0, (PartyDue[i] - Tick) / T));
        var f = 0;
        foreach (var b in PartyFeud)
            if (b)
                f++;
        w.U8((byte)Math.Min(f, 255));
        var done = 0;
        for (var k = 0; k < PartyFeud.Length && done < 255; k++)
            if (PartyFeud[k] && k % N < Count)
            {
                w.U8((byte)(k / N)).U8((byte)(k % N));
                done++;
            }
    }

    private void ReadParties(SaveReader r, byte[] map)
    {
        int n = r.U8();
        for (var j = 0; j < n; j++)
        {
            var kind = (PactKind)r.U8();
            int a = r.U8();
            var party = r.U8();
            var held = r.I32();
            var left = r.I32();
            var kept = r.U8();
            var due = r.I32();
            if (a >= map.Length || map[a] == 0 || party == 0 || party >= Parties || kind is not (PactKind.Ceasefire or PactKind.NonAggression or PactKind.Alliance or PactKind.Trade or PactKind.Tribute))
                continue;
            var i = Array.IndexOf(PartyK, PactKind.None);
            if (i < 0)
                break;
            (PartyK[i], PartyPower[i], PartyOf[i], PartyStart[i], PartyEnd[i], PartyKept[i], PartyDue[i]) =
                (kind, map[a], party, Tick - (long)held * T, left < 0 ? 0 : Tick + (long)left * T, kept, due < 0 ? 0 : Tick + (long)due * T);
        }
        int fn = r.U8();
        for (var j = 0; j < fn; j++)
        {
            var party = r.U8();
            int a = r.U8();
            if (party > 0 && party < Parties && a < map.Length && map[a] != 0)
                PartyFeud[PK(party, map[a])] = true;
        }
    }
}
