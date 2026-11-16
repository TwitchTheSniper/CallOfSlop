using System.Text;

namespace CallOfSlop;

/// <summary>A Logistics Dispatcher's fleet: its registered ships (by their Route Computers' ids) and when it sends one.</summary>
public sealed class LinkFleet
{
    public const int MaxShips = 8;
    public readonly uint[] Ships = new uint[MaxShips];
    public int N;
    public byte Threshold = 75; // % of a ship's free payload that waits before it is sent
    public ushort Timeout = 15; // minutes the oldest waiting line may wait before a ship goes anyway
    public string Last = "";

    public bool Has(uint id)
    {
        for (var i = 0; i < N; i++)
            if (Ships[i] == id)
                return true;
        return false;
    }
}

/// <summary>
/// The Logistics Dispatcher (docs/sandbox/design/linked-logistics.md 6.5): at a factory with a link end, once a second, it registers
/// route ships of its owner that dock at one of its Cargo Docks (at most eight), and sends an idle one (docked here, no route of its own)
/// on a one-off run (load here, unload there, come back) when enough waits for a linked network (its threshold of the ship's free payload)
/// or the oldest line has waited past its timeout. It also fetches from a supplier that has no Dispatcher of its own. Its card lists
/// the fleet: state, load, fuel.
/// </summary>
public static class LinkDispatch
{
    private struct Group
    {
        public string World;
        public ushort Body;
        public float Mass;
        public int Units;
        public double Oldest;
        public bool Fetch;
    }

    private static readonly Group[] Groups = new Group[8];

    public static void Step(ConstructWorld world, string key)
    {
        _world = world;
        foreach (var (bid, part) in world.LinkState.Parts)
        {
            if (world.Get(bid) is not { } f || part >= f.PartCount || f.DefOf(part).LinkKind != LinkKind.Dispatcher || f.Parts[part].Link is not { } lp)
                continue;
            var fleet = lp.Fleet ??= new LinkFleet();
            ref var p = ref f.Parts[part];
            if (!LinkedLogistics.Working(world, f, part))
            {
                p.Out = p.Out1 = 0;
                continue;
            }
            var here = LinkReports.Terminal(f, dockOnly: true);
            Register(world, f, here, fleet, lp);
            var gn = Waiting(key, f);
            var outN = 0;
            for (var i = 0; i < fleet.N; i++)
                if (ShipById(world, fleet.Ships[i], out var s, out _) is { } rc && rc.Route is { Phase: not (RoutePhase.Done or RoutePhase.Idle) } && s!.CarriedBy != f.Id)
                    outN++;
            p.Out = outN > 0 ? 1 : 0;
            p.Out1 = gn > 0 ? 1 : 0;
            if (here < 0 || gn == 0)
                continue;
            for (var g = 0; g < gn; g++)
            {
                ref readonly var grp = ref Groups[g];
                if (Idle(world, f, fleet) is not { } ship || ship.Parts[ShipRoutePart(ship)].Link is not { } rlp)
                    break;
                var limit = LinkRoutes.PayloadLimit(ship);
                var free = limit > 0 ? limit - ship.Mass : 0;
                var due = free > 0 ? grp.Mass >= fleet.Threshold / 100f * free : grp.Units >= 4;
                due |= LinkLedger.Now - grp.Oldest > fleet.Timeout * 60.0;
                if (!due)
                    continue;
                if (DockOf(grp.World, grp.Body) is not { } far)
                    continue;
                var r = new LinkRoute { OneOff = true, SentBy = lp.Id };
                var home = new LinkStop { World = key, Body = f.Id, Part = (ushort)here, Name = LinkedLogistics.NameOf(f, here) };
                // Home, there, home: a run to deliver loads here first; a fetch leaves home at once (nothing of its own to load for it).
                r.Stops[0] = home;
                LinkRoutes.Unpack(grp.Fetch ? LinkRoutes.LeaveDone | 1u << 8 | 95u << 24 : LinkRoutes.DefaultRules, ref r.Stops[0]);
                if (grp.Fetch)
                    r.Stops[0].Flags = LinkRoutes.LeaveTime; // (a minute's wait at most: what is for there is loaded meanwhile)
                r.Stops[1] = new LinkStop { World = far.World, Body = far.Body, Part = far.Part, Name = far.Name };
                LinkRoutes.Unpack(grp.Fetch ? LinkRoutes.DefaultRules : LinkRoutes.LeaveDone | 10u << 8 | 95u << 24, ref r.Stops[1]);
                r.Stops[2] = home;
                LinkRoutes.Unpack(LinkRoutes.LeaveDone | 1u << 8 | 95u << 24, ref r.Stops[2]);
                r.N = 3;
                r.Phase = RoutePhase.Docked;
                r.Since = LinkLedger.Now;
                rlp.Route = r;
                rlp.Version++;
                fleet.Last = $"sent {ship.DisplayName()} {(grp.Fetch ? "to fetch from" : "to")} {far.Name}: {grp.Units} part(s) waiting";
                lp.Version++;
                Console.WriteLine($"[linked] dispatcher on construct {f.Id}: {fleet.Last}");
            }
        }
    }

