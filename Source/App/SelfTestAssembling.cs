using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string AsmHead = "{\"format\": \"call-of-slop-assembling/1\", \"recipes\": [";

    /// <summary>The shipped table, what every recipe rests on, and every kind of bad file refused with a reason.</summary>
    private static void AssemblingTableChecks(Action<bool, string> want)
    {
        var t = AssemblingTable.Current;
        var names = RecyclingTable.Current;
        want(t.Problem.Length == 0 && t.Recipes.Length >= 450, $"the shipped table (\"{t.Problem}\", {t.Recipes.Length} recipes)");
        want(ConstructParts.Refused.Count == 0, $"refused parts: {string.Join("; ", ConstructParts.Refused.Take(3))}");
        foreach (var name in (string[])["Assembler", "Workshop Assembler", "Industrial Assembler", "Military Assembler", "Plasma Assembler"])
        {
            var d = ConstructParts.Need(name);
            want(d.IsAssembler && d.PhysicsOnly && d.Role == PartRole.Storage && d.PowerDraw > 0 && d.MaxInputs == 1 && d.DataIn is [ConstructGatherer.RunPort] && d.Slots.Length > 0, $"the {name} part ({d.IsAssembler}, {d.PowerDraw} W)");
            want(d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options == t.Choices, $"the {name}'s recipe choice isn't the table");
        }
        // Every input can be had: each material comes from a gatherer, a table or a recipe, each part input is an item with a recipe of its own.
        var raw = new HashSet<string>(ResourceTable.Current.Kinds.Select(k => k.Material)) { "wood" };
        foreach (var r in RefiningTable.Current.Recipes)
            raw.Add(r.OutputName);
        foreach (var r in t.Recipes)
        {
            foreach (var (m, _) in r.Materials)
                want(raw.Contains(names.NameOf(m)), $"{r.Name}: nothing makes {names.NameOf(m)}");
            foreach (var (p, _) in r.Parts)
                want(!p.PhysicsOnly && t.Recipes.Any(o => o.Output == p), $"{r.Name}: {p.Name} can't be fed or built");
            want(r.Batch.All(c => c is >= ' ' and <= '~'), $"{r.Name}'s text");
            if (r.Output.Tier == 1 && (r.Output.IsRefiner || r.Output.IsAssembler || r.Output.IsGatherer))
                want(r.MinTier == 1, $"{r.Name} is a tier 1 machine that a tier 1 Assembler can't make (minTier {r.MinTier}): survival couldn't start");
        }
        var engine = t.Recipes.First(r => r.Name == "Engine");
        want(engine.Materials.Length >= 1 && engine.Parts.Length >= 2, $"an Engine draws on several chains ({engine.Batch})");
        want(t.Recipes.Count(r => r.Materials.Length + r.Parts.Length >= 2) > t.Recipes.Length * 0.8f, "most recipes draw on two inputs at least");
        const string good = "{\"output\": \"Gear Set\", \"materials\": {\"steel\": 5}, \"seconds\": 4}";
        var own = AssemblingTable.Parse(AsmHead + good + "]}");
        want(own.Problem.Length == 0 && own.Recipes is [{ Name: "Gear Set", Count: 1, MinTier: 1 }] && own.Choices is [AssemblingTable.NoRecipe, "Gear Set"], $"a good file (\"{own.Problem}\")");
        (string Json, string Want)[] bad =
        [
            ("{", "depth"), ("[]", "format"), ("{\"format\": \"nope\"}", "format"), ("{\"format\": \"call-of-slop-assembling/1\"}", "recipes"), (AsmHead + "]}", "recipes"),
            (AsmHead + good + "], \"extra\": 1}", "extra"), (AsmHead + "3]}", "recipe 1"),
            (AsmHead + good.Replace("\"seconds\": 4", "\"seconds\": 4, \"fuel\": 2") + "]}", "fuel"),
            (AsmHead + good.Replace("Gear Set", "Jam") + "]}", "Jam"), (AsmHead + "{\"seconds\": 4}]}", "output"),
            (AsmHead + good.Replace("steel", "jam") + "]}", "jam"), (AsmHead + good.Replace("\"steel\": 5", "\"steel\": 0") + "]}", "steel"),
            (AsmHead + good.Replace("\"steel\": 5", "\"steel\": 2.5") + "]}", "steel"), (AsmHead + good.Replace("\"seconds\": 4", "\"seconds\": 0") + "]}", "seconds"),
            (AsmHead + good + ", " + good + "]}", "already has a recipe"),
            (AsmHead + "{\"output\": \"Gear Set\", \"seconds\": 4}]}", "takes something"),
            (AsmHead + "{\"output\": \"Gear Set\", \"parts\": {\"Gear Set\": 1}, \"seconds\": 4}]}", "from itself"),
            (AsmHead + "{\"output\": \"Gear Set\", \"parts\": {\"Engine\": 1}, \"seconds\": 4}]}", "physics-only"),
            (AsmHead + "{\"output\": \"Gear Set\", \"parts\": {\"Jam\": 1}, \"seconds\": 4}]}", "Jam"),
            (AsmHead + "{\"output\": \"Engine\", \"count\": 2, \"materials\": {\"steel\": 5}, \"seconds\": 4}]}", "count"),
            (AsmHead + good.Replace("\"seconds\": 4", "\"seconds\": 4, \"minTier\": 9") + "]}", "minTier"),
            (AsmHead + "{\"output\": \"Gear Set\", \"materials\": {\"steel\": 1, \"copper\": 1, \"gold\": 1, \"glass\": 1}, \"parts\": {\"Bearing\": 1, \"Lens\": 1, \"Lamp\": 1}, \"seconds\": 4}]}", "at most"),
        ];
        foreach (var (json, why) in bad)
        {
            var b = AssemblingTable.Parse(json);
            want(b.Problem.Contains(why) && b.Recipes.Length == 0, $"{json} wasn't refused for \"{why}\" (\"{b.Problem}\")");
        }
        want(AssemblingTable.Load("no-such-file.json", ConstructParts.All) is { Problem.Length: > 0, Recipes.Length: 0 }, "a missing file gives no recipes and a reason");

        var file = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Assembling", "Assembler.json"));
        ConstructParts.Parse(file, "good.json");
        (string From, string To, string Want)[] parts =
        [
            ("\"outDir\": \"+z\"", "\"outDir\": \"+y\"", "outDir"), ("\"assemble\": {", "\"assemble\": { \"haste\": 2, ", "haste"), ("\"assemble\": {", "\"assemble\": { \"speed\": 500, ", "speed"),
            ("\"batches\": 4", "\"batches\": 0", "batches"), ("\"key\": \"recipe\"", "\"key\": \"period\", \"min\": 1, \"max\": 2, \"default\": 1", "doesn't use"),
        ];
        foreach (var (from, to, why) in parts)
            try
            {
                ConstructParts.Parse(file.Replace(from, to), "bad.json");
                want(false, $"an assembler part with {to} wasn't refused");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(why), $"an assembler part with {to}: \"{ex.Message}\", want \"{why}\"");
            }
    }

    /// <summary>
    /// The big machines are buildings, not bricks: the size table; less than half of their bounds solid; a player fits their aisle;
    /// a body can leave and a player can reach from beside a 10 m machine; every chute port on the outside.
    /// </summary>
    private static void BigMachineChecks(Action<bool, string> want)
    {
        (string Line, float[] Across)[] lines =
        [
            ("Smelter", [0, 3.5f, 4.5f, 6, 8, 10]), ("Sawmill", [0, 3, 4, 5, 7, 9]), ("Foundry", [0, 4, 5, 6.5f, 8, 10]), ("Assembler", [0, 2, 2.5f, 3.5f, 4.5f, 6]),
        ];
        foreach (var (line, across) in lines)
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var d = ConstructParts.Need(TierName(line, tier));
                var size = d.BoundsMax - d.BoundsMin;
                want(MathF.Abs(MathF.Max(size.X, size.Z) - across[tier]) < 0.1f, $"{d.Name} is {size} across, not {across[tier]}");
                var solid = 0f;
                for (var b = 0; b < d.SolidBoxes; b++)
                {
                    var h = d.Boxes[b].Half * 2;
                    solid += h.X * h.Y * h.Z;
                }
                want(solid < size.X * size.Y * size.Z * 0.55f, $"{d.Name} is a brick: {solid:0} of {size.X * size.Y * size.Z:0} m3 solid");
                want(d.NamedPorts.Count(p => p.Type == RouteType.Chute) == 2, $"{d.Name}'s chute ports");
                want(d.SolidBoxes <= ConstructParts.MaxBoxes, $"{d.Name}'s boxes");
                if (line is "Smelter" or "Foundry")
                {
                    // The aisle: a player stands between the body and the tray, on the floor plate.
                    var feet = new Vector3(0, 0.03f * across[tier], 0.1f * across[tier]);
                    var body = new Obb(feet + new Vector3(0, World.PlayerHeight * 0.5f, 0), new Vector3(World.PlayerRadius, World.PlayerHeight * 0.5f, World.PlayerRadius), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
                    for (var b = 0; b < d.SolidBoxes; b++)
                        want(!Geometry.ObbOverlap(body, new Obb(d.Boxes[b].Center, d.Boxes[b].Half, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)), $"{d.Name}: box {b} stands in the aisle");
                }
            }
    }

    /// <summary>
    /// The Foundry, on the real step with real chutes: two bins into one port make steel at the recipe's rate; a batch short of one
    /// input waits; a third material and a higher tier's recipe are refused; the pair picks the recipe; the bytes; the mass; a spill.
    /// </summary>
    private static void RefiningFoundry(Map yard, Action<bool, string> want)
    {
        World.SetMap(yard);
        var w = new ConstructWorld { CreativeRecycling = false };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (OpenSpot(w) is not { } spot || BuildBlueprint(w, "Foundry Works", spot.X, spot.Z) is not { } c)
        {
            want(false, "no Foundry Works on the yard");
            return;
        }
        c.Owner = 255;
        var names = RecyclingTable.Current;
        byte metal = names.IdOf("metal"), stone = names.IdOf("stone"), steel = names.IdOf("steel"), sand = names.IdOf("sand"), copper = names.IdOf("copper"), gold = names.IdOf("gold"), wood = names.IdOf("wood");
        var fi = PartNamed(c, "Foundry");
        var st = w.StoreOf(c, fi)!;
        var d = c.DefOf(fi);
        int a = PartNamed(c, "Materials Bin"), b = PartNamed(c, "Materials Bin", 1), o = PartNamed(c, "Materials Bin", 2);
        var inv = new SandboxInventory();
        string Look() => ConstructRefining.Look(d, st, false, inv);
        void WantLook(string text)
        {
            var line = Look();
            want(line.Contains(text) && line.All(ch => ch is >= ' ' and <= '~'), $"the Foundry's look line \"{line}\" should say \"{text}\"");
        }
        WantLook("takes metal + stone");
        w.Deposit(c, a, 40, metal);
        Run(2.5f);
        want(st.MixOf(metal) >= 20 && !st.Busy, $"metal alone waits ({st.MixOf(metal)}, busy {st.Busy})");
        WantLook("needs 2 metal");
        w.Deposit(c, b, 20, stone);
        Run(1.5f);
        want(st.MixN == 2 && st.Busy, $"both inputs at once ({st.MixN} kinds, busy {st.Busy})");
        WantLook("REFINING metal + stone to steel");
        Run(25);
        var made = w.StoreOf(c, o)!;
        want(made.Amount == 40 && made.Material == steel && st.MixN == 0 && w.StoreOf(c, a)!.Amount == 0 && w.StoreOf(c, b)!.Amount == 0, $"40 metal and 20 stone make 40 steel down the chute ({made.Amount} {names.NameOf(made.Material)}, input \"{Look()}\")");

        // A third material stays in its source; the pair it holds picks the recipe.
        w.Feed(c, fi, 3, metal);
        want(w.Feed(c, fi, 10, sand) == 0 && st.Refused == sand, $"sand with metal (refused {st.Refused})");
        WantLook("chute refused: sand stays in the source, no recipe mixes it with 3 metal");
        want(w.Feed(c, fi, 10, wood) == 0 && st.Refused == wood, "wood at a Foundry");
        WantLook("it takes metal + stone");

        // Tier 2 and 3 recipes.
        want(w.Feed(c, fi, 10, copper) == 0 && st.Refused == copper, "copper at a tier 1 Foundry");
        WantLook("it needs a tier 2 foundry, this is tier 1");
        want(ConstructRefining.TipMaterial(inv, d, st) == 0, "nothing carried, nothing to tip");
        inv.AddMaterial(stone, 5);
        want(ConstructRefining.TipMaterial(inv, d, st) == stone, "a Foundry holding metal takes stone from what you carry");

        // The bytes, and the mass of the mixed input.
        w.Feed(c, fi, 8, stone);
        st.Busy = false;
        var pw = new PacketWriter(2048);
        ConstructStorage.Write(pw, st);
        var copy = new CStore();
        var rd = new PacketReader(pw.Span);
        ConstructStorage.Read(ref rd, copy);
        want(copy.MixN == st.MixN && copy.MixOf(metal) == st.MixOf(metal) && copy.MixOf(stone) == st.MixOf(stone) && copy.Refused == st.Refused, $"a mixed input's bytes ({copy.MixN} kinds)");
        var bad = pw.Span.ToArray();
        bad[bad.Length - 2] = 0;
        bad[bad.Length - 1] = 0;
        try
        {
            var br = new PacketReader(bad);
            ConstructStorage.Read(ref br, new CStore());
            want(false, "a mixed input of nothing was read");
        }
        catch (MalformedPacketException)
        {
        }
        var kg = st.MixOf(metal) * names.WeightOf(metal) + st.MixOf(stone) * names.WeightOf(stone);
        w.LoadChanged = true;
        Run(ConstructWorld.LoadEvery / (float)World.TickRate * 2);
        want(c.LoadOf(fi, out var load, out _, out _) && MathF.Abs(load - kg) < kg * 0.6f, $"the Foundry's load ({load} kg, input {kg} kg)");

        // A broken machine spills each material.
        w.Feed(c, fi, 40, metal);
        var ids = w.Bodies.Select(x => x.Id).ToHashSet();
        var spilled = st.MixN;
        w.DamagePart(c, fi, 1e7f, out _, raw: true);
        c.Rebuild();
        var bundles = w.Bodies.Where(x => !ids.Contains(x.Id) && x.Wreck && x.PartCount == 1).ToList();
        want(spilled == 2 && bundles.Any(x => x.Parts[0].Setting == metal) && bundles.Any(x => x.Parts[0].Setting == stone), $"a broken Foundry's spill ({bundles.Count} bundles)");

        // The curve, at a higher tier: bronze at tier 2, circuits at tier 3.
        foreach (var (machine, input, tier) in ((string, byte, int)[])[("Workshop Foundry", copper, 2), ("Industrial Foundry", gold, 3)])
            want(ConstructParts.Need(machine) is { RefineMix: true } big && big.Tier == tier && RefiningTable.Current.MixesOf("foundry").Any(r => r.Uses(input) && r.MinTier == tier), $"{machine} works {names.NameOf(input)}");
    }

    private static int Inputs(CStore st)
    {
        var n = 0;
        for (var k = 0; k < st.MixN; k++)
            n += st.MixAmount![k];
        return n + st.ItemCount;
    }

    /// <summary>
    /// "Assembling" (docs/sandbox/CONSTRUCTS.md "Assembling"): the table and the size table, then the whole chain on the yard in
    /// survival on the real steps. Ore goes through the Smelting Station, metal and stone through the Foundry Works and steel
    /// into the Assembly Bay by real chutes (blueprint links and world links), beside parts from another chain on a shelf; an Engine
    /// leaves as a body at the output point. Then a missing input, a blocked output, power, Run, the tier gate, a refused chute,
    /// items on the tray and down a chute, F, a recipe change, the mass, the beacon's view, creative and no allocation.
    /// </summary>
    private static void CheckAssembling()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"assembling: the {ConstructYardMap} map is missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        AssemblingTableChecks(Want);
        BigMachineChecks(Want);
        var names = RecyclingTable.Current;
        byte ore = names.IdOf("iron ore"), metal = names.IdOf("metal"), stone = names.IdOf("stone"), steel = names.IdOf("steel"), wood = names.IdOf("wood");
        yard.RestoreProps();
        World.SetMap(yard);
        var w = new ConstructWorld { CreativeRecycling = false };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        Construct? Place(string blueprint)
        {
            if (OpenSpot(w) is not { } s || BuildBlueprint(w, blueprint, s.X, s.Z) is not { } c)
                return null;
            c.Owner = 255;
            return c;
        }
        if (Place("Smelting Station") is not { } station || Place("Foundry Works") is not { } works || Place("Assembly Bay") is not { } bay)
        {
            Fail("assembling: the blueprints didn't all go on the yard");
            return;
        }
        var asm = PartNamed(bay, "Workshop Assembler");
        var st = w.StoreOf(bay, asm)!;
        var def = bay.DefOf(asm);
        var edits = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        var me = (byte)0;
        var inv = edits.Inventory.Of(me);
        AssembleRecipe? Recipe() => ConstructAssembling.RecipeOf(AssemblingTable.Current, bay.Parts[asm], def);
        string Look(bool near = false) => ConstructAssembling.Look(def, st, Recipe(), near, inv, null);
        void WantLook(string text)
        {
            var line = Look();
            Want(line.Contains(text) && line.All(ch => ch is >= ' ' and <= '~'), $"the look line \"{line}\" should say \"{text}\"");
        }
        var engine = AssemblingTable.Current.Recipes.First(r => r.Name == "Engine");
        Want(Recipe() == engine && bay.HasAssembler && bay.RouteCount == 3 && bay.HasBattery && bay.Anchored, $"the Assembly Bay (recipe {Recipe()?.Name}, {bay.RouteCount} chutes, battery {bay.HasBattery})");
        Want(station.RouteCount == 2 && works.RouteCount == 3, $"the chutes reach the machines' ports ({station.RouteCount}, {works.RouteCount})");

        // ---- The chain, by real chutes: ore, metal; metal and stone, steel; steel and the shelf's parts, an Engine.
        int oreBin = PartNamed(station, "Materials Bin"), metalOut = PartNamed(station, "Materials Bin", 1);
        int metalBin = PartNamed(works, "Materials Bin"), stoneBin = PartNamed(works, "Materials Bin", 1), steelOut = PartNamed(works, "Materials Bin", 2);
        int steelBin = PartNamed(bay, "Materials Bin"), shelf = PartNamed(bay, "Parts Shelf");
        var none = ReadOnlySpan<WNode>.Empty;
        Want(w.AddWorldLink(q, RouteType.Chute, station, metalOut, 255, none, works, metalBin, 255) >= 0, $"a chute from the Smelting Station to the Foundry Works: {w.CheckWorldLink(q, RouteType.Chute, station, metalOut, 255, none, works, metalBin, 255)}");
        Want(w.AddWorldLink(q, RouteType.Chute, works, steelOut, 255, none, bay, steelBin, 255) >= 0, $"a chute from the Foundry Works to the Assembly Bay: {w.CheckWorldLink(q, RouteType.Chute, works, steelOut, 255, none, bay, steelBin, 255)}");
        var piston = ConstructParts.Need("Piston Set");
        var loom = ConstructParts.Need("Wiring Loom");
        foreach (var p in (ConstructPartDef[])[piston, piston, loom])
            Want(SlotStore.Instance.PutPart(w, bay, shelf, p, false) || SlotStore.Instance.PutPart(w, bay, shelf, p, true), $"a {p.Name} on the shelf");
        Run(0.3f);
        WantLook("needs");
        w.Deposit(station, oreBin, 44, ore);
        w.Deposit(works, stoneBin, 12, stone);
        var known = w.Bodies.Select(b => b.Id).ToHashSet();
        Construct? Made() => w.Bodies.FirstOrDefault(b => !known.Contains(b.Id) && b.PartCount == 1 && b.Parts[0].D == engine.Output);
        var waited = 0f;
        for (; waited < 70 && Made() == null; waited += 1)
            Run(1);
        var smelter = w.StoreOf(station, PartNamed(station, "Smelter"))!;
        var foundry = w.StoreOf(works, PartNamed(works, "Foundry"))!;
        Line($"assembling: the chain made an Engine in {waited} s: steel {st.MixOf(steel)}, {st.KindN} kinds left, Smelter {smelter.InAmount} in, Foundry holds {foundry.MixN}");
        if (Made() is not { } built)
        {
            Fail($"assembling: no Engine after {waited} s: \"{Look()}\"; Smelter in {smelter.InAmount}, Foundry \"{ConstructRefining.Look(works.DefOf(PartNamed(works, "Foundry")), foundry, false, inv)}\"");
            return;
        }
        Want(Inputs(st) <= 2, $"the batch used its inputs: \"{Look()}\"");
        bay.WorldFrame(bay.Parts[asm].Seg, out var fq, out var ft);
        var outPoint = ft + Vector3.Transform(bay.Parts[asm].Pos + Rot24.Apply(bay.Parts[asm].Rot, def.AsmOutAt), fq);
        Want(Vector3.Distance(built.WorldCom with { Y = 0 }, outPoint with { Y = 0 }) < 2f && built.Owner == bay.Owner, $"the Engine left at {built.WorldCom}, not at the output point {outPoint}");
        w.Remove(built);

        // ---- A missing input: it says which, and waits.
        WantLook("needs");
        Want(!st.Busy, "short of a batch it's busy");

        // ---- A full batch, then each thing that stops it: power, Run, a blocked output.
        void Fill()
        {
            for (var k = 0; k < 2; k++)
                w.FeedPart(bay, asm, piston);
            w.FeedPart(bay, asm, loom);
            w.FeedAssembler(bay, asm, 20, steel);
        }
        void WantInputsFor(string what) => Want(!ConstructAssemblingShort(st, engine), $"{what}: \"{Look()}\"");
        Fill();
        WantInputsFor("filled");
        bay.Charge = 0;
        Run(1);
        Want(!st.Busy && Look().Contains("no power") && Inputs(st) >= 23, $"no power: \"{Look()}\"");
        bay.Charge = bay.ChargeCapacity;
        Run(0.5f);
        Want(st.Busy && bay.PowerUse >= def.PowerDraw, $"powered it works and draws ({st.Busy}, {bay.PowerUse} W)");
        WantLook("BUILDING Engine");
        var sw = ModFit(w, bay, "Switch", "-y", "assembling");
        if (sw < 0)
            return;
        Wire(w, bay, sw, asm);
        bay.Parts[sw].State = 0;
        Run(0.3f);
        var heldJob = st.JobTicks;
        Want(st.Held && !st.Busy, $"a Run wire off holds it (held {st.Held}, busy {st.Busy})");
        Run(1);
        Want(st.JobTicks == heldJob && Inputs(st) >= 23, $"held off it worked ({st.JobTicks} from {heldJob})");
        w.RemoveWires(bay, sw);
        bay.Rebuild();

        // Something on the output point: the batch waits with its inputs, says why, and leaves when it clears.
        var outDir = Vector3.Transform(Rot24.Apply(bay.Parts[asm].Rot, def.AsmOutDir), fq);
        var block = ConstructEdits.DropLoose(w, ConstructParts.Need("Smelter"), outPoint + outDir * 2.2f + new Vector3(0, 2.6f, 0), Quaternion.Identity)!;
        block.Pinned = true;
        block.Rebuild();
        known = w.Bodies.Select(b => b.Id).ToHashSet();
        for (var t = 0; t < 30 && st.Blocked == 0; t++)
            Run(1);
        Want(st.Blocked == (byte)AsmBlock.OutputBlocked && Made() == null && Inputs(st) >= 23 && !st.Busy, $"a blocked output: blocked {st.Blocked}, \"{Look()}\"");
        WantLook("something is in the way of its output point");
        w.Remove(block);
        Run(1.5f);
        Want(st.Blocked == 0 && Made() is { } after && Inputs(st) <= 2, $"cleared, the Engine leaves: blocked {st.Blocked}, \"{Look()}\"");
        if (Made() is { } left)
            w.Remove(left);

        // ---- The tier gate: an Engine asks for tier 2; a tier 1 Assembler refuses it and says so.
        Run(0.1f);
        var plain = ModFit(w, bay, "Assembler", "bottom", "assembling");
        if (plain >= 0)
        {
            var cfg = (byte)ConstructParts.Need("Assembler").CfgSlot[(int)CfgKey.Recipe];
            var choice = Array.IndexOf(AssemblingTable.Current.Choices, "Engine");
            ConstructConfig.Set(ref bay.Parts[plain], bay.DefOf(plain), cfg, choice);
            var ps = w.StoreOf(bay, plain)!;
            Want(w.FeedAssembler(bay, plain, 20, steel) == 0 && ps.Refused == steel, $"a tier 1 Assembler took steel for an Engine (refused {ps.Refused})");
            var line = ConstructAssembling.Look(bay.DefOf(plain), ps, engine, false, inv, null);
            Want(line.Contains("needs a tier 2 assembler, this is tier 1"), $"the tier gate's look line: \"{line}\"");
            w.RemovePart(bay, plain);
            bay.Rebuild();
            asm = PartNamed(bay, "Workshop Assembler");
            steelBin = PartNamed(bay, "Materials Bin");
            shelf = PartNamed(bay, "Parts Shelf");
        }
        else
            Want(false, "no room for a tier 1 Assembler on the bay");

        // ---- A refused chute: what the recipe doesn't use stays in the source, and the look line says so.
        w.Withdraw(bay, steelBin, 100000);
        w.Deposit(bay, steelBin, 30, wood);
        Run(3);
        Want(w.StoreOf(bay, steelBin)!.Amount == 30 && st.Refused == wood && st.MixOf(wood) == 0, $"wood at an Engine's Assembler (bin {w.StoreOf(bay, steelBin)!.Amount}, refused {st.Refused})");
        WantLook("chute refused: wood stays in the source");
        w.Withdraw(bay, steelBin, 30);

        // ---- An item: on the tray, down a chute to the Chest; F takes one; a beacon's network sees the tray and can't fill it.
        var slot = (byte)def.CfgSlot[(int)CfgKey.Recipe];
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, Array.IndexOf(AssemblingTable.Current.Choices, "Gear Set"));
        Run(0.2f);
        var gear = ConstructParts.Need("Gear Set");
        var chest = PartNamed(bay, "Chest");
        Want(ConstructAssembling.OutputOf(def, st, Recipe()!) == AsmOut.Tray, "a Gear Set should go on the tray");
        w.FeedAssembler(bay, asm, 20, steel);
        var machineLoad = st.MixOf(steel) * names.WeightOf(steel);
        Run(ConstructWorld.LoadEvery / (float)World.TickRate * 2);
        Run(0.1f);
        Want(bay.LoadMass >= machineLoad * 0.5f, $"an Assembler's input counts in the mass ({bay.LoadMass} kg, {machineLoad} kg of steel)");
        for (var t = 0; t < 8; t++)
            Run(1);
        var chestStore = w.StoreOf(bay, chest)!;
        Want(chestStore.Items + st.Items >= 2 && st.Items + chestStore.Items <= 4, $"Gear Sets on the tray ({st.Items}) and down the chute ({chestStore.Items})");
        Want(chestStore.Items >= 1, $"the chute took nothing off the tray ({chestStore.Items})");
        Want(SlotStore.Instance.Serves(def) && SlotStore.Instance.Parts(w, bay, asm) == st.Items, "a beacon's network reads the tray");
        Want(!SlotStore.Instance.PutPart(w, bay, asm, gear, false) && !SlotStore.Instance.PutPart(w, bay, asm, gear, true), "a beacon's network filled an Assembler's tray");
        Want(ConstructStorage.CanSet(def, st, 0, gear, out _) is { } tray && tray.Contains("output tray"), "a slot of the tray took a part by hand");

        // ---- F: a held part it wants, carried material it wants, the tray, then its input back after a recipe change.
        var eye = ft + Vector3.Transform(bay.Parts[asm].Pos, fq) + new Vector3(0, 1.5f, 0);
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, Array.IndexOf(AssemblingTable.Current.Choices, "Control Unit"));
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, Array.IndexOf(AssemblingTable.Current.Choices, "Servo Motor"));
        Run(0.2f);
        Want(Recipe()?.Name == "Servo Motor", "the recipe didn't change");
        inv.AddMaterial(steel, 40);
        var why = edits.UseBin(w, me, eye, bay.Id, asm);
        Want(why == null && st.MixOf(steel) > 0 && inv.Materials[steel] < 40, $"F tips what it wants: \"{why}\", {st.MixOf(steel)}");
        ref var hand = ref edits.Hand(me);
        hand.Held = ConstructParts.Need("Bearing").Id;
        why = edits.UseBin(w, me, eye, bay.Id, asm);
        Want(why == null && hand.Held == ConstructParts.None && st.KindIndex(ConstructParts.Need("Bearing").Id) >= 0, $"F puts in a held part: \"{why}\"");
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, 0);
        Run(0.2f);
        WantLook("no recipe");
        var before = inv.Materials[steel];
        for (var t = 0; t < 16 && (st.Items > 0 || st.MixN > 0 || st.KindN > 0); t++)
        {
            why = edits.UseBin(w, me, eye, bay.Id, asm);
            Want(why == null, $"F empties a machine: \"{why}\"");
            hand.Held = ConstructParts.None;
        }
        Want(st.Items == 0 && st.MixN == 0 && st.KindN == 0 && inv.Materials[steel] > before, $"F took everything back (tray {st.Items}, mix {st.MixN}, kinds {st.KindN}, steel {inv.Materials[steel]} from {before})");
        Want(edits.UseBin(w, me, eye, bay.Id, asm) is { } empty && empty.Contains("no recipe"), "F at an empty machine with no recipe says why");

        // ---- From beside a 10 m machine: the server measures reach to the part's nearest point, not its centre.
        if (OpenSpot(w) is { } farSpot && ConstructBuild.PlaceNew(w, ConstructParts.Need("Plasma Smelter"), new FreePlacement { Pos = farSpot, Rot = Quaternion.Identity, Anchor = true }) is { } huge)
        {
            huge.Rebuild();
            var side = farSpot + new Vector3(5.8f, 1.6f, -2f);
            Want(edits.UseBin(w, me, side, huge.Id, 0) is null or { } and not "too far away", $"beside a Plasma Smelter F is \"too far away\": {edits.UseBin(w, me, side, huge.Id, 0)}");
            Want(edits.UseBin(w, me, farSpot + new Vector3(12f, 1.6f, -2f), huge.Id, 0) == "too far away", "from 7 m off it's still in reach");
            w.Remove(huge);
        }
        else
            Want(false, "no room for a Plasma Smelter");

        // ---- Creative: instant and unpowered; a broken machine spills its input.
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, Array.IndexOf(AssemblingTable.Current.Choices, "Gear Set"));
        Run(0.2f);
        w.FeedAssembler(bay, asm, 10, steel);
        bay.Charge = 0;
        w.CreativeRecycling = true;
        Run(0.25f);
        w.CreativeRecycling = false;
        Want(st.MixOf(steel) == 0 && st.Items + chestStore.Items >= 2, $"creative: {st.MixOf(steel)} steel left, tray {st.Items}");
        bay.Charge = bay.ChargeCapacity;

        // ---- Nothing allocates: a machine working and a blocked one, over 240 steady ticks.
        ConstructConfig.Set(ref bay.Parts[asm], def, slot, Array.IndexOf(AssemblingTable.Current.Choices, "Engine"));
        Run(0.5f);
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 240; t++)
            ConstructAssembling.Step(w, q);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Want(alloc == 0, $"the assembling step allocates {alloc} B over 240 ticks");
        foreach (var p in problems)
            Fail($"assembling: {p}");
        yard.RestoreProps();
    }

    private static bool ConstructAssemblingShort(CStore st, AssembleRecipe r) => ConstructAssembling.StateOf(ConstructParts.Need("Workshop Assembler"), st, r) == AsmState.Short;

    /// <summary>
    /// "Assembling network": a real host, a client and a late joiner over the transport, survival rules. The client spawns the
    /// Assembly Bay, sets a recipe with the configure tool and tips its carried material in by the Bin message; the mixed input, the
    /// parts, the flags, the look line, a blocked output and the mass reach everyone, and a body built reaches all of them.
    /// </summary>
    private static void CheckAssemblingNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"assembling network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26491;
        var keep = sb.InfiniteParts;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        sb.InfiniteParts = false;
        try
        {
            using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
            server.Mode = GameMode.Sandbox;
            using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            Pump(server, [a], 1.0, ref now, clock);
            for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
                PumpFast(server, [a], 0.25, ref now);
            Pump(server, [a], 0.3, ref now, clock);
            if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
            {
                Fail("assembling network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("assembling network: no open ground on the yard");
                return;
            }
            sp.Teleport(spot + new Vector3(0, 0.05f, 0));
            sp.Yaw = 0;
            Pump(server, [a], 0.3, ref now, clock);
            var known = w.Bodies.Select(b => b.Id).ToHashSet();
            a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Assembly Bay"));
            Pump(server, [a], 1.0, ref now, clock);
            var bay = w.Bodies.FirstOrDefault(b => !known.Contains(b.Id) && b.HasAssembler);
            if (bay == null)
            {
                Fail($"assembling network: the Assembly Bay didn't spawn (\"{a.SandboxNotice}\")");
                return;
            }
            var asm = PartNamed(bay, "Workshop Assembler");
            var st = w.StoreOf(bay, asm)!;
            var def = bay.DefOf(asm);
            var names = RecyclingTable.Current;
            var steel = names.IdOf("steel");
            var recipes = AssemblingTable.Current;
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.1)
                    Pump(server, peers, 0.1, ref now, clock);
                return done();
            }
            CStore? Store(Game g) => g.Constructs.Get(bay.Id) is { } cc ? g.Constructs.StoreOf(cc, asm) : null;
            AssembleRecipe? RecipeOn(Game g) => g.Constructs.Get(bay.Id) is { } cc ? ConstructAssembling.RecipeOf(recipes, cc.Parts[asm], def) : null;
            string LookOf(Game g) => Store(g) is { } s ? ConstructAssembling.Look(def, s, RecipeOn(g), false, g.SandboxInventory(g.LocalId), null) : "(no store)";

            // The configure tool over the wire: the recipe is a setting.
            a.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Configure);
            Pump(server, [a], 0.2, ref now, clock);
            bay.WorldFrame(bay.Parts[asm].Seg, out var fq, out var ft);
            var centre = ft + Vector3.Transform(bay.Parts[asm].Pos, fq);
            sp.Teleport(centre + new Vector3(-1.6f, 0.05f, 0));
            Pump(server, [a], 0.2, ref now, clock);
            a.SandboxConfigure(bay.Id, [(byte)asm], [(byte)def.CfgSlot[(int)CfgKey.Recipe]], [Array.IndexOf(recipes.Choices, "Steel Plate")]);
            if (!Await(() => RecipeOn(a)?.Name == "Steel Plate" && RecipeOn(server)?.Name == "Steel Plate", 3, [a]))
                Fail($"assembling network: the recipe didn't reach both ends ({RecipeOn(server)?.Name}, {RecipeOn(a)?.Name}, \"{a.SandboxNotice}\")");

            // F over the wire: carried steel goes in; the client sees the mixed input, the work and the tray.
            server.SandboxInventory(sp.Id).AddMaterial(steel, 8);
            Pump(server, [a], 0.4, ref now, clock);
            a.SandboxSend(CActKind.Bin, body: bay.Id, part: (byte)asm);
            if (!Await(() => st.MixOf(steel) > 0 || st.Items > 0, 3, [a]))
                Fail($"assembling network: F didn't tip the steel in (\"{a.SandboxNotice}\")");
            if (!Await(() => Store(a) is { } s && (s.MixOf(steel) > 0 || s.Busy), 3, [a]))
                Fail($"assembling network: the client never saw its input (\"{LookOf(a)}\")");
            if (!Await(() => Store(a) is { Items: >= 2 } || w.StoreOf(bay, PartNamed(bay, "Chest")) is { Items: >= 1 }, 8, [a]))
                Fail($"assembling network: no Steel Plates on the tray (\"{LookOf(a)}\", host tray {st.Items})");

            // An Engine, part way: three stores' worth of state: materials and parts in the input, seen by a late joiner too.
            a.SandboxConfigure(bay.Id, [(byte)asm], [(byte)def.CfgSlot[(int)CfgKey.Recipe]], [Array.IndexOf(recipes.Choices, "Engine")]);
            Await(() => RecipeOn(server)?.Name == "Engine", 3, [a]);
            Pump(server, [a], 0.4, ref now, clock);
            var piston = ConstructParts.Need("Piston Set");
            w.FeedAssembler(bay, asm, 12, steel);
            w.FeedPart(bay, asm, piston);
            bool Same(Game g) => Store(g) is { } s && s.MixOf(steel) == st.MixOf(steel) && s.KindN == st.KindN && s.ItemCount == st.ItemCount && g.Constructs.Get(bay.Id) is { } cc && MathF.Abs(cc.Mass - bay.Mass) < 0.01f;
            if (!Await(() => Same(a), 4, [a]))
                Fail($"assembling network: the client's copy (\"{LookOf(a)}\") isn't the server's ({st.MixOf(steel)} steel, {st.KindN} kinds, {bay.Mass} kg)");
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            if (!Await(() => Same(late), 5, [a, late]))
                Fail($"assembling network: the joiner's copy (\"{LookOf(late)}\") isn't the server's");
            else if (!LookOf(late).Contains("needs"))
                Fail($"assembling network: the joiner's look line: \"{LookOf(late)}\"");

            // Finished, the body reaches everyone; before that, a blocked output's reason does.
            var outAt = centre + Vector3.Transform(Rot24.Apply(bay.Parts[asm].Rot, def.AsmOutAt + def.AsmOutDir * 0.5f), fq);
            var block = ConstructEdits.DropLoose(w, ConstructParts.Need("Smelter"), outAt + new Vector3(0, 2.6f, 2f), Quaternion.Identity)!;
            block.Pinned = true;
            block.Rebuild();
            w.FeedPart(bay, asm, piston);
            w.FeedPart(bay, asm, ConstructParts.Need("Wiring Loom"));
            w.FeedAssembler(bay, asm, 8, steel);
            if (!Await(() => Store(a) is { Blocked: not 0 } && Store(late) is { Blocked: not 0 }, 16, [a, late]))
                Fail($"assembling network: nobody heard the blocked output (host {st.Blocked}, client \"{LookOf(a)}\")");
            else if (!LookOf(late).Contains("in the way of its output point"))
                Fail($"assembling network: the joiner's look line: \"{LookOf(late)}\"");
            var ids = w.Bodies.Select(b => b.Id).ToHashSet();
            w.Remove(block);
            if (!Await(() => w.Bodies.Any(b => !ids.Contains(b.Id) && b.PartCount == 1 && b.Parts[0].D.Name == "Engine"), 3, [a, late]))
                Fail("assembling network: the Engine didn't leave when the way cleared");
            else
            {
                var body = w.Bodies.First(b => !ids.Contains(b.Id) && b.PartCount == 1);
                if (!Await(() => a.Constructs.Get(body.Id) != null && late.Constructs.Get(body.Id) != null && Store(a) is { Blocked: 0 } && Store(late) is { Blocked: 0 }, 3, [a, late]))
                    Fail("assembling network: a peer never got the Engine's body");
            }
        }
        finally
        {
            sb.InfiniteParts = keep;
            yard.RestoreProps();
        }
    }
}
