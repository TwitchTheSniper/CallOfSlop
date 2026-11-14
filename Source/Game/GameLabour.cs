using System.Numerics;
using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a gang of the colony's convict labourers is doing (Story/Labour/PenalColony.json; the wire byte, append only).</summary>
public enum LabourKind : byte
{
    Stand, Shovel, Pick, Hammer, Haul, Barrow, Queue, RollCall, Huddle, March, Sweep, Paint, Sit,
    Eat, // sat on stools down both sides of a table, spooning from a bowl (the canteen at dinner)
    Patrol, // officers only, walking a way round and round with a torch (lock-up)
    Count,
}

/// <summary>
/// One gang of the penal colony's convict labourers where it works (docs/story/LABOUR.md), resolved by the host and the
/// same on every PC: what they do, how many inmates and guards, its points in the world (one spot, or a way to walk), the
/// way they face and how far apart they stand.
/// </summary>
public struct LabourScene
{
    public const int MaxPoints = 8;

    public LabourKind Kind;
    public byte Inmates, Guards, Seed, Flags;
    public float Gap; // m between them (a row, a queue, a ring's radius)
    public Vector3 Facing; // the way the gang faces at its first point (along the ground)
    public byte PointCount;
    public Vector3 P0, P1, P2, P3, P4, P5, P6, P7; // (fixed: no array per scene)

    public const byte FlagSnow = 1, FlagCoal = 2, FlagRock = 3, FlagHeapMask = 3, FlagBrazier = 4, FlagLoaded = 8, FlagForeman = 16;

    public readonly Vector3 Point(int i) => i switch { 0 => P0, 1 => P1, 2 => P2, 3 => P3, 4 => P4, 5 => P5, 6 => P6, _ => P7 };

    public void SetPoint(int i, Vector3 v)
    {
        switch (i)
        {
            case 0: P0 = v; break;
            case 1: P1 = v; break;
            case 2: P2 = v; break;
            case 3: P3 = v; break;
            case 4: P4 = v; break;
            case 5: P5 = v; break;
            case 6: P6 = v; break;
            default: P7 = v; break;
        }
    }
}

/// <summary>
/// The convict labourers of the Ministry of Slop's penal colony (docs/story/LABOUR.md; Karl, 2026-11-13: more people at
/// the story's start, doing things): gangs shovelling snow and coal, a roll call on the muster square, queues at the
/// canteen and the records office, a column marched along the street under guard, men huddled at the heating mains,
/// kerbs painted white in the snow, a pile of rocks carried to another pile and back, picks and barrows at the quarry.
/// They are pictures, like the stand-in craft: scripted figures with no AI, no physics, no collision and nothing saved,
/// so they cost a box draw each and no bot. The host works out where each gang stands from the data file and the
/// colony's and the quarry's constructs, and sends the list once (<see cref="Msg.Labour"/>, and to each joiner); every PC
/// poses them from the server tick (Render/LabourRender.cs). While the colony's alarm sounds, the inmates kneel with
/// their hands on their heads.
/// </summary>
public sealed partial class Game
{
    public const int LabourScenesMax = 48; // (at most 64: the gangs on shift go as one u64)
    public const double LabourLookEvery = 2; // s between the host's looks for the colony and the quarry
    public const float LabourCallReach = 140; // m from the colony: the tannoy's call is heard (in the chat) this far

    /// <summary>The gangs now (both sides: the host's own, or what a client heard). <see cref="LabourVersion"/> changes with them.</summary>
    public readonly LabourScene[] LabourScenes = new LabourScene[LabourScenesMax];
    public int LabourCount { get; private set; }
    public int LabourVersion { get; private set; }
    private Map? _lbMap;
    private double _lbAt = double.NegativeInfinity;
    private ushort _lbColony, _lbWorks;
    private PacketWriter? _lbW;

    /// <summary>The host's switch (the Story window's People tab): off, there are no labourers on any PC.</summary>
    public bool LabourOn = true;

