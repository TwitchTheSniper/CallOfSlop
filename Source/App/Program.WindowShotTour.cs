using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's tour actions (a play-test pass: tools/playtest/tour.ps1): the camera to named places in one run, each
/// pictured and its frame rate read. <c>places</c> prints where the player is;
/// <c>view TARGET | DIST,UP,BEARING</c> puts the eye DIST m out from TARGET on a compass bearing (degrees from north) and UP m
/// above it, looking at it, where TARGET is <c>body N</c>, <c>geo LAT,LON</c> (the ground there,
/// on a planetoid) or <c>X,Y,Z</c>; <c>perf N [LABEL]</c> times the next N frames (mean, frames a second, the 99th percentile
/// and the worst). <c>--uncapped</c> turns VSync and every frame cap off so the numbers are the game's own.
/// </summary>
public static partial class Program
{
    private static int _wsPerfLeft, _wsPerfAll;
    private static string _wsPerfLabel = "";
    private static long _wsPerfLast;
    private static readonly List<double> _wsPerfMs = [];

    private static bool WsTourKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "places" or "view" or "lookat" or "perf" or "rec" or "skip" or "open" or "what";

    private static void WsTourSettings(string[] args)
    {
        if (!args.Any(a => a.Equals("--uncapped", StringComparison.OrdinalIgnoreCase)))
            return;
        _settings.VSync = false;
        _settings.MaxFps = _settings.MaxFpsMenu = _settings.MaxFpsBackground = 0;
    }

    /// <summary>Once a frame, first: a running <c>perf</c> takes this frame's time.</summary>
    private static void WsTourFrame()
    {
        WsTalkFrame();
        WsHeldFrame();
        var t = Stopwatch.GetTimestamp();
        if (_wsPerfLeft > 0 && _wsPerfLast != 0)
        {
            _wsPerfMs.Add(Stopwatch.GetElapsedTime(_wsPerfLast, t).TotalMilliseconds);
            if (--_wsPerfLeft == 0)
            {
                _wsPerfMs.Sort();
                var mean = _wsPerfMs.Average();
                var p99 = _wsPerfMs[Math.Min(_wsPerfMs.Count - 1, (int)(_wsPerfMs.Count * 0.99))];
                var p50 = _wsPerfMs[_wsPerfMs.Count / 2];
                WsSay($"[perf] {_wsPerfLabel}: {_wsPerfMs.Count} frames, mean {mean:0.00} ms ({1000 / mean:0} fps), median {p50:0.00} ms, 99th {p99:0.00} ms, worst {_wsPerfMs[^1]:0.00} ms"
                    + (_session is { } g ? $", bodies {g.Constructs.Bodies.Count}, players {g.Players.Count}, lamps lit {ConstructLamps.LastLit} of {ConstructLamps.LastNear}, free lights {LightGrid.DynamicFree}" : ""));
                if (PerfBench.Recording)
                    BenchSpotDone(PerfBench.End(keepOn: Ui.PerfMode != PerfView.Off), _session); // the profiler's breakdown, and with --bench the run's JSON (Program.Bench.cs)
            }
        }
        _wsPerfLast = t;
    }

