using System.Numerics;

namespace CallOfSlop;

public sealed partial class Construct
{
    // A Building Station's own pick pads (ConstructStationPads.cs), worked out on both peers from the parts at this RebuildStamp:
    // pairs of (station part, pad part), the stations in PieceRacks order.
    internal int StationPadStamp = -1, StationPadParts = -1;
    internal ushort[] StationPadPairs = [];
    internal int StationPadN;
}

/// <summary>
/// A Building Station's own pick pads (Sean 2026-11-21, the Zero Day works: "pick pads for pieces up to 14 t and 14 m long"). A
/// Lay-down Pad (or a Pad Tile pad) on the station's body whose deck lies wholly past one side of the bay, within <see cref="PadReach"/>
/// of it and within the bay's span along that side (past an end across, or beside a long side: Sean 2026-11-23, a pad attached to the
/// station is an input for it), is a pick pad of the station itself: its fitting arms
/// take a step's part off it as they take one from the hold, up to the station's <c>storage.pieces.lift</c> (14 t), so a part too
/// long or wide for the hold (a 14 m stern) is built in without a Hall Crane. A large part is ordered onto it as onto a Loader Arm's
/// pick pad (<see cref="ConstructLifters.PickPadFor"/>); what lies on it for the build counts as held and is claimed for it
/// (<see cref="ConstructLifters.PickPadHolds"/>, <c>PadClaims</c>). The runway is drawn on over the pad (StationRender.cs).
/// Both peers find the same pads from the parts alone: nothing here is saved or sent, and nothing allocates in a steady state.
/// </summary>
public static partial class ConstructStation
{
    public const float PadReach = 20f; // m past the bay's end a pick pad's deck may lie and the runway reaches on over it
    public const int MaxOwnPads = 4; // pick pads one station takes (the nearest first)
    private const int MaxPadPairs = 64;

    /// <summary>The pick pads of station <paramref name="t"/> into <paramref name="pads"/> (owner parts, nearest the bay first): how many.</summary>
    public static int OwnPads(Construct c, int t, Span<ushort> pads)
    {
        Note(c);
        var n = 0;
        for (var k = 0; k < c.StationPadN && n < pads.Length; k++)
            if (c.StationPadPairs[2 * k] == t && c.StationPadPairs[2 * k + 1] is var p && p < c.PartCount && !c.Parts[p].Broken)
                pads[n++] = p;
        return n;
    }

    /// <summary>Is part <paramref name="pad"/> (a pad's owner) a pick pad of station <paramref name="t"/> on the same body?</summary>
    public static bool IsOwnPad(Construct c, int t, int pad)
    {
        Note(c);
        for (var k = 0; k < c.StationPadN; k++)
            if (c.StationPadPairs[2 * k] == t && c.StationPadPairs[2 * k + 1] == pad)
                return pad < c.PartCount && !c.Parts[pad].Broken;
        return false;
    }

    /// <summary>The heaviest piece station <paramref name="t"/>'s crane picks off its pick pads (kg; 0: it takes none).</summary>
    public static float LiftKg(Construct c, int t)
    {
        if (!c.DefOf(t).StationNoHold)
            return c.DefOf(t).StationLiftKg;
        Span<StationDock> docks = stackalloc StationDock[MaxArms]; // (a rover station lifts what its largest rover lifts)
        var n = StationRovers.Docks(c, t, docks);
        var kg = 0f;
        for (var a = 0; a < n; a++)
            kg = MathF.Max(kg, StationRovers.Classes[docks[a].Cls].Kg);
        return MathF.Min(kg, c.DefOf(t).StationLiftKg);
    }

