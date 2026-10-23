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
    /// <summary>Links checked with the mover during the build, and directions dropped because the walk failed.</summary>
    public int WalkChecked, WalkRejected;

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
    /// <paramref name="ledge"/>) within 1.4 m. Reached is the bots' "at the node" rule (BotFollow), within the time a walk
    /// at the slowest uphill speed takes plus a second (and another for a mantle). Deterministic (mover tick 1).
    /// </summary>
    public static WalkResult MoverWalk(Vector3 a, Vector3 b, byte flags, Vector3 ledge, bool sprint)
    {
        var len = Vector3.Distance(a, b);
        var mantleEdge = (flags & FlagMantle) != 0;
        var limit = (int)((len / (World.WalkSpeed * SlopeRules.SlowAtLimit * 0.8f) + 1f + (mantleEdge ? 1f : 0f)) * World.TickRate);
        var s = new MoveState { Position = a, Grounded = true };
        var r = new WalkResult { Closest = float.MaxValue };
        var mover = ClassicMover.Instance;
        for (var t = 0; t < limit; t++)
        {
            var pos = s.Position;
            var dy = b.Y - pos.Y;
            var dx = b.X - pos.X;
            var dz = b.Z - pos.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
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
    /// Does a link a -> b need the mover's say (<see cref="MoverWalk"/>)? Mantles and drops, height changes beyond a small
    /// step, a body sample squeezed past a ledge (<paramref name="tight"/>) and, on terrain, slopes past the full-speed
    /// angle near the line. Plain level walks are left to the sampled checks.
    /// </summary>
    private static bool NeedsMoverWalk(Vector3 a, Vector3 b, byte flags, bool tight)
    {
        if (CrossesDoor(World.Map, a, b))
            return false; // through a shut door: bots open it, the plain mover can't (see CrossesDoor)
        if (flags != 0 || tight || MathF.Abs(a.Y - b.Y) > 0.3f)
            return true;
        // Something low in the way the samples stepped past (a thin kerb or rail under the body check's height, a step under
        // a shelf): a ray at shin height along the link.
        var shin = new Vector3(0, 0.3f, 0);
        var d = b - a;
        var dist = d.Length();
        if (dist > 0.01f && World.Physics.Raycast(a + shin, d / dist, dist, QueryFilter.Navigation) < dist - 0.05f)
            return true;
        if (World.Map.TerrainCollision is not { } ground)
            return false;
        var t = ground.Layer(MathF.Min(a.Y, b.Y));
        var len = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
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
    /// Does a body walking a -> b run into a shut building door? Bots open doors by walking into them (GameBuildings.cs) and the
    /// nav ignores doors, but the plain mover can't open one, so such links are left to the sampled checks.
    /// </summary>
    public static bool CrossesDoor(Map map, Vector3 a, Vector3 b)
    {
        if (map.BuildingDoors.Count == 0)
            return false;
        const float r = World.PlayerRadius + 0.05f;
        float y0 = MathF.Min(a.Y, b.Y), y1 = MathF.Max(a.Y, b.Y) + World.PlayerHeight;
        foreach (var d in map.BuildingDoors)
        {
            if (d.Open || d.Broken)
                continue;
            var box = map.Solid[d.SolidIndex];
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

    /// <summary>Walking and sprinting both get there (bots do either).</summary>
    private static bool MoverWalks(Vector3 a, Vector3 b, byte flags, Vector3 ledge) =>
        MoverWalk(a, b, flags, ledge, sprint: false).Reached && MoverWalk(a, b, flags, ledge, sprint: true).Reached;
}
