using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>Where one use of a Building Station's blueprint stands (docs/sandbox/design/sub-blueprints.md, section 3).</summary>
public enum UseState : byte
{
    Waiting, // no station has it yet
    Building, // a sub-station costs and builds it
    Done, // built: waiting to be carried to this bay
    Fitting, // the big fit: drawn to its spot and welded
    InPlace, // fitted (a missing part is ordered and fitted here as an ordinary step: the user's answer 5)
    Own, // nothing took it in time (or Cancel use): this station builds it itself, part by part
}

/// <summary>One use of a root job: its tag in the flat blueprint, who builds it, its body when done. Clients hold what the wire says.</summary>
public sealed class UseRow
{
    public int Tag; // its tag in the job's flat blueprint (Bp.Map)
    public string Name = "";
    public int Number; // "#2": its count among uses of the same blueprint
    public UseState State;
    public ushort Station = ushort.MaxValue; // the sub-station's part on this body
    public ushort Body; // its built body (Done, Fitting)
    public ushort Fitted, Steps; // its sub-job's count (clients: the tree's line)
    public int Looks; // the root's looks while Waiting: 60 (30 s) and it builds it itself
    public bool Touch; // its sub-station's bay touches this one's: handed over
    public bool Delivered; // fitted from a delivered body: its own links were laid by its own build
    public ushort Linked; // offered over a link: its ledger line (LinkedLogisticsSub.cs; 0: none)
    public string Note = "";
    public readonly HashSet<ushort> Skipped = new(); // stations whose sub-job failed for it
}

public sealed partial class StationJob
{
    // ---- Sub-blueprints (ConstructStationTree.cs). Both peers hold Tree's rows and Header; the rest is the server's.
    public UseRow[]? Tree; // a root's uses (null: none)
    public string Header = ""; // a sub-job's line: "for Station A: Frigate, Turret #2 - ..."
    public byte Role; // 0: an ordinary job (a root when Tree is set); 1: a sub-job, built for another station
    public int[]? StepRow; // each flat step's row (-1: the parent's own)
    public uint Stamp; // this job's own stamp (a sub-job names its root by body, part and stamp)
    public string UseId = ""; // a sub-job's use: its blueprint's id (a shipped one's flat blueprint carries none)
    public uint OrderAge; // the age stamp its orders rank by (world.OrderStamp at a root's Start; a sub-job takes its root's)
    public ushort RootBody, RootPart;
    public uint RootStamp;
    public int RootUse; // its row on the root
    public string Pinned = ""; // the top root's blueprint packed (its tree's versions as built): a load builds the same tree
    public int[] PinPath = []; // a sub-job's way down that tree: its row at each level
    public ConstructBuild.Blueprint? PinnedBp;
    public Task<ConstructBuild.Blueprint?>? Resolving; // Start's resolve and flatten, off the game thread
    public string ResolveWhy = "";
    // The big fit: one use's body drawn to its spot, then merged.
    public int BigRow = -1, BigTicks, BigTotal;
    public byte BigTurn;
    public Vector3 BigOffset, BigFromPos, BigToPos;
    public Quaternion BigFromRot, BigToRot;
    public int[] BigMatch = []; // its body's part i is flat step BigMatch[i]
}

/// <summary>
/// A Building Station's job tree (docs/sandbox/design/sub-blueprints.md): a blueprint's uses ordered from other stations on the same
/// construct as sub-jobs, built there and brought to its bay (the bays touch, or a player moves it), then fitted as one big step. A
/// use nothing takes for 30 s is built here, part by part. Server, at the station's 2 Hz look; the big fit on its fit timer.
/// </summary>
public static partial class ConstructStationTree
{
    public const int TakeLooks = 60; // 30 s at 2 Hz: then the root builds the use itself (the user's answer 1)
    public const float FitBase = 5f, FitPerTonne = 1f, FitMax = 60f; // the big fit's time, s (the user's answer 3)
    public const float HandReach = 8f; // a body set down this near the root's bay is taken
    public const int MaxCandidates = 16;
    public const int MaxRowsSent = 64;
    internal const ushort None = ushort.MaxValue;
    private static uint _stamp;

    public static uint NewStamp() => Interlocked.Increment(ref _stamp) is var s and not 0 ? s : Interlocked.Increment(ref _stamp);

    /// <summary>A root job's rows, one per top-level use of its flat blueprint, and each step's row.</summary>
    public static void Begin(StationJob j)
    {
        j.Tree = null;
        j.StepRow = null;
        if (j.Bp?.Map is not { } map)
            return;
        var rows = new List<UseRow>();
        var count = new Dictionary<string, int>();
        foreach (var t in map.Top())
        {
            var tag = map.Tags[t];
            count[tag.Id] = count.GetValueOrDefault(tag.Id) + 1;
            rows.Add(new UseRow { Tag = t, Name = tag.Name, Number = count[tag.Id], Steps = (ushort)Math.Min(None, tag.Count) });
        }
        j.Tree = rows.ToArray();
        j.StepRow = new int[j.Bp.Steps.Count];
        Array.Fill(j.StepRow, -1);
        for (var r = 0; r < rows.Count; r++)
        {
            var tag = map.Tags[rows[r].Tag];
            for (var k = tag.First; k < tag.First + tag.Count && k < j.StepRow.Length; k++)
                j.StepRow[k] = r;
        }
    }

    /// <summary>Is step <paramref name="k"/> another station's to build (a use not in place and not built here)?</summary>
    public static bool External(StationJob j, int k)
    {
        if (j.StepRow == null || j.Tree == null || k >= j.StepRow.Length || j.StepRow[k] < 0)
            return false;
        return j.Tree[j.StepRow[k]].State is not (UseState.InPlace or UseState.Own);
    }

    // ---- The root's look (2 Hz) ---------------------------------------------------------------------------------------------

