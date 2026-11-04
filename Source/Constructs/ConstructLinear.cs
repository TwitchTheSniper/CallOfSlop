using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One run of linear rail sections end to end (docs/sandbox/CONSTRUCTS.md, "Linear joints"): one carriage and one joint, derived on
/// every rebuild, on every peer. Nothing about it is saved or sent: the sections are ordinary parts and their links.
/// </summary>
public struct CRail
{
    public ushort Hinge, Drive; // the section with the carriage; the one that runs it (a powered section, or Hinge)
    public short Joint; // its joint, or -1 with nothing on the carriage
    public float Lo, Hi; // where the rail's ends are from the carriage's rest (m), along Hinge's axis
    public byte Sections;
    public bool Cut; // a wrecked section stands between this run and the rest of its rail
    public bool Busy; // something rides on the carriage
}

public sealed partial class Construct
{
    public const int MaxRailSections = 64; // sections a stack; a longer line is two rails
    public const float RailLineTolerance = 0.02f, RailLineDeg = 3f; // how far off a straight line two sections may sit (a freeform seam)
    public CRail[] Rails = []; // (made for the first construct that has one: almost none do)
    public int RailCount;
    public short[] RailOf = []; // per part: its run, or -1 (only with RailCount > 0)

    /// <summary>The run of a rail section, or -1.</summary>
    public int RailRun(int part) => RailCount > 0 && (uint)part < (uint)RailOf.Length && (uint)part < (uint)PartCount ? RailOf[part] : -1;

    /// <summary>The joint a part drives or carries: its own, or for a section of a stacked rail, the rail's.</summary>
    public int RailJointOf(int part)
    {
        var r = Parts[part].D.IsRail ? RailRun(part) : -1;
        return r >= 0 && r < RailCount ? Rails[r].Joint : JointOf(part);
    }

    /// <summary>Does another section of this part's rail carry the load? A rail has one carriage.</summary>
    public bool RailBusy(int part)
    {
        var r = (uint)part < (uint)PartCount && Parts[part].D.IsRail ? RailRun(part) : -1;
        return r >= 0 && r < RailCount && Rails[r].Busy && Rails[r].Hinge != part;
    }

    /// <summary>Does this section draw the carriage (each run shows one)?</summary>
    public bool RailShowsCarriage(int part)
    {
        var r = RailRun(part);
        return r < 0 || r >= RailCount || Rails[r].Hinge == part;
    }

    /// <summary>The sections end to end, and each run's limits, its joint and who runs it. Before anything reads a joint's limits.</summary>
    private void BuildRails()
    {
        RailCount = 0;
        for (var j = 0; j < JointCount; j++)
            Joints[j].Rail = false;
        var any = false;
        for (var i = 0; i < PartCount && !any; i++)
            any = Parts[i].D.IsRail;
        if (!any)
            return;
        if (RailOf.Length < PartCount)
            RailOf = new short[Math.Max(PartCount, Parts.Length)];
        Array.Fill(RailOf, (short)-1);
        Span<ushort> chain = stackalloc ushort[MaxRailSections];
        for (var i = 0; i < PartCount; i++)
        {
            if (!Parts[i].D.IsRail || RailOf[i] >= 0)
                continue;
            // Back to one end of the line, then along it.
            var cut = false;
            var at = i;
            var end = 0;
            for (var guard = 0; guard < MaxRailSections; guard++)
            {
                var n = RailNext(at, end, out var theirs, ref cut);
                if (n < 0 || n == i || RailOf[n] >= 0)
                    break;
                at = n;
                end = 1 - theirs;
            }
            end = 1 - end;
            var count = 0;
            chain[count++] = (ushort)at;
            for (var guard = 1; guard < MaxRailSections; guard++)
            {
                var n = RailNext(at, end, out var theirs, ref cut);
                if (n < 0 || chain[..count].Contains((ushort)n) || RailOf[n] >= 0)
                    break;
                chain[count++] = (ushort)n;
                at = n;
                end = 1 - theirs;
            }
            // One load a run: two loaded carriages (rails with loads pushed together) stay two runs, split at the second.
            var k = 0;
            while (k < count)
            {
                var start = k;
                var joint = -1;
                for (; k < count; k++)
                {
                    var j = JointOf(chain[k]);
                    if (j < 0 || !SegCarries(j + 1))
                        continue;
                    if (joint >= 0)
                        break;
                    joint = j;
                }
                AddRun(chain[start..k], joint, cut);
            }
        }
    }

