using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a socket is for (board item dev-sandbox-snappy-build-sb01, decision 24; docs/sandbox/CONSTRUCTS.md, "Snappy
/// building"). <see cref="Mount"/> is the structural bolt-on every face has by default; the rest are functional
/// connections. A part's file does not name them: <see cref="SnapBuild"/> reads them from its role, the socket's face and a
/// gun part's category, so no part file changed.
/// </summary>
public enum ConnType : byte
{
    Mount, // a structural face: bolts onto any socket of the same kind
    Barrel, // a gun barrel (or splitter) to a receiver's front (or a splitter's outputs)
    Muzzle, // a muzzle device to a barrel's front
    Optic, // a sight to a receiver's top
    Feed, // a magazine, belt feed or ammo part to a receiver
    ShaftIn, // a drivetrain input (a gearbox's back)
    ShaftOut, // a drivetrain output (an engine's or gearbox's front)
    Fuel, // fuel in (an engine's back) or out (a tank, a line)
    Power, // a battery's or an alternator's terminal
    Data, // a seat's data out (its back)
    Hub, // a wheel onto a suspension's axle
    Leaf, // a door onto a hinge's leaf
}

/// <summary>One of the held part's own connection points (<see cref="SnapBuild.OwnPoints"/>).</summary>
public struct OwnPoint
{
    public int Socket;
    public ConnType Type;
    public bool Usable; // the part can mate by this point (a single-purpose part uses only its functional ones)
}

/// <summary>A functional point of a placed ghost that lines up with a free matching socket: the connection that will be made.</summary>
public struct Lineup
{
    public int OwnSocket, Part, Socket;
    public ConnType Type;
}

/// <summary>
/// The free sockets of one construct that a held part type can use, found once and kept: refreshed only when the
/// construct changes (<see cref="Signature"/>) or the held type does, so a frame with nothing new does a hash of the
/// parts and links and no more. Arrays are reused; nothing is allocated per frame.
/// </summary>
public sealed class SnapCache
{
    public int Body = -1;
    public ushort Def = ushort.MaxValue;
    public long Sig;
    public int Count;
    public int Rebuilds; // (the self-test counts them)
    public int[] Part = new int[256];
    public int[] Socket = new int[256];
    public Vector3[] At = new Vector3[256]; // rest frame, as Construct.SocketAt gives it
    public byte[] Dir = new byte[256];
    public byte[] Seg = new byte[256];
    public ConnType[] Type = new ConnType[256]; // the type of the held part's own socket that mates there (colour)

    /// <summary>A hash of what decides the sockets: each part (kind, place, turn, segment, wrecked) and each link.</summary>
    public static long Signature(Construct c)
    {
        long h = 1469598103934665603L;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            h = (h ^ p.Def) * 1099511628211L;
            h = (h ^ (long)(int)MathF.Round(p.Pos.X * 100) ^ (long)(int)MathF.Round(p.Pos.Y * 100) << 11 ^ (long)(int)MathF.Round(p.Pos.Z * 100) << 22) * 1099511628211L;
            h = (h ^ p.Rot ^ (long)p.Seg << 8 ^ (p.Broken ? 1L << 20 : 0)) * 1099511628211L;
        }
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            h = (h ^ l.A ^ (long)l.B << 8 ^ (long)l.SA << 16 ^ (long)l.SB << 24) * 1099511628211L;
        }
        return h ^ c.PartCount << 40 ^ c.LinkCount << 50 ^ (long)c.MergeCount << 58;
    }

    /// <summary>Makes the cache current for <paramref name="def"/> on <paramref name="c"/>; true when it had to rebuild.</summary>
    public bool Refresh(Construct c, ConstructPartDef def)
    {
        var sig = Signature(c);
        if (c.Id == Body && def.Id == Def && sig == Sig)
            return false;
        Body = c.Id;
        Def = def.Id;
        Sig = sig;
        Rebuilds++;
        Count = 0;
        var order = SnapBuild.OwnOrder(def);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Broken)
                continue;
            for (var s = 0; s < c.SocketCount(i); s++)
            {
                if (c.SocketUsed(i, s))
                    continue;
                var own = -1;
                for (var k = 0; k < order.Length && own < 0; k++)
                    if (SnapBuild.CanMate(c, def, order[k], i, s))
                        own = order[k];
                if (own < 0)
                    continue;
                if (Count == Part.Length)
                    Grow();
                c.SocketAt(i, s, out var at, out var dir);
                Part[Count] = i;
                Socket[Count] = s;
                At[Count] = at;
                Dir[Count] = dir;
                Seg[Count] = c.Parts[i].Seg;
                Type[Count] = SnapBuild.OwnType(def, own);
                Count++;
            }
        }
        return true;
    }

    private void Grow()
    {
        var n = Part.Length * 2;
        Array.Resize(ref Part, n);
        Array.Resize(ref Socket, n);
        Array.Resize(ref At, n);
        Array.Resize(ref Dir, n);
        Array.Resize(ref Seg, n);
        Array.Resize(ref Type, n);
    }
}

