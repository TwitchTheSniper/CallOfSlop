namespace CallOfSlop.MapKit;

/// <summary>
/// Gothic architecture for map generators, drawn with cosmetic meshes (looks only: no collision, no nav, no light
/// shadows; every copy of the same piece is one instanced mesh): pointed ribs, quadripartite rib vaults with their
/// bosses, mouldings run along a wall face (string courses, cornices, plinths) with gaps for openings, and timber
/// beams or coffers under a flat ceiling. The wall pieces that repeat (piers, niches, arcades, tombs) are props in
/// Props/Gothic (tools/props/gothic_props.py). First used by Ashgrave Cathedral (AshgraveCathedral.Detail.cs).
/// </summary>
public static class Gothic
{
    /// <summary>
    /// The points of a pointed arch over a span (0 to <paramref name="span"/> along s, rising <paramref name="rise"/> at the
    /// middle), <paramref name="n"/> straight pieces a side. A low arch (rise under 1.3 x the half span) is a taller one
    /// squashed, so it stays pointed.
    /// </summary>
    public static List<(double S, double Y)> ArchPoints(double span, double rise, int n)
    {
        var half = span / 2;
        var hp = Math.Max(rise, 1.3 * half);
        var squash = hp > 0 ? rise / hp : 0;
        var r = (half * half + hp * hp) / span;
        var a1 = Math.Atan2(hp, r - half);
        var pts = new List<(double S, double Y)>();
        for (var i = 0; i <= n; i++)
        {
            var a = a1 * i / n;
            pts.Add((r - r * Math.Cos(a), r * Math.Sin(a) * squash));
        }
        for (var i = n - 1; i >= 0; i--)
            pts.Add((span - pts[i].S, pts[i].Y));
        return pts;
    }

    /// <summary>A straight stone piece from one point to another (a rib segment): a box <paramref name="thick"/> tall across its run and <paramref name="depth"/> wide.</summary>
    public static void Piece(Kit k, double x0, double y0, double z0, double x1, double y1, double z1, Color color, double thick, double depth, string? material = "Concrete")
    {
        double dx = x1 - x0, dz = z1 - z0, dy = y1 - y0;
        var horiz = Math.Sqrt(dx * dx + dz * dz);
        var len = Math.Sqrt(horiz * horiz + dy * dy);
        if (len < 0.02)
            return;
        // The box's local X runs along the piece: roll lifts it, then yaw turns it (a yaw of a turns +X to (cos a, -sin a)).
        var yaw = horiz < 1e-9 ? 0 : Math.Atan2(-dz, dx) * 180 / Math.PI;
        var roll = Math.Atan2(dy, horiz) * 180 / Math.PI;
        // (Lengths in steps of 0.25 m, centred on the run, so the pieces of every rib share a few meshes: one draw call each.)
        k.MeshAt("box", (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, color, yaw: Kit.R(yaw, 2), roll: Kit.R(roll, 2), width: Quant(len + thick * 0.5),
            height: thick, depth: depth, material: material);
    }

    /// <summary>A pointed rib in the vertical plane from (x0, z0) to (x1, z1), springing at <paramref name="spring"/> and meeting at <paramref name="apex"/> over the middle.</summary>
    public static void Rib(Kit k, double x0, double z0, double x1, double z1, double spring, double apex, Color color, double thick = 0.26, double depth = 0.22, int n = 4)
    {
        double dx = x1 - x0, dz = z1 - z0;
        var span = Math.Sqrt(dx * dx + dz * dz);
        var pts = ArchPoints(span, apex - spring, n);
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            double t0 = pts[i].S / span, t1 = pts[i + 1].S / span;
            Piece(k, x0 + dx * t0, spring + pts[i].Y, z0 + dz * t0, x0 + dx * t1, spring + pts[i + 1].Y, z0 + dz * t1, color, thick, depth);
        }
    }

    /// <summary>
    /// A quadripartite rib vault over one bay (x0, z0)-(x1, z1): the two diagonal ribs, the transverse rib(s) across the
    /// bay's ends (<paramref name="alongX"/>: the bays follow X, so the transverse ribs run along Z), a ridge rib and a
    /// boss (the "Vault Boss" prop) where the diagonals cross. Returns the boss's point.
    /// </summary>
    public static (double X, double Y, double Z) RibVault(Kit k, double x0, double z0, double x1, double z1, double spring, double apex, bool alongX, Color color,
        bool firstEnd = true, bool lastEnd = true, bool ridge = true, string? boss = "Vault Boss")
    {
        double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        Rib(k, x0, z0, x1, z1, spring, apex, color);
        Rib(k, x0, z1, x1, z0, spring, apex, color);
        var tApex = apex - 0.15;
        if (alongX)
        {
            if (firstEnd) Rib(k, x0, z0, x0, z1, spring, tApex, color, 0.3, 0.26);
            if (lastEnd) Rib(k, x1, z0, x1, z1, spring, tApex, color, 0.3, 0.26);
            if (ridge) Piece(k, x0, tApex + 0.02, cz, x1, tApex + 0.02, cz, color, 0.14, 0.14);
        }
        else
        {
            if (firstEnd) Rib(k, x0, z0, x1, z0, spring, tApex, color, 0.3, 0.26);
            if (lastEnd) Rib(k, x0, z1, x1, z1, spring, tApex, color, 0.3, 0.26);
            if (ridge) Piece(k, cx, tApex + 0.02, z0, cx, tApex + 0.02, z1, color, 0.14, 0.14);
        }
        if (boss != null)
            k.Prop(boss, cx, cz, apex - 0.36);
        return (cx, apex, cz);
    }

