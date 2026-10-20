using System.Numerics;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// Call of Slop: a small multiplayer arena shooter built directly on the BRUTAL framework
/// (Brutal.Glfw for the window/input, Brutal.OpenGl for rendering, Brutal.RakNet for networking,
/// Brutal.ImGui for the menus and HUD text, Brutal.OpenAl for 3D sound).
///
/// The game opens straight into the lobby: Single player (you vs bots), Multiplayer (host/join)
/// and Settings. Command-line shortcuts for testing:
///   CallOfSlop host [port]        start hosting immediately
///   CallOfSlop join &lt;ip&gt; [port]   join immediately
///   --lag=ms --loss=%              simulate a bad connection (testing)
///   --no-upnp                      don't touch the router when hosting
/// Modding helpers: --check-map FILE, --map-ops, --export-maps DIR, --check-weapon FILE, --export-weapons DIR.
/// </summary>
public static unsafe class Program
{
    public const ushort DefaultPort = 60000;
    public static double FrameDt;

    private const float BaseSensitivity = 0.0022f;

    // Keep the GL loader delegate rooted for the lifetime of the process.
    private static readonly GlGetProcAddressFunc GlLoader = GetGlProc;

    private static GlfwWindow _window = null!;
    private static readonly Renderer Gfx = new();
    private static readonly Ui Ui = new();
    private static readonly Audio AudioSystem = new();
    private static Settings _settings = new();
    private static Progress _progress = new();

    // ---- The current match (null while in the lobby) ----------------------------
    private static Game? _session;
    private static Game _game => _session!; // rendering/input code only runs during a match
    private static UpnpMapping? _upnp;
    private static Task? _upnpTask;
    private static string _joinAddress = "";
    private static string _lobbyMessage = "";

    private static bool _mouseCaptured;
    private static bool _skipNextMouseDelta = true;
    private static double2 _lastCursor;
    private static bool _wasDeadUi, _wasVoting;
    private static readonly bool[] _voteKeyDown = new bool[3];
    private static double _deathMenuAt;
    private const string LoadoutLockedText = "You can only change your loadout when you're dead";
    private static bool _quit;
    private static bool _vsync;

    // Command-line test options
    private static int _lagMs, _lossPct;
    private static bool _forceNoUpnp;

    private static float BaseFov => _settings.Fov * MathF.PI / 180f;

