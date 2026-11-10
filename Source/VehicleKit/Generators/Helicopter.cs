using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Helicopters (docs/vehiclekit/AIRCRAFT.md), on the Small Helicopter's wiring: a Turbine stood on end under a T Gearbox, a Helicopter Rotor on top, the gearbox's branch
/// shaft out to a Right-Angle Gearbox and a Tail Rotor at the boom's end; a Flight Stabilizer on Rotorcraft keys sends <c>lift</c> to the rotor's collective, <c>pitch</c>
/// and <c>roll</c> to its cyclic, and <c>yaw</c> to the tail rotor (whose torque mix cancels the main rotor's torque, as a helicopter's pedals are linked to its collective).
/// From the seed: the tail's length and which side its rotor turns on, a fin with or without a small tail plane, an open seat or a cab of frame blocks,
/// a second tank, and how hard the stabilizer leans. Only the small rotor: a Medium Helicopter Rotor weighs 4.7 t. The mast stands where the hub is over the centre of mass: built twice off to one side with it at either end of its stations, as <see cref="Plane"/> places its wing.
/// </summary>
public sealed class Helicopter(FactionStyle style) : VehicleShape(style), IAircraftShape
{
    public override string Shape => "Helicopter";

    public override VehicleCheckOptions? Checks => new() { Flies = true, MinMass = 300, MaxMass = 2500, MaxSideFill = 1, MaxTopFill = 1, MinStability = 0 };

    /// <summary>None ship as files: helicopters are made when they spawn (no style names the shape in its extras yet).</summary>
    public override IReadOnlyList<int> Seeds => [];

    private const string Stab = "Flight Stabilizer", Rotor = "Helicopter Rotor";

    /// <summary>What the seed chose, before anything is built: the same for every trial build of the balance.</summary>
    private sealed record Layout(string Tail, bool Left, bool Skids, bool Cab, bool TailPlane, int Tanks, int Lean, string Frame, string Body);

    /// <summary>Mast stations: the keel's +y sockets then the boom's, 0.25 m apart; the turbine, the tank and the gear go on the one that puts the hub over the centre of mass
    /// (the rotor's cyclic trims a twentieth of a metre of it at most, so it is built where the gap is least).</summary>
    private const int First = 3, Last = 13;

    public override void Build(Vehicle v)
    {
        var d = Mixed(v.Seed);
        var rig = d.Fork("rig");
        var tail = rig.Pick((string[])["Square Beam 1 m", "Square Beam 1.5 m", "Square Beam 2 m"]); // (behind a 3 m boom: the tail rotor's push, which holds the main rotor's torque, falls with its arm, and the craft hangs rolled against it)
        var left = rig.Chance(0.5); // (which side of the tail its rotor turns on)
        var skids = true; // (on wheels under the tank and a tail leg it stood 17 degrees nose up: wheels wait for a nose or tail wheel placed by the balance)
        var body = d.Fork("body");
        var cab = body.Chance(0.6);
        var tailPlane = body.Chance(0.4);
        var tanks = body.Chance(0.3) ? 2 : 1;
        var lean = d.Fork("temper").Pick((int[])[25, 30, 35]); // (it hangs some 9 degrees rolled against its tail rotor: the lean is on top of that)
        var paint = d.Fork("paint");
        var plan = new Layout(tail, left, skids, cab, tailPlane, tanks, lean, Style.Colour(paint, "frame"), Style.Colour(paint, "body"));
        // Two trial builds with the mast at either end of the keel, then the station that puts the hub over the centre of mass (the gap moves in a line with the mast).
        var a = Trial(v, plan, First);
        var b = Trial(v, plan, Last);
        var per = (b - a) / (Last - First);
        var k = Math.Clamp(Math.Abs(per) < 1e-4f ? Last : (int)MathF.Round(First - a / per), First, Last);
        Make(v, plan, k);
    }

    /// <summary>A helicopter built off to one side with its mast at <paramref name="station"/>: how far its centre of mass is behind the rotor's hub (m).</summary>
    private float Trial(Vehicle v, Layout plan, int station)
    {
        var t = new Vehicle(v.Name, Style, v.Seed);
        var hub = Make(t, plan, station);
        t.Live.PartBounds(hub.Index, out var mn, out var mx);
        return t.Live.Com.Z - (mn.Z + mx.Z) * 0.5f; // (both in the body's own frame)
    }

