using System.Numerics;

namespace CallOfSlop;

public sealed partial class ConstructWorld
{
    internal readonly List<ConstructRollOut.Roll> BodyRolls = new(4); // server: bodies rolling along Roller Tile runs (ConstructRollOut.cs). Not saved
    internal readonly Dictionary<int, ushort> OvenRolled = new(4); // server: the last body each oven ((body << 16) | part) rolled out, or tried to. Not saved
}

/// <summary>
/// Roll-out and roll-in (Sean, 2026-11-23: "the oven should allow parts coming in to be rolled directly into it, and the construction
/// station should be able to roll out, so it can push pieces it has finished out into the conveying system"). Docs: production.md
/// "Roll-out and roll-in".
/// <para>
/// A Roller Tile run whose first tile is butted on a Building Station's bay (its line, run back past its first tile, meets the bay's
/// floor) is the bay's <b>way out</b>. When a build is done and still stands in the bay, the station rolls it out: the body, held
/// frozen, lines up on the run, rides along it at the conveyor's speed (<see cref="ConstructConveyor.Speed"/>) and stops with its
/// front at the run's last tile, where it is let go. A run whose line, run on past its last tile, meets a Bake Oven's bed (the oven
/// <b>takes</b> the run) carries the body on, once the bed is empty with its hood up, onto the bed's middle as a crane sets a kit
/// (ConstructBakeOven.Landing, a quarter turn if that is what fits), and the oven bakes it. A piece riding a run the oven takes, and
/// waiting at its end, rolls onto the bed as a body the same way, so a baked part can be sent to be un-baked.
/// </para>
/// <para>
/// A body's roll is a carry, as a lifter's (ConstructLifterBodies.cs): the server poses it every tick in its run's construct's frame,
/// so a moving ship keeps it, and clients see its poses; nothing new goes on the wire. A save mid-roll keeps the body where its roll
/// ends (the run's end, or the bed), let go. Sub-jobs (their main station takes them) and Dress jobs never roll. Server only;
/// nothing allocates on a tick.
/// </para>
/// </summary>
public static class ConstructRollOut
{
    internal sealed class Roll
    {
        public ushort Body, Host; // the rolling body and the construct its run is on
        public int Run, Oven = -1, FromOven = -1; // the run's conveyor link (at the start); the Bake Oven's Core that takes it, or -1; the oven it rolls out of, or -1
        public Vector3 A, B, C, D; // the body's origin in the host's frame: where it started, lined up on the run, at the run's end, on the bed
        public Quaternion Rot, RotD; // its turn in the host's frame, and on the bed
        public byte Leg; // 0: A to B, 1: B to C, 2: waits at C for the bed, 3: C to D (turning to RotD)
        public float T; // metres along the leg
        public bool WasFrozen; // frozen before its roll (a blueprint's frozen base): it stays so where it is let go, unless on a bed
    }

    private const float Probe = 0.3f; // m past a run's end that its line looks for a bay or a bed
    private const float Lift = 0.02f; // m: a rolling body's underside over the run's line

    // ---- Finding the ways --------------------------------------------------------------------------------------------------

    /// <summary>The run's line in its construct's frame: its first and last nodes and its way. False when link <paramref name="k"/> isn't a run.</summary>
    private static bool Line(Construct c, int k, out Vector3 n0, out Vector3 n1, out Vector3 dir)
    {
        n0 = n1 = dir = default;
        if ((uint)k >= (uint)c.RouteCount || !RollerRuns.IsRun(c, k))
            return false;
        n0 = c.RouteNodes[c.Routes[k].Start].Pos;
        n1 = c.RouteNodes[c.Routes[k].Start + 1].Pos;
        var way = n1 - n0;
        if (way.LengthSquared() < 1e-4f)
            return false;
        dir = Vector3.Normalize(way);
        return true;
    }

