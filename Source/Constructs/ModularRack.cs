using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>How an Ordnance Rack's stations are laid out.</summary>
public enum RackLayout : byte
{
    Grid, // rows fore and aft by columns across, every station hanging below the beam
    Drum, // layers along an axis down the rack by segments round it: the drum turns each to the bottom to let it go
}

/// <summary>The old Modular Bomb Rack's station sizes (light, heavy, giant): only shapes from before the Ordnance Rack carry one.</summary>
public enum RackSize : byte
{
    Light,
    Heavy,
    Giant,
}

/// <summary>
/// What an Ordnance Rack is: its layout, A by B stations (grid: rows by columns; drum: layers by segments), the largest ordnance a station takes
/// (mm) and, for a shape from an older save, the Modular Bomb Rack size it was (<see cref="Legacy"/>, 1 to 3; 0 for every new shape). Count is
/// A x B, except an old odd rack (5 in pairs, a drum of 8), which keeps the stations it had. Count 0: not a modular rack.
/// </summary>
public readonly record struct RackShape(RackLayout Layout, byte A, byte B, byte Count, ushort LengthMm, ushort DiameterMm, byte Legacy = 0)
{
    public int Stations => Count;
    public float Length => LengthMm / 1000f;
    public float Diameter => DiameterMm / 1000f;
    public bool IsLegacy => Legacy != 0;
    public bool SameSettings(in RackShape o) => Layout == o.Layout && A == o.A && B == o.B && LengthMm == o.LengthMm && DiameterMm == o.DiameterMm;
}

/// <summary>
/// The Ordnance Rack (docs/sandbox/CONSTRUCTS.md, "Ordnance racks"): one rack for bombs and missiles whose shape is its settings, a grid of rows
/// and columns or a drum of layers and segments, sized by the longest and widest ordnance a station takes. Every station takes any of it: a design
/// whose motor lights at launch leaves forward like a missile, anything else drops (ConstructOrdnance.cs). A shape is one part def: the presets are
/// part files (a <c>"modularRack"</c> block that <see cref="Expand"/> turns into the whole part), and a shape no file has is made at run time,
/// one def a shape, in the extrusions' id space (read it through <c>CPart.D</c>, <c>DefOf</c> or <c>ConstructParts.Def</c>, never
/// <c>All[id]</c>). Its key is its name in saves and blueprints ("Ord Rack (2x4 grid 250x2000)": layout, then the widest by the longest ordnance in mm), nine bytes on the wire
/// (<see cref="Write"/>). The configure tool's Rack settings swap the part in place for the shape asked (<see cref="Reshape"/>, the Replacer's
/// own plan), so its welds and wires stay. The Modular Bomb Rack's shapes ("Bomb Rack (5 light, pairs)") and presets still load: each is the
/// Ordnance Rack with the same stations in the same places (<see cref="FromLegacy"/>), rated as it was, with no size limits.
/// </summary>
public static class ModularRack
{
    public const string Prefix = "Ord Rack ("; // (a run-time shape's name; run-time parts may be named up to ConstructParts.MaxRuntimeName characters)
    public const string LegacyPrefix = "Bomb Rack (";
    public const int MaxStations = ConstructParts.MaxSockets - 8; // in all (247: a part's 255 sockets, with room for its five mounts; a station's socket is a byte on the wire). A salvo lets one go a tick (about 4 s for a full rack), each a shell (ConstructWorld.MaxShells)
    public const float MaxReach = 190f; // m from its middle to its farthest edge (a part's boxes reach at most ConstructParts.MaxPartReach)
    public const int MaxSide = 16; // rows, columns, layers or segments
    public const int MinLengthMm = 250, MaxLengthMm = 60_000, MinDiameterMm = 50, MaxDiameterMm = 20_000;
    internal const byte WireKind = 247; // (after Extrusion.WireMark: a rack shape, nine bytes)

    public static readonly string[] LayoutWords = ["grid", "drum"];
    public static readonly string[] LegacyLayoutWords = ["row", "pairs", "drum"];
    public static readonly string[] SizeWords = ["light", "heavy", "giant"];

    /// <summary>An old station size's numbers: the longest and widest it stands for (m), the load a station (kg), mass, health, armour and cost.</summary>
    private readonly record struct SizeRow(float Length, float Diameter, float Station, float BaseMass, float MassEach, float BaseHealth, float HealthEach, float Armour, float Cost);

    private static readonly SizeRow[] Sizes =
    [
        new(1f, 0.25f, 500f, 2f, 4f, 60f, 10f, 3f, 1f), // (the Light Bomb, the Aerial Bomb and the Small Nuke all hang on one)
        new(2.5f, 0.625f, 5000f, 15f, 35f, 200f, 30f, 6f, 8f), // (the Heavy Bomb Rack's rating)
        new(8f, 2f, 2_000_000f, 300f, 900f, 1000f, 150f, 12f, 120f), // (the Giant Bomb Rack's)
    ];

    private const float TotalShare = 0.75f; // past the first station, each adds this share of a station's load to the whole rack's rating
    private const string Dark = "#2a2c26", Brass = "#c8a838", Hub = "#3a3d40";

    // ---- Ratings -----------------------------------------------------------------------------------------------------

