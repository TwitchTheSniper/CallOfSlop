using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Laser Cutter on the block system (design page drawing 16; MachineBlocks.cs): a Laser Cutter Core on a long side of a bed of identical
/// 2 x 2 m Laser Bed Blocks. The bed is the largest full rectangle of blocks beside the Core; it sets the largest blank (either way round,
/// under the bridge's 2 m). One bridge runs along the bed on the rails of its outer blocks and carries one head per block across it, so a
/// wider bed cuts faster (<see cref="Heads"/>; each head is the Core's <c>kw</c>). Each block in the bed adds a share of the offcut bin. All of
/// it is read from the construct's parts and welds, so a client's copy agrees with the host's after a block is welded on or shot off; the
/// bridge and heads' path is read from the cut key, so every peer draws the same path (<see cref="LookPose"/>).
/// </summary>
public static partial class ConstructLaserCutter
{
    /// <summary>The bed's size in metres (x across the Core's frame, y along it; zero with no Bed Blocks).</summary>
    public static Vector2 BedOf(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) ? Vector2.Min(MachineBlocks.BedSize(c, asm), c.Parts[part].D.ExtWheelMax) : Vector2.Zero;

    /// <summary>The bridge's heads: one per block across the bed (0 with no bed).</summary>
    public static int Heads(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) ? asm.W : 0;

    /// <summary>Bed Blocks in the bed.</summary>
    public static int Blocks(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) ? asm.W * asm.L : 0;

