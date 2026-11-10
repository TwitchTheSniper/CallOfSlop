using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Fixed-wing prop planes (docs/vehiclekit/AIRCRAFT.md), on the Light Plane's wiring: a Flight Stabilizer on Plane keys, its <c>throttle</c> into the props, <c>pitch</c> into the
/// elevator, <c>yaw</c> into the rudder, <c>left aileron</c> and <c>right aileron</c> into the ailerons and <c>flaps</c> into the flaps. From the seed: a single tractor engine
/// (a Turbine with a three-blade prop), a pusher on a pylon, or twin Light Turbojets under the wings (a construct drives shafts from one engine only, so twin props
/// would turn one prop); low, mid or high wings of three or four straight, narrow or fabric panels a side; a conventional tail or a T-tail on a boom; tricycle or tail-dragger gear.
/// The fuselage is a cockpit block and a beam spine. The wing goes where the balance wants it: the plane is built twice off to one side with the wing at two stations,
/// its centre of mass and centre of lift (<see cref="Construct.LiftCentre"/>) measured, and the station that puts the centre of mass a margin ahead of the lift is the one built.
/// </summary>
public sealed class Plane(FactionStyle style) : VehicleShape(style), IAircraftShape
{
    public override string Shape => "Plane";

    public override VehicleCheckOptions? Checks => new() { Flies = true, MinMass = 100, MaxMass = 3000, MaxSideFill = 1, MaxTopFill = 1, MinStability = 0, MaxRestTilt = VehicleFlightCheck.MaxPlaneRestTilt };

    /// <summary>None ship as files: planes are made when they spawn (no style names the shape in its extras yet).</summary>
    public override IReadOnlyList<int> Seeds => [];

    private enum Power { Tractor, Jet, Pusher }

    private enum WingAt { Low, Mid, High }

    /// <summary>What the seed chose, before anything is built: the same for every trial build of the balance.</summary>
    private sealed record Layout(Power Power, bool Turbine, WingAt Wing, string Panel, int Panels, bool Flaps, bool TTail, float Boom, bool Tricycle, string Frame, string Body, string Trim);

    /// <summary>How far the centre of mass goes ahead of the centre of lift (m): enough to be stable, little enough that the elevator can still lift the nose.</summary>
    private const float Margin = 0.2f;

    /// <summary>How far behind the centre of mass a tricycle's mains stand (m), at least.</summary>
    private const float MainsBehind = 0.25f;

    private const string Stab = "Flight Stabilizer", Spine = "Square Beam 2 m";

    /// <summary>Wing stations are the spine's sockets, 0.25 m apart from the first beyond the cockpit; the wing is built on one of these.</summary>
    private const int Stations = 8;

    public override void Build(Vehicle v)
    {
        var d = Mixed(v.Seed);
        var plan = Plan(d);
        var first = plan.Wing == WingAt.Mid ? 3 : 1; // (a mid wing's root would cut the cockpit block further forward)
        var last = Stations - 1;
        // Two trial builds with the wing at either end, then the station that gives the margin (both sums move in a line with the wing's place).
        var (a, _) = Trial(v, plan, first, 0);
        var (b, _) = Trial(v, plan, last, 0);
        var per = (b - a) / (last - first);
        var k = Math.Clamp(Math.Abs(per) < 1e-4f ? last : (int)MathF.Round(first + (Margin - a) / per), first, last);
        var (_, com) = Trial(v, plan, k, 0); // (then where its centre of mass is, for the mains)
        Make(v, plan, k, com);
    }

    /// <summary>The seed's dice, its bits mixed first (as <see cref="Drone"/>'s: <see cref="Dice"/>'s first roll of a fork follows a seed's low bits).</summary>
    private static Dice Mixed(int seed)
    {
        var z = unchecked((uint)seed * 0x9E3779B9u);
        z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
        z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
        return Dice.Of((int)(z ^ (z >> 16))).Fork("Plane");
    }

