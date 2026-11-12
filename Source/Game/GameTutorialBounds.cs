using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A tutorial step's play area (a step's "area", Story/Tutorial/Bounds.json). A byte in data only: never on the wire.</summary>
public enum PlayArea : byte
{
    None,
    Colony, // the penal colony and its bus stop at the gate
    Quarry, // the Ministry Quarry's works, the mine face and its bus stop
}

/// <summary>One area's words and size, as Bounds.json has them.</summary>
public sealed class PlayAreaDef
{
    public string Name = "";
    public float Margin = 25;
    public string[] Lines = [];
}

/// <summary>
/// Story/Tutorial/Bounds.json (format call-of-slop-bounds/1): the play areas' words and margins, loaded once, the same on every
/// peer. A bad file is refused with a reason and there are no bounds (the steps run as before).
/// </summary>
public static class PlayAreas
{
    public const string Format = "call-of-slop-bounds/1";

    public static string Speaker { get; private set; } = "[MINISTRY TANNOY]";
    public static string Back { get; private set; } = "";
    public static double Repeat { get; private set; } = 15;
    public static float Slack { get; private set; } = 4;
    public static string? Error { get; private set; }
    private static readonly PlayAreaDef?[] _areas = new PlayAreaDef?[3];
    private static bool _loaded;

    /// <summary>An area's definition, or null (none, or the file was refused).</summary>
    public static PlayAreaDef? Of(PlayArea a)
    {
        EnsureLoaded();
        return (int)a < _areas.Length ? _areas[(int)a] : null;
    }

    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = Path.Combine(AppFolders.Data, "Story", "Tutorial", "Bounds.json");
        try
        {
            Parse(File.ReadAllText(file));
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Array.Clear(_areas);
            Error = $"Bounds.json: {ex.Message}";
            Console.WriteLine($"[tutorial] refused: {Error}");
        }
    }

    private static void Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        static string Str(JsonElement e, string key, string where) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? Ascii(v.GetString() ?? "", $"{where}.{key}") : "";
        static float Num(JsonElement e, string key, float fallback, float lo, float hi, string where)
        {
            if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
                return fallback;
            var n = v.GetSingle();
            return n >= lo && n <= hi ? n : throw new FormatException($"{where}.{key}: {lo} to {hi}");
        }
        if (Str(root, "speaker", "file") is { Length: > 0 } sp)
            Speaker = sp;
        Back = Str(root, "back", "file");
        Repeat = Num(root, "repeat", 15, 5, 120, "file");
        Slack = Num(root, "slack", 4, 0, 20, "file");
        if (!root.TryGetProperty("areas", out var areas) || areas.ValueKind != JsonValueKind.Object)
            throw new FormatException("no \"areas\"");
        foreach (var a in areas.EnumerateObject())
        {
            if (!Enum.TryParse<PlayArea>(a.Name, true, out var kind) || kind == PlayArea.None || int.TryParse(a.Name, out _))
                throw new FormatException($"no such area \"{a.Name}\" (colony, quarry)");
            var def = new PlayAreaDef { Name = Str(a.Value, "name", a.Name), Margin = Num(a.Value, "margin", 25, 5, 200, a.Name) };
            if (def.Name.Length == 0)
                def.Name = "the " + a.Name.ToLowerInvariant();
            if (a.Value.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                def.Lines = lines.EnumerateArray().Select(l => l.ValueKind == JsonValueKind.String ? Ascii(l.GetString() ?? "", $"{a.Name}.lines") : throw new FormatException($"{a.Name}.lines holds text")).Where(l => l.Length > 0).ToArray();
            if (def.Lines.Length == 0)
                throw new FormatException($"{a.Name}: no \"lines\"");
            _areas[(int)kind] = def;
        }
    }

    /// <summary>A step's "area" (empty: none), or a FormatException.</summary>
    public static PlayArea Named(string name, string where) => name.Length == 0 ? PlayArea.None
        : Enum.TryParse<PlayArea>(name, true, out var a) && a != PlayArea.None && !int.TryParse(name, out _) ? a
        : throw new FormatException($"{where}: no such area \"{name}\" (colony, quarry)");

    private static string Ascii(string s, string where)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                throw new FormatException($"{where}: player text is ASCII only");
        return s;
    }
}

/// <summary>
/// The tutorial's play areas (Karl, 2026-11-12; docs/sandbox/TUTORIAL.md "Play areas"): a step of the induction or of a
/// Ministry duty can name an area (the colony, the quarry). On the host, ten times a second, someone on such a step who is
/// outside it is told by the Ministry's tannoy to go back, their marker points the way, and their step waits: its cues aren't
/// judged and actions they do out there don't count (<see cref="PlayAreaLets"/>). A step they have done (waiting for a group)
/// or a ride under way has no area. Nothing allocated.
/// </summary>
public sealed partial class Game
{
    private readonly bool[] _paOut = new bool[MaxPlayers];
    private readonly double[] _paSaidAt = new double[MaxPlayers];
    private readonly byte[] _paLine = new byte[MaxPlayers];
    private double _paNextLook;

    /// <summary>This PC's player is outside their step's play area (the server says so in <see cref="Msg.Quarry"/>).</summary>
    public bool PlayAreaOutLocal { get; private set; }

