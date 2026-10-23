using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Nav graph determinism: the same map must give the same graph on any PC, at any speed, whatever happens in the match
/// while it's built. Each map is built three ways and the graphs' fingerprints compared:
///   1. all at once (<see cref="NavGraph.Build"/>);
///   2. a step per slice (a time budget of almost nothing) while the match breaks props, opens and shuts doors, smashes
///      windows and blasts chunks between slices (what a real match does to a build spread over ticks);
///   3. the self-tests' usual way (50 ms slices) on the restored map.
/// </summary>
public static partial class SelfTest
{
    private static void CheckNavDeterminism()
    {
        var sw = Stopwatch.StartNew();
        foreach (var name in new[] { "Rooftops", "Slopview Mall", "Building Yard", "Destruction Test" })
        {
            if (Maps.Find(name) is not { } map)
            {
                Line($"{name}: not loaded, skipped");
                continue;
            }
            World.SetMap(map); // (restores props, doors, windows and chunks)
            NavGraph.Forget(map);
            var whole = NavGraph.Build(map);
            var h0 = NavFingerprint(whole);

            // A step per slice, with the match changing the map in between.
            NavGraph.Forget(map);
            int slices = 0, changes = 0, props = 0, doors = 0, windows = 0, chunks = 0;
            var breakable = map.BreakableProps;
            while (NavGraph.Current == NavGraph.Empty && slices < 2_000_000)
            {
                NavGraph.Pump(1e-6);
                slices++;
                if (slices % 97 != 0)
                    continue;
                // Something happens in the match.
                switch (changes++ % 4)
                {
                    case 0 when props < breakable.Length:
                        map.BreakProp(breakable[props++]);
                        break;
                    case 1 when map.BuildingDoors.Count > 0:
                        var di = doors++ % map.BuildingDoors.Count;
                        map.SetBuildingDoor(di, !map.BuildingDoors[di].Open);
                        break;
                    case 2 when windows < map.BuildingWindows.Count:
                        map.BreakBuildingWindow(windows++);
                        break;
                    case 3 when map.Destruction is { } d && chunks < d.Count:
                        map.BreakChunk(chunks);
                        chunks += 7;
                        break;
                }
            }
            var sliced = NavGraph.Current;
            var h1 = NavFingerprint(sliced);

            // Restored, the usual 50 ms slices.
            map.RestoreProps();
            NavGraph.Forget(map);
            for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
                NavGraph.Pump(50);
            var h2 = NavFingerprint(NavGraph.Current);
            Line($"{name,-16} nav fingerprint: whole {h0:x8} ({whole.Nodes.Length} nodes, {whole.EdgeCount} edges), {slices} one-step slices "
                 + $"while {Math.Min(props, breakable.Length)} props broke, {doors} doors swung, {windows} windows and {(chunks + 6) / 7} chunks broke: {h1:x8}; 50 ms slices {h2:x8}");
            if (h1 != h0 || h2 != h0)
                Fail($"{name}: the nav graph isn't deterministic (whole {h0:x8}, sliced while the match changed things {h1:x8}, 50 ms slices {h2:x8})");

            // Destruction patches (run within a time budget per tick in a match) are gone once the map is whole again.
            if (map.Destruction is { } dest && NavGraph.Current is { ExtraHead.Length: > 0 } live)
            {
                var min = new System.Numerics.Vector3(float.MaxValue);
                var max = new System.Numerics.Vector3(float.MinValue);
                for (var c = 0; c < dest.Count; c += 5)
                    if (map.BreakChunk(c))
                    {
                        min = System.Numerics.Vector3.Min(min, dest.Original[c].Min);
                        max = System.Numerics.Vector3.Max(max, dest.Original[c].Max);
                    }
                if (min.X <= max.X)
                    foreach (var _ in live.PatchSteps(map, new Box((min + max) / 2, (max - min) / 2), []))
                    {
                    }
                var patched = NavFingerprint(live);
                var version = live.PatchVersion;
                map.RestoreProps();
                var restored = NavFingerprint(live);
                Line($"{name,-16} a destruction patch (version {version}): fingerprint {patched:x8}, after the map is restored {restored:x8}");
                if (restored != h0)
                    Fail($"{name}: the nav graph keeps destruction patches after the map is restored ({restored:x8}, as built {h0:x8}, patched {patched:x8})");
            }
        }
        Line($"nav determinism: {sw.Elapsed.TotalSeconds:0.0} s");
    }

    /// <summary>FNV-1a over a graph's nodes and edges (positions, targets, costs, flags, ledges).</summary>
    private static uint NavFingerprint(NavGraph g)
    {
        var h = 2166136261u;
        void Mix(uint v)
        {
            h ^= v;
            h *= 16777619u;
        }
        void MixF(float f) => Mix(BitConverter.SingleToUInt32Bits(f));
        foreach (var n in g.Nodes)
        {
            MixF(n.X);
            MixF(n.Y);
            MixF(n.Z);
        }
        foreach (var s in g.EdgeStart)
            Mix((uint)s);
        for (var e = 0; e < g.EdgeTo.Length; e++)
        {
            Mix((uint)g.EdgeTo[e]);
            Mix(g.EdgeFlags[e]);
            MixF(g.EdgeCost[e]);
            MixF(g.EdgeLedge[e].X);
            MixF(g.EdgeLedge[e].Y);
            MixF(g.EdgeLedge[e].Z);
        }
        foreach (var off in g.NodeOff)
            Mix(off ? 1u : 0u);
        foreach (var off in g.EdgeOff)
            Mix(off ? 1u : 0u);
        return h;
    }
}
