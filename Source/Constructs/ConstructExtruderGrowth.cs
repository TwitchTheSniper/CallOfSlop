using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// An Extruder's expansion pieces (docs/sandbox/constructs/production.md "Extrusion", "Expansion pieces"): the small Extruder's Die Frames,
/// a big machine's Die Extensions (flush with its die face, each a row or column of die up to its "growSection") and the Run-out Tables of
/// every class but the Capital (each adds to the longest piece, up to its "growLength"). A piece counts only on a machine of its own size
/// class, joined to it directly or through other pieces, and not broken. Everything is read from the construct's parts and links, so a
/// client's copy agrees with the host's after a piece is welded on or shot off.
/// </summary>
public static partial class ConstructExtruder
{
    /// <summary>
    /// The machine's die (centre and size across the face, in its own frame) and its longest piece (m), its expansion pieces counted.
    /// One walk over the pieces joined to it.
    /// </summary>
    public static void Reach(Construct c, int part, out Vector2 centre, out Vector2 size, out float length)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var half = d.ExtDie * 0.5f;
        var at = new Vector2(d.ExtDieAt.X, d.ExtDieAt.Y);
        var min = at - half;
        var max = at + half;
        var frames = d.ExtDie.X < d.ExtMaxSection.X || d.ExtDie.Y < d.ExtMaxSection.Y;
        var extends = d.ExtGrowSection.X > d.ExtMaxSection.X || d.ExtGrowSection.Y > d.ExtMaxSection.Y;
        var runs = d.ExtGrowLength > d.ExtMaxLength;
        float px = 0, nx = 0, py = 0, ny = 0, run = 0;
        if (frames || extends || runs)
        {
            using var scratch = Scratch.Begin();
            Span<bool> seen = scratch.Take<bool>(c.PartCount);
            Span<ushort> queue = scratch.Take<ushort>(c.PartCount);
            int head = 0, tail = 0;
            seen[part] = true;
            queue[tail++] = (ushort)part;
            var back = Quaternion.Conjugate(Rot24.Quat(p.Rot));
            while (head < tail)
            {
                int from = queue[head++];
                for (var k = 0; k < c.LinkCount; k++)
                {
                    ref var l = ref c.Links[k];
                    int other = l.A == from ? l.B : l.B == from ? l.A : -1;
                    if (other < 0 || other >= c.PartCount || seen[other] || c.Parts[other].Broken)
                        continue;
                    var od = c.Parts[other].D;
                    var frame = frames && od.IsDieFrame;
                    var ext = extends && od.ExtGrowStep > 0 && od.Class == d.Class;
                    var table = runs && od.ExtRunOut > 0 && od.Class == d.Class;
                    if (!frame && !ext && !table)
                        continue;
                    seen[other] = true;
                    queue[tail++] = (ushort)other;
                    if (table)
                    {
                        run += od.ExtRunOut;
                        continue;
                    }
                    c.PartBounds(other, out var mn, out var mx);
                    var a = Vector3.Transform(mn - p.Pos, back);
                    var b = Vector3.Transform(mx - p.Pos, back);
                    if (frame)
                    {
                        min = Vector2.Min(min, new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y)));
                        max = Vector2.Max(max, new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y)));
                        continue;
                    }
                    // A Die Extension adds its row on the side of the die it stands on, when it is flush with the die face.
                    if (MathF.Max(a.Z, b.Z) < d.ExtDieAt.Z - 0.1f)
                        continue;
                    var off = (new Vector2(a.X + b.X, a.Y + b.Y) * 0.5f) - at;
                    if (MathF.Abs(off.X) >= MathF.Abs(off.Y))
                    {
                        if (off.X >= 0) px += od.ExtGrowStep;
                        else nx += od.ExtGrowStep;
                    }
                    else if (off.Y >= 0)
                        py += od.ExtGrowStep;
                    else
                        ny += od.ExtGrowStep;
                }
            }
        }
        // Die Extensions up to the machine's most, the far side first.
        var roomX = MathF.Max(0, d.ExtGrowSection.X - (max.X - min.X));
        var roomY = MathF.Max(0, d.ExtGrowSection.Y - (max.Y - min.Y));
        px = MathF.Min(px, roomX);
        nx = MathF.Min(nx, roomX - px);
        py = MathF.Min(py, roomY);
        ny = MathF.Min(ny, roomY - py);
        max += new Vector2(px, py);
        min -= new Vector2(nx, ny);
        centre = (min + max) * 0.5f;
        size = Vector2.Min(max - min, extends ? d.ExtGrowSection : d.ExtMaxSection);
        length = MathF.Min(MathF.Min(d.ExtMaxLength + run, MathF.Max(d.ExtMaxLength, d.ExtGrowLength)), Extrusion.MaxLengthCm / 100f);
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

    /// <summary>The widest a machine's die gets with its expansion pieces (m): Die Frames or Die Extensions.</summary>
    public static Vector2 MostSection(ConstructPartDef d) => Vector2.Max(d.ExtMaxSection, d.ExtGrowSection);

    /// <summary>The longest piece a machine makes with its Run-out Tables (m).</summary>
    public static float MostLength(ConstructPartDef d) => MathF.Min(MathF.Max(d.ExtMaxLength, d.ExtGrowLength), Extrusion.MaxLengthCm / 100f);

    /// <summary>What widens this machine's die ("Die Frames", "Medium Die Extensions"), or null when nothing does.</summary>
    public static string? WidenWith(ConstructPartDef d)
    {
        if (d.ExtDie.X < d.ExtMaxSection.X || d.ExtDie.Y < d.ExtMaxSection.Y)
            return "Die Frames";
        if (d.ExtGrowSection.X <= d.ExtMaxSection.X && d.ExtGrowSection.Y <= d.ExtMaxSection.Y)
            return null;
        foreach (var e in ConstructParts.All)
            if (e.ExtGrowStep > 0 && e.Class == d.Class)
                return e.Name + "s";
        return null;
    }

    /// <summary>What makes this machine's pieces longer ("Run-out Tables", "Medium Run-out Tables"), or null.</summary>
    public static string? LengthenWith(ConstructPartDef d)
    {
        if (d.ExtGrowLength <= d.ExtMaxLength)
            return null;
        foreach (var e in ConstructParts.All)
            if (e.ExtRunOut > 0 && e.Class == d.Class)
                return e.Name + "s";
        return null;
    }

    private static bool Sections(ConstructPartDef d) => d.IsExtruder && !d.IsWheelFormer && !d.IsPanelFormer && !WorkOrders.IsWholeFormer(d);

    /// <summary>The smallest catalogue Extruder that makes a <paramref name="w"/> x <paramref name="h"/> cm section <paramref name="l"/> cm long
    /// (0: any length) with its expansion pieces; null when none does.</summary>
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

    /// <summary>A catalogue machine as the text names it: "a Large Extruder (dies to 16 m, 32 m with Large Die Extensions)".</summary>
    public static string BuildText(ConstructPartDef b, bool length)
    {
        if (length)
            return LengthenWith(b) is { } t && MostLength(b) > b.ExtMaxLength
                ? $"build a {b.Name} (to {b.ExtMaxLength:0.#} m, {MostLength(b):0.#} m with {t})"
                : $"build a {b.Name} (to {MostLength(b):0.#} m)";
        return WidenWith(b) is { } w && MostSection(b).X > b.ExtDie.X
            ? $"build a {b.Name} (dies to {b.ExtDie.X:0.##} m, {MostSection(b).X:0.##} m with {w})"
            : $"build a {b.Name} (dies to {MostSection(b).X:0.##} m)";
    }

    /// <summary>
    /// What would make machine <paramref name="part"/> take a section <paramref name="w"/> x <paramref name="h"/> cm its die doesn't take now
    /// (the user, 2026-11-13: an expansion piece on this machine first, then the bigger Extruder): "weld Medium Die Extensions on its die face
    /// (it reaches 16 x 16 m with them)" or "build a Large Extruder (...)" or "no Extruder makes it".
    /// </summary>
    public static string WideFix(ConstructPartDef d, int w, int h)
    {
        var most = MostSection(d);
        if (w <= MathF.Round(most.X * 100) && h <= MathF.Round(most.Y * 100) && WidenWith(d) is { } with)
            return d.ExtDie.X < d.ExtMaxSection.X
                ? $"attach {with} to its sides (its die reaches {most.X:0.##} x {most.Y:0.##} m with them)"
                : $"weld {with} flush with its die face (its die reaches {most.X:0.##} x {most.Y:0.##} m with them)";
        return Bigger(w, h, 0) is { } b ? BuildText(b, false) : "no Extruder makes it that wide";
    }

    /// <summary>What would make machine <paramref name="d"/> make a piece <paramref name="l"/> cm long (its section <paramref name="w"/> x <paramref name="h"/> cm).</summary>
    public static string LongFix(ConstructPartDef d, int l, int w, int h)
    {
        if (l <= MathF.Round(MostLength(d) * 100) && LengthenWith(d) is { } with)
            return $"add {with} to its out bed (to {MostLength(d):0.#} m with them)";
        return Bigger(w, h, l) is { } b ? BuildText(b, true) : $"no Extruder makes a piece that long (at most {Extrusion.MaxLengthCm / 100} m)";
    }
}
