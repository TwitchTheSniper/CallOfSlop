using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A Shell Gantry's numbers (its part file's <c>"shellGantry"</c>; ConstructGantry.cs).</summary>
public sealed class ShellGantryDef
{
    public Vector3 BayMin, BayMax; // the bay in the part's frame: a shell whose feet stand in it is the gantry's (front -z, open)
    public readonly Vector3[] Arms = new Vector3[ConstructGantry.ArmCount]; // each arm's shoulder on the rear frame (ConstructGantry.Arm order)
    public float Reach = 4f; // how far an arm reaches from its shoulder (two links of half this): the parts table must be within it
}

/// <summary>One swap a Shell Gantry is making (server: its own; a client: as heard with <see cref="Msg.Gantry"/>).</summary>
public struct GantryJob
{
    public ushort Body, Part; // the gantry
    public ushort Shell; // the standing shell's construct
    public ushort TableBody, Table; // the parts table: its body and part (0 and ushort.MaxValue: none, in creative)
    public byte Slot; // ShellFrames.Slots
    public byte Old, New; // 1 + the module's index going off and going on
    public byte Arm; // which arm (ConstructGantry.ArmOf)
    public Vector3 PartAt; // server: the gantry part's place on its body, which tells it again after the body's parts are renumbered (ConstructGantry.FindPart)
    public Vector3 TableAt; // server: the parts table's place on its body, likewise (ConstructGantry.FindTable)
    public double Start; // ConstructConveyor.Clock when it began (a client: when it heard it, less the time already run); later than now: queued behind another slot's
}

/// <summary>
/// A Shell Gantry's waist clamp on the shell in its bay (both peers; ConstructGantry.Hold): closed from the first swap's start, open again
/// from when its last swap ended. An entry stays until its opening is drawn, then is reused. The hold itself is the server's: the shell
/// is <see cref="Construct.HeldBy"/> the gantry and posed every tick in the gantry part's frame (Game.GantryPin): shut on it where it
/// stood (or lay), the clamp lifts it upright to <see cref="ConstructGantry.HoldPoint"/> and holds it there, and once the last swap ends
/// it sets it down on its feet before the jaws open. It goes with a gantry aboard a moving ship (riding its frame) and is never frozen.
/// </summary>
public struct GantryLock
{
    public ushort Body, Part, Shell;
    public Vector3 PartAt; // server: the gantry part's place on its body (as GantryJob.PartAt)
    public Vector3 From; // server: the shell's origin in the gantry part's frame where this move began (shut on it, or set down from)
    public Quaternion FromTurn; // ...and its turn there
    public Vector3 At; // server: where the move ends (lifted while held; down on the bay's floor while lowering)
    public Quaternion Turn; // ...and its turn there (upright, facing out of the bay)
    public bool Lowering; // server: its last swap ended and it is setting the shell down (still held until it has)
    public double Since, Until; // when it began to close; when it began to open (-1: still held)
}

/// <summary>
/// The Shell Gantry (docs/sandbox/design/mining-shell.md "Shell Gantry layout"; the user, 2026-11-18): a square bay, 5 m each way, open at
/// the front, where a Robot Shell stands facing out; five arms on its rear frame (left upper and lower, right upper and lower, one over the
/// head) take a module off the shell, set it on the Shell Parts Table behind the bay and fit the one chosen in its place. The table is a
/// part of its own (a Piece Rack by its <c>storage.pieces</c>), on the gantry's construct or standing on its own, within the arms' reach
/// behind the rear frame: that is its link to the gantry, as a lifter serves what is in its reach. A swap is the server's, timed (<see cref="Duration"/>), and
/// every peer hears it (<see cref="Msg.Gantry"/>) and draws the arm on its way (GantryRender.cs). The shell's choice is kept in its
/// part's settings (<see cref="ShellFrames.WriteKit"/>), so it rides saves and the shell wakes with it.
/// </summary>
public static class ConstructGantry
{
    public const int ArmCount = 5;
    public const int MaxJobs = 32;

