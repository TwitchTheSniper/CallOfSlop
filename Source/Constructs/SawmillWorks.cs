using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a Sawmill's works go across its squares (docs/sandbox/constructs/production.md "Refiners as block machines", "The Sawmill"):
/// one rule that SawmillRender draws, SawmillParts builds a lone Saw Block's own band mill by (at n 1) and <see cref="AddHazards"/> cuts
/// by, every peer alike, from the squares the split made (MachineBlocks.Squares, laid out on SmelterWorks.Grid: the Core's own lattice in
/// its part frame, the floor at its foot). The front is -z (the Core's screen side).
/// <list type="bullet">
/// <item>Each square is one band mill. Across it from front to back (z): the log on its carriage (the log's front <see cref="ZFront"/>), the
/// band's cutting run (<see cref="ZCut"/>) which slices a board off the log's front as the carriage strokes it along x, the band's back
/// run behind its guard, and the headrig's column behind that, <see cref="Top"/> (3 + n m) tall, holding the upper wheel (radius
/// <see cref="WheelR"/>, its axle along x) on two arms over the log; the lower wheel is in the pit under the deck. The carriage rides two
/// rails along x behind the band, so neither it nor its log meets the column, the arms, the guide or the back run at any pose.</item>
/// <item>The carriage strokes on its square's own clock (<see cref="CarriageShare"/>): slowly through the band (the cut), quickly back.
/// A bigger square takes a longer, thicker log (<see cref="Length"/>, <see cref="LogR"/>).</item>
/// <item>Behind the column, a log deck of up to three logs waiting on skids (n of 2 or more).</item>
/// <item>The plank chain is one line <see cref="ChainOut"/> in front of the frontmost square, at <see cref="ChainY0"/> to
/// <see cref="ChainY1"/>, from the first square to the Plank Stacker nearest the front (SmelterWorks.RunnerEnd: the furthest along x of
/// those), with a lip onto it; with no Stacker it ends past the last square. Each square's boards come to it on a transfer from the band,
/// when the cells in front of its middle are free (FoundryWorks.Open), else under a covered way (not drawn).</item>
/// <item>A sawdust duct rises from each headrig's top to <see cref="DuctY"/> and runs to a trunk along x over the Core's row into the
/// cyclone on the Core (<see cref="CycloneX"/>, <see cref="CycloneZ"/>: the Core's own boxes).</item>
/// </list>
/// Allocates nothing.
/// </summary>
public static class SawmillWorks
{
    public const float BlockTop = 1f, Deck = 1.012f, StackerTop = 0.42f; // a Saw Block's top and its deck plate's, a Plank Stacker's deck (SawmillParts draws them)
    public const float RailY0 = 1.05f, RailY1 = 1.1f, CarriageY0 = 1.16f, CarriageY1 = 1.32f; // the rails, the carriage's frame (bottom, the bolsters' top: the log's seat)
    public const float ChainOut = 0.35f, ChainHalf = 0.16f, ChainY0 = 0.5f, ChainY1 = 0.62f;
    public const float CycloneX = 0.45f, CycloneZ = 0.4f, CycloneR = 0.42f, InletY = 6.4f, InletEnd = 1.1f, CycloneTop = 7.6f; // (the Core's cyclone: SawmillParts)
    private const float DuctLow = 6.4f, DuctOver = 0.5f;
    private const int CutDamage = 2; // per 60 Hz tick in a cut box (GameMachineHazards): the bands and the carriages
    private const int MostHazards = 60; // (the shared list's cap, as the Assemblers')

    /// <summary>Whether a Core is a sawmill whose works these are.</summary>
    public static bool Is(ConstructPartDef d) => d is { BlockSquares: true, IsRefiner: true } && d.RefineMachine == "sawmill";

    // ---- A square's band mill ----

    /// <summary>A log's radius on an n x n square (0.3 m on a 1 x 1, 0.7 on a 5 x 5): a bigger square takes a bigger log.</summary>
    public static float LogR(int n) => 0.2f + 0.1f * n;

    /// <summary>The band wheels' radius (0.5 m on a 1 x 1, 1.1 on a 5 x 5).</summary>
    public static float WheelR(int n) => 0.35f + 0.15f * n;

    /// <summary>Half a wheel's width along its axle (x), the band's width.</summary>
    public static float RimHalf(int n) => 0.07f + 0.015f * n;

    /// <summary>The headrig's top over the floor: 3 + n m (5 m on a 2 x 2).</summary>
    public static float Top(int n) => 3f + n;

    /// <summary>The carriage's and its log's length along x: the log passes wholly through the band in one stroke inside the square.</summary>
    public static float Length(int n) => n - 0.2f;

    /// <summary>How far the carriage goes in a stroke (n m: a log's length and 0.1 m either side of the band).</summary>
    public static float Travel(int n) => Length(n) + 0.2f;

    /// <summary>The carriage's depth (z) behind the band, its knees at its back holding the log.</summary>
    public static float CarDepth(int n) => MathF.Max(0.84f, 1.6f * LogR(n) + 0.2f);

    /// <summary>The upper wheel's middle over the floor: clear over the log, the guide and the arms.</summary>
    public static float WheelY(int n) => CarriageY1 + 2f * LogR(n) + 0.28f + WheelR(n);

