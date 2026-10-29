using System.Numerics;

namespace CallOfSlop;

/// <summary>What a <see cref="Msg.CBody"/> carries (its first byte).</summary>
public enum CBodyKind : byte
{
    Full, // one construct: parts, links, joints, pose
    Gone, // a construct removed
    Health, // part health changes: (id, part, health) each
    Clear, // every construct removed (a new match)
}

/// <summary>
/// The constructs on the wire (protocol 77). Structure goes reliably and only when it changes (an edit, a break, a
/// split; and the whole set to a joiner); poses go unreliably at 20 Hz for the bodies that move, with a few sleeping ones
/// refreshed each time so a lost packet never leaves a body wrong for long. Positions of parts travel on their 1 cm grid
/// (<see cref="ConstructWorld.Quantise"/>), so a client's copy is exactly the server's.
/// </summary>
public static class ConstructNet
{
    public const int PoseEvery = 3; // ticks between pose packets (20 Hz)
    public const int SleepingPerPacket = 4;
    private const int PoseBytes = 2 + 12 + 8 + 6 + 1 + 1 + 1 + 1;

    private static short Cm(float v) => (short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue);

    private static float FromCm(short v) => v / 100f;

    private static ushort S16(short v) => unchecked((ushort)v);

    private static short R16(ushort v) => unchecked((short)v);

    private static byte AxisDir(Vector3 axis)
    {
        var best = (byte)0;
        var bestDot = float.MinValue;
        for (byte d = 0; d < 6; d++)
        {
            var dot = Vector3.Dot(axis, ConstructParts.DirVector(d));
            if (dot > bestDot)
            {
                bestDot = dot;
                best = d;
            }
        }
        return best;
    }

