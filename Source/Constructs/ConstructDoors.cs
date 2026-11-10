using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

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
    private const float PlayerHalf = 0.3f; // (a player's column round their feet, for "don't shut on a player")

    // ---- The part file --------------------------------------------------------------------------------------------

    internal static void ParseDef(ConstructPartDef d, JsonElement? block, string file)
    {
        if (block is not { } b)
            return;
        var where = $"{file}, door";
        if (d.Role != PartRole.Door)
            throw new ConstructPartException($"{file}: only a part with \"role\": \"door\" has a \"door\" block");
        ConstructParts.Known(b, ["kind", "axis", "pivot", "angle", "travel", "seconds", "hang"], where);
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
        if (dd.Slide)
        {
            dd.Travel = ConstructParts.Num(ConstructParts.Need(b, "travel", where), file, "door.travel");
            if (dd.Travel is < 0.05f or > 20f)
                throw new ConstructPartException($"{file}: \"door.travel\" is 0.05 to 20 metres");
        }
        else
        {
            var deg = ConstructParts.Num(ConstructParts.Need(b, "angle", where), file, "door.angle");
            if (deg == 0 || MathF.Abs(deg) > 180 || deg % 90 != 0)
                throw new ConstructPartException($"{file}: \"door.angle\" is 90, 180, -90 or -180 degrees (a part turns in quarter turns)");
            dd.Angle = deg * MathF.PI / 180f;
        }
        dd.Seconds = ConstructParts.Opt(b, "seconds") is { } sec ? ConstructParts.Num(sec, file, "door.seconds") : 0.8f;
        if (dd.Seconds is < 0.1f or > 10f)
            throw new ConstructPartException($"{file}: \"door.seconds\" is 0.1 to 10");
        var hang = ConstructParts.Str(ConstructParts.Need(b, "hang", where), file, "door.hang");
        dd.Hang = Array.FindIndex(d.Sockets, s => s.Name == hang);
        if (dd.Hang < 0)
            throw new ConstructPartException($"{file}: \"door.hang\" names one of its sockets");
        d.Door = dd;
        d.DataIn = ["Open", "Toggle", "Lock"]; // (In, In1, In2: high opens and low shuts; a rise toggles; high locks F out)
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

    /// <summary>Where it is going (moving), or where it is.</summary>
    public static bool TargetOpen(Construct c, int part) => IsMoving(c, part) ? (c.Parts[part].State & Target) != 0 : SettledOpen(c, part);

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

    /// <summary>Server: door <paramref name="k"/> heads for open or shut (from wherever it is: a door on its way turns back).</summary>
    public static void Command(ConstructWorld world, Construct c, int k, bool open)
    {
        ref var dr = ref c.Doors[k];
        ref var p = ref c.Parts[dr.Part];
        if (TargetOpen(c, dr.Part) == open)
            return;
        var f = OpenFraction(c, k, byTicks: true);
        Start(c, k, open, f);
        p.Count = (ushort)Math.Max(1, (int)MathF.Round((open ? 1 - f : f) * p.D.Door!.Ticks));
        world.DoorEvents.Add((c.Id, dr.Part, (byte)(open ? 1 : 0))); // (one turned back is turned back on the clients too)
    }

    private static void Start(Construct c, int k, bool open, float from)
    {
        ref var dr = ref c.Doors[k];
        ref var p = ref c.Parts[dr.Part];
        if ((p.State & Moving) == 0)
            c.DoorMovingCount++;
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
        foreach (var c in world.Bodies)
        {
            if (!c.EnsureDoors() || (c.DoorMovingCount | c.DoorWiredCount) == 0)
                continue;
            StepBody(world, c);
        }
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
        if (c.DoorMovingCount == 0)
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
                Command(world, c, k, true); // (a player in the doorway: it opens again)
                continue;
            }
            Settle(c, k, open);
            world.DoorEvents.Add((c.Id, c.Doors[k].Part, (byte)((open ? 1 : 0) | 2)));
        }
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
        }
        if ((p.State & Moving) != 0)
            c.DoorMovingCount--;
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
        if (dd.Slide)
            shift = axis * (open ? dd.Travel : -dd.Travel);
        else
            r = Rot24.Nearest(Quaternion.CreateFromAxisAngle(axis, open ? dd.Angle : -dd.Angle), out _);
    }

    private static void Place(Construct c, int part, byte r, Vector3 pivot, Vector3 shift)
    {
        c.RestBounds(part, out var oldMin, out var oldMax);
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
        var half = Rot24.ApplyHalf(r, (mx - mn) * 0.5f) + new Vector3(PlayerHalf);
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

    /// <summary>A moving door's leaf, drawn: the turn (or slide) from its settled pose to where it is now, in the body frame.</summary>
    public static Matrix4x4 DrawPose(Construct c, int k)
    {
        var part = c.Doors[k].Part;
        ref var p = ref c.Parts[part];
        var dd = p.D.Door!;
        var f = OpenFraction(c, k, byTicks: false) - (SettledOpen(c, part) ? 1 : 0); // (-1..1: from the settled pose)
        var axis = Rot24.Apply(p.Rot, ConstructParts.DirVector(dd.Axis));
        if (dd.Slide)
            return Matrix4x4.CreateTranslation(axis * (dd.Travel * f));
        var pivot = p.Pos + Rot24.Apply(p.Rot, dd.Pivot);
        return Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, dd.Angle * f) * Matrix4x4.CreateTranslation(pivot);
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
        }
    }
}
