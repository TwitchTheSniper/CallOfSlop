using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    // The routing bench (board items lg01 and ls01): a fuel tank and an engine at the foundation's -x edge (both flush with
    // it, so a link from below its side runs over the slab's corner into them), a second engine (an input of pipes) in
    // the way of a run, and a frame block for an outside turn.
    private static readonly string BenchRouting = $$"""
        { "format": "call-of-slop-construct-blueprint/1", "name": "Bench R",
          "steps": [ { "part": "Foundation" }, {{RouteStep("Fuel Tank", 0, "top00", "-y")}}, {{RouteStep("Engine", 0, "top03", "-y")}},
                     {{RouteStep("Engine", 0, "top30", "-y")}}, {{RouteStep("Frame Block", 0, "top33", "-y")}} ] }
        """;

    /// <summary>
    /// "Link clipping grace" (board item dev-sandbox-link-clip-grace-lg01; ConstructRoutes.PathRule): a pipe from a tank
    /// into an engine, both on the slab's edge, whose end segments run from below the slab's side over its corner, passes
    /// within the clipping grace where the old rule refused it; a node 50% embedded in a part passes and one 70% embedded
    /// fails; an outside turn over a box's edge passes (the old rule refused it); the adjust search finds the nearest
    /// valid offset for a node clicked into a wall and the wheel's steps move it 1 cm (5 cm); the client's open-path
    /// preview agrees with the server's check in every case.
    /// </summary>
    private static void CheckLinkClipGrace()
    {
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchRouting, 0, 0, "Bench R") is not { } c)
            return;
        void Want(bool ok, string what)
        {
            if (!ok)
                Fail($"link clipping grace: {what}");
        }
        var tank = PartNamed(c, "Fuel Tank");
        var engine = PartNamed(c, "Engine");
        var block = PartNamed(c, "Frame Block");
        c.PartBounds(0, out var fmn, out var fmx);
        c.PartBounds(engine, out var emn, out var emx);
        var tc = ConstructRoutes.PortPoint(c, tank, 255);
        var ec = ConstructRoutes.PortPoint(c, engine, 255);
        var r = ConstructRoutes.Radius(RouteType.Pipe);
        // 1) The grace: from below the slab's -x side, 45 degrees up over its corner into the tank and the engine.
        var d = tc.X - (fmn.X - r);
        Span<CRouteNode> nodes = stackalloc CRouteNode[4];
        nodes[0] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(fmn.X - r, tc.Y - d, tc.Z)), Host = 0 };
        nodes[1] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(fmn.X - r, ec.Y - d, ec.Z)), Host = 0 };
        var below = nodes[1].Pos.Y < fmx.Y;
        var oldEnd = ConstructRoutes.Blocked(c, nodes[1].Pos, ec, r * 0.5f - 0.012f, -1, engine); // (the old rule: the box grown by half the radius less 1.2 cm)
        var grace = ConstructRoutes.Check(w, c, RouteType.Pipe, tank, 255, 255, 0, default, nodes[..2], engine, 255);
        var gracePreview = ConstructRoutes.OpenPath(c, RouteType.Pipe, tank, 255, 255, 0, default, nodes[..1], nodes[1].Pos, 0);
        // Past the grace the normal rule applies: on from the second node straight through the slab to its far side.
        var farWhy = ConstructRoutes.OpenPath(c, RouteType.Pipe, tank, 255, 255, 0, default, nodes[..2], ConstructWorld.Quantise(new Vector3(fmx.X + r, nodes[1].Pos.Y, ec.Z)), 0);
        Line($"link clipping grace: grace {ConstructRoutes.Grace(r):0.00} m for a pipe; tank and engine on the slab's edge, end segments from below its side ({below}) over its corner: "
             + $"old rule {(oldEnd is { } oe ? $"refused (the {c.DefOf(oe).Name})" : "passed")}, now \"{grace ?? "ok"}\" (preview \"{gracePreview ?? "ok"}\"); past the grace \"{farWhy ?? "ok"}\"");
        Want(below && oldEnd != null, "the rail case doesn't reproduce the old refusal (the bench changed?)");
        Want(grace == null && gracePreview == null, $"a pipe into a device on a rail isn't let through by the clipping grace ({grace ?? gracePreview})");
        Want(farWhy != null && farWhy.Contains("pass through"), $"a segment through the slab past the grace isn't refused ({farWhy ?? "ok"})");

        // 2) Embedding: a pipe's nodes along the slab's top, 50% and 70% of its thickness sunk in.
        var rs = ConstructRoutes.Radius(RouteType.Pipe);
        string? Embedded(float frac, bool preview)
        {
            var y = fmx.Y + rs - frac * 2f * rs; // (the centreline: the pipe's underside frac x thickness below the surface)
            Span<CRouteNode> n = stackalloc CRouteNode[4];
            n[0] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(0.15f, tc.Y, tc.Z)), Host = 0 };
            n[1] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(0.15f, y, tc.Z)), Host = 0 };
            n[2] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(0.15f, y, ec.Z)), Host = 0 };
            n[3] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(0.15f, ec.Y, ec.Z)), Host = 0 };
            return preview
                ? ConstructRoutes.OpenPath(c, RouteType.Pipe, tank, 255, 255, 0, default, n[..1], n[1].Pos, 0)
                : ConstructRoutes.Check(w, c, RouteType.Pipe, tank, 255, 255, 0, default, n, engine, 255);
        }
        var half = Embedded(0.5f, false);
        var deep = Embedded(0.7f, false);
        var halfP = Embedded(0.5f, true);
        var deepP = Embedded(0.7f, true);
        Line($"link clipping grace: embedding ({ConstructRoutes.EmbedFraction * 100:0}% of the thickness): a pipe 50% sunk \"{half ?? "ok"}\" (preview \"{halfP ?? "ok"}\"), 70% \"{deep ?? "ok"}\" (preview \"{deepP ?? "ok"}\")");
        Want(half == null && halfP == null, $"a node 50% embedded is refused ({half ?? halfP})");
        Want(deep != null && deep.Contains("sink") && deepP != null && deepP.Contains("sink") , $"a node 70% embedded isn't refused for sinking ({deep ?? "ok"}, preview {deepP ?? "ok"})");

        // 3) An outside turn over the frame block's top edge: along its top, 45 degrees down past the edge, down its side.
        c.PartBounds(block, out var bmn, out var bmx);
        var rw = ConstructRoutes.Radius(RouteType.Pipe);
        var z = (bmn.Z + bmx.Z) * 0.5f;
        Span<Vector3> pts = stackalloc Vector3[4];
        Span<int> hosts = stackalloc int[4];
        pts[0] = ConstructWorld.Quantise(new Vector3(bmx.X - 0.05f, bmx.Y + rw, z));
        pts[1] = ConstructWorld.Quantise(new Vector3(bmn.X + rw, bmx.Y + rw, z));
        pts[2] = ConstructWorld.Quantise(new Vector3(bmn.X - rw, bmx.Y - rw, z));
        pts[3] = ConstructWorld.Quantise(new Vector3(bmn.X - rw, fmx.Y + 0.1f, z));
        for (var i = 0; i < 4; i++)
            hosts[i] = block;
        var turn = ConstructRoutes.PathRule(c, RouteType.Pipe, pts, hosts, true, 255, false, 255);
        var oldTurn = ConstructRoutes.Blocked(c, pts[1], pts[2], rw * 0.5f - 0.012f, -1, -1);
        Line($"link clipping grace: an outside turn over the frame block's edge: old rule {(oldTurn != null ? "refused" : "passed")}, now \"{turn ?? "ok"}\"");
        Want(oldTurn != null, "the outside turn doesn't reproduce the old refusal");
        Want(turn == null, $"an outside turn round a box corner is refused ({turn})");

        // 4) Hold to adjust: a node clicked into the frame block's side (sunk 70%) from a node beside it: the search finds
        // the nearest valid offset out along the normal; the wheel's steps move it 1 cm (Ctrl 5 cm); the server agrees.
        var power = RouteType.Power;
        var rp = ConstructRoutes.Radius(power);
        Span<Vector3> open = stackalloc Vector3[3];
        Span<int> oh = stackalloc int[3];
        var normal = -Vector3.UnitX;
        var basePos = ConstructWorld.Quantise(new Vector3(bmn.X - rp + 1.4f * rp, (bmn.Y + bmx.Y) * 0.5f, z - 0.2f)); // (70% in: the centreline 0.4 x radius inside)
        var rule = new GraceRule { C = c, Type = power, Start = ConstructWorld.Quantise(new Vector3(bmn.X - 0.4f, (bmn.Y + bmx.Y) * 0.5f, z - 0.2f)), Host = block };
        var before = rule.Why(basePos);
        var found = ConstructRoutes.AdjustSearch(power, basePos, normal, ref rule);
        var at = found is { } f0 ? ConstructRoutes.AdjustPos(basePos, normal, f0) : basePos;
        var after = rule.Why(at);
        var step1 = ConstructRoutes.AdjustPos(basePos, normal, (found ?? 0) + ConstructRoutes.AdjustStepCm) - at;
        var step5 = ConstructRoutes.AdjustPos(basePos, normal, (found ?? 0) + ConstructRoutes.AdjustBigStepCm) - at;
        var back = rule.Why(ConstructRoutes.AdjustPos(basePos, normal, (found ?? 0) - 1));
        var clampIn = ConstructRoutes.AdjustClamp(power, -1000) == ConstructRoutes.AdjustMinCm(power) && ConstructRoutes.AdjustClamp(power, 1000) == ConstructRoutes.AdjustMaxCm;
        // The server's rule on the adjusted spot (PathRule, the rule Check runs).
        open[0] = rule.Start;
        open[1] = at;
        oh[0] = block;
        oh[1] = block;
        var server = ConstructRoutes.PathRule(c, power, open[..2], oh[..2], true, 255, false, 255);
        Line($"link clipping grace: adjust: clicked into the block (\"{before ?? "ok"}\"), the search found {(found is { } ff ? $"{ff:+0;-0;0} cm" : "nothing")} (\"{after ?? "ok"}\"), "
             + $"one notch {step1.Length() * 100:0.0} cm, Ctrl {step5.Length() * 100:0.0} cm, one back in \"{back ?? "ok"}\", clamped {clampIn}; server \"{server ?? "ok"}\"");
        Want(before != null && found is > 0 && after == null && back != null, "the adjust search doesn't find the nearest valid offset out of a wall");
        Want(MathF.Abs(step1.Length() - 0.01f) < 0.002f && MathF.Abs(step5.Length() - 0.05f) < 0.002f && Vector3.Dot(step1, normal) > 0, "the wheel's adjust steps don't move the node 1 cm (5 cm) along its normal");
        Want(clampIn, "the adjust range isn't clamped to the embedding limit and the maximum");
        Want(server == null, $"the server's rule refuses the adjusted node ({server})");
    }

    /// <summary>A node rule for the self-test: an open path from a junction-like start on a part to the node (PathRule).</summary>
    private struct GraceRule : ConstructRoutes.INodeRule
    {
        public Construct C;
        public RouteType Type;
        public Vector3 Start;
        public int Host;

        public readonly string? Why(Vector3 pos)
        {
            Span<Vector3> p = stackalloc Vector3[2];
            Span<int> h = stackalloc int[2];
            p[0] = Start;
            p[1] = pos;
            h[0] = Host;
            h[1] = Host;
            return ConstructRoutes.PathRule(C, Type, p, h, true, 255, false, 255);
        }
    }

    /// <summary>
    /// "Link free routing" (board item dev-sandbox-link-shift-free-ls01; ConstructRoutes.ClickEnds): with free routing on,
    /// a click on an input device (an engine, for a pipe) places a node on its surface and the link stays open; with it
    /// off the same click ends the link on that input; a link routed along that engine (a node on it) to another engine is
    /// accepted by the server; the Free routing key's default is Left Shift and shares it with Sprint without a clash.
    /// </summary>
    private static void CheckLinkFreeRouting()
    {
        var w = new ConstructWorld();
        if (RoutesBuild(w, BenchRouting, 0, 0, "Bench R") is not { } c)
            return;
        void Want(bool ok, string what)
        {
            if (!ok)
                Fail($"link free routing: {what}");
        }
        var tank = PartNamed(c, "Fuel Tank");
        var e1 = PartNamed(c, "Engine");
        var e2 = PartNamed(c, "Engine", 1);
        var r = ConstructRoutes.Radius(RouteType.Pipe);
        c.PartBounds(tank, out _, out var tmx);
        c.PartBounds(e2, out _, out var e2mx);
        var p0 = ConstructRoutes.PortPoint(c, tank, 255, RouteType.Pipe); // (the tank's "Fuel out" and the engine's "Fuel in": a point on a face each, and the link leaves and enters it straight)
        var p1 = ConstructRoutes.PortPoint(c, e1, 255, RouteType.Pipe);
        var e2c = ConstructRoutes.PortPoint(c, e2, 255);
        // Out of the tank's +x port, along the +z faces of the tank and the second engine (a node on that engine), then round to the first engine's -x port.
        var zFace = MathF.Max(tmx.Z, e2mx.Z) + r;
        Span<CRouteNode> nodes = stackalloc CRouteNode[6];
        nodes[0] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(p0.X + 0.3f, p0.Y, p0.Z)), Host = (byte)tank };
        nodes[1] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(p0.X + 0.3f, p0.Y, zFace)), Host = (byte)tank };
        nodes[2] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(e2c.X, p0.Y, zFace)), Host = (byte)e2 };
        nodes[3] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(e2c.X, p0.Y, 0.25f)), Host = 0 };
        nodes[4] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(p1.X - 0.3f, p0.Y, 0.25f)), Host = 0 };
        nodes[5] = new CRouteNode { Pos = ConstructWorld.Quantise(new Vector3(p1.X - 0.3f, p1.Y, p1.Z)), Host = 0 };
        var endsFree = ConstructRoutes.ClickEnds(c, RouteType.Pipe, tank, 255, e2, true);
        var endsSnap = ConstructRoutes.ClickEnds(c, RouteType.Pipe, tank, 255, e2, false);
        var nodeOnEngine = ConstructRoutes.OpenPath(c, RouteType.Pipe, tank, 255, 255, 0, default, nodes[..2], nodes[2].Pos, e2);
        var finishOnEngine = ConstructRoutes.Check(w, c, RouteType.Pipe, tank, 255, 255, 0, default, default, e2, 255); // (the click from the tank straight at it)
        var past = ConstructRoutes.Check(w, c, RouteType.Pipe, tank, 255, 255, 0, default, nodes, e1, 255);
        var added = past == null ? ConstructRoutes.Add(w, c, RouteType.Pipe, tank, 255, 255, 0, default, nodes, e1, 255) : -1;
        c.Rebuild();
        var feeds = added >= 0 && ConstructRoutes.PipeFeeds(c, e1) && !ConstructRoutes.PipeFeeds(c, e2);
        // The key: Left Shift by default, sharing it with Sprint (no clash), live in Sandbox only.
        var d = Keybinds.Defaults;
        var key = d[(int)GameAction.FreeRouting] == Keybinds.Key(Brutal.GlfwApi.GlfwKey.LeftShift) && d[(int)GameAction.Sprint] == d[(int)GameAction.FreeRouting]
                  && !Keybinds.Overlap(GameAction.FreeRouting, GameAction.Sprint) && Keybinds.Context(GameAction.FreeRouting) == Keybinds.BindContext.Sandbox && Duplicates(d).Length == 0;
        Line($"link free routing: a click on the second engine with free routing ends the link {endsFree} (a node there: \"{nodeOnEngine ?? "ok"}\"), without it ends {endsSnap} (\"{finishOnEngine ?? "ok"}\"); "
             + $"a pipe routed along it to the first engine \"{past ?? "ok"}\" (added {added >= 0}, feeds only the first {feeds}); key {Keybinds.Name(d[(int)GameAction.FreeRouting])} beside Sprint {key}");
        Want(!endsFree && nodeOnEngine == null, $"with free routing a click on an input device doesn't place a node ({nodeOnEngine ?? "it ended the link"})");
        Want(endsSnap && finishOnEngine == null, $"without free routing a click on that input doesn't end the link ({finishOnEngine ?? "not an end"})");
        Want(past == null && feeds, $"a link routed past a device with a matching port isn't accepted ({past ?? "not fed"})");
        Want(key, "the Free routing key isn't Left Shift beside Sprint (or it clashes)");
    }
}
