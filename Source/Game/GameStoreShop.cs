using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A store station's shop floor (Hexkey Orbital phase 5; docs/sandbox/constructs/station.md "Hexkey Orbital"). The stock the showroom shows
/// stands for sale on real shelves: small parts on the market hall's gondolas, parts to about 0.9 m in the warehouse racks, a few odd lots
/// at 0.6 of the price in the Reduced corner, and anything bigger loose on the Big-item pick-up's floor. The shelves are refilled from the
/// market's stock as it changes, so what is on them is always part of the stock, never more. A shopper takes a part off a shelf (by hand or
/// the physics gun) and it goes in their basket and out of the stock; putting it back takes it off the basket. Walking through a trade arch
/// pays for the basket: off their tab with the store's power, else in the goods they carry, else from their ship's hold (Karl, 2026-11-20:
/// "payment = the trade"). Hexkey Trolleys wait at Arrivals for carrying the lot.
/// </summary>
public sealed partial class Game
{
    /// <summary>A part a shopper took and hasn't paid for: which store, which part, its price.</summary>
    private readonly record struct BasketLine(ushort Station, ushort Part, float Price, ushort Body = 0, int Slot = -1); // (Body, Slot: a pick-up piece and the place it came from)

    private sealed class ShopState
    {
        public readonly Dictionary<long, int> ShelfAt = new(); // a shelf's place (its rounded origin) -> its zone's index in the layout
        public readonly HashSet<long> ReducedAt = new(); // ...the Reduced corner's shelves
        public ushort[] PickBody = [], PickPart = [];
        public ushort[] Trolley = [];
        public int ShelfHash = int.MinValue, Seed;
        public bool Warned;
    }

    private readonly Dictionary<ushort, ShopState> _shops = new(4);
    private readonly Dictionary<byte, List<BasketLine>> _baskets = new(8);
    private readonly Dictionary<byte, Vector3> _shopperAt = new(8); // a shopper's last place in their store's layout frame (for the arches' line)
    private readonly Dictionary<byte, Vector3> _shopperIn = new(8); // ...and their last place inside the store's box (where one leaving unpaid is put back)
    private readonly Dictionary<ushort, ushort> _storeSpotlight = new(4); // a store station -> the part its last delivery brought most of

    /// <summary>The Reduced corner's price, of the store's.</summary>
    private const float ReducedShare = 0.6f;

    /// <summary>How many of a kind a shelf slot shows at most (the rest stays in the stock room).</summary>
    private const int ShelfShowMost = 12;

    private const string GondolaBayName = "Hexkey Gondola Bay", RackBayName = "Hexkey Rack Bay", TrolleyName = "Hexkey Trolley";

    private static long ShelfKey(Vector3 p) => ((long)MathF.Round(p.X * 20) & 0x1FFFFF) << 42 | ((long)MathF.Round(p.Y * 20) & 0x1FFFFF) << 21 | (long)MathF.Round(p.Z * 20) & 0x1FFFFF; // (5 cm cells: a part's place rounds to the centimetre)

    /// <summary>Server: reset with the world (the shelves are found again from the station's parts).</summary>
    private void StoreShopReset()
    {
        _shops.Clear();
        _baskets.Clear();
        _shopperAt.Clear();
        _shopperIn.Clear();
        _storeSpotlight.Clear();
    }

    /// <summary>Server: a parts delivery reached a store station; the part it brought most of goes in the Spotlight room.</summary>
    private void StoreDelivered(ushort station, ushort part)
    {
        if (_storeFills.ContainsKey(station))
            _storeSpotlight[station] = part;
    }

    private static Vector3 LayoutOf(Vector3 anchorPos, byte anchorRot, StoreLayout lay, Vector3 local) =>
        lay.Origin + Vector3.Transform(local - anchorPos, Quaternion.Conjugate(Rot24.Quat(anchorRot)));

    private static Vector3 StoreWorld(Construct st, Vector3 local) => st.Pos + Vector3.Transform(local, st.Rot);

    // ---- the shelves --------------------------------------------------------------------------------------------------------