    /// <summary>The section joined to <paramref name="at"/>'s end <paramref name="end"/> (0, 1) in line, or -1; <paramref name="cut"/> when that one is wrecked.</summary>
    private int RailNext(int at, int end, out int theirEnd, ref bool cut)
    {
        theirEnd = 0;
        ref var p = ref Parts[at];
        if (p.Broken)
            return -1;
        var socket = end == 0 ? p.D.RailEnd0 : p.D.RailEnd1;
        for (var e = FirstLink(at); e >= 0; e = NextLink(e))
        {
            ref var l = ref Links[e >> 1];
            var mine = (e & 1) == 0 ? l.SA : l.SB;
            if (mine != socket)
                continue;
            return RailJoined(at, OtherEnd(e), (e & 1) == 0 ? l.SB : l.SA, out theirEnd, ref cut);
        }
        for (var k = 0; k < SeamCount; k++)
        {
            ref var l = ref SeamLinks[k];
            if (l.A == at && l.SA == socket)
                return RailJoined(at, l.B, l.SB, out theirEnd, ref cut);
            if (l.B == at && l.SB == socket)
                return RailJoined(at, l.A, l.SA, out theirEnd, ref cut);
        }
        return -1;
    }

    /// <summary>Does part <paramref name="other"/>'s socket <paramref name="theirs"/> carry on <paramref name="at"/>'s rail: same gauge and class, in line, on the same moving piece?</summary>
    private int RailJoined(int at, int other, int theirs, out int theirEnd, ref bool cut)
    {
        theirEnd = 0;
        if ((uint)other >= (uint)PartCount)
            return -1;
        ConstructPartDef a = Parts[at].D, b = Parts[other].D;
        if (!b.IsRail || b.RailGauge != a.RailGauge || b.Class != a.Class || MoveSeg(Parts[at].Seg) != MoveSeg(Parts[other].Seg))
            return -1;
        if (theirs != b.RailEnd0 && theirs != b.RailEnd1)
            return -1;
        theirEnd = theirs == b.RailEnd0 ? 0 : 1;
        var axis = RailAxis(at);
        if (MathF.Abs(Vector3.Dot(axis, RailAxis(other))) < MathF.Cos(RailLineDeg * MathF.PI / 180f))
            return -1;
        var off = RailCentre(other) - RailCentre(at);
        if ((off - axis * Vector3.Dot(off, axis)).LengthSquared() > RailLineTolerance * RailLineTolerance)
            return -1;
        if (Parts[other].Broken)
        {
            cut = true;
            return -1;
        }
        return other;
    }

    private Vector3 RailAxis(int part) => DirToRest(Parts[part].Seg, ConstructParts.DirVector(Rot24.ApplyDir(Parts[part].Rot, Parts[part].D.JointAxis)));

    private Vector3 RailCentre(int part) => ToRest(Parts[part].Seg, Parts[part].Pos);

    private void AddRun(ReadOnlySpan<ushort> sections, int joint, bool cut)
    {
        if (RailCount == Rails.Length)
            Array.Resize(ref Rails, Math.Max(4, RailCount * 2));
        var hinge = joint >= 0 ? Joints[joint].Hinge : sections[0];
        var drive = hinge;
        foreach (var s in sections)
            if (Parts[drive].D.RailDrive == RailDriveKind.Free && Parts[s].D.RailDrive != RailDriveKind.Free)
                drive = s;
        if (joint < 0)
            hinge = drive; // (nothing on it yet: the carriage shows on the powered section)
        var axis = RailAxis(hinge);
        var origin = RailCentre(hinge);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var s in sections)
        {
            var d = Parts[s].D;
            var at = Vector3.Dot(RailCentre(s) - origin, axis);
            var sense = Vector3.Dot(RailAxis(s), axis) >= 0 ? 1f : -1f;
            lo = MathF.Min(lo, at + MathF.Min(d.JointMin * sense, d.JointMax * sense));
            hi = MathF.Max(hi, at + MathF.Max(d.JointMin * sense, d.JointMax * sense));
            RailOf[s] = (short)RailCount;
        }
        Rails[RailCount++] = new CRail { Hinge = hinge, Drive = drive, Joint = (short)joint, Lo = lo, Hi = hi, Sections = (byte)sections.Length, Cut = cut, Busy = joint >= 0 };
        if (joint < 0)
            return;
        ref var jt = ref Joints[joint];
        ref var dp = ref Parts[drive];
        var dd = dp.D;
        var from = lo + (hi - lo) * Math.Clamp(ConstructConfig.Get(dp, dd, CfgKey.StopLow, 0) / 100f, 0, 1);
        var to = lo + (hi - lo) * Math.Clamp(ConstructConfig.Get(dp, dd, CfgKey.StopHigh, 100) / 100f, 0, 1);
        jt.Rail = true;
        jt.Drive = drive;
        jt.Type = dd.JointType;
        jt.Min = MathF.Min(from, to);
        jt.Max = MathF.Max(MathF.Max(from, to), jt.Min + 0.02f);
        jt.ShaftRail = dd.RailDrive == RailDriveKind.Shaft;
        jt.SpeedMode = ConstructConfig.Get(dp, dd, CfgKey.SetMode, 0) >= 0.5f;
        jt.RestAt = Math.Clamp(ConstructConfig.Get(dp, dd, CfgKey.Rest, 50) / 100f, 0, 1);
        jt.Lead = MathF.Max(1e-3f, ConstructConfig.Get(dp, dd, CfgKey.Lead, dd.RailLead));
    }
}

