using System.Numerics;

namespace CallOfSlop;

public sealed partial class StationJob
{
    public bool[] Held = []; // server: each unfitted step stands on the floor or touches a fitted step (an early fit has something under it)
    public bool HeldStale = true; // Held is made again before it is next read (a costing, a restart, a load)
    public Dictionary<long, List<int>>? HeldGrid; // the steps by the 4 m columns their plan bounds cover (made with Held: a touch test asks only its own columns)
}

/// <summary>
/// Early fits (Sean, 2026-11-27: "the outermost lower layer should be built first and build up over time ... production is just sitting
/// there waiting for a deck piece that goes on top, while it could be adding the rest of the keel"). A Building Station fits a step before
/// its parent when the step is a plain socket step on the body itself (<see cref="EarlyFree"/>, kept in the costing's plan) and it is a
/// keel piece, stands no higher than the keel or touches a part already on (<see cref="StationJob.Held"/>): it goes where the costing's build had it and is
/// welded to what it meets; its parent's link is made when the parent comes. The build order then goes by height alone (keel, what lies
/// under the lowest deck, deck by deck), never held up by a parent higher than its child (a Zero Day's Keel 3 and 4 hang on the Lower Deck
/// Aft in its blueprint). Server only. Allocates only when a build is costed.
/// </summary>
public static partial class ConstructStation
{
    public const float HeldTouch = 0.05f; // how close two parts' bounds come to count as one standing on the other (m)
    public const float HeldFloor = 0.3f; // a part whose foot is no higher than this over the first piece's (the keel's) stands on the cradle (m)

    /// <summary>Costing: may step <paramref name="k"/> (part <paramref name="i"/> of the costing's body) go on before its parent?</summary>
    private static bool EarlyFree(ConstructBuild.Blueprint bp, int k, Construct built, int i, ConstructPartDef d) =>
        k > 0 && built.Parts[i].Seg == 0 && built.MergeCount == 0 && !d.IsReceiverGun && d.Role != PartRole.GunPart
        && !(k < bp.StepWeld.Count && bp.StepWeld[k] != null) && built.ScaleOf(i) == 1f;

    /// <summary>Are step <paramref name="k"/>'s parent and "also" steps on?</summary>
    private static bool ParentsOn(StationJob j, int k)
    {
        var bp = j.Bp!;
        var on = bp.Steps[k].On;
        if (on < 0 || on >= j.PartOf.Length || j.PartOf[on] < 0)
            return false;
        if (k < bp.StepAlso.Count && bp.StepAlso[k] is { } also)
            foreach (var a in also)
                if (a.On < 0 || a.On >= j.PartOf.Length || j.PartOf[a.On] < 0)
                    return false;
        return true;
    }

    /// <summary>May step <paramref name="k"/> go on now ahead of its parent (the build's body is there, and something holds it up)?</summary>
    private static bool EarlyReady(StationJob j, int k)
    {
        if (j.Plan is not { } plan || plan.Free.Length != j.PartOf.Length || !plan.Free[k] || j.PartOf.Length == 0 || j.PartOf[0] < 0)
            return false;
        if (InUse(j, j.Bp!.Steps[k].On) || AlsoInUse(j, k))
            return false; // (a use's parts come in exact, by MergeAt, with no late links: what hangs on one waits for it)
        if (j.HeldStale || j.Held.Length != j.PartOf.Length)
            MakeHeld(j, plan);
        return j.Held[k];
    }

    private static bool InUse(StationJob j, int s) => j.StepRow != null && s >= 0 && s < j.StepRow.Length && j.StepRow[s] >= 0;

    private static bool AlsoInUse(StationJob j, int k)
    {
        var bp = j.Bp!;
        if (j.StepRow == null || k >= bp.StepAlso.Count || bp.StepAlso[k] is not { } also)
            return false;
        foreach (var a in also)
            if (InUse(j, a.On))
                return true;
        return false;
    }

