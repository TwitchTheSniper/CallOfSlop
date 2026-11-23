using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Straight to the destination (Sean, 2026-11-23: "parts should move directly to their destination if possible"). Production in the
/// works is made to order, not to a backlog, so a lifter that picks a made piece (a machine's out, a run's end, a stock rack) and
/// fits for a building station whose next steps want that kind fits it at once, carrying it from where it picked it to its place on the
/// build, instead of laying it on the station's pick pad and coming back for it. Only when no ready step in its reach takes it does the
/// piece go on by the Routers' rule (a want, a hand-off, else storage). Server only; allocates nothing.
/// </summary>
public static partial class ConstructLifters
{
    /// <summary>
    /// Server: a building station's ready, unclaimed step of kind <paramref name="d"/> whose place lifter <paramref name="l"/> reaches
    /// (in Auto: it does station work and carries), the station powered: the step's index, else -1. <paramref name="sc"/>,
    /// <paramref name="t"/> and <paramref name="j"/> are the station's body, part and job.
    /// </summary>
    public static int DirectStep(ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, out Construct? sc, out int t, out StationJob? j)
    {
        (sc, t, j) = (null, -1, null);
        if (world.StationOpen.Count == 0 || ld.Fitter || !Carries(ld, d) || (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0) != 0)
            return -1; // (Station only and Carry only keep to their halves; Off does nothing)
        var lift = WristLift(d);
        for (var si = 0; si < world.StationOpen.Count; si++)
        {
            if (world.Get(world.StationOpen[si]) is not { } b)
                continue;
            for (var p = 0; p < b.PartCount; p++)
            {
                if (b.Parts[p].Station is not { State: StationState.Building, Plan: { } plan, Bp: { } } job || !b.DefOf(p).IsBuildStation
                    || plan.At.Length != job.PartOf.Length || !ConstructRecycler.Powered(world, b, p))
                    continue;
                ConstructStation.Bay(b, p, out var floor, out _, out var bay);
                if (Vector3.Distance(floor, shoulder) > ld.ReachMax + bay.Length() * 0.5f + ld.Down)
                    continue;
                var looked = 0;
                for (var k = job.Cursor; k < job.PartOf.Length && looked < ConstructStation.OfferLooks; k++)
                {
                    if (job.PartOf[k] != -1)
                        continue;
                    looked++;
                    if (job.StepKind[k] < 0 || job.KindDef[job.StepKind[k]] != d.Id || !ConstructStation.ReadyStep(job, k)
                        || !InReach(ld, shoulder, lr, job.Pos + Vector3.Transform(plan.Centre[k], job.Rot), lift, out _))
                        continue;
                    (sc, t, j) = (b, p, job);
                    return k;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// Server: the piece already out of where it was picked (<paramref name="from"/>: its middle, <paramref name="fromRot"/>: its turn)
    /// carried straight to step <paramref name="k"/> of station (<paramref name="sc"/>, <paramref name="t"/>): it counts for the station's
    /// order as a part landing in its hold or on its pick pad does, the step is claimed, and the move ends in the station's own fit.
    /// </summary>
    private static void StartDirect(ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 lp, Quaternion lr, ConstructPartDef d, Vector3 from, Quaternion fromRot, Construct sc, int t, StationJob j, int k)
    {
        var plan = j.Plan!;
        ConstructCrafting.Arrived(world, sc, t, d); // (its order is met: nothing else is made or fetched for this step)
        ConstructStation.LiftClaim(j, k);
        var inv = Quaternion.Conjugate(lr);
        var place = j.Pos + Vector3.Transform(plan.At[k], j.Rot);
        l.OrderId = 0;
        l.Def = (ushort)d.Id;
        l.Paint = k < j.Bp!.StepPaint.Count ? j.Bp.StepPaint[k] : 0;
        l.Health = 255;
        l.From = Vector3.Transform(from - Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, fromRot) - lp, inv);
        l.FromRot = Quaternion.Normalize(inv * fromRot);
        l.To = Vector3.Transform(place - lp, inv);
        l.ToRot = Quaternion.Normalize(inv * j.Rot * plan.Rot[k]);
        l.Age = 0;
        l.Rise = RiseOf(c, l.Part, ld);
        l.Dur = CarryTicks(ld, l, d);
        l.Why = LiftWhy.None;
        l.ToHold = false;
        l.ToKind = LiftPointKind.None;
        Begin(l);
        l.State = LiftState.Carry;
        l.Moved = true;
        l.Fitted = false;
        (l.StationBody, l.StationPart, l.Step, l.Job, l.Plan) = (sc.Id, t, k, j, plan);
        LifterProbe.CarriesHeard++;
        LifterProbe.Carries++;
        LifterProbe.Direct++;
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: a {d.Name} straight to step {k} of the station (body {sc.Id} part {t})");
        world.LiftChanged(c.Id);
    }

    /// <summary><see cref="DirectStep"/> when <paramref name="when"/>, else -1.</summary>
    private static int DirectIf(bool when, ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, out Construct? sc, out int t, out StationJob? j)
    {
        (sc, t, j) = (null, -1, null);
        return when ? DirectStep(world, c, l, ld, shoulder, lr, d, out sc, out t, out j) : -1;
    }

    /// <summary>Would the put point a piece was given be a store or a hand-off (so a ready station step that wants it goes first)?</summary>
    private static bool StoreLike(CLift l, int to) => to < 0 || l.Points![to].Kind is LiftPointKind.Rack or LiftPointKind.RunStart;
}

public static partial class LifterProbe
{
    public static int Direct; // server: made pieces carried straight to a station step (ConstructLifterDirect.cs)
}