    /// <summary>
    /// The colony's day (both sides): the timetable's period now (Story/Labour/PenalColony.json "day"; -1 before the colony
    /// stands) and which gangs are on shift in it, a bit per scene. <see cref="LabourDayVersion"/> changes with them.
    /// </summary>
    public int LabourPeriod { get; private set; } = -1;
    public ulong LabourShift { get; private set; } = ulong.MaxValue;
    public int LabourDayVersion { get; private set; }
    private double _lbDayFrom; // server: when the day began (the colony stood: its first period)
    private PacketWriter? _lbDayW;

    /// <summary>Is that scene's gang on shift now (both sides)?</summary>
    public bool LabourOnShift(int scene) => (uint)scene < 64 && (LabourShift >> scene & 1) != 0;

    /// <summary>Are the gangs listed this map's?</summary>
    public bool LabourOf(Map map) => _lbMap == map;

    /// <summary>Server, every Sandbox tick: once the story colony (and later its quarry) stands, the gangs are worked out and sent.</summary>
    private void LabourTick(double now)
    {
        if (now - _lbAt < LabourLookEvery)
            return;
        _lbAt = now;
        LabourDayTick(now);
        IncidentTick(now); // (now and then something happens in a gang: GameLabourIncidents.cs)
        if (_lbMap != World.Map)
        {
            _lbMap = World.Map;
            _lbColony = _lbWorks = 0;
            if (LabourCount > 0)
            {
                LabourCount = 0;
                LabourVersion++;
                LabourSend(null);
            }
        }
        var colony = LabourOn ? _opColony : (ushort)0;
        var works = LabourOn ? _qrWorks : (ushort)0;
        if (colony == _lbColony && works == _lbWorks)
            return;
        if (!LabourOn)
        {
            _lbColony = _lbWorks = 0;
            if (LabourCount > 0)
            {
                LabourCount = 0;
                LabourVersion++;
                LabourSend(null);
            }
            return;
        }
        if (colony != 0 && Constructs.Get(colony) == null)
            colony = 0;
        if (works != 0 && Constructs.Get(works) == null)
            works = 0;
        if (colony == _lbColony && works == _lbWorks)
            return;
        if (colony != 0 && _lbColony == 0)
            _lbDayFrom = now; // (the colony stands: its day begins with the morning count)
        _lbColony = colony;
        _lbWorks = works;
        LabourResolve();
    }

    /// <summary>Server: every gang of the file whose place stands now, to every client.</summary>
    private void LabourResolve()
    {
        var script = LabourScript.Current;
        LabourCount = 0;
        IncidentsClear();
        if (script.Problem.Length == 0 && _lbColony != 0)
            for (var gi = 0; gi < script.Scenes.Length; gi++)
            {
                var g = script.Scenes[gi];
                if (LabourCount >= LabourScenesMax)
                    break;
                if (!LabourFrame(g, out var origin, out var ax, out var az))
                    continue;
                var s = new LabourScene { Kind = g.Kind, Inmates = g.Inmates, Guards = g.Guards, Seed = (byte)(LabourCount * 37 + 11), Flags = g.Flags, Gap = g.Gap };
                var n = Math.Min(g.Points.Length, LabourScene.MaxPoints);
                for (var i = 0; i < n; i++)
                {
                    var w = origin + ax * g.Points[i].X + az * g.Points[i].Y;
                    s.SetPoint(i, StandInPath.Ground(w + StandInPath.Up(w) * 30f));
                }
                s.PointCount = (byte)n;
                _nsGang = g.Name;
                if (n >= 2)
                    if (LabourRoute(ref s, s.Kind is LabourKind.March or LabourKind.Patrol)) // (round the buildings, once the paths' graph is built: GameNavSites.cs)
                        Log($"[paths] {g.Name}: its way fitted round the buildings ({n} points, now {s.PointCount})");
                var (sin, cos) = MathF.SinCos(g.Face * MathF.PI / 180f);
                s.Facing = StandInPath.Along(ax * sin - az * cos, s.P0);
                _lbGang[LabourCount] = (short)gi; // (which gang of the file: a labour post finds its gang by name, GameLabourHands.cs)
                LabourScenes[LabourCount++] = s;
            }
        LabourVersion++;
        var people = 0;
        for (var i = 0; i < LabourCount; i++)
            people += LabourScenes[i].Inmates + LabourScenes[i].Guards;
        Log($"[labour] {LabourCount} gangs, {people} people round the colony{(script.Problem.Length > 0 ? $" ({script.Problem})" : "")}");
        LabourSend(null);
        LabourPeriod = -1; // (the shift worked out again for the new list and sent after it: a client takes every gang as on until it hears)
        LabourDayTick(SandboxNow);
    }

