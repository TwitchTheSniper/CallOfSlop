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
    // Smart logistics (ConstructLogistics.cs): where it is going, on either peer as the server last said.
    public ushort Dest; // the destination's store handle (a tray or a Piece Rack; 0: none yet)
    public ushort Tag; // the order it serves there (a terminal's CraftOrder.Id; 0: storage)
    public byte Flags; // LogiFlags: storage, overflow, stuck, fetched; bits 4-7 the reason (LogiReason)
    public byte Hops; // server: junctions crossed (the loop guard)
    public byte Wait; // server: conveyor steps its Router has held it
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
    public ushort[] ToolRacks = []; // its Tool Racks (ToolRacks.cs), for drawing the tools on their hooks
    public int ToolRackCount;

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
    /// The stack of a Piece Rack the player looks at (<paramref name="eye"/> along <paramref name="look"/>), else the last
    /// column's highest stack of the kind put in last. False when it is empty. Server and client alike (the same columns).
    /// </summary>
    public bool RackPick(Construct c, int part, CStore st, Vector3 eye, Vector3 look, out int slab, out int lane)
    {
        slab = lane = -1;
        if (st.KindN == 0)
            return false;
        var rd = ConstructPads.RackDef(c, part);
        ConstructRackPack.Ensure(rd, st);
        if (look != Vector3.Zero && ConstructConveyor.RackAimed(c, part, st, eye, look, out slab, out lane))
            return true;
        return ConstructRackPack.LastOf(st, st.KindDef![st.KindN - 1], out slab, out lane);
    }

    /// <summary>Server: the top piece off a rack's stack, out of its store (the caller gives it somewhere). Null when there is none.</summary>
    public ConstructPartDef? RackTakeAt(Construct c, int part, int slab, int lane)
    {
        if (StoreOf(c, part) is not { } st)
            return null;
        var def = ConstructRackPack.Take(st, slab, lane);
        if (ConstructParts.Get(def) is not { } d)
            return null;
        st.AddKind(d.Id, -1);
        st.SlabSum = Math.Min(st.SlabSum, st.ItemCount);
        LoadChanged = true;
        Changed(st);
        c.Wake();
        return d;
    }

    /// <summary>
    /// Where piece <paramref name="k"/> off a rack goes loose: beside the rack on the side of <paramref name="eye"/>, just over
    /// the rack's top, a row along the rack and then rows further out (a drop of many lies on the floor, not in the rack). World.
    /// </summary>
    public static void RackBeside(Construct c, int part, ConstructPartDef d, Vector3 eye, int k, out Vector3 at, out Quaternion turn)
    {
        ref var p = ref c.Parts[part];
        var rd = ConstructPads.RackDef(c, part);
        c.WorldFrame(p.Seg, out var q, out var t);
        var r = Rot24.Quat(p.Rot);
        turn = q * r;
        var e = Vector3.Transform(eye - (t + Vector3.Transform(p.Pos, q)), Quaternion.Conjugate(turn)); // (the rack's own frame)
        var mn = rd.BoundsMin;
        var mx = rd.BoundsMax;
        var s = d.BoundsMax - d.BoundsMin;
        var side = e.X < (mn.X + mx.X) * 0.5f ? -1f : 1f;
        var row = s.Z > 1 ? 1 : 8; // (long pieces side by side outwards; short ones in rows of eight along the rack)
        var across = (k / row) * (s.X + 0.1f) + s.X * 0.5f + 0.15f;
        var along = Math.Clamp(e.Z, mn.Z + s.Z * 0.5f, mx.Z - s.Z * 0.5f) + ((k % row) - row / 2) * (s.Z + 0.1f);
        var centre = new Vector3(side > 0 ? mx.X + across : mn.X - across, mx.Y + s.Y * 0.5f + 0.05f, along);
        var local = centre - (d.BoundsMin + d.BoundsMax) * 0.5f; // (its origin, so its middle is there)
        at = t + Vector3.Transform(p.Pos + Vector3.Transform(local, r), q);
    }

    /// <summary>Where a stack's top piece is now (world): its centre and turn (the physics gun pulls it out from there).</summary>
    public static void RackPieceAt(Construct c, int part, CStore st, int slab, int lane, out Vector3 at, out Quaternion turn)
    {
        ref var p = ref c.Parts[part];
        var rd = ConstructPads.RackDef(c, part);
        var d = ConstructParts.Def(st.Slabs![slab].Def);
        int n = st.SlabLanes![slab * ConstructRackPack.MaxLanes + lane];
        var o = ConstructRackPack.Origin(rd, st.Slabs[slab], d, lane, Math.Max(0, n - 1)) + (d.BoundsMin + d.BoundsMax) * 0.5f;
        c.WorldFrame(p.Seg, out var q, out var t);
        var r = Rot24.Quat(p.Rot);
        at = t + Vector3.Transform(p.Pos + Vector3.Transform(o, r), q);
        turn = q * r;
    }

    /// <summary>Server: every piece of a kind off a rack, loose bodies stacked over it, at most <paramref name="most"/> (a world of bodies costs). How many.</summary>
    public int RackDropKind(Construct c, int part, ushort def, int most, Vector3 eye)
    {
        if (StoreOf(c, part) is not { } st || ConstructParts.Get(def) is not { } d)
            return 0;
        ConstructRackPack.Ensure(ConstructPads.RackDef(c, part), st);
        var n = 0;
        while (n < most && ConstructRackPack.LastOf(st, def, out var slab, out var lane))
        {
            RackBeside(c, part, d, eye, n, out var at, out var turn);
            if (ConstructEdits.DropLoose(this, d, at, turn) is not { } body)
                break;
            body.Owner = c.Owner;
            ConstructRackPack.Take(st, slab, lane);
            st.AddKind(def, -1);
            n++;
        }
        st.SlabSum = Math.Min(st.SlabSum, st.ItemCount);
        if (n > 0)
        {
            LoadChanged = true;
            Changed(st);
            c.Wake();
        }
        return n;
    }

    /// <summary>A piece into a Piece Rack, if it takes it: in the place packing gives it. Server.</summary>
    public bool RackPut(Construct c, int part, ConstructPartDef d, bool ordered = false)
    {
        part = ConstructPads.Owner(c, part); // (a Pad Tile in a bigger pad: into the pad, its owner tile's store)
        if (StoreOf(c, part) is not { } st || ConstructConveyor.RackCheck(c, part, st, d, ordered) != 0)
            return false;
        ConstructRackPack.Place(ConstructPads.RackDef(c, part), st, d, false, out _, out _);
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
    public const float Speed = 1.0f; // metres a second a piece rides (the user, 2026-11-14: twice the first 0.5, "parts just take forever to get places"; both peers step riders by it: protocol 318)
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

    /// <summary>Why a conveyor can't be set <paramref name="w"/> cm wide, or null (its height is unused: user, 2026-11-11).</summary>
    public static string? SizeRefusal(Construct c, int route, int w, int h)
    {
        ConstructRoutes.ConveyorMax(c, route, out var mw, out _); // (no wider than its turns leave room for: ConstructRoutesEnds.cs)
        if (w < MinSizeCm)
            return $"a conveyor is at least {MinSizeCm} cm wide";
        if (w > mw)
            return $"at most {mw / 100f:0.00} m wide here (its ends and its turns)";
        return null;
    }

    /// <summary>
    /// The width a piece needs (m): a conveyor has one size, its width, and is clear above it (user, 2026-11-11), so a piece needs
    /// its section's larger side (it may ride on its narrow side; a bed this wide takes it either way).
    /// </summary>
    public static float Needs(float across, float up) => MathF.Max(across, up);

    /// <summary>Does a piece this size fit conveyor <paramref name="r"/>?</summary>
    public static bool Fits(float across, float up, in CRoute r) => Needs(across, up) <= r.ConvW / 100f + 0.005f;

    // ---- The rack ------------------------------------------------------------------------------------------------

    /// <summary>Whether a Piece Rack takes <paramref name="d"/>: 0, or 1 not a rack, 2 not a piece, 3 too big for it, 4 full (no text: the step asks every time the lead piece waits; <see cref="RackRefusal"/> says it).</summary>
    public static int RackCheck(Construct c, int part, CStore st, ConstructPartDef d, bool ordered = false)
    {
        var rd = ConstructPads.RackDef(c, part);
        if (!rd.IsPieceRack || ConstructPads.IsMember(c, part))
            return 1; // (a Pad Tile in a bigger pad holds nothing itself: its pad is its owner tile's, ConstructPads.Owner)
        if (d.Role == PartRole.Storage && !rd.IsBuildStation && !rd.IsLayDownPad && !(ordered && (rd.IsCraftTerminal || rd.IsCargoHold))) // (a Heavy Container's hold takes a new empty store only from a hauler's load, ordered: GameStationHaulers.HaulParts) // (lifters phase 7: a Lay-down Pad takes a store part as the hold does: a def, new and empty; a loaded loose one is refused at its take-in, ConstructLifters.TakeInWhy) // (a terminal's tray only for its order's rider, a def, new and empty (<paramref name="ordered"/>, logistics fix-ups 2): an arm, a hand, a chute or a fetch is refused, so a loaded store never becomes a count)
            return 2; // (a piece, or anything a press stamps that fits: not storage, whose contents would be lost. A Building Station's hold takes any store part a blueprint names, a machine too, since a hold is a def count and what reaches it is new and empty: an order's rider, a hand, a stock; a loose loaded one in the bay is left alone, ConstructStation.TakeDropped: verification 2026-11-13c)
        if (rd.StationNoHold)
            return 3; // (a rover-dock Core has no hold: every piece goes onto a pad beside its bay, ConstructStationRovers.cs)
        ConstructRackPack.Ensure(rd, st);
        if (st.KindIndex(d.Id) < 0 && st.KindN >= CStore.MaxKinds || st.ItemCount >= rd.RackCount)
            return 4;
        return ConstructRackPack.Place(rd, st, d, true, out _, out _);
    }

    /// <summary>Why a rack refuses a piece, for a look line ("rack full", "piece too long for this rack"), or null.</summary>
    public static string? RackRefusal(Construct c, int part, CStore st, ConstructPartDef d)
    {
        switch (RackCheck(c, part, st, d))
        {
            case 0:
                return null;
            case 2:
                return "a rack takes pieces, not stores";
            case 3:
            {
                ConstructRackPack.Hold(ConstructPads.RackDef(c, part), out var lo, out var hi);
                return d.BoundsMax.Z - d.BoundsMin.Z > hi.Z - lo.Z ? "piece too long for this rack" : "piece too big across for this rack";
            }
            case 4:
                return "rack full";
            default:
                return "that isn't a rack";
        }
    }

    /// <summary>"Piece Rack: 62% full, 131 pieces, 4 kinds: 120 x L Bracket, ..." and F. Either side, from the store as heard.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool near, ConstructPartDef? aimed = null)
    {
        ConstructRackPack.Ensure(d, st);
        var sb = new System.Text.StringBuilder(d.Name).Append(": ");
        if (st.ItemCount == 0)
            sb.Append("empty");
        else
            sb.Append(FillText(d, st));
        const int listed = 3;
        for (var k = 0; k < st.KindN && k < listed; k++)
            if (ConstructParts.Get(st.KindDef![k]) is { } kd)
                sb.Append(k == 0 ? ": " : ", ").Append(st.KindCount![k]).Append(" x ").Append(kd.Name);
        if (st.KindN > listed)
            sb.Append($" and {st.KindN - listed} more kinds");
        if (st.KindN > 0 && near)
        {
            var name = aimed?.Name ?? "piece";
            sb.Append($" - {SandboxKeys.Use}: take the {name} - Shift+{SandboxKeys.Use}: drop up to {DropMost} like it loose");
        }
        return sb.ToString();
    }

    /// <summary>"62% full, 131 pieces, 4 kinds": what the look line and a console show of a rack.</summary>
    public static string FillText(ConstructPartDef d, CStore st)
    {
        ConstructRackPack.Ensure(d, st);
        var pct = (int)MathF.Round(ConstructRackPack.Fill(d, st) * 100);
        return $"{pct}% full, {st.ItemCount} piece{(st.ItemCount == 1 ? "" : "s")}, {st.KindN} kind{(st.KindN == 1 ? "" : "s")}" + (st.Reserved > 0 ? $" ({st.Reserved} reserved)" : "");
    }

    public const int DropMost = 40; // the most pieces one held F drops loose (each is a body)
    public const float RackDrawNear = 30f; // nearer than this a rack draws every piece; further, one box a column

    /// <summary>With the caches: a body's Piece Racks (they are special parts: they have stores).</summary>
    public static void BuildRacks(Construct c)
    {
        c.PieceRackCount = 0;
        c.ToolRackCount = 0;
        c.GantryCount = 0;
        c.PodCount = 0;
        foreach (var i in c.Specials)
        {
            if (c.Parts[i].D.ShellPod != null)
            {
                if (c.Pods.Length == c.PodCount)
                    Array.Resize(ref c.Pods, Math.Max(2, c.PodCount * 2));
                c.Pods[c.PodCount++] = (ushort)i; // (a shell pod: its frame is drawn, ShellPodRender.cs, and GameShellPod.cs ticks it)
            }
            if (c.Parts[i].D.Gantry != null)
            {
                if (c.Gantries.Length == c.GantryCount)
                    Array.Resize(ref c.Gantries, Math.Max(2, c.GantryCount * 2));
                c.Gantries[c.GantryCount++] = (ushort)i; // (a Shell Gantry: its arms are drawn, GantryRender.cs)
            }
            if (c.Parts[i].D.IsToolRack)
            {
                if (c.ToolRacks.Length == c.ToolRackCount)
                    Array.Resize(ref c.ToolRacks, Math.Max(4, c.ToolRackCount * 2));
                c.ToolRacks[c.ToolRackCount++] = (ushort)i;
            }
            if (!c.Parts[i].D.IsPieceRack || ConstructPads.IsMember(c, i))
                continue; // (a Pad Tile in a bigger pad: its owner tile stands for the pad, ConstructPads.cs)
            if (c.PieceRacks.Length == c.PieceRackCount)
                Array.Resize(ref c.PieceRacks, Math.Max(4, c.PieceRackCount * 2));
            c.PieceRacks[c.PieceRackCount++] = (ushort)i;
        }
        ConstructShellPod.FindHosts(c); // (the surfaces its flush pods' pockets open: drawn cut, Renderer.Cuts.cs)
    }

    /// <summary>A world ray into a rack part's own frame (unturned), where its columns are boxes.</summary>
    public static void IntoRack(Construct c, int part, Vector3 eye, Vector3 look, out Vector3 o, out Vector3 dir)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var inv = Quaternion.Conjugate(q * Rot24.Quat(p.Rot));
        o = Vector3.Transform(eye - (t + Vector3.Transform(p.Pos, q)), inv);
        dir = Vector3.Transform(look, inv);
    }

    /// <summary>The stack a ray from <paramref name="eye"/> along <paramref name="look"/> meets first within reach.</summary>
    public static bool RackAimed(Construct c, int part, CStore st, Vector3 eye, Vector3 look, out int slab, out int lane)
    {
        IntoRack(c, part, eye, look, out var o, out var dir);
        var near = 6f;
        return ConstructRackPack.Ray(ConstructPads.RackDef(c, part), st, o, dir, ref near, out slab, out lane);
    }

    // ---- Taking a piece off a conveyor (F, the physics gun) --------------------------------------------------------

    /// <summary>
    /// The riding piece a ray (world) meets first before <paramref name="best"/>, as the frame draws it (moved on from its
    /// last known front, RouteRender.DrawRiders); its index, or -1. A look only: nothing per rider runs otherwise.
    /// </summary>
    public static int RiderRay(Construct c, Vector3 origin, Vector3 dir, ref float best)
    {
        var elapsed = (float)Math.Clamp(Clock - c.ConveyedStamp, 0, 0.25);
        Span<float> tail = stackalloc float[Construct.MaxRoutes];
        Tails(c, tail);
        var found = -1;
        var prevRoute = -1;
        var prevBack = 0f;
        var routeLen = 0f;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            var k = r.Route;
            if (k >= c.RouteCount || c.Routes[k].Type != RouteType.Conveyor || ConstructParts.Get(r.Def) is not { } d)
                continue;
            if (k != prevRoute)
                routeLen = Length(c, k);
            Size(d, out _, out _, out var along);
            var limit = k != prevRoute ? routeLen - (tail[k] > 0 ? tail[k] + Gap : 0) : prevBack - Gap;
            var front = MathF.Max(r.At, MathF.Min(r.At + Speed * elapsed, limit));
            prevRoute = k;
            prevBack = front - along;
            Pose(c, k, front - along * 0.5f, d, out var o, out var rot, out var seg, r.ViaPlus1 - 1);
            c.WorldFrame(seg, out var q, out var t);
            var inv = Quaternion.Conjugate(q * rot);
            var lo = Vector3.Transform(origin - (t + Vector3.Transform(o, q)), inv);
            var ld = Vector3.Transform(dir, inv);
            if (Geometry.RayAabb(d.BoundsMin, d.BoundsMax, lo, ld, best) is { } hit && hit < best)
            {
                best = hit;
                found = i;
            }
        }
        return found;
    }

    /// <summary>Server: the rider on conveyor <paramref name="route"/> whose front is nearest <paramref name="atCm"/> (it moved on since the player aimed), within 1.5 m; or -1.</summary>
    public static int RiderNear(Construct c, int route, int atCm)
    {
        var best = -1;
        var gap = 150f;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            if (c.Conveyed[i].Route != route)
                continue;
            var g = MathF.Abs(c.Conveyed[i].At * 100 - atCm);
            if (g <= gap)
            {
                gap = g;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Server: rider <paramref name="i"/> off its conveyor: where it is in the world and its turn; the ones behind close up on their own.</summary>
    public static ConstructPartDef? TakeRider(ConstructWorld world, Construct c, int i, out Vector3 centre, out Quaternion rot, out byte health, out uint paint)
    {
        centre = default;
        rot = Quaternion.Identity;
        health = 255;
        paint = 0;
        if ((uint)i >= (uint)c.ConveyedCount)
            return null;
        var r = c.Conveyed[i];
        var d = ConstructParts.Get(r.Def);
        if (d != null && r.Route < c.RouteCount && c.Routes[r.Route].Type == RouteType.Conveyor)
            WorldPose(c, r.Route, r, d, out centre, out rot);
        else
            (centre, rot) = (r.LastPos, r.LastRot);
        (health, paint) = (r.Health, r.Paint);
        Remove(c, i);
        world.RidersChanged(c);
        c.Wake();
        return d;
    }

    /// <summary>
    /// Whether a conveyor's lead piece waits at its far end, and why: 0 it moves, 1 its end is gone, 2 to 5 the rack's
    /// <see cref="RackCheck"/> (2 not a piece, 3 too big, 4 full; 5 too long), 6 a junction's way on is full, 7 a machine isn't
    /// taking it, 8 held at a Router, 9 waiting at a Roller Tile run's end for a lifter. Either side, from what it heard; allocates nothing (a look line asks every frame).
    /// </summary>
    public static int Jam(ConstructWorld world, Construct c, int route)
    {
        if ((uint)route >= (uint)c.RouteCount || c.Routes[route].Type != RouteType.Conveyor)
            return 0;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            if (c.Conveyed[i].Route != route)
                continue;
            if (c.Conveyed[i].At < Length(c, route) - 0.02f || ConstructParts.Get(c.Conveyed[i].Def) is not { } d)
                return 0;
            var to = ConstructPads.Owner(c, c.Routes[route].To); // (a conveyor into any tile of a pad ends at its owner's store)
            if (to >= c.PartCount)
                return 1;
            var td = c.DefOf(to);
            if (td.IsPieceRack && world.StoreOf(c, to) is { } st)
            {
                var why = RackCheck(c, to, st, d);
                ConstructRackPack.Hold(ConstructPads.RackDef(c, to), out var lo, out var hi);
                return why == 3 && d.BoundsMax.Z - d.BoundsMin.Z > hi.Z - lo.Z ? 5 : why is >= 2 and <= 4 ? why : 0;
            }
            if (td.IsRouter && ((LogiFlags)c.Conveyed[i].Flags & LogiFlags.Stuck) != 0)
                return 8;
            if (td.IsRollerTile && RollerRuns.EndRunAt(c, to) == route)
                return 9; // (a run with no way on: its pieces wait at its end for a crane)
            return td.IsConveyorJunction ? 6 : 7;
        }
        return 0;
    }

    /// <summary>"jammed: rack full", for a <see cref="Jam"/> code (null for 0).</summary>
    public static string? JamText(Construct c, int route, int jam) => jam switch
    {
        1 => "jammed: its end is gone",
        2 => "jammed: a rack takes pieces, not stores",
        3 => "jammed: piece too big across for this rack",
        4 => "jammed: rack full",
        5 => "jammed: piece too long for this rack",
        6 => "jammed: the conveyor after the junction is full",
        7 => $"jammed: the {(c.Routes[route].To < c.PartCount ? c.DefOf(c.Routes[route].To).Name : "machine")} isn't taking it",
        9 => "waiting at the run's end for a crane or an arm",
        8 => "held at " + ConstructLogisticsBoard.RouterLabel(c, c.Routes[route].To) + ": " + StuckWhy(c, route), // (its name, else its number: phase 4c)
        _ => null,
    };

    private static string StuckWhy(Construct c, int route)
    {
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Route == route)
                return ConstructLogistics.ReasonText(ConstructLogistics.ReasonOf(c.Conveyed[i]));
        return "";
    }

    // ---- The link ------------------------------------------------------------------------------------------------

    /// <summary>The conveyor leaving part <paramref name="part"/>'s Piece out, or -1 (a bed machine's Core: its Out Table's, MachineBlocks.OutTable).</summary>
    public static int Out(Construct c, int part)
    {
        if (c.MachineAsmCount > 0 && c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && MachineBlocks.OutTable(c, asm) is var table and >= 0)
            part = table;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Parent == 255 && c.Routes[k].From == part)
                return k;
        return -1;
    }

    /// <summary>
    /// The pick pad a bed machine with no out conveyor hands to: a Lay-down Pad tile within a metre of its Out Table's Piece out (its
    /// pad's owner), or -1. Zero Day Works II's big lines end in their stations' pick pads, laid where the roller lay-bys were (Sean,
    /// 2026-11-26). Kept per body until its structure changes (a walk of its parts once a machine).
    /// </summary>
    public static int OutPad(Construct c, int part)
    {
        if (c.MachineAsmCount == 0 || MachineBlocks.AsmOf(c, part) is not (>= 0 and var a))
            return -1;
        // (kept on the machine's assembly, made again with it at each rebuild: a conveyor step asks it for every lead rider at such a
        // bed, PassesThrough; allocates nothing)
        ref var asm = ref c.MachineAsms[a];
        if (asm.OutPad == 0)
            asm.OutPad = FindOutPad(c, part) is var found and >= 0 ? found + 1 : -1;
        return asm.OutPad > 0 ? asm.OutPad - 1 : -1;
    }

    private static int FindOutPad(Construct c, int part)
    {
        if (!MachineBlocks.TryOf(c, part, out var asm) || MachineBlocks.OutTable(c, asm) is not (>= 0 and var table)
            || RollerRuns.PortByte(c.DefOf(table), "Piece out") is not (>= 0 and var pb))
            return -1;
        var at = ConstructRoutes.PortPoint(c, table, (byte)pb, RouteType.Conveyor);
        var seg = c.Parts[table].Seg;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Seg == seg && c.DefOf(i).IsLayDownPad && MathF.Abs(c.Parts[i].Pos.X - at.X) < 1.05f && MathF.Abs(c.Parts[i].Pos.Z - at.Z) < 1.05f
                && MathF.Abs(c.Parts[i].Pos.Y - c.Parts[table].Pos.Y) < 1.5f)
                return ConstructPads.Owner(c, i);
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
    public static void Pose(Construct c, int route, float s, ConstructPartDef d, out Vector3 origin, out Quaternion rot, out int seg, int via = -1, bool drawn = false)
        => Pose(c, route, s, d, out origin, out rot, out seg, out _, via, drawn);

    /// <summary>
    /// A straight vertical segment (an elevator, Sean 2026-11-23: drawn as a lift shaft, RouteRender.Shaft, not a roller bed). As steep as
    /// <see cref="RouteCurves.Tangent"/> takes a segment to be, so a shaft never has a vertical curve at either end; a slope is a roller bed.
    /// </summary>
    public static bool IsShaft(Vector3 way) => MathF.Abs(way.Y) > ShaftSteep;

    public const float ShaftSteep = 0.995f;

    /// <summary>
    /// The level way a shaft's riders face and its frame is drawn by (rest frame, y zero): the nearest non-shaft segment's level way, back
    /// along the link first (the way pieces came in), else on; else +z. <paramref name="i"/> is the shaft's segment (Point(i - 1) to Point(i)).
    /// </summary>
    public static Vector3 ShaftFacing(Construct c, int route, int i)
    {
        var count = c.Routes[route].Count;
        for (var j = i - 1; j >= 0; j--)
            if (LevelWay(ConstructRoutes.Point(c, route, j - 1, out _), ConstructRoutes.Point(c, route, j, out _), out var f))
                return f;
        for (var j = i + 1; j <= count; j++)
            if (LevelWay(ConstructRoutes.Point(c, route, j - 1, out _), ConstructRoutes.Point(c, route, j, out _), out var f))
                return f;
        return Vector3.UnitZ;
    }

    /// <summary>A segment's way flattened to level, when it is no shaft (and not too short to say).</summary>
    public static bool LevelWay(Vector3 a, Vector3 b, out Vector3 way)
    {
        var d = b - a;
        var len = d.Length();
        way = Vector3.UnitZ;
        if (len < 1e-3f || IsShaft(d / len))
            return false;
        var h = new Vector3(d.X, 0, d.Z);
        if (h.LengthSquared() < 1e-6f)
            return false;
        way = Vector3.Normalize(h);
        return true;
    }

    /// <summary>
    /// As <see cref="Pose(Construct, int, float, ConstructPartDef, out Vector3, out Quaternion, out int, int, bool)"/>; <paramref name="shaft"/>
    /// says the piece is in a straight vertical segment, where it rides level on a carriage (its underside on the line, facing
    /// <see cref="ShaftFacing"/>) as a lift carries it, not standing on end against a roller bed.
    /// </summary>
    public static void Pose(Construct c, int route, float s, ConstructPartDef d, out Vector3 origin, out Quaternion rot, out int seg, out bool shaft, int via = -1, bool drawn = false)
    {
        shaft = false;
        var a = ConstructRoutes.Point(c, route, -1, out seg);
        var b = a;
        var count = c.Routes[route].Count;
        var segLen = 0f;
        Vector3 z, mid;
        if (s < 0 && via >= 0 && LeadIn(c, route, via, out var inEnd, out var centre, out var outStart) is var lead and > 0)
        {
            if (-s > lead)
            {
                // Its middle is still on the conveyor it came off: as it was there.
                Pose(c, via, Length(c, via) + s + lead, d, out origin, out rot, out seg, out shaft, drawn: drawn);
                return;
            }
            // In the junction: along its lead-in, from the input's end through the junction's middle to here, turning from the
            // way it came to the way it goes as it crosses (a turntable's turn, not a snap at the middle).
            var first = Vector3.Distance(inEnd, centre);
            var u = lead + s; // (how far its middle is in)
            mid = u <= first ? Vector3.Lerp(inEnd, centre, first > 1e-4f ? u / first : 1) : Vector3.Lerp(centre, outStart, (u - first) / MathF.Max(1e-4f, lead - first));
            var zin = first > 1e-4f ? Vector3.Normalize(centre - inEnd) : Vector3.UnitZ;
            var zout = lead - first > 1e-4f ? Vector3.Normalize(outStart - centre) : zin;
            var blend = Vector3.Lerp(zin, zout, Math.Clamp(u / lead, 0, 1));
            z = blend.LengthSquared() > 1e-6f ? Vector3.Normalize(blend) : zout;
            seg = c.Parts[c.Routes[route].From].Seg;
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
            if (drawn && segLen > 1e-4f && count > 0) // (drawing only: the server's positions stay on the straight segments)
                OnCurve(c, route, i, a, b, z, segLen, s, ref mid, ref z);
            if (segLen > 1e-4f && IsShaft(z))
            {
                // In a lift shaft: level, facing the way the pieces came in (its timing and spacing stay the link's).
                shaft = true;
                z = ShaftFacing(c, route, i);
            }
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

    /// <summary>
    /// A piece inside a vertical curve's window (<see cref="RouteCurves"/>, drawn by RouteRender the same way): its middle and way follow the
    /// curve. Only where it is drawn changes: <paramref name="s"/>, its distance along segment <paramref name="i"/>, and so its timing and
    /// the link's length stay those of the straight segments (the user, 2026-11-17: the look only).
    /// </summary>
    private static void OnCurve(Construct c, int route, int i, Vector3 a, Vector3 b, Vector3 z, float segLen, float s, ref Vector3 mid, ref Vector3 way)
    {
        ref var r = ref c.Routes[route];
        if (s < 0 || s > segLen || ConstructRoutes.SegmentHealth(c, route, i) <= 0 || c.FlexSectionCount > 0 && ConstructRoutes.FlexSectionAt(c, route, i) >= 0)
            return;
        var w = r.ConvW / 100f;
        if (i < r.Count && s > segLen * (1f - RouteCurves.RoomShare) && ConstructRoutes.SegmentHealth(c, route, i + 1) > 0
            && !(c.FlexSectionCount > 0 && ConstructRoutes.FlexSectionAt(c, route, i + 1) >= 0))
        {
            var n = ConstructRoutes.Point(c, route, i + 1, out var sn);
            ConstructRoutes.Point(c, route, i, out var sb);
            var dn = n - b;
            var ln = dn.Length();
            if (sn == sb && ln > 1e-4f)
            {
                var zn = dn / ln;
                var t = RouteCurves.Tangent(RouteType.Conveyor, w, z, segLen, zn, ln);
                if (t > 0f && s > segLen - t)
                {
                    RouteCurves.At(b, z, zn, t, (s - (segLen - t)) / (2f * t), out mid, out way);
                    return;
                }
            }
        }
        if (i >= 1 && s < segLen * RouteCurves.RoomShare && ConstructRoutes.SegmentHealth(c, route, i - 1) > 0
            && !(c.FlexSectionCount > 0 && ConstructRoutes.FlexSectionAt(c, route, i - 1) >= 0))
        {
            var p = ConstructRoutes.Point(c, route, i - 2, out var sp);
            ConstructRoutes.Point(c, route, i - 1, out var sa);
            var dp = a - p;
            var lp = dp.Length();
            if (sp == sa && lp > 1e-4f)
            {
                var zp = dp / lp;
                var t = RouteCurves.Tangent(RouteType.Conveyor, w, zp, lp, z, segLen);
                if (t > 0f && s < t)
                    RouteCurves.At(a, zp, z, t, 0.5f + s / (2f * t), out mid, out way);
            }
        }
    }

    /// <summary>A rider's centre and turn in the world now (server: where it drops).</summary>
    internal static void WorldPose(Construct c, int route, in CRider r, ConstructPartDef d, out Vector3 centre, out Quaternion rot)
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
        if (!Fits(across, up, r))
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
        ConstructLogistics.AtSource(world, c, MachineBlocks.Owner(c, r.From), ref rider, piece); // (an order that asked this machine for it: its tray, when a Router is on the way)
        if (rider.Dest == 0)
            PastBed(world, c, route, MachineBlocks.Owner(c, r.From), ref rider, piece);
        Insert(c, rider);
        world.RemovePart(c, product);
        c.Wake();
        world.RidersChanged(c);
        return true;
    }

    /// <summary>
    /// Parts a Press or Die Lathe stamped onto the conveyor out of its Piece out, one behind another: true when all
    /// <paramref name="count"/> went; else nothing went and <paramref name="why"/> says why (the press waits with its pieces untouched).
    /// </summary>
    public static bool PutMade(ConstructWorld world, Construct c, int route, ConstructPartDef d, int count, out ExtrudeStall why, ushort dest = 0, ushort tag = 0)
    {
        why = ExtrudeStall.None;
        ref var r = ref c.Routes[route];
        if (r.FirstCut <= r.Count)
        {
            why = ExtrudeStall.ConveyorCut;
            return false;
        }
        Size(d, out var across, out var up, out var along);
        if (!Fits(across, up, r))
        {
            why = ExtrudeStall.ConveyorSmall;
            return false;
        }
        var need = along * count + Gap * (count - 1); // (room at the start: the last rider on it has gone their lengths and gaps)
        var last = -1;
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Route == route)
                last = i;
        if (last >= 0)
        {
            Size(ConstructParts.Def(c.Conveyed[last].Def), out _, out _, out var lastAlong);
            if (c.Conveyed[last].At - lastAlong - Gap < need)
                why = ExtrudeStall.ConveyorFull;
        }
        if (c.ConveyedCount + count > Construct.MaxConveyed || need > Length(c, route) + 0.01f)
            why = ExtrudeStall.ConveyorFull;
        if (why != ExtrudeStall.None)
            return false;
        for (var n = 0; n < count; n++)
        {
            var rider = new CRider { Def = d.Id, Route = (byte)route, At = along + (count - 1 - n) * (along + Gap), Health = 255 };
            WorldPose(c, route, rider, d, out rider.LastPos, out rider.LastRot);
            if (dest != 0)
                (rider.Dest, rider.Tag, rider.Flags) = (dest, tag, (byte)LogiFlags.Fetched); // (a terminal's fetch: it knows where)
            else
                ConstructLogistics.AtSource(world, c, MachineBlocks.Owner(c, r.From), ref rider, d);
            Insert(c, rider);
        }
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
        if (!Fits(across, up, r) || along > Length(c, o) + 0.01f)
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
    private static bool Pass(ConstructWorld world, Construct c, int k, int lead, ref bool changed)
    {
        int j = c.Routes[k].To;
        if (c.DefOf(j).IsRouter && ConstructRecycler.Powered(world, c, j) && ConstructLogistics.Map(c) is var m && m.RouterSlot(j) is var s and >= 0)
            return ConstructLogistics.Pass(world, c, m, s, k, lead, ref changed); // (a powered Router: the way to the piece's destination; unpowered, it deals in turn below)
        if (c.DefOf(j).IsTurntable && c.Conveyed[lead].Dest != 0 && ConstructLogistics.Map(c) is var tm && tm.RouterSlot(j) is var ts and >= 0
            && ConstructLogistics.TurntableWay(c, tm, ts, k, c.Conveyed[lead]) is var way and not -1)
            return way >= 0 && PassTo(c, k, lead, way); // (a Turntable Tile: the way to the piece's destination, waiting while it is full; none, it deals in turn below)
        var d = ConstructParts.Def(c.Conveyed[lead].Def);
        var jd = c.DefOf(j);
        var rollers = jd.IsRollerTile || jd.IsTurntable; // (a run's tile or a turntable: which way on, RollerRuns.Passes)
        Span<byte> outs = stackalloc byte[8];
        var n = 0;
        for (var o = 0; o < c.RouteCount && n < outs.Length; o++)
            if (c.Routes[o].Type == RouteType.Conveyor && c.Routes[o].Parent == 255 && c.Routes[o].From == j && (!rollers || RollerRuns.Passes(c, j, k, o)))
                outs[n++] = (byte)o;
        if (n < outs.Length && jd.IsRollerTile && !RollerRuns.IsRunPort(jd, c.Routes[k].ToPort) && RollerRuns.RunOf(c, j) is var run and >= 0)
            outs[n++] = (byte)run; // (a link into any tile's Piece in: onto its run, at the run's start)
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
            if (r.Hops < 255)
                r.Hops++; // (a Router further on knows a piece a Splitter keeps sending round)
            Insert(c, r);
            c.Routes[k].Cursor = (byte)((pick + 1) % n);
            for (var u = 0; u < n; u++)
                c.Routes[outs[u]].Timer = k + 1; // (a join: the input served last waits behind the others)
            return true;
        }
        return false;
    }

    /// <summary>A Router's choice: the lead piece of conveyor <paramref name="k"/> onto its output <paramref name="o"/> if that takes it now. True when it went.</summary>
    public static bool PassTo(Construct c, int k, int lead, int o)
    {
        var d = ConstructParts.Def(c.Conveyed[lead].Def);
        var lead0 = LeadIn(c, o, k, out _, out _, out _);
        if (!Takes(c, o, d, -lead0))
            return false;
        var r = c.Conveyed[lead];
        Remove(c, lead);
        r.Route = (byte)o;
        r.At = -lead0;
        r.ViaPlus1 = (byte)(k + 1);
        r.Hops = (byte)Math.Min(255, r.Hops + 1);
        r.Wait = 0;
        r.Flags &= unchecked((byte)~LogiFlags.Stuck);
        Insert(c, r);
        for (var u = 0; u < c.RouteCount; u++)
            if (c.Routes[u].Type == RouteType.Conveyor && c.Routes[u].Parent == 255 && c.Routes[u].From == c.Routes[k].To)
                c.Routes[u].Timer = k + 1; // (as a join's: the input served last waits behind the others)
        return true;
    }

    // ---- Each step (server) ----------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
        if (++world.ConveyorTicks < Every)
            return;
        world.ConveyorTicks = 0;
        StepAll(world, query);
    }

    private static void StepAll(ConstructWorld world, IWorldQuery query)
    {
        const float dt = Every / (float)World.TickRate;
        var now = Clock;
        ConstructLogistics.NewStep();
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.RollersStale && !c.Dirty)
                RollerRuns.Sync(world, c); // (Roller Tile runs after a rebuild: each run's conveyor as its tiles are)
            if (c.Logi is { OffAny: > 0 })
                ConstructLogistics.Piles(world, c); // (a pile set off beside a Router: the pieces taken away leave it, the last ends its Alarm)
            if (c.ConveyedCount == 0)
            {
                if (c.Logi is { AnyOut: true } && !c.Dirty)
                    ConstructLogistics.Refresh(c, server: true); // (the last piece taken off by hand: its Router's Alarm and Moving go to 0)
                continue;
            }
            if (c.Dirty)
                c.Rebuild();
            if (StepBody(world, query, c, dt, server: true))
            {
                world.RidersChanged(c);
                ConstructLogistics.Refresh(c, server: true);
            }
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
            var hold = lengths[k] - BedHold(world, c, k, along, first, r); // (a blank for a bed still busy waits whole on the run, not on the bed; only the lead goes onto it)
            var limit = MathF.Min(first ? end : prevBack - Gap, hold);
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
        // Pieces that rolled across a bed onto its machine's out run (PassOn): put on after the loop, which reads the riders by index.
        for (var t = 0; t < _throughN; t++)
        {
            var tr = _through[t];
            if (ConstructParts.Def(tr.Def) is { } td)
                WorldPose(c, tr.Route, tr, td, out tr.LastPos, out tr.LastRot);
            Insert(c, tr);
            changed = true;
        }
        _throughN = 0;
        // Leads at junctions go on, the one that waited longest first (a join takes its inputs in turn: Timer is the last served, plus one).
        for (var a = 0; a < nWaiting; a++)
            for (var b = a + 1; b < nWaiting; b++)
                if (Turn(c, waiting[b]) < Turn(c, waiting[a]))
                    (waiting[a], waiting[b]) = (waiting[b], waiting[a]);
        for (var w = 0; w < nWaiting; w++)
            for (var lead = 0; lead < c.ConveyedCount; lead++)
                if (c.Conveyed[lead].Route == waiting[w])
                {
                    if (Pass(world, c, waiting[w], lead, ref changed))
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

    /// <summary>
    /// A link onto a bed Core's Bed in (a Roller Tile run butted on a Panel Former's or a Laser Cutter's bed, RollerRuns.Butted): how far short
    /// of its end a piece stops, so it waits whole on the run at the bed's edge: the lead while a blank already lies on that bed, any other
    /// always (one blank at a time rides onto the bed). 0 otherwise. Either peer (OnBed is on the wire); allocates nothing.
    /// </summary>
    private static float BedHold(ConstructWorld world, Construct c, int k, float along, bool lead, in CRider rider)
    {
        ref readonly var r = ref c.Routes[k];
        if (r.To >= c.PartCount || r.Count == 0)
            return 0;
        var td = c.DefOf(r.To);
        if (!td.IsPanelFormer || ConstructRoutes.PortOf(td, RouteType.Conveyor, r.ToPort) is not { OnBed: true })
            return 0;
        if (lead && world.StoreOf(c, r.To) is not { OnBed: true } && (!PassesBed(c, r.To, rider) || ConstructParts.Def(rider.Def) is { } rd && PassesThrough(c, r.To, rd)))
            return 0; // (a piece made for another's order goes onto the bed only to roll on across it to the machine's out run, PassOn; else it waits whole at its edge for the crane, PastBed)
        var leg = Vector3.Distance(ConstructRoutes.Point(c, k, r.Count - 1, out _), ConstructRoutes.Point(c, k, r.Count, out _));
        return leg + along * 0.5f;
    }

    /// <summary>
    /// Server, a piece made onto a run that feeds a bed (a Roller Tile run butted on a Panel Former's or a Laser Cutter's bed, RollerRuns.Butted):
    /// when no order of the bed's machine takes it and a Crafting Terminal's or a station's order asked its maker for its kind, it rides for
    /// that order (its tray and the order's id), so it waits whole at the bed's edge and the crane over the run takes it past the bed
    /// (<see cref="BedHold"/>, RollerRuns.BedRunAt; Sean, 2026-11-23: a deck's plates bypass the forming station). Allocates nothing.
    /// </summary>
    private static void PastBed(ConstructWorld world, Construct c, int route, int maker, ref CRider rider, ConstructPartDef piece)
    {
        ref readonly var r = ref c.Routes[route];
        if (r.To >= c.PartCount || !c.DefOf(r.To).IsPanelFormer || ConstructRoutes.PortOf(c.DefOf(r.To), RouteType.Conveyor, r.ToPort) is not { OnBed: true }
            || !world.CraftOpen.Contains(c.Id) || ConstructPanelFormer.Takes(c, r.To, piece)
            || r.Count != 2)
            return;
        var crane = ConstructLifters.PicksRunEnd(world, c.Id, c.RouteNodes[r.Start + 1].Host);
        if (!crane && !PassesThrough(c, r.To, piece))
            return; // (no crane picks at the run's end and no out run to roll on to: it goes on onto the bed as before, never held there for ever)
        if (AskedOf(c, maker, piece, out var id) is not (>= 0 and var t))
            return;
        if (!crane && !ThroughReaches(world, c, r.To, piece, t, 0))
            return; // (rolling on would leave it where nothing takes it to the order: onto the bed as before)
        (rider.Dest, rider.Tag, rider.Flags) = (c.Parts[t].Store, id, 0);
    }

    /// <summary>
    /// Server: the open order (a Crafting Terminal's or a building station's) that asked machine <paramref name="maker"/> for a piece of
    /// kind <paramref name="piece"/>: the part that holds it (its store is a rider's Dest, <paramref name="id"/> its Tag), or -1. The first
    /// in part order, as <see cref="PastBed"/> has always taken it. Allocates nothing.
    /// </summary>
    public static int AskedOf(Construct c, int maker, ConstructPartDef piece, out ushort id)
    {
        id = 0;
        for (var t = 0; t < c.PartCount; t++)
        {
            if (c.Parts[t].Craft is not { Count: > 0 } q)
                continue;
            for (var k = 0; k < q.Count; k++)
            {
                ref readonly var o = ref q.Items[k];
                if (o.Asked > 0 && o.Where == maker && !o.Done && !o.Cancelled && ConstructCrafting.KindOf(ref q.Items[k]) == piece.Id)
                {
                    id = o.Id;
                    return t;
                }
            }
        }
        return -1;
    }

    /// <summary>Does a piece on a run into bed Core <paramref name="to"/> ride past the bed (made for another's order: <see cref="PastBed"/>)? Either peer (a rider's Dest is on the wire).</summary>
    public static bool PassesBed(Construct c, int to, in CRider r) => r.Dest != 0 && to < c.PartCount && r.Dest != c.Parts[ConstructPads.Owner(c, to)].Store; // (a pad's tile: its owner's store)

    /// <summary>
    /// Can a piece of kind <paramref name="d"/> that rides past bed Core <paramref name="to"/>'s machine (<see cref="PassesBed"/>) roll on
    /// across its bed onto the machine's out run (its Out Tables' run, the conveyor its made pieces go on), Sean 2026-11-23: "parts rolling
    /// through from the extruder past the panel former, past the laser cutter, if the steps aren't needed, all the way to the building
    /// station"? Only the link's size: either peer, allocates nothing.
    /// </summary>
    public static bool PassesThrough(Construct c, int to, ConstructPartDef d)
    {
        if (to >= c.PartCount || !c.DefOf(to).IsPanelFormer)
            return false;
        if (Out(c, to) is not (>= 0 and var o))
            return OutPad(c, to) is >= 0 and var pad && ConstructRackPack.Fit(ConstructPads.RackDef(c, pad), d, out _, out _, out _); // (no out run: onto the pick pad its Out Table butts, if that pad could ever hold it: Deliver)
        Size(d, out var across, out var up, out var along);
        return Fits(across, up, c.Routes[o]) && c.Routes[o].FirstCut > c.Routes[o].Count && along <= Length(c, o) + 0.01f;
    }

    /// <summary>
    /// Server: does rolling a piece of kind <paramref name="d"/> on across bed Core <paramref name="to"/>'s machine bring it to where order
    /// holder <paramref name="holder"/> gets it: its own store, a pick pad of it (a station's own), across another bed machine on, a Router
    /// (it sends the piece on by the Routers' rule), or a run's end a crane picks at? Follows the out runs a few machines deep; allocates nothing.
    /// </summary>
    private static bool ThroughReaches(ConstructWorld world, Construct c, int to, ConstructPartDef d, int holder, int depth)
    {
        if (depth <= 4 && to < c.PartCount && c.DefOf(to).IsPanelFormer && Out(c, to) < 0 && OutPad(c, to) is >= 0 and var pad)
            return PassesThrough(c, to, d) && (pad == holder || c.DefOf(holder).IsBuildStation && ConstructStation.IsOwnPad(c, holder, pad)); // (its Out Table onto a station's own pick pad that could hold it)
        if (depth > 4 || !PassesThrough(c, to, d) || Out(c, to) is not (>= 0 and var o))
            return false;
        ref readonly var r = ref c.Routes[o];
        if (r.To < c.PartCount)
        {
            var end = ConstructPads.Owner(c, r.To);
            if (end == holder || c.DefOf(holder).IsBuildStation && ConstructStation.IsOwnPad(c, holder, end))
                return true;
            if (c.DefOf(r.To).IsPanelFormer && ConstructRoutes.PortOf(c.DefOf(r.To), RouteType.Conveyor, r.ToPort) is { OnBed: true })
                return ThroughReaches(world, c, r.To, d, holder, depth + 1);
            if (c.DefOf(r.To).IsRouter)
                return ConstructLogistics.Reaches(c, r.To, holder); // (a Router with a way to the holder sends it on: to the order's holder by a way that takes it, else to storage, never lost; a piece no pad can hold was shorn into offcuts here once pads stopped taking its address, 2026-11-24)
        }
        return r.Count == 2 && RollerRuns.IsRun(c, o) && ConstructLifters.PicksRunEnd(world, c.Id, c.RouteNodes[r.Start + 1].Host);
    }

    private static readonly CRider[] _through = new CRider[4];
    private static int _throughN;

    /// <summary>
    /// Server, in the step: a piece at the end of a run onto a bed it only passes (<see cref="PassesThrough"/>) goes on at the start of the
    /// machine's out run when there is room there, keeping its order, health and paint; put on after the step's loop. True when it went.
    /// </summary>
    private static bool PassOn(Construct c, int to, in CRider r, ConstructPartDef d)
    {
        if (_throughN >= _through.Length || c.ConveyedCount + _throughN >= Construct.MaxConveyed || Out(c, to) is not (>= 0 and var o))
            return false;
        Size(d, out _, out _, out var along);
        if (LastOn(c, o) is >= 0 and var last && ConstructParts.Def(c.Conveyed[last].Def) is { } ld)
        {
            Size(ld, out _, out _, out var lastAlong);
            if (c.Conveyed[last].At - lastAlong - Gap < along)
                return false; // (the out run is full at its start: it waits on the bed)
        }
        for (var t = 0; t < _throughN; t++)
            if (_through[t].Route == o)
                return false;
        var n = r;
        (n.Route, n.At, n.ViaPlus1, n.Wait) = ((byte)o, along, 0, 0);
        _through[_throughN++] = n;
        return true;
    }

    /// <summary>The lead piece into what the link ends at (a Piece Rack, or a Press's bay): true when it went in.</summary>
    private static bool Deliver(ConstructWorld world, IWorldQuery query, Construct c, int route, in CRider r, ConstructPartDef d)
    {
        var to = ConstructPads.Owner(c, c.Routes[route].To); // (a conveyor into any tile of a pad puts into its owner's store)
        if (to >= c.PartCount)
            return false;
        var td = c.DefOf(to);
        if (td.IsPieceRack)
        {
            var ordered = r.Tag != 0 && ((LogiFlags)r.Flags & LogiFlags.Storage) == 0;
            if (!(td.IsBuildStation && ConstructStation.ParkEarly(world, c, to, d)) // (a batched station's early part waits in a holding bay: it counts as delivered)
                && !world.RackPut(c, to, d, ordered) && !(td.IsBuildStation && (ConstructStation.MakeRoomFor(world, c, to, d) && world.RackPut(c, to, d, ordered) || ConstructStation.ParkEarly(world, c, to, d, full: true))))
                return false; // (a station's full hold makes room for a part the build can fit now: ConstructStation.MakeRoomFor)
            if (td.IsCraftTerminal)
                ConstructCrafting.Arrived(world, c, to, d); // (it counts for the oldest open order of its kind)
            else if (world.StockTargetN > 0 || r.Tag != 0)
                ConstructLogisticsPlan.Arrived(world, c, to, r.Tag, d); // (a stock target's refill: it counts for its link)
            if (td.IsLayDownPad)
                ConstructLifters.PadArrived(world, c, to, ((LogiFlags)r.Flags & LogiFlags.Storage) == 0 ? r.Tag : (ushort)0, d); // (a station's large part on its pick pad: lifters phase 5)
            return true;
        }
        if (td.IsPanelFormer)
        {
            if (PassesBed(c, c.Routes[route].To, r) && PassesThrough(c, to, d))
            {
                if (Out(c, to) < 0 && OutPad(c, to) is >= 0 and var pad)
                {
                    if (ConstructLogistics.PartOfStore(c, r.Dest) is var holder && holder != pad
                        && !(holder >= 0 && c.DefOf(holder).IsBuildStation && ConstructStation.IsOwnPad(c, holder, pad)))
                        return false; // (another holder's piece: it waits here, as with no way on, never left on a pad nothing takes it from)
                    // (no out run: it rolls across the bed onto the pick pad its Out Table butts, Zero Day Works II's big lines, 2026-11-26)
                    var ordered = r.Tag != 0 && ((LogiFlags)r.Flags & LogiFlags.Storage) == 0;
                    if (!world.RackPut(c, pad, d, ordered))
                        return false; // (it waits on the bed while the pad can't take it)
                    ConstructLogisticsPlan.Arrived(world, c, ConstructPads.Owner(c, pad), r.Tag, d);
                    ConstructLifters.PadArrived(world, c, pad, ordered ? r.Tag : (ushort)0, d);
                    return true;
                }
                return PassOn(c, to, r, d); // (made for another's order: it rolls on across the bed, never into the machine)
            }
            if (!(td.IsLaserCutter ? ConstructLaserCutter.TakeFromConveyor(world, c, to, d) : ConstructPanelFormer.TakeFromConveyor(world, c, to, d, scrap: true))) // (extruded sheet into its bin; a cutter's blank onto its bed)
                return false;
            ConstructLogisticsPlan.Arrived(world, c, to, r.Tag, d); // (a chained panel order's sheet: it counts for its link)
            return true;
        }
        if (td.IsAssembler)
        {
            // A part its recipe uses, while it has room: the chute's own test (a part it doesn't use waits at its door).
            // (or a part a later order of its queue uses: else one waiting at the front stops all that rides behind it)
            if (!(world.WantsPart(c, to, d.Id) && world.FeedPart(c, to, d)) && !world.FeedQueuedPart(c, to, d))
                return false;
            ConstructLogisticsPlan.Arrived(world, c, to, r.Tag, d); // (a chained order's input: it counts for its link)
            return true;
        }
        if (td.IsPress && ConstructRoutes.PortOf(td, RouteType.Conveyor, c.Routes[route].ToPort) is { } port)
        {
            var bay = port.Name.Length > 9 && int.TryParse(port.Name.AsSpan(9), out var nb) ? nb - 1 : 0; // ("Piece in", "Piece in 2"...)
            if (!ConstructPress.TakeFromConveyor(world, query, c, to, bay, d, r.Health / 255f, r.Paint))
                return false;
            ConstructLogisticsPlan.Arrived(world, c, to, r.Tag, d); // (a chained press order's blank or kit: it counts for its link, Press phase 4)
            return true;
        }
        return false;
    }

    private static void Remove(Construct c, int i)
    {
        c.ConveyedCount--;
        if (i < c.ConveyedCount)
            Array.Copy(c.Conveyed, i + 1, c.Conveyed, i, c.ConveyedCount - i);
    }

    /// <summary>Server: rider <paramref name="i"/> comes off its link as a loose body where it is, at rest on the link (a roll onto a Bake Oven's bed takes it: ConstructRollOut.cs). The body, or null.</summary>
    internal static Construct? TakeOff(ConstructWorld world, Construct c, int i)
    {
        var r = c.Conveyed[i];
        if (r.Route >= c.RouteCount || ConstructParts.Get(r.Def) is not { } d)
            return null;
        WorldPose(c, r.Route, r, d, out var centre, out var rot);
        if (ConstructEdits.DropLoose(world, d, centre, rot) is not { } body)
            return null;
        Remove(c, i);
        body.Owner = c.Owner;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, d.Health * r.Health / 255f);
        np.Paint = r.Paint;
        return body;
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

    /// <summary>
    /// Server: rider <paramref name="i"/>, the lead piece at a Router that no rack can take, comes off the line as a loose body set
    /// down beside its way in at the first clear spot (either side of the line, up to four rows back along it, the body's own up), clear
    /// of every body's solid boxes the Router's own included (<see cref="ConstructLogistics"/> NoPlace). The new body's id, or 0: no clear
    /// spot, or the world is full (it stays on the line).
    /// </summary>
    public static ushort SetOff(ConstructWorld world, Construct c, int i, ConstructPartDef d, out Vector3 spot)
    {
        spot = default;
        var r = c.Conveyed[i];
        if (r.Route >= c.RouteCount)
            return 0;
        WorldPose(c, r.Route, r, d, out var centre, out var rot);
        var back = r;
        back.At = MathF.Max(0, r.At - 0.25f);
        WorldPose(c, r.Route, back, d, out var behind, out _);
        var bodyUp = Vector3.Transform(Vector3.UnitY, c.Rot); // (the body's own up: a base on a planetoid stands on its ground)
        var travel = centre - behind;
        travel -= bodyUp * Vector3.Dot(travel, bodyUp);
        travel = travel.LengthSquared() > 1e-6f ? Vector3.Normalize(travel) : Vector3.Transform(Vector3.UnitZ, c.Rot);
        var side = Vector3.Normalize(Vector3.Cross(travel, bodyUp));
        Size(d, out var across, out var up, out var along);
        var wide = MathF.Max(across, up);
        var found = false;
        for (var k = 0; k < 8 && !found; k++)
        {
            var at = centre + side * ((k & 1) == 0 ? 1 : -1) * (0.9f + wide) - travel * (k >> 1) * (along + 0.3f) + bodyUp * 0.2f; // (beside the Router: half a Router, the piece, a gap; then rows back)
            if (SpotClear(world, at, travel, along, wide * 0.5f))
                (spot, found) = (at, true);
        }
        if (!found || ConstructEdits.DropLoose(world, d, spot, rot) is not { } body)
            return 0;
        Remove(c, i);
        body.Owner = c.Owner;
        body.Vel = c.PointVel(centre);
        body.AngVel = c.AngVel;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, d.Health * r.Health / 255f);
        np.Paint = r.Paint;
        return body.Id;
    }

    /// <summary>Is a piece <paramref name="along"/> long and <paramref name="r"/> round at <paramref name="at"/> clear of every body's solid boxes? (A rare path: a set-off.)</summary>
    private static bool SpotClear(ConstructWorld world, Vector3 at, Vector3 travel, float along, float r)
    {
        var a = at - travel * MathF.Max(0, along * 0.5f - r);
        var b = at + travel * MathF.Max(0, along * 0.5f - r);
        r = MathF.Max(0.05f, r);
        foreach (var o in world.Bodies)
            if (Vector3.DistanceSquared(o.WorldCom, at) < (o.Radius + along + r + 1) * (o.Radius + along + r + 1) && !world.CapsuleClear(o, a, b + (a == b ? new Vector3(0, 1e-3f, 0) : default), r))
                return false;
        return true;
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

    /// <summary>One body's riders, the whole list: u16 body, u16 n, each u8 link, f32 front (m: before a junction's output starts, below 0), u8 health, u32 paint, u8 the link it came through plus one, u16 destination store handle, u16 order, u8 LogiFlags (protocol 264), then the piece's key (protocol 245).</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Riders).U16(c.Id).U16((ushort)c.ConveyedCount);
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref var r = ref c.Conveyed[i];
            w.U8(r.Route).F32(r.At).U8(r.Health).U32(r.Paint).U8(r.ViaPlus1).U16(r.Dest).U16(r.Tag).U8(r.Flags);
            ConstructStorage.WriteDef(w, r.Def); // (a piece's key, or a press's part by its id: protocol 247)
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
            var dest = r.U16();
            var tag = r.U16();
            var flags = r.U8();
            if (!float.IsFinite(at))
                throw new MalformedPacketException("a rider nowhere");
            var def = ConstructStorage.ReadDef(ref r);
            if (c == null)
                continue;
            // The same piece this copy has moved on a little past the server's word (which is a lag old): kept where it is, no step back.
            if (i < had && c.Conveyed[i].Route == route && c.Conveyed[i].Def == def && c.Conveyed[i].At > at && c.Conveyed[i].At - at < 0.5f)
                at = c.Conveyed[i].At;
            c.Conveyed[c.ConveyedCount++] = new CRider { Def = def, Route = route, At = at, Health = hp, Paint = paint, ViaPlus1 = via, Dest = dest, Tag = tag, Flags = flags };
        }
        if (c != null)
        {
            c.ConveyedStamp = Clock;
            if (!c.Dirty)
                ConstructLogistics.Refresh(c, server: false); // (a Router's lit way and stuck lamp, from the riders as heard)
        }
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
        public readonly List<int> Index = new(); // each kept rider's place in the save's list (the LOGI block is in that order)
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
            if (def == ConstructParts.None || !float.IsFinite(at) || ConstructParts.Get(def) == null)
                continue; // (a store part rides too, new and empty, to a Building Station's hold: its order counts it as Sent, so it is kept)
            saved.Riders.Add(new CRider { Def = def, Route = route, At = MathF.Max(0, at), Health = Math.Max((byte)1, hp), Paint = paint });
            saved.Index.Add(i);
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
