using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a Smelter's works go across its squares (docs/sandbox/constructs/production.md "Refiners as block machines", "Where the works
/// go"): one rule that SmelterRender draws and <see cref="AddHazards"/> burns by, every peer alike, from the squares the split made
/// (MachineBlocks.Squares). All in the Core's part frame: cell (x, z) is the Core's own lattice (its "origin" plus x and z cells), the
/// floor is the Core's foot.
/// <list type="bullet">
/// <item>The front is -z (the Core's screen side). The runner is one line <see cref="RunnerOut"/> in front of the frontmost square's
/// face; each square's taphole is in the column of its front row nearest its middle whose cells down to that line are free (none free:
/// it taps into a covered launder, not drawn), with a spur out to the line. The runner ends at the Casting Bed nearest the front line
/// (the furthest along x of those), with a short lip into its sow; with no bed it ends past the last square's far edge.</item>
/// <item>Each square of 2 or more has a stack for each column on its back row, its top 1 + n m over the furnace top (a 1 x 1 is a lone
/// block: its own stub stack).</item>
/// <item>The charging belt leaves the Core's hopper spout up onto the tops along the Core's row, as far as the furthest square; a square
/// off that row gets a cross belt from it, on the cell joint by its middle (stacks stand on the column middles). A belt over an empty cell
/// stands on a post.</item>
/// <item>Each square's hot-blast main runs along its front at <see cref="BlastY"/> over its free front columns, a tuyere down into each,
/// and rises over the roof at its -x end (the stoves' duct).</item>
/// </list>
/// Allocates nothing.
/// </summary>
public static class SmelterWorks
{
    public const float HearthTop = 3f, BedTop = 0.42f; // a Hearth Block's top and a Casting Bed's sand (SmelterParts draws them)
    public const float RunnerOut = 0.45f, RunnerHalf = 0.16f, RunnerY0 = 0.26f, RunnerY1 = 0.46f, TapY = 0.4f;
    public const float BlastY = 2.62f, BlastOut = 0.34f, BlastHalf = 0.13f;
    public const float BeltY = 3.32f, BeltZ = -0.3f, BeltHalf = 0.3f;
    public const float StackZ = 0.45f; // a stack's middle behind its column's cell middle (as a lone block's stub)
    private const int BurnDamage = 1; // per 60 Hz tick in a hot box (GameMachineHazards): the charge doors, the runner, the beds
    private const int MostHazards = 60; // (the shared list's cap, as the Assemblers')

    /// <summary>A Smelter Core's lattice in its own frame.</summary>
    public readonly struct Grid(ConstructPartDef d)
    {
        public readonly float Cell = d.BlockCell, Half = d.BlockCell * 0.5f, OX = (d.BlockOrigin ?? Vector3.Zero).X, OZ = (d.BlockOrigin ?? Vector3.Zero).Z,
            Floor = (d.BlockOrigin ?? Vector3.Zero).Y - d.BlockCell * 0.5f;
        public float X(int x) => OX + x * Cell; // a cell's middle
        public float Z(int z) => OZ + z * Cell;
    }

    /// <summary>Whether a Core is a smelter whose works these are.</summary>
    public static bool Is(ConstructPartDef d) => d is { BlockSquares: true, IsRefiner: true } && d.RefineMachine == "smelter";

    /// <summary>Whether a square or a Casting Bed of the machine takes cell (x, z).</summary>
    public static bool Taken(ReadOnlySpan<CMachineSquare> list, int x, int z)
    {
        foreach (ref readonly var q in list)
        {
            if (x >= q.X0 && x < q.X0 + q.N && z >= q.Z0 && z < q.Z0 + q.N)
                return true;
            if (x == q.X0 + q.N && z >= q.Z0 && z < q.Z0 + q.N && (q.Ends >> (z - q.Z0) & 1) != 0)
                return true;
        }
        return false;
    }

    /// <summary>The frontmost row any square has (its lowest z).</summary>
    public static int FrontRow(ReadOnlySpan<CMachineSquare> list)
    {
        var z = int.MaxValue;
        foreach (ref readonly var q in list)
            z = Math.Min(z, q.Z0);
        return z;
    }

    /// <summary>The runner's line (z) in front of the frontmost square.</summary>
    public static float RunnerZ(in Grid g, ReadOnlySpan<CMachineSquare> list) => g.Z(FrontRow(list)) - g.Half - RunnerOut;

