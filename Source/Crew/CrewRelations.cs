using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Bands a relation falls in (faction-controllers.md section 5.2). Crews fight below 0 whatever the band.</summary>
public enum RelBand : byte
{
    War,
    Hostile,
    Wary,
    Neutral,
    Friendly,
}

/// <summary>What moves a relation (Crew/Relations.json, section 5.3), in that file's key order.</summary>
public enum RelEvent : byte
{
    HitBody,
    HitCrew,
    KillCrew,
    DestroyShip,
    Board,
    Capture,
    Release,
    Drop,
    Hire,
    Help,
    BreakPact,
    BreakPactHeard,
    KeepPact,
    Count,
}

public enum PactKind : byte
{
    None,
    Ceasefire,
    NonAggression,
    Alliance,
}

/// <summary>Who sees what happens at a place: a crew of a power's awake within its sight (the server's crews; a tool's stand-in).</summary>
public interface IPowerSight
{
    bool Sees(byte power, Vector3 at);
}

/// <summary>The numbers relations move by (Crew/Relations.json, "call-of-slop-npc-relations/1"; built-in values when the file is missing).</summary>
public sealed class RelationRules
{
    public const string Format = "call-of-slop-npc-relations/1";
    public static readonly string[] Keys = ["hitBody", "hitCrew", "killCrew", "destroyShip", "board", "capture", "release", "drop", "hire", "help", "breakPact", "breakPactHeard", "keepPact"];

    public readonly int[] Amount = [-5, -3, -4, -15, -10, -20, 5, 3, 2, 2, -40, -15, 1];
    public readonly string[][] Scale = [["pride"], ["pride"], ["values.crew"], ["values.ships", "pride"], ["pride"], ["values.ships", "pride"], ["forgiveness"], ["forgiveness"], [], ["loyalty"], [], ["loyalty"], []];
    public int WarLine = -50, WaryLine = 0, NeutralLine = 20, FriendlyLine = 60, Hysteresis = 5;
    public float DecayPerMinute = 1f, EnemyShare = 0.2f;
    public int HelpMax = 10, HelpEverySeconds = 60, FightGapSeconds = 300;
    public int CeasefireMinutes = 30, CeasefireRelation = -40, CeasefireLosses = 10, StaleWarMinutes = 30, AnswerCooldownMinutes = 5;
    public float AllianceLoyalty = 0.5f, CeasefireRefuseAggression = 0.8f;

    private static RelationRules? _current;
    public static RelationRules Current => _current ??= Load(Path.Combine(AppFolders.Data, "Crew", "Relations.json"));

    public static RelationRules Load(string path)
    {
        var r = new RelationRules();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[crew] no {Path.GetFileName(path)}: relations move by the built-in numbers");
            return r;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new InvalidDataException($"its format isn't \"{Format}\"");
            if (root.TryGetProperty("events", out var ev))
                foreach (var kv in ev.EnumerateObject())
                {
                    var k = Array.IndexOf(Keys, kv.Name);
                    if (k < 0)
                    {
                        Console.WriteLine($"[crew] {path}: event \"{kv.Name}\" is no event this build knows: left out");
                        continue;
                    }
                    r.Amount[k] = Math.Clamp(kv.Value.GetProperty("amount").GetInt32(), -100, 100);
                    if (kv.Value.TryGetProperty("scale", out var sc))
                    {
                        var names = sc.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                        foreach (var n in names)
                            if (new NpcPowerDef().Param(n) < 0)
                                throw new InvalidDataException($"event \"{kv.Name}\" is scaled by \"{n}\", which is no parameter");
                        r.Scale[k] = names;
                    }
                }
            if (root.TryGetProperty("bands", out var b))
            {
                r.WarLine = Int(b, "war", r.WarLine, -100, -1);
                r.WaryLine = 0; // (crews fight below 0: the line between Hostile and Wary stays there)
                r.NeutralLine = Int(b, "neutral", r.NeutralLine, 1, 99);
                r.FriendlyLine = Int(b, "friendly", r.FriendlyLine, r.NeutralLine + 1, 100);
                r.Hysteresis = Int(b, "hysteresis", r.Hysteresis, 0, 20);
            }
            if (root.TryGetProperty("decay", out var d))
                r.DecayPerMinute = Num(d, "perMinute", r.DecayPerMinute, 0, 20);
            if (root.TryGetProperty("witness", out var w))
                r.EnemyShare = Num(w, "enemyShare", r.EnemyShare, 0, 1);
            if (root.TryGetProperty("help", out var h))
            {
                r.HelpMax = Int(h, "most", r.HelpMax, 0, 100);
                r.HelpEverySeconds = Int(h, "everySeconds", r.HelpEverySeconds, 1, 3600);
                r.FightGapSeconds = Int(h, "fightGapSeconds", r.FightGapSeconds, 10, 3600);
            }
            if (root.TryGetProperty("pacts", out var p))
            {
                r.CeasefireMinutes = Int(p, "ceasefireMinutes", r.CeasefireMinutes, 1, 1440);
                r.CeasefireRelation = Int(p, "ceasefireRelation", r.CeasefireRelation, -100, 100);
                r.CeasefireLosses = Int(p, "ceasefireLosses", r.CeasefireLosses, 1, 1000);
                r.StaleWarMinutes = Int(p, "staleWarMinutes", r.StaleWarMinutes, 1, 1440);
                r.AnswerCooldownMinutes = Int(p, "answerCooldownMinutes", r.AnswerCooldownMinutes, 0, 1440);
                r.AllianceLoyalty = Num(p, "allianceLoyalty", r.AllianceLoyalty, 0, 1);
                r.CeasefireRefuseAggression = Num(p, "ceasefireRefuseAggression", r.CeasefireRefuseAggression, 0, 1.01f);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            Console.WriteLine($"[crew] REFUSED {path}: {ex.Message}: relations move by the built-in numbers");
            return new RelationRules();
        }
        return r;
    }