    /// <summary>Server: the area this person's step keeps them in now, or None.</summary>
    private PlayArea PlayAreaFor(Player p)
    {
        var id = p.Id;
        if (id >= MaxPlayers || !p.Alive)
            return PlayArea.None;
        var riding = _qrWho[id] == p && (_qr[id].RideFrom != 0 || _qr[id].Rode);
        if (riding)
            return PlayArea.None; // (the bus has them, or has just set them down at the other stop)
        if (_tutWho[id] == p && _tutState[id] == TutorialState.Running)
            return _tutStep[id] < Tutorial.Steps.Length && !_tutDone[id] ? Tutorial.Steps[_tutStep[id]].Area : PlayArea.None;
        if (_qrWho[id] != p)
            return PlayArea.None;
        ref var run = ref _qr[id];
        var steps = QuarryScript.Current.Steps;
        if (run.State is < QuarryFirstStep or QuarryDone || run.State - QuarryFirstStep >= steps.Length)
            return PlayArea.None;
        var step = steps[run.State - QuarryFirstStep];
        return step.Cue == QuarryCue.Ride && (run.Saw & QuarryBit(QuarryCue.Ride)) != 0 ? PlayArea.None : step.Area;
    }

    /// <summary>Server: an area's middle, how far it reaches, and where the marker sends someone back to (its bus stop). False: this world hasn't one.</summary>
    private bool PlayAreaBounds(PlayArea a, out Vector3 middle, out float radius, out Vector3 back)
    {
        middle = back = default;
        radius = 0;
        if (PlayAreas.Of(a) is not { } def)
            return false;
        switch (a)
        {
            case PlayArea.Colony:
                if (Constructs.Get(_opColony) is not { } colony)
                    return false;
                middle = _opMiddle;
                back = QuarryColonyStop();
                radius = MathF.Max(colony.Radius, Flat(back - middle)) + def.Margin;
                return true;
            case PlayArea.Quarry:
                if (QuarryWorks is not { } works)
                    return false;
                middle = works.WorldCom;
                back = _qrStop;
                radius = MathF.Max(works.Radius, Flat(back - middle)) + def.Margin;
                return true;
        }
        return false;

        static float Flat(Vector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    /// <summary>How far out of an area's middle someone is, across the ground (a cliff or a hole doesn't count as away).</summary>
    private static float PlayAreaFrom(Vector3 at, Vector3 middle)
    {
        var d = at - middle;
        return MathF.Sqrt(d.X * d.X + d.Z * d.Z);
    }

    /// <summary>Server: whether what this person does now counts for their step (inside its area, or the step has none).</summary>
    private bool PlayAreaLets(Player p)
    {
        if (p.Id >= MaxPlayers)
            return true;
        if (_paOut[p.Id])
            return false;
        var a = PlayAreaFor(p);
        return a == PlayArea.None || !PlayAreaBounds(a, out var middle, out var radius, out _) || PlayAreaFrom(p.Position, middle) <= radius;
    }

    /// <summary>Server: is this person held outside their step's area (the step waits)?</summary>
    private bool PlayAreaHolds(byte id) => id < MaxPlayers && _paOut[id];

    /// <summary>Server, each Sandbox tick (ten looks a second): who has left their step's area, or come back into it.</summary>
    private void PlayAreaTick(double now)
    {
        if (now < _paNextLook && now >= _paNextLook - 1)
            return;
        _paNextLook = now + 0.1;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.Id >= MaxPlayers)
                continue;
            var id = p.Id;
            var a = PlayAreaFor(p);
            var outside = false;
            if (a != PlayArea.None && PlayAreaBounds(a, out var middle, out var radius, out _))
            {
                var d = PlayAreaFrom(p.Position, middle);
                outside = _paOut[id] ? d > radius - PlayAreas.Slack : d > radius;
            }
            if (outside == _paOut[id])
            {
                if (outside && now - _paSaidAt[id] >= PlayAreas.Repeat)
                    PlayAreaSay(p, a, now);
                continue;
            }
            _paOut[id] = outside;
            if (outside)
            {
                PlayAreaSay(p, a, now);
                Log($"[tutorial] {p.Name} left {PlayAreas.Of(a)?.Name ?? "the area"}: the step waits");
            }
            else if (a != PlayArea.None && PlayAreas.Back.Length > 0)
                OpeningSay(p, null, PlayAreas.Speaker, PlayAreas.Back, now);
            if (!outside && a != PlayArea.None)
            {
                _tutLastYaw[id] = p.Yaw; // (turning or walking out there doesn't count for a look or move step)
                _tutLastPos[id] = p.Position;
            }
            QuarrySend(p); // (the marker points back, and their panel says so)
        }
    }

    private void PlayAreaSay(Player p, PlayArea a, double now)
    {
        if (PlayAreas.Of(a) is not { Lines.Length: > 0 } def)
            return;
        var id = p.Id;
        _paSaidAt[id] = now;
        OpeningSay(p, null, PlayAreas.Speaker, def.Lines[_paLine[id] % def.Lines.Length], now);
        _paLine[id]++;
    }

    /// <summary>Server: where the marker points while this person is out of their area (its bus stop), or null.</summary>
    private Vector3? PlayAreaBack(Player p) =>
        p.Id < MaxPlayers && _paOut[p.Id] && PlayAreaBounds(PlayAreaFor(p), out _, out _, out var back) ? back : null;
}
