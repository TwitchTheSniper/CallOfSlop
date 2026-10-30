using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// The window smoke test (<c>--smoketest</c>): one short script that drives the real window end to end and logs
/// <c>[smoke]</c> lines to callofslop.log, then quits. It runs on a throwaway copy of the saves (SavePaths.UseSmokeSandbox),
/// so it never touches a real save. The script: the lobby, the Loadouts and Appearance screens opened and closed,
/// then three short runs (a Free-for-all match on Proto Box with a few seconds of moving, shooting and a melee swing, the
/// pause menu, the results screen and map vote; a Zombies start; a Sandbox start), each ending back in the lobby.
/// Everything deeper is covered by the headless self-test (SelfTest*.cs); the picture tools are --light-shots and friends.
/// </summary>
public static unsafe partial class Program
{
    private static bool _smoke;

    /// <summary>The runs after the lobby: a log label, the mode, the map and the bot count.</summary>
    private static readonly (string Label, GameMode Mode, string Map, string Fallback, int Bots)[] SmokeRuns =
    [
        ("match", GameMode.FreeForAll, "Proto Box", "", 7),
        ("zombies", GameMode.Zombies, "Blackmoor Asylum", "Zombies Test", 1),
        ("sandbox", GameMode.Sandbox, "Construct Yard", "", 0),
    ];

    private enum SmokeStep { Lobby, Loadouts, Appearance, Waiting, Playing, Lobby2 }

    private static SmokeStep _smokeStep;
    private static int _smokeRun = -1;
    private static double _smokeStepStart, _smokeWorstDt;
    private static int _smokeFrames, _smokeRunFrames;
    private static int _smokeScript; // the match run's sub-steps, below
    private static Buttons _smokeButtons; // held movement keys (ORed into the real input)
    private static bool _smokeFireHeld; // held trigger (ORed into the real one)

    private static void SmokeTick(double now)
    {
        _smokeFrames++;
        _smokeWorstDt = Math.Max(_smokeWorstDt, FrameDt);
        var t = now - _smokeStepStart;
        switch (_smokeStep)
        {
            case SmokeStep.Lobby when _smokeStepStart == 0:
                _smokeStepStart = now;
                LoadoutStore.ReadOnly = AppearanceStore.ReadOnly = true; // never write loadout.txt or appearance.txt
                BuildStore.ReadOnly = BlueprintStore.ReadOnly = true; // nor workbench.txt or blueprints.txt
                Console.WriteLine("[smoke] lobby");
                break;
            case SmokeStep.Lobby when t > 0.8:
                Ui.OpenLobbyLoadouts(); // main menu > Loadouts
                Next(SmokeStep.Loadouts, now);
                break;
            case SmokeStep.Loadouts when t > 0.6:
                Ui.LobbyLoadouts = false;
                Ui.OpenAppearance(); // main menu > Appearance
                Next(SmokeStep.Appearance, now);
                break;
            case SmokeStep.Appearance when t > 0.6:
                Console.WriteLine($"[smoke] menus: appearance preview {(Ui.LookPreview != null ? "on" : "OFF")}, GL error 0x{GlDebug.Error():X}");
                Ui.AppearanceOpen = false;
                BeginSmokeRun(now);
                break;
            case SmokeStep.Waiting:
                SmokeWait(now, t);
                break;
            case SmokeStep.Playing:
                SmokePlay(now, t);
                break;
            case SmokeStep.Lobby2 when t > 0.5:
                BeginSmokeRun(now);
                break;
        }

        void Next(SmokeStep step, double at)
        {
            _smokeStep = step;
            _smokeStepStart = at;
        }
    }

