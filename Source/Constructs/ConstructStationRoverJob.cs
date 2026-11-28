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
    /// goes no earlier than its parent's rank, unless it may go on before its parent (an early fit: ConstructStationEarlyFit.cs). Also the build's footprint in the bay (the rovers' keep-out ring).
    /// </summary>
    private static void BuildOrder(Construct c, int t, StationJob j)
    {
        var bp = j.Bp!;
        var n = bp.Steps.Count;
        j.StepRank = new int[n];
        j.RankOrder = new int[n];
        (j.GateRank, j.GateStall, j.BuiltTop) = (0, 0, 0f);
        j.HeldStale = true;
        if (j.Plan is not { } plan || plan.Centre.Length != n)
        {
            for (var k = 0; k < n; k++)
                j.RankOrder[k] = k;
            j.KeepLo = j.KeepHi = Vector2.Zero;
            j.StepFoot = [];
            j.StepLo = j.StepHi = [];
            return;
        }
        Bay(c, t, out var floor, out var rot, out _);
        var inv = Quaternion.Conjugate(rot);
        var los = new float[n];
        var his = new float[n];
        var floors = new List<float>();
        Vector2 klo = new(float.MaxValue), khi = new(float.MinValue);
        var defs = new ConstructPartDef?[n];
        j.StepFoot = new Vector4[n];
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
            j.StepFoot[k] = new Vector4(m.X - e.X, m.Z - e.Z, m.X + e.X, m.Z + e.Z);
            klo = Vector2.Min(klo, new Vector2(m.X - e.X, m.Z - e.Z));
            khi = Vector2.Max(khi, new Vector2(m.X + e.X, m.Z + e.Z));
            if (d.Name.Contains("Deck", StringComparison.OrdinalIgnoreCase) && d.Role is PartRole.Frame or PartRole.Panel && e.X * e.Z >= 1f)
                floors.Add(his[k]);
        }
        (j.KeepLo, j.KeepHi) = klo.X <= khi.X ? (klo, khi) : (Vector2.Zero, Vector2.Zero);
        (j.StepLo, j.StepHi) = (los, his);
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
            if (plan.Free.Length == n && plan.Free[k])
            {
                // (it may go on before its parent, standing on what is under it: its height alone ranks it, Sean 2026-11-27, "build
                // lowest up": the Zero Day's Keel 3 and 4 hang on the Lower Deck Aft in its blueprint and so waited for that deck)
                j.StepRank[k] = r;
                j.RankOrder[k] = k;
                continue;
            }
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

    /// <summary>
    /// Would step <paramref name="k"/>, of a rank past the lowest still open (<see cref="StationJob.OpenRank"/>: it goes on ahead of it,
    /// by the stall or as a piece at hand), cover a step of a lower rank still to fit: one whose footprint it overlaps and whose top lies
    /// no higher than its own bottom (a chair under a deck)? Then it waits, so no arm has to reach in under it later (Sean 2026-11-28:
    /// "if you're putting chairs on the first deck and then put the second deck on, how would the arm reach in"). Bounded: one pass over
    /// the steps for each step tried, and only past the open rank.
    /// </summary>
    private static bool Covers(StationJob j, int k)
    {
        if (j.StepRank.Length != j.PartOf.Length || j.StepRank[k] <= j.OpenRank || j.StepFoot.Length != j.PartOf.Length || j.StepLo.Length != j.PartOf.Length)
            return false;
        var f = j.StepFoot[k];
        var lo = j.StepLo[k] + 0.3f;
        for (var s = 0; s < j.PartOf.Length; s++)
        {
            if (s == k || j.PartOf[s] != -1 || j.StepRank[s] >= j.StepRank[k] || j.StepHi[s] > lo || ConstructStationTree.External(j, s))
                continue;
            var e = j.StepFoot[s];
            if (e.X < f.Z - 0.05f && e.Z > f.X + 0.05f && e.Y < f.W - 0.05f && e.W > f.Y + 0.05f)
                return true;
        }
        return false;
    }

    /// <summary>A step went on (whoever fitted it): the gate's stall clock starts again and the carry height learns the build's top.</summary>
    private static void Fitted(Construct c, int t, StationJob j, int k, ConstructPartDef? d)
    {
        j.GateStall = 0;
        HeldOn(j, k); // (what it holds up may go on early)
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
        j.OpenRank = open;
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
                ok = StationRovers.Reaches(rc, StationRovers.StandFor(g, rc, place), place);
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
        float hx = bay.X * 0.5f, hz = bay.Z * 0.5f;
        RoverFloor? floor = null;
        if (j.StepFoot.Length > 0 && j.StepFoot.Length == j.PartOf.Length)
        {
            floor = j.Floor ??= new RoverFloor();
            if (!floor.Fresh(j, hx, hz))
                floor.Make(j, hx, hz); // (again at each fit: what is built is what the rovers keep off)
        }
        // (the keep-out ring is what is fitted so far, not the finished build: Sean 2026-11-28; a job with no plan keeps the whole footprint)
        if (floor != null)
            return new StationRovers.Ground { Hx = hx, Hz = hz, KLo = floor.KLo, KHi = floor.KHi, Keep = floor.Any, Floor = floor };
        return new StationRovers.Ground { Hx = hx, Hz = hz, KLo = j.KeepLo, KHi = j.KeepHi, Keep = j.KeepHi.X > j.KeepLo.X, Floor = floor };
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

    /// <summary>
    /// The first rover the trip in <c>j.Try</c> for rover <paramref name="a"/> would meet (-1: none), sampled a second apart: their bodies,
    /// and (Sean 2026-11-28, "keep their pieces from clipping through each other") the piece it carries, of half length
    /// <paramref name="reach"/> hanging <paramref name="hang"/> under the jaws, against another's carried piece at a height they share or
    /// against another rover's chassis and turret under it.
    /// </summary>
    private static int FirstClash(StationJob j, ReadOnlySpan<StationDock> docks, int n, int a, bool moving = false, float reach = 0, float hang = 0)
    {
        // (a second apart first, which finds most clashes cheaply; then, clear at that, every quarter second, so a near miss between two
        // samples isn't missed: Sean 2026-11-28, a trip never goes through another rover or its piece)
        var hit = ClashEvery(j, docks, n, a, moving, reach, hang, 1f);
        return hit >= 0 ? hit : ClashEvery(j, docks, n, a, moving, reach, hang, ClashStep);
    }

    public const float ClashStep = 0.25f; // s between the fine clash check's samples

    private static int ClashEvery(StationJob j, ReadOnlySpan<StationDock> docks, int n, int a, bool moving, float reach, float hang, float step)
    {
        var p = j.Try;
        for (var u = 0f; u <= p.Total + 0.01f; u += step)
        {
            p.BaseAt(u, out var x, out var z, out _);
            var me = new Vector2(x, z);
            var carry = reach > 0 && u >= p.GripT && u < p.FitT;
            var hk = carry ? p.HookAt(u) : default;
            var mine = me + new Vector2(hk.X, hk.Z);
            for (var b = 0; b < n; b++)
            {
                if (b == a || moving && j.ArmTicks[b] == 0)
                    continue;
                PoseAt(j, docks[b], b, u, out var ob, out _);
                // (a rover parked on its dock is its body's box there, not a circle: one leaving the dock beside it, 2 m off, passes it, and
                // with no going through any more it waited for good, Sean 2026-11-28)
                if (j.ArmTicks[b] == 0 && !j.RovSet[b]
                        ? StationRovers.ParkedClash(docks[b], me, StationRovers.Classes[docks[a].Cls].HalfWid + 0.15f)
                        : StationRovers.Clash(docks[a], me, docks[b], ob, 0.3f))
                    return b;
                var other = PieceAt(j, b, u, ob, out var op, out var oy, out var oReach, out var oHang);
                var parked = j.ArmTicks[b] == 0 && !j.RovSet[b]; // (on its dock, off the bay: a piece swung past it is no clash)
                if (carry && !parked)
                {
                    var rcb = StationRovers.Classes[docks[b].Cls];
                    if (hk.Y - hang < rcb.Shoulder + 1f && Vector2.Distance(mine, ob) < reach + rcb.Radius + 0.3f)
                        return b; // (its piece low through the other's chassis)
                    if (other && Vector2.Distance(mine, op) < reach + oReach + 0.3f && hk.Y - hang < oy + 0.2f && oy - oHang < hk.Y + 0.2f)
                        return b; // (the two pieces at one height, crossing)
                }
                if (other && oy - oHang < StationRovers.Classes[docks[a].Cls].Shoulder + 1f && Vector2.Distance(op, me) < oReach + StationRovers.Classes[docks[a].Cls].Radius + 0.3f)
                    return b; // (the other's piece low through this one's chassis)
            }
        }
        return -1;
    }

    /// <summary>Rover <paramref name="b"/>'s carried piece <paramref name="ahead"/> s from now (it standing at <paramref name="ob"/>): where it hangs, its jaws' height, its half length and how deep it hangs. False: it carries none then.</summary>
    private static bool PieceAt(StationJob j, int b, float ahead, Vector2 ob, out Vector2 at, out float y, out float reach, out float hang)
    {
        (at, y, reach, hang) = (ob, 0, 0, 0);
        if (j.ArmTicks[b] == 0 || j.ArmStep[b] < 0 || ConstructParts.Get(j.ArmDef[b]) is not { } d)
            return false;
        var p = j.Rov[b];
        var el = (j.ArmTicks[b] - j.ArmLeft[b]) / (float)World.TickRate + ahead;
        if (el < p.GripT || el >= p.FitT)
            return false;
        var hk = p.HookAt(el);
        var size = d.BoundsMax - d.BoundsMin;
        (at, y, reach, hang) = (ob + new Vector2(hk.X, hk.Z), hk.Y, 0.5f * MathF.Max(size.X, size.Z), StationRovers.HangDepth(size, p.Turn, 0));
        return true;
    }

    /// <summary>
    /// The drawn trip kept on the ticked one: a rover is drawn by this peer's clock from its trip's start (<c>ArmStart</c>), and its piece
    /// goes on after the trip's ticks. When the world ticks slower than the clock (a big works' tick over its budget), the drawn rover got
    /// ahead: it stood over its place with nothing happening, and the piece went on seconds later while it was drawn elsewhere (Sean
    /// 2026-11-27). The start is eased towards the ticked time each tick, by less than a tick's time a tick when the drawn rover is ahead
    /// (it slows, almost to a stop, never going back along its way: after a pause, an unpowered spell or a big fit it waits for the ticks)
    /// and by at most a tick when it is behind. A joined client hears the server only at station events: its start is set again from a
    /// message more than 0.25 s out (ReadArms), so between messages it can still run ahead on a slow server.
    /// </summary>
    private static void DrawnPace(StationJob j, int a, int el)
    {
        var ticked = el / (double)World.TickRate;
        var ahead = ConstructConveyor.Clock - j.ArmStart[a] - ticked;
        if (Math.Abs(ahead) > 0.02)
            j.ArmStart[a] += Math.Clamp(ahead * 0.2, -1.0 / World.TickRate, 0.9 / World.TickRate);
    }

    /// <summary>A fitted part's top middle against where its rover put it (bay frame): logged when they are apart, since the two must agree.</summary>
    private static void FitDrift(ConstructWorld world, Construct c, int t, StationJob j, int k, ConstructPartDef d, Vector3 aim, int a)
    {
        if (k >= j.PartOf.Length || j.PartOf[k] < 0 || world.Get(j.Body) is not { } b || j.PartOf[k] >= b.PartCount)
            return;
        var i = j.PartOf[k];
        ref readonly var p = ref b.Parts[i];
        if (p.Seg != 0)
            return; // (a freeform weld's part sits in its own segment's frame: not measured here)
        var pd = b.DefOf(i);
        var q = b.Rot * Rot24.Quat(p.Rot);
        var mid = b.Pos + Vector3.Transform(p.Pos + Vector3.Transform((pd.BoundsMin + pd.BoundsMax) * 0.5f, Rot24.Quat(p.Rot)), b.Rot);
        var h = (pd.BoundsMax - pd.BoundsMin) * 0.5f;
        var up = MathF.Abs(Vector3.Transform(Vector3.UnitX, q).Y) * h.X + MathF.Abs(Vector3.Transform(Vector3.UnitY, q).Y) * h.Y + MathF.Abs(Vector3.Transform(Vector3.UnitZ, q).Y) * h.Z;
        Bay(c, t, out var floor, out var rot, out _);
        var got = Vector3.Transform(mid + Vector3.UnitY * up - floor, Quaternion.Conjugate(rot));
        var off = Vector3.Distance(got, aim);
        if (off > 0.25f)
            Console.WriteLine($"[station] body {c.Id} part {t}: step {k} ({d.Name}) went on {off:0.0} m from where rover {a + 1} put it (aim {aim.X:0.0},{aim.Y:0.0},{aim.Z:0.0}; fitted {got.X:0.0},{got.Y:0.0},{got.Z:0.0})");
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
        j.ArmSetOut[a] = j.ArmStart[a];
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
        for (var w = 0f; w <= 60f; w += 2f)
        {
            // (it waits only for rovers on a trip: no wait clears one standing still, and it used to wait the whole minute for that)
            StationRovers.PlanHome(j.Try, g, dk, at, h, hook, w);
            if (FirstClash(j, docks, n, a, moving: true) < 0 || w >= 60f)
                break;
        }
        j.ArmTicks[a] = keep;
        Begin(world, c, j, a, -1, None, None);
    }

    private const int JobLook = 30; // ticks between an idle rover's looks for a step of its own

    /// <summary>
    /// Has idle rover <paramref name="a"/> a job coming: a ready step nobody carries whose piece it can lift (any idle rover that lifts a
    /// piece may take it, Assign)? None: it drives home at once rather than stand in the bay (Sean 2026-11-27: "unless the idle rover
    /// knows it will have a job soon"). Bounded: one pass over the steps every <see cref="JobLook"/> ticks a standing rover.
    /// </summary>
    private static bool JobSoon(StationJob j, ReadOnlySpan<StationDock> docks, int n, int a)
    {
        if (j.Bp == null)
            return false;
        var cls = docks[a].Cls;
        var kg = StationRovers.Classes[cls].Kg;
        for (var k = 0; k < j.PartOf.Length; k++)
        {
            if (j.PartOf[k] != -1 || j.StepKind[k] is not (var kind and >= 0) || Array.IndexOf(j.ArmStep, k) >= 0 || !Ready(j, k))
                continue;
            if (ConstructParts.Get(j.KindDef[kind]) is not { } d || d.Mass > kg)
                continue;
            return true; // (any idle rover that lifts it may take it: Assign, Sean 2026-11-28)
        }
        return false;
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
            DrawnPace(j, a, el);
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
                        var aim = ArmTarget(c, t, j, k, d);
                        FitOne(world, c, t, j, k);
                        FitDrift(world, c, t, j, k, d, aim, a);
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
            var idle = ++j.RovIdle[a];
            if (home || idle > (int)(StationRovers.HomeAfter * World.TickRate) || idle % JobLook == 1 && !JobSoon(j, docks[..n], n, a))
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
        var order = j.RankOrder.Length == j.PartOf.Length ? j.RankOrder : null;
        // First the build order's open ranks; then, when none of those can go now, any later step whose piece is at hand and that has
        // something under it (Sean, 2026-11-27: the station sat waiting for a deck piece while it could have fitted others): lowest first.
        return AssignPass(world, c, t, j, tray, docks, n, order, false) || order != null && AssignPass(world, c, t, j, tray, docks, n, order, true);
    }

    private static bool AssignPass(ConstructWorld world, Construct c, int t, StationJob j, CStore tray, ReadOnlySpan<StationDock> docks, int n, int[]? order, bool ahead)
    {
        var g = GroundOf(c, t, j);
        var looks = 0;
        for (var i = 0; i < j.PartOf.Length && looks < AssignLooks; i++)
        {
            var k = order != null ? order[i] : i;
            if (j.PartOf[k] != -1 || j.StepKind[k] is not (var kind and >= 0) || Array.IndexOf(j.ArmStep, k) >= 0 || !Ready(j, k))
                continue;
            if (Gated(j, k) == ahead)
            {
                if (order != null && !ahead)
                    break; // (in rank order: every later step waits for the second pass)
                continue;
            }
            if (ahead && j.KindLaid[kind])
                continue; // (laid fittings keep the order)
            if (Covers(j, k))
                continue; // (it would go on over a lower piece still to come: a deck over a missing chair, Sean 2026-11-28)
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
                // Any idle rover that lifts it takes it (Sean 2026-11-28: a step never waits for a busy rover of a better size; the size
                // only breaks near ties between idle ones, ClassPenalty).
                if (!(j.ArmStep[a] < 0 && (j.ArmTicks[a] == 0 || j.Rov[a].Home)))
                    continue;
                var rc = StationRovers.Classes[docks[a].Cls];
                if (d.Mass > rc.Kg || !StationRovers.Reaches(rc, StationRovers.StandFor(g, rc, place), place))
                    continue;
                PoseAt(j, docks[a], a, 0, out var at, out _);
                Vector3? pick = null;
                var from = -3;
                if (!laid)
                {
                    from = PickNear(world, c, t, j, tray, kind, d, rc, g, at, place, out var pq);
                    if (from == -2)
                        continue;
                    pick = from == -1 ? new Vector3(docks[0].Mid.X, 1.2f, docks[0].Mid.Y) : pq;
                }
                var via = pick is { } pp ? new Vector2(pp.X, pp.Z) : new Vector2(place.X, place.Z);
                var cost = (Vector2.Distance(at, via) + Vector2.Distance(via, new Vector2(place.X, place.Z))) / rc.Speed + 10f
                    + StationRovers.ClassPenalty * Math.Abs(docks[a].Cls - want);
                if (cost < bestCost)
                    (best, bestCost, bestFrom, bestPick) = (a, cost, from, pick);
            }
            if (best < 0 || !(j.ArmStep[best] < 0 && (j.ArmTicks[best] == 0 || j.Rov[best].Home)))
                continue; // (nobody fits it now, or its rover is busy: left for that one)
            var turn = Quaternion.Normalize(Quaternion.Conjugate(BayRot(c, t)) * j.Rot * (j.Plan is { } lp && k < lp.Rot.Length ? lp.Rot[k] : Quaternion.Identity));
            var planned = Plan(world, c, t, j, docks, n, best, bestPick, place, d, g, turn);
            if (planned == PlanBlocked)
                return false; // (no clear trip yet: it waits for the way to clear, and the next look tries again; one blocked step a look)
            if (planned != PlanOk)
                continue;
            Begin(world, c, j, best, k, (ushort)d.Id, bestFrom >= 0 ? (ushort)bestFrom : None);
            j.Rov[best].Turn = turn;
            return true;
        }
        return false;
    }

    private static Quaternion BayRot(Construct c, int t)
    {
        Bay(c, t, out _, out var rot, out _);
        return rot;
    }

    private const int PlanOk = 0, PlanNoReach = 1, PlanBlocked = 2;
    public const int PlanShifts = 6; // stands slid along the lane at once (1 to 3 rover widths each way), before any later start
    public const int PlanTries = 48; // clash checks one plan may make: the slides, then every later start up to MaxWait

    /// <summary>
    /// Rover <paramref name="a"/>'s carry into <c>j.Try</c>, clear of the others: an idle rover standing in its way is sent home first;
    /// then a spot beside along the lane, then a later start (up to <see cref="StationRovers.MaxWait"/>). It never goes through another
    /// rover or its piece (Sean 2026-11-28): with no clear trip in <see cref="PlanTries"/> it waits (<see cref="PlanBlocked"/>) and the
    /// next look tries again. The carry height clears only the fitted parts along the piece's way (<see cref="PathTop"/>).
    /// </summary>
    private static int Plan(ConstructWorld world, Construct c, int t, StationJob j, ReadOnlySpan<StationDock> docks, int n, int a, Vector3? pick, Vector3 place,
        ConstructPartDef d, in StationRovers.Ground g, Quaternion turn)
    {
        var dk = docks[a];
        var rc = StationRovers.Classes[dk.Cls];
        PoseAt(j, dk, a, 0, out var at, out var h);
        var hook = HookNow(j, dk, a);
        var size = d.BoundsMax - d.BoundsMin;
        var src = pick ?? place;
        var s1 = StationRovers.StandFor(g, rc, src);
        var s2 = StationRovers.StandFor(g, rc, place);
        var cap = MathF.Min(StationRovers.MaxUp(rc, Vector2.Distance(s1, new Vector2(src.X, src.Z))), StationRovers.MaxUp(rc, Vector2.Distance(s2, new Vector2(place.X, place.Z))));
        // The carry height (Sean 2026-11-28: pieces kept out of the floor and what is built): the piece's lowest point, as it hangs turned
        // and tilted at the top of its swing (StationRender), clears the highest fitted part by 0.6 m, as high as the arm lifts it there.
        var tilt = StationRovers.TiltOf(size);
        var hang = MathF.Max(StationRovers.HangDepth(size, Quaternion.Identity, tilt), StationRovers.HangDepth(size, turn, tilt));
        var low = MathF.Max(src.Y, place.Y) + 0.3f;
        var up = MathF.Max(MathF.Min(j.BuiltTop + hang + 0.6f, cap), low);
        var weld = StationRovers.DropSeconds; // (once over its place a rover sets the piece down at once: the drive there is the time)
        var half = 0.5f * MathF.Max(size.X, size.Z);
        var keep = j.ArmTicks[a];
        j.ArmTicks[a] = 0; // (its own trip home is no obstacle to itself)
        try
        {
            if (!StationRovers.PlanCarry(j.Try, g, dk, at, h, hook, pick, place, up, weld, 0, 0))
                return PlanNoReach; // (no stand of its own reaches)
            // (then only as high as the fitted parts under the piece's way need, Sean 2026-11-28: the way at the build's top, its parts
            // looked up under it; the way's plan lies the same at any height)
            if (g.Floor != null)
                up = MathF.Max(MathF.Min(PathTop(j.Try, g.Floor, half, j.BuiltTop) + hang + 0.6f, cap), low);
            var tries = 0;
            var sent = 0;
            var limit = PlanTries;
            for (var w = 0f; w <= StationRovers.MaxWait; w += w < 20 ? 2 : w < 80 ? 5 : 12)
                for (var shift = 0; shift <= (w == 0 ? PlanShifts : 0); shift++)
                {
                    var off = shift == 0 ? 0 : (shift + 1) / 2 * ((shift & 1) == 1 ? 1 : -1);
                    if (!StationRovers.PlanCarry(j.Try, g, dk, at, h, hook, pick, place, up, weld, w, off))
                        continue;
                    if (++tries > limit)
                    {
                        return PlanBlocked;
                    }
                    var hit = FirstClash(j, docks, n, a, reach: half, hang: hang);
                    if (hit < 0)
                        return PlanOk;
                    if (w == 0 && j.ArmTicks[hit] == 0 && j.RovSet[hit] && j.ArmStep[hit] < 0 && sent < n)
                    {
                        // (an idle rover standing in its way goes home first, Sean 2026-11-28: then the way is planned again, round its trip)
                        sent++;
                        SendHome(world, c, t, j, docks, n, hit);
                        shift--;
                    }
                }
            return PlanBlocked;
        }
        finally
        {
            j.ArmTicks[a] = keep;
        }
    }

    /// <summary>
    /// The highest top of a fitted part under the piece's way on trip <paramref name="p"/> (from its grip to its fit, a quarter second
    /// apart), the piece's half length <paramref name="half"/> and half a metre round each point; <paramref name="none"/> for a trip with
    /// no carry. Allocates nothing.
    /// </summary>
    private static float PathTop(RoverPlan p, RoverFloor f, float half, float none)
    {
        if (p.FitT <= p.GripT)
            return none;
        var top = float.MinValue;
        for (var u = p.GripT; u <= p.FitT + 0.01f; u += ClashStep)
        {
            p.BaseAt(u, out var x, out var z, out _);
            var hk = p.HookAt(u);
            top = MathF.Max(top, f.TopNear(new Vector2(x + hk.X, z + hk.Z), half + 0.5f));
        }
        return top; // (float.MinValue: nothing fitted under its way)
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
            w.U64(p.Smooth); // (protocol 490: the keys it drives on through, a curve's)
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
            p.Smooth = r.U64() & (p.NB >= 64 ? ulong.MaxValue : (1UL << p.NB) - 1);
            int nh = r.U8();
            for (var k = 0; k < nh; k++)
                p.AddHook(r.U16() / (float)World.TickRate, r.CmVec());
            p.Total = ticks / (float)World.TickRate;
            if (!mine)
                continue;
            j!.ArmTicks[a] = ticks;
            j.ArmDef[a] = def;
            var start = ConstructConveyor.Clock - age / (double)World.TickRate;
            if (j.ArmSeen[a] != seq || j.ArmStart[a] == 0 || Math.Abs(j.ArmStart[a] - start) > 0.25)
            {
                if (j.ArmSeen[a] != seq || j.ArmStart[a] == 0)
                    j.ArmSetOut[a] = start; // (the order off a pad: set once a trip)
                j.ArmSeen[a] = seq;
                j.ArmStart[a] = start; // (and again, from any message, when the server's tick has fallen behind this clock: DrawnPace)
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
