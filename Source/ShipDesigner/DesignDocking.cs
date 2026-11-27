namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The adjustable airlock, the ammo socket, docking and boarding pieces (Karl, 2026-11-26). The airlock is a chamber like the game's
/// (Constructs/Parts/WallBays: AirlockChamber, AirlockInnerDoor, AirlockOuterHatch): an outer hatch on the skin, a chamber in, and an
/// inner door to the room, each sized by the piece (<see cref="Piece.AirW"/> and the rest). Its defaults are the old single piece's size.
/// The ammo socket sits inside the hull and feeds a gun (<see cref="Piece.Feed"/>, 0: the nearest weapon).
/// </summary>
public static class PieceDocking
{
    private const string Armour = PieceKinds.Armour, Gun = PieceKinds.Gun, Amber = PieceKinds.Amber, Glow = PieceKinds.Glow, Pale = PieceKinds.Pale, Black = PieceKinds.Black, Blue = PieceKinds.Blue;

    /// <summary>The airlock's settings' defaults: today's single piece (a 2.3 x 1.5 m hatch, a well 1.2 m deep).</summary>
    public const double AirW = 1.5, AirH = 2.3, AirD = 1.2, DoorW = 1.5;
    public static readonly string[] Doors = ["sliding", "hinged", "iris"];

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

    /// <summary>A piece's own opening or footprint: an airlock's from its outer hatch's size, the rest from its kind.</summary>
    public static (double Hx, double Hz, bool Round)? FootprintOf(Piece p, PieceKind k) =>
        k.Socket == "airlock" ? (p.AirH / 2, Math.Min(p.DoorW, p.AirW) / 2, p.Door == "iris") : p.Kind == DesignLightBars.Hull ? DesignLightBars.FootprintOf(p) : k.Footprint;

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

    /// <summary>
    /// The airlock as a chamber (in its frame: X across the hatch, Y out of the skin, Z along): the outer hatch's frame on the skin and
    /// the hatch, shut, just in; the chamber's walls p.AirD deep, p.AirH across and p.AirW along; at its back the inner door into the
    /// room, open, p.DoorW wide; a lamp by each door. Its door type (sliding, hinged, iris) shapes both doors.
    /// </summary>
    public static void Airlock(DesignMesh m, Piece p, bool shut = false)
    {
        double hx = p.AirH / 2, hw = p.AirW / 2, dz = Math.Min(p.DoorW, p.AirW) / 2, d = p.AirD, iris = p.Door == "iris" ? 1 : 0;
        // The outer frame on the skin round the hatch.
        m.Box(-hx - 0.2, 0, -dz - 0.2, hx + 0.2, 0.22, -dz, Armour);
        m.Box(-hx - 0.2, 0, dz, hx + 0.2, 0.22, dz + 0.2, Armour);
        m.Box(-hx - 0.2, 0, -dz, -hx, 0.22, dz, Armour);
        m.Box(hx, 0, -dz, hx + 0.2, 0.22, dz, Armour);
        m.Box(hx - 0.25, 0.22, -dz - 0.15, hx - 0.1, 0.3, -dz - 0.05, Glow);
        // The hatch's throat from the skin to the chamber, then the chamber itself (its walls 6 cm thick, outside its size).
        Walls(m, hx, dz, 0.02, -0.3);
        if (hw > dz)
        {
            m.Box(-hx - 0.06, -0.36, -hw - 0.06, hx + 0.06, -0.3, -dz, Gun); // the chamber's outer wall beside the hatch
            m.Box(-hx - 0.06, -0.36, dz, hx + 0.06, -0.3, hw + 0.06, Gun);
        }
        Walls(m, hx, hw, -0.3, -d);
        m.Box(-hx, -d + 0.02, -hw, -hx + 0.03, -0.32, hw, Black); // the chamber's floor or wall plating, 3 cm in from its wall
        m.Box(hx - 0.35, -0.9 * d, -hw + 0.05, hx - 0.05, -0.4, -hw + 0.12, Pale); // a suit locker
        m.Box(-hx * 0.2, -0.7 * d, hw - 0.08, hx * 0.2, -0.6 * d, hw - 0.03, Blue); // the cycle panel
        // The outer hatch, shut, 12 cm in (leaf 0).
        Door(m, p.Door, hx, dz, -0.12, false, 0);
        // The inner door's wall at the back, round its opening (as tall as the hatch, p.DoorW wide), the door open.
        if (hw > dz)
        {
            m.Box(-hx - 0.06, -d - 0.06, -hw - 0.06, hx + 0.06, -d, -dz, Gun);
            m.Box(-hx - 0.06, -d - 0.06, dz, hx + 0.06, -d, hw + 0.06, Gun);
        }
        Door(m, p.Door, hx, dz, -d, !shut, 1); // (leaf 1; the game builds it shut: DesignParts.Doors.cs)
        m.Box(hx - 0.25, -d - 0.1, dz + 0.02, hx - 0.1, -d - 0.04, dz + 0.1 + iris * 0.05, Amber);
    }

