using System.Globalization;
using System.Diagnostics;
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

    /// <summary>Two modular engine pieces mate whatever their size classes (sized-parts design 6.4: anything bolts on; a small fuel system starves its bank).</summary>
    public static bool EngineMate(ConstructPartDef a, ConstructPartDef b) => a.Piece != EnginePiece.None && b.Piece != EnginePiece.None;

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
        if (def.IsEnvelope && c.CellCount >= Construct.MaxCells)
            return $"a construct has at most {Construct.MaxCells} lift cells";
        if (def.Role == PartRole.Burner && c.BurnerCount >= Construct.MaxBurners)
            return $"a construct has at most {Construct.MaxBurners} burners";
        if (def.IsTankVolume && !def.IsEnvelope && ConstructFluid.VolumeCount(c) >= ConstructFluid.MaxVolumes)
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
        if (ts.Class != ds.Class && !EngineMate(target, def))
            return $"that's a {ConstructParts.ClassNames[ts.Class]} socket and this a {ConstructParts.ClassNames[ds.Class]} part: use an adapter";
        if (c.SocketUsed(part, socket))
            return "something is already attached there";
        if (ConstructAmmo.SocketRefusal(c, part, def) is { } noMag)
            return noMag;
        if (def.HasJoint && defSocket == def.LeafIndex)
            return $"a {def.Name} goes on by its base (its {def.LeafSocket} is what moves)";
        if (ts.Kind == SocketKind.Leaf && !onLeaf && !(target.Role == PartRole.Door && def.Role == PartRole.Hinge))
            return "doors hang on hinges";
        if (ds.Kind == SocketKind.Leaf && !onLeaf)
            return "doors hang on hinges";
        if (ts.Kind == SocketKind.Module && ModuleSockets.SizeRefusal(target.ModPart == ModPart.Socket ? target : def, target.ModPart == ModPart.Socket ? def : target) is { } size)
            return size;
        c.SocketAt(part, socket, out var at, out var dir);
        Span<byte> turns = stackalloc byte[4];
        var n = SpinTurns(ds.Dir, dir, turns);
        if (n == 0)
            return "no turn fits";
        var rot = turns[((spin % n) + n) % n];
        if (ts.Kind == SocketKind.Module && ModuleTurn(c, part, def, turns, n, spin) is var mt)
        {
            // (a spin always counts over the full list, as blueprints write it: a turn the footprint doesn't allow moves on to the next one that it does)
            if (mt < 0)
                return "no turn fits";
            rot = (byte)mt;
        }
        var scale = ConstructMerge.ScaleFor(c, part, socket, def);
        var pos = ConstructWorld.Quantise(at - Rot24.Apply(rot, ds.At * scale));
        var seg = c.Parts[part].Seg;
        var newJoint = onLeaf;
        if (newJoint && LeafRefusal(c, part) is { } full)
            return full;
        g = new ConstructGhost { Part = part, Socket = socket, DefSocket = defSocket, Variant = spin, Pos = pos, Rot = rot, Seg = seg, NewJoint = newJoint, Scale = scale };
        if (Blocked(c, def, pos, rot, scale, seg))
            return InTheWay;
        if (ModuleSockets.PlaceRefusal(c, def, part, pos, rot, scale) is { } module)
            return module;
        if (def.ShellPod is { Flush: true } && ConstructShellPod.BodyRefusal(c, def, pos, rot, seg) is { } blocked)
            return blocked; // (a flush pod whose body, proud of a thin surface's far side, would run into another part: after the overlap test, the dearer one)
        return null;
    }

    /// <summary>
    /// The turn a part takes onto a Module socket for this spin (module-sockets.md 2.1: 4 square, 2 oblong, 1 keyed): the spin's
    /// own turn in the full list when the footprint allows it, else the next allowed one after it; -1 when none is.
    /// </summary>
    private static int ModuleTurn(Construct c, int part, ConstructPartDef def, ReadOnlySpan<byte> turns, int n, int spin)
    {
        var target = c.DefOf(part);
        var plateIsTarget = target.ModPart == ModPart.Socket;
        var keyed = plateIsTarget && ModuleSockets.SocketOfPlate(c, part) is var si and >= 0 && c.ModSockets[si].Keyed;
        var at = ((spin % n) + n) % n;
        for (var k = 0; k < n; k++)
        {
            var r = turns[(at + k) % n];
            var ok = plateIsTarget
                ? ModuleSockets.TurnFits(target, c.Parts[part].Rot, def, r, keyed)
                : ModuleSockets.TurnFits(def, r, target, c.Parts[part].Rot, false);
            if (ok)
                return r;
        }
        return -1;
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

    /// <summary>
    /// The solid boxes a part is tested with as it goes on: all of them, but a flush shell pod's lid alone, since its body sinks into
    /// what it goes on (its body box, solid, then blocks what comes after it: ShellParts.Pod).
    /// </summary>
    public static int PlaceSolids(ConstructPartDef def) => def.ShellPod is { Flush: true } ? Math.Min(1, def.SolidBoxes) : def.SolidBoxes;

    /// <summary>The first part a part there would overlap (the same boxes and slack as <see cref="Blocked"/>, every part looked at: a reason's, never per frame), or -1.</summary>
    public static int BlockerOf(Construct c, ConstructPartDef def, Vector3 pos, byte rot, float scale = 1f)
    {
        var solids = PlaceSolids(def);
        for (var b = 0; b < solids; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center * scale);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half * scale) - new Vector3(0.01f);
            for (var i = 0; i < c.PartCount; i++)
                for (var k = 0; k < c.DefOf(i).SolidBoxes; k++)
                {
                    c.BoxOf(i, k, out var oc, out var oh);
                    if (MathF.Abs(bc.X - oc.X) < bh.X + oh.X && MathF.Abs(bc.Y - oc.Y) < bh.Y + oh.Y && MathF.Abs(bc.Z - oc.Z) < bh.Z + oh.Z)
                        return i;
                }
        }
        return -1;
    }

    /// <summary>Would a part there overlap one already on (all compared in the rest frame, a centimetre of slack)?</summary>
    public static bool Blocked(Construct c, ConstructPartDef def, Vector3 pos, byte rot, float scale = 1f, int seg = -1, int except = -1, bool podLids = false)
    {
        var solids = PlaceSolids(def);
        for (var b = 0; b < solids; b++)
        {
            var bc = pos + Rot24.Apply(rot, def.Boxes[b].Center * scale);
            var bh = Rot24.ApplyHalf(rot, def.Boxes[b].Half * scale) - new Vector3(0.01f);
            if (c.MergeCount > 0 && ConstructMerge.InMerged(c, bc, bh + new Vector3(0.01f), seg < 0 ? 0 : seg))
                return true; // (the front of a merged receiver, beyond its members)
            var frame = c.FreeformCount > 0 ? c.FrameOf(seg < 0 ? 0 : seg) : 0;
            var hits = Construct.PartScratch; // (a big body: only the parts near the box; this runs per ghost, per frame)
            var near = frame == 0 ? c.PartsInBox(bc - bh, bc + bh, hits) : c.PartsInBox(RestBox(c, frame, bc, bh, out var rmax), rmax, hits); // (the tree is in the rest frame: a freeform frame's box is turned into it)
            for (var hi = 0; hi < near; hi++)
            {
                var i = hits[hi];
                if (i == except)
                    continue; // (a part being swapped: ConstructReplace.cs)
                if (c.FreeformCount > 0 && c.FrameOf(c.Parts[i].Seg) != frame)
                    continue; // (a freeform segment's parts are in a frame of their own: board item fw01)
                var d = c.DefOf(i);
                for (var k = 0; k < (podLids ? PlaceSolids(d) : d.SolidBoxes); k++) // (podLids: a part swapped under a flush pod goes where the pod's body already is)
                {
                    c.BoxOf(i, k, out var oc, out var oh);
                    if (MathF.Abs(bc.X - oc.X) < bh.X + oh.X && MathF.Abs(bc.Y - oc.Y) < bh.Y + oh.Y && MathF.Abs(bc.Z - oc.Z) < bh.Z + oh.Z)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>A box in freeform frame <paramref name="frame"/>'s own coordinates as a box in the body's rest frame (around it: the part tree's frame), its min; max out.</summary>
    private static Vector3 RestBox(Construct c, int frame, Vector3 centre, Vector3 half, out Vector3 max)
    {
        c.RestPose(frame, out var q, out var t);
        var mid = t + Vector3.Transform(centre, q);
        var m = Matrix4x4.CreateFromQuaternion(q);
        var e = new Vector3(
            MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
            MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
            MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z) + new Vector3(0.01f); // (a centimetre round the turn's rounding)
        max = mid + e;
        return mid - e;
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
    public static int Attach(ConstructWorld world, Construct c, ConstructPartDef def, in ConstructGhost g, bool autoLink = true)
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
        if (autoLink)
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
        var touching = _touching ??= new int[Construct.MaxParts]; // (one walk over the parts, in part order: no list a part)
        var touchingN = 0;
        var seg = c.Parts[i].Seg;
        if (seg == 0 && c.PartCount >= Construct.TreeMinParts && c.MergeCount == 0) // (a merge rescales gun parts without refitting the tree: walk them all)
        {
            // A big body: the part tree's candidates (rest frame, which is segment 0's), tested as below and put back in part order,
            // so a socket links to the same part the full walk would find first (a 6,000-part works built in O(n log n), not O(n^2)).
            var near = _near ??= new int[Construct.MaxParts];
            var nearN = c.PartsInBox(imn, imx, near);
            for (var k = 0; k < nearN; k++)
            {
                var j = near[k];
                if (j == i || c.Parts[j].Seg != seg)
                    continue;
                c.PartBounds(j, out var jmn, out var jmx);
                if (jmn.X <= imx.X && jmx.X >= imn.X && jmn.Y <= imx.Y && jmx.Y >= imn.Y && jmn.Z <= imx.Z && jmx.Z >= imn.Z)
                    touching[touchingN++] = j;
            }
            Array.Sort(touching, 0, touchingN);
        }
        else
        for (var j = 0; j < c.PartCount; j++)
        {
            if (j == i || c.Parts[j].Seg != seg)
                continue;
            c.PartBounds(j, out var jmn, out var jmx);
            if (jmn.X <= imx.X && jmx.X >= imn.X && jmn.Y <= imx.Y && jmx.Y >= imn.Y && jmn.Z <= imx.Z && jmx.Z >= imn.Z)
                touching[touchingN++] = j;
        }
        for (var s = 0; s < d.Sockets.Length; s++)
        {
            if (c.SocketUsed(i, s) || d.Sockets[s].Kind != SocketKind.Struct || (d.HasJoint && s == d.LeafIndex))
                continue;
            c.SocketAt(i, s, out var at, out var dir);
            var facing = Opposite(dir);
            for (var tj = 0; tj < touchingN; tj++)
            {
                var j = touching[tj];
                var od = c.DefOf(j);
                var done = false;
                for (var t = 0; t < od.Sockets.Length && !done; t++)
                {
                    if (od.Sockets[t].Kind != SocketKind.Struct || od.Sockets[t].Class != d.Sockets[s].Class || (od.HasJoint && t == od.LeafIndex)) // (a joint's leaf only takes what hangs on it)
                        continue;
                    c.SocketAt(j, t, out var oat, out var odir);
                    if (odir == facing && Vector3.DistanceSquared(oat, at) < 0.0004f && !c.SocketUsed(j, t)) // (where it is first: walking the part's links costs more)
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

    [ThreadStatic]
    private static int[]? _touching; // (AutoLink's parts near the new one: the vehicle kit's thread builds too)

    [ThreadStatic]
    private static int[]? _near; // (AutoLink's tree query, before the bounds test)

    public const float WireReach = 8f; // the longest wire (between the two parts' centres)

    /// <summary>
    /// How long a wire between two parts is: centre to centre, except that a part bigger than the reach (a large or capital
    /// walker leg, a hull section) is reached at the nearest point of its bounds, so a big part can be wired at all.
    /// </summary>
    private static float WireSpan(Vector3 a0, Vector3 a1, Quaternion qa, Vector3 ta, Vector3 b0, Vector3 b1, Quaternion qb, Vector3 tb)
    {
        var ca = ta + Vector3.Transform((a0 + a1) * 0.5f, qa);
        var cb = tb + Vector3.Transform((b0 + b1) * 0.5f, qb);
        if (MathF.Max(MaxSide(a1 - a0), MaxSide(b1 - b0)) <= WireReach)
            return Vector3.Distance(ca, cb);
        var pa = Nearest(a0, a1, qa, ta, cb);
        return Vector3.Distance(pa, Nearest(b0, b1, qb, tb, pa));
    }

    private static float MaxSide(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));

    /// <summary>The point of a part's bounds (its segment's pose) nearest <paramref name="world"/>; a small part's own centre.</summary>
    private static Vector3 Nearest(Vector3 lo, Vector3 hi, Quaternion q, Vector3 t, Vector3 world)
    {
        if (MaxSide(hi - lo) <= WireReach)
            return t + Vector3.Transform((lo + hi) * 0.5f, q);
        var local = Vector3.Transform(world - t, Quaternion.Conjugate(q));
        return t + Vector3.Transform(Vector3.Clamp(local, lo, hi), q);
    }

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
        if (WireSpan(a0, a1, qa, ta, b0, b1, qb, tb) > WireReach)
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
        public List<string[]?> StepText = new(); // each step's messages ("text": LogicText.cs)
        public List<string[]?> StepMedia = new(); // each step's list ("media": ConstructMedia.cs)
        public List<List<(int Slot, float Value)>?> StepConfig = new(); // each step's part settings ("config": the configure tool's values; null: defaults)
        public HashSet<int> OrdersOnly = new(); // steps whose machine is built with an empty queue ("ordersOnly": true): it works orders and nothing else, never its own settings (WorkOrders)
        public List<(int From, int To)> Wires = new(); // wires between steps' parts (milestone 2): straight data links since the routed links
        public List<ConstructRoutes.LinkSpec> Links = new(); // routed links between steps' parts (decision 20)
        public List<(int Reel, int Inlet)> Hoses = new(); // hoses plugged in as built: an ammo reel to its socket, by step
        public List<ConnectorSpec> Connectors = new(); // ropes and springs between steps' parts (ConstructConnectors.cs)
        public List<NoCollideSpec> NoCollide = new(); // no-collide pairs between steps' parts (ConstructNoCollide.cs)
        public List<ConstructTracks.Spec> Tracks = new(); // freeform tracks round steps' wheels
        public bool FrozenBase; // it starts with its base frozen, and these steps' joints ("frozenBase", "frozenJoints": ConstructFreeChain.cs)
        public List<int> FrozenJoints = new();
        public bool Conduit; // its wires are laid as conduit along the walls though it isn't anchored: a ship's lamps ("conduit", ConstructConduit.cs)
        public List<ConstructCargo.CargoSpec> Cargo = new(); // loose parts it is built carrying in its frozen cargo volumes (decision 21)
        public List<Vector3?> StepAt = new(); // a step saved from a construct ("at": [x, y, z]): the part stands exactly there, whatever else is near it (sockets 2 cm apart weld, and a piece may overlap another by a weld); null: where its socket puts it
        public bool ExactLinks; // saved from a construct ("exactLinks": true): its parts link only as the steps say, nothing by landing on a free socket (ConstructClip.ToBlueprint)
        public List<(int On, string Socket, string Own)[]?> StepAlso = new(); // a saved step's other links to earlier steps ("also": [[step, their socket, its socket]...])
        public List<uint> StepPaint = new(); // each step's paint colour ("paint": "#rrggbb"; 0: its own colours; ConstructPaint.cs)
        public List<int> StepFuse = new(); // each step's fused unit ("fuse": 1, 2, ...: the steps with one number are one unit, under a new tag when built; 0: none; ConstructFuse.cs)
        public List<byte> StepFinish = new(); // each step's texture ("finish": a material's name, Materials/Surfaces.json; 0: its own materials, or a name this build doesn't have; ConstructFinish.cs)
        public List<ConstructCargo.CarrySpec> Carry = new(); // craft it is built carrying in its docks ("carry": [{"blueprint", "at", "turn"}]: the Zero Day's Shuttle; ConstructCargo.BuildCarry)
        public string Tow = ""; // a blueprint it spawns coupled behind it ("tow"): its first eye on this one's first ball (BuildOnGround)
        public List<(int Step, string Table)> Loot = new(); // containers that fill from a loot table when it is spawned ("loot": [{"step": n, "table": "raider"}]; ConstructLoot.cs)
        public List<(int Step, int Slot, string Part, int Count)> Stored = new(); // parts set in a container's slot when it is spawned, before its loot ("stored": [{"step": n, "slot": k, "part": "Box Mag", "count": 2}]; ConstructLoot.Stow)
        public List<(Vector3 Pos, Quaternion Rot)?> StepWeld = new(); // a step freeform-welded on (board item fw01): the part's frame in its "on" step's segment frame; null: socket steps
        public List<(int Step, WorkOrder Order)> Bill = new(); // lines on a Line Console's bill as it is built ("bill": [{"step": n, "piece": "Extrusion l-angle 20x20x150 metal", "count": 3}]; WorkOrders.cs): an order list, never material
        // Sub-blueprints (docs/sandbox/design/sub-blueprints.md; SubBlueprints.cs). A file's own fields:
        public string Id = ""; // "id": made once, kept through renames (16 hex digits; a shipped one "ship:<name>"); "": none yet
        public int Version; // "version": 1 up, +1 on every overwrite and lock change (0: none)
        public List<UseSpec> Uses = new(); // "uses": other saved blueprints built into this one; a step may name a use's step as ["uN", k] (SubBlueprints.UseRef)
        public ModuleBlock? Module; // "module": a module design's size, tags and stats, written from its parts at save (ModuleBlueprints.cs)
        public Dictionary<string, Dictionary<int, string>>? Packed; // "packed": the sub-blueprints a pack or a stamp's upload carries, by id and version (their text)
        // ...and a flattened one's (SubBlueprints.Flatten): every use's steps are its own, so every reader builds it as any blueprint.
        public UseMap? Map; // each step's use and each use's steps (null: no uses)
        public HashSet<int>? FlipExact; // steps whose links go the other way from ExactLinks (a use whose own blueprint links the other way)
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

    /// <summary>The blueprints being read now (LoadBlueprints' list while it fills, else null): a carried craft is found among those
    /// read before it while the list is checked, never through <see cref="Blueprints"/>, whose loader is running (constructs.md, 2026-11-10).</summary>
    [ThreadStatic] private static List<Blueprint>? _loading;

    /// <summary>A blueprint a carrier carries: from the list being read while the blueprints load, else the loaded ones.</summary>
    internal static Blueprint? FindCarried(string name) => _loading is { } l
        ? l.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        : FindBlueprint(name);

    /// <summary>Every blueprint of a folder (by file name), then the mods' changes and new ones (docs/mods/MODS.md); each must build.</summary>
    public static List<Blueprint> LoadBlueprints(string folder, List<string> refused, IReadOnlyList<Mods.Mod>? mods = null)
    {
        var list = new List<Blueprint>();
        var outer = _loading;
        _loading = list;
        var usesLater = new List<(Blueprint Bp, string Mod, int At, string Text, bool Carries)>(); // (and where it stands in the list: it goes back there, in file order)
        var remember = BlueprintCheckCache.Enabled(mods); // (dev builds skip the build check of a file that passed it unchanged: BlueprintCheckCache.cs)
        // Build it once on a scratch world: every step must fit (and a pre-built's wires are routed as conduit then, ConstructConduit.cs;
        // a dev build keeps those routes with the check's cache, so one that passed unchanged isn't built again just for them).
        void Check(Blueprint bp, string text)
        {
            var check = !remember || !BlueprintCheckCache.Passed(text);
            if (!check && ConstructConduit.Any(bp) && !ConstructConduit.Restore(bp, text))
                check = ConstructConduit.Wanted(bp) || !ConstructConduit.Remember(bp, text);
            if (!check)
                return;
            var scratch = new ConstructWorld();
            if (Build(scratch, bp, Vector3.Zero, Quaternion.Identity, out var why) == null)
                throw new ConstructPartException($"{bp.File}: {why}");
            if (remember)
            {
                BlueprintCheckCache.Add(text);
                ConstructConduit.Remember(bp, text);
            }
        }
        ConstructConduit.BeginLoad(); // (a pre-built's wires are routed as it is built here, not later in a server tick)
        try
        {
            foreach (var df in Mods.Resolve(Mods.BaseFiles(folder, false), mods ?? [], "Constructs/Blueprints", false, refused, (t, f) =>
                     {
                         var parsed = ParseBlueprint(t, f);
                         if (parsed.Uses.Count > 0)
                             return; // (a mod's blueprint with uses: flattened and built once every shipped one is read, below, and refused there with why)
                         if (Build(new ConstructWorld(), parsed, Vector3.Zero, Quaternion.Identity, out var why) == null)
                             throw new ConstructPartException($"{f}: {why}");
                     }))
            {
                try
                {
                    var bp = ParseBlueprint(df.Text, df.File);
                    if (bp.Uses.Count > 0)
                    {
                        usesLater.Add((bp, df.Mod, list.Count, df.Text, false)); // (a shipped blueprint may use shipped ones: flattened once they are all read)
                        continue;
                    }
                    if (bp.Carry.Exists(c => FindCarried(c.Blueprint) == null))
                    {
                        usesLater.Add((bp, df.Mod, list.Count, df.Text, true)); // (a carrier read before a craft it carries, the Mainframe before its Shuttles: built once every shipped one is read)
                        continue;
                    }
                    Check(bp, df.Text);
                    list.Add(bp);
                }
                catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
                {
                    var why = ex is ConstructPartException ? ex.Message : $"{df.File}: {ex.Message}";
                    refused.Add(df.Mod.Length > 0 ? $"mod {df.Mod}: {why}" : why);
                }
            }
            var inserted = 0;
            foreach (var (bp, mod, at, text, carries) in usesLater)
            {
                try
                {
                    if (carries)
                    {
                        Check(bp, text);
                        list.Insert(Math.Min(list.Count, at + inserted++), bp);
                        continue;
                    }
                    var flat = SubBlueprints.Flatten(bp, new SubBlueprints.Source { Library = false, Shipped = n => list.FirstOrDefault(b => b.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) });
                    if (Build(new ConstructWorld(), flat, Vector3.Zero, Quaternion.Identity, out var why) == null)
                        throw new ConstructPartException($"{bp.File}: {why}");
                    list.Insert(Math.Min(list.Count, at + inserted++), flat);
                }
                catch (ConstructPartException ex)
                {
                    refused.Add(mod.Length > 0 ? $"mod {mod}: {ex.Message}" : ex.Message);
                }
            }
            if (remember)
            {
                BlueprintCheckCache.Save();
                ConstructConduit.SaveDisk();
            }
        }
        finally
        {
            ConstructConduit.EndLoad();
            _loading = outer;
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
                        byte finish = 0;
                        var fuse = 0;
                        JsonElement? config = null;
                        string[]? texts = null;
                        string[]? media = null;
                        (Vector3, Quaternion)? weld = null;
                        Vector3? stepAt = null;
                        (int, string, string)[]? also = null;
                        foreach (var f in s.EnumerateObject())
                            switch (f.Name)
                            {
                                case "part": part = f.Value.GetString() ?? ""; break;
                                case "on": on = SubBlueprints.ParseRef(f.Value, file, k); break;
                                case "socket": sock = f.Value.GetString() ?? ""; break;
                                case "by": own = f.Value.GetString() ?? ""; break;
                                case "spin": spin = f.Value.ValueKind == JsonValueKind.String && f.Value.GetString() == "auto" ? -1 : f.Value.GetInt32(); break; // ("auto": smart rotation's guess, SmartRotation.BestSpin)
                                case "setting": setting = f.Value.GetInt32(); break;
                                case "config": config = f.Value.Clone(); break;
                                case "ordersOnly":
                                    if (f.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"ordersOnly\" is true or false");
                                    if (f.Value.GetBoolean())
                                        bp.OrdersOnly.Add(k);
                                    break;
                                case "text": texts = f.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray(); break;
                                case "media": media = f.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray(); break;
                                case "weld": weld = ParseWeld(f.Value, file, k, ref on); break;
                                case "at":
                                    if (f.Value.ValueKind != JsonValueKind.Array || f.Value.GetArrayLength() != 3 || new Vector3(f.Value[0].GetSingle(), f.Value[1].GetSingle(), f.Value[2].GetSingle()) is var ap && !float.IsFinite(ap.X + ap.Y + ap.Z))
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"at\" is [x, y, z]");
                                    stepAt = ap;
                                    break;
                                case "also":
                                    also = f.Value.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 3 ? (SubBlueprints.ParseRef(a[0], file, k), a[1].GetString() ?? "", a[2].GetString() ?? "")
                                        : throw new ConstructPartException($"{file}: steps[{k}]: \"also\" is a list of [step, their socket, its socket]")).ToArray();
                                    break;
                                case "paint":
                                    if (f.Value.ValueKind != JsonValueKind.String || !ConstructPaint.TryParse(f.Value.GetString(), out paint))
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"paint\" is a colour like \"#c04020\"");
                                    break;
                                case "finish":
                                    if (f.Value.ValueKind != JsonValueKind.String)
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"finish\" is a material's name, like \"tread plate\"");
                                    finish = ConstructFinish.Of(f.Value.GetString()); // (a material this build doesn't have: the part's own)
                                    break;
                                case "fuse":
                                    if (f.Value.ValueKind != JsonValueKind.Number || !f.Value.TryGetInt32(out fuse) || fuse < 0 || fuse > ConstructClip.MaxParts)
                                        throw new ConstructPartException($"{file}: steps[{k}]: \"fuse\" is a unit's number, 1 or more");
                                    break;
                                default: throw new ConstructPartException($"{file}: steps[{k}]: unknown key \"{f.Name}\" (part, on, socket, by, spin, at, also, setting, config, ordersOnly, weld, paint, finish, fuse)");
                            }
                        if (k > 0 && (on < 0 || on >= k) && !SubBlueprints.IsUseRef(on))
                            throw new ConstructPartException($"{file}: steps[{k}]: \"on\" must name an earlier step (0 to {k - 1})");
                        bp.Steps.Add((part, k == 0 ? -1 : on, sock, own, spin));
                        bp.StepSettings.Add(setting);
                        bp.StepWeld.Add(weld);
                        bp.StepAt.Add(stepAt);
                        bp.StepAlso.Add(also);
                        bp.StepPaint.Add(paint);
                        bp.StepFinish.Add(finish);
                        bp.StepFuse.Add(fuse);
                        bp.StepText.Add(texts);
                        bp.StepMedia.Add(media);
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
                        bp.Wires.Add((SubBlueprints.ParseRef(wire[0], file, -1), SubBlueprints.ParseRef(wire[1], file, -1)));
                    }
                    break;
                case "links":
                    foreach (var link in p.Value.EnumerateArray())
                        bp.Links.Add(ConstructRoutes.ParseLink(link, file, bp.Links.Count));
                    break;
                case "hoses":
                    foreach (var hose in p.Value.EnumerateArray())
                    {
                        if (hose.ValueKind != JsonValueKind.Array || hose.GetArrayLength() != 2)
                            throw new ConstructPartException($"{file}: a hose is [reel step, inlet step]");
                        bp.Hoses.Add((SubBlueprints.ParseRef(hose[0], file, -1), SubBlueprints.ParseRef(hose[1], file, -1)));
                    }
                    break;
                case "connectors":
                    foreach (var cn in p.Value.EnumerateArray())
                        bp.Connectors.Add(ConstructWorld.ParseConnector(cn, file));
                    break;
                case "noCollide":
                    foreach (var nc in p.Value.EnumerateArray())
                        bp.NoCollide.Add(ConstructWorld.ParseNoCollide(nc, file));
                    break;
                case "tracks":
                    foreach (var tk in p.Value.EnumerateArray())
                        bp.Tracks.Add(ConstructTracks.Parse(tk, file, bp.Tracks.Count));
                    break;
                case "exactLinks":
                    bp.ExactLinks = p.Value.ValueKind == JsonValueKind.True;
                    break;
                case "frozenBase":
                    bp.FrozenBase = p.Value.ValueKind == JsonValueKind.True;
                    break;
                case "conduit":
                    bp.Conduit = p.Value.ValueKind == JsonValueKind.True;
                    break;
                case "frozenJoints":
                    foreach (var fj in p.Value.EnumerateArray())
                        bp.FrozenJoints.Add(fj.GetInt32());
                    break;
                case "carry":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() > 8)
                        throw new ConstructPartException($"{file}: \"carry\" lists up to 8 craft");
                    foreach (var cy in p.Value.EnumerateArray())
                        bp.Carry.Add(ConstructCargo.ParseCarry(cy, file, bp.Carry.Count));
                    break;
                case "tow":
                    bp.Tow = p.Value.GetString() ?? "";
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
                case "stored":
                    if (p.Value.ValueKind != JsonValueKind.Array)
                        throw new ConstructPartException($"{file}: \"stored\" is [{{\"step\", \"slot\", \"part\", \"count\"}}]");
                    foreach (var sd in p.Value.EnumerateArray())
                        bp.Stored.Add(ConstructLoot.ParseStored(sd, file, bp.Stored.Count));
                    break;
                case "cargo":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() > ConstructCargo.MaxPerConstruct)
                        throw new ConstructPartException($"{file}: \"cargo\" lists up to {ConstructCargo.MaxPerConstruct} loose parts");
                    foreach (var cg in p.Value.EnumerateArray())
                        bp.Cargo.Add(ConstructCargo.ParseSpec(cg, file, bp.Cargo.Count));
                    break;
                case "id":
                    bp.Id = p.Value.ValueKind == JsonValueKind.String && SubBlueprints.IdRefusal(p.Value.GetString() ?? "") == null ? p.Value.GetString()! : throw new ConstructPartException($"{file}: \"id\" is 16 hex digits");
                    break;
                case "version":
                    bp.Version = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var ver) && ver >= 1 ? ver : throw new ConstructPartException($"{file}: \"version\" is 1 or more");
                    break;
                case "uses":
                    SubBlueprints.ParseUses(p.Value, file, bp);
                    break;
                case "module":
                    bp.Module = ModuleBlueprints.Parse(p.Value, file);
                    break;
                case "packed":
                    bp.Packed = SubBlueprints.ParsePacked(p.Value, file);
                    break;
                case "bill":
                    if (p.Value.ValueKind != JsonValueKind.Array)
                        throw new ConstructPartException($"{file}: \"bill\" is [{{\"step\", \"piece\", \"count\"}}]");
                    foreach (var bl in p.Value.EnumerateArray())
                        bp.Bill.Add(ConstructBill.ParseLine(bl, file, bp.Bill.Count));
                    break;
                default:
                    throw new ConstructPartException($"{file}: unknown key \"{p.Name}\" (format, name, description, group, id, version, exactLinks, steps, uses, module, packed, wires, links, connectors, cargo, loot, stored, bill)");
            }
        if (bp.Name.Length == 0 || bp.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be plain ASCII text");
        if (bp.Steps.Count == 0)
            throw new ConstructPartException($"{file}: \"steps\" is empty");
        SubBlueprints.CheckRefs(bp, file); // (a step naming ["uN", k] needs that use; none without "uses")
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
                case "on": on = SubBlueprints.ParseRef(f.Value, file, k); break;
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
    public static string WeldJson(int on, Vector3 pos, Quaternion rot) => WeldJson(on.ToString(CultureInfo.InvariantCulture), pos, rot);

    /// <summary>The same with its "on" as text (a sub-blueprint use's step: ["uN", k]).</summary>
    public static string WeldJson(string on, Vector3 pos, Quaternion rot) =>
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

    /// <summary>A saved step's other links to earlier steps; the reason one can't be made, or null.</summary>
    private static string? LinkAlso(ConstructWorld world, Construct c, Blueprint bp, int k, int[] partOf)
    {
        if (k >= bp.StepAlso.Count || bp.StepAlso[k] is not { } also)
            return null;
        foreach (var (on, socket, own) in also)
        {
            if (on < 0 || on >= k)
                return $"step {k}: \"also\" must name earlier steps";
            var theirs = ConstructMerge.SocketIndex(c, partOf[on], socket);
            var mine = Array.FindIndex(c.DefOf(partOf[k]).Sockets, s => s.Name == own);
            if (theirs < 0 || mine < 0 || !world.AddLink(c, partOf[on], theirs, partOf[k], mine))
                return $"step {k}: no link to step {on} from \"{own}\" to \"{socket}\"";
        }
        return null;
    }

    /// <summary>
    /// One step of a blueprint onto <paramref name="c"/> by the game's own rules (its socket, turn, "at", weld, "also" links, setting
    /// and config): <see cref="BuildSteps"/>' step, and the Building Station's, which fits steps in any order once a step's parent
    /// (and the steps its "also" names) are in <paramref name="partOf"/> (ConstructStation.cs). The reason it refuses, or null.
    /// </summary>
    public static string? FitStep(ConstructWorld world, Construct c, Blueprint bp, int k, int[] partOf) => FitStep(world, c, bp, k, partOf, false);

    /// <summary>
    /// <see cref="FitStep"/>; <paramref name="deferWeld"/>: a freeform weld step leaves its rebuild to the caller, who does it before the
    /// next step that goes on a jointed or welded segment (<see cref="BuildSteps"/>: a works with 92 welds rebuilt its 6,000 parts 92 times).
    /// </summary>
    private static string? FitStep(ConstructWorld world, Construct c, Blueprint bp, int k, int[] partOf, bool deferWeld)
    {
        var why = FitStepCore(world, c, bp, k, partOf, deferWeld);
        if (why == null && bp.Map != null)
            SubBlueprints.TagPart(c, bp.Map, k, partOf[k]); // (a use's part carries its use: the selector's frames, a save's uses)
        return why;
    }

    private static string? FitStepCore(ConstructWorld world, Construct c, Blueprint bp, int k, int[] partOf, bool deferWeld)
    {
        var (name, on, sock, own, spin) = bp.Steps[k];
        if (ConstructParts.Find(name) is not { } def)
        {
            return $"step {k}: no part named \"{name}\"";
        }
        if (k == 0)
        {
            partOf[0] = world.AddPart(c, def, Vector3.Zero, (byte)(Math.Max(spin, 0) % Rot24.Count));
            SetSetting(c, partOf[0], def, bp.StepSettings[0]);
            SetConfig(world, c, partOf[0], def, bp, 0);
            return null;
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
                return $"step {k}: too many parts";
            }
            if (!Exact(bp, k))
                AutoLink(world, c, partOf[k]);
            if (LinkAlso(world, c, bp, k, partOf) is { } lost)
            {
                return lost;
            }
            SetSetting(c, partOf[k], def, bp.StepSettings[k]);
            SetConfig(world, c, partOf[k], def, bp, k);
            return null;
        }
        if (k < bp.StepWeld.Count && bp.StepWeld[k] is { } weld)
        {
            // A freeform weld (board item fw01): the part on a new rigid segment at its stored pose.
            partOf[k] = world.AddFreeform(c, target, def, weld.Pos, weld.Rot);
            if (partOf[k] < 0)
            {
                return $"step {k}: too many parts or freeform welds";
            }
            if (LinkAlso(world, c, bp, k, partOf) is { } lost)
            {
                return lost;
            }
            SetSetting(c, partOf[k], def, bp.StepSettings[k]);
            SetConfig(world, c, partOf[k], def, bp, k);
            if (deferWeld)
                c.FreeformCount = CountFreeform(c); // (what the rebuild would count: the overlap tests compare by frame from now on)
            else
                c.Rebuild(); // (the next step may fit a part onto the new segment: frames are cached by the rebuild, board item fd01)
            return null;
        }
        if (def.Role == PartRole.GunPart)
            ConstructMerge.Refresh(c); // (a gun part's place scales with a merged receiver or a scaled part under it: found as the rebuild would)
        var ts = ConstructMerge.SocketIndex(c, target, sock);
        var ds = Array.FindIndex(def.Sockets, s => s.Name == own);
        if (ts < 0 || ds < 0)
        {
            return $"step {k}: {(ts < 0 ? $"{td.Name} has no socket \"{sock}\"" : $"{def.Name} has no socket \"{own}\"")}";
        }
        if (spin < 0)
            spin = Math.Max(0, SmartRotation.BestSpin(c, target, ts, def, ds)); // (a step whose turn is "auto")
        if (Place(c, target, ts, def, ds, spin, out var g) is { } bad && !(laidAt != null && bad == InTheWay))
        {
            // (which part: "something is in the way" sent a designer looking at the seam when it was the frame under the first panel)
            var what = bad == InTheWay && BlockerOf(c, def, g.Pos, g.Rot, g.Scale) is var bi and >= 0 ? $"the {c.DefOf(bi).Name} (step {Array.IndexOf(partOf, bi)}) is in the way" : bad;
            return $"step {k}: {def.Name} on {td.Name} \"{sock}\": {what}";
        }
        if (laidAt is { } stands)
            g.Pos = ConstructWorld.Quantise(stands);
        partOf[k] = Attach(world, c, def, g, !Exact(bp, k));
        if (partOf[k] < 0)
        {
            return $"step {k}: too many parts";
        }
        if (LinkAlso(world, c, bp, k, partOf) is { } missing)
        {
            return missing;
        }
        if (!SetSetting(c, partOf[k], def, bp.StepSettings[k]))
        {
            return $"step {k}: a {def.Name} has no setting {bp.StepSettings[k]}";
        }
        SetConfig(world, c, partOf[k], def, bp, k);
        return null;
    }

    /// <summary>
    /// Does step <paramref name="k"/> read anything a rebuild sets (so a weld's put-off rebuild runs first)? A step on a jointed or welded
    /// segment (its frames), a turn chosen by the game ("auto"), and every part whose placing checks read the rebuilt body: modules and
    /// their sockets, lift cells, burners, volumes, shell pods, magazines, gun parts and receivers, and what hangs on a joint.
    /// Plain structure on the body's own segment (the works' thousands of blocks, slabs and sheets) needs none.
    /// </summary>
    private static bool WantsRebuild(Construct c, Blueprint bp, int k, int[] partOf)
    {
        var (name, on, _, _, spin) = bp.Steps[k];
        if ((uint)on >= (uint)k || spin < 0 || ConstructParts.Find(name) is not { } d)
            return true;
        var target = partOf[on];
        if (c.Parts[target].Seg != 0)
            return true;
        var td = c.DefOf(target);
        return d.ModPart != ModPart.None || td.ModPart != ModPart.None || d.IsEnvelope || d.Role is PartRole.Burner or PartRole.GunPart or PartRole.Hinge or PartRole.Door
            || d.IsReceiverGun || td.IsReceiverGun || td.HasJoint || d.HasJoint || d.IsTankVolume || d.IsVolumeStore || ConstructCargo.IsVolume(d) || d.ShellPod != null
            || ConstructAmmo.IsMagazine(d);
    }

    private static int CountFreeform(Construct c)
    {
        var n = 0;
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform)
                n++;
        return n;
    }

    /// <summary>Does step <paramref name="k"/> link only as the blueprint says (a flattened use may link the other way: <see cref="Blueprint.FlipExact"/>)?</summary>
    public static bool Exact(Blueprint bp, int k) => bp.ExactLinks != (bp.FlipExact?.Contains(k) ?? false);

    /// <summary>What <see cref="BuildSteps"/> came to: the construct, or the reason a step refused.</summary>
    public sealed class BuildRun
    {
        public Construct? Built;
        public string Why = "";
        public bool KeepPartOf; // the caller wants each step's part
        public int[]? PartOf;
        public bool StepsOnly; // stop after the steps (GroundJob): Body and PartOf hold them, FinishBuild does the rest
        public Construct? Body;
    }

    /// <summary>
    /// <see cref="Build"/> a step at a time, for a blueprint too big for one tick (a stamp: ConstructEditsSelect.cs): each
    /// item is one step done; the result lands in <paramref name="run"/> when the sequence ends.
    /// </summary>
    public static IEnumerable<int> BuildSteps(ConstructWorld world, Blueprint bp, Vector3 pos, Quaternion rot, BuildRun run)
    {
        if (bp.Uses.Count > 0 && bp.Map == null)
        {
            run.Why = "it uses other blueprints: it is built from its flat form (SubBlueprints.Flatten)";
            yield break;
        }
        var c = world.Create(pos, rot);
        if (c == null)
        {
            run.Why = "too many constructs";
            yield break;
        }
        var partOf = new int[Math.Max(1, bp.Steps.Count)];
        bool weldRebuild = false, lastWeld = false; // (a weld step's rebuild, put off until a step needs it)
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            if (weldRebuild && k > 0 && WantsRebuild(c, bp, k, partOf))
            {
                c.Rebuild(); // (this step reads what a rebuild leaves: as the weld's own rebuild had it)
                weldRebuild = false;
            }
            if (FitStep(world, c, bp, k, partOf, true) is { } refused)
            {
                run.Why = refused;
                world.Remove(c);
                yield break;
            }
            lastWeld = k < bp.StepWeld.Count && bp.StepWeld[k] != null;
            weldRebuild |= lastWeld;
            yield return k;
        }
        if (lastWeld)
            c.Rebuild(); // (a weld last: the body left rebuilt, as the weld's own rebuild left it)
        if (run.StepsOnly)
        {
            run.Body = c; // (the rest goes on in the world it joins: FinishBuild)
            run.PartOf = partOf;
            yield break;
        }
        FinishBuild(world, c, bp, partOf, run);
    }

    /// <summary>
    /// What follows a blueprint's steps (paint, wires, routed links, hoses, ropes, tracks, what was frozen, the rebuild, its
    /// cargo): in the world the construct stays in, all at once. <see cref="BuildRun.Built"/> is set when it built; on a refusal
    /// the construct (and any cargo made) goes.
    /// </summary>
    public static void FinishBuild(ConstructWorld world, Construct c, Blueprint bp, int[] partOf, BuildRun run, bool linked = false)
    {
        for (var k = 0; k < bp.Steps.Count && k < bp.StepPaint.Count; k++)
            if (bp.StepPaint[k] != 0)
                c.Parts[partOf[k]].Paint = bp.StepPaint[k]; // (the paint tool's colours; a part with nothing to paint just carries it)
        for (var k = 0; k < bp.Steps.Count && k < bp.StepFinish.Count; k++)
            if (bp.StepFinish[k] != 0)
                c.Parts[partOf[k]].Finish = bp.StepFinish[k]; // (the texture paint tool's materials, ConstructFinish.cs)
        ConstructFuse.Renumber? units = null;
        for (var k = 0; k < bp.Steps.Count && k < bp.StepFuse.Count; k++)
            if (bp.StepFuse[k] != 0)
            {
                c.Parts[partOf[k]].Fuse = (units ??= new ConstructFuse.Renumber()).Tag(bp.StepFuse[k]); // (a unit of its own: new tags each build)
                c.Dirty = true;
            }
        // The wires, through the linking tool's rules.
        if (bp.Wires.Count > 0 && c.Dirty)
            c.Rebuild(); // (the wires' checks read the rebuilt body: once before them and once after, not once a wire)
        var conduit = !linked && (c.Anchored || bp.FrozenBase || bp.Conduit); // (a pre-built: its wires are laid as conduit along the walls, ConstructConduit.cs)
        var conduitEnd = conduit && ConstructConduit.Any(bp) ? ConstructConduit.Deadline() : 0;
        if (!linked) // (a Building Station laid the wires and links itself, one by one: ConstructStation.cs)
        for (var k = 0; k < bp.Wires.Count; k++)
        {
            var (from, to) = bp.Wires[k];
            if (from < 0 || from >= bp.Steps.Count || to < 0 || to >= bp.Steps.Count)
            {
                run.Why = $"wire {from} -> {to}: no such step";
                world.Remove(c);
                return;
            }
            if (CanWire(world, c, partOf[from], partOf[to]) is { } bad)
            {
                run.Why = $"wire {from} -> {to} ({c.DefOf(partOf[from]).Name} to {c.DefOf(partOf[to]).Name}): {bad}";
                world.Remove(c);
                return;
            }
            if (!conduit || !ConstructConduit.TryLay(world, c, bp, k, partOf[from], partOf[to], ConstructWorld.WirePort(c, partOf[from], partOf[to]), conduitEnd))
                world.AddWire(c, partOf[from], partOf[to], rebuild: false);
        }
        if (bp.Wires.Count > 0 && c.Dirty)
            c.Rebuild();
        // The routed links, through the linking tool's rules.
        for (var k = 0; k < bp.Links.Count && !linked; k++)
            if (ConstructRoutes.BuildLink(world, c, bp.Links[k], partOf.AsSpan(0, bp.Steps.Count), conduit && bp.Conduit ? bp : null, bp.Wires.Count + k, conduitEnd) is { } bad)
            {
                run.Why = $"links[{k}]: {bad}";
                world.Remove(c);
                return;
            }
        foreach (var (reel, inlet) in bp.Hoses)
            if (reel >= 0 && reel < bp.Steps.Count && inlet >= 0 && inlet < bp.Steps.Count)
                world.PlugHose(c, partOf[reel], c, partOf[inlet]);
        for (var k = 0; k < bp.Connectors.Count; k++)
            if (world.BuildConnector(c, bp.Connectors[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"connectors[{k}]: {bad}";
                world.Remove(c);
                return;
            }
        for (var k = 0; k < bp.NoCollide.Count; k++)
            if (world.BuildNoCollide(c, bp.NoCollide[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"noCollide[{k}]: {bad}";
                world.Remove(c);
                return;
            }
        // Fuel line parts and plain drive shafts become pipe and shaft links (blueprints auto-converted).
        ConstructRoutes.MigrateLegacy(world, c);
        for (var k = 0; k < bp.Tracks.Count; k++)
            if (ConstructTracks.Build(c, bp.Tracks[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"tracks[{k}]: {bad}";
                world.Remove(c);
                return;
            }
        // What was frozen when it was saved.
        foreach (var step in bp.FrozenJoints)
            if (step >= 0 && step < bp.Steps.Count && c.JointOf(partOf[step]) is var fj and >= 0)
                for (var j = fj; j < c.JointCount && c.Joints[j].Hinge == partOf[step] && !c.Joints[j].Freeform; j++)
                    c.Joints[j].Frozen = true;
        if (bp.FrozenBase)
            c.Frozen = c.Anchored = true;
        c.Rebuild();
        // Its Capacitor Banks come charged, as its magazines come full (Karl, 2026-11-17: a ship the game spawns is ready to fight);
        // a Building Station's build (linked) is a player's, whose banks come empty and fill from its circuit.
        if (!linked)
            c.Cap = c.CapCapacity;
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
                return;
            }
        }
        // The craft it carries in its docks (a Building Station's build carries none: a player builds those apart).
        if (bp.Carry.Count > 0 && !linked)
        {
            var made = new List<Construct>(bp.Carry.Count);
            if (ConstructCargo.BuildCarry(world, c, bp.Carry, made) is { } bad)
            {
                run.Why = bad;
                foreach (var m in made)
                    world.Remove(m);
                world.Remove(c);
                return;
            }
        }
        for (var k = 0; k < bp.Loot.Count; k++)
            if (bp.Loot[k].Step < 0 || bp.Loot[k].Step >= bp.Steps.Count || c.DefOf(partOf[bp.Loot[k].Step]).Slots.Length == 0)
            {
                run.Why = $"loot[{k}]: step {bp.Loot[k].Step} isn't a container with slots";
                world.Remove(c);
                return;
            }
        for (var k = 0; k < bp.Stored.Count; k++)
            if (bp.Stored[k].Step < 0 || bp.Stored[k].Step >= bp.Steps.Count || (bp.Stored[k].Slot < 0 || bp.Stored[k].Slot >= c.DefOf(partOf[bp.Stored[k].Step]).Slots.Length) && !c.DefOf(partOf[bp.Stored[k].Step]).IsPieceRack)
            {
                run.Why = $"stored[{k}]: step {bp.Stored[k].Step} has no slot {bp.Stored[k].Slot}";
                world.Remove(c);
                return;
            }
        for (var k = 0; k < bp.Bill.Count; k++)
            if (ConstructBill.FromLine(world, c, bp.Bill[k], partOf.AsSpan(0, bp.Steps.Count)) is { } bad)
            {
                run.Why = $"bill[{k}]: {bad}";
                world.Remove(c);
                return;
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

    /// <summary>A step's "config" (the part's settings), after its "setting"; and its "text".</summary>
    private static void SetConfig(ConstructWorld world, Construct c, int part, ConstructPartDef def, Blueprint bp, int step)
    {
        if (part >= 0 && def.Air != AirPart.None && (world.Creative || !world.PlayerBuild))
            c.Parts[part].Rounds = def.AirHolds; // (a ship the game spawns comes with its air, as with its magazines; a Building Station's build comes empty: ShipAir.cs)
        if (part >= 0 && step < bp.StepText.Count && bp.StepText[step] is { } texts)
            for (var k = 0; k < texts.Length && k < def.TextSlots; k++)
                LogicText.Set(ref c.Parts[part], k, texts[k]);
        if (part >= 0 && def.IsMedia && step < bp.StepMedia.Count && bp.StepMedia[step] is { } lines)
            ConstructMedia.SetList(ref c.Parts[part], lines); // (bad lines are left out: a blueprint is someone's file)
        if (part >= 0 && bp.OrdersOnly.Contains(step) && WorkOrders.TakesOrders(def) && c.Parts[part].Orders == null)
        {
            c.Parts[part].Orders = new OrderList(); // ("ordersOnly": an empty queue, so it waits for orders and never works its own settings)
            world.OrdersChanged(c);
        }
        if (part < 0 || step >= bp.StepConfig.Count || bp.StepConfig[step] is not { } values)
            return;
        // A ship the game spawns in survival comes with its magazines full (Karl, 2026-11-17): a store the blueprint fills with ammo is
        // filled in any world but by a player's build (a Building Station's fit: PlayerBuild), which comes empty and is filled by hand.
        var magazine = !world.Creative && !world.PlayerBuild && def.IsVolumeStore && FillsAmmo(def, values);
        foreach (var (slot, v) in values)
            if (world.Creative || magazine || !def.Config[slot].CreativeOnly) // (a survival world builds it as it comes: an empty tank, whatever the blueprint says)
                ConstructConfig.Set(ref c.Parts[part], def, slot, v);
        ConstructConfig.StartState(ref c.Parts[part], def);
        if (ConstructStoreVolume.HasContents(def) && (world.Creative || magazine))
            ConstructStoreVolume.SetContents(world, c, part, limit: false); // (its walls may not be up yet: an open box spills what it must later)
    }

    /// <summary>Does a store's step set its Material to bulk ammo?</summary>
    private static bool FillsAmmo(ConstructPartDef def, List<(int Slot, float Value)> values)
    {
        var slot = def.CfgSlot[(int)CfgKey.Material];
        var ammo = RecyclingTable.Current.IdOf("ammo");
        foreach (var (s, v) in values)
            if (s == slot && ammo != 0 && (int)v == ammo)
                return true;
        return false;
    }

    /// <summary>
    /// A blueprint's construct set down standing on the ground at <paramref name="ground"/> (its lowest point there),
    /// upright along the local up there (a planetoid: away from its centre; <paramref name="yaw"/> turns it about that up).
    /// All at once: a big blueprint costs a tick 10 to 20 ms, so the game's spawns of big ones go through <see cref="GroundJob"/>.
    /// </summary>
    public static Construct? BuildOnGround(ConstructWorld world, Blueprint bp, Vector3 ground, float yaw, out string why, Blueprint? tow = null)
    {
        var rot = GroundRot(ground, yaw);
        var run = new BuildRun { KeepPartOf = bp.Loot.Count > 0 || bp.Stored.Count > 0 };
        foreach (var _ in BuildSteps(world, bp, ground, rot, run))
        {
        }
        why = run.Why;
        var c = run.Built;
        if (c == null)
            return null;
        SetDown(world, c, bp, run.PartOf, ground, rot);
        if ((tow ?? (bp.Tow.Length > 0 ? FindBlueprint(bp.Tow) : null)) is { } behind)
            SpawnTow(world, c, behind, ground, yaw, rot, null);
        return c;
    }

    /// <summary>Upright on the ground there, turned by <paramref name="yaw"/> about its up.</summary>
    internal static Quaternion GroundRot(Vector3 ground, float yaw) =>
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationY(-yaw) * GravityFrames.Basis(ground)));

    /// <summary>
    /// A built construct's lowest point in its own frame (rebuilt first if it must be): its parts' bounds, or the wheels' bottoms, or a
    /// walker's resting feet when enough fed legs hold it. <see cref="SetDown"/> stands it there; the blueprint ghost rests on it too.
    /// </summary>
    internal static float LowOf(ConstructWorld world, Construct c)
    {
        if (c.Dirty)
            c.Rebuild(); // (its bounds, wheels and legs as built: the first tick would rebuild it anyway)
        var low = c.LocalMin.Y;
        for (var k = 0; k < c.WheelCount; k++)
            low = MathF.Min(low, c.Wheels[k].Centre.Y - c.Wheels[k].Travel * 0.5f - c.Wheels[k].Radius);
        if (c.LegCount > 0 && ConstructPhysics.PowerLegs(world, c, false) >= ConstructPhysics.MinWalkLegs)
            for (var k = 0; k < c.LegCount; k++)
                low = MathF.Min(low, c.Legs[k].Rest.Y);
        return low;
    }

    /// <summary>A built construct's loot rolled and its lowest point stood on the ground (its cargo moves with it).</summary>
    private static void SetDown(ConstructWorld world, Construct c, Blueprint bp, int[]? partOf, Vector3 ground, Quaternion rot)
    {
        if (partOf != null && bp.Stored.Count > 0)
            ConstructLoot.Stow(world, c, bp, partOf); // (what its maker stowed, before the loot rolls into what is left)
        if (partOf != null && bp.Loot.Count > 0)
            ConstructLoot.Fill(world, c, bp, partOf); // (a spawn rolls fresh; a scratch build or a stamp doesn't)
        // Lowest point: the parts' bounds, or the wheels' bottoms, or a walker's resting feet (docs/sandbox/GIANTS.md, "Walking
        // on real maps"): one with enough fed legs to hold it is set down standing, its hull clear of the ground and of what
        // stands on it, so it walks from the first tick instead of standing up from its hips.
        var low = LowOf(world, c);
        c.Pos = ground - Vector3.Transform(new Vector3(0, low - 0.02f, 0), rot);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        c.NetHistReset();
        if (bp.Cargo.Count > 0 || bp.Carry.Count > 0)
            ConstructCargo.FollowAll(world); // (its cargo moves with it)
    }

    /// <summary>
    /// A towed blueprint, spawned with its first eye on the tower's first ball and coupled. A tower with no ball, or a trailer
    /// with no eye, stands alone: the blueprint is at fault, not the spawn. <paramref name="built"/>: its steps already done (GroundJob).
    /// </summary>
    private static void SpawnTow(ConstructWorld world, Construct c, Blueprint bp, Vector3 ground, float yaw, Quaternion rot, GroundJob? built)
    {
        var ball = HitchOf(c, HitchKind.Ball);
        if (ball < 0)
            return;
        var at = ground + Vector3.Transform(new Vector3(0, 0, c.LocalMax.Z + 6), rot);
        var t = built != null ? built.Join(at, yaw, out _) : BuildOnGround(world, bp, at, yaw, out _);
        var eye = t == null ? -1 : HitchOf(t, HitchKind.Eye);
        if (t == null || eye < 0)
            return;
        var gap = ConstructWorld.HitchPoint(c, ball) - ConstructWorld.HitchPoint(t, eye);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        t.Pos += gap - up * Vector3.Dot(gap, up); // (along the ground: each stands on its own wheels)
        t.PrevPos = t.NetPos = t.NetFromPos = t.Pos;
        t.NetHistReset();
        if (world.CanCouple(c, ball, t, eye) == null)
            world.Couple(c, ball, t, eye);
    }

    /// <summary>
    /// <see cref="BuildOnGround"/> over several ticks (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn"): the steps run a slice
    /// at a time (<see cref="Step"/>) on a world of their own, which nothing simulates, saves or sends, and the construct
    /// joins the real world whole (<see cref="Join"/>, on one tick: what follows the steps, its loot and its tow), the same
    /// construct <see cref="BuildOnGround"/> makes. Where it stands is chosen only at the join, so a spawn in front of a
    /// player goes where they are then. A blueprint that tows something builds the tow's steps next, the same way.
    /// </summary>
    public sealed class GroundJob
    {
        public readonly ConstructWorld World;
        public readonly Blueprint Bp;
        private readonly Blueprint? _tow;
        private readonly ConstructWorld _scratch;
        private readonly BuildRun _run = new() { StepsOnly = true };
        private IEnumerator<int>? _steps;
        private GroundJob? _towJob;
        private bool _ready, _joined;

        /// <summary>Its steps (and its tow's) are done: <see cref="Join"/> may be called.</summary>
        public bool Ready => _ready;

        /// <summary>The body its steps built, in its scratch world (once ready; null when they refused): the blueprint ghost measures it.</summary>
        public Construct? Body => _ready ? _run.Body : null;

        /// <summary>The scratch world it was built in (its body's legs are fed there).</summary>
        public ConstructWorld Scratch => _scratch;

        /// <summary>Why its steps refused (once <see cref="Step"/> is true), or "".</summary>
        public string Why = "";

        /// <summary>Each step's part on the body it built (once its steps are done; null before), as <see cref="Build"/>'s <c>partOf</c>.</summary>
        public int[]? PartOf => _run.PartOf;

        /// <summary>What it cost: the slices' time, the longest, how many (the tow's included).</summary>
        public double CpuMs, WorstSliceMs;
        public int Slices;

        public GroundJob(ConstructWorld world, Blueprint bp, Blueprint? tow = null)
        {
            World = world;
            Bp = bp;
            _tow = tow ?? (bp.Tow.Length > 0 ? FindBlueprint(bp.Tow) : null);
            _scratch = new ConstructWorld { CreativeRecycling = world.CreativeRecycling }; // (creative-only settings keep or go as the real world's would)
        }

        /// <summary>Its steps, and its tow's, for about <paramref name="budgetMs"/> (at least one step). True once they are done (or refused: <see cref="Why"/>).</summary>
        public bool Step(double budgetMs)
        {
            if (_ready)
                return true;
            var start = Stopwatch.GetTimestamp();
            var until = start + (long)(budgetMs * Stopwatch.Frequency / 1000);
            do
            {
                if (_towJob != null)
                {
                    _ready = _towJob.Step(Math.Max(0, (until - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency)); // (a tow that refuses stays behind, as in BuildOnGround)
                    continue;
                }
                _steps ??= BuildSteps(_scratch, Bp, Vector3.Zero, Quaternion.Identity, _run).GetEnumerator();
                if (_steps.MoveNext())
                    continue;
                _steps.Dispose();
                if (_run.Body is not { } body)
                {
                    Why = _run.Why;
                    _ready = true;
                }
                else if (_tow != null && HitchOf(body, HitchKind.Ball) >= 0)
                    _towJob = new GroundJob(World, _tow);
                else
                    _ready = true;
            } while (!_ready && Stopwatch.GetTimestamp() < until);
            var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            CpuMs += ms;
            WorstSliceMs = Math.Max(WorstSliceMs, ms);
            Slices++;
            return _ready;
        }

        /// <summary>
        /// Once <see cref="Step"/> is true: the construct joins <see cref="World"/> standing on the ground at
        /// <paramref name="ground"/> facing <paramref name="yaw"/>, as <see cref="BuildOnGround"/> would set it down. Null and why when it can't.
        /// </summary>
        public Construct? Join(Vector3 ground, float yaw, out string why)
        {
            if (!_ready || _joined)
                throw new InvalidOperationException(_joined ? "a ground build joins once" : "its steps aren't done");
            _joined = true;
            if (_run.Body is not { } body)
            {
                why = Why;
                return null;
            }
            var rot = GroundRot(ground, yaw);
            var c = World.Adopt(_scratch, body, ground, rot, out why);
            if (c == null)
            {
                if (why != ConstructWorld.AdoptUnusual)
                    return null;
                Console.WriteLine($"[build] {Bp.Name}: {why}: built again at once");
                return BuildOnGround(World, Bp, ground, yaw, out why, _tow); // (what a vehicle never holds: correct, at a tick's cost)
            }
            var partOf = _run.PartOf!;
            _run.Why = "";
            FinishBuild(World, c, Bp, partOf, _run);
            why = _run.Why;
            if (_run.Built == null)
                return null;
            SetDown(World, c, Bp, partOf, ground, rot);
            if (_towJob != null)
                SpawnTow(World, c, _tow!, ground, yaw, rot, _towJob);
            return c;
        }
    }

    private static int HitchOf(Construct c, HitchKind kind)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Role == PartRole.Hitch && c.DefOf(i).Hitch == kind)
                return i;
        return -1;
    }
}
