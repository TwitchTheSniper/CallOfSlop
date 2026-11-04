using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>Where a group of parts goes: free at a world pose (of the clip's origin), or square onto a free socket of a construct.</summary>
public struct ClipPlace
{
    public bool Attach;
    public Vector3 Pos; // free: the clip's origin in the world
    public Quaternion Rot; // free: its turn
    public ushort Body, Part; // attached: the construct and the part
    public byte Socket; // ...the free socket on it
    public ushort ClipPart; // ...and the clip's part (in the clip's order) and socket that mate it
    public byte ClipSocket, Spin;
}

/// <summary>
/// A group of parts held apart from the world (docs/sandbox/CONSTRUCTS.md, "Build stations"): a construct in a world of its
/// own, in the order a blueprint would build it, its parts' numbers those of the rest frame they came from. Both the Selector
/// Station's copy and the Blueprint Desk's stamp are one of these: <see cref="ConstructWorld.CopyPiece"/> makes it from a
/// selection, <see cref="ConstructBuild.BuildSteps"/> from a blueprint, and a copy of it goes into the real world the same way.
/// The same selection gives the same clip on the server and a client, so the client's ghost is what the server will place.
/// </summary>
public sealed class ConstructClip
{
    /// <summary>The most a blueprint or a clip holds: one construct.</summary>
    public const int MaxParts = Construct.MaxParts;

    public readonly ConstructWorld World = new();
    public Construct Body = null!;
    public Vector3 Origin; // the point the pose places (the middle of its bounds, on the 1 cm grid), in Body's frame
    public Vector3 Min, Max; // its bounds round every part, in Body's frame
    public string Name = "";
    public int LinksLeftOut; // routed links of its source with an end outside the selection

    public int Parts => Body.PartCount;

    private ConstructClip()
    {
    }

    /// <summary>
    /// The clip of <paramref name="items"/> of <paramref name="src"/>. Null, with why. Their order is a walk over their links
    /// from the first base part, each part after one it hangs on, so a blueprint's steps can follow it.
    /// </summary>
    public static ConstructClip? Capture(Construct src, ReadOnlySpan<ushort> items, out string why, ConstructWorld? srcWorld = null)
    {
        var clip = new ConstructClip();
        var body = clip.World.CopyPiece(src, WalkOrder(src, items), Vector3.Zero, Quaternion.Identity, out why, srcWorld); // (ropes and springs with both ends in the selection come along)
        if (body == null)
            return null;
        clip.LinksLeftOut = LeftOut(src, items) - body.RouteCount;
        clip.Set(body);
        return clip;
    }

    /// <summary>The clip of a construct built on a world of its own (a blueprint's build): that construct is the clip.</summary>
    public static ConstructClip Of(Construct body, string name, ConstructWorld? srcWorld = null)
    {
        var clip = new ConstructClip { Name = name };
        var copy = clip.World.CopyPiece(body, WalkOrder(body, AllOf(body)), Vector3.Zero, Quaternion.Identity, out _, srcWorld);
        clip.Set(copy ?? throw new InvalidOperationException("a built construct copies"));
        return clip;
    }

    /// <summary>The routed links on or touching these parts.</summary>
    private static int LeftOut(Construct c, ReadOnlySpan<ushort> items)
    {
        var on = new bool[c.PartCount];
        foreach (var i in items)
            on[i] = true;
        var n = 0;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            var src = r.Source;
            if (r.To < on.Length && on[r.To] || src < on.Length && on[src])
                n++;
        }
        return n;
    }

    private static ushort[] AllOf(Construct c)
    {
        var all = new ushort[c.PartCount];
        for (var i = 0; i < all.Length; i++)
            all[i] = (ushort)i;
        return all;
    }

    private void Set(Construct body)
    {
        Body = body;
        Bounds(body, AllOf(body), out Min, out Max);
        Origin = OriginOf(Min, Max);
    }

    /// <summary>The bounds of some parts in their construct's rest frame (a merged part's whole shape).</summary>
    public static void Bounds(Construct c, ReadOnlySpan<ushort> items, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (var i in items)
        {
            c.PartBounds(i, out var a, out var b);
            min = Vector3.Min(min, a);
            max = Vector3.Max(max, b);
        }
    }

    public static Vector3 OriginOf(Vector3 min, Vector3 max) => ConstructWorld.Quantise((min + max) * 0.5f);

    /// <summary>Where Body's origin goes for the clip's origin to be at <paramref name="pos"/>, turned <paramref name="rot"/>.</summary>
    public static Vector3 BodyPos(Vector3 origin, Vector3 pos, Quaternion rot) => pos - Vector3.Transform(origin, rot);

    /// <summary>
    /// The parts in build order: from the first part on the base frame, then along links: a link is walked from a joint part
    /// into what hangs on it, never back; a freeform weld from its anchor; a part nothing reaches starts a walk of its own.
    /// </summary>
    public static int[] WalkOrder(Construct c, ReadOnlySpan<ushort> items)
    {
        var inSet = new bool[c.PartCount];
        foreach (var i in items)
            inSet[i] = true;
        var order = new int[items.Length];
        var seen = new bool[c.PartCount];
        var n = 0;
        var head = 0;
        // Roots first: the lowest segment, the construct's own root before any other.
        var lowest = int.MaxValue;
        foreach (var i in items)
            lowest = Math.Min(lowest, c.Parts[i].Seg);
        void Walk(int start, int[] into)
        {
            seen[start] = true;
            into[n++] = start;
            while (head < n)
            {
                int at = into[head++];
                for (var e = c.FirstLink(at); e >= 0; e = c.NextLink(e))
                {
                    int o = c.OtherEnd(e);
                    if (!inSet[o] || seen[o] || !Hangs(c, at, o))
                        continue;
                    seen[o] = true;
                    into[n++] = o;
                }
                for (var j = 0; j < c.JointCount; j++)
                {
                    ref var jt = ref c.Joints[j];
                    if (jt.Freeform && !jt.Cut && jt.Hinge == at && jt.Child < inSet.Length && inSet[jt.Child] && !seen[jt.Child])
                    {
                        seen[jt.Child] = true;
                        into[n++] = jt.Child;
                    }
                }
            }
        }
        if (c.Root >= 0 && c.Root < inSet.Length && inSet[c.Root] && c.Parts[c.Root].Seg == lowest)
            Walk(c.Root, order);
        foreach (var i in items)
            if (!seen[i] && c.Parts[i].Seg == lowest)
                Walk(i, order);
        // What only joints reach, with the joint part left behind, or a walk the rule above stopped: each starts its own.
        for (var pass = 0; pass <= c.JointCount && n < order.Length; pass++)
            foreach (var i in items)
                if (!seen[i])
                {
                    Walk(i, order);
                    break;
                }
        return order;
    }

    /// <summary>May a build go from part <paramref name="from"/> to linked part <paramref name="to"/>: the same segment, or down into what hangs on from's joint.</summary>
    private static bool Hangs(Construct c, int from, int to)
    {
        var a = c.Parts[from].Seg;
        var b = c.Parts[to].Seg;
        if (a == b)
            return true;
        return b > 0 && c.Joints[b - 1].Hinge == from;
    }

    // ---- Attaching -----------------------------------------------------------------------------------------------

    /// <summary>
    /// How the clip goes onto a free socket: <paramref name="turn"/> and <paramref name="offset"/> take its body frame into the
    /// target's rest frame. Null, or why not: the same rules as a part going on (kind, class, free, a joint's base).
    /// </summary>
    public string? AttachPose(Construct target, int part, int socket, int clipPart, int clipSocket, int spin, out byte turn, out Vector3 offset)
    {
        turn = 0;
        offset = default;
        if (part < 0 || part >= target.PartCount || socket < 0 || socket >= target.DefOf(part).Sockets.Length)
            return "no such socket";
        if (clipPart < 0 || clipPart >= Body.PartCount || clipSocket < 0 || clipSocket >= Body.DefOf(clipPart).Sockets.Length)
            return "no such socket";
        if (target.Parts[part].Seg != 0)
            return "that part is on a joint: attach to the body";
        if (target.Parts[part].Broken)
            return "that part is wrecked";
        if (target.SocketUsed(part, socket) || Body.SocketUsed(clipPart, clipSocket))
            return "something is already attached there";
        var ts = target.DefOf(part).Sockets[socket];
        var cs = Body.DefOf(clipPart).Sockets[clipSocket];
        if (ts.Kind != SocketKind.Struct || cs.Kind != SocketKind.Struct)
            return "those don't fit together";
        if (ts.Class != cs.Class)
            return $"that's a {ConstructParts.ClassNames[ts.Class]} socket and this a {ConstructParts.ClassNames[cs.Class]} one";
        target.SocketAt(part, socket, out var at, out var dir);
        Body.SocketAt(clipPart, clipSocket, out var own, out var ownDir);
        Span<byte> turns = stackalloc byte[4];
        var n = ConstructBuild.SpinTurns(ownDir, dir, turns);
        if (n == 0)
            return "no turn fits";
        turn = turns[((spin % n) + n) % n];
        offset = ConstructWorld.Quantise(at - Rot24.Apply(turn, own));
        return null;
    }

    /// <summary>Does any part land in one of <paramref name="target"/>'s (its body frame's parts, a centimetre of slack, skipping marked parts)? <paramref name="from"/>: the clip part to look at next; stops after <paramref name="count"/>.</summary>
    public bool Overlaps(Construct target, byte turn, Vector3 offset, bool[]? skip = null, int from = 0, int count = int.MaxValue)
    {
        var hits = Construct.PartScratch;
        for (var i = from; i < Body.PartCount && i - from < count; i++)
        {
            var d = Body.DefOf(i);
            ref var p = ref Body.Parts[i];
            var pos = offset + Rot24.Apply(turn, p.Pos);
            var rot = Rot24.Compose(turn, p.Rot);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                var bc = pos + Rot24.Apply(rot, d.Boxes[b].Center);
                var bh = Rot24.ApplyHalf(rot, d.Boxes[b].Half) - new Vector3(0.01f);
                var near = target.PartsInBox(bc - bh, bc + bh, hits);
                for (var h = 0; h < near; h++)
                {
                    var o = hits[h];
                    if (skip != null && o < skip.Length && skip[o] || target.Parts[o].Seg != 0)
                        continue;
                    var od = target.DefOf(o);
                    for (var k = 0; k < od.SolidBoxes; k++)
                    {
                        target.BoxOf(o, k, out var oc, out var oh);
                        if (MathF.Abs(bc.X - oc.X) < bh.X + oh.X && MathF.Abs(bc.Y - oc.Y) < bh.Y + oh.Y && MathF.Abs(bc.Z - oc.Z) < bh.Z + oh.Z)
                            return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>A part's place as free placement sees it: the pose of Body given where the clip's origin goes.</summary>
    public FreePlacement PartPlace(int i, Vector3 bodyPos, Quaternion rot)
    {
        ref var p = ref Body.Parts[i];
        return new FreePlacement { Pos = bodyPos + Vector3.Transform(p.Pos, rot), Rot = rot, Turn = p.Rot };
    }

    /// <summary>How many of each part it takes, by part id (this machine's: extrusions are their run-time ids).</summary>
    public Dictionary<ushort, int> Cost()
    {
        var cost = new Dictionary<ushort, int>();
        for (var i = 0; i < Body.PartCount; i++)
        {
            var id = Body.Parts[i].Def;
            cost[id] = cost.GetValueOrDefault(id) + 1;
        }
        return cost;
    }

    // ---- As a blueprint ------------------------------------------------------------------------------------------

    /// <summary>
    /// The clip as a shipped-format blueprint's text (ConstructBuild.ParseBlueprint reads it): a step per part in the clip's order,
    /// each on the step it was reached from by the sockets they meet at, with where it stands ("at"); a part the walk reached by a
    /// freeform weld a "weld" step; one no link joins to the rest just stands there. Null, with why, for what the format can't hold.
    /// </summary>
    public string? ToBlueprint(string name, string description, out string why)
    {
        why = "";
        var c = Body;
        var sb = new StringBuilder(c.PartCount * 96);
        sb.Append("{\n  \"format\": \"").Append(ConstructBuild.BlueprintFormat).Append("\",\n  \"name\": ").Append(Quote(name));
        if (description.Length > 0)
            sb.Append(",\n  \"description\": ").Append(Quote(description));
        sb.Append(",\n  \"steps\": [\n");
        for (var k = 0; k < c.PartCount; k++)
        {
            ref var p = ref c.Parts[k];
            var d = c.DefOf(k);
            sb.Append("    { \"part\": ").Append(Quote(d.Name));
            if (k == 0)
                sb.Append(", \"spin\": ").Append(p.Rot);
            else if (StepOn(k, out var on, out var ts, out var ds, out var spin, ref why))
            {
                var td = c.DefOf(on);
                sb.Append(", \"on\": ").Append(on).Append(", \"socket\": ").Append(Quote(td.Sockets[ts].Name)).Append(", \"by\": ").Append(Quote(d.Sockets[ds].Name)).Append(", \"spin\": ").Append(spin);
                var at = c.FrameOf(p.Seg) == 0 ? p.Pos - c.Parts[0].Pos : p.Pos; // (step 0 builds at the origin; a freeform piece's numbers are in its own frame)
                sb.Append(FormattableString.Invariant($", \"at\": [{at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}]"));
            }
            else if (why.Length > 0)
                return null;
            else if (WeldOf(k) is var weld and >= 0)
            {
                Freeform.StepPose(c, weld, out var wp, out var wr);
                if (c.FrameOf(c.Joints[weld].ParentSeg) == 0)
                    wp -= c.Parts[0].Pos; // (step 0 builds at the origin)
                sb.Append(", \"weld\": ").Append(ConstructBuild.WeldJson(c.Joints[weld].Hinge, wp, wr));
            }
            else
            {
                // Nothing to fit it by: it stands where it is.
                var at = p.Pos - c.Parts[0].Pos;
                sb.Append(", \"on\": 0, \"spin\": ").Append(p.Rot).Append(FormattableString.Invariant($", \"at\": [{at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}]"));
            }
            if (p.Setting != d.DefaultSetting)
                sb.Append(", \"setting\": ").Append(p.Setting);
            var cfg = ConstructConfig.ToJson(in p, d);
            if (cfg.Length > 0)
                sb.Append(", \"config\": ").Append(cfg);
            if (ConstructPaint.IsSet(p.Paint))
                sb.Append(", \"paint\": \"").Append(ConstructPaint.ToHex(p.Paint)).Append('"');
            sb.Append(k + 1 < c.PartCount ? " },\n" : " }\n");
        }
        sb.Append("  ]");
        if (c.RouteCount > 0)
            sb.Append(",\n  \"links\": ").Append(ConstructRoutes.LinksJson(c, c.Parts[0].Pos, saved: true));
        if (World.ConnectorsJson(c) is { Length: > 0 } connectors)
            sb.Append(",\n  \"connectors\": ").Append(connectors);
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private bool StepOn(int k, out int on, out int socket, out int own, out int spin, ref string why)
    {
        on = socket = own = spin = -1;
        var c = Body;
        // The earliest step it hangs on by a link.
        for (var e = c.FirstLink(k); e >= 0; e = c.NextLink(e))
        {
            int o = c.OtherEnd(e);
            if (o >= k || !Hangs(c, o, k) || on >= 0 && o > on)
                continue;
            ref var l = ref c.Links[e >> 1];
            var (st, sd) = (e & 1) != 0 ? (l.SA, l.SB) : (l.SB, l.SA); // (an entry on the B side: the other end is A)
            on = o;
            socket = st;
            own = sd;
        }
        if (on < 0)
            return false;
        var td = c.DefOf(on);
        var d = c.DefOf(k);
        if (socket >= td.Sockets.Length || own >= d.Sockets.Length)
        {
            why = "a merged receiver's gun parts can't go in a blueprint yet";
            return false;
        }
        if (d.HasJoint && own == d.LeafIndex)
            return false; // (the format fits a joint part by its base: this one goes on as a weld)
        c.SocketAt(on, socket, out _, out var dir);
        Span<byte> turns = stackalloc byte[4];
        var n = ConstructBuild.SpinTurns(d.Sockets[own].Dir, dir, turns);
        for (var s = 0; s < n; s++)
            if (turns[s] == c.Parts[k].Rot)
            {
                spin = s;
                return true;
            }
        return false; // (no socket turn gives it: a weld step keeps it where it is)
    }

    /// <summary>The freeform weld that put part <paramref name="k"/> on, from an earlier step, or -1.</summary>
    private int WeldOf(int k)
    {
        var c = Body;
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform && c.Joints[j].Child == k && c.Joints[j].Hinge < k)
                return j;
        return -1;
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            sb.Append(ch is '"' or '\\' ? "\\" + ch : ch < 32 ? "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture) : ch.ToString());
        return sb.Append('"').ToString();
    }
}
