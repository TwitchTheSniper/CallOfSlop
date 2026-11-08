using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--mega-host|--mega-client [KEY] [--map NAME] [--port P] [--clients N] [--hulls N] [--dist M] [--seconds S] [--warm]</c>: what a massive blast costs in the
/// real window (docs/perf/LIMITS.md, "Massive blasts"): a measurement, not a test; it fails nothing. Start one host and N client processes
/// (each under xvfb in the container, on a throwaway copy of the saves): the host waits for the clients, lays N hulls round a spot
/// <c>--dist</c> metres ahead of its player on the ground (Planetoid by default), and sets the design off there (by default the biggest antimatter
/// bomb the engine allows), as if it had landed. Every PC logs each frame from 5 s before to S s after: the frame, the perf window's areas
/// (render, terrain, particles, effects, the game, the server's ticks, the packets read), and at the end a summary and the worst frames, then quits.
/// </summary>
public static unsafe partial class Program
{
    private static int _mega; // 0 off, 1 host, 2 client
    private static string _megaKey = "Ordnance:B200:Impact Fuse+Antimatter Warhead@length=5.4+Bomb Tail Fins";
    private static int _megaClients = 2, _megaHulls = 100;
    private static float _megaDist = 300f;
    private static double _megaSecs = 20, _megaStart = -1, _megaBlastAt = -1, _megaReadyAt = -1;
    private static readonly List<float[]> _megaFrames = new(4096);
    private static readonly PerfArea[] MegaAreas = Enumerable.Range(0, (int)PerfArea.COUNT).Select(a => (PerfArea)a).ToArray();
    private const int MegaCols = 5; // before the areas: time from the blast, frame, draws, the worst server tick, ticks
    private static int _megaOps0, _megaShown0;
    private static bool _megaWarm; // --warm: a first blast, not measured (the JIT, the first crater's columns), then the measured one
    private static double _megaWarmAt = -1;

