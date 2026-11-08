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
    Hose, // world links (board item wl01): a hose outlet (a reel holding a hose with a nozzle or plug at its end) or a hose inlet (a coupler socket) (ConstructHoses.cs)
    EnginePiece, // a modular engine's crank section, cylinder, fuel system, booster, starter, exhaust or smokestack (its flywheel is the Engine; ConstructEngine.cs)
    Generator, // big drive: a power plant (a boiler, a furnace, a genset, a reactor): feeds the circuit it is on with no engine (ConstructPlant.cs)
    Thruster, // big drive: a force at a point along its axis, from fuel or power (ConstructThrust.cs)
    FluidTank, // holds one fluid of any kind on its own, a pipe's input and output (ConstructFluid.cs)
    AmmoSwitch, // several ammo feeds in, one out, a data input picking which (ConstructAmmo.cs)
    Burner, // a balloon's burner: fuel to heat in the lift cell up its flame (ConstructLift.cs)
    Rotor, // a propeller, rotor or ducted fan on a shaft or its own motor (ConstructRotor.cs)
}

/// <summary>A lift cell's envelope (a fluid tank's <c>"envelope"</c>; ConstructLift.cs).</summary>
public sealed class EnvelopeDef
{
    public float Skin; // kg a square metre of its box
    public float Side; // the longest side its box takes, metres
    public float Patch; // m2 of hole one patch closes
    public bool Vacuum; // rigid: a powered pump empties it, and it collapses when breached
}

/// <summary>A hose part's end (board item wl01): an outlet holds the hose on a reel, an inlet is the socket its receiver plugs into.</summary>
public enum HoseEnd : byte
{
    Outlet,
    Inlet,
}

/// <summary>What a hose carries (its variant). Wire bytes: add at the end.</summary>
public enum HoseCarry : byte
{
    Fluid, // a fluid hose (decision 28: one hose for any fluid): what the outlet construct's tanks hold into tanks of the inlet construct that hold the same
    Power, // a power lead: charge from the outlet construct's batteries into the inlet construct's
    Data, // a data lead: the signal wired into the outlet comes out of the inlet
    Items, // a cargo hose (ConstructFlexChute.cs): from the chutes into the outlet to the chutes out of the inlet
    Ammo, // an ammo reel: a gun past it draws from the feeds into the reel, across joints or between constructs (ConstructAmmo.cs)
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
    Harpoon, // a barbed head flying out on its line
}

/// <summary>What a construct gun's rounds do (the weapon builder's ammo flags that count against constructs).</summary>
[Flags]
public enum GunEffect : byte
{
    None = 0,
    Penetrating = 1, // on through a part into what is behind it
    Explosive = 2, // a splash where it lands
    Incendiary = 4, // sets fuel, wood and ammo alight
    Flame = 8, // fire, not a round: heat gets past armour, ranges end (the "flame" flag)
    Harpoon = 16, // a barbed head on a rope (the "harpoon" flag; ConstructHarpoon.cs)
    Shell = 32, // a real shell on an arc (the "artillery" flag; ConstructShells.cs)
}

/// <summary>How a joint part moves (milestone 2; docs/sandbox/CONSTRUCTS.md, "Joints").</summary>
public enum JointType : byte
{
    Passive, // the milestone 1 hinge: swings freely, latched open or shut by Interact; a signal wired to it locks it
    Motor, // a motorised hinge: driven to an angle (signal 1: max, 0: rest, -1: min)
    Turntable, // turns at a speed (signal -1..1 of its speed), no limits unless given
    Piston, // slides along its axis (signal 0..1 of its travel)
    Steering, // a steering pivot: follows the driver's steering, or a wired signal (-1..1)
    Fixed, // a freeform weld (board item fw01; ConstructFreeform.cs): no joint part and no freedom, the segment sits at CJoint.FixRot / FixPos on its parent (wire byte 5, protocol 97)
    Flex, // a flexible joint (ConstructFlex.cs): passive, springs back; two or three joints of this type in a row at one pivot make its ball (wire byte 6, protocol 117)
    Slider, // a free linear rail (ConstructLinear.cs): slides with no motor between its end stops; a signal locks it (wire byte 7, protocol 135)
    Swivel, // a free rotation part (ConstructLinear.cs): turns with no motor, no spring and by default no limits; a signal locks it (wire byte 8, protocol 137)
}

/// <summary>What moves a linear rail's carriage (ConstructLinear.cs; "rail" in a joint block).</summary>
public enum RailDriveKind : byte
{
    None, // not a rail
    Free, // nothing: JointType.Slider
    Electric, // a motor on the circuit: JointType.Piston with a rail
    Shaft, // a drive shaft through a lead screw: JointType.Piston with a rail
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
    Launch, // a catapult (ConstructLauncher.cs): a rising input or Interact fires what stands in its zone along its rail; puts out 1 while firing
    Arrest, // arrestor gear (ConstructLauncher.cs): on while its input is (unwired: always), slows what moves through its zone
    Conveyor, // a conveyor (ConstructDeck.cs): its belt runs at its input (-1..1) or, unwired, while Interact has it on
    Director, // a fire director (ConstructWeapons.cs): the guns it is wired into aim where its gunner looks and fire on their fire key
    GravityWell, // phase 5 (ConstructGravity.cs): pulls (or, on a negative input, pushes) everything near it; on at its input (-1..1) or, unwired, while Interact has it on
    GravityGen, // phase 5 (ConstructGravity.cs): its own "down" (the part's -Y) inside its box; on while its input is, or, unwired, while Interact has it on
    JumpDrive, // phase 6 (Game/GameSpaceJump.cs): in space, a rising input, Interact or (unwired) the driver's jump key starts or stops its charge; charged, the ship jumps to the system its "jumpTarget" names; puts out how far charged (0..1)
    AnalogLever, // a lever dragged by hand (AnalogLever.cs): its "value" setting, 0..1, mapped onto its "low" to "high" output
    DriveModule, // a Driving Module (ConstructModules.cs): the seat linked into it drives the construct through its drive output; its other outputs are the same controls one by one
    TurretModule, // a Turret Module (ConstructModules.cs): the seat linked into it aims and fires the guns its guns output is linked to; yaw, pitch, fire, aim and reload for wiring by hand
    Xor, // on when an odd number of its inputs are on (LogicBlocks.cs, from here to BusOut: docs/sandbox/CONSTRUCTS.md "Logic blocks")
    Nand, // off only when every input is on
    MathBlock, // ports a and b: add, subtract, multiply, divide, min or max, by its operator
    Latch, // set turns it on, reset off (reset wins)
    Counter, // up, down, reset (rising edges), up to its limit
    Memory, // stores value on a rising store
    Filter, // follows its input through a clamp, a smoothing time and a rate limit
    Mux, // puts out the input its select names
    RandomPulse, // a new random number on each rising input
    Numpad, // keys type a number, Accept puts it out, Clear resets (LogicInputs.cs)
    CodeLock, // keys type a code, Accept with the right one turns it on (LogicInputs.cs)
    KeySwitch, // a switch only its owner, or someone holding a Key, can turn
    Display, // shows its inputs on its panels (LogicDisplays.cs)
    Instrument, // shows its construct's state on its panels, no wiring (LogicDisplays.cs)
    AltitudeSensor, // metres above the world's zero
    TiltSensor, // pitch, and roll on its second output (degrees)
    HeadingSensor, // the way its front faces, degrees clockwise from north
    LevelSensor, // how full the tank or store its watch link ends at is, %
    DamageSensor, // on for its hold time after the part its watch link ends at is hit
    OccupancySensor, // on while the seat wired into it (unwired: the drive seat) has someone in it
    RadioTx, // sends its input on its channel (ConstructRadio.cs)
    RadioRx, // the nearest sender on its channel in range
    BusIn, // eight signals down one link (LogicBlocks.cs)
    BusOut, // splits a Data Bus link back into eight
    RemoteLink, // a Remote Transmitter: the seat wired into it sends its controls on a channel (ConstructRemote.cs)
    FlightStabilizer, // a seat's axes in, mixed channels out, with its own sensors and autopilot (ConstructFlight.cs)
    Decoupler, // a rising input splits the construct here into two live bodies (ConstructBoost.cs)
    AntiGrav, // on power cancels a share of its construct's weight, within its radius (ConstructBoost.cs)
    Mooring, // a mast: on (unwired: always), it ropes a balloon in reach; off or Interact casts it off (ConstructCarrier.cs)
    BombRack, // lets go of the explosive parts bolted to it, one at a time while its input is on (ConstructOrdnance.cs)
}

/// <summary>The tool a part is attached and removed with (decision 2: editing always needs a tool in hand).</summary>
public enum ConstructTool : byte
{
    None, // bare hands or a gun: use only (drive, sit, open doors), never edit
    Wrench, // retired (the attach tool does its work): byte 1 stays reserved, never reused; the wire and saves read it as Welder, and a part file's "tool": "wrench" still means an item. Was: small parts that are items: lamps, sensors, logic, gun parts, small gearboxes... (decision 27)
    Welder, // every physics-only piece: frames, panels, foundations, engines, big gearboxes, big wheels (decision 27)
    Linker, // milestone 2: the linking tool: wires (outputs to inputs) and logic settings; never fits or removes a part
    Configure = 4, // the configure tool: a part's settings panel (ConstructConfig.cs); never fits or removes a part
    GroundDrill = 7, // the ground drill (GameSandboxSculpt.cs): digs the ground into carried dirt, stone and sand and puts it back; never fits or removes a part
    Paint = 9, // the paint tool (ConstructPaint.cs): colours parts and links; never fits or removes a part
    Remover = 10, // the remover tool (ConstructRemover.cs): selects parts and links and deletes them outright; never fits or removes a part the usual way
    Selector = 11, // the selector (ConstructEditsSelect.cs, Program.SandboxSelect.cs): selects groups of parts to move, copy, delete, paint, weld or save; a Selector Station's aid
    Tracks = 13, // the track tool (ConstructTracks.cs, Program.SandboxTracks.cs): picks wheels round a loop and lays a track on them; never fits or removes a part
    Hands = 12, // bare hands out (ConstructEditsHands.cs, Program.SandboxHands.cs): LMB takes a loose part or a stored one into the hands, RMB drops what they hold; never fits or removes a part
    Nudge = 14, // the Nudge tool (ConstructNudge.cs, Program.SandboxNudge.cs): moves and turns whole constructs by a triad and arcs; never fits or removes a part
    NoCollide = 15, // the no-collide tool (ConstructNoCollide.cs, Program.SandboxNoCollide.cs): two pieces stop colliding, or collide again; never fits or removes a part
    Texture = 16, // the texture paint tool (ConstructFinish.cs): puts a material on parts; never fits or removes a part
    Material = 18, // the material painter (PartMaterials.cs, Program.SandboxMaterial.cs): makes parts of another material, the same shape; swaps the part's type in place
    Fuse = 17, // the fuse tool (ConstructFuse.cs, Program.SandboxFuse.cs): fuses parts into one unit and un-fuses them; never fits or removes a part
    PhysicsGun = 8, // the physics gun (ConstructGrab.cs): grabs, moves, turns and freezes whole constructs; never fits or removes a part (8: after the tools still to come, ConstructTools.Keys)
}

/// <summary>The tools a wire byte may name.</summary>
public static class ConstructToolWire
{
    /// <summary>What a player calls a tool in running text ("needs the attach tool"). The enum keeps its old name: the byte and the code never changed when the welder was renamed.</summary>
    public static string Label(this ConstructTool t) => t is ConstructTool.Welder or ConstructTool.Wrench ? "attach tool" : t.ToString().ToLowerInvariant();

    /// <summary>A tool that may have come from before the wrench was retired: it is the attach tool now.</summary>
    public static ConstructTool Current(ConstructTool t) => t == ConstructTool.Wrench ? ConstructTool.Welder : t;

    /// <summary>A tool from the wire (anything unknown: none; the retired wrench byte: the attach tool).</summary>
    public static ConstructTool From(byte b) => b == (byte)ConstructTool.Wrench ? ConstructTool.Welder : b <= (byte)ConstructTool.Configure || b is (byte)ConstructTool.PhysicsGun or (byte)ConstructTool.Paint or (byte)ConstructTool.Remover or (byte)ConstructTool.GroundDrill or (byte)ConstructTool.Selector or (byte)ConstructTool.Hands or (byte)ConstructTool.Tracks or (byte)ConstructTool.Nudge or (byte)ConstructTool.NoCollide or (byte)ConstructTool.Texture or (byte)ConstructTool.Fuse or (byte)ConstructTool.Material ? (ConstructTool)b : ConstructTool.None;
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
    Die, // an Extruder's output: only the piece growing on it goes there (ConstructExtruder.cs)
    Port, // only what a shaft port stands on (a suspension's Drive in, on its moving hub): nothing bolts here
    PressDie, // a press's die slot and a die's mount: only a die of its size class goes there (ConstructPress.cs)
}

