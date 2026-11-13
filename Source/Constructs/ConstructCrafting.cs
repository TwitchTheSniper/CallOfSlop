using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a Crafting Terminal's order stands (on the wire: a byte; new ones at the end).</summary>
public enum CraftState : byte
{
    Waiting, // nothing claimed or asked yet
    Fetching, // claimed in a store, leaving it for the tray
    Queued, // asked of a machine
    Riding, // every part is on its way
    InTray, // all of it is in the tray
    Collect, // claimed in a store with no route to the tray: the player collects it there
    Held, // can't go on: the note says why
}

/// <summary>One request at a Crafting Terminal: what, how many, and how far it has come.</summary>
public struct CraftOrder
{
    public WorkOrder What; // a catalogue part or a piece's key, and the count asked (Made unused)
    public ushort Id; // its number at this terminal (never 0)
    public ushort Got; // in the tray (or collected by hand from a store with no route)
    public ushort Sent; // left a store, riding to the tray
    public ushort Asked; // asked of machines and not seen yet
    public CraftState State;
    public ushort Where; // the part it waits on (a store or a machine of this body; 0xFFFF: none)
    public string Note; // Held: why
    public ushort Age; // steps since it was done (server)
    public bool Cancelled; // server: cancelled with parts already riding: they arrive anyway, and it asks for nothing more
    public bool Loose; // server: a machine with no conveyor dropped its parts beside itself, and they counted there (MadeLoose)
    public ushort Def; // server: the kind's id here (a run-time piece's is this machine's own); None until worked out
    public byte Lost; // server: why a part for it last went to storage instead (a LogiReason; its row says so until it is done)
    public uint Stamp; // server: its age (ConstructWorld.OrderStamp when first stepped): roots compete by class, then age (not saved: stamped again in order)
    public ChainLine[]? Lines; // its chained inputs' lines (both peers: made on the server when they change, sent in CBodyKind.Craft)
    public byte LinesMore; // ...and how many more links than the lines show

    public readonly int Left => Math.Max(0, What.Count - Got);
    public readonly bool Done => Got >= What.Count;
}

/// <summary>A Crafting Terminal's open requests, on its part (<see cref="CPart.Craft"/>), so they move with it.</summary>
public sealed class CraftList
{
    public CraftOrder[] Items = new CraftOrder[ConstructCrafting.MaxOrders];
    public int Count;
    public ushort NextId = 1;

    public int Find(ushort id)
    {
        for (var k = 0; k < Count; k++)
            if (Items[k].Id == id)
                return k;
        return -1;
    }

    public void RemoveAt(int k)
    {
        Array.Copy(Items, k + 1, Items, k, Count - k - 1);
        Count--;
    }
}

/// <summary>What a Crafting Terminal is connected to, worked out when its body's links change (<see cref="ConstructCrafting.NetOf"/>).</summary>
public sealed class CraftNet
{
    public const int MaxMachines = 64, MaxStores = 64, MaxRefiners = 16;

    public uint Key; // the body's links as they were (0: never worked out)
    public readonly int[] Machines = new int[MaxMachines]; // parts that take orders
    public int MachineN;
    public readonly int[] Stores = new int[MaxStores]; // parts that hold stock (racks, shelves, chests, volumes, bins)
    public readonly short[] Via = new short[MaxStores]; // ...how a part leaves it for the tray: a conveyor's index, -2 a chute, -1 no route
    public int StoreN;
    public readonly int[] Refiners = new int[MaxRefiners];
    public int RefinerN;
    public int Consoles;
}

/// <summary>A row of a terminal's list: something its network holds or can make.</summary>
public sealed class CraftRow
{
    public WorkOrder What; // Count 1
    public ConstructPartDef? Def; // the part (a piece: its run-time def once one exists here)
    public string Name = "";
    public int Stock; // in linked stores, unreserved
    public byte Make; // 0: nobody makes it; 1: now; 2: through a chain; 3: later (inputs missing)
    public string Missing = ""; // 2, 3: what is missing
    public int Machine = -1; // the machine that would make it
    public AssembleRecipe? Recipe;
}

/// <summary>
/// The Crafting Terminal (docs/sandbox/constructs/production.md "Crafting terminal"): a desk whose list is everything its network
/// holds or can make, and whose orders fetch unreserved stock from linked stores first (<see cref="ConstructReserve"/>), then
/// ask the linked machines for the rest (<see cref="WorkOrders"/>); the parts come to its tray, which is a Piece Rack's hold.
/// The network: everything joined to the terminal by intact conveyors and chutes, either way round, and the machines of every
/// Line Console wired into its Orders port. The list is worked out on either peer from stores, links and queues; the server
/// alone places, steps and cancels orders. Nothing runs per tick: a 2 Hz step walks only bodies with open orders.
/// </summary>
public static class ConstructCrafting
{
    public const int MaxOrders = 32; // open requests a terminal
    public const int StepEvery = 30; // ticks between steps (2 Hz)
    public const int DoneSteps = 120; // a finished order stays listed this long (60 s)
    private const ushort NoPart = ushort.MaxValue;

    // ---- The network -----------------------------------------------------------------------------------------------------

