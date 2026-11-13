using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The beacon table (Constructs/Beacons.json; docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"): how far each kind of beacon
/// broadcasts, by the id a part names in <c>storage.beacon</c>. A bad file gives the built-in table with <see cref="Problem"/>.
/// </summary>
public sealed class BeaconTable
{
    public const string Format = "call-of-slop-beacons/1";
    public const string Inventory = "inventory";
    public const float DefaultRange = 60, MinRange = 1, MaxRange = 1000;
    public const int MaxKinds = 16;

    private readonly Dictionary<string, float> _range = [];
    public string[] Ids = [];
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Beacons.json");

    private static BeaconTable? _current;

    /// <summary>The table in use (loaded once; the built-in one when the file is missing or refused).</summary>
    public static BeaconTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(BeaconTable? t) => _current = t;

    public bool Has(string id) => _range.ContainsKey(id);

    /// <summary>How far a beacon of this kind reaches (metres); an unknown id (a table swapped under a loaded part): the default.</summary>
    public float Range(string id) => _range.GetValueOrDefault(id, DefaultRange);

    /// <summary>Reads the file (a missing or bad one: the built-in table, with why).</summary>
    public static BeaconTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BuiltIn($"{Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static BeaconTable BuiltIn(string why)
    {
        var t = new BeaconTable { Problem = why, Ids = [Inventory] };
        t._range[Inventory] = DefaultRange;
        return t;
    }

    public static BeaconTable Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "beacons"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            if (!root.TryGetProperty("beacons", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxKinds)
                throw new FormatException($"\"beacons\" lists 1 to {MaxKinds} kinds");
            var t = new BeaconTable();
            foreach (var e in list.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object)
                    throw new FormatException("a beacon is an object (id, rangeMetres)");
                foreach (var p in e.EnumerateObject())
                    if (p.Name is not ("id" or "rangeMetres"))
                        throw new FormatException($"a beacon: unknown key \"{p.Name}\"");
                if (!e.TryGetProperty("id", out var idv) || idv.ValueKind != JsonValueKind.String || idv.GetString() is not { Length: > 0 and <= 24 } id || id.Any(c => c is < 'a' or > 'z'))
                    throw new FormatException("a beacon's \"id\" is 1 to 24 lower-case letters");
                if (!e.TryGetProperty("rangeMetres", out var rv) || rv.ValueKind != JsonValueKind.Number || !rv.TryGetSingle(out var range) || range < MinRange || range > MaxRange)
                    throw new FormatException($"beacon {id}: \"rangeMetres\" is {MinRange:0} to {MaxRange:0}");
                if (!t._range.TryAdd(id, range))
                    throw new FormatException($"beacon {id} is listed twice");
            }
            t.Ids = t._range.Keys.ToArray();
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return BuiltIn(ex.Message);
        }
    }
}

/// <summary>What a player's beacons are doing (<see cref="BeaconView.State"/>; on the wire).</summary>
public enum BeaconState : byte
{
    None, // no beacon of theirs anywhere near
    On, // in range of a working beacon: the network is theirs to use
    OutOfRange, // near one, not near enough
    NoPower,
    HeldOff, // its Run wire is off
    Wrecked,
    NoStores, // nothing on its chute network
    NotYours, // in range of someone else's
    COUNT,
}

/// <summary>
/// The small interface between the beacons and one kind of store: what the network needs of it, and nothing about how it
/// holds things. <see cref="ConstructBeacons.Kinds"/> lists the kinds; the first that <see cref="Serves"/> a storage part
/// speaks for it. A new kind of store (a custom-sized bin) adds one here. Everything goes through the world's own
/// <c>TakeOut</c>, <c>SetIn</c>, <c>Withdraw</c> and <c>Deposit</c> in the shipped kind, so a client hears the store change as ever.
/// </summary>
public interface INetworkStore
{
    /// <summary>Does this kind speak for storage parts of this type?</summary>
    bool Serves(ConstructPartDef d);

    /// <summary>How many parts it holds.</summary>
    int Parts(ConstructWorld world, Construct c, int part);

    /// <summary>Adds what it holds to a player's view.</summary>
    void Tally(ConstructWorld world, Construct c, int part, BeaconView into);

    /// <summary>How many of a part kind it holds.</summary>
    int Holds(ConstructWorld world, Construct c, int part, ushort id);

    /// <summary>One of a part kind out; false when it holds none.</summary>
    bool TakePart(ConstructWorld world, Construct c, int part, ConstructPartDef d);

    /// <summary>Up to <paramref name="amount"/> of a material out; how much came.</summary>
    int TakeBulk(ConstructWorld world, Construct c, int part, byte material, int amount);

    /// <summary>One part in: <paramref name="pile"/> onto a pile of its kind, else into a free slot it fits. False when it has no such room.</summary>
    bool PutPart(ConstructWorld world, Construct c, int part, ConstructPartDef d, bool pile);

    /// <summary>A material in: <paramref name="same"/> into a bin holding it, else into an empty one. How much went in.</summary>
    int PutBulk(ConstructWorld world, Construct c, int part, byte material, int amount, bool same);
}

/// <summary>The shipped kind: a <see cref="CStore"/>'s slots and bin, by the rules of physical storage.</summary>
public sealed class SlotStore : INetworkStore
{
    public static readonly SlotStore Instance = new();

    public bool Serves(ConstructPartDef d) => d.Role == PartRole.Storage && !d.IsBeacon && !d.IsTerminal && !d.IsGunBench; // (a Gun Workbench's slots are a gun being built; a terminal's tray is what it has already pulled)

    public int Parts(ConstructWorld world, Construct c, int part) => world.StoreOf(c, part)?.Items ?? 0;

    public void Tally(ConstructWorld world, Construct c, int part, BeaconView into)
    {
        if (world.StoreOf(c, part) is not { } st)
            return;
        for (var k = 0; k < st.SlotCount; k++)
            if (ConstructParts.Get(st.Def[k]) != null)
                into.Add(st.Def[k], st.Count[k]);
        if (st.Amount > 0 && st.Material != 0)
            into.AddMaterial(st.Material, st.Amount); // (a production machine's output, never its input: the input only goes through the machine)
    }