    private Layout Plan(Dice d)
    {
        var rig = d.Fork("rig");
        var power = rig.Pick((Power[])[Power.Tractor, Power.Tractor, Power.Jet, Power.Pusher]);
        var turbine = true; // (a Two-Blade Prop on an Engine pushes some 1,500 N: too little for a 600 kg plane at this game's gravity; AIRCRAFT.md)
        var wing = power switch
        {
            Power.Pusher => rig.Pick((WingAt[])[WingAt.Low, WingAt.Mid]), // (the pylon stands on the spine where a high wing's tank would be)
            _ => rig.Pick((WingAt[])[WingAt.Low, WingAt.Mid, WingAt.High]),
        };
        var wings = d.Fork("wings");
        var panel = wings.Pick((string[])["Wing Segment", "Wing Segment", "Narrow Wing Segment", "Fabric Wing Segment"]);
        var panels = panel == "Narrow Wing Segment" || power == Power.Jet ? 4 : wings.Pick((int[])[3, 3, 4]); // (at this game's gravity a 700 kg plane wants 18 m2 or more to stall under 30 m/s; a narrow panel is 2 m2)
        var flaps = wings.Chance(0.5);
        var tail = d.Fork("tail");
        var tTail = tail.Chance(0.35);
        var boom = 2f; // (a 1 m boom put the tail plane in the flaps of a wing set far back, and a pusher's prop in the fin)
        var tricycle = d.Fork("gear").Chance(0.5);
        var paint = d.Fork("paint");
        return new Layout(power, turbine, wing, panel, panels, flaps, tTail, boom, tricycle, Style.Colour(paint, "frame"), Style.Colour(paint, "body"), Style.Colour(paint, "frame"));
    }

    /// <summary>A plane built off to one side with its wing at station <paramref name="station"/>: how far its centre of mass is ahead of its centre of lift (m).</summary>
    private (float Margin, float Com) Trial(Vehicle v, Layout plan, int station, float com)
    {
        var t = new Vehicle(v.Name, Style, v.Seed);
        Make(t, plan, station, com);
        var c = t.Live;
        if (!c.LiftCentre(out var lift, out _))
            throw new VehicleKitException("the trial plane has no lift");
        return (lift.Z - c.Com.Z, c.Com.Z); // (forward is -z: the centre of mass ahead is a smaller z; the rest frame's z is the plane's, the cockpit at 0)
    }

