using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a player's fleet act (<see cref="CActKind.Fleet"/>) does (faction-controllers.md phase 6). New ops go at the end (wire bytes).</summary>
public enum FleetOp : byte
{
    New, // body: the picked ship (arg 1: every crewed ship of yours within FleetGatherRange of you), a new group, you its admiral
    Add, // slot, body
    Remove, // slot, body
    Disband, // slot
    Order, // slot, arg OrderKind, body (a target or what to guard; Form 0: on you), at (GoTo, Retreat; 0: home)
    Stop, // slot, arg the work (Load, Unload, Mine, Deliver; Route: pass through), at, value the dwell (s): a route's stop, the route then run
    Formation, // slot, arg FleetFormation
    Spacing, // slot, value m
    Doctrine, // slot, arg the doctrine's place in Crew/Fleet.json (its shape and spacing kept)
    Admiral, // slot, uid (0: you lead it)
    Flagship, // slot, body
    Task, // slot, arg GroupTask (civilian tasks: the warships screen it, its flagship keeps going)
}

/// <summary>
/// Players as admirals (docs/sandbox/design/faction-controllers.md phase 6, section 7; SURVIVAL.md's "fleet room"): a player's own hired crews' ships
/// as a group, the player (or a hand of theirs, job Admiral) its admiral, ordered from the command view through the server (owner or their faction's
/// leader only). Orders go over the fleet command network (CrewFleet.cs): aboard a ship with a radio part the player reaches 25 km, else their hand
/// set 1.5 km, and ships pass them on to each other; with a hand as admiral the player's order must reach the admiral's ship. Once a second the
/// player's place on the net is written into their groups, and each player hears the groups they may command (<see cref="Msg.FleetGroups"/>).
/// </summary>
public sealed partial class Game
{
    /// <summary>"Every crewed ship of yours near you" reaches this far (m).</summary>
    public const float FleetGatherRange = 3000;

    /// <summary>A player leads at most this many groups at once.</summary>
    public const int FleetGroupsEach = 16;

    // ---- Client (and host): what this player may command ------------------------------------------------------------------------------

    /// <summary>One group as its commander's peer knows it (Msg.FleetGroups). Pooled: a message refills them.</summary>
    public sealed class FleetGroupView
    {
        public int Slot;
        public ushort Number, AdmiralName = 0xFFFF, OrderBody;
        public byte Owner;
        public bool PlayerAdmiral, Heard, Retreating;
        public OrderKind Order;
        public Vector3 OrderAt;
        public GroupTask Task;
        public FleetFormation Formation;
        public float Spacing, Dwell;
        public byte Doctrine, OnNet, RouteAt;
        public readonly ushort[] Ships = new ushort[CrewGroup.MaxShips];
        public int Count;
        public readonly Vector3[] Route = new Vector3[CrewGroup.MaxRoute];
        public readonly OrderKind[] RouteWork = new OrderKind[CrewGroup.MaxRoute];
        public int RouteCount;

        public bool Has(ushort body) => Array.IndexOf(Ships, body, 0, Count) >= 0;
    }

    /// <summary>The groups this player may command, as the host last said (<see cref="Msg.FleetGroups"/>).</summary>
    public readonly List<FleetGroupView> FleetGroups = new(FleetGroupsEach + 1);
    private readonly List<FleetGroupView> _fleetPool = new(FleetGroupsEach + 1);

    /// <summary>The group of these that holds <paramref name="body"/>, or null.</summary>
    public FleetGroupView? FleetGroupOf(ushort body)
    {
        foreach (var v in FleetGroups)
            if (v.Has(body))
                return v;
        return null;
    }

    /// <summary>The doctrines' names, in Crew/Fleet.json's order (a Doctrine op's arg).</summary>
    public static string DoctrineName(int index)
    {
        var k = 0;
        foreach (var name in FleetRules.Current.Doctrines.Keys)
            if (k++ == index)
                return name;
        return "?";
    }