    /// <summary>Four walls round a rectangle hx by hz from y0 down to y1.</summary>
    private static void Walls(DesignMesh m, double hx, double hz, double y0, double y1)
    {
        m.Box(-hx - 0.06, y1, -hz - 0.06, hx + 0.06, y0, -hz, Gun);
        m.Box(-hx - 0.06, y1, hz, hx + 0.06, y0, hz + 0.06, Gun);
        m.Box(-hx - 0.06, y1, -hz, -hx, y0, hz, Gun);
        m.Box(hx, y1, -hz, hx + 0.06, y0, hz, Gun);
    }

    /// <summary>The airlock's two doors (walkable designs, step 4): each its leaf number in the piece's mesh (<see cref="DesignMesh.Leaves"/>),
    /// its type, and the opening it shuts (half sizes across X and along Z, in its frame, at depth Y). Leaf 0 is the outer hatch, 1 the inner door.</summary>
    public static (int Leaf, string Type, double Hx, double Hz, double Y)[] AirlockDoors(Piece p)
    {
        double hx = p.AirH / 2, dz = Math.Min(p.DoorW, p.AirW) / 2;
        return [(0, p.Door, hx, dz, -0.12), (1, p.Door, hx, dz, -p.AirD)];
    }

    /// <summary>A door across a hx by hz opening at depth y: sliding (two leaves, or slid into the wall when open), hinged (a leaf
    /// swung in on its hinge when open), or iris (a ring of blades, drawn back to a rim when open). What moves is tagged as leaf
    /// <paramref name="leaf"/> (<see cref="DesignMesh.Leaf"/>); its track, hinges and rim stay with the piece.</summary>
    private static void Door(DesignMesh m, string type, double hx, double hz, double y, bool open, int leaf)
    {
        const double t = 0.05;
        switch (type)
        {
            case "hinged":
                m.Leaf(leaf, () =>
                {
                    if (open)
                        m.Box(-hx + 0.02, y - 0.04 - 2 * hz + 0.04, hz - 0.01, hx - 0.02, y - 0.04, hz + t - 0.01, Armour); // swung 90 degrees into the room
                    else
                    {
                        m.Box(-hx + 0.02, y - t, -hz + 0.02, hx - 0.02, y, hz - 0.02, Armour);
                        m.Box(-hx * 0.3, y + 0.003, hz - 0.2, hx * 0.3, y + 0.03, hz - 0.12, Amber); // its wheel bar
                    }
                });
                m.Box(-hx + 0.1, y - 0.09, hz - 0.04, -hx + 0.3, y + 0.01, hz + 0.02, Pale);    // the hinges
                m.Box(hx - 0.3, y - 0.09, hz - 0.04, hx - 0.1, y + 0.01, hz + 0.02, Pale);
                break;
            case "iris":
            {
                // A rim, and eight blades: shut, they meet at the middle; open, only their ends show inside the rim.
                var r = Math.Min(hx, hz);
                m.Frustum(y - 0.08, r + 0.02, y + 0.01, r + 0.02, 16, Armour, cap0: false, cap1: false);
                var reach = open ? r * 0.18 : r;
                m.Leaf(leaf, () =>
                {
                    for (var i = 0; i < 4; i++)
                    {
                        double ax = i % 2 == 0 ? 1 : 0, az = 1 - ax, s = i < 2 ? 1 : -1, yy = y - 0.03 - 0.008 * i;
                        double x0 = ax * s * (r - reach), x1 = ax * s * r, z0 = az * s * (r - reach), z1 = az * s * r;
                        var half = r * 0.72;
                        m.Box(Math.Min(x0, x1) - az * half, yy - 0.02, Math.Min(z0, z1) - ax * half, Math.Max(x0, x1) + az * half, yy, Math.Max(z0, z1) + ax * half, i % 2 == 0 ? Gun : Armour);
                    }
                });
                break;
            }
            default: // sliding
                m.Leaf(leaf, () =>
                {
                    if (open)
                        m.Box(-hx + 0.02, y - t, hz + 0.08, hx - 0.02, y, hz + 0.08 + Math.Min(2 * hz, 0.4), Armour); // slid back into the wall's pocket
                    else
                    {
                        m.Box(-hx + 0.02, y - t, -hz + 0.02, hx - 0.02, y, -0.01, Armour);
                        m.Box(-hx + 0.02, y - t, 0.01, hx - 0.02, y, hz - 0.02, Armour);
                        m.Box(-hx + 0.02, y + 0.003, -0.01, hx - 0.02, y + 0.02, 0.01, Amber);
                    }
                });
                m.Box(hx - 0.06, y - t - 0.02, -hz, hx + 0.02, y + 0.02, hz + 0.5, Pale); // the track
                break;
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