    private static int Int(JsonElement e, string key, int def, int min, int max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        var x = v.GetInt32();
        return x >= min && x <= max ? x : throw new InvalidDataException($"\"{key}\" {x} is outside {min} to {max}");
    }

    private static float Num(JsonElement e, string key, float def, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        var x = v.GetSingle();
        return float.IsFinite(x) && x >= min && x <= max ? x : throw new InvalidDataException($"\"{key}\" {x} is outside {min} to {max}");
    }

    /// <summary>The raw band of value <paramref name="v"/>.</summary>
    public RelBand Raw(int v) => v <= WarLine ? RelBand.War : v < WaryLine ? RelBand.Hostile : v < NeutralLine ? RelBand.Wary : v < FriendlyLine ? RelBand.Neutral : RelBand.Friendly;

    private int Lower(RelBand b) => b switch { RelBand.War => -100, RelBand.Hostile => WarLine + 1, RelBand.Wary => WaryLine, RelBand.Neutral => NeutralLine, _ => FriendlyLine };

    /// <summary>The band after <paramref name="prev"/> for value <paramref name="v"/>, with <see cref="Hysteresis"/> points each way.</summary>
    public RelBand Next(RelBand prev, int v)
    {
        var raw = Raw(v);
        if (raw < prev && v > Lower(prev) - Hysteresis)
            return prev;
        if (raw > prev && v < Lower(prev + 1) + Hysteresis)
            return prev;
        return raw;
    }
}

/// <summary>
/// Relations that change through play (docs/sandbox/design/faction-controllers.md section 5, phase 1), server side: the power x power grid
/// (how A sees B, not symmetric), the powers' standing with player factions and lone players (in the factions table's NPC rows, as before),
/// bands with hysteresis, events and the third parties who know of them, decay towards rest values on real (simulated) time, and pacts
/// (ceasefire, non-aggression, alliance) that form and break by simple rules at each power's think (one power at a time, each every 5 s).
/// Fixed arrays: nothing allocated in a tick but a news line (an event, rare).
/// </summary>
public sealed class PowerRelations
{
    public const int N = FactionTable.MaxStory, MaxPacts = 32, MaxNews = 32;
    public const int ThinkSeconds = 5;
    private static int T => World.TickRate;

    public RelationRules Rules = RelationRules.Current;
    public FactionTable Table = new(); // the factions table whose NPC rows hold the standings with players (the Game sets its own)
    public IPowerSight? Sight;
    public Func<byte, string>? PartyName; // a player's or player faction's name for news (owner byte; a faction: 0x100 + id)
    public long Tick;
    public int Version; // bumped on any change clients see

    public readonly sbyte[] Rel = new sbyte[N * N]; // [a * N + b]: how power a + 1 sees power b + 1
    public readonly RelBand[] Band = new RelBand[N * N];
    public readonly bool[] MetFaction = new bool[FactionTable.Max * N];
    public readonly bool[] MetPlayer = new bool[256 * N];

    // Pacts.
    public readonly PactKind[] PactK = new PactKind[MaxPacts];
    public readonly byte[] PactA = new byte[MaxPacts], PactB = new byte[MaxPacts];
    public readonly long[] PactStart = new long[MaxPacts], PactEnd = new long[MaxPacts]; // ticks (End 0: none)
    public readonly int[] PactKept = new int[MaxPacts]; // real hours kept, rewarded
    private readonly PactKind[] _proposed = new PactKind[N * N]; // a proposed to b
    private readonly long[] _answerAfter = new long[N * N]; // a doesn't propose to b again before this tick
    // Wars: since when a sees itself at war with b, a's losses to b since, and its last.
    private readonly long[] _warSince = new long[N * N], _lastLoss = new long[N * N];
    private readonly int[] _loss = new int[N * N];
    // Help in a fight (section 5.3): when friend f last thanked doer d, and how much this fight. [f * N + d] powers; [N * N + f * 256 + p] players.
    private readonly long[] _helpAt = new long[N * N + N * 256];
    private readonly byte[] _helpSum = new byte[N * N + N * 256];
    // Decay and thinking.
    private readonly float[] _decay = new float[N];
    private readonly long[] _thought = new long[N];
    private int _next;
    private long _thinkAt;

