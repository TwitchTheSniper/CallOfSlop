using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>Why an Extruder isn't running (a store's <see cref="CStore.Refused"/> byte: on the wire, so a client's look line says the same). Keep these under the material count's limit.</summary>
public enum ExtrudeStall : byte
{
    None,
    NoPower,
    NoMaterial,
    BadMaterial, // what it holds can't be extruded
    Mixed, // it holds another material than the piece on its die
    BadShape, // its Custom section can't be made
    AtCap, // the piece is as long as it can be
    Blocked, // the ground, a wall or something fixed is in the path
    TooHeavy, // something loose it can't push is in the path
}

/// <summary>
/// The Extruder and its saw (docs/sandbox/CONSTRUCTS.md, "Extrusion"). An Extruder is a bin with a die: while it runs, the
/// piece on its die grows out of the die's face at the part's speed, using material by volume. The piece is a part of the
/// machine's own construct, linked to its die socket, so it rides with the machine, counts in its mass and is solid, and
/// everything about it is found from that link (nothing to keep in step when parts are renumbered, split or sent). A step
/// of growth (<see cref="Extrusion.StepCm"/>) swaps the part's def for the longer one and refreshes only the body's shape
/// (<see cref="Construct.RefreshShape"/>), a small delta on the wire. A Cut-off Saw across the path cuts on a rising signal
/// or by length: what is past its blade becomes a body of its own, the rest stays on the die and keeps growing. Server, at
/// 10 Hz, looking only at constructs that have one; a shape once made is kept, so a line in steady work allocates only
/// the bodies it cuts off.
/// </summary>
public static class ConstructExtruder
{
    public const int Every = 6; // ticks between steps (10 Hz)
    private const int StepMm = Extrusion.StepCm * 10;
    private const int PushTries = 15; // steps of pushing something that doesn't move before it says so
    private const int Milli = 1000; // a store's JobAmount counts thousandths of a unit of material owed here
    public const byte SawPending = 2; // a saw's State: a rising signal waits for this step
    private static readonly List<Construct> _near = new(32);

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
        if (++world.ExtrudeTicks < Every)
            return;
        world.ExtrudeTicks = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasExtruder)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsExtruder && world.StoreOf(c, i) is { } st)
                    Work(world, query, c, i, st);
        }
        // (cuts asked for by a rising signal are for this step: one that met no piece goes unused)
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasSaw)
                continue;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.Logic == CLogic.Saw)
                    c.Parts[i].State &= unchecked((byte)~SawPending);
        }
    }

    /// <summary>The part on the machine's die, or -1.</summary>
    public static int ProductOf(Construct c, int part)
    {
        var sock = c.Parts[part].D.ExtDieSocket;
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            if (l.A == part && l.SA == sock && l.B < c.PartCount)
                return l.B;
            if (l.B == part && l.SB == sock && l.A < c.PartCount)
                return l.A;
        }
        return -1;
    }

    /// <summary>Is part <paramref name="i"/> on a machine's die (linked to its die socket)?</summary>
    public static bool OnDie(Construct c, int i)
    {
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            if (l.A == i && l.B < c.PartCount && c.DefOf(l.B) is { IsExtruder: true } a && l.SB == a.ExtDieSocket)
                return true;
            if (l.B == i && l.A < c.PartCount && c.DefOf(l.A) is { IsExtruder: true } b && l.SA == b.ExtDieSocket)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The machine's die in its own frame: its centre and size across the face. It starts as the part's own and reaches across
    /// every working Die Frame joined to it (through its links and other frames), up to the part's maximum.
    /// </summary>
    public static void Die(Construct c, int part, out Vector2 centre, out Vector2 size)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var half = d.ExtDie * 0.5f;
        var at = new Vector2(d.ExtDieAt.X, d.ExtDieAt.Y);
        var min = at - half;
        var max = at + half;
        Span<bool> seen = stackalloc bool[c.PartCount];
        Span<ushort> queue = stackalloc ushort[c.PartCount];
        int head = 0, tail = 0;
        seen[part] = true;
        queue[tail++] = (ushort)part;
        var back = Quaternion.Conjugate(Rot24.Quat(p.Rot));
        while (head < tail)
        {
            int from = queue[head++];
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var l = ref c.Links[k];
                int other = l.A == from ? l.B : l.B == from ? l.A : -1;
                if (other < 0 || other >= c.PartCount || seen[other] || !c.Parts[other].D.IsDieFrame || c.Parts[other].Broken)
                    continue;
                seen[other] = true;
                queue[tail++] = (ushort)other;
                c.PartBounds(other, out var mn, out var mx);
                var a = Vector3.Transform(mn - p.Pos, back);
                var b = Vector3.Transform(mx - p.Pos, back);
                min = Vector2.Min(min, new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y)));
                max = Vector2.Max(max, new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y)));
            }
        }
        centre = (min + max) * 0.5f;
        size = Vector2.Min(max - min, d.ExtMaxSection);
    }

    /// <summary>The section the machine's settings ask for, held inside its die (centimetres, on the settings' 5 cm step).</summary>
    public static void Section(Construct c, int part, out ulong mask, out int w, out int h, out Vector2 centre)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        Die(c, part, out centre, out var size);
        var wide = MathF.Min(ConstructConfig.Get(p, d, CfgKey.Width, 0.25f), size.X);
        var tall = MathF.Min(ConstructConfig.Get(p, d, CfgKey.Height, 0.25f), size.Y);
        w = Math.Clamp((int)MathF.Floor(wide * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        h = Math.Clamp((int)MathF.Floor(tall * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        mask = MaskOf(p, d);
    }

    /// <summary>The section a machine's Profile and cell settings give.</summary>
    public static ulong MaskOf(in CPart p, ConstructPartDef d)
    {
        var profile = (int)ConstructConfig.Get(p, d, CfgKey.Profile, 0);
        if (profile < Extrusion.Presets.Length)
            return Extrusion.Presets[profile].Mask;
        return (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskA, 0) | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskB, 0) << 16
            | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskC, 0) << 32 | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskD, 0) << 48;
    }

    private static void SetStall(ConstructWorld world, CStore st, ExtrudeStall s)
    {
        if (st.Refused == (byte)s)
            return;
        st.Refused = (byte)s;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    private static void Work(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var product = ProductOf(c, part);
        if (product >= 0 && !c.Parts[product].D.IsExtrusion)
            product = -1; // (a bad link: nothing it can grow)
        var len = product >= 0 ? c.Parts[product].D.ExKey.L : 0;
        p.Out = p.OutNext = len / 100f; // (its output: the length so far)
        if (product >= 0 && Saws(world, c, ref product))
            len = product >= 0 ? c.Parts[product].D.ExKey.L : 0;
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.None);
            return;
        }
        var why = Grow(world, query, c, part, st, product, len);
        SetStall(world, st, why);
        ConstructGatherer.SetBusy(world, st, why == ExtrudeStall.None);
    }

    /// <summary>A step's growth, or why not.</summary>
    private static ExtrudeStall Grow(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, int len)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!ConstructRecycler.Powered(world, c, part))
            return ExtrudeStall.NoPower;
        ExtrusionKey key;
        Vector3 origin; // the piece's origin in the machine's segment frame
        byte rot;
        var fresh = product < 0;
        if (fresh)
        {
            if (st.Amount <= 0)
                return ExtrudeStall.NoMaterial;
            if (ExtrusionTable.Current.Find(st.Material) == null)
                return ExtrudeStall.BadMaterial;
            Section(c, part, out var mask, out var w, out var h, out var centre);
            if (Extrusion.Refusal(mask) != null)
                return ExtrudeStall.BadShape;
            key = new ExtrusionKey(mask, (ushort)w, (ushort)h, 0, st.Material, true);
            origin = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, new Vector3(centre.X, centre.Y, d.ExtDieAt.Z)));
            rot = p.Rot;
        }
        else
        {
            ref var pp = ref c.Parts[product];
            key = pp.D.ExKey;
            origin = pp.Pos;
            rot = pp.Rot;
            if (st.Amount > 0 && st.Material != key.Material)
                return ExtrudeStall.Mixed;
        }
        var limit = Math.Min(Extrusion.MaxLengthCm, (int)MathF.Round(d.ExtMaxLength * 100));
        if (len >= limit)
            return ExtrudeStall.AtCap;
        var row = ExtrusionTable.Current.Find(key.Material);
        if (row == null)
            return ExtrudeStall.BadMaterial;
        var areaM2 = Extrusion.Fill(key.Mask) * key.W * key.H / 10000f;
        st.JobTicks += (int)(d.ExtSpeed * Every / World.TickRate * 1000); // (millimetres grown and not yet a step)
        var steps = st.JobTicks / StepMm;
        if (steps == 0)
            return st.Amount > 0 ? ExtrudeStall.None : ExtrudeStall.NoMaterial;
        steps = Math.Min(steps, (limit - len) / Extrusion.StepCm);
        if (steps == 0)
            return ExtrudeStall.AtCap;
        // Material first: a step that can't be paid for waits.
        var owed = (long)(areaM2 * Extrusion.StepCm / 100f * steps * row.UnitsPerM3 * Milli) + st.JobAmount;
        var units = (int)(owed / Milli);
        if (units > st.Amount)
        {
            st.JobTicks = StepMm - 1;
            return ExtrudeStall.NoMaterial;
        }
        if (Extrusion.Def(key with { L = (ushort)(len + steps * Extrusion.StepCm) }) is not { } next)
            return ExtrudeStall.BadShape;
        if (fresh && c.PartCount >= Construct.MaxParts)
            return ExtrudeStall.Blocked;
        switch (PathClear(query, world, c, product, next, origin, rot, p.Seg, len, steps, d.ExtPush))
        {
            case Path.Blocked:
                st.JobTicks = StepMm - 1;
                return ExtrudeStall.Blocked;
            case Path.Pushing:
                st.JobTicks = StepMm - 1;
                if (st.JobMaterial < PushTries)
                {
                    st.JobMaterial++;
                    return ExtrudeStall.None;
                }
                return ExtrudeStall.TooHeavy;
        }
        st.JobMaterial = 0;
        st.JobTicks -= steps * StepMm;
        st.JobAmount = (int)(owed - units * (long)Milli);
        if (units > 0)
            world.Withdraw(c, part, units);
        if (fresh)
        {
            var i = world.AddPart(c, next, origin, rot, p.Seg);
            if (i < 0)
                return ExtrudeStall.Blocked;
            world.AddLink(c, part, d.ExtDieSocket, i, 0);
            return ExtrudeStall.None;
        }
        Resize(world, c, product, next);
        return ExtrudeStall.None;
    }

    private enum Path : byte { Clear, Blocked, Pushing }

    /// <summary>
    /// Is the room the new length takes free? The ground and fixed things stop it; a free body in the way is pushed along the
    /// path (<paramref name="pushForce"/>, newtons) and growth waits for it to go, so nothing is ever grown into.
    /// </summary>
    private static Path PathClear(IWorldQuery query, ConstructWorld world, Construct c, int product, ConstructPartDef next, Vector3 origin, byte rot, byte seg, int len, int steps, float pushForce)
    {
        c.WorldFrame(seg, out var q, out var t);
        var turn = q * Rot24.Quat(rot);
        var axis = Vector3.Transform(Vector3.UnitZ, turn);
        var world0 = t + Vector3.Transform(origin, q);
        var z = len / 100f;
        var depth = steps * Extrusion.StepCm / 100f;
        var result = Path.Clear;
        for (var b = 0; b < next.SolidBoxes; b++)
        {
            var box = next.Boxes[b];
            var centre = world0 + Vector3.Transform(box.Center with { Z = z + depth * 0.5f }, turn);
            var half = Vector3.Max(box.Half with { Z = depth * 0.5f } - new Vector3(0.01f), new Vector3(0.004f));
            var obb = new Obb(centre, half, turn);
            if (query.OverlapBox(obb, QueryFilter.All))
                return Path.Blocked;
            var near = world.Near(centre, half.Length() + 0.5f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var other = near[k];
                if (other.Dirty)
                    other.Rebuild();
                if (!Hits(other, other == c ? product : -1, obb))
                    continue;
                if (other == c || other.Anchored || other.CarriedBy != 0)
                    return Path.Blocked;
                Push(other, axis, pushForce);
                result = Path.Pushing;
            }
        }
        return result;
    }

    private static bool Hits(Construct b, int skip, in Obb obb)
    {
        var reach = obb.Half.Length();
        for (var i = 0; i < b.PartCount; i++)
        {
            if (i == skip)
                continue;
            b.WorldFrame(b.Parts[i].Seg, out var q, out var t);
            b.PartBounds(i, out var mn, out var mx);
            if (Vector3.Distance(t + Vector3.Transform((mn + mx) * 0.5f, q), obb.Center) > reach + (mx - mn).Length() * 0.5f + 0.1f)
                continue;
            var boxes = b.SolidCount(i);
            for (var k = 0; k < boxes; k++)
            {
                b.SolidBox(i, k, out var oc, out var oh);
                if (Geometry.ObbOverlap(obb, new Obb(t + Vector3.Transform(oc, q), oh, q)))
                    return true;
            }
        }
        return false;
    }

    private static void Push(Construct b, Vector3 axis, float force)
    {
        var dv = force * (Every / (float)World.TickRate) / MathF.Max(1, b.Mass);
        var along = Vector3.Dot(b.Vel, axis);
        const float most = 0.6f; // m/s: a shove, not a launch
        if (along >= most)
            return;
        b.Vel += axis * MathF.Min(dv, most - along);
        b.Wake();
    }

    /// <summary>The piece takes a new length in place: its def, its health by how much it grew, and the body's shape without a full rebuild.</summary>
    private static void Resize(ConstructWorld world, Construct c, int product, ConstructPartDef next)
    {
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        pp.Def = next.Id;
        pp.Health = MathF.Max(1, pp.Health + next.Health - was.Health);
        c.Wake();
        world.ShapeChanged(c);
        world.ExtrudeDirty.Add((c.Id, (ushort)product));
    }

    /// <summary>
    /// Any saw across the piece's path that should cut now: a rising signal into it, or its Cut every length past the blade.
    /// True when it cut (<paramref name="product"/> stays the same part, shorter).
    /// </summary>
    private static bool Saws(ConstructWorld world, Construct c, ref int product)
    {
        c.PartBounds(product, out var mn, out var mx);
        c.WorldFrame(c.Parts[product].Seg, out var q, out var t);
        ref var pp = ref c.Parts[product];
        var key = pp.D.ExKey;
        var turn = q * Rot24.Quat(pp.Rot);
        var origin = t + Vector3.Transform(pp.Pos, q);
        var axis = Vector3.Transform(Vector3.UnitZ, turn);
        var mid = t + Vector3.Transform((mn + mx) * 0.5f, q);
        var near = world.Near(mid, key.L * 0.005f + 3f, _near);
        for (var k = -1; k < near.Count; k++)
        {
            var b = k < 0 ? c : near[k];
            if (k >= 0 && b == c || !b.HasSaw)
                continue;
            for (var i = 0; i < b.PartCount; i++)
            {
                if (b.Parts[i].D.Logic != CLogic.Saw || !ConstructRecycler.Powered(world, b, i))
                    continue;
                if (!Blade(b, i, origin, axis, out var along))
                    continue;
                // The cut lands on the length lattice, at or behind the blade.
                var keep = (int)MathF.Floor(along * 100 / Extrusion.StepCm + 0.001f) * Extrusion.StepCm;
                var off = key.L - keep;
                if (keep < Extrusion.StepCm || off < Extrusion.StepCm)
                    continue;
                ref var s = ref b.Parts[i];
                var every = ConstructConfig.Get(s, s.D, CfgKey.CutLength, 0);
                var signal = (s.State & SawPending) != 0;
                if (signal || every > 0 && off >= (int)MathF.Round(every * 100))
                {
                    s.State &= unchecked((byte)~SawPending);
                    return Cut(world, c, product, keep);
                }
            }
        }
        return false;
    }

    /// <summary>Does saw part <paramref name="saw"/>'s throat take a piece on this line? <paramref name="along"/>: how far along it the blade is.</summary>
    public static bool Blade(Construct b, int saw, Vector3 origin, Vector3 axis, out float along)
    {
        ref var s = ref b.Parts[saw];
        var d = s.D;
        b.WorldFrame(s.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(s.Rot);
        var blade = t + Vector3.Transform(s.Pos + Rot24.Apply(s.Rot, d.SawBlade), q);
        var normal = Vector3.Transform(Vector3.UnitZ, turn);
        along = Vector3.Dot(blade - origin, axis);
        if (MathF.Abs(Vector3.Dot(normal, axis)) < 0.995f)
            return false;
        var off = Vector3.Transform(origin + axis * along - blade, Quaternion.Conjugate(turn));
        return along > 0 && MathF.Abs(off.X) <= d.SawThroat.X * 0.5f && MathF.Abs(off.Y) <= d.SawThroat.Y * 0.5f;
    }

    /// <summary>What is past <paramref name="keep"/> cm comes off as a body of its own, moving as the piece moved; the rest stays on the die.</summary>
    private static bool Cut(ConstructWorld world, Construct c, int product, int keep)
    {
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        var key = was.ExKey;
        if (Extrusion.Def(key with { L = (ushort)(key.L - keep), Growing = false }) is not { } piece || Extrusion.Def(key with { L = (ushort)keep }) is not { } rest)
            return false;
        c.WorldFrame(pp.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(pp.Rot);
        var at = t + Vector3.Transform(pp.Pos, q) + Vector3.Transform(new Vector3(0, 0, keep / 100f), turn);
        var share = pp.Health / MathF.Max(1, was.Health);
        var centre = at + Vector3.Transform((piece.BoundsMin + piece.BoundsMax) * 0.5f, turn);
        if (ConstructEdits.DropLoose(world, piece, centre, turn) is not { } body)
            return false;
        body.Owner = c.Owner;
        body.Vel = c.PointVel(centre);
        body.AngVel = c.AngVel;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, piece.Health * share);
        np.Paint = pp.Paint;
        Resize(world, c, product, rest);
        ref var after = ref c.Parts[product];
        after.Health = MathF.Max(1, rest.Health * share);
        c.Wake();
        return true;
    }

    /// <summary>Why nothing can be welded to this part just now (a piece still on its die), or null.</summary>
    public static string? GrowingRefusal(Construct c, int part) =>
        (uint)part < (uint)c.PartCount && c.DefOf(part) is { IsExtrusion: true, ExKey.Growing: true } ? "that piece is still on its Extruder's die: cut it off first" : null;

    /// <summary>A piece's length on the look line's key: changes when it grows (the store's version doesn't).</summary>
    public static int LengthOf(Construct c, int part) => ProductOf(c, part) is var k and >= 0 && c.Parts[k].D.IsExtrusion ? c.Parts[k].D.ExKey.L : 0;

    /// <summary>What it is doing, or why not. Read by the look line on either side.</summary>
    public static string Status(Construct c, int part, CStore st)
    {
        if (c.Parts[part].Broken)
            return " - wrecked";
        if (st.Held)
            return Program.HeldLook;
        return (ExtrudeStall)st.Refused switch
        {
            ExtrudeStall.NoPower => " - no power: needs a charged battery",
            ExtrudeStall.NoMaterial => $" - needs material: takes {ExtrusionTable.Current.Takes()}",
            ExtrudeStall.BadMaterial => $" - can't extrude {RecyclingTable.Current.NameOf(st.Material)}: takes {ExtrusionTable.Current.Takes()}",
            ExtrudeStall.Mixed => $" - holds {RecyclingTable.Current.NameOf(st.Material)}, the piece on its die is another material: cut it or take it off",
            ExtrudeStall.BadShape => " - " + (Extrusion.Refusal(MaskOf(c.Parts[part], c.Parts[part].D)) ?? "too many different pieces in this game"),
            ExtrudeStall.AtCap => " - the piece is as long as it can be: cut it or take it off",
            ExtrudeStall.Blocked => " - blocked: something fixed in the piece's path (a saw's throat, a wall, the ground)",
            ExtrudeStall.TooHeavy => " - what is in the piece's path is too heavy to push",
            _ => st.Busy ? ", extruding" : ", waiting",
        };
    }

    /// <summary>The whole look line: its bin, its die, the section, the length so far, the material use, what it is doing, what F would do.</summary>
    public static string Look(Construct c, int part, CStore st, bool near, SandboxInventory inv)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var names = RecyclingTable.Current;
        Section(c, part, out var mask, out var w, out var h, out _);
        Die(c, part, out _, out var die);
        var product = ProductOf(c, part);
        var sb = new StringBuilder(d.Name).Append(": ").Append(st.Amount).Append('/').Append(d.BulkCapacity);
        if (st.Amount > 0)
            sb.Append(' ').Append(names.NameOf(st.Material));
        if (product >= 0 && c.Parts[product].D.IsExtrusion)
        {
            var k = c.Parts[product].D.ExKey;
            (mask, w, h) = (k.Mask, k.W, k.H);
            sb.Append($", {Extrusion.ProfileLabel(mask)} {w / 100f:0.##} x {h / 100f:0.##} m, {k.L / 100f:0.##}/{MathF.Min(d.ExtMaxLength, Extrusion.MaxLengthCm / 100f):0.#} m");
        }
        else
            sb.Append($", {Extrusion.ProfileLabel(mask)} {w / 100f:0.##} x {h / 100f:0.##} m");
        sb.Append(Status(c, part, st));
        if (ConstructConfig.Get(p, d, CfgKey.Width, 0) > die.X + 0.001f || ConstructConfig.Get(p, d, CfgKey.Height, 0) > die.Y + 0.001f)
            sb.Append($" - held to the {die.X:0.##} x {die.Y:0.##} m die: weld on Die Frames");
        if (near)
        {
            var tip = ConstructEdits.TipMaterial(inv, d, st);
            if (tip != 0)
                sb.Append($" - {SandboxKeys.Use}: tip in {Math.Min(inv.Materials[tip], d.BulkCapacity - st.Amount)} {names.NameOf(tip)}");
            else if (st.Amount > 0)
                sb.Append($" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}");
        }
        return sb.ToString();
    }
}

