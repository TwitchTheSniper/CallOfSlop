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
    Storage, // physical storage (decision 18): slots that parts are set into, or a bin of bulk material with a fill level (ConstructStorage.cs)
    Transfer, // drivetrain parts: drive shafts, U and CV joints, gearboxes, chains and belts, clutches, transfer cases, PTOs, hubs (ConstructDrive.cs)
}

/// <summary>
/// How a part is walked on (building pieces; docs/sandbox/CONSTRUCTS.md, "Building pieces"): players and bots step up
/// onto it (like any anchored part) and bots' navigation may treat its tops as ground. None: not marked.
/// </summary>
public enum WalkKind : byte
{
    None,
    Floor, // a floor, deck, foundation or landing: flat ground
    Stairs, // steps (0.375 m rises): walked up and down
    Ramp, // a slope stepped in small rises (ramps, roofs): walked or driven
    Ladder, // a steep ship ladder (0.375 m rises over 0.25 m): walked up, slowly
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
    BotSpawner, // the Spawn category (GameSandboxSpawns.cs): spawns bots while enabled; puts out how many it has alive
    ZombieSpawner, // the Spawn category: spawns zombies while enabled; puts out how many it has alive
    Cargo, // a cargo volume (decision 21; ConstructCargo.cs): frozen or not (Interact flips it; a change of its input sets it); its box carries loose bodies while frozen
}

/// <summary>The tool a part is attached and removed with (decision 2: editing always needs a tool in hand).</summary>
public enum ConstructTool : byte
{
    None, // bare hands or a gun: use only (drive, sit, open doors), never edit
    Wrench, // bolted parts: wheels, seats, engines, doors...
    Welder, // welded parts: frames, panels, foundations
    Linker, // milestone 2: the linking tool: wires (outputs to inputs) and logic settings; never fits or removes a part
    Configure = 4, // the configure tool: a part's settings panel (ConstructConfig.cs); never fits or removes a part
    PhysicsGun = 8, // the physics gun (ConstructGrab.cs): grabs, moves, turns and freezes whole constructs; never fits or removes a part (8: after the tools still to come, ConstructTools.Keys)
}

/// <summary>The tools a wire byte may name.</summary>
public static class ConstructToolWire
{
    /// <summary>A tool from the wire (anything unknown: none).</summary>
    public static ConstructTool From(byte b) => b <= (byte)ConstructTool.Configure || b == (byte)ConstructTool.PhysicsGun ? (ConstructTool)b : ConstructTool.None;
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

/// <summary>
/// One box of a part's model and collision, in the part's own frame (metres). A detail box is drawn only: it adds no
/// bounds, mass, collision or hit area (and may stick out past a face). <see cref="Shape"/>: a detail box drawn as one of the weapon builder's contoured shapes (<see cref="PartShapes"/>: wedge,
/// arc, cylinder...; 0 = a plain box). <see cref="Hidden"/>: a solid box that is never drawn (a wedge's stepped collision
/// under its smooth shape). <see cref="Glass"/>: a see-through pane (how much of it is drawn, 0.1 to 0.9; 0 = opaque), drawn
/// in the blended pass after everything opaque (ConstructRender.DrawGlass) and passed through by bullets, which break it.
/// </summary>
public readonly record struct CPartBox(Vector3 Center, Vector3 Half, Vector3 Color, bool Detail = false, ushort Shape = 0, bool Hidden = false, float Glass = 0)
{
    public bool IsGlass => Glass > 0;

    /// <summary>The box's model matrix in its part's frame turned by <paramref name="rot"/> and moved to <paramref name="centre"/> (a shape keeps its own way round).</summary>
    public Matrix4x4 Model(byte rot, Vector3 centre) => Shape == 0
        ? Matrix4x4.CreateScale(Rot24.ApplyHalf(rot, Half) * 2) * Matrix4x4.CreateTranslation(centre)
        : Matrix4x4.CreateScale(Half * 2) * Matrix4x4.CreateFromQuaternion(Rot24.Quat(rot)) * Matrix4x4.CreateTranslation(centre);
}

/// <summary>An attachment point: where on the part (on a face of its bounds) and which way it faces (an axis, 0..5: +X -X +Y -Y +Z -Z).</summary>
public readonly record struct CSocket(string Name, Vector3 At, byte Dir, SocketKind Kind);

/// <summary>One part type, from a JSON file in Constructs/Parts (loaded by <see cref="ConstructParts"/>).</summary>
public sealed class ConstructPartDef
{
    public ushort Id; // index in ConstructParts.All (the wire id: two bytes since the routed links, 2026-10-30)
    public string Name = "";
    public string File = "";
    public string Folder = ""; // the subfolder of Constructs/Parts it is in ("" for the top; "Structure/Beams"...)
    public string Description = "";
    public string Group = "", Section = ""; // the inventory's group and section for it ("group", "section"; empty: by its role, SandboxCatalogue.cs)
    public PartRole Role;
    public ConstructTool Tool;
    public float AttachSeconds;
    public float Mass;
    public float Health;
    public float Armour; // flat damage taken off every hit
    public string Material = "steel";
    public bool PhysicsOnly; // decision 21: never an item (no hands, backpack or storage slot): welder-fitted, heavier than ConstructParts.HandCarryLimit, or "physicsOnly": true in its file
    public string PhysicsWhy = ""; // why ("welded", "heavy: 120 kg", "bulky"; empty for an item)
    public bool Core; // a construct core ("core": true): the starter block of a new construct (never a loose part; the root after seats and foundations)
    public float Friction = 0.7f;
    public PartBreak OnBreak;
    public int Scrap; // what salvaging it gives once wrecked (milestone 3: "scrap", else from its mass and material; ConstructInventory.cs)
    public CPartBox[] Boxes = []; // solid boxes first, then detail boxes
    public int SolidBoxes; // how many of Boxes are solid
    public bool HasGlass; // a box of it is a see-through glass pane ("glass": true): it shatters into small debris when it breaks
    public CSocket[] Sockets = [];
    public Vector3 BoundsMin, BoundsMax; // round every box, in the part's frame

