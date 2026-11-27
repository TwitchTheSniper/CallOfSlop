namespace CallOfSlop;

/// <summary>
/// A press swaps its die for its orders (the Zero Day works: four Presses, ten die families): when the head order of a press's queue
/// needs a die the press doesn't hold, a chute into the press from a store that holds that die (a Die Rack, a Block Rack) brings it, and
/// the old die goes back onto the rack the new one came from, as an Assembler Core's Tool Bay swaps tool blocks
/// (<see cref="AssemblerToolBay"/>). So a press with a chute from a rack of dies takes an order for any die on that rack
/// (<see cref="WorkOrders.Refusal"/>). The take happens at the chute's move, the swap at the start of the next step (a chute moves inside
/// the routes' own loop, where parts can't come and go: <see cref="Feed"/>). A swap starts only while no stroke is running: the head
/// order's die isn't fitted, so no bay is ready. Server only; nothing allocates.
/// </summary>
public static partial class ConstructPress
{
    /// <summary>The die the head order of a press's queue needs when the press doesn't hold it; null when it holds it, or has no order.</summary>
    public static PressDie? DieWanted(Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || c.Parts[part].D is not { IsPress: true, IsLathe: false } || c.Parts[part].Orders is not { Bill: false, Count: > 0 } q
            || Pressed.SpecOf(q.Head) is not { } spec)
            return null;
        return ReferenceEquals(SizedDieOf(PressTable.Current, c, part), spec.Die) ? null : spec.Die;
    }

    /// <summary>Is <paramref name="def"/> a die of the family <paramref name="die"/> that fits this press's die slot?</summary>
    internal static bool IsDieFor(ConstructPartDef def, PressDie die, ConstructPartDef press) =>
        def.IsPressDie && def.DieFamily == die.Family && press.PressDieSocket >= 0 && DieClassOf(def) == press.Sockets[press.PressDieSocket].Class;

    /// <summary>A slot of a store holding a die of the family <paramref name="die"/> for this press, or -1.</summary>
    internal static int DieSlot(CStore st, PressDie die, ConstructPartDef press)
    {
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Count[k] > 0 && ConstructParts.Get(st.Def[k]) is { } d && IsDieFor(d, die, press))
                return k;
        return -1;
    }

    /// <summary>
    /// The next press after cursor <paramref name="k"/> that a chute from store <paramref name="rack"/> of <paramref name="rackBody"/> feeds
    /// (its body's chutes first, then world links); false when there are no more. Start with k = 0.
    /// </summary>
    private static bool NextFed(ConstructWorld world, Construct rackBody, int rack, ref int k, out Construct? body, out int press)
    {
        for (; k < rackBody.RouteCount; k++)
        {
            ref readonly var r = ref rackBody.Routes[k];
            if (r.Type == RouteType.Chute && r.Source == rack && (uint)r.To < (uint)rackBody.PartCount && rackBody.Parts[r.To].D is { IsPress: true, IsLathe: false })
            {
                (body, press) = (rackBody, r.To);
                k++;
                return true;
            }
        }
        for (; k - rackBody.RouteCount < world.WorldLinkCount; k++)
        {
            ref readonly var l = ref world.WorldLinks[k - rackBody.RouteCount];
            if (l.Type == RouteType.Chute && l.BodyFrom == rackBody.Id && l.From == rack && world.Get(l.BodyTo) is { } to && l.To < to.PartCount && to.Parts[l.To].D is { IsPress: true, IsLathe: false })
            {
                (body, press) = (to, l.To);
                k++;
                return true;
            }
        }
        (body, press) = (null, -1);
        return false;
    }

    /// <summary>
    /// Another press a chute from the same store feeds (a rack shared by several presses) that holds a die of the family
    /// <paramref name="die"/> for <paramref name="press"/>'s slot: it gives the die back to the rack once its own head order doesn't need it
    /// (<see cref="ChuteSwap"/>). Its body and part; false when none does.
    /// </summary>
    private static bool SiblingHolding(ConstructWorld world, Construct rackBody, int rack, Construct c, int press, PressDie die, out Construct? holder, out int at)
    {
        (holder, at) = (null, -1);
        if (world.StoreOf(rackBody, rack) is not { SlotCount: > 0 })
            return false; // (a store with no slots never holds a die: a copper bin chuted into a Board and a Button press is no die rack between them, 2026-11-23)
        var d = c.DefOf(press);
        for (var k = 0; NextFed(world, rackBody, rack, ref k, out holder, out at);)
            if (!(ReferenceEquals(holder, c) && at == press) && DieIn(holder!, at) is var i and >= 0 && IsDieFor(holder!.Parts[i].D, die, d))
                return true;
        return false;
    }

    /// <summary>Does another press this store feeds want a die of <paramref name="def"/>'s family for its head order? (It is kept for that one.)</summary>
    private static bool SiblingWants(ConstructWorld world, Construct rackBody, int rack, Construct c, int press, ConstructPartDef def)
    {
        for (var k = 0; NextFed(world, rackBody, rack, ref k, out var other, out var at);)
            if (!(ReferenceEquals(other, c) && at == press) && DieWanted(other!, at) is { } want && want.Family == def.DieFamily)
                return true;
        return false;
    }

    /// <summary>
    /// The store a chute into this press takes from that holds a die of the family <paramref name="die"/> (the rack a swap would take it
    /// from), or feeds another press holding one that gives it back when done: its body and part; false when none does. For order checks, not a tick.
    /// </summary>
    public static bool DieRackOf(ConstructWorld world, Construct c, int part, PressDie die, out Construct? rackBody, out int rack)
    {
        var d = c.DefOf(part);
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Chute && r.To == part && (uint)r.Source < (uint)c.PartCount && world.StoreOf(c, r.Source) is { } st
                && (DieSlot(st, die, d) >= 0 || SiblingHolding(world, c, r.Source, c, part, die, out _, out _)))
            {
                (rackBody, rack) = (c, r.Source);
                return true;
            }
        }
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref readonly var l = ref world.WorldLinks[k];
            if (l.Type == RouteType.Chute && l.BodyTo == c.Id && l.To == part && world.Get(l.BodyFrom) is { } from && l.From < from.PartCount
                && world.StoreOf(from, l.From) is { } st && (DieSlot(st, die, d) >= 0 || SiblingHolding(world, from, l.From, c, part, die, out _, out _)))
            {
                (rackBody, rack) = (from, l.From);
                return true;
            }
        }
        return ConstructLifters.ArmDieRack(world, c, part, die, out rackBody, out rack); // (a Loader Arm reaching the press and a die rack: PressDieArm.cs)
    }

    /// <summary>Server: what the last die turned away says nothing now (a die changed in place by a Loader Arm, PressDieArm.cs).</summary>
    public static void ClearRefusal(ConstructWorld world, CStore st) => SetRefusal(world, st, 0, BayRefuse.None);

    /// <summary>
    /// Server: a press whose head order needs a die it doesn't hold presses first a later order its fitted die makes, whose blank is
    /// already in a bay (a swap costs two carries): that order moves to the head. True when one did.
    /// </summary>
    public static bool DieFirst(ConstructWorld world, Construct c, int part)
    {
        if (c.Parts[part].Orders is not { Bill: false, Count: > 1 } q || SizedDieOf(PressTable.Current, c, part) is not { } held || DieWanted(c, part) == null)
            return false;
        var bays = c.DefOf(part).PressBays.Length;
        for (var k = 1; k < q.Count; k++)
        {
            if (Pressed.SpecOf(q.Items[k]) is not { } spec || !ReferenceEquals(spec.Die, held))
                continue;
            var ready = false;
            for (var b = 0; b < bays && !ready; b++)
                ready = b != HopperBay && PieceIn(c, part, b) is var i and >= 0 && (c.Parts[i].D.ExKey with { Growing = false }) == spec.Blank;
            if (!ready)
                continue;
            var o = q.Items[k];
            Array.Copy(q.Items, 0, q.Items, 1, k);
            q.Items[0] = o;
            world.OrdersChanged(c);
            Console.WriteLine($"[press] the {c.DefOf(part).Name} presses its {o.Count} x {o.Name ?? ConstructParts.Def(o.Part).Name} on the {held.Family} Die it holds before it swaps");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Server: a blank at a press's door (the lead piece of a conveyor into it) that a bay turned away as "not its order", while no bay holds
    /// a blank: a later order of the queue that takes that blank goes to the head, so the press works what has reached it. Without this, a
    /// small press pair on Zero Day Works II jammed for good (Sean, 2026-11-27): the Housing press swapped to its Gear Die for a Drive Bell's
    /// Gear Sets, the Drive Core M blanks already made for it reached its door first, and the Gear Set blanks queued behind them on the same
    /// run. The order taken must be one the press can run: its die fitted, or on a rack or Die Wall it swaps from. True when one moved.
    /// </summary>
    public static bool DoorFirst(ConstructWorld world, Construct c, int part, ConstructPartDef blank)
    {
        if (!blank.IsExtrusion || c.Parts[part].Broken || c.Parts[part].Orders is not { Bill: false, Count: > 1 } q || world.StoreOf(c, part) is not { } st)
            return false;
        if (st.DoorAt != 0 && MoverPhysics.Clock - st.DoorAt < DoorEvery)
            return false; // (at most once a second a press: a blocked door is asked every tick, and two orders' blanks never trade the head back and forth)
        st.DoorAt = Math.Max(1u, MoverPhysics.Clock);
        var d = c.DefOf(part);
        for (var b = 0; b < d.PressBays.Length; b++)
            if (b != HopperBay && PieceIn(c, part, b) >= 0)
                return false; // (a blank in: the press works it first; the door's piece waits its turn)
        if (SizedDieOf(PressTable.Current, c, part) is not { } held)
            return false; // (no die fitted: a swap is under way for the head order, whose die must find it the head when it docks)
        var key = blank.ExKey with { Growing = false };
        if (Pressed.SpecOf(q.Head) is { } head && (key == head.Blank || HeadAtDoor(c, part, head.Blank)))
            return false; // (the head's own blank is at this door or another: refused for its size or room, or it goes in from there)
        for (var k = 1; k < q.Count; k++)
        {
            if (Pressed.SpecOf(q.Items[k]) is not { } spec || spec.Blank != key)
                continue;
            if (!ReferenceEquals(spec.Die, held) && !DieRackOf(world, c, part, spec.Die, out _, out _))
                continue; // (no die for it in reach: promoting it would stand the press still)
            var o = q.Items[k];
            Array.Copy(q.Items, 0, q.Items, 1, k);
            q.Items[0] = o;
            world.OrdersChanged(c);
            ClearRefusal(world, st);
            Console.WriteLine($"[press] the {d.Name} takes its {o.Count} x {o.Name ?? ConstructParts.Def(o.Part).Name} first: its blank is at the door, ahead of the head order's");
            return true;
        }
        return false;
    }

    /// <summary>Server ticks between two looks of <see cref="DoorFirst"/> at one press.</summary>
    private const uint DoorEvery = World.TickRate;

    /// <summary>Is a blank of key <paramref name="want"/> the lead piece on any conveyor into press <paramref name="part"/>? (Riders are kept grouped by link, the lead first.)</summary>
    private static bool HeadAtDoor(Construct c, int part, in ExtrusionKey want)
    {
        var prev = -1;
        for (var i = 0; i < c.ConveyedCount; i++)
        {
            int k = c.Conveyed[i].Route;
            if (k == prev)
                continue;
            prev = k;
            if (k < c.RouteCount && c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].To == part
                && ConstructParts.Def(c.Conveyed[i].Def) is { IsExtrusion: true } rd && (rd.ExKey with { Growing = false }) == want)
                return true;
        }
        return false;
    }

    /// <summary>
    /// A chute's move from a store into a press whose head order needs another die: that die, if the store holds one and the old die has a
    /// slot to go back to. True when one is on its way (it docks at the start of the next step).
    /// </summary>
    private static bool ChuteSwap(ConstructWorld world, Construct cf, int from, CStore src, Construct ct, int to)
    {
        if (DieWanted(ct, to) is not { } want || ct.Dirty)
            return false;
        foreach (var f in world.PressFeeds)
            if (f.ToBody == ct.Id && f.ToPart == to && f.Die != ConstructParts.None)
                return false; // (one is on its way already)
        var d = ct.DefOf(to);
        var slot = DieSlot(src, want, d);
        if (world.PressFeeds.Count >= world.PressFeeds.Capacity)
            return false;
        if (slot < 0)
        {
            // Not on the rack: another press this rack feeds may hold it. It gives it back once its own head order doesn't need it.
            if (SiblingHolding(world, cf, from, ct, to, want, out var holder, out var press) && !ReferenceEquals(DieOfHead(holder!, press), want)
                && FreeDieSlot(world, cf, from, holder!.Parts[DieIn(holder, press)].D) >= 0)
            {
                foreach (var f in world.PressFeeds)
                    if (f.Return && f.FromBody == holder.Id && f.FromPart == press)
                        return false;
                world.PressFeeds.Add(new Feed(holder.Id, (ushort)press, cf.Id, (ushort)from, (ushort)holder.Parts[DieIn(holder, press)].D.Id, Return: true));
            }
            return false; // (the next move takes it off the rack)
        }
        var old = DieIn(ct, to);
        if (old >= 0)
        {
            // The old die needs a slot on this store: the new one's, once emptied, or another that takes it.
            var oldDef = ct.Parts[old].D;
            var cd = cf.DefOf(from);
            var room = src.Count[slot] == 1 && ConstructStorage.Fits(cd.Slots[slot], oldDef, out _) && (!oldDef.PhysicsOnly || ConstructStorage.RackHolds(cd.Slots[slot], oldDef));
            for (var t = 0; t < src.SlotCount && !room; t++)
                room = ConstructStorage.Check(cd, src, t, oldDef, out _) == SlotCheck.Ok;
            if (!room)
                return false;
        }
        if (world.TakeOut(cf, from, slot) is not { } moved)
            return false;
        world.PressFeeds.Add(new Feed(cf.Id, (ushort)from, ct.Id, (ushort)to, (ushort)moved.Id, Swap: true));
        return true;
    }

    /// <summary>
    /// A swap's delivery: the old die (if any) goes back onto the store the new one came from, and the new one goes into the slot. What
    /// can't go where it should goes back to its store, else onto the press's tray; never lost silently. The part index it removed from
    /// the press's body (the old die), or -1.
    /// </summary>
    private static int DeliverSwap(ConstructWorld world, Construct to, in Feed f, ConstructPartDef die)
    {
        var from = world.Get(f.FromBody);
        if (from is { Dirty: true })
            from.Rebuild();
        int fromPart = from != null && f.FromPart < from.PartCount && world.StoreOf(from, f.FromPart) != null ? f.FromPart : -1;
        int press = f.ToPart, removed = -1;
        if (DieIn(to, press) is var old and >= 0)
        {
            var oldDef = to.Parts[old].D;
            if (ReferenceEquals(oldDef, die) || from == null || fromPart < 0 || FreeDieSlot(world, from, fromPart, oldDef) is not (>= 0 and var slot))
            {
                DieBack(world, from, fromPart, to, press, die);
                if (!ReferenceEquals(oldDef, die))
                    Console.WriteLine($"[press] the {to.Parts[press].D.Name} keeps its {oldDef.Name}: no room for it on the rack the {die.Name} came from");
                return -1;
            }
            world.SetIn(from, fromPart, slot, oldDef);
            var sameBody = ReferenceEquals(from, to);
            world.RemovePart(to, old);
            removed = old;
            if (old < press)
                press--;
            if (sameBody && old < fromPart)
                fromPart--;
            if (to.Dirty)
                to.Rebuild();
            if (press >= to.PartCount || !to.Parts[press].D.IsPress)
            {
                DieBack(world, from, fromPart, null, -1, die); // (the press came off with it: a part that held them together went)
                return removed;
            }
        }
        if (!FitDie(world, to, press, die))
        {
            DieBack(world, from, fromPart, to, press, die);
            Console.WriteLine($"[press] the {die.Name} can't go into the {to.Parts[press].D.Name}'s die slot");
        }
        return removed;
    }

    /// <summary>The die a press's head order needs, or null (no order).</summary>
    internal static PressDie? DieOfHead(Construct c, int part) =>
        c.Parts[part].Orders is { Bill: false, Count: > 0 } q && Pressed.SpecOf(q.Head) is { } spec ? spec.Die : null;

    /// <summary>
    /// A give-back's delivery: the die leaves press <see cref="Feed.FromPart"/> for the rack <see cref="Feed.ToPart"/>, unless the press's
    /// head order needs it now. The part index it removed from the press's body, or -1.
    /// </summary>
    private static int DeliverReturn(ConstructWorld world, in Feed f)
    {
        if (world.Get(f.FromBody) is not { } c || world.Get(f.ToBody) is not { } rb)
            return -1;
        if (c.Dirty)
            c.Rebuild();
        if (rb.Dirty)
            rb.Rebuild();
        if (f.FromPart >= c.PartCount || !c.Parts[f.FromPart].D.IsPress || DieIn(c, f.FromPart) is not (>= 0 and var die) || c.Parts[die].D.Id != f.Die
            || f.ToPart >= rb.PartCount || world.StoreOf(rb, f.ToPart) == null)
            return -1;
        var def = c.Parts[die].D;
        if (DieOfHead(c, f.FromPart) is { } head && head.Family == def.DieFamily)
            return -1; // (it got an order for this die meanwhile)
        if (FreeDieSlot(world, rb, f.ToPart, def) is not (>= 0 and var slot) || world.SetIn(rb, f.ToPart, slot, def) != null)
            return -1;
        world.RemovePart(c, die);
        return die;
    }

    private static void SwapBack(ConstructWorld world, in Feed f, ConstructPartDef die)
    {
        var from = world.Get(f.FromBody);
        if (from is { Dirty: true })
            from.Rebuild();
        DieBack(world, from, from != null && f.FromPart < from.PartCount && world.StoreOf(from, f.FromPart) != null ? f.FromPart : -1, null, -1, die);
    }

    /// <summary>A chute's plain die move into an empty slot (a press with no order): false for a die another press on the same rack waits for.</summary>
    public static bool FreeToTake(ConstructWorld world, Construct cf, int from, Construct ct, int to, ConstructPartDef def) => !SiblingWants(world, cf, from, ct, to, def);

    internal static int FreeDieSlot(ConstructWorld world, Construct c, int part, ConstructPartDef d)
    {
        if (world.StoreOf(c, part) is not { } st)
            return -1;
        var cd = c.DefOf(part);
        for (var t = 0; t < st.SlotCount; t++)
            if (ConstructStorage.Check(cd, st, t, d, out _) == SlotCheck.Ok)
                return t;
        return -1;
    }

    private static void DieBack(ConstructWorld world, Construct? from, int fromPart, Construct? c, int press, ConstructPartDef die)
    {
        if (from != null && fromPart >= 0 && FreeDieSlot(world, from, fromPart, die) is >= 0 and var slot && world.SetIn(from, fromPart, slot, die) == null)
            return;
        if (c != null && press >= 0 && world.StoreOf(c, press) is { } st)
        {
            world.TrayAdd(st, c.Parts[press].D, die);
            return;
        }
        Console.WriteLine($"[press] a {die.Name} on its way to a press was lost: neither its rack nor its press is there");
    }
}
