using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>Settings > Video: Low, Medium, High for materials, particles and post effects.</summary>
public enum VideoLevel : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>A shared material (a row of Materials/Surfaces.json), as the C# side reads it.</summary>
public sealed class SurfaceRow
{
    public string Name = "";
    public float Rough = 0.8f, Metal, Tint = 1f, Wear, Rust, Scale = 1f, Amount;
    public Vector3 Colour = new(0.5f), WearColour = new(0.6f);
    public int Pattern;
    public string PaintedName = "";
    public byte Painted; // the row a painted box uses (itself when none is named)
}

/// <summary>
/// The shared material library (docs/render/MATERIALS.md). A material is one row of this table: a part box, a prop part or an
/// extrusion holds its number (a byte), and the shaders read the row from one table on the GPU (<see cref="SurfaceGpu"/>), so
/// a change to a material is one row, and nothing about a material is stored in a part, a construct, a save, a blueprint or a
/// packet: a saved part reads its material through its def. The numbers are this build's own: they are only ever worked out at
/// load, from <see cref="SurfaceRules"/>. Rows 0 to 6 are the texture layers of <see cref="Material"/>, drawn the classic way.
/// </summary>
public static class Surfaces
{
    public const int MaxRows = 256;

    /// <summary>Rows before this are the old texture layers: the shaders skip the material path for them.</summary>
    public const int FirstShaded = (int)Material.Count;

    private static readonly object Gate = new();
    private static SurfaceFile? _file;

    /// <summary>The file's rows and rules (read once, on first use; a bad file is refused with a reason and the built-in rows stand in).</summary>
    public static SurfaceFile File
    {
        get
        {
            if (_file != null)
                return _file;
            lock (Gate)
                return _file ??= SurfaceFile.LoadDefault();
        }
    }

    /// <summary>The number of a row by name, or 0 when no row has it.</summary>
    public static byte Id(string name) => File.ByName.TryGetValue(name, out var id) ? id : (byte)0;

    public static SurfaceRow Row(int id) => File.Rows[id < File.Rows.Length ? id : 0];

    /// <summary>What goes to the shader for one box: the row, and in the fraction how worn the box is (0 whole, 1 wrecked).</summary>
    public static float Code(int row, float wear) => row + Math.Clamp(wear, 0f, 1f) * 0.99f;

    /// <summary>The row a box of a part with this paint is drawn with: paint is a tint over its material, and a material can name what it turns into under paint.</summary>
    public static byte OnPaint(byte surface, uint paint, bool paintable) => paint != 0 && paintable ? File.Rows[surface].Painted : surface;