    // Role data (only what the role uses is set)
    public float EngineForce, TopSpeed; // engine: drive force (N) shared by the wheels, top speed (m/s)
    public float WheelRadius, Grip = 1f; // wheel
    public float Travel, Frequency, Damping; // suspension: travel (m), spring frequency (Hz), damping ratio
    public float FuelCapacity; // fuel tank (litres)
    public Fluid TankFluid; // what the tank holds (routed links: the pipe style its output pre-selects)
    public bool Stage; // a damage stage of another part ("stage": true): what a wall turns into as it loses chunks; not offered in the parts menu or on a rack
    public WalkKind Walk; // building pieces: walked on ("walk": floor, stairs, ramp, ladder): players step up onto it, bots may path over it
    public float DamagedBelow; // below this share of its health it turns into DamagedTo (a wall losing a chunk; "damaged": {"below", "part"})
    public string DamagedName = ""; // the stage it turns into (resolved after loading)
    public int DamagedTo = -1; // ...its id, or -1
    public bool Offered => !Legacy && !Stage; // listed in the parts menu and on racks
    public bool Legacy; // replaced by a routed link (the Fuel Line, plain drive shafts): still loads and works, but isn't offered in the parts menu or on a rack
    public float BurnRate; // engine: litres a second at full throttle
    public Vector3 SeatAt; // seat: where the sitter's feet go (part frame)
    public byte JointAxis; // hinge: the axis it turns about (0..5)
    public Vector3 JointPivot; // hinge: a point on the axis (part frame)
    public float JointMin, JointMax; // hinge: limits (radians)
    public string LeafSocket = ""; // hinge: what attaches to this socket hangs on the joint
    public JointType JointType; // hinge: Passive; joint: the rest
    public float MotorSpeed, MotorStrength; // joint: radians (metres for a piston) a second; N m (N for a piston)
    public bool JointLimited = true; // a turntable without min/max turns forever
    public float JointGive; // joint: how far it has given at twice its rating ("give": metres for a piston, else degrees; radians here)
    public float SideLoad; // piston: the most load across its axis before it gives ("sideLoad", N; 0: no limit)
    public float OverloadDamage; // joint: hp a second at twice its rating while overloaded ("overloadDamage"; 0, the default: none)
    public int LeafIndex = -1; // hinge, joint: the leaf socket's index

