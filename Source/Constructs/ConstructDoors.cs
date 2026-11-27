using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A door's <c>"link"</c> block (a landing leg pod's brace and carriage, LegPodParts.cs): a crank turns <see cref="CrankAngle"/> about
/// <see cref="CrankAxis"/> through <see cref="Crank"/>; a rod pinned to it at <see cref="Knee"/> has its other end, <see cref="Pin"/>,
/// running along <see cref="Rail"/>. A swing door with one is the rod; a slide door is what carries the pin; with <see cref="Ride"/> the
/// leaf rides the knee without turning (a leg's foot, kept level). The drawing follows it (<see cref="ConstructDoors.LinkMove"/>): a door
/// that settles by its own quarter turn or slide is refused unless the link's end pose is that one; a held door's drawing keeps it.
/// Points in the part's frame as built (shut).
/// </summary>
public sealed class CDoorLink
{
    public Vector3 Crank, Knee, Pin;
    public byte CrankAxis, Rail;
    public float CrankAngle; // radians
    public float Branch; // which of the two places the pin may be (the one it is at as built)
    public bool Ride; // "ride": true: the leaf moves with the knee and never turns (no pin, no rail)
}

/// <summary>A part file's <c>"door"</c> block: the part is an event door's leaf (ConstructDoors.cs).</summary>
public sealed class CDoorDef
{
    public bool Slide; // "kind": "swing" turns about Axis through Pivot by Angle; "slide" moves along Axis by Travel
    public byte Axis; // a direction (0..5) in the part's frame
    public Vector3 Pivot; // the part's frame
    public float Angle; // radians, a whole number of quarter turns (right hand about Axis)
    public float Travel; // metres
    public float Seconds; // how long it takes to open or shut
    public int Hang = -1; // the socket it hangs by (its other sockets take what rides on the leaf)
    public bool Dock; // "dock": true: a docking hatch, opened and shut by the server as its body docks (ConstructDoors.StepDocks)
    public bool Launch; // "launch": true: a launch hatch, opened by the server when a launch from its own body finds it in the way, shut a while after (ConstructDoors.OpenForLaunch)
    public CDoorLink? Link; // "link": the leaf is one bar of a crank and slider (a landing leg's brace or its carriage), drawn on the way by it
    // "held": true: the open pose needn't be a quarter turn (a landing leg's strut at 45 degrees). Its parts settle by Shift alone (none,
    // or a riding link's knee move: a foot kept level), and while it is open it is drawn at its open pose (Construct.DrawnDoorOf), so
    // the collider, rays and players see the held leaf where it settled, not where it is drawn: keep its solid boxes at its hinge.
    public bool Held;
    public Vector3 Shift; // a held door's settle, the part's frame
    public byte Gear; // "gear": "canopy" (1) or "leg" (2): a landing leg pod's, worked together by a seat's gear key (ConstructDoors.ToggleGear)
    // "spring": a landing leg's foot that gives (Karl, 2026-11-19: suspension on the landing gear). Settled open, its pad stands on a
    // raycast spring (ConstructPhysics.GearFeet) and its solid boxes meet the ground only past the travel (a bump stop); its "oleo" boxes
    // are drawn up by the squash. A held door's only. SpringTravel 0: none.
    public float SpringTravel, SpringHz, SpringDamp;
    // A staged group (a ship design's boarding hatch, Karl 2026-11-27): doors with the same Seq on a body are worked as one, in two
    // stages (SeqStage): opening, the leads (its lid) open first and the followers (its ladder's sections) only once they rest open;
    // shutting, the followers first and the leads once they rest shut (ConstructDoors.Command). 0: none. Made by the design, not the part files.
    public ushort Seq;
    public byte SeqStage;
    public int Ticks => Math.Max(1, (int)MathF.Round(Seconds * World.TickRate));
}

public sealed partial class ConstructPartDef
{
    /// <summary>An event door (docs/sandbox/CONSTRUCTS.md, "Event doors"), or null.</summary>
    public CDoorDef? Door;
}

public sealed partial class ConstructWorld
{
    /// <summary>Server: event doors that started moving (Flags bit 0: towards open) or settled (bit 1), for the clients (CBodyKind.Door).</summary>
    public readonly List<(ushort Body, ushort Part, byte Flags)> DoorEvents = new(64);

    /// <summary>
    /// Every peer: event doors that started moving or settled here (the same flags as <see cref="DoorEvents"/>), for their
    /// sounds (Game.DoorSounds empties it each frame; GameDoorSounds.cs).
    /// </summary>
    public readonly List<(ushort Body, ushort Part, byte Flags)> DoorHeard = new(4097); // (its cap, so a burst never grows it)

    internal int DockDoorTicks; // (ConstructDoors.StepDocks's count to its next look: one a world)
}

/// <summary>
/// Event doors (docs/sandbox/CONSTRUCTS.md, "Event doors"): a door part with a <c>"door"</c> block on a construct's own
/// segment needs no hinge and spends no joint. It sits shut or open as an ordinary part of the body (the collider, rays
/// and players see its settled pose; it is drawn on its own, never in the merged meshes), and changes only on an event: F
/// (<see cref="Use"/>) or an edge on one of its wired ports. Then it moves for a moment: the server counts its ticks, every
/// peer draws it turning (<see cref="DrawPose"/>), and at the end its parts are put at the new pose once (<see cref="Settle"/>:
/// their boxes move in the collider and the part tree, nothing else; no rebuild, no refresh). While it moves its leaf stays
/// solid where it was: an opening door blocks until it is open, a shutting one lets a player through until it is shut, and
/// it doesn't shut on a player (it opens again). An idle door costs nothing a tick. Joint doors (a panel on a hinge, ConstructDoor.cs) work as before.
/// </summary>
public static class ConstructDoors
{
    public const byte Moving = 1, Target = 2; // CPart.State of a door part
    public const byte SeqLeads = 1, SeqFollows = 2; // CDoorDef.SeqStage
    private const float PlayerHalf = 0.3f; // (a player's column round their feet, for "don't shut on a player")

    // ---- The part file --------------------------------------------------------------------------------------------

