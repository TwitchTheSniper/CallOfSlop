using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's scoops (Constructs/Parts/Scoops): buckets for arms, in four styles and the four size classes (docs/sandbox/CONSTRUCTS.md,
/// "Scoops"; Source/Constructs/ConstructScoops.cs). Was tools/constructs/scoop_parts.py.
///
/// A scoop is a bin with a cutting edge. In a part's frame the floor is at the bottom (-y, fill rises along +y), the back wall at -z and
/// the edge at the front of the floor, cutting towards +z; the mouth opens up and forwards, its plane running from the edge to the top
/// of the back wall. Styles (a size's width is the style's cells across, times the class's cell):
///   Digging Bucket    the all-rounder: teeth, a deep curved shell.
///   Grading Bucket    wide and shallow, a plain lip: shaves the ground in flat, broad bites and holds a lot. Poor in stone.
///   Trenching Bucket  narrow and deep, heavy teeth: cuts hard and narrow, holds little.
///   Rock Bucket       a skeleton of bars and big teeth: the best in stone, and fines fall through the bars (its "keep").
/// Capacity is the fill box's volume in the units a cubic metre of ground gives (Constructs/Digging.json), so what a bucket carries is
/// what it dug. Mass is its plate area at the class's plate weight, and the cut force grows with the edge's width to the power 1.5,
/// never under 2.5 times the bucket's own weight, so a parked bucket rests on the ground.
/// </summary>
public sealed class ScoopParts : IPartGenerator
{
    public string Name => "scoop_parts";

    private const string Out = "Constructs/Parts/Scoops";
    private const int Units = 10; // units a cubic metre (Constructs/Digging.json)
    private const string Shell = "#b8862a", Wear = "#55585c", Tooth = "#3a3c40", Bar = "#6a6256";

    private sealed record Style(string Name, string Tag, Dictionary<string, int> Cells, double Long, double High, double Wall, int Force, double Bite, int Bites,
        bool Flat, int Spill, double Seconds, double Stone, PyDict? Keep);

    private static readonly Style Dig = new("Digging Bucket", "dig", new() { ["small"] = 5, ["medium"] = 3, ["large"] = 3, ["capital"] = 3 }, 1.0, 0.8, 0.05, 2600, 0.3, 2, false, 105, 2.0, 0.4, null);
    private static readonly Style Grade = new("Grading Bucket", "grade", new() { ["small"] = 7, ["medium"] = 5, ["large"] = 5, ["capital"] = 5 }, 0.6, 0.4, 0.03, 1100, 0.2, 3, true, 95, 1.5, 0.12, null);
    private static readonly Style Trench = new("Trenching Bucket", "trench", new() { ["small"] = 3, ["medium"] = 1, ["large"] = 1, ["capital"] = 1 }, 1.5, 1.2, 0.08, 6000, 0.55, 1, false, 110, 3.0, 0.6, null);
    private static readonly Style Rock = new("Rock Bucket", "rock", new() { ["small"] = 5, ["medium"] = 3, ["large"] = 3, ["capital"] = 3 }, 1.0, 0.8, 0.07, 4200, 0.3, 2, false, 105, 2.0, 1.0, Kw(("dirt", 0.3), ("sand", 0.1)));
    private static readonly Style[] Styles = [Dig, Grade, Trench, Rock];

    private static int CellsFor(Cls c, double metres) => (int)Math.Max(1, Py.RoundInt(metres / c.Cell + 0.499));

    /// <summary>n teeth along the edge, pointing +z: drawn, not solid (the edge is the lip).</summary>
    private static void Teeth(Part p, int n, double w, double z, double y0, double y1, double lng, string colour = Tooth)
    {
        for (var i = 0; i < n; i++)
        {
            var x = (i + 0.5) / n * w - w / 2;
            var hw = w / n * 0.28;
            p.Add(Box([x - hw, y0, z], [x + hw, y1, z + lng], colour, detail: true));
        }
    }

