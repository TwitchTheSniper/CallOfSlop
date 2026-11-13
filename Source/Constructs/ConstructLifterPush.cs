using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A lifter's piece in the air pushes players out of its way (arms-and-cranes.md 3.6, the user's answer 2), and nothing else:
/// it still passes through vehicles, walls and other pieces. For players the piece is a moving box, its true size, posed at
/// the command's tick by the path (<see cref="ConstructLifters.AgeAt"/>: a pure function of the mover clock), so the host and
/// a client's own prediction and replay push alike. Run after the constructs' own block in the shared movement step
/// (Game.SandboxPlayer).
/// <list type="bullet">
/// <item>A player the box overlaps is lifted onto it when its top is a step (within <see cref="StepUp"/> of the feet), else
/// pushed out sideways by the smallest move, or out another side when that one is blocked (at most <see cref="MaxEscape"/>:
/// never a teleport).</item>
/// <item><b>Pinned.</b> A push that would leave the player in the map, a construct or another moving piece is not made. On
/// the server the lifter then waits (<see cref="ConstructLifters.Pin"/>): its move stops where it is on both peers (the
/// lift word's WaitFrom) and goes on once no player is within <see cref="Clearance"/> of the stopped piece; after
/// <see cref="StuckTicks"/> it raises its Alarm. Nobody is crushed, teleported or put on top.</item>
/// <item>A client that predicts against a move the server has since stopped is pushed by it for a round trip at most;
/// reconciliation puts it right.</item>
/// </list>
/// Cost: nothing while no piece is in the air; else per player per command tick a distance test a carrying lifter and a box
/// test for those near. Nothing allocates (a fixed table of posed boxes).
/// </summary>
public static class ConstructLifterPush
{
    public const int StuckTicks = 10 * World.TickRate; // a wait for a player this long raises the Alarm
    public const float Clearance = 0.1f; // the wait ends when no player is within this of the stopped piece
    public const float MaxEscape = 0.75f; // a way out of another side longer than this would be a teleport: the arm waits instead
    public const float StepUp = 0.4f; // a piece's top this close over the feet is stood on, not pushed from
    private const float Lift = 0.3f; // the room checks start this far over the feet (a slope or a kerb is not a wall)
    private const int MaxBoxes = 64;

    private struct Posed
    {
        public Vector3 Centre, Half, World; // the box's middle, its half size, its world bounds' half size
        public Quaternion Rot;
        public CLift Lift;
    }

    private static readonly Posed[] Boxes = new Posed[MaxBoxes];
    private static int _count;
    private static bool _mapAlready, _bodyAlready; // (this Block call: the player's own spot already overlaps the map, a construct)

    /// <summary>For the window tool: pushes made and pins found since start (this peer).</summary>
    public static int Pushes, Pins;

    /// <summary>For the window tool: the push's own cost on this peer (calls, Stopwatch ticks), with lifters in the world.</summary>
    public static long BlockCalls, BlockTotal, BlockWorst;

    private static void Time(long t0)
    {
        var dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        BlockTotal += dt;
        BlockWorst = Math.Max(BlockWorst, dt);
    }

    /// <summary>For the window tool: what the last pin found in the way.</summary>
    public static string LastBlock = "";

    private static bool Blocked(string what)
    {
        LastBlock = what;
        return false;
    }

