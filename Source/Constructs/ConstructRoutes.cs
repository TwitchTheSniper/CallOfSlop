using System.Numerics;

namespace CallOfSlop;

/// <summary>What a routed link carries (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"). The ids are wire bytes: add new ones at the end.</summary>
public enum RouteType : byte
{
    Data, // a small oval cable: on/off and number signals (milestone 2's wires)
    Power, // a larger oval cable: electricity from batteries and alternators to what draws it
    Pipe, // a round pipe with flanges: fluids from tanks to engines (the fuel line part moved here)
    Shaft, // a solid drive shaft: torque from the engine and gearboxes to gearboxes, differentials and hubs
    Chute, // a square duct: items between storage parts
    COUNT,
}

/// <summary>What a pipe can carry (a tank's <c>"fluid"</c>; only fuel tanks exist so far). One pipe carries any of them (decision 28).</summary>
public enum Fluid : byte
{
    Fuel,
    Water,
    Oil,
    Steam,
}

/// <summary>A node of a routed link: where it sits (the rest frame of its host part's segment, on the 1 cm grid), the part it sits on, and the health of the segment that arrives at it.</summary>
public struct CRouteNode
{
    public Vector3 Pos;
    public byte Host; // the part it is fixed to (its segment carries it: links on jointed parts move with them)
    public float Health; // the segment from the previous point to this node
}

/// <summary>
/// One routed link (decision 20): a polyline from an output (a part's port, or a junction node on another link of the
/// same type) through its nodes to an input. Segment <c>k</c> ends at node <c>k</c>; the last one (index
/// <see cref="Count"/>) ends at the input. Nodes are in <see cref="Construct.RouteNodes"/> from <see cref="Start"/>.
/// </summary>
public struct CRoute
{
    public RouteType Type;
    public byte From, To; // parts (From is the branch's source part once rebuilt; while Parent is set, From is unused: 255)
    public byte FromPort, ToPort; // a socket of the part (shafts), a named data port (DataPortBase | k, the spawners), or 255: the part itself
    public byte Parent, ParentNode; // a branch: the link and its node it starts at (255: from a part)
    public ushort Start;
    public byte Count;
    public float EndHealth; // the last segment (last node -> the input)
    public uint Paint; // the paint tool's colour for its segments (0: the type's own; else 0x01000000 | 0xRRGGBB; ConstructPaint.cs)

    // Rebuilt with the caches (Construct.Rebuild -> ConstructRoutes.Rebuild)
    public byte Source; // the output part at the root of its tree
    public byte SourcePort; // the port it leaves that part by (the root link's FromPort)
    public byte FirstCut; // the first destroyed segment (Count + 1: none)
    public bool Fed; // its start is fed (every segment from the source to it intact)
    public float Share0; // its share where it starts (1 from a part; at a junction, the parent's flow there split evenly)
    public float Share; // its share of the source's flow at its input (data copies: 1)
    public byte Bends; // segments meeting at an angle (a shaft's U joints)

    // Server state
    public float Timer; // seconds: a chute's next move
    public byte Cursor; // a chute junction's round robin

    public readonly bool FedEnd => Fed && FirstCut > Count;
}

/// <summary>Routed links on a construct (fixed arrays, like everything else here: nothing allocates).</summary>
public sealed partial class Construct
{
    public const int MaxRoutes = 64;
    public const int MaxRouteNodes = 768;
    public const int MaxNodesPerRoute = 32;

    public readonly CRoute[] Routes = new CRoute[MaxRoutes];
    public int RouteCount;
    public readonly CRouteNode[] RouteNodes = new CRouteNode[MaxRouteNodes];
    public int RouteNodeCount;

    // Rebuilt: what the links connect (the drive line's shafts, the circuit's cables).
    public readonly CLink[] ShaftEdges = new CLink[MaxRoutes];
    public readonly float[] ShaftEff = new float[MaxRoutes];
    public int ShaftEdgeCount;
    public readonly CLink[] PowerEdges = new CLink[MaxRoutes];
    public int PowerEdgeCount;
    public bool Sparking; // client and server: a cut power cable sparks (drawn)

    public ref CRouteNode Node(int route, int k) => ref RouteNodes[Routes[route].Start + k];

    /// <summary>Routed links into part <paramref name="part"/> of a type (data: its inputs).</summary>
    public int RoutesInto(int part, RouteType type)
    {
        var n = 0;
        for (var k = 0; k < RouteCount; k++)
            if (Routes[k].To == part && Routes[k].Type == type)
                n++;
        return n;
    }
}

/// <summary>
/// Routed links (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): the linking tool's physical connections,
/// routed node by node over a construct. One system for five types (data and power cables, one pipe for any fluid,
/// drive shafts and chutes), each drawn by one segment piece and one elbow piece (ConstructRender), each with its
/// routing rule (<see cref="Check"/>), health and what it carries. A link is a graph edge, never a physics body: the
/// data links are the logic's wires (<see cref="Construct.Wires"/> is rebuilt from them), power links join the
/// circuit, pipes feed engines, shafts are drive-line edges, chutes move stored items. Nodes sit in the rest frame of the
/// part they are on, so links on jointed parts move with them (a cable flexes across a joint; a shaft may not cross one).
/// Server-authoritative: the client proposes a route (CAct.Route), the server checks it with the same rules.
/// </summary>
public static partial class ConstructRoutes
{
    // ---- Tunable constants (the developer's answers of 2026-10-30, and the proposals) ------------------------------

    public const float PipeStepDeg = 15f; // a pipe bends in 15 degree steps
    public const float ChuteStepDeg = 45f; // a chute bends in 45 degree steps (developer, 2026-10-30)
    public const float ShaftMaxBendDeg = 45f; // a shaft bends at most this much at a node, at a U joint (developer, 2026-10-30)
    public const bool ShaftJunctionNeedsTBox = true; // a shaft branches only through a T gearbox device (developer, 2026-10-30)
    public const float AngleSlackDeg = 3.5f; // nodes sit on the 1 cm grid: a snapped bend is within this of its step
    public const float Grid = 0.05f; // the node grid on a construct (rest frame)
    public const float MinSegment = 0.08f; // a segment at least this long
    public const float MaxSegment = 8f; // and at most this long (a straight link: milestone 2's wire reach)
    public const float MaxLength = 24f; // a whole link
    public const float LeakLitres = 0.5f; // a cut fuel pipe loses this much a second while the tank holds any
    public const float ChuteSeconds = 1f; // a chute moves one item (or a load of bulk) a second
    public const int ChuteLoad = 10; // bulk per move
    public const float ShaftBendEff = 0.99f; // what each U joint of a shaft link keeps

    public static float Radius(RouteType t) => t switch
    {
        RouteType.Data => 0.02f,
        RouteType.Power => 0.035f,
        RouteType.Pipe => 0.05f,
        RouteType.Shaft => 0.05f,
        _ => 0.15f,
    };

    /// <summary>A segment's health by type: cables weak, pipes and chutes medium, shafts strong.</summary>
    public static float Health(RouteType t) => t switch
    {
        RouteType.Data => 20f,
        RouteType.Power => 40f,
        RouteType.Pipe => 80f,
        RouteType.Shaft => 250f,
        _ => 80f,
    };

    private static readonly string[] TypeNames = ["data cable", "power cable", "pipe", "drive shaft", "chute"];

    public static string Name(RouteType t) => TypeNames[Math.Min((int)t, TypeNames.Length - 1)];

    public static string TypeKey(RouteType t) => t switch { RouteType.Data => "data", RouteType.Power => "power", RouteType.Pipe => "pipe", RouteType.Shaft => "shaft", _ => "chute" };

    public static RouteType? TypeOf(string key) => key.ToLowerInvariant() switch
    {
        "data" => RouteType.Data,
        "power" => RouteType.Power,
        "pipe" => RouteType.Pipe,
        "shaft" => RouteType.Shaft,
        "chute" => RouteType.Chute,
        _ => null,
    };

    /// <summary>The fluid a part puts out (a fuel tank's fuel), or null.</summary>
    public static Fluid? FluidOf(ConstructPartDef d) => d.Role == PartRole.FuelTank ? d.TankFluid : null;

    // ---- What plugs in where ------------------------------------------------------------------------------------

    /// <summary>Is part type <paramref name="d"/> an output of links of type <paramref name="t"/>?</summary>
    public static bool IsOutput(ConstructPartDef d, RouteType t) => t switch
    {
        RouteType.Data => d.HasOutput,
        RouteType.Power => d.Role is PartRole.Battery or PartRole.Alternator,
        RouteType.Pipe => d.Role == PartRole.FuelTank,
        RouteType.Shaft => d.Role is PartRole.Engine or PartRole.Transmission or PartRole.Transfer or PartRole.Differential,
        _ => d.Role == PartRole.Storage,
    };