    /// <summary>Host, from the filler's pass: the shelves stood, filled and trimmed to the stock; the pick-up's big pieces and the trolleys laid out.</summary>
    private void StoreShelvesStep(Construct st, StationMarket m, StationDef def, StoreLayout lay, int stockHash)
    {
        if (lay.Shelves.Length == 0 || !StoreFrame(st, lay, out var aPos, out var aRot))
            return;
        if (!_shops.TryGetValue(st.Id, out var sh))
        {
            _shops[st.Id] = sh = new ShopState { Seed = st.Id * 7919 };
            StoreClearOld(st, def, lay, aPos, aRot); // (a loaded world's shelves, trolleys and pick-up pieces: set out again, tracked)
        }
        if (ConstructParts.Find(GondolaBayName) is not { } gondola || ConstructParts.Find(RackBayName) is not { } rack || gondola.Slots.Length == 0 || rack.Slots.Length == 0)
        {
            if (!sh.Warned)
                Log($"[store] {def.Name}: no {GondolaBayName} or {RackBayName} part, its shelves are left empty");
            sh.Warned = true;
            return;
        }
        var changed = StoreTrolleys(st, lay, aPos, aRot, sh);
        if (stockHash == sh.ShelfHash && !changed)
            return; // (nothing moved: a shopper's take changes the stock, and so the hash, so the shelf is refilled on the next pass)
        sh.ShelfHash = stockHash;
        // what each kind in stock goes on: a gondola's slot, a rack's, or the pick-up floor
        var rooms = StoreRooms.Current;
        var deptOf = new Dictionary<string, int>();
        for (var d = 0; d < rooms.Departments.Length; d++)
        {
            foreach (var j in rooms.Departments[d].WallJobs)
                deptOf.TryAdd(j, d);
            foreach (var r in rooms.Departments[d].Rooms)
                foreach (var sl in r.Slots)
                    deptOf.TryAdd(sl.Job, d);
        }
        var ng = def.Goods.Length;
        var market = new List<int>();
        var warehouse = new List<int>();
        var pickup = new List<int>();
        for (var j = 0; j < def.Parts.Length && ng + j < m.Stock.Length; j++)
        {
            if (m.Stock[ng + j] <= 0 || def.Parts[j].Sell <= 0 || ConstructParts.Def(def.Parts[j].Part) is not { } pd)
                continue;
            if (!pd.PhysicsOnly && ConstructStorage.Fits(gondola.Slots[0], pd, out _))
                market.Add(j);
            else if (!pd.PhysicsOnly && ConstructStorage.Fits(rack.Slots[0], pd, out _))
                warehouse.Add(j);
            else
                pickup.Add(j);
        }
        int Order(int a, int b)
        {
            var ja = rooms.JobOf.TryGetValue(def.Parts[a].Part, out var x) && deptOf.TryGetValue(x, out var da) ? da : 99;
            var jb = rooms.JobOf.TryGetValue(def.Parts[b].Part, out var y) && deptOf.TryGetValue(y, out var db) ? db : 99;
            return ja != jb ? ja.CompareTo(jb) : string.CompareOrdinal(ConstructParts.Def(def.Parts[a].Part)?.Name, ConstructParts.Def(def.Parts[b].Part)?.Name);
        }
        market.Sort(Order);
        warehouse.Sort(Order);
        pickup.Sort(Order);
        // the Reduced corner: a few odd lots, rolled again each time the stock changes
        var reduced = new List<int>();
        var rng = new Random(sh.Seed ^ stockHash);
        var odd = market.Concat(warehouse).Where(j => m.Stock[ng + j] >= 2).ToList();
        var reducedSlots = lay.Shelves.Where(s => s.Zone == "reduced").Sum(s => (ConstructParts.Find(s.Part)?.Slots.Length) ?? 0);
        while (odd.Count > 0 && reduced.Count < Math.Min(reducedSlots, 8))
        {
            var k = rng.Next(odd.Count);
            reduced.Add(odd[k]);
            odd.RemoveAt(k);
        }
        // the kinds into slots, zone by zone, in the layout's order of places
        var want = new List<(int Spot, int Slot, int J, int Show)>();
        void Lay(string zone, List<int> kinds, bool one)
        {
            var at = 0;
            for (var s = 0; s < lay.Shelves.Length && at < kinds.Count; s++)
            {
                if (lay.Shelves[s].Zone != zone || ConstructParts.Find(lay.Shelves[s].Part) is not { } bay)
                    continue;
                for (var k = 0; k < bay.Slots.Length && at < kinds.Count; k++, at++)
                    want.Add((s, k, kinds[at], one ? 1 : ShelfShowMost));
            }
            if (at < kinds.Count)
                Log($"[store] {def.Name}: {kinds.Count - at} kind(s) left off the {zone} shelves (no shelf place left)");
        }
        Lay("reduced", reduced, true);
        Lay("market", market, false);
        Lay("warehouse", warehouse, false);
        // the shelves that are wanted stood, the others emptied and taken away
        var used = new HashSet<int>(want.Select(w => w.Spot));
        var have = new Dictionary<long, int>();
        for (var i = 0; i < st.PartCount; i++)
            if (st.DefOf(i) == gondola || st.DefOf(i) == rack)
                have[ShelfKey(st.Parts[i].Pos)] = i;
        var removed = 0;
        for (var i = st.PartCount - 1; i >= 0; i--)
        {
            var d = st.DefOf(i);
            if (d != gondola && d != rack || !sh.ShelfAt.TryGetValue(ShelfKey(st.Parts[i].Pos), out var spot) || used.Contains(spot))
                continue;
            StoreEmptyShelf(st, i);
            Constructs.RemovePart(st, i);
            removed++;
        }
        if (removed > 0)
        {
            have.Clear();
            for (var i = 0; i < st.PartCount; i++)
                if (st.DefOf(i) == gondola || st.DefOf(i) == rack)
                    have[ShelfKey(st.Parts[i].Pos)] = i;
        }
        sh.ShelfAt.Clear();
        sh.ReducedAt.Clear();
        var added = 0;
        var at = new Dictionary<int, int>(); // spot -> part index
        foreach (var s in used.Order())
        {
            var spot = lay.Shelves[s];
            var bay = ConstructParts.Find(spot.Part)!;
            var (pos, rot) = StoreShelfPlace(bay, spot, lay, aPos, aRot);
            var key = ShelfKey(pos);
            sh.ShelfAt[key] = s;
            if (spot.Zone == "reduced")
                sh.ReducedAt.Add(key);
            if (have.TryGetValue(key, out var i))
            {
                at[s] = i;
                continue;
            }
            i = Constructs.AddPart(st, bay, pos, rot);
            if (i < 0)
            {
                Log($"[store] {def.Name}: the station took no more parts ({st.PartCount}); a {bay.Name} in the {spot.Zone} left out");
                continue;
            }
            var host = -1;
            for (var h = 0; h < st.PartCount && host < 0; h++)
                if (st.DefOf(h).Name == spot.Host)
                    host = h;
            if (host < 0 || !Constructs.AddLink(st, i, 0, host, 0))
            {
                Constructs.RemovePart(st, i);
                Log($"[store] {def.Name}: a {bay.Name} in the {spot.Zone} left out ({(host < 0 ? $"no {spot.Host}" : "the station took no more links")})");
                continue;
            }
            at[s] = i;
            added++;
        }
        if (added + removed > 0)
        {
            st.Rebuild();
            var kept = 0;
            for (var i = 0; i < st.PartCount; i++)
                if (st.DefOf(i) == gondola || st.DefOf(i) == rack)
                    kept++;
            if (kept < used.Count)
                Log($"[store] {def.Name}: {used.Count - kept} of {used.Count} shelves came off the station at its rebuild");
        }
        // the slots: the right kind in each, as many as the stock holds (the Reduced corner's piece first)
        var shown = new int[def.Parts.Length];
        foreach (var (s, k, j, show) in want)
        {
            if (!at.TryGetValue(s, out var i) || ConstructParts.Def(def.Parts[j].Part) is not { } pd)
                continue;
            var left = m.Stock[ng + j] - shown[j];
            shown[j] += StoreFillSlot(st, i, k, pd, Math.Min(show, left));
        }
        foreach (var (s, i) in at) // slots no kind was given: emptied
            if (Constructs.StoreOf(st, i) is { } stx)
                for (var k = 0; k < stx.SlotCount; k++)
                    if (!want.Any(w => w.Spot == s && w.Slot == k))
                        while (Constructs.TakeOut(st, i, k) != null) { }
        StorePickup(st, m, def, lay, aPos, aRot, sh, pickup);
        Log($"[store] {def.Name}: shelves {used.Count} ({added} stood, {removed} taken away): {market.Count} kinds in the market hall, {warehouse.Count} in the warehouse, {reduced.Count} reduced, {Math.Min(pickup.Count, lay.Pickup.Length)} at the pick-up");
    }

