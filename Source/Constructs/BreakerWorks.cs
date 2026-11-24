using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Scrap Breaker (docs/sandbox/constructs/production.md "The Scrap Breaker"; the design "Smelters and refiners as block machines",
/// drawing "Scrap Breaker"): the block version of the Recycler, beside it. A Breaker Core with its own 1.6 m mouth and Shredder Blocks
/// welded on its +x side, split into squares (MachineBlocks.Squares, laid out on SmelterWorks.Grid: the Core's own lattice in its part frame,
/// the floor at its foot); each square is one shredder. This is the one rule ConstructRecycler takes wrecks by, BreakerParts builds a lone
/// Shredder Block's own rotors by (at n 1), BreakerRender draws and <see cref="AddHazards"/> hurts by, every peer alike. The front is -z
/// (the Core's screen side).
/// <list type="bullet">
/// <item>The mouth is the whole bed: the Core's own intake box and every square's top. A wreck is taken in when its middle lands over any
/// of them (<see cref="InMouth"/>), so a bigger bed catches bigger wrecks. Each block is solid to <see cref="Lid"/> (a hidden box: the
/// wreck rests on the rotors' teeth, under the rim at <see cref="Rim"/>).</item>
/// <item>Each square is one pair of toothed rotors along x, side by side in z about its middle (<see cref="RotorR"/>, <see cref="Gap"/>),
/// counter-turning while it shreds; bigger on a bigger square. Under them a belt along z runs to the square's front face and out onto the
/// collecting belt along the bed's front (<see cref="CollectorZ"/>), which runs to the Core's front and into its bin.</item>
/// <item>One wreck at a time at rate r: min(15, 2 + 0.04 kg) s for each <see cref="UnitsPerJob"/> units of yield (never less than one),
/// over r (<see cref="JobTicks"/>). A bare Core works at the Recycler's speed.</item>
/// </list>
/// Allocates nothing.
/// </summary>
public static class BreakerWorks
{
    public const float Rim = 2f, Lid = 1.7f, BaseTop = 0.3f; // a Shredder Block's rim, the top of its solid (where a wreck rests), its pit floor (BreakerParts draws them)
    public const float BeltY0 = 0.302f, BeltY1 = 0.36f; // the belt under the rotors
    public const float MouthTop = 3.6f; // the mouth's reach over the floor: a wreck's middle under it, over a square or the Core's mouth, is taken in
    public const float CollectorOut = 0.4f, CollectorHalf = 0.3f, CollectorY0 = 0.06f, CollectorY1 = 0.2f; // the collecting belt along the bed's front
    public const float CoreInletX = 0.55f; // where the collecting belt enters the Core's front (its +x half, under the screen)
    public const float SkirtOut = 0.55f, SkirtUp = 0.6f; // the funnel skirt round the bed: how far it flares out from the rim, and up
    public const int UnitsPerJob = 600; // the yield a job takes its full time for (the Recycler's bin, and a bare Core's)
    private const int RotorDamage = 2; // per 60 Hz tick in a rotor box (GameMachineHazards)
    private const int MostHazards = 60; // (the shared list's cap, as the Assemblers')

    /// <summary>Whether a Core is a Scrap Breaker's.</summary>
    public static bool Is(ConstructPartDef d) => d.IsBreaker;

    // ---- A square's shredder ----

    /// <summary>A rotor's drum radius (0.3 m on a 1 x 1, 0.5 on a 5 x 5).</summary>
    public static float RotorR(int n) => 0.25f + 0.05f * n;

    /// <summary>How far a rotor's teeth reach from its axis.</summary>
    public static float Teeth(int n) => 1.25f * RotorR(n);

    /// <summary>The rotors' axis over the floor: their teeth's tops 4 cm under the lid (the wreck rests on them).</summary>
    public static float AxisY(int n) => Lid - 0.04f - Teeth(n);

    /// <summary>Each rotor's axis this far either side (z) of its square's middle: the teeth of the two mesh.</summary>
    public static float Gap(int n) => 1.05f * RotorR(n);

    /// <summary>A rotor's length along x: the square's width less a bearing block at each end.</summary>
    public static float RotorLen(int n) => 2f * n - 0.7f;

    /// <summary>Rows of teeth along a rotor.</summary>
    public static int TeethRows(int n) => 2 * n + 1;

    /// <summary>The rotors' turn (rad/s) while it shreds: the back one the other way.</summary>
    public const float Spin = 2.2f;

    /// <summary>The collecting belt's line (z) in front of the frontmost square (and never behind the Core's front).</summary>
    public static float CollectorZ(in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list) =>
        MathF.Min(g.Z(Math.Min(0, SmelterWorks.FrontRow(list))) - g.Half - CollectorOut, g.OZ - g.Half - CollectorOut);

    /// <summary>A square's middle in the Core's frame (x, z).</summary>
    public static Vector2 Middle(in SmelterWorks.Grid g, in CMachineSquare q) => new(g.X(q.X0) - g.Half + q.N * g.Half, g.Z(q.Z0) - g.Half + q.N * g.Half);

    // ---- The mouth and the job (server: ConstructRecycler) ----

    /// <summary>
    /// Is a world point over a Breaker Core's mouth: its own intake box, or the top of one of its squares up to <see cref="MouthTop"/>?
    /// <paramref name="cx"/>, <paramref name="cz"/>: the cell it is over in the Core's cell frame (0, 0: the Core's own mouth).
    /// </summary>
    public static bool InMouth(Construct c, int core, Vector3 world, out sbyte cx, out sbyte cz)
    {
        (cx, cz) = (0, 0);
        if (ConstructRecycler.InIntake(c, core, world))
            return true;
        if (!MachineBlocks.TryOf(c, core, out var asm) || asm.Core != core || asm.SquareCount == 0)
            return false;
        ref var p = ref c.Parts[core];
        c.WorldFrame(p.Seg, out var q, out var t);
        var local = Rot24.Apply(Rot24.Inverse(p.Rot), Vector3.Transform(world - t, Quaternion.Conjugate(q)) - p.Pos); // (in the Core's own frame)
        var g = new SmelterWorks.Grid(p.D);
        if (local.Y < g.Floor + BaseTop || local.Y > g.Floor + MouthTop)
            return false;
        foreach (ref readonly var sq in MachineBlocks.Squares(c, asm))
        {
            float x0 = g.X(sq.X0) - g.Half, z0 = g.Z(sq.Z0) - g.Half, span = sq.N * g.Cell;
            if (local.X < x0 || local.X > x0 + span || local.Z < z0 || local.Z > z0 + span)
                continue;
            cx = (sbyte)Math.Clamp((int)MathF.Floor((local.X - x0) / g.Cell) + sq.X0, sq.X0, sq.X0 + sq.N - 1);
            cz = (sbyte)Math.Clamp((int)MathF.Floor((local.Z - z0) / g.Cell) + sq.Z0, sq.Z0, sq.Z0 + sq.N - 1);
            return true;
        }
        return false;
    }

    /// <summary>A Breaker's job in ticks: min(15, 2 + 0.04 kg) s for each 600 units of yield (at least one), over its rate (four times it: <paramref name="r4"/>).</summary>
    public static int JobTicks(RecyclingTable table, float mass, int amount, int r4)
    {
        var seconds = table.SecondsFor(mass) * MathF.Max(1f, amount / (float)UnitsPerJob) * 4f / Math.Max(4, r4);
        return Math.Max(1, (int)MathF.Ceiling(seconds * World.TickRate));
    }

    /// <summary>The square that holds cell (x, z) of the Core's frame, or -1 (the Core's own mouth, or none).</summary>
    public static int SquareAt(ReadOnlySpan<CMachineSquare> list, int x, int z)
    {
        for (var k = 0; k < list.Length; k++)
            if (x >= list[k].X0 && x < list[k].X0 + list[k].N && z >= list[k].Z0 && z < list[k].Z0 + list[k].N)
                return k;
        return -1;
    }

    // ---- The rotors hurt (server: ConstructBakeOven.Step refreshes the list) -------------------------------------------------------

    /// <summary>
    /// Every working Breaker's rotor boxes into <see cref="ConstructWorld.Hazards"/>: over each square, from under its rotors to a metre over
    /// its rim (a player on the mouth stands in it), a 1 x 1's too. The Core's own mouth has none: nothing on it moves. At most <see cref="MostHazards"/>.
    /// </summary>
    public static void AddHazards(ConstructWorld world)
    {
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
            if (!c.HasRecycler)
                continue;
            for (var a = 0; a < c.MachineAsmCount; a++)
            {
                ref readonly var asm = ref c.MachineAsms[a];
                if (asm.SquareCount == 0 || asm.Core >= c.PartCount || world.Hazards.Count >= MostHazards)
                    continue;
                ref readonly var p = ref c.Parts[asm.Core];
                if (p.Broken || !Is(p.D) || world.StoreOf(c, asm.Core) is not { } st || !st.Busy || st.Held || !ConstructRecycler.Powered(world, c, asm.Core)
                    || !Matrix4x4.Invert(ConstructBakeOven.CoreFrame(c, asm.Core), out var toCore))
                    continue;
                var g = new SmelterWorks.Grid(p.D);
                foreach (ref readonly var q in MachineBlocks.Squares(c, asm))
                {
                    var n = q.N;
                    var m = Middle(g, q);
                    var h = n * g.Half - 0.08f;
                    if (world.Hazards.Count < MostHazards)
                        world.Hazards.Add(new MachineHazard
                        {
                            ToLocal = toCore, Damage = RotorDamage,
                            Min = new Vector3(m.X - h, g.Floor + AxisY(n) - Teeth(n), m.Y - h), Max = new Vector3(m.X + h, g.Floor + Rim + 1f, m.Y + h),
                        });
                }
            }
        }
    }
}