    // Logic (milestone 2)
    public CLogic Logic;
    public float[] Settings = []; // the values its setting cycles through (seconds, metres, a threshold, a factor; a reader's key bits)
    public string[] SettingLabels = []; // what the HUD shows for each
    public string SettingName = ""; // "period", "key"...
    public byte DefaultSetting;

    // Settings as data (the configure tool; ConstructConfig.cs): the file's "config" schema, and each CfgKey's slot in it (-1: none)
    public CConfigDef[] Config = [];
    public sbyte[] CfgSlot = NoSlots;
    internal static readonly sbyte[] NoSlots = MakeNoSlots();

    private static sbyte[] MakeNoSlots()
    {
        var a = new sbyte[(int)CfgKey.COUNT];
        Array.Fill(a, (sbyte)-1);
        return a;
    }

    /// <summary>Has settings the configure tool can change.</summary>
    public bool Configurable => Config.Length > 0;

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
    public FaceHint Faces; // smart rotation (SmartRotation.cs): which way its face should point ("faces"; else its role's default)
    public byte FaceAxis = 5; // ...and which of its own directions is that face ("faceAxis"; -z, its front, by default)

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
    public bool Electric => Role == PartRole.Logic && Logic != CLogic.Cargo || Role == PartRole.Joint && JointType != JointType.Steering || PowerDraw > 0 || FuelKind == EngineFuel.Electric && Role == PartRole.Engine; // (steering is mechanical)
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
    // Physical storage (decision 18; ConstructStorage.cs)
    public CSlotDef[] Slots = []; // the slots parts are set into (part frame)
    public string BulkMaterial = ""; // a bin: the bulk material it holds ("scrap"; more come with resources)
    public int BulkCapacity; // a bin: how much of it
    public Vector3 FillMin, FillMax; // a bin: the box its contents fill from the bottom up (part frame)
    public bool IsBin => BulkCapacity > 0;
    public float StoreMaxMass; // the heaviest part (kg) it takes (0: any; a backpack's limit)
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

    /// <summary>
    /// Named data ports (the spawners; GameSandboxSpawns.cs): the inputs a data link can end at and the outputs it can start
    /// at. Port 0 is the part itself (every other part has just that one each way); a link names one by
    /// <see cref="ConstructRoutes.DataPort"/>. Empty: the one port.
    /// </summary>
    public string[] DataIn = [], DataOut = [];

    /// <summary>A spawner part (the Spawn category).</summary>
    public bool IsSpawner => Logic is CLogic.BotSpawner or CLogic.ZombieSpawner;

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
            CLogic.BotSpawner or CLogic.ZombieSpawner => 6, // (over its three inputs)
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

    /// <summary>The most part types (ids are two bytes on the wire since protocol 87; 65535 means none).</summary>
    public const int MaxTypes = 4096;

    public const ushort None = ushort.MaxValue;

    /// <summary>
    /// Decision 21: the heaviest part that is still an item (kg; the backpack's limit). Heavier parts, welder-fitted ones
    /// and those whose file says <c>"physicsOnly": true</c> exist only as bodies in the world (<see cref="ConstructPartDef.PhysicsOnly"/>).
    /// </summary>
    public const float HandCarryLimit = 40f;

    /// <summary>How much of a <c>"glass": true</c> box is drawn (its opacity; the props' glass is 0.4, Props/README.md).</summary>
    public const float DefaultGlass = 0.3f;

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
        var detail = Environment.GetCommandLineArgs().Any(a => a.Equals("--parts", StringComparison.OrdinalIgnoreCase)); // (--parts: every blueprint's parts and where they sit)
        foreach (var b in ConstructBuild.Blueprints)
        {
            Console.WriteLine($"OK  blueprint {b.Name}: {b.Steps.Count} steps, {ConstructDrive.Describe(b)} ({b.File})");
            if (detail && ConstructBuild.Build(new ConstructWorld(), b, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, out _) is { } c)
                for (var i = 0; i < c.PartCount; i++)
                {
                    c.PartBounds(i, out var mn, out var mx);
                    Console.WriteLine($"      {i,2} {c.DefOf(i).Name,-24} {mn.X:0.00} {mn.Y:0.00} {mn.Z:0.00} .. {mx.X:0.00} {mx.Y:0.00} {mx.Z:0.00}");
                }
        }
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

