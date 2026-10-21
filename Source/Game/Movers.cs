using System.Numerics;

namespace CallOfSlop;

/// <summary>One mover box placed at a pose: world centre, half size and the box's world yaw (as cos/sin).</summary>
public struct PosedBox
{
    public Vector3 Center;
    public Vector3 Half;
    public float Cos, Sin;
    public int Mover; // index into Map.Movers

    public readonly float Top => Center.Y + Half.Y;
    public readonly float Bottom => Center.Y - Half.Y;
}

/// <summary>
/// Collision, carrying and ray casts for movers (the map's moving solids, see <see cref="MoverDef"/>).
///
/// <para><b>Clock.</b> A mover's pose is a pure function of the server tick. Every player command carries the tick
/// it's simulated at (<see cref="InputCmd.Tick"/>): the client stamps its predicted commands with its estimate of
/// the server tick that will process them and the server simulates them at that same tick, so prediction, replay
/// during reconciliation and the server all see the platform in exactly the same place. Bots and the host's own
/// player use <see cref="Clock"/> (the server's current tick). Ray casts and grenades also use <see cref="Clock"/>.</para>
///
/// <para><b>Standing on a mover.</b> The player's feet are held in the platform's frame: each tick, if the player
/// stood on a mover at the previous tick's pose, their feet are moved by the platform's motion
/// (<c>pose(t) * pose(t-1)^-1</c>, a parent transform, so turning platforms carry you round their pivot). The player's
/// velocity is relative to the platform while on it: stepping or jumping off adds the platform's velocity at your
/// feet (you keep its momentum), landing on one subtracts it. Nothing extra is stored in <see cref="MoveState"/>,
/// so snapshots and the network format don't change.</para>
///
/// <para><b>Pushing.</b> Movers are oriented boxes (turning about the vertical only). After the normal box sweep, a
/// player overlapping a mover box is lifted onto it if it's a step (or it rose into them) and otherwise pushed out
/// sideways. Anyone who'd end up inside static geometry is put on top of the box or nudged upwards: nobody is ever
/// crushed or trapped.</para>
/// </summary>
public static class MoverPhysics
{
    /// <summary>
    /// The tick ray casts, grenades and commands without their own tick use: the server's current tick on the host,
    /// the latest predicted command's tick on a client.
    /// </summary>
    public static uint Clock;

    /// <summary>
    /// The local player's view turns with a rotating platform they stand on (like the body does), so the world
    /// doesn't spin around you. A per-frame camera effect only; the server just receives the turned yaw.
    /// </summary>
    public static readonly bool TurnViewWithPlatform = true;

    /// <summary>Feet within this of a box top count as standing on it.</summary>
    public const float SupportTolerance = 0.06f;

    // ---- Posed boxes, cached for the last few ticks ---------------------------------------------------------

    private sealed class Slot
    {
        public Map? Map;
        public double Tick = double.NaN;
        public PosedBox[] Boxes = [];
        public int Count;
    }

    private static readonly Slot[] Slots = [new(), new(), new(), new()];
    private static int _nextSlot;

    /// <summary>Every mover box of the current map at <paramref name="tick"/>.</summary>
    public static ReadOnlySpan<PosedBox> At(double tick) => At(World.Map, tick);

    public static ReadOnlySpan<PosedBox> At(Map map, double tick)
    {
        foreach (var s in Slots)
            if (ReferenceEquals(s.Map, map) && s.Tick == tick)
                return s.Boxes.AsSpan(0, s.Count);
        var slot = Slots[_nextSlot];
        _nextSlot = (_nextSlot + 1) % Slots.Length;
        var total = 0;
        foreach (var m in map.Movers)
            total += m.Parts.Length;
        if (slot.Boxes.Length < total)
            slot.Boxes = new PosedBox[total];
        slot.Count = Pose(map, tick, slot.Boxes);
        slot.Map = map;
        slot.Tick = tick;
        return slot.Boxes.AsSpan(0, slot.Count);
    }

    /// <summary>Places every mover box of <paramref name="map"/> at <paramref name="tick"/> into <paramref name="into"/>; returns the count.</summary>
    public static int Pose(Map map, double tick, Span<PosedBox> into)
    {
        var n = 0;
        for (var mi = 0; mi < map.Movers.Count; mi++)
        {
            var m = map.Movers[mi];
            var pose = m.PoseAt(tick);
            foreach (var p in m.Parts)
            {
                var (s, c) = MathF.SinCos(pose.Yaw + p.Yaw);
                into[n++] = new PosedBox { Center = pose.ToWorld(p.Center), Half = p.Half, Cos = c, Sin = s, Mover = mi };
            }
        }
        return n;
    }

