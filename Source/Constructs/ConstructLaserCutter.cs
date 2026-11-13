using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The Laser Cutter (docs/sandbox/constructs/production.md "The Laser Cutter"; design docs/sandbox/design/laser-cutter.md section 5): a machine of
/// the Panel Former's family that cuts a formed panel or a flat extruded piece (its blank) to a cut key (HullLoft.Cuts.cs), by work order only.
/// It has no stock of its own: the head order's blank comes in by conveyor into Piece in (anything else waits on the conveyor) and lies on its
/// bed (<see cref="CStore.JobAmount"/> 1, its material in <see cref="CStore.JobMaterial"/>). The cut is a timed stroke, longer through thicker
/// stock and shorter on a stronger machine (<see cref="Seconds"/>); done, the cut part rides out of Piece out, or drops on the out bed. What the
/// cut takes away comes back as offcut: counted in tenths of a unit (<see cref="CStore.InAmount"/>, of <see cref="CStore.InMaterial"/>), whole
/// units handed into its bin (the store's own bulk) as they add up; a chute empties the bin. A full bin, or one of another material, stalls it.
/// </summary>
public static class ConstructLaserCutter
{
    /// <summary>The energy a metre of cut takes per mm of thickness, kJ (the design's estimate: 3 s a metre through 20 mm on the 2.4 kW cutter).</summary>
    public const float KjPerMetreMm = 0.36f;

    /// <summary>A cut key's job, read once (HullLoft.CutJob) and kept: what the step, the conveyor and the screens read with no allocation.</summary>
    public sealed class Job
    {
        public string Blank = "";
        public bool Plate;
        public ExtrusionKey Stock;
        public float EdgeM, Mm;
        public byte Material;
        public int OffTenths; // the offcut, tenths of a unit of Material (the blank's mass less the cut part's)
        public float Longest, Next, Least; // the blank's bounds, sorted (m)
    }

    private static readonly Dictionary<string, Job?> Jobs = new(StringComparer.Ordinal);

