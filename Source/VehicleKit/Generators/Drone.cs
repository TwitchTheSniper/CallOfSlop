using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Multirotor drones (docs/vehiclekit/AIRCRAFT.md): a Flight Stabilizer flies whatever lifts it, so the shape is free. Two families by the seed:
/// a scout on a Drone Frame (four Drone Rotors, maybe pushers and a camera), and a multirotor on a hub of square beams: four, six or eight electric props or
/// ducted fans on plain arms or on T ends, a Battery Pack or two slung under the hub, legs, and a Drone Receiver or, on a big one, a seat.
/// Rotor hands alternate round the hub, so their torques cancel and the stabilizer yaws by them. No weapons yet.
/// </summary>
public sealed class Drone(FactionStyle style) : VehicleShape(style), IAircraftShape
{
    public override string Shape => "Drone";

    public override VehicleCheckOptions? Checks => new() { Flies = true, MinMass = 15, MaxMass = 2500, MaxSideFill = 1, MaxTopFill = 1, MinStability = 0 };

    /// <summary>None ship as files: drones are made when they spawn (no style names the shape in its extras yet).</summary>
    public override IReadOnlyList<int> Seeds => [];

    private const string Hub = "6-Way Hub", Tee = "T Junction", Stab = "Flight Stabilizer";

    public override void Build(Vehicle v)
    {
        var d = Mixed(v.Seed);
        var family = d.Fork("airframe").Next();
        if (family < 0.2)
            Scout(v, d);
        else if (family < 0.45)
            TubeQuad(v, d);
        else
            Multirotor(v, d);
    }

    /// <summary>
    /// The seed's dice, its bits mixed first: <see cref="Dice"/>'s first roll of a fork follows a seed's low bits, so seeds 4 and 5 rolled the same craft.
    /// Aircraft have no layers (<see cref="VehicleLayers"/>), so nothing routes these forks.
    /// </summary>
    private static Dice Mixed(int seed)
    {
        var z = unchecked((uint)seed * 0x9E3779B9u);
        z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
        z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
        return Dice.Of((int)(z ^ (z >> 16))).Fork("Drone");
    }

    // ---- The scout ---------------------------------------------------------------------------------------------------

    /// <summary>The Scout Drone's kind: a Drone Frame with its rotors under the arms and the rest on top.</summary>
    private void Scout(Vehicle v, Dice d)
    {
        v.Description = "A small scout drone on a Drone Frame. Fly it from a Remote Console on its channel: Shift climbs, Ctrl sinks, W A S D move and turn it.";
        var frame = v.Root("Drone Frame", new Vector3(0, 0.2f, 0), Orient.Forward);
        var paint = d.Fork("paint");
        v.Paint(frame, Style.Colour(paint, "frame"));
        foreach (var s in (string[])["r0", "r1", "r2", "r3"])
            v.Onto(frame, s, "Drone Rotor", "front");
        if (d.Fork("pushers").Chance(0.5))
            foreach (var s in (string[])["pushL", "pushR"])
                v.Onto(frame, s, "Drone Pusher", "front");
        var stab = Level(v, frame, "top", Stab, "-y", forward: true);
        Config(v, stab, d);
        v.Paint(v.Onto(frame, "topB", d.Fork("battery").Chance(0.6) ? "Drone Battery" : "Battery", "-y"), Style.Colour(paint, "body")); // (a Battery: heavier, a little longer in the air)
        v.Onto(frame, "topF", "Drone Receiver", "bottom");
        if (d.Fork("camera").Chance(0.7))
            v.Onto(frame, "nose", "Camera", "back");
    }

    // ---- The tube quad -------------------------------------------------------------------------------------------

