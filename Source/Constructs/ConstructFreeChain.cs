using System.Numerics;

namespace CallOfSlop;

/// <summary>A physics gun's beam on a piece of a mechanism this tick (ConstructEdits.GrabJointTick to <see cref="ConstructPhysics.BeamRows"/>).</summary>
public struct CBeam
{
    public ushort Body;
    public byte Seg;
    public Vector3 Local, Target; // the held point in the segment's frame; where the beam wants it (world)
    public float Accel, Mass; // what it may give what it moves (m/s2), and how much that is (kg)
}

public sealed partial class ConstructWorld
{
    public const int MaxBeams = 64;
    public readonly CBeam[] Beams = new CBeam[MaxBeams];
    public int BeamCount;
}

public sealed partial class Construct
{
    public const int MaxCouplePairs = 128; // joint pairs in a chain that pass motion to each other; a longer chain's far pairs go without
    public const float PinReach = 1f; // metres from a pinned piece's pivot to the three points that hold it
    public int FrozenPieces, PinCount; // joints frozen; those of them held in the world with something free behind them
    public ushort[] CouplePairs = []; // (ancestor, descendant) joint pairs, at least one of them free
    public int CouplePairCount;

    /// <summary>Is the piece part <paramref name="part"/> belongs to frozen (the base, or the jointed piece it moves with)?</summary>
    public bool PieceFrozen(int part)
    {
        var seg = (uint)part < (uint)PartCount ? MoveSeg(Parts[part].Seg) : 0;
        return seg == 0 ? Frozen : Joints[seg - 1].Frozen;
    }

    /// <summary>
    /// What the frozen pieces come to, and which joints pass motion to each other, from the flags as they stand. A frozen piece on
    /// something fixed in the world is locked to it (no cost in the solve); on something free it is held where it is by rows
    /// (<see cref="ConstructPhysics.PinRows"/>), so what is behind it moves round it. Nothing allocates unless the pair list grows.
    /// </summary>
    internal void BuildFreeChain()
    {
        FrozenPieces = PinCount = 0;
        Span<bool> held = stackalloc bool[JointCount + 1];
        held[0] = Anchored;
        for (var j = 0; j < JointCount; j++)
        {
            ref var jt = ref Joints[j];
            if (jt.Freeform)
            {
                held[j + 1] = held[jt.ParentSeg];
                continue;
            }
            jt.FrozenLock = jt.Frozen && held[jt.ParentSeg];
            jt.RootJoint = Rigid(jt.ParentSeg);
            var pin = jt.Frozen && !held[jt.ParentSeg];
            if (jt.Frozen)
                FrozenPieces++;
            if (pin)
            {
                PinCount++;
                if (!jt.PinSet)
                {
                    WorldFrame(j + 1, out jt.PinRot, out jt.PinPos); // (held from here on)
                    jt.PinSet = true;
                }
            }
            else
                jt.PinSet = false;
            held[j + 1] = jt.Frozen;
            jt.CoupleSet = false;
        }
        CouplePairCount = 0;
        for (var d = 0; d < JointCount; d++)
        {
            if (!ChainMember(d))
                continue;
            for (int s = Joints[d].ParentSeg, guard = 0; s > 0 && s <= JointCount && guard <= MaxJoints; guard++)
            {
                var a = s - 1;
                s = Joints[a].ParentSeg;
                if (!ChainMember(a) || !Joints[a].FreeMoving && !Joints[d].FreeMoving)
                    continue;
                if (CouplePairCount == MaxCouplePairs)
                    return;
                if ((CouplePairCount + 1) * 2 > CouplePairs.Length)
                    Array.Resize(ref CouplePairs, Math.Min(MaxCouplePairs * 2, Math.Max(8, CouplePairs.Length * 2)));
                CouplePairs[CouplePairCount * 2] = (ushort)a;
                CouplePairs[CouplePairCount * 2 + 1] = (ushort)d;
                CouplePairCount++;
            }
        }
    }

