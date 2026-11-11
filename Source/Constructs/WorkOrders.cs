using System.Text;

namespace CallOfSlop;

/// <summary>
/// One work order (docs/sandbox/constructs/production.md "Work orders"): a piece by its whole key (shape, section, size, length,
/// material; <see cref="Part"/> None), or a catalogue part (<see cref="Part"/> its id, the key empty), and how many. Nothing makes
/// catalogue parts yet: the Assembler takes them in a later phase.
/// </summary>
public struct WorkOrder
{
    public ExtrusionKey Key; // a piece: Growing false; a wheel's L is 0
    public ushort Part; // a catalogue part's id (a file part's, never a run-time one), or ConstructParts.None for a piece
    public ushort Count, Made;

    public readonly bool IsPiece => Part == ConstructParts.None;
    public readonly int Left => Math.Max(0, Count - Made);
}

/// <summary>
/// A machine's queue or a Line Console's bill: at most <see cref="WorkOrders.MaxOrders"/> orders, oldest first. Held on the part
/// (<see cref="CPart.Orders"/>), so it moves with the part through renumbering, splits and cuts. The server holds every order; a
/// client holds the head (up to <see cref="WorkOrders.Shown"/>) and the totals, which is all a screen shows.
/// </summary>
public sealed class OrderList
{
    public WorkOrder[] Items = new WorkOrder[4]; // grows only on a player's action, never in a step
    public int Count; // orders held here (a client: the head it was sent)
    public int Total; // orders in the list (a client: as the server said; the server: Count)
    public int PiecesLeft; // pieces still to make, every order (a client: as the server said)
    public bool Bill; // a Line Console's bill rather than a machine's queue
    public string Note = ""; // a bill: why its first line stayed (no machine took it), or what the last Send did (server; on the wire)

    public ref WorkOrder Head => ref Items[0];

    public void Add(in WorkOrder o)
    {
        if (Count == Items.Length)
            Array.Resize(ref Items, Math.Min(Items.Length * 2, WorkOrders.MaxOrders));
        Items[Count++] = o;
        Recount();
    }

    public void RemoveAt(int k)
    {
        Array.Copy(Items, k + 1, Items, k, Count - k - 1);
        Count--;
        Recount();
    }

    public void Clear()
    {
        Count = 0;
        Recount();
    }

    public void Recount()
    {
        Total = Count;
        var left = 0;
        for (var k = 0; k < Count; k++)
            left += Items[k].Left;
        PiecesLeft = left;
    }
}

/// <summary>
/// Work orders (docs/sandbox/constructs/production.md "Work orders"): an Extruder or a Wheel Former works a queue of orders, each
/// setting its section, size and length and how many to make; a Line Console holds a bill and deals it to the machines its wires
/// reach. A machine with a queue (even an empty one) works its orders and nothing else; Clear drops the queue and it is back on
/// its own settings. A different material is a different line: a machine takes an order only in the material its stock holds.
/// </summary>
public static class WorkOrders
{
    public const int MaxOrders = 255; // a queue's or a bill's orders
    public const int MaxCount = 999; // pieces an order asks for
    public const int Shown = 3; // orders a client is sent (the screen's head)
    private const int OrderBytes = 22; // one order on the wire

    // ---- Accepting --------------------------------------------------------------------------------------------------------

    /// <summary>A machine that can take orders: an Extruder or a Wheel Former (Fan and Airframe Formers wait for a later phase).</summary>
    public static bool TakesOrders(ConstructPartDef d) => d.IsExtruder && !d.IsFanFormer && !d.IsAirframeFormer;

