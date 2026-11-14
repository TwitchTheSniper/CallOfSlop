using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The display parts (docs/sandbox/CONSTRUCTS.md, "Displays"): panels (<see cref="PanelCard"/>) filled from a wired number or from
/// the construct's state. A pose's signals are tenths and stop at 12.7, too coarse for a readout, so a part's
/// <see cref="CPart.Reading"/> reaches clients on its own: a CBody carries them all, and <see cref="Scan"/> sends what changed five
/// times a second (<see cref="CBodyKind.Reading"/>). A Graph Screen keeps its own last 30 s on each peer that draws, sampled in
/// <see cref="Sample"/> whether or not it is in view; a joiner's starts empty. An inventory tile fills the same card from a scratch
/// part (<see cref="Preview"/>), so a preview can't drift from the real panel.
/// </summary>
public static class LogicDisplays
{
    public const int TraceLength = 60;
    public const float TraceEvery = 0.5f; // seconds a sample
    public const int ScanTicks = World.TickRate / 5;
    private static double _sampledAt;

    public static bool IsGraph(ConstructPartDef d) => Array.Exists(d.Panels, x => x.Source == "logic.graph");

    /// <summary>Server, five times a second: readings that moved.</summary>
    public static void Scan(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
            for (var k = 0; k < c.ReadingParts.Length; k++)
            {
                int part = c.ReadingParts[k];
                if (part >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[part];
                if (MathF.Abs(p.Reading - p.ReadingSent) > 0.0005f + MathF.Abs(p.Reading) * 0.001f)
                    world.ReadingDirty.Add((c.Id, (ushort)part));
            }
    }

    /// <summary>A batch of readings (CBodyKind.Reading): u8 count, then u16 body, u16 part, f32 each. Returns where it got to.</summary>
    public static int WriteChanges(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Reading);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 120; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount || !c.DefOf(part).HasReading)
                continue;
            ref var p = ref c.Parts[part];
            p.ReadingSent = p.Reading;
            w.U16(id).U16(part).F32(p.Reading);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    public static void ReadChanges(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var part = r.U16();
            var v = r.F32();
            if (world.Get(id) is { } c && part < c.PartCount && c.DefOf(part).HasReading && float.IsFinite(v))
            {
                c.Parts[part].Reading = v;
                if (c.DefOf(part).Logic != CLogic.Display)
                    PanelRender.Touch++; // (a key shows at once)
            }
        }
    }

    /// <summary>CBody's tail: every reading, for a joiner.</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        w.U16((ushort)c.ReadingParts.Length);
        for (var k = 0; k < c.ReadingParts.Length; k++)
            w.U16(c.ReadingParts[k]).F32(c.ReadingParts[k] < c.PartCount ? c.Parts[c.ReadingParts[k]].Reading : 0);
    }

    public static void Read(ref PacketReader r, Construct c)
    {
        int n = r.U16();
        for (var k = 0; k < n; k++)
        {
            var part = r.U16();
            var v = r.F32();
            if (part >= c.PartCount || !float.IsFinite(v))
                throw new MalformedPacketException($"a reading on part {part}");
            c.Parts[part].Reading = v;
        }
    }

    /// <summary>Each frame drawn: Graph Screens take a sample every <see cref="TraceEvery"/>.</summary>
    public static void Sample(ConstructWorld world, double now)
    {
        if (now - _sampledAt < TraceEvery && now >= _sampledAt)
            return;
        _sampledAt = now;
        foreach (var c in world.Bodies)
            for (var k = 0; k < c.GraphParts.Length; k++)
            {
                int part = c.GraphParts[k];
                if (part >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[part];
                p.Trace ??= new float[TraceLength];
                p.Trace[p.TraceAt] = p.Reading;
                p.TraceAt = (byte)((p.TraceAt + 1) % TraceLength);
            }
    }

    /// <summary>A panel's "ink" setting, on any part that has one.</summary>
    public static void Ink(in CPart p, ConstructPartDef d, PanelCard card)
    {
        var ink = ConstructConfig.Get(p, d, CfgKey.Ink, 0);
        if (ink != 0)
        {
            card.HasInk = true;
            card.Ink = ConstructConfig.Rgb(ink);
        }
    }

    public static bool Live(Construct c, int part, PanelCard card)
    {
        Ink(c.Parts[part], c.Parts[part].D, card);
        if (!c.Electrical || ConstructPower.On(c, part))
            return true;
        card.State = PanelState.Off;
        card.Add(0, "no power");
        return false;
    }

    private static float Share(in CPart p, ConstructPartDef d, float v)
    {
        var lo = ConstructConfig.Get(p, d, CfgKey.Low, 0);
        var hi = ConstructConfig.Get(p, d, CfgKey.High, 1);
        return hi != lo ? Math.Clamp((v - lo) / (hi - lo), 0, 1) : 0;
    }

    private static void Number(PanelCard card, int line, in CPart p, ConstructPartDef d, float v)
    {
        card.AddFixed(line, v, (int)ConstructConfig.Get(p, d, CfgKey.Decimals, 0));
        var slot = d.CfgSlot[(int)CfgKey.Unit];
        if (slot < 0)
            return;
        var e = d.Config[slot];
        var unit = (int)p.Cfg[slot];
        if (unit > 0 && unit < e.Options.Length)
            card.Add(line, ' ').Add(line, e.Options[unit]);
    }

    public static void Readout(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        card.State = PanelState.Working;
        card.Fit = true;
        Number(card, 0, p, p.D, p.Reading);
    }

    /// <summary>The Bar Gauges: the bar is the whole screen, with no number (a Number Readout says the number).</summary>
    private static void Bar(Construct c, int part, PanelCard card, PanelDraw draw)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        card.State = PanelState.Working;
        card.Draw = draw;
        card.Ramp = (PanelRamp)ConstructConfig.Get(p, p.D, CfgKey.Ramp, 0);
        card.Value = Share(p, p.D, p.Reading);
    }

    public static void BarH(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card) => Bar(c, part, card, PanelDraw.BarH);

    public static void BarV(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card) => Bar(c, part, card, PanelDraw.BarV);

    public static void Dial(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        Readout(world, c, part, panel, card);
        ref var p = ref c.Parts[part];
        card.Draw = PanelDraw.Dial;
        card.Fit = false;
        card.Value = Share(p, p.D, p.Reading);
    }

    /// <summary>The plot is the whole screen, with no number.</summary>
    public static void Graph(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        card.State = PanelState.Working;
        card.Draw = PanelDraw.Graph;
        card.Ramp = (PanelRamp)ConstructConfig.Get(p, p.D, CfgKey.Ramp, 0);
        card.Trace = p.Trace;
        card.TraceAt = p.TraceAt;
        card.TraceLow = ConstructConfig.Get(p, p.D, CfgKey.Low, 0);
        card.TraceHigh = ConstructConfig.Get(p, p.D, CfgKey.High, 1);
    }

    public static void Lamps(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        var d = p.D;
        card.State = PanelState.Idle;
        card.Draw = PanelDraw.Lamps;
        card.Lamps = d.DataIn.Length;
        card.LampBits = (int)p.Reading;
        // The labels, comma-separated; a lamp with none is its number.
        var text = LogicText.Get(p, 0).AsSpan();
        for (var k = 0; k < card.Lamps; k++)
        {
            var end = text.IndexOf(',');
            var label = (end < 0 ? text : text[..end]).Trim();
            text = end < 0 ? default : text[(end + 1)..];
            if (label.Length == 0)
                card.Add(k, k + 1);
            else
                foreach (var ch in label)
                    card.Add(k, ch);
        }
    }

    public static void Sign(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (panel.Board)
            Ink(c.Parts[part], c.Parts[part].D, card); // (a painted board needs no power: it reads the same with the battery flat)
        else if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        card.State = PanelState.Idle;
        card.Plain = true;
        var text = LogicText.Get(p, p.Reading >= 0.5f && p.Text2 != null ? 1 : 0);
        int at = 0, column = 0;
        if (panel.Board)
        {
            // A painted board: the part's paint behind; one row holds the whole line, made smaller to fit, never wrapped.
            card.HasBoard = true;
            card.Board = ConstructPaint.ToVector(ConstructPaint.Sample(p));
            card.Wrap(text, panel.Rows == 1 ? PanelCard.MaxChars : PanelRender.Columns(panel.Size, panel.Rows), 0, panel.Rows - 1, 0, ref at, ref column);
            return;
        }
        card.Wrap(text, PanelRender.Columns(panel.Size, panel.Rows), 0, panel.Rows - 1, 0, ref at, ref column);
    }

    public static void Compass(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        var heading = LogicSensors.Heading(c, part);
        card.State = PanelState.Working;
        card.Draw = PanelDraw.Compass;
        card.Value = heading / 360;
        card.Add(0, (int)MathF.Round(heading) % 360);
    }

    public static void Altimeter(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        card.State = PanelState.Working;
        card.Fit = true;
        Number(card, 0, p, p.D, LogicSensors.Altitude(c, part));
    }

    public static void Speedometer(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        var d = p.D;
        var units = ConstructConfig.Get(p, d, CfgKey.Units, 1);
        var v = c.Vel.Length() * (units >= 1.5f ? 2.23694f : units >= 0.5f ? 3.6f : 1f);
        card.State = PanelState.Working;
        card.Draw = PanelDraw.Dial;
        card.Value = Share(p, d, v);
        card.AddFixed(0, v, 0);
        card.Add(0, units >= 1.5f ? "mph" : units >= 0.5f ? "km/h" : "m/s"); // (no space: a Speedometer's first row holds six)
    }

    public static void Keypad(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!Live(c, part, card))
            return;
        ref var p = ref c.Parts[part];
        var d = p.D;
        card.Fit = true;
        if (d.Logic == CLogic.Numpad)
        {
            card.State = p.Reading < 0 ? PanelState.Idle : PanelState.Working;
            if (p.Reading < 0)
                card.AddFixed(0, ConstructConfig.Get(p, d, CfgKey.Value, 0), (int)ConstructConfig.Get(p, d, CfgKey.Decimals, 0));
            else
                card.Add(0, (int)p.Reading);
            return;
        }
        var open = (p.Out >= 0.5f) != (ConstructConfig.Get(p, d, CfgKey.Invert, 0) >= 0.5f);
        card.State = p.Reading > 0 ? PanelState.Working : open ? PanelState.Ok : PanelState.Wants;
        if (p.Reading > 0)
            for (var k = 0; k < (int)p.Reading; k++)
                card.Add(0, '*');
        else
            card.Add(0, open ? "OPEN" : "LOCKED");
    }

    /// <summary>
    /// What an inventory tile shows on a display part: its first panel, filled by the part's real fill from a scratch part given a
    /// typical reading. False for a part with no such panel. Made once a tile, so this may allocate.
    /// </summary>
    public static bool Preview(ConstructPartDef d, PanelCard card, out PanelDef panel)
    {
        panel = null!;
        if (d.Logic is not (CLogic.Display or CLogic.Instrument or CLogic.Numpad or CLogic.CodeLock) || d.Panels.Length == 0 || d.Panels[0].Fill == null)
            return false;
        panel = d.Panels[0];
        var world = new ConstructWorld();
        if (ConstructEdits.DropLoose(world, d, new Vector3(0, 120, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)) is not { } c)
            return false;
        ref var p = ref c.Parts[0];
        var lo = ConstructConfig.Get(p, d, CfgKey.Low, 0);
        var hi = ConstructConfig.Get(p, d, CfgKey.High, 1);
        var typical = lo + (hi - lo) * 0.62f;
        c.Vel = new Vector3(0, 0, typical / 3.6f); // (a Speedometer's default units are km/h)
        p.Reading = panel.Source switch
        {
            "logic.readout" => 42.5f,
            "logic.lamps" => 0x55,
            _ => d.Logic == CLogic.Numpad ? 1234 : d.Logic == CLogic.CodeLock ? 0 : typical,
        };
        if (panel.Source == "logic.sign")
            LogicText.Set(ref p, 0, "HELLO WORLD");
        if (panel.Source == "logic.graph")
        {
            p.Trace = new float[TraceLength];
            for (var k = 0; k < TraceLength; k++)
                p.Trace[k] = lo + (hi - lo) * (0.5f + 0.35f * MathF.Sin(k * 0.19f) + 0.08f * MathF.Sin(k * 0.7f));
        }
        card.Clear();
        panel.Fill(world, c, 0, panel, card);
        return true;
    }

    /// <summary>The look line's state for a display part (exact on every peer; the pose's signal is in tenths).</summary>
    public static string Look(in CPart p, ConstructPartDef d)
    {
        if (d.Logic == CLogic.Numpad)
            return p.Reading < 0 ? AnalogLever.Text(ConstructConfig.Get(p, d, CfgKey.Value, 0)) : "typing " + ((int)p.Reading).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return d.Logic == CLogic.CodeLock ? p.Reading > 0 ? "typing" : (p.Out >= 0.5f) != (ConstructConfig.Get(p, d, CfgKey.Invert, 0) >= 0.5f) ? "open" : "locked" : p.Reading.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