    private static float Volume(float length, float diameter) => diameter * diameter * length;
    private static readonly float VLight = Volume(1f, 0.25f), VHeavy = Volume(2.5f, 0.625f), VGiant = Volume(8f, 2f);

    /// <summary>
    /// A station's rated load (kg) for the ordnance it takes: a curve through the old light, heavy and giant ratings at their sizes (500 kg at
    /// 25 cm by 1 m, 5 t at 62.5 cm by 2.5 m, 2000 t at 2 m by 8 m), a power of the station's volume between them and on past them.
    /// </summary>
    public static float StationLoad(float length, float diameter)
    {
        var v = MathF.Max(1e-6f, Volume(length, diameter));
        var load = v <= VHeavy
            ? 500f * MathF.Pow(v / VLight, MathF.Log(10f) / MathF.Log(VHeavy / VLight))
            : 5000f * MathF.Pow(v / VHeavy, MathF.Log(400f) / MathF.Log(VGiant / VHeavy));
        return Math.Clamp(MathF.Round(load), 1f, ConstructParts.MaxPartMass);
    }

    public static float StationLoad(in RackShape s) => s.IsLegacy ? Sizes[s.Legacy - 1].Station : StationLoad(s.Length, s.Diameter);

    /// <summary>The whole rack's rating: a station's, and three quarters of one for each more (a rack can't take its heaviest on every hook).</summary>
    public static float TotalLoad(in RackShape s) => StationLoad(s) * (1 + TotalShare * (s.Stations - 1));

    /// <summary>The size of a station against the old light one, by the cube root of its volume (1, 2.5 and 8 for light, heavy and giant).</summary>
    private static float Grade(in RackShape s) => MathF.Cbrt(Volume(s.Length, s.Diameter) / VLight);

    /// <summary>What a shape is worth, to the survival rule that the configure tool only makes a rack smaller.</summary>
    public static float Cost(in RackShape s) => s.Stations * (s.IsLegacy ? Sizes[s.Legacy - 1].Cost : MathF.Pow(Grade(s), 2.3f)) * (s.Layout == RackLayout.Drum ? 1.25f : 1f);

    // ---- Keys --------------------------------------------------------------------------------------------------------

