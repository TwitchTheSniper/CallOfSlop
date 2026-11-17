using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sub-blueprint jobs over a link (docs/sandbox/design/linked-logistics.md 4.4; the user's answer 8): a Building Station whose Sub-jobs is
/// Linked takes uses dispatched over a link to its network. A root with no free station of its own offers a use to a linked network
/// whose report lists an idle Linked station with a bay that fits, before its 30 s fallback (which waits while the offer stands, 10 s at
/// most for the far end to start it). The far station builds it as any sub-job; its body waits "done - waiting for a ship"; carried into a
/// ship's Cargo Volume (a crane, an arm or by hand) it is the ship's manifest line; docked at the root's construct the ship lets it go and
/// the root takes it as a delivered body (its lifter, or by hand: the sub-blueprints delivery). A ship lost with it: the use is dispatched
/// again (the user's answer 10).
/// </summary>
public static class LinkSub
{
    public const double StartWait = 10; // seconds an offer may wait for the far station before the 30 s fallback runs on
    public const float HandOverReach = 60; // metres from the root's bay a delivered body left by hand is taken in

    /// <summary>What a posted offer needs to start far away (in memory: an offer not started before a restart is given up, the use dispatched again).</summary>
    private static readonly Dictionary<ushort, (ConstructBuild.Blueprint Bp, string Name, string Pinned, int[] Path, string UseId, uint OrderAge)> Offers = new();

    public static bool LinkedOn(Construct c, int s) => ConstructConfig.Get(c.Parts[s], c.DefOf(s), CfgKey.SubJobs, 1) >= 1.5f;

    /// <summary>A body's Linked stations for its report: idle ones and their bays (the ledger's copy).</summary>
    public static void Report(ConstructWorld world, Construct c, LinkReport r)
    {
        r.StationN = 0;
        for (var s = 0; s < c.PartCount && r.StationN < LinkReport.MaxStations; s++)
        {
            if (!c.DefOf(s).IsBuildStation || !LinkedOn(c, s) || c.Parts[s].Broken)
                continue;
            var idle = ConstructRecycler.Powered(world, c, s) && (c.Parts[s].Station is not { } sj || sj.State is StationState.Idle or StationState.Failed
                || sj.State == StationState.Done && (world.Get(sj.Body) is not { } b || ConstructStation.BayClearOf(world, c, s, b)));
            r.Stations[r.StationN] = (ushort)s;
            r.StationBay[r.StationN] = ConstructStation.BaySize(c, s);
            r.StationIdle[r.StationN++] = idle;
        }
    }

    /// <summary>Server, the root's look with no local station free: a use offered to a linked network's idle Linked station. True when posted.</summary>
    public static bool Offer(ConstructWorld world, Construct c, int t, StationJob j, int r, UseTag tag)
    {
        if (tag.Sub == null)
            return false;
        var key = LinkedLogistics.WorldKey();
        Span<uint> ps = stackalloc uint[16];
        foreach (var e in LinkLedger.Ends.Values)
        {
            if (e.World != key || e.Body != c.Id)
                continue;
            var n = LinkLedger.Partners(e, ps);
            for (var i = 0; i < n; i++)
            {
                var p = LinkLedger.Ends[ps[i]];
                if (LinkLedger.Asleep(p.World))
                    continue; // (a closed world builds nothing: a sub-job isn't estimated)
                for (var k = 0; k < p.Report.StationN; k++)
                {
                    if (!p.Report.StationIdle[k] || ConstructStation.QuickRefusal(tag.Sub, p.Report.StationBay[k]) != null || Taken(p.World, p.Body, p.Report.Stations[k]))
                        continue;
                    if (LinkLedger.NewLine(e, p, ConstructParts.None, 1) is not { } line)
                        return false;
                    var row = j.Tree![r];
                    (line.Use, line.SubStamp, line.SubStation, line.Owner, line.OwnerId) = ((ushort)(r + 1), j.Stamp, p.Report.Stations[k], c.Parts[t].Store, (ushort)t);
                    line.Note = $"offered to {p.Name}";
                    Offers[line.Id] = (tag.Sub, $"{j.Name}: {row.Name} #{row.Number}", j.Pinned, [.. j.PinPath, r], tag.Id, j.OrderAge);
                    row.State = UseState.Building;
                    row.Station = ConstructStationTree.None;
                    row.Linked = line.Id;
                    row.Note = $"offered to {p.Name} (linked)";
                    Console.WriteLine($"[linked] line {line.Id}: {row.Name} #{row.Number} offered to {p.Name}'s station {line.SubStation} ({LinkLedger.Title(p.World)})");
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Taken(string world, ushort body, ushort station)
    {
        foreach (var l in LinkLedger.Lines)
            if (l.Use != 0 && l.State is LinkLineState.Posted or LinkLineState.Open && l.SWorld == world && l.SBody == body && l.SubStation == station && l.Delivered == 0)
                return true;
        return false;
    }

    /// <summary>The root's look at a row offered over a link: its line's state, read back into the row. True when the row changed.</summary>
    public static bool Watch(ConstructWorld world, Construct c, int t, StationJob j, int r)
    {
        var row = j.Tree![r];
        var l = LinkLedger.Line(row.Linked);
        string note;
        if (l == null || l.State == LinkLineState.Failed || l.State == LinkLineState.Posted && LinkLedger.Now - l.PostedAt > StartWait || l.Lost > 0)
        {
            if (l != null && l.State != LinkLineState.Closed)
                LinkLedger.CancelLine(l.Id, l.Lost > 0 ? "lost with its ship" : "the far station didn't start it");
            row.Linked = 0;
            row.State = UseState.Waiting;
            row.Station = ConstructStationTree.None;
            row.Note = l?.Lost > 0 ? $"lost with {l.Ship}: dispatched again" : $"the linked station didn't take it{(l != null && l.Note.Length > 0 ? ": " + l.Note : "")}";
            return true;
        }
        if (l.Delivered > 0 && world.Get(row.Body) is { } b && b.SubStamp == j.Stamp)
        {
            row.Linked = 0;
            row.State = UseState.Done; // (the ordinary delivery from here: a lifter brings it, or move it into the bay)
            return true;
        }
        note = l.Loaded > 0 ? $"done at {LinkLedger.Title(l.SWorld)} - on {l.Ship}" : l.Ready > 0 ? $"done at {LinkLedger.Title(l.SWorld)} - waiting for a ship" : l.Note;
        if (row.Note == note)
            return false;
        row.Note = note;
        return true;
    }

    /// <summary>A line's use lost (its ship destroyed): the root's row is dispatched again.</summary>
    public static void Lost(ConstructWorld world, LinkLine l)
    {
        // (Watch sees l.Lost and puts the row back to Waiting at the root's next look.)
    }

    /// <summary>Server, the supplier's side once a second: start the offered sub-job, watch it, mark its finished body for the ship.</summary>
    public static void Supply(ConstructWorld world, Construct c, LinkLine l)
    {
        if (l.State == LinkLineState.Posted)
        {
            if (!Offers.TryGetValue(l.Id, out var o))
            {
                Fail(l, "its blueprint wasn't kept (the host restarted before it began)");
                return;
            }
            var s = l.SubStation;
            if (s >= c.PartCount || !c.DefOf(s).IsBuildStation || !LinkedOn(c, s))
            {
                Fail(l, "that station isn't set to Linked any more");
                return;
            }
            if (ConstructStation.StartSub(world, c, s, o.Bp, o.Name + " (linked)", 0, ConstructStationTree.None, l.SubStamp, l.Use - 1, o.Pinned, o.Path) is { } why)
            {
                Fail(l, why);
                return;
            }
            var sj = c.Parts[s].Station!;
            sj.UseId = o.UseId;
            sj.OrderAge = world.OrderStamp++;
            sj.Header = $"linked: {o.Name} - for {LinkLedger.Title(l.CWorld)}; when done, carry it into a docked ship's Cargo Volume";
            Offers.Remove(l.Id);
            l.State = LinkLineState.Open;
            l.Note = $"building at {LinkedLogistics.NameOf(c, s)}";
            world.StationChanged(c);
            return;
        }
        if (l.State != LinkLineState.Open || l.Loaded > 0)
            return;
        if (l.SubStation >= c.PartCount || c.Parts[l.SubStation].Station is not { Role: 1 } job || job.RootStamp != l.SubStamp)
        {
            if (l.Ready == 0)
                Fail(l, "the far station stopped it");
            return;
        }
        if (job.State == StationState.Failed)
        {
            Fail(l, $"the far station stopped: {job.Note}");
            return;
        }
        if (job.State == StationState.Done && world.Get(job.Body) is { } body && l.Ready == 0)
        {
            body.SubLine = l.Id;
            body.SubUse = l.Use;
            l.Ready = 1;
            l.Note = "done - waiting for a ship (carry it into a docked ship's Cargo Volume)";
            job.Note = "done - carry it into a docked ship's Cargo Volume: it goes by ship";
            world.StationChanged(c);
        }
    }

    private static void Fail(LinkLine l, string why)
    {
        l.State = LinkLineState.Failed;
        l.Note = why;
        l.ClosedAt = LinkLedger.Now;
        Offers.Remove(l.Id);
        Console.WriteLine($"[linked] line {l.Id} (a linked sub-job): {why}");
    }

    /// <summary>
    /// From the dock exchange of a docked ship: a linked sub-job's body in its Cargo Volume is loaded (its manifest line); at the root's
    /// construct it is let go and handed to the root. Also a body left near the root's bay by hand (or flown in some other way).
    /// </summary>
    public static void Exchange(ConstructWorld world, string key, Construct holder, int dock, Construct ship, int port, LinkPart lp, ref int moved)
    {
        foreach (var b in world.Bodies)
        {
            if (b.SubLine == 0 || b.CarriedBy != ship.Id || LinkLedger.Line(b.SubLine) is not { } l)
                continue;
            if (l.Loaded == 0 && l.SWorld == key)
            {
                l.Loaded = 1;
                l.Ready = 0;
                l.Ship = ship.DisplayName();
                (l.ShipSeen, l.ShipWorld) = (LinkLedger.Now, key);
                LinkDocks.AddManifest(lp, l, 1);
                l.Note = $"on {ship.DisplayName()}";
                moved++;
            }
            if (l.CWorld == key && l.CBody == holder.Id && l.Delivered == 0)
            {
                ConstructCargo.Release(world, b); // (out of the ship's volume: the root's lifter, or a player, takes it from here)
                HandOver(world, holder, l, b);
                for (var m = 0; m < lp.Manifest.Count; m++)
                    if (lp.Manifest[m].Line == l.Id)
                    {
                        var ml = lp.Manifest[m];
                        ml.Delivered = ml.Loaded;
                        lp.Manifest[m] = ml;
                        lp.Version++;
                    }
                moved++;
            }
        }
    }

    /// <summary>Server, once a second: linked bodies in this world that came by hand, or whose ship sits in a cargo volume not at a dock.</summary>
    public static void Step(ConstructWorld world, string key)
    {
        var any = false;
        foreach (var l in LinkLedger.Lines)
            any |= l.Use != 0 && l.State == LinkLineState.Open && l.CWorld == key && l.Delivered == 0;
        if (!any)
            return;
        foreach (var b in world.Bodies)
        {
            if (b.SubLine == 0 || b.CarriedBy != 0 || LinkLedger.Line(b.SubLine) is not { } l || l.CWorld != key || l.Delivered > 0 || world.Get(l.CBody) is not { } root)
                continue;
            var t = RootStation(root, l);
            if (t < 0)
                continue;
            ConstructStation.Bay(root, t, out var floor, out _, out _);
            if (Vector3.Distance(b.WorldCom, floor) <= HandOverReach)
                HandOver(world, root, l, b);
        }
    }

    private static int RootStation(Construct root, LinkLine l)
    {
        for (var t = 0; t < root.PartCount; t++)
            if (root.Parts[t].Station is { Tree: not null } j && j.Stamp == l.SubStamp)
                return t;
        return -1;
    }

    private static void HandOver(ConstructWorld world, Construct root, LinkLine l, Construct b)
    {
        var t = RootStation(root, l);
        if (t < 0 || root.Parts[t].Station is not { Tree: { } tree } j || l.Use - 1 >= tree.Length)
        {
            l.Note = "its main station's build is gone: the body is yours";
            b.SubLine = 0;
            l.State = LinkLineState.Closed;
            l.ClosedAt = LinkLedger.Now;
            return;
        }
        var row = tree[l.Use - 1];
        b.SubStamp = j.Stamp;
        b.SubLine = 0;
        b.HeldBy = 0;
        row.Body = b.Id;
        l.Delivered = 1;
        l.State = LinkLineState.Closed;
        l.ClosedAt = LinkLedger.Now;
        l.Note = $"delivered to {LinkedLogistics.NameOf(root, t)}";
        world.StationChanged(root);
        Console.WriteLine($"[linked] line {l.Id}: {row.Name} #{row.Number} delivered to its main station");
    }
}
