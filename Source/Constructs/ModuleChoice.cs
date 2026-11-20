using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A faction's <c>"modules"</c> block (docs/sandbox/design/module-sockets.md 9.1): what it wants of a ship's modules.</summary>
public sealed class ModuleWants
{
    public readonly float[] Tags = new float[(int)ModTag.COUNT]; // weight of each tag's benefit
    public float Speed, Power, Fuel, Mass, Cost; // how much each trade-off matters
    public float Accel = 1.5f, PowerMargin = 1.15f; // needs: m/s2 a ship should reach, made over drawn
    public int MinGuns;
    public int Pick = 3; // chosen among the best N (variety)

    /// <summary>Reads the block; a bad one throws InvalidDataException with the key.</summary>
    public static ModuleWants Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("modules: an object of tags, stats, needs and pick");
        var w = new ModuleWants();
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "tags":
                    foreach (var t in p.Value.EnumerateObject())
                    {
                        var i = Array.IndexOf(ModuleSocketTable.TagNames, t.Name);
                        if (i < 0)
                            throw new InvalidDataException($"modules.tags.{t.Name}: no such tag ({string.Join(", ", ModuleSocketTable.TagNames)})");
                        w.Tags[i] = Num(t.Value, $"modules.tags.{t.Name}", 0, 10);
                    }
                    break;
                case "stats":
                    foreach (var t in p.Value.EnumerateObject())
                    {
                        var v = Num(t.Value, $"modules.stats.{t.Name}", 0, 10);
                        switch (t.Name)
                        {
                            case "speed": w.Speed = v; break;
                            case "power": w.Power = v; break;
                            case "fuel": w.Fuel = v; break;
                            case "mass": w.Mass = v; break;
                            case "cost": w.Cost = v; break;
                            default: throw new InvalidDataException($"modules.stats.{t.Name}: no such stat (speed, power, fuel, mass, cost)");
                        }
                    }
                    break;
                case "needs":
                    foreach (var t in p.Value.EnumerateObject())
                        switch (t.Name)
                        {
                            case "accel": w.Accel = Num(t.Value, "modules.needs.accel", 0, 100); break;
                            case "powerMargin": w.PowerMargin = Num(t.Value, "modules.needs.powerMargin", 0, 10); break;
                            case "minGuns": w.MinGuns = (int)Num(t.Value, "modules.needs.minGuns", 0, 64); break;
                            default: throw new InvalidDataException($"modules.needs.{t.Name}: no such need (accel, powerMargin, minGuns)");
                        }
                    break;
                case "pick":
                    w.Pick = (int)Num(p.Value, "modules.pick", 1, 16);
                    break;
                default:
                    throw new InvalidDataException($"modules.{p.Name}: no such key (tags, stats, needs, pick)");
            }
        return w;
    }

    private static float Num(JsonElement e, string key, float lo, float hi) =>
        e.ValueKind == JsonValueKind.Number && e.GetSingle() is var f && f >= lo && f <= hi ? f : throw new InvalidDataException($"{key} is a number from {lo} to {hi}");
}

/// <summary>One module design a faction could fit: its size, tags and stats (from a flat build of its blueprint).</summary>
public readonly record struct ModuleCandidate(string Name, int W, int L, ModTag Primary, ushort Tags, float Mass, float Thrust, float MadeKw, float DrawW, int Guns, int Stores, int Harvest);

/// <summary>What a ship has without the module being chosen: mass, power made and drawn, thrust, guns (9.2's denominators).</summary>
public struct ShipSummary
{
    public float Mass, MadeKw, DrawW, Thrust;
    public int Guns;

    /// <summary>Adds a chosen module.</summary>
    public void Add(in ModuleCandidate m)
    {
        Mass += m.Mass;
        MadeKw += m.MadeKw;
        DrawW += m.DrawW;
        Thrust += m.Thrust;
        Guns += m.Guns;
    }
}