    /// <summary>Server: will this machine take the order? Null, or why not (one reason; a refused order is never queued).</summary>
    public static string? Refusal(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        var d = c.DefOf(part);
        if (!TakesOrders(d))
            return d.IsFanFormer || d.IsAirframeFormer ? $"the {d.Name} takes no orders yet" : $"the {d.Name} takes no orders";
        if (!o.IsPiece)
            return $"the {d.Name} makes pieces: {ConstructParts.Def(o.Part).Name} is an Assembler's order";
        if (o.Count is < 1 or > MaxCount)
            return $"an order is for 1 to {MaxCount} pieces";
        if (c.Parts[part].Orders is { Count: >= MaxOrders })
            return $"the {d.Name}'s queue is full ({MaxOrders} orders)";
        var k = o.Key;
        var names = RecyclingTable.Current;
        var mat = names.NameOf(k.Material);
        if (d.IsWheelFormer)
        {
            if (k.Shape != ExShape.Wheel)
                return $"the {d.Name} forms wheels: a {Extrusion.Label(k)} is an Extruder's order";
            if (ExtrusionTable.Current.FindWheel(k.Material) == null)
                return $"the {d.Name} can't form {mat}: takes {ExtrusionTable.Current.TakesWheels()}";
            if (k.W > MathF.Round(d.ExtWheelMax.X * 100) || k.H > MathF.Round(d.ExtWheelMax.Y * 100))
                return $"a wheel {k.W / 100f:0.##} m wide, {k.H / 100f:0.##} m across is beyond the {d.Name} ({d.ExtWheelMax.X:0.##} m wide, {d.ExtWheelMax.Y:0.##} m across)";
            if (k.W < MathF.Round(d.ExtWheelStart.X * 100) || k.H < MathF.Round(d.ExtWheelStart.Y * 100))
                return $"a wheel smaller than the {d.Name}'s first ({d.ExtWheelStart.X:0.##} x {d.ExtWheelStart.Y:0.##} m)";
        }
        else
        {
            if (k.Shape is not (ExShape.Section or ExShape.Round or ExShape.Pipe))
                return $"the {d.Name} extrudes sections: a {Extrusion.Label(k)} is another machine's order";
            if (k.Shape == ExShape.Section && Extrusion.Refusal(k.Mask) is { } bad)
                return bad;
            if (ExtrusionTable.Current.Find(k.Material) == null)
                return $"the {d.Name} can't extrude {mat}: takes {ExtrusionTable.Current.Takes()}";
            if (k.W > MathF.Round(d.ExtMaxSection.X * 100) || k.H > MathF.Round(d.ExtMaxSection.Y * 100))
                return $"a section {k.W / 100f:0.##} x {k.H / 100f:0.##} m is beyond the {d.Name} ({d.ExtMaxSection.X:0.##} x {d.ExtMaxSection.Y:0.##} m)";
            ConstructExtruder.Die(c, part, out _, out var die);
            if (k.W > MathF.Round(die.X * 100) + 0.5f || k.H > MathF.Round(die.Y * 100) + 0.5f)
                return $"a section {k.W / 100f:0.##} x {k.H / 100f:0.##} m is wider than the {d.Name}'s die ({die.X:0.##} x {die.Y:0.##} m): weld on Die Frames";
            var most = Math.Min(Extrusion.MaxLengthCm, (int)MathF.Round(d.ExtMaxLength * 100));
            if (k.L < Extrusion.StepCm || k.L > most || k.L % Extrusion.StepCm != 0)
                return $"a piece {k.L / 100f:0.##} m long: the {d.Name} makes {Extrusion.StepCm / 100f:0.##} to {most / 100f:0.##} m, in {Extrusion.StepCm} cm steps";
        }
        if (world.StoreOf(c, part) is { Amount: > 0 } st && st.Material != k.Material)
            return $"the {d.Name} is fed {names.NameOf(st.Material)}: an order in {mat} is another line";
        return null;
    }

    /// <summary>Server: queues an order on a machine, or says why not.</summary>
    public static string? Queue(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        if (Refusal(world, c, part, o) is { } why)
            return why;
        ref var p = ref c.Parts[part];
        p.Orders ??= new OrderList();
        p.Orders.Add(o with { Made = 0 });
        world.OrdersChanged(c);
        return null;
    }

