using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A store station's people (Hexkey Orbital phase 6, Karl 2026-11-21; docs/sandbox/constructs/station.md): the station gets a crew of its own
/// power (the Traders), which takes its seats as any crew does (Crew/CrewWorld.cs): staff behind the planning desks, the canteen's counter
/// and the Big-item pick-up, shoppers on the canteen's benches. Its shoppers get up now and then and walk the store by CrewNav (their
/// bench kept for them): a few stops at room sets, shelves and the Reduced corner, a while at each, then back to sit down. The
/// store's announcements go to the people inside it. Host only, once a second from the filler's step (GameStoreFill.cs).
/// </summary>
public sealed partial class Game
{
    /// <summary>A shopper's outing: what it is doing (0 sat, 1 at or on its way to a stop, 2 going back to its bench), until when, stops left.</summary>
    private sealed class Outing
    {
        public byte Stage;
        public long Until;
        public int Left;
        public bool Done; // its browse is over (it goes back aboard once it has sat a while)
    }

    /// <summary>A shopper off a docked ship: its ship, its job aboard, when it came aboard, whether it bought.</summary>
    private sealed class StoreVisitor
    {
        public ushort Ship;
        public CrewJob Job;
        public long Since;
        public bool Bought;
    }

    private sealed class StoreLife
    {
        public Vector3[] Stops = []; // places a shopper stops at (the station's rest frame): room sets, shelves, the Reduced corner
        public readonly Dictionary<uint, Outing> Out = new(); // by the member's uid
        public long SayAt;
        public int SayTurn;
        public readonly Dictionary<ushort, ushort> Moored = new(); // ships moored here at the last look, and their berth's part
        public Random Dice = new(1);
        public readonly Dictionary<uint, StoreVisitor> Visitors = new(); // shoppers off docked ships, by uid
        public readonly Dictionary<ushort, long> OffSince = new(), SayOff = new();
        public readonly Dictionary<uint, ushort> Shopped = new(); // members who shopped while their ship is moored here, and the ship (once a visit) // a moored ship seen off its berth since (a mooring blinks while a hauler trades)
    }

    private readonly Dictionary<ushort, StoreLife> _storeLife = new(4);

    /// <summary>The canteen's benches, where the store's shoppers sit (SpaceStationHexkey.CanteenBench).</summary>
    private const string CanteenBenchName = "Waiting Bench 2 m", StaffSeatName = "Seat";

    /// <summary>How many of a docked ship's crew come aboard to shop, at most.</summary>
    private const int VisitorsPerShip = 3;

    /// <summary>Seconds a ship is off its berth before the store counts it gone; seconds a shopper stays aboard the store at most (with no body: shopping as a record).</summary>
    private const int OffFor = 3, AshoreMost = 240, AshoreRecord = 60;

    /// <summary>Is any of this ship's crew aboard a store, shopping? Its hauler waits for them (GameStationHaulers.cs), up to <see cref="AshoreMost"/>.</summary>
    private bool StoreCrewAshore(ushort ship)
    {
        foreach (var life in _storeLife.Values)
            foreach (var v in life.Visitors.Values)
                if (v.Ship == ship)
                    return true;
        return false;
    }

    /// <summary>Seconds a shopper sits between outings, at a stop, and the stops an outing makes (ranges).</summary>
    private const int SitMin = 25, SitMax = 90, StopMin = 6, StopMax = 18, StopsMin = 2, StopsMax = 5;

    /// <summary>Seconds between the store's announcements.</summary>
    private const int SayEvery = 75;

    private void StoreLifeReset() => _storeLife.Clear();

