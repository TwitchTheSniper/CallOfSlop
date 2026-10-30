using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Routed links in blueprints (the <c>"links"</c> key) and the migration of the old connections: logic wires were already
/// data links (ConstructWorld.AddWire), Fuel Line parts become pipe links and plain drive-shaft parts become shaft links
/// when a blueprint is built (blueprints auto-converted). U and CV joints, gearboxes, T boxes, transfer cases, clutches,
/// chain and belt drives (they have ratios), power take-offs, hubs and differentials stay devices that links plug into.
/// </summary>
public static partial class ConstructRoutes
{
    /// <summary>One blueprint link: a type, the steps at its ends (or a branch off an earlier link), the ports, the nodes (rest frame).</summary>
    public sealed class LinkSpec
    {
        public RouteType Type;
        public int From = -1, To = -1;
        public string FromPort = "", ToPort = "";
        public int Branch = -1, BranchSeg; // a branch: the earlier link (its index in "links") and its segment
        public Vector3 At; // the junction
        public List<Vector3> Nodes = new();
    }

    private static readonly string[] LinkKeys = ["type", "style", "from", "to", "fromPort", "toPort", "nodes", "branch", "at"];

    public static LinkSpec ParseLink(JsonElement e, string file, int index)
    {
        var where = $"{file}: links[{index}]";
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{where} must be an object");
        var s = new LinkSpec();
        foreach (var p in e.EnumerateObject())
            if (Array.IndexOf(LinkKeys, p.Name) < 0)
                throw new ConstructPartException($"{where}: unknown key \"{p.Name}\" ({string.Join(", ", LinkKeys)})");
        s.Type = e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && TypeOf(t.GetString()!) is { } rt ? rt
            : throw new ConstructPartException($"{where}: \"type\" must be data, power, pipe, shaft or chute");
        // (an old blueprint may carry a pipe "style": read and ignored, there is one pipe for every fluid)
        if (e.TryGetProperty("branch", out var br))
        {
            if (br.ValueKind != JsonValueKind.Array || br.GetArrayLength() != 2 || !e.TryGetProperty("at", out var at))
                throw new ConstructPartException($"{where}: a branch is \"branch\": [link, segment] with \"at\": [x, y, z]");
            s.Branch = br[0].GetInt32();
            s.BranchSeg = br[1].GetInt32();
            if (s.Branch < 0 || s.Branch >= index)
                throw new ConstructPartException($"{where}: \"branch\" must name an earlier link");
            s.At = Vec3(at, where);
        }
        else
            s.From = e.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : throw new ConstructPartException($"{where}: \"from\" (a step) is missing");
        s.To = e.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Number ? to.GetInt32() : throw new ConstructPartException($"{where}: \"to\" (a step) is missing");
        if (e.TryGetProperty("fromPort", out var fp))
            s.FromPort = fp.GetString() ?? "";
        if (e.TryGetProperty("toPort", out var tp))
            s.ToPort = tp.GetString() ?? "";
        if (e.TryGetProperty("nodes", out var ns))
            foreach (var n in ns.EnumerateArray())
                s.Nodes.Add(Vec3(n, where));
        if (s.Nodes.Count > Construct.MaxNodesPerRoute)
            throw new ConstructPartException($"{where}: at most {Construct.MaxNodesPerRoute} nodes");
        return s;
    }

    private static Vector3 Vec3(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new ConstructPartException($"{where}: a point is [x, y, z]");
        var v = new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        if (!float.IsFinite(v.X + v.Y + v.Z))
            throw new ConstructPartException($"{where}: a point that isn't a number");
        return v;
    }

