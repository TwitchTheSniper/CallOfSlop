using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What kind of control a part setting gets in the configure tool's panel (and how its value is kept).</summary>
public enum CfgType : byte
{
    Number, // a slider with numeric entry: Min..Max, snapped to Step
    Choice, // a dropdown: the index of one of Options
    Toggle, // on or off: 1 or 0
    Colour, // a colour: 0xRRGGBB kept exactly in the float (24 bits fit)
    Key, // a key binding (an input reader's key): the index of one of Options, picked by pressing the key
    Mask, // 16 bits of a cell grid (an Extruder's section): four such settings are one 8 by 8 grid in the panel, as a whole number
}

/// <summary>
/// What a part setting means to the game: the key a part file names it by. Only these are read by the simulation and the
/// drawing, so a part file naming any other key is refused (with the list); which part kinds read which is in
/// docs/sandbox/CONSTRUCTS.md, "The configure tool".
/// </summary>
public enum CfgKey : byte
{
    None,
    Setting, // the part's choice (CPart.Setting): a reader's key, a gearbox's gear, a selector's positions, a lock, a preset
    Period, // timer: seconds a cycle
    Duty, // timer: % of the period it is on
    StartOn, // timer, switch, toggle: starts on
    Seconds, // delay: how long
    Mode, // delay: what it delays (both, on, off)
    Operator, // compare: > >= = <= <
    Threshold, // compare, speed sensor
    Hysteresis, // compare
    Factor, // scale
    Offset, // scale
    Low, // scale: the least it puts out
    High, // scale: the most it puts out
    Latched, // button: push on, push off
    Hold, // button: seconds it stays on
    Radius, // proximity sensor: metres
    Units, // speed sensor: m/s, km/h, mph
    Colour, // lamp
    Brightness, // lamp
    Blink, // lamp: flashes a second (0: steady)
    Pitch, // horn
    Volume, // horn
    Min, // joints: the near limit (degrees; a piston's metres)
    Max, // joints: the far limit
    Speed, // joints: degrees (metres) a second
    Strength, // joints: N m (N); brake: %
    Stiffness, // suspension: spring frequency, Hz
    Damping, // suspension: damping ratio
    Team, // bot spawner: friendly or enemy (GameSandboxSpawns.cs)
    Difficulty, // bot spawner: Easy .. Insane
    Loadout, // bot spawner: any gun, or a weapon class
    Rate, // spawners: spawns a minute
    Group, // spawners: how many each spawn
    MaxAlive, // spawners: the most alive at once from it
    Total, // spawners: how many in all (0: no limit)
    Mix, // zombie spawner: walkers only, or the zombie types mixed in
    SizeX, // cargo volume: the box's width, height and length (metres, the part's frame; ConstructCargo.cs)
    SizeY,
    SizeZ,
    AtX, // cargo volume: the box's centre (metres from the part's origin, the part's frame)
    AtY,
    AtZ,
    OrbitX, // seat: the third-person camera's orbit point, metres from the default point (the construct's centre of mass raised by half its height), in the construct's frame (Source/App/ThirdPerson.cs)
    OrbitY,
    OrbitZ,
    OrbitDist, // seat: the third-person camera's starting distance
    ZoomMin, // seat: the nearest the wheel brings the camera
    ZoomMax, // seat: the furthest
    FollowYaw, // seat: the camera turns with the construct's heading
    Grip, // panels and floors: riders are held on it up to nearly vertical (ConstructRide.cs; board item rf01, phase 2)
    Recipe, // an Assembler: what it builds (0: nothing yet, else 1 + an index into AssemblingTable.Recipes)
    Profile, // Extruder: the section (ConstructExtrusion.cs: a preset, or Custom)
    Width, // Extruder: metres across the section
    Height, // Extruder: metres up the section
    MaskA, // Extruder: a Custom section's cell rows 0 and 1
    MaskB, // ...rows 2 and 3
    MaskC, // ...rows 4 and 5
    MaskD, // ...rows 6 and 7
    CutLength, // Extruder: the length at which it cuts the piece off by itself (0: only on a signal)
    JumpTarget, // jump drive (phase 6): which star system it jumps to, 1 the nearest other one, 2 the next... (Galaxy.Nearest)
    MirrorAxis, // Mirror Table: which plane (off, side to side, front to back, up and down: ConstructMirror.cs)
    MirrorOffset, // Mirror Table: metres from the construct's centre
    Friction, // wheel: dry grip, g (the part's grip times its material's)
    DirtGrip, // wheel: times that on dirt
    SideGrip, // wheel: times that sideways
    Rolling, // wheel: rolling resistance, % of its load
    Bounciness, // wheel: % (ConstructPhysics.Wheels and ConstructWheelShape.cs)
    Value, // Analog Lever: where the handle stands, 0..1 (AnalogLever.cs)
    Snap, // Analog Lever: the step it snaps to (0: smooth)
    Spring, // Analog Lever: goes back to its rest value when let go
    Rest, // Analog Lever: where the spring takes it; Electric rail: % along its travel it sits at with nothing wired (ConstructLinear.cs)
    StopLow, // rails: % along the whole rail of its low end stop
    StopHigh, // rails: % along the whole rail of its high end stop
    SetMode, // Electric rail: what a Set signal commands (0: position, 1: speed)
    Lead, // shaft rail: metres the carriage moves a turn of the shaft
    Limited, // free rotation part: its min and max apply (off: it turns forever)
    Fluid, // Fluid Tank: what it holds (0: none, else 1 + a Fluid; ConstructFluid.cs)
    Fill, // Fluid Tank, Storage Volume: how full it is, %
    Material, // Storage Volume: the bulk it holds (0: none, else a RecyclingTable id; ConstructStoreVolume.cs)
    BackDrive, // shaft rail: a load can push the screw back (off: it holds with the engine off; ConstructLinear.cs)
    Wall, // Extruder: a Pipe's wall thickness, metres
    RunMode, // Wheel Former: what it builds up (0: width, 1: depth)
    AmmoType, // an ammo box, crate or container: what it holds (ConstructAmmo.Types)
    Code, // Code Lock: the right number
    Invert, // Code Lock: on until opened (for a hinge's Lock), not the other way round
    Decimals, // Numpad, readouts: places after the point
    Unit, // readouts: what the number is in (a choice of the part's options)
    Channel, // Wireless pair: which channel
    Limit, // Counter: the most it counts to (0: no limit)
    Wrap, // Counter: past the limit it starts again at 0
    Ink, // displays, machines' screens: the colour of what they show (0: their own colours)
    Ramp, // Bar Gauges and Graph Screens: how the colour follows the value (PanelRamp)
    Charge, // a builder receiver: its powder charge, percent (PowderCharge)
    Pushable, // hinge: a player walking into what hangs on it swings it (ConstructDoor.PushOpens)
    SteerKind, // a steering differential: reverse, drag or clutch steering (SteerMode)
    Loop, // Video Screen, Music Player: the list starts again after its last track (ConstructMedia.cs)
    SpeakerMode, // speaker: every way, or directional (ConstructSpeakers.cs)
    HearRange, // speaker: metres, up to its size's most
    Rolloff, // speaker: how fast it falls off
    Curve, // speaker: inverse or linear falloff
    ConeInner, // speaker: the full level's cone, degrees
    ConeOuter, // speaker: where it reaches the outer level, degrees
    ConeGain, // speaker: the level outside the cone
    Hole, // lift cell: the holes in it, m2 (not on the panel; ConstructLift.cs)
    Reverse, // control surface: it turns the other way for the same signal
    Trim, // control surface: degrees it sits at with no signal
    Hand, // rotor: which way it turns
    Pusher, // rotor: it pushes back towards its mount
    TorqueMix, // rotor: it adds what cancels the other rotors' torque (a tail rotor)
    BladeLost, // rotor: the blades gone, a bit each (not on the panel; ConstructRotor.cs)
    Blade0, Blade1, Blade2, Blade3, // rotor: its blades' wear, four blades a setting, 4 bits each (not on the panel; ConstructRotor.Wear)
    Blade4, Blade5, // (no longer used: their numbers stay)
    FanKind, // Fan Former: what it makes
    FanBlades, // Fan Former, rotor: how many blades
    FanDiameter, // Fan Former: how far across, m
    Keys, // Flight Stabilizer: what its keys mean (FlightPattern; ConstructFlight.cs)
    MaxTilt, // Flight Stabilizer: the most it leans, degrees (0: its pattern's)
    MaxTurn, // Flight Stabilizer: the fastest it turns, degrees a second (0: its pattern's)
    MaxClimb, // Flight Stabilizer: the fastest it climbs, m/s (0: its pattern's)
    MaxSpeed, // Flight Stabilizer: the fastest it is asked to go, m/s (0: its pattern's)
    Level, // Flight Stabilizer: it brings itself level
    TrimPitch, // Flight Stabilizer: degrees it counts as level, nose up
    TrimRoll, // Flight Stabilizer: degrees it counts as level, right wing down
    MouseAim, // Flight Stabilizer: it flies where the seat looks
    Autopilot, // Flight Stabilizer: the autopilot is on
    ApMode, // Flight Stabilizer: what the autopilot holds (FlightMode)
    TargetAlt, // Flight Stabilizer: the height held, m (0: where it was when engaged)
    TargetHeading, // Flight Stabilizer: the heading held, degrees (below 0: where it pointed when engaged)
    WaypointX, // Flight Stabilizer: the point it flies to
    WaypointZ,
    HomeX, // Flight Stabilizer: where it took off from (not on the panel)
    HomeZ,
    BladeStyle, // Fan Former, rotor: which blade (Air.json's "bladeStyles")
    InvertPitch, // Flight Stabilizer: W lifts the nose
    Grain, // solid booster: seconds of it burnt (not on the panel; ConstructBoost.cs)
    WingKind, // Airframe Former: what it cuts (a row of Extrusion.json's "airframes")
    WingChord, // Airframe Former: its chord, m
    WingSpan, // Airframe Former: its span, m
    Stabilised, // Turret Module: the seat's look stays on the point it was set on as the craft moves (Game.SandboxStabilise)
    FuseRange, // AA Sight: metres at which its gun's shells burst with nothing locked (0: none)
    PitchRate, PitchResponse, PitchDamping, PitchHold, PitchExpo, // Flight Stabilizer: the pitch loop (0: auto; ConstructFlight.cs)
    RollRate, RollResponse, RollDamping, RollHold, RollExpo, // Flight Stabilizer: the roll loop (0: auto; ConstructFlight.cs)
    YawRate, YawResponse, YawDamping, YawHold, YawExpo, // Flight Stabilizer: the yaw loop (0: auto; ConstructFlight.cs)
    ClimbResponse, ClimbDamping, ClimbHold, ClimbExpo, // Flight Stabilizer: the climb loop (0: auto; ConstructFlight.cs)
    FlightPreset, // Flight Stabilizer: Normal, Docile or Sport: what auto means
    ThrottleCurve, MaxBank, MaxPitchAngle, MaxDescent, TurnCoord, AutoLevel, WindHold, LandingAssist, CollectiveRange, ThrustLimit, // Flight Stabilizer: Advanced (0: auto)
    Throw, // control surface, nozzle: a share of its full deflection (ConstructAero.ThrowOf)
    Expo, // control surface, nozzle: soft around the middle, %
    HomeY, WaypointY, // Flight Stabilizer: on a round world, the rest of the point
    Stations, // retired: the Modular Bomb Rack's station count (no part has it; the Ordnance Rack's RackRows and RackColumns took over)
    RackLayout, // Ordnance Rack: grid or drum (its shape follows: ModularRack.cs)
    RackSize, // retired: the Modular Bomb Rack's light, heavy or giant (RackLength and RackDiameter took over)
    FireOrder, // bomb rack: one at a time, or a salvo (everything it holds on one press)
    RackInterval, // bomb rack: seconds between drops one at a time (a drum's turn)
    RackRows, // Ordnance Rack: a grid's rows fore and aft, a drum's layers along its axis
    RackColumns, // Ordnance Rack: a grid's columns across, a drum's segments round it
    RackLength, // Ordnance Rack: the longest ordnance a station takes, m
    RackDiameter, // Ordnance Rack: the widest ordnance a station takes, m
    ValueY, // Joystick: where the stick stands fore and aft, 0..1 (its X is Value; AnalogLever.cs)
    RestY, // Joystick: where the spring takes its Y (its X's is Rest)
    WalkProfile, // walker leg: Simple, Basic or Advanced (the walker reads its lead leg's: ConstructWalkProfile.cs)
    Stride, StepLift, BodyHeight, // walker leg, Basic: % of the build's own stride, step height and standing height
    Gait, // walker leg, Basic: Auto, Tripod or Wave
    LegPace, LegLift, LegSpread, // walker leg, Advanced: this leg's step speed, lift and foot spread, %
    LegGroup, // walker leg, Advanced: which half of a tripod it steps with (Auto, First, Second)
    DoorOpen, // event door: where it last settled, open or shut (not on the panel; ConstructDoors.cs)
    DoorLocked, // event door: F can't work it (a wire into its Lock port locks it too)
    WalkSpeed, // walker leg, every profile: the top speed at full throttle, % of the legs' own (ConstructWalkProfile.cs)
    LinePick, // Line Console: which of its machines its buttons work on (not on the panel; ProductionLines.cs)
    OrderCount, // Extruder, Wheel Former, Line Console: pieces an order asks for (WorkOrders.cs)
    OrderKind, // Line Console: what its next bill line is (a piece or a wheel)
    OrderMaterial, // Line Console: its next bill line's material (by its option's name)
    LinePage, // Line Console: the line or the bill on its screen (not on the panel)
    TextSize, // any part with a text panel: its text size, % of the part file's (DisplayPanel.cs, ConstructPanels.RowsAt)
    COUNT,
}

