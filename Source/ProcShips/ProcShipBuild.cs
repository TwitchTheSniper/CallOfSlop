using System.Diagnostics;
using System.Globalization;
using CallOfSlop.PartKit;
using CallOfSlop.PartKit.Generators;

namespace CallOfSlop.ProcShips;

/// <summary>
/// A procedural ship made from its spec (docs/procships/PROCSHIPS.md): its hull, its plan, its pieces' part files and its
/// blueprint. The blueprint welds the pieces out from the keel over the socket pairs where they touch (one pair a step, the
/// game welds the rest), then hangs its doors, hatches, ladders and module sockets on the sockets the pieces carry for them.
/// A ship is made once per code and kept, a few at a time (<see cref="ProcShipParts"/> makes its pieces from it).
/// </summary>
public sealed class ProcShip
{
    public const string Door = "Bulkhead Door 1.5x2.25 m", Hatch = "Docking Hatch";
    public const string DoorPaint = "#4a5259";

    public required ProcShipSpec Spec { get; init; }
    public required ProcHullShape Hull { get; init; }
    public required ProcShipPlan Plan { get; init; }
    public required List<(string Key, string Value)> Traits { get; init; }

    /// <summary>Each piece's part file, by its name.</summary>
    public required Dictionary<string, string> PieceJson { get; init; }

    /// <summary>Each piece's role (keel, spine, hull, floor, ceiling, roof, walls, lock, stern), band and side.</summary>
    public required Dictionary<string, (string Role, int Band, int Side)> Roles { get; init; }

    public required PyDict Blueprint { get; init; }

    /// <summary>The steps that hang things: what (door, lock inner, lock outer, belly hatch, dorsal hatch, ladder, socket), its step, and its index in the plan's list.</summary>
    public required List<(string What, int Step, int Index)> Hung { get; init; }

    public required int Pieces { get; init; }
    public required double MakeMs { get; init; }

    public string Name => $"Procedural {Spec.Title}";

    public int Steps => ((List<object?>)Blueprint["steps"]!).Count;

    // ---- made once per code

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ProcShip> Made = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Recent = new();
    private const int Keep = 12;

    /// <summary>The ship for a spec, made the first time it is asked for (throws <see cref="ProcShipException"/> when it can't be made).</summary>
    public static ProcShip Get(ProcShipSpec spec)
    {
        lock (Gate)
        {
            if (Made.TryGetValue(spec.Code, out var have))
            {
                Recent.Remove(spec.Code);
                Recent.AddFirst(spec.Code);
                return have;
            }
            var made = Make(spec);
            Made[spec.Code] = made;
            Recent.AddFirst(spec.Code);
            while (Recent.Count > Keep)
            {
                Made.Remove(Recent.Last!.Value);
                Recent.RemoveLast();
            }
            return made;
        }
    }

    /// <summary>The dice a spec rolls: its seed, salted by its class (Frigate 17 and Corvette 17 are different ships).</summary>
    public static ProcDice DiceOf(ProcShipSpec spec) => new(spec.Seed * 3 + (int)spec.Class);

