using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

public static partial class SelfTest
{
    private const string RefineHead = "{\"format\": \"call-of-slop-refining/1\", \"recipes\": [";

    /// <summary>The table: the shipped recipes and materials, and every kind of bad file refused with a reason (the built-in recipes stand in).</summary>
    private static void RefiningTableChecks(Action<bool, string> want)
    {
        var names = RecyclingTable.Current;
        var table = RefiningTable.Current;
        want(table.Problem.Length == 0 && table.Recipes.Length == 15, $"the shipped table (\"{table.Problem}\", {table.Recipes.Length} recipes)");
        want(names.Problem.Length == 0 && names.Materials.Length == 23 && RecyclingTable.MaxMaterials == 63 && RecyclingTable.MaxMaterials < SandboxInventory.MaxMaterials, $"the materials fit the inventory ({names.Materials.Length})");
        (string Machine, string In, string Out)[] shipped = [("smelter", "iron ore", "metal"), ("smelter", "copper ore", "copper"), ("smelter", "gold ore", "gold"), ("smelter", "sand", "glass"), ("sawmill", "wood", "planks")];
        foreach (var (m, i, o) in shipped)
            want(table.Find(m, names.IdOf(i)) is { } r && r.Output == names.IdOf(o), $"the {m} doesn't make {o} from {i}");
        foreach (var (m, a, b, o) in ((string, string, string, string)[])[("foundry", "metal", "stone", "steel"), ("foundry", "stone", "sand", "concrete"), ("foundry", "copper", "metal", "bronze"), ("foundry", "planks", "glass", "laminate"), ("foundry", "planks", "sand", "rubber"), ("foundry", "copper", "gold", "circuits")])
            want(table.MixesOf(m).Any(r => r.Uses(names.IdOf(a)) && r.Uses(names.IdOf(b)) && r.OutputName == o), $"the {m} doesn't make {o} from {a} and {b}");
        want(table.IsMix("foundry") && !table.IsMix("smelter") && ConstructParts.Need("Foundry").RefineMix, "the Foundry's input is mixed");
        want(table.Find("sawmill", names.IdOf("sand")) == null && table.Find("smelter", 0) == null && table.Find("kiln", 1) == null, "a recipe that isn't there was found");
        foreach (var m in (string[])["copper", "gold", "planks"])
            want(names.IdOf(m) > 12 && names.KgPerUnit.ContainsKey(m) && names.ColourOf(names.IdOf(m)) != new Vector3(0.5f), $"{m}: id {names.IdOf(m)}, its weight or its colour");
        want(names.Materials[..12].SequenceEqual((string[])["metal", "wood", "rubber", "circuits", "stone", "glass", "cloth", "sand", "iron ore", "copper ore", "gold ore", "dirt"]), "the old materials' ids moved");
        const string mixed = "{\"machine\": \"kiln\", \"inputs\": [{\"material\": \"metal\", \"units\": 2}, {\"material\": \"stone\", \"units\": 1}], \"output\": \"steel\", \"unitsOut\": 2, \"seconds\": 1}";
        want(RefiningTable.Parse(RefineHead + mixed + "]}") is { Problem.Length: 0, Recipes: [{ Mix.Length: 2, InputName: "metal + stone", Batch: "2 metal + 1 stone" }] }, "a good mixed recipe");
        const string good = "{\"machine\": \"smelter\", \"input\": \"sand\", \"unitsIn\": 2, \"output\": \"glass\", \"unitsOut\": 1, \"seconds\": 1}";
        var own = RefiningTable.Parse(RefineHead + good + "]}");
        want(own.Problem.Length == 0 && own.Recipes is [{ UnitsIn: 2, UnitsOut: 1, Ticks: 60 }], $"a good file (\"{own.Problem}\")");
        (string Json, string Want)[] bad =
        [
            ("{", "depth"), ("[]", "format"), ("{\"format\": \"nope\", \"recipes\": []}", "format"), ("{\"format\": 3}", "format"),
            ("{\"format\": \"call-of-slop-refining/1\"}", "recipes"), (RefineHead + "]}", "recipes"),
            (RefineHead + good + "], \"extra\": 1}", "extra"), (RefineHead + "3]}", "recipe 1"),
            (RefineHead + good.Replace("\"seconds\": 1", "\"seconds\": 1, \"fuel\": 2") + "]}", "fuel"),
            (RefineHead + good.Replace("\"machine\": \"smelter\", ", "") + "]}", "machine"), (RefineHead + good.Replace("smelter", "Big Kiln") + "]}", "lower-case"),
            (RefineHead + good.Replace("\"input\": \"sand\"", "\"input\": \"jam\"") + "]}", "jam"), (RefineHead + good.Replace("glass", "jam") + "]}", "jam"),
            (RefineHead + good.Replace("glass", "sand") + "]}", "itself"), (RefineHead + good + ", " + good + "]}", "already has a recipe"),
            (RefineHead + good.Replace("\"unitsIn\": 2", "\"unitsIn\": 0") + "]}", "unitsIn"), (RefineHead + good.Replace("\"unitsIn\": 2", "\"unitsIn\": 2.5") + "]}", "unitsIn"),
            (RefineHead + good.Replace("\"unitsOut\": 1", "\"unitsOut\": 500") + "]}", "unitsOut"), (RefineHead + good.Replace("\"seconds\": 1", "\"seconds\": 0") + "]}", "seconds"),
            (RefineHead + good.Replace("\"seconds\": 1", "\"seconds\": \"soon\"") + "]}", "seconds"),
            (RefineHead + mixed.Replace("\"inputs\": [", "\"inputs\": [{\"material\": \"metal\", \"units\": 1}, ") + "]}", "twice"),
            (RefineHead + mixed.Replace("{\"material\": \"stone\", \"units\": 1}", "{\"material\": \"jam\", \"units\": 1}") + "]}", "jam"),
            (RefineHead + mixed.Replace("{\"material\": \"stone\", \"units\": 1}", "{\"material\": \"stone\", \"units\": 0}") + "]}", "units"),
            (RefineHead + mixed.Replace(", {\"material\": \"stone\", \"units\": 1}", "") + "]}", "2 to 4"),
            (RefineHead + mixed.Replace("\"steel\"", "\"metal\"") + "]}", "itself"),
            (RefineHead + mixed.Replace("\"inputs\":", "\"input\": \"sand\", \"inputs\":") + "]}", "replaces"),
            (RefineHead + mixed + ", " + mixed + "]}", "already has a recipe"),
            (RefineHead + mixed + ", " + good.Replace("smelter", "kiln") + "]}", "mixed recipes"),
            (RefineHead + good.Replace("smelter", "kiln") + ", " + mixed + "]}", "single-input"),
        ];
        foreach (var (json, why) in bad)
        {
            var t = RefiningTable.Parse(json);
            want(t.Problem.Contains(why) && t.Recipes.Length == 5, $"{json} wasn't refused for \"{why}\" (\"{t.Problem}\", {t.Recipes.Length} built-in recipes)");
        }
        want(RefiningTable.Load("no-such-file.json") is { Problem.Length: > 0, Recipes.Length: 5 }, "a missing file gives the built-in recipes");

        // A bad machine part.
        var smelter = File.ReadAllText(Path.Combine(ConstructParts.Folder, "Refining", "Smelter.json"));
        ConstructParts.Parse(smelter, "good.json");
        (string From, string To, string Want)[] parts =
        [
            ("\"machine\": \"smelter\"", "\"machine\": \"kiln\"", "no recipes"),
            ("\"capacity\": 200", "\"capacity\": 5", "capacity"),
            ("\"capacity\": 400", "\"capacity\": 5", "at least"),
            ("\"material\": \"any\"", "\"material\": \"metal\"", "output bin"),
            ("\"min\": [-1.0325, 2.3975, -1.3825]", "\"min\": [-1.0325, 2.3975, -2.9]", "outside"),
            ("\"min\": [-1.0325, 2.3975, -1.3825], \"max\": [0.1925, 3.01, -0.5425]", "\"min\": [-0.4, 0.2, 1.1], \"max\": [0.4, 0.5, 1.4]", "overlaps"),
            ("\"refine\": {", "\"refine\": { \"haste\": 2, ", "haste"),
            ("\"refine\": {", "\"refine\": { \"speed\": 200, ", "speed"),
        ];
        foreach (var (from, to, why) in parts)
            try
            {
                ConstructParts.Parse(smelter.Replace(from, to), "bad.json");
                want(false, $"a machine part with {to} wasn't refused");
            }
            catch (ConstructPartException ex)
            {
                want(ex.Message.Contains(why), $"a machine part with {to}: \"{ex.Message}\", want \"{why}\"");
            }
    }