    private static void StartMegaWindow(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        _mega = args.Any(a => a.Equals("--mega-client", StringComparison.OrdinalIgnoreCase)) ? 2 : 1;
        if (args.FirstOrDefault(a => a.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase)) is { } key)
            _megaKey = key;
        if (int.TryParse(Arg("--clients"), out var nc))
            _megaClients = Math.Clamp(nc, 0, 8);
        if (int.TryParse(Arg("--hulls"), out var nh))
            _megaHulls = Math.Clamp(nh, 0, 1000);
        if (float.TryParse(Arg("--dist"), System.Globalization.CultureInfo.InvariantCulture, out var d))
            _megaDist = Math.Clamp(d, 20f, 3000f);
        _megaWarm = args.Any(a => a.Equals("--warm", StringComparison.OrdinalIgnoreCase));
        if (double.TryParse(Arg("--seconds"), System.Globalization.CultureInfo.InvariantCulture, out var s))
            _megaSecs = Math.Clamp(s, 5, 120);
        _settings.Mode = GameMode.Sandbox;
        _settings.MapName = (Maps.Find(Arg("--map") ?? "Planetoid") ?? Maps.All[0]).Name;
        _settings.MapRandom = false;
        _settings.Bots = _settings.HostBots = 0;
        _settings.AutoStart = 0;
        if (ushort.TryParse(Arg("--port"), out var port))
            _settings.Port = port;
        _settings.JoinAddress = "127.0.0.1";
        Console.WriteLine($"[mega-window] {(_mega == 1 ? $"host, waiting for {_megaClients} clients" : "client")} on {_settings.MapName}, port {_settings.Port}: {_megaKey}");
        StartSession(_mega == 1 ? LobbyAction.Host : LobbyAction.Join);
    }

    /// <summary>Each frame (after the frame's timings are in): into play, then the blast, then the frames logged.</summary>
    private static void MegaWindowFrame(double now)
    {
        if (_megaStart < 0)
            _megaStart = now;
        Ui.PerfMode = PerfView.Overlay; // (the perf areas are timed only while it is up)
        if ((int)((now - _megaStart) / 10) != (int)((now - _megaStart - FrameDt) / 10))
            Console.WriteLine($"[mega-window] {(int)(now - _megaStart)} s: session {(_session == null ? "none" : $"{_session.Phase}, players {_session.Players.Count}, local {(_session.Local?.Alive == true ? "alive" : "not alive")}")}, lobby room {InLobbyRoom}, menu {Ui.MenuOpen}");
        if (_session is not { } g)
        {
            if (now - _megaStart > 240)
                MegaQuit("no session");
            return;
        }
        if (_mega == 1 && g.Phase == MatchPhase.Lobby && g.Players.Count - 1 >= _megaClients && (int)(now * 2) != (int)((now - FrameDt) * 2))
            g.LobbyStart(true); // (every client is in: the host starts the match)
        if (g.Phase == MatchPhase.Warmup)
        {
            if (_mega == 1)
                g.ForceStart();
            g.SetReady(true);
            g.RequestRespawn();
        }
        else if (g.Phase == MatchPhase.Playing && Ui.MenuOpen && g.Local is { Alive: true })
        {
            Ui.AutoDeploy(g, now);
            SetMouseCaptured(true);
        }
        else if (g.Phase == MatchPhase.Playing && g.Local is not { Alive: true } && !g.RespawnRequested)
            g.RequestRespawn();
        if (g.Local is { } me)
            me.ImmuneUntil = now + 600; // (the camera stays up: the blast still runs over everyone else)
        var playing = g.Phase == MatchPhase.Playing && g.Local is { Alive: true } && !Ui.MenuOpen;
        if (!playing)
        {
            if (now - _megaStart > 300)
                MegaQuit($"never got into play (phase {g.Phase}, alive {g.Local?.Alive}, menu {Ui.MenuOpen})");
            return;
        }
        if (_megaReadyAt < 0)
        {
            _megaReadyAt = now;
            _megaOps0 = g.DigOpsRead;
            _megaShown0 = MegaBlastFx.Shown;
            g.LookPitch = 0.12f;
            if (_mega == 1)
                MegaBuild(g);
        }
        if (Perf.Filled > 2 && _megaReadyAt > 0)
        {
            var row = new float[MegaAreas.Length + MegaCols];
            row[0] = (float)(now - (_megaBlastAt > 0 ? _megaBlastAt : now + 1e3));
            row[1] = Perf.FrameMs(0);
            row[2] = PerfCounters.Draws;
            row[3] = Perf.TickWorstMs(0);
            row[4] = Perf.Ticks(0);
            for (var a = 0; a < MegaAreas.Length; a++)
                row[a + MegaCols] = Perf.AreaMs(0, MegaAreas[a]);
            _megaFrames.Add(row);
        }
        if (_mega == 1 && _megaBlastAt < 0)
        {
            var have = g.Players.Count - 1;
            if (have >= _megaClients && now - _megaReadyAt > 10 && _megaWarm && _megaWarmAt < 0)
            {
                _megaWarmAt = now;
                MegaSetOff(g, now, warm: true); // (behind the host, unmeasured: its effects are over before the measured one)
            }
            else if (have >= _megaClients && now - _megaReadyAt > 15 && (!_megaWarm || _megaWarmAt > 0 && now - _megaWarmAt > 20))
                MegaSetOff(g, now); // (15 s in: the clients are streamed the world and in play)
            else if (now - _megaReadyAt > 120)
                MegaQuit($"only {have} of {_megaClients} clients came");
            return;
        }
        if (_mega == 2 && _megaBlastAt < 0)
        {
            if (MegaBlastFx.Shown > _megaShown0 + (_megaWarm ? 1 : 0))
            {
                _megaBlastAt = now;
                MegaMarkBefore(now);
            }
            else if (now - _megaReadyAt > 200)
                MegaQuit("no blast came");
            return;
        }
        if (now - _megaBlastAt > _megaSecs)
            MegaQuit(null);
    }

    /// <summary>The frames logged before the blast get their times from it.</summary>
    private static void MegaMarkBefore(double now)
    {
        // (rows logged before carry a time 1000 s after now; they are now times before the blast, counted back by frame time)
        var t = 0f;
        for (var i = _megaFrames.Count - 1; i >= 0; i--)
        {
            t -= _megaFrames[i][1] / 1000f;
            _megaFrames[i][0] = t;
        }
        var keep = _megaFrames.FindIndex(r => r[0] > -5f);
        if (keep > 0)
            _megaFrames.RemoveRange(0, keep);
    }

    private const string MegaWarmKey = "Ordnance:B25:Impact Fuse+Fission Warhead+Bomb Tail Fins"; // (166 m: the same code, short of the hulls)
    private static Vector3 _megaSpot;
    private static int _megaBuilt;

    private static Vector3 MegaGround(Vector3 p) => World.Map.Planetoid is { } pl ? pl.GroundUnder(p) : new Vector3(p.X, World.Map.GroundHeight(p.X, p.Z), p.Z);

    /// <summary>When the host is in play: the spot ahead and the hulls round it, so they have settled before the blast.</summary>
    private static void MegaBuild(Game g)
    {
        var me = g.Local!;
        var at = _megaSpot = MegaGround(me.Position + g.LookDir(g.LookYaw, 0) * _megaDist);
        var up = GravityFrames.Curved ? GravityFrames.UpAt(at) : Vector3.UnitY;
        var w = g.Constructs;
        var block = ConstructParts.Need("Frame Block");
        var size = block.BoundsMax - block.BoundsMin;
        var side = Vector3.Normalize(Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var fwd = Vector3.Cross(side, up);
        for (var i = 0; i < _megaHulls; i++)
        {
            var a = i * 2.39996f;
            var r = 20f + 400f * MathF.Sqrt((i + 0.5f) / _megaHulls);
            var p = MegaGround(at + (side * MathF.Cos(a) + fwd * MathF.Sin(a)) * r);
            var pu = GravityFrames.Curved ? GravityFrames.UpAt(p) : Vector3.UnitY;
            var ps = Vector3.Normalize(Vector3.Cross(pu, MathF.Abs(pu.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
            var pf = Vector3.Cross(ps, pu);
            var rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(ps.X, ps.Y, ps.Z, 0, pu.X, pu.Y, pu.Z, 0, -pf.X, -pf.Y, -pf.Z, 0, 0, 0, 0, 1));
            if (w.Create(p + pu * 0.3f, rot) is not { } c)
                break;
            for (var k = 0; k < 60; k++)
                w.AddPart(c, block, new Vector3(k % 12 * size.X, k / 144 * size.Y, k / 12 % 12 * size.Z), 0);
            c.Rebuild();
            _megaBuilt++;
        }
    }

    private static void MegaSetOff(Game g, double now, bool warm = false)
    {
        var key = warm ? MegaWarmKey : _megaKey;
        if (Ordnance.Parse(key) is not { OrdStats: { } st } def || World.Map.Planetoid == null && World.Map.Terrain == null)
        {
            MegaQuit($"\"{key}\" isn't a design, or the map has no ground");
            return;
        }
        var me = g.Local!;
        var at = warm ? MegaGround(me.Position - (_megaSpot - me.Position)) : _megaSpot; // (the warming one as far behind the host)
        var up = GravityFrames.Curved ? GravityFrames.UpAt(at) : Vector3.UnitY;
        var w = g.Constructs;
        var ch = st.Flight.Charges[0];
        var pos = at + up * 0.3f;
        if (w.AddShell(new CShell { Kind = ShellKind.Bomb, Pos = pos, Prev = pos, Tested = pos, Landed = true, Delay = 1, Blast = ch.Blast, Damage = ch.Damage, Crater = ch.Crater, Design = def, By = 255, Flags = ShellFlags.Designed }) != 0)
            w.ShellBlastsWaiting++;
        var awake = 0;
        foreach (var c in w.Bodies)
            awake += c.Asleep ? 0 : 1;
        Console.WriteLine($"[mega-window] {(warm ? "warming blast" : "set off")}: {key}, {st.Blast:0} m asked ({w.CapRadius(st.Blast):0} m), crater {ch.Crater:0} m, {Vector3.Distance(me.Position, at):0} m {(warm ? "behind" : "ahead of")} the host; {_megaBuilt} hulls of 60 parts built round the spot ahead, {w.Bodies.Count} bodies ({awake} awake); {g.Players.Count} players");
        if (warm)
            return;
        _megaBlastAt = now;
        MegaMarkBefore(now);
        w.MegaStepWorstMs = 0;
        g.MegaDigWorstMs = 0;
    }

    private static void MegaQuit(string? why)
    {
        if (why != null)
            Console.WriteLine($"[mega-window] FAILED: {why}");
        else
            MegaSummary();
        _quit = true;
    }

    private static void MegaSummary()
    {
        var g = _session!;
        var who = _mega == 1 ? "host" : "client";
        List<float[]> Part(bool after) => _megaFrames.Where(r => after ? r[0] >= 0 : r[0] < 0).ToList();
        string Line(List<float[]> rows, string label)
        {
            if (rows.Count == 0)
                return $"{label}: no frames";
            var fr = rows.Select(r => r[1]).OrderBy(v => v).ToArray();
            var sb = new System.Text.StringBuilder($"{label}: {rows.Count} frames, frame median {fr[fr.Length / 2]:0.0} p95 {fr[(int)(fr.Length * 0.95)]:0.0} max {fr[^1]:0.0} ms, {fr.Count(v => v > 50)} over 50 ms; worst by area:");
            for (var a = 0; a < MegaAreas.Length; a++)
            {
                var col = rows.Select(r => r[a + MegaCols]).OrderBy(v => v).ToArray();
                if (col[^1] >= 1f)
                    sb.Append($" {Perf.Names[(int)MegaAreas[a]]} {col[col.Length / 2]:0.0}/{col[^1]:0.0}");
            }
            var ticks = rows.Select(r => r[3]).Where(v => v > 0).OrderBy(v => v).ToArray();
            sb.Append($"; one server tick: median {(ticks.Length > 0 ? ticks[ticks.Length / 2] : 0):0.00} worst {(ticks.Length > 0 ? ticks[^1] : 0):0.00} ms ({ticks.Count(v => v > 8)} frames with a tick over 8 ms); draws median {rows.Select(r => r[2]).OrderBy(v => v).ElementAt(rows.Count / 2):0}");
            return sb.ToString();
        }
        Console.WriteLine($"[mega-window] {who} {Line(Part(false), "before (5 s)")}");
        Console.WriteLine($"[mega-window] {who} {Line(Part(true), $"blast ({_megaSecs:0} s)")}");
        foreach (var r in Part(true).OrderByDescending(r => r[1]).Take(10))
        {
            var sb = new System.Text.StringBuilder($"[mega-window] {who} worst frame at +{r[0]:0.00} s: {r[1]:0.0} ms, {r[4]:0} ticks, worst tick {r[3]:0.0}:");
            for (var a = 0; a < MegaAreas.Length; a++)
                if (r[a + MegaCols] > 1f)
                    sb.Append($" {Perf.Names[(int)MegaAreas[a]]} {r[a + MegaCols]:0.0}");
            Console.WriteLine(sb.ToString());
        }
        // The blast's first two seconds, a tenth of a second at a time: the worst frame in each.
        var line = new System.Text.StringBuilder($"[mega-window] {who} worst frame per 0.1 s from the blast:");
        for (var k = 0; k < 40; k++)
        {
            var bin = _megaFrames.Where(r => r[0] >= k * 0.1f && r[0] < (k + 1) * 0.1f).ToList();
            line.Append(bin.Count == 0 ? " -" : $" {bin.Max(r => r[1]):0}");
        }
        Console.WriteLine(line.ToString());
        Console.WriteLine($"[mega-window] {who} did: dig ops read {g.DigOpsRead - _megaOps0}, effects {MegaBlastFx.Shown - _megaShown0}; "
                          + (g.IsServer ? $"server: front and walk worst {g.Constructs.MegaStepWorstMs:0.00} ms, a crater band worst {g.MegaDigWorstMs:0.00} ms, {g.MegaDigBands} bands, {g.Constructs.MegaPartsWalked} parts walked, {g.Constructs.Bodies.Count} bodies left" : "")
                          + $"; heap {GC.GetTotalMemory(false) / 1048576} MB, gc0 {GC.CollectionCount(0)} gc2 {GC.CollectionCount(2)}");
    }
}
