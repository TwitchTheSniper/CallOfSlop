using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Press as a clamshell (design page drawing 17b, Sean 2026-11-21). The wall blocks stacked on the bed's two long edges (its first
/// and last rows along the Core's z) are its shells: each wall column is one shell segment, hinged along the outer top edge of the bed
/// block it stands on. Each stroke (Sean, 2026-11-23: they close on the part to transform it) swings the segments open flat out to their
/// side over the blank, slams them shut on it in a quick ripple down the bed (one segment a cell along the flow, the Core's x), and holds
/// them shut while it presses; the part is made at the stroke's end. The top block of each column draws a roof lip out to the bed's
/// centre line, where the two halves meet. At rest the shells stand shut, where the blocks' own collision is.
/// <list type="bullet">
/// <item><b>Drawn only.</b> The swing is a function of the time into the stroke: a peer sees a stroke start when its store turns Busy or
/// its progress falls back to the 0 each stroke sends at its start (<see cref="StrokeSeconds"/>); the server's crush test reads the
/// stroke's work so far. Nothing new goes on the wire. The blocks' collision stays where they were welded: shut.</item>
/// <item><b>The swing strip.</b> Each side needs a clear strip as wide as its shells are tall. At the machine's rebuild a fixed part of
/// the same construct in a strip (not one of its own blocks) is kept as the machine's blocker (<see cref="SwingCheck"/>): the press
/// doesn't work, the screen says "Shell swing blocked" and the look line names the part.</item>
/// <item><b>No stopping for people.</b> The shells swing whoever is in the way (the server's crush test: Game.SandboxPressCrush).</item>
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
    private const int RippleMax = 8;
    /// <summary>The shortest stroke, in press steps (10 Hz): the last segment is shut before the part is made.</summary>
    public const int StrokeMinSteps = (int)((SlamAt + Ripple * RippleMax) * 10) + 1;
    /// <summary>A creative stroke, in press steps: free, but it still takes its swing.</summary>
    public const int CreativeStrokeSteps = 20;

    /// <summary>A shell segment: where its hinge is and which way it opens, in the body frame.</summary>
    public struct ShellPose
    {
        public Vector3 Hinge; // a point on the hinge line (the bed block's outer top edge)
        public Vector3 Axis; // the hinge line's direction: a positive turn about it swings the segment open (up toward out)
        public Vector3 Out, Up, Along; // outward from the bed, the Core's up, along the flow
        public float Cell; // the block's size (m)
        public float Gap; // from the shell's inner face to the bed's centre line (m): the roof lip's reach
        public int Index; // its place down the bed (0 at the Core's end): the ripple's order
        public int Core; // the press's Core
        public bool Top; // the top block of its column: it draws the roof lip
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
        s.Hinge = mid + Rot24.Apply(rot, new Vector3(x * cell, 0.5f * cell, (z + 0.5f * sgn) * cell));
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
    /// Seconds into the stroke a press is at, as this peer draws it at <paramref name="now"/> (ConstructRender.Now). A stroke starts when
    /// its store turns Busy or its progress falls back (each stroke sends a 0 at its start: WorkSized), seen here and kept on the store.
    /// </summary>
    public static double StrokeSeconds(CStore st, double now)
    {
        if (st.Busy && (!st.StrokeOn || st.Progress < st.StrokeSeen))
            st.StrokeSince = now;
        st.StrokeOn = st.Busy;
        st.StrokeSeen = st.Progress;
        return now - st.StrokeSince;
    }

    /// <summary>Server: seconds into the stroke under way, from its work so far (the crush test's clock: the same swing every peer draws).</summary>
    public static double StrokeSecondsServer(Construct c, int part, CStore st) => st.JobTicks / (double)Math.Max(1, WorkOf(c, part)) / 10.0;

    /// <summary>The turn (body frame) that swings a segment's boxes <paramref name="open"/> of the way open about its hinge.</summary>
    public static Matrix4x4 ShellTurn(in ShellPose s, float open) =>
        open <= 0 ? Matrix4x4.Identity
            : Matrix4x4.CreateTranslation(-s.Hinge) * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(s.Axis), open * MathF.PI * 0.5f) * Matrix4x4.CreateTranslation(s.Hinge);

    /// <summary>Is this press's swing strip blocked (its machine's blocker, found at the rebuild)?</summary>
    public static bool SwingBlocked(Construct c, int part) => c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.Blocker >= 0;

    /// <summary>What blocks this press's shells, for the look line ("the shells' swing hits Conveyor"), or "".</summary>
    public static string SwingText(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.Blocker >= 0 && asm.Blocker < c.PartCount
            ? $"the shells' swing hits the {c.Parts[asm.Blocker].D.Name}: move it {asm.L * c.Parts[part].D.BlockCell * 0.5f + Walls(c, part) * c.Parts[part].D.BlockCell:0} m clear of the bed's middle"
            : "";

    /// <summary>
    /// At the machine's rebuild: the first part of the construct, not one of this machine's own, in either side's swing strip (as wide
    /// as that side's tallest column, the bed's length, from just over the floor up). Kept as <see cref="CMachineAsm.Blocker"/>.
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
            var h = tall * cell;
            var face = (edge + 0.5f * sgn) * cell;
            var lo = new Vector3((asm.X0 - 0.5f) * cell + Margin, -0.5f * cell + 0.05f, sgn > 0 ? face + Margin : face - h);
            var hi = new Vector3((asm.X0 + asm.W - 0.5f) * cell - Margin, 0.5f * cell + h, sgn > 0 ? face + h : face - Margin);
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
