using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The Assembler's retirement (Sean, 2026-11-28; plan doc "Retire the Assembler: migration plan"): which maker each Assembler recipe
/// moves to. Small items go to press chains (a die gives a blank its working, as a tube pressed on a Bell die is a thruster bell); a
/// placed part of one solid box bakes from one extrusion on an oven, with no station; any other placed part becomes a kit of extrusions
/// welded at a Building Station and baked, its components fitted as kit pieces. Big parts are made as baked sub-assemblies on the
/// smallest oven that holds them, so the work spreads over every oven size; only a part that can't be split (a railgun barrel) needs a
/// bigger bed. Nothing here changes a table or a machine: it is the rule the next phases follow, and <c>--maker-report</c> shows it.
/// </summary>
public static class MakerRoutes
{
    public enum Route : byte { Press, OneBake, Kit, KitNow, PressedNow }

    public static readonly string[] RouteNames = ["press chain", "one-piece bake", "station kit + bake", "kit already", "pressed already"];

    /// <summary>
    /// Oven sizes, smallest first: (name, the bed's two sides and the tunnel's height, m), from Zero Day Works II's ovens (10 x 3 and
    /// 10 x 10 cells of <see cref="MachineBlocks.DefaultCell"/> m, tunnels 4 and 6 m) and the largest bed a Core takes (MachineBlocks'
    /// default 16 a side, 128 cells, so 16 x 8; the tunnel's top setting 12 m). Past the last: a bigger bed (Sean: only for parts that
    /// can't be split).
    /// </summary>
    public static readonly (string Name, float Along, float Across, float High)[] Ovens =
    [
        ("small oven 20 x 6 m", 20, 6, 4),
        ("big oven 20 x 20 m", 20, 20, 6),
        ("largest bed 32 x 16 m", 32, 16, 12),
    ];

    /// <summary>The route for an Assembler recipe's part.</summary>
    public static Route Of(ConstructPartDef d)
    {
        if (BakeKits.Has(d))
            return Route.KitNow;
        if (PressTable.Current.PresetKeyOf(d) != null)
            return Route.PressedNow;
        if (!d.PhysicsOnly)
            return Route.Press; // (an item: a hand carries it, so a press line's pieces size it)
        return Pieces(d) <= 1 ? Route.OneBake : Route.Kit;
    }

    /// <summary>How many extrusions its box kit would hold: its solid, drawn boxes (BakeKits.Make's rule).</summary>
    public static int Pieces(ConstructPartDef d)
    {
        var n = 0;
        for (var i = 0; i < d.SolidBoxes && i < d.Boxes.Length; i++)
            if (!d.Boxes[i].Hidden)
                n++;
        return n;
    }

    /// <summary>The smallest oven whose bed and tunnel hold the part laid any way up (its bounds' sides sorted), or Ovens.Length: none.</summary>
    public static int OvenFor(ConstructPartDef d) => OvenFor(d.BoundsMax - d.BoundsMin);

    public static int OvenFor(Vector3 size)
    {
        Span<float> s = [size.X, size.Y, size.Z];
        s.Sort(); // (s[0] the height it lies at, s[2] along the bed)
        for (var k = 0; k < Ovens.Length; k++)
            if (s[2] <= Ovens[k].Along && s[1] <= Ovens[k].Across && s[0] <= Ovens[k].High)
                return k;
        return Ovens.Length;
    }

    public static string OvenName(int k) => k < Ovens.Length ? Ovens[k].Name : "bigger bed (split it, or a bigger oven)";

    /// <summary>The longest side of each kit piece, m (one a solid drawn box): what an Extruder must reach.</summary>
    private static void PieceSizes(ConstructPartDef d, List<float> into)
    {
        for (var i = 0; i < d.SolidBoxes && i < d.Boxes.Length; i++)
            if (!d.Boxes[i].Hidden)
                into.Add(MathF.Max(d.Boxes[i].Half.X, MathF.Max(d.Boxes[i].Half.Y, d.Boxes[i].Half.Z)) * 2);
    }