/// <summary>
/// Faction and NPC module choice (docs/sandbox/design/module-sockets.md 9.2): pure functions over candidates and a ship summary.
/// Off the game thread or at a controller's coarse rate, never per tick.
/// </summary>
public static class ModuleChoice
{
    /// <summary>Does candidate <paramref name="m"/> fit a socket of size W x L and kind <paramref name="kind"/> (exact size; restricted kinds by primary tag; reactors fit generic sockets too)?</summary>
    public static bool Fits(in ModuleCandidate m, int w, int l, ModKind kind) =>
        m.W == w && m.L == l && kind switch
        {
            ModKind.Engine => m.Primary == ModTag.Engine,
            ModKind.Reactor => m.Primary == ModTag.Reactor,
            ModKind.Turret => m.Primary is ModTag.Turret or ModTag.TurretBay,
            _ => true,
        };

    /// <summary>A tag's benefit for <paramref name="m"/>: its stat against the best candidate's, 0..1 (a tag with no stat: 1 when it has it).</summary>
    private static float Benefit(ModTag t, in ModuleCandidate m, ReadOnlySpan<ModuleCandidate> all)
    {
        if ((m.Tags & (1 << (int)t)) == 0)
            return 0;
        static float Of(ModTag t, in ModuleCandidate c) => t switch
        {
            ModTag.Engine => c.Thrust,
            ModTag.Reactor => c.MadeKw,
            ModTag.Turret or ModTag.Missile => c.Guns,
            ModTag.Cargo => c.Stores,
            ModTag.Harvest => c.Harvest,
            _ => -1,
        };
        var mine = Of(t, m);
        if (mine < 0)
            return 1;
        var best = 0f;
        foreach (var c in all)
            best = MathF.Max(best, Of(t, c));
        return best > 0 ? mine / best : 1;
    }

    /// <summary>
    /// 9.2's score: the wanted tags' benefits, less the power the module would draw past the ship's spare (over the power made), its
    /// mass and its bill (the bill is its mass: a kilogram's worth, against the ship's), plus an engine's acceleration gain over the
    /// need (capped at 1: an engine that alone meets the need scores its full weight). Fuel isn't weighed yet (a module's burn isn't summed).
    /// </summary>
    public static float Score(ModuleWants w, in ModuleCandidate m, ReadOnlySpan<ModuleCandidate> all, in ShipSummary ship)
    {
        var s = 0f;
        for (var t = 0; t < (int)ModTag.COUNT; t++)
            if (w.Tags[t] > 0)
                s += w.Tags[t] * Benefit((ModTag)t, m, all);
        var spareKw = ship.MadeKw - ship.DrawW / 1000f * w.PowerMargin;
        var made = MathF.Max(1, ship.MadeKw + m.MadeKw);
        s -= w.Power * MathF.Max(0, m.DrawW / 1000f - MathF.Max(0, spareKw) - m.MadeKw) / made;
        var mass = MathF.Max(1, ship.Mass);
        s -= w.Mass * m.Mass / mass;
        s -= w.Cost * m.Mass / mass;
        if (m.Primary == ModTag.Engine && w.Accel > 0)
            s += w.Speed * MathF.Min(1, m.Thrust / (mass + m.Mass) / w.Accel); // (capped at the need: past it, more push is worth no more)
        return s;
    }

    /// <summary>
    /// Chooses for one socket: the candidates that fit, scored, one of the best <see cref="ModuleWants.Pick"/> by the dice. The index into
    /// <paramref name="all"/>, or -1. <paramref name="scores"/> gets each candidate's score (NaN: doesn't fit).
    /// </summary>
    public static int Choose(ModuleWants w, int sw, int sl, ModKind kind, ReadOnlySpan<ModuleCandidate> all, in ShipSummary ship, Random dice, Span<float> scores)
    {
        Span<int> best = stackalloc int[Math.Clamp(w.Pick, 1, 16)];
        var n = 0;
        for (var i = 0; i < all.Length; i++)
        {
            scores[i] = float.NaN;
            if (!Fits(all[i], sw, sl, kind))
                continue;
            scores[i] = Score(w, all[i], all, ship);
            // (keep the best N, best first)
            var at = n < best.Length ? n++ : best.Length;
            while (at > 0 && scores[best[at - 1]] < scores[i])
            {
                if (at < best.Length)
                    best[at] = best[at - 1];
                at--;
            }
            if (at < best.Length)
                best[at] = i;
        }
        return n == 0 ? -1 : best[dice.Next(n)];
    }