/// <summary>
/// Snappy building's rules, shared by the client (what to show and snap to) and the self-test; the server's checks are
/// still <see cref="ConstructBuild.Place"/>, so none of this can allow a placement it refuses. Each socket gets a
/// <see cref="ConnType"/>: the structural <see cref="ConnType.Mount"/>, or a functional one read from the part's role (an
/// engine's front is a shaft out, a gearbox's back a shaft in...). A single-purpose part (a wheel, a door, any gun part)
/// shows and snaps only by its functional sockets; any other part mounts by a structural face first and shows its
/// functional points beside it.
/// </summary>
public static class SnapBuild
{
    /// <summary>The magnet's reach on screen, in pixels at 1080 lines (scaled with the window's height).</summary>
    public const float SnapPixels = 80f;
    public const float FallbackReach = 6f; // metres from the aimed point: where a gun part still finds a socket when the screen magnet finds none

    /// <summary>Markers beyond this many metres are hidden; they fade out over the last part of it.</summary>
    public const float ShowRange = 9f;

    private sealed class DefInfo
    {
        public ConnType[] Own = [];
        public ConnType[] Port = [];
        public byte[] Order = [];
        public bool Single;
    }

    private static readonly Dictionary<ushort, DefInfo> Infos = new();

    private static DefInfo InfoOf(ConstructPartDef d)
    {
        if (Infos.TryGetValue(d.Id, out var info) && info.Own.Length == d.Sockets.Length)
            return info;
        info = Build(d);
        Infos[d.Id] = info;
        return info;
    }

    /// <summary>A wheel, a door or any gun part: it has one job, and only that connection is its way on.</summary>
    public static bool SinglePurpose(ConstructPartDef d) => InfoOf(d).Single;

    /// <summary>The type of the part's own socket <paramref name="s"/> when it is the one that mates.</summary>
    public static ConnType OwnType(ConstructPartDef d, int s) => InfoOf(d).Own[s];

    /// <summary>What socket <paramref name="s"/> of a placed part offers to whatever mates there.</summary>
    public static ConnType PortType(ConstructPartDef d, int s) => InfoOf(d).Port[s];

    /// <summary>The part's own sockets it may mate by, the structural ones first (a single-purpose part: its functional ones only).</summary>
    public static ReadOnlySpan<byte> OwnOrder(ConstructPartDef d) => InfoOf(d).Order;

    /// <summary>Does an own socket of type <paramref name="own"/> mate a port of type <paramref name="port"/>?</summary>
    public static bool Mates(ConnType own, ConnType port) =>
        own == port || own == ConnType.ShaftIn && port == ConnType.ShaftOut || own == ConnType.ShaftOut && port == ConnType.ShaftIn;