    /// <summary>Is part type <paramref name="d"/> an input of links of type <paramref name="t"/>?</summary>
    public static bool IsInput(ConstructPartDef d, RouteType t) => t switch
    {
        RouteType.Data => d.MaxInputs > 0,
        RouteType.Power => d.Electric,
        RouteType.Pipe => d.Role == PartRole.Engine && d.FuelKind == EngineFuel.Fuel,
        RouteType.Shaft => d.Role is PartRole.Transmission or PartRole.Transfer or PartRole.Differential,
        _ => d.Role == PartRole.Storage,
    };

    /// <summary>
    /// Does a click on part <paramref name="part"/> end a link of type <paramref name="type"/> being routed from part
    /// <paramref name="from"/> (<paramref name="parent"/> 255; else a branch)? On an input of its type, not back on its own
    /// output (a logic part may feed itself). Never with <paramref name="free"/> routing (board item ls01: the device is
    /// then only a surface for an ordinary node, so a link can run along a device with a matching port).
    /// </summary>
    public static bool ClickEnds(Construct c, RouteType type, int from, int parent, int part, bool free)
    {
        if (free || part < 0 || part >= c.PartCount)
            return false;
        var d = c.DefOf(part);
        return IsInput(d, type) && !(part == from && parent == 255 && !(type == RouteType.Data && d.Role == PartRole.Logic));
    }

    public static string OutputHint(RouteType t) => t switch
    {
        RouteType.Data => "start a data cable at a seat, a control, a sensor or a gate",
        RouteType.Power => "start a power cable at a battery or an alternator",
        RouteType.Pipe => "start a pipe at a tank",
        RouteType.Shaft => "start a drive shaft at an engine, a gearbox, a transfer part or a differential",
        _ => "start a chute at a storage part",
    };

    public static string InputHint(RouteType t) => t switch
    {
        RouteType.Data => "end it at a part that takes an input",
        RouteType.Power => "end it at a part that draws power",
        RouteType.Pipe => "end it at an engine",
        RouteType.Shaft => "end it at a gearbox, a transfer part or a differential",
        _ => "end it at a storage part",
    };

    /// <summary>A named data port's byte in a link (<see cref="CRoute.FromPort"/>, <see cref="CRoute.ToPort"/>): port 0 is 255 (the part itself), port k is this | k.</summary>
    public const byte DataPortBase = 0x80;

    /// <summary>The byte for data port <paramref name="k"/> (0: the part itself, 255).</summary>
    public static byte DataPortByte(int k) => k <= 0 ? (byte)255 : (byte)(DataPortBase | k);

    /// <summary>A data link's port byte as a port index (255: 0), or -1 for a byte that names no data port.</summary>
    public static int DataPort(byte port) => port == 255 ? 0 : (port & DataPortBase) != 0 && (port & 0x7F) < 16 ? port & 0x7F : -1;

    /// <summary>
    /// The ports of part <paramref name="part"/> a link of type <paramref name="t"/> can use (shafts: its free drive
    /// sockets; a data link at a part with named data ports, the spawners: each of its inputs, or outputs when
    /// <paramref name="input"/> is false; the others: the part itself, port 255). Returns how many were written into <paramref name="ports"/>.
    /// </summary>
    public static int Ports(Construct c, int part, RouteType t, Span<byte> ports, bool input = true)
    {
        if (part < 0 || part >= c.PartCount || ports.Length == 0)
            return 0;
        if (t != RouteType.Shaft)
        {
            var names = t == RouteType.Data ? input ? c.DefOf(part).DataIn : c.DefOf(part).DataOut : [];
            if (names.Length <= 1)
            {
                ports[0] = 255;
                return 1;
            }
            var k = 0;
            for (; k < names.Length && k < ports.Length; k++)
                ports[k] = DataPortByte(k);
            return k;
        }
        var d = c.DefOf(part);
        var n = 0;
        for (var s = 0; s < d.Sockets.Length && n < ports.Length; s++)
        {
            if (d.Ports.Length > 0 && Array.IndexOf(d.Ports, s) < 0)
                continue;
            if (d.Sockets[s].Kind is SocketKind.Axle or SocketKind.Leaf || c.SocketUsed(part, s) || ShaftOnPort(c, part, s))
                continue;
            ports[n++] = (byte)s;
        }
        return n;
    }

    /// <summary>A port's name for the picker and blueprints: a socket's, a named data port's ("spawn now"), or "" for the part itself.</summary>
    public static string PortName(ConstructPartDef d, RouteType t, byte port, bool input)
    {
        if (port == 255)
            return t == RouteType.Data && (input ? d.DataIn : d.DataOut) is { Length: > 1 } named ? named[0] : "";
        if (t == RouteType.Data)
        {
            var names = input ? d.DataIn : d.DataOut;
            var k = DataPort(port);
            return k >= 0 && k < names.Length ? names[k] : "";
        }
        return port < d.Sockets.Length ? d.Sockets[port].Name : "";
    }