    internal static void ParseDef(ConstructPartDef d, JsonElement? block, string file)
    {
        if (block is not { } b)
            return;
        var where = $"{file}, door";
        if (d.Role != PartRole.Door)
            throw new ConstructPartException($"{file}: only a part with \"role\": \"door\" has a \"door\" block");
        ConstructParts.Known(b, ["kind", "axis", "pivot", "angle", "travel", "seconds", "hang", "dock", "launch", "link", "held", "gear", "spring"], where);
        var dd = new CDoorDef();
        var kind = ConstructParts.Str(ConstructParts.Need(b, "kind", where), file, "door.kind");
        dd.Slide = kind switch
        {
            "swing" => false,
            "slide" => true,
            _ => throw new ConstructPartException($"{file}: \"door.kind\" is swing or slide"),
        };
        dd.Axis = ConstructParts.DirOf(ConstructParts.Str(ConstructParts.Need(b, "axis", where), file, "door.axis"), file, "door.axis");
        dd.Pivot = ConstructParts.Opt(b, "pivot") is { } pv ? ConstructParts.Vec(pv, file, "door.pivot") : Vector3.Zero;
        dd.Held = ConstructParts.Opt(b, "held") is { } hd && hd.ValueKind == JsonValueKind.True;
        var linked = ConstructParts.Opt(b, "link") != null;
        if (dd.Held && linked)
        {
            // (a held linked leaf goes by its link alone: no travel or angle of its own)
        }
        else if (dd.Slide)
        {
            dd.Travel = ConstructParts.Num(ConstructParts.Need(b, "travel", where), file, "door.travel");
            if (dd.Travel is < 0.05f or > 20f)
                throw new ConstructPartException($"{file}: \"door.travel\" is 0.05 to 20 metres");
        }
        else
        {
            var deg = ConstructParts.Num(ConstructParts.Need(b, "angle", where), file, "door.angle");
            if (deg == 0 || MathF.Abs(deg) > 180 || (!dd.Held && deg % 90 != 0))
                throw new ConstructPartException($"{file}: \"door.angle\" is 90, 180, -90 or -180 degrees (a part turns in quarter turns; a \"held\" door any angle to 180)");
            dd.Angle = deg * MathF.PI / 180f;
        }
        dd.Gear = ConstructParts.Opt(b, "gear") is { } gr ? ConstructParts.Str(gr, file, "door.gear") switch
        {
            "canopy" => (byte)1,
            "leg" => (byte)2,
            _ => throw new ConstructPartException($"{file}: \"door.gear\" is canopy or leg"),
        } : (byte)0;
        if (dd.Held && (dd.Dock || dd.Launch))
            throw new ConstructPartException($"{file}: a \"held\" door is neither a dock nor a launch hatch");
        dd.Seconds = ConstructParts.Opt(b, "seconds") is { } sec ? ConstructParts.Num(sec, file, "door.seconds") : 0.8f;
        if (dd.Seconds is < 0.1f or > 10f)
            throw new ConstructPartException($"{file}: \"door.seconds\" is 0.1 to 10");
        var hang = ConstructParts.Str(ConstructParts.Need(b, "hang", where), file, "door.hang");
        dd.Hang = Array.FindIndex(d.Sockets, s => s.Name == hang);
        if (dd.Hang < 0)
            throw new ConstructPartException($"{file}: \"door.hang\" names one of its sockets");
        dd.Dock = ConstructParts.Opt(b, "dock") is { } dk && dk.ValueKind == JsonValueKind.True;
        dd.Launch = ConstructParts.Opt(b, "launch") is { } ln && ln.ValueKind == JsonValueKind.True;
        if (ConstructParts.Opt(b, "link") is { } lk)
            dd.Link = ParseLink(dd, lk, file);
        if (ConstructParts.Opt(b, "spring") is { } sp)
        {
            var sw = where + ".spring";
            ConstructParts.Known(sp, ["travel", "hz", "damping"], sw);
            if (!dd.Held || dd.Gear != 2)
                throw new ConstructPartException($"{file}: a door.spring is on a \"held\" landing leg (\"gear\": \"leg\")");
            dd.SpringTravel = ConstructParts.Num(ConstructParts.Need(sp, "travel", sw), file, "door.spring.travel");
            dd.SpringHz = ConstructParts.Num(ConstructParts.Need(sp, "hz", sw), file, "door.spring.hz");
            dd.SpringDamp = ConstructParts.Opt(sp, "damping") is { } dp ? ConstructParts.Num(dp, file, "door.spring.damping") : 0.5f;
            if (dd.SpringTravel is < 0.02f or > 0.9f)
                throw new ConstructPartException($"{file}: \"door.spring.travel\" is 0.02 to 0.9 metres (under the ground's depth: ConstructPhysicsContacts.MaxGroundDepth)");
            if (dd.SpringHz is < 0.3f or > 10f || dd.SpringDamp is < 0f or > 2f)
                throw new ConstructPartException($"{file}: \"door.spring.hz\" is 0.3 to 10, \"damping\" 0 to 2");
        }
        d.Door = dd;
        d.DataIn = ["Open", "Toggle", "Lock"]; // (In, In1, In2: high opens and low shuts; a rise toggles; high locks F out)
    }

