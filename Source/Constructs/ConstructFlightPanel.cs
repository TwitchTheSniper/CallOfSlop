using System.Numerics;

namespace CallOfSlop;

public sealed partial class Construct
{
    /// <summary>
    /// Where its lift acts and how much it lifts at sea level, rest frame, for the balance markers and anything else that asks. This is the one place that adds lift up:
    /// a new kind of lifting thing adds itself here (<see cref="ConstructFlight.AddLift"/>).
    /// </summary>
    public bool LiftCentre(out Vector3 at, out float kg)
    {
        kg = 0;
        var sum = Vector3.Zero;
        var weight = 0f;
        for (var k = 0; k < CellCount; k++)
        {
            ref var cell = ref Cells[k];
            var lift = cell.LiftKg * ConstructLift.Share(cell.Mode, cell.Fill, 1);
            ConstructFlight.AddLift(ref sum, ref weight, ref kg, cell.At, lift, cell.Volume * 1e-3f);
        }
        at = weight > 0 ? sum / weight : Com;
        return weight > 0;
    }

    /// <summary>Where its thrusters' push acts, which way, and how hard (N), rest frame, as if all ran flat out.</summary>
    public bool ThrustCentre(out Vector3 at, out Vector3 dir, out float force)
    {
        var sum = Vector3.Zero;
        var push = Vector3.Zero;
        var weight = 0f;
        for (var k = 0; k < ThrusterCount; k++)
        {
            ref var t = ref Thrusters[k];
            var f = Parts[t.Part].D.ThrustForce;
            sum += t.At * f;
            push += t.Dir * f;
            weight += f;
        }
        force = push.Length();
        at = weight > 0 ? sum / weight : Com;
        dir = force > 0 ? push / force : Vector3.UnitY;
        return weight > 0;
    }
}

public static partial class ConstructFlight
{
    /// <summary>Adds one lifting thing to a lift centre: <paramref name="kg"/> at sea level at <paramref name="at"/>; <paramref name="share"/>: what it counts for while it lifts nothing yet, so an empty cell still shows where it will lift.</summary>
    public static void AddLift(ref Vector3 sum, ref float weight, ref float total, Vector3 at, float kg, float share)
    {
        var w = MathF.Max(kg, share);
        sum += at * w;
        weight += w;
        total += kg;
    }

    /// <summary>Does this construct fly, or mean to: the balance markers show on it.</summary>
    public static bool Flies(Construct c) => c.StabCount > 0 || c.ThrusterCount > 0 || c.CellCount > 0 || c.LiftCentre(out _, out _);

    private static readonly string[] ModeNames = ["ALT", "HDG", "HOVER", "WPT", "HOME"];

    /// <summary>The look line's and the HUD's word on its autopilot.</summary>
    public static string ApText(Construct c)
    {
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
            return "";
        ref var p = ref c.Parts[c.Stabs[0].Part];
        return ConstructConfig.Get(p, p.D, CfgKey.Autopilot, 0) >= 0.5f ? "AP " + ModeNames[Math.Clamp((int)ConstructConfig.Get(p, p.D, CfgKey.ApMode, 0), 0, ModeNames.Length - 1)] : "";
    }

    private static readonly string[] Axes = ["forward", "up", "side", "pitch", "roll", "yaw"];

    /// <summary>Which axis this craft can't be flown on (the rows its keys need), by bit; 0: all of them.</summary>
    public static int Missing(Construct c, FlightEffectors f)
    {
        var need = 1 << FlightEffectors.Pitch | 1 << FlightEffectors.Roll | 1 << FlightEffectors.Yaw;
        if (c.CellCount > 0)
            need = 1 << FlightEffectors.Yaw; // (her gas keeps her up and level)
        else if (c.StabCount > 0 && KeysOf(c, c.Parts[c.Stabs[0].Part], c.Parts[c.Stabs[0].Part].D) is var keys)
            need |= keys == FlightPattern.Plane ? 1 << FlightEffectors.Forward : 1 << FlightEffectors.Up;
        return need & ~f.Can;
    }

