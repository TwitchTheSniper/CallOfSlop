using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One row of a Smart Terminal's list: a part kind or a material, what a list, a panel or anything else shows and asks for
/// (<see cref="TerminalView"/>; docs/sandbox/CONSTRUCTS.md "The Smart Terminal").
/// </summary>
public readonly record struct TerminalRow(bool Material, ushort Id, string Name, int Count)
{
    public string Text => $"{Name} x{Count}";
}

/// <summary>
/// What a player's Smart Terminal has to say, with nothing about how it is shown: the terminal they stand at, its network's
/// stock and the rows made from it. Today's world-anchored list (Program.SandboxTerminal.cs) reads only this and sends only
/// <see cref="CActKind.TerminalPull"/>, so an in-world display panel can take its place. The stock is a
/// <see cref="BeaconView"/>: the same counts and the same bounded message.
/// </summary>
public sealed class TerminalView
{
    public ushort Handle; // the terminal's store (0: the player stands at none)
    public readonly BeaconView Stock = new() { IsTerminal = true };

    private readonly List<TerminalRow> _rows = new(64);
    private int _rowsVersion = -1;

    /// <summary>A client heard which terminal this is.</summary>
    public void Heard(ushort handle) => Handle = handle;

    // The row picked on this peer: what it is, as an index goes stale when the stock changes. Each player's own, never on the wire.
    private ushort _pickId = ushort.MaxValue;
    private bool _pickMaterial;

    /// <summary>The picked row's index in <see cref="Rows"/>: the first when what was picked is gone (0 with none).</summary>
    public int Picked
    {
        get
        {
            var rows = Rows;
            for (var k = 0; k < rows.Count; k++)
                if (rows[k].Id == _pickId && rows[k].Material == _pickMaterial)
                    return k;
            return 0;
        }
    }

    /// <summary>The wheel: moves the pick down the list, round from the end. The index picked.</summary>
    public int Step(int steps)
    {
        var rows = Rows;
        if (rows.Count == 0)
            return 0;
        var at = ((Picked + steps) % rows.Count + rows.Count) % rows.Count;
        _pickId = rows[at].Id;
        _pickMaterial = rows[at].Material;
        return at;
    }

    /// <summary>Materials first, then parts by name. Made again only when the stock changes.</summary>
    public IReadOnlyList<TerminalRow> Rows
    {
        get
        {
            if (_rowsVersion == Stock.Version)
                return _rows;
            _rowsVersion = Stock.Version;
            _rows.Clear();
            var table = RecyclingTable.Current;
            for (var m = 1; m < Stock.Materials.Length && m <= table.Materials.Length; m++)
                if (Stock.Materials[m] > 0)
                    _rows.Add(new TerminalRow(true, (ushort)m, table.NameOf((byte)m), Stock.Materials[m]));
            var first = _rows.Count;
            for (var id = 0; id < Stock.Counts.Length; id++)
                if (Stock.Counts[id] > 0 && ConstructParts.Get((ushort)id) is { } d)
                    _rows.Add(new TerminalRow(false, (ushort)id, d.Name, Stock.Counts[id]));
            for (var k = 0; k < Stock.MadeN; k++) // (made parts with the rest, one row per key: docs/sandbox/design/inventory-run-time-parts.md phase 3)
                if (Stock.MadeCount[k] > 0 && ConstructParts.Get(Stock.MadeDef[k]) is { } d)
                    _rows.Add(new TerminalRow(false, d.Id, PartRef.Listed(d), Stock.MadeCount[k]));
            _rows.Sort(first, _rows.Count - first, ByName);
            return _rows;
        }
    }

    private static readonly Comparer<TerminalRow> ByName = Comparer<TerminalRow>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));
}

/// <summary>The part's rules and its look line: which of its chute ports reads and which delivers.</summary>
public static class ConstructTerminals
{
    public const string NetworkPort = "Network", OutPort = "Chute out";
    public const float Reach = ConstructBuild.UseReach + 1.4f; // metres, eye to part: the list goes to a player standing this close
    public const float HandsReach = 8f; // metres: a pull to the hands lands on the tray once the player has walked further than this
    public const int MaxJobs = 32, MaxJobsEach = 8; // pulls under way: in the world, at one terminal
    public const float ShelveDrop = 0.8f; // metres: a part let go this far above a shelf goes on it

