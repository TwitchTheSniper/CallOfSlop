using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Baked parts (the bake route, Sean 2026-11-20; design: the "Detailed hull pieces" doc and the "Zero Day Hull Pieces" page): a Bake Oven
/// (ConstructBakeOven.cs) turns a welded group of subparts into one part, <c>Baked (#hash)</c>, and turns it back.
/// <para>
/// <b>The design.</b> A baked part's key names its design by hash: the design is text, one line a subpart (its part's name or key, its place
/// in centimetres from the baked part's origin, its Rot24 turn, its paint and its finish), sorted, so the same group always gives the same
/// hash wherever it was baked. The designs live in a table every peer shares by hash (<see cref="Known"/>), and each world keeps those its
/// ovens made (<see cref="ConstructWorld.Baked"/>): saved with the world in a section of its own ahead of the part names, sent to a joiner
/// before any body and to everyone as each is made (<see cref="CBodyKind.Baked"/>), so every peer can make the part from its key before a
/// body, a store or a save names it. A design read from the wire or a save is checked against its hash.
/// </para>
/// <para>
/// <b>The part.</b> Every subpart's boxes, turned and moved to its place, with its paint and finish drawn into them (a shaped box keeps its
/// turn in <see cref="CPartBox.Turn"/>), so armour stays where the slabs were; its sockets but the pairs that met inside the group; the mass
/// and health of all of them; the lamps. It recycles by its mass, so it costs what its subparts cost. At most
/// <see cref="ConstructParts.MaxBoxes"/> boxes: a bigger group is refused.
/// </para>
/// <para>
/// <b>What bakes.</b> Plain structure only: frames, panels and foundations (part files and run-time parts alike: formed skins, pressed slabs,
/// extrusions, hull sections, other baked parts), on the body or on freeform welds at a grid turn. Module Socket plates are never baked: they
/// stay real parts welded beside the baked one (the socket design). Un-baking puts the subparts back where they were, the damage shared out
/// by health, and the plates with them.
/// </para>
/// </summary>
public static partial class BakedParts
{
    public const string Prefix = "Baked (#";
    public const string Format = "call-of-slop-baked/1";
    public const int MaxText = 1 << 17; // bytes of one design (255 subparts of the longest keys)
    public const int MaxSubparts = 255;
    public const int MaxWorld = 4096; // designs one world keeps

    /// <summary>One subpart of a design: its part, place (cm), turn, paint (0: its own) and finish's name ("": its own).</summary>
    public readonly record struct Sub(string Name, int X, int Y, int Z, byte Rot, uint Paint, string Finish)
    {
        public Vector3 Pos => new Vector3(X, Y, Z) / 100f;
    }

    /// <summary>A design read from its text.</summary>
    public sealed class Design
    {
        public string Hash = "", Text = "";
        public Sub[] Subs = [];
    }

