namespace CallOfSlop.MapKit.Generators;

/// <summary>
/// Ashgrave Cathedral's architectural detail (developer-approved board item dev-cathedral-detail-cd01, 2026-10-28):
/// the walls get a bay rhythm of responds (pilasters, shafts and vault springers), plinths, string courses and cornices,
/// blind arcades, niches with saints, candles and reliquaries, wall tombs, plaques, banners and frames round the windows;
/// the high rooms get quadripartite rib vaults with bosses, the low ones timber beams or coffers; the floors get a
/// chequered band down the processional way and ledger stones. The repeated pieces are the reusable Props/Gothic props
/// (tools/props/gothic_props.py); the ribs, mouldings and beams are cosmetic meshes (Source/MapKit/Gothic.cs).
/// Everything here is looks only except the wall tombs' chests (solid, placed through the layout check): no door,
/// window, wall buy, machine, spawner or stair is covered (every piece is checked against them, and against the
/// fittings and furniture placed before), so the nav graph and the light's occluders don't change.
/// </summary>
public sealed partial class AshgraveCathedral
{
    // The vault bays: the nave's follow its arcade piers (x -27 ... -3), the others are chosen clear of the windows.
    static readonly double[] NaveBays = [-33.4, -27, -21, -15, -9, -3, 3.4];
    static readonly double[] NaveBayCentres = [-24, -18, -12, -6, 0.2];
    static readonly double[] ChoirBays = [16.0, 19.5, 24.5, 29.5, 34.5, 37.4];
    static readonly double[] TransArm = [-27.4, -20.4, -15.4];
    static readonly double[] Crossing = [-15.4, -9.13, -2.87, 3.4];
    static readonly double[] LadyBays = [-15.4, -9.13, -2.87, 3.4];

    const string Dress = "#9a9385", Dress2 = "#857f72", Rib = "#a39c8d", Beam = "#4a3420", Beam2 = "#3a2818";

    // What a piece must keep clear of: openings, flat wall things (glass, buys, hangings), fittings (torches,
    // chandeliers), furniture and machines on the floor (and stairwells), and the detail already placed.
    const int Hole = 1, Flat = 2, Fit = 4, Floor = 8, Mine = 16;
    readonly List<(double X0, double Y0, double Z0, double X1, double Y1, double Z1, int Kind, string What, JObject? Op)> _obst = new();

    /// <summary>A wall face to dress: along X (or Z) from A0 to A1 at Face, the room on the Facing side (+1 / -1), floor to ceiling.</summary>
    readonly record struct Face(bool AlongX, double At, int Facing, double A0, double A1, double Floor, double Ceil);

    void Detail()
    {
        Obstacles();
        WindowDetail();
        NaveDetail();
        TranseptDetail();
        ChoirDetail();
        LadyChapelDetail();
        SideRoomsDetail();
        UndergroundDetail();
        FloorDetail();
    }

    // ==== The obstacles ===============================================================================================

    void Obstacles()
    {
        foreach (var op in k.Ops)
        {
            switch (op.Str("op"))
            {
                case "WallX" or "WallZ" when op.Get("holes") is JArray holes:
                    var alongX = op.Str("op") == "WallX";
                    double lo = alongX ? op.Num("z") : op.Num("x"), t = op.Num("t");
                    foreach (var h in holes.Items.OfType<JObject>())
                    {
                        double c = h.Num("center"), w = h.Num("width") / 2 + 0.08, b = h.Num("bottom") - 0.05, top = h.Num("top") + 0.3;
                        if (alongX)
                            AddObst(c - w, b, lo - 0.7, c + w, top, lo + t + 0.7, Hole, "an opening");
                        else
                            AddObst(lo - 0.7, b, c - w, lo + t + 0.7, top, c + w, Hole, "an opening");
                    }
                    break;
                case "Prop":
                    var name = op.Str("name") ?? "";
                    double x = op.Num("x"), z = op.Num("z"), y = op.Num("y", 0), yaw = op.Num("yaw", 0);
                    var (hx, hgt, hz, kind) = name switch
                    {
                        "Wall Torch" => (0.2, 0.8, 0.55, Fit),
                        "Hanging Lantern" => (0.4, 1.6, 0.4, Fit),
                        "Iron Chandelier" => (1.1, 3.6, 1.1, Fit),
                        "Hanging Tapestry" => (0.95, 3.1, 1.1, Flat),
                        "Lancet Window Blue" or "Lancet Window Red" => (0.8, 4.3, 0.35, Flat),
                        "Rose Window" => (2.6, 5.0, 0.6, Flat),
                        "Stone Gargoyle" => (0.5, 1.0, 0.8, Fit),
                        "Organ Pipe Wall" => (3.7, 5.5, 0.7, Floor),
                        "Choir Stalls" => (2.2, 3.2, 0.7, Floor),
                        "Bookshelf" => (0.5, 2.2, 0.3, Floor),
                        _ => (0.7, 2.8, 0.7, Floor),
                    };
                    if (Math.Abs(Math.Sin(yaw * Math.PI / 180)) > 0.5)
                        (hx, hz) = (hz, hx);
                    AddObst(x - hx, y, z - hz, x + hx, y + hgt, z + hz, kind, name, name is "Wall Torch" ? op : null);
                    break;
            }
        }
        foreach (var wb in _wallBuyList)
            AddObst(wb.X - 0.9, wb.Y + 0.3, wb.Z - 0.9, wb.X + 0.9, wb.Y + 2.3, wb.Z + 0.9, Flat, $"wall buy {wb.Build}");
        foreach (var m in _machines)
            AddObst(m.X0 - 0.1, m.Y, m.Z0 - 0.1, m.X1 + 0.1, m.Y + 2.8, m.Z1 + 0.1, Floor, m.What);
        foreach (var s in _solids)
            AddObst(s.X0, s.Y, s.Z0, s.X1, s.Y + 2.8, s.Z1, Floor, s.What);
        // The stairwells (a floor piece there would float) and the loft stair's alcove.
        AddObst(20.7, Crypt, 1.0, 28.6, G + 2.9, 3.4, Floor, "the crypt stair");
        AddObst(8.6, Dun, -3.2, 11.4, Crypt + 0.5, 5.6, Floor, "the dungeon stair");
        AddObst(41.0, Tun, 23.0, 43.5, G + 0.5, 32.8, Floor, "the tunnel stair");
        AddObst(-20.8, G, 0.9, -11.2, Gal + 0.5, 3.4, Floor, "the loft stair");
        // The bell tower's door and the nave's spawns (keep the start area as it was).
        foreach (var q in _keep)
            AddObst(q.X - q.R, q.Y, q.Z - q.R, q.X + q.R, q.Y + 0.05, q.Z + q.R, Floor, q.What);
    }

