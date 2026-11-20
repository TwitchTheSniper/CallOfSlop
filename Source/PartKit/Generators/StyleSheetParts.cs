using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The style grading sheet (Karl, 2026-11-19): for one kind of item, several distinct design interpretations of his brief,
/// built as real parts so the sheet shows them through the game's own build rules (--preview-gallery DIR --group Style).
/// He grades them and picks one; the picked look is then built into the item's real generator, and these drafts go.
/// Group "Style" is unlisted (Constructs/Categories.json), so none of them show in the Parts tab. Writes Constructs/Parts/Styles.
///
/// Round of 2026-11-20: styles are per company now, not per faction. The first company's brief (Karl): "high-tech, robust and
/// very expensive. It's practical yet ergonomic, and has recessed lights to create ambiance." Five ship parts (a crew seat, a
/// workstation console, a bulkhead door, a crew locker and a life support module), each in five takes A to E. A take is one
/// reading of the company kept across all five parts (its palette, its edges, how its light is hidden), so a column of the
/// sheet is one candidate house style. The light is never a bare strip on a face: it sits in a shadow gap between panels, under
/// a lip or inside a bezel, so it washes out of the part rather than glowing on it. Looks only: a hidden box collides, nothing works.
/// Front +z, the wall behind -z, the floor y 0.
/// </summary>
public sealed class StyleSheetParts : IPartGenerator
{
    public string Name => "style_sheet";

    private const string Out = "Constructs/Parts/Styles";
    internal const string Group = "Style";

    /// <summary>One take on the company: its palette and how heavy its edges are.</summary>
    private sealed record Co(char L, string Title, string Idea, string Body, string BodyF, string Panel, string Trim, string TrimF,
        string Dark, string Pad, string PadF, string Light, string Dim, double Ch);

    private static readonly Co[] Cos =
    [
        new('A', "Graphite", "Graphite satin panels on brushed aluminium, warm white light out of shadow gaps between the panels.",
            "#2f3338", "matte paint", "#3c4148", "#a3aab1", "aluminium", "#141619", "#2b2d30", "fabric", "#ffdcae", "#5a4a36", 0.025),
        new('B', "Ceramic", "Pale ceramic-white shells with soft wide chamfers, dark seams and cool white lines, like lab equipment.",
            "#d3d7da", "gloss paint", "#c0c5ca", "#5d646c", "aluminium", "#1e2226", "#4b5159", "fabric", "#eaf4ff", "#47525e", 0.055),
        new('C', "Armoured", "Gunmetal plate with deep chamfers, rubber corner bumpers and flush bolts, a low amber light kept down by the floor.",
            "#4b5054", "painted plating", "#5a5f63", "#2a2d30", "steel", "#131517", "#1d2023", "rubber", "#ffb867", "#5c4128", 0.07),
        new('D', "Bronze", "Slate grey with anodised bronze trim, stitched brown leather pads and soft downlights from under every lip.",
            "#34373b", "matte paint", "#44474c", "#9c7b52", "brass", "#16181a", "#5b3f2c", "fabric", "#fff0d4", "#5d5040", 0.018),
        new('E', "Navy", "Deep navy blue-grey with satin silver edges and carbon inlays; pale ice light only inside narrow slots.",
            "#28323f", "gloss paint", "#34404e", "#b8bfc6", "chrome", "#11151a", "#1c222a", "carbon weave", "#c4e6ff", "#34495c", 0.04),
    ];

    /// <summary>The drawing kit with a take's palette, and the shapes the company uses: chamfered shells and slanted slabs.</summary>
    private sealed class S(FittingLookParts.Kit k, Co c)
    {
        public readonly FittingLookParts.Kit K = k;
        public readonly Co C = c;
        public Part P => K.P;

        /// <summary>A box with every edge chamfered by c (the hull of three points per corner).</summary>
        public static IEnumerable<double[]> Ch(double x0, double y0, double z0, double x1, double y1, double z1, double c)
        {
            c = Math.Min(c, Math.Min(x1 - x0, Math.Min(y1 - y0, z1 - z0)) * 0.45);
            foreach (var (X, sx) in new[] { (x0, -1.0), (x1, 1.0) })
                foreach (var (Y, sy) in new[] { (y0, -1.0), (y1, 1.0) })
                    foreach (var (Z, sz) in new[] { (z0, -1.0), (z1, 1.0) })
                    {
                        yield return [X, Y - sy * c, Z - sz * c];
                        yield return [X - sx * c, Y, Z - sz * c];
                        yield return [X - sx * c, Y - sy * c, Z];
                    }
        }
        /// <summary>A chamfered shell; <paramref name="lean"/> pushes its top back (z per metre of height).</summary>
        public void Shell(double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish, double? c = null, double lean = 0) =>
            K.Hull(Ch(x0, y0, z0, x1, y1, z1, c ?? C.Ch).Select(q => new[] { q[0], q[1], q[2] - (q[1] - y0) * lean }), col, finish);
        public void Body(double x0, double y0, double z0, double x1, double y1, double z1, double? c = null, double lean = 0) => Shell(x0, y0, z0, x1, y1, z1, C.Body, C.BodyF, c, lean);
        public void Panel(double x0, double y0, double z0, double x1, double y1, double z1, double? c = null, double lean = 0) => Shell(x0, y0, z0, x1, y1, z1, C.Panel, C.BodyF, c, lean);
        public void Trim(double x0, double y0, double z0, double x1, double y1, double z1, double? c = null) => Shell(x0, y0, z0, x1, y1, z1, C.Trim, C.TrimF, c ?? 0.004);
        public void Pad(double x0, double y0, double z0, double x1, double y1, double z1, double? c = null, double lean = 0) => Shell(x0, y0, z0, x1, y1, z1, C.Pad, C.PadF, c ?? Math.Max(0.02, C.Ch), lean);
        public void Dark(double x0, double y0, double z0, double x1, double y1, double z1, string finish = "plastic") => K.Box(x0, y0, z0, x1, y1, z1, C.Dark, finish);
        public void Light(double x0, double y0, double z0, double x1, double y1, double z1) => K.Glow(x0, y0, z0, x1, y1, z1, C.Light);
        public void Faint(double x0, double y0, double z0, double x1, double y1, double z1) => K.Glow(x0, y0, z0, x1, y1, z1, C.Dim);