/// <summary>
/// Linear joints (docs/sandbox/CONSTRUCTS.md, "Linear joints"): a rail and a carriage. A section is a joint part whose leaf is its
/// carriage, resting at the section's middle: a joint's number is the carriage's distance from there, so it runs below 0 as
/// well as above, and a stack only moves the joint's limits. Sections end to end make one run with one carriage and one joint
/// (<see cref="Construct.BuildRails"/>). Three kinds: a Free Slider (<see cref="JointType.Slider"/>: no motor), a Linear
/// Actuator (a piston's motor and strength model, its position or speed set by a signal) and a Shaft Linear Drive (the same
/// motor, turned by a drive shaft through a lead screw, ConstructDrillDrive.cs's feed). Nothing here allocates per tick.
/// </summary>
public static class ConstructLinear
{
    public const float SliderDrag = 0.8f; // a second: what a free carriage's rollers take off its speed
    public const float PastStopSpeed = 2f; // m/s a carriage runs back when the rail under it came off
    public const float WireReach = 30f; // metres: a sliding joint further out than a pose's millimetres reach goes in centimetres (protocol 135)
    public const float ScrewEfficiency = 0.9f;

    /// <summary>A joint block's "rail": {"gauge": cells across, "drive": "free", "electric" or "shaft", "lead": metres a turn}. The sockets "end-" and "end+" are what a stack joins by.</summary>
    public static void ParseRail(ConstructPartDef d, JsonElement block, string file, string where)
    {
        if (ConstructParts.Opt(block, "rail") is not { } r)
        {
            if (d.JointType == JointType.Slider)
                throw new ConstructPartException($"{file}: a slider is a rail: it needs a \"rail\" block");
            return;
        }
        ConstructParts.Known(r, ["gauge", "drive", "lead"], $"{where}.rail");
        d.RailDrive = ConstructParts.Str(ConstructParts.Need(r, "drive", $"{where}.rail"), file, "rail.drive").ToLowerInvariant() switch
        {
            "free" => RailDriveKind.Free,
            "electric" => RailDriveKind.Electric,
            "shaft" => RailDriveKind.Shaft,
            _ => throw new ConstructPartException($"{file}: \"rail.drive\" is free, electric or shaft"),
        };
        if (d.RailDrive == RailDriveKind.Free != (d.JointType == JointType.Slider) || d.JointType is not (JointType.Slider or JointType.Piston))
            throw new ConstructPartException($"{file}: a free rail is a \"slider\" joint, a driven one a \"piston\"");
        d.RailGauge = (int)ConstructParts.Num(ConstructParts.Need(r, "gauge", $"{where}.rail"), file, "rail.gauge");
        ConstructParts.Range(d.RailGauge, 1, 16, file, "rail.gauge");
        d.RailLead = ConstructParts.Opt(r, "lead") is { } ld ? ConstructParts.Num(ld, file, "rail.lead") : 0;
        if (d.RailDrive == RailDriveKind.Shaft)
            ConstructParts.Range(d.RailLead, 0.001f, 20, file, "rail.lead");
        d.RailEnd0 = Array.FindIndex(d.Sockets, s => s.Name == "end-");
        d.RailEnd1 = Array.FindIndex(d.Sockets, s => s.Name == "end+");
        if (d.RailEnd0 < 0 || d.RailEnd1 < 0)
            throw new ConstructPartException($"{file}: a rail has \"end-\" and \"end+\" sockets");
        d.DataIn = d.RailDrive switch
        {
            RailDriveKind.Free => ["Lock"],
            RailDriveKind.Electric => ["Set", "Hold"],
            _ => ["Set"],
        };
    }