    /// <summary>The GLSL every shader that draws materials takes after <see cref="Lighting.Glsl"/>: the table, the patterns and <c>SurfaceLit</c>.</summary>
    public const string Glsl = """

        // ---- Shared materials (docs/render/MATERIALS.md): one table on the GPU, one row a material ----
        #if GL46
        layout(std430, binding = 6) readonly buffer SurfaceTable { vec4 uSurfRows[]; };
        vec4 SurfRow(int row, int k) { return uSurfRows[row * 4 + k]; }
        #else
        uniform sampler2D uSurfTex;
        vec4 SurfRow(int row, int k) { return texelFetch(uSurfTex, ivec2(k, row), 0); }
        #endif

        float SurfHash(vec2 p)
        {
            vec3 q = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
            q += dot(q, q.yzx + 33.33);
            return fract((q.x + q.y) * q.z);
        }

        float SurfNoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = p - i;
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(SurfHash(i), SurfHash(i + vec2(1.0, 0.0)), f.x), mix(SurfHash(i + vec2(0.0, 1.0)), SurfHash(i + vec2(1.0, 1.0)), f.x), f.y);
        }

        // A pattern's height at uv (metres; side: the length of the face along uv.x, so streaks run the long way): -1..1 about 0.
        float SurfPattern(int kind, vec2 uv, float scale)
        {
            vec2 p = uv * scale;
            if (kind == 1) // brushed: fine streaks along u
                return SurfNoise(vec2(p.x * 0.04, p.y)) * 2.0 - 1.0;
            if (kind == 2) // speckle: grit
                return (SurfNoise(p) + SurfNoise(p * 2.7 + 11.0) * 0.5) * 1.33 - 1.0;
            if (kind == 3) // grain: planks with long wavy lines
            {
                float plank = floor(p.y * 0.42);
                float seam = smoothstep(0.92, 1.0, abs(fract(p.y * 0.42) * 2.0 - 1.0));
                float g = sin((p.y + SurfNoise(vec2(p.x * 0.25, p.y + plank * 7.0)) * 2.2) * 9.0);
                return g * 0.6 + (SurfHash(vec2(plank, 3.0)) - 0.5) - seam * 1.6;
            }
            if (kind == 4) // weave: over and under
            {
                vec2 c = fract(p) - 0.5;
                float over = mod(floor(p.x) + floor(p.y), 2.0);
                return (over > 0.5 ? c.x * c.x : c.y * c.y) * 8.0 - 1.0;
            }
            if (kind == 5) // tread: ribs
                return sin(p.y * 6.2832) * 0.8 + (fract(p.x * 0.5) < 0.12 ? -0.8 : 0.0);
            if (kind == 6) // panel: a seam a few times a metre
            {
                vec2 c = abs(fract(p) - 0.5);
                return -smoothstep(0.482, 0.495, max(c.x, c.y)) * 2.0;
            }
            return 0.0;
        }

        // A surface at world point p, normal n: lp is the point in the box (metres from its middle), ln its face's normal there, size
        // the box (0: no box edges to wear, a prop), and code the row with the box's wear in its fraction (Surfaces.Code).
        // uSurfQ.x is the setting: 1 Medium, 2 High (Low never comes here: it takes the classic light). All of it is maths on what the
        // pixel already has: no textures. Detail fades with distance, so a far hull pays for the light and nothing else.
        vec3 SurfaceLit(vec3 base, vec3 n, vec3 p, vec3 lp, vec3 ln, vec3 size, float code)
        {
            int row = int(code);
            float wear = fract(code) / 0.99;
            vec4 ra = SurfRow(row, 0); // rough, metal, wear, rust
            vec4 rb = SurfRow(row, 1); // colour, tint
            vec4 rc = SurfRow(row, 2); // wear colour
            vec4 rd = SurfRow(row, 3); // pattern, scale, amount
            vec3 albedo = mix(rb.rgb, base, rb.a);
            float rough = ra.x;
            float metal = ra.y;
            vec3 toEye = uCam - p;
            float dist = length(toEye);
            vec3 v = toEye / max(dist, 1e-3);
            float near = 1.0 - smoothstep(18.0, 60.0, dist);
            vec3 nn = n;
            if (near > 0.0)
            {
                vec3 an = abs(ln);
                bool xFace = an.x > an.y && an.x > an.z;
                bool yFace = an.y > an.z;
                vec2 uv = xFace ? lp.yz : yFace ? lp.xz : lp.xy;
                vec2 hs = (xFace ? size.yz : yFace ? size.xz : size.xy) * 0.5;
                if (hs.y > hs.x) // (streaks and grain run the long way of the face)
                {
                    uv = uv.yx;
                    hs = hs.yx;
                }
                vec2 off = vec2(float(row) * 3.1, dot(size, vec3(7.3, 3.7, 11.1)));
                if (rd.x > 0.5 && rd.z > 0.0)
                {
                    float h = SurfPattern(int(rd.x + 0.5), uv + off, rd.y) * rd.z * near;
                    albedo *= 1.0 + 0.5 * h;
                    rough = clamp(rough - h * 0.25, 0.04, 1.0);
                    if (uSurfQ.x > 1.5)
                    {
                        // High: the pattern tilts the normal a little (its slope along u and v), so light catches it.
                        float e = 0.012;
                        float hu = SurfPattern(int(rd.x + 0.5), uv + off + vec2(e, 0.0), rd.y) * rd.z * near - h;
                        float hv = SurfPattern(int(rd.x + 0.5), uv + off + vec2(0.0, e), rd.y) * rd.z * near - h;
                        vec3 t = normalize(cross(n, abs(n.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
                        nn = normalize(n - (t * hu + cross(n, t) * hv) * 1.2);
                    }
                }
                float nw = SurfNoise(uv * 5.0 + off);
                if (ra.z > 0.0 && size.x > 0.0)
                {
                    // Edge wear: how far the nearest edge of this face is, read from the box itself: no mesh or texture knows it.
                    vec2 ed = hs - abs(uv);
                    float edge = min(ed.x, ed.y);
                    float rub = (1.0 - smoothstep(0.0, 0.006 + 0.03 * nw * (0.5 + wear), edge)) * ra.z * near;
                    albedo = mix(albedo, rc.rgb, rub);
                    metal = mix(metal, min(metal + 0.5, 0.9), rub);
                }
                if (ra.w > 0.0 && wear > 0.02)
                {
                    // Hurt parts rust from their patches inward, and a wreck is scorched and flaking.
                    float n2 = uSurfQ.x > 1.5 ? nw * 0.6 + SurfNoise(uv * 13.0 + off) * 0.4 : nw;
                    float rust = smoothstep(1.02 - wear * ra.w * 0.95, 1.14 - wear * ra.w * 0.95, n2 + wear * 0.34) * near;
                    vec3 rustCol = mix(vec3(0.33, 0.17, 0.09), vec3(0.09, 0.07, 0.06), smoothstep(0.7, 1.0, wear)) * (0.7 + 0.6 * nw);
                    albedo = mix(albedo, rustCol, rust);
                    metal *= 1.0 - rust;
                    rough = mix(rough, 0.95, rust);
                }
            }
            vec3 diffuse = albedo * (1.0 - metal * 0.92);
            vec3 col = ApplyLightAt(diffuse, nn, 0.0, p);
            float amb = gLitAmb;
            // A rough sky in the surface: Schlick's fresnel, its gradient along the bounce, dimmer the rougher and the more shut in.
            vec3 f0 = mix(vec3(0.04), albedo, metal);
            float nv = max(dot(nn, v), 0.0);
            float pw = 1.0 - nv;
            pw = pw * pw * pw * pw * pw;
            vec3 fres = f0 + (max(vec3(1.0 - rough), f0) - f0) * pw;
            vec3 r = reflect(-v, nn);
            vec3 env = r.y >= 0.0 ? SkyGradient(r) : mix(SkyGradient(vec3(r.x, 0.0, r.z)), uAmbGround, min(-r.y * 3.0, 1.0));
            col += env * fres * amb * (1.0 - rough * 0.65);
            // The sun's highlight: a cheap normalised Blinn-Phong lobe. The baked grid's own main light gets the same.
            float gloss = clamp(1.0 - rough, 0.02, 0.98);
            float power = 2.0 + gloss * gloss * gloss * gloss * 900.0;
            float norm = (power + 8.0) * 0.04;
            vec3 h = normalize(uSunDir + v);
            float nl = max(dot(nn, uSunDir), 0.0);
            col += uSunColor * gSunVis * fres * pow(max(dot(nn, h), 0.0), power) * norm * nl;
            if (uLitMode.x > 0.5 && dot(gLitDir, gLitDir) > 0.01)
            {
                vec3 ld = normalize(gLitDir);
                vec3 lh = normalize(ld + v);
                col += gLitCol * fres * pow(max(dot(nn, lh), 0.0), power) * norm * max(dot(nn, ld), 0.0);
            }
            return col;
        }

        """;
}