    public static string Label(ConnType t) => t switch
    {
        ConnType.Mount => "mount",
        ConnType.Barrel => "barrel",
        ConnType.Muzzle => "muzzle",
        ConnType.Optic => "optic",
        ConnType.Feed => "feed",
        ConnType.ShaftIn => "shaft in",
        ConnType.ShaftOut => "shaft out",
        ConnType.Fuel => "fuel",
        ConnType.Power => "power",
        ConnType.Data => "data",
        ConnType.Hub => "wheel hub",
        _ => "hinge leaf",
    };

    /// <summary>The marker colour of a connection type.</summary>
    public static Vector3 Colour(ConnType t) => t switch
    {
        ConnType.Mount => new Vector3(0.35f, 0.75f, 1f),
        ConnType.Barrel => new Vector3(1f, 0.55f, 0.2f),
        ConnType.Muzzle => new Vector3(1f, 0.85f, 0.25f),
        ConnType.Optic => new Vector3(0.75f, 0.5f, 1f),
        ConnType.Feed => new Vector3(0.9f, 0.45f, 0.6f),
        ConnType.ShaftIn => new Vector3(0.3f, 0.95f, 0.8f),
        ConnType.ShaftOut => new Vector3(0.2f, 0.75f, 1f),
        ConnType.Fuel => new Vector3(1f, 0.4f, 0.3f),
        ConnType.Power => new Vector3(1f, 0.95f, 0.3f),
        ConnType.Data => new Vector3(0.6f, 1f, 0.45f),
        ConnType.Hub => new Vector3(0.95f, 0.95f, 0.95f),
        _ => new Vector3(0.8f, 0.65f, 0.45f),
    };

    // ---- Classification ------------------------------------------------------------------------------------------------

    private static DefInfo Build(ConstructPartDef d)
    {
        var n = d.Sockets.Length;
        var info = new DefInfo { Own = new ConnType[n], Port = new ConnType[n] };
        for (var s = 0; s < n; s++)
        {
            var sk = d.Sockets[s];
            var t = ConnType.Mount;
            if (sk.Kind == SocketKind.Axle)
                t = ConnType.Hub;
            else if (sk.Kind == SocketKind.Leaf || d.HasJoint && s == d.LeafIndex)
                t = ConnType.Leaf;
            else
                switch (d.Role)
                {
                    case PartRole.Engine:
                        t = sk.Dir == 5 ? ConnType.ShaftOut : sk.Dir == 4 ? ConnType.Fuel : ConnType.Mount;
                        break;
                    case PartRole.Transmission or PartRole.Transfer or PartRole.Differential:
                        t = sk.Dir == 4 ? ConnType.ShaftIn : sk.Dir == 5 ? ConnType.ShaftOut : ConnType.Mount;
                        break;
                    case PartRole.FuelTank or PartRole.FuelLine:
                        t = sk.Dir is 4 or 5 ? ConnType.Fuel : ConnType.Mount;
                        break;
                    case PartRole.Battery or PartRole.Alternator:
                        t = sk.Dir == 4 ? ConnType.Power : ConnType.Mount;
                        break;
                    case PartRole.Seat:
                        t = sk.Name == "back" ? ConnType.Data : ConnType.Mount;
                        break;
                }
            info.Own[s] = info.Port[s] = t;
        }
        if (d.IsReceiverGun)
        {
            // A receiver mounts on a frame by any face; as a target it offers its front to a barrel, its top to a sight, the rest to feeds.
            for (var s = 0; s < n; s++)
            {
                var dir = d.Sockets[s].Dir;
                info.Own[s] = ConnType.Mount;
                info.Port[s] = dir == 5 ? ConnType.Barrel : dir == 2 ? ConnType.Optic : dir == 4 ? ConnType.Mount : ConnType.Feed;
            }
        }
        if (d.Role == PartRole.GunPart)
        {
            info.Single = true;
            var cat2 = Parts.Find(d.GunPartName)?.Category ?? PartCategory.Barrel;
            var any = false;
            for (var s = 0; s < n; s++)
            {
                var name = d.Sockets[s].Name;
                var own = ConnType.Mount;
                var port = ConnType.Mount;
                switch (cat2)
                {
                    case PartCategory.Barrel:
                        own = name == "back" ? ConnType.Barrel : ConnType.Mount;
                        port = name == "front" ? ConnType.Muzzle : ConnType.Mount;
                        break;
                    case PartCategory.Splitter:
                        own = name == "back" ? ConnType.Barrel : ConnType.Mount;
                        port = name.StartsWith("front", StringComparison.Ordinal) ? ConnType.Barrel : ConnType.Mount;
                        break;
                    case PartCategory.Muzzle:
                        own = name == "back" ? ConnType.Muzzle : ConnType.Mount;
                        break;
                    case PartCategory.Optic:
                        own = ConnType.Optic;
                        break;
                    default: // feeds and ammo
                        own = ConnType.Feed;
                        break;
                }
                info.Own[s] = own;
                info.Port[s] = port;
                any |= own != ConnType.Mount;
            }
            if (!any)
                for (var s = 0; s < n; s++)
                    info.Own[s] = cat2 switch { PartCategory.Muzzle => ConnType.Muzzle, PartCategory.Optic => ConnType.Optic, PartCategory.Barrel or PartCategory.Splitter => ConnType.Barrel, _ => ConnType.Feed };
        }
        else if (d.Role is PartRole.Wheel or PartRole.Door)
            info.Single = true;
        var order = new List<byte>(n);
        for (var pass = 0; pass < 2; pass++)
            for (var s = 0; s < n; s++)
            {
                if (d.HasJoint && s == d.LeafIndex)
                    continue; // (a hinge goes on by its base: its leaf is what moves)
                var functional = info.Own[s] != ConnType.Mount;
                if (info.Single && !functional)
                    continue;
                if (pass == 0 ? !functional : functional)
                    order.Add((byte)s);
            }
        info.Order = order.ToArray();
        return info;
    }

