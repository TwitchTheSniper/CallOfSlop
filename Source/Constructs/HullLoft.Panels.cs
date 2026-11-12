using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// Formed panels (docs/sandbox/constructs/production.md "The Panel Former"): bodywork a Panel Former makes from sheet. A panel is a run-time
/// part made from its name, as a lofted hull bay is, so blueprints, saves, the wire, racks and conveyors carry it with no second path:
/// <c>Formed Panel (hood 180x140 c12 r8 t20 steel)</c>: a kind, its length and width in cm, its crown (how far the middle stands proud
/// across the width, cm), its rake (the kind's second number, cm: a fender's arch, an S-bend's offset, a hood's nose drop), its thickness in
/// mm and its material. The kit's own lofts (<c>Hull Loft (...)</c>) are formed panels too: the Panel Former makes both from their names.
/// </summary>
public static partial class HullLoft
{
    public const string PanelPrefix = "Formed Panel (";
    public const int PanelMaxAlong = 12, PanelMaxAcross = 8; // facets: at most 96 a panel
    public const int PanelMinCm = 20, PanelMaxCm = 600, PanelStepCm = 5, PanelMaxCrownCm = 80, PanelMaxRakeCm = 200, PanelMinMm = 12, PanelMaxMm = 40;

    /// <summary>A preset: its key word, its name, and its default length, width, crown and rake (cm).</summary>
    public readonly record struct PanelPreset(string Word, string Label, int L, int W, int Crown, int Rake, string What);

    public static readonly PanelPreset[] PanelPresets =
    [
        new("curve", "Curved panel", 150, 100, 10, 0, "a plain panel crowned across its width; rake arches it along its length"),
        new("sbend", "S-bend", 200, 100, 0, 30, "a panel that steps by its rake along its length in an S"),
        new("hood", "Hood", 160, 150, 14, 18, "a crowned hood, narrower at the nose, the nose dropped by its rake"),
        new("fender", "Fender", 140, 45, 6, 45, "an arch over a wheel, as high as its rake"),
        new("door", "Door skin", 110, 120, 6, 8, "a door's outer skin: a gentle crown and the top turned in by its rake"),
        new("roofbow", "Roof bow", 200, 180, 22, 6, "a roof: flat in the middle, rolled down at the sides, arched along by its rake"),
        new("cowl", "Cowl", 60, 150, 30, 12, "a round cowl between a hood and a screen, rising by its rake"),
    ];

    /// <summary>A panel's key, read: its preset, sizes in cm, thickness in mm and material.</summary>
    public readonly record struct PanelShape(int Kind, int L, int W, int Crown, int Rake, int Mm, string Material);

    public static bool IsPanelName(string name) => name.StartsWith(PanelPrefix, StringComparison.Ordinal);

    /// <summary>A named part a Panel Former makes: a formed panel or one of the vehicle kit's lofted parts.</summary>
    public static bool IsFormedName(string name) => IsPanelName(name) || name.StartsWith(Prefix, StringComparison.Ordinal);

    public static string PanelKey(in PanelShape s) =>
        string.Create(CultureInfo.InvariantCulture, $"{PanelPrefix}{PanelPresets[s.Kind].Word} {s.L}x{s.W} c{s.Crown} r{s.Rake} t{s.Mm} {s.Material})");

    /// <summary>A preset at its default size, 20 mm thick.</summary>
    public static PanelShape PanelDefault(int kind, string material = "steel")
    {
        var p = PanelPresets[Math.Clamp(kind, 0, PanelPresets.Length - 1)];
        return new PanelShape(Math.Clamp(kind, 0, PanelPresets.Length - 1), p.L, p.W, p.Crown, p.Rake, 20, material);
    }