    private static readonly Dictionary<string, Design> Known = new(StringComparer.Ordinal); // by hash: every design this run has heard of
    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal); // by key
    private static readonly Dictionary<ConstructPartDef, Design> Info = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<string> Unknown = new(StringComparer.Ordinal); // keys asked for before their design came (logged once)
    private static ConstructPartDef? _template;

    /// <summary>Counts for a check.</summary>
    public static int Made, Refused;

    /// <summary>Why the last <see cref="Bake"/>, <see cref="Unbake"/> or <see cref="Register"/> gave nothing.</summary>
    [ThreadStatic] public static string? LastRefusal;

    /// <summary>A key that names a design: a baked part's, or a packed group's (PackedGroups.cs).</summary>
    public static bool IsName(string name) => name.StartsWith(Prefix, StringComparison.Ordinal) || name.StartsWith(PackedPrefix, StringComparison.Ordinal);

    public static bool IsBaked(ConstructPartDef d) => d.IsNamedRuntime && d.Name.StartsWith(Prefix, StringComparison.Ordinal);

    public static string KeyOf(string hash) => Prefix + hash + ")";

    private static string? HashOfKey(string name)
    {
        var pre = name.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix.Length : name.StartsWith(PackedPrefix, StringComparison.Ordinal) ? PackedPrefix.Length : -1;
        return pre > 0 && name.EndsWith(')') && name.Length == pre + 17 ? name[pre..^1] : null;
    }

    /// <summary>The design a baked part was made from (null: not a baked part).</summary>
    public static Design? DesignOf(ConstructPartDef d)
    {
        if (!d.IsNamedRuntime || !IsName(d.Name))
            return null;
        lock (Interned)
            return Info.GetValueOrDefault(d);
    }

    /// <summary>The design of a hash, if this peer has heard of it.</summary>
    public static Design? Find(string hash)
    {
        lock (Interned)
            return Known.GetValueOrDefault(hash);
    }

    // ---- The design's text -------------------------------------------------------------------------------------------------

    public static string HashOf(string text)
    {
        var h = 14695981039346656037ul;
        foreach (var b in Encoding.UTF8.GetBytes(text))
            h = (h ^ b) * 1099511628211ul;
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>The design's text: the format line, then one line a subpart, sorted (the same group, the same text).</summary>
    public static string TextOf(IEnumerable<Sub> subs)
    {
        var lines = subs.Select(LineOf).ToList();
        lines.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder(Format).Append('\n');
        foreach (var l in lines)
            sb.Append(l).Append('\n');
        return sb.ToString();
    }

    private static string LineOf(Sub s) => string.Create(CultureInfo.InvariantCulture,
        $"{s.Name}\t{s.X},{s.Y},{s.Z}\t{s.Rot}\t{(ConstructPaint.IsSet(s.Paint) ? (s.Paint & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture) : "-")}\t{(s.Finish.Length > 0 ? s.Finish : "-")}");

    /// <summary>A design's text read back (null: not one this build reads; <see cref="LastRefusal"/> says why).</summary>
    public static Design? Read(string text)
    {
        if (text.Length > MaxText)
            return Refuse("the design is too long");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || lines[0] != Format)
            return Refuse("not a baked design");
        if (lines.Length - 1 > MaxSubparts)
            return Refuse($"over {MaxSubparts} subparts");
        var subs = new Sub[lines.Length - 1];
        for (var i = 1; i < lines.Length; i++)
        {
            var f = lines[i].Split('\t');
            if (f.Length != 5 || f[0].Length == 0 || f[0].Length > ConstructParts.MaxRuntimeName)
                return Refuse($"line {i} can't be read");
            var at = f[1].Split(',');
            if (at.Length != 3 || !int.TryParse(at[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(at[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
                || !int.TryParse(at[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z)
                || Math.Abs(x) > 40000 || Math.Abs(y) > 40000 || Math.Abs(z) > 40000)
                return Refuse($"line {i}: a place that can't be read");
            if (!byte.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out var rot) || rot >= Rot24.Count)
                return Refuse($"line {i}: a turn that can't be read");
            uint paint = 0;
            if (f[3] != "-")
            {
                if (!uint.TryParse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) || rgb > 0xFFFFFF)
                    return Refuse($"line {i}: a paint that can't be read");
                paint = ConstructPaint.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
            subs[i - 1] = new Sub(f[0], x, y, z, rot, paint, f[4] == "-" ? "" : f[4]);
        }
        return new Design { Text = text, Hash = HashOf(text), Subs = subs };
    }

    /// <summary>Keeps a design in the shared table (it is checked first). Its hash, or null when it can't be read.</summary>
    public static string? Register(string text)
    {
        var hash = HashOf(text);
        lock (Interned)
            if (Known.ContainsKey(hash))
                return hash;
        if (Read(text) is not { } design)
            return null;
        lock (Interned)
        {
            Known.TryAdd(hash, design);
            if (Unknown.Remove(KeyOf(hash)))
                Console.WriteLine($"[bake] design {hash} came: its part can be made now");
        }
        return hash;
    }

    private static Design? Refuse(string why)
    {
        LastRefusal = why;
        Refused++;
        return null;
    }

    // ---- The part ----------------------------------------------------------------------------------------------------------

    /// <summary>The baked part a key names, made the first time it is asked for. Null: not a baked key, a design this peer hasn't heard of, or one it can't make.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (HashOfKey(name) is not { } hash)
            return null;
        Design? design;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            if (!Known.TryGetValue(hash, out design))
            {
                if (Unknown.Add(name))
                    Console.WriteLine($"[bake] REFUSED \"{name}\": its design hasn't come to this peer (a baked part is made from its world's design store)");
                return null; // (not interned: the design may still come)
            }
        }
        // The subparts are found outside the lock (the first ask loads the parts; a subpart may be a baked part itself).
        var made = Build(design, name, name.StartsWith(PackedPrefix, StringComparison.Ordinal));
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var raced))
            {
                if (made != null)
                    PartShapes.Release(made.OwnShapes);
                return raced;
            }
            if (made != null && !Extrusion.Adopt(made))
            {
                PartShapes.Release(made.OwnShapes);
                made = null;
                LastRefusal = "the run-time part table is full";
            }
            if (made == null)
                Console.WriteLine($"[bake] REFUSED \"{name}\": {LastRefusal}");
            Interned[name] = made;
            if (made != null)
            {
                Info[made] = design;
                Made++;
            }
            return made;
        }
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

    // The template every baked part starts from (its role, tool and flags); Build replaces its boxes, sockets, mass and health.
    private const string TemplateJson = """
        {"format":"call-of-slop-construct-part/1","name":"Baked part","description":"Baked in a Bake Oven.","group":"Production","section":"Baked parts",
         "role":"frame","tool":"welder","mass":10,"health":100,"material":"steel","color":"#8a8f96","attachSeconds":4,"stage":true,"centreSockets":false,
         "boxes":[{"min":[-0.5,-0.01,-0.5],"max":[0.5,0.01,0.5]}],
         "sockets":[{"name":"back","at":[0,-0.01,0],"dir":"-y"}]}
        """;

    private static ConstructPartDef? Build(Design design, string name, bool packed)
    {
        var outer = PartShapes.BeginCollect();
        ConstructPartDef? d = null;
        try
        {
            var solid = new List<CPartBox>(64);
            var detail = new List<CPartBox>(64);
            var socks = new List<(CSocket S, int Sub)>(64);
            var lamps = new List<(Vector3 At, Vector3 Colour)>();
            float mass = 0, health = 0, heaviest = -1;
            var material = "steel";
            var colliderBoxes = false;
            for (var k = 0; k < design.Subs.Length; k++)
            {
                var s = design.Subs[k];
                if (ConstructParts.Find(s.Name) is not { } sd)
                {
                    LastRefusal = $"no part \"{s.Name}\" in this build";
                    return null;
                }
                if (!packed && BakeRefusal(sd) is { } why)
                {
                    LastRefusal = $"\"{s.Name}\" can't be baked: {why}";
                    return null;
                }
                var finish = ConstructFinish.Of(s.Finish);
                var p = s.Pos;
                foreach (var b in sd.Boxes)
                {
                    if (b.Shape != 0)
                        PartShapes.Keep(b.Shape); // (the subpart's shape is held by this part too: it may outlive the subpart's own def)
                    var nb = b with
                    {
                        Center = p + Rot24.Apply(s.Rot, b.Center),
                        Half = Rot24.ApplyHalf(s.Rot, b.Half),
                        Turn = b.Shape != 0 ? Rot24.Compose(s.Rot, b.Turn) : (byte)0,
                        Color = ConstructPaint.BoxColour(s.Paint, in b),
                        Surface = ConstructFinish.BoxRow(in b, s.Paint, finish),
                    };
                    (b.Detail ? detail : solid).Add(nb);
                }
                foreach (var so in sd.Sockets)
                    socks.Add((so with { At = p + Rot24.Apply(s.Rot, so.At), Dir = Rot24.ApplyDir(s.Rot, so.Dir) }, k));
                foreach (var l in sd.Lamps)
                    lamps.Add((p + Rot24.Apply(s.Rot, l.At), l.Colour));
                mass += sd.Mass;
                health += sd.Health;
                colliderBoxes |= sd.ColliderBoxes;
                if (sd.Mass > heaviest)
                    (heaviest, material) = (sd.Mass, sd.Material);
            }
            if (packed && solid.Count + detail.Count > ConstructParts.MaxBoxes && solid.Count <= ConstructParts.MaxBoxes)
                detail.RemoveRange(Math.Max(0, ConstructParts.MaxBoxes - solid.Count), detail.Count - Math.Max(0, ConstructParts.MaxBoxes - solid.Count)); // (a packed group is only seen in a store: its small detail goes first)
            if (solid.Count + detail.Count > ConstructParts.MaxBoxes)
            {
                LastRefusal = $"{solid.Count + detail.Count} boxes, at most {ConstructParts.MaxBoxes} a part";
                return null;
            }
            if (solid.Count == 0)
            {
                LastRefusal = "no solid box";
                return null;
            }
            // Sockets: every subpart's, less the pairs that met inside the group (two of different subparts at one point, facing each other).
            var keep = new bool[socks.Count];
            Array.Fill(keep, true);
            for (var i = 0; i < socks.Count; i++)
                for (var j = i + 1; j < socks.Count && keep[i]; j++)
                    if (keep[j] && socks[i].Sub != socks[j].Sub && socks[i].S.Dir == (socks[j].S.Dir ^ 1) && Vector3.DistanceSquared(socks[i].S.At, socks[j].S.At) < 0.02f * 0.02f)
                        keep[i] = keep[j] = false;
            var sockets = new List<CSocket>(Math.Min(socks.Count, ConstructParts.MaxSockets));
            for (var i = 0; i < socks.Count && sockets.Count < ConstructParts.MaxSockets; i++)
                if (keep[i])
                    sockets.Add(socks[i].S with { Name = $"s{sockets.Count}" });
            if (sockets.Count == 0)
                sockets.Add(socks[0].S with { Name = "s0" }); // (a group whose every socket met another: one to build on)

            var t = _template ??= ConstructParts.Parse(TemplateJson, "a baked part's template");
            d = t.ShallowCopy();
            d.Name = name;
            d.File = "";
            d.Description = packed
                ? $"A welded group of {design.Subs.Length} parts, kept as one piece in a store. It is the group again as soon as it leaves the store."
                : $"A baked part: {design.Subs.Length} subparts made one in a Bake Oven. An oven set to Un-bake turns it back.";
            d.Boxes = [.. solid, .. detail];
            d.SolidBoxes = solid.Count;
            d.HasGlass = d.Boxes.Any(x => x.IsGlass);
            (d.BoundsMin, d.BoundsMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var b in solid)
                (d.BoundsMin, d.BoundsMax) = (Vector3.Min(d.BoundsMin, b.Center - b.Half), Vector3.Max(d.BoundsMax, b.Center + b.Half));
            (d.SkinMin, d.SkinMax, d.HasSkin) = (new Vector3(float.MaxValue), new Vector3(float.MinValue), false);
            foreach (var b in detail)
                if (b.Shape != 0 && PartShapes.KindOf(b.Shape) == "hull")
                    (d.HasSkin, d.SkinMin, d.SkinMax) = (true, Vector3.Min(d.SkinMin, b.Center - b.Half), Vector3.Max(d.SkinMax, b.Center + b.Half));
            d.Hulls = null;
            d.HullCovers = null;
            d.HullSkin = null;
            d.ColliderBoxes = colliderBoxes;
            d.Sockets = [.. sockets];
            d.Lamps = [.. lamps];
            d.LampLines = []; // (a baked group's lamps are points)
            d.Mass = Math.Clamp(mass, 0.5f, ConstructParts.MaxPartMass);
            d.Health = Math.Clamp(health, 1f, ConstructParts.MaxPartHealth);
            d.Material = material;
            d.SurfaceOverride = 0;
            d.MatBase = null;
            d.IsNamedRuntime = true;
            d.ExHash = uint.Parse(design.Hash[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) ^ (packed ? 0x9E3779B9u : 0u); // (a group packed and the same group baked are two parts)
            d.PhysicsOnly = true; // (never into the hands or a backpack: a body in the world, or a count in a store)
            if (string.IsNullOrEmpty(d.PhysicsWhy))
                d.PhysicsWhy = "structural";
            return d;
        }
        finally
        {
            var shapes = PartShapes.EndCollect(outer);
            if (d != null)
                d.OwnShapes = shapes;
            else
                PartShapes.Release(shapes);
        }
    }

    /// <summary>Why a part can't be baked into a group (null: it can): plain structure only, nothing that works, moves or holds.</summary>
    public static string? BakeRefusal(ConstructPartDef d, bool tell = true)
    {
        if (IsPacked(d))
            return "a packed group (out of a store: it is its group again within a second)";
        if (d.ModPart != ModPart.None)
            return "a module socket or plug (plates stay real parts)";
        if (d.Role is not (PartRole.Frame or PartRole.Panel or PartRole.Foundation))
            return tell ? $"it is a {d.Role.ToString().ToLowerInvariant()}, not structure" : No;
        if (d.HasJoint || d.Rotor != null || d.HasRig || d.HasWork || d.HasEngineBoxes || d.HasRouterBoxes || d.UseBoxes.Length > 0 || d.StatusBox >= 0
            || d.Panels.Length > 0 || d.Explosive != null || d.IsEnvelope || d.Breach != BreachKind.None || d.HasSeams || d.HasMiddle)
            return "it works, moves or holds";
        return null;
    }

    // ---- Baking and un-baking a body (server) -------------------------------------------------------------------------------

    /// <summary>A part of a body as the bake sees it: its place and turn in the body's frame.</summary>
    private readonly record struct Placed(int Part, Vector3 Pos, byte Rot);

    private static bool Place(Construct c, int i, out Placed at)
    {
        ref var p = ref c.Parts[i];
        at = default;
        if (!c.Rigid(p.Seg))
            return false;
        c.RestPose(p.Seg, out var q, out var t);
        var r = Rot24.Nearest(q * Rot24.Quat(p.Rot), out var match);
        if (match < 0.9999f)
            return false;
        at = new Placed(i, t + Vector3.Transform(p.Pos, q), r);
        return true;
    }

    /// <summary>
    /// Why body <paramref name="c"/> can't be baked (null: it can): a part that isn't plain structure, a joint, a part off the grid turns,
    /// nothing to bake, or more boxes than one part takes. Module Socket plates are left out of the bake (they stay).
    /// </summary>
    public static string? CanBake(Construct c, bool tell = true)
    {
        var bake = 0;
        var lone = -1;
        var boxes = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (!Place(c, i, out _))
                return tell ? $"{Shown(d)} isn't fixed at a grid turn (a joint, or a weld at an angle)" : No;
            if (d.ModPart == ModPart.Socket)
                continue;
            if (BakeRefusal(d, tell) is { } why)
                return tell ? $"{Shown(d)} can't be baked: {why}" : No;
            bake++;
            lone = i;
            boxes += d.Boxes.Length;
        }
        if (bake == 1 && !(lone >= 0 && c.DefOf(lone).IsExtrusion && BakeKits.IsOnePieceKey(c.DefOf(lone).ExKey)))
            return "nothing to bake: weld two or more subparts"; // (or one extrusion that is a part's whole kit: a one-piece bake, BakeKits.cs)
        if (bake < 1)
            return "nothing to bake: weld two or more subparts";
        if (bake > MaxSubparts)
            return tell ? $"over {MaxSubparts} subparts" : No;
        if (boxes > ConstructParts.MaxBoxes)
            return tell ? $"{boxes} boxes, at most {ConstructParts.MaxBoxes} in one baked part" : No;
        return null;
    }

    /// <summary>Why body <paramref name="c"/> can't be un-baked (null: it can): it must hold one baked part, and nothing else but Module Socket plates.</summary>
    public static string? CanUnbake(Construct c, out int baked, bool tell = true)
    {
        baked = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.ModPart == ModPart.Socket)
                continue;
            if (!IsBaked(d))
                return tell ? $"{Shown(d)} isn't baked" : No;
            if (baked >= 0)
                return "more than one baked part: weld them apart";
            baked = i;
        }
        if (baked < 0)
            return "no baked part";
        if (!Place(c, baked, out _))
            return "the baked part isn't fixed at a grid turn";
        if (DesignOf(c.DefOf(baked)) == null)
            return "its design isn't known here";
        return null;
    }

    /// <summary>The refusal a check gives when its caller didn't ask why (no string made: the oven's check runs every tenth of a second).</summary>
    private const string No = "no";

    /// <summary>A part's name as a person reads it.</summary>
    public static string Shown(ConstructPartDef d) => IsBaked(d) ? "a baked part" : HullHoles.Shown(d);

    /// <summary>
    /// A group's design text, its subparts placed and turned in one frame, and the design's origin in that frame (the group's solid middle
    /// to the centimetre). The same group gives the same text wherever it stands, so a kit's text at each turn can be known before it is built.
    /// </summary>
    public static string DesignText(IReadOnlyList<(ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)> group, out Vector3 origin)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var g in group)
            foreach (var b in g.Def.Boxes.AsSpan(0, g.Def.SolidBoxes))
            {
                var centre = g.Pos + Rot24.Apply(g.Rot, b.Center);
                var half = Rot24.ApplyHalf(g.Rot, b.Half);
                (lo, hi) = (Vector3.Min(lo, centre - half), Vector3.Max(hi, centre + half));
            }
        // (the bounds to the half centimetre first, in whole numbers: a group welded freeform sits a hair off its grid, and a middle on a
        // half centimetre must not round one way here and the other way there)
        origin = new Vector3(MidCm(lo.X, hi.X), MidCm(lo.Y, hi.Y), MidCm(lo.Z, hi.Z)) / 100f;
        var subs = new List<Sub>(group.Count);
        foreach (var g in group)
        {
            var rel = (g.Pos - origin) * 100;
            subs.Add(new Sub(g.Def.Name, (int)MathF.Round(rel.X), (int)MathF.Round(rel.Y), (int)MathF.Round(rel.Z), g.Rot,
                ConstructPaint.IsSet(g.Paint) ? g.Paint : 0, g.Finish));
        }
        return TextOf(subs);
    }

    private static int MidCm(float lo, float hi)
    {
        var sum = (long)MathF.Round(lo * 200) + (long)MathF.Round(hi * 200); // four times the middle, in cm
        return (int)Math.Floor(sum / 4.0 + 0.5); // (halves up, not to even: the same group moved by whole centimetres keeps its hash; a group with a middle on a half centimetre may bake to a new hash after 2026-11-23)
    }

    /// <summary>A baked kit's body: the catalogue part where the kit's frame put it, the plates kept, the old body gone.</summary>
    private static Construct? Swap(ConstructWorld world, Construct c, ConstructPartDef def, Vector3 at, byte rot, float share, List<Placed> plates, int count)
    {
        if (world.Create(c.Pos, c.Rot) is not { } nb)
        {
            LastRefusal = "the world is full";
            return null;
        }
        var bi = world.AddPart(nb, def, at, rot);
        if (bi < 0)
        {
            world.Remove(nb, quiet: true);
            LastRefusal = "the world is full";
            return null;
        }
        nb.Parts[bi].Health = Math.Clamp(def.Health * share, 1f, def.Health);
        foreach (var p in plates)
            Copy(world, c, p.Part, nb, p.Pos, p.Rot);
        Settle(world, c, nb);
        world.Events.Add($"construct {c.Id}: baked {count} pieces of its kit into {def.Name} -> construct {nb.Id}");
        return nb;
    }

    /// <summary>
    /// Server: bakes body <paramref name="c"/> into a new body of one baked part (and its socket plates), where it stood. The old body goes.
    /// Null: it can't (<see cref="LastRefusal"/>). The design is kept in the world (sent to everyone with the next flush, before the body).
    /// </summary>
    public static Construct? Bake(ConstructWorld world, Construct c, ushort want = 0, OrderList? queue = null)
    {
        if (CanBake(c) is { } why)
        {
            LastRefusal = why;
            return null;
        }
        var parts = new List<Placed>(c.PartCount);
        var plates = new List<Placed>();
        float health = 0, full = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            Place(c, i, out var at);
            var d = c.DefOf(i);
            if (d.ModPart == ModPart.Socket)
            {
                plates.Add(at);
                continue;
            }
            parts.Add(at);
            health += c.Parts[i].Health;
            full += d.Health;
        }
        var group = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[parts.Count];
        for (var k = 0; k < parts.Count; k++)
        {
            ref var p = ref c.Parts[parts[k].Part];
            group[k] = (c.DefOf(parts[k].Part), parts[k].Pos, parts[k].Rot, p.Paint, ConstructFinish.Name(p.Finish));
        }
        var text = DesignText(group, out var origin);
        // A bake kit's design (BakeKits.cs) comes out as the part it is the kit of, turned as the kit was. A baked subpart is read as its own
        // subparts for this (a split kit's baked sub-assemblies: BakeKits.Subs.cs), so a split kit matches its whole kit's design.
        var (matchText, matchOrigin) = Flat(group) is { } flat ? (DesignText(flat, out var fo), fo) : (text, origin);
        var matchHash = HashOf(matchText);
        if (BakeKits.Target(matchHash, want, queue) is { } kit) // (want: the oven's order, when two parts share a kit; queue: a rule kit is matched by the oven's orders)
            return Swap(world, c, kit.Part, matchOrigin + kit.Offset, kit.Rot, health / Math.Max(full, 1e-3f), plates, parts.Count);
        // A sub-kit's design at any turn comes out as its own baked part, its canonical design turned (the split kit's station wants that key).
        if (BakeKits.SubTarget(matchHash, want, queue) is { } sub && BakeKits.SubOf(sub.Part) is { } sk)
        {
            world.KeepBaked(sk.Hash);
            return Swap(world, c, sub.Part, matchOrigin + sub.Offset, sub.Rot, health / Math.Max(full, 1e-3f), plates, parts.Count);
        }
        if (Environment.GetEnvironmentVariable("COS_FLATDBG") is { } dbg && matchText != text)
            File.AppendAllText(dbg, "FLAT want " + want + "\n" + matchText + "\n");
        if (parts.Count < 2)
        {
            LastRefusal = "one piece bakes only into a part it is the whole kit of, and no order of this oven's is that part";
            return null;
        }
        if (Register(text) is not { } hash)
            return null;
        if (Parse(KeyOf(hash)) is not { } def)
        {
            LastRefusal = $"the part couldn't be made ({LastRefusal})";
            return null;
        }
        world.KeepBaked(hash);
        if (world.Create(c.Pos, c.Rot) is not { } nb)
        {
            LastRefusal = "the world is full";
            return null;
        }
        var bi = world.AddPart(nb, def, origin, 0);
        if (bi < 0)
        {
            world.Remove(nb, quiet: true);
            LastRefusal = "the world is full";
            return null;
        }
        nb.Parts[bi].Health = Math.Clamp(def.Health * (full > 0 ? health / full : 1f), 1f, def.Health);
        foreach (var at in plates)
            Copy(world, c, at.Part, nb, at.Pos, at.Rot);
        Settle(world, c, nb);
        world.Events.Add($"construct {c.Id}: baked {parts.Count} subparts into {def.Name} -> construct {nb.Id}");
        return nb;
    }

    /// <summary>
    /// A group with its baked subparts read as their designs' subparts, each placed and turned where the baked part puts it (null: the group
    /// holds no baked part whose design is known). A baked part's design places its subparts from its origin, which is the part's.
    /// </summary>
    private static List<(ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)>? Flat(IReadOnlyList<(ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)> group)
    {
        var any = false;
        foreach (var g in group)
            any |= IsBaked(g.Def) && DesignOf(g.Def) != null;
        if (!any)
            return null;
        var flat = new List<(ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)>(group.Count * 4);
        foreach (var g in group)
        {
            if (!IsBaked(g.Def) || DesignOf(g.Def) is not { } design)
            {
                flat.Add(g);
                continue;
            }
            foreach (var sub in design.Subs)
            {
                if (ConstructParts.Find(sub.Name) is not { } sd)
                    return null;
                flat.Add((sd, g.Pos + Rot24.Apply(g.Rot, sub.Pos), Rot24.Compose(g.Rot, sub.Rot), sub.Paint, sub.Finish));
            }
        }
        return flat;
    }

    /// <summary>Server: un-bakes body <paramref name="c"/>: a new body of the design's subparts (and the plates) where it stood. Null: it can't (<see cref="LastRefusal"/>).</summary>
    public static Construct? Unbake(ConstructWorld world, Construct c)
    {
        if (CanUnbake(c, out var baked) is { } why)
        {
            LastRefusal = why;
            return null;
        }
        var bd = c.DefOf(baked);
        var design = DesignOf(bd)!;
        Place(c, baked, out var bat);
        var share = Math.Clamp(c.Parts[baked].Health / MathF.Max(bd.Health, 1f), 0f, 1f);
        var defs = new ConstructPartDef[design.Subs.Length];
        for (var k = 0; k < defs.Length; k++)
            if (ConstructParts.Find(design.Subs[k].Name) is { } sd)
                defs[k] = sd;
            else
            {
                LastRefusal = $"no part \"{design.Subs[k].Name}\" in this build";
                return null;
            }
        if (world.Create(c.Pos, c.Rot) is not { } nb)
        {
            LastRefusal = "the world is full";
            return null;
        }
        var n = 0;
        for (var k = 0; k < defs.Length; k++)
        {
            var s = design.Subs[k];
            var i = world.AddPart(nb, defs[k], bat.Pos + Rot24.Apply(bat.Rot, s.Pos), Rot24.Compose(bat.Rot, s.Rot));
            if (i < 0)
                continue;
            n++;
            ref var np = ref nb.Parts[i];
            np.Health = MathF.Max(1f, defs[k].Health * share);
            np.Paint = s.Paint;
            np.Finish = ConstructFinish.Of(s.Finish);
        }
        if (n == 0)
        {
            world.Remove(nb, quiet: true);
            LastRefusal = "the world is full";
            return null;
        }
        for (var i = 0; i < c.PartCount; i++)
            if (i != baked && Place(c, i, out var at))
                Copy(world, c, i, nb, at.Pos, at.Rot);
        Settle(world, c, nb);
        world.Events.Add($"construct {c.Id}: un-baked {bd.Name} into {n} subparts -> construct {nb.Id}");
        return nb;
    }

    /// <summary>A part of the old body (a socket plate) into the new one, with its own state.</summary>
    private static void Copy(ConstructWorld world, Construct from, int part, Construct to, Vector3 pos, byte rot)
    {
        ref var p = ref from.Parts[part];
        var i = world.AddPart(to, from.DefOf(part), pos, rot);
        if (i < 0)
        {
            Console.WriteLine($"[bake] construct {from.Id}: part {part} ({from.DefOf(part).Name}) couldn't come along: the world is full");
            return;
        }
        ref var q = ref to.Parts[i];
        q.Health = p.Health;
        q.Paint = p.Paint;
        q.Finish = p.Finish;
        q.Cfg = p.Cfg;
        q.Setting = p.Setting;
        q.State = p.State;
    }

    /// <summary>The new body takes the old one's place: its owner, motion and anchoring; the old one goes.</summary>
    private static void Settle(ConstructWorld world, Construct old, Construct nb)
    {
        nb.Owner = old.Owner;
        nb.Pinned = old.Pinned;
        nb.Vel = old.Vel;
        nb.AngVel = old.AngVel;
        world.Remove(old);
        nb.Rebuild();
        nb.Wake();
    }

    // ---- The world's store: the wire and the save ---------------------------------------------------------------------------

    /// <summary>One design: u8 n = 1 (more may follow in later protocols), u32 bytes, the text (CBodyKind.Baked).</summary>
    public static void Write(PacketWriter w, Design d)
    {
        var bytes = Encoding.UTF8.GetBytes(d.Text);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Baked).U32((uint)bytes.Length).Bytes(bytes);
    }

    /// <summary>Client: a design off the wire, checked by its hash on this side, into the shared table and the world's list.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var n = r.U32();
        if (n > MaxText)
            throw new MalformedPacketException($"a baked design of {n} bytes");
        var text = Encoding.UTF8.GetString(r.Take((int)n));
        if (Register(text) is { } hash)
            world.KeepBaked(hash);
        else
            Console.WriteLine($"[bake] a design from the host was refused: {LastRefusal}");
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
                world.KeepBaked(hash);
            else
                Console.WriteLine($"[bake] REFUSED a saved design: {LastRefusal}");
        }
    }

    /// <summary>
    /// Keeps in <paramref name="world"/> every design its bodies and Storage Volumes hold, however they came (a blueprint, a world read
    /// in, a body carried from another world): run before a save and before a joiner's designs go, so neither misses one. Not per tick.
    /// </summary>
    public static void NoteInUse(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            for (var i = 0; i < c.PartCount; i++)
                Note(world, c.DefOf(i));
        foreach (var st in world.Stores)
            if (st?.KindDef is { } kinds)
                for (var k = 0; k < st.KindN; k++)
                    if (kinds[k] >= Extrusion.RuntimeBase && Extrusion.Get(kinds[k]) is { } d)
                        Note(world, d);
    }

    /// <summary>Keeps one baked part's design in <paramref name="world"/> (ConstructWorld.AddPart).</summary>
    public static void NoteInUse(ConstructWorld world, ConstructPartDef d) => Note(world, d);

    private static void Note(ConstructWorld world, ConstructPartDef d)
    {
        if (d.IsNamedRuntime && IsName(d.Name) && DesignOf(d) is { } design)
            world.KeepBaked(design.Hash);
    }

    /// <summary>The world's designs as text, for its save.</summary>
    public static string[] TextsOf(ConstructWorld world)
    {
        var list = new string[world.Baked.Count];
        lock (Interned)
            for (var k = 0; k < list.Length; k++)
                list[k] = Known.TryGetValue(world.Baked[k], out var d) ? d.Text : "";
        return Array.FindAll(list, t => t.Length > 0);
    }
}

