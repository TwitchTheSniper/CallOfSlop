using System.Numerics;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// <c>--window-shot OUT.png|bmp [--map NAME] [--creative] [--blueprint NAME] [--at X,Y,Z] [--look-at X,Y,Z | --yaw D --pitch D] [--calm] [--frames N] [--every N] [--do "a; b; ..."] [--size WxH]</c>:
/// pictures of the real game in its real window, for an agent's own checking (README.md "Testing without playing"). It starts an offline creative
/// Sandbox world (default map Proving Ground: a big blueprint fits inside its arena) on a throwaway copy of the saves, optionally spawns a blueprint in front of the player through the
/// parts menu's own call (CActKind.Blueprint), flies the player (noclip) to the camera, runs a short script of actions through the real input
/// paths, waits <c>--frames</c> frames and saves the frame (with <c>--every N</c> a numbered series over those frames), prints the paths and quits.
/// Exit code: 0, or 1 when the window, the world or an action failed (the reason is printed).
/// Actions (<see cref="WsActions"/>): aim PART[#n], near PART[#n] [m], use, key ACTION, touch LABEL[@PART#n], wheel N, wait N, tool NAME, mains off|on, panel PART[#n], shot [NAME], deposit [TIER], fill PART[#n] N MATERIAL, link FROM[#n] > TO[#m].
/// </summary>
public static unsafe partial class Program
{
    private static bool _windowShot, _wsCalm;
    private static int _wsExit;
    private static string _wsOut = "", _wsMap = "Proving Ground", _wsBlueprint = "";
    private static Vector3? _wsAt, _wsLookAt, _wsSpawnAt;
    private static string _wsNet = "", _wsNotice = "";
    private static double _wsJoinTry; // (when a joining client last tried the host: it may still be loading)

    /// <summary>
    /// The window tool's joined client (<c>--net join:IP</c>), whose session has just been dropped: true when it joins again
    /// instead. Two windows started together reach RakNet before the host is listening (a headless start is 15-20 s of data and
    /// maps), and RakNet gives up after a few seconds with "Could not reach host": the client keeps trying while the host comes up.
    /// </summary>
    private static bool WsRejoin(string status, double now)
    {
        if (!_windowShot || !_wsNet.StartsWith("join:", StringComparison.OrdinalIgnoreCase) || _quit || now - _wsStart > 90)
            return false;
        if (now - _wsJoinTry < 2)
            return true; // (the session stays as it is for a moment: the next frame tries again)
        _wsJoinTry = now;
        WsSay($"joining again at {now - _wsStart:0} s ({status})");
        StartSession(LobbyAction.Join);
        _wsStep = 1;
        return true;
    }
    private static int _wsDesignTries;
    private static int _wsDragLeft, _wsDragAll, _wsDragKeys;
    private static long _wsDragBytes, _wsDragAt;
    private static float? _wsYaw, _wsPitch;
    private static int _wsFrames = 45, _wsEvery, _wsStep, _wsWait, _wsShots, _wsCount;
    private static double _wsStart;
    private static int _wsAwait; // frames a "station await" has waited
    private static readonly Queue<string> _wsQueue = new();
    private static readonly List<string> _wsSaved = [];
    private static string? _wsShotPath; // saved at the end of this frame, after the UI
    private static GameAction? _wsPress; // pressed for one frame (ORed into the real input: PollActions)
    private static ushort _wsBody; // the spawned blueprint
    private static TextWriter? _wsStdout;

    private const string WsActions = "aim PART[#n], near PART[#n] [m], from PART[#n] SIDE,UP,OUT, use, key ACTION, touch LABEL[@PART#n], wheel N, wait N, tool NAME, mains off|on, panel PART[#n], shot [NAME], deposit [TIER], fill PART[#n] N MATERIAL, link FROM[#n] > TO[#m], saveload";

