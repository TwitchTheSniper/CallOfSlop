namespace CallOfSlop;

/// <summary>
/// The Weapon Workbench (Zombies preview): opening the Workbench Range with two built weapons, coming back to the
/// workbench afterwards, and a small smoke-test step (workbench screenshots, then the range with a spun-up minigun).
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Set while StartSession builds a Workbench Range session (read by its hooks).</summary>
    private static WorkbenchLaunch? _rangeLaunch;

    /// <summary>Reopen the workbench when the range session ends.</summary>
    private static bool _returnToWorkbench;

    /// <summary>Held trigger for the smoke step (ORed into the real trigger).</summary>
    private static bool _smokeFireHeld;

    private const string RangeMapName = "Workbench Range";

    private static void StartWorkbenchRange(WorkbenchLaunch launch)
    {
        if (Maps.Find(RangeMapName) == null)
        {
            _lobbyMessage = "The Workbench Range map is missing (Maps/Dev/WorkbenchRange.json).";
            Ui.WorkbenchOpen = false;
            return;
        }
        Ui.WorkbenchOpen = false;
        _rangeLaunch = launch;
        try
        {
            StartSession(LobbyAction.SinglePlayer);
        }
        finally
        {
            _rangeLaunch = null;
        }
    }

    /// <summary>StartSession hook: the range map while launching from the workbench, else null.</summary>
    private static Map? RangeMap => _rangeLaunch != null ? Maps.Find(RangeMapName) : null;

    /// <summary>StartSession hook: turns a fresh single-player session into the Workbench Range.</summary>
    private static void ConfigureRange(Game g)
    {
        var l = _rangeLaunch!;
        g.BuilderRange = true; // no loadout screen; you respawn on your own
        g.WarmupEnabled = false;
        g.Mode = GameMode.FreeForAll;
        g.UseBuiltWeapons(l.Primary, l.Secondary);
        g.RequestRespawn();
        g.LoadoutNotice = $"Workbench Range: {l.Primary.Name} + {l.Secondary.Name}. Esc > Leave to go back to the workbench.";
        _returnToWorkbench = !_smoke;
        Console.WriteLine($"[workbench] range: {l.Primary.Name} ({l.Primary.BarrelCount} barrel(s), {l.Primary.Dps:0} DPS) + {l.Secondary.Name}");
    }

    /// <summary>The lobby frame while the workbench is up (or should come back after the range).</summary>
    private static bool DrawWorkbenchLobby(double now)
    {
        if (_returnToWorkbench && _session == null)
        {
            _returnToWorkbench = false;
            Ui.OpenWorkbench();
        }
        if (!Ui.WorkbenchOpen)
            return false;
        if (Ui.DrawWorkbench(AudioSystem, now) is { } launch)
            StartWorkbenchRange(launch);
        return true;
    }

    // ---- Smoke step (after the map select screen): the workbench with two builds, then the range with a minigun ----

    private static int _wbSmokeStage;
    private static double _wbSmokeStart;
    private static int _wbSmokeFrames;
    private static bool WorkbenchSmokeActive => _wbSmokeStage > 0;

    private static void StartWorkbenchSmoke(double now)
    {
        BuildStore.ReadOnly = true; // never write workbench.txt
        _wbSmokeStage = 1;
        _wbSmokeStart = now;
        Ui.OpenWorkbench();
        Console.WriteLine($"[smoke] workbench: {Parts.All.Length} parts, {Parts.LoadErrors.Count} part error(s)");
    }

    private static void WorkbenchSmokeTick(double now)
    {
        var t = now - _wbSmokeStart;
        var examples = BuilderExamples.All();
        switch (_wbSmokeStage)
        {
            case 1 when t > 1.0:
                SmokeShot("workbench");
                _wbSmokeStage = 2;
                break;
            case 2 when t > 1.3:
                if (examples.FirstOrDefault(e => e.Build.Name == "Lumberjack") is { } lj)
                    Ui.SmokeWorkbenchBuild(lj.Build);
                _wbSmokeStage = 3;
                break;
            case 3 when t > 2.2:
                SmokeShot("workbench_chainsaw");
                _wbSmokeStage = 4;
                break;
            case 4 when t > 2.5:
            {
                var mg = examples.FirstOrDefault(e => e.Build.Name == "Minigun");
                var tp = examples.FirstOrDefault(e => e.Build.Name == "Triple Pistol");
                var a = mg != null ? WeaponBuilder.Assemble(mg.Build).Def : null;
                var b = tp != null ? WeaponBuilder.Assemble(tp.Build).Def : null;
                if (a == null || b == null)
                {
                    Console.WriteLine("[smoke] workbench: example builds missing, range skipped");
                    _wbSmokeStage = 9;
                    break;
                }
                StartWorkbenchRange(new WorkbenchLaunch(a, b));
                _wbSmokeStage = 5;
                break;
            }
            case 5 when _session is { Phase: MatchPhase.Playing, Local.Alive: true } || t > 14:
                _smokeFireHeld = true; // spin the minigun up
                _wbSmokeFrames = _smokeFrames;
                _wbSmokeStage = 6;
                _wbSmokeStart = now - 6; // own clock from here
                break;
            case 6 when t > 8.0:
            {
                SmokeShot("range_minigun");
                var g = _session;
                Console.WriteLine($"[smoke] workbench range: {(g?.Local?.Alive == true ? "alive" : "not spawned")}, spool {g?.SpoolRate:0.00}, "
                                  + $"{g?.Tracers.Count ?? 0} tracer(s) live, {(_smokeFrames - _wbSmokeFrames) / 2.0:0} fps while firing");
                _wbSmokeStage = 7;
                break;
            }
            case 7 when t > 8.3:
                _smokeFireHeld = false;
                EndSession("");
                _returnToWorkbench = false;
                Ui.WorkbenchOpen = false;
                _wbSmokeStage = 9;
                break;
            case 9:
                _wbSmokeStage = 0;
                StartMoversSmoke(now); // the usual chain carries on
                break;
        }
    }
}