    // News (the last MaxNews lines, a ring; NewsTotal counts every line ever posted, so a sender knows what is new).
    public readonly string[] News = new string[MaxNews];
    public int NewsTotal;

    private static int Count => CrewPowers.Count;
    private static NpcPowerDef? Def(int id) => CrewPowers.Of((byte)id);

    /// <summary>A new world: every relation from the power files, no pacts, nobody met, no news.</summary>
    public void Reset()
    {
        Array.Clear(Rel);
        for (var a = 1; a <= Count; a++)
            for (var b = 1; b <= Count; b++)
            {
                var k = (a - 1) * N + b - 1;
                Rel[k] = Def(a)!.StartTo[b];
                Band[k] = Rules.Raw(Rel[k]);
            }
        Array.Clear(MetFaction);
        Array.Clear(MetPlayer);
        Array.Clear(PactK);
        Array.Clear(_proposed);
        Array.Clear(_answerAfter);
        Array.Clear(_warSince);
        Array.Clear(_lastLoss);
        Array.Clear(_loss);
        Array.Clear(_helpAt);
        Array.Clear(_helpSum);
        Array.Clear(_decay);
        Array.Clear(_thought);
        Array.Clear(News);
        NewsTotal = 0;
        for (var a = 1; a <= Count; a++)
            for (var b = 1; b <= Count; b++)
                if (Band[(a - 1) * N + b - 1] == RelBand.War)
                    _warSince[(a - 1) * N + b - 1] = Tick;
        Version++;
    }

    public int Get(byte a, byte b) => a == b ? 100 : a > 0 && b > 0 && a <= N && b <= N ? Rel[(a - 1) * N + b - 1] : 0;
    public RelBand BandOf(byte a, byte b) => a == b ? RelBand.Friendly : a > 0 && b > 0 && a <= N && b <= N ? Band[(a - 1) * N + b - 1] : RelBand.Neutral;

    public PactKind PactOf(byte a, byte b)
    {
        var i = PactIndex(a, b);
        return i < 0 ? PactKind.None : PactK[i];
    }

    private int PactIndex(byte a, byte b)
    {
        for (var i = 0; i < MaxPacts; i++)
            if (PactK[i] != PactKind.None && (PactA[i] == a && PactB[i] == b || PactA[i] == b && PactB[i] == a))
                return i;
        return -1;
    }

    /// <summary>Crews of <paramref name="a"/> don't start a fight with <paramref name="b"/>'s (a pact holds).</summary>
    public bool HoldsFire(byte a, byte b) => PactOf(a, b) != PactKind.None;

    /// <summary>Crews of <paramref name="a"/> fight <paramref name="b"/>'s on sight: below 0 and no pact.</summary>
    public bool Fights(byte a, byte b) => a != b && Get(a, b) < 0 && !HoldsFire(a, b);

    /// <summary><paramref name="a"/> helps <paramref name="f"/> in a fight it sees: allied, or friendly.</summary>
    public bool Helps(byte a, byte f) => a != 0 && f != 0 && a != f && (PactOf(a, f) == PactKind.Alliance || BandOf(a, f) == RelBand.Friendly);

    // ---- Standing with players (the factions table's NPC rows) -----------------------------------------------------------------------

    /// <summary>How power <paramref name="power"/> sees <paramref name="player"/>: their player faction's standing, else their own (met now if not yet).</summary>
    public ref sbyte Standing(byte power, byte player)
    {
        var t = Table;
        var def = Def(power)!;
        var f = power - 1;
        var party = t.Of[player];
        if (party != 0)
        {
            var k = party * N + f;
            if (!MetFaction[k])
            {
                MetFaction[k] = true;
                t.StoryStanding[k] = def.StartPlayers;
                t.Version++;
            }
            return ref t.StoryStanding[k];
        }
        var j = player * N + f;
        if (!MetPlayer[j])
        {
            MetPlayer[j] = true;
            t.PlayerStanding[j] = def.StartPlayers;
            t.PlayerStory[player] = true;
            t.Version++;
        }
        return ref t.PlayerStanding[j];
    }

