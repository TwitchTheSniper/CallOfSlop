using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A style's body language (docs/vehiclekit/BODY-STYLES.md): the cross-sections its rooms are lofted from, and how a seed varies them. The grammar still plans a room's
/// bays as tall, low and narrow sections (<see cref="ShapeGrammar.Bays(Dice, int, bool, BodyRange)"/>); a language turns each into this style's own section, at this
/// seed's height and width, and its brow and tail into this seed's rake. Kit 5 only (<see cref="VehicleKitVersion.Lofted"/>).
/// </summary>
public sealed record BodyLanguage(string Name, string Words, HullLoft.Section Tall, float HeightMin, float HeightMax, float BrowRakeMin, float BrowRakeMax, float TailRakeMin, float TailRakeMax,
    float BrowNarrow, float TailNarrow, float LowShare = 0.87f, float NarrowShare = 0.8f)
{
    private static HullLoft.Section S(params float[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1])).ToArray());

    // The languages. Every tall section keeps x >= 1.0 below 1.6 m (furniture reaches a metre out: Vehicle.BayHalf), stands on the floor at 1.0 m or more and is over
    // 2.1 m at its top, so a door bay can be any of them; a low one keeps 2 m at the centre.
    public static readonly BodyLanguage Slab = new("slab", "welded slabs: upright walls, one hard chamfer, a flat roof", S(1.04f, 0, 1.07f, 1.86f, 0.8f, 2.22f, 0, 2.22f), 2.16f, 2.32f, 0.62f, 0.8f, 0.7f, 0.9f, 0.96f, 0.94f);
    public static readonly BodyLanguage Ledge = new("ledge", "a box welded over a box: the upper walls stand proud of the lower on a ledge", S(1.02f, 0, 1.03f, 1.08f, 1.12f, 1.16f, 1.12f, 1.9f, 0.84f, 2.2f, 0, 2.2f), 2.14f, 2.3f, 0.7f, 0.85f, 0.75f, 0.9f, 0.95f, 0.93f);
    public static readonly BodyLanguage Pod = new("pod", "clean rounded panels: tumblehome and a crowned roof", S(1.0f, 0, 1.1f, 0.45f, 1.1f, 1.3f, 0.98f, 1.8f, 0.72f, 2.15f, 0.36f, 2.3f, 0, 2.33f), 2.26f, 2.4f, 0.64f, 0.76f, 0.65f, 0.82f, 0.9f, 0.9f);
    public static readonly BodyLanguage Van = new("van", "a panel van: near-upright sides rounding into a gently crowned roof", S(1.02f, 0, 1.08f, 0.6f, 1.08f, 1.75f, 0.96f, 2.08f, 0.6f, 2.24f, 0, 2.28f), 2.22f, 2.36f, 0.6f, 0.78f, 0.78f, 0.92f, 0.94f, 0.95f);
    public static readonly BodyLanguage Bus = new("bus", "a bus: tall upright sides, a domed roof with rounded corners", S(1.04f, 0, 1.07f, 0.95f, 1.06f, 1.85f, 0.98f, 2.12f, 0.78f, 2.3f, 0.4f, 2.38f, 0, 2.4f), 2.34f, 2.48f, 0.72f, 0.9f, 0.72f, 0.88f, 0.96f, 0.95f);
    public static readonly BodyLanguage Coach = new("coach", "a coach: a lower body, a long tumblehome and a raked front", S(1.02f, 0, 1.08f, 0.7f, 1.06f, 1.5f, 0.92f, 1.98f, 0.62f, 2.18f, 0, 2.24f), 2.2f, 2.32f, 0.6f, 0.72f, 0.72f, 0.88f, 0.9f, 0.93f);
    public static readonly BodyLanguage Boat = new("boat", "boat-like: a flare to a strake, a curved tumblehome, a boat-tail stern", S(1.02f, 0, 1.13f, 0.7f, 1.09f, 1.45f, 0.92f, 1.95f, 0.55f, 2.22f, 0, 2.26f), 2.2f, 2.36f, 0.65f, 0.82f, 0.6f, 0.78f, 0.92f, 0.8f);
    public static readonly BodyLanguage Cabin = new("cabin", "a site cabin: plain walls, eaves and a pitched roof", S(1.04f, 0, 1.06f, 1.92f, 1.12f, 1.98f, 0, 2.42f), 2.36f, 2.5f, 0.75f, 0.9f, 0.75f, 0.9f, 0.97f, 0.97f, 0.88f);
    public static readonly BodyLanguage Armour = new("armour", "faceted armour: a flared lower hull, a steep glacis-like upper, a narrow roof", S(1.0f, 0, 1.13f, 0.75f, 0.88f, 2.0f, 0.56f, 2.2f, 0, 2.2f), 2.16f, 2.28f, 0.62f, 0.75f, 0.65f, 0.8f, 0.9f, 0.88f);

    public static readonly BodyLanguage[] All = [Slab, Ledge, Pod, Van, Bus, Coach, Boat, Cabin, Armour];

    /// <summary>Which languages a style speaks, and how often (a style not named speaks armour).</summary>
    public static readonly Dictionary<string, (BodyLanguage L, double W)[]> ByStyle = new(StringComparer.Ordinal)
    {
        ["Bandit"] = [(Slab, 2), (Ledge, 1.5), (Armour, 0.5)],
        ["IT"] = [(Pod, 2), (Van, 1.5)],
        ["Scam"] = [(Cabin, 2), (Van, 1)],
        ["Highland"] = [(Boat, 3), (Slab, 0.5)],
        ["British"] = [(Bus, 2.5), (Coach, 1.5)],
    };

    public static (BodyLanguage L, double W)[] Of(FactionStyle st) => ByStyle.GetValueOrDefault(st.Name) ?? [(Armour, 1)];
}

