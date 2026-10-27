using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Baked and cached nav graphs (Source/Game/BotNavBake.cs): a graph read back from its file is the live build bit for bit,
/// on maps of every kind (props and doors, destruction, buildings, a planet, gravity zones, terrain caves); the game caches
/// what it builds and reads it next time; the release's baked folder comes first; and a file for a changed map, another
/// build, another format, or a damaged or cut-short file is never used. All in temp folders: never a real cache.
/// </summary>
public static partial class SelfTest
{
    private static void CheckNavBake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"callofslop-navbake-{Guid.NewGuid():N}");
        var baked = Path.Combine(root, "Baked");
        var cache = Path.Combine(root, "Cache");
        try
        {
            NavBakeRoundTrips();
            NavBake.BakedFolderOverride = baked;
            NavBake.CacheFolderOverride = cache;
            NavBake.Enabled = true;
            NavBakeCacheAndBaked(baked, cache);
            NavBakeRefusals(cache);
            NavBakeNeverWaits(cache);
        }
        finally
        {
            NavBake.Enabled = false;
            NavBake.BakedFolderOverride = null;
            NavBake.CacheFolderOverride = null;
            foreach (var m in Maps.All)
                NavGraph.Forget(m); // the next section builds its graphs live again
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static readonly string[] NavBakeMaps = ["Rooftops", "Building Yard", "Destruction Test", "Pocket Planet", "Keelhaul Yard", "Cave Test", "Paris Quarter"];

    /// <summary>Every kind of map: the live graph, written and read back, is the same graph.</summary>
    private static void NavBakeRoundTrips()
    {
        foreach (var name in NavBakeMaps)
        {
            if (Maps.Find(name) is not { } map)
            {
                Line($"{name}: not loaded, skipped");
                continue;
            }
            World.SetMap(map);
            NavGraph.Forget(map);
            var live = NavGraph.Build(map);
            var key = NavBake.KeyOf(map, map.ContentHash);
            var t = Stopwatch.GetTimestamp();
            key = NavBake.KeyOf(map, map.ContentHash); // (timed the second time: the first pays the JIT)
            var keyMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            // In a match the key is hashed on a worker from the boxes taken on the game thread (BotNav.cs, PumpGraph): the
            // same bytes. The tick's share (taking the boxes) is measured in NavBakeNeverWaits.
            var boxes = NavBake.TakeBoxes(map);
            var hash = map.ContentHash;
            var workerKey = Task.Run(() => NavBake.KeyOf(map, hash, boxes)).Result;
            if (!workerKey.AsSpan().SequenceEqual(key))
                Fail($"Nav bake: {name}: the key hashed on a worker isn't the one hashed on the game thread");
            var file = NavBake.Write(live, map, key);
            t = Stopwatch.GetTimestamp();
            var back = NavBake.Read(file, map.Name, map.ContentHash, key, out var why);
            var readMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            if (back == null)
            {
                Fail($"Nav bake: {name}: its own bake was refused ({why})");
                continue;
            }
            back.Attach(map, "baked");
            var same = back.Serialize().AsSpan().SequenceEqual(live.Serialize()) && NavFingerprint(back) == NavFingerprint(live);
            // The same answers too: a route between the two spawns furthest apart, and the nearest node to each spawn.
            var search = new NavSearch();
            int[] pa = new int[256], pb = new int[256];
            var s0 = map.Spawns[0];
            var s1 = map.Spawns.OrderByDescending(s => System.Numerics.Vector3.Distance(s, s0)).First();
            var la = search.Find(live, live.Nearest(s0), live.Nearest(s1, needSight: false), pa);
            var lb = search.Find(back, back.Nearest(s0), back.Nearest(s1, needSight: false), pb);
            same &= la == lb && pa.AsSpan(0, la).SequenceEqual(pb.AsSpan(0, lb));
            Line($"{name,-16} {live.Nodes.Length,5} nodes {live.EdgeCount,6} edges: built in {live.BuildMs,5:0} ms, key {keyMs:0.0} ms (a worker's, in a match), file {file.Length / 1024,4} KB read in {readMs:0.0} ms; same graph: {same}");
            if (!same)
                Fail($"Nav bake: {name}: the graph read back isn't the one built");
        }
    }

    /// <summary>Build, cache, read the cache; a baked file wins over the cache.</summary>
    private static void NavBakeCacheAndBaked(string baked, string cache)
    {
        if (Maps.Find("Rooftops") is not { } map)
            return;
        World.SetMap(map);
        NavGraph.Forget(map);
        var live = NavGraph.Build(map);
        var liveHash = NavFingerprint(live);
        NavGraph.Forget(map);
        var written = NavBake.Written;
        NavGraph.Pump(double.MaxValue);
        var first = NavGraph.Current;
        var file = Path.Combine(cache, NavBake.FileName(map.Name));
        for (var wait = 0; wait < 200 && NavBake.Written == written; wait++)
            Thread.Sleep(10); // the cache is written on a worker
        if (first.Origin != "built" || !File.Exists(file))
            Fail($"Nav bake: no cache: a first build came out {first.Origin}, cache file {(File.Exists(file) ? "written" : "missing")}");
        NavGraph.Forget(map);
        NavGraph.Pump(double.MaxValue);
        var second = NavGraph.Current;
        if (second.Origin != "cached" || NavFingerprint(second) != liveHash || !second.Serialize().AsSpan().SequenceEqual(first.Serialize()))
            Fail($"Nav bake: the second time the graph came out {second.Origin} ({NavBake.LastResult}), fingerprint {NavFingerprint(second):x8} vs {liveHash:x8}");

        // The release's baked folder comes before the cache.
        Directory.CreateDirectory(baked);
        File.WriteAllBytes(Path.Combine(baked, NavBake.FileName(map.Name)), NavBake.Write(live, map, NavBake.KeyOf(map, map.ContentHash)));
        NavGraph.Forget(map);
        NavGraph.Pump(double.MaxValue);
        var third = NavGraph.Current;
        if (third.Origin != "baked" || NavFingerprint(third) != liveHash)
            Fail($"Nav bake: with a baked file the graph came out {third.Origin} ({NavBake.LastResult})");
        Line($"Rooftops: built and cached, then read from the cache, then from the baked folder: {first.Origin} / {second.Origin} / {third.Origin}");
        File.Delete(Path.Combine(baked, NavBake.FileName(map.Name)));
    }

    /// <summary>A file that doesn't exactly match is ignored and the graph built live.</summary>
    private static void NavBakeRefusals(string cache)
    {
        if (Maps.Find("Rooftops") is not { } map)
            return;
        World.SetMap(map);
        NavGraph.Forget(map);
        var live = NavGraph.Build(map);
        var key = NavBake.KeyOf(map, map.ContentHash);
        var good = NavBake.Write(live, map, key);
        var path = Path.Combine(cache, NavBake.FileName(map.Name));

        // The same map name with one box 0.4 mm taller: too little for the fingerprint (it rounds to millimetres), not for the key.
        var json = MapFile.ToJson(map);
        var y1 = new System.Text.RegularExpressions.Regex("\"y1\": (-?[0-9.]+)");
        var edited = y1.Replace(json, m => $"\"y1\": {(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 0.0004).ToString(System.Globalization.CultureInfo.InvariantCulture)}", 1);
        var moved = MapFile.Load(edited, "Rooftops.json", "");
        var changedKey = NavBake.KeyOf(moved, moved.ContentHash);
        Line($"Rooftops with a box 0.4 mm taller: fingerprint {(moved.ContentHash == map.ContentHash ? "the same" : "changed")}, bake key {(changedKey.AsSpan().SequenceEqual(key) ? "THE SAME" : "changed")}");
        if (edited == json || changedKey.AsSpan().SequenceEqual(key))
            Fail("Nav bake: a changed box didn't change the bake key");

        var cases = new List<(string What, byte[] File, byte[] Key)>
        {
            ("the map changed", good, changedKey),
            ("cut short", good[..(good.Length - 40)], key),
            ("a byte flipped in the graph", Flip(good, good.Length - 100), key),
            ("another bake format", Flip(good, 8), key),
            ("another build", Flip(good, 14), key),
            ("not a bake", "not a nav graph"u8.ToArray(), key),
        };
        var refused = 0;
        foreach (var (what, bytes, k) in cases)
        {
            if (NavBake.Read(bytes, map.Name, map.ContentHash, k, out var why) == null)
                refused++;
            else
                Fail($"Nav bake: a file with {what} was accepted");
            Line($"  {what}: {why}");
        }
        // Through the game's own path: a damaged cache file means a live build (and a good cache file again).
        File.WriteAllBytes(path, Flip(good, good.Length - 100));
        NavGraph.Forget(map);
        NavGraph.Pump(double.MaxValue);
        if (NavGraph.Current.Origin != "built" || NavFingerprint(NavGraph.Current) != NavFingerprint(live))
            Fail($"Nav bake: with a damaged cache file the graph came out {NavGraph.Current.Origin} ({NavBake.LastResult})");
        Line($"{refused} of {cases.Count} mismatched or damaged files refused; a damaged cache file: {NavBake.LastResult}, built live");

        static byte[] Flip(byte[] b, int at)
        {
            var c = (byte[])b.Clone();
            c[at] ^= 0x5a;
            return c;
        }
    }

    /// <summary>
    /// In a match (a small budget per tick), looking for a bake never stalls a tick: the key is hashed and the file read on
    /// a worker. Measured as the game thread's CPU time per tick where that can be measured (Windows: TickCpu), so a runner
    /// that pauses the thread doesn't fail it; the maps where the key costs most (boxes, building nav points, solid meshes).
    /// </summary>
    private static void NavBakeNeverWaits(string cache)
    {
        foreach (var name in new[] { "Paris Quarter", "Building Yard", "Pocket Planet" })
            if (Maps.Find(name) is { } map)
                NavBakeNeverWaits(cache, map);
    }

    private static void NavBakeNeverWaits(string cache, Map map)
    {
        World.SetMap(map);
        NavGraph.Forget(map);
        NavGraph.Pump(double.MaxValue); // built (and cached)
        var built = NavFingerprint(NavGraph.Current);
        for (var wait = 0; wait < 300 && !File.Exists(Path.Combine(cache, NavBake.FileName(map.Name))); wait++)
            Thread.Sleep(10);
        NavGraph.Forget(map);
        double worst = 0;
        var ticks = 0;
        var cpu = new TickCpu();
        while (NavGraph.Current == NavGraph.Empty && ticks < 600)
        {
            var t = Stopwatch.GetTimestamp();
            cpu.Start();
            NavGraph.Pump(3);
            var ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            cpu.Stop(ms, count: true);
            worst = Math.Max(worst, ms);
            ticks++;
            if (NavGraph.Current == NavGraph.Empty)
                Thread.Sleep(1);
        }
        var g = NavGraph.Current;
        Line($"{map.Name} in a match: ready after {ticks} ticks ({g.Origin}), the longest tick's share {worst:0.0} ms wall, {cpu.WorstText} CPU"
            + (TickCpu.Available ? $" ({cpu.WorstDetail})" : ""));
        if (g.Origin != "cached" || NavFingerprint(g) != built)
            Fail($"Nav bake: {map.Name} in a match came out {g.Origin} ({NavBake.LastResult})");
        if (TickCpu.Available && cpu.WorstMs > MaxTickWorkMs)
            Fail($"Nav bake: {map.Name}: looking for the bake took {cpu.WorstMs:0.0} ms of one tick's CPU (budget {MaxTickWorkMs:0} ms; {cpu.WorstDetail})");
    }
}