    /// <summary>The order sockets are filled in (9.2): engines, reactors, turrets, then generic.</summary>
    public static int Order(ModKind k) => k switch { ModKind.Engine => 0, ModKind.Reactor => 1, ModKind.Turret => 2, _ => 3 };

    /// <summary>A flat build's module 0 (the outer one: its plug on no plate) as a candidate, or null.</summary>
    public static ModuleCandidate? Of(Construct c, string name)
    {
        if (c.Dirty)
            c.Rebuild();
        for (var i = 0; i < c.Modules.Length; i++)
        {
            var m = c.Modules[i];
            if (ModuleSockets.PlateOf(c, m.Plug) >= 0)
                continue;
            var d = c.DefOf(m.Plug);
            // A module's stats count only its own parts; an adapter's or bay's submodules add theirs (they go with it).
            float mass = 0, thrust = 0, made = 0, draw = 0;
            int guns = 0, stores = 0, harvest = 0;
            ushort tags = 0;
            foreach (var x in c.Modules)
            {
                mass += x.Mass;
                thrust += x.Thrust;
                made += x.Made;
                draw += x.Draw;
                guns += x.Guns;
                stores += x.Stores;
                harvest += x.Harvest;
                tags |= x.Tags;
            }
            return new ModuleCandidate(name, d.ModW, d.ModL, m.Primary, tags, mass, thrust, made, draw, guns, stores, harvest);
        }
        return null;
    }