    /// <summary>
    /// A moulding along a wall face: a band <paramref name="height"/> tall standing <paramref name="depth"/> out of the face
    /// from y, along X (<paramref name="alongX"/>) or Z from a0 to a1 on the face at <paramref name="face"/>, out towards
    /// <paramref name="facing"/> (+1 or -1), leaving the gaps (along-wall intervals) clear. A second, thinner band on top
    /// (<paramref name="cap"/> above 0) gives it a profile.
    /// </summary>
    public static void Moulding(Kit k, bool alongX, double face, int facing, double a0, double a1, double y, double height, double depth, Color color,
        IEnumerable<(double A0, double A1)>? gaps = null, double cap = 0, Color? capColor = null)
    {
        foreach (var (s0, s1) in Runs(a0, a1, gaps))
        {
            Band(k, alongX, face, facing, s0, s1, y, height, depth, color);
            if (cap > 0)
                Band(k, alongX, face, facing, s0, s1, y + height, cap, depth + 0.06, capColor ?? color);
        }
    }

    /// <summary>A length rounded up to the next 0.25 m (pieces of the same size are one instanced mesh).</summary>
    public static double Quant(double len) => Math.Ceiling(len / 0.25 - 1e-6) * 0.25;

    /// <summary>
    /// A band from s0 to s1 laid as standard pieces (2 m, or 1, 0.5 or 0.25 m for a short run), the last one pulled back
    /// to end at s1 (overlapping the one before: the same stone), so every band on a map shares a few meshes.
    /// </summary>
    static void Band(Kit k, bool alongX, double face, int facing, double s0, double s1, double y, double h, double d, Color color)
    {
        var c = face + facing * (d / 2 - 0.02);
        var r = s1 - s0;
        var q = r >= 2 ? 2.0 : r >= 1 ? 1.0 : r >= 0.5 ? 0.5 : 0.25;
        var n = Math.Max(1, (int)Math.Ceiling(r / q - 1e-6));
        for (var i = 0; i < n; i++)
        {
            var a = Math.Min(s0 + i * q, s1 - q) + q / 2;
            if (alongX)
                k.MeshAt("box", a, y + h / 2, c, color, width: q, height: h, depth: d + 0.04, material: "Concrete");
            else
                k.MeshAt("box", c, y + h / 2, a, color, width: d + 0.04, height: h, depth: q, material: "Concrete");
        }
    }

    /// <summary>The pieces of [a0, a1] outside the gaps (pieces under 0.25 m dropped).</summary>
    public static List<(double A0, double A1)> Runs(double a0, double a1, IEnumerable<(double A0, double A1)>? gaps)
    {
        var runs = new List<(double A0, double A1)> { (a0, a1) };
        foreach (var (g0, g1) in gaps ?? [])
        {
            var next = new List<(double A0, double A1)>();
            foreach (var (r0, r1) in runs)
            {
                if (g1 <= r0 || g0 >= r1)
                {
                    next.Add((r0, r1));
                    continue;
                }
                if (g0 > r0) next.Add((r0, g0));
                if (g1 < r1) next.Add((g1, r1));
            }
            runs = next;
        }
        return runs.Where(r => r.A1 - r.A0 >= 0.25).ToList();
    }

    /// <summary>
    /// Timber beams under a flat ceiling whose underside is at <paramref name="y"/>: beams across the room's short way
    /// every <paramref name="spacing"/> metres (or along X with <paramref name="alongX"/>), and with
    /// <paramref name="coffered"/> a second set the other way, lighter, making coffers.
    /// </summary>
    public static void Beams(Kit k, double x0, double z0, double x1, double z1, double y, double spacing, bool alongX, Color color, double size = 0.3, bool coffered = false, Color? cross = null)
    {
        void Set(bool ax, double sp, double s, Color c)
        {
            if (ax)
            {
                var n = Math.Max(1, (int)Math.Round((z1 - z0) / sp));
                for (var i = 1; i < n; i++)
                    k.MeshAt("box", (x0 + x1) / 2, y - s / 2, z0 + (z1 - z0) * i / n, c, width: x1 - x0, height: s, depth: s, material: "Wood");
            }
            else
            {
                var n = Math.Max(1, (int)Math.Round((x1 - x0) / sp));
                for (var i = 1; i < n; i++)
                    k.MeshAt("box", x0 + (x1 - x0) * i / n, y - s / 2, (z0 + z1) / 2, c, width: s, height: s, depth: z1 - z0, material: "Wood");
            }
        }
        Set(alongX, spacing, size, color);
        if (coffered)
            Set(!alongX, spacing, size * 0.6, cross ?? color);
    }
}
