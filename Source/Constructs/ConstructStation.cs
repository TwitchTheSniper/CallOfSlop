using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public enum StationState : byte
{
    Idle, // nothing picked (or cancelled)
    Costing, // its blueprint is being built in a scratch world, a slice a tick: bounds, link lengths, kinds
    Building, // the vehicle grows frozen in the bay
    Done, // released: "drive it out" (and Repeat waits for the bay to clear)
    Failed, // it doesn't fit, or a step refused: the note says why
}

public enum StationOp : byte
{
    Start, // n: the repeat count (0xFFFF forever), text: the blueprint's name
    Cancel,
    Release, // release early: what is fitted becomes a normal construct
    Repeat, // n: builds still to start after this one
    Give, // what the player holds goes to the build
}

/// <summary>A Building Station's job (<see cref="CPart.Station"/>; ConstructStation.cs). The first block is what both peers hold.</summary>
public sealed class StationJob
{
    public string Name = ""; // the blueprint, by name (never an index: the list shifts)
    public StationState State;
    public ushort Body; // the frozen construct it builds (0: none yet)
    public ushort Repeat; // builds still to start after this one (0xFFFF: forever)
    public ushort Fitted, Steps, LinksDone, Links;
    public ushort LastPart = ushort.MaxValue; // the part fitted last, on Body (clients: where the fitting effect goes)
    public byte FitSeq, LinkSeq; // count up with each part fitted and each link laid (clients: an effect when one changes)
    public string Note = ""; // what it waits for, by name; why it failed

    // ---- Server only.
    public ConstructBuild.Blueprint? Bp;
    public int[] PartOf = []; // each step's part on Body (-1: not fitted)
    public int LinkAt; // the next of its wires, then its links, in the blueprint's order
    public float[] LinkLen = []; // each wire's and link's length as the game lays it (m), from the scratch build
    public IEnumerator<int>? Costing;
    public ConstructBuild.BuildRun? Run;
    public bool Resume; // costing after a load: PartOf, Body and LinkAt are the save's
    public Vector3 Pos; // Body's pose in the bay
    public Quaternion Rot = Quaternion.Identity;
    public Vector3 Size; // the blueprint's bounds
    public int FitTicks, OrderTicks;
    public int Cursor; // every step before it is fitted
    public ushort[] KindDef = [], KindNeed = [], KindFit = [];
    public string?[] KindWhy = []; // why nothing linked holds or makes it (null: ordered or held)
    public int KindN;
    public readonly List<(ushort Def, int Count, bool Loose)> Return = new(); // parts given back after a cancel or a release, a few a step
    public double CostMs, WorstMs;
    public long Started; // Stopwatch ticks at Start (the log's start-to-drivable time)

    // ---- Client only: the effects it has shown.
    public byte SeenFit, SeenLink;
    public byte HeardFit, HeardLink;
    public double FitAt, LinkAt2;
}

/// <summary>
/// The Building Station (docs/sandbox/constructs/production.md, "Building station"): a blueprint built in the bay beside it,
/// part by part, from what its hold receives. It is a Crafting Terminal by data (<see cref="ConstructPartDef.IsBuildStation"/>
/// on a Piece Rack with <c>crafting</c>), so its hold, its ports, its network and its orders are ConstructCrafting's: a kind the
/// build lacks becomes a craft order (linked stock reserved first, the rest dealt to the wired consoles' machines). The vehicle
/// is an ordinary construct, frozen and <see cref="Construct.HeldBy"/> the station until done; each part goes on through
/// <see cref="ConstructBuild.FitStep"/>, the blueprint loader's own step. Server; nothing runs while no station has a job.
/// </summary>
public static class ConstructStation
{
    public const float FitSeconds = 2f; // the core fits one part this often by itself...
    public const float FitSecondsPerKg = 0.02f; // ...plus this for its mass...
    public const float FitSecondsMax = 10f; // ...at most
    public const float LinkSeconds = 1f; // a wire or a link laid
    public const double CostBudgetMs = 1.5; // a tick's slice of the scratch build
    public const int OrderEvery = 30; // ticks between looks at what to order (2 Hz)
    public const int OrdersAStep = 4; // craft orders placed in one look
    public const int ReturnAStep = 8; // parts given back in one look
    public const float BayGap = 0.5f; // between the station's side and its bay
    public const string WireStock = "copper", PipeStock = "steel"; // link stock by the metre (proposed): wires and cables; pipes, chutes, shafts, feeds, conveyors
    public const string HeldText = "a Building Station is building it: Cancel or Release early at the station";
    private const ushort None = ushort.MaxValue;

    // ---- The bay (either peer) ---------------------------------------------------------------------------------------------

    /// <summary>The bay's size from the station's settings: across (away from the station), up, along.</summary>
    public static Vector3 BaySize(Construct c, int part)
    {
        var d = c.DefOf(part);
        ref readonly var p = ref c.Parts[part];
        return new Vector3(ConstructConfig.Get(p, d, CfgKey.BayWidth, 6), ConstructConfig.Get(p, d, CfgKey.BayHeight, 4), ConstructConfig.Get(p, d, CfgKey.BayDepth, 8));
    }

