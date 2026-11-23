using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Bake kits (Sean, 2026-11-23: "large deck parts should be made through the extrusion chain in the factory, put together, and then baked in
/// the oven, not made in an assembler"). A part listed in Constructs/BakeKits.json has a kit: one extrusion for each of its drawn boxes, the
/// piece's section and length the box's size, painted and finished as the box is, at the box's place and turn. A Building Station builds
/// the kit as a welded group (its blueprint <c>{part} kit</c>, made here at run time), and a Bake Oven bakes that group into the part
/// itself, its own name, sockets, mass and look (<see cref="BakedParts.Bake"/> asks <see cref="Target"/> before it makes a
/// <c>Baked (#hash)</c>), so a ship's blueprint, its saves and its fittings know the part as before. The kit's design is matched at any of
/// the 24 grid turns. Docs: production.md "Bake kits".
/// </summary>
public static class BakeKits
{
    public const string Format = "call-of-slop-bake-kits/1";
    public const string KitSuffix = " kit";

    /// <summary>
    /// One piece of a kit: the extrusion (or, with <paramref name="Name"/>, a run-time part by its key, such as a plain Hull Section a Panel
    /// Former forms), where its origin sits in the part's frame and its turn, its paint and its finish's name.
    /// </summary>
    public readonly record struct Piece(ExtrusionKey Key, Vector3 Pos, byte Rot, uint Paint, string Finish, string? Name = null)
    {
        public string PartName => Name ?? Extrusion.Name(Key);

        public ConstructPartDef? Def => Name != null ? ConstructParts.Find(Name) : Extrusion.Def(Key);
    }

    public const string ListFormat = "call-of-slop-bake-kit-list/1";

    /// <summary>A kit's design at one turn: the part it bakes into, the part's turn and its origin from the design's origin.</summary>
    public readonly record struct Hit(ConstructPartDef Part, byte Rot, Vector3 Offset);

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "BakeKits.json");

    /// <summary>Kits a generator lists piece by piece (Constructs/BakeKits/*.json, format <see cref="ListFormat"/>): the Zero Day's keel, formed skins.</summary>
    public static string ListFolder => Path.Combine(AppFolders.Data, "Constructs", "BakeKits");

    private static string[]? _names;
    private static string _material = "steel";
    private static readonly Dictionary<string, Piece[]?> Kits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ConstructBuild.Blueprint?> Prints = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, List<Hit>>? _targets;
    private static readonly Dictionary<string, Piece[]> Listed = new(StringComparer.OrdinalIgnoreCase); // (by part: the kit's pieces as listed)
    private static readonly object Gate = new();

    /// <summary>The parts made from kits (Constructs/BakeKits.json "parts").</summary>
    public static string[] Names
    {
        get
        {
            if (_names != null)
                return _names;
            lock (Gate)
                return _names ??= [.. Load(FilePath).Concat(LoadLists(ListFolder)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    private static string[] Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            if (root.TryGetProperty("material", out var m) && m.GetString() is { Length: > 0 } mat)
                _material = mat;
            var list = new List<string>();
            if (root.TryGetProperty("parts", out var ps))
                foreach (var p in ps.EnumerateArray())
                    if (p.GetString() is { Length: > 0 } n)
                        list.Add(n);
            return [.. list];
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[bake kits] REFUSED {path}: {e.Message}");
            return [];
        }
    }

    /// <summary>Every kit list in the folder (none: no folder): each kit's part and pieces, a bad file or kit refused with a reason.</summary>
    private static List<string> LoadLists(string folder)
    {
        var names = new List<string>();
        if (!Directory.Exists(folder))
            return names;
        foreach (var path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = doc.RootElement;
                if (!root.TryGetProperty("format", out var f) || f.GetString() != ListFormat)
                    throw new FormatException($"\"format\" must be \"{ListFormat}\"");
                foreach (var k in root.GetProperty("kits").EnumerateArray())
                {
                    var part = k.GetProperty("part").GetString() ?? throw new FormatException("a kit with no \"part\"");
                    var pieces = new List<Piece>();
                    foreach (var q in k.GetProperty("pieces").EnumerateArray())
                    {
                        var at = q.GetProperty("at");
                        var pos = new Vector3(MathF.Round(at[0].GetSingle() * 100), MathF.Round(at[1].GetSingle() * 100), MathF.Round(at[2].GetSingle() * 100)) / 100f; // (a part's place rounds to the centimetre)
                        var rot = q.TryGetProperty("rot", out var r) ? r.GetByte() : (byte)0;
                        var paint = q.TryGetProperty("paint", out var pa) && ConstructPaint.TryParse(pa.GetString(), out var pv) ? pv : 0u;
                        var finish = q.TryGetProperty("finish", out var fi) ? fi.GetString() ?? "" : "";
                        pieces.Add(new Piece(default, pos, rot, paint, finish, q.GetProperty("part").GetString() ?? throw new FormatException($"{part}: a piece with no \"part\"")));
                    }
                    if (rot24Bad(pieces) is { } bad)
                        throw new FormatException($"{part}: {bad}");
                    Listed[part] = [.. pieces];
                    names.Add(part);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or IndexOutOfRangeException)
            {
                Console.WriteLine($"[bake kits] REFUSED {path}: {e.Message}");
            }
        return names;

        static string? rot24Bad(List<Piece> ps) => ps.Count is < 2 or > BakedParts.MaxSubparts ? $"{ps.Count} pieces (2 to {BakedParts.MaxSubparts})"
            : ps.FindIndex(p => p.Rot >= Rot24.Count) is var i and >= 0 ? $"piece {i}: turn {ps[i].Rot} (0 to {Rot24.Count - 1})" : null;
    }

    /// <summary>Is this part made from a kit (baked, never assembled)?</summary>
    public static bool Has(ConstructPartDef d)
    {
        if (d.IsNamedRuntime || d.IsExtrusion)
            return false;
        foreach (var n in Names) // (no lambda: it is asked in looks that must not allocate)
            if (n.Equals(d.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>A part's kit (null: it has none, or one of its boxes can't be made: the log says which).</summary>
    public static Piece[]? KitOf(ConstructPartDef d)
    {
        if (!Has(d))
            return null;
        lock (Gate)
        {
            if (Kits.TryGetValue(d.Name, out var have))
                return have;
            var kit = Listed.TryGetValue(d.Name, out var listed) ? Resolve(d, listed) : Make(d);
            Kits[d.Name] = kit;
            return kit;
        }
    }

    /// <summary>One extrusion a box: its longest side the length (5 cm steps), the thinner of the others its height (a Sheet under 10 cm).</summary>
    private static Piece[]? Make(ConstructPartDef d)
    {
        var mat = RecyclingTable.Current.IdOf(_material);
        var pieces = new List<Piece>();
        for (var i = 0; i < d.SolidBoxes; i++)
        {
            var b = d.Boxes[i];
            if (b.Hidden)
                continue; // (detail boxes, seams and markings, come with the bake: the kit is the part's solid plates and members)
            var size = b.Half * 2 * 100; // cm
            int la = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            int a1 = (la + 1) % 3, a2 = (la + 2) % 3;
            var (wa, ha) = Axis(size, a1) >= Axis(size, a2) ? (a1, a2) : (a2, a1);
            var len = Math.Max(Extrusion.StepCm, (int)MathF.Round(Axis(size, la) / Extrusion.StepCm) * Extrusion.StepCm);
            var w = (int)MathF.Round(Axis(size, wa));
            var h = (int)MathF.Round(Axis(size, ha));
            if (h < Extrusion.MinSectionCm)
            {
                // A Sheet (1 to 10 cm thick) when it is wider than thick and at least 10 cm wide; else a bar of the least section.
                h = Math.Max(Extrusion.MinSheetCm, h);
                if (w < Extrusion.MinSectionCm)
                    (w, h) = (Extrusion.MinSectionCm, Extrusion.MinSectionCm);
                else if (w <= h)
                    w = h + 1;
            }
            w = Math.Clamp(w, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
            h = Math.Min(h, Extrusion.MaxSectionCm);
            var key = new ExtrusionKey(ulong.MaxValue, (ushort)w, (ushort)h, (ushort)Math.Min(len, Extrusion.MaxLengthCm), mat, false);
            if (Extrusion.KeyRefusal(key) is { } why)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}: box {i} ({size.X:0} x {size.Y:0} x {size.Z:0} cm) makes no piece: {why}");
                return null;
            }
            if (TurnFor(wa, ha, la) is not { } rot)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}: box {i}: no grid turn lays a piece along it");
                return null;
            }
            // The piece's own frame: its width along x, height along y, length along z from 0 (ConstructExtrusion.BuildSection).
            var pos = b.Center - Rot24.Apply(rot, new Vector3(0, 0, key.L / 200f));
            pos = new Vector3(MathF.Round(pos.X * 100), MathF.Round(pos.Y * 100), MathF.Round(pos.Z * 100)) / 100f;
            var paint = ConstructPaint.FromVector(b.Color);
            pieces.Add(new Piece(key, pos, rot, paint, ConstructFinish.Offered(b.Surface) ? ConstructFinish.Name(b.Surface) : ""));
        }
        // The sections cored to weigh about what the part does (a solid kit of a 25 cm deck is three times its mass, past a hall crane's
        // 20 t): one profile for every piece thick enough, the one whose fill is nearest the part's mass over the solid kit's.
        float solid = 0;
        foreach (var p in pieces)
            solid += Extrusion.Def(p.Key)?.Mass ?? 0;
        if (solid > d.Mass * 1.25f)
        {
            var want = d.Mass / solid;
            var mask = ulong.MaxValue;
            foreach (var pr in Extrusion.Presets)
                if (pr.Id is "solid" or "wall" or "hollow" && MathF.Abs(Extrusion.Fill(pr.Mask) - want) < MathF.Abs(Extrusion.Fill(mask) - want))
                    mask = pr.Mask;
            for (var i = 0; i < pieces.Count; i++)
                if (!Extrusion.IsSheet(pieces[i].Key) && pieces[i].Key with { Mask = mask } is var cored && Extrusion.KeyRefusal(cored) == null && Extrusion.Def(cored) != null)
                    pieces[i] = pieces[i] with { Key = cored };
        }
        if (pieces.Count < 2 || pieces.Count > BakedParts.MaxSubparts)
        {
            Console.WriteLine($"[bake kits] REFUSED {d.Name}: {pieces.Count} pieces (2 to {BakedParts.MaxSubparts})");
            return null;
        }
        Order(pieces);
        return [.. pieces];
    }

    /// <summary>A listed kit: every piece's part known (null: one isn't, the log says which), the biggest first, as <see cref="Make"/> lays them.</summary>
    private static Piece[]? Resolve(ConstructPartDef d, Piece[] listed)
    {
        var pieces = new List<Piece>(listed);
        foreach (var p in pieces)
            if (p.Def == null)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}: no such part as its piece {p.PartName}");
                return null;
            }
        Order(pieces);
        return [.. pieces];
    }

    /// <summary>The biggest piece first: the kit's blueprint stands on it (step 0, unturned when it can be).</summary>
    private static void Order(List<Piece> pieces)
    {
        pieces.Sort((p, q) => Volume(q).CompareTo(Volume(p)));
        var first = pieces.FindIndex(p => p.Rot == 0);
        if (first > 0)
            (pieces[0], pieces[first]) = (pieces[first], pieces[0]);
    }

    private static float Volume(in Piece p) => p.Name == null ? Volume(p.Key) : p.Def is { } d ? (d.BoundsMax.X - d.BoundsMin.X) * (d.BoundsMax.Y - d.BoundsMin.Y) * (d.BoundsMax.Z - d.BoundsMin.Z) * 1e6f : 0;

    private static float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
    private static float Volume(in ExtrusionKey k) => (float)k.W * k.H * k.L;

    private static Vector3 Unit(int a) => a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;

    /// <summary>The turn that lays a piece's x along axis <paramref name="wa"/>, its y along <paramref name="ha"/> and its z along <paramref name="la"/> (signs free).</summary>
    private static byte? TurnFor(int wa, int ha, int la)
    {
        for (byte r = 0; r < Rot24.Count; r++)
        {
            var x = Vector3.Abs(Rot24.Apply(r, Vector3.UnitX));
            var y = Vector3.Abs(Rot24.Apply(r, Vector3.UnitY));
            var z = Vector3.Abs(Rot24.Apply(r, Vector3.UnitZ));
            if ((x - Unit(wa)).LengthSquared() < 1e-4f && (y - Unit(ha)).LengthSquared() < 1e-4f && (z - Unit(la)).LengthSquared() < 1e-4f
                && Rot24.Apply(r, Vector3.UnitY).Y >= -0.5f && Rot24.Apply(r, Vector3.UnitZ) is var zd && zd.X + zd.Y + zd.Z > 0)
                return r; // (z along the axis's plus side, y not down: one turn a box)
        }
        return null;
    }

    /// <summary>
    /// A kit's blueprint, <c>{part} kit</c>: the first piece the root, every other one welded on it freeform at its place and turn, painted
    /// and finished (null: no kit). Made once a part.
    /// </summary>
    public static ConstructBuild.Blueprint? BlueprintOf(ConstructPartDef d)
    {
        if (KitOf(d) is not { } kit)
            return null;
        lock (Gate)
        {
            if (Prints.TryGetValue(d.Name, out var have))
                return have;
            var root = kit[0];
            var sb = new StringBuilder();
            sb.Append("{ \"format\": \"call-of-slop-construct-blueprint/1\", \"name\": ").Append(JsonSerializer.Serialize(d.Name + KitSuffix)).Append(", \"steps\": [\n");
            for (var i = 0; i < kit.Length; i++)
            {
                var p = kit[i];
                sb.Append("  { \"part\": ").Append(JsonSerializer.Serialize(p.PartName));
                if (ConstructPaint.IsSet(p.Paint))
                    sb.Append(", \"paint\": \"").Append(ConstructPaint.ToHex(p.Paint)).Append('"');
                if (p.Finish.Length > 0)
                    sb.Append(", \"finish\": ").Append(JsonSerializer.Serialize(p.Finish));
                if (i > 0)
                {
                    // (in the root's frame: the root is unturned, else every place turns back by it)
                    var inv = Rot24.Inverse(root.Rot);
                    var at = Rot24.Apply(inv, p.Pos - root.Pos);
                    var q = Rot24.Quat(Rot24.Compose(inv, p.Rot));
                    sb.Append(string.Create(CultureInfo.InvariantCulture,
                        $", \"weld\": {{\"on\": 0, \"pos\": [{at.X:0.###}, {at.Y:0.###}, {at.Z:0.###}], \"rot\": [{q.X:0.####}, {q.Y:0.####}, {q.Z:0.####}, {q.W:0.####}]}}"));
                }
                sb.Append(i + 1 < kit.Length ? " },\n" : " }\n");
            }
            sb.Append("] }\n");
            ConstructBuild.Blueprint? bp = null;
            try
            {
                bp = ConstructBuild.ParseBlueprint(sb.ToString(), d.Name + KitSuffix);
            }
            catch (ConstructPartException e)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}{KitSuffix}: {e.Message}");
            }
            Prints[d.Name] = bp;
            return bp;
        }
    }

    /// <summary>A kit's blueprint by its name, <c>{part} kit</c> (null: not a kit's name).</summary>
    public static ConstructBuild.Blueprint? FindPrint(string name) =>
        name.EndsWith(KitSuffix, StringComparison.OrdinalIgnoreCase) && Names.Length > 0 && ConstructParts.Find(name[..^KitSuffix.Length]) is { } d ? BlueprintOf(d) : null;

    /// <summary>
    /// The part whose kit a design's hash is, at the turn it was baked (null: none). Every kit at every turn, worked out once. Two parts can
    /// share a kit (the keel's bands where the hull runs straight): <paramref name="want"/>, the oven's order, picks between them.
    /// </summary>
    public static Hit? Target(string hash, ushort want = 0)
    {
        if (Names.Length == 0)
            return null;
        lock (Gate)
        {
            if (_targets == null)
            {
                _targets = new Dictionary<string, List<Hit>>(StringComparer.Ordinal);
                foreach (var n in Names)
                {
                    if (ConstructParts.Find(n) is not { } d || KitOf(d) is not { } kit)
                    {
                        Console.WriteLine($"[bake kits] {n}: no such part, or no kit: never baked");
                        continue;
                    }
                    for (byte r = 0; r < Rot24.Count; r++)
                    {
                        var subs = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[kit.Length];
                        for (var i = 0; i < kit.Length; i++)
                            subs[i] = (kit[i].Def!, Rot24.Apply(r, kit[i].Pos), Rot24.Compose(r, kit[i].Rot), kit[i].Paint, kit[i].Finish);
                        var text = BakedParts.DesignText(subs, out var origin);
                        // The part's origin is the kit's frame's origin: turned by r, then measured from the design's origin.
                        var hash2 = BakedParts.HashOf(text);
                        if (!_targets.TryGetValue(hash2, out var hits))
                            _targets[hash2] = hits = [];
                        if (!hits.Exists(h => h.Part == d))
                            hits.Add(new Hit(d, r, -origin)); // (a kit alike at two turns: the first turn)
                    }
                }
            }
            if (!_targets.TryGetValue(hash, out var list))
                return null;
            foreach (var h in list)
                if (h.Part.Id == want)
                    return h;
            return list[0];
        }
    }
}

