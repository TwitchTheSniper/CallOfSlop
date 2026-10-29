using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One place a held part can go: onto socket <see cref="Socket"/> of part <see cref="Part"/>, by its own socket <see cref="DefSocket"/>.</summary>
public struct ConstructGhost
{
    public int Part, Socket, DefSocket, Variant;
    public Vector3 Pos; // the new part's origin, rest frame
    public byte Rot, Seg;
    public bool NewJoint; // onto a hinge's leaf: the part hangs on a new joint
}

/// <summary>
/// The building rules, shared by the server (which checks every edit), the client (ghost sockets) and blueprints:
/// where a part can go (sockets that mate: matching kinds, facing each other, nothing in the way), which tool it needs,
/// and how long attaching and removing take. Blueprints (Constructs/Blueprints) are build scripts: each step attaches a
/// part to a socket exactly as a player does, so a blueprint that loads proves the rules build it.
/// </summary>
public static class ConstructBuild
{
    public const float Reach = 4.5f; // how far from the eye a part can be attached or removed
    public const float UseReach = 3.2f; // seats, doors, loose parts
    public const float RemoveTimeShare = 0.6f; // removing is quicker than fitting
    public const int MaxGhosts = 96;

    /// <summary>Seconds to attach a part (from its file).</summary>
    public static float AttachSeconds(ConstructPartDef d) => d.AttachSeconds;

    public static float RemoveSeconds(ConstructPartDef d) => d.AttachSeconds * RemoveTimeShare;

    /// <summary>
    /// Can <paramref name="tool"/> edit with part <paramref name="d"/>? Decision 2: editing always needs the right tool in
    /// hand (wrench for bolted parts, welder for welded ones); bare hands or a gun never edit.
    /// </summary>
    public static bool ToolFits(ConstructTool tool, ConstructPartDef d) => tool != ConstructTool.None && d.Tool == tool;

    private static byte Opposite(byte dir) => (byte)(dir ^ 1);

    /// <summary>
    /// Where <paramref name="def"/> goes when its socket <paramref name="defSocket"/> mates socket <paramref name="socket"/> of
    /// part <paramref name="part"/>, turned by <paramref name="spin"/> (0..3) about that socket. Null reason: it fits.
    /// </summary>
    public static string? Place(Construct c, int part, int socket, ConstructPartDef def, int defSocket, int spin, out ConstructGhost g)
    {
        g = default;
        if (part < 0 || part >= c.PartCount)
            return "no such part";
        var target = c.DefOf(part);
        if (socket < 0 || socket >= target.Sockets.Length || defSocket < 0 || defSocket >= def.Sockets.Length)
            return "no such socket";
        if (c.PartCount >= Construct.MaxParts)
            return $"a construct holds at most {Construct.MaxParts} parts";
        if (c.Parts[part].Broken)
            return "that part is wrecked";
        var ts = target.Sockets[socket];
        var ds = def.Sockets[defSocket];
        if (ts.Kind != ds.Kind)
            return "those don't fit together";
        if (c.SocketUsed(part, socket))
            return "something is already attached there";
        if (ts.Kind == SocketKind.Leaf && !((target.Role == PartRole.Hinge && def.Role == PartRole.Door) || (target.Role == PartRole.Door && def.Role == PartRole.Hinge)))
            return "doors hang on hinges";
        c.SocketAt(part, socket, out var at, out var dir);
        Span<byte> turns = stackalloc byte[4];
        var n = Rot24.Matching(ds.Dir, Opposite(dir), turns);
        if (n == 0)
            return "no turn fits";
        // A fixed order: upright first, then facing forward, so spin 0 is the natural way round.
        for (var a = 0; a < n; a++)
        for (var b = a + 1; b < n; b++)
            if (Upright(turns[b]) > Upright(turns[a]))
                (turns[a], turns[b]) = (turns[b], turns[a]);
        var rot = turns[((spin % n) + n) % n];
        var pos = ConstructWorld.Quantise(at - Rot24.Apply(rot, ds.At));
        var seg = c.Parts[part].Seg;
        var newJoint = target.Role == PartRole.Hinge && ts.Kind == SocketKind.Leaf;
        if (newJoint && c.JointCount >= Construct.MaxJoints)
            return $"at most {Construct.MaxJoints} joints a construct";
        g = new ConstructGhost { Part = part, Socket = socket, DefSocket = defSocket, Variant = spin, Pos = pos, Rot = rot, Seg = seg, NewJoint = newJoint };
        if (Blocked(c, def, pos, rot))
            return "something is in the way";
        return null;
    }

