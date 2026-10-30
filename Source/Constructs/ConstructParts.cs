using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a construct part does (docs/sandbox/CONSTRUCTS.md, "Parts").</summary>
public enum PartRole : byte
{
    Frame, // structure: blocks and beams welded together
    Panel, // a plate: structure and cover
    Seat, // a driver's seat: the root of a vehicle, and where the driver sits
    Wheel, // rolls on the ground (a raycast wheel); needs a suspension to drive
    Suspension, // holds a wheel: its spring and travel
    Engine, // drives every wheel of its body while it has fuel and a driver
    FuelTank, // holds the fuel; feeds an engine it touches, or through fuel lines
    FuelLine, // a pipe: carries fuel from a tank to an engine
    Hinge, // a passive hinge joint: what is attached to its leaf swings
    Door, // a door leaf (hangs on a hinge; Interact opens and shuts it)
    Foundation, // anchors a construct to the ground (a base: never moves)
    Joint, // milestone 2: a moving joint (motorised hinge, turntable, piston, steering pivot): what is on its leaf moves
    Transmission, // milestone 2: takes the engine's drive (it must touch the engine) on to the differentials
    Differential, // milestone 2: an axle: drives the suspensions (wheels) bolted to it, fed by a transmission
    Logic, // milestone 2: a logic part (controls, sensors, gates, timers, lamps, horns, brakes), wired with the linking tool
    Gun, // milestone 3: a gun (a weapon builder receiver, or a heavy construct gun): fires from a gunner seat or a wired signal
    GunPart, // milestone 3: a weapon builder part (barrel, splitter, feed, ammo, muzzle, optic) bolted onto a gun
    AmmoBox, // milestone 3: rounds for the guns its feed chain reaches; shot through, it cooks off
    FeedChute, // milestone 3: carries rounds from an ammo box to a gun (like a fuel line)
    Battery, // milestone 3: stores charge (kJ) for the electrical parts its construct's links reach (ConstructPower.cs)
    Alternator, // milestone 3: touching a running engine, charges the batteries
    Hitch, // milestone 3: couples to another construct's hitch (a tow ball to an eye, a chain hook to a ring; ConstructLinks.cs)
    Transfer, // drivetrain parts: drive shafts, U and CV joints, gearboxes, chains and belts, clutches, transfer cases, PTOs, hubs (ConstructDrive.cs)
}

/// <summary>What a hitch part is (milestone 3): a ball or a hook couples to an eye.</summary>
public enum HitchKind : byte
{
    Ball, // a tow ball: a rigid free pivot (a trailer)
    Eye, // a ring or tow eye: what a ball or a hook couples to
    Hook, // a chain hook: a chain that only pulls (a wrecking ball)
}

/// <summary>How a construct gun looks and sounds when it fires (milestone 3; docs/sandbox/CONSTRUCTS.md, "Construct weapons").</summary>
public enum GunFx : byte
{
    Bullet, // a tracer from the muzzle
    Cannon, // a fat, slower tracer and a big report
    Rocket, // a slow smoke trail and a blast at the end
    Flame, // a short gout of fire
}

/// <summary>What a construct gun's rounds do (the weapon builder's ammo flags that count against constructs).</summary>
[Flags]
public enum GunEffect : byte
{
    None = 0,
    Penetrating = 1, // on through a part into what is behind it
    Explosive = 2, // a splash where it lands
    Incendiary = 4, // sets fuel, wood and ammo alight
}

/// <summary>How a joint part moves (milestone 2; docs/sandbox/CONSTRUCTS.md, "Joints").</summary>
public enum JointType : byte
{
    Passive, // the milestone 1 hinge: swings freely, latched open or shut by Interact; a signal wired to it locks it
    Motor, // a motorised hinge: driven to an angle (signal 1: max, 0: rest, -1: min)
    Turntable, // turns at a speed (signal -1..1 of its speed), no limits unless given
    Piston, // slides along its axis (signal 0..1 of its travel)
    Steering, // a steering pivot: follows the driver's steering, or a wired signal (-1..1)
}

/// <summary>What a logic part does (milestone 2; docs/sandbox/CONSTRUCTS.md, "Logic").</summary>
public enum CLogic : byte
{
    None,
    Button, // Interact: on for half a second
    Lever, // Interact: middle, forward (1), middle, back (-1)
    Switch, // Interact: on / off
    Plate, // a pressure plate: on while a player stands on it
    Reader, // an input reader: on while the sitter of a seat holds a key (the seat wired into it, else the driver's)
    Speed, // a speed sensor: its construct's speed (m/s)
    Proximity, // on while a player is within its range
    And, // on when every input is on
    Or, // on when any input is on
    Not, // on when no input is on
    Timer, // while enabled (no input, or an input on): on and off, each for half its period
    Delay, // copies its input once the input has held for its time
    Toggle, // flips each time its input comes on (any input on)
    Compare, // on when its inputs add up to more than its threshold
    Scale, // its inputs added up, times its factor
    Lamp, // lights while its input is on
    Horn, // sounds while its input is on
    Brake, // brakes its construct while its input is on
    FuelGauge, // milestone 3: its construct's fuel, 0..1 of what the tanks hold (a dial)
    ChargeGauge, // milestone 3: its construct's charge, 0..1 of what the batteries hold (a dial)
    Selector, // milestone 3: Interact or a rising input steps it on: 1, 2 ... its positions, then 1 again (a gear selector)
}

/// <summary>The tool a part is attached and removed with (decision 2: editing always needs a tool in hand).</summary>
public enum ConstructTool : byte
{
    None, // bare hands or a gun: use only (drive, sit, open doors), never edit
    Wrench, // bolted parts: wheels, seats, engines, doors...
    Welder, // welded parts: frames, panels, foundations
    Linker, // milestone 2: the linking tool: wires (outputs to inputs) and logic settings; never fits or removes a part
}

/// <summary>What happens to a part at zero health.</summary>
public enum PartBreak : byte
{
    Detach, // it breaks off (gone; anything it held that is cut off from the root falls away as a wreck)
    Disable, // it stays on but stops working (a dead engine, a holed tank)
}

/// <summary>What kind of attachment point a socket is: two sockets mate only when their kinds match.</summary>
public enum SocketKind : byte
{
    Struct, // frames, panels, seats, engines, tanks...
    Axle, // a wheel onto a suspension
    Leaf, // a door onto a hinge's leaf
}

/// <summary>One box of a part's model and collision, in the part's own frame (metres).</summary>
/// <summary>A detail box is drawn only: it adds no bounds, mass, collision or hit area (and may stick out past a face).</summary>
public readonly record struct CPartBox(Vector3 Center, Vector3 Half, Vector3 Color, bool Detail = false);

/// <summary>An attachment point: where on the part (on a face of its bounds) and which way it faces (an axis, 0..5: +X -X +Y -Y +Z -Z).</summary>
public readonly record struct CSocket(string Name, Vector3 At, byte Dir, SocketKind Kind);

