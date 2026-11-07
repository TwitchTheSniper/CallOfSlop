using System.Diagnostics;
using System.Globalization;

namespace CallOfSlop.VehicleKit;

/// <summary>A convoy's member: its place ("leader", "escort", "cargo"), its model, and its lane (0 the middle; -1 and 1 a wedge's sides).</summary>
public sealed record ConvoyMember(string Slot, VehicleModel Model, int Lane);

/// <summary>
/// A convoy as planned (<see cref="VehicleConvoys.Plan"/>): nothing made yet, only who goes where, so planning costs nothing and the same seed always plans the same.
/// </summary>
public sealed record ConvoyPlan(FactionStyle Style, int Seed, int Tier, int Scheme, string Formation, IReadOnlyList<ConvoyMember> Members)
{
    /// <summary>How many model families it fields (the leader's included).</summary>
    public int Families => Members.Select(m => (m.Model.Role, m.Model.Seed)).Distinct().Count();

    public string Label => $"{Style.Name} convoy {Seed}: {Members.Count} vehicles, {Formation}, tier {Tier}, {Families} families, scheme {Scheme}";
}

/// <summary>
/// Convoys (docs/vehiclekit/VEHICLEKIT.md, "Families and convoys"): a coherent group of a style's vehicles. A leader (a command vehicle, or a boss, at a tier up), escorts
/// (raiders or scouts) and cargo (a tanker, a recovery truck or a carrier); one paint scheme rolled from the style for all of them (the wear may still vary: each member
/// takes its own wear seed); mostly one or two model families, so it reads as a fleet; set down in a column or a wedge behind the leader, spaced by each vehicle's length
/// (<see cref="VehicleSpawner"/> in the game). Planning is pure: the seed picks everything, and every member is a <see cref="VehicleModel"/> whose key spawns it again.
/// </summary>
public static class VehicleConvoys
{
    public const int MinCount = 2, MaxCount = 6;

    /// <summary>The gap between one member's tail and the next one's nose (m), and how far a wedge's side lanes stand out (m).</summary>
    public const float Gap = 5, Lane = 7;

    /// <summary>A seed derived from a convoy's for one of its choices (a family's seed, the scheme, a member's wear).</summary>
    public static int Derive(int seed, int what) => VehicleSpawnKit.Reroll(seed, 1000 + what);

    private static readonly string[] EscortRoles = ["raider", "scout"];
    private static readonly (string Role, double W)[] CargoRoles = [("tanker", 3), ("recovery", 1), ("carrier", 1)];

