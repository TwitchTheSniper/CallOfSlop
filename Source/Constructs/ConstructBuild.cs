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
        var onLeaf = target.HasJoint && socket == target.LeafIndex; // what goes on a joint's leaf hangs on a new joint
        if (ts.Kind != ds.Kind && !(onLeaf && ds.Kind == SocketKind.Leaf))
            return "those don't fit together";
        if (c.SocketUsed(part, socket))
            return "something is already attached there";
        if (def.HasJoint && defSocket == def.LeafIndex)
            return $"a {def.Name} goes on by its base (its {def.LeafSocket} is what moves)";
        if (ts.Kind == SocketKind.Leaf && !((target.Role == PartRole.Hinge && def.Role == PartRole.Door) || (target.Role == PartRole.Door && def.Role == PartRole.Hinge)))
            return "doors hang on hinges";
        if (ds.Kind == SocketKind.Leaf && !onLeaf)
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
        var newJoint = onLeaf;
        if (newJoint && c.JointCount >= Construct.MaxJoints)
            return $"at most {Construct.MaxJoints} joints a construct";
        if (newJoint && c.JointOf(part) >= 0)
            return "something already hangs on that joint";
        g = new ConstructGhost { Part = part, Socket = socket, DefSocket = defSocket, Variant = spin, Pos = pos, Rot = rot, Seg = seg, NewJoint = newJoint };
        if (Blocked(c, def, pos, rot))
            return "something is in the way";
        return null;
    }

    private static float Upright(byte r) => Rot24.Apply(r, Vector3.UnitY).Y * 2 + Rot24.Apply(r, -Vector3.UnitZ).Z * -1 + Rot24.Apply(r, Vector3.UnitX).X * 0.1f;

    /// <summary>Would a part there overlap one already on (all compared in the rest frame, a centimetre of slack)?</summary>
    public static bool Blocked(Construct c, ConstructPartDef def, Vector3 pos, byte rot)
    {
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half) - new Vector3(0.01f);
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                for (var k = 0; k < d.SolidBoxes; k++)
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
            if (c.SocketUsed(i, s) || d.Sockets[s].Kind != SocketKind.Struct || (d.HasJoint && s == d.LeafIndex))
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
                    if (od.Sockets[t].Kind != SocketKind.Struct || c.SocketUsed(j, t) || (od.HasJoint && t == od.LeafIndex)) // (a joint's leaf only takes what hangs on it)
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

    public const float WireReach = 8f; // the longest wire (between the two parts' centres)

    /// <summary>
    /// Can part <paramref name="from"/>'s output be wired into part <paramref name="to"/>? Null when it can, else the reason
    /// (the linking tool shows it): wires join an output (a seat, a control, a sensor, a gate) to an input (a joint, the
    /// engine, a gate, a lamp...) of the same construct; a part can be wired to itself only if it is logic (a NOT on
    /// itself is a clock); an input reader reads a seat.
    /// </summary>
    public static string? CanWire(ConstructWorld world, Construct c, int from, int to)
    {
        if (from < 0 || from >= c.PartCount || to < 0 || to >= c.PartCount)
            return "no such part";
        var fd = c.DefOf(from);
        var td = c.DefOf(to);
        if (c.Parts[from].Broken || c.Parts[to].Broken)
            return "that part is wrecked";
        if (!fd.HasOutput)
            return $"a {fd.Name} has no output (wire from a seat, a control, a sensor or a gate)";
        if (td.MaxInputs == 0)
            return $"a {td.Name} takes no input";
        if (from == to && td.Role != PartRole.Logic)
            return "a part can't be wired to itself";
        if (td.Logic == CLogic.Reader && fd.Role != PartRole.Seat)
            return "an input reader reads a seat: wire it from one";
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Data && c.Routes[k].Source == from && c.Routes[k].To == to)
                return "those are already wired";
        if (c.RoutesInto(to, RouteType.Data) >= td.MaxInputs)
            return $"a {td.Name} takes at most {td.MaxInputs} input{(td.MaxInputs == 1 ? "" : "s")}";
        c.PartBounds(from, out var a0, out var a1);
        c.PartBounds(to, out var b0, out var b1);
        c.SegPose(c.Parts[from].Seg, out var qa, out var ta);
        c.SegPose(c.Parts[to].Seg, out var qb, out var tb);
        if (Vector3.Distance(ta + Vector3.Transform((a0 + a1) * 0.5f, qa), tb + Vector3.Transform((b0 + b1) * 0.5f, qb)) > WireReach)
            return $"a wire reaches at most {WireReach:0} m";
        if (c.RouteCount >= Construct.MaxRoutes)
            return $"at most {Construct.MaxRoutes} wires a construct";
        if (world.TotalRoutes >= ConstructWorld.MaxTotalWires)
            return "the world has all the wires it can take";
        return null;
    }

    /// <summary>A new construct of one part standing on the ground at <paramref name="ground"/>, facing <paramref name="yaw"/>.</summary>
    public static Construct? PlaceNew(ConstructWorld world, ConstructPartDef def, Vector3 ground, float yaw)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw); // our yaw turns -Z towards +X
        return PlaceNew(world, def, new FreePlacement { Pos = ground - Vector3.Transform(new Vector3(0, def.BoundsMin.Y - 0.01f, 0), rot), Rot = rot });
    }

    /// <summary>A new construct of one part as free placement put it (ConstructPlace.Pose): its root, with its own sockets to build on.</summary>
    public static Construct? PlaceNew(ConstructWorld world, ConstructPartDef def, in FreePlacement fp)
    {
        var c = world.Create(fp.Pos, fp.Rot);
        if (c == null)
            return null;
        if (world.AddPart(c, def, Vector3.Zero, (byte)(fp.Turn % Rot24.Count)) < 0)
        {
            world.Remove(c);
            return null;
        }
        c.Pinned = fp.Anchor;
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
        public List<int> StepSettings = new(); // each step's logic setting (-1: the part's default)
        public List<(int From, int To)> Wires = new(); // wires between steps' parts (milestone 2): straight data links since the routed links
        public List<ConstructRoutes.LinkSpec> Links = new(); // routed links between steps' parts (decision 20)
    }

    private static readonly List<string> _refusedBlueprints = new();
    private static readonly Lazy<List<Blueprint>> _blueprints = new(() => LoadBlueprints(BlueprintFolder, _refusedBlueprints), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string BlueprintFolder => Path.Combine(AppFolders.Data, "Constructs", "Blueprints");

    public static IReadOnlyList<Blueprint> Blueprints => _blueprints.Value;

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
                        int on = -1, spin = 0, setting = -1;
                        foreach (var f in s.EnumerateObject())
                            switch (f.Name)
                            {
                                case "part": part = f.Value.GetString() ?? ""; break;
                                case "on": on = f.Value.GetInt32(); break;
                                case "socket": sock = f.Value.GetString() ?? ""; break;
                                case "by": own = f.Value.GetString() ?? ""; break;
                                case "spin": spin = f.Value.GetInt32(); break;
                                case "setting": setting = f.Value.GetInt32(); break;
                                default: throw new ConstructPartException($"{file}: steps[{k}]: unknown key \"{f.Name}\" (part, on, socket, by, spin, setting)");
                            }
                        if (k > 0 && (on < 0 || on >= k))
                            throw new ConstructPartException($"{file}: steps[{k}]: \"on\" must name an earlier step (0 to {k - 1})");
                        bp.Steps.Add((part, k == 0 ? -1 : on, sock, own, spin));
                        bp.StepSettings.Add(setting);
                        k++;
                    }
                    break;
                case "wires":
                    foreach (var wire in p.Value.EnumerateArray())
                    {
                        if (wire.ValueKind != JsonValueKind.Array || wire.GetArrayLength() != 2)
                            throw new ConstructPartException($"{file}: a wire is [from step, to step]");
                        bp.Wires.Add((wire[0].GetInt32(), wire[1].GetInt32()));
                    }
                    break;
                case "links":
                    foreach (var link in p.Value.EnumerateArray())
                        bp.Links.Add(ConstructRoutes.ParseLink(link, file, bp.Links.Count));
                    break;
                default:
                    throw new ConstructPartException($"{file}: unknown key \"{p.Name}\" (format, name, description, steps, wires, links)");
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
                SetSetting(c, partOf[0], def, bp.StepSettings[0]);
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
            if (!SetSetting(c, partOf[k], def, bp.StepSettings[k]))
            {
                why = $"step {k}: a {def.Name} has no setting {bp.StepSettings[k]}";
                world.Remove(c);
                return null;
            }
        }
        // The wires, through the linking tool's rules.
        foreach (var (from, to) in bp.Wires)
        {
            if (from < 0 || from >= bp.Steps.Count || to < 0 || to >= bp.Steps.Count)
            {
                why = $"wire {from} -> {to}: no such step";
                world.Remove(c);
                return null;
            }
            if (CanWire(world, c, partOf[from], partOf[to]) is { } bad)
            {
                why = $"wire {from} -> {to} ({c.DefOf(partOf[from]).Name} to {c.DefOf(partOf[to]).Name}): {bad}";
                world.Remove(c);
                return null;
            }
            world.AddWire(c, partOf[from], partOf[to]);
        }
        // The routed links, through the linking tool's rules.
        for (var k = 0; k < bp.Links.Count; k++)
            if (ConstructRoutes.BuildLink(world, c, bp.Links[k], partOf[..bp.Steps.Count]) is { } bad)
            {
                why = $"links[{k}]: {bad}";
                world.Remove(c);
                return null;
            }
        // Fuel line parts and plain drive shafts become pipe and shaft links (blueprints auto-converted).
        ConstructRoutes.MigrateLegacy(world, c);
        c.Rebuild();
        return c;
    }

    private static bool SetSetting(Construct c, int part, ConstructPartDef def, int setting)
    {
        if (setting < 0)
            return true;
        if (setting >= def.Settings.Length)
            return false;
        c.Parts[part].Setting = (byte)setting;
        return true;
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
