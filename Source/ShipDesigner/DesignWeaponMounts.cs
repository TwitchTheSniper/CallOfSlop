namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Weapon mounts (Karl, 2026-11-26): a weapon is a socket on the skin that a gun fits into. A mount (a hull mount plate, a turret ring or a
/// gun sponson, sized from the game's parts in Constructs/Parts/ShipWorks and Hull/GunSponson) takes one weapon (<see cref="Piece.Fit"/>)
/// of its size class or smaller, chosen per mount. Each weapon reserves an internal volume behind its mount for its breech, loader or
/// handling room, drawn as a hologram box; a room or the skin clipping it is a warning. Shell magazines and ammo hoists link to a mount as
/// the ammo socket's feed does. Old designs' PDC turrets, railguns and torpedo sponsons load as a mount with that weapon fitted.
/// </summary>
public static class PieceWeapons
{
    private const string Armour = PieceKinds.Armour, Gun = PieceKinds.Gun, Amber = PieceKinds.Amber, Glow = PieceKinds.Glow, Pale = PieceKinds.Pale, Black = PieceKinds.Black, Blue = PieceKinds.Blue;

    /// <summary>A mount: its size class (1 S, 2 M, 3 L), its half size on the skin, where a weapon sits on it and how deep its own trunk
    /// goes in (the reserved volume starts behind that).</summary>
    public sealed record Mount(string Name, int Class, double Half, double Top, double Base, bool Ring, bool Sponson);

    /// <summary>The game's sizes: hull mounts 1.5, 2.5 and 4 m square, 30 cm thick; turret rings 1, 1.6 and 2.4 m across with trunks
    /// 0.6, 1 and 1.5 m deep. The gun sponson is the game's tub (0.62 out, 0.8 long) at the ship's scale, three times over.</summary>
    public static readonly Mount[] Mounts =
    [
        new("Hull mount S", 1, 0.75, 0.07, -0.3, false, false), new("Hull mount M", 2, 1.25, 0.07, -0.3, false, false), new("Hull mount L", 3, 2.0, 0.07, -0.3, false, false),
        new("Turret ring S", 1, 0.5, 0.25, -0.6, true, false), new("Turret ring M", 2, 0.8, 0.32, -1.0, true, false), new("Turret ring L", 3, 1.2, 0.4, -1.5, true, false),
        new("Gun sponson", 2, 1.2, 0, -0.3, false, true),
    ];

    /// <summary>A weapon that fits a mount: its class, its reserved volume behind the mount (half across, depth in, from and to along
    /// the hull: + towards the bow) and how it draws (on the mount's top, in a frame with Y up out of the mount, Z towards the bow).</summary>
    public sealed record Weapon(string Name, int Class, string Desc, double Hx, double Depth, double Z0, double Z1, string Reserve, Action<DesignMesh> Draw);

    public static readonly Weapon[] Weapons =
    [
        new("PDC turret", 1, "A point defence cannon, twin barrels.", 0.6, 0.9, -0.6, 0.6, "magazine and feed", m => Old(m, "PDC turret")),
        new("Flak cannon", 1, "The game's flak cannon: twin 3 m barrels on a 1 m mount.", 0.6, 1.1, -0.7, 0.7, "loader", Flak),
        new("Rocket battery", 2, "The game's rocket battery: a box of tubes on a trunnion.", 1.0, 1.7, -1.5, 1.0, "reload room", Rockets),
        new("Naval gun 20 cm", 2, "The game's 20 cm naval gun: a 4 m turret, an 8 m barrel.", 1.1, 2.3, -1.2, 1.2, "barbette and handling room", m => Naval(m, 1.0)),
        new("Torpedo sponson", 2, "Two torpedo tubes in an armoured house, 8 m long.", 1.0, 1.3, -6.0, -1.0, "torpedo loading room", m => Old(m, "Torpedo sponson")),
        new("Railgun", 3, "A spinal railgun: its rails run 14 m towards the bow.", 0.8, 1.3, -14.0, -2.0, "capacitor bank and breech, along the hull", m => Old(m, "Railgun")),
        new("Naval gun 40 cm", 3, "The game's 40 cm naval gun, drawn at half its 16 m turret to sit on an L ring.", 1.8, 4.2, -2.0, 2.0, "barbette and shell room", m => Naval(m, 2.0)),
    ];

    /// <summary>The internal pieces that feed a mount (the ammo socket, the shell magazine and the ammo hoist).</summary>
    public static readonly string[] Feeders = ["Ammo socket", "Shell magazine", "Ammo hoist"];

    public static bool IsFeeder(string kind) => Array.IndexOf(Feeders, kind) >= 0;

    public static Mount? MountOf(string kind) => Array.Find(Mounts, m => m.Name == kind);

    public static Weapon? WeaponOf(string name) => Array.Find(Weapons, w => w.Name == name);

    /// <summary>Whether a weapon fits a mount: its class no bigger than the mount's.</summary>
    public static bool Fits(string mount, string weapon) => MountOf(mount) is { } m && WeaponOf(weapon) is { } w && w.Class <= m.Class;

    /// <summary>The old weapon pieces, kept so old designs draw, and hidden from the Pieces tab.</summary>
    public static readonly string[] Legacy = ["PDC turret", "Railgun", "Torpedo sponson"];

    /// <summary>An old weapon piece as a mount of a matching size with that weapon fitted.</summary>
    public static void Migrate(Piece p)
    {
        var to = p.Kind switch { "PDC turret" => "Turret ring S", "Railgun" => "Hull mount L", "Torpedo sponson" => "Gun sponson", _ => null };
        if (to is null)
            return;
        p.Fit = p.Kind;
        p.Kind = to;
    }

    public static void Migrate(ShipDesign d)
    {
        foreach (var p in d.Pieces)
            Migrate(p);
    }

    /// <summary>The new kinds, appended to <see cref="PieceKinds.All"/>: the mounts, the shell magazine and the ammo hoist.</summary>
    public static PieceKind[] Kinds =>
    [
        .. Mounts.Select(k => new PieceKind(k.Name, "Weapons", "skin", null, "",
            k.Sponson ? "A gun sponson: an armoured tub bulged out of the side, a turntable on top. Fits an M weapon or smaller." :
            k.Ring ? $"A turret ring {2 * k.Half:0.#} m across, its trunk {-k.Base:0.#} m in. Fits {"SML"[k.Class - 1]} weapons or smaller." :
            $"An armoured hull plate {2 * k.Half:0.#} m square that a gun, launcher or turret ring goes through. Fits {"SML"[k.Class - 1]} weapons or smaller.",
            m => DrawMount(m, k))),
        new("Shell magazine", "Weapons", "skin", null, "", "A shell magazine inside the hull (the game's large one, 4 m): it feeds the mount it links to (or the nearest).", m =>
        {
            m.Box(-1.0, -2.6, -1.0, 1.0, -0.6, 1.0, Gun);
            m.Box(-0.8, -0.6, -0.8, 0.8, -0.52, 0.8, Armour);
            m.Box(-1.02, -1.7, -1.02, 1.02, -1.62, 1.02, Amber);
            m.Box(-0.2, -0.52, -0.2, 0.2, -0.05, 0.2, Armour);
            m.Box(0.6, -1.0, 1.0, 0.85, -0.85, 1.03, Glow);
        }),
        new("Ammo hoist", "Weapons", "skin", null, "", "An ammo hoist trunk inside the hull: it carries rounds up to the mount it links to (or the nearest).", m =>
        {
            m.Box(-0.25, -3.0, -0.25, 0.25, -0.05, 0.25, Gun);
            for (var y = -2.8; y < -0.2; y += 0.6)
                m.Box(-0.28, y, -0.28, 0.28, y + 0.06, 0.28, Amber);
            m.Box(-0.4, -3.1, -0.4, 0.4, -3.0, 0.4, Armour);
        }),
    ];

    /// <summary>The cladding clearance round a mount (it cuts no hole: its plate or ring covers the skin).</summary>
    public static (double Hx, double Hz, bool Round)? Footprint(string name) => MountOf(name) is { } k ? (k.Half + 0.1, k.Half + 0.1, k.Ring) : null;

    private static void DrawMount(DesignMesh m, Mount k)
    {
        var a = k.Half;
        if (k.Sponson)
        {
            // The tub: out from the skin along Y, its turntable on top (X up).
            m.Box(-0.9, 0, -a, 0.9, 1.8, a, Armour);
            m.Box(-0.75, 1.8, -a + 0.15, 0.75, 1.9, a - 0.15, Gun);
            m.Box(-0.92, 0.2, -a - 0.02, 0.92, 0.3, a + 0.02, Amber);
            return;
        }
        if (k.Ring)
        {
            m.Frustum(0, a, k.Top, a, 20, Armour);
            m.Frustum(k.Top, a * 0.92, k.Top + 0.04, a * 0.92, 20, Amber, cap0: false);
            m.Frustum(k.Base, a * 0.62, 0, a * 0.62, 16, Gun, cap0: true, cap1: false); // the trunk through the skin
            return;
        }
        m.Box(-a, k.Base, -a, a, 0.04, a, Armour);                                           // the plate, through the skin
        m.Box(-a + 0.12, 0.04, -a + 0.12, a - 0.12, k.Top, a - 0.12, Gun);
        for (var i = 0; i < 4; i++)
        {
            double sx = i % 2 == 0 ? 1 : -1, sz = i < 2 ? 1 : -1;
            m.Box(sx * (a - 0.1) - 0.05, 0.04, sz * (a - 0.1) - 0.05, sx * (a - 0.1) + 0.05, 0.1, sz * (a - 0.1) + 0.05, Pale); // bolts
        }
        m.Box(-a, 0.04, a - 0.06, a, 0.075, a, Amber);
    }

    /// <summary>Draws a mount and its weapon. <paramref name="frame"/> sets the mesh's frame to a local origin and axes in the mount's
    /// frame (the weapon on the sponson stands on its turntable, X up).</summary>
    public static void Draw(DesignMesh m, Piece p, Mount k, Action<double[], double[], double[], double[]> frame)
    {
        DrawMount(m, k);
        if (WeaponOf(p.Fit) is not { } w)
            return;
        if (k.Sponson)
            frame([0.9, 1.0, 0], [0, -1, 0], [1, 0, 0], [0, 0, 1]);
        else
            frame([0, k.Top, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]);
        w.Draw(m);
    }

    /// <summary>
    /// The game part a fitted weapon fires as (DesignParts.Systems.cs: the weapon element starts from it and keeps its gun's numbers), or
    /// null when the game has no gun for it (the torpedo sponson: its element stays inert, logged). These matches are by look and class.
    /// </summary>
    public static string? GamePart(string weapon) => weapon switch
    {
        "PDC turret" => "Zero Day PDC", "Flak cannon" => "Flak Cannon", "Rocket battery" => "Rocket Battery", "Naval gun 20 cm" => "Naval Gun 20 cm",
        "Railgun" => "Zero Day Railgun", "Naval gun 40 cm" => "Naval Gun 40 cm",
        _ => null,
    };

    /// <summary>
    /// A mount piece drawn as its two parts in the ship's frame, at <paramref name="w"/> (one copy's place, a mirrored one's too): the mount
    /// alone and the weapon fitted on it (null: none), each exactly as <see cref="DesignScene.PieceMesh"/> draws them together, so the game
    /// splits the designer's own drawing (DesignParts.Systems.cs) and makes nothing of its own. A mirrored place draws with its winding turned
    /// back, as a mirrored mesh has it.
    /// </summary>
    public static (DesignMesh Mount, DesignMesh? Weapon) Split(Piece p, DesignScene.Place w)
    {
        var k = MountOf(p.Kind)!;
        var across = PieceKinds.Find(p.Kind)?.Footprint is not null ? p.Hole : 1;
        double[] ro = [w.O[0] - w.Y[0] * p.Recess, w.O[1] - w.Y[1] * p.Recess, w.O[2] - w.Y[2] * p.Recess];
        var mount = new DesignMesh();
        mount.Frame(ro[0], ro[1], ro[2], w.X, w.Y, w.Z, p.Size, across);
        DrawMount(mount, k);
        DesignMesh? weapon = null;
        if (WeaponOf(p.Fit) is { } wp)
        {
            weapon = new DesignMesh();
            double[] L(double[] v) => [w.X[0] * v[0] + w.Y[0] * v[1] + w.Z[0] * v[2], w.X[1] * v[0] + w.Y[1] * v[1] + w.Z[1] * v[2], w.X[2] * v[0] + w.Y[2] * v[1] + w.Z[2] * v[2]];
            var (lo, ax, ay, az) = k.Sponson ? (new double[] { 0.9, 1.0, 0 }, new double[] { 0, -1, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 0, 1 })
                : (new double[] { 0, k.Top, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 });
            var oo = L(lo);
            weapon.Frame(ro[0] + oo[0] * p.Size, ro[1] + oo[1] * p.Size, ro[2] + oo[2] * p.Size, L(ax), L(ay), L(az), p.Size);
            wp.Draw(weapon);
        }
        var det = w.X[0] * (w.Y[1] * w.Z[2] - w.Y[2] * w.Z[1]) - w.X[1] * (w.Y[0] * w.Z[2] - w.Y[2] * w.Z[0]) + w.X[2] * (w.Y[0] * w.Z[1] - w.Y[1] * w.Z[0]);
        if (det < 0)
        {
            Unwind(mount);
            if (weapon != null)
                Unwind(weapon);
        }
        return (mount, weapon);
    }

    /// <summary>Turns every triangle's winding round (its first and last corners swapped, colours with them).</summary>
    private static void Unwind(DesignMesh m)
    {
        for (var t = 0; t < m.Tris; t++)
        {
            for (var c = 0; c < 3; c++)
                (m.P[t * 9 + c], m.P[t * 9 + 6 + c]) = (m.P[t * 9 + 6 + c], m.P[t * 9 + c]);
            if (t * 9 + 8 < m.C.Count)
                for (var c = 0; c < 3; c++)
                    (m.C[t * 9 + c], m.C[t * 9 + 6 + c]) = (m.C[t * 9 + 6 + c], m.C[t * 9 + c]);
        }
    }

    private static void Old(DesignMesh m, string kind) => Array.Find(PieceKinds.All, q => q.Name == kind)?.Draw(m);

    private static void Flak(DesignMesh m)
    {
        m.Box(-0.5, 0, -0.5, 0.5, 0.85, 0.5, Gun);
        m.Box(-0.4, 0.85, -0.4, 0.4, 0.95, 0.35, Armour);
        m.Barrel(-0.17, 0.45, 0.5, 3.5, 0.06, 8, Black);
        m.Barrel(0.17, 0.45, 0.5, 3.5, 0.06, 8, Black);
        m.Box(-0.3, 0.3, 3.2, 0.3, 0.6, 3.3, Armour);
    }

    private static void Rockets(DesignMesh m)
    {
        m.Frustum(0, 0.9, 0.3, 0.8, 14, Armour);
        m.Box(-0.15, 0.3, -0.2, 0.15, 0.9, 0.2, Gun);
        m.Box(-1.0, 0.8, -1.2, 1.0, 2.0, 1.0, Gun);
        for (var x = -0.75; x < 0.8; x += 0.5)
            for (var y = 1.05; y < 1.9; y += 0.4)
                m.Box(x - 0.17, y - 0.15, 1.0, x + 0.17, y + 0.15, 1.03, Black);
        m.Box(-1.02, 1.85, -1.22, 1.02, 1.92, 1.02, Amber);
    }

    /// <summary>A naval gun's turret at scale s (1: the 20 cm, a 4 m house and an 8 m barrel).</summary>
    private static void Naval(DesignMesh m, double s)
    {
        m.Frustum(0, 1.9 * s, 0.3 * s, 1.85 * s, 20, Armour);
        m.Box(-1.6 * s, 0.3 * s, -1.8 * s, 1.6 * s, 1.5 * s, 1.0 * s, Gun);
        m.Box(-1.3 * s, 0.3 * s, 1.0 * s, 1.3 * s, 1.2 * s, 1.8 * s, Gun);       // the sloped face, in two steps
        m.Box(-1.62 * s, 1.5 * s, -1.82 * s, 1.62 * s, 1.56 * s, 1.0 * s, Armour);
        m.Barrel(0, 0.85 * s, 1.8 * s, 9.0 * s, 0.24 * s, 12, Black);
        m.Barrel(0, 0.85 * s, 1.8 * s, 3.0 * s, 0.36 * s, 12, Armour);               // the mantlet and recoil sleeve
        m.Box(1.0 * s, 1.56 * s, -1.4 * s, 1.4 * s, 1.8 * s, -0.8 * s, Pale);          // the rangefinder hood
    }

    /// <summary>A weapon's reserved volume in its mount's frame: centre and half sizes (X across, Y out, Z towards the bow).</summary>
    public static (double[] C, double[] H)? Reserve(Piece p)
    {
        if (MountOf(p.Kind) is not { } k || WeaponOf(p.Fit) is not { } w)
            return null;
        double top = k.Base - 0.02, bot = k.Base - w.Depth;
        return ([0, (top + bot) / 2, (w.Z0 + w.Z1) / 2], [w.Hx, (top - bot) / 2, (w.Z1 - w.Z0) / 2]);
    }

    /// <summary>A piece's reserved volumes in the world (one per spot: its pair and mirror twins), as a centre and three half axes.</summary>
    public static List<(double[] C, double[] A, double[] B, double[] D)> Boxes(ShipDesign d, Piece p)
    {
        var list = new List<(double[], double[], double[], double[])>();
        if (Reserve(p) is not { } r || d.Hull(p.Hull) is not { } h)
            return list;
        var w = DesignScene.Where(h, p);
        double s = p.Size;
        double[] o = [w.O[0] - w.Y[0] * p.Recess, w.O[1] - w.Y[1] * p.Recess, w.O[2] - w.Y[2] * p.Recess];
        double[] V(double[] a, double f) => [a[0] * f, a[1] * f, a[2] * f];
        double[] c = [o[0] + s * (w.X[0] * r.C[0] + w.Y[0] * r.C[1] + w.Z[0] * r.C[2]), o[1] + s * (w.X[1] * r.C[0] + w.Y[1] * r.C[1] + w.Z[1] * r.C[2]), o[2] + s * (w.X[2] * r.C[0] + w.Y[2] * r.C[1] + w.Z[2] * r.C[2])];
        var ax = V(w.X, s * r.H[0]); var ay = V(w.Y, s * r.H[1]); var az = V(w.Z, s * r.H[2]);
        list.Add((c, ax, ay, az));
        static double[] F(double[] a, double cx) => [2 * cx - a[0], a[1], a[2]];
        static double[] Fv(double[] a) => [-a[0], a[1], a[2]];
        var pair = p.Pair && h.Copies == 2;
        if (pair)
            list.Add((F(c, 0), Fv(ax), Fv(ay), Fv(az)));
        if (p.Mirror)
        {
            var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
            var mc = F(c, cx);
            list.Add((mc, Fv(ax), Fv(ay), Fv(az)));
            if (pair)
                list.Add((F(mc, 0), ax, ay, az));
        }
        return list;
    }

    /// <summary>What clips a reserved volume: the first room it runs into, or the skin (null: it is clear).</summary>
    public static string? Clipped(ShipDesign d, Piece p, (double[] C, double[] A, double[] B, double[] D) b)
    {
        var h = d.Hull(p.Hull)!;
        const int n = 4;
        var skin = false;
        for (var i = 0; i <= n; i++)
            for (var j = 0; j <= n; j++)
                for (var k = 0; k <= n; k++)
                {
                    double fa = (2.0 * i / n - 1) * 0.97, fb = (2.0 * j / n - 1) * 0.97, fc = (2.0 * k / n - 1) * 0.97;
                    double x = b.C[0] + b.A[0] * fa + b.B[0] * fb + b.D[0] * fc, y = b.C[1] + b.A[1] * fa + b.B[1] * fb + b.D[1] * fc, z = b.C[2] + b.A[2] * fa + b.B[2] * fb + b.D[2] * fc;
                    foreach (var r in d.Rooms)
                    {
                        var bx = d.Box(r);
                        if (x > bx[0] && x < bx[1] && y > bx[2] && y < bx[3] && z > bx[4] && z < bx[5])
                            return DesignScene.Name(r);
                    }
                    if (h.Depth(x, y, z) < -0.02)
                        skin = true;
                }
        return skin ? "the skin" : null;
    }

    /// <summary>The checks: a weapon's reserved volume clipped by a room or the skin, or a weapon too big for its mount.</summary>
    public static void Check(ShipDesign d, DesignScene.Check c)
    {
        foreach (var p in d.Pieces)
        {
            if (MountOf(p.Kind) is not { } k || p.Fit.Length == 0)
                continue;
            if (!Fits(p.Kind, p.Fit))
            {
                c.Issues.Add(new("error", $"The {p.Fit} is too big for the {p.Kind}: pick a bigger mount", $"piece:{p.Id}"));
                continue;
            }
            foreach (var b in Boxes(d, p))
                if (Clipped(d, p, b) is { } what)
                {
                    c.Issues.Add(new("warn", $"The {p.Fit}'s {WeaponOf(p.Fit)!.Reserve} (the hologram behind its {p.Kind}) is clipped by {what}: move the mount or the room", $"piece:{p.Id}"));
                    break;
                }
        }
    }

    /// <summary>The reserved volumes as JSON for the page: [[centre, half axis A, B, D, clipped (1) or clear (0)], ...].</summary>
    public static string Json(ShipDesign d, Piece p)
    {
        var bs = Boxes(d, p);
        if (bs.Count == 0)
            return "[]";
        return "[" + string.Join(',', bs.Select(b => "[" + string.Join(',', b.C.Concat(b.A).Concat(b.B).Concat(b.D).Select(ShipDesign.S)) + "," + (Clipped(d, p, b) is null ? 0 : 1) + "]")) + "]";
    }
}