    /// <summary>Why a panel can't be made, or null. A key past a range is refused, never clamped, so every peer makes the same part or none.</summary>
    public static string? PanelRefusal(in PanelShape s)
    {
        if ((uint)s.Kind >= (uint)PanelPresets.Length)
            return "not a panel: no such preset";
        if (s.L < PanelMinCm || s.L > PanelMaxCm || s.W < PanelMinCm || s.W > PanelMaxCm || s.L % PanelStepCm != 0 || s.W % PanelStepCm != 0)
            return $"a panel is {PanelMinCm} to {PanelMaxCm} cm long and wide, in {PanelStepCm} cm steps";
        if (s.Crown < 0 || s.Crown > PanelMaxCrownCm || s.Crown > s.W / 2)
            return $"a panel's crown is 0 to {PanelMaxCrownCm} cm, at most half its width";
        if (Math.Abs(s.Rake) > PanelMaxRakeCm || Math.Abs(s.Rake) > s.L)
            return $"a panel's rake is within {PanelMaxRakeCm} cm and its own length";
        if (s.Mm < PanelMinMm || s.Mm > PanelMaxMm)
            return $"a panel is {PanelMinMm} to {PanelMaxMm} mm thick";
        if (RecyclingTable.Current.IdOf(s.Material) is var m && (m == 0 || ExtrusionTable.Current.Find(m) == null))
            return $"a panel can't be formed of {s.Material}: takes {ExtrusionTable.Current.Takes()}";
        return null;
    }

    /// <summary>A panel's key read back, or null: not a panel's name as a machine would write it.</summary>
    public static PanelShape? PanelOf(string name)
    {
        if (!IsPanelName(name) || !name.EndsWith(')'))
            return null;
        var w = name[PanelPrefix.Length..^1].Split(' ');
        if (w.Length < 6)
            return null;
        var kind = Array.FindIndex(PanelPresets, p => p.Word == w[0]);
        var size = w[1].Split('x');
        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        if (kind < 0 || size.Length != 2 || !Int(size[0], out var l) || !Int(size[1], out var wd)
            || w[2].Length < 2 || w[2][0] != 'c' || !Int(w[2][1..], out var crown)
            || w[3].Length < 2 || w[3][0] != 'r' || !Int(w[3][1..], out var rake)
            || w[4].Length < 2 || w[4][0] != 't' || !Int(w[4][1..], out var mm))
            return null;
        var s = new PanelShape(kind, l, wd, crown, rake, mm, string.Join(' ', w[5..]));
        return PanelRefusal(s) == null && PanelKey(s) == name ? s : null; // (one spelling a key: no "+8", no leading zeros)
    }

    /// <summary>The material a formed part is made of: a panel's own, steel for the kit's lofts (0: this build has no such material).</summary>
    public static byte FormedMaterial(string name) =>
        RecyclingTable.Current.IdOf(PanelOf(name) is { } s ? s.Material : "steel");