    /// <summary>Does the file name both ports, the delivery one out only?</summary>
    public static bool HasPorts(ConstructPartDef d)
    {
        bool net = false, chute = false;
        foreach (var p in d.NamedPorts)
        {
            net |= p.Type == RouteType.Chute && p.Name == NetworkPort && p.In && p.Out;
            chute |= p.Type == RouteType.Chute && p.Name == OutPort && !p.In && p.Out;
        }
        return net && chute;
    }

    private static bool IsOut(ConstructPartDef d, byte port)
    {
        foreach (var p in d.NamedPorts)
            if (p.Type == RouteType.Chute && p.Byte == port)
                return p.Name == OutPort;
        return false;
    }

    /// <summary>A chute leaving a terminal's Chute out: it carries what was pulled away, and what it feeds isn't on the network by it.</summary>
    public static bool Delivers(Construct c, int part, byte port) => c.DefOf(part) is { IsTerminal: true } d && IsOut(d, port);

    /// <summary>A chute leaving a terminal's Network port: it reads, and moves nothing.</summary>
    public static bool ReadOnly(Construct c, int part, byte port) => c.DefOf(part) is { IsTerminal: true } d && !IsOut(d, port);

    /// <summary>A terminal's state in the tooltip format. Player-visible: ASCII.</summary>
    public static string Say(string name, BeaconState state, int stores, int parts, string? waiting = null) => state switch
    {
        BeaconState.On => SandboxKeys.Line(name, SandboxKeys.Join($"{stores} store{(stores == 1 ? "" : "s")}", $"{parts} part{(parts == 1 ? "" : "s")}", waiting)),
        BeaconState.NoPower => SandboxKeys.Line(name, "off", "no power: link a charged battery"),
        BeaconState.HeldOff => SandboxKeys.Line(name, "off", "Run wire is off"),
        BeaconState.Wrecked => SandboxKeys.Line(name, "wrecked"),
        BeaconState.NoStores => SandboxKeys.Line(name, "off", "no stores: link a chute from its Network port to a store"),
        BeaconState.NotYours => SandboxKeys.Line(name, "someone else's"),
        _ => SandboxKeys.Line(name),
    };

    /// <summary>A terminal's state on any peer: from its store, which carries its network's numbers.</summary>
    public static BeaconState StateOn(CStore st, bool broken) =>
        broken ? BeaconState.Wrecked : st.Held ? BeaconState.HeldOff : st.Busy ? BeaconState.On : st.BeaconStores == 0 ? BeaconState.NoStores : BeaconState.NoPower;

    private const int ListRows = 4, ListFirst = 1, StatusLine = 5; // a card's six lines: the title, four rows of the list round the pick, what it is doing

    /// <summary>
    /// The screen's card (<see cref="PanelSources"/> "terminal"; docs/sandbox/CONSTRUCTS.md "The Smart Terminal"). A panel draws on each
    /// peer for that peer's player, so a player standing at this terminal sees their own list and their own pick (<see cref="ConstructWorld.LocalTerminal"/>),
    /// a window of four rows round it; anyone else sees what every peer knows from the store: its state and network's totals. Both see what it is pulling.
    /// </summary>
    public static void FillPanel(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        if (world.StoreOf(c, part) is not { } st)
            return;
        var state = StateOn(st, c.Parts[part].Broken);
        var view = world.LocalTerminal is { Handle: not 0 } v && v.Handle == st.Handle ? v : null;
        if (view is { Stock.State: BeaconState.NotYours })
            state = BeaconState.NotYours;
        card.State = state switch
        {
            BeaconState.On => st.PullWaiting > 0 ? PanelState.Working : PanelState.Ok,
            BeaconState.NotYours => PanelState.Refused,
            BeaconState.NoStores or BeaconState.HeldOff => PanelState.Wants,
            _ => PanelState.Fault,
        };
        if (state != BeaconState.On)
        {
            card.Add(0, c.DefOf(part).Name);
            card.Add(1, state switch
            {
                BeaconState.NoPower => "No power",
                BeaconState.HeldOff => "Run wire is off",
                BeaconState.NoStores => "No stores",
                BeaconState.NotYours => "Someone else's",
                _ => "Wrecked",
            });
            return;
        }
        var cols = PanelRender.Columns(panel.Size, panel.Rows);
        if (view is { Stock.On: true })
            FillList(view, card, cols);
        else
            FillTotals(c.DefOf(part), st, card);
        FillStatus(st, card);
    }