    /// <summary>A shelf's origin and turn (station frame) that stands it in its place's box with its open front the place's way.</summary>
    private static (Vector3 Pos, byte Rot) StoreShelfPlace(ConstructPartDef bay, StoreLayout.ShelfSpot spot, StoreLayout lay, Vector3 aPos, byte aRot)
    {
        var a = StoreLocal(aPos, aRot, lay, spot.Min);
        var b = StoreLocal(aPos, aRot, lay, spot.Max);
        var (mn, mx) = (Vector3.Min(a, b), Vector3.Max(a, b));
        var rot = StoreTurnWorld(bay, Rot24.Apply(aRot, -spot.Front), false); // (its back away from the front)
        var (lo, hi) = StoreBox(bay, rot);
        var mid = (mn + mx) / 2 - (lo + hi) / 2;
        return (new Vector3(MathF.Round(mid.X, 2), MathF.Round(mn.Y - lo.Y, 2), MathF.Round(mid.Z, 2)), rot); // (a part's place rounds to the centimetre)
    }

    /// <summary>Server: slot <paramref name="k"/> of shelf <paramref name="i"/> holding <paramref name="n"/> of <paramref name="pd"/> (as many as fit); how many it holds after.</summary>
    private int StoreFillSlot(Construct st, int i, int k, ConstructPartDef pd, int n)
    {
        if (Constructs.StoreOf(st, i) is not { } sto || k >= sto.SlotCount)
            return 0;
        if (sto.Def[k] != ConstructParts.None && sto.Def[k] != pd.Id)
            while (Constructs.TakeOut(st, i, k) != null) { }
        while (sto.Def[k] == pd.Id && sto.Count[k] > Math.Max(0, n))
            Constructs.TakeOut(st, i, k);
        while ((sto.Def[k] == ConstructParts.None ? 0 : sto.Count[k]) < n && Constructs.SetIn(st, i, k, pd) == null) { }
        return sto.Def[k] == pd.Id ? sto.Count[k] : 0;
    }

