using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A production machine's state as a line reads it (docs/sandbox/constructs/production.md "Production lines"): the six a player
/// tells apart at a glance, worked out from the store and the part on either peer, so nothing new goes on the wire.
/// </summary>
public enum LineState : byte
{
    Idle, // stopped: its operator's console (or any wire into Run) holds it off
    Running,
    Starved, // nothing in its input, or not enough for a batch
    Blocked, // its output is full or holds another material
    Fault, // its input holds what it can't work (no recipe, or a higher tier's)
    NoPower,
}

/// <summary>
/// Production lines (docs/sandbox/constructs/production.md "Production lines"): machines that a generator can place in a row and a player
/// can read and run from one place. Three shared pieces, refiners first:
/// <list type="bullet">
/// <item>the <b>line state</b> of a machine (<see cref="Of"/>), from its store, with a short hold so a state that lasts a packet doesn't flash;</item>
/// <item>the <b>status lamp</b>: a part box marked <c>"status"</c>, drawn unlit in the state's colour and blink, with a band that runs along it while the
/// machine works (<see cref="DrawLamp"/>; the part kit's PartScreens puts it over the machine's screen);</item>
/// <item>the <b>Line Console</b>: a Switch whose output is wired to each machine's Run. The wires are the line: its screen (panel source <c>line</c>)
/// lists the machines they reach, and its buttons start and stop the line (the switch), pick a machine and step that machine's recipe.</item>
/// </list>
/// Nothing here allocates; a console's list is walked from the construct's routes when its panel fills (4 Hz, in view) or a button is pressed.
/// </summary>
public static partial class ProductionLines
{
    internal const int MaxListed = 32; // machines a console's buttons step through
    private const double Hold = 0.3; // seconds a new state must last before the lamp and console show it (a store's bytes and its Busy can come a packet apart)

    /// <summary>A machine's raw state, or null for a part that isn't a production machine of a line yet. Its power is the construct's (on the wire with its pose).</summary>
    public static LineState? Raw(ConstructWorld world, Construct c, int part, CStore st)
    {
        var d = c.DefOf(part);
        if (d.IsExtruder)
            return ExtruderRaw(world, c, part, st);
        if (d.IsAssembler)
            return AssemblerRaw(world, c, part, d, st);
        if (d.IsPress || d.IsLathe)
            return PressRaw(world, c, part, st);
        if (d.IsRecycler)
            return ConstructRecycler.LineOf(world, c, part, st);
        if (d.IsGatherer)
            return ConstructGatherer.LineOf(world, c, part, st);
        if (!d.IsRefiner)
            return null;
        if (!ConstructRecycler.Powered(world, c, part))
            return LineState.NoPower; // (before Held: a console on a flat battery puts nothing out, which holds its machines off too)
        if (st.Held)
            return LineState.Idle;
        if (st.Busy)
            return LineState.Running;
        return ConstructRefining.StateOf(RefiningTable.Current, d, st, out _) switch
        {
            RefineState.Empty or RefineState.Short => LineState.Starved,
            RefineState.OutputFull or RefineState.OutputMixed => LineState.Blocked,
            RefineState.NoRecipe or RefineState.TierLow => LineState.Fault,
            _ => LineState.Running, // (ready and powered: it starts at its next step)
        };
    }

    /// <summary>An Extruder's or a Former's state, from its stall byte (ConstructExtruder.cs): a queue waiting for orders is Stopped.</summary>
    private static LineState ExtruderRaw(ConstructWorld world, Construct c, int part, CStore st)
    {
        if (!ConstructRecycler.Powered(world, c, part))
            return LineState.NoPower;
        if (st.Held)
            return LineState.Idle;
        if (st.Busy)
            return LineState.Running;
        return (ExtrudeStall)st.Refused switch
        {
            ExtrudeStall.NoMaterial or ExtrudeStall.OrderMaterial or ExtrudeStall.Mixed => LineState.Starved,
            ExtrudeStall.BadMaterial or ExtrudeStall.BadShape or ExtrudeStall.ConveyorSmall or ExtrudeStall.OrderTooLong => LineState.Fault,
            ExtrudeStall.NoPower => LineState.NoPower,
            ExtrudeStall.None or ExtrudeStall.NoOrders => LineState.Idle,
            _ => LineState.Blocked, // (at its cap, blocked, too heavy to push, a chute or a conveyor waiting)
        };
    }

