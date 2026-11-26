namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Starting points: the Zero Day's hull, decks, rooms and fittings (from TorpedoFrigateParts: its stations, its two decks along the length and
/// its rooms' widths), section shapes a station can take, and bodies to add (pods, a dorsal block, a boom). A shape is a start, never a kit:
/// every point stays the player's to move.
/// </summary>
public static class DesignTemplates
{
    private static List<(double, double)> L(params (double, double)[] p) => [.. p];

    /// <summary>Section shapes, each a half profile keel first (resampled to a body's point count when applied).</summary>
    public static readonly (string Name, List<(double H, double Y)> P)[] Sections =
    [
        ("Zero Day beam", L((2.8, -1.6), (5.3, 0.3), (6.0, 3.3), (5.0, 6.3), (2.6, 7.4))),
        ("Zero Day stern", L((3.0, -1.3), (6.0, 0.1), (6.6, 3.3), (5.7, 6.5), (3.1, 7.6))),
        ("Raked bow", L((1.5, -0.5), (3.0, 0.45), (3.8, 2.95), (3.3, 5.5), (1.6, 6.2))),
        ("Bow tip", L((0.2, 1.4), (0.45, 1.8), (0.65, 2.7), (0.5, 3.4), (0.25, 3.7))),
        ("Box hauler", L((3.0, 0.0), (3.25, 0.3), (3.25, 3.3), (3.0, 3.6), (2.2, 3.7))),
        ("Flat lander", L((3.6, 0.0), (4.2, 0.6), (4.0, 2.4), (2.8, 3.3), (1.2, 3.6))),
        ("Round", L((0.9, -0.8), (2.2, 0.0), (2.8, 1.6), (2.2, 3.2), (0.9, 4.0))),
        ("Tall keel", L((0.6, -2.5), (3.0, -0.5), (4.0, 2.5), (3.4, 5.0), (1.4, 5.8))),
        ("Pod", L((0.6, -1.0), (1.2, -0.6), (1.4, 0.0), (1.2, 0.6), (0.6, 1.0))),
        ("Wedge", L((0.3, -1.0), (2.6, 0.0), (3.6, 1.6), (2.6, 3.0), (0.4, 4.0))),
        ("Diamond", L((0.15, -2.0), (1.6, -1.0), (3.2, 1.0), (1.6, 3.0), (0.15, 4.0))),
        ("Hexagon", L((1.6, -1.3), (3.0, 0.0), (3.0, 2.6), (1.6, 3.9), (0.4, 4.0))),
        ("Octagon", L((1.2, -1.4), (2.9, -0.7), (3.3, 0.9), (3.3, 2.1), (2.9, 3.7), (1.2, 4.4))),
        ("Flat top", L((2.5, -1.0), (4.0, 0.0), (4.2, 2.0), (4.2, 3.2), (3.8, 3.4))),
        ("Stepped", L((2.0, -0.5), (4.0, 0.0), (3.2, 1.5), (3.2, 3.0), (1.5, 3.5))),
        ("Wide belly", L((4.0, 0.0), (4.2, 0.5), (3.0, 2.5), (1.5, 3.8), (0.5, 4.0))),
        ("Armoured slab", L((3.5, -0.3), (4.0, 0.0), (4.0, 2.8), (3.5, 3.1), (0.8, 3.3))),
        ("Teardrop", L((0.2, -2.0), (1.8, -0.5), (2.6, 1.2), (2.2, 2.8), (1.0, 3.6))),
        ("Tunnel", L((1.5, 0.0), (2.5, 0.2), (2.8, 2.0), (2.4, 3.6), (1.2, 4.2))),
        ("Shuttle", L((2.0, -0.2), (2.6, 0.4), (2.7, 1.6), (2.0, 2.4), (0.8, 2.7))),
        ("Tower", L((1.8, -1.0), (2.2, 0.0), (2.2, 6.0), (1.8, 7.0), (0.6, 7.3))),
        ("Low wing", L((1.5, -0.6), (6.5, 0.0), (6.0, 0.6), (2.5, 1.8), (1.2, 2.6))),
        ("Barrel", L((1.0, -1.6), (2.4, -0.9), (2.8, 0.6), (2.4, 2.1), (1.0, 2.8))),
    ];

