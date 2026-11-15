using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// <c>--preview-construct NAME|FILE [--out OUT.html]</c>: a blueprint (by name or file) or one construct part (by name), built
/// by the game's own rules (<see cref="ConstructBuild.BuildOnGround"/>), turned into the triangles the renderer draws (every
/// shown box, its shape mesh, paint, finish, glass and glow) and written as a small web page with a three.js viewer that
/// can be turned round, cut away by height and picked part by part. For agents designing ships and parts: the page is
/// published as an Artifact for the user (the ship-design skill). It is a design aid, not the renderer: no textures, no lamps'
/// light, no legs or tracks, joints at rest.
/// The page is <c>tools/preview/viewer.html</c> with the model's JSON put in place of its <c>__MODEL__</c> line; with no
/// template found (a packaged build) only the JSON is written, next to where the page would be.
/// </summary>
public static class ConstructPreview
{
    public static int Cli(string[] args)
    {
        var what = args.SkipWhile(a => !a.Equals("--preview-construct", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? "";
        var outFile = args.SkipWhile(a => !a.Equals("--out", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()
                      ?? Path.Combine(AppContext.BaseDirectory, "preview", Safe(Path.GetFileNameWithoutExtension(what)) + ".html");
        ConstructBuild.Blueprint bp;
        try
        {
            if (File.Exists(what))
                bp = ConstructBuild.ParseBlueprint(File.ReadAllText(what), Path.GetFileName(what));
            else if (ConstructBuild.FindBlueprint(what) is { } found)
                bp = found;
            else if (ConstructParts.Find(what) is { } part)
                bp = ConstructBuild.ParseBlueprint(JsonSerializer.Serialize(new { format = "call-of-slop-construct-blueprint/1", name = part.Name, description = part.Description, steps = new[] { new { part = part.Name } } }), part.Name);
            else
            {
                Console.WriteLine($"--preview-construct: no blueprint file, blueprint or part called \"{what}\"");
                return 1;
            }
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Console.WriteLine($"REFUSED {what}: {ex.Message}");
            return 1;
        }

        var world = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(world, bp, Vector3.Zero, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"REFUSED {bp.Name}: it doesn't build: {why}");
            return 1;
        }

        var json = Model(c, bp);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        var template = Template();
        if (template == null)
        {
            var jsonFile = Path.ChangeExtension(outFile, ".json");
            File.WriteAllText(jsonFile, json);
            Console.WriteLine($"no tools/preview/viewer.html above {AppContext.BaseDirectory}: wrote the model only, {jsonFile}");
            return 0;
        }
        File.WriteAllText(outFile, template.Replace("__MODEL__", json, StringComparison.Ordinal) // (the page is named after what it shows: the artifact gallery takes its <title>)
            .Replace("<title>Construct Preview</title>", "<title>" + System.Net.WebUtility.HtmlEncode(bp.Name) + "</title>", StringComparison.Ordinal));
        Console.WriteLine($"preview {bp.Name}: {c.PartCount} parts, {_tris} triangles, {_skipped} hidden boxes skipped, {new FileInfo(outFile).Length / 1024} KB -> {Path.GetFullPath(outFile)}");
        return 0;
    }

    private static int _tris, _skipped;

    private static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    private static string? Template()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "tools", "preview", "viewer.html")))
                return File.ReadAllText(Path.Combine(up.FullName, "tools", "preview", "viewer.html"));
        return null;
    }

    // The renderer's unit cube (shape 0): 12 triangles, corners at +-0.5, outward faces wound anticlockwise.
    private static readonly (Vector3 N, Vector3 A, Vector3 B)[] CubeFaces =
    [
        (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
    ];

    /// <summary>
    /// The model as JSON: looks (colour, glass, glow, finish), parts (name, first triangle, count) and three base64 arrays:
    /// positions as int16 (metres times <c>scale</c>), normals as int8 and one look index per triangle as uint16.
    /// </summary>
    private static string Model(Construct c, ConstructBuild.Blueprint bp)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var triLook = new List<ushort>();
        var looks = new List<(string Hex, float Glass, bool Glow, string Finish, float Metal, float Rough)>();
        var lookIndex = new Dictionary<(string, float, bool, string), ushort>();
        var parts = new StringBuilder();
        _tris = _skipped = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var p = c.Parts[i];
            var d = c.DefOf(i);
            c.RestPose(p.Seg, out var q, out var t);
            var first = triLook.Count;
            foreach (var b in d.Boxes)
            {
                if (b.Hidden)
                {
                    _skipped++;
                    continue;
                }
                var centre = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, b.Center), q);
                var m = b.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(centre);
                var row = Surfaces.Row(ConstructFinish.BoxRow(in b, p.Paint, p.Finish));
                var colour = ConstructPaint.BoxColour(p.Paint, in b);
                var hex = $"#{(int)(Math.Clamp(colour.X, 0, 1) * 255):x2}{(int)(Math.Clamp(colour.Y, 0, 1) * 255):x2}{(int)(Math.Clamp(colour.Z, 0, 1) * 255):x2}";
                var key = (hex, b.Glass, b.Glow, row.Name);
                if (!lookIndex.TryGetValue(key, out var look))
                {
                    look = (ushort)looks.Count;
                    lookIndex[key] = look;
                    looks.Add((hex, b.Glass, b.Glow, row.Name, row.Metal, row.Rough));
                }
                if (b.Shape == 0)
                {
                    foreach (var (n, a, bb) in CubeFaces)
                    {
                        var wn = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, n), q));
                        Vector3 Corner(float u, float v) => Vector3.Transform(n * 0.5f + a * u + bb * v, m);
                        var c00 = Corner(-0.5f, -0.5f);
                        var c10 = Corner(0.5f, -0.5f);
                        var c11 = Corner(0.5f, 0.5f);
                        var c01 = Corner(-0.5f, 0.5f);
                        Tri(c00, c10, c11, wn, wn, wn);
                        Tri(c00, c11, c01, wn, wn, wn);
                    }
                }
                else
                {
                    var mesh = PartShapes.Mesh(b.Shape);
                    for (var k = 0; k + 3 * PartShapes.FloatsPerVertex <= mesh.Length; k += 3 * PartShapes.FloatsPerVertex)
                    {
                        Vector3 V(int o) => Vector3.Transform(new Vector3(mesh[k + o], mesh[k + o + 1], mesh[k + o + 2]), m);
                        Vector3 N(int o)
                        {
                            var n = Vector3.TransformNormal(new Vector3(mesh[k + o + 3], mesh[k + o + 4], mesh[k + o + 5]), m);
                            return n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
                        }
                        const int S = PartShapes.FloatsPerVertex;
                        Tri(V(0), V(S), V(2 * S), N(0), N(S), N(2 * S));
                    }
                }

                void Tri(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 n0, Vector3 n1, Vector3 n2)
                {
                    pos.Add(a0);
                    pos.Add(a1);
                    pos.Add(a2);
                    nrm.Add(n0);
                    nrm.Add(n1);
                    nrm.Add(n2);
                    triLook.Add(look);
                }
            }
            if (parts.Length > 0)
                parts.Append(',');
            parts.Append(JsonSerializer.Serialize(new object[] { d.Name, first, triLook.Count - first }));
        }
        _tris = triLook.Count;

        // Quantise: positions to int16 over the largest extent, normals to int8.
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in pos)
        {
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
        }
        if (pos.Count == 0)
            lo = hi = Vector3.Zero;
        var reach = MathF.Max(MathF.Max(MathF.Max(MathF.Abs(lo.X), MathF.Abs(hi.X)), MathF.Max(MathF.Abs(lo.Y), MathF.Abs(hi.Y))), MathF.Max(MathF.Abs(lo.Z), MathF.Abs(hi.Z)));
        var scale = reach > 0 ? MathF.Min(1000f, 32000f / reach) : 1000f; // (1 mm steps up to 32 m from the origin; coarser beyond)
        var pb = new byte[pos.Count * 6];
        var nb = new byte[nrm.Count * 3];
        for (var i = 0; i < pos.Count; i++)
        {
            for (var a = 0; a < 3; a++)
            {
                var s = (short)Math.Clamp(MathF.Round(pos[i][a] * scale), short.MinValue, short.MaxValue);
                pb[i * 6 + a * 2] = (byte)s;
                pb[i * 6 + a * 2 + 1] = (byte)(s >> 8);
                nb[i * 3 + a] = (byte)(sbyte)Math.Clamp(MathF.Round(nrm[i][a] * 127f), -127, 127);
            }
        }
        var lb = new byte[triLook.Count * 2];
        for (var i = 0; i < triLook.Count; i++)
        {
            lb[i * 2] = (byte)triLook[i];
            lb[i * 2 + 1] = (byte)(triLook[i] >> 8);
        }

        var sb = new StringBuilder();
        sb.Append("{\"format\":\"call-of-slop-preview/1\",\"name\":").Append(JsonSerializer.Serialize(bp.Name));
        sb.Append(",\"description\":").Append(JsonSerializer.Serialize(bp.Description));
        sb.Append(",\"steps\":").Append(bp.Steps.Count).Append(",\"mass\":").Append(MathF.Round(c.Mass));
        sb.Append(",\"min\":[").Append(F(lo.X)).Append(',').Append(F(lo.Y)).Append(',').Append(F(lo.Z)).Append(']');
        sb.Append(",\"max\":[").Append(F(hi.X)).Append(',').Append(F(hi.Y)).Append(',').Append(F(hi.Z)).Append(']');
        sb.Append(",\"scale\":").Append(F(scale));
        sb.Append(",\"looks\":[");
        for (var i = 0; i < looks.Count; i++)
        {
            var l = looks[i];
            sb.Append(i > 0 ? "," : "").Append(JsonSerializer.Serialize(new object[] { l.Hex, l.Glass, l.Glow ? 1 : 0, l.Finish, l.Metal, l.Rough }));
        }
        sb.Append("],\"parts\":[").Append(parts).Append(']');
        sb.Append(",\"pos\":\"").Append(Convert.ToBase64String(pb)).Append('"');
        sb.Append(",\"nrm\":\"").Append(Convert.ToBase64String(nb)).Append('"');
        sb.Append(",\"look\":\"").Append(Convert.ToBase64String(lb)).Append("\"}");
        return sb.ToString();

        static string F(float f) => f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
