using System.Diagnostics;
using System.Text;

namespace CallOfSlop;

/// <summary>What a network offers its linked networks: kinds by stock and by making (copies, never references into another Game).</summary>
public sealed class LinkReport
{
    public const int MaxKinds = 128;
    public readonly ushort[] Kind = new ushort[MaxKinds];
    public readonly int[] Free = new int[MaxKinds];
    public readonly bool[] Makes = new bool[MaxKinds];
    public readonly float[] Rate = new float[MaxKinds]; // a maker's parts a second at its recipe's pace (the estimate's)
    public readonly int[] Batches = new int[MaxKinds]; // ...and how many its network's stock of the recipe's parts makes (one level)
    public int N;
    public int Version;
    public double Made; // when (LinkLedger.Now)
    public bool HasDock; // a Cargo Dock on the network: ships can trade here
    public ushort DockPart = ushort.MaxValue; // ...which part it is (a route's stop)
    public bool HasDispatcher; // a Logistics Dispatcher on its body (a consumer's Dispatcher fetches only from a supplier without one)
    public bool Powered;
    public long Hash; // (what the version follows)
    public const int MaxStations = 8;
    public readonly ushort[] Stations = new ushort[MaxStations]; // its Building Stations set to Sub-jobs Linked (LinkSub.Report)
    public readonly System.Numerics.Vector3[] StationBay = new System.Numerics.Vector3[MaxStations];
    public readonly bool[] StationIdle = new bool[MaxStations];
    public int StationN;
    public int MakesAge = int.MaxValue; // builds since the makes were last worked out (the catalogue, every MakesEvery builds)

    public int IndexOf(ushort kind)
    {
        for (var i = 0; i < N; i++)
            if (Kind[i] == kind)
                return i;
        return -1;
    }
}

/// <summary>A link end in the ledger: where it is, its pair, its last report. An end whose world is closed is asleep; its report stays.</summary>
public sealed class LinkEnd
{
    public uint Id, Pair;
    public LinkKind Kind;
    public string World = "", Name = "";
    public ushort Body, Part;
    public bool Working, Over, SeenPass;
    public double Posted, DownSince = -1; // last posted (LinkLedger.Now); since when it hasn't worked (-1: it works)
    public readonly LinkReport Report = new();
    public int ReportTick;
    public LinkOffer[] Offers = []; // the union of its partners' reports, made when one of them changes
    public long OffersKey = -1;
}

/// <summary>One remote line: a consumer's root asked a linked supplier for parts; it is made at the supplier's Cargo Dock and hauled.</summary>
public sealed class LinkLine
{
    public ushort Id;
    public uint ConsumerEnd, SupplierEnd;
    public string CWorld = "", SWorld = "";
    public ushort CBody, SBody;
    public ushort Owner, OwnerId; // the consumer's root: a terminal's or station's store handle and its order (Chain: OwnerId is a chained link's id)
    public bool Chain;
    public ushort Kind, Count, Ready, Loaded, Delivered, Lost, Taken;
    public ushort DockPart = ushort.MaxValue, DockOrder, SeenGot;
    public float Est; // estimated made while the supplier's world is closed, on the ledger (not yet loaded)
    public ushort EstLoaded, LedgerTaken, DeliverPending, LostPending; // what the ledger settled while a world was closed, owed when it opens
    public LinkLineState State;
    public string Note = "";
    public double PostedAt, ClosedAt, StalledFrom = -1;
    public string Ship = ""; // the ship it was last loaded on
    public ushort Use; // a linked sub-job: its use (LinkedLogisticsSub.cs)
    public uint SubStamp;
    public ushort SubStation;
    public bool Reclaim = true; // its ready parts are claimed again at its dock (claims aren't saved)
    public double ShipSeen = -1; // when a ship's manifest last showed it aboard (LinkDocks), and in which world
    public string ShipWorld = "";
    public ushort ShipBody; // ...and its body there
    public bool CancelAtDock; // its supplier's dock order is to be cancelled (a broken link: what isn't made yet isn't wanted)
    public int Outstanding => Math.Max(0, Count - Delivered - Lost - Taken);
    public int ToLoad => Math.Max(0, Count - Loaded);
}

public enum LinkLineState : byte { Posted, Open, Closed, Failed }