    public static ConstructPartDef? Get(ushort id) => id < All.Count ? All[id] : null;

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
                def.Folder = (Path.GetDirectoryName(Path.GetRelativePath(folder, file)) ?? "").Replace('\\', '/');
                if (!names.Add(def.Name))
                    throw new ConstructPartException($"{Path.GetFileName(file)}: another file already has the name \"{def.Name}\"");
                if (list.Count >= MaxTypes)
                    throw new ConstructPartException($"{Path.GetFileName(file)}: more than {MaxTypes} part types");
                def.Id = (ushort)list.Count;
                list.Add(def);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                refused.Add(ex is ConstructPartException ? ex.Message : $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        LinkStages(list, refused);
        return list.ToArray();
    }

    /// <summary>
    /// Resolves the damage stages ("damaged": a wall that loses chunks turns into its stage part in place): the stage must
    /// exist, be marked "stage", have the same role and exactly the same sockets (so every link stays), and the chain's
    /// thresholds must fall. A bad link is refused with the reason and dropped (the part still loads, breaking whole).
    /// </summary>
    private static void LinkStages(List<ConstructPartDef> list, List<string> refused)
    {
        foreach (var d in list)
        {
            if (d.DamagedName.Length == 0)
                continue;
            var t = list.FirstOrDefault(x => x.Name.Equals(d.DamagedName, StringComparison.OrdinalIgnoreCase));
            string? why = t == null ? $"no part named \"{d.DamagedName}\""
                : !t.Stage ? $"{t.Name} isn't marked \"stage\": true"
                : t.Role != d.Role ? $"{t.Name} is a {t.Role.ToString().ToLowerInvariant()}, not a {d.Role.ToString().ToLowerInvariant()}"
                : t.Sockets.Length != d.Sockets.Length || t.Sockets.Where((s, i) => s.Name != d.Sockets[i].Name || s.Dir != d.Sockets[i].Dir || s.Kind != d.Sockets[i].Kind || Vector3.DistanceSquared(s.At, d.Sockets[i].At) > 1e-6f).Any()
                    ? $"{t.Name}'s sockets aren't exactly {d.Name}'s"
                : t.DamagedName.Length > 0 && list.FirstOrDefault(x => x.Name.Equals(t.DamagedName, StringComparison.OrdinalIgnoreCase)) is { } next && t.DamagedBelow >= d.DamagedBelow
                    ? $"{t.Name} turns into {next.Name} below {t.DamagedBelow:0.##}, not below {d.DamagedBelow:0.##}"
                : null;
            if (why != null)
            {
                refused.Add($"{d.File}: \"damaged\": {why}");
                d.DamagedName = "";
                continue;
            }
            d.DamagedTo = t!.Id;
        }
    }

    // ---- Parsing ---------------------------------------------------------------------------------------------