    /// <summary>
    /// <c>--module-choice SHIP --faction F [--seed N]</c>: builds the ship flat, then for each of its free sockets in 9.2's order scores
    /// every shipped module design that fits (built flat once each) for the faction, prints the table and the pick, and adds the pick to
    /// the ship's summary before the next socket. The needs (power margin, acceleration, guns) are checked and reported at the end.
    /// </summary>
    public static int Cli(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var shipName = Flag("--module-choice");
        var factionName = Flag("--faction");
        var seed = int.TryParse(Flag("--seed"), out var sd) ? sd : 1;
        if (shipName == null || factionName == null)
        {
            Console.WriteLine("--module-choice SHIP --faction F [--seed N]");
            return 1;
        }
        var faction = Array.Find(CrewFactions.All, f => f != null && f.Name.Equals(factionName, StringComparison.OrdinalIgnoreCase));
        if (faction == null)
        {
            Console.WriteLine($"no faction \"{factionName}\"");
            return 1;
        }
        if (faction.Modules == null)
        {
            Console.WriteLine($"{faction.Name} has no \"modules\" block: it chooses nothing");
            return 1;
        }
        var bp = File.Exists(shipName) ? ConstructBuild.ParseBlueprint(File.ReadAllText(shipName), Path.GetFileName(shipName)) : ConstructBuild.FindBlueprint(shipName);
        if (bp == null)
        {
            Console.WriteLine($"no blueprint \"{shipName}\"");
            return 1;
        }
        var w = new ConstructWorld { CreativeRecycling = true };
        var ship = ConstructBuild.Build(w, bp, Vector3.Zero, Quaternion.Identity, out var why);
        if (ship == null)
        {
            Console.WriteLine($"{bp.Name} doesn't build: {why}");
            return 1;
        }
        ship.Rebuild();
        var sum = new ShipSummary();
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (ModuleSockets.ModuleOfPart(ship, i) >= 0)
                continue; // (the modules it already has are counted as candidates would be, below)
            var d = ship.DefOf(i);
            sum.Mass += d.Mass;
            sum.DrawW += d.PowerDraw;
            if (d.Role == PartRole.Generator)
                sum.MadeKw += d.PlantRate;
            if (d.Role == PartRole.Thruster)
                sum.Thrust += d.ThrustForce + d.AbForce;
            if (d.Role == PartRole.Gun)
                sum.Guns++;
        }
        foreach (var m in ship.Modules)
            if (m.Socket >= 0)
                sum.Add(new ModuleCandidate("", 0, 0, m.Primary, m.Tags, m.Mass, m.Thrust, m.Made, m.Draw, m.Guns, m.Stores, m.Harvest));
        // The candidates: every shipped module design, built flat once.
        var list = new List<ModuleCandidate>();
        foreach (var mb in ConstructBuild.Blueprints)
        {
            if (mb.Module == null)
                continue;
            var cw = new ConstructWorld { CreativeRecycling = true };
            if (ConstructBuild.Build(cw, mb, Vector3.Zero, Quaternion.Identity, out var mwhy) is not { } mc)
            {
                Console.WriteLine($"  (module {mb.Name} doesn't build: {mwhy})");
                continue;
            }
            if (Of(mc, mb.Name) is { } cand)
                list.Add(cand);
        }
        var all = list.ToArray();
        Console.WriteLine($"{bp.Name} for {faction.Name} (seed {seed}): {ship.ModSockets.Length} sockets, {all.Length} module designs; ship {sum.Mass / 1000:0.#} t, makes {sum.MadeKw:0} kW, draws {sum.DrawW / 1000:0.#} kW, {sum.Thrust / 1000:0} kN, {sum.Guns} guns");
        var order = Enumerable.Range(0, ship.ModSockets.Length).Where(k => !ship.ModSockets[k].Stand && ship.ModSockets[k].Module < 0).OrderBy(k => Order(ship.ModSockets[k].Kind)).ThenBy(k => k).ToArray();
        var dice = new Random(seed);
        var scores = new float[all.Length];
        foreach (var k in order)
        {
            var s = ship.ModSockets[k];
            var pick = Choose(faction.Modules, s.W, s.L, s.Kind, all, sum, dice, scores);
            Console.WriteLine($"socket part {s.Plate}: {ModuleSocketTable.SizeText(s.W, s.L)} {s.Kind}");
            for (var i = 0; i < all.Length; i++)
                if (!float.IsNaN(scores[i]))
                    Console.WriteLine(FormattableString.Invariant($"  {(i == pick ? "*" : " ")} {scores[i],7:0.000}  {all[i].Name} ({ModuleSockets.TagName(all[i].Primary)}, {all[i].Mass:0} kg, {all[i].Thrust / 1000:0} kN, makes {all[i].MadeKw:0} kW, draws {all[i].DrawW / 1000:0.#} kW, {all[i].Guns} guns)"));
            if (pick < 0)
                Console.WriteLine("    nothing fits");
            else
                sum.Add(all[pick]);
        }
        var wants = faction.Modules;
        var accel = sum.Mass > 0 ? sum.Thrust / sum.Mass : 0;
        Console.WriteLine(FormattableString.Invariant($"fitted: {sum.Mass / 1000:0.#} t, makes {sum.MadeKw:0} kW for {sum.DrawW / 1000:0.#} kW drawn (wants x{wants.PowerMargin:0.##}), {accel:0.##} m/s2 (wants {wants.Accel:0.##}), {sum.Guns} guns (wants {wants.MinGuns})"));
        var shortOf = new List<string>();
        if (sum.DrawW > 0 && sum.MadeKw * 1000 < sum.DrawW * wants.PowerMargin)
            shortOf.Add("power");
        if (accel < wants.Accel)
            shortOf.Add("acceleration");
        if (sum.Guns < wants.MinGuns)
            shortOf.Add("guns");
        Console.WriteLine(shortOf.Count == 0 ? "needs: met" : $"needs: short of {string.Join(", ", shortOf)} (the passes that swap for them aren't built yet)");
        return 0;
    }
}