    private static void FillTotals(ConstructPartDef d, CStore st, PanelCard card)
    {
        card.Add(0, d.Name);
        card.Add(1, st.BeaconStores).Add(1, st.BeaconStores == 1 ? " store" : " stores");
        card.Add(2, st.BeaconParts).Add(2, st.BeaconParts == 1 ? " part" : " parts");
        card.Add(4, "Come close to pick");
    }

    private static void FillList(TerminalView view, PanelCard card, int cols)
    {
        var rows = view.Rows;
        if (rows.Count == 0)
        {
            card.Add(0, "Network").Add(2, "Empty");
            return;
        }
        var picked = view.Picked;
        card.Add(0, "Item ").Add(0, picked + 1).Add(0, '/').Add(0, rows.Count);
        var top = Math.Max(0, Math.Min(picked - 1, rows.Count - ListRows));
        for (var k = 0; k < ListRows && top + k < rows.Count; k++)
        {
            var row = rows[top + k];
            var line = ListFirst + k;
            if (top + k == picked)
                card.Mark = line;
            card.AddRow(line, top + k == picked ? '>' : ' ', row.Name, row.Count, cols);
        }
    }

    private static void FillStatus(CStore st, PanelCard card)
    {
        if (st.PullWaiting > 0)
        {
            AddItem(card, StatusLine, st.PullId, st.PullMaterial);
            card.Add(StatusLine, ' ').Add(StatusLine, (int)st.PullSeconds).Add(StatusLine, 's');
            if (st.PullWaiting > 1)
                card.Add(StatusLine, " +").Add(StatusLine, st.PullWaiting - 1);
            return;
        }
        if (st.Amount > 0)
        {
            card.Add(StatusLine, st.Amount).Add(StatusLine, ' ').Add(StatusLine, RecyclingTable.Current.NameOf(st.Material)).Add(StatusLine, " waiting");
            return;
        }
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] != ConstructParts.None)
            {
                card.Add(StatusLine, "Tray: ");
                AddItem(card, StatusLine, st.Def[k], false);
                card.Add(StatusLine, " x").Add(StatusLine, (int)st.Count[k]);
                return;
            }
        card.Add(StatusLine, "Ready");
    }

    private static void AddItem(PanelCard card, int line, ushort id, bool material)
    {
        if (material)
            card.Add(line, RecyclingTable.Current.NameOf((byte)id));
        else if (ConstructParts.Get(id) is { } d)
            card.Add(line, d.RuntimeMade ? PartRef.Shown(d) : d.Name);
    }

    /// <summary>The look line on any peer: from its store, which carries its network's numbers.</summary>
    public static string Look(ConstructPartDef d, CStore st, bool broken)
    {
        var state = StateOn(st, broken);
        return Say(d.Name, state, st.BeaconStores, st.BeaconParts, st.Amount > 0 ? $"{st.Amount} {RecyclingTable.Current.NameOf(st.Material)} waiting for Chute out" : null);
    }

    /// <summary>A part's place in the world.</summary>
    public static Vector3 At(Construct c, int part)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        return t + Vector3.Transform(c.Parts[part].Pos, q);
    }

    /// <summary>Is there a free place on the tray for one of a part?</summary>
    public static bool TrayRoom(ConstructPartDef d, CStore st, ConstructPartDef item)
    {
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Def[k] == item.Id ? st.Count[k] < ConstructStorage.StackOf(d.Slots[k], item, st.Rot[k]) : st.Def[k] == ConstructParts.None && ConstructStorage.Fits(d.Slots[k], item, out _))
                return true;
        return false;
    }

    /// <summary>Does any tray slot take a part of this size at all?</summary>
    public static bool TrayFits(ConstructPartDef d, ConstructPartDef item)
    {
        foreach (var s in d.Slots)
            if (ConstructStorage.Fits(s, item, out _))
                return true;
        return false;
    }
}