    /// <summary>The part's own connection points, all of them, with the type of each and whether the part can mate by it (the held part's markers).</summary>
    public static int OwnPoints(ConstructPartDef d, Span<OwnPoint> into)
    {
        var info = InfoOf(d);
        var n = Math.Min(info.Own.Length, into.Length);
        for (var s = 0; s < n; s++)
            into[s] = new OwnPoint { Socket = s, Type = info.Own[s], Usable = info.Order.AsSpan().Contains((byte)s) };
        return n;
    }

    /// <summary>
    /// Can the part's own socket <paramref name="own"/> mate socket <paramref name="socket"/> of part <paramref name="part"/>
    /// (kinds, then types)? A cheap filter before <see cref="ConstructBuild.Place"/>, never wider than it.
    /// </summary>
    public static bool CanMate(Construct c, ConstructPartDef def, int own, int part, int socket)
    {
        var td = c.DefOf(part);
        var centre = socket >= td.Sockets.Length;
        var ts = td.Sockets[socket % td.Sockets.Length];
        var ds = def.Sockets[own];
        var onLeaf = td.HasJoint && socket == td.LeafIndex;
        if (ds.Kind != ts.Kind && !(onLeaf && ds.Kind == SocketKind.Leaf))
            return false;
        var ot = OwnType(def, own);
        if (ot == ConnType.Mount)
            return true;
        if (def.Role == PartRole.GunPart && (centre || td is { Role: PartRole.Gun, IsReceiverGun: false }))
            return true; // (a merged receiver's centre sockets and a heavy gun take any gun part)
        var port = centre ? ConnType.Mount : onLeaf ? ConnType.Leaf : PortType(td, socket);
        return Mates(ot, port);
    }

