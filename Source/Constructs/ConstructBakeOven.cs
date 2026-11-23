using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Why a Bake Oven isn't baking (its store's Refused byte: on the wire, its screen and lamp; ConstructBakeOven.cs).</summary>
public enum OvenStall : byte
{
    None,
    NoBed, // no full rectangle of Oven Bed Blocks beside the Core
    NoMasts, // a corner of the bed has no Mast Block on it
    NoRoom, // every bed cell has a mast on it: nowhere to set a group
    Empty, // nothing on the bed
    Several, // more than one body on the bed
    Overhang, // the group stands off the bed's clear cells (over its edge, or on a mast's cell)
    TooTall, // the group is higher than the raised hood
    TooMany, // more subparts than the bed takes
    Moving, // the group is still moving (it settles first)
    Done, // baked (or un-baked): lift it off
    NoPower,
    CantBake, // Bake: a part on the bed that isn't plain structure, a joint, a weld off the grid, or too many boxes (BakedParts.CanBake)
    NotBaked, // Un-bake: the group isn't one baked part (BakedParts.CanUnbake)
    Failed, // the bake or un-bake at the end of the heat was refused (the log says why); the group is as it went in
}

/// <summary>Where a Bake Oven's cycle is (its store's Blocked byte: on the wire, so every peer draws the hood the same).</summary>
public enum OvenPhase : byte { Open, Drop, Heat, Cool, Lift }

/// <summary>A place where a machine hurts whoever stands in it this step (server; Game reads and clears them each tick).</summary>
public struct MachineHazard
{
    public Matrix4x4 ToLocal; // world to the box's frame
    public Vector3 Min, Max; // the box, in that frame
    public int Damage; // per tick a player is in it
}

public sealed partial class ConstructWorld
{
    public readonly List<MachineHazard> Hazards = new(16); // this step's crush and burn boxes (ConstructBakeOven.Step)
    internal int OvenTicks;
}

/// <summary>
/// The Bake Oven (design page drawing 19): an Oven Core, the Oven Bed Blocks welded beside it (MachineBlocks.cs finds them: the bed is
/// their largest full rectangle) and a shared Mast Block column on each of the bed's four corners. The shortest corner column sets
/// the clear height under the raised hood: 2 m a mast less the hood's 0.5 m. A welded group is set on the bed from above (a crane,
/// a Lifter, the physics gun); with power, Run on (or nothing wired to it) and the group wholly on the bed's clear cells, settled, under
/// the hood and within the subpart limit (16 a bed block, at most 255), the cycle runs: the hood drops at 1 m/s, the bed heats for 2 s
/// a subpart times 12 over the bed's blocks (at least 0.5 s a subpart), it cools for 3 s, and the hood lifts. Each bed block draws
/// 3.5 kW while it heats; the screws draw the Core's own power while the hood moves.
/// <para>
/// The hood drops on whatever is on the bed: anyone under it is crushed, and anyone inside while it is down is burned (machines never
/// stop for people: hazard stripes and the HOT sign are the warning). The hood, its screw columns and the oven's own bridge are drawn
/// by BakeOvenRender.cs from the phase byte; nothing new goes on the wire.
/// </para>
/// <para>
/// The bake (BakedParts.cs): at the end of the heat, in Bake mode the group becomes a new body of one <c>Baked (#hash)</c> part (its Module
/// Socket plates stay real parts beside it), its design kept in the world's store; in Un-bake mode a baked part becomes its subparts again.
/// What can't be baked or un-baked is refused before the hood drops (<see cref="OvenStall.CantBake"/>, <see cref="OvenStall.NotBaked"/>).
/// </para>
/// </summary>
public static partial class ConstructBakeOven
{
    private const int Every = World.TickRate / 10; // a step is 0.1 s
    public const float HoodThick = 0.5f; // m: the hood's own height, taken off the masts' (drawing 19)
    private const float CoolSeconds = 3f;
    private const int CrushDamage = 2000, BurnDamage = 1; // per 60 Hz game tick (GameMachineHazards reads the list every tick): the skirts landing on someone, and the heat inside a closed hood (60 a second)
    private const float Settled = 0.05f; // m/s: a group moving slower than this has settled on the bed

