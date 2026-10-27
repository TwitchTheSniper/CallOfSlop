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
///   --zombies-round N              hosting Zombies: runs start at round N (the lobby's Start round, not saved)
///   --test-kit                     hosting Zombies: the test kit on maps that allow it (Horde Test; not saved)
///   --save-dir &lt;path&gt;              keep the saves in that folder (see SavePaths.cs)
/// Release: --bake DIR (baked nav graphs for every shipped map, BotNavBake.cs).
/// Modding helpers: --check-map FILE, --check-maps DIR, --gen-map NAME, --gen-maps, --gen-plan FILE PNG, --map-ops, --export-maps DIR, --check-weapon FILE, --export-weapons DIR,
/// --check-part FILE, --check-parts [DIR], --export-parts DIR [--resolved], --watch-parts (hot reload while playing).
/// </summary>
public static unsafe partial class Program
{
    public const ushort DefaultPort = 60000;
    public static double FrameDt;

    private const float BaseSensitivity = 0.0022f;

    // Keep the GL loader delegate rooted for the lifetime of the process.
    private static readonly GlGetProcAddressFunc GlLoader = GetGlProc;

    private static GlfwWindow _window = null!;
    private static readonly Renderer Gfx = new();
    private static readonly TerrainRenderer TerrainGfx = new();
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
    private static bool _wasDeadUi, _wasResults;
    private static readonly bool[] _voteKeyDown = new bool[3];
    private static double _deathMenuAt;
    private static readonly DeathCam _deathCam = new(); // the camera after you die (DeathCam.cs)
    private const string LoadoutLockedText = "You can only change your loadout when you're dead";
    private const string GunGameLoadoutText = "Gun Game: everyone gets the same gun and perks, so there's no loadout this match.";
    private static bool _quit;
    private static bool _vsync;

    // Command-line test options
    private static int _lagMs, _lossPct;
    private static bool _forceNoUpnp;

    private static float BaseFov => _settings.Fov * MathF.PI / 180f;