    public static int DoctrineCount => FleetRules.Current.Doctrines.Count;

    /// <summary>Client (or host): a fleet act.</summary>
    public void SandboxFleet(FleetOp op, int slot = 255, ushort body = 0, uint uid = 0, Vector3 at = default, byte arg = 0, float value = 0)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Fleet).U8((byte)op).U8((byte)Math.Clamp(slot, 0, 255)).U16(body).U32(uid).Vec(at).U8(arg).F32(value);
        SandboxSendBuilt(w);
    }

    /// <summary>A world left: nothing of the last one's groups.</summary>
    private void FleetViewsClear()
    {
        foreach (var v in FleetGroups)
            _fleetPool.Add(v);
        FleetGroups.Clear();
        Array.Clear(_fleetSentFor);
    }

    // ---- Server: the acts --------------------------------------------------------------------------------------------------------------

    /// <summary>Who a person is to the world, for their fleet groups: their world key, else their verified Steam id (0: not known yet). Names are
    /// unique only among those online, so a group is never keyed on one.</summary>
    private ulong FleetKeyOf(Player p) => p.Bot != null ? 0 : WorldKeyOf(p) is var k and not 0 ? k : TrustedSteamId(p);

    /// <summary>Is <paramref name="p"/> the owner of <paramref name="g"/> (by their saved identity; a group from a save made before the key was kept is
    /// claimed by its owner's name once, and keyed from then on)?</summary>
    private bool FleetOwner(Player p, CrewGroup g)
    {
        if (p.Bot != null || p.CrewSlot >= 0)
            return false;
        var key = FleetKeyOf(p);
        if (key == 0)
            return false;
        if (g.OwnerKey == 0 && g.OwnerName == p.Name)
            g.OwnerKey = key;
        if (g.OwnerKey != key)
            return false;
        g.OwnerName = p.Name; // (a rename keeps the group)
        return true;
    }

    /// <summary>Server: may <paramref name="p"/> command <paramref name="g"/>? Its owner, or the leader of the owner's faction.</summary>
    private bool FleetMay(Player p, CrewGroup g)
    {
        if (!g.Players || p.Bot != null)
            return false;
        if (FleetOwner(p, g))
            return true;
        var f = g.Owner != 255 ? FactionTable.Of[g.Owner] : (byte)0;
        return f != 0 && FactionTable.Leader[f] == p.Id && FactionTable.Of[p.Id] == f;
    }

    private bool FleetMine(Player p, Construct c) => c.Owner == p.Id && !c.Wreck && Crew.ShipOf(c.Id) is { Own: true, Prize: false };

    /// <summary>Server: a <see cref="CActKind.Fleet"/> (u8 op, u8 slot, u16 body, u32 uid, vec at, u8 arg, f32 value). Null: done; else why not.</summary>
    private string? FleetAct(Player p, ref PacketReader r)
    {
        if (!r.Has(1 + 1 + 2 + 4 + 12 + 1 + 4))
            return null;
        var op = (FleetOp)r.U8();
        int slot = r.U8();
        var body = r.U16();
        var uid = r.U32();
        var at = r.Vec();
        var arg = r.U8();
        var value = r.F32();
        if (!float.IsFinite(at.X + at.Y + at.Z + value))
            return null;
        var fleet = Crew.Fleet;
        if (op == FleetOp.New)
            return FleetNew(p, body, arg == 1);
        if (fleet.Of(slot) is not { } g || !FleetMay(p, g))
            return "no fleet group of yours";
        FleetCommander(g, p); // (the one giving the order must reach the net: the owner, or their faction's leader)
        string? why = null;
        switch (op)
        {
            case FleetOp.Add:
                if (Constructs.Get(body) is not { } c || !FleetMine(p, c))
                    return "that isn't a ship of yours with a hired crew";
                why = fleet.JoinPlayer(Crew, Constructs, g, body);
                break;
            case FleetOp.Remove:
                why = fleet.LeavePlayer(Crew, g, body);
                break;
            case FleetOp.Disband:
                fleet.Disband(Crew, g);
                SandboxHands.Notices.Add((p.Id, $"Group {g.Number} stood down: each crew is its own again"));
                break;
            case FleetOp.Order:
                why = FleetOrder(p, g, (OrderKind)arg, body, at);
                break;
            case FleetOp.Stop:
            {
                var work = (OrderKind)arg;
                if (work is not (OrderKind.Load or OrderKind.Unload or OrderKind.Mine or OrderKind.Deliver or OrderKind.Route))
                    return "no such stop";
                if (FleetNetWhy(g) is { } off)
                    return off;
                why = fleet.AddStop(g, at, work, Math.Clamp(value, 0, 600));
                if (why == null)
                    SandboxHands.Notices.Add((p.Id, $"Group {g.Number}: stop {g.RouteCount} on its route, {work.ToString().ToLowerInvariant()}{(work != OrderKind.Route ? $" {Math.Clamp(value, 0, 600):0} s" : "")}"));
                break;
            }
            case FleetOp.Formation:
                if (arg > (byte)FleetFormation.Screen)
                    return "no such formation";
                if (FleetNetWhy(g) is { } off2)
                    return off2;
                g.Doctrine.Formation = (FleetFormation)arg;
                g.ThinkAt = 0;
                break;
            case FleetOp.Spacing:
                if (FleetNetWhy(g) is { } off3)
                    return off3;
                g.Doctrine.Spacing = Math.Clamp(value, CrewFleet.MinSpacing, CrewFleet.MaxSpacing);
                g.ThinkAt = 0;
                break;
            case FleetOp.Doctrine:
            {
                if (arg >= DoctrineCount)
                    return "no such doctrine";
                var (shape, spacing) = (g.Doctrine.Formation, g.Doctrine.Spacing);
                g.Doctrine = fleet.Rules.Doctrines[DoctrineName(arg)].Copy();
                (g.Doctrine.Formation, g.Doctrine.Spacing) = (shape, spacing);
                break;
            }
            case FleetOp.Admiral:
                why = fleet.SetAdmiral(Crew, g, uid);
                break;
            case FleetOp.Flagship:
                why = fleet.SetFlagship(Crew, g, body);
                break;
            case FleetOp.Task:
                if (arg > (byte)GroupTask.Construction)
                    return "no such task";
                g.Task = (GroupTask)arg;
                g.ThinkAt = 0;
                break;
            default:
                return "no such fleet act";
        }
        if (why == null)
            Console.WriteLine($"[fleet] player {p.Id} ({p.Name}): {op} on group {g.Number}");
        CrewLog();
        return why;
    }

    /// <summary>A new group of the player's own crewed ships: the picked one, or (all) every one within <see cref="FleetGatherRange"/>.</summary>
    private string? FleetNew(Player p, ushort body, bool all)
    {
        if (p.Bot != null)
            return null;
        var mine = 0;
        foreach (var g in Crew.Fleet.Groups)
            if (g.Live && g.Players && FleetOwner(p, g))
                mine++;
        if (mine >= FleetGroupsEach)
            return $"you lead {FleetGroupsEach} groups already";
        Span<ushort> ids = stackalloc ushort[CrewGroup.MaxShips];
        var n = 0;
        if (all)
        {
            for (var i = 0; i < Crew.ShipHigh && n < ids.Length; i++)
                if (Crew.Ships[i] is { Own: true, Prize: false, Group: < 0 } s && Constructs.Get(s.Body) is { } c && FleetMine(p, c)
                    && Vector3.DistanceSquared(c.WorldCom, p.Position) < FleetGatherRange * FleetGatherRange)
                    ids[n++] = c.Id;
            if (n == 0)
                return $"no ship of yours with a hired crew (and in no group) within {FleetGatherRange / 1000:0} km";
        }
        else
        {
            if (Constructs.Get(body) is not { } c || !FleetMine(p, c))
                return "that isn't a ship of yours with a hired crew (hire hands for it in the Crew window)";
            ids[n++] = body;
        }
        if (FleetKeyOf(p) == 0)
            return "the host doesn't know who you are yet: try again in a moment";
        var slot = Crew.Fleet.FormPlayer(Crew, Constructs, p.Name, FleetKeyOf(p), p.Id, ids[..n], out var why);
        if (slot < 0)
            return why;
        var ng = Crew.Fleet.Groups[slot];
        FleetCommander(ng);
        SandboxHands.Notices.Add((p.Id, $"Group {ng.Number} formed: {ng.Count} ship{(ng.Count == 1 ? "" : "s")}, you its admiral"));
        Console.WriteLine($"[fleet] player {p.Id} ({p.Name}) formed group {ng.Number} of {ng.Count}");
        CrewLog();
        return null;
    }

    /// <summary>Why the group won't hear its commander now (null: it will).</summary>
    private string? FleetNetWhy(CrewGroup g)
    {
        if (Crew.Fleet.CommanderReaches(Crew, Constructs, g))
            return null;
        var rules = Crew.Fleet.Rules;
        return g.PlayerAdmiral
            ? $"no ship of group {g.Number} hears you: out of reach of the fleet net (aboard a ship with a radio part {rules.RadioRange / 1000:0} km, your hand set {rules.VoiceRange / 1000:0.#} km)"
            : $"Admiral {CrewNames.Of(g.AdmiralName)} can't hear you: out of reach of the fleet net (aboard a ship with a radio part {rules.RadioRange / 1000:0} km, your hand set {rules.VoiceRange / 1000:0.#} km)";
    }

    /// <summary>A group order: checked, and carried by the net. Null: given.</summary>
    private string? FleetOrder(Player p, CrewGroup g, OrderKind kind, ushort body, Vector3 at)
    {
        if (kind is not (OrderKind.Form or OrderKind.GoTo or OrderKind.Engage or OrderKind.Escort or OrderKind.Hold or OrderKind.Retreat or OrderKind.Board or OrderKind.Screen))
            return "no such order";
        if (FleetNetWhy(g) is { } off)
            return off;
        var member = body != 0 && Array.IndexOf(g.Ships, body, 0, g.Count) >= 0;
        switch (kind)
        {
            case OrderKind.Form:
                if (body == 0)
                    body = g.CommanderBody; // (form on me: the body I am aboard)
                if (body == 0)
                {
                    kind = OrderKind.GoTo; // (on foot: come to me)
                    at = p.Position;
                }
                else if (Array.IndexOf(g.Ships, body, 0, g.Count) >= 0)
                    (kind, body) = (OrderKind.Hold, 0); // (on a ship of its own: they form on its flagship)
                break;
            case OrderKind.GoTo:
                if (at == default)
                    return "go where?";
                break;
            case OrderKind.Engage or OrderKind.Board or OrderKind.Escort or OrderKind.Screen:
                if (Constructs.Get(body) is not { Wreck: false })
                    return "that is gone";
                if (member)
                    return "that ship is in the group";
                break;
            case OrderKind.Retreat:
                if (at == default)
                    at = g.Home;
                break;
        }
        Crew.Fleet.CommandPlayer(g, kind, at, body);
        // A ship under a nav order from the command view leaves it: the group flies it now (one master at the helm).
        for (var k = 0; k < g.Count; k++)
            if (NavOf(g.Ships[k]) is { } nav && Constructs.Get(g.Ships[k]) is { } nc)
                NavEnd(nav, nc, "the fleet group took it");
        var what = kind switch
        {
            OrderKind.Hold => "hold",
            OrderKind.GoTo => "go there",
            OrderKind.Retreat => "pull back",
            OrderKind.Form => "form on " + FleetBodyName(body),
            _ => kind.ToString().ToLowerInvariant() + " " + FleetBodyName(body),
        };
        SandboxHands.Notices.Add((p.Id, $"Group {g.Number}: {what} ({g.OnNet} of {g.Count} on the net at its last word)"));
        return null;
    }

    private string FleetBodyName(ushort body) => Constructs.Get(body) is { } c ? (c.Owner == 255 ? $"craft {c.Id}" : $"{PlayerName(c.Owner)}'s craft {c.Id}") : "it";

    private string PlayerName(byte id) => Players.TryGetValue(id, out var p) ? p.Name : "someone";

    /// <summary>A ship given a nav order by a player leaves its fleet group (the command view's orders and the group's are one at a time).</summary>
    internal void FleetNavTaken(ushort body)
    {
        if (Crew.ShipOf(body) is { Own: true, Group: >= 0 } s && Crew.Fleet.Of(s.Group) is { Players: true } g)
        {
            Crew.Fleet.LeavePlayer(Crew, g, body);
            if (g.Owner != 255)
                SandboxHands.Notices.Add((g.Owner, $"{FleetBodyName(body)} left group {g.Number}: it has a nav order"));
        }
    }

    // ---- Server: the commanders on the net, and what each peer hears, once a second -----------------------------------------------------

    private readonly bool[] _fleetNetHas = new bool[256];
    private readonly ushort[] _fleetNetBody = new ushort[256];
    private readonly int[] _fleetNetStamp = new int[256];

    /// <summary>The player commanding <paramref name="g"/> here (its owner, else their faction's leader), or null.</summary>
    private Player? FleetCommanderOf(CrewGroup g)
    {
        Player? leader = null;
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.CrewSlot >= 0)
                continue;
            if (FleetOwner(p, g))
                return p;
            if (g.Owner != 255 && FleetMay(p, g))
                leader = p;
        }
        return leader;
    }

    /// <summary>Where <paramref name="g"/>'s commander is and how far they reach the net (aboard a body with a radio part, else a hand set).</summary>
    private void FleetCommander(CrewGroup g, Player? giving = null)
    {
        var owner = (Player?)null;
        foreach (var p in Players.Values)
            if (FleetOwner(p, g))
                owner = p;
        g.Owner = owner?.Id ?? 255;
        var by = giving ?? owner ?? FleetCommanderOf(g);
        g.CommanderHere = by is { Alive: true };
        if (by == null)
            return;
        g.CommanderAt = by.Position;
        ref var h = ref SandboxHands.Hand(by.Id);
        var aboard = h.SeatBody != 0 ? h.SeatBody : by.Sim.RideBody;
        g.CommanderBody = aboard;
        g.CommanderReach = FleetRadioAboard(by.Id, aboard) ? Crew.Fleet.Rules.RadioRange : Crew.Fleet.Rules.VoiceRange;
    }

    /// <summary>Has the body this player is aboard a part that gives access to the fleet net (kept per player until it is rebuilt)?</summary>
    private bool FleetRadioAboard(byte player, ushort body)
    {
        if (body == 0 || Constructs.Get(body) is not { Wreck: false } c)
            return false;
        if (_fleetNetBody[player] == body && _fleetNetStamp[player] == c.RebuildStamp)
            return _fleetNetHas[player];
        var has = false;
        var parts = Crew.Fleet.Rules.NetParts;
        for (var i = 0; i < c.PartCount && !has; i++)
        {
            ref var p = ref c.Parts[i];
            has = !p.Broken && (p.D.RadarRange > 0 || Array.IndexOf(parts, p.D.Name) >= 0);
        }
        (_fleetNetBody[player], _fleetNetStamp[player], _fleetNetHas[player]) = (body, c.RebuildStamp, has);
        return has;
    }

    private readonly Player?[] _fleetSentFor = new Player?[256];
    private readonly ulong[] _fleetSentHash = new ulong[256];

    /// <summary>Server, each tick: once a second, each players' group's commander on the net, a ship given away leaves, and each peer hears its groups.</summary>
    private void FleetPlayersTick()
    {
        if (ServerTick % World.TickRate != 7)
            return;
        var fleet = Crew.Fleet;
        var any = false;
        foreach (var g in fleet.Groups)
        {
            if (!g.Live || !g.Players)
                continue;
            any = true;
            FleetCommander(g);
            if (g.Owner == 255)
                continue;
            for (var k = g.Count - 1; k >= 0; k--)
                if (Constructs.Get(g.Ships[k]) is { } c && c.Owner != g.Owner && Players.ContainsKey(c.Owner) && !Factions.Shares(g.Owner, c.Owner))
                {
                    fleet.LeavePlayer(Crew, g, c.Id); // (given away, or taken: not theirs to command)
                    if (!g.Live)
                        break;
                }
        }
        foreach (var (owner, text) in fleet.Notices)
            foreach (var p in Players.Values)
                if (owner != 0 && FleetKeyOf(p) == owner)
                    SandboxHands.Notices.Add((p.Id, text));
        fleet.Notices.Clear();
        foreach (var p in Players.Values)
        {
            if (p.Bot != null || p.CrewSlot >= 0)
                continue;
            if (!any && _fleetSentFor[p.Id] == p && _fleetSentHash[p.Id] == Empty)
                continue;
            WriteFleetGroups(p);
            var hash = Fnv(SbW.Span);
            if (_fleetSentFor[p.Id] == p && _fleetSentHash[p.Id] == hash)
                continue;
            _fleetSentFor[p.Id] = p;
            _fleetSentHash[p.Id] = hash;
            if (p.Id == LocalId)
            {
                var r = new PacketReader(SbW.Span[1..]);
                ReadFleetGroups(ref r);
            }
            else if (!Net.IsOffline && GuidOf(p) is { } guid)
                Net.SendTo(guid, SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    private ulong Empty => _fleetEmpty != 0 ? _fleetEmpty : _fleetEmpty = EmptyHash();
    private ulong _fleetEmpty;

    private ulong EmptyHash()
    {
        SbW.Begin(Msg.FleetGroups).U8(0);
        return Fnv(SbW.Span);
    }

    private static ulong Fnv(ReadOnlySpan<byte> b)
    {
        var h = 14695981039346656037UL;
        foreach (var x in b)
            h = (h ^ x) * 1099511628211UL;
        return h;
    }

    /// <summary>
    /// Msg.FleetGroups: u8 n, each (u8 slot, u16 number, u8 owner, u8 flags: 1 the player leads, 2 its commander on the net, 4 pulling back; u16
    /// admiral's name, u8 order, u16 its body, vec its place, u8 task, u8 formation, f32 spacing, u8 doctrine, u8 on the net, u8 count, count x u16
    /// ship, u8 route count, each (vec, u8 work), u8 route at, f32 dwell). Nothing allocated.
    /// </summary>
    private void WriteFleetGroups(Player p)
    {
        var w = SbW;
        w.Begin(Msg.FleetGroups);
        var n = 0;
        foreach (var g in Crew.Fleet.Groups)
            if (g.Live && FleetMay(p, g) && n < FleetGroupsEach)
                n++;
        w.U8((byte)n);
        var k = 0;
        foreach (var g in Crew.Fleet.Groups)
        {
            if (!g.Live || !FleetMay(p, g) || k++ >= n)
                continue;
            var doctrine = 0;
            foreach (var name in Crew.Fleet.Rules.Doctrines.Keys)
            {
                if (name == g.Doctrine.Name)
                    break;
                doctrine++;
            }
            w.U8((byte)g.Slot).U16(g.Number).U8(g.Owner).U8((byte)((g.PlayerAdmiral ? 1 : 0) | (g.CommanderHeard ? 2 : 0) | (g.Retreating ? 4 : 0)));
            w.U16(g.PlayerAdmiral ? (ushort)0xFFFF : g.AdmiralName).U8((byte)g.Order).U16(g.OrderBody).Vec(g.OrderAt).U8((byte)g.Task).U8((byte)g.Doctrine.Formation).F32(g.Doctrine.Spacing);
            w.U8((byte)Math.Min(doctrine, 255)).U8((byte)g.OnNet).U8((byte)g.Count);
            for (var i = 0; i < g.Count; i++)
                w.U16(g.Ships[i]);
            w.U8((byte)g.RouteCount);
            for (var i = 0; i < g.RouteCount; i++)
                w.Vec(g.Route[i]).U8((byte)g.RouteWork[i]);
            w.U8((byte)g.RouteAt).F32(g.Dwell);
        }
    }

    /// <summary>Client (and the host's own): Msg.FleetGroups.</summary>
    private void ReadFleetGroups(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        int n = r.U8();
        foreach (var v in FleetGroups)
            _fleetPool.Add(v);
        FleetGroups.Clear();
        for (var k = 0; k < n && k < FleetGroupsEach; k++)
        {
            if (!r.Has(1 + 2 + 1 + 1 + 2 + 1 + 2 + 12 + 1 + 1 + 4 + 1 + 1 + 1))
            {
                Console.WriteLine($"[fleet] a fleet groups message broke off at group {k} of {n}: the rest dropped");
                return;
            }
            var v = _fleetPool.Count > 0 ? _fleetPool[^1] : new FleetGroupView();
            if (_fleetPool.Count > 0)
                _fleetPool.RemoveAt(_fleetPool.Count - 1);
            v.Slot = r.U8();
            v.Number = r.U16();
            v.Owner = r.U8();
            var flags = r.U8();
            (v.PlayerAdmiral, v.Heard, v.Retreating) = ((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
            v.AdmiralName = r.U16();
            v.Order = (OrderKind)Math.Min((int)r.U8(), (int)OrderKind.Route);
            v.OrderBody = r.U16();
            v.OrderAt = r.Vec();
            v.Task = (GroupTask)Math.Min((int)r.U8(), (int)GroupTask.Construction);
            v.Formation = (FleetFormation)Math.Min((int)r.U8(), (int)FleetFormation.Screen);
            v.Spacing = r.F32();
            v.Doctrine = r.U8();
            v.OnNet = r.U8();
            v.Count = Math.Min((int)r.U8(), CrewGroup.MaxShips);
            if (!r.Has(v.Count * 2 + 1))
                return;
            for (var i = 0; i < v.Count; i++)
                v.Ships[i] = r.U16();
            v.RouteCount = Math.Min((int)r.U8(), CrewGroup.MaxRoute);
            if (!r.Has(v.RouteCount * 13 + 5))
                return;
            for (var i = 0; i < v.RouteCount; i++)
            {
                v.Route[i] = r.Vec();
                v.RouteWork[i] = (OrderKind)Math.Min((int)r.U8(), (int)OrderKind.Route);
            }
            v.RouteAt = r.U8();
            v.Dwell = r.F32();
            FleetGroups.Add(v);
        }
    }

    /// <summary>A window run's look (the crewadmiral verb): this peer's groups in words.</summary>
    public string FleetGroupsLine()
    {
        if (FleetGroups.Count == 0)
            return "no fleet groups of yours";
        var parts = new List<string>();
        foreach (var v in FleetGroups)
            parts.Add($"group {v.Number} (slot {v.Slot}, {v.Task}, {(v.PlayerAdmiral ? "you lead it" : $"Admiral {CrewNames.Of(v.AdmiralName)}")}, {DoctrineName(v.Doctrine)} doctrine, {v.Formation} {v.Spacing:0} m): {v.Count} ships [{string.Join(", ", v.Ships.Take(v.Count))}], order {v.Order}{(v.OrderBody != 0 ? $" on {v.OrderBody}" : "")}, {v.OnNet} on the net{(v.Heard ? ", hears you" : ", OUT OF REACH")}{(v.RouteCount > 0 ? $", route of {v.RouteCount} at stop {v.RouteAt + 1}" : "")}");
        return string.Join("; ", parts);
    }

    /// <summary>A window run's look (host): each player's group's ships and orders as the server has them.</summary>
    public string FleetPlayersLine()
    {
        var parts = new List<string>();
        foreach (var g in Crew.Fleet.Groups)
        {
            if (!g.Live || !g.Players)
                continue;
            var ships = new List<string>();
            for (var k = 0; k < g.Count; k++)
                if (Crew.ShipOf(g.Ships[k]) is { } s && Constructs.Get(s.Body) is { } c)
                    ships.Add($"{c.Id} {s.Order.Kind}{(s.Order.Body != 0 ? $" {s.Order.Body}" : "")} ({s.Ai?.State ?? "no driver"}, helm {(s.HelmOn(Crew) ? "manned" : "empty")}, {c.WorldCom.Y:0} m up)");
            parts.Add($"{g.OwnerName}'s group {g.Number}: order {g.Order}{(g.OrderBody != 0 ? $" {g.OrderBody}" : "")}, {g.OnNet}/{g.Count} on the net, commander {(g.CommanderHere ? $"here reaching {g.CommanderReach:0} m" : "away")}: {string.Join("; ", ships)}");
        }
        return parts.Count == 0 ? "no players' groups" : string.Join(" | ", parts);
    }
}

/// <summary>The window tool's test state for players' fleet groups (Program.WindowShotPlay.cs's crewadmiral): host only, never in play.</summary>
public sealed partial class Game
{
    /// <summary>
    /// Host, a test state: <paramref name="n"/> of blueprint <paramref name="name"/> set down round <paramref name="me"/>, each with a full hired crew of
    /// theirs (as hiring a crew would leave it), and (with <paramref name="lead"/>) one <paramref name="lead"/> before them, crewed too. Null: asked.
    /// </summary>
    /// <summary>The test state's lead ship (the one the others escort), once set down.</summary>
    public ushort FleetTestLead;

    public string? FleetTestShips(Player me, string name, int n, string? lead)
    {
        var f = CrewPowers.ForKindName("Player Crew");
        if (ConstructBuild.FindBlueprint(name) is not { } bp || f == 0)
            return $"no blueprint \"{name}\" (or no Player Crew power)";
        var fwd = GravityFrames.FlatForward(me.Position, me.Yaw);
        var right = Vector3.Cross(fwd, GravityFrames.UpAt(me.Position));
        void Crewed(Construct? c, string what, string why, byte owner)
        {
            if (c == null)
            {
                Console.WriteLine($"[fleet] test ship {what} not set down: {why}");
                return;
            }
            c.Owner = me.Id;
            if (Crew.Crew(c, ShipAiMode.Strangers, faction: f) is { } s)
            {
                for (var k = 0; k < s.MemberCount; k++)
                    Crew.Fac[s.Members[k]] = f;
                s.SeatsDirty = s.RosterDirty = true;
            }
            if (what == lead)
                FleetTestLead = c.Id;
            Console.WriteLine($"[fleet] test ship {what}: construct {c.Id}, {me.Name}'s own crew of {Crew.ShipOf(c.Id)?.MemberCount ?? 0}");
        }
        if (lead != null)
        {
            if (ConstructBuild.FindBlueprint(lead) is not { } lb)
                return $"no blueprint \"{lead}\"";
            if (VehicleSpawn.RequestBlueprint(lb, SandboxGroundAt(me.Position + fwd * 120), me.Yaw, done: Crewed) is { } why)
                return why;
        }
        for (var i = 0; i < n; i++)
            if (VehicleSpawn.RequestBlueprint(bp, SandboxGroundAt(me.Position + fwd * 120 + right * (i - (n - 1) * 0.5f) * 120 - fwd * 150), me.Yaw, done: Crewed) is { } why)
                return why;
        return null;
    }
}