    /// <summary>
    /// Server: where a gang's spots are: the world place of its frame's origin and its x and z axes (unit, in the world).
    /// "colony" is the Penal Colony construct's frame, a building its own blueprint's, "quarry" the Mine Face's (x along the
    /// face, z out from it); with a part, the origin is that part's place.
    /// </summary>
    private bool LabourFrame(LabourScript.Scene g, out Vector3 origin, out Vector3 ax, out Vector3 az)
    {
        origin = ax = az = default;
        if (g.At.Equals("quarry", StringComparison.OrdinalIgnoreCase))
        {
            if (Constructs.Get(_lbWorks) is not { } w)
                return false;
            for (var k = 0; k < w.PartCount; k++)
            {
                if (w.DefOf(k).Name != "Mine Face")
                    continue;
                var q = Rot24.Quat(w.Parts[k].Rot);
                origin = w.SegToWorld(w.Parts[k].Seg, w.Parts[k].Pos);
                ax = Vector3.Normalize(w.SegToWorld(w.Parts[k].Seg, w.Parts[k].Pos + Vector3.Transform(Vector3.UnitX, q)) - origin);
                az = Vector3.Normalize(w.SegToWorld(w.Parts[k].Seg, w.Parts[k].Pos + Vector3.Transform(-Vector3.UnitZ, q)) - origin);
                return true;
            }
            return false;
        }
        var c = g.At.Equals("colony", StringComparison.OrdinalIgnoreCase) ? Constructs.Get(_lbColony) : ColonyBuilding(g.At);
        if (c == null)
            return false;
        origin = c.ToWorld(Vector3.Zero);
        ax = Vector3.Normalize(c.ToWorld(Vector3.UnitX) - origin);
        az = Vector3.Normalize(c.ToWorld(Vector3.UnitZ) - origin);
        if (g.Part.Length == 0)
            return true;
        var nth = g.Nth;
        for (var k = 0; k < c.PartCount; k++)
            if (c.DefOf(k).Name.Equals(g.Part, StringComparison.OrdinalIgnoreCase) && nth-- == 0)
            {
                origin = c.SegToWorld(c.Parts[k].Seg, c.Parts[k].Pos);
                return true;
            }
        return false;
    }

    // ---- The wire (Msg.Labour) ------------------------------------------------------------------------------------

