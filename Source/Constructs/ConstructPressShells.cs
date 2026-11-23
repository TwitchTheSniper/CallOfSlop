using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Press as two round dies (Sean, 2026-11-23, replacing the swinging walls and their flat roof of drawing 17b). The wall blocks
/// stacked on the bed's two long edges (its first and last rows along the Core's z) are fixed housings. Each wall column holds one
/// segment of a round die: a solid quarter-round, hinged along the foot of the housing's inner face. Open, it stands stowed in its
/// housing; closing, it rolls over the bed until the two dies meet over the bed's centre line as one half-round, over the blank. Each
/// stroke: the blank lies on the bed, the dies close on it in a quick ripple down the bed (one segment a cell along the flow, the Core's
/// x) and hold while it presses; the part is made at the stroke's end and the dies open on it. Between strokes the dies stand open.
/// <list type="bullet">
/// <item><b>Drawn only.</b> The dies, the blank and the new part on the bed are a function of the time into the stroke and since it
/// ended: a peer sees a stroke start when its store turns Busy or its progress falls back to the 0 each stroke sends at its start, and
/// end when Busy falls (<see cref="StrokeUpdate"/>); the server's crush test reads the stroke's work so far. Nothing new goes on the
/// wire. The housings' collision is where they were welded; the dies have none.</item>
/// <item><b>The swing strip.</b> An open die stands in its housing; one wider than its housing (a bed wider than four blocks) reaches
/// past it. That reach, beside each side, must be clear: at the machine's rebuild a fixed part of the same construct there (not one of
/// its own blocks) is kept as the machine's blocker (<see cref="SwingCheck"/>): the press doesn't work, the screen says "Shell swing
/// blocked" and the look line names the part.</item>
/// <item><b>No stopping for people.</b> The dies close on whoever is on the bed (the server's crush test: Game.SandboxPressCrush).</item>
/// </list>
/// Nothing here allocates; the swing check runs at the rebuild only and costs one pass over the construct's parts per press.
/// </summary>
public static partial class ConstructPress
{
    // A stroke's swing (Sean, 2026-11-23: the shells close on the part to transform it): from the stroke's start the segments swing open
    // flat out (OpenTo), stand open over the blank (SlamFrom), slam shut on it (SlamAt), then stay shut until the stroke ends and the part
    // is made. Each segment a little behind the one before it down the bed (Ripple, at most RippleMax of them). In seconds into the stroke.
    public const double OpenTo = 0.55, SlamFrom = 0.75, SlamAt = 1.0;
    private const double Ripple = 0.05;
    public const int RippleMax = 8;
    /// <summary>The shortest stroke, in press steps (10 Hz): the last segment is shut before the part is made.</summary>
    public const int StrokeMinSteps = (int)((SlamAt + Ripple * RippleMax) * 10) + 1;
    /// <summary>A creative stroke, in press steps: free, but it still takes its swing.</summary>
    public const int CreativeStrokeSteps = 20;

    /// <summary>A housing segment and its die: where the die's hinge is and which way it opens, in the body frame.</summary>
    public struct ShellPose
    {
        public Vector3 Hinge; // a point on the die's hinge line (the foot of the housing's inner face, on the bed)
        public Vector3 Centre; // the bed's centre line at this segment, on the bed
        public Vector3 Axis; // the hinge line's direction: a positive turn about it opens the die (up toward out)
        public Vector3 Out, Up, Along; // outward from the bed, the Core's up, along the flow
        public float Cell; // the block's size (m)
        public float Gap; // from the housing's inner face to the bed's centre line (m): the die's radius
        public int Index; // its place down the bed (0 at the Core's end): the ripple's order
        public int Core; // the press's Core
        public bool Top; // the top block of its column: it draws the die
    }