    /// <summary>The order a machine works now, or false (no queue: its own settings; an empty queue: nothing).</summary>
    public static bool HeadOf(in CPart p, out WorkOrder head)
    {
        if (p.Orders is { Count: > 0, Bill: false } q)
        {
            head = q.Head;
            return true;
        }
        head = default;
        return false;
    }

    /// <summary>Is a piece on the die the head order's (its section, material and shape; its length may still be short)?</summary>
    public static bool ForHead(in WorkOrder head, in ExtrusionKey k) =>
        k.Shape == head.Key.Shape && k.Material == head.Key.Material && (k.Shape == ExShape.Wheel || k.Mask == head.Key.Mask && k.W == head.Key.W && k.H == head.Key.H && k.Wall == head.Key.Wall);

    /// <summary>Server: a piece for the head order came off: one more made, and the head goes once all are.</summary>
    public static void Made(ConstructWorld world, Construct c, OrderList? q)
    {
        if (q is not { Count: > 0 })
            return;
        ref var h = ref q.Head;
        h.Made++;
        if (h.Made >= h.Count)
            q.RemoveAt(0);
        else
            q.Recount();
        world.OrdersChanged(c);
    }

    // ---- The machine's buttons (server, under the configure rule: PanelTouch.cs) -------------------------------------------