    /// <summary>
    /// "Refining" (docs/sandbox/CONSTRUCTS.md "Refining"): the table and the parts; then the Smelting Station blueprint on the
    /// yard, in survival, on the real step: a real chute feeds the Smelter, which does nothing without power and refines at
    /// the recipe's rate with it, drawing only while it works, and a chute takes the metal on. A Run wire holds it off; a wrong
    /// material stays in the source and the look line says why; a short input, a full and a mixed output block with a reason;
    /// chutes draw only from the output; F tips into the input and takes from the output; the mass follows both bins; a
    /// Sawmill fed from another body; creative; a broken machine spills both bins; the store's bytes; no allocation.
    /// </summary>
    private static void CheckRefining()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox == null)
        {
            Fail($"refining: the {ConstructYardMap} map is missing");
            return;
        }
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        RefiningTableChecks(Want);
        var names = RecyclingTable.Current;
        byte ore = names.IdOf("iron ore"), metal = names.IdOf("metal"), wood = names.IdOf("wood"), sand = names.IdOf("sand"), glass = names.IdOf("glass"), planks = names.IdOf("planks");
        var iron = RefiningTable.Current.Find("smelter", ore)!;
        foreach (var name in (string[])["Smelter", "Sawmill"])
        {
            var d = ConstructParts.Need(name);
            Want(d.IsRefiner && d.PhysicsOnly && d.Tool == ConstructTool.Welder && d.Role == PartRole.Storage && d.PowerDraw > 0, $"the {name} part ({d.IsRefiner}, physics only {d.PhysicsOnly}, {d.Tool}, {d.PowerDraw} W)");
            Want(d.MaxInputs == 1 && d.DataIn is [ConstructGatherer.RunPort], $"the {name}'s Run input ({d.MaxInputs}, {string.Join("/", d.DataIn)})");
            Want(ZFight.Report(d.Name, ZFight.Boxes(d)).Count() == 0, $"the {name} has coplanar faces: {string.Join("; ", ZFight.Report(d.Name, ZFight.Boxes(d)))}");
        }
        var cat = SandboxCatalogue.Make();
        Want(ConstructBuild.FindBlueprint("Smelting Station") is { } sbp && cat.Blueprints.Sections.Any(s => s.Items.Contains(ConstructBuild.Blueprints.ToList().IndexOf(sbp))), "the Smelting Station isn't in the parts menu");

        yard.RestoreProps();
        World.SetMap(yard);
        var w = new ConstructWorld { CreativeRecycling = false };
        var q = World.Query;
        void Run(float seconds)
        {
            for (var t = 0; t < seconds * World.TickRate; t++)
                w.Step(q, World.TickDt);
        }
        if (OpenSpot(w) is not { } spot || BuildBlueprint(w, "Smelting Station", spot.X, spot.Z) is not { } c)
        {
            Fail("refining: no Smelting Station on the yard");
            return;
        }
        c.Owner = 255;
        var si = PartNamed(c, "Smelter");
        int binIn = PartNamed(c, "Materials Bin"), binOut = PartNamed(c, "Materials Bin", 1);
        var st = w.StoreOf(c, si)!;
        CStore sIn = w.StoreOf(c, binIn)!, sOut = w.StoreOf(c, binOut)!;
        var def = c.DefOf(si);
        var inv0 = new SandboxInventory();
        string Look(bool near = false) => ConstructRefining.Look(def, st, near, inv0);
        void WantLook(string text)
        {
            var line = Look();
            Want(line.Contains(text) && line.All(ch => ch is >= ' ' and <= '~'), $"the look line \"{line}\" should say \"{text}\"");
        }
        Want(c.HasRefiner && c.RouteCount == 2 && c.HasBattery && c.Anchored && c.Driver == 255, $"the station ({c.HasRefiner}, {c.RouteCount} links, battery {c.HasBattery}, anchored {c.Anchored})");
        var bareMass = c.Mass;
        Run(0.5f);
        Want(!st.Busy && c.PowerUse < def.PowerDraw, $"an empty Smelter draws ({c.PowerUse} W, busy {st.Busy})");
        WantLook("takes iron ore, sand");

        // ---- No power: the chute still feeds it, and nothing happens.
        c.Charge = 0;
        Run(0.2f);
        w.Deposit(c, binIn, 40, ore);
        Run(3);
        Want(st.InAmount >= 20 && st.InMaterial == ore && sIn.Amount == 40 - st.InAmount && sOut.Amount == 0 && st.Amount == 0, $"a real chute into a dead Smelter (input {st.InAmount} {names.NameOf(st.InMaterial)}, bin {sIn.Amount}, output {st.Amount})");
        Want(!st.Busy, "a Smelter with no power is busy");
        WantLook("no power");
        Run(2);

        // ---- Power: the recipe's rate, and it draws only while it works.
        c.Charge = c.ChargeCapacity;
        Run(0.25f);
        var drawing = st.Busy && c.PowerUse >= def.PowerDraw;
        Want(drawing, $"powered, it should work and draw ({st.Busy}, {c.PowerUse} W)");
        WantLook("refining iron ore to metal");
        Run(1.75f); // (2 s: 0.5 s a batch)
        var made = st.Amount + sOut.Amount;
        Line($"refining: 2 s: {made} metal from {40 - sIn.Amount - st.InAmount} ore; {c.PowerUse:0} W, chutes {sIn.Amount} in the bin");
        Want(made is >= 3 and <= 4 && 40 - sIn.Amount - st.InAmount == made * iron.UnitsIn, $"the rate ({made} metal in 2 s, {sIn.Amount} + {st.InAmount} ore left)");
        Run(9);
        Want(st.InAmount == 0 && sIn.Amount == 0 && st.Amount == 0 && sOut.Amount == 20 && sOut.Material == metal, $"40 ore makes 20 metal, down the chute (input {st.InAmount}, bin {sIn.Amount}, output {st.Amount}, out bin {sOut.Amount})");
        Run(0.2f);
        Want(!st.Busy && c.PowerUse < def.PowerDraw, $"done, it should stop drawing ({st.Busy}, {c.PowerUse} W)");
        Want(st.JobTicks == 0, $"done, it keeps {st.JobTicks} ticks of work");
        Run(ConstructWorld.LoadEvery / (float)World.TickRate * 2);
        Want(MathF.Abs(c.LoadMass - 40f) < 0.05f && MathF.Abs(c.Mass - (bareMass + 40f)) < 0.05f, $"the load is 20 metal ({c.LoadMass} kg, {c.Mass} kg, was {bareMass})");

        // ---- The Run wire: held off it waits, keeps its batch and draws nothing; on, it goes on. On a base, no seat.
        var sw = ModFit(w, c, "Switch", "-y", "refining");
        if (sw < 0)
            return;
        Wire(w, c, sw, si);
        c.Parts[sw].State = 1;
        Run(0.3f);
        w.Deposit(c, binIn, 10, ore);
        Run(1.2f);
        Want(st.Busy && !st.Held, $"a wire that is on runs it (busy {st.Busy}, held {st.Held})");
        c.Parts[sw].State = 0;
        Run(0.3f);
        var heldIn = st.InAmount;
        var heldOut = st.Amount + sOut.Amount;
        var heldBin = sIn.Amount;
        var heldJob = st.JobTicks;
        Want(st.Held && !st.Busy && c.PowerUse < def.PowerDraw, $"a wire that is off stops it (held {st.Held}, busy {st.Busy}, {c.PowerUse} W)");
        WantLook("Run wire is off");
        Run(1);
        Want(st.InAmount + sIn.Amount == heldIn + heldBin && st.Amount + sOut.Amount == heldOut && st.JobTicks == heldJob, $"held off it worked (input {st.InAmount}, made {st.Amount + sOut.Amount} from {heldOut}, job {st.JobTicks} from {heldJob})");
        c.Parts[sw].State = 1;
        Run(4);
        Want(!st.Held && st.InAmount == 0 && sIn.Amount == 0 && sOut.Amount == 25, $"let go it should finish ({st.InAmount}, {sIn.Amount}, {sOut.Amount})");

        // ---- A wrong material stays in the source, and says why.
        w.Deposit(c, binIn, 30, wood);
        var loadBefore = c.LoadMass;
        Run(3);
        Want(sIn.Amount == 30 && sIn.Material == wood && st.InAmount == 0 && st.Refused == wood, $"wood at a Smelter (bin {sIn.Amount}, input {st.InAmount}, refused {st.Refused})");
        WantLook("chute refused wood: takes iron ore");
        Want(MathF.Abs(c.LoadMass - (loadBefore + 30 * names.WeightOf(wood))) < 0.05f, $"the refused wood's weight ({c.LoadMass}, {loadBefore})");
        w.Withdraw(c, binIn, 100);
        Run(5);
        Want(st.Refused == 0 && !Look().Contains("refused"), $"the refusal should clear when the chute stops trying (\"{Look()}\")");
        // One material at a time; too little for a batch waits and says how much it needs.
        w.Feed(c, si, 1, ore);
        w.Deposit(c, binIn, 10, sand);
        Run(2.5f);
        Want(st.InAmount == 1 && st.InMaterial == ore && sIn.Amount == 10 && st.Refused == sand, $"another material while the input holds ore (input {st.InAmount}, bin {sIn.Amount}, refused {st.Refused})");
        WantLook("needs 2 iron ore a batch");
        WantLook("input holds iron ore");
        w.Withdraw(c, binIn, 100);
        w.Feed(c, si, 1, ore);
        Run(1);
        Want(st.InAmount == 0, $"the second unit makes a batch ({st.InAmount})");
        Run(5);

        // ---- Chutes draw only from the output, never the input.
        var outRoute = Enumerable.Range(0, c.RouteCount).First(k => c.Routes[k].From == si);
        ConstructRoutes.Remove(w, c, outRoute);
        c.Rebuild();
        w.Withdraw(c, binOut, 1000);
        c.Parts[sw].State = 0; // (held, so the input stays)
        Run(0.3f);
        w.Feed(c, si, 20, ore);
        var moved = ConstructRoutes.ChuteMove(w, c, si, c, binOut);
        Want(!moved && st.InAmount == 20 && sOut.Amount == 0, $"a chute took from the input ({moved}, {st.InAmount}, {sOut.Amount})");
        w.Deposit(c, si, 7, metal);
        moved = ConstructRoutes.ChuteMove(w, c, si, c, binOut);
        Want(moved && st.Amount == 0 && st.InAmount == 20 && sOut.Amount == 7 && sOut.Material == metal, $"a chute out of the output ({moved}, {st.Amount}, {st.InAmount}, {sOut.Amount})");

        // ---- A full output and a mixed one block, with a reason; emptied, it goes on.
        w.Deposit(c, si, def.BulkCapacity, metal);
        c.Parts[sw].State = 1;
        Run(1.5f);
        Want(st.InAmount == 20 && st.Amount == def.BulkCapacity && !st.Busy && c.PowerUse < def.PowerDraw, $"a full output should stop it ({st.InAmount}, {st.Amount}, busy {st.Busy}, {c.PowerUse} W)");
        WantLook("output full");
        moved = ConstructRoutes.ChuteMove(w, c, si, c, binOut);
        Run(1.1f);
        Want(moved && st.InAmount < 20, $"room again, it should go on ({moved}, input {st.InAmount})");
        Run(0.2f);
        w.Withdraw(c, si, 1000);
        w.Deposit(c, si, 3, glass);
        var keepIn = st.InAmount;
        Run(0.5f);
        Run(1);
        var keepIn2 = st.InAmount;
        Run(1);
        Want(st.InAmount == keepIn2 && st.Material == glass && !st.Busy && keepIn2 > 0, $"glass in the output should block iron ({st.InAmount} from {keepIn}, output {st.Amount} {names.NameOf(st.Material)})");
        WantLook("output holds glass: empty it to make metal");

        // ---- F: takes from the output, tips what it takes into the input, never tips into the output.
        var e = new ConstructEdits { Inventory = new SandboxInventories(), InfiniteParts = false };
        e.Inventory.SetInfinite(false);
        const byte me = 5;
        var inv = e.Inventory.Of(me);
        c.WorldFrame(c.Parts[si].Seg, out var fq, out var ft);
        c.PartBounds(si, out var fmn, out var fmx);
        var eye = ft + Vector3.Transform((fmn + fmx) * 0.5f, fq) + new Vector3(0, 0.5f, 0);
        Want(ConstructRefining.Look(def, st, true, inv).Contains("F: take 3 glass"), $"the look line offers the output (\"{ConstructRefining.Look(def, st, true, inv)}\")");
        var took = e.UseBin(w, me, eye, c.Id, si);
        Want(took == null && inv.Materials[glass] == 3 && st.Amount == 0, $"F took the glass ({took}, carried {inv.Materials[glass]}, output {st.Amount})");
        Run(6);
        Want(st.InAmount == 0, $"emptied, it should go on ({st.InAmount})");
        Run(0.2f);
        c.Parts[sw].State = 0;
        Run(0.2f);
        w.Withdraw(c, si, 1000);
        var nothing = e.UseBin(w, me, eye, c.Id, si);
        Want(nothing != null && nothing.Contains("iron ore") && st.InAmount == 0 && inv.Materials[glass] == 3, $"F with nothing it takes (\"{nothing}\", input {st.InAmount})");
        inv.AddMaterial(ore, 300);
        Want(ConstructRefining.Look(def, st, true, inv).Contains($"F: tip in {def.InCapacity} iron ore"), $"the look line offers the tip (\"{ConstructRefining.Look(def, st, true, inv)}\")");
        var tipped = e.UseBin(w, me, eye, c.Id, si);
        Want(tipped == null && st.InAmount == def.InCapacity && st.InMaterial == ore && st.Amount == 0 && inv.Materials[ore] == 300 - def.InCapacity, $"F tipped ore in ({tipped}, input {st.InAmount}, output {st.Amount}, carried {inv.Materials[ore]})");
        w.Deposit(c, si, 150, metal);
        var fromFull = e.UseBin(w, me, eye, c.Id, si);
        Want(fromFull == null && st.Amount == 50 && inv.Materials[metal] == 100 && st.InAmount == def.InCapacity, $"F at a full input takes a load ({fromFull}, output {st.Amount}, carried {inv.Materials[metal]})");

        // ---- The mass: both bins, at their own places; the cheap path is the rebuild's.
        w.Withdraw(c, si, 1000);
        w.Withdraw(c, binOut, 1000);
        Run(0.5f);
        var mass0 = c.Mass;
        var inKg = st.InAmount * names.WeightOf(ore);
        Want(MathF.Abs(c.LoadMass - inKg) < 0.05f, $"the input's weight counts ({c.LoadMass}, want {inKg})");
        ConstructStorage.FillBox(c.Parts[si], def, 100, out var oc, out _);
        ConstructStorage.InputBox(c.Parts[si], def, st.InAmount, out var ic, out _);
        w.Deposit(c, si, 100, metal);
        Run(0.5f);
        Want(c.LoadOf(si, out var both, out var centre, out var half) && MathF.Abs(both - (inKg + 200f)) < 0.05f && Vector3.Distance(centre, (ic * inKg + oc * 200f) / (inKg + 200f)) < 0.002f && half.Y > 0, $"both bins' load ({both} kg at {centre})");
        Want(MathF.Abs(c.LoadMass - both) < 0.05f && MathF.Abs(c.Mass - (mass0 + 200f)) < 0.05f && !c.Dirty, $"the mass follows the output ({c.LoadMass}, {c.Mass})");
        var (ma, ca, ia) = (c.Mass, c.Com, c.InvInertiaLocal);
        c.Dirty = true;
        c.Rebuild();
        Want(ma == c.Mass && ca == c.Com && ia == c.InvInertiaLocal, $"RefreshMass and Rebuild differ ({ma}/{c.Mass}, {ca}/{c.Com})");
        c.Parts[sw].State = 1;
        Run(1.1f);
        var batches = (def.InCapacity - st.InAmount) / iron.UnitsIn;
        Run(ConstructWorld.LoadEvery / (float)World.TickRate * 2);
        batches = (def.InCapacity - st.InAmount) / iron.UnitsIn;
        Want(batches > 0 && MathF.Abs(c.LoadMass - (st.InAmount * 4f + st.Amount * 2f)) <= 2 * 4f + 2f + 0.05f, $"the mass follows a batch ({c.LoadMass}, input {st.InAmount}, output {st.Amount})");

        // ---- A Sawmill on another body, fed across a world chute; a machine with nothing to do with a vehicle.
        var mill = ConstructBuild.Build(w, ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Mill", "steps": [
              { "part": "Concrete Foundation 4x4 m" }, { "part": "Sawmill", "on": 0, "socket": "t7_7", "by": "bottom" }, { "part": "Battery", "on": 0, "socket": "t15_15", "by": "-y" }
            ] }
            """, "mill.json"), ConstructWorld.Ground(World.Query, spot.X - 12, spot.Z), Quaternion.Identity, out var millWhy);
        if (mill == null)
        {
            Fail($"refining: the mill doesn't build: {millWhy}");
            return;
        }
        mill.Owner = 255;
        var mi = PartNamed(mill, "Sawmill");
        var ms = w.StoreOf(mill, mi)!;
        w.Deposit(c, binIn, 5, wood);
        Want(ConstructRoutes.ChuteMove(w, c, binIn, mill, mi) && ms.InAmount == 5 && ms.InMaterial == wood && sIn.Amount == 0, $"a chute from another body ({ms.InAmount}, {sIn.Amount})");
        Run(1.1f);
        Want(ms.Busy && mill.PowerUse >= mill.DefOf(mi).PowerDraw, $"the mill should work on its own battery ({ms.Busy}, {mill.PowerUse} W)");
        Run(2.5f);
        Want(ms.InAmount == 0 && ms.Amount == 10 && ms.Material == planks, $"5 wood makes 10 planks ({ms.InAmount}, {ms.Amount} {names.NameOf(ms.Material)})");
        ConstructRoutes.ChuteMove(w, c, binIn, mill, mi);
        w.Deposit(c, binIn, 10, ore);
        Want(!ConstructRoutes.ChuteMove(w, c, binIn, mill, mi) && ms.Refused == ore && ms.InAmount == 0, $"a Sawmill took ore ({ms.InAmount}, refused {ms.Refused})");
        w.Withdraw(c, binIn, 100);

        // ---- Creative: at once, unpowered; the Run wire still holds it; only what fits.
        c.Parts[sw].State = 0;
        Run(0.2f);
        w.Withdraw(c, si, 1000);
        w.CreativeRecycling = true;
        Run(0.3f);
        var creativeIn = st.InAmount;
        Want(creativeIn > 0 && st.Amount == 0, $"in creative a wire still holds it (input {creativeIn}, output {st.Amount})");
        ConstructRoutes.Remove(w, c, Enumerable.Range(0, c.RouteCount).First(k => c.Routes[k].Type == RouteType.Data));
        c.Rebuild();
        c.Charge = 0; // (a flat battery: a switch's wire would hold it off, as any logic with no power)
        Run(0.3f);
        Want(st.InAmount == 0 && st.Amount == creativeIn / 2 && !st.Busy, $"in creative it should refine at once ({st.InAmount}, {st.Amount} from {creativeIn}, busy {st.Busy})");
        w.Withdraw(c, si, 1000);
        w.Deposit(c, si, def.BulkCapacity - 3, metal);
        w.Feed(c, si, 20, ore);
        Run(0.3f);
        Want(st.Amount == def.BulkCapacity && st.InAmount == 14, $"in creative only what fits ({st.Amount}, input {st.InAmount})");
        w.CreativeRecycling = false;

        // ---- The store's bytes. Other stores cost nothing more.
        w.Withdraw(c, si, 1000);
        w.Deposit(c, si, 77, metal);
        st.Busy = true;
        st.Refused = wood;
        var pw = new PacketWriter(2048);
        ConstructStorage.Write(pw, st);
        var copy = new CStore();
        var rd = new PacketReader(pw.Span);
        ConstructStorage.Read(ref rd, copy);
        Want(copy.Amount == 77 && copy.Material == metal && copy.InAmount == st.InAmount && copy.InMaterial == ore && copy.Refused == wood && copy.Busy, $"a machine's bytes ({copy.Amount}, {copy.InAmount} {copy.InMaterial}, refused {copy.Refused})");
        var machineBytes = pw.Span.Length;
        var plain = new PacketWriter(2048);
        ConstructStorage.Write(plain, sIn);
        Want(plain.Span.Length == 5 && machineBytes == 9, $"a store is {plain.Span.Length} bytes, a machine's {machineBytes}");
        var bad = pw.Span.ToArray();
        bad[7] = 200; // (the input's material)
        try
        {
            var br = new PacketReader(bad);
            ConstructStorage.Read(ref br, new CStore());
            Want(false, "a machine's bad material was read");
        }
        catch (MalformedPacketException)
        {
        }
        st.Busy = false;
        st.Refused = 0;

        // ---- Nothing allocates, working or waiting.
        c.Charge = c.ChargeCapacity;
        w.Withdraw(c, si, 1000);
        Run(0.2f);
        w.Feed(c, si, def.InCapacity - st.InAmount, ore);
        for (var k = 0; k < 120; k++)
            ConstructRefining.Step(w);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 600; k++)
            ConstructRefining.Step(w);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"refining: 600 ticks of the step, working: {alloc} B");
        Want(alloc == 0, $"the refining step allocates ({alloc} B over 600 ticks)");
        Want(st.Amount > 0, "the allocation run made nothing");

        // ---- A broken machine spills both bins as bundles of their own materials.
        w.Withdraw(c, si, 1000);
        w.Deposit(c, si, 250, metal);
        var heldInput = st.InAmount;
        Want(heldInput > 100, $"the input for the spill ({heldInput})");
        var ids = w.Bodies.Select(b => b.Id).ToHashSet();
        w.DamagePart(c, si, 1e6f, out _, raw: true);
        Run(0.2f);
        var bundles = w.Bodies.Where(b => !ids.Contains(b.Id) && b.PartCount == 1 && b.DefOf(0).Name == ConstructStorage.MaterialBundle).ToList();
        Want(bundles.Count(b => b.Parts[0].Setting == metal) == 3 && bundles.Count(b => b.Parts[0].Setting == ore) == (heldInput + 99) / 100, $"the spill: {bundles.Count} bundles ({string.Join(", ", bundles.Select(b => names.NameOf(b.Parts[0].Setting)))}), {heldInput} ore");
        Want(names.YieldOf(bundles[0].Parts[0]).Material != 0, "a spilled bundle gives its material back");
        Want(w.StoreOf(c, si) == null || w.StoreOf(c, si)!.Empty, "the broken machine keeps its store's contents");
        Run(ConstructWorld.LoadEvery / (float)World.TickRate * 2);
        Want(MathF.Abs(c.LoadMass - (sIn.Amount * names.WeightOf(sIn.Material) + sOut.Amount * names.WeightOf(sOut.Material))) < 0.05f, $"the spilled load stayed on the body ({c.LoadMass})");
        yard.RestoreProps();
        RefiningTiers(yard, Want); // (SelfTestMachineTiers.cs)
        RefiningFoundry(yard, Want); // (SelfTestAssembling.cs)
        foreach (var p in problems)
            Fail($"refining: {p}");
    }

    /// <summary>
    /// "Refining network": a real host, a client and a late joiner over the transport, survival rules. The client spawns the
    /// Smelting Station and F tips its carried ore into the Smelter by the Bin message; both bins, the busy, held and refused
    /// flags, the look line and the mass reach the client, and the joiner hears a machine stopped part way.
    /// </summary>
    private static void CheckRefiningNet()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"refining network: the {ConstructYardMap} map is missing");
            return;
        }
        const ushort port = 26483;
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
                Fail("refining network: the client didn't join");
                return;
            }
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
            Pump(server, [a], 0.3, ref now, clock);
            var w = server.Constructs;
            if (OpenSpot(w) is not { } spot)
            {
                Fail("refining network: no open ground on the yard");
                return;
            }
            sp.Teleport(spot + new Vector3(0, 0.05f, 0));
            sp.Yaw = 0;
            Pump(server, [a], 0.3, ref now, clock);
            var known = w.Bodies.Select(b => b.Id).ToHashSet();
            a.SandboxSend(CActKind.Blueprint, part: (byte)IndexOfBlueprint("Smelting Station"));
            Pump(server, [a], 1.0, ref now, clock);
            var station = w.Bodies.FirstOrDefault(b => !known.Contains(b.Id) && b.HasRefiner);
            if (station == null)
            {
                Fail($"refining network: the Smelting Station didn't spawn (\"{a.SandboxNotice}\")");
                return;
            }
            var si = PartNamed(station, "Smelter");
            var binIn = PartNamed(station, "Materials Bin");
            var st = w.StoreOf(station, si)!;
            var def = station.DefOf(si);
            var names = RecyclingTable.Current;
            byte ore = names.IdOf("iron ore"), wood = names.IdOf("wood"), metal = names.IdOf("metal");
            bool Await(Func<bool> done, double seconds, Game[] peers)
            {
                for (var t = 0.0; t < seconds && !done(); t += 0.1)
                    Pump(server, peers, 0.1, ref now, clock);
                return done();
            }
            CStore? Store(Game g) => g.Constructs.Get(station.Id) is { } cc ? g.Constructs.StoreOf(cc, si) : null;
            string LookOf(Game g) => Store(g) is { } s ? ConstructRefining.Look(def, s, false, g.SandboxInventory(g.LocalId)) : "(no store)";

            // F over the wire: the client's carried ore goes into the input, then both sides see it work.
            server.SandboxInventory(sp.Id).AddMaterial(ore, 60);
            Pump(server, [a], 0.4, ref now, clock);
            station.WorldFrame(station.Parts[si].Seg, out var fq, out var ft);
            station.PartBounds(si, out var fmn, out var fmx);
            sp.Teleport(ft + Vector3.Transform((fmn + fmx) * 0.5f, fq) + new Vector3(0, 0.05f, 1.2f));
            Pump(server, [a], 0.2, ref now, clock);
            a.SandboxSend(CActKind.Bin, body: station.Id, part: (byte)si);
            if (!Await(() => st.InAmount + st.Amount > 0 && server.SandboxInventory(sp.Id).Materials[ore] == 0, 3, [a]))
                Fail($"refining network: F didn't tip the ore in (input {st.InAmount}, carried {server.SandboxInventory(sp.Id).Materials[ore]}, \"{a.SandboxNotice}\")");
            if (!Await(() => Store(a) is { Busy: true, InAmount: > 0 } s && s.InMaterial == ore, 3, [a]))
                Fail($"refining network: the client never saw it work (\"{LookOf(a)}\"; host {st.InAmount} busy {st.Busy})");
            else if (!LookOf(a).Contains("refining iron ore to metal") || !LookOf(a).Contains("in "))
                Fail($"refining network: the client's look line: \"{LookOf(a)}\"");

            // A refused chute reaches the look line.
            w.Deposit(station, binIn, 20, wood);
            if (!Await(() => Store(a) is { Refused: var r } && r == wood, 4, [a]))
                Fail($"refining network: the client didn't hear the refusal (host {st.Refused}, client {Store(a)?.Refused})");
            else if (!LookOf(a).Contains("chute refused wood"))
                Fail($"refining network: the client's look line: \"{LookOf(a)}\"");
            w.Withdraw(station, binIn, 100);

            // The tier gate needs nothing new on the wire: the refused material and the client's own table say why.
            var copperOre = names.IdOf("copper ore");
            w.Deposit(station, binIn, 20, copperOre);
            if (!Await(() => Store(a) is { Refused: var r } && r == copperOre, 4, [a]))
                Fail($"refining network: the client didn't hear the tier refusal (host {st.Refused}, client {Store(a)?.Refused})");
            else if (!LookOf(a).Contains("chute refused copper ore: needs a tier 2 smelter"))
                Fail($"refining network: the client's look line for the tier gate: \"{LookOf(a)}\"");
            w.Withdraw(station, binIn, 100);

            // Stopped part way by its power: the client and a joiner both hear what it holds and weigh the same.
            station.Charge = 0;
            if (!Await(() => !st.Busy, 3, [a]))
                Fail("refining network: a flat battery didn't stop it");
            bool Same(Game g) => g.Constructs.Get(station.Id) is { } cc && Store(g) is { } s && s.InAmount == st.InAmount && s.InMaterial == st.InMaterial && s.Amount == st.Amount && s.Material == st.Material && s.Busy == st.Busy
                && MathF.Abs(cc.Mass - station.Mass) < 0.01f && Vector3.Distance(cc.Com, station.Com) < 0.001f && !cc.Dirty;
            if (!Await(() => Same(a), 4, [a]))
                Fail($"refining network: the client's copy (input {Store(a)?.InAmount}, output {Store(a)?.Amount}, {a.Constructs.Get(station.Id)?.Mass} kg) isn't the server's ({st.InAmount}, {st.Amount}, {station.Mass} kg)");
            Line($"refining network: stopped at {st.InAmount} ore in, {st.Amount} metal out: server {station.Mass:0.0} kg, client {a.Constructs.Get(station.Id)?.Mass:0.0} kg");
            if (st.InAmount == 0)
                Fail("refining network: the ore ran out before the battery was cut");
            using var late = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Late", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
            if (!Await(() => Same(late), 5, [a, late]))
                Fail($"refining network: the joiner's copy (input {Store(late)?.InAmount}, output {Store(late)?.Amount}, {late.Constructs.Get(station.Id)?.Mass} kg) isn't the server's ({st.InAmount}, {st.Amount}, {station.Mass} kg)");
            else if (!LookOf(late).Contains("no power"))
                Fail($"refining network: the joiner's look line: \"{LookOf(late)}\"");

            // Power back: it finishes, and everyone hears the metal down the chute.
            station.Charge = station.ChargeCapacity;
            var binOut = PartNamed(station, "Materials Bin", 1);
            CStore? OutBin(Game g) => g.Constructs.Get(station.Id) is { } cc ? g.Constructs.StoreOf(cc, binOut) : null;
            if (!Await(() => OutBin(a) is { Amount: 30 } o && o.Material == metal && OutBin(late) is { Amount: 30 } && Store(a) is { InAmount: 0, Amount: 0 } && Store(late) is { InAmount: 0, Amount: 0 }, 25, [a, late]))
                Fail($"refining network: 60 ore should end as 30 metal in the bin (client {OutBin(a)?.Amount}, joiner {OutBin(late)?.Amount}, host {w.StoreOf(station, binOut)?.Amount}, input {st.InAmount}, output {st.Amount})");
        }
        finally
        {
            sb.InfiniteParts = keep;
            yard.RestoreProps();
        }
    }
}