    /// <summary>Is <paramref name="part"/> a shell segment of a Press (a wall block on one of the bed's long edges)? Its pose if so.</summary>
    public static bool Shell(Construct c, int part, out ShellPose s)
    {
        s = default;
        if (c.Parts[part].D.BlockRole != BlockRole.Block || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0 || asm.L < 2
            || !MachineBlocks.CellOf(c, part, out var x, out var y, out var z) || y < 1 || x < asm.X0 || x >= asm.X0 + asm.W)
            return false;
        var core = asm.Core;
        if (!c.Parts[core].D.IsPress)
            return false;
        int sgn = z == asm.Z0 ? -1 : z == asm.Z0 + asm.L - 1 ? 1 : 0;
        if (sgn == 0)
            return false;
        var rot = c.Parts[core].Rot;
        var cell = c.Parts[core].D.BlockCell;
        var mid = MachineBlocks.CoreMid(c, core);
        s.Core = core;
        s.Cell = cell;
        s.Out = Rot24.Apply(rot, new Vector3(0, 0, sgn));
        s.Up = Rot24.Apply(rot, Vector3.UnitY);
        s.Along = Rot24.Apply(rot, Vector3.UnitX);
        s.Axis = Vector3.Cross(s.Up, s.Out); // (turning Up toward Out)
        s.Hinge = mid + Rot24.Apply(rot, new Vector3(x * cell, 0.5f * cell, (z - 0.5f * sgn) * cell));
        s.Centre = mid + Rot24.Apply(rot, new Vector3(x * cell, 0.5f * cell, (asm.Z0 + (asm.L - 1) * 0.5f) * cell));
        s.Gap = (asm.L - 2) * cell * 0.5f;
        s.Index = x - asm.X0;
        s.Top = MachineBlocks.ColumnAt(c, asm, x, z) == y;
        return true;
    }

    /// <summary>How far open (0 shut, 1 flat out) segment <paramref name="index"/> is <paramref name="seconds"/> into a stroke; shut while the press isn't working.</summary>
    public static float Openness(bool working, int index, double seconds)
    {
        if (!working)
            return 0;
        var ph = Phase(index, seconds);
        if (ph <= 0 || ph >= SlamAt)
            return 0;
        if (ph < OpenTo)
        {
            var t = ph / OpenTo;
            return (float)(t * t * (3 - 2 * t));
        }
        if (ph < SlamFrom)
            return 1;
        var u = (ph - SlamFrom) / (SlamAt - SlamFrom);
        return (float)(1 - u * u); // (gathering speed into the slam)
    }

    /// <summary>Where segment <paramref name="index"/> is in its swing, in seconds into the stroke (each segment down the bed a little behind).</summary>
    public static double Phase(int index, double seconds) => seconds - Math.Min(index, RippleMax) * Ripple;

    /// <summary>
    /// A press as this peer draws it at <paramref name="now"/> (ConstructRender.Now): notes a stroke's start (its store turns Busy, or its
    /// progress falls back to the 0 each stroke sends at its start: WorkSized) and end (Busy falls), and at a start what lies on the bed
    /// and what it will make (<see cref="StrokePieces"/>). Kept on the store; the same answer however often a frame asks. Allocates only
    /// at a stroke's start, when a pressed part's key is named.
    /// </summary>
    public static void StrokeUpdate(Construct c, int core, CStore st, double now)
    {
        if (st.StrokeAt == now)
            return;
        st.StrokeAt = now;
        if (st.Busy && (!st.StrokeOn || st.Progress < st.StrokeSeen))
        {
            st.StrokeFromRest = !st.StrokeOn && now - st.StrokeEnd > OpenTo; // (from standing open: the dies are already open over the bed)
            st.StrokeSince = now;
            st.StrokeLast = st.StrokeMade;
            StrokePieces(c, core, out st.StrokeBlank, out st.StrokeMade);
            if (st.StrokeBlank != null || st.StrokeMade != null)
                st.StrokeTall = MathF.Max(Tall(st.StrokeBlank), Tall(st.StrokeMade));
        }
        else if (!st.Busy && st.StrokeOn)
            st.StrokeEnd = now;
        st.StrokeOn = st.Busy;
        st.StrokeSeen = st.Progress;
    }

    private static float Tall(ConstructPartDef? d) => d == null ? 0 : d.BoundsMax.Y - d.BoundsMin.Y;

    /// <summary>How high the shut dies stand over the bed (m): room for the stroke's pieces (<see cref="CStore.StrokeTall"/>) and the die's face, at most <see cref="MaxLift"/>.</summary>
    public static float DieLift(CStore st, float radius) => Math.Clamp(st.StrokeTall + DieRelief(radius) + 0.03f, 0.1f, MaxLift(radius));