    /// <summary>What a look at a stabilizer finds wrong, or null.</summary>
    public static string? Problem(Construct c, int part)
    {
        if (c.Parts[part].Broken)
            return "wrecked";
        if (IndexOf(c, part) != 0 || c.Effectors is not { } f)
            return "another Flight Stabilizer flies this construct";
        if (c.DriveSeat < 0 && ConstructConfig.Get(c.Parts[part], c.Parts[part].D, CfgKey.Autopilot, 0) < 0.5f && (c.Stabs[0].WiredIn & ~1) == 0)
            return "no seat or receiver on this construct";
        var miss = Missing(c, f);
        if (miss == 0)
            return null;
        var text = ConstructEffectors.Found(f) + ": can't control";
        for (var r = 0; r < Axes.Length; r++)
            if ((miss & 1 << r) != 0)
                text += " " + Axes[r];
        return text;
    }

    /// <summary>The look line's state of a stabilizer: what it found.</summary>
    public static string Look(Construct c, int part) => IndexOf(c, part) == 0 && c.Effectors is { } f ? $"{ConstructEffectors.Found(f)}, keys {KeysOf(c, c.Parts[part], c.Parts[part].D)}" : "";

    /// <summary>A Flight Control Panel: what the first stabilizer on the construct reads, and its settings. It works from the pose, so a client shows it as it sees it. No allocation.</summary>
    public static void FillPanel(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!LogicDisplays.Live(c, part, card))
            return;
        card.Plain = true;
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount || c.Effectors is not { } f)
        {
            card.State = PanelState.Wants;
            card.Add(0, "No Flight Stabilizer");
            return;
        }
        ref var s = ref c.Parts[c.Stabs[0].Part];
        var d = s.D;
        var com = c.WorldCom;
        var up = ConstructPhysics.HoldUp(com);
        var a = Read(c, s, up);
        var on = ConstructConfig.Get(s, d, CfgKey.Autopilot, 0) >= 0.5f;
        var mode = Math.Clamp((int)ConstructConfig.Get(s, d, CfgKey.ApMode, 0), 0, ModeNames.Length - 1);
        var miss = Missing(c, f);
        card.State = s.Broken ? PanelState.Fault : miss != 0 ? PanelState.Wants : on ? PanelState.Working : PanelState.Idle;
        card.Add(0, "ALT ").Add(0, (int)MathF.Round(ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(com)))).Add(0, " m  CLB ").AddFixed(0, Vector3.Dot(c.Vel, up), 1);
        card.Add(1, "SPD ").AddFixed(1, c.Vel.Length(), 1).Add(1, " m/s  HDG ").Add(1, ((int)MathF.Round(a.Heading * 180 / MathF.PI) + 360) % 360);
        card.Add(2, "PIT ").Add(2, (int)MathF.Round(a.Pitch * 180 / MathF.PI)).Add(2, "  ROL ").Add(2, (int)MathF.Round(a.Roll * 180 / MathF.PI));
        card.Add(3, on ? "AP ON  " : "AP OFF ").Add(3, ModeNames[mode]);
        var alt = ConstructConfig.Get(s, d, CfgKey.TargetAlt, 0);
        if (alt > 0)
            card.Add(3, ' ').Add(3, (int)alt).Add(3, " m");
        var level = ConstructConfig.Get(s, d, CfgKey.Level, 1) >= 0.5f;
        card.Add(4, level ? "LEVEL ON  STR " : "LEVEL OFF STR ").Add(4, (int)ConstructConfig.Get(s, d, CfgKey.Strength, 100)).Add(4, '%');
        AddMissing(card, 5, miss);
        card.TouchLabels[2] = ModeNames[(mode + 1) % ModeNames.Length];
        card.TouchLabels[4] = level ? "Level off" : "Level on";
    }

    private static void AddMissing(PanelCard card, int line, int miss)
    {
        if (miss == 0)
        {
            card.Add(line, "ALL AXES OK");
            return;
        }
        card.Add(line, "NO");
        for (var r = 0; r < Axes.Length; r++)
            if ((miss & 1 << r) != 0)
                card.Add(line, ' ').Add(line, Axes[r]);
    }

    /// <summary>A Flight Diagnostic Panel: the plot's data on the card (<see cref="PanelDraw.Flight"/>), the counts and what it can't do.</summary>
    public static void FillDiagnostic(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!LogicDisplays.Live(c, part, card))
            return;
        card.Plain = true;
        if (c.StabCount == 0 || c.Effectors is not { } f)
        {
            card.State = PanelState.Wants;
            card.Add(0, "No Flight Stabilizer");
            return;
        }
        var miss = Missing(c, f);
        card.State = miss != 0 ? PanelState.Wants : PanelState.Working;
        card.Draw = PanelDraw.Flight;
        card.Flight = f;
        card.FlightMin = c.LocalMin;
        card.FlightMax = c.LocalMax;
        card.FlightCom = c.Com;
        var sum = 0f; // (a card is laid out again only when what it shows changed)
        for (var k = 0; k < f.Count; k++)
            sum += (k + 1) * (f.Items[k].Cmd + (f.Items[k].Auth > 0 ? 3 : 0));
        card.Value = sum;
        card.Add(0, f.Thrusters + f.Repulsors + f.Rotors).Add(0, " push, ").Add(0, f.Gimbals).Add(0, " gimbal, ").Add(0, f.Surfaces).Add(0, " surf");
        AddMissing(card, 1, miss);
    }

    /// <summary>What it commands, to clients, four times a second while it is at work and a Flight Diagnostic Panel is on the construct: u16 body, u8 n, then (i8 command, u8 authority) each.</summary>
    public static bool WriteDiagnostic(PacketWriter w, Construct c)
    {
        if (!c.HasFlightDiag || !c.StabLive && !c.DiagSent || c.Effectors is not { Count: > 0 } f)
            return false;
        c.DiagSent = c.StabLive;
        var n = Math.Min(f.Count, 200);
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Flight).U16(c.Id).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U8((byte)(sbyte)Math.Clamp(MathF.Round(f.Items[k].Cmd * 127), -127, 127)).U8((byte)Math.Clamp(MathF.Round(f.Items[k].Auth * 200), 0, 255));
        return true;
    }

    public static void ReadDiagnostic(ref PacketReader r, ConstructWorld world)
    {
        var id = r.U16();
        var n = r.U8();
        var f = world.Get(id)?.Effectors;
        for (var k = 0; k < n; k++)
        {
            var cmd = (sbyte)r.U8();
            var auth = r.U8();
            if (f == null || k >= f.Count)
                continue;
            f.Items[k].Cmd = cmd / 127f;
            f.Items[k].Auth = auth / 200f;
        }
    }

    /// <summary>A button on a Flight Control Panel. Null, or why not.</summary>
    public static string? Touch(ConstructWorld world, Construct c, string op)
    {
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
            return "no Flight Stabilizer on this construct";
        var part = c.Stabs[0].Part;
        ref var p = ref c.Parts[part];
        var d = p.D;
        switch (op)
        {
            case "off":
                Put(ref p, d, CfgKey.Autopilot, 0);
                break;
            case "on":
                Put(ref p, d, CfgKey.Autopilot, 1);
                break;
            case "mode":
                Put(ref p, d, CfgKey.ApMode, ((int)ConstructConfig.Get(p, d, CfgKey.ApMode, 0) + 1) % ModeNames.Length);
                break;
            case "level":
                Put(ref p, d, CfgKey.Level, ConstructConfig.Get(p, d, CfgKey.Level, 1) >= 0.5f ? 0 : 1);
                break;
            case "strength":
                var now = ConstructConfig.Get(p, d, CfgKey.Strength, 100);
                Put(ref p, d, CfgKey.Strength, now >= 150 ? 50 : now + 50);
                break;
            case "altitude":
                Put(ref p, d, CfgKey.TargetAlt, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom)));
                break;
            case "heading":
                Put(ref p, d, CfgKey.TargetHeading, (Read(c, p, ConstructPhysics.HoldUp(c.WorldCom)).Heading * 180 / MathF.PI + 360) % 360);
                break;
            default:
                SetHome(world, c, part);
                break;
        }
        c.Wake();
        return null;
    }
}