    /// <summary>A square's taphole column (-1: none free to the front line).</summary>
    public static int TapColumn(ReadOnlySpan<CMachineSquare> list, in CMachineSquare q)
    {
        var front = FrontRow(list);
        var mid = q.X0 + (q.N - 1) / 2;
        for (var k = 0; k < 2 * q.N; k++)
        {
            var col = mid + ((k & 1) == 0 ? k / 2 : -(k + 1) / 2); // (the middle, then either side of it)
            if (col < q.X0 || col >= q.X0 + q.N)
                continue;
            var free = true;
            for (var z = front; z < q.Z0 && free; z++)
                free = !Taken(list, col, z);
            if (free)
                return col;
        }
        return -1;
    }

    /// <summary>Where the runner ends: x and z of its lip into the bed's sow (or its end past the last square), and whether it is a bed.</summary>
    public static bool RunnerEnd(in Grid g, ReadOnlySpan<CMachineSquare> list, out float x, out float z)
    {
        int bx = int.MinValue, bz = int.MaxValue;
        var far = float.MinValue;
        foreach (ref readonly var q in list)
        {
            far = MathF.Max(far, g.X(q.X0 + q.N - 1) + g.Half);
            if (q.Ends == 0)
                continue;
            var k = q.Z0 + System.Numerics.BitOperations.TrailingZeroCount(q.Ends);
            if (k < bz || k == bz && q.X0 + q.N > bx)
                (bx, bz) = (q.X0 + q.N, k);
        }
        if (bz != int.MaxValue)
        {
            (x, z) = (g.X(bx), g.Z(bz) - 0.1f);
            return true;
        }
        (x, z) = (far + 0.6f, RunnerZ(g, list));
        return false;
    }

    /// <summary>A tap's path for square <paramref name="q"/>: the taphole, the line, along it, into the bed (Core frame, y not set); its length.</summary>
    public static float TapPath(in Grid g, ReadOnlySpan<CMachineSquare> list, in CMachineSquare q, int col, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3)
    {
        var line = RunnerZ(g, list);
        RunnerEnd(g, list, out var ex, out var ez);
        p0 = new Vector2(g.X(col), g.Z(q.Z0) - g.Half);
        p1 = new Vector2(p0.X, line);
        p2 = new Vector2(ex, line);
        p3 = new Vector2(ex, ez);
        return Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
    }

    /// <summary>A square's tap period (s): a bigger furnace taps less often, each on its own clock.</summary>
    public static float TapPeriod(in CMachineSquare q) => 4f + q.N;

    // ---- The burns (server: ConstructBakeOven.Step refreshes the list) ------------------------------------------------------------

    /// <summary>
    /// Every working Smelter's hot boxes into <see cref="ConstructWorld.Hazards"/>: a band in front of each square's charge doors, the runner
    /// (spurs, line and lip) and every Casting Bed. A lone Core has none: its hearth is behind a door. At most <see cref="MostHazards"/>.
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
                var g = new Grid(p.D);
                var list = MachineBlocks.Squares(c, asm);
                var line = RunnerZ(g, list);
                RunnerEnd(g, list, out var ex, out var ez);
                var x0 = float.MaxValue;
                foreach (ref readonly var q in list)
                {
                    float xa = g.X(q.X0) - g.Half, xb = g.X(q.X0 + q.N - 1) + g.Half, za = g.Z(q.Z0) - g.Half;
                    Add(world, toCore, new Vector3(xa, g.Floor + 1.3f, za - 0.6f), new Vector3(xb, g.Floor + 2.5f, za)); // (the charge doors' heat)
                    for (var k = 0; k < q.N; k++)
                        if (((q.Ends >> k) & 1) != 0)
                        {
                            float bx = g.X(q.X0 + q.N), bz = g.Z(q.Z0 + k);
                            Add(world, toCore, new Vector3(bx - g.Half, g.Floor, bz - g.Half), new Vector3(bx + g.Half, g.Floor + 1f, bz + g.Half)); // (a Casting Bed: it burns while the machine works, runner or not)
                        }
                    var col = TapColumn(list, q);
                    if (col < 0)
                        continue; // (fronted by another square: no taphole of its own, so no spur)
                    var tx = g.X(col);
                    x0 = MathF.Min(x0, tx);
                    Add(world, toCore, new Vector3(tx - 0.4f, g.Floor, line - 0.4f), new Vector3(tx + 0.4f, g.Floor + 0.9f, za)); // (its spur)
                }
                if (x0 < float.MaxValue)
                {
                    Add(world, toCore, new Vector3(x0 - 0.4f, g.Floor, line - 0.4f), new Vector3(ex + 0.4f, g.Floor + 0.9f, line + 0.4f)); // (the line)
                    Add(world, toCore, new Vector3(ex - 0.4f, g.Floor, MathF.Min(line, ez)), new Vector3(ex + 0.4f, g.Floor + 0.9f, MathF.Max(line, ez))); // (its lip)
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
