using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The excavator, a mining vehicle (docs/vehiclekit/EXCAVATOR-notes.md; factory-generator-notes.md "Extraction must follow the ore"): a tracked undercarriage carrying
/// the engine, a house on a slewing turntable with the cab, and a boom, a stick and a Digging Bucket on motor hinges. A Drill Module on the stick's end chutes what it
/// drills into the bucket, a Small Genset at the house's back powers the motors, so the arm carries the drill over fresh ore and swings the load round to a hauler's open bed or a bin.
/// Built in its travelling pose: the boom upright, the stick out forward, the bucket hanging under it. The driver's seat drives the tracks (its Driving Module),
/// Q and E slew the house (the module's roll output into the turntable), three Analog Levers beside the seat set the boom, the stick and the bucket (a lever where
/// it starts is the pose it was built in), and a switch runs the drill. Any crew driver can take the seat.
/// </summary>
public sealed class Excavator(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Excavator";
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override VehicleCheckOptions? Checks => new() { MaxParts = 300, MaxMass = 16000, MinDistance = 8, MinTurn = 30, MinStability = 0.5f, MaxYawDrift = 20 }; // (stability: built with its boom upright, as excavators travel, on tracks 2.1 m apart)

    /// <summary>
    /// The joints' ranges, degrees from the pose it was built in. The boom's lever runs it from upright (0) forward and down to <see cref="BoomDown"/>: the stick then
    /// hangs straight down. The stick's and the bucket's levers start in their middles.
    /// </summary>
    public const float BoomDown = 95, StickSwing = 90, BucketSwing = 100;

    public override void Build(Vehicle v)
    {
        v.Description = "A mining excavator: tracks, a slewing house with the cab, a boom, stick and digging bucket, and a drill on the stick that chutes ore into the bucket. "
                        + "Drive it from the seat; Q and E slew the house; the levers right of the seat set the boom, the stick and the bucket (left to right); the switch runs the drill. "
                        + "Lower the drill onto a vein, then swing the bucket over a hauler's bed and tip it to pour.";
        var d = v.Dice.Fork("shape");
        v.Heavy();

        // ---- The undercarriage: rails, a Wide Differential at the back and a Track on a Stiff Suspension each side, the track reaching forward.
        var rails = d.Pick((float[])[3.25f, 3.5f, 3.75f]);
        v.Ladder(-1.675f, rails, 0.625f, 0.75f, "Wide Differential");
        foreach (var side in new[] { "-x", "+x" })
        {
            var spring = v.Onto(v.Diff!, side, "Stiff Suspension", "mount");
            VPart? track = null;
            for (var spin = 0; spin < 4 && track == null; spin++)
                if (v.TurnOf(spring, "axle", "Track", "axle", spin) is { } turn && Rot24.Apply(turn, Vector3.UnitZ).Z > 0.9f)
                    track = v.Onto(spring, "axle", "Track", "axle", spin); // (its sprocket at the back, the track reaching forward under the house)
            v.Wheels.Add(track ?? throw new VehicleKitException("no turn of the Track reaches forward from the differential"));
        }
        var trackMid = v.RearAxleZ - 2.15f;
        v.Powertrain(v.RailZ(v.RearAxleZ - 1.5f), "V8 Engine", "Automatic Gearbox");
        v.Onto(v.Engine!, "+x", "Alternator", "-x", 0);
        var tailSill = v.Sill(v.RailZ(v.RearAxleZ + 0.5f));
        var tank = v.Put("Fuel Tank", v.SocketPoint(tailSill, "+y2") + new Vector3(0, 0.25f, 0), Orient.Forward, 0.3f, tailSill, "-y");
        v.FuelFrom(tank);

        // ---- The house: a turntable on a three-layer sill ahead of the engine, a spine along it, a cross beam at its front, a deck.
        var houseZ = v.RailZ(trackMid - 0.5f + 0.25f * d.Int(0, 1));
        var pedestal = v.Sill(houseZ, 0.5f, 3); // (winged: a 0.75 m beam's ends would share the turntable's side planes)
        var table = v.Put("Turntable 3x3 Medium", new Vector3(0, Vehicle.SillTop(3) + 0.15f, houseZ), Orient.Upright, 0.2f, pedestal, "base");
        var top = v.SocketPoint(table, "top").Y;
        var spine = v.Put("Square Beam 2 m", new Vector3(0, top + Vehicle.Half, houseZ - 0.375f), Orient.Along(Vector3.UnitZ), 0.2f, table);
        var front = v.SocketPoint(spine, "-z").Z;
        var rear = v.SocketPoint(spine, "+z").Z;
        var cross = v.Put("Square Beam 2 m", new Vector3(0, top + Vehicle.Half, front - Vehicle.Half), Orient.Along(Vector3.UnitX), 0.2f, spine);
        var deckY = top + 2 * Vehicle.Half;

        // ---- The boom: a Motor Hinge 3x3 on the cross beam's right, its axis across, the boom standing up on its leaf. (Its 30 kN m holds the arm laid out
        // at a few degrees' give with a tier-1 drill; a Motor Hinge Medium would take a medium socket the house hasn't got.)
        var boomX = 0.625f + 0.125f * d.Int(0, 1);
        var boomHinge = v.Put("Motor Hinge 3x3 Medium", new Vector3(boomX, deckY + 0.375f, front - Vehicle.Half), new Orient(Vector3.UnitX, Vector3.UnitY, Vector3.UnitY, -Vector3.UnitX), 0.2f, cross, "base"); // (its axis to the left: a positive angle lays the boom forward)
        var boomLong = d.Pick((float[])[2.5f, 2.75f]); // (2 + 0.5, 2 + 0.75)
        var boomBase = v.SocketPoint(boomHinge, "leaf");
        var boom = Run(v, boomHinge, "leaf", boomBase, Vector3.UnitY, boomLong);
        // ---- The stick: a Motor Hinge 3x3 on the boom's front near its top, its leaf forward, the stick out forward from it.
        var stickHinge = v.Put("Motor Hinge 3x3 Medium", new Vector3(boomX, boomBase.Y + boomLong - 0.375f, front - Vehicle.Half - 0.125f - 0.375f), new Orient(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX), 0.2f, boom[^1], "base");
        var stickLong = d.Pick((float[])[1.75f, 2f]); // (lengths the beam run makes exactly: 1 + 0.75, 2)
        var stickBase = v.SocketPoint(stickHinge, "leaf");
        var stick = Run(v, stickHinge, "leaf", stickBase, -Vector3.UnitZ, stickLong);
        var stickEnd = stickBase.Z - stickLong;
        // ---- The bucket: a Motor Hinge 2x2 under the stick's last cell, its leaf down, the Digging Bucket hanging from it by its back, mouth to the cab.
        var bucketHinge = v.Stand("Motor Hinge 2x2 Medium", new Vector3(boomX, stickBase.Y - Vehicle.Half, stickEnd + Vehicle.Half), new Orient(Vector3.UnitX, -Vector3.UnitY, Vector3.UnitY, Vector3.UnitX), stick[^1], "base");
        var scoop = v.Onto(bucketHinge, "leaf", "Digging Bucket", "back", SpinFor(v, bucketHinge, "leaf", "Digging Bucket", "back", Vector3.UnitY, Vector3.UnitZ));
        // ---- The drill on the stick's top at its end, its bit forward past the end: with the boom down it stands upright over the ground ahead of the bucket.
        const string drillName = "Drill Module"; // (tier 1, 200 kg: the Workshop's 300 kg at the arm's end put the boom past its rating; swap it for copper)
        var drill = v.Stand(drillName, new Vector3(boomX, stickBase.Y + Vehicle.Half, stickEnd + 0.375f), new Orient(-Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY), stick[^1], "back", 0.3f);
        v.Link("chute", drill, scoop, "Chute out", null);

        // ---- The deck, round the boom's foot.
        var footHole = new Vehicle.Rect(boomX - 0.5f, boomX + 0.5f, front - 0.75f, front + 0.25f);
        // ---- The cab: a seat on a sill across the spine left of the boom, the style's cab shell over it, a Driving Module behind.
        var seatZ = front + 1.125f; // (the spine's socket rows are a quarter apart from its front end)
        var seatSill = v.Put("Square Beam 2 m", new Vector3(-0.125f, deckY + Vehicle.Half, seatZ), Orient.Along(Vector3.UnitX), 0.2f, spine);
        var shellSill = v.Put("Square Beam 2 m", new Vector3(-0.125f, deckY + Vehicle.Half, seatZ + 0.5f), Orient.Along(Vector3.UnitX), 0.2f, spine); // (the cab shell's feet)
        var sillHole = new Vehicle.Rect(-1.125f, 1.125f, seatZ - 0.25f, seatZ + 0.75f);
        var seat = v.Stand("Seat", new Vector3(-0.75f, deckY + 2 * Vehicle.Half, seatZ), Orient.Forward, seatSill, "bottom", 0.2f);
        v.Seats.Add(seat);
        var module = v.Onto(seat, "back", "Driving Module", "-z", 0);
        v.Wire(seat, module);
        v.Wire(module, v.Engine!);
        v.Link("data", module, table, "roll", null); // (Q and E slew the house: the turntable turns while its input is off zero and holds where it stops)
        // ---- The levers and the drill's switch, right of the seat on its sill.
        VPart Lever(float x, double low, double value)
        {
            var lever = v.Stand("Analog Lever", new Vector3(x, deckY + 2 * Vehicle.Half, seatZ), Orient.Forward, seatSill, "-y");
            v.Config(lever, ("low", low), ("high", 1.0), ("value", value));
            return lever;
        }
        var boomLever = Lever(-0.25f, 0, 0);
        var stickLever = Lever(0f, -1, 0.5);
        var bucketLever = Lever(0.25f, -1, 0.5);
        v.Config(boomHinge, ("min", 0.0), ("max", (double)BoomDown));
        v.Config(stickHinge, ("min", -(double)StickSwing), ("max", (double)StickSwing));
        v.Config(bucketHinge, ("min", -(double)BucketSwing), ("max", (double)BucketSwing));
        v.Wire(boomLever, boomHinge);
        v.Wire(stickLever, stickHinge);
        v.Wire(bucketLever, bucketHinge);
        var drillSwitch = v.Put("Switch", new Vector3(0.5f, deckY + 2 * Vehicle.Half + 0.1f, seatZ), Orient.Any, 0.2f, seatSill);
        v.Wire(drillSwitch, drill);
        // ---- The power pack at the house's back, its counterweight: the arm's motors draw tens of kilowatts while they move, far past an alternator's 1.5 kW,
        // so a Small Genset (100 kW) on its own fuel tank feeds the circuit, and the battery (which a drill needs in survival) rides on it.
        var genset = v.Onto(spine, "+z", "Small Genset", "-z", 0);
        var houseTank = v.Onto(genset, "+y", "Fuel Tank", "-y", 0);
        v.Link("pipe", houseTank, genset, null, "Fuel in", laid: true);
        var battery = v.Onto(genset, "-x", "Battery", "+x", 0);

        v.Deck("house", deckY, new Vehicle.Rect(-1.125f, 1.125f, front - Vehicle.Half, rear - 0.25f), "deck", [footHole, sillHole]);
        var shell = d.Fork("cab").Pick((string[])["Roll Cage", "Tin Roof Cab", "Chop Top Cab"]);
        v.Panels["cabshell"] = [v.Stand(shell, new Vector3(-0.5f, deckY + 2 * Vehicle.Half, seatZ + 0.5f), Orient.Forward, shellSill, "footL", 0.2f)];

        // ---- Paint.
        var paint = d.Fork("paint");
        v.PaintAll([.. boom, .. stick, .. v.Panels["house"]], "body", paint);
        v.PaintAll([boomHinge, stickHinge, bucketHinge, scoop, table, spine, cross, seatSill, shellSill], "frame", paint);
        v.PaintAll(v.Panels["cabshell"], "body", paint);
        v.PaintAll([battery, drillSwitch, genset], "frame", paint);
        v.PaintAll([houseTank], "body", paint);
    }

    /// <summary>Square beams end to end from <paramref name="on"/>'s <paramref name="socket"/> at <paramref name="from"/> along <paramref name="dir"/>, <paramref name="length"/> metres (2 m, then 1, 0.75, 0.5 m).</summary>
    private static List<VPart> Run(Vehicle v, VPart on, string socket, Vector3 from, Vector3 dir, float length)
    {
        var list = new List<VPart>();
        var left = length;
        var at = from;
        var last = on;
        while (left > 0.01f)
        {
            var m = left >= 2 ? 2f : left >= 1.75f ? 1f : left >= 1 ? 1f : left >= 0.75f ? 0.75f : 0.5f;
            if (left < 0.49f)
                throw new VehicleKitException($"a beam run of {length} m can't be made of 2, 1, 0.75 and 0.5 m beams");
            var name = m switch { 2f => "Square Beam 2 m", 1f => "Square Beam 1 m", 0.75f => "Square Beam 0.75 m", _ => "Square Beam 0.5 m" };
            last = v.Put(name, at + dir * (m * 0.5f), Orient.Along(dir), 0.05f, last);
            list.Add(last);
            at += dir * m;
            left -= m;
        }
        _ = socket;
        return list;
    }

    /// <summary>The spin that turns <paramref name="part"/>'s own <paramref name="own"/> to <paramref name="want"/> on <paramref name="on"/>'s socket.</summary>
    private static int SpinFor(Vehicle v, VPart on, string socket, string part, string by, Vector3 own, Vector3 want)
    {
        for (var spin = 0; spin < 4; spin++)
            if (v.TurnOf(on, socket, part, by, spin) is { } turn && Vector3.Dot(Rot24.Apply(turn, own), want) > 0.9f)
                return spin;
        throw new VehicleKitException($"no spin of the {part} on the {on.Name}'s {socket} turns it that way");
    }
}