    /// <summary>
    /// A pad's deck in station <paramref name="t"/>'s bay frame (x across, z along, from the middle of its floor): its middle and its
    /// half size. False when it isn't a pad, or the station has no bay.
    /// </summary>
    public static bool PadInBay(Construct c, int t, int pad, out Vector3 mid, out Vector3 half)
    {
        mid = half = default;
        var d = ConstructPads.RackDef(c, pad);
        if (!d.IsLayDownPad || BaySize(c, t) is not { X: > 0 })
            return false;
        Bay(c, t, out var floor, out var rot, out _);
        Freeform.PartWorld(c, pad, out var pos, out var prot);
        ConstructRackPack.Hold(d, out var lo, out var hi);
        var inv = Quaternion.Conjugate(rot);
        mid = Vector3.Transform(pos + Vector3.Transform(new Vector3((lo.X + hi.X) * 0.5f, lo.Y, (lo.Z + hi.Z) * 0.5f), prot) - floor, inv);
        var q = inv * prot;
        var h = (hi - lo) * 0.5f;
        half = Vector3.Abs(Vector3.Transform(Vector3.UnitX, q)) * h.X + Vector3.Abs(Vector3.Transform(Vector3.UnitY, q)) * h.Y + Vector3.Abs(Vector3.Transform(Vector3.UnitZ, q)) * h.Z;
        return true;
    }

    /// <summary>The body's stations' pick pads, again when its parts changed (either peer).</summary>
    private static void Note(Construct c)
    {
        if (c.StationPadStamp == c.StructStamp && c.StationPadParts == c.PartCount)
            return;
        (c.StationPadStamp, c.StationPadParts, c.StationPadN) = (c.StructStamp, c.PartCount, 0);
        for (var a = 0; a < c.PieceRackCount; a++)
        {
            int t = c.PieceRacks[a];
            if (t >= c.PartCount || !c.DefOf(t).IsBuildStation || c.DefOf(t).StationLiftKg <= 0 || BaySize(c, t) is not { X: > 0 } bay)
                continue;
            var taken = 0;
            float hx = bay.X * 0.5f, hz = bay.Z * 0.5f;
            // The nearest pads first: a few passes, each taking the nearest pad not yet taken (MaxOwnPads is small).
            for (var pass = 0; pass < MaxOwnPads; pass++)
            {
                int best = -1;
                var bestOff = float.MaxValue;
                for (var b = 0; b < c.PieceRackCount; b++)
                {
                    int p = c.PieceRacks[b];
                    if (p >= c.PartCount || p == t || !PadInBay(c, t, p, out var mid, out var half) || Taken(c, t, p))
                        continue;
                    // At the bay's floor, wholly past one side of the bay and within reach of it: past an end across (x) within the bay's
                    // length, or past a long side (z) within its width (Sean, 2026-11-23: a pad attached to the station is its input, where
                    // a roller run from the line sets a piece down). The runway or the bridges reach on over it (StationRender.cs).
                    if (MathF.Abs(mid.Y) > 3f || SideOff(mid, half, hx, hz) is not { } off)
                        continue;
                    if (off < bestOff)
                        (best, bestOff) = (p, off);
                }
                if (best < 0 || c.StationPadN >= MaxPadPairs)
                    break;
                if (c.StationPadPairs.Length < 2 * (c.StationPadN + 1))
                    Array.Resize(ref c.StationPadPairs, Math.Max(8, c.StationPadPairs.Length * 2));
                c.StationPadPairs[2 * c.StationPadN] = (ushort)t;
                c.StationPadPairs[2 * c.StationPadN + 1] = (ushort)best;
                c.StationPadN++;
                taken++;
            }
            if (taken > 0)
                Console.WriteLine($"[station] body {c.Id} part {t}: {taken} pick pad{(taken == 1 ? "" : "s")} for its rovers");
        }
    }