    /// <summary>A home-made scout: a Battery for a hub, steel tube arms, Drone Rotors hung under their ends, the stabilizer on top.</summary>
    private void TubeQuad(Vehicle v, Dice d)
    {
        v.Description = "A home-made drone: four rotors on tube arms round a battery. Fly it from a Remote Console on its channel: Shift climbs, Ctrl sinks, W A S D move and turn it.";
        var paint = d.Fork("paint");
        var hub = v.Root("Battery", new Vector3(0, 0.4f, 0), Orient.Forward);
        v.Paint(hub, Style.Colour(paint, "body"));
        var arm = d.Fork("rig").Pick((string[])["Steel Tube 0.5 m", "Steel Tube 1 m"]);
        var last = arm == "Steel Tube 1 m" ? 3 : 1;
        var tubes = new List<(VPart Tube, Vector3 Out)>();
        foreach (var (socket, way) in (ReadOnlySpan<(string, Vector3)>)[("-z", -Vector3.UnitZ), ("+z", Vector3.UnitZ), ("-x", -Vector3.UnitX), ("+x", Vector3.UnitX)])
        {
            var tube = Along(v, hub, socket, arm, "end-", way);
            v.Paint(tube, Style.Colour(paint, "frame"));
            v.Onto(tube, $"-y{last}", "Drone Rotor", "front");
            tubes.Add((tube, way));
        }
        var stab = Level(v, hub, "+y", Stab, "-y", forward: true);
        Config(v, stab, d);
        v.Onto(tubes.First(t => t.Out.Z > 0).Tube, "+y1", "Drone Receiver", "bottom");
        if (d.Fork("camera").Chance(0.6))
            Level(v, tubes.First(t => t.Out.Z < 0).Tube, "-y0", "Camera", "+y", forward: true);
    }

    // ---- The multirotor ------------------------------------------------------------------------------------------

    /// <summary>The ways arms carry rotors: one at each arm's end, or two on a T at its end.</summary>
    private enum Layout { Plus, Hex, Octo, H }

    private void Multirotor(Vehicle v, Dice d)
    {
        var rig = d.Fork("rig");
        var layout = rig.Pick((Layout[])[Layout.Plus, Layout.Plus, Layout.Hex, Layout.Octo, Layout.H]);
        var heavy = rig.Chance(0.5);
        var fan = heavy ? (rig.Chance(0.7) ? "Ducted Fan" : "Electric Prop") : "Electric Prop";
        if (fan == "Electric Prop" && layout is Layout.Plus or Layout.H)
            layout = rig.Chance(0.5) ? Layout.Hex : Layout.Octo; // (four electric props barely lift the pack: six or eight)
        var arm = layout != Layout.Plus ? rig.Pick((float[])[1.5f, 2f]) : rig.Pick((float[])[1f, 1.5f, 2f]);
        var packs = heavy && fan == "Ducted Fan" && rig.Chance(0.5) ? 2 : 1;
        var manned = heavy && fan == "Ducted Fan" && arm >= 2 && rig.Chance(0.4);
        var count = layout switch { Layout.Plus => 4, Layout.H => 4, Layout.Hex => 6, _ => 8 };
        v.Description = $"A {(manned ? "crewed" : "remote")} multirotor, {count} {(fan == "Ducted Fan" ? "ducted fans" : "electric props")} on a hub. " +
                        (manned ? "Shift climbs, Ctrl sinks, W A S D move and turn it." : "Fly it from a Remote Console on its channel: Shift climbs, Ctrl sinks, W A S D move and turn it.");

        var paint = d.Fork("paint");
        var frameColour = Style.Colour(paint, "frame");
        var bodyColour = Style.Colour(paint, "body");
        var hub = v.Root(Hub, new Vector3(0, 1.2f, 0), Orient.Forward);
        v.Paint(hub, frameColour);

        // Arms: front, back, left, right, each a beam out of the hub with its +y sockets up. Which carry rotors, and how, is the layout's.
        var arms = new List<(VPart Beam, Vector3 Out)>();
        foreach (var (socket, way) in (ReadOnlySpan<(string, Vector3)>)[("mz110", -Vector3.UnitZ), ("pz112", Vector3.UnitZ), ("mx011", -Vector3.UnitX), ("px211", Vector3.UnitX)])
        {
            var along = layout == Layout.H && way.X != 0 ? 0 : arm; // (an H has no side arms)
            if (along <= 0)
                continue;
            var beam = Along(v, hub, socket, Beam(along), "-z", way);
            v.Paint(beam, frameColour);
            arms.Add((beam, way));
        }
        var rotors = new List<VPart>();
        var legsOn = new List<VPart>();
        var last = (int)MathF.Round(arm / 0.25f) - 1;
        foreach (var (beam, way) in arms)
        {
            var ends = layout switch
            {
                Layout.Plus => false,
                Layout.Hex => way.Z != 0, // (front and back arms end in a T: two rotors each, one on each side arm)
                _ => true,
            };
            if (!ends)
            {
                rotors.Add(v.Onto(beam, $"+y{last}", fan, "base"));
                legsOn.Add(beam);
                continue;
            }
            var tee = Along(v, beam, "+z", Tee, "mz100", way);
            v.Paint(tee, frameColour);
            foreach (var s in (string[])["px200", "mx000"])
            {
                var stub = Along(v, tee, s, Beam(0.75f), "-z", Rot24.Apply(v.TurnOf(tee), s == "px200" ? Vector3.UnitX : -Vector3.UnitX));
                v.Paint(stub, frameColour);
                rotors.Add(v.Onto(stub, "+y2", fan, "base"));
                legsOn.Add(stub); // (legs out at the T's ends: an H has no side arms to stand on)
            }
        }
        Hands(v, rotors);

        // Under the hub: the packs, and legs out on the arms (or the T's), longer than the packs hang.
        var pack = v.Onto(hub, "my101", "Battery Pack", "+y");
        v.Paint(pack, bodyColour);
        if (packs == 2)
            v.Paint(v.Onto(pack, "-y", "Battery Pack", "+y"), bodyColour);
        var leg = Beam(packs == 2 ? 1f : 0.75f);
        foreach (var beam in legsOn)
            v.Paint(v.Onto(beam, "-y1", leg, "-z"), frameColour);

        // On top: the stabilizer, level and facing forward; a receiver or a seat behind it; a camera ahead.
        var stab = Level(v, hub, "py121", Stab, "-y", forward: true);
        Config(v, stab, d);
        var back = arms.First(a => a.Out.Z > 0).Beam;
        if (manned)
            Level(v, back, "+y2", "Seat", "bottom", forward: true);
        else
            v.Onto(back, "+y0", "Drone Receiver", "bottom"); // (by the hub: a fan on a short arm reaches in to 0.75 m)
        if (d.Fork("camera").Chance(0.7))
            Level(v, arms.First(a => a.Out.Z < 0).Beam, "-y0", "Camera", "+y", forward: true);
    }