/// <summary>One setting a part declares (its file's <c>"config"</c> list; docs/sandbox/CONSTRUCTS.md, "The configure tool").</summary>
public sealed class CConfigDef
{
    public string Key = "";
    public CfgKey Id;
    public CfgType Type;
    public float Min, Max = 1, Default, Step;
    public string Unit = "", Label = "", Group = "", Tooltip = "";
    public string[] Options = []; // a choice's (or key's) names
    public bool Advanced; // on the panel under its Advanced fold
    public bool Hidden; // not on the panel: state kept in the settings, so it rides saves and CBody (a lift cell's holes)
    public bool CreativeOnly; // set only in creative worlds: the server refuses it in survival, the panel shows it read-only, a blueprint's value is left out there
    public bool Bound; // kept in CPart.Setting (the part's legacy choice: the linking tool's R cycles it) rather than CPart.Cfg
    public float[] Presets = []; // the values the linking tool's R cycles a number through (the part's old "settings" list)
    public float Rated; // a rotary joint's speed: its rated speed (deg/s) when the range runs from stopped to twice it, so the panel's slider shows -100% to +100% of it; else 0

    // Text made once at load (the panel draws without allocating)
    public string UiId = "", InputId = "", ResetId = "", CaptureId = "", Format = "", DefaultText = "", TooltipText = "";

    /// <summary>A value held inside what this setting takes: in range and on its step, a whole index, 0/1, a 24-bit colour.</summary>
    public float Clamp(float v)
    {
        if (!float.IsFinite(v))
            return Default;
        switch (Type)
        {
            case CfgType.Toggle:
                return v >= 0.5f ? 1 : 0;
            case CfgType.Choice:
            case CfgType.Key:
                return Math.Clamp(MathF.Round(v), 0, Math.Max(0, Options.Length - 1));
            case CfgType.Colour:
                return Math.Clamp(MathF.Round(v), 0, 0xFFFFFF);
            case CfgType.Mask:
                return Math.Clamp(MathF.Round(v), 0, 0xFFFF);
            default:
                v = Math.Clamp(v, Min, Max);
                if (Step > 0)
                    v = Math.Clamp(Min + MathF.Round((v - Min) / Step) * Step, Min, Max);
                return v;
        }
    }

    /// <summary>The value as the panel and the HUD show it ("2.5 s", "on", "fire", "#ffd080").</summary>
    public string Show(float v) => Type switch
    {
        CfgType.Toggle => v >= 0.5f ? "on" : "off",
        CfgType.Choice or CfgType.Key => Options.Length > 0 ? Options[(int)Math.Clamp(v, 0, Options.Length - 1)] : "",
        CfgType.Colour => "#" + ((int)v).ToString("x6", CultureInfo.InvariantCulture),
        _ => v.ToString(Step >= 1 ? "0" : Step >= 0.1f ? "0.#" : "0.##", CultureInfo.InvariantCulture) + (Unit.Length > 0 ? " " + Unit : ""),
    };
}