    /// <summary>Whether a machine is at work now, for the boxes that show it ("work" on a box: <see cref="BoxWork"/>): a line machine that shows Running, any other with a busy store and power.</summary>
    public static bool Working(ConstructWorld world, Construct c, int part, CStore st, double now) =>
        Raw(world, c, part, st) is null ? st.Busy && !st.Held && ConstructRecycler.Powered(world, c, part) : Of(world, c, part, st, now) == LineState.Running && st.Busy;

    /// <summary>A Press's or a Die Lathe's state, from its stall byte (ConstructPress.cs, on the wire): Running while it stamps, Starved waiting for a
    /// piece or a die, Blocked with its tray, its path or its conveyor full, at Fault when what it makes can't leave or it is the wrong size.</summary>
    private static LineState PressRaw(ConstructWorld world, Construct c, int part, CStore st)
    {
        if (!ConstructRecycler.Powered(world, c, part) || (PressStall)st.Refused == PressStall.NoPower)
            return LineState.NoPower;
        if (st.Held)
            return LineState.Idle;
        if (st.Busy)
            return LineState.Running;
        return (PressStall)st.Refused switch
        {
            PressStall.Wants or PressStall.TooShort or PressStall.NoDie or PressStall.NoRecipe or PressStall.Bay2Short => LineState.Starved,
            PressStall.TrayFull or PressStall.Blocked or PressStall.ConveyorFull or PressStall.ConveyorCut => LineState.Blocked,
            PressStall.WrongClass or PressStall.ConveyorSmall => LineState.Fault,
            _ => LineState.Idle, // (ready: nothing to say until a piece comes)
        };
    }

    /// <summary>An Assembler's state (ConstructAssembling.cs): no recipe or an empty queue is Stopped; a recipe it can't work is a Fault.</summary>
    private static LineState AssemblerRaw(ConstructWorld world, Construct c, int part, ConstructPartDef d, CStore st)
    {
        if (!ConstructRecycler.Powered(world, c, part))
            return LineState.NoPower;
        if (st.Held)
            return LineState.Idle;
        if (st.Busy && st.Blocked == 0)
            return LineState.Running;
        ref var p = ref c.Parts[part];
        return ConstructAssembling.StateOf(p, d, st, ConstructAssembling.RecipeOf(AssemblingTable.Current, p, d)) switch
        {
            AsmState.Short => LineState.Starved,
            AsmState.OutputFull or AsmState.Blocked => st.Blocked == (byte)AsmBlock.ConveyorSmall ? LineState.Fault : LineState.Blocked,
            AsmState.TierLow or AsmState.Untaught => LineState.Fault,
            AsmState.NoRecipe or AsmState.NoOrders => LineState.Idle,
            _ => LineState.Running, // (ready and powered: it starts at its next step)
        };
    }

    /// <summary>The state shown: a new one once it has lasted <see cref="Hold"/>. Keeps its memory on the store (this peer's own, never sent).</summary>
    public static LineState Of(ConstructWorld world, Construct c, int part, CStore st, double now)
    {
        if (Raw(world, c, part, st) is not { } raw)
            return LineState.Idle;
        if (raw != st.LineRaw)
        {
            st.LineRaw = raw;
            st.LineSince = now;
        }
        if (raw != st.LineShown && (!st.LineSeen || now - st.LineSince >= Hold || now < st.LineSince || raw == LineState.Running && st.LineShown == LineState.Idle))
            st.LineShown = raw; // (the first look, and pressing Start, show at once)
        st.LineSeen = true;
        return st.LineShown;
    }

    public static string Word(LineState s) => s switch
    {
        LineState.Idle => "Stopped",
        LineState.Running => "Running",
        LineState.Starved => "Starved",
        LineState.Blocked => "Blocked",
        LineState.Fault => "Fault",
        _ => "No power",
    };