/// <summary>One part type, from a JSON file in Constructs/Parts (loaded by <see cref="ConstructParts"/>).</summary>
public sealed class ConstructPartDef
{
    public byte Id; // index in ConstructParts.All (the wire id)
    public string Name = "";
    public string File = "";
    public string Description = "";
    public PartRole Role;
    public ConstructTool Tool;
    public float AttachSeconds;
    public float Mass;
    public float Health;
    public float Armour; // flat damage taken off every hit
    public string Material = "steel";
    public float Friction = 0.7f;
    public PartBreak OnBreak;
    public int Scrap; // what salvaging it gives once wrecked (milestone 3: "scrap", else from its mass and material; ConstructInventory.cs)
    public CPartBox[] Boxes = []; // solid boxes first, then detail boxes
    public int SolidBoxes; // how many of Boxes are solid
    public CSocket[] Sockets = [];
    public Vector3 BoundsMin, BoundsMax; // round every box, in the part's frame

    // Role data (only what the role uses is set)
    public float EngineForce, TopSpeed; // engine: drive force (N) shared by the wheels, top speed (m/s)
    public float WheelRadius, Grip = 1f; // wheel
    public float Travel, Frequency, Damping; // suspension: travel (m), spring frequency (Hz), damping ratio
    public float FuelCapacity; // fuel tank (litres)
    public float BurnRate; // engine: litres a second at full throttle
    public Vector3 SeatAt; // seat: where the sitter's feet go (part frame)
    public byte JointAxis; // hinge: the axis it turns about (0..5)
    public Vector3 JointPivot; // hinge: a point on the axis (part frame)
    public float JointMin, JointMax; // hinge: limits (radians)
    public string LeafSocket = ""; // hinge: what attaches to this socket hangs on the joint
    public JointType JointType; // hinge: Passive; joint: the rest
    public float MotorSpeed, MotorStrength; // joint: radians (metres for a piston) a second; N m (N for a piston)
    public bool JointLimited = true; // a turntable without min/max turns forever
    public int LeafIndex = -1; // hinge, joint: the leaf socket's index

    // Logic (milestone 2)
    public CLogic Logic;
    public float[] Settings = []; // the values its setting cycles through (seconds, metres, a threshold, a factor; a reader's key bits)
    public string[] SettingLabels = []; // what the HUD shows for each
    public string SettingName = ""; // "period", "key"...
    public byte DefaultSetting;

    // Milestone 3: power, couplings, gauges (docs/sandbox/CONSTRUCTS.md, "Milestone 3")
    public float PowerDraw; // watts while it works (a lamp lit, a horn sounding, a logic part on; a motor: idling, on top of its work)
    public float BatteryCapacity; // battery: kJ (comes full)
    public float AlternatorRate; // alternator: kW into the batteries while the engine it touches runs
    public HitchKind Hitch; // hitch
    public Vector3 HitchAt; // hitch: the coupling point (part frame)
    public float HitchChain; // hitch: a chain this long (m; 0: a rigid ball joint)
    public bool HasDial; // a gauge's dial: a needle on a face, turned by its signal
    public Vector3 DialAt; // the needle's hub (part frame)
    public byte DialDir; // the face it is on (out of the part)
    public float DialMax = 1f; // the signal at full scale (270 degrees)

    // Drivetrain families (docs/sandbox/CONSTRUCTS.md, "Drivetrain parts"; ConstructDrive.cs)
    public float EngineTorque; // engine: peak torque at the crank, N m
    public float IdleRpm, PeakRpm, MaxRpm; // engine: idle, where the curve peaks, the limiter
    public EngineCurve Curve; // engine: the torque curve's shape
    public EngineFuel FuelKind; // engine: what it runs on (a fuel tank, the battery, nothing)
    public bool NeedsStarter; // engine: needs a battery's starter on any construct (a diesel, a turbine)
    public float SpoolSeconds; // engine: a turbine's time from idle to full power
    public float Noise; // engine: 0 quiet .. 1 loud (the look line)
    public TransKind Trans; // transmission: manual, sequential, automatic, cvt, reversing
    public float ReverseRatio, ShiftSeconds, TransEfficiency = 1f; // transmission: reverse reduction, the power cut of a shift, what it keeps
    public float CvtLow, CvtHigh; // a CVT's range of reductions
    public DiffKind Diff; // differential: open, locked, limited slip, lockable
    public float DiffRatio = 1f, DiffEfficiency = 1f; // differential: final drive reduction and what it keeps
    public DriveKind Drive; // transfer part: what it is
    public float DriveRatio = 1f, DriveEfficiency = 1f; // transfer part: reduction (engine side turns per output turn), what it keeps
    public float MaxAngle; // a U or CV joint: the most it bends (radians)
    public float MaxTorque; // a belt, U joint...: the most torque it carries before it slips (N m at the part; 0: no limit)
    public int[] Ports = []; // transfer part: the sockets the drive goes in and out of (empty: every socket)
    public int FrontPort = -1; // a transfer case: its switched (front) output socket
    public float OffroadGrip = 1f, SideGrip = 1f, RollResist = 0.01f; // wheel: grip on terrain (times grip), sideways (times grip), rolling resistance
    public bool Drivable = true, FlatTyre; // wheel: can be driven (a caster can't); goes flat at zero health instead of falling off
    public float TrackLength; // wheel: a track unit this long (several contact rays along it)
    public int TrackRays; // wheel: a track unit's contact rays
    public float MaxLoad = 100000f; // suspension: kg a wheel it carries springs for (more: it sags and bottoms)
    public bool ActiveSuspension; // suspension: a signal wired in sets its ride height (0 low .. 1 high)
    public float[] SuspFrequency = [], SuspDamping = [], SuspHold = []; // suspension: an adjustable one's presets (its Settings); hold: ride height 0..1 (-1: a plain spring)

    /// <summary>A transmission's gear reductions are its <see cref="Settings"/> (the engine turns this many times per output turn).</summary>
    public bool HasGears => Role == PartRole.Transmission && (Settings.Length > 0 || Trans == TransKind.Cvt);

    /// <summary>Uses power on an electrical construct (logic parts, moving joints, anything with a draw; the engine's starter separately).</summary>
    public bool Electric => Role == PartRole.Logic || Role == PartRole.Joint && JointType != JointType.Steering || PowerDraw > 0 || FuelKind == EngineFuel.Electric && Role == PartRole.Engine; // (steering is mechanical)
    // Weapons (milestone 3; ConstructWeapons.cs)
    public string GunReceiver = "", GunAction = ""; // a gun on a weapon builder receiver (its action: the receiver's stock one unless named)
    public float GunDamage, GunRpm, GunSpread, GunRange, GunReload, GunSpeed, GunBlast, GunBlastDamage, GunKick; // a heavy gun's own numbers
    public float GunMaxRange = 300f; // how far its rounds reach at all
    public int GunMag, GunPellets = 1;
    public string[] GunFlags = [];
    public Vector3 GunMuzzle; // a heavy gun's muzzle (part frame); a receiver's comes from its barrels
    public GunFx GunFx;
    public string GunSound = ""; // the weapon file whose report it uses
    public string GunPartName = ""; // a gun part: the weapon builder part it is
    public int BoxRounds; // an ammo box: rounds it comes with
    public float CookRadius, CookDamage; // an ammo box: its cook-off at full

    public bool IsReceiverGun => Role == PartRole.Gun && GunReceiver.Length > 0;

    /// <summary>Catches fire from incendiary rounds (wood, rubber, fuel tanks, ammo boxes).</summary>
    public bool Flammable => Material is "wood" or "rubber" or "cloth" || Role is PartRole.FuelTank or PartRole.AmmoBox;