    public int Holds(ConstructWorld world, Construct c, int part, ushort id)
    {
        if (world.StoreOf(c, part) is not { } st)
            return 0;
        var n = 0;
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == id)
                n += st.Count[k];
        return n;
    }

    public bool TakePart(ConstructWorld world, Construct c, int part, ConstructPartDef d)
    {
        if (world.StoreOf(c, part) is not { } st)
            return false;
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == d.Id)
                return world.TakeOut(c, part, k) != null;
        return false;
    }

    public int TakeBulk(ConstructWorld world, Construct c, int part, byte material, int amount) =>
        world.StoreOf(c, part) is { } st && st.Material == material ? world.Withdraw(c, part, amount) : 0;

    public bool PutPart(ConstructWorld world, Construct c, int part, ConstructPartDef d, bool pile)
    {
        if (world.StoreOf(c, part) is not { } st)
            return false;
        var cd = c.DefOf(part);
        for (var k = 0; k < st.SlotCount; k++)
            if ((st.Def[k] == d.Id) == pile && ConstructStorage.Check(cd, st, k, d, out _) == SlotCheck.Ok)
                return world.SetIn(c, part, k, d) == null;
        return false;
    }

    public int PutBulk(ConstructWorld world, Construct c, int part, byte material, int amount, bool same)
    {
        if (world.StoreOf(c, part) is not { } st)
            return 0;
        var cd = c.DefOf(part);
        if (!cd.IsBin || cd.IsRecycler || cd.IsGatherer || cd.IsRefiner || cd.IsExtruder || cd.IsScoop || (same ? st.Amount == 0 || st.Material != material : st.Amount != 0))
            return 0; // (a machine's bin is its output: nothing is tipped into that)
        return world.Deposit(c, part, amount, material);
    }
}

/// <summary>A Storage Volume on a network: its parts by kind and its bulk (ConstructStoreVolume.cs). One box holds one or the other.</summary>
public sealed class VolumeStore : INetworkStore
{
    public static readonly VolumeStore Instance = new();

    public bool Serves(ConstructPartDef d) => d.IsVolumeStore;

    public int Parts(ConstructWorld world, Construct c, int part) => world.StoreOf(c, part)?.ItemCount ?? 0;

    public void Tally(ConstructWorld world, Construct c, int part, BeaconView into)
    {
        if (world.StoreOf(c, part) is not { } st)
            return;
        for (var k = 0; k < st.KindN; k++)
            into.Add(st.KindDef![k], st.KindCount![k]);
        if (st.Amount > 0 && st.Material != 0)
            into.AddMaterial(st.Material, st.Amount);
    }

    public int Holds(ConstructWorld world, Construct c, int part, ushort id)
    {
        if (world.StoreOf(c, part) is not { } st)
            return 0;
        var k = st.KindIndex(id);
        return k < 0 ? 0 : st.KindCount![k];
    }

    public bool TakePart(ConstructWorld world, Construct c, int part, ConstructPartDef d) => world.TakePart(c, part, d.Id) != null;

    public int TakeBulk(ConstructWorld world, Construct c, int part, byte material, int amount) =>
        world.StoreOf(c, part) is { } st && st.Material == material ? world.Withdraw(c, part, amount) : 0;

    public bool PutPart(ConstructWorld world, Construct c, int part, ConstructPartDef d, bool pile) =>
        world.StoreOf(c, part) is { } st && st.KindIndex(d.Id) >= 0 == pile && world.PutPart(c, part, d) == null;

    public int PutBulk(ConstructWorld world, Construct c, int part, byte material, int amount, bool same)
    {
        if (world.StoreOf(c, part) is not { } st || st.KindN > 0 || (same ? st.Amount == 0 || st.Material != material : st.Amount > 0))
            return 0;
        return world.Deposit(c, part, amount, material);
    }
}

/// <summary>
/// What one player's beacons show them (the server's copy, or a client's as heard): the merged counts of every network in
/// range, the state and the totals. Fixed arrays; made once per player who meets a beacon.
/// </summary>
public sealed class BeaconView
{
    public readonly ushort[] Counts = new ushort[ConstructParts.MaxTypes]; // by part id
    public readonly int[] Materials = new int[SandboxInventory.MaxMaterials]; // by material id
    public BeaconState State;
    public int Stores, Parts, Bulk; // totals
    public ushort Range, Distance; // metres: the widest beacon in range; how far the nearest is (out of range, in steps of Step)
    public int Version; // goes up on every change a window or the HUD should show

    // Server only.
    internal readonly ushort[] SentCounts = new ushort[ConstructParts.MaxTypes];
    internal readonly int[] SentMaterials = new int[SandboxInventory.MaxMaterials];
    internal BeaconState SentState;
    internal int SentStores, SentParts, SentBulk;
    internal ushort SentRange, SentDistance;
    internal bool Unsent, Reset; // something to send; the client's copy starts empty first
    internal int Cursor; // the next part id to look at (a list longer than a packet takes turns)
    internal ulong Mask;
    internal int Epoch = -1;
    internal bool IsTerminal; // a Smart Terminal's list, not a beacon's: its own message (ConstructTerminal.cs)

    // Made parts (run-time ids: docs/sandbox/design/inventory-run-time-parts.md phase 3): a short list beside the table, by this
    // machine's id, sent by key (PartRef). Past MaxMade kinds the rest are left out of the list (MadeOver says how many).
    public const int MaxMade = 64;
    public readonly ushort[] MadeDef = new ushort[MaxMade];
    public readonly ushort[] MadeCount = new ushort[MaxMade];
    public int MadeN, MadeOver;
    internal uint SentMade; // server: the made list's stamp as last sent whole (0: nothing sent)
    internal int MadeCursor; // server: the next made entry of a list split over messages (0: none under way)
    internal uint MadeSending; // server: the stamp of the list being split

    public BeaconView() => RuntimeParts.Track(this); // (its made ids keep their parts while it lists them)

    public bool On => State == BeaconState.On;

