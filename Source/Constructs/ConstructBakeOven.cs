using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Why a Bake Oven isn't baking (its store's Refused byte: on the wire, its screen and lamp; ConstructBakeOven.cs).</summary>
public enum OvenStall : byte
{
    None,
    NoBed, // no full rectangle of Oven Bed Blocks beside the Core
    NoMasts, // (unused since the roller hearth tunnel, 2026-11-24: the byte's numbers stay as they were)
    NoRoom, // (unused since the roller hearth tunnel)
    Empty, // nothing in the tunnel
    Several, // more than one body in the tunnel
    Overhang, // the group stands off the hearth (into a wall or a door's swing)
    TooTall, // the group is higher than the tunnel's roof
    TooMany, // more subparts than the bed takes
    Moving, // the group is still moving (it settles first)
    Done, // baked (or un-baked): roll it out or lift it off
    NoPower,
    CantBake, // Bake: a part on the bed that isn't plain structure, a joint, a weld off the grid, or too many boxes (BakedParts.CanBake)
    NotBaked, // Un-bake: the group isn't one baked part (BakedParts.CanUnbake)
    Failed, // the bake or un-bake at the end of the heat was refused (the log says why); the group is as it went in
}

/// <summary>
/// Where a Bake Oven's cycle is (its store's Blocked byte: on the wire, so every peer draws the doors the same). The numbers are the
/// old hood's (Drop: the doors swing shut; Lift: they swing open), so saves keep their meaning.
/// </summary>
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
/// The Bake Oven, a roller hearth tunnel (Sean, 2026-11-24: the Bake Oven Lab's design; docs production.md "Bake Oven and baked parts"):
/// an Oven Core (the Recuperator) and the Oven Bed Blocks welded beside it (MachineBlocks.cs finds them: the bed is their largest full
/// rectangle). The machine draws a tunnel over the whole bed (BakeOvenRender.cs): ceramic hearth rollers with their tops at the roll line
/// (<see cref="ConstructPartDef.BakeRollTop"/>, a Roller Tile's, so a kit rolls in level), fixed side walls and a roof lined with
/// firebrick, ribbon windows on both walls and a pair of swing doors at each end. The tunnel runs along the bed's side the Core is
/// welded to; its in end is the end nearer the Core, its out end the other (<see cref="Bed"/>), unless a roller run leads in or out of
/// one end: then the runs lay the tunnel and the Core may stand anywhere round the bed (ConstructRollOut.OvenWays). The clear height over the rollers is
/// the Core's Tunnel height setting.
/// <para>
/// A kit comes in on a Roller Tile run that meets the in end (ConstructRollOut.cs), or is set in from above by a crane or an arm. With
/// power, Run on (or nothing wired to it) and the group wholly over the hearth, settled, under the roof and within the subpart limit
/// (16 a bed block, at most 255), the cycle runs: both doors swing shut (<see cref="ConstructPartDef.BakeDoorSeconds"/>), it heats for 2 s
/// a subpart times 12 over the bed's blocks (at least 0.5 s a subpart), cools for 3 s, and the doors swing open. Then the baked part
/// rolls out of the out end along a run that starts there (ConstructRollOut.OutOf), or waits for a lifter. Each bed block draws 3.5 kW
/// while it heats; the Core its own while the doors move.
/// </para>
/// <para>
/// The doors swing outward on whatever stands in their way, and whoever is in the closed tunnel is burned (machines never stop for
/// people: hazard edges and the HOT sign are the warning). The doors and the tunnel are drawn from the phase byte; nothing new goes on
/// the wire.
/// </para>
/// <para>
/// The bake (BakedParts.cs): at the end of the heat, in Bake mode the group becomes a new body of one <c>Baked (#hash)</c> part (its Module
/// Socket plates stay real parts beside it), its design kept in the world's store; in Un-bake mode a baked part becomes its subparts again.
/// What can't be baked or un-baked is refused before the doors shut (<see cref="OvenStall.CantBake"/>, <see cref="OvenStall.NotBaked"/>).
/// </para>
/// </summary>
public static partial class ConstructBakeOven
{
    private const int Every = World.TickRate / 10; // a step is 0.1 s
    private const float CoolSeconds = 3f;
    public const float Wall = 0.4f; // m: the tunnel's side walls, over the bed's outer edge
    public const float DoorZone = 0.45f; // m in from each end of the bed: the portal, its sill and the doors' leaves
    public const float SwingDeg = 100f; // how far a leaf swings out, degrees
    public const float DefaultClear = 3f; // m: the Tunnel height setting's default
    private const int SwingDamage = 4, BurnDamage = 1; // per 60 Hz game tick (GameMachineHazards reads the list every tick): a swinging leaf, the heat in the closed tunnel
    private const float Settled = 0.05f; // m/s: a group moving slower than this has settled on the hearth