/// <summary>Materials/Surfaces.json read: its rows, the rules that give parts their row, and the bulk and prop tables.</summary>
public sealed class SurfaceFile
{
    public SurfaceRow[] Rows = [];
    public readonly Dictionary<string, byte> ByName = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<SurfaceRule> Rules = new();
    public readonly Dictionary<string, byte> Bulk = new(StringComparer.OrdinalIgnoreCase);
    public readonly byte[] Props = new byte[(int)Material.Count];

    public static readonly string[] PatternNames = ["none", "brushed", "speckle", "grain", "weave", "tread", "panel"];

    /// <summary>Why the file was refused, if it was.</summary>
    public string Refused = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Materials", "Surfaces.json");

    public static SurfaceFile LoadDefault()
    {
        var path = FilePath;
        try
        {
            if (System.IO.File.Exists(path))
                return Parse(System.IO.File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            Console.WriteLine($"[surfaces] {path} refused: {ex.Message}");
            return Plain(ex.Message);
        }
        return Plain("no Materials/Surfaces.json");
    }

    /// <summary>The legacy rows only: everything draws as it always did.</summary>
    private static SurfaceFile Plain(string why)
    {
        var f = new SurfaceFile { Refused = why };
        f.Rows = new SurfaceRow[(int)Material.Count];
        for (var i = 0; i < f.Rows.Length; i++)
            f.Rows[i] = new SurfaceRow { Name = ((Material)i).ToString(), Painted = (byte)i };
        return f;
    }

    public static SurfaceFile Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var f = new SurfaceFile();
        var rows = new List<SurfaceRow>();
        foreach (var e in root.GetProperty("surfaces").EnumerateArray())
        {
            var r = new SurfaceRow { Name = e.GetProperty("name").GetString() ?? "" };
            foreach (var p in e.EnumerateObject())
                switch (p.Name)
                {
                    case "name": break;
                    case "rough": r.Rough = Num(p.Value, 0.02f, 1f, r.Name); break;
                    case "metal": r.Metal = Num(p.Value, 0, 1, r.Name); break;
                    case "tint": r.Tint = Num(p.Value, 0, 1, r.Name); break;
                    case "wear": r.Wear = Num(p.Value, 0, 1, r.Name); break;
                    case "rust": r.Rust = Num(p.Value, 0, 1, r.Name); break;
                    case "scale": r.Scale = Num(p.Value, 0.1f, 400, r.Name); break;
                    case "amount": r.Amount = Num(p.Value, 0, 1, r.Name); break;
                    case "colour": r.Colour = Hex(p.Value.GetString(), r.Name); break;
                    case "wearColour": r.WearColour = Hex(p.Value.GetString(), r.Name); break;
                    case "pattern":
                        r.Pattern = Array.IndexOf(PatternNames, p.Value.GetString());
                        if (r.Pattern < 0)
                            throw new InvalidDataException($"{r.Name}: no pattern \"{p.Value.GetString()}\"");
                        break;
                    case "painted": r.PaintedName = p.Value.GetString() ?? ""; break;
                    default: throw new InvalidDataException($"{r.Name}: unknown key \"{p.Name}\"");
                }
            if (!f.ByName.TryAdd(r.Name, (byte)rows.Count))
                throw new InvalidDataException($"two surfaces are called \"{r.Name}\"");
            rows.Add(r);
        }
        if (rows.Count < (int)Material.Count || rows.Count > Surfaces.MaxRows)
            throw new InvalidDataException($"{rows.Count} rows: the first {(int)Material.Count} are the texture layers, and at most {Surfaces.MaxRows}");
        f.Rows = rows.ToArray();
        for (var i = 0; i < f.Rows.Length; i++)
            f.Rows[i].Painted = f.Rows[i].PaintedName.Length > 0 ? f.Need(f.Rows[i].PaintedName) : (byte)i;
        foreach (var e in root.GetProperty("rules").EnumerateArray())
            f.Rules.Add(SurfaceRule.Parse(e, f));
        if (root.TryGetProperty("bulk", out var bulk))
            foreach (var p in bulk.EnumerateObject())
                f.Bulk[p.Name] = f.Need(p.Value.GetString() ?? "");
        for (var i = 0; i < f.Props.Length; i++)
            f.Props[i] = (byte)i;
        if (root.TryGetProperty("props", out var props))
            foreach (var p in props.EnumerateObject())
                f.Props[(int)Enum.Parse<Material>(p.Name)] = f.Need(p.Value.GetString() ?? "");
        return f;
    }