    /// <summary>Server: the root's look at its uses: dispatch, watch, take what was delivered, and build itself what nothing took.</summary>
    public static void Look(ConstructWorld world, Construct c, int t, StationJob j)
    {
        if (j.Tree == null || j.BigRow >= 0)
            return;
        var changed = false;
        for (var r = 0; r < j.Tree.Length; r++)
        {
            var row = j.Tree[r];
            switch (row.State)
            {
                case UseState.Waiting:
                    if (TakeLoose(world, c, t, j, r))
                    {
                        changed = true;
                        break;
                    }
                    if (Dispatch(world, c, t, j, r))
                    {
                        changed = true;
                        break;
                    }
                    if (++row.Looks >= TakeLooks)
                    {
                        BuildHere(j, row, "no station took it in 30 s: built here, part by part");
                        changed = true;
                    }
                    else if (row.Looks % 4 == 1)
                    {
                        var left = (TakeLooks - row.Looks) / 2;
                        Note(row, $"{row.Note.Split(':')[0]}: this station builds it in {left} s", ref changed);
                    }
                    break;
                case UseState.Building:
                case UseState.Done:
                    changed |= row.Linked != 0 ? LinkSub.Watch(world, c, t, j, r) : Watch(world, c, t, j, r); // (a use built over a link: its line says where it is)
                    break;
            }
            if (j.BigRow >= 0)
                break; // (one big fit at a time: the boom fits nothing else meanwhile)
        }
        if (changed)
            world.StationChanged(c);
    }

    private static void Note(UseRow row, string note, ref bool changed)
    {
        if (row.Note == note)
            return;
        row.Note = note;
        changed = true;
    }

    /// <summary>A use nothing took (or cancelled): its steps become this station's own, ordered and fitted as any.</summary>
    private static void BuildHere(StationJob j, UseRow row, string why)
    {
        row.State = UseState.Own;
        row.Station = None;
        row.Note = why;
        ConstructStation.Rekind(j);
    }

    /// <summary>The sub-job of a row, if its station still holds it.</summary>
    private static StationJob? SubOf(Construct c, StationJob j, int r)
    {
        var row = j.Tree![r];
        if (row.Station >= c.PartCount || !c.DefOf(row.Station).IsBuildStation)
            return null;
        return c.Parts[row.Station].Station is { Role: 1 } s && s.RootStamp == j.Stamp && s.RootUse == r ? s : null;
    }

    /// <summary>A row with a sub-job: its progress, a failure (dispatched again), its body done (taken when it is in reach).</summary>
    private static bool Watch(ConstructWorld world, Construct c, int t, StationJob j, int r)
    {
        var row = j.Tree![r];
        var changed = false;
        var sub = SubOf(c, j, r);
        // A finished body made for this use is what counts once it is done: its station may take other work as soon as it has left
        // the bay (a lifter carrying it, a player moving it).
        if (row.State == UseState.Done && world.Get(row.Body) is { } kept && kept.SubStamp == j.Stamp && (sub == null || sub.Body != kept.Id))
            return Delivery(world, c, t, j, r, kept, false);
        if (sub == null || sub.State is StationState.Failed or StationState.Idle)
        {
            // Wrecked, cancelled at the sub-station, or it failed: dispatched again, never that station for this use.
            if (sub is { State: StationState.Failed })
                row.Skipped.Add(row.Station);
            var why = sub == null ? "its station stopped" : sub.State == StationState.Failed ? $"its station stopped: {sub.Note}" : "cancelled at its station";
            if (world.Get(row.Body) is { } freed && row.State == UseState.Done)
                freed.HeldBy = 0;
            row.State = UseState.Waiting;
            row.Station = None;
            row.Body = 0;
            row.Looks = 0;
            row.Note = why;
            return true;
        }
        if (row.Fitted != sub.Fitted || row.Steps != sub.Steps)
        {
            (row.Fitted, row.Steps) = (sub.Fitted, sub.Steps);
            changed = true;
        }
        if (sub.State == StationState.Building || sub.State == StationState.Costing)
        {
            Note(row, sub.State == StationState.Costing ? "costing" : $"building {sub.Fitted}/{sub.Steps}", ref changed);
            return changed;
        }
        if (sub.State != StationState.Done || world.Get(sub.Body) is not { } body)
            return changed;
        if (row.State != UseState.Done)
        {
            row.State = UseState.Done;
            row.Body = body.Id;
            changed = true;
        }
        return Delivery(world, c, t, j, r, body, true) || changed;
    }

    /// <summary>
    /// A use's finished body: bays that touch hand it over; one still in its own station's bay is carried by a lifter that reaches
    /// both (else a player moves it); one set down near this bay (by hand, a crane, its own wheels) is taken for the big fit.
    /// </summary>
    private static bool Delivery(ConstructWorld world, Construct c, int t, StationJob j, int r, Construct body, bool atStation)
    {
        var row = j.Tree![r];
        var changed = false;
        var inOwnBay = atStation && !row.Touch && row.Station < c.PartCount && !ConstructStation.BayClearOf(world, c, row.Station, body);
        if (row.Touch || !inOwnBay && NearBay(c, t, body))
        {
            if (ConstructLifters.CarryingBody(world, body.Id) != null)
                return changed; // (still on its way in)
            if (BeginFit(world, c, t, j, r, body) is { } refused)
                Note(row, refused, ref changed);
            else
                changed = true;
        }
        else if (ConstructLifters.CarryingBody(world, body.Id) is { } carrier)
            Note(row, $"done - a {carrier} brings it", ref changed);
        else
        {
            var carryWhy = "";
            var spotted = Spot(world, j, r, body, out var toPos, out var toRot, out var spotWhy);
            if (spotted && ConstructLifters.CarryBody(world, c, t, body, toPos, toRot, out var who, out carryWhy))
                Note(row, $"done - a {who} brings it", ref changed);
            else
                Note(row, $"done: {(spotted ? carryWhy : spotWhy)}: move {row.Name} #{row.Number} into this bay", ref changed);
        }
        return changed;
    }