    public bool UsesTool(ConstructTool t) => t != ConstructTool.None && t == Tool;

    /// <summary>A joint part: a passive hinge or a moving joint (what goes on its leaf hangs on a new joint).</summary>
    public bool HasJoint => Role is PartRole.Hinge or PartRole.Joint;

    /// <summary>Has a signal output (wires start here): seats (someone sits there) and every logic part but lamps, horns and brakes.</summary>
    public bool HasOutput => Role == PartRole.Seat || (Role == PartRole.Logic && Logic is not (CLogic.Lamp or CLogic.Horn or CLogic.Brake));

    /// <summary>How many wires may come into it (0: it takes no input).</summary>
    public int MaxInputs => Role switch
    {
        PartRole.Joint or PartRole.Hinge or PartRole.Engine or PartRole.Gun => 4,
        PartRole.Transmission => 1, // milestone 3: a gear-select signal (1, 2, 3...)
        PartRole.Transfer => Drive is DriveKind.Clutch or DriveKind.TransferCase ? 1 : 0, // a clutch's engage, a transfer case's 4WD
        PartRole.Differential => Diff == DiffKind.Lockable ? 1 : 0, // locks it
        PartRole.Suspension => ActiveSuspension ? 1 : 0, // its ride height
        PartRole.Logic => Logic switch
        {
            CLogic.Button or CLogic.Lever or CLogic.Switch or CLogic.Plate or CLogic.Speed or CLogic.Proximity or CLogic.FuelGauge or CLogic.ChargeGauge => 0,
            CLogic.Reader or CLogic.Timer or CLogic.Delay or CLogic.Selector => 1,
            _ => 4,
        },
        _ => 0,
    };

    /// <summary>Is a signal part (its value travels to clients): an output, or a lamp, horn or brake.</summary>
    public bool IsSignal => HasOutput || Role == PartRole.Logic;
    public override string ToString() => Name;
}

/// <summary>A part file that can't be used: the reason names the file and the problem.</summary>
public sealed class ConstructPartException(string message) : Exception(message);

/// <summary>
/// The construct parts library: every JSON file in Constructs/Parts, loaded once at start-up (data first: a new part is a
/// new file). A bad file is refused with a reason (<see cref="Refused"/>; <c>--check-construct-parts</c> prints them) and
/// the rest still load. Ids follow the file names' sorted order, so every PC with the same files gives the same ids.
/// </summary>
public static partial class ConstructParts
{
    public const string Format = "call-of-slop-construct-part/1";

    /// <summary>The most part types (ids are one byte on the wire; 255 means none).</summary>
    public const int MaxTypes = 250;

    public const byte None = 255;

    private static readonly List<string> _refused = new();
    private static readonly Lazy<ConstructPartDef[]> _all = new(() => LoadFolder(Folder, _refused), LazyThreadSafetyMode.ExecutionAndPublication); // (maps load in parallel)

    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Parts");

    public static IReadOnlyList<ConstructPartDef> All => _all.Value;

    /// <summary>Files refused at load, with the reason (one line each).</summary>
    public static IReadOnlyList<string> Refused
    {
        get
        {
            _ = All;
            return _refused;
        }
    }

    /// <summary>A content hash of the loaded parts (the same files give the same hash on every PC).</summary>
    public static uint Hash
    {
        get
        {
            var h = 2166136261u;
            foreach (var d in All)
                foreach (var ch in d.Name + d.Role + d.Mass + d.Health + d.Boxes.Length + d.Sockets.Length)
                    h = (h ^ ch) * 16777619u;
            return h;
        }
    }

    /// <summary><c>--check-construct-parts</c>: every part file, blueprint and tool model, loaded or refused with the reason. Exit code: the refusals.</summary>
    public static int CheckCli()
    {
        foreach (var d in All)
            Console.WriteLine($"OK  part {d.Id,3} {d.Name,-14} {d.Role,-10} {d.Tool,-6} {d.Mass,6:0} kg {d.Health,6:0} hp armour {d.Armour:0}, {d.Boxes.Length} boxes, {d.Sockets.Length} sockets ({d.File})");
        foreach (var b in ConstructBuild.Blueprints)
            Console.WriteLine($"OK  blueprint {b.Name}: {b.Steps.Count} steps, {ConstructDrive.Describe(b)} ({b.File})");
        foreach (var r in Refused)
            Console.WriteLine($"REFUSED {r}");
        foreach (var r in ConstructBuild.RefusedBlueprints)
            Console.WriteLine($"REFUSED {r}");
        foreach (var m in ConstructTools.All)
            Console.WriteLine($"OK  tool {m.Key,-9} {m.Name,-14} {m.Boxes.Length} boxes{(m.Support != null ? ", two hands" : "")}{(m.Tool == null ? " (still to come)" : "")} ({m.File})");
        foreach (var r in ConstructTools.Refused)
            Console.WriteLine($"REFUSED {r}");
        return Refused.Count + ConstructBuild.RefusedBlueprints.Count + ConstructTools.Refused.Count;
    }

    public static ConstructPartDef? Get(byte id) => id < All.Count ? All[id] : null;

