using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--linked-check</c>: linked logistics tried headless (docs/sandbox/design/linked-logistics.md), on the showcase's Linked Works, Linked
/// Yard and Linked Hauler in worlds of their own (creative: no power wanted). One planetoid: pair, order at the yard, the works' dock fills,
/// the hauler held in the works' clamp loads, set in the yard's clamp unloads, the terminal gets its parts; a ship lost with cargo is
/// ordered again; a part taken from the hold is not; the save blocks and the ledger round trip. Two worlds (Relays): the works' world
/// stops (asleep), its stock is credited on the ledger, the hauler's stop there is settled on the ledger, and the works' world takes the
/// settlement when it runs again. Prints what it saw; exit 0 when every step did what it should. A tool: it allocates.
/// </summary>
public static class LinkedCheck
{
    private static int _fails;

    private static void Say(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _fails++;
    }

    public static int Cli(string[] args)
    {
        _fails = 0;
        var works = ConstructBuild.FindBlueprint("Linked Works");
        var yard = ConstructBuild.FindBlueprint("Linked Yard");
        var hauler = ConstructBuild.FindBlueprint("Linked Hauler");
        if (works == null || yard == null || hauler == null)
        {
            Console.WriteLine($"linked-check: the showcase blueprints aren't loaded ({string.Join("; ", ConstructBuild.RefusedBlueprints.Take(3))})");
            return 1;
        }
        var maps = Maps.All.Where(m => m.Planetoid == null && m.Gravity == null).Take(2).ToArray();
        var placeA = VehicleKit.VehicleDriveCheck.Place(1);
        var placeB = new VehicleKit.VehicleDriveCheck.CheckPlace(new WorldContext { Name = "linked check B", Map = maps.Length > 1 ? maps[1] : maps[0], Mover = World.Mover, GravityScale = 1, GravitySetting = 100, Gravity = new GravityFrames.Saved() });
        LinkLedger.Reset();
        var wa = new ConstructWorld { CreativeRecycling = true };
        var q = new SelfTest.DriveWorld();
        Construct Build(ConstructWorld w, VehicleKit.VehicleDriveCheck.CheckPlace place, ConstructBuild.Blueprint bp, Vector3 at)
        {
            var job = new ConstructBuild.GroundJob(w, bp, null);
            while (!job.Step(1000))
            {
            }
            using (place.In())
                return job.Join(at, 0, out var why) ?? throw new InvalidOperationException($"{bp.Name} doesn't build: {why}");
        }
        void Run(ConstructWorld w, VehicleKit.VehicleDriveCheck.CheckPlace place, double seconds)
        {
            using (place.In())
                for (var t = 0; t < seconds * World.TickRate; t++)
                    w.Step(q, World.TickDt);
        }
        int PartOf(Construct c, Func<ConstructPartDef, bool> f)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (f(c.DefOf(i)))
                    return i;
            return -1;
        }
        var gear = ConstructParts.Find("Gear Set")!;

        Console.WriteLine("linked-check: one planetoid (Transmitter and Receiver)");
        var wk = Build(wa, placeA, works, new Vector3(0, 0, 0));
        var yd = Build(wa, placeA, yard, new Vector3(60, 0, 0));
        var store = PartOf(wk, d => d.IsVolumeStore);
        var recv = PartOf(wk, d => d.LinkKind == LinkKind.Receiver);
        var trans = PartOf(yd, d => d.LinkKind == LinkKind.Transmitter);
        var term = PartOf(yd, d => d.IsCraftTerminal && d.LinkKind == LinkKind.None);
        var wDock = PartOf(wk, d => d.LinkKind == LinkKind.Dock);
        var yDock = PartOf(yd, d => d.LinkKind == LinkKind.Dock);
        for (var k = 0; k < 8; k++)
            wa.PutPart(wk, store, gear);
        Run(wa, placeA, 3);
        var code = LinkedLogistics.Code(yd.Parts[trans].Link?.Id ?? 0);
        string? why;
        using (placeA.In())
            why = LinkedLogistics.Act(wa, wk, recv, LinkOp.Pair, 0, 0, code);
        Say(why == null, $"Receiver pairs with the Transmitter's code {code}: {why ?? "paired"}");
        Run(wa, placeA, 5);
        Console.WriteLine($"     works' Receiver: {wk.Parts[recv].Link?.Status.Replace("\n", " | ")}");
        var offers = yd.Parts[trans].Link?.Offers ?? [];
        Say(offers.Any(o => o.Kind == gear.Id && o.Free >= 8), $"the yard's Transmitter sees the works' stock: {string.Join(", ", offers.Select(o => $"{ConstructParts.Get(o.Kind)?.Name} {o.Free}{(o.Makes ? " (makes)" : "")}"))}");
        using (placeA.In())
            why = ConstructCrafting.Place(wa, yd, term, new WorkOrder { Part = (ushort)gear.Id, Count = 4 });
        Say(why == null, $"an order for 4 Gear Sets at the yard's terminal (nothing at the yard makes them): {why ?? "placed"}");
        Run(wa, placeA, 12);
        var line = LinkLedger.Lines.FirstOrDefault();
        Say(line != null && line.Ready == 4, $"a remote line, its parts in the works' Cargo Dock: {(line == null ? "none" : $"line {line.Id}: {line.State}, ready {line.Ready}, {line.Note}")}");
        Console.WriteLine($"     the yard's order: {Order(yd, term)}");
        Console.WriteLine($"     the works' dock: {wk.Parts[wDock].Link?.Status.Replace("\n", " | ")}");

