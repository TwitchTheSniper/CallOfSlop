using System.Numerics;

namespace CallOfSlop;

/// <summary>One berth clamp arm of a station (a part with a "clampArm" block) and where its head is.</summary>
public struct ClampArm
{
    public int Part, Berth; // the arm's base part, and the berth (a dock part) it serves: the nearest to its shoulder
    public Vector3 Wrist, Out; // its wrist, and the way its pad faces back out from the hull it holds (the pad is HeadLen down -Out): the arm's part frame
    public ushort Ship; // the ship it reaches for or holds (0: none, it goes back to its saddle)
    public Vector3 GripS, OutS; // where on that ship its pad goes, and the hull's outward normal there: the ship's rest frame
    public bool Holds; // the ship is moored at its berth: the head stays on the hull
    public bool OnHull; // the head has reached the hull it holds, and moves with it from then on
    public ushort Refused; // host: a ship the berth wouldn't take when the head touched it (logged once); not reached for again while it stays in reach
}

public sealed class ClampArmSet
{
    public ClampArm[] Arms = [];
    public int N, PartCount = -1;
    public long HostStepped; // when the host's tick last stepped them (Environment.TickCount64): a host's renderer leaves them to it
}

/// <summary>
/// Station berth clamp arms (Karl, 2026-11-18; docs/sandbox/constructs/station.md "Clamp arms"). A berth's arm sits on the docking arm
/// by it; a ship come slowly (under <see cref="SeekSpeed"/> against the station) within its reach of a free berth is reached for: the
/// head runs out at <see cref="HeadSpeed"/> to the hull nearest the arm. When it touches, the host takes the ship (GameStationArms.cs):
/// its drives and flight stop and it is drawn in to the berth; moored, the head stays on its hull. Host and peers step the same rule
/// from the same poses, so a client's arm reaches when the host's does; the hold comes from the moorings the host sends
/// (Msg.Stations). Bounded: a station's arms (a dozen) by the ships near it, one ray when an arm picks a ship.
/// </summary>
public static class ConstructClampArms
{
    public const float HeadSpeed = 8f, SeekSpeed = 4f, TouchDist = 0.4f, HeadLen = 4.2f;

    /// <summary>A station's arms, found again when its parts change (a berth or an arm shot off renumbers them).</summary>
    public static ClampArmSet? Of(Construct st)
    {
        if (!st.IsStation)
            return null;
        var set = st.ClampArms ??= new ClampArmSet();
        if (set.PartCount == st.PartCount)
            return set.N > 0 ? set : null;
        set.PartCount = st.PartCount;
        set.N = 0;
        for (var i = 0; i < st.PartCount; i++)
        {
            var d = st.DefOf(i);
            if (!d.IsClampArm)
                continue;
            Shoulder(st, i, d, out var sh, out var rot);
            var berth = -1;
            var best = float.MaxValue;
            for (var j = 0; j < st.PartCount; j++)
            {
                if (!st.DefOf(j).IsDock)
                    continue;
                ConstructCargo.Box(st, j, out var mid, out _);
                st.WorldFrame(st.Parts[j].Seg, out var q, out var t);
                var dd = Vector3.DistanceSquared(t + Vector3.Transform(mid, q), sh);
                if (dd < best)
                    (best, berth) = (dd, j);
            }
            if (berth < 0)
                continue;
            if (set.N == set.Arms.Length)
                Array.Resize(ref set.Arms, Math.Max(4, set.N * 2));
            set.Arms[set.N++] = new ClampArm { Part = i, Berth = berth, Wrist = d.ClampStow, Out = Vector3.UnitY };
        }
        return set.N > 0 ? set : null;
    }

    /// <summary>An arm's shoulder in the world, and its part's turn.</summary>
    public static void Shoulder(Construct st, int part, ConstructPartDef d, out Vector3 world, out Quaternion rot)
    {
        Freeform.PartWorld(st, part, out var pos, out rot);
        world = pos + Vector3.Transform(d.ClampShoulder, rot);
    }