    // The swap's times (s; docs/sandbox/design/mining-shell.md "Swap animation"): the arm reaches behind the slot, moves in and grips the
    // module, unbolts it, pulls it out (from here the shell is drawn without it), carries it to the table, sets it down, moves over and
    // grips the new one, carries it back, pushes it in, bolts it (the fit: the kit is written, the shell drawn whole again) and goes
    // back to rest.
    public const double TAtShell = 1.2, TGripped = 1.6, TUnbolted = 2.2, TPulled = 2.7, TAtTable = 4.2, TSetDown = 4.5, TSwapped = 4.9,
        TBack = 6.4, TInserted = 6.8, TFitted = 7.4, Duration = 8.6;

    /// <summary>The waist clamp's times (s): it closes on the shell when the first swap begins and opens when the last one ends.</summary>
    public const double LockClose = 0.8, LockOpen = 0.8;

    /// <summary>
    /// The clamp's lift (s): once its jaws are shut it lifts the shell upright off the bay's floor (done by 1.3 s, as the first arm
    /// reaches the shell at <see cref="TAtShell"/>), and when the last swap ends it sets it down on its feet before the jaws open.
    /// </summary>
    public const double LockLift = 0.5;

    /// <summary>How high the clamp holds a shell's feet off the bay's floor (m).</summary>
    public const float LiftHeight = 0.2f;

    /// <summary>Where a held shell's feet are in the gantry part's frame: the bay's middle, lifted <see cref="LiftHeight"/> off its floor (<paramref name="up"/> false: on it).</summary>
    public static Vector3 HoldPoint(ShellGantryDef gd, bool up) =>
        new((gd.BayMin.X + gd.BayMax.X) * 0.5f, gd.BayMin.Y + 0.01f + (up ? LiftHeight : 0), (gd.BayMin.Z + gd.BayMax.Z) * 0.5f);

    /// <summary>
    /// How far the clamp's move is (0: at <see cref="GantryLock.From"/>, 1: at <see cref="GantryLock.At"/>), eased: while held, the lift
    /// after the jaws shut; while lowering, the way down.
    /// </summary>
    public static float Carry(in GantryLock l, double now)
    {
        var s = l.Lowering ? Ramp(now - l.Until, 0, LockLift) : Ramp(now - l.Since, LockClose, LockClose + LockLift);
        return s * s * (3 - 2 * s);
    }

    public const string HeldText = "a Shell Gantry's clamp holds it while its arms work: wait for the swap to end";

    /// <summary>The arm that works on a slot: the upper arms a side's arm (and the right upper the back), the lower arms the legs, the head arm the head.</summary>
    public static byte ArmOf(int slot) => slot switch { 0 => 4, 1 => 2, 2 => 0, 3 => 2, 4 => 1, _ => 3 };

    public static readonly string[] ArmNames = ["left upper", "left lower", "right upper", "right lower", "head"];

    /// <summary>The part files' <c>"shellGantry"</c>: <c>{"bay": [[x, y, z], [x, y, z]], "arms": [5 x [x, y, z]], "reach": m}</c> on a gantry, <c>{"table": true}</c> on its parts table.</summary>
    public static void Parse(ConstructPartDef d, JsonElement root, string file)
    {
        if (ConstructParts.Opt(root, "shellGantry") is not { } g)
            return;
        ConstructParts.Known(g, ["bay", "arms", "reach", "table"], $"{file}, shellGantry");
        if (ConstructParts.Opt(g, "table") is { ValueKind: JsonValueKind.True })
        {
            if (!d.IsPieceRack)
                throw new ConstructPartException($"{file}: a Shell Gantry's parts table (\"shellGantry\": {{\"table\": true}}) holds pieces: it needs storage.pieces");
            d.GantryTable = true;
            return;
        }
        var gd = new ShellGantryDef();
        var bay = ConstructParts.Need(g, "bay", $"{file}, shellGantry");
        if (bay.ValueKind != JsonValueKind.Array || bay.GetArrayLength() != 2)
            throw new ConstructPartException($"{file}: shellGantry.bay is [[x, y, z], [x, y, z]] in metres");
        gd.BayMin = Vec(bay[0], file, "shellGantry.bay");
        gd.BayMax = Vec(bay[1], file, "shellGantry.bay");
        if (gd.BayMax.X - gd.BayMin.X < 1 || gd.BayMax.Y - gd.BayMin.Y < 2 || gd.BayMax.Z - gd.BayMin.Z < 1)
            throw new ConstructPartException($"{file}: shellGantry.bay is at least 1 x 2 x 1 m");
        var arms = ConstructParts.Need(g, "arms", $"{file}, shellGantry");
        if (arms.ValueKind != JsonValueKind.Array || arms.GetArrayLength() != ArmCount)
            throw new ConstructPartException($"{file}: shellGantry.arms lists {ArmCount} shoulders ({string.Join(", ", ArmNames)})");
        for (var k = 0; k < ArmCount; k++)
            gd.Arms[k] = Vec(arms[k], file, "shellGantry.arms");
        if (ConstructParts.Opt(g, "reach") is { } r)
            gd.Reach = ConstructParts.Num(r, file, "shellGantry.reach");
        ConstructParts.Range(gd.Reach, 1, 20, file, "shellGantry.reach");
        d.Gantry = gd;
    }