/// <summary>
/// <c>--bake-check [PART;PART...]</c>: bakes a group headless, in a world of its own, and checks the round trip (BakedParts.cs): the baked
/// part's boxes, mass and health against its subparts', every shaped box's mesh where the subpart drew it, the design through the wire and
/// a save, un-baking back to the same parts, and a second bake of those to the same hash. A group's parts are laid out in a row, each at its
/// own turn. Exit code: the failures.
/// </summary>
public static class BakeCheck
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bake-check", StringComparison.OrdinalIgnoreCase));
        var list = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "Frame Beam;Snub Hood;Armour Slope 1 m;Bolt Deck 1 m;Wall Bay Socket 1m";
        var names = list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fails = 0;
        void Fail(string why)
        {
            fails++;
            Console.WriteLine("FAIL " + why);
        }
        var world = new ConstructWorld();
        var c = world.Create(new Vector3(0, 10, 0), Quaternion.Identity)!;
        var x = 0f;
        byte turn = 0;
        foreach (var n in names)
        {
            if (ConstructParts.Find(n) is not { } d)
            {
                Fail($"no part \"{n}\"");
                continue;
            }
            var size = d.BoundsMax - d.BoundsMin;
            var i = world.AddPart(c, d, new Vector3(x + MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * 0.5f, 0, 0), turn);
            c.Parts[i].Paint = (turn & 1) == 0 ? 0 : ConstructPaint.Rgb(200, 40, 30);
            x += MathF.Max(size.X, MathF.Max(size.Y, size.Z)) + 0.05f;
            turn = (byte)((turn + 7) % Rot24.Count);
        }
        c.Rebuild();
        var before = Parts(c);
        Console.WriteLine($"group: {c.PartCount} parts; can bake: {BakedParts.CanBake(c) ?? "yes"}");
        // The meshes where each subpart drew them (body frame), to compare with the baked part's.
        var drawn = Meshes(c);
        if (BakedParts.Bake(world, c) is not { } baked)
        {
            Fail($"bake refused: {BakedParts.LastRefusal}");
            return fails;
        }
        var bi = Array.FindIndex(baked.Parts, 0, baked.PartCount, p => BakedParts.IsBaked(ConstructParts.Def(p.Def)));
        var bd = baked.DefOf(bi);
        var design = BakedParts.DesignOf(bd)!;
        Console.WriteLine($"baked: {bd.Name}: {bd.Boxes.Length} boxes ({bd.SolidBoxes} solid), {bd.Sockets.Length} sockets, {bd.Mass:0.#} kg, {bd.Health:0} hp; body {baked.Id} of {baked.PartCount} parts");
        Console.Write(design.Text);
        float mass = 0, hp = 0;
        var boxes = 0;
        foreach (var s in design.Subs)
        {
            var sd = ConstructParts.Find(s.Name)!;
            (mass, hp, boxes) = (mass + sd.Mass, hp + sd.Health, boxes + sd.Boxes.Length);
        }
        if (MathF.Abs(mass - bd.Mass) > 0.01f * mass || MathF.Abs(hp - bd.Health) > 0.01f * hp || boxes != bd.Boxes.Length)
            Fail($"sums: {mass} kg {hp} hp {boxes} boxes in the subparts, {bd.Mass} {bd.Health} {bd.Boxes.Length} in the part");
        var after = Meshes(baked);
        if (drawn.Count != after.Count)
            Fail($"{drawn.Count} drawn boxes before, {after.Count} after");
        else
        {
            drawn.Sort(Cmp);
            after.Sort(Cmp);
            var worst = 0f;
            for (var k = 0; k < drawn.Count; k++)
                worst = MathF.Max(worst, Vector3.Distance(drawn[k], after[k]));
            Console.WriteLine($"meshes: {drawn.Count} points, the farthest {worst * 1000:0.0} mm from where its subpart drew it");
            if (worst > 0.002f)
                Fail("a baked box isn't drawn where its subpart was");
        }
        // The design through the wire and a save.
        var pw = new PacketWriter(256);
        BakedParts.Write(pw, design);
        var r = new PacketReader(pw.Span[2..]);
        var other = new ConstructWorld();
        BakedParts.Read(ref r, other);
        if (other.Baked.Count != 1 || other.Baked[0] != design.Hash)
            Fail("the wire's copy doesn't match");
        var sw = new SaveWriter(1024);
        BakedParts.WriteSave(sw, BakedParts.TextsOf(world));
        var third = new ConstructWorld();
        BakedParts.ReadSave(new SaveReader(sw.Span.ToArray()), third);
        if (third.Baked.Count != 1 || third.Baked[0] != design.Hash)
            Fail("the save's copy doesn't match");
        // Un-bake, then bake again.
        if (BakedParts.Unbake(world, baked) is not { } back)
        {
            Fail($"un-bake refused: {BakedParts.LastRefusal}");
            return fails;
        }
        var again = Parts(back);
        if (!before.SequenceEqual(again))
        {
            Fail("un-baked parts differ:");
            foreach (var l in before.Except(again))
                Console.WriteLine("  before " + l);
            foreach (var l in again.Except(before))
                Console.WriteLine("  after  " + l);
        }
        if (BakedParts.Bake(world, back) is not { } twice)
            Fail($"second bake refused: {BakedParts.LastRefusal}");
        else if (Array.FindIndex(twice.Parts, 0, twice.PartCount, p => p.Def == bd.Id) < 0)
            Fail("the second bake made another design");
        Console.WriteLine(fails == 0 ? "bake check: passed" : $"bake check: {fails} failure(s)");
        return fails;
    }

    private static int Cmp(Vector3 a, Vector3 b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);

    /// <summary>Every part, its place relative to the first's, turn and paint, sorted.</summary>
    private static List<string> Parts(Construct c)
    {
        var o = c.Parts[0].Pos;
        var l = new List<string>();
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            l.Add($"{c.DefOf(i).Name} {p.Pos.X:0.00},{p.Pos.Y:0.00},{p.Pos.Z:0.00} turn {p.Rot} paint {p.Paint:x}");
        }
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    /// <summary>The corners of every box's mesh (a plain box's unit cube) as drawn, in the body's frame.</summary>
    private static List<Vector3> Meshes(Construct c)
    {
        var pts = new List<Vector3>();
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = c.DefOf(i);
            if (d.ModPart == ModPart.Socket)
                continue;
            foreach (var b in d.Boxes)
            {
                if (b.Hidden)
                    continue;
                var m = b.Model(p.Rot, p.Pos + Rot24.Apply(p.Rot, b.Center));
                for (var k = 0; k < 8; k++)
                {
                    var v = Vector3.Transform(new Vector3((k & 1) - 0.5f, (k >> 1 & 1) - 0.5f, (k >> 2 & 1) - 0.5f), m);
                    pts.Add(new Vector3(MathF.Round(v.X * 1e4f), MathF.Round(v.Y * 1e4f), MathF.Round(v.Z * 1e4f)) / 1e4f);
                }
            }
        }
        return pts;
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The designs this world's ovens baked or its save brought, by hash (BakedParts.cs): saved with it, sent to every joiner.</summary>
    public readonly List<string> Baked = new(8);
    private readonly HashSet<string> _bakedSet = new(StringComparer.Ordinal);

    /// <summary>Server: designs new this tick, to everyone at the next flush (before the bodies that name them).</summary>
    public readonly List<string> BakedNew = new(4);

    /// <summary>Keeps a design in this world (once).</summary>
    public void KeepBaked(string hash)
    {
        if (_bakedSet.Count >= BakedParts.MaxWorld || !_bakedSet.Add(hash))
            return;
        Baked.Add(hash);
        BakedNew.Add(hash);
    }

    internal void ClearBaked()
    {
        Baked.Clear();
        _bakedSet.Clear();
        BakedNew.Clear();
    }
}