/// <summary>
/// The Smart Terminals' side of the chute networks (ConstructBeacons.cs): a terminal is a node of the same graph, worked out
/// only on a change, and the counts go up only when a store changed. A player's list is made at the view rate, only for a
/// player standing at a terminal, and only when something changed; no store is looked at otherwise.
/// </summary>
public sealed partial class ConstructBeacons
{
    public const int MaxTerminals = 64; // more are ignored: they read as having no stores
    private const int MaxEdges = 2048; // chutes kept for working out hops; past this a store further off counts as one hop

    private struct Terminal
    {
        public ushort Handle, Body, Part;
        public byte Net, Owner;
        public BeaconState State;
    }

    private readonly Terminal[] _term = new Terminal[MaxTerminals];
    private int _terms, _edges;
    private readonly ushort[] _edgeA = new ushort[MaxEdges], _edgeB = new ushort[MaxEdges];
    private readonly ushort[] _hops = new ushort[Handles];

    /// <summary>Smart Terminals in the world.</summary>
    public int TerminalCount => _terms;

    private static bool Theirs(byte owner, byte player) => owner == player || owner == 255;

    /// <summary>Each terminal's Run, power and lamp, as a beacon's. True when a state changed.</summary>
    private bool RefreshTerminals(ConstructWorld w)
    {
        var changed = false;
        for (var b = 0; b < _terms; b++)
        {
            ref var n = ref _term[b];
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
        return changed;
    }

    private bool TerminalDist(ConstructWorld w, in Terminal n, Vector3 eye, out float dist)
    {
        dist = float.MaxValue;
        if (w.Get(n.Body) is not { } c || n.Part >= c.PartCount)
            return false;
        dist = Vector3.Distance(ConstructTerminals.At(c, n.Part), eye);
        return dist <= ConstructTerminals.Reach;
    }

    /// <summary>Server, a player's look (at the view rate): the nearest terminal in reach and its network's stock, made again only when something changed.</summary>
    public void UpdateTerminal(ConstructWorld w, byte player, Vector3 eye, TerminalView view)
    {
        Refresh(w);
        var v = view.Stock;
        var best = -1;
        var near = float.MaxValue;
        for (var b = 0; b < _terms; b++)
            if (TerminalDist(w, _term[b], eye, out var d) && d < near)
            {
                near = d;
                best = b;
            }
        if (best < 0)
        {
            LeaveTerminal(view);
            return;
        }
        ref var n = ref _term[best];
        var state = Theirs(n.Owner, player) ? n.State : BeaconState.NotYours;
        var mask = state == BeaconState.On && n.Net != NoNet ? 1UL << n.Net : 0;
        if (view.Handle == n.Handle && v.Epoch == Epoch && v.State == state && v.Mask == mask)
            return;
        var was = v.Mask;
        view.Handle = n.Handle;
        v.Mask = mask;
        v.Epoch = Epoch;
        v.State = state;
        Array.Clear(v.Counts);
        Array.Clear(v.Materials);
        v.MadeN = v.MadeOver = 0;
        v.Stores = v.Parts = v.Bulk = 0;
        if (mask != 0)
            for (var h = _head[n.Net]; h != 0; h = _next[h])
                if (Live(w, h, out var c, out var part))
                {
                    v.Stores++;
                    Kinds[_kind[h]].Tally(w, c, part, v);
                    v.Parts += Kinds[_kind[h]].Parts(w, c, part);
                }
        foreach (var m in v.Materials)
            v.Bulk += m;
        v.Unsent = true;
        if (mask == 0 && was != 0)
            v.Reset = true;
        v.Version++;
    }

    /// <summary>The player walked away, or is gone: nothing shows.</summary>
    public static void LeaveTerminal(TerminalView view)
    {
        if (view.Handle == 0)
            return;
        var v = view.Stock;
        view.Handle = 0;
        v.Clear();
        v.Reset = v.Unsent = true;
    }

    /// <summary>A terminal's network, if it serves this player now. Otherwise why not, as its state.</summary>
    public BeaconState TerminalNet(ConstructWorld w, ushort handle, byte player, out byte net)
    {
        Refresh(w);
        net = NoNet;
        for (var b = 0; b < _terms; b++)
        {
            ref var n = ref _term[b];
            if (n.Handle != handle)
                continue;
            if (!Theirs(n.Owner, player))
                return BeaconState.NotYours;
            net = n.Net;
            return net == NoNet && n.State == BeaconState.On ? BeaconState.NoStores : n.State;
        }
        return BeaconState.NoStores; // (past MaxTerminals)
    }

    /// <summary>
    /// Server, one pull: the store on the network nearest by chutes that holds this part (or material), and how many chutes
    /// away. Runs when a player asks, never per tick; the hops relax over the chutes the graph kept. Null handle: none has it.
    /// </summary>
    internal ushort NearestStore(ConstructWorld w, ConstructEdits edits, byte player, ushort terminal, byte net, ConstructPartDef? d, byte material, out int hops)
    {
        Array.Fill(_hops, ushort.MaxValue);
        _hops[terminal] = 0;
        for (var again = true; again;)
        {
            again = false;
            for (var e = 0; e < _edges; e++)
            {
                ushort a = _edgeA[e], b = _edgeB[e];
                if (_hops[a] != ushort.MaxValue && _hops[a] + 1 < _hops[b])
                {
                    _hops[b] = (ushort)(_hops[a] + 1);
                    again = true;
                }
                if (_hops[b] != ushort.MaxValue && _hops[b] + 1 < _hops[a])
                {
                    _hops[a] = (ushort)(_hops[b] + 1);
                    again = true;
                }
            }
        }
        hops = int.MaxValue;
        ushort found = 0;
        for (var h = _head[net]; h != 0; h = _next[h])
        {
            if (!Live(w, h, out var c, out _) || edits.Contested(c, player) != null)
                continue;
            var away = _hops[h] == ushort.MaxValue ? 1 : Math.Max(1, (int)_hops[h]);
            if (away < hops && StoreHas(w, h, d, material))
            {
                hops = away;
                found = h;
            }
        }
        return found;
    }

    /// <summary>Does this store of the network still hold the part (or material)?</summary>
    internal bool StoreHas(ConstructWorld w, ushort store, ConstructPartDef? d, byte material) =>
        Live(w, store, out var c, out var part) && (d != null ? Kinds[_kind[store]].Holds(w, c, part, d.Id) > 0 : w.StoreOf(c, part) is { Amount: > 0 } st && st.Material == material);

    /// <summary>One of a part out of a store of the network (false: it has none now).</summary>
    internal bool TakeFrom(ConstructWorld w, ushort store, ConstructPartDef d) => Live(w, store, out var c, out var part) && Kinds[_kind[store]].TakePart(w, c, part, d);

    /// <summary>Up to <paramref name="want"/> of a material out of a store of the network.</summary>
    internal int TakeBulkFrom(ConstructWorld w, ushort store, byte material, int want) => Live(w, store, out var c, out var part) ? Kinds[_kind[store]].TakeBulk(w, c, part, material, want) : 0;
}

public sealed partial class ConstructWorld
{
    /// <summary>This peer's player's terminal list, for the panels (a <see cref="Game"/> sets it): each world's own, so it isn't one more static.</summary>
    public TerminalView? LocalTerminal;