    private static CDoorLink ParseLink(CDoorDef dd, JsonElement b, string file)
    {
        var where = $"{file}, door.link";
        ConstructParts.Known(b, ["crank", "crankAxis", "crankAngle", "knee", "pin", "rail", "ride"], where);
        var l = new CDoorLink
        {
            Crank = ConstructParts.Vec(ConstructParts.Need(b, "crank", where), file, "door.link.crank"),
            Knee = ConstructParts.Vec(ConstructParts.Need(b, "knee", where), file, "door.link.knee"),
            CrankAxis = ConstructParts.DirOf(ConstructParts.Str(ConstructParts.Need(b, "crankAxis", where), file, "door.link.crankAxis"), file, "door.link.crankAxis"),
            CrankAngle = ConstructParts.Num(ConstructParts.Need(b, "crankAngle", where), file, "door.link.crankAngle") * MathF.PI / 180f,
            Ride = ConstructParts.Opt(b, "ride") is { } rd && rd.ValueKind == JsonValueKind.True,
        };
        if (l.CrankAngle == 0 || MathF.Abs(l.CrankAngle) > MathF.PI + 1e-4f)
            throw new ConstructPartException($"{file}: \"door.link.crankAngle\" is -180 to 180 degrees, not 0");
        if (l.Ride)
        {
            if (!dd.Held)
                throw new ConstructPartException($"{file}: a door.link that rides its knee is on a \"held\" door");
            l.Pin = l.Knee;
            dd.Shift = Vector3.Transform(Vector3.Zero, LinkMove(dd, l, 1f)); // (it settles where its knee went)
            return l;
        }
        l.Pin = ConstructParts.Vec(ConstructParts.Need(b, "pin", where), file, "door.link.pin");
        l.Rail = dd.Slide && ConstructParts.Opt(b, "rail") == null ? dd.Axis : ConstructParts.DirOf(ConstructParts.Str(ConstructParts.Need(b, "rail", where), file, "door.link.rail"), file, "door.link.rail");
        var r = ConstructParts.DirVector(l.Rail);
        l.Branch = Vector3.Dot(l.Pin - l.Knee, r) >= 0 ? 1 : -1;
        if (dd.Held)
            return l; // (it stays where it was built: only its drawing moves)
        // The link has to end where the door settles, or what is drawn jumps at the settle.
        var end = LinkMove(dd, l, 1f);
        var settle = dd.Slide
            ? Matrix4x4.CreateTranslation(ConstructParts.DirVector(dd.Axis) * dd.Travel)
            : Matrix4x4.CreateTranslation(-dd.Pivot) * Matrix4x4.CreateFromAxisAngle(ConstructParts.DirVector(dd.Axis), dd.Angle) * Matrix4x4.CreateTranslation(dd.Pivot);
        foreach (var q in new[] { l.Knee, l.Pin, l.Pin + Vector3.One })
            if (Vector3.Distance(Vector3.Transform(q, end), Vector3.Transform(q, settle)) > 0.03f)
                throw new ConstructPartException($"{file}: door.link ends {Vector3.Distance(Vector3.Transform(q, end), Vector3.Transform(q, settle)):0.###} m from where the door settles: its pivot, angle or travel don't match the link");
        return l;
    }

    /// <summary>
    /// A linked leaf <paramref name="o"/> of the way open (0 as built, 1 open), in its part's frame as built: the crank turned by o of
    /// its angle, the pin where the rod's length puts it on its rail, the rod (a swing door) turned and moved to join them, or the
    /// carriage (a slide door) moved with the pin.
    /// </summary>
    public static Matrix4x4 LinkMove(CDoorDef dd, CDoorLink l, float o)
    {
        var ax = ConstructParts.DirVector(l.CrankAxis);
        var r = ConstructParts.DirVector(l.Rail);
        var knee = l.Crank + Vector3.Transform(l.Knee - l.Crank, Quaternion.CreateFromAxisAngle(ax, l.CrankAngle * o));
        if (l.Ride)
            return Matrix4x4.CreateTranslation(knee - l.Knee);
        var rod = Vector3.Distance(l.Pin, l.Knee);
        var d = l.Pin - knee;
        var dr = Vector3.Dot(d, r);
        var t = -dr + l.Branch * MathF.Sqrt(MathF.Max(0, dr * dr - d.LengthSquared() + rod * rod));
        var pin = l.Pin + r * t;
        if (dd.Slide)
            return Matrix4x4.CreateTranslation(pin - l.Pin);
        // The rod: turned about the crank's axis from (pin - knee) as built to (pin - knee) now, then its knee put on the crank's.
        Vector3 a = Vector3.Normalize(l.Pin - l.Knee), b = Vector3.Normalize(pin - knee);
        var turn = MathF.Atan2(Vector3.Dot(Vector3.Cross(a, b), ax), Vector3.Dot(a, b));
        return Matrix4x4.CreateTranslation(-l.Knee) * Matrix4x4.CreateFromAxisAngle(ax, turn) * Matrix4x4.CreateTranslation(knee);
    }