    /// <summary>The <c>"bake"</c> block of a Core's <c>"storage"</c>.</summary>
    public static void Parse(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        ConstructParts.Known(b, ["bedTop", "kwBlock", "hoodSpeed", "partSeconds", "partsBlock", "maxParts"], $"{bw}.bake");
        float Num(string key, float lo, float hi)
        {
            var v = ConstructParts.Num(ConstructParts.Need(b, key, $"{bw}.bake"), file, $"storage.bake.{key}");
            ConstructParts.Range(v, lo, hi, file, $"storage.bake.{key}");
            return v;
        }
        d.BakeBedTop = Num("bedTop", 0.1f, 2f);
        d.BakeKwBlock = Num("kwBlock", 0.1f, 1000f);
        d.BakeHoodSpeed = Num("hoodSpeed", 0.1f, 10f);
        d.BakePartSeconds = Num("partSeconds", 0.1f, 60f);
        d.BakePartsBlock = (int)Num("partsBlock", 1, 255);
        d.BakeMaxParts = (int)Num("maxParts", 1, 255);
        d.IsBakeOven = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    /// <summary>An oven's bed as its blocks make it: the rectangle (Core cell frame), its corner masts and the clear height (0 with no bed or masts).</summary>
    public readonly struct Bed
    {
        public readonly int X0, Z0, W, L, Columns, Blocks;
        public readonly float Cell, Top, Clear;

        public Bed(Construct c, in CMachineAsm asm)
        {
            var d = c.Parts[asm.Core].D;
            (X0, Z0, W, L, Cell, Top) = (asm.X0, asm.Z0, asm.W, asm.L, d.BlockCell, d.BakeBedTop);
            Blocks = W * L;
            Columns = MachineBlocks.CornerColumns(c, asm);
            Clear = Columns > 0 ? Columns * Cell - HoodThick : 0;
        }

        /// <summary>Is cell (x, z) of the Core's frame a bed cell with nothing stacked on it?</summary>
        public bool Free(Construct c, in CMachineAsm asm, int x, int z) =>
            x >= X0 && x < X0 + W && z >= Z0 && z < Z0 + L && MachineBlocks.ColumnAt(c, asm, x, z) == 0;

        /// <summary>The cell's middle in the Core part's frame (its origin: the bed layer's middle).</summary>
        public Vector3 CellMid(ConstructPartDef core, int x, int z) => (core.BlockOrigin ?? Vector3.Zero) + new Vector3(x * Cell, 0, z * Cell);

        /// <summary>The hood's underside when raised, and the masts' top, in the Core part's frame.</summary>
        public float Raised => Top + Clear;
        public float MastTop => Top + Columns * Cell;
    }

    /// <summary>The Core part's frame in the world (part frame to world).</summary>
    public static Matrix4x4 CoreFrame(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>Seconds of heat a group of <paramref name="parts"/> subparts takes on a bed of <paramref name="blocks"/> blocks.</summary>
    public static float HeatSeconds(ConstructPartDef d, int parts, int blocks) =>
        parts * MathF.Max(0.5f, d.BakePartSeconds * 12f / Math.Max(1, blocks));

    /// <summary>The subparts this bed takes.</summary>
    public static int MaxParts(ConstructPartDef d, int blocks) => Math.Min(d.BakeMaxParts, d.BakePartsBlock * blocks);

    /// <summary>
    /// How far down the hood is, 0 (raised) to 1 (on the bed), by its phase and the share of the phase gone (<paramref name="t"/>, 0 to 1).
    /// <paramref name="row"/>/<paramref name="rows"/>: a section's place in the ripple (each section starts a little after the one before).
    /// </summary>
    public static float HoodDown(OvenPhase phase, float t, int row = 0, int rows = 1)
    {
        const float Spread = 0.35f; // (the last section starts this share of the way in, and every section takes the rest)
        var lag = rows > 1 ? Spread * row / (rows - 1) : 0f;
        var s = Math.Clamp((t - lag) / (rows > 1 ? 1f - Spread : 1f), 0f, 1f);
        s = s * s * (3 - 2 * s);
        return phase switch
        {
            OvenPhase.Drop => s,
            OvenPhase.Heat or OvenPhase.Cool => 1f,
            OvenPhase.Lift => 1f - s,
            _ => 0f,
        };
    }

    /// <summary>The power an oven draws now (W): each bed block's heat while it heats, the Core's own while the hood moves.</summary>
    public static float PowerOf(ConstructWorld world, Construct c, int part)
    {
        var d = c.Parts[part].D;
        if (world.StoreOf(c, part) is not { } st || !MachineBlocks.TryOf(c, part, out var asm))
            return d.PowerDraw;
        return (OvenPhase)st.Blocked == OvenPhase.Heat ? asm.W * asm.L * d.BakeKwBlock * 1000f : d.PowerDraw;
    }

    // ---- The step (server, 10 Hz) ----------------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world)
    {
        if (++world.OvenTicks < Every)
            return;
        world.OvenTicks = 0;
        world.OvenLooks++;
        world.Hazards.Clear();
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (c.MachineAsmCount == 0)
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                var core = c.MachineAsms[a].Core;
                if (core < c.PartCount && c.Parts[core].D.IsBakeOven && world.StoreOf(c, core) is { } st)
                    Work(world, c, core, a, st);
            }
        }
        ConstructAssembling.AddHazards(world); // (after the ovens', so their crush boxes always have room: Assemblers' working beds, the arms strike and the weld burns)
    }