    /// <summary>Server: what a terminal is bringing, for its panel on every peer: sent only when it changes.</summary>
    internal void NotePull(CStore st, ushort id, bool material, byte waiting, byte seconds)
    {
        if (st.PullId == id && st.PullMaterial == material && st.PullWaiting == waiting && st.PullSeconds == seconds)
            return;
        st.PullId = id;
        st.PullMaterial = material;
        st.PullWaiting = waiting;
        st.PullSeconds = seconds;
        st.Version++;
        StoreDirty.Add(st.Handle);
    }

    /// <summary>Server: a material waits in a terminal for its Chute out.</summary>
    internal void TerminalBulk(CStore st, byte material, int amount)
    {
        st.Material = material;
        st.Amount += amount;
        Changed(st);
    }
}

/// <summary>
/// The server's half of the Smart Terminal and the shelves: a pull is a job that waits a chute move per hop, then takes the
/// item from the real store (so nothing is ever in flight to save or lose) and puts it on the tray or in the hands.
/// </summary>
public sealed partial class ConstructEdits
{
    private struct TerminalJob
    {
        public ushort Handle, Body, Part, Id, Store;
        public byte Player;
        public bool Hands, Material;
        public int Ticks;
    }

    private readonly TerminalJob[] _jobs = new TerminalJob[ConstructTerminals.MaxJobs];
    private int _jobN;

