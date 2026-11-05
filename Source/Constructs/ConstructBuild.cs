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
    public byte Scale; // a gun part on a merged receiver's centre socket (or on a scaled gun part): drawn and simulated this many times bigger (0 or 1: as its file)
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
    public const string InTheWay = "something is in the way";

    /// <summary>Seconds to attach a part (from its file).</summary>
    public static float AttachSeconds(ConstructPartDef d) => d.AttachSeconds;

    public static float RemoveSeconds(ConstructPartDef d) => d.AttachSeconds * RemoveTimeShare;

    /// <summary>
    /// Can <paramref name="tool"/> edit with part <paramref name="d"/>? Decision 2: editing always needs the right tool in
    /// hand; bare hands or a gun never edit. The attach tool (the welder in code) fits and takes off every part: physics-only
    /// pieces by its three-step socket weld, items from the hands onto a socket (<see cref="ToolFor"/>).
    /// </summary>
    public static bool ToolFits(ConstructTool tool, ConstructPartDef d) => tool != ConstructTool.None && tool == ToolFor(d);

    /// <summary>The tool a part goes on and comes off with: the attach tool, since the wrench's work moved to it.</summary>
    public static ConstructTool ToolFor(ConstructPartDef d) => ConstructTool.Welder;

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
        if (socket < 0 || socket >= c.SocketCount(part) || defSocket < 0 || defSocket >= def.Sockets.Length)
            return "no such socket";
        if (c.PartCount >= Construct.MaxParts)
            return $"a construct holds at most {Construct.MaxParts} parts";
        if (c.Parts[part].Broken)
            return "that part is wrecked";
        if (ConstructCargo.IsVolume(def) && !def.IsDock && ConstructCargo.CountVolumes(c) >= ConstructCargo.MaxVolumes)
            return $"a construct has at most {ConstructCargo.MaxVolumes} cargo volumes";
        if (def.IsVolumeStore && ConstructStoreVolume.Count(c) >= ConstructStoreVolume.MaxPerConstruct)
            return $"a construct has at most {ConstructStoreVolume.MaxPerConstruct} Storage Volumes";
        if (def.IsTankVolume && ConstructFluid.VolumeCount(c) >= ConstructFluid.MaxVolumes)
            return $"a construct has at most {ConstructFluid.MaxVolumes} Tank Volumes";
        var centre = socket >= target.Sockets.Length; // a merged receiver's centre socket (ConstructMerge.cs)
        var ts = target.Sockets[socket % target.Sockets.Length];
        var ds = def.Sockets[defSocket];
        if (centre && def.Role != PartRole.GunPart)
            return "the merged receiver's centre sockets take gun parts";
        if (!centre && c.MergeOf[part] != 0 && def.Role == PartRole.GunPart)
            return "that receiver is merged: fit gun parts to the merged receiver's centre sockets";
        var onLeaf = target.HasJoint && socket == target.LeafIndex; // what goes on a joint's leaf hangs on a new joint
        if (!KindsMate(ts.Kind, ds.Kind, onLeaf) || ts.Kind == SocketKind.Port)
            return "those don't fit together";
        if (ts.Class != ds.Class)
            return $"that's a {ConstructParts.ClassNames[ts.Class]} socket and this a {ConstructParts.ClassNames[ds.Class]} part: use an adapter";
        if (c.SocketUsed(part, socket))
            return "something is already attached there";
        if (def.HasJoint && defSocket == def.LeafIndex)
            return $"a {def.Name} goes on by its base (its {def.LeafSocket} is what moves)";
        if (ts.Kind == SocketKind.Leaf && !onLeaf && !(target.Role == PartRole.Door && def.Role == PartRole.Hinge))
            return "doors hang on hinges";
        if (ds.Kind == SocketKind.Leaf && !onLeaf)
            return "doors hang on hinges";
        c.SocketAt(part, socket, out var at, out var dir);
        Span<byte> turns = stackalloc byte[4];
        var n = SpinTurns(ds.Dir, dir, turns);
        if (n == 0)
            return "no turn fits";
        var rot = turns[((spin % n) + n) % n];
        var scale = ConstructMerge.ScaleFor(c, part, socket, def);
        var pos = ConstructWorld.Quantise(at - Rot24.Apply(rot, ds.At * scale));
        var seg = c.Parts[part].Seg;
        var newJoint = onLeaf;
        if (newJoint && LeafRefusal(c, part) is { } full)
            return full;
        g = new ConstructGhost { Part = part, Socket = socket, DefSocket = defSocket, Variant = spin, Pos = pos, Rot = rot, Seg = seg, NewJoint = newJoint, Scale = scale };
        if (Blocked(c, def, pos, rot, scale, seg))
            return InTheWay;
        return null;
    }

    /// <summary>
    /// The turns that make a socket facing <paramref name="own"/> mate a socket facing <paramref name="target"/>, by spin: a fixed
    /// order, upright first, then facing forward, so spin 0 is the natural way round. How many (a blueprint step's "spin" indexes it).
    /// </summary>
    public static int SpinTurns(byte own, byte target, Span<byte> turns)
    {
        var n = Rot24.Matching(own, Opposite(target), turns);
        for (var a = 0; a < n; a++)
        for (var b = a + 1; b < n; b++)
            if (Upright(turns[b]) > Upright(turns[a]))
                (turns[a], turns[b]) = (turns[b], turns[a]);
        return n;
    }

    // ---- A joint's leaf (shared by the wrench's Place / Attach and the welder's socket and freeform welds: board item wh01) ----

    /// <summary>Do these sockets' kinds mate? The same kind, or anything hung on a joint's leaf: a door's leaf socket, or a plain socket, so any panel can be a door (a hinge's leaf is a leaf socket, a motor's a plain one).</summary>
    public static bool KindsMate(SocketKind target, SocketKind own, bool onLeaf) =>
        target == own || onLeaf && own is SocketKind.Leaf or SocketKind.Struct;

    /// <summary>Does what goes on socket <paramref name="socket"/> of part <paramref name="part"/> hang on that part's joint (it is the joint's leaf socket)?</summary>
    public static bool OnLeaf(Construct c, int part, int socket) =>
        (uint)part < (uint)c.PartCount && c.DefOf(part) is { HasJoint: true } d && socket == d.LeafIndex;

    /// <summary>
    /// Why nothing more can hang on joint part <paramref name="part"/>'s leaf, or null: a joint that already carries
    /// something takes nothing more on its leaf socket, and a new one needs room for a joint (<paramref name="extraJoints"/>:
    /// joints the piece brings along). A joint left with nothing on it (what hung on it came off) is used again.
    /// </summary>
    public static string? LeafRefusal(Construct c, int part, int extraJoints = 0)
    {
        if (c.RailBusy(part))
            return "this stacked rail already carries a load: it has one carriage";
        var j = c.JointOf(part);
        if (j >= 0)
            return c.SegCarries(j + 1) ? "something already hangs on that joint" : null;
        var slots = c.DefOf(part).JointSlots;
        return c.JointCount + extraJoints + slots > Construct.MaxJoints
            ? slots > 1 ? $"at most {Construct.MaxJoints} joints a construct (a flexible joint takes {slots})" : $"at most {Construct.MaxJoints} joints a construct" : null;
    }

    /// <summary>
    /// The segment a piece hung on joint part <paramref name="part"/>'s leaf goes on (call <see cref="LeafRefusal"/> first):
    /// the joint's own, made now (at rest), or the empty one it had, put back at rest. 0 when there is no room.
    /// </summary>
    public static byte HangOnLeaf(ConstructWorld world, Construct c, int part)
    {
        var j = c.JointOf(part);
        if (j < 0)
            return world.AddJoint(c, part);
        var seg = c.LeafSegOf(j);
        for (var k = j; k < seg; k++)
        {
            ref var jt = ref c.Joints[k];
            jt.Angle = jt.PrevAngle = jt.Vel = 0;
            jt.Ref = 0;
            jt.RefSet = false;
        }
        c.Dirty = true;
        return (byte)seg;
    }

    private static float Upright(byte r) => Rot24.Apply(r, Vector3.UnitY).Y * 2 + Rot24.Apply(r, -Vector3.UnitZ).Z * -1 + Rot24.Apply(r, Vector3.UnitX).X * 0.1f;

    /// <summary>Would a part there overlap one already on (all compared in the rest frame, a centimetre of slack)?</summary>
    public static bool Blocked(Construct c, ConstructPartDef def, Vector3 pos, byte rot, float scale = 1f, int seg = -1, int except = -1)
    {
        for (var b = 0; b < def.SolidBoxes; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center * scale);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half * scale) - new Vector3(0.01f);
            if (c.MergeCount > 0 && ConstructMerge.InMerged(c, bc, bh + new Vector3(0.01f), seg < 0 ? 0 : seg))
                return true; // (the front of a merged receiver, beyond its members)
            var frame = c.FreeformCount > 0 ? c.FrameOf(seg < 0 ? 0 : seg) : 0;
            var hits = Construct.PartScratch; // (a big body: only the parts near the box; this runs per ghost, per frame)
            var near = c.FreeformCount > 0 ? c.PartsInBox(new Vector3(float.MinValue), new Vector3(float.MaxValue), hits) : c.PartsInBox(bc - bh, bc + bh, hits);
            for (var hi = 0; hi < near; hi++)
            {
                var i = hits[hi];
                if (i == except)
                    continue; // (a part being swapped: ConstructReplace.cs)
                if (c.FreeformCount > 0 && c.FrameOf(c.Parts[i].Seg) != frame)
                    continue; // (a freeform segment's parts are in a frame of their own: board item fw01)
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
            for (var s = 0; s < c.SocketCount(i) && n < into.Length; s++)
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
        if (def.IsReceiverGun && c.Dirty)
            c.Rebuild(); // (the merges as they stand, before this receiver: ConstructMerge.Settle)
        if (g.NewJoint)
        {
            seg = HangOnLeaf(world, c, g.Part);
            if (seg == 0)
                return -1;
        }
        var i = world.AddPart(c, def, g.Pos, g.Rot, seg);
        if (i < 0)
            return -1;
        c.PartScale[i] = g.Scale; // (until the rebuild derives it: its other sockets link where they are)
        world.AddLink(c, g.Part, g.Socket, i, g.DefSocket);
        AutoLink(world, c, i);
        if (def.IsReceiverGun)
            i = ConstructMerge.Settle(world, c, -1, i); // (a square of receivers merges: gun parts on their own sockets pop off)
        return i;
    }

    /// <summary>Links part <paramref name="i"/>'s free sockets to free sockets of the same kind it touches, facing it (welds a grid together).</summary>
    public static void AutoLink(ConstructWorld world, Construct c, int i)
    {
        var d = c.DefOf(i);
        // The parts it touches (the rest can't meet its sockets): building a hull stays linear in the parts near each new one.
        c.PartBounds(i, out var imn, out var imx);
        var reachOut = new Vector3(0.05f);
        imn -= reachOut;
        imx += reachOut;
        var touching = new List<int>(16); // (an edit, not a tick)
        for (var j = 0; j < c.PartCount; j++)
        {
            if (j == i || c.Parts[j].Seg != c.Parts[i].Seg)
                continue;
            c.PartBounds(j, out var jmn, out var jmx);
            if (jmn.X <= imx.X && jmx.X >= imn.X && jmn.Y <= imx.Y && jmx.Y >= imn.Y && jmn.Z <= imx.Z && jmx.Z >= imn.Z)
                touching.Add(j);
        }
        for (var s = 0; s < d.Sockets.Length; s++)
        {
            if (c.SocketUsed(i, s) || d.Sockets[s].Kind != SocketKind.Struct || (d.HasJoint && s == d.LeafIndex))
                continue;
            c.SocketAt(i, s, out var at, out var dir);
            foreach (var j in touching)
            {
                var od = c.DefOf(j);
                var done = false;
                for (var t = 0; t < od.Sockets.Length && !done; t++)
                {
                    if (od.Sockets[t].Kind != SocketKind.Struct || od.Sockets[t].Class != d.Sockets[s].Class || c.SocketUsed(j, t) || (od.HasJoint && t == od.LeafIndex)) // (a joint's leaf only takes what hangs on it)
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
        if (ConstructModules.LinkWhy(fd, 255, td, ConstructModules.IsBus(fd, 255) ? ConstructRoutes.DataPortByte(ConstructModules.BusPort(td)) : (byte)255) is { } moduleWhy)
            return moduleWhy;
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
        if (world.TotalRoutes >= world.WireCap)
            return "the world has all the wires it can take";
        return null;
    }

    /// <summary>A new construct of one part standing on the ground at <paramref name="ground"/>, facing <paramref name="yaw"/>.</summary>
    public static Construct? PlaceNew(ConstructWorld world, ConstructPartDef def, Vector3 ground, float yaw)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw); // our yaw turns -Z towards +X
        if (GravityFrames.Curved)
            rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(GravityFrames.Basis(ground)) * rot); // (a planet: upright on the ground there, yaw in its frame)
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

    public const float MaxWeldReach = 4096f; // the farthest a blueprint's weld step may be from its anchor (a capital hull's pieces lie far apart)

    public const string BlueprintFormat = "call-of-slop-construct-blueprint/1";

    /// <summary>A blueprint: a name and its build steps (part, then which part's socket it goes on, by which of its own sockets, turned how).</summary>
    public sealed class Blueprint
    {
        public string Name = "", File = "", Description = "", Group = ""; // (Group: the inventory group it shows under; "": by what it holds)
        public List<(string Part, int On, string Socket, string Own, int Spin)> Steps = new();
        public List<int> StepSettings = new(); // each step's logic setting (-1: the part's default)
        public List<List<(int Slot, float Value)>?> StepConfig = new(); // each step's part settings ("config": the configure tool's values; null: defaults)
        public List<(int From, int To)> Wires = new(); // wires between steps' parts (milestone 2): straight data links since the routed links
        public List<ConstructRoutes.LinkSpec> Links = new(); // routed links between steps' parts (decision 20)
        public List<ConnectorSpec> Connectors = new(); // ropes and springs between steps' parts (ConstructConnectors.cs)
        public List<ConstructTracks.Spec> Tracks = new(); // freeform tracks round steps' wheels
        public bool FrozenBase; // it starts with its base frozen, and these steps' joints ("frozenBase", "frozenJoints": ConstructFreeChain.cs)
        public List<int> FrozenJoints = new();
        public List<ConstructCargo.CargoSpec> Cargo = new(); // loose parts it is built carrying in its frozen cargo volumes (decision 21)
        public List<Vector3?> StepAt = new(); // a step saved from a construct ("at": [x, y, z]): the part stands exactly there, whatever else is near it (sockets 2 cm apart weld, and a piece may overlap another by a weld); null: where its socket puts it
        public List<uint> StepPaint = new(); // each step's paint colour ("paint": "#rrggbb"; 0: its own colours; ConstructPaint.cs)
        public List<(int Step, string Table)> Loot = new(); // containers that fill from a loot table when it is spawned ("loot": [{"step": n, "table": "bandit"}]; ConstructLoot.cs)
        public List<(Vector3 Pos, Quaternion Rot)?> StepWeld = new(); // a step freeform-welded on (board item fw01): the part's frame in its "on" step's segment frame; null: socket steps
    }

    private static readonly List<string> _refusedBlueprints = new();
    private static readonly Lazy<List<Blueprint>> _blueprints = new(() => LoadBlueprints(BlueprintFolder, _refusedBlueprints, Mods.Active), LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>Every blueprint of a folder (by file name), then the mods' changes and new ones (docs/mods/MODS.md); each must build.</summary>
    public static List<Blueprint> LoadBlueprints(string folder, List<string> refused, IReadOnlyList<Mods.Mod>? mods = null)
    {
        var list = new List<Blueprint>();
        foreach (var df in Mods.Resolve(Mods.BaseFiles(folder, false), mods ?? [], "Constructs/Blueprints", false, refused, (t, f) =>
                 {
                     if (Build(new ConstructWorld(), ParseBlueprint(t, f), Vector3.Zero, Quaternion.Identity, out var why) == null)
                         throw new ConstructPartException($"{f}: {why}");
                 }))
        {
            try
            {
                var bp = ParseBlueprint(df.Text, df.File);
                // Build it once on a scratch world: every step must fit.
                var scratch = new ConstructWorld();
                if (Build(scratch, bp, Vector3.Zero, Quaternion.Identity, out var why) == null)
                    throw new ConstructPartException($"{bp.File}: {why}");
                list.Add(bp);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                var why = ex is ConstructPartException ? ex.Message : $"{df.File}: {ex.Message}";
                refused.Add(df.Mod.Length > 0 ? $"mod {df.Mod}: {why}" : why);
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
                case "group":
                    bp.Group = p.Value.GetString() ?? "";
                    break;
                case "steps":
                    var k = 0;
                    foreach (var s in p.Value.EnumerateArray())
                    {
                        string part = "", sock = "", own = "";
                        int on = -1, spin = 0, setting = -1;
                        var paint = 0u;
                        JsonElement? config = null;
                        (Vector3, Quaternion)? weld = null;
                        Vector3? stepAt = null;
                        foreach (var f in s.EnumerateObject())
                            switch (f.Name)
                            {
                                case "part": part = f.Value.GetString() ?? ""; break;
                                case "on": on = f.Value.GetInt32(); break;
                                case "socket": sock = f.Value.GetString() ?? ""; break;
                                case "by": own = f.Value.GetString() ?? ""; break;
                                case "spin": spin = f.Value.ValueKind == JsonValueKind.String && f.Value.GetString() == "auto" ? -1 : f.Value.GetInt32(); break; // ("auto": smart rotation's guess, SmartRotation.BestSpin)
                                case "setting": setting = f.Value.GetInt32(); break;
                                case "config": config = f.Value.Clone(); break;
                                case "weld": weld = ParseWeld(f.Value, file, k, ref on); break;
                                case "at":
                                    if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 3 || new Vector3(f.Value[0].GetSingle(), f.Value[1].GetSingle(), f.Value[2].GetSingle()) is var ap && !float.IsFinite(ap.X + ap.Y + ap.Z))
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"at\" is [x, y, z]");
                                    stepAt = ap;
                                    break;
                                case "paint":
                                    if (f.Value.ValueKind != JsonValueKind.String || !ConstructPaint.TryParse(f.Value.GetString(), out paint))
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"paint\" is a colour like \"#c04020\"");
                                    break;
                                default: throw new ConstructPartException($"{file}: steps[{k}]: unknown key \"{f.Name}\" (part, on, socket, by, spin, at, setting, config, weld, paint)");
                            }
                        if (k > 0 && (on < 0 || on >= k))
                            throw new ConstructPartException($"{file}: steps[{k}]: \"on\" must name an earlier step (0 to {k - 1})");
                        bp.Steps.Add((part, k == 0 ? -1 : on, sock, own, spin));
                        bp.StepSettings.Add(setting);
                        bp.StepWeld.Add(weld);
                        bp.StepAt.Add(stepAt);
                        bp.StepPaint.Add(paint);
                        bp.StepConfig.Add(config is { } cfg
                            ? ConstructParts.Find(part) is { } cdef ? ConstructConfig.ParseValues(cdef, cfg, $"{file}: steps[{k}]") : throw new ConstructPartException($"{file}: steps[{k}]: no part named \"{part}\"")
                            : null);
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
                case "connectors":
                    foreach (var cn in p.Value.EnumerateArray())
                        bp.Connectors.Add(ConstructWorld.ParseConnector(cn, file));
                    break;
                case "tracks":
                    foreach (var tk in p.Value.EnumerateArray())
                        bp.Tracks.Add(ConstructTracks.Parse(tk, file, bp.Tracks.Count));
                    break;
                case "frozenBase":
                    bp.FrozenBase = p.Value.ValueKind == JsonValueKind.True;
                    break;
                case "frozenJoints":
                    foreach (var fj in p.Value.EnumerateArray())
                        bp.FrozenJoints.Add(fj.GetInt32());
                    break;
                case "loot":
                    foreach (var lt in p.Value.EnumerateArray())
                    {
                        var table = lt.GetProperty("table").GetString() ?? "";
                        if (!ConstructLoot.Tables.ContainsKey(table))
                            throw new ConstructPartException($"{file}: loot: no table \"{table}\" in Loot.json");
                        bp.Loot.Add((lt.GetProperty("step").GetInt32(), table));
                    }
                    break;
                case "cargo":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() > ConstructCargo.MaxPerConstruct)
                        throw new ConstructPartException($"{file}: \"cargo\" lists up to {ConstructCargo.MaxPerConstruct} loose parts");
                    foreach (var cg in p.Value.EnumerateArray())
                        bp.Cargo.Add(ConstructCargo.ParseSpec(cg, file, bp.Cargo.Count));
                    break;
                default:
                    throw new ConstructPartException($"{file}: unknown key \"{p.Name}\" (format, name, description, group, steps, wires, links, connectors, cargo, loot)");
            }
        if (bp.Name.Length == 0 || bp.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be plain ASCII text");
        if (bp.Steps.Count == 0)
            throw new ConstructPartException($"{file}: \"steps\" is empty");
        return bp;
    }

    /// <summary>A step's "weld" (a freeform weld, board item fw01): {"on": step, "pos": [x, y, z], "rot": [x, y, z, w]} (the part's frame in that step's segment frame; rot a quaternion).</summary>
    private static (Vector3, Quaternion) ParseWeld(JsonElement e, string file, int k, ref int on)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: steps[{k}]: \"weld\" is {{\"on\": step, \"pos\": [x, y, z], \"rot\": [x, y, z, w]}}");
        var pos = Vector3.Zero;
        var rot = Quaternion.Identity;
        foreach (var f in e.EnumerateObject())
            switch (f.Name)
            {
                case "on": on = f.Value.GetInt32(); break;
                case "pos":
                    if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 3)
                        throw new ConstructPartException($"{file}: steps[{k}]: weld \"pos\" is [x, y, z]");
                    pos = new Vector3(f.Value[0].GetSingle(), f.Value[1].GetSingle(), f.Value[2].GetSingle());
                    break;
                case "rot":
                    if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 4)
                        throw new ConstructPartException($"{file}: steps[{k}]: weld \"rot\" is a quaternion [x, y, z, w]");
                    rot = new Quaternion(f.Value[0].GetSingle(), f.Value[1].GetSingle(), f.Value[2].GetSingle(), f.Value[3].GetSingle());
                    break;
                default: throw new ConstructPartException($"{file}: steps[{k}]: weld: unknown key \"{f.Name}\" (on, pos, rot)");
            }
        if (!float.IsFinite(pos.X + pos.Y + pos.Z + rot.X + rot.Y + rot.Z + rot.W) || rot.LengthSquared() < 0.5f || pos.Length() > MaxWeldReach)
            throw new ConstructPartException($"{file}: steps[{k}]: weld \"pos\" / \"rot\" must be finite, rot a unit quaternion, pos within {MaxWeldReach:0} m");
        return (pos, Quaternion.Normalize(rot));
    }

    /// <summary>A freeform weld as a blueprint step's "weld" text (the inverse of the parser; the self-test's round trip).</summary>
    public static string WeldJson(int on, Vector3 pos, Quaternion rot) =>
        FormattableString.Invariant($"{{\"on\": {on}, \"pos\": [{pos.X:R}, {pos.Y:R}, {pos.Z:R}], \"rot\": [{rot.X:R}, {rot.Y:R}, {rot.Z:R}, {rot.W:R}]}}");

    /// <summary>Builds a blueprint as a new construct at a pose, through the same rules as a player (null and the reason on a step that doesn't fit).</summary>
    public static Construct? Build(ConstructWorld world, Blueprint bp, Vector3 pos, Quaternion rot, out string why)
    {
        var run = new BuildRun();
        foreach (var _ in BuildSteps(world, bp, pos, rot, run))
        {
        }
        why = run.Why;
        return run.Built;
    }

    /// <summary>What <see cref="BuildSteps"/> came to: the construct, or the reason a step refused.</summary>
    public sealed class BuildRun
    {
        public Construct? Built;
        public string Why = "";
        public bool KeepPartOf; // the caller wants each step's part
        public int[]? PartOf;
    }

    /// <summary>
    /// <see cref="Build"/> a step at a time, for a blueprint too big for one tick (a stamp: ConstructEditsSelect.cs): each
    /// item is one step done; the result lands in <paramref name="run"/> when the sequence ends.
    /// </summary>
    public static IEnumerable<int> BuildSteps(ConstructWorld world, Blueprint bp, Vector3 pos, Quaternion rot, BuildRun run)
    {
        var c = world.Create(pos, rot);
        if (c == null)
        {
            run.Why = "too many constructs";
            yield break;
        }
        var partOf = new int[Math.Max(1, bp.Steps.Count)];
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var (name, on, sock, own, spin) = bp.Steps[k];
            if (ConstructParts.Find(name) is not { } def)
            {
                run.Why = $"step {k}: no part named \"{name}\"";
                world.Remove(c);
                yield break;
            }
            if (k == 0)
            {
                partOf[0] = world.AddPart(c, def, Vector3.Zero, (byte)(Math.Max(spin, 0) % Rot24.Count));
                SetSetting(c, partOf[0], def, bp.StepSettings[0]);
                SetConfig(world, c, partOf[0], def, bp, 0);
                yield return k;
                continue;
            }
            var target = partOf[on];
            var td = c.DefOf(target);
            var laidAt = k < bp.StepAt.Count ? bp.StepAt[k] : null;
            if (sock.Length == 0 && laidAt is { } alone && !(k < bp.StepWeld.Count && bp.StepWeld[k] != null))
            {
                // Saved with no socket to go by (a piece the selection doesn't join to the rest): it stands there, welded by whatever sockets it meets.
                partOf[k] = world.AddPart(c, def, ConstructWorld.Quantise(alone), (byte)(Math.Max(spin, 0) % Rot24.Count));
                if (partOf[k] < 0)
                {
                    run.Why = $"step {k}: too many parts";
                    world.Remove(c);
                    yield break;
                }
                AutoLink(world, c, partOf[k]);
                SetSetting(c, partOf[k], def, bp.StepSettings[k]);
                SetConfig(world, c, partOf[k], def, bp, k);
                yield return k;
                continue;
            }
            if (k < bp.StepWeld.Count && bp.StepWeld[k] is { } weld)
            {
                // A freeform weld (board item fw01): the part on a new rigid segment at its stored pose.
                partOf[k] = world.AddFreeform(c, target, def, weld.Pos, weld.Rot);
                if (partOf[k] < 0)
                {
                    run.Why = $"step {k}: too many parts or freeform welds";
                    world.Remove(c);
                    yield break;
                }
                SetSetting(c, partOf[k], def, bp.StepSettings[k]);
                SetConfig(world, c, partOf[k], def, bp, k);
                c.Rebuild(); // (the next step may fit a part onto the new segment: frames are cached by the rebuild, board item fd01)
                yield return k;
                continue;
            }
            var ts = Array.FindIndex(td.Sockets, s => s.Name == sock);
            var ds = Array.FindIndex(def.Sockets, s => s.Name == own);
            if (ts < 0 || ds < 0)
            {
                run.Why = $"step {k}: {(ts < 0 ? $"{td.Name} has no socket \"{sock}\"" : $"{def.Name} has no socket \"{own}\"")}";
                world.Remove(c);
                yield break;
            }
            if (spin < 0)
                spin = Math.Max(0, SmartRotation.BestSpin(c, target, ts, def, ds)); // (a step whose turn is "auto")
            if (Place(c, target, ts, def, ds, spin, out var g) is { } bad && !(laidAt != null && bad == InTheWay))
            {
                run.Why = $"step {k}: {def.Name} on {td.Name} \"{sock}\": {bad}";
                world.Remove(c);
                yield break;
            }
            if (laidAt is { } stands)
                g.Pos = ConstructWorld.Quantise(stands);
            partOf[k] = Attach(world, c, def, g);
            if (partOf[k] < 0)
            {
                run.Why = $"step {k}: too many parts";
                world.Remove(c);
                yield break;
            }
            if (!SetSetting(c, partOf[k], def, bp.StepSettings[k]))
            {
                run.Why = $"step {k}: a {def.Name} has no setting {bp.StepSettings[k]}";
                world.Remove(c);
                yield break;
            }
            SetConfig(world, c, partOf[k], def, bp, k);
            yield return k;
        }
        for (var k = 0; k < bp.Steps.Count && k < bp.StepPaint.Count; k++)
            if (bp.StepPaint[k] != 0)
                c.Parts[partOf[k]].Paint = bp.StepPaint[k]; // (the paint tool's colours; a part with nothing to paint just carries it)
        // The wires, through the linking tool's rules.
        foreach (var (from, to) in bp.Wires)
        {
            if (from < 0 || from >= bp.Steps.Count || to < 0 || to >= bp.Steps.Count)
            {
                run.Why = $"wire {from} -> {to}: no such step";
                world.Remove(c);
                yield break;
            }
            if (CanWire(world, c, partOf[from], partOf[to]) is { } bad)
            {
                run.Why = $"wire {from} -> {to} ({c.DefOf(partOf[from]).Name} to {c.DefOf(partOf[to]).Name}): {bad}";
                world.Remove(c);
                yield break;
            }
            world.AddWire(c, partOf[from], partOf[to]);
        }
        // The routed links, through the linking tool's rules.
        for (var k = 0; k < bp.Links.Count; k++)
            if (ConstructRoutes.BuildLink(world, c, bp.Links[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"links[{k}]: {bad}";
                world.Remove(c);
                yield break;
            }
        for (var k = 0; k < bp.Connectors.Count; k++)
            if (world.BuildConnector(c, bp.Connectors[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"connectors[{k}]: {bad}";
                world.Remove(c);
                yield break;
            }
        // Fuel line parts and plain drive shafts become pipe and shaft links (blueprints auto-converted).
        ConstructRoutes.MigrateLegacy(world, c);
        for (var k = 0; k < bp.Tracks.Count; k++)
            if (ConstructTracks.Build(c, bp.Tracks[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"tracks[{k}]: {bad}";
                world.Remove(c);
                yield break;
            }
        // What was frozen when it was saved.
        foreach (var step in bp.FrozenJoints)
            if (step >= 0 && step < bp.Steps.Count && c.JointOf(partOf[step]) is var fj and >= 0)
                for (var j = fj; j < c.JointCount && c.Joints[j].Hinge == partOf[step] && !c.Joints[j].Freeform; j++)
                    c.Joints[j].Frozen = true;
        if (bp.FrozenBase)
            c.Frozen = c.Anchored = true;
        c.Rebuild();
        // What it carries (decision 21): loose parts in the cargo volumes its steps start frozen.
        if (bp.Cargo.Count > 0)
        {
            var made = new List<Construct>(bp.Cargo.Count);
            if (ConstructCargo.BuildCargo(world, c, bp.Cargo, made) is { } bad)
            {
                run.Why = bad;
                foreach (var m in made)
                    world.Remove(m);
                world.Remove(c);
                yield break;
            }
        }
        for (var k = 0; k < bp.Loot.Count; k++)
            if (bp.Loot[k].Step < 0 || bp.Loot[k].Step >= bp.Steps.Count || c.DefOf(partOf[bp.Loot[k].Step]).Slots.Length == 0)
            {
                run.Why = $"loot[{k}]: step {bp.Loot[k].Step} isn't a container with slots";
                world.Remove(c);
                yield break;
            }
        if (run.KeepPartOf)
            run.PartOf = partOf;
        run.Built = c;
    }

    private static bool SetSetting(Construct c, int part, ConstructPartDef def, int setting)
    {
        if (setting < 0)
            return true;
        if (setting >= def.Settings.Length)
            return false;
        c.Parts[part].Setting = (byte)setting;
        ConstructConfig.ApplyPreset(ref c.Parts[part], def); // (a timer's setting picks its period from the presets)
        return true;
    }

    /// <summary>A step's "config" (the part's settings), after its "setting".</summary>
    private static void SetConfig(ConstructWorld world, Construct c, int part, ConstructPartDef def, Blueprint bp, int step)
    {
        if (part < 0 || step >= bp.StepConfig.Count || bp.StepConfig[step] is not { } values)
            return;
        foreach (var (slot, v) in values)
            if (world.Creative || !def.Config[slot].CreativeOnly) // (a survival world builds it as it comes: an empty tank, whatever the blueprint says)
                ConstructConfig.Set(ref c.Parts[part], def, slot, v);
        ConstructConfig.StartState(ref c.Parts[part], def);
        if (def.IsVolumeStore && world.Creative)
            ConstructStoreVolume.SetContents(world, c, part, limit: false); // (its walls may not be up yet: an open box spills what it must later)
    }

    /// <summary>
    /// A blueprint's construct set down standing on the ground at <paramref name="ground"/> (its lowest point there),
    /// upright along the local up there (a planetoid: away from its centre; <paramref name="yaw"/> turns it about that up).
    /// </summary>
    public static Construct? BuildOnGround(ConstructWorld world, Blueprint bp, Vector3 ground, float yaw, out string why)
    {
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationY(-yaw) * GravityFrames.Basis(ground)));
        var run = new BuildRun { KeepPartOf = bp.Loot.Count > 0 };
        foreach (var _ in BuildSteps(world, bp, ground, rot, run))
        {
        }
        why = run.Why;
        var c = run.Built;
        if (c == null)
            return null;
        if (run.PartOf != null)
            ConstructLoot.Fill(world, c, bp, run.PartOf); // (a spawn rolls fresh; a scratch build or a stamp doesn't)
        // Lowest point: the parts' bounds, or the wheels' bottoms.
        var low = c.LocalMin.Y;
        for (var k = 0; k < c.WheelCount; k++)
            low = MathF.Min(low, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        c.Pos = ground - Vector3.Transform(new Vector3(0, low - 0.02f, 0), rot);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.NetHistReset();
        if (bp.Cargo.Count > 0)
            ConstructCargo.FollowAll(world); // (its cargo moves with it)
        return c;
    }
}
