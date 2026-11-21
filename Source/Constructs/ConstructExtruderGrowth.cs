using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An Extruder's die and reach on the block system (docs/sandbox/constructs/production.md "Extrusion", design page drawing 15): the Extruder
/// Core alone has its own small die; Die Blocks welded beside it, flush with its die face, make the die the largest full rectangle of
/// blocks that holds one welded to the Core (MachineBlocks.cs finds it at the rebuild). Each block in the die adds its push and its draw,
/// so the speed is the same at any size. Run-out Tables under the die (on the Core's or a block's out bed, or on another table) take the
/// longest piece out to the farthest table's end. Everything is read from the construct's parts and links, so a client's copy agrees with
/// the host's after a block is welded on or shot off.
/// </summary>
public static partial class ConstructExtruder
{
    /// <summary>
    /// The machine's die (centre and size across the face, in its own frame) and its longest piece (m), its Die Blocks and Run-out Tables
    /// counted. The tables are walked only on a machine that takes them.
    /// </summary>
    public static void Reach(Construct c, int part, out Vector2 centre, out Vector2 size, out float length)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        centre = new Vector2(d.ExtDieAt.X, d.ExtDieAt.Y);
        size = d.ExtDie;
        if (d.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0)
        {
            var cell = d.BlockCell;
            var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
            centre = new Vector2(o.X + (asm.X0 + (asm.W - 1) * 0.5f) * cell, o.Y + (asm.Z0 + (asm.L - 1) * 0.5f) * cell);
            size = Vector2.Min(new Vector2(asm.W * cell, asm.L * cell), d.ExtMaxSection);
        }
        length = MathF.Min(d.ExtMaxLength, Extrusion.MaxLengthCm / 100f);
        if (d.ExtGrowLength > d.ExtMaxLength)
            length = MathF.Min(MathF.Max(length, TablesReach(c, part)), MostLength(d));
    }

    /// <summary>How far past the die face the machine's Run-out Tables reach (m), joined to it, to its Die Blocks or to each other, and not broken.</summary>
    private static float TablesReach(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var asm = MachineBlocks.AsmOf(c, part);
        using var scratch = Scratch.Begin();
        Span<bool> seen = scratch.Take<bool>(c.PartCount);
        Span<ushort> queue = scratch.Take<ushort>(c.PartCount);
        int head = 0, tail = 0;
        seen[part] = true;
        queue[tail++] = (ushort)part;
        var back = Quaternion.Conjugate(Rot24.Quat(p.Rot));
        var far = 0f;
        while (head < tail)
        {
            int from = queue[head++];
            for (var e = c.FirstLink(from); e >= 0; e = c.NextLink(e))
            {
                var other = c.OtherEnd(e);
                if (other < 0 || other >= c.PartCount || seen[other] || c.Parts[other].Broken)
                    continue;
                var od = c.Parts[other].D;
                var block = asm >= 0 && MachineBlocks.AsmOf(c, other) == asm;
                if (od.ExtRunOut <= 0 && !block)
                    continue;
                seen[other] = true;
                queue[tail++] = (ushort)other;
                if (od.ExtRunOut <= 0)
                    continue;
                c.PartBounds(other, out var mn, out var mx);
                var a = Vector3.Transform(mn - p.Pos, back);
                var b = Vector3.Transform(mx - p.Pos, back);
                far = MathF.Max(far, MathF.Max(a.Z, b.Z) - d.ExtDieAt.Z);
            }
        }
        return far;
    }

    /// <summary>Die Blocks in the machine's die (0: a Core on its own die, or not a block machine).</summary>
    public static int Blocks(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) ? asm.W * asm.L : 0;

    /// <summary>How hard the machine pushes what is in its piece's way (N): the Core's own and each block's in its die.</summary>
    public static float PushOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.ExtBlockPush > 0 ? (d.ExtPush + Blocks(c, part) * d.ExtBlockPush) * TierPower(c, part) : d.ExtPush;
    }

    /// <summary>A block machine's tier (its tier pieces, drawing 21) as a multiplier of its draw and push: 1 at tier 1.</summary>
    private static float TierPower(Construct c, int part) => PartKit.Generators.MachineTiers.Multipliers(MachineBlocks.TierOf(c, part)).Power;

    /// <summary>What the machine draws while it works (W): the Core's own and each block's in its die.</summary>
    public static float PowerOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.ExtBlockPower > 0 ? (d.PowerDraw + Blocks(c, part) * d.ExtBlockPower) * TierPower(c, part) : d.PowerDraw;
    }

    /// <summary>The longest piece the machine makes now (m), its Run-out Tables counted.</summary>
    public static float MaxLength(Construct c, int part)
    {
        var d = c.Parts[part].D;
        if (d.ExtGrowLength <= d.ExtMaxLength)
            return MathF.Min(d.ExtMaxLength, Extrusion.MaxLengthCm / 100f);
        Reach(c, part, out _, out _, out var length);
        return length;
    }

    /// <summary>The longest piece now in cm (the step's cap).</summary>
    public static int MaxLengthCm(Construct c, int part) => Math.Min(Extrusion.MaxLengthCm, (int)MathF.Round(MaxLength(c, part) * 100));

    /// <summary>The widest a machine's die gets with its Die Blocks (m).</summary>
    public static Vector2 MostSection(ConstructPartDef d) => Vector2.Max(d.ExtDie, d.ExtMaxSection);

    /// <summary>The longest piece a machine makes with its Run-out Tables (m).</summary>
    public static float MostLength(ConstructPartDef d) => MathF.Min(MathF.Max(d.ExtMaxLength, d.ExtGrowLength), Extrusion.MaxLengthCm / 100f);

    /// <summary>What widens this machine's die ("Die Blocks"), or null when nothing does.</summary>
    public static string? WidenWith(ConstructPartDef d)
    {
        if (d.BlockRole != BlockRole.Core || d.ExtMaxSection.X <= d.ExtDie.X && d.ExtMaxSection.Y <= d.ExtDie.Y)
            return null;
        foreach (var e in ConstructParts.All)
            if (e.BlockRole == BlockRole.Block && e.BlockFamily == d.BlockFamily)
                return e.Name + "s";
        return null;
    }

    /// <summary>What makes this machine's pieces longer ("Run-out Tables"), or null.</summary>
    public static string? LengthenWith(ConstructPartDef d)
    {
        if (d.ExtGrowLength <= d.ExtMaxLength)
            return null;
        foreach (var e in ConstructParts.All)
            if (e.ExtRunOut > 0)
                return e.Name + "s";
        return null;
    }

    private static bool Sections(ConstructPartDef d) => d.IsExtruder && !d.IsWheelFormer && !d.IsPanelFormer && !WorkOrders.IsWholeFormer(d);

    /// <summary>The smallest catalogue Extruder that makes a <paramref name="w"/> x <paramref name="h"/> cm section <paramref name="l"/> cm long
    /// (0: any length) with its Die Blocks and tables; null when none does.</summary>
    public static ConstructPartDef? Bigger(int w, int h, int l)
    {
        ConstructPartDef? best = null;
        foreach (var d in ConstructParts.All)
        {
            if (!Sections(d))
                continue;
            var most = MostSection(d);
            if (w > MathF.Round(most.X * 100) || h > MathF.Round(most.Y * 100) || l > MathF.Round(MostLength(d) * 100))
                continue;
            if (best == null || d.ExtMaxSection.X < best.ExtMaxSection.X || d.ExtMaxSection.X == best.ExtMaxSection.X && d.ExtMaxLength < best.ExtMaxLength)
                best = d;
        }
        return best;
    }

    /// <summary>A catalogue machine as the text names it: "an Extruder (dies to 0.5 m, 32 m with Die Blocks)".</summary>
    public static string BuildText(ConstructPartDef b, bool length)
    {
        if (length)
            return LengthenWith(b) is { } t && MostLength(b) > b.ExtMaxLength
                ? $"build an {b.Name} (to {b.ExtMaxLength:0.#} m, {MostLength(b):0.#} m with {t})"
                : $"build an {b.Name} (to {MostLength(b):0.#} m)";
        return WidenWith(b) is { } w && MostSection(b).X > b.ExtDie.X
            ? $"build an {b.Name} (dies to {b.ExtDie.X:0.##} m, {MostSection(b).X:0.##} m with {w})"
            : $"build an {b.Name} (dies to {MostSection(b).X:0.##} m)";
    }

    /// <summary>
    /// What would make machine <paramref name="d"/> take a section <paramref name="w"/> x <paramref name="h"/> cm its die doesn't take now:
    /// "weld Die Blocks beside it, flush with its die face (2 m each; its die reaches 32 x 32 m)" or "no Extruder makes it that wide".
    /// </summary>
    public static string WideFix(ConstructPartDef d, int w, int h)
    {
        var most = MostSection(d);
        if (w <= MathF.Round(most.X * 100) && h <= MathF.Round(most.Y * 100) && WidenWith(d) is { } with)
        {
            var cell = d.BlockCell;
            var across = Math.Max(1, (int)MathF.Ceiling(w / 100f / cell - 0.001f));
            var up = Math.Max(1, (int)MathF.Ceiling(h / 100f / cell - 0.001f));
            return $"weld {with} beside it, flush with its die face: {across} x {up} of them ({across * cell:0.#} x {up * cell:0.#} m; its die reaches {most.X:0.##} x {most.Y:0.##} m)";
        }
        return Bigger(w, h, 0) is { } b && b != d ? BuildText(b, false) : "no Extruder makes it that wide";
    }

    /// <summary>What would make machine <paramref name="d"/> make a piece <paramref name="l"/> cm long (its section <paramref name="w"/> x <paramref name="h"/> cm).</summary>
    public static string LongFix(ConstructPartDef d, int l, int w, int h)
    {
        if (l <= MathF.Round(MostLength(d) * 100) && LengthenWith(d) is { } with)
            return $"lay {with} under its die, end to end (to {MostLength(d):0.#} m with them)";
        return Bigger(w, h, l) is { } b && b != d ? BuildText(b, true) : $"no Extruder makes a piece that long (at most {Extrusion.MaxLengthCm / 100} m)";
    }

    // ---- What the renderer reads (drawing 11's animations: drawn only, nothing on the server or the wire) ------------------------

    /// <summary>The length (cm) of the piece on this Extruder's die, 0 for none: its rams and shear move by it. Walks the Core's own links only.</summary>
    public static int PieceCm(Construct c, int core)
    {
        var sock = c.Parts[core].D.ExtDieSocket;
        for (var e = c.FirstLink(core); e >= 0; e = c.NextLink(e))
        {
            ref readonly var l = ref c.Links[e >> 1];
            if (((e & 1) == 0 ? l.SA : l.SB) == sock && c.OtherEnd(e) is var o and >= 0 && o < c.PartCount && c.Parts[o].D is { IsExtrusion: true } pd)
                return pd.ExKey.L;
        }
        return 0;
    }

    /// <summary>The Extruder Core whose die a Run-out Table serves (walked back along each table's "in" end), or -1: its rollers turn while it works.</summary>
    public static int TableMachine(Construct c, int table)
    {
        var at = table;
        for (var hop = 0; hop < 64; hop++)
        {
            var next = -1;
            var socks = c.Parts[at].D.Sockets;
            for (var e = c.FirstLink(at); e >= 0 && next < 0; e = c.NextLink(e))
            {
                ref readonly var l = ref c.Links[e >> 1];
                var s = (e & 1) == 0 ? l.SA : l.SB;
                if (s < socks.Length && socks[s].Kind == SocketKind.RunOut && socks[s].Name == "in")
                    next = c.OtherEnd(e);
            }
            if (next < 0 || next >= c.PartCount)
                return -1;
            var nd = c.Parts[next].D;
            if (nd.IsExtruder)
                return next;
            if (nd.BlockRole == BlockRole.Block)
                return MachineBlocks.TryOf(c, next, out var asm) ? asm.Core : -1;
            if (nd.ExtRunOut <= 0)
                return -1;
            at = next;
        }
        return -1;
    }
}
