using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The Flight Stabilizer's autopilot set from code, as its panel sets it (docs/vehiclekit/AIRCRAFT.md, "Spawning and bot pilots"): the flight check and the bot pilot
/// (<see cref="AircraftPilot"/>) both fly through it. Writes settings only; allocates nothing.
/// </summary>
public static class AircraftAutopilot
{
    /// <summary>The stabilizer that flies <paramref name="c"/> (its part index), or -1.</summary>
    public static int Stab(Construct c) => c.StabCount > 0 && c.Stabs[0].Part < c.PartCount ? c.Stabs[0].Part : -1;

    /// <summary>It flies as a plane (the stabilizer's keys): it can't stop or hover, so it circles a point and lands nowhere by itself.</summary>
    public static bool IsPlane(Construct c) => Stab(c) is var s and >= 0 && ConstructFlight.KeysOf(c, c.Parts[s], c.Parts[s].D) == FlightPattern.Plane;

    /// <summary>
    /// The autopilot on in <paramref name="mode"/>, holding <paramref name="height"/> (the hold's own height, <see cref="ConstructPhysics.HoldHeight"/>: world Y on a flat map).
    /// <paramref name="heading"/> in degrees clockwise from north (negative: left as it is); <paramref name="point"/> the waypoint (Waypoint mode).
    /// <paramref name="world"/>, when given, hears the change (its panels and saves). False when it has no stabilizer.
    /// </summary>
    public static bool Set(Construct c, FlightMode mode, float height, float heading = -1, Vector3 point = default, ConstructWorld? world = null)
    {
        var s = Stab(c);
        if (s < 0)
            return false;
        ref var p = ref c.Parts[s];
        var d = p.D;
        ConstructConfig.Put(ref p, d, CfgKey.ApMode, (float)mode);
        ConstructConfig.Put(ref p, d, CfgKey.TargetAlt, ConstructPhysics.HoldShown(MathF.Max(1, height)));
        if (heading >= 0)
            ConstructConfig.Put(ref p, d, CfgKey.TargetHeading, heading % 360);
        if (mode == FlightMode.Waypoint)
        {
            ConstructConfig.Put(ref p, d, CfgKey.WaypointX, point.X);
            ConstructConfig.Put(ref p, d, CfgKey.WaypointY, point.Y);
            ConstructConfig.Put(ref p, d, CfgKey.WaypointZ, point.Z);
        }
        ConstructConfig.Put(ref p, d, CfgKey.Autopilot, 1);
        world?.StructureDirty.Add(c.Id);
        return true;
    }

    /// <summary>The autopilot off (the stabilizer's hover or level hold, or the hands in the seat, from now on).</summary>
    public static void Off(Construct c, ConstructWorld? world = null)
    {
        var s = Stab(c);
        if (s < 0)
            return;
        ref var p = ref c.Parts[s];
        ConstructConfig.Put(ref p, p.D, CfgKey.Autopilot, 0);
        world?.StructureDirty.Add(c.Id);
    }

    /// <summary>Its stabilizer's own setting for the landing assist (m above the ground where it slows its descent), when it has the setting.</summary>
    public static void LandingAssist(Construct c, float metres)
    {
        if (Stab(c) is var s and >= 0)
            ConstructConfig.Put(ref c.Parts[s], c.Parts[s].D, CfgKey.LandingAssist, metres);
    }

    /// <summary>A plane's throttle (0 to 1: the stabilizer keeps it where it is left, as Shift and Ctrl do).</summary>
    public static void Throttle(Construct c, float share)
    {
        if (Stab(c) is var s and >= 0)
            c.Parts[s].Mem = Math.Clamp(share, 0, 1);
    }

    /// <summary>The stabilizer's forward in the world (its nose), and its heading in degrees clockwise from -Z (north on a flat map).</summary>
    public static Vector3 Nose(Construct c) => Stab(c) is var s and >= 0 ? Vector3.Transform(-Vector3.UnitZ, Quaternion.Multiply(c.Rot, Rot24.Quat(c.Parts[s].Rot))) : Vector3.Transform(-Vector3.UnitZ, c.Rot);

    /// <summary>A plane's cruise throttle once level (the flight check levels off at it, as a pilot does).</summary>
    public const float CruiseThrottle = 0.7f;

