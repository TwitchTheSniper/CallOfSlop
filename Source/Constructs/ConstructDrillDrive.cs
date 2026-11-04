using System.Numerics;

namespace CallOfSlop;

/// <summary>How the drive shaft into a drill stands (on the wire: a store's <see cref="CStore.Drive"/>).</summary>
public enum ShaftState : byte
{
    None, // no shaft linked: it runs on electric power
    Turning, // the engine turns it
    EngineOff, // the engine on the line isn't running
    ClutchOut, // a clutch (or a transfer case) on the line is out
    Cut, // a shaft link into it is cut
    NoEngine, // the line doesn't reach a working engine
    COUNT,
}

/// <summary>One drill on an engine's drive line, found with the drive line (<see cref="Construct.Rebuild"/>).</summary>
public struct CShaftFeed
{
    public ushort Part;
    public bool Geared; // through the transmission: its gear counts
    public ushort GateMask; // the gates on the way (ConstructDrive.Gates)
    public float Ratio, Eff; // the reduction and what is left, after the gearbox
}

public sealed partial class Construct
{
    public const int MaxShaftFeeds = 16; // drills on one construct's drive line; more have no drive
    public CShaftFeed[]? ShaftFeeds; // made for the first construct that needs it: almost none do
    public int ShaftFeedCount;
    public float ShaftLoad; // N m at the crank that working drills take (server, set at 10 Hz by the gather step)
    public float RailLoad; // N m at the crank that shaft rails take (server, set each tick: ConstructLinear.cs)
    public float ShaftShare = 1; // the share of that load the engine can carry this tick (server)

    /// <summary>The drills the engine reaches, through the transmission and straight from its own Drive out.</summary>
    private void BuildShaftFeeds(Span<CLink> edges, Span<float> edgeEff)
    {
        var any = false;
        for (var i = 0; i < PartCount && !any; i++)
            any = Parts[i].D.ShaftIn;
        if (!any)
            return;
        if (Transmission >= 0)
            FeedShafts(Transmission, true, edges, edgeEff);
        FeedShafts(Engine, false, edges, edgeEff);
        if (ShaftFeedCount > 0)
            HasPowertrain = true; // (an engine with a drill on it turns with no gearbox: its rpm is worked out each tick)
    }