        // The hauler, set in the works' clamp's box (as a pilot lands it).
        var clampW = PartOf(wk, d => d.IsDock && d.LinkKind == LinkKind.None);
        var ha = Build(wa, placeA, hauler, LinkDocks.PartAt(wk, clampW) + new Vector3(0, 1.2f, 0) - new Vector3(0, 0, 0));
        var port = PartOf(ha, d => d.LinkKind == LinkKind.Port);
        for (var k = 0; k < 15 && ha.CarriedBy != wk.Id; k++)
            Run(wa, placeA, 1);
        Say(ha.CarriedBy == wk.Id, $"the works' Docking Clamp holds the hauler: CarriedBy {ha.CarriedBy} (works {wk.Id})");
        Run(wa, placeA, 4);
        var aboard = ha.Parts[port].Link?.Manifest.Sum(m => m.Aboard) ?? 0;
        Say(aboard == 4 && LinkDocks.ShipHolds(wa, ha, (ushort)gear.Id) == 4, $"loaded: manifest {aboard}, hold {LinkDocks.ShipHolds(wa, ha, (ushort)gear.Id)}; port: {ha.Parts[port].Link?.Status.Replace("\n", " | ")}");
        // Flown to the yard (set in its clamp's box).
        var clampY = PartOf(yd, d => d.IsDock && d.LinkKind == LinkKind.None);
        using (placeA.In())
        {
            ConstructCarrier.LetGo(wa, ha);
            ha.Pos = LinkDocks.PartAt(yd, clampY) + new Vector3(0, 1.2f, 0);
            ha.Vel = ha.AngVel = Vector3.Zero;
            ha.DockAway = 0;
            ha.Wake();
        }
        for (var k = 0; k < 15 && ha.CarriedBy != yd.Id; k++)
            Run(wa, placeA, 1);
        Say(ha.CarriedBy == yd.Id, $"the yard's Docking Clamp holds the hauler: CarriedBy {ha.CarriedBy} (yard {yd.Id})");
        Run(wa, placeA, 12);
        Say(line is { State: LinkLineState.Closed, Delivered: 4 }, $"unloaded: line {line?.State}, delivered {line?.Delivered}; {line?.Note}");
        Console.WriteLine($"     the yard's order: {Order(yd, term)}");
        Say(yd.Parts[term].Craft is { } cq && cq.Count > 0 && cq.Items[0].Got == 4, "the yard's terminal has its 4 Gear Sets");

        // Lost with a ship: ordered again.
        using (placeA.In())
            why = ConstructCrafting.Place(wa, yd, term, new WorkOrder { Part = (ushort)gear.Id, Count = 2 });
        Run(wa, placeA, 6);
        using (placeA.In())
        {
            ConstructCarrier.LetGo(wa, ha);
            ha.Pos = LinkDocks.PartAt(wk, clampW) + new Vector3(0, 1.2f, 0);
            ha.Vel = ha.AngVel = Vector3.Zero;
            ha.DockAway = 0;
            ha.Wake();
        }
        Run(wa, placeA, 10);
        var line2 = LinkLedger.Lines.LastOrDefault(l => l.Count == 2);
        Say(line2 is { Loaded: 2 }, $"a second order's 2 loaded: {line2?.Loaded} ({line2?.Note})");
        using (placeA.In())
        {
            ha.Parts[port].Broken = true; // (its port shot off: as a destroyed ship's)
            ha.Parts[port].Health = 0;
        }
        Run(wa, placeA, 4);
        var line3 = LinkLedger.Lines.LastOrDefault(l => l.Id != line2?.Id && l.Count == 2 && l.Kind == gear.Id);
        Say(line2 is { Lost: 2 } && line3 != null, $"lost with the ship: line {line2?.Id} lost {line2?.Lost}; ordered again: {(line3 == null ? "no" : $"line {line3.Id}")}");
        Console.WriteLine($"     the yard's order: {Order(yd, term)}");