    /// <summary>The bay in the world: the middle of its floor, its turn, its size (x across, y up, z along).</summary>
    public static void Bay(Construct c, int part, out Vector3 floor, out Quaternion rot, out Vector3 size)
    {
        var d = c.DefOf(part);
        size = BaySize(c, part);
        Freeform.PartWorld(c, part, out var pos, out rot);
        var local = new Vector3(d.BoundsMax.X + BayGap + size.X * 0.5f, d.BoundsMin.Y, (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f);
        floor = pos + Vector3.Transform(local, rot);
    }

    /// <summary>Why a blueprint this size isn't built in a bay that size, or null.</summary>
    public static string? FitRefusal(Vector3 need, Vector3 bay) =>
        need.X <= bay.X + 0.005f && need.Y <= bay.Y + 0.005f && need.Z <= bay.Z + 0.005f
            ? null
            : $"doesn't fit the bay: it is {need.X:0.0} x {need.Z:0.0} m and {need.Y:0.0} m high, the bay {bay.X:0.0} x {bay.Z:0.0} m and {bay.Y:0.0} m high (the station's Bay settings)";

    /// <summary>What a built body takes up, wheels' travel included: its low corner and its size, body frame.</summary>
    public static void Bounds(Construct b, out Vector3 lo, out Vector3 size)
    {
        if (b.Dirty)
            b.Rebuild();
        lo = b.LocalMin;
        for (var k = 0; k < b.WheelCount; k++)
            lo.Y = MathF.Min(lo.Y, b.Wheels[k].Centre.Y - b.Wheels[k].Travel * 0.5f - b.Wheels[k].Radius);
        size = b.LocalMax - lo;
    }

    // ---- Words (either peer) -----------------------------------------------------------------------------------------------

    public static string StateText(StationJob? j) => j == null ? "idle" : j.State switch
    {
        StationState.Costing => $"{j.Name}: working out its bill",
        StationState.Building => j.Fitted < j.Steps ? $"{j.Name}: {j.Fitted} of {j.Steps} parts" : $"{j.Name}: link {j.LinksDone} of {j.Links}",
        StationState.Done => $"{j.Name}: done - drive it out",
        StationState.Failed => $"{j.Name}: stopped",
        _ => "idle",
    };

    /// <summary>The station's look line.</summary>
    public static string Look(ConstructWorld world, Construct c, int t, CStore st, bool near)
    {
        var j = c.Parts[t].Station;
        var size = BaySize(c, t);
        var sb = new System.Text.StringBuilder(c.DefOf(t).Name).Append(": ");
        if (j == null || j.State == StationState.Idle)
            sb.Append($"bay {size.X:0.#} x {size.Z:0.#} x {size.Y:0.#} m, ").Append(ConstructCrafting.Sees(ConstructCrafting.NetOf(world, c, t)));
        else
            sb.Append(StateText(j));
        if (st.ItemCount > 0)
            sb.Append($", {st.ItemCount} in the hold");
        if (j is { Note.Length: > 0 })
            sb.Append(" - ").Append(j.Note);
        if (near)
            sb.Append($" - {SandboxKeys.Use}: {(j is { State: StationState.Building } ? "its panel, or give it the part you hold" : "pick a blueprint")}");
        return sb.ToString();
    }

    /// <summary>The station's screen (panel source <c>station</c>). Either peer; short lines.</summary>
    public static void FillPanel(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        var d = c.DefOf(part);
        if (!d.IsBuildStation || world.StoreOf(c, part) is not { } st)
            return;
        if (!ConstructRecycler.Powered(world, c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, d.Name);
            card.Add(1, c.Parts[part].Broken ? "Wrecked" : "No power");
            return;
        }
        var j = c.Parts[part].Station;
        card.Add(0, "BUILDING STATION");
        if (j == null || j.State == StationState.Idle)
        {
            card.State = PanelState.Ok;
            var s = BaySize(c, part);
            card.Add(1, "Bay ").AddShort(1, s.X).Add(1, 'x').AddShort(1, s.Z).Add(1, 'x').AddShort(1, s.Y);
            card.Add(3, "Use the screen:");
            card.Add(4, "pick a blueprint");
            return;
        }
        card.State = j.State switch { StationState.Failed => PanelState.Fault, StationState.Done => PanelState.Ok, _ => PanelState.Working };
        card.Add(1, j.Name).Clip(1, PanelCard.MaxChars);
        switch (j.State)
        {
            case StationState.Costing:
                card.Add(2, "Working out bill");
                break;
            case StationState.Building:
                card.Add(2, "Parts ").Add(2, j.Fitted).Add(2, '/').Add(2, j.Steps);
                card.Add(3, "Links ").Add(3, j.LinksDone).Add(3, '/').Add(3, j.Links);
                card.Add(4, "Hold: ").Add(4, st.ItemCount);
                break;
            case StationState.Done:
                card.Add(2, "DONE");
                card.Add(3, "Drive it out");
                if (j.Repeat > 0)
                    card.Add(4, j.Repeat == None ? "Repeats" : "More: ").Add(4, j.Repeat == None ? 0 : j.Repeat);
                break;
            default:
                card.Add(2, "STOPPED");
                break;
        }
        // The note, wrapped over the rows left.
        for (int row = 5, at = 0; row < 8 && at < j.Note.Length; row++)
        {
            var n = Math.Min(PanelCard.MaxChars, j.Note.Length - at);
            for (var k = 0; k < n; k++)
                card.Add(row, j.Note[at + k]);
            at += n;
        }
    }

    // ---- Kinds and orders (server) -----------------------------------------------------------------------------------------

    /// <summary>The work order that asks for one of a step's part: a piece by its key, a panel by its name, a catalogue part by its id.</summary>
    public static WorkOrder OrderFor(ConstructPartDef d, int count)
    {
        var n = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount);
        if (HullLoft.IsFormedName(d.Name))
            return ConstructPanelFormer.Order(d.Name, n);
        if (d.Name.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase) && Extrusion.ParseKey(d.Name) is { } key)
            return new WorkOrder { Key = key with { Growing = false }, Part = ConstructParts.None, Count = n };
        return new WorkOrder { Part = (ushort)d.Id, Count = n };
    }

    private static int KindOf(StationJob j, ushort def)
    {
        for (var i = 0; i < j.KindN; i++)
            if (j.KindDef[i] == def)
                return i;
        return -1;
    }

    private static void MakeKinds(StationJob j)
    {
        var bp = j.Bp!;
        var defs = new List<ushort>();
        var need = new List<ushort>();
        foreach (var step in bp.Steps)
        {
            var id = (ushort)(ConstructParts.Find(step.Part)?.Id ?? ConstructParts.None);
            var k = defs.IndexOf(id);
            if (k < 0)
            {
                defs.Add(id);
                need.Add(1);
            }
            else
                need[k]++;
        }
        j.KindDef = defs.ToArray();
        j.KindNeed = need.ToArray();
        j.KindFit = new ushort[defs.Count];
        j.KindWhy = new string?[defs.Count];
        j.KindN = defs.Count;
    }

    /// <summary>What the station's open craft orders still ask for, of one kind.</summary>
    private static int Asked(CraftList? q, ushort def)
    {
        var n = 0;
        if (q != null)
            for (var k = 0; k < q.Count; k++)
                if (!q.Items[k].Cancelled && ConstructCrafting.KindOf(ref q.Items[k]) == def)
                    n += q.Items[k].Left;
        return n;
    }

    /// <summary>
    /// Server, 2 Hz while building: done craft orders go, and each kind the build still lacks (need less fitted, held and asked)
    /// becomes a craft order, the kinds of the earliest unfitted steps first. The note says what the build waits for.
    /// </summary>
    private static void Orders(ConstructWorld world, Construct c, int t, CStore tray, StationJob j)
    {
        var q = c.Parts[t].Craft;
        if (q != null)
            for (var k = q.Count - 1; k >= 0; k--)
                if (q.Items[k].Done)
                {
                    q.RemoveAt(k);
                    world.CraftChanged(c);
                }
        var placed = 0;
        string? first = null;
        var lacking = 0;
        for (var i = 0; i < j.KindN; i++)
        {
            var def = j.KindDef[i];
            var lack = j.KindNeed[i] - j.KindFit[i] - ConstructCrafting.Holds(tray, def);
            if (lack <= 0)
            {
                j.KindWhy[i] = null;
                continue;
            }
            lacking += lack;
            var d = ConstructParts.Get(def);
            var name = d?.Name ?? "a part this build doesn't have";
            var asked = Asked(c.Parts[t].Craft, def);
            if (lack > asked && d != null && placed < OrdersAStep && (c.Parts[t].Craft?.Count ?? 0) < ConstructCrafting.MaxOrders)
            {
                placed++;
                j.KindWhy[i] = ConstructCrafting.Place(world, c, t, OrderFor(d, lack - asked));
            }
            else if (d == null)
                j.KindWhy[i] = "no such part";
            if (first == null)
            {
                first = j.KindWhy[i] is { } why ? $"{name}: {why}" : $"{name}";
                if (j.KindWhy[i] == null && c.Parts[t].Craft is { } cq)
                    for (var k = 0; k < cq.Count; k++)
                        if (ConstructCrafting.KindOf(ref cq.Items[k]) == def)
                        {
                            first = $"{name} ({ConstructCrafting.StateText(c, cq.Items[k])})";
                            break;
                        }
            }
        }
        var note = lacking == 0 ? "" : $"waits for {lacking} part{(lacking == 1 ? "" : "s")}: {first}";
        if (j.Fitted >= j.Steps && j.LinksDone < j.Links)
            note = j.Note.StartsWith("waits for", StringComparison.Ordinal) && j.Note.Contains("stock") ? j.Note : "";
        if (note != j.Note)
        {
            j.Note = note.Length > 120 ? note[..120] : note;
            world.StationChanged(c);
        }
    }

    // ---- The job (server) --------------------------------------------------------------------------------------------------

    /// <summary>A blueprint by name: the shipped ones, then the host's own saved ones.</summary>
    public static ConstructBuild.Blueprint? Find(string name) =>
        ConstructBuild.FindBlueprint(name) ?? Own(name);

    private static ConstructBuild.Blueprint? Own(string name)
    {
        try
        {
            return PlayerBlueprints.NameRefusal(name) == null && PlayerBlueprints.Exists(name) ? ConstructBuild.ParseBlueprint(File.ReadAllText(PlayerBlueprints.PathOf(name)), name) : null;
        }
        catch (Exception e) when (e is ConstructPartException or IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Server: Start. Null, or why not.</summary>
    public static string? Start(ConstructWorld world, Construct c, int t, string name, ushort repeat)
    {
        if (!c.DefOf(t).IsBuildStation)
            return "that isn't a Building Station";
        if (c.Parts[t].Station is { State: StationState.Costing or StationState.Building })
            return "it is building: Cancel or Release early first";
        if (c.Parts[t].Station is { Return.Count: > 0 })
            return "it is still giving parts back";
        if (Find(name) is not { } bp)
            return $"no blueprint named \"{name}\"";
        if (bp.Steps.Count == 0 || bp.Steps.Count >= None)
            return "that blueprint has no parts";
        var j = new StationJob { Name = bp.Name, Bp = bp, Repeat = repeat, Steps = (ushort)bp.Steps.Count, Started = Stopwatch.GetTimestamp() };
        c.Parts[t].Station = j;
        BeginCosting(world, j);
        world.StationOpened(c);
        world.StationChanged(c);
        return null;
    }

    private static void BeginCosting(ConstructWorld world, StationJob j)
    {
        j.State = StationState.Costing;
        j.Run = new ConstructBuild.BuildRun { KeepPartOf = true };
        var scratch = new ConstructWorld { CreativeRecycling = world.CreativeRecycling };
        j.Costing = ConstructBuild.BuildSteps(scratch, j.Bp!, Vector3.Zero, Quaternion.Identity, j.Run).GetEnumerator();
        j.CostMs = j.WorstMs = 0;
    }

    private static void Fail(ConstructWorld world, Construct c, int t, StationJob j, string why)
    {
        CancelOrders(world, c, t);
        if (world.Get(j.Body) is { HeldBy: not 0 } b)
            Free(world, b, j); // (what stands is released as it is: nothing is lost)
        j.State = StationState.Failed;
        j.Note = why.Length > 160 ? why[..160] : why;
        j.Costing = null;
        j.Run = null;
        Console.WriteLine($"[station] {j.Name}: stopped: {why}");
        world.StationChanged(c);
    }

    private static void CancelOrders(ConstructWorld world, Construct c, int t)
    {
        if (c.Parts[t].Craft is not { } q)
            return;
        Span<ushort> ids = stackalloc ushort[ConstructCrafting.MaxOrders];
        var n = 0;
        for (var k = 0; k < q.Count; k++)
            ids[n++] = q.Items[k].Id;
        for (var k = 0; k < n; k++)
            ConstructCrafting.Cancel(world, c, t, ids[k]);
    }

    /// <summary>The frozen body becomes a normal construct where it stands.</summary>
    private static void Free(ConstructWorld world, Construct b, StationJob j)
    {
        b.HeldBy = 0;
        b.Frozen = j.Bp?.FrozenBase ?? false;
        b.Dirty = true;
        b.Rebuild();
        b.Wake();
        world.StructureDirty.Add(b.Id);
    }

    /// <summary>Server: Cancel: the orders go, the frozen body is taken apart and its parts go back (racks with room, else beside the bay), a few a step.</summary>
    public static string? Cancel(ConstructWorld world, Construct c, int t)
    {
        if (c.Parts[t].Station is not { } j || j.State == StationState.Idle)
            return "nothing to cancel";
        CancelOrders(world, c, t);
        if (j.State is StationState.Costing or StationState.Building && world.Get(j.Body) is { HeldBy: not 0 } b)
        {
            for (var i = 0; i < b.PartCount; i++)
            {
                var id = (ushort)b.Parts[i].Def;
                var k = j.Return.FindIndex(r => r.Def == id && !r.Loose);
                if (k < 0)
                    j.Return.Add((id, 1, false));
                else
                    j.Return[k] = (id, j.Return[k].Count + 1, false);
            }
            world.Remove(b);
        }
        j.State = StationState.Idle;
        j.Body = 0;
        j.Repeat = 0;
        j.Costing = null;
        j.Run = null;
        j.Fitted = j.LinksDone = 0;
        j.Note = j.Return.Count > 0 ? "cancelled: giving the parts back" : "";
        world.StationOpened(c);
        world.StationChanged(c);
        return null;
    }

    /// <summary>Server: Release early: what is fitted becomes a normal construct; arrived parts not fitted drop beside the bay; orders are cancelled.</summary>
    public static string? Release(ConstructWorld world, Construct c, int t)
    {
        if (c.Parts[t].Station is not { State: StationState.Building } j || world.StoreOf(c, t) is not { } tray)
            return "nothing is being built";
        CancelOrders(world, c, t);
        if (world.Get(j.Body) is { } b)
            Free(world, b, j);
        for (var i = 0; i < j.KindN; i++)
            if (ConstructParts.Get(j.KindDef[i]) is { } d)
            {
                var n = 0;
                while (n < j.KindNeed[i] - j.KindFit[i] && ConstructCrafting.TakeOne(world, c, t, tray, d))
                    n++;
                if (n > 0)
                    j.Return.Add((j.KindDef[i], n, true));
            }
        j.State = StationState.Done;
        j.Repeat = 0;
        j.Note = "released early: finish it by hand";
        world.StationOpened(c);
        world.StationChanged(c);
        return null;
    }

    /// <summary>Server: the repeat count of a job.</summary>
    public static string? SetRepeat(ConstructWorld world, Construct c, int t, ushort n)
    {
        if (c.Parts[t].Station is not { } j || j.State == StationState.Idle)
            return "pick a blueprint first";
        j.Repeat = n;
        world.StationOpened(c);
        world.StationChanged(c);
        return null;
    }

    /// <summary>
    /// Server: a part from a player's hands. Fitted at once when a step of its kind has its parent in place (by hand is faster),
    /// else into the hold. Null and <paramref name="took"/>, or why not.
    /// </summary>
    public static string? Give(ConstructWorld world, Construct c, int t, ConstructPartDef d, out bool took)
    {
        took = false;
        if (c.Parts[t].Station is not { State: StationState.Building } j || world.StoreOf(c, t) is not { } tray)
            return "nothing is being built";
        var kind = KindOf(j, (ushort)d.Id);
        if (kind < 0)
            return $"the {j.Name} has no {d.Name}";
        if (j.KindFit[kind] + ConstructCrafting.Holds(tray, (ushort)d.Id) >= j.KindNeed[kind])
            return $"the build has every {d.Name} it needs";
        var k = NextStep(j, null, (ushort)d.Id);
        if (k >= 0)
        {
            took = true;
            FitOne(world, c, t, j, k);
            return null;
        }
        if (!world.RackPut(c, t, d))
            return $"no step takes the {d.Name} yet, and the hold has no room for it";
        took = true;
        world.CraftChanged(c);
        return null;
    }

    /// <summary>Is step <paramref name="k"/>'s parent in place (and the steps its "also" links name)?</summary>
    private static bool Ready(StationJob j, int k)
    {
        var bp = j.Bp!;
        if (k == 0)
            return true;
        var on = bp.Steps[k].On;
        if (on < 0 || on >= j.PartOf.Length || j.PartOf[on] < 0)
            return false;
        if (k < bp.StepAlso.Count && bp.StepAlso[k] is { } also)
            foreach (var a in also)
                if (a.On < 0 || a.On >= j.PartOf.Length || j.PartOf[a.On] < 0)
                    return false;
        return true;
    }

    /// <summary>The first unfitted step whose parent is in place and whose kind the hold has (<paramref name="only"/>: that kind, held or not). -1: none.</summary>
    private static int NextStep(StationJob j, CStore? tray, ushort only = None)
    {
        var bp = j.Bp!;
        while (j.Cursor < j.PartOf.Length && j.PartOf[j.Cursor] >= 0)
            j.Cursor++;
        ushort lastDef = None;
        var lastHeld = false;
        for (var k = j.Cursor; k < j.PartOf.Length; k++)
        {
            if (j.PartOf[k] >= 0 || !Ready(j, k))
                continue;
            var def = (ushort)(ConstructParts.Find(bp.Steps[k].Part)?.Id ?? None);
            if (only != None)
            {
                if (def == only)
                    return k;
                continue;
            }
            if (def != lastDef)
                (lastDef, lastHeld) = (def, def != None && ConstructCrafting.Holds(tray!, def) > 0);
            if (lastHeld)
                return k;
        }
        return -1;
    }

    /// <summary>One step goes on the frozen body by the blueprint's own rules (the body is made with step 0), with its paint and finish.</summary>
    private static void FitOne(ConstructWorld world, Construct c, int t, StationJob j, int k)
    {
        var bp = j.Bp!;
        var b = world.Get(j.Body);
        if (b == null)
        {
            if (k != 0 || world.Create(j.Pos, j.Rot) is not { } made)
            {
                Fail(world, c, t, j, k != 0 ? "the build is gone" : "too many constructs");
                return;
            }
            b = made;
            b.Frozen = true;
            b.HeldBy = c.Id;
            b.Owner = c.Owner;
            j.Body = b.Id;
        }
        if (ConstructBuild.FitStep(world, b, bp, k, j.PartOf) is { } why)
        {
            if (j.PartOf[k] >= 0 && b.PartCount == j.Fitted + 1)
                j.Fitted++; // (the part went on before the refusal: the count stays the body's)
            Fail(world, c, t, j, why);
            return;
        }
        ref var p = ref b.Parts[j.PartOf[k]];
        if (k < bp.StepPaint.Count && bp.StepPaint[k] != 0)
            p.Paint = bp.StepPaint[k];
        if (k < bp.StepFinish.Count && bp.StepFinish[k] != 0)
            p.Finish = bp.StepFinish[k];
        b.Dirty = true;
        b.Rebuild();
        b.PrevPos = b.NetPos = b.NetFromPos = b.Pos = j.Pos;
        world.StructureDirty.Add(b.Id);
        j.Fitted++;
        var kind = KindOf(j, (ushort)p.Def);
        if (kind >= 0)
            j.KindFit[kind]++;
        j.LastPart = (ushort)j.PartOf[k];
        j.FitSeq++;
        world.StationChanged(c);
    }

    private static int FitTicksFor(ConstructPartDef d) =>
        (int)(MathF.Min(FitSecondsMax, FitSeconds + d.Mass * FitSecondsPerKg) * World.TickRate);

    /// <summary>The next wire or link: laid when the steps it names are fitted and its stock is in a linked bin. True when the job moved on.</summary>
    private static bool NextLink(ConstructWorld world, Construct c, int t, StationJob j, Construct b)
    {
        var bp = j.Bp!;
        var i = j.LinkAt;
        var wire = i < bp.Wires.Count;
        var type = wire ? RouteType.Data : bp.Links[i - bp.Wires.Count].Type;
        var metres = i < j.LinkLen.Length ? j.LinkLen[i] : 1f;
        var units = Math.Max(1, (int)MathF.Ceiling(metres));
        var stock = type is RouteType.Data or RouteType.Power ? WireStock : PipeStock;
        var material = RecyclingTable.Current.IdOf(stock);
        var net = ConstructCrafting.NetOf(world, c, t);
        if (ConstructCrafting.Bulk(world, c, net, material, -1) < units)
        {
            var note = $"waits for {units} {stock} in a linked bin: stock for {metres:0.0} m of {ConstructRoutes.Name(type)}";
            if (j.Note != note)
            {
                j.Note = note;
                world.StationChanged(c);
            }
            return false;
        }
        string? bad;
        if (wire)
        {
            var (from, to) = bp.Wires[i];
            bad = from < 0 || from >= j.PartOf.Length || to < 0 || to >= j.PartOf.Length ? "no such step" : ConstructBuild.CanWire(world, b, j.PartOf[from], j.PartOf[to]);
            if (bad == null)
                world.AddWire(b, j.PartOf[from], j.PartOf[to], rebuild: false);
        }
        else
            bad = ConstructRoutes.BuildLink(world, b, bp.Links[i - bp.Wires.Count], j.PartOf.AsSpan(0, bp.Steps.Count));
        if (bad == null)
        {
            var left = units;
            for (var s = 0; s < net.StoreN && left > 0; s++)
                if (world.StoreOf(c, net.Stores[s]) is { Amount: > 0 } st && st.Material == material)
                    {
                    var take = Math.Min(left, st.Amount);
                    world.TakeBulk(st, take);
                    left -= take;
                }
            b.Dirty = true;
            b.Rebuild();
            world.StructureDirty.Add(b.Id);
            j.LinkSeq++;
        }
        else
            Console.WriteLine($"[station] {j.Name}: link {i} left out: {bad}");
        j.LinkAt++;
        j.LinksDone++;
        j.Note = bad == null ? "" : $"a link was left out: {bad}";
        world.StationChanged(c);
        return true;
    }

    /// <summary>Every part and link is in: the blueprint's finish (fused units, hoses, ropes, tracks, cargo), then it is a normal construct.</summary>
    private static void Finish(ConstructWorld world, Construct c, int t, StationJob j, Construct b)
    {
        var run = new ConstructBuild.BuildRun();
        ConstructBuild.FinishBuild(world, b, j.Bp!, j.PartOf, run, linked: true);
        if (run.Built == null)
        {
            j.Body = 0;
            Fail(world, c, t, j, $"its finish refused and the build was lost: {run.Why}");
            return;
        }
        Free(world, b, j);
        j.State = StationState.Done;
        j.Note = "";
        var s = (Stopwatch.GetTimestamp() - j.Started) / (double)Stopwatch.Frequency;
        Console.WriteLine($"[station] {j.Name}: done in {s:0.0} s ({j.Steps} parts, {j.Links} links); costing {j.CostMs:0.0} ms, worst slice {j.WorstMs:0.00} ms");
        world.StationChanged(c);
    }

    /// <summary>The costing's slice; at its end the bounds, the fit, the pose, the links' lengths and the kinds.</summary>
    private static void Cost(ConstructWorld world, Construct c, int t, StationJob j)
    {
        var start = Stopwatch.GetTimestamp();
        var until = start + (long)(CostBudgetMs * Stopwatch.Frequency / 1000);
        var more = true;
        while (more && Stopwatch.GetTimestamp() < until)
            more = j.Costing!.MoveNext();
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        j.CostMs += ms;
        j.WorstMs = Math.Max(j.WorstMs, ms);
        if (more)
            return;
        j.Costing!.Dispose();
        j.Costing = null;
        var run = j.Run!;
        j.Run = null;
        if (run.Built is not { } built)
        {
            Fail(world, c, t, j, $"the blueprint doesn't build: {run.Why}");
            return;
        }
        var bp = j.Bp!;
        Bounds(built, out var lo, out var size);
        j.Size = size;
        Bay(c, t, out var floor, out var rot, out var bay);
        j.Links = (ushort)Math.Min(None - 1, bp.Wires.Count + bp.Links.Count);
        j.LinkLen = new float[j.Links];
        for (var i = 0; i < j.Links; i++)
            j.LinkLen[i] = i < built.RouteCount ? MathF.Max(0.1f, ConstructConveyor.Length(built, i)) : 1f;
        MakeKinds(j);
        Console.WriteLine($"[station] {j.Name}: costed in {j.CostMs:0.0} ms, worst slice {j.WorstMs:0.00} ms; {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, {j.KindN} kinds");
        if (j.Resume)
        {
            j.Resume = false;
            for (var k = 0; k < j.PartOf.Length; k++)
                if (j.PartOf[k] >= 0 && KindOf(j, (ushort)(ConstructParts.Find(bp.Steps[k].Part)?.Id ?? None)) is var kind and >= 0)
                    j.KindFit[kind]++;
            if (world.Get(j.Body) is { } held)
            {
                (j.Pos, j.Rot) = (held.Pos, held.Rot);
                held.HeldBy = c.Id;
            }
            else
                Place(j, floor, rot, lo, size);
        }
        else
        {
            if (FitRefusal(size, bay) is { } no)
            {
                Fail(world, c, t, j, no);
                return;
            }
            Place(j, floor, rot, lo, size);
            j.PartOf = new int[bp.Steps.Count];
            Array.Fill(j.PartOf, -1);
            j.LinkAt = 0;
            j.Fitted = j.LinksDone = 0;
            j.Cursor = 0;
        }
        j.State = StationState.Building;
        j.FitTicks = World.TickRate / 2;
        j.OrderTicks = OrderEvery;
        world.StationChanged(c);
    }

    private static void Place(StationJob j, Vector3 floor, Quaternion rot, Vector3 lo, Vector3 size)
    {
        j.Rot = rot;
        j.Pos = floor - Vector3.Transform(new Vector3(lo.X + size.X * 0.5f, lo.Y - 0.02f, lo.Z + size.Z * 0.5f), rot);
    }

    /// <summary>Is the bay clear of the construct just built (Repeat waits for it)?</summary>
    private static bool BayClear(ConstructWorld world, Construct c, int t, StationJob j)
    {
        if (world.Get(j.Body) is not { } b)
            return true;
        Bay(c, t, out var floor, out var rot, out var bay);
        var local = Vector3.Transform(b.WorldCom - floor, Quaternion.Conjugate(rot));
        var r = b.Radius;
        return MathF.Abs(local.X) > bay.X * 0.5f + r || MathF.Abs(local.Z) > bay.Z * 0.5f + r || local.Y > bay.Y + r || local.Y < -r;
    }

    /// <summary>Parts given back: a rack with room on the network, else the hold, else loose beside the bay.</summary>
    private static void GiveBack(ConstructWorld world, Construct c, int t, StationJob j)
    {
        var net = ConstructCrafting.NetOf(world, c, t);
        Bay(c, t, out var floor, out var rot, out var bay);
        for (var n = 0; n < ReturnAStep && j.Return.Count > 0; n++)
        {
            var (def, count, loose) = j.Return[^1];
            j.Return[^1] = (def, count - 1, loose);
            if (count <= 1)
                j.Return.RemoveAt(j.Return.Count - 1);
            if (ConstructParts.Get(def) is not { } d)
                continue;
            var put = false;
            for (var s = 0; s < net.StoreN && !put && !loose; s++)
                put = c.DefOf(net.Stores[s]).IsPieceRack && world.RackPut(c, net.Stores[s], d);
            if (!put && !loose)
                put = world.RackPut(c, t, d);
            if (!put)
            {
                // Beside the bay, in a row along its far side.
                var k = (j.Return.Count * 7 + count) % 16;
                var at = floor + Vector3.Transform(new Vector3(bay.X * 0.5f + 1.2f + k / 8 * 1.2f, 0.3f, (k % 8 - 3.5f) * bay.Z / 8f), rot);
                if (ConstructBuild.PlaceNew(world, d, at, 0) == null)
                {
                    j.Return.Add((def, 1, loose)); // (the world is full: it waits)
                    j.Note = "no room to give parts back: they wait";
                    return;
                }
            }
        }
        if (j.Return.Count == 0 && j.State == StationState.Idle)
            j.Note = "";
        world.StationChanged(c);
    }

    /// <summary>Server, every tick: nothing while no station has a job.</summary>
    public static void Step(ConstructWorld world)
    {
        if (world.StationOpen.Count == 0)
            return;
        for (var bi = world.StationOpen.Count - 1; bi >= 0; bi--)
        {
            var any = false;
            if (world.Get(world.StationOpen[bi]) is { } c)
                for (var t = 0; t < c.PartCount; t++)
                    if (c.Parts[t].Station is { } j && c.Parts[t].D.IsBuildStation)
                        any |= StepJob(world, c, t, j);
            if (!any)
                world.StationOpen.RemoveAt(bi);
        }
    }

    /// <summary>One station's tick. False when it has nothing more to do (it leaves the open list).</summary>
    private static bool StepJob(ConstructWorld world, Construct c, int t, StationJob j)
    {
        if (j.State == StationState.Costing)
        {
            if (j.Costing == null)
                BeginCosting(world, j); // (after a load)
            Cost(world, c, t, j);
            return true;
        }
        if (++j.OrderTicks >= OrderEvery)
        {
            j.OrderTicks = 0;
            if (j.Return.Count > 0)
                GiveBack(world, c, t, j);
            if (j.State == StationState.Building && world.StoreOf(c, t) is { } hold && ConstructRecycler.Powered(world, c, t))
                Orders(world, c, t, hold, j);
            if (j.State == StationState.Done && j.Repeat > 0 && j.Bp != null && BayClear(world, c, t, j))
            {
                // Repeat: the bay is clear, so the bill goes out again.
                if (j.Repeat != None)
                    j.Repeat--;
                Bay(c, t, out _, out _, out var bay);
                if (FitRefusal(j.Size, bay) is { } no)
                {
                    Fail(world, c, t, j, no);
                    return false;
                }
                j.Body = 0;
                Array.Fill(j.PartOf, -1);
                Array.Clear(j.KindFit);
                j.LinkAt = j.Cursor = 0;
                j.Fitted = j.LinksDone = 0;
                j.LastPart = None;
                j.Started = Stopwatch.GetTimestamp();
                j.State = StationState.Building;
                j.Note = "";
                world.StationChanged(c);
            }
        }
        if (j.State != StationState.Building)
            return j.Return.Count > 0 || j.State == StationState.Done && j.Repeat > 0;
        if (--j.FitTicks > 0)
            return true;
        j.FitTicks = OrderEvery; // (nothing to fit: look again in half a second)
        if (world.StoreOf(c, t) is not { } tray || !ConstructRecycler.Powered(world, c, t))
            return true;
        var b = world.Get(j.Body);
        if (j.Body != 0 && (b == null || b.PartCount != j.Fitted || b.HeldBy != c.Id))
        {
            Fail(world, c, t, j, b == null ? "the build was destroyed" : "the build was changed in the bay: released as it stands");
            return false;
        }
        if (j.Fitted < j.Steps)
        {
            var k = NextStep(j, tray);
            if (k < 0)
                return true;
            var d = ConstructParts.Find(j.Bp!.Steps[k].Part)!;
            if (!ConstructCrafting.TakeOne(world, c, t, tray, d))
                return true;
            world.CraftChanged(c);
            FitOne(world, c, t, j, k);
            j.FitTicks = FitTicksFor(d);
            return j.State == StationState.Building;
        }
        if (b == null)
            return true;
        if (j.LinkAt < j.Links)
        {
            if (NextLink(world, c, t, j, b))
                j.FitTicks = (int)(LinkSeconds * World.TickRate);
            return true;
        }
        Finish(world, c, t, j, b);
        return j.State == StationState.Done && j.Repeat > 0;
    }

    // ---- The wire (CBodyKind.Station, protocol 255) ------------------------------------------------------------------------

    public static bool Any(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Station != null)
                return true;
        return false;
    }

    public static void Write(PacketWriter w, Construct c)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Station).U16(c.Id);
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Station is not { } j)
                continue;
            w.U16((ushort)i).U8((byte)j.State).Str(j.Name).U16(j.Fitted).U16(j.Steps).U16(j.LinksDone).U16(j.Links).U16(j.Repeat).U16(j.Body).U16(j.LastPart).U8(j.FitSeq).U8(j.LinkSeq).Str(j.Note);
            n++;
        }
        w.PatchU16(at, (ushort)n);
    }

    public static void Read(ref PacketReader r, ConstructWorld world)
    {
        var c = world.Get(r.U16());
        int n = r.U16();
        for (var k = 0; k < n; k++)
        {
            int part = r.U16();
            var state = (StationState)r.U8();
            var name = r.Str();
            var fitted = r.U16();
            var steps = r.U16();
            var linksDone = r.U16();
            var links = r.U16();
            var repeat = r.U16();
            var body = r.U16();
            var last = r.U16();
            var fitSeq = r.U8();
            var linkSeq = r.U8();
            var note = r.Str();
            if (c == null || part >= c.PartCount || !c.DefOf(part).IsBuildStation)
                continue;
            var j = c.Parts[part].Station;
            if (j == null)
                c.Parts[part].Station = j = new StationJob { SeenFit = fitSeq, SeenLink = linkSeq, HeardFit = fitSeq, HeardLink = linkSeq }; // (a joiner sees no burst of old effects)
            (j.State, j.Name, j.Fitted, j.Steps, j.LinksDone, j.Links, j.Repeat, j.Body, j.LastPart, j.FitSeq, j.LinkSeq, j.Note) = (state, name, fitted, steps, linksDone, links, repeat, body, last, fitSeq, linkSeq, note);
        }
    }

    // ---- Saves (after a body's crafting orders: an older save ends before it) -----------------------------------------------

    public static (ushort Part, StationJob Job)[]? Snapshot(Construct c)
    {
        List<(ushort, StationJob)>? o = null;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Station is { } j && j.State is not (StationState.Idle or StationState.Failed))
                (o ??= new()).Add(((ushort)i, new StationJob
                {
                    Name = j.Name, State = j.State, Body = j.Body, Repeat = j.Repeat, Steps = j.Steps, Fitted = j.Fitted, LinksDone = j.LinksDone,
                    LinkAt = j.LinkAt, PartOf = (int[])j.PartOf.Clone(),
                }));
        return o?.ToArray();
    }

    public static void WriteSave(SaveWriter w, (ushort Part, StationJob Job)[] jobs)
    {
        w.U16((ushort)jobs.Length);
        foreach (var (part, j) in jobs)
        {
            var fresh = j.State == StationState.Costing && j.PartOf.Length == 0; // (still costing: it starts again)
            w.U16(part).Str(j.Name).U8((byte)j.State).U16(j.Repeat).U16(j.Body).U16((ushort)j.LinkAt).U16(j.LinksDone).U16((ushort)(fresh ? 0 : j.PartOf.Length));
            if (!fresh)
                foreach (var p in j.PartOf)
                    w.U16(p < 0 ? None : (ushort)p);
        }
    }

    public static (ushort Part, StationJob Job)[]? ReadSave(SaveReader r, int bodyEnd)
    {
        if (!r.Has(2) || r.Pos + 2 > bodyEnd)
            return null;
        int n = r.U16();
        var jobs = new List<(ushort, StationJob)>(n);
        for (var i = 0; i < n; i++)
        {
            var part = r.U16();
            var j = new StationJob { Name = r.Str(), State = (StationState)r.U8(), Repeat = r.U16(), Body = r.U16(), LinkAt = r.U16(), LinksDone = r.U16() };
            int steps = r.U16();
            j.PartOf = new int[steps];
            for (var k = 0; k < steps; k++)
            {
                var p = r.U16();
                j.PartOf[k] = p == None ? -1 : p;
                if (p != None)
                    j.Fitted++;
            }
            jobs.Add((part, j));
        }
        return jobs.ToArray();
    }

    /// <summary>Puts a save's jobs on the body's stations: a job that was building costs its blueprint again (sliced) and goes on from its fitted set.</summary>
    public static void Apply(ConstructWorld world, Construct c, (ushort Part, StationJob Job)[] jobs)
    {
        foreach (var (part, j) in jobs)
        {
            if (part >= c.PartCount || !c.DefOf(part).IsBuildStation || Find(j.Name) is not { } bp)
                continue;
            j.Bp = bp;
            j.Steps = (ushort)bp.Steps.Count;
            j.Started = Stopwatch.GetTimestamp();
            if (j.State == StationState.Done)
                continue; // (what it built is an ordinary body; a repeat is asked for again)
            var resume = j.PartOf.Length == bp.Steps.Count && j.State == StationState.Building;
            if (!resume)
            {
                j.PartOf = [];
                j.Body = 0;
                j.Fitted = j.LinksDone = 0;
                j.LinkAt = 0;
            }
            j.Resume = resume;
            j.State = StationState.Costing;
            c.Parts[part].Station = j;
            world.StationOpened(c);
            world.StationChanged(c);
        }
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<ushort> StationOpen = new(8); // server: bodies whose stations have work (the step walks only these)
    public readonly List<ushort> StationDirty = new(8); // bodies whose stations' state changed (CBodyKind.Station)

    public void StationOpened(Construct c)
    {
        if (!StationOpen.Contains(c.Id))
            StationOpen.Add(c.Id);
    }

    public void StationChanged(Construct c)
    {
        if (!StationDirty.Contains(c.Id))
            StationDirty.Add(c.Id);
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: a player's action at a Building Station (CActKind.Station), under the rule of a finger on a console: reach, power, who
    /// may configure the station's construct. Give is aimed at the station or at the build it holds. Null, or why not.
    /// </summary>
    public string? StationAct(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, StationOp op, ushort n, string name, string? refusal)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return "that station is gone";
        var d = c.DefOf(part);
        if (!d.IsBuildStation)
            return $"a {d.Name} builds no blueprints";
        if (!InfiniteParts && ConstructGrab.Defended(c, player, AliveAt, NameOf) is { } defended)
            return defended;
        if (refusal != null)
            return refusal;
        var reach = Vector3.Distance(eye, ReachPoint(c, part, eye));
        if (op == StationOp.Give && c.Parts[part].Station is { Body: not 0 } held && world.Get(held.Body) is { } build)
            reach = MathF.Min(reach, Vector3.Distance(eye, build.WorldCom) - build.Radius);
        if (reach > ConstructConfig.Reach)
            return "too far away";
        if (!ConstructRecycler.Powered(world, c, part))
            return $"the {d.Name} has no power";
        string? why;
        switch (op)
        {
            case StationOp.Start:
                why = ConstructStation.Start(world, c, part, name, n);
                break;
            case StationOp.Cancel:
                why = ConstructStation.Cancel(world, c, part);
                break;
            case StationOp.Release:
                why = ConstructStation.Release(world, c, part);
                break;
            case StationOp.Repeat:
                why = ConstructStation.SetRepeat(world, c, part, n);
                break;
            case StationOp.Give:
            {
                ref var h = ref Hands[player];
                if (ConstructParts.Get(h.Held) is not { } inHand)
                    return "your hands are empty: carry a part the build needs";
                why = ConstructStation.Give(world, c, part, inHand, out var took);
                if (took && !InfiniteParts)
                    h.Held = ConstructParts.None;
                break;
            }
            default:
                return "a bad action";
        }
        if (why == null)
            Done.Add($"player {player}: station {op} {name} at part {part} of construct {c.Id}");
        return why;
    }
}

