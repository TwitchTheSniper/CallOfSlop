using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CallOfSlop;

/// <summary>
/// One place on a map where bots got stuck: every stuck spell (Game.BotStuckWatch) that reached recovery stage 1 or
/// more, or went straight to the last resort (off the nav area), within <see cref="StuckSpots.Radius"/> of the spot.
/// </summary>
public sealed class StuckSpot
{
    public string Map = "";
    public Vector3 At;
    public int NavNode = -1; // the nearest nav node when the spot was first seen (-1: none)
    public int Spells; // stuck spells that reached a recovery stage here (or gave up at once: off the nav area)
    public int SelfKills; // of those, how many ended in the self-kill
    public int Rescues; // ...or in a rescue teleport (Last Man Standing, Zombies co-op)
    public int MaxStage; // the furthest recovery stage reached here (3: every stage; 4: gave up)
    public readonly int[] Causes = new int[CauseCount]; // spells by BotStuckCause (the cause as the spell started, or as it gave up)
    public string Modes = ""; // the modes it was seen in ("Free-for-all, Team Deathmatch")

    public const int CauseCount = (int)BotStuckCause.Void + 1;

    /// <summary>The most common cause here.</summary>
    public BotStuckCause MainCause
    {
        get
        {
            var best = 0;
            for (var i = 1; i < Causes.Length; i++)
                if (Causes[i] > Causes[best])
                    best = i;
            return (BotStuckCause)best;
        }
    }
}

/// <summary>
/// The stuck-spot report: where bots got stuck this session, clustered by spot, so map problems can be found and fixed
/// instead of leaving the bots to kill themselves. The game records into <see cref="Session"/> (server, no allocation once a
/// spot is known), writes it to <c>Logs/stuck-spots-*.json</c> in the save folder at every match end, and prints a per-map
/// summary in the log; <c>--stuck-report DIR</c> merges such reports (and the <c>[bots] stuck:</c> lines of game logs) from
/// testers and CI runs into one table. The self-test writes its pooled report (every bot match of the run) as
/// <c>stuck-spots.json</c> beside <c>selftest.log</c>.
/// </summary>
public sealed class StuckSpots
{
    /// <summary>Spells this close (across the ground, and within <see cref="Height"/> up or down) are the same spot.</summary>
    public const float Radius = 2.5f;

    public const float Height = 2.5f;

    public const int Format = 1;

    /// <summary>This session's spots (every game the process runs records here).</summary>
    public static readonly StuckSpots Session = new();

    private readonly Dictionary<string, List<StuckSpot>> _byMap = new();

    /// <summary>Changes since the report was last written (the match end skips the write when nothing changed).</summary>
    public int Version;

    public IEnumerable<string> Maps => _byMap.Keys;

    public int Count
    {
        get
        {
            var n = 0;
            foreach (var l in _byMap.Values)
                n += l.Count;
            return n;
        }
    }

    public IReadOnlyList<StuckSpot> On(string map) => _byMap.TryGetValue(map, out var l) ? l : [];

    public IEnumerable<StuckSpot> All => _byMap.Values.SelectMany(l => l);

    public void Clear()
    {
        _byMap.Clear();
        Version++;
    }

    /// <summary>The spot at <paramref name="at"/> on <paramref name="map"/>, or a new one (null when <paramref name="create"/> is false).</summary>
    public StuckSpot? Find(string map, Vector3 at, bool create)
    {
        if (!_byMap.TryGetValue(map, out var list))
        {
            if (!create)
                return null;
            _byMap[map] = list = new List<StuckSpot>();
        }
        StuckSpot? best = null;
        var bestD = Radius * Radius;
        foreach (var s in list)
        {
            var d = s.At - at;
            var flat = d.X * d.X + d.Z * d.Z;
            if (flat <= bestD && MathF.Abs(d.Y) <= Height)
                (best, bestD) = (s, flat);
        }
        if (best != null || !create)
            return best;
        best = new StuckSpot { Map = map, At = at };
        list.Add(best);
        return best;
    }