    /// <summary>
    /// After a player's movement step (server, and a client's prediction and replay): out of every lifter's piece in the air
    /// at <paramref name="tick"/>. <paramref name="server"/>: a pin holds the lifter.
    /// </summary>
    public static void Block(ConstructWorld world, ref MoveState s, float height, uint tick, bool server)
    {
        if (world.LiftBodies.Count == 0 || s.RideUp != Vector3.Zero)
            return; // (contraption space: a rider moves in its construct's frame, and a piece passes through constructs)
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var n = Gather(world, tick, s.Position, height);
        BlockCalls++;
        if (n == 0)
        {
            Time(t0);
            return;
        }
        var based = false;
        for (var iter = 0; iter < 2; iter++)
        {
            var any = false;
            for (var i = 0; i < n; i++)
            {
                ref readonly var b = ref Boxes[i];
                if (b.Lift.WaitFrom != 0)
                    continue; // (a stopped piece pushes nobody: the player it waits for walks out of it; and a pin costs no room checks a tick)
                if (!Overlap(b, s.Position, height, World.PlayerRadius, out var push))
                    continue;
                any = true;
                if (!based)
                {
                    // What already overlaps where they stand (a ledge they stand on, a part they lean in) is not a wall a push meets.
                    var raised = s.Position with { Y = s.Position.Y + Lift };
                    var h = MathF.Max(0.3f, height - Lift);
                    _mapAlready = MoverPhysics.BlockedAll(raised, h, tick);
                    _bodyAlready = world.BodyIn(raised, h - 0.02f);
                    based = true;
                }
                var top = b.Centre.Y + b.World.Y;
                if (top - s.Position.Y <= StepUp && Free(world, s.Position with { Y = top + 0.001f }, height, tick, i, n))
                {
                    s.Position.Y = top + 0.001f; // (a step: onto it; it moves on from under them, never carrying them)
                    s.Grounded = true;
                    if (s.Velocity.Y < 0)
                        s.Velocity.Y = 0;
                    Pushes++;
                    continue;
                }
                if (!Free(world, s.Position + push, height, tick, i, n) && !Escape(world, b, s.Position, height, tick, i, n, ref push))
                {
                    Pins++;
                    if (Pins == 1)
                        Console.WriteLine($"[lifter] pinned: feet {s.Position:0.00}, push {push:0.00}, last in the way: {LastBlock}");
                    if (server)
                        ConstructLifters.Pin(world, b.Lift, tick);
                    continue; // (pinned: not pushed; on the server the arm waits)
                }
                s.Position += push;
                var len = MathF.Sqrt(push.X * push.X + push.Z * push.Z);
                if (len > 1e-5f)
                {
                    var nx = push.X / len;
                    var nz = push.Z / len;
                    var into = s.Velocity.X * nx + s.Velocity.Z * nz;
                    if (into < 0)
                    {
                        s.Velocity.X -= nx * into;
                        s.Velocity.Z -= nz * into;
                    }
                }
                Pushes++;
            }
            if (!any)
                break;
        }
        Time(t0);
    }

    /// <summary>Server, while a lifter waits for a player: is everyone clear of its stopped piece (by <see cref="Clearance"/>)?</summary>
    public static bool Clear(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (!Pose(c, l, ld, ConstructLifters.AgeAt(l, ConstructLifters.Now), out var b))
            return true;
        for (var k = 0; k < world.PlayerSpotCount; k++)
            if (Overlap(b, world.PlayerSpots[k], World.PlayerHeight, World.PlayerRadius + Clearance, out _))
                return false;
        return true;
    }

    /// <summary>The window tool: the piece in a hand as a world box (its middle and its world bounds' half size), <paramref name="age"/> ticks into the move.</summary>
    public static bool PieceBox(Construct c, CLift l, LifterDef ld, float age, out Vector3 centre, out Vector3 half)
    {
        var ok = Pose(c, l, ld, age, out var b);
        (centre, half) = (b.Centre, b.World);
        return ok;
    }

