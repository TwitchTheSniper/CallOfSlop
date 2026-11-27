using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Building Station's arm rovers at work (the rover docks; the model and its rules are StationRovers in ConstructStationRovers.cs):
/// each tick a rover on a trip counts it down and fits its part when the weld ends; an idle rover off its pad drives home after a while;
/// and idle rovers take the next ready steps in the build order (keel first, then what lies under the lowest deck, then deck by deck:
/// each deck's structure, then its fittings), each step to the rover that would have it on soonest with the class rule's penalty, one
/// new trip a tick. Server; the trips go on the wire (WriteArms) and every peer draws them (StationRender.cs).
/// </summary>
public static partial class ConstructStation
{
    public const int StallTicks = 30 * World.TickRate; // no fit this long, nothing on its way: the next rank may go (a part nothing brings never holds the whole build)
    public const int AssignLooks = 32; // ready steps one look tries

    /// <summary>The station's rovers: one a dock (the Core and its Dock Expansions; either peer).</summary>
    public static int Arms(Construct c, int part)
    {
        Span<StationDock> docks = stackalloc StationDock[MaxArms];
        return StationRovers.Docks(c, part, docks);
    }

    /// <summary>May step <paramref name="k"/> go on now by the build order (its rank is open)?</summary>
    internal static bool Gated(StationJob j, int k) => j.StepRank.Length != j.PartOf.Length || j.StepRank[k] <= j.GateRank;

    // ---- The build order (at the costing's end) ----------------------------------------------------------------------------

    /// <summary>
    /// Each step's rank (the lab's rule, Sean 2026-11-24): every Keel piece 0; what lies wholly under the lowest deck's top 1; then 2 + its
    /// deck's level x 2, + 1 for a fitting (not frame or panel), a piece's deck being the highest deck top at or under its foot. A step
    /// goes no earlier than its parent's rank. Also the build's footprint in the bay (the rovers' keep-out ring).
    /// </summary>
    private static void BuildOrder(Construct c, int t, StationJob j)
    {
        var bp = j.Bp!;
        var n = bp.Steps.Count;
        j.StepRank = new int[n];
        j.RankOrder = new int[n];
        (j.GateRank, j.GateStall, j.BuiltTop) = (0, 0, 0f);
        if (j.Plan is not { } plan || plan.Centre.Length != n)
        {
            for (var k = 0; k < n; k++)
                j.RankOrder[k] = k;
            j.KeepLo = j.KeepHi = Vector2.Zero;
            return;
        }
        Bay(c, t, out var floor, out var rot, out _);
        var inv = Quaternion.Conjugate(rot);
        var los = new float[n];
        var his = new float[n];
        var floors = new List<float>();
        Vector2 klo = new(float.MaxValue), khi = new(float.MinValue);
        var defs = new ConstructPartDef?[n];
        for (var k = 0; k < n; k++)
        {
            var d = defs[k] = ConstructParts.Find(bp.Steps[k].Part);
            if (d == null)
                continue;
            var q = inv * j.Rot * plan.Rot[k];
            var h = (d.BoundsMax - d.BoundsMin) * 0.5f;
            var e = Vector3.Abs(Vector3.Transform(Vector3.UnitX, q)) * h.X + Vector3.Abs(Vector3.Transform(Vector3.UnitY, q)) * h.Y + Vector3.Abs(Vector3.Transform(Vector3.UnitZ, q)) * h.Z;
            var m = Vector3.Transform(j.Pos + Vector3.Transform(plan.Centre[k], j.Rot) - floor, inv);
            (los[k], his[k]) = (m.Y - e.Y, m.Y + e.Y);
            klo = Vector2.Min(klo, new Vector2(m.X - e.X, m.Z - e.Z));
            khi = Vector2.Max(khi, new Vector2(m.X + e.X, m.Z + e.Z));
            if (d.Name.Contains("Deck", StringComparison.OrdinalIgnoreCase) && d.Role is PartRole.Frame or PartRole.Panel && e.X * e.Z >= 1f)
                floors.Add(his[k]);
        }
        (j.KeepLo, j.KeepHi) = klo.X <= khi.X ? (klo, khi) : (Vector2.Zero, Vector2.Zero);
        floors.Sort();
        for (var k = floors.Count - 1; k > 0; k--)
            if (floors[k] - floors[k - 1] < 0.5f)
                floors.RemoveAt(k); // (one deck's plates at one height)
        var floor0 = floors.Count > 0 ? floors[0] : float.MinValue;
        for (var k = 0; k < n; k++)
        {
            int r;
            if (defs[k] is not { } d)
                r = 0;
            else if (d.Name.Contains("Keel", StringComparison.OrdinalIgnoreCase))
                r = 0;
            else if (his[k] < floor0 - 0.3f)
                r = 1;
            else
            {
                var lvl = 0;
                for (var f = 0; f < floors.Count; f++)
                    if (floors[f] <= los[k] + 0.3f)
                        lvl = f + 1;
                r = 2 + lvl * 2 + (d.Role is PartRole.Frame or PartRole.Panel ? 0 : 1);
            }
            var on = bp.Steps[k].On;
            if (k > 0 && on >= 0 && on < k)
                r = Math.Max(r, j.StepRank[on]); // (never before its parent: it is welded to it)
            if (k < bp.StepAlso.Count && bp.StepAlso[k] is { } also)
                foreach (var a in also)
                    if (a.On >= 0 && a.On < k)
                        r = Math.Max(r, j.StepRank[a.On]);
            j.StepRank[k] = r;
            j.RankOrder[k] = k;
        }
        var ranks = j.StepRank;
        Array.Sort(j.RankOrder, (a, b) => ranks[a] != ranks[b] ? ranks[a].CompareTo(ranks[b]) : a.CompareTo(b));
        Console.WriteLine($"[station] {j.Name}: build order {floors.Count} deck{(floors.Count == 1 ? "" : "s")}, ranks up to {(n > 0 ? ranks.Max() : 0)}; footprint {khi.X - klo.X:0.0} x {khi.Y - klo.Y:0.0} m");
    }