    public void Add(ushort id, int n)
    {
        if (id < Counts.Length)
        {
            Counts[id] = (ushort)Math.Min(Counts[id] + n, ushort.MaxValue);
            return;
        }
        if (!Extrusion.IsRuntimeId(id) || n <= 0)
            return;
        for (var k = 0; k < MadeN; k++)
            if (MadeDef[k] == id)
            {
                MadeCount[k] = (ushort)Math.Min(MadeCount[k] + n, ushort.MaxValue);
                return;
            }
        if (MadeN == MaxMade)
        {
            MadeOver++;
            return;
        }
        MadeDef[MadeN] = id;
        MadeCount[MadeN++] = (ushort)Math.Min(n, ushort.MaxValue);
    }

    /// <summary>How many of a part this view lists: the table for a catalogue part, the made list for a run-time one.</summary>
    public int CountOf(ushort id)
    {
        if (id < Counts.Length)
            return Counts[id];
        for (var k = 0; k < MadeN; k++)
            if (MadeDef[k] == id)
                return MadeCount[k];
        return 0;
    }

    /// <summary>The made list's stamp (ids, keys and counts, in order; never 0). Allocates nothing.</summary>
    internal uint MadeStamp()
    {
        var h = 2166136261u ^ (uint)MadeN;
        for (var k = 0; k < MadeN; k++)
        {
            h = (h ^ MadeDef[k]) * 16777619u;
            h = (h ^ (uint)(ConstructParts.Get(MadeDef[k])?.Name.GetHashCode() ?? 0)) * 16777619u; // (an id freed and made again for another key reads as a change)
            h = (h ^ MadeCount[k]) * 16777619u;
        }
        return h | 1;
    }

    public void AddMaterial(byte id, int n)
    {
        if (id < Materials.Length)
            Materials[id] = (int)Math.Min(Materials[id] + (long)n, int.MaxValue);
    }

    /// <summary>What is shown goes (the server's next message says so).</summary>
    public void Clear()
    {
        Array.Clear(Counts);
        Array.Clear(Materials);
        MadeN = MadeOver = 0;
        State = BeaconState.None;
        Stores = Parts = Bulk = 0;
        Range = Distance = 0;
        Mask = 0;
        Epoch = -1;
        Version++;
    }

    /// <summary>Server: the client's copy is empty now (a joiner, a new match): everything goes again.</summary>
    public void Forget()
    {
        Array.Clear(SentCounts);
        Array.Clear(SentMaterials);
        SentMade = 0;
        MadeCursor = 0;
        SentState = BeaconState.None;
        SentStores = SentParts = SentBulk = 0;
        SentRange = SentDistance = 0;
        Reset = true;
        Unsent = true;
        Cursor = 0;
    }

    private string _line = "";
    private int _lineVersion = -1;

    /// <summary>The HUD's line ("" with nothing to say), made only when it changes.</summary>
    public string Line
    {
        get
        {
            if (_lineVersion != Version)
            {
                _lineVersion = Version;
                _line = ConstructBeacons.Say(State, Stores, Parts, Bulk, Range, Distance);
            }
            return _line;
        }
    }
}

/// <summary>
/// Inventory Beacons (docs/sandbox/CONSTRUCTS.md "The Inventory Beacon"; the user's "as if they were using creative mode, but in
/// survival"). A beacon is a storage part with no room of its own, so it has a store handle like every node of a chute
/// network: <see cref="Graph"/> joins handles along every intact chute (a construct's links, junctions included, and world
/// links) and lists each beacon's network. That runs only when something changed (<see cref="Dirty"/>: a body's
/// <see cref="Construct.StoreNetKey"/>, a store freed, a world link), and the counts only when a store changed
/// (<see cref="Stock"/>), at most at <see cref="Every"/> ticks. Nothing allocates after the first beacon; nothing here runs
/// per tick beyond a counter. The server is the authority: every take and put walks the real stores.
/// </summary>
public sealed partial class ConstructBeacons
{
    public const int MaxBeacons = 64; // a network is a bit of a player's mask: this many networks at most
    public const int MaxNodes = 256; // beacons and build stations (a station is a beacon with an aid: BuildAids.cs); more are ignored, logged once
    private const byte NoNet = 255; // a node past the last network: it reaches nothing
    public const int Every = 6; // ticks between steps (10 Hz)
    public const int ViewEvery = 15; // ticks between a player's looks (4 Hz)
    public const float NearFactor = 2; // an out-of-range beacon is mentioned within this many ranges
    public const int DistanceStep = 5; // metres: the out-of-range line's steps (fewer messages as a player walks)

    /// <summary>The kinds of store. A new one goes first if it speaks for parts the shipped kind would also take.</summary>
    public static readonly INetworkStore[] Kinds = [VolumeStore.Instance, SlotStore.Instance];

    private struct Node
    {
        public ushort Handle, Body;
        public ushort Part;
        public byte Net;
        public float Range;
        public byte Owner;
        public BeaconState State;
        public BuildAid Aid; // a build station's aid (None: an Inventory Beacon)
    }

    private const int Handles = ConstructWorld.MaxStores + 1;
    private readonly ushort[] _root = new ushort[Handles];
    private readonly ushort[] _body = new ushort[Handles]; // by handle: its construct (0: not in use)
    private readonly ushort[] _part = new ushort[Handles];
    private readonly ushort[] _next = new ushort[Handles]; // the next store of the same network
    private readonly byte[] _kind = new byte[Handles];
    private readonly ushort[] _head = new ushort[MaxBeacons]; // by network: its first store
    private readonly int[] _stores = new int[MaxBeacons], _parts = new int[MaxBeacons];
    private readonly ushort[] _netRoot = new ushort[MaxBeacons];
    private readonly Node[] _nodes = new Node[MaxNodes];
    private int _count, _nets, _ticks, _stockSeen = -1;
    private bool _warned;

    /// <summary>A store, chute or world link changed: the networks are worked out again at the next step or action.</summary>
    public bool Dirty = true;

    /// <summary>Goes up on every change to what any store holds.</summary>
    public int Stock;

    /// <summary>Goes up whenever a network, its counts or a beacon's state changes: a player's view is made again only then.</summary>
    public int Epoch;

    /// <summary>Beacons in the world (the first <see cref="MaxBeacons"/>).</summary>
    public int Count => _count;

