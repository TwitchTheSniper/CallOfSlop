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
        if (Arg("--parts-in") is { } box && box.Split(',') is [var bx0, var bz0, var bx1, var bz1]
            && float.TryParse(bx0, out var px0) && float.TryParse(bz0, out var pz0) && float.TryParse(bx1, out var px1) && float.TryParse(bz1, out var pz1))
        {
            // --parts-in X0,Z0,X1,Z1: what stands on the floor there, a line per kind and its footprint (for laying a site out).
            var lo = new Vector2(MathF.Min(px0, px1), MathF.Min(pz0, pz1));
            var hi = new Vector2(MathF.Max(px0, px1), MathF.Max(pz0, pz1));
            var kinds = new SortedDictionary<string, (int N, Vector2 Lo, Vector2 Hi)>();
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                if (d.Name.StartsWith("Works Floor Slab", StringComparison.Ordinal))
                    continue;
                c.PartBounds(i, out var a, out var b);
                if (b.X < lo.X || a.X > hi.X || b.Z < lo.Y || a.Z > hi.Y)
                    continue;
                var key = $"{d.Name} @{MathF.Round((a.X + b.X) * 0.5f / 2) * 2:0}";
                var f = (new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
                kinds[key] = kinds.TryGetValue(key, out var e) ? (e.N + 1, Vector2.Min(e.Lo, f.Item1), Vector2.Max(e.Hi, f.Item2)) : (1, f.Item1, f.Item2);
            }
            foreach (var (k, e) in kinds)
                Console.WriteLine($"  {e.N,4} x {k}: x {e.Lo.X:0.0}..{e.Hi.X:0.0}, z {e.Lo.Y:0.0}..{e.Hi.Y:0.0}");
        }
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
        if (Arg("--line-split") is { } ls && Enum.TryParse<LineSplit>(ls, true, out var lsv)) // (--line-split share|whole|first: every station's Line split, to compare)
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).IsBuildStation && c.DefOf(i).CfgSlot[(int)CfgKey.LineSplit] is var lslot and >= 0)
                    ConstructConfig.Set(ref c.Parts[i], c.DefOf(i), lslot, (int)lsv);
        if (args.Contains("--dies")) //(--dies: every Die Wall stocked with a die of each family, as a creative site start does: PressDieArm.cs)
            Console.WriteLine($"  dies: {ConstructLifters.StockDieWalls(w, c)} set on the Die Walls");
        // Every Line Console started, as a player starting the works would (Zero Day Works II's consoles are laid stopped): a stopped
        // console holds its machines (a Bake Oven never starts its kit).
        var started = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (ProductionLines.IsConsole(c.DefOf(i)) && (c.Parts[i].State & 1) == 0)
            {
                ProductionLines.Run(c, i); // (Start: it wakes the body, as a player's press does)
                started++;
            }
        if (started > 0)
            Console.WriteLine($"  {started} stopped Line Console(s) started");
        // --roll-tiles "X,Z;X,Z...": Roller Tiles welded onto the site (turned as its runs south are, at their height) before the run: a way
        // out of a station's bay or into an oven's bed tried on a site that doesn't lay one (ConstructRollOut.cs).
        if (Arg("--roll-tiles") is { } rollTiles)
        {
            RollerRuns.Sync(w, c);
            var like = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).IsRollerTile && RollerRuns.RunOf(c, i) is var k and >= 0
                && c.RouteNodes[c.Routes[k].Start + 1].Pos.Z < c.RouteNodes[c.Routes[k].Start].Pos.Z - 0.5f).FirstOrDefault(-1);
            if (like < 0)
                Console.WriteLine("  --roll-tiles: no Roller Tile run south on the site to copy");
            else
            {
                foreach (var xz in rollTiles.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    if (xz.Split(',') is [var rx, var rz] && float.TryParse(rx, out var tx) && float.TryParse(rz, out var tz))
                    {
                        var i = w.AddPart(c, c.DefOf(like), new Vector3(tx, c.Parts[like].Pos.Y, tz), c.Parts[like].Rot);
                        c.Rebuild();
                        ConstructBuild.AutoLink(w, c, i);
                        Console.WriteLine($"  a Roller Tile welded at {tx}, {tz} (part {i})");
                    }
                c.Rebuild();
                RollerRuns.Sync(w, c);
                for (var k = 0; k < c.RouteCount; k++)
                    for (var s2 = 0; s2 < c.PartCount; s2++)
                        if (c.DefOf(s2).IsBuildStation && ConstructRollOut.RunOut(c, s2) == k)
                            Console.WriteLine($"  run {k} rolls out of the bay of station {s2}" + (ConstructRollOut.OvenOf(c, k) is var ov and >= 0 ? $" into the bed of the Bake Oven {ov}" : ""));
            }
        }
        // The station: the one nearest the point given, else the one with the biggest bay.
        var t = -1;
        var best = float.MinValue;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsBuildStation)
            {
                var size = ConstructStation.BaySize(c, i);
                var p = c.Parts[i].Pos;
                Console.WriteLine($"  Building Station part {i} at {p.X:0.0}, {p.Z:0.0}: bay {size.X:0.#} x {size.Z:0.#} m, {ConstructStation.HeightText(size)}");
                var score = near is { } n ? -Vector2.Distance(n, new Vector2(p.X, p.Z)) : size.X * size.Z;
                if (score > best)
                    (best, t) = (score, i);
            }
        if (args.Contains("--nets")) // (each station's order network: the machines its Line Console brings in)
            RollerRuns.Sync(w, c);
        if (args.Contains("--nets"))
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).IsBuildStation && ConstructCrafting.NetOf(w, c, i) is var net)
                    Console.WriteLine($"  station {i} at {c.Parts[i].Pos.X:0},{c.Parts[i].Pos.Z:0} rolls to oven {(ConstructRollOut.RunOut(c, i) is var ro and >= 0 ? ConstructRollOut.OvenOf(c, ro) : -2)}; network: " + string.Join(", ", Enumerable.Range(0, net.MachineN).Select(m => net.Machines[m])
                        .Select(m => $"{c.DefOf(m).Name} {m} at {c.Parts[m].Pos.X:0},{c.Parts[m].Pos.Z:0}"))
                        + "; its consoles run: " + string.Join(", ", Enumerable.Range(0, c.RouteCount).Where(k => c.Routes[k].Type == RouteType.Data && c.Routes[k].To == i
                            && c.Routes[k].Source < c.PartCount && ProductionLines.IsConsole(c.DefOf(c.Routes[k].Source))).SelectMany(k =>
                            {
                                var into = new int[32];
                                return into.Take(ProductionLines.Machines(c, c.Routes[k].Source, into)).Select(m => $"{c.DefOf(m).Name} {m}").ToArray();
                            })));
        void Consoles() // (--consoles: each Station Console, the station its watch link names, its Done output and its screen as a peer fills it)
        {
            if (!args.Contains("--consoles"))
                return;
            for (var i = 0; i < c.PartCount; i++)
                if (c.DefOf(i).Logic == CLogic.StationConsole && c.DefOf(i).Panels is [{ Fill: { } fill } panel, ..])
                {
                    var card = new PanelCard();
                    fill(w, c, i, panel, card);
                    var lines = string.Join(" | ", Enumerable.Range(0, PanelCard.MaxLines).Where(l => card.Line(l).Length > 0).Select(l => new string(card.Line(l))));
                    var st = ConstructStationConsole.StationOf(c, c.Parts[i].Watch);
                    Console.WriteLine($"  Station Console {i} at {c.Parts[i].Pos.X:0.#},{c.Parts[i].Pos.Z:0.#}: watches {(st < 0 ? "nothing" : $"station {st} (Station {ConstructStationConsole.Number(c, st)})")}, Done {ConstructStationConsole.Done(c, c.Parts[i].Watch)}, screen: {lines}");
                }
        }
        Consoles();
        if (Arg("--floor-map") is { } mapArg && mapArg.Split(',') is [var mx0, var mz0, var mx1, var mz1]) // (--floor-map X0,Z0,X1,Z1: a 1 m map of what stands on the floor there, # taken, . free; for laying a site out)
        {
            int ax = (int)float.Parse(mx0), az = (int)float.Parse(mz0), bx = (int)float.Parse(mx1), bz = (int)float.Parse(mz1);
            var taken = new bool[bx - ax, bz - az];
            for (var i = 0; i < c.PartCount; i++)
            {
                if (c.DefOf(i).Name.StartsWith("Works Floor Slab", StringComparison.Ordinal))
                    continue;
                c.PartBounds(i, out var a, out var b);
                if (Vector3.Distance(a, -b) < 4f && c.FrameOf(c.Parts[i].Seg) != 0)
                    continue; // (a freeform weld's bounds are in its own frame)
                for (var gx = Math.Max(ax, (int)MathF.Floor(a.X + 0.05f)); gx < Math.Min(bx, (int)MathF.Ceiling(b.X - 0.05f)); gx++)
                    for (var gz = Math.Max(az, (int)MathF.Floor(a.Z + 0.05f)); gz < Math.Min(bz, (int)MathF.Ceiling(b.Z - 0.05f)); gz++)
                        taken[gx - ax, gz - az] = true;
            }
            for (var gz = az; gz < bz; gz++)
                Console.WriteLine($"  map {gz,4} " + string.Concat(Enumerable.Range(ax, bx - ax).Select(gx => taken[gx - ax, gz - az] ? '#' : (gx % 10 == 0 ? '|' : '.'))));
        }
        if (Arg("--near") is { } nearArg && nearArg.Split(',') is [var nx0, var nz0, var nx1, var nz1]) // (--near X0,Z0,X1,Z1: the parts in that rectangle, by kind and row)
        {
            float ax = float.Parse(nx0), az = float.Parse(nz0), bx = float.Parse(nx1), bz = float.Parse(nz1);
            foreach (var g in Enumerable.Range(0, c.PartCount).Where(i => c.Parts[i].Pos.X >= ax && c.Parts[i].Pos.X <= bx && c.Parts[i].Pos.Z >= az && c.Parts[i].Pos.Z <= bz)
                .GroupBy(i => (c.DefOf(i).Name, MathF.Round(c.Parts[i].Pos.Z))).OrderBy(g => g.Key.Item2))
                Console.WriteLine($"  near: z {g.Key.Item2}: {g.Count()} x {g.Key.Name}, x {g.Min(i => c.Parts[i].Pos.X):0.0}..{g.Max(i => c.Parts[i].Pos.X):0.0}, y {c.Parts[g.First()].Pos.Y:0.00}"
                    + string.Concat(g.Where(i => c.DefOf(i).ExtRunOut > 0).Select(i => $" [{i} table of {ConstructExtruder.TableMachine(c, i)}, run {RollerRuns.RunOf(c, i)}, links {string.Join("/", Enumerable.Range(0, 1).SelectMany(_ => { var l = new List<string>(); for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e)) l.Add(c.DefOf(c.OtherEnd(e)).Name); return l; }))}]"))
                    + string.Concat(g.Where(i => c.DefOf(i).IsRollerTile).Select(i => $" [{i} run {RollerRuns.RunOf(c, i)}]"))
                    + string.Concat(g.Where(i => WorkOrders.TakesOrders(c.DefOf(i))).Select(i => ConstructConveyor.Out(c, i) is var o and >= 0
                        ? $" [{i} out {o}: {ConstructConveyor.Length(c, o):0.0} m to {c.DefOf(c.Routes[o].To).Name} {c.Routes[o].To} at {c.Parts[c.Routes[o].To].Pos.X:0},{c.Parts[c.Routes[o].To].Pos.Z:0}]" : $" [{i} no out]"))
                    + string.Concat(g.Where(i => WorkOrders.TakesOrders(c.DefOf(i)) || c.DefOf(i).IsTurntable).SelectMany(i => Enumerable.Range(0, c.RouteCount)
                        .Where(k => c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].To < c.PartCount && c.Routes[k].From < c.PartCount && (MachineBlocks.Owner(c, c.Routes[k].To) == i || c.Routes[k].From == i))
                        .Select(k => $" [{i}: link {k} ({ConstructConveyor.Length(c, k):0.0} m) {c.DefOf(c.Routes[k].From).Name} {c.Routes[k].From} at {c.Parts[c.Routes[k].From].Pos.X:0},{c.Parts[c.Routes[k].From].Pos.Z:0} -> {c.DefOf(c.Routes[k].To).Name} {c.Routes[k].To} at {c.Parts[c.Routes[k].To].Pos.X:0},{c.Parts[c.Routes[k].To].Pos.Z:0}]"))));
        }
        if (Arg("--why") is { } whyParts) // (--why "PART;PART": each machine's answer to an order for it)
            foreach (var wn in whyParts.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (ConstructParts.Find(wn.Trim()) is { } wd)
                    for (var i = 0; i < c.PartCount; i++)
                        if (WorkOrders.TakesOrders(c.DefOf(i)) && !c.DefOf(i).IsBakeOven)
                            Console.WriteLine($"  why {wd.Name}: {c.DefOf(i).Name} {i} at {c.Parts[i].Pos.X:0},{c.Parts[i].Pos.Z:0}: {WorkOrders.Refusal(w, c, i, ConstructStation.OrderFor(wd, 1)) ?? "takes it"}");
        if (t < 0)
        {
            Console.WriteLine("  no Building Station in it");
            return 1;
        }
        if (Arg("--press-order") is { } pressOrder) // (--press-order PART --oven-at X,Z: an order queued at the Press nearest the point, run and watched)
            return PressOrder(w, q, c, place, pressOrder, Arg("--oven-at"), seconds, keep, Fill, args.Contains("--via-station") ? t : -1,
                int.TryParse(Arg("--count"), out var pcount) ? Math.Clamp(pcount, 1, WorkOrders.MaxCount) : 1); // (--count N: N of the part, so a line's Extruders share the blanks)
        if (Arg("--oven-order") is { } ovenOrder)
            using (place.In())
                RollerRuns.Sync(w, c); // (the stations' roll-outs, as the first conveyor step lays them: an oven's order asks the station that rolls to it)
        if (Arg("--oven-order") is { } ovenOrder2)
            return OvenOrder(w, q, c, place, ovenOrder2, Arg("--oven-at"), seconds, args.Contains("--trace"), keep, Fill);
        if (string.IsNullOrEmpty(build))
            return 0;
        if (ConstructStation.Find(build) is not { } target)
        {
            Console.WriteLine($"--station-check: no blueprint named \"{build}\"");
            return 1;
        }
        {
            Span<ushort> pads = stackalloc ushort[8];
            var np = ConstructStation.OwnPads(c, t, pads);
            Console.WriteLine($"  station part {t}: {ConstructStation.Arms(c, t)} rovers (docks), {np} pick pads{string.Concat(pads[..np].ToArray().Select(p => $" (part {p} at {c.Parts[p].Pos.X:0}, {c.Parts[p].Pos.Z:0}, hold {ConstructPads.RackDef(c, p).RackHoldMax - ConstructPads.RackDef(c, p).RackHoldMin:0.#})"))}");
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
                        maker = $"{c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0}";
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
        var slowMs = double.TryParse(Arg("--slow-ticks"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sm) ? sm : 0;
        if (slowMs > 0)
            (AllocProfile.On, AllocProfile.SlowTickMs) = (true, slowMs); // (--slow-ticks MS: each tick over MS named by its slowest sections, at the end)
        using (place.In())
            for (var k = 0; k < seconds * World.TickRate; k++)
            {
                MoverPhysics.Clock++; // (the server tick: Game.ServerTickStep sets it in a game; lifters' moves run on it, and never ended here before 2026-11-22)
                AllocProfile.TickStart();
                w.Step(q, World.TickDt);
                AllocProfile.TickEnd((uint)k);
                if (args.Contains("--trace") && k % (5 * World.TickRate) == 0)
                    foreach (var m in makers.Where(m => c.DefOf(m).IsAssembler))
                        Console.WriteLine($"  {k / World.TickRate,4} s: the {c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0}: queue {c.Parts[m].Orders?.Count ?? -1}, "
                            + (w.StoreOf(c, m) is { } ast2 ? ConstructAssembling.Look(c.DefOf(m), ast2, ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[m], c.DefOf(m)), ConstructAssembling.FitOf(c, m), false, new SandboxInventory(), null, c.Parts[m].Orders is { Bill: false }, null) : ""));
                // (and every Press and Extruder with orders: what it waits for, 2026-11-23)
                if (args.Contains("--trace") && k % (5 * World.TickRate) == 0)
                    foreach (var m in makers.Where(m => (c.DefOf(m).IsPress && !c.DefOf(m).IsLathe || c.DefOf(m).IsExtruder) && c.Parts[m].Orders is { Count: > 0 }))
                        if (w.StoreOf(c, m) is { } pst)
                            Console.WriteLine($"  {k / World.TickRate,4} s: the {c.DefOf(m).Name} part {m}: queue {c.Parts[m].Orders?.Count ?? -1}, "
                                + (c.DefOf(m).IsPress ? ConstructPress.Look(c, m, pst, false, null, Vector3.Zero) : ConstructExtruder.Look(c, m, pst, false, new SandboxInventory())));
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
        if (slowMs > 0)
        {
            AllocProfile.On = false;
            Console.WriteLine($"  ticks over {slowMs:0.#} ms: {AllocProfile.SlowTicks.Count}");
            foreach (var line in AllocProfile.SlowTicks)
                Console.WriteLine($"    {line}");
        }
        Consoles();
        string Holds(int i) => w.StoreOf(c, i) is not { } st ? "no store" : $"{st.Amount} {RecyclingTable.Current.NameOf(st.Material)}"
            + (st.InAmount > 0 ? $", in {st.InAmount} {RecyclingTable.Current.NameOf(st.InMaterial)}" : "")
            + (st.MixN > 0 ? ", mix " + string.Join(" ", Enumerable.Range(0, st.MixN).Select(k => $"{st.MixAmount![k]} {RecyclingTable.Current.NameOf(st.MixMat![k])}")) : "");
        foreach (var m in makers.Where(m => c.Parts[m].Orders is { Count: > 0 }))
        {
            var ol = c.Parts[m].Orders!;
            Console.WriteLine($"  the {c.DefOf(m).Name} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0} queue: {string.Join("; ", Enumerable.Range(0, ol.Count).Select(k => $"{ol.Items[k].Count} x {(ol.Items[k].Part != ConstructParts.None ? ConstructParts.Def(ol.Items[k].Part).Name : ol.Items[k].Name ?? "a piece")} (made {ol.Items[k].Made})"))}");
        }
        // What still rides a conveyor at the end, and how far along (an ordered piece that never arrives: 2026-11-23).
        for (var r = 0; r < c.ConveyedCount; r++)
        {
            var cr = c.Conveyed[r];
            if (cr.Route < c.RouteCount)
                Console.WriteLine($"  riding: {ConstructParts.Def(cr.Def)?.Name} on the conveyor from the {c.DefOf(c.Routes[cr.Route].Source).Name} to the {c.DefOf(c.Routes[cr.Route].To).Name}, {cr.At:0.00} m along, flags {cr.Flags}");
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
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders is { Count: > 0 } mo)
                for (var k = 0; k < mo.Count; k++)
                    Console.WriteLine($"  {c.DefOf(i).Name} {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: order {mo.Items[k].Name ?? mo.Items[k].Key.ToString()} x{mo.Items[k].Left}: {mo.Note}"
                        + (w.StoreOf(c, i) is { } ms ? $" (store {ms.Amount} of {RecyclingTable.Current.NameOf(ms.Material)}, in {ms.InAmount}, busy {ms.Busy}, held {ms.Held}, blocked {ms.Blocked}, refused {(ExtrudeStall)ms.Refused})" : "")
                        + string.Concat(Enumerable.Range(0, c.RouteCount).Where(k => MachineBlocks.Owner(c, c.Routes[k].From) == i)
                            .Select(k => $" [route {k} to {c.DefOf(MachineBlocks.Owner(c, c.Routes[k].To)).Name} {ConstructConveyor.Length(c, k):0.0} m, riders {Enumerable.Range(0, c.ConveyedCount).Count(j => c.Conveyed[j].Route == k)}]")));
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref readonly var cr = ref c.Conveyed[i];
            ConstructConveyor.WorldPose(c, cr.Route, cr, ConstructParts.Def(cr.Def)!, out var rp, out _);
            Console.WriteLine($"  riding: {ConstructParts.Def(cr.Def)?.Name} on link {cr.Route} at {cr.At:0.0} of {ConstructConveyor.Length(c, cr.Route):0.0} m ({rp.X:0}, {rp.Z:0}), dest {cr.Dest} tag {cr.Tag}, from the {c.DefOf(c.Routes[cr.Route].From).Name} {c.Routes[cr.Route].From} to the {(c.Routes[cr.Route].To < c.PartCount ? c.DefOf(c.Routes[cr.Route].To).Name : "-")} {c.Routes[cr.Route].To} ({c.Routes[cr.Route].Count} legs)");
        }
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
        // Each station's roll-out (its runs are laid on the first conveyor step): the oven whose bed its kits roll into.
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsBuildStation)
            {
                var run = ConstructRollOut.RunOut(c, i);
                Console.WriteLine($"  Building Station part {i}: " + (run < 0 ? "no roll-out run" : ConstructRollOut.OvenOf(c, run) is var oven and >= 0
                    ? $"rolls out into the bed of the Bake Oven part {oven} at {c.Parts[oven].Pos.X:0}, {c.Parts[oven].Pos.Z:0}" : $"rolls out on run {run}, into no oven's bed: {ConstructRollOut.NoOvenWhy(c, run)}"));
            }
        // Each other station's job as it stands (a line's kit station: what it builds and waits for).
        for (var i = 0; i < c.PartCount; i++)
            if (i != t && c.DefOf(i).IsBuildStation && c.Parts[i].Station is { } oj)
                Console.WriteLine($"  Building Station part {i}: {ConstructStation.StateText(oj)}, {oj.Fitted}/{oj.Steps} fitted: {oj.Note}");
        if (args.Contains("--reach"))
            Reach(w, c, t, j);
        var whys = new Dictionary<string, List<string>>();
        for (var i = 0; i < j.KindN; i++)
            if (j.KindWhy[i] is { } why)
            {
                var name = ConstructParts.Get(j.KindDef[i])?.Name ?? $"#{j.KindDef[i]}";
                if (!whys.TryGetValue(why, out var list))
                    whys[why] = list = [];
                list.Add($"{j.KindNeed[i] - j.KindFit[i]} x {name}");
            }
        for (var i = 0; i < j.KindN; i++)
            if (j.KindWhy[i] != null && ConstructParts.Get(j.KindDef[i]) is { } kd && BakeKits.Has(kd))
                for (var m = 0; m < c.PartCount; m++)
                    if (c.DefOf(m).IsBakeOven)
                        Console.WriteLine($"  the Bake Oven at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0} on the {kd.Name}: {WorkOrders.Refusal(w, c, m, ConstructStation.OrderFor(kd, 1)) ?? "takes it"}");
        Console.WriteLine($"  kinds whose order was refused ({whys.Sum(x => x.Value.Count)}), by reason:");
        foreach (var (why, list) in whys.OrderByDescending(x => x.Value.Count))
            Console.WriteLine($"    {why}: {string.Join(", ", list)}");
        Console.WriteLine($"  riders on its conveyors ({c.ConveyedCount}):");
        for (var k = 0; k < c.ConveyedCount; k++)
        {
            ref readonly var r = ref c.Conveyed[k];
            var rt = r.Route < c.RouteCount ? c.Routes[r.Route] : default;
            Console.WriteLine($"    {c.DefOf(0) switch { _ => ConstructParts.Def(r.Def)?.Name ?? "?" }} on conveyor {r.Route} ({(r.Route < c.RouteCount ? $"{c.DefOf(rt.Source).Name} -> {c.DefOf(rt.To).Name}" : "gone")}) at {r.At:0.0} m, dest {r.Dest} tag {r.Tag} flags {r.Flags}{(r.Route < c.RouteCount && w.StoreOf(c, ConstructPads.Owner(c, rt.To)) is { } dst && ConstructParts.Def(r.Def) is { } rdf ? $", its end's check {ConstructConveyor.RackCheck(c, ConstructPads.Owner(c, rt.To), dst, rdf)} ({dst.ItemCount} items)" : "")}");
        }
        if (c.Parts[t].Craft is { } cq)
        {
            Console.WriteLine($"  open orders ({cq.Count}):");
            for (var k = 0; k < cq.Count; k++)
                Console.WriteLine($"    {cq.Items[k].What.Count} x {ConstructParts.Get(ConstructCrafting.KindOf(ref cq.Items[k]))?.Name ?? "?"}: {ConstructCrafting.StateText(c, cq.Items[k])}");
        }
        return preview.Unmade;
    }

    /// <summary>
    /// <c>--reach</c> (Sean 2026-11-24: arms placed so they move pieces from the pads at the end of the lines straight into the bay): for
    /// the station's build, the lifters near its bay, each with the steps it can fit (carries the kind, and the piece's middle at its place is
    /// in reach with its lift: the rule <see cref="ConstructLifters.DirectStep"/> and the station's offers use), whether it reaches the hold,
    /// and how much of each pick pad's deck it reaches with a 4 m wrist lift (a hull piece's); then the steps no arm fits and why. Cranes
    /// are counted apart: the question is the arms'. Body frame, as the rest of the report.
    /// </summary>
    private static void Reach(ConstructWorld w, Construct c, int t, StationJob j)
    {
        if (j.Plan is not { } plan || plan.Centre.Length != j.StepKind.Length)
        {
            Console.WriteLine("  reach: the build isn't costed yet (run a second or more)");
            return;
        }
        ConstructStation.Bay(c, t, out var floor, out var bayRot, out var bay);
        Span<ushort> own = stackalloc ushort[8];
        var nOwn = ConstructStation.OwnPads(c, t, own);
        // (its own pick pads, then any other pad within 30 m of the bay's edge: an arm's pick pad, a staging or fittings pad)
        var padList = own[..nOwn].ToArray().Select(p => (int)p).ToList();
        for (var a = 0; a < c.PieceRackCount; a++)
            if (c.PieceRacks[a] is var p && p < c.PartCount && !padList.Contains(p) && ConstructStation.PadInBay(c, t, p, out var pm, out var ph)
                && MathF.Abs(pm.X) - ph.X < bay.X * 0.5f + 30 && MathF.Abs(pm.Z) - ph.Z < bay.Z * 0.5f + 30)
                padList.Add(p);
        var np = padList.Count;
        var arms = new List<(int Part, LifterDef Ld, Vector3 Shoulder, Quaternion Rot)>();
        var cranes = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (ConstructLifters.DefOf(c, i) is { Fitter: false } ld)
            {
                ConstructLifters.Frame(c, i, ld, out _, out var lr, out var shoulder);
                if (Vector3.Distance(floor, shoulder) > ld.ReachMax + bay.Length() * 0.5f + ld.Down + (ld.Bridge ? 64 : 0))
                    continue;
                if (ld.Bridge)
                    cranes++;
                else
                    arms.Add((i, ld, shoulder, lr));
            }
        Console.WriteLine($"  reach: {arms.Count} arm(s) and {cranes} crane(s) by station part {t}'s bay; {j.Steps} steps, {nOwn} pick pad(s) of its own and {np - nOwn} other pad(s) near");
        const float PadLift = 4f; // (a hull piece's wrist lift over its middle: half a 7 m diagonal and the grip)
        var padCover = new int[np];
        var padPoints = new int[np];
        var padPts = new List<Vector3>[np];
        for (var k = 0; k < np; k++)
        {
            padPts[k] = [];
            if (!ConstructStation.PadInBay(c, t, padList[k], out var mid, out var half))
                continue;
            for (var x = -half.X + 0.5f; x <= half.X - 0.5f + 0.01f; x += 1f)
                for (var z = -half.Z + 0.5f; z <= half.Z - 0.5f + 0.01f; z += 1f)
                    padPts[k].Add(floor + Vector3.Transform(new Vector3(mid.X + x, mid.Y + 1f, mid.Z + z), bayRot)); // (a piece's middle a metre over the deck)
            padPoints[k] = padPts[k].Count;
        }
        var stepBy = new int[j.StepKind.Length];
        foreach (var a in arms)
        {
            var fits = 0;
            for (var s = 0; s < j.StepKind.Length; s++)
            {
                if (j.StepKind[s] < 0 || ConstructParts.Get(j.KindDef[j.StepKind[s]]) is not { } d || !ConstructLifters.Carries(a.Ld, d))
                    continue;
                if (!ConstructLifters.InReach(a.Ld, a.Shoulder, a.Rot, j.Pos + Vector3.Transform(plan.Centre[s], j.Rot), ConstructLifters.WristLift(d), out _))
                    continue;
                fits++;
                stepBy[s]++;
            }
            var hold = ConstructLifters.HoldPick(c, t, a.Ld, a.Shoulder, a.Rot, ConstructLifters.SafeLift, ConstructLifters.SafeLift, out _);
            var padText = new System.Text.StringBuilder();
            for (var k = 0; k < np; k++)
            {
                var n = padPts[k].Count(p => ConstructLifters.InReach(a.Ld, a.Shoulder, a.Rot, p, PadLift, out _));
                if (n == 0)
                    continue;
                padText.Append($", pad {padList[k]} {100 * n / Math.Max(1, padPoints[k])}%");
            }
            Console.WriteLine($"    {c.DefOf(a.Part).Name} (part {a.Part}) at {a.Shoulder.X:0}, {a.Shoulder.Z:0}: fits {fits} of {j.Steps} steps; the hold {(hold ? "in reach" : "out of reach")}{padText}");
            CarryRises(w, c, j, plan, a.Part, a.Ld, a.Shoulder, a.Rot, padList, padPts);
        }
        for (var k = 0; k < np; k++)
        {
            var n = padPts[k].Count(p => arms.Any(a => ConstructLifters.InReach(a.Ld, a.Shoulder, a.Rot, p, PadLift, out _)));
            padCover[k] = n;
            Console.WriteLine($"    {(k < nOwn ? "pick pad" : "other pad")} {padList[k]} at {c.Parts[padList[k]].Pos.X:0}, {c.Parts[padList[k]].Pos.Z:0}: {100 * n / Math.Max(1, padPoints[k])}% of its deck in some arm's reach");
        }
        var none = new SortedDictionary<string, int>();
        for (var s = 0; s < j.StepKind.Length; s++)
        {
            if (stepBy[s] > 0 || j.StepKind[s] < 0 || ConstructParts.Get(j.KindDef[j.StepKind[s]]) is not { } d)
                continue;
            var why = arms.Count == 0 ? "no arm" : arms.Any(a => ConstructLifters.Carries(a.Ld, d)) ? "out of every arm's reach"
                : d.Mass > arms.Max(a => a.Ld.Payload) ? $"{d.Mass / 1000:0.#} t, past every arm's payload" : "bigger than every arm's hand";
            var key = $"{d.Name} ({why})";
            none[key] = none.GetValueOrDefault(key) + 1;
        }
        Console.WriteLine($"    steps no arm fits: {none.Values.Sum()} of {j.Steps}{(none.Count > 0 ? ": " : "")}{string.Join(", ", none.Select(x => $"{x.Value} x {x.Key}"))}");
    }

    /// <summary>
    /// The carry height an arm would take with the biggest piece it fits from each pad it reaches to that piece's step (the server's
    /// clearance over what stands in the way, <c>ConstructLifters.ClearRise</c>): its pad point nearest the pad's middle, the piece
    /// lying as it is fitted.
    /// </summary>
    private static void CarryRises(ConstructWorld w, Construct c, StationJob j, StationJob.LiftPlan plan, int part, LifterDef ld, Vector3 shoulder, Quaternion rot, List<int> pads, List<Vector3>[] padPts)
    {
        var best = -1;
        var bestSize = 0f;
        for (var s = 0; s < j.StepKind.Length; s++)
            if (j.StepKind[s] >= 0 && ConstructParts.Get(j.KindDef[j.StepKind[s]]) is { } d && ConstructLifters.Carries(ld, d)
                && ConstructLifters.InReach(ld, shoulder, rot, j.Pos + Vector3.Transform(plan.Centre[s], j.Rot), ConstructLifters.WristLift(d), out _)
                && (d.BoundsMax - d.BoundsMin) is var sz && sz.X * sz.Y * sz.Z > bestSize)
                (best, bestSize) = (s, sz.X * sz.Y * sz.Z);
        if (best < 0)
            return;
        var piece = ConstructParts.Get(j.KindDef[j.StepKind[best]])!;
        ConstructLifters.Frame(c, part, ld, out var lp, out var lr, out _);
        var inv = Quaternion.Conjugate(lr);
        var turn = Quaternion.Normalize(inv * j.Rot * plan.Rot[best]);
        var mid = (piece.BoundsMin + piece.BoundsMax) * 0.5f;
        var halfY = Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(j.Rot * plan.Rot[best])) is var up
            ? MathF.Abs(up.X) * (piece.BoundsMax.X - piece.BoundsMin.X) * 0.5f + MathF.Abs(up.Y) * (piece.BoundsMax.Y - piece.BoundsMin.Y) * 0.5f + MathF.Abs(up.Z) * (piece.BoundsMax.Z - piece.BoundsMin.Z) * 0.5f : 0;
        var stepMid = j.Pos + Vector3.Transform(plan.Centre[best], j.Rot);
        for (var k = 0; k < pads.Count; k++)
        {
            var near = padPts[k].Where(p => ConstructLifters.InReach(ld, shoulder, rot, p, ConstructLifters.WristLift(piece), out _)).ToList();
            if (near.Count == 0)
                continue;
            var centre = new Vector3(near.Average(p => p.X), near.Average(p => p.Y), near.Average(p => p.Z));
            var at = near.MinBy(p => Vector3.DistanceSquared(p, centre)) - Vector3.UnitY * 1f + Vector3.UnitY * (halfY + 0.05f); // (the piece's middle over the deck)
            var l = new CLift
            {
                Part = (ushort)part,
                From = Vector3.Transform(at - lp, inv) - Vector3.Transform(mid, turn),
                To = Vector3.Transform(stepMid - lp, inv) - Vector3.Transform(mid, turn),
                FromRot = turn,
                ToRot = turn,
            };
            var rise = ConstructLifters.ClearRise(w, c, l, ld, piece);
            Console.WriteLine($"      a {piece.Name} from pad {pads[k]} to its step: carried {rise:0.0} m over the higher end");
        }
    }

    /// <summary>
    /// <c>--oven-order PART [--oven-at X,Z]</c>: an order for a part made from a kit (BakeKits.cs) queued on the site's Bake Oven nearest the
    /// point, then the site run: the kit started at a station, built, carried to the bed, baked and picked off. Reports each stage's time.
    /// </summary>
    private static int PressOrder(ConstructWorld w, SelfTest.DriveWorld q, Construct c, VehicleDriveCheck.CheckPlace place, string partName, string? at, int seconds,
        List<(int Bin, int Material, int N)> keep, Func<int, int, byte, int> fill, int station, int count)
    {
        if (ConstructParts.Find(partName) is not { } d)
        {
            Console.WriteLine($"--press-order: no part \"{partName}\"");
            return 1;
        }
        var point = at?.Split(',') is [var xs, var zs] && float.TryParse(xs, out var x) && float.TryParse(zs, out var z) ? new Vector2(x, z) : Vector2.Zero;
        var press = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).IsPress).OrderBy(i => Vector2.Distance(point, new Vector2(c.Parts[i].Pos.X, c.Parts[i].Pos.Z))).FirstOrDefault(-1);
        if (press < 0)
        {
            Console.WriteLine("  no Press on the site");
            return 1;
        }
        string? why;
        using (place.In())
            why = station >= 0 ? ConstructCrafting.Place(w, c, station, ConstructStation.OrderFor(d, count)) : WorkOrders.Queue(w, c, press, ConstructStation.OrderFor(d, count)); // (--via-station: the station's own ask, which chains the blank)
        Console.WriteLine($"  {(station >= 0 ? $"station {station}'s ask; " : "")}the Press at {c.Parts[press].Pos.X:0}, {c.Parts[press].Pos.Z:0} (part {press}): order for the {d.Name}: {why ?? "taken"}");
        if (why != null)
            return 1;
        var last = "";
        var exCores = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i) is { IsExtruder: true, IsPanelFormer: false, BlockRole: BlockRole.Core }).ToArray();
        var exLast = new string[exCores.Length];
        using (place.In())
            for (var k = 0; k < seconds * World.TickRate; k++)
            {
                MoverPhysics.Clock++;
                w.Step(q, World.TickDt);
                if (k % World.TickRate == 0) // (each Extruder's queue as it changes: which of a line's Extruders makes the blanks)
                    for (var e = 0; e < exCores.Length; e++)
                        if ((c.Parts[exCores[e]].Orders is { Count: > 0 } eo ? $"{eo.Count} order(s), head x{eo.Items[0].Left}" : "idle") is var et && et != (exLast[e] ?? "idle"))
                            Console.WriteLine($"  {k / World.TickRate,6} s: Extruder {exCores[e]} at {c.Parts[exCores[e]].Pos.X:0}, {c.Parts[exCores[e]].Pos.Z:0}: {exLast[e] = et}");
                if (k % World.TickRate == 0)
                    foreach (var (bin, mat, n) in keep)
                        if (c.DefOf(bin).IsAssembler)
                            fill(bin, n, (byte)mat);
                        else if (w.StoreOf(c, bin) is { } st && (st.Amount == 0 || st.Material == mat) && st.Amount < n)
                            w.Deposit(c, bin, n - st.Amount, (byte)mat);
                if (k % (5 * World.TickRate) == 0 && press < c.PartCount && c.DefOf(press).IsPress && w.StoreOf(c, press) is { } pst)
                {
                    var look = ConstructPress.Look(c, press, pst, false, null, c.Parts[press].Pos);
                    if (look != last)
                        Console.WriteLine($"  {k / World.TickRate,6} s: press: {last = look}");
                    if (station < 0 && c.Parts[press].Orders is not { Count: > 0 })
                    {
                        Console.WriteLine($"  {k / World.TickRate,6} s: the order is done");
                        return 0;
                    }
                }
            }
        Console.WriteLine($"  after {seconds} s the order is still open");
        foreach (var e in exCores) // (which presses each Extruder's pieces can reach: a line's Extruders share a press's blanks, ConstructLogisticsPlan.FindMaker)
            if (Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i) is { IsPress: true, IsLathe: false } && ConstructLogisticsPlan.Leads(c, e, i)).ToList() is { Count: > 0 } reach)
                Console.WriteLine($"  Extruder {e} at {c.Parts[e].Pos.X:0}, {c.Parts[e].Pos.Z:0} leads to the Press(es) " + string.Join(", ", reach.Select(i => $"{i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}")));
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Craft is { Count: > 0 } cq)
                for (var k = 0; k < cq.Count; k++)
                    Console.WriteLine($"  {c.DefOf(i).Name} {i}'s ask: {ConstructCrafting.StateText(c, cq.Items[k])}");
        for (var i = 0; i < c.ConveyedCount; i++)
            Console.WriteLine($"  riding: {ConstructParts.Def(c.Conveyed[i].Def).Name} on link {c.Conveyed[i].Route} at {c.Conveyed[i].At:0.0} of {ConstructConveyor.Length(c, c.Conveyed[i].Route):0.0} m, to {c.DefOf(c.Routes[c.Conveyed[i].Route].To).Name} {c.Routes[c.Conveyed[i].Route].To} at {c.Parts[c.Routes[c.Conveyed[i].Route].To].Pos.X:0},{c.Parts[c.Routes[c.Conveyed[i].Route].To].Pos.Z:0} from {c.DefOf(c.Routes[c.Conveyed[i].Route].From).Name} {c.Routes[c.Conveyed[i].Route].From} at {c.Parts[c.Routes[c.Conveyed[i].Route].From].Pos.X:0},{c.Parts[c.Routes[c.Conveyed[i].Route].From].Pos.Z:0}, dest {c.Conveyed[i].Dest} (part {ConstructLogistics.PartOfStore(c, c.Conveyed[i].Dest)} {(ConstructLogistics.PartOfStore(c, c.Conveyed[i].Dest) is var dpp and >= 0 ? c.DefOf(dpp).Name : "")}), way {(ConstructLogistics.Map(c) is var lm && lm.RouterSlot(c.Routes[c.Conveyed[i].Route].To) is var lrs and >= 0 ? ConstructLogistics.TurntableWay(c, lm, lrs, c.Conveyed[i].Route, c.Conveyed[i]) : -9)}, flags {c.Conveyed[i].Flags}; outs from there: " + string.Join("; ", Enumerable.Range(0, c.RouteCount).Where(k => c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].From == c.Routes[c.Conveyed[i].Route].To).Select(k => $"link {k} {ConstructConveyor.Length(c, k):0.0} m to {c.DefOf(c.Routes[k].To).Name} {c.Routes[k].To} at {c.Parts[c.Routes[k].To].Pos.X:0},{c.Parts[c.Routes[k].To].Pos.Z:0} riders {c.Routes[k].Count}"))
                + (ConstructPads.Owner(c, c.Routes[c.Conveyed[i].Route].To) is var rto && w.StoreOf(c, rto) is { } rst && ConstructParts.Def(c.Conveyed[i].Def) is { } rdef
                    ? $" (rack check {ConstructConveyor.RackCheck(c, rto, rst, rdef)}, dest {c.Conveyed[i].Dest}, tag {c.Conveyed[i].Tag}, bounds {rdef.BoundsMax - rdef.BoundsMin}, pad {rto} hold {Hold(ConstructPads.RackDef(c, rto))})" : ""));
        foreach (var b in w.Bodies)
            if (b != c && b.PartCount < 4)
            {
                Console.WriteLine($"  loose: {b.DefOf(0).Name} at {b.Pos.X:0.0}, {b.Pos.Y:0.0}, {b.Pos.Z:0.0}");
                for (var i = 0; i < c.PartCount; i++) // (the lifters within 20 m of it, and what each says)
                    if (ConstructLifters.DefOf(c, i) != null && Vector2.Distance(new Vector2(c.Parts[i].Pos.X, c.Parts[i].Pos.Z), new Vector2(b.Pos.X, b.Pos.Z)) < 20)
                    {
                        Console.WriteLine($"    lifter {i} {c.DefOf(i).Name} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: {ConstructLifters.Look(w, c, i)}");
                        var lpts = new LiftPoint[64];
                        var lpn = ConstructLifters.PanelPoints(w, c, i, lpts);
                        Console.WriteLine("      points: " + string.Join("; ", lpts[..lpn].Select(pt => $"{ConstructLifters.PointName(w, pt)} {pt.Part}")));
                        Console.WriteLine($"      takes it? {ConstructLifters.OutWhy(w, c, i, b) ?? "yes"}");
                    }
            }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders is { Count: > 0 } mo)
                Console.WriteLine($"  {c.DefOf(i).Name} {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: {mo.Count} order(s), head x{mo.Items[0].Left}: {mo.Note}" + (c.DefOf(i).IsExtruder && !c.DefOf(i).IsPanelFormer && w.StoreOf(c, i) is { } exs ? $"; looks: {ConstructExtruder.Look(c, i, exs, false, new SandboxInventory())}" : ""));
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i) is { IsAssembler: true, BlockRole: BlockRole.Core } ad && w.StoreOf(c, i) is { } ast3 && (c.Parts[i].Orders is { Count: > 0 } || ast3.ItemCount > 0))
                Console.WriteLine($"  Assembler {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: " + ConstructAssembling.Look(ad, ast3, ConstructAssembling.RecipeOf(AssemblingTable.Current, c.Parts[i], ad), ConstructAssembling.FitOf(c, i), false, new SandboxInventory(), null, c.Parts[i].Orders is { Bill: false }, null));
        Span<ushort> own = stackalloc ushort[ConstructStation.MaxOwnPads];
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsBuildStation && ConstructStation.OwnPads(c, i, own) is var pn)
                Console.WriteLine($"  station {i}'s pick pads: " + string.Join(", ", own[..pn].ToArray().Select(p => $"{p} at {c.Parts[p].Pos.X:0},{c.Parts[p].Pos.Z:0} holds {w.StoreOf(c, p)?.ItemCount ?? 0}")));
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsLayDownPad && ConstructPads.Owner(c, i) == i && w.StoreOf(c, i) is { ItemCount: > 0 } ps)
                Console.WriteLine($"  pad {i} at {c.Parts[i].Pos.X:0},{c.Parts[i].Pos.Z:0} holds {ps.ItemCount}" + string.Concat(Enumerable.Range(0, c.PartCount).Where(t => c.DefOf(t).IsBuildStation)
                    .Select(t => ConstructStation.PadInBay(c, t, i, out var pm, out var ph) ? $"; in station {t}'s bay ({ConstructStation.BaySize(c, t)}): at {pm.X:0.0},{pm.Y:0.0},{pm.Z:0.0} half {ph.X:0.0},{ph.Z:0.0}" : "")));
        return 1;
    }

    private static string Hold(ConstructPartDef rd)
    {
        ConstructRackPack.Hold(rd, out var lo, out var hi);
        return $"{hi - lo}";
    }

    private static int OvenOrder(ConstructWorld w, SelfTest.DriveWorld q, Construct c, VehicleDriveCheck.CheckPlace place, string partName, string? at, int seconds, bool trace,
        List<(int Bin, int Material, int N)> keep, Func<int, int, byte, int> fill)
    {
        if (ConstructParts.Find(partName) is not { } d)
        {
            Console.WriteLine($"--oven-order: no part \"{partName}\"");
            return 1;
        }
        var point = at?.Split(',') is [var xs, var zs] && float.TryParse(xs, out var x) && float.TryParse(zs, out var z) ? new Vector2(x, z) : Vector2.Zero;
        var oven = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).IsBakeOven).OrderBy(i => Vector2.Distance(point, new Vector2(c.Parts[i].Pos.X, c.Parts[i].Pos.Z))).FirstOrDefault(-1);
        if (oven < 0)
        {
            Console.WriteLine("  no Bake Oven on the site");
            return 1;
        }
        string? why;
        using (place.In())
            why = WorkOrders.Queue(w, c, oven, ConstructStation.OrderFor(d, 1));
        Console.WriteLine($"  the Bake Oven at {c.Parts[oven].Pos.X:0}, {c.Parts[oven].Pos.Z:0} (part {oven}): order for the {d.Name}: {why ?? "taken"}");
        if (why != null)
            return 1;
        var made = -1;
        var lastStall = (byte)255;
        var loose = Environment.GetCommandLineArgs().Contains("--kit-loose");
        var carry = Environment.GetCommandLineArgs().Contains("--kit-carry");
        var kit = BakeKits.KitOf(d) ?? [];
        var cmd = Environment.GetCommandLineArgs();
        var after = Array.IndexOf(cmd, "--after") is var ai and >= 0 && ai + 1 < cmd.Length && int.TryParse(cmd[ai + 1], out var an) ? an : 0; // (--after N: run on N s once it is baked: the part rolling out)
        ushort lastLoose = 0;
        var nextLoose = 0;
        using (place.In())
            for (var k = 0; k < seconds * World.TickRate && (made < 0 || k < made + after * World.TickRate); k++)
            {
                MoverPhysics.Clock++;
                w.Step(q, World.TickDt);
                if (k % World.TickRate == 0)
                    foreach (var (bin, mat, n) in keep)
                        if (c.DefOf(bin).IsAssembler)
                            fill(bin, n, (byte)mat);
                        else if (w.StoreOf(c, bin) is { } st && (st.Amount == 0 || st.Material == mat) && st.Amount < n)
                            w.Deposit(c, bin, n - st.Amount, (byte)mat);
                if (w.StoreOf(c, oven) is { } ost && ost.Refused != lastStall)
                {
                    lastStall = ost.Refused;
                    Console.WriteLine($"  {k / (float)World.TickRate,6:0.0} s: oven: {ConstructBakeOven.StallText((OvenStall)ost.Refused)}");
                }
                if (trace && k % (10 * World.TickRate) == 0)
                    for (var s = 0; s < c.PartCount; s++)
                        if (c.Parts[s].Station is { } sj && sj.Name.EndsWith(BakeKits.KitSuffix, StringComparison.Ordinal))
                            Console.WriteLine($"  {k / World.TickRate,6} s: station {s}: {ConstructStation.StateText(sj)}, {sj.Fitted}/{sj.Steps}: {sj.Note}");
                if (loose && k % (World.TickRate / 2) == 0 && (lastLoose == 0 || w.Get(lastLoose) == null))
                {
                    // --kit-loose: the kit's pieces set down one by one in its station's bay (as a player drops them), not ordered.
                    lastLoose = 0;
                    for (var s = 0; s < c.PartCount && lastLoose == 0; s++)
                        if (c.Parts[s].Station is { State: StationState.Building } sj && sj.Name.Equals(d.Name + BakeKits.KitSuffix, StringComparison.Ordinal) && nextLoose < kit.Length)
                        {
                            ConstructStation.Bay(c, s, out var floor, out _, out _);
                            var pd = Extrusion.Def(kit[nextLoose].Key)!;
                            var b = w.Create(floor + new Vector3(0, 1.5f, 0), Quaternion.Identity)!;
                            w.AddPart(b, pd, -(pd.BoundsMin + pd.BoundsMax) * 0.5f, 0);
                            b.Rebuild();
                            lastLoose = b.Id;
                            Console.WriteLine($"  {k / (float)World.TickRate,6:0.0} s: {pd.Name} set down in the bay of station {s}");
                            nextLoose++;
                        }
                }
                if (carry && k % World.TickRate == 0 && w.StoreOf(c, oven) is { Refused: (byte)OvenStall.Empty })
                    for (var s = 0; s < c.PartCount; s++)
                        if (c.Parts[s].Station is { State: StationState.Done } sj && sj.Name.Equals(d.Name + BakeKits.KitSuffix, StringComparison.Ordinal)
                            && w.Get(sj.Body) is { } kb && !ConstructStation.BayClearOf(w, c, s, kb) && ConstructLifters.CarryingBody(w, kb.Id) == null)
                        {
                            // --kit-carry: the kit set on the bed by the check, as a crane would set it (for a site whose cranes don't reach both).
                            if (ConstructBakeOven.Landing(c, oven, kb, out var lp, out var lr) is { } no)
                                Console.WriteLine($"  the kit doesn't land: {no}");
                            else
                            {
                                kb.Pos = kb.PrevPos = lp;
                                kb.Rot = kb.PrevRot = lr;
                                kb.Vel = kb.AngVel = Vector3.Zero;
                                kb.HeldBy = 0;
                                kb.Wake();
                                Console.WriteLine($"  {k / (float)World.TickRate,6:0.0} s: the kit (body {kb.Id}) set on the oven's bed by hand");
                            }
                        }
                if (made < 0 && c.Parts[oven].Orders is not { Count: > 0 })
                    made = k;
            }
        Console.WriteLine(made >= 0 ? $"  the {d.Name} baked after {made / (float)World.TickRate:0.0} s" : $"  after {seconds} s the order is still open: {c.Parts[oven].Orders?.Note}");
        // (each line machine's queue and what it made: how a station's Line split shared the kit's pieces over its lines, 2026-11-27)
        for (var m = 0; m < c.PartCount; m++)
            if (c.DefOf(m).IsExtruder && c.Parts[m].Orders is { Bill: false, Count: > 0 } mq)
                Console.WriteLine($"  {c.DefOf(m).Name} {m} at {c.Parts[m].Pos.X:0}, {c.Parts[m].Pos.Z:0}: {mq.Count} order(s), {mq.PiecesLeft} piece(s) left" +
                                  string.Concat(Enumerable.Range(0, mq.Count).Select(k => $"; {ConstructCrafting.NameOf(mq.Items[k])} {mq.Items[k].Made}/{mq.Items[k].Count}")));
        for (var s = 0; s < c.PartCount; s++)
            if (c.Parts[s].Station is { } sj && sj.Name.EndsWith(BakeKits.KitSuffix, StringComparison.Ordinal))
            {
                Console.WriteLine($"  station {s} at {c.Parts[s].Pos.X:0}, {c.Parts[s].Pos.Z:0}: {ConstructStation.StateText(sj)}, {sj.Fitted}/{sj.Steps}: {sj.Note}");
                for (var kk = 0; kk < sj.KindN; kk++)
                    Console.WriteLine($"    kind {ConstructParts.Get(sj.KindDef[kk])?.Name}: need {sj.KindNeed[kk]}, fitted {sj.KindFit[kk]}, ready {sj.KindReady[kk]}: {sj.KindWhy[kk] ?? "-"}");
                if (c.Parts[s].Craft is { } cq)
                    for (var k = 0; k < cq.Count; k++)
                        Console.WriteLine($"    {cq.Items[k].What.Count} x {ConstructCrafting.NameOf(cq.Items[k].What)}: {ConstructCrafting.StateText(c, cq.Items[k])}");
                if (sj.Plan is { } plan)
                {
                    ConstructStation.Bay(c, s, out var bfl, out _, out var bsz);
                    Console.WriteLine($"    job at {sj.Pos:0.00} turn {sj.Rot:0.00}, bay floor {bfl:0.00} size {bsz:0.0}, step 0 at {plan.At[0]:0.00} middle {plan.Centre[0]:0.00}");
                }
                if (sj.Plan is { } plan2)
                    foreach (var lb in w.LiftBodies)
                        if (w.Get(lb.Body) is { } ac)
                            for (var li = 0; li < lb.N; li++)
                                if (lb.Lifts[li] is { } l && ConstructLifters.DefOf(ac, l.Part) is { Bridge: true } ld)
                                {
                                    ConstructLifters.Frame(ac, l.Part, ld, out _, out var lr, out var sh);
                                    for (var kk = sj.Cursor; kk < sj.PartOf.Length; kk++)
                                    {
                                        if (sj.PartOf[kk] != -1 || sj.StepKind[kk] < 0 || ConstructParts.Get(sj.KindDef[sj.StepKind[kk]]) is not { } sd)
                                            continue;
                                        var spot = sj.Pos + Vector3.Transform(plan2.Centre[kk], sj.Rot);
                                        var rel = Vector3.Transform(spot - sh, Quaternion.Conjugate(lr));
                                        Console.WriteLine($"    crane {l.Part}: step {kk} {sd.Name}: ready {ConstructStation.ReadyStep(sj, kk)}, carries {ConstructLifters.Carries(ld, sd)} ({sd.Mass:0} kg, {sd.BoundsMax - sd.BoundsMin:0.00}), reach {ConstructLifters.InReach(ld, sh, lr, spot, ConstructLifters.WristLift(sd), out _)} (rel {rel:0.0}, across {ld.AcrossMin:0.0}..{ld.AcrossMax:0.0}, run {ld.RunLo:0.0}..{ld.RunHi:0.0}, depth {ld.Depth:0.0}, down {ld.Down:0.0}, lift {ConstructLifters.WristLift(sd):0.0}), pad {ConstructLifters.PickPadHolding(w, l, sd.Id)}");
                                        break;
                                    }
                                }
            }
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Orders is { Count: > 0 } mo)
                for (var k = 0; k < mo.Count; k++)
                    Console.WriteLine($"  {c.DefOf(i).Name} {i} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0}: order {mo.Items[k].Name ?? mo.Items[k].Key.ToString()} x{mo.Items[k].Left}: {mo.Note}"
                        + (w.StoreOf(c, i) is { } ms ? $" (store {ms.Amount} of {RecyclingTable.Current.NameOf(ms.Material)}, in {ms.InAmount}, busy {ms.Busy}, held {ms.Held}, blocked {ms.Blocked}, refused {(ExtrudeStall)ms.Refused})" : "")
                        + string.Concat(Enumerable.Range(0, c.RouteCount).Where(k => MachineBlocks.Owner(c, c.Routes[k].From) == i)
                            .Select(k => $" [route {k} to {c.DefOf(MachineBlocks.Owner(c, c.Routes[k].To)).Name} {ConstructConveyor.Length(c, k):0.0} m, riders {Enumerable.Range(0, c.ConveyedCount).Count(j => c.Conveyed[j].Route == k)}]")));
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            ref readonly var cr = ref c.Conveyed[i];
            ConstructConveyor.WorldPose(c, cr.Route, cr, ConstructParts.Def(cr.Def)!, out var rp, out _);
            Console.WriteLine($"  riding: {ConstructParts.Def(cr.Def)?.Name} on link {cr.Route} at {cr.At:0.0} of {ConstructConveyor.Length(c, cr.Route):0.0} m ({rp.X:0}, {rp.Z:0}), dest {cr.Dest} tag {cr.Tag}, from the {c.DefOf(c.Routes[cr.Route].From).Name} {c.Routes[cr.Route].From} to the {(c.Routes[cr.Route].To < c.PartCount ? c.DefOf(c.Routes[cr.Route].To).Name : "-")} {c.Routes[cr.Route].To} ({c.Routes[cr.Route].Count} legs)");
        }
        foreach (var b in w.Bodies)
            if (b != c)
                Console.WriteLine($"  a body of {b.PartCount} part(s) ({(b.PartCount > 0 ? b.DefOf(0).Name : "")}) at {b.WorldCom.X:0.0}, {b.WorldCom.Y:0.0}, {b.WorldCom.Z:0.0}");
        for (var i = 0; i < c.PartCount; i++)
            if (ConstructLifters.DefOf(c, i) is { } ld && (ld.Bridge || Vector2.Distance(new Vector2(c.Parts[i].Pos.X, c.Parts[i].Pos.Z), new Vector2(c.Parts[oven].Pos.X, c.Parts[oven].Pos.Z)) < 60))
                Console.WriteLine($"  lifter {i} {c.DefOf(i).Name} at {c.Parts[i].Pos.X:0}, {c.Parts[i].Pos.Z:0} (payload {ld.Payload:0} kg, mode {ConstructConfig.Get(c.Parts[i], c.DefOf(i), CfgKey.LiftMode, 0)}): {ConstructLifters.Look(w, c, i)}");
        return made >= 0 ? 0 : 1;
    }
}
