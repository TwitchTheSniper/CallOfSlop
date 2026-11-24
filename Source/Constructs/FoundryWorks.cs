using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a Foundry's works go across its squares (docs/sandbox/constructs/production.md "Refiners as block machines", "The Foundry"):
/// one rule that FoundryRender draws and <see cref="AddHazards"/> burns by, every peer alike, from the squares the split made
/// (MachineBlocks.Squares, laid out on SmelterWorks.Grid: the Core's own lattice in its part frame, the floor at its foot).
/// <list type="bullet">
/// <item>Each square is one crucible over its middle, <see cref="Radius"/> n - 0.2 m (2n - 0.4 across), on trunnions along x; it tips
/// toward the front (-z) to pour on its own clock (<see cref="TiltShare"/>: a bigger one pours longer), its lip's spout reaching over the
/// launder. A 1 x 1's crucible is its Crucible Block's own ("alone" boxes, "work": "hinge"), on the same clock.</item>
/// <item>The launder is one trough on trestles <see cref="LaunderOut"/> in front of the frontmost square, at <see cref="LaunderY0"/> to
/// <see cref="LaunderY1"/>, from the first crucible's lip to the Mould Line nearest the front (SmelterWorks.RunnerEnd: the furthest along x
/// of those), where a lip runs it into the first mould; with no Mould Line it ends in a ladle past the last square. A square behind
/// another pours into the launder by a spur when the cells in front of its middle are free, else into a covered launder (not drawn).</item>
/// <item>One hood on four posts over the whole bed and its Mould Lines, its eaves <see cref="EavesOver"/> m over the biggest square's n,
/// one collared stack to <see cref="StackTop"/> m.</item>
/// </list>
/// Allocates nothing.
/// </summary>
public static class FoundryWorks
{
    public const float PitTop = 1.6f, MouldTop = 1f; // a Crucible Block's top and a Mould Line's (FoundryParts draws them)
    public const float LaunderOut = 0.3f, LaunderHalf = 0.18f, LaunderY0 = 1.12f, LaunderY1 = 1.34f;
    public const float EavesOver = 3f, StackTop = 12f, HoodOut = 0.3f, HoodFront = 0.85f;
    public const float CrucibleY0 = 1.64f, TiltDegrees = 30f; // (a crucible's lowest bottom, and how far a 1 x 1's tips: Bottom, Tilt)
    private const int BurnDamage = 1; // per 60 Hz tick in a hot box (GameMachineHazards): the pours, the launder, the Mould Lines
    private const int MostHazards = 60; // (the shared list's cap, as the Assemblers')

    /// <summary>Whether a Core is a foundry whose works these are.</summary>
    public static bool Is(ConstructPartDef d) => d is { BlockSquares: true, IsRefiner: true } && d.RefineMachine == "foundry";

    // ---- A square's crucible ----

    /// <summary>A crucible's radius over an n x n square, at its rim: 2n - 0.4 m across (0.8 on a 1 x 1, the Crucible Block's own). It tapers to
    /// <see cref="TaperBottom"/> of that at its foot.</summary>
    public static float Radius(int n) => n - 0.2f;

    /// <summary>A crucible's foot against its rim (it widens about 15% to the top), and its middle course's.</summary>
    public const float TaperBottom = 1f / 1.15f, TaperMiddle = 1f / 1.07f;

    /// <summary>Its height, bottom to rim (0.96 m on a 1 x 1).</summary>
    public static float Height(int n) => 0.96f + 0.6f * (n - 1);

    /// <summary>How far it tips toward the front to pour (degrees): <see cref="TiltDegrees"/> on a 1 x 1, less on a bigger one, whose lip drops further.</summary>
    public static float Tilt(int n) => TiltDegrees / MathF.Sqrt(Math.Max(1, n));

    private const float TrunnionShare = 0.58f, Clear = 0.05f; // (the trunnions' height up the crucible; the tipped shell's least gap over the pit's top)