    /// <summary>Every event door has these: where it settled (hidden: it rides saves, blueprints and CBody) and a lock.</summary>
    internal static void AddSettings(ConstructPartDef d, List<CConfigDef> list)
    {
        if (!list.Exists(e => e.Id == CfgKey.DoorLocked))
            list.Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.DoorLocked), Id = CfgKey.DoorLocked, Type = CfgType.Toggle, Label = "Locked", Group = "Door", Tooltip = "On: F can't open or shut it. Its wires still do. A wire into its Lock port locks it while high." });
        list.Add(new CConfigDef { Key = ConstructConfig.KeyName(CfgKey.DoorOpen), Id = CfgKey.DoorOpen, Type = CfgType.Toggle, Hidden = true, Label = "Open", Group = "Door", Tooltip = "Not on the panel." });
    }

    // ---- State ----------------------------------------------------------------------------------------------------

    public static bool SettledOpen(Construct c, int part) => ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.DoorOpen, 0) >= 0.5f;

    private static void SetSettled(Construct c, int part, bool open)
    {
        ref var p = ref c.Parts[part];
        var s = p.D.CfgSlot[(int)CfgKey.DoorOpen];
        if (s >= 0)
            p.Cfg[s] = open ? 1 : 0;
    }

    public static bool IsMoving(Construct c, int part) => (c.Parts[part].State & Moving) != 0;

    /// <summary>Where it is going (moving, or waiting on its staged group to move first: <see cref="CDoorDef.Seq"/>), or where it is.</summary>
    public static bool TargetOpen(Construct c, int part)
    {
        if (c.DoorPendingCount > 0 && c.DoorOfListed(part) is var k and >= 0 && c.Doors[k].Part == part && c.Doors[k].Pending != 0)
            return c.Doors[k].Pending == 1;
        return MovingTo(c, part);
    }

    /// <summary>Where it is going (moving), or where it is, by its own state alone.</summary>
    private static bool MovingTo(Construct c, int part) => IsMoving(c, part) ? (c.Parts[part].State & Target) != 0 : SettledOpen(c, part);

    public static bool Locked(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        return ConstructConfig.Get(p, p.D, CfgKey.DoorLocked, 0) >= 0.5f || p.InCount2 > 0 && p.In2 >= 0.5f;
    }

    /// <summary>How far open door <paramref name="k"/> is (0 shut, 1 open): on the server by its ticks left, elsewhere by the drawing clock.</summary>
    public static float OpenFraction(Construct c, int k, bool byTicks)
    {
        ref var dr = ref c.Doors[k];
        ref var p = ref c.Parts[dr.Part];
        if ((p.State & Moving) == 0)
            return SettledOpen(c, dr.Part) ? 1 : 0;
        var up = (p.State & Target) != 0;
        if (byTicks)
        {
            var left = (float)p.Count / p.D.Door!.Ticks;
            return Math.Clamp(up ? 1 - left : left, 0, 1);
        }
        var run = (float)((ConstructRender.Now - dr.StartAt) / p.D.Door!.Seconds);
        return Math.Clamp(up ? dr.StartF + run : dr.StartF - run, 0, 1);
    }

    // ---- Events ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// F on part <paramref name="part"/>: null when it isn't an event door's (the caller goes on), "" when it was worked,
    /// else why not. A door part linked straight to another (a double door's leaves) works the other too.
    /// </summary>
    public static string? Use(ConstructWorld world, Construct c, int part)
    {
        if (c.DoorOf(part) is not (var k and >= 0))
            return null;
        var door = c.Doors[k].Part;
        if (c.Parts[door].Broken)
            return "the door is wrecked";
        if (Locked(c, door))
            return "it's locked";
        if (c.Parts[door].D.Door!.Gear != 0)
            return "landing gear: B in a seat of the craft lowers and raises it"; // (worked as one: a single foot raised would drop that corner)
        var open = !TargetOpen(c, door);
        Command(world, c, k, open);
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var ln = ref c.Links[l];
            var other = ln.A == door ? ln.B : ln.B == door ? ln.A : -1;
            if (other >= 0 && c.DoorOf(other) is var m and >= 0 && c.Doors[m].Part == other && !Locked(c, other))
                Command(world, c, m, open);
        }
        return "";
    }

    /// <summary>
    /// Server: door <paramref name="k"/> heads for open or shut (from wherever it is: a door on its way turns back). A door of a staged
    /// group (<see cref="CDoorDef.Seq"/>) works its whole group, in turn.
    /// </summary>
    public static void Command(ConstructWorld world, Construct c, int k, bool open)
    {
        if (c.Parts[c.Doors[k].Part].D.Door!.Seq is var seq and > 0)
            CommandSeq(world, c, seq, open);
        else
            Move(world, c, k, open);
    }

    /// <summary>
    /// A staged group heads for open or shut: the first stage (the leads to open, the followers to shut) moves now; the second moves at once
    /// if the first already rests there, else waits (<see cref="CDoor.Pending"/>) until it does (<see cref="StepPending"/>).
    /// </summary>
    private static void CommandSeq(ConstructWorld world, Construct c, ushort seq, bool open)
    {
        var first = open ? SeqLeads : SeqFollows;
        for (var k = 0; k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door! is { } dd && dd.Seq == seq && dd.SeqStage == first && !c.Parts[c.Doors[k].Part].Broken && !Locked(c, c.Doors[k].Part))
                Move(world, c, k, open);
        var rests = StageRests(c, seq, first, open);
        for (var k = 0; k < c.DoorCount; k++)
        {
            if (c.Parts[c.Doors[k].Part].D.Door! is not { } dd || dd.Seq != seq || dd.SeqStage == first || c.Parts[c.Doors[k].Part].Broken)
                continue;
            if (rests)
                Move(world, c, k, open);
            else
                SetPending(c, k, (sbyte)(open ? 1 : 2));
        }
    }

    /// <summary>Whether every door of stage <paramref name="stage"/> of group <paramref name="seq"/> rests open (or shut), not moving.</summary>
    private static bool StageRests(Construct c, ushort seq, byte stage, bool open)
    {
        for (var k = 0; k < c.DoorCount; k++)
        {
            var part = c.Doors[k].Part;
            if (c.Parts[part].D.Door! is { } dd && dd.Seq == seq && dd.SeqStage == stage && !c.Parts[part].Broken && (IsMoving(c, part) || SettledOpen(c, part) != open))
                return false;
        }
        return true;
    }

    /// <summary>Whether every door of stage <paramref name="stage"/> of group <paramref name="seq"/> is still headed open (or shut).</summary>
    private static bool StageHeads(Construct c, ushort seq, byte stage, bool open)
    {
        for (var k = 0; k < c.DoorCount; k++)
        {
            var part = c.Doors[k].Part;
            if (c.Parts[part].D.Door! is { } dd && dd.Seq == seq && dd.SeqStage == stage && !c.Parts[part].Broken && MovingTo(c, part) != open)
                return false;
        }
        return true;
    }

    private static void SetPending(Construct c, int k, sbyte to)
    {
        ref var dr = ref c.Doors[k];
        if (dr.Pending == 0 && to != 0)
            c.DoorPendingCount++;
        else if (dr.Pending != 0 && to == 0)
            c.DoorPendingCount--;
        dr.Pending = to;
    }

    /// <summary>
    /// Server: staged doors waiting on their group's first stage move once it rests where they are headed; one whose first stage was
    /// turned back (or stopped short: locked, wrecked) waits no more and stays as it is.
    /// </summary>
    private static void StepPending(ConstructWorld world, Construct c)
    {
        for (var k = 0; k < c.DoorCount && c.DoorPendingCount > 0; k++)
        {
            ref var dr = ref c.Doors[k];
            if (dr.Pending == 0)
                continue;
            var open = dr.Pending == 1;
            var dd = c.Parts[dr.Part].D.Door!;
            var first = open ? SeqLeads : SeqFollows;
            if (StageRests(c, dd.Seq, first, open))
            {
                SetPending(c, k, 0);
                Move(world, c, k, open);
            }
            else if (!StageHeads(c, dd.Seq, first, open))
                SetPending(c, k, 0);
        }
    }

    /// <summary>Server: door <paramref name="k"/> alone heads for open or shut.</summary>
    private static void Move(ConstructWorld world, Construct c, int k, bool open)
    {
        ref var dr = ref c.Doors[k];
        ref var p = ref c.Parts[dr.Part];
        if (dr.Pending != 0)
            SetPending(c, k, 0);
        if (MovingTo(c, dr.Part) == open)
            return;
        var f = OpenFraction(c, k, byTicks: true);
        Start(c, k, open, f);
        p.Count = (ushort)Math.Max(1, (int)MathF.Round((open ? 1 - f : f) * p.D.Door!.Ticks));
        world.DoorEvents.Add((c.Id, dr.Part, (byte)(open ? 1 : 0))); // (one turned back is turned back on the clients too)
        world.DoorHeard.Add((c.Id, dr.Part, (byte)(open ? 1 : 0)));
    }

    private static void Start(Construct c, int k, bool open, float from)
    {
        ref var dr = ref c.Doors[k];
        ref var p = ref c.Parts[dr.Part];
        if ((p.State & Moving) == 0)
        {
            c.DoorMovingCount++;
            c.DoorPoseStamp++;
        }
        p.State = (byte)(Moving | (open ? Target : 0));
        dr.StartF = from;
        dr.StartAt = ConstructRender.Now;
    }

    /// <summary>
    /// Server, every tick (ConstructWorld.Step, after the logic): the bodies with a moving or wired door. A body with neither
    /// costs a compare; a wired door costs its three port readings; only a moving door does more.
    /// </summary>
    public static void Step(ConstructWorld world)
    {
        if (world.DoorEvents.Count > 4096)
            world.DoorEvents.Clear(); // (a world nobody flushes: a generator's, a test's)
        if (world.DoorHeard.Count > 4096)
            world.DoorHeard.Clear(); // (a world nobody hears)
        if (++world.DockDoorTicks >= DockEvery)
        {
            world.DockDoorTicks = 0;
            StepDocks(world);
        }
        foreach (var c in world.Bodies)
        {
            if (!c.EnsureDoors())
                continue;
            if (c.LaunchHatchHold > 0 && --c.LaunchHatchHold == 0)
                ShutLaunchHatches(world, c);
            if (c.GearTicks > 0 && --c.GearTicks == 0)
                CommandGear(world, c, c.GearOut ? (byte)2 : (byte)1, c.GearOut); // (the second half of a gear key: the legs after the canopy, or the canopy after the legs)
            if ((c.DoorMovingCount | c.DoorWiredCount | c.DoorPendingCount) == 0)
                continue;
            if (c.GearOut && c.DoorMovingCount > 0)
                GearLift(c);
            StepBody(world, c);
        }
    }

    // ---- Landing gear: the landing leg pods' canopies and legs, worked as one by a seat's gear key ------------------

    /// <summary>0: no landing leg pod on the body; 1: its legs are up (or going up); 2: down (or going down). Every peer (the seat's HUD line).</summary>
    public static int GearState(Construct c)
    {
        if (c.DoorCount == 0 || !c.EnsureDoors())
            return 0;
        var any = false;
        for (var k = 0; k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door!.Gear == 2)
            {
                any = true;
                if (!TargetOpen(c, c.Doors[k].Part))
                    return 1;
            }
        return any ? 2 : 0;
    }

    public static bool HasGear(Construct c) => GearState(c) > 0;

    /// <summary>
    /// Server: the gear key. Lowering, the canopies open at once and the legs follow once they are open; raising, the legs come up
    /// at once and the canopies shut after them (Karl, 2026-11-18: "the canopy opens, and ... the gantry inside ... move[s] the landing
    /// feet out"). The second half waits on <see cref="Construct.GearTicks"/> (a key again turns it round). A refusal's reason, or null.
    /// </summary>
    public static string? ToggleGear(ConstructWorld world, Construct c)
    {
        var state = GearState(c);
        if (state == 0)
            return "this craft has no landing leg pods";
        var lower = state == 1;
        if (!lower && (FeetClear(c, 0f) < GearStandClear || FeetOnDeck(world, c)))
            return "the craft stands on its legs: lift off first";
        c.GearOut = lower;
        var first = lower ? (byte)1 : (byte)2;
        CommandGear(world, c, first, lower);
        var ticks = 0;
        for (var k = 0; k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door is { } dd && dd.Gear == first)
                ticks = Math.Max(ticks, dd.Ticks);
        c.GearTicks = ticks + 3; // (a few ticks' gap, so the second half starts from a settled first)
        return null;
    }

    /// <summary>
    /// A sprung landing foot's give (<see cref="CDoorDef.SpringTravel"/>) if part <paramref name="part"/> is one, settled open and still,
    /// else 0: its solid boxes meet the ground this much deeper (a bump stop at the end of the spring's travel). From the list as built (no build).
    /// </summary>
    public static float FootGive(Construct c, int part)
    {
        if (c.GearFootCount == 0 || c.DoorOfListed(part) is not (var k and >= 0) || c.Doors[k].Part != part)
            return 0;
        ref var p = ref c.Parts[part];
        return p.D.Door!.SpringTravel > 0 && (p.State & Moving) == 0 && SettledOpen(c, part) ? p.D.Door.SpringTravel : 0;
    }

    /// <summary>Do the lowered feet stand on a deck (a station's, a carrier's): within <see cref="GearStandClear"/> of one under any foot.</summary>
    private static bool FeetOnDeck(ConstructWorld world, Construct c)
    {
        var down = -Vector3.Transform(Vector3.UnitY, c.Rot);
        for (var k = 0; k < c.DoorCount; k++)
        {
            var part = c.Doors[k].Part;
            ref var p = ref c.Parts[part];
            if (p.D.Door is not { Gear: 2, Held: true } dd || dd.Shift == Vector3.Zero || !SettledOpen(c, part))
                continue;
            c.PartBounds(part, out var mn, out var mx);
            var sole = c.SegToWorld(p.Seg, new Vector3((mn.X + mx.X) * 0.5f, mn.Y, (mn.Z + mx.Z) * 0.5f));
            var give = dd.SpringTravel;
            if (ConstructPhysics.DeckUnder(world, c, sole - down * (give + 0.5f), down, give + 0.5f + GearStandClear) != null)
                return true;
        }
        return false;
    }

    /// <summary>Feet this close over the ground (m) are stood on: the gear key won't raise them.</summary>
    private const float GearStandClear = 0.3f;

    /// <summary>
    /// The lowest point of a pod's feet (the riding links that slide down: <see cref="CDoorDef.Shift"/>) over the ground under it,
    /// with each foot still settled shut drawn <paramref name="f"/> of its way out (as the drawing moves it). A great height when nothing is under them.
    /// </summary>
    private static float FeetClear(Construct c, float f)
    {
        var clear = float.MaxValue;
        for (var k = 0; k < c.DoorCount; k++)
        {
            var part = c.Doors[k].Part;
            ref var p = ref c.Parts[part];
            if (p.D.Door is not { Gear: 2, Held: true } dd || dd.Shift == Vector3.Zero)
                continue;
            var move = SettledOpen(c, part) || f <= 0 ? Vector3.Zero : Rot24.Apply(p.Rot, Vector3.Transform(Vector3.Zero, OpenMove(dd, f))); // (a ride link turns nothing: its move is the knee's)
            c.PartBounds(part, out var mn, out var mx);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? mx.X : mn.X, (i & 2) != 0 ? mx.Y : mn.Y, (i & 4) != 0 ? mx.Z : mn.Z) + move;
                clear = MathF.Min(clear, ConstructLifters.Altitude(c.SegToWorld(p.Seg, corner), out _));
            }
        }
        return clear;
    }

    /// <summary>
    /// Server, each tick the legs come down: a craft on the ground is lifted as far as its feet, drawn on their way out, would be
    /// under it. A held door's collider only moves when it settles, and a foot that appeared metres under the ground there was past
    /// what the ground's contacts hold (<c>MaxGroundDepth</c>): the craft stayed down with its feet buried (2026-11-18, the Dray).
    /// </summary>
    private static void GearLift(Construct c)
    {
        var f = 0f;
        for (var k = 0; k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door is { Gear: 2, Held: true } dd && dd.Shift != Vector3.Zero && (c.Parts[c.Doors[k].Part].State & Moving) != 0)
                f = MathF.Max(f, OpenFraction(c, k, byTicks: true));
        if (f <= 0 || c.Anchored || c.Frozen)
            return; // (a base held to the ground isn't lifted)
        var under = -FeetClear(c, f);
        if (under <= 0.001f || under > 2f)
            return; // (clear of the ground; or far under it, which isn't a pod's feet on the ground)
        ConstructLifters.Altitude(c.Pos, out var up);
        c.Pos += up * under;
        var vu = Vector3.Dot(c.Vel, up);
        if (vu < 0)
            c.Vel -= up * vu; // (the feet hold it: it doesn't fall back between ticks)
        c.Wake();
    }

    private static void CommandGear(ConstructWorld world, Construct c, byte gear, bool open)
    {
        if (!c.EnsureDoors())
            return;
        for (var k = 0; k < c.DoorCount; k++)
            if (c.Parts[c.Doors[k].Part].D.Door!.Gear == gear)
                Command(world, c, k, open);
    }

    // ---- Launch hatches -------------------------------------------------------------------------------------------

    /// <summary>Ticks a launch hatch the server opened stays open after its body's last launch (then it shuts).</summary>
    public const int LaunchHold = World.TickRate * 4;

    /// <summary>Is part <paramref name="part"/> on a launch hatch (a <c>"door"</c> block with <c>"launch": true</c>) that may be opened: door index, else -1.</summary>
    public static int LaunchHatchOf(Construct c, int part)
    {
        if (c.DoorOf(part) is not (var k and >= 0))
            return -1;
        var door = c.Doors[k].Part;
        return c.Parts[door].D.Door!.Launch && !c.Parts[door].Broken && !Locked(c, door) ? k : -1;
    }

    /// <summary>
    /// Server: a launch from body <paramref name="c"/> found its launch hatch <paramref name="k"/> in the way (the Zero Day's torpedo
    /// ports, Karl 2026-11-18: "a hatch here instead, which opens before the missiles fire"). It opens; the rack looks again soon and
    /// launches once the leaf has swung out of the way. The body's hatches shut <see cref="LaunchHold"/> after its last launch.
    /// </summary>
    public static void OpenForLaunch(ConstructWorld world, Construct c, int k)
    {
        Command(world, c, k, true);
        c.LaunchHatchHold = LaunchHold;
    }

    /// <summary>Server: a launch left body <paramref name="c"/>; hatches it opened for launches stay open a while longer.</summary>
    public static void Launched(Construct c)
    {
        if (c.LaunchHatchHold > 0)
            c.LaunchHatchHold = LaunchHold;
    }

    private static void ShutLaunchHatches(ConstructWorld world, Construct c)
    {
        for (var k = 0; k < c.DoorCount; k++)
        {
            var door = c.Doors[k].Part;
            if (c.Parts[door].D.Door!.Launch && !c.Parts[door].Broken && !Locked(c, door) && TargetOpen(c, door))
                Command(world, c, k, false);
        }
    }

    // ---- Docking hatches ------------------------------------------------------------------------------------------

    private const int DockEvery = 15; // ticks between looks (a quarter second: a hatch is no hurry)
    private const float DockMate = 1.0f; // m: two docking hatches this close (shut, world) are mated

    /// <summary>
    /// Server, every <see cref="DockEvery"/> ticks: docking hatches (a <c>"door"</c> block with <c>"dock": true</c>, the Shuttle's
    /// and the Zero Day's: Karl, 2026-11-17, "a docking hatch which opens when docked"). A craft held by a dock (a Docking Clamp
    /// or a bay: <see cref="Construct.CarriedBy"/> with a <see cref="Construct.CarryTag"/>; a grapple's grip has none) whose
    /// hatch lies within <see cref="DockMate"/> of one on the body holding it opens both; when the dock lets go both shut. Only
    /// a change commands a door, so F still opens or shuts one by hand between. Costs a compare a body without one.
    /// </summary>
    private static void StepDocks(ConstructWorld world)
    {
        foreach (var c in world.Bodies)
        {
            if (!c.EnsureDoors() || c.DoorDockCount == 0)
                continue;
            for (var k = 0; k < c.DoorCount; k++)
                c.Doors[k].MateNow = false;
        }
        foreach (var b in world.Bodies)
        {
            if (b.DoorDockCount == 0 || b.CarriedBy == 0 || b.CarryTag == 0 || world.Get(b.CarriedBy) is not { } c || c.DoorDockCount == 0)
                continue;
            if (b.Dirty || !c.EnsureDoors())
            {
                // (a body waiting for its rebuild can't be looked at: both keep what they had, or an edit or a hit would flap them shut)
                KeepMates(b);
                KeepMates(c);
                continue;
            }
            for (var i = 0; i < b.DoorCount; i++)
            {
                ref var di = ref b.Doors[i];
                if (!b.Parts[di.Part].D.Door!.Dock)
                    continue;
                var wi = b.ToWorld(ShutPos(b, di.Part));
                for (var j = 0; j < c.DoorCount; j++)
                {
                    ref var dj = ref c.Doors[j];
                    if (!c.Parts[dj.Part].D.Door!.Dock || Vector3.DistanceSquared(wi, c.ToWorld(ShutPos(c, dj.Part))) > DockMate * DockMate)
                        continue;
                    di.MateNow = dj.MateNow = true;
                }
            }
        }
        foreach (var c in world.Bodies)
        {
            if (c.DoorDockCount == 0 || c.Dirty)
                continue;
            for (var k = 0; k < c.DoorCount; k++)
            {
                ref var dr = ref c.Doors[k];
                if (!c.Parts[dr.Part].D.Door!.Dock || c.Parts[dr.Part].Broken)
                    continue;
                if (!dr.DockSeen || dr.MateNow != dr.Mated)
                {
                    // (the first look opens a hatch already mated, as a ship built docked; one not mated is left as it settled;
                    // a locked one is left as it is, so a craft caught by a clamp can't open a ship's locked hatch)
                    if ((dr.DockSeen || dr.MateNow) && !Locked(c, dr.Part))
                    {
                        Command(world, c, k, dr.MateNow);
                        dr.ShutDue = !dr.MateNow;
                    }
                    dr.DockSeen = true;
                    dr.Mated = dr.MateNow;
                }
                else if (dr.ShutDue && !IsMoving(c, dr.Part))
                {
                    // (shut on undocking, it opened again for a player in the way: shut it once they are out; done once it has shut)
                    if (SettledOpen(c, dr.Part) && !Locked(c, dr.Part))
                        Command(world, c, k, false);
                    else
                        dr.ShutDue = false;
                }
            }
        }
    }

    /// <summary>A body that can't be looked at this time: its docking hatches keep the mate they had.</summary>
    private static void KeepMates(Construct c)
    {
        for (var k = 0; k < c.DoorCount; k++)
            c.Doors[k].MateNow |= c.Doors[k].Mated;
    }

    /// <summary>Where door part <paramref name="part"/> is when shut, in the body frame (a slid-open hatch's place less its travel).</summary>
    internal static Vector3 ShutPos(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var dd = p.D.Door!;
        var at = !SettledOpen(c, part) ? p.Pos
            : dd.Held ? p.Pos - Rot24.Apply(p.Rot, dd.Shift) : dd.Slide ? p.Pos - Rot24.Apply(p.Rot, ConstructParts.DirVector(dd.Axis)) * dd.Travel : p.Pos;
        return c.ToRest(p.Seg, at); // (a door on a freeform weld sits in its weld's frame)
    }

    private static void StepBody(ConstructWorld world, Construct c)
    {
        if (c.DoorWiredCount > 0)
            for (var k = 0; k < c.DoorCount; k++)
            {
                ref var dr = ref c.Doors[k];
                if (!dr.Wired)
                    continue;
                ref var p = ref c.Parts[dr.Part];
                var openIn = p.InCount > 0 && p.In >= 0.5f;
                var toggleIn = p.InCount1 > 0 && p.In1 >= 0.5f;
                if (!dr.Seen)
                {
                    dr.Seen = true;
                    dr.LastOpen = openIn;
                    dr.LastToggle = toggleIn; // (only a change moves it: a door opened by F, saved and loaded stays open on a low wire)
                    continue;
                }
                if (p.InCount > 0 && openIn != dr.LastOpen)
                    Command(world, c, k, openIn);
                else if (toggleIn && !dr.LastToggle)
                    Command(world, c, k, !TargetOpen(c, dr.Part));
                dr.LastOpen = openIn;
                dr.LastToggle = toggleIn;
                if (c.Dirty)
                    return; // (the body waits for a rebuild: the list is built again next tick)
            }
        if ((c.DoorMovingCount | c.DoorPendingCount) == 0)
            return;
        for (var k = 0; k < c.DoorCount; k++)
        {
            ref var p = ref c.Parts[c.Doors[k].Part];
            if ((p.State & Moving) == 0)
                continue;
            if (p.Count > 1)
            {
                p.Count--;
                continue;
            }
            var open = (p.State & Target) != 0;
            if (!open && SettledOpen(c, c.Doors[k].Part) && OnPlayer(world, c, k))
            {
                Move(world, c, k, true); // (a player in the doorway: it opens again)
                continue;
            }
            Settle(c, k, open);
            world.DoorEvents.Add((c.Id, c.Doors[k].Part, (byte)((open ? 1 : 0) | 2)));
            world.DoorHeard.Add((c.Id, c.Doors[k].Part, (byte)((open ? 1 : 0) | 2)));
        }
        if (c.DoorPendingCount > 0)
            StepPending(world, c); // (a staged group's second stage, once its first rests)
    }

    /// <summary>
    /// Door <paramref name="k"/> comes to rest open or shut (server, and a client on the server's word): its leaf's parts are
    /// put at the new pose (whole quarter turns or a slide, so they stay on the grid), and only their boxes move in the collider
    /// and the part tree (<see cref="Construct.DoorPartMoved"/>): tens of microseconds whatever the body's size, nothing allocated.
    /// The body's mass, inertia and merged meshes stay as built (interiors phase 7 measured a whole-body refresh here: 8 to 10 ms
    /// a door on the 6,300-part Titan Walker). True when it moved.
    /// </summary>
    public static bool Settle(Construct c, int k, bool open)
    {
        ref var dr = ref c.Doors[k];
        var part = dr.Part;
        ref var p = ref c.Parts[part];
        var moved = SettledOpen(c, part) != open;
        if (moved)
        {
            Delta(c, part, open, out var r, out var pivot, out var shift);
            Place(c, part, r, pivot, shift);
            for (var m = 0; m < dr.LeafCount; m++)
                Place(c, c.DoorLeaves[dr.LeafFirst + m], r, pivot, shift);
            SetSettled(c, part, open);
            c.Wake(); // (its collider moved: a parked body standing on a leg pod's foot, or a hatch shut on something, settles again)
        }
        if ((p.State & Moving) != 0)
            c.DoorMovingCount--;
        c.DoorPoseStamp++;
        p.State = 0;
        p.Count = 0;
        dr.StartF = open ? 1 : 0;
        dr.StartAt = ConstructRender.Now;
        return moved;
    }

    /// <summary>From the settled pose to the other one: a quarter-turn turn about a pivot, or a shift (rest frame).</summary>
    private static void Delta(Construct c, int part, bool open, out byte r, out Vector3 pivot, out Vector3 shift)
    {
        ref var p = ref c.Parts[part];
        var dd = p.D.Door!;
        var axis = Rot24.Apply(p.Rot, ConstructParts.DirVector(dd.Axis));
        pivot = p.Pos + Rot24.Apply(p.Rot, dd.Pivot); // (on the axis: the same from either pose)
        r = 0;
        shift = Vector3.Zero;
        if (dd.Held)
            shift = Rot24.Apply(p.Rot, dd.Shift) * (open ? 1 : -1);
        else if (dd.Slide)
            shift = axis * (open ? dd.Travel : -dd.Travel);
        else
            r = Rot24.Nearest(Quaternion.CreateFromAxisAngle(axis, open ? dd.Angle : -dd.Angle), out _);
    }

    private static void Place(Construct c, int part, byte r, Vector3 pivot, Vector3 shift)
    {
        c.ReachBounds(part, out var oldMin, out var oldMax); // (as the part tree holds it)
        c.DoorPartMoving(part);
        ref var q = ref c.Parts[part];
        var oldPos = q.Pos;
        q.Pos = ConstructWorld.Quantise(pivot + Rot24.Apply(r, q.Pos - pivot) + shift);
        q.Rot = Rot24.Compose(r, q.Rot);
        c.DoorPartMoved(part, oldMin, oldMax, oldPos, r);
    }

    /// <summary>Would door <paramref name="k"/>'s part, shut, stand in a living player (their column from the feet up)?</summary>
    private static bool OnPlayer(ConstructWorld world, Construct c, int k)
    {
        var part = c.Doors[k].Part;
        Delta(c, part, open: false, out var r, out var pivot, out var shift);
        c.PartBounds(part, out var mn, out var mx);
        var centre = pivot + Rot24.Apply(r, (mn + mx) * 0.5f - pivot) + shift;
        var half = Rot24.ApplyHalf(r, (mx - mn) * 0.5f);
        ModuleSockets.RestAabb(c, c.Parts[part].Seg, ref centre, ref half); // (a door on a freeform weld: from its weld's frame to the body's)
        half += new Vector3(PlayerHalf);
        var back = Quaternion.Conjugate(c.Rot);
        var up = Vector3.Transform(Vector3.UnitY, back);
        for (var i = 0; i < world.PlayerSpotCount; i++)
        {
            var feet = Vector3.Transform(world.PlayerSpots[i] - c.Pos, back);
            for (var h = 0.2f; h < 1.8f; h += 0.4f)
            {
                var d = Vector3.Abs(feet + up * h - centre);
                if (d.X <= half.X && d.Y <= half.Y && d.Z <= half.Z)
                    return true;
            }
        }
        return false;
    }

    // ---- Drawing --------------------------------------------------------------------------------------------------

    /// <summary>A moving door's leaf (or a held one's, open), drawn: the turn (or slide) from its settled pose to where it is now, in the body frame.</summary>
    public static Matrix4x4 DrawPose(Construct c, int k)
    {
        var part = c.Doors[k].Part;
        ref var p = ref c.Parts[part];
        var dd = p.D.Door!;
        var open = SettledOpen(c, part);
        if (dd.Link != null || dd.Held)
            return LinkPose(dd, p.Rot, p.Pos, OpenFraction(c, k, byTicks: false), open);
        var f = OpenFraction(c, k, byTicks: false) - (open ? 1 : 0); // (-1..1: from the settled pose)
        var axis = Rot24.Apply(p.Rot, ConstructParts.DirVector(dd.Axis));
        if (dd.Slide)
            return Matrix4x4.CreateTranslation(axis * (dd.Travel * f));
        var pivot = p.Pos + Rot24.Apply(p.Rot, dd.Pivot);
        return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, dd.Angle * f) * Matrix4x4.CreateTranslation(pivot);
    }

    /// <summary>
    /// A linked or held door <paramref name="o"/> of the way open (0 shut, 1 open), in the frame its part sits in (at <paramref name="at"/>,
    /// turned <paramref name="rot"/>, as it is now, settled open or not): its open move from as built, then back from the settle it is at
    /// (a held door's: <see cref="CDoorDef.Shift"/>).
    /// </summary>
    public static Matrix4x4 LinkPose(CDoorDef dd, byte rot, Vector3 at, float o, bool settledOpen)
    {
        var place = Matrix4x4.CreateFromQuaternion(Rot24.Quat(rot)) * Matrix4x4.CreateTranslation(at);
        Matrix4x4.Invert(place, out var back);
        var m = OpenMove(dd, o);
        if (settledOpen)
        {
            Matrix4x4.Invert(dd.Held ? Matrix4x4.CreateTranslation(dd.Shift) : OpenMove(dd, 1), out var shut);
            m *= shut; // (a part point to where it is o open, then from the settled frame: v * move * settle^-1 * place)
        }
        return back * m * place;
    }

    /// <summary>A door <paramref name="o"/> of the way open from as built (0 shut, 1 open), in its part's frame: its link, its slide or its turn.</summary>
    public static Matrix4x4 OpenMove(CDoorDef dd, float o)
    {
        if (dd.Link != null)
            return LinkMove(dd, dd.Link, o);
        if (dd.Slide)
            return Matrix4x4.CreateTranslation(ConstructParts.DirVector(dd.Axis) * (dd.Travel * o));
        return Matrix4x4.CreateTranslation(-dd.Pivot) * Matrix4x4.CreateFromAxisAngle(ConstructParts.DirVector(dd.Axis), dd.Angle * o) * Matrix4x4.CreateTranslation(dd.Pivot);
    }

    // ---- The wire --------------------------------------------------------------------------------------------------

    /// <summary>A batch of door events (CBodyKind.Door): u8 count, then u16 body, u16 part, u8 flags each (bit 0: towards open; bit 1: settled). Returns where it got to.</summary>
    public static int Write(PacketWriter w, List<(ushort Body, ushort Part, byte Flags)> events, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Door);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < events.Count && n < 200; i++, n++)
            w.U16(events[i].Body).U16(events[i].Part).U8(events[i].Flags);
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Client: the server's door events. A start is drawn from where this PC has it; a settle puts the parts where the server did.</summary>
    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        for (var j = 0; j < n; j++)
        {
            var id = r.U16();
            var part = r.U16();
            var flags = r.U8();
            if (world.Get(id) is not { } c || c.DoorOf(part) is not (var k and >= 0) || c.Doors[k].Part != part)
                continue; // (not this PC's copy of the door: its next Full puts it right)
            var open = (flags & 1) != 0;
            if ((flags & 2) != 0)
                Settle(c, k, open);
            else if (TargetOpen(c, part) != open)
                Start(c, k, open, OpenFraction(c, k, byTicks: false));
            else
                continue;
            if (world.DoorHeard.Count < 4096)
                world.DoorHeard.Add((id, part, flags));
        }
    }
}