/// <summary>One body's roll of its language: its height and width, how far its brow and tail rake, and the sections that come of it.</summary>
public sealed class BodyLook
{
    public BodyLanguage Language { get; }
    public float Height, Width, BrowRake, TailRake, BrowNarrow, TailNarrow;

    public BodyLook(BodyLanguage l, Dice d)
    {
        Language = l;
        Height = (float)d.Between(l.HeightMin, l.HeightMax);
        Width = (float)d.Between(0.98, 1.02);
        BrowRake = (float)d.Between(l.BrowRakeMin, l.BrowRakeMax);
        TailRake = (float)d.Between(l.TailRakeMin, l.TailRakeMax);
        BrowNarrow = l.BrowNarrow * (float)d.Between(0.97, 1.0);
        TailNarrow = l.TailNarrow * (float)d.Between(0.97, 1.0);
    }

    /// <summary>A body's look from the style's languages, on the body layer's "language" fork.</summary>
    public static BodyLook Roll(Vehicle v)
    {
        var d = v.Dice.Fork("language");
        var list = BodyLanguage.Of(v.Style);
        var roll = d.Next() * list.Sum(x => x.W);
        var pick = list[^1].L;
        foreach (var (l, w) in list)
            if ((roll -= w) < 0)
            {
                pick = l;
                break;
            }
        return new BodyLook(pick, d.Fork("proportions"));
    }

    /// <summary>The section a plan's token stands for: T tall, L low, N narrow; <paramref name="end"/> shrinks it for a brow (-1) or a tail (+1).</summary>
    public HullLoft.Section Section(string token, int end = 0)
    {
        var l = Language;
        var t = l.Tall;
        var ky = Height / t.Top;
        var kx = Width;
        if (token == "L")
            ky *= l.LowShare;
        else if (token == "N")
        {
            kx *= l.NarrowShare;
            ky *= (1 + l.LowShare) / 2;
        }
        if (end == 0)
            ky = MathF.Max(ky, 2.02f / t.Top); // (headroom at the centre: a player is 1.8 m)
        if (end < 0)
            (kx, ky) = (kx * BrowNarrow, ky * BrowRake);
        else if (end > 0)
            (kx, ky) = (kx * TailNarrow, ky * TailRake);
        kx = MathF.Max(kx, 0.76f / t.P[0].X); // (a seat or a crate stands half a metre out by a brow or a tail: the fixed bays' narrowest end is 0.74 m)
        var s = t.Scaled(kx, ky);
        if (end == 0 && token != "N")
            s = new HullLoft.Section(s.P.Select((p, i) => i == 0 || p.Y < 1.6f ? p with { X = MathF.Max(p.X, i == 0 ? 1.0f : 1.02f) } : p).ToArray()); // (furniture's metre)
        return s;
    }