    /// <summary>The <c>"bake"</c> block of a Core's <c>"storage"</c>.</summary>
    public static void Parse(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        ConstructParts.Known(b, ["bedTop", "rollTop", "kwBlock", "doorSeconds", "partSeconds", "partsBlock", "maxParts"], $"{bw}.bake");
        float Num(string key, float lo, float hi)
        {
            var v = ConstructParts.Num(ConstructParts.Need(b, key, $"{bw}.bake"), file, $"storage.bake.{key}");
            ConstructParts.Range(v, lo, hi, file, $"storage.bake.{key}");
            return v;
        }
        d.BakeBedTop = Num("bedTop", 0.1f, 2f);
        d.BakeRollTop = Num("rollTop", d.BakeBedTop, 3f);
        d.BakeKwBlock = Num("kwBlock", 0.1f, 1000f);
        d.BakeDoorSeconds = Num("doorSeconds", 0.5f, 30f);
        d.BakePartSeconds = Num("partSeconds", 0.1f, 60f);
        d.BakePartsBlock = (int)Num("partsBlock", 1, 255);
        d.BakeMaxParts = (int)Num("maxParts", 1, 255);
        d.IsBakeOven = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    /// <summary>
    /// An oven's bed as its blocks make it, and its tunnel. The tunnel's own frame (<see cref="ToCore"/>, the Bake Oven Lab's machine frame):
    /// metres, y up from the Core's foot, x across the bed from 0 to <see cref="Across"/>, z along it from the in end (0) to the out end
    /// (<see cref="Along"/>). The tunnel runs along the bed's edge the Core is welded beside; the in end is the one nearer the Core.
    /// </summary>
    public readonly struct Bed
    {
        public readonly int X0, Z0, W, L, Blocks;
        public readonly float Cell, Top, Roll, Clear, Across, Along;
        public readonly bool AlongZ; // the tunnel runs along the Core frame's z (else its x)
        public readonly bool Reversed; // the work flows towards the Core frame's lower z (or x)
        public readonly Matrix4x4 ToCore; // the tunnel's frame to the Core part's

        public Bed(Construct c, in CMachineAsm asm)
        {
            var d = c.Parts[asm.Core].D;
            (X0, Z0, W, L, Cell, Top, Roll) = (asm.X0, asm.Z0, asm.W, asm.L, d.BlockCell, d.BakeBedTop, d.BakeRollTop);
            Blocks = W * L;
            Clear = W > 0 ? Math.Clamp(ConstructConfig.Get(c.Parts[asm.Core], d, CfgKey.Height, DefaultClear), 1f, 16f) : 0;
            if (asm.OvenWay != 0)
            {
                // Its roller runs lay it (ConstructRollOut.OvenWays): the Core may stand anywhere round the bed (Sean, 2026-11-28).
                AlongZ = (asm.OvenWay & 2) == 0;
                Reversed = (asm.OvenWay & 4) != 0;
            }
            else
            {
                // No run in or out: beside the bed across x (the Core's cell 0 within the bed's rows) the tunnel runs along z, and the
                // other way round; the in end is the one nearer the Core.
                AlongZ = W == 0 || 0 >= Z0 && 0 <= Z0 + L - 1 || !(0 >= X0 && 0 <= X0 + W - 1);
                Reversed = AlongZ ? -Z0 > Z0 + L - 1 : -X0 > X0 + W - 1; // (the Core nearer the bed's high end: that is the in end)
            }
            var o = d.BlockOrigin ?? Vector3.Zero;
            var half = Cell * 0.5f;
            float minX = o.X + X0 * Cell - half, maxX = minX + W * Cell, minZ = o.Z + Z0 * Cell - half, maxZ = minZ + L * Cell;
            (Across, Along) = AlongZ ? (W * Cell, L * Cell) : (L * Cell, W * Cell);
            // A turn about y (never a mirror) that takes the lab's (x across, z along) onto the Core's frame, and the in end's corner.
            var (turn, at) = (AlongZ, Reversed) switch
            {
                (true, false) => (0f, new Vector3(minX, 0, minZ)),
                (true, true) => (MathF.PI, new Vector3(maxX, 0, maxZ)),
                (false, false) => (MathF.PI / 2, new Vector3(minX, 0, maxZ)),
                _ => (-MathF.PI / 2, new Vector3(maxX, 0, minZ)),
            };
            ToCore = Matrix4x4.CreateRotationY(turn) * Matrix4x4.CreateTranslation(at);
        }

        /// <summary>The hearth a kit lies on, in the tunnel's frame: inside the walls and the doors' zones, from the rollers to the roof.</summary>
        public Vector3 InMin => new(Wall + 0.05f, Roll, DoorZone);
        public Vector3 InMax => new(Across - Wall - 0.05f, Roll + Clear, Along - DoorZone);

        /// <summary>The roof's underside (tunnel frame y), and the width of one door leaf.</summary>
        public float Roof => Roll + Clear;
        public float Leaf => MathF.Max(0.5f, (Across - 0.2f) * 0.5f);

        /// <summary>The tunnel's middle line at the roll height, in the Core part's frame, at <paramref name="z"/> along it.</summary>
        public Vector3 Mid(float z) => Vector3.Transform(new Vector3(Across * 0.5f, Roll, z), ToCore);

        /// <summary>The tunnel's axis (in to out) in the Core part's frame.</summary>
        public Vector3 Axis => Vector3.TransformNormal(Vector3.UnitZ, ToCore);
    }

    /// <summary>The Core part's frame in the world (part frame to world).</summary>
    public static Matrix4x4 CoreFrame(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        c.WorldFrame(p.Seg, out var q, out var t);
        return Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>The tunnel's frame in the world (tunnel frame to world).</summary>
    public static Matrix4x4 TunnelFrame(Construct c, int part, in Bed bed) => bed.ToCore * CoreFrame(c, part);

    /// <summary>Seconds of heat a group of <paramref name="parts"/> subparts takes on a bed of <paramref name="blocks"/> blocks.</summary>
    public static float HeatSeconds(ConstructPartDef d, int parts, int blocks) =>
        parts * MathF.Max(0.5f, d.BakePartSeconds * 12f / Math.Max(1, blocks));

    /// <summary>The subparts this bed takes.</summary>
    public static int MaxParts(ConstructPartDef d, int blocks) => Math.Min(d.BakeMaxParts, d.BakePartsBlock * blocks);

    /// <summary>
    /// How far the doors are shut, 0 (open) to 1 (shut), by the phase and the share of the phase gone (<paramref name="t"/>, 0 to 1), eased.
    /// </summary>
    public static float DoorsShut(OvenPhase phase, float t)
    {
        var s = Math.Clamp(t, 0f, 1f);
        s = s * s * (3 - 2 * s);
        return phase switch
        {
            OvenPhase.Drop => s,
            OvenPhase.Heat or OvenPhase.Cool => 1f,
            OvenPhase.Lift => 1f - s,
            _ => 0f,
        };
    }

    /// <summary>The power an oven draws now (W): each bed block's heat while it heats, the Core's own while the doors move.</summary>
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
        ConstructAssembling.AddHazards(world); // (after the ovens', so their boxes always have room: Assemblers' working beds, the arms strike and the weld burns)
        SmelterWorks.AddHazards(world); // (working Smelters' charge doors, runners and Casting Beds burn: SmelterWorks.cs)
        FoundryWorks.AddHazards(world); // (working Foundries' pours, launders and Mould Lines burn: FoundryWorks.cs)
        SawmillWorks.AddHazards(world); // (working Sawmills' bands and carriages cut: SawmillWorks.cs)
        BreakerWorks.AddHazards(world); // (working Scrap Breakers' rotors: BreakerWorks.cs)
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
            {
                why = (OvenStall)st.Refused; // (this group is done, or was refused at the end: it rolls out, or waits to be lifted off, or the mode turned)
                if (powered && !held && !p.Broken)
                    ConstructRollOut.OutOf(world, c, part, group); // (along the run that starts at the tunnel's out end, if there is one)
            }
            else if (why == OvenStall.None && !powered)
                why = OvenStall.NoPower;
            if (powered && !held && !p.Broken && p.Orders is { Count: > 0 })
                Kits(world, c, part, asm, bed, why, open: true, bedKit: BedKit(why)); // (its orders: a kit built at a station and set on the bed, ConstructBakeOvenOrders.cs)
            if (why == OvenStall.Empty && powered && !held && !p.Broken)
                ConstructRollOut.RiderOnto(world, c, part); // (a piece waiting at the end of a run the oven takes rolls into the tunnel: ConstructRollOut.cs)
            if (why != OvenStall.None || held || p.Broken)
            {
                SetStall(world, st, why);
                ConstructGatherer.SetBusy(world, st, false);
                MachineScreens.SetProgress(world, st, 0);
                return;
            }
            // Start: the doors swing shut.
            st.JobAmount = group;
            st.OvenParts = parts; // (not JobMaterial: a save reads that byte as a material)
            st.JobFull = Full(d, parts, bed);
            Enter(world, st, OvenPhase.Drop, Steps(d.BakeDoorSeconds));
            ConstructGatherer.SetBusy(world, st, true);
            SetStall(world, st, OvenStall.None);
            return;
        }
        // Under way: it stops for nothing but power (a wire off doesn't open doors that are shut).
        if (!powered)
        {
            SetStall(world, st, OvenStall.NoPower);
            ConstructGatherer.SetBusy(world, st, false);
            return;
        }
        SetStall(world, st, OvenStall.None);
        ConstructGatherer.SetBusy(world, st, true);
        AddHazards(world, c, part, bed, phase);
        if (!held && !p.Broken && p.Orders is { Count: > 0 })
            Kits(world, c, part, asm, bed, OvenStall.None, open: false, bedKit: phase is OvenPhase.Drop or OvenPhase.Heat); // (the next kit starts at its station while this one bakes: ConstructStationAhead.cs)
        if (st.JobFull == 0) // (loaded mid-cycle: the total isn't saved, so count it again from here)
            st.JobFull = Full(d, st.OvenParts, bed);
        if (--st.JobTicks > 0)
        {
            var left = st.JobTicks + phase switch
            {
                OvenPhase.Drop => Steps(HeatSeconds(d, st.OvenParts, bed.Blocks)) + Steps(CoolSeconds) + Steps(d.BakeDoorSeconds),
                OvenPhase.Heat => Steps(CoolSeconds) + Steps(d.BakeDoorSeconds),
                OvenPhase.Cool => Steps(d.BakeDoorSeconds),
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
                Enter(world, st, OvenPhase.Lift, Steps(d.BakeDoorSeconds));
                break;
            default:
                Enter(world, st, OvenPhase.Open, 0);
                SetStall(world, st, st.OvenFailed ? OvenStall.Failed : OvenStall.Done); // (JobAmount keeps the group: it isn't baked again until it leaves)
                ConstructGatherer.SetBusy(world, st, false);
                MachineScreens.SetProgress(world, st, 100);
                break;
        }
    }

    private static int Full(ConstructPartDef d, int parts, in Bed bed) =>
        Steps(d.BakeDoorSeconds) + Steps(HeatSeconds(d, parts, bed.Blocks)) + Steps(CoolSeconds) + Steps(d.BakeDoorSeconds);

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
            Console.WriteLine($"[oven] construct {c.Id}: the group in the tunnel (construct {st.JobAmount}) went while the doors were shut");
            return;
        }
        var want = WorkOrders.HeadOf(c.Parts[part], out var order) && BakeKits.OvenDef(order) is { } wd ? (ushort)wd.Id : (ushort)0; // (two parts with one kit: the one ordered)
        var made = unbake ? BakedParts.Unbake(world, group) : BakedParts.Bake(world, group, want, c.Parts[part].Orders);
        if (made == null)
        {
            st.OvenFailed = true;
            Console.WriteLine($"[oven] construct {c.Id}: {(unbake ? "un-bake" : "bake")} of construct {group.Id} REFUSED: {BakedParts.LastRefusal}");
            return;
        }
        st.JobAmount = made.Id; // (the new body is the one that rolls out or waits to be lifted off)
        if (!unbake && made.PartCount > 0 && WorkOrders.HeadOf(c.Parts[part], out var head) && BakeKits.OvenDef(head) is { } hd
            && Array.FindIndex(made.Parts, 0, made.PartCount, q => q.Def == hd.Id) >= 0)
            WorkOrders.Made(world, c, c.Parts[part].Orders); // (a kit of its head order baked into the part: one made, ConstructBakeOvenOrders.cs)
        Console.WriteLine($"[oven] construct {c.Id}: {(unbake ? "un-baked" : "baked")} construct {group.Id} into construct {made.Id} ({made.PartCount} parts)");
    }

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

    /// <summary>A body's rest box in the tunnel's frame (the eight corners of its box through the world).</summary>
    public static void BoundsIn(Construct b, in Matrix4x4 toTunnel, out Vector3 lo, out Vector3 hi)
    {
        b.WorldFrame(0, out var bq, out var bt);
        lo = new Vector3(float.MaxValue);
        hi = new Vector3(float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) == 0 ? b.LocalMin.X : b.LocalMax.X, (k & 2) == 0 ? b.LocalMin.Y : b.LocalMax.Y, (k & 4) == 0 ? b.LocalMin.Z : b.LocalMax.Z);
            var v = Vector3.Transform(bt + Vector3.Transform(corner, bq), toTunnel);
            lo = Vector3.Min(lo, v);
            hi = Vector3.Max(hi, v);
        }
    }

    /// <summary>
    /// What stands in the tunnel, and why it can't bake (None: <paramref name="group"/> is the body's id, <paramref name="parts"/> its parts).
    /// A body counts as in the tunnel when its bounds reach over the bed under the roof; it must lie wholly over the hearth.
    /// </summary>
    public static OvenStall Check(ConstructWorld world, Construct c, int part, in CMachineAsm asm, in Bed bed, out int group, out int parts)
    {
        group = parts = 0;
        if (asm.W == 0)
            return OvenStall.NoBed;
        var d = c.Parts[part].D;
        if (!Matrix4x4.Invert(TunnelFrame(c, part, bed), out var toTunnel))
            return OvenStall.NoBed;
        var why = OvenStall.Empty;
        var bodies = world.Bodies;
        const float slack = 0.05f;
        var (inLo, inHi) = (bed.InMin, bed.InMax);
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var b = bodies[bi];
            if (b == c || b.PartCount == 0 || b.HeldBy != 0)
                continue; // (a body held, a kit still on a lifter's hook or rolling, isn't in yet)
            BoundsIn(b, toTunnel, out var lo, out var hi);
            if (hi.X <= slack || lo.X >= bed.Across - slack || hi.Z <= slack || lo.Z >= bed.Along - slack || hi.Y <= bed.Top + slack || lo.Y >= bed.Roof + 0.5f)
                continue; // (not over the bed)
            if (group != 0)
                return OvenStall.Several;
            group = b.Id;
            parts = b.PartCount;
            why = OvenStall.None;
            if (lo.X < inLo.X - slack || hi.X > inHi.X + slack || lo.Z < inLo.Z - slack || hi.Z > inHi.Z + slack)
                why = OvenStall.Overhang;
            else if (hi.Y > bed.Roof + slack)
                why = OvenStall.TooTall;
            else if (parts > MaxParts(d, bed.Blocks))
                why = OvenStall.TooMany;
            else if ((b.Vel - c.Vel).LengthSquared() > Settled * Settled)
                why = OvenStall.Moving;
            else
                why = Unbaking(c.Parts[part], d) ? BakedParts.CanUnbake(b, out _, tell: false) == null ? OvenStall.None : OvenStall.NotBaked
                    : BakedParts.CanBake(b, tell: false) == null ? OvenStall.None : OvenStall.CantBake;
        }
        return why;
    }

    /// <summary>
    /// Does the oven keep body <paramref name="b"/>, lying in its tunnel, from a lifter: its doors aren't open yet, or it will roll out
    /// along the oven's way out (ConstructRollOut.OutOf) and be taken at the run instead?
    /// </summary>
    public static bool Keeps(ConstructWorld world, Construct c, int part, Construct b)
    {
        if ((uint)part >= (uint)c.PartCount || !c.DefOf(part).IsBakeOven || world.StoreOf(c, part) is not { } st || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
            return false;
        var bed = new Bed(c, asm);
        if (!Matrix4x4.Invert(TunnelFrame(c, part, bed), out var toTunnel))
            return false;
        BoundsIn(b, toTunnel, out var lo, out var hi);
        if (hi.X <= 0 || lo.X >= bed.Across || hi.Z <= 0.3f || lo.Z >= bed.Along - 0.3f || hi.Y <= bed.Top || lo.Y >= bed.Roof + 0.5f)
            return false; // (not in the tunnel: rolled out, or never in)
        if (st.Blocked != (byte)OvenPhase.Open)
            return true;
        return ConstructRollOut.OutRunOf(c, part) >= 0 && ConstructRecycler.Powered(world, c, part) && !(world.OvenRolled.TryGetValue(c.Id << 16 | part, out var rolled) && rolled == b.Id); // (once rolled, stopped short or with no power to roll it: a lifter's)
    }

    /// <summary>The swing boxes in front of both ends while the doors move, the burn box inside the shut tunnel.</summary>
    private static void AddHazards(ConstructWorld world, Construct c, int part, in Bed bed, OvenPhase phase)
    {
        if (world.Hazards.Count > 60 || phase is not (OvenPhase.Drop or OvenPhase.Heat or OvenPhase.Cool or OvenPhase.Lift))
            return;
        if (!Matrix4x4.Invert(TunnelFrame(c, part, bed), out var toTunnel))
            return;
        if (phase is OvenPhase.Drop or OvenPhase.Lift)
        {
            // Each end's leaves sweep out to a leaf's width past the portal, from the sill to the head.
            var reach = bed.Leaf;
            world.Hazards.Add(new MachineHazard { ToLocal = toTunnel, Min = new Vector3(0, bed.Roll, -reach), Max = new Vector3(bed.Across, bed.Roof, DoorZone), Damage = SwingDamage });
            world.Hazards.Add(new MachineHazard { ToLocal = toTunnel, Min = new Vector3(0, bed.Roll, bed.Along - DoorZone), Max = new Vector3(bed.Across, bed.Roof, bed.Along + reach), Damage = SwingDamage });
        }
        else
            world.Hazards.Add(new MachineHazard { ToLocal = toTunnel, Min = new Vector3(Wall, bed.Top, DoorZone * 0.5f), Max = new Vector3(bed.Across - Wall, bed.Roof, bed.Along - DoorZone * 0.5f), Damage = BurnDamage }); // (shut in with the bake)
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
        OvenStall.NoMasts or OvenStall.NoRoom => "No hearth",
        OvenStall.Empty => "Tunnel empty",
        OvenStall.Several => "One group only",
        OvenStall.Overhang => "Off the hearth",
        OvenStall.TooTall => "Too tall for tunnel",
        OvenStall.TooMany => "Too many subparts",
        OvenStall.Moving => "Settling",
        OvenStall.Done => "Done: roll it out",
        OvenStall.CantBake => "Can't bake that",
        OvenStall.NotBaked => "Not a baked part",
        OvenStall.Failed => "Refused: take it out",
        OvenStall.NoPower => "No power",
        _ => "Ready",
    };

    /// <summary>The Core's screen (MachineScreens.Fill): what it is doing, its mode and bed, its tunnel, and the cycle's bar.</summary>
    public static void Screen(Construct c, int part, CStore st, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var phase = (OvenPhase)st.Blocked;
        if (st.Busy || phase != OvenPhase.Open)
        {
            card.State = PanelState.Working;
            card.Add(0, phase switch { OvenPhase.Drop => "Doors shutting", OvenPhase.Heat => "Baking", OvenPhase.Cool => "Cooling", OvenPhase.Lift => "Doors opening", _ => "Working" });
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
            card.Add(1, (int)MathF.Round(bed.Across / bed.Cell)).Add(1, 'x').Add(1, (int)MathF.Round(bed.Along / bed.Cell)).Add(1, " tunnel");
            var dm = (int)MathF.Round(bed.Clear * 10);
            card.Add(2, "Clear ").Add(2, dm / 10).Add(2, '.').Add(2, dm % 10).Add(2, " m, ").Add(2, MaxParts(d, bed.Blocks)).Add(2, " parts");
        }
        else
            card.Add(1, "no bed");
        card.Bar = st.Busy || phase != OvenPhase.Open ? st.Progress / 100f : 0f;
    }
}