    private static readonly string[] TopKeys =
    [
        "format", "name", "description", "role", "tool", "attachSeconds", "mass", "health", "armour", "material", "friction",
        "onBreak", "color", "boxes", "sockets", "engine", "wheel", "suspension", "fuelTank", "seat", "hinge", "joint", "logic",
        "transmission", "battery", "alternator", "hitch", "power", "dial", "gun", "gunPart", "ammoBox", "scrap", "core", "group", "section", "storage", "differential", "drive", "config", "legacy", "walk", "damaged", "stage", "faces", "faceAxis",
        "physicsOnly",
    ];
    /// <summary>The keys a part box takes: its place, colour, detail and hidden flags, a contoured shape with its settings (index 8 up to the last), and the glass flag (last).</summary>
    /// <summary>The keys a part box takes: its place, colour, detail and hidden flags, and a contoured shape with its settings.</summary>
    private static readonly string[] BoxKeys =
        ["min", "max", "at", "size", "color", "detail", "hidden", "shape", "segments", "inner", "ends", "taper", "shift", "radius", "edges", "angle", "dir", "glass"];

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
        d.Group = Opt(root, "group") is { } grp ? AsciiText(grp, file, "group") : "";
        d.Section = Opt(root, "section") is { } sec ? AsciiText(sec, file, "section") : "";
        if (d.Group.Length > 32 || d.Section.Length > 32 || (d.Section.Length > 0 && d.Group.Length == 0))
            throw new ConstructPartException($"{file}: \"group\" and \"section\" are at most 32 characters, and a \"section\" needs a \"group\"");
        d.Role = EnumOf<PartRole>(StrKey(root, "role", file), file, "role");
        d.Tool = EnumOf<ConstructTool>(StrKey(root, "tool", file), file, "tool");
        if (d.Tool is ConstructTool.None or ConstructTool.Linker or ConstructTool.Configure)
            throw new ConstructPartException($"{file}: \"tool\" is \"wrench\" or \"welder\"");
        d.AttachSeconds = Opt(root, "attachSeconds") is { } secs ? Num(secs, file, "attachSeconds") : d.Tool == ConstructTool.Welder ? 2.5f : 1.5f;
        Range(d.AttachSeconds, 0.2f, 20f, file, "attachSeconds");
        d.Legacy = Opt(root, "legacy") is { ValueKind: JsonValueKind.True };
        d.Stage = Opt(root, "stage") is { ValueKind: JsonValueKind.True };
        d.Walk = Opt(root, "walk") is { } walk ? EnumOf<WalkKind>(Str(walk, file, "walk"), file, "walk") : WalkKind.None;
        if (Opt(root, "damaged") is { } dmg)
        {
            if (dmg.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: \"damaged\" is {{\"below\": a share of its health, \"part\": the stage it turns into}}");
            Known(dmg, ["below", "part"], $"{file}, damaged");
            d.DamagedBelow = Num(Need(dmg, "below", $"{file}, damaged"), file, "damaged.below");
            Range(d.DamagedBelow, 0.05f, 0.95f, file, "damaged.below");
            d.DamagedName = StrKey(dmg, "part", $"{file}, damaged");
        }
        d.Mass = Num(Need(root, "mass", file), file, "mass");
        Range(d.Mass, 0.5f, 50000f, file, "mass"); // (building pieces: a 4 m concrete pad is about 14 t)
        // Decision 21: what can't be carried as an item, by the tool that fits it and its mass (so every new welder or heavy part follows without a list).
        var bulky = Opt(root, "physicsOnly") is { } po && (po.ValueKind == JsonValueKind.True ? true : po.ValueKind == JsonValueKind.False ? false : throw new ConstructPartException($"{file}: \"physicsOnly\" is true or false"));
        d.PhysicsWhy = d.Tool == ConstructTool.Welder ? "welded" : d.Mass > HandCarryLimit ? $"heavy: {d.Mass:0} kg" : bulky ? "bulky" : "";
        d.PhysicsOnly = d.PhysicsWhy.Length > 0;
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
        d.Core = Opt(root, "core") is { } core && (core.ValueKind == JsonValueKind.True ? true : core.ValueKind == JsonValueKind.False ? false : throw new ConstructPartException($"{file}: \"core\" is true or false"));
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
            Known(b, BoxKeys, $"{file}, {where}");
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
            var detail = Opt(b, "detail") is { ValueKind: JsonValueKind.True };
            var hidden = Opt(b, "hidden") is { ValueKind: JsonValueKind.True };
            if (hidden && detail)
                throw new ConstructPartException($"{file}: {where} is a detail (drawn only) and hidden (never drawn): a hidden box is a solid one's collision");
            var shape = ShapeOf(b, max - min, detail, file, where);
            var glass = 0f;
            if (Opt(b, "glass") is { } gl)
            {
                glass = gl.ValueKind switch
                {
                    JsonValueKind.True => DefaultGlass,
                    JsonValueKind.False => 0f,
                    JsonValueKind.Number when gl.GetSingle() is var gv && gv >= 0.1f && gv <= 0.9f => gv,
                    _ => throw new ConstructPartException($"{file}: {where}.glass is true, or how much of the pane is drawn (0.1 to 0.9)"),
                };
                if (glass > 0 && hidden)
                    throw new ConstructPartException($"{file}: {where} is hidden (never drawn) and glass (drawn see-through)");
            }
            list.Add(new CPartBox((min + max) / 2, (max - min) / 2, c, detail, shape, hidden, glass));
        }
        if (list.All(x => x.Detail))
            throw new ConstructPartException($"{file}: every box is a detail: at least one must be solid");
        if (list.All(x => x.Hidden))
            throw new ConstructPartException($"{file}: every box is hidden: at least one must be drawn");
        // Solid boxes first (the physics walks those), details after.
        d.Boxes = list.Where(x => !x.Detail).Concat(list.Where(x => x.Detail)).ToArray();
        d.SolidBoxes = list.Count(x => !x.Detail);
        d.HasGlass = list.Any(x => x.IsGlass);
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
            if (MathF.Abs(at[axis] - face) > 0.005f && !OnBoxFace(d, at, dir))
                throw new ConstructPartException($"{file}: socket \"{name}\" faces {DirName(dir)} but is not on that face of the part (at {at[axis]:0.###}, the face is at {face:0.###}), nor on that face of one of its solid boxes");
            for (var a = 0; a < 3; a++)
                if (a != axis && (at[a] < d.BoundsMin[a] - 0.005f || at[a] > d.BoundsMax[a] + 0.005f))
                    throw new ConstructPartException($"{file}: socket \"{name}\" is outside the part");
            socks.Add(new CSocket(name, at, dir, kind));
        }
        d.Sockets = socks.ToArray();

        // Role data
        string[] roleKeys = ["engine", "wheel", "suspension", "fuelTank", "seat", "hinge", "joint", "logic", "transmission", "battery", "alternator", "hitch", "gun", "gunPart", "ammoBox", "storage", "differential", "drive"];
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
            PartRole.Storage => "storage",
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
                Known(block!.Value, ["capacity", "fluid"], bw);
                d.FuelCapacity = Num(Need(block.Value, "capacity", bw), file, "fuelTank.capacity");
                Range(d.FuelCapacity, 0.1f, 10000, file, "fuelTank.capacity");
                d.TankFluid = Opt(block.Value, "fluid") is { } fl ? EnumOf<Fluid>(Str(fl, file, "fuelTank.fluid"), file, "fuelTank.fluid") : Fluid.Fuel; // (routed links: a pipe's style)
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
            case PartRole.Storage:
                ConstructStorage.ParseStorage(d, block!.Value, file);
                break;
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
        SmartRotation.ParseFaces(d, Opt(root, "faces"), Opt(root, "faceAxis"), file);
        ConstructConfig.Parse(d, root, file); // (last: it folds in the role's own choice and checks the keys fit the part)
        return d;
    }

    private static readonly string[] JointTypes = ["motor", "turntable", "piston", "steering"];

    /// <summary>
    /// A box's contoured shape (the weapon builder's <see cref="PartShapes"/>; 0: a plain box). Only a detail box may have
    /// one: collision and hit areas stay boxes, so a shaped part draws its shape over hidden solid boxes that stand for it.
    /// </summary>
    private static ushort ShapeOf(JsonElement b, Vector3 size, bool detail, string file, string where)
    {
        var shape = Opt(b, "shape") is { } sh ? Str(sh, file, where + ".shape").ToLowerInvariant() : "box";
        if (shape == "box")
        {
            foreach (var k in BoxKeys.AsSpan(8, BoxKeys.Length - 9))
                if (Opt(b, k) != null)
                    throw new ConstructPartException($"{file}: {where}: \"{k}\" is a shape's setting (give the box a \"shape\")");
            return 0;
        }
        if (Array.IndexOf(PartShapes.Names, shape) < 0)
            throw new ConstructPartException($"{file}: {where}.shape is box or one of {string.Join(", ", PartShapes.Names)}, not \"{shape}\"");
        if (!detail)
            throw new ConstructPartException($"{file}: {where} has a shape, so it must be a detail (\"detail\": true): collision stays boxes (give the part hidden solid boxes)");
        var settings = PartShapes.SettingsOf(shape);
        foreach (var k in BoxKeys.AsSpan(8, BoxKeys.Length - 9))
            if (Opt(b, k) != null && Array.IndexOf(settings, k) < 0)
                throw new ConstructPartException($"{file}: {where}: a {shape} takes {(settings.Length > 0 ? string.Join(", ", settings) : "no settings")}, not \"{k}\"");
        var pb = new PartBox { Shape = shape, Size = size };
        if (Opt(b, "segments") is { } sg)
        {
            pb.Segments = (int)Num(sg, file, where + ".segments");
            Range(pb.Segments, 3, 48, file, where + ".segments");
        }
        if (Opt(b, "inner") is { } inn)
        {
            pb.Inner = Num(inn, file, where + ".inner");
            Range(pb.Inner, 0, 0.98f, file, where + ".inner");
        }
        if (Opt(b, "ends") is { } en)
        {
            pb.Ends = Str(en, file, where + ".ends");
            if (pb.Ends is not ("closed" or "open" or "front" or "back"))
                throw new ConstructPartException($"{file}: {where}.ends is closed, open, front or back");
        }
        if (Opt(b, "taper") is { } tp)
        {
            if (tp.ValueKind != JsonValueKind.Array || tp.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: {where}.taper is [x, y]");
            pb.Taper = new Vector2(Num(tp[0], file, where + ".taper"), Num(tp[1], file, where + ".taper"));
            Range(MathF.Min(pb.Taper.X, pb.Taper.Y), 0, 1, file, where + ".taper");
            Range(MathF.Max(pb.Taper.X, pb.Taper.Y), 0, 1, file, where + ".taper");
        }
        if (Opt(b, "shift") is { } sf)
        {
            if (sf.ValueKind != JsonValueKind.Array || sf.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: {where}.shift is [x, y]");
            pb.Shift = new Vector2(Num(sf[0], file, where + ".shift"), Num(sf[1], file, where + ".shift"));
            Range(MathF.Min(pb.Shift.X, pb.Shift.Y), -0.5f, 0.5f, file, where + ".shift");
            Range(MathF.Max(pb.Shift.X, pb.Shift.Y), -0.5f, 0.5f, file, where + ".shift");
        }
        if (Opt(b, "radius") is { } rd)
        {
            pb.Radius = Num(rd, file, where + ".radius");
            Range(pb.Radius, 0.01f, 0.5f, file, where + ".radius");
        }
        if (Opt(b, "edges") is { } ed)
        {
            pb.Edges = Str(ed, file, where + ".edges");
            if (pb.Edges is not ("all" or "long"))
                throw new ConstructPartException($"{file}: {where}.edges is all or long");
        }
        if (Opt(b, "angle") is { } an)
        {
            pb.Angle = Num(an, file, where + ".angle");
            Range(pb.Angle, 1, 360, file, where + ".angle");
        }
        if (Opt(b, "dir") is { } dr)
        {
            pb.Dir = Num(dr, file, where + ".dir");
            Range(pb.Dir, -360, 360, file, where + ".dir");
        }
        try
        {
            return PartShapes.For(pb);
        }
        catch (WeaponFileException ex)
        {
            throw new ConstructPartException($"{file}: {where}: {ex.Message}");
        }
    }

    /// <summary>A moving joint's block: type, axis, pivot, limits, its leaf socket (a struct socket: anything can go on it), speed and strength.</summary>
    private static void ParseJoint(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["type", "axis", "pivot", "min", "max", "leaf", "speed", "strength", "give", "sideLoad", "overloadDamage"], bw);
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
        // The strength model (ConstructPhysics.cs): how far it gives above its rating, side loads, overload damage (off unless given).
        d.JointGive = Opt(b, "give") is { } gv ? Num(gv, file, "joint.give") * unit : piston ? 0.25f * d.JointMax : 20 * MathF.PI / 180f;
        Range(d.JointGive, 0.001f, piston ? 4f : MathF.PI, file, "joint.give");
        d.SideLoad = Opt(b, "sideLoad") is { } sl ? Num(sl, file, "joint.sideLoad") : 0f;
        Range(d.SideLoad, 0, 10_000_000, file, "joint.sideLoad");
        if (d.SideLoad > 0 && !piston)
            throw new ConstructPartException($"{file}: only a piston takes \"sideLoad\"");
        d.OverloadDamage = Opt(b, "overloadDamage") is { } od ? Num(od, file, "joint.overloadDamage") : 0f;
        Range(d.OverloadDamage, 0, 100_000, file, "joint.overloadDamage");
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
        if (d.IsSpawner)
        {
            d.DataIn = ["enabled", "spawn now", "rate"]; // (GameSandboxSpawns.cs: on/off, a pulse, spawns a minute)
            d.DataOut = ["alive", "spawned"]; // (how many it has alive; a pulse each time it spawns)
        }
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

    /// <summary>
    /// Is a socket at <paramref name="at"/> facing <paramref name="dir"/> on that face of one of the part's solid boxes
    /// (inside an opening: a door's jamb, a window's sill, the edge of a floor's stairwell)?
    /// </summary>
    private static bool OnBoxFace(ConstructPartDef d, Vector3 at, byte dir)
    {
        var axis = dir >> 1;
        for (var b = 0; b < d.SolidBoxes; b++)
        {
            var box = d.Boxes[b];
            var face = (dir & 1) == 0 ? box.Center[axis] + box.Half[axis] : box.Center[axis] - box.Half[axis];
            if (MathF.Abs(at[axis] - face) > 0.005f)
                continue;
            var inside = true;
            for (var a = 0; a < 3 && inside; a++)
                if (a != axis && MathF.Abs(at[a] - box.Center[a]) > box.Half[a] + 0.005f)
                    inside = false;
            if (inside)
                return true;
        }
        return false;
    }

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

    internal static byte DirOf(string s, string file, string where)
    {
        var i = Array.IndexOf(DirNames, s.ToLowerInvariant());
        return i >= 0 ? (byte)i : throw new ConstructPartException($"{file}: {where}: a direction is one of {string.Join(", ", DirNames)}");
    }

    internal static void Known(JsonElement obj, string[] keys, string where)
    {
        foreach (var p in obj.EnumerateObject())
            if (!keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ConstructPartException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", keys)})");
    }

    internal static JsonElement? Opt(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value;
        return null;
    }

    internal static JsonElement Need(JsonElement obj, string name, string where) =>
        Opt(obj, name) ?? throw new ConstructPartException($"{where}: missing \"{name}\"");

    private static string StrKey(JsonElement obj, string name, string file) => Str(Need(obj, name, file), file, name);

    internal static string Str(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ConstructPartException($"{file}: \"{what}\" must be text");

    private static string AsciiText(JsonElement e, string file, string what)
    {
        var s = Str(e, file, what);
        return s.Any(c => c < 32 || c > 126) ? throw new ConstructPartException($"{file}: \"{what}\" must be plain ASCII") : s;
    }

    internal static float Num(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new ConstructPartException($"{file}: \"{what}\" must be a number");

    internal static void Range(float v, float lo, float hi, string file, string what)
    {
        if (v < lo || v > hi)
            throw new ConstructPartException($"{file}: \"{what}\" is {v.ToString(CultureInfo.InvariantCulture)}, it must be {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
    }

    internal static Vector3 Vec(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], file, what), Num(e[1], file, what), Num(e[2], file, what))
            : throw new ConstructPartException($"{file}: \"{what}\" must be [x, y, z]");

    internal static Vector3 Colour(JsonElement e, string file, string what)
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

    /// <summary>The turn that does <paramref name="b"/> and then <paramref name="a"/> (Apply(result, v) == Apply(a, Apply(b, v))).</summary>
    public static byte Compose(byte a, byte b)
    {
        var x = ApplyDir(a, ApplyDir(b, 0));
        var y = ApplyDir(a, ApplyDir(b, 2));
        for (byte r = 0; r < Count; r++)
            if (ApplyDir(r, 0) == x && ApplyDir(r, 2) == y)
                return r;
        return 0;
    }

    /// <summary>The turn nearest a rotation, and how near (|dot| of the quaternions: 1 is exact).</summary>
    public static byte Nearest(Quaternion q, out float match)
    {
        byte best = 0;
        match = -1;
        for (byte r = 0; r < Count; r++)
        {
            var d = MathF.Abs(Quaternion.Dot(Quats[r], q));
            if (d > match)
            {
                match = d;
                best = r;
            }
        }
        return best;
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