    /// <summary>The part a node at <paramref name="p"/> (rest frame) sits on: the nearest one.</summary>
    public static int NearestPart(Construct c, Vector3 p)
    {
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var dist = (Vector3.Max(Vector3.Abs(p - centre) - half, Vector3.Zero)).LengthSquared();
                if (dist < bestD)
                {
                    bestD = dist;
                    best = i;
                }
            }
        }
        return best;
    }

    /// <summary>Builds blueprint link <paramref name="s"/> through the linking tool's rules (<paramref name="partOf"/>: each step's part). Null, or why not.</summary>
    public static string? BuildLink(ConstructWorld world, Construct c, LinkSpec s, ReadOnlySpan<int> partOf)
    {
        if (s.To < 0 || s.To >= partOf.Length || s.Branch < 0 && (s.From < 0 || s.From >= partOf.Length))
            return "no such step";
        var to = partOf[s.To];
        var from = s.Branch < 0 ? partOf[s.From] : -1;
        // (blueprint links are added in order, after the wires: link k is route WireRoutes + k)
        var parent = 255;
        if (s.Branch >= 0)
        {
            parent = c.RouteCount - (BuiltLinks(c) - s.Branch);
            if (parent < 0 || parent >= c.RouteCount)
                return "no such link to branch from";
        }
        var fromPort = (byte)255;
        var toPort = (byte)255;
        if (s.FromPort.Length > 0 && from >= 0 && (fromPort = PortByName(c.DefOf(from), s.Type, s.FromPort, input: false)) == 255 && !(s.Type == RouteType.Data && HasDataPort(c.DefOf(from), s.FromPort, false)))
            return $"a {c.DefOf(from).Name} has no {(s.Type == RouteType.Data ? "data output" : "socket")} \"{s.FromPort}\"";
        if (s.ToPort.Length > 0 && (toPort = PortByName(c.DefOf(to), s.Type, s.ToPort, input: true)) == 255 && !(s.Type == RouteType.Data && HasDataPort(c.DefOf(to), s.ToPort, true)))
            return $"a {c.DefOf(to).Name} has no {(s.Type == RouteType.Data ? "data input" : "socket")} \"{s.ToPort}\"";
        Span<CRouteNode> nodes = stackalloc CRouteNode[Construct.MaxNodesPerRoute];
        for (var i = 0; i < s.Nodes.Count; i++)
        {
            var pos = ConstructWorld.Quantise(s.Nodes[i]);
            nodes[i] = new CRouteNode { Pos = pos, Host = (byte)Math.Max(0, NearestPart(c, pos)) };
        }
        var junction = ConstructWorld.Quantise(s.At);
        if (Check(world, c, s.Type, from, fromPort, parent, s.BranchSeg, junction, nodes[..s.Nodes.Count], to, toPort, straight: s.Nodes.Count == 0 && s.Branch < 0) is { } why)
            return $"{Name(s.Type)} {c.DefOf(parent == 255 ? from : c.Routes[parent].Source).Name} -> {c.DefOf(to).Name}: {why}";
        if (Add(world, c, s.Type, from, fromPort, parent, s.BranchSeg, junction, nodes[..s.Nodes.Count], to, toPort) < 0)
            return "no room for another link";
        c.Routes[c.RouteCount - 1].Cursor = BlueprintMark;
        return null;
    }

    private const byte BlueprintMark = 0xB1; // (a link built from a blueprint's "links", while it is being built: branches find their parent by it)

    private static int BuiltLinks(Construct c)
    {
        var n = 0;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Cursor == BlueprintMark)
                n++;
        return n;
    }

    private static byte SocketIndex(Construct c, int part, string name)
    {
        var d = c.DefOf(part);
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Name == name)
                return (byte)s;
        return 255;
    }

    /// <summary>
    /// The old connections become links: each chain of Fuel Line parts between a tank and an engine becomes a fuel pipe
    /// along the same path, and each chain of plain drive-shaft parts between two drive parts a shaft link from port to
    /// port, so long as taking the old parts off splits nothing off the construct (a part hung only on a fuel line stays
    /// as it was: the old parts still work). Returns how many links were made.
    /// </summary>
    public static int MigrateLegacy(ConstructWorld world, Construct c)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Cursor == BlueprintMark)
                c.Routes[k].Cursor = 0;
        var made = 0;
        for (var guard = 0; guard < Construct.MaxParts; guard++)
        {
            if (!MigrateOne(world, c))
                break;
            made++;
        }
        return made;
    }

    private static bool IsLegacyFuel(ConstructPartDef d) => d.Role == PartRole.FuelLine;

    private static bool IsLegacyShaft(ConstructPartDef d) => d.Role == PartRole.Transfer && d.Drive == DriveKind.Shaft;

    private static bool MigrateOne(ConstructWorld world, Construct c)
    {
        Span<bool> tried = stackalloc bool[Construct.MaxParts];
        tried.Clear();
        Span<bool> comp = stackalloc bool[Construct.MaxParts];
        Span<byte> order = stackalloc byte[Construct.MaxParts];
        for (var start = 0; start < c.PartCount; start++)
        {
            var sd = c.DefOf(start);
            var fuel = IsLegacyFuel(sd);
            if (tried[start] || !fuel && !IsLegacyShaft(sd) || c.Parts[start].Broken)
                continue;
            // The chain of old parts of this kind.
            comp.Clear();
            var n = 0;
            order[n++] = (byte)start;
            comp[start] = true;
            for (var h = 0; h < n; h++)
                for (var l = 0; l < c.LinkCount; l++)
                {
                    ref var lk = ref c.Links[l];
                    var other = lk.A == order[h] ? lk.B : lk.B == order[h] ? lk.A : -1;
                    if (other < 0 || comp[other] || c.Parts[other].Broken)
                        continue;
                    var od = c.DefOf(other);
                    if (fuel ? IsLegacyFuel(od) : IsLegacyShaft(od))
                    {
                        comp[other] = true;
                        order[n++] = (byte)other;
                    }
                }
            for (var i = 0; i < n; i++)
                tried[order[i]] = true;
            // Its ends: the parts the chain touches (with the socket each is touched at).
            int endA = -1, endB = -1, sockA = 255, sockB = 255, lineA = -1, lineB = -1, ends = 0;
            for (var l = 0; l < c.LinkCount; l++)
            {
                ref var lk = ref c.Links[l];
                int inner, outer, outerSock;
                if (comp[lk.A] && !comp[lk.B]) (inner, outer, outerSock) = (lk.A, lk.B, lk.SB);
                else if (comp[lk.B] && !comp[lk.A]) (inner, outer, outerSock) = (lk.B, lk.A, lk.SA);
                else continue;
                var od = c.DefOf(outer);
                var useful = fuel ? od.Role == PartRole.FuelTank || od.Role == PartRole.Engine && od.FuelKind == EngineFuel.Fuel : IsOutput(od, RouteType.Shaft) || IsInput(od, RouteType.Shaft);
                if (!useful)
                    continue; // (a bracket it was bolted to)
                if (!fuel && od.Ports.Length > 0 && Array.IndexOf(od.Ports, outerSock) < 0)
                    continue; // (fitted by a drive part's side: it never carried the drive)
                if (outer == endA || outer == endB)
                    continue;
                ends++;
                if (endA < 0)
                    (endA, sockA, lineA) = (outer, outerSock, inner);
                else
                    (endB, sockB, lineB) = (outer, outerSock, inner);
            }
            if (ends != 2)
                continue;
            if (fuel)
            {
                // From the tank to the engine.
                if (c.DefOf(endA).Role != PartRole.FuelTank)
                    (endA, endB, lineA, lineB) = (endB, endA, lineB, lineA);
                if (c.DefOf(endA).Role != PartRole.FuelTank || c.DefOf(endB).Role != PartRole.Engine)
                    continue;
            }
            else
            {
                if (!IsInput(c.DefOf(endB), RouteType.Shaft))
                    (endA, endB, sockA, sockB, lineA, lineB) = (endB, endA, sockB, sockA, lineB, lineA);
                if (!IsOutput(c.DefOf(endA), RouteType.Shaft) || !IsInput(c.DefOf(endB), RouteType.Shaft) || c.Parts[endA].Seg != c.Parts[endB].Seg)
                    continue;
            }
            // Taking the chain off must split nothing off: everything else still reaches the root.
            if (!StaysWhole(c, comp))
                continue;
            // The path's bends (the old parts' centres where the chain turns), in order from end A.
            Span<Vector3> path = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
            var pn = ChainPath(c, comp, lineA, lineB, path);
            Span<CRouteNode> nodes = stackalloc CRouteNode[Construct.MaxNodesPerRoute];
            var nn = 0;
            var from = fuel ? PortPoint(c, endA, 255) : PortPoint(c, endA, (byte)sockA);
            var to = fuel ? PortPoint(c, endB, 255) : PortPoint(c, endB, (byte)sockB);
            var prev = from;
            for (var i = 0; i < pn && nn < Construct.MaxNodesPerRoute; i++)
            {
                var next = i + 1 < pn ? path[i + 1] : to;
                var d0 = path[i] - prev;
                var d1 = next - path[i];
                if (d0.LengthSquared() < 1e-6f || d1.LengthSquared() < 1e-6f || Vector3.Dot(Vector3.Normalize(d0), Vector3.Normalize(d1)) > 0.9998f)
                    continue; // (straight on: no node needed)
                nodes[nn++] = new CRouteNode { Pos = ConstructWorld.Quantise(path[i]), Host = (byte)endA };
                prev = path[i];
            }
            // Take the old parts off (highest index first), then link the two ends where they are now.
            var tagA = c.Parts[endA].Tag;
            var tagB = c.Parts[endB].Tag;
            for (var i = c.PartCount - 1; i >= 0; i--)
                if (comp[i])
                    world.RemovePart(c, i);
            var a = ByTag(c, tagA, endA, comp);
            var b = ByTag(c, tagB, endB, comp);
            if (a < 0 || b < 0)
                return true; // (can't happen: nothing split off)
            for (var i = 0; i < nn; i++)
                nodes[i].Host = (byte)a;
            var type = fuel ? RouteType.Pipe : RouteType.Shaft;
            Add(world, c, type, a, fuel ? (byte)255 : (byte)sockA, 255, 0, default, nodes[..nn], b, fuel ? (byte)255 : (byte)sockB);
            world.Events.Add($"construct {c.Id}: {(fuel ? "a fuel line" : "a drive shaft")} of {n} part(s) became a {Name(type)} link");
            c.Rebuild();
            return true;
        }
        return false;
    }

    /// <summary>The part with tag <paramref name="tag"/> (or, with no tags on a client copy, the index it moved to once the chain went).</summary>
    private static int ByTag(Construct c, ushort tag, int old, ReadOnlySpan<bool> removed)
    {
        if (tag != 0)
        {
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Tag == tag)
                    return i;
            return -1;
        }
        var shift = 0;
        for (var i = 0; i < old; i++)
            if (removed[i])
                shift++;
        return old - shift;
    }

    private static bool StaysWhole(Construct c, ReadOnlySpan<bool> comp)
    {
        Span<bool> seen = stackalloc bool[Construct.MaxParts];
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        seen.Clear();
        var root = c.Root >= 0 && c.Root < c.PartCount && !comp[c.Root] ? c.Root : -1;
        for (var i = 0; i < c.PartCount && root < 0; i++)
            if (!comp[i])
                root = i;
        if (root < 0)
            return false;
        int head = 0, tail = 0;
        queue[tail++] = (byte)root;
        seen[root] = true;
        while (head < tail)
        {
            int at = queue[head++];
            for (var l = 0; l < c.LinkCount; l++)
            {
                ref var lk = ref c.Links[l];
                var other = lk.A == at ? lk.B : lk.B == at ? lk.A : -1;
                if (other < 0 || seen[other] || comp[other])
                    continue;
                seen[other] = true;
                queue[tail++] = (byte)other;
            }
        }
        for (var i = 0; i < c.PartCount; i++)
            if (!comp[i] && !seen[i])
                return false;
        return true;
    }

    /// <summary>The chain's part centres in order from <paramref name="first"/> to <paramref name="last"/> (a walk along its links).</summary>
    private static int ChainPath(Construct c, ReadOnlySpan<bool> comp, int first, int last, Span<Vector3> path)
    {
        Span<int> prevOf = stackalloc int[Construct.MaxParts];
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
            prevOf[i] = -2;
        int head = 0, tail = 0;
        queue[tail++] = (byte)first;
        prevOf[first] = -1;
        while (head < tail)
        {
            int at = queue[head++];
            if (at == last)
                break;
            for (var l = 0; l < c.LinkCount; l++)
            {
                ref var lk = ref c.Links[l];
                var other = lk.A == at ? lk.B : lk.B == at ? lk.A : -1;
                if (other < 0 || !comp[other] || prevOf[other] != -2)
                    continue;
                prevOf[other] = at;
                queue[tail++] = (byte)other;
            }
        }
        // Back from the last to the first, then reversed.
        var n = 0;
        for (var at = last; at >= 0 && n < path.Length; at = prevOf[at])
        {
            c.PartBounds(at, out var mn, out var mx);
            path[n++] = (mn + mx) * 0.5f;
            if (prevOf[at] == -2)
                break;
        }
        path[..n].Reverse();
        return n;
    }

    // ---- A construct's links as a blueprint's "links" (the round trip; parts by index, which is the step on a blueprint's own build) ----

    /// <summary>The links of <paramref name="c"/> as a blueprint's <c>"links"</c> JSON array (parts by index).</summary>
    public static string LinksJson(Construct c)
    {
        var sb = new StringBuilder("[");
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (k > 0)
                sb.Append(',');
            sb.Append("{\"type\":\"").Append(TypeKey(r.Type)).Append('"');
            if (r.Parent != 255)
            {
                // (the segment as it was when this branch was built: junctions of later branches before it weren't there yet)
                var seg = (int)r.ParentNode;
                for (var j = k + 1; j < c.RouteCount; j++)
                    if (c.Routes[j].Parent == r.Parent && c.Routes[j].ParentNode < r.ParentNode)
                        seg--;
                sb.Append(",\"branch\":[").Append(r.Parent).Append(',').Append(seg).Append("],\"at\":");
                AppendVec(sb, c.Node(r.Parent, r.ParentNode).Pos);
            }
            else
                sb.Append(",\"from\":").Append(r.From);
            sb.Append(",\"to\":").Append(r.To);
            if (r.FromPort != 255 && r.Parent == 255)
                sb.Append(",\"fromPort\":\"").Append(PortName(c.DefOf(r.From), r.Type, r.FromPort, input: false)).Append('"');
            if (r.ToPort != 255)
                sb.Append(",\"toPort\":\"").Append(PortName(c.DefOf(r.To), r.Type, r.ToPort, input: true)).Append('"');
            if (r.Count > 0)
            {
                sb.Append(",\"nodes\":[");
                var first = true;
                for (var i = 0; i < r.Count; i++)
                {
                    if (IsJunction(c, k, i))
                        continue; // (a junction node comes back from its branch)
                    if (!first)
                        sb.Append(',');
                    first = false;
                    AppendVec(sb, c.Node(k, i).Pos);
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        return sb.Append(']').ToString();
    }

    private static bool IsJunction(Construct c, int route, int node)
    {
        for (var k = route + 1; k < c.RouteCount; k++)
            if (c.Routes[k].Parent == route && c.Routes[k].ParentNode == node)
                return true;
        return false;
    }

    private static void AppendVec(StringBuilder sb, Vector3 v) =>
        sb.Append('[').Append(v.X.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(v.Y.ToString("0.##", CultureInfo.InvariantCulture))
            .Append(',').Append(v.Z.ToString("0.##", CultureInfo.InvariantCulture)).Append(']');
}
