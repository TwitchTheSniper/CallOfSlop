using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Contraption space (docs/sandbox/CONSTRUCTS.md, "Contraption space"): a player on a construct lives in its frame. The
/// mover runs in the coordinates of the segment under the feet, against the construct's own boxes (and anything else near
/// them, turned into that frame), with gravity along the rider's own up (<see cref="MoveState.RideUp"/>, one direction in the
/// construct's rest frame): <see cref="MoveState.RideLocal"/> and <see cref="MoveState.RideVel"/> are the whole of their
/// movement, and the construct's pose only says where that is in the world. So the construct's speed, turn, tilt and roll
/// never reach the mover: 200 m/s, a banking turn and an inverted hull all walk like still, level ground, and the same
/// inputs give the same local state on the server, in the client's prediction and in its replays, whatever pose each has.
///
/// <para>It is a branch of <see cref="World.Simulate"/> like noclip: nothing here runs unless <see cref="MoveState.RideUp"/>
/// is set, so every other mode and every non-rider moves exactly as before. Bots keep the plain ride of ConstructRide.cs.</para>
/// </summary>
public static partial class ConstructRide
{
    /// <summary>The world a frame step finds its construct in (the game sets it before a player's step).</summary>
    public static ConstructWorld? Bodies;

    /// <summary>A frozen build's own floor is only ridden when its up leans further than this from the world's (cos 15 degrees): a level base needs no frame.</summary>
    public const float AnchoredLevel = 0.966f;

    /// <summary>A construct's face counts as its up when its normal is this close to that up (about 18 degrees).</summary>
    public const float UpFace = 0.95f;

    /// <summary>A jolt the frame can't hold (m/s^2): the deck's acceleration, less what its own turning explains. A crash, not a hard turn.</summary>
    public static float CrashAccel = 15f * 9.81f, GripCrashAccel = 30f * 9.81f;

    /// <summary>Standing, the body may be this far (m) into the map at the feet before the world counts (a thin plate on rough ground).</summary>
    public const float WorldFootSlack = 0.35f;

    /// <summary>How far the body must overlap something else's bodies at speed before it is scraped off them (m/s across).</summary>
    public const float ScrapeSpeed = 8f;

    private struct ShapeTag
    {
        public ushort Body;
        public byte Seg;
        public int Part;
    }

    private static ShapeTag[] _tags = new ShapeTag[256];
    private static readonly List<Construct> _rideNear = new(32);

    // What the last frame step stood on and walked into, for the server's side of it in BlockPlayer.
    private static int _groundPart = -1;
    private static Construct? _groundBody;
    private static Construct? _wallBody;
    private static int _wallSeg;
    private static Vector3 _wallAt, _wallPush;

    /// <summary>World turn of a rider's upright space: a body built with +y up, in the frame of a segment posed <paramref name="q"/>.</summary>
    public static Quaternion RiderRot(Quaternion q, Vector3 up) => q * TurnBetween(Vector3.UnitY, up);

    /// <summary>A world look direction as yaw and pitch about the rider's up.</summary>
    public static void LookIn(Quaternion rider, Vector3 dir, out float yaw, out float pitch)
    {
        var l = Vector3.Transform(dir, Quaternion.Conjugate(rider));
        yaw = MathF.Atan2(l.X, -l.Z);
        pitch = MathF.Asin(Math.Clamp(l.Y / MathF.Max(l.Length(), 1e-6f), -1f, 1f));
    }