    /// <summary>A profile resampled to n points by distance along it (ends kept).</summary>
    public static List<(double H, double Y)> Resample(List<(double H, double Y)> p, int n)
    {
        if (p.Count == n)
            return [.. p];
        var cum = new double[p.Count];
        for (var i = 1; i < p.Count; i++)
            cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(p[i].H - p[i - 1].H, 2) + Math.Pow(p[i].Y - p[i - 1].Y, 2));
        var o = new List<(double, double)>();
        for (var k = 0; k < n; k++)
        {
            var want = cum[^1] * k / (n - 1);
            var i = 0;
            while (i < p.Count - 2 && cum[i + 1] < want)
                i++;
            var seg = cum[i + 1] - cum[i];
            var t = seg < 1e-9 ? 0 : (want - cum[i]) / seg;
            o.Add((Math.Round(p[i].H + (p[i + 1].H - p[i].H) * t, 3), Math.Round(p[i].Y + (p[i + 1].Y - p[i].Y) * t, 3)));
        }
        return o;
    }

    public static readonly string[] Bodies = ["Main hull", "Pod pair", "Dorsal block", "Boom"];

    public static HullBody Body(ShipDesign d, string name)
    {
        var h = new HullBody { Id = d.NewId(), Name = name };
        Station S(double z, string sec, double scale = 1) =>
            new() { Z = z, P = [.. Array.Find(Sections, s => s.Name == sec).P.Select(q => (q.H * scale, q.Y * scale))] };
        switch (name)
        {
            case "Pod pair":
                h.X = 7.5; h.Y = 3.3; h.Z = 30; h.Mirror = true;
                h.Stations = [S(0, "Pod", 0.5), S(2, "Pod"), S(9, "Pod"), S(10.5, "Pod", 0.7)];
                break;
            case "Dorsal block":
                h.Y = 6.0; h.Z = 26;
                h.Stations = [S(0, "Box hauler", 0.6), S(1.5, "Box hauler", 0.8), S(10, "Box hauler", 0.8), S(11, "Box hauler", 0.65)];
                break;
            case "Boom":
                h.Y = 2.5; h.Z = 46;
                h.Stations = [S(0, "Round", 0.35), S(14, "Round", 0.3)];
                break;
            default:
                h.Stations = [S(0, "Raked bow", 0.8), S(4, "Box hauler"), S(16, "Box hauler"), S(19, "Box hauler", 0.9)];
                break;
        }
        foreach (var s in h.Stations)
            s.P = Resample(s.P, 5);
        h.Tidy();
        return h;
    }

    /// <summary>
    /// A body made to a size (Karl, 2026-11-24: the second object a connection reaches): a section shape fitted to a width and a height, a
    /// length cut into segments, its end stations drawn in a little.
    /// </summary>
    public static HullBody Sized(ShipDesign d, string section, double w, double h, double len, int segments)
    {
        var sec = Array.Find(Sections, s => s.Name == section).P ?? Sections[0].P;
        double sw = sec.Max(q => q.H), y0 = sec.Min(q => q.Y), y1 = sec.Max(q => q.Y);
        List<(double H, double Y)> fit = [.. sec.Select(q => (Math.Round(q.H / sw * w / 2, 3), Math.Round((q.Y - y0) / Math.Max(1e-6, y1 - y0) * h, 3)))];
        segments = Math.Clamp(segments, 1, ShipDesign.MaxStations - 1);
        len = Math.Clamp(len, 0.5, 200);
        var body = new HullBody { Id = d.NewId(), Name = section };
        for (var i = 0; i <= segments; i++)
        {
            var end = i == 0 || i == segments;
            body.Stations.Add(new Station { Z = Math.Round(len * i / segments, 3), P = end && segments > 1 ? Scale(fit, 0.7) : [.. fit] });
        }
        body.Tidy();
        return body;
    }

    private static List<(double H, double Y)> Scale(List<(double, double)> p, double f)
    {
        var mid = (p.Min(q => q.Item2) + p.Max(q => q.Item2)) / 2;
        return [.. p.Select(q => (q.Item1 * f, mid + (q.Item2 - mid) * f))];
    }

    public static readonly string[] Starts = ["Zero Day", "Blank hull"];

    public static ShipDesign Start(string name)
    {
        var d = new ShipDesign();
        if (name != "Zero Day")
        {
            d.Name = "New ship";
            d.Hulls.Add(Body(d, "Main hull"));
            d.Decks.Add(new Deck { Floor = 0.25, Height = 2.75 });
            return d;
        }
        d.Name = "Zero Day (copy)";
        var mid = L((2.8, -1.6), (5.3, 0.3), (6.0, 3.3), (5.0, 6.3), (2.6, 7.4));
        var hull = new HullBody { Id = d.NewId(), Name = "Zero Day hull", Skin = "#c9cdd2" };
        void St(double z, List<(double, double)> p) => hull.Stations.Add(new Station { Z = z, P = p });
        St(-3.5, L((0.2, 1.4), (0.45, 1.8), (0.65, 2.7), (0.5, 3.4), (0.25, 3.7)));
        St(2.0, L((1.5, -0.5), (3.0, 0.45), (3.8, 2.95), (3.3, 5.5), (1.6, 6.2)));
        St(8.0, L((2.3, -1.1), (4.4, 0.3), (5.2, 3.1), (4.4, 6.2), (2.4, 7.1)));
        St(14, [.. mid]); St(20, [.. mid]); St(30, [.. mid]); St(38, [.. mid]);
        St(42, L((3.0, -1.7), (5.6, 0.2), (6.3, 3.3), (5.3, 6.4), (2.8, 7.5)));
        St(44, L((3.0, -1.3), (6.0, 0.1), (6.6, 3.3), (5.7, 6.5), (3.1, 7.6)));
        St(46.5, L((2.6, -0.5), (5.6, 0.5), (6.4, 3.3), (5.5, 6.3), (3.0, 7.3)));
        d.Hulls.Add(hull);

        // The two decks along the length (floors 0.25 and 3.5, 3 m clear).
        d.Decks.Add(new Deck { Floor = 0.25, Height = 3.0 });
        d.Decks.Add(new Deck { Floor = 3.5, Height = 3.0 });

        void R(int deck, double x0, double x1, double z0, double z1, string kind, string label = "")
        { var r = new Room { Id = d.NewId(), Deck = deck, X0 = x0, X1 = x1, Z0 = z0, Z1 = z1, Kind = kind, Label = label }; r.KindDefaults(); d.Rooms.Add(r); }
        R(0, -1, 1, 8.5, 13, "Corridor");
        R(0, -3.5, -1, 9, 13, "Airlock room");
        R(0, 1, 3.5, 9, 13, "Armoury");
        R(0, -1, 1, 13, 15, "Shaft", "Ladder");
        R(0, -1, 1, 15, 33, "Corridor");
        R(0, -4.5, -1, 15, 27, "Hold", "Port hold");
        R(0, 1, 4.5, 15, 27, "Armoury", "Torpedo room");
        R(0, -4.5, -1, 27, 33, "Workshop");
        R(0, 1, 4.5, 27, 33, "Med bay");
        R(0, -4.5, 4.5, 33, 42, "Engineering");
        R(1, -2.5, 2.5, 9, 13, "Bridge");
        R(1, -1, 1, 13, 36, "Corridor");
        R(1, -3.5, -1, 15, 21, "Cabin");
        R(1, -3.5, -1, 21, 27, "Cabin");
        R(1, 1, 3.5, 15, 21, "Cabin");
        R(1, 1, 3.5, 21, 30, "Mess");
        R(1, -3.5, -1, 27, 36, "Cabin", "Captain");
        R(1, 1, 3.5, 30, 36, "Workshop", "Spares");
        R(1, -3.5, 3.5, 36, 42, "Reactor");

        void Aft(string kind, double u, double v) =>
            d.Pieces.Add(new Piece { Id = d.NewId(), Kind = kind, Hull = hull.Id, Surface = "aft", U = u, V = v });
        void At(string kind, double z, int side, double y, bool mirror = false, double turn = 0) =>
            d.Pieces.Add(new Piece { Id = d.NewId(), Kind = kind, Hull = hull.Id, Z = z, Side = side, T = hull.NearestT(side * Math.Max(0.01, hull.SkinHalf(z, y)), y, z), Mirror = mirror, Turn = turn });
        void Top(string kind, double z) => d.Pieces.Add(new Piece { Id = d.NewId(), Kind = kind, Hull = hull.Id, Z = z, T = 1 });
        void Keel(string kind, double z, double t = 0, bool mirror = false) =>
            d.Pieces.Add(new Piece { Id = d.NewId(), Kind = kind, Hull = hull.Id, Z = z, T = t, Mirror = mirror });

        Aft("Main drive", -3.6, 3.3); Aft("Main drive", 0, 3.3); Aft("Main drive", 3.6, 3.3);
        Aft("Small drive", -2.0, 0.6); Aft("Small drive", 2.0, 0.6);
        Top("Railgun", 22);
        Top("Sensor mast", 37);
        At("Torpedo sponson", 24, 1, 3.3, mirror: true);
        At("PDC turret", 11, 1, 6.0, mirror: true);
        At("PDC turret", 39, 1, -0.6, mirror: true);
        At("Radiator", 40, 1, 5.0, mirror: true);
        At("RCS quad", 4, 1, 4.5, mirror: true);
        At("RCS quad", 44.5, 1, 6.0, mirror: true);
        Keel("Landing leg", 18.2, 0.08, mirror: true);
        Keel("Landing leg", 35.4, 0.08, mirror: true);
        At("Airlock", 11, -1, 1.6);
        At("Cargo hatch", 21, -1, 1.5);
        Keel("Docking collar", 29);
        Keel("Boarding hatch", 14);
        PieceWeapons.Migrate(d); // its guns on mounts (Karl, 2026-11-26)
        return d;
    }
}
