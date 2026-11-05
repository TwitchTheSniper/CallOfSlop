using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a part is in a hydraulic circuit.</summary>
public enum HydKind : byte
{
    None,
    Pump, // charges a circuit, turned by a shaft or on power
    Valve, // a logic part whose signal, -1 to 1, opens it: a Lever Valve, a Digital Valve Block
    Cylinder, // a head with a hose in: a cylinder on a circuit
    Electric, // a head with a Set signal and its own motor: no circuit
}

/// <summary>Why a hydraulic cylinder can't move.</summary>
public enum HydState : byte
{
    Ok,
    NoHose, // no oil link into it: limp
    HoseCut, // its hose is cut: limp and spraying
    NoValve, // its hose doesn't come from a valve
    NoPump, // the valve has no pump, or its supply is cut: limp
}

/// <summary>A cylinder head on a construct, found with the caches. The solve fills in what it may do; the tick moves the connector.</summary>
public struct CHydHead
{
    public ushort Part, Valve;
    public short Route, Pump; // its oil link; its pump's index (-1: none)
    public HydState State;
    public float Want; // the signed speed the valve asks (m/s)
    public float Speed; // the signed speed its share of the flow gives
    public float Force; // N: pressure over area
    public float Load; // N: what its row carried, smoothed
    public bool Stalled; // pushing against what it can't move
}

/// <summary>A pump on a construct: one pressure and one flow, solved a few times a second.</summary>
public struct CHydPump
{
    public ushort Part;
    public float Pressure; // Pa
    public float Flow; // litres a second it can give now
    public float Used; // ... and what was asked of it
    public float Need; // Pa: the pressure the work asks
    public float Rpm;
    public bool Shaft; // on a drive shaft (else on power)
    public float Through; // the turning force's gain to the crank
}

public struct CHydValve
{
    public ushort Part;
    public short Route, Pump;
}

public sealed partial class Construct
{
    public CHydHead[]? HydHeads; // made for the first construct that needs them: most never do
    public CHydPump[]? HydPumps;
    public CHydValve[]? HydValves;
    public int HydHeadCount, HydPumpCount, HydValveCount;
    public ushort[]? Trays; // the Cable Trays and Clamps (ConstructTrays.cs)
    public int TrayCount;
    public float HydLoad; // N m at the crank that its pumps take (server)
    public int HydSpray; // the solves until a cut hose sprays again

    /// <summary>The ticks left of a stalled engine (a pump's load held it down): it won't turn until they run out.</summary>
    public int StallTicks;
    public int LugTicks; // ticks an overloaded engine has been held down

    public int HydHeadOf(int part)
    {
        for (var k = 0; k < HydHeadCount; k++)
            if (HydHeads![k].Part == part)
                return k;
        return -1;
    }
}

public static partial class ConstructParts
{
    private static void ParseHydraulics(ConstructPartDef d, JsonElement root, string file)
    {
        d.ToolOnly = Opt(root, "toolOnly") is { ValueKind: JsonValueKind.True };
        ConstructTrays.Parse(d, root, file);
        if (Opt(root, "hydraulics") is not { } h)
            return;
        Known(h, ["kind", "bar", "flow", "ratedRpm", "area", "speed", "force"], $"{file}, hydraulics");
        var kind = Str(Need(h, "kind", file), file, "hydraulics.kind");
        d.Hyd = kind switch
        {
            "pump" => HydKind.Pump,
            "valve" => HydKind.Valve,
            "cylinder" => HydKind.Cylinder,
            "electro" => HydKind.Electric,
            _ => throw new ConstructPartException($"{file}: hydraulics.kind is pump, valve, cylinder or electro, not \"{kind}\""),
        };
        float N(string key, float fallback = 0) => Opt(h, key) is { } e ? Num(e, file, "hydraulics." + key) : fallback;
        d.HydBar = N("bar");
        d.HydFlow = N("flow");
        d.HydRpm = N("ratedRpm");
        d.HydArea = N("area");
        d.HydSpeed = N("speed");
        d.HydForce = N("force");
        if (d.Hyd == HydKind.Pump && (d.HydBar <= 0 || d.HydFlow <= 0 || d.HydRpm <= 0))
            throw new ConstructPartException($"{file}: a pump needs bar, flow and ratedRpm");
        if (d.Hyd == HydKind.Valve && d.Role != PartRole.Logic)
            throw new ConstructPartException($"{file}: a valve is a logic part: its signal opens it");
        if (d.Hyd == HydKind.Cylinder && (d.HydArea <= 0 || d.HydSpeed <= 0))
            throw new ConstructPartException($"{file}: a cylinder head needs area and speed");
        if (d.Hyd == HydKind.Electric && (d.HydForce <= 0 || d.HydSpeed <= 0 || d.Role != PartRole.Logic))
            throw new ConstructPartException($"{file}: an electric head is a logic part (its Set signal) with force and speed");
    }
}