    /// <summary>
    /// Server: the whole list to everyone (<paramref name="to"/> null) or one joiner: u8 n, each (u8 kind, u8 inmates,
    /// u8 guards, u8 seed, u8 flags, u8 gap in 10 cm, vec facing, u8 points, vec each).
    /// </summary>
    private void LabourSend(RakNetGUID? to)
    {
        if (Net.IsOffline || !IsServer)
            return;
        var w = _lbW ??= new PacketWriter(2048);
        w.Begin(Msg.Labour).U8((byte)LabourCount);
        for (var i = 0; i < LabourCount; i++)
        {
            ref readonly var s = ref LabourScenes[i];
            w.U8((byte)s.Kind).U8(s.Inmates).U8(s.Guards).U8(s.Seed).U8(s.Flags).U8((byte)Math.Clamp(MathF.Round(s.Gap * 10f), 1, 255)).Vec(s.Facing).U8(s.PointCount);
            for (var k = 0; k < s.PointCount; k++)
                w.Vec(s.Point(k));
        }
        if (to is { } g)
            Net.SendTo(g, w.Span, PacketReliability.ReliableOrdered);
        else
            Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: a joiner hears the gangs.</summary>
    private void SendLabourState(RakNetGUID to)
    {
        if (LabourCount > 0)
        {
            LabourSend(to);
            LabourDaySend(to); // (which of them are on shift: GameLabourDay.cs)
        }
        if (LabourHandCount > 0)
            LabourHandsSend(to); // (who works among them: GameLabourHands.cs)
    }

    /// <summary>Client: a <see cref="Msg.Labour"/> (after its id): the whole list again.</summary>
    private void ReadLabour(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        var n = Math.Min((int)r.U8(), LabourScenesMax);
        var count = 0;
        LabourShift = ulong.MaxValue; // (until the host says which are on shift: Msg.LabourDay follows the list)
        IncidentsClear();
        for (var i = 0; i < n; i++)
        {
            if (!r.Has(19))
                break;
            var s = new LabourScene
            {
                Kind = (LabourKind)Math.Min(r.U8(), (byte)(LabourKind.Count - 1)),
                Inmates = Math.Min(r.U8(), LabourScript.MaxPerGang),
                Guards = Math.Min(r.U8(), (byte)4),
                Seed = r.U8(),
                Flags = r.U8(),
                Gap = r.U8() / 10f,
                Facing = r.Vec(),
            };
            var pts = Math.Min((int)r.U8(), LabourScene.MaxPoints);
            if (!r.Has(pts * 12))
                break;
            var ok = float.IsFinite(s.Facing.X + s.Facing.Y + s.Facing.Z) && s.Facing.LengthSquared() > 0.25f;
            for (var k = 0; k < pts; k++)
            {
                var v = r.Vec();
                ok &= float.IsFinite(v.X + v.Y + v.Z);
                s.SetPoint(k, v);
            }
            s.PointCount = (byte)pts;
            if (ok && pts > 0)
                LabourScenes[count++] = s;
        }
        if (count != LabourCount)
            Log($"[labour] heard {count} gangs from the host");
        LabourCount = count;
        LabourVersion++;
        _lbMap = World.Map;
    }
}

/// <summary>
/// The labour file (Story/Labour/PenalColony.json, format <see cref="Format"/>; docs/story/LABOUR.md): each gang's place,
/// what it does and how many. A bad file is refused with a reason, and then there is nobody.
/// </summary>
public sealed class LabourScript
{
    public const string Format = "call-of-slop-labour/1";
    public const byte MaxPerGang = 16;
    public const int MaxPeople = 120; // on shift at once
    public const int MaxListed = 220; // in the whole file
    public const int MaxPeriods = 16;
    public const int MaxCall = ChatRules.MaxLength - 20; // a tannoy call's characters ("[MINISTRY TANNOY]: " goes before it, in a chat line of ChatRules.MaxLength)

    /// <summary>A period of the colony's day: its name, how long it lasts and what the tannoy calls when it begins.</summary>
    public sealed class Period
    {
        public string Name = "", Tannoy = "";
        public double Seconds;
    }

    public sealed class Scene
    {
        public string Name = "", About = "", At = "colony", Part = "";
        public int Nth;
        public LabourKind Kind;
        public byte Inmates, Guards, Flags;
        public float Face, Gap;
        public Vector2[] Points = [];
        public uint Periods = uint.MaxValue; // the day's periods it works in, a bit each (all: no "when")
    }

    public Scene[] Scenes = [];
    public Period[] Day = []; // (none: every gang works all the time)
    public string Problem = "";

    private static LabourScript? _current;
    public static LabourScript Current => _current ??= Load(Path.Combine(AppFolders.Data, "Story", "Labour", "PenalColony.json"));

