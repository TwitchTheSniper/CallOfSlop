using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The fluent layer: a road vehicle in the terms a generator thinks in. A ladder frame, axles, a powertrain, crew, decks and walls, a
/// turret, armour, cargo, paint and trophies, each worked out to sockets here. Every method fits real parts through
/// <see cref="BlueprintScript"/>, and the vehicle's coordinates are x right, y up from the rails' centre, -z forward.
/// A method keeps what later ones need (<see cref="Engine"/>, <see cref="Diff"/>...), so the order is frame, axles, powertrain, crew, body, dress.
/// </summary>
public sealed partial class Vehicle : BlueprintScript
{
    public const float Half = 0.125f; // half a square beam, and the socket grid's half step
    public const float RailX = 0.25f; // rails stand on a differential's and a cross beam's socket spacing
    public const string Beam = "Square Beam";

    public FactionStyle Style { get; }
    public Dice Dice { get; }
    public int Seed { get; }

    public Vehicle(string name, FactionStyle style, int seed) : base(name)
    {
        Style = style;
        Seed = seed;
        Dice = Dice.Of(seed);
        Group = style.Group;
    }

    // ---- The frame ------------------------------------------------------------------------------------------------

    /// <summary>A ladder frame's parts, as <see cref="Ladder"/> built them.</summary>
    public List<VPart> LeftRail { get; } = new();

    public List<VPart> RightRail { get; } = new();

    public VPart? NoseBeam { get; private set; }
    public float FrontAxleZ { get; private set; }
    public float RearAxleZ { get; private set; }
    public float NoseZ { get; private set; }
    public float TailZ { get; private set; }

    /// <summary>The beams, longest first, that make up <paramref name="length"/> (a multiple of 0.25 m).</summary>
    private static List<float> Lengths(float length, string what)
    {
        var left = (int)MathF.Round(length * 4);
        if (MathF.Abs(left - length * 4) > 0.01f || left <= 0)
            throw new VehicleKitException($"{what}: {length} m isn't a multiple of 0.25 m");
        var list = new List<float>();
        foreach (var (quarters, m) in new[] { (8, 2f), (4, 1f), (3, 0.75f), (2, 0.5f) })
            while (left >= quarters && left != quarters + 1)
            {
                list.Add(m);
                left -= quarters;
            }
        if (left != 0)
            throw new VehicleKitException($"{what}: {length} m can't be made of square beams");
        return list;
    }

    private static string BeamName(float m) => m switch { 2f => $"{Beam} 2 m", 1f => $"{Beam} 1 m", 0.75f => $"{Beam} 0.75 m", 0.5f => $"{Beam} 0.5 m", _ => throw new VehicleKitException($"no {m} m square beam") };

    /// <summary>A run of beams along z from <paramref name="z0"/> (a negative length runs forward), on <paramref name="first"/> (null: the vehicle's root).</summary>
    private void RailRun(List<VPart> rail, float x, float z0, float length, VPart? first)
    {
        var sign = MathF.Sign(length);
        var z = z0;
        foreach (var m in Lengths(MathF.Abs(length), "a rail"))
        {
            var centre = new Vector3(x, 0, z + sign * m * 0.5f);
            var along = Orient.Along(Vector3.UnitZ);
            rail.Add(rail.Count == 0 && first == null ? Root(BeamName(m), centre, along) : Put(BeamName(m), centre, along, 0.03f, rail.Count > 0 ? rail[^1] : first));
            z += sign * m;
        }
    }

    /// <summary>
    /// The frame: two rails 0.5 m apart at axle height, a cross beam carrying the front axle, the rear differential in line with the rails
    /// (it has sockets on the rails' spacing) and rails on behind it.
    /// <paramref name="railLength"/>: between the front cross beam and the differential. <paramref name="nose"/>, <paramref name="tail"/>: frame ahead of the front axle and behind the rear one.
    /// </summary>
    public Vehicle Ladder(float frontAxleZ, float railLength, float nose, float tail, string? differential = null)
    {
        var diffName = differential ?? Style.Part(Dice.Fork("drive"), "differential");
        const float diffHalf = 0.25f; // (its -z and +z sockets)
        var zs = frontAxleZ + Half;
        FrontAxleZ = frontAxleZ;
        RailRun(LeftRail, -RailX, zs, railLength - 0.25f, null); // (stops short: a differential's input port shares its -z0 socket, so that socket stays free)
        FrontCross = Put($"{Beam} 0.75 m", new Vector3(0, 0, frontAxleZ), Orient.Along(Vector3.UnitX), 0.03f, LeftRail[0]);
        RailRun(RightRail, RailX, zs, railLength, FrontCross);
        RearAxleZ = zs + railLength + diffHalf;
        Diff = Stand(diffName, new Vector3(RailX, 0, RearAxleZ - diffHalf), Orient.Forward, RightRail[^1], "-z1");
        RailRun(TailL, -RailX, RearAxleZ + diffHalf, tail - diffHalf, Diff);
        RailRun(TailR, RailX, RearAxleZ + diffHalf, tail - diffHalf, Diff);
        TailZ = RearAxleZ + tail;
        RailRun(NoseL, -RailX, frontAxleZ - Half, -(nose - Half), FrontCross);
        RailRun(NoseR, RailX, frontAxleZ - Half, -(nose - Half), FrontCross);
        NoseZ = frontAxleZ - nose;
        return this;
    }