    private static Vector3 Vec(JsonElement e, string file, string what) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(ConstructParts.Num(e[0], file, what), ConstructParts.Num(e[1], file, what), ConstructParts.Num(e[2], file, what))
            : throw new ConstructPartException($"{file}: \"{what}\" points are [x, y, z] in metres");

    // ---- Where things are -----------------------------------------------------------------------------------------

    /// <summary>A world point in the gantry part's own frame.</summary>
    public static Vector3 ToPart(Construct c, int part, Vector3 world)
    {
        Freeform.PartWorld(c, part, out var pos, out var rot);
        return Vector3.Transform(world - pos, Quaternion.Conjugate(rot));
    }

    /// <summary>The standing shell whose feet are in the gantry's bay (a shell alone on its construct, not wrecked), or null. Walks the bodies: never per tick.</summary>
    public static Construct? ShellIn(ConstructWorld world, Construct c, int part)
    {
        if (c.DefOf(part).Gantry is not { } gd)
            return null;
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var centre = pos + Vector3.Transform((gd.BayMin + gd.BayMax) * 0.5f, rot);
        var r = (gd.BayMax - gd.BayMin).Length() * 0.5f + 1;
        foreach (var b in world.Bodies)
        {
            if (b == c || b.PartCount != 1 || !b.Parts[0].D.ShellBody || Vector3.DistanceSquared(b.WorldCom, centre) > r * r)
                continue;
            if (InBay(c, part, gd, b))
                return b;
        }
        return null;
    }

    /// <summary>Does shell body <paramref name="b"/> stand in the bay (its feet inside it, a little slack above the floor)?</summary>
    public static bool InBay(Construct c, int part, ShellGantryDef gd, Construct b)
    {
        if (b.PartCount != 1 || !b.Parts[0].D.ShellBody || b.Parts[0].Broken)
            return false;
        var feet = ToPart(c, part, b.ToWorld(b.Parts[0].Pos));
        return feet.X >= gd.BayMin.X && feet.X <= gd.BayMax.X && feet.Z >= gd.BayMin.Z && feet.Z <= gd.BayMax.Z && feet.Y >= gd.BayMin.Y - 0.3f && feet.Y <= gd.BayMax.Y - 1;
    }

    /// <summary>
    /// The parts table the gantry's arms reach: a Shell Parts Table behind the rear frame within every arm's reach, on the gantry's own
    /// construct or on another body (a table set down on the ground beside it). -1: none. Walks nearby bodies: never per tick.
    /// </summary>
    public static int TableOf(ConstructWorld world, Construct c, int part, out Construct? table)
    {
        table = null;
        if (c.DefOf(part).Gantry is not { } gd)
            return -1;
        var best = -1;
        var near = float.MaxValue;
        Look(c, part, gd, c, ref best, ref near, ref table);
        Freeform.PartWorld(c, part, out var pos, out var rot);
        var back = pos + Vector3.Transform(new Vector3(0, 2, gd.BayMax.Z + 1), rot);
        var r = gd.Reach + 4;
        foreach (var b in world.Bodies)
            if (b != c && b.PieceRackCount > 0 && Vector3.DistanceSquared(b.WorldCom, back) < (r + b.Radius) * (r + b.Radius))
                Look(c, part, gd, b, ref best, ref near, ref table);
        return best;
    }