    /// <summary>A new stuck spell at a spot (game: a bot reached recovery stage 1, or gave up at once).</summary>
    public StuckSpot Spell(string map, string mode, Vector3 at, BotStuckCause cause, int navNode)
    {
        var s = Find(map, at, create: true)!;
        if (s.Spells == 0)
            s.NavNode = navNode;
        s.Spells++;
        s.Causes[(int)cause]++;
        s.MaxStage = Math.Max(s.MaxStage, 1);
        if (!ContainsMode(s.Modes, mode))
            s.Modes = s.Modes.Length == 0 ? mode : s.Modes + ", " + mode; // (once per spot and mode)
        Version++;
        return s;
    }

    private static bool ContainsMode(string modes, string mode)
    {
        foreach (var part in modes.AsSpan().Split(", "))
            if (modes.AsSpan()[part].SequenceEqual(mode))
                return true;
        return false;
    }

    /// <summary>The spell at <paramref name="s"/> reached a further recovery stage.</summary>
    public void Stage(StuckSpot s, int stage)
    {
        if (stage > s.MaxStage)
        {
            s.MaxStage = stage;
            Version++;
        }
    }

    /// <summary>The spell at <paramref name="s"/> ended in the last resort (a self-kill, or a rescue teleport).</summary>
    public void GaveUp(StuckSpot s, BotStuckCause cause, BotStuckCause spellCause, bool rescue)
    {
        if (rescue)
            s.Rescues++;
        else
            s.SelfKills++;
        if (cause != spellCause)
        {
            s.Causes[(int)spellCause]--; // the cause as it gave up is the better guess
            s.Causes[(int)cause]++;
        }
        s.MaxStage = 4;
        Version++;
    }

