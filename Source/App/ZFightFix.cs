using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>TEMPORARY: writes the nudges that fix every coplanar pair (to be removed before the branch is finished).</summary>
public static class ZFightFix
{
    private static string V(Vector3 v) => $"[{v.X:R},{v.Y:R},{v.Z:R}]";

    public static int Run(string outFile, string kind)
    {
        var sb = new StringBuilder("[\n");
        int models = 0, boxes = 0;
        void Emit(string k, string file, string pack, string model, IReadOnlyList<ZFight.ZBox> before, bool ground)
        {
            var hi = Vector3.Zero;
            foreach (var b in before)
                hi = Vector3.Max(hi, Vector3.Abs(b.C) + b.H);
            var maxDim = MathF.Max(hi.X, MathF.Max(hi.Y, hi.Z)) * 2;
            var delta = k == "part" ? 0.001f : Math.Clamp(0.003f + 0.0015f * maxDim, 0.003f, 0.025f);
            if (ZFight.Find(before, ground).Count == 0)
                return;
            var after = ZFight.Resolve(before, delta, ground, out var passes);
            var left = ZFight.Find(after, ground).Count;
            models++;
            for (var i = 0; i < before.Count; i++)
            {
                if (before[i].C == after[i].C && before[i].H == after[i].H)
                    continue;
                boxes++;
                sb.Append($"{{\"kind\":\"{k}\",\"file\":{System.Text.Json.JsonSerializer.Serialize(file)},\"pack\":{System.Text.Json.JsonSerializer.Serialize(pack)},\"model\":{System.Text.Json.JsonSerializer.Serialize(model)},\"name\":\"{before[i].Name}\",\"left\":{left},");
                sb.Append($"\"oc\":{V(before[i].C)},\"oh\":{V(before[i].H)},\"nc\":{V(after[i].C)},\"nh\":{V(after[i].H)}}},\n");
            }
        }
        if (kind.StartsWith("dbgb"))
        {
            foreach (var d in Buildings.All)
            {
                var plan = BuildingGen.Generate(d, new BuildingVariant { Furnish = false }, 0, Vector3.Zero, 0, Props.Global);
                var bx = ZFight.Boxes(plan);
                var hs = ZFight.Find(bx, true, true);
                if (hs.Count > 0)
                    Console.WriteLine($"== {d.Name}: {hs.Count}");
                foreach (var h in hs)
                    Console.WriteLine("   " + ZFight.Describe(bx, h) + $" colours {plan.Shell[Math.Min(h.A, plan.Shell.Count - 1)].Color} {plan.Shell[Math.Min(h.B, plan.Shell.Count - 1)].Color}");
            }
            return 0;
        }
        if (kind.StartsWith("dbg:"))
        {
            var parts = kind[4..].Split("@"); var d = Props.Find(parts[0])!; var dl = parts.Length > 1 ? float.Parse(parts[1]) : 0.003f;
            var bx = ZFight.Boxes(d);
            foreach (var h in ZFight.Find(bx, true))
                Console.WriteLine(ZFight.Describe(bx, h));
            var after = ZFight.Resolve(bx, dl, true, out var ps);
            Console.WriteLine($"passes {ps}, left {ZFight.Find(after, true).Count}");
            for (var i = 0; i < bx.Count; i++)
                if (bx[i].C != after[i].C || bx[i].H != after[i].H)
                    Console.WriteLine($"changed {i}: C {bx[i].C} -> {after[i].C}; H {bx[i].H} -> {after[i].H}");
            foreach (var i in ZFight.Find(bx, true).SelectMany(h => new[] { h.A, h.B }))
                Console.WriteLine($"{i}: C {bx[i].C} X {bx[i].X} Y {bx[i].Y} Z {bx[i].Z} H {bx[i].H}");
            return 0;
        }
        if (kind == "prop")
        {
            foreach (var d in Props.All)
                Emit("prop", d.SourceFile ?? "", "", d.Name, ZFight.Boxes(d), true);
            var maps = Path.Combine(AppFolders.Data, "Maps");
            foreach (var dir in Directory.GetDirectories(maps).OrderBy(x => x))
                if (Directory.Exists(Path.Combine(dir, "Props")))
                    foreach (var d in Props.ForPack(dir).Local)
                        Emit("prop", d.SourceFile ?? "", Path.GetFileName(dir), d.Name, ZFight.Boxes(d), true);
        }
        else if (kind == "construct")
        {
            foreach (var d in ConstructParts.All)
                Emit("construct", d.File, "", d.Name, ZFight.Boxes(d), false);
        }
        else if (kind == "part")
        {
            foreach (var p in Parts.All)
                Emit("part", p.SourceFile ?? "", "", p.Name, ZFight.Boxes(p), false);
        }
        sb.Append("{}]\n");
        File.WriteAllText(outFile, sb.ToString());
        Console.WriteLine($"zfight-fix {kind}: {models} model(s), {boxes} box change(s) -> {outFile}");
        return 0;
    }
}
