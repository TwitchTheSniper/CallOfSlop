using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts (the spawnable ship designs plan, steps 1 and 2, Karl 2026-11-26): a Ship Designer design (Source/ShipDesigner) as one run-time
/// construct part, <c>Design (#hash)</c>, never converted into the catalogue's parts. A spawned design is a body holding that one part.
/// <para>
/// <b>The design.</b> Its JSON, read and written again by <see cref="ShipDesign"/> without indents (<see cref="Canon"/>), so one design always gives
/// one hash. The designs live in a table every peer shares by hash (<see cref="Known"/>), and each world keeps those its bodies use
/// (<see cref="ConstructWorld.ShipDesigns"/>): saved with the world in a section of its own ahead of the part names (WorldSaveWriter.ShipDesignTag),
/// sent to a joiner before any body and to everyone as each comes (<see cref="CBodyKind.ShipDesign"/>), as the Bake Oven's designs are
/// (BakedParts.cs). A design read from the wire or a save is checked against its hash and must read as a design.
/// </para>
/// <para>
/// <b>The part.</b> Drawn as the designer's own meshes (skin, cladding, joins, pieces), merged into one vertex-coloured mesh (<see cref="Look"/>,
/// drawn by Renderer.Design.cs). It collides as convex hulls (ConstructHulls, the narrow phase's hull tests, the collider's tree): one a band between
/// two rings of each hull body's copy (split across its profile when a band has more corners than <see cref="ConvexHull.MaxCorners"/>), one for
/// each join and each piece that stands out of the bands (from its mesh's extreme points). For now it is solid: interiors are a later step. Its
/// solid boxes are hidden and stand for no collider (every one is covered by the hulls): they carry the mass, each its own share
/// (<see cref="ConstructPartDef.BoxMass"/>): the skin's area at <see cref="SkinKgPerM2"/>, the rooms' floors at <see cref="DeckKgPerM2"/> and the
/// pieces' surfaces at <see cref="PieceKgPerM2"/>.
/// </para>
/// </summary>
public static class DesignParts
{
    public const string Prefix = "Design (#";
    public const int MaxText = 1 << 20; // bytes of one design's text
    public const int MaxWorld = 256; // designs one world keeps
    public const int MaxHulls = 1024; // convex colliders one design part makes (each band, join and piece; past it the rest are left out, logged)

    // ---- Mass: plain figures, all here (Karl's plan: skin area times a plate density, plus decks and pieces) ----
    public const float SkinKgPerM2 = 80f; // a hull plate, about 1 cm of steel with its frames
    public const float DeckKgPerM2 = 60f; // a room's floor with its beams
    public const float PieceKgPerM2 = 150f; // a piece's drawn surface (drives, guns, legs, masts: machinery, denser than skin)
    public const float HpPerKg = 0.5f; // the part's health from its mass

    /// <summary>A design's look: one merged mesh in the part's frame, flat normals and a colour a vertex (drawn by Renderer.Design.cs).</summary>
    public sealed class Look
    {
        public float[] Pos = [], Normal = [];
        public uint[] Colour = [];
        public int Vertices => Pos.Length / 3;
        public Vector3 Min, Max;
        internal object? Gpu; // (the render thread's buffers: Renderer.Design.cs)
    }

    /// <summary>One design: its text, hash and (once its part is made) what it came to.</summary>
    public sealed class Entry
    {
        public string Hash = "", Text = "", Name = "";
    }