    /// <summary>The plane with its wing at <paramref name="station"/>; a tricycle's mains half a metre behind <paramref name="com"/> (the centre of mass's z, 0 on a trial).</summary>
    private void Make(Vehicle v, Layout l, int station, float com)
    {
        var engineName = l.Turbine ? "Turbine" : "Engine";
        var propName = l.Turbine ? "Three-Blade Prop" : "Two-Blade Prop";
        v.Description = $"A {(l.Power == Power.Jet ? "twin-jet" : l.Power == Power.Pusher ? "pusher" : "single-engined")} {(l.Wing == WingAt.High ? "high" : l.Wing == WingAt.Low ? "low" : "mid")}-wing plane " +
                        $"on {(l.Tricycle ? "tricycle gear" : "a tail wheel")}{(l.Panel == "Fabric Wing Segment" ? ", fabric wings" : "")}{(l.TTail ? ", T-tail" : "")}. " +
                        "Shift and Ctrl set the throttle, S lifts the nose, A and D bank and turn.";

        // The fuselage: a cockpit block, a seat on it, the spine behind and a boom behind that.
        var cockpit = v.Root("Frame Block", new Vector3(0, 1.6f, 0), Orient.Forward);
        v.Paint(cockpit, l.Body);
        var seat = Level(v, cockpit, "+y", "Seat", "bottom", forward: true);
        var spine = v.Onto(cockpit, "+z", Spine, "-z", spin: SpinUp(v, cockpit, "+z", Spine, "-z"));
        v.Paint(spine, l.Frame);
        var boom = v.Onto(spine, "+z", l.Boom >= 2 ? Spine : "Square Beam 1 m", "-z", spin: SpinUp(v, spine, "+z", l.Boom >= 2 ? Spine : "Square Beam 1 m", "-z"));
        v.Paint(boom, l.Frame);
        var tailEnd = l.Boom >= 2 ? 7 : 3;

        // The wing carrier: the fuel tank under or over the spine at the station, or the spine itself for a mid wing.
        var tank = v.Onto(spine, l.Wing == WingAt.High ? $"+y{station}" : $"-y{station}", "Fuel Tank", l.Wing == WingAt.High ? "-y" : "+y");
        v.Paint(tank, l.Body);
        VPart stab;
        if (l.Wing == WingAt.High)
        {
            stab = Level(v, tank, "+y", Stab, "-y", forward: true);
            v.Paint(v.Onto(spine, $"-y{station}", "Battery", "+y"), l.Trim);
        }
        else
        {
            stab = Level(v, spine, $"+y{(l.Power != Power.Pusher ? station : PylonAt(station) - 2)}", Stab, "-y", forward: true); // (a pusher's pylon stands there)
            v.Paint(v.Onto(tank, "-y", "Battery", "+y"), l.Trim);
        }
        v.Config(stab, ("keys", "Plane"), ("strength", 200), ("throttleCurve", l.Power == Power.Jet ? 100 : 0)); // (twice the stabilizer's gains: at the default its pitch lags the altitude hold's asks by 10 degrees, and the hold rocks 15 m up and down; two Light Turbojets push 24 kN, so a jet's throttle is cubed: full for the take-off, a third of it at a cruise of 0.7, else it cruises past 60 m/s, where the hold rocks again)
        v.Link("data", seat, stab);

        // The wings, root to tip a side: ailerons on the outer panels, flaps on the inner ones when it has them.
        var (rootL, rootR) = l.Wing == WingAt.Mid ? (($"-x{station}", spine), ($"+x{station}", spine)) : (("-x", tank), ("+x", tank));
        var left = Panels(v, l, rootL.Item2, rootL.Item1, "+x", -1);
        var right = Panels(v, l, rootR.Item2, rootR.Item1, "-x", 1);
        var aileronL = Level(v, left[^1], "back", "Aileron", "hinge", forward: true);
        var aileronR = Level(v, right[^1], "back", "Aileron", "hinge", forward: true);
        v.Paint(aileronL, l.Body); // (control surfaces in their host's colour: their ends are flush with the host's)
        v.Paint(aileronR, l.Body);
        v.Link("data", stab, aileronL, "left aileron");
        v.Link("data", stab, aileronR, "right aileron");
        if (l.Flaps)
            foreach (var w in (VPart[])[left[0], right[0]])
            {
                var flap = Level(v, w, "back", "Flap", "hinge", forward: true);
                v.Paint(flap, l.Body);
                v.Link("data", stab, flap, "flaps");
            }

        // The tail: a tail plane on the boom's end and a fin on it, or a fin on the boom and the tail plane on its top.
        VPart tailPlane, fin;
        if (l.TTail)
        {
            fin = Level(v, boom, $"+y{tailEnd}", "Tail Fin", "-y", forward: true);
            tailPlane = Level(v, fin, "+y", "Tail Plane", "-y", forward: true);
        }
        else
        {
            tailPlane = Level(v, boom, "+z", "Tail Plane", "front", forward: true);
            fin = Level(v, tailPlane, "+y", "Tail Fin", "-y", forward: true);
        }
        // A tail plane a side as well, each with its elevator: a 6 m tail, so the elevator can lift the nose off the runway at take-off speed.
        var tails = new List<VPart> { tailPlane };
        foreach (var side in (string[])["-x", "+x"])
            tails.Add(Level(v, tailPlane, side, "Tail Plane", side == "-x" ? "+x" : "-x", forward: true));
        v.Paint(fin, l.Body);
        foreach (var t in tails)
        {
            v.Paint(t, l.Body);
            var elevator = Level(v, t, "back", "Elevator", "hinge", forward: true);
            v.Paint(elevator, l.Body);
            v.Link("data", stab, elevator, "pitch");
        }
        var rudder = v.Onto(fin, "back", "Rudder", "hinge");
        v.Paint(rudder, l.Body);
        v.Link("data", stab, rudder, "yaw");

        // The engines, each fed from the tank, each prop on the stabilizer's throttle.
        var props = new List<VPart>();
        switch (l.Power)
        {
            case Power.Tractor:
            {
                var stub = v.Onto(cockpit, "-z", "Square Beam 0.5 m", "+z", spin: SpinUp(v, cockpit, "-z", "Square Beam 0.5 m", "+z")); // (a neck: the Turbine's sides would be flush with the cockpit block's)
                v.Paint(stub, l.Frame);
                var engine = v.Onto(stub, "-z", engineName, "-z");
                v.Paint(engine, l.Frame);
                props.Add(v.Onto(engine, "+z", propName, "base"));
                v.Link("pipe", tank, engine, "Fuel out", "Fuel in");
                break;
            }
            case Power.Jet:
                foreach (var w in (VPart[])[left[1], right[1]])
                {
                    var jet = Level(v, w, "-y", "Light Turbojet", "+y", forward: true); // (under the second panels: the stabilizer finds a thruster by itself)
                    v.Paint(jet, l.Frame);
                    v.Link("pipe", tank, jet, "Fuel out", "Fuel in");
                }
                break;
            case Power.Pusher:
            {
                var pylon = v.Onto(spine, $"+y{PylonAt(station)}", "Square Beam 1 m", "-z");
                v.Paint(pylon, l.Frame);
                var engine = Facing(v, pylon, "+z", engineName, "-y", Vector3.UnitZ);
                v.Paint(engine, l.Frame);
                props.Add(v.Onto(engine, "+z", propName, "base"));
                v.Link("pipe", tank, engine, "Fuel out", "Fuel in");
                break;
            }
        }
        for (var k = 0; k < props.Count; k++)
        {
            var pusher = l.Power == Power.Pusher;
            if (pusher)
                v.Config(props[k], ("pusher", 1)); // (it faces back and pushes towards its engine)
            v.Link("data", stab, props[k], "throttle");
        }

        // The gear: a nose leg under the cockpit and mains behind the wing, or mains under the cockpit and a tail wheel under the boom.
        if (l.Tricycle)
        {
            // A nose leg that stands the nose wheels level with the mains, which hang from the spine just behind the centre of mass, clear of the tank: the elevator can lift the nose.
            var nose = v.Onto(cockpit, "-y", "Square Beam 0.75 m", "+z");
            v.Paint(nose, l.Frame);
            var foot = v.Onto(nose, "-z", "Frame Block", "+y");
            v.Paint(foot, l.Frame);
            Wheels(v, foot);
            // The first free socket under the spine or boom at least a quarter metre behind it: close behind, so the elevator's moment beats the nose's weight on the mains.
            var at = Math.Max((int)MathF.Ceiling((com + MainsBehind - 0.375f) / 0.25f), 2); // (spine socket k sits at 0.375 + 0.25 k; the first two are over the nose leg's wheels)
            for (; ; at++)
            {
                if (at > Stations + tailEnd)
                    throw new VehicleKitException("no free socket for the mains under the spine or boom");
                if (Math.Abs(at - station) < 2)
                    continue; // (the tank, or a high wing's battery, hangs under the spine there)
                var (on, socket) = at < Stations ? (spine, $"-y{at}") : (boom, $"-y{at - Stations}");
                if (v.TurnOf(on, socket, "Square Beam 1 m", "+z", 0) == null)
                    continue; // (taken, or in the way of something hung there: the next one back)
                Mains(v, l, on, socket, "Square Beam 1 m");
                break;
            }
        }
        else
        {
            Mains(v, l, cockpit, "-y", "Square Beam 1 m"); // (taller than the tail wheel: it sits nose up, so its wing lifts it off as it gathers speed)
            var strut = v.Onto(boom, $"-y{tailEnd}", "Square Beam 0.5 m", "+z"); // (at the boom's end, well behind a wing set far back, and below the tail plane)
            v.Paint(strut, l.Frame);
            var foot = v.Onto(strut, "-z", "Frame Block", "+y");
            v.Paint(foot, l.Frame);
            Wheels(v, foot);
        }
    }