/// <summary>The Extruder's side of the world (ConstructExtruder.cs).</summary>
public sealed partial class ConstructWorld
{
    public int ExtrudeTicks; // server: ticks towards the next Extruder step

    /// <summary>Pieces whose length changed since the last network step: (body, part).</summary>
    public readonly List<(ushort Body, ushort Part)> ExtrudeDirty = new(32);

    /// <summary>A part's def changed size in place: the body's shape, without a rebuild, and the broadphase's sphere.</summary>
    public void ShapeChanged(Construct c)
    {
        c.RefreshShape();
        GridGrew(c);
    }
}

/// <summary>A part file's <c>storage.extrude</c> block (an Extruder).</summary>
public static class ConstructExtruderParts
{
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        const System.Text.Json.JsonValueKind Obj = System.Text.Json.JsonValueKind.Object;
        if (b.ValueKind != Obj || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner)
            throw new ConstructPartException($"{file}: storage \"extrude\" is an object (speed, die, at, maxSection, maxLength, pushForce) on a bin: what feeds it");
        ConstructParts.Known(b, ["speed", "die", "at", "maxSection", "maxLength", "pushForce"], $"{bw}.extrude");
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: an Extruder's bin takes \"{ConstructStorage.Any}\" material (Constructs/Extrusion.json says which it can use)");
        d.ExtSpeed = ConstructParts.Num(ConstructParts.Need(b, "speed", $"{bw}.extrude"), file, "storage.extrude.speed");
        ConstructParts.Range(d.ExtSpeed, 0.02f, 4, file, "storage.extrude.speed");
        d.ExtDie = Pair(ConstructParts.Need(b, "die", $"{bw}.extrude"), file, "storage.extrude.die");
        d.ExtDieAt = ConstructParts.Vec(ConstructParts.Need(b, "at", $"{bw}.extrude"), file, "storage.extrude.at");
        d.ExtMaxSection = Pair(ConstructParts.Need(b, "maxSection", $"{bw}.extrude"), file, "storage.extrude.maxSection");
        d.ExtMaxLength = ConstructParts.Num(ConstructParts.Need(b, "maxLength", $"{bw}.extrude"), file, "storage.extrude.maxLength");
        ConstructParts.Range(d.ExtMaxLength, 0.5f, Extrusion.MaxLengthCm / 100f, file, "storage.extrude.maxLength");
        d.ExtPush = ConstructParts.Num(ConstructParts.Need(b, "pushForce", $"{bw}.extrude"), file, "storage.extrude.pushForce");
        ConstructParts.Range(d.ExtPush, 0, 1000000, file, "storage.extrude.pushForce");
        if (d.ExtDie.X > d.ExtMaxSection.X || d.ExtDie.Y > d.ExtMaxSection.Y || d.ExtMaxSection.X * 100 > Extrusion.MaxSectionCm || d.ExtMaxSection.Y * 100 > Extrusion.MaxSectionCm)
            throw new ConstructPartException($"{file}: storage.extrude: \"die\" fits inside \"maxSection\", at most {Extrusion.MaxSectionCm / 100} m a side");
        d.ExtDieSocket = Array.FindIndex(d.Sockets, s => s.Kind == SocketKind.Die);
        if (d.ExtDieSocket < 0)
            throw new ConstructPartException($"{file}: an Extruder needs a socket of kind \"die\" (its output, facing +z)");
        d.IsExtruder = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    private static Vector2 Pair(System.Text.Json.JsonElement e, string file, string what)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new ConstructPartException($"{file}: \"{what}\" is [width, height]");
        var v = new Vector2(ConstructParts.Num(e[0], file, what), ConstructParts.Num(e[1], file, what));
        ConstructParts.Range(v.X, 0.1f, 8, file, what);
        ConstructParts.Range(v.Y, 0.1f, 8, file, what);
        return v;
    }
}
