using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The NPC powers' standing with the players (docs/sandbox/design/bot-brain.md section 7, phase 3) and with each other
/// (docs/sandbox/design/faction-controllers.md section 5, phase 1: Crew/CrewRelations.cs), server side, and the credits each player holds
/// (phase 6). Standing with players sits in the factions table's NPC rows (<see cref="FactionTable.StoryNames"/>, <see cref="FactionTable.StoryStanding"/>
/// for a player faction, <see cref="FactionTable.PlayerStanding"/> for someone in none), the powers' grid and pacts beside them, which
/// <see cref="Msg.Factions"/> sends, so the Factions window shows it; it is filled from Crew/Powers (else one power a kind) while the story is
/// off (the story owns those rows when it runs). A power's standing with someone it hasn't met starts at its stance (hostile -50, wary 0,
/// neutral 20); deeds move it (Crew/Relations.json); below 0 its crews fight them. Saved in the world's Crew section with each person's
/// record, the grid in the Powers section.
/// </summary>
public sealed partial class Game : IPowerSight
{
    private readonly long[] _credits = new long[256];
    private readonly PowerRelations _rel = new();
    private int _relShown = -1, _newsSent;

    /// <summary>The relations engine, over this place's factions table.</summary>
    internal PowerRelations Relations
    {
        get
        {
            _rel.Table = FactionTable;
            _rel.Sight ??= this;
            _rel.PartyName ??= PartyNameOf;
            return _rel;
        }
    }

    /// <summary>The NPC rows hold the crews' powers (the story is off and they are read).</summary>
    private bool CrewStandingOn => !StoryOn && CrewPowers.Count > 0;

    /// <summary>Server, each tick: the factions table names the powers (once, and again after a new world cleared it); the powers think; clients get the grid, pacts and news.</summary>
    private void CrewFactionsTick()
    {
        if (!CrewStandingOn)
            return;
        var t = FactionTable;
        if (t.StoryNames.Length != CrewPowers.Count)
        {
            var names = new string[CrewPowers.Count];
            for (var k = 0; k < names.Length; k++)
                names[k] = CrewPowers.All[k + 1]!.Name;
            t.StoryNames = names; // (a new world: once)
            t.Version++;
        }
        var rel = Relations;
        rel.Step();
        if (_relShown != rel.Version || t.PowerCount != CrewPowers.Count)
        {
            _relShown = rel.Version;
            ShowRelations(t, rel);
        }
        PoliticsTick(rel); // (phase 5: pacts with player factions, contracts: GameCrewPolitics.cs)
        if (_newsSent != rel.NewsTotal)
            SendPowerNews(rel);
    }

    /// <summary>The engine's grid and pacts into the factions table (what clients are sent and the window reads).</summary>
    private static void ShowRelations(FactionTable t, PowerRelations rel)
    {
        var n = CrewPowers.Count;
        t.PowerCount = n;
        for (var j = 0; j < t.PowerRel.Length; j++)
            t.PowerRel[j] = rel.Shown(j); // (-100 only for a blood feud: phase 3)
        var k = 0;
        for (var i = 0; i < PowerRelations.MaxPacts && k < FactionTable.MaxPacts; i++)
            if (rel.PactK[i] != PactKind.None)
            {
                t.PactKindOf[k] = (byte)rel.PactK[i];
                t.PactA[k] = (byte)(rel.PactA[i] - 1);
                t.PactB[k] = (byte)(rel.PactB[i] - 1);
                t.PactHeld[k] = (ushort)Math.Min((rel.Tick - rel.PactStart[i]) / (60 * World.TickRate), 0xFFFE);
                t.PactLeft[k] = rel.PactEnd[i] == 0 ? (ushort)0xFFFF : (ushort)Math.Min(Math.Max(0, rel.PactEnd[i] - rel.Tick) / (60 * World.TickRate) + 1, 0xFFFE);
                k++;
            }
        t.PactCount = k;
        t.PowerGone = 0;
        for (var a = 1; a <= n && a <= 16; a++)
            if (rel.Gone[a])
                t.PowerGone |= (ushort)(1 << (a - 1));
        // Phase 5: pacts with player factions, offers and proposals, blood feuds (CrewPartyPacts.cs).
        k = 0;
        for (var i = 0; i < PowerRelations.MaxPartyPacts && k < FactionTable.MaxPacts; i++)
            if (rel.PartyK[i] != PactKind.None)
            {
                (t.PartyPactKind[k], t.PartyPactPower[k], t.PartyPactParty[k]) = ((byte)rel.PartyK[i], (byte)(rel.PartyPower[i] - 1), rel.PartyOf[i]);
                t.PartyPactHeld[k] = (ushort)Math.Min((rel.Tick - rel.PartyStart[i]) / (60 * World.TickRate), 0xFFFE);
                t.PartyPactLeft[k] = rel.PartyEnd[i] == 0 ? (ushort)0xFFFF : (ushort)Math.Min(Math.Max(0, rel.PartyEnd[i] - rel.Tick) / (60 * World.TickRate) + 1, 0xFFFE);
                k++;
            }
        t.PartyPactCount = k;
        t.OfferCount = t.AskCount = 0;
        for (var j = 0; j < rel.Offer.Length; j++)
        {
            if (rel.Offer[j] != PactKind.None && rel.Tick < rel.OfferUntil[j] && t.OfferCount < FactionTable.MaxPartyAsks)
                (t.OfferPower[t.OfferCount], t.OfferParty[t.OfferCount], t.OfferKind[t.OfferCount++]) = ((byte)(j % PowerRelations.N), (byte)(j / PowerRelations.N), (byte)rel.Offer[j]);
            if (rel.PartyAsk[j] != PactKind.None && t.AskCount < FactionTable.MaxPartyAsks)
                (t.AskPower[t.AskCount], t.AskParty[t.AskCount], t.AskKind[t.AskCount++]) = ((byte)(j % PowerRelations.N), (byte)(j / PowerRelations.N), (byte)rel.PartyAsk[j]);
        }
        Array.Copy(rel.PartyFeud, t.PartyFeud, t.PartyFeud.Length);
        t.Version++;
    }