    /// <summary>A plan's bay names ("Brow T", "Door", "Bay T-L", "Tail L") as lofted parts' keys, front to back.</summary>
    public string[] Names(string[] bays)
    {
        var names = new string[bays.Length];
        for (var k = 0; k < bays.Length; k++)
        {
            var b = bays[k];
            if (b == "Door")
            {
                var t = Section("T");
                names[k] = HullLoft.Key(1.5f, true, false, false, t, t);
                continue;
            }
            var parts = b.Split(' ');
            var tokens = parts[1].Split('-');
            var (front, back) = (tokens[0], tokens[^1]);
            names[k] = parts[0] switch
            {
                "Brow" => HullLoft.Key(1, false, true, false, Section(front, -1), Section(front)),
                "Tail" => HullLoft.Key(1, false, false, true, Section(back), Section(back, 1)),
                _ => HullLoft.Key(1, false, false, false, Section(front), Section(back)),
            };
        }
        return names;
    }

    public string Label => string.Create(CultureInfo.InvariantCulture, $"{Language.Name} {Height:0.00} m, brow {BrowRake:0.00}, tail {TailRake:0.00}");
}

public sealed partial class Vehicle
{
    /// <summary>Kit 5: the room's body language and this seed's roll of it (<see cref="BodyLook"/>); null: the fixed hull bays of kits 1 to 4.</summary>
    public BodyLook? Look { get; set; }

    /// <summary>The construct as built so far, for a measure (the report below).</summary>
    internal Construct Built => C;
}

/// <summary>
/// <c>--body-report Style [--seeds N] [--from S]</c>: what the rooms are made of and how their cross-sections differ (docs/vehiclekit/BODY-STYLES.md, "Measuring"):
/// distinct sections and bays, and a front-view distance the silhouette term can't see, within the style and against the other styles' rooms in the same run.
/// </summary>
public static class BodyReportCli
{
    private const int Bins = 12;

    /// <summary>A room's cross-section from the front: its half width in <see cref="Bins"/> bands of its own height, each over its widest.</summary>
    private static float[] Front(Vehicle v)
    {
        var bins = new float[Bins];
        if (v.Rooms.Count == 0)
            return bins;
        var bays = new HashSet<int>(v.Rooms.SelectMany(r => r.Bays).Select(b => b.Index));
        var (tris, owner) = VehiclePlan.PartTriangles(v.Built);
        float lo = float.MaxValue, hi = float.MinValue;
        for (var i = 0; i < tris.Length; i++)
            if (bays.Contains(owner[i / 3]))
                (lo, hi) = (MathF.Min(lo, tris[i].Y), MathF.Max(hi, tris[i].Y));
        if (hi <= lo)
            return bins;
        var mid = v.Rooms[0].Middle.X;
        for (var i = 0; i < tris.Length; i++)
            if (bays.Contains(owner[i / 3]))
            {
                var b = Math.Clamp((int)((tris[i].Y - lo) / (hi - lo) * Bins), 0, Bins - 1);
                bins[b] = MathF.Max(bins[b], MathF.Abs(tris[i].X - mid));
            }
        var most = bins.Max();
        for (var b = 0; b < Bins; b++)
            bins[b] = most > 0 ? bins[b] / most : 0;
        // Fill a band no vertex fell in from the one below (a tall flat wall has vertices only at its ends).
        for (var b = 1; b < Bins; b++)
            if (bins[b] == 0)
                bins[b] = bins[b - 1];
        return bins;
    }

    private static float Dist(float[] a, float[] b) => Enumerable.Range(0, Bins).Sum(i => MathF.Abs(a[i] - b[i])) / Bins;