/// <summary>A part's setting values (one float a setting slot, <see cref="ConstructConfig.MaxSlots"/>); they move with the part.</summary>
[InlineArray(ConstructConfig.NearSlots)]
public struct CfgNear
{
    private float _v;
}

/// <summary>
/// A part's setting values. The first <see cref="ConstructConfig.NearSlots"/> are on the part; a part with more (only a Flight Stabilizer, for now)
/// keeps the rest in an array it makes when one is set, so every other part pays one reference for them. Copy a part's values with <see cref="Copy"/>.
/// </summary>
public struct CfgValues
{
    private CfgNear _near;
    private float[]? _more;

    public float this[int s]
    {
        readonly get => s < ConstructConfig.NearSlots ? _near[s] : _more != null ? _more[s - ConstructConfig.NearSlots] : 0;
        set
        {
            if (s < ConstructConfig.NearSlots)
            {
                _near[s] = value;
                return;
            }
            _more ??= new float[ConstructConfig.MaxSlots - ConstructConfig.NearSlots];
            _more[s - ConstructConfig.NearSlots] = value;
        }
    }

    public readonly CfgValues Copy()
    {
        var c = this;
        if (_more != null)
            c._more = (float[])_more.Clone();
        return c;
    }
}

/// <summary>
/// Part settings as data (board item dev-sandbox-configure-tool-ct01; docs/sandbox/CONSTRUCTS.md, "The configure tool"):
/// each part file declares a <c>"config"</c> schema (key, type, range, default, unit, label, group, tooltip), so the
/// configure tool's panel is made from the data and a new part gets its menu by itself. The values live on the part
/// (<see cref="CPart.Cfg"/>; a choice the part already had, like a gearbox's gear, stays its <see cref="CPart.Setting"/>),
/// travel in Msg.CBody (only the ones off their default), are changed by the server alone (CActKind.Configure, checked and
/// clamped) and load from blueprint steps (<c>"config": {"period": 2}</c>). The simulation reads them through
/// <see cref="Get(in CPart, ConstructPartDef, CfgKey, float)"/>, a table lookup: nothing allocates.
/// </summary>
public static class ConstructConfig
{
    public const int NearSlots = 19, MaxSlots = 64; // on the part, and in all (19 since the Flight Stabilizer; the rest are CfgValues' array)

    /// <summary>How far away a part can be configured from (metres to the part's centre).</summary>
    public const float Reach = 8f;

    /// <summary>Decision 19: an enemy's construct is defended while its owner is alive this close (metres).</summary>
    public const float ContestRadius = 40f;

    private static readonly string[] KeyNames = MakeKeyNames();

    private static string[] MakeKeyNames()
    {
        var names = new string[(int)CfgKey.COUNT];
        for (var i = 1; i < names.Length; i++)
        {
            var n = ((CfgKey)i).ToString();
            names[i] = char.ToLowerInvariant(n[0]) + n[1..];
        }
        return names;
    }

    public static string KeyName(CfgKey k) => KeyNames[(int)k];

    /// <summary>A setting's value on a part: its slot's, or <paramref name="fallback"/> when the part has no such setting.</summary>
    public static float Get(in CPart p, ConstructPartDef d, CfgKey k, float fallback)
    {
        var s = d.CfgSlot[(int)k];
        if (s < 0)
            return fallback;
        return d.Config[s].Bound ? p.Setting : p.Cfg[s];
    }

    /// <summary>A setting against its default: exactly 1 until it is changed, and 1 for a part without it.</summary>
    public static float Ratio(in CPart p, ConstructPartDef d, CfgKey k)
    {
        var s = d.CfgSlot[(int)k];
        return s < 0 ? 1 : p.Cfg[s] / d.Config[s].Default;
    }

    /// <summary>A setting less its default: exactly 0 until it is changed, and 0 for a part without it.</summary>
    public static float Delta(in CPart p, ConstructPartDef d, CfgKey k)
    {
        var s = d.CfgSlot[(int)k];
        return s < 0 ? 0 : p.Cfg[s] - d.Config[s].Default;
    }

    /// <summary>The value in slot <paramref name="slot"/>.</summary>
    public static float At(in CPart p, ConstructPartDef d, int slot) => d.Config[slot].Bound ? p.Setting : p.Cfg[slot];

    public static bool Has(ConstructPartDef d, CfgKey k) => d.CfgSlot[(int)k] >= 0;

    /// <summary>Sets slot <paramref name="slot"/> (clamped); true when the value changed. A bound slot is the part's Setting.</summary>
    /// <summary>Writes a setting the game itself keeps (a lift cell's holes), with no limits but the file's.</summary>
    public static void Put(ref CPart p, ConstructPartDef d, CfgKey k, float v)
    {
        var slot = d.CfgSlot[(int)k];
        if (slot >= 0)
            p.Cfg[slot] = d.Config[slot].Clamp(v);
    }

    public static bool Set(ref CPart p, ConstructPartDef d, int slot, float v)
    {
        if ((uint)slot >= (uint)d.Config.Length)
            return false;
        var e = d.Config[slot];
        v = e.Clamp(v);
        if (e.Bound)
        {
            var b = (byte)Math.Min(v, Math.Max(0, d.Settings.Length - 1));
            if (b == p.Setting)
                return false;
            p.Setting = b;
            return true;
        }
        if (p.Cfg[slot] == v)
            return false;
        p.Cfg[slot] = v;
        return true;
    }

    /// <summary>A new part's values: every setting at its default; and what a part starts in (a switch that starts on).</summary>
    public static void Init(ref CPart p, ConstructPartDef d)
    {
        p.LeverShown = p.LeverShownY = -1;
        p.Text = p.Text2 = null;
        p.Media = null;
        p.Mem = 0;
        p.Reading = d.Logic == CLogic.Numpad ? LogicInputs.NoEntry : 0;
        for (var s = 0; s < d.Config.Length; s++)
            if (!d.Config[s].Bound)
                p.Cfg[s] = d.Config[s].Default;
        StartState(ref p, d);
    }

    /// <summary>A part's starting state from its settings (a switch or toggle set to start on).</summary>
    public static void StartState(ref CPart p, ConstructPartDef d)
    {
        if (d.Logic is CLogic.Switch or CLogic.Toggle or CLogic.Cargo && Has(d, CfgKey.StartOn))
            p.State = (byte)((p.State & ~1) | (Get(p, d, CfgKey.StartOn, 0) >= 0.5f ? 1 : 0));
    }

    /// <summary>The linking tool's R (the old setting cycle) moved the part's Setting: a number with presets takes the preset.</summary>
    public static void ApplyPreset(ref CPart p, ConstructPartDef d)
    {
        for (var s = 0; s < d.Config.Length; s++)
            if (d.Config[s].Presets.Length > 0)
                p.Cfg[s] = d.Config[s].Clamp(d.Config[s].Presets[Math.Min(p.Setting, (byte)(d.Config[s].Presets.Length - 1))]);
    }

    /// <summary>Is every value at its default (the part was never configured)?</summary>
    public static bool AllDefault(in CPart p, ConstructPartDef d)
    {
        for (var s = 0; s < d.Config.Length; s++)
            if (d.Config[s].Bound ? p.Setting != d.DefaultSetting : p.Cfg[s] != d.Config[s].Default)
                return false;
        return true;
    }