    /// <summary>A hash of what the network follows from: the body's links, its part count and the terminal's place.</summary>
    private static uint KeyOf(Construct c, int t)
    {
        var h = 2166136261u ^ (uint)c.PartCount * 16777619u ^ (uint)t;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type is not (RouteType.Conveyor or RouteType.Chute or RouteType.Data))
                continue;
            h = (h ^ (uint)r.Type) * 16777619u;
            h = (h ^ r.Source) * 16777619u;
            h = (h ^ r.To) * 16777619u;
            h = (h ^ (r.FedEnd ? 1u : 0u)) * 16777619u;
        }
        return h == 0 ? 1 : h;
    }

    /// <summary>The terminal's network, from its cache when the body's links are as they were. Either peer. Allocates the first time only.</summary>
    public static CraftNet NetOf(ConstructWorld world, Construct c, int t)
    {
        var handle = c.Parts[t].Store;
        if (!world.CraftNets.TryGetValue(handle, out var net))
            world.CraftNets[handle] = net = new CraftNet();
        var key = KeyOf(c, t);
        if (net.Key == key)
            return net;
        net.Key = key;
        Build(c, t, net);
        return net;
    }

    private static bool IsStore(ConstructPartDef d) =>
        d.Role == PartRole.Storage && !d.IsCraftTerminal && !d.IsAssembler && !d.IsRefiner && !d.IsExtruder && !d.IsPress && !d.IsBeacon && !d.IsTerminal && !d.IsGunBench
        && !d.IsRecycler && !d.IsGatherer;

    private static void Build(Construct c, int t, CraftNet net)
    {
        net.MachineN = net.StoreN = net.RefinerN = net.Consoles = 0;
        Span<int> seen = stackalloc int[256];
        var n = 0;
        seen[n++] = t;
        // The Line Consoles wired into its Orders port bring their machines.
        Span<int> line = stackalloc int[32];
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.To != t || !r.FedEnd || r.Source >= c.PartCount || !ProductionLines.IsConsole(c.DefOf(r.Source)))
                continue;
            net.Consoles++;
            var m = ProductionLines.Machines(c, r.Source, line);
            for (var j = 0; j < m; j++)
                if (n < seen.Length && !seen[..n].Contains(line[j]))
                    seen[n++] = line[j];
        }
        // Then everything joined by intact conveyors and chutes, either way round.
        for (var q = 0; q < n; q++)
        {
            var at = seen[q];
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref readonly var r = ref c.Routes[k];
                if (r.Type is not (RouteType.Conveyor or RouteType.Chute) || !r.FedEnd || r.Source >= c.PartCount || r.To >= c.PartCount)
                    continue;
                var other = r.Source == at ? r.To : r.To == at ? r.Source : -1;
                if (other >= 0 && n < seen.Length && !seen[..n].Contains(other))
                    seen[n++] = other;
            }
        }
        for (var q = 1; q < n; q++)
        {
            var d = c.DefOf(seen[q]);
            if (WorkOrders.TakesOrders(d))
            {
                if (net.MachineN < CraftNet.MaxMachines)
                    net.Machines[net.MachineN++] = seen[q];
            }
            else if (d.IsRefiner)
            {
                if (net.RefinerN < CraftNet.MaxRefiners)
                    net.Refiners[net.RefinerN++] = seen[q];
            }
            else if (IsStore(d) && c.Parts[seen[q]].Store != 0 && net.StoreN < CraftNet.MaxStores)
            {
                net.Via[net.StoreN] = RouteTo(c, seen[q], t);
                net.Stores[net.StoreN++] = seen[q];
            }
        }
    }

    /// <summary>How a part leaves store <paramref name="s"/> for terminal <paramref name="t"/>: its conveyor (through joins), -2 a chute straight in, -1 none.</summary>
    internal static short RouteTo(Construct c, int s, int t)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Source != s || !r.FedEnd)
                continue;
            if (r.Type == RouteType.Chute && r.To == t)
                return -2;
            if (r.Type != RouteType.Conveyor)
                continue;
            int to = r.To;
            for (var hop = 0; hop < 8 && to != t; hop++)
            {
                if (to < c.PartCount && c.DefOf(to).IsRouter && ConstructLogistics.Reaches(c, to, t))
                {
                    to = t; // (a Router sends a fetched piece on to its tray: ConstructLogistics)
                    break;
                }
                // Through a join (one conveyor out); a splitter sends pieces in turn, so it is no route.
                var next = -1;
                var outs = 0;
                for (var j = 0; j < c.RouteCount; j++)
                    if (c.Routes[j].Type == RouteType.Conveyor && c.Routes[j].Source == to && c.Routes[j].FedEnd)
                    {
                        outs++;
                        next = c.Routes[j].To;
                    }
                if (outs != 1 || to >= c.PartCount || !c.DefOf(to).IsConveyorJunction)
                    break;
                to = next;
            }
            if (to == t)
                return (short)k;
        }
        return -1;
    }

    /// <summary>"3 machines, 5 stores" (and its consoles): what the terminal sees, for its look line.</summary>
    public static string Sees(CraftNet net)
    {
        var s = $"{net.MachineN} machine{(net.MachineN == 1 ? "" : "s")}, {net.StoreN} store{(net.StoreN == 1 ? "" : "s")}";
        return net.Consoles > 0 ? $"{s}, {net.Consoles} console{(net.Consoles == 1 ? "" : "s")}" : s;
    }

    /// <summary>The terminal's look line: what it sees, its tray and its open orders.</summary>
    public static string Look(ConstructWorld world, Construct c, int t, CStore st, bool near)
    {
        var d = c.DefOf(t);
        if (d.IsBuildStation)
            return ConstructStation.Look(world, c, t, st, near);
        var net = NetOf(world, c, t);
        var sb = new System.Text.StringBuilder(d.Name).Append(": ").Append(Sees(net));
        if (net.MachineN + net.StoreN == 0)
            sb.Append(" (link conveyors or chutes to it, or a Line Console's wire into Orders)");
        sb.Append(" - tray ").Append(st.ItemCount == 0 ? "empty" : $"{st.ItemCount} part{(st.ItemCount == 1 ? "" : "s")}");
        if (c.Parts[t].Craft is { Count: > 0 } q)
            sb.Append($", {q.Count} order{(q.Count == 1 ? "" : "s")}");
        if (near)
            sb.Append(st.ItemCount > 0 ? $" - {SandboxKeys.Use} on the tray: take a part - {SandboxKeys.Use} on the screen: order" : $" - {SandboxKeys.Use}: order a part");
        return sb.ToString();
    }

    // ---- Stock -----------------------------------------------------------------------------------------------------------

    /// <summary>How many of a kind a store holds (a rack's or a volume's kinds, a shelf's slots).</summary>
    public static int Holds(CStore st, ushort def)
    {
        var n = 0;
        for (var k = 0; k < st.KindN; k++)
            if (st.KindDef![k] == def)
                n += st.KindCount![k];
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == def)
                n += st.Count[k];
        return n;
    }

    /// <summary>A kind in a store that nobody has claimed.</summary>
    public static int Available(ConstructWorld world, CStore st, ushort def) => Math.Max(0, Holds(st, def) - world.Reserve.Reserved(st.Handle, def));

    /// <summary>Unreserved stock of a kind across the network.</summary>
    public static int Stock(ConstructWorld world, Construct c, CraftNet net, ushort def)
    {
        var n = 0;
        for (var i = 0; i < net.StoreN; i++)
            if (world.StoreOf(c, net.Stores[i]) is { } st)
                n += Available(world, st, def);
        return n;
    }

    /// <summary>A loose material across the network's bins, and in machine <paramref name="m"/>'s own input (-1: none).</summary>
    public static int Bulk(ConstructWorld world, Construct c, CraftNet net, byte material, int m)
    {
        var n = 0;
        for (var i = 0; i < net.StoreN; i++)
            if (world.StoreOf(c, net.Stores[i]) is { Amount: > 0 } st && st.Material == material)
                n += st.Amount;
        if (m >= 0 && world.StoreOf(c, m) is { } ms)
            n += c.DefOf(m).IsAssembler ? ms.MixOf(material) : ms.Material == material ? ms.Amount : 0;
        return n;
    }

    internal static bool TakeOne(ConstructWorld world, Construct c, int part, CStore st, ConstructPartDef d)
    {
        var sd = c.DefOf(part);
        if (sd.IsPieceRack)
        {
            ConstructRackPack.Ensure(sd, st);
            return ConstructRackPack.LastOf(st, (ushort)d.Id, out var slab, out var lane) && world.RackTakeAt(c, part, slab, lane) != null;
        }
        if (sd.IsVolumeStore)
            return world.TakePart(c, part, (ushort)d.Id) != null;
        return SlotStore.Instance.TakePart(world, c, part, d);
    }

    /// <summary>An order's kind as a def here (a piece's def is made the first time its key is asked for), or null.</summary>
    public static ConstructPartDef? DefOf(in WorkOrder o) => o.Name != null ? (HullLoft.IsFormedName(o.Name) ? ConstructParts.Find(o.Name) : null) : o.IsPiece ? Extrusion.Def(o.Key) : ConstructParts.Get(o.Part);

    /// <summary>"Gear Set", "L-angle 0.2 x 0.2 m, 1 m, metal".</summary>
    public static string NameOf(in WorkOrder o)
    {
        if (o.Name != null)
            return HullLoft.PanelLabel(o.Name);
        if (!o.IsPiece)
            return ConstructParts.Get(o.Part)?.Name ?? "part";
        var k = o.Key;
        var mat = RecyclingTable.Current.NameOf(k.Material);
        return k.Shape == ExShape.Wheel ? $"{Extrusion.Label(k)}, {mat}" : $"{Extrusion.Label(k)}, {k.L / 100f:0.##} m, {mat}";
    }

    // ---- The list (either peer) ------------------------------------------------------------------------------------------

    private static readonly int[] StdProfiles = [0, 3, 4, 6]; // solid bar, hollow box, I-beam, L-angle
    private static readonly ushort[] StdSizes = [10, 20], StdLengths = [100, 200, 400];

    /// <summary>
    /// The terminal's list: what its stores hold unreserved, every recipe its Assemblers' families make, a few standard sizes
    /// from each Extruder in the material it is fed and each Wheel Former's first wheel. Made when a panel asks (it allocates
    /// its rows and texts: never in a step).
    /// </summary>
    public static void Catalogue(ConstructWorld world, Construct c, int t, List<CraftRow> rows)
    {
        rows.Clear();
        var net = NetOf(world, c, t);
        var byKey = new Dictionary<(ushort, ExtrusionKey, string?), CraftRow>();
        CraftRow Row(in WorkOrder what, ConstructPartDef? d)
        {
            var key = (what.Part, what.IsPiece ? what.Key : default, what.Name);
            if (!byKey.TryGetValue(key, out var row))
            {
                byKey[key] = row = new CraftRow { What = what with { Count = 1, Made = 0 }, Def = d, Name = NameOf(what) };
                rows.Add(row);
            }
            return row;
        }
        // What is on the shelves.
        for (var i = 0; i < net.StoreN; i++)
        {
            if (world.StoreOf(c, net.Stores[i]) is not { } st)
                continue;
            void Kind(ushort def)
            {
                if (ConstructParts.Get(def) is not { } d)
                    return;
                var named = Extrusion.IsRuntimeId(def) && d.ExKey.W == 0;
                if (named && !HullLoft.IsFormedName(d.Name))
                    return; // (another named run-time part: nothing here orders those)
                var what = named ? ConstructPanelFormer.Order(d.Name, 1) : Extrusion.IsRuntimeId(def) ? new WorkOrder { Key = d.ExKey, Part = ConstructParts.None } : new WorkOrder { Part = def };
                var row = Row(what, d);
                row.Def ??= d;
                row.Stock = Stock(world, c, net, def); // (the whole network's, so a kind in two stores isn't added twice)
            }
            for (var k = 0; k < st.KindN; k++)
                Kind(st.KindDef![k]);
            for (var k = 0; k < st.SlotCount; k++)
                if (st.Def[k] != ConstructParts.None)
                    Kind(st.Def[k]);
        }
        // What the machines make.
        var names = RecyclingTable.Current;
        for (var i = 0; i < net.MachineN; i++)
        {
            var m = net.Machines[i];
            var md = c.DefOf(m);
            if (md.IsAssembler)
            {
                foreach (var r in AssemblingTable.Current.ByMachine[md.AsmMachine])
                {
                    if (r.MinTier > md.Tier)
                        continue;
                    var row = Row(new WorkOrder { Part = (ushort)r.Output.Id }, r.Output);
                    if (row.Make == 1)
                        continue;
                    var missing = Missing(world, c, net, m, r, out var chain);
                    var make = (byte)(missing.Length == 0 ? 1 : chain ? 2 : 3);
                    if (row.Make == 0 || make < row.Make)
                    {
                        row.Make = make;
                        row.Missing = missing;
                        row.Machine = m;
                        row.Recipe = r;
                    }
                }
                continue;
            }
            var st = world.StoreOf(c, m);
            var fed = st is { Amount: > 0 };
            var mat = fed ? st!.Material : FirstMaterial(md);
            if (md.IsPanelFormer)
            {
                // A Panel Former: each preset at its own size and the world's shared designs, in what it is fed (steel when empty).
                var sheet = fed ? names.NameOf(st!.Material) : "steel";
                void Panel(string name, string label)
                {
                    var what = ConstructPanelFormer.Order(name, 1);
                    if (ConstructParts.Find(name) is not { } pd || WorkOrders.Refusal(world, c, m, what) != null)
                        return;
                    var row = Row(what, pd);
                    if (label.Length > 0)
                        row.Name = label;
                    if (row.Make == 1)
                        return;
                    var units = HullLoft.FormedUnits(pd, what.Key.Material);
                    row.Machine = m;
                    row.Make = (byte)(fed && st!.Amount >= units ? 1 : Bulk(world, c, net, what.Key.Material, -1) > 0 ? 2 : 3);
                    row.Missing = row.Make == 1 ? "" : $"needs {units} {names.NameOf(what.Key.Material)} sheet in the {md.Name}{(fed ? $", holds {st!.Amount}" : "")}";
                }
                for (var k = 0; k < HullLoft.PanelPresets.Length; k++)
                    Panel(HullLoft.PanelKey(HullLoft.PanelDefault(k, sheet)), "");
                foreach (var dsg in world.Designs)
                    Panel(dsg.Key, $"{dsg.Name} ({HullLoft.PanelLabel(dsg.Key)})");
                continue;
            }
            void Piece(in ExtrusionKey key)
            {
                var what = new WorkOrder { Key = key, Part = ConstructParts.None, Count = 1 };
                if (WorkOrders.Refusal(world, c, m, what) != null)
                    return;
                var row = Row(what, null);
                if (row.Make == 1)
                    return;
                row.Machine = m;
                row.Make = (byte)(fed ? 1 : Bulk(world, c, net, mat, -1) > 0 ? 2 : 3);
                row.Missing = fed ? "" : row.Make == 2 ? $"the {md.Name} needs {names.NameOf(mat)}: a linked bin holds some" : $"needs {names.NameOf(mat)} in the {md.Name}";
            }
            if (md.IsWheelFormer)
                Piece(new ExtrusionKey(0, (ushort)MathF.Round(md.ExtWheelStart.X * 100), (ushort)MathF.Round(md.ExtWheelStart.Y * 100), 0, mat, false, ExShape.Wheel));
            else
                foreach (var p in StdProfiles)
                    foreach (var s in StdSizes)
                        foreach (var l in StdLengths)
                            Piece(new ExtrusionKey(Extrusion.Presets[p].Mask, s, s, l, mat, false));
        }
    }

    private static byte FirstMaterial(ConstructPartDef d)
    {
        for (byte m = 1; m <= RecyclingTable.Current.Materials.Length; m++)
            if (d.IsWheelFormer ? ExtrusionTable.Current.FindWheel(m) != null : ExtrusionTable.Current.Find(m) != null)
                return m;
        return 1;
    }

    /// <summary>What a batch of a recipe on machine <paramref name="m"/> lacks ("4 steel, 1 Gear Set"), empty when nothing; <paramref name="chain"/>: every lack is itself made by a linked machine or refiner.</summary>
    private static string Missing(ConstructWorld world, Construct c, CraftNet net, int m, AssembleRecipe r, out bool chain)
    {
        chain = true;
        System.Text.StringBuilder? sb = null;
        var names = RecyclingTable.Current;
        var ms = world.StoreOf(c, m);
        foreach (var (mat, units) in r.Materials)
        {
            if (Bulk(world, c, net, mat, m) >= units)
                continue;
            var made = RefinerMakes(c, net, mat);
            chain &= made != null;
            (sb ??= new()).Append(sb.Length > 0 ? ", " : "").Append(units).Append(' ').Append(names.NameOf(mat));
            if (made != null)
                sb.Append(" (the ").Append(made).Append(" can make it)");
        }
        foreach (var (part, count) in r.Parts)
        {
            var have = Stock(world, c, net, (ushort)part.Id) + (ms != null ? Holds(ms, (ushort)part.Id) : 0);
            if (have >= count)
                continue;
            var made = MachineMakes(c, net, (ushort)part.Id);
            chain &= made != null;
            (sb ??= new()).Append(sb.Length > 0 ? ", " : "").Append(count).Append(' ').Append(part.Name);
            if (made != null)
                sb.Append(" (the ").Append(made).Append(" can make it)");
        }
        return sb == null ? "" : "needs " + sb;
    }

    private static string? RefinerMakes(Construct c, CraftNet net, byte material)
    {
        for (var i = 0; i < net.RefinerN; i++)
        {
            var d = c.DefOf(net.Refiners[i]);
            foreach (var r in RefiningTable.Current.Recipes)
                if (r.Output == material && r.Machine == d.RefineMachine && r.MinTier <= d.Tier)
                    return d.Name;
        }
        return null;
    }

    private static string? MachineMakes(Construct c, CraftNet net, ushort part)
    {
        if (AssemblingTable.Current.Of(part) is not { } r)
            return null;
        for (var i = 0; i < net.MachineN; i++)
        {
            var d = c.DefOf(net.Machines[i]);
            if (d.IsAssembler && d.AsmMachine == r.Machine && r.MinTier <= d.Tier)
                return d.Name;
        }
        return null;
    }

    /// <summary>A stamp that changes when a list made by <see cref="Catalogue"/> may have: the links, the stores' and machines' versions, the claims.</summary>
    public static int Stamp(ConstructWorld world, Construct c, int t)
    {
        var net = NetOf(world, c, t);
        var h = (int)net.Key;
        for (var i = 0; i < net.StoreN; i++)
            h = h * 31 + (world.StoreOf(c, net.Stores[i])?.Version ?? 0);
        for (var i = 0; i < net.MachineN; i++)
            h = h * 31 + (world.StoreOf(c, net.Machines[i])?.Version ?? 0);
        return (h * 31 + world.Reserve.Count) * 31 + world.DesignStamp; // (and the shared designs: a former lists them)
    }

    /// <summary>An order's state in words, for the Orders tab (either peer).</summary>
    public static string StateText(Construct c, in CraftOrder o)
    {
        var where = o.Where < c.PartCount ? "the " + c.DefOf(o.Where).Name : "a linked machine";
        return o.State switch
        {
            CraftState.Fetching => $"fetching from {where}",
            CraftState.Queued => string.IsNullOrEmpty(o.Note) ? $"queued at {where}" : $"queued at {where}: {o.Note}",
            CraftState.Riding => string.IsNullOrEmpty(o.Note) ? "riding to the tray" : o.Note,
            CraftState.InTray => string.IsNullOrEmpty(o.Note) ? "in the tray" : o.Note,
            CraftState.Collect => string.IsNullOrEmpty(o.Note) ? $"no route from {where}: collect it there" : o.Note,
            CraftState.Held => string.IsNullOrEmpty(o.Note) ? "held" : o.Note,
            _ => "waiting",
        };
    }

    private static readonly string[] StateWords = ["waiting", "fetching", "queued", "riding", "in tray", "collect", "held"];

    /// <summary>The terminal's own screen (panel source <c>craft</c>): what it sees and its first orders. Either peer; allocates nothing.</summary>
    public static void FillPanel(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        var d = c.DefOf(part);
        if (!d.IsCraftTerminal || world.StoreOf(c, part) is not { } st)
            return;
        if (!ConstructRecycler.Powered(world, c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, d.Name);
            card.Add(1, c.Parts[part].Broken ? "Wrecked" : "No power");
            return;
        }
        var net = NetOf(world, c, part);
        var q = c.Parts[part].Craft;
        card.State = q is { Count: > 0 } ? PanelState.Working : PanelState.Ok;
        card.Add(0, "CRAFTING");
        card.Add(1, net.MachineN).Add(1, net.MachineN == 1 ? " machine" : " machines");
        card.Add(2, net.StoreN).Add(2, net.StoreN == 1 ? " store" : " stores");
        card.Add(3, "Tray: ").Add(3, st.ItemCount).Add(3, st.ItemCount == 1 ? " part" : " parts");
        if (q is not { Count: > 0 })
        {
            card.Add(5, "Use the screen:");
            card.Add(6, "order a part");
            return;
        }
        // (short lines: the screen is 19 characters wide at its size)
        for (var k = 0; k < q.Count && k < 4; k++)
        {
            ref readonly var o = ref q.Items[k];
            card.Add(4 + k, o.Got).Add(4 + k, '/').Add(4 + k, o.What.Count).Add(4 + k, ' ').Add(4 + k, StateWords[(int)o.State]);
        }
    }

    // ---- Orders (server) -------------------------------------------------------------------------------------------------

    /// <summary>Server: a player's order at a terminal. Null, or why not (nothing is listed when refused).</summary>
    public static string? Place(ConstructWorld world, Construct c, int t, in WorkOrder what)
    {
        var d = c.DefOf(t);
        if (!d.IsCraftTerminal || world.StoreOf(c, t) is not { } tray)
            return "that isn't a Crafting Terminal";
        if (what.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount}";
        if (c.Parts[t].Craft is { Count: >= MaxOrders })
            return $"the {d.Name} has {MaxOrders} orders open: cancel one or wait";
        if (what.Name == null && !what.IsPiece && ConstructParts.Get(what.Part) == null)
            return "no such part";
        if (DefOf(what) is not { } kind)
            return "that piece can't be made";
        var net = NetOf(world, c, t);
        string? refusal = null;
        var ok = Stock(world, c, net, (ushort)kind.Id) > 0;
        for (var i = 0; i < net.MachineN && !ok; i++)
        {
            var why = WorkOrders.Refusal(world, c, net.Machines[i], what);
            ok = why == null;
            if (why != null && (refusal == null || c.DefOf(net.Machines[i]).IsAssembler != what.IsPiece))
                refusal = why;
        }
        if (!ok)
            return refusal ?? $"nothing linked to the {d.Name} holds or makes the {NameOf(what)}";
        ref var p = ref c.Parts[t];
        p.Craft ??= new CraftList();
        var q = p.Craft;
        var id = q.NextId++;
        if (q.NextId == 0)
            q.NextId = 1;
        q.Items[q.Count++] = new CraftOrder { What = what with { Made = 0 }, Id = id, Where = NoPart, Note = "", Def = (ushort)kind.Id };
        world.CraftOpened(c);
        StepTerminal(world, c, t, tray, q);
        world.CraftChanged(c);
        return null;
    }

    /// <summary>Server: cancels an order: its claims go, and what it asked of a machine and wasn't begun comes off that queue.</summary>
    public static string? Cancel(ConstructWorld world, Construct c, int t, ushort id)
    {
        if (!c.DefOf(t).IsCraftTerminal || c.Parts[t].Craft is not { } q)
            return "that order is gone";
        var k = q.Find(id);
        if (k < 0)
            return "that order is gone";
        ref var o = ref q.Items[k];
        world.Reserve.Release(world, c.Parts[t].Store, id);
        ConstructLogisticsPlan.Cancelled(world, c, t, id); // (its chained inputs: their entries go, their unstarted orders leave their makers)
        if (o.Asked > 0)
        {
            var net = NetOf(world, c, t);
            for (var i = 0; i < net.MachineN; i++)
                if (c.Parts[net.Machines[i]].Orders is { Bill: false } mq)
                    for (var j = mq.Count - 1; j >= 0; j--)
                    {
                        ref var mo = ref mq.Items[j];
                        if (mo.Made == 0 && mo.Count == o.Asked && mo.Part == o.What.Part && mo.Name == o.What.Name && (!mo.IsPiece || mo.Key == o.What.Key))
                        {
                            mq.RemoveAt(j);
                            world.OrdersChanged(c);
                            i = net.MachineN;
                            break;
                        }
                    }
        }
        if (o.Sent > 0)
        {
            // Parts already on a conveyor can't be called back: the order shrinks to them, says so, and ends when they land.
            o.What.Count = (ushort)(o.Got + o.Sent);
            o.Asked = 0;
            o.Cancelled = true;
            o.State = CraftState.Riding;
            o.Where = NoPart;
            o.Note = $"cancelled: {o.Sent} part{(o.Sent == 1 ? "" : "s")} on the way arrive{(o.Sent == 1 ? "s" : "")} anyway";
        }
        else
            q.RemoveAt(k);
        world.CraftChanged(c);
        return null;
    }

    /// <summary>Server: a part landed in a terminal's tray: it counts for the oldest open order of its kind.</summary>
    public static void Arrived(ConstructWorld world, Construct c, int t, ConstructPartDef d)
    {
        if (c.Parts[t].Craft is not { } q)
            return;
        for (var k = 0; k < q.Count; k++)
        {
            ref var o = ref q.Items[k];
            if (o.Done || KindOf(ref o) != d.Id)
                continue;
            o.Got++;
            if (o.Sent > 0)
                o.Sent--;
            else if (o.Asked > 0)
                o.Asked--;
            if (o.Done)
            {
                o.State = CraftState.InTray;
                o.Where = NoPart;
                o.Note = "";
                o.Age = 0;
            }
            world.CraftChanged(c);
            return;
        }
    }

    /// <summary>
    /// Server: a machine with no Piece out conveyor dropped what it made beside itself (a Panel Former's out bed). The oldest order on its
    /// body that asked a machine for that kind counts it: nothing will carry it to a store or a tray. Its note says where it lies.
    /// </summary>
    public static void MadeLoose(ConstructWorld world, Construct c, int machine, ConstructPartDef d)
    {
        if (!world.CraftOpen.Contains(c.Id))
            return;
        for (var t = 0; t < c.PartCount; t++)
        {
            if (c.Parts[t].Craft is not { } q)
                continue;
            for (var k = 0; k < q.Count; k++)
            {
                ref var o = ref q.Items[k];
                if (o.Done || o.Asked == 0 || KindOf(ref o) != d.Id)
                    continue;
                o.Got++;
                o.Asked--;
                o.Loose = true;
                if (o.Done)
                {
                    o.State = CraftState.InTray;
                    o.Where = (ushort)machine;
                    o.Note = $"made: on the {c.DefOf(machine).Name}'s out bed";
                    o.Age = 0;
                }
                world.CraftChanged(c);
                return;
            }
        }
    }

    internal static ushort KindOf(ref CraftOrder o)
    {
        if (o.Def == 0 || o.Def == ConstructParts.None)
            o.Def = (ushort)(DefOf(o.What)?.Id ?? ConstructParts.None);
        return o.Def;
    }

    /// <summary>What machine <paramref name="m"/> lacks for an order ("needs 4 rubber, 2 cloth"), or empty.</summary>
    private static string Starved(ConstructWorld world, Construct c, CraftNet net, int m, in WorkOrder what)
    {
        var d = c.DefOf(m);
        if (d.IsAssembler)
            return !what.IsPiece && AssemblingTable.Current.Of(what.Part) is { } r ? Missing(world, c, net, m, r, out _) : "";
        if (d.IsExtruder && ConstructExtruder.RunHeld(c, m))
            return "its line is stopped: press Start on its console";
        if (d.IsPanelFormer)
            return world.StoreOf(c, m) is { } fst && fst.Refused == (byte)ExtrudeStall.NoMaterial ? ConstructPanelFormer.Lacks(c, m, fst) : "";
        if (!d.IsExtruder || world.StoreOf(c, m) is not { } st || st.Amount > 0)
            return "";
        return "needs " + RecyclingTable.Current.NameOf(what.Key.Material);
    }

    /// <summary>Server: a chute's move into a terminal: one part its orders have claimed in the chute's source, into the tray. True when one moved.</summary>
    public static bool ChuteInto(ConstructWorld world, Construct cf, int from, Construct ct, int to)
    {
        if (world.StoreOf(cf, from) is not { } src || world.StoreOf(ct, to) is not { } tray || ct.Parts[to].Craft == null)
            return false;
        var res = world.Reserve;
        for (var i = 0; i < res.Count; i++)
        {
            ref var r = ref res.Items[i];
            if (r.Store != src.Handle || r.Owner != tray.Handle || ConstructParts.Get(r.Def) is not { } d)
                continue;
            if (ConstructConveyor.RackCheck(ct, to, tray, d) != 0 || !TakeOne(world, cf, from, src, d))
                continue;
            res.Shrink(i, 1, src);
            world.RackPut(ct, to, d);
            Arrived(world, ct, to, d);
            world.CraftChanged(ct);
            return true;
        }
        return false;
    }

    /// <summary>Server, 2 Hz: every terminal with open orders. A world with none does nothing.</summary>
    public static void Step(ConstructWorld world)
    {
        if (world.CraftOpen.Count == 0 && world.ChainCount == 0 || ++world.CraftTicks < StepEvery)
            return;
        world.CraftTicks = 0;
        ConstructLogisticsPlan.Step(world); // (chained orders' links kept honest, before the terminals count on them)
        // Roots compete by class, then age (the user, Q2): terminals are stepped oldest open order first, each followed by its chains,
        // so the oldest claims free stock first. Every root is Normal until phase 3's priorities; stock targets will come last.
        Span<ulong> rank = stackalloc ulong[RankedMax];
        Span<int> rankBody = stackalloc int[RankedMax], rankPart = stackalloc int[RankedMax];
        var n = 0;
        for (var b = world.CraftOpen.Count - 1; b >= 0; b--)
        {
            var any = false;
            if (world.Get(world.CraftOpen[b]) is { } c)
                for (var t = 0; t < c.PartCount; t++)
                    if (c.Parts[t].Craft is { Count: > 0 } q && c.Parts[t].D.IsCraftTerminal && world.StoreOf(c, t) is { } tray)
                    {
                        any = true;
                        var oldest = uint.MaxValue;
                        for (var k = 0; k < q.Count; k++)
                        {
                            if (q.Items[k].Stamp == 0)
                                q.Items[k].Stamp = world.OrderStamp++;
                            if (!q.Items[k].Done && !q.Items[k].Cancelled)
                                oldest = Math.Min(oldest, q.Items[k].Stamp);
                        }
                        if (n < RankedMax)
                        {
                            (rank[n], rankBody[n], rankPart[n]) = ((ulong)oldest << 32 | (uint)n, c.Id, t);
                            n++;
                        }
                        else
                        {
                            StepTerminal(world, c, t, tray, q); // (past the table: in body order)
                            ConstructLogisticsPlan.Serve(world, c, t);
                        }
                    }
            if (!any)
                world.CraftOpen.RemoveAt(b);
        }
        rank[..n].Sort();
        for (var i = 0; i < n; i++)
        {
            var at = (int)(rank[i] & 0xFFFF);
            var t = rankPart[at];
            if (world.Get((ushort)rankBody[at]) is { } c && t < c.PartCount && c.Parts[t].Craft is { Count: > 0 } q && world.StoreOf(c, t) is { } tray)
            {
                StepTerminal(world, c, t, tray, q);
                ConstructLogisticsPlan.Serve(world, c, t);
            }
        }
    }

    private const int RankedMax = 64;

    private static readonly ChainLine[] LineBuf = new ChainLine[ConstructLogisticsPlan.MaxLines];

    /// <summary>Server: the order's chain lines made again; a new array only when they changed (a steady chain allocates nothing). True: changed.</summary>
    private static bool ChainLines(ConstructWorld world, Construct c, int t, ref CraftOrder o)
    {
        var more = 0;
        var n = o.Done || o.Cancelled ? 0 : ConstructLogisticsPlan.Lines(world, c, t, o.Id, LineBuf, out more);
        more = Math.Min(255, more);
        var old = o.Lines;
        var same = (old?.Length ?? 0) == n && o.LinesMore == more;
        for (var i = 0; i < n && same; i++)
            same = old![i].Same(LineBuf[i]);
        if (same)
            return false;
        o.Lines = n == 0 ? null : LineBuf.AsSpan(0, n).ToArray();
        o.LinesMore = (byte)more;
        return true;
    }

    private static void StepTerminal(ConstructWorld world, Construct c, int t, CStore tray, CraftList q)
    {
        var net = NetOf(world, c, t);
        var res = world.Reserve;
        var owner = tray.Handle;
        var changed = false;
        // Claims are cut to what their stores still hold (a part taken by hand), and what was collected by hand at a store with no route counts.
        for (var i = res.Count - 1; i >= 0; i--)
        {
            if (res.Items[i].Owner != owner)
                continue;
            var st = world.StoreByHandle(res.Items[i].Store);
            var k = q.Find(res.Items[i].OwnerId);
            var held = st == null ? 0 : Holds(st, res.Items[i].Def);
            var over = st == null || k < 0 ? res.Items[i].Count : Math.Min((int)res.Items[i].Count, Math.Max(0, world.Reserve.Reserved(st.Handle, res.Items[i].Def) - held));
            if (over <= 0)
                continue;
            if (k >= 0 && q.Items[k].State == CraftState.Collect)
                q.Items[k].Got = (ushort)Math.Min(q.Items[k].What.Count, q.Items[k].Got + over);
            res.Shrink(i, over, st);
            changed = true;
        }
        for (var k = 0; k < q.Count; k++)
        {
            ref var o = ref q.Items[k];
            var before = (o.State, o.Where, o.Got, o.Sent, o.Asked);
            if (o.Done)
            {
                if (o.State != CraftState.InTray)
                {
                    o.State = CraftState.InTray;
                    o.Where = NoPart;
                    o.Note = "";
                    o.Age = 0;
                    changed = true;
                }
                if (++o.Age >= DoneSteps)
                {
                    res.Release(world, owner, o.Id);
                    q.RemoveAt(k--);
                    changed = true;
                }
                continue;
            }
            if (o.Cancelled)
                continue; // (it waits only for what is riding: Arrived ends it)
            var def = KindOf(ref o);
            if (ConstructParts.Get(def) is not { } d)
            {
                o.State = CraftState.Held;
                o.Note = "held: this build can't make it";
                continue;
            }
            // 1. Stock first: claim what is free in the linked stores.
            var held = res.Held(owner, o.Id);
            var need = o.What.Count - o.Got - o.Sent - held;
            for (var i = 0; i < net.StoreN && need > 0; i++)
            {
                if (world.StoreOf(c, net.Stores[i]) is not { } st)
                    continue;
                var take = Math.Min(need, Available(world, st, def));
                if (take <= 0 || !res.Add(st, def, take, owner, o.Id))
                    continue;
                changed = true;
                need -= take;
                held += take;
                o.Asked = (ushort)Math.Max(0, o.Asked - take); // (what a machine made for it has come in)
            }
            // ...and send what is claimed: a rack's piece onto its conveyor, one a step; a chute brings its own (ChuteInto).
            var state = CraftState.Waiting;
            var where = NoPart;
            string note = "";
            for (var i = 0; i < res.Count; i++)
            {
                ref var r = ref res.Items[i];
                if (r.Owner != owner || r.OwnerId != o.Id)
                    continue;
                var s = -1;
                for (var j = 0; j < net.StoreN; j++)
                    if (c.Parts[net.Stores[j]].Store == r.Store)
                        s = j;
                if (s < 0)
                    continue;
                where = (ushort)net.Stores[s];
                var via = net.Via[s];
                if (via == -1)
                {
                    state = CraftState.Collect;
                    continue;
                }
                state = CraftState.Fetching;
                var fits = ConstructConveyor.RackCheck(c, t, tray, d);
                if (fits == 3)
                {
                    // (a panel folded taller than the tray: it stays claimed at its rack for the player, as with no route)
                    state = CraftState.Collect;
                    note = $"too big for the tray: collect it at the {c.DefOf(net.Stores[s]).Name}";
                    continue;
                }
                if (fits != 0)
                {
                    state = CraftState.Held;
                    note = "held: the tray is full - take parts out";
                    continue;
                }
                if (via < 0 || via >= c.RouteCount || world.StoreOf(c, net.Stores[s]) is not { } st)
                    continue;
                if (ConstructConveyor.PutMade(world, c, via, d, 1, out var why, owner, o.Id)) // (it rides for this order: a Router on the way sends it to the tray)
                {
                    if (TakeOne(world, c, net.Stores[s], st, d))
                    {
                        o.Sent++;
                        held--;
                        res.Shrink(i, 1, st);
                    }
                    break; // (the table moved: the rest next step)
                }
                if (why == ExtrudeStall.ConveyorSmall)
                {
                    state = CraftState.Held;
                    note = $"held: the conveyor from the {c.DefOf(net.Stores[s]).Name} is too narrow for it";
                }
            }
            // 2. The rest: asked of the first linked machine that takes it.
            var ask = o.What.Count - o.Got - o.Sent - held - o.Asked;
            if (ask > 0)
            {
                string? refusal = null;
                for (var i = 0; i < net.MachineN && ask > 0; i++)
                {
                    var why = WorkOrders.Queue(world, c, net.Machines[i], o.What with { Count = (ushort)ask, Made = 0 });
                    if (why == null)
                    {
                        ConstructLogisticsPlan.Asked(world, c, net, t, o, net.Machines[i], ask); // (the parts its recipe needs: asked of other machines)
                        o.Asked += (ushort)ask;
                        ask = 0;
                        if (state == CraftState.Waiting)
                            where = (ushort)net.Machines[i];
                    }
                    else if (refusal == null || c.DefOf(net.Machines[i]).IsAssembler != o.What.IsPiece)
                        refusal = why;
                }
                if (ask > 0 && state == CraftState.Waiting)
                {
                    state = CraftState.Held;
                    note = "held: " + (refusal ?? "no linked machine makes it");
                }
            }
            if (state == CraftState.Waiting && o.Asked > 0)
            {
                state = CraftState.Queued;
                if (where == NoPart)
                    where = o.State == CraftState.Queued ? o.Where : NoPart;
            }
            if (state == CraftState.Waiting && o.Sent > 0)
                state = CraftState.Riding;
            if (state == CraftState.Queued)
            {
                // Why a queued order waits (its machine lacks an input), looked at every fifth step: the note is made only then.
                note = o.Note ?? "";
                if (o.State != CraftState.Queued || o.Age++ % 5 == 0)
                    note = ConstructLogisticsPlan.Note(world, c, t, o.Id) // (a chained input: the link nobody makes, or who makes it)
                           ?? (where < c.PartCount ? Starved(world, c, net, where, o.What) : "");
                if (o.Lost != 0 && note == "")
                    note = "a part went to storage: " + ConstructLogistics.ReasonText((LogiReason)o.Lost) + " - it is fetched from there";
                if (note != (o.Note ?? ""))
                    changed = true;
            }
            o.State = state;
            o.Where = where;
            o.Note = note;
            changed |= before != (o.State, o.Where, o.Got, o.Sent, o.Asked);
            if (world.ChainCount > 0 || o.Lines != null)
                changed |= ChainLines(world, c, t, ref o);
        }
        if (changed)
            world.CraftChanged(c);
    }

    // ---- The wire (CBodyKind.Craft) --------------------------------------------------------------------------------------

    /// <summary>Does a body hold a terminal with orders, or a store with claims?</summary>
    public static bool Any(ConstructWorld world, Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Craft != null || world.StoreOf(c, i) is { Reserved: > 0 })
                return true;
        return false;
    }

    /// <summary>
    /// One body's terminals and claims: u16 body; u8 terminals, each u16 part, u8 n, n orders (the work order's 22 bytes, u16 id,
    /// u16 got, u8 state, u16 where, a note); then u8 stores, each u16 handle, u8 kinds, each a def (a piece by its key) and u16 count.
    /// </summary>
    public static void Write(PacketWriter w, ConstructWorld world, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Craft).U16(c.Id);
        var at = w.Length;
        w.U8(0);
        var n = 0;
        for (var i = 0; i < c.PartCount && n < 255; i++)
        {
            if (c.Parts[i].Craft is not { } q)
                continue;
            w.U16((ushort)i).U8((byte)q.Count);
            for (var k = 0; k < q.Count; k++)
            {
                ref readonly var o = ref q.Items[k];
                var key = o.What.Key;
                if (o.What.Name != null)
                    w.U8(WorkOrders.NamedShape).U8(key.Material).U16(o.What.Count).Str(o.What.Name); // (protocol 260: a formed panel by its name, as a work order's)
                else
                    w.U8((byte)key.Shape).U32((uint)key.Mask).U32((uint)(key.Mask >> 32)).U16(key.W).U16(key.H).U16(key.L).U16(key.Wall).U8(key.Material).U16(o.What.Part).U16(o.What.Count);
                w.U16(o.Id).U16(o.Got).U8((byte)o.State).U16(o.Where).Str(o.Note ?? "");
                var lines = o.Lines ?? [];
                w.U8((byte)lines.Length).U8(o.LinesMore); // (protocol 268: the chain lines under its row)
                foreach (ref readonly var l in lines.AsSpan())
                    w.U8(l.Depth).U16(l.Kind).U16(l.Made).U16(l.Asked).U16(l.Maker).U16(l.Dest).U16(l.Stock).Str(l.Reason ?? "");
            }
            n++;
        }
        w.PatchU8(at, (byte)n);
        at = w.Length;
        w.U8(0);
        n = 0;
        var res = world.Reserve;
        for (var i = 0; i < c.PartCount && n < 255; i++)
        {
            var h = c.Parts[i].Store;
            if (h == 0 || !IsStore(c.Parts[i].D))
                continue;
            w.U16(h);
            var kat = w.Length;
            w.U8(0);
            var kinds = 0;
            for (var j = 0; j < res.Count && kinds < 255; j++)
                if (res.Items[j].Store == h && res.Items[j].Count > 0)
                {
                    ConstructStorage.WriteDef(w, res.Items[j].Def);
                    w.U16(res.Items[j].Count);
                    kinds++;
                }
            w.PatchU8(kat, (byte)kinds);
            n++;
        }
        w.PatchU8(at, (byte)n);
    }

    private static readonly (ushort, ushort)[] ReadKinds = new (ushort, ushort)[255];

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        var c = world.Get(id);
        if (c != null)
            for (var i = 0; i < c.PartCount; i++)
                c.Parts[i].Craft = null; // (the server's word is the whole set)
        int terms = r.U8();
        for (var j = 0; j < terms; j++)
        {
            var part = r.U16();
            int n = r.U8();
            if (n > MaxOrders)
                throw new MalformedPacketException($"a terminal with {n} orders");
            var q = new CraftList();
            for (var k = 0; k < n; k++)
            {
                var shape = r.U8();
                CraftOrder o;
                byte mat;
                if (shape == WorkOrders.NamedShape)
                {
                    mat = r.U8();
                    var cnt = r.U16();
                    var nm = r.Text(ConstructParts.MaxRuntimeName);
                    if (!HullLoft.IsFormedName(nm))
                        throw new MalformedPacketException("a bad panel order");
                    o = new CraftOrder { What = new WorkOrder { Key = new ExtrusionKey(0, 0, 0, 0, mat, false, ExShape.Section), Part = ConstructParts.None, Count = cnt, Name = nm } };
                    shape = 0;
                }
                else
                {
                    var mask = (ulong)r.U32() | (ulong)r.U32() << 32;
                    var kw = r.U16();
                    var kh = r.U16();
                    var kl = r.U16();
                    var wall = r.U16();
                    mat = r.U8();
                    o = new CraftOrder { What = new WorkOrder { Key = new ExtrusionKey(mask, kw, kh, kl, mat, false, (ExShape)shape, wall), Part = r.U16(), Count = r.U16() } };
                }
                o.Id = r.U16();
                o.Got = r.U16();
                var state = r.U8();
                o.Where = r.U16();
                o.Note = r.Text(200);
                int ln = r.U8();
                o.LinesMore = r.U8();
                if (ln > ConstructLogisticsPlan.MaxLines)
                    throw new MalformedPacketException($"an order with {ln} chain lines");
                if (ln > 0)
                {
                    o.Lines = new ChainLine[ln];
                    for (var i = 0; i < ln; i++)
                    {
                        o.Lines[i] = new ChainLine { Depth = r.U8(), Kind = r.U16(), Made = r.U16(), Asked = r.U16(), Maker = r.U16(), Dest = r.U16(), Stock = r.U16(), Reason = r.Text(200) };
                        if (ConstructParts.Get(o.Lines[i].Kind) == null || o.Lines[i].Depth > ConstructLogisticsPlan.MaxDepth + 1)
                            throw new MalformedPacketException("a bad chain line");
                    }
                }
                if (shape > (byte)ExShape.Wing || state > (byte)CraftState.Held || mat > RecyclingTable.Current.Materials.Length || o.What.Part != ConstructParts.None && ConstructParts.Get(o.What.Part) == null)
                    throw new MalformedPacketException("a bad crafting order");
                o.State = (CraftState)state;
                q.Items[q.Count++] = o;
            }
            if (c != null && part < c.PartCount)
                c.Parts[part].Craft = q;
        }
        int stores = r.U8();
        for (var j = 0; j < stores; j++)
        {
            var h = r.U16();
            int kinds = r.U8();
            for (var k = 0; k < kinds; k++)
                ReadKinds[k] = (ConstructStorage.ReadDef(ref r), r.U16());
            if (world.StoreByHandle(h) is { } st)
                world.Reserve.ClientSet(st, ReadKinds.AsSpan(0, kinds));
        }
    }

    // ---- Saves (after a body's work orders: an older save ends before it) -------------------------------------------------

    public static (ushort Part, CraftList List)[]? Snapshot(Construct c)
    {
        List<(ushort, CraftList)>? o = null;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Craft is { Count: > 0 } q)
                (o ??= new()).Add(((ushort)i, new CraftList { Items = (CraftOrder[])q.Items.Clone(), Count = q.Count, NextId = q.NextId }));
        return o?.ToArray();
    }

    public static void WriteSave(SaveWriter w, (ushort Part, CraftList List)[] lists, WorldNames names)
    {
        w.U16((ushort)lists.Length);
        foreach (var (part, q) in lists)
        {
            w.U16(part).U16(q.NextId).U8((byte)q.Count);
            for (var k = 0; k < q.Count; k++)
            {
                ref readonly var o = ref q.Items[k];
                var key = o.What.Key;
                if (o.What.Name != null)
                    w.U8(WorkOrders.NamedShape).U16(o.What.Count).Str(o.What.Name); // (a formed panel: its name is its key and holds its material)
                else
                    w.U8((byte)key.Shape).U64(key.Mask).U16(key.W).U16(key.H).U16(key.L).U16(key.Wall).U8(key.Material).U16(names.Part(o.What.Part)).U16(o.What.Count);
                w.U16(o.Id).U16(o.Got).U16(o.Asked).U16(o.Sent); // (a part riding is saved with its conveyor and counts as it lands)
            }
        }
    }

    public static (ushort Part, CraftList List)[]? ReadSave(SaveReader r, int bodyEnd, WorldNamesRead names)
    {
        if (!r.Has(2) || r.Pos + 2 > bodyEnd)
            return null;
        int n = r.U16();
        var lists = new List<(ushort, CraftList)>(n);
        for (var j = 0; j < n; j++)
        {
            var part = r.U16();
            var q = new CraftList { NextId = Math.Max((ushort)1, r.U16()) };
            int count = r.U8();
            for (var k = 0; k < count; k++)
            {
                var shape = r.U8();
                if (shape == WorkOrders.NamedShape)
                {
                    var ncnt = r.U16();
                    var nm = r.Str();
                    var (nid, ngot, nasked, nsent) = (r.U16(), r.U16(), r.U16(), r.U16());
                    if (ncnt is 0 or > WorkOrders.MaxCount || !HullLoft.IsFormedName(nm) || nm.Length > ConstructParts.MaxRuntimeName || q.Count >= MaxOrders)
                        continue;
                    q.Items[q.Count++] = new CraftOrder { What = ConstructPanelFormer.Order(nm, ncnt), Id = nid, Got = Math.Min(ncnt, ngot), Sent = nsent, Asked = nasked, Where = NoPart, Note = "", Def = ConstructParts.None };
                    continue;
                }
                var mask = r.U64();
                var kw = r.U16();
                var kh = r.U16();
                var kl = r.U16();
                var wall = r.U16();
                var mat = names.Material(r.U8());
                var pn = r.U16();
                var def = pn == ushort.MaxValue ? ConstructParts.None : names.Part(pn, "a crafting order");
                var cnt = r.U16();
                var oid = r.U16();
                var got = r.U16();
                var asked = r.U16();
                var sent = r.U16();
                if (shape > (byte)ExShape.Wing || cnt is 0 or > WorkOrders.MaxCount || pn != ushort.MaxValue && def == ConstructParts.None || pn == ushort.MaxValue && mat == 0 || q.Count >= MaxOrders)
                    continue; // (what this build can't make or name is left out)
                q.Items[q.Count++] = new CraftOrder
                {
                    What = new WorkOrder { Key = new ExtrusionKey(mask, kw, kh, kl, mat, false, (ExShape)shape, wall), Part = def, Count = cnt },
                    Id = oid, Got = Math.Min(cnt, got), Sent = sent, Asked = asked, Where = NoPart, Note = "", Def = ConstructParts.None,
                };
            }
            lists.Add((part, q));
        }
        return lists.ToArray();
    }

    /// <summary>Puts a save's lists on the body's terminals; their claims are made again at the next step.</summary>
    public static void Apply(ConstructWorld world, Construct c, (ushort Part, CraftList List)[] lists)
    {
        foreach (var (part, q) in lists)
            if (part < c.PartCount && c.DefOf(part).IsCraftTerminal && q.Count > 0)
            {
                c.Parts[part].Craft = q;
                world.CraftOpened(c);
            }
    }
}

/// <summary>The Crafting Terminal's side of the world (ConstructCrafting.cs, ConstructReserve.cs).</summary>
public sealed partial class ConstructWorld
{
    public readonly ConstructReserve Reserve = new(); // claims on stock (server: the truth; client: as heard)
    public readonly Dictionary<ushort, CraftNet> CraftNets = new(); // each terminal's network, by its store handle (a cache)
    public readonly List<ushort> CraftOpen = new(16); // server: bodies with open orders (the 2 Hz step walks only these)
    public readonly List<ushort> CraftDirty = new(16); // bodies whose orders or claims changed (CBodyKind.Craft)
    public int CraftTicks;

    public void CraftOpened(Construct c)
    {
        if (!CraftOpen.Contains(c.Id))
            CraftOpen.Add(c.Id);
    }

    public void CraftChanged(Construct c)
    {
        if (!CraftDirty.Contains(c.Id))
            CraftDirty.Add(c.Id);
    }

    /// <summary>A live store by its handle, or null.</summary>
    public CStore? StoreByHandle(ushort h) => h != 0 && h <= MaxStores && Stores[h].Live ? Stores[h] : null;
}