    /// <summary>The standing without meeting (a power's start value for someone it hasn't met).</summary>
    public int Peek(byte power, byte player)
    {
        if (Def(power) is not { } def)
            return 0;
        var t = Table;
        var party = t.Of[player];
        return party != 0
            ? MetFaction[party * N + power - 1] ? t.StoryStanding[party * N + power - 1] : def.StartPlayers
            : MetPlayer[player * N + power - 1] ? t.PlayerStanding[player * N + power - 1] : def.StartPlayers;
    }

    // ---- Events ----------------------------------------------------------------------------------------------------------------------

    /// <summary>Event <paramref name="e"/>'s amount for <paramref name="who"/>, scaled by its parameters (each 0.5 + p; 0.5 everywhere: the base amount). Never 0 for a base that isn't.</summary>
    public int Amount(RelEvent e, NpcPowerDef who)
    {
        var a = Rules.Amount[(int)e];
        if (a == 0)
            return 0;
        var f = 1f;
        foreach (var p in Rules.Scale[(int)e])
            f *= 0.5f + who.Param(p);
        var v = (int)MathF.Round(a * f, MidpointRounding.AwayFromZero);
        return v != 0 ? v : Math.Sign(a);
    }

    private static bool Attack(RelEvent e) => e <= RelEvent.Capture;

    /// <summary>
    /// <paramref name="doerPower"/> (a power; 0: none) or <paramref name="doerPlayer"/> (a person; 255: none) did <paramref name="e"/> to power
    /// <paramref name="victim"/> at <paramref name="at"/>: the victim's view of the doer moves, a pact between them breaks on an attack, and powers
    /// that know of it (a crew of theirs sees the place, or they are the victim's allies) take a part: a friend of the victim's its loyalty's share,
    /// an enemy of the victim's the opposite fifth.
    /// </summary>
    public void Post(RelEvent e, byte victim, byte doerPower, byte doerPlayer, Vector3 at)
    {
        if (Def(victim) is not { } v || doerPower == victim || doerPower == 0 && doerPlayer == 255 || doerPower > Count)
            return;
        var amount = Amount(e, v);
        if (doerPower != 0)
        {
            Move(victim, doerPower, amount);
            if (Attack(e))
            {
                if (PactIndex(victim, doerPower) is var pi and >= 0)
                    Broken(pi, doerPower, victim, at);
                if (e is RelEvent.KillCrew or RelEvent.DestroyShip && Band[(victim - 1) * N + doerPower - 1] == RelBand.War)
                {
                    var k = (victim - 1) * N + doerPower - 1;
                    _loss[k] += e == RelEvent.DestroyShip ? 5 : 1;
                    _lastLoss[k] = Tick;
                }
            }
        }
        else
            MovePlayer(victim, doerPlayer, amount);
        if (e is RelEvent.BreakPact or RelEvent.BreakPactHeard or RelEvent.KeepPact)
            return;
        // Word gets around.
        for (var c = 1; c <= Count; c++)
        {
            if (c == victim || c == doerPower || Def(c) is not { Controller: true } cd)
                continue;
            var allied = PactOf((byte)c, victim) == PactKind.Alliance;
            if (!allied && Sight?.Sees((byte)c, at) != true)
                continue;
            var view = Get((byte)c, victim);
            var share = allied || view >= Rules.FriendlyLine ? (int)MathF.Round(amount * cd.Loyalty, MidpointRounding.AwayFromZero)
                : view < 0 ? -(int)(amount * Rules.EnemyShare)
                : 0;
            if (share == 0)
                continue;
            if (doerPower != 0)
                Move((byte)c, doerPower, share);
            else
                MovePlayer((byte)c, doerPlayer, share);
        }
    }

    /// <summary>
    /// <paramref name="doerPower"/> or <paramref name="doerPlayer"/> hurt someone fighting power <paramref name="friend"/>'s crew near it: the
    /// friend thinks better of them, at most once each <see cref="RelationRules.HelpEverySeconds"/> and <see cref="RelationRules.HelpMax"/> a fight.
    /// </summary>
    public void Help(byte friend, byte doerPower, byte doerPlayer, Vector3 at)
    {
        if (Def(friend) is not { } f || doerPower == friend || doerPower == 0 && doerPlayer == 255 || doerPower > Count)
            return;
        var k = doerPower != 0 ? (friend - 1) * N + doerPower - 1 : N * N + (friend - 1) * 256 + doerPlayer;
        if (_helpAt[k] != 0 && Tick - _helpAt[k] < (long)Rules.HelpEverySeconds * T)
            return;
        if (_helpAt[k] == 0 || Tick - _helpAt[k] > (long)Rules.FightGapSeconds * T)
            _helpSum[k] = 0; // (a new fight)
        var amount = Math.Min(Amount(RelEvent.Help, f), Rules.HelpMax - _helpSum[k]);
        if (amount <= 0)
            return;
        _helpAt[k] = Tick;
        _helpSum[k] = (byte)(_helpSum[k] + amount);
        if (doerPower != 0)
            Move(friend, doerPower, amount);
        else
            MovePlayer(friend, doerPlayer, amount);
    }