    /// <summary>Host, once a second for each store station here: its crew, its shoppers' outings, its announcements.</summary>
    private void StoreLifeStep(Construct st, StationMarket m, StationDef def, StoreLayout lay)
    {
        if (!StoreFrame(st, lay, out var aPos, out var aRot))
            return;
        if (!_storeLife.TryGetValue(st.Id, out var life))
        {
            _storeLife[st.Id] = life = new StoreLife { Dice = new Random(st.Id * 31 + 7), SayAt = ServerTick + 20 * World.TickRate };
            life.Stops = StoreStops(lay, aPos, aRot);
            foreach (var (ship, part) in m.Moored)
                life.Moored[ship] = part;
        }
        if (Crew.ShipOf(st.Id) is not { } s)
        {
            var seats = 0;
            for (var i = 0; i < st.PartCount; i++)
                if (st.DefOf(i).Name == StaffSeatName)
                    seats++;
            if (seats == 0 || Crew.Crew(st, ShipAiMode.Strangers, spares: Math.Min(seats, CrewWorld.MaxCrewPerBody), faction: StationPower(m)) is not { } made)
                return;
            Log($"[store] {def.Name}: staffed by a crew of {made.MemberCount} ({CrewPowers.Of(made.Faction)?.Name ?? "nobody's"}), {seats} staff seats");
            CrewLog();
            return;
        }
        StoreVisitors(st, s, m, life);
        StoreOutings(st, s, m, def, life);
        StoreAnnounce(st, m, def, lay, aPos, aRot, life);
    }

    /// <summary>
    /// Host: the store's shoppers are the crews of the ships docked at it (Karl, 2026-11-21): a few of a docked NPC ship's spare hands come
    /// aboard (their records move to the station's roster, as a hired hand's do: GameCrewHire.cs), take a canteen bench, browse and buy for
    /// their ship, and go back aboard when it casts off (or is gone: then they are gone too).
    /// </summary>
    private void StoreVisitors(Construct st, CrewShip s, StationMarket m, StoreLife life)
    {
        // back aboard: their browse is over, they've been ashore long enough, or their ship cast off (off its berth a few seconds)
        var now = ServerTick;
        foreach (var (uid, v) in life.Visitors)
        {
            var (ship, job) = (v.Ship, v.Job);
            var moored = Constructs.Get(ship) is { } c && c.MooredTo == st.Id;
            if (moored)
                life.OffSince.Remove(ship);
            else if (!life.OffSince.ContainsKey(ship))
                life.OffSince[ship] = now;
            var gone = !moored && now - life.OffSince[ship] >= OffFor * World.TickRate;
            var mem0 = StoreMember(s, uid);
            var done = life.Out.TryGetValue(uid, out var o0) && o0.Done && now >= o0.Until;
            var spent = now - v.Since >= (mem0 >= 0 && Crew.Player[mem0] == 0 ? AshoreRecord : AshoreMost) * World.TickRate;
            if (!gone && !done && !spent)
                continue;
            if (!v.Bought && mem0 >= 0 && !gone)
                StoreVisitorBuys(st, m, StationMarkets.All[m.Def], life, mem0); // (shopped as a record, or ran out of time: its pick still made)
            life.Visitors.Remove(uid);
            life.Out.Remove(uid);
            life.OffSince.Remove(ship);
            var mem = StoreMember(s, uid);
            if (mem < 0)
            {
                Log($"[store] {StationName(st)}: shopper {uid} off construct {ship} is no longer aboard the store (dead, or gone some other way)");
                continue;
            }
            var (name, skill, health, fac) = (Crew.Name[mem], Crew.Skill[mem], Crew.Health[mem], Crew.Fac[mem]);
            Crew.Release(mem, this);
            if (Constructs.Get(ship) is { Wreck: false } back && Crew.ShipOf(ship) is { } home && Crew.Add(home, job, -1, uid, health, skill, name) is var h and >= 0)
            {
                Crew.Fac[h] = fac;
                Log($"[store] {StationName(st)}: {CrewNames.Of(name)} went back aboard construct {back.Id} ({(gone ? "it cast off" : done ? "done shopping" : "out of time")}, {(now - v.Since) / World.TickRate} s aboard the store)");
            }
            else
                Log($"[store] {StationName(st)}: {CrewNames.Of(name)}'s ship is gone: they leave with the next");
            break; // (one a second: the dictionary changed)
        }
        // aboard: a docked ship's spare hands, a few each
        foreach (var (ship, _) in m.Moored)
        {
            if (Crew.ShipOf(ship) is not { Faction: not 0, Own: false, Prize: false, Boarded: false } from || s.MemberCount >= CrewWorld.MaxCrewPerBody)
                continue;
            var have = 0;
            foreach (var v in life.Visitors.Values)
                if (v.Ship == ship)
                    have++;
            for (var k = from.MemberCount - 1; k >= 0 && have < VisitorsPerShip && s.MemberCount < CrewWorld.MaxCrewPerBody; k--)
            {
                var mem = from.Members[k];
                var stn = Crew.Station[mem];
                if (life.Shopped.ContainsKey(Crew.Uid[mem]) || Crew.State[mem] is not (CrewState.Station or CrewState.Free) || Crew.Job[mem] != CrewJob.Spare && !(stn >= 0 && stn < from.StCount && from.StKind[stn] == CrewStationKind.Rest))
                    continue;
                var (uid, name, skill, health, fac, job) = (Crew.Uid[mem], Crew.Name[mem], Crew.Skill[mem], Crew.Health[mem], Crew.Fac[mem], Crew.Job[mem]);
                Crew.Release(mem, this);
                var here = Crew.Add(s, CrewJob.Spare, -1, uid, health, skill, name);
                if (here < 0)
                {
                    Crew.Add(from, job, -1, uid, health, skill, name); // (no room after all: stays aboard)
                    break;
                }
                Crew.Fac[here] = fac;
                life.Visitors[uid] = new StoreVisitor { Ship = ship, Job = job, Since = ServerTick };
                life.Shopped[uid] = ship;
                have++;
                Log($"[store] {StationName(st)}: {CrewNames.Of(name)} off construct {ship} came aboard to shop (tick {ServerTick})");
                k = Math.Min(k, from.MemberCount); // (the roster shrank)
            }
        }
    }

    private int StoreMember(CrewShip s, uint uid)
    {
        for (var k = 0; k < s.MemberCount; k++)
            if (Crew.Uid[s.Members[k]] == uid && Crew.State[s.Members[k]] != CrewState.Dead)
                return s.Members[k];
        return -1;
    }

    /// <summary>A visitor at the end of its browse buys a part in stock for its ship (into its hold where a slot takes it): the stock and the power's books as a sale.</summary>
    private void StoreVisitorBuys(Construct st, StationMarket m, StationDef def, StoreLife life, int mem)
    {
        if (!life.Visitors.TryGetValue(Crew.Uid[mem], out var v) || v.Bought || def.Parts.Length == 0)
            return;
        v.Bought = true;
        if (life.Dice.Next(3) == 0)
            return; // (one shopper in three buys nothing)
        var ng = def.Goods.Length;
        var inStock = 0;
        for (var j = 0; j < def.Parts.Length; j++)
            if (ng + j < m.Stock.Length && m.Stock[ng + j] > 0)
                inStock++;
        if (inStock == 0)
            return;
        var pick = life.Dice.Next(inStock);
        for (var j = 0; j < def.Parts.Length; j++)
        {
            if (ng + j >= m.Stock.Length || m.Stock[ng + j] <= 0 || pick-- > 0 || ConstructParts.Def(def.Parts[j].Part) is not { } d)
                continue;
            m.Stock[ng + j]--;
            StationsVersion++;
            StationMargin(m, def.Parts[j].Sell);
            var into = Constructs.Get(v.Ship) is { Wreck: false } ship && StoreIntoHold(ship, d) ? $" (into construct {ship.Id}'s hold)" : "";
            Log($"[store] {StationName(st)}: {CrewNames.Of(Crew.Name[mem])} bought a {d.Name} for {MathF.Ceiling(def.Parts[j].Sell):0}{into}");
            return;
        }
    }

    /// <summary>A part into the first empty storage slot of a ship that takes it. False: none does.</summary>
    private bool StoreIntoHold(Construct ship, ConstructPartDef d)
    {
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (Constructs.StoreOf(ship, i) is not { } store)
                continue;
            for (var k = 0; k < store.SlotCount; k++)
                if (store.Def[k] == ConstructParts.None && Constructs.SetIn(ship, i, k, d) == null)
                    return true;
        }
        return false;
    }