    /// <summary>The most the shut dies stand over the bed (m): half their radius, at most <see cref="LiftCap"/>.</summary>
    public static float MaxLift(float radius) => MathF.Max(0.1f, MathF.Min(radius * 0.5f, LiftCap));

    private const float LiftCap = 0.6f;

    /// <summary>How deep the fitted die's look stands off a round die's working face (m).</summary>
    public static float DieRelief(float radius) => Math.Clamp(radius * 0.05f, 0.04f, 0.15f);

    /// <summary>Seconds into the stroke under way, as this peer draws it (after <see cref="StrokeUpdate"/>).</summary>
    public static double StrokeSeconds(CStore st, double now) => now - st.StrokeSince;

    /// <summary>The blank the stroke under way presses and the part it makes (the next a press with a sized die would make now), or null.</summary>
    private static void StrokePieces(Construct c, int core, out ConstructPartDef? blank, out ConstructPartDef? made)
    {
        (blank, made) = (null, null);
        if (SizedDieOf(PressTable.Current, c, core) is not { } die)
            return;
        var bay = ReadyBay(die, c, core, out var pattern, out var preset);
        if (bay < 0 || pattern == null)
            return;
        var piece = PieceIn(c, core, bay);
        if (piece < 0)
            return;
        blank = c.Parts[piece].D;
        made = preset ?? Pressed.Parse(Pressed.KeyOf(die, pattern, blank.ExKey with { Growing = false }));
    }

    /// <summary>What lies on the bed under the dies, as drawn.</summary>
    public enum BedShow : byte { None, Blank, Made }

    /// <summary>How long the new part shows on the bed once the dies have opened on it (s).</summary>
    public const double MadeShows = 1.5;

    /// <summary>
    /// How far open (1 stowed in its housing, 0 closed over the bed) die segment <paramref name="index"/> is now, and what lies on the bed:
    /// in a stroke, the dies open on the last part if they were shut (back to back), the blank lies on the bed, they close on it (SlamFrom
    /// to SlamAt) and stay shut; after the stroke they open on the new part, which shows a while.
    /// </summary>
    public static float DieOpen(CStore st, int index, double now, bool working, out BedShow show)
    {
        if (working)
        {
            var ph = Phase(index, now - st.StrokeSince);
            if (!st.StrokeFromRest && ph < OpenTo)
            {
                show = st.StrokeLast != null ? BedShow.Made : BedShow.None;
                var t = Math.Max(0, ph) / OpenTo;
                return (float)(t * t * (3 - 2 * t));
            }
            show = BedShow.Blank;
            if (ph < SlamFrom)
                return 1;
            if (ph < SlamAt)
            {
                var u = (ph - SlamFrom) / (SlamAt - SlamFrom);
                return (float)(1 - u * u); // (gathering speed into the slam)
            }
            // (shut on it, in the room the dies leave over the bed: the blank until the stroke ends)
            return 0;
        }
        if (st.StrokeEnd <= 0)
        {
            show = BedShow.None;
            return 1;
        }
        var e = Phase(index, now - st.StrokeEnd);
        show = now - st.StrokeEnd < OpenTo + MadeShows ? BedShow.Made : BedShow.None;
        if (e >= OpenTo)
            return 1;
        var k = Math.Max(0, e) / OpenTo;
        return (float)(k * k * (3 - 2 * k));
    }

    /// <summary>Server: seconds into the stroke under way, from its work so far (the crush test's clock: the same swing every peer draws).</summary>
    public static double StrokeSecondsServer(Construct c, int part, CStore st) => st.JobTicks / (double)Math.Max(1, WorkOf(c, part)) / 10.0;

    /// <summary>Is this press's swing strip blocked (its machine's blocker, found at the rebuild)?</summary>
    public static bool SwingBlocked(Construct c, int part) => c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.Blocker >= 0;