    /// <summary>How <paramref name="a"/> sees <paramref name="b"/> moves by <paramref name="delta"/>; its band follows (with hysteresis), a war starts or ends, news.</summary>
    public void Move(byte a, byte b, int delta)
    {
        if (a == b || a == 0 || b == 0 || a > Count || b > Count || delta == 0)
            return;
        var k = (a - 1) * N + b - 1;
        var was = Rel[k];
        Rel[k] = (sbyte)Math.Clamp(was + delta, -100, 100);
        if (Rel[k] == was)
            return;
        Version++;
        SetBand(a, b, Rules.Next(Band[k], Rel[k]));
    }

    /// <summary>A forced value (a ceasefire): the band from the value alone.</summary>
    private void Set(byte a, byte b, int v)
    {
        var k = (a - 1) * N + b - 1;
        Rel[k] = (sbyte)Math.Clamp(v, -100, 100);
        Version++;
        SetBand(a, b, Rules.Raw(Rel[k]));
    }

    private void SetBand(byte a, byte b, RelBand band)
    {
        var k = (a - 1) * N + b - 1;
        var was = Band[k];
        if (band == was)
            return;
        Band[k] = band;
        if (band == RelBand.War)
        {
            _warSince[k] = Tick;
            _loss[k] = 0;
            Say($"{Name(a)} declares war on {Name(b)}");
        }
        else if (was == RelBand.War)
        {
            _warSince[k] = 0;
            Say($"{Name(a)} is no longer at war with {Name(b)}");
        }
        else if (band == RelBand.Friendly)
            Say($"{Name(a)} now counts {Name(b)} as friends");
        else if (was == RelBand.Friendly)
            Say($"{Name(a)} no longer counts {Name(b)} as friends");
        else if (band == RelBand.Hostile && was > band)
            Say($"{Name(a)} turns hostile to {Name(b)}");
    }

    /// <summary>How <paramref name="power"/> sees a person (their faction, else themselves) moves by <paramref name="delta"/>.</summary>
    public void MovePlayer(byte power, byte player, int delta)
    {
        if (player == 255 || Def(power) is null || delta == 0)
            return;
        ref var v = ref Standing(power, player);
        var was = v;
        v = (sbyte)Math.Clamp(v + delta, -100, 100);
        if (v == was)
            return;
        Table.Version++;
        if (was >= 0 && v < 0)
            Console.WriteLine($"[crew] {Def(power)!.Name} now fight player {player} (standing {v})");
        if (was > Rules.WarLine && v <= Rules.WarLine)
            Say($"{Name(power)} declares war on {Party(player)}");
        else if (was < Rules.FriendlyLine && v >= Rules.FriendlyLine)
            Say($"{Name(power)} now counts {Party(player)} as friends");
    }

    // ---- Pacts -------------------------------------------------------------------------------------------------------------------------

    public static string PactName(PactKind k) => k switch { PactKind.Ceasefire => "ceasefire", PactKind.NonAggression => "non-aggression pact", PactKind.Alliance => "alliance", _ => "pact" };

    private void Form(PactKind kind, byte a, byte b)
    {
        var i = PactIndex(a, b);
        if (i < 0)
            i = Array.IndexOf(PactK, PactKind.None);
        if (i < 0)
        {
            Console.WriteLine($"[crew] {Name(a)} and {Name(b)} would sign a {PactName(kind)}, but the world holds {MaxPacts} pacts already: left out");
            return;
        }
        (PactK[i], PactA[i], PactB[i], PactStart[i], PactKept[i]) = (kind, a, b, Tick, 0);
        PactEnd[i] = kind == PactKind.Ceasefire ? Tick + (long)Rules.CeasefireMinutes * 60 * T : 0;
        if (kind == PactKind.Ceasefire)
        {
            foreach (var (x, y) in new[] { (a, b), (b, a) }) // (rare: a pact signed)
                if (Get(x, y) < Rules.CeasefireRelation)
                    Set(x, y, Rules.CeasefireRelation);
        }
        Version++;
        Say(kind == PactKind.Alliance ? $"{Name(a)} and {Name(b)} are now allies" : $"{Name(a)} and {Name(b)} sign a {PactName(kind)}");
    }

    private void End(int i, string why)
    {
        Say($"The {PactName(PactK[i])} between {Name(PactA[i])} and {Name(PactB[i])} {why}");
        PactK[i] = PactKind.None;
        Version++;
    }

