using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Construct routed links" (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): each type's routing rule (a
    /// pipe's 15 degree steps, a chute's 45, a shaft's 45 degree limit, its refused corner and its refused bare junction),
    /// nodes placed off the surface never sink into it and a segment through a part is refused; each type carries what it
    /// carries from output to input (a signal, power past a wrecked part, fuel to an engine, torque on the migrated truck,
    /// scrap down a chute); junctions copy (data) or split evenly (power, fuel, items); links follow a driving and a
    /// jointed construct; a destroyed segment cuts the flow (a cut fuel pipe leaks, a power cable sparks) and a repair
    /// mends it; wrong pipes (fuel weeping through a water pipe catches fire, an oil pipe makes the engine sputter); the
    /// linking tool's server checks; blueprint and network round trips; the migrations (wires, fuel lines, drive shafts);
    /// part ids past one byte; no allocation per tick. Short scripted scenarios on flat ground.
    /// </summary>
    private static void CheckConstructRoutes()
    {
        RoutesRules();
        RoutesSignalsAndPower();
        RoutesPipes();
        RoutesShafts();
        RoutesChutes();
        RoutesFollow();
        RoutesRoundTrips();
        RoutesMigration();
        RoutesWideIds();
        RoutesBudget();
    }

    private static readonly DriveWorld RoutesGround = new();

    /// <summary>A blueprint from JSON text (steps and links), built on flat ground at (x, z); null and a failure if it doesn't build.</summary>
    private static Construct? RoutesBuild(ConstructWorld w, string json, float x, float z, string what)
    {
        ConstructBuild.Blueprint bp;
        try
        {
            bp = ConstructBuild.ParseBlueprint(json, what);
        }
        catch (ConstructPartException e)
        {
            Fail($"routed links: {what} refused: {e.Message}");
            return null;
        }
        var c = ConstructBuild.BuildOnGround(w, bp, new Vector3(x, 0, z), 0, out var why);
        if (c == null)
            Fail($"routed links: {what} doesn't build: {why}");
        return c;
    }

    private static string RouteStep(string part, int on, string socket, string by) => $$"""{ "part": "{{part}}", "on": {{on}}, "socket": "{{socket}}", "by": "{{by}}" }""";

    // The signal and power bench: a foundation with a switch, two lamps, a battery, and a lamp on top of a fuel tank (the
    // tank is the only frame between that lamp and the battery: wreck it and the frame circuit is cut).
    private static readonly string BenchA = $$"""
        { "format": "call-of-slop-construct-blueprint/1", "name": "Bench A",
          "steps": [ { "part": "Foundation" }, {{RouteStep("Switch", 0, "top00", "-y")}}, {{RouteStep("Lamp", 0, "top03", "-y")}},
                     {{RouteStep("Lamp", 0, "top33", "-y")}}, {{RouteStep("Battery", 0, "top30", "-y")}}, {{RouteStep("Fuel Tank", 0, "top22", "-y")}},
                     {{RouteStep("Lamp", 5, "+y", "-y")}} ],
          "links": [
            { "type": "data", "from": 1, "to": 2, "nodes": [[-0.45, 0.32, -0.75], [-0.45, 0.32, 0.75]] },
            { "type": "data", "branch": [0, 1], "at": [-0.45, 0.32, -0.2], "to": 3, "nodes": [[0.75, 0.32, -0.2]] }
          ] }
        """;

    private static void RoutesRules()
    {
        // The bend rules, one type at a time.
        var pipeOk = ConstructRoutes.BendRule(RouteType.Pipe, 15) == null && ConstructRoutes.BendRule(RouteType.Pipe, 30) == null && ConstructRoutes.BendRule(RouteType.Pipe, 90) == null;
        var pipeBad = ConstructRoutes.BendRule(RouteType.Pipe, 22) is { } pb && pb.Contains("15 degree");
        var chuteOk = ConstructRoutes.BendRule(RouteType.Chute, 45) == null && ConstructRoutes.BendRule(RouteType.Chute, 90) == null;
        var chuteBad = ConstructRoutes.BendRule(RouteType.Chute, 30) is { } cb && cb.Contains("45 degree");
        var shaftOk = ConstructRoutes.BendRule(RouteType.Shaft, 30) == null && ConstructRoutes.BendRule(RouteType.Shaft, 45) == null;
        var shaftBad = ConstructRoutes.BendRule(RouteType.Shaft, 60) is { } sb && sb.Contains("45 degrees");
        var shaftCorner = ConstructRoutes.BendRule(RouteType.Shaft, 90) is { } sc && sc.Contains("gearbox");
        var freeOk = ConstructRoutes.BendRule(RouteType.Data, 37) == null && ConstructRoutes.BendRule(RouteType.Power, 133) == null;
        // Snapping: a 22 degree turn of a pipe comes out at a 15 degree step, a chute's at 0 or 45; on the 1 cm grid.
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchA, 0, 0, "Bench A") is not { } c)
            return;
        var prev = new Vector3(-0.5f, 0.34f, -0.5f);
        var surface = prev + new Vector3(MathF.Sin(22 * MathF.PI / 180), 0, MathF.Cos(22 * MathF.PI / 180)) * 0.6f - new Vector3(0, 0.04f, 0);
        var snapped = ConstructRoutes.Snap(c, RouteType.Pipe, 0, prev, Vector3.UnitZ, surface, Vector3.UnitY);
        var bend = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(snapped - prev), Vector3.UnitZ), -1, 1)) * 180 / MathF.PI;
        var onStep = MathF.Abs(bend - MathF.Round(bend / 15) * 15) <= ConstructRoutes.AngleSlackDeg && ConstructRoutes.BendRule(RouteType.Pipe, bend) == null;
        var chuteSnap = ConstructRoutes.Snap(c, RouteType.Chute, 0, prev, Vector3.UnitZ, surface, Vector3.UnitY);
        var chuteBend = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(chuteSnap - prev), Vector3.UnitZ), -1, 1)) * 180 / MathF.PI;
        var grid = snapped == ConstructWorld.Quantise(snapped);
        // Nodes off the surface: a point on the foundation's top comes out at each type's radius above it, never inside.
        var sunk = new List<string>();
        foreach (var t in new[] { RouteType.Data, RouteType.Power, RouteType.Pipe, RouteType.Shaft, RouteType.Chute })
        {
            var p = ConstructRoutes.Snap(c, t, 0, new Vector3(-0.2f, 1f, -0.5f), Vector3.Zero, new Vector3(-0.17f, 0.3f, -0.43f), Vector3.UnitY);
            if (ConstructRoutes.SignedDistance(c, p, -1, -1, out _) < ConstructRoutes.Radius(t) * 0.9f - 0.012f || MathF.Abs(p.Y - (0.3f + ConstructRoutes.Radius(t))) > 0.011f)
                sunk.Add($"{t} at {p.Y:0.000}");
        }
        // A sunk node and a segment through a part are refused with their reasons.
        var sw = PartNamed(c, "Switch");
        var lamp = PartNamed(c, "Lamp", 2);
        Span<CRouteNode> nodes = stackalloc CRouteNode[2];
        nodes[0] = new CRouteNode { Pos = new Vector3(-0.45f, 0.29f, -0.75f), Host = 0 };
        var sinks = ConstructRoutes.Check(w, c, RouteType.Data, 0, sw, 255, 255, 0, default, nodes[..1], lamp, 255) ?? "";
        nodes[0] = new CRouteNode { Pos = new Vector3(0.25f, 0.32f, -0.4f), Host = 0 };
        nodes[1] = new CRouteNode { Pos = new Vector3(0.25f, 0.32f, 0.9f), Host = 0 };
        var through = ConstructRoutes.Check(w, c, RouteType.Power, 0, PartNamed(c, "Battery"), 255, 255, 0, default, nodes[..2], lamp, 255) ?? "";
        // The type's ends: a pipe can't start at a switch, a chute can't end at a lamp.
        var noOut = ConstructRoutes.Check(w, c, RouteType.Pipe, 0, sw, 255, 255, 0, default, default, lamp, 255) ?? "";
        var noIn = ConstructRoutes.Check(w, c, RouteType.Power, 0, PartNamed(c, "Battery"), 255, 255, 0, default, default, PartNamed(c, "Fuel Tank"), 255) ?? "";
        Line($"routed links rules: pipe steps {pipeOk} (22 refused: {pipeBad}), chute {chuteOk} (30 refused: {chuteBad}), shaft 30/45 {shaftOk}, 60 refused {shaftBad}, 90 needs a gearbox {shaftCorner}, cables free {freeOk}; "
             + $"a 22 degree pipe snapped to {bend:0.0} (on a step {onStep}), a chute to {chuteBend:0.0}, on the grid {grid}; nodes off the surface (sunk: {sunk.Count}); refusals: \"{sinks}\", \"{through}\", \"{noOut}\", \"{noIn}\"");
        if (!pipeOk || !pipeBad || !chuteOk || !chuteBad || !shaftOk || !shaftBad || !shaftCorner || !freeOk)
            Fail("routed links: a type's bend rule is wrong");
        if (!onStep || MathF.Abs(chuteBend - MathF.Round(chuteBend / 45) * 45) > ConstructRoutes.AngleSlackDeg || !grid)
            Fail($"routed links: the snap doesn't keep the angle rule (pipe {bend:0.0}, chute {chuteBend:0.0})");
        if (sunk.Count > 0)
            Fail($"routed links: snapped nodes intersect their surface: {string.Join(", ", sunk)}");
        if (!sinks.Contains("sink") || !through.Contains("pass through") || !noOut.Contains("pipe output") || !noIn.Contains("takes no power"))
            Fail("routed links: a sunk node, a segment through a part or a wrong end wasn't refused for the right reason");
    }

    private static void RoutesSignalsAndPower()
    {
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchA, 0, 0, "Bench A") is not { } c)
            return;
        var sw = PartNamed(c, "Switch");
        int l1 = PartNamed(c, "Lamp"), l2 = PartNamed(c, "Lamp", 1), l3 = PartNamed(c, "Lamp", 2);
        var bat = PartNamed(c, "Battery");
        // Data: the switch's signal runs down the routed cable to the first lamp, and the junction copies it to the second.
        c.Parts[sw].State = 1;
        DriveSteps(w, RoutesGround, 4);
        var copied = c.Parts[l1].In >= 0.5f && c.Parts[l2].In >= 0.5f && c.WireCount == 2 && c.Routes[1].Share == 1f;
        // Cut the branch's segment: the second lamp goes dark, the first stays lit.
        ConstructRoutes.Damage(w, c, 1, 0, 1000);
        DriveSteps(w, RoutesGround, 3);
        var cut = c.Parts[l1].In >= 0.5f && c.Parts[l2].In < 0.5f;
        // Repaired: lit again.
        ConstructRoutes.Repair(w, c, 1);
        DriveSteps(w, RoutesGround, 3);
        var mended = c.Parts[l2].In >= 0.5f;
        // Power: the lamp on the tank is on the battery's circuit through the tank; wreck the tank and it isn't, a power
        // cable from the battery brings it back; a branch off the cable splits the flow evenly.
        var before = ConstructPower.On(c, l3) && c.Electrical;
        w.DamagePart(c, PartNamed(c, "Fuel Tank"), 10000, out _);
        DriveSteps(w, RoutesGround, 2);
        var dark = !ConstructPower.On(c, l3);
        Span<CRouteNode> nodes = stackalloc CRouteNode[4];
        nodes[0] = new CRouteNode { Pos = new Vector3(0.75f, 0.335f, -0.45f), Host = 0 };
        nodes[1] = new CRouteNode { Pos = new Vector3(0.25f, 0.335f, -0.45f), Host = 0 };
        nodes[2] = new CRouteNode { Pos = new Vector3(0.25f, 0.65f, -0.04f), Host = (byte)PartNamed(c, "Fuel Tank") };
        nodes[3] = new CRouteNode { Pos = new Vector3(0.25f, 0.84f, -0.04f), Host = (byte)PartNamed(c, "Fuel Tank") };
        var why = ConstructRoutes.Check(w, c, RouteType.Power, 0, bat, 255, 255, 0, default, nodes, l3, 255);
        var cable = why == null ? ConstructRoutes.Add(w, c, RouteType.Power, 0, bat, 255, 255, 0, default, nodes, l3, 255) : -1;
        var junction = new Vector3(0.5f, 0.335f, -0.45f);
        var why2 = cable < 0 ? "no cable" : ConstructRoutes.Check(w, c, RouteType.Power, 0, -1, 255, cable, 1, junction, default, l2, 255);
        var branch = why2 == null ? ConstructRoutes.Add(w, c, RouteType.Power, 0, -1, 255, cable, 1, junction, default, l2, 255) : -1;
        c.Rebuild();
        DriveSteps(w, RoutesGround, 2);
        var back = cable >= 0 && ConstructPower.On(c, l3);
        var split = branch >= 0 && MathF.Abs(c.Routes[cable].Share - 0.5f) < 1e-3f && MathF.Abs(c.Routes[branch].Share - 0.5f) < 1e-3f;
        // Cut the cable before the junction: the lamp on the tank is dark again, and the cut cable sparks.
        if (cable >= 0)
            ConstructRoutes.Damage(w, c, cable, 0, 1000);
        DriveSteps(w, RoutesGround, 2);
        var cutPower = !ConstructPower.On(c, l3) && c.Sparking;
        Line($"routed links signals: the switch lights both lamps through a junction (copied) {copied}; the branch cut {cut}, repaired {mended}; "
             + $"power: the tank's lamp on before {before}, dark with the tank wrecked {dark}, back on a power cable {back} ({why ?? "ok"}; branch {why2 ?? "ok"}), split evenly {split}, cut again and sparking {cutPower}");
        if (!copied || !cut || !mended)
            Fail("routed links: a data link doesn't carry (or copy, or cut) its signal");
        if (!before || !dark || !back || !split || !cutPower)
            Fail("routed links: a power cable doesn't carry power past a wrecked part (or split it, or cut it)");
    }

    // The fuel bench: a tank and two engines on a foundation, the pipe along its top with a junction to the second engine.
    private static string BenchB(string style) => $$"""
        { "format": "call-of-slop-construct-blueprint/1", "name": "Bench B",
          "steps": [ { "part": "Foundation" }, {{RouteStep("Fuel Tank", 0, "top00", "-y")}}, {{RouteStep("Engine", 0, "top03", "-y")}}, {{RouteStep("Engine", 0, "top33", "-y")}} ],
          "links": [
            { "type": "pipe", "style": "{{style}}", "from": 1, "to": 2, "nodes": [[-0.45, 0.34, -0.75], [-0.45, 0.34, 0.75]] },
            { "type": "pipe", "style": "{{style}}", "branch": [0, 1], "at": [-0.45, 0.34, 0], "to": 3, "nodes": [[0.45, 0.34, 0], [0.45, 0.34, 0.75]] }
          ] }
        """;

    private static void RoutesPipes()
    {
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchB("fuel"), 0, 0, "Bench B") is not { } c)
            return;
        int e1 = PartNamed(c, "Engine"), e2 = PartNamed(c, "Engine", 1);
        var feeds = ConstructRoutes.PipeFeeds(c, e1) && ConstructRoutes.PipeFeeds(c, e2);
        var split = MathF.Abs(c.Routes[0].Share - 0.5f) < 1e-3f && MathF.Abs(c.Routes[1].Share - 0.5f) < 1e-3f;
        // A bend off the 15 degree steps is refused.
        Span<CRouteNode> nodes = stackalloc CRouteNode[2];
        nodes[0] = new CRouteNode { Pos = new Vector3(-0.45f, 0.34f, -0.75f), Host = 0 };
        nodes[1] = new CRouteNode { Pos = new Vector3(-0.2f, 0.34f, 0.0f), Host = 0 }; // (about 71 degrees off the first leg's line)
        var w2 = new ConstructWorld();
        var angled = "";
        if (RoutesBuild(w2, BenchB("fuel"), 0, 0, "Bench B") is { } c2)
        {
            ConstructRoutes.Remove(w2, c2, 0);
            angled = ConstructRoutes.Check(w2, c2, RouteType.Pipe, 0, PartNamed(c2, "Fuel Tank"), 255, 255, 0, default, nodes, PartNamed(c2, "Engine"), 255) ?? "";
        }
        // Cut the branch: the second engine starves, the first is still fed; a cut fuel pipe leaks.
        var fuel0 = c.Fuel;
        ConstructRoutes.Damage(w, c, 1, 1, 1000);
        DriveSteps(w, RoutesGround, 120);
        var leaked = fuel0 - c.Fuel;
        var cut = ConstructRoutes.PipeFeeds(c, e1) && !ConstructRoutes.PipeFeeds(c, e2);
        // Wrong pipes: fuel through a water pipe weeps and catches fire after a while; through an oil pipe the engine sputters; a steam pipe is fine.
        var wv = new ConstructWorld();
        string weep = "", sputterLine = "";
        var lit = false;
        var sputters = false;
        var fine = false;
        if (RoutesBuild(wv, BenchB("water"), 0, 0, "Bench B (water pipe)") is { } wc)
        {
            weep = ConstructRoutes.IssueText(wc.Routes[0].Issue, Fluid.Fuel, PipeStyle.Water) ?? "";
            var f0 = wc.Fuel;
            DriveSteps(wv, RoutesGround, 60); // (a second of weeping: not lit yet)
            var early = wc.Burning == 0 && wc.Routes[0].Timer > 0.9f;
            wc.Routes[0].Timer = ConstructRoutes.IgniteSeconds - 0.5f; // (the rest of its time skipped: a short scenario)
            DriveSteps(wv, RoutesGround, 40);
            lit = early && wc.Fuel < f0 && wc.Burning > 0;
        }
        var wo = new ConstructWorld();
        if (DriveBlueprint("Buggy") is { } buggy)
        {
            var oily = Swap(buggy);
            oily.Links.Clear();
            oily.Links.Add(new ConstructRoutes.LinkSpec { Type = RouteType.Pipe, Style = (byte)PipeStyle.Oil, From = buggy.Links[0].From, To = buggy.Links[0].To });
            if (DriveSpawn(wo, oily, 0, 0, 0, "the buggy on an oil pipe") is { } car)
            {
                Seat(car);
                DriveSteps(wo, RoutesGround, 30, _ => DriveKeys(car, Buttons.Forward));
                sputters = car.Routes[0].Issue == PipeIssue.Sputters && car.FuelQuality < 1f && car.CanDrive;
                sputterLine = ConstructRoutes.IssueText(car.Routes[0].Issue, Fluid.Fuel, PipeStyle.Oil) ?? "";
            }
            fine = ConstructRoutes.IssueOf(Fluid.Fuel, PipeStyle.Steam) == PipeIssue.None && ConstructRoutes.IssueOf(Fluid.Fuel, PipeStyle.Fuel) == PipeIssue.None
                   && ConstructRoutes.IssueOf(Fluid.Steam, PipeStyle.Water) == PipeIssue.Bursts;
            // The buggy's own fuel pipe: shot through, it stops; mended with the wrench, it drives again.
            var wb = new ConstructWorld();
            if (DriveSpawn(wb, buggy, 0, 0, 0, "the buggy") is { } b)
            {
                var drives = b.CanDrive;
                ConstructRoutes.Damage(wb, b, 0, 0, 1000);
                b.Rebuild();
                var stops = !b.CanDrive && b.DriveProblem.Contains("fuel");
                ConstructRoutes.Repair(wb, b, 0);
                b.Rebuild();
                Line($"routed links buggy: drives on its fuel pipe {drives}, stops with it cut {stops} ({b.DriveProblem}), drives again repaired {b.CanDrive}");
                if (!drives || !stops || !b.CanDrive)
                    Fail("routed links: the buggy's fuel pipe doesn't feed (or cut, or repair) its engine");
            }
        }
        Line($"routed links pipes: fuel reaches both engines {feeds}, split evenly {split}; an off-step bend refused \"{angled}\"; the branch cut: only the first fed {cut}, {leaked:0.0} l leaked in 2 s; "
             + $"a water pipe: \"{weep}\", caught fire {lit}; an oil pipe: \"{sputterLine}\" {sputters}; the ratings {fine}");
        if (!feeds || !split || !cut)
            Fail("routed links: a pipe junction doesn't split the fuel (or a cut doesn't stop it)");
        if (!angled.Contains("15 degree"))
            Fail("routed links: a pipe bend off its 15 degree steps wasn't refused");
        if (leaked < ConstructRoutes.LeakLitres * 1.5f)
            Fail($"routed links: a cut fuel pipe should leak ({leaked:0.00} l in 2 s)");
        if (weep.Length == 0 || !lit || !sputters || !fine)
            Fail("routed links: a wrong pipe's consequences are wrong");
    }

    private static void RoutesShafts()
    {
        // The migrated truck: its shafts are links now; it drives all four wheels, and cutting the front shaft drops the front.
        var w = new ConstructWorld();
        if (DriveBlueprint("4x4 Truck") is not { } bp || DriveSpawn(w, bp, 0, 0, 0, "the truck") is not { } truck)
            return;
        var shafts = 0;
        var legacy = 0;
        for (var k = 0; k < truck.RouteCount; k++)
            if (truck.Routes[k].Type == RouteType.Shaft)
                shafts++;
        for (var i = 0; i < truck.PartCount; i++)
            if (truck.DefOf(i).Legacy)
                legacy++;
        int Driven(Construct c)
        {
            var n = 0;
            for (var k = 0; k < c.WheelCount; k++)
                if (c.Wheels[k].Driven)
                    n++;
            return n;
        }
        truck.Parts[PartNamed(truck, "Transfer Case")].Setting = 1; // (4WD)
        truck.Rebuild();
        var all = Driven(truck);
        var front = -1;
        var bestZ = float.MaxValue;
        for (var k = 0; k < truck.RouteCount; k++)
        {
            if (truck.Routes[k].Type != RouteType.Shaft)
                continue;
            var z = ConstructRoutes.Point(truck, k, truck.Routes[k].Count, out _).Z;
            if (z < bestZ)
            {
                bestZ = z;
                front = k;
            }
        }
        if (front >= 0)
            ConstructRoutes.Damage(w, truck, front, 0, 10000);
        truck.Rebuild();
        var fewer = Driven(truck);
        // A bare shaft junction is refused (it needs a T gearbox), and a shaft can't turn a corner without a gearbox.
        var junction = front >= 0 ? ConstructRoutes.Check(w, truck, RouteType.Shaft, 0, -1, 255, front, 0, ConstructRoutes.Point(truck, front, -1, out _), default, PartNamed(truck, "Wide Differential"), 255) ?? "" : "";
        var w2 = new ConstructWorld();
        const string bench = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Bench D",
              "steps": [ { "part": "Foundation" }, { "part": "Transmission", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Differential", "on": 0, "socket": "top12", "by": "bottom0" } ],
              "links": [ { "type": "shaft", "from": 1, "fromPort": "+z", "to": 2, "toPort": "-z0" } ] }
            """;
        string corner = "", straight = "";
        if (RoutesBuild(w2, bench, 0, 0, "Bench D") is { } d)
        {
            straight = d.RouteCount == 1 && d.Routes[0].Type == RouteType.Shaft ? "ok" : "missing";
            var tr = PartNamed(d, "Transmission");
            var df = PartNamed(d, "Differential");
            var fp = d.Routes[0].FromPort;
            var tp = d.Routes[0].ToPort;
            ConstructRoutes.Remove(w2, d, 0);
            Span<CRouteNode> nodes = stackalloc CRouteNode[1];
            nodes[0] = new CRouteNode { Pos = new Vector3(-0.75f, 0.55f, 0f), Host = 0 };
            corner = ConstructRoutes.Check(w2, d, RouteType.Shaft, 0, tr, fp, 255, 0, default, nodes, df, tp) ?? "";
        }
        Line($"routed links shafts: the truck has {shafts} shaft links and {legacy} old shaft parts; drives {all} wheels in 4WD, {fewer} with its front shaft cut; a bare junction: \"{junction}\"; "
             + $"a straight shaft on a bench {straight}, one turning a corner: \"{corner}\"");
        if (shafts < 2 || legacy > 0 || all != 4 || fewer != 2)
            Fail("routed links: the truck's shaft links don't carry the drive as its shafts did");
        if (!junction.Contains("T gearbox") || straight != "ok" || !corner.Contains("gearbox"))
            Fail("routed links: a shaft's junction or corner wasn't refused (or a straight one was)");
    }

    private static void RoutesChutes()
    {
        const string bench = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Bench E",
              "steps": [ { "part": "Foundation" }, { "part": "Scrap Bin", "on": 0, "socket": "top00", "by": "bottom" }, { "part": "Scrap Bin", "on": 0, "socket": "top03", "by": "bottom" },
                         { "part": "Scrap Bin", "on": 0, "socket": "top33", "by": "bottom" } ],
              "links": [ { "type": "chute", "from": 1, "to": 2 } ] }
            """;
        var w = new ConstructWorld();
        if (RoutesBuild(w, bench, 0, 0, "Bench E") is not { } c)
            return;
        int a = PartNamed(c, "Scrap Bin"), b = PartNamed(c, "Scrap Bin", 1), d = PartNamed(c, "Scrap Bin", 2);
        // A junction half way along the chute to the third bin.
        var mid = (ConstructRoutes.Point(c, 0, -1, out _) + ConstructRoutes.Point(c, 0, 0, out _)) * 0.5f;
        mid = ConstructWorld.Quantise(mid);
        var why = ConstructRoutes.Check(w, c, RouteType.Chute, 0, -1, 255, 0, 0, mid, default, d, 255);
        if (why == null)
            ConstructRoutes.Add(w, c, RouteType.Chute, 0, -1, 255, 0, 0, mid, default, d, 255);
        c.Rebuild();
        w.Deposit(c, a, 200);
        DriveSteps(w, RoutesGround, 4 * World.TickRate + 2);
        var sa = w.StoreOf(c, a)?.Amount ?? -1;
        var sb = w.StoreOf(c, b)?.Amount ?? -1;
        var sd = w.StoreOf(c, d)?.Amount ?? -1;
        Line($"routed links chutes: 200 scrap in the first bin, 4 s of a chute with a junction ({why ?? "ok"}): {sa} left, {sb} and {sd} in the other two");
        if (why != null || sb <= 0 || sd <= 0 || Math.Abs(sb - sd) > ConstructRoutes.ChuteLoad || sa + sb + sd != 200)
            Fail("routed links: a chute junction doesn't share the items evenly");
    }

    private static void RoutesFollow()
    {
        // Driving: the buggy's pipe stays where it sits on its tank.
        var w = new ConstructWorld();
        float drift = -1, travelled = 0;
        if (DriveBlueprint("Buggy") is { } bp && DriveSpawn(w, bp, 0, 0, 0, "the buggy") is { } car)
        {
            Span<CRouteNode> one = stackalloc CRouteNode[1];
            var tank = PartNamed(car, "Fuel Tank");
            // (a node on the tank's top: re-lay the pipe with it)
            ConstructRoutes.Remove(w, car, 0);
            car.PartBounds(tank, out var mn, out var mx);
            one[0] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3((mn.X + mx.X) * 0.5f, mx.Y + 0.04f, mx.Z - 0.05f)), Host = (byte)tank };
            ConstructRoutes.Add(w, car, RouteType.Pipe, 0, tank, 255, 255, 0, default, one, PartNamed(car, "Engine"), 255);
            car.Rebuild();
            Seat(car);
            var p0 = car.WorldCom;
            var gap0 = Vector3.Distance(ConstructRoutes.WorldPoint(car, 0, 0), ConstructRender.PartCentre(car, tank));
            DriveSteps(w, RoutesGround, 120, t => DriveKeys(car, t < 80 ? Buttons.Forward : Buttons.Forward | Buttons.Left));
            drift = MathF.Abs(Vector3.Distance(ConstructRoutes.WorldPoint(car, 0, 0), ConstructRender.PartCentre(car, tank)) - gap0);
            travelled = Vector3.Distance(p0, car.WorldCom);
        }
        // Jointed: a node on the turret's turning beam turns with it (a cable across the joint flexes).
        var wt = new ConstructWorld();
        float jdrift = -1, swung = 0;
        if (ConstructBuild.FindBlueprint("Turret") is { } tb && ConstructBuild.BuildOnGround(wt, tb, Vector3.Zero, 0, out _) is { } tur && tur.JointCount > 0)
        {
            var beam = -1;
            for (var i = 0; i < tur.PartCount && beam < 0; i++)
                if (tur.Parts[i].Seg == 1)
                    beam = i;
            if (beam >= 0)
            {
                tur.PartBounds(beam, out var mn, out var mx);
                tur.RouteNodes[0] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3((mn.X + mx.X) * 0.5f, mx.Y + 0.03f, mx.Z - 0.1f)), Host = (byte)beam, Health = 20 };
                tur.RouteNodeCount = 1;
                tur.Routes[0] = new CRoute { Type = RouteType.Data, From = 0, To = (byte)beam, FromPort = 255, ToPort = 255, Parent = 255, ParentNode = 255, Start = 0, Count = 1, EndHealth = 20 };
                tur.RouteCount = 1;
                var gap0 = Vector3.Distance(ConstructRoutes.WorldPoint(tur, 0, 0), ConstructRender.PartCentre(tur, beam));
                var at0 = ConstructRoutes.WorldPoint(tur, 0, 0);
                tur.Joints[0].Angle = MathF.PI / 2;
                jdrift = MathF.Abs(Vector3.Distance(ConstructRoutes.WorldPoint(tur, 0, 0), ConstructRender.PartCentre(tur, beam)) - gap0);
                swung = Vector3.Distance(at0, ConstructRoutes.WorldPoint(tur, 0, 0));
            }
        }
        Line($"routed links follow: the buggy drove {travelled:0.0} m, its pipe's node moved {drift:0.0000} m off its tank; the turret turned 90 degrees, a node on its beam swung {swung:0.00} m and kept to the beam within {jdrift:0.0000} m");
        if (drift < 0 || drift > 0.002f || travelled < 3 || jdrift < 0 || jdrift > 0.002f || swung < 0.05f)
            Fail("routed links: links don't follow a moving or jointed construct");
    }

    private static void RoutesRoundTrips()
    {
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchA, 0, 0, "Bench A") is not { } c)
            return;
        ConstructRoutes.Damage(w, c, 1, 0, 7); // (part damage on a segment: carried as a byte)
        ConstructRoutes.Damage(w, c, 0, 2, 1000); // (a cut one)
        c.Rebuild();
        // The blueprint round trip: the links as a blueprint's "links", built again on the same steps.
        var json = ConstructRoutes.LinksJson(c);
        var text = BenchA[..BenchA.LastIndexOf("\"links\"", StringComparison.Ordinal)] + "\"links\": " + json + " }";
        var w2 = new ConstructWorld();
        var same = RoutesBuild(w2, text, 0, 0, "Bench A again") is { } again && SameRoutes(c, again, health: false);
        // The network round trip: CBody to a client's world, and a truncated one refused.
        var wr = new PacketWriter(16384);
        ConstructNet.WriteFull(wr, c);
        var client = new ConstructWorld();
        var reader = new PacketReader(wr.Span[1..]);
        ConstructNet.Read(ref reader, client, 0);
        var mirrored = client.Get(c.Id) is { } copy && SameRoutes(c, copy, health: true) && copy.WireCount == c.WireCount;
        var refused = false;
        try
        {
            var cutShort = new PacketReader(wr.Span[1..(wr.Length - 6)]);
            ConstructNet.Read(ref cutShort, new ConstructWorld(), 0);
        }
        catch (MalformedPacketException)
        {
            refused = true;
        }
        // The server's check of the linking tool: the wrong tool, then the link.
        var e = new ConstructEdits();
        const byte me = 3;
        var eye = c.ToWorld(new Vector3(0, 1.6f, -2.5f));
        var bat = PartNamed(c, "Battery");
        var lamp = PartNamed(c, "Lamp", 1);
        e.SetTool(me, ConstructTool.Wrench);
        var wrongTool = e.Route(w, me, eye, c.Id, RouteType.Power, 0, bat, 255, 255, 0, default, default, lamp, 255) ?? "";
        e.SetTool(me, ConstructTool.Linker);
        var routed = e.Route(w, me, eye, c.Id, RouteType.Power, 0, bat, 255, 255, 0, default, default, lamp, 255);
        var deleted = e.RouteEdit(w, me, eye, c.Id, RouteEditOp.Delete, c.RouteCount - 1, 0, (byte)bat, (byte)lamp, default, 0);
        Line($"routed links round trips: blueprint \"links\" {same} ({json.Length} chars); a client's copy {mirrored} ({wr.Length} bytes), a short packet refused {refused}; "
             + $"the linking tool: the wrench refused \"{wrongTool}\", routed {routed ?? "ok"}, deleted {deleted ?? "ok"}");
        if (!same || !mirrored || !refused)
            Fail("routed links: a blueprint or network round trip changes the links");
        if (!wrongTool.Contains("linking tool") || routed != null || deleted != null)
            Fail("routed links: the server's linking tool checks are wrong");
    }

    private static bool SameRoutes(Construct a, Construct b, bool health)
    {
        if (a.RouteCount != b.RouteCount || a.RouteNodeCount != b.RouteNodeCount)
            return false;
        for (var k = 0; k < a.RouteCount; k++)
        {
            ref var x = ref a.Routes[k];
            ref var y = ref b.Routes[k];
            if (x.Type != y.Type || x.Style != y.Style || x.From != y.From || x.To != y.To || x.FromPort != y.FromPort || x.ToPort != y.ToPort || x.Parent != y.Parent
                || x.ParentNode != y.ParentNode || x.Count != y.Count || x.FirstCut != y.FirstCut && health)
                return false;
            for (var i = 0; i < x.Count; i++)
            {
                ref var n = ref a.RouteNodes[x.Start + i];
                ref var m = ref b.RouteNodes[y.Start + i];
                if (Vector3.Distance(n.Pos, m.Pos) > 0.006f || n.Host != m.Host || health && (n.Health <= 0) != (m.Health <= 0))
                    return false;
            }
        }
        return true;
    }

    private static void RoutesMigration()
    {
        // An old-style blueprint: fuel lines between a tank and an engine (both on the foundation) and a wire.
        const string old = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Old Bench",
              "steps": [ { "part": "Foundation" }, { "part": "Fuel Tank", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Engine", "on": 0, "socket": "top03", "by": "-y" },
                         { "part": "Fuel Line", "on": 1, "socket": "+z", "by": "-x" }, { "part": "Fuel Line", "on": 3, "socket": "+x", "by": "-x" },
                         { "part": "Switch", "on": 0, "socket": "top30", "by": "-y" }, { "part": "Lamp", "on": 0, "socket": "top33", "by": "-y" } ],
              "wires": [ [5, 6] ] }
            """;
        var w = new ConstructWorld();
        if (RoutesBuild(w, old, 0, 0, "Old Bench") is not { } c)
            return;
        var lines = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == PartRole.FuelLine)
                lines++;
        int pipes = 0, data = 0;
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (c.Routes[k].Type == RouteType.Pipe)
                pipes++;
            if (c.Routes[k].Type == RouteType.Data && c.Routes[k].Count == 0)
                data++;
        }
        var fed = ConstructRoutes.PipeFeeds(c, PartNamed(c, "Engine"));
        // A tank hung only on its fuel line keeps the old part (taking it off would drop the tank).
        const string hung = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Hung Bench",
              "steps": [ { "part": "Foundation" }, { "part": "Engine", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Fuel Line", "on": 1, "socket": "+y", "by": "-x" },
                         { "part": "Fuel Tank", "on": 2, "socket": "+x", "by": "-y" } ] }
            """;
        var w2 = new ConstructWorld();
        var kept = RoutesBuild(w2, hung, 0, 0, "Hung Bench") is { } h && PartNamed(h, "Fuel Line") >= 0 && h.RouteCount == 0 && h.PartCount == 4;
        // The shipped blueprints: no fuel lines or plain shafts left in any of them.
        var left = new List<string>();
        foreach (var b in ConstructBuild.Blueprints)
            if (ConstructBuild.Build(new ConstructWorld(), b, Vector3.Zero, Quaternion.Identity, out _) is { } built)
                for (var i = 0; i < built.PartCount; i++)
                    if (built.DefOf(i).Legacy)
                        left.Add($"{b.Name}: {built.DefOf(i).Name}");
        var hidden = ConstructParts.All.Count(d => d.Legacy) == 4;
        Line($"routed links migration: 2 fuel lines became {pipes} pipe link (lines left {lines}, the engine fed {fed}); a wire became {data} data link; a tank hung on its line kept it {kept}; "
             + $"old parts left in shipped blueprints: {(left.Count == 0 ? "none" : string.Join(", ", left))}; legacy parts hidden {hidden}");
        if (pipes != 1 || lines != 0 || !fed || data != 1 || !kept || left.Count > 0 || !hidden)
            Fail("routed links: the migration of wires, fuel lines and shafts is wrong");
    }

    private static void RoutesWideIds()
    {
        // 300 part files in a folder: every one loads with its own id (one byte held 250).
        var dir = Path.Combine(Path.GetTempPath(), $"cos-wide-ids-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        try
        {
            for (var i = 0; i < 300; i++)
                File.WriteAllText(Path.Combine(dir, $"P{i:000}.json"),
                    $$"""{ "format": "call-of-slop-construct-part/1", "name": "Wide {{i}}", "role": "frame", "tool": "welder", "mass": 1, "health": 10, "boxes": [ { "size": [0.1, 0.1, 0.1] } ], "sockets": [ { "at": [0, -0.05, 0], "dir": "-y" } ] }""");
            var refused = new List<string>();
            var defs = ConstructParts.LoadFolder(dir, refused);
            var inv = new SandboxInventory { Infinite = false };
            var stored = inv.Add(299) && inv.Count(299) == 1;
            var ok = defs.Length == 300 && refused.Count == 0 && defs[^1].Id == 299 && ConstructParts.None == ushort.MaxValue && stored;
            Line($"routed links part ids: {defs.Length} part files loaded ({refused.Count} refused), the last id {(defs.Length > 0 ? defs[^1].Id : -1)}; an inventory holds part 299 {stored}; none is {ConstructParts.None}");
            if (!ok)
                Fail("routed links: part ids don't go past one byte");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void RoutesBudget()
    {
        var w = new ConstructWorld();
        var a = RoutesBuild(w, BenchA, 0, 0, "Bench A");
        var b = RoutesBuild(w, BenchB("water"), 6, 0, "Bench B");
        Construct? car = DriveBlueprint("Buggy") is { } bp ? DriveSpawn(w, bp, 0, 12, 0, "the buggy") : null;
        if (a == null || b == null || car == null)
            return;
        Seat(car);
        var sw = PartNamed(a, "Switch");
        void Tick(int t)
        {
            a.Parts[sw].State = (byte)((t / 20) & 1); // (the switch flicked on and off)
            DriveKeys(car, t % 120 < 80 ? Buttons.Forward : Buttons.Forward | Buttons.Left);
        }
        Action<int> tick = Tick; // (made once: the delegate itself allocates)
        DriveSteps(w, RoutesGround, 60, tick);
        var before = GC.GetAllocatedBytesForCurrentThread();
        DriveSteps(w, RoutesGround, 120, tick);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ray = 0L;
        var hits = 0;
        var r0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
            if (w.Raycast(new Vector3(-0.45f, 2f, 0.1f), -Vector3.UnitY, 5f, out var hit) && hit.RoutePlus1 > 0)
                hits++;
        ray = GC.GetAllocatedBytesForCurrentThread() - r0;
        Line($"routed links budget: 120 ticks of three constructs with links (signals, a wrong pipe, a buggy driving): {bytes} bytes allocated; 200 rays at a cable: {hits} hit it, {ray} bytes");
        if (bytes > 0 || ray > 0)
            Fail($"routed links: {bytes} bytes allocated in 120 ticks ({ray} in the rays)");
        if (hits != 200)
            Fail("routed links: a ray down onto a cable didn't hit it");
    }
}