    public List<VPart> NoseL { get; } = new();

    public List<VPart> NoseR { get; } = new();

    public List<VPart> TailL { get; } = new();

    public List<VPart> TailR { get; } = new();

    /// <summary>The front cross beam (the steering pivots hang on its ends).</summary>
    public VPart? FrontCross { get; private set; }

    /// <summary>The rear differential.</summary>
    public VPart? Diff { get; private set; }

    /// <summary>
    /// A cross beam on top of the rails at <paramref name="z"/>, with <paramref name="wing"/> metres of beam out past it on each side. <paramref name="layers"/>: beams stacked, each 0.25 m;
    /// a bed over the wheels stands on two. Returns the top beam's middle part. Its top is <see cref="SillTop"/>(layers).
    /// </summary>
    public VPart Sill(float z, float wing = 0, int layers = 1)
    {
        var y = Half * 2;
        var sill = Put($"{Beam} 0.75 m", new Vector3(0, y, z), Orient.Along(Vector3.UnitX), 0.03f);
        for (var k = 1; k < layers; k++)
        {
            y += Half * 2;
            sill = Put($"{Beam} 0.75 m", new Vector3(0, y, z), Orient.Along(Vector3.UnitX), 0.03f, sill);
        }
        foreach (var side in new[] { -1f, 1f })
        {
            var last = sill;
            var x = side * 0.375f;
            foreach (var m in wing > 0 ? Lengths(wing, "a sill's wing") : [])
            {
                last = Put(BeamName(m), new Vector3(x + side * m * 0.5f, y, z), Orient.Along(Vector3.UnitX), 0.03f, last);
                x += side * m;
            }
        }
        return sill;
    }

    /// <summary>The height of the top of a sill of so many layers.</summary>
    public static float SillTop(int layers) => Half + 2 * Half * layers;

    // ---- Axles --------------------------------------------------------------------------------------------------

    /// <summary>What hangs at each corner, for the check and for dressing.</summary>
    public List<VPart> Wheels { get; } = new();

    private VPart Corner(VPart mount, string socket, string spring, string wheel)
    {
        var s = Onto(mount, socket, spring, "mount");
        var w = Onto(s, "axle", wheel, "axle");
        Wheels.Add(w);
        return w;
    }

    /// <summary>The steered axle on the front cross beam: a Steering Pivot at each end, then the spring and the wheel.</summary>
    public Vehicle SteeredAxle(string? wheel = null, string? spring = null)
    {
        var d = Dice.Fork("axles");
        WheelName = wheel ?? Style.Part(d, "wheel");
        SpringName = spring ?? Style.Part(d, "spring");
        foreach (var side in new[] { "-z", "+z" })
        {
            var pivot = Onto(FrontCross!, side, "Steering Pivot", "base", 0);
            Corner(pivot, "leaf", SpringName, WheelName);
        }
        return this;
    }

    public string WheelName { get; private set; } = "";
    public string SpringName { get; private set; } = "";

    /// <summary>The driven axle: a spring and a wheel on each end of the differential.</summary>
    public Vehicle DrivenAxle()
    {
        foreach (var side in new[] { "-x", "+x" })
            Corner(Diff!, side, SpringName, WheelName);
        return this;
    }

    // ---- The powertrain -----------------------------------------------------------------------------------------

    public VPart? Engine { get; private set; }
    public VPart? Gearbox { get; private set; }

