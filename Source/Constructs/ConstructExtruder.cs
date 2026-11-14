using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>Why an Extruder isn't running (a store's <see cref="CStore.Refused"/> byte: on the wire, so a client's look line says the same). Keep these under the material count's limit.</summary>
public enum ExtrudeStall : byte
{
    None,
    NoPower,
    NoMaterial,
    BadMaterial, // what it holds can't be extruded
    Mixed, // it holds another material than the piece on its die
    BadShape, // its Custom section can't be made
    AtCap, // the piece is as long as it can be
    Blocked, // the ground, a wall or something fixed is in the path
    TooHeavy, // something loose it can't push is in the path
    ChuteWaits, // the piece is done and waits for the chute to a press
    ConveyorSmall, // the piece is wider or taller than its conveyor is set to (ConstructConveyor.cs)
    ConveyorFull, // its conveyor has no room at its start: the pieces on it are backed up
    ConveyorCut, // its conveyor is cut: mend it
    NoOrders, // its queue of work orders is empty (WorkOrders.cs): it waits for the next
    OrderMaterial, // its bin holds another material than its head order asks for
    OffcutFull, // a Laser Cutter: its offcut bin is full, or holds another material (ConstructLaserCutter.cs)
    OrderTooLong, // its head order is longer than the machine makes now (a Run-out Table gone): add tables, or Cancel it (ConstructExtruderGrowth.cs)
}

/// <summary>
/// The Extruder and the Wheel Former (docs/sandbox/CONSTRUCTS.md, "Extrusion", "The Wheel Former"). An Extruder is a bin with
/// a die: while it runs, the piece on its die grows out of the die's face at the part's speed, using material by volume. The
/// piece is a part of the machine's own construct, linked to its die socket, so it rides with the machine, counts in its mass
/// and is solid, and everything about it is found from that link (nothing to keep in step when parts are renumbered, split or
/// sent). A step of growth (<see cref="Extrusion.StepCm"/>) swaps the part's def for the longer one and refreshes only the
/// body's shape (<see cref="Construct.RefreshShape"/>), a small delta on the wire. A rising signal into the machine's Cut
/// input, or an Extruder's Cut every length, parts the piece off at the die: it becomes a body of its own, and the next one
/// starts from the settings as they stand. A Wheel Former is the same machine with a hub for a die: its piece is a wheel,
/// which takes Width or Depth by its run mode. Server, at 10 Hz, looking only at constructs that have one; a shape once
/// made is kept, so a line in steady work allocates only the bodies it cuts off.
/// </summary>
public static partial class ConstructExtruder
{
    public const int Every = 6; // ticks between steps (10 Hz)
    private const int StepMm = Extrusion.StepCm * 10;
    private const int PushTries = 15; // steps of pushing something that doesn't move before it says so
    private const int Milli = 1000; // a store's JobAmount counts thousandths of a unit of material owed here
    public const byte CutPending = 2; // a machine's State: a rising signal into Cut waits for its next step (ConstructLogic)
    public const int CutPort = 1, ModePort = 2; // their places in DataIn (Run first)
    public const string CutName = "Cut", ModeName = "Mode";
    private static readonly List<Construct> _near = new(32);
    private static readonly byte AxleTurn = MakeAxleTurn(); // a wheel's axle (its x) along the machine's z

    private static byte MakeAxleTurn()
    {
        for (byte r = 0; r < Rot24.Count; r++)
            if (Rot24.ApplyDir(r, 0) == 4 && Rot24.ApplyDir(r, 2) == 2)
                return r;
        throw new InvalidOperationException("no turn takes x to z");
    }