    private static void Look(Construct c, int part, ShellGantryDef gd, Construct b, ref int best, ref float near, ref Construct? table)
    {
        for (var k = 0; k < b.PieceRackCount; k++)
        {
            int i = b.PieceRacks[k];
            if (i >= b.PartCount || !b.Parts[i].D.GantryTable || b.Parts[i].Broken)
                continue;
            var at = TablePoint(c, part, b, i);
            if (at.Z < gd.BayMax.Z)
                continue; // (behind the rear frame only)
            var far = 0f;
            for (var a = 0; a < ArmCount; a++)
                far = MathF.Max(far, Vector3.Distance(at, gd.Arms[a]));
            if (far <= gd.Reach && far < near)
                (best, near, table) = (i, far, b);
        }
    }

    /// <summary>Where the arms set a module down on table part <paramref name="table"/> of <paramref name="tb"/>: the middle of its hold's floor, in the gantry part's frame.</summary>
    public static Vector3 TablePoint(Construct c, int part, Construct tb, int table)
    {
        ConstructRackPack.Hold(tb.DefOf(table), out var lo, out var hi);
        Freeform.PartWorld(tb, table, out var tp, out var tr);
        var w = tp + Vector3.Transform(new Vector3((lo.X + hi.X) * 0.5f, lo.Y + 0.15f, (lo.Z + hi.Z) * 0.5f), tr);
        return ToPart(c, part, w);
    }

    /// <summary>A slot's point on a shell standing as part 0 of <paramref name="shell"/>, in the world (the arm's hand takes the module there).</summary>
    public static Vector3 SlotPoint(Construct shell, int slot)
    {
        var local = SlotLocal(shell.Parts[0].D, slot);
        Freeform.PartWorld(shell, 0, out var pos, out var rot);
        return pos + Vector3.Transform(local, rot);
    }

    /// <summary>A slot's point in a shell part's own frame (the frame's scale in).</summary>
    public static Vector3 SlotLocal(ConstructPartDef d, int slot) => slot switch
    {
        0 => new Vector3(0, 1.7f, 0),
        1 => new Vector3(0, 1.25f, 0.18f),
        2 => new Vector3(-0.31f, 1.05f, 0),
        3 => new Vector3(0.31f, 1.05f, 0),
        4 => new Vector3(-0.11f, 0.45f, 0),
        _ => new Vector3(0.11f, 0.45f, 0),
    } * ShellFrames.Of(ShellFrames.FrameOf(d)).Scale;

    /// <summary>How far a slot's boxes reach below its point (negative; 0: none): what a module set down on the table stands on.</summary>
    public static float SlotFoot(ConstructPartDef d, int slot)
    {
        var map = BoxSlots(d);
        var y = SlotLocal(d, slot).Y;
        var foot = 0f;
        for (var b = 0; b < map.Length; b++)
            if (map[b] == slot && !d.Boxes[b].Hidden)
                foot = MathF.Min(foot, d.Boxes[b].Center.Y - d.Boxes[b].Half.Y - y);
        return foot;
    }

    /// <summary>
    /// Gantry part <paramref name="part"/> of <paramref name="c"/> as a job or clamp knows it (its place on the body,
    /// <paramref name="at"/>): its index now (a removal renumbers the parts: ConstructWorld.Compact), or -1 when it is gone, broken
    /// or no longer a gantry. Walks only the body's gantries.
    /// </summary>
    public static int FindPart(Construct c, int part, Vector3 at)
    {
        if (Same(c, part, at, false))
            return part;
        for (var g = 0; g < c.GantryCount; g++)
            if (Same(c, c.Gantries[g], at, false))
                return c.Gantries[g];
        for (var i = 0; i < c.PartCount; i++) // (the gantry list is made with the body's caches, a few ticks late on a big body: then every part; only on a renumber)
            if (Same(c, i, at, false))
                return i;
        return -1;
    }

    /// <summary>A parts table as a job knows it (its place on its body), as <see cref="FindPart"/> finds a gantry: its index now, or -1.</summary>
    public static int FindTable(Construct tb, int table, Vector3 at)
    {
        if (Same(tb, table, at, true))
            return table;
        for (var i = 0; i < tb.PartCount; i++)
            if (Same(tb, i, at, true))
                return i;
        return -1;
    }