    public byte Need(string name) => ByName.TryGetValue(name, out var id) ? id : throw new InvalidDataException($"no surface \"{name}\"");

    private static float Num(JsonElement e, float min, float max, string who)
    {
        var v = e.GetSingle();
        if (!(v >= min && v <= max))
            throw new InvalidDataException($"{who}: {v} is outside {min} to {max}");
        return v;
    }

    private static Vector3 Hex(string? text, string who)
    {
        if (text is not { Length: 7 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new InvalidDataException($"{who}: \"{text}\" is not #rrggbb");
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }

    /// <summary>The table the GPU holds: four vec4s a row (<see cref="Surfaces.Glsl"/>'s SurfRow), <paramref name="rows"/> rows.</summary>
    public float[] GpuTable(int rows)
    {
        var t = new float[rows * 16];
        for (var i = 0; i < rows; i++)
        {
            var r = Rows[i < Rows.Length ? i : 0];
            var o = i * 16;
            t[o] = r.Rough; t[o + 1] = r.Metal; t[o + 2] = r.Wear; t[o + 3] = r.Rust;
            t[o + 4] = r.Colour.X; t[o + 5] = r.Colour.Y; t[o + 6] = r.Colour.Z; t[o + 7] = r.Tint;
            t[o + 8] = r.WearColour.X; t[o + 9] = r.WearColour.Y; t[o + 10] = r.WearColour.Z;
            t[o + 12] = r.Pattern; t[o + 13] = r.Scale; t[o + 14] = r.Amount;
        }
        return t;
    }
}

/// <summary>One rule of Materials/Surfaces.json: what a part box must be to take <see cref="Surface"/>; a test left out passes.</summary>
public sealed class SurfaceRule
{
    public byte Surface;
    public string[]? Roles, Materials;
    public string? Folder, Name;
    public bool? Glass, Glow, Tyre;
    public Vector2? Hue;
    public float? Dark;

    public static SurfaceRule Parse(JsonElement e, SurfaceFile f)
    {
        var r = new SurfaceRule();
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "surface": r.Surface = f.Need(p.Value.GetString() ?? ""); break;
                case "role": r.Roles = List(p.Value); break;
                case "material": r.Materials = List(p.Value); break;
                case "folder": r.Folder = p.Value.GetString(); break;
                case "name": r.Name = p.Value.GetString(); break;
                case "glass": r.Glass = p.Value.GetBoolean(); break;
                case "glow": r.Glow = p.Value.GetBoolean(); break;
                case "tyre": r.Tyre = p.Value.GetBoolean(); break;
                case "dark": r.Dark = p.Value.GetSingle(); break;
                case "hue": r.Hue = new Vector2(p.Value[0].GetSingle(), p.Value[1].GetSingle()); break;
                default: throw new InvalidDataException($"a surface rule has an unknown key \"{p.Name}\"");
            }
        return r;
    }

    private static string[] List(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [e.GetString() ?? ""];

    /// <summary>Hue in degrees, saturation, brightness.</summary>
    public static (float H, float S, float V) Hsv(Vector3 c)
    {
        var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        var min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var d = max - min;
        if (d < 1e-4f)
            return (0, 0, max);
        var h = max == c.X ? (c.Y - c.Z) / d : max == c.Y ? 2 + (c.Z - c.X) / d : 4 + (c.X - c.Y) / d;
        return ((h * 60 + 360) % 360, d / MathF.Max(max, 1e-4f), max);
    }

    public bool Fits(ConstructPartDef d, in CPartBox b)
    {
        if (Glass is { } g && g != b.IsGlass)
            return false;
        if (Glow is { } w && w != b.Glow)
            return false;
        if (Tyre is { } t && t != (d.Role == PartRole.Wheel && !b.Paintable))
            return false;
        if (Roles != null && Array.FindIndex(Roles, x => x.Equals(d.Role.ToString(), StringComparison.OrdinalIgnoreCase)) < 0)
            return false;
        if (Materials != null && Array.FindIndex(Materials, x => x.Equals(d.Material, StringComparison.OrdinalIgnoreCase)) < 0)
            return false;
        if (Folder != null && !d.Folder.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Name != null && !d.Name.Contains(Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Hue is { } hue || Dark != null)
        {
            var (h, s, v) = Hsv(b.Color);
            if (Hue is { } range && !(s > 0.15f && h >= range.X && h <= range.Y))
                return false;
            if (Dark is { } dark && v >= dark)
                return false;
        }
        return true;
    }
}

/// <summary>Gives a part's boxes their material: by the box's or the part's own <c>"finish"</c>, an extrusion's bulk material, else the first rule that fits (Materials/Surfaces.json).</summary>
public static class SurfaceRules
{
    public static void Apply(ConstructPartDef d)
    {
        var f = Surfaces.File;
        if (f.Rules.Count == 0)
            return;
        var boxes = d.Boxes;
        for (var i = 0; i < boxes.Length; i++)
        {
            ref readonly var b = ref boxes[i];
            if (b.Surface != 0)
                continue; // (the file named one)
            var row = d.SurfaceOverride != 0 && !b.IsGlass ? d.SurfaceOverride : Pick(f, d, in b);
            if (row != 0)
                boxes[i] = b with { Surface = row };
        }
    }

    private static byte Pick(SurfaceFile f, ConstructPartDef d, in CPartBox b)
    {
        foreach (var r in f.Rules)
            if (r.Fits(d, in b))
                return r.Surface;
        return 0;
    }

    /// <summary>The surface of an extrusion or wheel of a bulk material (by name, Recycling.json).</summary>
    public static byte OfBulk(string bulk) => Surfaces.File.Bulk.TryGetValue(bulk, out var id) ? id : (byte)0;
}

/// <summary>
/// What a part box looks like, interned: the colour, its tone under paint and its material are one row here and the box holds
/// the row's number, so 10,000 boxes in a few hundred colours keep one copy of each. Rows are added at load and when an extrusion
/// is first made; they are never sent, saved or hashed, and differ from one machine to the next. Reads take no lock: the
/// array is replaced whole when it grows.
/// </summary>
public static class BoxLooks
{
    public readonly record struct Row(Vector3 Color, float Tone, byte Surface);

    private static Row[] _rows = MakeRows();
    private static int _count = 1;
    private static readonly Dictionary<Row, ushort> Index = new();
    private static readonly object Gate = new();

    private static Row[] MakeRows() => new Row[1024]; // (row 0: default(CPartBox))

    public static int Count => _count;

    public static ref readonly Row Get(ushort id) => ref _rows[id];

    public static ushort Of(Vector3 color, float tone, byte surface)
    {
        var key = new Row(color, tone, surface);
        lock (Gate)
        {
            if (Index.TryGetValue(key, out var id))
                return id;
            if (_count == ushort.MaxValue)
                throw new InvalidOperationException("more than 65,535 part box looks");
            if (_count == _rows.Length)
            {
                var more = new Row[_rows.Length * 2];
                Array.Copy(_rows, more, _count);
                _rows = more;
            }
            _rows[_count] = key;
            id = (ushort)_count;
            Index[key] = id;
            _count++;
            return id;
        }
    }
}

/// <summary>
/// The material table on the GPU: 256 rows of four vec4s, in a storage buffer at binding 6 on the 4.6 path, in a small float
/// texture on unit 16 on the 4.1 path (<see cref="Surfaces.Glsl"/>'s SurfRow reads either). Uploaded once; a change to a
/// material rewrites this one table.
/// </summary>
public static unsafe class SurfaceGpu
{
    public const uint Binding = 6;
    public const int Unit = 16; // (0-15 are taken: VideoFeeds.Unit lists them; a shader may name any unit below the combined limit, 48 at least on GL 4.1)

    private static bool _ready;
    private static uint _ssbo, _tex;

    /// <summary>Bytes the table takes on the GPU.</summary>
    public const int Bytes = Surfaces.MaxRows * 64;

    /// <summary>Called for every scene program, once it is linked: the table exists, and the 4.1 sampler reads its unit.</summary>
    public static void Register(GlProgram program)
    {
        Upload();
        fixed (byte* n = "uSurfTex\0"u8)
        {
            var loc = Native.glGetUniformLocation(program, n);
            if (loc >= 0)
                Native.glUniform1i(loc, Unit);
        }
    }

    /// <summary>Makes the table, or rewrites it.</summary>
    public static void Upload()
    {
        if (_ready)
            return;
        _ready = true;
        var table = Surfaces.File.GpuTable(Surfaces.MaxRows);
        fixed (float* p = table)
        {
            if (RenderCaps.Gl46)
            {
                GlBuffer b;
                Native.glGenBuffers(1, &b);
                _ssbo = b.Id;
                Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, b);
                Native.glBufferData(GL_SHADER_STORAGE_BUFFER, Bytes, p, GL_STATIC_DRAW);
                Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, Binding, b);
                Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, default);
                GlDebug.Label(GL_BUFFER, _ssbo, "SurfaceGpu.table");
                return;
            }
            GlTexture t = default;
            Native.glGenTextures(1, &t);
            _tex = t.Id;
            Native.glActiveTexture(GL_TEXTURE0 + Unit);
            Native.glBindTexture(GL_TEXTURE_2D, _tex);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA32F, 4, Surfaces.MaxRows, 0, GL_RGBA, GL_FLOAT, p);
            Native.glActiveTexture(GL_TEXTURE0);
        }
    }
}