    /// <summary>An order from a part's own settings: an Extruder's section and Cut every; a Wheel Former's width and diameter; its order count.</summary>
    public static string? FromSettings(Construct c, int part, out WorkOrder o)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        o = default;
        var count = (ushort)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.OrderCount, 1), 1, MaxCount);
        if (ProductionLines.IsConsole(d))
            return ConsoleOrder(p, d, count, out o);
        if (!TakesOrders(d))
            return $"the {d.Name} takes no orders";
        if (d.IsWheelFormer)
        {
            var w = Cm(ConstructConfig.Get(p, d, CfgKey.Width, d.ExtWheelStart.X));
            var h = Cm(ConstructConfig.Get(p, d, CfgKey.Height, d.ExtWheelStart.Y));
            o = new WorkOrder { Key = new ExtrusionKey(0, (ushort)w, (ushort)h, 0, 0, false, ExShape.Wheel), Part = ConstructParts.None, Count = count };
            return null;
        }
        ConstructExtruder.Section(c, part, out var mask, out var sw, out var sh, out _, out var shape, out var wall);
        var len = Cm(ConstructConfig.Get(p, d, CfgKey.CutLength, 0));
        if (len < Extrusion.StepCm)
            return $"set the {d.Name}'s Cut every: an order is a length";
        o = new WorkOrder { Key = new ExtrusionKey(mask, (ushort)sw, (ushort)sh, (ushort)len, 0, false, shape, wall), Part = ConstructParts.None, Count = count };
        return null;
    }

    private static int Cm(float m) => Math.Clamp((int)MathF.Round(m * 20) * Extrusion.StepCm, 0, ushort.MaxValue);

    /// <summary>The material for an order made at a machine: what its bin holds, else the first its table takes.</summary>
    private static byte MaterialFor(ConstructWorld world, Construct c, int part)
    {
        var d = c.DefOf(part);
        if (world.StoreOf(c, part) is { Amount: > 0 } st)
            return st.Material;
        var names = RecyclingTable.Current;
        for (byte m = 1; m <= names.Materials.Length; m++)
            if (d.IsWheelFormer ? ExtrusionTable.Current.FindWheel(m) != null : ExtrusionTable.Current.Find(m) != null)
                return m;
        return 1;
    }

    /// <summary>Touch "orders.queue": one order of the machine's own settings and Order count.</summary>
    public static string? QueueHere(ConstructWorld world, Construct c, int part)
    {
        if (FromSettings(c, part, out var o) is { } why)
            return why;
        o.Key = o.Key with { Material = MaterialFor(world, c, part) };
        return Queue(world, c, part, o);
    }

    /// <summary>Touch "orders.cancel": the head order goes (the piece on the die, if any, is cut off as it is).</summary>
    public static string? CancelHead(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders is not { Count: > 0 } q)
            return "no orders to cancel";
        q.RemoveAt(0);
        world.OrdersChanged(c);
        return null;
    }

    /// <summary>Touch "orders.clear": the queue goes, and the machine is back on its own settings.</summary>
    public static string? ClearHere(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders == null)
            return "no queue: it works its own settings";
        c.Parts[part].Orders = null;
        world.OrdersChanged(c);
        return null;
    }

    // ---- The bill (a Line Console's) -------------------------------------------------------------------------------------

    public const int KindPiece = 0, KindWheel = 1;

    /// <summary>The order a Line Console's settings say: Kind, Profile, Width, Height, Pipe wall, Length, Material, Count.</summary>
    private static string? ConsoleOrder(in CPart p, ConstructPartDef d, ushort count, out WorkOrder o)
    {
        o = default;
        var matSlot = d.CfgSlot[(int)CfgKey.OrderMaterial];
        if (matSlot < 0 || d.Config[matSlot].Options.Length == 0)
            return "this console has no order settings";
        var opts = d.Config[matSlot].Options;
        var material = RecyclingTable.Current.IdOf(opts[Math.Clamp((int)p.Cfg[matSlot], 0, opts.Length - 1)].ToLowerInvariant());
        if (material == 0)
            return "that material isn't in this game";
        var w = Cm(ConstructConfig.Get(p, d, CfgKey.Width, 0.25f));
        var h = Cm(ConstructConfig.Get(p, d, CfgKey.Height, 0.25f));
        if (ConstructConfig.Get(p, d, CfgKey.OrderKind, 0) >= 0.5f)
        {
            o = new WorkOrder { Key = new ExtrusionKey(0, (ushort)w, (ushort)h, 0, material, false, ExShape.Wheel), Part = ConstructParts.None, Count = count };
            return null;
        }
        var profile = (int)ConstructConfig.Get(p, d, CfgKey.Profile, 0);
        var shape = Extrusion.ShapeOf(profile);
        var mask = shape == ExShape.Section ? ConstructExtruder.MaskOf(p, d) : 0;
        if (shape == ExShape.Section && mask == 0)
            return "Custom needs a machine's cell grid: pick a preset section";
        ushort wall = 0;
        if (shape != ExShape.Section)
        {
            h = w;
            if (shape == ExShape.Pipe)
                wall = (ushort)Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Wall, 0.03f) * 100), 1, Math.Min(Extrusion.MaxWallCm, Math.Max(1, (w - Extrusion.MinBoreCm) / 2)));
        }
        var len = Cm(ConstructConfig.Get(p, d, CfgKey.CutLength, 1));
        o = new WorkOrder { Key = new ExtrusionKey(mask, (ushort)Math.Max(w, Extrusion.MinSectionCm), (ushort)Math.Max(h, Extrusion.MinSectionCm), (ushort)len, material, false, shape, wall), Part = ConstructParts.None, Count = count };
        return null;
    }

    /// <summary>Server: an order on the bill (Add, or the bill from a blueprint).</summary>
    public static string? AddToBill(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        ref var p = ref c.Parts[part];
        p.Orders ??= new OrderList { Bill = true };
        if (p.Orders.Count >= MaxOrders)
            return $"the bill is full ({MaxOrders} lines)";
        p.Orders.Add(o with { Made = 0 });
        p.Orders.Note = "";
        world.OrdersChanged(c);
        return null;
    }

    /// <summary>Touch, bill page "Add": the console's order settings as a line on its bill.</summary>
    public static string? BillAdd(ConstructWorld world, Construct c, int part)
    {
        if (FromSettings(c, part, out var o) is { } why)
            return why;
        return AddToBill(world, c, part, o);
    }

    /// <summary>Touch, bill page "Clear".</summary>
    public static string? BillClear(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders is not { Count: > 0 })
            return "the bill is empty";
        c.Parts[part].Orders = null;
        world.OrdersChanged(c);
        return null;
    }

    /// <summary>
    /// Touch, bill page "Send": each line of the bill, in order, goes whole to the next of the console's machines (round robin, on
    /// from the last that took one) that accepts it. A line none accepts stays, with the first machine's reason; the rest go.
    /// </summary>
    public static string? BillSend(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders is not { Count: > 0 } bill)
            return "the bill is empty: Add lines first";
        Span<int> list = stackalloc int[32];
        var n = ProductionLines.Machines(c, part, list);
        var takers = 0;
        for (var k = 0; k < n; k++)
            if (TakesOrders(c.DefOf(list[k])))
                list[takers++] = list[k];
        if (takers == 0)
            return "no machine on this line takes orders: wire the console's output to an Extruder's or a Wheel Former's Run";
        var cursor = DealCursor % takers;
        string? first = null;
        int sent = 0, kept = 0;
        for (var i = 0; i < bill.Count;)
        {
            string? why = null;
            var took = false;
            for (var t = 0; t < takers && !took; t++)
            {
                var m = list[(cursor + t) % takers];
                if (Queue(world, c, m, bill.Items[i]) is { } no)
                    why ??= $"{c.DefOf(m).Name}: {no}";
                else
                {
                    took = true;
                    cursor = (cursor + t + 1) % takers;
                }
            }
            if (took)
            {
                bill.RemoveAt(i);
                sent++;
            }
            else
            {
                first ??= why;
                kept++;
                i++;
            }
        }
        DealCursor = cursor;
        bill.Note = kept > 0 ? $"{kept} kept: {first}" : "";
        if (bill.Count == 0)
            c.Parts[part].Orders = null;
        world.OrdersChanged(c);
        return kept > 0 ? (sent > 0 ? $"sent {sent}; {kept} no machine took: {first}" : $"no machine took it: {first}") : null;
    }

    private static int DealCursor; // (server: where the next Send starts its round robin; one for the process is enough for "on from the last")

    // ---- Screens (either peer, allocating nothing) ---------------------------------------------------------------------------

    /// <summary>A line of the screen: "3/7 I-beam 0.25x0.25 4m metal", a marker first.</summary>
    public static void Row(PanelCard card, int line, char marker, in WorkOrder o)
    {
        card.Add(line, marker).Add(line, o.Made).Add(line, '/').Add(line, o.Count).Add(line, ' ');
        if (!o.IsPiece)
        {
            card.Add(line, ConstructParts.Def(o.Part).Name);
            return;
        }
        var k = o.Key;
        switch (k.Shape)
        {
            case ExShape.Wheel:
                card.Add(line, "Wheel ").AddFixed(line, k.H / 100f, 2).Add(line, 'x').AddFixed(line, k.W / 100f, 2);
                break;
            case ExShape.Round:
            case ExShape.Pipe:
                card.Add(line, k.Shape == ExShape.Round ? "Round " : "Pipe ").AddFixed(line, k.W / 100f, 2).Add(line, ' ').AddFixed(line, k.L / 100f, 2).Add(line, 'm');
                break;
            default:
                card.Add(line, Extrusion.ProfileLabel(k.Mask)).Add(line, ' ').AddFixed(line, k.W / 100f, 2).Add(line, 'x').AddFixed(line, k.H / 100f, 2).Add(line, ' ').AddFixed(line, k.L / 100f, 2).Add(line, 'm');
                break;
        }
        card.Add(line, ' ').Add(line, RecyclingTable.Current.NameOf(k.Material));
    }

    /// <summary>A machine's queue on its screen, from line <paramref name="from"/>: the totals, then the next three.</summary>
    public static void FillQueue(in CPart p, PanelCard card, int from)
    {
        if (p.Orders is not { } q)
            return;
        if (q.Total == 0)
        {
            card.Add(from, "Orders: none, waiting");
            return;
        }
        card.Add(from, "Orders ").Add(from, q.Total).Add(from, ", ").Add(from, q.PiecesLeft).Add(from, " to make");
        for (var k = 0; k < Math.Min(q.Count, Shown); k++)
            Row(card, from + 1 + k, k == 0 ? '>' : ' ', q.Items[k]);
    }

    /// <summary>A Line Console's bill page.</summary>
    public static void FillBill(Construct c, int part, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        card.TouchLabels[0] = "Add";
        card.TouchLabels[1] = "Send";
        card.TouchLabels[2] = "Clear";
        card.TouchLabels[3] = "Line";
        var q = p.Orders;
        var total = q?.Total ?? 0;
        card.State = total == 0 ? PanelState.Idle : q!.Note.Length > 0 ? PanelState.Wants : PanelState.Ok;
        card.Add(0, "Bill: ").Add(0, total).Add(0, " lines, ").Add(0, q?.PiecesLeft ?? 0).Add(0, " pieces");
        if (FromSettings(c, part, out var next) is null)
            Row(card, 1, '+', next);
        if (q != null)
            for (var k = 0; k < Math.Min(q.Count, Shown); k++)
                Row(card, 2 + k, ' ', q.Items[k]);
        if (q is { Note.Length: > 0 })
            card.Add(5, q.Note.Length > PanelCard.MaxChars ? q.Note[..PanelCard.MaxChars] : q.Note);
    }

    // ---- The wire (CBodyKind.Orders) ---------------------------------------------------------------------------------------

    /// <summary>Does a body hold any queue or bill?</summary>
    public static bool Any(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders != null)
                return true;
        return false;
    }

    /// <summary>One body's queues and bills: u16 body, u16 lists; each u16 part, u8 flags (1: a bill), u16 orders, u32 pieces left, a note, u8 n, then n orders.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Orders).U16(c.Id);
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Orders is not { } q)
                continue;
            w.U16((ushort)i).U8((byte)(q.Bill ? 1 : 0)).U16((ushort)q.Total).U32((uint)q.PiecesLeft).Str(q.Note);
            var shown = Math.Min(q.Count, Shown);
            w.U8((byte)shown);
            for (var k = 0; k < shown; k++)
                WriteOrder(w, q.Items[k]);
            n++;
        }
        w.PatchU16(at, (ushort)n);
    }

    private static void WriteOrder(PacketWriter w, in WorkOrder o)
    {
        var k = o.Key;
        w.U8((byte)k.Shape).U32((uint)k.Mask).U32((uint)(k.Mask >> 32)).U16(k.W).U16(k.H).U16(k.L).U16(k.Wall).U8(k.Material).U16(o.Part).U16(o.Count).U16(o.Made);
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        int n = r.U16();
        var c = world.Get(id);
        if (c != null)
            for (var i = 0; i < c.PartCount; i++)
                c.Parts[i].Orders = null; // (the server's word is the whole set)
        for (var j = 0; j < n; j++)
        {
            var part = r.U16();
            var flags = r.U8();
            int total = r.U16();
            var left = r.U32();
            var note = r.Text(200);
            int shown = r.U8();
            if (shown > Shown || total > MaxOrders || shown > total)
                throw new MalformedPacketException($"an order list of {total}, {shown} shown");
            var q = new OrderList { Bill = (flags & 1) != 0, Note = note };
            for (var k = 0; k < shown; k++)
            {
                var shape = r.U8();
                var mask = (ulong)r.U32() | (ulong)r.U32() << 32;
                var o = new WorkOrder
                {
                    Key = new ExtrusionKey(mask, r.U16(), r.U16(), r.U16(), 0, false, (ExShape)shape, 0),
                };
                var wall = r.U16();
                var mat = r.U8();
                o.Key = o.Key with { Wall = wall, Material = mat };
                o.Part = r.U16();
                o.Count = r.U16();
                o.Made = r.U16();
                if (shape > (byte)ExShape.Wing || mat > RecyclingTable.Current.Materials.Length || o.Made > o.Count || o.Part != ConstructParts.None && ConstructParts.Get(o.Part) == null)
                    throw new MalformedPacketException("a bad work order");
                q.Add(o);
            }
            q.Total = total;
            q.PiecesLeft = (int)Math.Min(left, int.MaxValue);
            if (c != null && part < c.PartCount)
                c.Parts[part].Orders = q;
        }
    }

    // ---- Saves (after a body's conveyors block: an older save ends before it) ------------------------------------------------

    /// <summary>A body's lists for a snapshot (a save is rare: a copy is fine, and the writer may run on another thread).</summary>
    public static (ushort Part, OrderList List)[]? Snapshot(Construct c)
    {
        List<(ushort, OrderList)>? o = null;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders is { } q)
            {
                var copy = new OrderList { Bill = q.Bill, Items = q.Items[..q.Count], Count = q.Count };
                copy.Recount();
                (o ??= new()).Add(((ushort)i, copy));
            }
        return o?.ToArray();
    }

    public static void WriteSave(SaveWriter w, (ushort Part, OrderList List)[] lists, WorldNames names)
    {
        w.U16((ushort)lists.Length);
        foreach (var (part, q) in lists)
        {
            w.U16(part).U8((byte)(q.Bill ? 1 : 0)).U16((ushort)q.Count);
            for (var k = 0; k < q.Count; k++)
            {
                ref readonly var o = ref q.Items[k];
                var key = o.Key;
                w.U8((byte)key.Shape).U64(key.Mask).U16(key.W).U16(key.H).U16(key.L).U16(key.Wall).U8(key.Material).U16(names.Part(o.Part)).U16(o.Count).U16(o.Made);
            }
        }
    }

    public static (ushort Part, OrderList List)[]? ReadSave(SaveReader r, int bodyEnd, WorldNamesRead names)
    {
        if (!r.Has(2) || r.Pos + 2 > bodyEnd)
            return null;
        int n = r.U16();
        var lists = new List<(ushort, OrderList)>(n);
        for (var j = 0; j < n; j++)
        {
            var part = r.U16();
            var bill = (r.U8() & 1) != 0;
            int count = r.U16();
            if (count > MaxOrders)
                throw new WorldFileException($"{count} work orders");
            var q = new OrderList { Bill = bill };
            for (var k = 0; k < count; k++)
            {
                var shape = r.U8();
                var mask = r.U64();
                var w = r.U16();
                var h = r.U16();
                var l = r.U16();
                var wall = r.U16();
                var mat = names.Material(r.U8());
                var pn = r.U16();
                var def = pn == ushort.MaxValue ? ConstructParts.None : names.Part(pn, "a work order");
                var cnt = r.U16();
                var made = r.U16();
                if (shape > (byte)ExShape.Wing || cnt is 0 or > MaxCount || pn != ushort.MaxValue && def == ConstructParts.None || pn == ushort.MaxValue && mat == 0)
                    continue; // (what this build can't make or name is left out)
                q.Add(new WorkOrder { Key = new ExtrusionKey(mask, w, h, l, mat, false, (ExShape)shape, wall), Part = def, Count = cnt, Made = Math.Min(made, cnt) });
            }
            lists.Add((part, q));
        }
        return lists.ToArray();
    }

    /// <summary>Puts a save's lists on the body's parts (one on a part that can't hold it is left out).</summary>
    public static void Apply(Construct c, (ushort Part, OrderList List)[] lists)
    {
        foreach (var (part, q) in lists)
        {
            if (part >= c.PartCount)
                continue;
            var d = c.DefOf(part);
            if (q.Bill ? !ProductionLines.IsConsole(d) : !TakesOrders(d))
                continue;
            c.Parts[part].Orders = q;
        }
    }
}