    /// <summary>A pusher's pylon: on the wing's station, but far enough back that its engine clears the seat.</summary>
    private static int PylonAt(int station) => Math.Max(station, 3);

    /// <summary>One side's panels, root to tip, onto <paramref name="socket"/> of <paramref name="on"/> by their <paramref name="by"/> socket; <paramref name="side"/> -1 left, 1 right.</summary>
    private static List<VPart> Panels(Vehicle v, Layout l, VPart on, string socket, string by, int side)
    {
        var list = new List<VPart>();
        var out_ = side < 0 ? "-x" : "+x";
        for (var k = 0; k < l.Panels; k++)
        {
            var w = k == 0 ? Level(v, on, socket, l.Panel, by, forward: true) : Level(v, list[^1], out_, l.Panel, by, forward: true);
            v.Paint(w, l.Body);
            list.Add(w);
        }
        return list;
    }

    /// <summary>A leg down from <paramref name="socket"/> and a cross beam with a wheel at each end, as the Light Plane's mains.</summary>
    private static void Mains(Vehicle v, Layout l, VPart on, string socket, string leg)
    {
        var post = v.Onto(on, socket, leg, "+z");
        v.Paint(post, l.Frame);
        var axle = Facing(v, post, "-z", Spine, "+y3", Vector3.UnitX); // (across the plane, so the wheels at its ends roll forward)
        v.Paint(axle, l.Frame);
        foreach (var end in (string[])["-z", "+z"])
            v.Onto(v.Onto(axle, end, "Suspension", "mount"), "axle", "Wheel", "axle");
    }

