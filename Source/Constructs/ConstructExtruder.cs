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
public static class ConstructExtruder
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
    public static void Die(Construct c, int part, out Vector2 centre, out Vector2 size)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var half = d.ExtDie * 0.5f;
        var at = new Vector2(d.ExtDieAt.X, d.ExtDieAt.Y);
        var min = at - half;
        var max = at + half;
        Span<bool> seen = stackalloc bool[c.PartCount];
        Span<ushort> queue = stackalloc ushort[c.PartCount];
        int head = 0, tail = 0;
        seen[part] = true;
        queue[tail++] = (ushort)part;
        var back = Quaternion.Conjugate(Rot24.Quat(p.Rot));
        while (head < tail)
        {
            int from = queue[head++];
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var l = ref c.Links[k];
                int other = l.A == from ? l.B : l.B == from ? l.A : -1;
                if (other < 0 || other >= c.PartCount || seen[other] || !c.Parts[other].D.IsDieFrame || c.Parts[other].Broken)
                    continue;
                seen[other] = true;
                queue[tail++] = (ushort)other;
                c.PartBounds(other, out var mn, out var mx);
                var a = Vector3.Transform(mn - p.Pos, back);
                var b = Vector3.Transform(mx - p.Pos, back);
                min = Vector2.Min(min, new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y)));
                max = Vector2.Max(max, new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y)));
            }
        }
        centre = (min + max) * 0.5f;
        size = Vector2.Min(max - min, d.ExtMaxSection);
    }

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

    private static void SetStall(ConstructWorld world, CStore st, ExtrudeStall s)
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
        var product = ProductOf(c, part);
        if (product >= 0 && !c.Parts[product].D.IsExtrusion)
            product = -1; // (a bad link: nothing it can grow)
        var asked = (p.State & CutPending) != 0;
        p.State &= unchecked((byte)~CutPending); // (one that met no piece goes unused)
        var len = product >= 0 ? c.Parts[product].D.ExKey.L : 0;
        var depth = d.IsWheelFormer && (d.IsFanFormer || ByDepth(p, d));
        p.Out = p.OutNext = product < 0 ? 0 : (d.IsWheelFormer ? depth ? c.Parts[product].D.ExKey.H : c.Parts[product].D.ExKey.W : len) / 100f; // (its output: the length so far, a wheel's size in this mode)
        if (d.IsFanFormer && product >= 0 && c.Parts[product].D.ExKey.H >= FanTargetCm(p, d))
            asked = true; // (a Fan Former parts a prop off when it reaches the size asked)
        if (product >= 0 && !p.Broken && (asked || CutLengthCm(p, d) is var every and > 0 && len >= every) && ConstructRecycler.Powered(world, c, part))
        {
            if (!d.IsWheelFormer && ConstructPress.ChutesToPress(world, c, part))
            {
                // A chute into a press takes the piece whole (ConstructPress.Chute): it waits on the die until a bay will have it.
                p.State |= CutPending;
                ConstructGatherer.SetBusy(world, st, false);
                SetStall(world, st, ExtrudeStall.ChuteWaits);
                return false;
            }
            if (Cut(world, c, product) != null)
                return true;
        }
        var held = RunHeld(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            ConstructGatherer.SetBusy(world, st, false);
            SetStall(world, st, ExtrudeStall.None);
            return false;
        }
        var why = d.IsFanFormer ? GrowFan(world, query, c, part, st, product) : d.IsWheelFormer ? GrowWheel(world, query, c, part, st, product, depth) : Grow(world, query, c, part, st, product, len);
        SetStall(world, st, why);
        ConstructGatherer.SetBusy(world, st, why == ExtrudeStall.None);
        return false;
    }

    /// <summary>An Extruder's Cut every, in cm (0: none).</summary>
    private static int CutLengthCm(in CPart p, ConstructPartDef d) => d.IsWheelFormer ? 0 : (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.CutLength, 0) * 100);

    /// <summary>Does the Run wire hold the machine off? Its other inputs don't count.</summary>
    private static bool RunHeld(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        return p.InCount > 0 ? p.In < 0.5f : c.RoutesInto(part, RouteType.Data, 255) > 0;
    }

    /// <summary>Is a Wheel Former building Depth (the diameter)? A wire into Mode decides, else its Run mode setting.</summary>
    public static bool ByDepth(in CPart p, ConstructPartDef d) => p.InCount2 > 0 ? p.In2 >= 0.5f : ConstructConfig.Get(p, d, CfgKey.RunMode, 0) >= 0.5f;

    /// <summary>A step's growth, or why not.</summary>
    private static ExtrudeStall Grow(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, int len)
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
            if (ExtrusionTable.Current.Find(st.Material) == null)
                return ExtrudeStall.BadMaterial;
            Section(c, part, out var mask, out var w, out var h, out var centre, out var shape, out var wall);
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
        if (CutLengthCm(p, d) is var every and >= Extrusion.StepCm && every < limit)
            limit = every; // (a piece stops at its length, whatever the speed)
        if (len >= limit)
            return ExtrudeStall.AtCap;
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
        switch (PathClear(query, world, c, product, next, origin, rot, p.Seg, len, steps, d.ExtPush))
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
    private static ExtrudeStall GrowWheel(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product, bool depth)
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

    /// <summary>How far across a Fan Former's settings ask for, on the 5 cm step, inside the machine.</summary>
    private static int FanTargetCm(in CPart p, ConstructPartDef d)
    {
        var most = (int)MathF.Round(d.ExtWheelMax.Y * 100);
        var asked = (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.FanDiameter, d.ExtWheelMax.Y) * 20) * Extrusion.StepCm;
        return Math.Clamp(asked, (int)MathF.Round(d.ExtWheelStart.Y * 100), most);
    }

    /// <summary>
    /// A step of a Fan Former: the whole prop at its machine's first size, then bigger across with the material the added blade
    /// takes, until it reaches the size asked (<see cref="Work"/> parts it off then). Its kind and blades are set when it starts.
    /// </summary>
    private static ExtrudeStall GrowFan(ConstructWorld world, IWorldQuery query, Construct c, int part, CStore st, int product)
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
            if (ExtrusionTable.Current.FindFan(st.Material) == null)
                return ExtrudeStall.BadMaterial;
            key = default;
            var wall = Extrusion.FanWall((int)ConstructConfig.Get(p, d, CfgKey.FanKind, 0), (int)ConstructConfig.Get(p, d, CfgKey.FanBlades, 2));
            next = new ExtrusionKey(0, 0, (ushort)MathF.Round(d.ExtWheelStart.Y * 100), 0, st.Material, true, ExShape.Fan, wall);
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
        var row = ExtrusionTable.Current.FindFan(next.Material);
        if (row == null)
            return ExtrudeStall.BadMaterial;
        var limit = Math.Min(Extrusion.MaxFanCm, FanTargetCm(p, d));
        st.JobTicks += (int)(d.ExtSpeed * Every / World.TickRate * 1000);
        var steps = st.JobTicks / StepMm;
        if (steps == 0)
            return st.Amount > 0 ? ExtrudeStall.None : ExtrudeStall.NoMaterial;
        if (!fresh)
        {
            steps = Math.Min(steps, (limit - key.H) / Extrusion.StepCm);
            if (steps <= 0)
                return ExtrudeStall.AtCap;
            next = next with { H = (ushort)(key.H + steps * Extrusion.StepCm) };
        }
        var owed = (long)((Extrusion.FanVolume(next) - (fresh ? 0 : Extrusion.FanVolume(key))) * row.UnitsPerM3 * Milli) + st.JobAmount;
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
                return Path.Blocked;
            var near = world.Near(centre, half.Length() + 0.5f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var other = near[k];
                if (other.Dirty)
                    other.Rebuild();
                if (!Hits(other, other == c ? product : -1, obb))
                    continue;
                if (other == c || other.Anchored || other.CarriedBy != 0)
                    return Path.Blocked;
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
                return Path.Blocked;
            var near = world.Near(centre, half.Length() + 0.5f, _near);
            for (var k = 0; k < near.Count; k++)
            {
                var other = near[k];
                if (other.Dirty)
                    other.Rebuild();
                if (!Hits(other, other == c ? product : -1, obb))
                    continue;
                if (other == c || other.Anchored || other.CarriedBy != 0)
                    return Path.Blocked;
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
        return (p.State & CutPending) != 0 || CutLengthCm(p, p.D) is var every and > 0 && c.Parts[product].D.ExKey.L >= every;
    }

    internal static bool Hits(Construct b, int skip, in Obb obb)
    {
        var reach = obb.Half.Length();
        for (var i = 0; i < b.PartCount; i++)
        {
            if (i == skip)
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

    /// <summary>Why nothing can be welded to this part just now (a piece still on its die), or null.</summary>
    public static string? GrowingRefusal(Construct c, int part) =>
        (uint)part < (uint)c.PartCount && c.DefOf(part) is { IsExtrusion: true, ExKey.Growing: true } ? "that piece is still on its machine's die: cut it off first" : null;

    /// <summary>The length of the piece on the die, in cm.</summary>
    public static int LengthOf(Construct c, int part) => ProductOf(c, part) is var k and >= 0 && c.Parts[k].D.IsExtrusion ? c.Parts[k].D.ExKey.L : 0;

    /// <summary>The look line's key for the piece's size: changes when it grows (the store's version doesn't).</summary>
    public static int SizeKey(Construct c, int part) => ProductOf(c, part) is var k and >= 0 && c.Parts[k].D is { IsExtrusion: true } d ? d.ExKey.L + (d.ExKey.W * 257 ^ d.ExKey.H * 65537) : 0;

    /// <summary>What it is doing, or why not. Read by the look line on either side.</summary>
    public static string Status(Construct c, int part, CStore st)
    {
        if (c.Parts[part].Broken)
            return " - wrecked";
        if (st.Held)
            return Program.HeldLook;
        var d = c.Parts[part].D;
        var wheel = d.IsWheelFormer;
        var takes = d.IsFanFormer ? ExtrusionTable.Current.TakesFans() : wheel ? ExtrusionTable.Current.TakesWheels() : ExtrusionTable.Current.Takes();
        var round = Extrusion.ShapeOf((int)ConstructConfig.Get(c.Parts[part], d, CfgKey.Profile, 0)) != ExShape.Section;
        return (ExtrudeStall)st.Refused switch
        {
            ExtrudeStall.NoPower => " - no power: needs a charged battery",
            ExtrudeStall.NoMaterial => $" - needs material: takes {takes}",
            ExtrudeStall.BadMaterial => $" - can't {(wheel ? "form" : "extrude")} {RecyclingTable.Current.NameOf(st.Material)}: takes {takes}",
            ExtrudeStall.Mixed => $" - holds {RecyclingTable.Current.NameOf(st.Material)}, the piece on its {(wheel ? "hub" : "die")} is another material: cut it or take it off",
            ExtrudeStall.BadShape => " - " + ((wheel || round ? null : Extrusion.Refusal(MaskOf(c.Parts[part], d))) ?? "too many different pieces in this game"),
            ExtrudeStall.AtCap => wheel ? " - the wheel is as big as this mode goes: change mode or send Cut" : " - the piece is as long as it can be: cut it or take it off",
            ExtrudeStall.Blocked => wheel ? " - blocked: the wheel has no room to grow" : " - blocked: something fixed in the piece's path (a wall, the ground)",
            ExtrudeStall.TooHeavy => " - what is in the piece's path is too heavy to push",
            ExtrudeStall.ChuteWaits => " - the piece waits for its chute: the press's bay is full or won't take it",
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
        if (d.IsWheelFormer)
        {
            if (d.IsFanFormer)
                sb.Append($", to {FanTargetCm(p, d) / 100f:0.##} m");
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
                sb.Append($", {Extrusion.Label(k)}, {k.L / 100f:0.##}/{MathF.Min(d.ExtMaxLength, Extrusion.MaxLengthCm / 100f):0.#} m");
            }
            else
                sb.Append(", ").Append(Extrusion.Label(new ExtrusionKey(mask, (ushort)w, (ushort)h, 0, 0, true, shape, wall)));
            sb.Append(Status(c, part, st));
            if (ConstructConfig.Get(p, d, CfgKey.Width, 0) > die.X + 0.001f || ConstructConfig.Get(p, d, CfgKey.Height, 0) > die.Y + 0.001f)
                sb.Append($" - held to the {die.X:0.##} x {die.Y:0.##} m die{(d.ExtDie.X < d.ExtMaxSection.X ? ": attach Die Frames" : "")}");
        }
        if (near)
        {
            var tip = ConstructEdits.TipMaterial(inv, d, st);
            if (tip != 0)
                sb.Append($" - {SandboxKeys.Use}: tip in {Math.Min(inv.Materials[tip], d.BulkCapacity - st.Amount)} {names.NameOf(tip)}");
            else if (st.Amount > 0)
                sb.Append($" - {SandboxKeys.Use}: take {Math.Min(ConstructStorage.TakeLoad, st.Amount)} {names.NameOf(st.Material)}");
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
        ConstructParts.Known(b, ["speed", "die", "at", "maxSection", "maxLength", "pushForce"], $"{bw}.extrude");
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
        ConstructParts.Known(b, ["speed", "at", "maxWidth", "maxDiameter", "startWidth", "startDiameter", "pushForce", "fan"], $"{bw}.wheel");
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: a Wheel Former's bin takes \"{ConstructStorage.Any}\" material (Constructs/Extrusion.json says which it can use)");
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
        d.ExtWheelMax = new Vector2(Size("maxWidth", Extrusion.MinWheelWidthCm / 100f, Extrusion.MaxWheelWidthCm / 100f), Size("maxDiameter", Extrusion.MinWheelCm / 100f, Extrusion.MaxWheelCm / 100f));
        d.ExtWheelStart = new Vector2(Size("startWidth", Extrusion.MinWheelWidthCm / 100f, d.ExtWheelMax.X), Size("startDiameter", Extrusion.MinWheelCm / 100f, d.ExtWheelMax.Y));
        d.ExtPush = ConstructParts.Num(ConstructParts.Need(b, "pushForce", $"{bw}.wheel"), file, "storage.wheel.pushForce");
        ConstructParts.Range(d.ExtPush, 0, 1000000, file, "storage.wheel.pushForce");
        Socket(d, file);
        d.IsExtruder = d.IsWheelFormer = true;
        d.IsFanFormer = ConstructParts.Opt(b, "fan") is { ValueKind: System.Text.Json.JsonValueKind.True };
        d.DataIn = d.IsFanFormer ? [ConstructGatherer.RunPort, ConstructExtruder.CutName] : [ConstructGatherer.RunPort, ConstructExtruder.CutName, ConstructExtruder.ModeName];
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
