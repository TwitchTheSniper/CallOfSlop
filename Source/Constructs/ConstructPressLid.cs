using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Press as a swing-lid press (Sean, 2026-11-25, the Press Lab's "Swing lid" with the Power Pack Core and adjustable jaws: the
/// project's press-lab/BUILD-SPEC.md). It replaced the round dies that rolled shut over the bed (PR 558/579). A press with a bed (Press
/// Blocks, and Press Bed Blocks between its housings' rows) is drawn whole by its Core over the bed's rectangle (ConstructRenderPress.cs):
/// a low side frame on each long edge, a bed and bolster open under the die (a pit), the shaped lower die in a full-size holder whose four
/// screw-driven fences close on the blank, and the lid: the upper die under one heavy ribbed lid hinged along the top of the frame across
/// from the Core, on a pedestal that rises with the die. The top stays open while a blank comes in, so a crane can drop one onto the die.
/// <list type="bullet">
/// <item><b>A stroke.</b> The blank lies on the lower die; the lid swings shut (<see cref="ShutSec"/>), the latch hooks on the Core's
/// frame swing over its top (<see cref="LatchSec"/>), the lid squeezes down by its tool's reach while the part is pressed, and the part is
/// made at the stroke's end. Then the hooks open and the lid rises (<see cref="UnlatchSec"/>), swings open (<see cref="OpenSec"/>) and an
/// ejector lifts the part out of the die (<see cref="EjectSec"/>); only then does the part leave the press, and the next stroke may start (<see cref="LidOpenSteps"/>).</item>
/// <item><b>Drawn only.</b> The lid, the jaws and the pieces on the die are a function of the time into the stroke and since it ended: a
/// peer sees a stroke start when its store turns Busy or its progress falls back to the 0 each stroke sends at its start, and end when Busy
/// falls (<see cref="StrokeUpdate"/>). Nothing new goes on the wire. The blocks' cells are the collision; the lid has none.</item>
/// <item><b>The swing strip.</b> Open, the lid stands past upright and leans out over the hinge side. That strip beside the press must
/// be clear: at the machine's rebuild a fixed part of the same construct there is kept as the machine's blocker (<see cref="SwingCheck"/>):
/// the press doesn't work, the screen says "Lid swing blocked" and the look line names the part. A block stacked on the bed (the old
/// round dies' wall blocks) stands where the lid lives and blocks it too.</item>
/// <item><b>No stopping for people.</b> The lid shuts on whoever is on the bed (the server's crush test: Game.SandboxPressCrush).</item>
/// </list>
/// Nothing here allocates; the swing check runs at the rebuild only.
/// </summary>
public static partial class ConstructPress
{
    // A stroke's lid (seconds): shut, latch, then the press time; after the stroke the hooks open, the lid swings open and the ejector lifts.
    public const double ShutSec = 2.2, LatchSec = 0.6, SqueezeSec = 0.8, UnlatchSec = 0.5, OpenSec = 2.4, EjectSec = 0.4;
    /// <summary>The open lid's angle (rad): past upright, leaning out over the hinge side.</summary>
    public const float OpenAngle = 1.85f;
    /// <summary>The latch hooks' open turn (rad).</summary>
    public const float LatchOpen = 1.2f;
    /// <summary>The shortest stroke, in press steps (10 Hz): the lid is shut and latched before the part is made, with a short squeeze.</summary>
    public const int StrokeMinSteps = (int)((ShutSec + LatchSec + 0.5) * 10) + 1;
    /// <summary>A creative stroke, in press steps: free, but it still takes its swing.</summary>
    public const int CreativeStrokeSteps = StrokeMinSteps + 2;
    /// <summary>Press steps after a stroke before its part leaves and the next may start: the lid opens and the ejector lifts the part out.</summary>
    public const int LidOpenSteps = (int)((UnlatchSec + OpenSec + EjectSec) * 10) + 1;
    /// <summary>A repeat stroke (another blank the same size waiting: ConstructPress.SameWaiting): the shut lid lifts off the made part (s), then stamps again.</summary>
    public const double ReleaseSec = 0.4;
    /// <summary>The shortest repeat stroke, in press steps: the lid stays shut and latched; it lifts off the last part and squeezes the next.</summary>
    public const int RepeatMinSteps = (int)((ReleaseSec + SqueezeSec + 0.2) * 10) + 1;
    /// <summary>Press steps the shut lid waits for the same-size blank on its way in before it opens after all.</summary>
    public const byte RepeatHoldSteps = 20;
    /// <summary>How long the drawn part shows on the die once it is lifted out (s): until the real one leaves (LidOpenSteps rounds up a step).</summary>
    public const double MadeShows = 0.1;

    /// <summary>What lies on the die, as drawn.</summary>
    public enum BedShow : byte { None, Blank, Made }

    /// <summary>The lid's pose now: its angle (0 shut, <see cref="OpenAngle"/> open), squeeze (0..1 of its tool's reach), latch hooks (0 open, 1 over), jaws (0 the last piece's size, 1 this one's) and ejector (0 down, 1 up).</summary>
    public struct LidPose
    {
        public float Angle, Drop, Latch, Jaws, Eject;
        public BedShow Show;
    }

    /// <summary>Has this press a lid (a bed)? A Core alone presses on its own deck.</summary>
    public static bool HasLid(Construct c, int part) => Bed(c, part, out _);

    /// <summary>
    /// Is <paramref name="part"/> a cell of a Press's bed that its Core draws (the lid press's model covers the bed's rectangle on its
    /// floor layer)? Its collision stays; it draws nothing of its own. A broken Core draws no lid, so its bed draws itself again.
    /// </summary>
    public static bool Covered(Construct c, int part)
    {
        if (c.Parts[part].D.BlockRole != BlockRole.Block || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0 || (uint)asm.Core >= (uint)c.PartCount
            || !c.Parts[asm.Core].D.IsPress || c.Parts[asm.Core].Broken || !MachineBlocks.CellOf(c, part, out var x, out var y, out var z))
            return false;
        return y == 0 && x >= asm.X0 && x < asm.X0 + asm.W && z >= asm.Z0 && z < asm.Z0 + asm.L;
    }

    /// <summary>
    /// The hinge side in the Core's cell frame: the bed's long edge farther from the Core's own row, +1 along the Core's z, -1 against it.
    /// A bed centred on the Core's row (a small press: Core and blocks in one row) hinges on -z (the Zero Day works' small presses have
    /// the block Assembler's masts 1 m off their +z side and only low belts on their -z side).
    /// </summary>
    public static int HingeSide(in CMachineAsm asm) => asm.Z0 + asm.L - 1 + asm.Z0 > 0 ? 1 : -1;

    /// <summary>The bed's deck (where the blank lies) over the machine's floor (m): a low bed's at the rollers' height, else the blocks' top.</summary>
    public static float DeckOf(in CMachineAsm asm, float cell) => asm.LowBed ? RollerRuns.RunY + ConstructConveyor.BedTop : cell;

    /// <summary>A side frame's width across (m): a low bed's housings' rows, else the Press Lab's share of the bed.</summary>
    public static float FrameWidth(in CMachineAsm asm, float cell) => asm.LowBed ? cell : Math.Min(1.4f, Math.Max(0.45f, 0.35f * asm.L * cell * 0.5f));

    /// <summary>The side frames' top over the floor (m): a low bed's housings' blocks, else 0.6 m over the deck.</summary>
    public static float FrameTop(in CMachineAsm asm, float cell) => asm.LowBed ? cell - 0.003f : DeckOf(asm, cell) + 0.6f;

    /// <summary>The lid's width across (m), from its hinge to its free edge over the Core's frame.</summary>
    public static float LidWidth(in CMachineAsm asm, float cell) => asm.L * cell - 0.5f * FrameWidth(asm, cell);

    private static float Smooth(double t)
    {
        var k = (float)Math.Clamp(t, 0, 1);
        return k * k * (3 - 2 * k);
    }

    /// <summary>
    /// A press as this peer draws it at <paramref name="now"/> (ConstructRender.Now): notes a stroke's start (its store turns Busy, or its
    /// progress falls back to the 0 each stroke sends at its start: WorkSized) and end (Busy falls), and at a start what lies on the die,
    /// what it will make, the last blank's size (the jaws run from it) and the lid's angle then. Kept on the store; the same answer however
    /// often a frame asks. Allocates only at a stroke's start, when a pressed part's key is named.
    /// </summary>
    public static void StrokeUpdate(Construct c, int core, CStore st, double now)
    {
        if (st.StrokeAt == now)
            return;
        st.StrokeAt = now;
        if (st.Busy && (!st.StrokeOn || st.Progress < st.StrokeSeen))
        {
            LidAt(st, now, false, out var was); // (the lid as it stands: open, or still opening when a stroke came early)
            st.StrokeLidFrom = st.StrokeOn ? 0 : was.Angle; // (back to back with no fall of Busy: still shut)
            st.StrokeRepeat = st.StrokeOn; // (Busy never fell: a repeat, the lid kept shut and latched, the stamp alone)
            st.StrokeSince = now;
            st.StrokeLast = st.StrokeMade;
            var lastBlank = st.StrokeBlank;
            var lastDie = st.StrokeDie;
            StrokePieces(c, core, out st.StrokeBlank, out st.StrokeMade, out st.StrokeDie);
            st.StrokeBlankPrev = lastDie != null && ReferenceEquals(lastDie, st.StrokeDie) ? lastBlank : null; // (after a die swap the jaws start from fully open)
        }
        else if (!st.Busy && st.StrokeOn)
            st.StrokeEnd = now;
        st.StrokeOn = st.Busy;
        st.StrokeSeen = st.Progress;
    }

    /// <summary>Seconds into the stroke under way, as this peer draws it (after <see cref="StrokeUpdate"/>).</summary>
    public static double StrokeSeconds(CStore st, double now) => now - st.StrokeSince;

    /// <summary>The blank the stroke under way presses, the part it makes (the next a press with a sized die would make now) and the die, or null.</summary>
    private static void StrokePieces(Construct c, int core, out ConstructPartDef? blank, out ConstructPartDef? made, out PressDie? die)
    {
        (blank, made) = (null, null);
        die = SizedDieOf(PressTable.Current, c, core);
        if (die == null)
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

    /// <summary>
    /// The lid's pose now (after <see cref="StrokeUpdate"/>): in a stroke it swings shut from where it stood, latches and squeezes; after
    /// it, it unlatches and rises, swings open and the ejector lifts the new part, which shows a while. Never sent: every peer works it out.
    /// </summary>
    public static void LidAt(CStore st, double now, bool working, out LidPose p)
    {
        p = default;
        p.Jaws = 1;
        if (working)
        {
            var t = now - st.StrokeSince;
            p.Show = BedShow.Blank;
            if (st.StrokeRepeat)
            {
                // A repeat: shut and latched throughout; the lid lifts off the part it made, then squeezes the next blank.
                p.Latch = 1;
                p.Drop = t < ReleaseSec ? 1 - Smooth(t / ReleaseSec) : Smooth((t - ReleaseSec) / SqueezeSec);
                return;
            }
            p.Jaws = Smooth(t / 1.2); // (the fences and the die run to this blank's size as the lid comes over)
            if (t < ShutSec)
            {
                var s = Smooth(t / ShutSec);
                p.Angle = st.StrokeLidFrom * (1 - s * s); // (gathering speed into the slam)
                return;
            }
            p.Latch = (float)Math.Min(1, (t - ShutSec) / LatchSec);
            p.Drop = Smooth((t - ShutSec - LatchSec) / SqueezeSec);
            return;
        }
        p.Angle = OpenAngle;
        if (st.StrokeEnd <= 0)
            return;
        var e = now - st.StrokeEnd;
        p.Show = BedShow.Made;
        if (e < UnlatchSec)
        {
            p.Angle = 0;
            p.Drop = 1 - Smooth(e / UnlatchSec);
            p.Latch = (float)(1 - e / UnlatchSec);
            return;
        }
        e -= UnlatchSec;
        if (e < OpenSec)
        {
            p.Angle = OpenAngle * Smooth(e / OpenSec);
            return;
        }
        e -= OpenSec;
        p.Eject = Smooth(e / EjectSec);
        if (e >= EjectSec + MadeShows)
            p.Show = BedShow.None;
    }

    /// <summary>Server: seconds into the stroke under way, from its work so far (the crush test's clock: the same swing every peer draws).</summary>
    public static double StrokeSecondsServer(Construct c, int part, CStore st) => st.JobTicks / (double)Math.Max(1, WorkOf(c, part)) / 10.0;

    /// <summary>Is this press's lid blocked (its machine's blocker, found at the rebuild)?</summary>
    public static bool SwingBlocked(Construct c, int part) => c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.Blocker >= 0;

    /// <summary>What blocks this press's lid, for the look line ("the lid's swing hits the Conveyor ..."), or "".</summary>
    public static string SwingText(Construct c, int part)
    {
        if (c.Parts[part].D.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm) || asm.Blocker < 0 || asm.Blocker >= c.PartCount)
            return "";
        var b = asm.Blocker;
        if (MachineBlocks.AsmOf(c, b) == MachineBlocks.AsmOf(c, part))
            return $"a {c.Parts[b].D.Name} stacked on the bed stands where the lid shuts: take the blocks off the bed (the press has no walls now)";
        return $"the lid's swing hits the {c.Parts[b].D.Name}: keep {SwingReach(asm, c.Parts[part].D.BlockCell):0.#} m out from the hinge side clear, from the frame's top up";
    }

    /// <summary>How far out past the hinge side's frame the open lid and its struts reach (m).</summary>
    public static float SwingReach(in CMachineAsm asm, float cell)
    {
        var ut = FrameWidth(asm, cell);
        return Math.Max(0.6f, -0.5f * ut - LidWidth(asm, cell) * MathF.Cos(OpenAngle) + 0.9f);
    }

    /// <summary>
    /// At the machine's rebuild: a block stacked on the bed (where the shut lid lies), else the first part of the construct, not one of
    /// this machine's own, in the open lid's strip beside the hinge side (over the hinge row and out past it, the bed's length and the
    /// struts', from the frame's top up to the open lid's top). Kept as <see cref="CMachineAsm.Blocker"/>.
    /// </summary>
    internal static void SwingCheck(Construct c, ref CMachineAsm asm, int id)
    {
        asm.Blocker = -1;
        if (asm.W == 0)
            return;
        var stacked = false;
        for (var x = asm.X0; x < asm.X0 + asm.W && !stacked; x++)
            for (var z = asm.Z0; z < asm.Z0 + asm.L && !stacked; z++)
                stacked = MachineBlocks.ColumnAt(c, asm, x, z) > 0;
        if (stacked)
        {
            // (rare: an old press with wall blocks. One pass over the parts to name one of them.)
            for (var j = 0; j < c.PartCount; j++)
                if (j < c.BlockAsmOf.Length && c.BlockAsmOf[j] == id && MachineBlocks.CellOf(c, j, out var bx, out var by, out var bz) && by > 0
                    && bx >= asm.X0 && bx < asm.X0 + asm.W && bz >= asm.Z0 && bz < asm.Z0 + asm.L)
                {
                    asm.Blocker = (short)j;
                    return;
                }
        }
        var core = asm.Core;
        var rot = c.Parts[core].Rot;
        var un = Quaternion.Conjugate(Rot24.Quat(rot));
        var cell = c.Parts[core].D.BlockCell;
        var mid = MachineBlocks.CoreMid(c, core);
        const float Margin = 0.03f;
        var frame = c.FrameOf(c.Parts[core].Seg);
        var s = HingeSide(asm);
        var ut = FrameWidth(asm, cell);
        var reach = SwingReach(asm, cell);
        var face = s > 0 ? (asm.Z0 + asm.L - 0.5f) * cell : (asm.Z0 - 0.5f) * cell; // (the hinge side's outer face, in the Core's cell frame)
        var floor = -0.5f * cell;
        var top = floor + DeckOf(asm, cell) + MaxCm(c, core) / 100f + 1.5f + LidWidth(asm, cell) * MathF.Sin(OpenAngle);
        float z0 = s > 0 ? face - ut + Margin : face - reach, z1 = s > 0 ? face + reach : face + ut - Margin;
        var lo = new Vector3((asm.X0 - 0.5f) * cell - 0.35f, floor + FrameTop(asm, cell) + Margin, z0);
        var hi = new Vector3((asm.X0 + asm.W - 0.5f) * cell + 0.35f, top, z1);
        var first = -1;
        if (frame == 0)
        {
            // The strip's box in the body (rest) frame from its eight corners (a quarter turn keeps it a box): the part tree's candidates,
            // the same test, the first of them in part order (2026-11-24: testing every part of the Zero Day works at each rebuild cost a millisecond a press).
            var q = Rot24.Quat(rot);
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var corner = mid + Vector3.Transform(new Vector3((k & 1) != 0 ? hi.X : lo.X, (k & 2) != 0 ? hi.Y : lo.Y, (k & 4) != 0 ? hi.Z : lo.Z), q);
                bmin = Vector3.Min(bmin, corner);
                bmax = Vector3.Max(bmax, corner);
            }
            var pad = new Vector3(0.01f);
            var n = c.PartsInBox(bmin - pad, bmax + pad, Construct.PartScratch);
            for (var k = 0; k < n; k++)
            {
                var j = Construct.PartScratch[k];
                if ((first < 0 || j < first) && SwingHits(c, j, id, frame, mid, un, lo, hi))
                    first = j;
            }
        }
        else
            for (var j = 0; j < c.PartCount && first < 0; j++)
                if (SwingHits(c, j, id, frame, mid, un, lo, hi))
                    first = j;
        if (first >= 0)
            asm.Blocker = (short)first;
    }

    /// <summary>Is part <paramref name="j"/> (not of machine <paramref name="id"/>, solid, in its frame) in a swing's box, in the Core's frame?</summary>
    private static bool SwingHits(Construct c, int j, int id, int frame, Vector3 mid, Quaternion un, Vector3 lo, Vector3 hi)
    {
        if (j < c.BlockAsmOf.Length && c.BlockAsmOf[j] == id || c.Parts[j].Broken || c.SolidCount(j) == 0
            || c.FrameOf(c.Parts[j].Seg) != frame) // (bounds compare within one frame: a freeform weld's part sits in its own)
            return false;
        c.PartBounds(j, out var mn, out var mx);
        var a = Vector3.Transform(mn - mid, un);
        var b = Vector3.Transform(mx - mid, un);
        Vector3 pl = Vector3.Min(a, b), ph = Vector3.Max(a, b);
        return pl.X < hi.X && ph.X > lo.X && pl.Y < hi.Y && ph.Y > lo.Y && pl.Z < hi.Z && ph.Z > lo.Z;
    }

    /// <summary>
    /// The space under the shut lid of a working press, in the Core's cell frame (for the crush test): between the side frames over the
    /// bed, from the deck up to the lid. False with no lid.
    /// </summary>
    public static bool LidSpace(Construct c, int part, out Vector3 mid, out byte rot, out Vector3 insideLo, out Vector3 insideHi)
    {
        (mid, rot, insideLo, insideHi) = (default, 0, default, default);
        if (!Bed(c, part, out var asm))
            return false;
        var cell = c.Parts[part].D.BlockCell;
        mid = MachineBlocks.CoreMid(c, part);
        rot = c.Parts[part].Rot;
        var ut = FrameWidth(asm, cell);
        var floor = -0.5f * cell + DeckOf(asm, cell);
        insideLo = new Vector3((asm.X0 - 0.5f) * cell, floor, (asm.Z0 - 0.5f) * cell + ut);
        insideHi = new Vector3((asm.X0 + asm.W - 0.5f) * cell, floor + 2.5f, (asm.Z0 + asm.L - 0.5f) * cell - ut);
        return true;
    }
}
