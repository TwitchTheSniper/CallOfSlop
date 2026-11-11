using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A piece riding a conveyor (<see cref="RouteType.Conveyor"/>): a run-time part that left its machine's die and is not a
/// part of any construct until it arrives, is taken off or drops. On the wire and in a save it is its key, never its id.
/// </summary>
public struct CRider
{
    public ushort Def; // this machine's id of the piece (Extrusion.Def of its key)
    public byte Route; // the conveyor it rides on its construct (255: its link went: it drops at the next step where it last was)
    public float At; // its front, metres along the link from where it starts
    public byte Health; // its health share (255: whole)
    public uint Paint;
    public Vector3 LastPos; // server: its centre and turn in the world at the last step (where it drops if its link goes)
    public Quaternion LastRot;
}

/// <summary>The pieces riding a construct's conveyors (fixed arrays; grown once, never per tick).</summary>
public sealed partial class Construct
{
    public const int MaxConveyed = 1024;
    public CRider[] Conveyed = [];
    public int ConveyedCount;
    public double ConveyedStamp; // when the riders' fronts were last set (ConstructConveyor.Clock): a frame draws them moved on from there

    public void ReserveConveyed(int n)
    {
        if (n > Conveyed.Length)
            Array.Resize(ref Conveyed, Math.Min(Math.Max(n, Math.Max(16, Conveyed.Length * 2)), MaxConveyed));
    }
}

/// <summary>The conveyors' side of the world: their step's clock and the bodies whose riders changed.</summary>
public sealed partial class ConstructWorld
{
    public int ConveyorTicks; // server: ticks towards the next conveyor step
    public readonly List<ushort> RidersDirty = new(32); // bodies whose riders were added, removed or dropped (CBodyKind.Riders)

    public void RidersChanged(Construct c)
    {
        if (!RidersDirty.Contains(c.Id))
            RidersDirty.Add(c.Id);
    }

    /// <summary>A piece into a Piece Rack, if it takes it. Server.</summary>
    public bool RackPut(Construct c, int part, ConstructPartDef d)
    {
        if (StoreOf(c, part) is not { } st || ConstructConveyor.RackRefusal(c, part, st, d) != null)
            return false;
        st.AddKind(d.Id, 1);
        LoadChanged = true;
        Changed(st);
        c.Wake();
        return true;
    }
}

/// <summary>
/// Roller conveyors (docs/sandbox/constructs/production.md, "Conveyors"): the large-piece link. A conveyor is a routed link
/// (<see cref="RouteType.Conveyor"/>) from a machine's Piece out to a Piece Rack's Piece in, set to a width and height within
/// both ends' openings (<see cref="CRoute.ConvW"/>, <see cref="CRoute.ConvH"/>). A piece cut at the die goes onto it whole
/// (<see cref="HandOff"/>) and rides at <see cref="Speed"/>, in order and spaced by its length, to the rack, which stores it
/// by kind. A cut, deleted or lost link drops every piece on it as a loose body where it was. Server, at 10 Hz, only bodies
/// with riders; nothing allocates.
/// </summary>
public static class ConstructConveyor
{
    public const int Every = 6; // ticks between steps (10 Hz)
    public const float Speed = 0.5f; // metres a second a piece rides (proposed)
    public const float Gap = 0.1f; // the least room between two pieces
    public const float BedTop = 0.06f; // a piece's underside above the link's line
    public const int MinSizeCm = 10, StepCm = 5;

    private static readonly double TickSeconds = 1.0 / Stopwatch.Frequency;

    /// <summary>Seconds on a steady clock (a frame draws riders moved on from <see cref="Construct.ConveyedStamp"/>).</summary>
    public static double Clock => Stopwatch.GetTimestamp() * TickSeconds;

    // ---- Sizes ---------------------------------------------------------------------------------------------------

    /// <summary>A piece's size as it rides: across (its x), up (its y) and along (its z), from its bounds.</summary>
    public static void Size(ConstructPartDef d, out float across, out float up, out float along)
    {
        var s = d.BoundsMax - d.BoundsMin;
        (across, up, along) = (s.X, s.Y, s.Z);
    }

    /// <summary>The opening of an end's conveyor port (metres), or zero.</summary>
    public static Vector2 Opening(Construct c, int part, byte port) =>
        part >= 0 && part < c.PartCount && ConstructRoutes.PortOf(c.DefOf(part), RouteType.Conveyor, port) is { } np ? np.Opening : Vector2.Zero;