    void AddObst(double x0, double y0, double z0, double x1, double y1, double z1, int kind, string what, JObject? op = null) =>
        _obst.Add((Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1), kind, what, op));

    bool Free(double x0, double y0, double z0, double x1, double y1, double z1, int mask)
    {
        foreach (var o in _obst)
            if ((o.Kind & mask) != 0 && x0 < o.X1 && x1 > o.X0 && y0 < o.Y1 && y1 > o.Y0 && z0 < o.Z1 && z1 > o.Z0)
                return false;
        return true;
    }

    /// <summary>The box a wall piece takes: <paramref name="hw"/> either side of along-wall position a, y0..y1, d out of the face.</summary>
    static (double X0, double Y0, double Z0, double X1, double Y1, double Z1) OnWall(Face f, double a, double hw, double y0, double y1, double d) =>
        f.AlongX ? (a - hw, y0, Math.Min(f.At, f.At + f.Facing * d), a + hw, y1, Math.Max(f.At, f.At + f.Facing * d))
                 : (Math.Min(f.At, f.At + f.Facing * d), y0, a - hw, Math.Max(f.At, f.At + f.Facing * d), y1, a + hw);

    bool FreeOn(Face f, double a, double hw, double y0, double y1, double d, int mask = Hole | Flat | Floor | Mine)
    {
        var b = OnWall(f, a, hw, y0, y1, d);
        return Free(b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1, mask);
    }

    void Claim(Face f, double a, double hw, double y0, double y1, double d, string what)
    {
        var b = OnWall(f, a, hw, y0, y1, d);
        AddObst(b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1, Mine, what);
    }

    static double FaceYaw(Face f) => f.AlongX ? (f.Facing > 0 ? 0 : 180) : (f.Facing > 0 ? 90 : -90);

    (double X, double Z) At(Face f, double a, double off = 0) => f.AlongX ? (a, f.At + f.Facing * off) : (f.At + f.Facing * off, a);

    /// <summary>A wall prop on face f at along-wall a, height y (above the floor level y is absolute), if its box is free; true when placed.</summary>
    bool WallPiece(Face f, string name, double a, double y, double hw, double h, double d, int mask = Hole | Flat | Floor | Mine, double scale = 1, double off = 0)
    {
        if (!FreeOn(f, a, hw, y, y + h, d + off, mask))
            return false;
        var (x, z) = At(f, a, off);
        if (Math.Abs(scale - 1) > 1e-9)
            k.Prop(name, x, z, y, FaceYaw(f), more: ("scale", Kit.R(scale)));
        else
            k.Prop(name, x, z, y, FaceYaw(f));
        Claim(f, a, hw, y, y + h, d + off, name);
        return true;
    }

    // ==== Responds: the shafts that carry the vault ribs down the walls ================================================

    /// <summary>
    /// A respond on face f at a, its springer's top at <paramref name="spring"/>: a Gothic Respond or Gothic Tall Respond
    /// prefab when one fits exactly, otherwise a pilaster and shafts (from <paramref name="from"/>, or from the first free
    /// height when something stands against the wall below). A torch on the spot moves onto the respond's face.
    /// </summary>
    void Respond(Face f, double a, double spring, double s = 1.0, double? from = null)
    {
        const double d = 0.48;
        double hw = 0.4 * s, y0 = from ?? f.Floor;
        // Torches where the respond goes hang on its face instead (their light moves a few centimetres).
        foreach (var i in Enumerable.Range(0, _obst.Count).Where(i => _obst[i].Op != null))
        {
            var o = _obst[i];
            var b = OnWall(f, a, hw + 0.45, y0, spring, 0.6);
            if (!(b.X0 < o.X1 && b.X1 > o.X0 && b.Y0 < o.Y1 && b.Y1 > o.Y0 && b.Z0 < o.Z1 && b.Z1 > o.Z0))
                continue;
            var (tx, tz) = At(f, a, d * s - 0.04);
            o.Op!.Set("x", Kit.R(tx));
            o.Op.Set("z", Kit.R(tz));
            _obst[i] = (tx - 0.2, o.Y0, tz - 0.2, tx + 0.2, o.Y1, tz + 0.2, Fit, o.What, o.Op);
        }
        var top = spring - 1.2 * s;
        if (from == null && FreeOn(f, a, hw, y0, spring, d * s))
        {
            if (Math.Abs(spring - (y0 + 5.2 * s)) < 0.01 && WallPiece(f, "Gothic Respond", a, y0, hw, 5.2 * s, d * s, scale: s))
                return;
            if (Math.Abs(spring - (y0 + 9.2 * s)) < 0.01 && WallPiece(f, "Gothic Tall Respond", a, y0, hw, 9.2 * s, d * s, scale: s))
                return;
        }
        // Piece by piece: the springer, shafts down from it, the pilaster at the floor if the floor is free.
        var pieces = new List<(string Name, double Y, double H)> { ("Gothic Vault Springer", top, 1.2 * s) };
        var ground = from == null && FreeOn(f, a, hw, y0, y0 + 4 * s, d * s);
        var bottom = ground ? y0 + 4 * s : y0;
        if (!ground && from == null)
        {
            // Start above whatever stands against the wall here.
            var y = y0;
            while (y < top && !FreeOn(f, a, hw, y, top, d * s))
                y += 0.25;
            bottom = y;
        }
        var sy = top - 2 * s;
        for (; sy >= bottom - 0.01; sy -= 2 * s)
            pieces.Add(("Gothic Wall Shaft", sy, 2 * s));
        if (ground)
        {
            if (sy + 2 * s > bottom + 0.01)
                pieces.Add(("Gothic Wall Shaft", bottom, 2 * s)); // (overlaps the one above it: the stack reaches the pilaster)
            pieces.Add(("Gothic Pilaster", y0, 4 * s));
        }
        foreach (var (name, py, ph) in pieces)
            WallPiece(f, name, a, py, hw, ph, d * s, scale: s);
    }

    /// <summary>The mouldings of a wall face: each band at its height, with gaps where an opening or a flat wall thing is.</summary>
    void Bands(Face f, params (double Y, double H, double D, double Cap)[] bands)
    {
        foreach (var (y, h, d, cap) in bands)
        {
            var gaps = new List<(double A0, double A1)>();
            foreach (var o in _obst)
            {
                if ((o.Kind & (Hole | Flat | Floor)) == 0 || (o.Kind == Floor && y > f.Floor + 0.6))
                    continue;
                var b = OnWall(f, 0, 0, y - 0.02, y + h + cap + 0.02, d + 0.1);
                bool hit = f.AlongX ? o.Z0 < b.Z1 && o.Z1 > b.Z0 : o.X0 < b.X1 && o.X1 > b.X0;
                if (!hit || o.Y0 >= b.Y1 || o.Y1 <= b.Y0)
                    continue;
                gaps.Add(f.AlongX ? (o.X0, o.X1) : (o.Z0, o.Z1));
            }
            Gothic.Moulding(k, f.AlongX, f.At, f.Facing, f.A0, f.A1, y, h, d, Dress, gaps, cap, Dress2);
        }
    }

    /// <summary>
    /// The low zone of a bay (floor to <paramref name="top"/>) between responds at a0 and a1: the first of the given pieces
    /// that fits at the bay's centre (nudged up to a metre either way), a pair of blind arcades when the bay is wide.
    /// </summary>
    void BayFill(Face f, double a0, double a1, double top, params string[] pick)
    {
        var c = (a0 + a1) / 2;
        var room = a1 - a0 - 1.0;
        foreach (var p in pick)
        {
            var (hw, h, d, y, n) = p switch
            {
                "arcade" => (1.55, 2.8, 0.22, 0.0, 1),
                "arcade2" => (1.55, 2.8, 0.22, 0.0, 2),
                "niche" or "saint" or "candles" or "reliquary" => (0.6, 3.3, 0.42, 0.0, 1),
                "tomb" => (1.35, 4.1, 0.95, 0.0, 1),
                "plaque" => (0.5, 1.45, 0.12, 1.5, 1),
                "panelling" => (1.52, 2.3, 0.14, 0.0, 1),
                "panelling2" => (1.52, 2.3, 0.14, 0.0, 2),
                _ => throw new MapKitException($"Detail: unknown bay piece {p}"),
            };
            if (2 * hw * n > room || y + h > top - f.Floor)
                continue;
            var name = p switch
            {
                "arcade" or "arcade2" => "Gothic Blind Arcade",
                "niche" => "Gothic Wall Niche",
                "saint" => "Gothic Niche Saint",
                "candles" => "Gothic Niche Candles",
                "reliquary" => "Gothic Niche Reliquary",
                "tomb" => "Gothic Wall Tomb",
                "plaque" => "Memorial Plaque",
                _ => "Gothic Panelling",
            };
            for (var step = 0; step <= 2 * (int)((a1 - a0) / 0.25); step++)
            {
                var nudge = (step % 2 == 0 ? 1 : -1) * 0.25 * ((step + 1) / 2);
                var cc = c + nudge;
                var spots = n == 1 ? new[] { cc } : new[] { cc - hw, cc + hw };
                var mask = p == "tomb" ? Hole | Flat | Floor | Mine : Hole | Flat | Floor | Mine | Fit; // (a torch where a tomb goes moves aside)
                if (spots.Any(s => s - hw < a0 + 0.45 || s + hw > a1 - 0.45 || !FreeOn(f, s, hw, f.Floor + y, f.Floor + y + h, d, mask)))
                    continue;
                if (p == "tomb" && !TombFits(f, cc))
                    continue;
                if (p == "tomb")
                    MoveTorches(f, cc, hw, f.Floor + y, f.Floor + y + h);
                foreach (var s in spots)
                    WallPiece(f, name, s, f.Floor + y, hw, h, d, Hole | Flat | Floor | Mine | Fit);
                if (p == "tomb")
                {
                    var (x, z) = At(f, cc, 0.45);
                    var (hx, hz) = f.AlongX ? (1.15, 0.45) : (0.45, 1.15);
                    Solid(x - hx, z - hz, x + hx, z + hz, f.Floor, "wall tomb");
                }
                return;
            }
        }
    }

    /// <summary>Torches on face f within hw of a move just past that span's nearer end (their light moves with them).</summary>
    void MoveTorches(Face f, double a, double hw, double y0, double y1)
    {
        for (var i = 0; i < _obst.Count; i++)
        {
            var o = _obst[i];
            if (o.Op == null)
                continue;
            var b = OnWall(f, a, hw, y0, y1, 0.8);
            if (!(b.X0 < o.X1 && b.X1 > o.X0 && b.Y0 < o.Y1 && b.Y1 > o.Y0 && b.Z0 < o.Z1 && b.Z1 > o.Z0))
                continue;
            var along = f.AlongX ? o.Op.Num("x") : o.Op.Num("z");
            var to = along < a ? a - hw - 0.35 : a + hw + 0.35;
            var (tx, tz) = f.AlongX ? (to, o.Op.Num("z")) : (o.Op.Num("x"), to);
            o.Op.Set("x", Kit.R(tx));
            o.Op.Set("z", Kit.R(tz));
            _obst[i] = (tx - 0.2, o.Y0, tz - 0.2, tx + 0.2, o.Y1, tz + 0.2, Fit, o.What, o.Op);
        }
    }

    /// <summary>A wall tomb's chest stands 0.9 m out from the wall: clear of every keep-clear spot and a 1.6 m way past it.</summary>
    bool TombFits(Face f, double a)
    {
        var (x, z) = At(f, a, 0.45);
        if (NearKeep(x, f.Floor, z, 1.6))
            return false;
        var b = OnWall(f, a, 1.6, f.Floor, f.Floor + 2, 2.6);
        return Free(b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1, Floor);
    }

    /// <summary>Responds at every bay line inside the face, and the bays' low zones filled from <paramref name="pick"/>.</summary>
    void Bays(Face f, double[] lines, double spring, double s, double fillTop, params string[] pick)
    {
        foreach (var a in lines)
            if (a > f.A0 + 0.3 && a < f.A1 - 0.3)
                Respond(f, a, spring, s);
        var all = new List<double> { f.A0 };
        all.AddRange(lines.Where(a => a > f.A0 + 0.3 && a < f.A1 - 0.3));
        all.Add(f.A1);
        for (var i = 0; i + 1 < all.Count; i++)
            BayFill(f, all[i] + 0.2, all[i + 1] - 0.2, fillTop, pick);
    }

    /// <summary>A banner on face f at a with its rod at <paramref name="rod"/>, if the wall's free there.</summary>
    void Banner(Face f, double a, double rod, string colour) =>
        WallPiece(f, "Gothic Banner " + colour, a, rod - 3.2, 0.7, 3.4, 0.4, Hole | Flat | Mine | Fit);

    /// <summary>Base and capital collars round a square pier (centre x, z; half width hw) from y0 to y1.</summary>
    void PierCollars(double x, double z, double hw, double y0, double y1, double s = 1.0)
    {
        k.MeshAt("box", x, y0 + 0.2 * s, z, Dress2, width: 2 * hw + 0.28 * s, height: 0.4 * s, depth: 2 * hw + 0.28 * s, material: "Concrete");
        k.MeshAt("box", x, y0 + 0.47 * s, z, Dress, width: 2 * hw + 0.16 * s, height: 0.14 * s, depth: 2 * hw + 0.16 * s, material: "Concrete");
        k.MeshAt("box", x, y1 - 0.42 * s, z, Dress2, width: 2 * hw + 0.1 * s, height: 0.1 * s, depth: 2 * hw + 0.1 * s, material: "Concrete");
        k.MeshAt("box", x, y1 - 0.24 * s, z, Dress, width: 2 * hw + 0.24 * s, height: 0.26 * s, depth: 2 * hw + 0.24 * s, material: "Concrete");
        k.MeshAt("box", x, y1 - 0.06 * s, z, Dress2, width: 2 * hw + 0.36 * s, height: 0.12 * s, depth: 2 * hw + 0.36 * s, material: "Concrete");
    }

    // ==== The nave ======================================================================================================

    void NaveDetail()
    {
        // The arcade piers: base and capital collars on the ground piers and the gallery piers.
        foreach (var x in new[] { -27.0, -21.0, -15.0, -9.0, -3.0, 2.4 })
            foreach (var z in new[] { -10.5, -1.5 })
            {
                PierCollars(x, z, 0.45, G, Gal - 0.5);
                PierCollars(x, z, 0.4, Gal, 10.4, 0.8);
            }
        // The high vault: shafts up the piers' nave faces from the galleries to springers at 15.2, ribs meeting at 17.85
        // under the roof, bosses at the bays' centres (the west bay has no rib across its end: the rose window).
        var naveN = new Face(true, -10.2, 1, -33.4, 3.4, Gal, 18);
        var naveS = new Face(true, -1.8, -1, -33.4, 3.4, Gal, 18);
        foreach (var x in NaveBays[1..^1])
        {
            foreach (var f in new[] { naveN, naveS })
            {
                var g = f with { At = f.At + f.Facing * 0.1 };
                for (var y = Gal; y < 14 - 0.01; y += 2)
                    WallPiece(g, "Gothic Wall Shaft", x, y, 0.4, 2, 0.48, Mine);
                WallPiece(g, "Gothic Vault Springer", x, 14.0, 0.4, 1.2, 0.48, Mine);
            }
        }
        VaultRow(NaveBays, -10.2, -1.8, 15.0, 17.85, true, firstEnd: false);
        // The clerestory: a string course at its foot (over the galleries' openings) and a cornice under the roof.
        foreach (var f in new[] { naveN with { Floor = 10.4 }, naveS with { Floor = 10.4 } })
            Bands(f, (10.4, 0.24, 0.16, 0.1), (17.45, 0.3, 0.2, 0.12));

        // The aisles: low rib vaults (responds on the outer walls, springing at 3.64), plinths, a string course under
        // the gallery floor, frames round the windows and niches, arcades and plaques between.
        var north = new Face(true, -15.4, 1, -33.4, 3.4, G, 5.5);
        var south = new Face(true, 3.4, -1, -33.4, 3.4, G, 5.5);
        var alcove = new Face(true, 1.0, -1, -20.6, -11.4, G, 5.5);
        Bands(north, (0, 0.36, 0.1, 0.06), (4.85, 0.18, 0.14, 0.08));
        Bands(south, (0, 0.36, 0.1, 0.06), (4.85, 0.18, 0.14, 0.08));
        Bands(alcove, (0, 0.36, 0.1, 0.06));
        Bays(north, NaveBays[1..^1], 3.64, 0.7, 3.6, "saint", "arcade", "plaque");
        Bays(south, [-27, -9, -3], 3.64, 0.7, 3.6, "tomb", "reliquary", "arcade", "plaque");
        VaultRow(NaveBays, -15.4, -10.95, 3.44, 5.3, true, ridge: false);
        VaultRow([-33.4, -27, -21], -1.05, 3.4, 3.44, 5.3, true, ridge: false); // (not over the loft stair's alcove)
        VaultRow([-9, -3, 3.4], -1.05, 3.4, 3.44, 5.3, true, ridge: false);

        // The west front: a plinth, a string course over the west doors, banners either side of the doors.
        var west = new Face(false, -33.4, 1, -15.4, 3.4, G, 18);
        Bands(west, (0, 0.36, 0.1, 0.06), (4.7, 0.24, 0.16, 0.1));
        // The east wall over the rood screen: a blind arcade gallery and a saint in a niche, a plinth.
        var east = new Face(false, 3.4, -1, -10.2, -1.8, G, 18);
        Bands(east with { A0 = -15.4, A1 = 3.4 }, (0, 0.36, 0.1, 0.06), (4.5, 0.24, 0.16, 0.1));
        foreach (var z in new[] { -7.6, -4.4 })
            WallPiece(east, "Gothic Blind Arcade", z, 10.9, 1.55, 2.8, 0.22);
        WallPiece(east, "Gothic Niche Saint", -6.0, 13.8, 0.6, 3.3, 0.42);
        foreach (var z in new[] { -9.4, -2.6 })
            Banner(east, z, 9.6, z < -6 ? "Crimson" : "Azure");
    }

    // ==== The transept and crossing ======================================================================================

    void TranseptDetail()
    {
        var w = new Face(false, 4.6, 1, -27.4, -15.4, G, 18);
        var e = new Face(false, 15.4, -1, -27.4, -15.4, G, 18);
        var n = new Face(true, -27.4, 1, 4.6, 15.4, G, 18);
        var cw = new Face(false, 4.0, 1, -15.4, 3.4, G, 18);
        var ce = new Face(false, 15.4, -1, -15.4, 3.4, G, 18);
        var s = new Face(true, 3.4, -1, 4.0, 15.4, G, 18);
        foreach (var f in new[] { w, e, n, cw, ce, s })
            Bands(f, (0, 0.4, 0.1, 0.06), (4.5, 0.26, 0.16, 0.1), (10.6, 0.22, 0.14, 0.1), (17.3, 0.34, 0.22, 0.12));
        Bays(w, TransArm, 9.2, 1, 4.4, "tomb", "candles", "plaque");
        Bays(e, TransArm, 9.2, 1, 4.4, "tomb", "saint", "plaque");
        Bays(cw, Crossing, 9.2, 1, 4.4, "tomb", "arcade", "plaque");
        Bays(ce, Crossing, 9.2, 1, 4.4, "tomb", "arcade", "plaque");
        Bays(n, [], 9.2, 1, 4.4, "niche");
        Bays(s, [], 9.2, 1, 4.4, "arcade2", "arcade");
        VaultRow(TransArm, 4.6, 15.4, 9.0, 17.8, false, firstEnd: false, lastEnd: false); // (no rib across the rose window)
        VaultRow(Crossing, 4.0, 15.4, 9.0, 17.8, false);
        // The upper walls: blind arcades and banners between the string courses.
        foreach (var (f, lines) in new[] { (cw, Crossing), (ce, Crossing) })
            for (var i = 0; i + 1 < lines.Length; i++)
            {
                var c = (lines[i] + lines[i + 1]) / 2;
                if (!WallPiece(f, "Gothic Blind Arcade", c, 6.6, 1.55, 2.8, 0.22))
                    Banner(f, c, 10.2, i % 2 == 0 ? "Crimson" : "Verdant");
            }
        foreach (var f in new[] { w, e })
            Banner(f, -18.0, 10.2, "Azure");
        k.Prop("Corona Chandelier", 10.0, -24.0, 10.0);
        k.Prop("Corona Chandelier", 9.7, 0.27, 9.5);
    }

    // ==== The choir =====================================================================================================

    void ChoirDetail()
    {
        var n = new Face(true, -15.4, 1, 16.0, 37.4, G, 16);
        var s = new Face(true, 3.4, -1, 16.0, 37.4, G, 16);
        var e = new Face(false, 37.4, -1, -15.4, 3.4, G, 16);
        var w = new Face(false, 16.0, 1, -15.4, 3.4, G, 16);
        foreach (var f in new[] { n, s, e, w })
            Bands(f, (0, 0.4, 0.1, 0.06), (4.7, 0.26, 0.16, 0.1), (11.2, 0.22, 0.14, 0.1), (15.35, 0.34, 0.22, 0.12));
        Bays(n, ChoirBays[1..^1], 9.2, 1, 4.6, "tomb", "candles", "plaque");
        Bays(s, ChoirBays[1..^1], 9.2, 1, 4.6, "panelling2", "panelling", "saint", "plaque");
        Bays(e, [-9.13, -2.87], 9.2, 1, 4.6, "reliquary", "plaque");
        Bays(w, [-9.13, -2.87], 9.2, 1, 4.6, "plaque");
        VaultRow(ChoirBays, -15.4, 3.4, 9.0, 15.85, true);
        // The upper south wall (no windows): blind arcades and banners; the north's lancets get their surrounds.
        for (var i = 0; i + 1 < ChoirBays.Length; i++)
        {
            var c = (ChoirBays[i] + ChoirBays[i + 1]) / 2;
            if (!WallPiece(s, "Gothic Blind Arcade", c, 6.4, 1.55, 2.8, 0.22))
                Banner(s, c, 10.6, "Crimson");
        }
        foreach (var c in new[] { 17.75, 37.0 - 1.45 })
            Banner(n, c, 10.6, "Azure");
        k.Prop("Corona Chandelier", 27.0, -6.0, 9.6);
    }

    // ==== The Lady Chapel ================================================================================================

    void LadyChapelDetail()
    {
        var n = new Face(true, -15.4, 1, 38.0, 49.4, G, 12);
        var s = new Face(true, 3.4, -1, 38.0, 49.4, G, 12);
        var e = new Face(false, 49.4, -1, -15.4, 3.4, G, 12);
        var w = new Face(false, 38.0, 1, -15.4, 3.4, G, 12);
        foreach (var f in new[] { n, s, e, w })
            Bands(f, (0, 0.36, 0.1, 0.06), (4.1, 0.22, 0.14, 0.1), (11.4, 0.3, 0.2, 0.1));
        Bays(e, LadyBays[1..^1], 7.36, 0.8, 4.0, "candles", "plaque");
        Bays(w, LadyBays[1..^1], 7.36, 0.8, 4.0, "saint", "plaque");
        Bays(n, [], 7.36, 0.8, 4.0, "tomb", "niche", "plaque");
        Bays(s, [], 7.36, 0.8, 4.0, "tomb", "reliquary", "plaque");
        VaultRow(LadyBays, 38.0, 49.4, 7.2, 11.85, false);
        foreach (var (f, a, c) in new[] { (n, 40.5, "Azure"), (n, 47.5, "Azure"), (s, 40.5, "Verdant"), (s, 47.0, "Verdant") })
            Banner(f, a, 8.2, c);
        k.Prop("Corona Chandelier", 43.7, -6.0, 8.0);
    }

    // ==== The vestry and the chapter house ==============================================================================

    void SideRoomsDetail()
    {
        // The vestry: oak panelling, a plinth, a timber ceiling.
        var vn = new Face(true, 4.0, 1, 16.0, 37.6, G, 5);
        var vs = new Face(true, 13.6, -1, 16.0, 37.6, G, 5);
        var vw = new Face(false, 16.0, 1, 4.0, 13.6, G, 5);
        var ve = new Face(false, 37.6, -1, 4.0, 13.6, G, 5);
        foreach (var f in new[] { vn, vs, vw, ve })
        {
            Bands(f, (0, 0.3, 0.08, 0.04), (4.55, 0.2, 0.18, 0.08));
            for (var a = f.A0 + 1.7; a < f.A1 - 1.5; a += 3.1)
                WallPiece(f, "Gothic Panelling", a, G, 1.52, 2.3, 0.14, Hole | Flat | Floor | Mine | Fit);
        }
        Gothic.Beams(k, 16.0, 4.0, 37.6, 13.6, 5.0, 2.4, false, Beam, 0.32, coffered: true, cross: Beam2);
        // The chapter house: a coffered ceiling, niches with saints, plaques, a string course.
        var cn = new Face(true, 4.0, 1, 4.0, 15.6, G, 8);
        var cs = new Face(true, 21.6, -1, 4.0, 15.6, G, 8);
        var cw = new Face(false, 4.0, 1, 4.0, 21.6, G, 8);
        var ce = new Face(false, 15.6, -1, 4.0, 21.6, G, 8);
        foreach (var f in new[] { cn, cs, cw, ce })
            Bands(f, (0, 0.32, 0.1, 0.05), (3.6, 0.2, 0.14, 0.08), (7.55, 0.28, 0.18, 0.1));
        foreach (var (f, lines) in new[] { (cn, new[] { 7.9, 11.7 }), (cw, new[] { 9.9, 15.8 }), (ce, new[] { 9.9, 15.8 }) })
        {
            var all = new List<double> { f.A0 };
            all.AddRange(lines);
            all.Add(f.A1);
            for (var i = 0; i + 1 < all.Count; i++)
                BayFill(f, all[i] + 0.2, all[i + 1] - 0.2, 3.5, "saint", "plaque");
        }
        Gothic.Beams(k, 4.0, 4.0, 15.6, 21.6, 8.0, 2.2, true, Beam, 0.34, coffered: true, cross: Beam2);
    }

    // ==== The crypt, the ossuary and the dungeons ========================================================================

    void UndergroundDetail()
    {
        const double ceil = -0.6;
        // The crypt: collars on its pillars, a rib vault over every bay between them, responds on the walls at the pillar
        // lines, niches with candles and reliquaries, wall tombs.
        double[] xs = [16.4, 21.0, 26.5, 32.0, 37.4], zs = [-15.4, -10.0, -3.5, 0.9];
        foreach (var x in xs[1..^1])
            foreach (var z in zs[1..^1])
                PierCollars(x, z, 0.4, Crypt, ceil, 0.7);
        for (var j = 0; j + 1 < zs.Length; j++)
            VaultRow(xs, zs[j], zs[j + 1], Crypt + 2.55, ceil - 0.08, true, ridge: false);
        var n = new Face(true, -15.4, 1, 16.4, 37.4, Crypt, ceil);
        var s = new Face(true, 0.9, -1, 16.4, 29.8, Crypt, ceil);
        var w = new Face(false, 16.4, 1, -15.4, 0.9, Crypt, ceil);
        var e = new Face(false, 37.4, -1, -15.4, 3.4, Crypt, ceil);
        foreach (var f in new[] { n, s, w, e })
            Bands(f, (Crypt, 0.32, 0.1, 0.05), (ceil - 0.42, 0.2, 0.14, 0.08));
        Bays(n, xs[1..^1], Crypt + 2.6, 0.5, Crypt + 3.4, "tomb", "candles", "plaque");
        Bays(s, xs[1..^1], Crypt + 2.6, 0.5, Crypt + 3.4, "reliquary", "plaque");
        Bays(w, zs[1..^1], Crypt + 2.6, 0.5, Crypt + 3.4, "candles", "plaque");
        Bays(e, zs[1..^1], Crypt + 2.6, 0.5, Crypt + 3.4, "candles", "plaque");

        // The ossuary: ribs across its width, a plinth.
        double[] oz = [-15.4, -9.13, -2.87, 3.4];
        VaultRow(oz, 4.0, 15.6, Crypt + 2.6, ceil - 0.08, false, ridge: false);
        foreach (var f in new[] { new Face(false, 4.0, 1, -15.4, 3.4, Crypt, ceil), new Face(false, 15.6, -1, -15.4, 3.4, Crypt, ceil),
                     new Face(true, 3.4, -1, 4.0, 15.6, Crypt, ceil) })
            Bands(f, (Crypt, 0.32, 0.1, 0.05));

        // The dungeons: heavy transverse arches across the corridor at the cell walls, beams in the inquisitor's chamber
        // and the guard room.
        foreach (var x in new[] { -13.8, -9.0, -4.0, 1.0, 6.0 })
            Gothic.Rib(k, x, 14.0, x, 18.0, Dun + 2.2, -6.5, UnderStone, 0.36, 0.42, 3);
        Gothic.Beams(k, -26.0, 6.0, -14.4, 26.0, -6.4, 2.5, true, Beam2, 0.3);
        Gothic.Beams(k, 6.0, 6.0, 16.0, 14.0, -6.4, 2.5, false, Beam2, 0.3);
    }

    const string UnderStone = "#4c4842";

    /// <summary>A row of rib vault bays between the lines (along X with <paramref name="alongX"/>, from c0 to c1 across), sharing their transverse ribs.</summary>
    void VaultRow(double[] lines, double c0, double c1, double spring, double apex, bool alongX, bool firstEnd = true, bool lastEnd = true, bool ridge = true)
    {
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            bool first = i > 0 || firstEnd, last = i + 2 == lines.Length && lastEnd;
            if (alongX)
                Gothic.RibVault(k, lines[i], c0, lines[i + 1], c1, spring, apex, true, Rib, first, last, ridge);
            else
                Gothic.RibVault(k, c0, lines[i], c1, lines[i + 1], spring, apex, false, Rib, first, last, ridge);
        }
    }

    // ==== Windows: surrounds round the lancets, frames round the boarded windows ==========================================

    void WindowDetail()
    {
        foreach (var op in k.Ops.ToList())
        {
            if (op.Str("op") != "Prop" || op.Str("name") is not { } name || !name.StartsWith("Lancet Window"))
                continue;
            double x = op.Num("x"), z = op.Num("z"), y = op.Num("y", 0), yaw = op.Num("yaw", 0);
            k.Prop("Gothic Window Surround", x, z, y, yaw);
            var acrossX = Math.Abs(Math.Sin(yaw * Math.PI / 180)) > 0.5;
            AddObst(x - (acrossX ? 0.4 : 1.2), y - 0.2, z - (acrossX ? 1.2 : 0.4), x + (acrossX ? 0.4 : 1.2), y + 5.0, z + (acrossX ? 1.2 : 0.4), Mine, "a window surround");
        }
        // The boarded windows inside the cathedral (a frame on the room's side; the opening itself stays clear).
        foreach (var w in _windows.Items.OfType<JObject>())
        {
            var area = w.Str("area");
            if (area is not ("Nave" or "Transept" or "Choir" or "Lady Chapel"))
                continue;
            var box = (JArray)w.Get("box")!;
            double x0 = ((JNumber)box.Items[0]).Value, z0 = ((JNumber)box.Items[2]).Value, x1 = ((JNumber)box.Items[3]).Value, z1 = ((JNumber)box.Items[5]).Value;
            var inside = (JArray)w.Get("inside")!;
            double ix = ((JNumber)inside.Items[0]).Value, iz = ((JNumber)inside.Items[2]).Value;
            if (x1 - x0 > z1 - z0) // (in a wall along X)
            {
                double cx = (x0 + x1) / 2, fz = iz > z1 ? z1 : z0;
                k.Prop("Gothic Window Frame", cx, fz, G, iz > z1 ? 0 : 180);
                AddObst(cx - 1.12, G + 0.7, fz - 0.4, cx + 1.12, G + 3.6, fz + 0.4, Mine, "a window frame");
            }
            else
            {
                double cz = (z0 + z1) / 2, fx = ix > x1 ? x1 : x0;
                k.Prop("Gothic Window Frame", fx, cz, G, ix > x1 ? 90 : -90);
                AddObst(fx - 0.4, G + 0.7, cz - 1.12, fx + 0.4, G + 3.6, cz + 1.12, Mine, "a window frame");
            }
        }
    }

    // ==== The floors =====================================================================================================

    void FloorDetail()
    {
        // The processional way: a chequered band from the west doors up the nave, across the crossing and up the choir.
        foreach (var (x0, x1) in new[] { (-32.0, 2.8), (4.6, 15.0), (16.4, 32.6) })
            for (var x = x0 + 1.5; x <= x1 - 1.5 + 0.01; x += 3.0)
                k.Prop("Chequered Floor Band", x, -6.0, G);
        // Ledger stones in the aisles, the crossing, the choir, the Lady Chapel and the crypt, where nothing stands.
        var rng = new Rng(29);
        foreach (var (x0, z0, x1, z1, y) in new[] { (-32.0, -14.6, 2.0, -12.0, G), (-32.0, 0.6, 2.0, 2.6, G), (5.0, -14.0, 15.0, -9.0, G), (5.0, -3.0, 15.0, 2.0, G),
                     (18.0, -9.6, 31.0, -7.4, G), (18.0, -4.6, 31.0, -2.4, G), (39.0, -14.0, 48.5, 2.5, G), (17.0, -15.0, 37.0, 0.4, Crypt), (5.0, -14.5, 15.0, -4.0, Crypt) })
            for (var x = x0 + 0.8; x < x1 - 0.5; x += 2.1)
                for (var z = z0 + 1.1; z < z1 - 0.9; z += 3.0)
                {
                    if (rng.Next() < 0.4)
                        continue;
                    if (!Free(x - 0.55, y, z - 1.05, x + 0.55, y + 0.1, z + 1.05, Floor | Hole))
                        continue;
                    k.Prop("Ledger Stone", x, z, y, rng.Next() < 0.5 ? 0 : 180);
                }
    }
}