    /// <summary>A colour value as linear-ish 0..1 RGB.</summary>
    public static System.Numerics.Vector3 Rgb(float packed)
    {
        var v = (int)Math.Clamp(packed, 0, 0xFFFFFF);
        return new System.Numerics.Vector3(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    public static float Pack(System.Numerics.Vector3 rgb) =>
        ((int)MathF.Round(Math.Clamp(rgb.X, 0, 1) * 255) << 16) | ((int)MathF.Round(Math.Clamp(rgb.Y, 0, 1) * 255) << 8) | (int)MathF.Round(Math.Clamp(rgb.Z, 0, 1) * 255);

    // ---- Who may configure --------------------------------------------------------------------------------------------

    /// <summary>
    /// Why a player may not configure this construct, or null (decision 19; a faction mate's counts as your own,
    /// <see cref="Factions.Shares"/>): your own, nobody's (the map's), a wreck or a loose part are fine; someone else's is refused, "defended" while its owner is
    /// alive within <see cref="ContestRadius"/>. <paramref name="ownerName"/> and <paramref name="ownerNear"/> come from the game.
    /// </summary>
    public static string? Refusal(Construct c, byte player, string ownerName, bool ownerNear)
    {
        if (Factions.Shares(c.Owner, player) || c.Owner == 255 || c.Wreck || c.Loose)
            return null;
        return ownerNear ? $"Owned by {ownerName} - defended" : $"Owned by {ownerName}: only its owner can configure it";
    }

    // ---- Parsing (part files) -----------------------------------------------------------------------------------------

    private static readonly string[] EntryKeys = ["key", "type", "min", "max", "default", "step", "unit", "label", "group", "tooltip", "options", "creative", "hidden", "advanced"];
    private static readonly string[] TypeNames = ["number", "choice", "toggle", "colour", "key", "mask"];

    /// <summary>
    /// The part's <c>"config"</c> list (up to <see cref="MaxSlots"/> settings), plus the choice it already had folded in as
    /// a <c>"setting"</c> entry when the file doesn't list one (a reader's key, a gearbox's gear, a suspension's presets...),
    /// and a number with the old <c>"settings"</c> list as its presets (the linking tool's R still cycles them).
    /// </summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        var list = new List<CConfigDef>();
        if (ConstructParts.Opt(root, "config") is { } cfg)
        {
            if (cfg.ValueKind != JsonValueKind.Array)
                throw new ConstructPartException($"{file}: \"config\" lists the part's settings");
            var i = 0;
            foreach (var e in cfg.EnumerateArray())
                list.Add(ParseEntry(d, e, file, $"config[{i++}]"));
        }
        var presetKey = PresetKey(d);
        var bindable = d.Settings.Length > 0 && presetKey == CfgKey.None && !(d.Role == PartRole.Transmission && d.Trans is TransKind.Automatic or TransKind.Cvt || d.Settings.Length < 2 && d.Logic != CLogic.Reader);
        foreach (var e in list)
        {
            if (e.Id == CfgKey.Setting)
            {
                if (!bindable)
                    throw new ConstructPartException($"{file}: \"config\": a {d.Name} has no choice for a \"setting\" entry");
                Bind(d, e);
            }
            if (e.Id == presetKey && presetKey != CfgKey.None)
            {
                e.Presets = d.Settings;
                if (e.Default == float.MinValue)
                    e.Default = d.Settings[Math.Min(d.DefaultSetting, (byte)(d.Settings.Length - 1))];
            }
        }
        // The old choice, folded in.
        if (bindable && !list.Exists(e => e.Id == CfgKey.Setting))
        {
            var e = new CConfigDef { Key = "setting", Id = CfgKey.Setting, Type = d.Logic == CLogic.Reader ? CfgType.Key : CfgType.Choice, Group = "General" };
            e.Label = d.SettingName.Length > 0 ? char.ToUpperInvariant(d.SettingName[0]) + d.SettingName[1..] : "Setting";
            e.Tooltip = d.Role switch
            {
                PartRole.Transmission => "The gear with no signal wired in.",
                PartRole.Suspension => "",
                PartRole.Differential => "Locked: both wheels turn together.",
                PartRole.Transfer => "",
                _ => d.Logic switch
                {
                    CLogic.Reader => "The seat key it reads.",
                    CLogic.Selector => "",
                    _ => "",
                },
            };
            Bind(d, e);
            list.Insert(0, e);
        }
        if (presetKey != CfgKey.None && !list.Exists(e => e.Id == presetKey))
        {
            // A part file without a "config" entry for it: a number over the kind's whole range, its presets the settings.
            var (lo, hi, unit) = presetKey switch
            {
                CfgKey.Period or CfgKey.Seconds => (0.05f, 60f, "s"),
                CfgKey.Radius => (0.5f, 60f, "m"),
                CfgKey.Factor => (-100f, 100f, ""),
                _ => (-1000f, 1000f, ""),
            };
            lo = MathF.Min(lo, d.Settings.Min());
            hi = MathF.Max(hi, d.Settings.Max());
            var label = d.SettingName.Length > 0 ? char.ToUpperInvariant(d.SettingName[0]) + d.SettingName[1..] : KeyName(presetKey);
            list.Insert(0, new CConfigDef { Key = KeyName(presetKey), Id = presetKey, Type = CfgType.Number, Min = lo, Max = hi, Step = 0.01f, Unit = unit, Label = label, Group = "General",
                Presets = d.Settings, Default = d.Settings[Math.Min(d.DefaultSetting, (byte)(d.Settings.Length - 1))] });
        }
        if (d.Role == PartRole.Wheel)
            AddWheelSettings(d, list);
        if (d.Rotor != null)
            AddRotorSettings(d, list);
        if (d.Door != null)
            ConstructDoors.AddSettings(d, list);
        AddThrowSettings(d, list);
        // Every part that shows text: its size (last, so a part file's own settings keep their slots).
        if (ConstructPanels.HasText(d) && list.Count < MaxSlots && !list.Exists(e => e.Id == CfgKey.TextSize))
            list.Add(new CConfigDef { Key = KeyName(CfgKey.TextSize), Id = CfgKey.TextSize, Type = CfgType.Number, Label = "Text size", Group = "Display", Unit = "%",
                Min = ConstructPanels.MinTextSize, Max = ConstructPanels.MaxTextSize, Step = 25, Default = 100, Tooltip = "How big its screen's text is. Smaller shows more rows and longer lines." });
        Finish(d, list, file);
    }

    /// <summary>A part's list of settings, checked and made ready: its slots, ids and text.</summary>
    private static void Finish(ConstructPartDef d, List<CConfigDef> list, string file)
    {
        if (list.Count > MaxSlots)
            throw new ConstructPartException($"{file}: \"config\" lists at most {MaxSlots} settings");
        for (var a = 0; a < list.Count; a++)
        {
            for (var b = a + 1; b < list.Count; b++)
                if (list[a].Id == list[b].Id)
                    throw new ConstructPartException($"{file}: \"config\" lists \"{list[a].Key}\" twice");
            var e = list[a];
            if (e.Default == float.MinValue)
                e.Default = DerivedDefault(d, e, file);
            if (e.Type == CfgType.Number && (e.Default < e.Min || e.Default > e.Max))
                throw new ConstructPartException($"{file}: \"config\" \"{e.Key}\": the default {e.Default.ToString(CultureInfo.InvariantCulture)} is outside {e.Min.ToString(CultureInfo.InvariantCulture)} to {e.Max.ToString(CultureInfo.InvariantCulture)}");
            e.Default = e.Clamp(e.Default);
            var rated = d.MotorSpeed * 180f / MathF.PI;
            if (e.Type == CfgType.Number && e.Id == CfgKey.Speed && d.Role == PartRole.Joint && d.JointType is not (JointType.Piston or JointType.Slider) && rated > 0
                && e.Min <= 0 && MathF.Abs(e.Max - 2 * rated) < 0.51f)
                e.Rated = rated;
            foreach (var pv in e.Presets)
                if (pv < e.Min || pv > e.Max)
                    throw new ConstructPartException($"{file}: \"config\" \"{e.Key}\": the preset {pv.ToString(CultureInfo.InvariantCulture)} (from \"settings\") is outside its range");
            e.UiId = $"##cfgv{a}";
            e.InputId = $"##cfgi{a}";
            e.ResetId = $"x##cfgr{a}";
            e.CaptureId = $"##cfgk{a}";
            e.Format = e.Type == CfgType.Number ? "%." + (e.Step >= 1 ? 0 : e.Step >= 0.1f || e.Step == 0 && e.Max - e.Min >= 20 ? 1 : 2) + "f" + (e.Unit.Length > 0 ? " " + e.Unit.Replace("%", "%%") : "") : "";
            e.DefaultText = "Default: " + e.Show(e.Default);
            e.TooltipText = (e.Tooltip.Length > 0 ? e.Tooltip + "\n" : "") + (e.Type == CfgType.Number ? $"{e.Show(e.Min)} to {e.Show(e.Max)}. " : "") + e.DefaultText;
        }
        d.Config = list.ToArray();
        d.CfgSlot = new sbyte[(int)CfgKey.COUNT];
        Array.Fill(d.CfgSlot, (sbyte)-1);
        for (var s = 0; s < d.Config.Length; s++)
            d.CfgSlot[(int)d.Config[s].Id] = (sbyte)s;
        CheckFits(d, file);
        if (d.IsSizedBox && (d.CfgSlot[(int)CfgKey.SizeX] < 0 || d.CfgSlot[(int)CfgKey.SizeY] < 0 || d.CfgSlot[(int)CfgKey.SizeZ] < 0 || d.CfgSlot[(int)CfgKey.AtX] < 0 || d.CfgSlot[(int)CfgKey.AtY] < 0 || d.CfgSlot[(int)CfgKey.AtZ] < 0))
            throw new ConstructPartException($"{file}: a storage volume needs the six box settings in \"config\" (sizeX, sizeY, sizeZ, atX, atY, atZ)");
    }

    /// <summary>A wheel made at run time (a Wheel Former's): the same tyre settings as a part file's, with no file behind them.</summary>
    public static void ForWheel(ConstructPartDef d)
    {
        var list = new List<CConfigDef>();
        AddWheelSettings(d, list);
        Finish(d, list, d.Name);
    }

    /// <summary>A rotor made at run time (a Fan Former's): the settings of a part file's rotor, with no file behind them.</summary>
    /// <summary>A control surface a machine made: as a part file's "config" would (<see cref="CfgKey.Reverse"/>, <see cref="CfgKey.Trim"/>).</summary>
    public static void ForSurface(ConstructPartDef d)
    {
        var most = d.Surface!.Control * 180f / MathF.PI;
        Finish(d,
        [
            new CConfigDef { Key = KeyName(CfgKey.Reverse), Id = CfgKey.Reverse, Type = CfgType.Toggle, Label = "Reverse", Group = "Surface", Tooltip = "It turns the other way for the same signal: a pair of ailerons, one each way." },
            new CConfigDef { Key = KeyName(CfgKey.Trim), Id = CfgKey.Trim, Type = CfgType.Number, Label = "Trim", Group = "Surface", Unit = "deg", Min = -most, Max = most, Step = 0.5f, Tooltip = "Where it sits with no signal." },
        ], d.Name);
    }

    public static void ForRotor(ConstructPartDef d)
    {
        var list = new List<CConfigDef>();
        AddRotorSettings(d, list);
        AddThrowSettings(d, list);
        Finish(d, list, d.Name);
    }

    /// <summary>Every rotor has these, and its blades' health in hidden settings (they ride saves and CBody).</summary>
    /// <summary>Every control surface and nozzle: how far it goes, and how soft around the middle.</summary>
    private static void AddThrowSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        var surface = d.Surface is { Control: > 0 };
        if (!surface && d.ThrustGimbal <= 0)
            return;
        var group = surface ? "Control" : "Nozzle";
        var most = (surface ? d.Surface!.Control : d.ThrustGimbal) * 180f / MathF.PI;
        if (!list.Exists(e => e.Id == CfgKey.Throw))
            list.Add(new CConfigDef { Key = KeyName(CfgKey.Throw), Id = CfgKey.Throw, Type = CfgType.Number, Label = "Throw", Group = group, Unit = "%", Min = 10, Max = 300, Step = 5, Default = 100, Tooltip = $"How far it moves: 100 is its full {most:0} degrees. Past that for a weak one, up to {ConstructAero.ThrowMost * 180f / MathF.PI:0} degrees. A stabilizer knows." });
        if (!list.Exists(e => e.Id == CfgKey.Expo))
            list.Add(new CConfigDef { Key = KeyName(CfgKey.Expo), Id = CfgKey.Expo, Type = CfgType.Number, Label = "Expo", Group = group, Unit = "%", Min = 0, Max = 100, Step = 5, Default = 0, Tooltip = "Soft around the middle, the same at the ends." });
        if (!surface && !list.Exists(e => e.Id == CfgKey.Reverse))
            list.Add(new CConfigDef { Key = KeyName(CfgKey.Reverse), Id = CfgKey.Reverse, Type = CfgType.Toggle, Label = "Reverse", Group = group, Tooltip = "It moves the other way for the same signal." });
    }

