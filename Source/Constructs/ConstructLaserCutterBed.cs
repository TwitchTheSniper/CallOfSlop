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

    /// <summary>How far the bridge parks in from the bed's in edge, the blank's margin, and the closest two heads come (a carriage's width, m).</summary>
    private const float HeadRest = 0.48f, Margin = 0.25f, HeadGap = 0.4f;

    /// <summary>
    /// The heads' plan for one cut key on one size of bed (Laser Cutter Lab, Sean 2026-11-24): every head rides the bridge on its own
    /// carriage, so heads cut at once wherever their cuts sit side by side along the bridge. Cuts whose outlines run through the same
    /// stations along the bed (an opening repeated across the sheet, the corners at one end) are one pass, a block of neighbouring heads
    /// taking one each; the heads with nothing to cut in a pass run out to the bridge's ends, packed a carriage apart, clear of the working
    /// ones. The plan is walked over the cut's own time (the law already counts every head's power). Frames: the bridge's place along the
    /// blank, each head's place across it, and which heads cut on the way into the frame. Blank-relative metres (along, across).
    /// </summary>
    public sealed class HeadPlan
    {
        public int Key;
        public float[] Time = [];   // each frame's end, 0..1 of the cut
        public float[] Along = [];  // the bridge's place (m along the blank)
        public float[] Across = []; // frames x heads: each head's place (m across the blank)
        public bool[] On = [];      // frames x heads: cutting on the way into the frame
    }

    /// <summary>The path's closed outlines, one cut each (CutOutline closes each one on its first point; the moves between them are dropped).</summary>
    private static List<Vector2[]> Loops(Vector2[] path)
    {
        var loops = new List<Vector2[]>();
        var start = 0;
        for (var k = 1; k < path.Length; k++)
            if (k - start >= 2 && Vector2.DistanceSquared(path[k], path[start]) < 1e-8f)
            {
                loops.Add(path[start..(k + 1)]);
                start = k + 1;
            }
        if (path.Length - start >= 2)
            loops.Add(path[start..]);
        return loops;
    }

    /// <summary>
    /// Builds a job's plan for a bed <paramref name="heads"/> blocks across and <paramref name="rows"/> along, its length along the bed's
    /// longer side or not (<paramref name="along"/>). Allocates: once per key and bed size, by the job's cache.
    /// </summary>
    public static HeadPlan PlanHeads(Vector2[] path, float longest, float next, int heads, int rows, float cell, bool along)
    {
        int n = Math.Max(1, heads);
        float alongLen = along ? longest : next, acrossLen = along ? next : longest;
        var loops = Loops(path);
        // (u: along the bed, the bridge's way; v: across it, the heads' way; both from the blank's corner)
        var cuts = loops.ConvertAll(l => Array.ConvertAll(l, q => along ? q : new Vector2(q.Y, q.X)));
        float aLo = acrossLen * 0.5f - n * cell * 0.5f, aHi = acrossLen * 0.5f + n * cell * 0.5f; // (the bridge's ends, across)
        var restU = alongLen * 0.5f - (rows - 1) * cell * 0.5f - HeadRest; // (where the bridge parks: its rest over the first row)
        var rest = new float[n];
        for (var h = 0; h < n; h++)
            rest[h] = aLo + (h + 0.5f) * cell;
        var gap = MathF.Min(HeadGap, cell); // (packed a carriage apart)
        // Passes: cuts that run through the same stations along the bed (point for point), wide apart across it.
        var used = new bool[cuts.Count];
        var passes = new List<List<int>>();
        var order = Enumerable.Range(0, cuts.Count).OrderBy(i => cuts[i].Min(q => q.X)).ToList();
        (float, float) Span(int i) => (cuts[i].Min(q => q.Y), cuts[i].Max(q => q.Y));
        foreach (var i in order)
        {
            if (used[i])
                continue;
            used[i] = true;
            var group = new List<int> { i };
            foreach (var j in order)
            {
                if (used[j] || group.Count >= n || cuts[j].Length != cuts[i].Length)
                    continue;
                var same = true;
                for (var k = 0; k < cuts[i].Length && same; k++)
                    same = MathF.Abs(cuts[j][k].X - cuts[i][k].X) < 0.01f;
                var (j0, j1) = Span(j);
                foreach (var g in group)
                {
                    var (g0, g1) = Span(g);
                    same &= j0 > g1 + gap || j1 < g0 - gap;
                }
                if (same)
                {
                    used[j] = true;
                    group.Add(j);
                }
            }
            group.Sort((x, y) => Span(x).Item1.CompareTo(Span(y).Item1));
            passes.Add(group);
        }
        // Frames, weighted by length (a rapid move counts a quarter, a pierce a tenth of a metre), then scaled to 0..1.
        var time = new List<float>();
        var alongF = new List<float>();
        var acrossF = new List<float>();
        var onF = new List<bool>();
        var cur = (float[])rest.Clone();
        float curU = restU, t = 0;
        void Frame(float w, float u, float[] a, bool[]? on)
        {
            t += MathF.Max(1e-3f, w);
            time.Add(t);
            alongF.Add(u);
            acrossF.AddRange(a);
            for (var h = 0; h < n; h++)
                onF.Add(on != null && on[h]);
            curU = u;
            cur = a;
        }
        foreach (var group in passes)
        {
            var m = group.Count;
            var mid = group.Average(g => (Span(g).Item1 + Span(g).Item2) * 0.5f);
            var k0 = Math.Clamp((int)MathF.Round((mid - aLo) / cell - m * 0.5f), 0, n - m);
            var lo = group.Min(g => Span(g).Item1);
            var hi = group.Max(g => Span(g).Item2);
            var a = new float[n];
            for (var h = 0; h < n; h++)
                a[h] = h < k0 ? MathF.Min(aLo + 0.2f + gap * h, lo - gap * (k0 - h))
                    : h >= k0 + m ? MathF.Max(aHi - 0.2f - gap * (n - 1 - h), hi + gap * (h - k0 - m + 1))
                    : cuts[group[h - k0]][0].Y;
            var move = MathF.Abs(cuts[group[0]][0].X - curU);
            for (var h = 0; h < n; h++)
                move = MathF.Max(move, MathF.Abs(a[h] - cur[h]));
            Frame(move * 0.25f, cuts[group[0]][0].X, a, null);
            var on = new bool[n];
            for (var h = k0; h < k0 + m; h++)
                on[h] = true;
            Frame(0.1f, curU, a, on); // (the pierce)
            for (var k = 1; k < cuts[group[0]].Length; k++)
            {
                var b = (float[])cur.Clone();
                var w = 0f;
                for (var r = 0; r < m; r++)
                {
                    var q = cuts[group[r]];
                    b[k0 + r] = q[k].Y;
                    w = MathF.Max(w, Vector2.Distance(q[k], q[k - 1]));
                }
                Frame(w, cuts[group[0]][k].X, b, on);
            }
        }
        var back = MathF.Abs(restU - curU);
        for (var h = 0; h < n; h++)
            back = MathF.Max(back, MathF.Abs(rest[h] - cur[h]));
        Frame(back * 0.25f, restU, rest, null);
        var plan = new HeadPlan { Time = [.. time], Along = [.. alongF], Across = [.. acrossF], On = [.. onF] };
        for (var k = 0; k < plan.Time.Length; k++)
            plan.Time[k] /= t;
        return plan;
    }

    /// <summary>
    /// The look of a Bed Block <paramref name="block"/> of cutter <paramref name="core"/> at time <paramref name="now"/> while it cuts: how far
    /// the bridge has run from its rest (body frame), how far this block's head stands off its block's middle along the bridge, whether this
    /// block's head is cutting now, and the point under it (body frame, on the slats). False: the block isn't in the bed's first row (it
    /// carries no bridge) or there is no job; everything stays at rest. Heads cut at once (<see cref="HeadPlan"/>). Nothing allocates once
    /// the job's plan for this size of bed is made.
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
        // The blank lies in the middle of the bed, its length along the bed's longer side; the plan is read once per key and bed size.
        var along = asm.L >= asm.W;
        var key = asm.W * 64 + asm.L;
        if (j.Plan == null || j.Plan.Key != key)
        {
            j.Path ??= HullLoft.CutOutline(order.Name, j.Longest, j.Next) ?? [Vector2.Zero, new Vector2(j.Longest, 0)];
            j.Plan = PlanHeads(j.Path, j.Longest, j.Next, asm.W, asm.L, cell, along);
            j.Plan.Key = key;
        }
        var plan = j.Plan;
        var secs = Seconds(j, c, core);
        var f = (float)(now % secs / secs);
        int k = 0, lo = 0, hi = plan.Time.Length - 1;
        while (lo <= hi) // (the first frame ending after f)
        {
            var m = (lo + hi) >> 1;
            if (plan.Time[m] < f)
                lo = m + 1;
            else
            {
                k = m;
                hi = m - 1;
            }
        }
        if (lo >= plan.Time.Length)
            k = plan.Time.Length - 1;
        var t0 = k > 0 ? plan.Time[k - 1] : 0f;
        var s = Math.Clamp((f - t0) / MathF.Max(1e-6f, plan.Time[k] - t0), 0f, 1f);
        var h = cx - asm.X0;
        int n = asm.W, k1 = k > 0 ? k - 1 : plan.Time.Length - 1; // (frame -1 is the last: the plan ends at its rest)
        var u = float.Lerp(plan.Along[k1], plan.Along[k], s);
        var v = float.Lerp(plan.Across[k1 * n + h], plan.Across[k * n + h], s);
        float alongLen = along ? j.Longest : j.Next, acrossLen = along ? j.Next : j.Longest;
        float inEdge = o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * cell - alongLen * 0.5f;
        float sideEdge = o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell - acrossLen * 0.5f;
        var rowMid = o.Z + asm.Z0 * cell;
        var colMid = o.X + cx * cell;
        var dz = Math.Clamp(inEdge + u - (rowMid - HeadRest), -0.12f, asm.L * cell - HeadRest);
        var dx = sideEdge + v - colMid;
        active = plan.On[k * n + h];
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
