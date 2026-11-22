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
public readonly record struct ModuleCandidate(string Name, int W, int L, ModTag Primary, ushort Tags, float Mass, float Thrust, float MadeKw, float DrawW, int Guns, int Stores, int Harvest, bool Round = false);

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

    /// <summary>Takes a chosen module off again.</summary>
    public void Remove(in ModuleCandidate m)
    {
        Mass -= m.Mass;
        MadeKw -= m.MadeKw;
        DrawW -= m.DrawW;
        Thrust -= m.Thrust;
        Guns -= m.Guns;
    }
}

/// <summary>A free socket to fill: its size and kind.</summary>
public readonly record struct ModuleSlot(int W, int L, ModKind Kind, bool Round = false);

/// <summary>
/// Faction and NPC module choice (docs/sandbox/design/module-sockets.md 9.2): pure functions over candidates and a ship summary.
/// Off the game thread or at a controller's coarse rate, never per tick.
/// </summary>
public static class ModuleChoice
{
    /// <summary>Does candidate <paramref name="m"/> fit a socket of size W x L and kind <paramref name="kind"/> (exact size; restricted kinds by primary tag; reactors fit generic sockets too)?</summary>
    public static bool Fits(in ModuleCandidate m, int w, int l, ModKind kind, bool round = false) =>
        m.W == w && m.L == l && m.Round == round && kind switch
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
    public static int Choose(ModuleWants w, int sw, int sl, ModKind kind, ReadOnlySpan<ModuleCandidate> all, in ShipSummary ship, Random dice, Span<float> scores, bool round = false)
    {
        Span<int> best = stackalloc int[Math.Clamp(w.Pick, 1, 16)];
        var n = 0;
        for (var i = 0; i < all.Length; i++)
        {
            scores[i] = float.NaN;
            if (!Fits(all[i], sw, sl, kind, round))
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

    /// <summary>The needs a ship falls short of (9.2's floors): bit 0 power, bit 1 acceleration, bit 2 guns.</summary>
    public const int ShortPower = 1, ShortAccel = 2, ShortGuns = 4;

    /// <summary>How far short of each need the ship is: power in W, acceleration in m/s2, guns (0 each when met).</summary>
    public static (float Power, float Accel, float Guns) Deficit(ModuleWants w, in ShipSummary s) =>
        (MathF.Max(0, s.DrawW * w.PowerMargin - s.MadeKw * 1000),
         MathF.Max(0, w.Accel - (s.Mass > 0 ? s.Thrust / s.Mass : 0)),
         MathF.Max(0, w.MinGuns - s.Guns));

    /// <summary>The short-of bits of <see cref="Deficit"/>.</summary>
    public static int Short(ModuleWants w, in ShipSummary s)
    {
        var (p, a, g) = Deficit(w, s);
        return (p > 0 ? ShortPower : 0) | (a > 0 ? ShortAccel : 0) | (g > 0 ? ShortGuns : 0);
    }

    /// <summary>
    /// Fills every slot (9.2): one pass in <see cref="Order"/>, each pick added to the ship before the next socket; then up to three
    /// passes on the needs. In each, for every need still short (power, then acceleration, then guns), the one swap of one socket's
    /// module for another that fits which most cuts that shortfall without making another need worse is made (ties: the better
    /// score). So a ship short of power trades its hungriest module for a frugal one or a generic socket's cargo for a reactor, one
    /// short of push takes stronger engines, one short of guns turns a generic socket into a gun module. Each slot's pick (an index
    /// into <paramref name="all"/>, -1: nothing fits); <paramref name="sum"/> ends as the fitted ship. <paramref name="log"/> hears
    /// each socket's table and each swap.
    /// </summary>
    public static int[] Fill(ModuleWants w, ReadOnlySpan<ModuleSlot> slots, ModuleCandidate[] all, ref ShipSummary sum, Random dice, Action<string>? log = null)
    {
        var picks = new int[slots.Length];
        var order = new int[slots.Length];
        for (var k = 0; k < order.Length; k++)
            order[k] = k;
        var sl = slots.ToArray();
        Array.Sort(order, (a, b) => Order(sl[a].Kind) != Order(sl[b].Kind) ? Order(sl[a].Kind).CompareTo(Order(sl[b].Kind)) : a.CompareTo(b));
        var scores = new float[all.Length];
        foreach (var k in order)
        {
            var s = sl[k];
            var pick = picks[k] = Choose(w, s.W, s.L, s.Kind, all, sum, dice, scores, s.Round);
            if (log != null)
            {
                log($"socket {k}: {ModuleSocketTable.SizeText(s.W, s.L, s.Round)} {s.Kind}");
                for (var i = 0; i < all.Length; i++)
                    if (!float.IsNaN(scores[i]))
                        log(FormattableString.Invariant($"  {(i == pick ? "*" : " ")} {scores[i],7:0.000}  {all[i].Name} ({ModuleSockets.TagName(all[i].Primary)}, {all[i].Mass:0} kg, {all[i].Thrust / 1000:0} kN, makes {all[i].MadeKw:0} kW, draws {all[i].DrawW / 1000:0.#} kW, {all[i].Guns} guns)"));
                if (pick < 0)
                    log("    nothing fits");
            }
            if (pick >= 0)
                sum.Add(all[pick]);
        }
        for (var pass = 0; pass < 3; pass++)
        {
            var any = false;
            for (var need = 0; need < 3; need++)
            {
                var before = Deficit(w, sum);
                var cur = need == 0 ? before.Power : need == 1 ? before.Accel : before.Guns;
                if (cur <= 0)
                    continue;
                int bestK = -1, bestC = -1;
                float bestCut = 0, bestScore = float.NegativeInfinity;
                for (var k = 0; k < sl.Length; k++)
                {
                    var trial = sum;
                    if (picks[k] >= 0)
                        trial.Remove(all[picks[k]]);
                    for (var c = 0; c < all.Length; c++)
                    {
                        if (c == picks[k] || !Fits(all[c], sl[k].W, sl[k].L, sl[k].Kind, sl[k].Round))
                            continue;
                        var t = trial;
                        t.Add(all[c]);
                        var after = Deficit(w, t);
                        var now = need == 0 ? after.Power : need == 1 ? after.Accel : after.Guns;
                        var cut = cur - now;
                        // (another need may not get worse: a swap for guns that drops the ship below its power floor is no fix)
                        if (cut <= 0 || after.Power > before.Power + 1e-3f || after.Accel > before.Accel + 1e-5f || after.Guns > before.Guns)
                            continue;
                        var score = Score(w, all[c], all, trial);
                        if (cut > bestCut * 1.0001f || (cut >= bestCut * 0.9999f && score > bestScore))
                            (bestK, bestC, bestCut, bestScore) = (k, c, cut, score);
                    }
                }
                if (bestK < 0)
                    continue;
                log?.Invoke($"pass {pass + 1}: short of {(need == 0 ? "power" : need == 1 ? "acceleration" : "guns")}: socket {bestK} takes {all[bestC].Name}{(picks[bestK] >= 0 ? $" for {all[picks[bestK]].Name}" : "")}");
                if (picks[bestK] >= 0)
                    sum.Remove(all[picks[bestK]]);
                picks[bestK] = bestC;
                sum.Add(all[bestC]);
                any = true;
            }
            if (!any)
                break;
        }
        return picks;
    }

    /// <summary>Every shipped module design (a blueprint with a <c>"module"</c> block), built flat once each, as candidates.</summary>
    public static ModuleCandidate[] Shipped(Action<string>? log = null)
    {
        var list = new List<ModuleCandidate>();
        foreach (var mb in ConstructBuild.Blueprints)
        {
            if (mb.Module == null || mb.Module.Cells) // a console faceplate isn't a ship module to choose
                continue;
            var cw = new ConstructWorld { CreativeRecycling = true };
            if (ConstructBuild.Build(cw, mb, Vector3.Zero, Quaternion.Identity, out var mwhy) is not { } mc)
            {
                log?.Invoke($"  (module {mb.Name} doesn't build: {mwhy})");
                continue;
            }
            if (Of(mc, mb.Name) is { } cand)
                list.Add(cand);
        }
        return [.. list];
    }

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
            return new ModuleCandidate(name, d.ModW, d.ModL, m.Primary, tags, mass, thrust, made, draw, guns, stores, harvest, d.ModRound);
        }
        return null;
    }