    public static ProcShip Make(ProcShipSpec spec)
    {
        var clock = Stopwatch.StartNew();
        var dice = DiceOf(spec);
        var traits = new List<(string Key, string Value)>();
        var role = ProcShipSpec.RoleOf(spec, dice);
        ProcHullShape hull = null!;
        ProcShipPlan plan = null!;
        // A hull too small for its rooms (a lobby that doesn't fit amidships, a deck too narrow for its ladder) is made again a size
        // up, at most three times: the same choices, longer and wider. The first refusal is the one reported.
        string? refusal = null;
        for (var grow = 0; ; grow++)
        {
            traits.Clear();
            try
            {
                hull = ProcHullShape.Make(spec, role, dice, traits, grow);
                plan = ProcShipPlan.Make(spec, role, hull, dice, traits);
                break;
            }
            catch (ProcShipException e)
            {
                string T(string k) => traits.FirstOrDefault(t => t.Key == k).Value ?? "?";
                refusal ??= traits.Count > 0 ? $"{e.Message} (a {T("Role")}, {T("Hull")} hull, {T("Length")}, {T("Beam")} beam, {T("Decks")} decks)" : e.Message;
                if (grow == ProcHullShape.MostGrown || traits.Count == 0)
                    throw new ProcShipException(refusal);
            }
        }
        var draw = new ProcShipDraw(hull, plan, spec.Code);
        List<Part> parts;
        try
        {
            parts = draw.Make();
        }
        catch (InvalidOperationException e)
        {
            throw new ProcShipException(e.Message);
        }
        var files = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        foreach (var q in parts)
        {
            PyDict d;
            try
            {
                d = q.Json();
            }
            catch (InvalidOperationException e)
            {
                throw new ProcShipException($"a piece is over a part's limits: {e.Message}");
            }
            files[(string)q.D["name"]!] = d;
        }

        // The structure: out from the keel amidships over the socket pairs, breadth first (each piece on the first one it meets).
        var adj = files.Keys.ToDictionary(n => n, _ => new List<(string Other, string Mine, string Theirs)>(), StringComparer.Ordinal);
        foreach (var (a, sa, b, sb) in draw.Joints)
        {
            adj[a].Add((b, sa, sb));
            adj[b].Add((a, sb, sa));
        }
        var first = ProcShipDraw.NameOf(spec.Code, $"Keel {draw.BandOf((hull.MidZ0 + hull.MidZ1) / 2) + 1}");
        if (!files.ContainsKey(first))
            first = files.Keys.First();
        var steps = new List<object?> { new PyDict { ["part"] = first } };
        var stepOf = new Dictionary<string, int>(StringComparer.Ordinal) { [first] = 0 };
        var queue = new Queue<string>([first]);
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            foreach (var (b, mine, theirs) in adj[a])
            {
                if (stepOf.ContainsKey(b))
                    continue;
                stepOf[b] = steps.Count;
                steps.Add(new PyDict { ["part"] = b, ["on"] = stepOf[a], ["socket"] = mine, ["by"] = theirs });
                queue.Enqueue(b);
            }
        }
        if (stepOf.Count != files.Count)
            throw new ProcShipException("pieces joined to nothing: " + string.Join(", ", files.Keys.Where(n => !stepOf.ContainsKey(n))));

