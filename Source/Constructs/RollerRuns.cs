using System.Numerics;

namespace CallOfSlop;

public sealed partial class Construct
{
    public bool RollersStale; // a rebuild found Roller Tiles or a run's conveyor: the server lays the runs again (RollerRuns.Sync)
}

/// <summary>
/// Roller Tiles and the Turntable Tile (design page drawing 23; docs/sandbox/constructs/production.md "Roller Tiles"). Roller
/// Tiles welded together are one run: MachineBlocks finds each weld from its lowest-index tile (family "roller", seeded as Pad Tiles
/// are) and the largest full rectangle holding it, all turned one way. The run is one conveyor along its middle from its -z end to its
/// +z end, as wide as its tiles across (<see cref="MaxAcross"/> at most), laid by the server as an ordinary conveyor link, so riders,
/// saves and the wire are a conveyor's. It starts at what is butted on its in end (an Out Table, its machine's out table; a
/// Turntable Tile; a Run-out Table, its Extruder's Piece out; any part with a Piece out there) or at its own first tile, and ends at what is butted on its out end (a machine's
/// or a pad's Piece in, a bed fed by conveyor: its Core's Bed in, a Turntable Tile) or at its last tile, where pieces wait for a crane or go on along a conveyor linked from the
/// tile's Piece out. A conveyor linked into a tile's Piece in goes onto its run. A Turntable Tile passes a piece from the run that ends
/// in it onto the others that start at it, in turn, never back the way it came.
/// </summary>
public static class RollerRuns
{
    public const int MaxAcross = 6; // tiles across a run's conveyor: 12 m (a conveyor's width goes in 5 cm steps in a byte further on)
    public const float Cell = 2f, RunY = 0.94f, EndIn = 0.05f; // a tile's size, its run's line over its foot (PieceRackParts.RunY), the line's first node in from its end
    public const string StartPort = "Run start", StartPortHalf = "Run start +x", EndPort = "Run end", EndPortHalf = "Run end +x";

    /// <summary>After MachineBlocks.Build (every peer): a body with a run, or that had one, is laid again on the server's next conveyor step.</summary>
    internal static void Note(Construct c)
    {
        if (c.RollersStale)
            return;
        for (var a = 0; a < c.MachineAsmCount; a++)
            if (c.Parts[c.MachineAsms[a].Core].D.IsRollerTile)
            {
                c.RollersStale = true;
                return;
            }
        for (var k = 0; k < c.RouteCount; k++)
            if (IsRun(c, k))
            {
                c.RollersStale = true;
                return;
            }
    }

    /// <summary>Is link <paramref name="k"/> a run's own conveyor (two nodes, both on Roller Tiles)?</summary>
    public static bool IsRun(Construct c, int k)
    {
        ref readonly var r = ref c.Routes[k];
        if (r.Type != RouteType.Conveyor || r.Parent != 255 || r.Count != 2)
            return false;
        int h0 = c.RouteNodes[r.Start].Host, h1 = c.RouteNodes[r.Start + 1].Host;
        if (h0 >= c.PartCount || h1 >= c.PartCount || !c.Parts[h0].D.IsRollerTile || !c.Parts[h1].D.IsRollerTile)
            return false;
        if (h0 >= c.BlockAsmOf.Length || h1 >= c.BlockAsmOf.Length || c.BlockAsmOf[h0] != c.BlockAsmOf[h1])
            return false; // (a player's link from one run's tile to another's)
        // A run starts at its tile's Run start or at what is butted on it, never at a tile's Piece out; it ends at Run end, never a Piece in.
        return !(r.From < c.PartCount && c.Parts[r.From].D.IsRollerTile && !IsRunPort(c.Parts[r.From].D, r.FromPort))
            && !(r.To < c.PartCount && c.Parts[r.To].D.IsRollerTile && !IsRunPort(c.Parts[r.To].D, r.ToPort));
    }