    public static void WriteFull(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Full).U16(c.Id);
        w.U8((byte)((c.Wreck ? 1 : 0) | (c.Anchored ? 2 : 0) | (c.Asleep ? 4 : 0)));
        w.U8((byte)Math.Max(0, c.Root)).U8(c.Driver).U8((byte)(c.DriverSeat < 0 ? 255 : c.DriverSeat));
        w.F32(c.Fuel);
        w.Vec(c.Pos).F32(c.Rot.X).F32(c.Rot.Y).F32(c.Rot.Z).F32(c.Rot.W).Vec(c.Vel).Vec(c.AngVel);
        w.U8((byte)c.PartCount);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            w.U8(p.Def).U16(S16(Cm(p.Pos.X))).U16(S16(Cm(p.Pos.Y))).U16(S16(Cm(p.Pos.Z))).U8(p.Rot).U8(p.Seg)
                .U16((ushort)Math.Clamp(MathF.Ceiling(p.Health), 0, 65535)).U8((byte)(p.Broken ? 1 : 0));
        }
        w.U16((ushort)c.LinkCount);
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            w.U8(l.A).U8(l.B).U8(l.SA).U8(l.SB);
        }
        w.U8((byte)c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            w.U8(jt.Hinge).U8(jt.ParentSeg).U16(S16(Cm(jt.Pivot.X))).U16(S16(Cm(jt.Pivot.Y))).U16(S16(Cm(jt.Pivot.Z))).U8(AxisDir(jt.Axis))
                .F32(jt.Min).F32(jt.Max).F32(jt.Angle).F32(jt.Vel).U8((byte)(jt.Open ? 1 : 0));
        }
    }

    public static void WriteGone(PacketWriter w, ushort id) => w.Begin(Msg.CBody).U8((byte)CBodyKind.Gone).U16(id);

    public static void WriteClear(PacketWriter w) => w.Begin(Msg.CBody).U8((byte)CBodyKind.Clear);

    /// <summary>A batch of part health changes (the caller keeps it under the MTU: at most ~200 entries).</summary>
    public static int WriteHealth(PacketWriter w, ConstructWorld world, List<(ushort Body, byte Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Health);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 200; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount)
                continue;
            w.U16(id).U8(part).U16((ushort)Math.Clamp(MathF.Ceiling(c.Parts[part].Health), 0, 65535));
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Reads a <see cref="Msg.CBody"/> (after the id byte) into a client's world. Bad values are refused, never trusted.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world, double now)
    {
        var kind = (CBodyKind)r.U8();
        switch (kind)
        {
            case CBodyKind.Clear:
                world.Clear();
                world.Removed.Clear();
                break;
            case CBodyKind.Gone:
                if (world.Get(r.U16()) is { } gone)
                    world.Remove(gone);
                world.Removed.Clear();
                break;
            case CBodyKind.Health:
            {
                int n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var id = r.U16();
                    var part = r.U8();
                    var hp = r.U16();
                    if (world.Get(id) is { } c && part < c.PartCount)
                        c.Parts[part].Health = hp;
                }
                break;
            }
            case CBodyKind.Full:
                ReadFull(ref r, world, now);
                break;
            default:
                throw new MalformedPacketException($"unknown construct message {kind}");
        }
    }

    private static void ReadFull(ref PacketReader r, ConstructWorld world, double now)
    {
        var id = r.U16();
        var flags = r.U8();
        var root = r.U8();
        var driver = r.U8();
        var seat = r.U8();
        var fuel = r.F32();
        var pos = r.Vec();
        var rot = new Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
        var vel = r.Vec();
        var ang = r.Vec();
        if (!float.IsFinite(pos.X + pos.Y + pos.Z + rot.X + rot.Y + rot.Z + rot.W) || rot.LengthSquared() < 0.5f)
            throw new MalformedPacketException("a construct pose that isn't a number");
        rot = Quaternion.Normalize(rot);
        var c = world.Get(id);
        var fresh = c == null;
        if (c == null)
        {
            c = world.Create(pos, rot, id);
            if (c == null)
                throw new MalformedPacketException("more constructs than the caps allow");
        }
        world.StructureDirty.Remove(id);
        int parts = r.U8();
        if (parts > Construct.MaxParts)
            throw new MalformedPacketException($"a construct of {parts} parts");
        c.PartCount = 0;
        for (var i = 0; i < parts; i++)
        {
            var def = r.U8();
            var p = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16())));
            var turn = r.U8();
            var seg = r.U8();
            var hp = r.U16();
            var broken = r.U8() != 0;
            if (ConstructParts.Get(def) == null || turn >= Rot24.Count)
                throw new MalformedPacketException($"construct part {i}: unknown type {def} or turn {turn}");
            c.Parts[c.PartCount++] = new CPart { Def = def, Pos = p, Rot = turn, Seg = seg, Health = hp, Broken = broken };
        }
        int links = r.U16();
        if (links > Construct.MaxLinks)
            throw new MalformedPacketException($"a construct of {links} links");
        c.LinkCount = 0;
        for (var k = 0; k < links; k++)
        {
            var l = new CLink { A = r.U8(), B = r.U8(), SA = r.U8(), SB = r.U8() };
            if (l.A >= parts || l.B >= parts)
                throw new MalformedPacketException("a construct link to a part that isn't there");
            c.Links[c.LinkCount++] = l;
        }
        int joints = r.U8();
        if (joints > Construct.MaxJoints)
            throw new MalformedPacketException($"a construct of {joints} joints");
        c.JointCount = 0;
        for (var j = 0; j < joints; j++)
        {
            var jt = new CJoint
            {
                Hinge = r.U8(),
                ParentSeg = r.U8(),
                Pivot = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16()))),
                Axis = ConstructParts.DirVector((byte)(r.U8() % 6)),
                Min = r.F32(),
                Max = r.F32(),
                Angle = r.F32(),
                Vel = r.F32(),
                Open = r.U8() != 0,
            };
            if (jt.Hinge >= parts || jt.ParentSeg > j || !float.IsFinite(jt.Angle + jt.Min + jt.Max + jt.Vel))
                throw new MalformedPacketException("a construct joint that doesn't fit");
            jt.PrevAngle = jt.Angle;
            c.Joints[c.JointCount++] = jt;
        }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Seg > c.JointCount)
                throw new MalformedPacketException("a construct part on a joint that isn't there");
        c.Wreck = (flags & 1) != 0;
        c.Driver = driver;
        c.DriverSeat = seat == 255 || seat >= parts ? -1 : seat;
        c.Rebuild();
        c.Anchored = (flags & 2) != 0;
        c.Asleep = (flags & 4) != 0;
        c.Root = root < parts ? root : 0;
        c.Fuel = fuel;
        c.Vel = vel;
        c.AngVel = ang;
        if (fresh)
        {
            c.Pos = c.PrevPos = c.NetPos = c.NetFromPos = pos;
            c.Rot = c.PrevRot = c.NetRot = c.NetFromRot = rot;
        }
        else
            SetTarget(c, pos, rot, now);
    }

    private static void SetTarget(Construct c, Vector3 pos, Quaternion rot, double now)
    {
        c.NetFromPos = c.Pos;
        c.NetFromRot = c.Rot;
        c.NetPos = pos;
        c.NetRot = rot;
        c.NetAt = now;
    }

    /// <summary>
    /// Server: the poses of the bodies that move, plus a few sleeping ones in turn. Returns false when there is nothing to send.
    /// </summary>
    public static bool WritePoses(PacketWriter w, ConstructWorld world, uint tick, ref int sleepCursor)
    {
        w.Begin(Msg.CPose).U32(tick);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var budget = PacketWriter.Mtu - 8;
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c.Asleep || c.Anchored && c.JointCount == 0)
                continue;
            if (w.Length + PoseBytes + c.JointCount * 2 > budget || n == 255)
                break;
            WritePose(w, c);
            n++;
        }
        for (var k = 0; k < SleepingPerPacket && bodies.Count > 0; k++)
        {
            sleepCursor = (sleepCursor + 1) % bodies.Count;
            var c = bodies[sleepCursor];
            if (!(c.Asleep || c.Anchored && c.JointCount == 0))
                continue;
            if (w.Length + PoseBytes + c.JointCount * 2 > budget || n == 255)
                break;
            WritePose(w, c);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return n > 0;
    }

    private static void WritePose(PacketWriter w, Construct c)
    {
        var q = c.Rot;
        if (q.W < 0)
            q = -q;
        w.U16(c.Id).Vec(c.Pos)
            .U16(S16((short)MathF.Round(q.X * 32767))).U16(S16((short)MathF.Round(q.Y * 32767))).U16(S16((short)MathF.Round(q.Z * 32767))).U16(S16((short)MathF.Round(q.W * 32767)))
            .U16(S16(Cm(c.Vel.X / 4))).U16(S16(Cm(c.Vel.Y / 4))).U16(S16(Cm(c.Vel.Z / 4)))
            .U8(unchecked((byte)(sbyte)Math.Clamp(MathF.Round(c.Steer * 100), -127, 127)))
            .U8(c.Driver)
            .U8((byte)(c.Asleep ? 1 : 0))
            .U8((byte)c.JointCount);
        for (var j = 0; j < c.JointCount; j++)
            w.U16(S16((short)Math.Clamp(MathF.Round(c.Joints[j].Angle * 1000), short.MinValue, short.MaxValue)));
    }

    /// <summary>Client: a <see cref="Msg.CPose"/> (after the id byte). Older packets than the last one are ignored.</summary>
    public static void ReadPoses(ref PacketReader r, ConstructWorld world, double now, ref uint lastTick)
    {
        var tick = r.U32();
        int n = r.U8();
        var stale = tick < lastTick;
        if (!stale)
            lastTick = tick;
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var pos = r.Vec();
            var q = new Quaternion(R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f, R16(r.U16()) / 32767f);
            var vel = new Vector3(FromCm(R16(r.U16())), FromCm(R16(r.U16())), FromCm(R16(r.U16()))) * 4;
            var steer = (sbyte)r.U8() / 100f;
            var driver = r.U8();
            var asleep = r.U8() != 0;
            int joints = r.U8();
            var c = world.Get(id);
            for (var j = 0; j < joints; j++)
            {
                var a = R16(r.U16()) / 1000f;
                if (c != null && !stale && j < c.JointCount)
                {
                    c.Joints[j].PrevAngle = c.Joints[j].Angle;
                    c.Joints[j].Angle = a;
                }
            }
            if (c == null || stale || !float.IsFinite(pos.X + pos.Y + pos.Z) || q.LengthSquared() < 0.5f)
                continue;
            SetTarget(c, pos, Quaternion.Normalize(q), now);
            c.Vel = vel;
            c.Steer = steer;
            c.Driver = driver;
            c.Asleep = asleep;
            c.NetTick = tick;
        }
    }

    /// <summary>Client, every frame: each body drawn between the last two poses heard (over one pose interval).</summary>
    public static void Blend(ConstructWorld world, double now, float dt)
    {
        const double span = PoseEvery / (double)World.TickRate;
        foreach (var c in world.Bodies)
        {
            var t = (float)Math.Clamp((now - c.NetAt) / span, 0, 1.5);
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            // A little past the last pose (at its velocity) so a moving body doesn't stop and start between packets.
            c.Pos = t <= 1 ? Vector3.Lerp(c.NetFromPos, c.NetPos, t) : c.NetPos + c.Vel * (float)((t - 1) * span);
            c.Rot = Quaternion.Slerp(c.NetFromRot, c.NetRot, MathF.Min(t, 1));
            for (var k = 0; k < c.WheelCount; k++)
            {
                ref var wh = ref c.Wheels[k];
                var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
                wh.Spin += Vector3.Dot(c.Vel, fwd) / wh.Radius * dt;
            }
        }
    }
}