    // ---- Ghosts ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The first own socket and spin of <paramref name="def"/> that fits socket <paramref name="socket"/> of part <paramref name="part"/>,
    /// the structural ones first, or the own socket <paramref name="prefer"/> first (the switch key). False: none fits.
    /// </summary>
    public static bool Fit(Construct c, ConstructPartDef def, int part, int socket, int prefer, out ConstructGhost g)
    {
        var order = OwnOrder(def);
        if (prefer >= 0 && order.Contains((byte)prefer) && CanMate(c, def, prefer, part, socket))
            for (var spin = 0; spin < 4; spin++)
                if (ConstructBuild.Place(c, part, socket, def, prefer, spin, out g) == null)
                {
                    g.Variant = prefer * 4 + spin;
                    return true;
                }
        for (var k = 0; k < order.Length; k++)
        {
            var o = order[k];
            if (o == prefer || !CanMate(c, def, o, part, socket))
                continue;
            for (var spin = 0; spin < 4; spin++)
                if (ConstructBuild.Place(c, part, socket, def, o, spin, out g) == null)
                {
                    g.Variant = o * 4 + spin;
                    return true;
                }
        }
        g = default;
        return false;
    }

    /// <summary>
    /// The ghost sockets near <paramref name="near"/> (rest frame, within <paramref name="radius"/>): one per cached free
    /// socket the held part can really go on, by its structural mount first. The same list as
    /// <see cref="ConstructBuild.Ghosts"/> gives, minus the sockets and own faces the part has no business with.
    /// </summary>
    public static int Ghosts(Construct c, ConstructPartDef def, SnapCache cache, Span<ConstructGhost> into, Vector3 near, float radius)
    {
        cache.Refresh(c, def);
        var n = 0;
        var r2 = radius * radius;
        for (var k = 0; k < cache.Count && n < into.Length; k++)
        {
            if (Vector3.DistanceSquared(cache.At[k], near) > r2)
                continue;
            if (Fit(c, def, cache.Part[k], cache.Socket[k], -1, out var g))
                into[n++] = g;
        }
        return n;
    }

    // ---- Screen magnet -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cached socket nearest the crosshair on screen, within <paramref name="pixels"/> (nearest by screen distance, then
    /// depth: a socket in front of one behind it at the same spot wins). -1: none. <paramref name="viewProj"/> is row-vector,
    /// <paramref name="size"/> the window in pixels; the crosshair is its middle.
    /// </summary>
    public static int Magnet(Construct c, SnapCache cache, in Matrix4x4 viewProj, Vector2 size, float pixels)
    {
        var best = -1;
        var bestD = pixels * pixels;
        var bestDepth = float.MaxValue;
        var half = size * 0.5f;
        for (var k = 0; k < cache.Count; k++)
        {
            c.SegPose(cache.Seg[k], out var q, out var t);
            var world = c.ToWorld(t + Vector3.Transform(cache.At[k], q));
            var clip = Vector4.Transform(new Vector4(world, 1), viewProj);
            if (clip.W <= 0.01f)
                continue; // behind the camera
            var sx = (clip.X / clip.W) * half.X;
            var sy = (clip.Y / clip.W) * half.Y;
            var d2 = sx * sx + sy * sy;
            if (d2 > bestD + 1f)
                continue;
            if (d2 < bestD - 1f || Math.Abs(d2 - bestD) <= 1f && clip.W < bestDepth)
            {
                best = k;
                bestD = MathF.Min(d2, bestD);
                bestDepth = clip.W;
            }
        }
        return best;
    }

    /// <summary>The cached socket nearest <paramref name="near"/> (rest frame) within <paramref name="reach"/> metres; -1: none.</summary>
    public static int Nearest(SnapCache cache, Vector3 near, float reach)
    {
        var best = -1;
        var bestD = reach * reach;
        for (var k = 0; k < cache.Count; k++)
        {
            var d = Vector3.DistanceSquared(cache.At[k], near);
            if (d <= bestD)
            {
                bestD = d;
                best = k;
            }
        }
        return best;
    }

