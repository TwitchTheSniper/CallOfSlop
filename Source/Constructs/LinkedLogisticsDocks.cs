using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Cargo Docks and Ports (docs/sandbox/design/linked-logistics.md 6.1, 4.3, section 7): a ship held by a Docking Clamp of a construct, with
/// a Cargo Port within <see cref="PortReach"/> of one of that construct's Cargo Docks, trades with the dock once a second: what it carries
/// for this network is unloaded into the dock's hold (stock for the network's orders), then what lines here wait for is loaded into its
/// Storage Volumes and Piece Racks, each part a manifest line on its port. Goods only ever move between the two stores, a few a second.
/// Manifests are checked here too: a ship destroyed loses its lines (ordered again), a part taken from its hold shrinks its line (not
/// ordered again: the user's answer 9), a line on a ship that hasn't moved for ten minutes reads stalled.
/// </summary>
public static class LinkDocks
{
    public const float PortReach = 6f; // metres between a ship's Cargo Port and the dock's hatch (the part's middle) while a clamp holds it
    public const int MovesASecond = 2; // parts each way a second a docked ship (the user's design: one chute move a second; twice that for parts)

    private static readonly List<(ushort Ship, ushort Port, ushort Body, ushort Dock)> Docked = new(8);

    public static Vector3 PartAt(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        var mid = (p.D.BoundsMin + p.D.BoundsMax) * 0.5f;
        return t + Vector3.Transform(p.Pos + Vector3.Transform(mid, Rot24.Quat(p.Rot)), q);
    }

