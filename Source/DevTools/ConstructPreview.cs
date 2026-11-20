using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// <c>--preview-construct NAME|FILE [--out OUT.html]</c> (and <c>--preview-gallery DIR</c>, every ship in one page): a blueprint (by name or file) or one construct part (by name), built
/// by the game's own rules (<see cref="ConstructBuild.BuildOnGround"/>), turned into the triangles the renderer draws (every
/// shown box, its shape mesh, paint, finish, glass and glow) and written as a small web page with a three.js viewer that
/// can be turned round, cut away by height and picked part by part. For agents designing ships and parts: the page is
/// published as an Artifact for the user (the ship-design skill). It is a design aid, not the renderer: no textures, no lamps'
/// light, no legs or tracks, joints at rest.
/// The page is <c>tools/preview/viewer.html</c> with the model's JSON put in place of its <c>__MODEL__</c> line; with no
/// template found (a packaged build) only the JSON is written, next to where the page would be.
/// </summary>
public static partial class ConstructPreview
{
    public static int Cli(string[] args)
    {
        if (Flag(args, "--preview-shot") is { } shotOf)
            return Shot(shotOf, args);
        if (Flag(args, "--anim-gallery") is { } animDir)
            return AnimGallery(animDir);
        if (Flag(args, "--preview-gallery") is { } galleryDir)
            return Gallery(galleryDir, Flag(args, "--only"), Flag(args, "--group") ?? "Ship");
        var what = Flag(args, "--preview-construct") ?? "";
        var outFile = Flag(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "preview", Safe(Path.GetFileNameWithoutExtension(what)) + ".html");
        if (Export(what, out var bp, out var parts, out _) is not { } json)
            return 1;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        var template = Template();
        if (template == null)
        {
            var jsonFile = Path.ChangeExtension(outFile, ".json");
            File.WriteAllText(jsonFile, json);
            Console.WriteLine($"no tools/preview/viewer.html above {AppContext.BaseDirectory}: wrote the model only, {jsonFile}");
            return 0;
        }
        File.WriteAllText(outFile, Page(template, json, bp.Name)); // (the page is named after what it shows: the artifact gallery takes its <title>)
        Console.WriteLine($"preview {bp.Name}: {parts} parts, {_tris} triangles, {_skipped} hidden boxes skipped, {new FileInfo(outFile).Length / 1024} KB -> {Path.GetFullPath(outFile)}");
        return 0;
    }