    /// <summary>The largest a conveyor between these ends may be set to, in cm (the smaller of both openings, on the 5 cm steps).</summary>
    public static void Limit(Construct c, int from, byte fromPort, int to, byte toPort, out ushort w, out ushort h)
    {
        var a = Opening(c, from, fromPort);
        var b = Opening(c, to, toPort);
        var m = Vector2.Min(a, b);
        w = (ushort)Math.Max(MinSizeCm, (int)(m.X * 100 + 0.01f) / StepCm * StepCm);
        h = (ushort)Math.Max(MinSizeCm, (int)(m.Y * 100 + 0.01f) / StepCm * StepCm);
    }

    public static void Limit(Construct c, int route, out ushort w, out ushort h)
    {
        ref var r = ref c.Routes[route];
        Limit(c, r.From, r.FromPort, r.To, r.ToPort, out w, out h);
    }

    /// <summary>Why a conveyor can't be set to <paramref name="w"/> by <paramref name="h"/> cm, or null.</summary>
    public static string? SizeRefusal(Construct c, int route, int w, int h)
    {
        Limit(c, route, out var mw, out var mh);
        if (w < MinSizeCm || h < MinSizeCm)
            return $"a conveyor is at least {MinSizeCm} cm each way";
        if (w > mw || h > mh)
            return $"its ends take at most {mw / 100f:0.00} x {mh / 100f:0.00} m";
        return null;
    }

    // ---- The rack ------------------------------------------------------------------------------------------------

    /// <summary>Why a Piece Rack won't take <paramref name="d"/>, or null.</summary>
    public static string? RackRefusal(Construct c, int part, CStore st, ConstructPartDef d)
    {
        var rd = c.DefOf(part);
        if (!rd.IsPieceRack)
            return "that isn't a Piece Rack";
        if (!d.IsExtrusion)
            return $"a {rd.Name} holds pieces off an Extruder or a Former";
        Size(d, out var across, out var up, out var along);
        var big = MathF.Max(across, up);
        var small = MathF.Min(across, up);
        if (big > MathF.Max(rd.RackSection.X, rd.RackSection.Y) + 0.005f || small > MathF.Min(rd.RackSection.X, rd.RackSection.Y) + 0.005f || along > rd.RackLength + 0.005f)
            return $"the {rd.Name} holds pieces up to {rd.RackSection.X:0.##} x {rd.RackSection.Y:0.##} x {rd.RackLength:0.##} m";
        if (st.KindIndex(d.Id) < 0 && st.KindN >= CStore.MaxKinds || st.ItemCount >= rd.RackCount)
            return $"the {rd.Name} is full ({rd.RackCount} pieces)";
        return null;
    }

    // ---- The link ------------------------------------------------------------------------------------------------