    /// <summary>
    /// <c>--maker-report [--build NAME] [--out FILE]</c>: every Assembler recipe's route, with the oven size each placed part needs, and
    /// with <c>--build</c> a blueprint's Assembler-made parts by route, oven size and piece length. Prints a summary and writes the whole
    /// list (default maker-report.txt next to the DLL). Changes nothing.
    /// </summary>
    public static int Cli(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var path = Arg("--out") ?? Path.Combine(AppContext.BaseDirectory, "maker-report.txt");
        var asm = AssemblingTable.Current;
        if (asm.Problem.Length > 0 || PressTable.Current.Problem.Length > 0)
        {
            Console.WriteLine($"maker-report: a table was refused: {asm.Problem} {PressTable.Current.Problem}");
            return 1;
        }
        var sb = new StringBuilder();
        var summary = new StringBuilder();
        var byRoute = new int[RouteNames.Length];
        var byOven = new int[Ovens.Length + 1];
        var tools = 0;
        var lines = new List<string>[RouteNames.Length];
        for (var k = 0; k < lines.Length; k++)
            lines[k] = new List<string>();
        foreach (var r in asm.Recipes)
        {
            var d = r.Output;
            var route = Of(d);
            byRoute[(int)route]++;
            var oven = route is Route.OneBake or Route.Kit or Route.KitNow ? OvenFor(d) : -1;
            if (oven >= 0)
                byOven[oven]++;
            if (r.Tool.Length > 0)
                tools++;
            var size = d.BoundsMax - d.BoundsMin;
            var parts = r.Parts.Length == 0 ? "" : "; fits " + string.Join(" + ", r.Parts.Select(p => $"{p.Count} {p.Part.Name}"));
            lines[(int)route].Add(FormattableString.Invariant(
                $"  {r.Name} ({size.X:0.##} x {size.Y:0.##} x {size.Z:0.##} m, {d.Mass:0.#} kg, {Pieces(d)} pieces{(oven >= 0 ? ", " + OvenName(oven) : "")}{(r.Tool.Length > 0 ? ", needs the " + r.Tool + " tool" : "")}{parts})"));
        }
        summary.AppendLine($"maker-report: {asm.Recipes.Length} Assembler recipes");
        for (var k = 0; k < RouteNames.Length; k++)
            summary.AppendLine($"  {RouteNames[k]}: {byRoute[k]}");
        summary.AppendLine("  placed parts by the smallest oven that holds them whole (a big one is made as sub-assemblies where it splits):");
        for (var k = 0; k <= Ovens.Length; k++)
            summary.AppendLine($"    {OvenName(k)}: {byOven[k]}");
        summary.AppendLine($"  rows needing a tool block (Thruster Jig, Sewing Table): {tools}");
        if (Arg("--build") is { } build)
        {
            if (ConstructBuild.FindBlueprint(build) is not { } bp)
            {
                Console.WriteLine($"maker-report: no blueprint named \"{build}\"");
                return 1;
            }
            Build(bp, summary);
        }
        sb.Append(summary);
        for (var k = 0; k < RouteNames.Length; k++)
        {
            sb.AppendLine().AppendLine($"{RouteNames[k]} ({lines[k].Count}):");
            foreach (var l in lines[k])
                sb.AppendLine(l);
        }
        File.WriteAllText(path, sb.ToString());
        Console.Write(summary);
        Console.WriteLine($"maker-report: the whole list: {path}");
        return 0;
    }

    /// <summary>A blueprint's Assembler-made catalogue parts by route and oven, and how long its kit pieces are.</summary>
    private static void Build(ConstructBuild.Blueprint bp, StringBuilder sb)
    {
        var bill = ConstructBill.FromBlueprint(bp);
        var kinds = new int[RouteNames.Length];
        var count = new int[RouteNames.Length];
        var oven = new int[Ovens.Length + 1];
        var sizes = new List<float>();
        var boards = 0;
        var gears = 0;
        var bearings = 0;
        var pressNames = new List<string>();
        foreach (var (_, d, n) in bill.Parts)
        {
            if (d == null || AssemblingTable.Current.Of(d.Id) is not { } r)
                continue;
            var route = Of(d);
            kinds[(int)route]++;
            count[(int)route] += n;
            if (route == Route.Press)
                pressNames.Add($"{d.Name} x{n}");
            if (route is Route.OneBake or Route.Kit or Route.KitNow)
            {
                oven[OvenFor(d)] += n;
                for (var i = 0; i < n; i++)
                    PieceSizes(d, sizes);
            }
            foreach (var (p, c) in r.Parts)
            {
                if (p.Name == "Circuit Board")
                    boards += c * n;
                else if (p.Name == "Gear Set")
                    gears += c * n;
                else if (p.Name == "Bearing")
                    bearings += c * n;
            }
        }
        sb.AppendLine($"{bp.Name}: its Assembler-made catalogue parts");
        for (var k = 0; k < RouteNames.Length; k++)
            if (kinds[k] > 0)
                sb.AppendLine($"  {RouteNames[k]}: {kinds[k]} kinds, {count[k]} parts");
        sb.AppendLine("  baked parts by the smallest oven that holds them whole:");
        for (var k = 0; k <= Ovens.Length; k++)
            if (oven[k] > 0)
                sb.AppendLine($"    {OvenName(k)}: {oven[k]}");
        if (sizes.Count > 0)
        {
            sizes.Sort();
            int Upto(float m) => sizes.Count(s => s <= m + 0.001f);
            sb.AppendLine(FormattableString.Invariant(
                $"  kit pieces: {sizes.Count}; longest side up to 0.5 m {Upto(0.5f)}, 1 m {Upto(1)}, 2 m {Upto(2)}, 4 m {Upto(4)}, 8 m {Upto(8)}, 16 m {Upto(16)}, longest {sizes[^1]:0.##} m"));
        }
        if (pressNames.Count > 0)
            sb.AppendLine($"  press chain kinds: {string.Join(", ", pressNames)}");
        sb.AppendLine($"  components its recipes fit (direct): {gears} Gear Sets, {boards} Circuit Boards, {bearings} Bearings");
    }
}