    /// <summary>
    /// Server: puts a built aircraft in the air as if it had been flying (docs/vehiclekit/AIRCRAFT.md, "Air spawn"): moved up by <paramref name="lift"/> (the caller moves the
    /// body; this sets what it does), its rotors and props at their rated speed, the engine running, the stabilizer armed with its autopilot holding the height it is at; a plane
    /// also goes at <paramref name="speed"/> along its nose with the throttle at cruise. Home is where it is. Called once, never on a steady tick.
    /// </summary>
    public static void Launch(ConstructWorld world, Construct c, float speed)
    {
        if (c.Dirty)
            c.Rebuild();
        var s = Stab(c);
        if (s < 0)
            return;
        var plane = IsPlane(c);
        for (var k = 0; k < c.RotorCount; k++)
            if (c.Rotors[k].Part < c.PartCount && c.Parts[c.Rotors[k].Part].D.Rotor is { } rd)
                c.Rotors[k].Omega = rd.RatedOmega;
        if (c.Engine >= 0 && c.Engine < c.PartCount && c.HasFuel)
        {
            c.EngineRunning = true;
            c.Rpm = MathF.Max(c.Rpm, c.EngineDef.MaxRpm * 0.95f);
        }
        ref var p = ref c.Parts[s];
        p.State |= 1; // (armed: at work, as after a take-off)
        if (plane)
        {
            p.Mem = CruiseThrottle;
            var nose = Nose(c);
            c.Vel = Vector3.Normalize(new Vector3(nose.X, 0, nose.Z)) * speed;
        }
        else
            c.Vel = Vector3.Zero;
        c.AngVel = Vector3.Zero;
        ConstructFlight.SetHome(world, c, s);
        var height = ConstructPhysics.HoldHeight(c.WorldCom);
        if (plane)
            Set(c, FlightMode.Waypoint, height, -1, c.WorldCom + Nose(c) * 300, world); // (straight on until the pilot names a point: it circles one when near)
        else
            Set(c, FlightMode.Hover, height, -1, default, world);
        c.Wake();
    }
}

/// <summary>What a bot pilot is doing.</summary>
public enum PilotOrder : byte
{
    Hold, // hover where it is (a plane circles where it is)
    Patrol, // round a point: a rotorcraft from corner to corner of a square about it, a plane circling it
    Goto, // to a point and hold there
    Return, // home (where it was launched or took off), then down: drones and helicopters land; a plane circles home
    Land, // down where it is (rotorcraft only; a plane circles)
}

/// <summary>
/// A server-side bot pilot for an AI aircraft (docs/vehiclekit/AIRCRAFT.md, "Bot pilots"): it sets the Flight Stabilizer's autopilot (<see cref="AircraftAutopilot"/>) and lets the
/// stabilizer fly. Looked at every <see cref="Every"/> ticks; between looks it costs a counter. No allocation after it is made. It lets go when the craft is gone, wrecked
/// (no stabilizer), boarded by a player (its seat taken: captured) or landed.
/// </summary>
public sealed class AircraftPilot
{
    /// <summary>Ticks between looks.</summary>
    public const int Every = 15;

    /// <summary>How near a rotorcraft must come to a patrol corner or a point (m, along the ground) before it is reached.</summary>
    public const float Reach = 8f;

    public readonly ushort Id;
    public PilotOrder Order;
    public Vector3 Centre, Home;
    public float Radius, Height; // patrol radius (m); height above the ground under it (m)
    public bool Plane;
    public int Leg; // the patrol corner it flies to
    public bool Landed; // the craft is down and let go: the pilot is done
    private int _tick;
    private float _landAlt = float.NaN;

    public AircraftPilot(Construct c, PilotOrder order, Vector3 centre, float radius, float height)
    {
        Id = c.Id;
        Order = order;
        Centre = centre;
        Radius = MathF.Max(order == PilotOrder.Patrol ? 20 : 0, radius);
        Height = MathF.Max(5, height);
        Plane = AircraftAutopilot.IsPlane(c);
        Home = c.WorldCom;
    }

    /// <summary>What it does now, for a log line.</summary>
    public string Line(ConstructWorld w) => w.Get(Id) is { } c
        ? $"{Order}{(Order == PilotOrder.Patrol ? $" leg {Leg}" : "")}, {ConstructPhysics.HoldHeight(c.WorldCom) - Ground(c.WorldCom):0} m up, {c.Vel.Length():0.0} m/s{(Landed ? ", landed" : "")}"
        : "gone";

    /// <summary>A new order, acted on at once.</summary>
    public void Give(Construct c, ConstructWorld w, PilotOrder order, Vector3 centre)
    {
        Order = order;
        Centre = centre;
        Leg = 0;
        _landAlt = float.NaN;
        _tick = 0;
        Look(c, w);
    }

    /// <summary>One server tick. False once the pilot is done with it (gone, wrecked, captured, landed): the caller drops it.</summary>
    public bool Step(ConstructWorld w)
    {
        if (Landed)
            return false;
        if (--_tick > 0)
            return true;
        _tick = Every;
        if (w.Get(Id) is not { } c || AircraftAutopilot.Stab(c) < 0)
            return false;
        if (c.Driver != 255)
        {
            AircraftAutopilot.Off(c, w); // (a player has the seat: theirs now)
            return false;
        }
        Look(c, w);
        return !Landed;
    }

