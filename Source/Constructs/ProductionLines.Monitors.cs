using System.Numerics;

namespace CallOfSlop;

/// <summary>What a part's Data out shows on a display it is linked to (ProductionLines.Monitors.cs).</summary>
public enum MonitorKind : byte
{
    None,
    Console, // a Line Console: its line
    Machine, // a refiner, Extruder, Wheel Former, Assembler, Press or Die Lathe: that machine
    Rack, // a Piece Rack: its fill and kinds
    Store, // a Materials Bin, Storage Volume, Parts Shelf or other store: its fill and contents
    Network, // a Conveyor Router: its conveyor network's board (ConstructLogisticsBoard.cs)
}

/// <summary>
/// Production monitors (docs/sandbox/constructs/production.md "Production monitors"). A Line Console, a production machine or a Piece Rack
/// has a <b>Data out</b> port; a data link from it to any display part (a readout, sign, gauge, graph or lamp bank: a logic Display) makes that
/// display show the source's state instead of its own, laid out by the rows it has at its Text size: one line (tiny), a row a machine
/// (small), two lines a machine (medium) or a board (large). No new part and no second link: the link is an ordinary data wire, so saves,
/// blueprints and joiners keep it. A monitor reads the same stores and parts the console's own screen does, on either peer, and fills with
/// the panels (4 Hz, in view); nothing here allocates.
/// <para>
/// The console also has <b>Alarm</b> (any machine on its line Starved, Blocked, Fault or No power), <b>Running</b> (any running) and
/// <b>Made</b> (pieces or batches made since Start) outputs, worked out on the server at 10 Hz (<see cref="Step"/>), and the
/// <b>Stop after</b> setting, which stops the line when Made reaches it. Made reaches clients with the orders (CBodyKind.Orders, protocol 252).
/// </para>
/// </summary>
public static partial class ProductionLines
{
    public const int PortData = 1, PortAlarm = 2, PortRunning = 3, PortMade = 4; // a console's outputs after Run
    private static readonly string[] ConsoleOut = ["Run", "Data out", "Alarm", "Running", "Made"];
    private static readonly string[] ExtruderOut = ["Length", "Data out"];
    private static readonly string[] DataOutOnly = ["Data out"];
    private const byte AlarmBit = 1, RunningBit = 2, OnBit = 4;

    /// <summary>The loader: a console's, a machine's or a rack's Data out (after its panels and machine blocks).</summary>
    public static void MonitorPorts(ConstructPartDef d)
    {
        if (d.Role == PartRole.Logic && IsConsole(d))
        {
            d.Monitor = MonitorKind.Console;
            d.DataOut = ConsoleOut;
        }
        else if (d.IsExtruder)
        {
            d.Monitor = MonitorKind.Machine;
            d.DataOut = ExtruderOut;
        }
        else if (d.IsRefiner || d.IsAssembler || d.IsPress || d.IsLathe || d.IsRecycler || d.IsGatherer)
        {
            d.Monitor = MonitorKind.Machine;
            d.DataOut = DataOutOnly; // (one name: the part itself, port 255, as a single output always was)
        }
        else if (d.IsRouter)
        {
            d.Monitor = MonitorKind.Network;
            d.DataOut = ConstructLogistics.RouterOut; // (Alarm, Moving, then Data out: the board)
        }
        else if (d.IsPieceRack)
        {
            d.Monitor = MonitorKind.Rack;
            d.DataOut = DataOutOnly;
        }
        else if (d.Role == PartRole.Storage && !d.IsScoop && !d.IsTerminal && !d.IsBeacon && (d.IsBin || d.Slots.Length >= 4))
        {
            d.Monitor = MonitorKind.Store; // (bins, volumes, shelves: StoreMonitor.cs)
            d.DataOut = DataOutOnly;
        }
    }

    /// <summary>Is <paramref name="port"/> (a route's source port byte) this source's Data out?</summary>
    private static bool IsDataOut(ConstructPartDef d, byte port) => d.Monitor switch
    {
        MonitorKind.Console => ConstructRoutes.DataPort(port) == PortData,
        MonitorKind.Machine when d.IsExtruder => ConstructRoutes.DataPort(port) == 1,
        MonitorKind.Network => ConstructRoutes.DataPort(port) == ConstructLogistics.PortBoard,
        MonitorKind.None => false,
        _ => port == 255,
    };

