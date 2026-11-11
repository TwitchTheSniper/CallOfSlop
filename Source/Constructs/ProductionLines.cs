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
/// machine works (<see cref="DrawLamp"/>; tools/constructs/screens.py puts it over the machine's screen);</item>
/// <item>the <b>Line Console</b>: a Switch whose output is wired to each machine's Run. The wires are the line: its screen (panel source <c>line</c>)
/// lists the machines they reach, and its buttons start and stop the line (the switch), pick a machine and step that machine's recipe.</item>
/// </list>
/// Nothing here allocates; a console's list is walked from the construct's routes when its panel fills (4 Hz, in view) or a button is pressed.
/// </summary>
public static class ProductionLines
{
    public const int MaxShown = 4; // machines a console's screen lists
    private const int MaxListed = 32; // machines a console's buttons step through
    private const double Hold = 0.3; // seconds a new state must last before the lamp and console show it (a store's bytes and its Busy can come a packet apart)

    /// <summary>A machine's raw state, or null for a part that isn't a production machine of a line yet.</summary>
    public static LineState? Raw(ConstructPartDef d, CStore st)
    {
        if (!d.IsRefiner)
            return null;
        if (st.Held)
            return LineState.Idle;
        if (st.Busy)
            return LineState.Running;
        return ConstructRefining.StateOf(RefiningTable.Current, d, st, out _) switch
        {
            RefineState.Empty or RefineState.Short => LineState.Starved,
            RefineState.OutputFull or RefineState.OutputMixed => LineState.Blocked,
            RefineState.NoRecipe or RefineState.TierLow => LineState.Fault,
            _ => LineState.NoPower, // (ready, not held, not working: the step found no power)
        };
    }

    /// <summary>The state shown: a new one once it has lasted <see cref="Hold"/>. Keeps its memory on the store (this peer's own, never sent).</summary>
    public static LineState Of(ConstructPartDef d, CStore st, double now)
    {
        if (Raw(d, st) is not { } raw)
            return LineState.Idle;
        if (raw != st.LineRaw)
        {
            st.LineRaw = raw;
            st.LineSince = now;
        }
        if (raw != st.LineShown && (now - st.LineSince >= Hold || now < st.LineSince || raw == LineState.Running && st.LineShown == LineState.Idle))
            st.LineShown = raw; // (pressing Start shows at once)
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
    public static int DrawLamp(Renderer gfx, ConstructPartDef d, CStore st, byte rot, in CPartBox box, Vector3 centre, in Matrix4x4 frame, float opacity, double now)
    {
        Look(Of(d, st, now), out var colour, out var blink, out var chase);
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

    /// <summary>The machines a console's output reaches by intact wires, in the order the wires were laid, each once. Allocates nothing.</summary>
    public static int Machines(Construct c, int console, Span<int> into)
    {
        var n = 0;
        for (var k = 0; k < c.RouteCount && n < into.Length; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.Source != console || !r.FedEnd || r.To >= c.PartCount || NotMachine(c, r.To))
                continue;
            if (!into[..n].Contains(r.To))
                into[n++] = r.To;
        }
        return n;
    }

    private static bool NotMachine(Construct c, int part) => !c.DefOf(part).IsRefiner; // (refiners are the lines' machines so far)

    private static int Pick(in CPart p, ConstructPartDef d, int count) => count == 0 ? 0 : Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.LinePick, 0), 0, count - 1);

    /// <summary>The console's screen: the line's state, then a row for each machine (a marker on the picked one), its state and recipe.</summary>
    public static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        LogicDisplays.Ink(p, d, card);
        Span<int> list = stackalloc int[MaxListed];
        var n = Machines(c, part, list);
        var on = p.Out >= 0.5f;
        card.TouchLabels[0] = on ? "Stop" : "Start";
        if (n == 0)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Line: no machines");
            card.Add(2, "Wire its output to the");
            card.Add(3, "Run of each machine.");
            return;
        }
        var pick = Pick(p, d, n);
        var worst = PanelState.Working;
        var running = 0;
        var first = Math.Clamp(pick - (MaxShown - 1), 0, Math.Max(0, n - MaxShown)); // (the picked machine stays on the screen)
        for (var k = 0; k < n; k++)
        {
            var md = c.DefOf(list[k]);
            if (world.StoreOf(c, list[k]) is not { } st)
                continue;
            var s = Of(md, st, ConstructRender.Now);
            if (s == LineState.Running)
                running++;
            else if (s != LineState.Idle && Panel(s) > worst)
                worst = Panel(s);
            if (k < first || k >= first + MaxShown)
                continue;
            var line = 1 + k - first;
            card.Add(line, k == pick ? '>' : ' ');
            Pad(card, line, ShortName(md), 11);
            card.Add(line, ' ');
            Pad(card, line, Word(s), 8);
            card.Add(line, ' ');
            Pad(card, line, ConstructRefining.SetOf(c.Parts[list[k]], md)?.OutputName ?? "auto", 11);
        }
        card.State = !on ? PanelState.Idle : worst;
        card.Add(0, on ? "Line running " : "Line stopped ").Add(0, running).Add(0, '/').Add(0, n);
        if (n > MaxShown)
            card.Add(5, "Machine ").Add(5, pick + 1).Add(5, " of ").Add(5, n);
    }

    /// <summary>"Industrial Smelter" -> "Smelter T3": the line's machines are told apart by kind and tier.</summary>
    private static string ShortName(ConstructPartDef d) => d.LineName ??= d.Tier > 1 ? $"{Capital(d.RefineMachine)} T{d.Tier}" : Capital(d.RefineMachine);

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static void Pad(PanelCard card, int line, string s, int width)
    {
        for (var k = 0; k < width; k++)
            card.Add(line, k < s.Length ? s[k] : ' ');
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
