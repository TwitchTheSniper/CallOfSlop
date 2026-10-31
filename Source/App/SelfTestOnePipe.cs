using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "One pipe" (board item dev-sandbox-one-pipe-op01, decision 28; docs/sandbox/CONSTRUCTS.md, "Routed links"): there is one
    /// pipe for every fluid. Blueprints that still name a pipe style (fuel, water, oil, steam, anything) load as the one pipe
    /// that feeds both engines and neither weeps, burns nor sputters; a wire stream with an old style nibble reads as the same
    /// pipe; a fuel tank to an engine pipe and a water link (see "World links") both use <see cref="RouteType.Pipe"/>; the old
    /// fuel and water hose names load as the fluid hose; the linking tool's ring has a plain Pipe with no arc; and no source
    /// file mentions the old pipe style type.
    /// </summary>
    private static void CheckOnePipe()
    {
        // 1) Old blueprints with styled pipes are plain pipes that still carry their fluid, with nothing going wrong.
        var verdicts = new List<string>();
        foreach (var style in new[] { "fuel", "water", "oil", "steam", "something else" })
        {
            var w = new ConstructWorld();
            if (RoutesBuild(w, BenchB(style), 0, 0, "Bench B (" + style + ")") is not { } c)
                continue;
            int e1 = PartNamed(c, "Engine"), e2 = PartNamed(c, "Engine", 1);
            var feeds = ConstructRoutes.PipeFeeds(c, e1) && ConstructRoutes.PipeFeeds(c, e2);
            var name = ConstructRoutes.Name(c.Routes[0].Type);
            var fuel0 = c.Fuel;
            DriveSteps(w, RoutesGround, 1500); // (25 s: the old wrong-pipe trouble took 20 s to light, 30 to burst)
            var calm = c.Burning == 0 && c.RouteCount == 2 && MathF.Abs(c.Fuel - fuel0) < 0.01f && ConstructRoutes.PipeFeeds(c, e1);
            if (!feeds || !calm || name != "pipe")
                verdicts.Add($"{style}: feeds {feeds}, calm {calm}, called '{name}'");
        }
        // A re-saved blueprint says nothing about a style.
        var wj = new ConstructWorld();
        var json = RoutesBuild(wj, BenchB("steam"), 0, 0, "Bench B (steam)") is { } sc ? ConstructRoutes.LinksJson(sc) : "";
        if (json.Contains("style", StringComparison.Ordinal))
            verdicts.Add("a saved blueprint still writes a style");

        // 2) A routed-links wire stream from before the bump (the high nibble of the type byte was the style) reads as the same pipe.
        string? wire = null;
        {
            var w1 = new ConstructWorld();
            var w2 = new ConstructWorld();
            if (RoutesBuild(w1, BenchB(), 0, 0, "Bench B") is { } a && RoutesBuild(w2, BenchB(), 0, 0, "Bench B again") is { } b)
            {
                var wr = new PacketWriter(4096);
                ConstructRoutes.Write(wr, a);
                var bytes = wr.Span.ToArray();
                var plain = bytes[1] == (byte)RouteType.Pipe; // (the type byte alone: no style nibble any more)
                bytes[1] |= 3 << 4; // (an old steam pipe)
                var reader = new PacketReader(bytes);
                ConstructRoutes.Read(ref reader, a, a.PartCount);
                a.Rebuild();
                wire = plain && SameRoutes(b, a, health: true) && ConstructRoutes.PipeFeeds(a, PartNamed(a, "Engine")) ? null : $"plain byte {plain}";
            }
        }
        if (wire != null)
            verdicts.Add("the wire: " + wire);

        // 3) The fuel and water hoses are the one fluid hose: the old names still find it, and an old blueprint using them builds.
        var reel = ConstructParts.Find("Fluid Hose Reel");
        var inlet = ConstructParts.Find("Fluid Hose Inlet");
        var aliasOk = reel != null && inlet != null && reel.HoseCarry == HoseCarry.Fluid && inlet.HoseCarry == HoseCarry.Fluid
                      && ConstructParts.Find("Fuel Hose Reel") == reel && ConstructParts.Find("Water Hose Reel") == reel
                      && ConstructParts.Find("fuel hose inlet") == inlet && ConstructParts.Find("Water Hose Inlet") == inlet
                      && ConstructParts.All.Count(d => d.Role == PartRole.Hose && d.HoseCarry == HoseCarry.Fluid) == 2;
        var wh = new ConstructWorld();
        var oldHose = $$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "Old Hose", "steps": [ { "part": "Fuel Tank" }, {{RouteStep("Water Hose Inlet", 0, "+x", "-z")}} ] }""";
        var hoseBuilt = RoutesBuild(wh, oldHose, 0, 0, "Old Hose") is { } hc && hc.DefOf(1) == inlet;
        if (!aliasOk || !hoseBuilt)
            verdicts.Add($"hoses: aliases {aliasOk}, an old blueprint builds with the fluid inlet {hoseBuilt}");

        // 4) The linking tool's ring has no link types at all now (board item at01): no option has a third-tier arc.
        var wheel = new SandboxLinkWheel();
        if (wheel.Options.Any(o => o.Options != null || o.Label is "Pipe" or "Data cable" or "Power cable" or "Drive shaft" or "Chute"))
            verdicts.Add($"the ring still has a link type or an arc: {string.Join(", ", wheel.Options.Select(o => o.Label))}");

        // 5) The old pipe style type is gone from the source (this file spells it in two pieces).
        var gone = SourceTreeMentions("Pipe" + "Style", out var where);
        if (gone != 0)
            verdicts.Add($"{gone} source line(s) still mention the old pipe style type (first: {where})");

        Line($"one pipe: styled blueprints (5 styles) load as plain pipes that feed both engines and stay calm for 25 s; the wire reads an old style nibble; the old hose names find the fluid hose {aliasOk}; the ring has no pipe type; {verdicts.Count} problems");
        if (verdicts.Count > 0)
            Fail($"one pipe: {string.Join("; ", verdicts.Take(4))}");
    }

    /// <summary>How many lines under the repo's Source folder contain <paramref name="token"/> (0 when the source isn't next to the build, as in a packaged copy).</summary>
    private static int SourceTreeMentions(string token, out string first)
    {
        first = "";
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "Source");
            if (!File.Exists(Path.Combine(src, "Constructs", "ConstructRoutes.cs")))
                continue;
            var n = 0;
            foreach (var f in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        if (n++ == 0)
                            first = $"{Path.GetRelativePath(src, f)}:{i + 1}";
                    }
            }
            return n;
        }
        return 0;
    }
}