    private static bool Same(Construct c, int part, Vector3 at, bool table) =>
        (uint)part < (uint)c.PartCount && !c.Parts[part].Broken && (table ? c.Parts[part].D.GantryTable : c.Parts[part].D.Gantry != null) && c.Parts[part].Pos == at;

    /// <summary>
    /// Both peers, each frame (GantryRender.Draw): a shell in a shut clamp is marked held (<see cref="Construct.GantryHold"/>) though it
    /// came after the swap was heard (a joiner hears the swaps before the bodies) or was streamed out and back. Walks only the clamps.
    /// </summary>
    public static void Refresh(ConstructWorld world)
    {
        for (var k = 0; k < world.GantryLockCount; k++)
            if (world.GantryLocks[k].Until < 0 && world.Get(world.GantryLocks[k].Shell) is { GantryHold: false } s)
                s.GantryHold = true;
    }

    /// <summary>An arm's hand at rest, in the part's frame: hung down and forward of its shoulder.</summary>
    public static Vector3 RestOf(ShellGantryDef gd, int arm) => gd.Arms[arm] + new Vector3(0, -0.55f, -0.6f);

    /// <summary>
    /// Which way a slot's module comes off, in the shell's own frame (a person's metres; times the frame's scale): the head up and back,
    /// the back pack straight back, an arm outwards and a little back, a leg back. The hand waits there before it goes in.
    /// </summary>
    public static Vector3 AwayOf(int slot) => slot switch
    {
        0 => new Vector3(0, 0.22f, 0.2f),
        1 => new Vector3(0, 0, 0.32f),
        2 => new Vector3(-0.3f, 0, 0.1f),
        3 => new Vector3(0.3f, 0, 0.1f),
        _ => new Vector3(0, 0.15f, 0.32f), // (a leg comes up clear of the deck as it comes back)
    };

    /// <summary>Where the arms set an old module down and take the new one from, beside the table's middle (the gantry part's frame).</summary>
    public static Vector3 SetDownOf(Vector3 table) => table + new Vector3(0, 0, -0.25f);
    public static Vector3 PickUpOf(Vector3 table) => table + new Vector3(0, 0, 0.25f);

    /// <summary>
    /// Where a job's hand is at <paramref name="t"/> seconds in, in the gantry part's frame, from the slot's point, the way its module comes
    /// off (<paramref name="away"/>, already in that frame and size) and the table's point; what it carries (0 nothing, 1 the old module,
    /// 2 nothing with the old one on the table, 3 the new one with the old one on the table) and how far its gripper is shut (0 to 1).
    /// Before 0 (queued) and after <see cref="Duration"/> it is at rest.
    /// </summary>
    public static Vector3 Hand(ShellGantryDef gd, int arm, Vector3 slot, Vector3 away, Vector3 table, double t, out int carry, out float grip)
    {
        var rest = RestOf(gd, arm);
        var off = slot + away;
        var down = SetDownOf(table);
        var pick = PickUpOf(table);
        var lift = Vector3.UnitY * 0.15f;
        carry = t < TUnbolted ? 0 : t < TSetDown ? 1 : t < TSwapped ? 2 : t < TFitted ? 3 : 0;
        grip = MathF.Min(Ramp(t, TGripped - 0.2, TGripped), 1 - Ramp(t, TSetDown, TSetDown + 0.15))
            + Ramp(t, TSwapped - 0.2, TSwapped) - Ramp(t, TFitted, TFitted + 0.2);
        grip = Math.Clamp(grip, 0, 1);
        if (t <= 0)
            return rest;
        if (t < TAtShell)
            return Path(rest, off, t / TAtShell);
        if (t < TGripped)
            return Vector3.Lerp(off, slot, Smooth((t - TAtShell) / (TGripped - TAtShell)));
        if (t < TUnbolted)
            return slot;
        if (t < TPulled)
            return Vector3.Lerp(slot, off, Smooth((t - TUnbolted) / (TPulled - TUnbolted)));
        if (t < TAtTable)
            return Path(off, down + lift, (t - TPulled) / (TAtTable - TPulled));
        if (t < TSetDown)
            return Vector3.Lerp(down + lift, down, Smooth((t - TAtTable) / (TSetDown - TAtTable)));
        if (t < TSwapped)
        {
            var s = Smooth((t - TSetDown) / (TSwapped - TSetDown));
            return Vector3.Lerp(down, pick, s) + lift * MathF.Sin(MathF.PI * s);
        }
        if (t < TBack)
            return Path(pick, off, (t - TSwapped) / (TBack - TSwapped));
        if (t < TInserted)
            return Vector3.Lerp(off, slot, Smooth((t - TBack) / (TInserted - TBack)));
        if (t < TFitted)
            return slot;
        const double Out = 0.35; // (straight back out before it swings home)
        if (t < TFitted + Out)
            return Vector3.Lerp(slot, off, Smooth((t - TFitted) / Out));
        return Path(off, rest, Math.Min(1, (t - TFitted - Out) / (Duration - TFitted - Out)));
    }

