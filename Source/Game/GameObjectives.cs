using System.Text;
using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The "what next" line (docs/story/OBJECTIVES.md; Karl, 2026-11-13): once the warden's induction and the first duty are
/// over, a story player always has a current goal on the HUD, from <c>Story/Objectives.json</c>: the first goal in the list
/// they haven't met yet. Each goal is judged from the game's own state, never ticked off by hand: standing with a faction,
/// the Orbital Depot's run, a craft of their own with thrusters and a driving seat, having left the planetoid, having landed
/// on another, a Shell Control Chair and a Robot Shell of their own. The host judges everyone once a second and tells each
/// person their goal (<see cref="Msg.Objective"/>) only when it changes. Nothing is shown while a story panel has the screen
/// (the induction, the quarry, the depot), during the opening, or on a planetoid's own game. Survival story worlds only.
/// Server state is fixed arrays by player id and one walk over the bodies a second: nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const byte ObjectiveNone = 255;

    private readonly byte[] _objSent = new byte[256]; // the goal each person was last told (ObjectiveNone: none)
    private readonly short[] _objHave = new short[256], _objNeed = new short[256];
    private readonly Player?[] _objWho = new Player?[256]; // whose the slot is (a new Player object in it: a new person)
    private readonly byte[] _objFirsts = new byte[256]; // firsts seen while here (bits of ObjectiveKind): see ObjectiveFirst
    private readonly byte[] _objOwns = new byte[256]; // this second's walk: what each owner byte owns (bits of ObjectiveKind)
    private double _objNextLook;
    private bool _objWantChairs; // someone's goal is the shell: the walk looks through parts for chairs

    // ---- Every peer: this PC's own goal, as the server tells it
    public byte ObjectiveLocal { get; private set; } = ObjectiveNone;
    public int ObjectiveHave { get; private set; }
    public int ObjectiveNeed { get; private set; }
    public double ObjectiveChangedAt { get; private set; }

    /// <summary>The goal before this one, while its "done" line shows (ObjectiveNone: none).</summary>
    public byte ObjectiveMet { get; private set; } = ObjectiveNone;

    /// <summary>The goal this PC's player has now, or null.</summary>
    public ObjectiveGoal? ObjectiveNow => ObjectiveLocal < ObjectiveScript.Current.Goals.Length ? ObjectiveScript.Current.Goals[ObjectiveLocal] : null;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): nobody has a goal.</summary>
    private void ObjectivesReset()
    {
        Array.Fill(_objSent, ObjectiveNone);
        Array.Clear(_objWho);
        Array.Clear(_objFirsts);
        ObjectiveLocal = ObjectiveMet = ObjectiveNone;
    }

    /// <summary>Server, every tick in Sandbox: once a second, each person's goal; told to them when it changes.</summary>
    private void ObjectivesTick(double now)
    {
        if (now < _objNextLook && now >= _objNextLook - 2)
            return;
        _objNextLook = now + 1;
        var goals = ObjectiveScript.Current.Goals;
        var on = goals.Length > 0 && Story != null && !SandboxInfiniteParts && Host == null && Phase == MatchPhase.Playing;
        if (on)
            ObjectivesWalk();
        var wantChairs = false;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            if (_objWho[p.Id] != p)
            {
                _objWho[p.Id] = p;
                _objFirsts[p.Id] = 0;
                _objSent[p.Id] = 254; // (not told yet: anything below is news)
            }
            byte goal = ObjectiveNone;
            int have = 0, need = 0;
            if (on && ObjectiveShows(p))
                for (var k = 0; k < goals.Length && k < ObjectiveNone; k++)
                    if (!ObjectiveDone(p, goals[k], out have, out need))
                    {
                        goal = (byte)k;
                        wantChairs |= goals[k].Kind == ObjectiveKind.Shell;
                        break;
                    }
            if (goal == _objSent[p.Id] && (goal == ObjectiveNone || have == _objHave[p.Id] && need == _objNeed[p.Id]))
                continue;
            _objSent[p.Id] = goal;
            _objHave[p.Id] = (short)Math.Clamp(have, short.MinValue, short.MaxValue);
            _objNeed[p.Id] = (short)Math.Clamp(need, short.MinValue, short.MaxValue);
            ObjectiveSend(p);
        }
        _objWantChairs = wantChairs;
    }

    /// <summary>Is the line for them now? Not while a story panel has the screen (the induction, the quarry's or the depot's steps) or the opening has them.</summary>
    private bool ObjectiveShows(Player p)
    {
        var id = p.Id;
        if (_tutState[id] is not (TutorialState.Finished or TutorialState.Skipped) || OpeningHas(id))
            return false;
        if (_qrWho[id] == p && _qr[id].State is not (QuarryNone or QuarryDone))
            return false;
        return _dpWho[id] != p || _dp[id].State is DepotNone or DepotDone;
    }

    /// <summary>Has this person met the goal? (have, need: the numbers its line shows, for a standing goal.)</summary>
    private bool ObjectiveDone(Player p, ObjectiveGoal g, out int have, out int need)
    {
        have = need = 0;
        switch (g.Kind)
        {
            case ObjectiveKind.Standing:
            {
                if (Story is not { } s || StoryWho(p) is not (var who and >= 0))
                    return true; // (the story doesn't know them yet: nothing to say)
                var f = s.Factions.Find(g.Faction);
                if (f < 0)
                    return true;
                var tiers = s.Factions.Def(f).Tiers;
                var t = -1;
                for (var i = 0; i < tiers.Length && t < 0; i++)
                    if (tiers[i].Name.Equals(g.Tier, StringComparison.OrdinalIgnoreCase))
                        t = i;
                if (t <= 0)
                    return true; // (no such tier, or the lowest: met by everyone)
                need = tiers[t - 1].Below;
                have = s.Factions.Standing(f, s.Factions.Party(who));
                return have >= need;
            }
            case ObjectiveKind.Depot:
                return _dpDepot == 0 || _dpWho[p.Id] == p && _dp[p.Id].State == DepotDone;
            default:
                return ObjectiveFirst(p, g.Kind);
        }
    }

    /// <summary>
    /// Server: has this person done a first (a craft of their own, off the planetoid, landed on another, a shell and chair of
    /// their own)? Seen from what they own and where they are, kept while they are here. The technologies thread's
    /// milestones (saved with the world) take this over once they are in.
    /// </summary>
    private bool ObjectiveFirst(Player p, ObjectiveKind k)
    {
        var bit = (byte)(1 << (int)k);
        if ((_objFirsts[p.Id] & bit) != 0)
            return true;
        var now = k switch
        {
            ObjectiveKind.Ship => (_objOwns[p.Id] & bit) != 0,
            ObjectiveKind.Shell => (_objOwns[p.Id] & bit) != 0,
            ObjectiveKind.Space => ObjectiveInSpace(p) || ObjectiveAway(p),
            ObjectiveKind.Landing => ObjectiveAway(p),
            _ => false,
        };
        if (now)
            _objFirsts[p.Id] |= bit;
        return now;
    }

    /// <summary>On another planetoid: away on one the host runs, or the whole game landed somewhere other than the story's home.</summary>
    private bool ObjectiveAway(Player p) =>
        p.AwayOn is { } on && on != "space" && !on.Equals(StoryHomeMap, StringComparison.OrdinalIgnoreCase)
        || !World.Map.Name.Equals(StoryHomeMap, StringComparison.OrdinalIgnoreCase);

    /// <summary>Sitting in a construct that is out in space.</summary>
    private bool ObjectiveInSpace(Player p)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        return h.Seated && Constructs.Get(h.SeatBody) is { } c && InSpace(c);
    }

    /// <summary>Server, once a second: what each owner byte owns, one walk over the bodies (the parts only for chairs, when someone wants one).</summary>
    private void ObjectivesWalk()
    {
        Array.Clear(_objOwns);
        const byte ship = 1 << (int)ObjectiveKind.Ship, shell = 1 << (int)ObjectiveKind.Shell, chair = 1 << 7;
        Span<byte> shells = stackalloc byte[256];
        shells.Clear();
        foreach (var c in Constructs.Bodies)
        {
            var o = c.Owner;
            if (o == 255 || c.PartCount == 0)
                continue;
            if (c.ThrusterCount > 0 && c.DriveSeat >= 0 && (_dpWho[o] == null || _dp[o].Lander != c.Id))
                _objOwns[o] |= ship; // (a craft of their own: not the lander the depot lent them)
            if (c.PartCount == 1 && c.Parts[0].D.ShellBody)
                shells[o] = 1;
            else if (_objWantChairs && (_objOwns[o] & chair) == 0)
                for (var k = 0; k < c.PartCount; k++)
                    if (c.Parts[k].D.ShellRange > 0)
                    {
                        _objOwns[o] |= chair;
                        break;
                    }
        }
        for (var o = 0; o < 255; o++)
            if (shells[o] != 0 && (_objOwns[o] & chair) != 0)
                _objOwns[o] |= shell;
    }

    private void ObjectiveSend(Player p)
    {
        var (goal, have, need) = (_objSent[p.Id], _objHave[p.Id], _objNeed[p.Id]);
        if (p.Id == LocalId)
        {
            ObjectiveHear(goal, have, need);
            return;
        }
        if (GuidOf(p) is not { } guid)
            return;
        var w = SbW;
        w.Begin(Msg.Objective).U8(goal).U16(unchecked((ushort)have)).U16(unchecked((ushort)need));
        Net.SendTo(guid, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.Objective"/> (after its id).</summary>
    private void ReadObjective(ref PacketReader r)
    {
        var goal = r.U8();
        var have = unchecked((short)r.U16());
        var need = unchecked((short)r.U16());
        ObjectiveHear(goal, have, need);
    }

    private void ObjectiveHear(byte goal, int have, int need)
    {
        if (goal != ObjectiveLocal)
        {
            ObjectiveMet = ObjectiveLocal != ObjectiveNone && goal != ObjectiveNone && goal > ObjectiveLocal ? ObjectiveLocal : ObjectiveNone;
            ObjectiveChangedAt = Brutal.GlfwApi.Glfw.Time;
        }
        ObjectiveLocal = goal;
        ObjectiveHave = have;
        ObjectiveNeed = need;
    }
}

/// <summary>What a goal is judged by (a first's bit in the server's masks is its number).</summary>
public enum ObjectiveKind : byte
{
    Standing, Depot, Ship, Space, Landing, Shell,
}

/// <summary>A goal of <c>Story/Objectives.json</c>: what it is judged by, and its words.</summary>
public sealed class ObjectiveGoal
{
    public string Id = "", Title = "", Text = "", Memo = "", Hint = "";
    public ObjectiveKind Kind;
    public string Faction = "", Tier = ""; // a standing goal's
}

/// <summary>The goals, in order (<c>Story/Objectives.json</c>, format <see cref="Format"/>). A bad file is refused with its reason: no goals.</summary>
public sealed class ObjectiveScript
{
    public const string Format = "call-of-slop-objectives/1";
    public const int MaxGoals = 32;

    public string Voice = "", Problem = "";
    public float HintAfter = 45;
    public ObjectiveGoal[] Goals = [];

    private static ObjectiveScript? _current;
    public static ObjectiveScript Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Objectives.json"));

    public static ObjectiveScript Load(string path)
    {
        try
        {
            var o = Parse(File.ReadAllText(path));
            Console.WriteLine($"[objectives] {o.Goals.Length} goals");
            return o;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            Console.WriteLine($"[objectives] refused: {ex.Message}");
            return new ObjectiveScript { Problem = $"Objectives.json: {ex.Message}" };
        }
    }

    public static ObjectiveScript Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        static string Str(JsonElement e, string key, string where, bool need)
        {
            if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString() ?? "";
                foreach (var ch in s)
                    if (ch is < ' ' or > '~')
                        throw new FormatException($"{where}.{key}: player text is ASCII");
                return s;
            }
            return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
        }
        var o = new ObjectiveScript { Voice = Str(root, "voice", "the file", false) };
        if (root.TryGetProperty("hintAfter", out var ha) && ha.ValueKind == JsonValueKind.Number)
            o.HintAfter = Math.Clamp(ha.GetSingle(), 0, 3600);
        if (!root.TryGetProperty("goals", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"goals\" list");
        var goals = new List<ObjectiveGoal>();
        foreach (var e in list.EnumerateArray())
        {
            var where = $"goals[{goals.Count}]";
            var g = new ObjectiveGoal
            {
                Id = Str(e, "id", where, true), Title = Str(e, "title", where, true), Text = Str(e, "text", where, true),
                Memo = Str(e, "memo", where, false), Hint = Str(e, "hint", where, false),
            };
            var kind = Str(e, "kind", where, true);
            if (!Enum.TryParse(kind, true, out g.Kind) || int.TryParse(kind, out _))
                throw new FormatException($"{where} ({g.Id}): no such kind \"{kind}\" (standing, depot, ship, space, landing, shell)");
            if (g.Kind == ObjectiveKind.Standing)
            {
                g.Faction = Str(e, "faction", where, true);
                g.Tier = Str(e, "tier", where, true);
            }
            goals.Add(g);
            if (goals.Count > MaxGoals)
                throw new FormatException($"more than {MaxGoals} goals");
        }
        o.Goals = goals.ToArray();
        return o;
    }

    /// <summary>A goal's words with {have} and {need} filled, then the player's keys (<see cref="Tutorial.Fill"/>). Made once per change.</summary>
    public static string Fill(string text, int have, int need, Settings? s)
    {
        if (text.Contains("{have}") || text.Contains("{need}"))
            text = new StringBuilder(text).Replace("{have}", have.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{need}", need.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
        return Tutorial.Fill(text, s);
    }
}