/// <summary>
/// What a Building Station's panel shows of a blueprint before Start, worked out on the peer that looks (nothing of it is on the
/// wire): whether it fits the bay, and its bill by group with what the hold and the linked stores have, where the rest would be
/// ordered, and what nothing linked makes. The blueprint is built in a scratch world a slice a frame (<see cref="Step"/>), so a
/// 5,000-part one never holds a frame.
/// </summary>
public sealed class StationPreview
{
    public readonly List<(string Text, byte Tone)> Lines = new(64); // Tone 0 a heading, 1 held or in stock, 2 to order, 3 nothing makes it
    public string Name = "", Why = "";
    public bool Ready;
    public Vector3 Size;
    public int Parts, Kinds, Unmade;
    public double CostMs, WorstMs;
    private ConstructBuild.Blueprint? _bp;
    private ConstructBuild.BuildRun? _run;
    private IEnumerator<int>? _steps;
    private Construct? _built;

    public void Begin(ConstructBuild.Blueprint bp)
    {
        _steps?.Dispose();
        (_bp, Name, Ready, Why, _built) = (bp, bp.Name, false, "", null);
        Lines.Clear();
        CostMs = WorstMs = 0;
        _run = new ConstructBuild.BuildRun { KeepPartOf = true };
        _steps = ConstructBuild.BuildSteps(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, _run).GetEnumerator();
    }