    public static int Main(string[] args)
    {
        SetUpLogFile(args);
        // No full blocking garbage collections mid-match (one took 34 ms in the self-test): gen 2 is collected in the
        // background instead, unless memory runs low. Server ticks and frames only see the short gen 0/1 pauses.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Console.WriteLine($"Call of Slop - built with the BRUTAL framework, Copyright (c) 2024 Ahwoo Limited. {DateTime.Now}");

        if (args.Any(a => a.Equals("--selftest-list", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.List(); // the self-test's section names, for --selftest-only
        var selfTestOnly = args.SkipWhile(a => !a.Equals("--selftest-only", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        if (selfTestOnly?.StartsWith("--") == true || selfTestOnly == null && args.Any(a => a.Equals("--selftest-only", StringComparison.OrdinalIgnoreCase)))
            selfTestOnly = ""; // given without names: an error that lists the valid ones, never a silent full run
        if (selfTestOnly != null || args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run(selfTestOnly); // headless checks (all, or only the named sections), see SelfTest.cs
        if (args.SkipWhile(a => !a.Equals("--export-maps", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } exportDir)
            return Maps.Export(exportDir); // writes the loaded maps back out as map files
        if (args.SkipWhile(a => !a.Equals("--check-map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } checkFile)
            return Maps.Check(checkFile); // for map makers: does this file load, and does it pass the "Map contract" checks?
        if (args.SkipWhile(a => !a.Equals("--check-maps", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } checkDir)
            return Maps.CheckAll(checkDir); // the same for every map file in a folder (a pack)
        if (args.Any(a => a.Equals("--map-ops", StringComparison.OrdinalIgnoreCase)))
            return Maps.PrintOps(); // for map makers: every op and its parameters
        if (args.SkipWhile(a => !a.Equals("--gen-map", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } genName)
            return MapKit.MapKitCli.GenMap(genName, args); // the map kit (Source/MapKit): run one generator, write and check its map
        if (args.Any(a => a.Equals("--gen-maps", StringComparison.OrdinalIgnoreCase)))
            return MapKit.MapKitCli.GenMaps(args); // every generator: which outputs differ from the committed files
        if (args.SkipWhile(a => !a.Equals("--gen-plan", StringComparison.OrdinalIgnoreCase)).Skip(1).Take(2).ToArray() is [var planMap, var planPng])
            return MapKit.MapKitCli.GenPlan(planMap, planPng); // a top-down PNG plan of any map file
        if (args.SkipWhile(a => !a.Equals("--check-weapon", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } weaponFile)
            return WeaponSet.Check(weaponFile); // for modders: does this weapon (or attachment) file load?
        if (args.SkipWhile(a => !a.Equals("--export-weapons", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } weaponDir)
            return WeaponSet.Export(weaponDir); // writes the loaded weapons and attachments back out as files
        if (args.SkipWhile(a => !a.Equals("--check-part", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } partFile)
            return PartTools.Check(partFile); // for part makers: does this part (fragment, base, registry) file load, and where does it fit?
        if (args.Any(a => a.Equals("--check-parts", StringComparison.OrdinalIgnoreCase)))
            return PartTools.CheckAll(args.SkipWhile(a => !a.Equals("--check-parts", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--")));
        if (args.SkipWhile(a => !a.Equals("--export-parts", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } partDir)
            return PartTools.Export(partDir, args.Any(a => a.Equals("--resolved", StringComparison.OrdinalIgnoreCase))); // part files normalised (or resolved)
        if (args.Any(a => a.Equals("--master", StringComparison.OrdinalIgnoreCase)))
            return MasterServerApp.Run(args); // public server list, see ServerBrowser.cs
        if (args.SkipWhile(a => !a.Equals("--bake", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } bakeDir)
            return NavBake.BakeAll(bakeDir); // Release workflow: every shipped map's nav graph, built once and shipped (BotNavBake.cs)
        if (args.SkipWhile(a => !a.Equals("--stuck-report", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } stuckDir)
            return StuckSpots.Cli(stuckDir, args); // merges stuck-spot reports and logs from testers and CI (StuckSpots.cs)
        if (args.SkipWhile(a => !a.Equals("--verify-update", StringComparison.OrdinalIgnoreCase)).Skip(1).Take(2).ToArray() is [var manifestFile, var zipFile])
            return UpdateVerifyCli.Run(manifestFile, zipFile, // Release workflow: the game's own updater checks the package (UpdateVerifyCli.cs)
                args.SkipWhile(a => !a.Equals("--platform", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? UpdateManifest.CurrentPlatform);

        // Saves live in %APPDATA%\CallOfSlop (or portable.txt / --save-dir) so they carry over between builds (SavePaths.cs).
        // The smoke test works on a throwaway copy and never touches the real save folder.
        if (args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase)))
            SavePaths.UseSmokeSandbox(args);
        else
            SavePaths.Init(args);
        _settings = Settings.Load();
        Ui.BenchSettings = _settings; // the workbench's "Show incompatible parts" checkbox
        _progress = Progress.Load();
        AppearanceStore.Load(); // before the loadouts: a first run takes the hat from loadout.txt's old hat= line (Appearance.cs)
        LoadoutStore.Load();
        Console.WriteLine($"[update] this build: {BuildInfo.Label} (protocol {ServerInfo.ProtocolVersion})");
        if (!args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase)))
        {
            // Update check (Source/Updates): in the background, never blocking startup; release builds only, at most every 6 h.
            Updater.Shared = new Updater(null, SavePaths.PathOf("update_check.txt"), SavePaths.ExeFolder);
            if (_settings.UpdateCheck && BuildInfo.IsRelease)
                Updater.Shared.Check(manual: false);
            // Old versions the updater installed are removed once this build reaches its menu (UpdateCleanup.cs).
            UpdateCleaner.Shared = new UpdateCleaner(SavePaths.ExeFolder, BuildInfo.ReleaseVersion, SavePaths.PathOf(UpdateCleanup.LogFileName), SavePaths.Folder);
        }
        _lagMs = FlagValue(args, "--lag");
        _lossPct = FlagValue(args, "--loss");
        _forceNoUpnp = args.Any(a => a.Equals("--no-upnp", StringComparison.OrdinalIgnoreCase));
        _smoke = args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase));
        (_cliStartRound, var roundValue) = ZombiesRoundFlag(args); // --zombies-round N (or =N): the next Zombies runs start at round N
        _cliTestKit = args.Any(a => a.Equals("--test-kit", StringComparison.OrdinalIgnoreCase));
        var plain = SavePaths.WithoutSaveDir(args).Where(a => !a.StartsWith("--") && !ReferenceEquals(a, roundValue)).ToArray();

        // Remove the router mapping even if the process is closed abruptly.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _upnp?.Dispose();

        try
        {
            AudioSystem.Init();
            AudioSystem.MasterVolume = _settings.Volume;
            AudioSystem.Music.Volume = _settings.MusicVolume;
            (AudioSystem.EffectsVolume, AudioSystem.FootstepVolume) = (_settings.EffectsVolume, _settings.FootstepVolume);
            AudioSystem.OwnGunVolume = _settings.OwnGunVolume;
            LoadAudioMix(); // audio_mix.json from the Audio mixer window, if there is one (Program.AudioMixer.cs)
            CreateWindow();
            RunSplash(); // the BRUTAL logo before anything else, even with a command-line shortcut (licence section 2.2)

            // Command-line shortcuts skip the lobby.
            if (UrlProtocol.FindLink(plain) is { } inviteLink)
                JoinFromInvite(inviteLink); // a clicked callofslop://join/... link (Program.Listing.cs)
            else if (plain.Length > 0 && plain[0].Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                if (plain.Length > 1 && ushort.TryParse(plain[1], out var hp))
                    _settings.Port = hp;
                StartSession(LobbyAction.Host);
            }
            else if (plain.Length > 1 && plain[0].Equals("join", StringComparison.OrdinalIgnoreCase) && InviteCode.LooksLikeInvite(plain[1]))
                JoinFromInvite(plain[1]); // "join ABCD-EFGH-JKMN-PQRS"
            else if (plain.Length > 0 && plain[0].Equals("join", StringComparison.OrdinalIgnoreCase))
            {
                if (plain.Length > 1)
                    _settings.JoinAddress = Settings.CleanAddress(plain[1]);
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
            _manualList?.Dispose(); // "Run a server list for friends" (Program.Listing.cs)
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
    private static double _smokeHoldStart;
    private static int _smokeStage;
    private static int _smokeFrames;
    private static double _smokeWorstDt;
    private static readonly string[] SmokeMaps = ["Proto Box", "Courtyard", "Harbor", "Downtown", "Highlands", "Military Base", "Terrain Test", "Adobe Mesa"]; // Adobe Mesa: a former High Ground map, now in Legacy (terrain, foliage, movers)

    private static void SmokeTick(double now)
    {
        _smokeFrames++;
        _smokeWorstDt = Math.Max(_smokeWorstDt, FrameDt);
        var t = now - _smokeStepStart;
        if (FoliageSmokeActive)
        {
            FoliageSmokeTick(now);
            return;
        }
        if (MoversSmokeActive)
        {
            MoversSmokeTick(now); // Program.Movers.cs
            return;
        }
        if (PropsSmokeActive)
        {
            PropsSmokeTick(now); // Program.Props.cs
            return;
        }
        if (MeshSmokeActive)
        {
            MeshSmokeTick(now); // Program.Meshes.cs
            return;
        }
        if (LegsSmokeActive)
        {
            LegsSmokeTick(now); // Program.Legs.cs
            return;
        }
        if (SkySmokeActive)
        {
            SkySmokeTick(now); // Program.Sky.cs
            return;
        }
        if (CaveSmokeActive)
        {
            CaveSmokeTick(now); // Program.Caves.cs
            return;
        }
        if (WorkbenchSmokeActive)
        {
            WorkbenchSmokeTick(now); // Program.Workbench.cs
            return;
        }
        if (ZombiesSmokeActive)
        {
            ZombiesSmokeTick(now); // Program.Zombies.cs
            if (!ZombiesSmokeActive)
            {
                StartCoopSmoke(now); // a co-op defend wave on Silo (Program.Pve.cs), then the usual chain carries on
                if (!CoopSmokeActive)
                    StartMoversSmoke(now);
            }
            return;
        }
        if (CoopSmokeActive)
        {
            CoopSmokeTick(now); // Program.Pve.cs
            if (!CoopSmokeActive)
                StartMoversSmoke(now); // the usual chain carries on
            return;
        }
        if (_smokeMap < 0)
        {
            // Lobby: two gun galleries (stock, then fully kitted), screenshotted, then on to the maps.
            if (_smokeStepStart == 0)
            {
                _smokeStepStart = now;
                Console.WriteLine("[smoke] lobby");
                LoadoutStore.SmokeKit = id => SmokeFullKit(Weapons.Get(id), _smokeMap + 1);
                LoadoutStore.ReadOnly = true; // the smoke test must never write your loadout.txt
                AppearanceStore.ReadOnly = true; // nor your appearance.txt
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
            else if (_smokeGallery == 3 && t > 2.5 && Ui.LobbyLoadouts)
            {
                Ui.LobbyLoadouts = false;
                Ui.OpenAppearance(); // main menu > Appearance (UiAppearance.cs)
            }
            else if (_smokeGallery == 3 && t > 2.7 && !_smokeAppearanceShot)
            {
                SmokeShot("appearance"); // the pickers and the turning 3D model with its hat
                _smokeAppearanceShot = true;
                Console.WriteLine($"[smoke] appearance: preview {(Ui.LookPreview != null ? "on" : "OFF")}, look {AppearanceStore.Look}, hat {Cosmetics.KeyOf(AppearanceStore.Hat)}");
            }
            else if (_smokeGallery == 3 && t > 2.8)
            {
                _smokeGallery = 4; // the map select screen (Program.MapSelect.cs)
                Ui.LobbyLoadouts = false;
                Ui.AppearanceOpen = false;
                _shotTaken = false;
            }
            else if (_smokeGallery == 4 && SmokeMapSelectTick(now, t - 2.8))
            {
                _smokeGallery = 0;
                StartWorkbenchSmoke(now); // the weapon workbench and its range (Program.Workbench.cs), then movers, foliage, the maps
            }
            return;
        }

        if (_smokeMap == 0 && _smokeWeaponSelect < 7)
        {
            SmokeWeaponSelect(now, t);
            return;
        }

        var game = _session;
        // Map 0, after the play screenshot: scoped ADS shots (on their own clock, 6.3-9.8 s), then the arms
        // (reload, sprint, melee; own clock), then kill streaks (needs you alive), then the death cam (gets you killed).
        // Map 0: the bots mustn't kill you before the ADS, arms and streak shots (the death cam step kills you on purpose).
        if (_smokeMap == 0 && _smokeStage is 7 or 8 && _smokeDeathCam == 0 && game is { IsServer: true, Local: { Alive: true } me })
            me.ImmuneUntil = now + 0.5;
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeAdsStep <= SmokeAdsOptics.Length * 2)
        {
            SmokeAds(t);
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeArms < 7)
        {
            SmokeArms(game, now, t);
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeStreakStep < 4)
        {
            SmokeStreaks(now);
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeNameTagStep < 3)
        {
            SmokeNameTags(now); // Program.NameTags.cs
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeScoreStep < 3)
        {
            SmokeScoreboard(now); // Program.Scoreboard.cs: the scoreboard with its Level column
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeMixerStep < 3)
        {
            SmokeAudioMixer(now); // Program.AudioMixer.cs: the Audio mixer window over a match, with a test shot on its meters
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeTweakStep < 2)
        {
            SmokeWeaponTweaks(now); // Program.WeaponTweaks.cs: the weapon tweak window over the match (nothing changed or saved)
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeHatStep < 4)
        {
            SmokeHats(now); // Program.Prestige.cs: the five prestige hats on a line-up of bots, then the same bots bare-headed (body model)
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeFrenzyStep < SmokeFrenzyKills + 2)
        {
            SmokeFrenzy(now); // Program.Frenzy.cs
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeFrenzyFxStep < SmokeFrenzyFxClasses.Length)
        {
            SmokeFrenzyFx(now); // Program.FrenzyFx.cs: four classes' frenzy visuals at level 6
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeTacStep < SmokeTacSteps)
        {
            SmokeTactical(now); // Program.Tactical.cs: placed devices and a flashbang, screenshotted
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeRagdollStep < 9)
        {
            SmokeRagdolls(now); // Program.Ragdolls.cs: five bots killed five ways (ragdolls), a blast on the pile, a bot pushed
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeBloodStep < 9)
        {
            SmokeBlood(now); // Program.Blood.cs: the blood overlay at 30% health, a bot's body blood at 20%
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 8 && _smokeDeathCam < 3)
        {
            SmokeDeathCam(now, t);
            return;
        }
        // After asking to spawn: wait out the ready-up warmup and the countdown (which reopens the loadout screen), and
        // the respawn itself, so the play / ADS / arms shots show the live match and not the menu. At most 15 s.
        if (game != null && _smokeStage == 7 && now - _smokeHoldStart < 15
            && (game.Phase is MatchPhase.Warmup or MatchPhase.Countdown || Ui.MenuOpen || game.Local is not { Alive: true }))
        {
            if (game.Phase == MatchPhase.Playing && Ui.MenuOpen && game.Local is { Alive: true })
            {
                Ui.AutoDeploy(game, now);
                SetMouseCaptured(true);
            }
            else if (game.Phase == MatchPhase.Playing && game.Local is not { Alive: true } && !game.RespawnRequested)
                game.RequestRespawn();
            _smokeStepStart = now - 3.4; // the stage clock waits too
            return;
        }
        if (_smokeMap == 0 && _smokeStage == 3 && SmokeOddGunsmithTick(now))
        {
            _smokeStepStart = now - 1.7; // the stage clock waits (Program.Attachments.cs)
            return;
        }
        switch (_smokeStage)
        {
            case 0 when t > 0.8:
                if (_smokeMap == 0 && _teamSmoke == 0)
                    SmokeShot("loadout_overview");
                if (_smokeMap == 0 && SmokeTeamsTick(game, now))
                    break; // the TEAMS window shot (Program.Teams.cs)
                if (_smokeMap == 0 && SmokeLobbyRoomTick(game, now))
                    break; // the lobby room shot (Program.LobbyRoom.cs)
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
                {
                    SmokeShot("perks_0");
                }
                _smokeStage++;
                break;
            case 6 when t > 3.4:
                Ui.SmokeTab(0);
                if (game != null)
                {
                    Ui.SmokeRespawn(game, now);
                    _smokeHoldStart = now;
                    Console.WriteLine($"[smoke] respawn requested (phase {game.Phase}, alive {game.Local?.Alive}, perks {game.ChosenPerks})");
                }
                _smokeStage++;
                break;
            case 7 when t > 6.2:
                game?.TryMelee(now);
                if ((_smokeMap < 4 || SmokeMaps[_smokeMap] is "Terrain Test" or "Adobe Mesa") && game?.Local is { Alive: true })
                    SmokeShot(SmokeMaps[_smokeMap] switch { "Terrain Test" => "play_terrain", "Adobe Mesa" => "play_highground", _ => $"play_{_smokeMap}" });
                _smokeStage++;
                break;
            case 8 when t > 8.5:
                if (World.Map.HasTerrain)
                    Console.WriteLine($"[smoke] terrain: {TerrainGfx.LastFrame.Chunks} chunks, {TerrainGfx.LastFrame.DrawCalls} draw call(s), {TerrainGfx.LastFrame.Triangles} triangles");
                Console.WriteLine($"[smoke] scene graph: {ScenePresentation.Stats}");
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

    private static int _smokeDeathCam;

    /// <summary>
    /// Smoke step on the first map, after the play screenshot: the nearest bot kills you, and 2.2 s later
    /// (the death cam has turned to face it, the kill card is up) a screenshot. Then the usual steps go on.
    /// </summary>
    private static double _smokeDeathAt;

    private static void SmokeDeathCam(double now, double t)
    {
        var game = _session;
        switch (_smokeDeathCam)
        {
            case 0 when t > 6.5:
                var bot = game?.DebugKillLocalByBot(now);
                Console.WriteLine(bot != null ? $"[smoke] death cam: killed by {game!.NameOf(bot.Id)} ({bot.WeaponDef.Name})" : "[smoke] death cam: no bot could kill you (skipped)");
                _smokeDeathCam = bot != null ? 1 : 3;
                _smokeDeathAt = now;
                break;
            case 1 when now - _smokeDeathAt > 2.2: // the camera has turned to the killer and the kill card is up
                SmokeShot("deathcam");
                var info = _deathCam.Info;
                Console.WriteLine($"[smoke] death cam: active {_deathCam.Active}, kill info {(info != null ? $"{info.Kind} by {info.KillerName} with {info.WeaponName} [{Attachments.Describe(info.Kit)}], {info.Distance:0} m, killer HP {info.KillerHp}" : "MISSING")}, "
                    + $"looking at killer {_deathCam.HasTarget}, fov x{_deathCam.FovMul:0.00}, loadout open {Ui.MenuOpen}, thumbs {Ui.Thumbs.Count}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                _smokeDeathCam++;
                break;
            case 2 when now - _smokeDeathAt > 2.4:
                _smokeDeathCam++;
                _smokeStepStart += 2.5; // the usual steps carry on where they were
                break;
        }
    }

    private static int _smokeArms;
    private static double _smokeArmsAt;
    private static Buttons _smokeButtons;
    private static double _smokeArmsStart;

    /// <summary>
    /// Smoke step on the first map: the first-person arms during a reload, a sprint and a melee swing, screenshotted
    /// (smoke_arms_reload / smoke_arms_sprint / smoke_arms_melee), with one log line on the rig.
    /// </summary>
    private static void SmokeArms(Game? game, double now, double t)
    {
        if (game?.Local is not { Alive: true })
        {
            _smokeArms = 7;
            return;
        }
        var reload = game.Active.Def.Reload;
        switch (_smokeArms)
        {
            case 0:
                _smokeArmsStart = now;
                game.Active.Ammo = Math.Max(0, game.Active.Ammo - 1);
                game.StartReload(now);
                _smokeArmsAt = now;
                _smokeArms++;
                break;
            case 1 when now - _smokeArmsAt > reload * 0.45:
                SmokeShot("arms_reload");
                Console.WriteLine($"[smoke] arms: {game.Active.Def.Name} reload style {_vmAnim.Anchors.Style}, hands reached {_vmAnim.Right.Reached}/{_vmAnim.Left.Reached}, carrying {_vmAnim.Carrying}");
                _smokeArms++;
                break;
            case 2 when now - _smokeArmsAt > reload + 0.3:
                _smokeButtons = Buttons.Forward | Buttons.Sprint;
                _smokeArmsAt = now;
                _smokeArms++;
                break;
            case 3 when now - _smokeArmsAt > 0.9:
                SmokeShot("arms_sprint");
                Console.WriteLine($"[smoke] arms: sprinting at {new Vector2(game.Local.Sim.Velocity.X, game.Local.Sim.Velocity.Z).Length():0.0} m/s; "
                    + $"[head bob] dip {_headBob.Amplitude * 100:0.0} cm, sway {_headBob.Offset.X * 100:0.0} cm, roll {_headBob.Roll * 180 / MathF.PI:0.00} deg, "
                    + $"{_headBob.Cadence:0.0} strides/s, weight {_headBob.Weight:0.00}, setting {_settings.HeadBob}%");
                _smokeArms++;
                break;
            case 4 when now - _smokeArmsAt > 1.0:
                _smokeButtons = Buttons.None;
                game.TryMelee(now);
                _smokeArmsAt = now;
                _smokeArms++;
                break;
            case 5 when now - _smokeArmsAt > 0.12:
                SmokeShot("arms_melee");
                _smokeArms++;
                break;
            case 6 when now - _smokeArmsAt > 0.5:
                _smokeArms++;
                _smokeStepStart += now - _smokeArmsStart; // give the usual steps their clock back
                break;
        }
    }

    private static int _smokeWeaponSelect;
    private static string? _smokeLockedGun;
    private static int _smokeStreakStep, _smokeStreakFrames;
    private static double _smokeStreakStart;

    /// <summary>
    /// Smoke step on the first map, after the first play screenshot: calls in a Sentry Turret, a Mortar Barrage and a
    /// Recon Sweep and screenshots the turret and the shells landing. Keeps its own clock, then hands the usual one back.
    /// </summary>
    private static void SmokeStreaks(double now)
    {
        var t = now - _smokeStreakStart;
        switch (_smokeStreakStep)
        {
            case 0:
                _smokeStreakStart = now;
                _smokeStreakFrames = _smokeFrames;
                Console.WriteLine($"[smoke] kill streaks: {_game.SmokeCallStreaks(now)}");
                Console.WriteLine($"[smoke] kill notice: {_game.SmokeKillNearestBot(now)}"); // KillNotices.cs
                _smokeStreakStep++;
                break;
            case 1 when t > 0.9:
                SmokeShot("streak_turret");
                SmokeShot("killnotice"); // "KILLED <bot>" under the crosshair, and the turret's health bar + HUD readout
                _smokeStreakStep++;
                break;
            case 2 when t > KillStreaks.MortarDelay + 0.45:
                SmokeShot("streak_mortar");
                _smokeStreakStep++;
                break;
            case 3 when t > KillStreaks.MortarDelay + 0.7:
                Console.WriteLine($"[smoke] kill streaks: {(_smokeFrames - _smokeStreakFrames) / Math.Max(t, 0.001):0} fps avg, entities {_game.StreakEntities.Count}, "
                    + $"strikes left {_game.Strikes.Count}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeFrames = _smokeStreakFrames;
                _smokeStreakStep++;
                break;
        }
    }

    /// <summary>
    /// Smoke step on the first map: the weapon-select screen ("Choose primary"), screenshotted as is and
    /// with a locked gun selected (previewed, EQUIP greyed out), then the progression page. Then the usual steps start (their clock restarts).
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
                Ui.SmokeTab(7); // the progression page (opened from the footer's level bar)
                _smokeWeaponSelect++;
                break;
            case 5 when t > 3.3:
                SmokeShot("loadout_progression");
                _smokeWeaponSelect++;
                break;
            case 6 when t > 3.5:
                Ui.SmokeTab(0); // back to the overview, then the usual steps
                _smokeWeaponSelect++;
                _smokeStepStart = now;
                _smokeFrames = 0;
                _smokeWorstDt = 0;
                break;
        }
    }

    private static int _smokeAdsStep;
    private static bool _smokeAimHeld;
    private static WeaponDef? _smokeAdsKeep;
    private static readonly (OpticKind Kind, string Shot)[] SmokeAdsOptics =
        [(OpticKind.RedDot, "ads_reddot"), (OpticKind.Hybrid3, "ads_3x"), (OpticKind.Scope4, "ads_4x"), (OpticKind.Scope8, "ads_8x"), (OpticKind.Scope12, "ads_12x")];

    /// <summary>
    /// Smoke step on the first map: a sniper rifle with a red dot, then the 3x hybrid, the 4x, 8x and 12x scopes,
    /// each held at full ADS and screenshotted (smoke_ads_reddot/ads_3x/ads_4x/ads_8x/ads_12x.bmp) with the lens's
    /// screen coverage logged. Runs inside the usual play window, then puts the real weapon back.
    /// </summary>
    private static void SmokeAds(double t)
    {
        var game = _session;
        if (game == null || _smokeAdsStep > SmokeAdsOptics.Length * 2)
            return;
        var i = _smokeAdsStep / 2;
        var at = 6.3 + i * 0.7;
        if (_smokeAdsStep % 2 == 0)
        {
            if (t < at)
                return;
            _smokeAdsStep++;
            if (i == SmokeAdsOptics.Length)
            {
                _smokeAimHeld = false;
                if (_smokeAdsKeep != null)
                    game.Active.Def = _smokeAdsKeep;
                return;
            }
            var gun = Weapons.Find("Needle Bolt-Action") ?? Weapons.All.First(w => w.Class == WeaponClass.Sniper);
            var def = Attachments.For(gun).Where(a => a.Slot == AttachSlot.Optic)
                .Select(a => Attachments.Apply(gun, Attachments.With(0, AttachSlot.Optic, a.Index)))
                .FirstOrDefault(d => d.Optic == SmokeAdsOptics[i].Kind);
            if (def == null)
            {
                Console.WriteLine($"[smoke] {SmokeAdsOptics[i].Shot}: no {SmokeAdsOptics[i].Kind} optic fits {gun.Name}");
                _smokeAdsStep++;
                return;
            }
            _smokeAdsKeep ??= game.Active.Def;
            game.Active.Def = def;
            game.AimT = 0;
            _smokeAimHeld = true;
        }
        else if (t >= at + 0.55)
        {
            _smokeAdsStep++;
            var d = game.Active.Def;
            SmokeShot(SmokeAdsOptics[i].Shot);
            Console.WriteLine($"[smoke] {SmokeAdsOptics[i].Shot}: {d.Name} [{Attachments.Describe(d.Kit)}] optic {d.Optic} {d.AdsZoom:0.#}x, aim {game.AimT:0.00}, "
                + $"lens covers {SightLens.LastCoverage * 100:0.0}% of the screen (radius {SightLens.LastView.HalfH:0.00} of half the height, {Sights.EyeDistance(d) * 100:0.0} cm from the eye; "
                + $"design {Sights.Coverage(Sights.ProjectAimed(d, 16f / 9f), 16f / 9f) * 100:0.0}% at 16:9), see-through {SightLens.SeeThrough}, reticle {Sights.Spec(d).Reticle}, "
                + $"alive {game.Local?.Alive}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
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
        SkyRenderer.UseDefaultLight(); // guns are lit the same whatever the map's sky
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

    /// <summary>The game runs without a console window, so console output goes to callofslop.log (serverlist.log for --master, which can run beside the game).</summary>
    private static void SetUpLogFile(string[] args)
    {
        try
        {
            var name = args.Any(a => a.Equals("--master", StringComparison.OrdinalIgnoreCase)) ? "serverlist.log" : "callofslop.log";
            var writer = new StreamWriter(AppFolders.LogPath(name), append: false) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }
        catch
        {
            // No log; not fatal.
        }
    }

    private static int _cliStartRound;
    private static bool _cliTestKit;

    /// <summary>"--zombies-round 30" or "--zombies-round=30": the round, and the separate value argument (so it isn't read as a port).</summary>
    private static (int Round, string? Value) ZombiesRoundFlag(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--zombies-round=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--zombies-round=".Length..], out var r))
                return (Math.Clamp(r, 1, Game.MaxStartRound), null);
            if (a.Equals("--zombies-round", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out r))
                return (Math.Clamp(r, 1, Game.MaxStartRound), args[i + 1]);
        }
        return (0, null);
    }

    private static int FlagValue(string[] args, string name)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        return arg != null && int.TryParse(arg[(name.Length + 1)..], out var v) ? Math.Max(0, v) : 0;
    }

    private static nint GetGlProc(byte* name) => Glfw.PInvoke.GetProcAddress(name);

    private static void CreateWindow()
    {
        Glfw.ErrorCallback += SafeClipboard.LogGlfwError; // rate-limited (an image on the clipboard raises one per paste)
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

    private static void StartSession(LobbyAction kind, Invite? invite = null)
    {
        EndSession("");
        _lobbyMessage = "";
        if (_smoke)
            LoadoutStore.ReadOnly = AppearanceStore.ReadOnly = true;
        var loadout = LoadoutStore.Active(_smoke ? SmokeProgress() : _progress);
        try
        {
            NetPeer net;
            var bots = 0;
            switch (kind)
            {
                case LobbyAction.SinglePlayer:
                    net = NetPeer.Offline();
                    bots = _rangeLaunch != null ? 0 : _settings.Bots; // the Workbench Range is just you
                    break;
                case LobbyAction.Host:
                    net = NetPeer.Host(_settings.Port, Game.MaxPlayers - 1);
                    bots = _settings.HostBots;
                    BeginHostAddressing(); // the address line, UPnP, STUN and the invite code (Program.Listing.cs)
                    break;
                default:
                    net = NetPeer.Join(_settings.JoinAddress, _settings.Port);
                    LearnListFromHost(_settings.JoinAddress, _settings.Port, invite); // the host's server list, for next time (Program.Listing.cs)
                    break;
            }

            if (_lagMs > 0 || _lossPct > 0)
                Console.WriteLine(net.SimulateBadNetwork(_lagMs, _lagMs / 5, _lossPct)
                    ? $"[test] simulating {_lagMs} ms latency and {_lossPct}% loss"
                    : "[test] network simulator not available in this RakNet build");

            var map = RangeMap ?? (kind == LobbyAction.Join ? Maps.All[0] : _settings.PickMap(bots + 1)); // joiners get the host's map; random picks a map that fits everyone
            _session = new Game(net, AudioSystem, _settings.Name, loadout.Primary, loadout.Secondary, bots, _settings.Difficulty, map.Id,
                loadout.Grenade, loadout.Equipment, loadout.PrimaryKit, loadout.SecondaryKit, loadout.Perks)
                {
                    Progress = _smoke ? SmokeProgress() : _progress, // the smoke test must never touch your real save
                    ChosenHat = AppearanceStore.EffectiveHat(_smoke ? SmokeProgress() : _progress), // hat and look: the APPEARANCE screen (GameCosmeticLoadout.cs)
                    ChosenLook = AppearanceStore.Look,
                };
            _session.WarmupEnabled = true; // ready-up before every match (single player: Ready or Deploy starts it)
            _session.AutoStartSeconds = kind == LobbyAction.Host ? _settings.AutoStart : 0;
            _session.ScoreLimitMode = _settings.ScoreLimit; // Auto or Fixed (ScoreLimits.cs); clients get the limit itself in the mode state
            _session.TeamSelection = _settings.TeamSelect; // team rules (TeamPick.cs); clients learn the host's from the mode state
            _session.MaxTeamDiff = _settings.TeamMaxDiff;
            ConfigureLobbyRoom(_session, kind); // the pre-match lobby room and team balance (Program.LobbyRoom.cs)
            _session.BotGearMode = _settings.BotGear; // host setting: bots match the players' level (BotGear.cs)
            _session.FrenzyBuffsEnabled = _settings.FrenzyBuffs; // host setting (FrenzyServer.cs); clients learn it from the snapshot
            _session.HostHordeCap = _settings.ZombieHordeCap; // host setting (ZombieHorde.cs): the server's cap; clients don't need it
            if (kind != LobbyAction.Join)
            {
                _session.ZStartRound = _cliStartRound > 0 ? _cliStartRound : _settings.ZombieStartRound; // host settings (ZombiesStartRound.cs); clients learn them from the lobby state
                _session.ZTestKit = _cliTestKit || _settings.ZombieTestKit;
            }
            if (kind != LobbyAction.Join)
                _session.GravityPercent = _settings.Gravity; // host setting (GameGravity.cs); clients learn it from the Welcome and the lobby state
            _session.DestructionEnabled = _settings.Destruction; // host setting (GameDestruction.cs): the server decides what breaks
            ApplyVoiceHostSettings(_session, kind == LobbyAction.Join); // "Allow voice chat", dead chat (Program.Voice.cs)
            _session.Mode = GameModes.Fits(_settings.Mode, map) ? _settings.Mode : GameMode.FreeForAll; // clients learn the host's mode from the server; Zombies needs a Zombies map
            if (kind != LobbyAction.Join)
                Console.WriteLine($"[session] {kind}: {GameModes.Name(_session.Mode)} on {map.Name} with {bots} bots{(_session.Mode != _settings.Mode ? $" ({GameModes.Name(_settings.Mode)} doesn't play on this map)" : "")}");
            _session.TrueRandom = kind != LobbyAction.Join && _settings.MapRandom && MapPack.IsTrueRandom(_settings.MapPack); // the map vote draws from every pack
            World.PhysicsMode = kind == LobbyAction.Join ? PhysicsMode.Classic : _settings.Physics; // joiners: the host's, from the Welcome
            if (kind == LobbyAction.Host)
            {
                _session.ServerName = _settings.ServerNameOrDefault;
                _session.GamePort = _settings.Port;
                StartHostListing(_session); // register on a server list, or run one (Program.Listing.cs)
            }
            if (_rangeLaunch != null)
                ConfigureRange(_session); // Program.Workbench.cs: built weapons, no loadout screen
            Game.SettleMemory(); // a loading moment: clean up now rather than in a tick mid-match
            if (!_session.SkipsLoadout && !_session.LobbyEnabled)
                Ui.OpenMenu(_session); // pick a loadout before the first spawn (Gun Game: none, you spawn straight in)
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
        StopHostListing(); // Program.Listing.cs
        if (_upnpTask is { IsCompleted: false })
            _upnpTask.Wait(TimeSpan.FromSeconds(4));
        _upnpTask = null;
        _upnp?.Dispose();
        _upnp = null;
        Ui.MenuOpen = false;
        Ui.PauseOpen = false;
        _deathCam.Stop();
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
                    if (TrackLobbyRoom(now)) // the lobby room: no player to steer (Program.LobbyRoom.cs)
                        UpdateLobbyRoom(now);
                    else
                        UpdateLocalPlayer(now);
                    var paused = game.Net.IsOffline && Ui.PauseOpen;
                    if (!paused)
                        game.Update(now, FrameDt);
                }
            }
            UpdateVoice(now); // voice chat: the mic in, voices out (Program.Voice.cs)
            TickPartsWatch(now); // part hot reload: --watch-parts (Program.PartsReload.cs)
            UpdateListing(now); // server lists and the invite code (Program.Listing.cs)

            // Menu music outside a session, lobby music around matches, silence while one is live (Music.cs).
            AudioSystem.Music.Volume = _settings.MusicVolume;
            AudioSystem.Music.HardbassOnly = _settings.MusicChoice == MusicChoice.HardbassOnly;
            AudioSystem.Music.SetMood(MusicPlayer.MoodFor(_session != null, _session?.Phase ?? MatchPhase.Playing));
            UpdateFrenzy(now); // the kill-frenzy music during a live match (Program.Frenzy.cs)
            UpdateFrenzyFx(now); // per-class frenzy visuals (Program.FrenzyFx.cs)
            UpdateFrenzyGunSound(); // per-class frenzy gun sound (Program.FrenzyGunSound.cs)

            if (_session == null)
                MapThumbs.Pump(Gfx, now); // map select pictures: at most one per frame, before the scene (Source/Render/MapThumbnails.cs)
            if (Ui.WorkbenchOpen)
                PartThumbs.Pump(Gfx); // workbench part pictures: a few per frame, cached (Source/Render/PartThumbnails.cs)
            if (_session != null && InLobbyRoom)
                RenderLobbyRoom(now); // the lobby's map behind the menu: its assets are drawn (loaded) before the match
            else if (_session != null)
                Render(now);
            else if (_smokeGallery is 1 or 2)
                DrawGunGallery(_smokeGallery);
            else if (FoliageSmokeActive)
                DrawFoliageSmoke();
            else if (MoversSmokeActive)
                DrawMoversSmoke();
            else if (PropsSmokeActive)
                DrawPropsSmoke();
            else if (MeshSmokeActive)
                DrawMeshSmoke();
            else if (LegsSmokeActive)
                DrawLegsSmoke();
            else if (SkySmokeActive)
                DrawSkySmoke();
            else if (CaveSmokeActive)
                DrawCaveSmoke();
            else
                RenderLobbyBackground(now);
            if (Ui.Preview is { } showcase)
                DrawGunsmithPreview(showcase, now);
            if (Ui.AppearanceOpen && _session == null && Ui.LookPreview is { } lookPreview)
                DrawAppearancePreview(lookPreview, now); // the APPEARANCE screen's turning model (Program.Appearance.cs)

            Ui.BeginFrame();
            if (_settings.ShowFps)
                FpsCounter.Draw(now);
            if (_session is { } lobbyGame && InLobbyRoom)
            {
                DrawLobbyRoomUi(lobbyGame, now); // Program.LobbyRoom.cs
            }
            else if (_session is { } g)
            {
                // While the weapon workbench is open over the match the player HUD is not drawn at all (the match keeps running).
                var benchCovers = Ui.WorkbenchOpen;
                if (!benchCovers)
                {
                    DrawNameTags(g); // under the HUD windows (Program.NameTags.cs)
                    TacticalHud.DrawMarkers(g, now, Ui.Scale); // motion pings, decoy tags, your devices (TacticalHud.cs)
                    LauncherHud.DrawMarkers(g, now, Ui.Scale); // the lock-on brackets, the wire's guide dot, sticky charges (LauncherHud.cs)
                    DrawFriendlyHitText(g, now); // "FRIENDLY" under the friendly hit marker (Program.FriendlyHit.cs)
                    Ui.DrawHud(g, now, _joinAddress, _settings);
                }
                DrawBenchInMatch(now); // a Zombies workbench, open over the match (Program.Zombies.cs)
                if (Ui.DrawLoadoutMenu(g, now))
                {
                    SetMouseCaptured(true);
                    _swallowClick = true; // the Deploy click mustn't also fire the gun
                }
                if (!benchCovers)
                {
                    Ui.DrawKillCard(g, _deathCam, now, _settings); // after the loadout screen, so it can sit beside it
                    DrawChatUi(g, now); // the HUD chat and the mic indicator (Program.Voice.cs); chat can't open with the bench up
                }
                switch (Ui.DrawPauseMenu(g, _settings))
                {
                    case PauseAction.Resume:
                        SetMouseCaptured(true);
                        _swallowClick = true;
                        break;
                    case PauseAction.Loadout:
                        if (g.SkipsLoadout)
                            g.LoadoutNotice = GunGameLoadoutText;
                        else
                            Ui.OpenMenu(g);
                        break;
                    case PauseAction.Suicide:
                        g.RequestSuicide(); // normal death flow follows (death cam, then loadout or Gun Game respawn)
                        SetMouseCaptured(true);
                        _swallowClick = true;
                        break;
                    case PauseAction.Leave:
                        EndSession("");
                        break;
                    case PauseAction.BackToLobby:
                        g.ReturnToLobbyNow(); // the lobby room, everyone still connected (TrackLobbyRoom frees the mouse)
                        break;
                    case PauseAction.Quit:
                        _quit = true;
                        break;
                }
            }
            else if (_smokeGallery is not (1 or 2) && !FoliageSmokeActive && !MoversSmokeActive && !PropsSmokeActive && !MeshSmokeActive && !LegsSmokeActive && !SkySmokeActive && !CaveSmokeActive) // no lobby menu over the smoke-test gun gallery or foliage views
            {
                if (Ui.LobbyLoadouts)
                {
                    Ui.DrawLobbyLoadouts(AudioSystem, now);
                }
                else if (Ui.AppearanceOpen)
                {
                    Ui.DrawAppearance(AudioSystem, now, _settings.Name); // main menu > Appearance (UiAppearance.cs)
                }
                else if (DrawWorkbenchLobby(now))
                {
                    // the weapon workbench (Zombies preview) is up instead of the main menu (Program.Workbench.cs)
                }
                else
                {
                    if (SavePaths.TakeWarningOnce() is { } saveWarning)
                        _lobbyMessage = saveWarning; // a save from a newer build that this one won't overwrite (shown once)
                    var action = Ui.DrawLobby(_settings, _lobbyMessage, AudioSystem);
                    if (action == LobbyAction.Quit || Updater.Shared?.QuitRequested == true) // the installed update has started (UiUpdates.cs)
                        _quit = true;
                    else if (action != LobbyAction.None)
                        StartSession(action);
                }
            }
            Ui.DrawAudioMixer(AudioSystem, now, _session?.Local?.WeaponDef ?? Weapons.All[0], _settings.Bind(GameAction.AudioMixer)); // over everything; closed it only turns the meters off (Program.AudioMixer.cs)
            Ui.DrawWeaponTweaks(_session, now, _session?.Local?.WeaponDef ?? Weapons.All[0], _settings.Bind(GameAction.WeaponTweaks)); // dev window over everything; closed one bool check (Program.WeaponTweaks.cs)
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

    /// <summary>
    /// Shows the BRUTAL logo splash (see <see cref="Splash"/>) in its own little loop before the lobby or any session.
    /// The smoke test watches it in full (no skipping) and screenshots it once (smoke_splash.bmp).
    /// </summary>
    private static void RunSplash()
    {
        var shot = false;
        while (!_window.ShouldClose && !_quit)
        {
            Glfw.PollEvents();
            var now = Glfw.Time;
            if (!Splash.Active(now))
                break;
            var fb = _window.FramebufferSize;
            if (fb.X > 0 && fb.Y > 0)
                Gfx.BeginFrame(fb.X, fb.Y, new Vector3(0.04f, 0.04f, 0.05f));
            Ui.BeginFrame();
            Splash.Draw(now, allowSkip: !_smoke);
            Ui.EndFrame();
            if (_smoke && !shot && Splash.Elapsed(now) > 1.2)
            {
                SaveScreenshot("splash");
                shot = true;
                Console.WriteLine($"[smoke] splash: BRUTAL logo shown, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
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
        // Preview the map currently picked in the lobby (or selected on the map select screen while it's up).
        // (with "random from pack" picked, the background tours the pack's maps, a different one every 12 seconds; True Random: every pack's)
        var pick = Ui.MapSelectPreview;
        var tour = MapPack.RandomPool(pick?.Pack ?? _settings.MapPack, Maps.Packs, Maps.All);
        var random = pick != null ? pick.Value.Map == null : _settings.MapRandom;
        var preview = random ? tour[(int)(now / 12) % tour.Count] : Maps.Find(pick?.Map ?? _settings.MapName);
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
        SkyRenderer.Use(map); // this map's sun, ambient and sky-coloured fog in every scene shader (no-op if unchanged)
        // Buildings (walls, floors, roofs, doors, glass) and the map's own boxes, instanced and culled by cell (BuildingRenderer.cs,
        // Program.Buildings.cs). Props are drawn as models by the prop renderer, a shaped map's floor as a mesh.
        if (!BuildingGfx.Draw(map, Gfx, Glfw.Time))
            for (var i = 0; i < map.Solid.Count; i++)
                if (map.IsDrawnBox(i)) // (no instancing: the boxes one by one, as before)
                    Gfx.DrawBox(map.Solid[i], map.Colors[i], mode: 1, material: map.Materials[i]);
        DrawMovers(map); // moving platforms at this frame's pose (Program.Movers.cs)
        TerrainGfx.Draw(Gfx, map); // terrain maps only (built once per map)
        PropGfx.Draw(map, Gfx, Glfw.Time); // props and debris, instanced (Program.Props.cs)
        MeshGfx.Draw(map, Gfx, Glfw.Time); // Mesh and Model ops: primitives and glTF models, instanced (Program.Meshes.cs)
        FoliageGfx.Draw(map, Gfx, _settings.FoliageQuality, Glfw.Time); // after the opaque boxes; see Program.Foliage.cs
        DecalGfx.Draw(map, Gfx, _settings.ImpactDecals, Glfw.Time); // map decals and bullet impacts over the opaque world (Program.Decals.cs)
        SkyRenderer.Draw(Gfx, map, _settings.SkyQuality, Glfw.Time); // last opaque pass: only where nothing was drawn (Source/Render/Sky)
    }

    // =====================================================================
    // Input
    // =====================================================================

    private static bool MenusOpen => Ui.MenuOpen || Ui.PauseOpen || Ui.BenchRun != null || InLobbyRoom || Ui.AudioMixerOpen || Ui.WeaponTweaksOpen; // (a Zombies workbench too; the lobby room; the Audio mixer and weapon tweak windows)

    /// <summary>The results / map vote screen is up: the cursor stays free there (CursorPolicy).</summary>
    private static bool ResultsScreen => _session is { Phase: MatchPhase.Ended } && !InLobbyRoom;

    /// <summary>This frame's inputs to the one mouse capture rule (CursorPolicy.cs).</summary>
    private static CursorState CursorNow() => new(_session != null, MenusOpen, ResultsScreen, _window != null && _window.GetAttribute(GlfwWindowAttribute.Focused) != 0);

    private static void SetMouseCaptured(bool captured)
    {
        if (captured && !CursorPolicy.MayCapture(CursorNow()))
            captured = false; // a menu or the results screen is up: keep the cursor free
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

        // Main menu > Loadouts: Esc is the screen's Back (one page back, then out to the menu).
        if ((_session == null || InLobbyRoom) && key == GlfwKey.Escape && Ui.LobbyLoadouts)
        {
            Ui.LoadoutBack();
            return;
        }
        if (_session == null && key == GlfwKey.Escape && Ui.AppearanceOpen)
        {
            Ui.CloseAppearance(); // main menu > Appearance: Esc is its Back (UiAppearance.cs)
            return;
        }

        if (key == GlfwKey.Escape && Ui.ChatOpen)
        {
            Ui.CloseChat(); // Esc leaves the chat line, not the match
            return;
        }

        if (AudioMixerKey(key)) // F8 (rebindable) or Esc: the Audio mixer window (Program.AudioMixer.cs)
            return;
        if (WeaponTweaksKey(key)) // F7 (rebindable) or Esc: the weapon tweak window (Program.WeaponTweaks.cs)
            return;

        if (_session == null || key != GlfwKey.Escape)
            return; // everything else is polled through the keybinds in UpdateLocalPlayer

        if (_session.Local == null)
        {
            EndSession("Cancelled");
            return;
        }
        if (InLobbyRoom)
            return; // the lobby room has its own LEAVE button
        if (Ui.MenuOpen)
        {
            // Same as the loadout screen's Back: one page back, and from the overview it closes.
            if (Ui.LoadoutBack())
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
            _game.SwitchTo(_game.NextSlot, Glfw.Time); // 2 slots, or up to 4 with the Zombies slot upgrade
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

        if (CursorPolicy.ClickCaptures(CursorNow(), _mouseCaptured, Ui.WantsMouse))
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

    /// <summary>When the interact key went down at a cart where holding it means something else (-1: not waiting).</summary>
    private static double _interactDownAt = -1;

    private static void PollActions()
    {
        Array.Copy(_down, _prev, _down.Length);
        var blocked = Ui.WantsTextInput || Ui.Rebinding != null || Ui.ChatOpen; // typing in the chat suspends the game controls
        for (var i = 0; i < _down.Length; i++)
            _down[i] = !blocked && Keybinds.IsDown(_window, _settings.Binds[i]);
    }

    private static void UpdateLocalPlayer(double now)
    {
        PollActions();
        ChatKeys(); // Chat (all) / Chat (team) open the HUD chat line (Program.Voice.cs)

        var cursor = _window.CursorPos;
        var delta = new double2(cursor.X - _lastCursor.X, cursor.Y - _lastCursor.Y);
        _lastCursor = cursor;
        if (_skipNextMouseDelta)
        {
            delta = new double2(0, 0);
            _skipNextMouseDelta = false;
        }

        // Match countdown: open the loadout menu while everyone loads in, deploy it at GO.
        if (_game.TakeCountdownStarted() && !Ui.MenuOpen && !Ui.PauseOpen && !_game.SkipsLoadout)
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

        // Results screen: free the mouse so players can click a map in the vote; 1/2/3 also vote. The cursor stays
        // free until the next match starts (CursorPolicy refuses every capture meanwhile), then comes back by itself.
        var resultsNow = ResultsScreen;
        if (resultsNow && !_wasResults)
        {
            Ui.MenuOpen = false;
            SetMouseCaptured(false);
        }
        else if (CursorPolicy.RecaptureAfterResults(_wasResults, CursorNow(), _mouseCaptured))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
        _wasResults = resultsNow;
        var votingNow = _game.VoteOpen;
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

        // Death screen: the death cam turns to your killer, then the loadout menu opens with a Respawn button.
        // Nobody is respawned automatically (and not over the results screen). A click skips the death cam.
        var deadNow = _game.Local is not { Alive: true };
        if (deadNow && !_wasDeadUi)
        {
            _deathMenuAt = now + (_game.Local != null ? DeathCam.Duration : 1.2);
            if (_game.SkipsLoadout && _game.Local is not { Deaths: > 0 })
                _deathMenuAt = now; // Gun Game, just joined: no death to show, straight in
            if (_game.Local is { } me && !GravityFrames.Curved) // (a planet: the plain view from above, below)
                _deathCam.Begin(me.RenderEye, _game.LookYaw, _game.LookPitch, now, (o, d, max) => World.Query.Raycast(o, d, max, QueryFilter.All));
        }
        if (!deadNow)
        {
            _deathMenuAt = 0;
            if (_deathCam.Active)
            {
                _deathCam.Stop();
                _game.LastDeath = null;
            }
        }
        _wasDeadUi = deadNow;
        if (_deathMenuAt > now && _deathCam.Running(now) && _mouseCaptured && !_swallowClick && Pressed(GameAction.Fire))
        {
            _deathMenuAt = now;
            _swallowClick = true;
        }
        if (_deathMenuAt > 0 && now >= _deathMenuAt)
        {
            _deathMenuAt = 0;
            if (_game.SkipsLoadout)
            {
                // Gun Game: no loadout screen, the death cam goes straight to a respawn.
            }
            else if (!Ui.MenuOpen && !Ui.PauseOpen && !_game.RespawnRequested && _game.Phase != MatchPhase.Ended)
            {
                Ui.OpenMenu(_game);
                SetMouseCaptured(false);
            }
        }
        if (_game.SkipsLoadout)
        {
            // Gun Game: never a loadout screen (a client learns the mode after joining, so close one already open),
            // and once the death cam is over you respawn on your own.
            if (Ui.MenuOpen)
            {
                Ui.MenuOpen = false;
                if (!Ui.PauseOpen)
                    SetMouseCaptured(true);
            }
            if (deadNow && _deathMenuAt == 0 && _game.Local != null && !_game.RespawnRequested && _game.Phase != MatchPhase.Ended)
                _game.RequestRespawn();
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
            else if (_game.SkipsLoadout)
            {
                _game.LoadoutNotice = _game.LoadoutNotice == GunGameLoadoutText ? GunGameLoadoutText + " " : GunGameLoadoutText;
            }
            else if (_game.CanChangeLoadout)
            {
                Ui.OpenMenu(_game);
                SetMouseCaptured(false);
                _deathMenuAt = 0; // opened early over the death cam: don't reopen it later
            }
            else
            {
                // Loadouts only change between lives.
                _game.LoadoutNotice = _game.LoadoutNotice == LoadoutLockedText ? LoadoutLockedText + " " : LoadoutLockedText;
            }
        }
        Ui.ShowScoreboard = _mouseCaptured && Down(GameAction.Scoreboard);
        if (Pressed(GameAction.Ready) && !Ui.PauseOpen)
            _game.ToggleReady();

        // Mouse look is applied every frame (never waits for a tick or the network). Not while typing in the chat.
        if (_mouseCaptured && !Ui.ChatOpen)
        {
            // Lower sensitivity while zoomed so aiming feels the same at any FOV.
            var zoom = float.Lerp(1f, _game.Active.Def.AdsZoom, _game.AimT);
            var sens = BaseSensitivity * _settings.Sensitivity / zoom * _game.TacticalLookMult; // a stun slows turning (GameTactical.cs)
            var dy = (float)delta.Y * (_settings.InvertY ? -1 : 1);
            _game.LookYaw = World.WrapAngle(_game.LookYaw + (float)delta.X * sens);
            _game.LookPitch = Math.Clamp(_game.LookPitch - dy * sens, -1.55f, 1.55f);
        }

        if (_game.Local is { Alive: false })
            _crouchToggle = _proneToggle = false; // respawn standing

        ZombiesInput(now, _mouseCaptured && Pressed(GameAction.Interact)); // Zombies: the interact key's hold, workbenches (Program.Zombies.cs)
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
                _game.SwitchTo(_game.NextSlot, now);
            if (Pressed(GameAction.Weapon3))
                _game.SwitchTo(2, now); // Zombies slot upgrade (refused while you have 2 slots)
            if (Pressed(GameAction.Weapon4))
                _game.SwitchTo(3, now);
            if (Pressed(GameAction.Interact))
            {
                if (_game.InteractCanHold)
                    _interactDownAt = now; // at a parked or manned cart: tap = man it / step off, hold = push it
                else
                    _game.Interact(now); // push cart and slot station (GameZombies.cs); nothing in normal matches
            }
            if (_interactDownAt >= 0 && (!Down(GameAction.Interact) || now - _interactDownAt >= Game.CartHoldSeconds))
            {
                _game.Interact(now, hold: Down(GameAction.Interact));
                _interactDownAt = -1;
            }
            if (Pressed(GameAction.Grenade))
                _game.TryThrow(equipment: false, now);
            if (Pressed(GameAction.Equipment))
                _game.TryThrow(equipment: true, now);
            if (Pressed(GameAction.Melee))
                _game.TryMelee(now);
            for (var i = 0; i < 3; i++)
                if (Pressed(GameAction.Streak1 + i))
                    _game.StreakKey(i, now); // kill streaks (cheapest first)

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
        _game.HeldButtons = buttons | _smokeButtons; // (_smokeButtons: the smoke test's arms step only)
        _game.AimHeld = (_mouseCaptured && Down(GameAction.Aim)) || _smokeAimHeld;

        // Trigger: semi-auto weapons need the press edge, autos use the held state.
        var down = _mouseCaptured && Down(GameAction.Fire);
        if (_swallowClick)
        {
            if (!down)
                _swallowClick = false;
            down = false;
        }
        var pressed = down && Pressed(GameAction.Fire);
        _game.UpdateTrigger(now, down || _smokeFireHeld, pressed); // (_smokeFireHeld: the workbench smoke step's minigun)
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
            if (local.Alive != _camUpAlive || !ReferenceEquals(World.Map, _camUpMap))
            {
                _camUp = Vector3.Zero; // a spawn or a new map: the camera takes the local up at once, no roll from the old spot
                _camUpAlive = local.Alive;
                _camUpMap = World.Map;
            }
            eye = local.Alive ? _game.MannedEye ?? local.RenderEye : GravityFrames.Above(local.RenderPosition, 4f); // (manning a cart: behind its gun)
            fwd = _game.LookDir(_game.LookYaw, local.Alive ? _game.LookPitch : -1.2f);
            if (!local.Alive && ZombiesSpectateCamera(out var spEye, out var spFwd))
            {
                eye = spEye; // Zombies, bled out: watching a living teammate (Program.Zombies.cs)
                fwd = spFwd;
            }
            else if (!local.Alive && _deathCam.Active)
            {
                _deathCam.Track(_game, now, (float)FrameDt); // eases round to face your killer (DeathCam.cs)
                eye = _deathCam.Eye;
                fwd = _deathCam.Forward;
            }
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
        if (local is { Alive: true })
        {
            // Head bob (Source/Render/HeadBob.cs): moves only the rendered eye; aim and bullets keep the unbobbed eye.
            _headBob.Update(new HeadBobInput
            {
                Dt = (float)FrameDt, Velocity = GravityFrames.ToLocal(local.RenderPosition, local.RenderVelocity), Grounded = local.RenderGrounded, Mantling = local.RenderMantling, Stance = local.RenderStance,
                Aim = _game.AimT, Strength = _settings.HeadBob / 100f, GaitPhase = LegAnimator.GaitPhase(local.Id, Glfw.Time), // last frame's first-person legs, if drawn
            });
            eye = GravityFrames.Curved ? _headBob.Apply(eye, _game.LookFrame, _game.LookYaw) : _headBob.Apply(eye, _game.LookYaw);
            eye -= GravityFrames.UpAt(eye) * _game.KnockDrop; // stumbling from a blast push (Knockback.cs)
        }
        else
            _headBob.Reset();

        var fov = local is { Alive: true } ? CurrentFov() : BaseFov * (_deathCam.Active ? _deathCam.FovMul : 1f);
        var camUp = GravityFrames.SmoothUp(ref _camUp, eye, (float)FrameDt); // straight up, or on a planet away from its centre (the horizon rolls with the ground; eased across a gravity zone's edge)
        if (local is { Alive: true })
        {
            // Recoil shake and view punch move only the rendered camera, never the aim.
            var rk = _game.RecoilCamera;
            var kk = _game.KnockCamera; // a blast push's view kick (Knockback.cs)
            fwd = _game.LookDir(_game.LookYaw + rk.Yaw + kk.Yaw, Math.Clamp(_game.LookPitch + rk.Pitch + kk.Pitch, -1.56f, 1.56f));
            camUp = Vector3.Transform(camUp, Quaternion.CreateFromAxisAngle(fwd, -(rk.Roll + kk.Roll + _headBob.Roll)));
            fov *= 1f + rk.Fov;
        }
        var view = Matrix4x4.CreateLookAt(eye, eye + fwd, camUp);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, w / h, 0.05f, World.Map.FarPlane);
        Gfx.SetCamera(view, proj);
        Gfx.SetFog(World.Map.Sky, World.Map.FogStart, World.Map.FogEnd);
        UpdateNameTags(now, eye, fwd, view, proj); // HUD world labels and name tags (Program.NameTags.cs)

        // ---- Arena ----------------------------------------------------------
        SightLens.MarkWorldStart(); // the lens pass logs the world pass's cost next to its own
        ScenePresentation.BeginFrame(MeshGfx);
        UpdateRagdolls(now); // new bodies for the newly dead, their 60 Hz steps (Program.Ragdolls.cs); drawn with the props' debris
        DrawArena();
        DrawRagdollsFallback();

        // ---- Players --------------------------------------------------------
        LegAnimator.Viewer = eye; // foot IK rays only for players near the camera (Source/Render/LegAnimator.cs)
        LegAnimator.MoverTick = _game.MoverRenderTick;
        foreach (var p in _game.Players.Values)
        {
            if (p.Id == _game.LocalId && p.Alive)
            {
                if (ScenePresentation.Enabled && ScenePresentation.FirstPersonLegs)
                    ScenePresentation.DrawLocalLegs(Gfx, p, Glfw.Time); // your own legs, seen when looking down
                continue;
            }
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

        StreakRender.Draw(Gfx, _game, now); // turrets, drones, strikes (StreakRender.cs)
        LauncherRender.Draw(Gfx, _game, now); // rockets, launcher grenades, sticky charges (LauncherRender.cs)
        TacticalRender.Draw(Gfx, _game, now); // tactical devices (TacticalRender.cs)
        DrawZombiesWorld(now); // push carts and the slot station (Program.Workbench.cs); nothing in normal matches
        DrawZombiesMode(now); // the Zombies mode: zombies, doors, windows, buyables (Program.Zombies.cs); nothing on other maps
        DrawAmmoFx(now); // special ammo flashes, in any mode (Program.Zombies.cs)
        DrawPveEnemies(now, eye, fwd); // co-op enemy soldiers, with level of detail (Program.Pve.cs); nothing outside Co-op
        _worldView = view;

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
            if (t.Speed > 0)
            {
                // A slow round (Game.TracerSpeed): a fat slug with a short streak behind it travels out to where it hit,
                // then fades like any tracer.
                var flown = age * t.Speed;
                var head = t.From + d * MathF.Min(len, flown);
                var streak = MathF.Min(MathF.Min(len, flown), MathF.Max(0.6f, t.Speed * 0.012f));
                var slugAlpha = flown < len ? 1f : Math.Clamp(1f - (age - len / t.Speed) / 0.25f, 0f, 1f);
                if (slugAlpha <= 0f || streak < 0.01f)
                    continue;
                var sy = MathF.Atan2(d.X, -d.Z);
                var sp = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
                Gfx.Draw(Matrix4x4.CreateScale(t.Width * 1.6f, t.Width * 1.6f, streak) * Matrix4x4.CreateRotationX(sp) * Matrix4x4.CreateRotationY(-sy)
                         * Matrix4x4.CreateTranslation(head - d * (streak * 0.5f)), t.Color, slugAlpha, mode: 2);
                if (flown >= len)
                    Gfx.Draw(Matrix4x4.CreateScale(0.1f * (1 + (age - len / t.Speed) * 4)) * Matrix4x4.CreateTranslation(t.To), new Vector3(1f, 0.9f, 0.6f), slugAlpha, mode: 2);
                continue;
            }
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
        DrawShieldShimmer(now); // Last Stand shields on other players (Program.Health.cs)
        TacticalRender.DrawGlow(Gfx, _game, now); // cover walls, flash and stun bursts, device lights (TacticalRender.cs)
        LauncherRender.DrawGlow(Gfx, _game, now); // rocket flames and smoke trails (LauncherRender.cs)
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
            if (now < cloud.Born)
                continue; // a Smoke Screen canister still on its way down
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
        ObjectiveRender.Draw(Gfx, _game, now); // hill zone, flags, carrier beacons (game modes)
        // Enemies revealed by a Recon Pulse / Recon Sweep show through walls, in their team colour (Source/Render/ReconOverlay.cs).
        Gfx.SetDepthTest(false);
        ReconOverlay.Draw(Gfx, _game, now);
        Gfx.SetDepthTest(true);
        Gfx.SetDepthWrite(true);
        Gfx.SetBlend(false);
        _frenzyFx.DrawWorld(Gfx, _game, now); // frenzy sparks, trails, others' gun glow: one instanced draw (Program.FrenzyFx.cs)

        Gfx.SetFog(World.Map.Sky, 0, 0); // no haze on the viewmodel and HUD

        // ---- First-person gun -----------------------------------------------
        if (local is { Alive: true })
        {
            GunModel.AdsCutaway = SightLens.Cutaway(_game.Active.Def, _game.AimT); // reflex dots hand over to the lens pass as you aim
            DrawViewModel(now, proj);
            GunModel.AdsCutaway = 0;
            DrawFrenzyFxViewModel(now); // your gun's frenzy glow (Program.FrenzyFx.cs)
        }
        _frenzyFx.EndFrame(now);

        // ---- HUD ------------------------------------------------------------
        if (!Ui.WorkbenchOpen)
            DrawHud(now, w, h, local, fov); // hidden while the weapon workbench is open (the crosshair, ammo, prompts, weapon list)
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
        SkyRenderer.UseDefaultLight(); // the showcase gun is lit the same whatever the map's sky

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
        if (pv.Camera is { } benchCam)
        {
            // The workbench: its own orbit camera, and a part sliding into its socket (Source/UI/UiWorkbench.cs).
            benchCam.Matrices(vw / MathF.Max(vh, 1), out var bpose, out var bview, out var bproj);
            Gfx.SetCamera(bview, bproj);
            if (pv.AnimRest != null && pv.AnimPart != null)
            {
                DrawGun(pv.AnimRest, bpose, false);
                DrawGun(pv.AnimPart, Matrix4x4.CreateTranslation(pv.AnimOffset) * bpose, false);
            }
            else
                DrawGun(def, bpose, false);
            Gfx.SetViewport(0, 0, fb.X, fb.Y);
            return;
        }
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
        SkyRenderer.UseDefaultLight(); // gun pictures are lit the same whatever the map's sky
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

    /// <summary>Another player's box body with its gun and arms: through the scene graph (Source/Scene/Rigs/PlayerRig.cs), or the old immediate path.</summary>
    private static void DrawPlayer(Player p)
    {
        if (ScenePresentation.Enabled)
            ScenePresentation.DrawPlayer(Gfx, p, Glfw.Time);
        else
            PlayerRender.DrawImmediate(Gfx, p, Glfw.Time);
    }

    private static readonly ViewModelAnimator _vmAnim = new();
    private static readonly HeadBob _headBob = new();

    // The camera's eased up on gravity-zone maps (GravityFrames.SmoothUp), and what it was last reset for.
    private static Vector3 _camUp;
    private static bool _camUpAlive;
    private static Map? _camUpMap;

    private static void DrawViewModel(double now, Matrix4x4 proj)
    {
        Gfx.ClearDepthBuffer();
        // The viewmodel always uses the un-zoomed FOV so the gun doesn't balloon when aiming.
        var fb = _window.FramebufferSize;
        var vmProj = Sights.ViewModelProjection(fb.X / (float)fb.Y);
        Gfx.SetCamera(Matrix4x4.Identity, vmProj); // camera space: -Z forward, +X right, +Y up

        var def = _game.Active.Def;
        // Arms and gun animation (Source/Render/ViewModelAnimator.cs): hip/ADS, sway, bob, sprint, jump, equip, melee, reloads, mantle, kick.
        var me = _game.Local!;
        if (_game.MeleeProgress(now) >= 0 && me.MeleeBuild is { } mb)
            def = mb.View; // a built melee weapon is swung in place of the gun (Zombies melee slot)
        _vmAnim.Update(new ViewModelInput
        {
            PoseOverride = PushedCartPose(def), // a pushed cart sits on the ground in the world; the hands reach its bar (Program.Workbench.cs)
            Def = def, Now = now, Dt = (float)FrameDt, Aim = _game.AimT, Reload = _game.ReloadProgress(now), Melee = _game.MeleeProgress(now),
            Equip = now < _game.EquipUntil ? Math.Clamp((float)((_game.EquipUntil - now) / def.EquipTime), 0f, 1f) : 0f,
            Kick = _game.WeaponKick, Velocity = me.Sim.Velocity, Grounded = me.Sim.Grounded, MantleTicks = me.Sim.MantleTicks, MantleTo = me.Sim.MantleTo,
            Eye = _game.MannedEye ?? me.RenderEye, Yaw = _game.LookYaw, Pitch = _game.LookPitch, SprintHeld = (_game.HeldButtons & Buttons.Sprint) != 0,
            HeadBob = _headBob.Weight, HeadBobPhase = _headBob.DipPhase,
        });
        var pose = _vmAnim.GunPose;
        if (Game.IsEmptyGun(def))
            return; // an empty slot (taken apart at a workbench): nothing in your hands (the melee swing above still draws)
        // The view through the optic's lens: the world behind it is copied now, the lens is drawn over the gun below.
        var lens = SightLens.Begin(def, _game.AimT, pose, vmProj, fb.X, fb.Y);

        var armColors = ArmColors.For(_game.Local!.Color * 0.5f + new Vector3(0.06f));
        if (Game.IsCart(def) && _game.MannedCart != null)
        {
            _vmAnim.DrawArms(Gfx, armColors); // manning a cart: it's drawn in the world (its gun turns); only your hands on its bar here
            return;
        }
        if (ScenePresentation.Enabled) // camera -> gun -> parts/attachments, camera -> arms (Source/Scene/Rigs/ViewModelRig.cs)
        {
            ScenePresentation.DrawViewModel(Gfx, _vmAnim, def, GunModel.AdsCutaway, now < _game.MuzzleFlashUntil, (float)Glfw.Time, armColors);
            if (lens)
                SightLens.Draw(Gfx);
            return;
        }
        (GunModel.MagOffset, GunModel.MagHidden) = (_vmAnim.MagOffset, _vmAnim.MagHidden); // the magazine comes out during a reload
        DrawGun(def, pose, now < _game.MuzzleFlashUntil);
        (GunModel.MagOffset, GunModel.MagHidden) = (Vector3.Zero, false);
        _vmAnim.DrawArms(Gfx, armColors);
        if (lens)
            SightLens.Draw(Gfx);
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
        DrawBloodOverlay(now, w, h, local); // blood splatters by your health, under the HUD (Program.Blood.cs)

        if (local is { Alive: true })
        {
            // Aimed through an optic, its lens has the reticle (Source/Render/SightLens.cs); otherwise the crosshair.
            if (!SightLens.HidesCrosshair(_game.Active.Def, _game.AimT))
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

            if (now < _game.HitMarkerUntil && _game.HitMarkerFriendly)
                DrawFriendlyHitMarker(cx, cy, k, now); // a teammate: no damage (Program.FriendlyHit.cs)
            else if (now < _game.HitMarkerUntil)
            {
                var c = _game.HitMarkerHead ? new Vector3(1f, 0.85f, 0.2f) : new Vector3(1f, 0.25f, 0.25f);
                for (var i = 0; i < 4; i++)
                {
                    var sx = (i & 1) == 0 ? -1 : 1;
                    var sy = (i & 2) == 0 ? -1 : 1;
                    Gfx.Rect(cx + sx * 14 * k - 3 * k, cy + sy * 14 * k - 3 * k, 6 * k, 6 * k, c);
                    if (_game.HitMarkerWallbang) // through a wall: a second, inner set of ticks (GamePenetration.cs)
                        Gfx.Rect(cx + sx * 7 * k - 1.5f * k, cy + sy * 7 * k - 1.5f * k, 3 * k, 3 * k, c);
                }
            }

            // Health bar (bottom-left of the HUD safe area; the number is drawn by ImGui)
            var (safeLeft, safeWidth) = HudLayout.Span(w, h, _settings.HudWidth);
            var hp = Math.Clamp(local.Health / (float)World.MaxHealth, 0f, 1f);
            float m = 30 * k, bw = 300 * k, bh = 18 * k, inset = 3 * k;
            var left = safeLeft + m;
            Gfx.Rect(left, m, bw, bh, black, 0.5f);
            Gfx.Rect(left + inset, m + inset, (bw - 2 * inset) * hp, bh - 2 * inset, HealthBarColor(local, hp, now), 0.95f);
            DrawHealthExtras(local, left, m, bw, bh, k, now); // regen glow and Last Stand shield pips (Program.Health.cs)

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
        if (now < _game.ShieldFlashUntil) // your Last Stand shield blocked a hit
            Gfx.Rect(0, 0, w, h, new Vector3(0.35f, 0.85f, 1f), Math.Clamp((float)((_game.ShieldFlashUntil - now) / 0.3) * 0.3f, 0f, 0.3f));
        TacticalHud.DrawOverlay(Gfx, _game, now, w, h); // flashbang white-out, stun edges (TacticalHud.cs)
        if (local is { Alive: false })
            Gfx.Rect(0, 0, w, h, new Vector3(0.5f, 0f, 0f), 0.35f);
        if (!_mouseCaptured)
            Gfx.Rect(0, 0, w, h, black, MenusOpen ? 0.55f : 0.35f);

        Gfx.SetBlend(false);
        Gfx.SetDepthTest(true);
    }

}
