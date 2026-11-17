namespace CallOfSlop;

/// <summary>
/// The NPC faction kinds' standing with the players (docs/sandbox/design/bot-brain.md section 7, phase 3), server side, and the credits each
/// player holds (phase 6). Standing sits in the factions table's NPC rows (<see cref="FactionTable.StoryNames"/>, <see cref="FactionTable.StoryStanding"/>
/// for a player faction, <see cref="FactionTable.PlayerStanding"/> for someone in none), which <see cref="Msg.Factions"/> already sends, so the
/// Factions window shows it; it is filled from Crew/Factions while the story is off (the story owns those rows when it runs). A kind's
/// standing with someone it hasn't met starts at its stance (hostile -50, wary 0, neutral 20); a deed (a hit on its body or its crew) moves it;
/// below 0 its crews fight them. Saved in the world's Crew section with each person's record.
/// </summary>
public sealed partial class Game
{
    private readonly bool[] _npcMetFaction = new bool[FactionTable.Max * FactionTable.MaxStory];
    private readonly bool[] _npcMetPlayer = new bool[256 * FactionTable.MaxStory];
    private readonly long[] _credits = new long[256];

    /// <summary>The NPC rows hold the crews' factions (the story is off and they are read).</summary>
    private bool CrewStandingOn => !StoryOn && CrewFactions.Count > 0;

    /// <summary>Server, each tick: the factions table names the NPC kinds (once, and again after a new world cleared it).</summary>
    private void CrewFactionsTick()
    {
        if (!CrewStandingOn || FactionTable.StoryNames.Length == CrewFactions.Count)
            return;
        var names = new string[CrewFactions.Count];
        for (var k = 0; k < names.Length; k++)
            names[k] = CrewFactions.All[k + 1]!.Name;
        FactionTable.StoryNames = names; // (a new world: once)
        FactionTable.Version++;
    }

    /// <summary>How NPC faction kind <paramref name="faction"/> sees <paramref name="player"/> (-100 to 100): their player faction's standing, else their own.</summary>
    public int CrewStanding(byte faction, byte player)
    {
        if (CrewFactions.Of(faction) is not { } def)
            return 0;
        if (!CrewStandingOn)
            return def.StartStanding;
        ref var v = ref StandingRef(faction, player, def);
        return v;
    }

    private ref sbyte StandingRef(byte faction, byte player, NpcFactionDef def)
    {
        var t = FactionTable;
        var f = faction - 1;
        var party = t.Of[player];
        if (party != 0)
        {
            var k = party * FactionTable.MaxStory + f;
            if (!_npcMetFaction[k])
            {
                _npcMetFaction[k] = true;
                t.StoryStanding[k] = (sbyte)def.StartStanding;
                t.Version++;
            }
            return ref t.StoryStanding[k];
        }
        var j = player * FactionTable.MaxStory + f;
        if (!_npcMetPlayer[j])
        {
            _npcMetPlayer[j] = true;
            t.PlayerStanding[j] = (sbyte)def.StartStanding;
            t.PlayerStory[player] = true;
            t.Version++;
        }
        return ref t.PlayerStanding[j];
    }

    void ICrewHost.Deed(byte faction, byte player, int amount)
    {
        if (!CrewStandingOn || CrewFactions.Of(faction) is not { } def || !Players.ContainsKey(player))
            return;
        ref var v = ref StandingRef(faction, player, def);
        var was = v;
        v = (sbyte)Math.Clamp(v + amount, -100, 100);
        if (v == was)
            return;
        FactionTable.Version++;
        if (was >= 0 && v < 0)
            Console.WriteLine($"[crew] {def.Name} now fight player {player} (standing {v})");
        // Its enemies think better of whoever hurts it: a little, for a hit.
        if (amount < 0)
            for (var k = 1; k <= CrewFactions.Count; k++)
                if (k != faction && CrewFactions.All[k]!.ToFactions[faction] == CrewStance.Hostile)
                {
                    ref var e = ref StandingRef((byte)k, player, CrewFactions.All[k]!);
                    e = (sbyte)Math.Clamp(e - amount / 5, -100, 100);
                }
    }