    private static void Work(ConstructWorld world, Construct c, int part, int asmIndex, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        ref readonly var asm = ref c.MachineAsms[asmIndex];
        var bed = new Bed(c, asm);
        var held = ConstructGatherer.HeldOff(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        var powered = ConstructRecycler.Powered(world, c, part);
        var phase = (OvenPhase)st.Blocked;
        if (phase == OvenPhase.Open)
        {
            var why = Check(world, c, part, asm, bed, out var group, out var parts);
            if (group != 0 && why is not (OvenStall.Several or OvenStall.Empty) && st.Refused is (byte)OvenStall.Done or (byte)OvenStall.Failed && st.JobAmount == group
                && st.OvenUnbaked == Unbaking(p, d))
                why = (OvenStall)st.Refused; // (this group is done, or was refused at the end: it waits to be lifted off, or the mode turned)
            else if (why == OvenStall.None && !powered)
                why = OvenStall.NoPower;
            if (powered && !held && !p.Broken && p.Orders is { Count: > 0 })
                Kits(world, c, part, asm, bed, why, group); // (its orders: a kit built at a station and set on the bed, ConstructBakeOvenOrders.cs)
            if (why != OvenStall.None || held || p.Broken)
            {
                SetStall(world, st, why);
                ConstructGatherer.SetBusy(world, st, false);
                MachineScreens.SetProgress(world, st, 0);
                return;
            }
            // Start: the hood comes down.
            st.JobAmount = group;
            st.OvenParts = parts; // (not JobMaterial: a save reads that byte as a material)
            st.JobFull = Steps(DropSeconds(bed, d)) + Steps(HeatSeconds(d, parts, bed.Blocks)) + Steps(CoolSeconds) + Steps(DropSeconds(bed, d));
            Enter(world, st, OvenPhase.Drop, Steps(DropSeconds(bed, d)));
            ConstructGatherer.SetBusy(world, st, true);
            SetStall(world, st, OvenStall.None);
            return;
        }
        // Under way: it stops for nothing but power (a wire off doesn't lift a hood that is down).
        if (!powered)
        {
            SetStall(world, st, OvenStall.NoPower);
            ConstructGatherer.SetBusy(world, st, false);
            return;
        }
        SetStall(world, st, OvenStall.None);
        ConstructGatherer.SetBusy(world, st, true);
        AddHazards(world, c, part, asm, bed, phase, st);
        if (st.JobFull == 0) // (loaded mid-cycle: the total isn't saved, so count it again from here)
            st.JobFull = Steps(DropSeconds(bed, d)) + Steps(HeatSeconds(d, st.OvenParts, bed.Blocks)) + Steps(CoolSeconds) + Steps(DropSeconds(bed, d));
        if (--st.JobTicks > 0)
        {
            var left = st.JobTicks + phase switch
            {
                OvenPhase.Drop => Steps(HeatSeconds(d, st.OvenParts, bed.Blocks)) + Steps(CoolSeconds) + Steps(DropSeconds(bed, d)),
                OvenPhase.Heat => Steps(CoolSeconds) + Steps(DropSeconds(bed, d)),
                OvenPhase.Cool => Steps(DropSeconds(bed, d)),
                _ => 0,
            };
            MachineScreens.SetProgress(world, st, 100 - left * 100 / Math.Max(1, st.JobFull));
            return;
        }
        switch (phase)
        {
            case OvenPhase.Drop:
                Enter(world, st, OvenPhase.Heat, Steps(HeatSeconds(d, st.OvenParts, bed.Blocks)));
                break;
            case OvenPhase.Heat:
                Act(world, c, part, st);
                Enter(world, st, OvenPhase.Cool, Steps(CoolSeconds));
                break;
            case OvenPhase.Cool:
                Enter(world, st, OvenPhase.Lift, Steps(DropSeconds(bed, d)));
                break;
            default:
                Enter(world, st, OvenPhase.Open, 0);
                SetStall(world, st, st.OvenFailed ? OvenStall.Failed : OvenStall.Done); // (JobAmount keeps the group: it isn't baked again until it leaves)
                ConstructGatherer.SetBusy(world, st, false);
                MachineScreens.SetProgress(world, st, 100);
                break;
        }
    }

    /// <summary>Un-bake mode is the Core's Mode setting on.</summary>
    public static bool Unbaking(in CPart p, ConstructPartDef d) => ConstructConfig.Get(p, d, CfgKey.Mode, 0) >= 0.5f;

    /// <summary>The end of the heat: the group on the bed baked into one part, or un-baked into its subparts (BakedParts.cs); JobAmount follows it.</summary>
    private static void Act(ConstructWorld world, Construct c, int part, CStore st)
    {
        st.OvenFailed = false;
        var unbake = st.OvenUnbaked = Unbaking(c.Parts[part], c.Parts[part].D);
        if (world.Get((ushort)st.JobAmount) is not { } group)
        {
            st.OvenFailed = true;
            Console.WriteLine($"[oven] construct {c.Id}: the group on the bed (construct {st.JobAmount}) went while the hood was down");
            return;
        }
        var want = WorkOrders.HeadOf(c.Parts[part], out var order) && !order.IsPiece && order.Name == null ? order.Part : (ushort)0; // (two parts with one kit: the one ordered)
        var made = unbake ? BakedParts.Unbake(world, group) : BakedParts.Bake(world, group, want);
        if (made == null)
        {
            st.OvenFailed = true;
            Console.WriteLine($"[oven] construct {c.Id}: {(unbake ? "un-bake" : "bake")} of construct {group.Id} REFUSED: {BakedParts.LastRefusal}");
            return;
        }
        st.JobAmount = made.Id; // (the new body is the one that waits to be lifted off)
        if (!unbake && made.PartCount > 0 && WorkOrders.HeadOf(c.Parts[part], out var head) && !head.IsPiece && head.Name == null
            && Array.FindIndex(made.Parts, 0, made.PartCount, q => q.Def == head.Part) >= 0)
            WorkOrders.Made(world, c, c.Parts[part].Orders); // (a kit of its head order baked into the part: one made, ConstructBakeOvenOrders.cs)
        Console.WriteLine($"[oven] construct {c.Id}: {(unbake ? "un-baked" : "baked")} construct {group.Id} into construct {made.Id} ({made.PartCount} parts)");
    }

    private static float DropSeconds(in Bed bed, ConstructPartDef d) => MathF.Max(1f, bed.Clear / d.BakeHoodSpeed);
    private static int Steps(float seconds) => Math.Max(1, (int)MathF.Ceiling(seconds * 10));

    private static void Enter(ConstructWorld world, CStore st, OvenPhase phase, int steps)
    {
        st.JobTicks = steps;
        if (st.Blocked == (byte)phase)
            return;
        st.Blocked = (byte)phase;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    private static void SetStall(ConstructWorld world, CStore st, OvenStall s)
    {
        if (st.Refused == (byte)s)
            return;
        st.Refused = (byte)s;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    /// <summary>
    /// What stands on the bed, and why it can't bake (None: <paramref name="group"/> is the body's id, <paramref name="parts"/> its parts).
    /// A body counts as on the bed when its bounds reach into the clear space over it; it must lie wholly over the bed's clear cells.
    /// </summary>
    public static OvenStall Check(ConstructWorld world, Construct c, int part, in CMachineAsm asm, in Bed bed, out int group, out int parts)
    {
        group = parts = 0;
        if (asm.W == 0)
            return OvenStall.NoBed;
        if (bed.Columns == 0)
            return OvenStall.NoMasts;
        var d = c.Parts[part].D;
        var frame = CoreFrame(c, part);
        if (!Matrix4x4.Invert(frame, out var toCore))
            return OvenStall.NoBed;
        var o = d.BlockOrigin ?? Vector3.Zero;
        float half = bed.Cell * 0.5f, bx0 = o.X + bed.X0 * bed.Cell - half, bz0 = o.Z + bed.Z0 * bed.Cell - half;
        float bx1 = bx0 + bed.W * bed.Cell, bz1 = bz0 + bed.L * bed.Cell;
        var free = false;
        for (var x = bed.X0; x < bed.X0 + bed.W && !free; x++)
            for (var z = bed.Z0; z < bed.Z0 + bed.L && !free; z++)
                free = bed.Free(c, asm, x, z);
        if (!free)
            return OvenStall.NoRoom;
        var why = OvenStall.Empty;
        var bodies = world.Bodies;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var b = bodies[bi];
            if (b == c || b.PartCount == 0 || b.HeldBy != 0)
                continue; // (a body held, a kit still on a lifter's hook, isn't on the bed yet)
            // Its bounds in the Core's frame (the eight corners of its rest-frame box, through the world).
            b.WorldFrame(0, out var bq, out var bt);
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? b.LocalMin.X : b.LocalMax.X, (k & 2) == 0 ? b.LocalMin.Y : b.LocalMax.Y, (k & 4) == 0 ? b.LocalMin.Z : b.LocalMax.Z);
                var v = Vector3.Transform(bt + Vector3.Transform(corner, bq), toCore);
                lo = Vector3.Min(lo, v);
                hi = Vector3.Max(hi, v);
            }
            const float slack = 0.05f;
            if (hi.X <= bx0 + slack || lo.X >= bx1 - slack || hi.Z <= bz0 + slack || lo.Z >= bz1 - slack || hi.Y <= bed.Top + slack || lo.Y >= bed.MastTop)
                continue; // (not over the bed)
            if (group != 0)
                return OvenStall.Several;
            group = b.Id;
            parts = b.PartCount;
            // Wholly over clear cells: every cell its footprint touches is a bed cell with no mast on it.
            int cx0 = (int)MathF.Floor((lo.X + slack - o.X + half) / bed.Cell), cx1 = (int)MathF.Floor((hi.X - slack - o.X + half) / bed.Cell);
            int cz0 = (int)MathF.Floor((lo.Z + slack - o.Z + half) / bed.Cell), cz1 = (int)MathF.Floor((hi.Z - slack - o.Z + half) / bed.Cell);
            why = OvenStall.None;
            for (var x = cx0; x <= cx1 && why == OvenStall.None; x++)
                for (var z = cz0; z <= cz1 && why == OvenStall.None; z++)
                    if (!bed.Free(c, asm, x, z))
                        why = OvenStall.Overhang;
            if (why == OvenStall.None && hi.Y > bed.Raised + slack)
                why = OvenStall.TooTall;
            else if (why == OvenStall.None && parts > MaxParts(d, bed.Blocks))
                why = OvenStall.TooMany;
            else if (why == OvenStall.None && (b.Vel - c.Vel).LengthSquared() > Settled * Settled)
                why = OvenStall.Moving;
            else if (why == OvenStall.None)
                why = Unbaking(c.Parts[part], d) ? BakedParts.CanUnbake(b, out _, tell: false) == null ? OvenStall.None : OvenStall.NotBaked
                    : BakedParts.CanBake(b, tell: false) == null ? OvenStall.None : OvenStall.CantBake;
        }
        return why;
    }