    /// <summary>
    /// Its bottom over the floor: raised on its trunnions so that, tipped by <see cref="Tilt"/>, its front foot stays <see cref="Clear"/> over the
    /// pit's top (its rim's radius, the most it could reach; 1.975 m on a 1 x 1).
    /// </summary>
    public static float Bottom(int n)
    {
        var (s, k) = MathF.SinCos(Tilt(n) * MathF.PI / 180f);
        var drop = Radius(n) * s - TrunnionShare * Height(n) * (1f - k); // (how far the front foot falls below the upright bottom)
        return MathF.Max(CrucibleY0, PitTop + Clear + drop);
    }

    /// <summary>The trunnions' height over the floor: the line it tips about.</summary>
    public static float Trunnion(int n) => Bottom(n) + Height(n) * TrunnionShare;

    /// <summary>How far its lip's spout reaches past the rim, toward the front: so that, tipped, the lip is over the launder's middle.</summary>
    public static float Spout(int n)
    {
        var (s, k) = MathF.SinCos(Tilt(n) * MathF.PI / 180f);
        var dy = Height(n) * (1f - TrunnionShare) - 0.05f;
        return (n + LaunderOut - dy * s) / k - Radius(n);
    }

    /// <summary>A square's pour cycle (s): it tips, pours, comes back and rests, a bigger one pouring longer.</summary>
    public static float Period(int n) => 7f + n;

    private static float Pour(int n) => 1.5f + 0.4f * n;

    /// <summary>A square's own clock offset (s), from its corner cell: each crucible pours on its own clock, every peer alike.</summary>
    public static float Phase(int x0, int z0) => x0 * 1.7f + z0 * 2.9f;

    /// <summary>How far a square's crucible is tipped now, 0 (upright) to 1 (<see cref="TiltDegrees"/>), and whether it pours.</summary>
    public static float TiltShare(double now, int n, int x0, int z0, out bool pouring)
    {
        var t = (float)((now + Phase(x0, z0)) % Period(n));
        if (t < 0)
            t += Period(n);
        const float up = 1.5f, down = 1.5f;
        var pour = Pour(n);
        pouring = t >= up && t < up + pour;
        if (t < up)
            return Ease(t / up);
        if (t < up + pour)
            return 1f;
        if (t < up + pour + down)
            return 1f - Ease((t - up - pour) / down);
        return 0f;
    }

    private static float Ease(float u) => u * u * (3f - 2f * u);

    /// <summary>
    /// A square's lip in the Core's frame (its middle x; z and y tipped by <paramref name="share"/>): where the pour leaves it. The crucible's
    /// middle is at (<paramref name="cx"/>, <paramref name="cz"/>).
    /// </summary>
    public static Vector3 Lip(int n, float cx, float cz, float floor, float share)
    {
        var a = share * Tilt(n) * MathF.PI / 180f;
        float dz = -(Radius(n) + Spout(n)), dy = Bottom(n) + Height(n) - 0.05f - Trunnion(n);
        var (s, k) = MathF.SinCos(a);
        return new Vector3(cx, floor + Trunnion(n) + dz * s + dy * k, cz + dz * k - dy * s);
    }

    /// <summary>A square's middle (x, z) in the Core's frame.</summary>
    public static Vector2 Middle(in SmelterWorks.Grid g, in CMachineSquare q) => new(g.X(q.X0) - g.Half + q.N * g.Half, g.Z(q.Z0) - g.Half + q.N * g.Half);

    // ---- The launder ----

    /// <summary>The launder's line (z) in front of the frontmost square.</summary>
    public static float LaunderZ(in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list) => g.Z(SmelterWorks.FrontRow(list)) - g.Half - LaunderOut;

    /// <summary>Whether a square pours into the launder in view: in the front row, or the cells in front of its middle free (else a covered launder).</summary>
    public static bool Open(ReadOnlySpan<CMachineSquare> list, in CMachineSquare q)
    {
        var front = SmelterWorks.FrontRow(list);
        int c0 = q.X0 + (q.N - 1) / 2, c1 = q.X0 + q.N / 2;
        for (var z = front; z < q.Z0; z++)
            if (SmelterWorks.Taken(list, c0, z) || SmelterWorks.Taken(list, c1, z))
                return false;
        return true;
    }