    /// <summary>The stops a shopper makes, in the station's rest frame: a step into each room set from the route, each shelf's front, the Reduced corner's.</summary>
    private static Vector3[] StoreStops(StoreLayout lay, Vector3 aPos, byte aRot)
    {
        var stops = new List<Vector3>(lay.Bays.Length + lay.Shelves.Length / 8);
        foreach (var b in lay.Bays)
        {
            var z = b.OpenPlusZ ? b.Max.Z - 1.2f : b.Min.Z + 1.2f; // (just inside the room, by its open side)
            stops.Add(StoreLocal(aPos, aRot, lay, new Vector3((b.Min.X + b.Max.X) / 2, b.Min.Y + 0.05f, z)));
        }
        for (var i = 0; i < lay.Shelves.Length; i += 8) // (one in eight: a shelf every few metres along each aisle)
        {
            var sp = lay.Shelves[i];
            var c = (sp.Min + sp.Max) / 2 + sp.Front * ((sp.Max - sp.Min).Length() / 2 + 0.8f);
            stops.Add(StoreLocal(aPos, aRot, lay, new Vector3(c.X, sp.Min.Y + 0.05f, c.Z)));
        }
        return [.. stops];
    }

    /// <summary>The store's shoppers: members on a canteen bench get up, walk a few stops and come back to sit down.</summary>
    private void StoreOutings(Construct st, CrewShip s, StationMarket m, StationDef def, StoreLife life)
    {
        if (life.Stops.Length == 0 || s.Tier != CrewTier.Bodied || s.Boarded)
            return;
        var now = ServerTick;
        var host = (ICrewHost)this;
        for (var k = 0; k < s.MemberCount; k++)
        {
            var mem = s.Members[k];
            var stn = Crew.Station[mem];
            if (Crew.State[mem] != CrewState.Station || stn < 0 || stn >= s.StCount || s.StPart[stn] is var bench && (bench < 0 || bench >= st.PartCount)
                || st.DefOf(bench).Name != CanteenBenchName || !life.Visitors.ContainsKey(Crew.Uid[mem]))
                continue;
            if (!life.Out.TryGetValue(Crew.Uid[mem], out var o))
                life.Out[Crew.Uid[mem]] = o = new Outing { Until = now + life.Dice.Next(4, 15) * World.TickRate }; // (up soon: its ship won't wait all day)
            if (Crew.Player[mem] == 0)
            {
                (o.Stage, o.Until) = (0, now + life.Dice.Next(SitMin, SitMax) * World.TickRate); // (no body: it sits as a record)
                continue;
            }
            var who = (byte)(Crew.Player[mem] - 1);
            if (o.Done || now < o.Until || !Crew.ErrandDone(mem))
                continue;
            switch (o.Stage)
            {
                case 0: // up off the bench, out to the first stop
                    if (!Players.TryGetValue(who, out var p) || !host.Feet(who, out _))
                        break;
                    host.StandAt(who, st, CrewWorld.PartFoot(st, bench) + new Vector3(0, 0, -0.8f));
                    o.Left = life.Dice.Next(StopsMin, StopsMax + 1);
                    o.Stage = 1;
                    Log($"[store] {StationName(st)}: shopper {Crew.Uid[mem]} gets up for {o.Left} stops");
                    o.Until = now + World.TickRate; // (a second to stand)
                    break;
                case 1 when o.Left > 0: // the next stop (or another, if no way there)
                    o.Left--;
                    var off = false;
                    for (var t = 0; t < 4 && !off; t++)
                        off = StoreNearStop(st, life, who, out var stop) && Crew.Errand(st, s, mem, stop, this);
                    if (!off)
                        Log($"[store] {StationName(st)}: shopper {Crew.Uid[mem]} found no way to a stop near it");
                    o.Until = now + life.Dice.Next(StopMin, StopMax) * World.TickRate; // (counted from its arrival: the errand runs first)
                    break;
                case 1: // done browsing: buys a part for its ship, then home to the bench
                    StoreVisitorBuys(st, m, def, life, mem);
                    o.Stage = Crew.Errand(st, s, mem, CrewWorld.PartFoot(st, bench), this) ? (byte)2 : (byte)3;
                    Log($"[store] {StationName(st)}: shopper {Crew.Uid[mem]} heads back to the canteen{(o.Stage == 3 ? " (no way: put there)" : "")}");
                    break;
                default: // there (or no way back: sat down where the bench is, as a walk's timer puts a crew member); a while, then back aboard
                    host.Sit(who, st, bench);
                    (o.Stage, o.Until, o.Done) = (0, now + life.Dice.Next(10, 30) * World.TickRate, true);
                    break;
            }
        }
    }