    /// <summary>A step went on (whoever fitted it): the gate's stall clock starts again and the carry height learns the build's top.</summary>
    private static void Fitted(Construct c, int t, StationJob j, int k, ConstructPartDef? d)
    {
        j.GateStall = 0;
        if (d != null && j.Plan is { } plan && k < plan.Centre.Length)
            j.BuiltTop = MathF.Max(j.BuiltTop, ArmTarget(c, t, j, k, d).Y);
    }

    /// <summary>The lowest rank with a step still to fit (a stall of <see cref="StallTicks"/> lets the next go).</summary>
    private static void Gate(StationJob j, bool busy)
    {
        if (j.StepRank.Length != j.PartOf.Length)
            return;
        var open = int.MaxValue;
        foreach (var k in j.RankOrder)
            if (j.PartOf[k] == -1 && !ConstructStationTree.External(j, k))
            {
                open = j.StepRank[k];
                break;
            }
        if (open == int.MaxValue)
            return;
        if (open > j.GateRank)
            j.GateRank = open;
        if (busy)
            j.GateStall = 0;
        else if (j.GateRank <= open && (j.GateStall += j.ArmGap) >= StallTicks)
        {
            j.GateStall = 0;
            var next = int.MaxValue;
            foreach (var k in j.RankOrder)
                if (j.PartOf[k] == -1 && j.StepRank[k] > j.GateRank)
                {
                    next = j.StepRank[k];
                    break;
                }
            if (next != int.MaxValue)
            {
                Console.WriteLine($"[station] {j.Name}: nothing fitted for {StallTicks / World.TickRate} s, the next rank ({next}) may go");
                j.GateRank = next;
            }
        }
    }