    /// <summary>Is the arm turning bolts at <paramref name="t"/> (unbolting the old module or bolting the new one)?</summary>
    public static bool Bolting(double t) => t >= TGripped && t < TUnbolted || t >= TInserted && t < TFitted;

    private static float Ramp(double t, double a, double b) => (float)Math.Clamp((t - a) / (b - a), 0, 1);

    private static float Smooth(double s)
    {
        var f = (float)Math.Clamp(s, 0, 1);
        return f * f * (3 - 2 * f);
    }

    /// <summary>A smooth move from a to b, lifted over the middle (the hand clears the frame and the shell's shoulders).</summary>
    private static Vector3 Path(Vector3 a, Vector3 b, double s)
    {
        var f = Smooth(s);
        return Vector3.Lerp(a, b, f) + Vector3.UnitY * (0.45f * MathF.Sin(MathF.PI * f));
    }

    /// <summary>The elbow of an arm from <paramref name="shoulder"/> to <paramref name="hand"/> with two links of half the reach, bent up and back.</summary>
    public static Vector3 Elbow(ShellGantryDef gd, Vector3 shoulder, Vector3 hand)
    {
        var link = gd.Reach * 0.5f;
        var d = hand - shoulder;
        var len = d.Length();
        if (len < 1e-3f)
            return shoulder + Vector3.UnitY * link;
        var mid = shoulder + d * 0.5f;
        var h = len >= 2 * link ? 0 : MathF.Sqrt(link * link - len * len * 0.25f);
        var bend = Vector3.UnitY - d * (Vector3.Dot(Vector3.UnitY, d) / (len * len));
        if (bend.LengthSquared() < 1e-4f)
            bend = Vector3.UnitZ;
        return mid + Vector3.Normalize(bend) * h;
    }