    private static bool NearBay(Construct c, int t, Construct body)
    {
        ConstructStation.Bay(c, t, out var floor, out var rot, out var bay);
        var local = Vector3.Transform(body.WorldCom - floor, Quaternion.Conjugate(rot));
        return MathF.Abs(local.X) <= bay.X * 0.5f + HandReach && MathF.Abs(local.Z) <= bay.Z * 0.5f + HandReach && local.Y > -HandReach && local.Y < bay.Y + HandReach;
    }

    public const int LooseLooks = 8; // bodies near the bay a look checks for a use at most

    /// <summary>
    /// A loose body near the bay that is a whole use of this row's blueprint, taken for the big fit: one a player stamped or built (the
    /// design's "by hand" way; deliberate), never another station's sub-job body, never anchored, and the station owner's (or a team
    /// mate's). The bay test, cheap, comes first; a look checks <see cref="LooseLooks"/> near bodies at most.
    /// </summary>
    private static bool TakeLoose(ConstructWorld world, Construct c, int t, StationJob j, int r)
    {
        var row = j.Tree![r];
        var id = j.Bp!.Map!.Tags[row.Tag].Id;
        ConstructStation.Bay(c, t, out var floor, out var rot, out var bay);
        var inv = Quaternion.Conjugate(rot);
        var looked = 0;
        foreach (var b in world.Bodies)
        {
            if (b.Uses == null || b.HeldBy != 0 || b.GrabbedBy != 255 || b.Driver != 255 || b.Anchored || b.Id == j.Body || b.PartCount > row.Steps)
                continue;
            var local = Vector3.Transform(b.WorldCom - floor, inv);
            if (MathF.Abs(local.X) > bay.X * 0.5f + HandReach || MathF.Abs(local.Z) > bay.Z * 0.5f + HandReach || local.Y < -HandReach || local.Y > bay.Y + HandReach)
                continue;
            if (b.SubStamp != 0 && b.SubStamp != j.Stamp || !Factions.Shares(b.Owner, c.Owner) && b.Owner != 255)
                continue; // (another station's sub-job body, or someone else's)
            if (++looked > LooseLooks)
                break;
            if (TopTag(b, id) < 0)
                continue;
            if (BeginFit(world, c, t, j, r, b) is { } refused)
            {
                row.Note = refused;
                continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>The body's tag of blueprint <paramref name="id"/> that every part is in, or -1.</summary>
    private static int TopTag(Construct b, string id)
    {
        if (b.Uses == null)
            return -1;
        for (var t = 0; t < b.Uses.Count; t++)
        {
            if (b.Uses[t].Id != id || b.Uses[t].Parent >= 0)
                continue;
            var all = true;
            for (var i = 0; i < b.PartCount && all; i++)
                all = SubBlueprints.In(b, i, t);
            if (all)
                return t;
        }
        return -1;
    }

    // ---- Dispatch ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A free station on the same construct whose network this one's reaches, Sub-jobs Auto, powered, idle, its bay big enough: one
    /// whose bay touches this one's first, then the smallest bay, then the nearest. True when one took the use.
    /// </summary>
    private static bool Dispatch(ConstructWorld world, Construct c, int t, StationJob j, int r)
    {
        var row = j.Tree![r];
        var tag = j.Bp!.Map!.Tags[row.Tag];
        if (tag.Sub == null)
            return false;
        if (!Ready(j, tag, out var waits))
        {
            row.Note = waits;
            return false;
        }
        var net = ConstructCrafting.NetOf(world, c, t);
        Span<int> cand = stackalloc int[MaxCandidates];
        Span<float> score = stackalloc float[MaxCandidates];
        var n = 0;
        int busy = 0, off = 0, small = 0;
        ConstructStation.Bay(c, t, out var rootFloor, out _, out _);
        for (var s = 0; s < c.PartCount && n < MaxCandidates; s++)
        {
            if (s == t || !c.DefOf(s).IsBuildStation || row.Skipped.Contains((ushort)s))
                continue;
            if (!SubJobsOn(c, s))
            {
                off++;
                continue;
            }
            if (!ConstructRecycler.Powered(world, c, s) || !Idle(world, c, s))
            {
                busy++;
                continue;
            }
            var bay = ConstructStation.BaySize(c, s);
            if (ConstructStation.QuickRefusal(tag.Sub, bay) != null)
            {
                small++;
                continue;
            }
            if (!SameNet(world, c, s, net))
                continue;
            ConstructStation.Bay(c, s, out var floor, out _, out _);
            var touch = BaysTouch(c, t, s);
            score[n] = (touch ? 0 : 1e9f) + bay.X * bay.Y * bay.Z * 1000f + Vector3.Distance(floor, rootFloor);
            cand[n++] = s;
        }
        if (n == 0 && LinkSub.Offer(world, c, t, j, r, tag))
            return true; // (a linked network's idle Linked station, before the 30 s fallback: linked-logistics.md 4.4)
        if (n == 0)
        {
            row.Note = $"no station free{(busy > 0 ? $": {busy} busy" : "")}{(small > 0 ? $", {small} with a bay too small" : "")}{(off > 0 ? $", {off} with Sub-jobs Off" : "")}";
            return false;
        }
        var best = 0;
        for (var k = 1; k < n; k++)
            if (score[k] < score[best])
                best = k;
        var st = cand[best];
        var name = $"{j.Name}: {row.Name} #{row.Number}";
        if (ConstructStation.StartSub(world, c, st, tag.Sub, name, c.Id, (ushort)t, j.Stamp, r, j.Pinned, [.. j.PinPath, r]) is { } why)
        {
            row.Skipped.Add((ushort)st);
            row.Note = why;
            return false;
        }
        c.Parts[st].Station!.UseId = tag.Id;
        c.Parts[st].Station!.OrderAge = j.OrderAge != 0 ? j.OrderAge : world.OrderStamp++; // (its parts queue as old as this build)
        row.State = UseState.Building;
        row.Station = (ushort)st;
        row.Touch = BaysTouch(c, t, st);
        row.Note = "costing";
        var subJob = c.Parts[st].Station!;
        subJob.Header = $"for {c.DefOf(t).Name} {t}: {name}" + (row.Touch ? " - its bay touches the main station's: handed over" : " - a Loader Arm or Bridge Crane reaching both bays brings it, else move it into the main station's bay by hand");
        return true;
    }

    /// <summary>Is every step and use the use's joining links name in place (the flat blueprint's own order)? Else what it waits for.</summary>
    private static bool Ready(StationJob j, UseTag tag, out string waits)
    {
        var bp = j.Bp!;
        for (var k = tag.First; k < tag.First + tag.Count; k++)
        {
            var on = bp.Steps[k].On;
            if (on >= 0 && (on < tag.First || on >= tag.First + tag.Count) && j.PartOf.Length > on && j.PartOf[on] < 0)
            {
                waits = $"waits for the {bp.Steps[on].Part} (step {on})";
                return false;
            }
            if (bp.StepAlso[k] is { } also)
                foreach (var a in also)
                    if ((a.On < tag.First || a.On >= tag.First + tag.Count) && j.PartOf.Length > a.On && j.PartOf[a.On] < 0)
                    {
                        waits = $"waits for the {bp.Steps[a.On].Part} (step {a.On})";
                        return false;
                    }
        }
        waits = "";
        return true;
    }

    public static bool SubJobsOn(Construct c, int s) => ConstructConfig.Get(c.Parts[s], c.DefOf(s), CfgKey.SubJobs, 1) >= 0.5f;

    internal static bool Idle(ConstructWorld world, Construct c, int s)
    {
        if (c.Parts[s].Station is not { } sj)
            return true;
        if (sj.State is StationState.Costing or StationState.Building || sj.Return.Count > 0 || sj.Repeat > 0 && sj.State == StationState.Done)
            return false;
        return world.Get(sj.Body) is not { } b || ConstructStation.BayClearOf(world, c, s, b);
    }

    /// <summary>Do two stations share their network (a machine or a store both reach, or one's hold in the other's)?</summary>
    private static bool SameNet(ConstructWorld world, Construct c, int s, CraftNet net)
    {
        var other = ConstructCrafting.NetOf(world, c, s);
        for (var a = 0; a < net.MachineN; a++)
            for (var b = 0; b < other.MachineN; b++)
                if (net.Machines[a] == other.Machines[b])
                    return true;
        for (var a = 0; a < net.StoreN; a++)
        {
            if (net.Stores[a] == s)
                return true;
            for (var b = 0; b < other.StoreN; b++)
                if (net.Stores[a] == other.Stores[b])
                    return true;
        }
        return net.MachineN == 0 && net.StoreN == 0 && other.MachineN == 0 && other.StoreN == 0; // (two bare stations on one construct: nothing to tell them apart by)
    }

    /// <summary>Do two stations' bays touch or overlap (the no-crane layout: helper stations round a big bay)?</summary>
    public static bool BaysTouch(Construct c, int a, int b)
    {
        // (in the first bay's own frame: two bays of one construct turned alike are boxes there, whatever way the construct faces)
        ConstructStation.Bay(c, a, out var af, out var ar, out var asz);
        ConstructStation.Bay(c, b, out var bf, out var br, out var bsz);
        var inv = Quaternion.Conjugate(ar);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? 0 : 1, (k & 4) == 0 ? -0.5f : 0.5f) * bsz;
            var p = Vector3.Transform(bf + Vector3.Transform(corner, br) - af, inv);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        const float slack = 0.6f;
        return min.X <= asz.X * 0.5f + slack && max.X >= -asz.X * 0.5f - slack && min.Y <= asz.Y + slack && max.Y >= -slack && min.Z <= asz.Z * 0.5f + slack && max.Z >= -asz.Z * 0.5f - slack;
    }

    // ---- The big fit -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A use's body taken for its big fit: its parts checked against the flat blueprint's at the use's place (a part it lacks is
    /// fitted here after, as an ordinary step; a part the use doesn't have refuses it), then drawn to its spot on the fit timer.
    /// Null, or why it was refused (the body is left where it is).
    /// </summary>
    private static string? BeginFit(ConstructWorld world, Construct c, int t, StationJob j, int r, Construct body)
    {
        var row = j.Tree![r];
        var tag = j.Bp!.Map!.Tags[row.Tag];
        if (world.Get(j.Body) is not { } root)
            return "waits for the build's first part";
        if (!Ready(j, tag, out var waits))
            return waits;
        if (j.Plan is not { } plan || plan.At.Length != j.Bp.Steps.Count)
            return "waits for the bill";
        var bt = TopTag(body, tag.Id);
        if (bt < 0)
            return $"that body isn't one {tag.Name}";
        // The body's frame in the root's: its use's frame (the body's tag) onto the flat blueprint's (the row's tag).
        var btag = body.Uses![bt];
        var turn = Rot24.Compose(tag.Turn, SubBlueprints.Inverse(btag.Turn));
        var offset = ConstructWorld.Quantise(tag.Origin - Rot24.Apply(turn, btag.Origin));
        var want = new Dictionary<(string, int, int, int, byte), int>();
        for (var k = tag.First; k < tag.First + tag.Count; k++)
            if (j.PartOf[k] < 0)
            {
                var p = plan.At[k];
                want[(j.Bp.Steps[k].Part, Cm(p.X), Cm(p.Y), Cm(p.Z), Rot24.Nearest(plan.Rot[k], out _))] = k;
            }
        var match = new int[body.PartCount];
        var extra = 0;
        string firstExtra = "";
        for (var i = 0; i < body.PartCount; i++)
        {
            ref var p = ref body.Parts[i];
            if (body.FrameOf(p.Seg) != 0)
                return $"{tag.Name} #{row.Number} has freeform-attached parts: it can't be fitted as one";
            var at = offset + Rot24.Apply(turn, p.Pos);
            if (!want.Remove((body.DefOf(i).Name, Cm(at.X), Cm(at.Y), Cm(at.Z), Rot24.Compose(turn, p.Rot)), out var k))
            {
                extra++;
                if (firstExtra.Length == 0)
                    firstExtra = body.DefOf(i).Name;
                match[i] = -1;
                continue;
            }
            match[i] = k;
        }
        if (extra > 0)
            return $"{tag.Name} #{row.Number} arrived with {extra} part{(extra == 1 ? "" : "s")} it doesn't have ({firstExtra}): take {(extra == 1 ? "it" : "them")} off, or Cancel use";
        if (world.CanMerge(root, body) is { } no)
            return no;
        // Taken: frozen, held by this construct, drawn to its spot during the first third of the fit.
        body.Frozen = true;
        body.HeldBy = c.Id;
        body.Vel = body.AngVel = Vector3.Zero;
        j.BigRow = r;
        j.BigTurn = turn;
        j.BigOffset = offset;
        j.BigMatch = match;
        j.BigTicks = 0;
        var tonnes = body.Mass / 1000f;
        j.BigTotal = (int)(MathF.Min(FitMax, FitBase + FitPerTonne * tonnes) * World.TickRate);
        (j.BigFromPos, j.BigFromRot) = (body.Pos, body.Rot);
        j.BigToRot = Quaternion.Normalize(root.Rot * Rot24.Quat(turn));
        j.BigToPos = root.Pos + Vector3.Transform(offset, root.Rot);
        row.State = UseState.Fitting;
        row.Body = body.Id;
        row.Note = $"in place - welding, {j.BigTotal / World.TickRate} s";
        world.StructureDirty.Add(body.Id);
        world.StationChanged(c);
        if (row.Station != None && SubOf(c, j, r) is { } sub)
            sub.Note = "handed over: the main station fits it";
        Console.WriteLine($"[station] {j.Name}: big fit of {tag.Name} #{row.Number} ({body.PartCount} parts, {tonnes:0.0} t) begins, {j.BigTotal / (float)World.TickRate:0} s");
        return null;
    }

    private static int Cm(float v) => (int)MathF.Round(v * 100);

    /// <summary>Where a use's body stands once fitted (its world pose on the build), for a lifter's carry. False and why not yet.</summary>
    private static bool Spot(ConstructWorld world, StationJob j, int r, Construct body, out Vector3 pos, out Quaternion rot, out string why)
    {
        (pos, rot) = (Vector3.Zero, Quaternion.Identity);
        var tag = j.Bp!.Map!.Tags[j.Tree![r].Tag];
        if (world.Get(j.Body) is not { } root)
        {
            why = "waits for the build's first part";
            return false;
        }
        if (!Ready(j, tag, out why))
            return false;
        var bt = TopTag(body, tag.Id);
        if (bt < 0)
        {
            why = $"that body isn't one {tag.Name}";
            return false;
        }
        var btag = body.Uses![bt];
        var turn = Rot24.Compose(tag.Turn, SubBlueprints.Inverse(btag.Turn));
        var offset = ConstructWorld.Quantise(tag.Origin - Rot24.Apply(turn, btag.Origin));
        rot = Quaternion.Normalize(root.Rot * Rot24.Quat(turn));
        pos = root.Pos + Vector3.Transform(offset, root.Rot);
        return true;
    }

    /// <summary>Server, each tick of a big fit: the body drawn to its spot, then at the timer's end one merge.</summary>
    public static void FitTick(ConstructWorld world, Construct c, int t, StationJob j)
    {
        var row = j.Tree![j.BigRow];
        if (world.Get(row.Body) is not { } body || world.Get(j.Body) is not { } root)
        {
            row.State = UseState.Waiting;
            row.Note = "its body is gone";
            row.Body = 0;
            j.BigRow = -1;
            world.StationChanged(c);
            return;
        }
        j.BigTicks++;
        var move = Math.Max(1, j.BigTotal / 3);
        if (j.BigTicks <= move)
        {
            var f = j.BigTicks / (float)move;
            f = f * f * (3 - 2 * f);
            var pos = Vector3.Lerp(j.BigFromPos, root.Pos + Vector3.Transform(j.BigOffset, root.Rot), f);
            var rot = Quaternion.Slerp(j.BigFromRot, Quaternion.Normalize(root.Rot * Rot24.Quat(j.BigTurn)), f);
            body.PrevPos = body.NetPos = body.NetFromPos = body.Pos = pos;
            body.PrevRot = body.NetRot = body.NetFromRot = body.Rot = rot;
            body.Wake();
            return;
        }
        if (j.BigTicks < j.BigTotal || world.FitThisTick)
            return; // (one big fit a tick per world: FitThisTick)
        Weld(world, c, t, j, root, body);
    }

    /// <summary>The weld: one merge at the use's place, each part to its flat step, the joining links, the parts' tags; the rest of the use is built here.</summary>
    private static void Weld(ConstructWorld world, Construct c, int t, StationJob j, Construct root, Construct body)
    {
        var r = j.BigRow;
        var row = j.Tree![r];
        var tag = j.Bp!.Map!.Tags[row.Tag];
        j.BigRow = -1;
        var sw = Stopwatch.GetTimestamp();
        var off = root.PartCount;
        var n = body.PartCount;
        var match = j.BigMatch;
        SubBlueprints.EnsureTable(root, j.Bp.Map); // (the build's own table first: the body's tags come after it, and go once its parts are retagged)
        var tagsBefore = root.Uses!.Count;
        if (match.Length != n || !world.MergeAt(root, new JoinPlan { Other = body, Turn = j.BigTurn, Offset = j.BigOffset }))
        {
            row.State = UseState.Waiting;
            row.Note = "the weld refused: it is dispatched again";
            body.HeldBy = 0;
            world.StationChanged(c);
            return;
        }
        var fitted = 0;
        for (var i = 0; i < n; i++)
            if (match[i] >= 0)
            {
                j.PartOf[match[i]] = off + i;
                SubBlueprints.TagPart(root, j.Bp.Map, match[i], off + i);
                fitted++;
            }
        for (var k = tag.First; k < tag.First + tag.Count; k++)
            if (j.PartOf[k] >= 0)
                SubBlueprints.LinkOutside(world, root, j.Bp, k, j.PartOf, tag.First, tag.Count);
        if (root.Uses != null && root.Uses.Count > tagsBefore)
            root.Uses.RemoveRange(tagsBefore, root.Uses.Count - tagsBefore); // (the body's own tags: every part of it carries the build's now)
        // The check that the join is exact: every welded part where the costing put its step (nothing fails silently).
        if (j.Plan is { } plan && plan.At.Length == j.PartOf.Length)
        {
            var off1 = 0;
            for (var k = tag.First; k < tag.First + tag.Count; k++)
                if (j.PartOf[k] >= 0 && Vector3.DistanceSquared(root.Parts[j.PartOf[k]].Pos, plan.At[k]) > 0.0001f)
                    off1++;
            if (off1 > 0)
                Console.WriteLine($"[station] {j.Name}: {tag.Name} #{row.Number}: {off1} welded parts are off their places by more than 1 cm");
        }
        root.Dirty = true;
        root.Rebuild(); // (once: the merge left it for the joining links)
        root.PrevPos = root.NetPos = root.NetFromPos = root.Pos = j.Pos;
        world.StructureDirty.Add(root.Id);
        j.Fitted = (ushort)Math.Min(None - 1, j.Fitted + fitted);
        j.LastPart = (ushort)(off + Math.Max(0, n - 1));
        j.FitSeq++;
        world.FitThisTick = true;
        row.State = UseState.InPlace;
        row.Delivered = true;
        row.Body = 0;
        var missing = tag.Count - fitted;
        row.Note = missing > 0 ? $"in place: {missing} missing part{(missing == 1 ? "" : "s")} fitted here" : "in place";
        ConstructStation.Rekind(j);
        var ms = (Stopwatch.GetTimestamp() - sw) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"[station] {j.Name}: {tag.Name} #{row.Number} welded in: {fitted} parts in {ms:0.00} ms{(missing > 0 ? $", {missing} to fit here" : "")}");
        world.StationChanged(c);
    }

    // ---- The sub-job's end, cancels ----------------------------------------------------------------------------------------

    /// <summary>
    /// A sub-job's steps and own links are in: no finish and no fill (the whole ship's, done once by the root). Its body stays frozen,
    /// held by this construct, one use of its blueprint; with no way to the root's bay it is freed for a player to move.
    /// </summary>
    public static void SubDone(ConstructWorld world, Construct c, int t, StationJob j, Construct b)
    {
        SubBlueprints.TagStamp(b, j.Bp!, j.UseId);
        b.SubStamp = j.RootStamp; // (made for that root's use: no other station takes it)
        j.State = StationState.Done;
        j.Repeat = 0;
        var touch = world.Get(j.RootBody) is { } rc && j.RootPart < rc.PartCount && rc.Id == c.Id && BaysTouch(c, j.RootPart, t);
        if (!touch)
        {
            b.HeldBy = 0; // (frozen where it stands, as a stamped group lands: any player may move it with the physics gun)
            b.Wake();
        }
        j.Note = touch ? "handed to the main station" : "waiting to be carried: a lifter reaching both bays, or move it into the main station's bay";
        world.StructureDirty.Add(b.Id);
        world.StationChanged(c);
        Console.WriteLine($"[station] {j.Name}: sub-job done ({j.Steps} parts){(touch ? "" : ", freed for carrying")}");
    }

    /// <summary>
    /// The root's job ended. Cancel (<paramref name="takeApart"/>): every sub-job under it cancels and a delivered body not yet fitted
    /// is taken apart, its parts given back. Release early or a failure: sub-jobs still building cancel, and every finished or fitting
    /// body is freed as it stands, an ordinary construct (nothing is lost, nothing stays held).
    /// </summary>
    public static void CancelTree(ConstructWorld world, Construct c, StationJob j, bool takeApart = true)
    {
        if (j.Tree == null)
            return;
        for (var r = 0; r < j.Tree.Length; r++)
        {
            var row = j.Tree[r];
            if (SubOf(c, j, r) is { } sub && row.Station < c.PartCount)
            {
                if (sub.State == StationState.Done && world.Get(sub.Body) is { } done)
                {
                    if (takeApart)
                        TakeApart(world, sub, done);
                    else
                        FreeBody(world, done);
                    sub.State = StationState.Idle;
                    sub.Body = 0;
                    sub.Note = takeApart ? "its main station cancelled: giving the parts back" : "its main station stopped: what it built is yours";
                    world.StationOpened(c);
                    world.StationChanged(c);
                }
                else
                    ConstructStation.Cancel(world, c, row.Station);
            }
            else if (row.State is UseState.Fitting or UseState.Done && world.Get(row.Body) is { } held && held.Id != j.Body)
                FreeBody(world, held);
            row.State = UseState.Waiting;
            row.Body = 0;
        }
        j.BigRow = -1;
    }

    /// <summary>A sub-blueprint body let go: no station holds it, not frozen, an ordinary construct where it stands.</summary>
    private static void FreeBody(ConstructWorld world, Construct b)
    {
        b.HeldBy = 0;
        b.Frozen = false;
        b.SubStamp = 0;
        b.Dirty = true;
        b.Rebuild();
        b.Wake();
        world.StructureDirty.Add(b.Id);
    }

    private static void TakeApart(ConstructWorld world, StationJob owner, Construct b)
    {
        for (var i = 0; i < b.PartCount; i++)
        {
            var id = (ushort)b.Parts[i].Def;
            var k = owner.Return.FindIndex(x => x.Def == id && !x.Loose);
            if (k < 0)
                owner.Return.Add((id, 1, false));
            else
                owner.Return[k] = (id, owner.Return[k].Count + 1, false);
        }
        world.Remove(b);
    }

    /// <summary>Cancel use (the tree's row): its sub-job cancels (its body is taken apart) and the use is dispatched again; a use built here goes back to waiting only before any of it is fitted.</summary>
    public static string? CancelUse(ConstructWorld world, Construct c, int t, int r)
    {
        if (c.Parts[t].Station is not { Tree: { } tree } j || r < 0 || r >= tree.Length)
            return "no such use";
        var row = tree[r];
        switch (row.State)
        {
            case UseState.Building:
            case UseState.Done:
                if (SubOf(c, j, r) is { } sub)
                {
                    if (sub.State == StationState.Done && world.Get(sub.Body) is { } done)
                    {
                        TakeApart(world, sub, done);
                        sub.State = StationState.Idle;
                        sub.Body = 0;
                        world.StationOpened(c);
                    }
                    else
                        ConstructStation.Cancel(world, c, row.Station);
                }
                break;
            case UseState.Fitting:
                if (world.Get(row.Body) is { } fitting)
                    fitting.HeldBy = 0;
                j.BigRow = -1;
                break;
            default:
                return row.State == UseState.Own ? "it is being built here" : row.State == UseState.InPlace ? "it is in place" : "it waits for a station already";
        }
        row.State = UseState.Waiting;
        row.Station = None;
        row.Body = 0;
        row.Looks = 0;
        row.Note = "cancelled: dispatched again";
        world.StationChanged(c);
        return null;
    }

    /// <summary>Should step <paramref name="i"/> of the links (wires first) be laid? A delivered use's own links came with its body.</summary>
    public static bool SkipLink(StationJob j, int i)
    {
        if (j.Tree == null || j.StepRow == null)
            return false;
        var bp = j.Bp!;
        int from, to;
        List<int>? hosts = null;
        if (i < bp.Wires.Count)
            (from, to) = bp.Wires[i];
        else
        {
            var s = bp.Links[i - bp.Wires.Count];
            (from, to, hosts) = (s.Branch < 0 ? s.From : s.To, s.To, s.Hosts);
        }
        if (from < 0 || to < 0 || from >= j.StepRow.Length || to >= j.StepRow.Length)
            return false;
        var r = j.StepRow[from];
        if (r < 0 || j.StepRow[to] != r || !j.Tree[r].Delivered)
            return false;
        if (hosts != null)
            foreach (var h in hosts)
                if (h < 0 || h >= j.StepRow.Length || j.StepRow[h] != r)
                    return false;
        return true;
    }

    // ---- The wire (after each job in CBodyKind.Station) ----------------------------------------------------------------------

    public static void Write(PacketWriter w, StationJob j)
    {
        w.U8(j.Role);
        if (j.Role == 1)
            w.Str(j.Header.Length > 200 ? j.Header[..200] : j.Header);
        var n = Math.Min(j.Tree?.Length ?? 0, MaxRowsSent);
        w.U8((byte)n);
        for (var r = 0; r < n; r++)
        {
            var row = j.Tree![r];
            w.U8((byte)row.State).U16(row.Station).U16(row.Body).U16(row.Fitted).U16(row.Steps).Str(row.Name).U8((byte)Math.Min(255, row.Number)).Str(row.Note.Length > 120 ? row.Note[..120] : row.Note);
        }
    }

    public static void Read(ref PacketReader r, StationJob? j)
    {
        var role = r.U8();
        var header = role == 1 ? r.Str() : "";
        int n = r.U8();
        var rows = n > 0 ? new UseRow[n] : null;
        for (var k = 0; k < n; k++)
            rows![k] = new UseRow { State = (UseState)r.U8(), Station = r.U16(), Body = r.U16(), Fitted = r.U16(), Steps = r.U16(), Name = r.Str(), Number = r.U8(), Note = r.Str() };
        if (j == null)
            return;
        j.Role = role;
        j.Header = header;
        j.Tree = rows;
    }

    /// <summary>The tree as the station's panel shows it, a line a use: "Turret #2   Station 14   done - Bridge Crane bringing it".</summary>
    public static IEnumerable<string> Lines(Construct c, StationJob j)
    {
        if (j.Tree == null)
            yield break;
        var inPlace = j.Tree.Count(x => x.State == UseState.InPlace);
        yield return $"uses: {j.Tree.Length} ({inPlace} in place)";
        foreach (var row in j.Tree)
        {
            var where = row.State switch
            {
                UseState.Building or UseState.Done => row.Station < c.PartCount ? $"Station {row.Station}" : "a station",
                UseState.Own or UseState.Fitting or UseState.InPlace => "this station",
                _ => "waiting",
            };
            var what = row.State == UseState.Building ? $"building {row.Fitted}/{row.Steps}" : row.Note.Length > 0 ? row.Note : row.State.ToString().ToLowerInvariant();
            yield return $"  {row.Name} #{row.Number}   {where}   {what}";
        }
    }

    // ---- Saves (the SUBJ block) --------------------------------------------------------------------------------------------

    /// <summary>A body's stations' tree fields: per job its role, stamps, root, pinned blueprint and rows. Null when none has any.</summary>
    public static byte[]? SaveBytes(ConstructWorld world, Construct c)
    {
        SaveWriter? w = null;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Station is { } j && (j.Role != 0 || j.Tree != null) && j.State is not (StationState.Idle or StationState.Failed))
            {
                w ??= new SaveWriter(1024);
                if (n++ == 0)
                    w.U16(0); // (the count, patched below)
                w.U16((ushort)i).U8(j.Role).U32(j.Stamp).U16(j.RootBody).U16(j.RootPart).U32(j.RootStamp).U16((ushort)Math.Max(0, j.RootUse)).Str(j.Header.Length > 200 ? j.Header[..200] : j.Header);
                var packed = j.Pinned.Length > 0 ? ConstructEdits.Deflate(j.Pinned) : [];
                w.I32(packed.Length).Bytes(packed);
                w.U8((byte)Math.Min(SubBlueprints.MaxDepth + 1, j.PinPath.Length)); // (a sub-job's way down the pinned tree: a row at each level)
                for (var q = 0; q < j.PinPath.Length && q <= SubBlueprints.MaxDepth; q++)
                    w.U16((ushort)j.PinPath[q]);
                var rows = j.Tree?.Length ?? 0;
                w.U16((ushort)rows);
                for (var r = 0; r < rows; r++)
                {
                    var row = j.Tree![r];
                    var state = row.State == UseState.Fitting ? UseState.Done : row.State; // (a fit under way starts again after a load)
                    w.U8((byte)state).U16(row.Station).U16(row.Body).U8((byte)((row.Delivered ? 1 : 0) | (row.Touch ? 2 : 0))).U16((ushort)Math.Min(ushort.MaxValue, row.Looks));
                }
            }
        if (w == null)
            return null;
        var bytes = w.Span.ToArray();
        bytes[0] = (byte)n;
        bytes[1] = (byte)(n >> 8);
        return bytes;
    }