    /// <summary>Starts the next run as a single-player session (or finishes the test when none is left).</summary>
    private static void BeginSmokeRun(double now)
    {
        _smokeRun++;
        if (_smokeRun >= SmokeRuns.Length)
        {
            Console.WriteLine($"[smoke] done: {_smokeFrames} frames, worst frame {_smokeWorstDt * 1000:0} ms, GL error 0x{GlDebug.Error():X}");
            _quit = true;
            return;
        }
        var (label, mode, mapName, fallback, bots) = SmokeRuns[_smokeRun];
        var map = Maps.Find(mapName) ?? (fallback.Length > 0 ? Maps.Find(fallback) : null);
        if (map == null)
        {
            Console.WriteLine($"[smoke] {label}: no map '{mapName}', skipped");
            _smokeStep = SmokeStep.Lobby2;
            _smokeStepStart = now;
            return;
        }
        _settings.MapName = map.Name;
        _settings.MapRandom = false;
        _settings.Mode = mode;
        _settings.Bots = bots;
        StartSession(LobbyAction.SinglePlayer);
        if (_session != null && mode == GameMode.FreeForAll)
            Ui.SmokeRespawn(_session, now); // the loadout as it stands, then deploy (UiLoadout.cs)
        Console.WriteLine($"[smoke] {label}: started {map.Name} ({mode}): session {(_session != null ? "ok" : "FAILED")}");
        _smokeStep = SmokeStep.Waiting;
        _smokeStepStart = now;
        _smokeScript = 0;
        _smokeButtons = Buttons.None;
        _smokeFireHeld = false;
    }

    /// <summary>Waits out the ready-up, the countdown and the spawn (at most 25 s), then the run starts playing.</summary>
    private static void SmokeWait(double now, double t)
    {
        if (_session is not { } g)
        {
            EndSmokeRun(now);
            return;
        }
        if (g.Phase == MatchPhase.Warmup)
        {
            g.SetReady(true); // single player: ready starts the countdown (a no-op once ready, or before the local player exists)
            g.RequestRespawn();
        }
        else if (g.Phase == MatchPhase.Playing && Ui.MenuOpen && g.Local is { Alive: true })
        {
            Ui.AutoDeploy(g, now);
            SetMouseCaptured(true);
        }
        else if (g.Phase == MatchPhase.Playing && g.Local is not { Alive: true } && !g.RespawnRequested)
            g.RequestRespawn();
        if (g.Phase == MatchPhase.Playing && g.Local is { Alive: true } && !Ui.MenuOpen)
        {
            if (g.Local is { } me)
                me.ImmuneUntil = now + 600; // the smoke run never goes down
            _smokeStep = SmokeStep.Playing;
            _smokeStepStart = now;
            _smokeRunFrames = _smokeFrames;
        }
        else if (t > 25)
        {
            Console.WriteLine($"[smoke] {SmokeRuns[_smokeRun].Label}: FAILED to reach play (phase {g.Phase}, alive {g.Local?.Alive}, menu {Ui.MenuOpen})");
            EndSmokeRun(now);
        }
    }

    /// <summary>A few seconds of play; the match run also moves, shoots, swings, opens the pause menu and ends on the results screen.</summary>
    private static void SmokePlay(double now, double t)
    {
        if (_session is not { } g)
        {
            EndSmokeRun(now);
            return;
        }
        var label = SmokeRuns[_smokeRun].Label;
        if (_smokeRun == 0)
        {
            switch (_smokeScript)
            {
                case 0: // run forward, sprinting
                    _smokeButtons = Buttons.Forward | Buttons.Sprint;
                    _smokeScript = 1;
                    break;
                case 1 when t > 1.0: // keep moving and fire
                    _smokeFireHeld = true;
                    _smokeScript = 2;
                    break;
                case 2 when t > 2.5: // stop, swing the knife
                    _smokeButtons = Buttons.None;
                    _smokeFireHeld = false;
                    g.TryMelee(now);
                    _smokeScript = 3;
                    break;
                case 3 when t > 3.0: // the pause menu, open and shut
                    Ui.PauseOpen = true;
                    _smokeScript = 4;
                    break;
                case 4 when t > 3.5:
                    Ui.PauseOpen = false;
                    SetMouseCaptured(true);
                    _smokeScript = 5;
                    break;
                case 5 when t > 4.0:
                    Console.WriteLine($"[smoke] {label}: {World.Map.Name}: alive {g.Local?.Alive}, weapon {g.Active.Def.Name}, phase {g.Phase}, players {g.Players.Count}, "
                        + $"{(_smokeFrames - _smokeRunFrames) / Math.Max(t, 0.001):0} fps avg, worst frame {_smokeWorstDt * 1000:0} ms, GL error 0x{GlDebug.Error():X}");
                    g.DebugEndMatch(now); // the results screen and the map vote
                    _smokeScript = 6;
                    break;
                case 6 when t > 5.5:
                    g.CastVote(1);
                    _smokeScript = 7;
                    break;
                case 7 when t > 6.2:
                    Console.WriteLine($"[smoke] {label}: vote screen: options {string.Join(", ", g.VoteNames)}, my vote {g.MyVote}, mouse free {!_mouseCaptured}, GL error 0x{GlDebug.Error():X}");
                    EndSmokeRun(now);
                    break;
            }
            return;
        }
        if (t > 3.0)
        {
            Console.WriteLine($"[smoke] {label}: {World.Map.Name}: alive {g.Local?.Alive}, phase {g.Phase}, players {g.Players.Count}, "
                + $"{(_smokeFrames - _smokeRunFrames) / Math.Max(t, 0.001):0} fps avg, worst frame {_smokeWorstDt * 1000:0} ms, GL error 0x{GlDebug.Error():X}");
            EndSmokeRun(now);
        }
    }