    private bool ChainMember(int j) => Joints[j].Type is not (JointType.Fixed or JointType.Flex or JointType.Steering);
}

/// <summary>
/// Mechanisms as chains (docs/sandbox/CONSTRUCTS.md, "Pieces of a mechanism"). The joints are one number each, solved one by one, so
/// three things here make a chain act as one: a physics gun's beam pulls on the held point through every joint behind it, a frozen
/// piece on something free is held in the world by rows, and joints pass motion to each other. Nothing here runs for a
/// construct without these: a beam list, two counts on a body.
/// </summary>
public static partial class ConstructPhysics
{
    /// <summary>How much of the motion between joints is passed on each tick: below 1, as one number a joint solved a tick behind the other rings otherwise.</summary>
    public const float CoupleShare = 0.85f;

    /// <summary>
    /// The beams this tick: three rows at the held point, as the body's beam pushes (a speed that closes the gap over
    /// <see cref="ConstructGrab.SettleTime"/>, no faster than it can brake). A row shares its push out along the chain by how easily
    /// each joint moves that way; stops, Locks and frozen pieces take no share. The body behind it doesn't move for it.
    /// </summary>
    private static void BeamRows(ConstructWorld world, int n, float dt)
    {
        for (var k = 0; k < world.BeamCount; k++)
        {
            ref var b = ref world.Beams[k];
            if (world.Get(b.Body) is not { } c || (uint)c.SimIndex >= (uint)n || !ReferenceEquals(_bodies[c.SimIndex], c) || !_jact[c.SimIndex] || b.Seg > c.JointCount)
                continue;
            c.WorldFrame(b.Seg, out var q, out var t);
            var at = t + Vector3.Transform(b.Local, q);
            var gap = b.Target - at;
            var want = gap / ConstructGrab.SettleTime;
            var cap = MathF.Min(ConstructGrab.MaxPull, MathF.Sqrt(2f * ConstructGrab.BrakeShare * b.Accel * gap.Length()));
            if (want.LengthSquared() > cap * cap)
                want *= cap / want.Length();
            if (!float.IsFinite(want.X + want.Y + want.Z))
                continue;
            var most = MathF.Max(b.Mass, 1f) * (b.Accel + GravityAccel) * dt;
            BeamRow(c, (short)c.SimIndex, b.Seg, at, Vector3.UnitX, want.X, most);
            BeamRow(c, (short)c.SimIndex, b.Seg, at, Vector3.UnitY, want.Y, most);
            BeamRow(c, (short)c.SimIndex, b.Seg, at, Vector3.UnitZ, want.Z, most);
        }
        world.BeamCount = 0;
    }

    private static void BeamRow(Construct c, short index, int seg, Vector3 at, Vector3 axis, float speed, float most)
    {
        var ua = _chainCount;
        var na = Chain(c, index, seg, at, out _);
        if (na <= 0)
            return;
        _chainCount += na;
        if (!Add(index, -1, at, axis, 0, 0f, ua, na))
            return;
        ref var ct = ref _contacts[_contactCount - 1];
        ct.Bilateral = true;
        ct.KinA = true;
        ct.Beam = most;
        ct.Drive = -speed;
        LastJointContacts--; // (not a contact on a joint's parts)
    }