    /// <summary>Adds another report's spots (clustered with these by spot).</summary>
    public void Merge(StuckSpot o)
    {
        var s = Find(o.Map, o.At, create: true)!;
        if (s.Spells == 0 && s.SelfKills == 0)
            s.NavNode = o.NavNode;
        s.Spells += o.Spells;
        s.SelfKills += o.SelfKills;
        s.Rescues += o.Rescues;
        s.MaxStage = Math.Max(s.MaxStage, o.MaxStage);
        for (var i = 0; i < s.Causes.Length; i++)
            s.Causes[i] += o.Causes[i];
        foreach (var m in o.Modes.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            if (!ContainsMode(s.Modes, m))
                s.Modes = s.Modes.Length == 0 ? m : s.Modes + ", " + m;
        Version++;
    }

    // ---- Text ----------------------------------------------------------------------------------

    public static string CauseName(BotStuckCause c) => c.ToString();

    /// <summary>One spot in a line: where, how often, the cause.</summary>
    public static string Describe(StuckSpot s) =>
        $"({s.At.X:0.0}, {s.At.Y:0.0}, {s.At.Z:0.0}) node {s.NavNode}: {s.Spells} spell(s), {s.SelfKills} self-kill(s){(s.Rescues > 0 ? $", {s.Rescues} rescue(s)" : "")}, "
        + $"stage {(s.MaxStage >= 4 ? "gave up" : s.MaxStage.ToString(CultureInfo.InvariantCulture))}, {CausesText(s)}";

    private static string CausesText(StuckSpot s)
    {
        var parts = new List<string>();
        for (var i = 0; i < s.Causes.Length; i++)
            if (s.Causes[i] > 0)
                parts.Add($"{(BotStuckCause)i} {s.Causes[i]}");
        return parts.Count == 0 ? "no cause" : string.Join(", ", parts);
    }

    /// <summary>The report as a table: per map, its spots (most spells first).</summary>
    public List<string> Table(int perMap = int.MaxValue)
    {
        var lines = new List<string>();
        foreach (var map in _byMap.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var spots = _byMap[map].OrderByDescending(s => s.Spells + s.SelfKills).ThenBy(s => s.At.X).ToList();
            if (spots.Count == 0)
                continue;
            lines.Add($"{map}: {spots.Count} spot(s), {spots.Sum(s => s.Spells)} spell(s), {spots.Sum(s => s.SelfKills)} self-kill(s)");
            foreach (var s in spots.Take(perMap))
                lines.Add("  " + Describe(s));
            if (spots.Count > perMap)
                lines.Add($"  ... and {spots.Count - perMap} more");
        }
        return lines;
    }

    // ---- Files ---------------------------------------------------------------------------------

    public string ToJson(string source)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("source", source);
            w.WriteString("build", BuildInfo.Label);
            w.WriteString("written", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            w.WriteStartArray("spots");
            foreach (var map in _byMap.Keys.Order(StringComparer.Ordinal))
                foreach (var s in _byMap[map])
                {
                    w.WriteStartObject();
                    w.WriteString("map", s.Map);
                    w.WriteStartArray("at");
                    w.WriteNumberValue(MathF.Round(s.At.X, 2));
                    w.WriteNumberValue(MathF.Round(s.At.Y, 2));
                    w.WriteNumberValue(MathF.Round(s.At.Z, 2));
                    w.WriteEndArray();
                    w.WriteNumber("node", s.NavNode);
                    w.WriteNumber("spells", s.Spells);
                    w.WriteNumber("selfKills", s.SelfKills);
                    w.WriteNumber("rescues", s.Rescues);
                    w.WriteNumber("maxStage", s.MaxStage);
                    w.WriteStartObject("causes");
                    for (var i = 0; i < s.Causes.Length; i++)
                        if (s.Causes[i] != 0)
                            w.WriteNumber(((BotStuckCause)i).ToString(), s.Causes[i]);
                    w.WriteEndObject();
                    w.WriteString("modes", s.Modes);
                    w.WriteEndObject();
                }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Reads a report written by <see cref="ToJson"/> into this one (merged). Returns the spots read; throws on a bad file.</summary>
    public int ReadJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (!root.TryGetProperty("spots", out var spots) || spots.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"spots\" list");
        var n = 0;
        foreach (var e in spots.EnumerateArray())
        {
            var at = e.GetProperty("at");
            var s = new StuckSpot
            {
                Map = e.GetProperty("map").GetString() ?? "",
                At = new Vector3(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle()),
                NavNode = e.TryGetProperty("node", out var node) ? node.GetInt32() : -1,
                Spells = e.TryGetProperty("spells", out var sp) ? sp.GetInt32() : 0,
                SelfKills = e.TryGetProperty("selfKills", out var sk) ? sk.GetInt32() : 0,
                Rescues = e.TryGetProperty("rescues", out var rs) ? rs.GetInt32() : 0,
                MaxStage = e.TryGetProperty("maxStage", out var ms) ? ms.GetInt32() : 0,
                Modes = e.TryGetProperty("modes", out var md) ? md.GetString() ?? "" : "",
            };
            if (e.TryGetProperty("causes", out var causes))
                foreach (var c in causes.EnumerateObject())
                    if (Enum.TryParse<BotStuckCause>(c.Name, out var cause))
                        s.Causes[(int)cause] += c.Value.GetInt32();
            Merge(s);
            n++;
        }
        return n;
    }

    private static readonly Regex LogLine = new(
        @"\[bots\] stuck: .*? on (?<map>.+?) \((?<mode>[^()]*)\) at \((?<x>-?[\d.]+), (?<y>-?[\d.]+), (?<z>-?[\d.]+)\): (?<cause>.+?) for [\d.]+ s -> (?<action>.+?) \(\d+ on this map",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A game log's <c>[bots] stuck:</c> line (a self-kill or rescue; logged at most once a minute per bot, so logs
    /// under-count) as a spot, or null. For logs from testers on builds without the report.
    /// </summary>
    public static StuckSpot? FromLogLine(string line)
    {
        var m = LogLine.Match(line);
        if (!m.Success)
            return null;
        var inv = CultureInfo.InvariantCulture;
        var s = new StuckSpot
        {
            Map = m.Groups["map"].Value,
            Modes = m.Groups["mode"].Value,
            At = new Vector3(float.Parse(m.Groups["x"].Value, inv), float.Parse(m.Groups["y"].Value, inv), float.Parse(m.Groups["z"].Value, inv)),
            Spells = 1,
            MaxStage = 4,
        };
        if (m.Groups["action"].Value.StartsWith("self-kill", StringComparison.Ordinal))
            s.SelfKills = 1;
        else
            s.Rescues = 1;
        s.Causes[(int)CauseFromText(m.Groups["cause"].Value)]++;
        return s;
    }

    private static BotStuckCause CauseFromText(string text)
    {
        foreach (BotStuckCause c in Enum.GetValues<BotStuckCause>())
            if (c != BotStuckCause.None && Game.StuckCauseText(c) == text)
                return c;
        return BotStuckCause.Blocked;
    }

    /// <summary>This session's report file in the save folder (one per session, rewritten at each match end).</summary>
    public static string SessionFile => _sessionFile ??= Path.Combine(SavePaths.Folder, "Logs", $"stuck-spots-{DateTime.Now:yyyyMMdd-HHmmss}.json");

    private static string? _sessionFile;

    private int _writtenVersion = -1;

    /// <summary>Writes the session report if anything changed since the last write (match end). False on a write error (logged).</summary>
    public bool WriteSession()
    {
        if (Version == _writtenVersion || Count == 0)
            return true;
        _writtenVersion = Version;
        return Write(SessionFile, "game session");
    }

    /// <summary>
    /// Match end: <see cref="WriteSession"/> on a worker (the JSON is taken here, on the game thread; the disk write, which
    /// flushes, never holds a tick up).
    /// </summary>
    public void WriteSessionInBackground()
    {
        if (Version == _writtenVersion || Count == 0)
            return;
        _writtenVersion = Version;
        var path = SessionFile;
        var json = ToJson("game session");
        Task.Run(() => WriteText(path, json));
    }

    public bool Write(string path, string source) => WriteText(path, ToJson(source));

    private static bool WriteText(string path, string json)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            return SavePaths.WriteAtomic(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[bots] couldn't write the stuck-spot report {path}: {ex.Message}");
            return false;
        }
    }

    // ---- CLI: --stuck-report DIR [--out FILE] --------------------------------------------------------

    /// <summary>
    /// Merges every stuck-spot report (<c>stuck-spots*.json</c>) and the <c>[bots] stuck:</c> lines of every <c>*.log</c>
    /// under <paramref name="dir"/> (a tester's save folder, CI artifacts, a mix) and prints the per-map table. With
    /// <c>--out FILE</c> it writes the merged report too. Exit code: 0, or 1 when nothing could be read.
    /// </summary>
    public static int Cli(string dir, IReadOnlyList<string> args, TextWriter? output = null)
    {
        if (!Directory.Exists(dir) && !File.Exists(dir))
        {
            Console.WriteLine($"--stuck-report: no such folder or file: {dir}");
            return 1;
        }
        using var stdout = output == null ? new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true } : null; // (Console goes to callofslop.log)
        var say = (TextWriter?)stdout ?? output!;
        void Say(string line)
        {
            say.WriteLine(line);
            if (output == null)
                Console.WriteLine(line);
        }
        var ownLog = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "callofslop.log")); // this run's own log: nothing to read
        var all = new StuckSpots();
        var files = File.Exists(dir) ? [dir] : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        int reports = 0, logs = 0, logLines = 0, bad = 0;
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (Path.GetFullPath(f) == ownLog)
                continue;
            try
            {
                if (name.StartsWith("stuck-spots", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    all.ReadJson(File.ReadAllText(f));
                    reports++;
                }
                else if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    var any = false;
                    foreach (var line in File.ReadLines(f))
                        if (line.Contains("[bots] stuck: ", StringComparison.Ordinal) && FromLogLine(line) is { } s)
                        {
                            all.Merge(s);
                            logLines++;
                            any = true;
                        }
                    if (any)
                        logs++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                Say($"skipped {f}: {ex.Message}");
                bad++;
            }
        }
        Say($"Stuck-spot report: {reports} report(s), {logLines} [bots] stuck: line(s) from {logs} log(s){(bad > 0 ? $", {bad} unreadable file(s)" : "")}");
        foreach (var line in all.Table())
            Say(line);
        if (all.Count == 0)
            Say("no stuck spots");
        if (args.SkipWhile(a => !a.Equals("--out", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } outFile)
        {
            if (!all.Write(outFile, $"--stuck-report {dir}"))
                return 1;
            Say($"merged report written to {outFile}");
        }
        return reports + logs > 0 ? 0 : 1;
    }
}