    /// <summary>
    /// Every peer, each tick or frame: each arm's target (the ship moored at its berth, else a ship slow within its reach of the free
    /// berth, else nothing) and its head moved toward it. On the host, <paramref name="touched"/> gets each arm whose head reached a
    /// ship it doesn't hold yet (index into the set).
    /// </summary>
    public static void Step(ConstructWorld world, Construct st, float dt, List<int>? touched, bool host)
    {
        if (Of(st) is not { } set)
            return;
        for (var k = 0; k < set.N; k++)
        {
            ref var a = ref set.Arms[k];
            if (a.Part >= st.PartCount || st.Parts[a.Part].Broken || a.Berth >= st.PartCount)
                continue;
            var d = st.DefOf(a.Part);
            Shoulder(st, a.Part, d, out var shoulder, out var rot);
            var inv = Quaternion.Conjugate(rot);
            Freeform.PartWorld(st, a.Part, out var partPos, out _);
            Pick(world, st, ref a, shoulder, d.ClampReach, host);
            Vector3 want, wantOut;
            if (a.Ship != 0 && world.Get(a.Ship) is { } ship)
            {
                var grip = ship.Pos + Vector3.Transform(a.GripS, ship.Rot);
                var nOut = Vector3.Transform(a.OutS, ship.Rot);
                want = Vector3.Transform(grip + nOut * HeadLen - partPos, inv);
                wantOut = Vector3.Transform(nOut, inv);
            }
            else
                (want, wantOut) = (d.ClampStow, Vector3.UnitY);
            var gap = want - a.Wrist;
            var len = gap.Length();
            var step = HeadSpeed * (a.Holds ? 2 : 1) * dt; // (a ship moored before the head reached it: the head catches up)
            if (!a.Holds)
                a.OnHull = false;
            if (a.OnHull || len <= step)
            {
                a.Wrist = want; // (on the hull, the head moves with it)
                a.OnHull = a.Holds;
            }
            else
                a.Wrist += gap * (step / len);
            a.Out = Vector3.Normalize(Vector3.Lerp(a.Out, wantOut, MathF.Min(1, dt * 3)) + wantOut * 1e-3f);
            if (touched != null && a.Ship != 0 && !a.Holds && len <= TouchDist + step)
                touched.Add(k);
        }
    }

    /// <summary>The arm's ship: the one moored at its berth, else keep the one it reaches for while it stays slow and in reach, else the nearest such.</summary>
    private static void Pick(ConstructWorld world, Construct st, ref ClampArm a, Vector3 shoulder, float reach, bool host)
    {
        ushort moored = 0;
        foreach (var b in world.Bodies)
            if (b.MooredTo == st.Id && b.MoorPart == a.Berth)
            {
                moored = b.Id;
                break;
            }
        if (moored != 0)
        {
            if (a.Ship != moored && world.Get(moored) is { } m)
            {
                Grip(world, m, shoulder, ref a);
                a.OnHull = false;
            }
            a.Ship = moored;
            a.Holds = true;
            return;
        }
        a.Holds = false;
        if (a.Ship != 0 && (world.Get(a.Ship) is not { } cur || !Reachable(world, st, a.Berth, cur, shoulder, reach, host) || NearestBerth(st, cur.WorldCom) != a.Berth))
            a.Ship = 0;
        if (a.Ship != 0)
            return;
        if (a.Refused != 0 && (world.Get(a.Refused) is not { } rf || !Reachable(world, st, a.Berth, rf, shoulder, reach, host)))
            a.Refused = 0; // (it has gone out of reach, or flown off: it may be reached for again when it comes back)
        Construct? best = null;
        var bestD = float.MaxValue;
        foreach (var b in world.Bodies)
        {
            if (b.Id == a.Refused || !Reachable(world, st, a.Berth, b, shoulder, reach, host))
                continue;
            var dd = Vector3.DistanceSquared(b.WorldCom, shoulder);
            if (dd < bestD)
                (bestD, best) = (dd, b);
        }
        if (best == null || NearestBerth(st, best.WorldCom) != a.Berth)
            return; // (a ship is the business of the berth it is nearest: two berths share each docking arm)
        foreach (var o in world.Bodies)
            if (o.MooredTo == st.Id && o.MoorPart == a.Berth)
                return; // (the berth is taken)
        Grip(world, best, shoulder, ref a);
        a.Ship = best.Id;
    }

    /// <summary>The station's berth (a dock part) whose box's middle is nearest a point.</summary>
    public static int NearestBerth(Construct st, Vector3 at)
    {
        var berth = -1;
        var best = float.MaxValue;
        for (var j = 0; j < st.PartCount; j++)
        {
            if (!st.DefOf(j).IsDock || st.Parts[j].Broken)
                continue;
            ConstructCargo.Box(st, j, out var mid, out _);
            st.WorldFrame(st.Parts[j].Seg, out var q, out var t);
            var dd = Vector3.DistanceSquared(t + Vector3.Transform(mid, q), at);
            if (dd < best)
                (best, berth) = (dd, j);
        }
        return berth;
    }