    /// <summary>A pose's units a metre or radian for this joint (a joint can only be told apart by what both peers rebuilt).</summary>
    public static float PoseScale(in CJoint jt) => jt.Slides && MathF.Max(MathF.Abs(jt.Min), MathF.Abs(jt.Max)) > WireReach ? 100f : 1000f;

    /// <summary>The part whose settings run this section's rail (itself, when it is no section of a stack).</summary>
    public static int Master(Construct c, int part)
    {
        var r = (uint)part < (uint)c.PartCount && c.Parts[part].D.IsRail ? c.RailRun(part) : -1;
        return r >= 0 && r < c.RailCount ? c.Rails[r].Drive : part;
    }

    /// <summary>Where an Electric rail is sent (metres from its rest): the Set signal's share of its travel, or unwired its rest, or the far end after F.</summary>
    public static float Target(in CJoint jt)
    {
        var at = jt.Wired ? Math.Clamp(jt.Input, 0, 1) : jt.Open ? jt.RestAt >= 0.75f ? 0f : 1f : jt.RestAt;
        return jt.Min + (jt.Max - jt.Min) * at;
    }

    /// <summary>A rail run by speed this tick: -1..1 of <paramref name="speed"/>, which is set here (a shaft's comes from the engine, and so does <paramref name="rating"/>'s cap while it moves). 0 holds.</summary>
    public static float Turn(Construct c, ref CJoint jt, ref float speed, ref float rating)
    {
        if (jt.Hold)
        {
            speed = jt.Speed;
            return 0;
        }
        if (!jt.ShaftRail)
            return Math.Clamp(jt.Input, -1, 1);
        var sense = Sense(c, jt);
        speed = ShaftSpeed(c, jt, out var through);
        if (sense == 0 || speed <= 0 || sense > 0 && jt.Angle >= jt.Max - 1e-3f || sense < 0 && jt.Angle <= jt.Min + 1e-3f)
            return 0;
        var e = c.EngineDef;
        rating = MathF.Min(rating, MathF.Tau * e.EngineTorque * through / jt.Lead);
        return sense;
    }

    /// <summary>The speed a rail run by speed wants (0 at the stop it is pushing at): what keeps a body awake.</summary>
    public static float Want(Construct c, in CJoint jt)
    {
        if (jt.Hold)
            return 0;
        var sense = jt.ShaftRail ? Sense(c, jt) : Math.Clamp(jt.Input, -1, 1);
        if (sense > 0 && jt.Angle >= jt.Max - 1e-3f || sense < 0 && jt.Angle <= jt.Min + 1e-3f)
            return 0;
        var speed = jt.Speed;
        if (jt.ShaftRail)
        {
            speed = ShaftOf(c, jt.Drive, out _, out _) == ShaftState.Turning ? MathF.Max(ShaftSpeed(c, jt, out _), 0.1f) : 0;
            if (speed == 0 && c.Asleep && ConstructPhysics.DriveOn(c))
                speed = 0.1f; // (an engine started on a sleeping body: it has to turn over)
        }
        return sense * speed;
    }

    /// <summary>Which way the shaft sends it: the reverse gear or a reverse throttle back; a wire's sign and share, or unwired, F.</summary>
    private static float Sense(Construct c, in CJoint jt)
    {
        var way = jt.Wired ? Math.Clamp(jt.Input, -1, 1) : jt.Open ? -1f : 1f;
        return c.GearRatio < 0 || ConstructPhysics.DriveThrottle(c) < 0 ? -way : way;
    }

    /// <summary>How the shaft into a section stands, as a drill's does; the shaft's rpm, and the turning force's gain through the line (a lower gear, more of it).</summary>
    public static ShaftState ShaftOf(Construct c, int part, out float rpm, out float through)
    {
        rpm = 0;
        through = 1;
        var linked = ConstructDrillDrive.Linked(c, part, out var cut);
        if (!linked)
            return ShaftState.None;
        var k = cut ? -1 : ConstructDrillDrive.FeedOf(c, part);
        if (cut)
            return ShaftState.Cut;
        if (k < 0)
            return ShaftState.NoEngine;
        ref var f = ref c.ShaftFeeds![k];
        if (!ConstructPhysics.DriveOn(c))
            return ShaftState.EngineOff;
        if ((f.GateMask & ~c.GateOpen) != 0)
            return ShaftState.ClutchOut;
        var gear = f.Geared ? MathF.Max(0.01f, MathF.Abs(c.GearRatio)) : 1f;
        rpm = c.Rpm / gear / MathF.Max(0.01f, f.Ratio);
        through = MathF.Max(0.01f, f.Ratio * f.Eff) * (f.Geared ? gear * MathF.Max(0.01f, c.TransEff) : 1f) * ScrewEfficiency;
        return rpm < 1 ? ShaftState.EngineOff : ShaftState.Turning;
    }

