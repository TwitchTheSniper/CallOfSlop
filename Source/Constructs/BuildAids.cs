namespace CallOfSlop;

/// <summary>What a build station gives (docs/sandbox/CONSTRUCTS.md "Build stations"). Wire bytes: new ones go before COUNT, never renumber.</summary>
public enum BuildAid : byte
{
    None, // an Inventory Beacon: a network, no aid
    Mirror, // Mirror Table: every part placed also lands on the other side of a plane (ConstructMirror.cs)
    Line, // Line Layer: drag a row or a plate field in one stroke (ConstructLine.cs)
    Replace, // Replacer: swap a part for another that fits, keeping welds and links (ConstructReplace.cs)
    Gantry, // Build Gantry: an orbit camera, reach from the station (Program.SandboxGantry.cs)
    Select, // Selector: box select (feat/build-select)
    Blueprint, // Blueprint Desk: save a selection, stamp it (feat/build-select)
    COUNT,
}

/// <summary>The second byte of a <see cref="CActKind.BuildAid"/> action (GameSandboxAids.cs). New ones go at the end.</summary>
public enum BuildAidOp : byte
{
    Mirror, // u8 axis (0 off, 1..3 ConstructMirror.Axes), f32 offset: the plane, on every Mirror Table in reach (creative with none: the player's own)
    Line, // u16 body, u16 part, u16 part id, u8 socket, u8 variant, u8 dir U, f32 pitch U, u16 count U, u8 dir V, f32 pitch V, u16 count V: a row or field (ConstructLine.Spec)
    Replace, // u16 body, u16 part, u16 part id (ConstructReplace.cs)
    Gantry, // u16 body (0: leave): the Build Gantry's camera on that construct, or back to the body
}

/// <summary>
/// The build stations' names and words, and the one check of who needs a station (<see cref="Free"/>). A station is an
/// Inventory Beacon with an aid (its <c>storage.beacon</c> id names the aid): power, Run, wreck, chute network, range and owner
/// are the beacon's rules (ConstructBeacons.cs), and in range it serves its network as a beacon does.
/// </summary>
public static class BuildAids
{
    /// <summary>
    /// THE check (decision: creative needs no stations): do this world's aids work with no station? Creative is for testing
    /// and exploring, so everything is on; the stations can still be built. Change this one line to change the rule; the
    /// server's <see cref="ConstructEdits.AidWhy"/> and the client's <see cref="Game.SandboxAidLive"/> both ask it.
    /// </summary>
    public static bool Free(bool infiniteParts) => infiniteParts;

    private static readonly string[] Ids = ["", "mirror", "line", "replace", "gantry", "select", "blueprint"];
    private static readonly string[] Names = ["", "Mirror", "Line", "Replace", "Gantry", "Select", "Blueprint"];
    private static readonly string[] Stations = ["", "Mirror Table", "Line Layer", "Replacer", "Build Gantry", "Selector", "Blueprint Desk"];
    private static readonly string[] Letters = ["", "M", "L", "R", "G", "S", "B"];

    /// <summary>The aid a beacon id names (None: an Inventory Beacon).</summary>
    public static BuildAid Of(string beaconId)
    {
        var k = Array.IndexOf(Ids, beaconId);
        return k > 0 ? (BuildAid)k : BuildAid.None;
    }

    public static string Name(BuildAid a) => Names[(int)a];

    public static string Station(BuildAid a) => Stations[(int)a];

    /// <summary>The letter on the aid's HUD icon.</summary>
    public static string Letter(BuildAid a) => Letters[(int)a];

    /// <summary>What is wrong with a station, in the tooltip's words ("" when nothing is).</summary>
    public static string Problem(BeaconState s, int range, int distance) => s switch
    {
        BeaconState.OutOfRange => $"out of range ({distance} m away, range {range} m)",
        BeaconState.NoPower => "no power - link a charged battery",
        BeaconState.HeldOff => "its Run wire is off",
        BeaconState.Wrecked => "wrecked",
        BeaconState.NoStores => "no chute network - link a chute to a store",
        BeaconState.NotYours => "someone else's",
        _ => "",
    };