    /// <summary>The crush box under the skirts' foot while they come down (a body's height deep), the burn box under a closed hood.</summary>
    private static void AddHazards(ConstructWorld world, Construct c, int part, in CMachineAsm asm, in Bed bed, OvenPhase phase, CStore st)
    {
        if (world.Hazards.Count > 60 || phase is not (OvenPhase.Drop or OvenPhase.Heat or OvenPhase.Cool))
            return;
        if (!Matrix4x4.Invert(CoreFrame(c, part), out var toCore))
            return;
        var d = c.Parts[part].D;
        var o = d.BlockOrigin ?? Vector3.Zero;
        var half = bed.Cell * 0.5f;
        var min = new Vector3(o.X + bed.X0 * bed.Cell - half, bed.Top, o.Z + bed.Z0 * bed.Cell - half);
        var max = new Vector3(min.X + bed.W * bed.Cell, bed.Raised, min.Z + bed.L * bed.Cell);
        if (phase == OvenPhase.Drop)
        {
            var steps = Steps(DropSeconds(bed, d));
            var under = bed.Raised - bed.Clear * HoodDown(phase, 1f - st.JobTicks / (float)steps);
            // Only under the skirts: a band round the bed's edge (the roof stays up; whoever is inside is shut in, not crushed).
            var lo = MathF.Max(bed.Top, under - 1.9f);
            const float band = 0.45f;
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min with { Y = lo }, Max = new Vector3(max.X, under, min.Z + band), Damage = CrushDamage });
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = new Vector3(min.X, lo, max.Z - band), Max = max with { Y = under }, Damage = CrushDamage });
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min with { Y = lo }, Max = new Vector3(min.X + band, under, max.Z), Damage = CrushDamage });
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = new Vector3(max.X - band, lo, min.Z), Max = max with { Y = under }, Damage = CrushDamage });
        }
        else
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min, Max = max, Damage = BurnDamage }); // (the skirts are down: whoever is under the hood is shut in with the bake)
    }

    // ---- What every peer shows: its line state, its screen ---------------------------------------------------------------------

    public static LineState LineOf(ConstructWorld world, Construct c, int part, CStore st)
    {
        if (!ConstructRecycler.Powered(world, c, part) || (OvenStall)st.Refused == OvenStall.NoPower)
            return LineState.NoPower;
        if (st.Busy)
            return LineState.Running;
        if (st.Held)
            return LineState.Idle;
        return (OvenStall)st.Refused switch
        {
            OvenStall.Empty or OvenStall.Moving => LineState.Starved,
            OvenStall.Done or OvenStall.Several or OvenStall.Failed => LineState.Blocked,
            OvenStall.None => LineState.Idle,
            _ => LineState.Fault,
        };
    }

    public static string StallText(OvenStall s) => s switch
    {
        OvenStall.NoBed => "Weld bed blocks",
        OvenStall.NoMasts => "Masts on corners",
        OvenStall.NoRoom => "No clear bed cell",
        OvenStall.Empty => "Bed empty",
        OvenStall.Several => "One group only",
        OvenStall.Overhang => "Off the clear bed",
        OvenStall.TooTall => "Too tall for hood",
        OvenStall.TooMany => "Too many subparts",
        OvenStall.Moving => "Settling",
        OvenStall.Done => "Done: lift it off",
        OvenStall.CantBake => "Can't bake that",
        OvenStall.NotBaked => "Not a baked part",
        OvenStall.Failed => "Refused: lift it off",
        OvenStall.NoPower => "No power",
        _ => "Ready",
    };

    /// <summary>The Core's screen (MachineScreens.Fill): what it is doing, its mode and bed, its clear height, and the cycle's bar.</summary>
    public static void Screen(Construct c, int part, CStore st, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var phase = (OvenPhase)st.Blocked;
        if (st.Busy || phase != OvenPhase.Open)
        {
            card.State = PanelState.Working;
            card.Add(0, phase switch { OvenPhase.Drop => "Hood down", OvenPhase.Heat => "Baking", OvenPhase.Cool => "Cooling", OvenPhase.Lift => "Hood up", _ => "Working" });
        }
        else if (st.Held)
        {
            card.State = PanelState.Idle;
            card.Add(0, "Held off");
        }
        else
        {
            var s = (OvenStall)st.Refused;
            card.State = s switch { OvenStall.Empty or OvenStall.None or OvenStall.Moving => PanelState.Idle, OvenStall.Done => PanelState.Wants, OvenStall.NoPower => PanelState.Fault, _ => PanelState.Refused };
            card.Add(0, StallText(s));
        }
        card.Add(1, ConstructConfig.Get(p, d, CfgKey.Mode, 0) >= 0.5f ? "Un-bake " : "Bake ");
        if (MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0)
        {
            var bed = new Bed(c, asm);
            card.Add(1, asm.W).Add(1, 'x').Add(1, asm.L).Add(1, " bed");
            if (bed.Columns > 0)
            {
                var dm = (int)MathF.Round(bed.Clear * 10);
                card.Add(2, "Clear ").Add(2, dm / 10).Add(2, '.').Add(2, dm % 10).Add(2, " m, ").Add(2, MaxParts(d, bed.Blocks)).Add(2, " parts");
            }
            else
                card.Add(2, "No corner masts");
        }
        else
            card.Add(1, "no bed");
        card.Bar = st.Busy || phase != OvenPhase.Open ? st.Progress / 100f : 0f;
    }
}