    public static int Run(string styleName, string[] args)
    {
        string? Arg(string k) => args.SkipWhile(a => !a.Equals(k, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seeds = int.TryParse(Arg("--seeds"), out var n) ? n : 6;
        var from = int.TryParse(Arg("--from"), out var f) ? f : 1;
        var styles = styleName.Equals("all", StringComparison.OrdinalIgnoreCase) ? Styles.All.ToArray() : Styles.All.Where(s => s.Name.Equals(styleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (styles.Length == 0)
        {
            Console.WriteLine($"--body-report: no style {styleName}");
            return 1;
        }
        var fronts = new List<(string Style, float[] F)>();
        var failures = 0;
        foreach (var st in styles)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var langs = new Dictionary<string, int>(StringComparer.Ordinal);
            var mine = new List<float[]>();
            var ms = new List<double>();
            var parts = new List<int>();
            foreach (var g in VehicleRegistry.For(st, false).Where(g => ShapeGrammar.RecipeOf(g) != null))
                for (var seed = from; seed < from + seeds; seed++)
                {
                    try
                    {
                        var (_, v, _, t) = VehicleDiversity.Generate(g, seed);
                        ms.Add(t);
                        parts.Add(v.Built.PartCount);
                        foreach (var b in v.Rooms.SelectMany(r => r.Bays))
                        {
                            keys.Add(b.Def.Name);
                            if (b.Def.Name.StartsWith(HullLoft.Prefix, StringComparison.Ordinal))
                                foreach (var s in b.Def.Name[HullLoft.Prefix.Length..^1].Split(' ').Skip(1))
                                    sections.Add(s);
                            else
                                sections.Add(b.Def.Name);
                        }
                        var lang = v.Look?.Language.Name ?? "fixed bays";
                        langs[lang] = langs.GetValueOrDefault(lang) + 1;
                        if (v.Rooms.Count > 0)
                        {
                            var fr = Front(v);
                            mine.Add(fr);
                            fronts.Add((st.Name, fr));
                        }
                    }
                    catch (Exception e) when (e is VehicleKitException or ConstructPartException)
                    {
                        failures++;
                        Console.WriteLine($"  {g.Name} {seed}: {e.Message}");
                    }
                }
            var pairs = new List<float>();
            for (var i = 0; i < mine.Count; i++)
                for (var j = i + 1; j < mine.Count; j++)
                    pairs.Add(Dist(mine[i], mine[j]));
            Console.WriteLine($"{st.Name}: {ms.Count} vehicles, {mine.Count} with rooms; bay parts {keys.Count} distinct, sections {sections.Count} distinct; languages {string.Join(", ", langs.Select(x => $"{x.Key} {x.Value}"))}");
            Console.WriteLine($"  front profile: mean pair {(pairs.Count > 0 ? pairs.Average() : 0):0.000}; generation mean {(ms.Count > 0 ? ms.Average() : 0):0} ms, worst {(ms.Count > 0 ? ms.Max() : 0):0} ms; parts mean {(parts.Count > 0 ? parts.Average() : 0):0}");
            if (mine.Count > 0)
                Console.WriteLine("  mean front: " + string.Join(" ", Enumerable.Range(0, Bins).Select(b => mine.Average(x => x[b]).ToString("0.00", CultureInfo.InvariantCulture))));
        }
        if (styles.Length > 1)
        {
            Console.WriteLine("front profile between styles (mean pair; the diagonal is within the style):");
            var names = styles.Select(s => s.Name).ToArray();
            var sb = new StringBuilder("            ");
            foreach (var b in names)
                sb.Append($"{b,10}");
            Console.WriteLine(sb.ToString());
            foreach (var a in names)
            {
                sb.Clear().Append($"{a,-12}");
                foreach (var b in names)
                {
                    var d = (from x in fronts where x.Style == a from y in fronts where y.Style == b && !ReferenceEquals(x.F, y.F) select Dist(x.F, y.F)).DefaultIfEmpty(0).Average();
                    sb.Append($"{d,10:0.000}");
                }
                Console.WriteLine(sb.ToString());
            }
        }
        Console.WriteLine($"failures: {failures}");
        return failures;
    }
}

public static class LoftProbe // TEMP
{
    public static int Run(string[] args)
    {
        VehicleKitVersion.Force = 5;
        var g = VehicleRegistry.All.First(x => x.Name == "Bandit War Bus");
        var v = VehicleRegistry.Make(g, 1);
        var room = v.Rooms[0];
        var (tris, owner) = VehiclePlan.PartTriangles(v.Built);
        var off = room.FloorRest;
        Console.WriteLine($"room z {room.R.Z0}..{room.R.Z1} floortop {room.FloorTop} off {off}");
        foreach (var b in room.Bays)
            Console.WriteLine($"bay {b.Index} centre {v.CentreOf(b)} {b.Def.Name} boxes {b.Def.Boxes.Length}");
        for (var z = room.R.Z0 + 0.35f; z < room.R.Z1 - 0.3f; z += 0.25f)
        {
            var from = new Vector3(0, room.FloorTop + 1.1f, z) + off;
            var hits = new List<string>();
            for (var k = 0; k + 2 < tris.Length; k += 3)
                if (VehiclePlan.Hits(tris[k..(k + 3)], from, -Vector3.UnitX, 3.5f))
                    hits.Add(owner[k / 3] + ":" + v.Built.DefOf(owner[k / 3]).Name[..Math.Min(14, v.Built.DefOf(owner[k / 3]).Name.Length)]);
            Console.WriteLine($"z {z:0.00}: {string.Join(", ", hits.Distinct())}");
        }
        return 0;
    }
}