    /// <summary>The port byte a name stands for on a part (a socket, or a named data port for a data link), or 255 when there's no such port.</summary>
    public static byte PortByName(ConstructPartDef d, RouteType t, string name, bool input)
    {
        if (t == RouteType.Data)
        {
            var names = input ? d.DataIn : d.DataOut;
            var k = Array.IndexOf(names, name);
            return k > 0 ? DataPortByte(k) : (byte)255; // (port 0 is the part itself)
        }
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Name == name)
                return (byte)s;
        return 255;
    }

    /// <summary>Does a part have a data port named <paramref name="name"/> (an input, or an output)?</summary>
    public static bool HasDataPort(ConstructPartDef d, string name, bool input) => Array.IndexOf(input ? d.DataIn : d.DataOut, name) >= 0;

    private static bool ShaftOnPort(Construct c, int part, int socket)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Shaft && (r.From == part && r.FromPort == socket || r.To == part && r.ToPort == socket))
                return true;
        }
        return false;
    }

    /// <summary>Where a link meets a part: a socket (shafts), else the part's centre. Rest frame of the part's segment.</summary>
    public static Vector3 PortPoint(Construct c, int part, byte port)
    {
        if (port != 255 && port < c.DefOf(part).Sockets.Length)
        {
            c.SocketAt(part, port, out var at, out _);
            return at;
        }
        c.PartBounds(part, out var mn, out var mx);
        return (mn + mx) * 0.5f;
    }

    /// <summary>The direction a port faces (outward), or zero for a part's centre.</summary>
    public static Vector3 PortDir(Construct c, int part, byte port)
    {
        if (port == 255 || port >= c.DefOf(part).Sockets.Length)
            return Vector3.Zero;
        c.SocketAt(part, port, out _, out var dir);
        return ConstructParts.DirVector(dir);
    }

    /// <summary>
    /// Point <paramref name="i"/> of link <paramref name="r"/>: -1 its start (the output's port, or the junction node it
    /// branches from), 0..Count-1 its nodes, Count its end (the input's port). Rest frame of segment <paramref name="seg"/>.
    /// </summary>
    public static Vector3 Point(Construct c, int r, int i, out int seg)
    {
        ref var rt = ref c.Routes[r];
        if (i < 0)
        {
            if (rt.Parent != 255 && rt.Parent < c.RouteCount)
            {
                ref var pn = ref c.Node(rt.Parent, rt.ParentNode);
                seg = pn.Host < c.PartCount ? c.Parts[pn.Host].Seg : 0;
                return pn.Pos;
            }
            seg = rt.From < c.PartCount ? c.Parts[rt.From].Seg : 0;
            return rt.From < c.PartCount ? PortPoint(c, rt.From, rt.FromPort) : Vector3.Zero;
        }
        if (i < rt.Count)
        {
            ref var n = ref c.RouteNodes[rt.Start + i];
            seg = n.Host < c.PartCount ? c.Parts[n.Host].Seg : 0;
            return n.Pos;
        }
        seg = rt.To < c.PartCount ? c.Parts[rt.To].Seg : 0;
        return rt.To < c.PartCount ? PortPoint(c, rt.To, rt.ToPort) : Vector3.Zero;
    }

    /// <summary>Point <paramref name="i"/> of link <paramref name="r"/> in the world (its segment as it is now).</summary>
    public static Vector3 WorldPoint(Construct c, int r, int i)
    {
        var p = Point(c, r, i, out var seg);
        c.WorldFrame(seg, out var q, out var t);
        return t + Vector3.Transform(p, q);
    }

    public static float SegmentHealth(Construct c, int r, int k) => k < c.Routes[r].Count ? c.RouteNodes[c.Routes[r].Start + k].Health : c.Routes[r].EndHealth;

    // ---- The caches ----------------------------------------------------------------------------------------------

    /// <summary>
    /// With the caches (before the drive line and the circuit): each link's source, whether it is fed, its share at a
    /// junction (split evenly for power, fluids, torque and items; data copies); the data links
    /// become the logic's wires, the shafts drive-line edges, the power cables circuit edges. Branches always come after
    /// the link they branch from, so one pass in order does it.
    /// </summary>
    public static void Rebuild(Construct c)
    {
        c.WireCount = 0;
        c.ShaftEdgeCount = 0;
        c.PowerEdgeCount = 0;
        Span<byte> branches = stackalloc byte[Construct.MaxRouteNodes];
        branches.Clear();
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Parent != 255 && r.Parent < k && r.ParentNode < c.Routes[r.Parent].Count)
                branches[c.Routes[r.Parent].Start + r.ParentNode]++;
        }
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            r.FirstCut = (byte)(r.Count + 1);
            for (var s = 0; s <= r.Count; s++)
                if (SegmentHealth(c, k, s) <= 0)
                {
                    r.FirstCut = (byte)s;
                    break;
                }
            r.Bends = 0;
            var split = r.Type != RouteType.Data;
            float arrive;
            if (r.Parent != 255 && r.Parent < k)
            {
                ref var p = ref c.Routes[r.Parent];
                r.Source = p.Source;
                r.SourcePort = p.SourcePort;
                r.Fed = p.Fed && p.FirstCut > r.ParentNode;
                // The flow reaching the junction node, shared between the parent's way on and every branch there.
                arrive = p.Share0;
                for (var j = 0; j < r.ParentNode; j++)
                    if (split && branches[p.Start + j] > 0)
                        arrive /= 1 + branches[p.Start + j];
                arrive = split ? arrive / (1 + branches[p.Start + r.ParentNode]) : arrive;
            }
            else
            {
                r.Source = r.From;
                r.SourcePort = r.FromPort;
                r.Fed = r.From < c.PartCount;
                arrive = 1f;
            }
            r.Share0 = arrive;
            var end = arrive;
            if (split)
                for (var j = 0; j < r.Count; j++)
                    if (branches[r.Start + j] > 0)
                        end /= 1 + branches[r.Start + j];
            r.Share = end;
            if (r.Type == RouteType.Shaft)
            {
                var prev = Vector3.Zero;
                for (var i = -1; i < r.Count; i++)
                {
                    var d = Point(c, k, i + 1, out _) - Point(c, k, i, out _);
                    if (d.LengthSquared() < 1e-8f)
                        continue;
                    d = Vector3.Normalize(d);
                    if (prev != Vector3.Zero && Vector3.Dot(prev, d) < 0.9998f)
                        r.Bends++;
                    prev = d;
                }
            }
            if (!r.FedEnd || r.To >= c.PartCount || r.Source >= c.PartCount)
                continue;
            switch (r.Type)
            {
                case RouteType.Data:
                    var dup = false;
                    var fromK = (byte)Math.Max(0, DataPort(r.SourcePort));
                    var toK = (byte)Math.Max(0, DataPort(r.ToPort));
                    for (var w = 0; w < c.WireCount && !dup; w++)
                        dup = c.Wires[w].From == r.Source && c.Wires[w].To == r.To && c.Wires[w].FromPort == fromK && c.Wires[w].ToPort == toK;
                    if (!dup && c.WireCount < Construct.MaxWires)
                        c.Wires[c.WireCount++] = new CWire { From = r.Source, To = r.To, FromPort = fromK, ToPort = toK };
                    break;
                case RouteType.Shaft:
                    if (r.Parent == 255 && c.ShaftEdgeCount < Construct.MaxRoutes)
                    {
                        c.ShaftEdges[c.ShaftEdgeCount] = new CLink { A = r.From, SA = r.FromPort, B = r.To, SB = r.ToPort };
                        c.ShaftEff[c.ShaftEdgeCount++] = MathF.Pow(ShaftBendEff, r.Bends);
                    }
                    break;
                case RouteType.Power:
                    if (c.PowerEdgeCount < Construct.MaxRoutes)
                        c.PowerEdges[c.PowerEdgeCount++] = new CLink { A = r.Source, B = r.To };
                    break;
            }
        }
    }

    /// <summary>Does an intact pipe carry fuel from a working tank to engine <paramref name="engine"/>?</summary>
    public static bool PipeFeeds(Construct c, int engine)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Pipe && r.To == engine && r.FedEnd && r.Source < c.PartCount && c.Parts[r.Source].D.Role == PartRole.FuelTank && c.Parts[r.Source].Working)
                return true;
        }
        return false;
    }

    // ---- The routing rules ---------------------------------------------------------------------------------------

    /// <summary>
    /// Can this link go on construct <paramref name="c"/>? Null when it can, else the reason (the preview shows it in red).
    /// <paramref name="parent"/> 255: from part <paramref name="from"/>'s port; otherwise a branch from link
    /// <paramref name="parent"/>'s segment <paramref name="parentSeg"/> at <paramref name="junction"/> (rest frame).
    /// <paramref name="nodes"/>: the nodes (rest frame, quantised, and the part each sits on). <paramref name="straight"/>:
    /// a migrated wire or fuel line (no nodes, checked for reach only).
    /// </summary>
    public static string? Check(ConstructWorld? world, Construct c, RouteType type, int from, byte fromPort, int parent, int parentSeg, Vector3 junction,
        ReadOnlySpan<CRouteNode> nodes, int to, byte toPort, bool straight = false)
    {
        if (type >= RouteType.COUNT)
            return "no such link type";
        if (to < 0 || to >= c.PartCount)
            return "no such part";
        if (c.RouteCount >= Construct.MaxRoutes)
            return $"at most {Construct.MaxRoutes} links a construct";
        if (nodes.Length > Construct.MaxNodesPerRoute)
            return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
        if (c.RouteNodeCount + nodes.Length + 1 > Construct.MaxRouteNodes)
            return "this construct has all the link nodes it can take";
        if (world != null && world.TotalRoutes >= world.WireCap)
            return "the world has all the links it can take";
        int source;
        if (parent != 255)
        {
            if (parent < 0 || parent >= c.RouteCount)
                return "that link is gone";
            ref var p = ref c.Routes[parent];
            if (p.Type != type)
                return $"a {Name(type)} can't branch off a {Name(p.Type)}";
            if (type == RouteType.Shaft && ShaftJunctionNeedsTBox)
                return "a drive shaft branches only through a T gearbox: end the shaft at one and start the branch from its other port";
            if (parentSeg < 0 || parentSeg > p.Count)
                return "no such segment";
            if (SegmentHealth(c, parent, parentSeg) <= 0)
                return "that segment is cut: repair it first";
            if (c.Routes[parent].Count + 1 > Construct.MaxNodesPerRoute)
                return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
            source = p.Source;
            // The junction must lie on that segment.
            var a = Point(c, parent, parentSeg - 1, out _);
            var b = Point(c, parent, parentSeg, out _);
            Geometry.ClosestSegmentSegment(a, b, junction, junction, out var on, out _);
            if (Vector3.Distance(on, junction) > 0.03f)
                return "the branch must start on the link";
        }
        else
        {
            if (from < 0 || from >= c.PartCount)
                return "no such part";
            source = from;
        }
        var fd = c.DefOf(source);
        var td = c.DefOf(to);
        if (c.Parts[source].Broken || c.Parts[to].Broken)
            return "that part is wrecked";
        if (parent == 255 && !IsOutput(fd, type))
            return $"a {fd.Name} has no {Name(type)} output ({OutputHint(type)})";
        if (!IsInput(td, type))
            return $"a {td.Name} takes no {Name(type)} ({InputHint(type)})";
        if (parent == 255 && from == to && !(type == RouteType.Data && td.Role == PartRole.Logic))
            return "a part can't be linked to itself";
        if (type == RouteType.Data)
        {
            if (td.Logic == CLogic.Reader && fd.Role != PartRole.Seat)
                return "an input reader reads a seat: wire it from one";
            if (c.RoutesInto(to, RouteType.Data) >= td.MaxInputs)
                return $"a {td.Name} takes at most {td.MaxInputs} input{(td.MaxInputs == 1 ? "" : "s")}";
        }
        var srcPort = parent != 255 ? c.Routes[parent].SourcePort : fromPort;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == type && c.Routes[k].Source == source && c.Routes[k].To == to && (type != RouteType.Data || c.Routes[k].SourcePort == srcPort && c.Routes[k].ToPort == toPort))
                return "those are already linked";
        if (type == RouteType.Shaft)
        {
            if (fromPort == 255 || toPort == 255 || fromPort >= fd.Sockets.Length || toPort >= td.Sockets.Length)
                return "a drive shaft goes into a drive port";
            Span<byte> ports = stackalloc byte[16];
            if (!Contains(ports[..Ports(c, from, type, ports)], fromPort))
                return $"that port of the {fd.Name} is taken or carries no drive";
            if (!Contains(ports[..Ports(c, to, type, ports)], toPort))
                return $"that port of the {td.Name} is taken or carries no drive";
        }
        else if (type == RouteType.Data && (fromPort != 255 || toPort != 255))
        {
            // Named data ports (the spawners): an output port of the source, an input port of the part it goes into.
            var fk = DataPort(fromPort);
            var tk = DataPort(toPort);
            if (fk < 0 || fromPort != 255 && (parent != 255 || fk >= fd.DataOut.Length) || tk < 0 || toPort != 255 && tk >= td.DataIn.Length)
                return "that part has no such data port";
        }
        else if (fromPort != 255 || toPort != 255)
            return "only a drive shaft goes into a socket";
        // The geometry: every point in the rest frame (each segment at joint angle 0), checked by the one path rule the
        // client's preview uses too (PathRule).
        Span<Vector3> pts = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
        Span<int> hosts = stackalloc int[Construct.MaxNodesPerRoute + 2];
        var n = 0;
        if (parent != 255)
        {
            pts[n] = junction;
            hosts[n++] = HostOf(c, parent, parentSeg);
        }
        else
        {
            pts[n] = PortPoint(c, from, fromPort);
            hosts[n++] = from;
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Host >= c.PartCount)
                return "a node must sit on this construct";
            if (!float.IsFinite(nodes[i].Pos.X + nodes[i].Pos.Y + nodes[i].Pos.Z))
                return "bad node";
            pts[n] = nodes[i].Pos;
            hosts[n++] = nodes[i].Host;
        }
        pts[n] = PortPoint(c, to, toPort);
        hosts[n++] = to;
        return PathRule(c, type, pts[..n], hosts[..n], parent != 255, parent != 255 ? (byte)255 : fromPort, true, toPort, straight);
    }

    /// <summary>
    /// The routing rule for a path (rest frame), one rule for the server's <see cref="Check"/> and the client's preview:
    /// <paramref name="pts"/>[0] is the start (the output's port point, or a junction on a parent link when
    /// <paramref name="junctionStart"/>), then the nodes, then, when <paramref name="closed"/>, the input's port point
    /// (an open path is a link still being routed: its last point is the candidate node). <paramref name="hosts"/>: the
    /// part each point is on (the start's: the output, or the parent segment's host; the end's: the input). Checks the
    /// segment lengths, a shaft crossing a joint, nodes sinking into parts and segments passing through them (with the
    /// clipping grace at the ends and the embedding forgiveness: <see cref="Grace"/>, <see cref="EmbedFraction"/>), and
    /// the type's angle rule. Null when it is fine, else the reason. Nothing allocates.
    /// </summary>
    public static string? PathRule(Construct c, RouteType type, ReadOnlySpan<Vector3> pts, ReadOnlySpan<int> hosts, bool junctionStart, byte fromPort, bool closed, byte toPort,
        bool straight = false)
    {
        var n = pts.Length;
        if (n < 2 || hosts.Length < n)
            return null;
        for (var i = 0; i < n; i++)
            if (hosts[i] < 0 || hosts[i] >= c.PartCount)
                return "a node must sit on this construct";
        var radius = Radius(type);
        Span<float> along = stackalloc float[Construct.MaxNodesPerRoute + 3];
        if (n > along.Length)
            return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
        along[0] = 0;
        for (var i = 1; i < n; i++)
        {
            var len = Vector3.Distance(pts[i - 1], pts[i]);
            if (!straight && len < MinSegment)
                return "those nodes are too close together";
            if (len > MaxSegment)
                return $"a segment reaches at most {MaxSegment:0} m: add a node";
            along[i] = along[i - 1] + len;
        }
        var length = along[n - 1];
        if (length > MaxLength)
            return $"a link reaches at most {MaxLength:0} m";
        if (straight)
            return null;
        if (type == RouteType.Shaft)
            for (var i = 1; i < n; i++)
                if (c.Parts[hosts[i]].Seg != c.Parts[hosts[0]].Seg)
                    return "a drive shaft can't cross a joint: fit a U joint or CV joint there";
        // The clipping grace (board item lg01): from where the link leaves its output (or from its junction) and up to
        // where it enters its input, nothing clips it for Grace(radius).
        var graceStart = Grace(radius) + (junctionStart ? 0f : ExitAlong(c, hosts[0], pts[0], pts[1]));
        var graceEnd = closed ? Grace(radius) + ExitAlong(c, hosts[n - 1], pts[n - 1], pts[n - 2]) : 0f;
        // Nodes don't sink into a part (deeper than the embedding forgiveness), outside the grace.
        var last = closed ? n - 1 : n;
        for (var i = 1; i < last; i++)
        {
            if (along[i] <= graceStart || closed && length - along[i] <= graceEnd)
                continue;
            if (NodeSinks(c, pts[i], radius) is { } inside)
                return $"that node would sink into the {c.DefOf(inside).Name}: place it on the surface";
        }
        // Segments don't pass through solid parts (the output and input themselves excepted on the end segments, where it
        // plugs in), outside the grace.
        for (var i = 1; i < n; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var len = along[i] - along[i - 1];
            var t0 = MathF.Max(0f, graceStart - along[i - 1]);
            var t1 = len - (closed ? MathF.Max(0f, graceEnd - (length - along[i])) : 0f);
            if (t1 <= t0 || len < 1e-6f)
                continue;
            var d = (b - a) / len;
            var skipA = i == 1 ? hosts[0] : -1;
            var skipB = closed && i == n - 1 ? hosts[n - 1] : -1;
            if (SegmentBlocked(c, a + d * t0, a + d * t1, radius, skipA, skipB) is { } wall)
                return $"it would pass through the {c.DefOf(wall).Name}: route it round (or through an opening)";
        }
        // The type's angle rule.
        var prev = type == RouteType.Shaft && !junctionStart ? PortDir(c, hosts[0], fromPort) : Vector3.Zero;
        for (var i = 1; i < n; i++)
        {
            var d = Vector3.Normalize(pts[i] - pts[i - 1]);
            if (prev != Vector3.Zero)
            {
                var bend = MathF.Acos(Math.Clamp(Vector3.Dot(prev, d), -1f, 1f)) * 180f / MathF.PI;
                if (BendRule(type, bend) is { } why)
                    return why;
            }
            prev = d;
        }
        if (type == RouteType.Shaft && closed)
        {
            var into = -PortDir(c, hosts[n - 1], toPort);
            var bend = MathF.Acos(Math.Clamp(Vector3.Dot(prev, into), -1f, 1f)) * 180f / MathF.PI;
            if (bend > ShaftMaxBendDeg + AngleSlackDeg)
                return $"a drive shaft goes into a port at most {ShaftMaxBendDeg:0} degrees off its line ({bend:0} here)";
        }
        return null;
    }

    private static bool Contains(ReadOnlySpan<byte> s, byte v)
    {
        foreach (var x in s)
            if (x == v)
                return true;
        return false;
    }

    /// <summary>Why a bend of <paramref name="deg"/> degrees breaks a type's rule, or null.</summary>
    public static string? BendRule(RouteType type, float deg)
    {
        switch (type)
        {
            case RouteType.Pipe:
            case RouteType.Chute:
            {
                var step = type == RouteType.Pipe ? PipeStepDeg : ChuteStepDeg;
                var off = MathF.Abs(deg - MathF.Round(deg / step) * step);
                if (off > AngleSlackDeg)
                    return $"a {(type == RouteType.Pipe ? "pipe" : "chute")} bends in {step:0} degree steps ({deg:0} here)";
                return null;
            }
            case RouteType.Shaft:
                if (deg > 80f)
                    return "a drive shaft can't turn a corner: fit a right-angle gearbox there";
                if (deg > ShaftMaxBendDeg + AngleSlackDeg)
                    return $"a drive shaft bends at most {ShaftMaxBendDeg:0} degrees at a U joint ({deg:0} here)";
                return null;
            default:
                return null;
        }
    }

    /// <summary>The part a segment of a link is fixed to at its far end (a junction on it sits there).</summary>
    public static int HostOf(Construct c, int route, int seg)
    {
        ref var r = ref c.Routes[route];
        return seg < r.Count ? c.RouteNodes[r.Start + seg].Host : r.To;
    }

    // ---- Clipping grace and embedding forgiveness (board item dev-sandbox-link-clip-grace-lg01) -------------------

    public const float GraceDiameters = 2f; // the clipping grace at each end: this many of the link's diameters...
    public const float GraceExtra = 0.10f; // ...plus this much (metres)
    public const float EmbedFraction = 0.6f; // a link may sink this much of its thickness into a part
    public const float EmbedSlack = 0.002f; // (float noise on the 1 cm grid)

    /// <summary>How far from its output (or junction) and from its input a link of <paramref name="radius"/> clips nothing.</summary>
    public static float Grace(float radius) => GraceDiameters * 2f * radius + GraceExtra;

    /// <summary>How far a link's centreline may be inside a part's surface: (2 x EmbedFraction - 1) x radius (0.2 x radius at 60%).</summary>
    public static float EmbedDepth(float radius) => (2f * EmbedFraction - 1f) * radius;

    /// <summary>The signed distance from <paramref name="p"/> to the nearest solid box (rest frame; negative inside one), and its part (-1: none).</summary>
    public static float SignedDistance(Construct c, Vector3 p, int skipA, int skipB, out int part)
    {
        var best = float.MaxValue;
        part = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (i == skipA || i == skipB)
                continue;
            var d = c.DefOf(i);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var q = Vector3.Abs(p - centre) - half;
                var sd = Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
                if (sd < best)
                {
                    best = sd;
                    part = i;
                }
            }
        }
        return best;
    }

    /// <summary>The solid part a node of a link of <paramref name="radius"/> at <paramref name="p"/> sinks into past the embedding forgiveness (rest frame), or null.</summary>
    public static int? NodeSinks(Construct c, Vector3 p, float radius, int skipA = -1, int skipB = -1)
    {
        var sd = SignedDistance(c, p, skipA, skipB, out var part);
        return part >= 0 && sd < -(EmbedDepth(radius) + EmbedSlack) ? part : null;
    }

    /// <summary>The solid part a segment of a link of <paramref name="radius"/> passes through past the embedding forgiveness (each box shrunk by <see cref="EmbedDepth"/>), or null.</summary>
    public static int? SegmentBlocked(Construct c, Vector3 a, Vector3 b, float radius, int skipA, int skipB) =>
        Blocked(c, a, b, -(EmbedDepth(radius) + EmbedSlack), skipA, skipB);

    /// <summary>The solid part segment ab passes through (each box grown by <paramref name="grow"/>, shrunk when negative), or null.</summary>
    public static int? Blocked(Construct c, Vector3 a, Vector3 b, float grow, int skipA, int skipB)
    {
        var d = b - a;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (i == skipA || i == skipB)
                continue;
            var def = c.DefOf(i);
            for (var k = 0; k < def.SolidBoxes; k++)
            {
                c.BoxOf(i, k, out var centre, out var half);
                var h = half + new Vector3(grow);
                if (h.X <= 0 || h.Y <= 0 || h.Z <= 0)
                    continue;
                if (SegmentHitsBox(a - centre, d, h))
                    return i;
            }
        }
        return null;
    }

    /// <summary>How far along segment ab (from a) it runs inside part <paramref name="part"/>'s solid boxes before it leaves them (0: a is outside).</summary>
    public static float ExitAlong(Construct c, int part, Vector3 a, Vector3 b)
    {
        if (part < 0 || part >= c.PartCount)
            return 0f;
        var d = b - a;
        var len = d.Length();
        if (len < 1e-6f)
            return 0f;
        var def = c.DefOf(part);
        var exit = 0f;
        // (boxes that touch: keep extending while one starts where the run so far ends)
        for (var pass = 0; pass < 4; pass++)
        {
            var grew = false;
            for (var k = 0; k < def.SolidBoxes; k++)
            {
                c.BoxOf(part, k, out var centre, out var half);
                if (!SegmentBoxSpan(a - centre, d, half, out var t0, out var t1))
                    continue;
                if (t0 * len <= exit + 1e-3f && t1 * len > exit + 1e-5f)
                {
                    exit = MathF.Min(t1, 1f) * len;
                    grew = true;
                }
            }
            if (!grew)
                break;
        }
        return exit;
    }

    private static bool SegmentBoxSpan(Vector3 o, Vector3 d, Vector3 h, out float t0, out float t1)
    {
        t0 = float.MinValue;
        t1 = float.MaxValue;
        for (var ax = 0; ax < 3; ax++)
        {
            var oa = o[ax];
            var da = d[ax];
            if (MathF.Abs(da) < 1e-9f)
            {
                if (oa < -h[ax] || oa > h[ax])
                    return false;
                continue;
            }
            var ta = (-h[ax] - oa) / da;
            var tb = (h[ax] - oa) / da;
            if (ta > tb)
                (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            if (t0 > t1)
                return false;
        }
        return t1 >= 0f && t0 <= 1f;
    }

    // ---- Hold to adjust a node (board item lg01: the client's adjust mode; the server re-checks the result) -------

    public const int AdjustMaxCm = 50; // the search and the wheel go at most this far out along the normal
    public const int AdjustStepCm = 1, AdjustBigStepCm = 5; // a wheel notch (Ctrl: the big step)

    /// <summary>How far in (cm, negative) a node may be adjusted from its snapped spot (radius off the surface): down to the embedding limit.</summary>
    public static int AdjustMinCm(RouteType type)
    {
        var r = Radius(type);
        return -(int)MathF.Floor((r + EmbedDepth(r)) * 100f + 1e-3f);
    }

    /// <summary>The node <paramref name="cm"/> centimetres along <paramref name="normal"/> from <paramref name="basePos"/> (rest frame, on the 1 cm grid).</summary>
    public static Vector3 AdjustPos(Vector3 basePos, Vector3 normal, int cm)
    {
        var nrm = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
        return ConstructWorld.Quantise(basePos + nrm * (cm * 0.01f));
    }

    /// <summary>Clamps an adjust offset to its range.</summary>
    public static int AdjustClamp(RouteType type, int cm) => Math.Clamp(cm, AdjustMinCm(type), AdjustMaxCm);

    /// <summary>
    /// The adjust search: from the snapped spot, out along the normal first (1 cm steps up to <see cref="AdjustMaxCm"/>),
    /// then in down to the embedding limit; the first offset whose node <paramref name="rule"/> accepts (null), or null.
    /// </summary>
    public static int? AdjustSearch<T>(RouteType type, Vector3 basePos, Vector3 normal, ref T rule) where T : struct, INodeRule
    {
        if (rule.Why(AdjustPos(basePos, normal, 0)) == null)
            return 0;
        for (var cm = 1; cm <= AdjustMaxCm; cm++)
            if (rule.Why(AdjustPos(basePos, normal, cm)) == null)
                return cm;
        for (var cm = -1; cm >= AdjustMinCm(type); cm--)
            if (rule.Why(AdjustPos(basePos, normal, cm)) == null)
                return cm;
        return null;
    }

    /// <summary>A rule for one node's spot (the adjust search tries spots through it): null when the link is fine with the node there.</summary>
    public interface INodeRule
    {
        string? Why(Vector3 pos);
    }

    /// <summary>
    /// The rule for a link being routed (open) with its next node at a spot: the start (an output's port, or a junction on
    /// <paramref name="Parent"/>), the nodes so far, then the spot. One rule with the server's (<see cref="PathRule"/>).
    /// </summary>
    public struct OpenRule : INodeRule
    {
        public Construct C;
        public RouteType Type;
        public int From; // the output (when Parent is 255)
        public byte FromPort;
        public int Parent, ParentSeg; // a branch: the link and its segment (255: from a part)
        public Vector3 Junction;
        public CRouteNode[] Nodes; // the nodes so far (Count of them)
        public int Count;
        public byte Host; // the part the next node sits on

        public readonly string? Why(Vector3 pos) => OpenPath(C, Type, From, FromPort, Parent, ParentSeg, Junction, Nodes.AsSpan(0, Count), pos, Host);
    }

    /// <summary>The rule for a link being routed with its next node at <paramref name="next"/> on part <paramref name="host"/> (see <see cref="OpenRule"/>).</summary>
    public static string? OpenPath(Construct c, RouteType type, int from, byte fromPort, int parent, int parentSeg, Vector3 junction, ReadOnlySpan<CRouteNode> nodes, Vector3 next,
        int host)
    {
        if (nodes.Length >= Construct.MaxNodesPerRoute)
            return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
        if (host < 0 || host >= c.PartCount)
            return "a node must sit on this construct";
        Span<Vector3> pts = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
        Span<int> hosts = stackalloc int[Construct.MaxNodesPerRoute + 2];
        var n = 0;
        if (parent != 255)
        {
            if (parent < 0 || parent >= c.RouteCount)
                return "that link is gone";
            pts[n] = junction;
            hosts[n++] = HostOf(c, parent, parentSeg);
        }
        else
        {
            if (from < 0 || from >= c.PartCount)
                return "no such part";
            pts[n] = PortPoint(c, from, fromPort);
            hosts[n++] = from;
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            pts[n] = nodes[i].Pos;
            hosts[n++] = nodes[i].Host;
        }
        pts[n] = next;
        hosts[n++] = host;
        return PathRule(c, type, pts[..n], hosts[..n], parent != 255, parent != 255 ? (byte)255 : fromPort, false, 255);
    }

    /// <summary>The rule for a whole link (the server's <see cref="Check"/>) with node <c>Node</c> of its nodes at a spot: the input end's adjust (its last node).</summary>
    public struct ClosedRule : INodeRule
    {
        public ConstructWorld? World;
        public Construct C;
        public RouteType Type;
        public int From;
        public byte FromPort;
        public int Parent, ParentSeg;
        public Vector3 Junction;
        public CRouteNode[] Nodes;
        public int Count, Node;
        public int To;
        public byte ToPort;

        public readonly string? Why(Vector3 pos)
        {
            var keep = Nodes[Node].Pos;
            Nodes[Node].Pos = pos;
            var why = Check(World, C, Type, From, FromPort, Parent, ParentSeg, Junction, Nodes.AsSpan(0, Count), To, ToPort);
            Nodes[Node].Pos = keep;
            return why;
        }
    }

    /// <summary>The rule for moving node <c>Node</c> of link <c>Route</c> (the edit mode's move node; the server's <see cref="CheckMove"/>).</summary>
    public struct MoveRule : INodeRule
    {
        public Construct C;
        public int Route, Node;
        public byte Host;

        public readonly string? Why(Vector3 pos) => CheckMove(C, Route, Node, pos, Host);
    }

    private static bool SegmentHitsBox(Vector3 o, Vector3 d, Vector3 h)
    {
        float t0 = 0, t1 = 1;
        for (var ax = 0; ax < 3; ax++)
        {
            var oa = o[ax];
            var da = d[ax];
            if (MathF.Abs(da) < 1e-9f)
            {
                if (oa < -h[ax] || oa > h[ax])
                    return false;
                continue;
            }
            var ta = (-h[ax] - oa) / da;
            var tb = (h[ax] - oa) / da;
            if (ta > tb)
                (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta);
            t1 = MathF.Min(t1, tb);
            if (t0 > t1)
                return false;
        }
        return true;
    }

    // ---- Placing a node (the client's preview; the server re-checks the result) --------------------------------

    /// <summary>
    /// Where the next node goes for the surface point <paramref name="surface"/> with normal <paramref name="normal"/>
    /// (both rest frame): off the surface by the link's radius, on the construct's grid across the surface, onto the
    /// straight line on from the last segment when close to it, lined up with a neighbouring link's node, then turned to
    /// the type's angle rule (a pipe's 15 degree steps, a chute's 45, a shaft's 45 degree limit). Quantised to 1 cm.
    /// </summary>
    public static Vector3 Snap(Construct c, RouteType type, Vector3 prev, Vector3 prevDir, Vector3 surface, Vector3 normal)
    {
        var radius = Radius(type);
        var n = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var p = surface + n * radius;
        // The grid, on the two axes along the surface (the normal's axis keeps the offset).
        var axis = MathF.Abs(n.X) > MathF.Abs(n.Y) ? MathF.Abs(n.X) > MathF.Abs(n.Z) ? 0 : 2 : MathF.Abs(n.Y) > MathF.Abs(n.Z) ? 1 : 2;
        for (var a = 0; a < 3; a++)
            if (a != axis)
                p[a] = MathF.Round(p[a] / Grid) * Grid;
        // Line up with a neighbouring link's node (parallel runs), within 4 cm on an axis along the surface.
        for (var k = 0; k < c.RouteNodeCount; k++)
            for (var a = 0; a < 3; a++)
                if (a != axis && MathF.Abs(c.RouteNodes[k].Pos[a] - p[a]) < 0.04f && MathF.Abs(c.RouteNodes[k].Pos[axis] - p[axis]) < 0.3f)
                    p[a] = c.RouteNodes[k].Pos[a];
        return AngleSnap(type, prev, prevDir, p);
    }

    /// <summary>
    /// The angle part of <see cref="Snap"/> (shared with the world links' <see cref="WorldSnap"/>): a point
    /// <paramref name="p"/> on from <paramref name="prev"/> goes onto the straight line on when close to it, else turned to
    /// the type's angle rule (a pipe's 15 degree steps, a chute's 45, a shaft's 45 degree limit). Quantised to 1 cm.
    /// </summary>
    public static Vector3 AngleSnap(RouteType type, Vector3 prev, Vector3 prevDir, Vector3 p)
    {
        var d = p - prev;
        var len = d.Length();
        if (len < 1e-4f)
            return ConstructWorld.Quantise(p);
        var dir = d / len;
        // A straight continuation when nearly on the last segment's line.
        if (prevDir != Vector3.Zero && Vector3.Dot(dir, prevDir) > MathF.Cos(6f * MathF.PI / 180f))
            return ConstructWorld.Quantise(prev + prevDir * Vector3.Dot(d, prevDir));
        if (prevDir == Vector3.Zero || type is RouteType.Data or RouteType.Power)
            return ConstructWorld.Quantise(p);
        var bend = MathF.Acos(Math.Clamp(Vector3.Dot(prevDir, dir), -1f, 1f));
        float want;
        if (type == RouteType.Shaft)
            want = MathF.Min(bend, ShaftMaxBendDeg * MathF.PI / 180f);
        else
        {
            var step = (type == RouteType.Pipe ? PipeStepDeg : ChuteStepDeg) * MathF.PI / 180f;
            want = MathF.Round(bend / step) * step;
        }
        if (MathF.Abs(want - bend) < 1e-4f)
            return ConstructWorld.Quantise(p);
        var turnAxis = Vector3.Cross(prevDir, dir);
        if (turnAxis.LengthSquared() < 1e-8f)
            return ConstructWorld.Quantise(p);
        var turned = Vector3.Transform(prevDir, Quaternion.CreateFromAxisAngle(Vector3.Normalize(turnAxis), want));
        // As far along the snapped direction as the surface point is (its projection), at least the shortest segment.
        var along = MathF.Max(MinSegment + 0.02f, Vector3.Dot(d, turned));
        // Long enough that the 1 cm grid keeps the bend within its slack.
        along = MathF.Max(along, 0.25f);
        return ConstructWorld.Quantise(prev + turned * along);
    }

    // ---- Edits (server) ------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a checked link (call <see cref="Check"/> first): a branch inserts its junction node into the parent's
    /// segment first. Returns the new link's index, or -1 when full.
    /// </summary>
    public static int Add(ConstructWorld? world, Construct c, RouteType type, int from, byte fromPort, int parent, int parentSeg, Vector3 junction,
        ReadOnlySpan<CRouteNode> nodes, int to, byte toPort)
    {
        if (c.RouteCount >= Construct.MaxRoutes || c.RouteNodeCount + nodes.Length + 1 > Construct.MaxRouteNodes)
            return -1;
        var parentNode = 255;
        if (parent != 255)
        {
            parentNode = parentSeg;
            InsertNode(c, parent, parentSeg, new CRouteNode { Pos = ConstructWorld.Quantise(junction), Host = (byte)HostOf(c, parent, parentSeg), Health = SegmentHealth(c, parent, parentSeg) });
        }
        var k = c.RouteCount++;
        c.Routes[k] = new CRoute
        {
            Type = type,
            From = parent != 255 ? (byte)255 : (byte)from,
            FromPort = parent != 255 ? (byte)255 : fromPort,
            To = (byte)to,
            ToPort = toPort,
            Parent = (byte)parent,
            ParentNode = (byte)parentNode,
            Start = (ushort)c.RouteNodeCount,
            Count = (byte)nodes.Length,
            EndHealth = Health(type),
            Source = parent != 255 ? c.Routes[parent].Source : (byte)from,
            Fed = true,
            FirstCut = (byte)(nodes.Length + 1),
            Share0 = 1f,
            Share = 1f,
        };
        for (var i = 0; i < nodes.Length; i++)
            c.RouteNodes[c.RouteNodeCount++] = new CRouteNode { Pos = ConstructWorld.Quantise(nodes[i].Pos), Host = nodes[i].Host, Health = Health(type) };
        Changed(world, c);
        return k;
    }

    private static void Changed(ConstructWorld? world, Construct c)
    {
        c.Dirty = true;
        c.Wake();
        world?.StructureDirty.Add(c.Id);
    }

    /// <summary>Inserts a node into link <paramref name="route"/> before segment <paramref name="seg"/>'s end (a junction, a moved re-route).</summary>
    private static void InsertNode(Construct c, int route, int seg, CRouteNode node)
    {
        ref var r = ref c.Routes[route];
        var at = r.Start + seg;
        for (var i = c.RouteNodeCount; i > at; i--)
            c.RouteNodes[i] = c.RouteNodes[i - 1];
        c.RouteNodes[at] = node;
        c.RouteNodeCount++;
        r.Count++;
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (k != route && c.Routes[k].Start >= at && c.Routes[k].Start > r.Start)
                c.Routes[k].Start++;
            if (c.Routes[k].Parent == route && c.Routes[k].ParentNode >= seg)
                c.Routes[k].ParentNode++;
        }
    }

    /// <summary>Removes link <paramref name="route"/> and every branch off it. Returns how many links went.</summary>
    public static int Remove(ConstructWorld? world, Construct c, int route)
    {
        if (route < 0 || route >= c.RouteCount)
            return 0;
        Span<bool> gone = stackalloc bool[Construct.MaxRoutes];
        gone.Clear();
        gone[route] = true;
        for (var k = route + 1; k < c.RouteCount; k++)
            if (c.Routes[k].Parent != 255 && c.Routes[k].Parent < c.RouteCount && gone[c.Routes[k].Parent])
                gone[k] = true;
        var n = Compact(c, gone);
        if (n > 0)
            Changed(world, c);
        return n;
    }

    /// <summary>Removes every link into or out of part <paramref name="part"/> (of a type, or all: <paramref name="type"/> null). Returns how many went.</summary>
    public static int RemoveAt(ConstructWorld? world, Construct c, int part, RouteType? type = null)
    {
        Span<bool> gone = stackalloc bool[Construct.MaxRoutes];
        gone.Clear();
        var any = false;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (type is { } t && r.Type != t)
                continue;
            if (r.From == part || r.To == part || r.Parent != 255 && r.Parent < k && gone[r.Parent])
                any = gone[k] = true;
        }
        if (!any)
            return 0;
        var n = Compact(c, gone);
        Changed(world, c);
        return n;
    }

    /// <summary>Drops the links marked <paramref name="gone"/> (and their nodes), keeping the rest in order with their parents renumbered.</summary>
    private static int Compact(Construct c, Span<bool> gone)
    {
        Span<int> map = stackalloc int[Construct.MaxRoutes];
        var rn = 0;
        var nn = 0;
        var removed = 0;
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (gone[k] || c.Routes[k].Parent != 255 && (c.Routes[k].Parent >= k || map[c.Routes[k].Parent] < 0))
            {
                map[k] = -1;
                removed++;
                continue;
            }
            var r = c.Routes[k];
            for (var i = 0; i < r.Count; i++)
                c.RouteNodes[nn + i] = c.RouteNodes[r.Start + i];
            r.Start = (ushort)nn;
            nn += r.Count;
            if (r.Parent != 255)
                r.Parent = (byte)map[r.Parent];
            map[k] = rn;
            c.Routes[rn++] = r;
        }
        c.RouteCount = rn;
        c.RouteNodeCount = nn;
        return removed;
    }

    /// <summary>
    /// After parts were renumbered (<paramref name="map"/>: old index to new, -1 gone): links whose ends went are dropped
    /// (with their branches); a node whose part went stays where it was on the link's start part, its segment cut (the
    /// surface under it is gone). <paramref name="src"/> may be <paramref name="dst"/> (a compaction) or not (a piece split off).
    /// </summary>
    public static void Remap(Construct src, ReadOnlySpan<int> map, Construct dst)
    {
        Span<int> routeMap = stackalloc int[Construct.MaxRoutes];
        var same = src == dst;
        var rn = same ? 0 : dst.RouteCount;
        var nn = same ? 0 : dst.RouteNodeCount;
        var first = rn;
        for (var k = 0; k < src.RouteCount; k++)
        {
            var r = src.Routes[k];
            routeMap[k] = -1;
            var from = r.Parent != 255 ? 0 : r.From < map.Length ? map[r.From] : -1;
            var to = r.To < map.Length ? map[r.To] : -1;
            var parent = r.Parent == 255 ? -2 : r.Parent < k ? routeMap[r.Parent] : -1;
            if (from < 0 || to < 0 || parent == -1 || rn >= Construct.MaxRoutes || nn + r.Count > Construct.MaxRouteNodes)
                continue;
            var start = r.Start;
            r.Start = (ushort)nn;
            var anchor = r.Parent != 255 ? to : from;
            for (var i = 0; i < r.Count; i++)
            {
                var node = src.RouteNodes[start + i];
                var host = node.Host < map.Length ? map[node.Host] : -1;
                if (host < 0)
                {
                    host = anchor;
                    node.Health = 0; // (the part it sat on is gone: that segment is cut)
                }
                node.Host = (byte)host;
                dst.RouteNodes[nn++] = node;
            }
            if (r.Parent == 255)
                r.From = (byte)from;
            else
                r.Parent = (byte)parent;
            r.To = (byte)to;
            routeMap[k] = rn;
            dst.Routes[rn++] = r;
        }
        dst.RouteCount = rn;
        dst.RouteNodeCount = nn;
        _ = first;
    }

    /// <summary>Copies <paramref name="b"/>'s links into <paramref name="a"/> (a join: b's parts now start at <paramref name="off"/>, its frame moved by <paramref name="offset"/> and turned by <paramref name="turn"/>).</summary>
    public static void Merge(Construct a, Construct b, int off, Vector3 offset, byte turn)
    {
        var baseRoute = a.RouteCount;
        for (var k = 0; k < b.RouteCount && a.RouteCount < Construct.MaxRoutes; k++)
        {
            var r = b.Routes[k];
            if (a.RouteNodeCount + r.Count > Construct.MaxRouteNodes)
                break;
            var start = r.Start;
            r.Start = (ushort)a.RouteNodeCount;
            for (var i = 0; i < r.Count; i++)
            {
                var node = b.RouteNodes[start + i];
                node.Pos = ConstructWorld.Quantise(offset + Rot24.Apply(turn, node.Pos));
                node.Host = (byte)(node.Host + off);
                a.RouteNodes[a.RouteNodeCount++] = node;
            }
            if (r.Parent != 255)
                r.Parent = (byte)(r.Parent + baseRoute);
            else
                r.From = (byte)(r.From + off);
            r.To = (byte)(r.To + off);
            a.Routes[a.RouteCount++] = r;
        }
    }

    /// <summary>Moves node <paramref name="node"/> of link <paramref name="route"/> (the edit mode's "move node"): the whole link is re-checked with it moved (<see cref="CheckMove"/>). Null, or why not.</summary>
    public static string? MoveNode(ConstructWorld? world, Construct c, int route, int node, Vector3 pos, byte host)
    {
        if (CheckMove(c, route, node, pos, host) is { } why)
            return why;
        ref var r = ref c.Routes[route];
        c.RouteNodes[r.Start + node].Pos = ConstructWorld.Quantise(pos);
        c.RouteNodes[r.Start + node].Host = host;
        Changed(world, c);
        return null;
    }

    /// <summary>Can node <paramref name="node"/> of link <paramref name="route"/> move to <paramref name="pos"/> on part <paramref name="host"/>? Null, or why not (the server's rule and the client's preview).</summary>
    public static string? CheckMove(Construct c, int route, int node, Vector3 pos, byte host)
    {
        if (route < 0 || route >= c.RouteCount || node < 0 || node >= c.Routes[route].Count)
            return "no such node";
        if (host >= c.PartCount)
            return "a node must sit on this construct";
        ref var r = ref c.Routes[route];
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Parent == route && c.Routes[k].ParentNode == node)
                return "a branch starts at that node: move the branch first";
        Span<CRouteNode> nodes = stackalloc CRouteNode[Construct.MaxNodesPerRoute];
        for (var i = 0; i < r.Count; i++)
            nodes[i] = c.RouteNodes[r.Start + i];
        nodes[node] = new CRouteNode { Pos = ConstructWorld.Quantise(pos), Host = host, Health = nodes[node].Health };
        string? why;
        if (r.Parent != 255)
            why = CheckBranchMove(c, r, nodes[..r.Count]);
        else
        {
            // Check it as a new link without itself (its own "already linked" and caps).
            var saved = r;
            r.Type = RouteType.COUNT; // (not matched by the duplicate check while checked)
            why = Check(null, c, saved.Type, saved.From, saved.FromPort, 255, 0, Vector3.Zero, nodes[..saved.Count], saved.To, saved.ToPort);
            r = saved;
        }
        return why != null && !why.StartsWith("at most", StringComparison.Ordinal) ? why : null;
    }

    /// <summary>A branch with its nodes moved: the path rule from its junction (the parent's node) to its input.</summary>
    private static string? CheckBranchMove(Construct c, in CRoute r, ReadOnlySpan<CRouteNode> nodes)
    {
        if (r.Parent >= c.RouteCount || r.ParentNode >= c.Routes[r.Parent].Count || r.To >= c.PartCount)
            return "that link is gone";
        Span<Vector3> pts = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
        Span<int> hosts = stackalloc int[Construct.MaxNodesPerRoute + 2];
        var n = 0;
        ref var pn = ref c.Node(r.Parent, r.ParentNode);
        pts[n] = pn.Pos;
        hosts[n++] = pn.Host;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Host >= c.PartCount)
                return "a node must sit on this construct";
            pts[n] = nodes[i].Pos;
            hosts[n++] = nodes[i].Host;
        }
        pts[n] = PortPoint(c, r.To, r.ToPort);
        hosts[n++] = r.To;
        return PathRule(c, r.Type, pts[..n], hosts[..n], true, 255, true, r.ToPort);
    }

    // ---- Damage and repair ---------------------------------------------------------------------------------------

    /// <summary>
    /// Damage to segment <paramref name="seg"/> of link <paramref name="route"/>. At zero it is cut: what it carried stops
    /// beyond it (a fuel pipe leaks, a power cable sparks; a blast lights the leak). Returns true if it was cut.
    /// </summary>
    public static bool Damage(ConstructWorld world, Construct c, int route, int seg, float damage, bool blast = false)
    {
        if (route < 0 || route >= c.RouteCount || seg < 0 || seg > c.Routes[route].Count || damage <= 0)
            return false;
        ref var r = ref c.Routes[route];
        ref var hp = ref seg < r.Count ? ref c.RouteNodes[r.Start + seg].Health : ref r.EndHealth;
        if (hp <= 0)
            return false;
        hp -= damage;
        c.Wake();
        if (hp > 0)
            return false;
        hp = 0;
        c.Damaged = true;
        Changed(world, c);
        world.Events.Add($"construct {c.Id}: a {Name(r.Type)} segment was cut");
        if (blast && r.Type == RouteType.Pipe && r.Source < c.PartCount && c.Parts[r.Source].D.Role == PartRole.FuelTank && c.Fuel > 0)
            world.Ignite(c, r.Source); // (a blast through a fuel line lights what leaks)
        return true;
    }

    /// <summary>Puts every segment of link <paramref name="route"/> back to full health (the wrench or the welder). Returns true if anything was mended.</summary>
    public static bool Repair(ConstructWorld? world, Construct c, int route)
    {
        if (route < 0 || route >= c.RouteCount)
            return false;
        ref var r = ref c.Routes[route];
        var full = Health(r.Type);
        var mended = r.EndHealth < full;
        r.EndHealth = full;
        for (var i = 0; i < r.Count; i++)
        {
            mended |= c.RouteNodes[r.Start + i].Health < full;
            c.RouteNodes[r.Start + i].Health = full;
        }
        if (mended)
        {
            r.Timer = 0;
            Changed(world, c);
        }
        return mended;
    }

    /// <summary>The nearest link segment along a ray (world), within <paramref name="best"/>; true when one is nearer.</summary>
    public static bool Ray(Construct c, Vector3 origin, Vector3 dir, ref float best, out int route, out int seg, out Vector3 point)
    {
        route = seg = -1;
        point = default;
        var found = false;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            var radius = Radius(r.Type) + 0.01f;
            var a = WorldPoint(c, k, -1);
            for (var i = 0; i <= r.Count; i++)
            {
                var b = WorldPoint(c, k, i);
                var d2 = Geometry.ClosestSegmentSegment(origin, origin + dir * best, a, b, out var onRay, out var onSeg);
                if (d2 <= radius * radius)
                {
                    var t = Vector3.Dot(onRay - origin, dir);
                    if (t >= 0 && t < best)
                    {
                        best = t;
                        route = k;
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

    /// <summary>A blast's damage to every link segment within <paramref name="radius"/> (falling off with distance).</summary>
    public static void Blast(ConstructWorld world, Construct c, Vector3 centre, float radius, float maxDamage)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            var a = WorldPoint(c, k, -1);
            for (var i = 0; i <= c.Routes[k].Count && k < c.RouteCount; i++)
            {
                var b = WorldPoint(c, k, i);
                Geometry.ClosestSegmentSegment(a, b, centre, centre, out var on, out _);
                var dist = Vector3.Distance(on, centre);
                if (dist < radius)
                {
                    var f = 1f - dist / radius;
                    Damage(world, c, k, i, maxDamage * f * f, blast: true);
                }
                a = b;
            }
        }
    }

    // ---- Each tick (server) --------------------------------------------------------------------------------------

    /// <summary>
    /// One tick of what the links do beyond being graph edges: a cut fuel pipe leaks, a cut power cable sparks, and
    /// chutes move items. Nothing allocates; a construct with no links costs nothing.
    /// </summary>
    public static void Step(ConstructWorld world, Construct c, float dt)
    {
        c.Sparking = false;
        if (c.RouteCount == 0)
            return;
        if (c.Dirty)
            c.Rebuild();
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            switch (r.Type)
            {
                case RouteType.Power:
                    if (r.Fed && r.FirstCut <= r.Count && c.Charge > 0)
                        c.Sparking = true;
                    break;
                case RouteType.Pipe:
                    StepPipe(world, c, k, dt);
                    break;
                case RouteType.Chute:
                    if (r.FedEnd)
                        StepChute(world, c, k, dt);
                    break;
            }
        }
    }

    private static void StepPipe(ConstructWorld world, Construct c, int k, float dt)
    {
        ref var r = ref c.Routes[k];
        if (!r.Fed || r.Source >= c.PartCount || c.Parts[r.Source].D.Role != PartRole.FuelTank || !c.Parts[r.Source].Working || c.Fuel <= 0)
            return;
        if (r.FirstCut <= r.Count)
        {
            c.Fuel = MathF.Max(0, c.Fuel - LeakLitres * dt); // (cut: it pours out)
            return;
        }
    }

    private static void StepChute(ConstructWorld world, Construct c, int k, float dt)
    {
        ref var r = ref c.Routes[k];
        r.Timer += dt;
        // A junction shares evenly: each branch waits its share's worth longer (two ways: every other second each).
        var period = ChuteSeconds / MathF.Max(0.05f, r.Share);
        if (r.Timer < period)
            return;
        r.Timer -= period;
        ChuteMove(world, c, r.Source, c, r.To);
    }

    /// <summary>
    /// One move of a chute from storage part <paramref name="from"/> of <paramref name="cf"/> into <paramref name="to"/>
    /// of <paramref name="ct"/> (the same construct, or another through a world link): a load of bulk between bins, else
    /// the first stored item that fits a slot. True when something moved.
    /// </summary>
    public static bool ChuteMove(ConstructWorld world, Construct cf, int from, Construct ct, int to)
    {
        if (from < 0 || from >= cf.PartCount || to < 0 || to >= ct.PartCount || world.StoreOf(cf, from) is not { } src || world.StoreOf(ct, to) is not { } into)
            return false;
        var fd = cf.DefOf(from);
        var td = ct.DefOf(to);
        if (fd.IsBin && td.IsBin)
        {
            var load = Math.Min(ChuteLoad, src.Amount);
            if (load <= 0)
                return false;
            var put = world.Deposit(ct, to, load);
            if (put > 0)
                world.Withdraw(cf, from, put);
            return put > 0;
        }
        for (var s = 0; s < src.SlotCount; s++)
        {
            if (src.Def[s] == ConstructParts.None || ConstructParts.Get(src.Def[s]) is not { } item)
                continue;
            for (var t = 0; t < into.SlotCount; t++)
                if (ConstructStorage.Check(td, into, t, item, out _) == SlotCheck.Ok)
                {
                    if (world.TakeOut(cf, from, s) is { } moved)
                    {
                        world.SetIn(ct, to, t, moved);
                        return true;
                    }
                    return false;
                }
        }
        return false;
    }

    // ---- The wire format (protocol 90): compact, nodes on the 1 cm grid --------------------------------------------

    private static short Cm(float v) => (short)Math.Clamp(MathF.Round(v * 100), short.MinValue, short.MaxValue);

    /// <summary>Writes a construct's links: per link 9 bytes plus a health byte per segment, per node 7 bytes.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.U8((byte)c.RouteCount);
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            w.U8((byte)r.Type).U8(r.From).U8(r.FromPort).U8(r.To).U8(r.ToPort).U8(r.Parent).U8(r.ParentNode).U8(r.Count);
            for (var i = 0; i < r.Count; i++)
            {
                ref var n = ref c.RouteNodes[r.Start + i];
                w.U8(n.Host).U16(unchecked((ushort)Cm(n.Pos.X))).U16(unchecked((ushort)Cm(n.Pos.Y))).U16(unchecked((ushort)Cm(n.Pos.Z))).U8(HealthByte(n.Health, r.Type));
            }
            w.U8(HealthByte(r.EndHealth, r.Type));
        }
    }

    private static byte HealthByte(float hp, RouteType t) => hp <= 0 ? (byte)0 : (byte)Math.Clamp((int)MathF.Ceiling(hp / Health(t) * 255), 1, 255);

    /// <summary>Reads <see cref="Write"/>'s bytes (after the parts: every index is checked against them).</summary>
    public static void Read(ref PacketReader r, Construct c, int parts)
    {
        int count = r.U8();
        if (count > Construct.MaxRoutes)
            throw new MalformedPacketException($"a construct of {count} links");
        c.RouteCount = 0;
        c.RouteNodeCount = 0;
        for (var k = 0; k < count; k++)
        {
            var ts = r.U8(); // (protocol 98: the high nibble was a pipe style; old bytes are masked off)
            var rt = new CRoute
            {
                Type = (RouteType)(ts & 15),
                From = r.U8(),
                FromPort = r.U8(),
                To = r.U8(),
                ToPort = r.U8(),
                Parent = r.U8(),
                ParentNode = r.U8(),
                Count = r.U8(),
            };
            if (rt.Type >= RouteType.COUNT)
                throw new MalformedPacketException($"link {k}: type {ts}");
            if (rt.To >= parts || (rt.Parent == 255 ? rt.From >= parts : rt.Parent >= k || rt.ParentNode >= c.Routes[rt.Parent].Count) || rt.Count > Construct.MaxNodesPerRoute
                || c.RouteNodeCount + rt.Count > Construct.MaxRouteNodes)
                throw new MalformedPacketException($"link {k} doesn't fit its construct");
            rt.Start = (ushort)c.RouteNodeCount;
            var full = Health(rt.Type);
            for (var i = 0; i < rt.Count; i++)
            {
                var host = r.U8();
                var pos = new Vector3(unchecked((short)r.U16()) / 100f, unchecked((short)r.U16()) / 100f, unchecked((short)r.U16()) / 100f);
                var hp = r.U8();
                if (host >= parts)
                    throw new MalformedPacketException($"link {k}: a node on a part that isn't there");
                c.RouteNodes[c.RouteNodeCount++] = new CRouteNode { Pos = pos, Host = host, Health = hp / 255f * full };
            }
            rt.EndHealth = r.U8() / 255f * full;
            c.Routes[c.RouteCount++] = rt;
        }
    }
}