        // A part taken from the hold by a player: the line shrinks, nothing is ordered again (the user's answer 9).
        using (placeA.In())
        {
            ha.Parts[port].Broken = false;
            ha.Parts[port].Health = ha.DefOf(port).Health;
        }
        Run(wa, placeA, 6);
        var linesBefore = LinkLedger.Lines.Count;
        using (placeA.In())
            for (var k = LinkDocks.ShipHolds(wa, ha, (ushort)gear.Id); k > 1; k--)
                LinkDocks.TakeFromShip(wa, ha, gear); // (all but one: the lost line's two still lie in the hold, as loose cargo now)
        Run(wa, placeA, 3);
        Say(line3 is { Taken: 1 } && LinkLedger.Lines.Count == linesBefore, $"a part taken from the hold: line {line3?.Id} taken {line3?.Taken}, loaded {line3?.Loaded}; no new line ({LinkLedger.Lines.Count} lines); {line3?.Note}");
        Console.WriteLine($"     the yard's order: {Order(yd, term)}");

        // The Dispatcher: the hauler, docked at the works, joins its fleet; with parts waiting it is sent on a one-off run.
        var disp = PartOf(wk, d => d.LinkKind == LinkKind.Dispatcher);
        var fleet = wk.Parts[disp].Link?.Fleet;
        Say(fleet is { N: 1 }, $"the works' Dispatcher registered the hauler: {wk.Parts[disp].Link?.Status.Replace("\n", " | ")}");
        using (placeA.In())
            why = ConstructCrafting.Place(wa, yd, term, new WorkOrder { Part = (ushort)gear.Id, Count = 1 });
        using (placeA.In())
            LinkedLogistics.Act(wa, wk, disp, LinkOp.FleetRules, 10, 1, "");
        var rcp = PartOf(ha, d => d.LinkKind == LinkKind.Route);
        LinkLedger.Offset += 70; // (the oldest line past the Dispatcher's one-minute timeout)
        Run(wa, placeA, 8);
        var sent = ha.Parts[rcp].Link?.Route;
        Say(sent is { OneOff: true, N: 3 }, $"the Dispatcher sent it: {(sent == null ? "no route" : $"{sent.N} stops, {sent.Phase}: {sent.Why}")}; {fleet?.Last}");
        if (sent != null)
            ha.Parts[rcp].Link!.Route = null; // (headless: no flight here; the window tool flies it)

        // Saves: the body block and the ledger.
        var bytes = LinkedLogistics.SaveBytes(ha);
        var copy = ha.Parts[port].Link;
        if (bytes != null)
            LinkedLogistics.LoadBytes(ha, bytes);
        Say(bytes != null && ha.Parts[port].Link != null && ha.Parts[port].Link!.Id == copy!.Id, $"a port's LLNK block round trips ({bytes?.Length ?? 0} bytes)");
        var ledger = LinkLedger.SaveBytes();
        var (ends, lines) = (LinkLedger.Ends.Count, LinkLedger.Lines.Count);
        LinkLedger.Load(new SaveReader(ledger));
        Say(LinkLedger.Ends.Count == ends && LinkLedger.Lines.Count == lines, $"the LINK section round trips: {ends} ends, {lines} lines ({ledger.Length} bytes)");