    /// <summary><paramref name="breaker"/> attacked <paramref name="victim"/> under pact <paramref name="i"/>: it ends, the victim takes it hard, and those who know think less of the breaker.</summary>
    private void Broken(int i, byte breaker, byte victim, Vector3 at)
    {
        var kind = PactK[i];
        PactK[i] = PactKind.None;
        Version++;
        Say($"{Name(breaker)} breaks its {PactName(kind)} with {Name(victim)}");
        Move(victim, breaker, Amount(RelEvent.BreakPact, Def(victim)!));
        for (var c = 1; c <= Count; c++)
            if (c != victim && c != breaker && Def(c) is { Controller: true } cd
                && (PactOf((byte)c, victim) != PactKind.None || Get((byte)c, victim) >= Rules.FriendlyLine || Sight?.Sees((byte)c, at) == true))
                Move((byte)c, breaker, Amount(RelEvent.BreakPactHeard, cd));
    }

    /// <summary>Some power both see below 0 (and, <paramref name="war"/>, one of them at war with it).</summary>
    private bool CommonEnemy(byte a, byte b, bool war)
    {
        for (var c = 1; c <= Count; c++)
            if (c != a && c != b && Get(a, (byte)c) < 0 && Get(b, (byte)c) < 0 && (!war || BandOf(a, (byte)c) == RelBand.War || BandOf(b, (byte)c) == RelBand.War))
                return true;
        return false;
    }

    private bool AllianceWanted(byte a, byte b) => BandOf(a, b) == RelBand.Friendly && BandOf(b, a) == RelBand.Friendly
        && Def(a)!.Loyalty >= Rules.AllianceLoyalty && Def(b)!.Loyalty >= Rules.AllianceLoyalty && CommonEnemy(a, b, war: true);

    private bool NonAggressionWanted(byte a, byte b) => BandOf(a, b) >= RelBand.Neutral && BandOf(b, a) >= RelBand.Neutral && CommonEnemy(a, b, war: false);

    private bool CeasefireWanted(byte a, byte b)
    {
        var k = (a - 1) * N + b - 1;
        if (BandOf(a, b) != RelBand.War && BandOf(b, a) != RelBand.War)
            return false;
        var line = Rules.CeasefireLosses * (0.5f + Def(a)!.Pride);
        var since = Math.Max(_warSince[k], _warSince[(b - 1) * N + a - 1]);
        var quiet = Math.Max(_lastLoss[k], _lastLoss[(b - 1) * N + a - 1]);
        var stale = (long)Rules.StaleWarMinutes * 60 * T;
        // Or a long war that saw fighting (a loss either way) and has gone quiet: a world where two powers start at war and never meet keeps its war.
        return _loss[k] >= line || since > 0 && quiet > since && Tick - since >= stale && Tick - quiet >= stale / 3;
    }

    // ---- Thinking --------------------------------------------------------------------------------------------------------------------

    /// <summary>A server tick: one power thinks when its turn comes (each every <see cref="ThinkSeconds"/>).</summary>
    public void Step()
    {
        Tick++;
        if (Count == 0 || Tick < _thinkAt)
            return;
        _thinkAt = Tick + Math.Max(1, ThinkSeconds * T / Count);
        if (_next >= Count)
            _next = 0;
        Think((byte)(++_next));
    }

    /// <summary>One power's think: its relations drift towards rest, its pacts are kept, expire or end, it answers proposals and makes them.</summary>
    public void Think(byte a)
    {
        var def = Def(a)!;
        var dt = _thought[a - 1] == 0 ? 0 : Tick - _thought[a - 1];
        _thought[a - 1] = Tick;
        if (!def.Controller)
            return;
        Decay(a, def, dt);
        for (var i = 0; i < MaxPacts; i++)
        {
            if (PactK[i] == PactKind.None || PactA[i] != a && PactB[i] != a)
                continue;
            var o = PactA[i] == a ? PactB[i] : PactA[i];
            if (PactEnd[i] != 0 && Tick >= PactEnd[i])
            {
                End(i, "runs out");
                continue;
            }
            if (PactK[i] == PactKind.Alliance && (BandOf(a, o) < RelBand.Neutral || BandOf(o, a) < RelBand.Neutral))
            {
                End(i, "falls apart");
                continue;
            }
            if (PactA[i] == a && (Tick - PactStart[i]) / (3600L * T) > PactKept[i])
            {
                PactKept[i]++;
                Move(a, o, Amount(RelEvent.KeepPact, def));
                Move(o, a, Amount(RelEvent.KeepPact, Def(o)!));
            }
            if (PactK[i] == PactKind.NonAggression && AllianceWanted(a, o) && _proposed[(a - 1) * N + o - 1] == PactKind.None && Tick >= _answerAfter[(a - 1) * N + o - 1])
                _proposed[(a - 1) * N + o - 1] = PactKind.Alliance;
        }
        for (var b = (byte)1; b <= Count; b++)
        {
            if (b == a || Def(b) is not { Controller: true } bd)
                continue;
            var ab = (a - 1) * N + b - 1;
            var ba = (b - 1) * N + a - 1;
            // Answer b's proposal to a.
            if (_proposed[ba] is var kind and not PactKind.None)
            {
                _proposed[ba] = PactKind.None;
                var cur = PactOf(a, b);
                var yes = kind switch
                {
                    PactKind.Ceasefire => cur == PactKind.None && CeasefireWanted(b, a) && (def.Aggression < Rules.CeasefireRefuseAggression || _loss[ab] > 0),
                    PactKind.NonAggression => cur == PactKind.None && NonAggressionWanted(a, b),
                    PactKind.Alliance => cur is PactKind.None or PactKind.NonAggression && AllianceWanted(a, b),
                    _ => false,
                };
                if (yes)
                    Form(kind, b, a);
                else
                {
                    _answerAfter[ba] = Tick + (long)Rules.AnswerCooldownMinutes * 60 * T;
                    Console.WriteLine($"[crew] {def.Name} turns down {bd.Name}'s {PactName(kind)}");
                }
                continue;
            }
            // Propose.
            if (PactOf(a, b) != PactKind.None || _proposed[ab] != PactKind.None || Tick < _answerAfter[ab])
                continue;
            var want = CeasefireWanted(a, b) ? PactKind.Ceasefire
                : AllianceWanted(a, b) ? PactKind.Alliance
                : NonAggressionWanted(a, b) ? PactKind.NonAggression
                : PactKind.None;
            if (want != PactKind.None)
                _proposed[ab] = want;
        }
    }