    private static float Upright(byte r) => Rot24.Apply(r, Vector3.UnitY).Y * 2 + Rot24.Apply(r, -Vector3.UnitZ).Z * -1 + Rot24.Apply(r, Vector3.UnitX).X * 0.1f;

    /// <summary>Would a part there overlap one already on (all compared in the rest frame, a centimetre of slack)?</summary>
    public static bool Blocked(Construct c, ConstructPartDef def, Vector3 pos, byte rot)
    {
        for (var b = 0; b < def.Boxes.Length; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half) - new Vector3(0.01f);
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                for (var k = 0; k < d.Boxes.Length; k++)
                {
                    c.BoxOf(i, k, out var oc, out var oh);
                    if (MathF.Abs(bc.X - oc.X) < bh.X + oh.X && MathF.Abs(bc.Y - oc.Y) < bh.Y + oh.Y && MathF.Abs(bc.Z - oc.Z) < bh.Z + oh.Z)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The ghost sockets for a held part on construct <paramref name="c"/>: one per free socket it can go on (the first of
    /// its own sockets and turns that fits), near <paramref name="near"/> (rest frame, within <paramref name="radius"/>).
    /// </summary>
    public static int Ghosts(Construct c, ConstructPartDef def, Span<ConstructGhost> into, Vector3 near, float radius)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount && n < into.Length; i++)
        {
            if (c.Parts[i].Broken)
                continue;
            var td = c.DefOf(i);
            for (var s = 0; s < td.Sockets.Length && n < into.Length; s++)
            {
                c.SocketAt(i, s, out var at, out _);
                if (Vector3.DistanceSquared(at, near) > radius * radius)
                    continue;
                if (c.SocketUsed(i, s))
                    continue;
                for (var v = 0; v < def.Sockets.Length * 4; v++)
                    if (Place(c, i, s, def, v / 4, v % 4, out var g) == null)
                    {
                        g.Variant = v;
                        into[n++] = g;
                        break;
                    }
            }
        }
        return n;
    }

    /// <summary>The next variant (its own socket and spin) that fits the same target socket after <paramref name="after"/>, or -1.</summary>
    public static int NextVariant(Construct c, ConstructPartDef def, int part, int socket, int after)
    {
        var total = def.Sockets.Length * 4;
        for (var k = 1; k <= total; k++)
        {
            var v = (after + k) % total;
            if (Place(c, part, socket, def, v / 4, v % 4, out _) == null)
                return v;
        }
        return -1;
    }

    /// <summary>
    /// Attaches <paramref name="def"/> as <paramref name="g"/> says (call <see cref="Place"/> first): the part, its link,
    /// a joint when it hangs on a hinge, and links to any other free sockets it lands on. Returns the new part's index or -1.
    /// </summary>
    public static int Attach(ConstructWorld world, Construct c, ConstructPartDef def, in ConstructGhost g)
    {
        var seg = g.Seg;
        if (g.NewJoint)
        {
            seg = world.AddJoint(c, g.Part);
            if (seg == 0)
                return -1;
        }
        var i = world.AddPart(c, def, g.Pos, g.Rot, seg);
        if (i < 0)
            return -1;
        world.AddLink(c, g.Part, g.Socket, i, g.DefSocket);
        AutoLink(world, c, i);
        return i;
    }

    /// <summary>Links part <paramref name="i"/>'s free sockets to free sockets of the same kind it touches, facing it (welds a grid together).</summary>
    public static void AutoLink(ConstructWorld world, Construct c, int i)
    {
        var d = c.DefOf(i);
        for (var s = 0; s < d.Sockets.Length; s++)
        {
            if (c.SocketUsed(i, s) || d.Sockets[s].Kind != SocketKind.Struct)
                continue;
            c.SocketAt(i, s, out var at, out var dir);
            for (var j = 0; j < c.PartCount; j++)
            {
                if (j == i || c.Parts[j].Seg != c.Parts[i].Seg)
                    continue;
                var od = c.DefOf(j);
                var done = false;
                for (var t = 0; t < od.Sockets.Length && !done; t++)
                {
                    if (od.Sockets[t].Kind != SocketKind.Struct || c.SocketUsed(j, t))
                        continue;
                    c.SocketAt(j, t, out var oat, out var odir);
                    if (odir == Opposite(dir) && Vector3.DistanceSquared(oat, at) < 0.0004f)
                    {
                        world.AddLink(c, i, s, j, t);
                        done = true;
                    }
                }
                if (done)
                    break;
            }
        }
    }

    /// <summary>A new construct of one part standing on the ground at <paramref name="ground"/>, facing <paramref name="yaw"/>.</summary>
    public static Construct? PlaceNew(ConstructWorld world, ConstructPartDef def, Vector3 ground, float yaw)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw); // our yaw turns -Z towards +X
        var c = world.Create(ground - Vector3.Transform(new Vector3(0, def.BoundsMin.Y - 0.01f, 0), rot), rot);
        if (c == null)
            return null;
        if (world.AddPart(c, def, Vector3.Zero, 0) < 0)
        {
            world.Remove(c);
            return null;
        }
        c.Rebuild();
        return c;
    }

    // ---- Blueprints ------------------------------------------------------------------------------------------------

    public const string BlueprintFormat = "call-of-slop-construct-blueprint/1";

    /// <summary>A blueprint: a name and its build steps (part, then which part's socket it goes on, by which of its own sockets, turned how).</summary>
    public sealed class Blueprint
    {
        public string Name = "", File = "", Description = "";
        public List<(string Part, int On, string Socket, string Own, int Spin)> Steps = new();
    }

    private static List<Blueprint>? _blueprints;
    private static readonly List<string> _refusedBlueprints = new();

    public static string BlueprintFolder => Path.Combine(AppFolders.Data, "Constructs", "Blueprints");

    public static IReadOnlyList<Blueprint> Blueprints => _blueprints ??= LoadBlueprints(BlueprintFolder, _refusedBlueprints);

    public static IReadOnlyList<string> RefusedBlueprints
    {
        get
        {
            _ = Blueprints;
            return _refusedBlueprints;
        }
    }

    public static Blueprint? FindBlueprint(string name) => Blueprints.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static List<Blueprint> LoadBlueprints(string folder, List<string> refused)
    {
        var list = new List<Blueprint>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            try
            {
                var bp = ParseBlueprint(File.ReadAllText(file), Path.GetFileName(file));
                // Build it once on a scratch world: every step must fit.
                var scratch = new ConstructWorld();
                if (Build(scratch, bp, Vector3.Zero, Quaternion.Identity, out var why) == null)
                    throw new ConstructPartException($"{bp.File}: {why}");
                list.Add(bp);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                refused.Add(ex is ConstructPartException ? ex.Message : $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list;
    }

    public static Blueprint ParseBlueprint(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var bp = new Blueprint { File = file };
        foreach (var p in root.EnumerateObject())
            switch (p.Name)
            {
                case "format":
                    if (p.Value.GetString() != BlueprintFormat)
                        throw new ConstructPartException($"{file}: \"format\" must be \"{BlueprintFormat}\"");
                    break;
                case "name":
                    bp.Name = p.Value.GetString() ?? "";
                    break;
                case "description":
                    bp.Description = p.Value.GetString() ?? "";
                    break;
                case "steps":
                    var k = 0;
                    foreach (var s in p.Value.EnumerateArray())
                    {
                        string part = "", sock = "", own = "";
                        int on = -1, spin = 0;
                        foreach (var f in s.EnumerateObject())
                            switch (f.Name)
                            {
                                case "part": part = f.Value.GetString() ?? ""; break;
                                case "on": on = f.Value.GetInt32(); break;
                                case "socket": sock = f.Value.GetString() ?? ""; break;
                                case "by": own = f.Value.GetString() ?? ""; break;
                                case "spin": spin = f.Value.GetInt32(); break;
                                default: throw new ConstructPartException($"{file}: steps[{k}]: unknown key \"{f.Name}\" (part, on, socket, by, spin)");
                            }
                        if (k > 0 && (on < 0 || on >= k))
                            throw new ConstructPartException($"{file}: steps[{k}]: \"on\" must name an earlier step (0 to {k - 1})");
                        bp.Steps.Add((part, k == 0 ? -1 : on, sock, own, spin));
                        k++;
                    }
                    break;
                default:
                    throw new ConstructPartException($"{file}: unknown key \"{p.Name}\" (format, name, description, steps)");
            }
        if (bp.Name.Length == 0 || bp.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be plain ASCII text");
        if (bp.Steps.Count == 0)
            throw new ConstructPartException($"{file}: \"steps\" is empty");
        return bp;
    }

    /// <summary>Builds a blueprint as a new construct at a pose, through the same rules as a player (null and the reason on a step that doesn't fit).</summary>
    public static Construct? Build(ConstructWorld world, Blueprint bp, Vector3 pos, Quaternion rot, out string why)
    {
        why = "";
        var c = world.Create(pos, rot);
        if (c == null)
        {
            why = "too many constructs";
            return null;
        }
        Span<int> partOf = stackalloc int[Math.Max(1, bp.Steps.Count)];
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var (name, on, sock, own, spin) = bp.Steps[k];
            if (ConstructParts.Find(name) is not { } def)
            {
                why = $"step {k}: no part named \"{name}\"";
                world.Remove(c);
                return null;
            }
            if (k == 0)
            {
                partOf[0] = world.AddPart(c, def, Vector3.Zero, (byte)(spin % Rot24.Count));
                continue;
            }
            var target = partOf[on];
            var td = c.DefOf(target);
            var ts = Array.FindIndex(td.Sockets, s => s.Name == sock);
            var ds = Array.FindIndex(def.Sockets, s => s.Name == own);
            if (ts < 0 || ds < 0)
            {
                why = $"step {k}: {(ts < 0 ? $"{td.Name} has no socket \"{sock}\"" : $"{def.Name} has no socket \"{own}\"")}";
                world.Remove(c);
                return null;
            }
            if (Place(c, target, ts, def, ds, spin, out var g) is { } bad)
            {
                why = $"step {k}: {def.Name} on {td.Name} \"{sock}\": {bad}";
                world.Remove(c);
                return null;
            }
            partOf[k] = Attach(world, c, def, g);
            if (partOf[k] < 0)
            {
                why = $"step {k}: too many parts";
                world.Remove(c);
                return null;
            }
        }
        c.Rebuild();
        return c;
    }

    /// <summary>A blueprint's construct set down standing on the ground at <paramref name="ground"/> (its lowest point there).</summary>
    public static Construct? BuildOnGround(ConstructWorld world, Blueprint bp, Vector3 ground, float yaw, out string why)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
        var c = Build(world, bp, ground, rot, out why);
        if (c == null)
            return null;
        // Lowest point: the parts' bounds, or the wheels' bottoms.
        var low = c.LocalMin.Y;
        for (var k = 0; k < c.WheelCount; k++)
            low = MathF.Min(low, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        c.Pos = ground - Vector3.Transform(new Vector3(0, low - 0.02f, 0), rot);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        return c;
    }
}