    /// <summary>A cut key's job, or null: not a cut key, refused, or its blank is no part. Allocates the first time a key is seen.</summary>
    public static Job? JobOf(string name)
    {
        if (Jobs.TryGetValue(name, out var j))
            return j;
        if (Jobs.Count >= 512)
            Jobs.Clear(); // (a bound: a key is read again when next asked)
        j = null;
        if (HullLoft.CutJob(name, out var blank, out var plate, out var stock, out var edge, out var mm)
            && ConstructParts.Find(blank) is { } bd && ConstructParts.Find(name) is { } cd)
        {
            // (Find finishes a stand-in asked for by name, HullLoft.Parse; should one still come back, its mass and bounds are a box's: never kept)
            if (HullLoft.IsStandIn(bd) || HullLoft.IsStandIn(cd))
                return null;
            var mat = HullLoft.FormedMaterial(name);
            var s = bd.BoundsMax - bd.BoundsMin;
            var longest = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
            var least = MathF.Min(s.X, MathF.Min(s.Y, s.Z));
            j = new Job
            {
                Blank = blank, Plate = plate, Stock = stock, EdgeM = edge, Mm = mm, Material = mat,
                OffTenths = (int)MathF.Round(MathF.Max(0, bd.Mass - cd.Mass) / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(mat)) * 10),
                Longest = longest, Least = least, Next = s.X + s.Y + s.Z - longest - least,
            };
        }
        Jobs[name] = j;
        return j;
    }

    /// <summary>Seconds a cut takes on a cutter: 2 s to set up, then the energy of the cut (edge by thickness) over the machine's power.</summary>
    public static float Seconds(Job j, ConstructPartDef cutter) => 2 + KjPerMetreMm * j.EdgeM * j.Mm / MathF.Max(0.1f, cutter.CutKw);

    /// <summary>Does a cutter's bed take this job's blank? Its longest side within the bed's length, the next within its width, the least within its height.</summary>
    public static bool Fits(ConstructPartDef cutter, Job j) =>
        j.Longest <= cutter.ExtWheelMax.Y + 0.005f && j.Next <= cutter.ExtWheelMax.X + 0.005f && j.Least <= cutter.CutMaxHeight + 0.005f;

    /// <summary>The smallest Laser Cutter whose bed takes a job's blank, by name, or null.</summary>
    public static string? TierFor(Job j)
    {
        ConstructPartDef? best = null;
        foreach (var d in ConstructParts.All)
            if (d.IsLaserCutter && Fits(d, j) && (best == null || d.ExtWheelMax.Y < best.ExtWheelMax.Y))
                best = d;
        return best?.Name;
    }

    /// <summary>Server: will this cutter take an order? Null, or why not.</summary>
    public static string? Refusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (o.Name == null)
            return $"not a cut part: the {d.Name} cuts panels and plates, {(o.IsPiece ? $"a {Extrusion.Label(o.Key)} is {WorkOrders.Maker(o.Key)}'s order" : $"{ConstructParts.Def(o.Part).Name} is an Assembler's order")}";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount} parts";
        if (!HullLoft.IsCutName(o.Name))
            return HullLoft.IsFormedName(o.Name) ? "nothing to cut: a Panel Former's order" : $"not a cut part: the {d.Name} cuts nothing called \"{o.Name}\"";
        if (JobOf(o.Name) is not { } j)
            return HullLoft.CutRefusal(o.Name) is { Length: > 0 } why ? why : "its blank is no part this build makes";
        if (!Fits(d, j))
            return j.Least > d.CutMaxHeight + 0.005f
                ? $"too tall for the bed: {j.Least:0.##} m, the {d.Name} takes {d.CutMaxHeight:0.##} m"
                : $"too large for this cutter: {j.Longest:0.##} x {j.Next:0.##} m, the {d.Name} takes {d.ExtWheelMax.Y:0.##} x {d.ExtWheelMax.X:0.##} m";
        if (c.Parts[part].Orders is { Count: >= WorkOrders.MaxOrders })
            return $"the {d.Name}'s queue is full ({WorkOrders.MaxOrders} orders)";
        return null;
    }

    /// <summary>A step of one cutter (10 Hz, server; from ConstructExtruder.Step). It allocates nothing but the rider or the body a cut part is.</summary>
    internal static void Work(ConstructWorld world, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (st.OnBed != st.JobAmount > 0)
        {
            st.OnBed = st.JobAmount > 0; // (after a load: the saved job says what lies on the bed; the wire carries OnBed)
            st.Version++;
            world.StoreDirty.Add(st.Handle);
        }
        var held = ConstructExtruder.RunHeld(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            Idle(world, st, ExtrudeStall.None);
            return;
        }
        if (!WorkOrders.HeadOf(p, out var head))
        {
            Idle(world, st, ExtrudeStall.NoOrders); // (a blank on the bed stays there: F takes it back)
            return;
        }
        if (head.Name == null || JobOf(head.Name) is not { } j || ConstructParts.Find(head.Name) is not { } made)
        {
            Idle(world, st, ExtrudeStall.BadShape);
            return;
        }
        if (st.JobAmount == 0)
        {
            Idle(world, st, ExtrudeStall.NoMaterial); // (waiting for its blank: Lacks says which)
            return;
        }
        if (!ConstructRecycler.Powered(world, c, part))
        {
            Idle(world, st, ExtrudeStall.NoPower); // (the blank stays on the bed, the stroke where it stopped)
            return;
        }
        var need = (int)(Seconds(j, d) * World.TickRate);
        if (st.JobTicks < need)
        {
            st.JobTicks += ConstructExtruder.Every;
            var pct = (byte)Math.Clamp(st.JobTicks * 100L / Math.Max(1, need), 0, 100);
            if (pct / 10 != st.Progress / 10)
            {
                st.Progress = pct; // (the screen's bar, sent in tenths)
                st.Version++;
                world.StoreDirty.Add(st.Handle);
            }
            ConstructExtruder.SetStall(world, st, ExtrudeStall.None);
            ConstructGatherer.SetBusy(world, st, true);
            return;
        }
        // Cut. The offcut first: its whole units must go into the bin, or the cut part waits on the bed.
        var oldMat = st.InMaterial;
        var oldUnit = oldMat != 0 && oldMat != j.Material && st.InAmount >= 5 ? 1 : 0; // (tenths of another material round to the nearest unit)
        var tenths = (oldMat == j.Material ? st.InAmount : 0) + j.OffTenths;
        var units = tenths / 10;
        if (!BinTakes(d, st.Amount, st.Material, oldMat, oldUnit)
            || !BinTakes(d, st.Amount + oldUnit, oldUnit > 0 ? oldMat : st.Material, j.Material, units))
        {
            Idle(world, st, ExtrudeStall.OffcutFull);
            return;
        }
        if (ConstructConveyor.Out(c, part) is var conveyor and >= 0)
        {
            if (!ConstructConveyor.PutMade(world, c, conveyor, made, 1, out var stall))
            {
                Idle(world, st, stall);
                return;
            }
        }
        else
        {
            c.WorldFrame(p.Seg, out var q, out var t);
            var turn = q * Rot24.Quat(p.Rot);
            var size = made.BoundsMax - made.BoundsMin;
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(d.ExtDieAt + new Vector3(0, size.Y * 0.5f + 0.05f, 0), turn); // (its middle over the out bed)
            if (ConstructEdits.DropLoose(world, made, at, turn) is not { } body)
            {
                Idle(world, st, ExtrudeStall.Blocked);
                return;
            }
            body.Owner = c.Owner;
            body.Vel = c.PointVel(at);
            ConstructCrafting.MadeLoose(world, c, part, made);
        }
        if (oldUnit > 0)
            world.Deposit(c, part, 1, oldMat);
        if (units > 0)
            world.Deposit(c, part, units, j.Material);
        st.InAmount = tenths % 10;
        st.InMaterial = st.InAmount > 0 ? j.Material : (byte)0;
        st.JobAmount = 0;
        st.OnBed = false;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        WorkOrders.Made(world, c, p.Orders);
        ProductionLines.Made(world, c, part, 1);
        ConstructExtruder.SetStall(world, st, ExtrudeStall.None);
        ConstructGatherer.SetBusy(world, st, false);
    }

    /// <summary>Would the offcut bin, holding <paramref name="amount"/> of <paramref name="held"/>, take <paramref name="n"/> units of <paramref name="mat"/>?</summary>
    private static bool BinTakes(ConstructPartDef d, int amount, byte held, byte mat, int n) =>
        n == 0 || (amount == 0 || held == mat) && amount + n <= d.BulkCapacity;

    private static void Idle(ConstructWorld world, CStore st, ExtrudeStall why)
    {
        ConstructGatherer.SetBusy(world, st, false);
        ConstructExtruder.SetStall(world, st, why);
    }

    /// <summary>
    /// Server: the head order is going (Cancel, Clear): a blank on the bed, uncut, drops onto the out bed as a loose part, so it is never cut to
    /// the next order. Where nothing has room for it, it is lost with its order.
    /// </summary>
    public static void GiveBack(ConstructWorld world, Construct c, int part)
    {
        if (world.StoreOf(c, part) is not { JobAmount: > 0 } st)
            return;
        if (WorkOrders.HeadOf(c.Parts[part], out var head) && head.Name != null && JobOf(head.Name) is { } j && ConstructParts.Find(j.Blank) is { } blank)
        {
            ref var p = ref c.Parts[part];
            c.WorldFrame(p.Seg, out var q, out var t);
            var turn = q * Rot24.Quat(p.Rot);
            var size = blank.BoundsMax - blank.BoundsMin;
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(p.D.ExtDieAt + new Vector3(0, size.Y * 0.5f + 0.05f, 0), turn);
            if (ConstructEdits.DropLoose(world, blank, at, turn) is { } body)
            {
                body.Owner = c.Owner;
                body.Vel = c.PointVel(at);
            }
        }
        st.JobAmount = 0;
        st.OnBed = false;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        ConstructGatherer.SetBusy(world, st, false);
    }

    /// <summary>The tag a blank routed to a cutter rides with (a Conveyor Router's want, ConstructLogistics.WantAt); no chain takes this id.</summary>
    public const ushort BedTag = 0xFFFF;

    /// <summary>Server: does this cutter want this piece now (its bed empty, the piece its head order's blank)? A Router sends it one then.</summary>
    public static bool Wants(ConstructWorld world, Construct c, int part, ConstructPartDef piece) =>
        world.StoreOf(c, part) is { JobAmount: 0 } && WorkOrders.HeadOf(c.Parts[part], out var head) && head.Name != null
        && JobOf(head.Name) is { } j && IsBlank(j, piece);

    /// <summary>Is this piece the head order's blank? (A formed panel by its name, an extruded piece by its key: nothing allocates.)</summary>
    public static bool IsBlank(Job j, ConstructPartDef piece) =>
        j.Plate ? piece.IsExtrusion && piece.ExKey == j.Stock : !piece.IsExtrusion && string.Equals(piece.Name, j.Blank, StringComparison.Ordinal);

    /// <summary>
    /// Server: a piece at the end of a conveyor into a cutter's Piece in. Only the head order's blank is taken, and only onto an empty bed;
    /// anything else waits on the conveyor.
    /// </summary>
    public static bool TakeFromConveyor(ConstructWorld world, Construct c, int part, ConstructPartDef piece)
    {
        if (world.StoreOf(c, part) is not { JobAmount: 0 } st || !WorkOrders.HeadOf(c.Parts[part], out var head) || head.Name == null
            || JobOf(head.Name) is not { } j || !IsBlank(j, piece))
            return false;
        st.JobAmount = 1;
        st.OnBed = true;
        st.JobMaterial = j.Material;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        return true;
    }

    /// <summary>What its look line and screen say it lacks: "needs Formed Panel (...) on its bed".</summary>
    public static string Lacks(Construct c, int part, CStore st) =>
        WorkOrders.HeadOf(c.Parts[part], out var head) && head.Name != null && JobOf(head.Name) is { } j ? $"needs {j.Blank} on its bed" : "needs its blank";

    /// <summary>What it is doing, or why not (the look line's end).</summary>
    public static string Status(Construct c, int part, CStore st) => (ExtrudeStall)st.Refused switch
    {
        ExtrudeStall.NoPower => " - no power: needs a charged battery",
        ExtrudeStall.NoMaterial => " - " + Lacks(c, part, st),
        ExtrudeStall.BadShape => " - not a cut part: nothing cuts its order (Cancel it)",
        ExtrudeStall.OffcutFull => $" - offcut bin full{(st.Amount > 0 ? $" of {RecyclingTable.Current.NameOf(st.Material)}" : "")}: empty it by chute from its Offcut port",
        ExtrudeStall.Blocked => " - blocked: no room on its out bed",
        ExtrudeStall.ConveyorSmall => $" - conveyor too narrow for the cut part - aim the linking tool at it: wheel or {SandboxKeys.Reload} to widen",
        ExtrudeStall.ConveyorFull => " - blocked: its conveyor is full",
        ExtrudeStall.ConveyorCut => " - blocked: its conveyor is cut: mend it",
        ExtrudeStall.NoOrders => ", waiting for orders (a cut part's order: a designer, a terminal or a bill)",
        _ => st.Busy ? ", cutting" : ", waiting",
    };

    /// <summary>The look line's middle (its status follows): the offcut in its bin and the tenths counted, the head order, the blank on the bed and the stroke.</summary>
    public static void Look(Construct c, int part, CStore st, StringBuilder sb)
    {
        var names = RecyclingTable.Current;
        sb.Append(" offcut");
        if (st.InAmount > 0)
            sb.Append(" (+0.").Append(st.InAmount).Append(' ').Append(names.NameOf(st.InMaterial)).Append(')');
        if (WorkOrders.HeadOf(c.Parts[part], out var h) && h.Name != null)
        {
            sb.Append(", ").Append(h.Name);
            if (st.OnBed)
                sb.Append(", blank on the bed ").Append(st.Progress).Append('%');
        }
    }

    /// <summary>A part file's <c>storage.cut</c> block: <c>{kw, at, maxWidth, maxLength, maxHeight}</c> on a bin (its offcut).</summary>
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        if (b.ValueKind != System.Text.Json.JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"cut\" is an object (kw, at, maxWidth, maxLength, maxHeight) on a bin: its offcut");
        ConstructParts.Known(b, ["kw", "at", "maxWidth", "maxLength", "maxHeight"], $"{bw}.cut");
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: a Laser Cutter's offcut bin takes \"{ConstructStorage.Any}\" material");
        d.CutKw = ConstructParts.Num(ConstructParts.Need(b, "kw", $"{bw}.cut"), file, "storage.cut.kw");
        ConstructParts.Range(d.CutKw, 0.1f, 1000, file, "storage.cut.kw");
        d.ExtSpeed = 1;
        d.ExtDieAt = ConstructParts.Vec(ConstructParts.Need(b, "at", $"{bw}.cut"), file, "storage.cut.at");
        var w = ConstructParts.Num(ConstructParts.Need(b, "maxWidth", $"{bw}.cut"), file, "storage.cut.maxWidth");
        var l = ConstructParts.Num(ConstructParts.Need(b, "maxLength", $"{bw}.cut"), file, "storage.cut.maxLength");
        d.CutMaxHeight = ConstructParts.Num(ConstructParts.Need(b, "maxHeight", $"{bw}.cut"), file, "storage.cut.maxHeight");
        ConstructParts.Range(w, 0.2f, 32, file, "storage.cut.maxWidth");
        ConstructParts.Range(l, w, 32, file, "storage.cut.maxLength");
        ConstructParts.Range(d.CutMaxHeight, 0.05f, 16, file, "storage.cut.maxHeight");
        d.ExtWheelMax = new Vector2(w, l);
        d.ExtWheelStart = new Vector2(0.2f, 0.2f);
        d.ExtDieSocket = -1;
        d.IsExtruder = d.IsWheelFormer = d.IsPanelFormer = d.IsLaserCutter = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }
}
