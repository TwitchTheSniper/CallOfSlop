using System.Diagnostics;

namespace CallOfSlop;

// TEMPORARY: launch timing for docs/perf/STREAMING.md (removed before the branch is finished).
public static partial class SelfTest
{
    public static int TimeMapLoad(string mode, string dir)
    {
        var p0 = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        MapBake.Enabled = mode == "baked";
        MapBake.CacheWrites = mode == "write";
        MapBake.BakedFolderOverride = Path.Combine(dir, "Baked");
        MapBake.CacheFolderOverride = Path.Combine(dir, "Cache");
        var t = Stopwatch.GetTimestamp();
        var n = Maps.All.Length;
        var ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        var sinceStart = (DateTime.UtcNow - p0).TotalMilliseconds;
        MapBake.FlushWrites();
        var t2 = Stopwatch.GetTimestamp();
        Maps.LoadFolder(Maps.Folder, new List<string>(), true, mode == "live" ? Maps.BakeUse.None : Maps.BakeUse.ReadWrite);
        var warm = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;
        File.AppendAllText(Path.Combine(dir, "times.txt"), $"{mode} {n} maps: first load {ms:0} ms ({sinceStart:0} ms since the process started), again {warm:0} ms; {MapBake.LastSummary}; libs {Maps.LastLoadTimes.LibrariesMs:0} ms\n");
        return 0;
    }

    private static void CheckMapBakeTiming()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-maptime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            void Run(string mode)
            {
                var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, WorkingDirectory = AppContext.BaseDirectory };
                psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CallOfSlop.dll"));
                psi.ArgumentList.Add("--time-map-load");
                psi.ArgumentList.Add(mode);
                psi.ArgumentList.Add(dir);
                var sw = Stopwatch.StartNew();
                using var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                File.AppendAllText(Path.Combine(dir, "times.txt"), $"  ({mode}: process {sw.ElapsedMilliseconds} ms)\n");
            }
            Run("live");
            Run("write");
            Directory.Move(Path.Combine(dir, "Cache"), Path.Combine(dir, "Baked"));
            Run("baked");
            Run("live");
            Run("baked");
            Run("live");
            Run("baked");
            foreach (var l in File.ReadAllLines(Path.Combine(dir, "times.txt")))
                Line(l);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
