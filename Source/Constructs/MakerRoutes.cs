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
    public enum Route : byte { Press, OneBake, Kit, KitNow, PressedNow, Assembler }

    public static readonly string[] RouteNames = ["press chain", "one-piece bake", "station kit + bake", "listed kit", "pressed already", "still Assembler"];

    /// <summary>
    /// Oven sizes, smallest first: (name, the bed's two sides and the tunnel's height, m), from Zero Day Works II's ovens (10 x 3 and
    /// 10 x 10 cells of <see cref="MachineBlocks.DefaultCell"/> m, tunnels 4 and 6 m) and the largest bed a Core takes (MachineBlocks'
    /// default 16 a side, 128 cells, so 16 x 8; the tunnel's top setting 12 m), then the Bake Oven XL's (2026-11-28: 25 x 17 cells, its
    /// tunnel to 17 m; BakeOvenParts.XlSide), the bed for the parts no smaller oven holds whole (Sean: bigger beds only for parts that
    /// can't be split). Past the last: no oven.
    /// </summary>
    public static readonly (string Name, float Along, float Across, float High)[] Ovens =
    [
        ("small oven 20 x 6 m", 20, 6, 4),
        ("big oven 20 x 20 m", 20, 20, 6),
        ("largest bed 32 x 16 m", 32, 16, 12),
        ("XL oven 50 x 34 m", 50, 34, 17),
    ];

    /// <summary>The route an Assembler recipe's part takes now (phase 3: every placed part by the rule is baked, BakeKits.Has).</summary>
    public static Route Of(ConstructPartDef d)
    {
        if (BakeKits.Has(d))
            return !BakeKits.ByRule(d) || BakeKits.Names.Contains(d.Name, StringComparer.OrdinalIgnoreCase) ? Route.KitNow : BakeKits.OnePiece(d) ? Route.OneBake : Route.Kit;
        if (PressTable.Current.PresetKeyOf(d) != null)
            return Route.PressedNow;
        if (!d.PhysicsOnly)
            return AssemblingTable.Current.Of((ushort)d.Id) is { Tool.Length: > 0 } ? Route.Assembler : Route.Press; // (the 20 kg Lift Cell: its Sewing Table row, phase 4 review) // (an item: a hand carries it, so a press line's pieces size it; not yet sorted die by die)
        return Route.Assembler; // (a part whose boxes make no kit: the log says why; the tool rows bake since phase 4)
    }

    /// <summary>How many extrusions its box kit would hold: its solid, drawn boxes (BakeKits.Make's rule).</summary>
    public static int Pieces(ConstructPartDef d)
    {
        var n = 0;
        for (var i = 0; i < d.SolidBoxes && i < d.Boxes.Length; i++)
            if (!d.Boxes[i].Hidden)
                n++;
        return n > 0 ? n : Math.Min(d.SolidBoxes, d.Boxes.Length); // (none drawn: its hidden solid boxes, as BakeKits.Make takes them)
    }

    /// <summary>The smallest oven whose bed and tunnel hold the part laid any way up (its bounds' sides sorted), or Ovens.Length: none.</summary>
    public static int OvenFor(ConstructPartDef d) => BakeKits.KitSize(d) is { X: > 0 } k ? HearthFor(k) : HearthFor(d.BoundsMax - d.BoundsMin); // (its hearth: what a real oven's tunnel holds, as BedLacks and the split ask)

    public static int OvenFor(Vector3 size)
    {
        Span<float> s = [size.X, size.Y, size.Z];
        s.Sort(); // (s[0] the height it lies at, s[2] along the bed)
        for (var k = 0; k < Ovens.Length; k++)
            if (s[2] <= Ovens[k].Along && s[1] <= Ovens[k].Across && s[0] <= Ovens[k].High)
                return k;
        return Ovens.Length;
    }

    /// <summary>
    /// What an oven's walls and doors take off its bed's two sides (m): Zero Day Works II's 20 x 6 m small bed has a 19.1 x 5.1 m hearth
    /// (ConstructBakeOven.BedLacks). A split (BakeKits.Split) sizes its sub-kits to the hearth, so a sub-kit that fits the size on paper also
    /// lies in a real small oven's tunnel.
    /// </summary>
    public const float HearthMargin = 0.95f;

    /// <summary>The smallest oven whose hearth (its bed less <see cref="HearthMargin"/> each way) and tunnel hold these sides, or Ovens.Length: none.</summary>
    public static int HearthFor(Vector3 size)
    {
        Span<float> s = [size.X, size.Y, size.Z];
        s.Sort();
        for (var k = 0; k < Ovens.Length; k++)
            if (s[2] <= Ovens[k].Along - HearthMargin && s[1] <= Ovens[k].Across - HearthMargin && s[0] <= Ovens[k].High)
                return k;
        return Ovens.Length;
    }

    public static string OvenName(int k) => k < Ovens.Length ? Ovens[k].Name : $"no oven (past the {Ovens[^1].Name}'s hearth: its Assembler row stays its maker)";

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
        var split = 0;
        var wholeShare = new int[Ovens.Length + 1];
        var splitShare = new int[Ovens.Length + 1];
        var lines = new List<string>[RouteNames.Length];
        for (var k = 0; k < lines.Length; k++)
            lines[k] = new List<string>();
        var noOven = new List<string>(); // (placed parts no oven's hearth holds whole: still the Assembler's)
        var cold = System.Diagnostics.Stopwatch.StartNew(); // (OffAssembler's first look at every row, as a picker or a terminal's catalogue makes it)
        var offN = 0;
        foreach (var r in asm.Recipes)
            if (BakeKits.OffAssembler(r.Output))
                offN++;
        var coldMs = cold.Elapsed.TotalMilliseconds;
        var differ = asm.Recipes.Where(r => BakeKits.Off(r.Output, fast: true) != BakeKits.Off(r.Output, fast: false)).Select(r => r.Name).ToList();
        foreach (var r in asm.Recipes)
        {
            var d = r.Output;
            var route = Of(d);
            byRoute[(int)route]++;
            var oven = route is Route.OneBake or Route.Kit or Route.KitNow ? OvenFor(d) : -1;
            if (oven >= 0)
                byOven[oven]++;
            if (oven == Ovens.Length)
            {
                var ks = BakeKits.KitSize(d) is { X: > 0 } kz ? kz : d.BoundsMax - d.BoundsMin;
                noOven.Add(FormattableString.Invariant($"{r.Name} {ks.X:0.#} x {ks.Y:0.#} x {ks.Z:0.#} m"));
            }
            var size = d.BoundsMax - d.BoundsMin;
            var parts = r.Parts.Length == 0 ? "" : "; fits " + string.Join(" + ", r.Parts.Select(p => $"{p.Count} {p.Part.Name}"));
            var kitN = BakeKits.KitOf(d)?.Length ?? Pieces(d);
            lines[(int)route].Add(FormattableString.Invariant(
                $"  {r.Name} ({size.X:0.##} x {size.Y:0.##} x {size.Z:0.##} m, {d.Mass:0.#} kg, {kitN} pieces{(oven >= 0 ? ", " + OvenName(oven) : "")}{(route != Route.Assembler && BakeKits.Bootstrap(d) && !BakeKits.OffAssembler(d) ? ", the Assembler too (a production machine's part: bootstrap)" : "")}{(r.Tool.Length > 0 ? ", needs the " + r.Tool + " tool" : "")}{parts})"));
            if (oven >= 0 && BakeKits.Split(d) is { } tree)
            {
                if (tree.Kids.Count > 0)
                    split++;
                if (tree.Kids.Count > 0 || tree.Oven > 0)
                    Tree(BakeKits.KitOf(d)!, tree, lines[(int)route], "    ");
                Share(tree, BakeKits.KitOf(d)!.Length, 1, wholeShare, splitShare);
            }
        }
        summary.AppendLine($"maker-report: {asm.Recipes.Length} Assembler recipes");
        for (var k = 0; k < RouteNames.Length; k++)
            summary.AppendLine($"  {RouteNames[k]}: {byRoute[k]}");
        summary.AppendLine("  placed parts by the smallest oven that holds them whole (a big one is made as sub-assemblies where it splits):");
        for (var k = 0; k <= Ovens.Length; k++)
            summary.AppendLine($"    {OvenName(k)}: {byOven[k]}");
        summary.AppendLine($"  placed parts no oven holds whole, still the Assembler's: {noOven.Count}{(noOven.Count > 0 ? " (" + string.Join(", ", noOven.Take(8)) + (noOven.Count > 8 ? ", ..." : "") + ")" : "")}");
        var kept = asm.Recipes.Count(r => !BakeKits.OffAssembler(r.Output));
        summary.AppendLine($"  left on the Assembler (phase 6: no press or oven makes them; the rest are bill only): {kept}");
        var boot = asm.Recipes.Count(r => BakeKits.Bootstrap(r.Output) && BakeKits.Has(r.Output) && !BakeKits.OffAssembler(r.Output));
        summary.AppendLine($"    of them production machines' parts that are baked too (bootstrap: an old world's first oven or press): {boot}");
        summary.AppendLine(FormattableString.Invariant($"  OffAssembler's first look at the {asm.Recipes.Length} rows: {coldMs:0.#} ms ({offN} off); its bounds-first way and the kit's differ on {differ.Count}{(differ.Count > 0 ? ": " + string.Join(", ", differ.Take(8)) : "")}"));
        summary.AppendLine($"  baked parts split into sub-kits for smaller ovens: {split}");
        ShareLines(summary, "every baked part, one of each", wholeShare, splitShare);
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
        if (noOven.Count > 0)
        {
            sb.AppendLine().AppendLine($"no oven holds whole ({noOven.Count}):");
            foreach (var l in noOven)
                sb.AppendLine("  " + l);
        }
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
        var asmNames = new List<string>(); // (the kinds still the Assembler's: what a works without one can't make)
        var splitKinds = new List<string>();
        var wholeShare = new int[Ovens.Length + 1];
        var splitShare = new int[Ovens.Length + 1];
        foreach (var (_, d, n) in bill.Parts)
        {
            if (d == null || AssemblingTable.Current.Of(d.Id) is not { } r)
                continue;
            var route = Of(d);
            kinds[(int)route]++;
            count[(int)route] += n;
            if (route == Route.Press)
                pressNames.Add($"{d.Name} x{n}");
            if (route == Route.Assembler)
                asmNames.Add($"{d.Name} x{n}");
            if (route is Route.OneBake or Route.Kit or Route.KitNow)
            {
                oven[OvenFor(d)] += n;
                for (var i = 0; i < n; i++)
                    PieceSizes(d, sizes);
                if (BakeKits.Split(d) is { } tree)
                {
                    Share(tree, BakeKits.KitOf(d)!.Length, n, wholeShare, splitShare);
                    if (tree.Kids.Count > 0)
                        splitKinds.Add($"{d.Name} x{n}");
                }
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
        if (asmNames.Count > 0)
            sb.AppendLine($"  still Assembler kinds: {string.Join(", ", asmNames)}");
        ShareLines(sb, bp.Name, wholeShare, splitShare);
        if (splitKinds.Count > 0)
            sb.AppendLine($"  split into sub-kits: {string.Join(", ", splitKinds)}");
        sb.AppendLine($"  components its recipes fit (direct): {gears} Gear Sets, {boards} Circuit Boards, {bearings} Bearings");
    }

    /// <summary>A kit's tree, one line a node: its pieces (or sub-kits), its size and the oven it bakes on.</summary>
    private static void Tree(BakeKits.Piece[] kit, BakeKits.SplitNode n, List<string> into, string indent)
    {
        var what = n.Kids.Count > 0 ? $"{n.Kids.Count} baked sub-kits" : n.Pieces.Count == 1 ? $"one piece ({kit[n.Pieces[0]].PartName})" : $"{n.Pieces.Count} pieces";
        into.Add(FormattableString.Invariant($"{indent}{what}, {n.Size.X:0.##} x {n.Size.Y:0.##} x {n.Size.Z:0.##} m: {(n.Kids.Count == 0 && n.Pieces.Count == 1 ? "no bake" : OvenName(n.Oven))}"));
        foreach (var k in n.Kids)
            Tree(kit, k, into, indent + "  ");
    }

    /// <summary>
    /// The subparts each oven size bakes for <paramref name="n"/> of a part: whole (its kit on the smallest oven that holds it all) and split
    /// (each sub-kit on the smallest that holds it, the joins of baked sub-kits on theirs).
    /// </summary>
    private static void Share(BakeKits.SplitNode tree, int pieces, int n, int[] whole, int[] split)
    {
        whole[tree.Oven] += pieces * n;
        Walk(tree);
        void Walk(BakeKits.SplitNode node)
        {
            if (node.Kids.Count > 0)
            {
                split[node.Oven] += node.Kids.Count * n;
                foreach (var k in node.Kids)
                    Walk(k);
            }
            else if (node.Pieces.Count > 1 || node == tree)
                split[node.Oven] += node.Pieces.Count * n;
        }
    }

    private static void ShareLines(StringBuilder sb, string what, int[] whole, int[] split)
    {
        var tw = Math.Max(1, whole.Sum());
        var ts = Math.Max(1, split.Sum());
        sb.AppendLine($"  oven work for {what} (subparts baked; whole kits -> split into sub-kits):");
        for (var k = 0; k <= Ovens.Length; k++)
            if (whole[k] > 0 || split[k] > 0)
                sb.AppendLine(FormattableString.Invariant($"    {OvenName(k)}: {whole[k]} ({whole[k] * 100f / tw:0}%) -> {split[k]} ({split[k] * 100f / ts:0}%)"));
    }
}
