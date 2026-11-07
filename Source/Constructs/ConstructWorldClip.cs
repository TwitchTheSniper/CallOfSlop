using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Whole groups of parts (docs/sandbox/CONSTRUCTS.md, "Build stations"): a copy of some parts into a new body of any world, as
/// new parts (the Selector Station's copy, a stamped blueprint), and a cut of some parts out of a construct, as they are (its
/// move). Both go through the normal paths: the same part, link, joint and route tables, and the same splitting.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>
    /// The parts <paramref name="order"/> lists, of any construct of any world, as new parts of a new body of this world at
    /// the pose given: part k of the new body is part order[k]. What a player designed comes along (turn, settings, paint, the
    /// links, joints and routed links wholly among them); what the parts have been through doesn't (health, rounds, what they
    /// held). Parts and routed nodes keep their numbers in the source's rest frame. Null, with <paramref name="why"/>.
    /// </summary>
    public Construct? CopyPiece(Construct src, ReadOnlySpan<int> order, Vector3 pos, Quaternion rot, out string why, ConstructWorld? srcWorld = null)
    {
        why = "";
        if (order.Length == 0 || order.Length > Construct.MaxParts)
        {
            why = "nothing to copy";
            return null;
        }
        var marked = new int[src.PartCount];
        Array.Fill(marked, -1);
        for (var k = 0; k < order.Length; k++)
            marked[order[k]] = k;
        // The frame: the one the first part is in. A joint comes along when its hinge (a weld: both sides) does and it carries something.
        var frame = src.FrameOf(src.Parts[order[0]].Seg);
        Span<int> segMap = stackalloc int[Construct.MaxJoints + 2];
        segMap.Fill(-1);
        segMap[0] = 0;
        if (frame != 0)
            segMap[frame] = 0;
        var carried = new bool[src.JointCount + 2];
        foreach (var i in order)
            carried[src.Parts[i].Seg] = true;
        var c = Create(pos, rot);
        if (c == null)
        {
            why = "the world is full";
            return null;
        }
        for (var j = 0; j < src.JointCount; j++)
        {
            ref var jt = ref src.Joints[j];
            if (j + 1 == frame || !carried[j + 1] && !JointCarries(src, j, carried) || jt.Hinge >= marked.Length || marked[jt.Hinge] < 0 || jt.Freeform && (jt.Cut || jt.Child >= marked.Length || marked[jt.Child] < 0))
                continue;
            if (c.JointCount >= Construct.MaxJoints)
                break;
            c.ReserveJoints(c.JointCount + 1);
            var copy = jt;
            copy.Hinge = (ushort)marked[jt.Hinge];
            if (jt.Freeform)
                copy.Child = (ushort)marked[jt.Child];
            copy.ParentSeg = (byte)Math.Max(0, segMap[jt.ParentSeg]);
            copy.Angle = copy.PrevAngle = copy.Vel = copy.Input = copy.Load = copy.Force = copy.Over = copy.Strain = copy.Ref = copy.AimErr = 0;
            copy.Open = copy.Aimed = copy.RefSet = copy.Cut = false;
            c.Joints[c.JointCount++] = copy;
            segMap[j + 1] = c.JointCount;
        }
        Dictionary<uint, uint>? fuseTags = null; // (a copy's fused units are units of their own: new tags, ConstructFuse.cs)
        foreach (var i in order)
        {
            ref var s = ref src.Parts[i];
            var seg = segMap[s.Seg];
            if (seg < 0)
            {
                if (src.FrameOf(s.Seg) != frame)
                {
                    why = "a freeform-attached piece comes along only with both sides of its attachment";
                    Remove(c);
                    return null;
                }
                seg = 0; // (its joint stayed behind: it comes as it stands at rest)
            }
            var n = AddPart(c, s.D, s.Pos, s.Rot, (byte)seg);
            if (n < 0)
            {
                why = $"a construct holds at most {Construct.MaxParts} parts, and the world {PartCap}";
                Remove(c);
                return null;
            }
            ref var d = ref c.Parts[n];
            d.Setting = s.Setting;
            d.Cfg = s.Cfg.Copy();
            d.Paint = s.Paint;
            d.Finish = s.Finish;
            if (s.Fuse != 0)
            {
                fuseTags ??= new Dictionary<uint, uint>();
                if (!fuseTags.TryGetValue(s.Fuse, out var tag))
                    fuseTags[s.Fuse] = tag = ConstructFuse.NewTag();
                d.Fuse = tag;
            }
            ConstructConfig.StartState(ref d, d.D);
        }
        for (var l = 0; l < src.LinkCount; l++)
        {
            ref var link = ref src.Links[l];
            if (marked[link.A] >= 0 && marked[link.B] >= 0)
                AddLink(c, marked[link.A], link.SA, marked[link.B], link.SB);
        }
        ConstructRoutes.Remap(src, marked, c);
        ConstructTracks.Remap(src, marked, c);
        for (var k = 0; k < c.TrackCount; k++)
            c.Tracks[k].Health = -1; // (a copy's tracks are whole)
        for (var k = 0; k < c.RouteCount; k++)
        {
            // (a copy's links are whole, wherever they ran)
            ref var r = ref c.Routes[k];
            r.EndHealth = ConstructRoutes.Health(r.Type);
            for (var n = 0; n < r.Count; n++)
                c.RouteNodes[r.Start + n].Health = r.EndHealth;
        }
        c.Dirty = true;
        c.Rebuild();
        if (srcWorld != null)
        {
            CopyConnectors(srcWorld, src, marked, c);
            CopyNoCollides(srcWorld, src, marked, c);
        }
        return c;
    }

    private static bool JointCarries(Construct src, int joint, bool[] carried)
    {
        // A joint that carries nothing itself but one further out does (a segment with only a joint part on it).
        for (var k = joint + 1; k < src.JointCount; k++)
            if (src.Joints[k].ParentSeg == joint + 1 && (carried[k + 1] || JointCarries(src, k, carried)))
                return true;
        return false;
    }

    /// <summary>
    /// The parts <paramref name="marks"/> names leave <paramref name="c"/> as they are (contents, health, joints, the links
    /// among them) as one new body, unwrecked and <paramref name="owner"/>'s; whatever that leaves unjoined to the root splits
    /// off as a wreck, as any removal does. Every part marked: the construct itself comes back. Null when the world has no room.
    /// </summary>
    public Construct? CutOut(Construct c, ReadOnlySpan<bool> marks, byte owner)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount && i < marks.Length; i++)
            n += marks[i] ? 1 : 0;
        if (n == 0 || CapRefusal() != null)
            return null;
        if (c.Dirty)
            c.Rebuild();
        if (n >= c.PartCount)
        {
            c.Wreck = false;
            c.Owner = owner;
            return c;
        }
        var keepDriver = c.Driver;
        var comp = new int[c.PartCount];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = i < marks.Length && marks[i] ? 1 : -1;
        var root = c.Root >= 0 && c.Root < c.PartCount && comp[c.Root] != 1 ? c.Root : -1;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (comp[i] != 1 && c.Parts[i].D.Role is PartRole.Seat or PartRole.Foundation)
                root = i;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (comp[i] != 1)
                root = i;
        Flood(c, root, 0, comp);
        var comps = 2;
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        var piece = SplitOff(c, comp, 1);
        for (var k = 2; k < comps; k++)
            SplitOff(c, comp, k);
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (keepDriver != 255 && c.Driver == 255)
            Events.Add($"driver {keepDriver} thrown out");
        if (piece != null)
        {
            piece.Wreck = false;
            piece.Damaged = c.Damaged;
            piece.Owner = owner;
        }
        return piece;
    }

    /// <summary>
    /// A body that came out of a selection in more than one piece splits into one body a piece, each where it is, all as
    /// <paramref name="like"/> is (owner, frozen). Returns how many more bodies that made.
    /// </summary>
    public int SplitPieces(Construct like)
    {
        var first = Bodies.Count;
        Resplit(like, out var pieces);
        for (var k = first; k < Bodies.Count; k++)
        {
            var b = Bodies[k];
            b.Wreck = false;
            b.Damaged = like.Damaged;
            b.Owner = like.Owner;
            b.Frozen = like.Frozen;
            b.FrozenBy = like.FrozenBy;
            b.Vel = b.AngVel = Vector3.Zero;
        }
        return pieces;
    }

    /// <summary><see cref="ConstructBuild.AutoLink"/> for a lot of parts at once on a rebuilt construct: what each touches comes from the part tree, so it stays linear on a big hull.</summary>
    public int AutoLinkMany(Construct c, int from, int to)
    {
        if (c.Dirty)
            c.Rebuild();
        var hits = Construct.PartScratch;
        var added = 0;
        for (var i = from; i < to && i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            c.PartBounds(i, out var mn, out var mx);
            var near = c.PartsInBox(mn - new Vector3(0.05f), mx + new Vector3(0.05f), hits);
            for (var s = 0; s < d.Sockets.Length; s++)
            {
                if (c.SocketUsed(i, s) || d.Sockets[s].Kind != SocketKind.Struct || d.HasJoint && s == d.LeafIndex)
                    continue;
                c.SocketAt(i, s, out var at, out var dir);
                var done = false;
                for (var h = 0; h < near && !done; h++)
                {
                    var j = hits[h];
                    if (j == i || c.Parts[j].Seg != c.Parts[i].Seg)
                        continue;
                    var od = c.DefOf(j);
                    for (var t = 0; t < od.Sockets.Length && !done; t++)
                    {
                        if (od.Sockets[t].Kind != SocketKind.Struct || od.Sockets[t].Class != d.Sockets[s].Class || c.SocketUsed(j, t) || od.HasJoint && t == od.LeafIndex)
                            continue;
                        c.SocketAt(j, t, out var oat, out var odir);
                        if (odir == (dir ^ 1) && Vector3.DistanceSquared(oat, at) < 0.0004f)
                        {
                            AddLink(c, i, s, j, t);
                            added++;
                            done = true;
                        }
                    }
                }
            }
        }
        return added;
    }
}
