using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static string NpBench(string steps, string links) => $$"""
        { "format": "call-of-slop-construct-blueprint/1", "name": "Bench NP", "steps": [ { "part": "Foundation" }, {{steps}} ], "links": [ {{links}} ] }
        """;

    private static string NpNames(Construct c, int part, RouteType t, bool input)
    {
        Span<byte> ports = stackalloc byte[8];
        var n = ConstructRoutes.Ports(c, part, t, ports, input);
        var names = new string[n];
        for (var i = 0; i < n; i++)
            names[i] = ConstructRoutes.PortName(c.DefOf(part), t, ports[i], input);
        return string.Join("|", names);
    }

    /// <summary>
    /// "Named link ports" (board item dev-link-named-ports-np01): part files declare their link ports by function (ConstructPorts.cs).
    /// A shaft link offers an engine only "Drive out", a gearbox "Input shaft" and "Output shaft", a differential an "Input
    /// shaft" at the middle of its face and "Output left" / "Output right" at the side faces; a frame block offers none (red
    /// with "has no shaft port"); a fuel pipe from a tank to an engine has one candidate (no picker); a pipe whose fluid
    /// doesn't suit the inlet warns; exhaust and coolant ports are listed "(no effect yet)"; an old blueprint with directional
    /// shaft ports (a socket's name) loads onto the nearest named port, or the link is dropped with a log line; every
    /// part with a linking role has the ports it needs.
    /// </summary>
    private static void CheckNamedPorts()
    {
        void Want(bool ok, string what)
        {
            if (!ok)
                Fail($"named ports: {what}");
        }
        // Every part: the ports its role needs, on its surface (the loader refuses the rest).
        var problems = new List<string>();
        var ported = 0;
        foreach (var d in ConstructParts.All)
        {
            problems.AddRange(ConstructParts.PortProblems(d).Select(p => $"{d.Name}: {p}"));
            ported += d.NamedPorts.Length > 0 ? 1 : 0;
        }
        Want(problems.Count == 0, $"parts without the ports their role needs: {string.Join("; ", problems.Take(6))}");

        var w = new ConstructWorld();
        const string shaftBench = """
            { "part": "Transmission", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Differential", "on": 0, "socket": "top12", "by": "bottom0" },
            { "part": "Engine", "on": 0, "socket": "top30", "by": "-y" }, { "part": "Frame Block", "on": 0, "socket": "top33", "by": "-y" }
            """;
        if (RoutesBuild(w, NpBench(shaftBench, ""), 0, 0, "Bench NP shafts") is not { } c)
            return;
        int tr = PartNamed(c, "Transmission"), df = PartNamed(c, "Differential"), en = PartNamed(c, "Engine"), fb = PartNamed(c, "Frame Block");
        var engineOut = NpNames(c, en, RouteType.Shaft, false);
        var engineIn = NpNames(c, en, RouteType.Shaft, true);
        var boxOut = NpNames(c, tr, RouteType.Shaft, false);
        var boxIn = NpNames(c, tr, RouteType.Shaft, true);
        var diffIn = NpNames(c, df, RouteType.Shaft, true);
        var diffOut = NpNames(c, df, RouteType.Shaft, false);
        var frameAny = NpNames(c, fb, RouteType.Shaft, true) + NpNames(c, fb, RouteType.Shaft, false);
        var toFrame = ConstructRoutes.Check(w, c, RouteType.Shaft, en, ConstructRoutes.PortNamed(c.DefOf(en), RouteType.Shaft, "Drive out")!.Byte, 255, 0, default, default, fb, 0) ?? "";
        var fromFrame = ConstructRoutes.Check(w, c, RouteType.Shaft, fb, 0, 255, 0, default, default, tr, 0) ?? "";
        Line($"named ports shafts: engine out \"{engineOut}\" in \"{engineIn}\"; gearbox in \"{boxIn}\" out \"{boxOut}\"; differential in \"{diffIn}\" out \"{diffOut}\"; a frame block offers \"{frameAny}\", \"{toFrame}\" / \"{fromFrame}\"");
        Want(engineOut == "Drive out" && engineIn == "", $"a shaft link to an engine offers only Drive out (out \"{engineOut}\", in \"{engineIn}\")");
        Want(boxIn == "Input shaft" && boxOut == "Output shaft", $"a gearbox offers Input shaft / Output shaft by name (\"{boxIn}\" / \"{boxOut}\")");
        Want(diffIn == "Input shaft" && diffOut.Split('|').OrderBy(x => x).SequenceEqual(["Output left", "Output right"]), $"a differential offers Input shaft and Output left / right (\"{diffIn}\" / \"{diffOut}\")");
        Want(frameAny == "" && toFrame.Contains("has no shaft port") && fromFrame.Contains("has no shaft port"), $"a frame block offers no shaft port, red with the reason (\"{toFrame}\" / \"{fromFrame}\")");

        // Port positions: a differential's Input shaft is the middle of a face, its outputs the middle of the side faces; each faces out.
        foreach (var (name, input) in new[] { ("Input shaft", true), ("Output left", false), ("Output right", false) })
        {
            var np = ConstructRoutes.PortNamed(c.DefOf(df), RouteType.Shaft, name)!;
            var p = ConstructRoutes.PortPoint(c, df, np.Byte, RouteType.Shaft);
            var dir = ConstructRoutes.PortDir(c, df, np.Byte, RouteType.Shaft);
            c.PartBounds(df, out var mn, out var mx);
            var half = (mx - mn) * 0.5f;
            var rel = p - (mn + mx) * 0.5f;
            int onFace = 0, mid = 0;
            for (var a = 0; a < 3; a++)
            {
                if (MathF.Abs(MathF.Abs(rel[a]) - half[a]) < 0.01f)
                    onFace++;
                if (MathF.Abs(rel[a]) < 0.01f)
                    mid++;
            }
            Want(onFace == 1 && mid == 2 && Vector3.Dot(dir, rel) > 0 && MathF.Abs(dir.Length() - 1f) < 1e-3f, $"the differential's \"{name}\" isn't the middle of a face facing out (point {rel}, dir {dir})");
        }

        // Pipes: tank -> engine has one fuel candidate; Coolant in is listed but does nothing yet; exhaust into a fuel inlet warns.
        const string pipeBench = """
            { "part": "Fuel Tank", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Engine", "on": 0, "socket": "top30", "by": "-y" }, { "part": "Engine", "on": 0, "socket": "top33", "by": "-y" }
            """;
        var w2 = new ConstructWorld();
        if (RoutesBuild(w2, NpBench(pipeBench, """{ "type": "pipe", "from": 1, "to": 2 }"""), 0, 0, "Bench NP pipe") is not { } p1)
            return;
        int tank = PartNamed(p1, "Fuel Tank"), e1 = PartNamed(p1, "Engine"), e2 = PartNamed(p1, "Engine", 1);
        Span<byte> pb = stackalloc byte[8];
        var all = ConstructRoutes.Ports(p1, e1, RouteType.Pipe, pb, input: true);
        var forFuel = ConstructRoutes.EndPorts(p1, e1, RouteType.Pipe, ConstructRoutes.CarriedFluid(p1, tank, 255), pb);
        var onlyName = forFuel == 1 ? ConstructRoutes.PortName(p1.DefOf(e1), RouteType.Pipe, pb[0], true) : "";
        var tankOut = NpNames(p1, tank, RouteType.Pipe, false);
        var fed = ConstructRoutes.PipeFeeds(p1, e1);
        var coolant = ConstructRoutes.PortNamed(p1.DefOf(e1), RouteType.Pipe, "Coolant in")!;
        var note = ConstructRoutes.PortNote(p1.DefOf(e1), RouteType.Pipe, coolant.Byte);
        var wrongPort = ConstructRoutes.Check(w2, p1, RouteType.Pipe, tank, 255, 255, 0, default, default, e2, coolant.Byte, straight: true);
        Line($"named ports pipes: the tank offers \"{tankOut}\"; an engine takes {all} pipe inlets, {forFuel} for fuel (\"{onlyName}\"); the fuel pipe feeds it {fed}; coolant in: \"{note}\"");
        Want(tankOut.StartsWith("Fuel out") && all >= 2 && forFuel == 1 && onlyName == "Fuel in", "a fuel pipe from a tank to an engine has exactly one candidate, Fuel in");
        Want(fed && note.Contains("no effect yet"), "the pipe doesn't feed the engine, or Coolant in isn't marked as having no effect");
        _ = wrongPort;

        const string exhaust = """
            { "part": "Engine", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Engine", "on": 0, "socket": "top30", "by": "-y" }
            """;
        var w3 = new ConstructWorld();
        if (RoutesBuild(w3, NpBench(exhaust, """{ "type": "pipe", "from": 1, "fromPort": "Exhaust out", "to": 2, "toPort": "Fuel in" }"""), 0, 0, "Bench NP exhaust") is not { } x1)
            return;
        var warn = ConstructRoutes.FluidWarning(x1, 0);
        var endNote = ConstructRoutes.EndNote(x1, 0) ?? "";
        var xf = ConstructRoutes.PipeFeeds(x1, PartNamed(x1, "Engine", 1));
        Line($"named ports fluids: exhaust into a fuel inlet: \"{warn}\", look line \"{endNote}\", feeds {xf}");
        Want(warn == "exhaust into a fuel inlet" && endNote.Contains("WARNING") && endNote.Contains("no effect yet") && !xf, "an exhaust pipe into a fuel inlet doesn't warn (or it feeds the engine)");

        // Old saves: directional shaft ports (socket names) map to the nearest named port; with none, the link is dropped and logged.
        var w4 = new ConstructWorld();
        if (RoutesBuild(w4, NpBench("""{ "part": "Transmission", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Differential", "on": 0, "socket": "top12", "by": "bottom0" }""",
                """{ "type": "shaft", "from": 1, "fromPort": "+z", "to": 2, "toPort": "-z0" }"""), 0, 0, "Bench NP old") is not { } old)
            return;
        var mapped = old.RouteCount == 1 && old.Routes[0].Type == RouteType.Shaft
                     && ConstructRoutes.PortName(old.DefOf(old.Routes[0].From), RouteType.Shaft, old.Routes[0].FromPort, false) == "Output shaft"
                     && ConstructRoutes.PortName(old.DefOf(old.Routes[0].To), RouteType.Shaft, old.Routes[0].ToPort, true) == "Input shaft";
        var w5 = new ConstructWorld();
        var before = w5.Events.Count;
        var dropped = RoutesBuild(w5, NpBench("""{ "part": "Transmission", "on": 0, "socket": "top00", "by": "-y" }, { "part": "Battery", "on": 0, "socket": "top12", "by": "-y" }""",
                """{ "type": "shaft", "from": 1, "fromPort": "+z", "to": 2, "toPort": "+x" }"""), 0, 0, "Bench NP dropped");
        var logged = w5.Events.Skip(before).Any(e => e.Contains("dropped"));
        Line($"named ports old saves: a directional pair mapped to the named ports {mapped}; a link to a part with no shaft port dropped {dropped is { RouteCount: 0 }} (logged {logged})");
        Want(mapped, "an old directional shaft link didn't map to the nearest named ports");
        Want(dropped is { RouteCount: 0 } && logged, "an old shaft link to a part with no shaft port wasn't dropped with a log line");

        // The blueprint round trip writes names and reads them back.
        var json = ConstructRoutes.LinksJson(old);
        Want(json.Contains("\"fromPort\":\"Output shaft\"") && json.Contains("\"toPort\":\"Input shaft\""), $"a blueprint's links name the ports by function ({json})");
        Line($"named ports: {ConstructParts.All.Count} parts, {ported} with named ports; blueprint links {json.Length} chars");
    }
}
