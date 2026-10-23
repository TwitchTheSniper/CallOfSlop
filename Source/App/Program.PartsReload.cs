namespace CallOfSlop;

/// <summary>
/// Part hot reload from the game window: with <c>--watch-parts</c> on the command line, the Parts folder is checked twice
/// a second and reloaded as soon as a file changes (and has stopped changing), whether you're on the workbench, in the
/// Workbench Range or in an offline Zombies game. The pause menu's "Reload part files" does the same by hand.
/// </summary>
public static unsafe partial class Program
{
    private static bool? _watchParts;
    private static bool _partsHooked;
    private static double _partsNextCheck;
    private static (int Count, long Ticks, long Bytes) _partsStamp, _partsPending;

    /// <summary>Called every frame from the main loop: hooks the reload event once and watches the folder if asked to.</summary>
    private static void TickPartsWatch(double now)
    {
        if (!_partsHooked)
        {
            _partsHooked = true;
            Parts.Reloaded += _ =>
            {
                _cartGunParts.Clear(); // push-cart gun pieces sliced from the old bodies (Program.Workbench.cs)
                Ui.OnPartsReloaded(); // the workbench preview
            };
        }
        _watchParts ??= Environment.GetCommandLineArgs().Any(a => a.Equals("--watch-parts", StringComparison.OrdinalIgnoreCase));
        if (_watchParts != true || _smoke || now < _partsNextCheck)
            return;
        _partsNextCheck = now + 0.5;
        var stamp = PartsStamp();
        if (_partsStamp == default)
        {
            _partsStamp = _partsPending = stamp; // first look: remember, don't reload
            Console.WriteLine($"[parts] watching {Parts.Folder} for changes (--watch-parts)");
            return;
        }
        if (stamp == _partsStamp)
            return;
        if (stamp != _partsPending)
        {
            _partsPending = stamp; // still being written: wait until it holds still for one check
            return;
        }
        _partsStamp = stamp;
        ReloadPartsNow();
    }

    /// <summary>Reloads the part files: through the session when there is one (rebuilds weapons in play), else directly.</summary>
    private static void ReloadPartsNow()
    {
        if (_session is { } g)
            g.ReloadPartFiles(); // refuses while other players are connected
        else
            _lobbyMessage = Parts.Reload().Summary;
    }

    /// <summary>Count, newest write time and total size of every .json under Parts (cheap: a few hundred file stats).</summary>
    private static (int, long, long) PartsStamp()
    {
        try
        {
            int n = 0;
            long newest = 0, bytes = 0;
            foreach (var f in new DirectoryInfo(Parts.Folder).EnumerateFiles("*.json", SearchOption.AllDirectories))
            {
                n++;
                newest = Math.Max(newest, f.LastWriteTimeUtc.Ticks);
                bytes += f.Length;
            }
            return (n, newest, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _partsStamp;
        }
    }
}
