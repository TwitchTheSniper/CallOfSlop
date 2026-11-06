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

    /// <summary>What a look at a stabilizer finds wrong, or null.</summary>
    public static string? Problem(Construct c, int part)
    {
        if (c.Parts[part].Broken)
            return "wrecked";
        if (!Reaches(c, part))
            return "no outputs linked: link them to thrusters, flaps or gimbals";
        return ConstructModules.SeatOf(c, part) < 0 && (IndexOf(c, part) is var k and >= 0 && (c.Stabs[k].WiredIn & ~1) == 0) && ConstructConfig.Get(c.Parts[part], c.Parts[part].D, CfgKey.Autopilot, 0) < 0.5f ? "no seat or receiver linked into it" : null;
    }

    /// <summary>A Flight Panel: what the first stabilizer on the construct reads. It works from the pose, so a client shows it as it sees it. No allocation.</summary>
    public static void FillPanel(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (!LogicDisplays.Live(c, part, card))
            return;
        card.Plain = true;
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
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
        card.State = s.Broken ? PanelState.Fault : on ? PanelState.Working : PanelState.Idle;
        card.Add(0, "ALT ").Add(0, (int)MathF.Round(ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(com)))).Add(0, " m  CLB ").AddFixed(0, Vector3.Dot(c.Vel, up), 1);
        card.Add(1, "SPD ").AddFixed(1, c.Vel.Length(), 1).Add(1, " m/s");
        card.Add(2, "HDG ").Add(2, ((int)MathF.Round(a.Heading * 180 / MathF.PI) + 360) % 360);
        card.Add(3, "PIT ").Add(3, (int)MathF.Round(a.Pitch * 180 / MathF.PI)).Add(3, "  ROL ").Add(3, (int)MathF.Round(a.Roll * 180 / MathF.PI));
        card.Add(4, on ? "AP ON  " : "AP OFF ").Add(4, ModeNames[mode]);
        var alt = ConstructConfig.Get(s, d, CfgKey.TargetAlt, 0);
        if (alt > 0)
            card.Add(4, ' ').Add(4, (int)alt).Add(4, " m");
        card.TouchLabels[0] = "AP on";
        card.TouchLabels[1] = "AP off";
        card.TouchLabels[2] = ModeNames[(mode + 1) % ModeNames.Length];
    }

    /// <summary>A button on a Flight Panel: 0 off, 1 on, 2 the next mode, 3 home is here. Null, or why not.</summary>
    public static string? Touch(ConstructWorld world, Construct c, int op)
    {
        if (c.StabCount == 0 || c.Stabs[0].Part >= c.PartCount)
            return "no Flight Stabilizer on this construct";
        var part = c.Stabs[0].Part;
        ref var p = ref c.Parts[part];
        var d = p.D;
        switch (op)
        {
            case 0:
                Put(ref p, d, CfgKey.Autopilot, 0);
                break;
            case 1:
                Put(ref p, d, CfgKey.Autopilot, 1);
                break;
            case 2:
                Put(ref p, d, CfgKey.ApMode, ((int)ConstructConfig.Get(p, d, CfgKey.ApMode, 0) + 1) % ModeNames.Length);
                break;
            default:
                SetHome(world, c, part);
                break;
        }
        c.Wake();
        return null;
    }
}