    /// <summary>
    /// Breadth first from <paramref name="start"/> along the drive parts, each through its ports as the wheels' drive line
    /// goes (the part that starts it through its shaft output), to the drills' Drive in. Joints stop it: a shaft can't cross
    /// one. A power take-off's output counts. Each drill keeps the first way that reached it.
    /// </summary>
    private void FeedShafts(int start, bool geared, Span<CLink> edges, Span<float> edgeEff)
    {
        Span<bool> fed = stackalloc bool[PartCount];
        Span<float> ratio = stackalloc float[PartCount];
        Span<float> eff = stackalloc float[PartCount];
        Span<ushort> mask = stackalloc ushort[PartCount];
        Span<ushort> queue = stackalloc ushort[PartCount];
        int head = 0, tail = 0;
        queue[tail++] = (ushort)start;
        fed[start] = true;
        ratio[start] = 1;
        eff[start] = 1;
        if (Transmission >= 0)
            fed[Transmission] = true; // (from the engine: what is past the gearbox was reached from the gearbox)
        while (head < tail)
        {
            int at = queue[head++];
            var da = Parts[at].D;
            var transfer = da.Role == PartRole.Transfer;
            if (transfer && da.Drive is DriveKind.Hub or DriveKind.Stub)
                continue;
            var outRatio = ratio[at] * (transfer ? da.DriveRatio : 1f) * (da.Role == PartRole.Differential ? da.DiffRatio : 1f);
            var outEff = eff[at] * (transfer ? da.DriveEfficiency : 1f) * (da.Role == PartRole.Differential ? da.DiffEfficiency : 1f);
            var outMask = mask[at];
            if (transfer && da.Drive is DriveKind.Clutch && GateCount < MaxGates)
                outMask |= AddGate(at, 0, -1, 0);
            for (var k = 0; k < edges.Length; k++)
            {
                ref var l = ref edges[k];
                int other, sa, sb;
                if (l.A == at) (other, sa, sb) = (l.B, l.SA, l.SB);
                else if (l.B == at) (other, sa, sb) = (l.A, l.SB, l.SA);
                else continue;
                if (fed[other] || at == start && ConstructRoutes.PortOf(da, RouteType.Shaft, (byte)sa) is not { Out: true })
                    continue;
                if (transfer && da.Ports.Length > 0 && Array.IndexOf(da.Ports, sa) < 0)
                    continue;
                var dob = Parts[other].D;
                if (dob.ShaftIn)
                {
                    if (sb == dob.DriveInSocket)
                    {
                        var m = outMask;
                        if (transfer && da.Drive == DriveKind.TransferCase && sa == da.FrontPort && GateCount < MaxGates)
                            m |= AddGate(at, 1, -1, 0);
                        AddFeed(other, geared, m, outRatio, outEff * edgeEff[k]);
                    }
                    continue;
                }
                if (dob.Role is not (PartRole.Transfer or PartRole.Differential) || !Parts[other].Working)
                    continue;
                if (dob.Role == PartRole.Transfer && dob.Ports.Length > 0 && Array.IndexOf(dob.Ports, sb) < 0)
                    continue;
                fed[other] = true;
                ratio[other] = outRatio;
                eff[other] = outEff * edgeEff[k];
                mask[other] = outMask;
                queue[tail++] = (ushort)other;
            }
        }
    }

    private void AddFeed(int part, bool geared, ushort mask, float ratio, float eff)
    {
        ShaftFeeds ??= new CShaftFeed[MaxShaftFeeds];
        if (ShaftFeedCount >= MaxShaftFeeds)
            return;
        for (var k = 0; k < ShaftFeedCount; k++)
            if (ShaftFeeds[k].Part == part)
                return;
        ShaftFeeds[ShaftFeedCount++] = new CShaftFeed { Part = (ushort)part, Geared = geared, GateMask = mask, Ratio = ratio, Eff = eff };
    }
}

/// <summary>
/// A drill on a drive shaft (docs/sandbox/CONSTRUCTS.md, "Gathering"): two ways to run a Drill Module. With no shaft linked
/// it runs on electric power, as it always did. With a shaft linked into its Drive in (a shaft link, or a drive part bolted
/// to that socket) the shaft alone runs it, and the rule is the part's own link: a cut, idle or disconnected shaft stops it
/// with a reason, even with a battery there, and it never draws power. Its rate goes with the shaft's speed up to the
/// tier's rated rpm. Working, it takes its torque off the engine: less for the wheels, more fuel, and an engine that can't
/// carry it is pulled down and drills slower. Never gated on a seat or a free body: an anchored base runs it. Server at
/// 10 Hz; nothing allocates.
/// </summary>
public static class ConstructDrillDrive
{
    public const float IdleTorque = 0.25f; // the share of its torque a throttled-back engine still gives a load (an engine doesn't stall at idle under a small one)
    public const float MinCarry = 0.05f; // the least share of a load an overloaded engine carries: it lugs, it doesn't die
    private const int RpmStep = 10; // a store's rpm is rounded to this, so a small wobble sends nothing