    private void Decay(byte a, NpcPowerDef def, long dt)
    {
        _decay[a - 1] += def.Forgiveness * Rules.DecayPerMinute * dt / (60f * T);
        var steps = (int)_decay[a - 1];
        if (steps <= 0)
            return;
        _decay[a - 1] -= steps;
        for (var b = 1; b <= Count; b++)
        {
            if (b == a)
                continue;
            var k = (a - 1) * N + b - 1;
            var to = Toward(Rel[k], def.RestTo[b], steps, Band[k] == RelBand.War);
            if (to != Rel[k])
                Move(a, (byte)b, to - Rel[k]);
        }
        var t = Table;
        var changed = false;
        for (var party = 1; party < FactionTable.Max; party++)
            if (MetFaction[party * N + a - 1])
            {
                ref var v = ref t.StoryStanding[party * N + a - 1];
                var to = Toward(v, def.RestPlayers, steps, v <= Rules.WarLine);
                changed |= to != v;
                v = (sbyte)to;
            }
        for (var p = 0; p < 256; p++)
            if (MetPlayer[p * N + a - 1])
            {
                ref var v = ref t.PlayerStanding[p * N + a - 1];
                var to = Toward(v, def.RestPlayers, steps, v <= Rules.WarLine);
                changed |= to != v;
                v = (sbyte)to;
            }
        if (changed)
            t.Version++;
    }

    /// <summary><paramref name="v"/> moved <paramref name="steps"/> towards <paramref name="rest"/>; at war, never past the war line on its own (a war ends by a ceasefire).</summary>
    private int Toward(int v, int rest, int steps, bool war)
    {
        var to = v < rest ? Math.Min(v + steps, rest) : Math.Max(v - steps, rest);
        return war && to > Rules.WarLine && v <= Rules.WarLine ? Math.Max(v, Rules.WarLine) : to;
    }

    // ---- News ------------------------------------------------------------------------------------------------------------------------

    private void Say(string line)
    {
        News[NewsTotal % MaxNews] = line;
        NewsTotal++;
        Version++;
        Console.WriteLine($"[crew] news: {line}");
    }

    /// <summary>The news line posted <paramref name="total"/>th (0-based), if still held.</summary>
    public string? NewsLine(int total) => total >= 0 && total < NewsTotal && total >= NewsTotal - MaxNews ? News[total % MaxNews] : null;

    private static string Name(byte power) => Def(power)?.Name ?? $"power {power}";

    private string Party(byte player)
    {
        var party = Table.Of[player];
        return PartyName?.Invoke(party != 0 ? party : player) is { Length: > 0 } n ? n : party != 0 ? $"faction {party}" : $"player {player}";
    }

    /// <summary>A tool's line (allocates): the grid, a row a power.</summary>
    public string GridText()
    {
        var sb = new System.Text.StringBuilder();
        for (var a = 1; a <= Count; a++)
        {
            sb.Append($"  {Name((byte)a),-18}");
            for (var b = 1; b <= Count; b++)
                sb.Append(a == b ? "    ." : $" {Get((byte)a, (byte)b),4}");
            sb.AppendLine();
        }
        for (var i = 0; i < MaxPacts; i++)
            if (PactK[i] != PactKind.None)
                sb.AppendLine($"  pact: {PactName(PactK[i])} {Name(PactA[i])} - {Name(PactB[i])}");
        return sb.ToString();
    }

    // ---- Saves (the world's Powers section) ------------------------------------------------------------------------------------------