    /// <summary>
    /// A ship the arm may reach for: in space, free, slow against the station, its hull within reach of the shoulder, and one its berth
    /// would take (GameStations.StationMoorable). The host alone knows a ship's flight and when it was cast off: a ship just cast off from
    /// this station isn't reached for until it has left the berth it lay in (MoorAway, cleared by StationMoorScan) and the docks' wait is over.
    /// </summary>
    private static bool Reachable(ConstructWorld world, Construct st, int berth, Construct b, Vector3 shoulder, float reach, bool host) =>
        b != st && !b.IsStation && !b.Wreck && b.CarriedBy == 0 && b.PartCount > 0
        && (!host || b.SpaceShip != 0 && b.SpaceAway == 0 && b.MoorAway != st.Id && world.CarrierClock >= b.DockAway)
        && !b.Anchored && !b.Pinned && !b.Frozen && b.GrabbedBy == 255
        && (b.MooredTo == st.Id || b.Mass <= st.DefOf(berth).DockMaxMass && Game.BerthFits(st, berth, b, out _)) // (a berth takes the ships its size fits: berth classes by size)
        && (b.ThrusterCount > 0 || b.StabCount > 0) // (a ship, not a loose piece: GameStations.StationMoorable)
        && (b.MooredTo == 0 || b.MooredTo == st.Id)
        && Vector3.Distance(b.WorldCom, shoulder) - b.Radius * 0.5f < reach
        && (b.Vel - st.PointVel(b.WorldCom)).Length() < SeekSpeed;

    /// <summary>
    /// Where on a ship the pad goes: on its Docking Hatch nearest the arm, if it has one (Karl 2026-11-18: a ship's own docking connector
    /// for the arm, whatever the ship), else the middle of the face of its bounds that looks most at the arm, brought onto its hull by one ray.
    /// </summary>
    private static void Grip(ConstructWorld world, Construct ship, Vector3 shoulder, ref ClampArm a)
    {
        if (HatchGrip(ship, shoulder, ref a))
            return;
        var toArm = Vector3.Transform(shoulder - ship.WorldCom, Quaternion.Conjugate(ship.Rot));
        var ax = MathF.Abs(toArm.X) >= MathF.Abs(toArm.Y) && MathF.Abs(toArm.X) >= MathF.Abs(toArm.Z) ? 0 : MathF.Abs(toArm.Y) >= MathF.Abs(toArm.Z) ? 1 : 2;
        // (a ship mostly takes the clamp on its top or its side: the long axis' ends are its bow and stern)
        var size = ship.LocalMax - ship.LocalMin;
        var longAx = size.X >= size.Z ? 0 : 2;
        if (ax == longAx)
            ax = 1;
        var sign = (ax == 0 ? toArm.X : ax == 1 ? toArm.Y : toArm.Z) >= 0 ? 1f : -1f;
        var n = ax == 0 ? Vector3.UnitX * sign : ax == 1 ? Vector3.UnitY * sign : Vector3.UnitZ * sign;
        var face = ship.Com;
        var edge = sign > 0 ? ship.LocalMax : ship.LocalMin;
        if (ax == 0) face.X = edge.X;
        else if (ax == 1) face.Y = edge.Y;
        else face.Z = edge.Z;
        a.GripS = face;
        a.OutS = n;
        var nW = Vector3.Transform(n, ship.Rot);
        var from = ship.Pos + Vector3.Transform(face, ship.Rot) + nW * 2f;
        var depth = (ax == 0 ? size.X : ax == 1 ? size.Y : size.Z) + 2f;
        if (world.Raycast(from, -nW, depth, out var hit, only: ship))
            a.GripS = Vector3.Transform(hit.Point - ship.Pos, Quaternion.Conjugate(ship.Rot));
    }

    /// <summary>The pad on the ship's docking hatch (a door part with <c>"dock": true</c>) nearest the shoulder of those facing it, its outward face the hatch's up. False: it has none facing the arm.</summary>
    private static bool HatchGrip(Construct ship, Vector3 shoulder, ref ClampArm a)
    {
        var best = -1;
        var bestD = float.MaxValue;
        var inv = Quaternion.Conjugate(ship.Rot);
        for (var i = 0; i < ship.PartCount; i++)
        {
            ref var p = ref ship.Parts[i];
            if (p.Broken || p.D.Door is not { Dock: true })
                continue;
            var pw = ship.SegToWorld(p.Seg, p.Pos);
            if (Vector3.Dot(ship.SegToWorld(p.Seg, p.Pos + Rot24.Apply(p.Rot, Vector3.UnitY)) - pw, shoulder - pw) <= 0)
                continue; // (a hatch facing away from the arm, a belly hatch under an arm overhead: the hull instead)
            var d = Vector3.DistanceSquared(pw, shoulder);
            if (d < bestD)
                (best, bestD) = (i, d);
        }
        if (best < 0)
            return false;
        ref var h = ref ship.Parts[best];
        var at = ship.SegToWorld(h.Seg, h.Pos);
        var upW = Vector3.Normalize(ship.SegToWorld(h.Seg, h.Pos + Rot24.Apply(h.Rot, Vector3.UnitY)) - at);
        a.OutS = Vector3.Normalize(Vector3.Transform(upW, inv));
        a.GripS = Vector3.Transform(at - ship.Pos, inv) + a.OutS * 0.1f; // (on the lid)
        return true;
    }
}
