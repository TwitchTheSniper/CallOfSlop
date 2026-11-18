using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Bands a relation falls in (faction-controllers.md section 5.2, widened by Karl's answer 2 in phase 3). Crews fight below 0 whatever the band.
/// Below War, a relation at -100 is a <b>blood feud</b> (<see cref="PowerRelations.Blood"/>): it never moves again, so no peace is ever made;
/// above Friendly, <see cref="Kindred"/> powers want to join each other, and the weaker merges into the stronger when both stay there.
/// </summary>
public enum RelBand : byte
{
    War,
    Hostile,
    Wary,
    Neutral,
    Friendly,
    Kindred, // phase 3: wants to join and merge with them
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
    CaptureOfficer, // phase 2: an admiral or a captain taken prisoner (faction-controllers.md, the user's answers on admirals 7)
    ReleaseOfficer, // ... released or ransomed back
    RaidConvoy, // phase 3: a convoy's cargo ship lost to a raid (the controllers: Crew/CrewControllers.cs)
    EscortConvoy, // ... a convoy brought home with the doer's help (an answered call)
    AnswerCall, // ... a call for help answered
    RefuseCall, // ... an ally's call for help turned down
    PayTribute, // ... a tribute paid on time, each real hour
    TakeSite, // phase 4: a key site taken from its holder (Crew/CrewTerritory.cs); a whole planet lost adds TakePlanet
    TakePlanet,
    ContractDone, // phase 5: a contract for credits done for the power (Crew/CrewContracts.cs)
    ContractFailed, // ... failed or given up
    Count,
}

public enum PactKind : byte
{
    None,
    Ceasefire,
    NonAggression,
    Alliance,
    Trade, // phase 3 (Karl's answer 2): both earn by it; no fights
    Tribute, // the payer (PactA) pays a share of its income to the other (PactB) for peace; no end while paid
    Vassalage, // a tribute grown old and friendly: the vassal (PactA) pays and fights its lord's (PactB) enemies, as an ally
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
    public static readonly string[] Keys = ["hitBody", "hitCrew", "killCrew", "destroyShip", "board", "capture", "release", "drop", "hire", "help", "breakPact", "breakPactHeard", "keepPact", "captureOfficer", "releaseOfficer",
        "raidConvoy", "escortConvoy", "answerCall", "refuseCall", "payTribute", "takeSite", "takePlanet", "contractDone", "contractFailed"];

    public readonly int[] Amount = [-5, -3, -4, -15, -10, -20, 5, 3, 2, 2, -40, -15, 1, -25, 15, -10, 5, 3, -10, 1, -10, -40, 5, -5];
    public readonly string[][] Scale = [["pride"], ["pride"], ["values.crew"], ["values.ships", "pride"], ["pride"], ["values.ships", "pride"], ["forgiveness"], ["forgiveness"], [], ["loyalty"], [], ["loyalty"], [], ["pride"], ["forgiveness"],
        ["values.cargo", "greed"], ["values.cargo"], ["loyalty"], ["loyalty"], ["greed"], ["values.planets"], ["values.planets", "pride"], ["greed"], ["pride"]];
    public int WarLine = -50, WaryLine = 0, NeutralLine = 20, FriendlyLine = 60, KindredLine = 90, Hysteresis = 5;
    public float BloodPride = 0.4f; // a relation at the floor hit again turns to a blood feud when the victim's pride is at least this
    public int MergeMinutes = 30; // both kindred this long: the weaker joins the stronger
    public float TradeGreed = 0.5f, TributeWeaker = 0.6f, TributePride = 0.6f, TributeGreed = 0.4f, VassalLoyalty = 0.3f;
    public int VassalMinutes = 60; // a tribute held this long with the payer at least neutral to the receiver: vassalage
    public float TributeShare = 0.25f, TradePerMinute = 20; // the tribute's share of the payer's income; what a trade pact earns each side a minute (units)
    public float DecayPerMinute = 1f, EnemyShare = 0.2f;
    public int HelpMax = 10, HelpEverySeconds = 60, FightGapSeconds = 300;
    public int CeasefireMinutes = 30, CeasefireRelation = -40, CeasefireLosses = 10, StaleWarMinutes = 30, AnswerCooldownMinutes = 5;
    public float AllianceLoyalty = 0.5f, CeasefireRefuseAggression = 0.8f;
    public int FriendlyFireHits = 8; // a friend's or a pact partner's hits forgiven in a minute (stray rounds, splash); past it, deliberate
    // Phase 5 ("players"): pacts with player factions. A power's own offer stands OfferMinutes, then it waits OfferCooldownMinutes; a faction calls its allies at
    // most every CallCooldownMinutes; a trade pact pays the leader TradeCredits a minute; a tribute costs the leader TributeCredits every TributeEveryMinutes.
    public int PartyOfferMinutes = 10, PartyOfferCooldownMinutes = 15, CallCooldownMinutes = 3, TributeEveryMinutes = 10, TradeCredits = 10, TributeCredits = 150;
    // Phase 5 ("contracts"): jobs for credits a power offers (Crew/CrewContracts.cs).
    public int ContractOfferSeconds = 45, ContractOfferMinutes = 15, EscortSeconds = 180, EscortMinutes = 20, DeliverMinutes = 30, RaidMinutes = 30;
    public float EscortRange = 400, DeliverRange = 60;
    public int EscortPay = 300, RaidPay = 400, DeliverBase = 100, DeliverPerUnit = 2, DeliverMin = 40, DeliverMax = 160;
    public string[] DeliverMaterials = ["iron ore", "copper ore", "stone", "planks", "metal"];

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
                r.KindredLine = Int(b, "kindred", r.KindredLine, r.FriendlyLine + 1, 100);
                r.Hysteresis = Int(b, "hysteresis", r.Hysteresis, 0, 20);
                r.BloodPride = Num(b, "bloodPride", r.BloodPride, 0, 1.01f);
                r.MergeMinutes = Int(b, "mergeMinutes", r.MergeMinutes, 1, 100000);
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
                r.FriendlyFireHits = Int(p, "friendlyFireHits", r.FriendlyFireHits, 0, 255);
                r.TradeGreed = Num(p, "tradeGreed", r.TradeGreed, 0, 1.01f);
                r.TradePerMinute = Num(p, "tradePerMinute", r.TradePerMinute, 0, 1e6f);
                r.TributeWeaker = Num(p, "tributeWeaker", r.TributeWeaker, 0, 1);
                r.TributePride = Num(p, "tributePride", r.TributePride, 0, 1.01f);
                r.TributeGreed = Num(p, "tributeGreed", r.TributeGreed, 0, 1.01f);
                r.TributeShare = Num(p, "tributeShare", r.TributeShare, 0, 1);
                r.VassalMinutes = Int(p, "vassalMinutes", r.VassalMinutes, 1, 100000);
                r.VassalLoyalty = Num(p, "vassalLoyalty", r.VassalLoyalty, 0, 1.01f);
            }
            if (root.TryGetProperty("players", out var pl))
            {
                r.PartyOfferMinutes = Int(pl, "offerMinutes", r.PartyOfferMinutes, 1, 1440);
                r.PartyOfferCooldownMinutes = Int(pl, "offerCooldownMinutes", r.PartyOfferCooldownMinutes, 0, 1440);
                r.CallCooldownMinutes = Int(pl, "callCooldownMinutes", r.CallCooldownMinutes, 0, 1440);
                r.TributeEveryMinutes = Int(pl, "tributeEveryMinutes", r.TributeEveryMinutes, 1, 1440);
                r.TradeCredits = Int(pl, "tradeCredits", r.TradeCredits, 0, 100000);
                r.TributeCredits = Int(pl, "tributeCredits", r.TributeCredits, 0, 1000000);
            }
            if (root.TryGetProperty("contracts", out var ct))
            {
                r.ContractOfferSeconds = Int(ct, "offerSeconds", r.ContractOfferSeconds, 5, 86400);
                r.ContractOfferMinutes = Int(ct, "offerMinutes", r.ContractOfferMinutes, 1, 1440);
                r.EscortSeconds = Int(ct, "escortSeconds", r.EscortSeconds, 10, 86400);
                r.EscortMinutes = Int(ct, "escortMinutes", r.EscortMinutes, 1, 1440);
                r.DeliverMinutes = Int(ct, "deliverMinutes", r.DeliverMinutes, 1, 1440);
                r.RaidMinutes = Int(ct, "raidMinutes", r.RaidMinutes, 1, 1440);
                r.EscortRange = Num(ct, "escortRange", r.EscortRange, 10, 10000);
                r.DeliverRange = Num(ct, "deliverRange", r.DeliverRange, 5, 1000);
                r.EscortPay = Int(ct, "escortPay", r.EscortPay, 0, 1000000);
                r.RaidPay = Int(ct, "raidPay", r.RaidPay, 0, 1000000);
                r.DeliverBase = Int(ct, "deliverBase", r.DeliverBase, 0, 1000000);
                r.DeliverPerUnit = Int(ct, "deliverPerUnit", r.DeliverPerUnit, 0, 10000);
                r.DeliverMin = Int(ct, "deliverMin", r.DeliverMin, 1, 60000);
                r.DeliverMax = Int(ct, "deliverMax", r.DeliverMax, r.DeliverMin, 60000);
                if (ct.TryGetProperty("materials", out var mats))
                {
                    var names = mats.EnumerateArray().Select(e => e.GetString() ?? "").Where(n => n.Length > 0).ToArray();
                    if (names.Length == 0)
                        throw new InvalidDataException("\"contracts.materials\" names no material");
                    r.DeliverMaterials = names;
                }
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
    public RelBand Raw(int v) => v <= WarLine ? RelBand.War : v < WaryLine ? RelBand.Hostile : v < NeutralLine ? RelBand.Wary : v < FriendlyLine ? RelBand.Neutral : v < KindredLine ? RelBand.Friendly : RelBand.Kindred;

    private int Lower(RelBand b) => b switch { RelBand.War => -100, RelBand.Hostile => WarLine + 1, RelBand.Wary => WaryLine, RelBand.Neutral => NeutralLine, RelBand.Friendly => FriendlyLine, _ => KindredLine };

    /// <summary>The band in words (the Faction window, news); -100 is a blood feud.</summary>
    public string Words(int v) => v <= PowerRelations.BloodValue ? "blood enemies" : Raw(v) switch
    {
        RelBand.War => "at war",
        RelBand.Hostile => "hostile",
        RelBand.Wary => "wary",
        RelBand.Neutral => "neutral",
        RelBand.Friendly => "friendly",
        _ => "wants to merge",
    };

    /// <summary>The band after <paramref name="prev"/> for value <paramref name="v"/>, with <see cref="Hysteresis"/> points each way.</summary>
    public RelBand Next(RelBand prev, int v)
    {
        var raw = Raw(v);
        if (raw < prev && v > Lower(prev) - Hysteresis)
            return prev;
        if (raw > prev && prev < RelBand.Kindred && v < Lower(prev + 1) + Hysteresis)
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
public sealed partial class PowerRelations
{
    public const int N = FactionTable.MaxStory, MaxPacts = 32, MaxNews = 32;
    public const int ThinkSeconds = 5;
    /// <summary>A blood feud shows as this value (the grid sent to clients; the engine keeps the feud as a flag, <see cref="Feud"/>).</summary>
    public const int BloodValue = -100;
    private static int T => World.TickRate;

    /// <summary>Phase 3: a power gone for good (wiped out, or merged into another): it thinks no more, signs nothing, and nothing is posted to it.</summary>
    public readonly bool[] Gone = new bool[N + 1];
    /// <summary>Phase 3: a power's might (the controllers': ships and stock), for tribute and merging (null: all equal).</summary>
    public Func<byte, float>? Might;
    /// <summary>Phase 3: power a joins power b (both kindred long enough; a is the weaker): the controllers move its ships, then <see cref="Fall"/>.</summary>
    public Action<byte, byte>? Merge;
    private readonly long[] _kinSince = new long[N * N];

    /// <summary>How <paramref name="a"/> sees <paramref name="b"/> is a blood feud: no peace, ever.</summary>
    public bool Blood(byte a, byte b) => a != b && a > 0 && b > 0 && a <= N && b <= N && Feud[(a - 1) * N + b - 1];

    /// <summary>How a sees b is a blood feud (Karl's answer 2): set by a grave deed at the floor, saved (POWR version 2), never cleared but by a new world.
    /// A relation at -100 alone is no feud (worlds from before phase 3 clamped there).</summary>
    public readonly bool[] Feud = new bool[N * N];

    /// <summary>The value clients are shown: -100 only for a feud (others at -99 at worst), so the window's words need no flag on the wire.</summary>
    public sbyte Shown(int k) => Feud[k] ? (sbyte)BloodValue : (sbyte)Math.Max((int)Rel[k], BloodValue + 1);

    /// <summary>A gone power's heir: the power it merged into (0: wiped out, none).</summary>
    public readonly byte[] MergedInto = new byte[N + 1];

    public RelationRules Rules = RelationRules.Current;
    public FactionTable Table = new(); // the factions table whose NPC rows hold the standings with players (the Game sets its own)
    public IPowerSight? Sight;
    public Func<byte, string>? PartyName; // a player's or player faction's name for news (owner byte; a faction: 0x100 + id)
    public long Tick = 1; // (from 1: a tick of 0 is "never" in the stamps below)
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
        Array.Clear(MetFaction);
        Array.Clear(MetPlayer);
        ResetPowers();
    }

    /// <summary>The powers among themselves from their files: the grid, no pacts, wars, help or news (who has met whom is kept).</summary>
    public void ResetPowers()
    {
        Array.Clear(Rel);
        for (var a = 1; a <= Count; a++)
            for (var b = 1; b <= Count; b++)
            {
                var k = (a - 1) * N + b - 1;
                Rel[k] = Def(a)!.StartTo[b];
                Band[k] = Rules.Raw(Rel[k]);
            }
        Array.Clear(PactK);
        Array.Clear(_strayAt);
        Array.Clear(_strayHits);
        Array.Clear(_proposed);
        Array.Clear(_answerAfter);
        Array.Clear(_warSince);
        Array.Clear(_lastLoss);
        Array.Clear(_loss);
        Array.Clear(_helpAt);
        Array.Clear(_helpSum);
        Array.Clear(_decay);
        Array.Clear(_thought);
        Array.Clear(_kinSince);
        Array.Clear(Gone);
        Array.Clear(MergedInto);
        Array.Clear(Feud);
        Array.Clear(News);
        NewsTotal = 0;
        ResetParties(); // (phase 5: pacts with player factions, their feuds and contracts: CrewPartyPacts.cs)
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

    /// <summary>
    /// Whether a crew of <paramref name="a"/> fights a crew of <paramref name="b"/> that is fighting a crew of power <paramref name="theirPower"/> (0:
    /// none) or person <paramref name="theirPerson"/> (255: none): never under a pact; on sight below 0; and, not friends with b, when b fights a
    /// friend or ally of a's, or a person a counts as a friend (allies help players and each other in a fight they see: section 5.2, answer 9).
    /// </summary>
    public bool ShipFights(byte a, byte b, byte theirPower, byte theirPerson)
    {
        if (a == b || HoldsFire(a, b))
            return false;
        var v = Get(a, b);
        if (v < 0)
            return true;
        if (v >= Rules.FriendlyLine)
            return false;
        if (theirPower != 0 && theirPower != b && Helps(a, theirPower))
            return true;
        return theirPerson != 255 && (Peek(a, theirPerson) >= Rules.FriendlyLine || PartyAllied(a, theirPerson)); // (phase 5: an ally of their faction's fights its foes)
    }

    private readonly long[] _strayAt = new long[N * N];
    private readonly byte[] _strayHits = new byte[N * N];

    /// <summary>
    /// An attack by <paramref name="doer"/> on <paramref name="victim"/> while a pact holds between them or the victim counts the doer a friend:
    /// true while it is stray fire to forgive (a round or splash in a fight they share), up to <see cref="RelationRules.FriendlyFireHits"/> hits
    /// in a minute (a minute with none starts the count again); past that it is deliberate, and an attack. <paramref name="first"/>: the first of a run.
    /// </summary>
    public bool StrayFire(byte victim, byte doer, out bool first)
    {
        first = false;
        if (victim == doer || !HoldsFire(victim, doer) && BandOf(victim, doer) < RelBand.Friendly)
            return false;
        var k = (victim - 1) * N + doer - 1;
        if (Tick - _strayAt[k] > 60L * T)
            _strayHits[k] = 0;
        _strayAt[k] = Tick;
        if (_strayHits[k] < 255)
            _strayHits[k]++;
        first = _strayHits[k] == 1;
        if (_strayHits[k] == Rules.FriendlyFireHits + 1)
            Console.WriteLine($"[crew] {Name(doer)} keeps firing on {Name(victim)}: deliberate");
        return _strayHits[k] <= Rules.FriendlyFireHits;
    }

    /// <summary><paramref name="a"/> helps <paramref name="f"/> in a fight it sees: allied, or friendly.</summary>
    public bool Helps(byte a, byte f) => a != 0 && f != 0 && a != f && (PactOf(a, f) is PactKind.Alliance or PactKind.Vassalage || BandOf(a, f) >= RelBand.Friendly);

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

    private static bool Attack(RelEvent e) => e <= RelEvent.Capture || e is RelEvent.CaptureOfficer or RelEvent.RaidConvoy or RelEvent.TakeSite or RelEvent.TakePlanet;

    /// <summary>
    /// <paramref name="doerPower"/> (a power; 0: none) or <paramref name="doerPlayer"/> (a person; 255: none) did <paramref name="e"/> to power
    /// <paramref name="victim"/> at <paramref name="at"/>: the victim's view of the doer moves, a pact between them breaks on an attack, and powers
    /// that know of it (a crew of theirs sees the place, or they are the victim's allies) take a part: a friend of the victim's its loyalty's share,
    /// an enemy of the victim's the opposite fifth.
    /// </summary>
    public void Post(RelEvent e, byte victim, byte doerPower, byte doerPlayer, Vector3 at)
    {
        if (Def(victim) is not { } v || doerPower == victim || doerPower == 0 && doerPlayer == 255 || doerPower > Count || Gone[victim])
            return;
        var amount = Amount(e, v);
        if (doerPower != 0 && e is RelEvent.DestroyShip or RelEvent.Capture or RelEvent.CaptureOfficer or RelEvent.RaidConvoy or RelEvent.TakePlanet && Rel[(victim - 1) * N + doerPower - 1] <= BloodValue + 1 && v.Pride >= Rules.BloodPride && !Blood(victim, doerPower) && Might != null)
        {
            // A grave deed against a power already at the floor: a blood feud, for good (Karl's answer 2; only where the controllers run: Might set).
            Set(victim, doerPower, BloodValue);
            Feud[(victim - 1) * N + doerPower - 1] = true;
            Say($"{Name(victim)} swears a blood feud against {Name(doerPower)}");
        }
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
        {
            if (PlayerDeed(e, v, victim, doerPlayer, at))
                return; // (a pact partner's stray fire: forgiven, phase 5)
            MovePlayer(victim, doerPlayer, amount);
            Contracts.Deed(e, victim, doerPlayer, Table.Of[doerPlayer]); // (a raid contract done by it: Crew/CrewContracts.cs)
        }
        if (e is RelEvent.BreakPact or RelEvent.BreakPactHeard or RelEvent.KeepPact or RelEvent.PayTribute or RelEvent.AnswerCall or RelEvent.RefuseCall or RelEvent.ContractDone or RelEvent.ContractFailed)
            return;
        // Word gets around.
        for (var c = 1; c <= Count; c++)
        {
            if (c == victim || c == doerPower || Def(c) is not { Controller: true } cd || Gone[c])
                continue;
            var allied = PactOf((byte)c, victim) is PactKind.Alliance or PactKind.Vassalage;
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
        if (Feud[k])
            return; // (a blood feud never moves)
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
        else if (band == RelBand.Kindred)
            Say($"{Name(a)} would join {Name(b)} if they asked");
        else if (band == RelBand.Friendly && was < band)
            Say($"{Name(a)} now counts {Name(b)} as friends");
        else if (was >= RelBand.Friendly && band < RelBand.Friendly)
            Say($"{Name(a)} no longer counts {Name(b)} as friends");
        else if (band == RelBand.Hostile && was > band)
            Say($"{Name(a)} turns hostile to {Name(b)}");
    }

    /// <summary>How <paramref name="power"/> sees a person (their faction, else themselves) moves by <paramref name="delta"/>.</summary>
    public void MovePlayer(byte power, byte player, int delta)
    {
        if (player == 255 || Def(power) is null || delta == 0)
            return;
        if (Table.Of[player] is var party and not 0)
        {
            MoveParty(power, party, delta); // (their faction's standing: phase 5's pacts and feuds are a faction's: CrewPartyPacts.cs)
            return;
        }
        ref var v = ref Standing(power, player);
        var was = v;
        v = (sbyte)Math.Clamp(v + delta, BloodValue + 1, 100); // (-100 is a blood feud's alone, and a person in no faction swears none)
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

    public static string PactName(PactKind k) => k switch
    {
        PactKind.Ceasefire => "ceasefire",
        PactKind.NonAggression => "non-aggression pact",
        PactKind.Alliance => "alliance",
        PactKind.Trade => "trade agreement",
        PactKind.Tribute => "tribute",
        PactKind.Vassalage => "vassalage",
        _ => "pact",
    };

    /// <summary>The pact between <paramref name="a"/> and <paramref name="b"/> (and its index), for the controllers' payments.</summary>
    public int PactAt(byte a, byte b) => PactIndex(a, b);

    /// <summary>A controller ends pact <paramref name="i"/> (a tribute unpaid): news, no relation event but <paramref name="hurt"/> to the receiver's view of the payer.</summary>
    public void EndPact(int i, string why, int hurt = 0)
    {
        if (i < 0 || i >= MaxPacts || PactK[i] == PactKind.None)
            return;
        var (a, b) = (PactA[i], PactB[i]);
        End(i, why);
        if (hurt != 0)
            Move(b, a, hurt);
    }

    /// <summary>Power <paramref name="p"/> is gone for good (answer 5: wiped out; or merged): its pacts end, it thinks no more; news.</summary>
    public void Fall(byte p, string line, bool quiet = false, byte into = 0)
    {
        if (p == 0 || p > Count || Gone[p])
            return;
        Gone[p] = true;
        MergedInto[p] = into;
        for (var i = 0; i < MaxPacts; i++)
            if (PactK[i] != PactKind.None && (PactA[i] == p || PactB[i] == p))
                PactK[i] = PactKind.None;
        for (var i = 0; i < MaxPartyPacts; i++)
            if (PartyK[i] != PactKind.None && PartyPower[i] == p)
                PartyK[i] = PactKind.None;
        Version++;
        if (!quiet)
            Say(line);
    }

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
        if (kind is PactKind.Ceasefire or PactKind.Tribute)
        {
            foreach (var (x, y) in new[] { (a, b), (b, a) }) // (rare: a pact signed)
                if (Get(x, y) < Rules.CeasefireRelation)
                    Set(x, y, Rules.CeasefireRelation);
        }
        Version++;
        Say(kind switch
        {
            PactKind.Alliance => $"{Name(a)} and {Name(b)} are now allies",
            PactKind.Tribute => $"{Name(a)} pays tribute to {Name(b)} for peace",
            PactKind.Vassalage => $"{Name(a)} becomes a vassal of {Name(b)}",
            _ => $"{Name(a)} and {Name(b)} sign a {PactName(kind)}",
        });
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
            if (c != a && c != b && !Gone[c] && Get(a, (byte)c) < 0 && Get(b, (byte)c) < 0 && (!war || BandOf(a, (byte)c) == RelBand.War || BandOf(b, (byte)c) == RelBand.War))
                return true;
        return false;
    }

    /// <summary>Some power both <paramref name="a"/> and <paramref name="b"/> see below 0.</summary>
    public bool CommonEnemyOf(byte a, byte b) => CommonEnemy(a, b, war: false);

    private bool AllianceWanted(byte a, byte b) => BandOf(a, b) >= RelBand.Friendly && BandOf(b, a) >= RelBand.Friendly
        && Def(a)!.Loyalty >= Rules.AllianceLoyalty && Def(b)!.Loyalty >= Rules.AllianceLoyalty && CommonEnemy(a, b, war: true);

    private bool NonAggressionWanted(byte a, byte b) => BandOf(a, b) >= RelBand.Neutral && BandOf(b, a) >= RelBand.Neutral && CommonEnemy(a, b, war: false);

    private float MightOf(byte p) => Might?.Invoke(p) ?? 1;

    /// <summary>Trade (Karl's answer 2): both neutral or better, no war, and one of them greedy enough to want the business.</summary>
    private bool TradeWanted(byte a, byte b) => BandOf(a, b) >= RelBand.Neutral && BandOf(b, a) >= RelBand.Neutral
        && MathF.Max(Def(a)!.Greed, Def(b)!.Greed) >= Rules.TradeGreed && Might != null && MightOf(a) > 0 && MightOf(b) > 0; // (both with an economy)

    /// <summary>Tribute: <paramref name="a"/> would pay <paramref name="b"/> for peace: a ceasefire it wants, much the weaker, and not too proud.</summary>
    private bool TributeWanted(byte a, byte b) => Might != null && CeasefireWanted(a, b) && MightOf(a) < MightOf(b) * Rules.TributeWeaker && Def(a)!.Pride < Rules.TributePride;

    /// <summary>Vassalage: a tribute held long, the payer at least neutral to the receiver.</summary>
    private bool VassalWanted(byte a, byte b, int i) => PactK[i] == PactKind.Tribute && PactA[i] == a && Tick - PactStart[i] >= (long)Rules.VassalMinutes * 60 * T && BandOf(a, b) >= RelBand.Neutral;

    private bool CeasefireWanted(byte a, byte b)
    {
        var k = (a - 1) * N + b - 1;
        if (BandOf(a, b) != RelBand.War && BandOf(b, a) != RelBand.War || Blood(a, b) || Blood(b, a))
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
        if (!def.Controller || Gone[a])
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
            if (PactK[i] is PactKind.Alliance or PactKind.Vassalage or PactKind.Trade && (BandOf(a, o) < RelBand.Neutral || BandOf(o, a) < RelBand.Neutral)
                || PactK[i] == PactKind.NonAggression && (BandOf(a, o) == RelBand.War || BandOf(o, a) == RelBand.War))
            {
                End(i, "falls apart");
                continue;
            }
            if (PactK[i] == PactKind.Tribute && PactA[i] == a && BandOf(a, o) >= RelBand.Neutral && !VassalWanted(a, o, i) && BandOf(o, a) >= RelBand.Friendly)
            {
                End(i, "ends: the payer is free of it"); // (friends both ways: tribute is no longer asked)
                continue;
            }
            if (PactA[i] == a && (Tick - PactStart[i]) / (3600L * T) > PactKept[i])
            {
                PactKept[i]++;
                Move(a, o, Amount(RelEvent.KeepPact, def));
                Move(o, a, Amount(RelEvent.KeepPact, Def(o)!));
            }
            var ao = (a - 1) * N + o - 1;
            if (_proposed[ao] == PactKind.None && Tick >= _answerAfter[ao])
            {
                if (PactK[i] is PactKind.NonAggression or PactKind.Trade && AllianceWanted(a, o))
                    _proposed[ao] = PactKind.Alliance;
                else if (PactK[i] == PactKind.NonAggression && TradeWanted(a, o))
                    _proposed[ao] = PactKind.Trade;
                else if (VassalWanted(a, o, i))
                    _proposed[ao] = PactKind.Vassalage;
            }
        }
        PartyThink(a, def); // (phase 5: its pacts with player factions, their proposals and its offers: CrewPartyPacts.cs)
        for (var b = (byte)1; b <= Count; b++)
        {
            if (b == a || Def(b) is not { Controller: true } bd || Gone[b])
                continue;
            var ab = (a - 1) * N + b - 1;
            var ba = (b - 1) * N + a - 1;
            // Kindred both ways a long while: the weaker joins the stronger (Karl's answer 2: merging powers is a real outcome).
            if (BandOf(a, b) == RelBand.Kindred && BandOf(b, a) == RelBand.Kindred && Merge != null)
            {
                if (_kinSince[ab] == 0)
                    _kinSince[ab] = Tick;
                else if (Tick - _kinSince[ab] >= (long)Rules.MergeMinutes * 60 * T && MightOf(a) <= MightOf(b))
                {
                    _kinSince[ab] = 0;
                    Merge(a, b);
                    if (Gone[a])
                        return;
                }
            }
            else
                _kinSince[ab] = 0;
            // Answer b's proposal to a.
            if (_proposed[ba] is var kind and not PactKind.None)
            {
                _proposed[ba] = PactKind.None;
                var cur = PactOf(a, b);
                if (cur == kind)
                    continue; // (both proposed it at once: signed already)
                var yes = kind switch
                {
                    PactKind.Ceasefire => cur == PactKind.None && CeasefireWanted(b, a) && (def.Aggression < Rules.CeasefireRefuseAggression || _loss[ab] > 0),
                    PactKind.NonAggression => cur == PactKind.None && NonAggressionWanted(a, b),
                    PactKind.Alliance => cur is PactKind.None or PactKind.NonAggression or PactKind.Trade && AllianceWanted(a, b),
                    PactKind.Trade => cur is PactKind.None or PactKind.NonAggression && TradeWanted(a, b),
                    PactKind.Tribute => cur == PactKind.None && CeasefireWanted(b, a) && def.Greed >= Rules.TributeGreed,
                    PactKind.Vassalage => cur == PactKind.Tribute && def.Loyalty >= Rules.VassalLoyalty,
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
            var want = TributeWanted(a, b) ? PactKind.Tribute
                : CeasefireWanted(a, b) ? PactKind.Ceasefire
                : AllianceWanted(a, b) ? PactKind.Alliance
                : TradeWanted(a, b) ? PactKind.Trade
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
            if (PactOf(a, (byte)b) == PactKind.Ceasefire || Feud[k])
                continue; // (a ceasefire holds the grudge where it was set; a blood feud never fades)
            var to = Toward(Rel[k], def.RestTo[b], steps, Band[k] == RelBand.War);
            if (to != Rel[k])
                Move(a, (byte)b, to - Rel[k]);
        }
        var t = Table;
        var changed = false;
        for (var party = 1; party < FactionTable.Max; party++)
            if (MetFaction[party * N + a - 1] && !PartyFeud[party * N + a - 1]) // (a blood feud never fades)
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

    /// <summary>A line of news from outside the engine (the controllers: a convoy sent, a raid, a call answered).</summary>
    public void Tell(string line) => Say(line);

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

    public const byte SaveVersion = 3; // (2: blood feuds, a bit a pair, after the news; 3: phase 5's pacts with player factions, their feuds and the contracts)

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
        for (var a = 0; a < n; a++)
            for (var b = 0; b < n; b += 8)
            {
                var bits = 0;
                for (var j = 0; j < 8 && b + j < n; j++)
                    if (Feud[a * N + b + j])
                        bits |= 1 << j;
                w.U8((byte)bits);
            }
        WriteParties(w); // (version 3: CrewPartyPacts.cs)
        Contracts.TickNow = Tick;
        Contracts.Write(w);
        return w.Span.ToArray();
    }

    /// <summary>A saved world's relations over the powers' files (a power the save doesn't name keeps its start; one it names that isn't read is left out, with a line).</summary>
    public void Load(byte[] bytes)
    {
        try
        {
            var r = new SaveReader(bytes);
            var v = r.U8();
            if (v is not (1 or 2 or SaveVersion))
            {
                Console.WriteLine($"[crew] the world's powers section is of version {v} (this build reads 1 to {SaveVersion}): relations start from the power files");
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
                if (a >= n || b >= n || map[a] == 0 || map[b] == 0 || kind is not (PactKind.Ceasefire or PactKind.NonAggression or PactKind.Alliance or PactKind.Trade or PactKind.Tribute or PactKind.Vassalage) || i >= MaxPacts)
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
            Array.Clear(Feud); // (version 1, from before blood feuds: none, whatever its values)
            if (v >= 2)
                for (var a = 0; a < n; a++)
                    for (var b = 0; b < n; b += 8)
                    {
                        var bits = r.U8();
                        for (var j = 0; j < 8 && b + j < n; j++)
                            if ((bits >> j & 1) != 0 && map[a] != 0 && map[b + j] != 0 && map[a] != map[b + j])
                                Feud[(map[a] - 1) * N + map[b + j] - 1] = true;
                    }
            ResetParties(); // (version 2 and before: no pacts with player factions, no contracts)
            if (v >= 3)
            {
                ReadParties(r, map);
                Contracts.TickNow = Tick;
                Contracts.Read(r, map);
            }
            Version++;
        }
        catch (Exception ex) when (ex is WorldFileException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            Console.WriteLine($"[crew] the world's powers section doesn't read ({ex.Message}): the powers' relations among themselves, pacts and news start from the power files (standings with players are kept)");
            ResetPowers();
        }
    }
}