    private static readonly Dictionary<string, Entry> Known = new(StringComparer.Ordinal); // by hash
    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal); // by key
    private static readonly Dictionary<ConstructPartDef, (Entry E, Element El)> Info = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<string> Unknown = new(StringComparer.Ordinal);
    private static ConstructPartDef? _template;

    [ThreadStatic] public static string? LastRefusal;

    /// <summary>The last design made into its elements: how long it took and what it came to (a check reads these).</summary>
    public static double LastBuildMs, LastStepMs; // the last design made: in all, and its longest step (ElementSteps)
    public static string LastSummary = "";

    public static bool IsName(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool IsDesign(ConstructPartDef d) => d.IsNamedRuntime && IsName(d.Name);

    public static string KeyOf(string hash) => Prefix + hash + ")";

    public static string HashOf(string text) => BakedParts.HashOf(text);

    /// <summary>A design part's look (null: not one).</summary>
    public static Look? LookOf(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        lock (Interned)
            return Info.TryGetValue(d, out var i) ? i.El.Look : null;
    }

    /// <summary>A design part's design (null: not one).</summary>
    public static Entry? EntryOf(ConstructPartDef d)
    {
        if (!IsDesign(d))
            return null;
        lock (Interned)
            return Info.TryGetValue(d, out var i) ? i.E : null;
    }

    /// <summary>
    /// A blueprint's <c>"shipDesigns"</c> (ConstructClip.ToBlueprint): the design of every design part in it, so a player's blueprint of a
    /// design ship builds in a world that never saw that design. Null when it has none.
    /// </summary>
    public static string? BlueprintBlock(Construct c)
    {
        List<Entry>? used = null;
        for (var i = 0; i < c.PartCount; i++)
            if (EntryOf(c.DefOf(i)) is { } e && !(used ??= []).Contains(e))
                used.Add(e);
        return used == null ? null : "[" + string.Join(", ", used.Select(e => e.Text)) + "]";
    }

    /// <summary>A blueprint's <c>"shipDesigns"</c> read: each one into the table (before its steps name their parts).</summary>
    public static void ReadBlueprintBlock(System.Text.Json.JsonElement v, string file)
    {
        if (v.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new ConstructPartException($"{file}: \"shipDesigns\" is a list of ship designs");
        foreach (var one in v.EnumerateArray())
            if (Canon(one.GetRawText()) is not { } text || Register(text) is null)
                throw new ConstructPartException($"{file}: a ship design in \"shipDesigns\" isn't read: {LastRefusal}");
    }

    public static Entry? Find(string hash)
    {
        lock (Interned)
            return Known.GetValueOrDefault(hash);
    }

    /// <summary>A design's JSON as the table keeps it (read as a design and written again, without indents). Null: it isn't one (<see cref="LastRefusal"/>).</summary>
    public static string? Canon(string json)
    {
        try
        {
            var d = ShipDesign.FromJson(json);
            return JsonNode.Parse(d.ToJson())!.ToJsonString();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
        {
            LastRefusal = "not a ship design: " + ex.Message;
            return null;
        }
    }

    /// <summary>Keeps a design's text in the shared table (it must read as a design). Its hash, or null.</summary>
    public static string? Register(string text)
    {
        if (text.Length > MaxText)
        {
            LastRefusal = "the design is too long";
            return null;
        }
        var hash = HashOf(text);
        lock (Interned)
            if (Known.ContainsKey(hash))
                return hash;
        ShipDesign d;
        try
        {
            d = ShipDesign.FromJson(text);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException or KeyNotFoundException)
        {
            LastRefusal = "not a ship design: " + ex.Message;
            return null;
        }
        lock (Interned)
        {
            Known.TryAdd(hash, new Entry { Hash = hash, Text = text, Name = d.Name });
            if (Unknown.Remove(KeyOf(hash)))
                Console.WriteLine($"[design] design {hash} came: its part can be made now");
        }
        return hash;
    }

    // ---- The elements: what a design is made of, one part each ------------------------------------------------------------

    /// <summary>
    /// One element of a design, the part it becomes: a band of a hull body's copy (between two rings), a join (each side of a mirrored one), a
    /// piece (each copy), a room's floor. <see cref="Id"/> names it within its design (the part's key is the design's and this); its geometry is
    /// in the part's frame, which stands at <see cref="Origin"/> in the ship's. A bill of materials reads <see cref="Kind"/>, <see cref="Name"/>,
    /// <see cref="Area"/> and <see cref="Kg"/>.
    /// </summary>
    public sealed class Element
    {
        public string Id = "", Kind = "", Name = "";
        public Vector3 Origin;
        public float Area, Kg;
        public Vector3 BoxMin, BoxMax; // where its mass is (part frame)
        public ConvexHull[] Hulls = [];
        public Look Look = new();
        public readonly List<int> Links = []; // the elements it is welded to (by index)
        internal LookBuilder? Mesh; // (while it is made, in the ship's frame)
        internal List<Vector3>? Points; // (ditto: its hulls' points, in runs, before they are moved)
        internal List<int>? Runs;
    }

    /// <summary>A design made into its elements once a run (the first of its parts asked for makes them all).</summary>
    private sealed class Made
    {
        public Entry E = null!;
        public List<Element> Elements = [];
        public Dictionary<string, int> ById = new(StringComparer.Ordinal);
        public string Summary = "";
    }

    private static readonly Dictionary<string, Made> Makes = new(StringComparer.Ordinal); // by hash

    /// <summary>
    /// The design's elements, the parts it is built of (the one place that maps a design to parts: a bill of materials reads it too). Every
    /// band, join, piece and floor, with its look, colliders and mass, and the welds between them: each band to the next, a join to the bands
    /// it meets, a piece to the band it stands on, a floor to its deck's band, and whatever that leaves apart to its nearest element, so the
    /// body is one piece.
    /// </summary>
    public static List<Element> ElementsOf(ShipDesign d)
    {
        var els = new List<Element>();
        foreach (var _ in ElementSteps(d, els))
        {
        }
        return els;
    }

    /// <summary>
    /// <see cref="ElementsOf"/> a step at a time into <paramref name="els"/>: a step a hull body's copy, a join, a piece or an element's
    /// colliders, so the held spawn makes a new design over several ticks (each a few milliseconds) instead of one long one.
    /// </summary>
    public static IEnumerable<int> ElementSteps(ShipDesign d, List<Element> els)
    {
        var bandOf = new Dictionary<(int Hull, int Copy), (int First, int Count, double[] Z)>();
        // The hull bodies: each copy's bands, its skin and cladding cut between their rings (the end caps with the end bands).
        for (var hi = 0; hi < d.Hulls.Count; hi++)
        {
            var h = d.Hulls[hi];
            var rings = h.Ring;
            if (rings.Count < 2)
                continue;
            var tint = Tint(h.Material);
            var src = d.CladSource(h);
            var cladTint = Tint(src.CladMaterial is "Hull" or "" ? h.Material : src.CladMaterial);
            var zs = rings.Select(r => h.Z + r.Z).ToArray();
            for (var copy = 0; copy < h.Copies; copy++)
            {
                var first = els.Count;
                for (var i = 0; i + 1 < rings.Count; i++)
                    els.Add(new Element { Id = $"h{hi}.{copy}.b{i}", Kind = "band", Name = $"{h.Name}{(h.Copies > 1 ? copy == 0 ? " (right)" : " (left)" : "")}, band {i + 1}", Mesh = new LookBuilder(), Points = [], Runs = [] });
                Bands(h, copy, els, first);
                yield return els.Count;
                var (skin, fore, aft, sc, fc, ac) = DesignScene.HullMeshes(d, h, copy);
                yield return els.Count;
                var axis = new Vector3((float)h.CopyX(copy), (float)(h.Y + 0.5 * (h.Bounds().Y0 + h.Bounds().Y1) - h.Y), float.NaN);
                void Slice(DesignMesh m, Vector3 t)
                {
                    for (var tr = 0; tr < m.Tris; tr++)
                    {
                        var z = (m.P[tr * 9 + 2] + m.P[tr * 9 + 5] + m.P[tr * 9 + 8]) / 3;
                        var b = Array.BinarySearch(zs, z);
                        b = b >= 0 ? b : ~b - 1;
                        els[first + Math.Clamp(b, 0, rings.Count - 2)].Mesh!.AddTri(m, tr, t, axis);
                    }
                }
                Slice(skin, tint);
                Slice(fore, tint);
                Slice(aft, tint);
                Slice(sc, cladTint);
                Slice(fc, cladTint);
                Slice(ac, cladTint);
                for (var k = first; k < els.Count; k++)
                    if (k > first)
                        Weld(els, k - 1, k);
                bandOf[(h.Id, copy)] = (first, rings.Count - 1, zs);
                yield return els.Count;
            }
        }
        int BandAt(int hull, int copy, double z)
        {
            if (!bandOf.TryGetValue((hull, copy), out var b) && !bandOf.TryGetValue((hull, 0), out b))
                return -1;
            var i = Array.BinarySearch(b.Z, z);
            i = i >= 0 ? i : ~i - 1;
            return b.First + Math.Clamp(i, 0, b.Count - 1);
        }
        // Joins: one element a side (a mirrored one's twin is its own), welded to the bands it meets.
        for (var ji = 0; ji < d.Joins.Count; ji++)
        {
            var j = d.Joins[ji];
            var m = j.Mesh(d);
            var tint = Tint(j.PaintOf(d).Material);
            var sides = j.Mirror ? 2 : 1;
            for (var s = 0; s < sides; s++)
            {
                var e = new Element { Id = sides > 1 ? $"j{ji}.{s}" : $"j{ji}", Kind = "join", Name = $"join {ji + 1}{(sides > 1 ? s == 0 ? " (right)" : " (left)" : "")}", Mesh = new LookBuilder() };
                var pts = new List<double>();
                for (var tr = 0; tr < m.Tris; tr++)
                {
                    var x = m.P[tr * 9] + m.P[tr * 9 + 3] + m.P[tr * 9 + 6];
                    if (sides > 1 && (x < 0) != (s == 1))
                        continue;
                    e.Mesh.AddTri(m, tr, tint, new Vector3(float.NaN));
                    for (var k = 0; k < 9; k++)
                        pts.Add(m.P[tr * 9 + k]);
                }
                if (pts.Count == 0)
                    continue;
                e.Area = e.Mesh.Area;
                e.Kg = e.Area * SkinKgPerM2;
                HullOf(e, pts);
                var at = els.Count;
                els.Add(e);
                var mid = (e.BoxMin + e.BoxMax) * 0.5f;
                foreach (var end in new[] { j.A, j.B })
                    if (BandAt(end.Hull, s == 1 && sides > 1 ? 1 - end.Copy : end.Copy, mid.Z) is var b and >= 0)
                        Weld(els, b, at);
            }
            yield return els.Count;
        }
        // Pieces: one element a copy (its mirrored twin, its pair on the other body), welded to the band it stands on.
        for (var pi = 0; pi < d.Pieces.Count; pi++)
        {
            var p = d.Pieces[pi];
            if (d.Hull(p.Hull) is not { } h || PieceKinds.Find(p.Kind) is null)
                continue;
            var one = DesignScene.PieceMesh(d, p, copies: false);
            if (one.Tris == 0)
                continue;
            var sets = new List<(DesignMesh M, int Copy)> { (one, p.Copy) };
            if (p.Mirror)
                sets.Add((Mirror(one, h.CopyX(Math.Min(p.Copy, h.Copies - 1))), p.Copy)); // (its twin across its own body's centreline)
            if (p.Pair && h.Copies == 2)
                for (int k = 0, n = sets.Count; k < n; k++)
                    sets.Add((Mirror(sets[k].M, 0), 1 - sets[k].Copy)); // (the same on the other body of the pair, across the ship)
            for (var k = 0; k < sets.Count; k++)
            {
                var m = sets[k].M;
                var e = new Element { Id = sets.Count > 1 ? $"p{pi}.{k}" : $"p{pi}", Kind = "piece", Name = p.Kind, Mesh = new LookBuilder() };
                for (var tr = 0; tr < m.Tris; tr++)
                    e.Mesh.AddTri(m, tr, Vector3.One, new Vector3(float.NaN));
                e.Area = e.Mesh.Area;
                e.Kg = e.Area * PieceKgPerM2;
                HullOf(e, m.P);
                var at = els.Count;
                els.Add(e);
                if (BandAt(p.Hull, sets[k].Copy, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f) is var b and >= 0)
                    Weld(els, b, at);
            }
            yield return els.Count;
        }
        // Rooms' floors: a slab each (solid inside the bands for now), welded to the band of its deck's body there.
        for (var ri = 0; ri < d.Rooms.Count; ri++)
        {
            var r = d.Rooms[ri];
            var bx = d.Box(r);
            var (x0, x1, y0, y1, z0, z1) = ((float)bx[0], (float)bx[1], (float)bx[2], (float)bx[3], (float)bx[4], (float)bx[5]);
            var area = d.Stacked ? (x1 - x0) * (y1 - y0) : (x1 - x0) * (z1 - z0);
            if (area <= 0.01f)
                continue;
            var e = new Element { Id = $"r{ri}", Kind = "floor", Name = $"{DesignScene.Name(r)} floor", Area = area, Kg = area * DeckKgPerM2 };
            (e.BoxMin, e.BoxMax) = d.Stacked ? (new Vector3(x0, y0, z1 - 0.1f), new Vector3(x1, y1, z1)) : (new Vector3(x0, y0 - 0.1f, z0), new Vector3(x1, y0, z1));
            var at = els.Count;
            els.Add(e);
            var hull = d.Decks[r.Deck].Hull != 0 ? d.Decks[r.Deck].Hull : d.Hulls.Count > 0 ? d.Hulls[0].Id : 0;
            if (BandAt(hull, (x0 + x1) < 0 ? 1 : 0, (e.BoxMin.Z + e.BoxMax.Z) * 0.5f) is var b and >= 0)
                Weld(els, b, at);
        }
        // The bands' looks and hulls; then each element into its own frame.
        foreach (var e in els)
        {
            if (e.Points != null)
            {
                for (var k = 0; k < e.Runs!.Count; k++)
                {
                    var from = k == 0 ? 0 : e.Runs[k - 1];
                    var pts = e.Points.GetRange(from, e.Runs[k] - from);
                    if (ConvexHull.Of(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pts)) is { } hull)
                        e.Hulls = [.. e.Hulls, hull];
                }
                e.Points = null;
            }
            var middle = (e.BoxMin + e.BoxMax) * 0.5f;
            e.Origin = ConstructWorld.Quantise(middle);
            e.BoxMin -= e.Origin;
            e.BoxMax -= e.Origin;
            e.Hulls = [.. e.Hulls.Select(hl => hl.MovedInto(null, Quaternion.Identity, -e.Origin))];
            e.Look = e.Mesh?.Done(-e.Origin) ?? new Look();
            e.Mesh = null;
            if (e.Hulls.Length > 0)
                yield return els.Count;
        }
        Connect(els);
    }

    private static void Weld(List<Element> els, int a, int b)
    {
        if (a == b || els[a].Links.Contains(b))
            return;
        els[a].Links.Add(b);
        els[b].Links.Add(a);
    }

    /// <summary>Whatever the welds leave apart (bodies that only overlap, a floor with no band under it) goes to its nearest element.</summary>
    private static void Connect(List<Element> els)
    {
        if (els.Count < 2)
            return;
        var comp = new int[els.Count];
        Array.Fill(comp, -1);
        void Flood(int s, int id)
        {
            var stack = new Stack<int>();
            stack.Push(s);
            comp[s] = id;
            while (stack.Count > 0)
                foreach (var n in els[stack.Pop()].Links)
                    if (comp[n] < 0)
                    {
                        comp[n] = id;
                        stack.Push(n);
                    }
        }
        Flood(0, 0);
        for (var i = 1; i < els.Count; i++)
        {
            if (comp[i] >= 0)
                continue;
            Flood(i, i);
            // The nearest element already in the first piece, by their middles.
            var mine = (els[i].BoxMin + els[i].BoxMax) * 0.5f + els[i].Origin;
            int best = 0;
            var bd = float.MaxValue;
            for (var k = 0; k < els.Count; k++)
                if (comp[k] == 0)
                {
                    var o = (els[k].BoxMin + els[k].BoxMax) * 0.5f + els[k].Origin;
                    if (Vector3.DistanceSquared(o, mine) < bd)
                        (bd, best) = (Vector3.DistanceSquared(o, mine), k);
                }
            Weld(els, best, i);
            for (var k = 0; k < els.Count; k++)
                if (comp[k] == i)
                    comp[k] = 0;
        }
    }

    private static DesignMesh Mirror(DesignMesh m, double cx)
    {
        var o = new DesignMesh();
        for (var t = 0; t < m.Tris; t++)
            for (var v = 2; v >= 0; v--)
            {
                var i = (t * 3 + v) * 3;
                o.P.Add(2 * cx - m.P[i]); o.P.Add(m.P[i + 1]); o.P.Add(m.P[i + 2]);
                o.C.Add(m.C[i]); o.C.Add(m.C[i + 1]); o.C.Add(m.C[i + 2]);
            }
        return o;
    }

    private static Made? MadeOf(string hash, out string why)
    {
        var r = new MakeRun();
        foreach (var _ in MakeSteps(hash, r))
        {
        }
        why = r.Why;
        return r.Made;
    }

    /// <summary>What <see cref="MakeSteps"/> came to: the made design, or why not.</summary>
    private sealed class MakeRun
    {
        public Made? Made;
        public string Why = "";
        public double LongestMs;
    }

    private static bool Bad(Exception ex) => ex is FormatException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException or KeyNotFoundException;

    /// <summary>A design's elements made a step at a time (<see cref="ElementSteps"/>), once a process per design; <paramref name="run"/> holds the result.</summary>
    private static IEnumerable<int> MakeSteps(string hash, MakeRun run)
    {
        Entry? e;
        lock (Interned)
        {
            if (Makes.TryGetValue(hash, out var have))
            {
                run.Made = have;
                yield break;
            }
            if (!Known.TryGetValue(hash, out e))
            {
                run.Why = "its design hasn't come to this peer (a design part is made from its world's design store)";
                yield break;
            }
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var els = new List<Element>();
        ShipDesign? d = null;
        try
        {
            d = ShipDesign.FromJson(e.Text);
        }
        catch (Exception ex) when (Bad(ex))
        {
            run.Why = "its design couldn't be read: " + ex.Message;
        }
        if (d == null)
        {
            Console.WriteLine($"[design] REFUSED design {hash} (\"{e.Name}\"): {run.Why}");
            yield break;
        }
        using (var steps = ElementSteps(d, els).GetEnumerator())
        {
            var step = System.Diagnostics.Stopwatch.GetTimestamp();
            while (true)
            {
                bool more;
                try
                {
                    more = steps.MoveNext();
                }
                catch (Exception ex) when (Bad(ex))
                {
                    run.Why = "its design couldn't be made: " + ex.Message;
                    Console.WriteLine($"[design] REFUSED design {hash} (\"{e.Name}\"): {run.Why}");
                    yield break;
                }
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                run.LongestMs = Math.Max(run.LongestMs, (now - step) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (!more)
                    break;
                yield return els.Count;
                step = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        var made = new Made { E = e, Elements = els };
        for (var k = 0; k < els.Count; k++)
            made.ById[els[k].Id] = k;
        LastBuildMs = clock.Elapsed.TotalMilliseconds;
        LastStepMs = run.LongestMs;
        var tris = els.Sum(x => x.Look.Vertices) / 3;
        var kg = els.Sum(x => x.Kg);
        made.Summary = $"{els.Count} parts ({els.Count(x => x.Kind == "band")} bands, {els.Count(x => x.Kind == "join")} joins, {els.Count(x => x.Kind == "piece")} pieces, {els.Count(x => x.Kind == "floor")} floors), {tris} triangles, {els.Sum(x => x.Hulls.Length)} hulls, {kg / 1000:0.0} t";
        LastSummary = made.Summary;
        Console.WriteLine($"[design] made \"{e.Name}\" ({hash}) in {LastBuildMs:0} ms (its longest step {run.LongestMs:0.0} ms): {made.Summary}");
        lock (Interned)
        {
            if (Makes.TryGetValue(hash, out var raced))
                made = raced;
            else
                Makes[hash] = made;
        }
        run.Made = made;
    }

    /// <summary>A design's elements (made the first time it is asked for), or null when it isn't known here or can't be made.</summary>
    public static IReadOnlyList<Element>? Elements(string hash) => MadeOf(hash, out _)?.Elements;

    // ---- The parts ---------------------------------------------------------------------------------------------------------

    public static string KeyOf(string hash, string element) => KeyOf(hash) + " " + element;

    private static bool Split(string name, out string hash, out string element)
    {
        (hash, element) = ("", "");
        if (!IsName(name) || name.Length < Prefix.Length + 19 || name[Prefix.Length + 16] != ')' || name[Prefix.Length + 17] != ' ')
            return false;
        hash = name.Substring(Prefix.Length, 16);
        element = name[(Prefix.Length + 18)..];
        return element.Length > 0;
    }

    /// <summary>The design part a key names, made the first time it is asked for. Null: not a design key, a design not heard of here, or one that can't be made.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!Split(name, out var hash, out var id))
            return null;
        lock (Interned)
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
        if (MadeOf(hash, out var why) is not { } made)
        {
            lock (Interned)
                if (Unknown.Add(name) && Unknown.Count < 64)
                    Console.WriteLine($"[design] REFUSED \"{name}\": {why}");
            return null; // (not interned: the design may still come)
        }
        ConstructPartDef? def = null;
        if (!made.ById.TryGetValue(id, out var k))
            LastRefusal = $"no element \"{id}\" in its design";
        else
            def = PartOf(made, made.Elements[k], name);
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var raced))
                return raced;
            if (def != null && !Extrusion.Adopt(def))
            {
                def = null;
                LastRefusal = "the run-time part table is full";
            }
            if (def == null)
                Console.WriteLine($"[design] REFUSED \"{name}\": {LastRefusal}");
            else
                Info[def] = (made.E, made.Elements[k]);
            Interned[name] = def;
            return def;
        }
    }

    private static ConstructPartDef PartOf(Made made, Element e, string name)
    {
        var t = _template ??= ConstructParts.Parse(TemplateJson, "a design part's template");
        var def = t.ShallowCopy();
        def.Name = name;
        def.File = "";
        def.Description = $"{e.Name} of \"{made.E.Name}\", a ship from the Ship Designer.";
        var half = Vector3.Max((e.BoxMax - e.BoxMin) * 0.5f, new Vector3(0.02f));
        var centre = (e.BoxMax + e.BoxMin) * 0.5f;
        def.Boxes = [new CPartBox(centre, half, new Vector3(0.6f), Hidden: true, Paintable: false)];
        def.SolidBoxes = 1;
        def.BoxMass = null;
        def.HasGlass = false;
        (def.BoundsMin, def.BoundsMax) = (centre - half, centre + half);
        foreach (var hl in e.Hulls)
        {
            hl.Bounds(out var mn, out var mx);
            (def.BoundsMin, def.BoundsMax) = (Vector3.Min(def.BoundsMin, mn), Vector3.Max(def.BoundsMax, mx));
        }
        def.Hulls = e.Hulls;
        def.HullCovers = e.Hulls.Length > 0 ? [true] : []; // (its hulls stand for it; a floor, with none, collides as its slab)
        // A player meets its hulls as skin (ConstructRideMove.AddSkin, ConstructWorld.SkinBlock), never its bounding box.
        def.HasSkin = def.SkinOnly = e.Hulls.Length > 0;
        def.HullSkin = def.HasSkin ? Enumerable.Repeat(true, e.Hulls.Length).ToArray() : null;
        (def.SkinMin, def.SkinMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var hl in e.Hulls)
        {
            hl.Bounds(out var mn, out var mx);
            (def.SkinMin, def.SkinMax) = (Vector3.Min(def.SkinMin, mn), Vector3.Max(def.SkinMax, mx));
        }
        if (!def.HasSkin)
            (def.SkinMin, def.SkinMax) = (Vector3.Zero, Vector3.Zero);
        def.ColliderBoxes = false;
        def.Sockets = [t.Sockets[0] with { Name = "s0", At = Vector3.Zero }]; // (one socket the welds between elements hang on)
        def.Lamps = [];
        def.Mass = Math.Clamp(e.Kg, 0.5f, ConstructParts.MaxPartMass);
        def.Health = Math.Clamp(e.Kg * HpPerKg, 1f, ConstructParts.MaxPartHealth);
        def.SurfaceOverride = 0;
        def.MatBase = null;
        def.IsNamedRuntime = true;
        def.ExHash = BakedParts.HashOf(name) is var hx ? uint.Parse(hx[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
        def.PhysicsOnly = true;
        def.PhysicsWhy = "a ship's";
        return def;
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table; their designs stay known.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
        {
            if (Interned.Count == 0)
                return;
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
            List<ConstructPartDef>? gone = null;
            foreach (var d in Info.Keys)
                if (doomed(d))
                    (gone ??= new()).Add(d);
            if (gone != null)
                foreach (var d in gone)
                    Info.Remove(d);
        }
    }

    /// <summary>A key off the wire, as this machine's part id: a plain part when it can't be made (never a dropped packet).</summary>
    internal static ushort ReadWire(string name) => Parse(name) is { } d ? d.Id : ConstructParts.Need("Frame Beam").Id;

    private const string TemplateJson = """
        {"format":"call-of-slop-construct-part/1","name":"Design part","description":"A part of a ship from the Ship Designer.","group":"Production","section":"Designs",
         "role":"frame","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":4,"stage":true,"centreSockets":false,
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5]}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;

    /// <summary>
    /// A design's body built in <paramref name="c"/> (ConstructBuild.BuildSteps, for a blueprint of <see cref="BlueprintOf"/>): its elements'
    /// parts where the design has them, then the welds between them; one step an element (the held spawn builds it a slice a tick).
    /// </summary>
    public static IEnumerable<int> BuildSteps(ConstructWorld world, Construct c, string hash, ConstructBuild.BuildRun run)
    {
        var mr = new MakeRun();
        foreach (var k in MakeSteps(hash, mr))
            yield return -1; // (the elements made a step a tick-slice: the first spawn of a design)
        if (mr.Made is not { } made)
        {
            run.Why = mr.Why;
            yield break;
        }
        var partOf = new int[made.Elements.Count];
        for (var k = 0; k < made.Elements.Count; k++)
        {
            var e = made.Elements[k];
            if (Parse(KeyOf(hash, e.Id)) is not { } def)
            {
                run.Why = $"its {e.Name} couldn't be made ({LastRefusal})";
                yield break;
            }
            partOf[k] = world.AddPart(c, def, e.Origin, 0);
            if (partOf[k] < 0)
            {
                run.Why = "the world is full";
                yield break;
            }
            yield return k;
        }
        for (var k = 0; k < made.Elements.Count; k++)
            foreach (var n in made.Elements[k].Links)
                if (n > k && !world.AddLink(c, partOf[k], 0, partOf[n], 0))
                {
                    run.Why = "too many welds";
                    yield break;
                }
        c.Rebuild();
    }

    // ---- Colliders ---------------------------------------------------------------------------------------------------------

    /// <summary>A body copy's bands from element <paramref name="first"/> on: each one's hull points (one run, or a few across a big profile), skin area, mass and bounds.</summary>
    private static void Bands(HullBody h, int copy, List<Element> els, int first)
    {
        var rings = h.Ring;
        var cx = h.CopyX(copy);
        Span<double> ah = stackalloc double[HullBody.MaxLoft], ay = stackalloc double[HullBody.MaxLoft];
        Span<double> bh = stackalloc double[HullBody.MaxLoft], by = stackalloc double[HullBody.MaxLoft];
        for (var i = 0; i + 1 < rings.Count; i++)
        {
            var e = els[first + i];
            var za = (float)(h.Z + rings[i].Z);
            var zb = (float)(h.Z + rings[i + 1].Z);
            var n = h.Pair(i, ah, ay, bh, by);
            var sh = ah.ToArray();
            var sy = ay.ToArray();
            var th = bh.ToArray();
            var ty = by.ToArray();
            Vector3 A(int k, int s) => new((float)(cx + s * sh[k]), (float)(h.Y + sy[k]), za);
            Vector3 B(int k, int s) => new((float)(cx + s * th[k]), (float)(h.Y + ty[k]), zb);
            var area = 0f;
            for (var k = 0; k + 1 < n; k++)
                area += 2 * Quad(A(k, 1), A(k + 1, 1), B(k + 1, 1), B(k, 1));
            area += Quad(A(0, -1), A(0, 1), B(0, 1), B(0, -1)) + Quad(A(n - 1, -1), A(n - 1, 1), B(n - 1, 1), B(n - 1, -1));
            e.Area = area;
            e.Kg = area * SkinKgPerM2;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var k = 0; k < n; k++)
                foreach (var v in new[] { A(k, 1), A(k, -1), B(k, 1), B(k, -1) })
                    (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            (e.BoxMin, e.BoxMax) = (lo, hi);
            if (MathF.Abs(zb - za) < 1e-3f)
                continue;
            var pts = e.Points!;
            if (4 * n <= ConvexHull.MaxCorners)
            {
                for (var k = 0; k < n; k++)
                {
                    pts.Add(A(k, 1)); pts.Add(A(k, -1)); pts.Add(B(k, 1)); pts.Add(B(k, -1));
                }
                e.Runs!.Add(pts.Count);
                continue;
            }
            // Too many corners for one: each side in runs of points, each run closed on the centreline.
            const int Run = ConvexHull.MaxCorners / 2 - 3;
            foreach (var s in new[] { -1, 1 })
                for (var k0 = 0; k0 < n - 1; k0 += Run - 1)
                {
                    var k1 = Math.Min(n - 1, k0 + Run - 1);
                    for (var k = k0; k <= k1; k++)
                    {
                        pts.Add(A(k, s)); pts.Add(B(k, s));
                    }
                    pts.Add(A(k0, 0)); pts.Add(A(k1, 0)); pts.Add(B(k0, 0)); pts.Add(B(k1, 0));
                    e.Runs!.Add(pts.Count);
                }
        }
    }

    /// <summary>The 26 directions a hull of a mesh is taken along (the cube's faces, edges and corners): its extreme point along each.</summary>
    private static readonly Vector3[] Dirs = MakeDirs();

    private static Vector3[] MakeDirs()
    {
        var l = new List<Vector3>();
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                    if (x != 0 || y != 0 || z != 0)
                        l.Add(Vector3.Normalize(new Vector3(x, y, z)));
        return [.. l];
    }

    /// <summary>An element's hull from its mesh's points (the extreme point along each of <see cref="Dirs"/>) and its bounds.</summary>
    private static void HullOf(Element e, IReadOnlyList<double> p)
    {
        Span<Vector3> best = stackalloc Vector3[26];
        Span<float> at = stackalloc float[26];
        at.Fill(float.MinValue);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i + 2 < p.Count; i += 3)
        {
            var v = new Vector3((float)p[i], (float)p[i + 1], (float)p[i + 2]);
            (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            for (var k = 0; k < Dirs.Length; k++)
            {
                var s = Vector3.Dot(v, Dirs[k]);
                if (s > at[k])
                    (at[k], best[k]) = (s, v);
            }
        }
        (e.BoxMin, e.BoxMax) = (lo, hi);
        if (ConvexHull.Of(best) is { } hull)
            e.Hulls = [hull];
    }

    private static float Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => 0.5f * Vector3.Cross(c - a, d - b).Length();

    // ---- The look ----------------------------------------------------------------------------------------------------------

    /// <summary>The designer's materials as a tint over the vertex colours (UiShipDesignerScene's SdMats): metals greyed, carbon dark.</summary>
    private static Vector3 Tint(string material) => material switch
    {
        "Steel" => Rgb(0xc8ccd0),
        "Brushed aluminium" => Rgb(0xe4e7ea),
        "Titanium" => Rgb(0xb6b2a8),
        "Copper" => Rgb(0xf0a070),
        "Carbon" => Rgb(0x3a3d42),
        _ => Vector3.One,
    };

    private static Vector3 Rgb(uint hex) => new Vector3((hex >> 16) & 255, (hex >> 8) & 255, hex & 255) / 255f;

    /// <summary>Gathers triangles into one mesh: flat normals, each turned out from a middle (a hull's axis), colours tinted and made linear.</summary>
    internal sealed class LookBuilder
    {
        private readonly List<float> _p = new(1024), _n = new(1024);
        private readonly List<uint> _c = new(256);
        public float Area;

        /// <param name="middle">What a normal faces away from: a point, or (NaN z) a line along z through x, y; all NaN: the triangle's own side (as drawn).</param>
        public void AddTri(DesignMesh m, int t, Vector3 tint, Vector3 middle)
        {
            var i = t * 9;
            var a = new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]);
            var b = new Vector3((float)m.P[i + 3], (float)m.P[i + 4], (float)m.P[i + 5]);
            var c = new Vector3((float)m.P[i + 6], (float)m.P[i + 7], (float)m.P[i + 8]);
            var n = Vector3.Cross(b - a, c - a);
            var len = n.Length();
            if (len < 1e-7f)
                return;
            Area += 0.5f * len;
            n /= len;
            if (!float.IsNaN(middle.X))
            {
                var mid = (a + b + c) / 3;
                var from = float.IsNaN(middle.Z) ? new Vector3(middle.X, middle.Y, mid.Z) : middle;
                if (Vector3.Dot(n, mid - from) < 0)
                    n = -n;
            }
            for (var k = 0; k < 3; k++)
            {
                var v = k == 0 ? a : k == 1 ? b : c;
                _p.Add(v.X); _p.Add(v.Y); _p.Add(v.Z);
                _n.Add(n.X); _n.Add(n.Y); _n.Add(n.Z);
                var ci = (t * 3 + k) * 3;
                var col = ci + 2 < m.C.Count ? new Vector3(m.C[ci], m.C[ci + 1], m.C[ci + 2]) / 255f * tint : tint;
                _c.Add(Pack(col));
            }
        }

        private static uint Pack(Vector3 srgb)
        {
            static uint L(float v)
            {
                v = Math.Clamp(v, 0, 1);
                var lin = v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f); // (the box shader's colours are linear: Renderer.cs)
                return (uint)MathF.Round(MathF.Sqrt(lin) * 255f); // (kept as its square root: 8 bits keep the darks; the vertex shader squares it)
            }
            return L(srgb.X) | L(srgb.Y) << 8 | L(srgb.Z) << 16 | 0xff000000u;
        }

        public Look Done(Vector3 shift)
        {
            var l = new Look { Pos = [.. _p], Normal = [.. _n], Colour = [.. _c], Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
            for (var i = 0; i + 2 < l.Pos.Length; i += 3)
            {
                var v = new Vector3(l.Pos[i] + shift.X, l.Pos[i + 1] + shift.Y, l.Pos[i + 2] + shift.Z);
                (l.Pos[i], l.Pos[i + 1], l.Pos[i + 2]) = (v.X, v.Y, v.Z);
                (l.Min, l.Max) = (Vector3.Min(l.Min, v), Vector3.Max(l.Max, v));
            }
            return l;
        }
    }

    // ---- The world's store: the wire and the save ---------------------------------------------------------------------------

    /// <summary>One design: u32 bytes, the text (CBodyKind.ShipDesign).</summary>
    public static void Write(PacketWriter w, Entry e)
    {
        var bytes = Encoding.UTF8.GetBytes(e.Text);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.ShipDesign).U32((uint)bytes.Length).Bytes(bytes);
    }

    /// <summary>Client: a design off the wire, checked here, into the shared table and the world's list.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var n = r.U32();
        if (n > MaxText)
            throw new MalformedPacketException($"a ship design of {n} bytes");
        var text = Encoding.UTF8.GetString(r.Take((int)n));
        if (Register(text) is { } hash)
            world.KeepShipDesign(hash);
        else
            Console.WriteLine($"[design] a design from the host was refused: {LastRefusal}");
    }

    public static void WriteSave(SaveWriter w, IReadOnlyList<string> texts)
    {
        w.I32(texts.Count);
        foreach (var t in texts)
            w.Str(t);
    }

    public static void ReadSave(SaveReader r, ConstructWorld world)
    {
        var n = r.Count(16);
        for (var k = 0; k < n; k++)
        {
            var text = r.Str();
            if (Register(text) is { } hash)
                world.KeepShipDesign(hash);
            else
                Console.WriteLine($"[design] REFUSED a saved design: {LastRefusal}");
        }
    }

    /// <summary>Keeps in <paramref name="world"/> every design its bodies hold (before a save and before a joiner's designs go). Not per tick.</summary>
    public static void NoteInUse(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                NoteInUse(world, c.DefOf(i));
    }

    public static void NoteInUse(ConstructWorld world, ConstructPartDef d)
    {
        if (EntryOf(d) is { } e)
            world.KeepShipDesign(e.Hash);
    }

    /// <summary>The world's designs as text, for its save.</summary>
    public static string[] TextsOf(ConstructWorld world)
    {
        var list = new List<string>(world.ShipDesigns.Count);
        lock (Interned)
            foreach (var h in world.ShipDesigns)
                if (Known.TryGetValue(h, out var e))
                    list.Add(e.Text);
        return [.. list];
    }

    /// <summary>A blueprint that builds the design's body (ConstructBuild.BuildSteps: <see cref="BuildSteps"/>), for the held-spawn path that sets blueprints down.</summary>
    public static ConstructBuild.Blueprint BlueprintOf(string hash, string name) => new() { Name = name, File = "a ship design", DesignHash = hash };
}

public sealed partial class ConstructWorld
{
    /// <summary>The ship designs this world's bodies use, by hash (DesignParts.cs): saved with it, sent to every joiner.</summary>
    public readonly List<string> ShipDesigns = new(8);
    private readonly HashSet<string> _shipDesignSet = new(StringComparer.Ordinal);

    /// <summary>Server: designs new this tick, to everyone at the next flush (before the bodies that name them).</summary>
    public readonly List<string> ShipDesignsNew = new(4);

    public void KeepShipDesign(string hash)
    {
        if (_shipDesignSet.Count >= DesignParts.MaxWorld || !_shipDesignSet.Add(hash))
            return;
        ShipDesigns.Add(hash);
        ShipDesignsNew.Add(hash);
    }

    internal void ClearShipDesigns()
    {
        ShipDesigns.Clear();
        _shipDesignSet.Clear();
        ShipDesignsNew.Clear();
    }
}

public sealed partial class Construct
{
    /// <summary>Where part <paramref name="i"/>'s mass sits (its rest bounds' middle, or a design part's boxes by their shares: DesignParts.cs).</summary>
    internal Vector3 MassMiddle(int i, Vector3 mn, Vector3 mx)
    {
        var d = Parts[i].D;
        if (d.BoxMass is not { } share || share.Length < d.SolidBoxes)
            return (mn + mx) * 0.5f;
        var c = Vector3.Zero;
        for (var b = 0; b < d.SolidBoxes; b++)
        {
            BoxOf(i, b, out var centre, out _);
            c += centre * share[b];
        }
        return c;
    }
}