    /// <summary>What a formed part costs in units of its material: its mass at the material's weight, so it weighs what went in.</summary>
    public static int FormedUnits(ConstructPartDef d, byte material) =>
        Math.Max(1, (int)MathF.Ceiling(d.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(material))));

    /// <summary>The panel's surface: its height at s along (0 the back edge, 1 the nose) and x across (-1 to 1), metres; and its half width there.</summary>
    private static float PanelHeight(in PanelShape p, float s, float x)
    {
        float c = p.Crown / 100f, r = p.Rake / 100f;
        var bow = 1 - x * x;
        return p.Kind switch
        {
            0 => c * bow + r * 4 * s * (1 - s), // curve
            1 => c * bow + r * s * s * (3 - 2 * s), // sbend
            2 => c * bow * (1 - 0.35f * s) - r * s * s, // hood
            3 => c * bow + r * MathF.Sin(MathF.PI * s), // fender
            4 => c * bow + r * 0.25f * (x + 1) * (x + 1), // door: x = 1 is its top edge
            5 => c * (1 - x * x * x * x) + r * 4 * s * (1 - s), // roofbow
            _ => c * MathF.Sqrt(MathF.Max(0, 1 - x * x * 0.96f)) + r * s, // cowl
        };
    }

    private static float PanelHalf(in PanelShape p, float s) => p.W / 200f * (p.Kind == 2 ? 1 - 0.18f * s * s : 1);

    /// <summary>The facets a key takes: (along, across).</summary>
    public static (int Along, int Across) PanelGrid(in PanelShape p)
    {
        var bent = p.Rake != 0 && p.Kind is not (4 or 6);
        var along = bent ? Math.Clamp(p.L / 12, 6, PanelMaxAlong) : p.Kind == 2 ? 4 : 1;
        var across = p.Crown > 0 || p.Kind == 4 && p.Rake != 0 ? Math.Clamp(p.W / 15, 4, PanelMaxAcross) : 1;
        return (along, across);
    }

    private static Vector3 PanelPoint(in PanelShape p, float s, float x) => new(x * PanelHalf(p, s), PanelHeight(p, s, x), s * p.L / 100f);

    private static Vector3 PanelNormal(in PanelShape p, float s, float x)
    {
        const float e = 0.002f;
        var du = PanelPoint(p, MathF.Min(1, s + e), x) - PanelPoint(p, MathF.Max(0, s - e), x);
        var dv = PanelPoint(p, s, MathF.Min(1, x + e)) - PanelPoint(p, s, MathF.Max(-1, x - e));
        var n = Vector3.Cross(du, dv);
        return n.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(n);
    }

    /// <summary>(HullLoft.Make's hook.) A formed panel from its name; null: not a panel's name at all.</summary>
    private static (bool Ok, ConstructPartDef? Def)? MakePanel(string name)
    {
        if (!IsPanelName(name))
            return null;
        if (PanelOf(name) is not { } sh)
            return (false, null);
        try
        {
            var def = ConstructParts.Parse(PanelJson(name, sh), name);
            return Adopt(def, name) is { } made ? (true, made) : (false, null);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[panels] {name}: refused, {e.Message}");
            return (false, null);
        }
    }

    private static string PanelJson(string name, in PanelShape sh)
    {
        var p = new Builder();
        var (nu, nv) = PanelGrid(sh);
        var t = sh.Mm / 1000f;
        var area = 0f;
        var grid = new Vector3[nu + 1, nv + 1];
        var normal = new Vector3[nu + 1, nv + 1];
        for (var i = 0; i <= nu; i++)
            for (var j = 0; j <= nv; j++)
            {
                var (s, x) = (i / (float)nu, j * 2f / nv - 1);
                grid[i, j] = PanelPoint(sh, s, x);
                normal[i, j] = PanelNormal(sh, s, x);
            }
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                Vector3[] q = [grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]];
                Vector3[] off = [normal[i, j], normal[i, j + 1], normal[i + 1, j + 1], normal[i + 1, j]]; // (neighbours share each corner's offset: the seams close)
                var n = Vector3.Cross(q[2] - q[0], q[1] - q[3]);
                area += n.Length() / 2;
                // Rows shade a little by height, as a loft's, so a paint's tone shows the shape.
                var tone = (i + j) % 2 == 0 ? Steel : SteelHigh;
                p.Facet(q, n.LengthSquared() < 1e-12f ? Vector3.UnitY : n, nv == 1 && nu == 1 ? Steel : tone, t, off: off);
            }
        // Collision: up to four hidden boxes along its length, each over its strip's own height (a growing or riding panel collides by boxes, never a
        // hull). Their sides are whole centimetres just inside the drawn edges, so no hidden face shares a plane with a facet's rim and the edge
        // sockets, which stand on them, are whole centimetres too (docs/LESSONS.md, "Generated geometry").
        static float In(float v, bool up) => up ? MathF.Ceiling(v * 100 + 0.2f) / 100 : MathF.Floor(v * 100 - 0.2f) / 100;
        var strips = Math.Min(4, nu);
        var boxes = new (Vector3 Mn, Vector3 Mx)[strips];
        for (var k = 0; k < strips; k++)
        {
            int i0 = k * nu / strips, i1 = (k + 1) * nu / strips;
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var i = i0; i <= i1; i++)
                for (var j = 0; j <= nv; j++)
                    (mn, mx) = (Vector3.Min(mn, grid[i, j]), Vector3.Max(mx, grid[i, j]));
            mn = new Vector3(In(mn.X, true), MathF.Round(mn.Y, 2), k == 0 ? In(mn.Z, true) : MathF.Round(mn.Z, 2));
            mx = new Vector3(In(mx.X, false), MathF.Round(mx.Y, 2), k == strips - 1 ? In(mx.Z, false) : MathF.Round(mx.Z, 2));
            if (mx.Y - mn.Y < 0.03f)
                (mn.Y, mx.Y) = (mn.Y - 0.01f, mn.Y + 0.02f);
            boxes[k] = (mn, mx);
            p.Hide(mn, mx);
        }
        // Edge sockets, about every half metre: e/w along its sides, s/n across its ends, and one under its middle. Each stands on a face of the
        // hidden box of its strip, at the surface's height there.
        static float Cm2(float v) => MathF.Round(v, 2);
        int StripAt(float z)
        {
            for (var k = 0; k < strips; k++)
                if (z <= boxes[k].Mx.Z + 0.0001f)
                    return k;
            return strips - 1;
        }
        var along = Math.Clamp(sh.L / 50, 1, 6);
        var across = Math.Clamp(sh.W / 50, 1, 6);
        for (var k = 0; k < along; k++)
        {
            var s = (k + 0.5f) / along;
            var z = Cm2(s * sh.L / 100f);
            var (mn, mx) = boxes[StripAt(z)];
            z = Math.Clamp(z, mn.Z, mx.Z);
            p.Sock($"e{k}", new Vector3(mx.X, Math.Clamp(Cm2(PanelPoint(sh, s, 1).Y), mn.Y, mx.Y), z), "+x");
            p.Sock($"w{k}", new Vector3(mn.X, Math.Clamp(Cm2(PanelPoint(sh, s, -1).Y), mn.Y, mx.Y), z), "-x");
        }
        for (var k = 0; k < across; k++)
        {
            var x = (k + 0.5f) / across * 2 - 1;
            var (a0, a1) = boxes[0];
            var (b0, b1) = boxes[strips - 1];
            var ps = PanelPoint(sh, 0, x);
            var pn = PanelPoint(sh, 1, x);
            p.Sock($"s{k}", new Vector3(Math.Clamp(Cm2(ps.X), a0.X, a1.X), Math.Clamp(Cm2(ps.Y), a0.Y, a1.Y), a0.Z), "-z");
            p.Sock($"n{k}", new Vector3(Math.Clamp(Cm2(pn.X), b0.X, b1.X), Math.Clamp(Cm2(pn.Y), b0.Y, b1.Y), b1.Z), "+z");
        }
        var (m0, m1) = boxes[StripAt(sh.L / 200f)];
        p.Sock("back", new Vector3(0, m0.Y, Math.Clamp(Cm2(sh.L / 200f), m0.Z, m1.Z)), "-y");
        var mat = RecyclingTable.Current.IdOf(sh.Material);
        var row = ExtrusionTable.Current.Find(mat)!;
        var volume = area * t;
        var mass = MathF.Round(Math.Clamp(volume * row.UnitsPerM3 * RecyclingTable.Current.WeightOf(mat), 1, 4000), 1);
        var preset = PanelPresets[sh.Kind];
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"{preset.Label}, {sh.L / 100f:0.##} by {sh.W / 100f:0.##} m of {sh.Mm} mm {sh.Material}: {preset.What}. Formed by a Panel Former; weld it by its edges.",
            ["group"] = "Vehicle Body",
            ["section"] = "Formed panels",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(volume * row.HealthPerM3, 40, 4000)),
            ["armour"] = MathF.Round(Math.Clamp(row.ArmourPerCm * sh.Mm / 10f, 1, row.MaxArmour)),
            ["material"] = row.Class,
            ["color"] = Steel,
            ["attachSeconds"] = 2.0,
            ["stage"] = true, // (never offered in the menus: a Panel Former makes it)
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }

    /// <summary>
    /// <c>--form-panel PRESET [length=cm] [width=cm] [crown=cm] [rake=cm] [thick=mm] [material=NAME]</c>, or a whole key in quotes: prints the
    /// key, its facets, boxes and sockets, its mass and cost, and the former tier that takes it; checks it builds and that a second make from
    /// the name gives the same part. <c>--form-panel list</c> prints the presets. The exit code is the number of problems.
    /// </summary>
    public static int PanelCli(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            foreach (var pr in PanelPresets)
                Console.WriteLine($"{pr.Word,-8} {pr.Label,-13} {PanelKey(PanelDefault(Array.IndexOf(PanelPresets, pr)))}  ({pr.What})");
            return 0;
        }
        string name;
        if (IsFormedName(args[0]))
            name = args[0];
        else
        {
            var kind = Array.FindIndex(PanelPresets, p => p.Word.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (kind < 0)
            {
                Console.WriteLine($"no preset \"{args[0]}\": {string.Join(", ", PanelPresets.Select(p => p.Word))}");
                return 1;
            }
            var s = PanelDefault(kind);
            foreach (var a in args.Skip(1))
            {
                var kv = a.Split('=', 2);
                if (kv.Length != 2)
                {
                    Console.WriteLine($"\"{a}\": key=value (length, width, crown, rake, thick, material)");
                    return 1;
                }
                var num = int.TryParse(kv[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v);
                switch (kv[0].ToLowerInvariant())
                {
                    case "length" when num: s = s with { L = v }; break;
                    case "width" when num: s = s with { W = v }; break;
                    case "crown" when num: s = s with { Crown = v }; break;
                    case "rake" when num: s = s with { Rake = v }; break;
                    case "thick" when num: s = s with { Mm = v }; break;
                    case "material": s = s with { Material = kv[1] }; break;
                    default:
                        Console.WriteLine($"\"{a}\": key=value (length, width, crown, rake in cm; thick in mm; material)");
                        return 1;
                }
            }
            if (PanelRefusal(s) is { } why)
            {
                Console.WriteLine($"refused: {why}");
                return 1;
            }
            name = PanelKey(s);
        }
        if (Parse(name) is not { } d)
        {
            Console.WriteLine($"refused: \"{name}\" is not a panel any machine makes");
            return 1;
        }
        var bad = 0;
        var mat = FormedMaterial(name);
        int facets = 0, hidden = 0;
        foreach (var b in d.Boxes)
        {
            if (b.Detail)
                facets++;
            else
                hidden++;
        }
        var size = d.BoundsMax - d.BoundsMin;
        Console.WriteLine(name);
        Console.WriteLine($"  facets {facets}, collision boxes {hidden}, sockets {d.Sockets.Length} ({string.Join(' ', d.Sockets.Select(s => s.Name))})");
        Console.WriteLine($"  bounds {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, mass {d.Mass:0.#} kg, health {d.Health:0}, armour {d.Armour:0}");
        Console.WriteLine($"  cost {FormedUnits(d, mat)} {RecyclingTable.Current.NameOf(mat)}; made by: {ConstructPanelFormer.TierFor(d) ?? "no Panel Former is big enough"}");
        Console.WriteLine($"  hash {PanelHash(d):x8} (boxes, sockets, mass and health: the same on every peer)");
        // A second make from the name alone (the table forgotten) gives the same part.
        var first = PanelHash(d);
        lock (Interned)
            Interned.Remove(name);
        if (Parse(name) is not { } again || PanelHash(again) != first)
        {
            Console.WriteLine("  PROBLEM: a second make from the name differs");
            bad++;
        }
        if (IsPanelName(name) && facets > PanelMaxAlong * PanelMaxAcross)
        {
            Console.WriteLine($"  PROBLEM: {facets} facets, over {PanelMaxAlong * PanelMaxAcross}");
            bad++;
        }
        return bad;
    }

    /// <summary>A hash of a part's geometry and sockets as made: boxes, hull points, socket places and directions. Equal on two peers when they made the same part.</summary>
    public static uint PanelHash(ConstructPartDef d)
    {
        var h = 2166136261u;
        void Mix(float v) => h = (h ^ (uint)BitConverter.SingleToInt32Bits(MathF.Round(v, 4) + 0f)) * 16777619u;
        foreach (var b in d.Boxes)
        {
            Mix(b.Center.X); Mix(b.Center.Y); Mix(b.Center.Z);
            Mix(b.Half.X); Mix(b.Half.Y); Mix(b.Half.Z);
            h = (h ^ (b.Detail ? 1u : 0u)) * 16777619u;
        }
        foreach (var s in d.Sockets)
        {
            Mix(s.At.X); Mix(s.At.Y); Mix(s.At.Z);
            h = (h ^ s.Dir) * 16777619u;
            foreach (var ch in s.Name)
                h = (h ^ ch) * 16777619u;
        }
        Mix(d.Mass);
        Mix(d.Health);
        return h;
    }
}