    /// <summary>The conveyor of the run Roller Tile <paramref name="part"/> is in, or -1.</summary>
    public static int RunOf(Construct c, int part)
    {
        if (part >= c.BlockAsmOf.Length || c.BlockAsmOf[part] < 0)
            return -1;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Count == 2 && IsRun(c, k) && c.BlockAsmOf[c.RouteNodes[c.Routes[k].Start].Host] == c.BlockAsmOf[part])
                return k;
        return -1;
    }

    /// <summary>
    /// The run that ends at Roller Tile <paramref name="tile"/>'s own Run end with no way on (no conveyor off its Piece out): where its
    /// pieces wait for a crane or an arm (ConstructLifters, LiftPointKind.RunEnd). Its conveyor, or -1. Either peer; allocates nothing.
    /// </summary>
    public static int EndRunAt(Construct c, int tile)
    {
        if ((uint)tile >= (uint)c.PartCount || !c.Parts[tile].D.IsRollerTile)
            return -1;
        var d = c.Parts[tile].D;
        var run = -1;
        for (var k = 0; k < c.RouteCount && run < 0; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Conveyor && r.Parent == 255 && r.Count == 2 && r.To == tile && IsRunPort(d, r.ToPort)
                && c.RouteNodes[r.Start + 1].Host == tile && IsRun(c, k))
                run = k;
        }
        if (run < 0)
            return -1;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Parent == 255 && c.Routes[k].From == tile && !IsRunPort(d, c.Routes[k].FromPort))
                return -1; // (it goes on along a conveyor linked from the tile's Piece out)
        return run;
    }

    /// <summary>
    /// The run whose last tile is <paramref name="tile"/> and that ends butted on a bed Core's Bed in (a Panel Former's or a Laser Cutter's
    /// bed fed straight from the run): a piece made for another's order waits whole there for a crane (ConstructConveyor.PastBed). Its
    /// conveyor, or -1. Either peer; allocates nothing.
    /// </summary>
    public static int BedRunAt(Construct c, int tile)
    {
        if ((uint)tile >= (uint)c.PartCount || !c.Parts[tile].D.IsRollerTile)
            return -1;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Conveyor && r.Parent == 255 && r.Count == 2 && r.To < c.PartCount && c.RouteNodes[r.Start + 1].Host == tile
                && (c.Parts[r.To].D.IsPanelFormer && ConstructRoutes.PortOf(c.Parts[r.To].D, RouteType.Conveyor, r.ToPort) is { OnBed: true } || c.Parts[r.To].D.IsLayDownPad) && IsRun(c, k))
                return k; // (or onto a pad: a formed skin for a station's order waits at the pad's edge, too long for it or not its way)
        }
        return -1;
    }

    /// <summary>The lead rider of bed run <paramref name="route"/> when it rides past the bed and has come to its wait at the bed's edge, or -1.</summary>
    public static int PassingLead(Construct c, int route)
    {
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Route == route)
            {
                if (!ConstructConveyor.PassesBed(c, c.Routes[route].To, c.Conveyed[i]) || ConstructParts.Def(c.Conveyed[i].Def) is not { } d)
                    return -1;
                ConstructConveyor.Size(d, out _, out _, out var along);
                var leg = System.Numerics.Vector3.Distance(ConstructRoutes.Point(c, route, c.Routes[route].Count - 1, out _), ConstructRoutes.Point(c, route, c.Routes[route].Count, out _));
                return c.Conveyed[i].At >= ConstructConveyor.Length(c, route) - leg - along * 0.5f - 0.02f ? i : -1;
            }
        return -1;
    }

    /// <summary>
    /// The run that starts at Roller Tile <paramref name="tile"/>'s own Run start (nothing butted on its in end): where a lifter may put
    /// a piece onto it (ConstructLifters, LiftPointKind.RunStart; the hand-offs toward a station). Its conveyor, or -1. Either peer;
    /// allocates nothing.
    /// </summary>
    public static int StartRunAt(Construct c, int tile)
    {
        if ((uint)tile >= (uint)c.PartCount || !c.Parts[tile].D.IsRollerTile)
            return -1;
        var d = c.Parts[tile].D;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Conveyor && r.Parent == 255 && r.Count == 2 && r.From == tile && IsRunPort(d, r.FromPort)
                && c.RouteNodes[r.Start].Host == tile && IsRun(c, k))
                return k;
        }
        return -1;
    }

    /// <summary>The lead rider of conveyor <paramref name="route"/> when it waits at the link's far end, or -1.</summary>
    public static int WaitingLead(Construct c, int route)
    {
        for (var i = 0; i < c.ConveyedCount; i++)
            if (c.Conveyed[i].Route == route)
                return c.Conveyed[i].At >= ConstructConveyor.Length(c, route) - 0.02f ? i : -1; // (riders are kept grouped by link, the lead first)
        return -1;
    }

    private struct Want
    {
        public ushort From, To;
        public byte FromPort, ToPort;
        public Vector3 N0, N1;
        public ushort H0, H1;
        public ushort Width;
        public bool Have;
    }

    private const int MaxRuns = 128;
    private static readonly Want[] _wants = new Want[MaxRuns];

    /// <summary>Server, on the conveyor step after a rebuild that noted rollers: each run's conveyor as its tiles and ends are now.</summary>
    public static void Sync(ConstructWorld world, Construct c)
    {
        c.RollersStale = false;
        var n = 0;
        for (var a = 0; a < c.MachineAsmCount && n < MaxRuns; a++)
            if (c.Parts[c.MachineAsms[a].Core].D.IsRollerTile && Wanted(c, a, out _wants[n]))
                n++;
        var full = n >= MaxRuns;
        if (full)
            Console.WriteLine($"[rollers] {c.Id}: more than {MaxRuns} Roller Tile runs: the rest keep the links they have");
        Span<bool> gone = stackalloc bool[Construct.MaxRoutes];
        gone.Clear();
        var drop = false;
        for (var k = 0; k < c.RouteCount; k++)
        {
            if (!IsRun(c, k))
                continue;
            ref var r = ref c.Routes[k];
            var match = -1;
            for (var w = 0; w < n && match < 0; w++)
                if (!_wants[w].Have && Same(c, r, _wants[w]))
                    match = w;
            if (match < 0)
            {
                if (full)
                    continue; // (perhaps one of the runs past the cap)
                gone[k] = drop = true;
                continue;
            }
            _wants[match].Have = true;
            if (r.ConvW != _wants[match].Width)
            {
                r.ConvW = r.ConvH = _wants[match].Width;
                world.StructureDirty.Add(c.Id);
            }
        }
        if (drop)
            ConstructRoutes.RemoveMany(world, c, gone); // (its riders come off as loose pieces where they were)
        Span<CRouteNode> nodes = stackalloc CRouteNode[2];
        for (var w = 0; w < n; w++)
        {
            ref readonly var want = ref _wants[w];
            if (want.Have)
                continue;
            nodes[0] = new CRouteNode { Pos = want.N0, Host = want.H0 };
            nodes[1] = new CRouteNode { Pos = want.N1, Host = want.H1 };
            var k = ConstructRoutes.Add(world, c, RouteType.Conveyor, want.From, want.FromPort, 255, 0, default, nodes, want.To, want.ToPort);
            if (k < 0)
            {
                Console.WriteLine($"[rollers] {c.Id}: no room for a Roller Tile run's conveyor (links {c.RouteCount}/{Construct.MaxRoutes})");
                break;
            }
            c.Routes[k].ConvW = c.Routes[k].ConvH = want.Width;
        }
    }

    private static bool Same(Construct c, in CRoute r, in Want w) =>
        r.From == w.From && r.FromPort == w.FromPort && r.To == w.To && r.ToPort == w.ToPort
        && c.RouteNodes[r.Start].Host == w.H0 && c.RouteNodes[r.Start + 1].Host == w.H1
        && Vector3.DistanceSquared(c.RouteNodes[r.Start].Pos, ConstructWorld.Quantise(w.N0)) < 1e-4f
        && Vector3.DistanceSquared(c.RouteNodes[r.Start + 1].Pos, ConstructWorld.Quantise(w.N1)) < 1e-4f;

    /// <summary>Run <paramref name="a"/>'s conveyor: its ends, its two nodes on the line and its width. False: no full rectangle, or it lies on a moving part.</summary>
    private static bool Wanted(Construct c, int a, out Want w)
    {
        w = default;
        ref readonly var asm = ref c.MachineAsms[a];
        var seed = asm.Core;
        if (asm.W == 0 || c.Parts[seed].Seg != 0)
            return false;
        var rot = c.Parts[seed].Rot;
        var inv = MachineBlocks.Rot24Inverse(rot);
        var across = Math.Min((int)asm.W, MaxAcross);
        var x0 = asm.X0 + (asm.W - across) / 2; // (a run wider than a conveyor takes: its middle tiles)
        var even = across % 2 == 0;
        var col = x0 + (across - 1) / 2; // (the tile the line leaves: the middle one, or left of the middle by its +x edge)
        int first = asm.Z0, last = asm.Z0 + asm.L - 1;
        int h0 = -1, h1 = -1;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.BlockAsmOf[i] != a || !c.Parts[i].D.IsRollerTile || c.Parts[i].Rot != rot || c.Parts[i].Seg != 0)
                continue;
            var rel = Rot24.Apply(inv, c.Parts[i].Pos - c.Parts[seed].Pos) / Cell;
            int tx = (int)MathF.Round(rel.X), tz = (int)MathF.Round(rel.Z);
            if (tx == col && tz == first)
                h0 = i;
            if (tx == col && tz == last)
                h1 = i;
        }
        if (h0 < 0 || h1 < 0)
            return false;
        var lx = col * Cell + (even ? 0.99f : 0f);
        float zs = first * Cell - 0.997f, ze = last * Cell + 0.997f;
        Vector3 Body(float z) => c.Parts[seed].Pos + Rot24.Apply(rot, new Vector3(lx, RunY, z));
        var start = Body(zs);
        var end = Body(ze);
        w.N0 = Body(zs + EndIn);
        w.N1 = Body(ze - EndIn);
        w.H0 = (ushort)h0;
        w.H1 = (ushort)h1;
        w.Width = (ushort)(across * Cell * 100);
        // Its in end: what is butted on the first row's -z faces, else its own first tile.
        if (Butted(c, a, first, x0, across, 5, start, out var from, out var fromPort, wantOut: true))
            (w.From, w.FromPort) = ((ushort)from, fromPort);
        else if (PortByte(c.Parts[h0].D, even ? StartPortHalf : StartPort) is var sp and >= 0)
            (w.From, w.FromPort) = ((ushort)h0, (byte)sp);
        else
            return false;
        if (Butted(c, a, last, x0, across, 4, end, out var to, out var toPort, wantOut: false))
            (w.To, w.ToPort) = ((ushort)to, toPort);
        else if (PortByte(c.Parts[h1].D, even ? EndPortHalf : EndPort) is var ep and >= 0)
            (w.To, w.ToPort) = ((ushort)h1, (byte)ep);
        else
            return false;
        return true;
    }

    /// <summary>
    /// The part butted on a run's end row (its tiles' faces <paramref name="dir"/>, 5: -z, 4: +z) that gives it pieces
    /// (<paramref name="wantOut"/>: a conveyor out port) or takes them, and that port: the one nearest the run's end. An Out Table
    /// gives its machine's out table (MachineBlocks.OutTable: the one its machine hands its piece to).
    /// </summary>
    private static bool Butted(Construct c, int a, int row, int x0, int across, byte dir, Vector3 end, out int part, out byte port, bool wantOut)
    {
        part = -1;
        port = 255;
        var best = 1.3f * 1.3f + across * across; // (within the run's half width and a little)
        var seed = c.MachineAsms[a].Core;
        var inv = MachineBlocks.Rot24Inverse(c.Parts[seed].Rot);
        // Its out end meeting a bed fed by conveyor (a Panel Former's, a Laser Cutter's): the run ends on that Core's Bed in, whose point is
        // the bed's middle, so the piece rides off the run straight onto the bed, never through the Core. By where the run's line goes on,
        // not by welds: a Laser Cutter's bed lies half a tile off its feed's grid.
        if (!wantOut && MachineBlocks.BedAt(c, end + Rot24.Apply(c.Parts[seed].Rot, ConstructParts.DirVector(dir)) * 0.25f) is var bc and >= 0
            && PortByte(c.DefOf(bc), ConstructParts.BedInPort) is var bp and >= 0)
        {
            (part, port) = (bc, (byte)bp);
            return true;
        }
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.BlockAsmOf[i] != a || !c.Parts[i].D.IsRollerTile)
                continue;
            var rel = Rot24.Apply(inv, c.Parts[i].Pos - c.Parts[seed].Pos) / Cell;
            int tx = (int)MathF.Round(rel.X), tz = (int)MathF.Round(rel.Z);
            if (tz != row || tx < x0 || tx >= x0 + across)
                continue;
            for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
            {
                ref readonly var lk = ref c.Links[e >> 1];
                var own = (e & 1) == 0 ? lk.SA : lk.SB;
                var socks = c.Parts[i].D.Sockets;
                if (own >= socks.Length || socks[own].Dir != dir)
                    continue;
                var o = c.OtherEnd(e);
                var od = c.DefOf(o);
                if (od.IsRollerTile)
                    continue; // (its own tiles; another run's side or end goes through a Turntable Tile, not straight in)
                if (wantOut && od.BlockRole == BlockRole.End && MachineBlocks.TryOf(c, o, out var oa) && MachineBlocks.OutTable(c, oa) is var table and >= 0)
                {
                    if (PortByte(c.DefOf(table), "Piece out") is var tp and >= 0)
                    {
                        (part, port) = (table, (byte)tp);
                        return true;
                    }
                }
                // A Run-out Table: the run is its Extruder's Piece out, so a cut piece rides from the die along the tables onto the run, whole
                // (ConstructConveyor.HandOff), with no crane or arm between them.
                if (wantOut && od.ExtRunOut > 0 && ConstructExtruder.TableMachine(c, o) is var ex and >= 0 && PortByte(c.DefOf(ex), "Piece out") is var xp and >= 0
                    && !PlayerOut(c, ex)) // (a conveyor a player linked from its Piece out keeps it: the run starts at its own first tile)
                {
                    (part, port) = (ex, (byte)xp);
                    return true;
                }
                foreach (var np in od.NamedPorts)
                {
                    if (np.Type != RouteType.Conveyor || (wantOut ? !np.Out : !np.In))
                        continue;
                    var d2 = Vector3.DistanceSquared(ConstructRoutes.PortPoint(c, o, np.Byte, RouteType.Conveyor), end);
                    if (d2 < best)
                        (best, part, port) = (d2, o, np.Byte);
                }
            }
        }
        return part >= 0;
    }

    /// <summary>Does a conveyor other than a Roller Tile run leave part <paramref name="part"/> (one a player linked)? ConstructConveyor.Out takes one link.</summary>
    private static bool PlayerOut(Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].Parent == 255 && c.Routes[k].From == part && !IsRun(c, k))
                return true;
        return false;
    }

    /// <summary>A conveyor port's byte by its name (the first is 255, the part itself), or -1.</summary>
    public static int PortByte(ConstructPartDef d, string name)
    {
        foreach (var np in d.NamedPorts)
            if (np.Type == RouteType.Conveyor && string.Equals(np.Name, name, StringComparison.Ordinal))
                return np.Byte;
        return -1;
    }

    internal static bool IsRunPort(ConstructPartDef d, byte port) =>
        ConstructRoutes.PortOf(d, RouteType.Conveyor, port) is { } np && np.Name.StartsWith("Run ", StringComparison.Ordinal);

    /// <summary>
    /// Server, at junction <paramref name="j"/> (a Roller Tile or a Turntable Tile): may the lead of conveyor <paramref name="from"/>,
    /// which ends there, go on along conveyor <paramref name="onto"/>, which starts there? A tile sends what its run brought along its
    /// Piece out link, and what a link brought onto its run; a turntable sends it any way but back.
    /// </summary>
    public static bool Passes(Construct c, int j, int from, int onto)
    {
        var d = c.DefOf(j);
        if (d.IsRollerTile)
            return IsRunPort(d, c.Routes[from].ToPort) && !IsRunPort(d, c.Routes[onto].FromPort);
        if (d.IsTurntable)
            return ConstructRoutes.PortOf(d, RouteType.Conveyor, c.Routes[from].ToPort) is not { } a
                || ConstructRoutes.PortOf(d, RouteType.Conveyor, c.Routes[onto].FromPort) is not { } b || a.Face != b.Face;
        return true;
    }

    /// <summary>Drawing: are Roller Tile <paramref name="part"/>'s rollers turning (a piece on its run), or a Turntable Tile's disc (a piece coming in or going out)?</summary>
    public static bool Moving(Construct c, int part)
    {
        if (c.ConveyedCount == 0)
            return false;
        var d = c.DefOf(part);
        var asm = d.IsRollerTile && part < c.BlockAsmOf.Length ? c.BlockAsmOf[part] : -1;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            var k = c.Conveyed[i].Route;
            if (k >= c.RouteCount)
                continue;
            ref readonly var r = ref c.Routes[k];
            if (d.IsTurntable ? r.To == part || r.From == part
                : asm >= 0 && r.Count == 2 && r.Type == RouteType.Conveyor && c.RouteNodes[r.Start].Host < c.BlockAsmOf.Length && c.BlockAsmOf[c.RouteNodes[r.Start].Host] == asm)
                return true;
        }
        return false;
    }
}