    private static string Beam(float metres) => metres switch { 0.75f => "Square Beam 0.75 m", 1f => "Square Beam 1 m", 1.5f => "Square Beam 1.5 m", _ => "Square Beam 2 m" };

    /// <summary>
    /// A beam or junction onto <paramref name="socket"/>, turned so it runs along <paramref name="way"/> (its own +z) with its own +y up: its rotor sockets face the sky.
    /// </summary>
    private static VPart Along(Vehicle v, VPart on, string socket, string part, string by, Vector3 way)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f && Vector3.Dot(Rot24.Apply(t, Vector3.UnitZ), way) > 0.9f)
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't go on {on.Name}'s {socket} running {way}");
    }

    /// <summary>A part onto a socket, upright, and facing forward (-z) when <paramref name="forward"/>: a stabilizer reads its own frame, a seat faces the way it flies.</summary>
    private static VPart Level(Vehicle v, VPart on, string socket, string part, string by, bool forward)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } t && Vector3.Dot(Rot24.Apply(t, Vector3.UnitY), Vector3.UnitY) > 0.9f && (!forward || Vector3.Dot(Rot24.Apply(t, -Vector3.UnitZ), -Vector3.UnitZ) > 0.9f))
                return v.Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"{part} won't stand level on {on.Name}'s {socket}");
    }

    /// <summary>Rotor hands alternate round the craft, by the angle of each rotor from the centre: opposite torques cancel, and the stabilizer yaws by them.</summary>
    private static void Hands(Vehicle v, List<VPart> rotors)
    {
        var round = rotors.OrderBy(r => { var p = v.CentreOf(r); return MathF.Atan2(p.X, -p.Z); }).ToList();
        for (var k = 0; k < round.Count; k++)
            v.Config(round[k], ("hand", k % 2));
    }

    /// <summary>The stabilizer's settings: rotorcraft keys; how hard it leans is the faction's temper, a little each way.</summary>
    private static void Config(Vehicle v, VPart stab, Dice d)
    {
        var lean = d.Fork("temper").Pick((int[])[15, 20, 25, 30]);
        v.Config(stab, ("keys", "Rotorcraft"), ("maxTilt", lean));
    }
}