        /// <summary>A slab on the slanted face from a to b (y, z in the side view): fractions t0..t1 along it, o0..o1 out from it.</summary>
        public void Slab(double x0, double x1, (double Y, double Z) a, (double Y, double Z) b, double t0, double t1, double o0, double o1, string col, string finish, bool glow = false)
        {
            var (dy, dz) = (b.Y - a.Y, b.Z - a.Z);
            var len = Math.Sqrt(dy * dy + dz * dz);
            var (ny, nz) = (dz / len, -dy / len);
            if (ny + nz < 0) // (every slanted face here looks up or forward)
                (ny, nz) = (-ny, -nz);
            var pts = new List<double[]>();
            foreach (var t in new[] { t0, t1 })
                foreach (var o in new[] { o0, o1 })
                    foreach (var x in new[] { x0, x1 })
                        pts.Add([x, a.Y + dy * t + ny * o, a.Z + dz * t + nz * o]);
            K.Hull(pts, col, finish, glow);
        }
        /// <summary>A rubber corner bumper standing up a vertical edge (the Armoured take).</summary>
        public void Bumper(double x, double z, double y0, double y1, double sx, double sz) =>
            K.Hull(Ch(x - (sx > 0 ? 0.03 : 0.012), y0, z - (sz > 0 ? 0.03 : 0.012), x + (sx > 0 ? 0.012 : 0.03), y1, z + (sz > 0 ? 0.012 : 0.03), 0.012), "#1d2023", "rubber");
        /// <summary>A flush bolt head on a +z face.</summary>
        public void BoltZ(double x, double y, double z) => K.Hull(FittingLookParts.Cyl(x, y, 0.012, 0, 1, 6).Select(q => new[] { q[0], q[2], z + q[1] * 0.008 }), C.Trim, "steel");
    }

