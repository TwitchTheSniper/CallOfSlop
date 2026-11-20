using System.Globalization;
using System.Numerics;
using CallOfSlop.PartKit;

namespace CallOfSlop.ProcShips;

/// <summary>
/// The procedural ship generator's commands (docs/procships/PROCSHIPS.md):
/// <c>--gen-procship SPEC [--out DIR]</c> makes one ship ("F17", "frigate 17 dart"), prints its traits, checks its pieces never
/// overlap, fits every step by the game's build rules (each refusal listed, not only the first) and writes its blueprint;
/// <c>--gen-procship sweep [--per N] [--fit] [--hull H] [--bow B] [--role R] [--nacelles]</c> makes N seeds of each class (with that
/// hull, bow, role or nacelles fixed) and counts the ones that can't be made, by reason, and what the rest came out as;
/// <c>--procship-gallery DIR [--seeds "F17,C3d"] [--per N]</c> writes the Procedural gallery (ConstructPreview.ProcShips.cs).
/// </summary>
public static class ProcShipCli
{
    private static string? Flag(string[] args, string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    public static int Gen(string what, string[] args)
    {
        if (what.Equals("sweep", StringComparison.OrdinalIgnoreCase))
        {
            ProcHull? hull = Flag(args, "--hull") is { } hs ? Enum.TryParse<ProcHull>(hs, true, out var hv) ? hv : throw new ArgumentException($"--hull: no hull \"{hs}\"") : null;
            ProcRole? role = Flag(args, "--role") is { } rs ? Enum.TryParse<ProcRole>(rs, true, out var rv) ? rv : throw new ArgumentException($"--role: no role \"{rs}\"") : null;
            ProcBow? bow = Flag(args, "--bow") is { } bws ? Enum.TryParse<ProcBow>(bws, true, out var bv) ? bv : throw new ArgumentException($"--bow: no bow \"{bws}\"") : null;
            return Sweep(int.TryParse(Flag(args, "--per"), out var per) ? per : 40, args.Any(a => a.Equals("--fit", StringComparison.OrdinalIgnoreCase)),
                hull, role, args.Any(a => a.Equals("--nacelles", StringComparison.OrdinalIgnoreCase)), bow);
        }
        if (ProcShipSpec.Parse(what) is not { } spec)
        {
            Console.WriteLine($"--gen-procship: \"{what}\" names no ship: a code (F17, c4d, D12b3) or words (frigate 17 dart 3 decks)");
            return 1;
        }
        ProcShip ship;
        try
        {
            ship = ProcShip.Make(spec);
        }
        catch (ProcShipException e)
        {
            Console.WriteLine($"REFUSED {spec.Title} ({spec.Code}): {e.Message}");
            return 1;
        }
        Console.WriteLine($"{ship.Name} ({spec.Code}, generator {ProcShipSpec.Version}): {ship.Pieces} pieces, {ship.Steps} steps, made in {ship.MakeMs:0} ms");
        foreach (var (k, v) in ship.Traits)
            Console.WriteLine($"  {k}: {v}");
        var bad = Overlaps(ship, 25);
        bad += FitAll(ship, 40);
        bad += Climbs(ship, 20);
        if (Flag(args, "--out") is { } dir)
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"Proc{spec.Code}.json");
            File.WriteAllText(file, PyJson.Dumps(ship.Blueprint, 2) + "\n");
            Console.WriteLine($"wrote {Path.GetFullPath(file)}");
        }
        return bad;
    }

    /// <summary>Seeds 1..per of each class made (and with <paramref name="fit"/>, checked and fitted step by step): how many can't be, by reason, and the made ones' roles, hulls, bows, fits and tall rooms.</summary>
    private static int Sweep(int per, bool fit, ProcHull? hull, ProcRole? role, bool nacelles, ProcBow? bow = null)
    {
        var fails = 0;
        foreach (var cls in Enum.GetValues<ProcClass>())
        {
            var why = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            double ms = 0;
            var pieces = new List<int>();
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new Dictionary<string, List<int>>(StringComparer.Ordinal); // (the seeds behind a tally worth looking at)
            void Count(string k) => kinds[k] = kinds.GetValueOrDefault(k) + 1;
            for (var seed = 1; seed <= per; seed++)
            {
                var problems = new List<string>();
                try
                {
                    var ship = ProcShip.Make(new ProcShipSpec(seed, cls, hull, null, role, nacelles, bow));
                    ms += ship.MakeMs;
                    pieces.Add(ship.Pieces);
                    Count(ship.Hull.Role.ToString().ToLowerInvariant());
                    Count(ship.Hull.Style.ToString().ToLowerInvariant());
                    Count($"{ship.Hull.Bow.ToString().ToLowerInvariant()} bow");
                    Count($"{ship.Plan.Kit.ReactorWord}{(ship.Plan.Kit.Reactors == 2 ? " twin" : "")} reactor");
                    Count($"{ship.Plan.Kit.Drive.ToString().ToLowerInvariant()} drive");
                    foreach (var (k, v) in ship.Traits.Where(t => t.Key == "Trade-off" || t.Key == "Reactors" && t.Value.Contains("cut", StringComparison.Ordinal)))
                    {
                        var key = k == "Trade-off" ? "trade-off" : "reactor cut";
                        Count(key);
                        if (!seen.TryGetValue(key, out var at))
                            seen[key] = at = [];
                        at.Add(seed);
                    }
                    Count($"{ship.Hull.Decks} decks");
                    if (ship.Hull.Nacelles.Count > 0)
                        Count("nacelles");
                    foreach (var r in ship.Plan.Rooms.Where(q => q.Top > q.Deck))
                        Count($"{r.Name.ToLowerInvariant()} ({r.Top - r.Deck + 1} decks)");
                    foreach (var r in ship.Plan.HallRooms)
                        Count(r.Name.ToLowerInvariant());
                    if (fit)
                    {
                        Overlaps(ship, 0, problems);
                        if (FitAll(ship, 0, problems) == 0)
                            Climbs(ship, 0, problems);
                    }
                }
                catch (ProcShipException e)
                {
                    problems.Add(e.Message);
                }
                foreach (var m in problems.Distinct())
                {
                    var key = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(m, @"Proc [CFD]\d+\S*", "Proc"), @"[\d.]+", "#");
                    if (!why.TryGetValue(key, out var list))
                        why[key] = list = [];
                    if (!list.Contains(seed))
                        list.Add(seed);
                }
                if (problems.Count > 0)
                    fails++;
            }
            Console.WriteLine($"{cls}: {pieces.Count} of {per} made{(fit ? "" : " (not fitted)")}, {(pieces.Count > 0 ? ms / pieces.Count : 0):0} ms each, pieces {(pieces.Count > 0 ? $"{pieces.Min()}-{pieces.Max()}" : "-")}");
            Console.WriteLine("  " + string.Join(", ", kinds.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")));
            foreach (var (k, seeds) in seen.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Console.WriteLine($"  {k}: seeds {string.Join(", ", seeds.Take(12))}{(seeds.Count > 12 ? ", ..." : "")}");
            foreach (var (k, seeds) in why.OrderByDescending(kv => kv.Value.Count))
                Console.WriteLine($"  {seeds.Count}x {k} (seeds {string.Join(", ", seeds.Take(8))}{(seeds.Count > 8 ? ", ..." : "")})");
        }
        return fails;
    }

    /// <summary>Solid boxes of two different pieces that overlap by more than the build's centimetre: each listed (up to <paramref name="most"/>, or into <paramref name="into"/> quietly). Returns how many.</summary>
    public static int Overlaps(ProcShip ship, int most, List<string>? into = null)
    {
        var solids = new List<(string Piece, int Box, double[] Mn, double[] Mx)>();
        foreach (var (name, json) in ship.PieceJson)
        {
            var d = PyJson.Loads(json) as PyDict ?? throw new InvalidOperationException(name);
            var boxes = (List<object?>)d["boxes"]!;
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = (PyDict)boxes[i]!;
                if (Py.Truthy(b.Get("detail")))
                    continue;
                solids.Add((name, i, [.. ((List<object?>)b["min"]!).Select(Py.D)], [.. ((List<object?>)b["max"]!).Select(Py.D)]));
            }
        }
        var n = 0;
        for (var i = 0; i < solids.Count; i++)
            for (var j = i + 1; j < solids.Count; j++)
            {
                var (a, b) = (solids[i], solids[j]);
                if (a.Piece == b.Piece)
                    continue;
                var over = true;
                for (var k = 0; k < 3 && over; k++)
                    over = Math.Min(a.Mx[k], b.Mx[k]) - Math.Max(a.Mn[k], b.Mn[k]) > 0.011;
                if (!over)
                    continue;
                var line = $"OVERLAP {a.Piece} #{a.Box} {Box(a.Mn, a.Mx)} and {b.Piece} #{b.Box} {Box(b.Mn, b.Mx)}";
                if (into != null)
                    into.Add($"overlap: {a.Piece[(a.Piece.IndexOf(' ', 5) + 1)..]} and {b.Piece[(b.Piece.IndexOf(' ', 5) + 1)..]}");
                else if (n < most)
                    Console.WriteLine(line);
                n++;
            }
        if (into == null)
            Console.WriteLine(n == 0 ? "no solid boxes of two pieces overlap" : $"{n} overlaps");
        return n;

        static string Box(double[] mn, double[] mx) =>
            $"[{string.Join(" ", mn.Select(v => (v + ProcShipDraw.H).ToString("0.###", CultureInfo.InvariantCulture)))} .. {string.Join(" ", mx.Select(v => (v + ProcShipDraw.H).ToString("0.###", CultureInfo.InvariantCulture)))}]";
    }

    private static bool _yard;

    /// <summary>
    /// Every ladder climbed up and down by the game's own climb, as <c>--ladder-check</c> climbs the shipped ones (LadderCheck.Climb):
    /// each one that refuses listed (up to <paramref name="most"/>, or into <paramref name="into"/> quietly). Returns how many refused.
    /// </summary>
    public static int Climbs(ProcShip ship, int most, List<string>? into = null)
    {
        if (!_yard && !(_yard = LadderCheck.Yard()))
            return 0;
        ConstructBuild.Blueprint bp;
        try
        {
            bp = ConstructBuild.ParseBlueprint(PyJson.Dumps(ship.Blueprint), ship.Name);
        }
        catch (Exception e) when (e is ConstructPartException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return 0; // (FitAll says why)
        }
        var lines = new List<string>();
        var (ladders, bad) = LadderCheck.Climb(bp, lines.Add);
        var shown = 0;
        foreach (var l in lines.Where(l => l.StartsWith(ship.Name + ": ", StringComparison.Ordinal) && !l.EndsWith(": up ok, down ok", StringComparison.Ordinal)))
        {
            var what = l[(ship.Name.Length + 2)..];
            if (into != null)
                into.Add("ladder: " + System.Text.RegularExpressions.Regex.Replace(what, @" at <[^>]*>", ""));
            else if (shown++ < most)
                Console.WriteLine("CLIMB " + what);
        }
        if (into == null)
            Console.WriteLine(bad == 0 ? $"every ladder climbs up and down: {ladders}" : $"{bad} of {ladders} ladders refused");
        return bad;
    }

    /// <summary>Every step fitted by the game's rules in a scratch world, going on past a refusal (what hangs on a refused step is skipped): each refusal listed. Returns how many.</summary>
    public static int FitAll(ProcShip ship, int most, List<string>? into = null)
    {
        ConstructBuild.Blueprint bp;
        try
        {
            bp = ConstructBuild.ParseBlueprint(PyJson.Dumps(ship.Blueprint), ship.Name);
        }
        catch (Exception e) when (e is ConstructPartException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            if (into != null)
                into.Add($"blueprint: {e.Message}");
            else
                Console.WriteLine($"REFUSED blueprint: {e.Message}");
            return 1;
        }
        var world = new ConstructWorld();
        var c = world.Create(Vector3.Zero, Quaternion.Identity)!;
        var partOf = new int[bp.Steps.Count];
        int refused = 0, skipped = 0;
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            if (k > 0 && partOf[bp.Steps[k].On] < 0)
            {
                partOf[k] = -1;
                skipped++;
                continue;
            }
            if (ConstructBuild.FitStep(world, c, bp, k, partOf) is { } why)
            {
                partOf[k] = -1;
                if (into != null)
                    into.Add("refused: " + System.Text.RegularExpressions.Regex.Replace(why, @"^step \d+: ", ""));
                else if (refused < most)
                    Console.WriteLine($"REFUSED {why}");
                refused++;
            }
        }
        c.Rebuild();
        if (into == null)
            Console.WriteLine(refused == 0 ? $"every step fits: {c.PartCount} parts, {c.Mass / 1000:0.#} t" : $"{refused} steps refused, {skipped} more on them skipped; {c.PartCount} parts fitted");
        world.Remove(c);
        return refused;
    }
}