    public const byte SaveVersion = 1;

    /// <summary>
    /// u8 version; u8 n, n x text power name; n x n x i8 (row a: how a sees each); u8 pacts, each (u8 kind, u8 a, u8 b, i32 seconds held,
    /// i32 seconds left (-1: none), u8 hours kept); n x n x (i32 seconds at war, -1 none; i32 losses); u8 news, each text. Ids are indices into
    /// the names, so a world read with powers renumbered keeps them by name.
    /// </summary>
    public byte[] SaveBytes()
    {
        var w = new SaveWriter(512);
        var n = Count;
        w.U8(SaveVersion).U8((byte)n);
        for (var a = 1; a <= n; a++)
            w.Str(Name((byte)a));
        for (var a = 0; a < n; a++)
            for (var b = 0; b < n; b++)
                w.U8((byte)Rel[a * N + b]);
        var pn = 0;
        foreach (var k in PactK)
            if (k != PactKind.None)
                pn++;
        w.U8((byte)pn);
        for (var i = 0; i < MaxPacts; i++)
            if (PactK[i] != PactKind.None)
                w.U8((byte)PactK[i]).U8((byte)(PactA[i] - 1)).U8((byte)(PactB[i] - 1)).I32((int)((Tick - PactStart[i]) / T)).I32(PactEnd[i] == 0 ? -1 : (int)Math.Max(0, (PactEnd[i] - Tick) / T)).U8((byte)Math.Min(PactKept[i], 255));
        for (var a = 0; a < n; a++)
            for (var b = 0; b < n; b++)
                w.I32(_warSince[a * N + b] == 0 ? -1 : (int)((Tick - _warSince[a * N + b]) / T)).I32(_loss[a * N + b]);
        var news = Math.Min(NewsTotal, MaxNews);
        w.U8((byte)news);
        for (var k = NewsTotal - news; k < NewsTotal; k++)
            w.Str(News[k % MaxNews]);
        return w.Span.ToArray();
    }

    /// <summary>A saved world's relations over the powers' files (a power the save doesn't name keeps its start; one it names that isn't read is left out, with a line).</summary>
    public void Load(byte[] bytes)
    {
        try
        {
            var r = new SaveReader(bytes);
            var v = r.U8();
            if (v != SaveVersion)
            {
                Console.WriteLine($"[crew] the world's powers section is of version {v} (this build reads {SaveVersion}): relations start from the power files");
                return;
            }
            int n = r.U8();
            var map = new byte[n];
            for (var k = 0; k < n; k++)
            {
                var name = r.Str();
                map[k] = CrewPowers.Find(name) is var id && id > 0 && Def(id)!.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? id : (byte)0;
                if (map[k] == 0)
                    Console.WriteLine($"[crew] the world's relations name power \"{name}\", which isn't read now: its row is left out");
            }
            for (var a = 0; a < n; a++)
                for (var b = 0; b < n; b++)
                {
                    var x = (sbyte)r.U8();
                    if (map[a] != 0 && map[b] != 0 && map[a] != map[b])
                    {
                        var k = (map[a] - 1) * N + map[b] - 1;
                        Rel[k] = x;
                        Band[k] = Rules.Raw(x);
                    }
                }
            Array.Clear(PactK);
            int pn = r.U8();
            for (var i = 0; i < pn; i++)
            {
                var kind = (PactKind)r.U8();
                int a = r.U8(), b = r.U8();
                var held = r.I32();
                var left = r.I32();
                var kept = r.U8();
                if (a >= n || b >= n || map[a] == 0 || map[b] == 0 || kind is not (PactKind.Ceasefire or PactKind.NonAggression or PactKind.Alliance) || i >= MaxPacts)
                    continue;
                (PactK[i], PactA[i], PactB[i], PactStart[i], PactEnd[i], PactKept[i]) = (kind, map[a], map[b], Tick - (long)held * T, left < 0 ? 0 : Tick + (long)left * T, kept);
            }
            for (var a = 0; a < n; a++)
                for (var b = 0; b < n; b++)
                {
                    var since = r.I32();
                    var loss = r.I32();
                    if (map[a] != 0 && map[b] != 0)
                    {
                        var k = (map[a] - 1) * N + map[b] - 1;
                        _warSince[k] = since < 0 ? 0 : Math.Max(1, Tick - (long)since * T);
                        _loss[k] = loss;
                    }
                }
            int nn = r.U8();
            Array.Clear(News);
            NewsTotal = 0;
            for (var k = 0; k < nn; k++)
            {
                News[NewsTotal % MaxNews] = r.Str();
                NewsTotal++;
            }
            Version++;
        }
        catch (Exception ex) when (ex is WorldFileException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            Console.WriteLine($"[crew] the world's powers section doesn't read ({ex.Message}): relations start from the power files");
            Reset();
        }
    }
}
