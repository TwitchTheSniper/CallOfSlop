using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What happens to a gang now and then (docs/story/LABOUR.md "Incidents"; the wire byte, append only).</summary>
public enum LabourIncidentKind : byte
{
    None,
    Collapse, // one inmate goes down and sits slumped in the snow; the guard stands over them until they get up
    Scuffle, // two side by side shove each other; the guard comes over and they stand with hands on heads
    Search, // the guard pats one down, arms out, and confiscates something small
    Count,
}

/// <summary>
/// Incidents in the colony's gangs (docs/story/LABOUR.md "Incidents"; Karl, 2026-11-14: make the colony interesting): now and
/// then, in front of a player standing near a gang that has a guard, one of the gang collapses, two of them scuffle, or the
/// guard searches one. Shown, not told: the gang's place stands still while it plays (the count goes on, the work goes on),
/// the others look, and the guard says one thing to those close by. Host only, apart from <see cref="Msg.LabourIncident"/>
/// (u8 scene, u8 kind, u8 slot, u32 the tick it began), which every PC poses from (Render/LabourRender.cs). Nothing saved;
/// nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const float IncidentNear = 24f; // m: a gang this close to a player, in front of them, may have one
    public const float IncidentHeard = 22f; // m: the guard's words reach the chat this far
    public const double IncidentEveryMin = 75, IncidentEveryMax = 140; // s between incidents round the colony

    /// <summary>Each scene's incident (both sides): its kind (None: none), its inmate and the server tick it began.</summary>
    public readonly LabourIncidentKind[] LabourIncident = new LabourIncidentKind[LabourScenesMax];
    public readonly byte[] LabourIncidentSlot = new byte[LabourScenesMax];
    public readonly uint[] LabourIncidentTick = new uint[LabourScenesMax];
    private double _liNext = 60, _liThenAt = double.PositiveInfinity;
    private int _liThenScene = -1;
    private PacketWriter? _liW;

    /// <summary>How long an incident lasts (s), the same on every PC.</summary>
    public static float IncidentSeconds(LabourIncidentKind k) => k switch
    {
        LabourIncidentKind.Collapse => 21f,
        LabourIncidentKind.Scuffle => 17f,
        LabourIncidentKind.Search => 17f,
        _ => 0f,
    };

    /// <summary>When the guard says the second thing (s into the incident).</summary>
    private static float IncidentThen(LabourIncidentKind k) => k switch
    {
        LabourIncidentKind.Collapse => 14f,
        LabourIncidentKind.Scuffle => 6.5f,
        _ => 10.5f,
    };

    /// <summary>Which incidents a gang of that kind can have (none for those that walk off, sit or eat).</summary>
    public static bool IncidentFits(LabourIncidentKind i, in LabourScene s) => s.Guards > 0 && i switch
    {
        LabourIncidentKind.Collapse => s.Kind is LabourKind.RollCall or LabourKind.Queue or LabourKind.Stand or LabourKind.Shovel or LabourKind.Pick or LabourKind.Hammer or LabourKind.March,
        LabourIncidentKind.Scuffle => s.Inmates >= 2 && s.Kind is LabourKind.RollCall or LabourKind.Queue or LabourKind.Stand or LabourKind.Huddle,
        LabourIncidentKind.Search => s.Kind is LabourKind.RollCall or LabourKind.Queue or LabourKind.Stand or LabourKind.Huddle or LabourKind.March,
        _ => false,
    };

    /// <summary>Is a scene's incident playing at this server time (s)?</summary>
    public bool IncidentOn(int scene, double serverSeconds) =>
        (uint)scene < LabourScenesMax && LabourIncident[scene] != LabourIncidentKind.None
        && serverSeconds - LabourIncidentTick[scene] / (double)World.TickRate is var it && it >= 0 && it < IncidentSeconds(LabourIncident[scene]);

    /// <summary>Server, from <see cref="LabourTick"/> (every 2 s): perhaps an incident in front of someone; the guard's second line when due.</summary>
    private void IncidentTick(double now)
    {
        if (_liThenScene >= 0 && now >= _liThenAt)
        {
            IncidentSay(_liThenScene, then: true, now);
            _liThenScene = -1;
        }
        if (now < _liNext || LabourCount == 0 || ColonyAlarmOn || LabourIncidents.Current.Problem.Length > 0)
            return;
        var at = ServerTick / (double)World.TickRate;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers || !p.Alive || OpeningHas(p.Id))
                continue;
            var look = GravityFrames.FlatForward(p.Position, p.Yaw);
            var best = -1;
            var bestD = IncidentNear * IncidentNear;
            for (var k = 0; k < LabourCount; k++)
            {
                ref readonly var s = ref LabourScenes[k];
                if (!LabourOnShift(k) || s.Guards == 0 || IncidentOn(k, at) || LabourHandIn(k))
                    continue;
                var d = s.P0 - p.Position;
                var d2 = d.LengthSquared();
                if (d2 >= bestD || d2 < 4f || Vector3.Dot(look, d / MathF.Sqrt(d2)) < 0.55f)
                    continue;
                best = k;
                bestD = d2;
            }
            if (best < 0)
                continue;
            ref readonly var g = ref LabourScenes[best];
            var kind = LabourIncidentKind.None;
            for (var tries = 0; tries < 6 && kind == LabourIncidentKind.None; tries++)
            {
                var pick = (LabourIncidentKind)(1 + _rng.Next((int)LabourIncidentKind.Count - 1));
                if (IncidentFits(pick, g))
                    kind = pick;
            }
            if (kind == LabourIncidentKind.None)
                continue;
            IncidentBegin(best, kind, now);
            Log($"[labour] an incident: {kind} in gang {best} ({LabourScript.Current.Scenes[Math.Clamp(_lbGang[best], 0, LabourScript.Current.Scenes.Length - 1)].Name}), in front of {p.Name}");
            return;
        }
    }

    /// <summary>Server: an incident of that kind begins in that gang now (one inmate picked), is sent and the guard speaks.</summary>
    private void IncidentBegin(int scene, LabourIncidentKind kind, double now)
    {
        ref readonly var g = ref LabourScenes[scene];
        var slot = g.Inmates <= 1 ? 0 : _rng.Next(kind == LabourIncidentKind.Scuffle ? g.Inmates - 1 : g.Inmates);
        if (g.Kind == LabourKind.Queue && slot == 0 && g.Inmates > 2)
            slot = 1; // (not the one at the front, who is about to go round)
        LabourIncident[scene] = kind;
        LabourIncidentSlot[scene] = (byte)slot;
        LabourIncidentTick[scene] = ServerTick;
        IncidentSend(scene);
        IncidentSay(scene, then: false, now);
        _liThenScene = scene;
        _liThenAt = now + IncidentThen(kind);
        _liNext = now + IncidentEveryMin + _rng.NextDouble() * (IncidentEveryMax - IncidentEveryMin);
    }

    /// <summary>
    /// Server, the window tool's "incident GANG KIND": that incident begins now in the named gang (on shift, with a guard, and a
    /// kind it can have). Null when it began, else why not.
    /// </summary>
    public string? IncidentStartNamed(string gang, string kind)
    {
        if (!Enum.TryParse<LabourIncidentKind>(kind, true, out var k) || k is LabourIncidentKind.None or LabourIncidentKind.Count)
            return $"no incident \"{kind}\" (collapse, scuffle, search)";
        var gangs = LabourScript.Current.Scenes;
        for (var i = 0; i < LabourCount; i++)
        {
            if (_lbGang[i] < 0 || _lbGang[i] >= gangs.Length || !gangs[_lbGang[i]].Name.Equals(gang, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!LabourOnShift(i) || !IncidentFits(k, LabourScenes[i]))
                return $"gang \"{gang}\" is off shift or can't have a {k}";
            IncidentBegin(i, k, SandboxNow);
            Log($"[labour] an incident: {k} in gang {i} ({gangs[_lbGang[i]].Name}), asked for");
            return null;
        }
        return $"no gang \"{gang}\" stands now";
    }

    /// <summary>Server: does a story person work in that gang now (it is theirs: no incident)?</summary>
    private bool LabourHandIn(int scene)
    {
        for (var h = 0; h < LabourHandCount; h++)
            if (LabourHandScene[h] == scene)
                return true;
        return false;
    }

    /// <summary>Server: the guard's words, in the chat of each player within <see cref="IncidentHeard"/> of the gang.</summary>
    private void IncidentSay(int scene, bool then, double now)
    {
        if ((uint)scene >= LabourCount || LabourIncident[scene] is not (var kind and not LabourIncidentKind.None))
            return;
        var lines = LabourIncidents.Current.Lines(kind, then);
        if (lines.Length == 0)
            return;
        ref readonly var s = ref LabourScenes[scene];
        var who = (s.Flags & LabourScene.FlagForeman) != 0 ? "Foreman" : s.Kind == LabourKind.RollCall ? "Sergeant" : "Officer";
        var text = lines[(int)(LabourIncidentTick[scene] / 7 % (uint)lines.Length)];
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers || OpeningHas(p.Id))
                continue;
            if (Vector3.DistanceSquared(p.Position, s.P0) <= IncidentHeard * IncidentHeard)
                OpeningSay(p, null, who, text, now);
        }
    }

    /// <summary>Server: an incident to everyone: u8 scene, u8 kind, u8 slot, u32 its first tick.</summary>
    private void IncidentSend(int scene)
    {
        if (Net.IsOffline || !IsServer)
            return;
        var w = _liW ??= new PacketWriter(16);
        w.Begin(Msg.LabourIncident).U8((byte)scene).U8((byte)LabourIncident[scene]).U8(LabourIncidentSlot[scene]).U32(LabourIncidentTick[scene]);
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.LabourIncident"/> (after its id).</summary>
    private void ReadLabourIncident(ref PacketReader r)
    {
        if (!r.Has(7))
            return;
        var scene = r.U8();
        var kind = r.U8();
        var slot = r.U8();
        var tick = r.U32();
        if (scene >= LabourScenesMax || kind >= (byte)LabourIncidentKind.Count)
            return;
        LabourIncident[scene] = (LabourIncidentKind)kind;
        LabourIncidentSlot[scene] = slot;
        LabourIncidentTick[scene] = tick;
    }

    /// <summary>Both sides, a new gang list: no incidents.</summary>
    private void IncidentsClear() => Array.Clear(LabourIncident);
}