    /// <summary>What blocks this press's shells, for the look line ("the shells' swing hits Conveyor"), or "".</summary>
    public static string SwingText(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.Blocker >= 0 && asm.Blocker < c.PartCount
            ? $"the dies' swing hits the {c.Parts[asm.Blocker].D.Name}: keep {(asm.L - 2) * c.Parts[part].D.BlockCell * 0.5f:0.#} m out from each housing's inner face clear, up to {MaxLift((asm.L - 2) * c.Parts[part].D.BlockCell * 0.5f) + (asm.L - 2) * c.Parts[part].D.BlockCell * 0.5f * 1.415f:0.#} m over the bed"
            : "";

    /// <summary>
    /// At the machine's rebuild: the first part of the construct, not one of this machine's own, where either side's open die reaches past
    /// its housing (a die wider than its housing: the bed's length, the die's height over the bed). Kept as <see cref="CMachineAsm.Blocker"/>.
    /// </summary>
    internal static void SwingCheck(Construct c, ref CMachineAsm asm, int id)
    {
        asm.Blocker = -1;
        if (asm.L < 2)
            return;
        var core = asm.Core;
        var rot = c.Parts[core].Rot;
        var un = Quaternion.Conjugate(Rot24.Quat(rot));
        var cell = c.Parts[core].D.BlockCell;
        var mid = MachineBlocks.CoreMid(c, core);
        const float Margin = 0.03f;
        var frame = c.FrameOf(c.Parts[core].Seg);
        for (var side = 0; side < 2; side++)
        {
            int sgn = side == 0 ? -1 : 1, edge = side == 0 ? asm.Z0 : asm.Z0 + asm.L - 1;
            var tall = 0;
            for (var x = asm.X0; x < asm.X0 + asm.W; x++)
                tall = Math.Max(tall, MachineBlocks.ColumnAt(c, asm, x, edge));
            if (tall <= 0)
                continue;
            var reach = (asm.L - 2) * cell * 0.5f; // (the die's radius: open, it stands that far out from the housing's inner face)
            if (reach <= Margin)
                continue;
            // Its sweep: about its hinge (the housing's inner face, at most MaxLift over the bed) from over the bed to out over the housing. Its far corner,
            // reach * sqrt 2 from the hinge, passes straight over the hinge; past the hinge it reaches out at most its radius. Over the
            // housing and past it, from the bed up to that corner's height (the housing's own blocks are skipped).
            var face = (edge + 0.5f * sgn) * cell;
            var lo = new Vector3((asm.X0 - 0.5f) * cell + Margin, 0.5f * cell + Margin, sgn > 0 ? face + Margin : face - reach);
            var hi = new Vector3((asm.X0 + asm.W - 0.5f) * cell - Margin, 0.5f * cell + MaxLift(reach) + reach * 1.415f, sgn > 0 ? face + reach : face - Margin);
            for (var j = 0; j < c.PartCount; j++)
            {
                if (j < c.BlockAsmOf.Length && c.BlockAsmOf[j] == id || c.Parts[j].Broken || c.SolidCount(j) == 0
                    || c.FrameOf(c.Parts[j].Seg) != frame) // (bounds compare within one frame: a freeform weld's part sits in its own)
                    continue;
                c.PartBounds(j, out var mn, out var mx);
                var a = Vector3.Transform(mn - mid, un);
                var b = Vector3.Transform(mx - mid, un);
                Vector3 pl = Vector3.Min(a, b), ph = Vector3.Max(a, b);
                if (pl.X < hi.X && ph.X > lo.X && pl.Y < hi.Y && ph.Y > lo.Y && pl.Z < hi.Z && ph.Z > lo.Z)
                {
                    asm.Blocker = (short)j;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The space inside the shut shells and the two swing strips of a working press, in the Core's frame (for the crush test): false
    /// when it has no walls. <paramref name="inside"/> is between the shells over the bed; the strips run out from each side.
    /// </summary>
    public static bool ShellSpace(Construct c, int part, out Vector3 mid, out byte rot, out Vector3 insideLo, out Vector3 insideHi, out float strip)
    {
        (mid, rot, insideLo, insideHi, strip) = (default, 0, default, default, 0);
        var walls = Walls(c, part);
        if (walls <= 0 || !MachineBlocks.TryOf(c, part, out var asm))
            return false;
        var cell = c.Parts[part].D.BlockCell;
        mid = MachineBlocks.CoreMid(c, part);
        rot = c.Parts[part].Rot;
        insideLo = new Vector3((asm.X0 - 0.5f) * cell, 0.5f * cell, (asm.Z0 + 0.5f) * cell);
        insideHi = new Vector3((asm.X0 + asm.W - 0.5f) * cell, 0.5f * cell + walls * cell, (asm.Z0 + asm.L - 1.5f) * cell);
        strip = walls * cell;
        return true;
    }
}