    /// <summary>Each step's "held": a keel piece, its foot no higher than the first piece's, or its bounds touching a fitted step's (once a costing, a restart or a load).</summary>
    private static void MakeHeld(StationJob j, StationJob.LiftPlan plan)
    {
        var n = j.PartOf.Length;
        if (j.Held.Length != n)
            j.Held = new bool[n];
        else
            Array.Clear(j.Held);
        j.HeldStale = false;
        if (plan.Lo.Length != n)
            return;
        var grid = j.HeldGrid ??= new Dictionary<long, List<int>>();
        foreach (var l in grid.Values)
            l.Clear();
        for (var k = 0; k < n; k++)
            if (Placed(plan, k))
            {
                Cells(plan.Lo[k], plan.Hi[k], out var x0, out var z0, out var x1, out var z1);
                for (var x = x0; x <= x1; x++)
                    for (var z = z0; z <= z1; z++)
                    {
                        if (!grid.TryGetValue(Key(x, z), out var l))
                            grid[Key(x, z)] = l = new List<int>(4);
                        l.Add(k);
                    }
            }
        // (the keel's pieces lie on the bay's cradle in a line, whatever they hang on in the blueprint, and so does anything no higher than
        // the first piece's foot: a Zero Day's Keel 3 came before Keel 4 between it and the Keel and lay on its pad, held by nothing)
        var up = Up(j);
        var floor = Foot(plan, 0, up);
        var ranked = j.StepRank.Length == n;
        for (var k = 0; k < n; k++)
            if (j.PartOf[k] < 0 && (Foot(plan, k, up) <= floor + HeldFloor || ranked && j.StepRank[k] == 0))
                j.Held[k] = true;
        for (var k = 0; k < n; k++)
            if (j.PartOf[k] >= 0)
                Touched(j, plan, k);
    }

    /// <summary>Step <paramref name="k"/> went on: every unfitted step its bounds touch is held up now.</summary>
    private static void Touched(StationJob j, StationJob.LiftPlan plan, int k)
    {
        if (plan.Lo.Length != j.Held.Length || k >= plan.Lo.Length || !Placed(plan, k) || j.HeldGrid is not { } grid)
            return;
        var lo = plan.Lo[k] - new Vector3(HeldTouch);
        var hi = plan.Hi[k] + new Vector3(HeldTouch);
        Cells(lo, hi, out var x0, out var z0, out var x1, out var z1);
        for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
                if (grid.TryGetValue(Key(x, z), out var l))
                    foreach (var s in l)
                        if (!j.Held[s] && j.PartOf[s] < 0 && plan.Lo[s].X <= hi.X && plan.Hi[s].X >= lo.X && plan.Lo[s].Y <= hi.Y && plan.Hi[s].Y >= lo.Y
                            && plan.Lo[s].Z <= hi.Z && plan.Hi[s].Z >= lo.Z)
                            j.Held[s] = true;
    }

    public const float HeldCell = 4f; // the touch grid's column (m)

    /// <summary>Did the costing's build have a part for step <paramref name="k"/> (a step it had none for keeps empty bounds)?</summary>
    private static bool Placed(StationJob.LiftPlan plan, int k) => plan.Hi[k] != plan.Lo[k];

    private static void Cells(Vector3 lo, Vector3 hi, out int x0, out int z0, out int x1, out int z1)
    {
        (x0, z0) = ((int)MathF.Floor(lo.X / HeldCell), (int)MathF.Floor(lo.Z / HeldCell));
        (x1, z1) = ((int)MathF.Floor(hi.X / HeldCell), (int)MathF.Floor(hi.Z / HeldCell));
        x1 = Math.Min(x1, x0 + 64); // (a part bigger than 256 m: its far columns aren't asked)
        z1 = Math.Min(z1, z0 + 64);
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    /// <summary>The bay's up in the build's body frame.</summary>
    private static Vector3 Up(StationJob j) => Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(j.Rot));

    /// <summary>Step <paramref name="k"/>'s lowest point along <paramref name="up"/> (its bounds' corner furthest down).</summary>
    private static float Foot(StationJob.LiftPlan plan, int k, Vector3 up) =>
        Vector3.Dot((plan.Lo[k] + plan.Hi[k]) * 0.5f, up) - Vector3.Dot(Vector3.Abs(plan.Hi[k] - plan.Lo[k]) * 0.5f, Vector3.Abs(up));

    /// <summary>The fit of step <paramref name="k"/> onto the build's body <paramref name="b"/>: by its socket when its parent is on, else early, where the costing had it.</summary>
    private static string? FitEither(ConstructWorld world, Construct b, StationJob j, int k)
    {
        var bp = j.Bp!;
        var early = k > 0 && !ParentsOn(j, k) && j.Plan is { } plan && plan.Free.Length == j.PartOf.Length && plan.Free[k];
        var why = early ? ConstructBuild.FitAt(world, b, bp, k, j.PartOf, j.Plan!.At[k], j.Plan.RotIx[k]) : ConstructBuild.FitStep(world, b, bp, k, j.PartOf);
        if (why == null)
            ConstructBuild.LinkLate(world, b, bp, k, j.PartOf, early); // (and the links of steps that went on before it, a load between or not)
        return why;
    }

    /// <summary>After a step went on: what it holds up (<see cref="StationJob.Held"/>).</summary>
    private static void HeldOn(StationJob j, int k)
    {
        if (!j.HeldStale && j.Plan is { } plan && j.Held.Length == j.PartOf.Length)
            Touched(j, plan, k);
    }
}