    public static ConstructPartDef? Find(string name)
    {
        foreach (var d in All)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    public static ConstructPartDef Need(string name) => Find(name) ?? throw new ConstructPartException($"no construct part named \"{name}\"");

    /// <summary>Loads every part file of a folder (sorted by file name), refusing bad ones into <paramref name="refused"/>.</summary>
    public static ConstructPartDef[] LoadFolder(string folder, List<string> refused)
    {
        var list = new List<ConstructPartDef>();
        if (!Directory.Exists(folder))
        {
            refused.Add($"{folder}: the construct parts folder is missing");
            return [];
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), StringComparer.Ordinal))
        {
            try
            {
                var def = Parse(File.ReadAllText(file), Path.GetFileName(file));
                if (!names.Add(def.Name))
                    throw new ConstructPartException($"{Path.GetFileName(file)}: another file already has the name \"{def.Name}\"");
                if (list.Count >= MaxTypes)
                    throw new ConstructPartException($"{Path.GetFileName(file)}: more than {MaxTypes} part types");
                def.Id = (byte)list.Count;
                list.Add(def);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                refused.Add(ex is ConstructPartException ? ex.Message : $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list.ToArray();
    }

    // ---- Parsing ---------------------------------------------------------------------------------------------

    private static readonly string[] TopKeys =
    [
        "format", "name", "description", "role", "tool", "attachSeconds", "mass", "health", "armour", "material", "friction",
        "onBreak", "color", "boxes", "sockets", "engine", "wheel", "suspension", "fuelTank", "seat", "hinge", "joint", "logic",
        "transmission", "battery", "alternator", "hitch", "power", "dial", "gun", "gunPart", "ammoBox", "scrap", "differential", "drive",
    ];

    /// <summary>The keys an input reader can read: the low byte is <see cref="Buttons"/>, then fire, aim and reload (sent by a seated client).</summary>
    public static readonly (string Name, ushort Bit)[] ReaderKeys =
    [
        ("fire", 0x100), ("aim", 0x200), ("reload", 0x400), ("forward", (ushort)Buttons.Forward), ("back", (ushort)Buttons.Back),
        ("left", (ushort)Buttons.Left), ("right", (ushort)Buttons.Right), ("jump", (ushort)Buttons.Jump), ("sprint", (ushort)Buttons.Sprint),
    ];

    public const ushort KeyFire = 0x100, KeyAim = 0x200, KeyReload = 0x400;

    /// <summary>Parses one part file's text. Throws <see cref="ConstructPartException"/> with the reason for anything wrong.</summary>
    public static ConstructPartDef Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: a part file is one JSON object");
        Known(root, TopKeys, file);
        if (Opt(root, "format") is { } fmt && fmt.GetString() != Format)
            throw new ConstructPartException($"{file}: \"format\" must be \"{Format}\"");
        var d = new ConstructPartDef { File = file };
        d.Name = StrKey(root, "name", file);
        if (d.Name.Length is 0 or > 32 || d.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be 1-32 plain ASCII characters");
        d.Description = Opt(root, "description") is { } desc ? AsciiText(desc, file, "description") : "";
        d.Role = EnumOf<PartRole>(StrKey(root, "role", file), file, "role");
        d.Tool = EnumOf<ConstructTool>(StrKey(root, "tool", file), file, "tool");
        if (d.Tool is ConstructTool.None or ConstructTool.Linker)
            throw new ConstructPartException($"{file}: \"tool\" is \"wrench\" or \"welder\"");
        d.AttachSeconds = Opt(root, "attachSeconds") is { } secs ? Num(secs, file, "attachSeconds") : d.Tool == ConstructTool.Welder ? 2.5f : 1.5f;
        Range(d.AttachSeconds, 0.2f, 20f, file, "attachSeconds");
        d.Mass = Num(Need(root, "mass", file), file, "mass");
        Range(d.Mass, 0.5f, 5000f, file, "mass");
        d.Health = Num(Need(root, "health", file), file, "health");
        Range(d.Health, 1f, 100000f, file, "health");
        d.Armour = Opt(root, "armour") is { } arm ? Num(arm, file, "armour") : 0f;
        Range(d.Armour, 0f, 1000f, file, "armour");
        d.Material = Opt(root, "material") is { } mat ? Str(mat, file, "material") : "steel";
        d.Friction = Opt(root, "friction") is { } fr ? Num(fr, file, "friction") : d.Material == "rubber" ? 1.0f : 0.6f;
        Range(d.Friction, 0f, 2f, file, "friction");
        d.OnBreak = Opt(root, "onBreak") is { } ob ? EnumOf<PartBreak>(Str(ob, file, "onBreak"), file, "onBreak")
            : d.Role is PartRole.Engine or PartRole.FuelTank or PartRole.Seat or PartRole.Transmission or PartRole.Differential or PartRole.Battery or PartRole.Alternator or PartRole.Transfer ? PartBreak.Disable : PartBreak.Detach;
        d.Scrap = Opt(root, "scrap") is { } scr ? (int)MathF.Round(Num(scr, file, "scrap")) : SandboxInventory.ScrapFrom(d.Mass, d.Material);
        Range(d.Scrap, 0f, 10000f, file, "scrap");
        var colour = Opt(root, "color") is { } col ? Colour(col, file, "color") : new Vector3(0.5f);

        // Boxes
        if (Opt(root, "boxes") is not { ValueKind: JsonValueKind.Array } boxes || boxes.GetArrayLength() == 0)
            throw new ConstructPartException($"{file}: \"boxes\" must list at least one box");
        if (boxes.GetArrayLength() > 16)
            throw new ConstructPartException($"{file}: at most 16 boxes a part");
        var list = new List<CPartBox>();
        var bi = 0;
        foreach (var b in boxes.EnumerateArray())
        {
            var where = $"boxes[{bi++}]";
            Known(b, ["min", "max", "at", "size", "color", "detail"], $"{file}, {where}");
            Vector3 min, max;
            if (Opt(b, "min") is { } mn && Opt(b, "max") is { } mx)
            {
                min = Vec(mn, file, where + ".min");
                max = Vec(mx, file, where + ".max");
            }
            else if (Opt(b, "size") is { } sz)
            {
                var size = Vec(sz, file, where + ".size");
                var at = Opt(b, "at") is { } a ? Vec(a, file, where + ".at") : Vector3.Zero;
                min = at - size / 2;
                max = at + size / 2;
            }
            else
                throw new ConstructPartException($"{file}: {where} needs \"min\" and \"max\", or \"size\" (and \"at\")");
            if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f || max.Z - min.Z < 0.01f)
                throw new ConstructPartException($"{file}: {where} is thinner than 1 cm (or inside out)");
            if (Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)) is var far && MathF.Max(far.X, MathF.Max(far.Y, far.Z)) > 4f)
                throw new ConstructPartException($"{file}: {where} reaches more than 4 m from the part's origin");
            var c = Opt(b, "color") is { } bc ? Colour(bc, file, where + ".color") : colour;
            list.Add(new CPartBox((min + max) / 2, (max - min) / 2, c, Opt(b, "detail") is { ValueKind: JsonValueKind.True }));
        }
        if (list.All(x => x.Detail))
            throw new ConstructPartException($"{file}: every box is a detail: at least one must be solid");
        // Solid boxes first (the physics walks those), details after.
        d.Boxes = list.Where(x => !x.Detail).Concat(list.Where(x => x.Detail)).ToArray();
        d.SolidBoxes = list.Count(x => !x.Detail);
        d.BoundsMin = new Vector3(float.MaxValue);
        d.BoundsMax = new Vector3(float.MinValue);
        foreach (var b in d.Boxes.AsSpan(0, d.SolidBoxes))
        {
            d.BoundsMin = Vector3.Min(d.BoundsMin, b.Center - b.Half);
            d.BoundsMax = Vector3.Max(d.BoundsMax, b.Center + b.Half);
        }

        // Sockets: each on a face of the part's bounds, facing out of it.
        if (Opt(root, "sockets") is not { ValueKind: JsonValueKind.Array } sockets || sockets.GetArrayLength() == 0)
            throw new ConstructPartException($"{file}: \"sockets\" must list at least one attachment point");
        if (sockets.GetArrayLength() > 40)
            throw new ConstructPartException($"{file}: at most 40 sockets a part");
        var socks = new List<CSocket>();
        var si = 0;
        foreach (var s in sockets.EnumerateArray())
        {
            var where = $"sockets[{si++}]";
            Known(s, ["name", "at", "dir", "kind"], $"{file}, {where}");
            var name = Opt(s, "name") is { } sn ? Str(sn, file, where + ".name") : $"s{si - 1}";
            if (socks.Any(x => x.Name == name))
                throw new ConstructPartException($"{file}: two sockets are called \"{name}\"");
            var at = Vec(Need(s, "at", $"{file}, {where}"), file, where + ".at");
            var dir = DirOf(Str(Need(s, "dir", $"{file}, {where}"), file, where + ".dir"), file, where);
            var kind = Opt(s, "kind") is { } k ? EnumOf<SocketKind>(Str(k, file, where + ".kind"), file, where + ".kind") : SocketKind.Struct;
            var axis = dir >> 1;
            var face = (dir & 1) == 0 ? d.BoundsMax[axis] : d.BoundsMin[axis];
            if (MathF.Abs(at[axis] - face) > 0.005f)
                throw new ConstructPartException($"{file}: socket \"{name}\" faces {DirName(dir)} but is not on that face of the part (at {at[axis]:0.###}, the face is at {face:0.###})");
            for (var a = 0; a < 3; a++)
                if (a != axis && (at[a] < d.BoundsMin[a] - 0.005f || at[a] > d.BoundsMax[a] + 0.005f))
                    throw new ConstructPartException($"{file}: socket \"{name}\" is outside the part");
            socks.Add(new CSocket(name, at, dir, kind));
        }
        d.Sockets = socks.ToArray();