        // ---- Two worlds: Relays, a closed works.
        Console.WriteLine($"linked-check: two worlds (Relays): {placeA.Ctx.Map?.Name} and {placeB.Ctx.Map?.Name}");
        LinkLedger.Reset();
        var w1 = new ConstructWorld { CreativeRecycling = true };
        var w2 = new ConstructWorld { CreativeRecycling = true };
        var yard2 = Build(w1, placeA, yard, new Vector3(0, 0, 0));
        var works2 = Build(w2, placeB, works, new Vector3(0, 0, 0));
        var store2 = PartOf(works2, d => d.IsVolumeStore);
        for (var k = 0; k < 6; k++)
            w2.PutPart(works2, store2, gear);
        Run(w1, placeA, 3);
        Run(w2, placeB, 3);
        var relayY = PartOf(yard2, d => d.LinkKind == LinkKind.Relay);
        var relayW = PartOf(works2, d => d.LinkKind == LinkKind.Relay);
        using (placeB.In())
            why = LinkedLogistics.Act(w2, works2, relayW, LinkOp.Pair, 0, 0, LinkedLogistics.Code(yard2.Parts[relayY].Link!.Id));
        Say(why == null, $"a Relay pairs with a Relay on another world: {why ?? "paired"}");
        for (var k = 0; k < 4; k++)
        {
            Run(w1, placeA, 1);
            Run(w2, placeB, 1);
        }
        var term2 = PartOf(yard2, d => d.IsCraftTerminal && d.LinkKind == LinkKind.None);
        using (placeA.In())
            why = ConstructCrafting.Place(w1, yard2, term2, new WorkOrder { Part = (ushort)gear.Id, Count = 3 });
        Say(why == null, $"an order for 3 at the yard on the other world: {why ?? "placed"}");
        Run(w1, placeA, 3); // (the works' world is closed now: nobody steps it)
        LinkLedger.Offset += 8; // (the ledger's clock is real time: a check runs faster than that, so it is moved on as the hours would)
        Run(w1, placeA, 3);
        LinkLedger.Offset += 8;
        Run(w1, placeA, 3);
        var xl = LinkLedger.Lines.FirstOrDefault();
        Say(xl != null && LinkLedger.Asleep(xl.SWorld) && xl.Est >= 3, $"the works' world asleep, its stock credited on the ledger: {(xl == null ? "no line" : $"est {xl.Est:0.0}, {xl.Note}")}");
        // A route ship's stop there, settled on the ledger (from orbit): the hauler in the yard's world, its route at that stop.
        var h2 = Build(w1, placeA, hauler, new Vector3(30, 2, 0));
        var rc = PartOf(h2, d => d.LinkKind == LinkKind.Route);
        var r = new LinkRoute { N = 2, Phase = RoutePhase.Settling, Since = LinkLedger.Now };
        r.Stops[0] = new LinkStop { World = works2Key(placeB), Body = works2.Id, Part = (ushort)PartOf(works2, d => d.LinkKind == LinkKind.Dock), Name = "works" };
        LinkRoutes.Unpack(LinkRoutes.LeaveDone | 1u << 8 | 95u << 24, ref r.Stops[0]);
        r.Stops[1] = new LinkStop { World = placeA.Ctx.Map!.Name, Body = yard2.Id, Part = (ushort)PartOf(yard2, d => d.LinkKind == LinkKind.Dock), Name = "yard" };
        LinkRoutes.Unpack(LinkRoutes.LeaveDone | 1u << 8 | 95u << 24, ref r.Stops[1]);
        h2.Parts[rc].Link = new LinkPart { Route = r };
        Run(w1, placeA, 4);
        var p2 = PartOf(h2, d => d.LinkKind == LinkKind.Port);
        Say(xl is { Loaded: 3, EstLoaded: 3 } && LinkDocks.ShipHolds(w1, h2, (ushort)gear.Id) == 3, $"the stop at the closed works settled on the ledger: loaded {xl?.Loaded} (estimated {xl?.EstLoaded}), hold {LinkDocks.ShipHolds(w1, h2, (ushort)gear.Id)}; route: {r.Phase} {r.Why}");
        // The works' world opens: what left on the ledger leaves its stores.
        var before = w2.StoreOf(works2, store2)!.ItemCount;
        Run(w2, placeB, 3);
        var after = w2.StoreOf(works2, store2)!.ItemCount;
        Say(xl is { EstLoaded: 0 } && after == before - 3, $"the works open again: settled parts leave its stores ({before} -> {after}), est loaded {xl?.EstLoaded}");
        Console.WriteLine($"linked-check: {(_fails == 0 ? "all passed" : $"{_fails} FAILED")}");
        LinkLedger.Reset();
        return _fails;
    }

    private static string works2Key(VehicleKit.VehicleDriveCheck.CheckPlace p) => p.Ctx.Map?.Name ?? "";

    private static string Order(Construct c, int t)
    {
        if (c.Parts[t].Craft is not { Count: > 0 } q)
            return "no orders";
        return string.Join("; ", Enumerable.Range(0, q.Count).Select(k => $"#{q.Items[k].Id} {q.Items[k].What.Count} {ConstructParts.Get(q.Items[k].What.Part)?.Name}: got {q.Items[k].Got}, asked {q.Items[k].Asked}, {q.Items[k].State} \"{q.Items[k].Note}\""));
    }
}