    /// <summary>Leaves the session for the lobby; the next run starts a moment later.</summary>
    private static void EndSmokeRun(double now)
    {
        _smokeButtons = Buttons.None;
        _smokeFireHeld = false;
        EndSession("");
        _settings.Mode = GameMode.FreeForAll;
        _smokeStep = SmokeStep.Lobby2;
        _smokeStepStart = now;
        _smokeWorstDt = 0;
    }

    private static void SmokeShot(string name) => _shotName = name;

    private static string? _shotName;

    /// <summary>The next screenshot is saved at full size (not halved).</summary>
    private static bool _shotFull;

    /// <summary>A throwaway copy of the player's progress that can't be saved.</summary>
    private static Progress SmokeProgress()
    {
        var copy = Progress.Load();
        copy.ReadOnly = true;
        return copy;
    }

    /// <summary>Reads the finished frame back and saves it (half size, 2 x 2 averaged) as smoke_NAME.bmp next to the game.</summary>
    private static void SaveScreenshot(string name)
    {
        var fb = _window.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var pixels = new byte[w * h * 4];
        OpenGl.Native.glPixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            OpenGl.Native.glReadPixels(0, 0, w, h, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, p);
        var full = _shotFull; // (--perf-shot: full size, so the small text reads)
        _shotFull = false;
        var div = full ? 1 : 2;
        int ow = w / div, oh = h / div;
        var rowBytes = (ow * 3 + 3) & ~3;
        using var f = new BinaryWriter(File.Create(AppFolders.LogPath($"smoke_{name}.bmp")));
        f.Write((byte)'B');
        f.Write((byte)'M');
        f.Write(54 + rowBytes * oh);
        f.Write(0);
        f.Write(54);
        f.Write(40);
        f.Write(ow);
        f.Write(oh); // positive height = bottom-up rows, same order glReadPixels gives
        f.Write((short)1);
        f.Write((short)24);
        f.Write(0);
        f.Write(rowBytes * oh);
        f.Write(2835);
        f.Write(2835);
        f.Write(0);
        f.Write(0);
        var row = new byte[rowBytes];
        for (var y = 0; y < oh; y++)
        {
            for (var x = 0; x < ow; x++)
            {
                // The average of each 2 x 2 block (not one pixel of it), so dithered see-through things (prop glass, faded
                // litter) show as they look at full size instead of aliasing to solid or empty.
                var i = ((y * div) * w + x * div) * 4;
                if (full)
                {
                    (row[x * 3], row[x * 3 + 1], row[x * 3 + 2]) = (pixels[i + 2], pixels[i + 1], pixels[i]);
                    continue;
                }
                var j = i + w * 4;
                row[x * 3] = (byte)((pixels[i + 2] + pixels[i + 6] + pixels[j + 2] + pixels[j + 6] + 2) >> 2);
                row[x * 3 + 1] = (byte)((pixels[i + 1] + pixels[i + 5] + pixels[j + 1] + pixels[j + 5] + 2) >> 2);
                row[x * 3 + 2] = (byte)((pixels[i] + pixels[i + 4] + pixels[j] + pixels[j + 4] + 2) >> 2);
            }
            f.Write(row);
        }
        Console.WriteLine($"[smoke] screenshot smoke_{name}.bmp ({ow}x{oh})");
    }
}
