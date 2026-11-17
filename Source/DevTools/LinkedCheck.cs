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
        if (args.Any(a => a == "--fq"))
        {
            // (a probe: why a rotorcraft doesn't lift under its waypoint autopilot)
            var fq = new ConstructBuild.GroundJob(wa, ConstructBuild.FindBlueprint(args.SkipWhile(a => a != "--fq").Skip(1).FirstOrDefault() ?? "Fan Quad")!, null);
            while (!fq.Step(1000))
            {
            }
            Construct c;
            using (placeA.In())
                c = fq.Join(new Vector3(0, 0, 0), 0, out _)!;
            // (--to X,Z: the waypoint, default 100,0; --yaw D: the craft turned that far first)
            var to = args.SkipWhile(a => a != "--to").Skip(1).FirstOrDefault()?.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray() ?? [100f, 0f];
            if (args.SkipWhile(a => a != "--yaw").Skip(1).FirstOrDefault() is { } yaw)
                c.Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.Parse(yaw, System.Globalization.CultureInfo.InvariantCulture) * MathF.PI / 180f);
            using (placeA.In())
                ConstructFlight.NavEngage(wa, c, args.Contains("--wpt") ? FlightMode.Waypoint : FlightMode.Hover, new Vector3(to[0], 0, to[1]), ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom) + 20));
            for (var sec = 0; sec < 12; sec++)
            {
                using (placeA.In())
                    for (var t = 0; t < World.TickRate; t++)
                        wa.Step(q, World.TickDt);
                var rs = string.Join(", ", Enumerable.Range(0, c.RotorCount).Select(k => FormattableString.Invariant($"[cmd {c.Rotors[k].Command:0.00} w {c.Rotors[k].Omega:0} T {c.Rotors[k].Thrust:0} scanned {c.Rotors[k].Scanned} in {c.Parts[c.Rotors[k].Part].In:0.00}/{c.Parts[c.Rotors[k].Part].InCount}]")));
                Console.WriteLine(FormattableString.Invariant($"fq {sec}: x {c.WorldCom.X:0.0} z {c.WorldCom.Z:0.0} y {c.WorldCom.Y:0.00} up {Vector3.Transform(Vector3.UnitY, c.Rot).Y:0.00} vel {c.Vel.Y:0.0} anch {c.Anchored} carried {c.CarriedBy} mass {c.Mass:0} owns {c.StabOwns} elec {c.Electrical} charge {c.Charge:0} AP {ConstructFlight.ApText(c)} rotors {rs}"));
            }
            return 0;
        }
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
        var yd = Build(wa, placeA, yard, new Vector3(120, 0, 0));
        var store = PartOf(wk, d => d.IsVolumeStore);
        var recv = PartOf(wk, d => d.LinkKind == LinkKind.Receiver);
        var trans = PartOf(yd, d => d.LinkKind == LinkKind.Transmitter);
        var term = PartOf(yd, d => d.IsCraftTerminal && d.LinkKind == LinkKind.None);
        var wDock = PartOf(wk, d => d.LinkKind == LinkKind.Dock);
        var yDock = PartOf(yd, d => d.LinkKind == LinkKind.Dock);
        for (var k = 0; k < 8; k++)
            wa.PutPart(wk, store, gear);
        var dispPart = PartOf(wk, d => d.LinkKind == LinkKind.Dispatcher);
        wk.Parts[dispPart].Broken = true; // (the works' Dispatcher off until its own test: it would send the hauler the teleports below move by hand)
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

        // (the hauler set upright in the works' clamp again, as a pilot lands it: the teleports above can leave it on its back)
        using (placeA.In())
        {
            ConstructCarrier.LetGo(wa, ha);
            ha.Pos = LinkDocks.PartAt(wk, clampW) + new Vector3(0, 1.2f, 0);
            ha.Rot = Quaternion.Identity;
            ha.Vel = ha.AngVel = Vector3.Zero;
            ha.DockAway = 0;
            ha.Wake();
        }
        for (var k = 0; k < 15 && ha.CarriedBy != wk.Id; k++)
            Run(wa, placeA, 1);
        for (var k = 0; k < 48 && args.Contains("--trace"); k++)
        {
            Run(wa, placeA, 0.25);
            Console.WriteLine(FormattableString.Invariant($"     t {k * 0.25:0.00}: y {ha.WorldCom.Y:0.00} up {Vector3.Transform(Vector3.UnitY, ha.Rot).Y:0.00} angvel {ha.AngVel.Length():0.0} held {ha.CarriedBy} phase {ha.Parts[PartOf(ha, d => d.LinkKind == LinkKind.Route)].Link?.Route?.Phase} rotors {string.Join(" ", Enumerable.Range(0, ha.RotorCount).Select(i => $"{ha.Rotors[i].Command:0.0}/{ha.Rotors[i].Thrust:0}"))}"));
        }
        Console.WriteLine($"     hauler set in the works' clamp again: held by {ha.CarriedBy}, up {Vector3.Transform(Vector3.UnitY, ha.Rot).Y:0.00}, at {ha.WorldCom:0.0}, route {ha.Parts[PartOf(ha, d => d.LinkKind == LinkKind.Route)].Link?.Route?.Phase}");
        wk.Parts[dispPart].Broken = false;
        Run(wa, placeA, 2);
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
        // The run flown for real: off the works' dock by its rule, to 30 m over the yard's dock, down into its clamp, unloaded, and back.
        var unloaded = false;
        for (var k = 0; k < 60 && sent != null && sent.Phase != RoutePhase.Done; k++)
        {
            Run(wa, placeA, 4);
            unloaded |= sent.At == 2 && ha.Parts[port].Link!.Manifest.Sum(m => m.Aboard) == 0;
            if (k % 3 == 0)
                Console.WriteLine($"     {k * 4 + 4} s: up {Vector3.Transform(Vector3.UnitY, ha.Rot).Y:0.00} AP {ConstructFlight.ApText(ha)} hauler at {ha.WorldCom.X:0},{ha.WorldCom.Y:0},{ha.WorldCom.Z:0}, held by {ha.CarriedBy}, aboard {ha.Parts[port].Link!.Manifest.Sum(m => m.Aboard)}; stop {sent.At + 1} {sent.Phase}: {sent.Why}");
        }
        Say(unloaded, $"the run flew for real: off the works' dock by its rule, 120 m to the yard, down into its clamp, unloaded: {(unloaded ? "yes" : "no")}");
        var flew = sent is { Phase: RoutePhase.Done } && unloaded && ha.CarriedBy == wk.Id;
        Say(flew, $"and back to the works: route {sent?.Phase}, held by {ha.CarriedBy}, up {Vector3.Transform(Vector3.UnitY, ha.Rot).Y:0.00}, charge {ConstructPower.Share(ha) * 100:0}%");
        Console.WriteLine($"     the yard's order: {Order(yd, term)}");

        // A standing route (level b), works -> yard -> works and round again: loads at the works, leaves by its rules, flies, docks,
        // unloads, comes back and charges at the works' clamp (its Battery Packs), twice over.
        wk.Parts[dispPart].Broken = true; // (the Dispatcher's one-off runs out of the way)
        for (var k = 0; k < 4; k++)
            wa.PutPart(wk, store, gear);
        Run(wa, placeA, 3); // (the works' next stock report: every 2 s)
        using (placeA.In())
            why = ConstructCrafting.Place(wa, yd, term, new WorkOrder { Part = (ushort)gear.Id, Count = 3 });
        Console.WriteLine($"     an order for 3 more at the yard: {why ?? "placed"}; {Order(yd, term)}");
        Run(wa, placeA, 4);
        var rl = ha.Parts[rcp].Link!;
        var wRef = rl.Docks.FirstOrDefault(d => d.Body == wk.Id && d.Part == wDock);
        var yRef = rl.Docks.FirstOrDefault(d => d.Body == yd.Id && d.Part == yDock);
        var rules = LinkRoutes.LeaveFill | LinkRoutes.LeaveTime | LinkRoutes.LeaveDone | 1u << 8 | 95u << 24;
        using (placeA.In())
        {
            LinkedLogistics.Act(wa, ha, rcp, LinkOp.RouteClear, 0, 0, "");
            why = LinkedLogistics.Act(wa, ha, rcp, LinkOp.RouteAdd, (uint)wRef.Body << 16 | wRef.Part, rules, wRef.World ?? "");
            why ??= LinkedLogistics.Act(wa, ha, rcp, LinkOp.RouteAdd, (uint)yRef.Body << 16 | yRef.Part, rules, yRef.World ?? "");
            why ??= LinkedLogistics.Act(wa, ha, rcp, LinkOp.RouteStart, 0, 0, "");
        }
        Say(why == null, $"a standing route, works -> yard, set and started on the Route Computer: {why ?? "started"}");
        var standing = ha.Parts[rcp].Link?.Route;
        int atYard = 0, atWorks = 0;
        var heldBy = ha.CarriedBy;
        var lowest = 1f;
        var line4 = LinkLedger.Lines.LastOrDefault(l => l.Count == 3 && l.Kind == gear.Id);
        for (var k = 0; k < 900 && atWorks < 2; k++)
        {
            Run(wa, placeA, 1);
            lowest = MathF.Min(lowest, Vector3.Transform(Vector3.UnitY, ha.Rot).Y);
            if (ha.CarriedBy != heldBy)
            {
                heldBy = ha.CarriedBy;
                atYard += heldBy == yd.Id ? 1 : 0;
                atWorks += heldBy == wk.Id ? 1 : 0;
                Console.WriteLine($"     {k + 1} s: held by {(heldBy == yd.Id ? "the yard" : heldBy == wk.Id ? "the works" : heldBy.ToString())}; charge {ConstructPower.Share(ha) * 100:0}%; stop {standing?.At + 1} {standing?.Phase}: {standing?.Why}");
            }
            else if (k % 30 == 0)
                Console.WriteLine($"     {k + 1} s: up {Vector3.Transform(Vector3.UnitY, ha.Rot).Y:0.00} at {ha.WorldCom.X:0},{ha.WorldCom.Y:0},{ha.WorldCom.Z:0} charge {ConstructPower.Share(ha) * 100:0}%; stop {standing?.At + 1} {standing?.Phase}: {standing?.Why}");
        }
        Say(atYard >= 2 && atWorks >= 2 && lowest > 0.7f, $"the route ran works -> yard -> works twice: docked at the yard {atYard}, at the works {atWorks}, never tipped (least up {lowest:0.00}); charge {ConstructPower.Share(ha) * 100:0}%");
        Say(line4 is { State: LinkLineState.Closed, Delivered: 3 }, $"its order of 3 delivered by the route: {(line4 == null ? "no line" : $"line {line4.Id}: {line4.State}, delivered {line4.Delivered}, {line4.Note}")}");
        using (placeA.In())
            LinkedLogistics.Act(wa, ha, rcp, LinkOp.RouteClear, 0, 0, "");

        // A sub-blueprint job over the link: the yard's station builds the Submodule Skiff; its pods go to the works' station (Sub-jobs Linked).
        var ySt = PartOf(yd, d => d.IsBuildStation);
        var wSt = PartOf(wk, d => d.IsBuildStation);
        var beam = ConstructParts.Find("Square Beam 0.25 m")!;
        for (var k = 0; k < 8; k++)
        {
            wa.RackPut(wk, wSt, beam);
            wa.RackPut(yd, ySt, beam);
        }
        using (placeA.In())
            why = ConstructStation.Start(wa, yd, ySt, "Submodule Skiff", 0);
        Say(why == null, $"the yard's station starts the Submodule Skiff: {why ?? "started"}");
        LinkLine? sub = null;
        for (var k = 0; k < 60 && !(sub is { Ready: 1 }); k++)
        {
            Run(wa, placeA, 1);
            sub = LinkLedger.Lines.FirstOrDefault(l => l.Use != 0);
        }
        Say(sub is { Ready: 1 }, $"a pod offered over the link, built at the works: {(sub == null ? "no line" : $"line {sub.Id}: {sub.State}, ready {sub.Ready}, {sub.Note}")}");
        Console.WriteLine($"     yard station: {StationProbe.Text(wa, yd, ySt)}; rows: {string.Join(" | ", (yd.Parts[ySt].Station?.Tree ?? []).Select(r => $"{r.Name} #{r.Number} {r.State} {r.Note}"))}");
        Console.WriteLine($"     works station: {StationProbe.Text(wa, wk, wSt)}");
        if (sub is { Ready: 1 } && wa.Bodies.FirstOrDefault(b => b.SubLine == sub.Id) is { } pod)
        {
            // Flown in and set down by the yard's bay (as a player brings it in a ship's Cargo Volume and lets it go there).
            ConstructStation.Bay(yd, ySt, out var floor, out _, out _);
            using (placeA.In())
            {
                pod.Pos = floor + new Vector3(0, 6, 0);
                pod.Wake();
            }
            Run(wa, placeA, 3);
            Say(sub is { Delivered: 1 }, $"the pod is handed to the yard's station: line {sub.State}, {sub.Note}");
            Run(wa, placeA, 20);
            Console.WriteLine($"     yard station: {StationProbe.Text(wa, yd, ySt)}; rows: {string.Join(" | ", (yd.Parts[ySt].Station?.Tree ?? []).Select(r => $"{r.Name} #{r.Number} {r.State} {r.Note}"))}");
        }

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
