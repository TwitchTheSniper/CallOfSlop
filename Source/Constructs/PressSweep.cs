using System.Text;

namespace CallOfSlop;

/// <summary>
/// `--press-sweep [--out FILE]` (design doc "Assembler on the block system", "What moves to the Press line"; Assembler PR 3): every
/// Assembler recipe sorted by the rule for moving a part to the Press. A part moves when it is one shape from one blank: no component
/// part in its recipe but a kit a die's bay 2 takes, no lamps or screens (a like body drops them), and a die whose presets are parts of
/// its kind and size. The list is for the developer to approve before any row moves; nothing here changes a table. Prints a summary and
/// writes the whole list (default press-sweep.txt next to the DLL).
/// </summary>
public static class PressSweep
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--out", StringComparison.OrdinalIgnoreCase));
        var path = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(AppContext.BaseDirectory, "press-sweep.txt");
        var asm = AssemblingTable.Current;
        var press = PressTable.Current;
        if (asm.Problem.Length > 0 || press.Problem.Length > 0)
        {
            Console.WriteLine($"press-sweep: a table was refused: {asm.Problem} {press.Problem}");
            return 1;
        }
        var cats = PartCategories.Current;
        // Each die: the sections its presets' parts sit in, the sizes they span (longest and shortest sides, m), and the kits its bay 2 takes.
        var dies = press.Dies;
        var sections = new HashSet<string>[dies.Length];
        var lo = new float[dies.Length];
        var hi = new float[dies.Length];
        var kits = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < dies.Length; i++)
        {
            sections[i] = new HashSet<string>(StringComparer.Ordinal);
            lo[i] = float.MaxValue;
            hi[i] = 0;
            foreach (var p in dies[i].Presets)
            {
                if (cats.Place(p.Part, out var cat, out var sec))
                    sections[i].Add(cat + "/" + sec);
                var l = Longest(p.Part);
                lo[i] = MathF.Min(lo[i], l);
                hi[i] = MathF.Max(hi[i], l);
            }
            foreach (var pat in dies[i].Patterns)
                if (pat.Bay2?.Part is { } kit)
                    kits.Add(kit.Name);
        }
        var move = new StringBuilder();
        var dieChange = new StringBuilder();
        var stay = new StringBuilder();
        int nMove = 0, nChange = 0, nStay = 0;
        foreach (var r in asm.Recipes)
        {
            if (r.Tool.Length > 0 || r.Machine != 0)
                continue;
            var d = r.Output;
            var why = Stays(r, kits);
            var size = d.BoundsMax - d.BoundsMin;
            var line = $"{r.Name} ({size.X:0.##} x {size.Y:0.##} x {size.Z:0.##} m; {r.Batch})";
            if (why != null)
            {
                stay.AppendLine($"  {line}: {why}");
                nStay++;
                continue;
            }
            cats.Place(d, out var c, out var s);
            var kind = c + "/" + s;
            var l = Longest(d);
            string? best = null, bySize = null, byKind = null;
            for (var i = 0; i < dies.Length; i++)
            {
                var fits = hi[i] > 0 && l >= lo[i] * 0.5f && l <= hi[i] * 2f;
                var same = sections[i].Contains(kind);
                if (fits && same)
                    best ??= dies[i].Family;
                else if (fits)
                    bySize ??= dies[i].Family;
                else if (same)
                    byKind ??= $"{dies[i].Family} (its presets run {lo[i]:0.##} to {hi[i]:0.##} m: its range would have to reach {l:0.##} m)";
            }
            if (best != null)
            {
                move.AppendLine($"  {line}: the {best} die");
                nMove++;
            }
            else if (byKind != null)
            {
                dieChange.AppendLine($"  {line}: the {byKind}");
                nChange++;
            }
            else
            {
                stay.AppendLine($"  {line}: no die presses its kind ({kind}){(bySize != null ? $"; the {bySize} die is its size" : "")}");
                nStay++;
            }
        }
        var text = new StringBuilder();
        text.AppendLine($"Press sweep: {nMove} rows a die presses as it is, {nChange} that need a die's size range widened, {nStay} that stay on the Assembler.");
        text.AppendLine();
        text.AppendLine("A die presses it (a pattern of its kind and size):");
        text.Append(move);
        text.AppendLine();
        text.AppendLine("A die of its kind, once its size range is widened:");
        text.Append(dieChange);
        text.AppendLine();
        text.AppendLine("Stays on the Assembler:");
        text.Append(stay);
        File.WriteAllText(path, text.ToString());
        Console.WriteLine($"press-sweep: {nMove} press as they are, {nChange} need a die change, {nStay} stay; the list: {path}");
        return 0;
    }

    /// <summary>Why a recipe stays on the Assembler by the rule, or null when it could be pressed.</summary>
    private static string? Stays(AssembleRecipe r, HashSet<string> kits)
    {
        var d = r.Output;
        if (r.Parts.Length > 1)
            return "joins several components";
        if (r.Parts.Length == 1 && !kits.Contains(r.Parts[0].Part.Name))
            return $"joins a component ({r.Parts[0].Part.Name}) no die's bay 2 takes";
        if (d.Panels.Length > 0)
            return "has a screen";
        foreach (var b in d.Boxes)
            if (b.Glow)
                return "has lamps";
        if (d.Role is PartRole.Seat or PartRole.Engine or PartRole.Thruster or PartRole.Wheel)
            return $"is a working part ({d.Role})";
        return null;
    }

    private static float Longest(ConstructPartDef d)
    {
        var s = d.BoundsMax - d.BoundsMin;
        return MathF.Max(s.X, MathF.Max(s.Y, s.Z));
    }
}