    /// <summary>An engine on a sill over the rails at <paramref name="z"/>, its gearbox behind it and a drive shaft to the differential.</summary>
    public Vehicle Powertrain(float z, string? engine = null, string? gearbox = null)
    {
        var d = Dice.Fork("drive");
        var en = engine ?? Style.Part(d, "engine");
        var gb = gearbox ?? Style.Part(d, "gearbox");
        var sill = Sill(z);
        Engine = Stand(en, new Vector3(0, 0.375f, z), Orient.Forward, sill, "-y");
        Gearbox = Onto(Engine, "+z", gb, "-z", 0);
        Link("shaft", Gearbox, Diff!, "Output shaft", "Input shaft");
        return this;
    }

    /// <summary>A fuel tank's pipe to the engine. <paramref name="nodes"/>: the way it runs; none: straight. A generator's pipe is laid: it may run through a hole in a plate, as a saved one may.</summary>
    public Vehicle FuelFrom(VPart tank, params Vector3[] nodes)
    {
        Tanks.Add(tank);
        Link("pipe", tank, Engine!, laid: true, nodes: nodes);
        return this;
    }

    public List<VPart> Tanks { get; } = new();

    // ---- Crew ----------------------------------------------------------------------------------------------------

    public VPart? DriverSeat { get; private set; }
    public VPart? DrivingModule { get; private set; }
    public List<VPart> Seats { get; } = new();

    /// <summary>The driver: a seat with its feet at <paramref name="at"/>, a Driving Module on its side, wired seat, module, engine.</summary>
    public Vehicle Driver(Vector3 at)
    {
        DriverSeat = Stand("Seat", at, Orient.Forward, null, "bottom");
        Seats.Add(DriverSeat);
        DrivingModule = Onto(DriverSeat, "+x", "Driving Module", "-x", 0);
        Wire(DriverSeat, DrivingModule);
        Wire(DrivingModule, Engine!);
        return this;
    }

    /// <summary>A passenger's seat: rides, drives nothing.</summary>
    public Vehicle Passenger(Vector3 at)
    {
        Seats.Add(Stand("Seat", at, Orient.Forward, null, "bottom"));
        return this;
    }

    public VPart? Gun { get; private set; }
    public VPart? GunnerSeat { get; private set; }
    public WeaponOption? Weapon { get; private set; }

    /// <summary>
    /// A manned turret on <paramref name="sill"/> at <paramref name="z"/>: a turntable, a beam along it, the gunner's seat on the beam with a Turret Module, a
    /// motor hinge for elevation and the gun on its leaf with its feed. The gunner's look points the joints and Fire fires it (docs/sandbox/CONSTRUCTS.md "Control modules").
    /// </summary>
    public Vehicle Turret(VPart sill, WeaponOption? weapon = null)
    {
        Weapon = weapon ?? Dice.Fork("weapon").Pick(Style.Weapons);
        var table = Stand("Turntable", new Vector3(0, CentreOf(sill).Y + Half, CentreOf(sill).Z), Orient.Upright, sill, "base");
        var arm = Stand($"{Beam} 1 m", SocketPoint(table, "top"), Orient.Along(Vector3.UnitZ), table, "-y1");
        GunnerSeat = Onto(arm, "+y3", "Seat", "bottom", 0);
        Seats.Add(GunnerSeat);
        var hinge = Onto(arm, "+y0", "Motor Hinge", "base", 0);
        Gun = Onto(hinge, "leaf", Weapon.Gun, Weapon.Mount, 0);
        if (Weapon.Feed != null)
            Onto(Gun, Weapon.FeedAt, Weapon.Feed, Weapon.FeedBy, 0);
        var module = Onto(GunnerSeat, "+x", "Turret Module", "-x", 0);
        Wire(GunnerSeat, module);
        Wire(module, Gun);
        return this;
    }

    /// <summary>Where a part's socket is, in vehicle coordinates.</summary>
    public Vector3 SocketPoint(VPart p, string socket)
    {
        var s = Array.FindIndex(C.DefOf(p.Index).Sockets, x => x.Name == socket);
        if (s < 0)
            throw new VehicleKitException($"a {p.Name} has no socket \"{socket}\"");
        C.SocketAt(p.Index, s, out var at, out _);
        return FromRest(at);
    }

    /// <summary>The middle of a part already on, in vehicle coordinates.</summary>
    public Vector3 CentreOf(VPart p)
    {
        C.PartBounds(p.Index, out var mn, out var mx);
        return FromRest((mn + mx) * 0.5f);
    }
}