    /// <summary>
    /// Plans a convoy of <paramref name="count"/> (2 to 6) of <paramref name="st"/>'s vehicles at <paramref name="tier"/> from <paramref name="seed"/>. <paramref name="mix"/>:
    /// the roles of the members behind the leader, in order (one family a role); null: the seed's own mix. Throws when the style fields none of a role.
    /// </summary>
    public static ConvoyPlan Plan(FactionStyle st, int count, int tier, int seed, IReadOnlyList<string>? mix = null)
    {
        tier = Math.Clamp(tier, 1, VehicleRoles.MaxTier);
        var d = Dice.Of(seed).Fork("convoy " + st.Name);
        var fielded = VehicleRoles.FieldedBy(st);
        bool Fields(string role) => VehicleRoles.Find(role) is { } r && VehicleRoles.ShapesFor(st, r).Count > 0;
        string Weighted(IEnumerable<(string Role, double W)> list, Dice dd)
        {
            var ok = list.Where(x => x.W > 0 && Fields(x.Role)).ToList();
            if (ok.Count == 0)
                throw new VehicleKitException($"the {st.Name} style fields none of {string.Join(", ", list.Select(x => x.Role))}");
            var roll = dd.Next() * ok.Sum(x => x.W);
            foreach (var (role, w) in ok)
                if ((roll -= w) < 0)
                    return role;
            return ok[^1].Role;
        }
        var scheme = Derive(seed, 0);
        var used = new HashSet<string>(StringComparer.Ordinal);
        int Family(int what, string role) // (a family's seed; another when its model name is one the convoy fields already, so no two lines share a name)
        {
            var s = Derive(seed, what);
            for (var k = 1; k <= 8 && !used.Add(VehicleFamilies.ModelName(new VehicleModel(st.Name, role, tier, s))); k++)
                s = Derive(seed, what + 100 * k);
            return s;
        }
        var lead = Weighted([("boss", tier >= 3 ? 0.5 : 0.12), ("command", 1)], d.Fork("leader"));
        var leader = new VehicleModel(st.Name, lead, Math.Min(VehicleRoles.MaxTier, tier + 1), Family(1, lead), Scheme: scheme);
        var behind = new List<(string Slot, VehicleModel M)>();
        if (mix is { Count: > 0 })
        {
            var families = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in mix.Take(MaxCount - 1))
            {
                var role = VehicleRoles.Find(r) ?? throw new VehicleKitException($"no role \"{r}\": {string.Join(", ", VehicleRoles.All.Select(x => x.Name))}");
                if (!families.TryGetValue(role.Name, out var fs))
                    families[role.Name] = fs = Family(20 + families.Count, role.Name);
                var twins = behind.Count(b => b.M.Role == role.Name && b.M.Seed == fs);
                behind.Add((role.Name is "raider" or "scout" or "gun platform" ? "escort" : "cargo", new VehicleModel(st.Name, role.Name, tier, fs, twins == 1 && role.Combat ? 2 : 1, Scheme: scheme)));
            }
        }
        else
        {
            count = Math.Clamp(count, MinCount, MaxCount);
            var cargo = count >= 5 ? 2 : count >= 3 ? 1 : 0;
            var escorts = count - 1 - cargo;
            // Escorts: one family (a raider or a scout line), a Mk2 among them now and then, a variant on one; past three, sometimes a second line.
            var escortRole = Weighted(EscortRoles.Select(r => (r, fielded.GetValueOrDefault(r))), d.Fork("escorts"));
            var other = EscortRoles.First(r => r != escortRole);
            var second = escorts >= 3 && Fields(other) && d.Fork("second line").Chance(0.4);
            var es = Family(2, escortRole);
            var md = d.Fork("marks");
            var variantAt = md.Chance(0.35) && escorts >= 2 ? md.Int(1, escorts - 1) : -1;
            for (var k = 0; k < escorts; k++)
            {
                if (second && k == escorts - 1)
                {
                    behind.Add(("escort", new VehicleModel(st.Name, other, tier, Family(4, other), Scheme: scheme)));
                    continue;
                }
                var mark = k == 0 && escorts >= 2 && md.Chance(0.6) ? 2 : 1;
                var variant = k == variantAt ? md.Pick([VehicleFamilies.GunTruck, VehicleFamilies.UpArmoured]) : "";
                behind.Add(("escort", new VehicleModel(st.Name, escortRole, tier, es, mark, variant, scheme)));
            }
            // Cargo: one family; a second of a tanker line is a twin, of any other line its tanker conversion.
            if (cargo > 0)
            {
                var cargoRole = Weighted(CargoRoles.Select(c => (c.Role, c.W * Math.Max(0.2, fielded.GetValueOrDefault(c.Role)))), d.Fork("cargo"));
                var cs = Family(3, cargoRole);
                for (var k = 0; k < cargo; k++)
                    behind.Add(("cargo", new VehicleModel(st.Name, cargoRole, tier, cs, 1, k > 0 && cargoRole != "tanker" ? VehicleFamilies.TankerConversion : "", scheme)));
            }
            // The order: half the escorts first, then the cargo, then the rest guarding the rear.
            var front = behind.Where(b => b.Slot == "escort").Take((escorts + 1) / 2).ToList();
            behind = front.Concat(behind.Where(b => b.Slot == "cargo")).Concat(behind.Where(b => b.Slot == "escort").Skip(front.Count)).ToList();
        }
        var wedge = behind.Count >= 3 && d.Fork("formation").Chance(0.35);
        var members = new List<ConvoyMember> { new("leader", leader, 0) };
        for (var k = 0; k < behind.Count; k++)
        {
            var (slot, m) = behind[k];
            // Every member its own wear (the grammar's shapes: a kit-1 shape has no layers, and twins of it are twins).
            members.Add(new ConvoyMember(slot, m with { Layers = new VehicleLayers(Wear: Derive(seed, 10 + k)) }, wedge ? (k % 2 == 0 ? -1 : 1) : 0));
        }
        return new ConvoyPlan(st, seed, tier, scheme, wedge ? "wedge" : "column", members);
    }

    /// <summary>
    /// A convoy's paint scheme: for each of the style's palette roles but its rust, one of its colours (two of a role that lists four or more), from the scheme's seed.
    /// Every colour is the style's own, so identity's palette score holds; the same scheme seed always gives the same scheme.
    /// </summary>
    public static Dictionary<string, string[]> SchemeOf(FactionStyle st, int scheme)
    {
        var s = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (role, list) in st.Palette.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (role == "rust")
                continue; // (rust is what wear paints: it stays the style's)
            var distinct = list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var take = distinct.Count >= 4 ? 2 : 1;
            s[role] = Dice.Of(scheme).Fork("scheme " + role).Shuffled(distinct).Take(take).ToArray();
        }
        return s;
    }

    public static string SchemeLabel(FactionStyle st, int scheme) => string.Join(", ", SchemeOf(st, scheme).Select(kv => $"{kv.Key} {string.Join("+", kv.Value)}"));

    /// <summary>A rough length (m) for a shape, front and rear of its origin, for a member set down before its own is known (a committed stand-in).</summary>
    public static (float Front, float Rear) Estimate(string shape) => shape switch
    {
        "Technical" => (2.5f, 3.5f),
        "War Bus" or "Double Decker" => (3.5f, 8.5f),
        _ => (3.5f, 6.5f),
    };
}