    public static void Step(ConstructWorld world, IWorldQuery query)
    {
        if (++world.ExtrudeTicks < Every)
            return;
        world.ExtrudeTicks = 0;
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasExtruder)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsExtruder && world.StoreOf(c, i) is { } st && Work(world, query, c, i, st))
                    break; // (a cut took a part off: the indices moved, and the rest wait for the next step)
        }
    }

    /// <summary>The part on the machine's die, or -1.</summary>
    public static int ProductOf(Construct c, int part)
    {
        var sock = c.Parts[part].D.ExtDieSocket;
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            if (l.A == part && l.SA == sock && l.B < c.PartCount)
                return l.B;
            if (l.B == part && l.SB == sock && l.A < c.PartCount)
                return l.A;
        }
        return -1;
    }

    /// <summary>Is part <paramref name="i"/> on a machine's die (linked to its die socket)?</summary>
    public static bool OnDie(Construct c, int i)
    {
        for (var k = 0; k < c.LinkCount; k++)
        {
            ref var l = ref c.Links[k];
            if (l.A == i && l.B < c.PartCount && c.DefOf(l.B) is { IsExtruder: true } a && l.SB == a.ExtDieSocket)
                return true;
            if (l.B == i && l.A < c.PartCount && c.DefOf(l.A) is { IsExtruder: true } b && l.SA == b.ExtDieSocket)
                return true;
            if (l.A == i && l.B < c.PartCount && ConstructPress.IsBay(c.DefOf(l.B), l.SB) || l.B == i && l.A < c.PartCount && ConstructPress.IsBay(c.DefOf(l.A), l.SA))
                return true; // (a piece in a press's bay stays unfinished: ConstructPress.cs)
        }
        return false;
    }

    /// <summary>
    /// The machine's die in its own frame: its centre and size across the face. It starts as the part's own and reaches across
    /// every working Die Frame joined to it (through its links and other frames), up to the part's maximum.
    /// </summary>
    public static void Die(Construct c, int part, out Vector2 centre, out Vector2 size) => Reach(c, part, out centre, out size, out _);

    /// <summary>The section the machine's settings ask for, held inside its die (centimetres, on the settings' 5 cm step).</summary>
    public static void Section(Construct c, int part, out ulong mask, out int w, out int h, out Vector2 centre)
    {
        Section(c, part, out mask, out w, out h, out centre, out _, out _);
    }

    /// <summary>The same with its shape: a Round or a Pipe takes Width for its diameter, held inside the die both ways; a Pipe's wall leaves a bore.</summary>
    public static void Section(Construct c, int part, out ulong mask, out int w, out int h, out Vector2 centre, out ExShape shape, out ushort wall)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        Die(c, part, out centre, out var size);
        var wide = MathF.Min(ConstructConfig.Get(p, d, CfgKey.Width, 0.25f), size.X);
        var tall = MathF.Min(ConstructConfig.Get(p, d, CfgKey.Height, 0.25f), size.Y);
        w = Math.Clamp((int)MathF.Floor(wide * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        h = Math.Clamp((int)MathF.Floor(tall * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        mask = MaskOf(p, d);
        shape = Extrusion.ShapeOf((int)ConstructConfig.Get(p, d, CfgKey.Profile, 0));
        wall = 0;
        if (shape == ExShape.Section)
            return;
        mask = 0;
        var across = Math.Clamp((int)MathF.Floor(MathF.Min(MathF.Min(wide, size.X), size.Y) * 20 + 0.001f) * 5, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
        w = h = across;
        if (shape == ExShape.Pipe)
            wall = (ushort)Math.Clamp((int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Wall, 0.03f) * 100), 1, Math.Min(Extrusion.MaxWallCm, (across - Extrusion.MinBoreCm) / 2));
    }

    /// <summary>A round section's look on the grid (the panel and the die's preview).</summary>
    public static ulong RoundMaskOf(int profile, float across, float wall) =>
        Extrusion.RoundMask(profile == Extrusion.PipeProfile ? MathF.Max(0, 1 - 2 * wall / MathF.Max(0.1f, across)) : 0);

    /// <summary>The section a machine's Profile and cell settings give (a round one as it shows on the grid).</summary>
    public static ulong MaskOf(in CPart p, ConstructPartDef d)
    {
        var profile = (int)ConstructConfig.Get(p, d, CfgKey.Profile, 0);
        if (Extrusion.ShapeOf(profile) != ExShape.Section)
            return RoundMaskOf(profile, ConstructConfig.Get(p, d, CfgKey.Width, 0.25f), ConstructConfig.Get(p, d, CfgKey.Wall, 0.03f));
        if (profile < Extrusion.Presets.Length)
            return Extrusion.Presets[profile].Mask;
        if (profile >= Extrusion.AirfoilProfile(0) && profile - Extrusion.AirfoilProfile(0) < ExtrusionTable.Current.Airfoils.Length)
            return ExtrusionTable.Current.Airfoils[profile - Extrusion.AirfoilProfile(0)].Mask;
        return (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskA, 0) | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskB, 0) << 16
            | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskC, 0) << 32 | (ulong)(ushort)ConstructConfig.Get(p, d, CfgKey.MaskD, 0) << 48;
    }

    internal static void SetStall(ConstructWorld world, CStore st, ExtrudeStall s)
    {
        if (st.Refused == (byte)s)
            return;
        st.Refused = (byte)s;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>A step of one machine. True when a cut took a part off the construct.</summary>
    private static bool Work(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.IsLaserCutter)
        {
            ConstructLaserCutter.Work(world, c, part, st); // (a cut part, by order: ConstructLaserCutter.cs)
            return false;
        }
        if (d.IsPanelFormer)
        {
            ConstructPanelFormer.Work(world, c, part, st); // (a panel is made whole, by order: ConstructPanelFormer.cs)
            return false;
        }
        var product = ProductOf(c, part);
        if (product >= 0 && !c.Parts[product].D.IsExtrusion)
            product = -1; // (a bad link: nothing it can grow)
        var asked = (p.State & CutPending) != 0;
        p.State &= unchecked((byte)~CutPending); // (one that met no piece goes unused)
        var len = product >= 0 ? c.Parts[product].D.ExKey.L : 0;
        var whole = d.IsFanFormer || d.IsAirframeFormer; // (makes a piece to its settings)
        var depth = d.IsWheelFormer && (whole || ByDepth(p, d));
        // Work orders (WorkOrders.cs): a queue's head order replaces the settings; a piece on the die that isn't the head's is cut off as it is.
        var queue = p.Orders is { Bill: false } ql ? ql : null;
        var hasHead = WorkOrders.HeadOf(p, out var head);
        var headKey = default(ExtrusionKey);
        var keyed = hasHead && FormerPresets.KeyFor(head, out headKey); // (a piece's key, or a former's preset's: FormerPresets.cs)
        var forOrder = false;
        if (queue != null && product >= 0)
        {
            ref readonly var pk = ref c.Parts[product].D.ExKey;
            if (!keyed || !WorkOrders.ForHead(headKey, pk))
                asked = true;
            else if (whole)
            {
                forOrder = (d.IsAirframeFormer ? pk.L >= headKey.L : pk.H >= headKey.H); // (a prop parts off at its order's diameter, a wing at its span)
                asked |= forOrder;
            }
            else if (d.IsWheelFormer)
            {
                depth = pk.W >= head.Key.W;
                forOrder = pk.W >= head.Key.W && pk.H >= head.Key.H;
                asked |= forOrder;
            }
            else
                forOrder = pk.L >= head.Key.L;
        }
        var size = product < 0 ? 0 : d.IsAirframeFormer ? len : c.Parts[product].D.ExKey.H;
        p.Out = p.OutNext = product < 0 ? 0 : (d.IsWheelFormer ? depth ? size : c.Parts[product].D.ExKey.W : len) / 100f; // (its output: the length so far, a wheel's size in this mode)
        if (whole && queue == null && product >= 0 && size >= TargetCm(p, d))
            asked = true; // (a Fan or an Airframe Former parts a piece off when it reaches the size asked; with a queue, its order's size)
        if (product >= 0 && !p.Broken && (asked || CutDueCm(c, part, p, d) is var every and > 0 && len >= every) && ConstructRecycler.Powered(world, c, part))
        {
            if (forOrder && FormerPresets.PartOf(head) is { } made)
            {
                // A former's preset (FormerPresets.cs): the catalogue part it names comes off in the piece's place, paid as the piece was.
                if (CutAs(world, c, part, product, made, out var stall))
                {
                    WorkOrders.Made(world, c, queue);
                    ProductionLines.Made(world, c, part, 1);
                    return true;
                }
                p.State |= CutPending;
                ConstructGatherer.SetBusy(world, st, false);
                SetStall(world, st, stall);
                return false;
            }
            if (ConstructConveyor.Out(c, part) is var conveyor and >= 0)
            {
                // A conveyor out of its Piece out takes the piece whole (ConstructConveyor.cs): it waits on the die, the machine blocked, until it fits and there is room.
                if (ConstructConveyor.HandOff(world, c, conveyor, product, out var stall))
                {
                    if (forOrder)
                        WorkOrders.Made(world, c, queue);
                    ProductionLines.Made(world, c, part, 1); // (its console's Made count)
                    return true;
                }
                if (asked)
                    p.State |= CutPending;
                ConstructGatherer.SetBusy(world, st, false);
                SetStall(world, st, stall);
                return false;
            }
            if (!d.IsWheelFormer && ConstructPress.ChutesToPress(world, c, part))
            {
                // A chute into a press takes the piece whole (ConstructPress.Chute): it waits on the die until a bay will have it.
                p.State |= CutPending;
                ConstructGatherer.SetBusy(world, st, false);
                SetStall(world, st, ExtrudeStall.ChuteWaits);
                return false;
            }
            if (Cut(world, c, product) != null)
            {
                if (forOrder)
                    WorkOrders.Made(world, c, queue);
                ProductionLines.Made(world, c, part, 1);
                return true;
            }
        }
        var held = RunHeld(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.None);
            return false;
        }
        if (queue != null && !hasHead)
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.NoOrders);
            return false;
        }
        var why = whole ? GrowWhole(world, query, c, part, st, product, keyed, headKey) : d.IsWheelFormer ? GrowWheel(world, query, c, part, st, product, depth, hasHead, head) : Grow(world, query, c, part, st, product, len, hasHead, head);
        SetStall(world, st, why);
        ConstructGatherer.SetBusy(world, st, why == ExtrudeStall.None);
        return false;
    }

    /// <summary>An Extruder's Cut every, in cm (0: none).</summary>
    private static int CutLengthCm(in CPart p, ConstructPartDef d) => d.IsWheelFormer ? 0 : (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.CutLength, 0) * 100);

    /// <summary>Where a piece is cut by length, in cm (0: none): a queue's head order's length, else Cut every.</summary>
    private static int CutAtCm(in CPart p, ConstructPartDef d) =>
        p.Orders is { Bill: false } q ? (q.Count > 0 && !d.IsWheelFormer ? q.Head.Key.L : 0) : CutLengthCm(p, d);

    /// <summary>Where Cut every parts a piece now: past what the machine reaches (a Run-out Table shot off, or set for tables it hasn't got) it cuts at
    /// its cap; an order's length is never cut short (<see cref="ExtrudeStall.OrderTooLong"/>).</summary>
    private static int CutDueCm(Construct c, int part, in CPart p, ConstructPartDef d)
    {
        var every = CutAtCm(p, d);
        if (every <= 0 || p.Orders is { Bill: false } || d.IsWheelFormer || d.ExtGrowLength <= d.ExtMaxLength || every <= (int)MathF.Round(d.ExtMaxLength * 100))
            return every;
        return Math.Min(every, MaxLengthCm(c, part));
    }

    /// <summary>Does the Run wire hold the machine off? Its other inputs don't count.</summary>
    internal static bool RunHeld(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        return p.InCount > 0 ? p.In < 0.5f : c.RoutesInto(part, RouteType.Data, 255) > 0;
    }

    /// <summary>Is a Wheel Former building Depth (the diameter)? A wire into Mode decides, else its Run mode setting.</summary>
    public static bool ByDepth(in CPart p, ConstructPartDef d) => p.InCount2 > 0 ? p.In2 >= 0.5f : ConstructConfig.Get(p, d, CfgKey.RunMode, 0) >= 0.5f;

    /// <summary>A step's growth, or why not.</summary>
    private static ExtrudeStall Grow(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, int len, bool order, in WorkOrder head)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!ConstructRecycler.Powered(world, c, part))
            return ExtrudeStall.NoPower;
        ExtrusionKey key;
        Vector3 origin; // the piece's origin in the machine's segment frame
        byte rot;
        var fresh = product < 0;
        if (fresh)
        {
            if (st.Amount <= 0)
                return ExtrudeStall.NoMaterial;
            if (order && st.Material != head.Key.Material)
                return ExtrudeStall.OrderMaterial;
            if (order && head.Key.L > MaxLengthCm(c, part))
                return ExtrudeStall.OrderTooLong; // (longer than it makes now: its Run-out Tables gone since it was queued)
            if (ExtrusionTable.Current.Find(st.Material) == null)
                return ExtrudeStall.BadMaterial;
            Section(c, part, out var mask, out var w, out var h, out var centre, out var shape, out var wall);
            if (order)
                (mask, w, h, shape, wall) = (head.Key.Mask, head.Key.W, head.Key.H, head.Key.Shape, head.Key.Wall); // (the die's centre stays the settings')
            if (shape == ExShape.Section && Extrusion.Refusal(mask) != null)
                return ExtrudeStall.BadShape;
            key = new ExtrusionKey(mask, (ushort)w, (ushort)h, 0, st.Material, true, shape, wall);
            origin = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, new Vector3(centre.X, centre.Y, d.ExtDieAt.Z)));
            rot = p.Rot;
        }
        else
        {
            ref var pp = ref c.Parts[product];
            key = pp.D.ExKey;
            origin = pp.Pos;
            rot = pp.Rot;
            if (st.Amount > 0 && st.Material != key.Material)
                return ExtrudeStall.Mixed;
        }
        var limit = Math.Min(Extrusion.MaxLengthCm, (int)MathF.Round(d.ExtMaxLength * 100));
        if (d.ExtGrowLength > d.ExtMaxLength && len + 100 >= limit)
            limit = MaxLengthCm(c, part); // (its Run-out Tables: walked only near the plain cap, a step grows at most 16 cm)
        if (CutDueCm(c, part, p, d) is var every and >= Extrusion.StepCm && every < limit)
            limit = every; // (a piece stops at its length, whatever the speed)
        if (len >= limit)
            return order && head.Key.L > limit ? ExtrudeStall.OrderTooLong : ExtrudeStall.AtCap; // (a table gone under a longer order: say so, never cut it short)
        var row = ExtrusionTable.Current.Find(key.Material);
        if (row == null)
            return ExtrudeStall.BadMaterial;
        var areaM2 = Extrusion.AreaM2(key);
        st.JobTicks += (int)(d.ExtSpeed * Every / World.TickRate * 1000); // (millimetres grown and not yet a step)
        var steps = st.JobTicks / StepMm;
        if (steps == 0)
            return st.Amount > 0 ? ExtrudeStall.None : ExtrudeStall.NoMaterial;
        steps = Math.Min(steps, (limit - len) / Extrusion.StepCm);
        if (steps == 0)
            return ExtrudeStall.AtCap;
        // Material first: a step that can't be paid for waits.
        var stepCost = areaM2 * Extrusion.StepCm / 100f * row.UnitsPerM3 * Milli;
        var owed = (long)(stepCost * steps) + st.JobAmount;
        if (owed / Milli > st.Amount && steps > 1)
        {
            // What the bin can pay for now: a huge section's steps at speed can come to more than a bin holds.
            steps = Math.Max(1, (int)((st.Amount * (long)Milli - st.JobAmount) / MathF.Max(1, stepCost)));
            owed = (long)(stepCost * steps) + st.JobAmount;
        }
        var units = (int)(owed / Milli);
        if (units > st.Amount)
        {
            st.JobTicks = StepMm - 1;
            return ExtrudeStall.NoMaterial;
        }
        if (Extrusion.Def(key with { L = (ushort)(len + steps * Extrusion.StepCm) }) is not { } next)
            return ExtrudeStall.BadShape;
        if (fresh && c.PartCount >= Construct.MaxParts)
            return ExtrudeStall.Blocked;
        var path = PathClear(query, world, c, product, next, origin, rot, p.Seg, len, steps, d.ExtPush);
        if (path == Path.Blocked)
            (LastBlockBody, LastBlockPart) = (c.Id, part); // (the window tool's note is this machine's)
        else if (LastBlockBody == c.Id && LastBlockPart == part)
            (LastBlockBody, LastBlockPart, LastBlock) = (0, -1, ""); // (its way is clear again)
        switch (path)
        {
            case Path.Blocked:
                st.JobTicks = StepMm - 1;
                return ExtrudeStall.Blocked;
            case Path.Pushing:
                st.JobTicks = StepMm - 1;
                if (st.JobMaterial < PushTries)
                {
                    st.JobMaterial++;
                    return ExtrudeStall.None;
                }
                return ExtrudeStall.TooHeavy;
        }
        st.JobMaterial = 0;
        st.JobTicks -= steps * StepMm;
        st.JobAmount = (int)(owed - units * (long)Milli);
        if (units > 0)
            world.Withdraw(c, part, units);
        if (fresh)
        {
            var i = world.AddPart(c, next, origin, rot, p.Seg);
            if (i < 0)
                return ExtrudeStall.Blocked;
            world.AddLink(c, part, d.ExtDieSocket, i, 0);
            return ExtrudeStall.None;
        }
        Resize(world, c, product, next);
        return ExtrudeStall.None;
    }

    /// <summary>
    /// A step of a Wheel Former: the first wheel at its starting size, then wider or bigger by the steps this one has earned, with
    /// the material the added volume takes. Its origin stays on the hub's plate, so a size is only a def swap.
    /// </summary>
    private static ExtrudeStall GrowWheel(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, bool depth, bool order, in WorkOrder head)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!ConstructRecycler.Powered(world, c, part))
            return ExtrudeStall.NoPower;
        var fresh = product < 0;
        ExtrusionKey key, next;
        Vector3 origin;
        byte rot;
        if (fresh)
        {
            if (st.Amount <= 0)
                return ExtrudeStall.NoMaterial;
            if (order && st.Material != head.Key.Material)
                return ExtrudeStall.OrderMaterial;
            if (ExtrusionTable.Current.FindWheel(st.Material) == null)
                return ExtrudeStall.BadMaterial;
            key = default;
            next = new ExtrusionKey(0, (ushort)MathF.Round(d.ExtWheelStart.X * 100), (ushort)MathF.Round(d.ExtWheelStart.Y * 100), 0, st.Material, true, ExShape.Wheel);
            origin = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, d.ExtDieAt));
            rot = Rot24.Compose(p.Rot, AxleTurn);
        }
        else
        {
            ref var pp = ref c.Parts[product];
            key = next = pp.D.ExKey;
            origin = pp.Pos;
            rot = pp.Rot;
            if (st.Amount > 0 && st.Material != key.Material)
                return ExtrudeStall.Mixed;
        }
        var row = ExtrusionTable.Current.FindWheel(next.Material);
        if (row == null)
            return ExtrudeStall.BadMaterial;
        var have = depth ? key.H : key.W;
        var limit = Math.Min(depth ? Extrusion.MaxWheelCm : Extrusion.MaxWheelWidthCm, (int)MathF.Round((depth ? d.ExtWheelMax.Y : d.ExtWheelMax.X) * 100));
        if (order)
            limit = Math.Min(limit, depth ? head.Key.H : head.Key.W); // (an order's size: width first, then the diameter)
        if (!fresh && have >= limit)
            return ExtrudeStall.AtCap;
        st.JobTicks += (int)(d.ExtSpeed * Every / World.TickRate * 1000);
        var steps = st.JobTicks / StepMm;
        if (steps == 0)
            return st.Amount > 0 ? ExtrudeStall.None : ExtrudeStall.NoMaterial;
        if (!fresh)
        {
            steps = Math.Min(steps, (limit - have) / Extrusion.StepCm);
            if (steps == 0)
                return ExtrudeStall.AtCap;
            next = depth ? next with { H = (ushort)(have + steps * Extrusion.StepCm) } : next with { W = (ushort)(have + steps * Extrusion.StepCm) };
        }
        var owed = (long)((Extrusion.WheelVolume(next) - (fresh ? 0 : Extrusion.WheelVolume(key))) * row.UnitsPerM3 * Milli) + st.JobAmount;
        var units = (int)(owed / Milli);
        if (units > st.Amount)
        {
            st.JobTicks = StepMm - 1;
            return ExtrudeStall.NoMaterial;
        }
        if (Extrusion.Def(next) is not { } grown)
            return ExtrudeStall.BadShape;
        if (fresh && c.PartCount >= Construct.MaxParts)
            return ExtrudeStall.Blocked;
        switch (WheelClear(query, world, c, product, grown, origin, rot, p.Seg, d.ExtPush))
        {
            case Path.Blocked:
                st.JobTicks = StepMm - 1;
                return ExtrudeStall.Blocked;
            case Path.Pushing:
                st.JobTicks = StepMm - 1;
                if (st.JobMaterial < PushTries)
                {
                    st.JobMaterial++;
                    return ExtrudeStall.None;
                }
                return ExtrudeStall.TooHeavy;
        }
        st.JobMaterial = 0;
        st.JobTicks -= steps * StepMm;
        st.JobAmount = (int)(owed - units * (long)Milli);
        if (units > 0)
            world.Withdraw(c, part, units);
        if (fresh)
        {
            var i = world.AddPart(c, grown, origin, rot, p.Seg);
            if (i < 0)
                return ExtrudeStall.Blocked;
            world.AddLink(c, part, d.ExtDieSocket, i, 0);
            return ExtrudeStall.None;
        }
        Resize(world, c, product, grown);
        return ExtrudeStall.None;
    }

    /// <summary>The size a Fan or an Airframe Former's settings ask for (across, or the span), on the 5 cm step, inside the machine.</summary>
    private static int TargetCm(in CPart p, ConstructPartDef d)
    {
        var most = (int)MathF.Round(d.ExtWheelMax.Y * 100);
        var asked = (int)MathF.Round(ConstructConfig.Get(p, d, d.IsAirframeFormer ? CfgKey.WingSpan : CfgKey.FanDiameter, d.ExtWheelMax.Y) * 20) * Extrusion.StepCm;
        return Math.Clamp(asked, (int)MathF.Round(d.ExtWheelStart.Y * 100), most);
    }

    /// <summary>An Airframe Former's chord, in cm on the 5 cm step, inside the machine.</summary>
    private static int ChordCm(in CPart p, ConstructPartDef d)
    {
        var asked = (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.WingChord, d.ExtWheelStart.X) * 20) * Extrusion.StepCm;
        return Math.Clamp(asked, (int)MathF.Round(d.ExtWheelStart.X * 100), (int)MathF.Round(d.ExtWheelMax.X * 100));
    }

    /// <summary>
    /// A step of a Fan or an Airframe Former: the whole prop at its machine's first size, then bigger across with the material the added
    /// blade takes, until it reaches the size asked (<see cref="Work"/> parts it off then); a wing, the same along its span at the chord
    /// set. Its kind and blades or its chord are set when it starts.
    /// </summary>
    private static ExtrudeStall GrowWhole(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, bool order, in ExtrusionKey want)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!ConstructRecycler.Powered(world, c, part))
            return ExtrudeStall.NoPower;
        var fresh = product < 0;
        var air = d.IsAirframeFormer;
        ExtrusionKey key, next;
        Vector3 origin;
        byte rot;
        if (fresh)
        {
            if (st.Amount <= 0)
                return ExtrudeStall.NoMaterial;
            if (order && st.Material != want.Material)
                return ExtrudeStall.OrderMaterial;
            if ((air ? ExtrusionTable.Current.FindWing(st.Material) : ExtrusionTable.Current.FindFan(st.Material)) == null)
                return ExtrudeStall.BadMaterial;
            key = default;
            var start = (ushort)MathF.Round(d.ExtWheelStart.Y * 100);
            if (order)
                next = air ? want with { L = start, Growing = true } : want with { H = start, Growing = true }; // (its order's kind, chord or blades: Work parts it off at the order's size)
            else if (air)
                next = new ExtrusionKey(0, (ushort)ChordCm(p, d), 0, start, st.Material, true, ExShape.Wing, (ushort)ConstructConfig.Get(p, d, CfgKey.WingKind, 0));
            else
                next = new ExtrusionKey(0, 0, start, 0, st.Material, true, ExShape.Fan, Extrusion.FanWall((int)ConstructConfig.Get(p, d, CfgKey.FanKind, 0), (int)ConstructConfig.Get(p, d, CfgKey.BladeStyle, 0), (int)ConstructConfig.Get(p, d, CfgKey.FanBlades, 3)));
            origin = ConstructWorld.Quantise(p.Pos + Rot24.Apply(p.Rot, d.ExtDieAt));
            rot = p.Rot;
        }
        else
        {
            ref var pp = ref c.Parts[product];
            key = next = pp.D.ExKey;
            origin = pp.Pos;
            rot = pp.Rot;
            if (st.Amount > 0 && st.Material != key.Material)
                return ExtrudeStall.Mixed;
        }
        var row = air ? ExtrusionTable.Current.FindWing(next.Material) : ExtrusionTable.Current.FindFan(next.Material);
        if (row == null)
            return ExtrudeStall.BadMaterial;
        var limit = Math.Min(air ? Extrusion.MaxLengthCm : Extrusion.MaxFanCm, order ? (air ? want.L : want.H) : TargetCm(p, d));
        st.JobTicks += (int)(d.ExtSpeed * Every / World.TickRate * 1000);
        var steps = st.JobTicks / StepMm;
        if (steps == 0)
            return st.Amount > 0 ? ExtrudeStall.None : ExtrudeStall.NoMaterial;
        if (!fresh)
        {
            steps = Math.Min(steps, (limit - (air ? key.L : key.H)) / Extrusion.StepCm);
            if (steps <= 0)
                return ExtrudeStall.AtCap;
            next = air ? next with { L = (ushort)(key.L + steps * Extrusion.StepCm) } : next with { H = (ushort)(key.H + steps * Extrusion.StepCm) };
        }
        var volume = air ? Extrusion.WingVolume(next) - (fresh ? 0 : Extrusion.WingVolume(key)) : Extrusion.FanVolume(next) - (fresh ? 0 : Extrusion.FanVolume(key));
        var owed = (long)(volume * row.UnitsPerM3 * Milli) + st.JobAmount;
        var units = (int)(owed / Milli);
        if (units > st.Amount)
        {
            st.JobTicks = StepMm - 1;
            return ExtrudeStall.NoMaterial;
        }
        if (Extrusion.Def(next) is not { } grown)
            return ExtrudeStall.BadShape;
        if (fresh && c.PartCount >= Construct.MaxParts)
            return ExtrudeStall.Blocked;
        switch (WheelClear(query, world, c, product, grown, origin, rot, p.Seg, d.ExtPush))
        {
            case Path.Blocked:
                st.JobTicks = StepMm - 1;
                return ExtrudeStall.Blocked;
            case Path.Pushing:
                st.JobTicks = StepMm - 1;
                if (st.JobMaterial < PushTries)
                {
                    st.JobMaterial++;
                    return ExtrudeStall.None;
                }
                return ExtrudeStall.TooHeavy;
        }
        st.JobMaterial = 0;
        st.JobTicks -= steps * StepMm;
        st.JobAmount = (int)(owed - units * (long)Milli);
        if (units > 0)
            world.Withdraw(c, part, units);
        if (fresh)
        {
            var i = world.AddPart(c, grown, origin, rot, p.Seg);
            if (i < 0)
                return ExtrudeStall.Blocked;
            world.AddLink(c, part, d.ExtDieSocket, i, 0);
            return ExtrudeStall.None;
        }
        Resize(world, c, product, grown);
        return ExtrudeStall.None;
    }

    private enum Path : byte { Clear, Blocked, Pushing }

    /// <summary>Is the room a wheel's new size takes free? As <see cref="PathClear"/>, over its whole boxes: a free body is pushed out from the hub.</summary>
    private static Path WheelClear(IWorldQuery query, ConstructWorld world, Construct c, int product, ConstructPartDef next, Vector3 origin, byte rot, byte seg, float pushForce)
    {
        c.WorldFrame(seg, out var q, out var t);
        var turn = q * Rot24.Quat(rot);
        var hub = t + Vector3.Transform(origin, q);
        var result = Path.Clear;
        for (var b = 0; b < next.SolidBoxes; b++)
        {
            var box = next.Boxes[b];
            var centre = hub + Vector3.Transform(box.Center, turn);
            var half = Vector3.Max(box.Half - new Vector3(0.01f), new Vector3(0.004f));
            var obb = new Obb(centre, half, turn);
            if (query.OverlapBox(obb, QueryFilter.All))
            {
                LastBlock = "the map (ground or a wall)";
                return Path.Blocked;
            }
            var near = world.Near(centre, half.Length() + 0.5f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var other = near[k];
                if (other.Dirty)
                    other.Rebuild();
                if (!Hits(other, other == c ? product : -1, obb))
                    continue;
                if (other == c || other.Anchored || other.CarriedBy != 0)
                {
                    LastBlock = other == c ? "its own construct" : other.Anchored ? "an anchored construct" : "a carried construct";
                    return Path.Blocked;
                }
                var away = other.WorldCom - hub;
                Push(other, away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.UnitY, pushForce);
                result = Path.Pushing;
            }
        }
        return result;
    }

    /// <summary>
    /// Is the room the new length takes free? The ground and fixed things stop it; a free body in the way is pushed along the
    /// path (<paramref name="pushForce"/>, newtons) and growth waits for it to go, so nothing is ever grown into.
    /// </summary>
    /// <summary>For the window tool: what the last blocked step met (no allocation: fixed strings).</summary>
    public static string LastBlock = "";

    /// <summary>For the window tool: the machine <see cref="LastBlock"/> is about (body, part); cleared when its way is clear again.</summary>
    public static ushort LastBlockBody;
    public static int LastBlockPart = -1;

    private static Path PathClear(IWorldQuery query, ConstructWorld world, Construct c, int product, ConstructPartDef next, Vector3 origin, byte rot, byte seg, int len, int steps, float pushForce)
    {
        c.WorldFrame(seg, out var q, out var t);
        var turn = q * Rot24.Quat(rot);
        var axis = Vector3.Transform(Vector3.UnitZ, turn);
        var world0 = t + Vector3.Transform(origin, q);
        var z = len / 100f;
        var depth = steps * Extrusion.StepCm / 100f;
        var result = Path.Clear;
        for (var b = 0; b < next.SolidBoxes; b++)
        {
            var box = next.Boxes[b];
            var centre = world0 + Vector3.Transform(box.Center with { Z = z + depth * 0.5f }, turn);
            var half = Vector3.Max(box.Half with { Z = depth * 0.5f } - new Vector3(0.01f), new Vector3(0.004f));
            var obb = new Obb(centre, half, turn);
            if (query.OverlapBox(obb, QueryFilter.All))
            {
                LastBlock = "the map (ground or a wall)";
                return Path.Blocked;
            }
            var near = world.Near(centre, half.Length() + 0.5f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var other = near[k];
                if (other.Dirty)
                    other.Rebuild();
                if (!Hits(other, other == c ? product : -1, obb, own: other == c))
                    continue;
                if (other == c || other.Anchored || other.CarriedBy != 0)
                {
                    LastBlock = other == c ? "its own construct" : other.Anchored ? "an anchored construct" : "a carried construct";
                    return Path.Blocked;
                }
                Push(other, axis, pushForce);
                result = Path.Pushing;
            }
        }
        return result;
    }

    /// <summary>Is a finished piece waiting on this machine's die for a chute (a Cut asked for, or its Cut every length reached)?</summary>
    public static bool DueOff(Construct c, int part, int product)
    {
        ref var p = ref c.Parts[part];
        return (p.State & CutPending) != 0 || CutDueCm(c, part, p, p.D) is var every and > 0 && c.Parts[product].D.ExKey.L >= every;
    }

    internal static bool Hits(Construct b, int skip, in Obb obb, bool own = false)
    {
        var reach = obb.Half.Length();
        for (var i = 0; i < b.PartCount; i++)
        {
            if (i == skip || own && b.Parts[i].D is { ExtRunOut: > 0 } or { ExtGrowStep: > 0 } or { IsDieFrame: true }) // (an Extruder's own expansion pieces never block its piece)
                continue;
            b.WorldFrame(b.Parts[i].Seg, out var q, out var t);
            b.PartBounds(i, out var mn, out var mx);
            if (Vector3.Distance(t + Vector3.Transform((mn + mx) * 0.5f, q), obb.Center) > reach + (mx - mn).Length() * 0.5f + 0.1f)
                continue;
            var boxes = b.SolidCount(i);
            for (var k = 0; k < boxes; k++)
            {
                b.SolidBox(i, k, out var oc, out var oh);
                if (Geometry.ObbOverlap(obb, new Obb(t + Vector3.Transform(oc, q), oh, q)))
                    return true;
            }
        }
        return false;
    }

    private static void Push(Construct b, Vector3 axis, float force)
    {
        var dv = force * (Every / (float)World.TickRate) / MathF.Max(1, b.Mass);
        var along = Vector3.Dot(b.Vel, axis);
        const float most = 0.6f; // m/s: a shove, not a launch
        if (along >= most)
            return;
        b.Vel += axis * MathF.Min(dv, most - along);
        b.Wake();
    }

    /// <summary>The piece takes a new length in place: its def, its health by how much it grew, and the body's shape without a full rebuild.</summary>
    internal static void Resize(ConstructWorld world, Construct c, int product, ConstructPartDef next)
    {
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        pp.Def = next.Id;
        pp.Health = MathF.Max(1, pp.Health + next.Health - was.Health);
        c.Wake();
        world.ShapeChanged(c);
        world.ExtrudeDirty.Add((c.Id, (ushort)product));
    }

    /// <summary>The piece comes off at the die as a body of its own, moving as the machine moved, with its health and paint. The part goes from the machine's construct.</summary>
    internal static Construct? Cut(ConstructWorld world, Construct c, int product)
    {
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        if (Extrusion.Def(was.ExKey with { Growing = false }) is not { } piece)
            return null;
        c.WorldFrame(pp.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(pp.Rot);
        var centre = t + Vector3.Transform(pp.Pos, q) + Vector3.Transform((piece.BoundsMin + piece.BoundsMax) * 0.5f, turn);
        var share = pp.Health / MathF.Max(1, was.Health);
        var paint = pp.Paint;
        if (ConstructEdits.DropLoose(world, piece, centre, turn) is not { } body)
            return null;
        body.Owner = c.Owner;
        body.Vel = c.PointVel(centre);
        body.AngVel = c.AngVel;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, piece.Health * share);
        np.Paint = paint;
        world.RemovePart(c, product);
        c.Wake();
        return body;
    }

    /// <summary>
    /// A former's preset (FormerPresets.cs): the finished piece goes and the catalogue part <paramref name="made"/> comes off in its place, with
    /// the piece's share of health and its paint. With a Piece out conveyor it rides that as a made part (ConstructConveyor.PutMade), else it
    /// drops at the die as a body of its own, laid where the piece lay (a wing's span along the piece's, its leading edge the piece's).
    /// True when it went (the piece left the construct: indices moved); else <paramref name="why"/> says why it waits on the die.
    /// </summary>
    internal static bool CutAs(ConstructWorld world, Construct c, int machine, int product, ConstructPartDef made, out ExtrudeStall why)
    {
        why = ExtrudeStall.None;
        ref var pp = ref c.Parts[product];
        var was = pp.D;
        var share = pp.Health / MathF.Max(1, was.Health);
        var paint = pp.Paint;
        if (ConstructConveyor.Out(c, machine) is var conveyor and >= 0)
        {
            if (!ConstructConveyor.PutMade(world, c, conveyor, made, 1, out why))
                return false;
            world.RemovePart(c, product);
            c.Wake();
            return true;
        }
        if (Extrusion.Def(was.ExKey with { Growing = false }) is not { } piece)
        {
            why = ExtrudeStall.BadShape;
            return false;
        }
        c.WorldFrame(pp.Seg, out var q, out var t);
        var turn = q * Rot24.Quat(pp.Rot);
        var centre = t + Vector3.Transform(pp.Pos, q) + Vector3.Transform((piece.BoundsMin + piece.BoundsMax) * 0.5f, turn);
        if (made.Surface != null && was.ExKey.Shape == ExShape.Wing)
            turn *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2); // (a catalogue wing's span is its x, its leading edge -z: the piece's are z and -x)
        if (ConstructEdits.DropLoose(world, made, centre, turn) is not { } body)
        {
            why = ExtrudeStall.Blocked;
            return false;
        }
        body.Owner = c.Owner;
        body.Vel = c.PointVel(centre);
        body.AngVel = c.AngVel;
        ref var np = ref body.Parts[0];
        np.Health = MathF.Max(1, made.Health * share);
        np.Paint = paint;
        world.RemovePart(c, product);
        c.Wake();
        return true;
    }

    /// <summary>Why nothing can be welded to this part just now (a piece still on its die), or null.</summary>
    public static string? GrowingRefusal(Construct c, int part) =>
        (uint)part < (uint)c.PartCount && c.DefOf(part) is { IsExtrusion: true, ExKey.Growing: true } ? "that piece is still on its machine's die: cut it off first" : null;

    /// <summary>The length of the piece on the die, in cm.</summary>
    public static int LengthOf(Construct c, int part) => ProductOf(c, part) is var k and >= 0 && c.Parts[k].D.IsExtrusion ? c.Parts[k].D.ExKey.L : 0;

    /// <summary>The look line's key for the piece's size: changes when it grows (the store's version doesn't).</summary>
    public static int SizeKey(Construct c, int part) => ProductOf(c, part) is var k and >= 0 && c.Parts[k].D is { IsExtrusion: true } d ? d.ExKey.L + (d.ExKey.W * 257 ^ d.ExKey.H * 65537) : 0;

    /// <summary>": 0.10 m, piece needs 0.30 m" (a conveyor has one size, its width: ConstructConveyor.Needs).</summary>
    private static string ConveyorSize(Construct c, int part)
    {
        if (ConstructConveyor.Out(c, part) is not (var k and >= 0))
            return "";
        var piece = ProductOf(c, part) is var p and >= 0 ? c.Parts[p].D : null;
        var needs = 0f;
        if (piece != null)
        {
            ConstructConveyor.Size(piece, out var across, out var up, out _);
            needs = ConstructConveyor.Needs(across, up);
        }
        return $": {c.Routes[k].ConvW / 100f:0.00} m{(needs > 0 ? $", piece needs {needs:0.00} m" : "")}";
    }

    /// <summary>Why a piece can't be made (<see cref="ExtrudeStall.BadShape"/>): the head order's key as <see cref="Extrusion.KeyRefusal"/>
    /// reads it (an order saved or queued before the order check refused such keys), else the die's own section; null when neither says.</summary>
    private static string? StallShape(Construct c, int part, ConstructPartDef d, bool roundOrWheel)
    {
        if (WorkOrders.HeadOf(c.Parts[part], out var head) && head.IsPiece && Extrusion.KeyRefusal(head.Key) is { } bad)
            return bad;
        return roundOrWheel ? null : Extrusion.Refusal(MaskOf(c.Parts[part], d));
    }

    /// <summary>What it is doing, or why not. Read by the look line on either side.</summary>
    public static string Status(Construct c, int part, CStore st)
    {
        if (c.Parts[part].Broken)
            return " - wrecked";
        if (st.Held)
            return Program.HeldLook;
        var d = c.Parts[part].D;
        if (d.IsLaserCutter)
            return ConstructLaserCutter.Status(c, part, st);
        if (d.IsPanelFormer)
            return (ExtrudeStall)st.Refused switch
            {
                ExtrudeStall.NoPower => " - no power: needs a charged battery",
                ExtrudeStall.NoMaterial => " - " + ConstructPanelFormer.Lacks(c, part, st),
                ExtrudeStall.OrderMaterial => $" - needs {(c.Parts[part].Orders is { Count: > 0 } pq ? RecyclingTable.Current.NameOf(pq.Head.Key.Material) : "another material")} for its order, holds {RecyclingTable.Current.NameOf(st.Material)}",
                ExtrudeStall.BadShape => " - not a panel: nothing makes its order (Cancel it)",
                ExtrudeStall.Blocked => " - blocked: no room on its out bed",
                ExtrudeStall.ConveyorSmall => $" - conveyor too narrow for the panel - aim the linking tool at it: wheel or {SandboxKeys.Reload} to widen",
                ExtrudeStall.ConveyorFull => " - blocked: its conveyor is full",
                ExtrudeStall.ConveyorCut => " - blocked: its conveyor is cut: mend it",
                ExtrudeStall.NoOrders => ", waiting for orders (Queue on its screen, or a Line Console's bill)",
                _ => st.Busy ? ", forming" : ", waiting",
            };
        var wheel = d.IsWheelFormer;
        var takes = d.IsAirframeFormer ? ExtrusionTable.Current.TakesWings() : d.IsFanFormer ? ExtrusionTable.Current.TakesFans() : wheel ? ExtrusionTable.Current.TakesWheels() : ExtrusionTable.Current.Takes();
        var round = Extrusion.ShapeOf((int)ConstructConfig.Get(c.Parts[part], d, CfgKey.Profile, 0)) != ExShape.Section;
        return (ExtrudeStall)st.Refused switch
        {
            ExtrudeStall.NoPower => " - no power: needs a charged battery",
            ExtrudeStall.NoMaterial => $" - needs material: takes {takes}",
            ExtrudeStall.BadMaterial => $" - can't {(wheel ? "form" : "extrude")} {RecyclingTable.Current.NameOf(st.Material)}: takes {takes}",
            ExtrudeStall.Mixed => $" - holds {RecyclingTable.Current.NameOf(st.Material)}, the piece on its {(wheel ? "hub" : "die")} is another material: cut it or take it off",
            ExtrudeStall.BadShape => " - " + (StallShape(c, part, d, wheel || round) ?? "too many different pieces in this game"),
            ExtrudeStall.AtCap => wheel ? " - the wheel is as big as this mode goes: change mode or send Cut" : " - the piece is as long as it can be: cut it or take it off",
            ExtrudeStall.Blocked => wheel ? " - blocked: the wheel has no room to grow" : " - blocked: something fixed in the piece's path (a wall, the ground)",
            ExtrudeStall.TooHeavy => " - what is in the piece's path is too heavy to push",
            ExtrudeStall.ChuteWaits => " - the piece waits for its chute: the press's bay is full or won't take it",
            ExtrudeStall.ConveyorSmall => $" - conveyor too narrow{ConveyorSize(c, part)} - aim the linking tool at it: wheel or {SandboxKeys.Reload} to widen",
            ExtrudeStall.ConveyorFull => " - blocked: its conveyor is full",
            ExtrudeStall.ConveyorCut => " - blocked: its conveyor is cut: mend it",
            ExtrudeStall.NoOrders => ", waiting for orders (Clear on its screen: back to its own settings)",
            ExtrudeStall.OrderMaterial => $" - needs {(c.Parts[part].Orders is { Count: > 0 } q ? RecyclingTable.Current.NameOf(q.Head.Key.Material) : "another material")} for its order, holds {RecyclingTable.Current.NameOf(st.Material)}",
            ExtrudeStall.OrderTooLong => c.Parts[part].Orders is { Count: > 0 } lq ? $" - its order is {lq.Head.Key.L / 100f:0.##} m long, it makes {MaxLengthCm(c, part) / 100f:0.##} m now: {LongFix(d, lq.Head.Key.L, lq.Head.Key.W, lq.Head.Key.H)}, or Cancel it" : " - its order is longer than it makes now",
            _ => st.Busy ? (wheel ? ", forming" : ", extruding") : ", waiting",
        };
    }

    /// <summary>The whole look line: its bin, its die, the section, the length so far, the material use, what it is doing, what F would do.</summary>
    public static string Look(Construct c, int part, CStore st, bool near, SandboxInventory inv)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var names = RecyclingTable.Current;
        var product = ProductOf(c, part);
        var sb = new StringBuilder(d.Name).Append(": ").Append(st.Amount).Append('/').Append(d.BulkCapacity);
        if (st.Amount > 0)
            sb.Append(' ').Append(names.NameOf(st.Material));
        if (d.IsPanelFormer)
        {
            if (d.IsLaserCutter)
                ConstructLaserCutter.Look(c, part, st, sb);
            else if (WorkOrders.HeadOf(p, out var ph) && ph.Name != null)
                sb.Append(", ").Append(ph.Name);
            sb.Append(Status(c, part, st));
        }
        else if (d.IsWheelFormer)
        {
            if (d.IsFanFormer)
                sb.Append($", to {TargetCm(p, d) / 100f:0.##} m");
            else if (d.IsAirframeFormer)
                sb.Append($", {Extrusion.AirframeOf((ushort)ConstructConfig.Get(p, d, CfgKey.WingKind, 0))?.Label}: {ChordCm(p, d) / 100f:0.##} by {TargetCm(p, d) / 100f:0.##} m");
            else
                sb.Append(ByDepth(p, d) ? ", Depth mode" : ", Width mode");
            if (product >= 0 && c.Parts[product].D is { IsExtrusion: true } on)
                sb.Append(", ").Append(Extrusion.Label(on.ExKey));
            sb.Append(Status(c, part, st));
        }
        else
        {
            Section(c, part, out var mask, out var w, out var h, out _, out var shape, out var wall);
            Die(c, part, out _, out var die);
            if (product >= 0 && c.Parts[product].D.IsExtrusion)
            {
                var k = c.Parts[product].D.ExKey;
                sb.Append($", {Extrusion.Label(k)}, {k.L / 100f:0.##}/{MaxLength(c, part):0.#} m");
            }
            else
                sb.Append(", ").Append(Extrusion.Label(new ExtrusionKey(mask, (ushort)w, (ushort)h, 0, 0, true, shape, wall)));
            sb.Append(Status(c, part, st));
            if (ConstructConfig.Get(p, d, CfgKey.Width, 0) > die.X + 0.001f || ConstructConfig.Get(p, d, CfgKey.Height, 0) > die.Y + 0.001f)
                sb.Append($" - held to the {die.X:0.##} x {die.Y:0.##} m die{(die.X < MostSection(d).X - 0.001f || die.Y < MostSection(d).Y - 0.001f ? ": " + WideFix(d, (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Width, 0) * 100), (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Height, 0) * 100)) : "")}");
        }
        if (near)
        {
            var tip = ConstructEdits.TipMaterial(inv, d, st);
            if (tip != 0)
                sb.Append($" - {SandboxKeys.Use}: tip in {Math.Min(inv.Materials[tip], d.BulkCapacity - st.Amount)} {names.NameOf(tip)}");
            else if (st.Amount > 0)
                sb.Append($" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}");
            if (ExtruderPicks.Pickable(d) && ConstructEdits.HasRecipeButton(d))
                sb.Append(" - Recipe on its screen: section and size");
        }
        return sb.ToString();
    }
}