    public static PanelState Panel(LineState s) => s switch
    {
        LineState.Idle => PanelState.Idle,
        LineState.Running => PanelState.Working,
        LineState.Starved => PanelState.Wants,
        LineState.Blocked => PanelState.Wants,
        LineState.Fault => PanelState.Refused,
        _ => PanelState.Fault,
    };

    private static readonly Vector3 Green = new(0.25f, 1f, 0.35f), White = new(0.55f, 0.62f, 0.72f), Amber = new(1f, 0.68f, 0.08f),
        Orange = new(1f, 0.38f, 0.04f), Red = new(1f, 0.1f, 0.06f), Dark = new(0.09f, 0.08f, 0.08f);

    /// <summary>The lamp's colour and how often it blinks (0: steady), and whether a band runs along it.</summary>
    public static void Look(LineState s, out Vector3 colour, out float blink, out bool chase)
    {
        chase = false;
        blink = 0;
        switch (s)
        {
            case LineState.Running: colour = Green; chase = true; break;
            case LineState.Idle: colour = White; break;
            case LineState.Starved: colour = Amber; blink = 1f; break;
            case LineState.Blocked: colour = Orange; blink = 3f; break;
            case LineState.Fault: colour = Red; blink = 3f; break;
            default: colour = Dark; break; // (no power: the lamp is out)
        }
    }

    private const int ChaseCells = 6; // a running lamp's cells: a brighter band runs along them
    private const float ChaseSpeed = 1.5f; // cells... per second over the lamp's length: the band crosses it in 1/ChaseSpeed s

