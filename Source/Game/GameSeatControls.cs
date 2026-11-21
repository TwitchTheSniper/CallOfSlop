namespace CallOfSlop;

/// <summary>One row of the seat controls panel: a section's heading, or a control with its keys and its state.</summary>
public struct SeatRow
{
    public bool Heading;
    public string Text; // the control's words, or the section's name
    public string Key1, Key2; // its keys ("" for none: a heading, or a device that is on while you sit)
    public string State; // ON, OFF, DOWN, IN USE...; a heading's: what the section is linked to
    public byte Tone; // the state's colour: 0 dim, 1 on (cyan), 2 off or a warning (amber)
}

/// <summary>What the seat controls panel shows: the seat, how many links leave it, and its rows by section.</summary>
public sealed class SeatPanel
{
    public const int MaxRows = 48;
    public string Title = "", Links = "";
    public readonly SeatRow[] Rows = new SeatRow[MaxRows];
    public int Count;
}

/// <summary>
/// The seat controls panel's contents (Karl, 2026-11-21: "when sitting in a seat, a UI on the bottom-right ... it grows with the amount of
/// controls there are ... separate them into their relevant areas"; drawn by UI/UiSeatPanel.cs). Built from what the seat is linked to:
/// its Flight Stabilizer or Driving Module (FLIGHT or DRIVING, by the keys it answers to), its Turret Modules and the radar (WEAPONS), the
/// craft's landing legs, rail or dock and the seat's ejection handle (DOCKING AND LANDING), and every Input Reader that reads this seat,
/// followed through logic gates to what it works (MOTORS, LIGHTS, DOORS, OTHER DEVICES); Karl's sections, 2026-11-21: Flight, Driving, Motors, Weapons. Made again only when the seat, its links, a state it shows or
/// a bind changes: nothing allocates a frame.
/// </summary>
public sealed partial class Game
{
    private readonly SeatPanel _seatPanel = new();
    private long _seatPanelKey = long.MinValue;

    /// <summary>Set by the UI each frame it draws the seat controls panel: the HUD's lines then leave out the keys the panel lists.</summary>
    public bool SeatPanelShown;

