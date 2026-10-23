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
        int totalEdges = 0, totalWalked = 0, totalFailed = 0, badMaps = 0;
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
                totalEdges += nav.BuiltEdges;
                totalWalked += walked;
                totalFailed += fails.Count;
                if (fails.Count == 0)
                    continue;
                badMaps++;
                var head = $"{m.Name}: {fails.Count} of {walked} walked edges failed";
                Line(head);
                notes.Add(head);
                foreach (var f in fails.Take(6))
                {
                    var text = FormattableString.Invariant(
                        $"  {(f.Sprint ? "sprint" : "walk")} ({f.A.X:0.0}, {f.A.Y:0.0}, {f.A.Z:0.0}) -> ({f.B.X:0.0}, {f.B.Y:0.0}, {f.B.Z:0.0}) flags {f.Flags}: ended at ({f.End.X:0.0}, {f.End.Y:0.0}, {f.End.Z:0.0}), {f.Why}");
                    Line(text);
                    notes.Add(text);
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
        notes.Insert(0, sum);
        Line("::notice title=Nav edge walks::" + string.Join("%0A", notes.Take(80).Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
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
                if (j >= builtEnd || (nav.EdgeOff.Length != 0 && nav.EdgeOff[e]) || (nav.NodeOff.Length != 0 && (nav.NodeOff[i] || nav.NodeOff[j])))
                    continue;
                from[e] = i;
                var a = nav.Nodes[i];
                var b = nav.Nodes[j];
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
        var len = Vector3.Distance(a, b);
        var mantleEdge = (nav.EdgeFlags[e] & NavGraph.FlagMantle) != 0;
        var ledge = nav.EdgeLedge[e];
        var limit = (int)((len / (World.WalkSpeed * SlopeRules.SlowAtLimit * 0.8f) + 1f + (mantleEdge ? 1f : 0f)) * World.TickRate);
        var s = new MoveState { Position = a, Grounded = true };
        var airborne = 0;
        var mantled = false;
        var minDist = float.MaxValue;
        for (var t = 0; t < limit; t++)
        {
            var pos = s.Position;
            var dy = b.Y - pos.Y;
            var dist = SlopeFlat(b - pos);
            minDist = MathF.Min(minDist, dist);
            if (dist < 0.8f && dy < 1.0f && dy > -2.5f)
            {
                end = pos;
                why = "";
                return true;
            }
            var aim = b;
            var keys = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None);
            if (mantleEdge && SlopeFlat(ledge - pos) < 1.4f && ledge.Y > pos.Y + World.StepHeight)
            {
                keys |= Buttons.Jump;
                aim = ledge;
            }
            var yaw = MathF.Atan2(aim.X - pos.X, -(aim.Z - pos.Z));
            World.Simulate(ref s, new InputCmd { Buttons = keys, Yaw = yaw, SpeedPct = 100, Tick = 1 });
            if (s.MantleTicks > 0)
                mantled = true;
            if (!s.Grounded && s.MantleTicks == 0)
                airborne++;
        }
        end = s.Position;
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
        reason.Add(FormattableString.Invariant($"closest {minDist:0.0} m of {SlopeFlat(b - a):0.0}"));
        why = string.Join(", ", reason);
        return false;
    }
}
