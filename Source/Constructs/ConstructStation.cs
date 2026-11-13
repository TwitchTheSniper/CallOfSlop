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
public sealed partial class StationJob
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

    public IEnumerator<int>? Costing;
    public ConstructBuild.BuildRun? Run;
    public bool Resume; // costing after a load: PartOf, Body and LinkAt are the save's
    public Vector3 Pos; // Body's pose in the bay
    public Quaternion Rot = Quaternion.Identity;
    public Vector3 Size; // the blueprint's bounds
    public int FitTicks, OrderTicks, NoteKey;
    public int FillKey; // the fill stage's counts as its note last said them
    public int Cursor; // every step before it is fitted
    public ushort[] KindDef = [], KindNeed = [], KindFit = [];
    public string?[] KindWhy = []; // why nothing linked holds or makes it (null: ordered or held)
    public int KindN;
    public int[] StepKind = [], KindReady = []; // each step's kind; how many unfitted steps of a kind have their parent in place (worked out at each look)
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
public static partial class ConstructStation
{
    public const float FitSeconds = 2f; // the core fits one part this often by itself...
    public const float FitSecondsPerKg = 0.02f; // ...plus this for its mass...
    public const float FitSecondsMax = 10f; // ...at most
    public const float LinkSeconds = 1f; // a wire or a link laid
    public const double CostBudgetMs = 1.0; // a tick's slice of the scratch build
    public const int OrderEvery = 30; // ticks between looks at what to order (2 Hz)
    public const int OrdersAStep = 4; // craft orders placed in one look
    public const int ReturnAStep = 8; // parts given back in one look
    public const float FillLitresALook = 50f; // fuel and fluids given the build in one look (2 Hz): 100 L a second (proposed)
    public const int AmmoUnitsALook = 10; // units of bulk ammo in one look: 200 rounds a second
    public const int GasUnitsALook = 4; // units of bulk gas from the linked bins in one look: 400 L a second (proposed)
    public const float ChargeKW = 50f; // a build's batteries charged at this, plus what the station's power plants make, from its own charge (proposed)
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