/// <summary>
/// <c>--bake-kit [NAME;NAME...|all]</c>, headless: each kit's blueprint built through the player's rules and baked (it must come out as its
/// part, where the kit stood), then the kit laid at four other turns and baked (the part at that turn). Docs: production.md "Bake kits".
/// </summary>
public static class BakeKitCheck
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bake-kit", StringComparison.OrdinalIgnoreCase));
        var list = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "all";
        var names = list.Equals("all", StringComparison.OrdinalIgnoreCase) ? BakeKits.Names : list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fails = 0;
        void Fail(string why)
        {
            fails++;
            Console.WriteLine("FAIL " + why);
        }
        foreach (var n in names)
        {
            if (ConstructParts.Find(n) is not { } d || BakeKits.KitOf(d) is not { } kit || BakeKits.BlueprintOf(d) is not { } bp)
            {
                Fail($"{n}: no part, or no kit (the log says why)");
                continue;
            }
            Console.WriteLine($"{d.Name}: {kit.Length} pieces");
            foreach (var p in kit)
                Console.WriteLine(FormattableString.Invariant($"  {p.PartName} at {p.Pos.X:0.##},{p.Pos.Y:0.##},{p.Pos.Z:0.##} turn {p.Rot} {ConstructPaint.ToHex(p.Paint)} {p.Finish}"));
            // Through the blueprint, as a station builds it.
            var world = new ConstructWorld();
            if (ConstructBuild.Build(world, bp, new Vector3(0, 20, 0), Quaternion.Identity, out var why) is not { } c)
            {
                Fail($"{d.Name}: the kit's blueprint didn't build: {why}");
                continue;
            }
            Expect(world, c, d, Rot24.Inverse(kit[0].Rot), Rot24.Apply(Rot24.Inverse(kit[0].Rot), -kit[0].Pos), "built from its blueprint"); // (the body's frame is its first piece's)
            // Laid at other turns (an oven bakes a kit whichever way it lies).
            foreach (byte t in new byte[] { 1, 5, 10, 17, 23 })
            {
                var w2 = new ConstructWorld();
                var c2 = w2.Create(new Vector3(0, 20, 0), Quaternion.Identity)!;
                foreach (var p in kit)
                {
                    var i = w2.AddPart(c2, p.Def!, Rot24.Apply(t, p.Pos), Rot24.Compose(t, p.Rot));
                    c2.Parts[i].Paint = p.Paint;
                    c2.Parts[i].Finish = ConstructFinish.Of(p.Finish);
                }
                c2.Rebuild();
                Expect(w2, c2, d, t, Vector3.Zero, $"laid at turn {t}");
            }
        }
        Console.WriteLine(fails == 0 ? "bake kit check: passed" : $"bake kit check: {fails} failure(s)");
        return fails;

        void Expect(ConstructWorld world, Construct c, ConstructPartDef d, byte turn, Vector3 origin, string how)
        {
            // A box kit's solid bounds are its part's; a listed kit of formed skins isn't the part box for box (its plates are drawn shapes,
            // its solids the voids behind them), so the part's origin is held to the kit frame's origin instead (both in the body's frame).
            var listed = c.DefOf(0).IsNamedRuntime;
            var (lo, hi) = Solid(c);
            if (BakedParts.Bake(world, c, (ushort)d.Id) is not { } b)
            {
                Fail($"{d.Name} {how}: bake refused: {BakedParts.LastRefusal}");
                return;
            }
            var i = Array.FindIndex(b.Parts, 0, b.PartCount, p => p.Def == d.Id);
            if (i < 0)
            {
                Fail($"{d.Name} {how}: baked into {b.DefOf(0).Name}, not the part");
                if (BakedParts.DesignOf(b.DefOf(0)) is { } got)
                    Console.Write(got.Text);
                return;
            }
            var (lo2, hi2) = Solid(b);
            var off = listed ? Vector3.Distance(b.Parts[i].Pos, origin) : MathF.Max(Vector3.Distance(lo, lo2), Vector3.Distance(hi, hi2));
            Console.WriteLine(FormattableString.Invariant($"  {how}: {d.Name} at turn {b.Parts[i].Rot}, its {(listed ? "origin" : "solid bounds")} {off * 100:0.#} cm from the kit's"));
            if (b.Parts[i].Rot != turn)
                Fail($"{d.Name} {how}: turn {b.Parts[i].Rot}, not {turn}");
            if (off > 0.06f)
                Fail($"{d.Name} {how}: the part sits {off:0.###} m from where its kit lay");
        }
    }

    /// <summary>A body's solid boxes' bounds in its own frame.</summary>
    private static (Vector3, Vector3) Solid(Construct c)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            c.RestPose(c.Parts[i].Seg, out var q, out var t);
            var pos = t + Vector3.Transform(c.Parts[i].Pos, q);
            var r = Rot24.Nearest(q * Rot24.Quat(c.Parts[i].Rot), out _);
            foreach (var bx in d.Boxes.AsSpan(0, d.SolidBoxes))
            {
                if (bx.Hidden)
                    continue;
                var centre = pos + Rot24.Apply(r, bx.Center);
                var half = Rot24.ApplyHalf(r, bx.Half);
                (lo, hi) = (Vector3.Min(lo, centre - half), Vector3.Max(hi, centre + half));
            }
        }
        return (lo, hi);
    }
}