    // ---- Shape tests ------------------------------------------------------------------------------------------

    /// <summary>
    /// Does a vertical cylinder of radius <paramref name="r"/> at (x, z) overlap the box horizontally? If so,
    /// <paramref name="push"/> is the smallest horizontal (x, z) move that gets it out.
    /// </summary>
    public static bool CircleOverlap(in PosedBox b, float x, float z, float r, out Vector2 push)
    {
        float dx = x - b.Center.X, dz = z - b.Center.Z;
        float lx = dx * b.Cos + dz * b.Sin, lz = -dx * b.Sin + dz * b.Cos;
        float hx = b.Half.X, hz = b.Half.Z;
        float ox = lx - Math.Clamp(lx, -hx, hx), oz = lz - Math.Clamp(lz, -hz, hz);
        var d2 = ox * ox + oz * oz;
        float px, pz;
        if (d2 > 0)
        {
            if (d2 >= r * r)
            {
                push = default;
                return false;
            }
            var d = MathF.Sqrt(d2);
            px = ox / d * (r - d);
            pz = oz / d * (r - d);
        }
        else
        {
            float penX = hx - MathF.Abs(lx) + r, penZ = hz - MathF.Abs(lz) + r;
            if (penX < penZ)
            {
                px = lx < 0 ? -penX : penX;
                pz = 0;
            }
            else
            {
                px = 0;
                pz = lz < 0 ? -penZ : penZ;
            }
        }
        push = new Vector2(px * b.Cos - pz * b.Sin, px * b.Sin + pz * b.Cos);
        return true;
    }

    /// <summary>Ray against one posed box: distance along the (normalised) ray, or null.</summary>
    public static float? Raycast(in PosedBox b, Vector3 origin, Vector3 dir, float maxDist)
    {
        var d = origin - b.Center;
        var lo = new Vector3(d.X * b.Cos + d.Z * b.Sin, d.Y, -d.X * b.Sin + d.Z * b.Cos);
        var ld = new Vector3(dir.X * b.Cos + dir.Z * b.Sin, dir.Y, -dir.X * b.Sin + dir.Z * b.Cos);
        return new Box(Vector3.Zero, b.Half).Raycast(lo, ld, maxDist);
    }

    /// <summary>Nearest mover hit along a ray at <paramref name="tick"/>, or <paramref name="maxDist"/>.</summary>
    public static float Raycast(Vector3 origin, Vector3 dir, float maxDist, double tick)
    {
        var best = maxDist;
        foreach (ref readonly var b in At(tick))
            if (Raycast(b, origin, dir, best) is { } t && t < best)
                best = t;
        return best;
    }

    /// <summary>Is a small sphere (grenade) at <paramref name="p"/> inside a mover box at <paramref name="tick"/>?</summary>
    public static bool PointInside(Vector3 p, float r, double tick)
    {
        foreach (ref readonly var b in At(tick))
        {
            var d = p - b.Center;
            float lx = d.X * b.Cos + d.Z * b.Sin, lz = -d.X * b.Sin + d.Z * b.Cos;
            if (MathF.Abs(lx) < b.Half.X + r && MathF.Abs(d.Y) < b.Half.Y + r && MathF.Abs(lz) < b.Half.Z + r)
                return true;
        }
        return false;
    }