    /// <summary>Reads the flags (before the window): the size and a throwaway save folder are set here.</summary>
    private static bool WindowShotArgs(string[] args)
    {
        var i = Array.FindIndex(args, a => a.Equals("--window-shot", StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return false;
        _windowShot = true;
        _wsStdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        string? Val(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        _wsOut = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? Path.GetFullPath(args[i + 1]) : AppFolders.LogPath("window_shot.png");
        _wsMap = Val("--map") ?? _wsMap;
        _wsBlueprint = Val("--blueprint") ?? "";
        _wsAt = Val("--at") is { } at ? WsVec(at) : null;
        _wsCalm = args.Any(a => a.Equals("--calm", StringComparison.OrdinalIgnoreCase)); // (no cold storm: a site is seen whole)
        _wsLookAt = Val("--look-at") is { } la ? WsVec(la) : null;
        _wsSpawnAt = Val("--spawn-at") is { } sa ? WsVec(sa) : null;
        _wsNet = Val("--net") ?? "";
        if (_wsNet.StartsWith("join:", StringComparison.OrdinalIgnoreCase))
            _wsBlueprint = ""; // (a client finds the host's constructs by their parts' names)
        _wsYaw = float.TryParse(Val("--yaw"), System.Globalization.CultureInfo.InvariantCulture, out var y) ? y * MathF.PI / 180 : null;
        _wsPitch = float.TryParse(Val("--pitch"), System.Globalization.CultureInfo.InvariantCulture, out var p) ? p * MathF.PI / 180 : null;
        _wsFrames = int.TryParse(Val("--frames"), out var f) ? Math.Max(1, f) : _wsFrames;
        _wsEvery = int.TryParse(Val("--every"), out var e) ? Math.Max(0, e) : 0;
        if (Val("--do") is { } script)
            foreach (var a in script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                _wsQueue.Enqueue(a);
        foreach (var a in _wsQueue)
            if (!WsKnown(a))
            {
                WsSay($"unknown action \"{a}\"; actions: {WsActions}");
                _wsExit = 1;
            }
        return true;
    }

    private static void WindowShotSettings(string[] args)
    {
        _settings.Display = DisplayMode.Windowed;
        (_settings.Width, _settings.Height) = (1280, 720);
        if (args.SkipWhile(a => !a.Equals("--size", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?.Split('x') is [var w, var h] && int.TryParse(w, out var wi) && int.TryParse(h, out var hi))
            (_settings.Width, _settings.Height) = (Math.Clamp(wi, 320, 3840), Math.Clamp(hi, 240, 2160));
        _settings.Mode = GameMode.Sandbox;
        _settings.SandboxSurvival = args.Any(a => a.Equals("--survival", StringComparison.OrdinalIgnoreCase)); // (creative by default; --survival: a survival world, with the story's colony on its home map)
        _settings.MapRandom = false;
        _settings.StoryStart = args.SkipWhile(a => !a.Equals("--start", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? ""; // (a story start by id, on its own planetoid: GameStoryStart.cs)
        _settings.Bots = 0;
        _settings.ShowFps = false;
        LoadoutStore.ReadOnly = AppearanceStore.ReadOnly = BuildStore.ReadOnly = BlueprintStore.ReadOnly = true;
    }

    private static Vector3? WsVec(string s) =>
        s.Split(',') is [var x, var y, var z] && float.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out var a)
        && float.TryParse(y, System.Globalization.CultureInfo.InvariantCulture, out var b) && float.TryParse(z, System.Globalization.CultureInfo.InvariantCulture, out var c) ? new Vector3(a, b, c) : null;

    private static bool WsKnown(string action) => action.Split(' ', 2)[0].ToLowerInvariant() is "design" or "drag" or "mousemove" or "station" or "aim" or "near" or "from" or "use" or "key" or "touch" or "wheel" or "wait" or "tool" or "mains" or "panel" or "shot" or "deposit" or "fill" or "link" or "click" or "clickup" or "esc" or "ui" or "picker" or "bring" or "craft" or "disown" or "saveload" or "stock" or "feed" or "kit" or "poke" or "joints" or "timed" or "saveworld" or "loadworld" or "logi" || WsGhostKnown(action);

    private static void WsSay(string s)
    {
        Console.WriteLine($"[windowshot] {s}");
        _wsStdout?.WriteLine($"[windowshot] {s}");
    }

    private static void WsFail(string why)
    {
        WsSay($"FAILED: {why}");
        _wsExit = 1;
        _quit = true;
    }

    /// <summary>Once a frame, before the input and the game's update (the main loop, beside the smoke test).</summary>
    private static void WindowShotTick(double now)
    {
        if (_wsExit != 0 && _wsStep == 0)
        {
            WsFail("bad arguments");
            return;
        }
        _wsCount++;
        switch (_wsStep)
        {
            case 0: // start the world
                var map = Maps.Find(_wsMap);
                if (map == null)
                {
                    WsFail($"no map \"{_wsMap}\"");
                    return;
                }
                _settings.MapName = map.Name;
                if (_wsNet.StartsWith("host", StringComparison.OrdinalIgnoreCase))
                {
                    if (_wsNet.Split(':') is [_, var hp] && ushort.TryParse(hp, out var hport))
                        _settings.Port = hport;
                    _settings.HostBots = 0;
                    StartSession(LobbyAction.Host); // (the real transport: a client joins with --net join:IP[:PORT])
                }
                else if (_wsNet.StartsWith("join:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = _wsNet.Split(':');
                    _settings.JoinAddress = parts[1];
                    if (parts.Length > 2 && ushort.TryParse(parts[2], out var jport))
                        _settings.Port = jport;
                    StartSession(LobbyAction.Join);
                }
                else
                    StartSession(LobbyAction.SinglePlayer);
                if (_session == null)
                {
                    WsFail("the session didn't start");
                    return;
                }
                _wsStart = now;
                _wsStep = 1;
                return;
            case 1: // wait for play
                if (_session is not { } g)
                {
                    WsFail("the session ended");
                    return;
                }
                if (g.InLobby && g.IsServer)
                    g.LobbyStart(force: true, now); // (a host: START at once)
                if (g.Phase == MatchPhase.Warmup)
                {
                    g.SetReady(true);
                    g.RequestRespawn();
                }
                else if (g.Phase == MatchPhase.Playing && Ui.MenuOpen && g.Local is { Alive: true })
                    Ui.AutoDeploy(g, now);
                else if (g.Phase == MatchPhase.Playing && g.Local is not { Alive: true } && !g.RespawnRequested)
                    g.RequestRespawn();
                if (g.Phase == MatchPhase.Playing && g.Local is { Alive: true } me && !Ui.MenuOpen)
                {
                    me.ImmuneUntil = now + 3600;
                    SetMouseCaptured(true);
                    if (!g.SandboxOn)
                    {
                        WsFail($"{World.Map.Name} isn't a Sandbox map");
                        return;
                    }
                    if (_wsBlueprint.Length > 0)
                    {
                        var all = ConstructBuild.Blueprints;
                        var index = -1;
                        for (var k = 0; k < all.Count && index < 0; k++)
                            if (all[k].Name.Equals(_wsBlueprint, StringComparison.OrdinalIgnoreCase))
                                index = k;
                        if (index < 0)
                        {
                            WsFail($"no blueprint \"{_wsBlueprint}\"");
                            return;
                        }
                        if (_wsSpawnAt is { } sp)
                            me.Teleport(new Vector3(sp.X, World.Map.GroundHeight(sp.X, sp.Z), sp.Z)); // (the blueprint goes down in front of here)
                        _wsBody = (ushort)(g.Constructs.Bodies.Count > 0 ? g.Constructs.Bodies.Max(b => b.Id) : 0);
                        g.SandboxSend(CActKind.Blueprint, part: (ushort)index); // (the parts menu's own call: UiSandboxParts.cs)
                    }
                    _wsStep = 2;
                    _wsWait = 0;
                }
                else if (now - _wsStart > (_wsNet.StartsWith("join:", StringComparison.OrdinalIgnoreCase) ? 90 : 40)) // (a joined client waits out the host's own start-up)
                    WsFail($"the world didn't reach play (phase {g.Phase}, alive {g.Local?.Alive}, menu {Ui.MenuOpen}{(g.Disconnected ? $", {g.Status}" : "")})");
                return;
            case 2: // the blueprint down, then the camera
                if (_session is not { Local: { } p } game)
                {
                    WsFail("the session ended");
                    return;
                }
                if (_wsBlueprint.Length > 0)
                {
                    Construct? made = null;
                    foreach (var b in game.Constructs.Bodies)
                        if (b.Id > _wsBody && (made == null || b.PartCount > made.PartCount))
                            made = b;
                    if (made == null)
                    {
                        if (++_wsWait > 600)
                            WsFail($"the {_wsBlueprint} wasn't set down ({string.Join("; ", game.SandboxHands.Notices.Select(n => n.Item2))})");
                        return;
                    }
                    _wsBody = made.Id;
                    WsSay($"{_wsBlueprint}: construct {made.Id}, {made.PartCount} parts, centre {made.WorldCom:0.0}, radius {made.Radius:0.0} m");
                }
                game.NoclipShot = _wsAt != null; // (a survival world's camera flies too when it is put somewhere)
                p.NoclipWanted = game.NoclipAllowed(p); // (hover where the camera is put)
                var spawned = WsSpawned(game);
                if (_wsAt is { } at)
                    WsEyeTo(game, p, at);
                else if (spawned != null)
                {
                    // Back and up from the blueprint, the way the player looks: all of it in view.
                    var fwd = Vector3.Normalize(new Vector3(spawned.WorldCom.X - p.Position.X, 0, spawned.WorldCom.Z - p.Position.Z) + new Vector3(1e-4f, 0, 0));
                    WsEyeTo(game, p, spawned.WorldCom - fwd * (spawned.Radius * 1.3f + 2) + new Vector3(0, spawned.Radius * 0.6f + 1, 0));
                }
                if (_wsLookAt is { } look)
                    WsLook(game, p.Eye, look);
                else if (_wsYaw != null || _wsPitch != null)
                    (game.LookYaw, game.LookPitch) = (_wsYaw ?? game.LookYaw, _wsPitch ?? game.LookPitch);
                else if (spawned != null)
                    WsLook(game, p.Eye, spawned.WorldCom);
                _wsStep = 3;
                _wsWait = 20; // (the camera and the first fills settle before the script)
                return;
            case 3: // the script
                if (_session is { } sg && sg.SandboxNotice is { Length: > 0 } said && said != _wsNotice)
                    WsSay($"notice: {said}"); // (a refusal or a hint on the HUD)
                _wsNotice = _session?.SandboxNotice ?? "";
                if (_wsWait > 0)
                {
                    _wsWait--;
                    return;
                }
                if (_wsQueue.Count == 0)
                {
                    _wsStep = 4;
                    _wsWait = _wsFrames;
                    return;
                }
                WsRun(_wsQueue.Dequeue(), now);
                return;
            case 4: // settle, then the picture (or a series)
                _wsWait--;
                if (_wsEvery > 0 && (_wsFrames - _wsWait) % _wsEvery == 0 && _wsWait > 0)
                    WsShot($"{_wsShots:000}");
                if (_wsWait > 0)
                    return;
                WsShot(_wsEvery > 0 ? $"{_wsShots:000}" : null);
                _wsStep = 5;
                return;
            case 5:
                WsSay($"done: {_wsSaved.Count} picture(s), {_wsCount} frames");
                foreach (var s in _wsSaved)
                    WsSay($"saved {s}");
                _quit = true;
                _wsStep = 6;
                return;
        }
    }

    private static Construct? WsSpawned(Game g) => _wsBlueprint.Length > 0 ? g.Constructs.Get(_wsBody) : null;

    /// <summary>The player's eye to a place (noclip holds them there).</summary>
    private static void WsEyeTo(Game g, Player p, Vector3 eye)
    {
        var feet = eye - (p.Eye - p.Position);
        if (Vector3.DistanceSquared(World.ClampToArena(feet), feet) > 0.01f)
            WsSay($"note: {feet:0.0} is outside {World.Map.Name}'s arena or under its ground: the player stops at the edge (a bigger map: --map)");
        p.Teleport(feet);
    }

    private static void WsLook(Game g, Vector3 eye, Vector3 at)
    {
        var d = at - eye;
        if (d.LengthSquared() < 1e-6f)
            return;
        d = Vector3.Normalize(d);
        g.LookYaw = MathF.Atan2(d.X, -d.Z);
        g.LookPitch = Math.Clamp(MathF.Asin(Math.Clamp(d.Y, -1f, 1f)), -1.5f, 1.5f);
    }

    /// <summary>The n-th part (from 1) of a name on the spawned blueprint ("Extruder#2"), or any body when nothing was spawned.</summary>
    private static (Construct? C, int Part) WsFind(Game g, string spec)
    {
        var hash = spec.LastIndexOf('#');
        var name = hash > 0 ? spec[..hash].Trim() : spec.Trim();
        var nth = hash > 0 && int.TryParse(spec[(hash + 1)..], out var n) ? n : 1;
        var bodies = WsSpawned(g) is { } s ? [s] : g.Constructs.Bodies;
        foreach (var c in bodies)
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).Name.Equals(name, StringComparison.OrdinalIgnoreCase) && --nth == 0)
                    return (c, i);
        return (null, -1);
    }

    /// <summary>Where a part is looked at: the middle of its first screen, else the middle of the part.</summary>
    private static Vector3 WsPartSpot(Construct c, int part, out Vector3 normal)
    {
        var d = c.DefOf(part);
        if (d.Panels.Length > 0)
        {
            PanelTouch.Frame(c, part, d.Panels[0], out var centre, out normal, out _);
            return centre;
        }
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        normal = Vector3.Zero;
        return t + Vector3.Transform(p.Pos, q);
    }

    private static void WsRun(string action, double now)
    {
        if (_session is not { Local: { } me } g)
        {
            WsFail("the session ended");
            return;
        }
        var parts = action.Split(' ', 2, StringSplitOptions.TrimEntries);
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";
        WsSay($"> {action}");
        if (WsGhostVerb(g, me, verb, arg))
            return; // (the blueprint ghost's actions: Program.WindowShotGhost.cs)
        switch (verb)
        {
            case "wait":
                _wsWait = int.TryParse(arg, out var w) ? Math.Max(0, w) : 30;
                return;
            case "aim" or "near" or "panel":
            {
                var spec = verb == "near" && arg.LastIndexOf(' ') is var sp and > 0 && float.TryParse(arg[(sp + 1)..], System.Globalization.CultureInfo.InvariantCulture, out _) ? arg[..sp] : arg;
                var (c, part) = WsFind(g, spec);
                if (c == null)
                {
                    WsFail($"no part \"{spec}\"");
                    return;
                }
                var spot = WsPartSpot(c, part, out var normal);
                if (verb == "near")
                {
                    var dist = spec.Length < arg.Length ? float.Parse(arg[(spec.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture) : 1.6f;
                    var away = normal != Vector3.Zero ? normal : Vector3.Normalize(new Vector3(me.Eye.X - spot.X, 0, me.Eye.Z - spot.Z) + new Vector3(1e-4f, 0, 0));
                    WsEyeTo(g, me, spot + away * dist + (normal == Vector3.Zero ? new Vector3(0, dist * 0.6f + 0.5f, 0) : Vector3.Zero)); // (no screen: from above and aside)
                }
                if (verb == "panel")
                {
                    WsDumpPanels(g, c, part);
                    return;
                }
                WsLook(g, me.Eye, spot);
                _wsWait = 3;
                return;
            }
            case "stock": // what a store holds by name (slots, and kinds: a rack's pieces too) as this peer has it, and the loose one-part bodies within 6 m of it
            {
                var (sc, spart) = WsFind(g, arg);
                if (sc == null)
                {
                    WsFail($"no part \"{arg}\"");
                    return;
                }
                var held = new SortedDictionary<string, int>(StringComparer.Ordinal);
                void Count(ushort id, int n)
                {
                    if (id != ConstructParts.None && n > 0)
                        held[ConstructParts.Def(id).Name] = held.GetValueOrDefault(ConstructParts.Def(id).Name) + n;
                }
                if (g.Constructs.StoreOf(sc, spart) is { } sst)
                {
                    for (var k = 0; k < sst.SlotCount; k++)
                        Count(sst.Def[k], sst.Count[k]);
                    for (var k = 0; k < sst.KindN; k++)
                        Count(sst.KindDef![k], sst.KindCount![k]);
                }
                WsSay($"stock: {sc.DefOf(spart).Name}: {(held.Count == 0 ? "nothing" : string.Join(", ", held.Select(kv => $"{kv.Key} x{kv.Value}")))}");
                var spotS = WsPartSpot(sc, spart, out _);
                var reach = 6 + (sc.DefOf(spart).BoundsMax - sc.DefOf(spart).BoundsMin).Length(); // (6 m past a big machine's far side)
                var loose = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var b in g.Constructs.Bodies)
                    if (b != sc && b.PartCount == 1 && Vector3.Distance(b.WorldCom, spotS) < reach)
                        loose[b.DefOf(0).Name] = loose.GetValueOrDefault(b.DefOf(0).Name) + 1;
                WsSay($"stock: loose near it: {(loose.Count == 0 ? "nothing" : string.Join(", ", loose.Select(kv => $"{kv.Key} x{kv.Value}")))}");
                return;
            }
            case "feed": // host: a piece by its key into a press's bay, by the bays' own rule (its Piece in's: refused with the reason, or taken)
            {
                var keyAt = arg.IndexOf(" " + Extrusion.Prefix, StringComparison.Ordinal); // (PRESS may be two words: "Medium Press#1 2 Extrusion ...")
                var head = keyAt > 0 ? arg[..keyAt] : "";
                var sp = head.LastIndexOf(' ');
                var (fc, fpart) = sp > 0 && g.IsServer ? WsFind(g, head[..sp]) : (null, 0);
                if (fc == null || !int.TryParse(head[(sp + 1)..], out var fbay) || ConstructParts.Find(arg[(keyAt + 1)..]) is not { IsExtrusion: true } piece || g.Constructs.StoreOf(fc, fpart) is not { } fst)
                {
                    WsFail("feed PRESS[#n] BAY EXTRUSION-KEY (on the host or offline)");
                    return;
                }
                var why = ConstructPress.Refusal(PressTable.Current, fc, fpart, fbay - 1, piece.ExKey); // (the store's byte keeps an earlier refusal a while)
                var taken = ConstructPress.TakeFromConveyor(g.Constructs, World.Query, fc, fpart, fbay - 1, piece, 1, 0);
                WsSay($"feed: {piece.Name} into bay {fbay}: {(taken ? "taken" : why != BayRefuse.None ? $"refused ({why})" : "not taken (no room out of its mouth, or the bay is full)")}");
                _wsWait = 2;
                return;
            }
            case "kit": // host: N of a part or a material into a press's bay 2 (its kit hopper), by the hopper's own rule
            {
                var bits = arg.Split(' ');
                var at = Array.FindIndex(bits, b => int.TryParse(b, out _));
                var (kc, kpart) = at > 0 && at < bits.Length - 1 && g.IsServer ? WsFind(g, string.Join(' ', bits[..at])) : (null, 0);
                if (kc == null)
                {
                    WsFail("kit PRESS[#n] N PART-OR-MATERIAL (on the host or offline)");
                    return;
                }
                var n = int.Parse(bits[at]);
                var what = string.Join(' ', bits[(at + 1)..]);
                var took = 0;
                if (ConstructParts.Find(what) is { } kp && RecyclingTable.Current.IdOf(what) == 0)
                    for (var k = 0; k < n && ConstructPress.FeedKitPart(g.Constructs, kc, kpart, kp); k++)
                        took++;
                else
                    took = ConstructPress.FeedKitMaterial(g.Constructs, kc, kpart, n, RecyclingTable.Current.IdOf(what));
                WsSay($"kit: {took} of {n} {what} went into bay 2");
                _wsWait = 2;
                return;
            }
            case "timed": // host: machines work as in survival (their time and power) in this creative world
                g.Constructs.TimedMachines = true;
                WsSay("timed: machines take their time and need power");
                return;
            case "poke": // host: a button pressed as F presses it on the server (ConstructEdits.Use: its hold time), for a check that needs no aim
            {
                var (pc, ppart) = g.IsServer ? WsFind(g, arg) : (null, 0);
                if (pc == null || pc.DefOf(ppart).Logic != CLogic.Button)
                {
                    WsFail($"poke: no button \"{arg}\" (on the host or offline)");
                    return;
                }
                ref var bp = ref pc.Parts[ppart];
                if (ConstructConfig.Get(bp, bp.D, CfgKey.Latched, 0) >= 0.5f)
                    bp.State ^= 1; // (latched: push on, push off)
                else
                    bp.Count = (ushort)Math.Max(1, (int)MathF.Round(ConstructConfig.Get(bp, bp.D, CfgKey.Hold, ConstructLogic.ButtonSeconds) * World.TickRate));
                pc.Wake();
                WsSay($"poke: pressed the {pc.DefOf(ppart).Name}");
                _wsWait = 2;
                return;
            }
            case "joints": // each joint of the spawned body as this peer has it: its part, type, angle (a piston's metres), limits and input
                if (WsSpawned(g) is { } jc)
                    for (var j = 0; j < jc.JointCount; j++)
                    {
                        ref var jt = ref jc.Joints[j];
                        WsSay(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"joints: {j} {jc.DefOf(jt.Hinge).Name} {jt.Type} at {jt.Angle:0.000} ({jt.Min:0.00}..{jt.Max:0.00}) input {jt.Input:0.00} wired {jt.Wired}"));
                    }
                else
                    WsSay("joints: no spawned body");
                return;
            case "saveworld" or "loadworld": // host: the world saved under a name through the real save and copied out, or (a fresh process) that copy loaded; then the run-time parts of every body
            {
                WsSay($"{verb}: " + (verb == "saveworld" ? g.StationSaveProbe(arg) : g.StationLoadProbe(arg)));
                foreach (var b in g.Constructs.Bodies)
                {
                    var made = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    for (var i = 0; i < b.PartCount; i++)
                        if (b.DefOf(i).RuntimeMade)
                            made[b.DefOf(i).Name] = made.GetValueOrDefault(b.DefOf(i).Name) + 1;
                    if (made.Count > 0)
                        WsSay($"{verb}: body {b.Id} ({b.PartCount} parts): {string.Join(", ", made.Select(kv => $"{kv.Key} x{kv.Value}"))}");
                }
                _wsWait = 5;
                return;
            }
            case "use":
                _wsPress = GameAction.Interact;
                _wsWait = 3;
                return;
            case "key":
                if (!Enum.TryParse<GameAction>(arg, true, out var ga))
                {
                    WsFail($"no action \"{arg}\" ({string.Join(", ", Enum.GetNames<GameAction>())})");
                    return;
                }
                _wsPress = ga;
                _wsWait = 3;
                return;
            case "wheel":
                ScrollInput(int.TryParse(arg, out var by) ? by : 1);
                _wsWait = 2;
                return;
            case "tool":
                if (!Enum.TryParse<ConstructTool>(arg, true, out var tool))
                {
                    WsFail($"no tool \"{arg}\" ({string.Join(", ", Enum.GetNames<ConstructTool>())})");
                    return;
                }
                g.SandboxSend(CActKind.Tool, part: (byte)tool);
                _wsWait = 5;
                return;
            case "touch":
                WsTouch(g, me, arg);
                return;
            case "from": // "from PART[#n] SIDE,UP,OUT": the eye that far beside, above and in front of the part's screen (OUT negative: behind it), looking at it
            {
                var cut = arg.LastIndexOf(' ');
                var off = cut > 0 ? arg[(cut + 1)..].Split(',') : [];
                var (c, part) = WsFind(g, cut > 0 ? arg[..cut] : arg);
                if (c == null || off.Length != 3 || !off.All(o => float.TryParse(o, System.Globalization.CultureInfo.InvariantCulture, out _)))
                {
                    WsFail("from PART[#n] SIDE,UP,OUT");
                    return;
                }
                var v = off.Select(o => float.Parse(o, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var spot = WsPartSpot(c, part, out var normal);
                if (normal == Vector3.Zero)
                    normal = Vector3.UnitZ;
                WsEyeTo(g, me, spot + Vector3.Cross(Vector3.UnitY, normal) * v[0] + Vector3.UnitY * v[1] + normal * v[2]);
                WsLook(g, me.Eye, spot);
                _wsWait = 3;
                return;
            }
            case "mains":
            {
                var (c, part) = WsFind(g, "Switch");
                if (c == null)
                {
                    WsFail("no Switch on the blueprint");
                    return;
                }
                var wantOff = arg.Equals("off", StringComparison.OrdinalIgnoreCase);
                if (c.MainsOff == wantOff)
                    return;
                var spot = WsPartSpot(c, part, out _);
                WsEyeTo(g, me, spot + Vector3.Normalize(new Vector3(me.Eye.X - spot.X, 0, me.Eye.Z - spot.Z) + new Vector3(1e-4f, 0, 0)) * 1.4f + new Vector3(0, 0.3f, 0));
                WsLook(g, me.Eye, spot);
                _wsQueue.EnqueueFront("use");
                _wsWait = 3;
                return;
            }
            case "click": // the left mouse button at a window pixel "X,Y", through ImGui's own event queue (a panel's tile or button)
            {
                if (arg.Split(',') is not [var cx, var cy] || !float.TryParse(cx, System.Globalization.CultureInfo.InvariantCulture, out var px) || !float.TryParse(cy, System.Globalization.CultureInfo.InvariantCulture, out var py))
                {
                    WsFail($"click X,Y: \"{arg}\"");
                    return;
                }
                var io = Brutal.ImGuiApi.ImGui.GetIO();
                io.AddMousePosEvent(px, py);
                io.AddMouseButtonEvent(0, true);
                _wsQueue.EnqueueFront("clickup");
                _wsWait = 3;
                return;
            }
            case "drag": // the left button held from "X1,Y1" to "X2,Y2" over 12 frames, through ImGui's event queue (a slider dragged by the mouse)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (arg.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [var from, var to] || from.Split(',') is not [var ax, var ay] || to.Split(',') is not [var dbx, var dby]
                    || !float.TryParse(ax, ci, out var x1) || !float.TryParse(ay, ci, out var y1) || !float.TryParse(dbx, ci, out var x2) || !float.TryParse(dby, ci, out var y2))
                {
                    WsFail($"drag X1,Y1 X2,Y2: \"{arg}\"");
                    return;
                }
                var dio = Brutal.ImGuiApi.ImGui.GetIO();
                dio.AddMousePosEvent(x1, y1);
                dio.AddMouseButtonEvent(0, true);
                _wsQueue.EnqueueFront("clickup");
                for (var k = 12; k >= 1; k--)
                    _wsQueue.EnqueueFront(string.Create(ci, $"mousemove {x1 + (x2 - x1) * k / 12f:0.#},{y1 + (y2 - y1) * k / 12f:0.#}"));
                _wsWait = 2;
                return;
            }
            case "mousemove":
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (arg.Split(',') is [var mx, var my] && float.TryParse(mx, ci, out var mpx) && float.TryParse(my, ci, out var mpy))
                    Brutal.ImGuiApi.ImGui.GetIO().AddMousePosEvent(mpx, mpy);
                _wsWait = 1;
                return;
            }
            case "clickup":
                Brutal.ImGuiApi.ImGui.GetIO().AddMouseButtonEvent(0, false);
                _wsWait = 3;
                return;
            case "esc": // the Escape key, through the window's key handler
                OnKey(_window!, GlfwKey.Escape, 0, GlfwKeyAction.Press, default);
                _wsWait = 3;
                return;
            case "disown": // host: the spawned blueprint belongs to nobody, so a joined client may work its consoles and terminals
                if (g.IsServer && WsSpawned(g) is { } mine)
                {
                    mine.Owner = 255;
                    g.Constructs.StructureDirty.Add(mine.Id);
                    WsSay("disown: the blueprint is nobody's");
                }
                return;
            case "bring": // host: every other player to stand in front of a part's screen (a client's player can't be flown by its own tool)
            {
                var (bc, bpart) = WsFind(g, arg);
                if (bc == null)
                {
                    WsFail($"no part \"{arg}\"");
                    return;
                }
                var spot = WsPartSpot(bc, bpart, out var normal);
                var low = bc.DefOf(bpart).Panels is [var first, ..] && first.At.Y < 0.8f && MathF.Abs(normal.Y) < 0.5f; // (a Router's low screen: stand in a finger's reach)
                var stand = spot + (normal != Vector3.Zero ? normal : Vector3.UnitZ) * (low ? 1.0f : 1.8f);
                var feet = new Vector3(stand.X, World.Map.GroundHeight(stand.X, stand.Z) + 0.35f, stand.Z);
                var n = 0;
                foreach (var other in g.Players.Values)
                    if (other != me)
                    {
                        other.Teleport(feet);
                        n++;
                    }
                WsSay($"bring: {n} player(s) to {feet:0.0}");
                _wsWait = 5;
                return;
            }
            case "saveload": // host: the spawned body written as a world save writes it and read back into a fresh world; the parts' names before and after
            {
                if (WsSpawned(g) is not { } sb)
                {
                    WsFail("saveload: no spawned blueprint");
                    return;
                }
                var snap = new WorldSnapshot();
                g.Constructs.CaptureInto(snap);
                var names = new WorldNames();
                var body = new SaveWriter();
                ConstructWorld.WriteBody(body, snap, snap.Bodies.First(b => b.Id == sb.Id), names);
                var nameBytes = new SaveWriter();
                names.Write(nameBytes);
                var read = new WorldNamesRead();
                read.Read(new SaveReader(nameBytes.Span.ToArray()));
                var loaded = new ConstructWorld().ReadBody(new SaveReader(body.Span.ToArray()), read, _ => 0);
                var same = loaded != null && loaded.PartCount == sb.PartCount;
                for (var i = 0; i < sb.PartCount; i++)
                {
                    var after = loaded != null && i < loaded.PartCount ? loaded.DefOf(i).Name : "(missing)";
                    same &= after == sb.DefOf(i).Name;
                    WsSay($"saveload: part {i} {sb.DefOf(i).Name} -> {after}");
                }
                WsSay($"saveload: {(same ? "the same parts" : "DIFFERENT")} ({body.Span.Length} bytes)");
                return;
            }
            case "logi": // smart logistics (Program.WindowShotLogi.cs): riders, a Router's net and why; host: order, cut, fill, spawn
                WsLogi(g, arg);
                return;
            case "craft": // a Crafting Terminal's orders, tray and reserved parts as this peer has them; on the host, also a save and load of its body
            {
                var (cc, cpart) = WsFind(g, arg);
                if (cc == null || !cc.DefOf(cpart).IsCraftTerminal || g.Constructs.StoreOf(cc, cpart) is not { } tray)
                {
                    WsFail($"no Crafting Terminal \"{arg}\"");
                    return;
                }
                var q = cc.Parts[cpart].Craft;
                var kinds = string.Join(", ", Enumerable.Range(0, tray.KindN).Select(k => $"{tray.KindCount![k]} {ConstructParts.Get(tray.KindDef![k])?.Name}").OrderBy(x => x));
                var reserved = 0;
                for (var i = 0; i < g.Constructs.Reserve.Count; i++)
                    reserved += g.Constructs.Reserve.Items[i].Count;
                WsSay($"craft: {q?.Count ?? 0} order(s); tray [{kinds}]; reserved {reserved}");
                for (var k = 0; q != null && k < q.Count; k++)
                    WsSay($"craft:   #{q.Items[k].Id} {q.Items[k].Got}/{q.Items[k].What.Count} {ConstructCrafting.NameOf(q.Items[k].What)}: {ConstructCrafting.StateText(cc, q.Items[k])}");
                if (g.IsServer)
                {
                    var snap = new WorldSnapshot();
                    g.Constructs.CaptureInto(snap);
                    var names = new WorldNames();
                    var body = new SaveWriter();
                    ConstructWorld.WriteBody(body, snap, snap.Bodies.First(b => b.Id == cc.Id), names);
                    var nameBytes = new SaveWriter();
                    names.Write(nameBytes);
                    var read = new WorldNamesRead();
                    read.Read(new SaveReader(nameBytes.Span.ToArray()));
                    var w2 = new ConstructWorld();
                    var loaded = w2.ReadBody(new SaveReader(body.Span.ToArray()), read, _ => 0);
                    // (the body alone: the tray is a store, saved in the world's stores section, so it isn't in this round trip; saveworld covers it)
                    WsSay($"craft: saved and loaded: {(loaded == null ? "NO BODY" : $"{loaded.Parts[cpart].Craft?.Count ?? 0} order(s) (the tray is saved with the world's stores, not the body)")}");
                }
                return;
            }
            case "design": // the Panel Designer's panel: "design" reports it; "design preset 2", "design length 600" set the sliders as a drag would (clamped the same)
            {
                if (arg is "await" or "held" || arg.StartsWith("bring ", StringComparison.Ordinal))
                {
                    // Two windows keep no common clock: wait for the other side instead of counting frames. "await": press use until the designer's
                    // panel is open; "held": until a store holds a formed panel; "bring N": until N other players are here, then bring them to the desk.
                    var others = g.Players.Count - 1;
                    var held = false;
                    foreach (var hs in g.Constructs.Stores)
                        for (var k = 0; hs is { Live: true } && k < hs.KindN; k++)
                            held |= ConstructParts.Get(hs.KindDef![k]) is { } hd && HullLoft.IsPanelName(hd.Name) && hs.KindCount![k] > 0;
                    var ready = arg == "await" ? Ui.DesignerOpen : arg == "held" ? held : int.TryParse(arg[6..], out var need) && others >= need;
                    if (!ready)
                    {
                        if (++_wsDesignTries > 400)
                        {
                            WsFail($"design {arg}: never happened");
                            return;
                        }
                        _wsQueue.EnqueueFront("design " + arg);
                        if (arg == "await")
                        {
                            _wsQueue.EnqueueFront("use");
                            if (WsFind(g, "Panel Designer").Item1 != null)
                                _wsQueue.EnqueueFront("aim Panel Designer"); // (a client is brought to the desk by the host: it looks at the screen itself)
                        }
                        _wsWait = 30;
                        return;
                    }
                    WsSay($"design {arg}: after {_wsDesignTries} waits");
                    _wsDesignTries = 0;
                    if (arg.StartsWith("bring ", StringComparison.Ordinal) && WsFind(g, "Panel Designer").Item1 != null)
                        _wsQueue.EnqueueFront("bring Panel Designer"); // (no desk: they only had to be here)
                    _wsWait = 20;
                    return;
                }
                if (arg.StartsWith("drag ", StringComparison.Ordinal) && int.TryParse(arg[5..], out var dragFrames))
                {
                    // A slider dragged: a new length every frame for N frames (each a new key), then what it cost and what it left behind.
                    if (_wsDragLeft == 0)
                    {
                        _wsDragLeft = _wsDragAll = Math.Max(1, dragFrames);
                        _wsDragBytes = GC.GetTotalAllocatedBytes();
                        _wsDragAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        (Ui.DesignerSpentMs, Ui.DesignerWorstMs) = (0, 0);
                        _wsDragKeys = Ui.DesignerPreview.Made;
                    }
                    Ui.DesignerSet("length", 20 + (_wsDragAll - _wsDragLeft) * 5 % 230);
                    if (--_wsDragLeft > 0)
                    {
                        _wsQueue.EnqueueFront(action);
                        _wsWait = 1;
                        return;
                    }
                    var secs = System.Diagnostics.Stopwatch.GetElapsedTime(_wsDragAt).TotalSeconds;
                    WsSay($"design drag: {_wsDragAll} values in {secs:0.0} s ({secs * 1000 / _wsDragAll:0.0} ms a frame, all of the game); the preview's own work {Ui.DesignerSpentMs / _wsDragAll:0.000} ms a frame, worst {Ui.DesignerWorstMs:0.00} ms; {(GC.GetTotalAllocatedBytes() - _wsDragBytes) / _wsDragAll} bytes a frame (the whole process); {Ui.DesignerPreview.Made - _wsDragKeys} keys previewed; {RuntimeParts.Line()}");
                    return;
                }
                var dw = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (arg == "open") // the panel opened on the first Panel Designer as the use key on its desk opens it (the aim skipped)
                {
                    if (WsFind(g, "Panel Designer") is ({ } oc, var op))
                        OpenPanelDesigner(oc, op);
                    WsSay($"design open: {(Ui.DesignerOpen ? "open" : "NOT OPEN")}");
                    _wsWait = 5;
                    return;
                }
                if (arg is "save" or "order")
                {
                    Ui.DesignerPress(arg); // (the button, pressed at the panel's next frame)
                    _wsWait = 3;
                    return;
                }
                if (arg == "parts") // every formed panel placed on a body, by name and geometry hash: two peers compare them
                {
                    foreach (var b in g.Constructs.Bodies)
                        for (var i = 0; i < b.PartCount; i++)
                            if (HullLoft.IsPanelName(b.DefOf(i).Name))
                                WsSay($"design: placed on body {b.Id} part {i}: {b.DefOf(i).Name} hash {HullLoft.PanelHash(b.DefOf(i)):x8}");
                    return;
                }
                if (dw.Length >= 2 && dw[0] == "profile" && dw[1].Split(',') is { Length: 2 } pv && int.TryParse(pv[0], out var ph) && int.TryParse(pv[1], out var pcm))
                {
                    Ui.DesignerProfile(ph, pcm); // "profile H,CM": a loft's handle H (0-7 the back edge, 8-15 the nose) to CM, as a drag would
                    _wsWait = 3;
                }
                else if (dw.Length >= 2 && dw[0] == "name")
                    Ui.DesignerName(arg[(arg.IndexOf(' ') + 1)..].Trim());
                else
                    if (dw.Length == 2 && int.TryParse(dw[1], out var dv))
                {
                    Ui.DesignerSet(dw[0], dv);
                    _wsWait = 3;
                }
                var ds = Ui.DesignerShape;
                WsSay($"design: {(Ui.DesignerOpen ? "open" : "closed")}; {(HullLoft.PanelRefusal(ds) ?? HullLoft.PanelKey(ds))}; preview {(Ui.DesignerPreview.Shown == null ? "none" : $"{HullLoft.PanelKey(Ui.DesignerPreview.ShownShape)} hash {HullLoft.PanelHash(Ui.DesignerPreview.Shown):x8}")}{(Ui.DesignerPreview.Building ? " (building)" : "")}, {Ui.DesignerPreview.Made} keys previewed; library {Ui.DesignerDesigns}; world designs [{string.Join("; ", g.Constructs.Designs.Select(x => $"{x.Name} = {x.Key}"))}]");
                WsSay($"design: {RuntimeParts.Line()}");
                foreach (var st in g.Constructs.Stores)
                    for (var k = 0; st is { Live: true } && k < st.KindN; k++)
                        if (ConstructParts.Get(st.KindDef![k]) is { } fd && HullLoft.IsPanelName(fd.Name))
                            WsSay($"design:   held {st.KindCount![k]} x {fd.Name} hash {HullLoft.PanelHash(fd):x8}");
                if (g.IsServer && g.Constructs.Designs.Count > 0)
                {
                    var sw = new SaveWriter();
                    PanelDesigns.WriteSave(sw, g.Constructs.Designs);
                    var w3 = new ConstructWorld();
                    PanelDesigns.ReadSave(new SaveReader(sw.Span.ToArray()), w3);
                    WsSay($"design: saved and loaded: {w3.Designs.Count} design(s)");
                }
                return;
            }
            case "station": // a Building Station: "station" prints the first one's job; then "station pick NAME", start [N], cancel, release, stock (host: the parts it lacks into its hold), hold NAME (host: that part into this player's hands), save (host: its body saved and loaded)
            {
                var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (g.IsServer && arg.StartsWith("loadworld ", StringComparison.Ordinal))
                    WsSay("station: " + g.StationLoadProbe(arg[10..])); // (first: the station is in the world it loads)
                var (sc, spart) = WsFind(g, "Building Station");
                if (sc == null || !sc.DefOf(spart).IsBuildStation)
                {
                    WsFail($"no Building Station {arg}");
                    return;
                }
                var sverb = words.Length > 0 ? words[0] : "";
                var rest = words.Length > 1 ? words[1] : "";
                switch (sverb)
                {
                    case "pick":
                        WsSay($"station: pick {rest}: {(Ui.StationPick(rest) ? "ok" : "NO SUCH BLUEPRINT")}");
                        break;
                    case "start":
                        g.SandboxStation(sc.Id, spart, StationOp.Start, (ushort)(int.TryParse(rest, out var times) ? Math.Max(0, times - 1) : 0), Ui.StationPicked);
                        break;
                    case "cancel":
                        g.SandboxStation(sc.Id, spart, StationOp.Cancel);
                        break;
                    case "release":
                        g.SandboxStation(sc.Id, spart, StationOp.Release);
                        break;
                    case "give":
                        g.SandboxStation(sc.Id, spart, StationOp.Give);
                        break;
                    case "stock" when g.IsServer:
                        WsSay($"station: stock: {StationProbe.Stock(g.Constructs, sc, spart)}");
                        break;
                    case "hold" when g.IsServer && ConstructParts.Find(rest) is { } hd:
                        foreach (var pl in g.Players.Values)
                            g.SandboxHands.Hands[pl.Id].Held = (ushort)hd.Id; // (every player: a client's give is tried with it)
                        WsSay($"station: every player holds a {hd.Name}{(hd.PhysicsOnly ? " (PHYSICS-ONLY: real hands refuse it)" : "")}");
                        break;
                    case "reload" when g.IsServer: // the whole world through the real save and the real load, in place
                        WsSay("station: reload: " + g.StationReloadProbe());
                        (sc, spart) = WsFind(g, "Building Station");
                        if (sc == null)
                        {
                            WsFail("no Building Station after the load");
                            return;
                        }
                        break;
                    case "drop" when g.IsServer && ConstructParts.Find(rest) is { } loose: // a loose part set down in the bay, as the physics gun would leave it
                    {
                        ConstructStation.Bay(sc, spart, out var bfloor, out var bturn, out var bsize);
                        var made = ConstructBuild.PlaceNew(g.Constructs, loose, bfloor + System.Numerics.Vector3.Transform(new System.Numerics.Vector3(bsize.X * 0.3f, 0.3f, bsize.Z * 0.3f), bturn), 0);
                        WsSay(made == null ? "station: drop: no room" : "station: a loose " + loose.Name + " lies in the bay (construct " + made.Id + ")");
                        break;
                    }
                    case "costtop": // the biggest shipped blueprints a 64 m bay doesn't refuse from their parts, costed as a station does
                        foreach (var line in StationCostProbe.CostTop(new System.Numerics.Vector3(64, 64, 64), int.TryParse(rest, out var topN) ? topN : 6))
                            WsSay("station: cost: " + line);
                        break;
                    case "saveworld" when g.IsServer:
                        WsSay("station: " + g.StationSaveProbe(rest));
                        break;
                    case "loadworld" when g.IsServer:
                        // (loaded above, before the station was looked for)
                        (sc, spart) = WsFind(g, "Building Station");
                        if (sc == null)
                        {
                            WsFail("no Building Station after the load");
                            return;
                        }
                        break;
                    case "repeat":
                        g.SandboxStation(sc.Id, spart, StationOp.Repeat, (ushort)(int.TryParse(rest, out var more) ? more : 0));
                        break;
                    case "await": // "station await fitted|linked|done": the script waits (a frame at a time, 100 s at most: no long runs) until the job gets there
                    {
                        var aj = sc.Parts[spart].Station;
                        var met = rest switch
                        {
                            "fitted" => aj is { Steps: > 0 } && aj.Fitted >= aj.Steps,
                            "linked" => aj is { Steps: > 0 } && aj.Fitted >= aj.Steps && aj.LinksDone >= aj.Links,
                            "done" => aj is { State: StationState.Done or StationState.Failed },
                            _ => true,
                        };
                        if (!met && _wsAwait++ < 60 * 100)
                        {
                            _wsQueue.EnqueueFront("station await " + rest);
                            _wsWait = 1;
                            return;
                        }
                        var waited = _wsAwait;
                        _wsAwait = 0;
                        if (!met)
                        {
                            WsFail($"station await {rest}: not reached after {waited} frames ({StationProbe.Text(g.Constructs, sc, spart)})");
                            return;
                        }
                        WsSay($"station: await {rest}: reached after {waited} frames");
                        break;
                    }
                    case "tanks": // the build's fuel, Fluid Tanks and ammo boxes as this peer has them, and the station's own source
                        WsSay("station: tanks: " + StationProbe.Tanks(g.Constructs, sc, spart));
                        break;
                    case "clear" when g.IsServer && sc.Parts[spart].Station is { } cj && g.Constructs.Get(cj.Body) is { HeldBy: 0 } built: // the finished vehicle is dragged 14 m out of the bay
                        ConstructStation.Bay(sc, spart, out _, out var brot, out _);
                        built.Pos += System.Numerics.Vector3.Transform(new System.Numerics.Vector3(14, 0.3f, 0), brot);
                        built.PrevPos = built.NetPos = built.NetFromPos = built.Pos;
                        built.NetHistReset();
                        built.Wake();
                        WsSay("station: the built vehicle was dragged out of the bay");
                        break;
                    case "measure":
                        StationProbe.On = true;
                        Ui.PerfMode = PerfView.Overlay; // (the profiler times the server tick while its overlay is up)
                        WsSay("station: " + StationProbe.Cost());
                        {
                            var ticks = new List<float>();
                            for (var back = 0; back < Perf.History; back++)
                                if (Perf.Ticks(back) > 0)
                                    ticks.Add(Perf.TickWorstMs(back));
                            ticks.Sort();
                            WsSay(ticks.Count == 0 ? "station: no frames measured yet" : "station: server tick over the last " + ticks.Count + " frames: median " + ticks[ticks.Count / 2].ToString("0.00") + " ms, p99 " + ticks[(int)(ticks.Count * 0.99)].ToString("0.00") + " ms, worst " + ticks[^1].ToString("0.00") + " ms");
                        }
                        break;
                    case "save" when g.IsServer:
                    {
                        var snap = new WorldSnapshot();
                        g.Constructs.CaptureInto(snap);
                        var names = new WorldNames();
                        var body = new SaveWriter();
                        ConstructWorld.WriteBody(body, snap, snap.Bodies.First(b => b.Id == sc.Id), names);
                        var nameBytes = new SaveWriter();
                        names.Write(nameBytes);
                        var read = new WorldNamesRead();
                        read.Read(new SaveReader(nameBytes.Span.ToArray()));
                        var w2 = new ConstructWorld();
                        var loaded = w2.ReadBody(new SaveReader(body.Span.ToArray()), read, _ => 0);
                        WsSay($"station: saved ({body.Length} bytes) and loaded: {(loaded == null ? "NO BODY" : StationProbe.Text(w2, loaded, spart))}; fitted steps kept {loaded?.Parts[spart].Station?.PartOf.Count(p => p >= 0) ?? -1}");
                        break;
                    }
                }
                WsSay($"station: {StationProbe.Text(g.Constructs, sc, spart)}");
                if (!ConstructRecycler.Powered(g.Constructs, sc, spart))
                    WsSay("station: " + StationPowerProbe.Text(sc));
                if (Ui.StationPreview is { Ready: true } pv && sverb == "pick")
                    WsSay($"station: preview {pv.Name}: {pv.Parts} parts, cost {pv.CostMs:0.0} ms, worst slice {pv.WorstMs:0.00} ms");
                _wsWait = 2;
                return;
            }
            case "picker": // "picker": the open recipe picker's tiles (value, or dim); "picker N": picks the tile of value N as a click does;
                // "picker @PART[#n]": opens it on that machine as its Recipe button does (a joined window, whose eye a touch can't fly)
                if (arg.StartsWith('@'))
                {
                    var (pc, pp) = WsFind(g, arg[1..].Trim());
                    WsSay(pc != null && MachineRecipePicker(pc, pp) ? $"picker: opened on {pc.DefOf(pp).Name} (part {pp})" : $"picker: not opened on {arg[1..].Trim()}");
                    _wsWait = 3;
                }
                else if (arg.Length > 0 && char.IsLetter(arg[0]))
                {
                    // An Extruder's picker (UiRecipePickerExtruder.cs): "picker size W H [WALL]" (cm), "picker cell N", "picker set".
                    WsSay($"picker: {Ui.ExtruderPickerDo(arg) ?? "no Extruder picker open"}");
                    _wsWait = 3;
                }
                else if (int.TryParse(arg, out var pickValue))
                {
                    Ui.RecipePickerPick(pickValue);
                    _wsWait = 3;
                }
                else
                    WsSay($"picker: {Ui.RecipePickerDump()}");
                return;
            case "ui": // what is open and who has the mouse
                WsSay($"ui: menus {(MenusOpen ? "open" : "closed")}, crafting {(Ui.CraftingOpen ? "open" : "closed")} ({Ui.CraftingTiles} tiles), designer {(Ui.DesignerOpen ? "open" : "closed")}, mouse {(_mouseCaptured ? "captured" : "free")}");
                return;
            case "shot":
                WsShot(arg.Length > 0 ? arg : $"{_wsShots:000}");
                _wsWait = 1;
                return;
            case "link": // a data link FROM's Data out (or only output) to TO, as the linking tool sends it: "link Materials Bin#1 > Text Sign#3"; "link pipe Fuel Tank#1 > Building Station": a straight pipe from FROM's first pipe out to TO's first pipe in
            {
                var pipe = arg.StartsWith("pipe ", StringComparison.Ordinal);
                var ends = (pipe ? arg[5..] : arg).Split('>', StringSplitOptions.TrimEntries);
                var (c, from) = ends.Length == 2 ? WsFind(g, ends[0]) : (null, -1);
                var (c2, to) = ends.Length == 2 ? WsFind(g, ends[1]) : (null, -1);
                if (c == null || c2 != c)
                {
                    WsFail($"link FROM[#n] > TO[#m] on one construct (\"{arg}\")");
                    return;
                }
                if (g.SandboxHands.Hand(me.Id).Tool != ConstructTool.Linker)
                {
                    g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Linker);
                    _wsQueue.EnqueueFront($"link {arg}");
                    _wsWait = 5;
                    return;
                }
                var spot = WsPartSpot(c, to, out _);
                WsEyeTo(g, me, spot + new Vector3(0, 1.2f, 0) + Vector3.Normalize(new Vector3(me.Eye.X - spot.X, 0, me.Eye.Z - spot.Z) + new Vector3(1e-4f, 0, 0)) * 1.5f);
                if (pipe)
                {
                    byte outP = 255, inP = 255;
                    foreach (var np in c.DefOf(from).NamedPorts)
                        if (np.Type == RouteType.Pipe && np.Out && outP == 255)
                            outP = np.Byte;
                    foreach (var np in c.DefOf(to).NamedPorts)
                        if (np.Type == RouteType.Pipe && np.In && inP == 255)
                            inP = np.Byte;
                    if (!g.IsServer)
                    {
                        WsFail("link pipe: on the host (a test state, laid without the tool's reach)");
                        return;
                    }
                    // Out of each port half a metre, straight up 5 m (over the machines), and across in steps of at most 4 m.
                    var up = new Vector3(0, 5f, 0);
                    var pa = ConstructRoutes.PortPoint(c, from, outP, RouteType.Pipe) + ConstructRoutes.PortDir(c, from, outP, RouteType.Pipe) * 0.5f;
                    var pb = ConstructRoutes.PortPoint(c, to, inP, RouteType.Pipe) + ConstructRoutes.PortDir(c, to, inP, RouteType.Pipe) * 0.5f;
                    var segs = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(pa, pb) / 4f));
                    var pnodes = new CRouteNode[segs + 3];
                    pnodes[0] = new CRouteNode { Host = (ushort)from, Pos = pa };
                    for (var s = 0; s <= segs; s++)
                        pnodes[s + 1] = new CRouteNode { Host = (ushort)(s * 2 < segs ? from : to), Pos = Vector3.Lerp(pa, pb, s / (float)segs) + up };
                    pnodes[segs + 2] = new CRouteNode { Host = (ushort)to, Pos = pb };
                    var bad = ConstructRoutes.Check(g.Constructs, c, RouteType.Pipe, from, outP, 255, 0, default, pnodes, to, inP);
                    if (bad == null && ConstructRoutes.Add(g.Constructs, c, RouteType.Pipe, from, outP, 255, 0, default, pnodes, to, inP) < 0)
                        bad = "no room for another link";
                    if (bad == null)
                    {
                        c.Rebuild();
                        g.Constructs.StructureDirty.Add(c.Id);
                    }
                    WsSay($"link: pipe {c.DefOf(from).Name}#{from} > {c.DefOf(to).Name}#{to}: {bad ?? "laid"}");
                    _wsWait = 5;
                    return;
                }
                var port = c.DefOf(from).DataOut is { Length: > 1 } outs ? ConstructRoutes.DataPortByte(Array.IndexOf(outs, "Data out")) : (byte)255;
                g.SandboxSendRoute(c.Id, RouteType.Data, from, port, 255, 0, default, ReadOnlySpan<CRouteNode>.Empty, to, 255);
                _wsWait = 5;
                return;
            }
            case "fill": // a store's bin set on the server (a test state: a full output, another material), sent as any change is
            {
                var bits = arg.Split(' ');
                var at = Array.FindLastIndex(bits, b => int.TryParse(b, out _)); // (a material may be two words: "iron ore")
                var amount = at > 0 ? int.Parse(bits[at]) : 0;
                if (at < 1 || at == bits.Length - 1 || !g.IsServer)
                {
                    WsFail("fill PART[#n] N MATERIAL, link FROM[#n] > TO[#m] (on the host or offline)");
                    return;
                }
                var (c, part) = WsFind(g, string.Join(' ', bits[..at]));
                var mat = RecyclingTable.Current.IdOf(string.Join(' ', bits[(at + 1)..]));
                if (c == null || g.Constructs.StoreOf(c, part) is not { } st || mat == 0)
                {
                    WsFail($"fill: no store or material in \"{arg}\"");
                    return;
                }
                st.Amount = Math.Clamp(amount, 0, c.DefOf(part).BulkCapacity);
                st.Material = st.Amount > 0 ? mat : (byte)0;
                st.Version++;
                g.Constructs.StoreDirty.Add(st.Handle);
                _wsWait = 2;
                return;
            }
            case "deposit": // the nearest ground a drill of this tier taps (rings out from the player, 4 m apart): for --spawn-at
            {
                var tier = int.TryParse(arg, out var t) ? t : 1;
                if (World.Map.Resources is not { } field)
                {
                    WsSay("deposit: this map has no resources");
                    return;
                }
                for (var r = 0f; r <= 600; r += 4)
                    for (var a = 0f; a < MathF.Tau; a += r > 0 ? 4 / r : MathF.Tau)
                    {
                        float x = me.Position.X + r * MathF.Cos(a), z = me.Position.Z + r * MathF.Sin(a);
                        if (field.TappableAt(x, z) is { } kind && kind.MinTier <= tier && WsDeep(field, x, z, kind))
                        {
                            WsSay($"deposit: {kind.Name} at {x:0},{field.Ground(x, z):0.0},{z:0} ({r:0} m away)");
                            return;
                        }
                    }
                WsSay("deposit: none within 600 m");
                return;
            }
        }
        WsFail($"unknown action \"{action}\"; actions: {WsActions}");
    }

    /// <summary>Well inside a deposit: the same kind 12 m out every way (a blueprint spawned near it lands on it).</summary>
    private static bool WsDeep(ResourceField field, float x, float z, ResourceKind kind)
    {
        for (var k = 0; k < 8; k++)
            if (field.TappableAt(x + 12 * MathF.Cos(k * MathF.Tau / 8), z + 12 * MathF.Sin(k * MathF.Tau / 8)) != kind)
                return false;
        return true;
    }

    /// <summary>A finger on a screen's button by its label, through the crosshair: Hands out, the eye in front of it, the view on it, then LMB.</summary>

    private static void WsTouch(Game g, Player me, string arg)
    {
        var screen = -1; // "Hold: Off @ Conveyor Router /1": that part's second screen only (a part with a screen each side)
        if (arg.LastIndexOf(" /", StringComparison.Ordinal) is var sl and > 0 && int.TryParse(arg[(sl + 2)..], out var sn))
            (arg, screen) = (arg[..sl], sn);
        var on = arg.IndexOf('@'); // "Bill @ Line Console#2": that part's button only
        var label = on > 0 ? arg[..on].Trim() : arg;
        var (only, onlyPart) = on > 0 ? WsFind(g, arg[(on + 1)..]) : (null, -1);
        if (on > 0 && only == null)
        {
            WsFail($"no part \"{arg[(on + 1)..].Trim()}\"");
            return;
        }
        foreach (var c in only != null ? [only] : WsSpawned(g) is { } s ? [s] : g.Constructs.Bodies)
            for (var part = 0; part < c.PartCount; part++)
            {
                if (only != null && part != onlyPart)
                    continue;
                var d = c.DefOf(part);
                for (var n = 0; n < d.Panels.Length; n++)
                {
                    if (screen >= 0 && n != screen)
                        continue;
                    var panel = d.Panels[n];
                    var card = WsFill(g, c, part, panel);
                    for (var b = 0; b < panel.Touch.Length; b++)
                    {
                        var now = card.TouchLabels[b] ?? panel.Touch[b].Label;
                        if (!now.Equals(label, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (g.SandboxHands.Hand(me.Id).Tool != ConstructTool.Hands)
                        {
                            g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Hands);
                            _wsQueue.EnqueueFront(screen >= 0 ? $"touch {arg} /{screen}" : $"touch {arg}");
                            _wsWait = 5;
                            return;
                        }
                        PanelTouch.Frame(c, part, panel, out var centre, out var normal, out var up);
                        var right = Vector3.Cross(up, normal);
                        var r = panel.Touch[b].At;
                        var spot = centre + right * (((r.X + r.Z) * 0.5f - 0.5f) * panel.Size.X) + up * (((r.Y + r.W) * 0.5f - 0.5f) * panel.Size.Y);
                        var moved = Vector3.Distance(me.Eye, spot) > 2.2f || Vector3.Dot(me.Eye - spot, normal) < 0.3f;
                        if (moved && !g.IsServer)
                        {
                            // (a joined client can't fly itself: the host owns its position and snaps a teleport back. It presses from where it
                            // stands, as a player would, when that is in reach and in front; else the host brings it first: "bring PART")
                            if (Vector3.Distance(me.Eye, spot) > ConstructBuild.UseReach - 0.1f || Vector3.Dot(me.Eye - spot, normal) < 0.1f)
                            {
                                WsFail($"touch: the joined client stands {Vector3.Distance(me.Eye, spot):0.0} m from that button (eye {me.Eye:0.0}); the host brings it first: bring PART");
                                return;
                            }
                            moved = false;
                        }
                        if (moved)
                            WsEyeTo(g, me, panel.At.Y < 0.8f && MathF.Abs(normal.Y) < 0.5f
                                ? centre + normal * 0.9f + Vector3.UnitY * 1.2f // (a low screen, a Router's: read standing, looking down, as a player does)
                                : centre + normal * 1.3f);
                        WsLook(g, me.Eye, spot);
                        {
                            // (what the crosshair's ray meets on the way, as the pointer will see it: the ground or a wall first, another part, or this button)
                            var dir = g.LookDir(g.LookYaw, g.LookPitch);
                            var wall = World.RaycastWorld(me.Eye, dir, ConstructBuild.Reach + 1);
                            var aimed = g.Constructs.Raycast(me.Eye, dir, MathF.Min(wall, ConstructBuild.Reach + 1), out var th, look: true, noTracks: true);
                            Vector2 tat = default;
                            var tp = aimed && th.Part >= 0 && th.Part < th.Body.PartCount ? PanelTouch.Find(th.Body, th.Part, me.Eye, dir, out tat) : -1;
                            WsSay($"touch ray: button {Vector3.Distance(me.Eye, spot):0.00} m (reach {ConstructBuild.UseReach}), ground or wall {wall:0.00} m, "
                                + (aimed ? $"hit {th.Body.DefOf(th.Part).Name} part {th.Part} at {th.Distance:0.00} m, slot {th.SlotPlus1}, route {th.RoutePlus1}, panel {tp}{(tp >= 0 ? $" button {PanelTouch.Region(th.Body.DefOf(th.Part).Panels[tp], tat)}" : "")}" : "no part"));
                        }
                        _wsQueue.EnqueueFront("key Fire");
                        _wsWait = 3; // (the pointer finds the button first, then the click)
                        WsSay($"touch: {d.Name} (part {part}) panel {n} button {b} \"{now}\"");
                        return;
                    }
                }
            }
        WsFail($"no button \"{label}\" on any screen");
    }

    private static PanelCard WsFill(Game g, Construct c, int part, PanelDef panel)
    {
        var card = new PanelCard { Touch = panel.Touch };
        var d = c.DefOf(part);
        var percent = ConstructConfig.Has(d, CfgKey.TextSize) ? ConstructConfig.Get(c.Parts[part], d, CfgKey.TextSize, 100) : 100;
        var source = panel == d.Panels[0] ? ProductionLines.MonitorSource(c, part) : -1;
        if (source >= 0)
        {
            card.Rows = card.TextRows = ProductionLines.MonitorRows(panel, percent, out var cols);
            card.Cols = cols;
            ProductionLines.FillMonitor(g.Constructs, c, source, card);
        }
        else if (panel.Fill != null)
        {
            card.Rows = ConstructPanels.RowsAt(panel, percent);
            card.Cols = Math.Min(PanelCard.MaxChars, PanelRender.Columns(panel.Size, card.Rows));
            card.TextRows = ConstructPanels.FreeRows(panel, card.Rows);
            panel.Fill(g.Constructs, c, part, panel, card);
        }
        return card;
    }

    /// <summary>A part's screens as this peer fills them, and what is open over the match.</summary>
    private static void WsDumpPanels(Game g, Construct c, int part)
    {
        var d = c.DefOf(part);
        WsSay($"panel: {d.Name} (part {part} of construct {c.Id}) at {WsPartSpot(c, part, out _):0.00}, {d.Panels.Length} screen(s); eye {g.Local?.Eye:0.00}, look {g.LookDir(g.LookYaw, g.LookPitch):0.00}; open:recipe picker {Ui.RecipePickerOpen}, configure {Ui.SandboxConfigOpen}, pause {Ui.PauseOpen}, menus {MenusOpen}");
        WsSay($"  logic: out {c.Parts[part].Out:0.##}, in {c.Parts[part].In:0.##}, state {c.Parts[part].State}, count {c.Parts[part].Count}; the body: {c.WireCount} wires, logic {c.HasLogic}, powered {c.Parts[part].Powered}");
        if (g.Constructs.StoreOf(c, part) is { } st)
            WsSay($"  store: line state {(ProductionLines.Raw(g.Constructs, c, part, st) is { } s ? ProductionLines.Word(s) : "-")}, busy {st.Busy}, held {st.Held}, blocked {st.Blocked}, {st.Amount} {RecyclingTable.Current.NameOf(st.Material)}, {st.ItemCount} items, mains off {c.MainsOff}; creative {g.Constructs.CreativeRecycling} (parts {g.SandboxInfiniteParts}, survival option {g.SandboxSurvivalOption}, map {World.Map.Name})");
        if (d.Monitor != MonitorKind.None && (d.Panels.Length == 0 || d.Monitor is MonitorKind.Store or MonitorKind.Network))
        {
            // What a display on its Data out shows: a medium one (6 rows of 24) and a tiny one (a line); a Router's board also as a 14-row one of 40.
            foreach (var rows in d.Monitor == MonitorKind.Network ? (ReadOnlySpan<int>)[16, 6, 1] : [6, 1])
            {
                var card = new PanelCard { Rows = rows, TextRows = rows, Cols = rows > 6 ? 64 : 24 };
                ProductionLines.FillMonitor(g.Constructs, c, part, card);
                for (var l = 0; l < rows; l++)
                    if (card.Length(l) > 0)
                        WsSay($"  monitor{rows} {l}| {card.Line(l)}");
            }
        }
        for (var n = 0; n < d.Panels.Length; n++)
        {
            var card = WsFill(g, c, part, d.Panels[n]);
            WsSay($"  screen {n} ({d.Panels[n].Source}, {card.Rows} rows x {card.Cols}): state {card.State}, bar {card.Bar:0.00}, scroll {card.Scroll}/{card.ScrollMax}");
            for (var l = 0; l < PanelCard.MaxLines; l++)
                if (card.Length(l) > 0)
                    WsSay($"  {l,2}| {card.Line(l)}");
            for (var b = 0; b < d.Panels[n].Touch.Length; b++)
                WsSay($"  button {b}: {card.TouchLabels[b] ?? d.Panels[n].Touch[b].Label}");
        }
    }

    /// <summary>The frame is saved at its end (after the UI): OUT, or OUT_NAME for a series or a named shot.</summary>
    private static void WsShot(string? name)
    {
        var ext = Path.GetExtension(_wsOut);
        _wsShotPath = name == null ? _wsOut : Path.Combine(Path.GetDirectoryName(_wsOut) ?? ".", $"{Path.GetFileNameWithoutExtension(_wsOut)}_{name}{(ext.Length > 0 ? ext : ".png")}");
        _wsShots++;
    }

    /// <summary>The main loop's end of frame: the back buffer read and written, full size.</summary>
    private static void WindowShotSave()
    {
        if (_wsShotPath is not { } path)
            return;
        _wsShotPath = null;
        var fb = _window.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var rgba = new byte[w * h * 4];
        OpenGl.Native.glPixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* px = rgba)
            OpenGl.Native.glReadPixels(0, 0, w, h, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, px);
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4, o = (y * w + x) * 3; // (GL rows run bottom up)
                (rgb[o], rgb[o + 1], rgb[o + 2]) = (rgba[i], rgba[i + 1], rgba[i + 2]);
            }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                WsBmp(path, w, h, rgb);
            else
                MapKit.PngWriter.Write(path, w, h, rgb);
            _wsSaved.Add(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WsFail($"can't write {path}: {ex.Message}");
        }
    }

    private static void WsBmp(string path, int w, int h, byte[] rgb)
    {
        var rowBytes = (w * 3 + 3) & ~3;
        using var f = new BinaryWriter(File.Create(path));
        f.Write((byte)'B');
        f.Write((byte)'M');
        f.Write(54 + rowBytes * h);
        f.Write(0);
        f.Write(54);
        f.Write(40);
        f.Write(w);
        f.Write(h);
        f.Write((short)1);
        f.Write((short)24);
        f.Write(0);
        f.Write(rowBytes * h);
        f.Write(2835);
        f.Write(2835);
        f.Write(0);
        f.Write(0);
        var row = new byte[rowBytes];
        for (var y = h - 1; y >= 0; y--)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 3;
                (row[x * 3], row[x * 3 + 1], row[x * 3 + 2]) = (rgb[i + 2], rgb[i + 1], rgb[i]);
            }
            f.Write(row);
        }
    }

    /// <summary>PollActions: the script's key, down for one frame.</summary>
    private static void WindowShotKeys(bool[] down)
    {
        if (_wsPress is { } a)
        {
            down[(int)a] = true;
            _wsPress = null;
        }
    }
}

internal static class WsQueueExt
{
    /// <summary>Puts an action at the head of the script (a step that needs another first).</summary>
    public static void EnqueueFront(this Queue<string> q, string item)
    {
        var rest = q.ToArray();
        q.Clear();
        q.Enqueue(item);
        foreach (var r in rest)
            q.Enqueue(r);
    }
}
