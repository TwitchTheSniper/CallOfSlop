using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private static readonly string[] TierWords = ["", "", "Workshop ", "Industrial ", "Military ", "Plasma "];
    private static readonly float[] TierSizes = [0, 1, 1.25f, 1.5f, 2, 2.5f];

    /// <summary>A machine line's part at a tier ("Drill Module", 3: "Industrial Drill Module").</summary>
    private static string TierName(string line, int tier) => TierWords[tier] + line;

    /// <summary>
    /// The five tiers of the four machine lines as data: tier 1 keeps the old numbers, each step up is faster, holds more,
    /// is tougher, draws more and weighs more, and a unit costs less power; each is physically larger by its size, boxes, bins
    /// and work box together, with its sockets on the frame grid and a bit that reaches no further under the part; bins hold
    /// what their volume holds; the tiers look different, have no coplanar faces and the top one glows; the Tab inventory has them in order. The "minTier" table values, and bad ones refused.
    /// </summary>
    private static void MachineTierData(Action<bool, string> want)
    {
        (string Line, string Section, float Rate, float Power, float Mass)[] lines =
            [("Drill Module", "Drills", 10, 900, 320), ("Saw Module", "Saws", 12, 900, 260), ("Sawmill", "Sawmills", 1, 700, 240), ("Smelter", "Smelters", 1, 1200, 420)];
        float RateOf(ConstructPartDef d) => d.IsGatherer ? d.GatherRate : d.RefineWork / (float)ConstructRefining.BaseWork;
        var cat = SandboxCatalogue.Make();
        foreach (var (line, section, rate, power, mass) in lines)
        {
            var t1 = ConstructParts.Need(line);
            want(RateOf(t1) == rate && t1.PowerDraw == power && t1.Mass == mass && t1.BulkCapacity == 400 && t1.ChuteLoad == ConstructRoutes.ChuteLoad, $"tier 1 of {line} changed ({RateOf(t1)}, {t1.PowerDraw} W, {t1.Mass} kg, {t1.BulkCapacity}, chute {t1.ChuteLoad})");
            var last = t1;
            var looks = new HashSet<string>();
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var d = ConstructParts.Need(TierName(line, tier));
                want(d.Tier == tier && d.Group == "Production" && d.Section == section && d.IsGatherer == t1.IsGatherer && d.IsRefiner == t1.IsRefiner && d.RefineMachine == t1.RefineMachine && d.PhysicsOnly, $"{d.Name}: tier {d.Tier}, {d.Group}/{d.Section}");
                var size = TierSizes[tier];
                bool Near(float a, float b) => MathF.Abs(a - b) < 0.02f;
                want(d.Sockets.Where(s => s.Name is "bottom" or "+x" or "-x" or "back").Select(s => (s.Name, s.Dir)).SequenceEqual(t1.Sockets.Where(s => s.Name is "bottom" or "+x" or "-x" or "back").Select(s => (s.Name, s.Dir))), $"{d.Name}: its sockets aren't tier 1's");
                want(tier == 1 || d.Sockets.All(s => Vector3.Max(Vector3.Abs(s.At * 4 - new Vector3(MathF.Round(s.At.X * 4), MathF.Round(s.At.Y * 4), MathF.Round(s.At.Z * 4))), Vector3.Zero).Length() < 0.001f), $"{d.Name}: a socket off the 0.25 m grid: {string.Join(", ", d.Sockets.Select(s => $"{s.Name} {s.At}"))}");
                want(d.Sockets.First(s => s.Name == "bottom").At == Vector3.Zero, $"{d.Name}'s bottom socket moved");
                var side = d.Sockets.First(s => s.Name == "+x").At.X;
                want(side >= size * 0.5f - 0.001f && d.BoundsMax.X >= size * t1.BoundsMax.X - 0.02f && d.BoundsMin.Z <= size * t1.BoundsMin.Z + 0.02f, $"{d.Name} isn't {size} times as big ({d.BoundsMin} to {d.BoundsMax})");
                want(MathF.Abs(d.BoundsMax.Y - size * t1.BoundsMax.Y) < 0.02f && MathF.Abs(d.BoundsMin.Y - t1.BoundsMin.Y) < 0.001f, $"{d.Name}'s height or reach under the part ({d.BoundsMin.Y} to {d.BoundsMax.Y})");
                var fill = d.FillMax - d.FillMin;
                var fill1 = t1.FillMax - t1.FillMin;
                var room = fill.X * fill.Y * fill.Z / (fill1.X * fill1.Y * fill1.Z);
                want(Near(fill.X / fill1.X, size) && MathF.Abs(d.BulkCapacity / (float)t1.BulkCapacity / room - 1) < 0.15f, $"{d.Name}'s bin: {d.BulkCapacity} in {room:0.00} times tier 1's room");
                if (d.IsGatherer)
                    want(Near(d.WorkMax.Z / t1.WorkMax.Z, size) && Near(d.WorkMin.Y, t1.WorkMin.Y * (d.WorkMin.Y > 0 ? size : 1)), $"{d.Name}'s work box ({d.WorkMin} to {d.WorkMax})");
                else
                    want(Near(d.InFillMax.X / t1.InFillMax.X, size) && d.InCapacity == t1.InCapacity * d.BulkCapacity / t1.BulkCapacity, $"{d.Name}'s input ({d.InCapacity}, {d.InFillMin} to {d.InFillMax})");
                want(ZFight.Report(d.Name, ZFight.Boxes(d)).Count() == 0, $"{d.Name} has coplanar faces: {string.Join("; ", ZFight.Report(d.Name, ZFight.Boxes(d)))}");
                want(d.Name.All(c => c is >= ' ' and <= '~') && d.Description.Contains($"tier {tier} of {ConstructParts.MaxTier}"), $"{d.Name}'s text: \"{d.Description}\"");
                want(looks.Add(string.Join(",", d.Boxes.Select(b => b.Color))), $"{d.Name} looks like a lower tier");
                want(d.Boxes.Any(b => b.Glow) == (tier >= 3), $"{d.Name}'s glow");
                if (tier == 1)
                    continue;
                want(RateOf(d) > RateOf(last) && d.BulkCapacity > last.BulkCapacity && d.Health > last.Health && d.Armour > last.Armour && d.Mass > last.Mass && d.PowerDraw > last.PowerDraw && d.ChuteLoad > last.ChuteLoad,
                    $"{d.Name} isn't a step up from {last.Name} ({RateOf(d)} from {RateOf(last)}, {d.BulkCapacity} from {last.BulkCapacity}, {d.Health} hp, {d.Mass} kg, {d.PowerDraw} W)");
                want(d.PowerDraw / RateOf(d) < last.PowerDraw / RateOf(last), $"{d.Name}: power a unit doesn't fall ({d.PowerDraw / RateOf(d)} from {last.PowerDraw / RateOf(last)})");
                want(!d.IsRefiner || d.InCapacity > last.InCapacity, $"{d.Name}'s input ({d.InCapacity})");
                last = d;
            }
            var sec = cat.Parts.Sections.FirstOrDefault(s => s.Name == section);
            want(sec != null && cat.Parts.Groups[sec.Group].Name == "Production", $"no {section} section in Production");
            want(sec != null && sec.Items.Select(i => ConstructParts.All[i].Tier).SequenceEqual([1, 2, 3, 4, 5]), $"the {section} section isn't in tier order: {(sec == null ? "" : string.Join(", ", sec.Items.Select(i => ConstructParts.All[i].Name)))}");
        }
        // The file's tier and the gate's values.
        var drillFile = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Gathering", "DrillModuleT3.json"));
        foreach (var (from, to, why) in ((string, string, string)[])[("\"tier\": 3", "\"tier\": 6", "tier"), ("\"tier\": 3", "\"tier\": 0", "tier"), ("\"chuteLoad\": 40", "\"chuteLoad\": 5", "chuteLoad")])
            try
            {
                ConstructParts.Parse(drillFile.Replace(from, to), "bad.json");
                want(false, $"a part with {to} wasn't refused");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(why), $"a part with {to}: \"{ex.Message}\"");
            }
        var table = ResourceTable.Current;
        foreach (var (kind, min) in ((string, int)[])[("forest", 1), ("iron vein", 1), ("stone seam", 1), ("sand bed", 1), ("copper vein", 2), ("gold vein", 3)])
            want(table.IndexOf(kind) >= 0 && table.Kinds[table.IndexOf(kind)].MinTier == min, $"{kind}'s minTier isn't {min}");
        var head = "{\"format\":\"" + ResourceTable.Format + "\",\"kinds\":[{\"name\":\"x\",\"material\":\"gold ore\",\"layer\":\"underground\",\"cell\":99,\"chance\":1,\"radius\":[8,9],\"aspect\":[1,1],\"units\":5";
        want(ResourceTable.Parse(head + ",\"minTier\":4}]}") is { Problem.Length: 0 } good && good.Kinds[0].MinTier == 4, "a good minTier wasn't read");
        want(ResourceTable.Parse(head + ",\"minTier\":6}]}").Problem.Contains("minTier"), "a minTier of 6 wasn't refused");
        var recipe = "{\"format\": \"call-of-slop-refining/1\", \"recipes\": [{\"machine\": \"smelter\", \"input\": \"sand\", \"unitsIn\": 2, \"output\": \"glass\", \"unitsOut\": 1, \"seconds\": 1";
        want(RefiningTable.Parse(recipe + ", \"minTier\": 3}]}") is { Problem.Length: 0, Recipes: [{ MinTier: 3 }] }, "a recipe's minTier wasn't read");
        want(RefiningTable.Parse(recipe + ", \"minTier\": 9}]}").Problem.Contains("minTier"), "a recipe's minTier of 9 wasn't refused");
        var refining = RefiningTable.Current;
        var names = RecyclingTable.Current;
        want(refining.Find("smelter", names.IdOf("iron ore"))?.MinTier == 1 && refining.Find("smelter", names.IdOf("sand"))?.MinTier == 1 && refining.Find("sawmill", names.IdOf("wood"))?.MinTier == 1
            && refining.Find("smelter", names.IdOf("copper ore"))?.MinTier == 2 && refining.Find("smelter", names.IdOf("gold ore"))?.MinTier == 3, "the recipes' minTier");
        want(refining.Takes("smelter", 1) == "iron ore, sand" && refining.Takes("smelter", 2) == "iron ore, copper ore, sand" && refining.Takes("smelter", 3) == "iron ore, copper ore, gold ore, sand" && refining.Takes("smelter") == refining.Takes("smelter", 5), $"what a tier takes: \"{refining.Takes("smelter", 1)}\"");
    }

    /// <summary>
    /// Every tier of Drill and Saw Module through the real step, in survival on a battery, at its own rate and drawing its
    /// power; the gate: a drill below a deposit's minTier takes nothing and the look line says why and still points to one
    /// it can work; a fast module never takes more than its bin has room for.
    /// </summary>
    private static void GatherTiers(Map yard, Action<bool, string> want)
    {
        var keep = yard.Sandbox!.Resources;
        var keepTable = ResourceTable.Current;
        yard.Sandbox.Resources = new SandboxResources
        {
            Generate = false,
            Deposits = [new SandboxDeposit("iron vein", -20, -20, 7, 1, 0), new SandboxDeposit("copper vein", 20, -20, 7, 1, 0), new SandboxDeposit("gold vein", 20, 20, 7, 1, 0), new SandboxDeposit("forest", -20, 20, 22, 1, 0)],
        };
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            var f = yard.Resources!;
            var w = new ConstructWorld { Resources = f, CreativeRecycling = false };
            var q = World.Query;
            void Run(float seconds)
            {
                for (var t = 0; t < seconds * World.TickRate; t++)
                    w.Step(q, World.TickDt);
            }
            var names = RecyclingTable.Current;
            var ore = names.IdOf("iron ore");
            Vector3 On(float x, float z) => new(x, f.Ground(x, z) - 0.3f, z);
            var trees = Trunks(f, yard);
            var report = "";
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var drillName = TierName("Drill Module", tier);
                if (ModRig(w, drillName, On(-20 + tier, -20), out _) is not { } rig)
                {
                    want(false, $"no rig for the {drillName}");
                    continue;
                }
                var st = w.StoreOf(rig, 0)!;
                var d = rig.DefOf(0);
                const float span = 1.5f;
                var left = f.LeftAt(-20 + tier, -20);
                Run(span);
                var low = d.GatherRate * (span - 0.15f);
                var high = d.GatherRate * (span + 0.05f);
                want(st.Busy && st.Material == ore && st.Amount >= low && st.Amount <= high, $"the {drillName} made {st.Amount} in {span} s at {d.GatherRate}/s (busy {st.Busy}, {names.NameOf(st.Material)})");
                want(left - f.LeftAt(-20 + tier, -20) == st.Amount, $"the {drillName}: the deposit lost {left - f.LeftAt(-20 + tier, -20)}, the bin holds {st.Amount}");
                want(rig.PowerUse >= d.PowerDraw, $"the {drillName} draws {rig.PowerUse} W, want at least {d.PowerDraw}");
                // A step never takes more than the bin has room for.
                st.Amount = d.BulkCapacity - 1;
                left = f.LeftAt(-20 + tier, -20);
                Run(0.5f);
                want(st.Amount == d.BulkCapacity && left - f.LeftAt(-20 + tier, -20) == 1 && !st.Busy, $"the {drillName} next to full: {st.Amount}/{d.BulkCapacity}, took {left - f.LeftAt(-20 + tier, -20)}");
                w.Remove(rig);

                var sawName = TierName("Saw Module", tier);
                var tree = trees[tier * 3];
                if (ModRig(w, sawName, new Vector3(tree.T.X, f.Ground(tree.T.X, tree.T.Z) + 1.5f, tree.T.Z), out _) is not { } mill)
                {
                    want(false, $"no rig for the {sawName}");
                    continue;
                }
                var sawStore = w.StoreOf(mill, 0)!;
                var sd = mill.DefOf(0);
                var whole = ResourceTable.Current.Kinds.Find(k => k.Surface)!.Units;
                var ticks = 0;
                for (; ticks < 700 && !f.IsFelled(tree.Key); ticks++)
                    w.Step(q, World.TickDt);
                var seconds = ticks / (float)World.TickRate;
                var fell = whole / sd.GatherRate;
                want(f.IsFelled(tree.Key) && sawStore.Amount == whole && seconds >= fell - 0.1f && seconds <= fell + 0.25f, $"the {sawName} felled a {whole} wood tree in {seconds:0.00} s, want {fell:0.00} (bin {sawStore.Amount})");
                report += $"{d.GatherRate}+{sd.GatherRate}/s ";
                w.Remove(mill);
            }
            Line($"machine tiers: drills+saws at {report}");

            // ---- The gate: copper asks for tier 2, gold for tier 3, on the server and on the look line.
            w.CreativeRecycling = true; // (no battery needed here)
            void Gate(string drill, float x, float z, bool works, string material, string? says)
            {
                var c = GatherBody(w, drill, On(x, z));
                var st = w.StoreOf(c, 0)!;
                for (var t = 0; t < World.TickRate; t++)
                    ConstructGatherer.Step(w);
                st.Version++;
                var look = Program.GathererLook(c.DefOf(0), st, new ConstructHit { Body = c, Part = 0 });
                if (works)
                    want(st.Busy && st.Amount > 0 && st.Material == names.IdOf(material) && look.Contains("DRILLING"), $"the {drill} over {material}: {st.Amount}, \"{look}\"");
                else
                {
                    want(!st.Busy && st.Amount == 0, $"the {drill} worked over {material} ({st.Amount})");
                    want(look.Contains(says!) && look.All(ch => ch is >= ' ' and <= '~'), $"the {drill} over {material} says \"{look}\", want \"{says}\"");
                    want(look.Contains("nearest deposit: iron vein") || drill != "Drill Module", $"the hint should point to what it can work: \"{look}\"");
                }
                w.Remove(c);
            }
            Gate("Drill Module", 20, -20, false, "copper ore", "copper vein needs a tier 2 drill, this is tier 1");
            Gate("Workshop Drill Module", 21, -20, true, "copper ore", null);
            Gate("Drill Module", 20, 20, false, "gold ore", "gold vein needs a tier 3 drill, this is tier 1");
            Gate("Workshop Drill Module", 20, 20, false, "gold ore", "gold vein needs a tier 3 drill, this is tier 2");
            Gate("Industrial Drill Module", 21, 20, true, "gold ore", null);
            Gate("Plasma Drill Module", 19, 20, true, "gold ore", null);
            want(f.NearestUnderground(19, -19, 1).Contains("iron vein") && f.NearestUnderground(19, -19, 2).Contains("copper vein"), $"the hint by tier: \"{f.NearestUnderground(19, -19, 1)}\"; \"{f.NearestUnderground(19, -19, 2)}\"");

            // Trees can ask for a tier too.
            ResourceTable.Use(ResourceTable.Parse(GatedForest));
            var tall = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("forest", -20, 20, 22, 1, 0)] };
            var gated = new ResourceField(yard, ResourceTable.Current, tall);
            var (tx, tz) = (trees[0].T.X, trees[0].T.Z);
            var ow = new ConstructWorld { Resources = gated, CreativeRecycling = true };
            var low2 = GatherBody(ow, "Workshop Saw Module", new Vector3(tx, gated.Ground(tx, tz) + 1.5f, tz));
            for (var t = 0; t < World.TickRate; t++)
                ConstructGatherer.Step(ow);
            want(ow.StoreOf(low2, 0)!.Amount == 0, "a saw below the forest's minTier cut it");
            ow.Remove(low2);
            var high2 = GatherBody(ow, "Industrial Saw Module", new Vector3(tx, gated.Ground(tx, tz) + 1.5f, tz));
            for (var t = 0; t < World.TickRate; t++)
                ConstructGatherer.Step(ow);
            want(ow.StoreOf(high2, 0)!.Amount > 0, "a saw at the forest's minTier didn't cut it");
        }
        finally
        {
            ResourceTable.Use(keepTable);
            yard.Sandbox.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
        }
    }

    private const string GatedForest = "{\"format\":\"" + ResourceTable.Format + "\",\"kinds\":[{\"name\":\"forest\",\"material\":\"wood\",\"layer\":\"surface\",\"minTier\":3,\"cell\":240,\"chance\":0.75,\"radius\":[55,105],\"aspect\":[0.6,1.0],\"units\":60}]}";

    /// <summary>A Smelting Station with a machine of a line and tier in place of the Smelter: on two foundations, since a tier 5 is wider than one.</summary>
    private static Construct? TierStation(ConstructWorld w, string machine, Vector3 spot, Action<bool, string> want)
    {
        var bp = ConstructBuild.ParseBlueprint($$"""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Tier Station", "steps": [
              { "part": "Foundation" },
              { "part": "Foundation", "on": 0, "socket": "x+1", "by": "x-1" },
              { "part": "{{machine}}", "on": 0, "socket": "top11", "by": "bottom" },
              { "part": "Materials Bin", "on": 1, "socket": "top10", "by": "bottom" },
              { "part": "Materials Bin", "on": 1, "socket": "top13", "by": "bottom" },
              { "part": "Battery", "on": 1, "socket": "top30", "by": "-y" }
            ], "links": [ { "type": "chute", "from": 3, "to": 2 }, { "type": "chute", "from": 2, "to": 4 } ] }
            """, "tier-station.json");
        var c = ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, spot.X, spot.Z), 0, out var why);
        want(c != null, $"a station with the {machine}: {why}");
        if (c != null)
            c.Owner = 255;
        return c;
    }

    /// <summary>
    /// Every tier of Smelter and Sawmill through a real station in survival: a chute feeds it, it makes batches at its speed
    /// and draws its power, chutes carry a higher tier's bigger load; a step allocates nothing, even finishing several
    /// batches. The gate: a Smelter below a recipe's minTier refuses a chute and F with the reason on the look line, and a
    /// machine holding what its tier can't work is blocked. Tier 3 does gold.
    /// </summary>
    private static void RefiningTiers(Map yard, Action<bool, string> want)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var names = RecyclingTable.Current;
        var table = RefiningTable.Current;
        var w = new ConstructWorld { CreativeRecycling = false };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (OpenSpot(w) is not { } spot)
        {
            want(false, "no open spot for the tier stations");
            return;
        }
        byte ore = names.IdOf("iron ore"), metal = names.IdOf("metal"), wood = names.IdOf("wood"), planks = names.IdOf("planks"), copper = names.IdOf("copper ore"), goldOre = names.IdOf("gold ore"), gold = names.IdOf("gold");
        var report = "";
        TierShapes(yard, want);
        TierOnVehicle(want);
        yard.RestoreProps();
        World.SetMap(yard);
        foreach (var (line, input, output, machine) in ((string, byte, byte, string)[])[("Smelter", ore, metal, "smelter"), ("Sawmill", wood, planks, "sawmill")])
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var name = TierName(line, tier);
                if (TierStation(w, name, spot, want) is not { } c)
                    continue;
                var si = PartNamed(c, name);
                var binIn = PartNamed(c, "Materials Bin");
                var binOut = PartNamed(c, "Materials Bin", 1);
                CStore st = w.StoreOf(c, si)!, sIn = w.StoreOf(c, binIn)!, sOut = w.StoreOf(c, binOut)!;
                var d = c.DefOf(si);
                var recipe = table.Find(machine, input)!;
                // The chute's load follows the machine at its end.
                w.Deposit(c, binIn, Math.Min(ConstructParts.Need("Materials Bin").BulkCapacity, d.ChuteLoad * 2), input);
                var moved = sIn.Amount;
                for (var t = 0; t < 1.1f * World.TickRate && sIn.Amount == moved; t++)
                    w.Step(q, World.TickDt);
                want(moved - sIn.Amount == Math.Min(d.ChuteLoad, moved), $"a chute into the {name} moved {moved - sIn.Amount}, want {d.ChuteLoad}");
                // The rate: the input full, the output kept clear by its chute.
                w.Feed(c, si, d.InCapacity, input);
                const float span = 2f;
                Run(0.1f);
                var from = st.Amount + sOut.Amount;
                Run(span);
                var perSecond = (st.Amount + sOut.Amount - from) / span;
                var wanted = recipe.UnitsOut / recipe.Seconds * (d.RefineWork / (float)ConstructRefining.BaseWork);
                want(st.Busy && st.InMaterial == input && MathF.Abs(perSecond - wanted) <= MathF.Max(1f, wanted * 0.06f), $"the {name} made {perSecond:0.0}/s, want {wanted:0.0}/s (busy {st.Busy}, {st.InAmount}/{d.InCapacity} in, {st.Amount} out)");
                want(c.PowerUse >= d.PowerDraw, $"the {name} draws {c.PowerUse} W, want {d.PowerDraw}");
                want(st.Material == output || st.Amount == 0, $"the {name} made {names.NameOf(st.Material)}");
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var s = 0; s < 60; s++)
                    ConstructRefining.Step(w);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                want(bytes == 0, $"the {name}'s step allocates ({bytes} B)");
                report += $"{perSecond:0.#}/s ";
                w.Remove(c);
            }
        Line($"machine tiers: refining at {report}");

        // ---- The gate.
        var low = TierStation(w, "Smelter", spot, want);
        if (low == null)
            return;
        var ls = PartNamed(low, "Smelter");
        var lowIn = PartNamed(low, "Materials Bin");
        CStore ss = w.StoreOf(low, ls)!, src = w.StoreOf(low, lowIn)!;
        var ld = low.DefOf(ls);
        var inv = new SandboxInventory();
        string Look() => ConstructRefining.Look(ld, ss, false, inv);
        want(Look().Contains("takes iron ore, sand (copper ore needs tier 2, gold ore needs tier 3)"), $"an empty tier 1 Smelter says \"{Look()}\"");
        w.Deposit(low, lowIn, 20, copper);
        Run(1.5f);
        want(ss.InAmount == 0 && src.Amount == 20 && ss.Refused == copper, $"copper ore into a tier 1 Smelter (input {ss.InAmount}, bin {src.Amount}, refused {ss.Refused})");
        want(Look().Contains("chute refused: copper ore stays in the source, it needs a tier 2 smelter, this is tier 1"), $"the look line: \"{Look()}\"");
        want(w.Feed(low, ls, 20, goldOre) == 0, "gold ore fed into a tier 1 Smelter");
        inv.AddMaterial(goldOre, 8);
        want(ConstructRefining.TipMaterial(inv, ld, ss) == 0, "F would tip gold ore into a tier 1 Smelter");
        inv.AddMaterial(ore, 8);
        want(ConstructRefining.TipMaterial(inv, ld, ss) == ore, "F should tip the iron ore past the gold ore");
        ss.InMaterial = goldOre;
        ss.InAmount = 8;
        Run(0.3f);
        want(ConstructRefining.StateOf(table, ld, ss, out _) == RefineState.TierLow && !ss.Busy && ss.InAmount == 8 && ss.Amount == 0, $"gold ore in a tier 1 Smelter's input ({ss.InAmount}, busy {ss.Busy})");
        want(Look().Contains("blocked: gold ore needs a tier 3 smelter, this is tier 1"), $"the look line: \"{Look()}\"");
        w.Remove(low);
        void Works(string name, byte input, byte output, bool ok)
        {
            if (TierStation(w, name, spot, want) is not { } c)
                return;
            var si = PartNamed(c, name);
            var st = w.StoreOf(c, si)!;
            var fed = w.Feed(c, si, 40, input);
            want((fed > 0) == ok, $"the {name} and {names.NameOf(input)}: fed {fed}");
            if (ok)
            {
                Run(2);
                var bin = w.StoreOf(c, PartNamed(c, "Materials Bin", 1))!; // (its chute takes the output on)
                want((st.Material == output || bin.Material == output) && st.Amount + bin.Amount > 0 && st.InAmount < 40, $"the {name} didn't make {names.NameOf(output)} ({st.Amount} {names.NameOf(st.Material)}, bin {bin.Amount})");
            }
            w.Remove(c);
        }
        Works("Workshop Smelter", copper, names.IdOf("copper"), true);
        Works("Workshop Smelter", goldOre, gold, false);
        Works("Industrial Smelter", goldOre, gold, true);
        Works("Plasma Smelter", goldOre, gold, true);
    }

    /// <summary>"Gathering network": the gate needs nothing new on the wire: a client works out the drill's refusal from the same table, and hears a higher tier work.</summary>
    private static void GatherTierNet(Game server, Game a, ResourceField f, ref double now, System.Diagnostics.Stopwatch clock)
    {
        var w = server.Constructs;
        Vector3 On(float x, float z) => new(x, f.Ground(x, z) - 0.3f, z);
        var refused = GatherBody(w, "Drill Module", On(TierDepositX, 0));
        var works = GatherBody(w, "Workshop Drill Module", On(TierDepositX + 1, 0));
        Pump(server, [a], 1.0, ref now, clock);
        var hs = w.StoreOf(refused, 0)!;
        CStore? Client(Construct c) => a.Constructs.Get(c.Id) is { } cc ? a.Constructs.StoreOf(cc, 0) : null;
        var look = Client(refused) is { } cs && a.Constructs.Get(refused.Id) is { } copy ? Program.GathererLook(copy.DefOf(0), cs, new ConstructHit { Body = copy, Part = 0 }) : "(no copy)";
        if (hs.Amount != 0 || hs.Busy || Client(refused) is not { Amount: 0, Busy: false } || !look.Contains("copper vein needs a tier 2 drill, this is tier 1"))
            Fail($"gathering network: a tier 1 drill on copper: host {hs.Amount}, client {Client(refused)?.Amount}, \"{look}\"");
        var names = RecyclingTable.Current;
        if (Client(works) is not { Busy: true, Amount: > 0 } ws || ws.Material != names.IdOf("copper ore"))
            Fail($"gathering network: the client didn't hear the tier 2 drill on copper ({Client(works)?.Amount}, busy {Client(works)?.Busy})");
        w.Remove(refused);
        w.Remove(works);
        Pump(server, [a], 0.2, ref now, clock);
    }

    private const float TierDepositX = 34;

    /// <summary>
    /// Bigger means bigger everywhere (the merged receivers' Lesson): at every tier the collider, a ray, the outline, a
    /// player and a ghost all meet the grown shape, out where tier 1's would not reach. Then the real attaching path for
    /// parts too heavy to carry, the welder's socket weld: every tier, on a loose body, its bottom socket onto a Foundation,
    /// and a Frame Block welded onto its side socket, landing on the 0.25 m grid.
    /// </summary>
    private static void TierShapes(Map yard, Action<bool, string> want)
    {
        yard.RestoreProps();
        World.SetMap(yard);
        var w = new ConstructWorld();
        foreach (var line in (string[])["Drill Module", "Saw Module", "Sawmill", "Smelter"])
            for (var tier = 2; tier <= ConstructParts.MaxTier; tier++)
            {
                var name = TierName(line, tier);
                var d = ConstructParts.Need(name);
                var t1 = ConstructParts.Need(line);
                // A pinned body at the origin of a frame of its own, high enough to keep clear of the ground.
                if (SwBody(w, name, new Vector3(0, 12, 0), Quaternion.Identity, pinned: true) is not { } c)
                    continue;
                var solids = c.SolidCount(0);
                c.ShapeBounds(0, out var omn, out var omx);
                want(solids == d.SolidBoxes && Vector3.DistanceSquared(omn, d.BoundsMin) < 1e-6f && Vector3.DistanceSquared(omx, d.BoundsMax) < 1e-6f, $"{name}: the outline {omn} to {omx}, bounds {d.BoundsMin} to {d.BoundsMax}, {solids} solid boxes");
                // The side wall, between tier 1's side and this one's, seen along x: it's solid out there.
                var wall = d.Boxes[..d.SolidBoxes].Where(b => b.Half.Y > 0.05f && b.Center.X + b.Half.X > t1.BoundsMax.X + 0.05f).MaxBy(b => b.Center.X + b.Half.X);
                var hitAt = new Vector3(0, wall.Center.Y, wall.Center.Z);
                var from = c.ToWorld(hitAt with { X = wall.Center.X + wall.Half.X + 0.6f });
                var hit = w.Raycast(from, -Vector3.UnitX, 2f, out var h, null) && h.Body == c;
                want(hit && MathF.Abs(h.Distance - 0.6f) < 0.02f, $"{name}: a ray at its wall hit {(hit ? h.Distance.ToString("0.000") : "nothing")}, want 0.6 m");
                var ms = new MoveState { Position = c.ToWorld(new Vector3(wall.Center.X + wall.Half.X - 0.04f, wall.Center.Y - wall.Half.Y + 0.02f, wall.Center.Z)), Grounded = false };
                w.BlockPlayer(ref ms, World.PlayerHeight, server: true);
                var rest = ms.Position - c.Pos;
                want(rest.X > wall.Center.X + wall.Half.X + World.PlayerRadius - 0.05f || rest.Y >= wall.Center.Y + wall.Half.Y - 0.02f, $"{name}: a player in its wall stayed at {rest}");
                want(ConstructBuild.Blocked(c, ConstructParts.Need("Frame Block"), new Vector3(wall.Center.X, wall.Center.Y, wall.Center.Z), 0), $"{name}: a ghost inside its wall isn't blocked");
                w.Remove(c);
            }

        var hands = FwHands();
        foreach (var line in (string[])["Drill Module", "Saw Module", "Sawmill", "Smelter"])
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var name = TierName(line, tier);
                if (SwBody(w, "Foundation", new Vector3(0, 12, 0), Quaternion.Identity, pinned: true) is not { } baseC
                    || SwBody(w, name, new Vector3(6, 14, 0), Quaternion.Identity, pinned: false) is not { } loose)
                    continue;
                var d = loose.DefOf(0);
                var flow = new SocketWeldFlow();
                var target = new WeldTarget { Body = baseC.Id, Part = 0, Socket = (byte)SwSocket(baseC.DefOf(0), d.IsGatherer ? "top13" : "top11") }; // (a bit hangs over the edge)
                flow.PickSource(loose.Id, 0, SwSocket(d, "bottom"));
                flow.PickTarget(baseC, target, loose, null);
                for (var turn = 0; turn < 3 && SocketWeld.Check(w, null, loose, 0, SwSocket(d, "bottom"), baseC, target, flow.Angle, ReadOnlySpan<Vector3>.Empty) != null; turn++)
                    flow.NextVariant(loose, baseC);
                var why = hands.BeginSocketWeld(w, 0, new Vector3(3.5f, 13.5f, 1), loose.Id, 0, SwSocket(d, "bottom"), target, flow.Angle, 0);
                if (baseC.Dirty)
                    baseC.Rebuild();
                var ok = why == null && w.Get(loose.Id) == null && baseC.PartCount == 2 && baseC.FreeformCount == 0;
                want(ok, $"a socket weld of the {name} onto a Foundation: {why ?? "not one body on the grid"} ({baseC.PartCount} parts, {baseC.FreeformCount} freeform)");
                if (ok)
                {
                    want(w.StoreOf(baseC, 1) != null, $"the welded {name} has no store");
                    var at = Array.FindIndex(d.Sockets, s => s.Name == "+x");
                    var sp = ConstructBuild.Place(baseC, 1, at, ConstructParts.Need("Frame Block"), SwSocket(ConstructParts.Need("Frame Block"), "-x"), 0, out var g);
                    want(sp == null, $"a Frame Block on the {name}'s +x socket: {sp}");
                    if (sp == null)
                    {
                        var rel = g.Pos - baseC.Parts[1].Pos;
                        want(MathF.Abs(rel.X * 4 - MathF.Round(rel.X * 4)) < 0.01f && MathF.Abs(rel.Y * 4 - MathF.Round(rel.Y * 4)) < 0.01f && MathF.Abs(rel.Z * 4 - MathF.Round(rel.Z * 4)) < 0.01f || tier == 1, $"the Frame Block on the {name} lands off the grid ({rel})");
                    }
                }
                w.Remove(baseC);
                if (w.Get(loose.Id) is { } left)
                    w.Remove(left);
            }
    }

    /// <summary>
    /// Tier 5 modules on a vehicle, driven: a Plasma Drill Module and a Plasma Saw Module on a 4x4 Truck, fitted through the
    /// building rules where a bit reaches the ground. It still rolls and steers its load away, and parked over a vein the bit
    /// is at the ground and the drill works on the truck's own battery, in survival; brought to a forest, the saw cuts.
    /// </summary>
    private static void TierOnVehicle(Action<bool, string> want)
    {
        if (ConstructBuild.FindBlueprint("4x4 Truck") is not { } truck || Maps.Find(GatherMap) is not { } yard || yard.Sandbox == null)
        {
            want(false, "no 4x4 Truck blueprint, or no map to drive it on");
            return;
        }
        var keep = yard.Sandbox.Resources;
        yard.RestoreProps();
        World.SetMap(yard);
        if (FlatLane(yard) is not { } lane)
        {
            want(false, $"no flat lane on {yard.Name}");
            return;
        }
        var rules = new SandboxResources { Generate = false, Deposits = [new SandboxDeposit("iron vein", lane.X, lane.Z, 45, 1, 0)] };
        yard.Sandbox.Resources = rules;
        yard.DropResources();
        yard.RestoreProps();
        World.SetMap(yard);
        try
        {
            var f = yard.Resources!;
            var w = new ConstructWorld { Resources = f, CreativeRecycling = false };
            var q = World.Query;
            if (ConstructBuild.BuildOnGround(w, truck, ConstructWorld.Ground(q, lane.X, lane.Z), 0, out var why) is not { } car)
            {
                want(false, $"no room for the truck: {why}");
                return;
            }
            DriveSteps(w, q, 90);
            // Fits a module where its lowest point comes nearest the wheels' (a drill), or anywhere (a saw); upright.
            int Mount(ConstructPartDef def, bool low)
            {
                var bottom = Array.FindIndex(def.Sockets, s => s.Name == "bottom");
                var (part, socket, turn, best) = (-1, -1, 0, float.MaxValue);
                for (var i = 0; i < car.PartCount; i++)
                    for (var s = 0; s < car.DefOf(i).Sockets.Length; s++)
                        for (var spin = 0; spin < 4; spin++)
                        {
                            if (ConstructBuild.Place(car, i, s, def, bottom, spin, out var g) != null || Rot24.Apply(g.Rot, Vector3.UnitY).Y < 0.9f || car.Parts[i].Seg != 0)
                                continue;
                            var tip = g.Pos.Y + def.BoundsMin.Y - car.LocalMin.Y;
                            var score = low ? MathF.Abs(tip - 0.1f) : -tip;
                            if (tip > 0.04f && score < best)
                                (part, socket, turn, best) = (i, s, spin, score);
                        }
                if (part < 0)
                    return -1;
                ConstructBuild.Place(car, part, socket, def, bottom, turn, out var ghost);
                var at = ConstructBuild.Attach(w, car, def, ghost);
                car.Rebuild();
                return at;
            }
            var drillDef = ConstructParts.Need("Plasma Drill Module");
            var sawDef = ConstructParts.Need("Plasma Saw Module");
            int drill = Mount(drillDef, true), saw = Mount(sawDef, false);
            if (drill < 0 || saw < 0)
            {
                want(false, $"tier 5 modules don't fit a truck (drill {drill}, saw {saw})");
                return;
            }
            CStore ds = w.StoreOf(car, drill)!, ss = w.StoreOf(car, saw)!;
            want(car.Parts[drill].Powered && car.Parts[saw].Powered, $"the truck's battery doesn't reach them ({car.Parts[drill].Powered}, {car.Parts[saw].Powered})");
            var start = car.WorldCom;
            Seat(car);
            for (var t = 0; t < 4 * World.TickRate; t++)
            {
                DriveKeys(car, Buttons.Forward);
                w.Step(q, World.TickDt);
            }
            var moved = Vector3.Distance(start with { Y = 0 }, car.WorldCom with { Y = 0 });
            car.Driver = 255;
            for (var t = 0; t < 6 * World.TickRate && car.Vel.Length() > StopSpeed; t++)
            {
                DriveKeys(car, Buttons.Back);
                w.Step(q, World.TickDt);
            }
            for (var t = 0; t < World.TickRate && car.Vel.Length() > StopSpeed; t++)
                w.Step(q, World.TickDt);
            ConstructGatherer.WorkBox(car, drill, out var bit, out var tipY, out _);
            var gap = tipY - f.Ground(bit.X, bit.Z);
            var before = ds.Amount;
            var charge = car.Charge;
            for (var t = 0; t < 2 * World.TickRate; t++)
                w.Step(q, World.TickDt);
            ConstructGatherer.WorkBox(car, drill, out bit, out tipY, out _);
            var worked = ds.Amount - before;
            Line($"machine tiers: tier 5 modules on a {car.Mass:0} kg truck: {moved:0.0} m in 4 s, bit {gap:0.00} m over the ground, {worked} ore in 2 s, {charge - car.Charge:0} J");
            want(moved > 8, $"the truck with tier 5 modules only went {moved:0.0} m");
            want(gap <= ConstructGatherer.GroundSlack && worked >= 2 * drillDef.GatherRate * 0.9f && ds.Material == RecyclingTable.Current.IdOf("iron ore"), $"the mounted drill: bit {gap:0.00} m over the ground, {worked} in 2 s (busy {ds.Busy}, charge {car.Charge})");

            // Brought to a forest (made where it stands: the yard has no room to drive to one).
            ConstructGatherer.WorkBox(car, saw, out var blade, out _, out var reach);
            rules.Deposits = [new SandboxDeposit("forest", blade.X, blade.Z, 18, 1, 0)];
            var forest = new ResourceField(yard, ResourceTable.Current, rules);
            want(forest.NearestTree(blade, reach, out var tree), $"no tree in the saw's reach ({reach:0.0} m)");
            w.Resources = forest;
            for (var t = 0; t < 0.6f * World.TickRate; t++)
                ConstructGatherer.Step(w);
            want(ss.Amount > 0 || forest.IsFelled(tree), $"the mounted saw cut nothing (reach {reach:0.0} m, busy {ss.Busy})");
        }
        finally
        {
            yard.Sandbox.Resources = keep;
            yard.DropResources();
            yard.RestoreProps();
        }
    }

    private const float StopSpeed = 0.05f;

    /// <summary>A stretch of flat, open ground to drive on, 60 m long and 10 wide, or null.</summary>
    private static Vector3? FlatLane(Map map)
    {
        var q = World.Query;
        for (var z = -120f; z <= 120f; z += 16)
            for (var x = -120f; x <= 120f; x += 16)
            {
                var y = ConstructWorld.Ground(q, x, z).Y;
                var flat = true;
                for (var dz = -30f; dz <= 30f && flat; dz += 3)
                    for (var dx = -5f; dx <= 5f && flat; dx += 2.5f)
                        flat = MathF.Abs(ConstructWorld.Ground(q, x + dx, z + dz).Y - y) < 0.15f;
                if (flat)
                    return new Vector3(x, y, z);
            }
        return null;
    }
}