    /// <summary>Can this part be a monitor: a logic Display with a panel, not itself a source?</summary>
    public static bool CanMonitor(ConstructPartDef d) => d.Logic == CLogic.Display && d.Role == PartRole.Logic && d.Panels.Length > 0 && d.Monitor == MonitorKind.None;

    /// <summary>The part whose Data out is linked into display <paramref name="part"/> by an intact link, or -1.</summary>
    public static int MonitorSource(Construct c, int part)
    {
        if (!CanMonitor(c.DefOf(part)))
            return -1;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.To != part || !r.FedEnd || r.Source >= c.PartCount)
                continue;
            if (IsDataOut(c.DefOf(r.Source), r.SourcePort))
                return r.Source;
        }
        return -1;
    }

    /// <summary>A console's Alarm, Running and Made outputs (port 2 up; Run is its Out, Data out its Out1: nothing).</summary>
    public static float PortValue(in CPart p, int port) => port switch
    {
        PortAlarm => (p.LineBits & AlarmBit) != 0 ? 1 : 0,
        PortRunning => (p.LineBits & RunningBit) != 0 ? 1 : 0,
        PortMade => p.LineMade,
        _ => 0,
    };

    // ---- The server's side: the outputs, Made and Stop after -------------------------------------------------------------

    /// <summary>Server, at 10 Hz: each console's Alarm and Running, Made reset by a new Start, and Stop after.</summary>
    public static void Step(ConstructWorld world)
    {
        if (++world.LineTicks < ConstructRefining.Every)
            return;
        world.LineTicks = 0;
        var bodies = world.Bodies;
        Span<int> list = stackalloc int[MaxListed];
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasLineConsole || c.Dirty)
                continue;
            foreach (var i in c.PanelParts)
            {
                if (i >= c.PartCount || c.Parts[i].D.Monitor != MonitorKind.Console)
                    continue;
                ref var p = ref c.Parts[i];
                var on = p.Out >= 0.5f && !p.Broken;
                if (on && (p.LineBits & OnBit) == 0 && p.LineMade != 0)
                {
                    p.LineMade = 0; // (a new Start counts from nothing)
                    world.OrdersChanged(c);
                }
                byte bits = on ? OnBit : (byte)0;
                var n = Machines(c, i, list);
                for (var k = 0; k < n; k++)
                {
                    if (world.StoreOf(c, list[k]) is not { } st || Raw(world, c, list[k], st) is not { } s)
                        continue;
                    if (s == LineState.Running)
                        bits |= RunningBit;
                    else if (s != LineState.Idle)
                        bits |= AlarmBit;
                }
                if (bits != p.LineBits)
                {
                    p.LineBits = bits;
                    c.Wake();
                }
                StopAfter(c, i);
            }
        }
    }

    /// <summary>Server: machine <paramref name="machine"/> made <paramref name="n"/> pieces or batches: each running console wired to its Run counts them.</summary>
    public static void Made(ConstructWorld world, Construct c, int machine, int n)
    {
        if (!c.HasLineConsole || n <= 0)
            return;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.To != machine || r.SourcePort != 255 || r.ToPort != 255 || !r.FedEnd || r.Source >= c.PartCount)
                continue;
            ref var p = ref c.Parts[r.Source];
            if (p.D.Monitor != MonitorKind.Console || p.Out < 0.5f)
                continue;
            p.LineMade = (int)Math.Min((long)p.LineMade + n, int.MaxValue);
            world.OrdersChanged(c);
            StopAfter(c, r.Source);
        }
    }

    private static void StopAfter(Construct c, int console)
    {
        ref var p = ref c.Parts[console];
        var limit = (int)ConstructConfig.Get(p, p.D, CfgKey.LineStopAfter, 0);
        if (limit <= 0 || p.LineMade < limit || (p.State & 1) == 0)
            return;
        p.State &= unchecked((byte)~1); // (the switch off: every machine on the line is held, as Stop does)
        c.Wake();
    }

    // ---- Made in a world save (WorldSaveBodies.MadeKey) ------------------------------------------------------------------------

    /// <summary>The most a save keeps (a setting is an f32: whole numbers are exact to 2^24).</summary>
    public const int MaxSavedMade = 1 << 24;

    /// <summary>A loaded console's count: a console that was running carries on counting, not reset as a new Start would.</summary>
    public static void LoadMade(ref CPart p, float v)
    {
        if (!float.IsFinite(v) || v < 1 || p.D.Monitor != MonitorKind.Console)
            return;
        p.LineMade = (int)Math.Min(v, MaxSavedMade);
        p.LineBits = p.Out >= 0.5f && !p.Broken ? OnBit : (byte)0;
    }

    // ---- Made on the wire (the end of CBodyKind.Orders) ----------------------------------------------------------------------

    public static bool AnyMade(Construct c)
    {
        if (!c.HasLineConsole)
            return false;
        foreach (var i in c.PanelParts)
            if (i < c.PartCount && c.Parts[i].LineMade != 0)
                return true;
        return false;
    }

    /// <summary>u8 n, each u16 part, u32 made: the consoles whose count isn't 0.</summary>
    public static void WriteMade(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U8(0);
        var n = 0;
        if (c.HasLineConsole)
            foreach (var i in c.PanelParts)
                if (i < c.PartCount && c.Parts[i].LineMade != 0 && n < 255)
                {
                    w.U16(i).U32((uint)c.Parts[i].LineMade);
                    n++;
                }
        w.PatchU8(at, (byte)n);
    }

    public static void ReadMade(ref PacketReader r, Construct? c)
    {
        int n = r.U8();
        if (c != null && c.HasLineConsole)
            foreach (var i in c.PanelParts)
                if (i < c.PartCount)
                    c.Parts[i].LineMade = 0;
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var made = r.U32();
            if (c == null)
                continue;
            if (part >= c.PartCount || c.DefOf(part).Monitor != MonitorKind.Console)
                throw new MalformedPacketException($"a Made count on part {part}");
            c.Parts[part].LineMade = (int)Math.Min(made, int.MaxValue);
        }
    }

    // ---- The monitor's card --------------------------------------------------------------------------------------------------

    private const float RowMetres = 0.06f; // a monitor's row at Text size 100 % (a 1 m Text Sign: 8 rows, a medium list)
    private const int MinCols = 18; // a list needs this many characters: a small panel takes smaller text
    private const int TinyRows = 2, SmallRows = 6, MediumRows = 14; // up to these rows: one line, a row a machine, two a machine; past them a board
    private const double ScrollStep = 2.0; // seconds a monitor's list rests before it moves on a row

    /// <summary>A monitor's rows: its panel's height in 6 cm rows at its Text size (one line for a strip), smaller where a list wouldn't fit across.</summary>
    public static int MonitorRows(PanelDef panel, float percent, out int cols)
    {
        var rows = Math.Clamp((int)(panel.Size.Y / (RowMetres * Math.Max(percent, 10) / 100f) + 0.25f), 1, PanelCard.MaxLines);
        if (rows <= TinyRows)
        {
            cols = PanelCard.MaxChars; // (one line as big as fits: PanelCard.Fit)
            return 1;
        }
        cols = PanelRender.Columns(panel.Size, rows);
        while (cols < MinCols && rows < PanelCard.MaxLines)
            cols = PanelRender.Columns(panel.Size, ++rows);
        cols = Math.Min(cols, PanelCard.MaxChars);
        return rows;
    }

    /// <summary>The first of <paramref name="n"/> entries a list of <paramref name="fit"/> rows shows now: it moves on a row every <see cref="ScrollStep"/> s and rests at each end.</summary>
    internal static int AutoFirst(int n, int fit, double now)
    {
        var positions = n - fit + 1;
        if (positions <= 1)
            return 0;
        var cycle = positions + 2;
        var step = (int)(now / ScrollStep % cycle);
        return Math.Clamp(step - 1, 0, positions - 1);
    }

    /// <summary>Fills display <paramref name="part"/>'s card from <paramref name="source"/>'s Data out (PanelRender, in place of the display's own source).</summary>
    public static void FillMonitor(ConstructWorld world, Construct c, int source, PanelCard card)
    {
        var sd = c.DefOf(source);
        card.Fit = card.Rows == 1;
        switch (sd.Monitor)
        {
            case MonitorKind.Console:
                FillLine(world, c, source, card);
                break;
            case MonitorKind.Rack:
                FillRack(world, c, source, sd, card);
                break;
            case MonitorKind.Store:
                StoreMonitor.Fill(world, c, source, sd, card);
                break;
            case MonitorKind.Network:
                ConstructLogisticsBoard.Fill(world, c, source, card);
                break;
            default:
                FillMachine(world, c, source, sd, card);
                break;
        }
    }

    private static void FillLine(ConstructWorld world, Construct c, int console, PanelCard card)
    {
        var now = ConstructRender.Now;
        ref var cp = ref c.Parts[console];
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, console, list);
        var on = cp.Out >= 0.5f;
        if (n == 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Line: no machines");
            return;
        }
        Span<LineState> states = stackalloc LineState[MaxListed];
        var worst = PanelState.Working;
        var running = 0;
        var stateW = 0;
        var toGo = 0;
        var longest = 0;
        for (var k = 0; k < n; k++)
        {
            var s = states[k] = world.StoreOf(c, list[k]) is { } st ? Of(world, c, list[k], st, now) : LineState.Idle;
            if (s == LineState.Running)
                running++;
            else if (s != LineState.Idle && Panel(s) > worst)
                worst = Panel(s);
            stateW = Math.Max(stateW, Word(s).Length);
            if (c.Parts[list[k]].Orders is { Bill: false } q)
                toGo += q.PiecesLeft;
            longest = Math.Max(longest, NameOf(c, list[..n], k).Length);
        }
        card.State = !on ? PanelState.Idle : worst;
        var cols = card.Cols;
        var rows = card.Rows;
        if (rows == 1)
        {
            // Tiny: one line in the worst state's colour.
            card.Add(0, "Line ").Add(0, running).Add(0, '/').Add(0, n).Add(0, on ? " running" : " stopped");
            if (toGo > 0)
                card.Add(0, " - ").Add(0, toGo).Add(0, " to go");
            else if (cp.LineMade > 0)
                card.Add(0, " - ").Add(0, cp.LineMade).Add(0, " made");
            return;
        }
        var nameW = Math.Min(longest, Math.Max(4, cols - 1 - stateW));
        if (rows <= SmallRows)
        {
            // Small: a row a machine, its state in full in its lamp's colour; a title when every machine fits under it.
            var titled = n + 1 <= rows;
            card.Plain = !titled;
            var line = 0;
            if (titled)
                Title(card, cp, on, running, n, cols, brief: true);
            line = titled ? 1 : 0;
            var fit = rows - line;
            var first = AutoFirst(n, fit, now);
            for (var k = first; k < n && line < rows; k++, line++)
                MachineRow(c, list[..n], k, states[k], card, line, nameW, cols);
            return;
        }
        if (rows <= MediumRows)
        {
            // Medium: the title, then two lines a machine: its name and state, and its detail (why it waits, its recipe or orders).
            Title(card, cp, on, running, n, cols, brief: false);
            var fit = Math.Max(1, (rows - 1) / 2);
            var first = AutoFirst(n, fit, now);
            var line = 1;
            for (var k = first; k < n && k < first + fit && line + 1 < rows; k++)
            {
                MachineRow(c, list[..n], k, states[k], card, line++, nameW, cols);
                card.Add(line, "  ");
                Detail(world, c, list[k], c.DefOf(list[k]), states[k], card, line);
                card.Clip(line, cols).Dim(line);
                line++;
            }
            return;
        }
        Board(world, c, console, list[..n], states[..n], card, on, running, toGo, nameW, now);
    }

    /// <summary>"Line running 2/3" and what it made: a monitor's title.</summary>
    private static void Title(PanelCard card, in CPart cp, bool on, int running, int n, int cols, bool brief)
    {
        card.Add(0, on ? "Line running " : "Line stopped ").Add(0, running).Add(0, '/').Add(0, n);
        if (!brief && cp.LineMade > 0)
            card.Add(0, ", ").Add(0, cp.LineMade).Add(0, " made");
        card.Clip(0, cols);
    }

    /// <summary>A machine's row: its name in a column (ending ".." when cut) and its state in full in its lamp's colour.</summary>
    private static void MachineRow(Construct c, ReadOnlySpan<int> list, int k, LineState s, PanelCard card, int line, int nameW, int cols)
    {
        card.Field(line, NameOf(c, list, k), nameW, false, 0).Add(line, ' ');
        var from = card.Length(line);
        card.Add(line, Word(s)).Tint(line, from, card.Length(line), Lamp(s));
        card.Clip(line, cols);
    }

    private static Vector3 Lamp(LineState s)
    {
        Look(s, out var colour, out _, out _);
        return s == LineState.NoPower ? NoPowerGrey : colour;
    }

    /// <summary>
    /// Large: a header (the line, running n/m, made, to go, Stop after), a table of machines (state, head order or recipe, queue, why it waits)
    /// and, when there is room, the racks the line's conveyors end at with their fill.
    /// </summary>
    private static void Board(ConstructWorld world, Construct c, int console, ReadOnlySpan<int> list, ReadOnlySpan<LineState> states, PanelCard card,
        bool on, int running, int toGo, int nameW, double now)
    {
        ref var cp = ref c.Parts[console];
        var cols = card.Cols;
        var rows = card.Rows;
        var n = list.Length;
        card.Add(0, on ? "Production line - running " : "Production line - stopped ").Add(0, running).Add(0, '/').Add(0, n).Clip(0, cols);
        card.Add(1, "Made ").Add(1, cp.LineMade).Add(1, "   To go ").Add(1, toGo);
        var limit = (int)ConstructConfig.Get(cp, cp.D, CfgKey.LineStopAfter, 0);
        if (limit > 0)
            card.Add(1, "   Stops at ").Add(1, limit);
        card.Clip(1, cols);
        const int stateW = 8, queueW = 9;
        var detailAt = nameW + 1 + stateW + 1 + queueW + 1;
        card.Add(2, "Machine").PadTo(2, nameW + 1).Add(2, "State").PadTo(2, nameW + 1 + stateW + 1).Add(2, "Queue").PadTo(2, detailAt).Add(2, "Doing").Clip(2, cols).Dim(2);
        Span<int> racks = stackalloc int[16];
        var rackN = LineRacks(c, list, racks);
        var room = rows - 3;
        var rackRows = rackN > 0 && room - n >= 3 ? Math.Min(rackN + 2, room - n) : 0; // (a gap and a heading, then a rack a row)
        var fit = room - rackRows;
        var first = AutoFirst(n, fit, now);
        var line = 3;
        for (var k = first; k < n && line < 3 + fit; k++, line++)
        {
            var m = list[k];
            card.Field(line, NameOf(c, list, k), nameW, false, 0).Add(line, ' ');
            var from = card.Length(line);
            card.Add(line, Word(states[k])).Tint(line, from, card.Length(line), Lamp(states[k]));
            card.PadTo(line, nameW + 1 + stateW + 1);
            from = card.Length(line);
            if (c.Parts[m].Orders is { Bill: false } q && q.Total > 0)
            {
                ref var h = ref q.Head;
                card.Add(line, h.Made).Add(line, '/').Add(line, h.Count);
                if (q.Total > 1)
                    card.Add(line, " +").Add(line, q.Total - 1);
            }
            else
                card.Add(line, '-');
            if (card.Length(line) > from + queueW)
                card.Clip(line, from + queueW);
            card.PadTo(line, detailAt);
            from = card.Length(line);
            Detail(world, c, m, c.DefOf(m), states[k], card, line);
            card.Clip(line, cols);
        }
        if (rackRows == 0)
            return;
        line = rows - rackRows + 1;
        card.Add(line++, "Racks").Dim(line - 1);
        for (var k = 0; k < rackN && line < rows; k++, line++)
        {
            var rk = racks[k];
            var rd = c.DefOf(rk);
            card.Field(line, rd.Name, nameW, false, 0).Add(line, ' ');
            if (world.StoreOf(c, rk) is { } st)
                RackLine(rd, st, card, line);
            card.Clip(line, cols);
        }
    }

    /// <summary>"62% full, 131 pieces, 4 kinds" without a string (ConstructConveyor.FillText's words).</summary>
    private static void RackLine(ConstructPartDef rd, CStore st, PanelCard card, int line)
    {
        ConstructRackPack.Ensure(rd, st);
        card.Add(line, (int)MathF.Round(ConstructRackPack.Fill(rd, st) * 100)).Add(line, "% full, ").Add(line, st.ItemCount).Add(line, st.ItemCount == 1 ? " piece, " : " pieces, ")
            .Add(line, st.KindN).Add(line, st.KindN == 1 ? " kind" : " kinds");
    }

    /// <summary>The Piece Racks the line's machines' conveyors end at, through junctions and presses (a few hops), each once.</summary>
    private static int LineRacks(Construct c, ReadOnlySpan<int> machines, Span<int> racks)
    {
        Span<int> reach = stackalloc int[48];
        var rn = 0;
        foreach (var m in machines)
            if (rn < reach.Length)
                reach[rn++] = m;
        var n = 0;
        for (var at = 0; at < rn; at++)
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref readonly var r = ref c.Routes[k];
                if (r.Type != RouteType.Conveyor || r.From != reach[at] || r.Parent != 255 || r.To >= c.PartCount)
                    continue;
                var td = c.DefOf(r.To);
                if (td.IsPieceRack)
                {
                    if (n < racks.Length && !racks[..n].Contains(r.To))
                        racks[n++] = r.To;
                }
                else if ((td.IsConveyorJunction || td.IsPress || td.IsLathe) && rn < reach.Length && !reach[..rn].Contains(r.To))
                    reach[rn++] = r.To;
            }
        return n;
    }

    /// <summary>A rack's monitor: its fill, then its kinds with their counts (scrolling by itself), and a bar.</summary>
    private static void FillRack(ConstructWorld world, Construct c, int rack, ConstructPartDef rd, PanelCard card)
    {
        if (world.StoreOf(c, rack) is not { } st)
        {
            card.State = PanelState.Off;
            card.Add(0, rd.Name);
            return;
        }
        ConstructRackPack.Ensure(rd, st);
        var fill = ConstructRackPack.Fill(rd, st);
        card.State = fill >= 0.98f ? PanelState.Wants : st.ItemCount > 0 ? PanelState.Working : PanelState.Idle;
        if (card.Rows == 1)
        {
            card.Add(0, rd.Name).Add(0, ' ').Add(0, (int)MathF.Round(fill * 100)).Add(0, "% full");
            return;
        }
        var cols = card.Cols;
        card.Add(0, rd.Name).Clip(0, cols);
        RackLine(rd, st, card, 1);
        card.Clip(1, cols);
        card.Bar = fill;
        var fit = card.Rows - 2;
        var first = AutoFirst(st.KindN, fit, ConstructRender.Now);
        for (var k = first; k < st.KindN && k - first < fit; k++)
            card.AddRow(2 + k - first, ' ', ConstructParts.Def(st.KindDef![k]).Name, st.KindCount![k], cols);
        if (st.KindN == 0)
            card.Add(2, "empty");
    }

    /// <summary>A machine's monitor: its own screen laid out for the display (one line on a tiny one: its name and what its screen's top line says).</summary>
    private static void FillMachine(ConstructWorld world, Construct c, int machine, ConstructPartDef md, PanelCard card)
    {
        var own = md.Panels.Length > 0 ? md.Panels[0] : null;
        var tiny = card.Rows == 1;
        if (own?.Fill != null || md.IsGatherer)
        {
            if (tiny)
                card.Rows = card.TextRows = 4; // (filled as a screen, then its top line kept)
            if (own?.Fill != null)
                own.Fill(world, c, machine, own, card);
            else
                MachineScreens.Gatherer(world, c, machine, md, card); // (a module has no screen of its own: a monitor is its screen)
        }
        for (var t = 0; t < card.TouchLabels.Length; t++)
            card.TouchLabels[t] = null;
        if (!tiny)
            return;
        // Tiny: "Extruder: Extruding", the state's colour.
        card.Rows = card.TextRows = 1;
        Span<char> top = stackalloc char[PanelCard.MaxChars];
        var line = card.Line(0);
        var len = Math.Min(line.Length, top.Length);
        line[..len].CopyTo(top);
        for (var l = 0; l < PanelCard.MaxLines; l++)
            card.ClearLine(l);
        card.Bar = -1;
        card.HasIcon = false;
        card.Draw = PanelDraw.None;
        card.Add(0, md.Name);
        if (len > 0)
            card.Add(0, ": ");
        for (var k = 0; k < len; k++)
            card.Add(0, top[k]);
        if (c.Parts[machine].Orders is { Bill: false, PiecesLeft: > 0 } q)
            card.Add(0, " - ").Add(0, q.PiecesLeft).Add(0, " to go");
    }
}

/// <summary>The line step's clock (ProductionLines.Monitors.cs).</summary>
public sealed partial class ConstructWorld
{
    public int LineTicks;
}