    /// <summary>
    /// <c>--module-choice SHIP --faction F [--seed N]</c>: builds the ship flat, then for each of its free sockets in 9.2's order scores
    /// every shipped module design that fits (built flat once each) for the faction, prints the table and the pick, and adds the pick to
    /// the ship's summary before the next socket; then <see cref="Fill"/>'s passes swap for the needs, each swap printed.
    /// </summary>
    public static int Cli(string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var shipName = Flag("--module-choice");
        var factionName = Flag("--faction");
        var seed = int.TryParse(Flag("--seed"), out var sd) ? sd : 1;
        if (shipName == null || factionName == null)
        {
            Console.WriteLine("--module-choice SHIP --faction F [--seed N] [--needs accel=N,powerMargin=N,minGuns=N]");
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
        var wants = faction.Modules;
        if (Flag("--needs") is { } nd)
        {
            // (a try of the passes: the faction's needs replaced for this run, "accel=N,powerMargin=N,minGuns=N")
            wants = new ModuleWants { Pick = faction.Modules.Pick };
            Array.Copy(faction.Modules.Tags, wants.Tags, wants.Tags.Length);
            (wants.Speed, wants.Power, wants.Fuel, wants.Mass, wants.Cost) = (faction.Modules.Speed, faction.Modules.Power, faction.Modules.Fuel, faction.Modules.Mass, faction.Modules.Cost);
            (wants.Accel, wants.PowerMargin, wants.MinGuns) = (faction.Modules.Accel, faction.Modules.PowerMargin, faction.Modules.MinGuns);
            foreach (var kv in nd.Split(','))
                if (kv.Split('=') is [var k, var v] && float.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    switch (k)
                    {
                        case "accel": wants.Accel = f; break;
                        case "powerMargin": wants.PowerMargin = f; break;
                        case "minGuns": wants.MinGuns = (int)f; break;
                    }
        }
        var all = Shipped(Console.WriteLine);
        Console.WriteLine($"{bp.Name} for {faction.Name} (seed {seed}): {ship.ModSockets.Length} sockets, {all.Length} module designs; ship {sum.Mass / 1000:0.#} t, makes {sum.MadeKw:0} kW, draws {sum.DrawW / 1000:0.#} kW, {sum.Thrust / 1000:0} kN, {sum.Guns} guns");
        var free = Enumerable.Range(0, ship.ModSockets.Length).Where(k => !ship.ModSockets[k].Stand && !ship.ModSockets[k].Cells && ship.ModSockets[k].Module < 0).ToArray();
        var slots = free.Select(k => new ModuleSlot(ship.ModSockets[k].W, ship.ModSockets[k].L, ship.ModSockets[k].Kind, ship.ModSockets[k].Round)).ToArray();
        var picks = Fill(wants, slots, all, ref sum, new Random(seed), Console.WriteLine);
        for (var i = 0; i < free.Length; i++)
            Console.WriteLine($"socket {i} (part {ship.ModSockets[free[i]].Plate}): {(picks[i] >= 0 ? all[picks[i]].Name : "nothing")}");
        var accel = sum.Mass > 0 ? sum.Thrust / sum.Mass : 0;
        Console.WriteLine(FormattableString.Invariant($"fitted: {sum.Mass / 1000:0.#} t, makes {sum.MadeKw:0} kW for {sum.DrawW / 1000:0.#} kW drawn (wants x{wants.PowerMargin:0.##}), {accel:0.##} m/s2 (wants {wants.Accel:0.##}), {sum.Guns} guns (wants {wants.MinGuns})"));
        var shortOf = Short(wants, sum);
        Console.WriteLine(shortOf == 0 ? "needs: met" : $"needs: still short of {string.Join(", ", new[] { (ShortPower, "power"), (ShortAccel, "acceleration"), (ShortGuns, "guns") }.Where(x => (shortOf & x.Item1) != 0).Select(x => x.Item2))} (no swap among the designs that fit helps)");
        return 0;
    }
}
