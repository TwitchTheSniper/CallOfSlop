namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The ammo socket, docking and boarding pieces (Karl, 2026-11-26; the airlock was taken out for rework, Karl 2026-11-27).
/// The ammo socket sits inside the hull and feeds a gun (<see cref="Piece.Feed"/>, 0: the nearest weapon).
/// </summary>
public static class PieceDocking
{
    private const string Armour = PieceKinds.Armour, Gun = PieceKinds.Gun, Amber = PieceKinds.Amber, Glow = PieceKinds.Glow, Pale = PieceKinds.Pale, Black = PieceKinds.Black, Blue = PieceKinds.Blue;

    /// <summary>The docking clamps' ring radii, S to Capital.</summary>
    private static readonly (string Name, double R)[] Clamps = [("S", 0.8), ("M", 1.3), ("L", 2.0), ("Capital", 3.4)];

    /// <summary>The new kinds, appended to <see cref="PieceKinds.All"/>.</summary>
    public static PieceKind[] Kinds =>
    [
        new("Ammo socket", "Weapons", "skin", null, "", "An internal feed socket inside the hull: it feeds the gun it links to (or the nearest weapon).", m =>
        {
            // All of it inside (its frame's -Y): a magazine box on a bracket, its feed chute turned up to the skin.
            m.Box(-0.45, -1.1, -0.6, 0.45, -0.35, 0.6, Gun);
            m.Box(-0.5, -1.16, -0.65, 0.5, -1.1, 0.65, Armour);
            m.Box(-0.47, -0.8, -0.62, 0.47, -0.74, 0.62, Amber);
            m.Box(-0.15, -0.35, -0.15, 0.15, -0.05, 0.15, Armour);
            m.Box(0.2, -0.5, 0.62, 0.35, -0.42, 0.64, Glow);
        }),
        .. Clamps.Select(c => new PieceKind("Docking clamp " + c.Name, "Docking", "skin", null, "", $"A {(c.Name == "Capital" ? "capital ship's" : c.Name + " size")} docking clamp: a ring {2 * c.R:0.#} m across with jaws that close on another ship's collar.", m => Clamp(m, c.R))),
        new("Magnetic lock", "Docking", "skin", null, "", "A magnetic docking lock: a flat pad of coils that holds a ship or a station's arm against the skin.", m =>
        {
            m.Box(-0.9, 0, -0.9, 0.9, 0.12, 0.9, Armour);
            m.Box(-0.75, 0.12, -0.75, 0.75, 0.22, 0.75, Gun);
            for (var x = -0.6; x < 0.65; x += 0.4)
                m.Box(x - 0.12, 0.22, -0.6, x + 0.12, 0.3, 0.6, Pale);
            m.Box(-0.9, 0.12, 0.8, 0.9, 0.18, 0.9, Amber);
            m.Box(-0.9, 0.12, -0.9, 0.9, 0.18, -0.8, Amber);
            m.Box(0.6, 0.3, 0.6, 0.72, 0.36, 0.72, Glow);
        }),
        new("Docking hatch", "Boarding", "skin", "hatch", "any room", "A round docking hatch that mates with another ship's: cut through the skin, it opens into the room behind it.", m =>
        {
            m.Frustum(0, 1.0, 0.25, 0.95, 16, Armour, cap0: false, cap1: false);
            m.Frustum(0.25, 1.05, 0.32, 1.05, 16, Amber, cap0: false, cap1: false);
            m.Leaf(0, () =>
            {
                m.Frustum(-0.12, 0.72, -0.06, 0.72, 16, Gun);            // the hatch lid, set in (leaf 0: a docking hatch in the game)
                m.Box(-0.06, -0.06, -0.55, 0.06, -0.02, 0.55, Amber);
            });
            m.Frustum(-1.0, 0.76, 0.02, 0.76, 16, Gun, cap0: false, cap1: false); // its well
        }),
        new("Hull grapple", "Boarding", "skin", null, "", "A grapple launcher: fires a claw on a cable to catch a ship and pull it alongside.", m =>
        {
            m.Frustum(0, 0.8, 0.3, 0.7, 12, Armour);
            m.Box(-0.45, 0.3, -0.6, 0.45, 0.9, 0.5, Gun);
            m.Barrel(0, 0.62, 0.5, 1.8, 0.16, 10, Black);
            m.Box(-0.35, 0.4, 1.8, 0.35, 0.84, 2.0, Amber);              // the claw's hub
            m.Box(-0.5, 0.58, 2.0, -0.38, 0.66, 2.45, Gun);              // its tines
            m.Box(0.38, 0.58, 2.0, 0.5, 0.66, 2.45, Gun);
            m.Box(-0.04, 0.9, 2.0, 0.04, 1.02, 2.45, Gun);
            m.Box(-0.04, 0.26, 2.0, 0.04, 0.38, 2.45, Gun);
        }),
        new("Breaching cutter", "Boarding", "skin", null, "", "A breaching cutter: a sealed skirt pressed to another hull while its ring cuts a way through.", m =>
        {
            m.Frustum(0, 1.25, 0.35, 1.2, 16, Armour);
            m.Frustum(0.35, 1.05, 1.1, 1.0, 16, Gun, cap0: false);
            m.Frustum(1.1, 1.15, 1.3, 1.2, 16, Black, cap0: true, cap1: false); // the skirt's seal
            m.Frustum(1.28, 0.9, 1.34, 0.9, 16, Glow, cap0: false);              // the cutting ring
            m.Box(1.05, 0.2, -0.3, 1.25, 0.8, 0.3, Amber);
            m.Box(-1.25, 0.2, -0.3, -1.05, 0.8, 0.3, Amber);
        }),
    ];

    /// <summary>The new sockets' holes: the docking hatch's round one.</summary>
    public static (double Hx, double Hz, bool Round)? Opening(string? socket) => socket switch { "hatch" => (0.76, 0.76, true), _ => null };

    /// <summary>What keeps the cladding clear round the new skin pieces (they cut no hole).</summary>
    public static (double Hx, double Hz, bool Round)? Footprint(string name) => name switch
    {
        "Magnetic lock" => (0.9, 0.9, false), "Hull grapple" => (0.8, 0.8, true), "Breaching cutter" => (1.25, 1.25, true),
        _ => ClampFoot(name) ?? PieceWeapons.Footprint(name),
    };

    private static (double, double, bool)? ClampFoot(string name)
    {
        foreach (var c in Clamps)
            if ("Docking clamp " + c.Name == name)
                return (c.R + 0.35, c.R + 0.35, true);
        return null;
    }

    /// <summary>A piece's own opening or footprint: a hull light bar's from its length, the rest from its kind.</summary>
    public static (double Hx, double Hz, bool Round)? FootprintOf(Piece p, PieceKind k) =>
        p.Kind == DesignLightBars.Hull ? DesignLightBars.FootprintOf(p) : k.Footprint;

    /// <summary>A docking clamp: a ring on a base, four jaws round it, open.</summary>
    private static void Clamp(DesignMesh m, double r)
    {
        var t = Math.Max(0.12, r * 0.12);
        m.Frustum(0, r + 0.3, 0.25, r + 0.2, 20, Armour);
        m.Frustum(0.25, r, 0.25 + 2 * t, r, 20, Gun, cap0: false, cap1: false);
        m.Frustum(0.25 + 2 * t, r + 0.05, 0.3 + 2 * t, r + 0.05, 20, Amber, cap0: false, cap1: false); // a rim band, open: the ring's mouth shows
        m.Frustum(0.25 + 2 * t, r - 0.03, 0.3 + 2 * t, r - 0.03, 20, Amber, cap0: false, cap1: false);
        m.Frustum(0.26, r - 0.04, 0.3, r - 0.04, 20, Black, cap0: false, cap1: true); // the mating face inside the ring
        var h = 0.25 + 2 * t;
        foreach (var (sx, sz) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
        {
            double cx = sx * (r + 0.05), cz = sz * (r + 0.05), wx = sx == 0 ? t * 1.6 : t, wz = sz == 0 ? t * 1.6 : t;
            m.Box(cx - wx, 0.25, cz - wz, cx + wx, h + 3 * t, cz + wz, Armour);          // the jaw's post
            double ix = cx - sx * 2.2 * t, iz = cz - sz * 2.2 * t;
            m.Box(Math.Min(cx, ix) - (sx == 0 ? wx : 0), h + 2 * t, Math.Min(cz, iz) - (sz == 0 ? wz : 0),
                  Math.Max(cx, ix) + (sx == 0 ? wx : 0), h + 3 * t, Math.Max(cz, iz) + (sz == 0 ? wz : 0), Amber); // its hook over the ring
        }
    }

    /// <summary>The gun an ammo socket feeds: the one it was linked to, else the nearest weapon on the ship (null: none).</summary>
    public static Piece? FeedOf(ShipDesign d, Piece p)
    {
        if (p.Feed != 0 && d.Pieces.Find(q => q.Id == p.Feed) is { } f && PieceKinds.Find(f.Kind)?.Group == "Weapons" && !PieceWeapons.IsFeeder(f.Kind))
            return f;
        Piece? best = null;
        var bd = double.MaxValue;
        var a = At(d, p);
        foreach (var q in d.Pieces)
        {
            if (q == p || PieceWeapons.IsFeeder(q.Kind) || PieceKinds.Find(q.Kind)?.Group != "Weapons")
                continue;
            var b = At(d, q);
            var dd = (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]);
            if (dd < bd)
                (best, bd) = (q, dd);
        }
        return best;
    }

    public static double[] At(ShipDesign d, Piece p) => d.Hull(p.Hull) is { } h ? DesignScene.Where(h, p).O : [0, 0, 0];
}