    private void StoreEmptyShelf(Construct st, int i)
    {
        if (Constructs.StoreOf(st, i) is { } sto)
            for (var k = 0; k < sto.SlotCount; k++)
                while (Constructs.TakeOut(st, i, k) != null) { }
    }

    /// <summary>
    /// A loose body the store set out rides the station's frame from its first tick (ConstructFrames: a station in orbit is a posed ship,
    /// and a body left to the physics beside it drifts off it, a third of a metre a second: seen in a window run, 2026-11-21).
    /// </summary>
    private static void StoreAboard(Construct st, Construct c)
    {
        c.Vel = st.Vel;
        if (ConstructFrames.CanRide(c) && ConstructFrames.CanCarry(st, c))
            ConstructFrames.Join(c, st);
    }

    // ---- the Big-item pick-up and the trolleys ------------------------------------------------------------------------------

    private void StorePickup(Construct st, StationMarket m, StationDef def, StoreLayout lay, Vector3 aPos, byte aRot, ShopState sh, List<int> pickup)
    {
        if (sh.PickBody.Length != lay.Pickup.Length)
            (sh.PickBody, sh.PickPart) = (new ushort[lay.Pickup.Length], new ushort[lay.Pickup.Length]);
        var spawned = 0;
        for (var s = 0; s < lay.Pickup.Length; s++)
        {
            var part = s < pickup.Count ? def.Parts[pickup[s]].Part : ConstructParts.None;
            var body = Constructs.Get(sh.PickBody[s]);
            if (body != null && sh.PickPart[s] == part)
                continue;
            if (body != null)
                Constructs.Remove(body); // (a kind no longer in stock, or another in its place)
            (sh.PickBody[s], sh.PickPart[s]) = (0, ConstructParts.None);
            if (part == ConstructParts.None || ConstructParts.Def(part) is not { } pd)
                continue;
            var up = Vector3.Transform(Vector3.UnitY, st.Rot);
            var centre = StoreWorld(st, StoreLocal(aPos, aRot, lay, lay.Pickup[s])) + up * ((pd.BoundsMax.Y - pd.BoundsMin.Y) / 2 + 0.02f);
            if (ConstructEdits.DropLoose(Constructs, pd, centre, st.Rot * Rot24.Quat(aRot)) is not { } c)
            {
                Log($"[store] {def.Name}: no room in the world for a {pd.Name} at the pick-up");
                continue;
            }
            StoreAboard(st, c);
            (sh.PickBody[s], sh.PickPart[s]) = (c.Id, part);
            spawned++;
        }
        if (spawned > 0)
            Log($"[store] {def.Name}: {spawned} big piece(s) set out at the pick-up");
    }

    /// <summary>Host: each trolley place with a trolley (a new one where the last is gone). True when one was set out.</summary>
    private bool StoreTrolleys(Construct st, StoreLayout lay, Vector3 aPos, byte aRot, ShopState sh)
    {
        if (lay.Trolleys.Length == 0 || ConstructParts.Find(TrolleyName) is not { } pd)
            return false;
        if (sh.Trolley.Length != lay.Trolleys.Length)
            sh.Trolley = new ushort[lay.Trolleys.Length];
        var any = false;
        var up = Vector3.Transform(Vector3.UnitY, st.Rot);
        for (var t = 0; t < lay.Trolleys.Length; t++)
        {
            if (Constructs.Get(sh.Trolley[t]) is { Wreck: false })
                continue;
            var centre = StoreWorld(st, StoreLocal(aPos, aRot, lay, lay.Trolleys[t])) + up * ((pd.BoundsMax.Y - pd.BoundsMin.Y) / 2 + 0.02f);
            if (ConstructEdits.DropLoose(Constructs, pd, centre, st.Rot * Rot24.Quat(aRot)) is not { } c)
                break;
            StoreAboard(st, c);
            sh.Trolley[t] = c.Id;
            any = true;
        }
        if (any)
            Log($"[store] {StationName(st)}: trolleys set out at Arrivals");
        return any;
    }

    // ---- baskets and the trade arches ---------------------------------------------------------------------------------------