    /// <summary>A load's tree fields put on the jobs before they are applied (ConstructStation.Apply builds a pinned job's blueprint from them).</summary>
    public static void Prepare((ushort Part, StationJob Job)[] jobs, byte[] bytes)
    {
        try
        {
            var r = new SaveReader(bytes);
            int n = r.U16();
            for (var k = 0; k < n; k++)
            {
                var part = r.U16();
                var role = r.U8();
                var stamp = r.U32();
                var rootBody = r.U16();
                var rootPart = r.U16();
                var rootStamp = r.U32();
                int rootUse = r.U16();
                var header = r.Str();
                var len = r.I32();
                var packed = r.Bytes(len);
                var path = new int[r.U8()];
                for (var q = 0; q < path.Length; q++)
                    path[q] = r.U16();
                int rows = r.U16();
                var tree = new UseRow[rows];
                for (var q = 0; q < rows; q++)
                {
                    tree[q] = new UseRow { State = (UseState)r.U8(), Station = r.U16(), Body = r.U16() };
                    var flags = r.U8();
                    (tree[q].Delivered, tree[q].Touch, tree[q].Looks) = ((flags & 1) != 0, (flags & 2) != 0, r.U16());
                }
                var j = Array.Find(jobs, x => x.Part == part).Job;
                if (j == null)
                    continue;
                (j.Role, j.Stamp, j.RootBody, j.RootPart, j.RootStamp, j.RootUse, j.Header) = (role, stamp, rootBody, rootPart, rootStamp, rootUse, header);
                _stamp = Math.Max(_stamp, Math.Max(stamp, rootStamp));
                if (packed.Length > 0)
                {
                    j.Pinned = ConstructEdits.Inflate(packed);
                    try
                    {
                        var bp = SubBlueprints.ParseFlat(j.Pinned, j.Name + ".json", new SubBlueprints.Source { Library = false });
                        foreach (var step in path)
                        {
                            var tops = bp.Map?.Top().ToList() ?? throw new ConstructPartException("its use isn't in it");
                            if (step >= tops.Count)
                                throw new ConstructPartException("its use isn't in it");
                            j.UseId = bp.Map.Tags[tops[step]].Id;
                            bp = SubBlueprints.SubFlat(bp, bp.Map.Tags[tops[step]]);
                        }
                        j.PinnedBp = bp;
                        j.PinPath = path;
                    }
                    catch (ConstructPartException ex)
                    {
                        Console.WriteLine($"[station] {j.Name}: its pinned blueprint doesn't load: {ex.Message}");
                    }
                }
                j.LoadedRows = tree;
            }
        }
        catch (Exception ex) when (ex is WorldFileException or ConstructPartException or System.IO.InvalidDataException)
        {
            Console.WriteLine($"[station] a SUBJ block skipped: {ex.Message}");
        }
    }

    /// <summary>After a load's costing: the saved rows on the job's new tree (same blueprint, same rows).</summary>
    public static void Restore(StationJob j)
    {
        if (j.LoadedRows is not { } saved || j.Tree == null)
            return;
        for (var r = 0; r < j.Tree.Length && r < saved.Length; r++)
        {
            var row = j.Tree[r];
            (row.State, row.Station, row.Body, row.Delivered, row.Touch, row.Looks) = (saved[r].State, saved[r].Station, saved[r].Body, saved[r].Delivered, saved[r].Touch, saved[r].Looks);
            if (row.State == UseState.InPlace && row.Delivered)
                row.Note = "in place";
        }
        j.LoadedRows = null;
        ConstructStation.Rekind(j);
    }
}

public sealed partial class StationJob
{
    public UseRow[]? LoadedRows; // a load's rows until the costing made the tree
}
