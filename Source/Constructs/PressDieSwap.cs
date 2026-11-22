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
    private static bool IsDieFor(ConstructPartDef def, PressDie die, ConstructPartDef press) =>
        def.IsPressDie && def.DieFamily == die.Family && press.PressDieSocket >= 0 && DieClassOf(def) == press.Sockets[press.PressDieSocket].Class;

    /// <summary>A slot of a store holding a die of the family <paramref name="die"/> for this press, or -1.</summary>
    private static int DieSlot(CStore st, PressDie die, ConstructPartDef press)
    {
        for (var k = 0; k < st.SlotCount; k++)
            if (st.Count[k] > 0 && ConstructParts.Get(st.Def[k]) is { } d && IsDieFor(d, die, press))
                return k;
        return -1;
    }

    /// <summary>
    /// The store a chute into this press takes from that holds a die of the family <paramref name="die"/> (the rack a swap would take it
    /// from): its body and part; false when none does. For order checks and look lines, not a tick.
    /// </summary>
    public static bool DieRackOf(ConstructWorld world, Construct c, int part, PressDie die, out Construct? rackBody, out int rack)
    {
        var d = c.DefOf(part);
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Chute && r.To == part && (uint)r.Source < (uint)c.PartCount && world.StoreOf(c, r.Source) is { } st && DieSlot(st, die, d) >= 0)
            {
                (rackBody, rack) = (c, r.Source);
                return true;
            }
        }
        for (var k = 0; k < world.WorldLinkCount; k++)
        {
            ref readonly var l = ref world.WorldLinks[k];
            if (l.Type == RouteType.Chute && l.BodyTo == c.Id && l.To == part && world.Get(l.BodyFrom) is { } from && l.From < from.PartCount
                && world.StoreOf(from, l.From) is { } st && DieSlot(st, die, d) >= 0)
            {
                (rackBody, rack) = (from, l.From);
                return true;
            }
        }
        (rackBody, rack) = (null, -1);
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
        if (slot < 0 || world.PressFeeds.Count >= world.PressFeeds.Capacity)
            return false;
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
    /// can't go where it should goes back to its store, else onto the press's tray; never lost silently.
    /// </summary>
    private static void DeliverSwap(ConstructWorld world, Construct to, in Feed f, ConstructPartDef die)
    {
        var from = world.Get(f.FromBody);
        if (from is { Dirty: true })
            from.Rebuild();
        int fromPart = from != null && f.FromPart < from.PartCount && world.StoreOf(from, f.FromPart) != null ? f.FromPart : -1;
        int press = f.ToPart;
        if (DieIn(to, press) is var old and >= 0)
        {
            var oldDef = to.Parts[old].D;
            if (ReferenceEquals(oldDef, die) || from == null || fromPart < 0 || FreeDieSlot(world, from, fromPart, oldDef) is not (>= 0 and var slot))
            {
                DieBack(world, from, fromPart, to, press, die);
                if (!ReferenceEquals(oldDef, die))
                    Console.WriteLine($"[press] the {to.Parts[press].D.Name} keeps its {oldDef.Name}: no room for it on the rack the {die.Name} came from");
                return;
            }
            world.SetIn(from, fromPart, slot, oldDef);
            var sameBody = ReferenceEquals(from, to);
            world.RemovePart(to, old);
            if (old < press)
                press--;
            if (sameBody && old < fromPart)
                fromPart--;
            if (to.Dirty)
                to.Rebuild();
            if (press >= to.PartCount || !to.Parts[press].D.IsPress)
            {
                DieBack(world, from, fromPart, null, -1, die); // (the press came off with it: a part that held them together went)
                return;
            }
        }
        if (!FitDie(world, to, press, die))
        {
            DieBack(world, from, fromPart, to, press, die);
            Console.WriteLine($"[press] the {die.Name} can't go into the {to.Parts[press].D.Name}'s die slot");
        }
    }

    private static void SwapBack(ConstructWorld world, in Feed f, ConstructPartDef die)
    {
        var from = world.Get(f.FromBody);
        if (from is { Dirty: true })
            from.Rebuild();
        DieBack(world, from, from != null && f.FromPart < from.PartCount && world.StoreOf(from, f.FromPart) != null ? f.FromPart : -1, null, -1, die);
    }

    private static int FreeDieSlot(ConstructWorld world, Construct c, int part, ConstructPartDef d)
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