    /// <summary>The headrig's depth, log front to the column's back.</summary>
    public static float Depth(int n) => 0.4f * LogR(n) + 2f * WheelR(n) + 0.55f;

    /// <summary>The log's front (z) in a square whose middle is <paramref name="mz"/>: 0.3 m in from the square's front, or centred on a 1 x 1.</summary>
    public static float ZFront(float mz, int n) => mz - n + MathF.Min(0.3f, (2f * n - Depth(n)) * 0.5f);

    /// <summary>The band's cutting run (z): it takes a board 0.4 of the log's radius off its front.</summary>
    public static float ZCut(float mz, int n) => ZFront(mz, n) + 0.4f * LogR(n);

    /// <summary>The log's middle (z) on its carriage.</summary>
    public static float LogZ(float mz, int n) => ZCut(mz, n) + 0.6f * LogR(n);

    /// <summary>The band's back run (z), behind its guard.</summary>
    public static float ZBack(float mz, int n) => ZCut(mz, n) + 2f * WheelR(n);

    /// <summary>The headrig's column (z, front and back), behind the back run.</summary>
    public static (float Z0, float Z1) Column(float mz, int n) => (ZBack(mz, n) + 0.1f, ZBack(mz, n) + 0.55f);

    /// <summary>Half the column's width along x: past the arms either side of the wheel.</summary>
    public static float ColumnHalf(int n) => RimHalf(n) + 0.16f;

    /// <summary>A square's stroke cycle (s): a bigger mill takes a longer log through.</summary>
    public static float Period(int n) => 4f + n;

    /// <summary>A square's own clock offset (s), from its corner cell.</summary>
    public static float Phase(int x0, int z0) => x0 * 1.3f + z0 * 2.3f;

    /// <summary>
    /// Where a square's carriage is in its stroke now: 0 (the log's far end short of the band) to 1 (the log past it); whether it cuts
    /// (going forward through the band, 0.7 of the cycle; back in 0.2, then a pause while the log is turned and set).
    /// </summary>
    public static float CarriageShare(double now, int n, int x0, int z0, out bool cutting)
    {
        var period = Period(n);
        var t = (float)((now + Phase(x0, z0)) % period);
        if (t < 0)
            t += period;
        var u = t / period;
        cutting = u < 0.7f;
        if (u < 0.7f)
            return u / 0.7f;
        if (u < 0.9f)
            return 1f - Ease((u - 0.7f) / 0.2f);
        return 0f;
    }

    private static float Ease(float u) => u * u * (3f - 2f * u);

    /// <summary>The carriage's middle (x) at stroke share <paramref name="share"/>, in a square whose middle is <paramref name="mx"/>.</summary>
    public static float CarriageX(float mx, int n, float share) => mx - Length(n) * 0.5f - 0.1f + share * Travel(n);

    // ---- The plank chain and the duct ----

    /// <summary>The plank chain's line (z) in front of the frontmost square.</summary>
    public static float ChainZ(in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list) => g.Z(SmelterWorks.FrontRow(list)) - g.Half - ChainOut;

    /// <summary>Whether a square's boards come to the chain in view (FoundryWorks.Open: in the front row, or the cells in front of its middle free).</summary>
    public static bool Open(ReadOnlySpan<CMachineSquare> list, in CMachineSquare q) => FoundryWorks.Open(list, q);

    /// <summary>The sawdust trunk's height: over every headrig, and at least the cyclone's inlet.</summary>
    public static float DuctY(ReadOnlySpan<CMachineSquare> list)
    {
        var y = DuctLow;
        foreach (ref readonly var q in list)
            y = MathF.Max(y, Top(q.N) + DuctOver);
        return y;
    }

    /// <summary>A duct's radius from a square's headrig.</summary>
    public static float DuctR(int n) => 0.13f + 0.02f * n;

    // ---- The cuts (server: ConstructBakeOven.Step refreshes the list) -------------------------------------------------------------

    /// <summary>
    /// Every working Sawmill's cut boxes into <see cref="ConstructWorld.Hazards"/>: each square's band (from the deck to its upper wheel)
    /// and its carriage's path with the log on it, a 1 x 1's too. A lone Core has none. At most <see cref="MostHazards"/>.
    /// </summary>
    public static void AddHazards(ConstructWorld world)
    {
        var bodies = world.Bodies;
        for (var ci = 0; ci < bodies.Count; ci++)
        {
            var c = bodies[ci];
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
                    float mx = g.X(q.X0) - g.Half + n * g.Half, mz = g.Z(q.Z0) - g.Half + n * g.Half, cut = ZCut(mz, n), rim = RimHalf(n);
                    Add(world, toCore, new Vector3(mx - rim - 0.15f, g.Floor + Deck, cut - 0.25f), new Vector3(mx + rim + 0.15f, g.Floor + WheelY(n), cut + 0.25f)); // (the band)
                    Add(world, toCore, new Vector3(mx - n * g.Half, g.Floor + Deck, ZFront(mz, n) - 0.15f), new Vector3(mx + n * g.Half, g.Floor + CarriageY1 + 2f * LogR(n) + 0.15f, cut + CarDepth(n) + 0.1f)); // (the carriage's path)
                }
            }
        }
    }

    private static void Add(ConstructWorld world, in Matrix4x4 toCore, Vector3 min, Vector3 max)
    {
        if (world.Hazards.Count < MostHazards)
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min, Max = max, Damage = CutDamage });
    }
}