    /// <summary>A pair of wheels on a block's sides: a nose leg's or a tail wheel's.</summary>
    private static void Wheels(Vehicle v, VPart block)
    {
        foreach (var side in (string[])["-x", "+x", "-z", "+z"])
        {
            var i = Array.FindIndex(block.Def.Sockets, s => s.Name == side);
            if (MathF.Abs(Rot24.Apply(v.TurnOf(block), block.Def.Sockets[i].At).X) > 0.2f) // (the block's sides that face across the plane, however it was turned)
                v.Onto(v.Onto(block, side, "Suspension", "mount"), "axle", "Wheel", "axle");
        }
    }

    /// <summary>The spin that keeps a beam's +y sockets up as it runs back along the spine.</summary>
    private static int SpinUp(Vehicle v, VPart on, string socket, string part, string by)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f)
                return spin;
        throw new VehicleKitException($"a {part} won't run level off {on.Name}'s {socket}");
    }

    /// <summary>A part onto a socket, upright, and facing forward (-z) when <paramref name="forward"/>: a stabilizer reads its own frame, a seat faces the way it flies.</summary>
    private static VPart Level(Vehicle v, VPart on, string socket, string part, string by, bool forward)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f && (!forward || Vector3.Dot(Rot24.Apply(t, -Vector3.UnitZ), -Vector3.UnitZ) > 0.9f))
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't stand level on {on.Name}'s {socket}" + Why(v, on, socket, part, by));
    }

    /// <summary>TEMP: why each spin is refused.</summary>
    private static string Why(Vehicle v, VPart on, string socket, string part, string by)
    {
        var s = "";
        for (var spin = 0; spin < 4; spin++)
        {
            var t = v.TurnOf(on, socket, part, by, spin);
            s += $" | spin {spin}: " + (t is { } r ? $"up {Rot24.Apply(r, Vector3.UnitY)} z {Rot24.Apply(r, Vector3.UnitZ)}" : "refused");
        }
        return s;
    }

    /// <summary>A part onto a socket with its own +z along <paramref name="way"/>: a pusher's engine faces back.</summary>
    private static VPart Facing(Vehicle v, VPart on, string socket, string part, string by, Vector3 way)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitZ), way) > 0.9f)
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't face {way} on {on.Name}'s {socket}" + Why(v, on, socket, part, by));
    }
}