        // Role data
        string[] roleKeys = ["engine", "wheel", "suspension", "fuelTank", "seat", "hinge", "joint", "logic", "transmission", "battery", "alternator", "hitch", "gun", "gunPart", "ammoBox", "differential", "drive"];
        var mine = d.Role switch
        {
            PartRole.Engine => "engine",
            PartRole.Wheel => "wheel",
            PartRole.Suspension => "suspension",
            PartRole.FuelTank => "fuelTank",
            PartRole.Seat => "seat",
            PartRole.Hinge => "hinge",
            PartRole.Joint => "joint",
            PartRole.Logic => "logic",
            PartRole.Transmission => "transmission",
            PartRole.Battery => "battery",
            PartRole.Alternator => "alternator",
            PartRole.Hitch => "hitch",
            PartRole.Gun => "gun",
            PartRole.GunPart => "gunPart",
            PartRole.AmmoBox => "ammoBox",
            PartRole.Differential => "differential",
            PartRole.Transfer => "drive",
            _ => "",
        };
        foreach (var key in roleKeys)
            if (key != mine && Opt(root, key) != null)
                throw new ConstructPartException($"{file}: \"{key}\" is only for {key} parts (this one is a {d.Role.ToString().ToLowerInvariant()})");
        var block = mine.Length > 0 ? Opt(root, mine) : null;
        if (mine.Length > 0 && block is not { ValueKind: JsonValueKind.Object } && !(d.Role is PartRole.Transmission or PartRole.Differential && block == null)) // (a transmission without gears has one; a differential without a block is an open one)
            throw new ConstructPartException($"{file}: a {mine} part needs a \"{mine}\" block");
        var bw = $"{file}, {mine}";
        switch (d.Role)
        {
            case PartRole.Engine:
                ParseEngine(d, block!.Value, file, bw);
                break;
            case PartRole.Wheel:
                ParseWheel(d, block!.Value, file, bw);
                break;
            case PartRole.Suspension:
                ParseSuspension(d, block!.Value, file, bw);
                break;
            case PartRole.FuelTank:
                Known(block!.Value, ["capacity"], bw);
                d.FuelCapacity = Num(Need(block.Value, "capacity", bw), file, "fuelTank.capacity");
                Range(d.FuelCapacity, 0.1f, 10000, file, "fuelTank.capacity");
                break;
            case PartRole.Seat:
                Known(block!.Value, ["at"], bw);
                d.SeatAt = Vec(Need(block.Value, "at", bw), file, "seat.at");
                break;
            case PartRole.Hinge:
                Known(block!.Value, ["axis", "pivot", "min", "max", "leaf"], bw);
                d.JointAxis = DirOf(Str(Need(block.Value, "axis", bw), file, "hinge.axis"), file, "hinge.axis");
                d.JointPivot = Opt(block.Value, "pivot") is { } pv ? Vec(pv, file, "hinge.pivot") : Vector3.Zero;
                d.JointMin = Num(Need(block.Value, "min", bw), file, "hinge.min") * MathF.PI / 180f;
                d.JointMax = Num(Need(block.Value, "max", bw), file, "hinge.max") * MathF.PI / 180f;
                if (d.JointMin > 0 || d.JointMax < 0 || d.JointMax - d.JointMin < 0.05f || d.JointMax - d.JointMin > MathF.Tau)
                    throw new ConstructPartException($"{file}: hinge \"min\" must be 0 or less and \"max\" 0 or more (degrees), at most 360 apart");
                d.LeafSocket = Str(Need(block.Value, "leaf", bw), file, "hinge.leaf");
                if (!d.Sockets.Any(s => s.Name == d.LeafSocket && s.Kind == SocketKind.Leaf))
                    throw new ConstructPartException($"{file}: hinge \"leaf\" must name one of its \"leaf\" sockets");
                d.LeafIndex = Array.FindIndex(d.Sockets, s => s.Name == d.LeafSocket);
                d.JointType = JointType.Passive;
                break;
            case PartRole.Joint:
                ParseJoint(d, block!.Value, file, bw);
                break;
            case PartRole.Logic:
                ParseLogic(d, block!.Value, file, bw);
                break;
            case PartRole.Transmission:
                ParseTransmission(d, block, file, bw);
                break;
            case PartRole.Differential:
                ParseDifferential(d, block, file, bw);
                break;
            case PartRole.Transfer:
                ParseTransfer(d, block!.Value, file, bw);
                break;
            case PartRole.Battery:
                Known(block!.Value, ["capacity"], bw);
                d.BatteryCapacity = Num(Need(block.Value, "capacity", bw), file, "battery.capacity");
                Range(d.BatteryCapacity, 1, 100000, file, "battery.capacity");
                break;
            case PartRole.Alternator:
                Known(block!.Value, ["rate"], bw);
                d.AlternatorRate = Num(Need(block.Value, "rate", bw), file, "alternator.rate");
                Range(d.AlternatorRate, 0.01f, 1000, file, "alternator.rate");
                break;
            case PartRole.Hitch:
            {
                Known(block!.Value, ["kind", "at", "chain"], bw);
                d.Hitch = EnumOf<HitchKind>(Str(Need(block.Value, "kind", bw), file, "hitch.kind"), file, "hitch.kind");
                d.HitchAt = Vec(Need(block.Value, "at", bw), file, "hitch.at");
                if (MathF.Max(MathF.Abs(d.HitchAt.X), MathF.Max(MathF.Abs(d.HitchAt.Y), MathF.Abs(d.HitchAt.Z))) > 1.5f)
                    throw new ConstructPartException($"{file}: \"hitch.at\" is more than 1.5 m from the part's origin");
                d.HitchChain = Opt(block.Value, "chain") is { } ch ? Num(ch, file, "hitch.chain") : 0f;
                Range(d.HitchChain, 0, 8, file, "hitch.chain");
                if (d.HitchChain > 0 && d.Hitch != HitchKind.Hook)
                    throw new ConstructPartException($"{file}: only a hook has a \"chain\"");
                if (d.Hitch == HitchKind.Hook && d.HitchChain < 0.3f)
                    throw new ConstructPartException($"{file}: a hook needs a \"chain\" of 0.3 to 8 m");
                break;
            }
            case PartRole.Gun:
                ParseGun(d, block!.Value, file, bw);
                break;
            case PartRole.GunPart:
            {
                Known(block!.Value, ["part"], bw);
                d.GunPartName = Str(Need(block.Value, "part", bw), file, "gunPart.part");
                if (Parts.Find(d.GunPartName) is not { } wp)
                    throw new ConstructPartException($"{file}: \"gunPart.part\": no weapon builder part named \"{d.GunPartName}\"");
                if (!ConstructWeapons.GunPartCategories.Contains(wp.Category))
                    throw new ConstructPartException($"{file}: \"gunPart.part\": a {wp.Category.ToString().ToLowerInvariant()} doesn't go on a construct gun (barrels, splitters, feeds, ammo, muzzle devices and optics do)");
                d.GunPartName = wp.Name;
                break;
            }
            case PartRole.AmmoBox:
                Known(block!.Value, ["rounds", "cookOff"], bw);
                d.BoxRounds = (int)Num(Need(block.Value, "rounds", bw), file, "ammoBox.rounds");
                Range(d.BoxRounds, 1, 60000, file, "ammoBox.rounds");
                if (Opt(block.Value, "cookOff") is { } cook)
                {
                    Known(cook, ["radius", "damage"], $"{bw}.cookOff");
                    d.CookRadius = Num(Need(cook, "radius", bw), file, "ammoBox.cookOff.radius");
                    Range(d.CookRadius, 0.5f, 20, file, "ammoBox.cookOff.radius");
                    d.CookDamage = Num(Need(cook, "damage", bw), file, "ammoBox.cookOff.damage");
                    Range(d.CookDamage, 1, 100000, file, "ammoBox.cookOff.damage");
                }
                break;
        }
        // Milestone 3: a power draw (watts while it works) and a gauge's dial.
        d.PowerDraw = Opt(root, "power") is { } pw ? Num(pw, file, "power") : d.Role == PartRole.Logic ? 2f : d.Role == PartRole.Joint && d.JointType != JointType.Steering ? 10f : d.ActiveSuspension ? 150f : 0f;
        Range(d.PowerDraw, 0, 1_000_000, file, "power");
        if (Opt(root, "dial") is { } dial)
        {
            if (dial.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: \"dial\" is an object (at, face, max)");
            if (d.Role != PartRole.Logic || !d.HasOutput)
                throw new ConstructPartException($"{file}: only a logic part with an output (a sensor, a gauge) has a \"dial\"");
            Known(dial, ["at", "face", "max"], $"{file}, dial");
            d.HasDial = true;
            d.DialAt = Vec(Need(dial, "at", $"{file}, dial"), file, "dial.at");
            d.DialDir = DirOf(Str(Need(dial, "face", $"{file}, dial"), file, "dial.face"), file, "dial.face");
            d.DialMax = Opt(dial, "max") is { } dm ? Num(dm, file, "dial.max") : 1f;
            Range(d.DialMax, 0.01f, 1000, file, "dial.max");
        }
        if (d.Role == PartRole.Door && !d.Sockets.Any(s => s.Kind == SocketKind.Leaf))
            throw new ConstructPartException($"{file}: a door needs a \"leaf\" socket (it hangs on a hinge)");
        if (d.Role is not (PartRole.Wheel or PartRole.Suspension) && d.Sockets.Any(s => s.Kind == SocketKind.Axle))
            throw new ConstructPartException($"{file}: only wheels and suspensions have \"axle\" sockets");
        if (d.Role is not (PartRole.Hinge or PartRole.Door) && d.Sockets.Any(s => s.Kind == SocketKind.Leaf))
            throw new ConstructPartException($"{file}: only hinges and doors have \"leaf\" sockets");
        return d;
    }

    private static readonly string[] JointTypes = ["motor", "turntable", "piston", "steering"];

    /// <summary>A moving joint's block: type, axis, pivot, limits, its leaf socket (a struct socket: anything can go on it), speed and strength.</summary>
    private static void ParseJoint(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["type", "axis", "pivot", "min", "max", "leaf", "speed", "strength"], bw);
        var type = Str(Need(b, "type", bw), file, "joint.type").ToLowerInvariant();
        var ti = Array.IndexOf(JointTypes, type);
        if (ti < 0)
            throw new ConstructPartException($"{file}: \"joint.type\" is one of {string.Join(", ", JointTypes)}, not \"{type}\"");
        d.JointType = (JointType)(ti + 1);
        d.JointAxis = DirOf(Str(Need(b, "axis", bw), file, "joint.axis"), file, "joint.axis");
        d.JointPivot = Opt(b, "pivot") is { } pv ? Vec(pv, file, "joint.pivot") : Vector3.Zero;
        var piston = d.JointType == JointType.Piston;
        var unit = piston ? 1f : MathF.PI / 180f; // a piston's limits and speed are metres; the rest degrees
        if (d.JointType == JointType.Turntable && Opt(b, "min") == null && Opt(b, "max") == null)
        {
            d.JointLimited = false;
            d.JointMin = float.NegativeInfinity;
            d.JointMax = float.PositiveInfinity;
        }
        else
        {
            d.JointMin = Num(Need(b, "min", bw), file, "joint.min") * unit;
            d.JointMax = Num(Need(b, "max", bw), file, "joint.max") * unit;
            if (piston && (d.JointMin != 0 || d.JointMax < 0.1f || d.JointMax > 4f))
                throw new ConstructPartException($"{file}: a piston's \"min\" is 0 and its \"max\" (its travel) 0.1 to 4 metres");
            if (!piston && (d.JointMin > 0 || d.JointMax < 0 || d.JointMax - d.JointMin < 0.05f || d.JointMax - d.JointMin > MathF.Tau))
                throw new ConstructPartException($"{file}: joint \"min\" must be 0 or less and \"max\" 0 or more (degrees), at most 360 apart");
        }
        d.MotorSpeed = Num(Need(b, "speed", bw), file, "joint.speed") * unit;
        Range(d.MotorSpeed, 0.05f, piston ? 10f : 4 * MathF.Tau, file, "joint.speed");
        d.MotorStrength = Num(Need(b, "strength", bw), file, "joint.strength");
        Range(d.MotorStrength, 1, 1_000_000, file, "joint.strength");
        d.LeafSocket = Str(Need(b, "leaf", bw), file, "joint.leaf");
        d.LeafIndex = Array.FindIndex(d.Sockets, s => s.Name == d.LeafSocket && s.Kind == SocketKind.Struct);
        if (d.LeafIndex < 0)
            throw new ConstructPartException($"{file}: joint \"leaf\" must name one of its struct sockets (what goes on it moves)");
    }

    private static readonly string[] GunFxNames = ["bullet", "cannon", "rocket", "flame"];

    /// <summary>
    /// A gun's block: either a weapon builder receiver (<c>"receiver"</c>, its <c>"action"</c> optional: the barrels, feed,
    /// ammo and so on are gun parts bolted to it on the construct, and its numbers come from the weapon builder's stat
    /// pipeline) or a heavy construct gun's own numbers (damage, rpm, spread, range, mag, reload...).
    /// </summary>
    private static void ParseGun(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["receiver", "action", "damage", "rpm", "spread", "range", "maxRange", "mag", "reload", "pellets", "speed", "flags", "blast", "blastDamage", "muzzle", "fx", "kick", "soundOf"], bw);
        d.GunMaxRange = Opt(b, "maxRange") is { } mr ? Num(mr, file, "gun.maxRange") : 300f;
        Range(d.GunMaxRange, 1, 1000, file, "gun.maxRange");
        d.GunKick = Opt(b, "kick") is { } kick ? Num(kick, file, "gun.kick") : 0f;
        Range(d.GunKick, 0, 100000, file, "gun.kick");
        d.GunFx = GunFx.Bullet;
        if (Opt(b, "fx") is { } fx)
        {
            var fi = Array.IndexOf(GunFxNames, Str(fx, file, "gun.fx").ToLowerInvariant());
            if (fi < 0)
                throw new ConstructPartException($"{file}: \"gun.fx\" is one of {string.Join(", ", GunFxNames)}");
            d.GunFx = (GunFx)fi;
        }
        d.GunSound = Opt(b, "soundOf") is { } so ? Str(so, file, "gun.soundOf") : "";
        if (d.GunSound.Length > 0 && Weapons.Find(d.GunSound) == null)
            throw new ConstructPartException($"{file}: \"gun.soundOf\": no weapon named \"{d.GunSound}\"");
        if (Opt(b, "receiver") is { } rec)
        {
            foreach (var k in new[] { "damage", "rpm", "spread", "range", "mag", "reload", "pellets", "flags", "blast", "blastDamage", "muzzle" })
                if (Opt(b, k) != null)
                    throw new ConstructPartException($"{file}: a gun on a receiver takes its numbers from the weapon builder (no \"{k}\")");
            var name = Str(rec, file, "gun.receiver");
            if (Parts.Find(name) is not { Receiver: not null } rp)
                throw new ConstructPartException($"{file}: \"gun.receiver\": no weapon builder receiver named \"{name}\"");
            d.GunReceiver = rp.Name;
            var act = Opt(b, "action") is { } a ? Str(a, file, "gun.action") : rp.Receiver.StockAction ?? "";
            if (Parts.Find(act) is not { Action: not null } ap)
                throw new ConstructPartException($"{file}: \"gun.action\": no weapon builder action named \"{act}\"");
            d.GunAction = ap.Name;
            return;
        }
        d.GunDamage = Num(Need(b, "damage", bw), file, "gun.damage");
        Range(d.GunDamage, 1, 100000, file, "gun.damage");
        d.GunRpm = Num(Need(b, "rpm", bw), file, "gun.rpm");
        Range(d.GunRpm, 1, 3600, file, "gun.rpm");
        d.GunSpread = Opt(b, "spread") is { } sp ? Num(sp, file, "gun.spread") : 1f;
        Range(d.GunSpread, 0, 45, file, "gun.spread");
        d.GunRange = Opt(b, "range") is { } rg ? Num(rg, file, "gun.range") : 60f;
        Range(d.GunRange, 1, 1000, file, "gun.range");
        d.GunMag = (int)Num(Need(b, "mag", bw), file, "gun.mag");
        Range(d.GunMag, 1, 5000, file, "gun.mag");
        d.GunReload = Opt(b, "reload") is { } rl ? Num(rl, file, "gun.reload") : 2f;
        Range(d.GunReload, 0.05f, 30, file, "gun.reload");
        d.GunPellets = Opt(b, "pellets") is { } pe ? (int)Num(pe, file, "gun.pellets") : 1;
        Range(d.GunPellets, 1, 24, file, "gun.pellets");
        d.GunSpeed = Opt(b, "speed") is { } spd ? Num(spd, file, "gun.speed") : 0f;
        Range(d.GunSpeed, 0, 2000, file, "gun.speed");
        d.GunFlags = Opt(b, "flags") is { ValueKind: JsonValueKind.Array } fl ? fl.EnumerateArray().Select(f => Str(f, file, "gun.flags").ToLowerInvariant()).ToArray() : [];
        foreach (var f in d.GunFlags)
            if (f is not ("penetrating" or "explosive" or "incendiary"))
                throw new ConstructPartException($"{file}: \"gun.flags\": \"{f}\" (a heavy gun's flags are penetrating, explosive, incendiary)");
        d.GunBlast = Opt(b, "blast") is { } bl ? Num(bl, file, "gun.blast") : 0f;
        Range(d.GunBlast, 0, 20, file, "gun.blast");
        d.GunBlastDamage = Opt(b, "blastDamage") is { } bd ? Num(bd, file, "gun.blastDamage") : d.GunDamage;
        Range(d.GunBlastDamage, 0, 100000, file, "gun.blastDamage");
        d.GunMuzzle = Vec(Need(b, "muzzle", bw), file, "gun.muzzle");
    }

    private static readonly string[] LogicKinds = Enum.GetNames<CLogic>().Skip(1).Select(n => n.ToLowerInvariant()).ToArray();

    /// <summary>A logic part's block: its kind and, for the kinds that have one, the setting it cycles through (the linking tool's R).</summary>
    private static void ParseLogic(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["kind", "settings", "labels", "setting", "settingName"], bw);
        var kind = Str(Need(b, "kind", bw), file, "logic.kind").ToLowerInvariant();
        var ki = Array.IndexOf(LogicKinds, kind);
        if (ki < 0)
            throw new ConstructPartException($"{file}: \"logic.kind\" is one of {string.Join(", ", LogicKinds)}, not \"{kind}\"");
        d.Logic = (CLogic)(ki + 1);
        var needs = d.Logic is CLogic.Reader or CLogic.Proximity or CLogic.Timer or CLogic.Delay or CLogic.Compare or CLogic.Scale or CLogic.Selector;
        if (d.Logic == CLogic.Reader)
        {
            // A reader's settings are key names.
            if (Opt(b, "labels") is not { ValueKind: JsonValueKind.Array } keys || keys.GetArrayLength() is 0 or > 16)
                throw new ConstructPartException($"{file}: an input reader needs \"labels\": the keys it can read (1-16 of {string.Join(", ", ReaderKeys.Select(k => k.Name))})");
            var labels = new List<string>();
            var bits = new List<float>();
            foreach (var k in keys.EnumerateArray())
            {
                var name = Str(k, file, "logic.labels").ToLowerInvariant();
                var at = Array.FindIndex(ReaderKeys, x => x.Name == name);
                if (at < 0)
                    throw new ConstructPartException($"{file}: an input reader reads one of {string.Join(", ", ReaderKeys.Select(x => x.Name))}, not \"{name}\"");
                labels.Add(name);
                bits.Add(ReaderKeys[at].Bit);
            }
            d.SettingLabels = labels.ToArray();
            d.Settings = bits.ToArray();
            if (Opt(b, "settings") != null)
                throw new ConstructPartException($"{file}: an input reader's keys are its \"labels\" (no \"settings\")");
        }
        else if (Opt(b, "settings") is { ValueKind: JsonValueKind.Array } set)
        {
            if (!needs)
                throw new ConstructPartException($"{file}: a {kind} has no setting");
            if (set.GetArrayLength() is 0 or > 16)
                throw new ConstructPartException($"{file}: \"logic.settings\" lists 1 to 16 values");
            d.Settings = set.EnumerateArray().Select(v => Num(v, file, "logic.settings")).ToArray();
            if (d.Logic == CLogic.Selector && d.Settings.Any(v => v < 2 || v > 9 || v != MathF.Floor(v)))
                throw new ConstructPartException($"{file}: a selector's settings are how many positions it has, 2 to 9");
            if (d.Logic is CLogic.Timer or CLogic.Delay or CLogic.Proximity && d.Settings.Any(v => v < 0.05f || v > 60))
                throw new ConstructPartException($"{file}: a {kind}'s settings are 0.05 to 60 ({(d.Logic == CLogic.Proximity ? "metres" : "seconds")})");
            d.SettingLabels = Opt(b, "labels") is { ValueKind: JsonValueKind.Array } lab
                ? lab.EnumerateArray().Select(v => AsciiText(v, file, "logic.labels")).ToArray()
                : d.Settings.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)).ToArray();
            if (d.SettingLabels.Length != d.Settings.Length)
                throw new ConstructPartException($"{file}: \"logic.labels\" must have one label per setting");
        }
        else if (needs)
            throw new ConstructPartException($"{file}: a {kind} needs \"settings\" (the values its setting cycles through)");
        else if (Opt(b, "labels") != null)
            throw new ConstructPartException($"{file}: a {kind} has no setting");
        d.SettingName = Opt(b, "settingName") is { } sn ? AsciiText(sn, file, "logic.settingName") : d.Logic switch
        {
            CLogic.Reader => "key",
            CLogic.Proximity => "range",
            CLogic.Timer => "period",
            CLogic.Delay => "delay",
            CLogic.Compare => "threshold",
            CLogic.Scale => "factor",
            CLogic.Selector => "positions",
            _ => "",
        };
        if (Opt(b, "setting") is { } def)
        {
            var v = Num(def, file, "logic.setting");
            if (v < 0 || v >= d.Settings.Length || v != MathF.Floor(v))
                throw new ConstructPartException($"{file}: \"logic.setting\" is the index of its starting setting (0 to {d.Settings.Length - 1})");
            d.DefaultSetting = (byte)v;
        }
    }

    // ---- Small helpers ------------------------------------------------------------------------------------

    private static readonly string[] DirNames = ["+x", "-x", "+y", "-y", "+z", "-z"];

    public static string DirName(byte dir) => DirNames[dir % 6];

    public static Vector3 DirVector(byte dir) => dir switch
    {
        0 => Vector3.UnitX,
        1 => -Vector3.UnitX,
        2 => Vector3.UnitY,
        3 => -Vector3.UnitY,
        4 => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };

    private static byte DirOf(string s, string file, string where)
    {
        var i = Array.IndexOf(DirNames, s.ToLowerInvariant());
        return i >= 0 ? (byte)i : throw new ConstructPartException($"{file}: {where}: a direction is one of {string.Join(", ", DirNames)}");
    }

    private static void Known(JsonElement obj, string[] keys, string where)
    {
        foreach (var p in obj.EnumerateObject())
            if (!keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ConstructPartException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", keys)})");
    }

    private static JsonElement? Opt(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
        return null;
    }

    private static JsonElement Need(JsonElement obj, string name, string where) =>
        Opt(obj, name) ?? throw new ConstructPartException($"{where}: missing \"{name}\"");

    private static string StrKey(JsonElement obj, string name, string file) => Str(Need(obj, name, file), file, name);

    private static string Str(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ConstructPartException($"{file}: \"{what}\" must be text");

    private static string AsciiText(JsonElement e, string file, string what)
    {
        var s = Str(e, file, what);
        return s.Any(c => c < 32 || c > 126) ? throw new ConstructPartException($"{file}: \"{what}\" must be plain ASCII") : s;
    }

    private static float Num(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new ConstructPartException($"{file}: \"{what}\" must be a number");

    private static void Range(float v, float lo, float hi, string file, string what)
    {
        if (v < lo || v > hi)
            throw new ConstructPartException($"{file}: \"{what}\" is {v.ToString(CultureInfo.InvariantCulture)}, it must be {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
    }

    private static Vector3 Vec(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], file, what), Num(e[1], file, what), Num(e[2], file, what))
            : throw new ConstructPartException($"{file}: \"{what}\" must be [x, y, z]");

    private static Vector3 Colour(JsonElement e, string file, string what)
    {
        try
        {
            return MapFile.ToColour(e, $"\"{what}\"", file + ": ");
        }
        catch (MapFileException ex)
        {
            throw new ConstructPartException(ex.Message);
        }
    }

    private static T EnumOf<T>(string s, string file, string what) where T : struct, Enum =>
        Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) && !int.TryParse(s, out _)
            ? v
            : throw new ConstructPartException($"{file}: \"{what}\" is one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}, not \"{s}\"");
}

