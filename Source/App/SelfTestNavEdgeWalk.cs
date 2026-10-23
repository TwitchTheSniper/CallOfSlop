using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Nav edge walks": on every map, the real mover (<see cref="World.Simulate"/>, classic movement) walks the
/// bots' nav edges one at a time, the way a bot follows them (face the next node, Forward, Jump + Forward at a mantle edge's
/// ledge), for a few seconds each. An edge the walker can't finish is a spot where bots get stuck (a slope it slides back
/// down, a ledge it can't climb, a wall in the way). No bots, no matches: deterministic, a couple of seconds for all maps.
/// Every edge with a height change, a flag (mantle, drop) or a slope on the way is walked, both walking and sprinting;
/// level edges on a stride, so each map stays within a budget.
/// </summary>
public static partial class SelfTest
{
    private const int EdgeWalkBudget = 2500; // edges walked per map at most (all the interesting ones first)

    private readonly record struct EdgeWalkFail(int From, int To, byte Flags, Vector3 A, Vector3 B, Vector3 End, bool Sprint, string Why);

    private static void CheckNavEdgeWalks()
    {
        var previousMap = World.Map;
        var previousMover = World.Mover;
        World.Mover = ClassicMover.Instance;
        var notes = new List<string>();
        var perMap = new List<string>();
        var byKind = new Dictionary<string, List<(string Map, EdgeWalkFail F)>>();
        int totalEdges = 0, totalWalked = 0, totalFailed = 0, badMaps = 0, builderChecked = 0, builderRejected = 0, lastResorts = 0;
        var rejectedBy = new List<(string Map, int Count)>();
        var sw = Stopwatch.StartNew();
        try
        {
            foreach (var m in Maps.All)
            {
                World.SetMap(m);
                for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
                    NavGraph.Pump(50);
                var nav = NavGraph.Current;
                if (nav.Nodes.Length == 0)
                    continue;
                var (walked, fails) = EdgeWalkMap(m, nav);
                builderChecked += nav.WalkChecked;
                builderRejected += nav.WalkRejected;
                lastResorts += nav.LastResorts;
                if (nav.WalkRejected > 0)
                    rejectedBy.Add((m.Name, nav.WalkRejected));
                totalEdges += nav.BuiltEdges;
                totalWalked += walked;
                totalFailed += fails.Count;
                if (fails.Count == 0)
                    continue;
                badMaps++;
                perMap.Add($"{m.Name} {fails.Count}/{walked}");
                foreach (var f in fails)
                {
                    var kind = EdgeWalkKind(m, f);
                    if (!byKind.TryGetValue(kind, out var l))
                        byKind[kind] = l = new();
                    l.Add((m.Name, f));
                    Line(FormattableString.Invariant($"  {m.Name} [{kind}] {(f.Sprint ? "sprint" : "walk")} ({f.A.X:0.0}, {f.A.Y:0.0}, {f.A.Z:0.0}) -> ({f.B.X:0.0}, {f.B.Y:0.0}, {f.B.Z:0.0}) flags {f.Flags}: ended at ({f.End.X:0.0}, {f.End.Y:0.0}, {f.End.Z:0.0}), {f.Why}"));
                }
            }
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(previousMap);
        }
        var sum = $"{totalWalked} of {totalEdges} nav edges walked on {Maps.All.Length} maps, {totalFailed} failed on {badMaps} map(s), {sw.Elapsed.TotalSeconds:0.0} s";
        Line(sum);
        notes.Add(sum);
        if (totalFailed > 0)
            Fail($"nav edge walks: {totalFailed} edge(s) on {badMaps} map(s) the mover can't walk the way bots follow them ({string.Join(", ", perMap.Take(10))})");
        notes.Add($"the nav builder walked {builderChecked} links with the mover and dropped {builderRejected} directions ({lastResorts} kept as last resorts, not walked here); most on "
                  + string.Join(", ", rejectedBy.OrderByDescending(x => x.Count).Take(12).Select(x => $"{x.Map} {x.Count}")));
        notes.Add("by kind: " + string.Join(", ", byKind.Select(kv => $"{kv.Key} {kv.Value.Count}")));
        notes.Add("by map: " + string.Join(", ", perMap));
        foreach (var (kind, l) in byKind)
        {
            // A few examples of each kind, from different maps (walking first).
            var shown = l.Where(x => !x.F.Sprint).GroupBy(x => x.Map).Select(g => g.First()).Take(8).ToList();
            if (shown.Count == 0)
                shown = l.GroupBy(x => x.Map).Select(g => g.First()).Take(8).ToList();
            foreach (var (map, f) in shown)
                notes.Add(FormattableString.Invariant($"[{kind}] {map}: {(f.Sprint ? "sprint" : "walk")} ({f.A.X:0.0}, {f.A.Y:0.0}, {f.A.Z:0.0}) -> ({f.B.X:0.0}, {f.B.Y:0.0}, {f.B.Z:0.0}) f{f.Flags} end ({f.End.X:0.0}, {f.End.Y:0.0}, {f.End.Z:0.0}) {f.Why}"));
        }
        Line("::notice title=Nav edge walks::" + string.Join("%0A", notes.Take(90).Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    private static string EdgeWalkKind(Map m, EdgeWalkFail f)
    {
        if ((f.Flags & NavGraph.FlagMantle) != 0)
            return "mantle";
        if ((f.Flags & (NavGraph.FlagMantle | NavGraph.FlagDrop)) == NavGraph.FlagDrop)
            return "drop";
        if (m.TerrainCollision is { } t && EdgeWalkSloped(t, f.A, f.B))
            return "slope";
        if (MathF.Abs(f.A.Y - f.B.Y) > 0.3f)
            return "climb";
        return "level";
    }

    /// <summary>Walks a map's edges (see the class summary). Returns how many were walked and the ones that failed.</summary>
    private static (int Walked, List<EdgeWalkFail> Fails) EdgeWalkMap(Map m, NavGraph nav)
    {
        var fails = new List<EdgeWalkFail>();
        var terrain = m.TerrainCollision;
        var builtEnd = nav.SpareCount > 0 ? nav.SpareStart : nav.Nodes.Length;
        var plain = new List<int>();
        var interesting = new List<int>();
        var from = new int[nav.EdgeTo.Length];
        for (var i = 0; i < builtEnd; i++)
        {
            if (nav.Component[i] != nav.MainComponent)
                continue;
            for (var e = nav.EdgeStart[i]; e < nav.EdgeStart[i + 1]; e++)
            {
                var j = nav.EdgeTo[e];
                if ((nav.EdgeFlags[e] & NavGraph.FlagLastResort) != 0)
                    continue; // known to fail, kept only as the one way somewhere (BotNavWalk.cs)
                if (j >= builtEnd || (nav.EdgeOff.Length != 0 && nav.EdgeOff[e]) || (nav.NodeOff.Length != 0 && (nav.NodeOff[i] || nav.NodeOff[j])))
                    continue;
                from[e] = i;
                var a = nav.Nodes[i];
                var b = nav.Nodes[j];
                if (NavGraph.CrossesDoor(m, a, b))
                    continue; // through a shut door: bots open it, the walker can't
                if (nav.EdgeFlags[e] != 0 || MathF.Abs(a.Y - b.Y) > 0.3f || (terrain != null && EdgeWalkSloped(terrain, a, b)))
                    interesting.Add(e);
                else
                    plain.Add(e);
            }
        }
        var list = new List<(int Edge, bool Sprint)>();
        foreach (var e in interesting)
        {
            list.Add((e, false));
            list.Add((e, true));
        }
        if (list.Count > EdgeWalkBudget)
        {
            // Too many: an even spread of them (deterministic).
            var keep = new List<(int, bool)>();
            var stride = list.Count / (double)EdgeWalkBudget;
            for (var k = 0.0; k < list.Count; k += stride)
                keep.Add(list[(int)k]);
            list = keep;
        }
        var room = EdgeWalkBudget - list.Count;
        if (room > 0 && plain.Count > 0)
        {
            var stride = Math.Max(1.0, plain.Count / (double)room);
            for (var k = 0.0; k < plain.Count; k += stride)
                list.Add((plain[(int)k], (int)k % 2 == 1));
        }
        foreach (var (e, sprint) in list)
        {
            var i = from[e];
            var j = nav.EdgeTo[e];
            if (!EdgeWalkOne(nav, e, nav.Nodes[i], nav.Nodes[j], sprint, out var end, out var why))
                fails.Add(new EdgeWalkFail(i, j, nav.EdgeFlags[e], nav.Nodes[i], nav.Nodes[j], end, sprint, why));
        }
        return (list.Count, fails);
    }

    /// <summary>Is any stretch of the terrain under a -> b steeper than the full-speed limit (25 degrees)?</summary>
    private static bool EdgeWalkSloped(ITerrainCollision t, Vector3 a, Vector3 b)
    {
        var flat = new Vector2(b.X - a.X, b.Z - a.Z);
        var len = flat.Length();
        var n = Math.Max(1, (int)MathF.Ceiling(len / 0.5f));
        for (var k = 0; k <= n; k++)
        {
            var p = Vector3.Lerp(a, b, k / (float)n);
            if (MathF.Abs(p.Y - t.HeightAt(p.X, p.Z)) < 0.6f && SlopeRules.SupportNormal(t, p.X, p.Z).Y < SlopeRules.FullSpeedNormalY)
                return true;
        }
        return false;
    }

    /// <summary>
    /// One edge walked like a bot follows it, from standing at <paramref name="a"/>. True when it reaches <paramref name="b"/>
    /// (the bots' "reached the node" rule) in time: the walk at the slowest uphill speed, plus a second, plus a mantle.
    /// </summary>
    private static bool EdgeWalkOne(NavGraph nav, int e, Vector3 a, Vector3 b, bool sprint, out Vector3 end, out string why)
    {
        var mantleEdge = (nav.EdgeFlags[e] & NavGraph.FlagMantle) != 0;
        var ledge = nav.EdgeLedge[e];
        var r = NavGraph.MoverWalk(a, b, nav.EdgeFlags[e], ledge, sprint);
        end = r.End;
        why = "";
        if (r.Reached)
            return true;
        var (airborne, mantled, minDist) = (r.Airborne, r.Mantled, r.Closest);
        var reason = new List<string>();
        if (World.Map.TerrainCollision?.Layer(end.Y) is { } ground && end.Y - ground.HeightAt(end.X, end.Z) < 0.1f)
        {
            var n = SlopeRules.SupportNormal(ground, end.X, end.Z);
            var deg = MathF.Acos(Math.Clamp(n.Y, -1f, 1f)) * 180f / MathF.PI;
            reason.Add(FormattableString.Invariant($"on terrain {deg:0} deg"));
        }
        if (mantleEdge)
            reason.Add(FormattableString.Invariant($"mantle edge (ledge {ledge.Y - a.Y:0.00} m up, mantled {mantled})"));
        if (airborne > World.TickRate / 2)
            reason.Add($"airborne {airborne} ticks");
        if (World.Blocked(end + new Vector3(0, 0.02f, 0)))
            reason.Add("inside geometry");
        var dir = new Vector3(b.X - end.X, 0, b.Z - end.Z);
        if (dir.LengthSquared() > 1e-4f)
        {
            dir = Vector3.Normalize(dir);
            var hits = new List<string>();
            foreach (var h in new[] { 0.3f, 0.9f, 1.5f })
            {
                var d = World.RaycastWorld(end + new Vector3(0, h, 0), dir, 3f);
                if (d < 3f)
                    hits.Add(FormattableString.Invariant($"{h:0.0}m:{d:0.00}"));
            }
            if (hits.Count > 0)
                reason.Add("wall ahead " + string.Join(" ", hits));
        }
        reason.Add(FormattableString.Invariant($"closest {minDist:0.0} m of {SlopeFlat(b - a):0.0}"));
        why = string.Join(", ", reason);
        return false;
    }
}