/// <summary>The command line for families and convoys: <c>--family "Style Role"</c> and <c>--convoy "Style"</c>.</summary>
public static class VehicleFamiliesCli
{
    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

    private static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static FactionStyle? StyleNamed(string name) => Styles.All.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || s.Group.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private sealed record Made(string What, VehicleModel M, Vehicle? V, string Text, VehicleDiversity.Features? F, string Error, double Ms);

    private static Made MakeOne(string what, VehicleModel m, bool features)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var v = VehicleFamilies.Make(m);
            var text = v.Text();
            var ms = clock.Elapsed.TotalMilliseconds;
            var (_, _, g, _, _) = VehicleFamilies.Resolve(m);
            v.Identity ??= VehicleIdentity.Of(v);
            var f = features ? VehicleDiversity.Of(g, m.Seed, v, text, new()) : null;
            return new Made(what, m, v, text, f, "", ms);
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
            return new Made(what, m, null, "", null, ex.Message, clock.Elapsed.TotalMilliseconds);
        }
    }

    private static void Print(Made o)
    {
        if (o.V is not { } v)
        {
            Console.WriteLine($"  {o.What,-22} {o.M.Key}: doesn't generate: {o.Error}");
            return;
        }
        Console.WriteLine($"  {o.What,-22} {v.Name}: {VehicleFamilies.Resolve(o.M).Ask.Shape}, plan {v.PlanAttempt + 1}, {o.Ms:0} ms, {(v.Fit?.Met == true ? "role met" : "role MISSED " + string.Join("; ", v.Fit?.Misses ?? []))}");
        Console.WriteLine($"    key {o.M.Key}");
        if (v.Fit != null)
            Console.WriteLine($"    {v.Fit}");
        Console.WriteLine($"    identity {v.Identity}");
        if (v.MarksPlaced.Count > 0)
            Console.WriteLine($"    placed: {string.Join("; ", v.MarksPlaced)}");
    }

    /// <summary>
    /// <c>--family "Style Role" [--seed S] [--tier T] [--check]</c>: the family's Mk1 to Mk3 and its variants (of Mk1), a named unique of it, and two other families' Mk1 of the
    /// same role, each with its distance to Mk1 (<see cref="VehicleDiversity"/>) and the matrix; then the mean within the family against the mean to the others. A
    /// family reads as one when its members are closer to each other than to another family's. Exit code: members that failed, plus 1 when the family isn't closer.
    /// </summary>
    public static int Family(string text, string[] args)
    {
        FactionStyle? st = null;
        VehicleRole? role = null;
        foreach (var s in Styles.All)
            foreach (var prefix in new[] { s.Name, s.Group })
                if (st == null && text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                    (st, role) = (s, VehicleRoles.Find(text[(prefix.Length + 1)..]));
        if (st == null || role == null)
        {
            Console.WriteLine($"--family \"{text}\": a style and a role, \"Bandit raider\". Roles: {string.Join(", ", VehicleRoles.All.Select(r => r.Name))}");
            return 1;
        }
        var seed = int.TryParse(Flag(args, "--seed"), out var sd) ? Math.Max(1, sd) : 1;
        var tier = int.TryParse(Flag(args, "--tier"), out var t) ? Math.Clamp(t, 1, VehicleRoles.MaxTier) : 1;
        var plain = new VehicleModel(st.Name, role.Name, tier, seed, Named: false);
        var list = new List<(string What, VehicleModel M, bool Family)>();
        for (var mk = 1; mk <= VehicleFamilies.Marks; mk++)
            list.Add(($"Mk{mk}", plain with { Mark = mk }, true));
        foreach (var variant in VehicleFamilies.Variants)
            list.Add((variant, plain with { Variant = variant }, true));
        list.Add(("named unique", plain with { Named = true }, true));
        for (var k = 1; k <= 2; k++)
            list.Add(($"other family {k}", plain with { Seed = VehicleConvoys.Derive(seed, 500 + k) }, false));
        var rolled = Enumerable.Range(1, 400).Count(s => VehicleFamilies.RollNamed(plain with { Seed = s, Named = null }));
        Console.WriteLine($"{st.Name} {role.Name} family {seed}, base tier {tier}: Mk1 at tier {tier}, Mk2 {Math.Min(4, tier + 1)}, Mk3 {Math.Min(4, tier + 2)}; seed {seed} is {(VehicleFamilies.RollNamed(plain with { Named = null }) ? "" : "not ")}a named unique ({rolled} of seeds 1-400 are)");
        var made = list.Select(x => MakeOne(x.What, x.M, true)).ToList();
        var bad = 0;
        foreach (var o in made)
        {
            Print(o);
            if (o.V is { } v && Has(args, "--check")) // (the full check, as --sweep-vehicle runs it: seconds a member)
            {
                var r = VehicleChecks.Check(o.Text, VehicleRegistry.Slug(v.Name) + ".json", VehicleFamilies.Resolve(o.M).G.Checks, v.Rooms);
                bad += r.Passed ? 0 : 1;
                Console.WriteLine(r.Passed ? "    check: passed" : $"    check: FAILED {string.Join("; ", r.Failures)}");
            }
        }
        var ok = made.Where(o => o.F != null).ToList();
        bad += made.Count - ok.Count;
        if (ok.Count < 2)
            return bad + 1;
        Console.WriteLine();
        Console.WriteLine("  distances (0 the same vehicle, 1 nothing shared; under 0.08 reads as one):");
        Console.WriteLine($"  {"",-22} {string.Join(" ", ok.Select((_, i) => $"{i,5}"))}");
        for (var i = 0; i < ok.Count; i++)
            Console.WriteLine($"  {i,2} {ok[i].What,-19} {string.Join(" ", ok.Select(o => $"{VehicleDiversity.Distance(ok[i].F!, o.F!),5:0.000}"))}");
        var fam = ok.Where(o => list.First(x => x.What == o.What).Family).ToList();
        var others = ok.Except(fam).ToList();
        var within = new List<float>();
        for (var i = 0; i < fam.Count; i++)
            for (var j = i + 1; j < fam.Count; j++)
                within.Add(VehicleDiversity.Distance(fam[i].F!, fam[j].F!));
        var between = (from x in fam from y in others select VehicleDiversity.Distance(x.F!, y.F!)).ToList();
        var mk1 = fam.FirstOrDefault(o => o.What == "Mk1");
        if (mk1 != null)
            foreach (var o in fam.Where(o => o != mk1))
            {
                var p = VehicleDiversity.Between(mk1.F!, o.F!);
                Console.WriteLine($"  Mk1 to {o.What}: {p.Total:0.000} (silhouette {p.Silhouette:0.000}, parts {p.Kinds:0.000}, layout {p.Layout:0.000}, size {p.Size:0.000}){(o.V!.PlanAttempt != mk1.V!.PlanAttempt ? $"; ANOTHER PLAN ({o.V.PlanAttempt + 1} against {mk1.V.PlanAttempt + 1}): another body" : "")}");
            }
        var w = within.Count == 0 ? 0 : within.Average();
        var b = between.Count == 0 ? 0 : between.Average();
        Console.WriteLine($"  within the family: mean {w:0.000} (worst {(within.Count == 0 ? 0 : within.Max()):0.000}); to the other families: mean {b:0.000} (least {(between.Count == 0 ? 0 : between.Min()):0.000}): {(w < b ? "the family reads as one" : "NOT CLOSER than another family")}");
        return bad + (w < b ? 0 : 1);
    }

    /// <summary>What reaches within 10 cm of the wheels' bottoms, as built from the text: a part that drags tilts a vehicle and stops it.</summary>
    private static string LowParts(string text)
    {
        var bp = ConstructBuild.ParseBlueprint(text, "low.json");
        var c = ConstructBuild.BuildOnGround(new ConstructWorld(), bp, System.Numerics.Vector3.Zero, 0, out var why);
        if (c == null)
            return "it doesn't build: " + why;
        var low = float.MaxValue;
        for (var k = 0; k < c.WheelCount; k++)
            low = MathF.Min(low, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        var hits = new List<string>();
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.DefOf(i).Role == PartRole.Wheel)
                continue;
            c.PartBounds(i, out var a, out var b);
            if (a.Y < low + 0.1f)
                hits.Add($"{c.DefOf(i).Name} {a.Y - low:+0.00;-0.00} m at z {(a.Z + b.Z) * 0.5f:0.0}");
        }
        var wheels = string.Join(", ", Enumerable.Range(0, c.WheelCount).Select(k => $"{c.DefOf(c.Wheels[k].Part).Name} at z {c.Wheels[k].Centre.Z:0.0} carries {c.Wheels[k].MaxLoad:0} kg"));
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            c.PartBounds(w.Part, out var wa, out var wb);
            for (var i = 0; i < c.PartCount; i++)
            {
                if (i == w.Part || i == w.Suspension || c.DefOf(i).Role is PartRole.Wheel or PartRole.Suspension or PartRole.Differential)
                    continue;
                c.PartBounds(i, out var a, out var b);
                if (a.X < wb.X - 0.02f && b.X > wa.X + 0.02f && a.Y < wb.Y - 0.02f && b.Y > wa.Y + 0.02f && a.Z < wb.Z - 0.02f && b.Z > wa.Z + 0.02f)
                    hits.Add($"{c.DefOf(i).Name} in wheel {k}'s box (top {wb.Y:0.00}, its bottom {a.Y:0.00})");
            }
        }
        return (hits.Count == 0 ? "none" : string.Join(", ", hits)) + $"; {c.Mass:0} kg, centre of mass z {c.Com.Z:0.0}; wheels: {wheels}";
    }

    /// <summary>
    /// <c>--convoy "Style" [--seed S] [--count N] [--tier T] [--mix raider,tanker] [--no-check]</c>: the convoy's plan (members, roles, models, the scheme), then each member
    /// made, measured and checked (the full check, as <c>--sweep-vehicle</c> runs it: seconds a member; <c>--no-check</c> skips it), with where it stands in the formation;
    /// the leader is made twice and must write the same text. Exit code: members that failed to generate or failed the check, plus 1 when the leader differs.
    /// </summary>
    public static int Convoy(string text, string[] args)
    {
        if (StyleNamed(text) is not { } st)
        {
            Console.WriteLine($"--convoy \"{text}\": a style ({string.Join(", ", Styles.All.Select(s => s.Name))})");
            return 1;
        }
        var seed = int.TryParse(Flag(args, "--seed"), out var sd) ? Math.Max(1, sd) : 1;
        var count = int.TryParse(Flag(args, "--count"), out var n) ? n : 5;
        var tier = int.TryParse(Flag(args, "--tier"), out var t) ? t : 2;
        var mix = Flag(args, "--mix")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var plan = VehicleConvoys.Plan(st, count, tier, seed, mix);
        Console.WriteLine(plan.Label);
        Console.WriteLine($"  scheme {plan.Scheme}: {VehicleConvoys.SchemeLabel(st, plan.Scheme)}");
        foreach (var m in plan.Members)
            Console.WriteLine($"  {m.Slot,-7} lane {m.Lane,2}  {VehicleFamilies.DisplayName(m.Model),-40} {m.Model.Key}");
        var check = !Has(args, "--no-check");
        var bad = 0;
        var made = new List<Made>();
        var back = new float[3];
        foreach (var m in plan.Members)
        {
            var o = MakeOne(m.Slot, m.Model, false);
            made.Add(o);
            Print(o);
            if (o.V is not { } v)
            {
                bad++;
                continue;
            }
            var (front, rear) = v.ExtentZ();
            var at = made.Count == 1 ? 0 : back[m.Lane + 1] + VehicleConvoys.Gap + front;
            if (made.Count == 1)
                back[0] = back[1] = back[2] = rear;
            else
                back[m.Lane + 1] = at + rear;
            Console.WriteLine($"    stands {at:0.0} m behind the leader's origin{(m.Lane != 0 ? $", {m.Lane * VehicleConvoys.Lane:+0;-0} m to the side" : "")}; {front + rear:0.0} m long");
            if (Flag(args, "--write") is { } dir)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, VehicleRegistry.Slug(v.Name) + ".json"), o.Text);
            }
            if (check)
            {
                var g = VehicleFamilies.Resolve(m.Model).G;
                var r = VehicleChecks.Check(o.Text, VehicleRegistry.Slug(v.Name) + ".json", g.Checks, v.Rooms);
                bad += r.Passed ? 0 : 1;
                Console.WriteLine(r.Passed ? "    check: passed" : $"    check: FAILED {string.Join("; ", r.Failures)}");
                if (!r.Passed)
                    Console.WriteLine($"    low parts: {LowParts(o.Text)}");
            }
        }
        if (made[0].V != null)
        {
            var again = VehicleFamilies.Make(plan.Members[0].Model).Text();
            var same = again == made[0].Text;
            Console.WriteLine($"  the leader made again: {(same ? "the same text" : "DIFFERENT TEXT")}");
            bad += same ? 0 : 1;
        }
        var ok = made.Where(o => o.V != null).ToList();
        Console.WriteLine($"  {ok.Count}/{made.Count} generated, {ok.Count(o => o.V!.Fit?.Met == true)} met their role, identity mean {(ok.Count == 0 ? 0 : ok.Average(o => o.V!.Identity!.Total)):0.00}, {made.Sum(o => o.Ms):0} ms in all");
        return bad;
    }
}