    /// <summary>What the offcut bin holds now: the Core's own and each block's share.</summary>
    public static int BinOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return Math.Min(d.BulkCapacity, d.CutBin + d.CutBinBlock * Blocks(c, part));
    }

    /// <summary>What the cutter draws while it cuts (W): its power for each head.</summary>
    public static float PowerOf(Construct c, int part) => c.Parts[part].D.PowerDraw * Math.Max(1, Heads(c, part));

    /// <summary>The conveyor that takes its cut parts away (from its Out Table's Piece out when it has one), or -1.</summary>
    public static int OutConveyor(Construct c, int part) => ConstructConveyor.Out(c, part);

    /// <summary>
    /// Where a cut part (or a blank given back) <paramref name="tall"/> high lies when no conveyor takes it, in the Core's frame: its middle
    /// over the middle of the bed, on the slats (the Core's own <c>at</c> with no bed).
    /// </summary>
    public static Vector3 OutAt(Construct c, int part, float tall)
    {
        var d = c.Parts[part].D;
        var at = d.ExtDieAt;
        if (d.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0)
        {
            var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
            var cell = d.BlockCell;
            at = new Vector3(o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell, at.Y, o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * cell);
        }
        return at + new Vector3(0, tall * 0.5f + 0.05f, 0);
    }

    // ---- The look: the bridge and its heads follow the cut's outline -------------------------------------------------------------

    /// <summary>How far the bridge parks in from the bed's in edge, and a head's reach either side of its block's middle (m).</summary>
    private const float HeadRest = 0.48f, HeadReach = 0.72f, Margin = 0.25f;

    /// <summary>
    /// The look of a Bed Block <paramref name="block"/> of cutter <paramref name="core"/> at time <paramref name="now"/> while it cuts: how far
    /// the bridge has run from its rest (body frame), how far this block's head stands off its block's middle along the bridge, whether this
    /// block's head is the one cutting now, and the point under it (body frame, on the slats). False: the block isn't in the bed's first row
    /// (it carries no bridge) or there is no job; everything stays at rest. Nothing allocates once the job's path is read.
    /// </summary>
    public static bool LookPose(Construct c, int block, int core, double now, out Vector3 bridge, out Vector3 head, out bool active, out Vector3 tip)
    {
        (bridge, head, active, tip) = (Vector3.Zero, Vector3.Zero, false, Vector3.Zero);
        ref var cp = ref c.Parts[core];
        var d = cp.D;
        if (!d.IsLaserCutter || !MachineBlocks.TryOf(c, core, out var asm) || asm.W == 0 || !WorkOrders.HeadOf(cp, out var order) || order.Name == null || JobOf(order.Name) is not { } j)
            return false;
        var cell = d.BlockCell;
        var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
        var turn = Rot24.Quat(cp.Rot);
        var back = Quaternion.Conjugate(turn);
        ref var bp = ref c.Parts[block];
        var bd = bp.D;
        var mid = Vector3.Transform(bp.Pos + Rot24.Apply(bp.Rot, (bd.BoundsMin + bd.BoundsMax) * 0.5f) - cp.Pos, back) - o; // (the block's middle from the Core's cell, in its frame)
        int cx = (int)MathF.Round(mid.X / cell), cz = (int)MathF.Round(mid.Z / cell);
        if (cz != asm.Z0 || cx < asm.X0 || cx >= asm.X0 + asm.W)
            return false;
        // Where the path is now: one cut a cycle, as long as the cut takes (every peer reads the same key and the same law).
        if (j.Path == null)
        {
            j.Path = HullLoft.CutOutline(order.Name, j.Longest, j.Next) ?? [Vector2.Zero, new Vector2(j.Longest, 0)];
            var len = 0f;
            for (var k = 1; k < j.Path.Length; k++)
                len += Vector2.Distance(j.Path[k - 1], j.Path[k]);
            j.PathLength = MathF.Max(0.01f, len);
        }
        var secs = Seconds(j, c, core);
        var s = (float)(now % secs / secs) * j.PathLength;
        var at = j.Path[^1];
        for (var k = 1; k < j.Path.Length; k++)
        {
            var seg = Vector2.Distance(j.Path[k - 1], j.Path[k]);
            if (s <= seg)
            {
                at = Vector2.Lerp(j.Path[k - 1], j.Path[k], seg > 1e-5f ? s / seg : 1);
                break;
            }
            s -= seg;
        }
        // The blank lies in the middle of the bed, its length along the bed's longer side.
        var along = asm.L >= asm.W;
        float u = along ? at.X : at.Y, v = along ? at.Y : at.X;
        float inEdge = o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * cell - (along ? j.Longest : j.Next) * 0.5f - Margin;
        float sideEdge = o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell - (along ? j.Next : j.Longest) * 0.5f - Margin;
        var rowMid = o.Z + asm.Z0 * cell;
        var colMid = o.X + cx * cell;
        var dz = Math.Clamp(inEdge + Margin + u - (rowMid - HeadRest), -0.12f, asm.L * cell - HeadRest);
        var x = sideEdge + Margin + v;
        var dx = Math.Clamp(x - colMid, -HeadReach, HeadReach);
        active = MathF.Abs(x - colMid) <= cell * 0.5f || cx == asm.X0 && x < colMid || cx == asm.X0 + asm.W - 1 && x > colMid;
        bridge = Vector3.Transform(new Vector3(0, 0, dz), turn);
        head = Vector3.Transform(new Vector3(dx, 0, 0), turn);
        tip = cp.Pos + Vector3.Transform(new Vector3(colMid + dx, d.ExtDieAt.Y, rowMid - HeadRest + dz), turn);
        return true;
    }

    /// <summary>
    /// The blank lying on a cutter's bed while it is cut (its look): its def, its turn and its middle in the Core's frame, in the middle of the
    /// bed with its length along the bed's longer side, as the cut part lies when it is done. False: no blank on the bed, or no bed.
    /// </summary>
    public static bool BlankOnBed(Construct c, int part, CStore st, out ConstructPartDef blank, out byte rot, out Vector3 at)
    {
        (blank, rot, at) = (null!, 0, default);
        ref var p = ref c.Parts[part];
        if (!st.OnBed || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0 || !WorkOrders.HeadOf(p, out var order) || order.Name == null || JobOf(order.Name) is not { } j)
            return false;
        j.BlankDef ??= ConstructParts.Find(j.Blank);
        if (j.BlankDef is not { } bd)
            return false;
        var along = asm.L >= asm.W;
        ref var r = ref along ? ref j.BlankRotAlong : ref j.BlankRotAcross;
        if (r == 255)
        {
            // The turn that lays its least side up and its longest along the bed's longer side.
            var size = bd.BoundsMax - bd.BoundsMin;
            var best = float.MaxValue;
            for (byte q = 0; q < Rot24.Count; q++)
            {
                var t = Vector3.Abs(Rot24.Apply(q, size));
                var miss = MathF.Abs(t.Y - j.Least) + MathF.Abs((along ? t.Z : t.X) - j.Longest);
                if (miss < best - 1e-4f)
                    (best, r) = (miss, q);
            }
        }
        rot = r;
        blank = bd;
        var mid = Rot24.Apply(rot, (bd.BoundsMin + bd.BoundsMax) * 0.5f);
        var o = OutAt(c, part, Vector3.Abs(Rot24.Apply(rot, bd.BoundsMax - bd.BoundsMin)).Y);
        at = o - new Vector3(0, 0.05f - 0.004f, 0) - mid; // (its origin, so its middle lies on the slats)
        return true;
    }
}