/// <summary>
/// The link ledger (docs/sandbox/design/linked-logistics.md section 5; the user's answers 5 and 6): the host's one table of link ends and
/// remote lines across every world it runs. Each world's step posts its ends and reads what concerns it, in memory on the main thread (a
/// planetoid runs as a Game in the host's own process). An end whose world hasn't posted for <see cref="AsleepAfter"/> seconds is asleep:
/// its report stays as last made; lines to it are estimated (its makers' rates against its stock of inputs, never past them) and a route
/// ship's stop there is settled here; when its world opens, what was settled lands in its stores (<see cref="Wake"/>). Saved in the main
/// world's LINK section. Bounded: 256 ends, 256 lines, one tick a second.
/// </summary>
public static class LinkLedger
{
    public const int MaxEnds = 256, MaxLines = 256, MaxLinesPerBody = 64;
    public const double AsleepAfter = 5, DownUnpair = 10, ClosedKeep = 60, StallAfter = 600, ShipGone = 15;
    public const int ReportEvery = 2, MakesEvery = 5; // posts between reports (2 s), reports between makes (10 s)

    public static readonly Dictionary<uint, LinkEnd> Ends = new();
    public static readonly List<LinkLine> Lines = new(MaxLines);
    private static readonly Dictionary<string, (double Seen, string Title, bool Asleep)> Worlds = new();
    private static ushort _nextLine = 1;
    private static double _ticked = -1;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static double Offset; // (a loaded ledger's time goes on from where it was saved)

    /// <summary>Seconds on the ledger's clock (real time: an asleep world's estimate runs on it).</summary>
    public static double Now => Clock.Elapsed.TotalSeconds + Offset;

    public static void Reset()
    {
        Ends.Clear();
        Lines.Clear();
        Worlds.Clear();
        _nextLine = 1;
        _ticked = -1;
    }

    public static bool Known(uint id) => Ends.ContainsKey(id);

    /// <summary>Does the ledger have anything for a world with no link parts (lines it posted or that wait for it)?</summary>
    public static bool HasWork(string world)
    {
        if (world.Length == 0)
            return false;
        foreach (var l in Lines)
            if (l.CWorld == world || l.SWorld == world)
                return true;
        return false;
    }

    public static bool Asleep(string world) => !Worlds.TryGetValue(world, out var w) || Now - w.Seen > AsleepAfter;

    public static string Title(string world) => Worlds.TryGetValue(world, out var w) && w.Title.Length > 0 ? w.Title : world;

    /// <summary>A world posted this second (its step).</summary>
    public static void Seen(string world, string title)
    {
        var was = Worlds.TryGetValue(world, out var w) ? w : (0, title, false); // (a world first seen is open: nothing to land)
        Worlds[world] = (Now, title, was.Asleep);
    }

    // ---- Ends ---------------------------------------------------------------------------------------------------------------

    public static void BeginWorld(string world)
    {
        foreach (var e in Ends.Values)
            if (e.World == world)
                e.SeenPass = false;
    }

    /// <summary>Is <paramref name="id"/> another live end's (not this part's): a copy of a coded part gets a fresh code.</summary>
    public static bool Twin(uint id, string world, ushort body, ushort part)
    {
        if (!Ends.TryGetValue(id, out var e))
            return false;
        if (e.World == world)
            return e.SeenPass && (e.Body != body || e.Part != part); // (seen this pass at another part: two parts with one code)
        return !Asleep(e.World) && Now - e.Posted < 3; // (live in another open world: a ship that left has stopped posting there)
    }

    /// <summary>One end, posted by its world's step: where it is, whether it works, its report every other post.</summary>
    public static void Post(ConstructWorld world, Construct c, int part, LinkPart lp, LinkKind kind, string key, bool working, bool over)
    {
        if (!Ends.TryGetValue(lp.Id, out var e))
        {
            if (Ends.Count >= MaxEnds)
                return;
            Ends[lp.Id] = e = new LinkEnd { Id = lp.Id };
            e.ReportTick = ReportEvery; // (its first report at once)
        }
        var moved = e.World != key || e.Body != c.Id;
        (e.World, e.Body, e.Part, e.Kind, e.Name, e.Over, e.SeenPass, e.Posted) = (key, c.Id, (ushort)part, kind, LinkedLogistics.NameOf(c, part), over, true, Now);
        if (e.Pair != lp.Pair)
            e.Pair = lp.Pair;
        if (working)
            e.DownSince = -1;
        else if (e.DownSince < 0)
            e.DownSince = Now;
        e.Working = working;
        if (moved)
            foreach (var l in Lines)
            {
                // (an end that moved with its body, a split or a reload: its lines follow it)
                if (l.ConsumerEnd == e.Id)
                    (l.CWorld, l.CBody) = (key, c.Id);
                if (l.SupplierEnd == e.Id)
                    (l.SWorld, l.SBody) = (key, c.Id);
            }
        if (++e.ReportTick >= ReportEvery)
        {
            e.ReportTick = 0;
            LinkReports.Build(world, c, e.Report);
        }
    }