/// <summary>
/// Hydraulics (docs/sandbox/CONSTRUCTS.md, "Hydraulics"). A circuit is pipe links carrying oil from a pump, through valves, to cylinder
/// heads, all on one construct. There is no fluid: each pump is one pressure and one flow, solved at <see cref="Hz"/>. Pressure over a
/// head's area is its force, the pump's flow shared among what the valves ask is its speed, so many cylinders slow each other down.
/// The work comes off the engine (<see cref="Construct.HydLoad"/>, carried like a drill's) or the battery, and an engine held down
/// long enough stalls. The cylinder itself is a connector (ConstructConnectors.cs) on its head part; this file says how far and how
/// hard, the connector's row does it. A construct with no hydraulic parts is one integer test here. Nothing allocates per tick.
/// </summary>
public static class ConstructHydraulics
{
    public const int Hz = 6, Every = World.TickRate / Hz;
    public const float Efficiency = 0.85f; // what the pump's work comes to at the rods
    public const float DeadBand = 0.03f; // a valve this near the middle is shut
    public const float LitresPerMetre = 0.1f; // a stroke of 1 m/s over 1 cm2 is this many litres a second
    public const int StallAfter = 2 * World.TickRate; // an engine held below StallShare this long stalls
    public const int StallTicks = 3 * World.TickRate; // ... and is dead this long
    public const float StallShare = 0.3f; // the share of the load below which it counts as held down
    public const float WindUp = 0.03f; // m: how far a cylinder's asked length may lead its real one
    public const int SprayEvery = 2; // solves between a cut hose's sprays

