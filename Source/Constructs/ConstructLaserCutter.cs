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
public static partial class ConstructLaserCutter
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
        public Vector2[]? Path; // the head's path over the blank (m: along, across), read once for the look (ConstructLaserCutterBed.cs)
        public readonly HeadPlan?[] Plans = new HeadPlan?[4]; // ...and the heads' plans over it, one per size of bed it lies on (ConstructLaserCutterBed.cs)
        public int NextPlan;
        public ConstructPartDef? BlankDef; // ...and the blank drawn on its bed, as it lies there (a turn for each way the bed runs)
        public byte BlankRotAlong = 255, BlankRotAcross = 255;
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

    /// <summary>Seconds a cut takes with <paramref name="heads"/> heads of <paramref name="cutter"/>'s: 2 s to set up, then the energy of the cut (edge by thickness) over their power.</summary>
    public static float Seconds(Job j, ConstructPartDef cutter, int heads) => 2 + KjPerMetreMm * j.EdgeM * j.Mm / MathF.Max(0.1f, cutter.CutKw * Math.Max(1, heads));

    /// <summary>Seconds a cut takes on this cutter: its bridge's heads, one per row of Bed Blocks across (ConstructLaserCutterBed.cs).</summary>
    public static float Seconds(Job j, Construct c, int part) => Seconds(j, c.Parts[part].D, Heads(c, part));

    /// <summary>Does a bed <paramref name="bed"/> (m, either way round) take this job's blank? Its longest side within the bed's longer side, the next within its shorter, the least within the bridge's clearance.</summary>
    public static bool Fits(ConstructPartDef cutter, Vector2 bed, Job j) =>
        j.Longest <= MathF.Max(bed.X, bed.Y) + 0.005f && j.Next <= MathF.Min(bed.X, bed.Y) + 0.005f && j.Least <= cutter.CutMaxHeight + 0.005f;

    /// <summary>Does this cutter's bed, as its Bed Blocks lie now, take this job's blank?</summary>
    public static bool Fits(Construct c, int part, Job j) => Fits(c.Parts[part].D, BedOf(c, part), j);

    /// <summary>Why this cutter's bed doesn't take this job's blank ("needs a 4 x 3 bed, this one is 3 x 2"), or null when it does.</summary>
    public static string? Misfit(Construct c, int part, Job j)
    {
        var d = c.Parts[part].D;
        if (j.Least > d.CutMaxHeight + 0.005f)
            return $"too tall for the bed: {j.Least:0.##} m, the bridge clears {d.CutMaxHeight:0.##} m";
        if (Fits(c, part, j))
            return null;
        var (w, l) = BedNeeded(j, d.BlockCell);
        var has = MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0 ? $"this one is {Math.Max(asm.W, asm.L)} x {Math.Min(asm.W, asm.L)}" : "this one has no bed: weld Laser Bed Blocks beside it";
        return l > MachineBlocks.MaxSide || w * l > MachineBlocks.MaxCells ? $"too large for any bed: {j.Longest:0.##} x {j.Next:0.##} m (a bed is at most {MachineBlocks.MaxSide} blocks a side and {MachineBlocks.MaxCells} in all)"
            : $"needs a {l} x {w} bed ({l * d.BlockCell:0.#} x {w * d.BlockCell:0.#} m), {has}";
    }

    /// <summary>The fewest Bed Blocks along and across that take a job's blank (cells of <paramref name="cell"/> m).</summary>
    public static (int Across, int Along) BedNeeded(Job j, float cell) =>
        (Math.Max(1, (int)MathF.Ceiling(j.Next / cell - 0.0025f)), Math.Max(1, (int)MathF.Ceiling(j.Longest / cell - 0.0025f)));

    /// <summary>What a bill or a planner says of the bed a job needs: "a 4 x 3 bed".</summary>
    public static string BedText(Job j)
    {
        var cell = MachineBlocks.DefaultCell;
        var (w, l) = BedNeeded(j, cell);
        return j.Least > MaxHeight + 0.005f ? $"no bed: {j.Least:0.##} m tall, a bridge clears {MaxHeight:0.##} m"
            : l > MachineBlocks.MaxSide || w * l > MachineBlocks.MaxCells ? $"no bed: {j.Longest:0.##} x {j.Next:0.##} m is past any bed's {MachineBlocks.MaxSide} blocks a side and {MachineBlocks.MaxCells} in all" : $"a {l} x {w} bed";
    }

    /// <summary>The bridge's clearance over every Laser Cutter's bed (m), from the catalogue's cutter.</summary>
    private static float MaxHeight
    {
        get
        {
            foreach (var d in ConstructParts.All)
                if (d.IsLaserCutter)
                    return d.CutMaxHeight;
            return 2;
        }
    }

    /// <summary>Server: will this cutter take an order? Null, or why not.</summary>
    public static string? Refusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (o.Name == null)
            return $"not a cut part: the {d.Name} cuts panels and plates, {(o.IsPiece ? $"a {Extrusion.Label(o.Key)} is {WorkOrders.Maker(o.Key)}'s order" : $"{ConstructParts.Def(o.Part).Name} is {WorkOrders.CatalogueMaker(o)}'s order")}";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount} parts";
        if (!HullLoft.IsCutName(o.Name))
            return HullLoft.IsFormedName(o.Name) ? "nothing to cut: a Panel Former's order" : $"not a cut part: the {d.Name} cuts nothing called \"{o.Name}\"";
        if (JobOf(o.Name) is not { } j)
            return HullLoft.CutRefusal(o.Name) is { Length: > 0 } why ? why : "its blank is no part this build makes";
        if (Misfit(c, part, j) is { } misfit)
            return misfit;
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
        var need = (int)(Seconds(j, c, part) * World.TickRate);
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
        var bin = BinOf(c, part);
        if (!BinTakes(bin, st.Amount, st.Material, oldMat, oldUnit)
            || !BinTakes(bin, st.Amount + oldUnit, oldUnit > 0 ? oldMat : st.Material, j.Material, units))
        {
            Idle(world, st, ExtrudeStall.OffcutFull);
            return;
        }
        if (OutConveyor(c, part) is var conveyor and >= 0)
        {
            if (!ConstructConveyor.PutMade(world, c, conveyor, made, 1, out var stall))
            {
                Idle(world, st, stall);
                return;
            }
        }
        else if (ConstructConveyor.OutPad(c, part) is >= 0 and var pad)
        {
            // Onto the pick pad its Out Table butts (Zero Day Works II's big lines, 2026-11-26): it waits on the bed while the pad is full.
            if (ConstructStation.PadEarly(world, c, pad, made) || !world.RackPut(c, pad, made, true))
            {
                Idle(world, st, ExtrudeStall.ConveyorFull);
                return;
            }
            ConstructLifters.PadArrived(world, c, pad, 0, made); // (it counts for the station order that waits for it there)
        }
        else
        {
            c.WorldFrame(p.Seg, out var q, out var t);
            var turn = q * Rot24.Quat(p.Rot);
            var size = made.BoundsMax - made.BoundsMin;
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(OutAt(c, part, size.Y), turn); // (its middle over the Out Table, or over the bed)
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
    private static bool BinTakes(int bin, int amount, byte held, byte mat, int n) =>
        n == 0 || (amount == 0 || held == mat) && amount + n <= bin;

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
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(OutAt(c, part, size.Y), turn);
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

    /// <summary>
    /// Server: an order queued at the cutter itself (its Design picker) asks a linked machine for its blanks: a formed panel of a Panel Former, a
    /// plate or flat bar of an Extruder, as a machine order (the blank rides plainly and the cutter takes it for that order). The maker, or null.
    /// </summary>
    public static string? AskBlanks(ConstructWorld world, Construct c, int part, string key, int count)
    {
        if (JobOf(key) is not { } j)
            return null;
        var order = j.Plate ? new WorkOrder { Key = j.Stock with { Growing = false }, Part = ConstructParts.None, Count = (ushort)count }
            : ConstructPanelFormer.Order(j.Blank, count);
        var net = ConstructCrafting.NetOf(world, c, part);
        for (var i = 0; i < net.MachineN; i++)
        {
            var m = net.Machines[i];
            if (m != part && !c.DefOf(m).IsLaserCutter && WorkOrders.Queue(world, c, m, order) == null)
                return c.DefOf(m).Name;
        }
        return null;
    }

    /// <summary>The tag a blank routed to a cutter rides with (a Conveyor Router's want, ConstructLogistics.WantAt); no chain takes this id.</summary>
    public const ushort BedTag = 0xFFFF;

    /// <summary>Server: does this cutter want this piece now (its bed empty, the piece its head order's blank)? A Router sends it one then.</summary>
    public static bool Wants(ConstructWorld world, Construct c, int part, ConstructPartDef piece) =>
        world.StoreOf(c, part) is { JobAmount: 0 } && OrderFor(c, part, piece) >= 0;

    /// <summary>
    /// The first order in the cutter's queue whose blank this piece is, or -1. Not only the head's: orders come from a designer, a terminal and
    /// the Design picker, each with its own blank on the way, and the blank that arrives first is cut first (none waits behind another's).
    /// </summary>
    private static int OrderFor(Construct c, int part, ConstructPartDef piece)
    {
        if (c.Parts[part].Orders is not { Bill: false, Count: > 0 } q)
            return -1;
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Left > 0 && q.Items[k].Name is { } n && JobOf(n) is { } j && IsBlank(j, piece))
                return k;
        return -1;
    }

    /// <summary>Is this piece the head order's blank? (A formed panel by its name, an extruded piece by its key: nothing allocates.)</summary>
    public static bool IsBlank(Job j, ConstructPartDef piece) =>
        j.Plate ? piece.IsExtrusion && piece.ExKey == j.Stock : !piece.IsExtrusion && string.Equals(piece.Name, j.Blank, StringComparison.Ordinal);

    /// <summary>
    /// Server: a piece at the end of a conveyor into a cutter's Piece in. Only the head order's blank is taken, and only onto an empty bed;
    /// anything else waits on the conveyor.
    /// </summary>
    /// <summary>Does a queued order take this blank (a run feeding the bed asks: ConstructConveyor.PastBed)?</summary>
    internal static bool Takes(Construct c, int part, ConstructPartDef piece) => OrderFor(c, part, piece) >= 0;

    public static bool TakeFromConveyor(ConstructWorld world, Construct c, int part, ConstructPartDef piece)
    {
        if (world.StoreOf(c, part) is not { JobAmount: 0 } st || OrderFor(c, part, piece) is not (var at and >= 0))
            return false;
        if (at > 0)
        {
            // Its order goes to the head (the bed is empty: nothing is under way), so the blank is cut to the order it was made for.
            var q = c.Parts[part].Orders!;
            var mine = q.Items[at];
            Array.Copy(q.Items, 0, q.Items, 1, at);
            q.Items[0] = mine;
            world.OrdersChanged(c);
        }
        var j = JobOf(c.Parts[part].Orders!.Items[0].Name!)!;
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
        WorkOrders.HeadOf(c.Parts[part], out var head) && head.Name != null && JobOf(head.Name) is { } j ? $"needs {j.Blank} on its bed{(c.RoutesInto(part, RouteType.Conveyor, 255) > 0 ? "" : ": only a conveyor into its Piece in brings it (a hand can't load the bed)")}" : "needs its blank"; // (run 7: a player with the blank in hand had no word that it can't be put on)

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
        sb.Append(" offcut, ");
        if (MachineBlocks.TryOf(c, part, out var asm))
            sb.Append(MachineBlocks.Describe(c, asm));
        else
            sb.Append("no bed: weld Laser Bed Blocks beside it");
        var heads = Heads(c, part);
        if (heads > 0)
            sb.Append(", ").Append(heads).Append(heads == 1 ? " head" : " heads");
        if (st.InAmount > 0)
            sb.Append(" (+0.").Append(st.InAmount).Append(' ').Append(names.NameOf(st.InMaterial)).Append(')');
        if (WorkOrders.HeadOf(c.Parts[part], out var h) && h.Name != null)
        {
            sb.Append(", ").Append(h.Name);
            if (st.OnBed)
                sb.Append(", blank on the bed ").Append(st.Progress).Append('%');
        }
    }

    /// <summary>
    /// A part file's <c>storage.cut</c> block: <c>{kw, at, maxWidth, maxLength, maxHeight, bin, binBlock}</c> on a bin (its offcut). On a Laser
    /// Cutter Core: kw is one head's, the bed's size comes from its Bed Blocks (maxWidth and maxLength cap it), maxHeight is the clearance under
    /// the bridge, and the offcut bin holds bin plus binBlock for each block in the bed (the bulk's capacity is its most).
    /// </summary>
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        if (b.ValueKind != System.Text.Json.JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"cut\" is an object (kw, at, maxWidth, maxLength, maxHeight) on a bin: its offcut");
        ConstructParts.Known(b, ["kw", "at", "maxWidth", "maxLength", "maxHeight", "bin", "binBlock"], $"{bw}.cut");
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
        ConstructParts.Range(l, w, HullLoft.CutPlateMaxCm / 100f, file, "storage.cut.maxLength"); // (the largest bed: MachineBlocks.MaxSide blocks of 2 m)
        ConstructParts.Range(d.CutMaxHeight, 0.05f, 16, file, "storage.cut.maxHeight");
        d.ExtWheelMax = new Vector2(w, l);
        d.CutBin = ConstructParts.Opt(b, "bin") is { } bin ? (int)ConstructParts.Num(bin, file, "storage.cut.bin") : d.BulkCapacity;
        d.CutBinBlock = ConstructParts.Opt(b, "binBlock") is { } bb ? (int)ConstructParts.Num(bb, file, "storage.cut.binBlock") : 0;
        if (d.CutBin < 1 || d.CutBinBlock < 0 || d.CutBin + d.CutBinBlock * MachineBlocks.MaxCells > d.BulkCapacity)
            throw new ConstructPartException($"{file}: storage.cut's bin ({d.CutBin}) and binBlock ({d.CutBinBlock}) a block for {MachineBlocks.MaxCells} blocks fit in its bulk's capacity ({d.BulkCapacity})");
        d.ExtWheelStart = new Vector2(0.2f, 0.2f);
        d.ExtDieSocket = -1;
        d.IsExtruder = d.IsWheelFormer = d.IsPanelFormer = d.IsLaserCutter = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }
}