    /// <summary>A world's ends not seen this pass are gone (their part removed or changed): their lines are dropped as a broken link's.</summary>
    public static void EndWorld(string world)
    {
        List<uint>? gone = null;
        foreach (var e in Ends.Values)
            if (e.World == world && !e.SeenPass)
                (gone ??= new List<uint>()).Add(e.Id);
        if (gone == null)
            return;
        foreach (var id in gone)
        {
            Broken(id, $"{Ends[id].Name}'s link part is gone");
            Ends.Remove(id);
        }
    }

    /// <summary>The ends <paramref name="e"/> is linked with now (valid pairs only): its pair, and every end paired with it.</summary>
    public static int Partners(LinkEnd e, Span<uint> into)
    {
        var n = 0;
        if (e.Pair != 0 && Ends.TryGetValue(e.Pair, out var p) && Valid(e, p) && n < into.Length)
            into[n++] = p.Id;
        foreach (var o in Ends.Values)
            if (o.Pair == e.Id && o.Id != e.Pair && Valid(e, o) && n < into.Length)
                into[n++] = o.Id;
        return n;
    }

    /// <summary>A pair that links: reach (a Relay at both ends to cross worlds), not one network, neither over its body's limit nor down past 10 s.</summary>
    public static bool Valid(LinkEnd a, LinkEnd b) =>
        a.Id != b.Id && !(a.World == b.World && a.Body == b.Body) && !a.Over && !b.Over
        && (a.World == b.World || a.Kind == LinkKind.Relay && b.Kind == LinkKind.Relay)
        && Up(a) && Up(b);

    private static bool Up(LinkEnd e) => Asleep(e.World) || e.DownSince < 0 || Now - e.DownSince < DownUnpair;

    /// <summary>Why a Receiver (or Relay) can't take this code, or null.</summary>
    public static string? PairRefusal(ConstructWorld world, Construct c, int part, LinkKind kind, uint id)
    {
        if (!Ends.TryGetValue(id, out var t) || t.Kind is not (LinkKind.Transmitter or LinkKind.Relay))
            return "no Transmitter or Relay has that code";
        var here = LinkedLogistics.WorldKey();
        if (t.World == here && t.Body == c.Id)
            return "that Transmitter is on this network";
        if (t.World != here && !(t.Kind == LinkKind.Relay && kind == LinkKind.Relay))
            return "a Transmitter and a Receiver link on one planetoid: a Logistics Relay at each end links across the star system";
        if (c.Parts[part].Link is { } lp && t.Id == lp.Id)
            return "that is its own code";
        return null;
    }

    /// <summary>A pair ended by a player (a code changed or cleared): the lines between those ends are dropped as a broken link's.</summary>
    public static void Unpaired(uint a, uint b, string why)
    {
        if (a == 0)
            return;
        foreach (var l in Lines)
            if (l.State is LinkLineState.Posted or LinkLineState.Open && (l.ConsumerEnd == a || l.SupplierEnd == a) && (b == 0 || l.ConsumerEnd == b || l.SupplierEnd == b))
                BreakLine(l, why);
    }

    /// <summary>An end went: its lines end as section 7 says (not started: cancelled; loaded goods keep their way; the rest drops).</summary>
    private static void Broken(uint id, string why)
    {
        foreach (var l in Lines)
            if (l.State is LinkLineState.Posted or LinkLineState.Open && (l.ConsumerEnd == id || l.SupplierEnd == id))
                BreakLine(l, why);
    }

