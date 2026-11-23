using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// <c>--station-check SITE --build NAME [--seconds 60] [--station-at X,Z]</c>: a site blueprint (a works, a factory) spawned headless on a
/// creative world, its biggest Building Station (or the one nearest X,Z) started on blueprint NAME, and run for a bounded game time. It
/// prints the station panel's bill (what is held, where each kind would be ordered, what nothing linked makes and why), then after the run
/// the job's state and note, each lacking kind's reason, and the station's open orders with their states: what a player would find stuck.
/// It writes nothing and changes no save. Exit code: the kinds nothing makes.
/// </summary>
public static class StationCheck
{
    public static int Cli(string site, string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var build = Arg("--build") ?? "";
        var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 60;
        Vector2? near = null;
        if (Arg("--station-at") is { } at && at.Split(',') is [var xs, var zs] && float.TryParse(xs, out var x) && float.TryParse(zs, out var z))
            near = new Vector2(x, z);
        if (ConstructBuild.FindBlueprint(site) is not { } bp)
        {
            Console.WriteLine($"--station-check: no blueprint named \"{site}\"");
            return 1;
        }
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld { CreativeRecycling = true };
        var q = new SelfTest.DriveWorld();
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(2))
        {
        }
        Construct c;
        using (place.In())
            c = job.Join(Vector3.Zero, 0, out var why) ?? throw new FactoryKitException($"{bp.Name} doesn't build: {why}");
        Console.WriteLine($"{bp.Name}: {c.PartCount} parts");
        // --fill "X,Z,MATERIAL,N;...": bulk put straight into the store nearest each point (a bin a hauler would fill); with --keep, topped
        // up to that every second of the run (a refinery that keeps up).
        var keep = new List<(int Bin, int Material, int N)>();
        // (the store nearest the point that takes the material: a bin for it, or an Assembler's own input mix, 2026-11-22: a bin was any
        // store, so a point at an Assembler filled nothing)
        int FillTarget(float px, float pz, string fm) => Enumerable.Range(0, c.PartCount)
            .Where(i => w.StoreOf(c, i) != null && !c.DefOf(i).IsBuildStation && (c.DefOf(i).IsAssembler || c.DefOf(i).IsBin && ConstructStorage.BinTakes(c.DefOf(i), RecyclingTable.Current.IdOf(fm))))
            .OrderBy(i => Vector2.Distance(new Vector2(c.Parts[i].Pos.X, c.Parts[i].Pos.Z), new Vector2(px, pz))).FirstOrDefault(-1);
        int Fill(int bin, int n, byte m)
        {
            if (bin < 0 || m == 0)
                return 0;
            if (!c.DefOf(bin).IsAssembler)
                return w.Deposit(c, bin, n, m);
            var st = w.StoreOf(c, bin)!;
            var add = Math.Max(0, n - st.MixOf(m));
            w.AddInput(st, m, add);
            return add;
        }
        foreach (var f in (Arg("--fill") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (f.Split(',') is [var fx, var fz, var fm, var fn] && float.TryParse(fx, out var px) && float.TryParse(fz, out var pz) && int.TryParse(fn, out var pn))
            {
                var bin = FillTarget(px, pz, fm);
                var put = Fill(bin, pn, RecyclingTable.Current.IdOf(fm));
                if (bin >= 0 && args.Contains("--keep"))
                    keep.Add((bin, RecyclingTable.Current.IdOf(fm), pn));
                Console.WriteLine($"  fill: {put} {fm} into the {(bin < 0 ? "nothing" : c.DefOf(bin).Name)} at {px}, {pz}");
            }
        // The station: the one nearest the point given, else the one with the biggest bay.
        var t = -1;
        var best = float.MinValue;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsBuildStation)
            {
                var size = ConstructStation.BaySize(c, i);
                var p = c.Parts[i].Pos;
                Console.WriteLine($"  Building Station part {i} at {p.X:0.0}, {p.Z:0.0}: bay {size.X:0.#} x {size.Y:0.#} x {size.Z:0.#} m");
                var score = near is { } n ? -Vector2.Distance(n, new Vector2(p.X, p.Z)) : size.X * size.Z;
                if (score > best)
                    (best, t) = (score, i);
            }
        if (t < 0)
        {
            Console.WriteLine("  no Building Station in it");
            return 1;
        }
        if (string.IsNullOrEmpty(build))
            return 0;
        if (ConstructBuild.FindBlueprint(build) is not { } target)
        {
            Console.WriteLine($"--station-check: no blueprint named \"{build}\"");
            return 1;
        }
        {
            Span<ushort> pads = stackalloc ushort[8];
            var np = ConstructStation.OwnPads(c, t, pads);
            Console.WriteLine($"  station part {t}: {ConstructStation.Arms(c, t)} fitting arms, {np} pick pads under its crane{string.Concat(pads[..np].ToArray().Select(p => $" (part {p} at {c.Parts[p].Pos.X:0}, {c.Parts[p].Pos.Z:0})"))}");
        }
        Console.WriteLine($"  station: part {t}; the bill of {target.Name} as its panel shows it before Start:");
        var preview = new StationPreview();
        preview.Begin(target);
        while (!preview.Step(50))
        {
        }
        if (preview.Why != "")
            Console.WriteLine($"  {target.Name} doesn't build: {preview.Why}");
        using (place.In())
            preview.Fill(w, c, t);
        foreach (var (text, tone) in preview.Lines)
            Console.WriteLine(tone == 0 ? $"  {text}" : $"    {(tone == 3 ? "!!" : tone == 2 ? "->" : "ok")} {text}");
        Console.WriteLine($"  {preview.Parts} parts of {preview.Kinds} kinds, {preview.Unmade} kind(s) nothing makes; fits the bay: {ConstructStation.FitRefusal(preview.Size, ConstructStation.BaySize(c, t), ConstructStation.BayCorner(c, t)) ?? "yes"} ({preview.Size.X:0.0} x {preview.Size.Y:0.0} x {preview.Size.Z:0.0} m)");
        // Every kind, against every order-taking machine on the site whether linked to the station or not: what could make it once
        // linked, and for what nothing on the site makes, each kind of machine's reason.
        var makers = Enumerable.Range(0, c.PartCount).Where(i => WorkOrders.TakesOrders(c.DefOf(i))).ToArray();
        Console.WriteLine($"  the site's {makers.Length} order-taking machines against the bill's kinds:");
        var could = new Dictionary<string, List<string>>();
        var never = new Dictionary<string, List<string>>();
        foreach (var name in target.Steps.Select(x => x.Part).Distinct())
        {
            if (ConstructParts.Find(name) is not { } d)
                continue;
            var n = target.Steps.Count(x => x.Part == name);
            var what = ConstructStation.OrderFor(d, n);
            string? maker = null;
            var reasons = new SortedSet<string>();
            foreach (var m in makers)
                using (place.In())
                    if (WorkOrders.Refusal(w, c, m, what) is { } no)
                        reasons.Add($"{c.DefOf(m).Name}: {no}");
                    else
                    {
                        maker = c.DefOf(m).Name;
                        break;
                    }
            var key = maker ?? string.Join(" | ", reasons.Where(r => !r.StartsWith("Extruder") && !r.StartsWith("Laser Cutter")));
            var into = maker != null ? could : never;
            if (!into.TryGetValue(key, out var list))
                into[key] = list = [];
            list.Add($"{n} x {name}");
        }
        foreach (var (m, list) in could.OrderByDescending(x => x.Value.Count))
            Console.WriteLine($"    -> the {m} would make {list.Count} kind(s): {string.Join(", ", list)}");
        foreach (var (why, list) in never.OrderByDescending(x => x.Value.Count))
            Console.WriteLine($"    !! nothing on the site makes {list.Count} kind(s) ({why}): {string.Join(", ", list)}");
        string? refused;
        using (place.In())
            refused = ConstructStation.Start(w, c, t, target.Name, 1);
        if (refused != null)
        {
            Console.WriteLine($"  Start refused: {refused}");
            return Math.Max(1, preview.Unmade);
        }
        var lastNote = "";
        var lifterLooks = new Dictionary<int, string>();
        using (place.In())
            for (var k = 0; k < seconds * World.TickRate; k++)
            {
                MoverPhysics.Clock++; // (the server tick: Game.ServerTickStep sets it in a game; lifters' moves run on it, and never ended here before 2026-11-22)
                w.Step(q, World.TickDt);
                if (args.Contains("--trace") && k % (5 * World.TickRate) == 0)
                    foreach (var m in makers.Where(m => c.DefOf(m).IsAssembler))
                        Console.WriteLine($"  {k / World.TickRate,4} s: the {c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0}: queue {c.Parts[m].Orders?.Count ?? -1}, "
                            + (w.StoreOf(c, m) is { } ast2 ? ConstructAssembling.Look(c.DefOf(m), ast2, ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[m], c.DefOf(m)), ConstructAssembling.FitOf(c, m), false, new SandboxInventory(), null, c.Parts[m].Orders is { Bill: false }, null) : ""));
                if (args.Contains("--trace") && k % World.TickRate == 0)
                    for (var i = 0; i < c.PartCount; i++)
                        if (ConstructLifters.DefOf(c, i) != null && ConstructLifters.Look(w, c, i) is var look && (!lifterLooks.TryGetValue(i, out var was) || was != look))
                        {
                            lifterLooks[i] = look; // (a lifter's look as it changes: what it carries where, and why it waits)
                            if (k > 0)
                                Console.WriteLine($"  {k / World.TickRate,4} s: lifter {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: {look}");
                        }
                if (k % World.TickRate == 0)
                    foreach (var (bin, mat, n) in keep)
                        if (c.DefOf(bin).IsAssembler)
                            Fill(bin, n, (byte)mat);
                        else if (w.StoreOf(c, bin) is { } st && (st.Amount == 0 || st.Material == mat) && st.Amount < n)
                            w.Deposit(c, bin, n - st.Amount, (byte)mat);
                if (c.Parts[t].Station is { } sj && sj.Note != lastNote)
                {
                    lastNote = sj.Note;
                    Console.WriteLine($"  {k / (float)World.TickRate,5:0.0} s: {ConstructStation.StateText(sj)}: {sj.Note}");
                }
            }
        string Holds(int i) => w.StoreOf(c, i) is not { } st ? "no store" : $"{st.Amount} {RecyclingTable.Current.NameOf(st.Material)}"
            + (st.InAmount > 0 ? $", in {st.InAmount} {RecyclingTable.Current.NameOf(st.InMaterial)}" : "")
            + (st.MixN > 0 ? ", mix " + string.Join(" ", Enumerable.Range(0, st.MixN).Select(k => $"{st.MixAmount![k]} {RecyclingTable.Current.NameOf(st.MixMat![k])}")) : "");
        foreach (var m in makers.Where(m => c.Parts[m].Orders is { Count: > 0 }))
        {
            var ol = c.Parts[m].Orders!;
            Console.WriteLine($"  the {c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0} queue: {string.Join("; ", Enumerable.Range(0, ol.Count).Select(k => $"{ol.Items[k].Count} x {(ol.Items[k].Part != ConstructParts.None ? ConstructParts.Def(ol.Items[k].Part).Name : ol.Items[k].Name ?? "a piece")} (made {ol.Items[k].Made})"))}");
        }
        foreach (var m in makers.Where(m => c.DefOf(m).IsAssembler))
            Console.WriteLine($"  the {c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0} holds {Holds(m)}; looks: "
                + (w.StoreOf(c, m) is { } ast ? ConstructAssembling.Look(c.DefOf(m), ast, ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[m], c.DefOf(m)), ConstructAssembling.FitOf(c, m), false, new SandboxInventory(), null, c.Parts[m].Orders is { Bill: false }, AssemblerToolBay.HeldIn(w, c, m)) : ""));
        foreach (var f in (Arg("--fill") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (f.Split(',') is [var fx, var fz, var fm, _] && float.TryParse(fx, out var px) && float.TryParse(fz, out var pz))
            {
                var bin = FillTarget(px, pz, fm);
                Console.WriteLine($"  at the end the {(bin < 0 ? "nothing" : c.DefOf(bin).Name)} at {px}, {pz} holds {(bin < 0 ? "" : Holds(bin))}");
            }
        // Every bin's stock at the end: where the bulk an Assembler waits for went (2026-11-22: the yard Assembler starved of steel).
        foreach (var i in Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).IsBin && w.StoreOf(c, i) is { Amount: > 0 }))
            Console.WriteLine($"  the {c.DefOf(i).Name} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0} holds {Holds(i)}");
        foreach (var b in w.Bodies)
            if (b != c)
                Console.WriteLine($"  a body of {b.PartCount} part(s) ({(b.PartCount > 0 ? b.DefOf(0).Name : "")}) at {b.WorldCom.X:0.0}, {b.WorldCom.Y:0.0}, {b.WorldCom.Z:0.0}");
        if (args.Contains("--lifters"))
            for (var i = 0; i < c.PartCount; i++)
                if (ConstructLifters.DefOf(c, i) != null)
                    Console.WriteLine($"  lifter {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: {ConstructLifters.Look(w, c, i)}");
        Console.WriteLine($"  power: on {c.PowerOn}, mains off {c.MainsOff}, draw {c.PowerUse:0} W, plant {c.PlantOut:0.0} kW");
        if (c.Parts[t].Station is not { } j)
        {
            Console.WriteLine("  no job after the run");
            return Math.Max(1, preview.Unmade);
        }
        Console.WriteLine($"  after {seconds} s: {ConstructStation.StateText(j)}, {j.Fitted}/{j.Steps} fitted: {j.Note}");
        var whys = new Dictionary<string, List<string>>();
        for (var i = 0; i < j.KindN; i++)
            if (j.KindWhy[i] is { } why)
            {
                var name = ConstructParts.Get(j.KindDef[i])?.Name ?? $"#{j.KindDef[i]}";
                if (!whys.TryGetValue(why, out var list))
                    whys[why] = list = [];
                list.Add($"{j.KindNeed[i] - j.KindFit[i]} x {name}");
            }
        Console.WriteLine($"  kinds whose order was refused ({whys.Sum(x => x.Value.Count)}), by reason:");
        foreach (var (why, list) in whys.OrderByDescending(x => x.Value.Count))
            Console.WriteLine($"    {why}: {string.Join(", ", list)}");
        if (c.Parts[t].Craft is { } cq)
        {
            Console.WriteLine($"  open orders ({cq.Count}):");
            for (var k = 0; k < cq.Count; k++)
                Console.WriteLine($"    {cq.Items[k].What.Count} x {ConstructParts.Get(ConstructCrafting.KindOf(ref cq.Items[k]))?.Name ?? "?"}: {ConstructCrafting.StateText(c, cq.Items[k])}");
        }
        return preview.Unmade;
    }
}