    /// <summary>
    /// Server, as a build starts: why its loaders can't fit one of its parts, or null. A part is fitted by a docked rover that lifts its mass
    /// and reaches where it goes from the bay (<see cref="StationRovers.Reaches"/>, the test a trip is planned by), or by a crane or arm over
    /// the bay that lifts it (<see cref="ConstructLifters.MostOverBay"/>). With no height of its own the bay builds as high as that (Sean,
    /// 2026-11-26), so a part no loader reaches is told here, before anything is ordered, not left waiting at its rank for good.
    /// </summary>
    private static string? LoaderRefusal(ConstructWorld world, Construct c, int t, StationJob j)
    {
        if (j.Bp is not { } bp || j.Plan is not { } plan || plan.Centre.Length != bp.Steps.Count || j.StepKind.Length != bp.Steps.Count)
            return null;
        Span<StationDock> docks = stackalloc StationDock[MaxArms];
        var n = StationRovers.Docks(c, t, docks);
        var lift = ConstructLifters.MostOverBay(world, c, t);
        var g = GroundOf(c, t, j);
        var roverKg = 0f;
        for (var a = 0; a < n; a++)
            roverKg = MathF.Max(roverKg, StationRovers.Classes[docks[a].Cls].Kg);
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            if (ConstructStationTree.External(j, k) || j.StepKind[k] is not (var kind and >= 0) || ConstructParts.Get(j.KindDef[kind]) is not { } d || d.Mass <= lift)
                continue; // (a use built at another station, or a crane over the bay takes it)
            var place = ArmTarget(c, t, j, k, d);
            var heavy = true;
            var ok = false;
            for (var a = 0; a < n && !ok; a++)
            {
                var rc = StationRovers.Classes[docks[a].Cls];
                if (d.Mass > rc.Kg)
                    continue;
                heavy = false;
                ok = StationRovers.Reaches(rc, StationRovers.SpotFor(g, rc, place), place);
            }
            if (ok)
                continue;
            if (n == 0)
                return "has no rover to fit its parts: weld the station's Core or a Dock Expansion on the bay's edge";
            return heavy
                ? $"its rovers can't lift its {d.Name} ({d.Mass / 1000f:0.#} t, theirs at most {roverKg / 1000f:0.#} t): weld a larger Dock Expansion on the bay's edge, or put a crane over the bay"
                : $"its rovers can't reach its {d.Name}, {place.Y:0.0} m over the bay's floor: weld a larger Dock Expansion on the bay's edge, or put a crane over the bay";
        }
        return null;
    }

    // ---- The rovers' tick (server) ---------------------------------------------------------------------------------------

    private static StationRovers.Ground GroundOf(Construct c, int t, StationJob j)
    {
        var bay = BaySize(c, t);
        return new StationRovers.Ground { Hx = bay.X * 0.5f, Hz = bay.Z * 0.5f, KLo = j.KeepLo, KHi = j.KeepHi, Keep = j.KeepHi.X > j.KeepLo.X };
    }

    /// <summary>Rover <paramref name="a"/> as it stands <paramref name="ahead"/> s from now (on its trip, or where it stands).</summary>
    private static void PoseAt(StationJob j, in StationDock dk, int a, float ahead, out Vector2 at, out float h)
    {
        if (j.ArmTicks[a] > 0)
        {
            var el = (j.ArmTicks[a] - j.ArmLeft[a]) / (float)World.TickRate + ahead;
            j.Rov[a].BaseAt(el, out var x, out var z, out h);
            at = new Vector2(x, z);
            return;
        }
        if (j.RovSet[a])
            (at, h) = (j.RovAt[a], j.RovH[a]);
        else
            (at, h) = (dk.Mid, MathF.Atan2(dk.Dir.X, dk.Dir.Y));
    }

    private static Vector3 HookNow(StationJob j, in StationDock dk, int a)
    {
        if (j.ArmTicks[a] > 0)
            return j.Rov[a].HookAt((j.ArmTicks[a] - j.ArmLeft[a]) / (float)World.TickRate);
        if (j.RovSet[a])
            return j.RovHook[a];
        var rc = StationRovers.Classes[dk.Cls];
        return new Vector3(dk.Dir.X * 1.5f, rc.Shoulder + 0.5f, dk.Dir.Y * 1.5f);
    }

    /// <summary>The first rover the trip in <c>j.Try</c> for rover <paramref name="a"/> would meet (-1: none), sampled a second apart.</summary>
    private static int FirstClash(StationJob j, ReadOnlySpan<StationDock> docks, int n, int a)
    {
        var p = j.Try;
        for (var u = 0f; u <= p.Total + 0.01f; u += 1f)
        {
            p.BaseAt(u, out var x, out var z, out _);
            var me = new Vector2(x, z);
            for (var b = 0; b < n; b++)
            {
                if (b == a)
                    continue;
                PoseAt(j, docks[b], b, u, out var ob, out _);
                if (StationRovers.Clash(docks[a], me, docks[b], ob, 0.3f))
                    return b;
            }
        }
        return -1;
    }

    /// <summary>Start rover <paramref name="a"/> on the trip in <c>j.Try</c>.</summary>
    private static void Begin(ConstructWorld world, Construct c, StationJob j, int a, int step, ushort def, ushort pad)
    {
        j.Rov[a].CopyFrom(j.Try);
        var ticks = (int)MathF.Ceiling(j.Try.Total * World.TickRate);
        j.ArmTicks[a] = (ushort)Math.Clamp(ticks, 1, ushort.MaxValue);
        j.ArmLeft[a] = j.ArmTicks[a];
        j.ArmStep[a] = step;
        j.ArmDef[a] = def;
        j.ArmPad[a] = pad;
        j.RovIdle[a] = 0;
        j.ArmSeq[a]++;
        (j.ArmSeen[a], j.ArmStart[a]) = (j.ArmSeq[a], ConstructConveyor.Clock); // (the host draws from the same job)
        world.StationChanged(c);
    }

    /// <summary>Send rover <paramref name="a"/> home from where it is now, its trip cut short (waiting up to a minute for its way to clear).</summary>
    private static void SendHome(ConstructWorld world, Construct c, int t, StationJob j, ReadOnlySpan<StationDock> docks, int n, int a)
    {
        var dk = docks[a];
        PoseAt(j, dk, a, 0, out var at, out var h);
        if (Vector2.Distance(at, dk.Mid) < 0.05f && j.ArmTicks[a] == 0)
        {
            j.RovSet[a] = false;
            return;
        }
        var hook = HookNow(j, dk, a);
        var g = GroundOf(c, t, j);
        var keep = j.ArmTicks[a];
        j.ArmTicks[a] = 0; // (its own old trip is no obstacle to itself)
        for (var w = 0f; w <= 60f; w += 3f)
        {
            StationRovers.PlanHome(j.Try, g, dk, at, h, hook, w);
            if (FirstClash(j, docks, n, a) < 0 || w >= 60f)
                break;
        }
        j.ArmTicks[a] = keep;
        Begin(world, c, j, a, -1, None, None);
    }

    /// <summary>Trips counted down, fits at their weld's end, idle rovers sent home. True while any rover is on a trip.</summary>
    private static bool TickTrips(ConstructWorld world, Construct c, int t, StationJob j, bool fit = false)
    {
        var busy = false;
        Span<StationDock> docks = stackalloc StationDock[MaxArms];
        var n = StationRovers.Docks(c, t, docks); // (kept with the body's parts: a stamp check)
        DockSwaps(world, c, j, docks, n);
        for (var a = 0; a < MaxArms; a++)
        {
            if (j.ArmTicks[a] == 0)
                continue;
            var plan = j.Rov[a];
            var el = j.ArmTicks[a] - j.ArmLeft[a];
            var k = j.ArmStep[a];
            if (k >= 0 && el >= (int)(plan.FitT * World.TickRate))
            {
                if (!fit || world.FitThisTick)
                {
                    busy = true; // (one fit a tick a world: it holds the piece a tick longer)
                    if (!fit)
                    {
                        j.ArmStep[a] = -1; // (the job stopped: the piece goes back with the cancel's parts)
                        j.ArmPad[a] = None;
                    }
                    continue;
                }
                var pad = j.ArmPad[a];
                j.ArmStep[a] = -1;
                j.ArmPad[a] = None;
                if (j.PartOf[k] == -1 && j.Bp != null && ConstructParts.Find(j.Bp.Steps[k].Part) is { } d)
                {
                    var took = j.StepKind[k] is var sk and >= 0 && j.KindLaid[sk] ? TakeLaid(world, c, t, d)
                        : pad != None ? pad < c.PartCount && world.StoreOf(c, pad) is { } pst && world.StationTake(c, pad, pst, d)
                        : world.StoreOf(c, t) is { } tray && world.StationTake(c, t, tray, d);
                    if (took)
                    {
                        if (pad != None)
                            Console.WriteLine($"[station] body {c.Id} part {t}: {StationRovers.Classes[a < n ? docks[a].Cls : 1].Name} {a + 1} fitted a {d.Name} ({d.Mass / 1000f:0.#} t) off its pick pad (part {pad})");
                        world.CraftChanged(c);
                        FitOne(world, c, t, j, k);
                        j.ArmWait = 0;
                        if (j.State != StationState.Building)
                            return true;
                    }
                }
            }
            if (--j.ArmLeft[a] > 0)
            {
                busy = true;
                continue;
            }
            // The trip's end: it stands where its trip left it.
            plan.BaseAt(plan.Total, out var x, out var z, out var h);
            j.RovAt[a] = new Vector2(x, z);
            j.RovH[a] = h;
            j.RovHook[a] = plan.HookAt(plan.Total);
            j.RovSet[a] = !plan.Home;
            j.ArmTicks[a] = 0;
            j.ArmStep[a] = -1;
            j.ArmPad[a] = None;
            j.RovIdle[a] = 0;
            world.StationChanged(c);
        }
        // Idle rovers off their pads: home after a while (at once when the job isn't building).
        var home = j.State != StationState.Building || j.Fitted >= j.Steps;
        for (var a = 0; a < MaxArms; a++)
        {
            if (j.ArmTicks[a] != 0 || !j.RovSet[a])
                continue;
            if (a >= n)
            {
                j.RovSet[a] = false; // (its dock is gone: so is it)
                continue;
            }
            if (home || ++j.RovIdle[a] > (int)(StationRovers.HomeAfter * World.TickRate))
            {
                SendHome(world, c, t, j, docks[..n], n, a);
                busy = true;
                break; // (one plan a tick)
            }
        }
        return busy;
    }

    /// <summary>
    /// Server: the rovers' tick while a build is on. Trips go on and fit; then, once in a look, idle rovers take ready steps in the build
    /// order. True while any rover is on a trip (the station then ticks every tick).
    /// </summary>
    private static bool StepArms(ConstructWorld world, Construct c, int t, StationJob j, CStore tray)
    {
        var busy = TickTrips(world, c, t, j, fit: true);
        if (j.State != StationState.Building)
            return false;
        Gate(j, ArmsBusy(j)); // (every look: its stall is counted in ticks)
        if (j.ArmWait > 0 && (j.ArmWait -= j.ArmGap) > 0)
            return busy; // (counted in ticks: a look may come every tick or every half second)
        Span<StationDock> docks = stackalloc StationDock[MaxArms];
        var n = StationRovers.Docks(c, t, docks);
        var free = false;
        for (var a = 0; a < n; a++)
            free |= j.ArmStep[a] < 0 && (j.ArmTicks[a] == 0 || j.Rov[a].Home);
        if (!free)
        {
            j.ArmWait = busy ? OrderEvery / 2 : OrderEvery;
            return busy;
        }
        if (Assign(world, c, t, j, tray, docks[..n], n))
            return true; // (a trip began: the next look on the next tick)
        j.ArmWait = OrderEvery;
        return busy;
    }

    /// <summary>One new trip: the first ready step (build order) whose best rover is idle. True when one began.</summary>
    private static bool Assign(ConstructWorld world, Construct c, int t, StationJob j, CStore tray, ReadOnlySpan<StationDock> docks, int n)
    {
        var g = GroundOf(c, t, j);
        var looks = 0;
        var order = j.RankOrder.Length == j.PartOf.Length ? j.RankOrder : null;
        for (var i = 0; i < j.PartOf.Length && looks < AssignLooks; i++)
        {
            var k = order != null ? order[i] : i;
            if (j.PartOf[k] != -1 || j.StepKind[k] is not (var kind and >= 0) || Array.IndexOf(j.ArmStep, k) >= 0 || !Ready(j, k))
                continue;
            if (!Gated(j, k))
            {
                if (order != null)
                    break; // (in rank order: every later step waits too)
                continue;
            }
            if (ConstructParts.Get(j.KindDef[kind]) is not { } d)
                continue;
            looks++;
            var laid = j.KindLaid[kind];
            if (laid && !LaidReady(world, c, t, d))
                continue;
            var want = StationRovers.Want(d);
            var place = ArmTarget(c, t, j, k, d);
            int best = -1, bestFrom = -2;
            var bestCost = float.MaxValue;
            Vector3? bestPick = null;
            for (var a = 0; a < n; a++)
            {
                var rc = StationRovers.Classes[docks[a].Cls];
                if (d.Mass > rc.Kg || !StationRovers.Reaches(rc, StationRovers.SpotFor(g, rc, place), place))
                    continue;
                var from = laid ? -3 : PickFrom(world, c, t, j, tray, kind, d, rc.Kg);
                if (from == -2)
                    continue;
                Vector3? pick = from >= 0 ? PadPickAt(world, c, t, j, from, d) : from == -1 ? new Vector3(docks[0].Mid.X, 1.2f, docks[0].Mid.Y) : null;
                if (pick is { } pq && !StationRovers.Reaches(rc, StationRovers.SpotFor(g, rc, pq), pq))
                {
                    // (the piece's own place on the pad is past this rover's reach: it takes it at the pad's edge, as rovers did before
                    // they went to the piece, rather than leave the step for nobody)
                    if (from < 0)
                        continue;
                    var pe = PadEdgeAt(c, t, from, d);
                    if (!StationRovers.Reaches(rc, StationRovers.SpotFor(g, rc, pe), pe))
                        continue;
                    pick = pe;
                }
                PoseAt(j, docks[a], a, 0, out var at, out _);
                var idle = j.ArmStep[a] < 0 && (j.ArmTicks[a] == 0 || j.Rov[a].Home);
                var wait = idle ? 0f : j.ArmLeft[a] / (float)World.TickRate;
                var via = pick is { } pp ? new Vector2(pp.X, pp.Z) : new Vector2(place.X, place.Z);
                var cost = wait + (Vector2.Distance(at, via) + Vector2.Distance(via, new Vector2(place.X, place.Z))) / rc.Speed + 10f
                    + StationRovers.ClassPenalty * Math.Abs(docks[a].Cls - want);
                if (cost < bestCost)
                    (best, bestCost, bestFrom, bestPick) = (a, cost, from, pick);
            }
            if (best < 0 || !(j.ArmStep[best] < 0 && (j.ArmTicks[best] == 0 || j.Rov[best].Home)))
                continue; // (nobody fits it now, or its rover is busy: left for that one)
            if (!Plan(c, t, j, docks, n, best, bestPick, place, d, g))
                continue;
            Begin(world, c, j, best, k, (ushort)d.Id, bestFrom >= 0 ? (ushort)bestFrom : None);
            var turn = Quaternion.Conjugate(BayRot(c, t)) * j.Rot * (j.Plan is { } lp && k < lp.Rot.Length ? lp.Rot[k] : Quaternion.Identity);
            j.Rov[best].Turn = Quaternion.Normalize(turn);
            return true;
        }
        return false;
    }

    private static Quaternion BayRot(Construct c, int t)
    {
        Bay(c, t, out _, out var rot, out _);
        return rot;
    }

    /// <summary>
    /// Rover <paramref name="a"/>'s carry into <c>j.Try</c>, clear of the others: a spot beside along the lane, then a later start (up to
    /// <see cref="StationRovers.MaxWait"/>); an idle rover standing in its way is sent home first. False: no stand reaches.
    /// </summary>
    private static bool Plan(Construct c, int t, StationJob j, ReadOnlySpan<StationDock> docks, int n, int a, Vector3? pick, Vector3 place, ConstructPartDef d, in StationRovers.Ground g)
    {
        var dk = docks[a];
        var rc = StationRovers.Classes[dk.Cls];
        PoseAt(j, dk, a, 0, out var at, out var h);
        var hook = HookNow(j, dk, a);
        var size = d.BoundsMax - d.BoundsMin;
        var src = pick ?? place;
        var s1 = StationRovers.SpotFor(g, rc, src);
        var s2 = StationRovers.SpotFor(g, rc, place);
        var cap = MathF.Min(StationRovers.MaxUp(rc, Vector2.Distance(s1, new Vector2(src.X, src.Z))), StationRovers.MaxUp(rc, Vector2.Distance(s2, new Vector2(place.X, place.Z))));
        var up = MathF.Max(MathF.Min(j.BuiltTop + size.Y + 0.6f, cap), MathF.Max(src.Y, place.Y) + 0.3f);
        var weld = FitTicksFor(d) / (float)World.TickRate;
        var keep = j.ArmTicks[a];
        j.ArmTicks[a] = 0; // (its own trip home is no obstacle to itself)
        try
        {
            var tries = 0;
            for (var w = 0f; w <= StationRovers.MaxWait; w += w < 20 ? 2 : w < 80 ? 5 : 12)
                for (var shift = 0; shift <= (w < 10 ? 4 : 0); shift++)
                {
                    var off = shift == 0 ? 0 : (shift + 1) / 2 * ((shift & 1) == 1 ? 1 : -1);
                    if (!StationRovers.PlanCarry(j.Try, g, dk, at, h, hook, pick, place, up, weld, w, off))
                    {
                        if (shift == 0 && w == 0)
                            return false; // (no stand of its own reaches)
                        continue;
                    }
                    if (++tries > (n > 4 ? 12 : 40))
                        return true; // (enough looking: it goes, as the lab's rare overlaps do)
                    var hit = FirstClash(j, docks, n, a);
                    if (hit < 0)
                        return true;
                }
            return StationRovers.PlanCarry(j.Try, g, dk, at, h, hook, pick, place, up, weld, 0, 0);
        }
        finally
        {
            j.ArmTicks[a] = keep;
        }
    }

    /// <summary>
    /// Server: a rover is numbered by its dock's place in the list, so a dock gone from the middle (taken off, shot away) moves the rest
    /// onto other docks: each rover whose dock part changed stops where it is told (its trip and rest pose dropped, its step free again).
    /// </summary>
    private static void DockSwaps(ConstructWorld world, Construct c, StationJob j, ReadOnlySpan<StationDock> docks, int n)
    {
        for (var a = 0; a < MaxArms; a++)
        {
            var part = a < n ? docks[a].Part : None;
            if (j.RovDock[a] == part)
                continue;
            if (j.ArmTicks[a] != 0 || j.RovSet[a])
            {
                j.ArmTicks[a] = 0;
                j.ArmStep[a] = -1;
                j.ArmPad[a] = None;
                j.RovSet[a] = false;
                j.RovIdle[a] = 0;
                world.StationChanged(c);
            }
            j.RovDock[a] = part;
        }
    }

    private static bool ArmsBusy(StationJob j)
    {
        foreach (var k in j.ArmStep)
            if (k >= 0)
                return true;
        return false;
    }

    /// <summary>Every rover's step dropped (a job starts, stops or is done fitting): one carrying a piece drives home with it.</summary>
    private static void ResetArms(ConstructWorld world, Construct c, StationJob j)
    {
        j.ArmWait = 0;
        for (var a = 0; a < MaxArms; a++)
        {
            if (j.ArmStep[a] < 0)
                continue;
            j.ArmStep[a] = -1;
            j.ArmPad[a] = None;
            j.ArmDef[a] = None;
            world.StationChanged(c);
        }
        // (the trips themselves run on: TickTrips sends each rover home when it stands idle and the job isn't building)
    }

    // ---- The wire (CBodyKind.Station, protocol 468) ------------------------------------------------------------------------

    /// <summary>Every rover: where it stands, and its trip when it is on one (with how far into it, for a joiner).</summary>
    private static void WriteArms(PacketWriter w, StationJob j)
    {
        var n = 0;
        for (var a = 0; a < MaxArms; a++)
            if (j.ArmTicks[a] > 0 || j.RovSet[a])
                n = a + 1;
        w.U8((byte)n);
        for (var a = 0; a < n; a++)
        {
            var on = j.ArmTicks[a] > 0;
            var pad = on && j.ArmStep[a] >= 0 ? j.ArmPad[a] : None; // (protocol 478: the pick pad it carries its piece off, so a client's pad hides it)
            w.U8((byte)((on ? 1 : 0) | (j.RovSet[a] ? 2 : 0) | (j.ArmStep[a] >= 0 ? 4 : 0) | (pad != None ? 8 : 0))).U8(j.ArmSeq[a]);
            if (pad != None)
                w.U16(pad);
            if (j.RovSet[a])
            {
                w.Cm(j.RovAt[a].X).Cm(j.RovAt[a].Y).U16(Angle(j.RovH[a]));
                w.CmVec(j.RovHook[a]);
            }
            if (!on)
                continue;
            var p = j.Rov[a];
            w.U16(j.ArmTicks[a]).U16((ushort)(j.ArmTicks[a] - j.ArmLeft[a]));
            ConstructStorage.WriteDef(w, j.ArmStep[a] >= 0 ? j.ArmDef[a] : None);
            w.U8((byte)(p.Home ? 1 : 0)).U16(Ticks(p.GripT)).U16(Ticks(p.FitT)).Cm(p.Up);
            w.U16(Q(p.Turn.X)).U16(Q(p.Turn.Y)).U16(Q(p.Turn.Z)).U16(Q(p.Turn.W));
            w.U8((byte)p.NB);
            for (var k = 0; k < p.NB; k++)
                w.U16(Ticks(p.Bt[k])).Cm(p.Bx[k]).Cm(p.Bz[k]).U16(Angle(p.Bh[k]));
            w.U8((byte)p.NH);
            for (var k = 0; k < p.NH; k++)
                w.U16(Ticks(p.Ht[k])).CmVec(p.Ho[k]);
        }
    }

    private static void ReadArms(ref PacketReader r, StationJob? j)
    {
        int n = r.U8();
        if (j != null)
            for (var a = n; a < MaxArms; a++)
                (j.ArmTicks[a], j.RovSet[a], j.ArmPad[a]) = (0, false, None);
        for (var a = 0; a < n; a++)
        {
            var flags = r.U8();
            var seq = r.U8();
            var pad = (flags & 8) != 0 ? r.U16() : None;
            Vector2 at = default;
            float h = 0;
            Vector3 hook = default;
            if ((flags & 2) != 0)
            {
                at = new Vector2(r.Cm(), r.Cm());
                h = FromAngle(r.U16());
                hook = r.CmVec();
            }
            var mine = j != null && a < MaxArms;
            if (mine)
                (j!.RovSet[a], j.RovAt[a], j.RovH[a], j.RovHook[a]) = ((flags & 2) != 0, at, h, hook);
            if (mine)
                j!.ArmPad[a] = pad;
            if ((flags & 1) == 0)
            {
                if (mine)
                    j!.ArmTicks[a] = 0;
                continue;
            }
            var ticks = r.U16();
            var age = r.U16();
            var def = ConstructStorage.ReadDef(ref r);
            var p = mine ? j!.Rov[a] : new RoverPlan();
            p.Clear();
            p.Home = r.U8() != 0;
            p.GripT = r.U16() / (float)World.TickRate;
            p.FitT = r.U16() / (float)World.TickRate;
            p.Up = r.Cm();
            p.Turn = Quaternion.Normalize(new Quaternion(UnQ(r.U16()), UnQ(r.U16()), UnQ(r.U16()), UnQ(r.U16())));
            int nb = r.U8();
            for (var k = 0; k < nb; k++)
                p.AddBase(r.U16() / (float)World.TickRate, r.Cm(), r.Cm(), FromAngle(r.U16()));
            int nh = r.U8();
            for (var k = 0; k < nh; k++)
                p.AddHook(r.U16() / (float)World.TickRate, r.CmVec());
            p.Total = ticks / (float)World.TickRate;
            if (!mine)
                continue;
            j!.ArmTicks[a] = ticks;
            j.ArmDef[a] = def;
            if (j.ArmSeen[a] != seq || j.ArmStart[a] == 0)
            {
                j.ArmSeen[a] = seq;
                j.ArmStart[a] = ConstructConveyor.Clock - age / (double)World.TickRate;
            }
            j.ArmSeq[a] = seq;
        }
    }

    private static ushort Ticks(float s) => (ushort)Math.Clamp((int)MathF.Round(s * World.TickRate), 0, ushort.MaxValue);
    private static ushort Angle(float h) => (ushort)(int)MathF.Round(StationRovers.Wrap(h) / (2 * MathF.PI) * 65536f + 32768f);
    private static float FromAngle(ushort v) => (v - 32768f) / 65536f * 2 * MathF.PI;
    private static ushort Q(float v) => (ushort)Math.Clamp((int)MathF.Round(v * 32767f) + 32768, 0, 65535);
    private static float UnQ(ushort v) => (v - 32768) / 32767f;
}