    /// <summary>
    /// The up a rider takes on a construct: the drive seat's, else the direction most of its faces lie across, the way that
    /// is nearest <paramref name="worldUp"/>. One direction for the whole construct, in its rest frame.
    /// </summary>
    public static Vector3 UpFor(Construct c, Vector3 worldUp)
    {
        if (c.DriveSeat >= 0 && c.DriveSeat < c.PartCount)
        {
            ref var seat = ref c.Parts[c.DriveSeat];
            c.RestPose(seat.Seg, out var rq, out _);
            return Vector3.Normalize(Vector3.Transform(Rot24.Apply(seat.Rot, Vector3.UnitY), rq));
        }
        if (c.RideAxes == null)
            FaceAxes(c);
        var axes = c.RideAxes!;
        var areas = c.RideAreas!;
        var best = Vector3.UnitY;
        var score = -1f;
        for (var i = 0; i < axes.Length; i++)
        {
            var along = Vector3.Dot(Vector3.Transform(axes[i], c.Rot), worldUp);
            var v = areas[i] * (0.35f + MathF.Abs(along));
            if (v > score)
            {
                score = v;
                best = along >= 0 ? axes[i] : -axes[i];
            }
        }
        return best;
    }

    /// <summary>The directions a construct's solid boxes face (either way), with their area: worked out once per shape (a player boards rarely).</summary>
    private static void FaceAxes(Construct c)
    {
        Span<Vector3> axes = stackalloc Vector3[16];
        Span<float> areas = stackalloc float[16];
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            c.RestPose(p.Seg, out var q, out _);
            for (var b = 0; b < c.SolidCount(i); b++)
            {
                c.SolidBox(i, b, out _, out var half);
                for (var a = 0; a < 3; a++)
                {
                    var axis = Vector3.Zero;
                    axis[a] = 1;
                    axis = Vector3.Transform(axis, q);
                    var area = half[(a + 1) % 3] * half[(a + 2) % 3];
                    var k = 0;
                    for (; k < n; k++)
                        if (MathF.Abs(Vector3.Dot(axes[k], axis)) > 0.996f)
                            break;
                    if (k == n)
                    {
                        if (n == axes.Length)
                            continue;
                        axes[n] = axis;
                        areas[n] = 0;
                        n++;
                    }
                    areas[k] += area;
                }
            }
        }
        if (n == 0)
        {
            axes[0] = Vector3.UnitY;
            areas[0] = 1;
            n = 1;
        }
        c.RideAxes = axes[..n].ToArray();
        c.RideAreas = areas[..n].ToArray();
    }

    /// <summary>Is a frozen build's floor leaning enough to ride (its up is not the world's)?</summary>
    private static bool Leaning(Construct c, Vector3 worldUp) =>
        Vector3.Dot(Vector3.Transform(UpFor(c, worldUp), c.Rot), worldUp) < AnchoredLevel;

    /// <summary>Is standing on this segment riding it, for someone in contraption space?</summary>
    public static bool FrameRideable(Construct c, int seg, Vector3 worldUp) => !c.Anchored || c.MoveSeg(seg) != 0 || Leaning(c, worldUp);

    /// <summary>Puts a rider where their frame says, in the world: the copies everything else reads.</summary>
    public static void Place(ref MoveState s, Construct c)
    {
        c.WorldFrame(s.RideSeg, out var q, out var t);
        s.Position = t + Vector3.Transform(s.RideLocal, q);
        s.RideWorld = s.Position;
        s.RideUpW = Vector3.Normalize(Vector3.Transform(s.RideUp, q));
        s.Velocity = Vector3.Transform(s.RideVel, q);
    }

    /// <summary>What moved a rider in the world since the frame last put them down (a player's body, a blast) goes into the frame. False: something took them away.</summary>
    private static bool Absorb(ref MoveState s, Quaternion q)
    {
        var d = s.Position - s.RideWorld;
        if (d.LengthSquared() > 9f)
            return false;
        var iq = Quaternion.Conjugate(q);
        if (d != Vector3.Zero)
            s.RideLocal += Vector3.Transform(d, iq);
        var dv = s.Velocity - Vector3.Transform(s.RideVel, q);
        if (dv.LengthSquared() > 1e-4f)
            s.RideVel += Vector3.Transform(dv, iq);
        return true;
    }

    /// <summary>Into a construct's frame: the feet, the velocity relative to it, and the up they take there.</summary>
    public static void Join(ref MoveState s, Construct c, int seg, Vector3 up, Vector3 relative)
    {
        seg = c.MoveSeg(seg);
        s.RideBody = c.Id;
        s.RideSeg = (byte)seg;
        s.RideUp = up;
        s.RideClimb = 0;
        s.RideAir = 0;
        s.RideFree = 0;
        s.RideGripUp = Vector3.Zero;
        c.WorldFrame(seg, out var q, out var t);
        var iq = Quaternion.Conjugate(q);
        s.RideLocal = Vector3.Transform(s.Position - t, iq);
        s.RideVel = Vector3.Transform(relative, iq);
        s.RideWorld = s.Position;
        s.RideUpW = Vector3.Normalize(Vector3.Transform(up, q));
        s.Velocity = relative;
    }

    /// <summary>Back aboard a saved world's construct: standing where they stood in its frame.</summary>
    public static void Rejoin(ref MoveState s, Construct c, int seg, Vector3 up, Vector3 local)
    {
        s.RideBody = c.Id;
        s.RideSeg = (byte)seg;
        s.RideUp = Vector3.Normalize(up);
        s.RideLocal = local;
        s.RideVel = Vector3.Zero;
        s.RideClimb = s.RideAir = s.RideFree = 0;
        s.Grounded = true;
        Place(ref s, c);
    }

    /// <summary>The ride ends in contraption space: their feet stay, a body that stood at an angle comes upright about its middle.</summary>
    private static void LeaveFrame(ref MoveState s, Construct c)
    {
        Place(ref s, c);
        var up = GravityFrames.UpAt(s.Position);
        var h = World.HeightOf(s.Stance) * 0.5f;
        if (Vector3.Dot(s.RideUpW, up) < 0.5f)
            s.Position += (s.RideUpW - up) * h;
        Leave(ref s, c, momentum: true);
    }

    /// <summary>The start of a rider's construct step in contraption space (see <see cref="Begin"/>, which it stands in for). Returns the construct, or null when the ride ended.</summary>
    private static Construct? BeginFrame(ConstructWorld w, ref MoveState s, float height, bool server, out Vector3 surfaceVel, out bool climbing)
    {
        surfaceVel = Vector3.Zero;
        climbing = false;
        var c = w.Get(s.RideBody);
        if (c == null || s.RideSeg > c.JointCount)
        {
            Clear(ref s);
            return null;
        }
        if (s.KnockTicks == Knockback.StaggerTicks - 1)
        {
            Absorb(ref s, QuatOf(c, s.RideSeg));
            LeaveFrame(ref s, c); // a blast's push (an impulse): off, with the deck's speed
            return null;
        }
        c.WorldFrame(s.RideSeg, out var q, out var t);
        if (s.RideClimb > 0)
        {
            s.RideLocal += (s.RideClimbTo - s.RideLocal) / s.RideClimb;
            s.RideClimb--;
            s.RideVel = Vector3.Zero;
            Place(ref s, c);
            s.Grounded = s.RideClimb == 0;
            s.RideAir = 0;
            surfaceVel = SurfaceVel(c, s.RideSeg, s.Position);
            climbing = s.RideClimb > 0;
            return c;
        }
        if (!Absorb(ref s, q))
        {
            Clear(ref s);
            return null;
        }
        Place(ref s, c);
        surfaceVel = SurfaceVel(c, s.RideSeg, s.Position);
        // A crash: the deck's jolt, less what its own turn explains (a hard turn at speed isn't one), throws them off at its speed before it.
        if (server && !c.Anchored && !c.Asleep && c.SpaceShip == 0) // (a ship on rails moves as its orbit says, warp and all: nothing to be thrown by)
        {
            var a = c.Accel - Vector3.Cross(c.AngVel, c.Vel);
            var limit = _groundPart >= 0 && _groundBody == c && GripPart(c, _groundPart) ? GripCrashAccel : CrashAccel;
            if (a.LengthSquared() > limit * limit)
            {
                s.Velocity += surfaceVel - (c.Vel - c.VelThen);
                Throws++;
                var up = s.RideUpW;
                if (Vector3.Dot(s.Velocity - surfaceVel, up) < ThrowPop)
                    s.Velocity += up * ThrowPop;
                s.Grounded = false;
                Clear(ref s);
                s.RideFree = FreeTicks;
                s.RideAir = RejoinTicks;
                w.Events.Add($"a rider was thrown off construct {c.Id} ({a.Length() / 9.81f:0.0} g)");
                return null;
            }
        }
        // The world still counts: carried into the map's geometry (a low bridge, a wall it scrapes past, the ground when they drop off a side) they come off at the deck's speed.
        var u = s.RideUpW;
        var r = World.PlayerRadius;
        var foot = s.Grounded ? WorldFootSlack : 0f;
        // (standing, the body's thin core only at a walk: stepping onto a steep panel's foot, the body leans out over the ground it left)
        var core = s.Grounded && surfaceVel.LengthSquared() < 4f ? 0.15f : r;
        var capsule = new Capsule(s.Position + u * (r + foot), s.Position + u * MathF.Max(r + foot, height - r), core);
        if (World.Physics.OverlapCapsule(capsule, new QueryFilter(QueryLayers.Static | QueryLayers.Movers | QueryLayers.Boundary, MoverPhysics.Clock)))
        {
            LeaveFrame(ref s, c);
            s.RideAir = RejoinTicks; // (a while before a deck takes them back: this spot is the world's)
            return null;
        }
        return c;
    }

    private static Quaternion QuatOf(Construct c, int seg)
    {
        c.WorldFrame(seg, out var q, out _);
        return q;
    }

    private static bool GripPart(Construct c, int part)
    {
        if (part >= c.PartCount)
            return false;
        ref var p = ref c.Parts[part];
        return p.D.CfgSlot[(int)CfgKey.Grip] >= 0 && ConstructConfig.Get(p, p.D, CfgKey.Grip, 0) >= 0.5f;
    }

    /// <summary>
    /// The server's side of the frame step just run (from <see cref="ConstructWorld.BlockPlayer"/>): who stands on what
    /// (pressure plates, a joint's load, spring feet), and a door the walk ran into.
    /// </summary>
    private static void NoteStanding(ConstructWorld w, in MoveState s, byte who)
    {
        if (s.Grounded && _groundBody is { } c && _groundPart >= 0 && _groundPart < c.PartCount)
        {
            ref var p = ref c.Parts[_groundPart];
            if (p.D.Logic == CLogic.Plate)
                p.Stood = true;
            if (p.Seg > 0 && p.Seg <= Construct.MaxJoints)
                c.RiderBits[p.Seg] |= 1UL << (who & 63);
            if (c.SpringCount > 0 && !c.Anchored && c.CarriedBy == 0 && p.Seg == 0)
            {
                c.SpringRiders++;
                c.SpringRiderSum += s.Position;
                c.Wake();
            }
        }
        if (_wallBody is { } door)
        {
            ConstructWorld.PushJoint(door, _wallSeg, _wallAt, _wallPush);
            _wallBody = null;
        }
    }

    /// <summary>The face <see cref="SteepBoard"/> last boarded (its up, in the segment's rest frame).</summary>
    public static Vector3 LastFace;

    /// <summary>
    /// Walking or jumping into a box face that is the construct's own up but too steep to stand on in the world (a panel raised
    /// at 60 degrees): the feet go onto it. A truck's side isn't its up, and an overhang never takes anyone.
    /// </summary>
    public static bool SteepBoard(Construct c, ref MoveState s, Vector3 centre, Vector3 half, Vector3 lf, Quaternion q, Vector3 t, Vector3 upLocal)
    {
        if (upLocal.Y > 0.7f || s.Velocity.Y > 0.5f && !s.Grounded)
            return false;
        var worldUp = GravityFrames.UpAt(s.Position);
        var u = UpFor(c, worldUp);
        var ax = MathF.Abs(u.X) > MathF.Abs(u.Y) ? (MathF.Abs(u.X) > MathF.Abs(u.Z) ? 0 : 2) : (MathF.Abs(u.Y) > MathF.Abs(u.Z) ? 1 : 2);
        if (MathF.Abs(u[ax]) < UpFace)
            return false;
        var sign = u[ax] > 0 ? 1 : -1;
        var n = Vector3.Zero;
        n[ax] = sign;
        var wn = Vector3.Transform(n, q);
        if (Vector3.Dot(wn, worldUp) < GripUp || Vector3.Dot(s.Velocity, wn) > 0.5f)
            return false;
        var r = World.PlayerRadius;
        var face = centre[ax] + sign * half[ax];
        var dist = (lf[ax] - face) * sign;
        if (dist > r + 0.15f || dist < -World.StepHeight)
            return false;
        var at = lf;
        for (var a = 0; a < 3; a++)
            if (a != ax)
            {
                if (MathF.Abs(lf[a] - centre[a]) > half[a] + r)
                    return false;
                at[a] = SafeMath.Clamp(lf[a], centre[a] - half[a], centre[a] + half[a]);
            }
        at[ax] = face + sign * 0.02f;
        s.Position = t + Vector3.Transform(at, q);
        s.Grounded = true;
        LastFace = n;
        return true;
    }

    /// <summary>The rest of a rider's construct step: the server's bookkeeping, and another body driving into them.</summary>
    public static void AfterFrame(ConstructWorld w, ref MoveState s, Construct ride, Vector3 surfaceVel, float height, bool server, byte who)
    {
        if (server)
            NoteStanding(w, s, who);
        var shapes = SweptMover.Instance.FrameShapes;
        if (shapes.Count == 0)
            return;
        // Another construct that runs into them: a hit by its speed relative to their deck, as for anyone (GameSandbox.cs), and a hard one scrapes them off.
        var up = s.RideUp;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shapes);
        var p = s.RideLocal + up * World.PlayerRadius;
        var e = s.RideLocal + up * MathF.Max(World.PlayerRadius, height - World.PlayerRadius);
        for (var i = 0; i < span.Length && i < _tags.Length; i++) // (the map's shapes in the list have no tag, and there can be more of them than tags)
        {
            var tag = _tags[i];
            if (tag.Body == ride.Id || CapsuleContact.Separation(span[i], p, e, World.PlayerRadius, out var n, out var pt) > -0.01f)
                continue;
            if (w.Get(tag.Body) is not { } o)
                continue;
            ride.WorldFrame(s.RideSeg, out var q, out var t);
            var at = t + Vector3.Transform(pt, q);
            var v = SurfaceVel(o, tag.Seg, at) - surfaceVel;
            var wn = Vector3.Transform(n, q);
            var speed = Vector3.Dot(v, wn);
            if (speed > w.LastHitSpeed)
            {
                w.LastHitSpeed = speed;
                w.LastHitMass = o.Mass;
                w.LastHitVel = v;
                w.LastHitNormal = wn;
                w.LastHitBody = o;
            }
            if (speed > ScrapeSpeed)
            {
                LeaveFrame(ref s, ride);
                s.RideAir = RejoinTicks;
                return;
            }
        }
    }

    /// <summary>Everything a rider can touch this tick, in the frame of segment <paramref name="seg"/>: the construct's own boxes from its poses alone, others' through the world.</summary>
    private static void Gather(ConstructWorld w, Construct c, int seg, Vector3 feet, Vector3 up, float reach)
    {
        var shapes = SweptMover.Instance.FrameShapes;
        shapes.Clear();
        var lo = Vector3.Min(feet, feet + up * World.PlayerHeight) - new Vector3(reach);
        var hi = Vector3.Max(feet, feet + up * World.PlayerHeight) + new Vector3(reach);
        var mid = (lo + hi) * 0.5f;
        var radius = (hi - lo).Length() * 0.5f;
        c.SegPose(seg, out var qs, out var ts);
        var iqs = Quaternion.Conjugate(qs);
        var inBody = ts + Vector3.Transform(mid, qs);
        var hits = Construct.PartScratch;
        var n = c.PartsInBox(inBody - new Vector3(radius), inBody + new Vector3(radius), hits);
        var last = -1;
        var rq = Quaternion.Identity;
        var rt = Vector3.Zero;
        for (var k = 0; k < n; k++)
        {
            var i = hits[k];
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            if (p.Seg != last)
            {
                last = p.Seg;
                if (last == seg)
                {
                    rq = Quaternion.Identity;
                    rt = Vector3.Zero;
                }
                else
                {
                    c.SegPose(last, out var qk, out var tk);
                    rq = iqs * qk;
                    rt = Vector3.Transform(tk - ts, iqs);
                }
            }
            var boxes = c.SolidCount(i);
            for (var b = 0; b < boxes; b++)
            {
                c.SolidBox(i, b, out var centre, out var half);
                Add(shapes, last == seg ? centre : rt + Vector3.Transform(centre, rq), half, rq, lo, hi, c.Id, last, i);
            }
        }
        c.WorldFrame(seg, out var qf, out var tf);
        var iqf = Quaternion.Conjugate(qf);
        var at = tf + Vector3.Transform(mid, qf);
        var near = w.Near(at, radius, _rideNear);
        for (var bi = 0; bi < near.Count; bi++)
        {
            var o = near[bi];
            if (o == c)
                continue;
            var rc = o.Radius + radius;
            if (Vector3.DistanceSquared(at, o.WorldCom) > rc * rc)
                continue;
            var here = o.ToLocal(at);
            var count = o.PartsInBox(here - new Vector3(radius + 3f), here + new Vector3(radius + 3f), hits);
            for (var k = 0; k < count; k++)
            {
                var i = hits[k];
                ref var p = ref o.Parts[i];
                if (p.Broken)
                    continue;
                o.WorldFrame(p.Seg, out var q2, out var t2);
                var r = iqf * q2;
                var off = Vector3.Transform(t2 - tf, iqf);
                var boxes = o.SolidCount(i);
                for (var b = 0; b < boxes; b++)
                {
                    o.SolidBox(i, b, out var centre, out var half);
                    Add(shapes, off + Vector3.Transform(centre, r), half, r, lo, hi, o.Id, p.Seg, i);
                }
            }
        }
    }

    private static void Add(List<ContactShape> shapes, Vector3 centre, Vector3 half, Quaternion rot, Vector3 lo, Vector3 hi, ushort body, int seg, int part)
    {
        var box = new Obb(centre, half, rot);
        var shape = ContactShape.FromObb(box, default);
        if (shape.Min.X > hi.X || shape.Max.X < lo.X || shape.Min.Y > hi.Y || shape.Max.Y < lo.Y || shape.Min.Z > hi.Z || shape.Max.Z < lo.Z)
            return;
        if (shapes.Count == _tags.Length)
            Array.Resize(ref _tags, _tags.Length * 2);
        _tags[shapes.Count] = new ShapeTag { Body = body, Seg = (byte)seg, Part = part };
        shapes.Add(shape);
    }

    /// <summary>
    /// One tick of a rider's movement, in the construct's frame: the branch of <see cref="World.Simulate"/>. False when the
    /// ride is over (the construct went, a teleport) and the ordinary mover should run instead.
    /// </summary>
    public static bool Step(ref MoveState s, in InputCmd cmd)
    {
        var w = Bodies;
        var c = w?.Get(s.RideBody);
        if (w == null || c == null || s.RideSeg > c.JointCount)
        {
            Clear(ref s);
            return false;
        }
        if (c.Dirty)
            c.Rebuild();
        c.WorldFrame(s.RideSeg, out var q, out _);
        if (!Absorb(ref s, q))
        {
            Clear(ref s); // (moved by something else: a teleport, a respawn)
            return false;
        }
        if (s.BoostTicks > 0)
            s.BoostTicks--;
        if (s.KnockTicks > 0)
            s.KnockTicks--;
        if (s.RideClimb > 0)
            return true; // (a climb moves them, in the construct step)
        var worldUp = GravityFrames.UpAt(s.Position);
        if (c.Anchored && c.MoveSeg(s.RideSeg) == 0 && !Leaning(c, worldUp))
        {
            LeaveFrame(ref s, c); // (a level frozen floor is the world's: frozen under them, or climbed onto)
            return false;
        }
        var up = Vector3.Normalize(s.RideUp);
        var rider = RiderRot(q, up);
        float yaw;
        if (cmd.RideBody == s.RideBody)
            yaw = cmd.RideYaw;
        else
            LookIn(rider, GravityFrames.Forward(s.Position, cmd.Yaw, cmd.Pitch), out yaw, out _); // (the look of a moment before they boarded)
        var turn = TurnBetween(Vector3.UnitY, up);
        var wish = Vector3.Transform(MoveInput.Wish(cmd.Buttons, yaw), turn);
        var speed = MoveInput.Speed(s, cmd.Buttons, cmd.SpeedPct);
        var control = s.KnockTicks > 0
            ? (s.Grounded ? Knockback.StaggerGroundControl : Knockback.StaggerAirControl)
            : s.Grounded ? 14f : 3f;
        var m = SweptMover.Instance;
        var reach = s.RideVel.Length() * World.TickDt + World.StepHeight + SweptMover.SnapDown + SweptMover.MinStepForward + 0.5f;
        Gather(w, c, s.RideSeg, s.RideLocal, up, reach);
        m.FrameStep(ref s.RideLocal, ref s.RideVel, ref s.Grounded, ref s.Stance, up, wish * speed, control, World.Gravity * World.GravityScale,
            cmd.Buttons, World.JumpSpeed * (s.MovePerk == PerkId.Parkour ? Perks.ParkourJump : 1f));
        _groundBody = null;
        _groundPart = -1;
        _wallBody = null;
        if (s.Grounded)
        {
            s.RideAir = 0;
            if (m.FrameCast(s.RideLocal, s.Stance, up, -up, 0.1f, out var n, out _, out var hit))
                Landed(w, ref s, ref c, up, n, hit);
        }
        else
        {
            if (s.RideAir < 255)
                s.RideAir++;
            c.SegPose(s.RideSeg, out var qs, out var ts);
            var lb = ts + Vector3.Transform(s.RideLocal, qs);
            var pad = new Vector3(BoundsPad);
            var above = Vector3.Abs(up) * (BoundsAbove - BoundsPad);
            var inside = lb.X > c.LocalMin.X - pad.X - (up.X < 0 ? above.X : 0) && lb.X < c.LocalMax.X + pad.X + (up.X > 0 ? above.X : 0)
                         && lb.Y > c.LocalMin.Y - pad.Y - (up.Y < 0 ? above.Y : 0) && lb.Y < c.LocalMax.Y + pad.Y + (up.Y > 0 ? above.Y : 0)
                         && lb.Z > c.LocalMin.Z - pad.Z - (up.Z < 0 ? above.Z : 0) && lb.Z < c.LocalMax.Z + pad.Z + (up.Z > 0 ? above.Z : 0);
            if (!inside && s.RideAir > CoyoteTicks)
            {
                LeaveFrame(ref s, c); // off the edge, or clear of it: the world's gravity from here, with their speed in it
                return true;
            }
        }
        var flat = s.RideVel - up * Vector3.Dot(s.RideVel, up);
        if (wish != Vector3.Zero && m.FrameCast(s.RideLocal, s.Stance, up, wish, 0.12f, out var wn, out var wp, out var wi))
            Walked(w, ref s, c, up, wish, flat, wn, wp, wi);
        Place(ref s, c);
        return true;
    }

    /// <summary>What's under the feet after the move: another segment or another construct takes them into its frame; a grip face gives them its up.</summary>
    private static void Landed(ConstructWorld w, ref MoveState s, ref Construct c, Vector3 up, Vector3 n, int hit)
    {
        var tag = _tags[hit];
        if (tag.Body != c.Id)
        {
            if (w.Get(tag.Body) is not { } other)
                return;
            Place(ref s, c);
            var worldUp = s.RideUpW;
            var rel = s.Velocity + SurfaceVel(c, s.RideSeg, s.Position) - SurfaceVel(other, tag.Seg, s.Position);
            var u = UpFor(other, worldUp);
            c.WorldFrame(s.RideSeg, out var qo, out _);
            other.WorldFrame(tag.Seg, out var qn, out _);
            var face = Vector3.Normalize(Vector3.Transform(Vector3.Transform(n, qo), Quaternion.Conjugate(qn)));
            if (Vector3.Dot(face, u) < World.MinWalkNormalY)
                u = face;
            Join(ref s, other, tag.Seg, u, rel);
            s.Grounded = true;
            c = other;
            _groundBody = other;
            _groundPart = tag.Part;
            SweptMover.Instance.FrameShapes.Clear(); // (they were in the old frame)
            return;
        }
        var ms = c.MoveSeg(tag.Seg);
        if (ms != s.RideSeg)
        {
            Place(ref s, c);
            var rel = s.Velocity + SurfaceVel(c, s.RideSeg, s.Position) - SurfaceVel(c, ms, s.Position);
            Join(ref s, c, ms, s.RideUp, rel);
            s.Grounded = true;
            SweptMover.Instance.FrameShapes.Clear();
        }
        _groundBody = c;
        _groundPart = tag.Part;
        var grip = GripPart(c, tag.Part);
        if (grip)
        {
            if (Vector3.Dot(n, up) < 0.9995f && Vector3.Dot(n, up) > 0.3f && FaceNormal(hit, n))
                s.RideUp = n; // (a grip face's own up: a magnet boot on any angle; never an edge's, which would walk them round it)
            return;
        }
        var home = UpFor(c, Vector3.Transform(up, c.Rot));
        if (Vector3.Dot(up, home) < 0.999f && Vector3.Dot(n, home) >= World.MinWalkNormalY)
            s.RideUp = home; // (off the grip faces, back on the construct's own up)
    }

    /// <summary>Is a contact's normal a face of its box (not one of its edges)?</summary>
    private static bool FaceNormal(int hit, Vector3 n)
    {
        ref readonly var b = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(SweptMover.Instance.FrameShapes)[hit].Box;
        return MathF.Abs(Vector3.Dot(n, b.AxisX)) > 0.999f || MathF.Abs(Vector3.Dot(n, b.AxisY)) > 0.999f || MathF.Abs(Vector3.Dot(n, b.AxisZ)) > 0.999f;
    }

    /// <summary>What the walk ran into: a door swings (the server, after the step), a grip wall becomes the floor.</summary>
    private static void Walked(ConstructWorld w, ref MoveState s, Construct c, Vector3 up, Vector3 wish, Vector3 flat, Vector3 n, Vector3 point, int hit)
    {
        var tag = _tags[hit];
        if (tag.Body != c.Id)
            return;
        if (Vector3.Dot(n, up) < 0.3f && GripPart(c, tag.Part) && FaceNormal(hit, n))
        {
            s.RideUp = n;
            s.RideLocal = point + n * 0.02f;
            s.RideVel = Vector3.Zero;
            s.Grounded = true;
            return;
        }
        if (c.MoveSeg(tag.Seg) != s.RideSeg && tag.Seg > 0)
        {
            c.WorldFrame(s.RideSeg, out var q, out var t);
            _wallBody = c;
            _wallSeg = tag.Seg;
            _wallAt = t + Vector3.Transform(point, q);
            _wallPush = Vector3.Transform(wish, q) * MathF.Max(1.5f, flat.Length());
        }
    }
}