    /// <summary>A news line's name for a person (an owner byte) or, past 255, a player faction (0x100 + id).</summary>
    private string PartyNameOf(byte who)
    {
        var t = FactionTable;
        var party = t.Of[who];
        if (party != 0)
            return t.Names[party];
        return Players.TryGetValue(who, out var p) ? p.Name : "";
    }

    /// <summary>A crew of power <paramref name="power"/>'s awake within its kind's sight of <paramref name="at"/> (word gets around: section 5.3).</summary>
    bool IPowerSight.Sees(byte power, System.Numerics.Vector3 at)
    {
        var sight = CrewPowers.KindOf(power)?.Sight ?? 2500;
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { Tier: not CrewTier.Asleep, MemberCount: > 0 } s && s.Faction == power && Constructs.Get(s.Body) is { } c
                && System.Numerics.Vector3.DistanceSquared(c.WorldCom, at) < sight * sight)
                return true;
        return false;
    }

    /// <summary>How power <paramref name="faction"/> sees <paramref name="player"/> (-100 to 100): their player faction's standing, else their own.</summary>
    public int CrewStanding(byte faction, byte player)
    {
        if (CrewPowers.Of(faction) is not { } def)
            return 0;
        if (!CrewStandingOn)
            return def.StartPlayers;
        return Relations.Standing(faction, player);
    }

    /// <summary>
    /// Who did it: a person, or, for a crew member's own hands or a crewed body's guns, the power they serve (<paramref name="power"/>, with its
    /// ship). A body a player owns, or a prize, is its owner's doing whatever crew works it (a player's "Fights everyone" ship, a taken
    /// ship's prisoners): their standing moves, as <c>DamageBy = c.Owner</c> always said. False: nobody to hold to account.
    /// </summary>
    private bool CrewDoer(byte byPlayer, ushort byBody, out byte power, out byte player, out CrewShip? ship)
    {
        (power, player, ship) = (0, 255, null);
        if (byBody == 0 && byPlayer != 255 && Players.TryGetValue(byPlayer, out var bot) && bot.CrewSlot >= 0)
        {
            // A crew member's own hands: its ship's doing.
            if (bot.CrewSlot < Crew.High && Crew.Ship[bot.CrewSlot] >= 0 && Crew.Ships[Crew.Ship[bot.CrewSlot]] is { } ms)
                byBody = ms.Body;
            byPlayer = 255;
        }
        if (byBody != 0 && Constructs.Get(byBody) is { } bc)
        {
            if (bc.Owner != 255)
                byPlayer = bc.Owner; // (a player's body: theirs)
            else if (Crew.ShipOf(byBody) is { Faction: not 0, Own: false, Prize: false } bs)
                (power, ship) = (bs.Faction, bs);
        }
        if (power == 0 && byPlayer != 255 && Players.TryGetValue(byPlayer, out var p) && p.CrewSlot < 0 && p.Bot == null)
            player = byPlayer;
        return power != 0 || player != 255;
    }

    /// <summary>
    /// Something was done to crew <paramref name="s"/> by a person or another crew (CrewWorld's captain, a kill, a lost ship): the relations
    /// move (Crew/Relations.json), the crew remembers who for a kill to come, a crew that shot it is answered, and whoever hurt someone fighting a
    /// friend of theirs is thanked by that friend.
    /// </summary>
    void ICrewHost.Deed(RelEvent e, CrewShip s, byte byPlayer, ushort byBody)
    {
        if (!CrewStandingOn || s.Faction == 0 || !CrewDoer(byPlayer, byBody, out var power, out var player, out var ship) || power == s.Faction)
            return;
        var at = Constructs.Get(s.Body)?.WorldCom ?? default;
        var rel = Relations;
        if (power != 0 && e <= RelEvent.Capture && rel.StrayFire(s.Faction, power, out var first))
        {
            // A friend's or a pact partner's stray round or splash: no deed, no broken pact, no fire back (deliberate fire, past
            // RelationRules.FriendlyFireHits hits in a minute, counts).
            if (first)
                Console.WriteLine($"[crew] construct {s.Body} ({CrewPowers.Of(s.Faction)!.Name}) hit by {CrewPowers.Of(power)!.Name}'s fire: stray, forgiven");
            return;
        }
        if (e <= RelEvent.Capture)
        {
            (s.DoerPlayer, s.DoerBody, s.DoerAt) = (byPlayer, byBody, Crew.Tick);
            if (ship != null)
                (s.FoeBody, s.FoeBodyAt) = (ship.Body, Crew.Tick); // (it answers fire, whatever its view: GameCrew.cs)
        }
        rel.Post(e, s.Faction, power, player, at);
        if (e is RelEvent.HitBody or RelEvent.HitCrew or RelEvent.KillCrew or RelEvent.DestroyShip && s.ContactBody != 0
            && Crew.ShipOf(s.ContactBody) is { Faction: not 0, Own: false } friend && friend.Faction != s.Faction)
            rel.Help(friend.Faction, power, player, at); // (s was fighting friend: whoever hurt s helped them)
    }

    /// <summary>Person <paramref name="player"/> did <paramref name="e"/> to power <paramref name="faction"/> (boarding, capture, prisoners, hiring).</summary>
    private void PowerDeed(RelEvent e, byte faction, byte player, System.Numerics.Vector3 at)
    {
        if (!CrewStandingOn || CrewPowers.Of(faction) is null || !Players.TryGetValue(player, out var p) || p.Bot != null || p.CrewSlot >= 0)
            return;
        Relations.Post(e, faction, 0, player, at);
    }

    /// <summary>A person or a crew member hit a crew member's body: a person is its foe from now; a deed against its power.</summary>
    private void CrewBodyHit(Player victim, Player by)
    {
        var m = victim.CrewSlot;
        if (m < 0 || m >= Crew.High || Crew.Ship[m] < 0 || Crew.Ships[Crew.Ship[m]] is not { } s)
            return;
        if (by.CrewSlot < 0 && !s.Foe(by.Id))
        {
            s.MarkFoe(by.Id);
            Console.WriteLine($"[crew] construct {s.Body}: player {by.Id} shot its crew");
        }
        if (s.Faction != 0)
            ((ICrewHost)this).Deed(RelEvent.HitCrew, s, by.Id, 0);
    }

    /// <summary>A tool: <paramref name="by"/>'s hit on a part of the first crewed body (allocates).</summary>
    public string CrewHitFirst(Player by)
    {
        for (var i = 0; i < Crew.ShipHigh; i++)
            if (Crew.Ships[i] is { MemberCount: > 0 } s && Constructs.Get(s.Body) is { } c && c.PartCount > 0)
            {
                Constructs.DamageBy = by.Id;
                Constructs.DamagePart(c, 0, 1, out var dealt, raw: true);
                Constructs.DamageBy = 255;
                return $"construct {c.Id} ({CrewPowers.Of(s.Faction)?.Name ?? "no faction"}) hit for {dealt:0.0}";
            }
        return "no crewed body";
    }

    /// <summary>A tool's line (allocates): each power's standing with <paramref name="p"/> as this peer's table has it.</summary>
    public string CrewStandingLine(Player p)
    {
        var t = FactionTable;
        var party = t.Of[p.Id];
        return string.Join(", ", t.StoryNames.Select((n, f) => $"{n} {(party != 0 ? t.StoryStanding[party * FactionTable.MaxStory + f] : t.PlayerStanding[p.Id * FactionTable.MaxStory + f])}"));
    }

    /// <summary>Server: news lines not yet sent, to everyone (<see cref="Msg.PowerNews"/>).</summary>
    private void SendPowerNews(PowerRelations rel)
    {
        var from = Math.Max(_newsSent, rel.NewsTotal - PowerRelations.MaxNews);
        _newsSent = rel.NewsTotal;
        PowerNewsAdd(rel, from);
        if (Net.IsOffline)
            return;
        WritePowerNews(SbW, rel, from);
        Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>The window's news (this peer's: the host's from its engine, a client's as sent), oldest first.</summary>
    public readonly List<string> PowerNews = [];

    private void PowerNewsAdd(PowerRelations rel, int from)
    {
        for (var k = from; k < rel.NewsTotal; k++)
            if (rel.NewsLine(k) is { } line)
                PowerNewsPush(line);
    }

    private void PowerNewsPush(string line)
    {
        if (PowerNews.Count >= PowerRelations.MaxNews)
            PowerNews.RemoveAt(0);
        PowerNews.Add(line);
    }

    /// <summary><see cref="Msg.PowerNews"/>: u8 n, n x text (the lines posted from <paramref name="from"/> on, oldest first).</summary>
    private static void WritePowerNews(PacketWriter w, PowerRelations rel, int from)
    {
        w.Begin(Msg.PowerNews);
        var n = 0;
        for (var k = from; k < rel.NewsTotal; k++)
            if (rel.NewsLine(k) != null)
                n++;
        w.U8((byte)n);
        for (var k = from; k < rel.NewsTotal; k++)
            if (rel.NewsLine(k) is { } line)
                w.Str(line);
    }

    /// <summary>Server: the news held, to one joiner.</summary>
    private void SendPowerNewsTo(RakNetGUID to)
    {
        if (!CrewStandingOn || _rel.NewsTotal == 0)
            return;
        WritePowerNews(SbW, _rel, _rel.NewsTotal - Math.Min(_rel.NewsTotal, PowerRelations.MaxNews));
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: news lines (after the id).</summary>
    internal void ReadPowerNews(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        int n = r.U8();
        for (var k = 0; k < n && r.Has(1); k++)
            PowerNewsPush(r.Text(200));
    }

    // ---- Credits (phase 6) --------------------------------------------------------------------------------------------------------

    /// <summary>A player's credits (Creative: unlimited).</summary>
    public long CreditsOf(byte player) => SandboxInfiniteParts ? long.MaxValue : _credits[player];

    // ---- Saves (the Crew section: WorldSaveWriter.CrewTag) -----------------------------------------------------------------------------

    /// <summary>A person's record from them: their credits, and their own standings (by kind name) when they are in no player faction.</summary>
    private void CrewCatch(Player p, WorldPlayer rec)
    {
        rec.Credits = _credits[p.Id];
        rec.CreditsStarted = _creditsStarted[p.Id];
        var list = new List<(string, sbyte)>();
        if (CrewStandingOn)
            for (var f = 0; f < CrewPowers.Count; f++)
                if (_rel.MetPlayer[p.Id * FactionTable.MaxStory + f])
                    list.Add((CrewPowers.All[f + 1]!.Name, FactionTable.PlayerStanding[p.Id * FactionTable.MaxStory + f]));
        rec.NpcStanding = list.ToArray();
    }

    /// <summary>A person arrived: their credits and standings from their record (an id used before starts clean).</summary>
    private void CrewArrive(Player p, WorldPlayer rec)
    {
        _credits[p.Id] = rec.Credits;
        _creditsDirty[p.Id] = true;
        CreditsFirst(p, rec);
        for (var f = 0; f < FactionTable.MaxStory; f++)
            _rel.MetPlayer[p.Id * FactionTable.MaxStory + f] = false;
        if (!CrewStandingOn)
            return;
        foreach (var (name, v) in rec.NpcStanding)
            if (CrewPowers.Find(name) is var k and > 0)
            {
                var j = p.Id * FactionTable.MaxStory + k - 1;
                _rel.MetPlayer[j] = true;
                FactionTable.PlayerStanding[j] = v;
                FactionTable.PlayerStory[p.Id] = true;
            }
        FactionTable.Version++;
    }

    /// <summary>The player factions' standings for a save: u8 n, each (u8 faction id, text kind, i8 standing). Null: none met.</summary>
    private byte[]? CrewFactionBytes()
    {
        if (!CrewStandingOn)
            return null;
        var w = new SaveWriter(256);
        var n = 0;
        for (var k = 0; k < _rel.MetFaction.Length; k++)
            if (_rel.MetFaction[k])
                n++;
        if (n == 0)
            return null;
        w.U8((byte)Math.Min(n, 255));
        var done = 0;
        for (var k = 0; k < _rel.MetFaction.Length && done < 255; k++)
            if (_rel.MetFaction[k] && CrewPowers.Of((byte)(k % FactionTable.MaxStory + 1)) is { } def)
            {
                w.U8((byte)(k / FactionTable.MaxStory)).Str(def.Name).U8((byte)FactionTable.StoryStanding[k]);
                done++;
            }
        return w.Span.ToArray();
    }

    /// <summary>A saved world's Crew section: each person's credits and standings onto their record, the factions' standings into the table.</summary>
    private void WorldReadCrew(SaveReader r, WorldSession ws)
    {
        if (!WorldReadCrewRecords(r, ws.Players))
            return;
        var len = r.I32();
        if (len <= 0)
            return;
        var fr = new SaveReader(r.Bytes(len));
        int fn = fr.U8();
        for (var k = 0; k < fn; k++)
        {
            var id = fr.U8();
            var name = fr.Str();
            var st = (sbyte)fr.U8();
            if (id < FactionTable.Max && CrewPowers.Find(name) is var f and > 0)
            {
                _rel.MetFaction[id * FactionTable.MaxStory + f - 1] = true;
                FactionTable.StoryStanding[id * FactionTable.MaxStory + f - 1] = st;
            }
        }
        FactionTable.Version++;
    }

    /// <summary>The Crew section's version and its people: each one's credits and standings onto the matching record of <paramref name="recs"/>. False: a version this build doesn't read (left off).</summary>
    private static bool WorldReadCrewRecords(SaveReader r, IReadOnlyList<WorldPlayer> recs)
    {
        var v = r.U8();
        if (v != 1)
        {
            Console.WriteLine($"[crew] the world's crew section is of version {v} (this build reads 1): left off");
            return false;
        }
        var n = r.Count(26);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var credits = r.I64();
            var started = r.U8() != 0;
            int sn = r.U8();
            var list = new (string, sbyte)[sn];
            for (var j = 0; j < sn; j++)
                list[j] = (r.Str(), (sbyte)r.U8());
            foreach (var rec in recs)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.Credits = credits;
                    rec.CreditsStarted = started;
                    rec.NpcStanding = list;
                    break;
                }
        }
        return true;
    }

    /// <summary>The powers' grid, pacts and news for a save (null: no powers, or the story owns the rows).</summary>
    private byte[]? PowersBytes() => CrewStandingOn ? Relations.SaveBytes() : null;

    /// <summary>A saved world's Powers section (WorldSaveWriter.PowersTag): the grid, pacts and news over the power files'.</summary>
    private void WorldReadPowers(SaveReader r)
    {
        var bytes = r.Bytes(r.I32());
        if (!CrewStandingOn)
            return;
        var rel = Relations;
        rel.Load(bytes);
        _relShown = -1;
        _newsSent = rel.NewsTotal;
        _notesRead = rel.NoteTotal;
        _contractsSent = -1;
        PowerNews.Clear();
        PowerNewsAdd(rel, Math.Max(0, rel.NewsTotal - PowerRelations.MaxNews));
    }

    /// <summary>A new world: nobody has met anyone.</summary>
    private void CrewFactionsClear()
    {
        _ = CrewFactions.All; // (the kinds' and powers' files and the names read here, at a world's start, never inside a server tick)
        _ = CrewPowers.All;
        _ = CrewNames.All;
        Crew.Fleet.Rules = FleetRules.Current; // (Crew/Fleet.json read here too)
        _questioning.Clear(); // (officer prisoners are the Fleet section's: CrewFleet.Officers)
        IntelMarks.Clear();
        Relations.Reset(); // (every relation from the power files, nobody met: a saved world's follow from its Powers and Crew sections)
        CrewPowersClear(); // (the controllers from their plans: GameCrewPowers.cs; a saved world's follow from its Controllers section)
        _relShown = -1;
        _newsSent = _rel.NewsTotal;
        PoliticsClear();
        PowerNews.Clear();
        Array.Clear(_credits);
        Array.Clear(_creditsStarted);
        Array.Clear(_creditsSeen);
    }
}