    /// <summary>The helicopter with its mast at keel socket <paramref name="station"/>; returns the main rotor.</summary>
    private static VPart Make(Vehicle v, Layout l, int station)
    {
        var frame = l.Frame;
        var skin = l.Body;
        v.Description = $"A light helicopter on {(l.Skids ? "skids" : "wheels")}{(l.Cab ? ", a cab round its seat" : "")}{(l.Tanks == 2 ? ", two tanks" : "")}. " +
                        "Shift climbs, Ctrl sinks, W and S lean it on, Q and E sideways, A and D turn.";

        // The keel: a 2 m beam, the seat on its front end, the turbine stood on it under the rotor, the tank under it.
        var keel = v.Root("Square Beam 2 m", new Vector3(0, 1.4f, 0), Orient.Forward);
        v.Paint(keel, frame);
        var seat = v.Onto(keel, "-z", "Seat", "back");
        if (l.Cab)
            foreach (var side in (string[])["+x", "-x"])
                v.Paint(v.Onto(seat, side, "Frame Block", side == "+x" ? "-x" : "+x"), skin);
        // The boom: the mast may stand on it too.
        var boom = v.Onto(keel, "+z", "Square Beam 2 m", "-z");
        v.Paint(boom, frame);
        var (on, k) = station < 8 ? (keel, station) : (boom, station - 8);
        var turbine = v.Onto(on, $"+y{k}", "Turbine", "-z");
        v.Paint(turbine, frame);
        var box = v.Onto(turbine, "+z", "T Gearbox", "-z", 0);
        var rotor = v.Onto(box, "+z", Rotor, "base");
        var tank = v.Onto(keel, "-y1", "Fuel Tank", "+y"); // (forward, under the seat's back, with the battery: weight ahead of the mast puts the mast ahead, and the tail rotor on a long arm pushes less)
        v.Paint(tank, skin);
        VPart? tank2 = null;
        if (l.Tanks == 2)
        {
            tank2 = v.Onto(tank, "+z", "Fuel Tank", "-z");
            v.Paint(tank2, skin);
        }
        v.Paint(v.Onto(tank2 ?? tank, "+z", "Battery", "-z"), frame);

        // The boom and the tail: a mast at its end, the right-angle box on the mast's side, the tail rotor on the box; the fin on the boom's end.
        var mid = v.Onto(boom, "+z", "Square Beam 1 m", "-z");
        v.Paint(mid, frame);
        var end = v.Onto(mid, "+z", l.Tail, "-z");
        v.Paint(end, frame);
        var last = (int)MathF.Round(Length(l.Tail) / 0.25f) - 1;
        var mast = v.Onto(end, $"+y{last}", "Square Beam 0.5 m", "-z");
        v.Paint(mast, frame);
        var angle = v.Onto(mast, l.Left ? "-x1" : "+x1", "Right-Angle Gearbox", "+y", 0);
        var tailRotor = v.Onto(angle, "-y", "Tail Rotor", "base");
        v.Config(tailRotor, ("torqueMix", 1), ("reverse", l.Left ? 0 : 1));
        var fin = v.Onto(end, "+z", "Tail Fin", "front");
        v.Paint(fin, skin);
        if (l.TailPlane)
            foreach (var side in (string[])["+x", "-x"])
            {
                var at = $"{side}{Math.Max(last - 1, 0)}";
                if (v.TurnOf(end, at, "Tail Plane", side == "+x" ? "-x" : "+x", 0) != null)
                    v.Paint(Level(v, end, at, "Tail Plane", side == "+x" ? "-x" : "+x"), skin);
            }

        // The stabilizer on the boom, level and facing forward, flying the rotor and the tail.
        var stab = Level(v, station >= 5 ? keel : boom, station >= 5 ? "+y2" : "+y6", Stab, "-y"); // (clear of the turbine's footing)
        v.Config(stab, ("keys", "Rotorcraft"), ("maxTilt", l.Lean));
        v.Link("data", seat, stab);
        v.Link("data", stab, rotor, "lift", "collective");
        v.Link("data", stab, rotor, "pitch", "cyclic pitch");
        v.Link("data", stab, rotor, "roll", "cyclic roll");
        v.Link("data", stab, tailRotor, "yaw", "collective");
        v.Link("shaft", box, angle, "Branch shaft", "Input shaft");
        v.Link("pipe", tank, turbine, "Fuel out", "Fuel in");
        if (tank2 != null)
            v.Link("pipe", tank2, turbine, "Fuel out", "Fuel in");

        // The gear: a cross beam under the tank, with wheels at its ends, or posts down from it to two skids along the craft; and a tail leg.
        var leg = v.TurnOf(on, $"-y{k}", "Square Beam 0.5 m", "+z", 0) != null ? v.Onto(on, $"-y{k}", "Square Beam 0.5 m", "+z") : v.Onto(tank2 ?? tank, "-y", "Square Beam 0.75 m", "+z"); // (under the mast, or under a tank that hangs there)
        v.Paint(leg, frame);
        var axle = v.Onto(leg, "-z", "Square Beam 2 m", "+y3", 1);
        v.Paint(axle, frame);
        if (l.Skids)
            foreach (var e in (string[])["-y0", "-y7"])
            {
                var post = v.Onto(axle, e, "Square Beam 0.5 m", "+z");
                v.Paint(post, frame);
                v.Paint(Along(v, post, "-z", "Square Beam 2 m", "+y3"), frame);
            }
        else
            foreach (var e in (string[])["-z", "+z"])
                v.Onto(v.Onto(axle, e, "Suspension", "mount"), "axle", "Wheel", "axle");
        return rotor;
    }

    private static float Length(string beam) => beam switch { "Square Beam 1 m" => 1f, "Square Beam 1.5 m" => 1.5f, _ => 2f };

    /// <summary>The seed's dice, its bits mixed first (as <see cref="Drone"/>'s).</summary>
    private static Dice Mixed(int seed)
    {
        var z = unchecked((uint)seed * 0x9E3779B9u);
        z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
        z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
        return Dice.Of((int)(z ^ (z >> 16))).Fork("Helicopter");
    }

    /// <summary>A beam onto a socket, running fore and aft with its own +y up: a skid.</summary>
    private static VPart Along(Vehicle v, VPart on, string socket, string part, string by)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f && MathF.Abs(Rot24.Apply(t, Vector3.UnitZ).Z) > 0.9f)
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't run fore and aft on {on.Name}'s {socket}");
    }

    /// <summary>A part onto a socket, upright and facing forward (-z): a stabilizer reads its own frame.</summary>
    private static VPart Level(Vehicle v, VPart on, string socket, string part, string by)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f && Vector3.Dot(Rot24.Apply(t, -Vector3.UnitZ), -Vector3.UnitZ) > 0.9f)
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't stand level on {on.Name}'s {socket}");
    }
}