    /// <summary>A person hit a crew member's body: their foe from now, and a deed against its faction.</summary>
    private void CrewBodyHit(Player victim, Player by)
    {
        var m = victim.CrewSlot;
        if (m < 0 || m >= Crew.High || Crew.Ship[m] < 0 || Crew.Ships[Crew.Ship[m]] is not { } s)
            return;
        if (!s.Foe(by.Id))
        {
            s.MarkFoe(by.Id);
            Console.WriteLine($"[crew] construct {s.Body}: player {by.Id} shot its crew");
        }
        if (s.Faction != 0)
            ((ICrewHost)this).Deed(s.Faction, by.Id, -3);
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
                return $"construct {c.Id} ({CrewFactions.Of(s.Faction)?.Name ?? "no faction"}) hit for {dealt:0.0}";
            }
        return "no crewed body";
    }

    /// <summary>A tool's line (allocates): each kind's standing with <paramref name="p"/> as this peer's table has it.</summary>
    public string CrewStandingLine(Player p)
    {
        var t = FactionTable;
        var party = t.Of[p.Id];
        return string.Join(", ", t.StoryNames.Select((n, f) => $"{n} {(party != 0 ? t.StoryStanding[party * FactionTable.MaxStory + f] : t.PlayerStanding[p.Id * FactionTable.MaxStory + f])}"));
    }

    // ---- Credits (phase 6) --------------------------------------------------------------------------------------------------------

    /// <summary>A player's credits (Creative: unlimited).</summary>
    public long CreditsOf(byte player) => SandboxInfiniteParts ? long.MaxValue : _credits[player];

    // ---- Saves (the Crew section: WorldSaveWriter.CrewTag) -----------------------------------------------------------------------------

    /// <summary>A person's record from them: their credits, and their own standings (by kind name) when they are in no player faction.</summary>
    private void CrewCatch(Player p, WorldPlayer rec)
    {
        rec.Credits = _credits[p.Id];
        var list = new List<(string, sbyte)>();
        if (CrewStandingOn)
            for (var f = 0; f < CrewFactions.Count; f++)
                if (_npcMetPlayer[p.Id * FactionTable.MaxStory + f])
                    list.Add((CrewFactions.All[f + 1]!.Name, FactionTable.PlayerStanding[p.Id * FactionTable.MaxStory + f]));
        rec.NpcStanding = list.ToArray();
    }

    /// <summary>A person arrived: their credits and standings from their record (an id used before starts clean).</summary>
    private void CrewArrive(Player p, WorldPlayer rec)
    {
        _credits[p.Id] = rec.Credits;
        for (var f = 0; f < FactionTable.MaxStory; f++)
            _npcMetPlayer[p.Id * FactionTable.MaxStory + f] = false;
        if (!CrewStandingOn)
            return;
        foreach (var (name, v) in rec.NpcStanding)
            if (CrewFactions.Find(name) is var k and > 0)
            {
                var j = p.Id * FactionTable.MaxStory + k - 1;
                _npcMetPlayer[j] = true;
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
        for (var k = 0; k < _npcMetFaction.Length; k++)
            if (_npcMetFaction[k])
                n++;
        if (n == 0)
            return null;
        w.U8((byte)Math.Min(n, 255));
        var done = 0;
        for (var k = 0; k < _npcMetFaction.Length && done < 255; k++)
            if (_npcMetFaction[k] && CrewFactions.Of((byte)(k % FactionTable.MaxStory + 1)) is { } def)
            {
                w.U8((byte)(k / FactionTable.MaxStory)).Str(def.Name).U8((byte)FactionTable.StoryStanding[k]);
                done++;
            }
        return w.Span.ToArray();
    }

    /// <summary>A saved world's Crew section: each person's credits and standings onto their record, the factions' standings into the table.</summary>
    private void WorldReadCrew(SaveReader r, WorldSession ws)
    {
        var v = r.U8();
        if (v != 1)
        {
            Console.WriteLine($"[crew] the world's crew section is of version {v} (this build reads 1): left off");
            return;
        }
        var n = r.Count(25);
        for (var k = 0; k < n; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var credits = r.I64();
            int sn = r.U8();
            var list = new (string, sbyte)[sn];
            for (var j = 0; j < sn; j++)
                list[j] = (r.Str(), (sbyte)r.U8());
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam)
                {
                    rec.Credits = credits;
                    rec.NpcStanding = list;
                    break;
                }
        }
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
            if (id < FactionTable.Max && CrewFactions.Find(name) is var f and > 0)
            {
                _npcMetFaction[id * FactionTable.MaxStory + f - 1] = true;
                FactionTable.StoryStanding[id * FactionTable.MaxStory + f - 1] = st;
            }
        }
        FactionTable.Version++;
    }

    /// <summary>A new world: nobody has met anyone.</summary>
    private void CrewFactionsClear()
    {
        Array.Clear(_npcMetFaction);
        Array.Clear(_npcMetPlayer);
        Array.Clear(_credits);
    }
}