    /// <summary>The ground (terrain and static shapes, never a construct: the craft itself is one) under a point, by one ray from 50 m above it; flat maps only.</summary>
    private static float Ground(Vector3 at)
    {
        const float above = 50, reach = 1000;
        var d = World.Query.Raycast(at + Vector3.UnitY * above, -Vector3.UnitY, reach, QueryFilter.StaticOnly);
        return d < reach ? at.Y + above - d : at.Y - reach;
    }

    /// <summary>The order turned into the autopilot's settings for the next <see cref="Every"/> ticks.</summary>
    private void Look(Construct c, ConstructWorld w)
    {
        var at = c.WorldCom;
        switch (Order)
        {
            case PilotOrder.Hold:
                if (Plane)
                    Circle(c, w, Centre);
                else
                    Point(c, w, Centre);
                break;
            case PilotOrder.Goto:
                if (Plane)
                    Circle(c, w, Centre);
                else
                    Point(c, w, Centre);
                break;
            case PilotOrder.Patrol:
                if (Plane)
                {
                    Circle(c, w, Centre);
                    break;
                }
                var corner = Corner(Leg);
                if (Flat(corner - at) < Reach)
                {
                    Leg = (Leg + 1) & 3;
                    corner = Corner(Leg);
                }
                Point(c, w, corner);
                break;
            case PilotOrder.Return:
                if (Plane || Flat(Home - at) > Reach)
                {
                    if (Plane)
                        Circle(c, w, Home);
                    else
                        Point(c, w, Home);
                    break;
                }
                Order = PilotOrder.Land;
                goto case PilotOrder.Land;
            case PilotOrder.Land:
                if (Plane)
                {
                    Circle(c, w, at);
                    break;
                }
                Down(c, w, at);
                break;
        }
    }

    private Vector3 Corner(int leg)
    {
        var x = (leg is 1 or 2) ? Radius : -Radius;
        var z = leg < 2 ? -Radius : Radius;
        return Centre + new Vector3(x, 0, z);
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>The height to hold over a point: <see cref="Height"/> above the higher of the ground there and under the craft.</summary>
    private float HoldOver(Vector3 at, Vector3 point) => MathF.Max(Ground(at), Ground(point)) + Height;

    /// <summary>A rotorcraft to a point: Waypoint mode there, at its height.</summary>
    private void Point(Construct c, ConstructWorld w, Vector3 point)
    {
        point.Y = HoldOver(c.WorldCom, point);
        AircraftAutopilot.Set(c, FlightMode.Waypoint, point.Y, -1, point, w);
    }

    /// <summary>A plane round a point: the stabilizer circles a waypoint it can't stop at; its throttle stays at cruise.</summary>
    private void Circle(Construct c, ConstructWorld w, Vector3 point)
    {
        point.Y = HoldOver(c.WorldCom, point);
        AircraftAutopilot.Set(c, FlightMode.Waypoint, point.Y, -1, point, w);
        AircraftAutopilot.Throttle(c, AircraftAutopilot.CruiseThrottle);
    }

    /// <summary>A rotorcraft down where it is: the held height stepped down at a walking pace with the landing assist on, then, on the ground and still, let go.</summary>
    private void Down(Construct c, ConstructWorld w, Vector3 at)
    {
        var ground = Ground(at);
        var gap = c.WorldCom.Y - ground - MathF.Max(0, c.Com.Y - c.LocalMin.Y); // (its lowest point over the ground, level: as the landing assist reads it)
        if (float.IsNaN(_landAlt))
        {
            _landAlt = ConstructPhysics.HoldHeight(at);
            AircraftAutopilot.LandingAssist(c, 4);
        }
        _landAlt = MathF.Max(ground - 1, _landAlt - 2.5f * Every / World.TickRate); // (2.5 m/s; it aims below the ground so the last metre isn't held off)
        var s = AircraftAutopilot.Stab(c);
        ConstructConfig.Put(ref c.Parts[s], c.Parts[s].D, CfgKey.ApMode, (float)FlightMode.Hover);
        ConstructConfig.Put(ref c.Parts[s], c.Parts[s].D, CfgKey.TargetAlt, ConstructPhysics.HoldShown(MathF.Max(1, _landAlt)));
        ConstructConfig.Put(ref c.Parts[s], c.Parts[s].D, CfgKey.Autopilot, 1);
        if (gap < 0.6f && MathF.Abs(c.Vel.Y) < 0.3f && c.Vel.Length() < 1f)
        {
            AircraftAutopilot.Off(c, w);
            c.Parts[s].State = (byte)(c.Parts[s].State & ~1); // (disarmed: idle, as a pilot lets go on the ground)
            Landed = true;
        }
        w.StructureDirty.Add(c.Id);
    }
}