    /// <summary>The ship's working Cargo Port, or -1.</summary>
    public static int PortOf(Construct ship)
    {
        if (ship.LinkParts == 0)
            return -1;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.DefOf(i).LinkKind == LinkKind.Port && !ship.Parts[i].Broken)
                return i;
        return -1;
    }

    /// <summary>The dock a held ship trades with: a working Cargo Dock of the construct holding it, near its port; or -1.</summary>
    public static int DockFor(ConstructWorld world, Construct ship, int port, out Construct? holder)
    {
        holder = ship.CarriedBy != 0 ? world.Get(ship.CarriedBy) : null;
        if (holder == null || port < 0 || holder.LinkParts == 0)
            return -1;
        var at = PartAt(ship, port);
        var best = -1;
        var bestD = PortReach;
        for (var i = 0; i < holder.PartCount; i++)
            if (holder.DefOf(i).LinkKind == LinkKind.Dock && !holder.Parts[i].Broken && Vector3.Distance(PartAt(holder, i), at) is var dd && dd <= bestD)
                (best, bestD) = (i, dd);
        return best;
    }

    /// <summary>Server, once a second: every docked ship trades; every manifest in this world is checked against its holds.</summary>
    public static void Step(ConstructWorld world, string key)
    {
        Docked.Clear();
        foreach (var (bid, part) in world.LinkState.Parts)
        {
            if (world.Get(bid) is not { } ship || part >= ship.PartCount || ship.DefOf(part).LinkKind != LinkKind.Port || ship.Parts[part].Link is not { } lp)
                continue;
            Check(world, key, ship, part, lp);
            if (ship.Parts[part].Broken || ship.CarriedBy == 0)
                continue;
            var dock = DockFor(world, ship, part, out var holder);
            if (dock >= 0 && holder != null && ConstructRecycler.Powered(world, holder, dock))
                Docked.Add((ship.Id, part, holder.Id, (ushort)dock));
        }
        foreach (var (sid, port, hid, dock) in Docked)
            if (world.Get(sid) is { } ship && world.Get(hid) is { } holder)
                Exchange(world, key, holder, dock, ship, port, MovesASecond, LinkRoutes.Filter(ship));
    }

    /// <summary>
    /// One second's trade between dock <paramref name="dock"/> of <paramref name="holder"/> and a ship's port: up to <paramref name="moves"/>
    /// parts unloaded (lines whose consumer is this network), then as many loaded (lines this network supplies, for the ship's route's
    /// stops when it has one: <paramref name="filter"/>). Also the ledger's settlement for a ship at a stop it can't land at (LinkRoutes).
    /// How many moved.
    /// </summary>
    public static int Exchange(ConstructWorld world, string key, Construct holder, int dock, Construct ship, int port, int moves, LinkRouteFilter filter)
    {
        if (ship.Parts[port].Link is not { } lp || world.StoreOf(holder, dock) is not { } tray)
            return 0;
        var moved = 0;
        // Unloading: the ship's lines for this network.
        for (var m = 0; m < lp.Manifest.Count && moved < moves; m++)
        {
            var ml = lp.Manifest[m];
            if (ml.Aboard <= 0 || ml.Use != 0 || LinkLedger.Line(ml.Line) is not { } l || l.CWorld != key || l.CBody != holder.Id || ConstructParts.Get(ml.Kind) is not { } d)
                continue;
            while (ml.Aboard > 0 && moved < moves)
            {
                if (!TakeFromShip(world, ship, d))
                    break; // (the hold lacks it: Check counts it taken)
                if (!world.RackPut(holder, dock, d, ordered: true))
                {
                    PutIntoShip(world, ship, d);
                    l.Note = $"the {holder.DefOf(dock).Name}'s hold is full: unloading waits";
                    break;
                }
                ml.Delivered++;
                l.Delivered++;
                moved++;
                ConstructCrafting.Arrived(world, holder, dock, d); // (a dock order of this kind counts it, as a tray does)
            }
            lp.Manifest[m] = ml;
            if (l.Outstanding == 0 && l.State == LinkLineState.Open)
                Close(l, $"delivered to {LinkedLogistics.NameOf(holder, dock)}");
            lp.Version++;
        }
        for (var m = lp.Manifest.Count - 1; m >= 0; m--)
            if (lp.Manifest[m].Aboard == 0 && (LinkLedger.Line(lp.Manifest[m].Line) is not { } ll || ll.State != LinkLineState.Open || lp.Manifest[m].Delivered > 0))
            {
                lp.Manifest.RemoveAt(m);
                lp.Version++;
            }
        LinkSub.Exchange(world, key, holder, dock, ship, port, lp, ref moved);
        // Loading: lines this network supplies.
        foreach (var l in LinkLedger.Lines)
        {
            if (moved >= moves)
                break;
            if (l.State != LinkLineState.Open || l.Use != 0 || l.SWorld != key || l.SBody != holder.Id || l.Ready == 0 || !filter.Takes(l) || ConstructParts.Get(l.Kind) is not { } d)
                continue;
            if (!LinkRoutes.PayloadTakes(ship, d))
            {
                l.Note = $"{ship.DisplayName()} is at its payload limit";
                break;
            }
            while (l.Ready > 0 && moved < moves)
            {
                if (!ConstructCrafting.TakeOne(world, holder, dock, tray, d))
                {
                    l.Ready = 0; // (gone from the hold: taken by hand; its claim was cut)
                    break;
                }
                if (!PutIntoShip(world, ship, d))
                {
                    world.RackPut(holder, dock, d, ordered: true);
                    l.Note = $"{ship.DisplayName()}'s holds are full";
                    break;
                }
                Unclaim(world, tray, l, 1);
                l.Ready--;
                l.Loaded++;
                l.Ship = ship.DisplayName();
                (l.ShipSeen, l.ShipWorld) = (LinkLedger.Now, key);
                AddManifest(lp, l, 1);
                moved++;
            }
            l.Note = l.Ready > 0 ? $"loading on {ship.DisplayName()}" : $"{l.Loaded} on {ship.DisplayName()}";
        }
        return moved;
    }

    public static void AddManifest(LinkPart lp, LinkLine l, int n)
    {
        for (var m = 0; m < lp.Manifest.Count; m++)
            if (lp.Manifest[m].Line == l.Id)
            {
                var ml = lp.Manifest[m];
                ml.Loaded += (ushort)n;
                lp.Manifest[m] = ml;
                lp.Version++;
                return;
            }
        var dest = LinkLedger.Ends.TryGetValue(l.ConsumerEnd, out var ce) ? ce.Name : LinkLedger.Title(l.CWorld);
        if (lp.Manifest.Count < 64)
            lp.Manifest.Add(new ManifestLine { Line = l.Id, Kind = l.Kind, Loaded = (ushort)n, Dest = dest, Use = l.Use });
        else
            Console.WriteLine($"[linked] a manifest is full (64 lines): line {l.Id}'s {n} part(s) ride as loose cargo");
        lp.Version++;
    }

    private static void Close(LinkLine l, string why)
    {
        l.State = LinkLineState.Closed;
        l.ClosedAt = LinkLedger.Now;
        l.Note = why;
        Console.WriteLine($"[linked] line {l.Id} closed: {why}");
    }

    private static void Unclaim(ConstructWorld world, CStore tray, LinkLine l, int n)
    {
        var res = world.Reserve;
        for (var i = res.Count - 1; i >= 0 && n > 0; i--)
            if (res.Items[i].Owner == LinkPlan.LinkOwner && res.Items[i].OwnerId == l.Id)
            {
                var cut = Math.Min(n, (int)res.Items[i].Count);
                res.Shrink(i, cut, tray);
                n -= cut;
            }
    }

    public static bool PutIntoShip(ConstructWorld world, Construct ship, ConstructPartDef d)
    {
        for (var i = 0; i < ship.PartCount; i++)
        {
            var sd = ship.DefOf(i);
            if (ship.Parts[i].Broken || sd.Role != PartRole.Storage || sd.IsCraftTerminal)
                continue;
            if (sd.IsVolumeStore && world.PutPart(ship, i, d) == null)
                return true;
            if (sd.IsPieceRack && world.RackPut(ship, i, d))
                return true;
        }
        return false;
    }

    public static bool TakeFromShip(ConstructWorld world, Construct ship, ConstructPartDef d)
    {
        for (var i = 0; i < ship.PartCount; i++)
        {
            var sd = ship.DefOf(i);
            if (sd.Role != PartRole.Storage || sd.IsCraftTerminal || world.StoreOf(ship, i) is not { } st || ConstructCrafting.Holds(st, (ushort)d.Id) == 0)
                continue;
            if (ConstructCrafting.TakeOne(world, ship, i, st, d))
                return true;
        }
        return false;
    }

    /// <summary>How many of a kind a ship's holds have (Storage Volumes, Piece Racks, slots).</summary>
    public static int ShipHolds(ConstructWorld world, Construct ship, ushort kind)
    {
        var n = 0;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.DefOf(i) is { Role: PartRole.Storage, IsCraftTerminal: false } && world.StoreOf(ship, i) is { } st)
                n += ConstructCrafting.Holds(st, kind);
        return n;
    }

    /// <summary>How full a ship's holds are, 0 to 1 (by space: a Storage Volume's box, a Piece Rack's hold). 1 with no hold.</summary>
    public static float ShipFill(ConstructWorld world, Construct ship)
    {
        float used = 0, room = 0;
        for (var i = 0; i < ship.PartCount; i++)
        {
            var sd = ship.DefOf(i);
            if (sd.Role != PartRole.Storage || sd.IsCraftTerminal || ship.Parts[i].Broken || world.StoreOf(ship, i) is not { } st)
                continue;
            if (sd.IsVolumeStore)
            {
                ConstructStoreVolume.Usable(ship, i, out var mn, out var mx);
                var r = ConstructStoreVolume.ItemRoom(mn, mx);
                room += r;
                used += Math.Min(r, st.ItemSpace);
            }
            else if (sd.IsPieceRack)
            {
                room += 1;
                used += ConstructRackPack.Fill(sd, st);
            }
        }
        return room > 0 ? Math.Clamp(used / room, 0, 1) : 1;
    }

    /// <summary>A port's manifest against its ship: destroyed (lines lost, ordered again), parts taken from a hold (lines shrink), still aboard (seen).</summary>
    private static void Check(ConstructWorld world, string key, Construct ship, int port, LinkPart lp)
    {
        if (lp.Manifest.Count == 0)
            return;
        var gone = ship.Parts[port].Broken || ship.Wreck;
        Span<ushort> kinds = stackalloc ushort[16];
        Span<int> have = stackalloc int[16];
        var kn = 0;
        for (var m = lp.Manifest.Count - 1; m >= 0; m--)
        {
            var ml = lp.Manifest[m];
            if (ml.Aboard == 0 || ml.Use != 0)
                continue;
            var l = LinkLedger.Line(ml.Line);
            if (gone)
            {
                if (l != null)
                    LostWith(l, ml.Aboard, ship.DisplayName());
                ml.Loaded = ml.Delivered;
                lp.Manifest[m] = ml;
                lp.Version++;
                continue;
            }
            if (l != null)
                (l.ShipSeen, l.ShipWorld, l.Ship) = (LinkLedger.Now, key, ship.DisplayName());
            var k = kinds[..kn].IndexOf(ml.Kind);
            if (k < 0 && kn < kinds.Length)
                (kinds[kn], have[kn], k) = (ml.Kind, ShipHolds(world, ship, ml.Kind), kn++);
            if (k < 0)
                continue;
            // Newest lines first take the shortfall (a later loading of the same kind sits on top).
            var aboard = ml.Aboard;
            var keep = Math.Min(aboard, have[k]);
            have[k] -= keep;
            if (keep < aboard)
            {
                var taken = aboard - keep;
                ml.Loaded -= (ushort)taken;
                lp.Manifest[m] = ml;
                lp.Version++;
                if (l != null)
                {
                    l.Taken += (ushort)taken;
                    l.Loaded -= (ushort)taken;
                    l.Count -= (ushort)Math.Min(l.Count, taken); // (not asked again: the user's answer 9; its order stays short until reordered)
                    l.Taken -= 0;
                    l.Note = $"{taken} taken from {ship.DisplayName()}'s hold: order again to replace them";
                    Console.WriteLine($"[linked] line {l.Id}: {taken} {ConstructParts.Get(l.Kind)?.Name} taken from {ship.DisplayName()}'s hold");
                    if (l.Outstanding == 0 && l.State == LinkLineState.Open)
                        Close(l, l.Note);
                }
            }
            if (l != null)
            {
                var moving = ship.Vel.LengthSquared() > 0.25f || LinkRoutes.Flying(ship);
                if (moving || ship.CarriedBy != 0 && l.CBody == ship.CarriedBy)
                    l.StalledFrom = -1;
                else if (l.StalledFrom < 0)
                    l.StalledFrom = LinkLedger.Now;
            }
        }
        if (gone)
            for (var m = lp.Manifest.Count - 1; m >= 0; m--)
                if (lp.Manifest[m].Aboard == 0)
                    lp.Manifest.RemoveAt(m);
    }

    /// <summary>A line's parts lost with a ship (destroyed, or gone from every open world): its order asks again (the user's answer 10).</summary>
    public static void LostWith(LinkLine l, int n, string ship)
    {
        n = Math.Min(n, Math.Max(0, l.Loaded - l.Delivered - l.Lost - l.Taken));
        if (n <= 0)
            return;
        l.Lost += (ushort)n;
        l.LostPending += (ushort)n;
        l.Note = $"{n} {ConstructParts.Get(l.Kind)?.Name} lost with {ship}: ordered again";
        Console.WriteLine($"[linked] line {l.Id}: {l.Note}");
        if (l.Outstanding == 0)
            Close(l, l.Note);
    }

    // ---- What a world settled on the ledger while it was closed lands when it opens (LinkLedger.Wake) ----------------------------

    /// <summary>Parts a ship delivered at this world's dock while the world was closed (or from orbit) go into the dock's hold. How many.</summary>
    public static int LandDelivered(ConstructWorld world, LinkLine l, int budget)
    {
        var c = world.Get(l.CBody);
        var dock = c == null ? -1 : LinkReports.Terminal(c, dockOnly: true);
        if (c == null || dock < 0 || ConstructParts.Get(l.Kind) is not { } d)
        {
            if (l.Note.StartsWith("delivered while", StringComparison.Ordinal) == false)
                l.Note = "delivered while the world was closed: no Cargo Dock here takes it";
            return 0;
        }
        var n = 0;
        while (l.DeliverPending > 0 && n < budget)
        {
            if (!world.RackPut(c, dock, d, ordered: true))
            {
                l.Note = $"the {c.DefOf(dock).Name}'s hold is full: {l.DeliverPending} wait to land";
                break;
            }
            ConstructCrafting.Arrived(world, c, dock, d);
            l.DeliverPending--;
            n++;
        }
        return n;
    }

    private static readonly CHolding[] Hold = new CHolding[CStore.MaxKinds + 1];

    /// <summary>
    /// The supplier's side when its world opens: parts a ship took from its dock on the ledger leave the hold; estimated output that left
    /// on a ship takes its inputs (or the stock it came from) out of the network; estimated output still waiting is made into the hold.
    /// What can't be found is logged (an estimate can't be undone). How many units were settled.
    /// </summary>
    public static int LandSupplied(ConstructWorld world, LinkLine l, int budget)
    {
        if (world.Get(l.SBody) is not { } c || ConstructParts.Get(l.Kind) is not { } d)
            return 0;
        var dock = l.DockPart < c.PartCount && c.DefOf(l.DockPart).LinkKind == LinkKind.Dock ? l.DockPart : LinkReports.Terminal(c, dockOnly: true);
        if (dock < 0 || world.StoreOf(c, dock) is not { } tray)
            return 0;
        var n = 0;
        while (l.LedgerTaken > 0 && n < budget)
        {
            if (ConstructCrafting.TakeOne(world, c, dock, tray, d))
                Unclaim(world, tray, l, 1);
            else if (!Consume(world, c, dock, d, 1))
                Console.WriteLine($"[linked] line {l.Id}: a {d.Name} taken on the ledger isn't in {LinkedLogistics.NameOf(c, dock)}'s hold or stores: over-credited");
            l.LedgerTaken--;
            n++;
        }
        while (l.EstLoaded > 0 && n < budget)
        {
            if (!Consume(world, c, dock, d, 1))
                Console.WriteLine($"[linked] line {l.Id}: an estimated {d.Name} that left on a ship had no inputs left here: over-credited");
            l.EstLoaded--;
            Credit(c, l, 1);
            n++;
        }
        while (l.Est >= 1 && n < budget)
        {
            if (!Consume(world, c, dock, d, 1))
                Console.WriteLine($"[linked] line {l.Id}: an estimated {d.Name} had no inputs left here: made anyway");
            if (!world.RackPut(c, dock, d, ordered: true))
            {
                l.Est = 0; // (no room: it is made the ordinary way, its order still open)
                break;
            }
            l.Est -= 1;
            l.Ready++;
            world.Reserve.Add(tray, l.Kind, 1, LinkPlan.LinkOwner, l.Id);
            Credit(c, l, 1);
            n++;
        }
        return n;
    }

    /// <summary>An estimated part counts for the line's dock order (as if made there), so the order doesn't make it twice.</summary>
    private static void Credit(Construct c, LinkLine l, int n)
    {
        if (l.DockPart < c.PartCount && c.Parts[l.DockPart].Craft is { } q && q.Find(l.DockOrder) is var k and >= 0)
        {
            ref var o = ref q.Items[k];
            o.Got = (ushort)Math.Min(o.What.Count, o.Got + n);
            if (o.Asked > 0)
                o.Asked = (ushort)Math.Max(0, o.Asked - n);
            l.SeenGot = o.Got;
        }
    }

    /// <summary>One part's worth out of the dock's network: the part itself from a store, else its recipe's parts (one level). False: nothing there.</summary>
    private static bool Consume(ConstructWorld world, Construct c, int dock, ConstructPartDef d, int n)
    {
        var net = ConstructCrafting.NetOf(world, c, dock);
        for (var i = 0; i < net.StoreN; i++)
            if (world.TakeHolding(c, net.Stores[i], new CHolding(true, (ushort)d.Id, 0), n) is var got and > 0)
                return true;
        if (AssemblingTable.Current.Of((ushort)d.Id) is not { } r || r.Parts.Length == 0)
            return false;
        var any = false;
        foreach (var (rp, rn) in r.Parts)
        {
            var want = Math.Max(1, rn * n / Math.Max(1, r.Count));
            for (var i = 0; i < net.StoreN && want > 0; i++)
                want -= world.TakeHolding(c, net.Stores[i], new CHolding(true, (ushort)rp.Id, 0), want);
            any |= want < Math.Max(1, rn * n / Math.Max(1, r.Count));
        }
        return any;
    }

    // ---- What a dock and a port say -------------------------------------------------------------------------------------------

    private static readonly StringBuilder Sb = new(256);

    public static string DockStatus(ConstructWorld world, Construct c, int part)
    {
        var key = LinkedLogistics.WorldKey();
        var sb = Sb.Clear();
        if (!ConstructRecycler.Powered(world, c, part))
            sb.Append("NO POWER\n");
        foreach (var (sid, port, hid, dock) in Docked)
            if (hid == c.Id && dock == part && world.Get(sid) is { } ship)
            {
                sb.Append("Docked: ").Append(ship.DisplayName()).Append($", holds {ShipFill(world, ship) * 100:0}% full").Append('\n');
                if (LinkRoutes.LeaveText(world, ship) is { Length: > 0 } leave)
                    sb.Append(leave).Append('\n');
            }
        var held = false;
        for (var i = 0; i < Docked.Count && !held; i++)
            held = Docked[i].Body == c.Id;
        if (!held)
            for (var i = 0; i < world.Bodies.Count; i++)
                if (world.Bodies[i].CarriedBy == c.Id && PortOf(world.Bodies[i]) is var p && (p < 0 || DockFor(world, world.Bodies[i], p, out _) != part))
                {
                    sb.Append(p < 0 ? $"{world.Bodies[i].DisplayName()} is held, but has no Cargo Port: no exchange\n" : $"{world.Bodies[i].DisplayName()}'s Cargo Port isn't over this hatch (within {PortReach:0} m)\n");
                    break;
                }
        var lines = 0;
        foreach (var l in LinkLedger.Lines)
        {
            if (l.State != LinkLineState.Open || l.Use != 0 || lines >= 6)
                continue;
            if (l.SWorld == key && l.SBody == c.Id)
            {
                var to = LinkLedger.Ends.TryGetValue(l.ConsumerEnd, out var ce) ? ce.Name : LinkLedger.Title(l.CWorld);
                sb.Append("For ").Append(to).Append(": ").Append(l.Loaded).Append(" of ").Append(l.Count).Append(' ').Append(ConstructParts.Get(l.Kind)?.Name).Append(" loaded, ").Append(l.Ready).Append(" ready\n");
                lines++;
            }
            else if (l.CWorld == key && l.CBody == c.Id && l.Loaded > l.Delivered)
            {
                sb.Append("Coming: ").Append(l.Loaded - l.Delivered - l.Lost - l.Taken).Append(' ').Append(ConstructParts.Get(l.Kind)?.Name).Append(" on ").Append(l.Ship).Append('\n');
                lines++;
            }
        }
        if (sb.Length == 0)
            sb.Append("No ship docked; nothing waits");
        return sb.ToString().TrimEnd('\n');
    }

    public static string PortStatus(Construct c, int part, LinkPart lp)
    {
        var sb = Sb.Clear();
        if (lp.Manifest.Count == 0)
            return "Manifest: empty";
        sb.Append("Manifest:\n");
        foreach (var m in lp.Manifest)
            if (m.Aboard > 0)
                sb.Append(m.Aboard).Append(' ').Append(m.Use != 0 ? "submodule" : ConstructParts.Get(m.Kind)?.Name).Append(" for ").Append(m.Dest).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}

public sealed partial class Construct
{
    /// <summary>A ship's name for a person: its Cargo Port's or Route Computer's name, else "ship 12".</summary>
    public string DisplayName()
    {
        if (LinkParts > 0)
            for (var i = 0; i < PartCount; i++)
                if (Parts[i].Link is { Name.Length: > 0 } lp && DefOf(i).LinkKind is LinkKind.Port or LinkKind.Route)
                    return lp.Name;
        return $"ship {Id}";
    }
}
