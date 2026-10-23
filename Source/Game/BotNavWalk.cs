using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The nav graph's ground truth: the real movement code walks a link the way a bot follows it. The sampled checks in
/// <see cref="NavGraph"/> (ground heights, body room, slope rules) are fast but can't see everything the mover does: a
/// ledge with no room to land on, a slope the mover slides back down, a thin railing between two samples, a step under a
/// low ceiling. So every link where those can happen (a mantle or a drop, a height change, a slope, a sample squeezed past
/// a ledge) is also walked with <see cref="ClassicMover"/> while the graph is built, and a direction the walker can't
/// finish gets no edge (a slide-down slope becomes one-way, a ledge it can't climb a drop only). The self-test
/// "Nav edge walks" walks the finished edges of every map the same way.
/// </summary>
public sealed partial class NavGraph
{
    /// <summary>Links checked with the mover during the build, directions dropped because the walk failed, and of those the ones kept as last resorts.</summary>
    public int WalkChecked, WalkRejected, LastResorts;

    /// <summary>Build work spent on the mover walks and on deciding which links need one (Stopwatch ticks).</summary>
    public long WalkTicks, PrepTicks;

    /// <summary>
    /// Adds back the failed directions (<paramref name="failed"/>) that are the only way in or out of somewhere: while some node
    /// that has edges isn't in the biggest group of nodes that can all reach each other, failed directions touching nodes
    /// outside it are added (flagged <see cref="FlagLastResort"/>, costed high), a few rounds at most (one per build slice).
    /// </summary>
    private static IEnumerable<bool> KeepLastResorts(NavGraph g, int n, List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)> edges,
        List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)> failed)
    {
        if (failed.Count == 0)
            yield break;
        var used = new bool[failed.Count];
        for (var round = 0; round < 4; round++)
        {
            if (round > 0)
                yield return false; // a round per slice (each looks at every edge)
            var inMain = StrongMain(n, edges);
            var added = 0;
            for (var k = 0; k < failed.Count; k++)
            {
                var f = failed[k];
                if (used[k] || (inMain[f.From] && inMain[f.To]))
                    continue;
                used[k] = true;
                edges.Add(f);
                added++;
            }
            g.LastResorts += added;
            if (added == 0)
                yield break;
        }
    }

    /// <summary>Which nodes are in the biggest strongly connected group of a plain edge list (Kosaraju, iterative).</summary>
    private static bool[] StrongMain(int n, List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)> edges)
    {
        var start = new int[n + 1];
        var rstart = new int[n + 1];
        foreach (var e in edges)
        {
            start[e.From + 1]++;
            rstart[e.To + 1]++;
        }
        for (var i = 0; i < n; i++)
        {
            start[i + 1] += start[i];
            rstart[i + 1] += rstart[i];
        }
        var to = new int[edges.Count];
        var from = new int[edges.Count];
        var fa = (int[])start.Clone();
        var fb = (int[])rstart.Clone();
        foreach (var e in edges)
        {
            to[fa[e.From]++] = e.To;
            from[fb[e.To]++] = e.From;
        }
        var order = new List<int>(n);
        var seen = new bool[n];
        var stack = new Stack<(int Node, int Next)>();
        for (var root = 0; root < n; root++)
        {
            if (seen[root])
                continue;
            seen[root] = true;
            stack.Push((root, start[root]));
            while (stack.Count > 0)
            {
                var (i, next) = stack.Pop();
                if (next < start[i + 1])
                {
                    stack.Push((i, next + 1));
                    if (!seen[to[next]])
                    {
                        seen[to[next]] = true;
                        stack.Push((to[next], start[to[next]]));
                    }
                }
                else
                    order.Add(i);
            }
        }
        var group = new int[n];
        Array.Fill(group, -1);
        var sizes = new List<int>();
        var flat = new Stack<int>();
        for (var k = order.Count - 1; k >= 0; k--)
        {
            var root = order[k];
            if (group[root] >= 0)
                continue;
            var id = sizes.Count;
            var size = 0;
            group[root] = id;
            flat.Push(root);
            while (flat.Count > 0)
            {
                var i = flat.Pop();
                size++;
                for (var e = rstart[i]; e < rstart[i + 1]; e++)
                    if (group[from[e]] < 0)
                    {
                        group[from[e]] = id;
                        flat.Push(from[e]);
                    }
            }
            sizes.Add(size);
        }
        var main = 0;
        for (var k = 1; k < sizes.Count; k++)
            if (sizes[k] > sizes[main])
                main = k;
        var inMain = new bool[n];
        for (var i = 0; i < n; i++)
            inMain[i] = sizes.Count > 0 && group[i] == main;
        return inMain;
    }

    /// <summary>The result of one <see cref="MoverWalk"/>.</summary>
    public struct WalkResult
    {
        public bool Reached;
        public Vector3 End;
        public int Airborne;
        public bool Mantled;
        public float Closest;
    }

    /// <summary>
    /// Walks from standing at <paramref name="a"/> to <paramref name="b"/> with the classic mover, like a bot on that edge:
    /// facing b, Forward (and Sprint), Jump + Forward facing the ledge when it's a mantle edge (<paramref name="flags"/>,
    /// <paramref name="ledge"/>) within 1.4 m, and one hop after 0.8 s without getting closer (the bots' first unstick try).
    /// Reached is the bots' "at the node" rule (BotFollow), within the time a walk at the slowest uphill speed takes plus a
    /// second (and another for a mantle). Deterministic (mover tick 1).
    /// </summary>
    public static WalkResult MoverWalk(Vector3 a, Vector3 b, byte flags, Vector3 ledge, bool sprint)
    {
        // Always at the map's own gravity, whatever the host's gravity setting (World.GravityScale): one graph per map, the
        // same on every PC and for every setting (the links are walks, mantles and drops, which don't need air time).
        var scale = World.GravityScale;
        World.GravityScale = 1f;
        try
        {
            return GravityFrames.Curved ? MoverWalkField(a, b, flags, ledge, sprint) : MoverWalkPlain(a, b, flags, ledge, sprint);
        }
        finally
        {
            World.GravityScale = scale;
        }
    }

    private static WalkResult MoverWalkPlain(Vector3 a, Vector3 b, byte flags, Vector3 ledge, bool sprint)
    {
        var len = Vector3.Distance(a, b);
        var mantleEdge = (flags & FlagMantle) != 0;
        var limit = (int)((len / (World.WalkSpeed * SlopeRules.SlowAtLimit * 0.8f) + 1f + (mantleEdge ? 1f : 0f)) * World.TickRate);
        var s = new MoveState { Position = a, Grounded = true };
        var r = new WalkResult { Closest = float.MaxValue };
        var mover = ClassicMover.Instance;
        var lastGain = 0;
        var gainAt = float.MaxValue;
        var hopPending = false;
        for (var t = 0; t < limit; t++)
        {
            var pos = s.Position;
            var dy = b.Y - pos.Y;
            var dx = b.X - pos.X;
            var dz = b.Z - pos.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < gainAt - 0.2f || (MathF.Abs(dy) > 0.5f && s.MantleTicks > 0))
            {
                gainAt = dist; // 0.2 m closer (or climbing): still going
                lastGain = t;
            }
            else if (t - lastGain > World.TickRate * 6 / 5)
                break; // no closer for 1.2 s, a hop included: stuck or sliding back (fail early, the build walks thousands of these)
            r.Closest = MathF.Min(r.Closest, dist);
            if (dist < 0.8f && dy < 1.0f && dy > -2.5f)
            {
                r.Reached = true;
                r.End = pos;
                return r;
            }
            var aim = b;
            var keys = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None);
            if (mantleEdge && ledge.Y > pos.Y + World.StepHeight
                && (ledge.X - pos.X) * (ledge.X - pos.X) + (ledge.Z - pos.Z) * (ledge.Z - pos.Z) < 1.4f * 1.4f)
            {
                keys |= Buttons.Jump;
                aim = ledge;
            }
            // No closer for 0.8 s: one hop forward, like a bot's first unstick try (BotUnstick: a ledge it didn't know about,
            // a second lip after the first mantle).
            if (t - lastGain == World.TickRate * 4 / 5 || (hopPending && s.Grounded))
            {
                hopPending = !s.Grounded;
                if (s.Grounded)
                    keys |= Buttons.Jump;
            }
            var yaw = MathF.Atan2(aim.X - pos.X, -(aim.Z - pos.Z));
            mover.Simulate(ref s, new InputCmd { Buttons = keys, Yaw = yaw, SpeedPct = 100, Tick = 1 });
            if (s.MantleTicks > 0)
                r.Mantled = true;
            if (!s.Grounded && s.MantleTicks == 0)
                r.Airborne++;
        }
        r.End = s.Position;
        return r;
    }

    /// <summary>
    /// <see cref="MoverWalk"/> on a map whose gravity isn't plain straight down (GravityZones.cs): the mover the game runs
    /// there (<see cref="World.Simulate"/>: the capsule mover with the local field, so low gravity, walls and rocks behave
    /// as in play), steering and measuring in the local ground plane at each step, the way bots do. The same rules otherwise
    /// (reach within 0.8 m across the ground and a little along up, give up after 1.2 s without getting closer, one hop at
    /// 0.8 s). Plain maps never come here, so their graphs are unchanged.
    /// </summary>
    private static WalkResult MoverWalkField(Vector3 a, Vector3 b, byte flags, Vector3 ledge, bool sprint)
    {
        var len = Vector3.Distance(a, b);
        var mantleEdge = (flags & FlagMantle) != 0;
        var limit = (int)((len / (World.WalkSpeed * SlopeRules.SlowAtLimit * 0.8f) + 1f + (mantleEdge ? 1f : 0f)) * World.TickRate);
        var s = new MoveState { Position = a, Grounded = true };
        var r = new WalkResult { Closest = float.MaxValue };
        var lastGain = 0;
        var gainAt = float.MaxValue;
        var hopPending = false;
        for (var t = 0; t < limit; t++)
        {
            var pos = s.Position;
            var up = GravityFrames.UpAt(pos);
            var rel = b - pos;
            var dy = Vector3.Dot(rel, up);
            var dist = (rel - up * dy).Length();
            if (dist < gainAt - 0.2f || (MathF.Abs(dy) > 0.5f && s.MantleTicks > 0))
            {
                gainAt = dist;
                lastGain = t;
            }
            else if (t - lastGain > World.TickRate * 6 / 5)
                break;
            r.Closest = MathF.Min(r.Closest, dist);
            if (dist < 0.8f && dy < 1.0f && dy > -2.5f)
            {
                r.Reached = true;
                r.End = pos;
                return r;
            }
            var aim = b;
            var keys = Buttons.Forward | (sprint ? Buttons.Sprint : Buttons.None);
            if (mantleEdge && up == Vector3.UnitY && ledge.Y > pos.Y + World.StepHeight
                && (ledge.X - pos.X) * (ledge.X - pos.X) + (ledge.Z - pos.Z) * (ledge.Z - pos.Z) < 1.4f * 1.4f)
            {
                keys |= Buttons.Jump;
                aim = ledge;
            }
            if (t - lastGain == World.TickRate * 4 / 5 || (hopPending && s.Grounded))
            {
                hopPending = !s.Grounded;
                if (s.Grounded)
                    keys |= Buttons.Jump;
            }
            World.Simulate(ref s, new InputCmd { Buttons = keys, Yaw = GravityFrames.YawOf(pos, aim - pos), SpeedPct = 100, Tick = 1 });
            if (s.MantleTicks > 0)
                r.Mantled = true;
            if (!s.Grounded && s.MantleTicks == 0)
                r.Airborne++;
        }
        r.End = s.Position;
        return r;
    }

    /// <summary>
    /// Does a link a -> b need the mover's say (<see cref="MoverWalk"/>)? Mantles and drops, height changes beyond a small
    /// step off the terrain, a body sample squeezed past a ledge (<paramref name="tight"/>), something low or waist-high in
    /// the way, and terrain steeper than <see cref="WalkCheckNormalY"/> near the line (<paramref name="steepest"/>). Plain
    /// walks are left to the sampled checks.
    /// </summary>
    private static readonly QueryFilter ShinFilter = new(QueryLayers.StaticShapes & ~QueryLayers.Doors); // boxes, props, meshes (not the terrain's own bumps)

    private static bool NeedsMoverWalk(Vector3 a, Vector3 b, byte flags, bool tight, float steepest)
    {
        if (CrossesDoor(World.Map, a, b))
            return false; // through a shut door: bots open it, the plain mover can't (see CrossesDoor)
        if (flags != 0 || tight || World.Map.FallKillY != null)
            return true; // (a void map: every link, a slip off an edge there is a death)
        // A height change off the terrain (steps, ramps of boxes); on the terrain the slope says it (below).
        if (MathF.Abs(a.Y - b.Y) > 0.3f && !(OnTerrain(a) && OnTerrain(b)))
            return true;
        // Something low in the way the samples stepped past (a thin kerb or rail under the body check's height, a step under
        // a shelf): a ray at shin height along the link.
        var shin = new Vector3(0, 0.3f, 0);
        var d = b - a;
        var dist = d.Length();
        if (dist > 0.01f)
        {
            var dir = d / dist;
            var side = Vector3.Normalize(new Vector3(-dir.Z, 0, dir.X) + new Vector3(0, 1e-6f, 0)) * (World.PlayerRadius + 0.05f); // just outside the body: a jamb it would catch on
            foreach (var h in (ReadOnlySpan<float>)[0f, 0.8f]) // shin and waist height
                foreach (var o in (ReadOnlySpan<float>)[0f, 1f, -1f]) // the middle and both sides of the body
                    if (World.Physics.Raycast(a + shin + new Vector3(0, h, 0) + side * o, dir, dist, ShinFilter) < dist - 0.05f)
                        return true;
        }
        return steepest < WalkCheckNormalY;
    }

    /// <summary>
    /// A squeezed climb (Walk's "tight": a window sill under its lintel) a -> b over <paramref name="ledge"/>: does a crouched
    /// body fit on the ledge where the line crosses it (or 0.1 m to either side)? A line that clips a window's frame (a
    /// diagonal between two grid nodes either side of a narrow window) fails: the mover can sometimes slide along the frame
    /// into the opening from the exact node, but a bot arriving a little off the line pins against the wall beside the
    /// window, out of reach of the ledge, and never climbs (the Rooftops far walk's old flake). Such a direction is kept only
    /// as a last resort (TryLink), like one the mover can't walk.
    /// </summary>
    private static bool LedgeRoom(PhysicsWorld c, Vector3 a, Vector3 b, Vector3 ledge)
    {
        var h = World.HeightOf(Stance.Crouch);
        var p = ledge + new Vector3(0, 0.01f, 0);
        if (!c.BlockedNav(p, h))
            return true;
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        if (d.LengthSquared() < 1e-4f)
            return false;
        d = Vector2.Normalize(d);
        var side = new Vector3(-d.Y, 0, d.X) * 0.1f;
        return !c.BlockedNav(p + side, h) || !c.BlockedNav(p - side, h);
    }

    /// <summary>cos(40 degrees): terrain links steeper than this near the line get the mover walk (gentler ones can't slide).</summary>
    private const float WalkCheckNormalY = 0.76604444f;

    /// <summary>Feet right on the terrain (not on a box floor over it)?</summary>
    private static bool OnTerrain(Vector3 p) =>
        World.Map.TerrainCollision?.Layer(p.Y) is { } t && MathF.Abs(p.Y - t.HeightAt(p.X, p.Z)) < 0.1f;

    /// <summary>
    /// Does a body walking a -> b run into a shut building door? Bots open doors by walking into them (GameBuildings.cs) and the
    /// nav ignores doors, but the plain mover can't open one, so such links are left to the sampled checks.
    /// </summary>
    public static bool CrossesDoor(Map map, Vector3 a, Vector3 b)
    {
        if (map.BuildingDoors.Count == 0)
            return false;
        const float r = World.PlayerRadius + 0.05f;
        float y0 = MathF.Min(a.Y, b.Y), y1 = MathF.Max(a.Y, b.Y) + World.PlayerHeight;
        var asBuilt = World.NavBuildAsBuilt; // building the graph: doors as they start the match, not as they are now (PumpGraph)
        foreach (var d in map.BuildingDoors)
        {
            if (asBuilt ? d.Plan.StartOpen : d.Open || d.Broken)
                continue;
            var box = asBuilt ? d.Plan.Closed : map.Solid[d.SolidIndex];
            if (box.Max.Y < y0 || box.Min.Y > y1)
                continue;
            // Segment against the box grown by the body radius (XZ slabs).
            float t0 = 0f, t1 = 1f;
            float dx = b.X - a.X, dz = b.Z - a.Z;
            if (!Slab(a.X, dx, box.Min.X - r, box.Max.X + r, ref t0, ref t1) || !Slab(a.Z, dz, box.Min.Z - r, box.Max.Z + r, ref t0, ref t1))
                continue;
            return true;
        }
        return false;

        static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (MathF.Abs(d) < 1e-6f)
                return o >= lo && o <= hi;
            float ta = (lo - o) / d, tb = (hi - o) / d;
            if (ta > tb)
                (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            return t0 <= t1;
        }
    }

    /// <summary>One direction of a link waiting for its mover walk during the build: its slot in the edge list.</summary>
    private readonly record struct PendingWalk(int Slot, Vector3 A, Vector3 B, byte Flags, Vector3 Ledge, float Steepest);

    /// <summary>
    /// The walks a direction must pass: walking from its start; sprinting too where the ground is steep enough to slide on
    /// (bots do either; elsewhere sprinting walks the same); and for a real ledge (one climb of most of a mantle's reach, not
    /// stairs sampled as small mantles) walking from half a body width to either side of the start (bots seldom arrive dead
    /// on the node), so a ledge that only works from one exact spot isn't an edge.
    /// </summary>
    private static IEnumerable<(Vector3 From, bool Sprint)> WalkStarts(PendingWalk w)
    {
        var (a, b, flags, ledge) = (w.A, w.B, w.Flags, w.Ledge);
        yield return (a, false);
        if (w.Steepest < SlopeRules.MinWalkNormalY)
            yield return (a, true);
        if ((flags & FlagMantle) == 0 || ledge.Y - a.Y < 0.9f || b.Y - ledge.Y > 0.3f)
            yield break;
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        if (d.LengthSquared() < 1e-4f)
            yield break;
        d = Vector2.Normalize(d);
        for (var k = 0; k < 2; k++)
        {
            var side = k == 0 ? 0.45f : -0.45f;
            var p = new Vector3(a.X - d.Y * side, a.Y, a.Z + d.X * side);
            var t = World.Physics.Raycast(p + new Vector3(0, 0.4f, 0), -Vector3.UnitY, 0.8f, QueryFilter.Navigation);
            if (t >= 0.8f || MathF.Abs(0.4f - t) > 0.1f)
                continue; // no floor there at that height (an edge or a step): bots don't stand there
            p.Y += 0.4f - t;
            if (World.Physics.BlockedNav(p + new Vector3(0, 0.01f, 0)))
                continue;
            yield return (p, false);
        }
    }

    /// <summary>All of <see cref="WalkStarts"/> at once (for blasted holes, BotNavPatch.cs: a handful of links).</summary>
    private static bool MoverWalks(Vector3 a, Vector3 b, byte flags, Vector3 ledge, float steepest)
    {
        foreach (var (from, sprint) in WalkStarts(new PendingWalk(-1, a, b, flags, ledge, steepest)))
            if (!MoverWalk(from, b, flags, ledge, sprint).Reached)
                return false;
        return true;
    }

    /// <summary>
    /// The steepest terrain near the line a -> b, as the lowest support normal Y (1: flat, or no terrain under it). Edges past
    /// the full-speed angle get <see cref="FlagSlope"/>; past <see cref="WalkCheckNormalY"/> the mover walks them.
    /// </summary>
    public static float SteepestNormalY(Vector3 a, Vector3 b)
    {
        if (World.Map.TerrainCollision is not { } ground)
            return 1f;
        var t = ground.Layer(MathF.Min(a.Y, b.Y));
        var len = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
        var n = Math.Max(1, (int)MathF.Ceiling(len / 0.5f));
        var min = 1f;
        for (var k = 0; k <= n; k++)
        {
            var p = Vector3.Lerp(a, b, k / (float)n);
            if (MathF.Abs(p.Y - t.HeightAt(p.X, p.Z)) < 0.6f)
                min = MathF.Min(min, SlopeRules.SupportNormal(t, p.X, p.Z).Y);
        }
        return min;
    }
}