    private static void AddRotorSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        list.Add(new CConfigDef { Key = KeyName(CfgKey.Hand), Id = CfgKey.Hand, Type = CfgType.Choice, Label = "Turns", Group = "Rotor", Options = ["Right hand", "Left hand"], Tooltip = "Which way it turns. A pair of opposite hands cancels each other's torque." });
        list.Add(new CConfigDef { Key = KeyName(CfgKey.Pusher), Id = CfgKey.Pusher, Type = CfgType.Toggle, Label = "Pusher", Group = "Rotor", Tooltip = "It pushes back towards its mount, not away from it." });
        if (d.Rotor.Heli)
            list.Add(new CConfigDef { Key = KeyName(CfgKey.Reverse), Id = CfgKey.Reverse, Type = CfgType.Toggle, Label = "Reverse collective", Group = "Rotor", Tooltip = "A plus signal takes push off: a tail rotor pushing to the right of the craft, on a Flight Stabilizer's yaw." });
        if (d.Rotor.Heli)
            list.Add(new CConfigDef { Key = KeyName(CfgKey.TorqueMix), Id = CfgKey.TorqueMix, Type = CfgType.Toggle, Label = "Torque mix", Group = "Rotor", Tooltip = "A tail rotor: it adds the collective that cancels the other rotors' torque by itself." });
        list.Add(new CConfigDef { Key = KeyName(CfgKey.BladeLost), Id = CfgKey.BladeLost, Type = CfgType.Mask, Hidden = true, Label = "Blades lost", Group = "Rotor", Tooltip = "Not on the panel." });
        for (var k = 0; k < ConstructRotor.WearSlots; k++)
            list.Add(new CConfigDef { Key = KeyName((CfgKey)((int)CfgKey.Blade0 + k)), Id = (CfgKey)((int)CfgKey.Blade0 + k), Type = CfgType.Mask, Hidden = true, Label = "Blade wear", Group = "Rotor", Tooltip = "Not on the panel." });
        // (a made prop carries these from its Fan Former; in creative any rotor changes them here)
        list.Add(new CConfigDef { Key = KeyName(CfgKey.FanBlades), Id = CfgKey.FanBlades, Type = CfgType.Number, Label = "Blades", Group = "Blades", Min = 2, Max = ConstructRotor.MaxBlades, Step = 1, Default = d.Rotor!.Blades, CreativeOnly = true, Tooltip = "How many blades. More take a little more power and push a little harder, and one lost matters less." });
        list.Add(new CConfigDef { Key = KeyName(CfgKey.BladeStyle), Id = CfgKey.BladeStyle, Type = CfgType.Choice, Label = "Blade style", Group = "Blades", Options = AirTable.Current.BladeStyles.Select(x => x.Label).ToArray(), Default = d.Rotor.Style, CreativeOnly = true, Tooltip = "The blade's shape: a small trade of push, power and top speed." });
    }

    /// <summary>Every wheel, track and tyre has the tyre's settings, each starting at what its part file says; a part file only lists one to change its range or text.</summary>
    private static void AddWheelSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        void Add(CfgKey id, string label, string unit, float min, float max, float def, string tip)
        {
            if (!list.Exists(e => e.Id == id))
                list.Add(new CConfigDef { Key = KeyName(id), Id = id, Type = CfgType.Number, Label = label, Group = "Tyre", Unit = unit, Min = MathF.Min(min, def), Max = MathF.Max(max, def), Default = def, Tooltip = tip });
        }
        Add(CfgKey.Friction, "Dry grip", "g", 0.05f, 4, d.Grip * d.Friction, "The hardest it can drive, brake or corner on hard ground.");
        Add(CfgKey.DirtGrip, "Dirt grip", "x", 0.1f, 3, d.OffroadGrip, "Dry grip times this on terrain.");
        Add(CfgKey.SideGrip, "Side grip", "x", 0.01f, 2, d.SideGrip, "Dry grip times this sideways. Low: it slides.");
        Add(CfgKey.Rolling, "Rolling drag", "%", 0, 50, d.RollResist * 100, "Drag as a share of its load.");
        Add(CfgKey.Bounciness, "Bounciness", "%", 0, 100, 0, "How much of a landing or a knock it gives back.");
    }

    /// <summary>The setting a logic part's old "settings" list now gives presets for.</summary>
    private static CfgKey PresetKey(ConstructPartDef d) => d.Role != PartRole.Logic || d.Settings.Length == 0 ? CfgKey.None : d.Logic switch
    {
        CLogic.Timer => CfgKey.Period,
        CLogic.Delay => CfgKey.Seconds,
        CLogic.Compare => CfgKey.Threshold,
        CLogic.Scale => CfgKey.Factor,
        CLogic.Proximity => CfgKey.Radius,
        _ => CfgKey.None,
    };

    private static void Bind(ConstructPartDef d, CConfigDef e)
    {
        e.Bound = true;
        if (e.Type is not (CfgType.Choice or CfgType.Key))
            e.Type = CfgType.Choice;
        e.Options = d.SettingLabels.Length == d.Settings.Length ? d.SettingLabels : d.Settings.Select((_, i) => (i + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
        e.Default = d.DefaultSetting;
    }

    private static CConfigDef ParseEntry(ConstructPartDef d, JsonElement e, string file, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: {where} is an object (key, type, label...)");
        ConstructParts.Known(e, EntryKeys, $"{file}, {where}");
        var c = new CConfigDef();
        c.Key = ConstructParts.Str(ConstructParts.Need(e, "key", $"{file}, {where}"), file, where + ".key");
        var ki = Array.FindIndex(KeyNames, k => k != null && k.Equals(c.Key, StringComparison.OrdinalIgnoreCase));
        if (ki <= 0)
            throw new ConstructPartException($"{file}: {where}: \"key\" is one of {string.Join(", ", KeyNames.Skip(1))}, not \"{c.Key}\"");
        c.Id = (CfgKey)ki;
        c.Key = KeyNames[ki];
        var type = ConstructParts.Opt(e, "type") is { } t ? ConstructParts.Str(t, file, where + ".type").ToLowerInvariant() : DefaultType(c.Id);
        if (type == "color")
            type = "colour";
        var ti = Array.IndexOf(TypeNames, type);
        if (ti < 0)
            throw new ConstructPartException($"{file}: {where}: \"type\" is one of {string.Join(", ", TypeNames)}, not \"{type}\"");
        c.Type = (CfgType)ti;
        c.Label = ConstructParts.Opt(e, "label") is { } l ? Text(l, file, where + ".label", 32) : char.ToUpperInvariant(c.Key[0]) + c.Key[1..];
        c.Group = ConstructParts.Opt(e, "group") is { } g ? Text(g, file, where + ".group", 24) : "General";
        c.Tooltip = ConstructParts.Opt(e, "tooltip") is { } tt ? Text(tt, file, where + ".tooltip", 200) : "";
        c.Unit = ConstructParts.Opt(e, "unit") is { } u ? Text(u, file, where + ".unit", 8) : "";
        if (ConstructParts.Opt(e, "creative") is { } cr)
            c.CreativeOnly = cr.ValueKind == JsonValueKind.True;
        c.Hidden = ConstructParts.Opt(e, "hidden") is { ValueKind: JsonValueKind.True };
        c.Advanced = ConstructParts.Opt(e, "advanced") is { ValueKind: JsonValueKind.True };
        if (c.Id == CfgKey.Fluid)
            c.Options = ConstructFluid.Options;
        if (c.Id == CfgKey.Material)
            c.Options = ConstructStoreVolume.MaterialOptions;
        if (c.Id == CfgKey.AmmoType)
            c.Options = ConstructAmmo.Types;
        if (ConstructParts.Opt(e, "options") is { } opts)
        {
            if (c.Type != CfgType.Choice || opts.ValueKind != JsonValueKind.Array || opts.GetArrayLength() is < 2 or > 16)
                throw new ConstructPartException($"{file}: {where}: \"options\" (2 to 16 names) belong to a choice");
            c.Options = opts.EnumerateArray().Select(o => Text(o, file, where + ".options", 24)).ToArray();
        }
        if (c.Type == CfgType.Choice && c.Options.Length == 0 && c.Id is not (CfgKey.Setting or CfgKey.Recipe)) // (a recipe's options are the Assembling or Refining table: AssemblingTable.Attach, ConstructRefining.Attach)
            throw new ConstructPartException($"{file}: {where}: a choice lists its \"options\"");
        if (c.Type == CfgType.Key && c.Id != CfgKey.Setting)
            throw new ConstructPartException($"{file}: {where}: a key binding is an input reader's \"setting\"");
        if (c.Type == CfgType.Number)
        {
            c.Min = ConstructParts.Num(ConstructParts.Need(e, "min", $"{file}, {where}"), file, where + ".min");
            c.Max = ConstructParts.Num(ConstructParts.Need(e, "max", $"{file}, {where}"), file, where + ".max");
            if (!(c.Max > c.Min))
                throw new ConstructPartException($"{file}: {where}: \"max\" must be more than \"min\"");
            c.Step = ConstructParts.Opt(e, "step") is { } st ? ConstructParts.Num(st, file, where + ".step") : 0;
            if (c.Step < 0 || c.Step > c.Max - c.Min)
                throw new ConstructPartException($"{file}: {where}: \"step\" is 0 (any value) up to the range");
        }
        else if (ConstructParts.Opt(e, "min") != null || ConstructParts.Opt(e, "max") != null || ConstructParts.Opt(e, "step") != null)
            throw new ConstructPartException($"{file}: {where}: only a number has \"min\", \"max\" and \"step\"");
        c.Default = float.MinValue; // (derived later when not given)
        if (ConstructParts.Opt(e, "default") is { } dv)
        {
            c.Default = c.Type switch
            {
                CfgType.Toggle => dv.ValueKind == JsonValueKind.True ? 1 : dv.ValueKind == JsonValueKind.False ? 0 : throw new ConstructPartException($"{file}: {where}: a toggle's \"default\" is true or false"),
                CfgType.Colour => ConstructConfig.Pack(ConstructParts.Colour(dv, file, where + ".default")),
                CfgType.Choice when dv.ValueKind == JsonValueKind.String => Array.IndexOf(c.Options, dv.GetString()) is var oi and >= 0 ? oi : throw new ConstructPartException($"{file}: {where}: the \"default\" is one of its options"),
                _ => ConstructParts.Num(dv, file, where + ".default"),
            };
            if (c.Type == CfgType.Choice && c.Id is not (CfgKey.Setting or CfgKey.Recipe) && (c.Default < 0 || c.Default >= c.Options.Length || c.Default != MathF.Floor(c.Default)))
                throw new ConstructPartException($"{file}: {where}: a choice's \"default\" is one of its options (or its index)");
        }
        if (c.Id == CfgKey.Setting && ConstructParts.Opt(e, "default") != null)
            throw new ConstructPartException($"{file}: {where}: the \"setting\" choice starts at the part's own starting setting (no \"default\")");
        return c;
    }

    private static string DefaultType(CfgKey k) => k switch
    {
        CfgKey.StartOn or CfgKey.Latched or CfgKey.Grip or CfgKey.Invert or CfgKey.Wrap or CfgKey.Pushable or CfgKey.Stabilised => "toggle",
        CfgKey.Profile => "choice",
        CfgKey.RunMode or CfgKey.LinePage or CfgKey.OrderKind or CfgKey.OrderMaterial => "choice",
        CfgKey.SteerKind => "choice",
        CfgKey.MaskA or CfgKey.MaskB or CfgKey.MaskC or CfgKey.MaskD => "mask",
        CfgKey.Colour or CfgKey.Ink => "colour",
        CfgKey.Ramp => "choice",
        CfgKey.Keys or CfgKey.ApMode or CfgKey.Setting or CfgKey.Mode or CfgKey.Operator or CfgKey.Units or CfgKey.Unit or CfgKey.Recipe or CfgKey.MirrorAxis or CfgKey.Fluid or CfgKey.Material => "choice",
        _ => "number",
    };

    private static string Text(JsonElement e, string file, string what, int max)
    {
        var s = ConstructParts.Str(e, file, what);
        if (s.Length == 0 || s.Length > max || s.Any(ch => ch < 32 || ch > 126))
            throw new ConstructPartException($"{file}: \"{what}\" is 1 to {max} plain ASCII characters");
        return s;
    }

    /// <summary>A setting without a "default": what the part did before it had settings (its joint's speed, its spring...).</summary>
    private static float DerivedDefault(ConstructPartDef d, CConfigDef e, string file)
    {
        const float deg = 180f / MathF.PI;
        var unit = d.JointType is JointType.Piston or JointType.Slider ? 1f : deg;
        return e.Id switch
        {
            CfgKey.Min when d.HasJoint && d.JointLimited => d.JointMin * unit,
            CfgKey.Max when d.HasJoint && d.JointLimited => d.JointMax * unit,
            CfgKey.Speed when d.Role == PartRole.Joint => d.MotorSpeed * unit,
            CfgKey.Strength when d.Role == PartRole.Joint => d.MotorStrength,
            CfgKey.Stiffness when d.Role == PartRole.Suspension => d.Frequency,
            CfgKey.Damping when d.Role == PartRole.Suspension => d.Damping,
            CfgKey.Stiffness when d.JointType == JointType.Flex => d.FlexStiffness * MathF.PI / 180f,
            CfgKey.Damping when d.JointType == JointType.Flex => d.FlexDamping,
            _ when e.Type is CfgType.Toggle or CfgType.Choice or CfgType.Key or CfgType.Mask => 0,
            _ when e.Type == CfgType.Colour => 0xFFFFFF,
            _ => throw new ConstructPartException($"{file}: \"config\" \"{e.Key}\" needs a \"default\""),
        };
    }

    /// <summary>The settings a part kind reads (anything else would be a menu that does nothing).</summary>
    private static void CheckFits(ConstructPartDef d, string file)
    {
        if (d.Role == PartRole.FluidTank && (d.CfgSlot[(int)CfgKey.Fluid] < 0 || d.CfgSlot[(int)CfgKey.Fill] < 0))
            throw new ConstructPartException($"{file}: a fluid tank lists its \"fluid\" and \"fill\" settings");
        foreach (var e in d.Config)
        {
            var ok = e.Id switch
            {
                CfgKey.Setting => true,
                CfgKey.Recipe => d.IsAssembler || d.IsLathe || d.IsRefiner,
                CfgKey.LinePick or CfgKey.OrderKind or CfgKey.OrderMaterial or CfgKey.LinePage => ProductionLines.IsConsole(d),
                CfgKey.TextSize => ConstructPanels.HasText(d),
                CfgKey.OrderCount => ProductionLines.IsConsole(d) || WorkOrders.TakesOrders(d),
                CfgKey.Period or CfgKey.Duty => d.Logic == CLogic.Timer,
                CfgKey.StartOn => d.Logic is CLogic.Timer or CLogic.Switch or CLogic.Toggle or CLogic.Cargo || d.IsSpawner,
                CfgKey.SizeX or CfgKey.SizeY or CfgKey.SizeZ or CfgKey.AtX or CfgKey.AtY or CfgKey.AtZ => ConstructCargo.HasBox(d),
                CfgKey.Hole => d.IsEnvelope,
                CfgKey.Reverse => d.Surface is { Control: > 0 } || d.Rotor != null || d.ThrustGimbal > 0,
                CfgKey.Trim => d.Surface is { Control: > 0 },
                CfgKey.Hand or CfgKey.Pusher or CfgKey.TorqueMix or CfgKey.BladeLost or (>= CfgKey.Blade0 and <= CfgKey.Blade3) => d.Rotor != null,
                CfgKey.BladeStyle => d.Rotor != null || d.IsFanFormer,
                CfgKey.FanBlades => d.Rotor != null || d.IsFanFormer,
                CfgKey.FanKind or CfgKey.FanDiameter => d.IsFanFormer,
                CfgKey.WingKind or CfgKey.WingChord or CfgKey.WingSpan => d.IsAirframeFormer,
                (>= CfgKey.Keys and <= CfgKey.HomeZ) or CfgKey.InvertPitch or (>= CfgKey.PitchRate and <= CfgKey.ThrustLimit) or CfgKey.HomeY or CfgKey.WaypointY => d.Logic == CLogic.FlightStabilizer,
                CfgKey.Throw or CfgKey.Expo => d.Surface is { Control: > 0 } || d.ThrustGimbal > 0,
                CfgKey.Grain => d.ThrustKind == ThrustKind.Solid,
                CfgKey.Stabilised => d.Logic == CLogic.TurretModule,
                CfgKey.FuseRange => d.Sight == SightKind.Lead,
                CfgKey.Mode => d.Logic == CLogic.Delay,
                CfgKey.Seconds => d.Logic is CLogic.Delay or CLogic.Filter,
                CfgKey.Grip => d.Role == PartRole.Panel || d.Walk != WalkKind.None,
                CfgKey.Profile or CfgKey.Width or CfgKey.Height or CfgKey.Wall or CfgKey.CutLength => d.IsExtruder || ProductionLines.IsConsole(d), // (a console's: its next bill line)
                CfgKey.MaskA or CfgKey.MaskB or CfgKey.MaskC or CfgKey.MaskD => d.IsExtruder,
                CfgKey.RunMode => d.IsWheelFormer,
                CfgKey.AmmoType => d.Role is PartRole.AmmoBox or PartRole.Storage,
                CfgKey.Charge => d.IsReceiverGun,
                CfgKey.SteerKind => d.Diff == DiffKind.Steering,
                CfgKey.JumpTarget => d.Logic == CLogic.JumpDrive,
                CfgKey.MirrorAxis or CfgKey.MirrorOffset => d.Aid == BuildAid.Mirror,
                CfgKey.SetMode => d.RailDrive == RailDriveKind.Electric,
                CfgKey.Rest => d.RailDrive == RailDriveKind.Electric || d.Logic == CLogic.AnalogLever,
                CfgKey.StopLow or CfgKey.StopHigh => d.IsRail,
                CfgKey.Lead or CfgKey.BackDrive => d.RailDrive == RailDriveKind.Shaft,
                CfgKey.Limited => d.JointType == JointType.Swivel,
                CfgKey.Fluid => d.Role == PartRole.FluidTank,
                CfgKey.Fill => d.Role == PartRole.FluidTank || d.IsVolumeStore,
                CfgKey.Material => d.IsVolumeStore,
                CfgKey.Hysteresis => d.Logic == CLogic.Compare,
                CfgKey.Operator => d.Logic is CLogic.Compare or CLogic.MathBlock,
                CfgKey.Threshold => d.Logic is CLogic.Compare or CLogic.Speed,
                CfgKey.Factor => d.Logic == CLogic.Scale,
                CfgKey.Offset => d.Logic is CLogic.Scale or CLogic.MathBlock,
                CfgKey.Low or CfgKey.High => d.Logic is CLogic.Scale or CLogic.AnalogLever or CLogic.Display or CLogic.Instrument or CLogic.Filter or CLogic.RandomPulse,
                CfgKey.Latched => d.Logic is CLogic.Button or CLogic.Switch,
                CfgKey.Pushable => d.Role == PartRole.Hinge,
                CfgKey.DoorOpen or CfgKey.DoorLocked => d.Door != null,
                CfgKey.Hold => d.Logic is CLogic.Button or CLogic.Switch or CLogic.CodeLock or CLogic.DamageSensor,
                CfgKey.Radius => d.Logic is CLogic.Proximity or CLogic.GravityWell or CLogic.RadioTx || d.IsSpawner,
                CfgKey.Rate => d.IsSpawner || d.Logic == CLogic.Filter,
                CfgKey.Group or CfgKey.MaxAlive or CfgKey.Total => d.IsSpawner, // (the spawners: GameSandboxSpawns.cs)
                CfgKey.Team or CfgKey.Difficulty or CfgKey.Loadout => d.Logic == CLogic.BotSpawner,
                CfgKey.Mix => d.Logic == CLogic.ZombieSpawner,
                CfgKey.Units => d.Logic is CLogic.Speed or CLogic.Instrument,
                CfgKey.Snap or CfgKey.Spring => d.Logic == CLogic.AnalogLever,
                CfgKey.ValueY or CfgKey.RestY => d.Logic == CLogic.AnalogLever && d.Stick,
                CfgKey.Value => d.Logic is CLogic.AnalogLever or CLogic.Counter or CLogic.Memory or CLogic.Numpad or CLogic.RandomPulse,
                CfgKey.Code or CfgKey.Invert => d.Logic == CLogic.CodeLock,
                CfgKey.Decimals => d.Logic is CLogic.Numpad or CLogic.Display or CLogic.Instrument,
                CfgKey.Unit => d.Logic is CLogic.Display or CLogic.Instrument,
                CfgKey.Channel => d.Logic is CLogic.RadioTx or CLogic.RadioRx || d.RemoteReceiver || d.RemoteRange > 0 || d.IsCamera || d.CameraScreen || d.ShellBody || d.ShellRange > 0,
                CfgKey.Ink => d.Logic is CLogic.Display or CLogic.Instrument or CLogic.Numpad or CLogic.CodeLock || d.Panels.Length > 0,
                CfgKey.Ramp => d.Logic == CLogic.Display,
                CfgKey.Limit or CfgKey.Wrap => d.Logic == CLogic.Counter,
                CfgKey.OrbitX or CfgKey.OrbitY or CfgKey.OrbitZ or CfgKey.OrbitDist or CfgKey.ZoomMin or CfgKey.ZoomMax or CfgKey.FollowYaw => d.Role == PartRole.Seat,
                CfgKey.Colour or CfgKey.Brightness or CfgKey.Blink => d.Logic == CLogic.Lamp,
                CfgKey.Pitch => d.Logic == CLogic.Horn,
                CfgKey.Volume => d.Logic == CLogic.Horn || d.IsMedia || d.IsSpeaker,
                CfgKey.SpeakerMode or CfgKey.HearRange or CfgKey.Rolloff or CfgKey.Curve or CfgKey.ConeInner or CfgKey.ConeOuter or CfgKey.ConeGain => d.IsSpeaker,
                CfgKey.Loop => d.IsMedia,
                CfgKey.Min or CfgKey.Max => d.HasJoint && (d.JointLimited || d.JointType == JointType.Swivel) && d.JointType != JointType.Steering,
                CfgKey.Speed => d.Role == PartRole.Joint && d.JointType is not (JointType.Steering or JointType.Flex or JointType.Slider or JointType.Swivel),
                CfgKey.Strength => d.Role == PartRole.Joint && d.JointType is not (JointType.Steering or JointType.Flex or JointType.Slider or JointType.Swivel) || d.Logic is CLogic.Brake or CLogic.GravityWell or CLogic.GravityGen or CLogic.FlightStabilizer,
                CfgKey.Friction or CfgKey.DirtGrip or CfgKey.SideGrip or CfgKey.Rolling or CfgKey.Bounciness => d.Role == PartRole.Wheel,
                CfgKey.Stiffness or CfgKey.Damping => d.Role == PartRole.Suspension && d.SuspFrequency.Length == 0 || d.JointType == JointType.Flex,
                CfgKey.RackLayout or CfgKey.RackRows or CfgKey.RackColumns or CfgKey.RackLength or CfgKey.RackDiameter => d.IsModularRack,
                CfgKey.FireOrder or CfgKey.RackInterval => d.Logic == CLogic.BombRack && !d.RackRail,
                CfgKey.WalkProfile or CfgKey.WalkSpeed or CfgKey.Stride or CfgKey.StepLift or CfgKey.BodyHeight or CfgKey.Gait
                    or CfgKey.LegPace or CfgKey.LegLift or CfgKey.LegSpread or CfgKey.LegGroup => d.Role == PartRole.Leg, // (the gait profiles: ConstructWalkProfile.cs)
                _ => false,
            };
            if (!ok)
                throw new ConstructPartException($"{file}: \"config\": a {d.Name} doesn't use a \"{e.Key}\" setting");
            if (e.Id is CfgKey.Min or CfgKey.Max && e.Type == CfgType.Number)
            {
                var unit = d.JointType == JointType.Piston ? 1f : 180f / MathF.PI;
                if (e.Min < d.JointMin * unit - 0.01f || e.Max > d.JointMax * unit + 0.01f)
                    throw new ConstructPartException($"{file}: \"config\" \"{e.Key}\" reaches past the joint's own limits ({d.JointMin * unit:0.##} to {d.JointMax * unit:0.##})");
            }
        }
    }

    // ---- Blueprints --------------------------------------------------------------------------------------------------

    /// <summary>A blueprint step's <c>"config"</c> object (setting key: value) for a part kind; throws a ConstructPartException-like message.</summary>
    public static List<(int Slot, float Value)> ParseValues(ConstructPartDef d, JsonElement obj, string where)
    {
        var list = new List<(int, float)>();
        if (obj.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{where}: \"config\" is an object of settings (key: value)");
        foreach (var p in obj.EnumerateObject())
        {
            var slot = Array.FindIndex(d.Config, e => e.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (slot < 0 && d.Logic is CLogic.Display or CLogic.Instrument && p.Name.ToLowerInvariant() is "decimals" or "unit")
                continue; // bar gauges and Graph Screens lost their numbers: a player's older blueprint still loads
            if (slot < 0)
                throw new ConstructPartException($"{where}: a {d.Name} has no setting \"{p.Name}\" (it has: {(d.Config.Length == 0 ? "none" : string.Join(", ", d.Config.Select(e => e.Key)))})");
            var e = d.Config[slot];
            float v = p.Value.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Number => p.Value.GetSingle(),
                JsonValueKind.String when e.Type == CfgType.Colour => Pack(ConstructParts.Colour(p.Value, where, p.Name)),
                JsonValueKind.String when e.Options.Length > 0 && Array.IndexOf(e.Options, p.Value.GetString()) is var oi and >= 0 => oi,
                _ => throw new ConstructPartException($"{where}: \"{p.Name}\" isn't a value a {d.Name}'s {e.Label.ToLowerInvariant()} takes"),
            };
            if (e.Type == CfgType.Number && (v < e.Min || v > e.Max))
                throw new ConstructPartException($"{where}: \"{p.Name}\" is {v.ToString(CultureInfo.InvariantCulture)}, it must be {e.Min.ToString(CultureInfo.InvariantCulture)} to {e.Max.ToString(CultureInfo.InvariantCulture)}");
            if (e.Id is CfgKey.RackLayout or CfgKey.RackRows or CfgKey.RackColumns or CfgKey.RackLength or CfgKey.RackDiameter && e.Clamp(v) != e.Default)
                throw new ConstructPartException($"{where}: a rack's shape is its part, not a setting: name the part \"{ModularRack.Key(ModularRack.WithSetting(d.Rack, e.Id, v))}\"");
            list.Add((slot, v));
        }
        return list;
    }

    /// <summary>A part's settings as a blueprint step's "config" object (the values off their default), or "" when none are.</summary>
    public static string ToJson(in CPart p, ConstructPartDef d)
    {
        var parts = new List<string>();
        for (var s = 0; s < d.Config.Length; s++)
        {
            var e = d.Config[s];
            var v = At(p, d, s);
            if (v == (e.Bound ? d.DefaultSetting : e.Default))
                continue;
            parts.Add($"\"{e.Key}\": " + e.Type switch
            {
                CfgType.Toggle => v >= 0.5f ? "true" : "false",
                CfgType.Colour => $"\"{e.Show(v)}\"",
                CfgType.Choice or CfgType.Key => $"\"{e.Show(v)}\"",
                _ => v.ToString("R", CultureInfo.InvariantCulture),
            });
        }
        return parts.Count == 0 ? "" : "{ " + string.Join(", ", parts) + " }";
    }

    // ---- Network -----------------------------------------------------------------------------------------------------

    /// <summary>Msg.CBody (protocol 89): each part setting off its default, as (part, slot, value).</summary>
    public static void Write(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U32(0); // (a u32 since protocol 190: 16,000 parts with five settings each would wrap a u16)
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            for (var s = 0; s < d.Config.Length; s++)
                if (!d.Config[s].Bound && p.Cfg[s] != d.Config[s].Default)
                {
                    w.U16((ushort)i).U8((byte)s).F32(p.Cfg[s]);
                    n++;
                }
        }
        w.PatchU32(at, (uint)n);
    }

    public static void Read(ref PacketReader r, Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            Init(ref c.Parts[i], c.Parts[i].D);
        var n = r.U32();
        if (n > (uint)c.PartCount * 256)
            throw new MalformedPacketException($"{n} construct settings on {c.PartCount} parts");
        for (var k = 0u; k < n; k++)
        {
            var part = r.U16();
            var slot = r.U8();
            var v = r.F32();
            if (part >= c.PartCount || slot >= c.Parts[part].D.Config.Length || c.Parts[part].D.Config[slot].Bound || !float.IsFinite(v))
                throw new MalformedPacketException($"a construct setting {slot} on part {part}");
            c.Parts[part].Cfg[slot] = c.Parts[part].D.Config[slot].Clamp(v);
        }
    }
}