    /// <summary>Route ships of the owner docked at this factory join its fleet (at most eight).</summary>
    private static void Register(ConstructWorld world, Construct f, int dock, LinkFleet fleet, LinkPart lp)
    {
        if (dock < 0)
            return;
        foreach (var b in world.Bodies)
        {
            if (b.CarriedBy != f.Id || fleet.N >= LinkFleet.MaxShips || ShipRoutePart(b) is not (var rp and >= 0) || b.Parts[rp].Link is not { Id: not 0 } rlp || fleet.Has(rlp.Id))
                continue;
            if (b.Owner != f.Owner && f.Owner != 255 && b.Owner != 255 && !Factions.Shares(f.Owner, b.Owner))
                continue;
            fleet.Ships[fleet.N++] = rlp.Id;
            fleet.Last = $"{b.DisplayName()} joined the fleet";
            lp.Version++;
        }
    }

    private static int ShipRoutePart(Construct ship)
    {
        if (ship.LinkParts == 0)
            return -1;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.DefOf(i).LinkKind == LinkKind.Route && !ship.Parts[i].Broken)
                return i;
        return -1;
    }

    /// <summary>A registered ship docked here with no route of its own (or its run done), or null.</summary>
    private static Construct? Idle(ConstructWorld world, Construct f, LinkFleet fleet)
    {
        for (var i = 0; i < fleet.N; i++)
            if (ShipById(world, fleet.Ships[i], out var ship, out _) is { } rlp && ship!.CarriedBy == f.Id && LinkDocks.PortOf(ship) >= 0
                && (rlp.Route == null || rlp.Route is { Phase: RoutePhase.Done or RoutePhase.Idle, Suspended: false } r && (r.OneOff || r.N < 2)))
                return ship;
        return null;
    }

    private static LinkPart? ShipById(ConstructWorld world, uint id, out Construct? ship, out int part)
    {
        foreach (var (bid, pp) in world.LinkState.Parts)
            if (world.Get(bid) is { } c && pp < c.PartCount && c.DefOf(pp).LinkKind == LinkKind.Route && c.Parts[pp].Link is { } lp && lp.Id == id)
            {
                (ship, part) = (c, pp);
                return lp;
            }
        (ship, part) = (null, -1);
        return null;
    }

    /// <summary>The linked networks cargo waits for here (and suppliers without a Dispatcher with cargo for here), in Groups. How many.</summary>
    private static int Waiting(string key, Construct f)
    {
        var n = 0;
        foreach (var l in LinkLedger.Lines)
        {
            if (l.State != LinkLineState.Open || l.Use != 0 || ConstructParts.Get(l.Kind) is not { } d)
                continue;
            var units = l.Ready + (int)l.Est;
            var aboard = l.Loaded - l.Delivered - l.Lost - l.Taken;
            if (aboard > 0 && l.ShipWorld == key && LinkLedger.Ends.Count >= 0 && ShipHere(l.ShipBody, f))
                units += aboard; // (loaded on a ship idle at this factory: a run waits to go)
            if (units <= 0)
                continue;
            bool fetch;
            string w;
            ushort b;
            if (l.SWorld == key && l.SBody == f.Id)
                (fetch, w, b) = (false, l.CWorld, l.CBody);
            else if (l.CWorld == key && l.CBody == f.Id && LinkLedger.Ends.TryGetValue(l.SupplierEnd, out var se) && !se.Report.HasDispatcher)
                (fetch, w, b) = (true, l.SWorld, l.SBody);
            else
                continue;
            var g = 0;
            while (g < n && !(Groups[g].World == w && Groups[g].Body == b && Groups[g].Fetch == fetch))
                g++;
            if (g == n)
            {
                if (n >= Groups.Length)
                    continue;
                Groups[n++] = new Group { World = w, Body = b, Fetch = fetch, Oldest = l.PostedAt };
            }
            Groups[g].Mass += d.Mass * units;
            Groups[g].Units += units;
            Groups[g].Oldest = Math.Min(Groups[g].Oldest, l.PostedAt);
        }
        return n;
    }

    private static ConstructWorld? _world;

    private static bool ShipHere(ushort ship, Construct f) => _world?.Get(ship) is { } s && s.CarriedBy == f.Id;

    /// <summary>The Cargo Dock of a linked network's body, from the ledger (its world may be closed).</summary>
    private static LinkDockRef? DockOf(string world, ushort body)
    {
        foreach (var e in LinkLedger.Ends.Values)
            if (e.World == world && e.Body == body && e.Report.HasDock && e.Report.DockPart != ushort.MaxValue)
                return new LinkDockRef { World = world, Body = body, Part = e.Report.DockPart, Name = e.Name };
        return null;
    }

    private static readonly StringBuilder Sb = new(256);

    public static string Status(ConstructWorld world, Construct f, int part, LinkPart lp)
    {
        var fleet = lp.Fleet;
        var sb = Sb.Clear();
        var linked = false;
        for (var i = 0; i < f.PartCount && !linked; i++)
            linked = f.DefOf(i).IsLinkEnd;
        if (!linked)
            sb.Append("NO LINK END: it needs a Transmitter, Receiver or Relay on this construct\n");
        if (LinkReports.Terminal(f, dockOnly: true) < 0)
            sb.Append("NO CARGO DOCK on this construct\n");
        if (fleet == null || fleet.N == 0)
            return sb.Append("Fleet: none yet. A ship with a Route Computer and a Cargo Port that docks here joins it.").ToString();
        sb.Append($"Sends at {fleet.Threshold}% of a ship's payload, or after {fleet.Timeout} min\n");
        for (var i = 0; i < fleet.N; i++)
        {
            if (ShipById(world, fleet.Ships[i], out var ship, out _) is not { } rlp || ship == null)
            {
                sb.Append("- a ship away (another world)\n");
                continue;
            }
            var r = rlp.Route;
            var state = ship.CarriedBy == f.Id && (r == null || r.Phase is RoutePhase.Done or RoutePhase.Idle) ? "idle here" : r?.Suspended == true ? "suspended" : r?.Why ?? "no route";
            sb.Append("- ").Append(ship.DisplayName()).Append(": ").Append(state).Append($", load {LinkDocks.ShipFill(world, ship) * 100:0}%, fuel {ship.Fuel:0} l\n");
        }
        if (fleet.Last.Length > 0)
            sb.Append("Last: ").Append(fleet.Last);
        return sb.ToString().TrimEnd('\n');
    }

    public static string? Act(ConstructWorld world, Construct f, int part, LinkPart lp, LinkOp op, uint a, uint b)
    {
        var fleet = lp.Fleet ??= new LinkFleet();
        if (op == LinkOp.FleetRules)
        {
            fleet.Threshold = (byte)Math.Clamp(a, 10, 100);
            fleet.Timeout = (ushort)Math.Clamp(b, 1, 240);
            lp.Version++;
            return null;
        }
        for (var i = 0; i < fleet.N; i++)
            if (fleet.Ships[i] == a)
            {
                fleet.Ships[i] = fleet.Ships[--fleet.N];
                lp.Version++;
                return null;
            }
        return "that ship isn't in the fleet";
    }

    public static void Save(SaveWriter w, LinkFleet? f)
    {
        if (f == null)
        {
            w.U8(0);
            return;
        }
        w.U8(1).U8(f.Threshold).U16(f.Timeout).U8((byte)f.N);
        for (var i = 0; i < f.N; i++)
            w.U32(f.Ships[i]);
    }

    public static LinkFleet? Load(SaveReader r)
    {
        if (r.U8() == 0)
            return null;
        var f = new LinkFleet { Threshold = r.U8(), Timeout = r.U16() };
        int n = r.U8();
        for (var i = 0; i < n; i++)
        {
            var id = r.U32();
            if (f.N < LinkFleet.MaxShips)
                f.Ships[f.N++] = id;
        }
        return f;
    }
}