/// <summary>
/// One box of a part's model and collision, in the part's own frame (metres). A detail box is drawn only: it adds no
/// bounds, mass, collision or hit area (and may stick out past a face). <see cref="Shape"/>: a detail box drawn as one of the weapon builder's contoured shapes (<see cref="PartShapes"/>: wedge,
/// arc, cylinder...; 0 = a plain box). <see cref="Hidden"/>: a solid box that is never drawn (a wedge's stepped collision
/// under its smooth shape). <see cref="Glass"/>: a see-through pane (how much of it is drawn, 0.1 to 0.9; 0 = opaque), drawn
/// in the blended pass after everything opaque (ConstructRender.DrawGlass) and passed through by bullets, which break it.
/// <see cref="Front"/>: which way a shape's front end (its -Z, the end a taper narrows) points in the part ("front" in the file): 0 = -Z, as a shape is made,
/// else 1 + an axis (0..5: +X -X +Y -Y +Z -Z). <see cref="Half"/> stays the box the shape fills in the part's frame.
/// </summary>
public readonly record struct CPartBox(Vector3 Center, Vector3 Half, Vector3 Color, bool Detail = false, ushort Shape = 0, bool Hidden = false, float Glass = 0, bool Paintable = true, float Tone = 1f, byte Side = 0, bool Glow = false, float Pump = 0, bool Spin = false, bool Roll = false)
{
    private readonly ushort _look = BoxLooks.Of(Color, Tone, 0); // (its colour, tone and material: one row of a shared table, BoxLooks)

    /// <summary>The box's own colour, read through its look.</summary>
    public Vector3 Color
    {
        get => BoxLooks.Get(_look).Color;
        init => _look = BoxLooks.Of(value, BoxLooks.Get(_look).Tone, BoxLooks.Get(_look).Surface);
    }

    /// <summary>Its colour's tone against the part's main colour (see <see cref="ConstructPaint.ToneOf"/>).</summary>
    public float Tone
    {
        get => BoxLooks.Get(_look).Tone;
        init => _look = BoxLooks.Of(BoxLooks.Get(_look).Color, value, BoxLooks.Get(_look).Surface);
    }

    /// <summary>Its shared material: a row of <see cref="Surfaces"/> (SurfaceRules gives it at load; 0: none yet).</summary>
    public byte Surface
    {
        get => BoxLooks.Get(_look).Surface;
        init => _look = BoxLooks.Of(BoxLooks.Get(_look).Color, BoxLooks.Get(_look).Tone, value);
    }

    public bool IsGlass => Glass > 0;

    public byte Front { get; init; }

    /// <summary>What a key box types ("key": "7"; a Numpad's or Code Lock's: LogicInputs.cs); 0: not a key.</summary>
    public char Key { get; init; }
    /// <summary>"travel": how far a "slide" or "press" rig moves.</summary>
    public float RigTravel { get; init; }
    /// <summary>"use": true: this box is what a hand works (a lever's handle, a button's cap): the aim glow covers these and not the whole part (<see cref="ConstructPartDef.UseBoxes"/>).</summary>
    public bool Use { get; init; }

    /// <summary>How a suspension's box moves with its wheel (<see cref="BoxRig"/>, "rig" in the file; docs/sandbox/CONSTRUCTS.md "Suspension types"). Its points are in the part's frame.</summary>
    public BoxRig Rig { get; init; }

    /// <summary>A rig's pivot ("pivot"): where an arm box turns; on a leg, where its moving end turns (<see cref="RigPivoted"/>; without one that end moves with the hub).</summary>
    public Vector3 RigPivot { get; init; }

    public bool RigPivoted { get; init; }

    /// <summary>A leg's fixed end ("from") and moving end at rest ("to"); "span" is the share of the way between them it draws.</summary>
    public Vector3 LegFrom { get; init; }

    public Vector3 LegTo { get; init; }

    public float SpanFrom { get; init; }

    public float SpanTo { get; init; }

    /// <summary>A joint part's box on the fixed side (<see cref="JointSides.Base"/>) or the moving side (<see cref="JointSides.Leaf"/>); 0 elsewhere ("side" in the file).</summary>
    public bool IsBase => Side == JointSides.Base;
    public bool IsLeaf => Side == JointSides.Leaf;


    /// <summary>The box's model matrix in its part's frame turned by <paramref name="rot"/> and moved to <paramref name="centre"/> (a shape keeps its own way round).</summary>
    public Matrix4x4 Model(byte rot, Vector3 centre) => Shape == 0
        ? Matrix4x4.CreateScale(Rot24.ApplyHalf(rot, Half) * 2) * Matrix4x4.CreateTranslation(centre)
        : Matrix4x4.CreateScale(MeshSize(Half * 2, Front)) * FrontTurns[Front] * Matrix4x4.CreateFromQuaternion(Rot24.Quat(rot)) * Matrix4x4.CreateTranslation(centre);

    /// <summary>
    /// A leg's model between two points of the frame the part sits in: its part-Y across the span from <paramref name="from"/> to
    /// <paramref name="to"/>, keeping the box's own width (a leg's section is square or round: which way it faces doesn't show).
    /// </summary>
    public Matrix4x4 Stretched(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        var len = dir.Length();
        var y = len > 1e-5f ? dir / len : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(y, MathF.Abs(y.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var z = Vector3.Cross(x, y);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        var size = new Vector3(Half.X * 2, MathF.Max(0.001f, len * (SpanTo - SpanFrom)), Half.Z * 2);
        var mid = Matrix4x4.CreateTranslation(from + dir * ((SpanFrom + SpanTo) * 0.5f));
        return Shape == 0
            ? Matrix4x4.CreateScale(size) * basis * mid
            : Matrix4x4.CreateScale(MeshSize(size, Front)) * FrontTurns[Front] * basis * mid;
    }

    /// <summary>A point of the shape's own mesh (a unit box) in the part's frame, turned by <see cref="Front"/>.</summary>
    public Vector3 ShapePoint(Vector3 mesh) => Center + Vector3.Transform(mesh * MeshSize(Half * 2, Front), FrontTurns[Front]);

    /// <summary>The size of a shape's own mesh that fills <paramref name="size"/> in the part's frame with its front that way.</summary>
    public static Vector3 MeshSize(Vector3 size, byte front) => ((front - 1) >> 1) switch
    {
        0 => new Vector3(size.Z, size.Y, size.X), // along x
        1 => new Vector3(size.X, size.Z, size.Y), // along y
        _ => size,
    };

    private static readonly Matrix4x4[] FrontTurns = MakeFrontTurns();

    /// <summary>A shape's axes in the part's frame, by <see cref="Front"/>: its Z runs back from the front, right-handed.</summary>
    private static Matrix4x4[] MakeFrontTurns()
    {
        var turns = new Matrix4x4[7];
        turns[0] = Matrix4x4.Identity;
        Vector3[] dirs = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (var d = 0; d < dirs.Length; d++)
        {
            var z = -dirs[d];
            var y = d >> 1 == 1 ? default : Vector3.UnitY;
            var x = d >> 1 == 1 ? Vector3.UnitX : Vector3.Cross(y, z);
            if (d >> 1 == 1)
                y = Vector3.Cross(z, x);
            turns[d + 1] = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        }
        return turns;
    }
}

/// <summary>
/// How a suspension's box moves with its wheel: <see cref="Hub"/> goes with the wheel (a knuckle, a strut's rod), <see cref="Arm"/>
/// turns about its pivot by the arm's angle, <see cref="Leg"/> is drawn stretched between a fixed point and one that moves (a damper).
/// </summary>
public enum BoxRig : byte { None, Hub, Arm, Leg, Lever, Knob, Slide, Flip, Press, Stick } // (Lever: an Analog Lever's handle, tilted about the part's x through its "pivot" by the part's value; Stick: a Joystick's, tilted both ways by its X and Y: AnalogLever.cs)

/// <summary>An attachment point: where on the part (on a face of its bounds) and which way it faces (an axis, 0..5: +X -X +Y -Y +Z -Z).</summary>
/// <param name="Class">Its size class (<see cref="ConstructParts.Small"/> and up): a socket snaps only to one of its own class; docs/sandbox/CONSTRUCTS.md, "Size classes".</param>
public readonly record struct CSocket(string Name, Vector3 At, byte Dir, SocketKind Kind, byte Class = 0);

/// <summary>One part type, from a JSON file in Constructs/Parts (loaded by <see cref="ConstructParts"/>).</summary>
public sealed class ConstructPartDef
{
    /// <summary>A copy sharing every array (the material painter's parts: PartMaterials.cs replaces what differs).</summary>
    internal ConstructPartDef ShallowCopy() => (ConstructPartDef)MemberwiseClone();

    public ushort Id; // index in ConstructParts.All (the wire id: two bytes since the routed links, 2026-10-30)
    public string Name = "";
    public string File = "";
    public string Folder = ""; // the subfolder of Constructs/Parts it is in ("" for the top; "Structure/Beams"...)
    public string Description = "";
    public string Group = "", Section = ""; // clues for the inventory's rules ("group", "section"; Constructs/Categories.json, PartCategories.cs): its category comes from there
    public PartRole Role;
    public ConstructTool Tool;
    public float AttachSeconds;
    public float Mass;
    public float Health;
    public float Armour; // flat damage taken off every hit
    public string Material = "steel";
    public byte SurfaceOverride; // "finish" in the file: the shared material of all its boxes (Surfaces.cs; else the rules in Materials/Surfaces.json)
    public bool PhysicsOnly; // decision 21: never an item (no hands, backpack or storage slot): welder-fitted, heavier than ConstructParts.HandCarryLimit, or "physicsOnly": true in its file
    public string PhysicsWhy = ""; // why ("structural", "heavy: 120 kg", "bulky"; empty for an item)
    public bool Core; // a construct core ("core": true): the starter block of a new construct (never a loose part; the root after seats and foundations)
    public float Friction = 0.7f;
    public PartBreak OnBreak;
    public PropBreakable? Explosive; // "explosive": a bomb: when it breaks it goes off like an explosive prop (fuse, blast, knockback, crater; ConstructWorld.Arm)
    public int Yield; // a material bundle: the units the Recycler gives back (its Setting is the material id); 0: by mass and class (RecyclingTable)
    public CPartBox[] Boxes = []; // solid boxes first, then detail boxes
    public int SolidBoxes; // how many of Boxes are solid
    internal ConvexHull[]? Hulls; // its convex colliders, made on first use (ConstructHulls)
    internal bool[]? HullCovers; // per solid box: a hull stands for it
    public bool ColliderBoxes; // "collider": "boxes": its hidden solid boxes collide whatever its shapes (ConstructHulls)
    public bool HasGlass; // a box of it is a see-through glass pane ("glass": true): it shatters into small debris when it breaks
    public CSocket[] Sockets = [];
    public byte Class; // its size class ("sizeClass"; ConstructParts.Small and up): what its sockets are unless one says otherwise
    public bool CentreOptOut; // "centreSockets": false: no centre sockets are added to it (WithCentreSockets)
    public Vector3 BoundsMin, BoundsMax; // round every box, in the part's frame

    // Role data (only what the role uses is set)
    public float EngineForce, TopSpeed; // engine: drive force (N) shared by the wheels, top speed (m/s)
    public float WheelRadius, Grip = 1f; // wheel
    public float Travel, Frequency, Damping; // suspension: travel (m), spring frequency (Hz), damping ratio
    public float FuelCapacity; // fuel tank (litres)
    public float TankFlow; // a Fluid Tank: litres a second through its pipes at most
    public Fluid TankFluid; // what the tank holds (hoses and world links only move it between tanks that hold the same)
    public bool Stage; // a damage stage of another part ("stage": true): what a wall turns into as it loses chunks; not offered in the parts menu or on a rack
    public bool Boarding; // a preferred boarding point ("boarding": true): Use near it grabs on and climbs aboard (ConstructRide.GrabOn)
    public WalkKind Walk; // building pieces: walked on ("walk": floor, stairs, ramp, ladder): players step up onto it, bots may path over it
    public float DamagedBelow; // below this share of its health it turns into DamagedTo (a wall losing a chunk; "damaged": {"below", "part"})
    public string DamagedName = ""; // the stage it turns into (resolved after loading)
    public int DamagedTo = -1; // ...its id, or -1
    public bool Offered => !Legacy && !Stage && !ToolOnly; // listed in the parts menu and on racks
    public bool Generated; // made at load from a weapon builder part (ConstructWeaponParts.cs), not from a file here
    public bool CreativeOnly; // offered only with infinite parts (Constructs/WeaponParts.json "creativeOnly": the Zombies-economy parts)
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
    public float FlexTwist; // flex joint: how far it twists about its axis each way (radians; 0: it doesn't, and takes one joint less)
    public float FlexStiffness; // flex joint: N m a radian of bend or twist
    public float FlexDamping; // flex joint: damping ratio (1: comes back without overshooting, whatever it carries)
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
    public float StarterKJ = ConstructPower.StarterKJ; // engine: what its starter takes from the batteries
    public bool PathArc; // suspension: its wheel swings on an arm ("path": arc) instead of sliding straight (docs/sandbox/CONSTRUCTS.md "Suspension types")
    public Vector3 PathPivot; // the arm's hinge in the part's frame
    public byte PathAxis; // the direction the hinge runs
    public float PathLength; // the arc's radius; 0: the hinge's distance from the wheel (a parallel-arm wishbone's is shorter)
    public float PathTilt; // how much of the arm's angle the wheel leans by (1: it is fixed to the arm; 0: it stays upright)
    public float SuspRide = -1; // suspension: a gas strut holds this height (0 low, 1 high) whatever it carries, up to its maxLoad; -1: a plain spring
    public float Slew; // suspension: how fast (m/s) its contact follows the ground (0: at once); a big track rides over a kerb instead of being hit by it
    public float MotorTorque, MotorPower, MotorTop; // suspension: a hub motor in the mount (N m at the wheel, kW, m/s it fades out at): drives its wheel from the circuit, no shaft (ConstructThrust.cs)
    public float PlantRate, PlantBurn, PlantSpool, PlantBuffer; // generator: kW, litres a second at full output, seconds to come up, kJ it holds
    public EngineFuel PlantFuel; // generator: Fuel, or None (a reactor)
    public float ThrustForce, ThrustBurn, ThrustPower, ThrustSpool; // thruster: N, litres a second and kW at full thrust, seconds from nothing to full
    public EngineFuel ThrustFuel; // thruster: Fuel or Electric
    public ThrustKind ThrustKind; // thruster: what makes its push (ConstructBoost.cs)
    public float AbForce, AbBurn, AbBand; // thruster: N and l/s its afterburner adds at full, and the share of throttle where it can light (0: none)
    public float ThrustOx; // rocket: l/s of oxidiser at full thrust
    public float BurnTime; // solid booster: seconds of grain
    public float ThrustGimbal, GimbalRate; // nozzle: radians either way, radians a second
    public float RepulsorRange; // repulsor: metres it reaches
    public byte ThrustDir; // thruster: the way its exhaust leaves (a direction index): the push is the other way
    public Vector3 ThrustAt; // thruster: where the push acts, and the exhaust starts (part frame)
    public FlameDef[] Flames = []; // "flame" (one or a list): flames drawn by data (ConstructRenderFlames.cs); a thruster or burner that gives none gets one of its own
    public Vector3 SmokeAt; // "smoke": the top of the stack (part frame)
    public float SmokeSize; // "smoke": the puffs' size (m); 0: no smoke
    public HitchKind Hitch; // hitch
    public Vector3 HitchAt; // hitch: the coupling point (part frame)
    public float HitchChain; // hitch: a chain this long (m; 0: a rigid ball joint)
    public HoseEnd HoseEnd; // hose: an outlet (reel) or an inlet (coupler)
    public HoseCarry HoseCarry; // hose: what it carries (outlet and inlet must match)
    public float HoseLength; // hose outlet: how far the hose pays out (m)
    public float HoseFlow; // hose outlet: litres a second (fluids) or kW (power) at most
    public Vector3 HoseAt; // hose: where the hose leaves the reel, or where the receiver plugs in (part frame)
    public bool HasDial; // a gauge's dial: a needle on a face, turned by its signal
    public Vector3 DialAt; // the needle's hub (part frame)
    public byte DialDir; // the face it is on (out of the part)
    public float DialMax = 1f; // the signal at full scale (270 degrees)
    public FaceHint Faces; // smart rotation (SmartRotation.cs): which way its face should point ("faces"; else its role's default)
    public byte FaceAxis = 5; // ...and which of its own directions is that face ("faceAxis"; -z, its front, by default)

    // Drivetrain families (docs/sandbox/CONSTRUCTS.md, "Drivetrain parts"; ConstructDrive.cs)
    public float EngineTorque; // engine: peak torque at the crank, N m
    public bool EngineModular; // a modular engine's flywheel ("engine": {"modular": size}): its numbers come from the pieces on its crank (ConstructEngine.cs)
    public EnginePiece Piece; // a modular engine's piece kind (the flywheel: Output)
    public byte PieceSize; // Small 0, Medium 1, Large 2, Capital 3
    public float PiecePower = 1f, PieceThirst = 1f, PieceSpool; // a fuel system's or booster's multipliers, a turbo's lag
    public int SmokeSocket = -1; // the socket smoke leaves by (a stack's top, an exhaust's top, a head)
    public bool HasEngineBoxes; // boxes that pump or spin (the renderer skips the rest)
    public bool HasRig; // boxes that move with the wheel (a suspension's arm, strut or damper; the renderer skips the rest)
    public int[] UseBoxes = []; // the boxes the aim glow covers ("use" in the file, an Analog Lever's handle); none: the part's use region, or all of it
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
    public NamedPort[] NamedPorts = []; // the link ports the part file names (ConstructPorts.cs): shafts, pipes, power
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
    public bool Electric => Role == PartRole.Logic && Logic != CLogic.Cargo && Logic != CLogic.FlightStabilizer && Logic != CLogic.Decoupler && Logic != CLogic.BombRack && !IsSeatModule || Role == PartRole.Joint && JointType is not (JointType.Steering or JointType.Flex or JointType.Slider or JointType.Swivel) && RailDrive != RailDriveKind.Shaft || PowerDraw > 0 || FuelKind == EngineFuel.Electric && Role == PartRole.Engine || MotorTorque > 0 || Role == PartRole.Thruster && ThrustFuel == EngineFuel.Electric || Rotor is { MotorKw: > 0 }; // (steering is mechanical)
    // Weapons (milestone 3; ConstructWeapons.cs)
    public float GunThrust, GunBurn, GunTurn, GunProx, GunCrater; // a heavy gun's shell (ConstructOrdnance.cs): its push (m/s2), seconds it burns, turn (degrees a second), proximity fuse (m), crater (m)
    public int GunShell = -1; // a heavy gun's ShellKind ("shell"); -1: its rounds are rounds
    public SightKind Sight; // "sight": bolted to a gun, what it adds (ConstructOrdnance.cs)
    public float SightReach, SightCone; // how far it sees (m), and how far off its axis or the look a target can be (degrees)
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
    public string BulkMaterial = ""; // a bin: the bulk material it holds ("any", or one of RecyclingTable.Materials)
    public int BulkCapacity; // a bin: how much of it
    public Vector3 FillMin, FillMax; // a bin: the box its contents fill from the bottom up (part frame)
    public bool IsBin => BulkCapacity > 0;
    public ScoopDef? Scoop; // a scoop or bucket (storage.scoop; ConstructScoops.cs): a bin with a cutting edge and a mouth
    public bool IsScoop => Scoop != null;
    public bool IsTankVolume; // a Tank Volume: a Fluid Tank whose capacity comes from a player-sized box (ConstructFluid.cs)
    public bool IsSizedBox => IsVolumeStore || IsTankVolume; // a box of interior space that holds something
    public float GasLitres; // a Gas Compressor: litres a unit of its bin's gas gives down its pipes (ConstructLift.cs)
    public EnvelopeDef? Envelope; // a lift cell: a Tank Volume whose box is a solid envelope (ConstructLift.cs)
    public bool IsEnvelope => Envelope != null;
    public float BurnerHeat, BurnerRate, FlameReach = 12f; // a burner: the cubic metres of cell it keeps at full heat, its litres a second, how far its flame reaches
    public Vector3 FlameAt; // ...and where the flame leaves (it goes up the part)
    public bool AmmoStore; // an Ammo Container: a Storage Volume whose bulk "ammo" feeds guns (ConstructAmmo.cs)
    public bool IsVolumeStore; // a Storage Volume: a bin with a player-sized box (its capacity comes from the box; ConstructStoreVolume.cs)
    public bool IsRecycler; // a bin that processes wrecked parts dropped into its intake box (ConstructRecycler.cs; sh01)
    public Vector3 IntakeMin, IntakeMax; // a recycler: the intake box (part frame)
    public bool IsRefiner; // a production machine (ConstructRefining.cs): its bulk bin is the output, and it has an input bin of its own
    public string RefineMachine = ""; // a production machine: its name in Constructs/Refining.json
    public int InCapacity; // a production machine: how much its input holds (a mixed input: of each material; an Assembler: its fill's scale)
    public bool RefineMix; // a production machine whose recipes take several materials (a Foundry): its input is CStore's mixed input
    public bool IsAssembler; // a machine that builds parts from a chosen recipe (ConstructAssembling.cs): its slots are its output tray, its input is mixed
    public byte AsmMachine; // an Assembler\'s family: which recipes it builds (AssemblingTable.Machines)
    public int AsmBatches = 4; // an Assembler: how many batches of each input it buffers
    public Vector3 AsmOutAt = Vector3.Zero, AsmOutDir = Vector3.UnitZ; // an Assembler: where a finished body leaves, and which way (part frame)
    public int RefineWork = ConstructRefining.BaseWork; // a production machine: thousandths of a tick of work a 10 Hz step does ("speed" in storage.refine)
    public Vector3 InFillMin, InFillMax; // a production machine: the box its input fills from the bottom up (part frame)
    public bool IsExtruder; // an Extruder (ConstructExtruder.cs): its bulk bin feeds what grows out of its die
    public float ExtSpeed; // an Extruder: metres a second ("speed" in storage.extrude)
    public Vector2 ExtDie, ExtMaxSection; // ...its own die and the most Die Frames can make of it (metres across, up the face)
    public Vector3 ExtDieAt; // ...the die's centre on its output face (part frame; the piece grows along +z from here)
    public float ExtMaxLength, ExtPush; // ...the longest piece (m), and how hard it pushes what is in the way (N)
    public int ExtDieSocket = -1; // ...its socket of kind "die"
    public bool IsRequisition; // a Requisition Crate ("requisition": true): a story faction's courier collects a Supply job from its box (Game/GameStoryJobs.cs)
    public bool IsDieFrame; // a Die Frame ("dieFrame": true): the die of an Extruder it is joined to reaches across it
    public bool IsFanFormer; // a Fan Former (also IsWheelFormer: ConstructExtruder.cs): its piece is a whole rotor
    public bool IsAirframeFormer; // an Airframe Former (also IsWheelFormer: ConstructExtruder.cs): its piece is a wing or a control surface, to the size it is set to
    public bool IsWheelFormer; // a Wheel Former (also IsExtruder; ConstructExtruder.cs): its die is a hub and its piece a wheel
    public Vector2 ExtWheelMax, ExtWheelStart; // ...the biggest and the first wheel (metres wide, across)
    public bool IsExtrusion; // a piece an Extruder made (ConstructExtrusion.cs): not from a file
    public ExtrusionKey ExKey; // ...what it is
    public bool IsOrdnance; // a modular bomb or missile off an Ordnance Bench (OrdnanceDesign.cs): its Name is its key, its id this machine's own
    public bool RuntimeMade => IsExtrusion || IsOrdnance || IsRackRuntime || IsNamedRuntime || MatBase != null; // (never on the wire or in a hash by id)
    public ConstructPartDef? MatBase; // a part the material painter made of another material (PartMaterials.cs): the part file's own def. Its Name is its key, its id this machine's own
    public string MatName = ""; // ...the bulk material it is made of (a name in Recycling.json; its Material is that row's salvage class)
    public OrdSectionDef? Ord; // "ordnance": a section of a modular bomb or missile
    public float RackLoad; // a Bomb Rack's rated load, kg (ConstructOrdnance.cs)
    public bool RackRail; // ...a launch rail: what it lets go leaves forward along it, a missile under its own thrust (OrdnanceFlight.cs)
    public RackShape Rack; // an Ordnance Rack's shape (ModularRack.cs; "logic.modular"): Stations is 0 for every other part
    public float RackStation; // ...its rated load a station, kg (RackLoad is the whole rack's)
    public bool IsRackRuntime; // ...made at run time for a shape no part file has: its Name is its key, its id this machine's own
    public bool IsNamedRuntime; // a part made at run time from its name alone (HullLoft.cs, a vehicle kit's lofted hull bay): its Name is its key on the wire and in saves, its id this machine's own
    public bool IsModularRack => Rack.Stations > 0;
    public OrdnanceDesign? OrdDesign; // a designed weapon's design and numbers (IsOrdnance)
    public OrdStats? OrdStats;
    public bool IsOrdnanceBench; // a storage part that is also an Ordnance Bench: F opens it (GameSandboxOrdnance.cs)
    public uint ExHash; // ...its key hashed: the same on every machine (ids aren't)
    public float ExArea; // ...its section's area (m2)
    public bool IsGatherer, GatherSaw; // a gathering module (a saw, else a drill): ConstructGatherer.cs
    public float GatherRate; // units a second
    public float GatherRpm; // a shaft-driven module: the shaft speed of its full rate (storage.gather.ratedRpm; 0: no shaft drive): ConstructDrillDrive.cs
    public float GatherTorque; // ...and the torque it takes off the shaft while it works, N m (storage.gather.shaftTorque)
    public int DriveInSocket = -1; // ...and the socket its shaft input stands on
    public int Tier = 1; // a machine's tier, 1 to MaxTier ("tier"): a deposit or a recipe may ask for a minimum (docs/sandbox/CONSTRUCTS.md "Machine tiers")
    public int ChuteLoad = ConstructRoutes.ChuteLoad; // bulk a chute moves at a time from or to this bin ("chuteLoad" in storage.bulk)
    public Vector3 WorkMin, WorkMax; // a gathering module: its work box (part frame)
    public bool IsTerminal; // a Smart Terminal: lists its chute network to whoever stands at it and pulls a chosen item to its tray (storage.terminal; ConstructTerminal.cs)
    public bool IsBeacon; // a storage part with no room of its own that broadcasts its chute network (storage.beacon; ConstructBeacons.cs)
    public string BeaconKind = ""; // ...its id in Constructs/Beacons.json
    public BuildAid Aid; // ...a build station: the aid it gives (BuildAids.cs; None: an Inventory Beacon)
    public bool IsGunBench; // a storage part that is also the Gun Workbench: F opens the weapon builder (GameSandboxBench.cs)
    public float StoreMaxMass; // the heaviest part (kg) it takes (0: any; a backpack's limit)
    public int BoxRounds; // an ammo box: rounds it comes with
    public float CookRadius, CookDamage; // an ammo box: its cook-off at full

    // Large systems (ConstructSystems.cs; docs/sandbox/CONSTRUCTS.md, "Large systems")
    public float DockSide, DockSettle = ConstructCargo.SettleSpeed, DockMaxMass = float.MaxValue, DockCharge, DockFuel; // a docking clamp or magnetic lock ("dock"): its longest box side, the speed it catches under, the heaviest body, kW and litres a second into what it holds
    public bool DockChute; // ...and its bins unload into the bins of the construct it is docked to
    public bool IsDock => DockSide > 0;
    public float DockPull, DockEject; // a dock's tractor reach past its box (m), and how fast it throws what it lets go along its front (m/s): ConstructCarrier.cs
    public float MastReach = 12; // a mooring mast: how far from its top it ties (m)
    public byte MastRope = 2; // ...and the rope's size
    public bool SeatEject; // an ejection seat ("seat"."eject"): a key or signal throws its rider clear on a parachute (PilotGear.cs)
    public byte Wear; // worn from the backpack ("wear"): PilotGear's bit
    public float LaunchSpeed, LaunchReach; // a launch rail's speed off it over the deck (0: its accel runs to the end of the rail), and how far past its ends an arrestor catches
    public float LaunchWidth, LaunchHeight, LaunchAccel, LaunchMaxMass = float.MaxValue; // a catapult or arrestor ("launch"): its zone over its top, m/s2, the heaviest body it takes
    public float JumpRange, JumpCharge, JumpEnergy; // a jump drive ("jump"): its reach (light-years), seconds to charge, kJ a tonne of ship a light-year
    public bool IsJumpDrive => Logic == CLogic.JumpDrive;
    public float BeltSpeed; // a conveyor ("conveyor"): m/s at a full signal
    public Vector3 BeltDir; // ...and which way (part frame)
    public float WinchMin, WinchSpeed, WinchRating; // a winch ("hitch"."winch"): its shortest chain (m), reeling speed (m/s), the heaviest body it lifts (N)
    public bool IsWinch => WinchSpeed > 0;
    public bool IsCargoWinch; // a winch a pilot works from a seat: a signal into grab takes the nearest eye in reach, or lets go ("winch"."grab": ConstructLinks.cs)
    public bool Helm; // a helm seat ("seat"."helm"): the construct's driver sits here before any other seat
    public bool Bunk; // a bunk ("seat"."bunk"): lying in it makes it the sitter's checkpoint in survival (GameSandboxDeath.cs)

    public bool IsReceiverGun => Role == PartRole.Gun && GunReceiver.Length > 0;

    /// <summary>Catches fire from incendiary rounds (wood, rubber, fuel tanks, ammo boxes).</summary>
    public bool Flammable => Material is "wood" or "rubber" or "cloth" && Envelope == null || Role is PartRole.FuelTank or PartRole.AmmoBox || Burns; // (a lift cell burns by its gas: ConstructWorld.CanBurn)

    /// <summary>Burns whatever its material: a designed weapon with a motor's propellant or a tank's fuel in it (OrdnanceDesign.Build).</summary>
    public bool Burns;

    /// <summary>
    /// A warhead: what makes an explosive part go off when it is broken (shot, blasted, burnt: ConstructWorld.Arm) rather than only break. A designed
    /// weapon's charge with filling or a sub-payload (not a Kinetic Penetrator alone, a motor, a tube or a spent stage); a part file's explosive with a blast.
    /// </summary>
    public bool Warhead;

    public bool UsesTool(ConstructTool t) => t != ConstructTool.None && t == Tool;

    /// <summary>A joint part: a passive hinge or a moving joint (what goes on its leaf hangs on a new joint).</summary>
    public bool HasJoint => Role is PartRole.Hinge or PartRole.Joint;

    /// <summary>How many of a construct's joints this part's joint takes: a flexible joint's ball is two swings and maybe a twist (ConstructFlex.cs), the rest one.</summary>
    public int JointSlots => JointType != JointType.Flex ? 1 : FlexTwist > 0 ? 3 : 2;

    // A linear rail (ConstructLinear.cs): "rail" in its joint block; "end-" and "end+" are the sockets a stack joins by.
    public RailDriveKind RailDrive;
    public int RailGauge; // sections stack when their gauge and size class match
    public float RailLead; // a shaft drive: metres a turn of the shaft (the setting's default)
    public int RailEnd0 = -1, RailEnd1 = -1; // its two end sockets
    public bool IsRail => RailDrive != RailDriveKind.None;
    public HydKind Hyd; // what it is in a hydraulic circuit (ConstructHydraulics.cs)
    public float HydBar, HydFlow, HydRpm, HydArea, HydSpeed, HydForce; // a pump's pressure, litres a second, rated rpm; a head's bore (cm2), top speed; an electric head's force
    public int TrayLanes; // a Cable Tray or Clamp: its lanes (ConstructTrays.cs) ...
    public float TrayPitch, TrayLength, TrayGuard; // ... how far apart, how long, and the length of run it guards
    public Vector3 TrayAt, TrayAxis, TrayAcross; // ... in the part's own frame
    public bool ToolOnly; // "toolOnly": never in the inventory or the Assemblers: a tool makes it (a cylinder's head)
    public SurfaceDef? Surface; // a lifting surface (ConstructAero.cs)
    public RotorDef? Rotor; // a propeller or a rotor (ConstructRotor.cs)
    public bool ShaftIn => GatherRpm > 0 || RailDrive == RailDriveKind.Shaft || Hyd == HydKind.Pump || Rotor is { Shaft: true }; // a part with a "Drive in" shaft port: ConstructDrillDrive.cs

    /// <summary>Has a signal output (wires start here): seats (someone sits there) and every logic part but lamps, horns and brakes.</summary>
    public PanelDef[] Panels = []; // flat display surfaces (DisplayPanel.cs)
    public bool RemoteReceiver, IsCamera; // ConstructRemote.cs
    public bool IsMedia; // a Video Screen or Music Player (ConstructMedia.cs)
    public bool MediaPipes; // a Pipe Box: it plays its list while its construct is driven (ConstructPipes.cs)
    public bool IsWeb; // a Browser Screen: IsMedia, and its one line is a web address (ConstructMedia.cs)
    public bool IsSpeaker; // a speaker (ConstructSpeakers.cs)
    public float SpeakerNear; // metres it holds full level
    public float MediaHear, MediaNear; // metres: where its sound is gone, and how far it stays at full level
    public int MediaWidth, MediaHeight; // the page its browser shows, pixels
    public OfficeKind Office; // a Fax Machine or a Computer: the IT Department's lines (OfficeParts.cs)
    public bool CameraScreen => Array.Exists(Panels, x => x.Source == "camera");
    public float RemoteRange, CameraFov;
    public bool ShellBody; // a Robot Shell: a body a player wakes in from a Shell Control Chair (ConstructShell.cs)
    public float ShellRange; // a Shell Control Chair: how far its shells may be, metres (0: not a chair)
    public Vector3 CameraAt;
    public bool IsPress, IsLathe, IsPressDie; // ConstructPress.cs
    public PressBayDef[] PressBays = [];
    public int PressDieSocket = -1, PressMaxCm;
    public Vector3 PressDieMin, PressDieMax;
    public string DieFamily = ""; // a die: its rows in Pressing.json
    public (int First, int Count) DieRows;

    public bool HasOutput => Role == PartRole.Seat || IsExtruder || IsMedia || (Role == PartRole.Logic && Logic is not (CLogic.Lamp or CLogic.Horn or CLogic.Brake or CLogic.GravityWell or CLogic.GravityGen or CLogic.Display or CLogic.Instrument or CLogic.RadioTx))
                             || Role == PartRole.Hose && HoseCarry == HoseCarry.Data && HoseEnd == HoseEnd.Inlet; // (a data lead's inlet puts out what the lead carries)

    /// <summary>
    /// Named data ports (the spawners; GameSandboxSpawns.cs): the inputs a data link can end at and the outputs it can start
    /// at. Port 0 is the part itself (every other part has just that one each way); a link names one by
    /// <see cref="ConstructRoutes.DataPort"/>. Empty: the one port.
    /// </summary>
    public string[] DataIn = [], DataOut = [];
    public bool Stick; // an Analog Lever with "axes": 2, a Joystick: X and Y (AnalogLever.cs)
    public float DecoupleKick, AntiShare, AntiRadius; // a Decoupler: the m/s it parts its two sides at; Anti-Grav: the share of weight it cancels, and its radius (ConstructBoost.cs)
    public float Gyro; // a Flight Stabilizer: the N m its gyro turns the craft about its up with (ConstructFlight.cs)

    /// <summary>A spawner part (the Spawn category).</summary>
    public bool IsSpawner => Logic is CLogic.BotSpawner or CLogic.ZombieSpawner;

    /// <summary>A control module: it breaks out the controls of the seat linked into it (ConstructModules.cs).</summary>
    public bool IsSeatModule => Logic is CLogic.DriveModule or CLogic.TurretModule; // (a Remote Transmitter reads a seat too: ConstructModules.LinkWhy)
    public bool Watches => Logic is CLogic.LevelSensor or CLogic.DamageSensor; // has a watch link (ConstructRoutes.WatchPort)
    public bool HasReading => Logic is >= CLogic.Xor and <= CLogic.BusOut and not (CLogic.KeySwitch or CLogic.Instrument or CLogic.RadioTx or CLogic.BusIn); // its number reaches clients exactly (CPart.Reading; LogicDisplays.cs)
    public bool WidePorts => Logic is CLogic.Mux or CLogic.BusIn or CLogic.BusOut or CLogic.FlightStabilizer || Logic == CLogic.Display && DataIn.Length > 1; // more data ports than the logic step's sums (Construct.PortIn)
    public int TextSlots; // messages it holds ("logic.text": CPart.Text and Text2; LogicText.cs)

    /// <summary>How many wires may come into it (0: it takes no input).</summary>
    public int MaxInputs => IsSpeaker || Surface is { Control: > 0 } ? 1 : Role switch // (a speaker's one cable comes from what it repeats; a control surface's angle)
    {
        PartRole.Joint or PartRole.Hinge or PartRole.Engine or PartRole.Gun => 4,
        PartRole.AmmoSwitch => 1, // which feed goes on
        PartRole.Hitch => IsCargoWinch ? 3 : IsWinch ? 1 : 0, // a winch: 1 reels in, -1 pays out
        PartRole.Transmission => 2, // milestone 3: a gear-select signal (1, 2, 3...), and a Driving Module's drive link
        PartRole.Transfer => Drive is DriveKind.Clutch or DriveKind.TransferCase ? 1 : 0, // a clutch's engage, a transfer case's 4WD
        PartRole.Differential => Diff is DiffKind.Lockable or DiffKind.Steering ? 1 : 0, // locks it; steers it (-1 to 1: 1 is right)
        PartRole.Suspension => MotorTorque > 0 ? 2 : ActiveSuspension ? 1 : 0, // its ride height; a hub motor's throttle and a Driving Module's drive link
        PartRole.Generator => 1, // run: on or off (unwired: it follows the demand)
        PartRole.Seat => SeatEject ? 1 : 0, // an ejection seat's signal
        PartRole.Thruster => 4, // throttle, hold height
        PartRole.Rotor => 4, // throttle or collective, cyclic pitch and roll
        PartRole.FluidTank => IsEnvelope ? 1 : 0, // a lift cell's vent
        PartRole.Burner => 2, // throttle, hold height
        PartRole.Hose => HoseCarry == HoseCarry.Data && HoseEnd == HoseEnd.Outlet ? 1 : 0, // a data lead's outlet: the signal it sends down the lead
        PartRole.Storage => IsExtruder ? DataIn.Length : IsGatherer || IsRecycler || IsRefiner || IsAssembler || IsPress || IsBeacon || IsTerminal ? 1 : 0, // (an Extruder: Run and Cut, a Wheel Former and Mode) a module's, Recycler's, production machine's or beacon's Run: on/off (ConstructGatherer.HeldOff)
        PartRole.Logic => Logic switch
        {
            CLogic.Button or CLogic.Lever or CLogic.AnalogLever or CLogic.Switch or CLogic.Plate or CLogic.Speed or CLogic.Proximity or CLogic.FuelGauge or CLogic.ChargeGauge => 0,
            CLogic.Numpad or CLogic.CodeLock or CLogic.KeySwitch or CLogic.Instrument or CLogic.AltitudeSensor or CLogic.TiltSensor or CLogic.HeadingSensor or CLogic.LevelSensor or CLogic.DamageSensor or CLogic.RadioRx => 0,
            CLogic.Filter or CLogic.RandomPulse or CLogic.OccupancySensor or CLogic.RadioTx or CLogic.BusOut => 1,
            CLogic.Mux or CLogic.BusIn or CLogic.Display or CLogic.FlightStabilizer => 16,
            CLogic.Decoupler or CLogic.AntiGrav => 1,
            CLogic.Reader or CLogic.Timer or CLogic.Delay or CLogic.Selector or CLogic.Director or CLogic.DriveModule or CLogic.TurretModule => 1,
            CLogic.BotSpawner or CLogic.ZombieSpawner => 6, // (over its three inputs)
            _ => 4,
        },
        _ => 0,
    };

    /// <summary>Is a signal part (its value travels to clients): an output, or a lamp, horn or brake.</summary>
    public bool ThrustHolds => ThrustGimbal <= 0 && ThrustKind is not (ThrustKind.Solid or ThrustKind.Repulsor); // (its second input is a height to hold)
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
    public const int MaxBoxes = 256; // a part's boxes (was 32 while each was a draw call: static parts are merged meshes now, docs/render/MERGED-MESHES.md; a box index fits a byte)
    public const int MaxTier = 5; // a machine's highest tier
    public const int MaxSockets = 255; // a part's sockets: indices 0-254 (one byte on the wire, CLink.SA/SB; 255 means "no socket" in places such as ConstructModules.LinkWhy)
    public const float MaxPartReach = 200f; // how far a box goes from a part's origin (a capital hull section, a 128 m keel from its end cell)
    public const float MaxPartMass = 50_000_000f, MaxPartHealth = 16_000_000f; // (kg, hp, which three bytes carry; a capital chassis deck)
    public const float MaxAttachSeconds = 60f, MaxPistonTravel = 64f, MaxHitchReach = 64f;
    public const int MaxName = 32; // a part file's name (a sanity check: the inventory's tiles and lists show it whole)
    public const int MaxRuntimeName = 128; // a part made at run time ("stage": the Ordnance Rack's shapes): its name is its key in saves and blueprints, never a fixed-width field (a save's names are length-prefixed, a packet's text is at most 255 bytes)

    // The size classes (docs/sandbox/CONSTRUCTS.md, "Size classes"; tools/constructs/scale.py): wire and file values of a socket's class.
    public const byte Small = 0, Medium = 1, Large = 2, Capital = 3;
    public static readonly string[] ClassNames = ["small", "medium", "large", "capital"];
    public static readonly float[] ClassCell = [0.25f, 1f, 4f, 16f]; // each class's lattice step (m)

    public const ushort None = ushort.MaxValue;

    /// <summary>
    /// Decision 21: the heaviest part that is still an item (kg; the backpack's limit). Heavier parts, welder-fitted ones
    /// and those whose file says <c>"physicsOnly": true</c> exist only as bodies in the world (<see cref="ConstructPartDef.PhysicsOnly"/>).
    /// </summary>
    public const float HandCarryLimit = 40f;

    /// <summary>How much of a <c>"glass": true</c> box is drawn (its opacity; the props' glass is 0.4, Props/README.md).</summary>
    public const float DefaultGlass = 0.3f;

    private static readonly List<string> _refused = new();
    private static readonly Lazy<ConstructPartDef[]> _all = new(() => WithSurfaces(PressTable.Attach(AssemblingTable.Attach(WithWeaponParts(LoadFolder(Folder, _refused, Mods.Active), _refused)))), LazyThreadSafetyMode.ExecutionAndPublication); // (maps load in parallel)

    public static string Folder => Path.Combine(AppFolders.Data, "Constructs", "Parts");

    /// <summary>Every box gets its shared material (Materials/Surfaces.json), from the part it is on: nothing in the part files has to say.</summary>
    /// <summary>
    /// A part's "flame": an object or a list of them, with "at" and "dir" (where it starts and the way it leaves, the part's frame), "radius"
    /// and "length" (m), "kind" (flame, jet, rocket), "source" (thrust, burner, engine, signal, always) and "colour" ("#rrggbb"). All but
    /// "at" and "dir" are optional. This is the whole of what a new burning part needs.
    /// </summary>
    private static void ReadFlames(JsonElement root, ConstructPartDef d, string file)
    {
        if (Opt(root, "flame") is not { } block)
            return;
        var list = new List<FlameDef>();
        foreach (var e in block.ValueKind == JsonValueKind.Array ? block.EnumerateArray().ToArray() : [block])
        {
            Known(e, ["at", "dir", "radius", "length", "kind", "source", "colour"], $"{file}, flame");
            var f = new FlameDef
            {
                At = Vec(Need(e, "at", $"{file}, flame"), file, "flame.at"),
                Dir = DirOf(Str(Need(e, "dir", $"{file}, flame"), file, "flame.dir"), file, "flame.dir"),
            };
            if (Opt(e, "radius") is { } r)
                f.Radius = Num(r, file, "flame.radius");
            if (Opt(e, "length") is { } l)
                f.Length = Num(l, file, "flame.length");
            Range(f.Radius, 0.01f, 64, file, "flame.radius");
            Range(f.Length, 0.05f, 400, file, "flame.length");
            if (Opt(e, "kind") is { } k)
                f.Kind = EnumOf<FxKind>(Str(k, file, "flame.kind"), file, "flame.kind");
            if (Opt(e, "source") is { } s)
                f.Source = EnumOf<FlameSource>(Str(s, file, "flame.source"), file, "flame.source");
            else
                f.Source = d.Role == PartRole.Burner ? FlameSource.Burner : FlameSource.Thrust;
            if (Opt(e, "colour") is { } c)
                f.Tint = Fx.Tint(Colour(c, file, "flame.colour"));
            list.Add(f);
        }
        d.Flames = list.ToArray();
    }

    /// <summary>A thruster or burner with no "flame" of its own: one from its numbers, as wide as its force asks for, or across its top.</summary>
    private static void DefaultFlame(ConstructPartDef d)
    {
        if (d.Flames.Length > 0)
            return;
        if (d.Role == PartRole.Thruster)
        {
            var r = MathF.Sqrt(d.ThrustForce) * 0.002f + 0.05f;
            d.Flames = [new FlameDef { At = d.ThrustAt, Dir = d.ThrustDir, Radius = r, Length = r * 6.5f, Kind = d.ThrustFuel == EngineFuel.Electric ? FxKind.Jet : FxKind.Rocket, Source = FlameSource.Thrust }];
        }
        else if (d.Role == PartRole.Burner)
        {
            var size = d.BoundsMax - d.BoundsMin;
            var mouth = ((d.BoundsMin + d.BoundsMax) * 0.5f) with { Y = d.BoundsMax.Y };
            var r = MathF.Max(0.08f, MathF.Min(size.X, size.Z) * 0.3f);
            d.Flames = [new FlameDef { At = mouth, Dir = 2, Radius = r, Length = MathF.Min(r * 7f, MathF.Max(1.2f, d.FlameReach * 0.3f)), Kind = FxKind.Flame, Source = FlameSource.Burner }];
        }
    }

    private static byte SurfaceByName(JsonElement e, string file, string where) => Surfaces.File.ByName.TryGetValue(Str(e, file, where + ".finish"), out var row)
        ? row
        : throw new ConstructPartException($"{file}: {where}.finish: no such material in Materials/Surfaces.json");

    private static ConstructPartDef[] WithSurfaces(ConstructPartDef[] all)
    {
        foreach (var d in all)
        {
            SurfaceRules.Apply(d);
            DefaultFlame(d);
        }
        return all;
    }

    /// <summary>The hand-made parts, then one generated def for every other weapon builder part (ConstructWeaponParts.cs).</summary>
    public static ConstructPartDef[] WithWeaponParts(ConstructPartDef[] files, List<string> refused)
    {
        var path = ConstructWeaponParts.FilePath;
        var rules = File.Exists(path) ? ConstructWeaponParts.Load(File.ReadAllText(path), "WeaponParts.json", refused) : new ConstructWeaponParts.Rules();
        var list = new List<ConstructPartDef>(files);
        ConstructWeaponParts.Append(list, refused, Parts.All, rules);
        return list.ToArray();
    }

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
        foreach (var p in ConstructLoot.Problems)
            Console.WriteLine($"REFUSED {p}");
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
        var zfights = 0;
        foreach (var d in All)
            foreach (var z in ZFight.Report(d.Name, ZFight.Boxes(d)))
            {
                Console.WriteLine($"PROBLEM z-fight: {z} ({d.File}; inset or raise one by 2-5 mm)");
                zfights++;
            }
        var portProblems = 0;
        foreach (var d in All)
        {
            foreach (var np in d.NamedPorts)
                Console.WriteLine($"OK  port {d.Name}: {np.Name} ({ConstructRoutes.TypeKey(np.Type)} {(np.In && np.Out ? "in/out" : np.In ? "in" : "out")}{(np.Fluid.Length > 0 ? ", " + np.Fluid : "")}{(np.NoEffect ? ", no effect yet" : "")}{(np.Implicit ? ", implicit" : "")})");
            foreach (var why in PortProblems(d))
            {
                Console.WriteLine($"PROBLEM ports: {d.Name}: {why} ({d.File})");
                portProblems++;
            }
        }
        foreach (var r in Refused)
            Console.WriteLine($"REFUSED {r}");
        if (ExtrusionTable.Current.Problem.Length > 0)
            Console.WriteLine($"REFUSED Extrusion.json: {ExtrusionTable.Current.Problem}");
        var badTable = (RefiningTable.Current.Problem.Length > 0 ? 1 : 0) + (AssemblingTable.Current.Problem.Length > 0 ? 1 : 0) + (ExtrusionTable.Current.Problem.Length > 0 ? 1 : 0);
        if (AssemblingTable.Current.Problem.Length > 0)
            Console.WriteLine($"REFUSED Assembling.json: {AssemblingTable.Current.Problem}");
        if (RefiningTable.Current.Problem.Length > 0)
            Console.WriteLine($"REFUSED Refining.json: {RefiningTable.Current.Problem}");
        if (BeaconTable.Current.Problem.Length > 0)
        {
            badTable++;
            Console.WriteLine($"REFUSED Beacons.json: {BeaconTable.Current.Problem}");
        }
        if (SurvivalStart.Current.Problem.Length > 0)
        {
            badTable++;
            Console.WriteLine($"REFUSED SurvivalStart.json: {SurvivalStart.Current.Problem}");
        }
        if (PartCategories.Current.Problem.Length > 0)
        {
            badTable++;
            Console.WriteLine($"REFUSED Categories.json: {PartCategories.Current.Problem}");
        }
        var catalogue = SandboxCatalogue.Make().Parts;
        foreach (var g in catalogue.Groups)
        {
            Console.WriteLine($"OK  category {g.Name} ({g.Items}): {string.Join(", ", g.Sections.Select(s => $"{catalogue.Sections[s].Name} {catalogue.Sections[s].Items.Length}"))}");
            if (detail)
                foreach (var s in g.Sections)
                    Console.WriteLine($"      {catalogue.Sections[s].Name}: {string.Join(", ", catalogue.Sections[s].Items.Select(i => All[i].Name))}");
            if (g.Name == "Other")
                foreach (var s in g.Sections)
                    foreach (var i in catalogue.Sections[s].Items)
                    {
                        Console.WriteLine($"PROBLEM category: {All[i].Name} isn't placed by Constructs/Categories.json ({All[i].File})");
                        badTable++;
                    }
        }
        foreach (var r in ConstructBuild.RefusedBlueprints)
            Console.WriteLine($"REFUSED {r}");
        foreach (var m in ConstructTools.All)
            Console.WriteLine($"OK  tool {m.Key,-9} {m.Name,-14} {m.Boxes.Length} boxes{(m.Support != null ? ", two hands" : "")}{(m.Tool == null ? " (still to come)" : "")} ({m.File})");
        foreach (var r in ConstructTools.Refused)
            Console.WriteLine($"REFUSED {r}");
        return Refused.Count + ConstructBuild.RefusedBlueprints.Count + ConstructTools.Refused.Count + zfights + portProblems + badTable;
    }

    /// <summary>
    /// What a part with a linking role is missing in its named ports (<c>--check-construct-parts</c>, the self-test): an
    /// engine needs a shaft output (and a fuel inlet when it burns fuel), a transmission a shaft input and output, a
    /// differential a shaft input, a transfer part a shaft port (the old drive-shaft parts excepted), a tank a pipe output,
    /// a battery or alternator a power output. Structural parts offer no shaft port at all.
    /// </summary>
    public static List<string> PortProblems(ConstructPartDef d)
    {
        var why = new List<string>();
        void Need(RouteType t, bool input, string what)
        {
            if (!ConstructRoutes.HasPort(d, t, input))
                why.Add($"a {d.Role} needs {what}");
        }
        switch (d.Role)
        {
            case PartRole.Engine:
                Need(RouteType.Shaft, false, "a shaft output port (\"Drive out\")");
                if (d.FuelKind == EngineFuel.Fuel)
                    Need(RouteType.Pipe, true, "a pipe input port (\"Fuel in\")");
                break;
            case PartRole.Transmission:
                Need(RouteType.Shaft, true, "a shaft input port");
                Need(RouteType.Shaft, false, "a shaft output port");
                break;
            case PartRole.Differential:
                Need(RouteType.Shaft, true, "a shaft input port");
                break;
            case PartRole.Transfer when !d.Legacy:
                Need(RouteType.Shaft, true, "a shaft input port");
                if (d.Drive is not (DriveKind.Hub or DriveKind.Stub))
                    Need(RouteType.Shaft, false, "a shaft output port");
                break;
            case PartRole.Thruster when d.ThrustFuel == EngineFuel.Fuel && d.ThrustKind != ThrustKind.Solid:
            case PartRole.Burner:
                Need(RouteType.Pipe, true, "a pipe input port (\"Fuel in\")");
                break;
            case PartRole.FuelTank:
                Need(RouteType.Pipe, false, "a pipe output port (\"Fuel out\")");
                break;
            case PartRole.FluidTank:
                Need(RouteType.Pipe, true, "a pipe input port (\"Fluid in\")");
                Need(RouteType.Pipe, false, "a pipe output port (\"Fluid out\")");
                break;
            case PartRole.Battery or PartRole.Alternator or PartRole.Generator:
                Need(RouteType.Power, false, "a power output port (\"Power out\")");
                if (d.Role == PartRole.Generator && d.PlantFuel == EngineFuel.Fuel)
                    Need(RouteType.Pipe, true, "a pipe input port (\"Fuel in\")");
                break;
        }
        if (d.Role is PartRole.Frame or PartRole.Panel && d.Hyd != HydKind.Pump && d.NamedPorts.Any(p => p.Type == RouteType.Shaft))
            why.Add("structural parts never offer shaft ports");
        if (d.Electric && !ConstructRoutes.HasPort(d, RouteType.Power, true))
            why.Add("a part that draws power needs a power input port");
        if (d.Role != PartRole.Engine && d.Role != PartRole.Differential && d.Role != PartRole.Transmission && d.Role != PartRole.Transfer && d.Role != PartRole.Suspension && !d.ShaftIn && d.NamedPorts.Any(p => p.Type == RouteType.Shaft))
            why.Add("only drive parts have shaft ports");
        return why;
    }

    public static ConstructPartDef? Get(ushort id) => id < All.Count ? All[id] : Extrusion.IsRuntimeId(id) ? Extrusion.Get(id) : null;

    /// <summary>The def behind a part's id: a part file's, or a piece an Extruder made (ids from <see cref="Extrusion.RuntimeBase"/>).</summary>
    public static ConstructPartDef Def(ushort id) => id >= Extrusion.RuntimeBase ? Extrusion.Get(id) ?? throw new InvalidOperationException($"no part {id}") : _all.Value[id];

    public static ConstructPartDef? Find(string name)
    {
        if (name.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase))
            return Ordnance.Parse(name); // (a design names itself, as a piece does)
        if (name.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase))
            return Extrusion.Parse(name); // (a piece names itself: a blueprint's extrusion)
        if (name.StartsWith(HullLoft.Prefix, StringComparison.Ordinal))
            return HullLoft.Parse(name); // (a lofted hull bay names itself too: HullLoft.cs)
        if (name.StartsWith(ModularRack.Prefix, StringComparison.OrdinalIgnoreCase) || name.StartsWith(ModularRack.LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            return ModularRack.Parse(name); // (a rack of a shape no file has names itself too, an old Modular Bomb Rack's as well)
        if (_byName.Value.TryGetValue(name, out var hit) && hit.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            return hit; // (a blueprint names a part a step: a lookup, not a walk over every part)
        if (PartMaterials.Parse(name) is { } remade)
            return remade; // (a part of another material names itself: "Plate Steel 0.5 m (wood)")
        foreach (var d in All)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return Renamed(name) is { } now ? Find(now) : null;
    }

    // Every part by name, the first of a name as the walk in Find would give it (read-only once made: parse threads read it too).
    private static readonly Lazy<Dictionary<string, ConstructPartDef>> _byName = new(() =>
    {
        var map = new Dictionary<string, ConstructPartDef>(All.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var d in All)
            map.TryAdd(d.Name, d);
        return map;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A part's current name when an older save, blueprint or map still uses a former one (decision 28: one fluid hose), else null.</summary>
    public static string? Renamed(string name) => name.ToLowerInvariant() switch
    {
        "fuel hose reel" or "water hose reel" => "Fluid Hose Reel",
        "fuel hose inlet" or "water hose inlet" => "Fluid Hose Inlet",
        "flight panel" => "Flight Control Panel",
        "medium flight panel" => "Medium Flight Control Panel",
        "large flight panel" => "Large Flight Control Panel",
        "long missile fuel tank" => "Missile Fuel Tank", // (folded: a design's tank has a length now, OrdnanceDesign.Folded; a stored one is a 0.5 m tank)
        _ => null,
    };

    public static ConstructPartDef Need(string name) => Find(name) ?? throw new ConstructPartException($"no construct part named \"{name}\"");

    /// <summary>
    /// Loads every part file of a folder (sorted by file name), then the mods' changes and new parts (docs/mods/MODS.md),
    /// refusing bad ones into <paramref name="refused"/>.
    /// </summary>
    public static ConstructPartDef[] LoadFolder(string folder, List<string> refused, IReadOnlyList<Mods.Mod>? mods = null)
    {
        var list = new List<ConstructPartDef>();
        if (!Directory.Exists(folder))
        {
            refused.Add($"{folder}: the construct parts folder is missing");
            return [];
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var df in Mods.Resolve(Mods.BaseFiles(folder, true), mods ?? [], "Constructs/Parts", true, refused, (t, f) => Parse(t, f)))
        {
            try
            {
                var def = Parse(df.Text, df.File);
                def.Folder = df.RelFolder;
                def.Sockets = WithCentreSockets(def);
                if (!names.Add(def.Name))
                    throw new ConstructPartException($"{df.File}: another file already has the name \"{def.Name}\"");
                if (list.Count >= MaxTypes)
                    throw new ConstructPartException($"{df.File}: more than {MaxTypes} part types");
                def.Id = (ushort)list.Count;
                list.Add(def);
            }
            catch (Exception ex) when (ex is ConstructPartException or JsonException or IOException)
            {
                var why = ex is ConstructPartException ? ex.Message : $"{df.File}: {ex.Message}";
                refused.Add(df.Mod.Length > 0 ? $"mod {df.Mod}: {why}" : why);
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
                : t.Sockets.Length != d.Sockets.Length || t.Sockets.Where((s, i) => s.Name != d.Sockets[i].Name || s.Dir != d.Sockets[i].Dir || s.Kind != d.Sockets[i].Kind || s.Class != d.Sockets[i].Class || Vector3.DistanceSquared(s.At, d.Sockets[i].At) > 1e-6f).Any()
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
        "format", "name", "description", "role", "tool", "attachSeconds", "mass", "health", "armour", "material", "finish", "friction", "collider",
        "onBreak", "color", "boxes", "sockets", "engine", "wheel", "suspension", "fuelTank", "fluidTank", "seat", "hinge", "joint", "logic",
        "transmission", "battery", "alternator", "hitch", "power", "dial", "gun", "gunPart", "ammoBox", "yield", "core", "group", "section", "storage", "differential", "drive", "config", "legacy", "hydraulics", "toolOnly", "tray", "walk", "boarding", "damaged", "stage", "faces", "faceAxis",
        "sight", "ordnance", "mooring", "wear", "physicsOnly", "hose", "ports", "centreSockets", "explosive", "tier", "dieFrame", "enginePiece", "sizeClass", "generator", "thruster", "burner", "flame", "rotor", "surface", "smoke", "dock", "launch", "jump", "conveyor", "panels", "pressDie", "remote", "camera", "shell", "office", "requisition", "media", "speaker",
    ];
    /// <summary>The keys a part box takes: its place, colour, detail and hidden flags, a contoured shape with its settings (index 8 up to the last), and the glass flag (last).</summary>
    /// <summary>The keys a part box takes: its place, colour, detail and hidden flags, and a contoured shape with its settings.</summary>
    private static readonly string[] BoxKeys =
        ["min", "max", "at", "size", "color", "detail", "hidden", "shape", "segments", "inner", "ends", "taper", "shift", "radius", "edges", "angle", "dir", "points", "glass", "paintable", "side", "glow", "pump", "spin", "roll", "use"];

    /// <summary>The keys an input reader can read: the low byte is <see cref="Buttons"/>, then fire, aim and reload (sent by a seated client).</summary>
    public static readonly (string Name, ushort Bit)[] ReaderKeys =
    [
        ("fire", 0x100), ("aim", 0x200), ("reload", 0x400), ("forward", (ushort)Buttons.Forward), ("back", (ushort)Buttons.Back),
        ("left", (ushort)Buttons.Left), ("right", (ushort)Buttons.Right), ("jump", (ushort)Buttons.Jump), ("sprint", (ushort)Buttons.Sprint),
    ];

    public const ushort KeyFire = 0x100, KeyAim = 0x200, KeyReload = 0x400, KeyRollLeft = 0x800, KeyRollRight = 0x1000, KeyHook = 0x2000, KeyEject = 0x4000;

    /// <summary>Parses one part file's text. Throws <see cref="ConstructPartException"/> with the reason for anything wrong.</summary>
    public static ConstructPartDef Parse(string json, string file)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: a part file is one JSON object");
        if (Opt(root, "modularRack") is { } rackShape)
            return Parse(ModularRack.Expand(root, rackShape, file), file); // (a preset of the Ordnance Rack: its shape is made from its settings, ModularRack.cs)
        Known(root, TopKeys, file);
        if (Opt(root, "format") is { } fmt && fmt.GetString() != Format)
            throw new ConstructPartException($"{file}: \"format\" must be \"{Format}\"");
        var d = new ConstructPartDef { File = file };
        d.Name = StrKey(root, "name", file);
        var most = Opt(root, "stage") is { ValueKind: JsonValueKind.True } ? MaxRuntimeName : MaxName;
        if (d.Name.Length == 0 || d.Name.Length > most || d.Name.Any(c => c < 32 || c > 126))
            throw new ConstructPartException($"{file}: \"name\" must be 1-{most} plain ASCII characters");
        d.Description = Opt(root, "description") is { } desc ? AsciiText(desc, file, "description") : "";
        d.Group = Opt(root, "group") is { } grp ? AsciiText(grp, file, "group") : "";
        d.Section = Opt(root, "section") is { } sec ? AsciiText(sec, file, "section") : "";
        if (d.Group.Length > 32 || d.Section.Length > 32 || (d.Section.Length > 0 && d.Group.Length == 0))
            throw new ConstructPartException($"{file}: \"group\" and \"section\" are at most 32 characters, and a \"section\" needs a \"group\"");
        d.Role = EnumOf<PartRole>(StrKey(root, "role", file), file, "role");
        // Decision 27 (tools by function): the file's "tool" is now only a hint that the part is structural ("welder": physics-only;
        // "wrench", or left out: an item unless it is heavy or bulky). The wrench is gone from the game and the attach tool fits
        // both, but the value keeps its meaning, so 533 part files and the generators stay as they are.
        var hint = Opt(root, "tool") is { } th ? EnumOf<ConstructTool>(Str(th, file, "tool"), file, "tool") : ConstructTool.Wrench;
        if (hint is ConstructTool.None or ConstructTool.Linker or ConstructTool.Configure or ConstructTool.Paint or ConstructTool.PhysicsGun or ConstructTool.Remover or ConstructTool.GroundDrill or ConstructTool.Selector or ConstructTool.Tracks or ConstructTool.Nudge or ConstructTool.NoCollide or ConstructTool.Texture or ConstructTool.Fuse or ConstructTool.Material)
            throw new ConstructPartException($"{file}: \"tool\" is \"wrench\" or \"welder\"");
        d.AttachSeconds = Opt(root, "attachSeconds") is { } secs ? Num(secs, file, "attachSeconds") : hint == ConstructTool.Welder ? 2.5f : 1.5f;
        Range(d.AttachSeconds, 0.2f, MaxAttachSeconds, file, "attachSeconds");
        d.Legacy = Opt(root, "legacy") is { ValueKind: JsonValueKind.True };
        d.Stage = Opt(root, "stage") is { ValueKind: JsonValueKind.True };
        d.Walk = Opt(root, "walk") is { } walk ? EnumOf<WalkKind>(Str(walk, file, "walk"), file, "walk") : WalkKind.None;
        d.Boarding = Opt(root, "boarding") is { ValueKind: JsonValueKind.True }; // (a grab handle, a boarding ladder, a step plate: Use grabs on here first; ConstructRide.cs)
        if (Opt(root, "damaged") is { } dmg)
        {
            if (dmg.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: \"damaged\" is {{\"below\": a share of its health, \"part\": the stage it turns into}}");
            Known(dmg, ["below", "part"], $"{file}, damaged");
            d.DamagedBelow = Num(Need(dmg, "below", $"{file}, damaged"), file, "damaged.below");
            Range(d.DamagedBelow, 0.05f, 0.95f, file, "damaged.below");
            d.DamagedName = StrKey(dmg, "part", $"{file}, damaged");
        }
        d.Class = Opt(root, "sizeClass") is { } szc ? ClassOf(Str(szc, file, "sizeClass"), file, "sizeClass") : Small;
        d.Tier = Opt(root, "tier") is { } tier ? (int)Num(tier, file, "tier") : 1;
        Range(d.Tier, 1, MaxTier, file, "tier");
        d.Mass = Num(Need(root, "mass", file), file, "mass");
        Range(d.Mass, 0.5f, MaxPartMass, file, "mass"); // (building pieces: a 4 m concrete pad is about 14 t; a Capital flywheel 280 t)
        // Decision 21: what can't be carried as an item, by the tool that fits it and its mass (so every new welder or heavy part follows without a list).
        var bulky = Opt(root, "physicsOnly") is { } po && (po.ValueKind == JsonValueKind.True ? true : po.ValueKind == JsonValueKind.False ? false : throw new ConstructPartException($"{file}: \"physicsOnly\" is true or false"));
        d.PhysicsWhy = hint == ConstructTool.Welder ? "structural" : d.Mass > HandCarryLimit ? $"heavy: {d.Mass:0} kg" : bulky ? "bulky" : "";
        d.PhysicsOnly = d.PhysicsWhy.Length > 0;
        d.Tool = ConstructBuild.ToolFor(d); // (the attach tool, for every part)
        d.Health = Num(Need(root, "health", file), file, "health");
        Range(d.Health, 1f, MaxPartHealth, file, "health");
        d.Armour = Opt(root, "armour") is { } arm ? Num(arm, file, "armour") : 0f;
        Range(d.Armour, 0f, 1000f, file, "armour");
        d.Material = Opt(root, "material") is { } mat ? Str(mat, file, "material") : "steel";
        if (Opt(root, "finish") is { } partFinish)
            d.SurfaceOverride = SurfaceByName(partFinish, file, "the part");
        d.Friction = Opt(root, "friction") is { } fr ? Num(fr, file, "friction") : d.Material == "rubber" ? 1.0f : 0.6f;
        Range(d.Friction, 0f, 2f, file, "friction");
        if (Opt(root, "collider") is { } coll)
            d.ColliderBoxes = Str(coll, file, "collider") switch
            {
                "boxes" => true,
                "shape" => false,
                var other => throw new ConstructPartException($"{file}: \"collider\" is boxes or shape, not \"{other}\""),
            };
        d.OnBreak = Opt(root, "onBreak") is { } ob ? EnumOf<PartBreak>(Str(ob, file, "onBreak"), file, "onBreak")
            : d.Role is PartRole.Engine or PartRole.FuelTank or PartRole.FluidTank or PartRole.Seat or PartRole.Transmission or PartRole.Differential or PartRole.Battery or PartRole.Alternator or PartRole.Transfer or PartRole.EnginePiece ? PartBreak.Disable : PartBreak.Detach;
        if (Opt(root, "ordnance") is { } ordn)
            d.Ord = OrdSectionDef.Parse(ordn, file);
        if (Opt(root, "sight") is { } sgt)
        {
            const string sw = "sight";
            Known(sgt, ["kind", "reach", "cone"], $"{file}: \"{sw}\"");
            d.Sight = Str(Need(sgt, "kind", file), file, "sight.kind").ToLowerInvariant() switch
            {
                "lead" => SightKind.Lead,
                "seeker" => SightKind.Seeker,
                var other => throw new ConstructPartException($"{file}: \"sight.kind\" is lead or seeker, not \"{other}\""),
            };
            d.SightReach = Num(Need(sgt, "reach", file), file, "sight.reach");
            Range(d.SightReach, 10f, 5000f, file, "sight.reach");
            d.SightCone = Num(Need(sgt, "cone", file), file, "sight.cone");
            Range(d.SightCone, 0.5f, 90f, file, "sight.cone");
        }
        if (Opt(root, "explosive") is { } ex)
        {
            const string ew = "explosive";
            Known(ex, ["radius", "damage", "fuse", "knockback", "crater"], $"{file}: \"{ew}\"");
            var br = new PropBreakable
            {
                ExplodeRadius = Num(Need(ex, "radius", file), file, "explosive.radius"),
                ExplodeDamage = Num(Need(ex, "damage", file), file, "explosive.damage"),
                ExplodeFuse = Opt(ex, "fuse") is { } fu ? Num(fu, file, "explosive.fuse") : ConstructWeapons.CookOffDelay,
                ExplodeCrater = Opt(ex, "crater") is { } cr ? Num(cr, file, "explosive.crater") : 0f,
            };
            if (Opt(ex, "knockback") is { } kb)
                br.ExplodeKnockback = Num(kb, file, "explosive.knockback");
            Range(br.ExplodeRadius, 0.5f, 150f, file, "explosive.radius");
            Range(br.ExplodeDamage, 1f, 100000f, file, "explosive.damage");
            Range(br.ExplodeFuse, 0.05f, 30f, file, "explosive.fuse");
            Range(br.ExplodeCrater, 0f, 60f, file, "explosive.crater");
            Range(br.ExplodeKnockback, 0f, 200f, file, "explosive.knockback");
            d.Explosive = br;
            d.Warhead = true; // (a bomb part's own charge: it goes off when it is broken)
        }
        d.Yield = Opt(root, "yield") is { } yl ? (int)MathF.Round(Num(yl, file, "yield")) : 0;
        Range(d.Yield, 0f, 10000f, file, "yield");
        d.Core = Opt(root, "core") is { } core && (core.ValueKind == JsonValueKind.True ? true : core.ValueKind == JsonValueKind.False ? false : throw new ConstructPartException($"{file}: \"core\" is true or false"));
        var colour = Opt(root, "color") is { } col ? Colour(col, file, "color") : new Vector3(0.5f);

        // Boxes
        if (Opt(root, "boxes") is not { ValueKind: JsonValueKind.Array } boxes || boxes.GetArrayLength() == 0)
            throw new ConstructPartException($"{file}: \"boxes\" must list at least one box");
        if (boxes.GetArrayLength() > MaxBoxes)
            throw new ConstructPartException($"{file}: at most {MaxBoxes} boxes a part");
        var list = new List<CPartBox>();
        var fronts = new List<byte>();
        var bi = 0;
        foreach (var b in boxes.EnumerateArray())
        {
            var where = $"boxes[{bi++}]";
            Known(b, [.. BoxKeys, "finish", "front", "rig", "pivot", "from", "to", "span", "key", "travel"], $"{file}, {where}");
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
            if (Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)) is var far && MathF.Max(far.X, MathF.Max(far.Y, far.Z)) > MaxPartReach)
                throw new ConstructPartException($"{file}: {where} reaches more than {MaxPartReach} m from the part's origin");
            var c = Opt(b, "color") is { } bc ? Colour(bc, file, where + ".color") : colour;
            var detail = Opt(b, "detail") is { ValueKind: JsonValueKind.True };
            var hidden = Opt(b, "hidden") is { ValueKind: JsonValueKind.True };
            if (hidden && detail)
                throw new ConstructPartException($"{file}: {where} is a detail (drawn only) and hidden (never drawn): a hidden box is a solid one's collision");
            byte front = 0;
            if (Opt(b, "front") is { } fd)
                front = (byte)(DirOf(Str(fd, file, where + ".front"), file, where + ".front") + 1);
            fronts.Add(front);
            var shape = ShapeOf(b, CPartBox.MeshSize(max - min, front), (min + max) * 0.5f, detail, file, where);
            if (front != 0 && shape == 0)
                throw new ConstructPartException($"{file}: {where}: \"front\" turns a shape (give the box a \"shape\")");
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
            // The paint tool (ConstructPaint.cs): "paintable" says whether the paint colour replaces the box's own. Default: yes, but not glass and not a wheel's tyre (its solid boxes).
            var paintable = glass <= 0 && !(d.Role == PartRole.Wheel && !detail);
            if (Opt(b, "paintable") is { } pa)
                paintable = pa.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw new ConstructPartException($"{file}: {where}.paintable is true or false"),
                };
            var side = Opt(b, "side") is { } sd ? Str(sd, file, where + ".side") switch
            {
                "base" => JointSides.Base,
                "leaf" => JointSides.Leaf,
                _ => throw new ConstructPartException($"{file}: {where}.side is \"base\" (the fixed side of a joint) or \"leaf\" (the moving side)"),
            } : (byte)0;
            var boxGlow = Opt(b, "glow") is { ValueKind: JsonValueKind.True }; // (drawn unlit and bright, like a lit lamp)
            var pump = Opt(b, "pump") is { } pm ? Num(pm, file, where + ".pump") : 0f; // (a modular engine's piston: slides this far up and down, ConstructEngine.cs)
            var turns = Opt(b, "spin") is { ValueKind: JsonValueKind.True }; // (a crank or flywheel: turns about the part's z with the engine)
            if ((pump != 0 || turns) && !detail)
                throw new ConstructPartException($"{file}: {where}: only a detail box can \"pump\" or \"spin\" (a moving box adds no collision)");
            var roll = Opt(b, "roll") is { ValueKind: JsonValueKind.True }; // (a track's drive wheel or roller: turns on the spot as the track runs)
            if (roll && !detail)
                throw new ConstructPartException($"{file}: {where}: only a detail box may \"roll\"");
            var made = new CPartBox((min + max) / 2, (max - min) / 2, c, detail, shape, hidden, glass, paintable, ConstructPaint.ToneOf(c, colour), side, boxGlow, pump, turns, roll);
            made = made with { Use = Opt(b, "use") is { ValueKind: JsonValueKind.True } };
            if (Opt(b, "finish") is { } boxFinish)
                made = made with { Surface = SurfaceByName(boxFinish, file, where) };
            if (Opt(b, "key") is { } keyBox)
                made = made with { Key = Str(keyBox, file, where + ".key") is { Length: 1 } one ? one[0] : throw new ConstructPartException($"{file}: {where}.key is one character") };
            list.Add(Opt(b, "rig") is { } rg ? ParseRig(made, b, rg, file, where) : made);
        }
        for (var i = 0; i < list.Count; i++)
            list[i] = list[i] with { Front = fronts[i] };
        if (list.All(x => x.Detail))
            throw new ConstructPartException($"{file}: every box is a detail: at least one must be solid");
        if (list.All(x => x.Hidden))
            throw new ConstructPartException($"{file}: every box is hidden: at least one must be drawn");
        // Solid boxes first (the physics walks those), details after.
        d.Boxes = list.Where(x => !x.Detail).Concat(list.Where(x => x.Detail)).ToArray();
        ReadFlames(root, d, file);
        d.HasEngineBoxes = list.Any(x => x.Pump != 0 || x.Spin);
        d.HasRig = list.Any(x => x.Rig != BoxRig.None);
        d.UseBoxes = Enumerable.Range(0, d.Boxes.Length).Where(k => d.Boxes[k].Use || d.Boxes[k].Rig is BoxRig.Lever or BoxRig.Knob or BoxRig.Slide or BoxRig.Stick).ToArray(); // (an Analog Lever's rig is its handle)
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
        if (sockets.GetArrayLength() > MaxSockets)
            throw new ConstructPartException($"{file}: at most {MaxSockets} sockets a part");
        var socks = new List<CSocket>();
        var si = 0;
        foreach (var s in sockets.EnumerateArray())
        {
            var where = $"sockets[{si++}]";
            Known(s, ["name", "at", "dir", "kind", "class"], $"{file}, {where}");
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
            socks.Add(new CSocket(name, at, dir, kind, Opt(s, "class") is { } scl ? ClassOf(Str(scl, file, where + ".class"), file, where + ".class") : d.Class));
        }
        d.Sockets = socks.ToArray();
        d.CentreOptOut = Opt(root, "centreSockets") is { ValueKind: JsonValueKind.False };
        d.IsDieFrame = Opt(root, "dieFrame") is { ValueKind: JsonValueKind.True };
        d.IsRequisition = Opt(root, "requisition") is { ValueKind: JsonValueKind.True };

        // Role data
        string[] roleKeys = ["engine", "wheel", "suspension", "fuelTank", "seat", "hinge", "joint", "logic", "transmission", "battery", "alternator", "hitch", "gun", "gunPart", "ammoBox", "storage", "differential", "drive", "hose", "enginePiece", "generator", "thruster", "burner", "fluidTank", "rotor"];
        var mine = d.Role switch
        {
            PartRole.Engine => "engine",
            PartRole.Wheel => "wheel",
            PartRole.Suspension => "suspension",
            PartRole.FuelTank => "fuelTank",
            PartRole.FluidTank => "fluidTank",
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
            PartRole.Hose => "hose",
            PartRole.EnginePiece => "enginePiece",
            PartRole.Generator => "generator",
            PartRole.Thruster => "thruster",
            PartRole.Burner => "burner",
            PartRole.Rotor => "rotor",
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
                d.DataIn = ["run", "throttle", ConstructModules.Bus]; // (board item cs03: "run" alone runs it at its signal, as before; with "throttle" wired too, "run" starts and stops it and "throttle" sets the throttle)
                break;
            case PartRole.EnginePiece:
                ParseEnginePiece(d, block!.Value, file, bw);
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
                Range(d.FuelCapacity, 0.1f, 100_000_000, file, "fuelTank.capacity");
                d.TankFluid = Opt(block.Value, "fluid") is { } fl ? EnumOf<Fluid>(Str(fl, file, "fuelTank.fluid"), file, "fuelTank.fluid") : Fluid.Fuel;
                break;
            case PartRole.FluidTank:
                Known(block!.Value, ["capacity", "flow", "volume", "envelope"], bw);
                d.IsTankVolume = Opt(block.Value, "volume") is { ValueKind: JsonValueKind.True };
                if (d.IsTankVolume && Opt(block.Value, "capacity") != null)
                    throw new ConstructPartException($"{file}: a tank volume's capacity comes from its box (no \"capacity\")");
                d.FuelCapacity = d.IsTankVolume ? 0 : Num(Need(block.Value, "capacity", bw), file, "fluidTank.capacity");
                if (!d.IsTankVolume)
                    Range(d.FuelCapacity, 0.1f, 100_000_000, file, "fluidTank.capacity");
                d.TankFlow = Num(Need(block.Value, "flow", bw), file, "fluidTank.flow");
                Range(d.TankFlow, 0.1f, 1_000_000, file, "fluidTank.flow");
                if (Opt(block.Value, "envelope") is { } env)
                {
                    if (!d.IsTankVolume)
                        throw new ConstructPartException($"{file}: an envelope is a sized box (\"volume\": true)");
                    Known(env, ["skin", "side", "patch", "vacuum"], bw);
                    d.Envelope = new EnvelopeDef
                    {
                        Skin = Num(Need(env, "skin", bw), file, "envelope.skin"),
                        Side = Num(Need(env, "side", bw), file, "envelope.side"),
                        Patch = Opt(env, "patch") is { } pa ? Num(pa, file, "envelope.patch") : 0.5f,
                        Vacuum = Opt(env, "vacuum") is { ValueKind: JsonValueKind.True },
                    };
                    Range(d.Envelope.Skin, 0.001f, 1000, file, "envelope.skin");
                    Range(d.Envelope.Side, 0.2f, 1000, file, "envelope.side");
                    d.DataIn = ["vent"];
                }
                break;
            case PartRole.Seat:
                Known(block!.Value, ["at", "helm", "eject", "bunk"], bw);
                d.SeatAt = Vec(Need(block.Value, "at", bw), file, "seat.at");
                d.Helm = Opt(block.Value, "helm") is { ValueKind: JsonValueKind.True };
                d.SeatEject = Opt(block.Value, "eject") is { ValueKind: JsonValueKind.True };
                d.Bunk = Opt(block.Value, "bunk") is { ValueKind: JsonValueKind.True };
                if (d.SeatEject)
                    d.DataIn = ["eject"];
                break;
            case PartRole.Hinge:
                d.DataIn = ["Lock", "Push"]; // (a signal locks it; Push, wired, says whether a player can swing it: ConstructDoor.PushOpens)
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
                RequireSides(d, file);
                break;
            case PartRole.Joint:
                ParseJoint(d, block!.Value, file, bw);
                RequireSides(d, file);
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
                Range(d.BatteryCapacity, 1, 100_000_000_000f, file, "battery.capacity");
                break;
            case PartRole.Alternator:
                Known(block!.Value, ["rate"], bw);
                d.AlternatorRate = Num(Need(block.Value, "rate", bw), file, "alternator.rate");
                Range(d.AlternatorRate, 0.01f, 1_000_000, file, "alternator.rate");
                break;
            case PartRole.Hitch:
            {
                Known(block!.Value, ["kind", "at", "chain", "winch"], bw);
                d.Hitch = EnumOf<HitchKind>(Str(Need(block.Value, "kind", bw), file, "hitch.kind"), file, "hitch.kind");
                d.HitchAt = Vec(Need(block.Value, "at", bw), file, "hitch.at");
                if (MathF.Max(MathF.Abs(d.HitchAt.X), MathF.Max(MathF.Abs(d.HitchAt.Y), MathF.Abs(d.HitchAt.Z))) > MaxHitchReach)
                    throw new ConstructPartException($"{file}: \"hitch.at\" is more than {MaxHitchReach} m from the part's origin");
                d.HitchChain = Opt(block.Value, "chain") is { } ch ? Num(ch, file, "hitch.chain") : 0f;
                Range(d.HitchChain, 0, MaxHitchReach, file, "hitch.chain");
                if (d.HitchChain > 0 && d.Hitch != HitchKind.Hook)
                    throw new ConstructPartException($"{file}: only a hook has a \"chain\"");
                if (d.Hitch == HitchKind.Hook && d.HitchChain < 0.3f)
                    throw new ConstructPartException($"{file}: a hook needs a \"chain\" of 0.3 to {MaxHitchReach} m");
                ParseWinch(d, Opt(block.Value, "winch"), file);
                break;
            }
            case PartRole.Hose:
            {
                Known(block!.Value, ["end", "carries", "length", "flow", "at"], bw);
                d.HoseEnd = EnumOf<HoseEnd>(Str(Need(block.Value, "end", bw), file, "hose.end"), file, "hose.end");
                var carries = Str(Need(block.Value, "carries", bw), file, "hose.carries");
                d.HoseCarry = EnumOf<HoseCarry>(carries.ToLowerInvariant() is "fuel" or "water" ? "fluid" : carries, file, "hose.carries"); // (the old fuel and water hoses are the one fluid hose)
                d.HoseAt = Vec(Need(block.Value, "at", bw), file, "hose.at");
                if (MathF.Max(MathF.Abs(d.HoseAt.X), MathF.Max(MathF.Abs(d.HoseAt.Y), MathF.Abs(d.HoseAt.Z))) > 1.5f)
                    throw new ConstructPartException($"{file}: \"hose.at\" is more than 1.5 m from the part's origin");
                if (d.HoseEnd == HoseEnd.Outlet)
                {
                    d.HoseLength = Num(Need(block.Value, "length", bw), file, "hose.length");
                    Range(d.HoseLength, 2, 30, file, "hose.length");
                    d.HoseFlow = Opt(block.Value, "flow") is { } hf ? Num(hf, file, "hose.flow") : 1f;
                    Range(d.HoseFlow, 0, 100, file, "hose.flow");
                }
                else if (Opt(block.Value, "length") != null || Opt(block.Value, "flow") != null)
                    throw new ConstructPartException($"{file}: only a hose outlet has a \"length\" and a \"flow\"");
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
                if (wp.Category == PartCategory.Receiver)
                    throw new ConstructPartException($"{file}: \"gunPart.part\": a receiver doesn't go on a construct gun as a gun part (make it a \"gun\" with a \"receiver\")");
                d.GunPartName = wp.Name;
                break;
            }
            case PartRole.Storage:
                ConstructStorage.ParseStorage(d, block!.Value, file);
                break;
            case PartRole.Generator:
                ParseGenerator(d, block!.Value, file, bw);
                break;
            case PartRole.Burner:
                Known(block!.Value, ["heat", "burnRate", "at", "reach"], bw);
                d.BurnerHeat = Num(Need(block.Value, "heat", bw), file, "burner.heat");
                Range(d.BurnerHeat, 1, 1e9f, file, "burner.heat");
                d.BurnerRate = Num(Need(block.Value, "burnRate", bw), file, "burner.burnRate");
                Range(d.BurnerRate, 0.001f, 100_000, file, "burner.burnRate");
                d.FlameAt = Opt(block.Value, "at") is { } fa ? Vec(fa, file, "burner.at") : Vector3.Zero;
                if (Opt(block.Value, "reach") is { } flame)
                    d.FlameReach = Num(flame, file, "burner.reach");
                d.DataIn = ["throttle", "height"];
                break;
            case PartRole.Thruster:
                ParseThruster(d, block!.Value, file, bw);
                break;
            case PartRole.Rotor:
                ParseRotor(d, block!.Value, file, bw);
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
        d.PowerDraw = Opt(root, "power") is { } pw ? Num(pw, file, "power") : d.Role == PartRole.Logic ? 2f : d.Role == PartRole.Joint && d.JointType is not (JointType.Steering or JointType.Flex or JointType.Slider or JointType.Swivel) && d.RailDrive != RailDriveKind.Shaft ? 10f : d.ActiveSuspension ? 150f : 0f;
        Range(d.PowerDraw, 0, 10_000_000_000, file, "power");
        ParseSmoke(d, root, file);
        ParseSurface(d, root, file);
        ParseNamedPorts(d, root, file); // (link ports by function: ConstructPorts.cs)
        ConstructRemote.Parse(d, root, file); // (the wireless seat link and cameras: ConstructRemote.cs)
        ConstructShell.Parse(d, root, file); // (robot shells and their chairs: ConstructShell.cs)
        ConstructPanels.Parse(d, root, file); // (display panels: DisplayPanel.cs)
        ConstructMedia.Parse(d, root, file);
        ConstructSpeakers.Parse(d, root, file);
        OfficeParts.Parse(d, root, file); // (the fax and the computer, after their panels: OfficeParts.cs)
        ConstructPress.ParseDie(d, root, file);
        if (d.IsTerminal && !ConstructTerminals.HasPorts(d))
            throw new ConstructPartException($"{file}: a terminal names two chute ports: \"{ConstructTerminals.NetworkPort}\" (both ways) and \"{ConstructTerminals.OutPort}\" (out only)");
        ParseSystems(d, root, file); // (docks, catapults, conveyors: ConstructSystems.cs)
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
    private static ushort ShapeOf(JsonElement b, Vector3 size, Vector3 centre, bool detail, string file, string where)
    {
        var shape = Opt(b, "shape") is { } sh ? Str(sh, file, where + ".shape").ToLowerInvariant() : "box";
        if (shape == "box")
        {
            foreach (var k in BoxKeys.AsSpan(8, BoxKeys.Length - 16))
                if (Opt(b, k) != null)
                    throw new ConstructPartException($"{file}: {where}: \"{k}\" is a shape's setting (give the box a \"shape\")");
            return 0;
        }
        if (Array.IndexOf(PartShapes.Names, shape) < 0)
            throw new ConstructPartException($"{file}: {where}.shape is box or one of {string.Join(", ", PartShapes.Names)}, not \"{shape}\"");
        if (!detail)
            throw new ConstructPartException($"{file}: {where} has a shape, so it must be a detail (\"detail\": true): collision stays boxes (give the part hidden solid boxes)");
        var settings = PartShapes.SettingsOf(shape);
        foreach (var k in BoxKeys.AsSpan(8, BoxKeys.Length - 16))
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
            Range(MathF.Min(pb.Shift.X, pb.Shift.Y), -64f, 64f, file, where + ".shift"); // (a sheet leaning a long way: a thin box's front end moved many of its thicknesses)
            Range(MathF.Max(pb.Shift.X, pb.Shift.Y), -64f, 64f, file, where + ".shift");
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
        if (Opt(b, "points") is { } pt)
        {
            if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() is < 4 or > 64)
                throw new ConstructPartException($"{file}: {where}.points is a list of 4 to 64 [x, y, z] in the part's frame");
            pb.Points = pt.EnumerateArray().Select(p => Vec(p, file, where + ".points") - centre).ToArray();
        }
        else if (shape == "hull")
            throw new ConstructPartException($"{file}: {where}: a hull needs its \"points\"");
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
        Known(b, ["type", "axis", "pivot", "min", "max", "leaf", "speed", "strength", "give", "sideLoad", "overloadDamage", "cone", "twist", "stiffness", "damping", "rail"], bw);
        var type = Str(Need(b, "type", bw), file, "joint.type").ToLowerInvariant();
        if (type == "flex")
        {
            ParseFlexJoint(d, b, file, bw);
            return;
        }
        var ti = Array.IndexOf(JointTypes, type);
        if (ti < 0 && type is not ("slider" or "swivel"))
            throw new ConstructPartException($"{file}: \"joint.type\" is one of {string.Join(", ", JointTypes)}, slider, swivel, flex, not \"{type}\"");
        d.JointType = type == "slider" ? JointType.Slider : type == "swivel" ? JointType.Swivel : (JointType)(ti + 1);
        if (d.JointType == JointType.Swivel)
            d.DataIn = ["Lock"];
        if (d.JointType == JointType.Steering)
            d.DataIn = ["angle", ConstructModules.Bus];
        d.JointAxis = DirOf(Str(Need(b, "axis", bw), file, "joint.axis"), file, "joint.axis");
        d.JointPivot = Opt(b, "pivot") is { } pv ? Vec(pv, file, "joint.pivot") : Vector3.Zero;
        var slider = d.JointType == JointType.Slider;
        var piston = d.JointType == JointType.Piston || slider;
        var unit = piston ? 1f : MathF.PI / 180f; // a piston's limits and speed are metres; the rest degrees
        ConstructLinear.ParseRail(d, b, file, bw);
        if (d.JointType is JointType.Turntable or JointType.Swivel && Opt(b, "min") == null && Opt(b, "max") == null)
        {
            d.JointLimited = false;
            d.JointMin = float.NegativeInfinity;
            d.JointMax = float.PositiveInfinity;
        }
        else
        {
            d.JointMin = Num(Need(b, "min", bw), file, "joint.min") * unit;
            d.JointMax = Num(Need(b, "max", bw), file, "joint.max") * unit;
            if (d.IsRail)
            {
                if (d.JointMin >= 0 || d.JointMax <= 0 || d.JointMax - d.JointMin < 0.1f || d.JointMax - d.JointMin > MaxPistonTravel)
                    throw new ConstructPartException($"{file}: a rail's \"min\" and \"max\" are where its ends are from its carriage's rest, less than 0 and more than 0, 0.1 to {MaxPistonTravel} metres apart");
            }
            else if (piston && (d.JointMin != 0 || d.JointMax < 0.1f || d.JointMax > MaxPistonTravel))
                throw new ConstructPartException($"{file}: a piston's \"min\" is 0 and its \"max\" (its travel) 0.1 to {MaxPistonTravel} metres");
            if (!piston && (d.JointMin > 0 || d.JointMax < 0 || d.JointMax - d.JointMin < 0.05f || d.JointMax - d.JointMin > MathF.Tau))
                throw new ConstructPartException($"{file}: joint \"min\" must be 0 or less and \"max\" 0 or more (degrees), at most 360 apart");
        }
        if (!slider && d.JointType != JointType.Swivel)
        {
            d.MotorSpeed = Num(Need(b, "speed", bw), file, "joint.speed") * unit;
            Range(d.MotorSpeed, 0.05f, piston ? 10f : 4 * MathF.Tau, file, "joint.speed");
            d.MotorStrength = Num(Need(b, "strength", bw), file, "joint.strength");
            Range(d.MotorStrength, 1, 2_000_000_000, file, "joint.strength");
        }
        // The strength model (ConstructPhysics.cs): how far it gives above its rating, side loads, overload damage (off unless given).
        d.JointGive = Opt(b, "give") is { } gv ? Num(gv, file, "joint.give") * unit : piston ? 0.25f * MathF.Max(d.JointMax, 0.1f) : 20 * MathF.PI / 180f;
        Range(d.JointGive, 0.001f, piston ? MaxPistonTravel : MathF.PI, file, "joint.give");
        d.SideLoad = Opt(b, "sideLoad") is { } sl ? Num(sl, file, "joint.sideLoad") : 0f;
        Range(d.SideLoad, 0, 2_000_000_000, file, "joint.sideLoad");
        if (d.SideLoad > 0 && (!piston || slider))
            throw new ConstructPartException($"{file}: only a piston takes \"sideLoad\"");
        d.OverloadDamage = Opt(b, "overloadDamage") is { } od ? Num(od, file, "joint.overloadDamage") : 0f;
        Range(d.OverloadDamage, 0, 100_000, file, "joint.overloadDamage");
        d.LeafSocket = Str(Need(b, "leaf", bw), file, "joint.leaf");
        d.LeafIndex = Array.FindIndex(d.Sockets, s => s.Name == d.LeafSocket && s.Kind == SocketKind.Struct);
        if (d.LeafIndex < 0)
            throw new ConstructPartException($"{file}: joint \"leaf\" must name one of its struct sockets (what goes on it moves)");
    }

    /// <summary>A flexible joint's block (ConstructFlex.cs): its axis (out towards the leaf: what it twists about), pivot, cone, twist, stiffness, damping, rating and leaf.</summary>
    private static void ParseFlexJoint(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        foreach (var no in (string[])["min", "max", "speed", "give", "sideLoad"])
            if (Opt(b, no) != null)
                throw new ConstructPartException($"{file}: a flexible joint has no motor or stops of its own: \"{no}\" doesn't apply (it has \"cone\", \"twist\", \"stiffness\" and \"damping\")");
        const float deg = MathF.PI / 180f;
        d.JointType = JointType.Flex;
        d.JointLimited = true;
        d.JointAxis = DirOf(Str(Need(b, "axis", bw), file, "joint.axis"), file, "joint.axis");
        d.JointPivot = Opt(b, "pivot") is { } pv ? Vec(pv, file, "joint.pivot") : Vector3.Zero;
        var cone = Num(Need(b, "cone", bw), file, "joint.cone");
        Range(cone, 2, 80, file, "joint.cone");
        d.JointMax = cone * deg;
        d.JointMin = -d.JointMax;
        var twist = Opt(b, "twist") is { } tw ? Num(tw, file, "joint.twist") : 0f;
        Range(twist, 0, 80, file, "joint.twist");
        d.FlexTwist = twist * deg;
        d.FlexStiffness = Num(Need(b, "stiffness", bw), file, "joint.stiffness") / deg; // (N m a degree in the file)
        Range(d.FlexStiffness * deg, 0.1f, 1_000_000, file, "joint.stiffness");
        d.FlexDamping = Opt(b, "damping") is { } dm ? Num(dm, file, "joint.damping") : 0.7f;
        Range(d.FlexDamping, 0, 4, file, "joint.damping");
        d.MotorStrength = Num(Need(b, "strength", bw), file, "joint.strength");
        Range(d.MotorStrength, 1, 1_000_000, file, "joint.strength");
        d.OverloadDamage = Opt(b, "overloadDamage") is { } od ? Num(od, file, "joint.overloadDamage") : 0f;
        Range(d.OverloadDamage, 0, 100_000, file, "joint.overloadDamage");
        d.LeafSocket = Str(Need(b, "leaf", bw), file, "joint.leaf");
        d.LeafIndex = Array.FindIndex(d.Sockets, s => s.Name == d.LeafSocket && s.Kind == SocketKind.Struct);
        if (d.LeafIndex < 0)
            throw new ConstructPartException($"{file}: joint \"leaf\" must name one of its struct sockets (what goes on it moves)");
    }

    /// <summary>A joint part tags boxes of both its sides (board item dev-joint-side-indicators-js01): "side": "base" and "side": "leaf".</summary>
    private static void RequireSides(ConstructPartDef d, string file)
    {
        if (!d.Boxes.Any(b => b.IsBase) || !d.Boxes.Any(b => b.IsLeaf))
            throw new ConstructPartException($"{file}: a joint part needs boxes tagged \"side\": \"base\" (the fixed side) and \"side\": \"leaf\" (the moving side)");
    }

    private static readonly string[] GunFxNames = ["bullet", "cannon", "rocket", "flame"];

    /// <summary>
    /// A gun's block: either a weapon builder receiver (<c>"receiver"</c>, its <c>"action"</c> optional: the barrels, feed,
    /// ammo and so on are gun parts bolted to it on the construct, and its numbers come from the weapon builder's stat
    /// pipeline) or a heavy construct gun's own numbers (damage, rpm, spread, range, mag, reload...).
    /// </summary>
    private static void ParseGun(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["shell", "thrust", "burn", "turn", "prox", "crater", "receiver", "action", "damage", "rpm", "spread", "range", "maxRange", "mag", "reload", "pellets", "speed", "flags", "blast", "blastDamage", "muzzle", "fx", "kick", "soundOf"], bw);
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
        if (Opt(b, "shell") is { } sh)
        {
            if (!Enum.TryParse<ShellKind>(Str(sh, file, "gun.shell"), ignoreCase: true, out var kind) || kind == ShellKind.Bomb)
                throw new ConstructPartException($"{file}: \"gun.shell\" is a shell a gun fires, one of {string.Join(", ", Enum.GetNames<ShellKind>().Where(n => n != nameof(ShellKind.Bomb)))}");
            d.GunShell = (int)kind;
            if (d.GunSpeed <= 0)
                throw new ConstructPartException($"{file}: a \"gun.shell\" needs a \"speed\"");
        }
        d.GunThrust = Opt(b, "thrust") is { } thr ? Num(thr, file, "gun.thrust") : 0f;
        Range(d.GunThrust, 0, 2000, file, "gun.thrust");
        d.GunBurn = Opt(b, "burn") is { } brn ? Num(brn, file, "gun.burn") : 0f;
        Range(d.GunBurn, 0, 60, file, "gun.burn");
        d.GunTurn = Opt(b, "turn") is { } trn ? Num(trn, file, "gun.turn") : 0f;
        Range(d.GunTurn, 0, 720, file, "gun.turn");
        d.GunProx = Opt(b, "prox") is { } prx ? Num(prx, file, "gun.prox") : 0f;
        Range(d.GunProx, 0, 100, file, "gun.prox");
        d.GunCrater = Opt(b, "crater") is { } crt ? Num(crt, file, "gun.crater") : 0f;
        Range(d.GunCrater, 0, 60, file, "gun.crater");
        if (d.GunShell < 0 && (d.GunThrust > 0 || d.GunBurn > 0 || d.GunTurn > 0 || d.GunProx > 0 || d.GunCrater > 0))
            throw new ConstructPartException($"{file}: \"thrust\", \"burn\", \"turn\", \"prox\" and \"crater\" belong to a gun with a \"shell\"");
        d.GunFlags = Opt(b, "flags") is { ValueKind: JsonValueKind.Array } fl ? fl.EnumerateArray().Select(f => Str(f, file, "gun.flags").ToLowerInvariant()).ToArray() : [];
        foreach (var f in d.GunFlags)
            if (f is not ("penetrating" or "explosive" or "incendiary"))
                throw new ConstructPartException($"{file}: \"gun.flags\": \"{f}\" (a heavy gun's flags are penetrating, explosive, incendiary)");
        d.GunBlast = Opt(b, "blast") is { } bl ? Num(bl, file, "gun.blast") : 0f;
        Range(d.GunBlast, 0, 60, file, "gun.blast");
        d.GunBlastDamage = Opt(b, "blastDamage") is { } bd ? Num(bd, file, "gun.blastDamage") : d.GunDamage;
        Range(d.GunBlastDamage, 0, 100000, file, "gun.blastDamage");
        d.GunMuzzle = Vec(Need(b, "muzzle", bw), file, "gun.muzzle");
    }

    private static readonly string[] LogicKinds = Enum.GetNames<CLogic>().Skip(1).Select(n => n.ToLowerInvariant()).ToArray();

    /// <summary>A logic part's block: its kind and, for the kinds that have one, the setting it cycles through (the linking tool's R).</summary>
    private static void ParseLogic(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["kind", "settings", "labels", "setting", "settingName", "blade", "throat", "ports", "text", "gyro", "kick", "share", "radius", "load", "rail", "station", "modular", "axes"], bw);
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
        LogicBlocks.Ports(d, Opt(b, "ports") is { } wide ? (int)Num(wide, file, "logic.ports") : 0);
        if (Opt(b, "axes") is { } axes)
        {
            var n = (int)Num(axes, file, "logic.axes");
            if (d.Logic != CLogic.AnalogLever || n is < 1 or > 2)
                throw new ConstructPartException($"{file}: \"logic.axes\" is an Analog Lever's, 1 or 2 (2: a Joystick)");
            if (n == 2)
            {
                d.Stick = true;
                d.DataOut = ["x", "y"]; // (X its first output, Y its second: AnalogLever.Step)
            }
        }
        d.TextSlots = Opt(b, "text") is { } texts ? (int)Num(texts, file, "logic.text") : 0;
        if (d.TextSlots is < 0 or > 2)
            throw new ConstructPartException($"{file}: \"logic.text\" is how many messages it holds, 0 to 2");
        d.RackLoad = Opt(b, "load") is { } ld ? Num(ld, file, "logic.load") : 0;
        d.RackRail = Opt(b, "rail") is { ValueKind: JsonValueKind.True };
        if (Opt(b, "modular") is { } modular)
        {
            if (d.Logic != CLogic.BombRack || d.RackRail)
                throw new ConstructPartException($"{file}: \"logic.modular\" is a bomb rack's shape");
            d.Rack = ModularRack.ShapeOf(modular, file);
            d.RackStation = Num(Need(b, "station", bw), file, "logic.station");
            Range(d.RackStation, 1f, MaxPartMass, file, "logic.station");
        }
        d.DecoupleKick = d.Logic == CLogic.Decoupler ? Opt(b, "kick") is { } kk ? Num(kk, file, "logic.kick") : 2f : 0;
        if (d.Logic == CLogic.AntiGrav)
        {
            d.AntiShare = Num(Need(b, "share", bw), file, "logic.share");
            Range(d.AntiShare, 0.01f, 1, file, "logic.share");
            d.AntiRadius = Num(Need(b, "radius", bw), file, "logic.radius");
            Range(d.AntiRadius, 0.5f, 100_000, file, "logic.radius");
        }
        if (d.Logic == CLogic.FlightStabilizer)
        {
            d.DataIn = ConstructFlight.InPorts;
            d.DataOut = ConstructFlight.OutPorts;
            d.Gyro = Opt(b, "gyro") is { } gyro ? Num(gyro, file, "logic.gyro") : 0;
        }
        if (d.IsSeatModule)
        {
            d.DataIn = ["seat"];
            d.DataOut = d.Logic == CLogic.DriveModule ? ConstructModules.DrivePorts : ConstructModules.TurretPorts;
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
    /// <summary>Large faces get a centre socket (board cs02): a face of the bounds whose both sides are at least this long (m).</summary>
    public const float CentreFaceMin = 0.75f;
    /// <summary>...unless one of the file's own sockets already faces out of it this close to the centre (m).</summary>
    public const float CentreNear = 0.10f;

    /// <summary>
    /// Appends a structural socket at the centre of each large face of a structural part (frames, panels, foundations) that
    /// has none near it, so things mount in the middle of a deck plate. Appended AFTER the file's own sockets: socket
    /// indices (links, blueprints, saves) never shift. A file opts out with "centreSockets": false. Named "centre +x" etc.
    /// </summary>
    public static CSocket[] WithCentreSockets(ConstructPartDef d)
    {
        if (d.CentreOptOut || d.Stage || d.DamagedName.Length > 0 || d.Role is not (PartRole.Frame or PartRole.Panel or PartRole.Foundation) || d.SolidBoxes == 0)
            return d.Sockets; // (a damage stage and the part that turns into it must keep exactly the same sockets)
        var lattice = d.Folder.StartsWith("Structure", StringComparison.Ordinal) || d.Folder.StartsWith("Building", StringComparison.Ordinal);
        List<CSocket>? add = null;
        for (byte dir = 0; dir < 6; dir++)
        {
            var axis = dir >> 1;
            var a1 = (axis + 1) % 3;
            var a2 = (axis + 2) % 3;
            if (d.BoundsMax[a1] - d.BoundsMin[a1] < CentreFaceMin || d.BoundsMax[a2] - d.BoundsMin[a2] < CentreFaceMin)
                continue;
            var at = (d.BoundsMin + d.BoundsMax) / 2;
            at[axis] = (dir & 1) == 0 ? d.BoundsMax[axis] : d.BoundsMin[axis];
            if (lattice)
            {
                // (the 0.25 m cell rules: along the face on a cell centre, out of it half a centimetre off a whole one)
                var o = at[axis] * 100 + 0.5f;
                if (MathF.Abs(at[a1] / 0.25f - MathF.Round(at[a1] / 0.25f)) > 1e-4f || MathF.Abs(at[a2] / 0.25f - MathF.Round(at[a2] / 0.25f)) > 1e-4f
                    || MathF.Abs(o - MathF.Round(o)) > 1e-3f || MathF.Abs(at[axis] * 100 - MathF.Round(at[axis] * 100)) < 1e-3f)
                    continue;
            }
            if (!OnBoxFace(d, at, dir))
                continue; // (the centre of the face is not solid there: a ring, a frame)
            var near = false;
            foreach (var s in d.Sockets)
                if (s.Dir == dir && MathF.Abs(s.At[axis] - at[axis]) < 0.02f && Vector3.DistanceSquared(s.At, at) < CentreNear * CentreNear)
                    near = true;
            if (near)
                continue;
            var name = "centre " + DirName(dir);
            if (d.Sockets.Any(x => x.Name == name))
                continue;
            (add ??= []).Add(new CSocket(name, at, dir, SocketKind.Struct, d.Class));
        }
        return add == null ? d.Sockets : [.. d.Sockets, .. add];
    }

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

    /// <summary>A size class by its file name.</summary>
    private static byte ClassOf(string name, string file, string what)
    {
        var k = Array.IndexOf(ClassNames, name);
        return k >= 0 ? (byte)k : throw new ConstructPartException($"{file}: \"{what}\" is small, medium, large or capital");
    }

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