    /// <summary>The panel for this PC's player's seat, or null when they don't sit in one (or fly a drone: its own lines say how).</summary>
    public SeatPanel? SeatControls()
    {
        if (!SandboxOn || Local is not { Alive: true } me)
            return null;
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!h.Seated || Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || ConstructRemote.Piloted(Constructs, me.Id) != null)
            return null;
        int seat = h.SeatPart;
        var gear = ConstructDoors.GearState(c);
        var hint = seat == c.DriveSeat ? ConstructCarrier.Hint(Constructs, c) : null;
        var hintKind = hint == null ? 0 : ReferenceEquals(hint, SandboxKeys.PilotUndock) ? 1 : ReferenceEquals(hint, SandboxKeys.PilotLaunch) ? 2 : 3;
        var turret = ConstructModules.ActiveGun(c, seat);
        var key = (long)c.Id << 48 ^ (long)seat << 36 ^ (long)c.RebuildStamp << 12 ^ (long)SandboxKeys.Stamp * 1000003L
                  ^ (ConstructFlight.DampenersOff(c) ? 1L : 0) ^ (ConstructFlight.MouseSteerOff(c) ? 2L : 0) ^ (long)gear << 2 ^ (long)hintKind << 4
                  ^ (RadarCraft == c && RadarCanLock ? 1L << 6 : 0) ^ (long)(turret + 1) << 7 ^ (c.DriveSeat == seat ? 1L << 62 : 0)
                  ^ (c.EngineRunning ? 1L << 61 : 0) ^ (long)c.StabCount << 56;
        if (key != _seatPanelKey)
        {
            _seatPanelKey = key;
            ComposeSeatPanel(c, seat, gear, hintKind);
        }
        return _seatPanel;
    }

    private void ComposeSeatPanel(Construct c, int seat, int gear, int hintKind)
    {
        var sp = _seatPanel;
        sp.Count = 0;
        var links = 0;
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == seat && c.Wires[k].To < c.PartCount)
                links++;
        sp.Title = Short(LogicText.Name(c, seat)).ToUpperInvariant();
        sp.Links = links == 1 ? "1 LINK" : $"{links} LINKS";
        var driver = seat == c.DriveSeat;

        // FLIGHT or DRIVE: what the seat's Flight Stabilizer or Driving Module answers to.
        if (driver && c.StabCount > 0 && !c.CanDrive && c.Stabs[0].Part < c.PartCount)
        {
            ref var p = ref c.Parts[c.Stabs[0].Part];
            Head("FLIGHT", LogicText.Name(c, c.Stabs[0].Part));
            switch (ConstructFlight.KeysOf(c, p, p.D))
            {
                case FlightPattern.Ship:
                {
                    var mouse = !ConstructFlight.MouseSteerOff(c) && ConstructConfig.Get(p, p.D, CfgKey.MouseAim, 0) >= 0.5f;
                    if (mouse)
                        Row("MOUSE", "", "steer the nose");
                    Row(SandboxKeys.KForward, SandboxKeys.KBack, "forward, back");
                    Row(SandboxKeys.KLeft, SandboxKeys.KRight, mouse ? "slide" : "yaw");
                    Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "roll");
                    Row(SandboxKeys.Sprint, SandboxKeys.KCrouch, "up, down");
                    if (ConstructFlight.HasDampeners(c))
                        Row(SandboxKeys.KDampeners, "", "inertia dampeners", ConstructFlight.DampenersOff(c) ? "OFF" : "ON", ConstructFlight.DampenersOff(c) ? (byte)2 : (byte)1);
                    if (ConstructFlight.HasMouseSteer(c))
                        Row(SandboxKeys.KMouseSteer, "", "mouse steering", mouse ? "ON" : "OFF", mouse ? (byte)1 : (byte)2);
                    break;
                }
                case FlightPattern.Plane:
                    Row(SandboxKeys.KForward, SandboxKeys.KBack, "nose down, up");
                    Row(SandboxKeys.KLeft, SandboxKeys.KRight, "roll");
                    Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "yaw");
                    Row(SandboxKeys.Sprint, SandboxKeys.KCrouch, "throttle");
                    Row(SandboxKeys.JumpKey, "", "brake");
                    break;
                case FlightPattern.Rocket:
                    Row(SandboxKeys.KForward, SandboxKeys.KBack, "lean on, back");
                    Row(SandboxKeys.KLeft, SandboxKeys.KRight, "lean left, right");
                    Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "spin");
                    Row(SandboxKeys.Sprint, SandboxKeys.KCrouch, "throttle");
                    break;
                default:
                    Row(SandboxKeys.KForward, SandboxKeys.KBack, "forward, back");
                    Row(SandboxKeys.KLeft, SandboxKeys.KRight, "yaw");
                    Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "roll");
                    Row(SandboxKeys.Sprint, SandboxKeys.KCrouch, "climb, descend");
                    break;
            }
        }
        else if (driver && (c.CanDrive || c.HasHubMotor))
        {
            var gears = c.Transmission >= 0 && c.Transmission < c.PartCount && c.DefOf(c.Transmission).HasGears ? c.DefOf(c.Transmission) : null;
            var shifts = gears is { Trans: TransKind.Manual or TransKind.Sequential } && gears.Settings.Length > 1 && c.Parts[c.Transmission].InCount == 0;
            Head("DRIVING", c.Engine >= 0 && c.Engine < c.PartCount ? LogicText.Name(c, c.Engine) : "");
            if (c.Electrical && c.CanDrive && !c.EngineRunning)
                Row(SandboxKeys.KForward, "", "start", "ENGINE OFF", 2);
            Row(SandboxKeys.KForward, SandboxKeys.KBack, "drive, reverse");
            Row(SandboxKeys.KLeft, SandboxKeys.KRight, "steer");
            Row(SandboxKeys.JumpKey, "", "brake");
            if (shifts)
                Row(SandboxKeys.Reload, SandboxKeys.Sprint, "gear up, down");
        }
        else if (driver && (c.ThrusterCount > 0 || c.RotorCount > 0))
        {
            Head("FLIGHT", "");
            Row(SandboxKeys.KForward, SandboxKeys.KBack, "thrust");
            Row(SandboxKeys.KLeft, SandboxKeys.KRight, "turn");
            Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "roll");
            Row(SandboxKeys.Sprint, "", "up and hold");
            Row(SandboxKeys.KCrouch, "", "down");
        }
        else if (driver && c.LegCount > 0)
        {
            Head("WALK", "");
            Row(SandboxKeys.KForward, SandboxKeys.KBack, "walk");
            Row(SandboxKeys.KLeft, SandboxKeys.KRight, "turn");
            Row(SandboxKeys.KRollLeft, SandboxKeys.KRollRight, "strafe");
            Row(SandboxKeys.Sprint, SandboxKeys.KCrouch, "raise, lower");
        }

        // WEAPONS: each Turret Module linked from the seat (Aim steps between them), a gun this seat works unwired, the radar's lock.
        var weapons = false;
        for (var k = 0; k < c.TurretModCount; k++)
        {
            ref var tm = ref c.TurretMods[k];
            if (tm.Seat != seat || tm.Part >= c.PartCount)
                continue;
            var guns = 0;
            var first = -1;
            for (var g = 0; g < c.GunCount; g++)
                if (c.Guns[g].Turret == tm.Part)
                {
                    guns++;
                    if (first < 0)
                        first = c.Guns[g].Part;
                }
            if (!weapons)
            {
                Head("WEAPONS", "");
                weapons = true;
            }
            var what = first < 0 ? "fire (no gun linked)" : guns > 1 ? $"fire {Short(LogicText.Name(c, first))} x{guns}" : $"fire {Short(LogicText.Name(c, first))}";
            var inUse = tm.Of > 1 && c.Parts[tm.Part].Out != 0;
            Row(SandboxKeys.Fire, "", what, tm.Of > 1 ? inUse ? "IN USE" : $"{tm.Slot + 1}/{tm.Of}" : "", inUse ? (byte)1 : (byte)0);
        }
        if (!weapons)
            for (var g = 0; g < c.GunCount; g++)
                if (c.Guns[g].Seat == seat && c.Guns[g].Turret < 0)
                {
                    Head("WEAPONS", "");
                    weapons = true;
                    Row(SandboxKeys.Fire, "", $"fire {Short(LogicText.Name(c, c.Guns[g].Part))}");
                    break;
                }
        if (ConstructModules.TurretsOf(c, seat) > 1)
            Row(SandboxKeys.Aim, "", "next turret");
        if (RadarCraft == c && RadarCanLock)
        {
            if (!weapons)
                Head("WEAPONS", "");
            Row(SandboxKeys.KRadar, "", "radar lock, again: next");
        }

        // DOCKING AND LANDING: the craft's landing leg pods (any seat works them), its rail or dock, the seat's ejection handle.
        var eject = c.Parts[seat].D.SeatEject;
        if (gear > 0 || hintKind > 0 || eject)
        {
            Head("DOCKING AND LANDING", "");
            if (hintKind > 0)
                Row(SandboxKeys.KHook, "", hintKind == 1 ? "undock" : hintKind == 2 ? "launch" : "hook to the rail");
            if (gear > 0)
                Row(SandboxKeys.KGear, "", "landing legs", gear == 2 ? "DOWN" : "UP", gear == 2 ? (byte)1 : (byte)0);
            if (eject)
                Row(SandboxKeys.KEject, "", "eject");
        }

        // LIGHTS, DOORS, DEVICES: the Input Readers that read this seat (wired from it, or unwired on the driver's seat), and parts wired
        // straight from the seat (on while someone sits), each to what it works at the end of its gates.
        var start = sp.Count;
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.From != seat || w.To >= c.PartCount)
                continue;
            var td = c.DefOf(w.To);
            if (td.Logic is CLogic.FlightStabilizer or CLogic.DriveModule or CLogic.TurretModule)
                continue;
            if (td.Logic == CLogic.Reader)
                ReaderRow(c, w.To);
            else
                DeviceRow(c, w.To, "", "", "while you sit");
        }
        if (driver)
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.Logic == CLogic.Reader && !WiredIn(c, i))
                    ReaderRow(c, i);
        GroupDevices(start);

        Head("SEAT", "");
        Row(SandboxKeys.Use, "", "get out");
    }

    /// <summary>An Input Reader's row: the key it reads, and what it works.</summary>
    private void ReaderRow(Construct c, int reader)
    {
        var d = c.DefOf(reader);
        ref var p = ref c.Parts[reader];
        var bit = d.Settings.Length > 0 ? (int)d.Settings[Math.Min(p.Setting, (byte)(d.Settings.Length - 1))] : 0;
        var key = "?";
        foreach (var (name, b) in ConstructParts.ReaderKeys)
            if (b == bit)
                key = SandboxKeys.ReaderKey(name);
        DeviceRow(c, reader, key, LogicText.Name(c, reader) != d.Name ? LogicText.Name(c, reader) : "", "");
    }

    private static readonly int[] DevStack = new int[64];
    private static readonly int[] DevEnds = new int[8];

    /// <summary>A row for a reader or a part wired from the seat: what is at the end of its wires, through logic gates, and its section.</summary>
    private void DeviceRow(Construct c, int from, string key, string label, string state)
    {
        var ends = 0;
        var more = 0;
        if (c.DefOf(from).Logic != CLogic.Reader)
            DevEnds[ends++] = from;
        else
        {
            var top = 0;
            DevStack[top++] = from;
            for (var guard = 0; top > 0 && guard < 64; guard++)
            {
                var at = DevStack[--top];
                for (var k = 0; k < c.WireCount; k++)
                {
                    if (c.Wires[k].From != at || c.Wires[k].To >= c.PartCount || c.Wires[k].To == from)
                        continue;
                    int to = c.Wires[k].To;
                    if (Gate(c.DefOf(to).Logic) && top < DevStack.Length)
                        DevStack[top++] = to;
                    else if (Array.IndexOf(DevEnds, to, 0, ends) < 0)
                    {
                        if (ends < DevEnds.Length)
                            DevEnds[ends++] = to;
                        else
                            more++;
                    }
                }
            }
        }
        string text;
        if (label.Length > 0)
            text = label;
        else if (ends == 0)
            text = "(not linked to anything)";
        else
        {
            // The ends by name, the same name once with a count ("Bulkhead Light x4").
            var sb = new System.Text.StringBuilder();
            var shown = 0;
            for (var e = 0; e < ends; e++)
            {
                var name = Short(LogicText.Name(c, DevEnds[e]));
                var seen = false;
                for (var f = 0; f < e && !seen; f++)
                    seen = Short(LogicText.Name(c, DevEnds[f])) == name;
                if (seen)
                    continue;
                var same = 0;
                for (var f = e; f < ends; f++)
                    if (Short(LogicText.Name(c, DevEnds[f])) == name)
                        same++;
                if (shown == 2)
                {
                    more += ends - e;
                    break;
                }
                if (shown > 0)
                    sb.Append(", ");
                sb.Append(name);
                if (same > 1)
                    sb.Append(" x").Append(same);
                shown++;
            }
            if (more > 0)
                sb.Append(" +").Append(more);
            text = sb.ToString();
        }
        var kind = ends == 0 ? (byte)3 : c.DefOf(DevEnds[0]).Logic == CLogic.Lamp ? (byte)1 : ConstructDoor.Swings(c, DevEnds[0]) ? (byte)2 : c.DefOf(DevEnds[0]).HasJoint || c.DefOf(DevEnds[0]).IsRail ? (byte)0 : (byte)3;
        if (_seatPanel.Count < SeatPanel.MaxRows - 2)
            _seatPanel.Rows[_seatPanel.Count++] = new SeatRow { Text = text, Key1 = Cap(key), Key2 = "", State = state, Tone = (byte)(10 + kind) }; // (10+: its section, sorted out in GroupDevices)
    }

    /// <summary>Sorts the device rows from <paramref name="start"/> into MOTORS (hinges, pistons, turntables, rails), LIGHTS, DOORS and OTHER DEVICES, each under its heading.</summary>
    private void GroupDevices(int start)
    {
        var sp = _seatPanel;
        var n = sp.Count - start;
        if (n <= 0)
            return;
        var copy = new SeatRow[n];
        Array.Copy(sp.Rows, start, copy, 0, n);
        sp.Count = start;
        for (var kind = 10; kind <= 13; kind++)
        {
            var any = false;
            foreach (var r in copy)
            {
                if (r.Tone != kind)
                    continue;
                if (!any)
                {
                    Head(kind == 10 ? "MOTORS" : kind == 11 ? "LIGHTS" : kind == 12 ? "DOORS" : "OTHER DEVICES", "");
                    any = true;
                }
                if (sp.Count < SeatPanel.MaxRows - 2)
                    sp.Rows[sp.Count++] = r with { Tone = 0 };
            }
        }
    }

    /// <summary>A logic part the panel looks through to what it works.</summary>
    private static bool Gate(CLogic l) => l is CLogic.And or CLogic.Or or CLogic.Not or CLogic.Xor or CLogic.Nand or CLogic.Toggle or CLogic.Delay or CLogic.Timer
        or CLogic.Compare or CLogic.Scale or CLogic.Latch or CLogic.Filter or CLogic.Mux or CLogic.Counter or CLogic.Memory;

    private static bool WiredIn(Construct c, int part)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].To == part)
                return true;
        return false;
    }

    /// <summary>A part's name for the panel: what is in brackets left out, cut at 30 characters.</summary>
    private static string Short(string name)
    {
        var b = name.IndexOf(" (", StringComparison.Ordinal);
        if (b > 0)
            name = name[..b];
        return name.Length > 30 ? name[..29] + "." : name;
    }

    private void Head(string text, string state)
    {
        if (_seatPanel.Count < SeatPanel.MaxRows)
            _seatPanel.Rows[_seatPanel.Count++] = new SeatRow { Heading = true, Text = text, Key1 = "", Key2 = "", State = state.Length > 0 ? Short(state).ToUpperInvariant() : "" };
    }

    private void Row(string key1, string key2, string text, string state = "", byte tone = 0)
    {
        if (_seatPanel.Count < SeatPanel.MaxRows)
            _seatPanel.Rows[_seatPanel.Count++] = new SeatRow { Text = text, Key1 = Cap(key1), Key2 = Cap(key2), State = state, Tone = tone };
    }

    /// <summary>A key's name on its keycap: short and in capitals ("Left Shift" is SHIFT).</summary>
    private static string Cap(string key)
    {
        if (key.Length == 0)
            return key;
        key = key.StartsWith("Left ", StringComparison.Ordinal) ? key[5..] : key.StartsWith("Right ", StringComparison.Ordinal) ? "R " + key[6..] : key;
        return key.Replace("Control", "Ctrl").ToUpperInvariant();
    }
}