    /// <summary>With the caches: the heads, pumps and valves, and the oil links between them.</summary>
    public static void Build(Construct c)
    {
        c.HydHeadCount = c.HydPumpCount = c.HydValveCount = c.TrayCount = 0;
        c.HydLoad = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.Parts[i].D;
            if (d.TrayLanes > 0)
            {
                Fit(ref c.Trays, c.TrayCount);
                c.Trays![c.TrayCount++] = (ushort)i;
            }
            switch (d.Hyd)
            {
                case HydKind.Pump:
                    Fit(ref c.HydPumps, c.HydPumpCount);
                    c.HydPumps![c.HydPumpCount++] = new CHydPump { Part = (ushort)i, Through = 1 };
                    break;
                case HydKind.Valve:
                    Fit(ref c.HydValves, c.HydValveCount);
                    c.HydValves![c.HydValveCount++] = new CHydValve { Part = (ushort)i, Route = -1, Pump = -1 };
                    break;
                case HydKind.Cylinder or HydKind.Electric:
                    Fit(ref c.HydHeads, c.HydHeadCount);
                    c.HydHeads![c.HydHeadCount++] = new CHydHead { Part = (ushort)i, Valve = ushort.MaxValue, Route = -1, Pump = -1 };
                    break;
            }
        }
        if (c.HydHeadCount + c.HydValveCount == 0)
            return;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Pipe || r.To >= c.PartCount || r.Source >= c.PartCount)
                continue;
            var to = c.Parts[r.To].D.Hyd;
            if (to == HydKind.Cylinder && c.HydHeadOf(r.To) is var h and >= 0)
            {
                c.HydHeads![h].Route = (short)k;
                c.HydHeads[h].Valve = r.Source;
            }
            else if (to == HydKind.Valve)
                for (var v = 0; v < c.HydValveCount; v++)
                    if (c.HydValves![v].Part == r.To)
                    {
                        c.HydValves[v].Route = (short)k;
                        for (var p = 0; p < c.HydPumpCount; p++)
                            if (c.HydPumps![p].Part == r.Source)
                                c.HydValves[v].Pump = (short)p;
                    }
        }
    }

    private static void Fit<T>(ref T[]? a, int n)
    {
        if (a == null)
            a = new T[4];
        else if (n >= a.Length)
            Array.Resize(ref a, a.Length * 2);
    }

    /// <summary>How head <paramref name="k"/>'s oil stands, from the links alone (the same on every peer). Which valve, and its pump's index.</summary>
    public static HydState Trace(Construct c, in CHydHead h, out int valve, out int pump)
    {
        valve = -1;
        pump = -1;
        if (h.Route < 0)
            return HydState.NoHose;
        if (!c.Routes[h.Route].FedEnd)
            return HydState.HoseCut;
        for (var v = 0; v < c.HydValveCount; v++)
        {
            ref var hv = ref c.HydValves![v];
            if (hv.Part != h.Valve)
                continue;
            valve = v;
            if (c.Parts[hv.Part].Broken || hv.Route < 0 || hv.Pump < 0 || !c.Routes[hv.Route].FedEnd)
                return HydState.NoPump;
            pump = hv.Pump;
            return c.Parts[c.HydPumps![pump].Part].Broken ? HydState.NoPump : HydState.Ok;
        }
        return HydState.NoValve;
    }

    public static string StateText(HydState s) => s switch
    {
        HydState.NoHose => "no oil: link a valve to it",
        HydState.HoseCut => "its hose is cut",
        HydState.NoValve => "its hose must come from a valve",
        HydState.NoPump => "no pressure: its valve has no pump",
        _ => "",
    };

    /// <summary>Server, <see cref="Hz"/> times a second: every circuit's one pressure and one flow, and the speed and force of each head on it.</summary>
    public static void Solve(ConstructWorld world, Construct c)
    {
        if (c.HydHeadCount + c.HydPumpCount == 0)
            return;
        if (c.Dirty)
            c.Rebuild();
        for (var p = 0; p < c.HydPumpCount; p++)
        {
            ref var pu = ref c.HydPumps![p];
            var d = c.Parts[pu.Part].D;
            var share = 0f;
            pu.Shaft = false;
            pu.Through = 1;
            if (!c.Parts[pu.Part].Broken)
            {
                var state = ConstructLinear.ShaftOf(c, pu.Part, out pu.Rpm, out pu.Through);
                if (state == ShaftState.None)
                    share = ConstructRecycler.Powered(world, c, pu.Part) ? 1 : 0;
                else
                {
                    pu.Shaft = true;
                    share = state == ShaftState.Turning ? MathF.Min(1, pu.Rpm / d.HydRpm) * c.ShaftShare : 0;
                }
            }
            pu.Flow = d.HydFlow * share;
            pu.Pressure = d.HydBar * 1e5f; // (the spools' checks hold what they hold with the pump stopped: only flow needs it turning)
            pu.Used = 0;
            pu.Need = 0;
        }
        for (var k = 0; k < c.HydHeadCount; k++)
        {
            ref var h = ref c.HydHeads![k];
            var d = c.Parts[h.Part].D;
            if (d.Hyd != HydKind.Cylinder)
                continue;
            h.State = Trace(c, h, out var valve, out var pump);
            h.Pump = (short)pump;
            h.Want = 0;
            h.Speed = 0;
            h.Force = 0;
            if (h.State == HydState.HoseCut)
                Spray(world, c, h.Route);
            if (h.State != HydState.Ok)
                continue;
            ref var pu = ref c.HydPumps![pump];
            var u = Math.Clamp(c.Parts[c.HydValves![valve].Part].Out, -1, 1);
            h.Want = MathF.Abs(u) < DeadBand ? 0 : u * d.HydSpeed;
            h.Force = pu.Pressure * d.HydArea * 1e-4f;
            pu.Used += MathF.Abs(h.Want) * d.HydArea * LitresPerMetre;
        }
        for (var k = 0; k < c.HydHeadCount; k++)
        {
            ref var h = ref c.HydHeads![k];
            if (c.Parts[h.Part].D.Hyd != HydKind.Cylinder || h.State != HydState.Ok)
                continue;
            ref var pu = ref c.HydPumps![h.Pump];
            var scale = pu.Used > 0 ? MathF.Min(1, pu.Flow / pu.Used) : 0;
            h.Speed = h.Want * scale;
            if (h.Want == 0)
                continue;
            // The pressure it takes: its load over its area, or all of it over the relief when it can't move.
            var d = c.Parts[h.Part].D;
            pu.Need = MathF.Max(pu.Need, h.Stalled ? pu.Pressure : MathF.Min(pu.Pressure, h.Load / (d.HydArea * 1e-4f)));
            if (h.Speed != 0 && c.Asleep)
                c.Wake();
        }
        var torque = 0f;
        for (var p = 0; p < c.HydPumpCount; p++)
        {
            // A pump's torque is its displacement times the pressure, whatever it turns at: pulled down, it asks the engine just as hard.
            ref var pu = ref c.HydPumps![p];
            if (!pu.Shaft || pu.Used <= 0)
                continue;
            var d = c.Parts[pu.Part].D;
            var displacement = d.HydFlow * 1e-3f / (d.HydRpm / ConstructDrive.RpmPerRadS);
            torque += pu.Need * displacement * MathF.Min(1, pu.Used / d.HydFlow) / Efficiency / MathF.Max(0.01f, pu.Through);
        }
        c.HydLoad = torque;
    }

    /// <summary>Is this pump on power, with oil asked of it?</summary>
    public static bool Pumping(Construct c, int part)
    {
        for (var p = 0; p < c.HydPumpCount; p++)
            if (c.HydPumps![p].Part == part)
                return !c.HydPumps[p].Shaft && c.HydPumps[p].Used > 0;
        return false;
    }

    /// <summary>A cut hose under pressure sprays where it is cut (a spill, on every PC).</summary>
    private static void Spray(ConstructWorld world, Construct c, int route)
    {
        if (--c.HydSpray > 0)
            return;
        c.HydSpray = SprayEvery;
        ref var r = ref c.Routes[route];
        var at = ConstructRoutes.WorldPoint(c, route, Math.Min((int)r.FirstCut, r.Count - 1));
        world.AddSnap(new CFireEvent { Kind = CFireKind.LinkSnap, Body = c.Id, Part = (ushort)RouteType.Pipe, At = at });
    }

    /// <summary>Server, each tick: an engine held down by its pumps long enough stalls.</summary>
    public static void StepStall(Construct c)
    {
        if (c.StallTicks > 0)
        {
            c.StallTicks--;
            return;
        }
        if (c.HydLoad > 0 && c.ShaftShare < StallShare && c.EngineRunning)
        {
            if (++c.LugTicks >= StallAfter)
            {
                c.LugTicks = 0;
                c.StallTicks = StallTicks;
                c.Rpm = 0;
                c.HydLoad = 0;
            }
        }
        else if (c.LugTicks > 0)
            c.LugTicks--;
    }
}

