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
    public ushort Table; // the parts table's part on the gantry's construct (ushort.MaxValue: none, in creative)
    public byte Slot; // ShellFrames.Slots
    public byte Old, New; // 1 + the module's index going off and going on
    public byte Arm; // which arm (ConstructGantry.ArmOf)
    public double Start; // ConstructConveyor.Clock when it began (a client: when it heard it, less the time already run)
}

/// <summary>
/// The Shell Gantry (docs/sandbox/design/mining-shell.md "Shell Gantry layout"; the user, 2026-11-18): a square bay, 5 m each way, open at
/// the front, where a Robot Shell stands facing out; five arms on its rear frame (left upper and lower, right upper and lower, one over the
/// head) take a module off the shell, set it on the Shell Parts Table behind the bay and fit the one chosen in its place. The table is a
/// part of its own (a Piece Rack by its <c>storage.pieces</c>), on the gantry's construct and within the arms' reach behind the rear frame:
/// that is its link to the gantry, as a lifter serves what is in its reach. A swap is the server's, timed (<see cref="Duration"/>), and
/// every peer hears it (<see cref="Msg.Gantry"/>) and draws the arm on its way (GantryRender.cs). The shell's choice is kept in its
/// part's settings (<see cref="ShellFrames.WriteKit"/>), so it rides saves and the shell wakes with it.
/// </summary>
public static class ConstructGantry
{
    public const int ArmCount = 5;
    public const int MaxJobs = 32;

    // The swap's times (s): the arm reaches the slot, unclips the module, carries it to the table, sets it down and takes the new
    // one, carries it back, fits it, and goes back to rest.
    public const double TAtShell = 1.2, TUnclipped = 2.0, TAtTable = 3.5, TSwapped = 4.0, TBack = 5.5, TFitted = 6.3, Duration = 7.5;

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

    /// <summary>The parts table the gantry's arms reach: a Shell Parts Table on its own construct, behind the rear frame, within every arm's reach. -1: none.</summary>
    public static int TableOf(Construct c, int part)
    {
        if (c.DefOf(part).Gantry is not { } gd)
            return -1;
        var best = -1;
        var near = float.MaxValue;
        foreach (var i in c.Specials)
        {
            if (i == part || !c.Parts[i].D.GantryTable || c.Parts[i].Broken)
                continue;
            var at = TablePoint(c, part, i);
            if (at.Z < gd.BayMax.Z)
                continue; // (behind the rear frame only)
            var far = 0f;
            for (var k = 0; k < ArmCount; k++)
                far = MathF.Max(far, Vector3.Distance(at, gd.Arms[k]));
            if (far <= gd.Reach && far < near)
                (best, near) = (i, far);
        }
        return best;
    }

    /// <summary>Where the arms set a module down on table part <paramref name="table"/>: the middle of its hold's floor, in the gantry part's frame.</summary>
    public static Vector3 TablePoint(Construct c, int part, int table)
    {
        ConstructRackPack.Hold(c.DefOf(table), out var lo, out var hi);
        Freeform.PartWorld(c, table, out var tp, out var tr);
        var w = tp + Vector3.Transform(new Vector3((lo.X + hi.X) * 0.5f, lo.Y + 0.15f, (lo.Z + hi.Z) * 0.5f), tr);
        return ToPart(c, part, w);
    }

    /// <summary>A slot's point on a shell standing as part 0 of <paramref name="shell"/>, in the world (the arm's hand takes the module there).</summary>
    public static Vector3 SlotPoint(Construct shell, int slot)
    {
        var scale = ShellFrames.Of(ShellFrames.FrameOf(shell.Parts[0].D)).Scale;
        var local = slot switch
        {
            0 => new Vector3(0, 1.7f, 0),
            1 => new Vector3(0, 1.25f, 0.18f),
            2 => new Vector3(-0.31f, 1.05f, 0),
            3 => new Vector3(0.31f, 1.05f, 0),
            4 => new Vector3(-0.11f, 0.45f, 0),
            _ => new Vector3(0.11f, 0.45f, 0),
        } * scale;
        Freeform.PartWorld(shell, 0, out var pos, out var rot);
        return pos + Vector3.Transform(local, rot);
    }