    /// <summary>Server: a player at a terminal asks for an item. <paramref name="flags"/>: 1 to the hands, 2 <paramref name="id"/> is a material. Null, or why not.</summary>
    public string? TerminalPull(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, ushort id, byte flags)
    {
        if (Storage(world, player, eye, body, part, out var c) is { } why)
            return why;
        var d = c!.DefOf(part);
        if (!d.IsTerminal)
            return "that isn't a terminal";
        var st = world.StoreOf(c, part)!;
        var state = world.Beacons.TerminalNet(world, st.Handle, player, out var net);
        if (state != BeaconState.On)
            return ConstructTerminals.Say(d.Name, state, 0, 0);
        bool hands = (flags & 1) != 0, material = (flags & 2) != 0;
        var item = material ? null : ConstructParts.Get(id);
        var mat = (byte)(material && id < SandboxInventory.MaxMaterials ? id : 0);
        if (material ? mat == 0 || mat > RecyclingTable.Current.Materials.Length : item == null)
            return "no such item";
        var name = item?.Name ?? RecyclingTable.Current.NameOf(mat);
        if (item is { PhysicsOnly: true } && !(item.RuntimeMade && hands)) // (a made piece off a Piece Rack is set down in front of you as a body: phase 3 of inventory-run-time-parts.md)
            return item.RuntimeMade ? $"the {PartRef.Shown(item)} is a made piece: pull it to your hands, it is set down in front of you" : PhysicsOnlyRefusal(item, "come through a chute");
        if (!hands && item != null && !ConstructTerminals.TrayFits(d, item))
            return $"the {name} is too big for the tray: pull it to your hands";
        if (!hands && material && !HasChuteOut(world, c, part))
            return $"{name} needs a chute on Chute out: pull it to your hands";
        if (material && !hands && st.Amount > 0 && st.Material != mat)
            return $"its Chute out still holds {RecyclingTable.Current.NameOf(st.Material)}";
        var mine = 0;
        for (var k = 0; k < _jobN; k++)
            mine += _jobs[k].Handle == st.Handle ? 1 : 0;
        if (mine >= ConstructTerminals.MaxJobsEach || _jobN >= ConstructTerminals.MaxJobs)
            return $"the {d.Name} is busy";
        var store = world.Beacons.NearestStore(world, this, player, st.Handle, net, item, mat, out var hops);
        if (store == 0)
            return $"no {name} left on the network";
        _jobs[_jobN++] = new TerminalJob
        {
            Handle = st.Handle,
            Body = body,
            Part = (ushort)part,
            Id = id,
            Store = store,
            Player = player,
            Hands = hands,
            Material = material,
            Ticks = (int)(hops * ConstructRoutes.ChuteSeconds * World.TickRate),
        };
        PublishPull(world, st.Handle);
        Notices.Add((player, $"{name} coming ({hops} chute{(hops == 1 ? "" : "s")} away)"));
        return null;
    }