    private static bool WsTourVerb(Game g, Player me, string verb, string arg)
    {
        switch (verb)
        {
            case "perf":
            {
                if (Ui.CaptureRecording)
                {
                    WsFail("perf: the F9 recording is running (rec stop first: both use PerfBench)");
                    return true;
                }
                var sp = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
                _wsPerfAll = _wsPerfLeft = int.TryParse(sp[0], out var n) ? Math.Clamp(n, 10, 5000) : 300;
                _wsPerfLabel = sp.Length > 1 ? sp[1] : $"spot {_wsShots}";
                _wsPerfMs.Clear();
                _wsPerfLast = 0;
                _wsWait = _wsPerfAll + 2;
                PerfBench.Begin(_wsPerfLabel); // (the profiler on over the same frames: PerfBench.cs)
                return true;
            }
            case "rec": // "rec start" / "rec stop": the F9 window's recording, as its Start button and the perf key do (UiPerfCapture.cs)
                if (arg.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    Ui.CaptureStopAsked = true;
                    SetPerfMode(PerfView.Window);
                }
                else if (!Ui.CaptureRecording)
                    Ui.StartCapture(g, Brutal.GlfwApi.Glfw.Time);
                _wsWait = 3;
                return true;
            case "skip": // the survival opening skipped, as its key does
                g.OpeningSkip();
                _wsWait = 90;
                return true;
            case "open": // a window as its key opens it: "open logbook [Records|Paths|Technologies]", "open parts", "open factions"
            {
                var sp = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
                SetLogbookOpen(false);
                SetFactionOpen(false);
                switch (sp[0].ToLowerInvariant())
                {
                    case "logbook":
                        Ui.LogbookPick = sp.Length > 1 ? sp[1] : null;
                        SetLogbookOpen(true);
                        break;
                    case "parts":
                        Ui.OpenSandboxParts(focusSearch: false);
                        SetMouseCaptured(false);
                        break;
                    case "factions":
                        SetFactionOpen(true);
                        break;
                    default:
                        WsFail("open logbook [TAB] | parts | factions");
                        return true;
                }
                _wsWait = 20;
                return true;
            }
            case "what": // "what [RIGHT,UP]": what the eye's ray (turned that many degrees) hits: the body and the part, how far
            {
                var o = arg.Split(',', StringSplitOptions.TrimEntries);
                float dr = 0, du = 0;
                if (o.Length == 2)
                {
                    float.TryParse(o[0], System.Globalization.CultureInfo.InvariantCulture, out dr);
                    float.TryParse(o[1], System.Globalization.CultureInfo.InvariantCulture, out du);
                }
                var dir = g.LookDir(g.LookYaw + dr * MathF.PI / 180, g.LookPitch + du * MathF.PI / 180);
                WsSay(g.Constructs.Raycast(me.Eye, dir, 200f, out var hit, look: true)
                    ? $"[what] {arg}: construct {hit.Body.Id}, part {hit.Part} {hit.Body.DefOf(hit.Part).Name}, {hit.Distance:0.0} m"
                    : $"[what] {arg}: no construct within 200 m");
                return true;
            }
            case "places":
            {
                var pl = World.Map.Planetoid;
                string Where(Vector3 p)
                {
                    if (pl == null)
                        return $"{p.X:0},{p.Y:0},{p.Z:0}";
                    var d = Vector3.Normalize(p - pl.Center);
                    return $"{p.X:0},{p.Y:0},{p.Z:0} (lat {MathF.Asin(d.Y) * 180 / MathF.PI:0.000}, lon {(MathF.Atan2(d.X, d.Z) * 180 / MathF.PI + 360) % 360:0.000})";
                }
                WsSay($"[place] player at {Where(me.Position)}");
                WsSay($"[place] bodies {g.Constructs.Bodies.Count}, players {g.Players.Count}");
                return true;
            }
            case "lookat": // "lookat TARGET": the look turned to a target (as "view" takes it) from where the eye is: a joined client, whose jumps the host takes back, looks at the host
            {
                if (WsTourTarget(g, arg.Trim(), me) is not { } target)
                {
                    WsFail($"lookat TARGET (body N, geo LAT,LON, other or X,Y,Z): \"{arg}\"");
                    return true;
                }
                var to = target - me.Eye;
                (g.LookYaw, g.LookPitch) = GravityFrames.Curved ? GravityFrames.Angles(me.Position, to) : (MathF.Atan2(to.X, -to.Z), MathF.Asin(Math.Clamp(Vector3.Normalize(to).Y, -1f, 1f)));
                WsSay(FormattableString.Invariant($"lookat: {to.Length():0.0} m away"));
                _wsWait = 3;
                return true;
            }
            case "view":
            {
                var bar = arg.LastIndexOf('|');
                var nums = bar > 0 ? arg[(bar + 1)..].Split(',', StringSplitOptions.TrimEntries) : [];
                var what = bar > 0 ? arg[..bar].Trim() : arg.Trim();
                var v = new float[3];
                if (nums.Length != 3 || !nums.Select((s, i) => float.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out v[i])).All(b => b)
                    || WsTourTarget(g, what, me) is not { } target)
                {
                    WsFail($"view TARGET | DIST,UP,BEARING (TARGET: body N, geo LAT,LON, other or X,Y,Z): \"{arg}\"");
                    return true;
                }
                GravityFrames.Frame(target, out var right, out var up, out var north);
                var b = v[2] * MathF.PI / 180;
                var eye = target + (north * MathF.Cos(b) + right * MathF.Sin(b)) * v[0] + up * v[1];
                g.NoclipShot = true; // (the eye hovers where it is put, in a survival world too)
                me.NoclipWanted = g.NoclipAllowed(me);
                WsEyeTo(g, me, eye);
                g.LookFrame = me.Position;
                (g.LookYaw, g.LookPitch) = GravityFrames.Curved ? GravityFrames.Angles(me.Position, target - me.Eye) : (MathF.Atan2((target - me.Eye).X, -(target - me.Eye).Z), MathF.Asin(Math.Clamp(Vector3.Normalize(target - me.Eye).Y, -1f, 1f)));
                _wsWait = 3;
                return true;
            }
        }
        return false;
    }

    private static Vector3? WsTourTarget(Game g, string what, Player? me = null)
    {
        if (what.Equals("other", StringComparison.OrdinalIgnoreCase)) // (another player's eye: the host looking at a joined client's face, a suit's helmet)
        {
            Player? first = null;
            foreach (var other in g.Players.Values)
                if (other != me && other.Alive)
                {
                    if (other.InShell)
                        return other.Eye; // (one in a robot shell first: the chair's sleeper beside it is not who is looked for)
                    first ??= other;
                }
            return first?.Eye;
        }
        if (what.StartsWith("geo ", StringComparison.OrdinalIgnoreCase))
        {
            if (World.Map.Planetoid is not { } pl || what[4..].Split(',') is not [var la, var lo]
                || !double.TryParse(la, System.Globalization.CultureInfo.InvariantCulture, out var lat) || !double.TryParse(lo, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                return null;
            double fl = lat * Math.PI / 180, fo = lon * Math.PI / 180;
            var dir = new Vector3((float)(Math.Cos(fl) * Math.Sin(fo)), (float)Math.Sin(fl), (float)(Math.Cos(fl) * Math.Cos(fo)));
            return pl.Center + dir * (float)(pl.Radius + pl.HeightAt(dir.X, dir.Y, dir.Z));
        }
        if (what.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(what[5..], out var id) && g.Constructs.Get(id) is { } bc ? bc.WorldCom : null;
        return WsVec(what);
    }
}