    // ---- Smart rotation and the switch key ---------------------------------------------------------------------------------

    /// <summary>
    /// Smart rotation's ranked variants limited to the own sockets snap building allows, and (stable) the preferred own
    /// socket's first (the structural mount when <paramref name="prefer"/> is -1). Returns how many are left.
    /// </summary>
    public static int FilterRank(ConstructPartDef def, Span<int> variants, Span<byte> rots, int n, int prefer)
    {
        var order = OwnOrder(def);
        var info = InfoOf(def);
        var keep = 0;
        Span<int> srcV = stackalloc int[n];
        Span<byte> srcR = stackalloc byte[n];
        variants[..n].CopyTo(srcV);
        rots[..n].CopyTo(srcR);
        for (var pass = 0; pass < 2; pass++)
            for (var k = 0; k < n; k++)
            {
                var own = srcV[k] / 4;
                if (!order.Contains((byte)own))
                    continue;
                var first = prefer >= 0 ? own == prefer : info.Own[own] == ConnType.Mount;
                if (pass == 0 ? !first : first)
                    continue;
                variants[keep] = srcV[k];
                rots[keep] = srcR[k];
                keep++;
            }
        return keep;
    }

    /// <summary>
    /// The switch key: the next own socket after the one <paramref name="currentVariant"/> uses that fits the target, as a
    /// variant (its first spin that fits). -1: the part has no other way on there.
    /// </summary>
    public static int NextOwn(Construct c, ConstructPartDef def, int part, int socket, int currentVariant)
    {
        var order = OwnOrder(def);
        var cur = currentVariant / 4;
        var at = order.IndexOf((byte)cur);
        for (var k = 1; k <= order.Length; k++)
        {
            var o = order[((at < 0 ? -1 : at) + k + order.Length) % order.Length];
            if (o == cur || !CanMate(c, def, o, part, socket))
                continue;
            for (var spin = 0; spin < 4; spin++)
                if (ConstructBuild.Place(c, part, socket, def, o, spin, out _) == null)
                    return o * 4 + spin;
        }
        return -1;
    }

    // ---- Auto-connect ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The functional points of the part, set as <paramref name="g"/> says, that line up with a free socket of the construct
    /// that takes them (same place, facing it): the connections placing it will make (<see cref="ConstructBuild.AutoLink"/>
    /// welds them). Returns how many.
    /// </summary>
    public static int Lineups(Construct c, ConstructPartDef def, in ConstructGhost g, Span<Lineup> into)
    {
        var n = 0;
        for (var o = 0; o < def.Sockets.Length && n < into.Length; o++)
        {
            var ot = OwnType(def, o);
            if (ot == ConnType.Mount || def.HasJoint && o == def.LeafIndex)
                continue;
            var sc = g.Scale > 1 ? g.Scale : 1f;
            var at = g.Pos + Rot24.Apply(g.Rot, def.Sockets[o].At * sc);
            var dir = Rot24.ApplyDir(g.Rot, def.Sockets[o].Dir);
            for (var i = 0; i < c.PartCount && n < into.Length; i++)
            {
                if (c.Parts[i].Broken || c.Parts[i].Seg != g.Seg)
                    continue;
                var td = c.DefOf(i);
                for (var s = 0; s < td.Sockets.Length; s++)
                {
                    if (c.SocketUsed(i, s) || !Mates(ot, PortType(td, s)))
                        continue;
                    c.SocketAt(i, s, out var tat, out var tdir);
                    if (tdir != (byte)(dir ^ 1) || Vector3.DistanceSquared(tat, at) > 0.0004f)
                        continue;
                    into[n++] = new Lineup { OwnSocket = o, Part = i, Socket = s, Type = ot };
                    break;
                }
            }
        }
        return n;
    }
}