public sealed partial class ConstructWorld
{
    private int _hydTicks;

    /// <summary>A tick's spray or snap, for the fire events (they are added after the guns' step clears them).</summary>
    public void AddSnap(in CFireEvent e)
    {
        if (_snapCount < _snaps.Length)
            _snaps[_snapCount++] = e;
    }

    /// <summary>Server, each tick: the circuits, at their own rate.</summary>
    private void StepHydraulics()
    {
        var solve = ++_hydTicks >= ConstructHydraulics.Every;
        if (solve)
            _hydTicks = 0;
        var bodies = Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c.StallTicks + c.LugTicks > 0 || c.HydLoad > 0)
                ConstructHydraulics.StepStall(c);
            if (solve)
                ConstructHydraulics.Solve(this, c);
        }
    }

    /// <summary>
    /// A new cylinder: its head goes on at the first point, welded where it is, and the connector runs from the head to the second
    /// point. <paramref name="electric"/>: the small self-contained kind. Its index, or -1. Why not, in <paramref name="why"/>.
    /// </summary>
    public int AddCylinder(IWorldQuery? q, bool electric, byte size, ConnectorEnd a, ConnectorEnd b, out string? why)
    {
        why = null;
        if (a.Body == 0)
            (a, b) = (b, a); // (the head goes on the construct)
        if (Get(a.Body) is not { } c || a.Part >= c.PartCount || !EndPoint(a, out var pa) || !EndPoint(b, out var pb))
        {
            why = "that part is gone";
            return -1;
        }
        var s = Math.Min((int)size, ConnectorSizes.Length - 1);
        var def = ConstructParts.Find($"{(electric ? "Electric" : "Hydraulic")} Cylinder Head {ConnectorSizes[s].Name}");
        if (def == null)
        {
            why = "that cylinder isn't in the game";
            return -1;
        }
        c.WorldFrame(c.Parts[a.Part].Seg, out var q0, out var t0);
        var head = AddFreeform(c, a.Part, def, Vector3.Transform(pa - t0, Quaternion.Conjugate(q0)), Quaternion.Identity);
        if (head < 0)
        {
            why = "that construct has no room for another welded piece";
            return -1;
        }
        c.Rebuild();
        StructureDirty.Add(c.Id);
        a.Part = (ushort)head;
        a.Local = Vector3.Zero;
        var dist = Vector3.Distance(pa, pb);
        var force = electric ? def.HydForce : def.HydArea * 1e-4f * 700e5f; // (as much as the biggest pump can ever ask: the pressure sets it)
        return AddConnector(q, new CConnector
        {
            Kind = ConnectorKind.Cylinder, Size = (byte)s, A = a, B = b, Length = dist, Rating = force,
            Stiffness = MathF.Max(ConnectorMinLength, dist * CylinderShort), Damping = dist * CylinderLong,
        });
    }

    public const byte CylinderElectric = 0x40; // on a new cylinder's size byte: the electric kind
    public const float CylinderShort = 0.7f, CylinderLong = 1.3f; // a new cylinder's reach, times the gap it was fitted across

    /// <summary>One tick of a cylinder: how long it is asked to be, and how hard (0: limp, no row).</summary>
    private void StepCylinder(ref CConnector cn, Construct? c, float dist, float dt)
    {
        var part = cn.A.Part;
        var k = c != null ? c.HydHeadOf(part) : -1;
        if (k < 0)
        {
            cn.RowForce = 0;
            cn.Length = dist;
            return;
        }
        ref var h = ref c!.HydHeads![k];
        var d = c.Parts[part].D;
        float speed, force;
        if (d.Hyd == HydKind.Electric)
        {
            force = d.HydForce;
            speed = 0;
            if (ConstructPower.On(c, part))
            {
                ref var p = ref c.Parts[part];
                var target = float.Lerp(cn.Stiffness, cn.Damping, Math.Clamp(p.Out, 0, 1));
                speed = Math.Clamp((target - cn.Length) / dt, -d.HydSpeed, d.HydSpeed);
            }
        }
        else
        {
            if (h.State is not HydState.Ok)
            {
                cn.RowForce = 0; // (limp: nothing holds it)
                cn.Length = dist;
                h.Stalled = false;
                return;
            }
            force = h.Force;
            speed = h.Speed;
        }
        force = MathF.Min(force, cn.Rating);
        var length = Math.Clamp(cn.Length + speed * dt, cn.Stiffness, cn.Damping);
        var lead = Math.Clamp(length, dist - ConstructHydraulics.WindUp, dist + ConstructHydraulics.WindUp); // (it can't wind up against what stops it)
        h.Stalled = speed != 0 && lead != length;
        cn.Length = MathF.Abs(length - cn.Length) > 0 && h.Stalled ? lead : length;
        cn.RowForce = force;
        h.Load += (cn.Load - h.Load) * 0.25f;
        if (speed != 0)
        {
            c.Wake();
            Get(cn.B.Body)?.Wake();
        }
    }
}