    /// <summary>
    /// What a body's parts take up, body frame, from the parts' own bounds (no rebuild: a 5,000-part scratch body is costed without one).
    /// A wheel's travel isn't counted; the build is set down by physics when it is freed.
    /// </summary>
    public static void PartBounds(Construct b, out Vector3 lo, out Vector3 size)
    {
        lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var inv = Quaternion.Conjugate(b.Rot);
        for (var i = 0; i < b.PartCount; i++)
        {
            var d = b.DefOf(i);
            Freeform.PartWorld(b, i, out var pos, out var rot);
            var centre = Vector3.Transform(pos + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, rot) - b.Pos, inv);
            var q = inv * rot;
            var h = (d.BoundsMax - d.BoundsMin) * 0.5f;
            var ax = Vector3.Abs(Vector3.Transform(Vector3.UnitX, q)) * h.X + Vector3.Abs(Vector3.Transform(Vector3.UnitY, q)) * h.Y + Vector3.Abs(Vector3.Transform(Vector3.UnitZ, q)) * h.Z;
            lo = Vector3.Min(lo, centre - ax);
            hi = Vector3.Max(hi, centre + ax);
        }
        if (b.PartCount == 0)
            lo = hi = Vector3.Zero;
        size = hi - lo;
    }

    /// <summary>About how long wire or link <paramref name="i"/> of a blueprint is (its wires first, then its links), on a body that has its end parts: m.</summary>
    public static float LinkEstimate(ConstructBuild.Blueprint bp, Construct b, int[] partOf, int i)
    {
        int from, to;
        if (i < bp.Wires.Count)
            (from, to) = bp.Wires[i];
        else
        {
            var s = bp.Links[i - bp.Wires.Count];
            (from, to) = (s.Branch < 0 ? s.From : -1, s.To);
        }
        if (from < 0 || to < 0 || from >= partOf.Length || to >= partOf.Length || partOf[from] < 0 || partOf[to] < 0 || partOf[from] >= b.PartCount || partOf[to] >= b.PartCount)
            return 2f;
        Freeform.PartWorld(b, partOf[from], out var a, out _);
        Freeform.PartWorld(b, partOf[to], out var z, out _);
        return MathF.Max(0.5f, Vector3.Distance(a, z) * 1.3f);
    }

    private static (float, float, float) Sorted(Vector3 v)
    {
        var hi = MathF.Max(v.X, MathF.Max(v.Y, v.Z));
        var lo = MathF.Min(v.X, MathF.Min(v.Y, v.Z));
        return (hi, v.X + v.Y + v.Z - hi - lo, lo);
    }

    /// <summary>
    /// Why a blueprint can't fit a bay, from its parts alone and without building it (a walk of its steps: 5,573 steps in about a
    /// millisecond), or null when only a build can tell: a part that no turn puts inside the bay, or parts whose boxes add up to more
    /// than one and a half times the bay's volume. Asked before any costing, on either peer.
    /// </summary>
    public static string? QuickRefusal(ConstructBuild.Blueprint bp, Vector3 bay)
    {
        var room = bay.X * bay.Y * bay.Z;

        double volume = 0;
        string last = "";
        ConstructPartDef? d = null;
        foreach (var step in bp.Steps)
        {
            if (step.Part != last)
                (last, d) = (step.Part, ConstructParts.Find(step.Part));
            if (d == null)
                continue;
            var s = d.BoundsMax - d.BoundsMin;
            // (a part fits when its sides, longest first, are no longer than the bay's, longest first)
            var (p0, p1, p2) = Sorted(s);
            var (b0, b1, b2) = Sorted(bay);
            if (p0 > b0 + 0.01f || p1 > b1 + 0.01f || p2 > b2 + 0.01f)
                return $"doesn't fit the bay: its {d.Name} alone is {s.X:0.0} x {s.Y:0.0} x {s.Z:0.0} m, the bay {bay.X:0.0} x {bay.Z:0.0} m and {bay.Y:0.0} m high (the station's Bay settings)";
            volume += s.X * s.Y * s.Z;
        }
        return volume > room * 1.5
            ? $"doesn't fit the bay: its {bp.Steps.Count} parts take about {volume:0} m3, the bay holds {room:0} m3 ({bay.X:0.0} x {bay.Z:0.0} m and {bay.Y:0.0} m high: the station's Bay settings)"
            : null;
    }

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
        StationState.Building => j.Fitted < j.Steps ? $"{j.Name}: {j.Fitted} of {j.Steps} parts" : j.LinksDone < j.Links ? $"{j.Name}: link {j.LinksDone} of {j.Links}" : $"{j.Name}: filling its tanks",
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
        if (!ConstructRecycler.Powered(world, c, t))
            sb.Append(" - NO POWER: it waits");
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
                if (j.Fitted >= j.Steps && j.LinksDone >= j.Links)
                    card.Add(4, j.Note.StartsWith("waits", StringComparison.Ordinal) ? "WAITS FOR FILL" : "Filling");
                else
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
        if (Pressed.IsName(d.Name) && WorkOrders.PressedOrder(d.Name, n) is { } pressed)
            return pressed; // (a pressed part by its key: a Press's order, Press phase 4)
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
        j.KindReady = new int[defs.Count];
        j.StepKind = new int[bp.Steps.Count];
        for (var s = 0; s < bp.Steps.Count; s++)
            j.StepKind[s] = defs.IndexOf((ushort)(ConstructParts.Find(bp.Steps[s].Part)?.Id ?? ConstructParts.None));
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
        // What can go on now: a kind is asked for when a step of it has its parent in place, so the hold never fills with parts that
        // can't be fitted yet; a part that came early (by hand, or left over) waits there.
        TakeDropped(world, c, t, tray, j);
        if (j.State != StationState.Building)
            return;
        Array.Clear(j.KindReady);
        while (j.Cursor < j.PartOf.Length && j.PartOf[j.Cursor] >= 0)
            j.Cursor++;
        for (var s = j.Cursor; s < j.PartOf.Length; s++)
            if (j.PartOf[s] < 0 && Ready(j, s))
                j.KindReady[j.StepKind[s]]++;
        var placed = 0;
        int firstKind = -1, firstOrder = -1, lacking = 0;
        for (var i = 0; i < j.KindN; i++)
        {
            var def = j.KindDef[i];
            var held = ConstructCrafting.Holds(tray, def);
            var lack = j.KindNeed[i] - j.KindFit[i] - held;
            if (lack <= 0)
            {
                j.KindWhy[i] = null;
                continue;
            }
            lacking += lack;
            var d = ConstructParts.Get(def);
            var asked = Asked(c.Parts[t].Craft, def);
            var want = Math.Clamp(j.KindReady[i] - held, 0, lack);
            if (want > asked && d != null && placed < OrdersAStep && (c.Parts[t].Craft?.Count ?? 0) < ConstructCrafting.MaxOrders)
            {
                placed++;
                j.KindWhy[i] = ConstructCrafting.Place(world, c, t, OrderFor(d, want - asked));
            }
            else if (d == null)
                j.KindWhy[i] = "no such part";
            if (firstKind < 0 && (j.KindReady[i] > 0 || j.KindWhy[i] != null))
            {
                firstKind = i; // (the first kind a step is ready for: what the build waits on by name)
                if (j.KindWhy[i] == null && c.Parts[t].Craft is { } cq)
                    for (var k = 0; k < cq.Count && firstOrder < 0; k++)
                        if (ConstructCrafting.KindOf(ref cq.Items[k]) == def)
                            firstOrder = k;
            }
        }
        if (j.Fitted >= j.Steps)
            return; // (its links: NextLink's own note)
        // The note is made again only when what it says changed (a look allocates nothing otherwise).
        var oq = c.Parts[t].Craft;
        var full = firstOrder >= 0 && oq!.Items[firstOrder].State == CraftState.Held && tray.ItemCount > 0 && oq.Items[firstOrder].Note.Contains("full", StringComparison.Ordinal);
        var key = lacking * 1009 + (firstKind + 1) * 31 + (firstOrder < 0 ? 0 : ((int)oq!.Items[firstOrder].State + 1) * 7 + oq.Items[firstOrder].Where + (oq.Items[firstOrder].Note?.GetHashCode() ?? 0))
            + (firstKind >= 0 ? j.KindWhy[firstKind]?.GetHashCode() ?? 0 : 0) + (full ? 3 : 0);
        if (key == j.NoteKey)
            return;
        j.NoteKey = key;
        var note = "";
        if (lacking > 0)
        {
            var name = firstKind < 0 ? "a part whose parent isn't in place" : ConstructParts.Get(j.KindDef[firstKind])?.Name ?? "a part this build doesn't have";
            // (a full hold empties as its parts are fitted: said as that, not as the terminal's "take parts out")
            var first = firstKind >= 0 && j.KindWhy[firstKind] is { } why ? $"{name}: {why}"
                : full ? $"{name} (next in: the hold is full until what it holds is fitted)"
                : firstOrder >= 0 ? $"{name} ({ConstructCrafting.StateText(c, oq!.Items[firstOrder])})" : name;
            note = $"waits for {lacking} part{(lacking == 1 ? "" : "s")}: {first}";
            if (note.Length > 120)
                note = note[..120];
        }
        if (note != j.Note)
        {
            j.Note = note;
            world.StationChanged(c);
        }
    }

    /// <summary>
    /// A loose part lying in the bay (a body of one part, held by nobody: set down by hand or dropped there with the physics gun) that
    /// the build still lacks is taken: fitted at once when a step of its kind has its parent in place, else into the hold. This is how a
    /// physics-only part, which no hands or backpack carry, is supplied by a player.
    /// </summary>
    private static void TakeDropped(ConstructWorld world, Construct c, int t, CStore tray, StationJob j)
    {
        Bay(c, t, out var floor, out var rot, out var bay);
        var inv = Quaternion.Conjugate(rot);
        Construct? found = null;
        foreach (var b in world.Bodies)
        {
            if (b.PartCount != 1 || b.HeldBy != 0 || b.GrabbedBy != 255 || b.Id == j.Body)
                continue;
            var local = Vector3.Transform(b.WorldCom - floor, inv);
            if (MathF.Abs(local.X) > bay.X * 0.5f || MathF.Abs(local.Z) > bay.Z * 0.5f || local.Y < -0.5f || local.Y > bay.Y)
                continue;
            var kind = KindOf(j, b.Parts[0].Def);
            if (kind < 0 || j.KindFit[kind] + ConstructCrafting.Holds(tray, b.Parts[0].Def) >= j.KindNeed[kind])
                continue;
            found = b;
            break;
        }
        if (found == null)
            return;
        var d = found.DefOf(0);
        var k = NextStep(j, null, (ushort)d.Id);
        if (k < 0 && ConstructConveyor.RackCheck(c, t, tray, d) != 0)
            return; // (no step takes it yet and the hold has no room: it lies there)
        world.Remove(found);
        if (k >= 0)
            FitOne(world, c, t, j, k);
        else
            world.RackPut(c, t, d);
        world.CraftChanged(c);
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
        if (c.Parts[t].Station is { Body: not 0 } was && !BayClear(world, c, t, was))
            return "the bay isn't clear: move what was built out of it first";
        if (Find(name) is not { } bp)
            return $"no blueprint named \"{name}\"";
        if (bp.Steps.Count == 0 || bp.Steps.Count >= None)
            return "that blueprint has no parts";
        if (QuickRefusal(bp, BaySize(c, t)) is { } small)
            return small; // (told from its parts alone: nothing is built to find out)
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
        j.Run = new ConstructBuild.BuildRun { StepsOnly = true }; // (the steps only, a slice a tick: the finish of a big blueprint is one long call, and the bay needs the bounds alone)
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
            if (j.PartOf[k] != -1 || !Ready(j, k)) // (-2: claimed by a lifter, its piece in the air: ConstructLifters.cs)
                continue;
            var def = j.StepKind[k] >= 0 ? j.KindDef[j.StepKind[k]] : None;
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
        var fuel0 = b.Fuel;
        var charge0 = b.Charge;
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
        if (!FillsFree(world))
            Empty(world, b, j.PartOf[k], fuel0, charge0);
        world.StructureDirty.Add(b.Id);
        j.Fitted++;
        var kind = KindOf(j, (ushort)p.Def);
        if (kind >= 0)
            j.KindFit[kind]++;
        j.LastPart = (ushort)j.PartOf[k];
        world.FitThisTick = true; // (one fit a tick a world, whoever fits: ConstructLifters.cs)
        j.FitSeq++;
        world.StationChanged(c);
    }

    /// <summary>Creative (machines untimed): the build's tanks and boxes come full, as a spawned blueprint's do.</summary>
    private static bool FillsFree(ConstructWorld world) => world.Creative && !world.TimedMachines;

    /// <summary>
    /// Survival: a part the station fits holds nothing (a fuel tank adds no fuel, an ammo box no rounds, a Fluid Tank none of its
    /// blueprint's fill); the fill stage (<see cref="Fill"/>) gives it what is piped to the station and what its linked bins hold.
    /// </summary>
    private static void Empty(ConstructWorld world, Construct b, int i, float fuel0, float charge0)
    {
        b.Fuel = MathF.Min(b.Fuel, fuel0); // (AddPart and the first rebuild fill a new tank: the pool stays what it was)
        b.Charge = MathF.Min(b.Charge, charge0); // (and the first rebuild charges the batteries)
        var d = b.DefOf(i);
        if (d.Role == PartRole.AmmoBox && b.Parts[i].Rounds > 0)
        {
            b.Parts[i].Rounds = 0;
            world.NoteAmmo(b, i);
        }
        else if (d.Role == PartRole.FluidTank && ConstructFluid.Held(b.Parts[i], d) != null)
            ConstructFluid.Lost(b, i);
    }

    /// <summary>What step <paramref name="k"/>'s Fluid Tank holds in its blueprint: the fluid and the litres (false: nothing a pipe carries).</summary>
    private static bool FluidWant(ConstructBuild.Blueprint bp, int k, Construct b, int i, ConstructPartDef d, out Fluid fluid, out float litres)
    {
        fluid = default;
        litres = 0;
        if (k >= bp.StepConfig.Count || bp.StepConfig[k] is not { } values)
            return false;
        int fluidSlot = d.CfgSlot[(int)CfgKey.Fluid], fillSlot = d.CfgSlot[(int)CfgKey.Fill];
        float f = 0, fill = 0;
        foreach (var (slot, value) in values)
            if (slot == fluidSlot)
                f = value;
            else if (slot == fillSlot)
                fill = value;
        if (f < 1 || fill <= 0 || (Fluid)((int)f - 1) is Fluid.HotAir or Fluid.Vacuum or Fluid.Exhaust)
            return false;
        fluid = (Fluid)((int)f - 1);
        litres = MathF.Min(fill / 100f * ConstructFluid.Capacity(b.Parts[i], d), ConstructFluid.Limit(b.Parts[i], d));
        return litres > 0.01f;
    }

    /// <summary>Up to <paramref name="litres"/> of <paramref name="fluid"/> into part <paramref name="to"/> of the build, through the intact pipes laid to the station's Fluid in.</summary>
    private static float FromPipes(Construct c, int t, Fluid fluid, Construct b, int to, float litres)
    {
        var moved = 0f;
        for (var k = 0; k < c.RouteCount && moved < litres; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type != RouteType.Pipe || r.To != t || r.Source >= c.PartCount || r.FirstCut <= r.Count || !r.FedEnd
                || !ConstructFluid.Holds(c, r.Source, out var f, out _) || f != fluid)
                continue;
            moved += ConstructFluid.Move(c, r.Source, b, to, litres - moved);
        }
        return moved;
    }

    /// <summary>
    /// The fill stage, a look every half second after the last link (survival), each kind to the station's fill setting
    /// (<see cref="CfgKey.FillFuel"/>, <see cref="CfgKey.FillAmmo"/>, <see cref="CfgKey.FillCharge"/>): the build's fuel pool and
    /// Fluid Tanks (to that share of their blueprint's fill) from tanks piped to the station's Fluid in, and gases from bulk
    /// hydrogen, helium and oxidiser in its linked bins; its ammo boxes, Ammo Containers and fitted magazines from bulk ammo in its
    /// linked bins; its batteries from the station construct's own charge. True while the stage goes on: something moved, or nothing
    /// did and the station waits (<see cref="CfgKey.FillWait"/>). What is still short is named in the note.
    /// </summary>
    private static bool Fill(ConstructWorld world, Construct c, int t, StationJob j, Construct b)
    {
        var bp = j.Bp!;
        var fuelShare = Share(c, t, CfgKey.FillFuel);
        var ammoShare = Share(c, t, CfgKey.FillAmmo);
        var chargeShare = Share(c, t, CfgKey.FillCharge);
        var budget = FillLitresALook;
        var gasUnits = GasUnitsALook;
        float fluidWant = 0, fluidHave = 0;
        var moved = false;
        var fuelTank = -1;
        var ammo = RecyclingTable.Current.IdOf(ConstructAmmo.Material);
        var units = AmmoUnitsALook;
        int roundsWant = 0, roundsHave = 0;
        for (var k = 0; k < bp.Steps.Count && k < j.PartOf.Length; k++)
        {
            var i = j.PartOf[k];
            if (i < 0 || i >= b.PartCount || !b.Parts[i].Working)
                continue;
            var d = b.DefOf(i);
            if (d.Role == PartRole.FuelTank)
                fuelTank = fuelTank < 0 ? i : fuelTank;
            else if (d.Role == PartRole.AmmoBox && d.BoxRounds > 0)
            {
                var want = (int)(d.BoxRounds * ammoShare);
                var need = want - b.Parts[i].Rounds;
                if (need > 0 && units > 0)
                {
                    var got = TakeLinked(world, c, t, ammo, Math.Min(units, (need + ConstructAmmo.RoundsPerUnit - 1) / ConstructAmmo.RoundsPerUnit));
                    if (got > 0)
                    {
                        units -= got;
                        b.Parts[i].Rounds = (ushort)Math.Min(want, b.Parts[i].Rounds + got * ConstructAmmo.RoundsPerUnit);
                        world.NoteAmmo(b, i);
                        moved = true;
                    }
                }
                roundsWant += want;
                roundsHave += Math.Min(want, (int)b.Parts[i].Rounds);
            }
            else if (ConstructAmmo.IsMagazine(d))
            {
                // A fitted magazine: its rounds and type in one ushort (ConstructAmmo.Pack); a station's comes empty, of the first type.
                var want = (int)(ConstructAmmo.Capacity(d) * ammoShare);
                var pack = b.Parts[i].Rounds;
                var have = ConstructAmmo.PackRounds(pack);
                if (want - have > 0 && units > 0)
                {
                    var got = TakeLinked(world, c, t, ammo, Math.Min(units, (want - have + ConstructAmmo.RoundsPerUnit - 1) / ConstructAmmo.RoundsPerUnit));
                    if (got > 0)
                    {
                        units -= got;
                        have = Math.Min(want, have + got * ConstructAmmo.RoundsPerUnit);
                        b.Parts[i].Rounds = ConstructAmmo.Pack(have, ConstructAmmo.PackType(pack));
                        world.NoteAmmo(b, i);
                        moved = true;
                    }
                }
                roundsWant += want;
                roundsHave += Math.Min(want, have);
            }
            else if (d.AmmoStore && world.StoreOf(b, i) is { } st)
            {
                // An Ammo Container: bulk ammo by the unit, as its bins hold it (nothing else in it: a store holds one material).
                var want = (int)(ConstructStorage.Capacity(b, i) * ammoShare);
                var mine = st.KindN == 0 && (st.Amount == 0 || st.Material == ammo);
                var have = mine ? st.Amount : want;
                if (mine && want - have > 0 && units > 0)
                {
                    var got = TakeLinked(world, c, t, ammo, Math.Min(units, want - have));
                    if (got > 0)
                    {
                        units -= got;
                        have += got;
                        world.SetBulk(st, (byte)ammo, have);
                        moved = true;
                    }
                }
                roundsWant += want * ConstructAmmo.RoundsPerUnit;
                roundsHave += Math.Min(want, have) * ConstructAmmo.RoundsPerUnit;
            }
            else if (d.Role == PartRole.FluidTank && FluidWant(bp, k, b, i, d, out var fluid, out var want))
            {
                want *= fuelShare;
                var held = ConstructFluid.Held(b.Parts[i], d);
                var have = held == fluid ? ConstructFluid.Litres(b.Parts[i], d) : held == null ? 0 : want; // (another fluid got in: left as it is)
                if (want - have > 0.01f && budget > 0)
                {
                    var got = FromPipes(c, t, fluid, b, i, MathF.Min(want - have, budget));
                    budget -= got;
                    have += got;
                    moved |= got > 0;
                }
                if (want - have > 0.01f && gasUnits > 0 && GasMaterial(fluid) is var gas and > 0)
                {
                    // A gas from the linked bins, as a Gas Compressor makes it: a unit is GasLitres (a part-used last unit's spare is lost).
                    var per = GasLitres;
                    var got = TakeLinked(world, c, t, gas, Math.Min(gasUnits, (int)MathF.Ceiling((want - have) / per)));
                    if (got > 0)
                    {
                        gasUnits -= got;
                        var litres = MathF.Min(got * per, MathF.Min(want - have, ConstructFluid.Room(b, i, fluid)));
                        ConstructFluid.Give(b, i, fluid, litres);
                        have += litres;
                        moved = true;
                    }
                }
                fluidWant += want;
                fluidHave += MathF.Min(have, want);
            }
        }
        var fuelWant = fuelTank < 0 ? 0 : b.FuelCapacity() * fuelShare;
        if (fuelTank >= 0 && fuelWant - b.Fuel > 0.01f && budget > 0)
        {
            var got = FromPipes(c, t, b.DefOf(fuelTank).TankFluid, b, fuelTank, MathF.Min(fuelWant - b.Fuel, budget));
            moved |= got > 0;
        }
        var fuelHave = MathF.Min(b.Fuel, fuelWant);
        // The batteries: from the station construct's own charge, at most ChargeKW plus what its power plants make now.
        var chargeWant = b.ChargeCapacity * chargeShare;
        if (chargeWant - b.Charge > 0.5f && c.PowerOn && c.Charge > 0)
        {
            var kj = MathF.Min((ChargeKW + MathF.Max(0, c.PlantOut)) * OrderEvery / World.TickRate, MathF.Min(c.Charge, chargeWant - b.Charge));
            if (kj > 0)
            {
                c.UseCharge(kj);
                b.Charge += kj;
                moved = true;
            }
        }
        var chargeHave = MathF.Min(b.Charge, chargeWant);
        var full = fuelHave >= fuelWant - 0.01f && fluidHave >= fluidWant - 0.01f && roundsHave >= roundsWant && chargeHave >= chargeWant - 0.5f;
        var waits = !full && !moved && Waits(c, t);
        var key = HashCode.Combine((int)fuelHave, (int)fuelWant, (int)fluidHave, (int)fluidWant, roundsHave, roundsWant, (int)chargeHave, moved ? 1 : waits ? 2 : 0);
        if (key != j.FillKey)
        {
            j.FillKey = key;
            j.Note = full ? "" : (moved ? "filling: " : waits ? "waits for its fill: " : "not filled: ")
                + FillText(fuelHave, fuelWant, fluidHave, fluidWant, roundsHave, roundsWant, chargeHave, chargeWant)
                + (moved ? "" : " - pipe tanks to its Fluid in, ammo and gas in a linked bin, power for batteries");
            world.StationChanged(c);
        }
        return !full && (moved || waits);
    }

    /// <summary>Are these only a station's fill settings (its window sets them with any tool in hand: ConstructEdits.Configure)?</summary>
    public static bool WindowSets(ConstructPartDef d, ReadOnlySpan<byte> slots)
    {
        if (!d.IsBuildStation || slots.Length == 0)
            return false;
        foreach (var s in slots)
            if (s >= d.Config.Length || d.Config[s].Id is < CfgKey.FillFuel or > CfgKey.FillWait)
                return false;
        return true;
    }

    /// <summary>A fill setting of the station, as a share (0 to 1; 1 when the part has no such setting).</summary>
    private static float Share(Construct c, int t, CfgKey k) => Math.Clamp(ConstructConfig.Get(c.Parts[t], c.DefOf(t), k, 100) / 100f, 0, 1);

    /// <summary>Does the station hold a build in its bay until it is filled (<see cref="CfgKey.FillWait"/>)?</summary>
    public static bool Waits(Construct c, int t) => ConstructConfig.Get(c.Parts[t], c.DefOf(t), CfgKey.FillWait, 0) >= 0.5f;

    /// <summary>The bulk material a gas comes from in a bin (0: none, a liquid).</summary>
    private static int GasMaterial(Fluid f) => f switch
    {
        Fluid.Hydrogen => RecyclingTable.Current.IdOf("hydrogen"),
        Fluid.Helium => RecyclingTable.Current.IdOf("helium"),
        Fluid.Oxidiser => RecyclingTable.Current.IdOf("oxidiser"),
        _ => 0,
    };

    /// <summary>Litres of gas a bulk unit makes: a Gas Compressor's (50 l), read once.</summary>
    private static float GasLitres => _gasLitres > 0 ? _gasLitres : _gasLitres = ConstructParts.Find("Gas Compressor") is { GasLitres: > 0 } g ? g.GasLitres : 50f;
    private static float _gasLitres;

    private static string FillText(float fuelHave, float fuelWant, float fluidHave, float fluidWant, int roundsHave, int roundsWant, float chargeHave, float chargeWant)
    {
        var s = "";
        if (fuelHave < fuelWant - 0.01f)
            s = $"fuel {fuelHave:0}/{fuelWant:0} L";
        if (fluidHave < fluidWant - 0.01f)
            s += (s.Length > 0 ? ", " : "") + $"tanks {fluidHave:0}/{fluidWant:0} L";
        if (roundsHave < roundsWant)
            s += (s.Length > 0 ? ", " : "") + $"ammo {roundsHave}/{roundsWant} rounds";
        if (chargeHave < chargeWant - 0.5f)
            s += (s.Length > 0 ? ", " : "") + $"charge {chargeHave:0}/{chargeWant:0} kJ";
        return s;
    }

    /// <summary>Up to <paramref name="units"/> of a bulk material from the station's linked bins; what was taken.</summary>
    private static int TakeLinked(ConstructWorld world, Construct c, int t, int material, int units)
    {
        var net = ConstructCrafting.NetOf(world, c, t);
        var got = 0;
        for (var s = 0; s < net.StoreN && got < units; s++)
            if (world.StoreOf(c, net.Stores[s]) is { Amount: > 0 } st && st.Material == material)
            {
                var take = Math.Min(units - got, st.Amount);
                world.TakeBulk(st, take);
                got += take;
            }
        return got;
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
        var metres = LinkEstimate(bp, b, j.PartOf, i);
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
            var left = Math.Max(1, (int)MathF.Ceiling(ConstructConveyor.Length(b, b.RouteCount - 1))); // (paid by the length it was laid at)
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
        if (run.Body is not { } built)
        {
            Fail(world, c, t, j, $"the blueprint doesn't build: {run.Why}");
            return;
        }
        var bp = j.Bp!;
        PartBounds(built, out var lo, out var size);
        j.Size = size;
        Bay(c, t, out var floor, out var rot, out var bay);
        j.Links = (ushort)Math.Min(None - 1, bp.Wires.Count + bp.Links.Count);
        MakeKinds(j);
        LiftCost(j, run, built); // (each step's place on the body, for the lifters: ConstructLifters.cs)
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
        if (StationProbe.On)
        {
            StationProbe.Timed(world);
            return;
        }
        StepOpen(world);
    }

    internal static void StepOpen(ConstructWorld world)
    {
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
                j.NoteKey = j.FillKey = 0;
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
            if (!world.StationTake(c, t, tray, d))
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
        if (!FillsFree(world) && Fill(world, c, t, j, b))
            return true; // (a look every half second while something moves)
        var left = j.Note; // (what the fill couldn't give: said after Done)
        Finish(world, c, t, j, b);
        if (j.State == StationState.Done && left.Length > 0)
            j.Note = left;
        return j.State == StationState.Done && j.Repeat > 0;
    }

    // ---- The wire (CBodyKind.Station, protocol 256) ------------------------------------------------------------------------

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
            var resume = j.PartOf.Length == bp.Steps.Count && j.State is StationState.Building or StationState.Costing; // (Costing with a fitted set: saved again before the costing after a load was done)
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

    /// <summary>
    /// A Building Station takes one of a kind out of its hold for fitting. A piece the packing has a place for goes as from a rack;
    /// one it holds but couldn't place (a save packs again by kind, and a cramped hold may not find the same places) goes by its kind,
    /// and the hold is packed afresh. False when the hold has none.
    /// </summary>
    public bool StationTake(Construct c, int part, CStore st, ConstructPartDef d)
    {
        if (ConstructCrafting.TakeOne(this, c, part, st, d))
            return true;
        if (ConstructCrafting.Holds(st, (ushort)d.Id) <= 0 || st.KindIndex(d.Id) < 0)
            return false;
        st.AddKind(d.Id, -1);
        st.SlabN = st.SlabSum = 0;
        if (st.ItemCount > 0)
            ConstructRackPack.Ensure(c.DefOf(part), st);
        LoadChanged = true;
        Changed(st);
        return true;
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
    private int[]? _partOf;

    public void Begin(ConstructBuild.Blueprint bp)
    {
        _steps?.Dispose();
        (_bp, Name, Ready, Why, _built) = (bp, bp.Name, false, "", null);
        Lines.Clear();
        CostMs = WorstMs = 0;
        _run = new ConstructBuild.BuildRun { StepsOnly = true };
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
        _built = _run!.Body;
        _partOf = _run.PartOf;
        if (_built == null)
            Why = _run.Why;
        else
            ConstructStation.PartBounds(_built, out _, out Size);
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
        for (var r = 0; r < _bp.Wires.Count + _bp.Links.Count && _partOf != null; r++)
        {
            // (about: the ends as the crow flies and a share for the run; the build pays each link by the length it is laid at)
            var len = MathF.Ceiling(ConstructStation.LinkEstimate(_bp, _built, _partOf, r));
            if (r < _bp.Wires.Count || _bp.Links[r - _bp.Wires.Count].Type is RouteType.Data or RouteType.Power)
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
        Lines.Add(("survival: filled after the links, to the Fill settings below: fuel and tanks through pipes to Fluid in (gases from linked bins too), ammo from bulk ammo in linked bins, batteries from the station's power", 1));
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
    public static bool On; // the window tool: the station step is timed and its allocations counted
    public static double TotalMs, WorstMs;
    public static long Bytes, WorstBytes;
    public static int Steps;

    internal static void Timed(ConstructWorld world)
    {
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        var t0 = Stopwatch.GetTimestamp();
        ConstructStation.StepOpen(world);
        var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        TotalMs += ms;
        WorstMs = Math.Max(WorstMs, ms);
        Bytes += bytes;
        WorstBytes = Math.Max(WorstBytes, bytes);
        Steps++;
    }

    /// <summary>The measure so far, then it starts again.</summary>
    public static string Cost()
    {
        var s = $"station step: {Steps} ticks, {TotalMs:0.00} ms in all, worst {WorstMs:0.000} ms; {Bytes} bytes in all, worst tick {WorstBytes} bytes";
        (TotalMs, WorstMs, Bytes, WorstBytes, Steps) = (0, 0, 0, 0, 0);
        return s;
    }

    /// <summary>Claims on stock held for a station's orders (every store).</summary>
    public static int Reserved(ConstructWorld world, Construct c, int t)
    {
        var n = 0;
        var owner = c.Parts[t].Store;
        for (var i = 0; i < world.Reserve.Count; i++)
            if (world.Reserve.Items[i].Owner == owner)
                n += world.Reserve.Items[i].Count;
        return n;
    }

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
        return (ConstructRecycler.Powered(world, c, t) ? "" : "NO POWER ") + (j == null ? "no job" : $"{j.State} \"{j.Name}\" parts {j.Fitted}/{j.Steps} links {j.LinksDone}/{j.Links} repeat {j.Repeat} body {j.Body} ({(b == null ? "none" : $"{b.PartCount} parts, {b.RouteCount} links, {(b.Frozen ? "frozen" : "free")}{(b.Anchored ? " anchored" : "")}{(b.Asleep ? " asleep" : " awake")}, wheels {b.WheelCount}, at {b.Pos.X:0.0},{b.Pos.Y:0.00},{b.Pos.Z:0.0}")}) reserved {Reserved(world, c, t)} hold {world.StoreOf(c, t)?.ItemCount ?? -1} orders {c.Parts[t].Craft?.Count ?? 0} note \"{j.Note}\"");
    }

    /// <summary>The build's fuel, Fluid Tanks and ammo boxes, and the station construct's fuel pool and the pipes into the station (the window tool's dump).</summary>
    public static string Tanks(ConstructWorld world, Construct c, int t)
    {
        var sb = new System.Text.StringBuilder($"station floor fuel {c.Fuel:0.0}/{c.FuelCapacity():0.0} L, pipes in:");
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Pipe && c.Routes[k].To == t)
                sb.Append($" from {c.DefOf(c.Routes[k].Source).Name}#{c.Routes[k].Source}{(c.Routes[k].FirstCut <= c.Routes[k].Count ? " (cut)" : "")}");
        var net = ConstructCrafting.NetOf(world, c, t);
        sb.Append("; linked bins:");
        for (var s = 0; s < net.StoreN; s++)
            if (world.StoreOf(c, net.Stores[s]) is { } st && st.Amount > 0)
            {
                var nth = 1; // (the window tool's NAME#n)
                for (var i = 0; i < net.Stores[s]; i++)
                    if (c.DefOf(i).Name == c.DefOf(net.Stores[s]).Name)
                        nth++;
                sb.Append($" {c.DefOf(net.Stores[s]).Name}#{nth} {st.Amount} {RecyclingTable.Current.NameOf(st.Material)}");
            }
        if (c.Parts[t].Station is not { } j || world.Get(j.Body) is not { } b)
            return sb.Append("; no build").ToString();
        sb.Append($"; build {b.Id} fuel {b.Fuel:0.0}/{b.FuelCapacity():0.0} L, charge {b.Charge:0}/{b.ChargeCapacity:0} kJ; station charge {c.Charge:0}/{c.ChargeCapacity:0} kJ");
        for (var i = 0; i < b.PartCount; i++)
        {
            var d = b.DefOf(i);
            if (d.Role == PartRole.AmmoBox)
                sb.Append($", {d.Name}#{i} {b.Parts[i].Rounds}/{d.BoxRounds}");
            else if (d.Role == PartRole.FluidTank)
                sb.Append($", {d.Name}#{i} {ConstructFluid.State(b.Parts[i], d)}");
            else if (ConstructAmmo.IsMagazine(d))
                sb.Append($", {d.Name}#{i} {ConstructAmmo.PackRounds(b.Parts[i].Rounds)}/{ConstructAmmo.Capacity(d)}");
            else if (d.AmmoStore && world.StoreOf(b, i) is { } ast)
                sb.Append($", {d.Name}#{i} {ast.Amount}/{ConstructStorage.Capacity(b, i)} units");
        }
        return sb.ToString();
    }
}