    /// <summary>Why an aid is off for a player. Player-visible: ASCII.</summary>
    public static string Say(BuildAid a, BeaconState s, int range, int distance) =>
        s == BeaconState.None ? $"{Name(a)}: needs a {Station(a)} in range" : SandboxKeys.Line(Station(a), "off", Problem(s, range, distance));

    /// <summary>The look line on a station, on any peer.</summary>
    public static string Look(ConstructPartDef d, BeaconState s, int stores, int range) =>
        s == BeaconState.On
            ? SandboxKeys.Line(d.Name, $"on, {stores} store{(stores == 1 ? "" : "s")}, range {range} m")
            : SandboxKeys.Line(d.Name, "off", Problem(s, range, 0));
}

/// <summary>
/// What one player's aids are doing (the server's copy, or a client's as heard): for each aid its state, its range and how
/// far the nearest is, the mirror plane in force, and whether the gantry holds them. Fixed arrays; one per player who plays Sandbox.
/// </summary>
public sealed class BuildAidView
{
    private const int N = (int)BuildAid.COUNT;

    public readonly BeaconState[] State = new BeaconState[N];
    public readonly ushort[] Range = new ushort[N], Distance = new ushort[N];
    public byte Mirror; // the plane in force: 0 off, else ConstructMirror's axis
    public float MirrorOffset;
    public bool Gantry; // the server holds them in the gantry's camera (false while on: leave it)
    public int Version; // goes up on every change

    // Server only: what ConstructBeacons.AidStates found, and the player's own plane (creative with no table).
    internal readonly BeaconState[] Next = new BeaconState[N];
    internal readonly ushort[] NextRange = new ushort[N], NextDistance = new ushort[N];
    internal bool HasTable, Unsent = true;
    internal byte TableAxis, OwnAxis;
    internal float TableOffset, OwnOffset;
    internal ushort GantryBody; // the construct the gantry looks at

    /// <summary>Server: takes what was found (all aids on when <paramref name="free"/>). Marks it for sending when it changed.</summary>
    public void Commit(bool free)
    {
        var changed = false;
        for (var a = 1; a < N; a++)
        {
            var s = free ? BeaconState.On : Next[a];
            changed |= s != State[a] || NextRange[a] != Range[a] || NextDistance[a] != Distance[a];
            State[a] = s;
            Range[a] = NextRange[a];
            Distance[a] = NextDistance[a];
        }
        var axis = HasTable ? TableAxis : free ? OwnAxis : (byte)0;
        var offset = HasTable ? TableOffset : free ? OwnOffset : 0;
        changed |= axis != Mirror || offset != MirrorOffset;
        Mirror = axis;
        MirrorOffset = offset;
        if (Gantry != (GantryBody != 0))
        {
            Gantry = GantryBody != 0;
            changed = true;
        }
        if (changed)
        {
            Unsent = true;
            Version++;
        }
    }

    /// <summary>A new match, a mode switch: nothing is on.</summary>
    public void Clear()
    {
        Array.Clear(State);
        Array.Clear(Next);
        Array.Clear(Range);
        Array.Clear(Distance);
        Mirror = OwnAxis = TableAxis = 0;
        MirrorOffset = OwnOffset = TableOffset = 0;
        HasTable = Gantry = false;
        GantryBody = 0;
        Version++;
        Unsent = true;
    }

    /// <summary>The whole view, in one small reliable message.</summary>
    public void Write(PacketWriter w)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Aids);
        for (var a = 1; a < N; a++)
            w.U8((byte)State[a]).U16(Range[a]).U16(Distance[a]);
        w.U8(Mirror).F32(MirrorOffset).U8((byte)(Gantry ? 1 : 0));
    }

    public void Read(ref PacketReader r)
    {
        for (var a = 1; a < N; a++)
        {
            var s = r.U8();
            State[a] = s < (byte)BeaconState.COUNT ? (BeaconState)s : BeaconState.None;
            Range[a] = r.U16();
            Distance[a] = r.U16();
        }
        Mirror = r.U8();
        MirrorOffset = r.F32();
        Gantry = r.U8() != 0;
        Version++;
    }
}

/// <summary>One part an aid shows where it would go (ConstructRender; a pose, so nothing here goes stale when parts renumber).</summary>
public struct AidGhost
{
    public ConstructPartDef Def;
    public System.Numerics.Vector3 Pos;
    public byte Rot;
    public bool Ok;
}