/// <summary>
/// A blueprint's bill (docs/sandbox/constructs/production.md "Work orders"): every extruded or formed piece it needs, grouped by
/// key with counts, and every catalogue part. The building station will call <see cref="FromBlueprint"/> to issue a blueprint's
/// pieces as orders; <c>--bill NAME</c> prints it.
/// </summary>
public static class ConstructBill
{
    public sealed class Bill
    {
        public readonly List<WorkOrder> Pieces = new(); // by key, first seen first
        public readonly List<(string Part, int Count)> Parts = new(); // catalogue parts, by name
    }

    public static Bill FromBlueprint(ConstructBuild.Blueprint bp)
    {
        var bill = new Bill();
        var parts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in bp.Steps)
        {
            if (step.Part.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (Extrusion.ParseKey(step.Part) is not { } key)
                    continue;
                key = key with { Growing = false };
                var k = bill.Pieces.FindIndex(o => o.Key == key);
                if (k < 0)
                    bill.Pieces.Add(new WorkOrder { Key = key, Part = ConstructParts.None, Count = 1 });
                else
                    bill.Pieces[k] = bill.Pieces[k] with { Count = (ushort)Math.Min(bill.Pieces[k].Count + 1, WorkOrders.MaxCount) };
                continue;
            }
            parts[step.Part] = parts.GetValueOrDefault(step.Part) + 1;
        }
        foreach (var (name, n) in parts)
            bill.Parts.Add((name, n));
        return bill;
    }

    /// <summary>Server: a blueprint's pieces onto a Line Console's bill (creative and the probe; the station's later). Null, or why not.</summary>
    public static string? ToConsole(ConstructWorld world, Construct c, int console, ConstructBuild.Blueprint bp)
    {
        if (!ProductionLines.IsConsole(c.DefOf(console)))
            return "that isn't a Line Console";
        var bill = FromBlueprint(bp);
        if (bill.Pieces.Count == 0)
            return $"{bp.Name} needs no extruded or formed pieces";
        foreach (var o in bill.Pieces)
            if (WorkOrders.AddToBill(world, c, console, o) is { } why)
                return why;
        return null;
    }

    /// <summary><c>--bill NAME</c>: a blueprint's bill as text.</summary>
    public static int Cli(string name)
    {
        if (ConstructBuild.FindBlueprint(name) is not { } bp)
        {
            Console.WriteLine($"no blueprint named \"{name}\"");
            return 1;
        }
        var bill = FromBlueprint(bp);
        var sb = new StringBuilder();
        sb.AppendLine($"{bp.Name}: {bill.Pieces.Count} kinds of piece, {bill.Pieces.Sum(o => o.Count)} pieces; {bill.Parts.Count} kinds of part, {bill.Parts.Sum(p => p.Count)} parts");
        foreach (var o in bill.Pieces)
            sb.AppendLine($"  {o.Count,4} x {Extrusion.Name(o.Key)}  ({Extrusion.Label(o.Key)}; {Maker(o.Key)})");
        foreach (var (part, n) in bill.Parts.OrderByDescending(p => p.Count))
            sb.AppendLine($"  {n,4} x {part}");
        Console.Write(sb.ToString());
        return 0;
    }

    private static string Maker(in ExtrusionKey k) => k.Shape switch
    {
        ExShape.Wheel => "a Wheel Former",
        ExShape.Fan or ExShape.Blade => "a Fan Former (no orders yet)",
        ExShape.Wing => "an Airframe Former (no orders yet)",
        _ => "an Extruder",
    };
}

/// <summary>Work orders' side of the world (WorkOrders.cs).</summary>
public sealed partial class ConstructWorld
{
    public readonly List<ushort> OrdersDirty = new(32); // bodies whose queues or bills changed (CBodyKind.Orders)

    public void OrdersChanged(Construct c)
    {
        if (!OrdersDirty.Contains(c.Id))
            OrdersDirty.Add(c.Id);
    }
}