    // ---- The hood ----

    /// <summary>The bed's outline with its Mould Lines (Core frame, x and z), and the hood's eaves over the floor (the biggest square's n + 3 m).</summary>
    public static float Hood(in SmelterWorks.Grid g, ReadOnlySpan<CMachineSquare> list, out float x0, out float x1, out float z0, out float z1)
    {
        (x0, x1, z0, z1) = (float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
        var most = 1;
        foreach (ref readonly var q in list)
        {
            most = Math.Max(most, q.N);
            x0 = MathF.Min(x0, g.X(q.X0) - g.Half);
            x1 = MathF.Max(x1, g.X(q.X0 + q.N - 1 + (q.Ends != 0 ? 1 : 0)) + g.Half);
            z0 = MathF.Min(z0, g.Z(q.Z0) - g.Half);
            z1 = MathF.Max(z1, g.Z(q.Z0 + q.N - 1) + g.Half);
        }
        (x0, x1, z0, z1) = (x0 - HoodOut, x1 + HoodOut, z0 - HoodFront, z1 + HoodOut);
        return most + EavesOver;
    }

    // ---- The burns (server: ConstructBakeOven.Step refreshes the list) ------------------------------------------------------------

    /// <summary>
    /// Every working Foundry's hot boxes into <see cref="ConstructWorld.Hazards"/>: the pour under each crucible's lip, the launder (spurs,
    /// line and lip) and every Mould Line. A lone Core has none: its melt is behind a sight glass. At most <see cref="MostHazards"/>.
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
                var list = MachineBlocks.Squares(c, asm);
                var line = LaunderZ(g, list);
                SmelterWorks.RunnerEnd(g, list, out var ex, out var ez);
                var x0 = float.MaxValue;
                float y0 = g.Floor + LaunderY0 - 0.3f, y1 = g.Floor + LaunderY1 + 0.4f;
                foreach (ref readonly var q in list)
                {
                    for (var k = 0; k < q.N; k++)
                        if (((q.Ends >> k) & 1) != 0)
                        {
                            float bx = g.X(q.X0 + q.N), bz = g.Z(q.Z0 + k);
                            Add(world, toCore, new Vector3(bx - g.Half, g.Floor, bz - g.Half), new Vector3(bx + g.Half, g.Floor + MouldTop + 0.6f, bz + g.Half)); // (a Mould Line: it burns while the machine works)
                        }
                    if (!Open(list, q))
                        continue; // (fronted by another square: its pour is in a covered launder)
                    var mid = Middle(g, q);
                    var lip = Lip(q.N, mid.X, mid.Y, g.Floor, 1f);
                    x0 = MathF.Min(x0, mid.X);
                    Add(world, toCore, new Vector3(mid.X - 0.45f, y0, MathF.Min(line, lip.Z) - 0.4f), new Vector3(mid.X + 0.45f, lip.Y + 0.2f, g.Z(q.Z0) - g.Half)); // (its pour and spur)
                }
                if (x0 < float.MaxValue)
                {
                    Add(world, toCore, new Vector3(x0 - 0.4f, y0, line - 0.45f), new Vector3(ex + 0.4f, y1, line + 0.45f)); // (the launder)
                    Add(world, toCore, new Vector3(ex - 0.4f, y0, MathF.Min(line, ez)), new Vector3(ex + 0.4f, y1, MathF.Max(line, ez))); // (its lip)
                }
            }
        }
    }

    private static void Add(ConstructWorld world, in Matrix4x4 toCore, Vector3 min, Vector3 max)
    {
        if (world.Hazards.Count < MostHazards)
            world.Hazards.Add(new MachineHazard { ToLocal = toCore, Min = min, Max = max, Damage = BurnDamage });
    }
}
