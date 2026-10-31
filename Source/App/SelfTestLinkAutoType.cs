using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Link auto type" (board item dev-link-auto-type-at01): the linking tool has no type choice; the port gives it.
    /// Through the real input (<see cref="Program.LinkTestFrame"/>, a client with a real host over the loopback transport):
    /// clicking an engine lists all its ports (every type, in and out) by name; picking Drive out starts a drive shaft whose
    /// end offers only shaft inputs; a fuel tank's Fuel out starts a pipe that ends on Fuel in with the other ports greyed
    /// with a reason ("a drive shaft can't connect to Fuel in"); branching off a pipe makes a pipe; a one-port device
    /// (a seat, a hinge) still gets a one-option picker, pre-highlighted and confirmed by a click; the old edit modes
    /// (delete, re-route) still work; the client sees what the server made.
    /// </summary>
    private static void CheckLinkAutoType()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"link auto type: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26433;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        bool WaitFor(Func<bool> ready, double seconds = 6.0)
        {
            var until = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < until)
            {
                if (ready())
                    return true;
                Pump(server, [a], 0.05, ref now, clock);
            }
            return ready();
        }
        Pump(server, [a], 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.5, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("link auto type: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        var spot = OpenSpot(server.Constructs, sp.Position);
        var w = server.Constructs;
        Construct? Place(string steps, Vector3 at)
        {
            var bp = ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "AT", "steps": [ { "part": "Foundation" }, {{steps}} ] }""", "at01");
            var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, at.X, at.Z), 0, out var why);
            if (c == null)
                Fail($"link auto type: a test construct didn't build: {why}");
            else
            {
                c.Pinned = true;
                c.Rebuild();
            }
            return c;
        }
        if (Place($"{RouteStep("Transmission", 0, "top00", "-y")}, {RouteStep("Engine", 0, "top30", "-y")}, {RouteStep("Differential", 0, "top02", "bottom0")}, {RouteStep("Fuel Tank", 0, "top33", "-y")}, {RouteStep("Engine", 0, "top20", "-y")}", spot) is not { } mech
            || Place($"{RouteStep("Seat", 0, "top33", "bottom")}, {RouteStep("Lamp", 0, "top30", "-y")}", spot + new Vector3(6, 0, 0)) is not { } elec)
            return;
        sp.Teleport(ConstructWorld.Ground(World.Query, spot.X + 3f, spot.Z - 2f));
        server.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        a.SandboxHands.Hand(a.LocalId).Tool = ConstructTool.Linker;
        Pump(server, [a], 0.6, ref now, clock);
        if (a.Constructs.Get(mech.Id) is not { } cm || a.Constructs.Get(elec.Id) is not { } ce)
        {
            Fail("link auto type: the client doesn't have the test constructs");
            return;
        }
        Program.LinkTestReset();
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        ConstructHit HitOn(Construct c, int part)
        {
            c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
            return new ConstructHit { Body = c, Part = part, Distance = 2f, Point = t + Vector3.Transform(c.Parts[part].Pos, q), Normal = Vector3.UnitY };
        }
        void Click(Construct c, int part, bool aim = false)
        {
            ref var h = ref a.SandboxHands.Hand(a.LocalId);
            Program.LinkTestFrame(a, ref h, HitOn(c, part), !aim, aim);
        }
        void Idle(Construct c, int part)
        {
            ref var h = ref a.SandboxHands.Hand(a.LocalId);
            Program.LinkTestFrame(a, ref h, HitOn(c, part), false); // (a frame with no button: the HUD line is remade)
        }
        void ClickRoute(Construct c, int route)
        {
            if (route < 0 || route >= c.RouteCount)
            {
                problems.Add($"no link {route} to click (the body has {c.RouteCount})");
                return;
            }
            ref var h = ref a.SandboxHands.Hand(a.LocalId);
            var hit = HitOn(c, c.Routes[route].Source);
            var p0 = ConstructRoutes.Point(c, route, -1, out var s0);
            var p1 = ConstructRoutes.Point(c, route, 0, out _);
            c.WorldFrame(s0, out var q, out var t);
            hit.Point = t + Vector3.Transform((p0 + p1) * 0.5f, q);
            hit.RoutePlus1 = (byte)(route + 1);
            hit.RouteSeg = 0;
            Program.LinkTestFrame(a, ref h, hit, true);
        }
        string[] Labels()
        {
            var pk = Program.LinkTestPicker;
            return Enumerable.Range(0, Program.LinkTestPickCount).Select(i => pk.Items[i].Label).ToArray();
        }
        int Idx(string label) => Array.IndexOf(Labels(), label);
        // Picks the option by pointing at it (as the player's mouse does) and clicking.
        void PickOption(string label, Construct c, int part)
        {
            var i = Idx(label);
            if (i < 0)
            {
                problems.Add($"the picker has no \"{label}\" (it has {string.Join(", ", Labels())})");
                Program.LinkTestReset();
                return;
            }
            Program.LinkTestHover(i);
            Click(c, part);
        }
        int Part(Construct c, string name, int nth = 0) => PartNamed(c, name, nth);

        // 1) The ring has no type; clicking an engine lists all its ports at once.
        var wheel = Program.LinkTestWheel;
        Want(wheel.Options.All(o => o.Label is not ("Pipe" or "Data cable" or "Power cable" or "Drive shaft" or "Chute")), "the ring still has a link type");
        var engine = Part(cm, "Engine");
        var trans = Part(cm, "Transmission");
        var tank = Part(cm, "Fuel Tank");
        var engine2 = Part(cm, "Engine", 1);
        Click(cm, engine);
        var engineLabels = Program.LinkTestPicker.IsOpen ? Labels() : [];
        var pk0 = Program.LinkTestPicker;
        Want(Program.LinkTestPicker.IsOpen && engineLabels.Contains("Fuel in") && engineLabels.Contains("Drive out") && engineLabels.Contains("Exhaust out"), $"clicking an engine lists [{string.Join(", ", engineLabels)}] (Fuel in, Drive out and Exhaust out expected together)");
        var fuelInItem = Idx("Fuel in") is var fi and >= 0 ? pk0.Items[fi] : null;
        var driveItem = Idx("Drive out") is var di and >= 0 ? pk0.Items[di] : null;
        Want(fuelInItem is { Enabled: false } && driveItem is { Enabled: true } && driveItem.Hint.Contains("drive shaft"), "an input port isn't greyed at the start, or Drive out isn't a drive shaft port");
        var types = Enumerable.Range(0, Program.LinkTestPickCount).Select(i => pk0.Items[i].Icon).Distinct().Count();
        Want(types >= 2, "the engine's ports don't show more than one link type");
        // 2) Drive out starts a drive shaft; its end offers only shaft inputs.
        PickOption("Drive out", cm, engine);
        Want(Program.LinkTestRouting && Program.LinkTestType == RouteType.Shaft && Program.LinkTestLabel == "Drive shaft from Engine: Drive out", $"picking Drive out started \"{Program.LinkTestLabel}\" ({Program.LinkTestType})");
        Idle(cm, engine2);
        Want(a.SandboxWireFrom.StartsWith("Drive shaft from Engine: Drive out"), $"the HUD line doesn't name the link: \"{a.SandboxWireFrom}\"");
        // (a fuel inlet is greyed with the reason; the engine is not a shaft input, so it is only a surface)
        var fuelInPort = ConstructRoutes.PortNamed(cm.DefOf(engine2), RouteType.Pipe, "Fuel in")!;
        Span<ConstructRoutes.LinkPort> all = new ConstructRoutes.LinkPort[ConstructRoutes.MaxLinkPorts];
        var n2 = ConstructRoutes.AllPorts(cm, engine2, all);
        string? whyFuel = null;
        for (var i = 0; i < n2; i++)
            if (all[i].Name == "Fuel in")
                whyFuel = ConstructRoutes.EndWhy(cm, engine2, RouteType.Shaft, "", all[i]);
        Want(whyFuel == "a drive shaft can't connect to Fuel in" && fuelInPort.Type == RouteType.Pipe, $"a fuel inlet's reason for a drive shaft is \"{whyFuel}\"");
        // A device whose ports are all of other types is no end (a surface).
        Want(!ConstructRoutes.ClickEnds(cm, RouteType.Shaft, engine, 255, engine2, false), "an engine ends a drive shaft");
        // (the engine's shaft can't reach a transmission's side without a corner gearbox; cancel it with Esc's reset)
        Program.LinkTestReset();
        // A transmission's Output shaft to the differential's Input shaft: the end picker lists the differential's ports,
        // Input shaft enabled and highlighted, the output shafts greyed, and a click confirms with no extra aim.
        var diff = Part(cm, "Differential");
        Click(cm, trans);
        PickOption("Output shaft", cm, trans);
        Idle(cm, diff);
        Want(Program.LinkTestRouting && Program.LinkTestType == RouteType.Shaft && a.SandboxWireFrom.StartsWith("Drive shaft from Transmission: Output shaft"), $"the transmission's Output shaft started \"{Program.LinkTestLabel}\"");
        Click(cm, diff);
        var endOpen = Program.LinkTestPicker.IsOpen;
        var endLabels = endOpen ? Labels() : [];
        var inputIdx = Idx("Input shaft");
        Want(endOpen && inputIdx >= 0 && Program.LinkTestPicker.Items[inputIdx].Enabled && Program.LinkTestPicker.Hovered == inputIdx,
            $"the differential's end picker [{string.Join(", ", endLabels)}] doesn't offer Input shaft highlighted ({(endOpen ? "open" : "closed")}; {a.SandboxNotice})");
        var outIdx = Idx("Output left");
        Want(outIdx < 0 || !Program.LinkTestPicker.Items[outIdx].Enabled && Program.LinkTestPicker.Items[outIdx].Note.Length > 0, "an output shaft isn't greyed at the end");
        var routesBefore = cm.RouteCount;
        Click(cm, diff); // (no extra aim: the highlighted port is confirmed)
        var shaftMade = WaitFor(() => cm.RouteCount == routesBefore + 1 && server.Constructs.Get(mech.Id)!.RouteCount == routesBefore + 1);
        Want(shaftMade && cm.Routes[cm.RouteCount - 1].Type == RouteType.Shaft && cm.Routes[cm.RouteCount - 1].To == diff, $"the shaft wasn't made ({a.SandboxNotice})");
        Want(!Program.LinkTestRouting && !Program.LinkTestPicker.IsOpen, "the tool is still routing after the link ended");

        // 3) A tank's Fuel out starts a pipe; Fuel in is offered, the other engine ports are greyed with a reason.
        Click(cm, tank);
        var tankLabels = Program.LinkTestPicker.IsOpen ? Labels() : [];
        Want(tankLabels.Contains("Fuel out"), $"the tank's picker is [{string.Join(", ", tankLabels)}]");
        PickOption("Fuel out", cm, tank);
        Want(Program.LinkTestRouting && Program.LinkTestType == RouteType.Pipe && Program.LinkTestLabel.StartsWith("Pipe from Fuel Tank"), $"Fuel out started \"{Program.LinkTestLabel}\" ({Program.LinkTestType})");
        Click(cm, engine);
        var pe = Program.LinkTestPicker;
        var fi2 = Idx("Fuel in");
        var drive2 = Idx("Drive out");
        Want(pe.IsOpen && fi2 >= 0 && pe.Items[fi2].Enabled && pe.Hovered == fi2, $"the engine's end picker [{string.Join(", ", Labels())}] doesn't highlight Fuel in");
        Want(drive2 >= 0 && !pe.Items[drive2].Enabled && pe.Items[drive2].Note == "a pipe can't connect to Drive out", $"Drive out's reason is \"{(drive2 >= 0 ? pe.Items[drive2].Note : "missing")}\"");
        var pipesBefore = cm.RouteCount;
        Click(cm, engine);
        var pipeMade = WaitFor(() => cm.RouteCount == pipesBefore + 1);
        ref var pr = ref cm.Routes[Math.Max(0, cm.RouteCount - 1)];
        Want(pipeMade && pr.Type == RouteType.Pipe && pr.To == engine && ConstructRoutes.PortName(cm.DefOf(engine), RouteType.Pipe, pr.ToPort, true) == "Fuel in", $"the pipe wasn't made on Fuel in ({a.SandboxNotice})");
        var pipeRoute = cm.RouteCount - 1;

        // 4) Branching off the pipe takes its type (and the tool says so).
        ClickRoute(cm, pipeRoute);
        Want(Program.LinkTestRouting && Program.LinkTestType == RouteType.Pipe && Program.LinkTestParent == pipeRoute, $"branching off a pipe started a {Program.LinkTestType} (parent {Program.LinkTestParent})");
        Program.LinkTestReset();

        // 5) A one-port device still shows a one-option picker, highlighted, confirmed by a click: a seat's data out (its only port) to a lamp (power in is greyed with the reason).
        var sw = Part(ce, "Seat");
        var lamp = Part(ce, "Lamp");
        Click(ce, sw);
        var one = Program.LinkTestPicker;
        Want(one.IsOpen && Program.LinkTestPickCount == 1 && one.Items.Length == 1 && one.Items[0].Enabled && one.Hovered == 0 && one.Items[0].Label.Length > 0, $"a seat's picker has {Program.LinkTestPickCount} options ([{string.Join(", ", Labels())}], hovered {one.Hovered})");
        Click(ce, sw);
        Want(Program.LinkTestRouting && Program.LinkTestType == RouteType.Data, "a click on the one option didn't start a data cable");
        Click(ce, lamp);
        var oneEnd = Program.LinkTestPicker;
        var lampData = Idx("Data in");
        var lampPower = Idx("Power in");
        Want(oneEnd.IsOpen && lampData >= 0 && oneEnd.Hovered == lampData && oneEnd.Items[lampData].Enabled && lampPower >= 0 && !oneEnd.Items[lampPower].Enabled
             && oneEnd.Items[lampPower].Note == "a data cable can't connect to Power in", $"a lamp's end picker [{string.Join(", ", Labels())}] (hovered {oneEnd.Hovered}, power note \"{(lampPower >= 0 ? oneEnd.Items[lampPower].Note : "")}\")");
        var dataBefore = ce.RouteCount;
        Click(ce, lamp);
        var dataMade = WaitFor(() => ce.RouteCount == dataBefore + 1);
        Want(dataMade && ce.Routes[ce.RouteCount - 1].Type == RouteType.Data, $"the data cable wasn't made ({a.SandboxNotice})");

        // 6) The old edit modes: delete a link; re-route one.
        Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Delete);
        var beforeDelete = ce.RouteCount;
        ClickRoute(ce, ce.RouteCount - 1);
        var deleted = WaitFor(() => ce.RouteCount == beforeDelete - 1 && server.Constructs.Get(elec.Id)!.RouteCount == beforeDelete - 1);
        Want(deleted, "the delete mode didn't delete the link");
        Program.LinkTestWheel.Choose((int)SandboxLinkWheel.Option.Reroute);
        ClickRoute(cm, 0);
        var rerouting = Program.LinkTestRouting && Program.LinkTestType == RouteType.Shaft && Program.LinkTestWheel.Mode == LinkMode.Link;
        Want(rerouting, $"re-route didn't start a shaft again ({Program.LinkTestType}, mode {Program.LinkTestWheel.Mode})");
        Program.LinkTestReset();

        // 7) The client sees what the server has.
        Pump(server, [a], 0.5, ref now, clock);
        string Mirror(Construct s, Construct cl)
        {
            if (s.RouteCount != cl.RouteCount)
                return $"{cl.RouteCount} links (the server: {s.RouteCount})";
            for (var k = 0; k < s.RouteCount; k++)
                if (s.Routes[k].Type != cl.Routes[k].Type || s.Routes[k].Source != cl.Routes[k].Source || s.Routes[k].To != cl.Routes[k].To || s.Routes[k].ToPort != cl.Routes[k].ToPort)
                    return $"link {k} differs";
            return "";
        }
        var mirror = Mirror(server.Constructs.Get(mech.Id)!, cm) + Mirror(server.Constructs.Get(elec.Id)!, ce);
        Want(mirror.Length == 0, $"the client doesn't see the server's links: {mirror}");

        Line($"link auto type: engine ports [{string.Join(", ", engineLabels)}]; drive shaft end [{string.Join(", ", endLabels)}] made {shaftMade}; pipe made {pipeMade}; one-option picker data made {dataMade}; delete {deleted}; re-route {rerouting}; mirror '{mirror}'; {problems.Count} problems");
        if (problems.Count > 0)
            Fail($"link auto type: {string.Join("; ", problems.Take(5))}");
    }
}