public static class StationCostProbe
{
    /// <summary>
    /// The window tool: the shipped blueprints with the most steps that a bay of <paramref name="bay"/> doesn't refuse from their
    /// parts alone, each costed as a station costs it (the steps only, a slice of <see cref="ConstructStation.CostBudgetMs"/> at a time):
    /// its steps, its size, whether it fits, the time in all and the longest slice.
    /// </summary>
    public static List<string> CostTop(Vector3 bay, int top)
    {
        var lines = new List<string>();
        var list = ConstructBuild.Blueprints.Where(b => ConstructStation.QuickRefusal(b, bay) == null).OrderByDescending(b => b.Steps.Count).Take(top);
        foreach (var bp in list)
        {
            // (as ConstructStation.Cost does it: the steps only, a slice at a time)
            var run = new ConstructBuild.BuildRun { StepsOnly = true };
            using var steps = ConstructBuild.BuildSteps(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, run).GetEnumerator();
            double total = 0, worst = 0;
            var slices = 0;
            for (var more = true; more;)
            {
                var t0 = Stopwatch.GetTimestamp();
                var until = t0 + (long)(ConstructStation.CostBudgetMs * Stopwatch.Frequency / 1000);
                while (more && Stopwatch.GetTimestamp() < until)
                    more = steps.MoveNext();
                var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                total += ms;
                worst = Math.Max(worst, ms);
                slices++;
            }
            var size = Vector3.Zero;
            if (run.Body != null)
                ConstructStation.PartBounds(run.Body, out _, out size);
            lines.Add(run.Body == null ? $"{bp.Name}: {bp.Steps.Count} steps: doesn't build: {run.Why}"
                : $"{bp.Name}: {bp.Steps.Count} steps, {size.X:0.0} x {size.Y:0.0} x {size.Z:0.0} m, {(ConstructStation.FitRefusal(size, bay) == null ? "fits" : "doesn't fit")}: {total:0.0} ms in {slices} slices, worst {worst:0.00} ms");
        }
        return lines;
    }
}

public static class StationPowerProbe
{
    /// <summary>The window tool: a body's mains as they stand (its batteries' inputs, its switches), for a floor that has no power.</summary>
    public static string Text(Construct c)
    {
        var sb = new System.Text.StringBuilder($"mains {(c.MainsOff ? "OFF" : "on")}, wired {c.MainsWired}, battery {c.HasBattery}, power on {c.PowerOn}, asleep {c.Asleep}, dirty {c.Dirty}:");
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Name is "Battery" or "Switch" or "Small Genset")
                sb.Append($" [{p.D.Name} #{i} in {p.In:0.0}/{p.InCount} out {p.Out:0.0} state {p.State} powered {p.Powered} broken {p.Broken}]");
        }
        return sb.ToString();
    }
}
