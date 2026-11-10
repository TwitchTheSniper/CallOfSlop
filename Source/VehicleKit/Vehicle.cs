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
    public Dice Dice { get; private set; }
    public int Seed { get; }

    /// <summary>Kit 3's layer seeds (<see cref="UseLayers"/>); null before kit 3 or without it.</summary>
    public VehicleLayers? Layers { get; private set; }

    private readonly Dice[] _layerRoots = new Dice[4];
    private string _stem = "";

    /// <summary>
    /// Kit 3: each layer forks from a root of its own seed (docs/vehiclekit/VEHICLEKIT.md, "Layered variety"), and <see cref="Dice"/>'s forks go to the layer their topic
    /// belongs to (<see cref="VehicleLayers.LayerOf"/>), so a method that forks "paint" or "weapon 2" rolls from the body's or the kit's seed. With no overrides every root is
    /// the seed's own, so a fork falls exactly as it would without layers.
    /// </summary>
    public Vehicle UseLayers(VehicleLayers layers)
    {
        Layers = layers;
        foreach (var l in VehicleLayers.All)
            _layerRoots[(int)l] = Dice.Of(layers.SeedOf(l, Seed)).Fork(_stem);
        Dice = _layerRoots[(int)VehicleLayer.Body].Routed(topic => _layerRoots[(int)VehicleLayers.LayerOf(topic)]);
        return this;
    }

    /// <summary>A layer's own root (kit 3); before <see cref="UseLayers"/>, the vehicle's dice.</summary>
    public Dice LayerDice(VehicleLayer l) => Layers == null ? Dice : _layerRoots[(int)l];

    /// <summary>Set when the front axle isn't under the hood's back (kit 3's chassis): the hood's fenders would stand over nothing.</summary>
    public bool NoFenders;

    /// <summary>Set by <see cref="Heavy"/>: wheels, springs and the powertrain come from the style's "heavy..." slots.</summary>
    public bool IsHeavy { get; private set; }

    /// <summary>A big vehicle: the chassis slots read "heavyWheel", "heavyEngine"... and the front axle needs <c>frontWing</c> to match a wide differential.</summary>
    public Vehicle Heavy()
    {
        IsHeavy = true;
        return this;
    }

    private string Slot(string name) => IsHeavy ? "heavy" + char.ToUpperInvariant(name[0]) + name[1..] : name;

    public Vehicle(string name, FactionStyle style, int seed) : base(name)
    {
        Style = style;
        Seed = seed;
        _stem = name.TrimEnd(' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        Dice = Dice.Of(seed).Fork(_stem); // (one seed number never means one recipe across shapes)
        Group = style.Group;
        TakeContext(); // (a spawn context, while one is being made in: VehicleContext.cs)
    }

    /// <summary>A vehicle whose name isn't "Name seed" (kit 3's overridden layers add to it): its dice still come from the generator's name.</summary>
    public Vehicle(string name, string stem, FactionStyle style, int seed) : base(name)
    {
        Style = style;
        Seed = seed;
        _stem = stem;
        Dice = Dice.Of(seed).Fork(stem);
        Group = style.Group;
        TakeContext();
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

    /// <summary>The tail rails' first socket row, behind the differential.</summary>
    public float TailGrid { get; private set; }

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
            rail.Add(rail.Count == 0 && first == null ? Root(BeamName(m), centre, along) : Put(BeamName(m), centre, along, 0.03f, z == z0 && first != null ? first : rail[^1]));
            z += sign * m;
        }
    }

    /// <summary>
    /// The frame: two rails 0.5 m apart at axle height, a cross beam carrying the front axle, the rear differential in line with the rails
    /// (it has sockets on the rails' spacing) and rails on behind it.
    /// <paramref name="railLength"/>: between the front cross beam and the differential. <paramref name="nose"/>, <paramref name="tail"/>: frame ahead of the front axle and behind the rear one.
    /// </summary>
    public Vehicle Ladder(float frontAxleZ, float railLength, float nose, float tail, string? differential = null, float frontWing = 0, float tandem = 0)
    {
        var diffName = differential ?? Style.Part(Dice.Fork("drive"), Slot("differential"));
        const float diffHalf = 0.25f; // (its -z and +z sockets)
        var zs = frontAxleZ + Half;
        FrontAxleZ = frontAxleZ;
        RailRun(LeftRail, -RailX, zs, railLength - 0.25f, null); // (stops short: a differential's input port shares its -z0 socket, so that socket stays free)
        FrontCross = Put($"{Beam} 0.75 m", new Vector3(0, 0, frontAxleZ), Orient.Along(Vector3.UnitX), 0.03f, LeftRail[0]);
        RailRun(RightRail, RailX, zs, railLength, FrontCross);
        RearAxleZ = zs + railLength + diffHalf;
        Diff = Stand(diffName, new Vector3(RailX, 0, RearAxleZ - diffHalf), Orient.Forward, RightRail[^1], "-z1");
        if (tandem > 0) // (a second driven axle: the rails break for it as they do for the first, the left one short of its input port)
        {
            RailRun(TailL, -RailX, RearAxleZ + diffHalf, tandem - diffHalf * 3, Diff);
            RailRun(TailR, RailX, RearAxleZ + diffHalf, tandem - diffHalf * 2, Diff);
            Diff2 = Stand(diffName, new Vector3(RailX, 0, RearAxleZ + tandem - diffHalf), Orient.Forward, TailR[^1], "-z1");
            RailRun(TailL, -RailX, RearAxleZ + tandem + diffHalf, tail - tandem - diffHalf, Diff2);
            RailRun(TailR, RailX, RearAxleZ + tandem + diffHalf, tail - tandem - diffHalf, Diff2);
            TailGrid = RearAxleZ + tandem + diffHalf + 0.125f;
        }
        else
        {
            RailRun(TailL, -RailX, RearAxleZ + diffHalf, tail - diffHalf, Diff);
            RailRun(TailR, RailX, RearAxleZ + diffHalf, tail - diffHalf, Diff);
            TailGrid = RearAxleZ + diffHalf + 0.125f;
        }
        TailZ = RearAxleZ + tail;
        RailRun(NoseL, -RailX, frontAxleZ - Half, -(nose - Half), FrontCross);
        RailRun(NoseR, RailX, frontAxleZ - Half, -(nose - Half), FrontCross);
        NoseZ = frontAxleZ - nose;
        _steerEnds.Clear();
        foreach (var side in new[] { -1f, 1f })
        {
            var end = FrontCross;
            var x = side * 0.375f;
            foreach (var m in frontWing > 0 ? Lengths(frontWing, "the front axle's wing") : [])
            {
                end = Put(BeamName(m), new Vector3(x + side * m * 0.5f, 0, frontAxleZ), Orient.Along(Vector3.UnitX), 0.03f, end);
                x += side * m;
            }
            _steerEnds.Add((end, side));
        }
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
        var s = Onto(mount, socket, spring, BoltBy(Def(spring)));
        var w = Onto(s, "axle", wheel, "axle");
        Wheels.Add(w);
        return w;
    }

    private readonly List<(VPart End, float Side)> _steerEnds = new();

    /// <summary>The steered axle on the front cross beam: a Steering Pivot at each end, then the spring and the wheel.</summary>
    public Vehicle SteeredAxle(string? wheel = null, string? spring = null)
    {
        var d = Dice.Fork("axles");
        WheelName = wheel ?? Style.Part(d, Slot("wheel"));
        SpringName = spring ?? Style.Part(d, Slot("spring"));
        foreach (var (end, side) in _steerEnds)
        {
            var socket = end == FrontCross ? (side < 0 ? "-z" : "+z") : FreeEnd(end);
            var pivot = Onto(end, socket, "Steering Pivot", "base", 0);
            Corner(pivot, "leaf", SpringName, WheelName);
        }
        return this;
    }

    private string FreeEnd(VPart beam) => C.SocketUsed(beam.Index, Array.FindIndex(beam.Def.Sockets, s => s.Name == "+z")) ? "-z" : "+z";

    public string WheelName { get; private set; } = "";
    public string SpringName { get; private set; } = "";

    /// <summary>The driven axle: a spring and a wheel on each end of the differential.</summary>
    public Vehicle DrivenAxle(string? wheel = null, string? spring = null)
    {
        foreach (var side in new[] { "-x", "+x" })
        {
            Corner(Diff!, side, spring ?? SpringName, wheel ?? WheelName);
            if (Diff2 != null)
                Corner(Diff2, side, spring ?? SpringName, wheel ?? WheelName);
        }
        return this;
    }

    // ---- The powertrain -----------------------------------------------------------------------------------------

    public VPart? Diff2 { get; private set; }

    public VPart? Engine { get; private set; }

    /// <summary>What a hood stands on: the engine, or off world a riser on the smaller Electric Motor (VehicleContext.cs).</summary>
    public VPart? HoodOn { get; private set; }
    public VPart? Gearbox { get; private set; }

    /// <summary>An engine on a sill over the rails at <paramref name="z"/>, its gearbox behind it and a drive shaft to the differential.</summary>
    public Vehicle Powertrain(float z, string? engine = null, string? gearbox = null)
    {
        var d = Dice.Fork("drive");
        var en = engine ?? Style.Part(d, Slot("engine"));
        var gb = gearbox ?? Style.Part(d, Slot("gearbox"));
        var sill = Sill(z);
        Engine = Stand(en, new Vector3(0, 0.375f, z), Orient.Forward, sill, "-y");
        HoodOn = Engine;
        if (engine == null && Def(en).FuelKind == EngineFuel.Electric && Def(en).BoundsMax.Y - Def(en).BoundsMin.Y < 0.45f)
            HoodOn = Onto(Engine, "+y", "Drone Battery", "-y", 0); // (a spawn context off world: the Electric Motor is 10 cm lower than an engine, so a hood stands on a Drone Battery on its top, within 2 cm of where it always does)
        Gearbox = Onto(Engine, "+z", gb, "-z", 0);
        if (Diff2 != null)
        {
            var split = Onto(Gearbox, "+z", "T Gearbox", "-z", 1); // (on its side: its branch would land on the seat's sill)
            var back = CentreOf(Diff2).Z;
            Link("shaft", split, Diff!, "Output shaft", "Input shaft");
            var from = CentreOf(split).Z;
            Vector3[] run = back - 0.5f - from > 7.9f // (a shaft's segment reaches at most 8 m: a long kit-3 wheelbase takes a node halfway; anything shorter is laid as it always was)
                ? [new Vector3(0.45f, CentreOf(split).Y, from), new Vector3(0.45f, -0.4f, from), new Vector3(0.45f, -0.4f, (from + back - 0.5f) / 2), new Vector3(0.45f, -0.4f, back - 0.5f), new Vector3(0, -0.4f, back - 0.5f), new Vector3(0, 0, back - 0.5f)]
                : [new Vector3(0.45f, CentreOf(split).Y, from), new Vector3(0.45f, -0.4f, from), new Vector3(0.45f, -0.4f, back - 0.5f), new Vector3(0, -0.4f, back - 0.5f), new Vector3(0, 0, back - 0.5f)];
            Link("shaft", split, Diff2, "Branch shaft", "Input shaft", laid: true, nodes: run);
        }
        else
            Link("shaft", Gearbox, Diff!, "Output shaft", "Input shaft");
        return this;
    }

    /// <summary>A fuel tank's pipe to the engine. <paramref name="nodes"/>: the way it runs; none: straight. A generator's pipe is laid: it may run through a hole in a plate, as a saved one may.</summary>
    public Vehicle FuelFrom(VPart tank, params Vector3[] nodes)
    {
        Tanks.Add(tank);
        if (!Electric) // (an Electric Motor burns nothing: the tank stays, unpiped)
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
        DrivingModule = Diff2 != null ? Onto(DriverSeat, "-x", "Driving Module", "+x", 0) : Onto(DriverSeat, "+x", "Driving Module", "-x", 0); // (a 6x6's T Gearbox stands beside the seat)
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

    /// <summary>Every gun with its seat, as <see cref="Turret"/> fitted them.</summary>
    public List<(VPart Gun, VPart Seat, WeaponOption Weapon)> Turrets { get; } = new();

    /// <summary>
    /// A manned turret on <paramref name="mount"/> (a sill, or anything with a free socket facing up): a turntable, a beam along it, a motor hinge for elevation and a gun on its leaf with its Turret Module.
    /// The gunner's seat rides the beam, or is <paramref name="seat"/>, anywhere within a wire of the gun: one inside a room works a roof gun and looks all the way round
    /// (docs/sandbox/CONSTRUCTS.md "Control modules"). <paramref name="weapon"/> null: the style's, by the seed.
    /// </summary>
    /// <summary>Set before <see cref="Turret"/> on a vehicle that calls <see cref="Feeds"/>: its guns have no magazine or box of their own.</summary>
    public bool FedTurrets;

    public Vehicle Turret(VPart mount, WeaponOption? weapon = null, VPart? seat = null, bool aft = false)
    {
        Weapon = weapon ?? (Role is { } r ? r.Pick(Dice.Fork(Turrets.Count == 0 ? "weapon" : "weapon " + Turrets.Count), Style.Weapons) : VehicleArmoury.Pick(Dice.Fork(Turrets.Count == 0 ? "weapon" : "weapon " + Turrets.Count), Style.Weapons));
        aft &= Weapon.Name != "Whaler"; // (its magazine hangs where an aft gun's seat goes)
        if (CentreOf(mount).Y < 1.5f && CentreOf(mount).Z - GunReach(Weapon) < CabBack) // (a long gun on a bed would poke through the cab: a pedestal lifts it over the cab's roof, as on a real technical)
        {
            var cab = Panels.GetValueOrDefault("cabshell")?.FirstOrDefault();
            var over = (cab == null ? 0f : CentreOf(cab).Y - (cab.Def.BoundsMin.Y + cab.Def.BoundsMax.Y) * 0.5f + cab.Def.BoundsMax.Y) + 0.2f - (CentreOf(mount).Y + 0.9f);
            mount = Stand($"{Beam} {(over <= 0.5f ? "0.5" : over <= 0.75f ? "0.75" : "1")} m", NearestUp(mount, CentreOf(mount)), Orient.Along(Vector3.UnitY), mount, "-z");
        }
        C.PartBounds(mount.Index, out _, out var top);
        var mid = CentreOf(mount);
        var table = TurretTable(mount, new Vector3(mid.X, FromRest(top).Y, mid.Z));
        var arm = Stand($"{Beam} 1 m", SocketPoint(table, "top"), aft ? new Orient(Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY) : Orient.Along(Vector3.UnitZ), table, "-y1");
        // Every piece is turned as the arm is (an aft arm faces back): the seat faces where the gun points at rest, never across it (a tester's report).
        GunnerSeat = seat ?? Onto(arm, "+y3", "Seat", "bottom", SpinLike(arm, "+y3", "Seat", "bottom", arm, Vector3.UnitX, Vector3.UnitY));
        Seats.Add(GunnerSeat);
        var hinge = TurretHinge(arm, aft);
        Gun = FitGun(hinge, Weapon, SpinLike(hinge, hinge.Def.HasJoint ? hinge.Def.LeafSocket : "top", GunDef(WeaponOption.Parse(Weapon.Build).Part).Name, "bottom", arm, Vector3.UnitX, Vector3.UnitY, aft ? 2 : 0));
        var module = seat == null ? Onto(GunnerSeat, "+x", "Turret Module", "-x", 0) : Onto(GunnerSeat, "back", "Turret Module", "-z", 0); // (a seat in a room keeps its module out of the aisle)
        Wire(GunnerSeat, module);
        Wire(module, Gun);
        if (TwinGun is { } twin)
            Wire(module, twin); // (one gunner lays both: the cradle's second gun fires with the first)
        if (Weapon.Mount == "shield")
            GunShield(arm);
        Turrets.Add((Gun, GunnerSeat, Weapon));
        return this;
    }

    /// <summary>Which turntables and elevation hinges the kit's turrets are given, by name, with how many of each (for the reports).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> TurretJoints = new();

    /// <summary>
    /// A turret's turntable by its gun (<see cref="Weapon"/>): a light gun turns fast on a small table (180 degrees a second, 3000 N m), a medium one on a
    /// 2x2 (90, 12000), a heavy or merged one on the slow, heavy Turntable (45). Highland and British light guns take a steadier 1x1 Medium half the time.
    /// A light gun on a pedestal beam takes the 2x2 (a 1x1 there would share the beam's faces). Where the chosen one has no room the Turntable goes on.
    /// </summary>
    private VPart TurretTable(VPart mount, Vector3 top)
    {
        var cls = VehicleRoles.ClassOf(Weapon!);
        C.PartBounds(mount.Index, out var lo, out var hi);
        if (cls == GunClass.Light && MathF.Min(hi.X - lo.X, hi.Z - lo.Z) < 0.3f)
            cls = GunClass.Medium; // (a 1x1 table on a pedestal beam as wide as it would share its four sides: a 2x2 overhangs it)
        var name = cls switch
        {
            GunClass.Light => Style.Name is "Highland" or "British" && Dice.Fork("turntable " + Turrets.Count).Chance(0.5) ? "Turntable 1x1 Medium" : "Turntable 1x1 Long",
            GunClass.Medium => "Turntable 2x2 Medium",
            _ => "Turntable",
        };
        if (name != "Turntable")
            try
            {
                return Counted(Put(name, top + new Vector3(0, HalfHeight(name), 0), Orient.Upright, 0.65f, mount, "base"));
            }
            catch (VehicleKitException)
            {
                // (no room for it there: the plain one)
            }
        return Counted(Put("Turntable", top + new Vector3(0, HalfHeight("Turntable"), 0), Orient.Upright, 0.65f, mount, "base"));
    }

    /// <summary>A turret's elevation hinge: a light gun's lifts fast (Motor Hinge 1x1 Long, 180 degrees a second), the rest on the Motor Hinge (90). Turned as the arm is.</summary>
    private VPart TurretHinge(VPart arm, bool aft)
    {
        if (VehicleRoles.ClassOf(Weapon!) == GunClass.Light)
            try
            {
                return Counted(Onto(arm, "+y0", "Motor Hinge 1x1 Long", "base", SpinLike(arm, "+y0", "Motor Hinge 1x1 Long", "base", arm, Vector3.UnitY, -Vector3.UnitX, aft ? 2 : 0)));
            }
            catch (VehicleKitException)
            {
                // (no room for it there: the plain one)
            }
        return Counted(Onto(arm, "+y0", "Motor Hinge", "base", SpinLike(arm, "+y0", "Motor Hinge", "base", arm, Vector3.UnitY, -Vector3.UnitX, aft ? 2 : 0)));
    }

    private static VPart Counted(VPart p)
    {
        TurretJoints.AddOrUpdate(p.Def.Name, 1, (_, n) => n + 1);
        return p;
    }

    private static float HalfHeight(string part) => (Def(part).BoundsMax.Y - Def(part).BoundsMin.Y) * 0.5f;

    /// <summary>
    /// The spin that fits <paramref name="part"/> on <paramref name="on"/>'s <paramref name="socket"/> turned as <paramref name="like"/> is, from the turn it takes on an
    /// unturned one (its X and Y going to <paramref name="x"/> and <paramref name="y"/>); <paramref name="fallback"/> when no spin gives that.
    /// </summary>
    private int SpinLike(VPart on, string socket, string part, string by, VPart like, Vector3 x, Vector3 y, int fallback = 0)
    {
        var turn = TurnOf(like);
        var wantX = Rot24.Apply(turn, x);
        var wantY = Rot24.Apply(turn, y);
        for (var spin = 0; spin < 4; spin++)
            if (TurnOf(on, socket, part, by, spin) is { } r && Vector3.DistanceSquared(Rot24.Apply(r, Vector3.UnitX), wantX) < 0.01f && Vector3.DistanceSquared(Rot24.Apply(r, Vector3.UnitY), wantY) < 0.01f)
                return spin;
        return fallback;
    }

    /// <summary>Guns on a fed turret that carry their own magazines (twin mounts): <see cref="Feeds"/> lays no belt to them.</summary>
    public HashSet<int> SelfFed { get; } = new();

    /// <summary>A twin mount's second gun, when the last <see cref="FitGun"/> made one (null otherwise).</summary>
    public VPart? TwinGun { get; private set; }

    /// <summary>
    /// A gun shield (an armoury recipe's "shield" mount): a half-metre plate standing on the arm between the gun and the gunner, below the gun's line, so it turns with the
    /// turntable and covers the gunner's chest from ahead. Where the arm has no room for it the gun goes on without (the mount is a look, not a need).
    /// </summary>
    private void GunShield(VPart arm)
    {
        const string plate = "Plate Steel 0.5 m";
        var along = Rot24.Apply(TurnOf(arm), Vector3.UnitZ);
        foreach (var socket in (string[])["+y1", "+y2"])
            for (var spin = 0; spin < 4; spin++)
            {
                if (TurnOf(arm, socket, plate, "-z0", spin) is not { } turn || MathF.Abs(Vector3.Dot(Rot24.Apply(turn, Vector3.UnitY), along)) < 0.9f)
                    continue; // (refused there, or stood across the arm rather than facing along it)
                try
                {
                    Paint(Onto(arm, socket, plate, "-z0", spin, clear: true), Style.Colour(Dice.Fork("shield"), "body"));
                    return;
                }
                catch (VehicleKitException)
                {
                    // (a face it would share with the arm or the gun: the next turn or socket)
                }
            }
    }

    private const float CabBack = 0.25f; // (the back of a cab shell, and the front wall of a room behind it)

    /// <summary>How far a gun reaches ahead of its mount: its receiver and barrel run, and a muzzle device on the barrel.</summary>
    public float GunReach(WeaponOption w)
    {
        var (receiver, children) = WeaponOption.Parse(w.Build);
        var n = Math.Max(1, w.Merge); // (a merged receiver and its gun parts are N times the size: ConstructMerge.cs)
        var reach = 0.6f + n * Size(GunDef(receiver));
        foreach (var (socket, value) in children)
            if (socket.Equals("barrel", StringComparison.OrdinalIgnoreCase))
            {
                var (barrel, on) = WeaponOption.Parse(value);
                reach += n * Size(GunDef(barrel));
                reach += n * on.Where(c => c.Key.Equals("muzzle", StringComparison.OrdinalIgnoreCase)).Sum(c => Size(GunDef(WeaponOption.Parse(c.Value).Part)));
                reach += n * on.Where(c => c.Key.StartsWith("barrel_", StringComparison.OrdinalIgnoreCase)).Select(c => Size(GunDef(WeaponOption.Parse(c.Value).Part))).DefaultIfEmpty().Max(); // (a splitter's barrels side by side: the longest)
            }
        return reach;
        static float Size(ConstructPartDef d) => d.BoundsMax.Z - d.BoundsMin.Z;
    }

    /// <summary>
    /// A gun from its parts, by its build text, on <paramref name="on"/>'s leaf or free socket: the receiver, then each part as a construct part on the receiver's matching face
    /// (barrel on the front, the others on the first free face), a muzzle device on the barrel, the box on the feed. The game's build rules then judge the gun:
    /// a part they refuse throws with their reason. Builder names work whatever part wraps them ("Workhorse Receiver" is the hand-made Rifle Receiver).
    /// </summary>
    public VPart FitGun(VPart on, WeaponOption w, int spin = 0)
    {
        var (receiver, children) = WeaponOption.Parse(w.Build); // (GunParts leaves out "ammo": a mounted gun's rounds are chosen by what feeds it; and on a fed turret the feed, which Feeds lays)
        var rec = Onto(on, on.Def.HasJoint ? on.Def.LeafSocket : "top", GunDef(receiver).Name, "bottom", spin);
        TwinGun = null;
        if (w.Merge > 1)
            MergeBlock(rec, w);
        else if (w.Mount == "twin")
            TwinGun = OntoTurned(rec, "right", GunDef(receiver).Name, "left", w); // (the cradle's second receiver, side by side: a 1x2 doesn't merge)
        var fed = FedTurrets;
        if (TwinGun != null)
        {
            FedTurrets = false; // (a twin keeps a magazine and box on each gun: the cradle has no free face for a belt's socket, so the magazine's belt skips it: SelfFed)
            if (fed)
                SelfFed.Add(rec.Index);
        }
        GunParts(rec, children, w);
        if (TwinGun != null)
            GunParts(TwinGun, WeaponOption.Parse(w.Build).Children, w);
        FedTurrets = fed;
        return rec;
    }

    /// <summary>A gun's parts on its receiver (a merged one's centre sockets), the box on its feed, and the build rules' word on it.</summary>
    private void GunParts(VPart rec, Dictionary<string, string> children, WeaponOption w)
    {
        children.Remove("ammo");
        if (FedTurrets)
            children.Remove("feed");
        VPart? feed = null;
        foreach (var (socket, value) in children)
        {
            var (part, sub) = WeaponOption.Parse(value);
            var faces = socket.ToLowerInvariant() switch
            {
                "barrel" => ["front"],
                "feed" => (string[])["left", "right", "top", "back"],
                "ammo" => ["right", "left", "top", "back"],
                "optic" => ["top", "right", "left"],
                "action" => ["back", "top", "right", "left"],
                _ => throw new VehicleKitException($"gun \"{w.Name}\": no place for its {socket} socket on a construct (barrel, feed, ammo, optic, action)"),
            };
            var p = OnAFace(rec, faces, GunDef(part).Name, w);
            if (socket.Equals("feed", StringComparison.OrdinalIgnoreCase))
                feed = p;
            if (sub.TryGetValue("muzzle", out var muzzle))
                Onto(p, "front", GunDef(muzzle).Name, "back", 0);
            foreach (var (bs, bv) in sub)
                if (bs.StartsWith("barrel_", StringComparison.OrdinalIgnoreCase) && int.TryParse(bs.AsSpan(7), out var nth))
                {
                    // A splitter's barrels: the Nth on its "front-N" output (a generated splitter has one a builder output), a muzzle device on each.
                    var (sbarrel, ssub) = WeaponOption.Parse(bv);
                    var outs = p.Def.Sockets.Where(x => x.Name.StartsWith("front-", StringComparison.Ordinal)).Select(x => x.Name).ToArray(); // ("front-N" on a generated splitter; a hand-made one's, "front-l" and "front-r", in order)
                    var outName = Array.IndexOf(outs, $"front-{nth}") >= 0 ? $"front-{nth}" : nth >= 1 && nth <= outs.Length ? outs[nth - 1] : throw new VehicleKitException($"gun \"{w.Name}\": the {p.Name} has no output {nth}");
                    var b = Onto(p, outName, GunDef(sbarrel).Name, "back", 0);
                    if (ssub.TryGetValue("muzzle", out var sm))
                        Onto(b, "front", GunDef(sm).Name, "back", 0);
                }
        }
        if (w.Box != null && !FedTurrets)
        {
            // The box on its magazine, else on the receiver or (merged) any of its cells: a box on any of them feeds the gun (ConstructWeapons.cs).
            var hosts = new List<VPart> { feed ?? rec };
            if (feed != null)
                hosts.Add(rec);
            hosts.AddRange(Cells(rec).Where(x => x.Index != rec.Index));
            var whyNot = "";
            VPart? box = null;
            foreach (var h in hosts)
                try
                {
                    box = OnAFace(h, ["left", "top", "right", "back", "bottom"], w.Box, w);
                    break;
                }
                catch (VehicleKitException ex)
                {
                    whyNot = whyNot.Length == 0 ? ex.Message : whyNot; // (the magazine's reasons: where it was wanted first)
                }
            if (box == null)
                throw new VehicleKitException(whyNot);
        }
        C.Rebuild();
        for (var g = 0; g < C.GunCount && !FedTurrets; g++)
            if (C.Guns[g].Part == rec.Index && C.Guns[g].Problem.Length > 0)
                throw new VehicleKitException($"gun \"{w.Name}\": {C.Guns[g].Problem}");
    }

    /// <summary>
    /// A merged receiver (docs/sandbox/CONSTRUCTS.md, "Merged receivers"): the rest of an NxN square of the lead's type beside it, to its right and above it, turned as it is,
    /// so the game merges them into one receiver N times the size whose centre sockets take the gun's parts (drawn N times bigger). Throws when they don't merge.
    /// </summary>
    private void MergeBlock(VPart lead, WeaponOption w)
    {
        var n = w.Merge;
        var cells = new VPart[n, n];
        cells[0, 0] = lead;
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                if (x + y > 0)
                    cells[x, y] = y == 0 ? OntoTurned(cells[x - 1, 0], "right", lead.Name, "left", w) : OntoTurned(cells[x, y - 1], "top", lead.Name, "bottom", w);
        C.Rebuild();
        if (!C.MergeLead(lead.Index) || C.Merges[C.MergeOf[lead.Index] - 1].N != n)
            throw new VehicleKitException($"gun \"{w.Name}\": its {n * n} {lead.Name}s didn't merge into a {n}x{n}");
    }

    /// <summary>A merged receiver's cells, its lead first (just the receiver when it isn't merged).</summary>
    public List<VPart> Cells(VPart rec)
    {
        var list = new List<VPart> { rec };
        if (C.MergeOf[rec.Index] == 0)
            return list;
        foreach (var p in Parts)
            if (p.Index != rec.Index && p.Index < C.PartCount && C.MergeOf[p.Index] == C.MergeOf[rec.Index])
                list.Add(p);
        return list;
    }

    /// <summary>A gun part on <paramref name="on"/>'s socket turned exactly as <paramref name="on"/> is (receivers merge and pair only turned alike).</summary>
    private VPart OntoTurned(VPart on, string socket, string part, string by, WeaponOption w)
    {
        var want = TurnOf(on);
        for (var spin = 0; spin < 4; spin++)
            if (TurnOf(on, socket, part, by, spin) == want)
                return Onto(on, socket, part, by, spin);
        throw new VehicleKitException($"gun \"{w.Name}\": no turn of a {part} on the {on.Name}'s \"{socket}\" matches it");
    }

    /// <summary>A part on the first of a gun part's faces it fits: which face holds what doesn't matter to the gun, and a hinge or a seat may be in the way of some.</summary>
    private VPart OnAFace(VPart on, string[] faces, string part, WeaponOption w)
    {
        var why = "";
        var names = (string[])["left", "right", "top", "bottom", "back", "front"];
        var centre = C.MergeLead(on.Index) && Def(part).Role == PartRole.GunPart; // (a merged receiver takes gun parts on its centre sockets only)
        foreach (var face in faces)
        {
            if (!Array.Exists(on.Def.Sockets, s => s.Name == face))
                continue;
            var by = names[Array.IndexOf(names, face) ^ 1];
            try
            {
                return centre ? OntoCentre(on, face, part, by) : Onto(on, face, part, by, 0);
            }
            catch (VehicleKitException ex)
            {
                why = why.Length == 0 ? ex.Message : why + "; " + ex.Message; // (every face's reason: the last alone hid a refusal by the rules behind an "in the way")
            }
        }
        throw new VehicleKitException($"gun \"{w.Name}\": {why}");
    }

    /// <summary>The construct part that is a weapon builder part.</summary>
    private static ConstructPartDef GunDef(string builderName) =>
        ConstructParts.All.FirstOrDefault(d => d.GunPartName == builderName || d.GunReceiver == builderName)
        ?? throw new VehicleKitException($"no construct part is the builder's \"{builderName}\" (Parts/<Category>)");

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