    /// <summary>Is a drive shaft linked into this drill (a shaft link, cut or not, or a drive part on its Drive in socket)?</summary>
    public static bool Linked(Construct c, int part, out bool cut)
    {
        cut = false;
        var linked = false;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Shaft && r.To == part)
            {
                linked = true;
                cut |= !r.FedEnd;
            }
        }
        if (linked)
            return true;
        var s = c.Parts[part].D.DriveInSocket;
        for (var k = 0; k < c.LinkCount + c.SeamCount; k++)
        {
            ref var l = ref k < c.LinkCount ? ref c.Links[k] : ref c.SeamLinks[k - c.LinkCount];
            var other = l.A == part && l.SA == s ? l.B : l.B == part && l.SB == s ? l.A : -1;
            if (other >= 0 && other < c.PartCount && c.Parts[other].D.Role is PartRole.Engine or PartRole.Transmission or PartRole.Transfer or PartRole.Differential)
                return true;
        }
        return false;
    }

    /// <summary>The drive line's entry for a drill, or -1.</summary>
    internal static int FeedOf(Construct c, int part)
    {
        for (var k = 0; k < c.ShaftFeedCount; k++)
            if (c.ShaftFeeds![k].Part == part)
                return k;
        return -1;
    }

    /// <summary>
    /// One gather step for module <paramref name="part"/>: how its shaft stands, on its store. The share of its rate the
    /// shaft gives, 0 to 1, or -1 with no shaft linked (a saw, or a drill on electric power).
    /// </summary>
    public static float Update(ConstructWorld world, Construct c, int part, CStore st)
    {
        var d = c.Parts[part].D;
        if (d.GatherRpm <= 0 || !Linked(c, part, out var cut))
        {
            Set(world, st, ShaftState.None, 0, 0);
            return -1;
        }
        var feed = cut ? -1 : FeedOf(c, part);
        var state = cut ? ShaftState.Cut : feed < 0 ? ShaftState.NoEngine : ShaftState.Turning;
        var rpm = 0f;
        if (feed >= 0)
        {
            ref var f = ref c.ShaftFeeds![feed];
            if (!ConstructPhysics.DriveOn(c))
                state = ShaftState.EngineOff;
            else if ((f.GateMask & ~c.GateOpen) != 0)
                state = ShaftState.ClutchOut;
            else
            {
                rpm = c.Rpm / (f.Geared ? MathF.Max(0.01f, MathF.Abs(c.GearRatio)) : 1f) / MathF.Max(0.01f, f.Ratio);
                if (rpm < 1)
                {
                    if (c.Asleep)
                        c.Wake(); // (an engine started on a sleeping free body: it has to turn over)
                    state = ShaftState.EngineOff;
                    rpm = 0;
                }
            }
        }
        var share = state == ShaftState.Turning ? MathF.Min(1, rpm / d.GatherRpm) * c.ShaftShare : 0;
        Set(world, st, state, (int)(rpm / RpmStep + 0.5f) * RpmStep, (int)(share * 100 + 0.5f));
        return share;
    }

    /// <summary>What a working drill takes at the crank, N m, by its reduction: a lower gear asks less of the engine.</summary>
    public static float Load(Construct c, int part)
    {
        var k = FeedOf(c, part);
        if (k < 0)
            return 0;
        ref var f = ref c.ShaftFeeds![k];
        var through = MathF.Max(0.01f, f.Ratio * f.Eff) * (f.Geared ? MathF.Max(0.01f, MathF.Abs(c.GearRatio) * c.TransEff) : 1f);
        return c.Parts[part].D.GatherTorque / through;
    }

    /// <summary>The torque the drills take off the engine this tick at <paramref name="rpm"/> (<see cref="Construct.ShaftShare"/>: how much of it the engine can carry).</summary>
    public static float Carry(Construct c, ConstructPartDef e, float rpm, float amount)
    {
        var load = c.ShaftLoad + c.RailLoad;
        if (load <= 0)
        {
            c.ShaftShare = 1;
            return 0;
        }
        var can = e.EngineTorque * ConstructDrive.Curve(e, rpm) * MathF.Max(amount, IdleTorque);
        c.ShaftShare = can >= load ? 1 : MathF.Max(MinCarry, can / load);
        return MathF.Min(load, can);
    }

    /// <summary>One tick of an engine with drills on it and no gearbox: the rpm follows the throttle, with no wheels to hold it.</summary>
    public static void BareEngine(Construct c, ConstructPartDef e, bool driving, float throttle, float dt)
    {
        c.GearRatio = 1;
        c.Gear = 0;
        if (!driving)
        {
            c.Rpm = 0;
            c.Spool = MathF.Max(0, c.Spool - dt);
            c.ShaftShare = 1;
            return;
        }
        var demand = MathF.Abs(throttle);
        var want = (e.IdleRpm + demand * (e.MaxRpm * 0.95f - e.IdleRpm)) * c.ShaftShare;
        c.Rpm += Math.Clamp(want - c.Rpm, -e.MaxRpm * 2 * dt, e.MaxRpm * 2 * dt);
        var amount = demand;
        if (e.SpoolSeconds > 0)
        {
            var rate = dt / e.SpoolSeconds;
            c.Spool = Math.Clamp(c.Spool + Math.Clamp(demand - c.Spool, -2 * rate, rate), 0, 1);
            amount = c.Spool;
            if (demand > 0 && c.Spool < 0.9f * demand)
                c.Note = DriveNote.Spooling;
        }
        var torque = e.EngineTorque * ConstructDrive.Curve(e, c.Rpm) * amount;
        var drills = Carry(c, e, c.Rpm, amount);
        ConstructDrive.Burn(c, e, c.Rpm, demand, ref torque, drills, dt);
        c.DriveTorque = MathF.Max(0, torque - drills);
        c.EngineLoad = MathF.Max(c.EngineLoad, MathF.Min(1f, c.EngineLoad + 6f * dt) * (torque > 0 ? amount : 0f)); // (the smoke, as ConstructDrive.Tick has it)
    }

    private static void Set(ConstructWorld world, CStore st, ShaftState state, int rpm, int pct)
    {
        rpm = Math.Clamp(rpm, 0, ushort.MaxValue);
        if (st.Drive == state && st.DriveRpm == rpm && st.DrivePct == pct)
            return;
        st.Drive = state;
        st.DriveRpm = (ushort)rpm;
        st.DrivePct = (byte)Math.Clamp(pct, 0, 100);
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>The look line of a drill at work (from the store, the same on every peer).</summary>
    public static string BusyLook(ConstructPartDef d, CStore st)
    {
        if (st.Drive != ShaftState.Turning)
            return ", drilling";
        var over = st.DrivePct < 100 && st.DriveRpm >= d.GatherRpm ? " - overloaded: lower gear or stronger engine" : "";
        return $", drilling, {st.DriveRpm}/{d.GatherRpm:0} rpm, {st.DrivePct}%{over}";
    }

    /// <summary>Why a drill isn't working, when it's the buffer or the shaft ("" otherwise). Worked out from the store and the links, so every peer says the same.</summary>
    public static string StopLook(Construct? c, int part, ConstructPartDef d, CStore st)
    {
        if (d.GatherSaw)
            return "";
        if (st.Amount >= d.BulkCapacity)
        {
            if (c == null || !ChuteOut(c, part, out var cut))
                return " - buffer full: connect its Chute out";
            return cut ? " - buffer full: its chute is cut" : " - buffer full: its chute has nowhere to put this";
        }
        var why = st.Drive switch
        {
            ShaftState.EngineOff => "the engine on its drive shaft isn't running",
            ShaftState.ClutchOut => "the clutch on its drive shaft is out",
            ShaftState.Cut => "its drive shaft is cut",
            ShaftState.NoEngine => "its drive shaft doesn't reach a working engine",
            _ => "",
        };
        return why.Length > 0 ? $" - stopped: {why}" : "";
    }

    /// <summary>Is a chute linked out of this part (<paramref name="cut"/>: every one of them is cut)?</summary>
    public static bool ChuteOut(Construct c, int part, out bool cut)
    {
        var any = false;
        cut = true;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Chute || r.Source != part)
                continue;
            any = true;
            cut &= !r.FedEnd;
        }
        return any;
    }
}