    /// <summary>Draws a machine's status lamp box (its part's <see cref="ConstructPartDef.StatusBox"/>); returns the draw calls.</summary>
    public static int DrawLamp(Renderer gfx, ConstructWorld world, Construct c, int part, CStore st, in CPartBox box, Vector3 centre, in Matrix4x4 frame, float opacity, double now)
    {
        Look(Of(world, c, part, st, now), out var colour, out var blink, out var chase);
        var rot = c.Parts[part].Rot;
        var size = Rot24.ApplyHalf(rot, box.Half) * 2;
        if (!chase)
        {
            var on = colour != Dark && (blink <= 0 || now * blink % 1.0 < 0.5);
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(centre) * frame, on ? colour : colour * 0.12f, opacity, mode: on ? 2 : 0);
            return 1;
        }
        // Running: the lamp in cells along its longest side, a bright band running along them (the line's motion, readable from 20 m).
        var axis = size.X >= size.Y && size.X >= size.Z ? Vector3.UnitX : size.Y >= size.Z ? Vector3.UnitY : Vector3.UnitZ;
        var length = Vector3.Dot(size, axis);
        var cell = size - axis * (length - length / ChaseCells);
        var head = (float)(now * ChaseSpeed % 1.0) * ChaseCells;
        for (var k = 0; k < ChaseCells; k++)
        {
            var behind = (head - k + ChaseCells) % ChaseCells; // (cells since the band passed)
            var bright = behind < 1 ? 1f : behind < 2 ? 0.55f : 0.3f;
            var at = centre + axis * ((k + 0.5f) / ChaseCells * length - length / 2);
            gfx.Draw(Matrix4x4.CreateScale(cell) * Matrix4x4.CreateTranslation(at) * frame, colour * bright, opacity, mode: 2);
        }
        return ChaseCells;
    }

    // ---- The Line Console ----------------------------------------------------------------------------------------------------

    /// <summary>Is this part a Line Console (a panel with the <c>line</c> source)?</summary>
    public static bool IsConsole(ConstructPartDef d)
    {
        foreach (var p in d.Panels)
            if (p.Source == "line")
                return true;
        return false;
    }

    /// <summary>The machines a console's Run output (port 0: its other outputs are Data out, Alarm, Running, Made) reaches by intact wires, in the order the wires were laid, each once. Allocates nothing.</summary>
    public static int Machines(Construct c, int console, Span<int> into)
    {
        var n = 0;
        for (var k = 0; k < c.RouteCount && n < into.Length; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.Source != console || r.SourcePort != 255 || !r.FedEnd || r.To >= c.PartCount || NotMachine(c, r.To))
                continue;
            if (!into[..n].Contains(r.To))
                into[n++] = r.To;
        }
        return n;
    }

    private static bool NotMachine(Construct c, int part) => c.DefOf(part) is { IsRefiner: false, IsExtruder: false, IsAssembler: false, IsRecycler: false, IsGatherer: false, IsPress: false, IsLathe: false }; // (refiners, Extruders, Formers, Assemblers, Presses, Die Lathes, Recyclers and gathering modules are the lines' machines)

    private static bool OnBill(in CPart p, ConstructPartDef d) => ConstructConfig.Get(p, d, CfgKey.LinePage, 0) >= 0.5f;

    private static int Pick(in CPart p, ConstructPartDef d, int count) => count == 0 ? 0 : Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.LinePick, 0), 0, count - 1);

    /// <summary>
    /// The console's screen (the line page): the line's state and the scroll mark, then a row a machine: a marker on the picked one, its name
    /// ("Extruder 2" when the line has two), its state in full in the lamp's colour, and a dimmer line of detail under it (why it waits, its
    /// recipe or its orders) for every machine when the rows allow, else for the picked one. Laid out on the part's Text size
    /// (<see cref="PanelCard.Cols"/>, <see cref="PanelCard.TextRows"/>); a name too long for its column slides along on the picked row and ends
    /// in ".." on the others; a list longer than the screen scrolls by the viewer's wheel or Up and Down (<see cref="PanelCard.Scroll"/>),
    /// and a new pick scrolls to itself.
    /// </summary>
    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        LogicDisplays.Ink(p, d, card);
        if (OnBill(p, d))
        {
            WorkOrders.FillBill(c, part, card); // (the bill page: WorkOrders.cs)
            return;
        }
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, part, list);
        var on = p.Out >= 0.5f;
        card.TouchLabels[0] = on ? "Stop" : "Start";
        card.TouchLabels[1] = "Next";
        card.TouchLabels[2] = "Recipe";
        card.TouchLabels[3] = "Bill";
        var cols = card.Cols;
        var rows = card.TextRows;
        if (n == 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Line: no machines");
            int at = 0, column = 0;
            card.Wrap("Wire its output to the Run of each machine.", cols, 1, rows - 1, 0, ref at, ref column);
            return;
        }
        Span<LineState> states = stackalloc LineState[MaxListed];
        var pick = Pick(p, d, n);
        var worst = PanelState.Working;
        var running = 0;
        var stateW = 0;
        for (var k = 0; k < n; k++)
        {
            states[k] = world.StoreOf(c, list[k]) is { } st ? Of(world, c, list[k], st, ConstructRender.Now) : LineState.Idle;
            var s = states[k];
            if (s == LineState.Running)
                running++;
            else if (s != LineState.Idle && Panel(s) > worst)
                worst = Panel(s);
            stateW = Math.Max(stateW, Word(s).Length);
        }
        card.State = !on ? PanelState.Idle : worst;
        // The name's column: what the state's leaves; on a narrow screen (a big Text size) the state goes on the detail line.
        var nameW = cols - 2 - stateW;
        var longest = 0;
        for (var k = 0; k < n; k++)
            longest = Math.Max(longest, NameOf(c, list[..n], k).Length);
        var stateBelow = nameW < longest && nameW < cols - 1; // (a name that wouldn't fit beside its state: the state goes under it, every name whole)
        if (stateBelow)
            nameW = cols - 1;
        var room = Math.Max(1, rows - 1);
        var every = stateBelow || 2 * n <= room; // (a detail line under every machine)
        var fit = every ? Math.Max(1, room / 2) : n + 1 <= room ? n : Math.Max(1, room - 1); // (one row kept for the pick's detail)
        card.ScrollMax = Math.Max(0, n - fit);
        var first = card.Scroll;
        if (card.Follow != pick)
        {
            // A new pick (Next, on any peer) comes into view.
            if (pick < first)
                first = pick;
            else if (pick >= first + fit)
                first = pick - fit + 1;
            card.Follow = pick;
        }
        first = card.Scroll = Math.Clamp(first, 0, card.ScrollMax);
        var shown = Math.Min(fit, n - first);
        var brief = shown < n && cols < 24; // (the scroll mark needs the room: "Running 2/3")
        var tight = shown < n && cols < 19; // (a big Text size: "On 2/3  1-2/3", the count kept before the scroll mark)
        card.Add(0, tight ? on ? "On " : "Off " : brief ? on ? "Running " : "Stopped " : on ? "Line running " : "Line stopped ").Add(0, running).Add(0, '/').Add(0, n);
        card.ScrollMark(0, first, shown, n, cols).Clip(0, cols);
        var line = 1;
        for (var k = first; k < first + shown && line < rows; k++)
        {
            var m = list[k];
            var md = c.DefOf(m);
            var s = states[k];
            Look(s, out var colour, out _, out _);
            if (s == LineState.NoPower)
                colour = NoPowerGrey;
            card.Add(line, k == pick ? '>' : ' ');
            if (k == pick)
                card.Mark = line; // (the pick on a lit strip: which machine Recipe and Next act on)
            card.Field(line, NameOf(c, list[..n], k), nameW, k == pick, ConstructRender.Now);
            if (!stateBelow)
            {
                card.Add(line, ' ');
                var from = card.Length(line);
                card.Add(line, Word(s)).Tint(line, from, card.Length(line), colour);
            }
            line++;
            if ((every || k == pick) && line < rows)
            {
                card.Add(line, "  ");
                if (stateBelow)
                {
                    card.Add(line, Word(s)).Tint(line, 2, card.Length(line), colour);
                    card.Add(line, ' ');
                }
                Detail(world, c, m, md, s, card, line);
                card.Clip(line, cols).Dim(line);
                line++;
            }
        }
    }

    internal static readonly Vector3 NoPowerGrey = new(0.5f, 0.5f, 0.52f);

    /// <summary>The detail under a machine's row: why it waits (when Starved, Blocked or at Fault), then its recipe or its orders.</summary>
    internal static void Detail(ConstructWorld world, Construct c, int m, ConstructPartDef md, LineState s, PanelCard card, int line)
    {
        ref var mp = ref c.Parts[m];
        var why = s is LineState.Starved or LineState.Blocked or LineState.Fault;
        if (md.IsPress || md.IsLathe)
        {
            // Why it waits in its screen's words ("Bays need pieces"), else what it does.
            if (world.StoreOf(c, m) is { } pst)
                card.Add(line, s == LineState.Running ? md.IsLathe ? "cutting" : "stamping" : ConstructPress.StallText((PressStall)pst.Refused));
            if (WorkOrders.HeadOf(mp, out var head))
                card.Add(line, ", order ").Add(line, head.Made).Add(line, '/').Add(line, head.Count); // (its head order: Press phase 4)
            return;
        }
        if (md.IsRecycler || md.IsGatherer)
        {
            // What it holds, after why it waits: "bin full, 20 ore", "no wrecks in, 12 scrap".
            if (world.StoreOf(c, m) is not { } gst)
                return;
            if (why)
                card.Add(line, md.IsGatherer ? ConstructGatherer.StallWord(md, (GatherStall)gst.Blocked) : s == LineState.Blocked ? "output full" : "no wrecks in").Add(line, ", ");
            else if (s == LineState.Running)
                card.Add(line, md.IsRecycler ? "recycling, " : md.GatherSaw ? "cutting, " : "drilling, ");
            card.Add(line, gst.Amount).Add(line, '/').Add(line, md.BulkCapacity);
            if (gst.Amount > 0)
                card.Add(line, ' ').Add(line, RecyclingTable.Current.NameOf(gst.Material));
            return;
        }
        if (md.IsExtruder)
        {
            if (why && world.StoreOf(c, m) is { } st)
                card.Add(line, StallWord((ExtrudeStall)st.Refused)).Add(line, ", ");
            if (mp.Orders is { } q)
            {
                if (q.Total == 0)
                    card.Add(line, "no orders");
                else
                    card.Add(line, q.PiecesLeft).Add(line, " to go, ").Add(line, q.Total).Add(line, q.Total == 1 ? " order" : " orders");
            }
            else
                card.Add(line, "own settings");
            return;
        }
        if (md.IsAssembler)
        {
            var r = ConstructAssembling.RecipeOf(AssemblingTable.Current, mp, md);
            if (why && world.StoreOf(c, m) is { } ast)
            {
                if (ast.Blocked != 0)
                    card.Add(line, ConstructAssembling.BlockedWord(ast)).Add(line, ", ");
                else if (r != null && ConstructAssembling.MissingOf(ast, r, out var mat, out var lack))
                {
                    card.Add(line, "needs ");
                    if (lack != null)
                        card.Add(line, r.CountOf(lack.Id)).Add(line, ' ').Add(line, lack.Name);
                    else
                        card.Add(line, r.UnitsOf(mat)).Add(line, ' ').Add(line, RecyclingTable.Current.NameOf(mat));
                    card.Add(line, ", ");
                }
                else if (r != null && r.MinTier > md.Tier)
                    card.Add(line, "needs tier ").Add(line, r.MinTier).Add(line, ", ");
            }
            if (mp.Orders is { } aq)
            {
                if (aq.Total == 0)
                    card.Add(line, "no orders");
                else
                    card.Add(line, aq.PiecesLeft).Add(line, " to go, ").Add(line, aq.Total).Add(line, aq.Total == 1 ? " order" : " orders");
            }
            else
                card.Add(line, r != null ? "makes " : "no recipe").Add(line, r?.Name ?? "");
            return;
        }
        var set = ConstructRefining.SetOf(mp, md);
        if (why && world.StoreOf(c, m) is { } rs)
        {
            var state = ConstructRefining.StateOf(RefiningTable.Current, md, rs, out var r);
            card.Add(line, state switch
            {
                RefineState.Empty => "needs input",
                RefineState.Short => "needs more",
                RefineState.OutputFull => "output full",
                RefineState.OutputMixed => "empty its output",
                RefineState.NoRecipe => "wrong input",
                RefineState.TierLow => "needs a higher tier",
                _ => "",
            });
            if (state == RefineState.Short && r != null)
                card.Add(line, " (").Add(line, r.UnitsIn).Add(line, ' ').Add(line, r.InputName).Add(line, ')');
            card.Add(line, ", ");
            if (set != null)
                card.Add(line, "makes ").Add(line, set.OutputName);
            else
                AutoNext(md, rs, r, card, line);
            return;
        }
        if (set != null)
            card.Add(line, "makes ").Add(line, set.OutputName);
        else if (world.StoreOf(c, m) is { } ast2)
        {
            ConstructRefining.StateOf(RefiningTable.Current, md, ast2, out var next);
            AutoNext(md, ast2, next, card, line);
        }
        else
            card.Add(line, "auto recipe");
    }

    /// <summary>Auto's next batch: "auto: next metal" from what is in its input, or "auto: by its input" while it is empty.</summary>
    private static void AutoNext(ConstructPartDef md, CStore st, RefineRecipe? next, PanelCard card, int line)
    {
        if (next != null)
            card.Add(line, "auto: next ").Add(line, next.OutputName);
        else
            card.Add(line, st.InAmount > 0 || st.MixN > 0 ? "auto: no recipe" : "auto: by its input");
    }

    /// <summary>Why an Extruder or a Former waits, in a few words.</summary>
    public static string StallWord(ExtrudeStall s) => s switch
    {
        ExtrudeStall.NoPower => "no power",
        ExtrudeStall.NoMaterial => "needs material",
        ExtrudeStall.BadMaterial => "wrong material",
        ExtrudeStall.Mixed => "other material",
        ExtrudeStall.BadShape => "bad section",
        ExtrudeStall.AtCap => "at full size",
        ExtrudeStall.Blocked => "path blocked",
        ExtrudeStall.TooHeavy => "path too heavy",
        ExtrudeStall.ChuteWaits => "chute waits",
        ExtrudeStall.ConveyorSmall => "conveyor too small",
        ExtrudeStall.ConveyorFull => "conveyor full",
        ExtrudeStall.ConveyorCut => "conveyor cut",
        ExtrudeStall.NoOrders => "waiting for orders",
        ExtrudeStall.OrderMaterial => "order needs other stock",
        ExtrudeStall.OrderTooLong => "order longer than it makes",
        ExtrudeStall.OffcutFull => "offcut bin full",
        _ => "waiting",
    };

    /// <summary>"Smelter T3", "Extruder", "Wheel Former": a machine's kind on a line, in full.</summary>
    private static string KindName(ConstructPartDef d) => d.LineName ??= d.IsExtruder || d.IsAssembler || d.IsRecycler || d.IsGatherer || d.IsPress || d.IsLathe ? d.Name : d.Tier > 1 ? $"{Capital(d.RefineMachine)} T{d.Tier}" : Capital(d.RefineMachine);

    /// <summary>A machine's name on its console: its kind, and its number among those of its kind on this line in wire order ("Extruder 2") when there are two or more. Cached per def.</summary>
    internal static string NameOf(Construct c, ReadOnlySpan<int> list, int k)
    {
        var kind = KindName(c.DefOf(list[k]));
        int number = 0, same = 0;
        for (var j = 0; j < list.Length; j++)
            if (string.Equals(KindName(c.DefOf(list[j])), kind, StringComparison.Ordinal))
            {
                same++;
                if (j <= k)
                    number++;
            }
        if (same < 2)
            return kind;
        var d = c.DefOf(list[k]);
        var cache = d.LineNumbered ??= new string?[MaxListed + 1];
        return cache[number] ??= kind + " " + number;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Server: the first button: Start / Stop, or on the bill page Add.</summary>
    public static string? Button1(ConstructWorld world, Construct c, int part) => OnBill(c.Parts[part], c.DefOf(part)) ? WorkOrders.BillAdd(world, c, part) : Run(c, part);

    /// <summary>Server: the second button: Next, or Send.</summary>
    public static string? Button2(ConstructWorld world, Construct c, int part) => OnBill(c.Parts[part], c.DefOf(part)) ? WorkOrders.BillSend(world, c, part) : Next(c, part);

    /// <summary>Server: the third button: Recipe, or Clear the bill.</summary>
    public static string? Button3(ConstructWorld world, Construct c, int part) => OnBill(c.Parts[part], c.DefOf(part)) ? WorkOrders.BillClear(world, c, part) : Recipe(c, part);

    /// <summary>Server: the fourth button: the line page or the bill page.</summary>
    public static string? Page(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        if (!ConstructConfig.Has(p.D, CfgKey.LinePage))
            return "this console has no bill";
        ConstructConfig.Put(ref p, p.D, CfgKey.LinePage, OnBill(p, p.D) ? 0 : 1);
        return null;
    }

    /// <summary>Server: Start / Stop. The console is a Switch: its output is every wired machine's Run.</summary>
    public static string? Run(Construct c, int part)
    {
        c.Parts[part].State ^= 1;
        c.Wake();
        return null;
    }

    /// <summary>Server: the next machine on the line.</summary>
    public static string? Next(Construct c, int part)
    {
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, part, list);
        if (n == 0)
            return "no machines on this line: wire the console's output to a machine's Run";
        ref var p = ref c.Parts[part];
        ConstructConfig.Put(ref p, p.D, CfgKey.LinePick, (Pick(p, p.D, n) + 1) % n);
        return null;
    }

    /// <summary>The console's picked machine (a part index), or -1 with none on its line; <paramref name="same"/>: the machines of its kind on the line.</summary>
    public static int Picked(Construct c, int console, out int same)
    {
        same = 0;
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, console, list);
        if (n == 0)
            return -1;
        ref var p = ref c.Parts[console];
        var m = list[Pick(p, p.D, n)];
        for (var k = 0; k < n; k++)
            if (c.Parts[list[k]].Def == c.Parts[m].Def)
                same++;
        return m;
    }

    /// <summary>
    /// Server: the recipe picker (CActKind.LineRecipe): machine <paramref name="machine"/> on the console's line takes recipe
    /// <paramref name="value"/> (its recipe setting's value: 0 Auto or None, then each recipe its tier makes), and with
    /// <paramref name="all"/> every machine of its kind on the line. Null, or why not; a bad machine or value changes nothing.
    /// </summary>
    public static string? SetRecipe(Construct c, int console, int machine, int value, bool all)
    {
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, console, list);
        if (n == 0)
            return "no machines on this line: wire the console's output to a machine's Run";
        if (machine >= c.PartCount || !list[..n].Contains(machine))
            return "that machine isn't on this line";
        if (RecipeRefusal(c.Parts[machine].D, value) is { } refused)
            return refused;
        var def = c.Parts[machine].Def;
        for (var k = 0; k < n; k++)
        {
            var m = list[k];
            if (m != machine && (!all || c.Parts[m].Def != def))
                continue;
            ref var mp = ref c.Parts[m];
            ConstructConfig.Put(ref mp, mp.D, RecipeKey(mp.D), value);
        }
        c.Wake();
        return null;
    }

    /// <summary>
    /// The setting the recipe picker sets on machine kind <paramref name="md"/>: a Fan Former's Makes, an Airframe Former's Cuts, else its
    /// recipe setting (an Assembler's or refiner's recipe, a Panel Former's preset, a press's Pattern, a Die Lathe's Cuts).
    /// </summary>
    public static CfgKey RecipeKey(ConstructPartDef md) => md.IsFanFormer ? CfgKey.FanKind : md.IsAirframeFormer ? CfgKey.WingKind : CfgKey.Recipe;

    /// <summary>
    /// Why machine kind <paramref name="md"/> can't take recipe value <paramref name="value"/> from the recipe picker (its recipe setting's value: 0 Auto
    /// or None, then each recipe; a Panel Former's preset), or null. The console's pick and a machine's own Recipe button share it.
    /// </summary>
    public static string? RecipeRefusal(ConstructPartDef md, int value)
    {
        var key = RecipeKey(md);
        if (!ConstructConfig.Has(md, key) || md.CfgSlot[(int)key] < 0)
            return $"the {md.Name} has no recipe to pick";
        var options = md.Config[md.CfgSlot[(int)key]].Options.Length;
        if (value < 0 || value >= options || md.IsRefiner && value > md.RefineChoices.Length)
            return $"the {md.Name} has no such recipe";
        if (md.IsAssembler && value >= 1 && AssemblingTable.Current.ByMachine[md.AsmMachine] is var asm && value <= asm.Length && asm[value - 1].MinTier > md.Tier)
            return $"{asm[value - 1].Name} needs a tier {asm[value - 1].MinTier} assembler";
        if (md.IsLathe && value >= 1 && PressTable.Current.Lathe is var lathe && value <= lathe.Length && !ConstructPress.LatheCuts(md, lathe[value - 1]))
            return $"a {md.Name} cuts dies of its own size, not a {lathe[value - 1].Output.Name}";
        return null;
    }

    /// <summary>Server: the picked machine's next recipe (Auto, then each its tier makes).</summary>
    public static string? Recipe(Construct c, int part)
    {
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, part, list);
        if (n == 0)
            return "no machines on this line: wire the console's output to a machine's Run";
        ref var p = ref c.Parts[part];
        var m = list[Pick(p, p.D, n)];
        ref var mp = ref c.Parts[m];
        var md = mp.D;
        if (!ConstructConfig.Has(md, CfgKey.Recipe) || md.RefineChoices.Length == 0)
            return $"the {md.Name} has no recipe to pick";
        var v = ((int)ConstructConfig.Get(mp, md, CfgKey.Recipe, 0) + 1) % (md.RefineChoices.Length + 1);
        ConstructConfig.Put(ref mp, md, CfgKey.Recipe, v);
        return null;
    }
}