    /// <summary>Server (the hands' hook): a part set on or taken off one of a store's shelves by a shopper.</summary>
    private void StoreShopMoved(byte player, Construct c, int part, ushort partDef, bool put)
    {
        if (!_shops.TryGetValue(c.Id, out var sh) || !sh.ShelfAt.ContainsKey(ShelfKey(c.Parts[part].Pos)) || StationMarketsHere.GetValueOrDefault(c.Id) is not { } m)
            return;
        var def = StationMarkets.All[m.Def];
        var j = Array.FindIndex(def.Parts, x => x.Part == partDef);
        var name = ConstructParts.Def(partDef)?.Name ?? "part";
        var basket = _baskets.TryGetValue(player, out var b) ? b : _baskets[player] = [];
        if (put)
        {
            var at = basket.FindIndex(x => x.Station == c.Id && x.Part == partDef);
            if (at >= 0)
                basket.RemoveAt(at);
            if (j >= 0 && def.Goods.Length + j < m.Stock.Length)
                m.Stock[def.Goods.Length + j]++;
            StationsVersion++;
            SandboxHands.Notices.Add((player, at >= 0 ? $"Put the {name} back: off your basket ({StoreBasketText(basket, c.Id)})." : $"Left a {name} on {StationName(c)}'s shelf: it's theirs now."));
            return;
        }
        if (j < 0)
            return;
        var price = (float)(def.Parts[j].Sell * (sh.ReducedAt.Contains(ShelfKey(c.Parts[part].Pos)) ? ReducedShare : 1) / StandingRate(StationPower(m), player)); // (a friend pays less: StandingRate)
        basket.Add(new BasketLine(c.Id, partDef, price));
        if (def.Goods.Length + j < m.Stock.Length)
            m.Stock[def.Goods.Length + j] = Math.Max(0, m.Stock[def.Goods.Length + j] - 1);
        StationsVersion++;
        SandboxHands.Notices.Add((player, $"{name}: {MathF.Ceiling(price):0} at the trade arches. Basket: {StoreBasketText(basket, c.Id)}."));
        Log($"[store] player {player} took a {name} off a shelf at {StationName(c)} ({price:0.0}); basket {StoreBasketText(basket, c.Id)}");
    }

    private static string StoreBasketText(List<BasketLine> basket, ushort station)
    {
        int n = 0;
        float v = 0;
        foreach (var x in basket)
            if (x.Station == station)
                (n, v) = (n + 1, v + x.Price);
        return n == 0 ? "empty" : $"{n} thing{(n > 1 ? "s" : "")}, {MathF.Ceiling(v):0}";
    }

    /// <summary>Host, once a second: big pieces taken off the pick-up floor into baskets; shoppers through a trade arch pay; one who left without paying is settled.</summary>
    private void StoreShopTick()
    {
        foreach (var (stId, sh) in _shops)
        {
            if (Constructs.Get(stId) is not { Wreck: false } st || StationMarketsHere.GetValueOrDefault(stId) is not { } m || m.Def < 0)
                continue;
            var def = StationMarkets.All[m.Def];
            if (!StoreLayout.All.TryGetValue(def.Blueprint, out var lay) || !StoreFrame(st, lay, out var aPos, out var aRot))
                continue;
            StorePickupTaken(st, m, def, lay, aPos, aRot, sh);
        }
        foreach (var (pid, basket) in _baskets)
        {
            if (basket.Count == 0 || !Players.TryGetValue(pid, out var p))
                continue;
            var stId = basket[0].Station;
            if (Constructs.Get(stId) is not { } st || StationMarketsHere.GetValueOrDefault(stId) is not { } m || m.Def < 0
                || !StoreLayout.All.TryGetValue(StationMarkets.All[m.Def].Blueprint, out var lay) || !StoreFrame(st, lay, out var aPos, out var aRot))
            {
                basket.RemoveAll(x => x.Station == stId); // (the store is gone)
                continue;
            }
            var here = LayoutOf(aPos, aRot, lay, p.Sim.RideBody == stId && p.Sim.RideSeg == 0 ? p.Sim.RideLocal : st.ToLocal(p.Position)); // (riding the station: its own frame, the same tick)
            var was = _shopperAt.TryGetValue(pid, out var w) ? w : here;
            _shopperAt[pid] = here;
            var inside = here.X >= lay.StoreMin.X && here.X <= lay.StoreMax.X && here.Y >= lay.StoreMin.Y && here.Y <= lay.StoreMax.Y && here.Z >= lay.StoreMin.Z && here.Z <= lay.StoreMax.Z;
            if (!p.Alive)
            {
                StoreLeftBehind(p, st, m, lay, aPos, aRot, basket);
                StoreBasketLost(p, st, basket); // (what they carried is lost with them: no way out with it)
                continue;
            }
            if (inside)
                _shopperIn[pid] = here;
            var arch = StoreThroughArch(lay, was, here);
            if ((arch || !inside) && StoreLeftBehind(p, st, m, lay, aPos, aRot, basket) && basket.Count == 0)
                continue;
            if (arch)
                StoreSettle(p, st, m, basket, walkedOut: false);
            else if (!inside && !StoreSettle(p, st, m, basket, walkedOut: true))
                StoreTurnBack(p, st, lay, aPos, aRot, basket);
        }
    }