    /// <summary>Would a player standing at <paramref name="feet"/> overlap a mover box?</summary>
    public static bool Overlaps(Vector3 feet, float height, double tick)
    {
        foreach (ref readonly var b in At(tick))
            if (feet.Y < b.Top - 0.001f && feet.Y + height > b.Bottom + 0.001f && CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out _))
                return true;
        return false;
    }

    /// <summary>Blocked by static geometry or a mover (movement only; bot navigation uses <see cref="World.Blocked"/>).</summary>
    public static bool BlockedAll(Vector3 feet, float height, double tick) =>
        World.Physics.OverlapBody(feet, height, new QueryFilter(QueryLayers.Static | QueryLayers.Movers, tick));

    /// <summary>Index of the box the feet stand on at <paramref name="tick"/> (the highest top within tolerance), or -1.</summary>
    public static int SupportBox(ReadOnlySpan<PosedBox> boxes, Vector3 feet, float tolerance = SupportTolerance)
    {
        var best = -1;
        for (var i = 0; i < boxes.Length; i++)
        {
            ref readonly var b = ref boxes[i];
            if (MathF.Abs(feet.Y - b.Top) > tolerance || (best >= 0 && b.Top <= boxes[best].Top))
                continue;
            if (CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out _))
                best = i;
        }
        return best;
    }

    /// <summary>
    /// The mover box carrying someone at <paramref name="feet"/>: the box they stand on, unless the point under their
    /// centre is static floor at the same height or higher (on a ledge beside a platform, the ledge wins).
    /// </summary>
    public static int CarrierBox(ReadOnlySpan<PosedBox> boxes, Vector3 feet)
    {
        var i = SupportBox(boxes, feet);
        if (i < 0)
            return -1;
        var top = boxes[i].Top;
        var phys = World.Physics;
        var near = new Vector3(0.001f, SupportTolerance + 0.001f, 0.001f);
        foreach (var k in phys.CollectBoxes(feet - near, feet + near))
        {
            var b = phys.Boxes[k];
            if (feet.X >= b.Min.X && feet.X <= b.Max.X && feet.Z >= b.Min.Z && feet.Z <= b.Max.Z && MathF.Abs(feet.Y - b.Max.Y) <= SupportTolerance && b.Max.Y >= top - 0.001f)
                return -1;
        }
        return i;
    }

    /// <summary>Which mover (index into <see cref="Map.Movers"/>) carries someone at <paramref name="feet"/> at <paramref name="tick"/>, or -1.</summary>
    public static int SupportMover(Vector3 feet, double tick)
    {
        if (World.Map.Movers.Count == 0)
            return -1;
        var boxes = At(tick);
        var i = CarrierBox(boxes, feet);
        return i < 0 ? -1 : boxes[i].Mover;
    }

    /// <summary>Velocity of the mover's material at <paramref name="feet"/> over the tick ending at <paramref name="tick"/>.</summary>
    public static Vector3 PointVelocity(int mover, Vector3 feet, uint tick)
    {
        if (tick == 0)
            return Vector3.Zero;
        var m = World.Map.Movers[mover];
        var p0 = m.PoseAt(tick - 1);
        var p1 = m.PoseAt(tick);
        var local = p1.ToLocal(feet);
        return (p1.ToWorld(local) - p0.ToWorld(local)) / World.TickDt;
    }

    // ---- The tick: carry, collide, momentum ----------------------------------------------------------------

    /// <summary>
    /// Start of a player's tick at <paramref name="tick"/>: if they stood on a mover at the previous tick's pose,
    /// carry their feet with it. Returns the mover (or -1) and the platform's velocity at their feet.
    /// </summary>
    public static int BeginTick(ref MoveState s, uint tick, float height, out Vector3 platformVel)
    {
        platformVel = Vector3.Zero;
        if (!s.Grounded || tick == 0)
            return -1;
        var boxes = At(tick - 1);
        var i = CarrierBox(boxes, s.Position);
        if (i < 0)
            return -1;
        var mover = boxes[i].Mover;
        var m = World.Map.Movers[mover];
        var start = s.Position;
        var p0 = m.PoseAt(tick - 1);
        var p1 = m.PoseAt(tick);
        var to = p1.ToWorld(p0.ToLocal(start));
        // Velocity on a platform is relative to it, so it turns with the platform too.
        var (sn, cs) = MathF.SinCos(World.WrapAngle(p1.Yaw - p0.Yaw));
        s.Velocity = new Vector3(s.Velocity.X * cs - s.Velocity.Z * sn, s.Velocity.Y, s.Velocity.X * sn + s.Velocity.Z * cs);
        platformVel = (to - start) / World.TickDt;
        // A wall the platform slides past holds the player back (the platform moves on under them).
        if (World.BlockedByBoxes(to, height))
        {
            to = start with { Y = to.Y };
            if (World.BlockedByBoxes(to, height))
                to = start;
        }
        s.Position = to;
        return mover;
    }

    /// <summary>End of a player's tick: keep the platform's momentum when leaving it, and move into its frame when landing on one.</summary>
    public static void EndTick(ref MoveState s, uint tick, int carried, Vector3 platformVel)
    {
        var now = s.Grounded ? SupportMover(s.Position, tick) : -1;
        if (carried >= 0 && now != carried)
        {
            s.Velocity.X += platformVel.X;
            s.Velocity.Z += platformVel.Z;
            if (!s.Grounded && platformVel.Y > 0)
                s.Velocity.Y += platformVel.Y; // jumping off a rising lift goes higher
        }
        if (now >= 0 && now != carried)
        {
            var v = PointVelocity(now, s.Position, tick);
            s.Velocity.X -= v.X;
            s.Velocity.Z -= v.Z;
        }
    }

    /// <summary>The vertical part of <see cref="ClassicMover"/>'s move against mover boxes: land on tops, bump heads on bottoms.</summary>
    public static void Vertical(Vector3 feet, float prevY, ref float newY, ref Vector3 velocity, ref bool grounded, float height, uint tick)
    {
        foreach (ref readonly var b in At(tick))
        {
            if (!CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out _))
                continue;
            if (velocity.Y <= 0 && prevY >= b.Top - 0.02f && newY < b.Top)
            {
                newY = b.Top;
                velocity.Y = 0;
                grounded = true;
            }
            else if (velocity.Y > 0 && prevY + height <= b.Bottom + 0.02f && newY + height > b.Bottom)
            {
                newY = b.Bottom - height;
                velocity.Y = 0;
            }
        }
    }

    /// <summary>
    /// After the horizontal box sweep: get the player out of any mover box they overlap. Low boxes (or a platform
    /// that rose into them) lift them on top; anything else pushes them out sideways and stops their motion into it.
    /// </summary>
    public static Vector3 PushOut(Vector3 feet, ref Vector3 velocity, ref bool grounded, float height, uint tick)
    {
        var boxes = At(tick);
        for (var iter = 0; iter < 4; iter++)
        {
            var any = false;
            foreach (ref readonly var b in boxes)
            {
                if (feet.Y >= b.Top - 0.001f || feet.Y + height <= b.Bottom + 0.001f)
                    continue;
                if (!CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out var push))
                    continue;
                any = true;
                var rise = b.Top - feet.Y;
                if (rise <= (grounded ? World.StepHeight : 0.3f) && !BlockedAll(feet with { Y = b.Top }, height, tick))
                {
                    feet.Y = b.Top; // a step, or the platform came up under us
                    grounded = true;
                    if (velocity.Y < 0)
                        velocity.Y = 0;
                    continue;
                }
                push = EscapePush(b, feet, height, push);
                feet.X += push.X + MathF.Sign(push.X) * 0.001f;
                feet.Z += push.Y + MathF.Sign(push.Y) * 0.001f;
                var len = push.Length();
                if (len > 1e-6f)
                {
                    var n = push / len;
                    var into = velocity.X * n.X + velocity.Z * n.Y;
                    if (into < 0)
                    {
                        velocity.X -= n.X * into;
                        velocity.Z -= n.Y * into;
                    }
                }
            }
            if (!any)
                break;
        }
        return Unstick(feet, height, tick);
    }

    /// <summary>
    /// The way out of a mover box that doesn't shove the player into static geometry: the shortest push first,
    /// then out through each of the box's four sides, nearest first. All blocked: the shortest (then Unstick).
    /// </summary>
    private static Vector2 EscapePush(in PosedBox b, Vector3 feet, float height, Vector2 shortest)
    {
        if (!World.BlockedByBoxes(feet + new Vector3(shortest.X * 1.01f, 0, shortest.Y * 1.01f), height))
            return shortest;
        float dx = feet.X - b.Center.X, dz = feet.Z - b.Center.Z;
        float lx = dx * b.Cos + dz * b.Sin, lz = -dx * b.Sin + dz * b.Cos;
        var r = World.PlayerRadius;
        Span<Vector2> local = [new(b.Half.X + r - lx, 0), new(-b.Half.X - r - lx, 0), new(0, b.Half.Z + r - lz), new(0, -b.Half.Z - r - lz)];
        local.Sort((p, q) => p.LengthSquared().CompareTo(q.LengthSquared()));
        foreach (var l in local)
        {
            var w = new Vector2(l.X * b.Cos - l.Y * b.Sin, l.X * b.Sin + l.Y * b.Cos);
            if (!World.BlockedByBoxes(feet + new Vector3(w.X * 1.01f, 0, w.Y * 1.01f), height))
                return w;
        }
        return shortest;
    }

    /// <summary>
    /// Never crushed: if the feet are inside static geometry or a mover, put them on top of the mover box they're in,
    /// or else nudge them up until there's room (up to 4 m).
    /// </summary>
    public static Vector3 Unstick(Vector3 feet, float height, uint tick)
    {
        if (!BlockedAll(feet, height, tick))
            return feet;
        foreach (ref readonly var b in At(tick))
        {
            if (feet.Y >= b.Top || feet.Y + height <= b.Bottom || !CircleOverlap(b, feet.X, feet.Z, World.PlayerRadius, out _))
                continue;
            var top = feet with { Y = b.Top + 0.001f };
            if (!BlockedAll(top, height, tick))
                return top;
        }
        for (var up = 0.25f; up <= 4f; up += 0.25f)
        {
            var p = feet with { Y = feet.Y + up };
            if (!BlockedAll(p, height, tick))
                return p;
        }
        return feet;
    }

    /// <summary>How much a player standing where <paramref name="s"/> says turns over the tick ending at <paramref name="tick"/>.</summary>
    public static float SupportYawDelta(in MoveState s, uint tick)
    {
        if (!s.Grounded || tick == 0 || World.Map.Movers.Count == 0)
            return 0;
        var boxes = At(tick - 1);
        var i = CarrierBox(boxes, s.Position);
        if (i < 0)
            return 0;
        var m = World.Map.Movers[boxes[i].Mover];
        return World.WrapAngle(m.PoseAt(tick).Yaw - m.PoseAt(tick - 1).Yaw);
    }

    /// <summary>
    /// Someone seen standing on a mover at <paramref name="fromTick"/> (a remote player drawn in the past, or a
    /// lag-compensated hitbox), moved with it to <paramref name="toTick"/>, so they stay on the platform as it's drawn.
    /// </summary>
    public static Vector3 CarryBetween(Vector3 feet, double fromTick, double toTick, ref float yaw)
    {
        var map = World.Map;
        if (map.Movers.Count == 0 || fromTick == toTick)
            return feet;
        var boxes = At(map, fromTick); // the same render tick for every player in a frame, so this is cached
        var i = SupportBox(boxes, feet, 0.15f);
        if (i < 0)
            return feet;
        var m = map.Movers[boxes[i].Mover];
        var p0 = m.PoseAt(fromTick);
        var p1 = m.PoseAt(toTick);
        yaw = World.WrapAngle(yaw + World.WrapAngle(p1.Yaw - p0.Yaw));
        return p1.ToWorld(p0.ToLocal(feet));
    }

    // ---- Bots: each mover's whole sweep is a no-go pillar ----------------------------------------------------

    private static Map? _sweepMap;
    private static Box[] _sweeps = [];

    /// <summary>Sweep pillars of the current map: the mover's whole reach, widened a little, from its lowest point to the sky.</summary>
    public static ReadOnlySpan<Box> Sweeps
    {
        get
        {
            var map = World.Map;
            if (!ReferenceEquals(_sweepMap, map))
            {
                _sweeps = SweepPillars(map);
                _sweepMap = map;
            }
            return _sweeps;
        }
    }

    /// <summary>A map's sweep pillars (see <see cref="Sweeps"/>); the physics world keeps its own copy for <see cref="QueryLayers.MoverSweeps"/>.</summary>
    public static Box[] SweepPillars(Map map) =>
        map.Movers.Select(m =>
        {
            var s = m.Sweep;
            var margin = World.PlayerRadius + 0.2f;
            var lo = new Vector3(s.Min.X - margin, s.Min.Y, s.Min.Z - margin);
            var hi = new Vector3(s.Max.X + margin, 200f, s.Max.Z + margin);
            return new Box((lo + hi) / 2, (hi - lo) / 2);
        }).ToArray();

    /// <summary>Nearest sweep pillar along a ray, or <paramref name="maxDist"/>.</summary>
    public static float RaycastSweeps(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = maxDist;
        foreach (ref readonly var b in Sweeps)
            if (b.Raycast(origin, dir, best) is { } t && t < best)
                best = t;
        return best;
    }
}