    /// <summary>The pieces in the air near the feet, posed at a tick, into <see cref="Boxes"/>.</summary>
    private static int Gather(ConstructWorld world, double tick, Vector3 feet, float height)
    {
        _count = 0;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            Construct? c = null;
            for (var i = 0; i < lb.N && _count < MaxBoxes; i++)
            {
                var l = lb.Lifts[i];
                if (l.State != LiftState.Carry || l.Def == 0)
                    continue;
                c ??= world.Get(lb.Body);
                if (c == null || l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } ld)
                    break;
                ConstructLifters.Frame(c, l.Part, ld, out _, out _, out var shoulder);
                var range = ld.ReachMax + ld.PieceLong + ld.Up + height + 1f; // (cheap: no piece of this lifter is further)
                if (Vector3.DistanceSquared(shoulder, feet) > range * range)
                    continue;
                if (Pose(c, l, ld, ConstructLifters.AgeAt(l, tick), out Boxes[_count]))
                    _count++;
            }
        }
        return _count;
    }

    /// <summary>The piece in a lifter's hand as a world box, <paramref name="age"/> ticks into its move.</summary>
    private static bool Pose(Construct c, CLift l, LifterDef ld, float age, out Posed b)
    {
        b = default;
        if (ConstructParts.Get(l.Def) is not { } d)
            return false;
        Freeform.PartWorld(c, l.Part, out var lp, out var lr);
        var origin = ConstructLifters.Pose(ld, l, age, out var turn);
        var q = Quaternion.Normalize(lr * turn);
        var o = lp + Vector3.Transform(origin, lr);
        b.Centre = o + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, q);
        b.Half = (d.BoundsMax - d.BoundsMin) * 0.5f;
        b.Rot = q;
        var m = Matrix4x4.CreateFromQuaternion(q);
        b.World = new Vector3(
            MathF.Abs(m.M11) * b.Half.X + MathF.Abs(m.M21) * b.Half.Y + MathF.Abs(m.M31) * b.Half.Z,
            MathF.Abs(m.M12) * b.Half.X + MathF.Abs(m.M22) * b.Half.Y + MathF.Abs(m.M32) * b.Half.Z,
            MathF.Abs(m.M13) * b.Half.X + MathF.Abs(m.M23) * b.Half.Y + MathF.Abs(m.M33) * b.Half.Z);
        b.Lift = l;
        return true;
    }

    /// <summary>
    /// Does a standing body (feet, height, radius) overlap the box? <paramref name="push"/>: the smallest sideways move out,
    /// tested at three heights over the part of the body the box spans.
    /// </summary>
    private static bool Overlap(in Posed b, Vector3 feet, float height, float r, out Vector3 push)
    {
        push = default;
        var top = b.Centre.Y + b.World.Y;
        var bottom = b.Centre.Y - b.World.Y;
        if (feet.Y >= top - 0.001f || feet.Y + height <= bottom + 0.001f)
            return false;
        if (MathF.Abs(feet.X - b.Centre.X) >= b.World.X + r || MathF.Abs(feet.Z - b.Centre.Z) >= b.World.Z + r)
            return false;
        var lo = MathF.Max(feet.Y + 0.05f, bottom + 0.01f);
        var hi = MathF.Min(feet.Y + height - 0.05f, top - 0.01f);
        if (hi < lo)
            hi = lo;
        var iq = Quaternion.Conjugate(b.Rot);
        var best = 0f;
        var hit = false;
        for (var k = 0; k < 3; k++)
        {
            var probe = new Vector3(feet.X, lo + (hi - lo) * k * 0.5f, feet.Z);
            var lpos = Vector3.Transform(probe - b.Centre, iq);
            var closest = Vector3.Clamp(lpos, -b.Half, b.Half);
            var wc = b.Centre + Vector3.Transform(closest, b.Rot);
            var dx = probe.X - wc.X;
            var dz = probe.Z - wc.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= r * r && Vector3.DistanceSquared(closest, lpos) > 1e-8f)
                continue; // (clear at this height: beside it, or under or over it only)
            Vector3 p;
            if (Vector3.DistanceSquared(closest, lpos) < 1e-8f || d2 < 1e-8f)
            {
                if (!Side(b, lpos, r, -1, out p))
                    continue; // (no side leads out sideways: a piece flat over the head; the step test or the pin covers it)
            }
            else
            {
                var d = MathF.Sqrt(d2);
                p = new Vector3(dx / d * (r - d), 0, dz / d * (r - d));
            }
            var len = p.LengthSquared();
            if (!hit || len > best)
                (push, best, hit) = (p, len, true);
        }
        if (hit)
            push += Vector3.Normalize(push with { Y = 0 } + new Vector3(1e-6f, 0, 0)) * 0.002f; // (a hair past the edge)
        return hit;
    }

    /// <summary>
    /// Out of the box through one of its sides that faces sideways: the nearest (<paramref name="only"/> -1) or side
    /// <paramref name="only"/> (0 to 5: an axis twice, plus then minus). False when that side does not lead sideways.
    /// </summary>
    private static bool Side(in Posed b, Vector3 lpos, float r, int only, out Vector3 push)
    {
        push = default;
        var best = float.MaxValue;
        for (var s = 0; s < 6; s++)
        {
            if (only >= 0 && s != only)
                continue;
            var axis = s >> 1;
            var sign = (s & 1) == 0 ? 1f : -1f;
            var unit = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            var w = Vector3.Transform(unit, b.Rot) * sign;
            var flat = MathF.Sqrt(w.X * w.X + w.Z * w.Z);
            if (flat < 0.3f)
                continue;
            var at = axis == 0 ? lpos.X : axis == 1 ? lpos.Y : lpos.Z;
            var half = axis == 0 ? b.Half.X : axis == 1 ? b.Half.Y : b.Half.Z;
            var dist = MathF.Max(0, half - at * sign) * flat + r; // (out through that face, then the body's radius, along the ground)
            if (dist >= best)
                continue;
            best = dist;
            push = new Vector3(w.X / flat * dist, 0, w.Z / flat * dist);
        }
        return best < float.MaxValue;
    }

    /// <summary>The shortest way out is blocked: out through another side that faces sideways, the nearest free one within <see cref="MaxEscape"/>.</summary>
    private static bool Escape(ConstructWorld world, in Posed b, Vector3 feet, float height, uint tick, int self, int n, ref Vector3 push)
    {
        var lpos = Vector3.Transform(new Vector3(feet.X, feet.Y + height * 0.5f, feet.Z) - b.Centre, Quaternion.Conjugate(b.Rot));
        Span<Vector3> ways = stackalloc Vector3[6];
        var count = 0;
        for (var s = 0; s < 6; s++)
            if (Side(b, lpos, World.PlayerRadius, s, out var p) && p.LengthSquared() <= MaxEscape * MaxEscape)
                ways[count++] = p;
        for (var k = 0; k < count; k++)
        {
            var at = k;
            for (var j = k + 1; j < count; j++)
                if (ways[j].LengthSquared() < ways[at].LengthSquared())
                    at = j;
            (ways[k], ways[at]) = (ways[at], ways[k]);
            if (Free(world, feet + ways[k], height, tick, self, n))
            {
                push = ways[k];
                return true;
            }
        }
        return false;
    }

    /// <summary>Is there room for the body here: clear of the map, of every construct and of the other pieces in the air?</summary>
    private static bool Free(ConstructWorld world, Vector3 feet, float height, uint tick, int self, int n)
    {
        var raised = feet with { Y = feet.Y + Lift }; // (a slope or a kerb under the pushed feet is not a wall: the mover steps them up)
        var h = MathF.Max(0.3f, height - Lift);
        if (!_mapAlready && MoverPhysics.BlockedAll(raised, h, tick))
            return Blocked($"the map at {raised:0.00}");
        if (!_bodyAlready && world.BodyIn(raised, h - 0.02f))
            return Blocked($"a construct at {raised:0.00}");
        for (var i = 0; i < n; i++)
            if (i != self && Overlap(Boxes[i], raised, h, World.PlayerRadius, out _))
                return Blocked("another piece");
        return true;
    }
}