    public static string Key(in RackShape s)
    {
        if (s.IsLegacy)
        {
            var word = s.Layout == RackLayout.Drum ? "drum" : s.A == 1 ? "row" : "pairs";
            return $"{LegacyPrefix}{s.Stations.ToString(CultureInfo.InvariantCulture)} {SizeWords[s.Legacy - 1]}, {word})";
        }
        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}{s.A}x{s.B} {LayoutWords[(int)s.Layout]} {s.DiameterMm}x{s.LengthMm})");
    }

    /// <summary>Why no rack has these settings (more stations than a rack may have, or too big a part), or null.</summary>
    public static string? Refusal(RackLayout layout, int a, int b, int lengthMm, int diameterMm)
    {
        if ((int)layout > 1 || a is < 1 or > MaxSide || b is < 1 or > MaxSide || lengthMm is < MinLengthMm or > MaxLengthMm || diameterMm is < MinDiameterMm or > MaxDiameterMm)
            return "those settings are out of range";
        if (a * b > MaxStations)
            return $"at most {MaxStations} stations on a rack: {a} x {b} is {a * b}";
        var s = new RackShape(layout, (byte)a, (byte)b, (byte)(a * b), (ushort)lengthMm, (ushort)diameterMm);
        return Reach(s) > MaxReach ? $"too big: it would reach {Reach(s):0} m from its middle, and a part reaches at most {MaxReach:0} m" : null;
    }

    /// <summary>A new shape from its settings (null: <see cref="Refusal"/> says why not).</summary>
    public static RackShape? Make(RackLayout layout, int a, int b, int lengthMm, int diameterMm) =>
        Refusal(layout, a, b, lengthMm, diameterMm) == null ? new RackShape(layout, (byte)a, (byte)b, (byte)(a * b), (ushort)lengthMm, (ushort)diameterMm) : null;

    /// <summary>How far its boxes reach from its middle, m (the largest of its half sizes).</summary>
    private static float Reach(in RackShape s)
    {
        var sc = Sc(s);
        if (s.Layout == RackLayout.Drum)
            return MathF.Max(PitchZ(s) * s.A / 2 + 0.05f * sc, MathF.Max(YokeTop(s) + 0.05f * sc, DrumRadius(s) * 1.2f));
        return MathF.Max(s.Diameter * s.B / 2, PitchZ(s) * (s.A - 1) / 2 + 0.2f * s.Length);
    }

    /// <summary>An old Modular Bomb Rack shape as the Ordnance Rack with the same stations in the same places.</summary>
    public static RackShape FromLegacy(int stations, int layout, int size)
    {
        var z = Sizes[size];
        var (lay, a, b) = layout switch
        {
            1 => (RackLayout.Grid, 2, (stations + 1) / 2), // pairs: two rows, fore and aft
            2 => (RackLayout.Drum, (stations + 2) / 3, 3), // rings of three: bottom, left, right
            _ => (RackLayout.Grid, 1, stations),
        };
        return new RackShape(lay, (byte)a, (byte)b, (byte)stations, (ushort)MathF.Round(z.Length * 1000), (ushort)MathF.Round(z.Diameter * 1000), (byte)(size + 1));
    }

    /// <summary>A key back into a shape, or null: an Ordnance Rack's, or a Modular Bomb Rack's from before.</summary>
    public static RackShape? ParseKey(string key)
    {
        if (!key.EndsWith(')'))
            return null;
        if (key.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var body = key[LegacyPrefix.Length..^1].Split(',', StringSplitOptions.TrimEntries);
            if (body.Length != 2)
                return null;
            var head = body[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length != 2 || !int.TryParse(head[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 1 or > 16)
                return null;
            var size = Array.FindIndex(SizeWords, w => w.Equals(head[1], StringComparison.OrdinalIgnoreCase));
            var layout = Array.FindIndex(LegacyLayoutWords, w => w.Equals(body[1], StringComparison.OrdinalIgnoreCase));
            return size < 0 || layout < 0 ? null : FromLegacy(n, layout, size);
        }
        if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var words = key[Prefix.Length..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 3)
            return null;
        var ab = words[0].Split('x');
        var dl = words[2].Split('x');
        var lay = Array.FindIndex(LayoutWords, w => w.Equals(words[1], StringComparison.OrdinalIgnoreCase));
        if (ab.Length != 2 || dl.Length != 2 || lay < 0
            || !int.TryParse(ab[0], NumberStyles.None, CultureInfo.InvariantCulture, out var a) || !int.TryParse(ab[1], NumberStyles.None, CultureInfo.InvariantCulture, out var b)
            || !int.TryParse(dl[0], NumberStyles.None, CultureInfo.InvariantCulture, out var dia) || !int.TryParse(dl[1], NumberStyles.None, CultureInfo.InvariantCulture, out var len))
            return null;
        return Make((RackLayout)lay, a, b, len, dia);
    }

    /// <summary>
    /// A part file's or a logic block's <c>"modular"</c> shape: <c>{"layout": "grid", "rows": 2, "columns": 4, "length": 2, "diameter": 0.25}</c>
    /// (a drum's <c>"layers"</c> and <c>"segments"</c>), or a Modular Bomb Rack's <c>{"stations": 8, "layout": "drum", "size": "heavy"}</c>.
    /// </summary>
    public static RackShape ShapeOf(JsonElement e, string file)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: a rack's shape is {{\"layout\": grid or drum, \"rows\" and \"columns\" (a drum's \"layers\" and \"segments\"), \"length\" and \"diameter\" in m}}");
        if (ConstructParts.Opt(e, "stations") != null)
        {
            ConstructParts.Known(e, ["stations", "layout", "size"], $"{file}: the rack's shape");
            var n = ConstructParts.Num(ConstructParts.Need(e, "stations", file), file, "stations");
            if (n != MathF.Floor(n) || n < 1 || n > 16)
                throw new ConstructPartException($"{file}: an old Modular Bomb Rack has 1 to 16 stations, not {n.ToString(CultureInfo.InvariantCulture)}");
            var old = Array.IndexOf(LegacyLayoutWords, ConstructParts.Str(ConstructParts.Need(e, "layout", file), file, "layout").ToLowerInvariant());
            var size = Array.IndexOf(SizeWords, ConstructParts.Str(ConstructParts.Need(e, "size", file), file, "size").ToLowerInvariant());
            if (old < 0 || size < 0)
                throw new ConstructPartException($"{file}: an old rack's layout is row, pairs or drum and its size light, heavy or giant");
            return FromLegacy((int)n, old, size);
        }
        var layout = Array.IndexOf(LayoutWords, ConstructParts.Str(ConstructParts.Need(e, "layout", file), file, "layout").ToLowerInvariant());
        if (layout < 0)
            throw new ConstructPartException($"{file}: a rack's layout is grid or drum");
        var (ka, kb) = layout == (int)RackLayout.Drum ? ("layers", "segments") : ("rows", "columns");
        ConstructParts.Known(e, ["layout", ka, kb, "length", "diameter"], $"{file}: the rack's shape");
        var a = ConstructParts.Num(ConstructParts.Need(e, ka, file), file, ka);
        var b = ConstructParts.Num(ConstructParts.Need(e, kb, file), file, kb);
        var len = ConstructParts.Num(ConstructParts.Need(e, "length", file), file, "length");
        var dia = ConstructParts.Num(ConstructParts.Need(e, "diameter", file), file, "diameter");
        if (a != MathF.Floor(a) || b != MathF.Floor(b))
            throw new ConstructPartException($"{file}: a rack's {ka} and {kb} are whole numbers");
        return Make((RackLayout)layout, (int)a, (int)b, (int)MathF.Round(len * 1000), (int)MathF.Round(dia * 1000))
            ?? throw new ConstructPartException($"{file}: a rack has 1 to {MaxSide} {ka} and {kb}, at most {MaxStations} stations, ordnance {MinLengthMm / 1000f:0.##} to {MaxLengthMm / 1000} m long and {MinDiameterMm / 1000f:0.##} to {MaxDiameterMm / 1000} m wide");
    }

    /// <summary>A preset file (name, description, <c>"modularRack"</c>, and <c>"legacy"</c> for the old presets) as the whole part's text, for <see cref="ConstructParts.Parse"/>.</summary>
    public static string Expand(JsonElement root, JsonElement shape, string file)
    {
        ConstructParts.Known(root, ["format", "name", "description", "modularRack", "legacy"], file);
        var name = ConstructParts.Str(ConstructParts.Need(root, "name", file), file, "name");
        var about = ConstructParts.Opt(root, "description") is { } d ? ConstructParts.Str(d, file, "description") : "";
        var legacy = ConstructParts.Opt(root, "legacy") is { ValueKind: JsonValueKind.True };
        return Json(name, about, ShapeOf(shape, file), runtime: false, legacy);
    }

    // ---- The shape's part -------------------------------------------------------------------------------------------

    private static JsonArray V(float x, float y, float z) => [MathF.Round(x, 4), MathF.Round(y, 4), MathF.Round(z, 4)];

    private static JsonObject Box(float x0, float y0, float z0, float x1, float y1, float z1, string? colour = null, bool detail = false, bool hidden = false, string? shape = null)
    {
        var b = new JsonObject { ["min"] = V(x0, y0, z0), ["max"] = V(x1, y1, z1) };
        if (colour != null)
            b["color"] = colour;
        if (detail)
            b["detail"] = true;
        if (hidden)
            b["hidden"] = true;
        if (shape != null)
        {
            b["shape"] = shape;
            b["segments"] = 20;
        }
        return b;
    }

    private static JsonObject Sock(string name, float x, float y, float z, string dir) => new() { ["name"] = name, ["at"] = V(x, y, z), ["dir"] = dir };

    /// <summary>The scale of the rack's beams and hooks: its diameter against the old light station's 25 cm (never under 0.4: a box is at least a centimetre).</summary>
    private static float Sc(in RackShape s) => MathF.Max(0.4f, s.Diameter / 0.25f);

    /// <summary>A hook's half length fore and aft: the old one's, short of the next row's.</summary>
    private static float HookDeep(in RackShape s) => MathF.Min(0.07f * Sc(s), 0.3f * PitchZ(s));

    /// <summary>Fore and aft between a grid's rows or a drum's layers: a tenth more than the ordnance's length.</summary>
    private static float PitchZ(in RackShape s) => 1.1f * s.Length;

    /// <summary>A drum's faces in the order its segments fill them (0 bottom, 1 left, 2 right, 3 top) and how many sit side by side on each.</summary>
    private static int Faces(int segments, Span<int> count)
    {
        count.Clear();
        switch (segments)
        {
            case 1:
                count[0] = 1;
                return 1;
            case 2:
                count[1] = count[2] = 1;
                return 2;
            case 3:
                count[0] = count[1] = count[2] = 1;
                return 3;
        }
        for (var f = 0; f < 4; f++)
            count[f] = segments / 4;
        ReadOnlySpan<int> extra = [0, 3, 1, 2]; // (bottom and top first: the drum stays balanced side to side)
        for (var k = 0; k < segments % 4; k++)
            count[extra[k]]++;
        return 4;
    }

    private static int MostOnAFace(int segments)
    {
        Span<int> count = stackalloc int[4];
        Faces(segments, count);
        return Math.Max(Math.Max(count[0], count[1]), Math.Max(count[2], count[3]));
    }

    /// <summary>A drum's radius to its hooks: the old drum's (0.48 of the diameter), and half a diameter more for each more side by side on a face.</summary>
    public static float DrumRadius(in RackShape s) => s.Diameter * (0.48f + 0.5f * (MostOnAFace(s.B) - 1));

    /// <summary>A drum of four faces hangs by a yoke over its top row; this is the yoke's top (the part's frame).</summary>
    private static float YokeTop(in RackShape s) => DrumRadius(s) + 1.2f * s.Diameter + 0.1f * Sc(s);

    /// <summary>Where station <paramref name="k"/> is, and which way its hook faces (the part's frame). A grid numbers its stations column by column.</summary>
    public static (Vector3 At, string Dir) Station(in RackShape s, int k)
    {
        var sc = Sc(s);
        if (s.Layout == RackLayout.Drum)
        {
            var layer = k / s.B;
            var seg = k % s.B;
            var len = PitchZ(s) * s.A;
            var z = -len / 2 + PitchZ(s) * (layer + 0.5f);
            var r = DrumRadius(s);
            Span<int> count = stackalloc int[4];
            Faces(s.B, count);
            var face = 0;
            while (face < 3 && seg >= count[face])
                seg -= count[face++];
            var t = (seg - (count[face] - 1) * 0.5f) * s.Diameter;
            return face switch
            {
                0 => (new Vector3(t, -r, z), "-y"),
                1 => (new Vector3(-r, t, z), "-x"),
                2 => (new Vector3(r, t, z), "+x"),
                _ => (new Vector3(t, r, z), "+y"),
            };
        }
        var row = k % s.A;
        var col = k / s.A;
        var w = s.Diameter * s.B;
        var d = PitchZ(s) * s.A;
        return (new Vector3(-w / 2 + s.Diameter * (col + 0.5f), -0.04f * sc, s.A == 1 ? 0 : -d / 2 + PitchZ(s) * (row + 0.5f)), "-y");
    }

    /// <summary>The part's whole text: boxes, sockets, numbers and settings from the shape. Every box that meets another overlaps it or shares its colour, so no two faces z-fight.</summary>
    public static string Json(string name, string description, in RackShape s, bool runtime, bool legacy = false)
    {
        var sc = Sc(s);
        var n = s.Stations;
        var boxes = new JsonArray();
        var sockets = new JsonArray();
        for (var k = 0; k < n; k++)
        {
            var (at, dir) = Station(s, k);
            sockets.Add(Sock($"lug{k}", at.X, at.Y, at.Z, dir));
        }
        float mass, health, armour, attach;
        if (s.IsLegacy)
        {
            var z = Sizes[s.Legacy - 1];
            mass = z.BaseMass + z.MassEach * n;
            health = z.BaseHealth + z.HealthEach * n;
            armour = z.Armour;
            attach = 1.5f + 0.1f * n * (s.Legacy == 1 ? 1 : 2);
        }
        else
        {
            var g = Grade(s);
            mass = 2f * MathF.Pow(g, 2.6f) + 4f * MathF.Pow(g, 2.4f) * n;
            health = 60f * MathF.Pow(g, 1.3f) + 10f * MathF.Pow(g, 1.2f) * n;
            armour = MathF.Min(50f, 3f * MathF.Pow(g, 0.67f));
            attach = 1.5f + 0.1f * n * Math.Clamp(g, 1f, 4f);
        }
        if (s.Layout == RackLayout.Drum)
        {
            var half = PitchZ(s) * s.A / 2;
            var r = DrumRadius(s);
            var cap = 0.03f * sc;
            var yoke = s.B >= 4; // (a top row: it hangs by a yoke over it, not a spine)
            boxes.Add(Box(-r, -r, -half, r, r, half, hidden: true)); // (the drum's collider)
            boxes.Add(Box(-r, -r, -half - cap * 0.5f, r, r, half + cap * 0.5f, Hub, detail: true, shape: "cylinder")); // (its ends inside the end plates)
            boxes.Add(Box(-r * 1.15f, -r * 1.15f, -half - cap, r * 1.15f, r * 1.15f, -half, Dark));
            boxes.Add(Box(-r * 1.15f, -r * 1.15f, half, r * 1.15f, r * 1.15f, half + cap, Dark));
            float top;
            if (yoke)
            {
                top = YokeTop(s);
                var e = Math.Clamp(0.003f * sc, 0.003f, 0.02f); // (the arms stand a little proud of the plates, the beam of the arms: no shared faces)
                boxes.Add(Box(-0.06f * sc, r, -half - cap - e, 0.06f * sc, top, -half + e, Dark)); // (the yoke's arms, up from the end plates)
                boxes.Add(Box(-0.06f * sc, r, half - e, 0.06f * sc, top, half + cap + e, Dark));
                top += e;
                boxes.Add(Box(-0.05f * sc, top - 0.06f * sc, -half - cap, 0.05f * sc, top, half + cap, Dark)); // (and its beam over the top row)
            }
            else
            {
                top = r + 0.08f * sc;
                boxes.Add(Box(-0.05f * sc, r * 0.8f, -half - cap * 0.5f, 0.05f * sc, top, half + cap * 0.5f, Dark)); // (the hanger spine along its top)
            }
            // The hooks: one a station while the part's boxes allow (ConstructParts.MaxBoxes; merged meshes draw them all as
            // one), else a rail along the layers for each place round the drum, as long as its first and last hooks.
            var own = boxes.Count + n <= ConstructParts.MaxBoxes;
            for (var k = 0; k < (own ? n : Math.Min(s.B, n)); k++)
            {
                var (at, dir) = Station(s, k);
                var last = own ? at.Z : Station(s, k + s.B * ((n - 1 - k) / s.B)).At.Z;
                var inset = dir is "-x" or "+x" ? Math.Clamp(0.004f * sc, 0.002f, 0.02f) : 0f; // (the side rails a little shorter: no face shared with a bottom or top one at a corner)
                float lo = r + 0.04f * sc, hi = r - 0.02f * sc, wide = 0.05f * sc, deep = HookDeep(s), z0 = at.Z - deep + inset, z1 = last + deep - inset;
                boxes.Add(dir switch
                {
                    "-y" => Box(at.X - wide, -lo, z0, at.X + wide, -hi, z1, Brass, detail: true),
                    "-x" => Box(-lo, at.Y - wide, z0, -hi, at.Y + wide, z1, Brass, detail: true),
                    "+x" => Box(hi, at.Y - wide, z0, lo, at.Y + wide, z1, Brass, detail: true),
                    _ => Box(at.X - wide, hi, z0, at.X + wide, lo, z1, Brass, detail: true),
                });
            }
            sockets.Add(Sock("top", 0, top, 0, "+y"));
            sockets.Add(Sock("back", 0, 0, half + cap, "+z"));
            sockets.Add(Sock("front", 0, 0, -half - cap, "-z"));
            mass *= 1.25f; // (the drum's turning gear)
        }
        else
        {
            var w = s.Diameter * s.B / 2;
            var d = PitchZ(s) * (s.A - 1) / 2 + 0.2f * s.Length;
            boxes.Add(Box(-w, -0.04f * sc, -d, w, 0.08f * sc, d));
            // The hooks, their tops inside the beam: one a station while the part's boxes allow (ConstructParts.MaxBoxes; merged
            // meshes draw them all as one), else a rail fore and aft for each column.
            var deep = HookDeep(s);
            if (boxes.Count + n <= ConstructParts.MaxBoxes)
                for (var k = 0; k < n; k++)
                {
                    var (at, _) = Station(s, k);
                    boxes.Add(Box(at.X - 0.05f * sc, -0.08f * sc, at.Z - deep, at.X + 0.05f * sc, -0.035f * sc, at.Z + deep, Brass, detail: true));
                }
            else
                for (var col = 0; col * s.A < n; col++)
                {
                    var (at, _) = Station(s, col * s.A);
                    var last = Station(s, Math.Min(n - 1, col * s.A + s.A - 1)).At.Z;
                    boxes.Add(Box(at.X - 0.05f * sc, -0.08f * sc, at.Z - deep, at.X + 0.05f * sc, -0.035f * sc, last + deep, Brass, detail: true));
                }
            sockets.Add(Sock("top", 0, 0.08f * sc, 0, "+y"));
            sockets.Add(Sock("back", 0, 0, d, "+z"));
            sockets.Add(Sock("front", 0, 0, -d, "-z"));
            sockets.Add(Sock("right", w, 0, 0, "+x"));
            sockets.Add(Sock("left", -w, 0, 0, "-x"));
        }
        var drum = s.Layout == RackLayout.Drum;
        var modular = s.IsLegacy
            ? new JsonObject { ["stations"] = n, ["layout"] = drum ? "drum" : s.A == 1 ? "row" : "pairs", ["size"] = SizeWords[s.Legacy - 1] }
            : new JsonObject { ["layout"] = LayoutWords[(int)s.Layout], [drum ? "layers" : "rows"] = s.A, [drum ? "segments" : "columns"] = s.B, ["length"] = s.Length, ["diameter"] = s.Diameter };
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = (description.Length > 0 ? description + " " : "") + Describe(s),
            ["group"] = "Weapons",
            ["section"] = "Ordnance racks",
            ["role"] = "logic",
            ["tool"] = "welder",
            ["mass"] = Math.Clamp(MathF.Round(mass, 1), 0.5f, ConstructParts.MaxPartMass),
            ["health"] = MathF.Round(MathF.Min(health, ConstructParts.MaxPartHealth)),
            ["armour"] = MathF.Round(armour, 1),
            ["material"] = "steel",
            ["color"] = Dark,
            ["attachSeconds"] = MathF.Min(ConstructParts.MaxAttachSeconds, MathF.Round(attach, 2)),
            ["logic"] = new JsonObject
            {
                ["kind"] = "bombrack",
                ["load"] = TotalLoad(s),
                ["station"] = StationLoad(s),
                ["modular"] = modular,
            },
            ["boxes"] = boxes,
            ["sockets"] = sockets,
            ["config"] = new JsonArray
            {
                new JsonObject { ["key"] = "rackLayout", ["type"] = "choice", ["label"] = "Layout", ["group"] = "Rack", ["options"] = new JsonArray(LayoutWords.Select(o => (JsonNode)o).ToArray()), ["default"] = (int)s.Layout,
                    ["tooltip"] = "Grid: rows fore and aft by columns across, all hanging below. Drum: layers along its axis by segments round it, each turned to the bottom to let go." },
                new JsonObject { ["key"] = "rackRows", ["label"] = drum ? "Layers" : "Rows", ["group"] = "Rack", ["min"] = 1, ["max"] = MaxSide, ["step"] = 1, ["default"] = s.A,
                    ["tooltip"] = (drum ? "Rings along the drum's axis." : "Rows fore and aft.") + $" At most {MaxStations} stations in all. Change it with nothing on its hooks; in survival a rack only gets smaller here." },
                new JsonObject { ["key"] = "rackColumns", ["label"] = drum ? "Segments" : "Columns", ["group"] = "Rack", ["min"] = 1, ["max"] = MaxSide, ["step"] = 1, ["default"] = s.B,
                    ["tooltip"] = (drum ? "Stations round the drum: on its bottom, sides and top, side by side past four." : "Stations side by side across.") + $" At most {MaxStations} stations in all." },
                new JsonObject { ["key"] = "rackLength", ["label"] = "Max ordnance length", ["group"] = "Rack", ["unit"] = "m", ["min"] = MinLengthMm / 1000f, ["max"] = MaxLengthMm / 1000f, ["step"] = 0.05f, ["default"] = s.Length,
                    ["tooltip"] = "The longest bomb or missile a station takes. The spacing, the rack's size and its rating follow." + (s.IsLegacy ? " (An old rack takes anything; set this and it becomes an Ordnance Rack with limits.)" : "") },
                new JsonObject { ["key"] = "rackDiameter", ["label"] = "Max ordnance diameter", ["group"] = "Rack", ["unit"] = "m", ["min"] = MinDiameterMm / 1000f, ["max"] = MaxDiameterMm / 1000f, ["step"] = 0.005f, ["default"] = s.Diameter,
                    ["tooltip"] = "The widest body a station takes (fins may overlap the next). The spacing, the rack's size and its rating follow." },
                new JsonObject { ["key"] = "fireOrder", ["type"] = "choice", ["label"] = "Fire order", ["group"] = "Release", ["options"] = new JsonArray("one at a time", "salvo"), ["default"] = 0,
                    ["tooltip"] = "One at a time: one each interval while its input is on. Salvo: one press lets everything go, a tick apart. A drum always turns one to the bottom each interval." },
                new JsonObject { ["key"] = "rackInterval", ["label"] = "Interval", ["group"] = "Release", ["unit"] = "s", ["min"] = 0.05f, ["max"] = 3, ["step"] = 0.05f, ["default"] = 0.35f,
                    ["tooltip"] = "Seconds between releases one at a time, and a drum's turn." },
            },
        };
        if (runtime)
            part["stage"] = true; // (never offered: the configure tool makes it from a preset)
        if (legacy)
            part["legacy"] = true; // (an old preset: it loads and works, but the Tab inventory doesn't offer it)
        return part.ToJsonString();
    }

    /// <summary>The shape in words, for the description and the look line.</summary>
    public static string Describe(in RackShape s)
    {
        var where = s.Layout == RackLayout.Drum
            ? $"{s.A} layer{(s.A == 1 ? "" : "s")} of {s.B} round a drum that turns each to the bottom to let it go"
            : s.A == 1 ? "side by side" : $"in {s.A} rows of {s.B}";
        var size = s.IsLegacy ? $"{SizeWords[s.Legacy - 1]} station{(s.Stations == 1 ? "" : "s")} (any size)" : $"station{(s.Stations == 1 ? "" : "s")} for ordnance up to {s.Diameter * 100:0.#} cm by {s.Length:0.##} m";
        return $"{s.Stations} {size} {where}. Bombs drop; missiles launch forward. Rated for {StationLoad(s):0} kg a station, {TotalLoad(s):0} kg in all.";
    }

    // ---- One def a shape --------------------------------------------------------------------------------------------

    private static readonly Dictionary<RackShape, ConstructPartDef> Interned = new();
    private static readonly Dictionary<RackShape, ConstructPartDef> Presets = new();
    private static IReadOnlyList<ConstructPartDef>? _presetsOf;

    private static bool Valid(in RackShape s) => s.Count >= 1 && s.Count <= MaxStations && (int)s.Layout <= 1 && s.A >= 1 && s.B >= 1 && s.A <= MaxSide && s.B <= MaxSide && s.Count <= s.A * s.B
        && s.Legacy <= 3 && s.LengthMm is >= MinLengthMm and <= MaxLengthMm && s.DiameterMm is >= MinDiameterMm and <= MaxDiameterMm && (s.IsLegacy || s.Count == s.A * s.B);

    /// <summary>The part for a shape: a preset file's when one has it, else one made the first time. Null: the run-time table is full.</summary>
    public static ConstructPartDef? Def(RackShape s)
    {
        if (!Valid(s))
            return null;
        var all = ConstructParts.All;
        lock (Interned)
        {
            if (!ReferenceEquals(_presetsOf, all))
            {
                Presets.Clear();
                foreach (var d in all)
                    if (d.IsModularRack && !d.IsRackRuntime)
                        Presets.TryAdd(d.Rack, d);
                _presetsOf = all;
            }
            if (Presets.TryGetValue(s, out var preset))
                return preset;
            if (Interned.TryGetValue(s, out var have))
            {
                have.Touched = Environment.TickCount64;
                return have;
            }
            var key = Key(s);
            ConstructPartDef def;
            try
            {
                def = ConstructParts.Parse(Json(key, "", s, runtime: true), key);
            }
            catch (ConstructPartException)
            {
                return null; // (a shape the rules refuse: can't happen with the numbers above)
            }
            def.IsRackRuntime = true;
            def.ExHash = Fnv(key);
            if (!Extrusion.Adopt(def))
                return null;
            Interned[s] = def;
            return def;
        }
    }

    /// <summary>(RuntimeParts) Drops the doomed racks from the key table.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
            RuntimeParts.Drop(Interned, doomed);
    }

    /// <summary>A rack's key (a blueprint's or a save's name) as its part.</summary>
    public static ConstructPartDef? Parse(string key) => ParseKey(key) is { } s ? Def(s) : null;

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>After <see cref="Extrusion.WireMark"/>: the shape, nine bytes (layout and old size, A, B, count, length and diameter in mm).</summary>
    public static void Write(PacketWriter w, ConstructPartDef d)
    {
        var s = d.Rack;
        w.U8((byte)((byte)s.Layout | s.Legacy << 4)).U8(s.A).U8(s.B).U8(s.Count).U16(s.LengthMm).U16(s.DiameterMm);
    }

    /// <summary>The shape from the wire, as this machine's part id. A stand-in, never a dropped packet, if the table is full.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var head = r.U8();
        var s = new RackShape((RackLayout)(head & 15), r.U8(), r.U8(), r.U8(), r.U16(), r.U16(), (byte)(head >> 4));
        if (!Valid(s))
            throw new MalformedPacketException($"an ordnance rack of {s.A} x {s.B} ({s.Count}), layout {(int)s.Layout}, {s.DiameterMm} x {s.LengthMm} mm");
        if (Def(s) is { } d)
            return d.Id;
        return ConstructParts.Need("Ordnance Rack").Id;
    }

    // ---- The configure tool -----------------------------------------------------------------------------------------

    private static int Mm(float m) => (int)MathF.Round(m * 1000);

    /// <summary>
    /// The shape a rack's settings ask for: its own while they are its own (so an old rack stays the old rack until a shape setting changes),
    /// else the new shape, or null when the settings ask for more stations than a rack may have.
    /// </summary>
    public static RackShape? Asked(in CPart p, ConstructPartDef d)
    {
        var r = d.Rack;
        var layout = (RackLayout)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.RackLayout, (int)r.Layout), 0, 1);
        var a = Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.RackRows, r.A)), 1, MaxSide);
        var b = Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.RackColumns, r.B)), 1, MaxSide);
        var len = Math.Clamp(Mm(ConstructConfig.Get(p, d, CfgKey.RackLength, r.Length)), MinLengthMm, MaxLengthMm);
        var dia = Math.Clamp(Mm(ConstructConfig.Get(p, d, CfgKey.RackDiameter, r.Diameter)), MinDiameterMm, MaxDiameterMm);
        var want = new RackShape(layout, (byte)a, (byte)b, 0, (ushort)len, (ushort)dia);
        if (want.SameSettings(r))
            return r;
        return Make(layout, a, b, len, dia);
    }

    /// <summary>Why the shape its settings ask for can't be made, or null.</summary>
    public static string? AskedRefusal(in CPart p, ConstructPartDef d)
    {
        var r = d.Rack;
        return Refusal((RackLayout)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.RackLayout, (int)r.Layout), 0, 1),
            Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.RackRows, r.A)), 1, MaxSide),
            Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.RackColumns, r.B)), 1, MaxSide),
            Math.Clamp(Mm(ConstructConfig.Get(p, d, CfgKey.RackLength, r.Length)), MinLengthMm, MaxLengthMm),
            Math.Clamp(Mm(ConstructConfig.Get(p, d, CfgKey.RackDiameter, r.Diameter)), MinDiameterMm, MaxDiameterMm));
    }

    /// <summary>A shape with one of its settings changed (a blueprint's refusal names the part it means).</summary>
    public static RackShape WithSetting(RackShape s, CfgKey k, float v)
    {
        var (layout, a, b, len, dia) = (s.Layout, (int)s.A, (int)s.B, (int)s.LengthMm, (int)s.DiameterMm);
        switch (k)
        {
            case CfgKey.RackLayout: layout = (RackLayout)Math.Clamp((int)v, 0, 1); break;
            case CfgKey.RackRows: a = Math.Clamp((int)MathF.Round(v), 1, MaxSide); break;
            case CfgKey.RackColumns: b = Math.Clamp((int)MathF.Round(v), 1, MaxSide); break;
            case CfgKey.RackLength: len = Math.Clamp(Mm(v), MinLengthMm, MaxLengthMm); break;
            case CfgKey.RackDiameter: dia = Math.Clamp(Mm(v), MinDiameterMm, MaxDiameterMm); break;
        }
        return Make(layout, a, b, len, dia) ?? s;
    }

    /// <summary>The configure panel's warning while its rows (layers) and columns (segments) ask for more than a rack may have, or null.</summary>
    public static string? TooMany(int a, int b) => a * b > MaxStations ? $"At most {MaxStations} stations: {a} x {b} is {a * b}" : null;

    /// <summary>Is anything on one of its hooks (or welded to it and held)?</summary>
    public static bool Loaded(Construct c, int part)
    {
        var d = c.DefOf(part);
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            var mine = (e & 1) == 0 ? c.Links[e >> 1].SA : c.Links[e >> 1].SB;
            if (mine < d.Sockets.Length && d.Sockets[mine].Name.StartsWith("lug", StringComparison.Ordinal) || c.DefOf(c.OtherEnd(e)).Explosive != null)
                return true;
        }
        Span<ushort> held = stackalloc ushort[ConstructOrdnance.MaxHeld];
        return ConstructOrdnance.Held(c, part, held) > 0;
    }

    /// <summary>
    /// Server, from the configure tool: the rack becomes the shape its settings ask for, in place, keeping its welds and wires (the Replacer's plan,
    /// so its mounting socket stays where it is and it grows from there). Null, or why not; the caller puts its settings back on a refusal.
    /// </summary>
    public static string? Reshape(ConstructWorld world, Construct c, int part, bool creative)
    {
        var d = c.DefOf(part);
        ref var p = ref c.Parts[part];
        if (Asked(p, d) is not { } want)
            return AskedRefusal(p, d) ?? "those settings are out of range";
        if (want == d.Rack)
            return null;
        if (Loaded(c, part))
            return "take everything off its hooks first";
        if (!creative && Cost(want) > Cost(d.Rack) + 1e-3f)
            return "in survival the configure tool only makes a rack smaller: build a bigger one";
        if (Def(want) is not { } e)
            return "no room for another rack shape: restart the game";
        if (ConstructReplace.Plan(world, c, part, e, out var swap, lugsFree: true) is { } why)
            return why;
        ConstructReplace.Apply(world, c, part, e, swap);
        return null;
    }
}
