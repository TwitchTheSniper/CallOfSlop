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
    public ushort Chain; // server: the chained order's link it serves (ConstructLogisticsPlan; 0: the player's own); saved in the LOGI block, never sent
    public string? Name; // a formed panel or a lofted hull part by its name, which is its key (HullLoft.Panels.cs): a Panel Former's order; Key holds its material only

    public readonly bool IsPiece => Part == ConstructParts.None && Name == null;
    public readonly bool IsPanel => Name != null && !Pressed.IsName(Name);
    public readonly bool IsPressed => Pressed.IsName(Name); // a pressed part by its key (Press phase 4): a Press's order

    /// <summary>What makes it: 0 an Extruder or a Former (a piece), 1 an Assembler or a Press (a catalogue part; a preset is a Press's), 2 a Panel Former (a named panel) or a Press (a pressed key).</summary>
    public readonly int Kind => Name != null ? 2 : Part != ConstructParts.None ? 1 : 0;
    public readonly int Left => Math.Max(0, Count - Made);
}

/// <summary>
/// A machine's queue or a Line Console's bill: at most <see cref="WorkOrders.MaxOrders"/> orders, oldest first. Held on the part
/// (<see cref="CPart.Orders"/>), so it moves with the part through renumbering, splits and cuts. The server holds every order; a
/// client holds the head (up to <see cref="WorkOrders.Sent"/>) and the totals, which is all a screen shows.
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
    public const int Sent = 32; // orders a client is sent (a screen's list scrolls through them; 3 before protocol 248)
    private const int OrderBytes = 22; // one order on the wire
    public const byte NamedShape = 255; // an order's shape byte for a named panel: its name follows as text (wire and saves)

    // ---- Accepting --------------------------------------------------------------------------------------------------------

    /// <summary>A machine that can take orders: an Extruder, a Wheel, Fan, Airframe or Panel Former, an Assembler of any family, or a Press (not a Die Lathe).</summary>
    public static bool TakesOrders(ConstructPartDef d) => d.IsExtruder || d.IsAssembler || d.IsPress && !d.IsLathe;

    /// <summary>A Fan or an Airframe Former: its piece is made whole to its order's size (ConstructExtruder.GrowWhole).</summary>
    public static bool IsWholeFormer(ConstructPartDef d) => d.IsFanFormer || d.IsAirframeFormer;

    /// <summary>The name of an Assembler family (<see cref="AssemblingTable.Machines"/>), for a refusal.</summary>
    public static string FamilyName(byte machine) => machine switch { 1 => "Thruster Assembler", 2 => "Tailor", _ => "Assembler" };

    /// <summary>Server: will this machine take the order? Null, or why not (one reason; a refused order is never queued).</summary>
    public static string? Refusal(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        var d = c.DefOf(part);
        if (!TakesOrders(d))
            return $"the {d.Name} takes no orders";
        if (d.IsLaserCutter)
            return ConstructLaserCutter.Refusal(world, c, part, d, o);
        if (o.Name != null && HullLoft.IsCutName(o.Name))
            return $"the {d.Name} cuts nothing: a cut part is a Laser Cutter's order";
        if (d.IsPress)
            return PressRefusal(c, part, d, o);
        if (d.IsPanelFormer)
            return ConstructPanelFormer.Refusal(world, c, part, d, o);
        if (o.IsPressed)
            return $"the {d.Name} presses nothing: {Pressed.Label(o.Name!)} is a Press's order";
        if (o.IsPanel)
            return $"the {d.Name} takes no panels: {o.Name} is a Panel Former's order";
        if (d.IsAssembler)
            return AssemblerRefusal(c, part, d, o);
        if (IsWholeFormer(d))
            return FormerRefusal(world, c, part, d, o);
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

    /// <summary>An Assembler's side: a catalogue part its family has a recipe for, at its tier, and room in the queue.</summary>
    private static string? AssemblerRefusal(Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (o.IsPiece)
            return $"the {d.Name} makes catalogue parts: a {Extrusion.Label(o.Key)} is {Maker(o.Key)}'s order";
        var made = ConstructParts.Def(o.Part);
        if (o.Count is < 1 or > MaxCount)
            return $"an order is for 1 to {MaxCount} parts";
        if (AssemblingTable.Current.Of(o.Part) is not { } r)
            return FormerPresets.Line(made) ?? $"no recipe makes the {made.Name}";
        if (r.Machine != d.AsmMachine)
            return $"the {made.Name} is made by a {FamilyName(r.Machine)}, not the {d.Name}";
        if (r.MinTier > d.Tier)
            return $"the {made.Name} needs a tier {r.MinTier} {FamilyName(r.Machine)} (this is tier {d.Tier})";
        if (c.Parts[part].Orders is { Count: >= MaxOrders })
            return $"the {d.Name}'s queue is full ({MaxOrders} orders)";
        return null;
    }

    /// <summary>
    /// A Press's side (Press phase 4): a pressed key, or a catalogue part that is a preset, whose die is the one fitted and whose blank is
    /// inside the press's across. The order sets what the press makes: its bays take only the order's blank (ConstructPress.SizedRefusal).
    /// </summary>
    private static string? PressRefusal(Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (Pressed.SpecOf(o) is not { } spec)
            return o.IsPiece ? $"the {d.Name} presses parts: a {Extrusion.Label(o.Key)} is {Maker(o.Key)}'s order"
                : o.Name != null ? (o.IsPressed ? "not a pressed part" : $"the {d.Name} presses parts: {o.Name} is a Panel Former's order")
                : $"no die presses the {ConstructParts.Def(o.Part).Name}";
        if (o.Count is < 1 or > MaxCount)
            return $"an order is for 1 to {MaxCount} parts";
        if (ConstructPress.SizedDieOf(PressTable.Current, c, part) is not { } die)
            return $"the {d.Name} has no die: this order needs a {spec.Die.Family} Die";
        if (!ReferenceEquals(die, spec.Die))
            return $"the {d.Name} holds a {die.Family} Die: this order needs a {spec.Die.Family} Die";
        var across = Pressed.Across(spec.Blank);
        if (across > d.PressMaxCm)
            return $"a {Pressed.ClassWord(Pressed.PressClass(across))} Press's order: the blank is {across / 100f:0.00} m across";
        if (c.Parts[part].Orders is { Count: >= MaxOrders })
            return $"the {d.Name}'s queue is full ({MaxOrders} orders)";
        return null;
    }

    /// <summary>
    /// A Fan or an Airframe Former's side: a prop or a wing by its key, or a catalogue part that is one of its presets (FormerPresets.cs), inside
    /// the machine's sizes, in a material its table takes and its bin isn't fed otherwise.
    /// </summary>
    private static string? FormerRefusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        var fan = d.IsFanFormer;
        if (!FormerPresets.KeyFor(o, out var k))
            return o.Name != null ? $"the {d.Name} takes no panels or pressed parts: {o.Name} is another machine's order"
                : AssemblingTable.Current.Of(o.Part) != null ? $"the {ConstructParts.Def(o.Part).Name} is an Assembler's order"
                : $"no former's preset makes the {ConstructParts.Def(o.Part).Name}";
        if (o.Count is < 1 or > MaxCount)
            return $"an order is for 1 to {MaxCount} pieces";
        if (k.Shape != (fan ? ExShape.Fan : ExShape.Wing))
            return $"the {d.Name} {(fan ? "forms props and rotors" : "cuts wings and control surfaces")}: a {Extrusion.Label(k)} is {Maker(k)}'s order";
        var names = RecyclingTable.Current;
        var mat = names.NameOf(k.Material);
        if ((fan ? ExtrusionTable.Current.FindFan(k.Material) : ExtrusionTable.Current.FindWing(k.Material)) == null)
            return $"the {d.Name} can't form {mat}";
        if (fan ? Extrusion.FanKindOf(k.Wall) == null : Extrusion.AirframeOf(k.Wall) == null)
            return $"the {d.Name} doesn't know that kind";
        if (FormerPresets.Fits(d, k) is { } size)
            return size;
        if (k.H % Extrusion.StepCm != 0 || k.W % Extrusion.StepCm != 0 || k.L % Extrusion.StepCm != 0)
            return $"sizes go in {Extrusion.StepCm} cm steps";
        if (c.Parts[part].Orders is { Count: >= MaxOrders })
            return $"the {d.Name}'s queue is full ({MaxOrders} orders)";
        if (world.StoreOf(c, part) is { Amount: > 0 } st && st.Material != k.Material)
            return $"the {d.Name} is fed {names.NameOf(st.Material)}: an order in {mat} is another line";
        return null;
    }

    /// <summary>A pressed part's order by its key (its material the blank's), or null when the key doesn't read.</summary>
    public static WorkOrder? PressedOrder(string key, int count) => Pressed.SpecOf(key) is { } s
        ? new WorkOrder { Name = key, Key = new ExtrusionKey(0, 0, 0, 0, s.Blank.Material, false, ExShape.Section), Part = ConstructParts.None, Count = (ushort)Math.Clamp(count, 1, MaxCount) }
        : null;

    /// <summary>Which machine makes a piece: "an Extruder", "a Wheel Former"...</summary>
    public static string Maker(in ExtrusionKey k) => k.Shape switch
    {
        ExShape.Wheel => "a Wheel Former",
        ExShape.Fan or ExShape.Blade => "a Fan Former",
        ExShape.Wing => "an Airframe Former",
        _ => "an Extruder",
    };

    /// <summary>Server: queues an order on a machine, or says why not.</summary>
    public static string? Queue(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        if (Refusal(world, c, part, o) is { } why)
            return why;
        ref var p = ref c.Parts[part];
        p.Orders ??= new OrderList();
        var add = o with { Made = 0 };
        if (!o.IsPiece && IsWholeFormer(p.D) && FormerPresets.KeyFor(o, out var preset))
            add.Key = preset; // (a former's preset: the part by its id, and the key it is cut as, for its material on screens and look lines)
        p.Orders.Add(add);
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

    /// <summary>
    /// Is a piece on the die the head order's (<paramref name="want"/>: its key, FormerPresets.KeyFor)? Its section, material and shape; its length
    /// may still be short. A prop its kind, style and blades (its diameter grows); a wing its kind and chord (its span grows).
    /// </summary>
    public static bool ForHead(in ExtrusionKey want, in ExtrusionKey k) =>
        k.Shape == want.Shape && k.Material == want.Material && k.Shape switch
        {
            ExShape.Wheel => true,
            ExShape.Fan => k.Wall == want.Wall,
            ExShape.Wing => k.Wall == want.Wall && k.W == want.W,
            _ => k.Mask == want.Mask && k.W == want.W && k.H == want.H && k.Wall == want.Wall,
        };

    /// <summary>Server: a piece for the head order came off: one more made, and the head goes once all are.</summary>
    public static void Made(ConstructWorld world, Construct c, OrderList? q, int n = 1)
    {
        if (q is not { Count: > 0 })
            return;
        ref var h = ref q.Head;
        h.Made = (ushort)Math.Min(h.Count, h.Made + n); // (an Assembler's batch may make more than the order still asks: the rest is a bonus)
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
        if (d.IsLaserCutter)
        {
            // (no design setting yet, phase 5: Queue adds its Order count of the head order's cut part)
            if (!HeadOf(p, out var lh) || lh.Name == null)
                return $"the {d.Name} cuts to an order: from a Panel Designer, a Crafting Terminal or a bill; then Queue repeats it";
            o = ConstructPanelFormer.Order(lh.Name, count);
            return null;
        }
        if (!TakesOrders(d))
            return $"the {d.Name} takes no orders";
        if (d.IsAssembler)
        {
            // An Assembler's order is its Recipe setting's part.
            var k = (int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) - 1;
            var list = AssemblingTable.Current.ByMachine[d.AsmMachine];
            if (k < 0 || k >= list.Length)
                return $"set the {d.Name}'s Recipe: an order is a part";
            o = new WorkOrder { Part = (ushort)list[k].Output.Id, Count = count };
            return null;
        }
        if (d.IsPanelFormer)
            return ConstructPanelFormer.FromSettings(c, part, count, "steel", out o);
        if (d.IsPress)
            return $"the {d.Name} takes its orders from a Crafting Terminal or a Line Console";
        if (d.IsFanFormer)
        {
            // A prop of its settings: what it makes, its blades and style, how far across.
            var across = Cm(ConstructConfig.Get(p, d, CfgKey.FanDiameter, d.ExtWheelStart.Y));
            var fanWall = Extrusion.FanWall((int)ConstructConfig.Get(p, d, CfgKey.FanKind, 0), (int)ConstructConfig.Get(p, d, CfgKey.BladeStyle, 0), (int)ConstructConfig.Get(p, d, CfgKey.FanBlades, 3));
            o = new WorkOrder { Key = new ExtrusionKey(0, 0, (ushort)across, 0, 0, false, ExShape.Fan, fanWall), Part = ConstructParts.None, Count = count };
            return null;
        }
        if (d.IsAirframeFormer)
        {
            // A wing or a control surface of its settings: what it cuts, its chord and its span.
            var chord = Cm(ConstructConfig.Get(p, d, CfgKey.WingChord, d.ExtWheelStart.X));
            var span = Cm(ConstructConfig.Get(p, d, CfgKey.WingSpan, d.ExtWheelStart.Y));
            o = new WorkOrder { Key = new ExtrusionKey(0, (ushort)chord, 0, (ushort)span, 0, false, ExShape.Wing, (ushort)ConstructConfig.Get(p, d, CfgKey.WingKind, 0)), Part = ConstructParts.None, Count = count };
            return null;
        }
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
            if (d.IsFanFormer ? ExtrusionTable.Current.FindFan(m) != null : d.IsAirframeFormer ? ExtrusionTable.Current.FindWing(m) != null
                : d.IsWheelFormer ? ExtrusionTable.Current.FindWheel(m) != null : ExtrusionTable.Current.Find(m) != null)
                return m;
        return 1;
    }

    /// <summary>Touch "orders.queue": one order of the machine's own settings and Order count.</summary>
    public static string? QueueHere(ConstructWorld world, Construct c, int part)
    {
        if (FromSettings(c, part, out var o) is { } why)
            return why;
        if (o.IsPiece)
            o.Key = o.Key with { Material = MaterialFor(world, c, part) };
        else if (o.IsPanel && !c.DefOf(part).IsLaserCutter && world.StoreOf(c, part) is { Amount: > 0 } fed && fed.Material != o.Key.Material
            && ConstructPanelFormer.FromSettings(c, part, o.Count, RecyclingTable.Current.NameOf(fed.Material), out var inStock) == null)
            o = inStock; // (a preset in what its bin holds)
        return Queue(world, c, part, o);
    }

    /// <summary>Touch "orders.cancel": the head order goes (the piece on the die, if any, is cut off as it is).</summary>
    public static string? CancelHead(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders is not { Count: > 0 } q)
            return "no orders to cancel";
        if (c.DefOf(part).IsLaserCutter)
            ConstructLaserCutter.GiveBack(world, c, part); // (the head's blank leaves the bed, so it is never cut to the next order)
        q.RemoveAt(0);
        world.OrdersChanged(c);
        return null;
    }

    /// <summary>Touch "orders.clear": the queue goes, and the machine is back on its own settings.</summary>
    public static string? ClearHere(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders == null)
            return "no queue: it works its own settings";
        if (c.DefOf(part).IsLaserCutter)
            ConstructLaserCutter.GiveBack(world, c, part);
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
            return "no machine on this line takes orders: wire the console's output to an Extruder's, a Wheel Former's, an Assembler's or a Press's Run";
        var cursor = DealCursor % takers;
        string? first = null;
        int sent = 0, kept = 0;
        for (var i = 0; i < bill.Count;)
        {
            string? why = null;
            var whyKind = false;
            var took = false;
            for (var t = 0; t < takers && !took; t++)
            {
                var m = list[(cursor + t) % takers];
                if (Queue(world, c, m, bill.Items[i]) is { } no)
                {
                    // (the reason that matters is the first from a machine of the order's kind: an Assembler's for a part, else an Extruder's or a Former's)
                    var md = c.DefOf(m);
                    var kind = md.IsLaserCutter ? bill.Items[i].Name != null && HullLoft.IsCutName(bill.Items[i].Name!)
                        : md.IsPress ? Pressed.SpecOf(bill.Items[i]) != null
                        : IsWholeFormer(md) && FormerPresets.KeyFor(bill.Items[i], out var fk) ? fk.Shape == (md.IsFanFormer ? ExShape.Fan : ExShape.Wing)
                        : (md.IsPanelFormer && !md.IsLaserCutter ? 2 : md.IsAssembler ? 1 : 0) == bill.Items[i].Kind;
                    if (why == null || kind && !whyKind)
                        (why, whyKind) = ($"{c.DefOf(m).Name}: {no}", kind);
                }
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

    /// <summary>
    /// An order on the screen: "3/7 I-beam 0.25x0.25 4.00m metal", a marker first. On one line when it fits <paramref name="cols"/>,
    /// else the length and material go on the next line, indented and dimmer, so nothing is cut off. Returns the lines it took.
    /// </summary>
    public static int Row(PanelCard card, int line, char marker, in WorkOrder o, int cols = PanelCard.MaxChars)
    {
        if (marker != '\0')
            card.Add(line, marker);
        card.Add(line, o.Made).Add(line, '/').Add(line, o.Count).Add(line, ' ');
        if (o.Name != null)
        {
            // A panel by its key, without the family's prefix when it is a formed panel's ("hood 160x150 c14 r18 t20 steel").
            var (from, to) = HullLoft.IsPanelName(o.Name) ? (HullLoft.PanelPrefix.Length, o.Name.Length - 1) : Pressed.IsName(o.Name) ? (Pressed.Prefix.Length, o.Name.Length - 1) : (0, o.Name.Length);
            for (var ch = from; ch < to && ch - from < cols; ch++)
                card.Add(line, o.Name[ch]);
            card.Clip(line, cols);
            return 1;
        }
        if (!o.IsPiece)
        {
            card.Add(line, ConstructParts.Def(o.Part).Name).Clip(line, cols);
            return 1;
        }
        var k = o.Key;
        var tail = line + 1;
        switch (k.Shape)
        {
            case ExShape.Wheel:
                card.Add(line, "Wheel ").AddShort(line, k.H / 100f).Add(line, 'x').AddShort(line, k.W / 100f);
                break;
            case ExShape.Fan:
                // "Propeller 1.8m 3 blades": its kind and size, the blades on the next line when it doesn't fit.
                card.Add(line, Extrusion.FanKindOf(k.Wall)?.Label ?? "Fan").Add(line, ' ').AddShort(line, k.H / 100f).Add(line, 'm');
                card.Add(tail, Extrusion.FanBladesOf(k.Wall)).Add(tail, " blades ");
                break;
            case ExShape.Wing:
                card.Add(line, Extrusion.AirframeOf(k.Wall)?.Label ?? "Wing").Add(line, ' ').AddShort(line, k.W / 100f).Add(line, 'x').AddShort(line, k.L / 100f);
                break;
            case ExShape.Round:
            case ExShape.Pipe:
                card.Add(line, k.Shape == ExShape.Round ? "Round " : "Pipe ").AddShort(line, k.W / 100f);
                card.AddShort(tail, k.L / 100f).Add(tail, "m ");
                break;
            default:
                card.Add(line, Extrusion.ProfileLabel(k.Mask)).Add(line, ' ').AddShort(line, k.W / 100f).Add(line, 'x').AddShort(line, k.H / 100f);
                card.AddShort(tail, k.L / 100f).Add(tail, "m ");
                break;
        }
        card.Add(tail, RecyclingTable.Current.NameOf(k.Material));
        if (tail >= PanelCard.MaxLines || card.Length(line) + 1 + card.Length(tail) <= cols)
        {
            card.Add(line, ' ').Join(line, tail).Clip(line, cols);
            return 1;
        }
        card.Clip(line, cols).Indent(tail, 4).Clip(tail, cols).Dim(tail);
        return 2;
    }

    /// <summary>
    /// A machine's queue on its screen, from line <paramref name="from"/>: the totals with the scroll mark, then as many orders as the screen's
    /// rows hold, from the viewer's scroll (<see cref="PanelCard.Scroll"/>); an order too long for a line takes two (<see cref="Row"/>).
    /// </summary>
    public static void FillQueue(in CPart p, PanelCard card, int from)
    {
        if (p.Orders is not { } q)
            return;
        var cols = card.Cols;
        if (from >= card.TextRows)
            return; // (no row left above the buttons at this Text size)
        if (q.Total == 0)
        {
            card.Add(from, "Orders: none, waiting").Clip(from, cols);
            return;
        }
        card.Add(from, q.Total).Add(from, q.Total == 1 ? " order, " : " orders, ").Add(from, q.PiecesLeft).Add(from, " to make");
        var shown = List(card, q, from + 1, card.TextRows, cols, k => k == 0 ? '>' : ' ');
        card.ScrollMark(from, card.Scroll, shown, q.Total, cols).Clip(from, cols);
    }

    /// <summary>A list's orders on lines <paramref name="top"/> up to <paramref name="end"/>, from the viewer's scroll, clamped; sets how far it scrolls. Returns how many show.</summary>
    private static int List(PanelCard card, OrderList q, int top, int end, int cols, Func<int, char> marker)
    {
        var first = card.Scroll = Math.Clamp(card.Scroll, 0, Math.Max(0, q.Count - 1));
        var line = top;
        var shown = 0;
        for (var k = first; k < q.Count && line < end; k++)
        {
            var used = Row(card, line, marker(k), q.Items[k], cols);
            if (line + used > end)
            {
                card.ClearLine(line).ClearLine(line + 1); // (an order that takes two lines doesn't show by half)
                break;
            }
            line += used;
            shown++;
        }
        // More below: at least one further; at the end, back up while the rows left over would hold more (an order takes at most two).
        card.ScrollMax = first + shown < q.Count ? Math.Max(first + 1, q.Count - (end - top)) : Math.Max(0, first - (end - line) / 2);
        return shown;
    }

    private static readonly Func<int, char> NoMarker = _ => ' ';

    /// <summary>A Line Console's bill page: the pieces, the scroll mark, the line Add would put on, the bill's lines from the viewer's scroll, and the note under them.</summary>
    public static void FillBill(Construct c, int part, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        card.TouchLabels[0] = "Add";
        card.TouchLabels[1] = "Send";
        card.TouchLabels[2] = "Clear";
        card.TouchLabels[3] = "Line";
        var cols = card.Cols;
        var rows = card.TextRows;
        var q = p.Orders;
        var total = q?.Total ?? 0;
        card.State = total == 0 ? PanelState.Idle : q!.Note.Length > 0 ? PanelState.Wants : PanelState.Ok;
        card.Add(0, "Bill: ").Add(0, q?.PiecesLeft ?? 0).Add(0, " pieces");
        var line = 1;
        if (FromSettings(c, part, out var next) is null)
        {
            var used = Row(card, 1, '+', next, cols);
            card.Dim(1);
            line += used;
        }
        var noteRows = q is { Note.Length: > 0 } ? Math.Min(2, Math.Max(1, rows - 4)) : 0;
        var shown = q != null ? List(card, q, line, rows - noteRows, cols, NoMarker) : 0;
        if (q == null)
            card.Scroll = card.ScrollMax = 0;
        card.ScrollMark(0, card.Scroll, shown, total, cols).Clip(0, cols);
        if (noteRows > 0)
        {
            int at = 0, column = 0;
            var top = rows - noteRows;
            card.Wrap(q!.Note, cols, top, rows - 1, 0, ref at, ref column);
            for (var l = top; l < rows; l++)
                card.Tint(l, 0, PanelCard.MaxChars, new System.Numerics.Vector3(1f, 0.68f, 0.08f)); // (amber, as a Starved lamp: why a line stayed)
        }
    }

    // ---- The wire (CBodyKind.Orders) ---------------------------------------------------------------------------------------

    /// <summary>Does a body hold any queue or bill?</summary>
    public static bool Any(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders != null)
                return true;
        return ProductionLines.AnyMade(c);
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
            var shown = Math.Min(q.Count, Sent);
            w.U8((byte)shown);
            for (var k = 0; k < shown; k++)
                WriteOrder(w, q.Items[k]);
            n++;
        }
        w.PatchU16(at, (ushort)n);
        ProductionLines.WriteMade(w, c); // (protocol 252: the consoles' Made counts)
    }

    private static void WriteOrder(PacketWriter w, in WorkOrder o)
    {
        var k = o.Key;
        if (o.Name != null)
        {
            // A named panel (protocol 253): shape 255, its material, counts, then the name as text.
            w.U8(NamedShape).U8(k.Material).U16(o.Count).U16(o.Made).Str(o.Name);
            return;
        }
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
            if (shown > Sent || total > MaxOrders || shown > total)
                throw new MalformedPacketException($"an order list of {total}, {shown} shown");
            var q = new OrderList { Bill = (flags & 1) != 0, Note = note };
            for (var k = 0; k < shown; k++)
            {
                var shape = r.U8();
                if (shape == NamedShape)
                {
                    var named = new WorkOrder { Part = ConstructParts.None };
                    var nm = r.U8();
                    named.Count = r.U16();
                    named.Made = r.U16();
                    named.Name = r.Text(ConstructParts.MaxRuntimeName);
                    named.Key = new ExtrusionKey(0, 0, 0, 0, nm, false, ExShape.Section);
                    if (nm > RecyclingTable.Current.Materials.Length || named.Made > named.Count || !HullLoft.IsFormedName(named.Name) && !HullLoft.IsCutName(named.Name) && !Pressed.IsName(named.Name))
                        throw new MalformedPacketException("a bad panel order");
                    q.Add(named);
                    continue;
                }
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
        ProductionLines.ReadMade(ref r, c);
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
                if (o.Name != null)
                {
                    w.U8(NamedShape).U16(o.Count).U16(o.Made).Str(o.Name); // (its material is the name's own)
                    continue;
                }
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
                if (shape == NamedShape)
                {
                    var pc = r.U16();
                    var pm = r.U16();
                    var pn2 = r.Str();
                    if (pc is >= 1 and <= MaxCount && (HullLoft.IsFormedName(pn2) || HullLoft.IsCutName(pn2)) && pn2.Length <= ConstructParts.MaxRuntimeName && HullLoft.FormedMaterial(pn2) != 0)
                        q.Add(ConstructPanelFormer.Order(pn2, pc) with { Made = Math.Min(pm, pc) });
                    else if (pc is >= 1 and <= MaxCount && Pressed.IsName(pn2) && PressedOrder(pn2, pc) is { } pressed)
                        q.Add(pressed with { Made = Math.Min(pm, pc) }); // (a pressed part's order, Press phase 4: a key this build can't read is left out)
                    continue;
                }
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
        public readonly List<WorkOrder> Panels = new(); // formed panels and lofted hull parts, by name, first seen first (a Panel Former's)
        public readonly List<(string Part, ConstructPartDef? Def, int Count)> Parts = new(); // catalogue parts, by name, first seen first (Def null: not a part this build has)
        public readonly List<WorkOrder> Pressed = new(); // pressed parts by their keys, first seen first (a Press's; their blanks are in Pieces, their kits in Kits)
        public readonly List<WorkOrder> Cut = new(); // cut panels and plates by their keys, first seen first (a Laser Cutter's; their blanks are in Panels or Pieces)
        public readonly List<(string What, int Count)> Kits = new(); // bay 2 kits the pressed parts take, by part or material name
        public readonly float[] LinkMetres = new float[(int)RouteType.COUNT]; // link stock by type: metres of every link of that type, as built
        public readonly int[] LinkCount = new int[(int)RouteType.COUNT];
        public string LinksWhy = ""; // why the links weren't counted (the blueprint didn't build), or ""
    }

    /// <summary>
    /// Every part a blueprint needs: its extruded and formed pieces by key, its catalogue parts by part, each with a count, and its link
    /// stock: metres of each link type (wire, power cable, pipe, shaft, chute, ammo feed, conveyor), measured on the blueprint built in a
    /// scratch world (so its links' lengths are the ones the game lays). The building station will issue a blueprint's bill from this.
    /// </summary>
    public static Bill FromBlueprint(ConstructBuild.Blueprint bp)
    {
        var bill = new Bill();
        var parts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            if (Pressed.IsName(step.Part) && global::CallOfSlop.Pressed.SpecOf(step.Part) is { } spec)
            {
                // A pressed part (Press phase 4): a Press's line by its key, its blank an Extruder's line, its kit a tally.
                AddPressed(bill, step.Part, spec);
                continue;
            }
            if (HullLoft.IsFormedName(step.Part))
            {
                AddPanel(bill.Panels, step.Part);
                continue;
            }
            if (HullLoft.IsCutName(step.Part) && ConstructLaserCutter.JobOf(step.Part) is { } cut)
            {
                // A cut part (laser cutter phase 4): a Laser Cutter's line by its key, its blank a Panel Former's or an Extruder's line.
                AddPanel(bill.Cut, step.Part);
                AddBlank(bill, cut);
                continue;
            }
            if (parts.TryGetValue(step.Part, out var n))
            {
                parts[step.Part] = n + 1;
                continue;
            }
            parts[step.Part] = 1;
            bill.Parts.Add((step.Part, ConstructParts.Find(step.Part), 0));
        }
        for (var k = 0; k < bill.Parts.Count; k++)
        {
            bill.Parts[k] = bill.Parts[k] with { Part = bill.Parts[k].Def?.Name ?? bill.Parts[k].Part, Count = parts[bill.Parts[k].Part] };
            if (PresetOf(bill.Parts[k].Def) is { } preset)
                for (var n = 0; n < bill.Parts[k].Count; n++)
                    AddInputs(bill, preset); // (a catalogue part a Press makes: its blank and kit, so the bill is complete down to stock)
        }
        if (bp.Links.Count == 0 && bp.Wires.Count == 0)
            return bill;
        var built = ConstructBuild.Build(new ConstructWorld(), bp, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, out var why);
        if (built == null)
        {
            bill.LinksWhy = why;
            return bill;
        }
        for (var r = 0; r < built.RouteCount; r++)
        {
            var t = (int)built.Routes[r].Type;
            if (t >= bill.LinkMetres.Length)
                continue;
            bill.LinkMetres[t] += ConstructConveyor.Length(built, r);
            bill.LinkCount[t]++;
        }
        return bill;
    }

    private static void AddPanel(List<WorkOrder> list, string name)
    {
        var k = list.FindIndex(o => o.Name == name);
        if (k < 0)
            list.Add(ConstructPanelFormer.Order(name, 1));
        else
            list[k] = list[k] with { Count = (ushort)Math.Min(list[k].Count + 1, WorkOrders.MaxCount) };
    }

    /// <summary>One cut part's blank onto the bill: a formed panel a Panel Former's line, a plate or flat bar an Extruder's.</summary>
    private static void AddBlank(Bill bill, ConstructLaserCutter.Job j)
    {
        if (!j.Plate)
        {
            AddPanel(bill.Panels, j.Blank);
            return;
        }
        var key = j.Stock with { Growing = false };
        var b = bill.Pieces.FindIndex(o => o.Key == key);
        if (b < 0)
            bill.Pieces.Add(new WorkOrder { Key = key, Part = ConstructParts.None, Count = 1 });
        else
            bill.Pieces[b] = bill.Pieces[b] with { Count = (ushort)Math.Min(bill.Pieces[b].Count + 1, WorkOrders.MaxCount) };
    }

    /// <summary>A catalogue part no Assembler makes but a die presses (a preset, Press phase 4), as its press order's spec; else null.</summary>
    public static Pressed.OrderSpec? PresetOf(ConstructPartDef? d) =>
        d != null && RecipeOf(d) == null && PressTable.Current.PresetKeyOf(d) is { } key ? Pressed.SpecOf(key) : null;

    private static void AddPressed(Bill bill, string key, Pressed.OrderSpec spec)
    {
        var k = bill.Pressed.FindIndex(o => o.Name == key);
        if (k < 0)
            bill.Pressed.Add(WorkOrders.PressedOrder(key, 1)!.Value);
        else
            bill.Pressed[k] = bill.Pressed[k] with { Count = (ushort)Math.Min(bill.Pressed[k].Count + 1, WorkOrders.MaxCount) };
        AddInputs(bill, spec);
    }

    /// <summary>One pressed part's blank (an Extruder's line) and bay 2 kit (a tally) onto the bill.</summary>
    private static void AddInputs(Bill bill, Pressed.OrderSpec spec)
    {
        var blank = spec.Blank with { Growing = false };
        var b = bill.Pieces.FindIndex(o => o.Key == blank);
        if (b < 0)
            bill.Pieces.Add(new WorkOrder { Key = blank, Part = ConstructParts.None, Count = 1 });
        else
            bill.Pieces[b] = bill.Pieces[b] with { Count = (ushort)Math.Min(bill.Pieces[b].Count + 1, WorkOrders.MaxCount) };
        if (spec.Pattern.Bay2 is not { } kit || Pressed.Bay2For(spec.Pattern, spec.Blank) is not (var n and > 0))
            return;
        var what = kit.What;
        var at = bill.Kits.FindIndex(x => x.What == what);
        if (at < 0)
            bill.Kits.Add((what, n));
        else
            bill.Kits[at] = (what, bill.Kits[at].Count + n);
    }

    /// <summary>The recipe that makes a catalogue part, or null (the factory can't make it).</summary>
    public static AssembleRecipe? RecipeOf(ConstructPartDef? d) => d == null ? null : AssemblingTable.Current.Of((ushort)d.Id);

    /// <summary>Server: a blueprint's pieces and the catalogue parts a recipe makes onto a Line Console's bill (creative and the probe; the station's later). Null, or why not.</summary>
    public static string? ToConsole(ConstructWorld world, Construct c, int console, ConstructBuild.Blueprint bp)
    {
        if (!ProductionLines.IsConsole(c.DefOf(console)))
            return "that isn't a Line Console";
        var bill = FromBlueprint(bp);
        var any = false;
        foreach (var o in bill.Pieces)
        {
            if (WorkOrders.AddToBill(world, c, console, o) is { } why)
                return why;
            any = true;
        }
        foreach (var o in bill.Panels)
        {
            if (WorkOrders.AddToBill(world, c, console, o) is { } why)
                return why;
            any = true;
        }
        foreach (var o in bill.Pressed)
        {
            if (WorkOrders.AddToBill(world, c, console, o) is { } why)
                return why;
            any = true;
        }
        foreach (var o in bill.Cut)
        {
            if (WorkOrders.AddToBill(world, c, console, o) is { } why)
                return why;
            any = true;
        }
        foreach (var (_, d, n) in bill.Parts)
            if (RecipeOf(d) != null || PresetOf(d) != null || FormerPresets.KeyOf(d) != null) // (a preset is a Press's or a former's order by its catalogue part)
            {
                if (WorkOrders.AddToBill(world, c, console, new WorkOrder { Part = (ushort)d!.Id, Count = (ushort)Math.Min(n, WorkOrders.MaxCount) }) is { } why)
                    return why;
                any = true;
            }
        return any ? null : $"{bp.Name} needs nothing a line makes";
    }

    /// <summary>
    /// A blueprint's <c>"bill"</c> line: <c>{"step": n, "piece": "Extrusion l-angle 20x20x150 metal", "count": 3}</c>, or a catalogue part an
    /// Assembler makes, <c>{"step": n, "part": "Gear Set", "count": 4}</c>: a line on the bill of the Line Console that step builds (the
    /// Production Showcase's). Orders only: a bill holds no material, so survival gets nothing free from it.
    /// </summary>
    public static (int Step, WorkOrder Order) ParseLine(System.Text.Json.JsonElement e, string file, int k)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new ConstructPartException($"{file}: bill[{k}] is {{\"step\", \"piece\" or \"part\", \"count\"}}");
        int step = -1, count = 1;
        string piece = "", part = "", panel = "", pressed = "", cut = "";
        foreach (var f in e.EnumerateObject())
            switch (f.Name)
            {
                case "step": step = f.Value.GetInt32(); break;
                case "piece": piece = f.Value.GetString() ?? ""; break;
                case "part": part = f.Value.GetString() ?? ""; break;
                case "panel": panel = f.Value.GetString() ?? ""; break;
                case "pressed": pressed = f.Value.GetString() ?? ""; break;
                case "cut": cut = f.Value.GetString() ?? ""; break;
                case "count": count = f.Value.GetInt32(); break;
                default: throw new ConstructPartException($"{file}: bill[{k}]: unknown key \"{f.Name}\" (step, piece, part, panel, pressed, cut, count)");
            }
        if (step < 0 || count < 1 || count > WorkOrders.MaxCount)
            throw new ConstructPartException($"{file}: bill[{k}]: a step and a count 1 to {WorkOrders.MaxCount}");
        if ((piece.Length > 0 ? 1 : 0) + (part.Length > 0 ? 1 : 0) + (panel.Length > 0 ? 1 : 0) + (pressed.Length > 0 ? 1 : 0) + (cut.Length > 0 ? 1 : 0) != 1)
            throw new ConstructPartException($"{file}: bill[{k}]: a \"piece\", a \"part\", a \"panel\", a \"pressed\" or a \"cut\", one of them");
        if (cut.Length > 0)
        {
            // A cut part by its key (laser cutter phase 4): a Laser Cutter's line.
            if (!HullLoft.IsCutName(cut) || HullLoft.CutRefusal(cut) is { } why)
                throw new ConstructPartException($"{file}: bill[{k}]: \"{cut}\" isn't a part a Laser Cutter cuts{(HullLoft.IsCutName(cut) ? $" ({HullLoft.CutRefusal(cut)})" : "")}, like \"Cut Panel (door 110x120 c6 r8 t20 steel | r70,60,40,50,5)\"");
            return (step, ConstructPanelFormer.Order(cut, count));
        }
        if (pressed.Length > 0)
        {
            // A pressed part by its key (Press phase 4): a Press's line.
            if (!Pressed.IsName(pressed) || WorkOrders.PressedOrder(pressed, count) is not { } po)
            {
                Pressed.TryParse(pressed, out _, out _, out _, out var why);
                throw new ConstructPartException($"{file}: bill[{k}]: \"{pressed}\" isn't a part a Press makes ({why}), like \"Pressed Part (bracket.l l-angle 15x15x20 steel)\"");
            }
            return (step, po);
        }
        if (panel.Length > 0)
        {
            if (!HullLoft.IsFormedName(panel) || ConstructParts.Find(panel) == null)
                throw new ConstructPartException($"{file}: bill[{k}]: \"{panel}\" isn't a panel a Panel Former makes, like \"{HullLoft.PanelKey(HullLoft.PanelDefault(2))}\"");
            return (step, ConstructPanelFormer.Order(panel, count));
        }
        if (part.Length > 0)
        {
            if (ConstructParts.Find(part) is not { } d || d.Id >= Extrusion.RuntimeBase || d.Name.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase))
                throw new ConstructPartException($"{file}: bill[{k}]: no catalogue part named \"{part}\"");
            return (step, new WorkOrder { Part = (ushort)d.Id, Count = (ushort)count });
        }
        if (Extrusion.ParseKey(piece) is not { Growing: false } key || key.Shape is not (ExShape.Section or ExShape.Round or ExShape.Pipe or ExShape.Wheel or ExShape.Fan or ExShape.Wing))
            throw new ConstructPartException($"{file}: bill[{k}]: \"{piece}\" isn't a piece an Extruder or a Wheel, Fan or Airframe Former makes, like \"Extrusion i-beam 25x25x400 metal\"");
        return (step, new WorkOrder { Key = key, Part = ConstructParts.None, Count = (ushort)count });
    }

    /// <summary>A blueprint's bill line onto the console its step built (<see cref="ConstructBuild.FinishBuild"/>). Null, or why not.</summary>
    public static string? FromLine(ConstructWorld world, Construct c, (int Step, WorkOrder Order) line, ReadOnlySpan<int> partOf)
    {
        if (line.Step >= partOf.Length || partOf[line.Step] < 0 || !ProductionLines.IsConsole(c.DefOf(partOf[line.Step])))
            return $"step {line.Step} isn't a Line Console";
        return WorkOrders.AddToBill(world, c, partOf[line.Step], line.Order);
    }

    /// <summary>
    /// <c>--bill NAME[|NAME...]</c>: a blueprint's bill as text, grouped: extruded pieces, formed pieces, catalogue parts by category (each with
    /// the recipe that makes it and from what tier, or "no recipe": the factory can't make those), then link stock by type; totals last.
    /// </summary>
    public static int Cli(string names)
    {
        var bad = 0;
        foreach (var name in names.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ConstructBuild.FindBlueprint(name) is not { } bp)
            {
                Console.WriteLine($"no blueprint named \"{name}\"");
                bad++;
                continue;
            }
            Console.Write(Text(bp));
        }
        return bad;
    }

    private static readonly string[] LinkNames = ["wire", "power cable", "pipe", "shaft", "chute", "ammo feed", "conveyor"];

    /// <summary>A blueprint's bill as the <c>--bill</c> text.</summary>
    public static string Text(ConstructBuild.Blueprint bp)
    {
        var bill = FromBlueprint(bp);
        var sb = new StringBuilder();
        int panelNone = 0, panelNoneParts = 0;
        var extruded = bill.Pieces.Where(o => o.Key.Shape is ExShape.Section or ExShape.Round or ExShape.Pipe).ToList();
        var formed = bill.Pieces.Where(o => o.Key.Shape is not (ExShape.Section or ExShape.Round or ExShape.Pipe)).ToList();
        sb.AppendLine($"{bp.Name}");
        if (extruded.Count > 0)
        {
            sb.AppendLine($" Extruded: {extruded.Count} kinds, {extruded.Sum(o => o.Count)} pieces (an Extruder)");
            foreach (var o in extruded)
                sb.AppendLine($"  {o.Count,4} x {Extrusion.Name(o.Key)}  ({Extrusion.Label(o.Key)})");
        }
        if (formed.Count > 0)
        {
            sb.AppendLine($" Formed: {formed.Count} kinds, {formed.Sum(o => o.Count)} pieces");
            foreach (var o in formed)
                sb.AppendLine($"  {o.Count,4} x {Extrusion.Name(o.Key)}  ({Extrusion.Label(o.Key)}; {WorkOrders.Maker(o.Key)}{(o.Key.Shape is ExShape.Wheel ? "" : ": no orders yet")})");
        }
        if (bill.Panels.Count > 0)
        {
            sb.AppendLine($" Formed panels: {bill.Panels.Count} kinds, {bill.Panels.Sum(o => o.Count)} panels (a Panel Former)");
            foreach (var o in bill.Panels)
            {
                var pd = ConstructParts.Find(o.Name!);
                var how = pd == null ? "not a part this build makes" : ConstructPanelFormer.TierFor(pd) is { } tier
                    ? $"{tier}: {HullLoft.FormedUnits(pd, o.Key.Material)} {RecyclingTable.Current.NameOf(o.Key.Material)}, {ConstructPanelFormer.Seconds(pd):0} s"
                    : "no recipe: too large for any Panel Former";
                if (pd == null || ConstructPanelFormer.TierFor(pd) == null)
                {
                    panelNone++;
                    panelNoneParts += o.Count;
                }
                sb.AppendLine($"  {o.Count,4} x {o.Name}  ({how})");
            }
        }
        if (bill.Pressed.Count > 0)
        {
            // Pressed parts by key (Press phase 4): die and pattern, the press class, the blank (counted under Extruded) and the kit.
            sb.AppendLine($" Pressed: {bill.Pressed.Count} kinds, {bill.Pressed.Sum(o => o.Count)} parts (a Press)");
            foreach (var o in bill.Pressed)
            {
                var spec = Pressed.SpecOf(o.Name!)!;
                var across = Pressed.Across(spec.Blank);
                var kit = spec.Pattern.Bay2 is { } a && Pressed.Bay2For(spec.Pattern, spec.Blank) is var n and > 0 ? $", bay 2: {n} {a.What}" : "";
                sb.AppendLine($"  {o.Count,4} x {o.Name}  ({spec.Die.Family} Die, {spec.Pattern.Label}; a {Pressed.ClassWord(Pressed.PressClass(across))} Press; blank {Pressed.BlankText(spec.Blank)}{kit}; {Pressed.Seconds(spec.Die, spec.Blank, Math.Max(0, Pressed.PressClass(across))):0.#} s)");
            }
        }
        if (bill.Cut.Count > 0)
        {
            // Cut parts by key (laser cutter phase 4): the cutter that takes each, its blank (counted above), the stroke and the offcut returned.
            sb.AppendLine($" Cut panels: {bill.Cut.Count} kinds, {bill.Cut.Sum(o => o.Count)} parts (a Laser Cutter)");
            foreach (var o in bill.Cut)
            {
                if (ConstructLaserCutter.JobOf(o.Name!) is not { } j)
                {
                    sb.AppendLine($"  {o.Count,4} x {o.Name}  (no part this build cuts)");
                    continue;
                }
                var tier = ConstructLaserCutter.TierFor(j);
                var cutter = tier != null ? ConstructParts.Find(tier) : null;
                var secs = cutter != null ? $"{ConstructLaserCutter.Seconds(j, cutter):0.#} s" : "no cutter's bed takes it";
                sb.AppendLine($"  {o.Count,4} x {o.Name}  ({tier ?? "no Laser Cutter"}; blank {j.Blank}; {secs}; offcut {j.OffTenths / 10f:0.#} {RecyclingTable.Current.NameOf(j.Material)})");
            }
        }
        if (bill.Kits.Count > 0)
            sb.AppendLine($" Kits (bay 2):{string.Join(", ", bill.Kits.Select(x => $"{x.Count} {x.What}"))}");
        var cats = PartCategories.Current;
        var groups = bill.Parts
            .Select(p => (p.Part, p.Def, p.Count, Cat: p.Def != null && cats.Place(p.Def, out var cat, out _) ? cat : "(unplaced)"))
            .GroupBy(p => p.Cat).OrderBy(g => g.Key, StringComparer.Ordinal);
        int kinds = 0, parts = 0, none = 0, noneParts = 0;
        sb.AppendLine($" Catalogue: {bill.Parts.Count} kinds, {bill.Parts.Sum(p => p.Count)} parts");
        foreach (var g in groups)
        {
            sb.AppendLine($"  {g.Key}:");
            foreach (var (part, def, n, _) in g.OrderByDescending(p => p.Count).ThenBy(p => p.Part, StringComparer.Ordinal))
            {
                kinds++;
                parts += n;
                var r = RecipeOf(def);
                var pressedBy = PresetOf(def);
                var formedAs = FormerPresets.KeyOf(def);
                if (r == null && pressedBy == null && formedAs == null)
                {
                    none++;
                    noneParts += n;
                }
                var how = def == null ? "not a part of this build" : pressedBy != null ? $"pressed ({pressedBy.Die.Family} Die, blank {Pressed.BlankText(pressedBy.Blank)})" : formedAs is { } fa ? $"formed ({(fa.Shape == ExShape.Fan ? "Fan Former" : "Airframe Former")}, {Extrusion.Name(fa)})" : r == null ? "no recipe" :$"{WorkOrders.FamilyName(r.Machine)} tier {r.MinTier}: {r.Batch}{(r.Count > 1 ? $" makes {r.Count}" : "")}";
                sb.AppendLine($"   {n,4} x {part}  ({how})");
            }
        }
        sb.AppendLine(" Links:");
        if (bill.LinksWhy.Length > 0)
            sb.AppendLine($"   not counted: the blueprint didn't build ({bill.LinksWhy})");
        for (var t = 0; t < bill.LinkMetres.Length; t++)
            if (bill.LinkCount[t] > 0)
                sb.AppendLine($"   {bill.LinkMetres[t],8:0.0} m of {(t < LinkNames.Length ? LinkNames[t] : ((RouteType)t).ToString())} ({bill.LinkCount[t]} links)");
        var pieces = bill.Pieces.Sum(o => o.Count);
        var panels = bill.Panels.Sum(o => o.Count);
        var pressedN = bill.Pressed.Sum(o => o.Count);
        sb.AppendLine($" Total: {pieces + panels + parts + pressedN + bill.Cut.Sum(o => o.Count)} parts ({pieces} pieces in {bill.Pieces.Count} kinds, {panels} formed panels in {bill.Panels.Count} kinds, {pressedN} pressed in {bill.Pressed.Count} kinds, {bill.Cut.Sum(o => o.Count)} cut in {bill.Cut.Count} kinds, {parts} catalogue in {kinds} kinds; the pieces and formed panels hold the pressed and cut parts' blanks); no recipe: {none + panelNone} kinds, {noneParts + panelNoneParts} parts; links {bill.LinkMetres.Sum():0.0} m");
        return sb.ToString();
    }
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