    public static LabourScript Load(string path)
    {
        try
        {
            var s = Parse(File.ReadAllText(path));
            Console.WriteLine($"[labour] {s.Scenes.Length} gangs");
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Console.WriteLine($"[labour] refused: {ex.Message}");
            return new LabourScript { Problem = $"PenalColony.json: {ex.Message}" };
        }
    }

    /// <summary>Gaps by kind when the file gives none (m: a row's spacing, a queue's, a ring's radius).</summary>
    private static float DefaultGap(LabourKind k) => k switch
    {
        LabourKind.Queue => 0.85f,
        LabourKind.RollCall => 1.1f,
        LabourKind.March => 1.15f,
        LabourKind.Huddle => 0.95f,
        LabourKind.Hammer => 1.05f,
        LabourKind.Eat => 0.8f,
        LabourKind.Haul or LabourKind.Barrow or LabourKind.Sweep or LabourKind.Paint or LabourKind.Patrol => 0f, // (spread along the way by time)
        _ => 1.7f,
    };

    public static LabourScript Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        if (!root.TryGetProperty("gangs", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"gangs\" list");
        var day = new List<Period>();
        if (root.TryGetProperty("day", out var dl))
        {
            if (dl.ValueKind != JsonValueKind.Array)
                throw new FormatException("\"day\" is a list of periods");
            foreach (var e in dl.EnumerateArray())
            {
                var where = $"day[{day.Count}]";
                var pd = new Period
                {
                    Name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "",
                    Tannoy = e.TryGetProperty("tannoy", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() ?? "" : "",
                    Seconds = (e.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : 0) * 60,
                };
                if (pd.Name.Length == 0)
                    throw new FormatException($"{where}: no \"name\"");
                if (pd.Tannoy.Length > MaxCall)
                    throw new FormatException($"{where} ({pd.Name}): \"tannoy\" at most {MaxCall} characters (the chat's line, with the speaker's name before it)");
                if (pd.Seconds is < 30 or > 7200)
                    throw new FormatException($"{where} ({pd.Name}): \"minutes\" 0.5 to 120");
                if (day.Exists(x => x.Name.Equals(pd.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"{where}: a second period called \"{pd.Name}\"");
                day.Add(pd);
            }
            if (day.Count > MaxPeriods)
                throw new FormatException($"at most {MaxPeriods} periods in the day");
        }
        var scenes = new List<Scene>();
        var people = 0;
        foreach (var e in list.EnumerateArray())
        {
            var where = $"gangs[{scenes.Count}]";
            string Str(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            float Num(string key, float def) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : def;
            var g = new Scene { Name = Str("name"), About = Str("about"), At = Str("at"), Part = Str("part") };
            where = g.Name.Length > 0 ? $"{where} ({g.Name})" : where;
            if (g.At.Length == 0)
                g.At = "colony";
            var kind = Str("do");
            if (!Enum.TryParse(kind, true, out g.Kind) || int.TryParse(kind, out _) || g.Kind == LabourKind.Count)
                throw new FormatException($"{where}: no such \"do\" \"{kind}\" ({string.Join(", ", Enum.GetNames<LabourKind>()[..^1]).ToLowerInvariant()})");
            g.Nth = (int)Num("nth", 0);
            var patrol = g.Kind == LabourKind.Patrol;
            var inmates = Num("inmates", patrol ? 0 : 1);
            var guards = Num("guards", patrol ? 2 : 0);
            if (patrol ? inmates != 0 || guards is < 1 or > 4 : inmates is < 1 or > MaxPerGang || guards is < 0 or > 4)
                throw new FormatException(patrol ? $"{where}: a patrol is officers only: guards 1 to 4, no inmates" : $"{where}: inmates 1 to {MaxPerGang}, guards 0 to 4");
            g.Inmates = (byte)inmates;
            g.Guards = (byte)guards;
            people += g.Inmates + g.Guards;
            g.Face = Num("face", 0);
            g.Gap = Num("gap", DefaultGap(g.Kind));
            if (g.Gap is < 0 or > 25)
                throw new FormatException($"{where}: gap 0 to 25 m");
            var pts = new List<Vector2>();
            if (e.TryGetProperty("spot", out var sp))
                pts.Add(Vec2(sp, where + ".spot"));
            if (e.TryGetProperty("way", out var way) && way.ValueKind == JsonValueKind.Array)
                foreach (var p in way.EnumerateArray())
                    pts.Add(Vec2(p, where + ".way"));
            if (pts.Count == 0)
                throw new FormatException($"{where}: no \"spot\" or \"way\"");
            if (pts.Count > LabourScene.MaxPoints)
                throw new FormatException($"{where}: at most {LabourScene.MaxPoints} points");
            var walks = g.Kind is LabourKind.Haul or LabourKind.Barrow or LabourKind.March or LabourKind.Sweep or LabourKind.Paint or LabourKind.Patrol;
            if (walks && pts.Count < 2)
                throw new FormatException($"{where}: \"{kind}\" walks a way: give \"way\" (two points or more)");
            g.Points = [.. pts];
            var heap = Str("heap");
            g.Flags = heap.ToLowerInvariant() switch
            {
                "" => 0,
                "snow" => LabourScene.FlagSnow,
                "coal" => LabourScene.FlagCoal,
                "rock" => LabourScene.FlagRock,
                _ => throw new FormatException($"{where}: heap is snow, coal or rock"),
            };
            if (e.TryGetProperty("brazier", out var bz) && bz.ValueKind == JsonValueKind.True)
                g.Flags |= LabourScene.FlagBrazier;
            var guard = Str("guard");
            if (guard.Equals("foreman", StringComparison.OrdinalIgnoreCase))
                g.Flags |= LabourScene.FlagForeman;
            else if (guard.Length > 0 && !guard.Equals("officer", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"{where}: guard is officer or foreman");
            if (e.TryGetProperty("loaded", out var ld) && ld.ValueKind == JsonValueKind.True)
                g.Flags |= LabourScene.FlagLoaded;
            if (e.TryGetProperty("when", out var wh))
            {
                if (wh.ValueKind != JsonValueKind.Array || wh.GetArrayLength() == 0)
                    throw new FormatException($"{where}: \"when\" is a list of the day's periods");
                g.Periods = 0;
                foreach (var w in wh.EnumerateArray())
                {
                    var name = w.ValueKind == JsonValueKind.String ? w.GetString() ?? "" : "";
                    var k = day.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (k < 0)
                        throw new FormatException($"{where}: \"when\" names \"{name}\", which isn't a period of the \"day\"");
                    g.Periods |= 1u << k;
                }
            }
            scenes.Add(g);
        }
        if (scenes.Count > Game.LabourScenesMax)
            throw new FormatException($"at most {Game.LabourScenesMax} gangs");
        if (people > MaxListed)
            throw new FormatException($"at most {MaxListed} people in the file (there are {people})");
        for (var k = 0; k < Math.Max(day.Count, 1); k++)
        {
            var at = 0;
            foreach (var g in scenes)
                if ((g.Periods >> k & 1) != 0)
                    at += g.Inmates + g.Guards;
            if (at > MaxPeople)
                throw new FormatException($"at most {MaxPeople} people at once ({(day.Count > 0 ? day[k].Name : "all day")} has {at})");
        }
        return new LabourScript { Scenes = [.. scenes], Day = [.. day] };
    }

    private static Vector2 Vec2(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new FormatException($"{where}: [x, z] in metres");
        var v = new Vector2(e[0].GetSingle(), e[1].GetSingle());
        if (!float.IsFinite(v.X + v.Y) || MathF.Abs(v.X) > 400 || MathF.Abs(v.Y) > 400)
            throw new FormatException($"{where}: within 400 m of the frame's origin");
        return v;
    }
}
