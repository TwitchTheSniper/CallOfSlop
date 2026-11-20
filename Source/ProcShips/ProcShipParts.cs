using System.Globalization;
using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.ProcShips;

/// <summary>
/// The procedural ships' parts, made at run time from their names alone, as HullLoft's lofted bays are (docs/procships/PROCSHIPS.md):
/// "Proc F17 Hull P3" is the piece "Hull P3" of ship F17, made by generating F17 (kept by its code), and "Proc Ladder 325" a ladder
/// climbing 3.25 m ("Proc Ladder 412 388": climbing 4.12 m, its rails 3.88 m tall). Each is a named run-time part
/// (<see cref="ConstructPartDef.IsNamedRuntime"/>): its name is its key in blueprints and saves, its id this machine's own.
/// </summary>
public static class ProcShipParts
{
    public const string Prefix = "Proc ";
    public const string LadderPrefix = "Proc Ladder ";

    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal);

    /// <summary>A ladder's name: its climb in centimetres, then its rails' height when they aren't a metre over its landing.</summary>
    public static string LadderName(double rise, double top)
    {
        int r = (int)Math.Round(rise * 100), t = (int)Math.Round(top * 100);
        return t == r + 100 ? $"{LadderPrefix}{r}" : $"{LadderPrefix}{r} {t}";
    }

    /// <summary>The part a name makes, made the first time it is asked for. Null: a name no ship has, or the run-time table is full.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length > ConstructParts.MaxRuntimeName)
            return null;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            var made = Make(name);
            Interned[name] = made;
            return made;
        }
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the name table, and the names that made nothing (made again if asked for).</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
    }

    private static ConstructPartDef? Make(string name)
    {
        string? json;
        try
        {
            json = JsonFor(name);
        }
        catch (ProcShipException e)
        {
            Console.WriteLine($"[procships] {name}: the ship can't be made: {e.Message}");
            return null;
        }
        if (json == null)
        {
            Console.WriteLine($"[procships] {name}: no such piece");
            return null;
        }
        ConstructPartDef def;
        try
        {
            def = ConstructParts.Parse(json, name);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[procships] {name}: refused, {e.Message}");
            return null;
        }
        def.IsNamedRuntime = true;
        def.ExHash = Fnv(name);
        return Extrusion.Adopt(def) ? def : null;
    }

    /// <summary>The part file a name makes, or null for a name no ship or ladder has. Throws <see cref="ProcShipException"/> when its ship can't be made.</summary>
    public static string? JsonFor(string name)
    {
        if (name.StartsWith(LadderPrefix, StringComparison.Ordinal))
        {
            var nums = name[LadderPrefix.Length..].Split(' ');
            if (nums.Length is < 1 or > 2 || !int.TryParse(nums[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rise) || rise is < 50 or > 2000)
                return null;
            var top = rise + 100;
            if (nums.Length == 2 && (!int.TryParse(nums[1], NumberStyles.None, CultureInfo.InvariantCulture, out top) || top < 50 || top > rise + 100))
                return null;
            return PyJson.Dumps(Ladder(name, rise / 100.0, top / 100.0).Json());
        }
        var rest = name[Prefix.Length..];
        var sp = rest.IndexOf(' ');
        if (sp <= 0)
            return null;
        var code = rest[..sp];
        if (ProcShipSpec.Parse(code) is not { } spec || spec.Code != code)
            return null; // (one spelling a ship: its code as it names its pieces)
        return ProcShip.Get(spec).PieceJson.GetValueOrDefault(name);
    }

    /// <summary>A ladder (the Zero Day's, TorpedoFrigateParts.Ladder): its hidden box from its foot to <paramref name="top"/>, rails, brackets and rungs, climbed from its front (-z).</summary>
    private static Part Ladder(string name, double rise, double top)
    {
        var p = new Part(name, Small, ProcShipDraw.Section, $"A ladder in a procedural ship: F climbs it ({rise.ToString("0.##", CultureInfo.InvariantCulture)} m).",
            role: "frame", tool: "welder", material: "steel", color: ProcShipDraw.Frame, group: ProcShipDraw.Group);
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], ProcShipDraw.Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < Math.Min(rise + 0.1, top - 0.1); y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], ProcShipDraw.Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.29, top - 0.06, -0.19], [0.29, top, -0.11], ProcShipDraw.Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, -0.095], "-y");
        p.Set(("ladder", rise), ("stage", true));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }
}