/// <summary>
/// The 24 turns that keep a box's faces on the axes (a part is placed in one of these, so every part box stays axis-aligned
/// in its body's frame and two parts' overlap is a plain box test). Index 0 is no turn. The order is fixed: it is the wire id.
/// </summary>
public static class Rot24
{
    public const int Count = 24;

    // Each turn as its matrix: column c is where the local axis c goes (as a signed axis index into body space).
    private static readonly sbyte[][] Cols = Build();
    private static readonly Quaternion[] Quats = Cols.Select(QuatOf).ToArray();

    private static sbyte[][] Build()
    {
        var list = new List<sbyte[]>();
        int[][] perms = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var p in perms)
            for (var signs = 0; signs < 8; signs++)
            {
                var m = new float[3, 3];
                for (var c = 0; c < 3; c++)
                    m[p[c], c] = (signs >> c & 1) == 0 ? 1 : -1;
                var det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
                if (det < 0)
                    continue;
                var col = new sbyte[3];
                for (var c = 0; c < 3; c++)
                    col[c] = (sbyte)(p[c] * 2 + ((signs >> c & 1) == 0 ? 0 : 1)); // a signed axis as a dir index (+x 0, -x 1...)
                list.Add(col);
            }
        return list.ToArray();
    }

    private static Quaternion QuatOf(sbyte[] col)
    {
        var x = ConstructParts.DirVector((byte)col[0]);
        var y = ConstructParts.DirVector((byte)col[1]);
        var z = ConstructParts.DirVector((byte)col[2]);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public static Quaternion Quat(byte r) => Quats[r % Count];

    /// <summary>A vector from the part's frame into its body's (exact: only swaps and sign changes).</summary>
    public static Vector3 Apply(byte r, Vector3 v)
    {
        var c = Cols[r % Count];
        var o = Vector3.Zero;
        o = Add(o, c[0], v.X);
        o = Add(o, c[1], v.Y);
        o = Add(o, c[2], v.Z);
        return o;

        static Vector3 Add(Vector3 o, sbyte dir, float k)
        {
            var s = (dir & 1) == 0 ? k : -k;
            switch (dir >> 1)
            {
                case 0: o.X += s; break;
                case 1: o.Y += s; break;
                default: o.Z += s; break;
            }
            return o;
        }
    }

    /// <summary>A box's half size turned (always positive).</summary>
    public static Vector3 ApplyHalf(byte r, Vector3 half) => Vector3.Abs(Apply(r, half));

    /// <summary>A direction index (0..5) turned.</summary>
    public static byte ApplyDir(byte r, byte dir)
    {
        var c = Cols[r % Count][dir >> 1];
        return (byte)((dir & 1) == 0 ? c : c ^ 1);
    }

    /// <summary>The turns that take direction <paramref name="from"/> (part frame) to <paramref name="to"/> (body frame), in a fixed order: four of them.</summary>
    public static int Matching(byte from, byte to, Span<byte> into)
    {
        var n = 0;
        for (byte r = 0; r < Count && n < into.Length; r++)
            if (ApplyDir(r, from) == to)
                into[n++] = r;
        return n;
    }
}