    /// <summary>Metres a second the lead screw turns at, up to the part's top speed, down with an engine pulled below its load.</summary>
    private static float ShaftSpeed(Construct c, in CJoint jt, out float through)
    {
        var state = ShaftOf(c, jt.Drive, out var rpm, out through);
        return state == ShaftState.Turning ? MathF.Min(jt.Speed, rpm / 60f * jt.Lead) * c.ShaftShare : 0;
    }

    /// <summary>What a shaft rail pushing with <paramref name="force"/> N takes at the crank (N m): only while it moves (a lead screw holds a load by itself).</summary>
    public static float CrankLoad(Construct c, in CJoint jt, float force)
    {
        if (MathF.Abs(jt.Vel) < 0.01f || ShaftOf(c, jt.Drive, out _, out var through) != ShaftState.Turning)
            return 0;
        return force * jt.Lead / MathF.Tau / through;
    }

    /// <summary>A carriage out past its stop (a section came off, a stop was moved): it runs back at a walk. True while it does.</summary>
    public static bool PastStop(ref CJoint jt, float dt)
    {
        var past = jt.Angle > jt.Max + 0.01f ? jt.Angle - jt.Max : jt.Angle < jt.Min - 0.01f ? jt.Angle - jt.Min : 0;
        if (past == 0)
            return false;
        jt.Vel = 0;
        jt.Angle -= MathF.CopySign(MathF.Min(MathF.Abs(past), PastStopSpeed * dt), past);
        return true;
    }

    /// <summary>What the look line changes with (where the carriage is, the run, the shaft).</summary>
    public static long LookKey(Construct c, int part)
    {
        var r = c.RailRun(part);
        if (r < 0 || r >= c.RailCount)
            return 0;
        ref var run = ref c.Rails[r];
        var pos = run.Joint >= 0 ? (long)(c.Joints[run.Joint].Angle * 10) : 0;
        return pos ^ (long)run.Sections << 12 ^ (run.Cut ? 1L << 18 : 0) ^ (long)c.Rpm / 100 << 20 ^ (long)ConstructPhysics.DriveThrottle(c) << 30;
    }

    /// <summary>The look line: its rail, where the carriage is, and why it won't run.</summary>
    public static string Look(Construct c, int part, bool near)
    {
        var d = c.DefOf(part);
        ref var p = ref c.Parts[part];
        var r = c.RailRun(part);
        if (r < 0 || r >= c.RailCount)
            return SandboxKeys.Line(d.Name, p.Broken ? "wrecked" : null);
        ref var run = ref c.Rails[r];
        var travel = run.Hi - run.Lo;
        var state = run.Sections > 1 ? $"{run.Sections} sections, {travel:0.#} m" : $"{travel:0.#} m";
        string? warn = null;
        string? action = null;
        var dd = c.DefOf(run.Drive);
        if (run.Joint >= 0)
        {
            ref var jt = ref c.Joints[run.Joint];
            state += $", carriage at {jt.Angle - run.Lo:0.0}";
            if (jt.Locked)
                state += ", locked";
            else if (jt.Hold)
                state += ", held";
            if (jt.Over > 1)
                warn = "overloaded";
            if (dd.RailDrive == RailDriveKind.Electric && !jt.Wired || dd.RailDrive == RailDriveKind.Shaft && !jt.Wired)
                action = near ? SandboxKeys.Do(SandboxKeys.Use, dd.RailDrive == RailDriveKind.Shaft ? "reverse it" : "send it along") : null;
        }
        else if (run.Sections > 1 && run.Hinge != part)
            state += ", carriage on another section";
        if (p.Broken)
            warn = "wrecked";
        else if (run.Cut)
            warn = "a wrecked section cuts this rail short";
        else if (dd.RailDrive == RailDriveKind.Electric && c.Electrical && !ConstructPower.On(c, run.Drive))
            warn = "no power";
        else if (dd.RailDrive == RailDriveKind.Shaft)
            warn = ShaftOf(c, run.Drive, out _, out _) switch
            {
                ShaftState.None => "needs a drive shaft into its Drive in",
                ShaftState.Cut => "its drive shaft is cut",
                ShaftState.NoEngine => "its drive shaft doesn't reach a working engine",
                ShaftState.EngineOff => "held: the engine on its shaft isn't running",
                ShaftState.ClutchOut => "held: the clutch on its shaft is out",
                _ => warn,
            };
        return SandboxKeys.Line(d.Name, state, warn, action);
    }
}
