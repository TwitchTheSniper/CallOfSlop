using System.Numerics;

namespace CallOfSlop;

public sealed partial class StationJob
{
    public bool[] Held = []; // server: each unfitted step stands on the floor or touches a fitted step (an early fit has something under it)
    public bool HeldStale = true; // Held is made again before it is next read (a costing, a restart, a load)
}

/// <summary>
/// Early fits (Sean, 2026-11-27: "the outermost lower layer should be built first and build up over time ... production is just sitting
/// there waiting for a deck piece that goes on top, while it could be adding the rest of the keel"). A Building Station fits a step before
/// its parent when the step is a plain socket step on the body itself (<see cref="EarlyFree"/>, kept in the costing's plan) and it stands
/// on the bay's floor or touches a part already on (<see cref="StationJob.Held"/>): it goes where the costing's build had it and is
/// welded to what it meets; its parent's link is made when the parent comes. The build order then goes by height alone (keel, what lies
/// under the lowest deck, deck by deck), never held up by a parent higher than its child (a Zero Day's Keel 3 and 4 hang on the Lower Deck
/// Aft in its blueprint). Server only. Allocates only when a build is costed.
/// </summary>
public static partial class ConstructStation
{
    public const float HeldTouch = 0.05f; // how close two parts' bounds come to count as one standing on the other (m)
    public const float HeldFloor = 0.3f; // a part whose foot is this close to the build's lowest foot stands on the floor (m)

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
        if (j.HeldStale || j.Held.Length != j.PartOf.Length)
            MakeHeld(j, plan);
        return j.Held[k];
    }

    /// <summary>Each step's "held": its foot on the floor, or its bounds touching a fitted step's (once a costing, a restart or a load).</summary>
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
        var up = Up(j);
        var floor = float.MaxValue;
        for (var k = 0; k < n; k++)
            floor = MathF.Min(floor, Foot(plan, k, up));
        for (var k = 0; k < n; k++)
            if (j.PartOf[k] < 0 && Foot(plan, k, up) <= floor + HeldFloor)
                j.Held[k] = true;
        for (var k = 0; k < n; k++)
            if (j.PartOf[k] >= 0)
                Touched(j, plan, k);
    }

    /// <summary>Step <paramref name="k"/> went on: every unfitted step its bounds touch is held up now.</summary>
    private static void Touched(StationJob j, StationJob.LiftPlan plan, int k)
    {
        if (plan.Lo.Length != j.Held.Length || k >= plan.Lo.Length)
            return;
        var lo = plan.Lo[k] - new Vector3(HeldTouch);
        var hi = plan.Hi[k] + new Vector3(HeldTouch);
        for (var s = 0; s < j.Held.Length; s++)
            if (!j.Held[s] && j.PartOf[s] < 0 && plan.Lo[s].X <= hi.X && plan.Hi[s].X >= lo.X && plan.Lo[s].Y <= hi.Y && plan.Hi[s].Y >= lo.Y
                && plan.Lo[s].Z <= hi.Z && plan.Hi[s].Z >= lo.Z)
                j.Held[s] = true;
    }

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