    /// <summary>The job at gantry (<paramref name="body"/>, <paramref name="part"/>), or -1.</summary>
    public static int JobAt(ConstructWorld world, ushort body, int part)
    {
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>The job at gantry (<paramref name="body"/>, <paramref name="part"/>) on slot <paramref name="slot"/>, or -1.</summary>
    public static int JobAt(ConstructWorld world, ushort body, int part, int slot)
    {
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].Part == part && world.GantryJobs[k].Slot == slot)
                return k;
        return -1;
    }

    /// <summary>The job arm <paramref name="arm"/> of a gantry is working at <paramref name="now"/> (the one begun last; -1: it rests).</summary>
    public static int ArmJob(ConstructWorld world, ushort body, int part, int arm, double now)
    {
        var best = -1;
        for (var k = 0; k < world.GantryJobCount; k++)
        {
            ref var j = ref world.GantryJobs[k];
            if (j.Body == body && j.Part == part && j.Arm == arm && j.Start <= now && (best < 0 || j.Start > world.GantryJobs[best].Start))
                best = k;
        }
        return best;
    }

    /// <summary>
    /// When a new swap on <paramref name="arm"/> of a gantry may begin: now, or after the swaps already asked of it (queued, so one
    /// change of several slots runs as a sequence): behind another arm's fit (that arm only goes home after it), behind the same arm's end.
    /// </summary>
    public static double NextStart(ConstructWorld world, ushort body, int part, int arm, double now)
    {
        var at = now;
        for (var k = 0; k < world.GantryJobCount; k++)
        {
            ref var j = ref world.GantryJobs[k];
            if (j.Body == body && j.Part == part)
                at = Math.Max(at, j.Start + (j.Arm == arm ? Duration : TFitted));
        }
        return at;
    }

    /// <summary>The part id of the module a gantry's arm carries off table part <paramref name="table"/> of body <paramref name="body"/> at <paramref name="now"/> (from its grip to the fit; -1: none). Render.</summary>
    public static int CarriedOff(ConstructWorld world, ushort body, int table, double now)
    {
        var all = ShellFrames.Modules;
        for (var k = 0; k < world.GantryJobCount; k++)
        {
            ref var j = ref world.GantryJobs[k];
            if (j.TableBody == body && j.Table == table && now - j.Start is var t && t >= TSwapped && t < TFitted && j.New - 1 is var n and >= 0 && n < all.Length
                && all[n].Part.Length > 0 && ConstructParts.Find(all[n].Part) is { } d)
                return d.Id;
        }
        return -1;
    }

    /// <summary>The slots of shell <paramref name="shell"/> whose module is off at <paramref name="now"/> (a bit a slot): from the pull to the fit. No job: 0.</summary>
    public static int OffSlots(ConstructWorld world, ushort shell, double now)
    {
        var mask = 0;
        for (var k = 0; k < world.GantryJobCount; k++)
        {
            ref var j = ref world.GantryJobs[k];
            if (j.Shell == shell && now - j.Start is var t && t >= TUnbolted && t < TFitted)
                mask |= 1 << j.Slot;
        }
        return mask;
    }

    /// <summary>
    /// Which slot each of a shell part's boxes belongs to (255: the torso's, never taken off), from where the box sits in a person-sized
    /// shell (the part kit's ShellParts.Robot): the head, visor and aerial; the back pack; a forearm and hand; a thigh, knee, shin and
    /// foot. Made once a part type (render only).
    /// </summary>
    public static byte[] BoxSlots(ConstructPartDef d)
    {
        if (d.ShellBoxSlots is { } made && made.Length == d.Boxes.Length)
            return made;
        var scale = MathF.Max(0.1f, ShellFrames.Of(ShellFrames.FrameOf(d)).Scale);
        var map = new byte[d.Boxes.Length];
        for (var b = 0; b < map.Length; b++)
        {
            var c = d.Boxes[b].Center / scale;
            var ax = MathF.Abs(c.X);
            map[b] = c.Y > 1.55f && ax < 0.15f && c.Z < 0.13f ? (byte)0
                : c.Z > 0.13f && ax < 0.2f && c.Y > 1.0f && c.Y < 1.5f ? (byte)1
                : ax > 0.25f && ax < 0.4f && c.Y > 0.6f && c.Y < 1.02f && MathF.Abs(c.Z) < 0.1f ? (byte)(c.X < 0 ? 2 : 3)
                : ax > 0.03f && ax < 0.2f && c.Y < 0.88f && MathF.Abs(c.Z) < 0.15f ? (byte)(c.X < 0 ? 4 : 5)
                : (byte)255;
        }
        d.ShellBoxSlots = map;
        return map;
    }

    // ---- The waist clamp -------------------------------------------------------------------------------------------

    /// <summary>The clamp entry of a gantry (held or still opening), or -1.</summary>
    public static int LockAt(ConstructWorld world, ushort body, int part)
    {
        var opening = -1;
        for (var k = 0; k < world.GantryLockCount; k++)
            if (world.GantryLocks[k].Body == body && world.GantryLocks[k].Part == part)
            {
                if (world.GantryLocks[k].Until < 0)
                    return k; // (a held one first: a renumbered gantry may share an index with another's clamp still opening)
                opening = k;
            }
        return opening;
    }

    /// <summary>Has a gantry's clamp finished opening (its entry free for another)?</summary>
    public static bool LockDone(in GantryLock l, double now) => l.Until >= 0 && now - l.Until > LockLift + LockOpen;

    /// <summary>
    /// The clamp of a gantry closes on <paramref name="shell"/> (both peers, as a swap begins or is heard): an entry made, reused or
    /// closed again if it was opening. Returns its index (-1: no room, nothing drawn) and whether it is new (the server freezes the shell then).
    /// </summary>
    public static int Hold(ConstructWorld world, ushort body, int part, ushort shell, double now, out bool fresh)
    {
        fresh = false;
        var k = LockAt(world, body, part);
        if (k >= 0)
        {
            ref var l = ref world.GantryLocks[k];
            if (l.Until < 0 && l.Shell == shell)
            {
                l.Since = Math.Min(l.Since, now); // (a joiner hearing a swap under way after a queued one: shut already)
                return k;
            }
            if (!LockDone(l, now) && l.Shell == shell)
            {
                var shut = Shut(l, now); // (opening: it closes again from where it is)
                l.Since = now - shut * LockClose;
                l.Until = -1;
                return k;
            }
        }
        else
        {
            for (var i = 0; i < world.GantryLockCount && k < 0; i++)
                if (LockDone(world.GantryLocks[i], now))
                    k = i;
            if (k < 0 && world.GantryLockCount < world.GantryLocks.Length)
                k = world.GantryLockCount++;
            if (k < 0)
                return -1;
        }
        world.GantryLocks[k] = new GantryLock { Body = body, Part = (ushort)part, Shell = shell, Since = now, Until = -1 };
        fresh = true;
        return k;
    }

    /// <summary>Does any swap of this gantry remain (so its clamp stays shut)?</summary>
    public static bool AnyJob(ConstructWorld world, ushort body, int part)
    {
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Body == body && world.GantryJobs[k].Part == part)
                return true;
        return false;
    }

    /// <summary>How shut a clamp is at <paramref name="now"/>: 0 open, 1 shut on the shell.</summary>
    public static float Shut(in GantryLock l, double now)
    {
        var closed = Ramp(now - l.Since, 0, LockClose);
        return l.Until < 0 ? closed : MathF.Min(Ramp(l.Until - l.Since, 0, LockClose), 1 - Ramp(now - l.Until, LockLift, LockLift + LockOpen)); // (shut while it sets the shell down)
    }

    /// <summary>Is this shell in a gantry's hands (it can't be woken or taken while it is)?</summary>
    public static bool Holds(ConstructWorld world, ushort shell)
    {
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Shell == shell)
                return true;
        return false;
    }

    /// <summary>How many of a module's part lie on a table (0: no table, or none).</summary>
    public static int OnTable(ConstructWorld world, Construct? tb, int table, ShellModule m)
    {
        if (tb == null || table < 0 || m.Part.Length == 0 || ConstructParts.Find(m.Part) is not { } d || world.StoreOf(tb, table) is not { } st)
            return 0;
        return ConstructCrafting.Holds(st, (ushort)d.Id);
    }

    /// <summary>The look line: what is in the bay, the table, the work under way.</summary>
    public static string Look(ConstructWorld world, Construct c, int part)
    {
        var shell = ShellIn(world, c, part);
        var table = TableOf(world, c, part, out var tb);
        var j = JobAt(world, c.Id, part);
        var what = j >= 0 && ShellFrames.Modules is { } all && world.GantryJobs[j].New - 1 is var n and >= 0 && n < all.Length
            ? $"fitting a {all[n].Name} to the {ShellFrames.SlotLabel(world.GantryJobs[j].Slot)}"
            : shell != null ? $"{shell.Parts[0].D.Name} in the bay" : "the bay is empty";
        var tl = table >= 0 && tb != null ? $"parts table: {world.StoreOf(tb, table)?.ItemCount ?? 0} parts" : "no Shell Parts Table behind it in its arms' reach";
        return $"Shell Gantry: {what}, {tl} - {SandboxKeys.Use}: its panel";
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The Shell Gantries' swaps under way (ConstructGantry.cs): the server's own; a client's as heard. A fixed array: nothing allocates.</summary>
    public readonly GantryJob[] GantryJobs = new GantryJob[ConstructGantry.MaxJobs];
    public int GantryJobCount;
    /// <summary>The gantries' waist clamps, held or opening (ConstructGantry.Hold): a fixed array, entries reused once open.</summary>
    public readonly GantryLock[] GantryLocks = new GantryLock[ConstructGantry.MaxJobs];
    public int GantryLockCount;
}

public sealed partial class Construct
{
    public ushort[] Gantries = []; // its Shell Gantries (made with the caches: ConstructConveyor.BuildRacks), for drawing their arms
    public int GantryCount;
}