    /// <summary>The conveyor leaving part <paramref name="part"/>'s Piece out, or -1.</summary>
    public static int Out(Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Parent == 255 && c.Routes[k].From == part)
                return k;
        return -1;
    }

    /// <summary>A link's length along its points (rest frame).</summary>
    public static float Length(Construct c, int route)
    {
        var a = ConstructRoutes.Point(c, route, -1, out _);
        var len = 0f;
        for (var i = 0; i <= c.Routes[route].Count; i++)
        {
            var b = ConstructRoutes.Point(c, route, i, out _);
            len += Vector3.Distance(a, b);
            a = b;
        }
        return len;
    }

    /// <summary>
    /// Where a piece whose middle is <paramref name="s"/> metres along the link sits, in the rest frame of the segment
    /// <paramref name="seg"/> its span starts in: its origin and turn (x across, y up, z along the link).
    /// </summary>
    public static void Pose(Construct c, int route, float s, ConstructPartDef d, out Vector3 origin, out Quaternion rot, out int seg)
    {
        var a = ConstructRoutes.Point(c, route, -1, out seg);
        var b = a;
        var count = c.Routes[route].Count;
        var segLen = 0f;
        var i = 0;
        for (; i <= count; i++)
        {
            b = ConstructRoutes.Point(c, route, i, out var sb);
            segLen = Vector3.Distance(a, b);
            if (s <= segLen || i == count)
                break;
            s -= segLen;
            a = b;
            seg = sb;
        }
        var z = segLen > 1e-4f ? (b - a) / segLen : Vector3.UnitZ;
        var mid = a + z * Math.Clamp(s, 0, segLen);
        var up = Vector3.UnitY - z * z.Y;
        up = up.LengthSquared() > 1e-4f ? Vector3.Normalize(up) : Vector3.UnitX;
        var x = Vector3.Cross(up, z);
        rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
        // The piece's middle across and along on the line, its underside on the rollers.
        var centre = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var local = new Vector3(centre.X, d.BoundsMin.Y - BedTop, centre.Z);
        origin = mid - Vector3.Transform(local, rot);
    }

    /// <summary>A rider's centre and turn in the world now (server: where it drops).</summary>
    private static void WorldPose(Construct c, int route, in CRider r, ConstructPartDef d, out Vector3 centre, out Quaternion rot)
    {
        Size(d, out _, out _, out var along);
        Pose(c, route, r.At - along * 0.5f, d, out var origin, out var lr, out var seg);
        c.WorldFrame(seg, out var q, out var t);
        rot = q * lr;
        centre = t + Vector3.Transform(origin, q) + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, rot);
    }

    // ---- The machine's side --------------------------------------------------------------------------------------

    /// <summary>
    /// A finished piece off a machine's die onto its conveyor: true when it went (the part left the construct: indices
    /// moved). Else <paramref name="why"/> says why it waits on the die.
    /// </summary>
    public static bool HandOff(ConstructWorld world, Construct c, int route, int product, out ExtrudeStall why)
    {
        why = ExtrudeStall.None;
        ref var r = ref c.Routes[route];
        if (r.FirstCut <= r.Count)
        {
            why = ExtrudeStall.ConveyorCut;
            return false;
        }
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        if (Extrusion.Def(was.ExKey with { Growing = false }) is not { } piece)
        {
            why = ExtrudeStall.BadShape;
            return false;
        }
        Size(piece, out var across, out var up, out var along);
        if (across > r.ConvW / 100f + 0.005f || up > r.ConvH / 100f + 0.005f)
        {
            why = ExtrudeStall.ConveyorSmall;
            return false;
        }
        // Room at the start: the last piece on it has gone this one's length and the gap.
        var last = -1;
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Route == route)
                last = i;
        if (last >= 0)
        {
            Size(ConstructParts.Def(c.Conveyed[last].Def), out _, out _, out var lastAlong);
            if (c.Conveyed[last].At - lastAlong - Gap < along)
            {
                why = ExtrudeStall.ConveyorFull;
                return false;
            }
        }
        if (c.ConveyedCount >= Construct.MaxConveyed || along > Length(c, route) + 0.01f)
        {
            why = ExtrudeStall.ConveyorFull;
            return false;
        }
        c.ReserveConveyed(c.ConveyedCount + 1);
        var rider = new CRider
        {
            Def = piece.Id,
            Route = (byte)route,
            At = along,
            Health = (byte)Math.Clamp((int)MathF.Round(pp.Health / MathF.Max(1, was.Health) * 255), 1, 255),
            Paint = pp.Paint,
        };
        WorldPose(c, route, rider, piece, out rider.LastPos, out rider.LastRot);
        c.Conveyed[c.ConveyedCount++] = rider;
        world.RemovePart(c, product);
        c.Wake();
        world.RidersChanged(c);
        return true;
    }

    // ---- Each step (server) ----------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world)
    {
        if (++world.ConveyorTicks < Every)
            return;
        world.ConveyorTicks = 0;
        const float dt = Every / (float)World.TickRate;
        var now = Clock;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.ConveyedCount == 0)
                continue;
            if (c.Dirty)
                c.Rebuild();
            if (StepBody(world, c, dt))
                world.RidersChanged(c);
            c.ConveyedStamp = now;
        }
    }

    /// <summary>Moves a body's riders on; true when one arrived or dropped (the clients are told).</summary>
    private static bool StepBody(ConstructWorld world, Construct c, float dt)
    {
        Span<float> lengths = stackalloc float[Construct.MaxRoutes];
        lengths.Clear();
        var changed = false;
        var prevRoute = -1;
        var prevBack = 0f;
        var i = 0;
        while (i < c.ConveyedCount)
        {
            ref var r = ref c.Conveyed[i];
            var d = ConstructParts.Get(r.Def);
            var k = r.Route;
            if (d == null || k >= c.RouteCount || c.Routes[k].Type != RouteType.Conveyor || c.Routes[k].FirstCut <= c.Routes[k].Count)
            {
                Drop(world, c, i, d, k < c.RouteCount && c.Routes[k].Type == RouteType.Conveyor ? k : -1);
                changed = true;
                continue; // (the next one moved down into i)
            }
            Size(d, out _, out _, out var along);
            if (lengths[k] <= 0)
                lengths[k] = MathF.Max(1e-3f, Length(c, k));
            var first = k != prevRoute;
            var limit = first ? lengths[k] : prevBack - Gap;
            var at = MathF.Min(r.At + Speed * dt, limit);
            if (at > r.At)
                r.At = at;
            if (first && r.At >= lengths[k] - 1e-3f && Deliver(world, c, k, d))
            {
                Remove(c, i);
                changed = true;
                prevRoute = k;
                prevBack = lengths[k] + Gap; // (the next one may close up to the end)
                continue;
            }
            WorldPose(c, k, r, d, out r.LastPos, out r.LastRot);
            prevRoute = k;
            prevBack = r.At - along;
            i++;
        }
        return changed;
    }

    /// <summary>The lead piece into what the link ends at (a Piece Rack): true when it went in.</summary>
    private static bool Deliver(ConstructWorld world, Construct c, int route, ConstructPartDef d)
    {
        var to = c.Routes[route].To;
        return to < c.PartCount && c.DefOf(to).IsPieceRack && world.RackPut(c, to, d);
    }

    private static void Remove(Construct c, int i)
    {
        c.ConveyedCount--;
        if (i < c.ConveyedCount)
            Array.Copy(c.Conveyed, i + 1, c.Conveyed, i, c.ConveyedCount - i);
    }

    /// <summary>Rider <paramref name="i"/> comes off as a loose body where it is (its link's place, or where it last was), moving as the body moved.</summary>
    private static void Drop(ConstructWorld world, Construct c, int i, ConstructPartDef? d, int route)
    {
        var r = c.Conveyed[i];
        Remove(c, i);
        if (d == null)
            return;
        Vector3 centre;
        Quaternion rot;
        if (route >= 0)
            WorldPose(c, route, r, d, out centre, out rot);
        else
            (centre, rot) = (r.LastPos, r.LastRot);
        if (ConstructEdits.DropLoose(world, d, centre, rot) is not { } body)
            return;
        body.Owner = c.Owner;
        body.Vel = c.PointVel(centre);
        body.AngVel = c.AngVel;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, d.Health * r.Health / 255f);
        np.Paint = r.Paint;
        world.Events.Add($"a {d.Name} dropped off its conveyor");
    }

    /// <summary>Drops every rider of a body (it is going: its link with it).</summary>
    public static void DropAll(ConstructWorld world, Construct c)
    {
        while (c.ConveyedCount > 0)
            Drop(world, c, c.ConveyedCount - 1, ConstructParts.Get(c.Conveyed[c.ConveyedCount - 1].Def), -1);
    }

    // ---- When links are renumbered ---------------------------------------------------------------------------------

    /// <summary>After the links were renumbered (<paramref name="map"/>: old index to new, -1 gone): riders follow; a gone link's riders wait to drop where they were.</summary>
    public static void Remap(Construct c, ReadOnlySpan<int> map, int oldCount)
    {
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            r.Route = r.Route < oldCount && map[r.Route] >= 0 ? (byte)map[r.Route] : (byte)255;
        }
    }

    /// <summary>A split: riders whose link went to <paramref name="dst"/> go with it (<paramref name="map"/>: src link to dst link, -1 stays).</summary>
    public static void MoveTo(Construct src, ReadOnlySpan<int> map, Construct dst)
    {
        var i = 0;
        while (i < src.ConveyedCount)
        {
            var r = src.Conveyed[i];
            if (r.Route < map.Length && map[r.Route] >= 0 && dst.ConveyedCount < Construct.MaxConveyed)
            {
                r.Route = (byte)map[r.Route];
                dst.ReserveConveyed(dst.ConveyedCount + 1);
                dst.Conveyed[dst.ConveyedCount++] = r;
                Remove(src, i);
                continue;
            }
            i++;
        }
    }

    /// <summary>A join: <paramref name="b"/>'s riders onto <paramref name="a"/>, its links now from <paramref name="baseRoute"/>.</summary>
    public static void Merge(Construct a, Construct b, int baseRoute)
    {
        for (var i = 0; i < b.ConveyedCount && a.ConveyedCount < Construct.MaxConveyed; i++)
        {
            var r = b.Conveyed[i];
            r.Route = baseRoute + r.Route < Construct.MaxRoutes ? (byte)(baseRoute + r.Route) : (byte)255;
            a.ReserveConveyed(a.ConveyedCount + 1);
            a.Conveyed[a.ConveyedCount++] = r;
        }
        b.ConveyedCount = 0;
    }

    // ---- The wire (protocol 244: CBodyKind.Riders) -------------------------------------------------------------------

    /// <summary>One body's riders, the whole list: u16 body, u16 n, each u8 link, u16 front (cm), u8 health, u32 paint, then the piece's key.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Riders).U16(c.Id).U16((ushort)c.ConveyedCount);
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            w.U8(r.Route).U16((ushort)Math.Clamp((int)MathF.Round(r.At * 100), 0, ushort.MaxValue)).U8(r.Health).U32(r.Paint);
            Extrusion.Write(w, ConstructParts.Def(r.Def));
        }
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        int n = r.U16();
        if (n > Construct.MaxConveyed)
            throw new MalformedPacketException($"a body of {n} riders");
        var c = world.Get(id);
        c?.ReserveConveyed(n);
        if (c != null)
            c.ConveyedCount = 0;
        for (var i = 0; i < n; i++)
        {
            var route = r.U8();
            var at = r.U16() / 100f;
            var hp = r.U8();
            var paint = r.U32();
            if (r.U16() != Extrusion.WireMark)
                throw new MalformedPacketException("a rider that isn't a piece");
            var def = Extrusion.Read(ref r);
            if (c != null)
                c.Conveyed[c.ConveyedCount++] = new CRider { Def = def, Route = route, At = at, Health = hp, Paint = paint };
        }
        if (c != null)
            c.ConveyedStamp = Clock;
    }

    // ---- Saves (after a body's behaviour brief: an older save ends before them) ------------------------------------------

    public static void WriteSave(SaveWriter w, ReadOnlySpan<CRoute> routes, ReadOnlySpan<CRider> riders, WorldNames names)
    {
        var n = 0;
        foreach (ref readonly var r in routes)
            if (r.Type == RouteType.Conveyor)
                n++;
        w.I32(n);
        for (var k = 0; k < routes.Length; k++)
            if (routes[k].Type == RouteType.Conveyor)
                w.U8((byte)k).U16(routes[k].ConvW).U16(routes[k].ConvH);
        w.I32(riders.Length);
        foreach (ref readonly var r in riders)
            w.U8(r.Route).F32(r.At).U8(r.Health).U32(r.Paint).U16(names.Part(r.Def));
    }

    /// <summary>What a save holds of a body's conveyors: read before the body is made, put on it once its links are (<see cref="Apply"/>).</summary>
    public sealed class Saved
    {
        public readonly List<(byte Route, ushort W, ushort H)> Sizes = new();
        public readonly List<CRider> Riders = new();
    }

    /// <summary>Reads <see cref="WriteSave"/>'s bytes, or null when the save ends before them (a rider whose piece this build can't make is left out).</summary>
    public static Saved? ReadSave(SaveReader r, int bodyEnd, WorldNamesRead names)
    {
        if (!r.Has(4) || r.Pos + 4 > bodyEnd)
            return null;
        var saved = new Saved();
        var n = r.I32();
        if (n < 0 || n > Construct.MaxRoutes)
            throw new WorldFileException($"{n} conveyor sizes");
        for (var i = 0; i < n; i++)
            saved.Sizes.Add((r.U8(), r.U16(), r.U16()));
        var m = r.I32();
        if (m < 0 || m > Construct.MaxConveyed)
            throw new WorldFileException($"{m} riders");
        for (var i = 0; i < m; i++)
        {
            var route = r.U8();
            var at = r.F32();
            var hp = r.U8();
            var paint = r.U32();
            var part = r.U16();
            var def = part < names.Parts.Length ? names.Parts[part] : ConstructParts.None;
            if (def == ConstructParts.None || !float.IsFinite(at) || ConstructParts.Get(def) is not { IsExtrusion: true })
                continue;
            saved.Riders.Add(new CRider { Def = def, Route = route, At = MathF.Max(0, at), Health = Math.Max((byte)1, hp), Paint = paint });
        }
        return saved;
    }

    /// <summary>A save's conveyor sizes and riders onto its body, once its links are there.</summary>
    public static void Apply(Construct c, Saved s)
    {
        foreach (var (k, w, h) in s.Sizes)
            if (k < c.RouteCount && c.Routes[k].Type == RouteType.Conveyor)
                (c.Routes[k].ConvW, c.Routes[k].ConvH) = (Math.Max((ushort)MinSizeCm, w), Math.Max((ushort)MinSizeCm, h));
        c.ReserveConveyed(s.Riders.Count);
        c.ConveyedCount = 0;
        foreach (var r in s.Riders)
            c.Conveyed[c.ConveyedCount++] = r with { LastPos = c.Pos, LastRot = c.Rot };
    }
}