    private static string? Flag(string[] args, string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    private static string Page(string template, string json, string title) =>
        template.Replace("__MODEL__", json, StringComparison.Ordinal)
            .Replace("<title>Construct Preview</title>", "<title>" + System.Net.WebUtility.HtmlEncode(title) + "</title>", StringComparison.Ordinal);

    /// <summary>A blueprint file, blueprint or part by name, built and turned into the model's JSON; null (and the reason printed) when it can't be.</summary>
    private static string? Export(string what, out ConstructBuild.Blueprint bp, out int parts, out List<(string Name, int Count)> uses)
    {
        bp = null!;
        parts = 0;
        uses = [];
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
                return null;
            }
        }
        catch (Exception ex) when (ex is ConstructPartException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Console.WriteLine($"REFUSED {what}: {ex.Message}");
            return null;
        }
        var c = ConstructBuild.BuildOnGround(new ConstructWorld(), bp, Vector3.Zero, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"REFUSED {bp.Name}: it doesn't build: {why}");
            return null;
        }
        parts = c.PartCount;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < c.PartCount; i++)
        {
            var n = c.DefOf(i).Name;
            if (!counts.TryAdd(n, 1))
                counts[n]++;
            else
                uses.Add((n, 0));
        }
        uses = uses.Select(u => (u.Name, counts[u.Name])).ToList();
        return Model(c, bp);
    }

    /// <summary>
    /// <c>--preview-gallery DIR [--only "A,B"]</c>: every ship (a blueprint whose first part is in the "Ship" group), every part those
    /// ships use and every other "Ship" part, or only the names given, as one gallery: DIR/index.html (the viewer with a ship picker
    /// and a deck-or-part picker), DIR/models/index.json and one DIR/models/NAME.json each. Models whose JSON didn't change are left
    /// untouched and the changed ones are printed, so an agent republishes the gallery Artifact with only those (files it leaves out are kept).
    /// <c>--group Station</c> makes the Station gallery the same way: every blueprint whose first part is in that group, and its parts.
    /// </summary>
    private static int Gallery(string dir, string? only, string group = "Ship")
    {
        var template = Template();
        if (template == null)
        {
            Console.WriteLine($"--preview-gallery: no tools/preview/viewer.html above {AppContext.BaseDirectory}");
            return 1;
        }
        var models = Path.Combine(dir, "models");
        Directory.CreateDirectory(models);
        var picked = only?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var shipNames = only != null
            ? picked.Where(n => ConstructBuild.FindBlueprint(n) != null || File.Exists(n)).ToList()
            : ConstructBuild.Blueprints.Where(b => b.Steps.Count > 0 && ConstructParts.Find(b.Steps[0].Part)?.Group == group).OrderBy(b => b.Name, StringComparer.Ordinal).Select(b => b.Name).ToList();
        var looseNames = only != null
            ? picked.Where(n => !shipNames.Contains(n)).ToList()
            : ConstructParts.All.Where(d => d != null && d.Group == group).OrderBy(d => d.Section, StringComparer.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal).Select(d => d.Name).ToList();
        var changed = new List<string>();
        var failed = 0;
        var ships = new List<object>();
        var parts = new Dictionary<string, object>(StringComparer.Ordinal); // (by part name: each part once, however many ships use it)
        var written = new HashSet<string>(StringComparer.Ordinal);

        string? Write(string name, out ConstructBuild.Blueprint bp, out int count, out List<(string Name, int Count)> uses)
        {
            if (Export(name, out bp, out count, out uses) is not { } json)
            {
                failed++;
                return null;
            }
            var file = Slug(bp.Name) + ".json";
            var path = Path.Combine(models, file);
            written.Add(file);
            if (!File.Exists(path) || File.ReadAllText(path) != json)
            {
                File.WriteAllText(path, json);
                changed.Add("models/" + file);
            }
            return json;
        }

        void Part(string name)
        {
            if (parts.ContainsKey(name) || Write(name, out var bp, out _, out _) is not { } json)
                return;
            parts[name] = new { id = Slug(bp.Name), name = bp.Name, file = "models/" + Slug(bp.Name) + ".json", section = ConstructParts.Find(bp.Name)?.Section ?? "", tris = _tris, kb = json.Length / 1024 };
        }

        foreach (var name in shipNames)
        {
            if (Write(name, out var bp, out var count, out var uses) is not { } json)
                continue;
            ships.Add(new { id = Slug(bp.Name), name = bp.Name, file = "models/" + Slug(bp.Name) + ".json", parts = count, tris = _tris, kb = json.Length / 1024, uses = uses.Select(u => new object[] { Slug(u.Name), u.Count }).ToArray() });
            foreach (var (part, _) in uses)
                Part(part);
        }
        var usedByShips = new HashSet<string>(parts.Keys, StringComparer.Ordinal);
        foreach (var name in looseNames)
            Part(name);
        var loose = parts.Keys.Where(k => !usedByShips.Contains(k)).Select(Slug).ToArray();

        // (a gallery other than the ships names its kind, which the page uses for "Whole station" and the like)
        var list = group == "Ship" ? JsonSerializer.Serialize(new { ships, parts = parts.Values, loose })
            : JsonSerializer.Serialize(new { ships, parts = parts.Values, loose, noun = group.ToLowerInvariant() });
        var listPath = Path.Combine(models, "index.json");
        if (!File.Exists(listPath) || File.ReadAllText(listPath) != list)
        {
            File.WriteAllText(listPath, list);
            changed.Add("models/index.json");
        }
        var page = Page(template, "null", group + " Gallery");
        var pagePath = Path.Combine(dir, "index.html");
        if (!File.Exists(pagePath) || File.ReadAllText(pagePath) != page)
        {
            File.WriteAllText(pagePath, page);
            changed.Add("index.html");
        }
        var stale = Directory.GetFiles(models, "*.json").Select(Path.GetFileName).Where(f => f != "index.json" && !written.Contains(f!)).ToList();
        foreach (var f in stale)
            File.Delete(Path.Combine(models, f!));
        Console.WriteLine($"gallery {Path.GetFullPath(dir)}: {ships.Count} ships, {parts.Count} parts ({loose.Length} in no ship), {failed} refused; changed: {(changed.Count == 0 ? "nothing" : string.Join(" ", changed))}" +
                          (stale.Count > 0 ? $"; removed (publish them as null): {string.Join(" ", stale.Select(f => "models/" + f))}" : ""));
        return failed;
    }

    /// <summary>A model's file and page name: lower-case ASCII letters, digits and hyphens (an Artifact's #name).</summary>
    private static string Slug(string s) => string.Join('-', new string(s.ToLowerInvariant().Select(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int _tris, _skipped;

    private static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    private static string? Template()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "tools", "preview", "viewer.html")))
                return File.ReadAllText(Path.Combine(up.FullName, "tools", "preview", "viewer.html"));
        return null;
    }

    /// <summary>
    /// The decks: heights where solid slabs add up to a floor (at least 6 m2 and a tenth of the biggest level), at least 2 m apart
    /// (the bigger of two close levels wins) and with a ceiling: slabs 1.9 to 4.5 m above covering at least 30 % of it (a roof has none). Each is named after
    /// the part with the most floor there. One level or none: no decks (a part, a small vehicle).
    /// </summary>
    private static List<(string Name, float Y)> Decks(Construct c, List<(float Y, float Area, int Part)> floors)
    {
        var levels = floors.GroupBy(f => MathF.Round(f.Y * 10) / 10)
            .Select(g => (Y: g.Key, Area: g.Sum(f => f.Area), Part: g.GroupBy(f => c.DefOf(f.Part).Name).OrderByDescending(n => n.Sum(f => f.Area)).First().Key))
            .ToList();
        if (levels.Count == 0)
            return [];
        var most = levels.Max(l => l.Area);
        var keep = new List<(float Y, float Area, string Part)>();
        foreach (var l in levels.Where(l => l.Area >= MathF.Max(6f, most * 0.1f) && levels.Where(o => o.Y >= l.Y + 1.9f && o.Y <= l.Y + 4.5f).Sum(o => o.Area) >= l.Area * 0.3f).OrderBy(l => l.Y))
        {
            if (keep.Count > 0 && l.Y - keep[^1].Y < 2f)
            {
                if (l.Area > keep[^1].Area)
                    keep[^1] = l;
                continue;
            }
            keep.Add(l);
        }
        if (keep.Count < 2)
            return [];
        return keep.Select((l, i) => ($"{i + 1}: {l.Part}", l.Y)).ToList();
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
        var floors = new List<(float Y, float Area, int Part)>();
        _tris = _skipped = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var p = c.Parts[i];
            var d = c.DefOf(i);
            c.RestPose(p.Seg, out var q, out var t);
            var first = triLook.Count;
            for (var bi = 0; bi < d.SolidBoxes; bi++) // (floors: the tops of solid boxes, hidden ones too, since a drawn facet may stand for them)
            {
                var slab = d.Boxes[bi];
                var sm = slab.Model(p.Rot, Vector3.Zero) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, slab.Center), q));
                var blo = new Vector3(float.MaxValue);
                var bhi = new Vector3(float.MinValue);
                for (var k = 0; k < 8; k++)
                {
                    var v = Vector3.Transform(new Vector3((k & 1) - 0.5f, ((k >> 1) & 1) - 0.5f, ((k >> 2) & 1) - 0.5f), sm);
                    blo = Vector3.Min(blo, v);
                    bhi = Vector3.Max(bhi, v);
                }
                if (bhi.Y - blo.Y < 0.6f) // (a slab, not a wall or a crate)
                    floors.Add((bhi.Y, (bhi.X - blo.X) * (bhi.Z - blo.Z), i));
            }
            _partFrames?.Add(Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t));
            for (var boxIndex = 0; boxIndex < d.Boxes.Length; boxIndex++)
            {
                var b = d.Boxes[boxIndex];
                var track = _trackOf?.Invoke(i, boxIndex) ?? (byte)0;
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
                        // (the box's corners aren't turned by the part's Rot24, only its extents are (Model's ApplyHalf), so the face made
                        // from n is on n's side in the construct's frame and faces n: turning n by the part's rotation too pointed every x and
                        // z face of a turned part inward or sideways, which drew dark wherever a piece swung one of them up)
                        var wn = Vector3.Normalize(Vector3.Transform(n, q));
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
                    // (wound to face its normals: the page draws both sides and turns a back face's normal round, and three quarters of a
                    // shape mesh's triangles came wound the other way, so a cylinder drew dark on its lit side)
                    if (Vector3.Dot(Vector3.Cross(a1 - a0, a2 - a0), n0 + n1 + n2) < 0)
                    {
                        (a1, a2) = (a2, a1);
                        (n1, n2) = (n2, n1);
                    }
                    pos.Add(a0);
                    pos.Add(a1);
                    pos.Add(a2);
                    nrm.Add(n0);
                    nrm.Add(n1);
                    nrm.Add(n2);
                    triLook.Add(look);
                    _triTrack?.Add(track);
                }
            }
            if (ConstructStoreVolume.Walled(p, d))
            {
                // A walled Storage Volume's floor and walls (made from its box, not its file: the game draws them in ConstructRenderCargo).
                const string wallHex = "#8c8a80";
                var wkey = (wallHex, 0f, false, "");
                if (!lookIndex.TryGetValue(wkey, out var wlook))
                {
                    wlook = (ushort)looks.Count;
                    lookIndex[wkey] = wlook;
                    looks.Add((wallHex, 0f, false, "", 0f, 0.9f));
                }
                for (var k = 0; k < ConstructStoreVolume.WallBoxes; k++)
                {
                    ConstructStoreVolume.WallBox(c, i, k, out var wc, out var wh);
                    var wm = Matrix4x4.CreateScale(wh * 2) * Matrix4x4.CreateTranslation(wc) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
                    foreach (var (n, a, bb) in CubeFaces)
                    {
                        var wn = Vector3.Normalize(Vector3.Transform(n, q));
                        Vector3 Corner(float u, float v) => Vector3.Transform(n * 0.5f + a * u + bb * v, wm);
                        foreach (var (v0, v1, v2) in new[] { (Corner(-0.5f, -0.5f), Corner(0.5f, -0.5f), Corner(0.5f, 0.5f)), (Corner(-0.5f, -0.5f), Corner(0.5f, 0.5f), Corner(-0.5f, 0.5f)) })
                        {
                            pos.Add(v0);
                            pos.Add(v1);
                            pos.Add(v2);
                            nrm.Add(wn);
                            nrm.Add(wn);
                            nrm.Add(wn);
                            triLook.Add(wlook);
                            _triTrack?.Add(0);
                        }
                    }
                    if (k == 0)
                        floors.Add((Vector3.Transform(wc + new Vector3(0, wh.Y, 0), q).Y + t.Y, wh.X * wh.Z * 4, i));
                }
            }
            if (parts.Length > 0)
                parts.Append(',');
            parts.Append(JsonSerializer.Serialize(new object[] { d.Name, first, triLook.Count - first }));
        }
        _tris = triLook.Count;
        _last = (pos, nrm, triLook, looks.Select(l => (l.Hex, l.Glass, l.Glow)).ToList()); // (for --preview-shot)
        if (_shotLinks)
            LinkTris(c);

        var decksSb = new StringBuilder();
        var decks = Decks(c, floors);
        for (var i = 0; i < decks.Count; i++)
            decksSb.Append(i > 0 ? "," : "").Append(JsonSerializer.Serialize(new object[] { decks[i].Name, MathF.Round(decks[i].Y, 3) }));
        // The links (cables, pipes, chutes, shafts...) as the build laid them: [type, x, y, z, x, y, z, ...] from the output through the nodes to the input, rest frame.
        var linksSb = new StringBuilder();
        for (var r = 0; r < c.RouteCount; r++)
        {
            linksSb.Append(r > 0 ? "," : "").Append("[\"").Append(ConstructRoutes.TypeKey(c.Routes[r].Type)).Append('"');
            for (var i = -1; i <= c.Routes[r].Count; i++)
            {
                var q = ConstructRoutes.RestPoint(c, r, i);
                linksSb.Append(',').Append(F3(q.X)).Append(',').Append(F3(q.Y)).Append(',').Append(F3(q.Z));
            }
            linksSb.Append(']');
        }
        return Encode(bp.Name, bp.Description, bp.Steps.Count, c.Mass, pos, nrm, triLook, looks, parts.ToString(), decksSb.ToString(), linksSb.ToString(), Cost(c, triLook.Count) + _animWrite?.Invoke());

        static string F3(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What the model costs the game, as the style sheet shows it (",\"cost\":{...}"): triangles, drawn boxes (and how many are
    /// shaped, lit or glass), whether every part is drawn from the merged mesh (a glass pane, a lamp or anything that moves makes a
    /// part walked box by box every frame: ConstructRenderBatch.StaticBoxes), and the collision: solid boxes and convex hulls.
    /// </summary>
    private static string Cost(Construct c, int tris)
    {
        int drawn = 0, shaped = 0, glow = 0, glass = 0, walked = 0, solid = 0, hulls = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            foreach (var b in d.Boxes)
            {
                if (b.Hidden)
                    continue;
                drawn++;
                if (b.Shape != 0)
                    shaped++;
                if (b.Glow)
                    glow++;
                if (b.IsGlass)
                    glass++;
            }
            if (ConstructRender.StaticBoxes(d) < 0)
                walked++;
            var hs = ConstructHulls.Of(d);
            hulls += hs.Length;
            for (var s = 0; s < d.SolidBoxes; s++)
                if (!ConstructHulls.Covered(d, s))
                    solid++;
        }
        return $",\"cost\":{{\"tris\":{tris},\"drawn\":{drawn},\"shaped\":{shaped},\"glow\":{glow},\"glass\":{glass},\"parts\":{c.PartCount},\"walked\":{walked},\"solid\":{solid},\"hulls\":{hulls}}}";
    }

    /// <summary>The model's JSON from its triangles (see <see cref="Model"/>); <paramref name="extra"/> is put in before the closing brace (",\"anim\":...").</summary>
    private static string Encode(string name, string description, int steps, float mass, List<Vector3> pos, List<Vector3> nrm, List<ushort> triLook, List<(string Hex, float Glass, bool Glow, string Finish, float Metal, float Rough)> looks, string parts, string decksJson, string linksJson, string? extra)
    {
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
        sb.Append("{\"format\":\"call-of-slop-preview/1\",\"name\":").Append(JsonSerializer.Serialize(name));
        sb.Append(",\"description\":").Append(JsonSerializer.Serialize(description));
        sb.Append(",\"steps\":").Append(steps).Append(",\"mass\":").Append(MathF.Round(mass));
        sb.Append(",\"min\":[").Append(F(lo.X)).Append(',').Append(F(lo.Y)).Append(',').Append(F(lo.Z)).Append(']');
        sb.Append(",\"max\":[").Append(F(hi.X)).Append(',').Append(F(hi.Y)).Append(',').Append(F(hi.Z)).Append(']');
        sb.Append(",\"scale\":").Append(F(scale));
        sb.Append(",\"decks\":[").Append(decksJson).Append(']');
        sb.Append(",\"looks\":[");
        for (var i = 0; i < looks.Count; i++)
        {
            var l = looks[i];
            sb.Append(i > 0 ? "," : "").Append(JsonSerializer.Serialize(new object[] { l.Hex, l.Glass, l.Glow ? 1 : 0, l.Finish, l.Metal, l.Rough }));
        }
        sb.Append("],\"parts\":[").Append(parts).Append(']');
        sb.Append(",\"links\":[").Append(linksJson).Append(']');
        sb.Append(",\"pos\":\"").Append(Convert.ToBase64String(pb)).Append('"');
        sb.Append(",\"nrm\":\"").Append(Convert.ToBase64String(nb)).Append('"');
        sb.Append(",\"look\":\"").Append(Convert.ToBase64String(lb)).Append('"');
        if (extra != null)
            sb.Append(extra);
        sb.Append('}');
        return sb.ToString();

        static string F(float f) => f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