/// <summary>The Extruder's side of the world (ConstructExtruder.cs).</summary>
public sealed partial class ConstructWorld
{
    public int ExtrudeTicks; // server: ticks towards the next Extruder step

    /// <summary>Pieces whose length changed since the last network step: (body, part).</summary>
    public readonly List<(ushort Body, ushort Part)> ExtrudeDirty = new(32);
    public readonly List<(ushort Body, ushort Part)> ReadingDirty = new(64); // (displays and keypads that changed: LogicDisplays.cs)
    public readonly List<(ushort Body, ushort Part)> AnalogDirty = new(32); // (Analog Levers that moved: AnalogLever.cs)

    /// <summary>A part's def changed size in place: the body's shape, without a rebuild, and the broadphase's sphere.</summary>
    public void ShapeChanged(Construct c)
    {
        c.RefreshShape();
        GridGrew(c);
    }
}

/// <summary>A part file's <c>storage.extrude</c> block (an Extruder).</summary>
public static class ConstructExtruderParts
{
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        const System.Text.Json.JsonValueKind Obj = System.Text.Json.JsonValueKind.Object;
        if (b.ValueKind != Obj || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner)
            throw new ConstructPartException($"{file}: storage \"extrude\" is an object (speed, die, at, maxSection, maxLength, pushForce) on a bin: what feeds it");
        ConstructParts.Known(b, ["speed", "die", "at", "maxSection", "maxLength", "pushForce", "growSection", "growLength"], $"{bw}.extrude");
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: an Extruder's bin takes \"{ConstructStorage.Any}\" material (Constructs/Extrusion.json says which it can use)");
        d.ExtSpeed = ConstructParts.Num(ConstructParts.Need(b, "speed", $"{bw}.extrude"), file, "storage.extrude.speed");
        ConstructParts.Range(d.ExtSpeed, 0.02f, 4, file, "storage.extrude.speed");
        d.ExtDie = Pair(ConstructParts.Need(b, "die", $"{bw}.extrude"), file, "storage.extrude.die");
        d.ExtDieAt = ConstructParts.Vec(ConstructParts.Need(b, "at", $"{bw}.extrude"), file, "storage.extrude.at");
        d.ExtMaxSection = Pair(ConstructParts.Need(b, "maxSection", $"{bw}.extrude"), file, "storage.extrude.maxSection");
        d.ExtMaxLength = ConstructParts.Num(ConstructParts.Need(b, "maxLength", $"{bw}.extrude"), file, "storage.extrude.maxLength");
        ConstructParts.Range(d.ExtMaxLength, 0.5f, Extrusion.MaxLengthCm / 100f, file, "storage.extrude.maxLength");
        d.ExtPush = ConstructParts.Num(ConstructParts.Need(b, "pushForce", $"{bw}.extrude"), file, "storage.extrude.pushForce");
        ConstructParts.Range(d.ExtPush, 0, 1000000, file, "storage.extrude.pushForce");
        if (d.ExtDie.X > d.ExtMaxSection.X || d.ExtDie.Y > d.ExtMaxSection.Y || d.ExtMaxSection.X * 100 > Extrusion.MaxSectionCm || d.ExtMaxSection.Y * 100 > Extrusion.MaxSectionCm)
            throw new ConstructPartException($"{file}: storage.extrude: \"die\" fits inside \"maxSection\", at most {Extrusion.MaxSectionCm / 100} m a side");
        d.ExtGrowSection = ConstructParts.Opt(b, "growSection") is { } gs ? Pair(gs, file, "storage.extrude.growSection") : d.ExtMaxSection;
        if (d.ExtGrowSection.X < d.ExtMaxSection.X || d.ExtGrowSection.Y < d.ExtMaxSection.Y || d.ExtGrowSection != d.ExtMaxSection && d.ExtDie != d.ExtMaxSection)
            throw new ConstructPartException($"{file}: storage.extrude: \"growSection\" (Die Extensions) is at least \"maxSection\", on a machine that is its die (no Die Frames)");
        d.ExtGrowLength = d.ExtMaxLength;
        if (ConstructParts.Opt(b, "growLength") is { } gl)
        {
            d.ExtGrowLength = ConstructParts.Num(gl, file, "storage.extrude.growLength");
            ConstructParts.Range(d.ExtGrowLength, d.ExtMaxLength, Extrusion.MaxLengthCm / 100f, file, "storage.extrude.growLength");
        }
        Socket(d, file);
        d.IsExtruder = true;
        d.DataIn = [ConstructGatherer.RunPort, ConstructExtruder.CutName];
    }

    /// <summary>A part file's <c>storage.wheel</c> block (a Wheel Former): an Extruder whose die is a hub.</summary>
    public static void ParseWheel(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        const System.Text.Json.JsonValueKind Obj = System.Text.Json.JsonValueKind.Object;
        if (b.ValueKind != Obj || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"wheel\" is an object (speed, at, maxWidth, maxDiameter, startWidth, startDiameter, pushForce) on a bin: what feeds it");
        ConstructParts.Known(b, ["speed", "at", "maxWidth", "maxDiameter", "startWidth", "startDiameter", "pushForce", "fan", "airframe"], $"{bw}.wheel");
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: a Wheel Former's bin takes \"{ConstructStorage.Any}\" material (Constructs/Extrusion.json says which it can use)");
        var fan = ConstructParts.Opt(b, "fan") is { ValueKind: System.Text.Json.JsonValueKind.True };
        var air = ConstructParts.Opt(b, "airframe") is { ValueKind: System.Text.Json.JsonValueKind.True }; // (its widest is a chord and its first and biggest across a span)
        float Size(string key, float min, float max)
        {
            var v = ConstructParts.Num(ConstructParts.Need(b, key, $"{bw}.wheel"), file, "storage.wheel." + key);
            ConstructParts.Range(v, min, max, file, "storage.wheel." + key);
            if (MathF.Abs(v * 20 - MathF.Round(v * 20)) > 0.001f)
                throw new ConstructPartException($"{file}: storage.wheel.{key} is on the {Extrusion.StepCm} cm step");
            return v;
        }
        d.ExtSpeed = ConstructParts.Num(ConstructParts.Need(b, "speed", $"{bw}.wheel"), file, "storage.wheel.speed");
        ConstructParts.Range(d.ExtSpeed, 0.02f, 4, file, "storage.wheel.speed");
        d.ExtDieAt = ConstructParts.Vec(ConstructParts.Need(b, "at", $"{bw}.wheel"), file, "storage.wheel.at");
        d.ExtWheelMax = new Vector2(Size("maxWidth", Extrusion.MinWheelWidthCm / 100f, (air ? Extrusion.MaxChordCm : Extrusion.MaxWheelWidthCm) / 100f), Size("maxDiameter", Extrusion.MinWheelCm / 100f, (air ? Extrusion.MaxLengthCm : fan ? Extrusion.MaxFanCm : Extrusion.MaxWheelCm) / 100f));
        d.ExtWheelStart = new Vector2(Size("startWidth", Extrusion.MinWheelWidthCm / 100f, d.ExtWheelMax.X), Size("startDiameter", Extrusion.MinWheelCm / 100f, d.ExtWheelMax.Y));
        d.ExtPush = ConstructParts.Num(ConstructParts.Need(b, "pushForce", $"{bw}.wheel"), file, "storage.wheel.pushForce");
        ConstructParts.Range(d.ExtPush, 0, 1000000, file, "storage.wheel.pushForce");
        Socket(d, file);
        d.IsExtruder = d.IsWheelFormer = true;
        d.IsFanFormer = fan;
        d.IsAirframeFormer = air;
        d.DataIn = fan || air ? [ConstructGatherer.RunPort, ConstructExtruder.CutName] : [ConstructGatherer.RunPort, ConstructExtruder.CutName, ConstructExtruder.ModeName];
    }

    private static void Socket(ConstructPartDef d, string file)
    {
        d.ExtDieSocket = Array.FindIndex(d.Sockets, s => s.Kind == SocketKind.Die);
        if (d.ExtDieSocket < 0)
            throw new ConstructPartException($"{file}: this machine needs a socket of kind \"die\" (its output, facing +z)");
    }

    private static Vector2 Pair(System.Text.Json.JsonElement e, string file, string what)
    {
        if (e.ValueKind != System.Text.Json.JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new ConstructPartException($"{file}: \"{what}\" is [width, height]");
        var v = new Vector2(ConstructParts.Num(e[0], file, what), ConstructParts.Num(e[1], file, what));
        ConstructParts.Range(v.X, 0.1f, Extrusion.MaxSectionCm / 100f, file, what);
        ConstructParts.Range(v.Y, 0.1f, Extrusion.MaxSectionCm / 100f, file, what);
        return v;
    }
}