        // What hangs on them: doors, hatches, ladders, the module sockets.
        var hung = new List<(string What, int Step, int Index)>();
        var doorIndex = 0;
        foreach (var (what, piece, sock, index) in draw.Hangs)
        {
            var on = stepOf[piece];
            PyDict step;
            switch (what)
            {
                case "door" or "lock inner" or "lock outer":
                    step = new PyDict { ["part"] = Door, ["on"] = on, ["socket"] = sock, ["by"] = "hang", ["paint"] = what == "door" ? DoorPaint : plan.Accent, ["finish"] = "plated steel" };
                    hung.Add((what, steps.Count, doorIndex++));
                    break;
                case "belly hatch" or "dorsal hatch":
                    step = Spun(new PyDict { ["part"] = Hatch, ["on"] = on, ["socket"] = sock, ["by"] = "base" }, "+y", "-y", BuildingParts.R0);
                    step["paint"] = plan.Accent;
                    step["finish"] = "plated steel";
                    hung.Add((what, steps.Count, plan.Hatches.FindIndex(t => t.Kind + " hatch" == what)));
                    break;
                case "ladder":
                    {
                        var l = plan.Ladders[index];
                        step = Spun(new PyDict { ["part"] = ProcShipParts.LadderName(l.Rise, l.Top), ["on"] = on, ["socket"] = sock, ["by"] = "bottom" }, "+y", "-y", l.Turned ? BuildingParts.RY180 : BuildingParts.R0);
                        hung.Add((what, steps.Count, index));
                        break;
                    }
                case "socket":
                    {
                        var s = plan.Sockets[index];
                        step = Spun(new PyDict { ["part"] = $"Module Socket {s.W}x{s.L}", ["on"] = on, ["socket"] = sock, ["by"] = "b00.00" }, s.Dir, "-y", s.Rot);
                        var config = new PyDict();
                        if (s.Kind != 0)
                            config[ConstructConfig.KeyName(CfgKey.ModKind)] = s.Kind;
                        if (s.Style != 0)
                            config[ConstructConfig.KeyName(CfgKey.ModStyle)] = s.Style;
                        if (s.Above > 0)
                            config[ConstructConfig.KeyName(CfgKey.ModAbove)] = s.Above;
                        if (s.Keyed)
                            config[ConstructConfig.KeyName(CfgKey.ModKeyed)] = 1;
                        if (config.Count > 0)
                            step["config"] = config;
                        hung.Add((what, steps.Count, index));
                        break;
                    }
                default:
                    throw new ProcShipException($"nothing hangs as a {what}");
            }
            steps.Add(step);
        }
        var bp = new PyDict
        {
            ["format"] = "call-of-slop-construct-blueprint/1",
            ["name"] = $"Procedural {spec.Title}",
            ["description"] = Describe(spec, traits),
            ["steps"] = steps,
        };
        traits.Add(("Sockets", plan.Sockets.Count == 0 ? "none" : string.Join(", ", plan.Sockets.GroupBy(s => s.Use).Select(g => $"{g.Count()} {g.Key}"))));
        traits.Add(("Pieces", files.Count.ToString(CultureInfo.InvariantCulture)));
        return new ProcShip
        {
            Spec = spec, Hull = hull, Plan = plan, Traits = traits,
            PieceJson = files.ToDictionary(kv => kv.Key, kv => PyJson.Dumps(kv.Value), StringComparer.Ordinal),
            Roles = new Dictionary<string, (string, int, int)>(draw.Roles, StringComparer.Ordinal),
            Blueprint = bp, Hung = hung, Pieces = files.Count, MakeMs = clock.Elapsed.TotalMilliseconds,
        };
    }

    public static string KindName(int kind) => kind switch { 1 => "engine", 2 => "reactor", 3 => "turret", _ => "cargo" };

    private static string Describe(ProcShipSpec spec, List<(string Key, string Value)> traits)
    {
        string T(string k) => traits.FirstOrDefault(t => t.Key == k).Value ?? "";
        return $"A procedural {T("Role")} {spec.Class.ToString().ToLowerInvariant()} ({spec.Code}, generator {ProcShipSpec.Version}): a {T("Hull")} hull {T("Length")} long with a {T("Bow")} bow, " +
               $"{T("Decks")} deck{(T("Decks") == "1" ? "" : "s")} of rooms inside it (tall rooms: {T("Tall rooms")}), nacelles: {T("Nacelles")}, side airlocks ({T("Side locks")}), " +
               $"a belly hatch: {T("Belly hatch")}, a dorsal hatch: {T("Dorsal hatch")}.";
    }

    /// <summary>A hang step turned <paramref name="rot"/>: its spin, the index of that turn in the game's list for the socket pair (ConstructBuild.SpinTurns, as BuildingParts.HangTurned).</summary>
    private static PyDict Spun(PyDict step, string target, string by, int rot)
    {
        var want = Array.IndexOf(BuildingParts.Dirs, target) ^ 1;
        var dir = Array.IndexOf(BuildingParts.Dirs, by);
        var turns = Enumerable.Range(0, 24).Where(rr => BuildingParts.ApplyDir(rr, dir) == want).ToList();
        for (var a = 0; a < turns.Count; a++)
            for (var b = a + 1; b < turns.Count; b++)
                if (BuildingParts.Upright(turns[b]) > BuildingParts.Upright(turns[a]))
                    (turns[a], turns[b]) = (turns[b], turns[a]);
        var spin = turns.IndexOf(rot);
        if (spin < 0)
            throw new ProcShipException($"{step["part"]} by its {by} on a {target} socket can't be turned {rot}");
        if (spin != 0)
            step["spin"] = spin;
        return step;
    }
}