    private static S New(Co c, string what, string section, string desc, double[] mn, double[] mx, Cls cls)
    {
        var p = new Part($"Style {what} {c.L} {c.Title}", cls, section, $"{c.L} ({c.Title}): {desc}", tool: "welder", material: "steel", color: c.Body, group: Group);
        p.Set(("finish", c.BodyF), ("collider", "boxes")); // (the hidden box collides; the looks' hulls don't)
        p.Add(Box(mn, mx, c.Body, hidden: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        return new S(new FittingLookParts.Kit(p), c);
    }

    // ---- The crew seat: a pedestal, a contoured pan and back, armrests with grips, a headrest.

    private static Part Seat(Co c)
    {
        var desc = c.L switch
        {
            'A' => "a contoured shell on a round pedestal; warm light from the shadow gap under the seat pan and behind the headrest.",
            'B' => "a white wraparound shell, soft pads set into it, a cool light line round the shell's inner rim.",
            'C' => "a deep bucket on a boxed base, thick side bolsters, rubber bumpers, amber footwell light.",
            'D' => "stitched leather in rows on a slim frame, bronze arm caps, downlights under both armrests.",
            _ => "cantilevered off a rear spine with an ice light slot up it; floats clear of the floor.",
        };
        var s = New(c, "Seat", "Seats", "A crew seat: " + desc, [-0.3, 0, -0.38], [0.3, 1.2, 0.3], Small);
        var lean = 0.18;
        double py = c.L == 'D' ? 0.44 : 0.4; // (seat pan's underside)
        // Base.
        if (c.L == 'E')
        {
            s.Dark(-0.16, 0, -0.38, 0.16, 0.025, -0.16, "steel");
            s.Body(-0.07, 0.02, -0.37, 0.07, 1.16, -0.3, 0.02);
            s.Dark(-0.012, 0.18, -0.302, 0.012, 1.0, -0.295);
            s.Light(-0.006, 0.2, -0.297, 0.006, 0.98, -0.291);
            s.Body(-0.06, py - 0.06, -0.33, 0.06, py + 0.02, 0.0, 0.015); // (the arm the pan hangs off)
        }
        else if (c.L == 'C')
        {
            s.Body(-0.2, 0.0, -0.22, 0.2, py - 0.004, 0.16, 0.05);
            s.Dark(-0.16, 0.0, 0.14, 0.16, 0.06, 0.18, "steel");
            s.Light(-0.14, 0.03, 0.17, 0.14, 0.045, 0.182); // (amber under the base's lip)
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sz in new[] { -1.0, 1.0 })
                    s.Bumper(sx * 0.2, sz > 0 ? 0.16 : -0.22, 0.02, py - 0.04, sx, sz);
        }
        else
        {
            s.Shell(-0.2, 0, -0.2, 0.2, 0.03, 0.2, c.Dark, "steel", 0.01);
            s.K.Hull(FittingLookParts.Cyl(0, -0.02, c.L == 'B' ? 0.085 : 0.06, 0.025, py - 0.004, 8), c.L == 'B' ? c.Body : c.Trim, c.L == 'B' ? c.BodyF : c.TrimF);
            if (c.L == 'D')
                s.K.Hull(FittingLookParts.Cyl(0, -0.02, 0.075, 0.03, 0.06, 8), c.Trim, c.TrimF);
        }
        // Pan: a body shell with a lit reveal under its cushion (A, B), a bolstered bucket (C), leather rows (D).
        s.Body(-0.27, py, -0.26, 0.27, py + 0.06, 0.24);
        if (c.L is 'A' or 'B')
        {
            s.Light(-0.24, py + 0.004, 0.22, 0.24, py + 0.016, 0.246); // (under the pan's front lip)
            s.Panel(-0.275, py + 0.055, -0.27, 0.275, py + 0.085, 0.255, 0.012);
            s.Pad(-0.23, py + 0.08, -0.22, 0.23, py + 0.135, 0.24);
        }
        else if (c.L == 'C')
        {
            s.Pad(-0.2, py + 0.055, -0.22, 0.2, py + 0.12, 0.22);
            foreach (var sx in new[] { -1.0, 1.0 })
                s.Body(sx > 0 ? 0.19 : -0.29, py + 0.04, -0.26, sx > 0 ? 0.29 : -0.19, py + 0.18, 0.24, 0.05);
        }
        else if (c.L == 'D')
        {
            for (var i = 0; i < 3; i++)
                s.Pad(-0.23, py + 0.055, -0.22 + i * 0.155, 0.23, py + 0.125, -0.074 + i * 0.155, 0.025);
            s.Trim(-0.275, py + 0.02, 0.236, 0.275, py + 0.045, 0.25);
        }
        else
            s.Pad(-0.24, py + 0.055, -0.24, 0.24, py + 0.12, 0.24, 0.03);
        // Back, leaning back, and its pad.
        var by0 = py + 0.07;
        var bz = c.L == 'E' ? -0.29 : -0.27;
        s.Body(-0.26, by0, bz, 0.26, by0 + 0.62, bz + 0.07, null, lean);
        if (c.L == 'D')
            for (var i = 0; i < 4; i++)
                s.Pad(-0.22, by0 + 0.06 + i * 0.13, bz + 0.065 - (0.06 + i * 0.13) * lean, 0.22, by0 + 0.175 + i * 0.13, bz + 0.12 - (0.06 + i * 0.13) * lean, 0.025, lean);
        else
            s.Pad(-0.22, by0 + 0.04, bz + 0.065 - 0.04 * lean, 0.22, by0 + 0.56, bz + 0.125 - 0.04 * lean, null, lean);
        if (c.L is 'B' or 'C') // (side wings of the shell)
            foreach (var sx in new[] { -1.0, 1.0 })
                s.Body(sx > 0 ? 0.235 : -0.3, by0, bz, sx > 0 ? 0.3 : -0.235, by0 + 0.5, bz + 0.19, c.L == 'B' ? 0.03 : 0.045, lean);
        if (c.L == 'B')
            foreach (var sx in new[] { -1.0, 1.0 })
                s.Light(sx > 0 ? 0.236 : -0.248, by0 + 0.06, bz + 0.17 - 0.06 * lean, sx > 0 ? 0.248 : -0.236, by0 + 0.12, bz + 0.188 - 0.06 * lean);
        // Headrest, on two posts; A lights the gap behind it.
        var hy = by0 + 0.69;
        var hz = bz - 0.69 * lean;
        s.Trim(-0.08, hy - 0.08, hz + 0.02, -0.06, hy + 0.01, hz + 0.04);
        s.Trim(0.06, hy - 0.08, hz + 0.02, 0.08, hy + 0.01, hz + 0.04);
        s.Pad(-0.16, hy, hz + 0.0, 0.16, hy + 0.15, hz + 0.1, 0.035);
        if (c.L == 'A')
            s.Light(-0.13, hy - 0.035, hz + 0.004, 0.13, hy - 0.022, hz + 0.03);
        // Armrests: a post and a pad, the grip at the front (the seat is for flying or a gun).
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double x0 = sx > 0 ? 0.272 : -0.33, x1 = sx > 0 ? 0.33 : -0.272;
            var ay = py + 0.24;
            s.Body(x0 + 0.012, py + 0.03, -0.2, x1 - 0.012, ay, -0.12, 0.012);
            if (c.L == 'D')
            {
                s.Pad(x0, ay, -0.2, x1, ay + 0.045, 0.16, 0.015);
                s.Trim(x0 - 0.002, ay + 0.035, 0.13, x1 + 0.002, ay + 0.055, 0.2);
                s.Light(x0 + 0.008, ay - 0.012, -0.08, x1 - 0.008, ay + 0.002, 0.1); // (a downlight under the arm)
            }
            else
                s.Pad(x0, ay, -0.2, x1, ay + 0.045, 0.18, 0.015);
            s.K.Hull(FittingLookParts.Cyl(sx * 0.3, 0.15, 0.021, ay + 0.04, ay + 0.15, 8), c.Dark, "rubber");
            s.K.Hull(FittingLookParts.Cyl(sx * 0.3, 0.15, 0.024, ay + 0.145, ay + 0.165, 8), c.Trim, c.TrimF);
            s.Dark(sx * 0.3 - 0.008, ay + 0.12, 0.124, sx * 0.3 + 0.008, ay + 0.14, 0.13, "plastic"); // (the trigger)
        }
        return s.P.Stats(55, 300, 4, 2.0);
    }

    // ---- The workstation console: a plinth with a kick light, a raked desk with a palm rest, a screen housing over it.