    /// <summary>Did the line from <paramref name="a"/> to <paramref name="b"/> (layout frame) pass through an arch's opening, going out (towards +z)?</summary>
    private static bool StoreThroughArch(StoreLayout lay, Vector3 a, Vector3 b)
    {
        foreach (var (mn, mx) in lay.Arches)
        {
            var mid = (mn.Z + mx.Z) / 2;
            if (a.Z >= mid || b.Z < mid)
                continue;
            var t = (mid - a.Z) / (b.Z - a.Z);
            var p = Vector3.Lerp(a, b, t);
            if (p.X >= mn.X && p.X <= mx.X && p.Y >= mn.Y - 0.5f && p.Y <= mx.Y)
                return true;
            if (b.X >= mn.X && b.X <= mx.X && b.Z <= mx.Z && b.Z >= mn.Z && b.Y >= mn.Y - 0.5f && b.Y <= mx.Y)
                return true;
        }
        return false;
    }

    /// <summary>Host: a shopper pays for what they took from store <paramref name="st"/>: off their tab, else in goods they carry, else from their ship's hold.</summary>
    private bool StoreSettle(Player p, Construct st, StationMarket m, List<BasketLine> basket, bool walkedOut)
    {
        var lines = 0;
        float sum = 0;
        foreach (var x in basket)
            if (x.Station == st.Id)
                (lines, sum) = (lines + 1, sum + x.Price);
        if (lines == 0)
            return true;
        var power = StationPower(m);
        var cost = (long)MathF.Ceiling(sum); // (each line was priced by standing as it went in the basket: StandingRate)
        var name = StationName(st);
        string how;
        string? cant;
        if (SandboxInfiniteParts || TabOf(p.Id, power) >= cost)
        {
            cant = TabPay(p.Id, power, cost);
            how = $"{cost} off your tab with the {TabPowerName(power)}";
        }
        else if (CarriedValue(p.Id) >= cost)
        {
            how = $"{cost} in the goods you carry ({CarriedTake(p.Id, cost)})";
            cant = null;
        }
        else
            cant = TabOrGoods(p.Id, power, cost, GoodsShipOf(p.Id, cost), out how);
        if (cant == null)
        {
            basket.RemoveAll(x => x.Station == st.Id);
            StationMargin(m, cost); // (the power's books, as a bought part's: GameStationMarket.cs)
            SandboxHands.Notices.Add((p.Id, $"{(walkedOut ? "You left" : "The arch beeps:")} {name}: {lines} thing{(lines > 1 ? "s" : "")} paid, {how}."));
            Log($"[store] {p.Name} paid {cost} for {lines} part(s) at {name}{(walkedOut ? " on leaving" : " at a trade arch")}: {how}");
            return true;
        }
        if (walkedOut)
        {
            // (no way out with unpaid parts, Karl 2026-11-21: StoreTurnBack puts them back inside)
            SandboxHands.Notices.Add((p.Id, $"The way out is shut: {lines} unpaid thing{(lines > 1 ? "s" : "")} ({cost}): {cant}. Pay at a trade arch, or put them back."));
            Log($"[store] {p.Name} tried to leave {name} with {lines} unpaid part(s) worth {cost}: {cant}");
            return false;
        }
        SandboxHands.Notices.Add((p.Id, $"The arch flashes red: {lines} thing{(lines > 1 ? "s" : "")} cost {cost}: {cant}. Put something back."));
        Log($"[store] {p.Name} couldn't pay {cost} for {lines} part(s) at a trade arch of {name}: {cant}");
        return false;
    }