    /// <summary>An arm's hand at rest, in the part's frame: hung down and forward of its shoulder.</summary>
    public static Vector3 RestOf(ShellGantryDef gd, int arm) => gd.Arms[arm] + new Vector3(0, -0.55f, -0.6f);

    /// <summary>
    /// Where a job's hand is at <paramref name="t"/> seconds in, in the gantry part's frame, from the slot's and the table's points (also
    /// in that frame), and what it carries: 0 nothing, 1 the old module, 2 the new one (3: the old one lies on the table, the new in hand).
    /// </summary>
    public static Vector3 Hand(ShellGantryDef gd, int arm, Vector3 slot, Vector3 table, double t, out int carry)
    {
        var rest = RestOf(gd, arm);
        var slotOff = slot + new Vector3(0, 0, 0.25f); // (the hand works the module from behind the shell)
        carry = t < TUnclipped ? 0 : t < TSwapped ? 1 : t < TFitted ? 3 : 0;
        if (t < TAtShell)
            return Path(rest, slotOff, t / TAtShell);
        if (t < TUnclipped)
            return Vector3.Lerp(slotOff, slot, (float)((t - TAtShell) / (TUnclipped - TAtShell)) * 0.6f);
        if (t < TAtTable)
            return Path(slot, table, (t - TUnclipped) / (TAtTable - TUnclipped));
        if (t < TSwapped)
            return table;
        if (t < TBack)
            return Path(table, slot, (t - TSwapped) / (TBack - TSwapped));
        if (t < TFitted)
            return slot;
        return Path(slotOff, rest, Math.Min(1, (t - TFitted) / (Duration - TFitted)));
    }

    /// <summary>A smooth move from a to b, lifted over the middle (the hand clears the frame and the shell's shoulders).</summary>
    private static Vector3 Path(Vector3 a, Vector3 b, double s)
    {
        var f = (float)Math.Clamp(s, 0, 1);
        f = f * f * (3 - 2 * f);
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

    /// <summary>Is this shell in a gantry's hands (it can't be woken or taken while it is)?</summary>
    public static bool Holds(ConstructWorld world, ushort shell)
    {
        for (var k = 0; k < world.GantryJobCount; k++)
            if (world.GantryJobs[k].Shell == shell)
                return true;
        return false;
    }

    /// <summary>How many of a module's part lie on a table (0: no table, or none).</summary>
    public static int OnTable(ConstructWorld world, Construct c, int table, ShellModule m)
    {
        if (table < 0 || m.Part.Length == 0 || ConstructParts.Find(m.Part) is not { } d || world.StoreOf(c, table) is not { } st)
            return 0;
        return ConstructCrafting.Holds(st, (ushort)d.Id);
    }

    /// <summary>The look line: what is in the bay, the table, the work under way.</summary>
    public static string Look(ConstructWorld world, Construct c, int part)
    {
        var shell = ShellIn(world, c, part);
        var table = TableOf(c, part);
        var j = JobAt(world, c.Id, part);
        var what = j >= 0 && ShellFrames.Modules is { } all && world.GantryJobs[j].New - 1 is var n and >= 0 && n < all.Length
            ? $"fitting a {all[n].Name} to the {ShellFrames.SlotLabel(world.GantryJobs[j].Slot)}"
            : shell != null ? $"{shell.Parts[0].D.Name} in the bay" : "the bay is empty";
        var tb = table >= 0 ? $"parts table: {world.StoreOf(c, table)?.ItemCount ?? 0} parts" : "no Shell Parts Table behind it in its arms' reach";
        return $"Shell Gantry: {what}, {tb} - {SandboxKeys.Use}: its panel";
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>The Shell Gantries' swaps under way (ConstructGantry.cs): the server's own; a client's as heard. A fixed array: nothing allocates.</summary>
    public readonly GantryJob[] GantryJobs = new GantryJob[ConstructGantry.MaxJobs];
    public int GantryJobCount;
}

public sealed partial class Construct
{
    public ushort[] Gantries = []; // its Shell Gantries (made with the caches: ConstructConveyor.BuildRacks), for drawing their arms
    public int GantryCount;
}