    private static bool HasChuteOut(ConstructWorld world, Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref var r = ref c.Routes[k];
            if (r.Type == RouteType.Chute && r.Source == part && ConstructTerminals.Delivers(c, part, r.SourcePort))
                return true;
        }
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref var l = ref world.WorldLinks[k];
            if (l.Type == RouteType.Chute && l.BodyFrom == c.Id && l.From == part && ConstructTerminals.Delivers(c, part, l.FromPort))
                return true;
        }
        return false;
    }

    /// <summary>Server, every tick: each terminal's first pull waits out its chutes (one at a time: a chute's rate). Nothing runs with none under way.</summary>
    public void TerminalStep(ConstructWorld world)
    {
        for (var i = 0; i < _jobN;)
        {
            var first = true;
            for (var k = 0; k < i && first; k++)
                first = _jobs[k].Handle != _jobs[i].Handle;
            if (!first || --_jobs[i].Ticks > 0)
            {
                if (first && _jobs[i].Ticks % (int)World.TickRate == 0)
                    PublishPull(world, _jobs[i].Handle);
                i++;
                continue;
            }
            var job = _jobs[i];
            Array.Copy(_jobs, i + 1, _jobs, i, --_jobN - i);
            Arrive(world, job);
            PublishPull(world, job.Handle);
        }
    }

    /// <summary>What a terminal's panel says it is doing, as its pulls stand now: a change, or a second gone. Never saved: a pull isn't.</summary>
    private void PublishPull(ConstructWorld world, ushort handle)
    {
        if (handle == 0 || handle > ConstructWorld.MaxStores || world.Stores[handle] is not { Live: true } st)
            return;
        var first = -1;
        var waiting = 0;
        for (var k = 0; k < _jobN; k++)
            if (_jobs[k].Handle == handle)
            {
                first = first < 0 ? k : first;
                waiting++;
            }
        if (first < 0)
            world.NotePull(st, 0, false, 0, 0);
        else
            world.NotePull(st, _jobs[first].Id, _jobs[first].Material, (byte)waiting, (byte)Math.Min((_jobs[first].Ticks + (int)World.TickRate - 1) / (int)World.TickRate, 255));
    }

    private void Arrive(ConstructWorld world, in TerminalJob j)
    {
        if (world.Get(j.Body) is not { } c || j.Part >= c.PartCount || c.Parts[j.Part].Store != j.Handle || !c.DefOf(j.Part).IsTerminal || world.StoreOf(c, j.Part) is not { } st)
        {
            Notices.Add((j.Player, "the terminal is gone: nothing came"));
            return;
        }
        var d = c.DefOf(j.Part);
        var state = world.Beacons.TerminalNet(world, j.Handle, j.Player, out var net);
        if (state != BeaconState.On)
        {
            Notices.Add((j.Player, $"the {d.Name} stopped: nothing came"));
            return;
        }
        var near = default(Vector3?);
        var spot = ConstructTerminals.At(c, j.Part);
        if (j.Hands && AliveAt?.Invoke(j.Player) is { } feet && Vector3.Distance(feet, spot) <= ConstructTerminals.HandsReach)
            near = spot;
        var item = j.Material ? null : ConstructParts.Get(j.Id);
        var mat = (byte)(j.Material ? j.Id : 0);
        var name = item?.Name ?? RecyclingTable.Current.NameOf(mat);
        // The store it was seen in may have lost it since (a chute, another player): the next nearest serves.
        var store = j.Store;
        if (!world.Beacons.StoreHas(world, store, item, mat))
            store = world.Beacons.NearestStore(world, this, j.Player, j.Handle, net, item, mat, out _);
        if (store == 0)
        {
            Notices.Add((j.Player, $"no {name} left on the network"));
            return;
        }
        if (item == null && j.Hands && near == null)
        {
            Notices.Add((j.Player, $"you walked away: the {name} stayed where it was"));
            return;
        }
        if (item != null)
            ArrivePart(world, c, j, d, st, store, item, near);
        else
            ArriveBulk(world, c, j, st, store, mat, name, near != null);
    }

    private void ArrivePart(ConstructWorld world, Construct c, in TerminalJob j, ConstructPartDef d, CStore st, ushort store, ConstructPartDef item, Vector3? near)
    {
        if (item.PhysicsOnly)
            near ??= ConstructTerminals.At(c, j.Part); // (a made piece is never a tray item: set down by the terminal, as Give drops a body)
        if (near == null && !ConstructTerminals.TrayRoom(d, st, item))
        {
            Notices.Add((j.Player, $"the {d.Name}'s tray is full: the {item.Name} stayed where it was"));
            return;
        }
        if (!world.Beacons.TakeFrom(world, store, item))
            return;
        if (near is { } at)
            Give(world, j.Player, item, at);
        else
        {
            world.TrayAdd(st, d, item);
            c.Wake();
        }
        Done.Add($"player {j.Player}: pulled a {item.Name} through the {d.Name} (construct {c.Id})");
    }

    private void ArriveBulk(ConstructWorld world, Construct c, in TerminalJob j, CStore st, ushort store, byte mat, string name, bool hands)
    {
        if (hands)
        {
            if (Inventory == null)
                return;
            var inv = Inventory.Of(j.Player);
            var want = ConstructStorage.TakeLoad - inv.Materials[mat];
            if (want <= 0)
            {
                Notices.Add((j.Player, $"you already carry a load of {name} ({inv.Materials[mat]})"));
                return;
            }
            var got = world.Beacons.TakeBulkFrom(world, store, mat, want);
            if (got > 0)
                inv.AddMaterial(mat, got);
            return;
        }
        if (st.Amount > 0 && st.Material != mat)
        {
            Notices.Add((j.Player, $"its Chute out still holds {RecyclingTable.Current.NameOf(st.Material)}: the {name} stayed where it was"));
            return;
        }
        var room = ConstructStorage.TakeLoad - st.Amount;
        var n = room > 0 ? world.Beacons.TakeBulkFrom(world, store, mat, room) : 0;
        if (n > 0)
        {
            world.TerminalBulk(st, mat, n);
            c.Wake();
        }
    }

    /// <summary>
    /// A loose part let go over a shelf (the physics gun's release, a drop from the hands) goes into the slot nearest where it
    /// would land: a shelf is held in slots, so a stack never tips over on a moving construct. Runs on the release, not per
    /// tick. What a shelf won't take (physics-only, too big, the slots taken) stays a body and comes to rest on the board.
    /// </summary>
    private void Shelve(ConstructWorld world, Construct body, byte player)
    {
        if (!body.Loose || body.DefOf(0) is not { PhysicsOnly: false } d || world.StoreOf(body, 0) != null)
            return;
        var from = body.WorldCom;
        var down = -GravityFrames.UpAt(from);
        var left = ConstructTerminals.ShelveDrop;
        ConstructHit hit;
        while (true)
        {
            if (!world.Raycast(from, down, left, out hit, ignore: body, look: true))
                return;
            if (hit.RoutePlus1 == 0)
                break;
            from += down * (hit.Distance + 0.02f); // (a link in the way: on down past it)
            left -= hit.Distance + 0.02f;
        }
        if (world.StoreOf(hit.Body, hit.Part) is not { } st)
            return;
        var shelf = hit.Body;
        var cd = shelf.DefOf(hit.Part);
        if (Contested(shelf, player) != null)
            return;
        ref var cp = ref shelf.Parts[hit.Part];
        shelf.WorldFrame(cp.Seg, out var q, out var t);
        var best = -1;
        var bestD = float.MaxValue;
        for (var k = 0; k < cd.Slots.Length && k < st.SlotCount; k++)
        {
            if (!cd.Slots[k].Rack || ConstructStorage.Check(cd, st, k, d, out _) != SlotCheck.Ok)
                continue;
            ConstructStorage.SlotBox(cp, cd.Slots[k], out var sc, out _);
            var dist = Vector3.DistanceSquared(t + Vector3.Transform(sc, q), hit.Point);
            if (dist < bestD)
            {
                bestD = dist;
                best = k;
            }
        }
        if (best < 0 || world.SetIn(shelf, hit.Part, best, d) != null)
            return;
        world.Remove(body);
        Done.Add($"player {player}: {d.Name} set on the {cd.Name}");
    }
}