    /// <summary>
    /// How far past the bay's side a pad's deck (<paramref name="mid"/>, <paramref name="half"/>, bay frame) lies, when it lies wholly past
    /// one side within <see cref="PadReach"/> and within the bay's span along that side; null otherwise.
    /// </summary>
    private static float? SideOff(Vector3 mid, Vector3 half, float hx, float hz)
    {
        var offX = MathF.Abs(mid.X) - half.X - hx;
        if (MathF.Abs(mid.Z) + half.Z <= hz + 0.05f && offX >= -0.05f && MathF.Abs(mid.X) + half.X <= hx + PadReach)
            return offX;
        var offZ = MathF.Abs(mid.Z) - half.Z - hz;
        if (MathF.Abs(mid.X) + half.X <= hx + 0.05f && offZ >= -0.05f && MathF.Abs(mid.Z) + half.Z <= hz + PadReach)
            return offZ;
        return null;
    }

    /// <summary>Is pad <paramref name="p"/> taken by any station already (one station's pad; the first station's when two could reach it)?</summary>
    private static bool Taken(Construct c, int t, int p)
    {
        for (var k = 0; k < c.StationPadN; k++)
            if (c.StationPadPairs[2 * k + 1] == p)
                return true;
        return false;
    }

    // ---- The arms' carries off a pad (server) -------------------------------------------------------------------------------

    /// <summary>
    /// Where an arm picks a step's part of kind <paramref name="def"/> from: the hold (-1) when it has more of it than the arms already
    /// carry from it, else the first pick pad that does and whose piece the crane lifts; -2: nowhere.
    /// </summary>
    private static int PickFrom(ConstructWorld world, Construct c, int t, StationJob j, CStore tray, int kind, ConstructPartDef d, float maxKg)
    {
        var def = j.KindDef[kind];
        var fromHold = 0;
        for (var a = 0; a < MaxArms; a++)
            if (j.ArmStep[a] >= 0 && j.StepKind[j.ArmStep[a]] == kind && j.ArmPad[a] == ushort.MaxValue)
                fromHold++;
        if (ConstructCrafting.Holds(tray, def) > fromHold)
            return -1;
        if (d.Mass > MathF.Min(maxKg, LiftKg(c, t)))
            return -2;
        Span<ushort> pads = stackalloc ushort[MaxOwnPads];
        var n = OwnPads(c, t, pads);
        for (var p = 0; p < n; p++)
        {
            if (world.StoreOf(c, pads[p]) is not { } st)
                continue;
            var carried = 0;
            for (var a = 0; a < MaxArms; a++)
                if (j.ArmStep[a] >= 0 && j.StepKind[j.ArmStep[a]] == kind && j.ArmPad[a] == pads[p])
                    carried++;
            if (ConstructCrafting.Holds(st, def) > carried)
                return pads[p];
        }
        return -2;
    }

    /// <summary>
    /// Where the hook takes a part off pad <paramref name="pad"/>, in the bay's frame: the piece's top, lying against the pad's bay-side edge
    /// (pieces roll in against it, so a rover standing on the bay beside it reaches; never past the deck's middle).
    /// </summary>
    private static Vector3 PadPickAt(Construct c, int t, int pad, ConstructPartDef d)
    {
        if (!PadInBay(c, t, pad, out var mid, out var half))
            return Vector3.Zero;
        var bay = BaySize(c, t);
        var s = d.BoundsMax - d.BoundsMin;
        var ph = MathF.Max(s.X, s.Z) * 0.5f + 0.3f; // (half its longest side: it may lie either way on the deck)
        if (MathF.Abs(mid.X) - half.X >= bay.X * 0.5f - 0.05f)
            mid.X -= MathF.Sign(mid.X) * MathF.Max(0, half.X - ph);
        else if (MathF.Abs(mid.Z) - half.Z >= bay.Z * 0.5f - 0.05f)
            mid.Z -= MathF.Sign(mid.Z) * MathF.Max(0, half.Z - ph);
        return mid with { Y = mid.Y + MathF.Min(s.Y, 4f) };
    }

    /// <summary>Has station <paramref name="t"/> any pick pad (its stuck note says where a long part goes)?</summary>
    public static bool HasOwnPad(Construct c, int t)
    {
        Span<ushort> pads = stackalloc ushort[1];
        return OwnPads(c, t, pads) > 0;
    }
}