    private static Part Console(Co c)
    {
        var desc = c.L switch
        {
            'A' => "a raked desk and a two-screen hood; warm light from the gap where the hood meets the desk and from the toe kick.",
            'B' => "one rounded white body, a wide single screen under a deep brow, cool light in the brow.",
            'C' => "heavy plated body with grab bars and corner guards; amber light only at the toe kick.",
            'D' => "slate desk on a slim plinth, bronze edge and a leather palm rest, downlight from the screen hood.",
            _ => "a desk floating on one central pedestal over an ice-lit slot, carbon desk inlay, three screens.",
        };
        var s = New(c, "Console", "Consoles", "A workstation: " + desc, [-0.55, 0, -0.4], [0.55, 1.4, 0.34], Small);
        double w = 0.55, dy = 0.78; // (half width; desk height at the front)
        (double Y, double Z) front = (dy, 0.34), back = (0.88, -0.06);
        // Plinth and the toe kick light (all but E).
        if (c.L == 'E')
        {
            s.Body(-0.12, 0, -0.38, 0.12, 0.6, -0.12, 0.02);
            s.Dark(-0.02, 0.04, -0.122, 0.02, 0.56, -0.115);
            s.Light(-0.008, 0.06, -0.117, 0.008, 0.54, -0.11);
            s.K.Profile(-w, w, [(0.6, -0.4), (0.6, 0.3), (front.Y - 0.05, 0.34), front, back, (0.88, -0.4)], c.Body, c.BodyF);
            s.Light(-w + 0.06, 0.585, -0.3, w - 0.06, 0.598, 0.26); // (a wash under the floating desk)
        }
        else
        {
            var kz = c.L == 'D' ? 0.05 : 0.16;
            s.Dark(-w + 0.04, 0, -0.38, w - 0.04, 0.105, kz, "steel");
            s.Light(-w + 0.08, 0.08, kz - 0.012, w - 0.08, 0.094, kz + 0.002);
            var bodyLo = c.L == 'D' ? 0.55 : 0.1;
            if (c.L == 'D')
                s.Body(-0.3, 0.1, -0.36, 0.3, 0.56, kz - 0.02, 0.015);
            s.K.Profile(-w, w, [(bodyLo, -0.4), (bodyLo, 0.28), (front.Y - 0.06, 0.34), front, back, (0.88, -0.4)], c.Body, c.BodyF);
            if (c.L == 'B') // (soften the ends: rounded side caps)
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.Shell(sx > 0 ? w - 0.004 : -w - 0.03, bodyLo + 0.02, -0.38, sx > 0 ? w + 0.03 : -w + 0.004, 0.86, 0.3, c.Body, c.BodyF, 0.03);
            if (c.L == 'C')
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    s.Bumper(sx * w, 0.3, 0.12, 0.76, sx, 1);
                    s.Trim(sx > 0 ? w - 0.06 : -w + 0.04, 0.42, 0.302, sx > 0 ? w - 0.04 : -w + 0.06, 0.66, 0.33, 0.006);
                    for (var i = 0; i < 3; i++)
                        s.BoltZ(sx * (w - 0.12), 0.2 + i * 0.2, 0.282 + (0.2 + i * 0.2 - 0.1) * 0.06 / (front.Y - 0.16));
                }
            // Front panel seams: a lower service panel with a dark reveal.
            if (c.L != 'D')
                s.Dark(-w + 0.06, 0.4, 0.302, w - 0.06, 0.412, 0.318);
        }
        // The desk: an inlay and a palm rest along the front edge (the ergonomic part).
        s.Slab(-w + 0.05, w - 0.05, front, back, 0.2, 0.9, -0.004, 0.006, c.L == 'E' ? "#1b1f24" : c.Panel, c.L == 'E' ? "carbon weave" : c.BodyF);
        if (c.L == 'D')
            s.Slab(-w - 0.004, w + 0.004, front, back, -0.03, 0.04, -0.01, 0.012, c.Trim, c.TrimF);
        s.Slab(-w + 0.08, w - 0.08, front, back, 0.02, 0.16, -0.004, 0.03, c.Pad, c.PadF);
        // Controls on the desk: a key strip and two small lit buttons, faint.
        s.Slab(-0.22, 0.22, front, back, 0.32, 0.5, 0.004, 0.014, c.Dark, "plastic");
        s.Slab(0.28, 0.34, front, back, 0.36, 0.44, 0.004, 0.016, c.Dim, "", true);
        s.Slab(0.38, 0.44, front, back, 0.36, 0.44, 0.004, 0.016, c.Dim, "", true);
        // The hood: screens on its raked face, the lit gap under it.
        (double Y, double Z) h0 = (0.9, -0.1), h1 = (1.36, -0.22);
        var hb = c.L == 'B' ? 0.06 : 0.0; // (B's brow sticks out further)
        s.K.Profile(-w + 0.02, w - 0.02, [(0.875, -0.396), h0, h1, (1.4, -0.27 + hb), (1.4, -0.396)], c.Body, c.BodyF);
        s.Light(-w + 0.06, 0.88, -0.115, w - 0.06, 0.892, -0.09); // (the gap where the hood meets the desk)
        if (c.L is 'B' or 'D')
        {
            s.Shell(-w + 0.02, 1.38, -0.32, w - 0.02, 1.44, -0.12 + hb, c.L == 'D' ? c.Trim : c.Body, c.L == 'D' ? c.TrimF : c.BodyF, 0.02);
            s.Light(-w + 0.08, 1.374, -0.2 + hb * 0.5, w - 0.08, 1.386, -0.14 + hb); // (a downlight in the brow)
        }
        var screens = c.L switch { 'B' => 1, 'E' => 3, _ => 2 };
        var gap = 0.03;
        var sw = (2 * (w - 0.06) - gap * (screens - 1)) / screens;
        for (var i = 0; i < screens; i++)
        {
            var x0 = -w + 0.06 + i * (sw + gap);
            // (down the raked face, top to bottom)
            s.Slab(x0, x0 + sw, h1, h0, 0.06, 0.94, -0.004, 0.008, c.Dark, "plastic");
            s.Slab(x0 + 0.03, x0 + sw - 0.03, h1, h0, 0.7, 0.76, 0.008, 0.012, c.Dim, "", true); // (a line of its readout)
            s.Slab(x0 + 0.03, x0 + sw * 0.55, h1, h0, 0.5, 0.55, 0.008, 0.012, c.Dim, "", true);
        }
        if (c.L == 'C') // (grab bars on the hood's sides)
            foreach (var sx in new[] { -1.0, 1.0 })
                s.Trim(sx > 0 ? w - 0.015 : -w - 0.01, 1.0, -0.16, sx > 0 ? w + 0.01 : -w + 0.015, 1.3, -0.13, 0.006);
        return s.P.Stats(120, 400, 4, 3.0);
    }

    // ---- The bulkhead door: a 1 x 2.1 m opening in a 1.7 x 2.6 m frame, the leaf shut.

    private static Part Door(Co c)
    {
        var desc = c.L switch
        {
            'A' => "square frame with cut top corners; warm light in a reveal round the opening and a downlight in the head.",
            'B' => "a rounded white portal with wide cut corners, two leaves meeting in the middle, a cool light line round the portal.",
            'C' => "thick plated frame with bolted jamb plates and two rams on the head, amber light in the sill.",
            'D' => "slate frame with a bronze inner lining, a tall pull and soft downlights out of the head.",
            _ => "angled jambs narrowing to the head, ice light in slots up both jambs, a carbon panel in the leaf.",
        };
        var s = New(c, "Door", "Doors", "A bulkhead door: " + desc, [-0.85, 0, -0.2], [0.85, 2.6, 0.2], Medium);
        double ow = 0.5, oh = 2.15, d = 0.2;
        // Jambs, head, sill.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            if (c.L == 'E')
                s.K.Face(-d, d, sx > 0 ? [(ow, 0), (0.85, 0), (0.85, 2.6), (ow - 0.08, 2.6)] : [(-0.85, 0), (-ow, 0), (-ow + 0.08, 2.6), (-0.85, 2.6)], c.Body, c.BodyF);
            else
                s.Body(sx > 0 ? ow : -0.85, 0, -d, sx > 0 ? 0.85 : -ow, 2.6, d, c.Ch * 0.6);
        }
        if (c.L == 'E')
            s.K.Face(-d + 0.004, d - 0.004, [(-0.43, oh), (0.43, oh), (0.43, 2.596), (-0.43, 2.596)], c.Body, c.BodyF);
        else
            s.Body(-ow - 0.01, oh, -d + 0.004, ow + 0.01, 2.596, d - 0.004, c.Ch * 0.4);
        s.Trim(-ow + 0.004, 0, -d + 0.02, ow - 0.004, 0.04, d - 0.02, 0.004);
        // Cut top corners (all but C), big for B.
        if (c.L != 'C')
        {
            var g = c.L == 'B' ? 0.3 : c.L == 'E' ? 0.0 : 0.14;
            if (g > 0)
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.K.Face(-d + 0.006, d - 0.006, [(sx * (ow + 0.004), oh - g), (sx * (ow + 0.004), oh + 0.004), (sx * (ow - g), oh + 0.004)], c.Body, c.BodyF);
            if (c.L == 'B') // (and the bottom corners a little)
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.K.Face(-d + 0.006, d - 0.006, [(sx * (ow + 0.004), 0.12), (sx * (ow + 0.004), 0.036), (sx * (ow - 0.084), 0.036)], c.Body, c.BodyF);
        }
        // The leaf (two for B), set back in the frame.
        if (c.L == 'B')
        {
            foreach (var sx in new[] { -1.0, 1.0 })
                s.Panel(sx > 0 ? 0.006 : -ow + 0.01, 0.045, -0.045, sx > 0 ? ow - 0.01 : -0.006, oh - 0.01, 0.045, 0.012);
            s.Dark(-0.006, 0.05, -0.04, 0.006, oh - 0.02, 0.04);
            s.Light(-0.003, 1.0, 0.03, 0.003, 1.3, 0.05); // (the leaves' meeting line lit at hand height)
        }
        else
            s.Panel(-ow + 0.01, 0.045, -0.04, ow - 0.01, oh - 0.01, 0.04, 0.01);
        if (c.L == 'E')
            s.Shell(-0.3, 0.5, 0.036, 0.3, 1.7, 0.05, "#1b1f24", "carbon weave", 0.01);
        if (c.L is 'A' or 'C')
            for (var i = 0; i < 2; i++) // (pressed ribs across the leaf)
                s.Panel(-0.4, 0.6 + i * 0.9, 0.036, 0.4, 0.7 + i * 0.9, 0.056, 0.01);
        // Handle and lock lamp, on the +x side.
        if (c.L == 'D')
            s.Trim(0.32, 0.8, 0.04, 0.35, 1.5, 0.07, 0.008);
        else
            s.Dark(0.3, 0.95, 0.036, 0.4, 1.15, 0.05);
        s.Faint(0.33, 1.3, 0.036, 0.37, 1.34, 0.052);
        // Recessed light: where and how each take hides it.
        switch (c.L)
        {
            case 'A': // (a reveal: a trim bezel stood proud on the face round the opening, the light in the gap behind it)
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    s.Trim(sx > 0 ? ow + 0.05 : -ow - 0.07, 0.04, d - 0.004, sx > 0 ? ow + 0.07 : -ow - 0.05, oh + 0.06, d + 0.018);
                    s.Light(sx > 0 ? ow + 0.072 : -ow - 0.084, 0.06, d - 0.008, sx > 0 ? ow + 0.084 : -ow - 0.072, oh + 0.04, d + 0.004);
                }
                s.Trim(-ow - 0.07, oh + 0.06, d - 0.004, ow + 0.07, oh + 0.08, d + 0.018);
                s.Light(-ow - 0.05, oh + 0.082, d - 0.008, ow + 0.05, oh + 0.094, d + 0.004);
                s.Light(-0.3, oh - 0.004, 0.08, 0.3, oh + 0.008, 0.16); // (downlight in the head's soffit)
                break;
            case 'B':
                s.Light(-ow + 0.03, oh - 0.004, 0.14, ow - 0.03, oh + 0.008, 0.17);
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.Light(sx > 0 ? ow - 0.004 : -ow - 0.008, 0.2, 0.14, sx > 0 ? ow + 0.008 : -ow + 0.004, oh - 0.36, 0.17);
                break;
            case 'C':
                s.Light(-ow + 0.03, 0.03, 0.06, ow - 0.03, 0.046, 0.16); // (in the sill)
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    s.Panel(sx > 0 ? ow + 0.06 : -0.8, 0.3, d - 0.004, sx > 0 ? 0.8 : -ow - 0.06, 2.3, d + 0.02, 0.012);
                    foreach (var y in new[] { 0.36, 1.3, 2.24 })
                        foreach (var bx in new[] { ow + 0.1, 0.76 })
                            s.BoltZ(sx * bx, y, d + 0.016);
                    s.Bumper(sx * 0.85, d, 0.02, 2.4, sx, 1);
                    // A ram on the head.
                    s.K.Hull(FittingLookParts.Cyl(0, 0, 0.03, 0, 1, 8).Select(q => new[] { sx * (0.25 + q[1] * 0.35), oh + 0.12 + q[0], d + 0.05 + q[2] }), c.Trim, "steel");
                    s.Dark(sx > 0 ? 0.58 : -0.66, oh + 0.07, d - 0.004, sx > 0 ? 0.66 : -0.58, oh + 0.17, d + 0.05, "steel");
                }
                break;
            case 'D':
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.Trim(sx > 0 ? ow - 0.006 : -ow - 0.012, 0.04, -d + 0.03, sx > 0 ? ow + 0.012 : -ow + 0.006, oh + 0.006, d - 0.03);
                s.Trim(-ow - 0.012, oh - 0.006, -d + 0.03, ow + 0.012, oh + 0.012, d - 0.03);
                s.Shell(-0.6, oh + 0.16, d - 0.004, 0.6, oh + 0.22, d + 0.06, c.Trim, c.TrimF, 0.008); // (a lip on the face over the door)
                s.Light(-0.55, oh + 0.154, d + 0.004, 0.55, oh + 0.166, d + 0.05);
                foreach (var x in new[] { -0.25, 0.25 })
                    s.Light(x - 0.04, oh - 0.004, 0.1, x + 0.04, oh + 0.002, 0.16);
                break;
            default:
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    s.K.Face(d - 0.004, d + 0.006, sx > 0 ? [(ow + 0.1, 0.2), (ow + 0.13, 0.2), (ow + 0.13 - 0.06, 2.3), (ow + 0.1 - 0.06, 2.3)] : [(-ow - 0.13, 0.2), (-ow - 0.1, 0.2), (-ow - 0.1 + 0.06, 2.3), (-ow - 0.13 + 0.06, 2.3)], c.Dark, "plastic");
                    s.K.Face(d + 0.002, d + 0.01, sx > 0 ? [(ow + 0.108, 0.22), (ow + 0.122, 0.22), (ow + 0.122 - 0.057, 2.28), (ow + 0.108 - 0.057, 2.28)] : [(-ow - 0.122, 0.22), (-ow - 0.108, 0.22), (-ow - 0.108 + 0.057, 2.28), (-ow - 0.122 + 0.057, 2.28)], c.Light, "", glow: true);
                }
                s.Trim(-0.43, 2.45, d - 0.004, 0.43, 2.48, d + 0.01);
                break;
        }
        return s.P.Stats(400, 1200, 10, 5.0);
    }

    // ---- The crew locker: the crew locker's footprint (0.6 x 0.45 x 1.9 m).

    private static Part Locker(Co c)
    {
        var desc = c.L switch
        {
            'A' => "upper and lower doors with a lit reveal between them, a warm wash out of the toe kick.",
            'B' => "one white door with a tall lit pull groove down its edge, soft corners.",
            'C' => "armoured door with rubber edge bumpers and a drop-down crown, amber light under the crown.",
            'D' => "louvred upper door, solid lower door, a bronze pull bar and a downlight under the crown.",
            _ => "a sealed door with cut corners and an ice slot up the side of the carcass.",
        };
        var s = New(c, "Locker", "Lockers", "A crew locker: " + desc, [-0.3, 0, -0.25], [0.3, 1.9, 0.2], Small);
        double F = 0.2; // (the carcass's front face)
        s.Dark(-0.27, 0, -0.23, 0.27, 0.09, F - 0.05, "steel");
        s.Body(-0.3, 0.085, -0.25, 0.3, 1.9, F, c.Ch * 0.7);
        switch (c.L)
        {
            case 'A':
                s.Light(-0.24, 0.006, F - 0.062, 0.24, 0.016, F - 0.048);
                s.Panel(-0.27, 0.12, F - 0.004, 0.27, 0.9, F + 0.018, 0.008);
                s.Panel(-0.27, 0.95, F - 0.004, 0.27, 1.84, F + 0.018, 0.008);
                s.Dark(-0.265, 0.9, F - 0.006, 0.265, 0.95, F + 0.004);
                s.Light(-0.25, 0.915, F - 0.002, 0.25, 0.935, F + 0.008);
                s.Trim(0.2, 1.0, F + 0.016, 0.23, 1.3, F + 0.034);
                s.Trim(0.2, 0.55, F + 0.016, 0.23, 0.85, F + 0.034);
                break;
            case 'B':
                s.Panel(-0.275, 0.12, F - 0.004, 0.275, 1.85, F + 0.02, 0.02);
                s.Dark(0.18, 0.5, F + 0.008, 0.23, 1.5, F + 0.024);
                s.Light(0.196, 0.53, F + 0.012, 0.214, 1.47, F + 0.03);
                s.Faint(-0.2, 1.7, F + 0.018, -0.12, 1.72, F + 0.026);
                break;
            case 'C':
                s.Panel(-0.26, 0.12, F - 0.004, 0.26, 1.72, F + 0.03, 0.03);
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    s.Bumper(sx * 0.3, F, 0.1, 1.72, sx, 1);
                    foreach (var y in new[] { 0.2, 0.92, 1.64 })
                        s.BoltZ(sx * 0.2, y, F + 0.028);
                }
                s.Trim(-0.12, 0.86, F + 0.028, 0.12, 0.92, F + 0.06);
                s.Body(-0.305, 1.75, -0.255, 0.305, 1.905, F + 0.06, 0.04);
                s.Light(-0.25, 1.744, F + 0.0, 0.25, 1.756, F + 0.045);
                break;
            case 'D':
                s.Panel(-0.27, 0.12, F - 0.004, 0.27, 0.8, F + 0.018, 0.006);
                s.Panel(-0.27, 0.84, F - 0.004, 0.27, 1.76, F + 0.018, 0.006);
                for (var i = 0; i < 9; i++)
                    s.Dark(-0.2, 1.2 + i * 0.055, F + 0.014, 0.2, 1.222 + i * 0.055, F + 0.024, "steel");
                s.Trim(-0.16, 0.95, F + 0.016, 0.16, 0.98, F + 0.05, 0.006);
                s.Shell(-0.31, 1.8, -0.255, 0.31, 1.905, F + 0.05, c.Trim, c.TrimF, 0.008);
                s.Light(-0.26, 1.794, F - 0.002, 0.26, 1.806, F + 0.04);
                break;
            default:
            {
                (double X, double Y)[] Cut(double w, double y0, double y1, double k) => [(-w + k, y0), (w - k, y0), (w, y0 + k), (w, y1 - k), (w - k, y1), (-w + k, y1), (-w, y1 - k), (-w, y0 + k)];
                s.K.Face(F - 0.004, F + 0.02, Cut(0.255, 0.13, 1.82, 0.08), c.Panel, c.BodyF);
                s.K.Face(F + 0.016, F + 0.03, Cut(0.15, 0.4, 1.5, 0.05), "#1b1f24", "carbon weave");
                s.Dark(0.296, 0.2, -0.15, 0.31, 1.75, -0.11);
                s.Light(0.304, 0.23, -0.142, 0.314, 1.72, -0.118);
                s.Faint(-0.02, 1.62, F + 0.016, 0.02, 1.66, F + 0.03);
                break;
            }
        }
        return s.P.Stats(60, 320, 6, 2.0);
    }

    // ---- The life support module: a 1x1 module on the standard Module Plug look, Data 1 and Coolant A.

    private static Part LifeSupport(Co c)
    {
        var desc = c.L switch
        {
            'A' => "two filter canisters in a recessed front bay lit warm from below, a fan grille on top.",
            'B' => "a white tower with a round intake on its face, a cool light ring set back inside it.",
            'C' => "an armoured box with a guarded pipe manifold on the front and amber light behind its vent slots.",
            'D' => "slate with bronze corner posts, a louvred intake and a downlight over the service panel.",
            _ => "navy with a tall slot window down the front, the filter column lit ice inside it.",
        };
        var H = 1.3;
        var k = FittingLookParts.Reactor("", 1, $"{c.L} ({c.Title}): A life support unit: " + desc, H, $"Style Life Support {c.L} {c.Title}", "Life support", ["Data 1", "Coolant A"], group: Group);
        var s = new S(k, c);
        var h = 0.376;
        double F = h; // (front face)
        s.Body(-h, 0.078, -h, h, H, h);
        switch (c.L)
        {
            case 'A':
                s.Dark(-0.3, 0.25, F - 0.12, 0.3, 1.0, F + 0.004, "steel"); // (the bay)
                foreach (var x in new[] { -0.15, 0.15 })
                {
                    s.K.Hull(FittingLookParts.Cyl(x, F - 0.06, 0.09, 0.3, 0.95, 10), c.Trim, c.TrimF);
                    s.K.Hull(FittingLookParts.Cyl(x, F - 0.06, 0.095, 0.4, 0.43, 10), c.Dark, "rubber");
                    s.K.Hull(FittingLookParts.Cyl(x, F - 0.06, 0.095, 0.8, 0.83, 10), c.Dark, "rubber");
                }
                s.Light(-0.29, 0.255, F - 0.11, 0.29, 0.268, F - 0.004);
                s.Panel(-0.32, 1.0, F - 0.004, 0.32, 1.06, F + 0.03, 0.01); // (the lip over the bay)
                s.Dark(-0.25, H - 0.004, -0.25, 0.25, H + 0.02, 0.25, "steel");
                for (var i = 0; i < 6; i++)
                    s.Trim(-0.24, H + 0.004, -0.22 + i * 0.088, 0.24, H + 0.03, -0.2 + i * 0.088);
                break;
            case 'B':
                s.Shell(-h - 0.01, 0.078, -h - 0.01, h + 0.01, 0.3, h + 0.01, c.Panel, c.BodyF, 0.05);
                s.K.Hull(FittingLookParts.Cyl(0, 0.75, 0.24, 0, 1, 12).Select(q => new[] { q[0], q[2], F - 0.004 + q[1] * 0.024 }), c.Panel, c.BodyF);
                s.K.Hull(FittingLookParts.Cyl(0, 0.75, 0.2, 0, 1, 12).Select(q => new[] { q[0], q[2], F + 0.012 + q[1] * 0.016 }), c.Light, "", glow: true);
                s.K.Hull(FittingLookParts.Cyl(0, 0.75, 0.185, 0, 1, 12).Select(q => new[] { q[0], q[2], F + 0.014 + q[1] * 0.02 }), c.Dark, "steel");
                for (var i = 0; i < 5; i++)
                    s.Trim(-0.17 + i * 0.085 - 0.006, 0.6, F + 0.03, -0.17 + i * 0.085 + 0.006, 0.9, F + 0.042);
                s.Faint(-0.08, 1.15, F - 0.004, 0.08, 1.17, F + 0.008);
                break;
            case 'C':
                foreach (var sx in new[] { -1.0, 1.0 })
                    foreach (var sz in new[] { -1.0, 1.0 })
                        s.Bumper(sx * h, sz * h, 0.2, H - 0.03, sx, sz);
                for (var i = 0; i < 4; i++)
                {
                    s.Dark(-0.28, 0.95 + i * 0.07, F - 0.004, 0.28, 0.98 + i * 0.07, F + 0.006, "steel");
                    s.Light(-0.26, 0.958 + i * 0.07, F - 0.006, 0.26, 0.972 + i * 0.07, F + 0.004);
                }
                foreach (var x in new[] { -0.2, 0.0, 0.2 })
                    s.K.Hull(FittingLookParts.Cyl(x, F + 0.05, 0.03, 0.25, 0.85, 8), c.Trim, "steel");
                s.Trim(-0.3, 0.3, F + 0.0, 0.3, 0.34, F + 0.1);
                s.Trim(-0.3, 0.78, F + 0.0, 0.3, 0.82, F + 0.1);
                s.Panel(-0.32, 0.2, F + 0.09, 0.32, 0.25, F + 0.11, 0.008); // (the guard rail)
                foreach (var y in new[] { 0.3, 0.8 })
                    foreach (var x in new[] { -0.3, 0.3 })
                        s.BoltZ(x, y + 0.02, F + 0.1);
                break;
            case 'D':
                foreach (var sx in new[] { -1.0, 1.0 })
                    foreach (var sz in new[] { -1.0, 1.0 })
                        s.Trim(sx > 0 ? h - 0.05 : -h - 0.008, 0.08, sz > 0 ? h - 0.05 : -h - 0.008, sx > 0 ? h + 0.008 : -h + 0.05, H + 0.01, sz > 0 ? h + 0.008 : -h + 0.05, 0.006);
                for (var i = 0; i < 8; i++)
                    s.Panel(-0.27, 0.78 + i * 0.05, F - 0.004, 0.27, 0.8 + i * 0.05, F + 0.022, 0.004);
                s.Panel(-0.27, 0.2, F - 0.004, 0.27, 0.66, F + 0.014, 0.006);
                s.Trim(-0.29, 0.7, F - 0.004, 0.29, 0.73, F + 0.06, 0.006);
                s.Light(-0.25, 0.694, F + 0.012, 0.25, 0.704, F + 0.05);
                s.Faint(0.18, 0.56, F + 0.012, 0.22, 0.6, F + 0.022);
                break;
            default:
                s.Dark(-0.07, 0.25, F - 0.03, 0.07, 1.15, F + 0.004, "steel");
                s.K.Hull(FittingLookParts.Cyl(0, F - 0.045, 0.04, 0.27, 1.13, 8), c.Light, "", glow: true);
                for (var i = 0; i < 6; i++)
                    s.Trim(-0.06, 0.32 + i * 0.14, F - 0.004, 0.06, 0.34 + i * 0.14, F + 0.01);
                foreach (var sx in new[] { -1.0, 1.0 })
                    s.Shell(sx > 0 ? 0.12 : -0.3, 0.3, F - 0.004, sx > 0 ? 0.3 : -0.12, 1.1, F + 0.012, "#1b1f24", "carbon weave", 0.008);
                s.Faint(0.15, 1.18, F - 0.004, 0.27, 1.2, F + 0.008);
                break;
        }
        return s.P.Stats(500, 900, 8, 6.0);
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var all = new List<Part>();
        foreach (var c in Cos)
            all.AddRange([Seat(c), Console(c), Door(c), Locker(c), LifeSupport(c)]);
        foreach (var p in all)
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