    /// <summary>How many times the networks were worked out, and the counts done, ever (the self-test checks they don't run per tick).</summary>
    public int GraphRuns, CountRuns;

    /// <summary>A hash of what a construct adds to the networks: its stores (index, handle, whether broken) and its intact chutes. Allocates nothing.</summary>
    public static uint KeyOf(Construct c)
    {
        var h = 2166136261u;
        var any = false;
        foreach (var i in c.Specials) // (a part with a store is special)
        {
            ref var p = ref c.Parts[i];
            if (p.Store == 0)
                continue;
            any = true;
            h = (h ^ (uint)(i | p.Store << 8 | (p.Broken ? 1 << 24 : 0))) * 16777619u;
        }
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Chute && r.FedEnd)
                h = (h ^ (uint)(r.Source | r.To << 8)) * 16777619u;
        }
        return any ? (h ^ c.Id) | 1u : 0; // (0: nothing here, as a new body's "seen")
    }

    private ushort Find(ushort h)
    {
        while (_root[h] != h)
        {
            _root[h] = _root[_root[h]];
            h = _root[h];
        }
        return h;
    }

    private void Join(ushort a, ushort b)
    {
        if (a == 0 || b == 0 || _body[a] == 0 || _body[b] == 0)
            return;
        if (_edges < MaxEdges)
        {
            _edgeA[_edges] = a; // (a pull's hops: ConstructTerminal.cs)
            _edgeB[_edges++] = b;
        }
        var ra = Find(a);
        var rb = Find(b);
        if (ra != rb)
            _root[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }

    /// <summary>Works out which stores each beacon reaches: every intact chute joins its two ends, whichever way it runs. A chute that is cut, at any segment before its end, joins nothing.</summary>
    private void Graph(ConstructWorld w)
    {
        Dirty = false;
        GraphRuns++;
        Array.Clear(_body);
        _count = _nets = _terms = _edges = 0;
        for (ushort h = 0; h < Handles; h++)
        {
            _root[h] = h;
            _next[h] = 0;
        }
        var bodies = w.Bodies;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var c = bodies[bi];
            if (c.StoreNetKey == 0 && !c.Dirty)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
            {
                var h = c.Parts[i].Store;
                if (h == 0 || h >= Handles || !w.Stores[h].Live)
                    continue;
                _body[h] = c.Id;
                _part[h] = (ushort)i;
                var d = c.DefOf(i);
                if (d.IsTerminal && _terms < MaxTerminals)
                    _term[_terms++] = new Terminal { Handle = h, Body = c.Id, Part = (ushort)i };
                if (!d.IsBeacon)
                    continue;
                if (_count >= MaxNodes)
                {
                    if (!_warned)
                        w.Events.Add($"more than {MaxNodes} beacons and stations: the rest do nothing");
                    _warned = true;
                    continue;
                }
                _nodes[_count++] = new Node { Handle = h, Body = c.Id, Part = (ushort)i, Range = BeaconTable.Current.Range(d.BeaconKind), State = BeaconState.NoPower, Aid = d.Aid };
            }
        }
        if (_count == 0 && _terms == 0)
            return; // (no beacons or terminals: nothing to reach)
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var c = bodies[bi];
            for (var k = 0; k < c.RouteCount; k++)
            {
                ref var r = ref c.Routes[k];
                if (r.Type == RouteType.Chute && r.FedEnd && r.Source < c.PartCount && r.To < c.PartCount && !ConstructTerminals.Delivers(c, r.Source, r.SourcePort))
                    Join(c.Parts[r.Source].Store, c.Parts[r.To].Store); // (a terminal's Chute out carries what it pulled away from the network: what it feeds isn't on it)
            }
        }
        for (var k = 0; k < w.WorldLinkCount; k++)
        {
            ref var l = ref w.WorldLinks[k];
            if (l.Type == RouteType.Chute && w.Get(l.BodyFrom) is { } cf && w.Get(l.BodyTo) is { } ct && l.From < cf.PartCount && l.To < ct.PartCount && !ConstructTerminals.Delivers(cf, l.From, l.FromPort))
                Join(cf.Parts[l.From].Store, ct.Parts[l.To].Store);
        }
        for (var k = 0; k < w.HoseCount; k++)
        {
            // A plugged cargo hose joins the stores whose chutes meet at its two ends.
            ref var h = ref w.Hoses[k];
            if (!h.Plugged || w.Get(h.BodyOut) is not { } rc || w.Get(h.BodyIn) is not { } ic || h.PartOut >= rc.PartCount || rc.DefOf(h.PartOut).HoseCarry != HoseCarry.Items)
                continue;
            for (var a = 0; a < rc.RouteCount; a++)
            {
                ref var r = ref rc.Routes[a];
                if (r.Type != RouteType.Chute || r.To != h.PartOut || !r.FedEnd || r.Source >= rc.PartCount)
                    continue;
                for (var b = 0; b < ic.RouteCount; b++)
                {
                    ref var o = ref ic.Routes[b];
                    if (o.Type == RouteType.Chute && o.Source == h.PartIn && o.FedEnd && o.To < ic.PartCount)
                        Join(rc.Parts[r.Source].Store, ic.Parts[o.To].Store);
                }
            }
        }
        for (var b = 0; b < _count; b++)
            _nodes[b].Net = NetOf(_nodes[b].Handle);
        for (var b = 0; b < _terms; b++)
            _term[b].Net = NetOf(_term[b].Handle);
        for (var h = (ushort)(Handles - 1); h > 0; h--)
        {
            if (_body[h] == 0 || w.Get(_body[h]) is not { } c)
                continue;
            var d = c.DefOf(_part[h]);
            var kind = KindOf(d);
            if (kind < 0)
                continue;
            var root = Find(h);
            for (var net = 0; net < _nets; net++)
                if (_netRoot[net] == root)
                {
                    _kind[h] = (byte)kind;
                    _next[h] = _head[net];
                    _head[net] = h;
                    break;
                }
        }
    }

    /// <summary>The network of a beacon's or terminal's store (two on one network share it: its stores count once), or <see cref="NoNet"/>.</summary>
    private byte NetOf(ushort handle)
    {
        var root = Find(handle);
        var net = 0;
        while (net < _nets && _netRoot[net] != root)
            net++;
        if (net == _nets)
        {
            if (_nets >= MaxBeacons)
                return NoNet;
            _netRoot[_nets++] = root;
            _head[net] = 0;
        }
        return (byte)net;
    }

    private static int KindOf(ConstructPartDef d)
    {
        for (var k = 0; k < Kinds.Length; k++)
            if (Kinds[k].Serves(d))
                return k;
        return -1;
    }

    /// <summary>A member of a network as it is now: its construct and part (a part may have been renumbered since <see cref="Graph"/>).</summary>
    private bool Live(ConstructWorld w, ushort h, out Construct c, out int part)
    {
        c = null!;
        part = _part[h];
        return w.Get(_body[h]) is { } b && (c = b) != null && part < b.PartCount && b.Parts[part].Store == h;
    }

    private void Recount(ConstructWorld w)
    {
        CountRuns++;
        for (var net = 0; net < _nets; net++)
        {
            int stores = 0, parts = 0;
            for (var h = _head[net]; h != 0; h = _next[h])
                if (Live(w, h, out var c, out var part))
                {
                    stores++;
                    parts += Kinds[_kind[h]].Parts(w, c, part);
                }
            _stores[net] = stores;
            _parts[net] = parts;
        }
    }

    private static BeaconState StateOf(ConstructWorld w, Construct c, int part, CStore st, int stores)
    {
        if (c.Parts[part].Broken)
            return BeaconState.Wrecked;
        if (st.Held)
            return BeaconState.HeldOff;
        if (!ConstructRecycler.Powered(w, c, part))
            return BeaconState.NoPower;
        return stores == 0 ? BeaconState.NoStores : BeaconState.On;
    }

    /// <summary>Server, every <see cref="Every"/> ticks: the networks if something changed, the counts if a store did, and each beacon's Run, power and lamp.</summary>
    public void Step(ConstructWorld w)
    {
        if (++_ticks < Every)
            return;
        _ticks = 0;
        Refresh(w);
    }

    /// <summary>Brings everything up to date (a step, or an action: a take is never decided on a stale network).</summary>
    public void Refresh(ConstructWorld w)
    {
        if (!Dirty && _count == 0 && _terms == 0)
            return;
        var graph = Dirty;
        if (graph)
            Graph(w);
        var changed = graph;
        if (graph || _stockSeen != Stock)
        {
            _stockSeen = Stock;
            Recount(w);
            changed = true;
        }
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            if (w.Get(n.Body) is not { } c || n.Part >= c.PartCount || c.Parts[n.Part].Store != n.Handle || w.StoreOf(c, n.Part) is not { } st)
            {
                changed |= n.State != BeaconState.Wrecked;
                n.State = BeaconState.Wrecked;
                continue;
            }
            n.Owner = c.Owner;
            ConstructGatherer.SetHeld(w, st, ConstructGatherer.HeldOff(c, n.Part));
            int stores = n.Net == NoNet ? 0 : _stores[n.Net], parts = n.Net == NoNet ? 0 : _parts[n.Net];
            var state = StateOf(w, c, n.Part, st, stores);
            changed |= state != n.State;
            n.State = state;
            ConstructGatherer.SetBusy(w, st, state == BeaconState.On);
            if (st.BeaconStores != stores || st.BeaconParts != parts)
            {
                st.BeaconStores = stores;
                st.BeaconParts = parts;
                st.Version++;
                w.StoreDirty.Add(st.Handle); // (the look line, on every client: not a change to what it holds)
            }
        }
        changed |= RefreshTerminals(w);
        if (changed)
            Epoch++;
    }

    private bool Near(ConstructWorld w, in Node n, Vector3 at, out float dist)
    {
        dist = float.MaxValue;
        if (w.Get(n.Body) is not { } c || n.Part >= c.PartCount)
            return false;
        c.WorldFrame(c.Parts[n.Part].Seg, out var q, out var t);
        dist = Vector3.Distance(t + Vector3.Transform(c.Parts[n.Part].Pos, q), at);
        return dist <= n.Range;
    }

    /// <summary>
    /// Server: what the build stations give a player at <paramref name="at"/>: for each aid the best state in reach, how far
    /// the nearest is (out of range) and its range, and a Mirror Table's plane. Fixed arrays; runs at the view rate or an action.
    /// </summary>
    public void AidStates(ConstructWorld w, byte player, Vector3 at, BuildAidView v)
    {
        Refresh(w);
        Span<float> nearest = stackalloc float[(int)BuildAid.COUNT];
        Span<int> ranks = stackalloc int[(int)BuildAid.COUNT];
        nearest.Fill(float.MaxValue);
        ranks.Clear();
        for (var a = 0; a < (int)BuildAid.COUNT; a++)
        {
            v.Next[a] = BeaconState.None;
            v.NextDistance[a] = v.NextRange[a] = 0;
        }
        var table = -1;
        var tableDist = float.MaxValue;
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            if (n.Aid == BuildAid.None)
                continue;
            var inRange = Near(w, n, at, out var d);
            if (!inRange && d > n.Range * NearFactor)
                continue;
            var state = !Theirs(n, player) ? inRange ? BeaconState.NotYours : BeaconState.None : !inRange ? BeaconState.OutOfRange : n.State;
            if (state == BeaconState.None)
                continue;
            var a = (int)n.Aid;
            var rank = Rank(state);
            if (rank > ranks[a] || rank == ranks[a] && d < nearest[a])
            {
                ranks[a] = rank;
                nearest[a] = d;
                v.Next[a] = state;
                v.NextRange[a] = (ushort)n.Range;
                v.NextDistance[a] = state == BeaconState.OutOfRange ? Bucket(d) : (ushort)0;
            }
            if (n.Aid == BuildAid.Mirror && state == BeaconState.On && d < tableDist)
            {
                tableDist = d;
                table = b;
            }
        }
        v.HasTable = false;
        if (table >= 0 && w.Get(_nodes[table].Body) is { } c && _nodes[table].Part < c.PartCount)
        {
            ConstructMirror.ReadTable(c, _nodes[table].Part, out var axis, out var offset);
            v.HasTable = true;
            v.TableAxis = axis;
            v.TableOffset = offset;
        }
    }

    /// <summary>Why a player at <paramref name="at"/> has no station of this aid: a line for the HUD, or null when they have one.</summary>
    public string? AidWhy(ConstructWorld w, byte player, Vector3 at, BuildAid aid, BuildAidView scratch)
    {
        AidStates(w, player, at, scratch);
        var a = (int)aid;
        return scratch.Next[a] == BeaconState.On ? null : BuildAids.Say(aid, scratch.Next[a], scratch.NextRange[a], scratch.NextDistance[a]);
    }

    /// <summary>The nearest working station of an aid a player reaches: where it is and how far it reaches. False: none.</summary>
    public bool AidStation(ConstructWorld w, byte player, Vector3 at, BuildAid aid, out Vector3 spot, out float range)
    {
        Refresh(w);
        spot = default;
        range = 0;
        var best = float.MaxValue;
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            if (n.Aid != aid || n.State != BeaconState.On || !Theirs(n, player) || !Near(w, n, at, out var d) || d >= best || w.Get(n.Body) is not { } c)
                continue;
            best = d;
            c.WorldFrame(c.Parts[n.Part].Seg, out var q, out var t);
            spot = t + Vector3.Transform(c.Parts[n.Part].Pos, q);
            range = n.Range;
        }
        return best < float.MaxValue;
    }

    /// <summary>Every working Mirror Table a player reaches takes this plane (a part setting: it saves with the construct). How many did.</summary>
    public int SetTables(ConstructWorld w, byte player, Vector3 at, int axis, float offset)
    {
        Refresh(w);
        var set = 0;
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            if (n.Aid != BuildAid.Mirror || n.State != BeaconState.On || !Theirs(n, player) || !Near(w, n, at, out _) || w.Get(n.Body) is not { } c)
                continue;
            if (ConstructMirror.WriteTable(c, n.Part, axis, offset))
                w.StructureDirty.Add(c.Id); // (the settings ride in CBody, as the configure tool's do)
            set++;
        }
        return set;
    }

    /// <summary>Whose beacon it is: a player's own, or nobody's (the map's, or left behind). A faction mate's counts as your own (decision 19, <see cref="Factions.Shares"/>).</summary>
    private static bool Theirs(in Node n, byte player) => Factions.Shares(n.Owner, player) || n.Owner == 255; // (a faction mate's beacon serves you too)

    /// <summary>The working networks a player at <paramref name="at"/> reaches, as a mask (0: none). Live: an action calls it.</summary>
    public ulong MaskFor(ConstructWorld w, byte player, Vector3 at)
    {
        Refresh(w);
        var mask = 0UL;
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            if (n.State == BeaconState.On && Theirs(n, player) && Near(w, n, at, out _))
                mask |= 1UL << n.Net;
        }
        return mask;
    }

    /// <summary>Why a player at <paramref name="at"/> has no network: the most useful thing to say, or None. Out of range only within <see cref="NearFactor"/> ranges.</summary>
    private BeaconState Best(ConstructWorld w, byte player, Vector3 at, out float dist, out float range)
    {
        var best = BeaconState.None;
        dist = float.MaxValue;
        range = 0;
        for (var b = 0; b < _count; b++)
        {
            ref var n = ref _nodes[b];
            var inRange = Near(w, n, at, out var d);
            if (!inRange && d > n.Range * NearFactor)
                continue;
            var state = !Theirs(n, player) ? inRange ? BeaconState.NotYours : BeaconState.None : !inRange ? BeaconState.OutOfRange : n.State;
            if (state == BeaconState.None)
                continue;
            if (Rank(state) > Rank(best) || Rank(state) == Rank(best) && d < dist)
            {
                best = state;
                dist = d;
                range = n.Range;
            }
        }
        return best;
    }

    private static int Rank(BeaconState s) => s switch
    {
        BeaconState.None => 0,
        BeaconState.OutOfRange => 1,
        BeaconState.NotYours => 2,
        BeaconState.On => 7,
        _ => 3,
    };

    /// <summary>Why this player has no network here ("" when there is no beacon to speak of).</summary>
    public string Why(ConstructWorld w, byte player, Vector3 at)
    {
        Refresh(w);
        var state = Best(w, player, at, out var dist, out var range);
        return Say(state, 0, 0, 0, (ushort)range, Bucket(dist));
    }

    private static ushort Bucket(float dist) => dist >= ushort.MaxValue ? (ushort)0 : (ushort)(MathF.Ceiling(dist / DistanceStep) * DistanceStep);

    /// <summary>The HUD's line for a state ("" for none). Player-visible: ASCII.</summary>
    public static string Say(BeaconState state, int stores, int parts, int bulk, int range, int distance) => state switch
    {
        BeaconState.On => $"Beacon: {stores} store{(stores == 1 ? "" : "s")}, {parts} part{(parts == 1 ? "" : "s")}{(bulk > 0 ? $", {bulk} material" : "")}, range {range} m",
        BeaconState.OutOfRange => $"Beacon: out of range ({distance} m away, range {range} m)",
        BeaconState.NoPower => "Beacon off: no power - link a charged battery",
        BeaconState.HeldOff => "Beacon off: Run wire is off",
        BeaconState.Wrecked => "Beacon off: wrecked",
        BeaconState.NoStores => "Beacon off: no stores on its chute network",
        BeaconState.NotYours => "Beacon: someone else's",
        _ => "",
    };

    /// <summary>The look line on a beacon, on any peer: from its store, which carries its network's numbers.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool broken)
    {
        var range = (int)BeaconTable.Current.Range(d.BeaconKind);
        var state = broken ? BeaconState.Wrecked : st.Held ? BeaconState.HeldOff : st.Busy ? BeaconState.On : st.BeaconStores == 0 ? BeaconState.NoStores : BeaconState.NoPower;
        if (d.Aid != BuildAid.None)
            return BuildAids.Look(d, state, st.BeaconStores, range);
        return Say(state, st.BeaconStores, st.BeaconParts, 0, range, 0);
    }

    /// <summary>
    /// Server, a player's look (at most <see cref="ViewEvery"/> ticks): the state, and the merged counts of every network they
    /// reach, made again only when something changed. Each store counts once however many beacons reach it.
    /// </summary>
    public void Update(ConstructWorld w, byte player, Vector3 at, BeaconView v)
    {
        var mask = MaskFor(w, player, at);
        float dist;
        float range = 0;
        BeaconState state;
        if (mask != 0)
        {
            state = BeaconState.On;
            dist = 0;
            for (var b = 0; b < _count; b++)
            {
                ref var n = ref _nodes[b];
                if (n.State == BeaconState.On && (mask >> n.Net & 1) != 0 && n.Range > range && Theirs(n, player) && Near(w, n, at, out _))
                    range = n.Range;
            }
        }
        else
            state = Best(w, player, at, out dist, out range);
        var bucket = state == BeaconState.OutOfRange ? Bucket(dist) : (ushort)0;
        if (mask == v.Mask && Epoch == v.Epoch && state == v.State && (ushort)range == v.Range && bucket == v.Distance)
            return;
        var was = v.Mask;
        v.Mask = mask;
        v.Epoch = Epoch;
        v.State = state;
        v.Range = (ushort)range;
        v.Distance = bucket;
        Array.Clear(v.Counts);
        Array.Clear(v.Materials);
        v.MadeN = v.MadeOver = 0;
        v.Stores = v.Parts = v.Bulk = 0;
        for (var net = 0; net < _nets; net++)
        {
            if ((mask >> net & 1) == 0)
                continue;
            for (var h = _head[net]; h != 0; h = _next[h])
                if (Live(w, h, out var c, out var part))
                {
                    v.Stores++;
                    Kinds[_kind[h]].Tally(w, c, part, v);
                    v.Parts += Kinds[_kind[h]].Parts(w, c, part);
                }
        }
        foreach (var m in v.Materials)
            v.Bulk += m;
        v.Unsent = true;
        if (mask == 0 && was != 0)
            v.Reset = true; // (left the network: no list of zeroes)
        v.Version++;
    }

    /// <summary>Does the network hold one of this kind, in a store this player may reach?</summary>
    public bool Has(ConstructWorld w, ConstructEdits edits, byte player, ulong mask, ConstructPartDef d)
    {
        for (var net = 0; net < _nets; net++)
        {
            if ((mask >> net & 1) == 0)
                continue;
            for (var h = _head[net]; h != 0; h = _next[h])
                if (Live(w, h, out var c, out var part) && Kinds[_kind[h]].Holds(w, c, part, d.Id) > 0 && edits.Contested(c, player) == null)
                    return true;
        }
        return false;
    }

    /// <summary>One of a kind out of the first store that has it. False when none is left: it was the last, and someone was first.</summary>
    public bool TakePart(ConstructWorld w, ConstructEdits edits, byte player, ulong mask, ConstructPartDef d)
    {
        for (var net = 0; net < _nets; net++)
        {
            if ((mask >> net & 1) == 0)
                continue;
            for (var h = _head[net]; h != 0; h = _next[h])
                if (Live(w, h, out var c, out var part) && edits.Contested(c, player) == null && Kinds[_kind[h]].TakePart(w, c, part, d))
                    return true;
        }
        return false;
    }

    /// <summary>Up to <paramref name="want"/> of a material out of the stores in turn; how much came.</summary>
    public int TakeBulk(ConstructWorld w, ConstructEdits edits, byte player, ulong mask, byte material, int want)
    {
        var got = 0;
        for (var net = 0; net < _nets && got < want; net++)
        {
            if ((mask >> net & 1) == 0)
                continue;
            for (var h = _head[net]; h != 0 && got < want; h = _next[h])
                if (Live(w, h, out var c, out var part) && edits.Contested(c, player) == null)
                    got += Kinds[_kind[h]].TakeBulk(w, c, part, material, want - got);
        }
        return got;
    }

    /// <summary>A part back into the network: a store already holding its kind, else any free slot it fits. The store's name, or null: no room.</summary>
    public string? PutPart(ConstructWorld w, ConstructEdits edits, byte player, ulong mask, ConstructPartDef d)
    {
        for (var pass = 0; pass < 2; pass++)
            for (var net = 0; net < _nets; net++)
            {
                if ((mask >> net & 1) == 0)
                    continue;
                for (var h = _head[net]; h != 0; h = _next[h])
                    if (Live(w, h, out var c, out var part) && edits.Contested(c, player) == null && Kinds[_kind[h]].PutPart(w, c, part, d, pass == 0))
                        return c.DefOf(part).Name;
            }
        return null;
    }

    /// <summary>A material into the network: bins holding it, then empty bins; how much went in.</summary>
    public int PutBulk(ConstructWorld w, ConstructEdits edits, byte player, ulong mask, byte material, int amount)
    {
        var put = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var net = 0; net < _nets && put < amount; net++)
            {
                if ((mask >> net & 1) == 0)
                    continue;
                for (var h = _head[net]; h != 0 && put < amount; h = _next[h])
                    if (Live(w, h, out var c, out var part) && edits.Contested(c, player) == null)
                        put += Kinds[_kind[h]].PutBulk(w, c, part, material, amount - put, pass == 0);
            }
        return put;
    }

    // ---- The wire (protocol 115) ------------------------------------------------------------------------------------

    public const int MaxEntries = 240; // kinds a message carries (4 bytes each); a longer change takes more messages, a tick each

    /// <summary>Has this view anything to send?</summary>
    public static bool Pending(BeaconView v) => v.Unsent || v.Reset;

    /// <summary>
    /// A player's view, as far as it changed since the last message, in one packet of bounded size: the state and totals,
    /// then (part id, count) for up to <see cref="MaxEntries"/> kinds that differ, then the materials that differ. What
    /// doesn't fit stays different and goes next time, from where this one stopped. Off a network it is the state alone.
    /// A Smart Terminal's stock goes the same way in its own message, with <paramref name="terminal"/>, its store's handle, first.
    /// </summary>
    public static void Write(PacketWriter w, BeaconView v, ushort terminal = 0)
    {
        var reset = v.Reset;
        if (v.Mask == 0 && reset)
        {
            Array.Clear(v.SentCounts);
            Array.Clear(v.SentMaterials);
        }
        w.Begin(Msg.CBody).U8((byte)(v.IsTerminal ? CBodyKind.Terminal : CBodyKind.Beacon));
        if (v.IsTerminal)
            w.U16(terminal); // (the terminal it shows: ConstructTerminal.cs)
        w.U8((byte)(reset ? 1 : 0)).U8((byte)v.State)
            .U16((ushort)Math.Min(v.Stores, ushort.MaxValue)).U16((ushort)Math.Min(v.Parts, ushort.MaxValue)).U32((uint)v.Bulk).U16(v.Range).U16(v.Distance);
        v.Reset = false;
        var at = w.Length;
        w.U16(0);
        var n = 0;
        var kinds = Math.Min(ConstructParts.All.Count, ConstructParts.MaxTypes);
        var k = 0;
        for (; k < kinds && n < MaxEntries; k++)
        {
            var id = (v.Cursor + k) % kinds;
            if (v.Counts[id] == v.SentCounts[id])
                continue;
            w.U16((ushort)id).U16(v.Counts[id]);
            v.SentCounts[id] = v.Counts[id];
            n++;
        }
        var whole = k >= kinds;
        v.Cursor = whole ? 0 : (v.Cursor + k) % kinds;
        w.PatchU16(at, (ushort)n);
        at = w.Length;
        w.U8(0);
        var mats = 0;
        for (var m = 0; m < v.Materials.Length; m++)
            if (v.Materials[m] != v.SentMaterials[m])
            {
                w.U8((byte)m).U32((uint)v.Materials[m]);
                v.SentMaterials[m] = v.Materials[m];
                mats++;
            }
        w.PatchU8(at, (byte)mats);
        var madeMore = WriteMade(w, v, reset);
        v.Unsent = !whole || madeMore; // (more to say next tick)
    }

    /// <summary>The bytes a beacon or terminal message fills before its made list goes on in the next (the writers' buffers hold 16384; a made entry is under 300).</summary>
    public const int MadeBudget = 12000;

    /// <summary>
    /// The made list (protocol 303): u8 0 when it is as last sent; else u8 1 (a whole list starts: the client empties its own) or 2 (the
    /// same list goes on), u8 n and n made parts (PartRef, u16 count). A list past <see cref="MadeBudget"/> goes on in the next message.
    /// True when some of it is still to go.
    /// </summary>
    private static bool WriteMade(PacketWriter w, BeaconView v, bool reset)
    {
        if (reset)
            v.SentMade = 0; // (the client empties its copy on a reset: the list goes again whole)
        var stamp = v.MadeStamp();
        if (v.MadeCursor > 0 && stamp != v.MadeSending)
            v.MadeCursor = 0; // (it changed half way: again from the start)
        if (v.MadeCursor == 0 && stamp == v.SentMade)
        {
            w.U8(0);
            return false;
        }
        w.U8((byte)(v.MadeCursor == 0 ? 1 : 2));
        var at = w.Length;
        w.U8(0);
        var n = 0;
        var k = v.MadeCursor;
        for (; k < v.MadeN && (n == 0 || w.Length < MadeBudget); k++, n++)
        {
            PartRef.Write(w, v.MadeDef[k]);
            w.U16(v.MadeCount[k]);
        }
        w.PatchU8(at, (byte)n);
        if (k >= v.MadeN)
        {
            v.SentMade = stamp;
            v.MadeCursor = 0;
            return false;
        }
        v.MadeCursor = k;
        v.MadeSending = stamp;
        return true;
    }

    private static void ReadMade(ref PacketReader r, BeaconView? v)
    {
        var flags = r.U8();
        if (flags == 0)
            return;
        if (flags > 2)
            throw new MalformedPacketException($"a beacon's made list flagged {flags}");
        int n = r.U8();
        if (n > BeaconView.MaxMade)
            throw new MalformedPacketException($"a beacon list of {n} made kinds");
        if (flags == 1 && v != null)
            v.MadeN = 0;
        for (var k = 0; k < n; k++)
        {
            var id = PartRef.Read(ref r);
            var count = r.U16();
            if (v != null && Extrusion.IsRuntimeId(id) && v.MadeN < BeaconView.MaxMade) // (a key this machine can't make reads as a catalogue stand-in: left out)
            {
                v.MadeDef[v.MadeN] = id;
                v.MadeCount[v.MadeN++] = count;
            }
        }
    }

    /// <summary>A client reads <see cref="Write"/>'s bytes (bad values refused, never trusted).</summary>
    public static void Read(ref PacketReader r, BeaconView? v)
    {
        var flags = r.U8();
        var state = r.U8();
        if (state >= (byte)BeaconState.COUNT)
            throw new MalformedPacketException($"a beacon state of {state}");
        var stores = r.U16();
        var parts = r.U16();
        var bulk = r.U32();
        var range = r.U16();
        var distance = r.U16();
        if ((flags & 1) != 0 && v != null)
        {
            Array.Clear(v.Counts);
            Array.Clear(v.Materials);
        }
        int n = r.U16();
        if (n > MaxEntries)
            throw new MalformedPacketException($"a beacon list of {n} kinds");
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var count = r.U16();
            if (id >= ConstructParts.MaxTypes || ConstructParts.Get(id) == null) // (catalogue ids only: a run-time part is never counted here yet)
                throw new MalformedPacketException($"a beacon count of part {id}");
            if (v != null)
                v.Counts[id] = count;
        }
        int mats = r.U8();
        for (var k = 0; k < mats; k++)
        {
            var id = r.U8();
            var amount = r.U32();
            if (id >= SandboxInventory.MaxMaterials || amount > int.MaxValue)
                throw new MalformedPacketException($"a beacon count of {amount} of material {id}");
            if (v != null)
                v.Materials[id] = (int)amount;
        }
        if ((flags & 1) != 0 && v != null)
            v.MadeN = 0;
        ReadMade(ref r, v);
        if (v == null)
            return;
        v.State = (BeaconState)state;
        v.Stores = stores;
        v.Parts = parts;
        v.Bulk = (int)Math.Min(bulk, int.MaxValue);
        v.Range = range;
        v.Distance = distance;
        v.Version++;
    }
}