    /// <summary>
    /// A stop near a shopper (its own level, within <see cref="StopReach"/>): a crew's walk search has a reach (CrewNav.MaxExpand), and a
    /// store's walk graph is big, so a shopper browses the floor it is on, a stop or two along at a time.
    /// </summary>
    private bool StoreNearStop(Construct st, StoreLife life, byte who, out Vector3 stop)
    {
        stop = default;
        if (!((ICrewHost)this).Feet(who, out var feet))
            return false;
        var at = st.ToLocal(feet);
        for (var t = 0; t < 24; t++)
        {
            var c = life.Stops[life.Dice.Next(life.Stops.Length)];
            if (MathF.Abs(c.Y - at.Y) < 2 && Vector2.DistanceSquared(new Vector2(c.X, c.Z), new Vector2(at.X, at.Z)) < StopReach * StopReach)
            {
                stop = c;
                return true;
            }
        }
        return false;
    }

    /// <summary>How far off a shopper's next stop may be (m).</summary>
    private const float StopReach = 30;

    /// <summary>The store's announcements to the people inside it: offers in the Reduced corner, the newest delivery, a ship casting off.</summary>
    private void StoreAnnounce(Construct st, StationMarket m, StationDef def, StoreLayout lay, Vector3 aPos, byte aRot, StoreLife life)
    {
        string? say = null;
        // a ship casting off: said at once
        foreach (var (ship, part) in life.Moored)
            if (Constructs.Get(ship) is var c && (c == null || c.MooredTo != st.Id))
            {
                if (c != null && !life.SayOff.ContainsKey(ship))
                    life.SayOff[ship] = ServerTick;
                if (c != null && ServerTick - life.SayOff[ship] < OffFor * World.TickRate)
                    continue; // (a mooring blinks while a hauler trades: off for a few seconds first)
                life.SayOff.Remove(ship);
                foreach (var (u, sh) in life.Shopped)
                    if (sh == ship)
                        life.Shopped.Remove(u); // (its next visit, its crew shops again)
                say = c != null ? $"the {StoreShipName(c)} is leaving {StoreBerthName(st, part)}. Have a safe flight." : null;
                life.Moored.Remove(ship);
                break;
            }
        foreach (var (ship, part) in m.Moored)
        {
            if (Constructs.Get(ship)?.MooredTo != st.Id)
                continue; // (the market's list is a second old: one that just cast off isn't moored again)
            life.Moored[ship] = part;
            life.SayOff.Remove(ship);
        }
        if (say == null && ServerTick >= life.SayAt)
        {
            life.SayAt = ServerTick + SayEvery * World.TickRate;
            say = (life.SayTurn++ % 3) switch
            {
                0 when StoreReducedOffer(st, def) is { } r => $"in the Reduced corner today: {r}. While they last.",
                1 when _storeSpotlight.TryGetValue(st.Id, out var sp) && ConstructParts.Def(sp) is { } spd => $"new in: the {spd.Name}. See it in the Spotlight room on the showroom floor.",
                _ => "trolleys wait at Arrivals. Push yours through a trade arch and the trade is done.",
            };
        }
        if (say == null)
            return;
        var heard = 0;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Bot != null || p.CrewSlot >= 0)
                continue;
            var here = LayoutOf(aPos, aRot, lay, p.Sim.RideBody == st.Id && p.Sim.RideSeg == 0 ? p.Sim.RideLocal : st.ToLocal(p.Position));
            if (here.X < lay.StoreMin.X || here.X > lay.StoreMax.X || here.Y < lay.StoreMin.Y || here.Y > lay.StoreMax.Y || here.Z < lay.StoreMin.Z || here.Z > lay.StoreMax.Z)
                continue;
            SandboxHands.Notices.Add((p.Id, $"{def.Name}: {say}"));
            heard++;
        }
        if (heard > 0)
            Log($"[store] {def.Name} announced to {heard}: {say}");
    }

    /// <summary>One of the store's Reduced pieces and its price ("Wall Shelf at 4"); null when the corner is empty.</summary>
    private string? StoreReducedOffer(Construct st, StationDef def)
    {
        if (!_shops.TryGetValue(st.Id, out var sh) || sh.ReducedAt.Count == 0)
            return null;
        for (var i = 0; i < st.PartCount; i++)
        {
            if (!sh.ReducedAt.Contains(ShelfKey(st.Parts[i].Pos)) || Constructs.StoreOf(st, i) is not { } store)
                continue;
            for (var k = 0; k < store.SlotCount; k++)
                if (ConstructParts.Def(store.Def[k]) is { } d && StorePartIndex(def, (ushort)d.Id) is var j and >= 0)
                    return $"the {d.Name} at {MathF.Ceiling(def.Parts[j].Sell * ReducedShare):0}";
        }
        return null;
    }

    /// <summary>What the store calls a ship: its power's ("the Traders' Packet Spine Freighter"), a person's, else its blueprint's name.</summary>
    private string StoreShipName(Construct c)
    {
        var kind = c.HaulRoute >= 0 && c.HaulRoute < StationMarkets.Haulers.Length && StationMarkets.Haulers[c.HaulRoute].Ship.Length > 0 ? StationMarkets.Haulers[c.HaulRoute].Ship : "ship";
        if (c.Owner != 255)
            return $"{PlayerName(c.Owner)}'s {kind}";
        return Crew.ShipOf(c.Id) is { Faction: not 0 } s && CrewPowers.Of(s.Faction) is { } pw ? $"{kind} of the {pw.Name}" : kind;
    }

    /// <summary>"pad 7" (a roof pad: its Docking Clamp Medium counted in the station's order) or "the freight pier".</summary>
    private static string StoreBerthName(Construct st, ushort part)
    {
        if (part >= st.PartCount)
            return "its berth";
        var d = st.DefOf(part);
        if (d.Name != "Docking Clamp Medium")
            return "the freight pier";
        var n = 0;
        for (var i = 0; i <= part; i++)
            if (st.DefOf(i) == d)
                n++;
        return $"pad {n}";
    }
}
