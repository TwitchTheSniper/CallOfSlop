using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A node of a world link (board item wl01): on the world (the terrain or static map geometry: <see cref="Body"/> 0, a
/// world position) or on a construct (a part of it: the rest frame of that part's segment, like <see cref="CRouteNode"/>).
/// </summary>
public struct WNode
{
    public Vector3 Pos; // the world (Body 0), else the rest frame of the host part's segment (1 cm grid)
    public ushort Tag; // server: the host part's tag (0: the world)
    public ushort Body; // where it is now (0: the world)
    public byte Part;
    public Vector3 Made; // server: where it was in the world when the link was made (a free construct's node that moves snaps it)
    public Vector3 Support; // server, a world node: the way to the surface it sits on (zero: none found; the link breaks when it goes)

    public readonly bool OnWorld => Body == 0;
}

/// <summary>
/// A world link (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links"): a routed link from a port of one construct
/// to a port of another (or the same one), whose nodes run along the terrain, static map geometry and constructs. Its ends
/// name their parts by <see cref="CPart.Tag"/> (like the wires between constructs), resolved each tick. Nodes are in
/// <see cref="ConstructWorld.WorldNodes"/> from <see cref="Start"/>.
/// </summary>
public struct WLink
{
    public RouteType Type;
    public ushort TagFrom, TagTo; // server
    public ushort BodyFrom, BodyTo; // where the ends are now
    public byte From, To, FromPort, ToPort;
    public ushort Start;
    public byte Count;
    public Vector3 MadeFrom, MadeTo; // server: the ends' world points when made
    public Quaternion RotFrom, RotTo; // server: the ends' constructs' turn when made
    public float Timer; // server: a chute's next move
}

/// <summary>
/// World links (board item wl01): routed links that leave a construct, so a base can be laid out on the ground with
/// pipes, cables and chutes running across the terrain between constructs. Server-owned (a list beside the constructs,
/// sent whole when it changes: CBodyKind.WorldLinks). The routing rules are the construct links' own
/// (<see cref="ConstructRoutes.WorldPathRule"/>: segment and length limits, the angle rules, the clipping grace and the
/// embedding forgiveness, against every construct's parts and the static world). A link that touches a free (not
/// anchored) construct snaps when that construct moves (<see cref="SnapMetres"/>, <see cref="SnapDegrees"/>); a world
/// node whose surface goes (dug or blown away) breaks its link. What flows: fuel from tank to tank, charge from batteries
/// into batteries, signals, items from storage to storage. Fixed arrays: nothing allocates.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxWorldLinks = 64;
    public const int MaxWorldNodes = 1024;
    public const float SnapMetres = 0.05f; // a free construct's point moved this far since the link was made: it snaps (settling suspension stays inside)
    public const float SnapDegrees = 3f; // ...or turned this much
    public const float SupportReach = 0.15f; // a world node's surface is within its radius plus this
    public const float PipeFlow = 2f; // litres a second through a world pipe
    public const float CableFlow = 5f; // kW through a world power cable

    public readonly WLink[] WorldLinks = new WLink[MaxWorldLinks];
    public int WorldLinkCount;
    public readonly WNode[] WorldNodes = new WNode[MaxWorldNodes];
    public int WorldNodeCount;

    /// <summary>The world links or the hoses changed (made, broken, resolved elsewhere): the server sends them again.</summary>
    public bool WorldLinksDirty;

    /// <summary>Server: HUD lines for the players near something that happened (a link snapped, a hose popped out): where, and what.</summary>
    public readonly List<(Vector3 At, string Text)> Hints = new(8);

    private int _supportCursor;
    private readonly CFireEvent[] _snaps = new CFireEvent[8];
    private int _snapCount;

    /// <summary>The tick's world links that tore off, as fire events (sparks, a spill): after the guns' step, which starts the tick's list.</summary>
    private void FlushSnaps()
    {
        for (var i = 0; i < _snapCount; i++)
            AddFire(_snaps[i]);
        _snapCount = 0;
    }
    internal static readonly ConstructRoutes.WHost[] HostScratch = new ConstructRoutes.WHost[Construct.MaxNodesPerRoute + 3];

    /// <summary>Is <paramref name="c"/> held still (a foundation, pinned or frozen): it counts as the world.</summary>
    public static bool Still(Construct c) => c.Anchored;

    public ref WNode WNodeAt(int link, int k) => ref WorldNodes[WorldLinks[link].Start + k];

    /// <summary>
    /// Point <paramref name="i"/> of world link <paramref name="k"/> in the world: -1 its start (the output's port), 0..Count-1
    /// its nodes, Count its end. False when its construct isn't there (a client between messages).
    /// </summary>
    public bool WorldPoint(int k, int i, out Vector3 p)
    {
        ref var l = ref WorldLinks[k];
        if (i < 0 || i >= l.Count)
        {
            var body = i < 0 ? l.BodyFrom : l.BodyTo;
            var part = i < 0 ? l.From : l.To;
            var port = i < 0 ? l.FromPort : l.ToPort;
            if (Get(body) is not { } c || part >= c.PartCount)
            {
                p = default;
                return false;
            }
            p = PortWorld(c, part, port);
            return true;
        }
        return NodeWorld(in WorldNodes[l.Start + i], out p);
    }

    /// <summary>A node's world position.</summary>
    public bool NodeWorld(in WNode n, out Vector3 p)
    {
        if (n.Body == 0)
        {
            p = n.Pos;
            return true;
        }
        if (Get(n.Body) is not { } c || n.Part >= c.PartCount)
        {
            p = default;
            return false;
        }
        c.WorldFrame(c.Parts[n.Part].Seg, out var q, out var t);
        p = t + Vector3.Transform(n.Pos, q);
        return true;
    }

    /// <summary>A part's link port in the world (its segment as it is now).</summary>
    public static Vector3 PortWorld(Construct c, int part, byte port)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        return t + Vector3.Transform(ConstructRoutes.PortPoint(c, part, port), q);
    }

    /// <summary>World links of a type into part <paramref name="part"/> of <paramref name="c"/>.</summary>
    public int WorldLinksInto(Construct c, int part, RouteType type)
    {
        var n = 0;
        for (var k = 0; k < WorldLinkCount; k++)
            if (WorldLinks[k].BodyTo == c.Id && WorldLinks[k].To == part && WorldLinks[k].Type == type)
                n++;
        return n;
    }

    // ---- Making and removing them (server) -------------------------------------------------------------------------

    /// <summary>
    /// Can this world link be made? Null when it can, else why not. <paramref name="nodes"/>: each node's
    /// <see cref="WNode.Body"/> (0: the world), <see cref="WNode.Part"/> and <see cref="WNode.Pos"/> (world, or the host part's segment rest frame).
    /// </summary>
    public string? CheckWorldLink(IWorldQuery? q, RouteType type, Construct cf, int from, byte fromPort, ReadOnlySpan<WNode> nodes, Construct ct, int to, byte toPort)
    {
        if (type >= RouteType.COUNT)
            return "no such link type";
        if (type == RouteType.Shaft)
            return "a drive shaft can't run between constructs: torque stays on one body (fit the gearbox on the same construct)";
        if (from < 0 || from >= cf.PartCount || to < 0 || to >= ct.PartCount)
            return "no such part";
        if (WorldLinkCount >= MaxWorldLinks)
            return $"at most {MaxWorldLinks} world links";
        if (nodes.Length > Construct.MaxNodesPerRoute)
            return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
        if (WorldNodeCount + nodes.Length > MaxWorldNodes)
            return "the world has all the link nodes it can take";
        if (TotalRoutes + WorldLinkCount >= WireCap)
            return "the world has all the links it can take";
        if (fromPort != 255 || toPort != 255)
            return "a link between constructs goes into the part itself, not a port";
        var fd = cf.DefOf(from);
        var td = ct.DefOf(to);
        if (cf.Parts[from].Broken || ct.Parts[to].Broken)
            return "that part is wrecked";
        if (!ConstructRoutes.IsOutput(fd, type))
            return $"a {fd.Name} has no {ConstructRoutes.Name(type)} output ({ConstructRoutes.OutputHint(type)})";
        if (!ConstructRoutes.IsWorldInput(td, type))
            return $"a {td.Name} takes no {ConstructRoutes.Name(type)} from another construct ({ConstructRoutes.WorldInputHint(type)})";
        if (cf == ct && from == to)
            return "a part can't be linked to itself";
        var leaves = cf != ct;
        for (var i = 0; i < nodes.Length && !leaves; i++)
            leaves = nodes[i].Body != cf.Id;
        if (!leaves)
            return "that link stays on one construct: route it as an ordinary link";
        if (type == RouteType.Data)
        {
            if (td.Logic == CLogic.Reader)
                return "an input reader reads a seat of its own construct";
            if (ct.RoutesInto(to, RouteType.Data) + XWiresInto(ct, to) + WorldLinksInto(ct, to, RouteType.Data) >= td.MaxInputs)
                return $"a {td.Name} takes at most {td.MaxInputs} input{(td.MaxInputs == 1 ? "" : "s")}";
        }
        for (var k = 0; k < WorldLinkCount; k++)
        {
            ref var l = ref WorldLinks[k];
            if (l.Type == type && l.BodyFrom == cf.Id && l.From == from && l.BodyTo == ct.Id && l.To == to)
                return "those are already linked";
        }
        Span<Vector3> pts = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
        var hosts = HostScratch.AsSpan(); // (holds construct references: a fixed scratch array, not the stack)
        var n = 0;
        pts[n] = PortWorld(cf, from, fromPort);
        hosts[n++] = new ConstructRoutes.WHost { C = cf, Part = from };
        for (var i = 0; i < nodes.Length; i++)
        {
            if (!float.IsFinite(nodes[i].Pos.X + nodes[i].Pos.Y + nodes[i].Pos.Z))
                return "bad node";
            Construct? hc = null;
            if (nodes[i].Body != 0 && (Get(nodes[i].Body) is not { } nc || nodes[i].Part >= nc.PartCount || (hc = nc) == null))
                return "a node's construct is gone";
            if (!NodeWorld(nodes[i], out pts[n]))
                return "a node's construct is gone";
            hosts[n++] = new ConstructRoutes.WHost { C = hc, Part = hc != null ? nodes[i].Part : -1 };
        }
        pts[n] = PortWorld(ct, to, toPort);
        hosts[n++] = new ConstructRoutes.WHost { C = ct, Part = to };
        if (ConstructRoutes.WorldPathRule(this, q, type, pts[..n], hosts[..n], true) is { } why)
            return why;
        for (var i = 0; i < nodes.Length; i++)
            if (nodes[i].Body == 0 && q != null && FindSupport(q, nodes[i].Pos, ConstructRoutes.Radius(type)) == Vector3.Zero)
                return "a node must sit on a surface (the ground, a wall, a construct)";
        return null;
    }

    /// <summary>Adds a checked world link (call <see cref="CheckWorldLink"/> first). Returns its index, or -1 when full.</summary>
    public int AddWorldLink(IWorldQuery? q, RouteType type, Construct cf, int from, byte fromPort, ReadOnlySpan<WNode> nodes, Construct ct, int to, byte toPort)
    {
        if (WorldLinkCount >= MaxWorldLinks || WorldNodeCount + nodes.Length > MaxWorldNodes)
            return -1;
        EnsureTag(cf, from);
        EnsureTag(ct, to);
        var k = WorldLinkCount++;
        ref var l = ref WorldLinks[k];
        l = new WLink
        {
            Type = type, TagFrom = cf.Parts[from].Tag, TagTo = ct.Parts[to].Tag, BodyFrom = cf.Id, BodyTo = ct.Id, From = (byte)from, To = (byte)to, FromPort = fromPort,
            ToPort = toPort, Start = (ushort)WorldNodeCount, Count = (byte)nodes.Length, MadeFrom = PortWorld(cf, from, fromPort), MadeTo = PortWorld(ct, to, toPort), RotFrom = cf.Rot,
            RotTo = ct.Rot,
        };
        var radius = ConstructRoutes.Radius(type);
        for (var i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            n.Pos = Quantise(n.Pos);
            if (n.Body != 0 && Get(n.Body) is { } hc)
            {
                EnsureTag(hc, n.Part);
                n.Tag = hc.Parts[n.Part].Tag;
            }
            else
            {
                n.Body = 0;
                n.Part = 0;
                n.Tag = 0;
                n.Support = q != null ? FindSupport(q, n.Pos, radius) : Vector3.Zero;
            }
            NodeWorld(n, out n.Made);
            WorldNodes[WorldNodeCount++] = n;
        }
        WorldLinksDirty = true;
        if (Still(ct))
            ct.Wake(); // (a free construct is left asleep: waking a parked vehicle lets it creep, which would snap the link at once)
        return k;
    }

    private void EnsureTag(Construct c, int part)
    {
        if (c.Parts[part].Tag == 0)
            c.Parts[part].Tag = NextTag();
    }

    /// <summary>Removes world link <paramref name="k"/> (its nodes too; the rest keep their order).</summary>
    public void RemoveWorldLink(int k)
    {
        if (k < 0 || k >= WorldLinkCount)
            return;
        var start = WorldLinks[k].Start;
        var count = WorldLinks[k].Count;
        for (var i = start; i + count < WorldNodeCount; i++)
            WorldNodes[i] = WorldNodes[i + count];
        WorldNodeCount -= count;
        for (var j = k; j + 1 < WorldLinkCount; j++)
        {
            WorldLinks[j] = WorldLinks[j + 1];
            WorldLinks[j].Start -= count;
        }
        WorldLinkCount--;
        WorldLinksDirty = true;
    }

    /// <summary>Removes every world link into or out of a part (it was taken off). Returns how many went.</summary>
    public int RemoveWorldLinksAt(Construct c, int part)
    {
        var n = 0;
        for (var k = WorldLinkCount - 1; k >= 0; k--)
            if (WorldLinks[k].BodyFrom == c.Id && WorldLinks[k].From == part || WorldLinks[k].BodyTo == c.Id && WorldLinks[k].To == part)
            {
                RemoveWorldLink(k);
                n++;
            }
        return n;
    }

    private void ClearWorldLinks()
    {
        if (WorldLinkCount + HoseCount > 0)
            WorldLinksDirty = true;
        WorldLinkCount = 0;
        WorldNodeCount = 0;
        HoseCount = 0;
        Hints.Clear();
    }

    /// <summary>The way from <paramref name="p"/> to the static surface it sits on (within its radius plus <see cref="SupportReach"/>), or zero.</summary>
    public static Vector3 FindSupport(IWorldQuery q, Vector3 p, float radius)
    {
        var reach = radius + SupportReach;
        var best = reach;
        var way = Vector3.Zero;
        // (down first: most nodes lie on the ground)
        ReadOnlySpan<Vector3> dirs = [-Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY];
        foreach (var d in dirs)
            if (q.Raycast(p, d, best, QueryFilter.StaticOnly, out var hit) && hit.Distance < best)
            {
                best = hit.Distance;
                way = d;
            }
        return way;
    }

    // ---- Each tick (server) ------------------------------------------------------------------------------------

    /// <summary>
    /// Server, every tick after the couplings: the world links' ends and construct nodes find their parts again (broken
    /// when a part is gone or wrecked); a link with a point on a free construct snaps when that point moved or turned
    /// past the tolerance (the ends spark or spill, a hint for the players near); one world node a tick checks its
    /// surface is still there; then what each link carries flows. Then the hoses (ConstructHoses.cs).
    /// </summary>
    private void StepWorldLinks(IWorldQuery? query, float dt, Func<byte, Vector3?>? hands)
    {
        for (var k = WorldLinkCount - 1; k >= 0; k--)
        {
            ref var l = ref WorldLinks[k];
            var (bf, pf, bt, pt) = (l.BodyFrom, l.From, l.BodyTo, l.To);
            var cf = Resolve(l.TagFrom, ref l.BodyFrom, ref l.From);
            var ct = Resolve(l.TagTo, ref l.BodyTo, ref l.To);
            if (bf != l.BodyFrom || pf != l.From || bt != l.BodyTo || pt != l.To)
                WorldLinksDirty = true;
            if (cf == null || ct == null || cf.Parts[l.From].Broken || ct.Parts[l.To].Broken)
            {
                BreakWorldLink(k, cf != null && ct != null ? "a part at its end is wrecked" : "a part at its end is gone", null);
                continue;
            }
            // The nodes on constructs, and the free-construct check.
            string? snapped = null;
            Construct? mover = null;
            for (var i = 0; i < l.Count && snapped == null; i++)
            {
                ref var n = ref WorldNodes[l.Start + i];
                if (n.Tag == 0)
                    continue;
                var (nb, np) = (n.Body, n.Part);
                if (Resolve(n.Tag, ref n.Body, ref n.Part) is not { } hc)
                {
                    snapped = "the part a node sat on is gone";
                    break;
                }
                if (nb != n.Body || np != n.Part)
                    WorldLinksDirty = true;
                if (!Still(hc) && NodeWorld(n, out var now) && Vector3.DistanceSquared(now, n.Made) > SnapMetres * SnapMetres)
                {
                    snapped = "moved";
                    mover = hc;
                }
            }
            if (snapped == null && !Still(cf) && Moved(PortWorld(cf, l.From, l.FromPort), l.MadeFrom, cf.Rot, l.RotFrom))
                (snapped, mover) = ("moved", cf);
            if (snapped == null && !Still(ct) && Moved(PortWorld(ct, l.To, l.ToPort), l.MadeTo, ct.Rot, l.RotTo))
                (snapped, mover) = ("moved", ct);
            if (snapped != null)
            {
                BreakWorldLink(k, snapped, mover);
                continue;
            }
            Flow(ref l, cf, ct, dt);
        }
        // One world node a tick: is its surface still there (decision 16: terrain dug or blown away breaks the link)?
        if (query != null && WorldNodeCount > 0)
        {
            _supportCursor = (_supportCursor + 1) % WorldNodeCount;
            ref var n = ref WorldNodes[_supportCursor];
            if (n.Tag == 0 && n.Support != Vector3.Zero)
            {
                var link = LinkOfNode(_supportCursor);
                if (link >= 0)
                {
                    var r = ConstructRoutes.Radius(WorldLinks[link].Type);
                    if (!query.Raycast(n.Pos, n.Support, r + SupportReach + 0.02f, QueryFilter.StaticOnly, out _))
                        BreakWorldLink(link, "the ground under it is gone", null);
                }
            }
        }
        StepHoses(dt, hands);
    }

    private int LinkOfNode(int node)
    {
        for (var k = 0; k < WorldLinkCount; k++)
            if (node >= WorldLinks[k].Start && node < WorldLinks[k].Start + WorldLinks[k].Count)
                return k;
        return -1;
    }

    /// <summary>Did a point move past the snap tolerance (or its construct turn past it)?</summary>
    private static bool Moved(Vector3 now, Vector3 made, Quaternion rot, Quaternion madeRot)
    {
        if (Vector3.DistanceSquared(now, made) > SnapMetres * SnapMetres)
            return true;
        var dot = MathF.Abs(Quaternion.Dot(rot, madeRot));
        return 2f * MathF.Acos(MathF.Min(1f, dot)) > SnapDegrees * MathF.PI / 180f;
    }

    /// <summary>A world link breaks: removed, its ends spark or spill (a hint for the players near), nothing is damaged.</summary>
    public void BreakWorldLink(int k, string why, Construct? mover)
    {
        ref var l = ref WorldLinks[k];
        var name = ConstructRoutes.Name(l.Type);
        WorldPoint(k, -1, out var a);
        WorldPoint(k, l.Count, out var b);
        var at = mover != null && Get(l.BodyTo) == mover ? b : a;
        var what = l.Type == RouteType.Pipe ? "spilling" : l.Type == RouteType.Power ? "sparking" : "";
        var text = mover != null
            ? $"the {name} to the {ConstructLabel(mover)} tore off{(what.Length > 0 ? $", {what}" : "")}: it moved"
            : $"a {name} broke: {why}";
        Events.Add($"world link {k} ({name}, construct {l.BodyFrom} -> {l.BodyTo}) broke: {why}");
        Hints.Add((at, text));
        if (l.Type is RouteType.Pipe or RouteType.Power && _snapCount < _snaps.Length)
            _snaps[_snapCount++] = new CFireEvent { Kind = CFireKind.LinkSnap, Body = l.BodyFrom, Part = (byte)l.Type, At = at }; // (added to the tick's fire events after the guns' step clears them)
        Get(l.BodyTo)?.Wake();
        RemoveWorldLink(k);
    }

    /// <summary>What a player calls a construct in a hint: its driver's seat makes it a vehicle, else its biggest kind of part.</summary>
    public static string ConstructLabel(Construct c) =>
        c.CanDrive ? "vehicle" : c.Anchored ? "base" : c.PartCount > 0 ? c.DefOf(c.Root >= 0 && c.Root < c.PartCount ? c.Root : 0).Name.ToLowerInvariant() : "construct";

    /// <summary>What one world link carries this tick: fuel from tank to tank, charge into batteries, a signal (the logic reads it), items.</summary>
    private void Flow(ref WLink l, Construct cf, Construct ct, float dt)
    {
        switch (l.Type)
        {
            case RouteType.Pipe:
                MoveFuel(cf, l.From, ct, PipeFlow * dt);
                break;
            case RouteType.Power:
                MoveCharge(cf, ct, CableFlow * dt);
                break;
            case RouteType.Data:
                ct.XWireIn++; // (the logic runs for it: ConstructLogic reads the source's value)
                var v = cf.Parts[l.From].Out;
                if (MathF.Abs(v - ct.Parts[l.To].In) > 1e-4f)
                    ct.Wake();
                break;
            case RouteType.Chute:
                l.Timer += dt;
                if (l.Timer >= ConstructRoutes.ChuteSeconds)
                {
                    l.Timer -= ConstructRoutes.ChuteSeconds;
                    ConstructRoutes.ChuteMove(this, cf, l.From, ct, l.To);
                }
                break;
        }
    }

    /// <summary>The fluid a construct's tanks hold (its first working tank's), or null without one.</summary>
    public static Fluid? TankFluid(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.FuelTank && c.Parts[i].Working)
                return c.Parts[i].D.TankFluid;
        return null;
    }

    /// <summary>Moves up to <paramref name="litres"/> from <paramref name="cf"/>'s tanks into <paramref name="ct"/>'s (as much as they have room for). Returns how much moved.</summary>
    public static float MoveFuel(Construct cf, int fromPart, Construct ct, float litres, Fluid? only = null)
    {
        if (cf == ct || cf.Fuel <= 0 || litres <= 0)
            return 0;
        if (fromPart >= 0 && (fromPart >= cf.PartCount || cf.Parts[fromPart].D.Role != PartRole.FuelTank || !cf.Parts[fromPart].Working))
            return 0;
        var ff = TankFluid(cf);
        var tf = TankFluid(ct);
        if (ff == null || tf == null || ff != tf || only is { } want && ff != want)
            return 0;
        var room = ct.FuelCapacity() - ct.Fuel;
        var moved = MathF.Min(litres, MathF.Min(cf.Fuel, room));
        if (moved <= 0)
            return 0;
        cf.Fuel -= moved;
        ct.Fuel += moved;
        return moved;
    }

    /// <summary>Moves up to <paramref name="kj"/> of charge from <paramref name="cf"/>'s batteries into <paramref name="ct"/>'s. Returns how much moved.</summary>
    public static float MoveCharge(Construct cf, Construct ct, float kj)
    {
        if (cf == ct || cf.Charge <= 0 || kj <= 0)
            return 0;
        var room = ct.ChargeCapacity - ct.Charge;
        var moved = MathF.Min(kj, MathF.Min(cf.Charge, room));
        if (moved <= 0)
            return 0;
        cf.Charge -= moved;
        ct.Charge += moved;
        return moved;
    }

    /// <summary>Data world links into <paramref name="c"/>: each source's value (the logic adds them like wires; ConstructLogic).</summary>
    public float WorldDataInto(Construct c, int part, out int count, out int high)
    {
        var sum = 0f;
        count = high = 0;
        for (var k = 0; k < WorldLinkCount; k++)
        {
            ref var l = ref WorldLinks[k];
            if (l.Type != RouteType.Data || l.BodyTo != c.Id || l.To != part || Get(l.BodyFrom) is not { } cf || l.From >= cf.PartCount)
                continue;
            var v = cf.Parts[l.From].Broken ? 0f : cf.Parts[l.From].Out;
            sum += v;
            count++;
            if (v >= 0.5f)
                high++;
        }
        return sum;
    }

    /// <summary>The nearest world link segment along a ray (world), within <paramref name="best"/>; true when one is nearer.</summary>
    public bool RayWorldLinks(Vector3 origin, Vector3 dir, ref float best, out int link, out int seg, out Vector3 point)
    {
        link = seg = -1;
        point = default;
        var found = false;
        for (var k = 0; k < WorldLinkCount; k++)
        {
            ref var l = ref WorldLinks[k];
            var radius = ConstructRoutes.Radius(l.Type) + 0.01f;
            if (!WorldPoint(k, -1, out var a))
                continue;
            for (var i = 0; i <= l.Count; i++)
            {
                if (!WorldPoint(k, i, out var b))
                    break;
                var d2 = Geometry.ClosestSegmentSegment(origin, origin + dir * best, a, b, out var onRay, out var onSeg);
                if (d2 <= radius * radius)
                {
                    var t = Vector3.Dot(onRay - origin, dir);
                    if (t >= 0 && t < best)
                    {
                        best = t;
                        link = k;
                        seg = i;
                        point = onSeg;
                        found = true;
                    }
                }
                a = b;
            }
        }
        return found;
    }
}