    /// <summary>A slice of the scratch build. True once it is done (<see cref="Why"/>: it doesn't build).</summary>
    public bool Step(double budgetMs)
    {
        if (Ready || _steps == null)
            return Ready;
        var start = Stopwatch.GetTimestamp();
        var until = start + (long)(budgetMs * Stopwatch.Frequency / 1000);
        var more = true;
        while (more && Stopwatch.GetTimestamp() < until)
            more = _steps.MoveNext();
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        CostMs += ms;
        WorstMs = Math.Max(WorstMs, ms);
        if (more)
            return false;
        _steps.Dispose();
        _steps = null;
        Ready = true;
        _built = _run!.Built;
        if (_built == null)
            Why = _run.Why;
        else
            ConstructStation.Bounds(_built, out _, out Size);
        _run = null;
        return true;
    }

    /// <summary>The bill's lines as the station's network stands now (the panel asks twice a second while open).</summary>
    public void Fill(ConstructWorld world, Construct c, int t)
    {
        Lines.Clear();
        Parts = Kinds = Unmade = 0;
        if (!Ready || _bp == null || _built == null || world.StoreOf(c, t) is not { } tray)
            return;
        var net = ConstructCrafting.NetOf(world, c, t);
        var names = new List<string>();
        var counts = new List<int>();
        foreach (var step in _bp.Steps)
        {
            var k = names.IndexOf(step.Part);
            if (k < 0)
            {
                names.Add(step.Part);
                counts.Add(1);
            }
            else
                counts[k]++;
        }
        Parts = _bp.Steps.Count;
        Kinds = names.Count;
        for (var group = 0; group < 3; group++)
        {
            var head = false;
            for (var i = 0; i < names.Count; i++)
            {
                var d = ConstructParts.Find(names[i]);
                var g = HullLoft.IsFormedName(names[i]) ? 1 : names[i].StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 2;
                if (g != group)
                    continue;
                if (!head)
                    Lines.Add((group == 0 ? "PIECES (an Extruder's, a Former's)" : group == 1 ? "PANELS (a Panel Former's)" : "CATALOGUE PARTS", 0));
                head = true;
                if (d == null)
                {
                    Lines.Add(($"{counts[i]} x {names[i]}: NOT A PART THIS BUILD HAS", 3));
                    Unmade++;
                    continue;
                }
                var held = Math.Min(counts[i], ConstructCrafting.Holds(tray, (ushort)d.Id));
                var stock = Math.Min(counts[i] - held, ConstructCrafting.Stock(world, c, net, (ushort)d.Id));
                var order = counts[i] - held - stock;
                var text = $"{counts[i]} x {d.Name}:";
                if (held > 0)
                    text += $" {held} in the hold,";
                if (stock > 0)
                    text += $" {stock} in linked stores,";
                if (order <= 0)
                {
                    Lines.Add((text.TrimEnd(','), 1));
                    continue;
                }
                var what = ConstructStation.OrderFor(d, order);
                string? maker = null, refusal = null;
                for (var m = 0; m < net.MachineN && maker == null && !what.IsPanel; m++)
                    if (WorkOrders.Refusal(world, c, net.Machines[m], what) is { } no)
                        refusal = refusal == null || c.DefOf(net.Machines[m]).IsAssembler != what.IsPiece ? no : refusal;
                    else
                        maker = c.DefOf(net.Machines[m]).Name;
                if (maker != null)
                    Lines.Add(($"{text} order {order} at the {maker}", 2));
                else
                {
                    Unmade++;
                    var why = what.IsPanel ? "a Panel Former's order isn't sent by the station yet" : refusal ?? (net.MachineN == 0 ? "no machine is linked" : "no linked machine makes it");
                    Lines.Add(($"{text} {order} NOTHING MAKES: {why} - bring {(order == 1 ? "it" : "them")} by hand", 3));
                }
            }
        }
        // Link stock, by the metre.
        float wire = 0, pipe = 0;
        var links = 0;
        for (var r = 0; r < _built.RouteCount; r++)
        {
            var len = MathF.Ceiling(MathF.Max(0.1f, ConstructConveyor.Length(_built, r)));
            if (_built.Routes[r].Type is RouteType.Data or RouteType.Power)
                wire += len;
            else
                pipe += len;
            links++;
        }
        if (links > 0)
        {
            Lines.Add(($"LINK STOCK ({links} link{(links == 1 ? "" : "s")}, from linked bins)", 0));
            Stock(world, c, net, ConstructStation.WireStock, "wires and cables", wire);
            Stock(world, c, net, ConstructStation.PipeStock, "pipes, chutes, shafts and feeds", pipe);
        }
        Lines.Add(("FLUIDS AND AMMO", 0));
        Lines.Add(("not filled by the station yet: tanks and magazines are as the blueprint spawns them", 1));
    }