/// <summary>
/// The guards' words in an incident (Story/Labour/Incidents.json, format <see cref="Format"/>): for each kind, what is said
/// as it begins ("first") and later ("then"), one picked by the incident's tick. A bad file is refused with a reason (the
/// log's <c>[labour]</c> line) and there are no incidents.
/// </summary>
public sealed class LabourIncidents
{
    public const string Format = "call-of-slop-incidents/1";
    private readonly string[][] _first = new string[(int)LabourIncidentKind.Count][], _then = new string[(int)LabourIncidentKind.Count][];
    public string Problem = "";

    private static LabourIncidents? _current;
    public static LabourIncidents Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Labour", "Incidents.json"));

    public string[] Lines(LabourIncidentKind k, bool then) => (then ? _then : _first)[(int)k] ?? [];

    public static LabourIncidents Load(string path)
    {
        var l = new LabourIncidents();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"format must be \"{Format}\"");
            for (var k = LabourIncidentKind.Collapse; k < LabourIncidentKind.Count; k++)
            {
                var key = k.ToString().ToLowerInvariant();
                if (!root.TryGetProperty(key, out var e))
                    throw new FormatException($"no \"{key}\"");
                l._first[(int)k] = Strings(e, "first", key);
                l._then[(int)k] = Strings(e, "then", key);
            }
            Console.WriteLine("[labour] incidents' words read");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            l.Problem = $"Incidents.json: {ex.Message}";
            Console.WriteLine($"[labour] refused: {l.Problem}");
        }
        return l;
    }

    private static string[] Strings(System.Text.Json.JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var a) || a.ValueKind != System.Text.Json.JsonValueKind.Array || a.GetArrayLength() == 0)
            throw new FormatException($"{where}: \"{key}\" is a list of lines");
        var list = new List<string>();
        foreach (var s in a.EnumerateArray())
        {
            var t = s.GetString() ?? "";
            if (t.Length == 0 || t.Length > LabourScript.MaxCall || t.Any(c => c < ' ' || c > '~'))
                throw new FormatException($"{where}.{key}: \"{t}\" (1 to {LabourScript.MaxCall} ASCII characters)");
            list.Add(t);
        }
        return [.. list];
    }
}