    /// <summary>Is body-frame point <paramref name="at"/> over station <paramref name="t"/>'s bay floor (within its outline, a little over and under)?</summary>
    private static bool InBay(Construct c, int t, Vector3 at)
    {
        ConstructStation.Bay(c, t, out var floor, out var rot, out var size);
        if (size.X <= 0)
            return false;
        c.WorldFrame(0, out var q, out var o);
        var rel = Vector3.Transform(o + Vector3.Transform(at, q) - floor, Quaternion.Conjugate(rot));
        return MathF.Abs(rel.X) <= size.X * 0.5f + Probe && MathF.Abs(rel.Z) <= size.Z * 0.5f + Probe && rel.Y >= -1.5f && rel.Y <= 2.5f;
    }

    /// <summary>The run out of station <paramref name="t"/>'s bay: a run whose first tile is butted on the bay (and that leaves it). Its link, or -1.</summary>
    public static int RunOut(Construct c, int t)
    {
        if ((uint)t >= (uint)c.PartCount || !c.DefOf(t).IsBuildStation || c.Parts[t].Broken)
            return -1;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && Line(c, k, out var n0, out var n1, out var dir)
                && InBay(c, t, n0 - dir * (RollerRuns.EndIn + Probe)) && !InBay(c, t, n1))
                return k;
        return -1;
    }

    /// <summary>The Bake Oven Core whose bed body-frame point <paramref name="at"/> lies over (its bed rectangle, a cell over or under its layer), or -1.</summary>
    public static int OvenAt(Construct c, Vector3 at)
    {
        for (var a = 0; a < c.MachineAsmCount; a++)
        {
            ref readonly var asm = ref c.MachineAsms[a];
            if (asm.W == 0 || (uint)asm.Core >= (uint)c.PartCount)
                continue;
            ref readonly var p = ref c.Parts[asm.Core];
            if (!p.D.IsBakeOven || p.Broken)
                continue;
            var cell = p.D.BlockCell;
            var rel = Rot24.Apply(MachineBlocks.Rot24Inverse(p.Rot), at - p.Pos) - (p.D.BlockOrigin ?? Vector3.Zero);
            float x = rel.X / cell, z = rel.Z / cell;
            if (x >= asm.X0 - 0.5f && x <= asm.X0 + asm.W - 0.5f && z >= asm.Z0 - 0.5f && z <= asm.Z0 + asm.L - 0.5f && MathF.Abs(rel.Y) <= cell)
                return asm.Core;
        }
        return -1;
    }

    /// <summary>A body-frame point and way of <paramref name="c"/> in oven <paramref name="part"/>'s tunnel frame (ConstructBakeOven.Bed).</summary>
    private static bool InTunnel(Construct c, int part, Vector3 at, Vector3 way, out Vector3 local, out Vector3 dir, out ConstructBakeOven.Bed bed)
    {
        (local, dir, bed) = (default, default, default);
        if (!MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
            return false;
        bed = new ConstructBakeOven.Bed(c, asm);
        if (!Matrix4x4.Invert(bed.ToCore, out var toTunnel))
            return false;
        ref readonly var p = ref c.Parts[part];
        var inv = MachineBlocks.Rot24Inverse(p.Rot);
        local = Vector3.Transform(Rot24.Apply(inv, at - p.Pos), toTunnel);
        dir = Vector3.TransformNormal(Rot24.Apply(inv, way), toTunnel);
        return true;
    }

    /// <summary>
    /// The Bake Oven that takes run <paramref name="k"/> (its line, run on past its last tile, meets the oven's bed at its tunnel's in
    /// end, running along the tunnel), or -1.
    /// </summary>
    public static int OvenOf(Construct c, int k)
    {
        if (!Line(c, k, out _, out var n1, out var dir))
            return -1;
        var at = n1 + dir * (RollerRuns.EndIn + Probe);
        var oven = OvenAt(c, at);
        if (oven < 0 || !InTunnel(c, oven, at, dir, out var local, out var way, out var bed))
            return -1;
        return way.Z > 0.9f && local.Z < bed.Along * 0.5f ? oven : -1;
    }

    /// <summary>Why run <paramref name="k"/> rolls into no oven's bed (for a check's report): where it ends and what lies there.</summary>
    public static string NoOvenWhy(Construct c, int k)
    {
        if (!Line(c, k, out _, out var n1, out var dir))
            return "not a roller run";
        var at = n1 + dir * (RollerRuns.EndIn + Probe);
        var oven = OvenAt(c, at);
        if (oven < 0)
            return $"its end ({at.X:0.0}, {at.Z:0.0}, heading {dir.X:0.0}, {dir.Z:0.0}) is over no oven's bed";
        if (!InTunnel(c, oven, at, dir, out var local, out var way, out var bed))
            return $"the Bake Oven part {oven} there has no bed";
        return $"it meets the Bake Oven part {oven}'s bed at {local.Z:0.0} m of its {bed.Along:0.0} m tunnel, heading {way.X:0.0}, {way.Z:0.0} in the tunnel's frame (it must enter at the in end, along the tunnel)";
    }

    /// <summary>
    /// Oven <paramref name="part"/>'s way out: a run whose line, run back past its first tile, meets the bed at the tunnel's out end and
    /// leads on along the tunnel. Its link, or -1.
    /// </summary>
    public static int OutRunOf(Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (c.Routes[k].Type != RouteType.Conveyor || !Line(c, k, out var n0, out _, out var dir))
                continue;
            var at = n0 - dir * (RollerRuns.EndIn + Probe);
            if (OvenAt(c, at) == part && InTunnel(c, part, at, dir, out var local, out var way, out var bed) && way.Z > 0.9f && local.Z > bed.Along * 0.5f)
                return k;
        }
        return -1;
    }

    /// <summary>How far past the tunnel's out end oven <paramref name="part"/>'s way out reaches (m), or 0 with none.</summary>
    public static float OutReach(Construct c, int part)
    {
        var k = OutRunOf(c, part);
        if (k < 0 || !Line(c, k, out _, out var n1, out var dir) || !InTunnel(c, part, n1 + dir * RollerRuns.EndIn, dir, out var local, out _, out var bed))
            return 0;
        return MathF.Max(0, local.Z - bed.Along);
    }

    /// <summary>Is a baked part rolling out of oven <paramref name="part"/>'s tunnel? (Nothing rolls in until it is out.)</summary>
    public static bool RollingOut(ConstructWorld world, Construct c, int part)
    {
        foreach (var r in world.BodyRolls)
            if (r.Host == c.Id && r.FromOven == part)
                return true;
        return false;
    }

    // ---- Starting a roll ---------------------------------------------------------------------------------------------------

    /// <summary>Is body <paramref name="body"/> rolling? Its roll, or null.</summary>
    internal static Roll? Of(ConstructWorld world, ushort body)
    {
        foreach (var r in world.BodyRolls)
            if (r.Body == body)
                return r;
        return null;
    }

    /// <summary>Server, a done station's look (twice a second): its build rolled out of the bay along its way out, if it has one.</summary>
    public static void FromStation(ConstructWorld world, Construct c, int t, StationJob j)
    {
        if (j.Body == 0 || j.RolledBody == j.Body)
            return;
        j.RolledBody = j.Body; // (tried once a build: a body that can't roll stays for a crane or its driver)
        if (world.Get(j.Body) is not { } b || b == c || b.HeldBy != 0 || b.CarriedBy != 0 || Of(world, b.Id) != null
            || ConstructLifters.CarryingBody(world, b.Id) != null || ConstructStation.BayClearOf(world, c, t, b))
            return;
        var k = RunOut(c, t);
        if (k < 0)
            return;
        if (Start(world, c, k, b, OvenOf(c, k)) is { } r)
            Console.WriteLine($"[rollout] {c.DefOf(t).Name} {t} on construct {c.Id} rolls {j.Name} (body {b.Id}, {b.PartCount} parts) out along its run"
                + (r.Oven >= 0 ? $" to the {c.DefOf(r.Oven).Name} {r.Oven}'s bed" : "") + $": {(Vector3.Distance(r.A, r.B) + Vector3.Distance(r.B, r.C)) / ConstructConveyor.Speed:0.0} s");
    }

    /// <summary>
    /// Server: body <paramref name="b"/> starts rolling along run <paramref name="k"/> of <paramref name="c"/>: lined up on it, to its end, then
    /// onto oven <paramref name="oven"/>'s bed (-1: let go at the end). Null when there is nowhere to roll (it already stands at the end).
    /// </summary>
    private static Roll? Start(ConstructWorld world, Construct c, int k, Construct b, int oven)
    {
        if (!Line(c, k, out var n0, out var n1, out var dir))
            return null;
        if (b.Dirty)
            b.Rebuild();
        c.WorldFrame(0, out var q, out var o);
        var inv = Quaternion.Conjugate(q);
        var lr = Quaternion.Normalize(inv * b.Rot);
        var lp = Vector3.Transform(b.Pos - o, inv);
        // The body's box in the host's frame (the run lies along one of its axes) and its underside's middle.
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? b.LocalMin.X : b.LocalMax.X, (i & 2) == 0 ? b.LocalMin.Y : b.LocalMax.Y, (i & 4) == 0 ? b.LocalMin.Z : b.LocalMax.Z);
            var v = lp + Vector3.Transform(corner, lr);
            (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
        }
        var under = new Vector3((lo.X + hi.X) * 0.5f, lo.Y, (lo.Z + hi.Z) * 0.5f);
        var off = lp - under; // (origin from the underside's middle)
        var half = MathF.Abs(Vector3.Dot(hi - lo, dir)) * 0.5f;
        var s = n0 - dir * RollerRuns.EndIn; // (the run's in end, on its line)
        var len = Vector3.Dot(n1 + dir * RollerRuns.EndIn - s, dir);
        var at0 = Vector3.Dot(under - s, dir);
        var at1 = MathF.Max(at0, len - half); // (its front at the run's end; a body longer than the run stops where it is)
        var up = Vector3.UnitY * Lift;
        var r = new Roll
        {
            Body = b.Id, Host = c.Id, Run = k, Oven = oven, Rot = lr, RotD = lr,
            A = lp, B = s + dir * at0 + up + off, C = s + dir * at1 + up + off,
        };
        r.D = r.C;
        if (oven < 0 && Vector3.Distance(r.A, r.B) + Vector3.Distance(r.B, r.C) < 0.1f)
            return null; // (rolled out already: it stands at the run's end)
        r.WasFrozen = b.Frozen;
        b.Frozen = true;
        b.HeldBy = c.Id;
        b.Vel = b.AngVel = Vector3.Zero;
        world.BodyRolls.Add(r);
        if (oven >= 0)
            world.OvenKits.TryAdd(c.Id << 16 | oven, b.Id); // (the oven's kit on its way: it starts no other: ConstructBakeOvenOrders.cs)
        return r;
    }

    /// <summary>
    /// Server, oven <paramref name="part"/>'s step with its bed empty and its hood up: the lead piece waiting at the end of a run the oven
    /// takes rolls onto the bed as a loose body. True when one went.
    /// </summary>
    public static bool RiderOnto(ConstructWorld world, Construct c, int part)
    {
        if (c.ConveyedCount == 0 || world.OvenKits.ContainsKey(c.Id << 16 | part))
            return false;
        foreach (var r in world.BodyRolls)
            if (r.Host == c.Id && (r.Oven == part || r.FromOven == part))
                return false; // (a body already on its way in, or the last one still on its way out)
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (c.Routes[k].Type != RouteType.Conveyor || OvenOf(c, k) != part || RollerRuns.WaitingLead(c, k) is not (var lead and >= 0))
                continue;
            if (ConstructConveyor.TakeOff(world, c, lead) is not { } body)
                return false;
            if (Start(world, c, k, body, part) is { } roll)
            {
                roll.B = roll.C = roll.A; // (it is at the run's end already: straight onto the bed)
                roll.Leg = 2;
                Console.WriteLine($"[rollout] a {body.Parts[0].D.Name} rolls off its run onto the {c.DefOf(part).Name} {part}'s bed on construct {c.Id} (body {body.Id})");
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Server, oven <paramref name="part"/>'s step with body <paramref name="body"/> done in its tunnel and the doors open: it rolls out of
    /// the out end along the oven's way out (<see cref="OutRunOf"/>), level, until its back is clear of the tunnel or its front is at the
    /// run's end, whichever is further, and is let go there for a crane or an arm. Nothing happens with no way out, while something lies
    /// in its way at the run, or once it is rolling.
    /// </summary>
    public static void OutOf(ConstructWorld world, Construct c, int part, int body)
    {
        if (world.Get((ushort)body) is not { } b || b == c || b.HeldBy != 0 || b.CarriedBy != 0 || b.GrabbedBy != 255 || Of(world, b.Id) != null
            || ConstructLifters.CarryingBody(world, b.Id) != null || world.OvenRolled.TryGetValue(c.Id << 16 | part, out var was) && was == b.Id)
            return;
        var k = OutRunOf(c, part);
        if (k < 0 || !Line(c, k, out var n0, out var n1, out var dir))
            return;
        if (b.Dirty)
            b.Rebuild();
        c.WorldFrame(0, out var q, out var o);
        var inv = Quaternion.Conjugate(q);
        var lr = Quaternion.Normalize(inv * b.Rot);
        var lp = Vector3.Transform(b.Pos - o, inv);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? b.LocalMin.X : b.LocalMax.X, (i & 2) == 0 ? b.LocalMin.Y : b.LocalMax.Y, (i & 4) == 0 ? b.LocalMin.Z : b.LocalMax.Z);
            var v = lp + Vector3.Transform(corner, lr);
            (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
        }
        var s = n0 - dir * RollerRuns.EndIn; // (the run's in end: the tunnel's out end)
        var len = Vector3.Dot(n1 + dir * RollerRuns.EndIn - s, dir);
        var half = MathF.Abs(Vector3.Dot(hi - lo, dir)) * 0.5f;
        var at0 = Vector3.Dot((lo + hi) * 0.5f - s, dir);
        var at1 = MathF.Max(half + 0.3f, len - half);
        if (at1 <= at0 + 0.05f)
            return;
        // It is lifted onto the run's line first if it lies lower (settled on the bed's platen between the rollers).
        var rise = MathF.Max(0f, s.Y + Lift - lo.Y);
        var lp1 = lp + Vector3.UnitY * rise;
        (lo, hi) = (lo + Vector3.UnitY * rise, hi + Vector3.UnitY * rise);
        // Something lying where it would go (the last part, not yet taken): it waits.
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
        var wide = MathF.Abs(Vector3.Dot(hi - lo, side)) * 0.5f;
        var mid = Vector3.Dot((lo + hi) * 0.5f, side);
        foreach (var other in world.Bodies)
        {
            if (other == c || other == b || other.PartCount == 0)
                continue;
            var at = Vector3.Transform(other.WorldCom - o, inv);
            var along = Vector3.Dot(at - s, dir);
            if (along > at0 + half && along < at1 + half + other.Radius && MathF.Abs(Vector3.Dot(at, side) - mid) < wide + other.Radius && MathF.Abs(at.Y - lo.Y) < 4f)
                return;
        }
        // The site's own parts in the way past its front (a pad's corner masts): it stops short of the first one.
        var front = at0 + half;
        var stop = at1 + half;
        var blocker = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref readonly var p = ref c.Parts[i];
            var pd = p.D;
            if (p.Seg != 0 || pd.IsRollerTile || i == part || i < c.BlockAsmOf.Length && c.BlockAsmOf[i] >= 0 && c.BlockAsmOf[i] < c.MachineAsmCount
                && c.MachineAsms[c.BlockAsmOf[i]].Core == part)
                continue; // (a run, its own Core and bed)
            if (pd.IsPieceRack && Rot24.Apply(p.Rot, Vector3.UnitY).Y > 0.99f && p.Pos.Y + pd.RackHoldMin.Y <= lo.Y + 0.15f)
                continue; // (a pad whose deck is under it: it rolls on over the deck, as a run's pieces do; its masts still stop it)
            var b0 = p.Pos + Rot24.Apply(p.Rot, pd.BoundsMin);
            var b1 = p.Pos + Rot24.Apply(p.Rot, pd.BoundsMax);
            var (pl, ph) = (Vector3.Min(b0, b1), Vector3.Max(b0, b1));
            if (ph.Y <= lo.Y + 0.15f || pl.Y >= hi.Y)
                continue; // (under its underside, a floor or a pad's deck, or over it)
            float a0 = Vector3.Dot(pl - s, dir), a1 = Vector3.Dot(ph - s, dir), s0 = Vector3.Dot(pl, side) - mid, s1 = Vector3.Dot(ph, side) - mid;
            if (MathF.Min(s0, s1) >= wide || MathF.Max(s0, s1) <= -wide)
                continue;
            var near = MathF.Min(a0, a1);
            if (MathF.Max(a0, a1) > front && near < stop)
                (stop, blocker) = (MathF.Max(front, near - 0.1f), i);
        }
        at1 = MathF.Min(at1, stop - half);
        world.OvenRolled[c.Id << 16 | part] = b.Id; // (once: what stops short is taken from where it stops)
        if (at1 <= at0 + 0.5f)
        {
            Console.WriteLine($"[rollout] body {b.Id} stays in the {c.DefOf(part).Name} {part}'s tunnel on construct {c.Id}: the {(blocker >= 0 ? c.DefOf(blocker).Name + " " + blocker : "run's end")} is in its way out");
            return;
        }
        var r = new Roll { Body = b.Id, Host = c.Id, Run = k, FromOven = part, Rot = lr, RotD = lr, A = lp, B = lp1, Leg = (byte)(rise > 0.01f ? 0 : 1) };
        r.C = r.D = lp1 + dir * (at1 - at0);
        r.WasFrozen = b.Frozen;
        b.Frozen = true;
        b.HeldBy = c.Id;
        b.Vel = b.AngVel = Vector3.Zero;
        world.BodyRolls.Add(r);
        Console.WriteLine($"[rollout] body {b.Id} ({b.PartCount} parts) rolls out of the {c.DefOf(part).Name} {part}'s tunnel on construct {c.Id}: {(at1 - at0) / ConstructConveyor.Speed:0.0} s"
            + (blocker >= 0 ? $", stopping short of the {c.DefOf(blocker).Name} {blocker}" : ""));
    }

    // ---- The step ----------------------------------------------------------------------------------------------------------

    /// <summary>Server, every tick: each rolling body posed along its way; at its end it is let go (on the bed, or at the run's end).</summary>
    public static void Step(ConstructWorld world)
    {
        if (world.BodyRolls.Count == 0)
            return;
        const float dt = 1f / World.TickRate;
        for (var i = world.BodyRolls.Count - 1; i >= 0; i--)
        {
            var r = world.BodyRolls[i];
            var c = world.Get(r.Host);
            var b = world.Get(r.Body);
            if (c == null || b == null || b.HeldBy != r.Host)
            {
                End(world, i, b, c); // (the run's construct went, or the body did, or something else took it)
                continue;
            }
            if (r.Leg == 2 && !Bed(world, c, r, b))
            {
                Pose(c, b, r.C, r.Rot);
                continue;
            }
            Vector3 from = r.Leg switch { 0 => r.A, 1 => r.B, _ => r.C }, to = r.Leg switch { 0 => r.B, 1 => r.C, _ => r.D };
            var leg = Vector3.Distance(from, to);
            r.T += ConstructConveyor.Speed * dt;
            var f = leg < 1e-3f ? 1f : MathF.Min(1f, r.T / leg);
            Pose(c, b, Vector3.Lerp(from, to, f), r.Leg == 3 ? Quaternion.Slerp(r.Rot, r.RotD, f) : r.Rot);
            if (f < 1f)
                continue;
            r.T = 0;
            if (r.Leg == 0)
                r.Leg = 1;
            else if (r.Leg == 1 && r.Oven >= 0)
                r.Leg = 2;
            else
            {
                if (r.Oven >= 0 && r.Leg == 3)
                    Console.WriteLine($"[rollout] body {b.Id} rolled onto the {c.DefOf(r.Oven).Name} {r.Oven}'s bed on construct {c.Id}");
                End(world, i, b, c);
            }
        }
    }

    /// <summary>Waiting at the run's end: is the oven's bed free (empty, hood up, no other body bound there)? If so the roll's last leg is set.</summary>
    private static bool Bed(ConstructWorld world, Construct c, Roll r, Construct b)
    {
        if ((uint)r.Oven >= (uint)c.PartCount || !c.DefOf(r.Oven).IsBakeOven || world.StoreOf(c, r.Oven) is not { } st)
        {
            if (world.OvenKits.TryGetValue(c.Id << 16 | r.Oven, out var was) && was == b.Id)
                world.OvenKits.Remove(c.Id << 16 | r.Oven);
            r.Oven = -1; // (the oven went: let go at the run's end)
            r.Leg = 3;
            r.D = r.C;
            return true;
        }
        if (st.Blocked != (byte)OvenPhase.Open || st.Refused != (byte)OvenStall.Empty || RollingOut(world, c, r.Oven))
            return false;
        var key = c.Id << 16 | r.Oven;
        if (world.OvenKits.TryGetValue(key, out var bound) && bound != b.Id && world.Get(bound) != null)
            return false; // (another kit is bound for the bed)
        // The landing, from its turn on the run (the oven chooses a quarter turn if that is what fits the clear cells).
        var (pos, rot) = (b.Pos, b.Rot);
        if (ConstructBakeOven.Landing(c, r.Oven, b, out var wp, out var wr) is { } no)
        {
            // (it never fits: let go at the run's end, and the oven's claim goes, so the oven isn't held for ever and a player can take it)
            Console.WriteLine($"[rollout] body {b.Id} is let go at the end of its run: it doesn't fit the {c.DefOf(r.Oven).Name} {r.Oven}'s bed: {no}");
            if (world.OvenKits.TryGetValue(key, out var mine) && mine == b.Id)
                world.OvenKits.Remove(key);
            r.Oven = -1;
            r.Leg = 3;
            r.D = r.C;
            return true;
        }
        (b.Pos, b.Rot) = (pos, rot);
        c.WorldFrame(0, out var q, out var o);
        var inv = Quaternion.Conjugate(q);
        r.D = Vector3.Transform(wp - o, inv);
        r.RotD = Quaternion.Normalize(inv * wr);
        world.OvenKits[key] = b.Id;
        r.Leg = 3;
        r.T = 0;
        return true;
    }

    private static void Pose(Construct c, Construct b, Vector3 local, Quaternion rot)
    {
        c.WorldFrame(0, out var q, out var o);
        b.Pos = b.PrevPos = b.NetPos = b.NetFromPos = o + Vector3.Transform(local, q);
        b.Rot = b.PrevRot = b.NetRot = b.NetFromRot = Quaternion.Normalize(q * rot);
        b.Vel = b.AngVel = Vector3.Zero;
        b.Wake(); // (an awake body's pose goes to clients every pose packet)
    }

    /// <summary>The roll ends: the body is let go where it is (it settles), and the oven's claim on it, if any, goes.</summary>
    private static void End(ConstructWorld world, int i, Construct? b, Construct? c)
    {
        var r = world.BodyRolls[i];
        world.BodyRolls.RemoveAt(i);
        if (r.Oven >= 0 && world.OvenKits.TryGetValue(r.Host << 16 | r.Oven, out var bound) && bound == r.Body)
            world.OvenKits.Remove(r.Host << 16 | r.Oven);
        if (b == null || b.HeldBy != r.Host)
            return;
        b.HeldBy = 0;
        if (!r.WasFrozen || r.Leg == 3 && r.Oven >= 0)
            ConstructEdits.Unfreeze(world, b);
        b.Wake();
        world.StructureDirty.Add(b.Id);
    }

    /// <summary>Is a body rolling and should it load frozen (a frozen base let go off a bed stays frozen)?</summary>
    internal static bool SavedFrozen(ConstructWorld world, Construct b, bool frozen) =>
        Of(world, b.Id) is { } r ? r.WasFrozen && !(r.Leg == 3 && r.Oven >= 0) : frozen;

    /// <summary>For a save made mid-roll: where the body's roll ends (the bed, or the run's end), so it loads there, let go.</summary>
    public static bool SavedPose(ConstructWorld world, Construct b, out Vector3 pos, out Quaternion rot)
    {
        (pos, rot) = (b.Pos, b.Rot);
        if (Of(world, b.Id) is not { } r || world.Get(r.Host) is not { } c)
            return false;
        c.WorldFrame(0, out var q, out var o);
        var (at, turn) = r.Leg == 3 ? (r.D, r.RotD) : (r.C, r.Rot);
        pos = o + Vector3.Transform(at, q);
        rot = Quaternion.Normalize(q * turn);
        return true;
    }
}