    private static void BreakLine(LinkLine l, string why)
    {
        // What is loaded keeps its destination (the dock there takes it as stock); what isn't is no longer asked: the consumer asks again.
        var notLoaded = l.ToLoad;
        if (notLoaded > 0)
        {
            l.Count = l.Loaded;
            l.LostPending += (ushort)notLoaded; // (off the consumer's order: it asks again, locally or by another link)
        }
        l.Note = $"link lost: {why}";
        l.State = l.Loaded > l.Delivered + l.Lost + l.Taken ? LinkLineState.Open : LinkLineState.Closed;
        l.ClosedAt = Now;
        l.CancelAtDock = true;
        Console.WriteLine($"[linked] line {l.Id} ({ConstructParts.Get(l.Kind)?.Name}): {l.Note}");
    }

    public static string? CancelLine(ushort id, string why)
    {
        var l = Line(id);
        if (l == null)
            return "that line is gone";
        BreakLine(l, why);
        return null;
    }

    // ---- Lines ----------------------------------------------------------------------------------------------------------

    public static LinkLine? Line(ushort id)
    {
        foreach (var l in Lines)
            if (l.Id == id)
                return l;
        return null;
    }

    /// <summary>A new line, or null when the ledger or the consumer's body is full.</summary>
    public static LinkLine? NewLine(LinkEnd consumer, LinkEnd supplier, ushort kind, int count)
    {
        if (Lines.Count >= MaxLines)
            return null;
        var mine = 0;
        foreach (var l in Lines)
            if (l.CWorld == consumer.World && l.CBody == consumer.Body && l.State != LinkLineState.Closed)
                mine++;
        if (mine >= MaxLinesPerBody)
            return null;
        var id = _nextLine;
        while (Line(id) != null || id == 0)
            id++;
        _nextLine = (ushort)(id + 1);
        var line = new LinkLine
        {
            Id = id, ConsumerEnd = consumer.Id, SupplierEnd = supplier.Id, CWorld = consumer.World, CBody = consumer.Body, SWorld = supplier.World, SBody = supplier.Body,
            Kind = kind, Count = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount), PostedAt = Now,
            Note = Asleep(supplier.World) ? $"{supplier.Name} is asleep (nobody there): production estimated" : $"posted to {supplier.Name}",
        };
        Lines.Add(line);
        return line;
    }

    /// <summary>Open lines a consumer's root posted (its order, or a chained link): how many it still waits for.</summary>
    public static int Posted(string world, ushort body, ushort owner, ushort ownerId, bool chain)
    {
        var n = 0;
        foreach (var l in Lines)
            if (l.State is LinkLineState.Posted or LinkLineState.Open && l.CWorld == world && l.CBody == body && l.Owner == owner && l.OwnerId == ownerId && l.Chain == chain)
                n += l.Outstanding;
        return n;
    }

    /// <summary>Once a second (from whichever world steps first): closed lines leave, stalls are seen, asleep suppliers' output is estimated.</summary>
    public static void Tick()
    {
        var now = Now;
        if (now - _ticked < 0.95)
            return;
        var dt = _ticked < 0 ? 0 : Math.Min(now - _ticked, 3600);
        _ticked = now;
        foreach (var (k, w) in Worlds)
            if (!w.Asleep && now - w.Seen > AsleepAfter)
            {
                Worlds[k] = (w.Seen, w.Title, true);
                Console.WriteLine($"[linked] {w.Title} is asleep: its ends' reports stay, lines to it are estimated");
                break; // (the dictionary changed: the next one next second)
            }
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            var l = Lines[i];
            if (l.State is LinkLineState.Closed or LinkLineState.Failed && now - l.ClosedAt > ClosedKeep && l.LostPending == 0 && l.DeliverPending == 0 && l.EstLoaded == 0 && l.LedgerTaken == 0 && !l.CancelAtDock)
            {
                Lines.RemoveAt(i);
                continue;
            }
            if (l.State is LinkLineState.Open or LinkLineState.Posted && Asleep(l.SWorld) && l.Use == 0)
                Estimate(l, dt);
            var aboard = l.Loaded - l.Delivered - l.Lost - l.Taken;
            if (l.Use == 0 && aboard > 0 && l.ShipSeen >= 0 && !Asleep(l.ShipWorld) && now - l.ShipSeen > ShipGone)
                LinkDocks.LostWith(l, aboard, l.Ship.Length > 0 ? l.Ship : "its ship"); // (its ship is in no open world any more: removed, not travelled)
        }
    }

    /// <summary>
    /// The user's answer 6: a closed world's factory makes what it was asked, estimated: free stock at once, then its maker's rate, never
    /// past what its stock of inputs makes (one level) nor the line's count. Credited to the line; the stores get it when the world opens.
    /// </summary>
    private static void Estimate(LinkLine l, double dt)
    {
        if (!Ends.TryGetValue(l.SupplierEnd, out var s))
            return;
        var r = s.Report;
        var k = r.IndexOf(l.Kind);
        var room = l.ToLoad - l.Ready - (int)l.Est;
        if (k < 0 || room <= 0 || !r.Powered)
            return;
        var add = 0f;
        var free = Math.Max(0, r.Free[k] - l.Ready);
        if (free > 0 && l.Est < free)
            add = Math.Min(room, free - l.Est);
        else if (r.Makes[k] && r.Rate[k] > 0)
        {
            var most = Math.Max(0, r.Batches[k] - (l.Est - free)); // (its inputs, one level: no output past them)
            add = (float)Math.Min(Math.Min(room, most), r.Rate[k] * dt);
        }
        if (add > 0)
        {
            l.Est += add;
            l.Note = $"{s.Name} is asleep (nobody there): about {(int)(l.Ready + l.Est)} of {l.Count} ready (estimated)";
        }
    }

    /// <summary>
    /// A world that was asleep opens (its first step since): what the ledger settled while it was closed lands in its stores, a little at a
    /// time (32 units a second): goods delivered to its docks, goods a ship took from them, estimated output (its inputs consumed).
    /// </summary>
    public static void Wake(ConstructWorld world, string key)
    {
        if (!Worlds.TryGetValue(key, out var w))
            return;
        if (w.Asleep)
        {
            Worlds[key] = (w.Seen, w.Title, false);
            Console.WriteLine($"[linked] {w.Title} is open again: the ledger's settlements land");
        }
        var budget = 32;
        foreach (var l in Lines)
        {
            if (budget <= 0)
                return;
            if (l.CWorld == key && l.DeliverPending > 0)
                budget -= LinkDocks.LandDelivered(world, l, budget);
            if (l.SWorld == key && (l.LedgerTaken > 0 || l.EstLoaded > 0 || l.Est >= 1))
                budget -= LinkDocks.LandSupplied(world, l, budget);
        }
    }

    // ---- What a panel says --------------------------------------------------------------------------------------------------

    private static readonly StringBuilder Sb = new(256);

    /// <summary>An end's panel: its code or pair, each partner's state, its lines. How many partners answer; whether lines wait for a ship.</summary>
    public static string EndStatus(LinkPart lp, LinkKind kind, out int linked, out bool waiting)
    {
        linked = 0;
        waiting = false;
        var sb = Sb.Clear();
        if (kind is LinkKind.Transmitter or LinkKind.Relay)
            sb.Append("Code ").Append(LinkedLogistics.Code(lp.Id)).Append('\n');
        if (kind is LinkKind.Receiver or LinkKind.Relay)
            sb.Append(lp.Pair == 0 ? "Not paired: type a Transmitter's code" : $"Paired with {LinkedLogistics.Code(lp.Pair)}").Append('\n');
        if (!Ends.TryGetValue(lp.Id, out var e))
            return sb.Append("starting...").ToString();
        if (e.Over)
            sb.Append($"NOT LINKED: a network takes {LinkedLogistics.MaxEndsPerBody} link ends\n");
        if (!e.Working)
            sb.Append(e.DownSince >= 0 && Now - e.DownSince >= DownUnpair ? "NOT LINKED: no power or broken\n" : "no power: links drop in 10 s\n");
        Span<uint> ps = stackalloc uint[16];
        var n = Partners(e, ps);
        linked = n;
        if (n == 0 && lp.Pair != 0 && Ends.TryGetValue(lp.Pair, out var far) && !Valid(e, far))
            sb.Append("its pair doesn't answer: ").Append(Asleep(far.World) ? "asleep" : far.World != e.World && !(far.Kind == LinkKind.Relay && e.Kind == LinkKind.Relay) ? "out of reach (a Relay at each end crosses worlds)" : "unpowered or broken").Append('\n');
        else if (n == 0 && lp.Pair != 0)
            sb.Append("its pair isn't known (built and powered?)\n");
        for (var i = 0; i < n; i++)
        {
            var p = Ends[ps[i]];
            sb.Append("Linked: ").Append(p.Name).Append(" (").Append(Title(p.World)).Append(')');
            if (Asleep(p.World))
                sb.Append(" - asleep, nobody there: estimated");
            if (!p.Report.HasDock)
                sb.Append(" - no Cargo Dock there");
            sb.Append('\n');
        }
        int outN = 0, inN = 0, wait = 0;
        foreach (var l in Lines)
        {
            if (l.State is LinkLineState.Closed or LinkLineState.Failed)
                continue;
            if (l.ConsumerEnd == e.Id || l.CWorld == e.World && l.CBody == e.Body)
                outN++;
            if (l.SupplierEnd == e.Id || l.SWorld == e.World && l.SBody == e.Body)
            {
                inN++;
                if (l.Ready > 0 || l.Est >= 1)
                    wait++;
            }
        }
        waiting = wait > 0;
        if (outN + inN > 0)
            sb.Append(outN).Append(" asked of linked networks, ").Append(inN).Append(" asked of this one").Append(wait > 0 ? $"; {wait} wait for a ship" : "").Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>What an end's partners offer, for its body's terminals (made again only when a partner's report changed).</summary>
    public static LinkOffer[] OffersFor(uint id, uint pair)
    {
        if (!Ends.TryGetValue(id, out var e))
            return [];
        Span<uint> ps = stackalloc uint[16];
        var n = Partners(e, ps);
        long key = n;
        for (var i = 0; i < n; i++)
            key = key * 1000003 + Ends[ps[i]].Report.Version * 31 + (Asleep(Ends[ps[i]].World) ? 1 : 0) + ps[i];
        if (key == e.OffersKey)
            return e.Offers;
        e.OffersKey = key;
        var list = new List<LinkOffer>();
        for (var i = 0; i < n; i++)
        {
            var p = Ends[ps[i]];
            var r = p.Report;
            var asleep = Asleep(p.World);
            for (var k = 0; k < r.N && list.Count < LinkReport.MaxKinds; k++)
                if (r.Free[k] > 0 || r.Makes[k])
                    list.Add(new LinkOffer { Kind = r.Kind[k], Free = r.Free[k], Makes = r.Makes[k], Where = p.Name, Asleep = asleep });
        }
        return e.Offers = [.. list];
    }

    // ---- The LINK section (the main world's save) ---------------------------------------------------------------------------

    public static byte[] SaveBytes()
    {
        var w = new SaveWriter(4096);
        w.U8(1).F64(Now);
        w.I32(Ends.Count);
        foreach (var e in Ends.Values)
        {
            w.U32(e.Id).U32(e.Pair).U8((byte)e.Kind).Str(e.World).Str(e.Name).U16(e.Body).U16(e.Part).U8((byte)((e.Working ? 1 : 0) | (e.Over ? 2 : 0)));
            var r = e.Report;
            w.U8((byte)((r.HasDock ? 1 : 0) | (r.Powered ? 2 : 0) | (r.HasDispatcher ? 4 : 0))).U16(r.DockPart).U16((ushort)r.N);
            for (var k = 0; k < r.N; k++)
                w.Str(ConstructParts.Get(r.Kind[k])?.Name ?? "").I32(r.Free[k]).U8((byte)(r.Makes[k] ? 1 : 0)).F32(r.Rate[k]).I32(r.Batches[k]);
        }
        w.I32(Worlds.Count);
        foreach (var (k, v) in Worlds)
            w.Str(k).Str(v.Title).F64(v.Seen);
        w.I32(Lines.Count);
        foreach (var l in Lines)
        {
            w.U16(l.Id).U32(l.ConsumerEnd).U32(l.SupplierEnd).Str(l.CWorld).Str(l.SWorld).U16(l.CBody).U16(l.SBody).U16(l.Owner).U16(l.OwnerId).U8((byte)(l.Chain ? 1 : 0));
            w.Str(ConstructParts.Get(l.Kind)?.Name ?? "").U16(l.Count).U16(l.Ready).U16(l.Loaded).U16(l.Delivered).U16(l.Lost).U16(l.Taken);
            w.U16(l.DockPart).U16(l.DockOrder).U16(l.SeenGot).F32(l.Est).U16(l.EstLoaded).U16(l.LedgerTaken).U16(l.DeliverPending).U16(l.LostPending);
            w.U8((byte)l.State).Str(l.Note).F64(l.PostedAt).F64(l.ClosedAt).Str(l.Ship).U16(l.Use).U32(l.SubStamp).U16(l.SubStation).U8((byte)(l.CancelAtDock ? 1 : 0));
        }
        return w.Span.ToArray();
    }

    /// <summary>A save's LINK section: the ledger as it was (ends asleep until their worlds post again). A section that doesn't read is logged; the ledger starts empty.</summary>
    public static void Load(SaveReader r)
    {
        Reset();
        try
        {
            if (r.U8() != 1)
            {
                Console.WriteLine("[linked] a LINK section of another version skipped");
                return;
            }
            Offset = 0;
            Offset = r.F64() - Now;
            var n = r.I32();
            for (var i = 0; i < n && i < MaxEnds; i++)
            {
                var e = new LinkEnd { Id = r.U32(), Pair = r.U32(), Kind = (LinkKind)r.U8(), World = r.Str(), Name = r.Str(), Body = r.U16(), Part = r.U16() };
                var f = r.U8();
                (e.Working, e.Over, e.Posted) = ((f & 1) != 0, (f & 2) != 0, Now);
                var rf = r.U8();
                (e.Report.HasDock, e.Report.Powered, e.Report.HasDispatcher, e.Report.DockPart) = ((rf & 1) != 0, (rf & 2) != 0, (rf & 4) != 0, r.U16());
                int rn = r.U16();
                for (var k = 0; k < rn; k++)
                {
                    var name = r.Str();
                    var free = r.I32();
                    var makes = r.U8() != 0;
                    var rate = r.F32();
                    var batches = r.I32();
                    if (ConstructParts.Find(name) is { } d && e.Report.N < LinkReport.MaxKinds)
                    {
                        var at = e.Report.N++;
                        (e.Report.Kind[at], e.Report.Free[at], e.Report.Makes[at], e.Report.Rate[at], e.Report.Batches[at]) = ((ushort)d.Id, free, makes, rate, batches);
                    }
                }
                e.Report.Version = 1;
                Ends[e.Id] = e;
            }
            var wn = r.I32();
            for (var i = 0; i < wn; i++)
            {
                var k = r.Str();
                var title = r.Str();
                var seen = r.F64();
                Worlds[k] = (Math.Min(seen, Now - AsleepAfter - 1), title, true); // (every world asleep until it posts)
            }
            var ln = r.I32();
            for (var i = 0; i < ln && i < MaxLines; i++)
            {
                var l = new LinkLine { Id = r.U16(), ConsumerEnd = r.U32(), SupplierEnd = r.U32(), CWorld = r.Str(), SWorld = r.Str(), CBody = r.U16(), SBody = r.U16(), Owner = r.U16(), OwnerId = r.U16(), Chain = r.U8() != 0 };
                var name = r.Str();
                (l.Count, l.Ready, l.Loaded, l.Delivered, l.Lost, l.Taken) = (r.U16(), r.U16(), r.U16(), r.U16(), r.U16(), r.U16());
                (l.DockPart, l.DockOrder, l.SeenGot, l.Est, l.EstLoaded, l.LedgerTaken, l.DeliverPending, l.LostPending) = (r.U16(), r.U16(), r.U16(), r.F32(), r.U16(), r.U16(), r.U16(), r.U16());
                (l.State, l.Note, l.PostedAt, l.ClosedAt, l.Ship, l.Use, l.SubStamp, l.SubStation, l.CancelAtDock) = ((LinkLineState)r.U8(), r.Str(), r.F64(), r.F64(), r.Str(), r.U16(), r.U32(), r.U16(), r.U8() != 0);
                if (ConstructParts.Find(name) is { } d)
                    l.Kind = (ushort)d.Id;
                else if (l.Use == 0)
                {
                    Console.WriteLine($"[linked] line {l.Id} of \"{name}\" dropped: no such part in this build");
                    continue;
                }
                Lines.Add(l);
                _nextLine = (ushort)Math.Max(_nextLine, l.Id + 1);
            }
            Console.WriteLine($"[linked] ledger loaded: {Ends.Count} ends, {Lines.Count} lines");
        }
        catch (Exception ex) when (ex is WorldFileException or ArgumentException or IndexOutOfRangeException)
        {
            Console.WriteLine($"[linked] the LINK section skipped: {ex.Message}");
            Reset();
        }
    }
}