    private void Stock(ConstructWorld world, Construct c, CraftNet net, string material, string what, float metres)
    {
        if (metres <= 0)
            return;
        var have = ConstructCrafting.Bulk(world, c, net, RecyclingTable.Current.IdOf(material), -1);
        Lines.Add(($"{metres:0} {material} for {metres:0} m of {what}: {Math.Min(have, (int)metres)} in linked bins{(have < metres ? $", {metres - have:0} MISSING (the links wait)" : "")}", (byte)(have < metres ? 3 : 1)));
    }
}

public static class StationProbe
{
    /// <summary>The window tool (host): every part the job still lacks goes straight into the hold, as if it had been delivered. What happened, in words.</summary>
    public static string Stock(ConstructWorld world, Construct c, int t)
    {
        if (c.Parts[t].Station is not { State: StationState.Building } j || world.StoreOf(c, t) is not { } tray)
            return "nothing is being built";
        int put = 0, refused = 0;
        var first = "";
        for (var i = 0; i < j.KindN; i++)
        {
            if (ConstructParts.Get(j.KindDef[i]) is not { } d)
                continue;
            for (var n = j.KindNeed[i] - j.KindFit[i] - ConstructCrafting.Holds(tray, j.KindDef[i]); n > 0; n--)
                if (world.RackPut(c, t, d))
                    put++;
                else
                {
                    refused++;
                    if (first.Length == 0)
                        first = $"{d.Name}: {ConstructConveyor.RackRefusal(c, t, tray, d)}";
                }
        }
        world.CraftChanged(c);
        return $"{put} part(s) into the hold{(refused > 0 ? $", {refused} refused ({first})" : "")}";
    }

    /// <summary>A job in one line, for the window tool's dump.</summary>
    public static string Text(ConstructWorld world, Construct c, int t)
    {
        var j = c.Parts[t].Station;
        var b = j == null ? null : world.Get(j.Body);
        return j == null ? "no job" : $"{j.State} \"{j.Name}\" parts {j.Fitted}/{j.Steps} links {j.LinksDone}/{j.Links} repeat {j.Repeat} body {j.Body} ({(b == null ? "none" : $"{b.PartCount} parts, {b.RouteCount} links, {(b.Frozen ? "frozen" : "free")}")}) hold {world.StoreOf(c, t)?.ItemCount ?? -1} orders {c.Parts[t].Craft?.Count ?? 0} note \"{j.Note}\"";
    }
}