    public static int Main(string[] args)
    {
        SetUpLogFile();
        Console.WriteLine($"Call of Slop - built with the BRUTAL framework, Copyright (c) 2024 Ahwoo Limited. {DateTime.Now}");

        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run(); // headless checks, see SelfTest.cs
        if (args.SkipWhile(a => !a.Equals("--export-maps", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } exportDir)
            return Maps.Export(exportDir); // writes the loaded maps back out as map files
        if (args.SkipWhile(a => !a.Equals("--check-map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } checkFile)
            return Maps.Check(checkFile); // for map makers: does this file load?
        if (args.Any(a => a.Equals("--map-ops", StringComparison.OrdinalIgnoreCase)))
            return Maps.PrintOps(); // for map makers: every op and its parameters
        if (args.SkipWhile(a => !a.Equals("--check-weapon", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } weaponFile)
            return WeaponSet.Check(weaponFile); // for modders: does this weapon (or attachment) file load?
        if (args.SkipWhile(a => !a.Equals("--export-weapons", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } weaponDir)
            return WeaponSet.Export(weaponDir); // writes the loaded weapons and attachments back out as files
        if (args.Any(a => a.Equals("--master", StringComparison.OrdinalIgnoreCase)))
            return MasterServerApp.Run(args); // public server list, see ServerBrowser.cs

        _settings = Settings.Load();
        _progress = Progress.Load();
        LoadoutStore.Load();
        _lagMs = FlagValue(args, "--lag");
        _lossPct = FlagValue(args, "--loss");
        _forceNoUpnp = args.Any(a => a.Equals("--no-upnp", StringComparison.OrdinalIgnoreCase));
        _smoke = args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase));
        var plain = args.Where(a => !a.StartsWith("--")).ToArray();

        // Remove the router mapping even if the process is closed abruptly.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _upnp?.Dispose();

        try
        {
            AudioSystem.Init();
            AudioSystem.MasterVolume = _settings.Volume;
            CreateWindow();

            // Command-line shortcuts skip the lobby.
            if (plain.Length > 0 && plain[0].Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                if (plain.Length > 1 && ushort.TryParse(plain[1], out var hp))
                    _settings.Port = hp;
                StartSession(LobbyAction.Host);
            }
            else if (plain.Length > 0 && plain[0].Equals("join", StringComparison.OrdinalIgnoreCase))
            {
                if (plain.Length > 1)
                    _settings.JoinAddress = plain[1];
                if (plain.Length > 2 && ushort.TryParse(plain[2], out var jp))
                    _settings.Port = jp;
                StartSession(LobbyAction.Join);
            }

            RunLoop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: {ex}");
            return 1;
        }
        finally
        {
            EndSession("");
            Ui.Dispose();
            AudioSystem.Dispose();
            Glfw.Terminate();
            if (!_smoke) // the smoke test borrows settings temporarily; never save them
            {
                _settings.Save();
                _progress.Save();
            }
        }
        return 0;
    }

    // =====================================================================
    // Smoke test (--smoketest): drives the real window through the lobby, the loadout and
    // gunsmith screens, the death/respawn flow and a few seconds of play on several maps,
    // logging frame times and GL errors to callofslop.log, then quits. Used to check
    // rendering and UI changes without anyone at the keyboard.
    // =====================================================================

    private static bool _smoke;
    private static int _smokeMap = -1;
    private static double _smokeStepStart;
    private static int _smokeStage;
    private static int _smokeFrames;
    private static double _smokeWorstDt;
    private static readonly string[] SmokeMaps = ["Proto Box", "Courtyard", "Harbor", "Downtown", "Highlands", "Military Base"];

    private static void SmokeTick(double now)
    {
        _smokeFrames++;
        _smokeWorstDt = Math.Max(_smokeWorstDt, FrameDt);
        var t = now - _smokeStepStart;
        if (_smokeMap < 0)
        {
            // Lobby: two gun galleries (stock, then fully kitted), screenshotted, then on to the maps.
            if (_smokeStepStart == 0)
            {
                _smokeStepStart = now;
                Console.WriteLine("[smoke] lobby");
                LoadoutStore.SmokeKit = id => SmokeFullKit(Weapons.Get(id), _smokeMap + 1);
                LoadoutStore.ReadOnly = true; // the smoke test must never write your loadout.txt
                _smokeGallery = 1;
                return;
            }
            // (each screenshot is taken at the end of the frame it was requested in, so switch galleries a tick later)
            if (_smokeGallery == 1 && t > 0.6 && t < 0.9 && _shotName == null && !_shotTaken)
            {
                SmokeShot("gallery_stock");
                _shotTaken = true;
            }
            else if (_smokeGallery == 1 && t > 0.9)
            {
                _smokeGallery = 2;
                _shotTaken = false;
            }
            else if (_smokeGallery == 2 && t > 1.4 && !_shotTaken)
            {
                SmokeShot("gallery_kitted");
                _shotTaken = true;
            }
            else if (_smokeGallery == 2 && t > 1.8)
            {
                _smokeGallery = 3; // main menu > Loadouts
                Ui.OpenLobbyLoadouts();
                _shotTaken = false;
            }
            else if (_smokeGallery == 3 && t > 2.4 && !_shotTaken)
            {
                SmokeShot("lobby_loadouts");
                _shotTaken = true;
            }
            else if (_smokeGallery == 3 && t > 2.8)
            {
                _smokeGallery = 0;
                Ui.LobbyLoadouts = false;
                SmokeNextMap(now);
            }
            return;
        }

        if (_smokeMap == 0 && _smokeWeaponSelect < 5)
        {
            SmokeWeaponSelect(now, t);
            return;
        }

        var game = _session;
        switch (_smokeStage)
        {
            case 0 when t > 0.8:
                if (_smokeMap == 0)
                    SmokeShot("loadout_overview");
                _smokeStage++;
                break;
            case 1 when t > 1.0:
                Ui.SmokeTab(4); // primary attachments showcase
                _smokeStage++;
                break;
            case 2 when t > 1.7:
                if (_smokeMap < 3)
                    SmokeShot($"gunsmith_primary_{_smokeMap}");
                _smokeStage++;
                break;
            case 3 when t > 2.0:
                Ui.SmokeTab(5); // secondary attachments showcase
                _smokeStage++;
                break;
            case 4 when t > 2.6:
                if (_smokeMap == 0)
                    SmokeShot("gunsmith_secondary_0");
                Ui.SmokeTab(6); // perks
                _smokeStage++;
                break;
            case 5 when t > 3.1:
                if (_smokeMap == 0)
                    SmokeShot("perks_0");
                _smokeStage++;
                break;
            case 6 when t > 3.4:
                Ui.SmokeTab(0);
                if (game != null)
                {
                    Ui.SmokeRespawn(game, now);
                    Console.WriteLine($"[smoke] respawn requested (phase {game.Phase}, alive {game.Local?.Alive}, perks {game.ChosenPerks})");
                }
                _smokeStage++;
                break;
            case 7 when t > 6.2:
                game?.TryMelee(now);
                if (_smokeMap < 4 && game?.Local is { Alive: true })
                    SmokeShot($"play_{_smokeMap}");
                _smokeStage++;
                break;
            case 8 when t > 8.5:
                Console.WriteLine($"[smoke] {World.Map.Name}: alive {game?.Local?.Alive}, weapon {game?.Active.Def.Name} [{Attachments.Describe(game?.Active.Def.Kit ?? 0)}], phase {game?.Phase}, players {game?.Players.Count}, "
                    + $"{_smokeFrames / Math.Max(t, 0.001):0} fps avg, worst frame {_smokeWorstDt * 1000:0} ms, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                game?.DebugEndMatch(now); // show the results screen and map vote
                _smokeStage++;
                break;
            case 9 when t > 10.5:
                game?.CastVote(1);
                _smokeStage++;
                break;
            case 10 when t > 11.2:
                if (_smokeMap == 0)
                    SmokeShot("vote_0");
                if (game != null)
                    Console.WriteLine($"[smoke] vote screen: options {string.Join(", ", game.VoteNames)}, "
                        + $"my vote {game.MyVote}, tally {string.Join("/", game.VoteTally)}, mouse free {!_mouseCaptured}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                SmokeNextMap(now);
                break;
        }
    }

    private static int _smokeWeaponSelect;
    private static string? _smokeLockedGun;

    /// <summary>
    /// Smoke step on the first map: the weapon-select screen ("Choose primary"), screenshotted as is and
    /// with a locked gun shown as if hovered. Then the usual steps start (their clock restarts).
    /// </summary>
    private static void SmokeWeaponSelect(double now, double t)
    {
        switch (_smokeWeaponSelect)
        {
            case 0 when t > 0.8:
                Ui.SmokeChoose(false);
                _smokeWeaponSelect++;
                break;
            case 1 when t > 1.6:
                SmokeShot("weapon_select");
                Console.WriteLine($"[smoke] weapon select: {Ui.Thumbs.Count} thumbnail(s) drawn, hero preview {(Ui.Preview != null ? "on" : "OFF")}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                _smokeWeaponSelect++;
                break;
            case 2 when t > 1.8: // a frame after the shot
                _smokeLockedGun = Ui.SmokeChoose(true);
                _smokeWeaponSelect++;
                break;
            case 3 when t > 2.5:
                SmokeShot("weapon_select_locked");
                Console.WriteLine($"[smoke] weapon select, locked gun shown: {_smokeLockedGun ?? "none (everything is unlocked at this level)"}, {Ui.Thumbs.Count} thumbnail(s)");
                _smokeWeaponSelect++;
                break;
            case 4 when t > 2.7:
                Ui.SmokeTab(0); // back to the overview, then the usual steps
                _smokeWeaponSelect++;
                _smokeStepStart = now;
                _smokeFrames = 0;
                _smokeWorstDt = 0;
                break;
        }
    }

    private static int _smokeGallery;
    private static bool _shotTaken;
    private static string? _shotName;

    // Guns to show off per smoke map (one per class), with a different attachment mix each time.
    private static readonly (string Primary, string Secondary)[] SmokeGuns =
    [
        ("Vanguard AR", "P9 Sidearm"), ("Needle Bolt-Action", "Rattler Revolver"), ("Breacher Pump", "Viper .45"),
        ("Anvil LMG", "Mk.23 Tactical"), ("Hornet SMG", "Stinger Burst Pistol"), ("Marksman M14", "P9 Sidearm"),
    ];

    /// <summary>A full kit for screenshots: one attachment in every slot the gun has, varied by <paramref name="variant"/>.</summary>
    private static uint SmokeFullKit(WeaponDef w, int variant)
    {
        uint kit = 0;
        for (var s = 0; s < Attachments.SlotCount; s++)
        {
            var opts = Attachments.For(w).Where(a => (int)a.Slot == s).ToArray();
            if (opts.Length > 0)
                kit = Attachments.With(kit, (AttachSlot)s, opts[(variant + s) % opts.Length].Index);
        }
        return kit;
    }

    private static void SmokeShot(string name) => _shotName = name;

    /// <summary>A throwaway copy of the player's progress that can't be saved.</summary>
    private static Progress SmokeProgress()
    {
        var copy = Progress.Load();
        copy.ReadOnly = true;
        return copy;
    }

    /// <summary>Reads the finished frame back and saves it (half size) as smoke_NAME.bmp next to the game.</summary>
    private static void SaveScreenshot(string name)
    {
        var fb = _window.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var pixels = new byte[w * h * 4];
        OpenGl.Native.glPixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            OpenGl.Native.glReadPixels(0, 0, w, h, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, p);
        int ow = w / 2, oh = h / 2;
        var rowBytes = (ow * 3 + 3) & ~3;
        using var f = new BinaryWriter(File.Create(Path.Combine(AppContext.BaseDirectory, $"smoke_{name}.bmp")));
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
                var i = ((y * 2) * w + x * 2) * 4;
                row[x * 3] = pixels[i + 2];
                row[x * 3 + 1] = pixels[i + 1];
                row[x * 3 + 2] = pixels[i];
            }
            f.Write(row);
        }
        Console.WriteLine($"[smoke] screenshot smoke_{name}.bmp ({ow}x{oh})");
    }

    /// <summary>Every weapon in a grid, side-on (smoke test): 1 = stock, 2 = with a full kit.</summary>
    private static void DrawGunGallery(int mode)
    {
        var fb = _window.FramebufferSize;
        Gfx.BeginFrame(fb.X, fb.Y, new Vector3(0.06f, 0.07f, 0.09f));
        const int cols = 10;
        var all = Weapons.All;
        var rows = (all.Length + cols - 1) / cols;
        float cw = fb.X / (float)cols, ch = fb.Y / (float)rows;
        for (var i = 0; i < all.Length; i++)
        {
            var w = mode == 2 ? Attachments.Apply(all[i], SmokeFullKit(all[i], i)) : all[i];
            int cx = i % cols, cy = i / cols;
            Gfx.SetViewport((int)(cx * cw), (int)(fb.Y - (cy + 1) * ch), (int)cw, (int)ch);
            Gfx.ClearDepthBuffer();
            var span = w.Length * 1.25f + 0.5f;
            var aspect = cw / ch;
            var dist = MathF.Max(span * 0.38f / aspect, span * 0.26f) / MathF.Tan(0.25f);
            Gfx.SetCamera(Matrix4x4.CreateLookAt(new Vector3(-dist, 0.05f, 0), Vector3.Zero, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(0.5f, aspect, 0.05f, 50f));
            var pose = Matrix4x4.CreateTranslation(0, 0, w.Length * 0.55f - 0.05f) * Matrix4x4.CreateRotationY(0.25f);
            DrawGun(w, pose, false);
        }
        Gfx.SetViewport(0, 0, fb.X, fb.Y);
    }

    private static void SmokeNextMap(double now)
    {
        _smokeMap++;
        if (_smokeMap >= SmokeMaps.Length)
        {
            Console.WriteLine("[smoke] done");
            _quit = true;
            return;
        }
        _settings.MapName = SmokeMaps[_smokeMap];
        _settings.MapRandom = false;
        _settings.Bots = 7;
        StartSession(LobbyAction.SinglePlayer);
        if (_session != null)
        {
            var (pn, sn) = SmokeGuns[_smokeMap % SmokeGuns.Length];
            Ui.SmokeSelect((Weapons.Find(pn) ?? Weapons.DefaultPrimary).Id, (Weapons.Find(sn) ?? Weapons.DefaultSecondary).Id);
        }
        Console.WriteLine($"[smoke] started {_settings.MapName}: session {(_session != null ? "ok" : "FAILED")}, menu open {Ui.MenuOpen}");
        _smokeStepStart = now;
        _smokeStage = 0;
        _smokeFrames = 0;
        _smokeWorstDt = 0;
    }

    /// <summary>The game runs without a console window, so console output goes to callofslop.log.</summary>
    private static void SetUpLogFile()
    {
        try
        {
            var writer = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "callofslop.log"), append: false) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }
        catch
        {
            // No log; not fatal.
        }
    }

    private static int FlagValue(string[] args, string name)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        return arg != null && int.TryParse(arg[(name.Length + 1)..], out var v) ? Math.Max(0, v) : 0;
    }

    private static nint GetGlProc(byte* name) => Glfw.PInvoke.GetProcAddress(name);

    private static void CreateWindow()
    {
        Glfw.ErrorCallback += (code, desc) => Console.WriteLine($"GLFW {code}: {desc}");
        if (!Glfw.Init())
            throw new Exception("Failed to initialize GLFW");

        // OpenGL 4.1 core: what the ImGui OpenGL backend needs (and the highest macOS offers).
        Glfw.WindowHint(GlfwWindowHint.ClientApi, Glfw.Constants.GLFW_OPENGL_API);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
        Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, Glfw.Constants.GLFW_TRUE);
        Glfw.WindowHint(GlfwWindowHint.Samples, 4);

        _window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Call of Slop", Size = new int2(_settings.Width, _settings.Height) });
        _window.MakeContextCurrent();
        _vsync = _settings.VSync;
        Glfw.SwapInterval(_vsync ? 1 : 0);

        if (!Glad.Initialize(GlLoader))
            throw new Exception("Failed to load OpenGL functions (GLAD)");

        Gfx.Init();
        Ui.Init(_window);
        Ui.UserScale = _settings.UiScale;
        Ui.Progress = _progress;
        ApplyDisplay(); // windowed / borderless / fullscreen from settings.txt

        // Remember the windowed size and position the player drags to.
        _window.OnSize += (_, size) =>
        {
            if (_settings.Display == DisplayMode.Windowed && size.X > 0 && size.Y > 0)
            {
                _settings.Width = size.X;
                _settings.Height = size.Y;
            }
        };
        _window.OnPosition += (_, pos) =>
        {
            if (_settings.Display == DisplayMode.Windowed)
                _windowedPos = pos;
        };

        _window.OnKey += OnKey;
        _window.OnMouseButton += OnMouseButton;
        _window.OnScroll += OnScroll;
        _window.OnFocus += (_, focus) =>
        {
            if (focus != GlfwFocusEvent.GainFocus)
                SetMouseCaptured(false);
        };
    }

    // =====================================================================
    // Sessions (lobby <-> match)
    // =====================================================================

    private static void StartSession(LobbyAction kind)
    {
        EndSession("");
        _lobbyMessage = "";
        if (_smoke)
            LoadoutStore.ReadOnly = true;
        var loadout = LoadoutStore.Active(_smoke ? SmokeProgress() : _progress);
        try
        {
            NetPeer net;
            var bots = 0;
            switch (kind)
            {
                case LobbyAction.SinglePlayer:
                    net = NetPeer.Offline();
                    bots = _settings.Bots;
                    break;
                case LobbyAction.Host:
                    net = NetPeer.Host(_settings.Port, Game.MaxPlayers - 1);
                    bots = _settings.HostBots;
                    var lan = UpnpMapping.GetLocalIPv4()?.ToString() ?? "this PC's IP";
                    var suffix = _settings.Port == DefaultPort ? "" : $" port {_settings.Port}";
                    _joinAddress = lan + suffix + " (LAN)";
                    if (_settings.UseUpnp && !_forceNoUpnp)
                    {
                        // Router discovery takes a few seconds; don't freeze the game while it happens.
                        var port = _settings.Port;
                        _joinAddress += " - opening router port...";
                        _upnpTask = Task.Run(() =>
                        {
                            var m = UpnpMapping.TryCreate(port, Console.WriteLine);
                            _upnp = m;
                            _joinAddress = m is { ExternalIp: { } ext, BehindSecondNat: false }
                                ? $"{ext}{suffix} (internet) or {lan}{suffix} (LAN)"
                                : m is { BehindSecondNat: true }
                                    ? $"{lan}{suffix} (LAN only - your connection is behind a second NAT)"
                                    : $"{lan}{suffix} (LAN only - UPnP unavailable, forward UDP {port} for internet play)";
                        });
                    }
                    break;
                default:
                    net = NetPeer.Join(_settings.JoinAddress, _settings.Port);
                    break;
            }

            if (_lagMs > 0 || _lossPct > 0)
                Console.WriteLine(net.SimulateBadNetwork(_lagMs, _lagMs / 5, _lossPct)
                    ? $"[test] simulating {_lagMs} ms latency and {_lossPct}% loss"
                    : "[test] network simulator not available in this RakNet build");

            var map = kind == LobbyAction.Join ? Maps.All[0] : _settings.PickMap(); // joiners get the host's map
            _session = new Game(net, AudioSystem, _settings.Name, loadout.Primary, loadout.Secondary, bots, _settings.Difficulty, map.Id,
                loadout.Grenade, loadout.Equipment, loadout.PrimaryKit, loadout.SecondaryKit, loadout.Perks)
                { Progress = _smoke ? SmokeProgress() : _progress }; // the smoke test must never touch your real save
            if (kind == LobbyAction.Host)
            {
                _session.ServerName = _settings.ServerNameOrDefault;
                _session.GamePort = _settings.Port;
                _session.AdvertiseTo = _settings.Advertise ? _settings.Master : null;
                if (_settings.Advertise && _settings.Master is { } m)
                    Console.WriteLine($"[host] advertising '{_session.ServerName}' to master server {m.Host}:{m.Port}");
            }
            Ui.OpenMenu(_session); // pick a loadout before the first spawn
            Ui.PauseOpen = false;
            SetMouseCaptured(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not start: {ex}");
            _lobbyMessage = kind == LobbyAction.Host && ex.Message.Contains("SocketPortAlreadyInUse")
                ? $"Port {_settings.Port} is already in use (is another copy hosting?). Pick another port."
                : ex.Message;
            EndSession(_lobbyMessage);
        }
    }

    private static void EndSession(string message)
    {
        if (message.Length > 0)
            _lobbyMessage = message;
        if (_session != null)
        {
            _session.Dispose();
            _session = null;
        }
        if (_upnpTask is { IsCompleted: false })
            _upnpTask.Wait(TimeSpan.FromSeconds(4));
        _upnpTask = null;
        _upnp?.Dispose();
        _upnp = null;
        Ui.MenuOpen = false;
        Ui.PauseOpen = false;
        Ui.ResetLobby();
        if (_window != null)
            SetMouseCaptured(false);
    }

    // =====================================================================
    // Main loop
    // =====================================================================

    private static void RunLoop()
    {
        var last = Glfw.Time;
        while (!_window.ShouldClose && !_quit)
        {
            Glfw.PollEvents();
            var now = Glfw.Time;
            if (_smoke)
                SmokeTick(now);
            FrameDt = Math.Min(now - last, 0.1);
            last = now;

            if (_settings.DisplayDirty)
                ApplyDisplay();
            Ui.UserScale = _settings.UiScale;

            if (_settings.VSync != _vsync)
            {
                _vsync = _settings.VSync;
                Glfw.SwapInterval(_vsync ? 1 : 0);
            }

            if (_session is { } game)
            {
                if (game.Disconnected)
                {
                    EndSession(game.Status);
                }
                else
                {
                    UpdateLocalPlayer(now);
                    var paused = game.Net.IsOffline && Ui.PauseOpen;
                    if (!paused)
                        game.Update(now, FrameDt);
                }
            }

            if (_session != null)
                Render(now);
            else if (_smokeGallery is 1 or 2)
                DrawGunGallery(_smokeGallery);
            else
                RenderLobbyBackground(now);
            if (Ui.Preview is { } showcase)
                DrawGunsmithPreview(showcase, now);

            Ui.BeginFrame();
            if (_session is { } g)
            {
                Ui.DrawHud(g, now, _joinAddress, _settings);
                if (Ui.DrawLoadoutMenu(g, now))
                {
                    SetMouseCaptured(true);
                    _swallowClick = true; // the Deploy click mustn't also fire the gun
                }
                switch (Ui.DrawPauseMenu(g, _settings))
                {
                    case PauseAction.Resume:
                        SetMouseCaptured(true);
                        _swallowClick = true;
                        break;
                    case PauseAction.Loadout:
                        Ui.OpenMenu(g);
                        break;
                    case PauseAction.Leave:
                        EndSession("");
                        break;
                    case PauseAction.Quit:
                        _quit = true;
                        break;
                }
            }
            else if (_smokeGallery is not (1 or 2)) // no lobby menu over the smoke-test gun gallery
            {
                if (Ui.LobbyLoadouts)
                {
                    Ui.DrawLobbyLoadouts(AudioSystem, now);
                }
                else
                {
                    var action = Ui.DrawLobby(_settings, _lobbyMessage, AudioSystem);
                    if (action == LobbyAction.Quit)
                        _quit = true;
                    else if (action != LobbyAction.None)
                        StartSession(action);
                }
            }
            Ui.EndFrame();
            if (Ui.Thumbs.Count > 0)
                DrawGunThumbs(now); // gun pictures on the loadout cards, over the UI

            if (_shotName != null)
            {
                SaveScreenshot(_shotName);
                _shotName = null;
            }
            _window.SwapBuffers();
        }
    }

    /// <summary>The lobby shows the arena from a slow orbiting camera.</summary>
    private static void RenderLobbyBackground(double now)
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        // Preview the map currently picked in the lobby.
        // (with "random from pack" picked, the background tours the pack's maps, a different one every 12 seconds)
        var tour = Maps.FindPack(_settings.MapPack) is { Maps.Count: > 0 } pack ? pack.Maps : (IReadOnlyList<Map>)Maps.All;
        var preview = _settings.MapRandom ? tour[(int)(now / 12) % tour.Count] : Maps.Find(_settings.MapName);
        if (preview != null && preview != World.Map)
            World.SetMap(preview);
        var map = World.Map;
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        var a = (float)now * 0.08f;
        var r = map.ViewRadius;
        var eye = new Vector3(MathF.Sin(a) * r * map.HalfX / MathF.Max(map.HalfX, map.HalfZ), r * 0.4f + MathF.Sin((float)now * 0.2f) * 2f,
            MathF.Cos(a) * r * map.HalfZ / MathF.Max(map.HalfX, map.HalfZ));
        var fwd = Vector3.Normalize(new Vector3(0, 1.5f, 0) - eye);
        AudioSystem.SetListener(eye, fwd);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart * 1.5f, map.FogEnd * 1.5f);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);

        // Darken so the menu reads clearly.
        Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, fb.X, 0, fb.Y, -1, 1));
        Gfx.SetDepthTest(false);
        Gfx.SetBlend(true);
        Gfx.Rect(0, 0, fb.X, fb.Y, Vector3.Zero, 0.35f);
        Gfx.SetBlend(false);
        Gfx.SetDepthTest(true);
    }

    private static void DrawArena()
    {
        var map = World.Map;
        for (var i = 0; i < map.Solid.Count; i++)
            Gfx.DrawBox(map.Solid[i], map.Colors[i], mode: 1, material: map.Materials[i]);
    }

    // =====================================================================
    // Input
    // =====================================================================

    private static bool MenusOpen => Ui.MenuOpen || Ui.PauseOpen;

    private static void SetMouseCaptured(bool captured)
    {
        if (captured && (MenusOpen || _session == null))
            captured = false;
        _mouseCaptured = captured;
        _window.SetInputMode(GlfwInputMode.Cursor, (int)(captured ? GlfwCursorMode.Disabled : GlfwCursorMode.Normal));
        if (captured && Glfw.RawMotionSupported())
            _window.SetInputMode(GlfwInputMode.RawMouseMotion, Glfw.Constants.GLFW_TRUE);
        _skipNextMouseDelta = true;
    }

    private static void OnKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods)
    {
        if (action != GlfwKeyAction.Press)
            return;

        // Settings > Controls is waiting for a key: this press is the new binding.
        if (Ui.Rebinding is { } rebind)
        {
            if (key != GlfwKey.Escape && key != GlfwKey.Unknown)
                Keybinds.Assign(_settings.Binds, rebind, Keybinds.Key(key));
            Ui.Rebinding = null;
            return;
        }

        if (_session == null || key != GlfwKey.Escape)
            return; // everything else is polled through the keybinds in UpdateLocalPlayer

        if (_session.Local == null)
        {
            EndSession("Cancelled");
            return;
        }
        if (Ui.MenuOpen)
        {
            Ui.MenuOpen = false;
            SetMouseCaptured(true);
        }
        else
        {
            Ui.PauseOpen = !Ui.PauseOpen;
            SetMouseCaptured(!Ui.PauseOpen);
        }
    }

    private static void OnScroll(GlfwWindow window, double2 offset)
    {
        if (_session != null && _mouseCaptured && Math.Abs(offset.Y) > 0.1)
            _game.SwitchTo(1 - _game.ActiveSlot, Glfw.Time);
    }

    private static void OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods)
    {
        if (action != GlfwButtonAction.Press)
            return;

        // A mouse button can be a binding too (ignore the click that opened the capture).
        if (Ui.Rebinding is { } rebind)
        {
            if (Glfw.Time - Ui.RebindStarted > 0.25)
            {
                Keybinds.Assign(_settings.Binds, rebind, Keybinds.Mouse(button));
                Ui.Rebinding = null;
            }
            return;
        }

        if (_session != null && !_mouseCaptured && !MenusOpen && !Ui.WantsMouse)
        {
            SetMouseCaptured(true);
            _swallowClick = true; // the click that recaptures the mouse shouldn't fire
        }
    }

    private static bool _swallowClick;
    private static bool _crouchToggle, _proneToggle, _holdJumpUntilRelease;
    private static double _stanceKeyAt = -1;
    private static bool _stanceHoldDone, _stanceCrouchedOnPress;
    private const double StanceHoldSeconds = 0.3;

    // ---- Keybind polling: one down/previous state per action ------------------------
    private static readonly bool[] _down = new bool[(int)GameAction.COUNT];
    private static readonly bool[] _prev = new bool[(int)GameAction.COUNT];

    private static bool Down(GameAction a) => _down[(int)a];

    private static bool Pressed(GameAction a) => _down[(int)a] && !_prev[(int)a];

    private static void PollActions()
    {
        Array.Copy(_down, _prev, _down.Length);
        var blocked = Ui.WantsTextInput || Ui.Rebinding != null;
        for (var i = 0; i < _down.Length; i++)
            _down[i] = !blocked && Keybinds.IsDown(_window, _settings.Binds[i]);
    }

    private static void UpdateLocalPlayer(double now)
    {
        PollActions();

        var cursor = _window.CursorPos;
        var delta = new double2(cursor.X - _lastCursor.X, cursor.Y - _lastCursor.Y);
        _lastCursor = cursor;
        if (_skipNextMouseDelta)
        {
            delta = new double2(0, 0);
            _skipNextMouseDelta = false;
        }

        // Match countdown: open the loadout menu while everyone loads in, deploy it at GO.
        if (_game.TakeCountdownStarted() && !Ui.MenuOpen && !Ui.PauseOpen)
        {
            Ui.OpenMenu(_game);
            SetMouseCaptured(false);
        }
        if (_game.TakePlayStarted() && Ui.MenuOpen && _game.Local is { Alive: true })
        {
            Ui.AutoDeploy(_game, now); // standing in the map already: apply what's picked and go
            if (!Ui.PauseOpen)
                SetMouseCaptured(true);
        }

        // Results screen: free the mouse so players can click a map in the vote; 1/2/3 also vote.
        var votingNow = _game.VoteOpen;
        if (votingNow && !_wasVoting)
        {
            Ui.MenuOpen = false;
            SetMouseCaptured(false);
        }
        _wasVoting = votingNow;
        if (votingNow && !Ui.PauseOpen)
        {
            GlfwKey[] voteKeys = [GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3];
            for (var i = 0; i < voteKeys.Length; i++)
            {
                var keyDown = _window.GetKey(voteKeys[i]) == GlfwKeyAction.Press;
                if (keyDown && !_voteKeyDown[i])
                    _game.CastVote(i);
                _voteKeyDown[i] = keyDown;
            }
        }

        // Death screen: shortly after you die, the loadout menu opens with a Respawn button.
        // Nobody is respawned automatically (and not over the results screen).
        var deadNow = _game.Local is not { Alive: true };
        if (deadNow && !_wasDeadUi)
            _deathMenuAt = now + 1.2;
        if (!deadNow)
            _deathMenuAt = 0;
        _wasDeadUi = deadNow;
        if (_deathMenuAt > 0 && now >= _deathMenuAt)
        {
            _deathMenuAt = 0;
            if (!Ui.MenuOpen && !Ui.PauseOpen && !_game.RespawnRequested && _game.Phase != MatchPhase.Ended)
            {
                Ui.OpenMenu(_game);
                SetMouseCaptured(false);
            }
        }

        // Loadout menu toggle works with the mouse free too (but not over the pause menu).
        if (Pressed(GameAction.Loadout) && !Ui.PauseOpen)
        {
            if (Ui.MenuOpen)
            {
                Ui.MenuOpen = false;
                if (!deadNow)
                    SetMouseCaptured(true);
            }
            else if (_game.CanChangeLoadout)
            {
                Ui.OpenMenu(_game);
                SetMouseCaptured(false);
            }
            else
            {
                // Loadouts only change between lives.
                _game.LoadoutNotice = _game.LoadoutNotice == LoadoutLockedText ? LoadoutLockedText + " " : LoadoutLockedText;
            }
        }
        Ui.ShowScoreboard = _mouseCaptured && Down(GameAction.Scoreboard);

        // Mouse look is applied every frame (never waits for a tick or the network).
        if (_mouseCaptured)
        {
            // Lower sensitivity while zoomed so aiming feels the same at any FOV.
            var zoom = float.Lerp(1f, _game.Active.Def.AdsZoom, _game.AimT);
            var sens = BaseSensitivity * _settings.Sensitivity / zoom;
            var dy = (float)delta.Y * (_settings.InvertY ? -1 : 1);
            _game.LookYaw = World.WrapAngle(_game.LookYaw + (float)delta.X * sens);
            _game.LookPitch = Math.Clamp(_game.LookPitch - dy * sens, -1.55f, 1.55f);
        }

        if (_game.Local is { Alive: false })
            _crouchToggle = _proneToggle = false; // respawn standing

        var buttons = Buttons.None;
        if (_mouseCaptured)
        {
            // One-shot actions
            if (Pressed(GameAction.CrouchToggle))
            {
                _crouchToggle = !_crouchToggle;
                _proneToggle = false;
            }
            if (Pressed(GameAction.Prone))
            {
                _proneToggle = !_proneToggle;
                _crouchToggle = false;
            }

            // Crouch/prone key (Ctrl by default): tap = crouch / stand, hold = go prone (or get up from prone).
            // Standing players crouch the moment the key goes down, so a tap feels instant.
            if (Pressed(GameAction.CrouchHold))
            {
                _stanceKeyAt = now;
                _stanceHoldDone = false;
                _stanceCrouchedOnPress = !_crouchToggle && !_proneToggle;
                if (_stanceCrouchedOnPress)
                    _crouchToggle = true;
            }
            if (_stanceKeyAt >= 0 && Down(GameAction.CrouchHold) && !_stanceHoldDone && now - _stanceKeyAt >= StanceHoldSeconds)
            {
                _stanceHoldDone = true;
                var wasProne = _proneToggle && !_stanceCrouchedOnPress;
                _proneToggle = !wasProne;
                _crouchToggle = false;
            }
            if (_stanceKeyAt >= 0 && !Down(GameAction.CrouchHold))
            {
                if (!_stanceHoldDone && !_stanceCrouchedOnPress)
                {
                    if (_proneToggle)
                    {
                        _proneToggle = false; // tap while prone: up to a crouch
                        _crouchToggle = true;
                    }
                    else
                    {
                        _crouchToggle = !_crouchToggle; // tap while crouched: stand
                    }
                }
                _stanceKeyAt = -1;
            }
            if (Pressed(GameAction.Jump) && (_crouchToggle || _proneToggle))
            {
                _crouchToggle = _proneToggle = false; // Jump stands you up first
                _holdJumpUntilRelease = true;
            }
            if (Pressed(GameAction.Reload))
                _game.StartReload(now);
            if (Pressed(GameAction.PrimaryWeapon))
                _game.SwitchTo(0, now);
            if (Pressed(GameAction.SecondaryWeapon))
                _game.SwitchTo(1, now);
            if (Pressed(GameAction.SwapWeapon))
                _game.SwitchTo(1 - _game.ActiveSlot, now);
            if (Pressed(GameAction.Grenade))
                _game.TryThrow(equipment: false, now);
            if (Pressed(GameAction.Equipment))
                _game.TryThrow(equipment: true, now);
            if (Pressed(GameAction.Melee))
                _game.TryMelee(now);

            // Held actions
            if (Down(GameAction.MoveForward)) buttons |= Buttons.Forward;
            if (Down(GameAction.MoveBack)) buttons |= Buttons.Back;
            if (Down(GameAction.MoveLeft)) buttons |= Buttons.Left;
            if (Down(GameAction.MoveRight)) buttons |= Buttons.Right;

            if (_holdJumpUntilRelease && !Down(GameAction.Jump))
                _holdJumpUntilRelease = false;
            if (Down(GameAction.Jump) && !_holdJumpUntilRelease) buttons |= Buttons.Jump;

            var moving = (buttons & (Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right)) != 0;
            if (Down(GameAction.Sprint) && !_game.Aiming)
            {
                buttons |= Buttons.Sprint;
                if (moving)
                    _crouchToggle = _proneToggle = false; // sprinting stands you up
            }

            if (_proneToggle)
                buttons |= Buttons.Prone;
            else if (_crouchToggle)
                buttons |= Buttons.Crouch;
        }
        _game.HeldButtons = buttons;
        _game.AimHeld = _mouseCaptured && Down(GameAction.Aim);

        // Trigger: semi-auto weapons need the press edge, autos use the held state.
        var down = _mouseCaptured && Down(GameAction.Fire);
        if (_swallowClick)
        {
            if (!down)
                _swallowClick = false;
            down = false;
        }
        var pressed = down && Pressed(GameAction.Fire);
        _game.UpdateTrigger(now, down, pressed);
    }

    // =====================================================================
    // Display (windowed / borderless / fullscreen)
    // =====================================================================

    private static int2 _windowedPos = new(100, 100);

    private static void ApplyDisplay()
    {
        _settings.DisplayDirty = false;
        var monitor = Glfw.PrimaryMonitor;
        if (monitor.IsNull())
            return;
        var desktop = monitor.VideoMode;
        var monitorPos = monitor.Position;

        switch (_settings.Display)
        {
            case DisplayMode.Fullscreen:
            {
                var rate = 0;
                foreach (var m in monitor.VideoModes)
                    if (m.Width == _settings.Width && m.Height == _settings.Height)
                        rate = Math.Max(rate, m.RefreshRate);
                if (rate == 0)
                {
                    // Not a mode this monitor has: use the desktop resolution instead.
                    _settings.Width = desktop.Width;
                    _settings.Height = desktop.Height;
                    rate = desktop.RefreshRate;
                }
                _window.SetFullscreen(monitor, new int2(_settings.Width, _settings.Height), rate);
                break;
            }
            case DisplayMode.Borderless:
                if (_window.IsFullscreen)
                    _window.SetWindowed(monitorPos, new int2(desktop.Width, desktop.Height));
                _window.SetAttribute(GlfwWindowAttribute.Decorated, Glfw.Constants.GLFW_FALSE);
                _window.SetWindowed(monitorPos, new int2(desktop.Width, desktop.Height));
                break;
            default:
            {
                _window.SetAttribute(GlfwWindowAttribute.Decorated, Glfw.Constants.GLFW_TRUE);
                var w = Math.Min(_settings.Width, desktop.Width);
                var h = Math.Min(_settings.Height, desktop.Height);
                // Centre on the monitor the first time; afterwards keep where the player put it.
                var pos = _windowedPos;
                if (pos.X < monitorPos.X || pos.Y < monitorPos.Y || pos.X + w > monitorPos.X + desktop.Width)
                    pos = new int2(monitorPos.X + (desktop.Width - w) / 2, monitorPos.Y + Math.Max(40, (desktop.Height - h) / 2));
                _window.SetWindowed(pos, new int2(w, h));
                break;
            }
        }
        Glfw.SwapInterval(_settings.VSync ? 1 : 0); // some drivers reset this on a mode change
        _skipNextMouseDelta = true;
        Console.WriteLine($"Display: {_settings.Display} {_window.FramebufferSize.X}x{_window.FramebufferSize.Y}");
    }

    // =====================================================================
    // Rendering
    // =====================================================================


    private static float CurrentFov() => BaseFov / float.Lerp(1f, _game.Active.Def.AdsZoom, _game.AimT);

    private static bool Scoped => _game.Active.Def.AdsZoom >= 3f && _game.AimT > 0.85f;

    private static void Render(double now)
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        float w = fb.X, h = fb.Y;

        Gfx.BeginFrame(fb.X, fb.Y, World.Map.Sky);

        // ---- Camera ---------------------------------------------------------
        var local = _game.Local;
        Vector3 eye, fwd;
        if (local != null)
        {
            eye = local.Alive ? local.RenderEye : local.RenderPosition + new Vector3(0, 4f, 0);
            fwd = local.Alive ? World.Forward(_game.LookYaw, _game.LookPitch) : World.Forward(_game.LookYaw, -1.2f);
        }
        else
        {
            var a = (float)now * 0.15f;
            eye = new Vector3(MathF.Sin(a) * 26f, 14f, MathF.Cos(a) * 26f);
            fwd = Vector3.Normalize(-eye + new Vector3(0, 1, 0));
        }
        _game.Audio.SetListener(eye, fwd);
        if (_game.Shake > 0.01f)
        {
            // Explosion shake: jitter the camera a few centimetres.
            var t = (float)now * 40f;
            eye += new Vector3(MathF.Sin(t * 1.3f), MathF.Sin(t * 1.7f + 1), MathF.Sin(t * 1.1f + 2)) * 0.08f * _game.Shake;
        }

        var fov = local is { Alive: true } ? CurrentFov() : BaseFov;
        var view = Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, w / h, 0.05f, World.Map.FarPlane);
        Gfx.SetCamera(view, proj);
        Gfx.SetFog(World.Map.Sky, World.Map.FogStart, World.Map.FogEnd);

        // ---- Arena ----------------------------------------------------------
        DrawArena();

        // ---- Players --------------------------------------------------------
        foreach (var p in _game.Players.Values)
        {
            if (p.Id == _game.LocalId && p.Alive)
                continue;
            DrawPlayer(p);
        }

        // ---- Grenades in flight and recon sensors ------------------------------------
        foreach (var pr in _game.Projectiles.Values)
        {
            var c = Throwables.Get(pr.Kind).Color;
            var spin = Matrix4x4.CreateRotationY((float)(now - pr.Born) * 9f) * Matrix4x4.CreateRotationX((float)(now - pr.Born) * 7f);
            Gfx.Draw(Matrix4x4.CreateScale(0.16f, 0.2f, 0.16f) * spin * Matrix4x4.CreateTranslation(pr.Pos), c);
        }
        foreach (var s in _game.Sensors)
        {
            var blink = ((int)((now - s.Born) * 2) % 2) == 0;
            Gfx.Draw(Matrix4x4.CreateScale(0.18f, 0.12f, 0.18f) * Matrix4x4.CreateTranslation(s.Pos), Throwables.Get(ThrowableKind.Recon).Color * (blink ? 1.4f : 0.6f), 1f, mode: blink ? 2 : 0);
            Gfx.Draw(Matrix4x4.CreateScale(0.03f, 0.5f, 0.03f) * Matrix4x4.CreateTranslation(s.Pos + new Vector3(0, 0.3f, 0)), new Vector3(0.2f, 0.2f, 0.2f)); // antenna
        }

        // ---- Tracers --------------------------------------------------------
        Gfx.SetBlend(true);
        foreach (var t in _game.Tracers)
        {
            var age = (float)(now - t.Born);
            var alpha = Math.Clamp(1f - age / 0.25f, 0f, 1f);
            var d = t.To - t.From;
            var len = d.Length();
            if (len < 0.01f)
                continue;
            d /= len;
            var yaw = MathF.Atan2(d.X, -d.Z);
            var pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
            var model =
                Matrix4x4.CreateScale(t.Width, t.Width, len)
                * Matrix4x4.CreateRotationX(pitch)
                * Matrix4x4.CreateRotationY(-yaw)
                * Matrix4x4.CreateTranslation((t.From + t.To) * 0.5f);
            Gfx.Draw(model, t.Color, alpha, mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(0.1f * (1 + age * 4)) * Matrix4x4.CreateTranslation(t.To), new Vector3(1f, 0.9f, 0.6f), alpha, mode: 2);
        }

        // ---- Explosions, smoke, recon reveal ---------------------------------------
        Gfx.SetDepthWrite(false);
        foreach (var b in _game.Blasts)
        {
            var age = (float)(now - b.Born);
            var k = Math.Clamp(age / 0.35f, 0f, 1f);
            var alpha = Math.Clamp(1f - age / 0.6f, 0f, 1f);
            var size = b.Radius * (0.3f + 0.7f * k) * 1.2f;
            Gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(b.Pos), new Vector3(1f, 0.55f, 0.15f), alpha * 0.55f, mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(size * 0.6f) * Matrix4x4.CreateRotationY(-0.3f) * Matrix4x4.CreateTranslation(b.Pos), new Vector3(1f, 0.9f, 0.5f), alpha * 0.8f, mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(size * 0.9f, 0.05f, size * 0.9f) * Matrix4x4.CreateTranslation(b.Pos with { Y = b.Pos.Y + 0.02f }), new Vector3(0.1f, 0.08f, 0.06f), alpha * 0.6f);
        }
        foreach (var cloud in _game.Smokes)
        {
            var age = (float)(now - cloud.Born);
            var grow = Math.Clamp(age / 1.2f, 0.15f, 1f);
            var fade = Math.Clamp((float)(cloud.Until - now) / 2.5f, 0f, 1f);
            var radius = Throwables.Get(ThrowableKind.Smoke).Radius;
            var rng = new Random(cloud.Born.GetHashCode());
            for (var i = 0; i < 14; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.6f, (float)rng.NextDouble() - 0.5f) * radius * 1.3f * grow;
                var puff = (2.2f + (float)rng.NextDouble() * 1.6f) * grow;
                var drift = new Vector3(MathF.Sin(age * 0.3f + i), 0.1f * age, MathF.Cos(age * 0.25f + i)) * 0.4f;
                var shade = 0.62f + (float)rng.NextDouble() * 0.12f;
                Gfx.Draw(Matrix4x4.CreateScale(puff) * Matrix4x4.CreateRotationY(i) * Matrix4x4.CreateTranslation(cloud.Pos + new Vector3(0, 1.2f, 0) + offset + drift),
                    new Vector3(shade, shade, shade + 0.02f), 0.55f * fade, mode: 2);
            }
        }
        // Enemies near your Recon Pulse show through walls.
        Gfx.SetDepthTest(false);
        foreach (var p in _game.Players.Values)
        {
            if (!_game.IsRevealed(p, now))
                continue;
            var hb = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var pulse = 0.35f + 0.15f * MathF.Sin((float)now * 8f);
            Gfx.DrawBox(hb, new Vector3(1f, 0.15f, 0.1f), mode: 2);
            Gfx.Draw(Matrix4x4.CreateScale(hb.Half * 2.2f) * Matrix4x4.CreateTranslation(hb.Center), new Vector3(1f, 0.3f, 0.2f), pulse, mode: 2);
        }
        Gfx.SetDepthTest(true);
        Gfx.SetDepthWrite(true);
        Gfx.SetBlend(false);

        Gfx.SetFog(World.Map.Sky, 0, 0); // no haze on the viewmodel and HUD

        // ---- First-person gun -----------------------------------------------
        if (local is { Alive: true } && !Scoped)
            DrawViewModel(now, proj);

        // ---- HUD ------------------------------------------------------------
        DrawHud(now, w, h, local, fov);
    }

    /// <summary>Box gun model shared by the viewmodel and other players. Origin = grip, -Z = muzzle.</summary>
    /// <summary>
    /// The attachment showcase: paints the loadout window's backdrop, then the selected gun large and
    /// slowly turning in the middle (side-on, muzzle to the left). The UI cards are drawn over it.
    /// </summary>
    private static void DrawGunsmithPreview(GunsmithPreview pv, double now)
    {
        var fb = _window.FramebufferSize;
        var ws = _window.Size;
        if (fb.X <= 0 || fb.Y <= 0 || ws.X <= 0 || ws.Y <= 0)
            return;
        float sx = fb.X / (float)ws.X, sy = fb.Y / (float)ws.Y;

        // Backdrop over the whole window, with a soft "stage" behind the gun
        Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, fb.X, 0, fb.Y, -1, 1));
        Gfx.SetDepthTest(false);
        Gfx.SetBlend(true);
        float wx = pv.WinMin.X * sx, wy = fb.Y - pv.WinMax.Y * sy, ww = (pv.WinMax.X - pv.WinMin.X) * sx, wh = (pv.WinMax.Y - pv.WinMin.Y) * sy;
        Gfx.Rect(wx, wy, ww, wh, new Vector3(0.035f, 0.04f, 0.05f), 0.97f);
        float vx = pv.ViewMin.X * sx, vy = fb.Y - pv.ViewMax.Y * sy, vw = (pv.ViewMax.X - pv.ViewMin.X) * sx, vh = (pv.ViewMax.Y - pv.ViewMin.Y) * sy;
        for (var i = 0; i < 6; i++)
        {
            var inset = i * MathF.Min(vw, vh) * 0.06f;
            Gfx.Rect(vx + inset, vy + inset, vw - 2 * inset, vh - 2 * inset, new Vector3(0.09f, 0.1f, 0.13f), 0.18f);
        }
        Gfx.SetBlend(false);

        // The gun, in its own viewport
        Gfx.SetViewport((int)vx, (int)vy, (int)vw, (int)vh);
        Gfx.ClearDepthBuffer();
        Gfx.SetDepthTest(true);
        var def = pv.Def;
        var front = -def.Length * 1.25f - 0.2f; // roughly the muzzle (with a suppressor)
        var backEnd = 0.3f; // end of the stock
        var span = backEnd - front;
        var aspect = vw / MathF.Max(vh, 1);
        const float fov = 0.5f;
        var dist = MathF.Max(span * 0.34f / aspect, span * 0.24f) / MathF.Tan(fov / 2);
        var t = (float)now;
        var yaw = MathF.Sin(t * 0.5f) * 0.28f;
        var view = Matrix4x4.CreateLookAt(new Vector3(-dist, 0.04f, 0), Vector3.Zero, Vector3.UnitY);
        Gfx.SetCamera(view, Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.05f, 50f));
        var pose = Matrix4x4.CreateTranslation(0, 0, -(front + backEnd) / 2) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(0.08f);
        DrawGun(def, pose, false);
        Gfx.SetViewport(0, 0, fb.X, fb.Y);
    }

    /// <summary>The gun pictures on the loadout screen's weapon cards (drawn after the UI, each in its own viewport).</summary>
    private static void DrawGunThumbs(double now)
    {
        var fb = _window.FramebufferSize;
        var ws = _window.Size;
        if (fb.X <= 0 || fb.Y <= 0 || ws.X <= 0 || ws.Y <= 0)
            return;
        float sx = fb.X / (float)ws.X, sy = fb.Y / (float)ws.Y;
        var scissor = false;
        foreach (var th in Ui.Thumbs)
        {
            float vx = th.Min.X * sx, vy = fb.Y - th.Max.Y * sy, vw = (th.Max.X - th.Min.X) * sx, vh = (th.Max.Y - th.Min.Y) * sy;
            if (vw < 8 || vh < 8)
                continue;
            if (th.Clipped)
            {
                // Cut the picture to its scrolling list (ImGui's clipping doesn't reach these); skip it if it's scrolled out.
                var x0 = MathF.Max(vx, th.ClipMin.X * sx);
                var x1 = MathF.Min(vx + vw, th.ClipMax.X * sx);
                var y0 = MathF.Max(vy, fb.Y - th.ClipMax.Y * sy);
                var y1 = MathF.Min(vy + vh, fb.Y - th.ClipMin.Y * sy);
                if (x1 - x0 < 1 || y1 - y0 < 1)
                    continue;
                OpenGl.Scissor((int)x0, (int)y0, (int)MathF.Ceiling(x1 - x0), (int)MathF.Ceiling(y1 - y0));
                if (!scissor)
                {
                    OpenGl.Enable(GlCapability.ScissorTest);
                    scissor = true;
                }
            }
            else if (scissor)
            {
                OpenGl.Native.glDisable(GlCapability.ScissorTest);
                scissor = false;
            }
            Gfx.SetViewport((int)vx, (int)vy, (int)vw, (int)vh);
            Gfx.ClearDepthBuffer();
            Gfx.SetDepthTest(true);
            Gfx.SetBlend(false);
            var def = th.Def;
            var front = -def.Length * 1.25f - 0.2f;
            var backEnd = 0.3f;
            var span = backEnd - front;
            var aspect = vw / MathF.Max(vh, 1);
            const float fov = 0.5f;
            // Fit the gun's length to ~90% of the width, or its height (~a third of its length) to the height.
            var dist = MathF.Max(span * 0.5f / (MathF.Tan(fov / 2) * aspect * 0.9f), span * 0.2f / (MathF.Tan(fov / 2) * 0.9f));
            var view = Matrix4x4.CreateLookAt(new Vector3(-dist, 0.05f, 0), Vector3.Zero, Vector3.UnitY);
            Gfx.SetCamera(view, Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.05f, 50f));
            var pose = Matrix4x4.CreateTranslation(0, 0, -(front + backEnd) / 2) * Matrix4x4.CreateRotationY(0.12f) * Matrix4x4.CreateRotationX(0.05f);
            DrawGun(def, pose, false);
            if (th.Dim > 0)
            {
                // Locked: a dark veil over the picture
                Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, vw, 0, vh, -1, 1));
                Gfx.SetDepthTest(false);
                Gfx.SetBlend(true);
                Gfx.Rect(0, 0, vw, vh, new Vector3(0.045f, 0.047f, 0.06f), Math.Clamp(th.Dim, 0f, 1f));
                Gfx.SetBlend(false);
            }
        }
        if (scissor)
            OpenGl.Native.glDisable(GlCapability.ScissorTest);
        Gfx.SetViewport(0, 0, fb.X, fb.Y);
    }

    /// <summary>Box gun model shared by the viewmodel, other players and the gunsmith preview (see GunModel.cs).</summary>
    private static void DrawGun(WeaponDef def, in Matrix4x4 pose, bool flash) => GunModel.Draw(Gfx, def, pose, flash, (float)Glfw.Time);

    private static void DrawPlayer(Player p)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var color = p.Color;

        if (!p.Alive)
        {
            Gfx.Draw(Matrix4x4.CreateScale(0.8f, 0.25f, 1.8f) * Matrix4x4.CreateTranslation(0, 0.125f, 0) * rot * at, color * 0.4f);
            return;
        }

        var visorColor = new Vector3(0.05f, 0.05f, 0.08f);
        float barY;
        switch (p.RenderStance)
        {
            case Stance.Prone:
            {
                // Lying flat along their facing: body behind, head and gun out front.
                Gfx.Draw(Matrix4x4.CreateScale(0.6f, 0.4f, 1.3f) * Matrix4x4.CreateTranslation(0, 0.2f, 0.15f) * rot * at, color);
                Gfx.Draw(Matrix4x4.CreateScale(0.4f, 0.38f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.3f, -0.75f) * rot * at, color * 1.1f);
                Gfx.Draw(Matrix4x4.CreateScale(0.34f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 0.36f, -0.96f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.25f, 0.3f, -0.9f) * rot * at;
                DrawGun(p.WeaponDef, gunPose, false);
                barY = 0.9f;
                break;
            }
            case Stance.Crouch:
            {
                const float body = 0.85f, top = 1.2f;
                Gfx.Draw(Matrix4x4.CreateScale(0.8f, body, 0.6f) * Matrix4x4.CreateTranslation(0, body / 2, 0) * rot * at, color);
                Gfx.Draw(Matrix4x4.CreateScale(0.45f, top - body, 0.45f) * Matrix4x4.CreateTranslation(0, (body + top) / 2, -0.05f) * rot * at, color * 1.1f);
                Gfx.Draw(Matrix4x4.CreateScale(0.38f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 1.07f, -0.29f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.3f, 0.82f, -0.3f) * rot * at;
                DrawGun(p.WeaponDef, gunPose, false);
                barY = 1.45f;
                break;
            }
            default:
            {
                Gfx.Draw(Matrix4x4.CreateScale(0.8f, World.HeadStart, 0.5f) * Matrix4x4.CreateTranslation(0, World.HeadStart / 2, 0) * rot * at, color);
                var headH = World.PlayerHeight - World.HeadStart;
                Gfx.Draw(Matrix4x4.CreateScale(0.45f, headH, 0.45f) * Matrix4x4.CreateTranslation(0, World.HeadStart + headH / 2, 0) * rot * at, color * 1.1f);
                Gfx.Draw(Matrix4x4.CreateScale(0.38f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 1.63f, -0.24f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.3f, 1.3f, -0.25f) * rot * at;
                DrawGun(p.WeaponDef, gunPose, false);
                barY = 2.05f;
                break;
            }
        }

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        Gfx.Draw(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0) * rot * at,
            Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac), 1f, mode: 2);
    }

    private static void DrawViewModel(double now, Matrix4x4 proj)
    {
        Gfx.ClearDepthBuffer();
        // The viewmodel always uses the un-zoomed FOV so the gun doesn't balloon when aiming.
        var fb = _window.FramebufferSize;
        var vmProj = Matrix4x4.CreatePerspectiveFieldOfView(70f * MathF.PI / 180f, fb.X / (float)fb.Y, 0.01f, 10f);
        Gfx.SetCamera(Matrix4x4.Identity, vmProj); // camera space: -Z forward, +X right, +Y up

        var def = _game.Active.Def;
        var sinceShot = (float)(now - _game.LastShotTime);
        var kick = MathF.Max(0, 1 - sinceShot / 0.1f) * (0.03f + def.RecoilDeg * 0.015f);

        var reload = 0f;
        var rp = _game.ReloadProgress(now);
        if (rp >= 0)
            reload = MathF.Sin(rp * MathF.PI);
        var equip = now < _game.EquipUntil ? (float)((_game.EquipUntil - now) / def.EquipTime) : 0f;

        var sim = _game.Local!.Sim;
        var speed = new Vector2(sim.Velocity.X, sim.Velocity.Z).Length();
        var bob = sim.Grounded ? MathF.Sin((float)now * 10f) * 0.012f * MathF.Min(speed / World.WalkSpeed, 1.5f) * (1 - _game.AimT * 0.8f) : 0f;

        // Hip position -> centred aim position
        var a = _game.AimT;
        var x = float.Lerp(0.24f, 0f, a);
        var y = float.Lerp(-0.22f, -GunModel.SightHeight(def), a); // aimed: the sight line sits on the centre of the screen
        var z = float.Lerp(-0.42f, GunModel.AdsZ(def), a) + kick; // optics come right up to the eye (closest for the ACOG)

        // Melee: the gun swings across and jabs forward, then comes back.
        var mp = _game.MeleeProgress(now);
        var jab = mp >= 0 ? MathF.Sin(mp * MathF.PI) : 0f;
        if (mp >= 0)
        {
            x -= jab * 0.18f;
            z -= jab * 0.22f;
        }

        var pose =
            Matrix4x4.CreateRotationX(-reload * 0.9f - equip * 1.2f + jab * 0.35f)
            * Matrix4x4.CreateRotationY(jab * 0.9f)
            * Matrix4x4.CreateRotationZ(jab * 0.5f)
            * Matrix4x4.CreateTranslation(x, y - reload * 0.15f - equip * 0.25f + bob, z);

        DrawGun(def, pose, now < _game.MuzzleFlashUntil);
    }

    private static void DrawHud(double now, float w, float h, Player? local, float fov)
    {
        Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, w, 0, h, -1, 1));
        Gfx.SetDepthTest(false);
        Gfx.SetBlend(true);

        var white = Vector3.One;
        var black = Vector3.Zero;
        float cx = w / 2, cy = h / 2;
        var k = Ui.Scale / 1.2f; // HUD drawn here follows the UI scale setting (1.0 at 1080p)

        if (local is { Alive: true })
        {
            if (Scoped)
            {
                // Sniper scope: black surround with a square window and fine crosshair.
                var r = h * 0.42f;
                Gfx.Rect(0, 0, cx - r, h, black);
                Gfx.Rect(cx + r, 0, w - (cx + r), h, black);
                Gfx.Rect(cx - r, 0, 2 * r, cy - r, black);
                Gfx.Rect(cx - r, cy + r, 2 * r, h - (cy + r), black);
                Gfx.Rect(cx - r, cy - 1, 2 * r, 2, black, 0.9f);
                Gfx.Rect(cx - 1, cy - r, 2, 2 * r, black, 0.9f);
                Gfx.Rect(cx - 2, cy - 2, 4, 4, new Vector3(1, 0.2f, 0.2f));
            }
            else if (_game.Active.Def.Optic is OpticKind.RedDot or OpticKind.Holo or OpticKind.Acog && _game.AimT > 0.6f)
            {
                // Optic reticle while aiming down sights.
                var opt = _game.Active.Def.Optic;
                var a = Math.Clamp((_game.AimT - 0.6f) / 0.4f, 0f, 1f);
                var red = new Vector3(1f, 0.15f, 0.1f);
                var dot = MathF.Max(3, MathF.Round(3 * k));
                Gfx.Rect(cx - dot / 2, cy - dot / 2, dot, dot, opt == OpticKind.Holo ? new Vector3(0.3f, 0.9f, 1f) : red, a);
                if (opt == OpticKind.Holo)
                {
                    var r = 22 * k;
                    for (var i = 0; i < 24; i++)
                    {
                        var ang = i / 24f * MathF.Tau;
                        Gfx.Rect(cx + MathF.Cos(ang) * r - 1.5f, cy + MathF.Sin(ang) * r - 1.5f, 3, 3, new Vector3(0.3f, 0.9f, 1f), a * 0.9f);
                    }
                }
                else if (opt == OpticKind.Acog)
                {
                    // Chevron with a drop line, inside a soft dark ring.
                    for (var i = 1; i <= 7; i++)
                    {
                        var step = i * 2.2f * k;
                        Gfx.Rect(cx - step - 1.5f, cy - step - 1.5f, 3, 3, red, a);
                        Gfx.Rect(cx + step - 1.5f, cy - step - 1.5f, 3, 3, red, a);
                    }
                    Gfx.Rect(cx - 1, cy - 70 * k, 2, 50 * k, black, a * 0.8f);
                    var ring = h * 0.46f;
                    Gfx.Rect(0, 0, cx - ring, h, black, a * 0.55f);
                    Gfx.Rect(cx + ring, 0, w - (cx + ring), h, black, a * 0.55f);
                }
            }
            else
            {
                // Dynamic crosshair: gap matches the real spread cone at this FOV.
                var spread = _game.CurrentSpread();
                var gap = MathF.Max(4, MathF.Tan(spread) / MathF.Tan(fov / 2) * (h / 2));
                var len = 10f * k;
                var th = MathF.Max(2, MathF.Round(2 * k));
                var alpha = 0.9f * (1 - _game.AimT * 0.6f);
                Gfx.Rect(cx - th / 2, cy + gap, th, len, white, alpha);
                Gfx.Rect(cx - th / 2, cy - gap - len, th, len, white, alpha);
                Gfx.Rect(cx + gap, cy - th / 2, len, th, white, alpha);
                Gfx.Rect(cx - gap - len, cy - th / 2, len, th, white, alpha);
                Gfx.Rect(cx - 1, cy - 1, 2, 2, white, 0.8f);
            }

            if (now < _game.HitMarkerUntil)
            {
                var c = _game.HitMarkerHead ? new Vector3(1f, 0.85f, 0.2f) : new Vector3(1f, 0.25f, 0.25f);
                for (var i = 0; i < 4; i++)
                {
                    var sx = (i & 1) == 0 ? -1 : 1;
                    var sy = (i & 2) == 0 ? -1 : 1;
                    Gfx.Rect(cx + sx * 14 * k - 3 * k, cy + sy * 14 * k - 3 * k, 6 * k, 6 * k, c);
                }
            }

            // Health bar (bottom-left of the HUD safe area; the number is drawn by ImGui)
            var (safeLeft, safeWidth) = HudLayout.Span(w, h, _settings.HudWidth);
            var hp = Math.Clamp(local.Health / (float)World.MaxHealth, 0f, 1f);
            float m = 30 * k, bw = 300 * k, bh = 18 * k, inset = 3 * k;
            var left = safeLeft + m;
            Gfx.Rect(left, m, bw, bh, black, 0.5f);
            Gfx.Rect(left + inset, m + inset, (bw - 2 * inset) * hp, bh - 2 * inset, Vector3.Lerp(new Vector3(1, 0.15f, 0.15f), new Vector3(0.25f, 0.95f, 0.35f), hp), 0.95f);

            // Ammo bar (bottom-right) or reload progress
            var def = _game.Active.Def;
            var right = safeLeft + safeWidth - m;
            var rp = _game.ReloadProgress(now);
            Gfx.Rect(right - bw, m, bw, bh, black, 0.5f);
            var fill = rp >= 0 ? rp : _game.Active.Ammo / (float)def.Mag;
            Gfx.Rect(right - bw + inset, m + inset, (bw - 2 * inset) * fill, bh - 2 * inset,
                rp >= 0 ? new Vector3(0.95f, 0.5f, 0.15f) : new Vector3(1f, 0.85f, 0.3f), 0.95f);
        }

        if (now < _game.DamageFlashUntil)
        {
            var a = (float)((_game.DamageFlashUntil - now) / 0.4) * 0.6f;
            Gfx.Rect(0, 0, w, h, new Vector3(0.9f, 0.05f, 0.05f), Math.Clamp(a, 0f, 0.6f));
        }
        if (local is { Alive: false })
            Gfx.Rect(0, 0, w, h, new Vector3(0.5f, 0f, 0f), 0.35f);
        if (!_mouseCaptured)
            Gfx.Rect(0, 0, w, h, black, MenusOpen ? 0.55f : 0.35f);

        Gfx.SetBlend(false);
        Gfx.SetDepthTest(true);
    }

}