    /// <summary>
    /// Pieces frozen with something free behind them: three points of each are held where it was frozen, through the joints up its
    /// chain and the body, so the base moves round it. Awake bodies only; a body without one checks an int.
    /// </summary>
    private static void PinRows(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = _bodies[i];
            if (c.PinCount == 0 || !_jact[i])
                continue;
            for (var j = 0; j < c.JointCount; j++)
            {
                ref var jt = ref c.Joints[j];
                if (!jt.PinSet)
                    continue;
                c.WorldFrame(j + 1, out var q, out var t);
                for (var k = 0; k < 3; k++)
                {
                    var off = jt.Pivot;
                    off[k] += Construct.PinReach;
                    var now = t + Vector3.Transform(off, q);
                    var e = now - (jt.PinPos + Vector3.Transform(off, jt.PinRot));
                    Row(c, (short)i, j + 1, now, c, (short)i, 0, now, false, Vector3.UnitX, e.X, true);
                    Row(c, (short)i, j + 1, now, c, (short)i, 0, now, false, Vector3.UnitY, e.Y, true);
                    Row(c, (short)i, j + 1, now, c, (short)i, 0, now, false, Vector3.UnitZ, e.Z, true);
                }
            }
        }
    }

    /// <summary>
    /// A body's mass and centre stay those of its rest pose, so what holds it up acts as though what hangs on its joints were where
    /// it was built. This puts right the difference: the turn from each jointed piece's weight, from where it is now against
    /// where it was built, through what holds the body up (ground, wheels, thrust). An arm reached out tips the body by its true
    /// moment, whatever the joints in between, and in free fall nothing turns it.
    /// </summary>
    private static void ArmWeight(Construct c, int index, Vector3 g, float dt)
    {
        var held = (c.Vel - c.ArmVel) / dt - g; // (what holds the body up, an acceleration: nothing in free fall)
        c.ArmVel = c.Vel;
        var most = g.Length() * 2;
        if (held.LengthSquared() > most * most)
            held *= most / held.Length(); // (a landing's spike isn't a load)
        var turn = Vector3.Zero;
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!jt.RootJoint)
                continue;
            var seg = j + 1;
            var rest = c.ToWorld(c.ToRest(seg, c.SegCom[seg]));
            turn -= Vector3.Cross(_jCom[_jBase[index] + j] - rest, held * c.SegMass[seg]);
        }
        c.AngVel += Construct.Mul(_invI[index], turn * dt);
    }

    /// <summary>
    /// Joints pass motion on, from the mass they share: a joint's momentum along another's motion, that joint's share of what it
    /// carries times the two motions at its centre (the mass matrix's off-diagonal). What changes of it this tick goes to the free
    /// joints of the pair, so a slider that hits its stop kicks the pendulum on it, and a swinging pendulum works the slider. Before the
    /// solve, so stops and contacts have the last word. Only for chains with a free joint in them.
    /// </summary>
    private static void CoupleJoints(Construct c, int index)
    {
        var slots = _jBase[index];
        for (var j = 0; j < c.JointCount; j++)
            c.Joints[j].CoupleNow = 0;
        for (var k = 0; k < c.CouplePairCount; k++)
        {
            ref var a = ref c.Joints[c.CouplePairs[k * 2]];
            ref var d = ref c.Joints[c.CouplePairs[k * 2 + 1]];
            int sa = slots + c.CouplePairs[k * 2], sd = slots + c.CouplePairs[k * 2 + 1];
            var com = _jCom[sd];
            var ua = a.Slides ? _jAxis[sa] : Vector3.Cross(_jAxis[sa], com - _jPivot[sa]);
            var ud = d.Slides ? _jAxis[sd] : Vector3.Cross(_jAxis[sd], com - _jPivot[sd]);
            var shared = _jMass[sd] * Vector3.Dot(ua, ud);
            a.CoupleNow += shared * d.Vel;
            d.CoupleNow += shared * a.Vel;
        }
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (jt.FreeMoving && jt.CoupleSet && !jt.Locked && !jt.Frozen && jt.Hinge < c.PartCount)
            {
                var passed = CoupleShare * (jt.CoupleNow - jt.Couple) * _jInvI[slots + j];
                jt.Vel -= passed;
                _jVel0[slots + j] -= passed; // (momentum from another joint, not something for the body to take back)
            }
            jt.Couple = jt.CoupleNow;
            jt.CoupleSet = true;
        }
    }
}