    /// <summary>The body of a style between its corners. Returns the plate area (m2) and the fill box's corners.</summary>
    private static (double Area, double[] Min, double[] Max) ShellOf(Part p, Style s, Cls c, double w, double xl, double xh, double y0, double y1, double z0, double z1, double t)
    {
        var h = y1 - y0;
        var lng = z1 - z0;
        var ribs = c.Key == "small" ? 3 : 5;
        double area;
        if (s == Rock)
        {
            // A skeleton: bars along the floor and up the back on a frame, gaps between them.
            var bar = w / (ribs * 2 + 1);
            for (var i = 0; i < ribs; i++)
            {
                var x = xl + bar * (2 * i + 1);
                p.Add(Box([x, y0, z0 + t], [x + bar, y0 + t, z1]));
                p.Add(Box([x, y0 + t, z0], [x + bar, y1, z0 + t], Bar));
            }
            p.Add(Box([xl, y0, z1 - t * 2], [xh, y0 + t, z1], Wear)); // the lip
            p.Add(Box([xl, y0, z0], [xh, y0 + t, z0 + t])); // the heel
            p.Add(Box([xl, y1 - t, z0 - 0.002 * c.X], [xh, y1, z0 + t])); // the top bar
            area = 2 * (w * lng + w * h) * 0.5;
        }
        else
        {
            p.Add(Box([xl, y0, z0 + t], [xh, y0 + t, z1])); // the floor
            p.Add(Box([xl, y0, z0], [xh, y1, z0 + t])); // the back
            p.Add(Box([xl, y0, z1 - t * 3], [xh, y0 + t * 1.4, z1], Wear)); // the wear lip
            p.Add(Box([xl + t, y0 + t, z0 + t], [xh - t, y0 + t * 2.5, z0 + t * 3.5], detail: true)); // the curve into the back
            area = w * lng + w * h;
        }
        // The sides stand tall at the back and fall away to the edge, in steps.
        var steps = s != Grade ? 3 : 2;
        for (var i = 0; i < steps; i++)
        {
            var za = z0 + lng * i / steps;
            var zb = z0 + lng * (i + 1) / steps;
            var top = y1 - (h * 0.75) * i / steps;
            foreach (var (x0, x1) in new[] { (xl, xl + t), (xh - t, xh) })
                p.Add(Box([x0, y0, za], [x1, top, zb]));
        }
        area += 2 * lng * h * 0.6;
        var innerTop = y0 + (h * 0.72);
        return (area, [R(xl + t + 0.005), R(y0 + t + 0.005), R(z0 + t + 0.005)], [R(xh - t - 0.005), R(innerTop), R(z0 + lng * 0.8)]);
    }

    private static Part Make(Style s, Cls c)
    {
        var n = s.Cells[c.Key];
        var w = n * c.Cell;
        var longCells = CellsFor(c, w * s.Long);
        var highCells = CellsFor(c, w * s.High);
        double z0 = c.Lo(), z1 = c.Hi(longCells);
        double y0 = c.Lo(), y1 = c.Hi(highCells);
        double lng = z1 - z0, high = y1 - y0;
        double xl = -w / 2, xh = w / 2;
        var t = Math.Max(0.02, w * s.Wall);
        var name = c == Small ? s.Name : $"{s.Name} {c.Name}";
        var desc = s == Dig ? "Pushed edge first into the ground it digs it out and holds what it cuts. Tip it to pour."
            : s == Grade ? "Wide and shallow: shaves the ground flat in broad bites and holds a lot. Poor in stone."
            : s == Trench ? "Narrow and deep: cuts hard, holds little."
            : "Bars and big teeth: the best in stone. Dirt and sand mostly fall through.";
        var p = new Part(name, c, "Scoops", desc, role: "storage", material: "steel", color: Shell, group: c == Small ? "Gathering" : null);
        var (area, fmin, fmax) = ShellOf(p, s, c, w, xl, xh, y0, y1, z0, z1, t);
        if (s != Grade)
        {
            var count = c == Small || n > 1 ? (s == Dig ? 5 : s == Trench ? 3 : 4) : 3;
            var tl = w * (s == Rock ? 0.14 : 0.1);
            Teeth(p, count, w, z1 + 0.002 * c.X, y0, y0 + t * 1.1, tl);
        }
        else
            p.Add(Box([xl, y0 - 0.002 * c.X, z1 - t], [xh, y0 + t * 0.5, z1 + w * 0.012], Tooth, detail: true)); // the blade
        p.Sock("back", [0, 0, z0], "-z");
        p.Sock("top", [0, y1, 0], "+y");
        p.Sock("under", [0, y0, 0], "-y");
        var volume = (fmax[0] - fmin[0]) * (fmax[1] - fmin[1]) * (fmax[2] - fmin[2]);
        var capacity = Math.Max(2, Py.RoundInt(volume * Units));
        var edgeY = y0 + 0.01;
        var mass = area * c.KgM2 * Math.Pow(t / c.Plate, 0.5) * 0.9;
        var force = Math.Max(s.Force * Math.Pow(w, 1.5), 2.5 * mass * 9.81); // (the ground stands more than the bucket's own weight: parked, it rests)
        var scoop = new PyDict
        {
            ["edge"] = Py.L(Vec(xl, edgeY, z1), Vec(xh, edgeY, z1)),
            ["rim"] = Py.L(Vec(xl, y1, z0), Vec(xh, y1, z0)),
            ["cuts"] = Py.L(0, 0, 1),
            ["mouth"] = Vec(0, lng, high),
            ["reach"] = R(high),
            ["cutForce"] = (long)Py.Round(force, -1),
            ["biteRadius"] = R(Math.Max(0.3, w * s.Bite)),
            ["spillDegrees"] = s.Spill,
            ["pourSeconds"] = s.Seconds,
        };
        if (s.Bites > 1)
            scoop["bites"] = s.Bites;
        if (s.Flat)
            scoop["flat"] = true;
        if (s.Stone < 1)
            scoop["stoneRate"] = s.Stone;
        if (s.Keep is not null)
            scoop["keep"] = s.Keep;
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", capacity), ("min", fmin.Cast<object?>().ToList()), ("max", fmax.Cast<object?>().ToList()))), ("scoop", scoop))));
        p.Stats(mass, area * c.HealthM2 * 1.5, c.Armour);
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var c in new[] { Small, Medium, Large, Capital })
            foreach (var s in Styles)
                parts.Add(("", Make(s, c)));
        WriteParts(o, Out, parts);
    }
}
