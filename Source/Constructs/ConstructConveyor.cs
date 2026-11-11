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
    public byte ViaPlus1; // the conveyor it came off through a junction, plus one, while its back is still in the junction (0: none): its pose there follows the junction's lead-in
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

    public ushort[] PieceRacks = []; // its Piece Racks (made with the caches: ConstructConveyor.BuildRacks), for drawing what they hold
    public int PieceRackCount;

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

    /// <summary>
    /// A piece off a Piece Rack, a loose body beside it, its middle over the rack's top: the one drawn where the player looks
    /// (<paramref name="eye"/> along <paramref name="look"/>), else the last put in. Server; null when empty.
    /// </summary>
    public ConstructPartDef? RackTake(Construct c, int part, Vector3 eye = default, Vector3 look = default)
    {
        if (StoreOf(c, part) is not { KindN: > 0 } st)
            return null;
        var def = st.KindDef![st.KindN - 1];
        if (look != Vector3.Zero && ConstructConveyor.RackAimed(c, part, st, eye, look) is var aimed and > 0)
            def = (ushort)aimed;
        if (ConstructParts.Get(def) is not { } d)
            return null;
        c.PartBounds(part, out var mn, out var mx);
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        var top = new Vector3((mn.X + mx.X) * 0.5f, mx.Y + (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.1f, (mn.Z + mx.Z) * 0.5f);
        var turn = q * Rot24.Quat(c.Parts[part].Rot);
        if (ConstructEdits.DropLoose(this, d, t + Vector3.Transform(top, q), turn) is not { } body)
            return null;
        body.Owner = c.Owner;
        st.AddKind(d.Id, -1);
        LoadChanged = true;
        Changed(st);
        c.Wake();
        return d;
    }

    /// <summary>A piece into a Piece Rack, if it takes it. Server.</summary>
    public bool RackPut(Construct c, int part, ConstructPartDef d)
    {
        if (StoreOf(c, part) is not { } st || ConstructConveyor.RackCheck(c, part, st, d) != 0)
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

    /// <summary>Whether a Piece Rack takes <paramref name="d"/>: 0, or 1 not a rack, 2 not a piece, 3 too big, 4 full (no text: the step asks every time the lead piece waits).</summary>
    public static int RackCheck(Construct c, int part, CStore st, ConstructPartDef d)
    {
        var rd = c.DefOf(part);
        if (!rd.IsPieceRack)
            return 1;
        if (!d.IsExtrusion)
            return 2;
        Size(d, out var across, out var up, out var along);
        var big = MathF.Max(across, up);
        var small = MathF.Min(across, up);
        if (big > MathF.Max(rd.RackSection.X, rd.RackSection.Y) + 0.005f || small > MathF.Min(rd.RackSection.X, rd.RackSection.Y) + 0.005f || along > rd.RackLength + 0.005f)
            return 3;
        if (st.KindIndex(d.Id) < 0 && st.KindN >= CStore.MaxKinds || st.ItemCount >= rd.RackCount)
            return 4;
        return 0;
    }

    /// <summary>A Piece Rack's look line: how many it holds of how many, each kind and its count (the first few), and F. Either side, from the store as heard.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool near)
    {
        var sb = new System.Text.StringBuilder($"{d.Name}: {st.ItemCount}/{d.RackCount} pieces");
        const int listed = 4;
        for (var k = 0; k < st.KindN && k < listed; k++)
            if (ConstructParts.Get(st.KindDef![k]) is { } kd)
                sb.Append(k == 0 ? ": " : ", ").Append(st.KindCount![k]).Append(" x ").Append(kd.Name);
        if (st.KindN > listed)
            sb.Append($" and {st.KindN - listed} more kinds");
        if (st.KindN > 0 && near)
            sb.Append($" - {SandboxKeys.Use}: take the one you look at off");
        return sb.ToString();
    }

    // ---- What a rack holds, drawn (RouteRender.DrawRacks) and aimed at (F) ------------------------------------------------

    public const int RackDrawn = 24; // the most pieces a rack draws (the rest are counted in its look line)
    private const float RackSide = 0.115f, RackBed = 0.133f, RackEnd = 0.074f, RackGap = 0.02f, RackLayer = 0.005f; // (inside its posts, over its slats, past its sill: PieceRackParts.cs)

    /// <summary>One piece as a rack shows it: its def and where its own origin sits in the rack part's frame (unturned).</summary>
    public struct RackSlot
    {
        public ushort Def;
        public Vector3 Origin;
    }

    /// <summary>With the caches: a body's Piece Racks (they are special parts: they have stores).</summary>
    public static void BuildRacks(Construct c)
    {
        c.PieceRackCount = 0;
        foreach (var i in c.Specials)
        {
            if (!c.Parts[i].D.IsPieceRack)
                continue;
            if (c.PieceRacks.Length == c.PieceRackCount)
                Array.Resize(ref c.PieceRacks, Math.Max(4, c.PieceRackCount * 2));
            c.PieceRacks[c.PieceRackCount++] = (ushort)i;
        }
    }

    /// <summary>
    /// Where a rack's pieces lie, in store order: side by side across its bed from the -z end, 2 cm apart, a layer on the
    /// tallest of the one below (5 mm over it) while a layer starts below the rack's top; at most <paramref name="slots"/>'s
    /// length. Both sides lay it out the same from the store, so F picks what the player sees. Returns how many.
    /// </summary>
    public static int RackLayout(ConstructPartDef rd, CStore st, Span<RackSlot> slots)
    {
        float left = rd.BoundsMin.X + RackSide, right = rd.BoundsMax.X - RackSide, end = rd.BoundsMin.Z + RackEnd;
        var x = left;
        var y = rd.BoundsMin.Y + RackBed;
        var top = y;
        var n = 0;
        for (var k = 0; k < st.KindN; k++)
        {
            if (ConstructParts.Get(st.KindDef![k]) is not { } d)
                continue;
            Size(d, out var across, out var up, out _);
            for (var j = 0; j < st.KindCount![k]; j++)
            {
                if (n >= slots.Length)
                    return n;
                if (x > left && x + across > right + 1e-3f)
                {
                    x = left;
                    y = top + RackLayer;
                    if (y >= rd.BoundsMax.Y)
                        return n;
                }
                slots[n++] = new RackSlot { Def = d.Id, Origin = new Vector3(x, y, end) - d.BoundsMin };
                x += across + RackGap;
                top = MathF.Max(top, y + up);
            }
        }
        return n;
    }

    /// <summary>The def of the drawn piece a ray from <paramref name="eye"/> along <paramref name="look"/> meets first, or -1.</summary>
    public static int RackAimed(Construct c, int part, CStore st, Vector3 eye, Vector3 look)
    {
        Span<RackSlot> slots = stackalloc RackSlot[RackDrawn];
        var rd = c.DefOf(part);
        var n = RackLayout(rd, st, slots);
        if (n == 0)
            return -1;
        // Into the rack part's own frame (unturned), where the slots are boxes.
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(p.Rot);
        var inv = Quaternion.Conjugate(turn);
        var o = Vector3.Transform(eye - (t + Vector3.Transform(p.Pos, q)), inv);
        var dir = Vector3.Transform(look, inv);
        var best = -1;
        var near = 6f;
        for (var i = 0; i < n; i++)
        {
            var d = ConstructParts.Def(slots[i].Def);
            if (Geometry.RayAabb(slots[i].Origin + d.BoundsMin, slots[i].Origin + d.BoundsMax, o, dir, near) is { } hit && hit < near)
            {
                near = hit;
                best = slots[i].Def;
            }
        }
        return best;
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
    public static void Pose(Construct c, int route, float s, ConstructPartDef d, out Vector3 origin, out Quaternion rot, out int seg, int via = -1)
    {
        var a = ConstructRoutes.Point(c, route, -1, out seg);
        var b = a;
        var count = c.Routes[route].Count;
        var segLen = 0f;
        Vector3 z, mid;
        if (s < 0 && via >= 0 && LeadIn(c, route, via, out var inEnd, out var centre, out var outStart) is var lead and > 0)
        {
            // Still in the junction it came through: on its lead-in, from the input's end through the junction's middle to here.
            var back = MathF.Min(-s, lead);
            var second = Vector3.Distance(centre, outStart);
            (a, b, var t) = back <= second ? (centre, outStart, second - back) : (inEnd, centre, lead - back);
            segLen = Vector3.Distance(a, b);
            z = segLen > 1e-4f ? (b - a) / segLen : Vector3.UnitZ;
            mid = a + z * MathF.Max(0, t);
        }
        else
        {
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
            z = segLen > 1e-4f ? (b - a) / segLen : Vector3.UnitZ;
            mid = a + z * MathF.Min(s, segLen); // (before its start: on, back along its first segment)
        }
        var up = Vector3.UnitY - z * z.Y;
        up = up.LengthSquared() > 1e-4f ? Vector3.Normalize(up) : Vector3.UnitX;
        var x = Vector3.Cross(up, z);
        rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
        // The piece's middle across and along on the line, its underside on the rollers.
        var middle = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var local = new Vector3(middle.X, d.BoundsMin.Y - BedTop, middle.Z);
        origin = mid - Vector3.Transform(local, rot);
    }

    /// <summary>A rider's centre and turn in the world now (server: where it drops).</summary>
    private static void WorldPose(Construct c, int route, in CRider r, ConstructPartDef d, out Vector3 centre, out Quaternion rot)
    {
        Size(d, out _, out _, out var along);
        Pose(c, route, r.At - along * 0.5f, d, out var origin, out var lr, out var seg, r.ViaPlus1 - 1);
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
        var rider = new CRider
        {
            Def = piece.Id,
            Route = (byte)route,
            At = along,
            Health = (byte)Math.Clamp((int)MathF.Round(pp.Health / MathF.Max(1, was.Health) * 255), 1, 255),
            Paint = pp.Paint,
        };
        WorldPose(c, route, rider, piece, out rider.LastPos, out rider.LastRot);
        Insert(c, rider);
        world.RemovePart(c, product);
        c.Wake();
        world.RidersChanged(c);
        return true;
    }

    // ---- Junctions: splitters and joins ------------------------------------------------------------------------------

    /// <summary>
    /// The lead-in through a junction from conveyor <paramref name="via"/>'s end to conveyor <paramref name="route"/>'s start:
    /// the input's end, the junction's middle (at the input's height) and the output's start (rest frame of the junction's
    /// segment). Its length, or 0 when they don't meet at one junction.
    /// </summary>
    public static float LeadIn(Construct c, int route, int via, out Vector3 inEnd, out Vector3 centre, out Vector3 outStart)
    {
        inEnd = centre = outStart = default;
        if (via >= c.RouteCount || route >= c.RouteCount || c.Routes[via].Type != RouteType.Conveyor)
            return 0;
        int j = c.Routes[route].From;
        if (c.Routes[route].Parent != 255 || c.Routes[via].To != j || j >= c.PartCount)
            return 0;
        inEnd = ConstructRoutes.Point(c, via, c.Routes[via].Count, out _);
        outStart = ConstructRoutes.Point(c, route, -1, out _);
        ref var p = ref c.Parts[j];
        var d = c.DefOf(j);
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var port = ConstructRoutes.PortOf(d, RouteType.Conveyor, c.Routes[via].ToPort);
        centre = p.Pos + Rot24.Apply(p.Rot, new Vector3(mid.X, port?.At.Y ?? mid.Y, mid.Z));
        return Vector3.Distance(inEnd, centre) + Vector3.Distance(centre, outStart);
    }

    /// <summary>
    /// How far back into each conveyor's end a piece that went on through its junction still lies (metres, by link): the
    /// lead piece behind it stops that much short. One walk over the riders; only those still in a junction cost a look.
    /// </summary>
    public static void Tails(Construct c, Span<float> tail)
    {
        tail.Clear();
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            if (r.ViaPlus1 == 0 || r.At >= MaxPieceAlong || ConstructParts.Get(r.Def) is not { } d)
                continue;
            Size(d, out _, out _, out var along);
            var via = r.ViaPlus1 - 1;
            if (r.At - along >= 0 || via >= tail.Length)
                continue;
            var into = along - r.At - LeadIn(c, r.Route, via, out _, out _, out _);
            if (into > tail[via])
                tail[via] = into;
        }
    }

    private const float MaxPieceAlong = 200f; // (longer than any piece: a rider this far on has left its junction)

    /// <summary>A rider onto its link behind the others on it (a body's riders stay grouped by link, front first: the step and the drawing read them so).</summary>
    private static void Insert(Construct c, CRider r)
    {
        c.ReserveConveyed(c.ConveyedCount + 1);
        var at = c.ConveyedCount;
        for (var i = c.ConveyedCount - 1; i >= 0; i--)
            if (c.Conveyed[i].Route == r.Route)
            {
                at = i + 1;
                break;
            }
        if (at < c.ConveyedCount)
            Array.Copy(c.Conveyed, at, c.Conveyed, at + 1, c.ConveyedCount - at);
        c.Conveyed[at] = r;
        c.ConveyedCount++;
    }

    /// <summary>The last rider on a link, or -1.</summary>
    private static int LastOn(Construct c, int route)
    {
        for (var i = c.ConveyedCount - 1; i >= 0; i--)
            if (c.Conveyed[i].Route == route)
                return i;
        return -1;
    }

    /// <summary>Whether conveyor <paramref name="o"/> takes piece <paramref name="d"/> at its start now (whole, fits its size, room behind the last on it).</summary>
    private static bool Takes(Construct c, int o, ConstructPartDef d, float startAt)
    {
        ref var r = ref c.Routes[o];
        if (r.FirstCut <= r.Count)
            return false;
        Size(d, out var across, out var up, out var along);
        if (across > r.ConvW / 100f + 0.005f || up > r.ConvH / 100f + 0.005f || along > Length(c, o) + 0.01f)
            return false;
        if (LastOn(c, o) is var last and >= 0)
        {
            Size(ConstructParts.Def(c.Conveyed[last].Def), out _, out _, out var lastAlong);
            if (c.Conveyed[last].At - lastAlong - Gap < startAt)
                return false;
        }
        return c.ConveyedCount < Construct.MaxConveyed;
    }

    /// <summary>
    /// The lead piece of conveyor <paramref name="k"/> through the junction it ends at onto one of its outputs: round robin
    /// from where the last went (the input's <see cref="CRoute.Cursor"/>), past any that is cut, too small or full. True when it went.
    /// </summary>
    private static bool Pass(Construct c, int k, int lead)
    {
        int j = c.Routes[k].To;
        var d = ConstructParts.Def(c.Conveyed[lead].Def);
        Span<byte> outs = stackalloc byte[8];
        var n = 0;
        for (var o = 0; o < c.RouteCount && n < outs.Length; o++)
            if (c.Routes[o].Type == RouteType.Conveyor && c.Routes[o].Parent == 255 && c.Routes[o].From == j)
                outs[n++] = (byte)o;
        for (var t = 0; t < n; t++)
        {
            var pick = (c.Routes[k].Cursor + t) % n;
            int o = outs[pick];
            var lead0 = LeadIn(c, o, k, out _, out _, out _);
            if (!Takes(c, o, d, -lead0))
                continue;
            var r = c.Conveyed[lead];
            Remove(c, lead);
            r.Route = (byte)o;
            r.At = -lead0;
            r.ViaPlus1 = (byte)(k + 1);
            Insert(c, r);
            c.Routes[k].Cursor = (byte)((pick + 1) % n);
            for (var u = 0; u < n; u++)
                c.Routes[outs[u]].Timer = k + 1; // (a join: the input served last waits behind the others)
            return true;
        }
        return false;
    }

    // ---- Each step (server) ----------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world, IWorldQuery query)
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
            if (StepBody(world, query, c, dt, server: true))
                world.RidersChanged(c);
            c.ConveyedStamp = now;
        }
    }

    /// <summary>
    /// A client's riders between the server's messages, by the same rule (no arrival, no junction: those are the server's
    /// word). Every frame; steps a body at most ten times a second.
    /// </summary>
    public static void ClientStep(ConstructWorld world)
    {
        var now = Clock;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.ConveyedCount == 0 || now - c.ConveyedStamp < Every / (double)World.TickRate || c.Dirty)
                continue;
            StepBody(world, null, c, (float)Math.Min(now - c.ConveyedStamp, 1.0), server: false);
            c.ConveyedStamp = now;
        }
    }

    /// <summary>Moves a body's riders on; true when one arrived, passed a junction or dropped (the clients are told).</summary>
    private static bool StepBody(ConstructWorld world, IWorldQuery? query, Construct c, float dt, bool server)
    {
        Span<float> lengths = stackalloc float[Construct.MaxRoutes];
        Span<float> tail = stackalloc float[Construct.MaxRoutes];
        Span<byte> waiting = stackalloc byte[16]; // conveyors whose lead waits at a junction this step
        lengths.Clear();
        Tails(c, tail);
        var nWaiting = 0;
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
                if (!server)
                {
                    i++;
                    continue; // (the server drops it and says so)
                }
                Drop(world, c, i, d, k < c.RouteCount && c.Routes[k].Type == RouteType.Conveyor ? k : -1);
                changed = true;
                continue; // (the next one moved down into i)
            }
            Size(d, out _, out _, out var along);
            if (lengths[k] <= 0)
                lengths[k] = MathF.Max(1e-3f, Length(c, k));
            var first = k != prevRoute;
            var end = lengths[k] - (tail[k] > 0 ? tail[k] + Gap : 0);
            var limit = first ? end : prevBack - Gap;
            var at = MathF.Min(r.At + Speed * dt, limit);
            if (at > r.At)
                r.At = at;
            if (r.ViaPlus1 != 0 && r.At - along >= 0)
                r.ViaPlus1 = 0; // (its back has left the junction)
            if (server && first && r.At >= lengths[k] - 1e-3f)
            {
                var to = c.Routes[k].To;
                if (to < c.PartCount && c.DefOf(to).IsConveyorJunction)
                {
                    if (nWaiting < waiting.Length)
                        waiting[nWaiting++] = k;
                }
                else if (Deliver(world, query!, c, k, r, d))
                {
                    Remove(c, i);
                    changed = true;
                    prevRoute = k;
                    prevBack = lengths[k] + Gap; // (the next one may close up to the end)
                    continue;
                }
            }
            if (server)
                WorldPose(c, k, r, d, out r.LastPos, out r.LastRot);
            prevRoute = k;
            prevBack = r.At - along;
            i++;
        }
        // Leads at junctions go on, the one that waited longest first (a join takes its inputs in turn: Timer is the last served, plus one).
        for (var a = 0; a < nWaiting; a++)
            for (var b = a + 1; b < nWaiting; b++)
                if (Turn(c, waiting[b]) < Turn(c, waiting[a]))
                    (waiting[a], waiting[b]) = (waiting[b], waiting[a]);
        for (var w = 0; w < nWaiting; w++)
            for (var lead = 0; lead < c.ConveyedCount; lead++)
                if (c.Conveyed[lead].Route == waiting[w])
                {
                    if (Pass(c, waiting[w], lead))
                        changed = true;
                    break;
                }
        return changed;
    }

    /// <summary>A waiting input's place in its junction's turn: how far after the input it served last (0: next).</summary>
    private static int Turn(Construct c, int k)
    {
        int j = c.Routes[k].To;
        for (var o = 0; o < c.RouteCount; o++)
            if (c.Routes[o].Type == RouteType.Conveyor && c.Routes[o].Parent == 255 && c.Routes[o].From == j)
                return j * 256 + ((k - (int)c.Routes[o].Timer + 256) & 255);
        return j * 256;
    }

    /// <summary>The lead piece into what the link ends at (a Piece Rack, or a Press's bay): true when it went in.</summary>
    private static bool Deliver(ConstructWorld world, IWorldQuery query, Construct c, int route, in CRider r, ConstructPartDef d)
    {
        var to = c.Routes[route].To;
        if (to >= c.PartCount)
            return false;
        var td = c.DefOf(to);
        if (td.IsPieceRack)
            return world.RackPut(c, to, d);
        if (td.IsPress && ConstructRoutes.PortOf(td, RouteType.Conveyor, c.Routes[route].ToPort) is { } port)
        {
            var bay = port.Name.Length > 9 && int.TryParse(port.Name.AsSpan(9), out var nb) ? nb - 1 : 0; // ("Piece in", "Piece in 2"...)
            return ConstructPress.TakeFromConveyor(world, query, c, to, bay, d, r.Health / 255f, r.Paint);
        }
        return false;
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
        {
            var k = c.Conveyed[c.ConveyedCount - 1].Route;
            var live = !c.Dirty && c.PartCount > 0 && k < c.RouteCount && c.Routes[k].Type == RouteType.Conveyor ? k : -1; // (its link still whole: where it is now)
            Drop(world, c, c.ConveyedCount - 1, ConstructParts.Get(c.Conveyed[c.ConveyedCount - 1].Def), live);
        }
    }

    // ---- When links are renumbered ---------------------------------------------------------------------------------

    /// <summary>After the links were renumbered (<paramref name="map"/>: old index to new, -1 gone): riders follow; a gone link's riders wait to drop where they were.</summary>
    public static void Remap(Construct c, ReadOnlySpan<int> map, int oldCount)
    {
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            r.Route = r.Route < oldCount && map[r.Route] >= 0 ? (byte)map[r.Route] : (byte)255;
            var via = r.ViaPlus1 - 1;
            r.ViaPlus1 = via >= 0 && via < oldCount && map[via] >= 0 ? (byte)(map[via] + 1) : (byte)0;
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
                var via = r.ViaPlus1 - 1;
                r.ViaPlus1 = via >= 0 && via < map.Length && map[via] >= 0 ? (byte)(map[via] + 1) : (byte)0;
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
            r.ViaPlus1 = r.ViaPlus1 != 0 && baseRoute + r.ViaPlus1 <= Construct.MaxRoutes ? (byte)(baseRoute + r.ViaPlus1) : (byte)0;
            a.ReserveConveyed(a.ConveyedCount + 1);
            a.Conveyed[a.ConveyedCount++] = r;
        }
        b.ConveyedCount = 0;
    }

    // ---- The wire (protocol 244: CBodyKind.Riders) -------------------------------------------------------------------

    /// <summary>One body's riders, the whole list: u16 body, u16 n, each u8 link, f32 front (m: before a junction's output starts, below 0), u8 health, u32 paint, u8 the link it came through plus one, then the piece's key (protocol 245).</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Riders).U16(c.Id).U16((ushort)c.ConveyedCount);
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            w.U8(r.Route).F32(r.At).U8(r.Health).U32(r.Paint).U8(r.ViaPlus1);
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
        var had = c?.ConveyedCount ?? 0;
        if (c != null)
            c.ConveyedCount = 0;
        for (var i = 0; i < n; i++)
        {
            var route = r.U8();
            var at = r.F32();
            var hp = r.U8();
            var paint = r.U32();
            var via = r.U8();
            if (!float.IsFinite(at))
                throw new MalformedPacketException("a rider nowhere");
            if (r.U16() != Extrusion.WireMark)
                throw new MalformedPacketException("a rider that isn't a piece");
            var def = Extrusion.Read(ref r);
            if (c == null)
                continue;
            // The same piece this copy has moved on a little past the server's word (which is a lag old): kept where it is, no step back.
            if (i < had && c.Conveyed[i].Route == route && c.Conveyed[i].Def == def && c.Conveyed[i].At > at && c.Conveyed[i].At - at < 0.5f)
                at = c.Conveyed[i].At;
            c.Conveyed[c.ConveyedCount++] = new CRider { Def = def, Route = route, At = at, Health = hp, Paint = paint, ViaPlus1 = via };
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