    /// <summary>
    /// Host: a shopper who left the store with unpaid parts (on foot, flying or in a seat) stood back inside, where they last were in it, riding the
    /// station. One who was never seen inside (put out some other way) has the parts written off, with a log line.
    /// </summary>
    private void StoreTurnBack(Player p, Construct st, StoreLayout lay, Vector3 aPos, byte aRot, List<BasketLine> basket)
    {
        if (!_shopperIn.TryGetValue(p.Id, out var lastIn))
        {
            StoreBasketLost(p, st, basket);
            return;
        }
        ref var h = ref SandboxHands.Hand(p.Id);
        if (h.Seated)
        {
            var was = Constructs.Get(h.SeatBody);
            SandboxHands.Stand(Constructs, p.Id);
            if (was != null && was.Driver == p.Id)
                SandboxHands.StopDriving(was);
        }
        var at = StoreWorld(st, StoreLocal(aPos, aRot, lay, lastIn) + new Vector3(0, 0.03f, 0));
        p.Teleport(at);
        p.Sim.Velocity = st.MovePointVel(at);
        ConstructRide.End(ref p.Sim, null, st, 0, Vector3.Zero, frame: true); // (the station's own frame, as a person rides it)
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = p.Position, Yaw = p.Yaw }; // (lag compensation doesn't rewind them outside)
        _shopperAt[p.Id] = lastIn;
        Log($"[store] {p.Name} turned back into {StationName(st)} at {lastIn:0.0} (layout)");
    }

    /// <summary>Host: a shopper's unpaid lines at a store gone with them (they died, or were put out of the store some way other than walking).</summary>
    private void StoreBasketLost(Player p, Construct st, List<BasketLine> basket)
    {
        var n = basket.RemoveAll(x => x.Station == st.Id);
        _shopperIn.Remove(p.Id);
        if (n > 0)
            Log($"[store] {p.Name}'s {n} unpaid part(s) at {StationName(st)} were lost with them (death or put outside)");
    }

    /// <summary>What a player carries in materials, at the trade values.</summary>
    private double CarriedValue(byte player)
    {
        var inv = SandboxInventory(player);
        double v = 0;
        for (byte i = 1; i < global::CallOfSlop.SandboxInventory.MaxMaterials; i++)
            v += inv.Materials[i] * (double)TradeValues.Of(i);
        return v;
    }

    /// <summary>Server: at least <paramref name="value"/> in materials out of what a player carries (counted first: CarriedValue). Returns what went.</summary>
    private string CarriedTake(byte player, double value)
    {
        var inv = SandboxInventory(player);
        var what = new System.Text.StringBuilder();
        for (byte i = 1; i < global::CallOfSlop.SandboxInventory.MaxMaterials && value > 0; i++)
        {
            if (inv.Materials[i] == 0)
                continue;
            var unit = (double)TradeValues.Of(i);
            var took = inv.TakeMaterial(i, (int)Math.Min(inv.Materials[i], Math.Ceiling(value / unit)));
            value -= took * unit;
            if (took > 0)
                what.Append(what.Length > 0 ? ", " : "").Append(took).Append(' ').Append(RecyclingTable.Current.NameOf(i));
        }
        return what.ToString();
    }

    /// <summary>Host, once a second: a big piece on the pick-up floor that a shopper took hold of (or carried off) goes in their basket.</summary>
    private void StorePickupTaken(Construct st, StationMarket m, StationDef def, StoreLayout lay, Vector3 aPos, byte aRot, ShopState sh)
    {
        for (var s = 0; s < sh.PickBody.Length; s++)
        {
            if (sh.PickBody[s] == 0)
                continue;
            var body = Constructs.Get(sh.PickBody[s]);
            // taken: held by a physics gun, or gone (picked up, broken off, cleared) with a shopper near. A piece pushed about stays the store's.
            // (No "moved off its place" rule: a loose body and the station it stands in are stepped a tick apart, so in orbit their world
            // places differ by the station's speed over a tick.)
            byte who;
            if (body != null && body.GrabbedBy < 255 && Players.ContainsKey(body.GrabbedBy))
                who = body.GrabbedBy;
            else if (body == null)
                who = StoreNearestShopper(StoreWorld(st, StoreLocal(aPos, aRot, lay, lay.Pickup[s])), 12);
            else
                continue;
            var part = sh.PickPart[s];
            (sh.PickBody[s], sh.PickPart[s]) = (0, ConstructParts.None);
            var j = StorePartIndex(def, part);
            if (j < 0)
                continue;
            if (def.Goods.Length + j < m.Stock.Length)
                m.Stock[def.Goods.Length + j] = Math.Max(0, m.Stock[def.Goods.Length + j] - 1);
            StationsVersion++;
            if (who == 255)
            {
                Log($"[store] {def.Name}: a {ConstructParts.Def(part)?.Name} went from the pick-up with nobody near: written off");
                continue;
            }
            var basket = _baskets.TryGetValue(who, out var b) ? b : _baskets[who] = [];
            var price = (float)(def.Parts[j].Sell / StandingRate(StationPower(m), who)); // (a friend pays less: StandingRate)
            basket.Add(new BasketLine(st.Id, part, price, body?.Id ?? 0, s));
            SandboxHands.Notices.Add((who, $"{ConstructParts.Def(part)?.Name}: {MathF.Ceiling(price):0} at the trade arches. Basket: {StoreBasketText(basket, st.Id)}."));
        }
    }

    private static int StorePartIndex(StationDef def, ushort part)
    {
        for (var j = 0; j < def.Parts.Length; j++)
            if (def.Parts[j].Part == part)
                return j;
        return -1;
    }

    /// <summary>
    /// Is a loose body inside the store's box? Lenient by a tick of the station's travel either way (a loose body and the station it rides are
    /// stepped a tick apart, so in orbit their world places differ by the station's speed over a tick).
    /// </summary>
    private static bool StoreHolds(Construct st, StoreLayout lay, Vector3 aPos, byte aRot, Construct b)
    {
        var tick = st.Vel / World.TickRate;
        for (var k = -1; k <= 1; k++)
        {
            var q = LayoutOf(aPos, aRot, lay, st.ToLocal(b.Pos + tick * k));
            if (q.X >= lay.StoreMin.X && q.X <= lay.StoreMax.X && q.Y >= lay.StoreMin.Y && q.Y <= lay.StoreMax.Y && q.Z >= lay.StoreMin.Z && q.Z <= lay.StoreMax.Z)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Host, at an arch, on leaving or on a death: a pick-up piece in a shopper's basket that still lies in the store (moved with the physics gun
    /// and let go) is the store's again, off the basket and back in its stock. True when a line went.
    /// </summary>
    private bool StoreLeftBehind(Player p, Construct st, StationMarket m, StoreLayout lay, Vector3 aPos, byte aRot, List<BasketLine> basket)
    {
        if (StationMarkets.All[m.Def] is not { } def || !_shops.TryGetValue(st.Id, out var sh))
            return false;
        var any = false;
        for (var k = basket.Count - 1; k >= 0; k--)
        {
            var x = basket[k];
            if (x.Station != st.Id || x.Body == 0 || Constructs.Get(x.Body) is not { Wreck: false } b || b.GrabbedBy == p.Id && p.Alive || !StoreHolds(st, lay, aPos, aRot, b))
                continue;
            basket.RemoveAt(k);
            any = true;
            if (StorePartIndex(def, x.Part) is var j and >= 0 && def.Goods.Length + j < m.Stock.Length)
                m.Stock[def.Goods.Length + j]++;
            StationsVersion++;
            if (x.Slot >= 0 && x.Slot < sh.PickBody.Length && Constructs.Get(sh.PickBody[x.Slot]) == null)
                (sh.PickBody[x.Slot], sh.PickPart[x.Slot]) = (b.Id, x.Part); // (the store's again where it lies)
            else
                Constructs.Remove(b); // (its place has a new one: back to the stock room)
            SandboxHands.Notices.Add((p.Id, $"{ConstructParts.Def(x.Part)?.Name} left in the store: off your basket."));
            Log($"[store] {p.Name} left a {ConstructParts.Def(x.Part)?.Name} in {StationName(st)}: off the basket, back in stock");
        }
        return any;
    }

    /// <summary>
    /// Host, the first pass at a store in this run (a loaded world): the shelves, trolleys and pick-up pieces a saved world kept are taken away,
    /// to be set out again and tracked (the shop's own state isn't saved). Shelves only hold shown stock, so nothing is lost.
    /// </summary>
    private void StoreClearOld(Construct st, StationDef def, StoreLayout lay, Vector3 aPos, byte aRot)
    {
        var gondola = ConstructParts.Find(GondolaBayName);
        var rack = ConstructParts.Find(RackBayName);
        var trolley = ConstructParts.Find(TrolleyName);
        int shelves = 0, loose = 0;
        for (var i = st.PartCount - 1; i >= 0; i--)
            if (st.DefOf(i) is var d && d != null && (d == gondola || d == rack))
            {
                StoreEmptyShelf(st, i);
                Constructs.RemovePart(st, i);
                shelves++;
            }
        if (shelves > 0)
            st.Rebuild();
        for (var n = Constructs.Bodies.Count - 1; n >= 0; n--)
        {
            var b = Constructs.Bodies[n];
            if (b == st || b.Wreck || b.Owner != 255 || b.PartCount != 1 || b.DefOf(0) is not { } bd)
                continue;
            if (bd != trolley && StorePartIndex(def, (ushort)bd.Id) < 0 || !StoreHolds(st, lay, aPos, aRot, b))
                continue;
            Constructs.Remove(b);
            loose++;
        }
        if (shelves + loose > 0)
            Log($"[store] {def.Name}: {shelves} shelves and {loose} trolleys and pick-up pieces from the saved world taken away, to be set out again");
    }

    /// <summary>Host: a player who left the game: their basket, if any, goes with them (written off, logged), so their id's next holder starts clean.</summary>
    private void StoreShopForget(byte player)
    {
        if (_baskets.Remove(player, out var basket) && basket.Count > 0)
            Log($"[store] {NameOf(player)} left the game with {basket.Count} unpaid part(s) in a store basket: written off");
        _shopperAt.Remove(player);
        _shopperIn.Remove(player);
    }

    private byte StoreNearestShopper(Vector3 at, float within)
    {
        byte best = 255;
        var bestD = within * within;
        foreach (var (id, p) in Players)
            if (p.Alive && p.Bot == null && p.CrewSlot < 0 && Vector3.DistanceSquared(p.Position, at) is var d && d < bestD)
                (best, bestD) = (id, d);
        return best;
    }
}
